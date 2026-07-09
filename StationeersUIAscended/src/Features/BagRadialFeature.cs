using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
{
    /// <summary>
    /// Tab: your inventory as radials. Root shows worn containers plus two
    /// nesting-killers: "Find item" (everything you can reach, flattened and grouped by
    /// type — no digging) and "Grab another: X" (repeats your last retrieval in one
    /// flick). Inside a bag: click a bag to enter it, click an item to take it, slide out
    /// on an item to open its controls/slots. By default TAP opens the radial (sticky)
    /// and HOLD shows the vanilla scoreboard; a config flips the two.
    /// </summary>
    public sealed class BagRadialFeature : IRadialFeature
    {
        public string Title => "Inventory";
        public bool Enabled => UIAConfig.BagRadialEnabled.Value;
        public KeyCode Key => UIAConfig.BagRadialKey.Value;
        public bool OpenOnTap => UIAConfig.BagRadialTapOpens.Value;

        public bool CanOpen() => Guards.LocalHuman != null;

        public List<RadialEntry> BuildRoot()
        {
            var entries = new List<RadialEntry>();
            var human = Guards.LocalHuman;
            if (human == null) return entries;

            // "Grab another: X" — one flick to repeat the last retrieval.
            if (RetrievalMemory.LastName != null)
            {
                var again = FindByPrefab(RetrievalMemory.LastPrefabHash);
                if (again != null)
                {
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
            }

            // "Find item" — the whole reachable inventory, flattened and grouped by type.
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

        // ---------- Find item ----------

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
                    Sublabel = (instances.Count > 1 ? "×" + instances.Count + " · " : "") + first.Location,
                    Icon = thing.GetThumbnail(),
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
                Icon = thing.GetThumbnail(),
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
                Sublabel = $"{fallbackName} · {used}/{container.Slots.Count}",
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

            // Category grouping for crowded bags (proposal §10)
            if (occupied.Count > UIAConfig.BagRadialGroupThreshold.Value)
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
            else
            {
                foreach (Slot s in occupied)
                {
                    var e = ItemEntry(bag, s);
                    if (e != null) entries.Add(e);
                }
            }

            // One stow target when holding something and the bag has room — this is how you
            // choose WHICH bag an item goes into.
            var hand = InventoryManager.ActiveHandSlot;
            var held = hand?.Get();
            if (held != null)
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
                        AccentOverride = Theme.Good,
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

            // Bags are navigated; everything else is taken (click) or opened (slide-out).
            if (ItemMenuBuilder.LooksLikeContainer(occ))
            {
                return new RadialEntry
                {
                    Label = occ.DisplayName,
                    ActionText = "Open bag",
                    Sublabel = occ.Slots.Count(s => s?.Get() != null) + "/" + occ.Slots.Count,
                    Icon = occ.GetThumbnail(),
                    ChildProvider = () => BuildBagLevel(occ),
                };
            }

            var thing = occ;
            bool hasInnards = (occ.Slots != null && occ.Slots.Count > 0)
                || occ.InteractOnOff != null || occ.InteractMode != null;
            return new RadialEntry
            {
                Label = occ.DisplayName,
                ActionText = "Take to hand",
                Sublabel = ToolbeltRadialFeature.DescribeState(occ),
                Icon = occ.GetThumbnail(),
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
