using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Assets.Scripts.Objects;
using BepInEx;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model ----------

    [XmlRoot("BagHotkeys")]
    public class BagHotkeyFile
    {
        [XmlElement("Bind")] public List<BagHotkeyBind> Binds = new List<BagHotkeyBind>();
    }

    public class BagHotkeyBind
    {
        [XmlAttribute("slot")] public int Slot;        // 0-9  (shown to the player as 1-0)
        [XmlAttribute("bagRef")] public long BagReferenceId;
    }

    /// <summary>
    /// Ctrl+1..0 quick-open bindings for storage bags (#3). A binding maps a number slot to a
    /// bag's persistent <see cref="Thing.ReferenceId"/>; pressing Ctrl+&lt;number&gt; opens that
    /// bag's radial wherever the bag currently lives in your inventory. Bindings are PER SAVE —
    /// ReferenceIds are save-scoped, so a new map starts empty — mirroring the per-save assignment
    /// storage in <see cref="BagProfileStore"/>. Persisted to
    /// <c>BepInEx/config/StationeersUIMod/Hotkeys/&lt;saveKey&gt;.xml</c>.
    ///
    /// Slots are indexed 0-9 internally; the number ROW is 1,2,…,9,0, so display slot i shows the
    /// digit <c>(i + 1) % 10</c>. A binding survives the holding container being taken off and put
    /// back on (the bag keeps its RefId); it simply does nothing while the bag is out of reach.
    /// </summary>
    public static class BagHotkeyStore
    {
        public const int SlotCount = 10;

        private static readonly Dictionary<int, long> _binds = new Dictionary<int, long>();
        private static string _loadedSaveKey;

        private static string HotkeysDir => Path.Combine(BagProfileStore.ConfigDir, "Hotkeys");

        // --- number-row <-> slot-index mapping ---

        /// <summary>The KeyCode for the top-row digit at slot i (0..9): slot 0→Alpha1 … slot 8→Alpha9, slot 9→Alpha0.</summary>
        public static UnityEngine.KeyCode KeyForSlot(int i)
            => i == 9 ? UnityEngine.KeyCode.Alpha0 : (UnityEngine.KeyCode)((int)UnityEngine.KeyCode.Alpha1 + i);

        /// <summary>Slot index (0..9) for a pressed top-row digit key, or -1.</summary>
        public static int SlotForKey(UnityEngine.KeyCode k)
        {
            if (k == UnityEngine.KeyCode.Alpha0) return 9;
            if (k >= UnityEngine.KeyCode.Alpha1 && k <= UnityEngine.KeyCode.Alpha9)
                return (int)k - (int)UnityEngine.KeyCode.Alpha1;
            return -1;
        }

        /// <summary>The digit shown to the player for slot i (1..9,0).</summary>
        public static int DisplayDigit(int i) => (i + 1) % 10;

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _binds.Clear();
            try
            {
                var path = Path.Combine(HotkeysDir, key + ".xml");
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(BagHotkeyFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (BagHotkeyFile)serializer.Deserialize(stream);
                    if (parsed?.Binds != null)
                        foreach (var b in parsed.Binds)
                            if (b.Slot >= 0 && b.Slot < SlotCount) _binds[b.Slot] = b.BagReferenceId;
                }
                UIALog.Info($"Loaded {_binds.Count} bag hotkey(s) for save '{key}'.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Bag hotkey load failed: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(HotkeysDir);
                var path = Path.Combine(HotkeysDir, (_loadedSaveKey ?? BagProfileStore.CurrentSaveKey()) + ".xml");
                var file = new BagHotkeyFile
                {
                    Binds = _binds.Select(kv => new BagHotkeyBind { Slot = kv.Key, BagReferenceId = kv.Value }).ToList(),
                };
                var serializer = new XmlSerializer(typeof(BagHotkeyFile));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("Bag hotkey save failed: " + e.Message);
            }
        }

        // --- public API ---

        /// <summary>Bind slot i (0..9) to a bag. Re-binding a slot replaces it; binding a bag that
        /// already occupies another slot MOVES it (one bag = at most one hotkey). Binding a slot to
        /// the bag already there toggles it OFF.</summary>
        public static void Bind(int slot, Thing bag)
        {
            if (bag == null || slot < 0 || slot >= SlotCount) return;
            EnsureSaveLoaded();
            long id = bag.ReferenceId;
            // Toggle off if this exact slot already points at this bag.
            if (_binds.TryGetValue(slot, out var cur) && cur == id) { _binds.Remove(slot); Save(); return; }
            // One bag = one hotkey: drop any other slot pointing at the same bag.
            foreach (var s in _binds.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList())
                _binds.Remove(s);
            _binds[slot] = id;
            Save();
        }

        public static void Unbind(int slot)
        {
            EnsureSaveLoaded();
            if (_binds.Remove(slot)) Save();
        }

        /// <summary>The bag ReferenceId bound to slot i, or null.</summary>
        public static long? RefForSlot(int slot)
        {
            EnsureSaveLoaded();
            return _binds.TryGetValue(slot, out var id) ? id : (long?)null;
        }

        /// <summary>The display digit (1..0) bound to this bag, or -1 — for the wedge badge.</summary>
        public static int DigitForBag(Thing bag)
        {
            if (bag == null) return -1;
            EnsureSaveLoaded();
            long id = bag.ReferenceId;
            foreach (var kv in _binds)
                if (kv.Value == id) return DisplayDigit(kv.Key);
            return -1;
        }

        /// <summary>Resolve the bag currently bound to slot i, if it still exists and is reachable
        /// in the local player's inventory tree (so a bag left in a dropped container just no-ops).
        /// Uses Thing.Find, which is O(1) over the reference table.</summary>
        public static DynamicThing ResolveBag(int slot)
        {
            long? id = RefForSlot(slot);
            if (id == null) return null;
            Thing found;
            try { if (!Thing.TryFind(id.Value, out found) || found == null) return null; }
            catch { return null; }
            var bag = found as DynamicThing;
            return bag != null && IsCarriedByLocalPlayer(bag) ? bag : null;
        }

        /// <summary>Bag must be within the local player's carried inventory tree to open by hotkey —
        /// never reach into a container sitting on the floor or in someone else's hands.</summary>
        private static bool IsCarriedByLocalPlayer(DynamicThing bag)
        {
            var human = Assets.Scripts.Inventory.InventoryManager.ParentHuman;
            if (human == null || bag == null) return false;
            Thing node = bag;
            int depth = 0;
            while (node != null && depth++ < 12)
            {
                if (node == human) return true;
                node = (node as DynamicThing)?.ParentSlot?.Parent;
            }
            return false;
        }

        /// <summary>Hot-reload / mod-off: forget the in-memory bindings (disk file is untouched).</summary>
        public static void Reset()
        {
            _binds.Clear();
            _loadedSaveKey = null;
        }
    }
}
