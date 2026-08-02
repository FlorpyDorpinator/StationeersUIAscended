using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;

namespace StationeersUIMod.Core
{
    /// <summary>A slot discovered somewhere in the player's accessible inventory graph.</summary>
    public sealed class ScannedSlot
    {
        public Slot Slot;
        public Thing Holder;          // the thing that owns the slot (human => worn root)
        public string Location;       // human-readable: "Toolbelt", "inside Mining Drill", ...
        public int Depth;
        public bool InsideTool;       // slot's holder is a tool-ish item rather than a bag/human
        public DynamicThing Expected; // occupant at menu-build time; actions verify it still matches

        public DynamicThing Occupant => Slot?.Get();

        /// <summary>Snapshot the current occupant so a later click can't act on a different item.</summary>
        public ScannedSlot Pin()
        {
            Expected = Slot?.Get();
            return this;
        }
    }

    /// <summary>
    /// Recursive, read-only walk of everything the local player can physically reach:
    /// worn slots, hands, then containers/tools nested below them (depth-capped, locked
    /// slots skipped). Shared by the slot-swap finder, SmartStow+ and the bag radial.
    /// </summary>
    public static class InventoryScanner
    {
        public static List<ScannedSlot> Scan(int maxDepth, bool includeToolSlots)
        {
            var result = new List<ScannedSlot>(64);
            Human human = InventoryManager.ParentHuman;
            if (human == null) return result;

            var visited = new HashSet<Thing> { human };
            foreach (Slot slot in human.Slots)
            {
                if (slot == null) continue;
                AddSlot(result, slot, human, WornLocationName(human, slot), 0, false);
                DynamicThing occ = slot.Get();
                if (occ != null)
                    Walk(result, visited, occ, LocationNameFor(human, slot, occ), 1, maxDepth, includeToolSlots);
            }
            return result;
        }

        private static void Walk(List<ScannedSlot> result, HashSet<Thing> visited, Thing holder,
            string holderLocation, int depth, int maxDepth, bool includeToolSlots)
        {
            if (depth > maxDepth || holder == null || !visited.Add(holder)) return;
            if (holder.Slots == null) return;

            bool holderIsTool = holder is Tool || holder is PowerTool;
            foreach (Slot slot in holder.Slots)
            {
                if (slot == null || slot.IsLocked) continue;
                if (holderIsTool && !includeToolSlots) continue;
                AddSlot(result, slot, holder, holderLocation, depth, holderIsTool);
                DynamicThing occ = slot.Get();
                if (occ != null)
                    Walk(result, visited, occ, "inside " + occ.DisplayName, depth + 1, maxDepth, includeToolSlots);
            }
        }

        private static void AddSlot(List<ScannedSlot> result, Slot slot, Thing holder,
            string location, int depth, bool insideTool)
        {
            result.Add(new ScannedSlot
            {
                Slot = slot,
                Holder = holder,
                Location = location,
                Depth = depth,
                InsideTool = insideTool,
            });
        }

        // ---- pooled scan (StowRouter's EnsureScan) -------------------------------------
        //
        // The router resolves per G press AND at ~4 Hz for the whole duration of a ghost-hint
        // drag; a fresh List + HashSet + one ScannedSlot per slot (+ unread Location concats)
        // per resolve was the mod's largest steady allocation source during a drag. These
        // buffers are reused per call (Clear per use) and the ScannedSlot instances recycle
        // through a grow-only pool. The returned list is valid ONLY until the next ScanPooled
        // call — router-internal use, never handed to anything that holds it. Location stays
        // null (no router stage reads it). ResetPool() drops every reference on mod shutdown
        // (hot-reload rule: pooled Slot/Thing refs must not survive an F6).

        private static readonly List<ScannedSlot> _pooledResult = new List<ScannedSlot>(64);
        private static readonly List<ScannedSlot> _slotPool = new List<ScannedSlot>(64);
        private static readonly HashSet<Thing> _pooledVisited = new HashSet<Thing>();
        private static int _poolUsed;

        /// <summary>Allocation-free variant of <see cref="Scan"/> for resolve-frequency callers.
        /// Same slot enumeration and depth/lock semantics; Location is not composed. The result
        /// is POOLED: read it within the same resolve, never cache or return it.</summary>
        public static List<ScannedSlot> ScanPooled(int maxDepth, bool includeToolSlots)
        {
            _pooledResult.Clear();
            _pooledVisited.Clear();
            int prevUsed = _poolUsed;
            _poolUsed = 0;

            Human human = InventoryManager.ParentHuman;
            if (human != null)
            {
                _pooledVisited.Add(human);
                foreach (Slot slot in human.Slots)
                {
                    if (slot == null) continue;
                    AddPooled(slot, human, 0, false);
                    DynamicThing occ = slot.Get();
                    if (occ != null) WalkPooled(occ, 1, maxDepth, includeToolSlots);
                }
            }

            // Pool entries used by the PREVIOUS scan but not this one would otherwise pin
            // stale Slot/Thing refs between resolves; clear just that tail (entries beyond
            // it are already clean by induction).
            for (int i = _poolUsed; i < prevUsed && i < _slotPool.Count; i++)
            {
                ScannedSlot s = _slotPool[i];
                s.Slot = null; s.Holder = null; s.Location = null; s.Expected = null;
                s.Depth = 0; s.InsideTool = false;
            }
            _pooledVisited.Clear(); // don't hold Thing refs between resolves
            return _pooledResult;
        }

        private static void WalkPooled(Thing holder, int depth, int maxDepth, bool includeToolSlots)
        {
            if (depth > maxDepth || holder == null || !_pooledVisited.Add(holder)) return;
            if (holder.Slots == null) return;

            bool holderIsTool = holder is Tool || holder is PowerTool;
            foreach (Slot slot in holder.Slots)
            {
                if (slot == null || slot.IsLocked) continue;
                if (holderIsTool && !includeToolSlots) continue;
                AddPooled(slot, holder, depth, holderIsTool);
                DynamicThing occ = slot.Get();
                if (occ != null) WalkPooled(occ, depth + 1, maxDepth, includeToolSlots);
            }
        }

        private static void AddPooled(Slot slot, Thing holder, int depth, bool insideTool)
        {
            ScannedSlot s;
            if (_poolUsed < _slotPool.Count) s = _slotPool[_poolUsed];
            else { s = new ScannedSlot(); _slotPool.Add(s); }
            _poolUsed++;
            s.Slot = slot;
            s.Holder = holder;
            s.Location = null;
            s.Depth = depth;
            s.InsideTool = insideTool;
            s.Expected = null;
            _pooledResult.Add(s);
        }

        /// <summary>Hot-reload teardown (via <c>StowRouter.Reset</c>): drop every pooled
        /// Slot/Thing reference so a reload or world change strands nothing.</summary>
        public static void ResetPool()
        {
            _pooledResult.Clear();
            _slotPool.Clear();
            _pooledVisited.Clear();
            _poolUsed = 0;
        }

        private static string WornLocationName(Human human, Slot slot)
        {
            if (slot == human.LeftHandSlot) return "Left Hand";
            if (slot == human.RightHandSlot) return "Right Hand";
            string name = slot.DisplayName;
            return string.IsNullOrEmpty(name) ? "Worn" : name;
        }

        private static string LocationNameFor(Human human, Slot slot, DynamicThing occupant)
        {
            if (slot == human.ToolbeltSlot) return "Toolbelt";
            if (slot == human.BackpackSlot) return "Backpack";
            if (slot == human.SuitSlot) return "Suit";
            if (slot == human.UniformSlot) return "Uniform";
            if (slot == human.LeftHandSlot) return "Left Hand";
            if (slot == human.RightHandSlot) return "Right Hand";
            return occupant.DisplayName;
        }

        /// <summary>
        /// Items compatible with <paramref name="targetSlot"/> found anywhere accessible.
        /// Excludes the target's own occupant. Compatibility mirrors Slot.IsAllowedType:
        /// class match (or None) plus the specific-prefab restriction when present.
        /// </summary>
        public static List<ScannedSlot> FindCompatible(Slot targetSlot, int maxDepth, bool includeToolSlots)
        {
            var found = new List<ScannedSlot>();
            if (targetSlot == null) return found;
            foreach (var scanned in Scan(maxDepth, includeToolSlots))
            {
                DynamicThing occ = scanned.Occupant;
                if (occ == null || scanned.Slot == targetSlot) continue;
                if (!IsTypeCompatible(targetSlot, occ)) continue;
                found.Add(scanned.Pin());
            }
            return found;
        }

        public static bool IsTypeCompatible(Slot targetSlot, DynamicThing thing)
        {
            if (thing == null || targetSlot == null) return false;
            if (targetSlot.Type != Slot.Class.None && thing.SlotType != targetSlot.Type) return false;
            if (!SlotAcceptsPrefab(targetSlot, thing.PrefabHash)) return false;
            return true;
        }

        /// <summary>The game update of 2026-07-28 replaced Slot.SpecificTypePrefabHash
        /// (int, -1 = unrestricted) with Slot.SpecificTypePrefabHashes (int[]): a slot may
        /// now be locked to SEVERAL specific prefabs. Null or empty = unrestricted (the old
        /// -1). Verified by reflection against the live Assembly-CSharp.dll.</summary>
        public static bool SlotIsPrefabRestricted(Slot slot)
        {
            var hashes = slot != null ? slot.SpecificTypePrefabHashes : null;
            return hashes != null && hashes.Length > 0;
        }

        /// <summary>True when the slot's specific-prefab lock (if any) admits this prefab.</summary>
        public static bool SlotAcceptsPrefab(Slot slot, int prefabHash)
        {
            var hashes = slot != null ? slot.SpecificTypePrefabHashes : null;
            if (hashes == null || hashes.Length == 0) return true;
            for (int i = 0; i < hashes.Length; i++)
                if (hashes[i] == prefabHash) return true;
            return false;
        }

        /// <summary>Consequence text when pulling <paramref name="source"/>'s occupant out (proposal §7.3).</summary>
        public static string ConsequenceOfRemoving(ScannedSlot source)
        {
            if (source?.Holder == null) return null;
            if (source.Holder is IBatteryPowered powered && source.Slot == powered.BatterySlot)
            {
                bool on = false;
                try { on = source.Holder.OnOff; } catch { }
                return source.Holder.DisplayName + (on ? " will turn off" : " will be unpowered");
            }
            if (source.Holder is Assets.Scripts.Objects.Clothing.SuitBase suit && source.Slot == suit.BatterySlot)
                return "Suit will lose power";
            return null;
        }
    }
}

