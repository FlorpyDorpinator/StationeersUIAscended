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
    /// Option A: "Search" flips the radial into the search panel (type, click, item lands
    /// in a free hand or at your feet). Inside a bag: click a bag to enter it, click an
    /// item to take it; slide out on a nested bag for STOW/TAKE, on a device for its
    /// controls/slots. Crowded bags group by the mod's own UIA sorting classes (backpacks
    /// are Storage, not Clothing). Items can be press-dragged out and parked on screen.
    ///
    /// Option D keeps the classic "Find item" list radial and vanilla SortingClass groups.
    /// </summary>
    public sealed class BagRadialFeature : IRadialFeature
    {
        public string Title => "Inventory";
        public bool Enabled => UIAConfig.BagRadialEnabled.Value;
        public KeyCode Key => UIAConfig.BagRadialKey.Value;
        public bool OpenOnTap => UIAConfig.BagRadialTapOpens.Value;

        public bool CanOpen() => Guards.LocalHuman != null;
        public bool OpensOnBoth => false;

        public List<RadialEntry> BuildRoot()
        {
            // A-family (A and B) shares one root; Option B's Hub wedge branches into the
            // SAME builder, so Tab and MMB->Hub can never drift apart.
            if (UIAConfig.IsA) return BuildHubRoot();

            var entries = new List<RadialEntry>();
            var human = Guards.LocalHuman;
            if (human == null) return entries;
            AddGrabAnother(entries);

            // Option D: the classic flattened list radial + container-only root.
            entries.Add(new RadialEntry
            {
                Label = "Find item",
                Sublabel = "search all bags",
                ActionText = "Open",
                ChildProvider = BuildFindLevel,
            });
            AddContainer(entries, human.BackpackSlot, "Backpack");
            AddContainer(entries, human.ToolbeltSlot, "Toolbelt");
            AddContainer(entries, human.SuitSlot, "Suit");
            AddContainer(entries, human.UniformSlot, "Uniform");
            return entries;
        }

        /// <summary>The Option A inventory root — what Tab opens, and what Option B's Hub
        /// wedge on the toolbelt radial branches into: search + every worn piece.</summary>
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

        /// <summary>"Grab another: X" — one flick to repeat the last retrieval.</summary>
        private static void AddGrabAnother(List<RadialEntry> entries)
        {
            if (RetrievalMemory.LastName == null) return;
            var again = FindByPrefab(RetrievalMemory.LastPrefabHash);
            if (again == null) return;
            entries.Add(new RadialEntry
            {
                Label = RetrievalMemory.LastName,
                ActionText = "Grab another",
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
            if (occ.Slots != null && occ.Slots.Count > 0)
            {
                int used = occ.Slots.Count(s => s?.Get() != null);
                entry.Sublabel = role + " - " + used + "/" + occ.Slots.Count;
                entry.DropResolver = dragged => ItemMenuBuilder.FirstFreeSlot(thing, dragged);
            }
            entries.Add(entry);
        }

        // ---------- Find item (Option D) ----------

        private static List<RadialEntry> BuildFindLevel()
        {
            var entries = new List<RadialEntry>();
            var groups = InventoryScanner
                .Scan(UIAConfig.ScanDepth.Value, UIAConfig.AllowToolSlotSources.Value)
                .Where(s => s.Occupant != null && s.Depth > 0)
                .GroupBy(s => s.Occupant.PrefabHash)
                .OrderBy(g => g.First().Occupant.DisplayName)
                .ToList();

            foreach (var group in groups)
            {
                var instances = group.OrderBy(s => s.Depth).Select(s => s.Pin()).ToList();
                var first = instances[0];
                var thing = first.Occupant;
                entries.Add(new RadialEntry
                {
                    Label = thing.DisplayName,
                    ActionText = "Take nearest",
                    Sublabel = (instances.Count > 1 ? "x" + instances.Count + " - " : "") + first.Location,
                    StateText = StateText.For(thing),
                    Icon = thing.GetThumbnail(),
                    DragSource = first,
                    OnSelect = () => TakeAndRemember(first),
                    // Pick a specific one when there are several.
                    SlideOutProvider = instances.Count > 1
                        ? () => instances.Select(InstanceEntry).ToList()
                        : (System.Func<List<RadialEntry>>)null,
                    SlideOutLabel = "Pick one",
                });
            }
            return entries;
        }

        private static RadialEntry InstanceEntry(ScannedSlot scanned)
        {
            var thing = scanned.Occupant;
            if (thing == null) return new RadialEntry { Label = "(gone)", Enabled = false };
            return new RadialEntry
            {
                Label = thing.DisplayName,
                ActionText = "Take to hand",
                Sublabel = scanned.Location,
                Warning = InventoryScanner.ConsequenceOfRemoving(scanned),
                StateText = StateText.For(thing),
                Icon = thing.GetThumbnail(),
                DragSource = scanned,
                OnSelect = () => TakeAndRemember(scanned),
            };
        }

        private static ScannedSlot FindByPrefab(int prefabHash)
        {
            return InventoryScanner
                .Scan(UIAConfig.ScanDepth.Value, UIAConfig.AllowToolSlotSources.Value)
                .Where(s => s.Occupant != null && s.Depth > 0 && s.Occupant.PrefabHash == prefabHash)
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

        private static void AddContainer(List<RadialEntry> entries, Slot slot, string fallbackName)
        {
            DynamicThing container = slot?.Get();
            if (container == null || container.Slots == null || container.Slots.Count == 0) return;
            int used = container.Slots.Count(s => s?.Get() != null);
            entries.Add(new RadialEntry
            {
                Label = container.DisplayName,
                ActionText = "Open",
                Sublabel = $"{fallbackName} - {used}/{container.Slots.Count}",
                Icon = container.GetThumbnail(),
                ChildProvider = () => BuildBagLevel(container),
            });
        }

        internal static List<RadialEntry> BuildBagLevel(DynamicThing bag)
        {
            var entries = new List<RadialEntry>();
            if (bag?.Slots == null) return entries;

            var occupied = new List<Slot>();
            var empty = new List<Slot>();
            foreach (Slot s in bag.Slots)
            {
                if (s == null || s.IsLocked) continue;
                if (s.Get() != null) occupied.Add(s);
                else empty.Add(s);
            }

            // Category grouping for crowded bags (proposal §10). Option A groups by the
            // mod's own UIA sorting classes — nested backpacks read "Storage", batteries
            // read "Power Cells" — instead of the game's coarse SortingClass. The
            // GroupBySortingClass toggle turns hierarchalising off entirely for A.
            bool mayGroup = occupied.Count > UIAConfig.BagRadialGroupThreshold.Value
                && (!UIAConfig.IsA || UIAConfig.BagGrouping.Value);
            if (mayGroup)
            {
                if (UIAConfig.IsA)
                {
                    foreach (var group in occupied
                                 .GroupBy(s => UIASort.Classify(s.Get()))
                                 .OrderBy(g => (int)g.Key))
                    {
                        var slots = group.ToList();
                        var first = slots[0].Get();
                        entries.Add(new RadialEntry
                        {
                            Label = UIASort.DisplayName(group.Key),
                            ActionText = "Open",
                            Sublabel = slots.Count + " item(s)",
                            Icon = first?.GetThumbnail(),
                            ChildProvider = () => slots.Select(s => ItemEntry(bag, s)).Where(e => e != null).ToList(),
                        });
                    }
                }
                else
                {
                    foreach (var group in occupied
                                 .GroupBy(s => s.Get().SortingClass)
                                 .OrderBy(g => (int)g.Key))
                    {
                        var slots = group.ToList();
                        var first = slots[0].Get();
                        entries.Add(new RadialEntry
                        {
                            Label = group.Key.ToString(),
                            ActionText = "Open",
                            Sublabel = slots.Count + " item(s)",
                            Icon = first?.GetThumbnail(),
                            ChildProvider = () => slots.Select(s => ItemEntry(bag, s)).Where(e => e != null).ToList(),
                        });
                    }
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

            // Free space presentation (Option A, playtest dropdown): individual empty-slot
            // STOW wedges, an aggregate STOW wedge, or both. The aggregate one doubles as
            // the drop target for parked chips.
            var hand = InventoryManager.ActiveHandSlot;
            var held = hand?.Get();
            if (UIAConfig.IsA)
            {
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
            }
            else if (held != null)
            {
                Slot free = empty.FirstOrDefault(s => Slot.AllowMove(held, s));
                if (free != null)
                {
                    entries.Add(new RadialEntry
                    {
                        Label = held.DisplayName,
                        ActionText = "Stow here",
                        Sublabel = "into " + bag.DisplayName,
                        Icon = held.GetThumbnail(),
                        AccentOverride = Theme.Accent,
                        FillOverride = Theme.RingStow, // orange = "held item goes here"
                        OnSelect = () => ItemActions.StowActiveHandTo(free),
                    });
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

            // Bags are navigated; everything else is taken (click) or opened (slide-out).
            if (ItemMenuBuilder.LooksLikeContainer(occ))
            {
                var entry = new RadialEntry
                {
                    Label = occ.DisplayName,
                    ActionText = UIAConfig.IsA ? "Open" : "Open bag",
                    Sublabel = occ.Slots.Count(s => s?.Get() != null) + "/" + occ.Slots.Count,
                    Icon = occ.GetThumbnail(),
                    ChildProvider = () => BuildBagLevel(thing),
                };
                if (UIAConfig.IsA)
                {
                    // Nested bag slide-out: STOW the held item into it / TAKE the bag itself.
                    entry.SlideOutProvider = () => ItemMenuBuilder.BuildTakeOrStowEntries(thing, source);
                    entry.SlideOutLabel = "More";
                    entry.DragSource = source;
                    entry.DropResolver = dragged => ItemMenuBuilder.FirstFreeSlot(thing, dragged);
                }
                return entry;
            }

            bool hasInnards = ItemMenuBuilder.HasInnards(occ);
            return new RadialEntry
            {
                Label = occ.DisplayName,
                ActionText = "Take to hand",
                Sublabel = ToolbeltRadialFeature.DescribeState(occ),
                StateText = StateText.For(occ),
                Icon = occ.GetThumbnail(),
                DragSource = source,
                OnSelect = () => TakeAndRemember(source),
                SlideOutProvider = hasInnards
                    ? () => ItemMenuBuilder.BuildManageEntries(thing, slot, includeTakeEntry: false)
                    : (System.Func<List<RadialEntry>>)null,
                SlideOutLabel = "Open",
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
