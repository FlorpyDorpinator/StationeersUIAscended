using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
{
    /// <summary>
    /// Phase 1 — the flagship: hold Middle Mouse, flick to a tool on your belt, release to
    /// equip it into the active hand (vanilla-style swap when the hand is occupied).
    /// Empty belt slots optionally appear as stow targets for the held item.
    /// </summary>
    public sealed class ToolbeltRadialFeature : IRadialFeature
    {
        public string Title => "Toolbelt";
        public bool Enabled => UIAConfig.ToolbeltRadialEnabled.Value;
        public KeyCode Key => UIAConfig.ToolbeltRadialKey.Value;

        public bool CanOpen()
        {
            var human = Guards.LocalHuman;
            return human?.ToolbeltSlot?.Get() != null;
        }

        public List<RadialEntry> BuildRoot()
        {
            var entries = new List<RadialEntry>();
            var human = Guards.LocalHuman;
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
                    var source = new ScannedSlot { Slot = slot, Holder = belt, Location = "Toolbelt" };
                    bool canEquip = hand != null &&
                        (hand.Get() == null ? Slot.AllowMove(occ, hand) : Slot.AllowSwap(slot, hand));
                    entries.Add(new RadialEntry
                    {
                        Label = occ.DisplayName,
                        Sublabel = DescribeState(occ),
                        Icon = occ.GetThumbnail(),
                        Enabled = canEquip,
                        DisabledReason = canEquip ? null : "Can't equip",
                        OnSelect = () => ItemActions.EquipToActiveHand(source),
                    });
                }
                else if (UIAConfig.ToolbeltShowStowEntries.Value && held != null && Slot.AllowMove(held, slot))
                {
                    Slot target = slot;
                    entries.Add(new RadialEntry
                    {
                        Label = "Stow " + held.DisplayName,
                        Sublabel = slot.DisplayName,
                        Icon = held.GetThumbnail(),
                        AccentOverride = Theme.Good,
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
                    if (thing.InteractOnOff != null && thing.OnOff) state += " · On";
                }
                else if (thing.InteractOnOff != null)
                {
                    state = thing.OnOff ? "On" : "Off";
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(qty) && !string.IsNullOrEmpty(state)) return Strip(qty) + " · " + state;
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
