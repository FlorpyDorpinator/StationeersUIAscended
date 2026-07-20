using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static string _loadedSaveKey;

        private static string BeltBindingsDir => Path.Combine(BagProfileStore.ConfigDir, "BeltBindings");

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _belts.Clear();
            try
            {
                var path = Path.Combine(BeltBindingsDir, key + ".xml");
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(BeltBindingFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (BeltBindingFile)serializer.Deserialize(stream);
                    if (parsed != null && parsed.Belts != null)
                    {
                        foreach (var belt in parsed.Belts)
                        {
                            if (belt == null) continue;
                            var table = new BeltTable { Seeded = belt.Seeded };
                            if (belt.Binds != null)
                                foreach (var b in belt.Binds)
                                    if (b != null && b.SlotIndex >= 0) table.SlotToType[b.SlotIndex] = b.TypeKey;
                            _belts[belt.ReferenceId] = table;
                        }
                    }
                }
                UIALog.Info($"Loaded belt bindings for {_belts.Count} belt(s), save '{key}'.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Belt binding load failed: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(BeltBindingsDir);
                var path = Path.Combine(BeltBindingsDir, (_loadedSaveKey ?? BagProfileStore.CurrentSaveKey()) + ".xml");
                var file = new BeltBindingFile();
                foreach (var kv in _belts)
                {
                    var entry = new BeltBindingEntry { ReferenceId = kv.Key, Seeded = kv.Value.Seeded };
                    foreach (var sb in kv.Value.SlotToType)
                        entry.Binds.Add(new BeltSlotBind { SlotIndex = sb.Key, TypeKey = sb.Value });
                    file.Belts.Add(entry);
                }
                var serializer = new XmlSerializer(typeof(BeltBindingFile));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("Belt binding save failed: " + e.Message);
            }
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
            table.SlotToType[slotIndex] = type;
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
            _loadedSaveKey = null;
        }

        // --- manual-drag rebind: observe the networked occupant-set funnel ---

        /// <summary>Fail-soft Harmony postfix on <see cref="Slot.Take"/> — the single point where
        /// a Thing lands in a slot (both the server move and the client's networked apply flow
        /// through <c>DynamicThing.MoveToSlot</c>/<c>DragInSlot</c> → <c>Slot.Take</c>, verified in
        /// the 27701 decompile). If the slot belongs to the LOCAL player's worn tool-belt, record
        /// the placement so a manual drag rebinds the home slot. Observation only — never mutates.</summary>
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
                    RecordPlacement(belt, __instance.SlotIndex, child);
                }
                catch { /* fail-soft: never let observation break a slot move */ }
            }
        }
    }
}
