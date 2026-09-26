using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Tab: your inventory as radials. Root shows worn containers plus the nesting-killers.
    ///
    /// "Search" flips the radial into the search panel (type, click, item lands in a free
    /// hand or at your feet). Inside a bag: click a bag to enter it, click an item to take
    /// it; slide out on a nested bag for STOW/TAKE, on a device for its controls/slots.
    /// Crowded bags group by the mod's own UIA sorting classes (backpacks are Storage, not
    /// Clothing). Items can be press-dragged out and parked on screen.
    /// </summary>
    public sealed class BagRadialFeature : IRadialFeature
    {
        public string Title => "Inventory";
        /// <summary>Always on (the post-0.9.2.5 play-test round): the per-wheel enable toggles are gone — the radial half's
        /// master switch (<c>UIAConfig.RadialEnabled</c>) is the only gate.</summary>
        public bool Enabled => true;
        public KeyCode Key => UIAConfig.BagRadialKey.Value;
        public bool OpenOnTap => UIAConfig.BagRadialTapOpens.Value;

        public bool CanOpen() => Guards.LocalHuman != null;
        public bool OpensOnBoth => false;

        /// <summary>Tab and the toolbelt ring's Hub wedge branch into the SAME builder, so the
        /// two can never drift apart. (The classic-schema root — a "Find item" list radial plus
        /// container-only wedges — went with the schema chooser in the post-0.9.2.5 play-test round.)</summary>
        public List<RadialEntry> BuildRoot() => BuildHubRoot();

        /// <summary>The inventory root — what Tab opens, and what the Hub wedge on the toolbelt
        /// radial branches into: search + every worn piece.</summary>
        internal static List<RadialEntry> BuildHubRoot()
        {
            var entries = new List<RadialEntry>();
            var human = Guards.LocalHuman;
            if (human == null) return entries;

            AddGrabAnother(entries);

            // The radial transforms into the search radial.
            entries.Add(new RadialEntry
            {
                Label = "Search",
                Sublabel = "all bags",
                ActionText = "Search",
                OnSelect = RadialMenu.RequestSearch,
            });

            // EVERY worn piece, opening the exact radial the 1-6 keys open (parity is
            // the rule: Tab->Suit must look identical to tapping 3). Empty worn slots
            // are STOW wedges.
            AddWorn(entries, human.HelmetSlot, "Helmet");
            AddWorn(entries, human.GlassesSlot, "Glasses");
            AddWorn(entries, human.SuitSlot, "Suit");
            AddWorn(entries, human.BackpackSlot, "Backpack");
            AddWorn(entries, human.UniformSlot, "Uniform");
            AddWorn(entries, human.ToolbeltSlot, "Toolbelt");
            return entries;
        }

        /// <summary>The "recent item" wedge — one flick to repeat the last retrieval.
        /// D-023: labelled persistently (CornerTag) and named "Recent item" in the hub —
        /// play-testers couldn't tell what the unlabelled wedge was.</summary>
        private static void AddGrabAnother(List<RadialEntry> entries)
        {
            if (RetrievalMemory.LastName == null) return;
            var again = FindByPrefab(RetrievalMemory.LastPrefabHash);
            if (again == null) return;
            entries.Add(new RadialEntry
            {
                Label = RetrievalMemory.LastName,
                ActionText = "Recent item",   // D-023: hub line (was "Grab another")
                Verb = "Take",                // D-022: action word (click = EquipToActiveHand)
                CornerTag = "Recent item",    // D-023: persistent tag inside the wedge
                Sublabel = again.Location,
                Icon = RetrievalMemory.LastIcon,
                AccentOverride = Theme.Accent,
                OnSelect = () => TakeAndRemember(again),
            });
        }

        /// <summary>One worn equipment piece on the Tab root — same radial as its 1-6 key.</summary>
        private static void AddWorn(List<RadialEntry> entries, Slot slot, string role)
        {
            if (slot == null || slot.IsLocked) return;
            DynamicThing occ = slot.Get();
            if (occ == null)
            {
                var held = InventoryManager.ActiveHandSlot?.Get();
                var e = ItemMenuBuilder.BuildStowEntry(slot, role, held);
                e.Sublabel = role + " (empty)";
                entries.Add(e);
                return;
            }
            var thing = occ;
            var wornSlot = slot;
            var source = new ScannedSlot { Slot = slot, Holder = slot.Parent, Location = role }.Pin();
            var entry = new RadialEntry
            {
                Label = occ.DisplayName,
                ActionText = "Open",
                Sublabel = role,
                StateText = StateText.For(occ),
                Icon = occ.GetThumbnail(),
                DragSource = source,
                // Parity: the same manage radial the equipment key builds (take entry too).
                ChildProvider = () => ItemMenuBuilder.BuildManageEntries(thing, wornSlot, includeTakeEntry: true),
            };
            // D-005 (A3): count and accept drops over the slots the radial LISTS only — a worn
            // Emergency EVA suit's hidden tanks are not storage (see ItemMenuBuilder.IsListableSlot).
            int usedSlots;
            int listedSlots = ItemMenuBuilder.CountListableSlots(occ, out usedSlots);
            if (listedSlots > 0)
            {
                entry.Sublabel = role + " - " + usedSlots + "/" + listedSlots;
                entry.DropResolver = dragged => ItemMenuBuilder.FirstFreeSlot(thing, dragged);
            }
            if (ItemMenuBuilder.IsBindableBag(occ)) entry.BindableBag = thing; // e.g. worn backpack (#3)
            entries.Add(entry);
        }

        // ---------- retrieval ----------
        // NOTE: BuildFindLevel/InstanceEntry (the classic schema's "Find item" flattened list
        // radial) were deleted with the schema chooser in the post-0.9.2.5 play-test round — the search PANEL
        // (RadialMenu.RequestSearch -> SearchPanelView) is the one search surface now.

        /// <summary>The recent-item candidate: the shallowest carried instance of the prefab. Never one
        /// sitting in (or beneath) a SEALED slot (D-005, A3 — <see cref="ScannedSlot.Sealed"/>): the
        /// wedge must not offer to rip a built-in part out of an emergency suit or tool.</summary>
        private static ScannedSlot FindByPrefab(int prefabHash)
        {
            return InventoryScanner
                .Scan(UIAConfig.ScanDepth.Value, UIAConfig.AllowToolSlotSources.Value)
                .Where(s => s.Occupant != null && s.Depth > 0 && !s.Sealed && s.Occupant.PrefabHash == prefabHash)
                .OrderBy(s => s.Depth)
                .FirstOrDefault()?.Pin();
        }

        private static void TakeAndRemember(ScannedSlot source)
        {
            var thing = source?.Occupant;
            if (ItemActions.EquipToActiveHand(source) && thing != null)
                RetrievalMemory.Record(thing);
        }

        // ---------- containers ----------
        // NOTE: AddContainer (the classic schema's container-only root wedge) was deleted with
        // the schema chooser in the post-0.9.2.5 play-test round; AddWorn builds every root wedge now.

        internal static List<RadialEntry> BuildBagLevel(DynamicThing bag)
        {
            var entries = new List<RadialEntry>();
            // Backstop: every route here is already gated by LooksLikeContainer/HasInnards, but an
            // off-limits container (body bag) must never enumerate its slots even if a new caller
            // forgets the gate.
            if (Core.InventoryScanner.ContentsOffLimits(bag)) return entries;
            if (bag?.Slots == null) return entries;

            // D-005 (A3): a sealed slot is neither an item wedge nor free space — so it can never
            // become a STOW wedge, nor the aggregate STOW wedge's target (built from `empty` below).
            var occupied = new List<Slot>();
            var empty = new List<Slot>();
            foreach (Slot s in bag.Slots)
            {
                if (s == null || s.IsLocked) continue;
                if (!ItemMenuBuilder.IsListableSlot(s)) continue;
                if (s.Get() != null) occupied.Add(s);
                else empty.Add(s);
            }

            // Category grouping for crowded bags (proposal §10). We group by the mod's own UIA
            // sorting classes — nested backpacks read "Storage", batteries read "Power Cells" —
            // instead of the game's coarse SortingClass. The GroupBySortingClass toggle turns
            // hierarchalising off entirely. (The vanilla-SortingClass grouping the classic schema
            // used went with the schema chooser in the post-0.9.2.5 play-test round.)
            bool mayGroup = occupied.Count > UIAConfig.BagRadialGroupThreshold.Value
                && UIAConfig.BagGrouping.Value;
            if (mayGroup)
            {
                foreach (var group in occupied
                             .GroupBy(s => UIASort.Classify(s.Get()))
                             .OrderBy(g => (int)g.Key))
                {
                    var slots = group.ToList();
                    // A category of ONE is redundant — a "group" you'd never need to open —
                    // so show that single item straight on the ring instead of a group wedge.
                    if (slots.Count == 1)
                    {
                        var single = ItemEntry(bag, slots[0]);
                        if (single != null) entries.Add(single);
                        continue;
                    }
                    var first = slots[0].Get();
                    entries.Add(new RadialEntry
                    {
                        Label = UIASort.DisplayName(group.Key),
                        ActionText = "Open",
                        Sublabel = slots.Count + " item(s)",
                        Icon = first?.GetThumbnail(),
                        GroupStyle = true, // category wedge: its own edge/fill palette
                        ChildProvider = () => slots.Select(s => ItemEntry(bag, s)).Where(e => e != null).ToList(),
                    });
                }
            }
            else
            {
                foreach (Slot s in occupied)
                {
                    var e = ItemEntry(bag, s);
                    if (e != null) entries.Add(e);
                }
            }

            // #6: vanilla's Sort/organise button, surfaced as a wedge (bags/boxes/crates/ore belts —
            // any container with generic None/Ore storage and 2+ items, matching the vanilla gate).
            var sortEntry = ItemMenuBuilder.BuildSortEntry(bag);
            if (sortEntry != null) entries.Add(sortEntry);

            // Free space presentation (the EmptySlotDisplay dropdown): individual empty-slot
            // STOW wedges, an aggregate STOW wedge, or both. The aggregate one doubles as
            // the drop target for parked chips.
            var hand = InventoryManager.ActiveHandSlot;
            var held = hand?.Get();
            var mode = UIAConfig.BagEmptySlots.Value;

            if (mode != EmptySlotMode.StowOnly)
            {
                foreach (Slot s in empty)
                {
                    var slotEntry = ItemMenuBuilder.BuildStowEntry(s,
                        string.IsNullOrEmpty(s.DisplayName) ? "Slot" : s.DisplayName, held);
                    entries.Add(slotEntry);
                }
            }

            if (mode != EmptySlotMode.EmptySlots)
            {
                // `empty` holds listable (non-sealed) slots only, and the drop resolver below
                // (FirstFreeSlot) skips sealed ones itself, so this wedge can never resolve to one.
                Slot free = held != null ? empty.FirstOrDefault(s => Slot.AllowMove(held, s)) : null;
                Slot target = free ?? empty.FirstOrDefault();
                if (target != null)
                {
                    var bagRef = bag;
                    // Aggregate STOW wedge: blank slot + STOW, held item previews on hover.
                    var e = ItemMenuBuilder.BuildStowEntry(target,
                        string.IsNullOrEmpty(target.DisplayName) ? bag.DisplayName : target.DisplayName, held);
                    e.Sublabel = "into " + bag.DisplayName;
                    // Chips pick whichever free slot fits THEM, not the held item's slot.
                    e.DropSlot = null;
                    e.DropResolver = dragged => ItemMenuBuilder.FirstFreeSlot(bagRef, dragged);
                    entries.Add(e);
                }
            }
            return entries;
        }

        private static RadialEntry ItemEntry(DynamicThing bag, Slot slot)
        {
            DynamicThing occ = slot.Get();
            if (occ == null) return null;
            var source = new ScannedSlot { Slot = slot, Holder = bag, Location = bag.DisplayName }.Pin();
            var thing = occ;

            // A sealed package nested in this bag stays unpack-only — never a bag-in-a-bag view.
            if (ItemMenuBuilder.IsUnpackBox(occ)) return ItemMenuBuilder.BuildUnpackBoxEntry(occ, source);

            // Bags are navigated; everything else is taken (click) or opened (slide-out).
            if (ItemMenuBuilder.LooksLikeContainer(occ))
            {
                var entry = new RadialEntry
                {
                    Label = occ.DisplayName,
                    ActionText = "Open",
                    Sublabel = ItemMenuBuilder.CountSlots(occ),   // listable slots only (D-005)
                    Icon = occ.GetThumbnail(),
                    ChildProvider = () => BuildBagLevel(thing),
                    BindableBag = ItemMenuBuilder.IsBindableBag(occ) ? thing : null, // Ctrl+number (#3)
                    // Nested bag slide-out: STOW the held item into it / TAKE the bag itself.
                    SlideOutProvider = () => ItemMenuBuilder.BuildTakeOrStowEntries(thing, source),
                    SlideOutLabel = "More",
                    DragSource = source,
                    DropResolver = dragged => ItemMenuBuilder.FirstFreeSlot(thing, dragged),
                };
                return entry;
            }

            bool hasInnards = ItemMenuBuilder.HasInnards(occ);
            bool canSlideOut = hasInnards || ItemMenuBuilder.CanSplit(occ); // a stack swipes to its splits
            return new RadialEntry
            {
                Label = occ.DisplayName,
                ActionText = "Take to hand",
                Sublabel = ToolbeltRadialFeature.DescribeState(occ),
                StateText = StateText.For(occ),
                Icon = occ.GetThumbnail(),
                DragSource = source,
                OnSelect = () => TakeAndRemember(source),
                SlideOutProvider = canSlideOut
                    ? () => ItemMenuBuilder.BuildManageEntries(thing, slot, includeTakeEntry: false)
                    : (System.Func<List<RadialEntry>>)null,
                SlideOutLabel = hasInnards ? "Open" : "Split",
                BindableBag = ItemMenuBuilder.IsBindableBag(occ) ? thing : null, // small boxes/crates (#3)
            };
        }

        /// <summary>The vanilla scoreboard toggle we suppressed — dispatched on tap when the
        /// radial opens on hold, or on hold when the radial opens on tap.</summary>
        private static void ShowScoreboard()
        {
            InventoryManager.Instance?.ToggleScoreboard(false);
        }

        public void OnTap()
        {
            if (!OwnsVanillaKey) return;
            ShowScoreboard();
        }

        public void OnHold()
        {
            if (!OwnsVanillaKey) return;
            ShowScoreboard();
        }

        public bool OwnsVanillaKey => Enabled && Key == KeyMap.ShowScoreBoard;
    }
}
