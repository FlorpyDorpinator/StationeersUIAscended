using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The flagship: hold Middle Mouse, flick to a tool on your belt, release to equip it
    /// into the active hand (vanilla-style swap when the hand is occupied). Empty belt
    /// slots are always shown so you can put a tool back. Sliding out past the rim on a
    /// tool opens its controls/slots as a satellite ring.
    /// </summary>
    public sealed class ToolbeltRadialFeature : IRadialFeature
    {
        public string Title => "Toolbelt";
        public bool Enabled => UIAConfig.ToolbeltRadialEnabled.Value;
        public KeyCode Key => UIAConfig.ToolbeltRadialKey.Value;
        public bool OpenOnTap => false;
        /// <summary>Option B: TAP opens this radial sticky, HOLD opens it transient.</summary>
        public bool OpensOnBoth => UIAConfig.IsB;

        public bool CanOpen()
        {
            var human = Guards.LocalHuman;
            if (human == null) return false;
            // Option B: The Hub makes the radial useful even with no toolbelt worn.
            return UIAConfig.IsB || human.ToolbeltSlot?.Get() != null;
        }

        public List<RadialEntry> BuildRoot()
        {
            var entries = new List<RadialEntry>();
            var human = Guards.LocalHuman;

            // Option B: THE HUB — wedge 0 sits top-center; it branches into everything the
            // Tab radial opens (search + every worn piece). Hold mode enters it by dwell
            // or LMB; sticky mode by tap. Text-only wedge, accent rim.
            if (UIAConfig.IsB)
            {
                entries.Add(new RadialEntry
                {
                    Label = "The Hub",
                    ActionText = "Enter",
                    Sublabel = "inventory + search",
                    AccentOverride = Theme.Accent,
                    ChildProvider = BagRadialFeature.BuildHubRoot,
                    Tag = RadialMenu.HubTag,
                });
            }

            DynamicThing belt = human?.ToolbeltSlot?.Get();
            if (belt == null || belt.Slots == null) return entries;

            Slot hand = InventoryManager.ActiveHandSlot;
            DynamicThing held = hand?.Get();

            foreach (Slot slot in belt.Slots)
            {
                if (slot == null) continue;
                DynamicThing occ = slot.Get();
                if (occ != null)
                {
                    var source = new ScannedSlot { Slot = slot, Holder = belt, Location = "Toolbelt" }.Pin();
                    bool handEmpty = hand != null && hand.Get() == null;
                    bool canEquip = hand != null &&
                        (handEmpty ? Slot.AllowMove(occ, hand) : Slot.AllowSwap(slot, hand));
                    var thing = occ;
                    entries.Add(new RadialEntry
                    {
                        Label = occ.DisplayName,
                        ActionText = handEmpty ? "Equip" : "Swap into hand",
                        Sublabel = DescribeState(occ),
                        StateText = StateText.For(occ),
                        Icon = occ.GetThumbnail(),
                        Enabled = canEquip,
                        DisabledReason = canEquip ? null : "Can't equip",
                        DragSource = source,
                        OnSelect = () => ItemActions.EquipToActiveHand(source),
                        SlideOutProvider = () => ItemMenuBuilder.BuildManageEntries(thing, slot, includeTakeEntry: false),
                        SlideOutLabel = ItemMenuBuilder.HasInnards(occ) ? "Open" : "Split", // a stack swipes to its splits
                    });
                }
                else if (UIAConfig.ToolbeltShowStowEntries.Value)
                {
                    if (UIAConfig.IsA)
                    {
                        // Option A STOW wedge: blank slot + "STOW"; the held item previews
                        // on hover with the orange fill.
                        entries.Add(ItemMenuBuilder.BuildStowEntry(slot,
                            string.IsNullOrEmpty(slot.DisplayName) ? "Belt" : slot.DisplayName, held));
                        continue;
                    }
                    Slot target = slot;
                    bool canStow = held != null && Slot.AllowMove(held, slot);
                    entries.Add(new RadialEntry
                    {
                        Label = string.IsNullOrEmpty(slot.DisplayName) ? "Empty" : slot.DisplayName,
                        ActionText = canStow ? "Stow " + held.DisplayName : null,
                        Sublabel = "(empty)",
                        Icon = canStow ? held.GetThumbnail() : slot.SlotTypeIcon,
                        Enabled = canStow,
                        DisabledReason = held == null ? "Nothing in hand" : "Held item doesn't fit",
                        AccentOverride = canStow ? Theme.Accent : (uint?)null,
                        FillOverride = canStow ? Theme.RingStow : (uint?)null, // orange = "held item goes here"
                        OnSelect = () => ItemActions.StowActiveHandTo(target),
                    });
                }
            }
            return entries;
        }

        public void OnTap()
        {
            // Middle mouse has no vanilla action in this build (PingHighlight is unbound); nothing to re-dispatch.
        }

        public void OnHold() { }

        internal static string DescribeState(DynamicThing thing)
        {
            if (thing == null) return null;
            string qty = null;
            try { qty = thing.GetQuantityText(); } catch { }
            string state = null;
            try
            {
                if (thing is PowerTool pt)
                {
                    var cell = pt.Battery;
                    state = cell == null ? "No battery" : cell.CurrentPowerPercentage + "%";
                    if (ItemMenuBuilder.IsRealControl(thing.InteractOnOff) && thing.OnOff) state += " - On";
                }
                else if (ItemMenuBuilder.IsRealControl(thing.InteractOnOff))
                {
                    state = thing.OnOff ? "On" : "Off";
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(qty) && !string.IsNullOrEmpty(state)) return Strip(qty) + " - " + state;
            if (!string.IsNullOrEmpty(state)) return state;
            return Strip(qty);
        }

        /// <summary>GetQuantityText can contain TMP rich-text tags; ImGui renders them literally.</summary>
        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int lt;
            while ((lt = s.IndexOf('<')) >= 0)
            {
                int gt = s.IndexOf('>', lt);
                if (gt < 0) break;
                s = s.Remove(lt, gt - lt + 1);
            }
            return s;
        }
    }
}

