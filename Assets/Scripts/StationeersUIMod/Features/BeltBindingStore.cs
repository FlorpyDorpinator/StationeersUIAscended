using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Assets.Scripts.Objects;
using HarmonyLib;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model ----------

    [XmlRoot("BeltBindings")]
    public class BeltBindingFile
    {
        [XmlElement("Belt")] public List<BeltBindingEntry> Belts = new List<BeltBindingEntry>();
    }

    public class BeltBindingEntry
    {
        [XmlAttribute("ref")] public long ReferenceId;   // Thing.ReferenceId of the belt (per-save)
        [XmlAttribute("seeded")] public bool Seeded;
        [XmlElement("Bind")] public List<BeltSlotBind> Binds = new List<BeltSlotBind>();
    }

    public class BeltSlotBind
    {
        [XmlAttribute("slot")] public int SlotIndex;     // Slot.SlotIndex on the belt
        [XmlAttribute("type")] public int TypeKey;       // Thing.PrefabHash (stable per-type id)
    }

    /// <summary>
    /// Per-belt "home slot" memory for the toolbelt radial (feature 1B.2/1B.3). Each worn
    /// tool-belt remembers which TOOL TYPE lives in which slot index, so the same tool always
    /// flies back to the same wedge and empty-but-bound slots can show a grey ghost label.
    ///
    /// The stable key for a tool is its <see cref="Thing.PrefabHash"/> (the prefab type id;
    /// verified in the 27701 decompile — <c>Thing.PrefabHash</c> / <c>GetPrefabHash()</c>).
    /// The belt itself is keyed by <see cref="Thing.ReferenceId"/>, which is save-scoped, so
    /// bindings are PER SAVE — mirroring <see cref="BagHotkeyStore"/>/<see cref="BagProfileStore"/>.
    /// Persisted to <c>BepInEx/config/StationeersUIMod/BeltBindings/&lt;saveKey&gt;.xml</c>.
    ///
    /// MP-safe by construction: this store only OBSERVES networked slot state (the Harmony
    /// postfix on <see cref="Slot.Take"/> fires on both server and client after a move settles)
    /// and never mutates game state.
    /// </summary>
    public static class BeltBindingStore
    {
        private sealed class BeltTable
        {
            public bool Seeded;
            public readonly Dictionary<int, int> SlotToType = new Dictionary<int, int>(); // slotIndex -> prefabHash
        }

        // refId -> table
        private static readonly Dictionary<long, BeltTable> _belts = new Dictionary<long, BeltTable>();
        // prefabHash -> display label (a type's name is constant; cache to avoid per-frame alloc)
        private static readonly Dictionary<int, string> _labelByType = new Dictionary<int, string>();
        // scratch reused by RecordPlacement to vacate a moved tool's old home without per-call
        // alloc (cleared before every use — carries no state across calls or reloads).
        private static readonly List<int> _vacateScratch = new List<int>();
        private static string _loadedSaveKey;

        // Disk plumbing lives in SaveScopedXmlStore (same folder/filename/serializer as before).
        private const string StoreFolder = "BeltBindings";
        private const string StoreLabel = "Belt binding";

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _belts.Clear();
            var parsed = SaveScopedXmlStore.LoadPerSave<BeltBindingFile>(StoreFolder, key, StoreLabel);
            if (parsed == null || parsed.Belts == null) return;
            foreach (var belt in parsed.Belts)
            {
                if (belt == null) continue;
                var table = new BeltTable { Seeded = belt.Seeded };
                if (belt.Binds != null)
                    foreach (var b in belt.Binds)
                        if (b != null && b.SlotIndex >= 0) table.SlotToType[b.SlotIndex] = b.TypeKey;
                _belts[belt.ReferenceId] = table;
            }
            UIALog.Info($"Loaded belt bindings for {_belts.Count} belt(s), save '{key}'.");
        }

        private static void Save()
        {
            var file = new BeltBindingFile();
            foreach (var kv in _belts)
            {
                var entry = new BeltBindingEntry { ReferenceId = kv.Key, Seeded = kv.Value.Seeded };
                foreach (var sb in kv.Value.SlotToType)
                    entry.Binds.Add(new BeltSlotBind { SlotIndex = sb.Key, TypeKey = sb.Value });
                file.Belts.Add(entry);
            }
            SaveScopedXmlStore.SavePerSave(StoreFolder, _loadedSaveKey, file, StoreLabel);
        }

        // --- public API ---

        /// <summary>If this belt has no table yet, create one from its current slot occupants
        /// (slotIndex -> occupant PrefabHash) and mark it seeded. Fail-soft on any null.</summary>
        public static void SeedIfNew(DynamicThing belt)
        {
            if (belt == null) return;
            EnsureSaveLoaded();
            long id = belt.ReferenceId;
            BeltTable table;
            if (_belts.TryGetValue(id, out table) && table.Seeded) return;
            if (table == null)
            {
                table = new BeltTable();
                _belts[id] = table;
            }
            try
            {
                var slots = belt.Slots;
                if (slots != null)
                {
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var slot = slots[i];
                        if (slot == null) continue;
                        var occ = slot.Get();
                        if (occ == null) continue;
                        table.SlotToType[slot.SlotIndex] = occ.PrefabHash; // seed only occupied slots
                    }
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Belt seed failed: " + e.Message);
            }
            table.Seeded = true;
            Save();
        }

        /// <summary>The slot index on this belt bound to the given tool's TYPE, or -1.</summary>
        public static int HomeSlotFor(DynamicThing belt, DynamicThing tool)
        {
            if (belt == null || tool == null) return -1;
            EnsureSaveLoaded();
            BeltTable table;
            if (!_belts.TryGetValue(belt.ReferenceId, out table)) return -1;
            int type = tool.PrefabHash;
            foreach (var kv in table.SlotToType)
                if (kv.Value == type) return kv.Key;
            return -1;
        }

        /// <summary>Short display name of the tool-type bound to that slot (for the grey ghost
        /// label), or null. Label is cached per type id — no per-frame alloc.</summary>
        public static string BoundLabelFor(DynamicThing belt, int slotIndex)
        {
            if (belt == null) return null;
            EnsureSaveLoaded();
            BeltTable table;
            if (!_belts.TryGetValue(belt.ReferenceId, out table)) return null;
            int type;
            if (!table.SlotToType.TryGetValue(slotIndex, out type)) return null;
            return LabelForType(type);
        }

        /// <summary>Bind the tool's TYPE -> slotIndex on this belt (a rebind), and persist.
        /// No-ops (and skips the disk write) when the slot already maps to that same type.</summary>
        public static void RecordPlacement(DynamicThing belt, int slotIndex, DynamicThing tool)
        {
            if (belt == null || tool == null || slotIndex < 0) return;
            EnsureSaveLoaded();
            long id = belt.ReferenceId;
            BeltTable table;
            if (!_belts.TryGetValue(id, out table))
            {
                table = new BeltTable();
                _belts[id] = table;
            }
            int type = tool.PrefabHash;
            int existing;
            if (table.SlotToType.TryGetValue(slotIndex, out existing) && existing == type)
                return; // already bound to this type — no churn
            // One home per tool TYPE (#9): if this type was bound to any OTHER slot on this belt,
            // vacate that old slot first, so a tool that moves to a new wedge stops ghosting on the
            // wedge it left (otherwise the frozen binding kept the grey label on the empty old slot).
            _vacateScratch.Clear();
            foreach (var kv in table.SlotToType)
                if (kv.Key != slotIndex && kv.Value == type) _vacateScratch.Add(kv.Key);
            for (int i = 0; i < _vacateScratch.Count; i++) table.SlotToType.Remove(_vacateScratch[i]);
            table.SlotToType[slotIndex] = type;
            Save();
        }

        // ---- explicit-drag rebind intent (FlorpyDorp's rule, 2026-08-06) ----------------------
        // Wedge bindings may ONLY move on an explicit MOUSE DRAG (dragging tools over each other
        // in the radial, or dragging one out of a bag onto a wedge). Every other way a tool lands
        // in a belt slot — the radial's swap-into-hand displacing the held tool, a keyboard stow,
        // SmartStow — is a MECHANICAL placement that must never steal a binding (the reported bug:
        // swapping a tool out of a bound wedge rebound the wedge to the incoming hand tool and
        // left the taken tool bound nowhere). The drag dispatch sites (ItemActions.DragTo /
        // SwapIntoSlot / WorldDragTo) register a one-shot intent here just before sending the
        // authoritative message; the Slot.Take postfix below only performs a full REBIND when the
        // landing matches a pending intent. The window is generous (3s unscaled) because on an MP
        // client Slot.Take fires when the SERVER ECHO lands, not at dispatch.
        // TWO intent entries, not one: a wedge-over-wedge DRAG is a swap, and BOTH landings are
        // explicit (the dragged tool into the target wedge AND the displaced tool into the source
        // wedge) — each rebinds its side. The swap dispatch sites note both.
        private struct DragIntent { public long BeltId; public int SlotIndex; public long ToolId; public float Until; }
        private static DragIntent _intentA = new DragIntent { SlotIndex = -1 };
        private static DragIntent _intentB = new DragIntent { SlotIndex = -1 };
        private static bool _intentNextIsB;

        /// <summary>Called by the DRAG dispatch paths just before they send a move/swap that lands
        /// <paramref name="tool"/> in <paramref name="dest"/>. No-op unless dest is a slot on the
        /// LOCAL player's worn tool-belt. One-shot per entry: consumed by the matching landing.</summary>
        public static void NoteExplicitPlacement(Slot dest, DynamicThing tool)
        {
            try
            {
                if (dest == null || tool == null) return;
                var belt = dest.Parent as DynamicThing;
                if (belt == null) return;
                var human = Guards.LocalHuman;
                if (human == null || human.ToolbeltSlot == null || human.ToolbeltSlot.Get() != belt) return;
                var e = new DragIntent
                {
                    BeltId = belt.ReferenceId,
                    SlotIndex = dest.SlotIndex,
                    ToolId = tool.ReferenceId,
                    Until = UnityEngine.Time.unscaledTime + 3f,
                };
                if (_intentNextIsB) _intentB = e; else _intentA = e;
                _intentNextIsB = !_intentNextIsB;
            }
            catch { }
        }

        private static bool ConsumeIntent(long beltId, int slotIndex, DynamicThing tool)
        {
            if (tool == null) return false;
            long toolId = tool.ReferenceId;
            float now = UnityEngine.Time.unscaledTime;
            if (_intentA.SlotIndex >= 0 && now <= _intentA.Until && beltId == _intentA.BeltId
                && slotIndex == _intentA.SlotIndex && toolId == _intentA.ToolId)
            { _intentA.SlotIndex = -1; return true; }
            if (_intentB.SlotIndex >= 0 && now <= _intentB.Until && beltId == _intentB.BeltId
                && slotIndex == _intentB.SlotIndex && toolId == _intentB.ToolId)
            { _intentB.SlotIndex = -1; return true; }
            return false;
        }

        /// <summary>A tool LANDED in a worn-belt slot (the Slot.Take postfix). Explicit-drag
        /// landings (a pending intent matches) get the full rebind; everything else is a
        /// mechanical placement and may only SEED — bind when the slot is unbound AND the tool's
        /// type has no home elsewhere on this belt (a brand-new tool acquiring its first wedge).
        /// It must never overwrite another type's slot binding nor vacate this type's existing
        /// home: the occupant is just visiting, and its own wedge is waiting for it.</summary>
        private static void RecordObserved(DynamicThing belt, int slotIndex, DynamicThing tool)
        {
            if (belt == null || tool == null || slotIndex < 0) return;
            EnsureSaveLoaded();
            if (ConsumeIntent(belt.ReferenceId, slotIndex, tool))
            {
                RecordPlacement(belt, slotIndex, tool);   // explicit drag: full rebind semantics
                return;
            }
            BeltTable table;
            if (!_belts.TryGetValue(belt.ReferenceId, out table))
            {
                table = new BeltTable();
                _belts[belt.ReferenceId] = table;
            }
            int type = tool.PrefabHash;
            if (table.SlotToType.ContainsKey(slotIndex)) return;      // slot has an owner (this type or another): leave it
            foreach (var kv in table.SlotToType)
                if (kv.Value == type) return;                          // type already has a home elsewhere: visiting
            table.SlotToType[slotIndex] = type;                        // first home for a new tool type
            Save();
        }

        /// <summary>Resolve a prefab-type id to its display name, cached (constant per type).</summary>
        private static string LabelForType(int type)
        {
            string label;
            if (_labelByType.TryGetValue(type, out label)) return label;
            label = null;
            try
            {
                Thing prefab = Prefab.Find(type);
                if (prefab != null) label = prefab.DisplayName;
            }
            catch { }
            _labelByType[type] = label; // cache even null so we don't retry a missing prefab each frame
            return label;
        }

        /// <summary>Hot-reload / mod-off: forget the in-memory bindings (disk file untouched).</summary>
        public static void Reset()
        {
            _belts.Clear();
            _labelByType.Clear();
            _vacateScratch.Clear();
            _loadedSaveKey = null;
            _intentA = new DragIntent { SlotIndex = -1 };   // never carry a drag intent across a reload/world change
            _intentB = new DragIntent { SlotIndex = -1 };
            _intentNextIsB = false;
        }

        // --- manual-drag rebind: observe the networked occupant-set funnel ---

        /// <summary>Fail-soft Harmony postfix on <see cref="Slot.Take"/> — the single point where
        /// a Thing lands in a slot (both the server move and the client's networked apply flow
        /// through <c>DynamicThing.MoveToSlot</c>/<c>DragInSlot</c> → <c>Slot.Take</c>, verified in
        /// the 27701 decompile). If the slot belongs to the LOCAL player's worn tool-belt, route
        /// through <see cref="RecordObserved"/>: a full rebind ONLY when a drag intent is pending
        /// (see NoteExplicitPlacement), seed-only otherwise. Recording every landing as a rebind
        /// was the 2026-08-06 bug — the radial's swap-into-hand displaced the held tool into the
        /// taken tool's wedge and stole its binding. Observation only — never mutates game state.</summary>
        [HarmonyPatch(typeof(Slot), "Take")]
        public static class SlotTakePatch
        {
            [HarmonyPostfix]
            public static void Postfix(Slot __instance, DynamicThing child)
            {
                try
                {
                    if (child == null || __instance == null) return;
                    var belt = __instance.Parent as DynamicThing;
                    if (belt == null) return;
                    var human = Guards.LocalHuman;
                    if (human == null || human.ToolbeltSlot == null) return;
                    // Only the belt currently worn by the local player.
                    if (human.ToolbeltSlot.Get() != belt) return;
                    RecordObserved(belt, __instance.SlotIndex, child);
                }
                catch { /* fail-soft: never let observation break a slot move */ }
            }
        }
    }
}
