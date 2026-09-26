using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>D-005 LISTING flag, computed ONCE at scan time by the one predicate
        /// (<see cref="ItemActions.IsSealedSlot"/>): this slot is sealed — a hidden (non-interactable)
        /// slot of a carried thing, or any slot of a stack — OR it lies anywhere BENEATH one (a bag the
        /// old phantom grid tucked into a coil, a tool still packed inside a kit: vanilla's UI can reach
        /// neither, so nothing inside them is offered either). Enumerating consumers (search, the
        /// recent-item wedge, <see cref="InventoryScanner.FindCompatible"/>, SmartStow's router) skip
        /// flagged entries, so a built-in part — an Emergency EVA suit's tanks/filters, an emergency
        /// tool's battery — is never offered as something to take, swap or stow into. The scan itself
        /// still RETURNS these entries (nothing is silently dropped); the grid's take-out-only rescue
        /// section runs its own walk (GridModel) and is unaffected. Execute-time funnels never trust
        /// this snapshot — they re-ask <see cref="ItemActions.IsSealedSlot"/> on the live slot. False on
        /// every hand-built ScannedSlot (a pinned action source).</summary>
        public bool Sealed;

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
    /// Sealed slots (and everything beneath them) are still walked and returned, FLAGGED
    /// (<see cref="ScannedSlot.Sealed"/>) for the enumerating consumers to skip.
    /// </summary>
    public static class InventoryScanner
    {
        /// <summary>Things whose CONTENTS the mod must never expose as storage (FlorpyDorp
        /// directive 2026-08-04): a body bag's three slots are the corpse's ORGANS
        /// (DynamicBodyBag.BrainSlot/LungsSlot/StomachSlot), and vanilla itself never opens a
        /// slot UI for the bag — only the mod's radial/grid/search could reach them. The ONE
        /// shared predicate for every surface (manage radial, bag radial, Universal Inventory
        /// recursion, this scanner), so no surface can disagree. A CLASS check, not a slot
        /// heuristic: organ slots are typed/prefab-locked exactly like ordinary component
        /// sockets, so no data-driven rule can tell them apart. The death CARDBOARD BOX (base
        /// CardboardBox) deliberately stays real storage. Vanilla interactions with the bag
        /// (cryotube revival, dragging the bag itself) are untouched — this only stops the
        /// mod's own UI from opening its contents.</summary>
        public static bool ContentsOffLimits(DynamicThing t)
            => t is Assets.Scripts.DynamicBodyBag;

        public static List<ScannedSlot> Scan(int maxDepth, bool includeToolSlots)
        {
            var result = new List<ScannedSlot>(64);
            Human human = InventoryManager.ParentHuman;
            if (human == null) return result;

            var visited = new HashSet<Thing> { human };
            foreach (Slot slot in human.Slots)
            {
                if (slot == null) continue;
                // The human's own slots are a creature's: never sealed (IsSealedSlot says so for any
                // Entity) — asked anyway so the flag has exactly one source.
                bool sealedHere = ItemActions.IsSealedSlot(slot);
                AddSlot(result, slot, human, WornLocationName(human, slot), 0, false, sealedHere);
                DynamicThing occ = slot.Get();
                if (occ != null)
                    Walk(result, visited, occ, LocationNameFor(human, slot, occ), 1, maxDepth, includeToolSlots, sealedHere);
            }
            return result;
        }

        /// <param name="underSealed">The slot holding <paramref name="holder"/> is sealed or lies beneath
        /// one: every slot found here inherits <see cref="ScannedSlot.Sealed"/>.</param>
        private static void Walk(List<ScannedSlot> result, HashSet<Thing> visited, Thing holder,
            string holderLocation, int depth, int maxDepth, bool includeToolSlots, bool underSealed)
        {
            if (depth > maxDepth || holder == null || !visited.Add(holder)) return;
            if (holder.Slots == null) return;
            // Never walk INTO an off-limits container (body bag): its slots must not surface in
            // search/stow/swap results. The bag ITSELF was already added as an occupant above.
            if (holder is DynamicThing dtH && ContentsOffLimits(dtH)) return;

            bool holderIsTool = holder is Tool || holder is PowerTool;
            foreach (Slot slot in holder.Slots)
            {
                if (slot == null || slot.IsLocked) continue;
                if (holderIsTool && !includeToolSlots) continue;
                bool sealedHere = underSealed || ItemActions.IsSealedSlot(slot);
                AddSlot(result, slot, holder, holderLocation, depth, holderIsTool, sealedHere);
                DynamicThing occ = slot.Get();
                if (occ != null)
                    Walk(result, visited, occ, "inside " + occ.DisplayName, depth + 1, maxDepth, includeToolSlots, sealedHere);
            }
        }

        private static void AddSlot(List<ScannedSlot> result, Slot slot, Thing holder,
            string location, int depth, bool insideTool, bool isSealed)
        {
            result.Add(new ScannedSlot
            {
                Slot = slot,
                Holder = holder,
                Location = location,
                Depth = depth,
                InsideTool = insideTool,
                Sealed = isSealed,
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
                    bool sealedHere = ItemActions.IsSealedSlot(slot);   // same single source as Scan
                    AddPooled(slot, human, 0, false, sealedHere);
                    DynamicThing occ = slot.Get();
                    if (occ != null) WalkPooled(occ, 1, maxDepth, includeToolSlots, sealedHere);
                }
            }

            // Pool entries used by the PREVIOUS scan but not this one would otherwise pin
            // stale Slot/Thing refs between resolves; clear just that tail (entries beyond
            // it are already clean by induction).
            for (int i = _poolUsed; i < prevUsed && i < _slotPool.Count; i++)
            {
                ScannedSlot s = _slotPool[i];
                s.Slot = null; s.Holder = null; s.Location = null; s.Expected = null;
                s.Depth = 0; s.InsideTool = false; s.Sealed = false;
            }
            _pooledVisited.Clear(); // don't hold Thing refs between resolves
            return _pooledResult;
        }

        private static void WalkPooled(Thing holder, int depth, int maxDepth, bool includeToolSlots, bool underSealed)
        {
            if (depth > maxDepth || holder == null || !_pooledVisited.Add(holder)) return;
            if (holder.Slots == null) return;
            // Same off-limits gate as Walk: SmartStow must never route INTO a body bag.
            if (holder is DynamicThing dtH && ContentsOffLimits(dtH)) return;

            bool holderIsTool = holder is Tool || holder is PowerTool;
            foreach (Slot slot in holder.Slots)
            {
                if (slot == null || slot.IsLocked) continue;
                if (holderIsTool && !includeToolSlots) continue;
                bool sealedHere = underSealed || ItemActions.IsSealedSlot(slot);   // inherited, like Walk
                AddPooled(slot, holder, depth, holderIsTool, sealedHere);
                DynamicThing occ = slot.Get();
                if (occ != null) WalkPooled(occ, depth + 1, maxDepth, includeToolSlots, sealedHere);
            }
        }

        private static void AddPooled(Slot slot, Thing holder, int depth, bool insideTool, bool isSealed)
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
            s.Sealed = isSealed;
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
        /// D-005 (A3): never an item sitting in a SEALED slot, or beneath one
        /// (<see cref="ScannedSlot.Sealed"/>) — a built-in part (an emergency suit's tank, an
        /// emergency tool's battery) must not be offered as a swap/install candidate it could never
        /// be put back from, and a belt trapped in a cable coil's slot must not reach the belt picker
        /// (whose swap would put the WORN belt into that coil — see ItemActions.SwapWornToolbelt).
        /// </summary>
        public static List<ScannedSlot> FindCompatible(Slot targetSlot, int maxDepth, bool includeToolSlots)
        {
            var found = new List<ScannedSlot>();
            if (targetSlot == null) return found;
            foreach (var scanned in Scan(maxDepth, includeToolSlots))
            {
                if (scanned.Sealed) continue;                    // D-005: never list a built-in / trapped item
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

        // The game update of 2026-07-28 replaced Slot.SpecificTypePrefabHash (int, -1 =
        // unrestricted) with Slot.SpecificTypePrefabHashes (int[]): a slot may now be locked
        // to SEVERAL specific prefabs. PUBLIC-BRANCH players still run builds with the OLD
        // field, and a COMPILED reference to either name hard-throws MissingFieldException on
        // the build that lacks it (playtester report 2026-08-03: "SmartStow+ failed, falling
        // back to vanilla" + a degraded Universal Inventory). So the field is resolved by
        // REFLECTION once - whichever shape this game build carries - and read through that.
        // The FieldInfo caches are immutable after resolve and hold no Unity objects, so
        // there is nothing to reset on hot reload (they die with the assembly).
        private static FieldInfo _slotHashesField;   // new shape: int[] SpecificTypePrefabHashes
        private static FieldInfo _slotHashField;     // old shape: int   SpecificTypePrefabHash
        private static bool _slotFieldResolved;

        private static void ResolveSlotRestrictionField()
        {
            if (_slotFieldResolved) return;
            _slotFieldResolved = true;
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var f = typeof(Slot).GetField("SpecificTypePrefabHashes", F);
                if (f != null && f.FieldType == typeof(int[])) { _slotHashesField = f; return; }
                f = typeof(Slot).GetField("SpecificTypePrefabHash", F);
                if (f != null && f.FieldType == typeof(int)) _slotHashField = f;
            }
            catch { }
        }

        /// <summary>True when this slot is locked to specific prefab(s). Works on BOTH game
        /// shapes (see the resolver above). Unknown/future shape = fail-OPEN (unrestricted, the
        /// old -1 default): being permissive here can only OFFER a candidate the authoritative
        /// server-side AllowMove/AllowSwap gate then refuses - never a wrong mutation.</summary>
        public static bool SlotIsPrefabRestricted(Slot slot)
        {
            if (slot == null) return false;
            ResolveSlotRestrictionField();
            try
            {
                if (_slotHashesField != null)
                {
                    var hashes = _slotHashesField.GetValue(slot) as int[];
                    return hashes != null && hashes.Length > 0;
                }
                if (_slotHashField != null)
                    return (int)_slotHashField.GetValue(slot) != -1;
            }
            catch { }
            return false;
        }

        /// <summary>True when the slot's specific-prefab lock (if any) admits this prefab.
        /// Dual-shape like <see cref="SlotIsPrefabRestricted"/>; unknown shape = admit.</summary>
        public static bool SlotAcceptsPrefab(Slot slot, int prefabHash)
        {
            if (slot == null) return true;
            ResolveSlotRestrictionField();
            try
            {
                if (_slotHashesField != null)
                {
                    var hashes = _slotHashesField.GetValue(slot) as int[];
                    if (hashes == null || hashes.Length == 0) return true;
                    for (int i = 0; i < hashes.Length; i++)
                        if (hashes[i] == prefabHash) return true;
                    return false;
                }
                if (_slotHashField != null)
                {
                    int h = (int)_slotHashField.GetValue(slot);
                    return h == -1 || h == prefabHash;
                }
            }
            catch { }
            return true;
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

