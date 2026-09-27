using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Tutorial;

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
    /// <c>BepInEx/config/StationeersUIMod/Hotkeys/&lt;worldKey&gt;.xml</c>, keyed via
    /// <see cref="SaveScopedXmlStore.ResolveSaveKey"/> (the per-world <c>World.CurrentId</c>, not the
    /// station name — SmartStow-Simple-Refactor-Plan §7.7). No key yet (main menu, or a legacy SP
    /// world before its first save) means memory-only — see <see cref="EnsureSaveLoaded"/>.
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

        // Disk plumbing lives in SaveScopedXmlStore (same folder/filename/serializer as before).
        private const string StoreFolder = "Hotkeys";
        private const string StoreLabel = "Bag hotkey";

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

        // --- per-save load/save (keyed off the per-world identity, plan §7.1/§7.7) ---

        /// <summary>(Re)load for the CURRENT world key. No key yet means: hold the in-memory table
        /// as-is, touch nothing on disk — never "unsaved.xml". A memory-only table that GAINS a key
        /// mid-session (this session's own bindings, made before any key existed) is carried into
        /// the newly-keyed file, mirroring <c>StowHomeStore.EnsureLoaded</c>.</summary>
        public static void EnsureSaveLoaded()
        {
            string key;
            if (!SaveScopedXmlStore.ResolveSaveKey(StoreFolder, _loadedSaveKey, out key)) { StandDown(); return; }
            if (key == _loadedSaveKey) return;

            Dictionary<int, long> carried = null;
            if (_loadedSaveKey == null && _binds.Count > 0)
                carried = new Dictionary<int, long>(_binds);

            _loadedSaveKey = key;
            _binds.Clear();
            LoadInto(key);

            // Fix-wave finding 5: an empty result under a brand-new key may mean adoption ran too
            // early (before the station name was set) and missed a legacy file that exists NOW.
            if (_binds.Count == 0 && SaveScopedXmlStore.RetryAdoptionIfEmpty(StoreFolder, key))
                LoadInto(key);

            if (carried != null)
            {
                foreach (var kv in carried) _binds[kv.Key] = kv.Value;   // this session's memory wins over disk
                Save();
            }
        }

        /// <summary>The actual per-save deserialize-and-populate step, split out so
        /// <see cref="EnsureSaveLoaded"/> can retry it once after finding 5's adoption retry.</summary>
        private static void LoadInto(string key)
        {
            var parsed = SaveScopedXmlStore.LoadPerSave<BagHotkeyFile>(StoreFolder, key, StoreLabel);
            if (parsed?.Binds == null) return;
            foreach (var b in parsed.Binds)
                if (b != null && b.Slot >= 0 && b.Slot < SlotCount) _binds[b.Slot] = b.BagReferenceId;
            UIALog.Info($"Loaded {_binds.Count} bag hotkey(s) for save '{key}'.");
        }

        /// <summary>Fix-wave finding 1: drop a dead world's table+key the moment no key resolves any
        /// more (world left, or between-worlds transition), so a stray mutation can never keep
        /// writing into the PREVIOUS world's file under a stale <see cref="_loadedSaveKey"/>. No-op
        /// once already standing down (cheap to call on every failed resolve).</summary>
        private static void StandDown()
        {
            if (_loadedSaveKey == null) return;
            _loadedSaveKey = null;
            _binds.Clear();
        }

        /// <summary>Write the table now. Memory-only (no world key resolved) NEVER writes — see
        /// <see cref="EnsureSaveLoaded"/>.</summary>
        private static void Save()
        {
            if (_loadedSaveKey == null) return;
            var file = new BagHotkeyFile
            {
                Binds = _binds.Select(kv => new BagHotkeyBind { Slot = kv.Key, BagReferenceId = kv.Value }).ToList(),
            };
            SaveScopedXmlStore.SavePerSave(StoreFolder, _loadedSaveKey, file, StoreLabel);
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
            // Tutorial hook (Build Contract s4): a NEW bind only — the toggle-off branch above
            // returns before here, so this never fires for an unbind.
            TutorialSignals.Raise(TSignal.BagBound);
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
