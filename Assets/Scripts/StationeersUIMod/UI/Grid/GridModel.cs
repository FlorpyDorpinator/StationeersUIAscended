using System.Collections.Generic;
using System.Text;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using Objects.Items; // SECOND game namespace (distinct from Assets.Scripts.Objects.Items): home of
                     // EmergencySuppliesBox + CerealBarBox. Verified collision-free vs every other
                     // using here, so no simple name in this file becomes ambiguous.
using StationeersUIMod.Features;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// How The Grid lays out the local player's inventory.
    /// <see cref="Nested"/> = the classic indented tree (tools expand into sub-sections).
    /// <see cref="Grid"/> = the flat Diablo-style pack: each STORAGE container is a bordered
    /// region of cells; a tool/component-holder stays a single leaf cell (does NOT expand).
    /// <para>DEPRECATED: <see cref="Nested"/> is no longer reachable. The in-window toggle and the
    /// F10 selector that used to switch modes were removed, so a stale <c>Nested</c> value in
    /// config would have stranded the user in a view with no way back out.
    /// <see cref="GridModel.ActiveMode"/> is now hard-wired to <see cref="Grid"/> and the config
    /// value is ignored; the enum survives only so old profiles/config still deserialize.</para>
    /// </summary>
    public enum GridDisplayMode
    {
        Nested,
        Grid,
    }

    /// <summary>
    /// One node in The Grid's container tree: either the local human's worn-slot root
    /// (<see cref="Container"/> == null) or a nested container occupying <see cref="ParentSlot"/>.
    /// <see cref="Slots"/> are this node's OWN slots (rendered as cells); <see cref="Children"/>
    /// are the containers nested inside those slots (rendered as indented sub-sections).
    /// A pure read-only view model — no game-state mutation lives here.
    /// </summary>
    public sealed class ContainerNode
    {
        /// <summary>The container this section represents, or null for the worn-slot root.</summary>
        public DynamicThing Container;
        /// <summary>The slot the container occupies (null at the root).</summary>
        public Slot ParentSlot;
        /// <summary>Header title (player/container display name).</summary>
        public string Title;
        /// <summary>This node's own slots, in display order (organ anchors filtered out).</summary>
        public List<Slot> Slots;
        /// <summary>Nested container sub-sections, in the same order their parent slots appear.</summary>
        public List<ContainerNode> Children;
        /// <summary>Stable per-save key for collapse state (Thing.ReferenceId; human ref at root).</summary>
        public long RefId;
        /// <summary>Occupied slot count in <see cref="Slots"/> (for the "used/total" header).</summary>
        public int UsedCount;
        /// <summary>Total slot count in <see cref="Slots"/>.</summary>
        public int TotalCount;
        /// <summary>Meaningful only on the ROOT node: the structural signature at build time.</summary>
        public long Signature;
        /// <summary>True when this container is torn out into its own pinned window
        /// (<see cref="StationeersUIMod.Features.GridPinStore"/>). The node is still BUILT — the pinned
        /// window binds it — but the Universal Inventory window must not render it as a child region.
        /// Never set on the root. View-only: pinning mutates no game state.</summary>
        public bool Pinned;
    }

    /// <summary>
    /// Builds the recursive container tree for the local player's inventory — worn slots, hands,
    /// and every container nested inside them, infinitely deep with a cycle guard. Client-safe
    /// reads only (occupant identity + networked slot state); no mutation. The panel rebuilds the
    /// tree only when <see cref="ComputeSignature"/> changes, so per-frame the model just hands
    /// back a cheap structural hash. Mirrors <see cref="StationeersUIMod.Core.InventoryScanner"/>'s
    /// guarded recursive walk (visited-set cycle guard, occupant snapshot).
    /// </summary>
    public static class GridModel
    {
        // Belt-and-suspenders depth cap; the visited set already prevents cycles.
        private const int MaxDepth = 24;

        // FNV-1a 64-bit constants for the rolling structural hash.
        private const long FnvOffset = unchecked((long)14695981039346656037UL);
        private const long FnvPrime = 1099511628211L;

        // Reused scratch set so ComputeSignature is allocation-free per frame. Single-threaded
        // (Unity main thread); cleared before AND after each walk so it strands no Thing refs
        // between frames (hot-reload safety). Also cleared by Reset().
        private static readonly HashSet<Thing> _sigVisited = new HashSet<Thing>();

        /// <summary>
        /// The ONE layout The Grid renders. <see cref="GridDisplayMode.Nested"/> is deprecated and
        /// deliberately unreachable: its escape hatches (the title-bar toggle and the F10 selector)
        /// were removed. The <c>UIAConfig.GridMode</c> config entry that used to gate this was itself
        /// removed in 0.9.2.5's config-migration cleanup (it had zero readers).
        /// </summary>
        public const GridDisplayMode ActiveMode = GridDisplayMode.Grid;

        // --- store-load throttle ---------------------------------------------------------------
        // GridCollapseStore/GridPinStore.EnsureSaveLoaded() each call BagProfileStore.CurrentSaveKey(),
        // which allocates a fresh string per invalid-name char it replaces. ComputeSignature runs
        // EVERY FRAME from Tick, so calling them there directly was per-frame garbage for a value
        // that only changes when the SAVE changes. Re-check on a slow cadence instead; every mutating
        // path (SetCollapsed/SetPinned) still calls EnsureSaveLoaded itself, and BuildRoot forces it.
        private const int StoreRecheckFrames = 120;
        private static int _storesCheckedFrame = int.MinValue;

        /// <summary>Load the per-save collapse/pin sets, at most once per <see cref="StoreRecheckFrames"/>
        /// frames unless <paramref name="force"/> (a rebuild, where correctness beats the string cost).</summary>
        private static void EnsureStoresLoaded(bool force)
        {
            int frame = UnityEngine.Time.frameCount;
            if (!force && frame - _storesCheckedFrame < StoreRecheckFrames) return;
            _storesCheckedFrame = frame;
            GridCollapseStore.EnsureSaveLoaded();
            GridPinStore.EnsureSaveLoaded();
        }

        /// <summary>
        /// Build the full container tree for the local human, or null if there is no local human.
        /// The returned root's <see cref="ContainerNode.Signature"/> matches what
        /// <see cref="ComputeSignature"/> returns for the same live state.
        /// </summary>
        public static ContainerNode BuildRoot()
        {
            Human human = InventoryManager.ParentHuman;
            if (human == null) return null;

            // Load the per-save collapse AND pin sets ONCE for this whole walk (forced: a rebuild is
            // rare, so pay the save-key cost here). Per-node lookups below read the already-loaded
            // sets via IsCollapsedLoaded/IsPinnedLoaded. Pinned containers are still walked (their
            // window binds the node) but are flagged so the main window skips them as child regions.
            EnsureStoresLoaded(true);

            // The layout mode is fixed (see ActiveMode) — Nested is unreachable. Still threaded
            // through the walk and folded into the hash so the shape of both stays identical to
            // ComputeSignature's.
            const GridDisplayMode mode = ActiveMode;

            var visited = new HashSet<Thing> { human };
            long sig = Mix(FnvOffset, human.ReferenceId);
            sig = Mix(sig, (int)mode);

            var root = new ContainerNode
            {
                Container = null,
                ParentSlot = null,
                Title = SafeName(human.DisplayName, "Equipment"),
                RefId = human.ReferenceId,
                Slots = new List<Slot>(8),
                Children = new List<ContainerNode>(4),
            };

            // Worn order: Helmet, Glasses, Suit, Back, Uniform, Toolbelt, Left, Right hand.
            sig = AddSlot(root, human.HelmetSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.GlassesSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.SuitSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.BackpackSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.UniformSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.ToolbeltSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.LeftHandSlot, visited, 0, mode, ref sig);
            sig = AddSlot(root, human.RightHandSlot, visited, 0, mode, ref sig);

            root.Signature = sig;
            return root;
        }

        /// <summary>Add one slot to <paramref name="node"/> (skipping organ anchors), recording its
        /// occupant into the structural hash and recursing into a container occupant as a child.
        ///
        /// <para>D-005: inside a CONTAINER node only the slots vanilla would draw become cells
        /// (<see cref="IsCellSlot"/>) — a hidden slot never, a stack's slot only while something is
        /// trapped in it (a take-out-only rescue cell). A skipped slot is neither a cell NOR a recursion
        /// point; its occupant is still folded into the hash, and <see cref="SigSlot"/> applies the
        /// SAME skip at the same depth, so the two walks stay identical (a mismatch would rebuild the
        /// tree every frame). The root (the human's worn slots, depth 0) is never filtered.</para></summary>
        private static long AddSlot(ContainerNode node, Slot slot, HashSet<Thing> visited, int depth, GridDisplayMode mode, ref long sig)
        {
            if (slot == null || IsOrganSlot(slot)) return sig;

            DynamicThing occ = slot.Get();
            sig = Mix(sig, occ != null ? occ.ReferenceId : 0L);

            // Container slots only (depth >= 1 — the root's are the human's own): a slot vanilla never
            // draws is neither a cell nor a recursion point. SigSlot mirrors this exactly.
            if (node.Container != null && !IsCellSlot(slot, occ)) return sig;

            node.Slots.Add(slot);
            node.TotalCount++;
            if (occ == null) return sig;

            node.UsedCount++;

            if (depth < MaxDepth && ShouldRecurse(occ, mode) && visited.Add(occ))
            {
                sig = Mix(sig, GridCollapseStore.IsCollapsedLoaded(occ.ReferenceId) ? 1L : 2L);
                bool pinned = GridPinStore.IsPinnedLoaded(occ.ReferenceId);
                sig = Mix(sig, pinned ? 3L : 4L);
                var child = new ContainerNode
                {
                    Container = occ,
                    ParentSlot = slot,
                    Title = SafeName(occ.DisplayName, "Container"),
                    RefId = occ.ReferenceId,
                    Pinned = pinned,
                    Slots = new List<Slot>(occ.Slots.Count),
                    Children = new List<ContainerNode>(),
                };
                var slots = occ.Slots;
                for (int i = 0; i < slots.Count; i++)
                    sig = AddSlot(child, slots[i], visited, depth + 1, mode, ref sig);
                node.Children.Add(child);
            }
            return sig;
        }

        /// <summary>
        /// A cheap, allocation-free structural hash of the local player's inventory: occupant
        /// identity per slot (in the same order <see cref="BuildRoot"/> walks) plus each
        /// container's collapse AND pin state. When this changes, the tree's SHAPE changed (insert /
        /// remove / swap / a container opened or closed / a container was pinned or unpinned) and
        /// the panel should rebuild. In-place
        /// quantity/charge changes do NOT move this — cells diff their own StateText for those.
        /// </summary>
        public static long ComputeSignature()
        {
            Human human = InventoryManager.ParentHuman;
            if (human == null) return 0L;

            // Collapse/pin sets: THROTTLED here (this runs every frame) — see EnsureStoresLoaded.
            // A pin/unpin still moves the signature immediately, because SetCollapsed/SetPinned load
            // and update the sets themselves; this call only re-checks for a SAVE change.
            EnsureStoresLoaded(false);

            // Same fixed mode + hash seeding as BuildRoot, so the two stay consistent for the same
            // live state.
            const GridDisplayMode mode = ActiveMode;

            _sigVisited.Clear();
            _sigVisited.Add(human);
            long h = Mix(FnvOffset, human.ReferenceId);
            h = Mix(h, (int)mode);
            try
            {
                h = SigSlot(h, human.HelmetSlot, 0, mode);
                h = SigSlot(h, human.GlassesSlot, 0, mode);
                h = SigSlot(h, human.SuitSlot, 0, mode);
                h = SigSlot(h, human.BackpackSlot, 0, mode);
                h = SigSlot(h, human.UniformSlot, 0, mode);
                h = SigSlot(h, human.ToolbeltSlot, 0, mode);
                h = SigSlot(h, human.LeftHandSlot, 0, mode);
                h = SigSlot(h, human.RightHandSlot, 0, mode);
            }
            finally
            {
                _sigVisited.Clear();
            }
            return h;
        }

        private static long SigSlot(long h, Slot slot, int depth, GridDisplayMode mode)
        {
            if (slot == null || IsOrganSlot(slot)) return h;

            DynamicThing occ = slot.Get();
            h = Mix(h, occ != null ? occ.ReferenceId : 0L);
            if (occ == null) return h;
            // D-005: the SAME container-slot skip AddSlot applies (depth >= 1 is exactly AddSlot's
            // node.Container != null), so a hidden slot is never a recursion point in either walk.
            if (depth > 0 && !IsCellSlot(slot, occ)) return h;

            if (depth < MaxDepth && ShouldRecurse(occ, mode) && _sigVisited.Add(occ))
            {
                h = Mix(h, GridCollapseStore.IsCollapsedLoaded(occ.ReferenceId) ? 1L : 2L);
                h = Mix(h, GridPinStore.IsPinnedLoaded(occ.ReferenceId) ? 3L : 4L);
                var slots = occ.Slots;
                for (int i = 0; i < slots.Count; i++)
                    h = SigSlot(h, slots[i], depth + 1, mode);
            }
            return h;
        }

        /// <summary>True if this slot is the local player's active hand (for the "active" cell mark).</summary>
        public static bool IsActiveHand(Slot slot)
        {
            return slot != null && slot == InventoryManager.ActiveHandSlot;
        }

        /// <summary>Hot-reload / mod-off: drop any cached Thing references held by the scratch set,
        /// and force the next walk to re-load the per-save stores (frame counts do not survive a
        /// reload meaningfully, and the reloaded assembly must not trust a stale check).</summary>
        public static void Reset()
        {
            _sigVisited.Clear();
            _storesCheckedFrame = int.MinValue;
        }

        /// <summary>
        /// Whether the tree walk descends into <paramref name="occ"/> as its own sub-section,
        /// per the active display mode:
        /// <list type="bullet">
        /// <item><b>Nested</b> — recurse into ANY slot-bearing occupant (tools expand, as today).</item>
        /// <item><b>Grid</b> — recurse ONLY into a general-storage container; a tool /
        /// component-holder stays a single leaf cell in the parent's <see cref="ContainerNode.Slots"/>.</item>
        /// </list>
        /// </summary>
        private static bool ShouldRecurse(DynamicThing occ, GridDisplayMode mode)
        {
            // Off-limits contents (body bag): never open its slots — not in Grid mode, not in
            // Tree mode (whose HasSlots rung would otherwise expose the organs), not even pinned.
            if (Core.InventoryScanner.ContentsOffLimits(occ)) return false;
            // A PINNED interactive item must build a node even when Grid mode would keep it a leaf cell
            // (a tool/device): that node is what PinContainer tears out into a themed pinned window, so
            // "open device internals" opens OUR window, not vanilla's. Includes button-ONLY things (a demo
            // charge, a canister valve — HasSlots false but HasKeyInteractions true): their window is just
            // the controls strip, no cells. The cheap slot/interaction checks short-circuit before the
            // pin-set probe, so a plain (inert) item never pays for it. The item stays a leaf cell too —
            // the node is pruned into its own window (PrunePinned), exactly like a pinned bag.
            if ((HasSlots(occ) || HasKeyInteractions(occ)) && GridPinStore.IsPinnedLoaded(occ.ReferenceId)) return true;
            if (mode != GridDisplayMode.Grid) return HasSlots(occ);
            // D-005 rescue: a STACK (the "Cable Coil" phantom grid) that still has something TRAPPED in
            // one of its slots keeps a region holding ONLY the trapped item(s), as take-out-only cells,
            // until it is emptied. Without this the item would vanish from view — and a consumed or
            // merged-away stack DESTROYS its children (Thing.OnDestroy skips the drop-to-world rescue
            // when DestroyChildrenOnDead, which is true on the coil prefab and by default).
            return IsStorageContainer(occ) || IsRescueContainer(occ);
        }

        /// <summary>
        /// True when <paramref name="t"/> is a general-storage container (its own bordered region
        /// of cells in Grid mode); false for a tool/device whose slots are its own components
        /// (a single leaf cell). Client-safe: reads only networked/prefab state.
        ///
        /// <para>D-005 (vanilla parity): only slots vanilla itself would DRAW count
        /// (<see cref="Core.ItemActions.IsVanillaVisibleSlot"/> — <c>Slot.IsInteractable</c>, the gate of
        /// <c>InventoryWindow.SetSlots</c> / <c>Thing.HasSlots</c>), and a STACK is never storage. A cable
        /// coil used to pass on a non-interactable slot and rendered as an empty grid that accepted
        /// items; vanilla shows it only its Split buttons.</para>
        /// </summary>
        public static bool IsStorageContainer(DynamicThing t)
        {
            if (t == null || t.Slots == null || t.Slots.Count == 0) return false; // leaf
            if (t is Tool) return false;                                          // drill/welder/tablet/...
            // A body bag's slots are the corpse's organs — never storage (FlorpyDorp directive
            // 2026-08-04; the death CARDBOARD BOX stays storage). Shared predicate, see
            // InventoryScanner.ContentsOffLimits.
            if (Core.InventoryScanner.ContentsOffLimits(t)) return false;
            // Single-purpose consumable/dispenser/starter boxes are NOT real storage even though
            // their slot data (a None,-1 slot) is indistinguishable from a genuine bag, so they can
            // only be told apart by CLASS: DisposableCardboardBox (water-bottle bag, cereal-bar box/
            // bag, water/insulated-canister package — also its InsulatedCanisterPackage subclass),
            // the starter EmergencySuppliesBox, any ItemContainer (covers FoodContainer: burger box,
            // egg carton, prefilled supply crates), and the CerealBarBox class. The REUSABLE base
            // CardboardBox / CardboardBoxLarge are deliberately NOT excluded — they stay real storage.
            if (t is DisposableCardboardBox || t is EmergencySuppliesBox
                || t is ItemContainer || t is CerealBarBox) return false;
            // D-005: a STACK (cable coil, ore, ingots, kits...) is never a container — merge/split/
            // consume know nothing of contents, and a consumed stack destroys its children. No
            // Stackable prefab in the 27701 asset rip carries a slot; the live coil evidently does.
            if (t is Stackable) return false;

            // Only slots vanilla would draw count. The prefab-lock read stays the ONE dual-shape
            // reflection accessor (Core.InventoryScanner.SlotIsPrefabRestricted — the 0.9.7.1 fix);
            // this file never names SpecificTypePrefabHash(es) directly.
            var s = t.Slots;
            bool anyShown = false;                                               // a slot vanilla would draw
            bool anyUnrestricted = false;
            bool anyGeneral = false;                                             // a None/Ore slot = general capacity
            for (int i = 0; i < s.Count; i++)
            {
                Slot slot = s[i];
                if (slot == null) continue;
                // Vanilla draws ONLY interactable slots (InventoryWindow.SetSlots); a hidden slot is
                // built-in / untouchable (the emergency suit's tanks, an emergency drill's battery, a
                // package's contents — 27701 rip), so it never makes a thing a container.
                if (!Core.ItemActions.IsVanillaVisibleSlot(slot)) continue;
                anyShown = true;
                if (!Core.InventoryScanner.SlotIsPrefabRestricted(slot)) anyUnrestricted = true;
                if (slot.Type == Slot.Class.None || slot.Type == Slot.Class.Ore) anyGeneral = true;
            }
            if (!anyShown) return false;                                          // nothing a player could use
            switch (t.SlotType)                                                   // worn container (incl. suit)
            {
                case Slot.Class.Back:
                case Slot.Class.Belt:
                case Slot.Class.Suit:
                case Slot.Class.Uniform:
                    return true;
            }
            // Backstop for FUTURE / modded single-purpose dispensers not named above: a purely
            // single-purpose holder restricts EVERY slot to specific prefabs
            // (SpecificTypePrefabHashes non-empty), whereas a real multi-purpose bag always keeps
            // at least one unrestricted slot. Verified in the 27701 prefab rip (then the single
            // -1-sentinel field): ore/mining bags, backpacks, belts and deployable crates all keep
            // unrestricted slots (mining bag slots are Type=Ore but prefab-unrestricted), so this
            // can never exclude genuine storage; the burger box (its lone None slot restricted to
            // ItemBurger) is exactly what it catches.
            if (!anyUnrestricted) return false;                                  // every slot prefab-locked -> not storage
            return anyGeneral;
        }

        /// <summary>D-005 rescue: a STACK with something sitting in one of its slots. Before D-005 the
        /// grid drew a stack's slot (the cable coil's) as an empty cell that accepted items; anything
        /// put there must stay visible and retrievable — a used-up or merged-away stack DESTROYS its
        /// children. Its region shows ONLY the trapped items, take-out-only, and disappears once the
        /// stack is empty. A stack never legitimately holds anything, so this is unambiguous (unlike a
        /// hidden slot on a tool or suit, which is built-in and stays hidden — vanilla parity).</summary>
        private static bool IsRescueContainer(DynamicThing t)
        {
            if (!(t is Stackable) || t.Slots == null || t.Slots.Count == 0) return false;
            if (Core.InventoryScanner.ContentsOffLimits(t)) return false;
            var s = t.Slots;
            for (int i = 0; i < s.Count; i++)
            {
                Slot slot = s[i];
                if (slot != null && !IsOrganSlot(slot) && slot.Get() != null) return true;
            }
            return false;
        }

        /// <summary>Does this slot of a CONTAINER node render as a cell? D-005 vanilla parity: only the
        /// slots vanilla draws (<see cref="Core.ItemActions.IsVanillaVisibleSlot"/>) — a hidden slot is
        /// built-in and never renders, empty or not — and never a stack's slot, EXCEPT while something is
        /// trapped in it (the take-out-only rescue cell; see <see cref="IsRescueContainer"/>). So a thing
        /// with no real storage never renders an empty grid that could swallow an item.</summary>
        internal static bool IsCellSlot(Slot slot, DynamicThing occupant)
        {
            if (slot == null) return false;
            if (!Core.ItemActions.IsSealedSlot(slot)) return true;              // an ordinary, vanilla-drawn slot
            return occupant != null && slot.Parent is Stackable;               // a stack's trapped item: rescue
        }

        /// <summary>Is this cell's slot TAKE-OUT-ONLY? True for a sealed slot (<see cref="Core.ItemActions.IsSealedSlot"/>)
        /// — in practice a stack's rescue cell. Such a cell is never a drop target, and its click/F take
        /// the item to a FREE hand instead of swapping (a swap would refill the sealed slot).</summary>
        internal static bool IsTakeOnlySlot(Slot slot)
        {
            return Core.ItemActions.IsSealedSlot(slot);
        }

        // --- helpers ---

        private static bool HasSlots(DynamicThing thing)
        {
            return thing != null && thing.Slots != null && thing.Slots.Count > 0;
        }

        /// <summary>Has at least one key interaction/button (on-off, lock/arm, valve) with no slot — the
        /// other half of "worth a pinned window". Wrapped because the vanilla getter walks Interactables.</summary>
        private static bool HasKeyInteractions(DynamicThing thing)
        {
            try { return thing != null && thing.HasKeyInteractions; } catch { return false; }
        }

        private static bool IsOrganSlot(Slot slot)
        {
            return slot.Type == Slot.Class.Organ;
        }

        /// <summary>
        /// Null/empty guard PLUS an ASCII fold, because this string is DISPLAYED (region headers and
        /// manila tabs). The game's TextMeshPro font reliably renders Basic Latin only, so a mod or
        /// localisation that puts a curly quote / dash / accent in a container's DisplayName would
        /// render as tofu. Common punctuation is transliterated; anything else non-ASCII is dropped.
        /// Allocation-free for the overwhelmingly common all-ASCII case, and only ever called on a
        /// tree REBUILD (never per frame).
        /// </summary>
        private static string SafeName(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;

            bool clean = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < ' ' || c > '~') { clean = false; break; }
            }
            if (clean) return name;

            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c >= ' ' && c <= '~') { sb.Append(c); continue; }
                switch (c)
                {
                    case ' ': sb.Append(' '); break;                          // nbsp
                    case '‐': case '‑': case '‒':
                    case '–': case '—': sb.Append('-'); break;            // hyphens/dashes
                    case '‘': case '’': sb.Append('\''); break;           // curly single
                    case '“': case '”': sb.Append('"'); break;            // curly double
                    case '…': sb.Append("..."); break;                         // ellipsis
                    case '°': sb.Append(" deg"); break;                        // degree
                    case '×': sb.Append('x'); break;                           // multiplication
                    default: break;                                                 // drop
                }
            }

            string safe = sb.ToString().Trim();
            return safe.Length == 0 ? fallback : safe;
        }

        private static long Mix(long h, long value)
        {
            return unchecked((h ^ value) * FnvPrime);
        }
    }
}
