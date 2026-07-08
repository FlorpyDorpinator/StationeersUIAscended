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
    /// Phase 7 — hold Tab: worn containers → bag contents (grouped by category when large)
    /// → equip to hand / dive into nested bags. Tap keeps the vanilla scoreboard.
    /// </summary>
    public sealed class BagRadialFeature : IRadialFeature
    {
        public string Title => "Inventory";
        public bool Enabled => UIAConfig.BagRadialEnabled.Value;
        public KeyCode Key => UIAConfig.BagRadialKey.Value;

        public bool CanOpen() => Guards.LocalHuman != null;

        public List<RadialEntry> BuildRoot()
        {
            var entries = new List<RadialEntry>();
            var human = Guards.LocalHuman;
            if (human == null) return entries;

            AddContainer(entries, human.BackpackSlot, "Backpack");
            AddContainer(entries, human.ToolbeltSlot, "Toolbelt");
            AddContainer(entries, human.SuitSlot, "Suit");
            AddContainer(entries, human.UniformSlot, "Uniform");
            return entries;
        }

        private static void AddContainer(List<RadialEntry> entries, Slot slot, string fallbackName)
        {
            DynamicThing container = slot?.Get();
            if (container == null || container.Slots == null || container.Slots.Count == 0) return;
            int used = container.Slots.Count(s => s?.Get() != null);
            entries.Add(new RadialEntry
            {
                Label = container.DisplayName,
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

            // One stow target when holding something and the bag has room
            var hand = InventoryManager.ActiveHandSlot;
            var held = hand?.Get();
            if (held != null)
            {
                Slot free = empty.FirstOrDefault(s => Slot.AllowMove(held, s));
                if (free != null)
                {
                    entries.Add(new RadialEntry
                    {
                        Label = "Stow " + held.DisplayName,
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
            var source = new ScannedSlot { Slot = slot, Holder = bag, Location = bag.DisplayName };

            // Nested bags become branches; plain items equip to hand.
            if (occ.Slots != null && occ.Slots.Count > 0 && occ.Slots.Any(s => s?.Get() != null))
            {
                return new RadialEntry
                {
                    Label = occ.DisplayName,
                    Sublabel = "open bag",
                    Icon = occ.GetThumbnail(),
                    ChildProvider = () => BuildBagLevel(occ),
                };
            }
            return new RadialEntry
            {
                Label = occ.DisplayName,
                Sublabel = ToolbeltRadialFeature.DescribeState(occ),
                Icon = occ.GetThumbnail(),
                OnSelect = () => ItemActions.EquipToActiveHand(source),
            };
        }

        /// <summary>Tap-Tab passthrough: the vanilla scoreboard toggle we suppressed.</summary>
        public void OnTap()
        {
            if (!OwnsVanillaKey) return;
            InventoryManager.Instance?.ToggleScoreboard(false);
        }

        public bool OwnsVanillaKey => Enabled && Key == KeyMap.ShowScoreBoard;
    }
}
