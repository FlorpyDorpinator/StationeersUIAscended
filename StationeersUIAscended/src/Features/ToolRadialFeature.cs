using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
{
    /// <summary>
    /// Phase 2+3 — hold R while holding a tool: a radial with the tool's controls
    /// (on/off, mode, open, activate) plus a Slots branch. Selecting a slot opens the
    /// compatible-item finder (proposal §7): every reachable battery/cartridge/etc.,
    /// labelled "[Item]: [Location]" with consequence warnings, swapped in atomically.
    /// </summary>
    public sealed class ToolRadialFeature : IRadialFeature
    {
        public string Title
        {
            get
            {
                var held = InventoryManager.ActiveHandSlot?.Get();
                return held != null ? held.DisplayName : "Tool";
            }
        }

        public bool Enabled => UIAConfig.ToolRadialEnabled.Value;
        public KeyCode Key => UIAConfig.ToolRadialKey.Value;

        public bool CanOpen() => InventoryManager.ActiveHandSlot?.Get() != null;

        public List<RadialEntry> BuildRoot()
        {
            var entries = new List<RadialEntry>();
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            if (held == null) return entries;

            AddControlEntries(entries, held);

            if (held.Slots != null && held.Slots.Count > 0)
            {
                entries.Add(new RadialEntry
                {
                    Label = "Slots",
                    Sublabel = held.Slots.Count + " slot(s)",
                    ChildProvider = () => BuildSlotEntries(held),
                });
            }
            return entries;
        }

        private static void AddControlEntries(List<RadialEntry> entries, DynamicThing held)
        {
            if (held.InteractOnOff != null)
            {
                entries.Add(new RadialEntry
                {
                    Label = held.OnOff ? "Turn Off" : "Turn On",
                    Sublabel = held.OnOff ? "Currently on" : "Currently off",
                    AccentOverride = held.OnOff ? Theme.Warn : Theme.Good,
                    OnSelect = () => ItemActions.ToggleOnOff(held),
                });
            }
            if (held.InteractMode != null)
            {
                string current = null;
                try
                {
                    var modes = held.ModeStrings;
                    if (modes != null && held.Mode >= 0 && held.Mode < modes.Length)
                        current = modes[held.Mode];
                }
                catch { }
                entries.Add(new RadialEntry
                {
                    Label = "Mode",
                    Sublabel = current != null ? "Now: " + current : null,
                    OnSelect = () => ItemActions.CycleMode(held),
                });
            }
            if (held.InteractOpen != null)
            {
                entries.Add(new RadialEntry
                {
                    Label = held.InteractOpen.State == 1 ? "Close" : "Open",
                    OnSelect = () => ItemActions.ToggleInteractable(held.InteractOpen),
                });
            }
            if (held.InteractActivate != null)
            {
                entries.Add(new RadialEntry
                {
                    Label = "Activate",
                    Sublabel = held.InteractActivate.State == 1 ? "Active" : null,
                    OnSelect = () => ItemActions.ToggleInteractable(held.InteractActivate),
                });
            }
        }

        private static List<RadialEntry> BuildSlotEntries(DynamicThing held)
        {
            var entries = new List<RadialEntry>();
            if (held?.Slots == null) return entries;
            foreach (Slot slot in held.Slots)
            {
                if (slot == null) continue;
                Slot target = slot;
                DynamicThing occ = slot.Get();
                entries.Add(new RadialEntry
                {
                    Label = string.IsNullOrEmpty(slot.DisplayName) ? slot.Type.ToString() : slot.DisplayName,
                    Sublabel = occ != null ? occ.DisplayName : "(empty)",
                    Icon = occ != null ? occ.GetThumbnail() : slot.SlotTypeIcon,
                    ChildProvider = () => BuildCandidateEntries(target),
                });
            }
            return entries;
        }

        /// <summary>The finder level: all compatible items, plus Eject for the current occupant.</summary>
        private static List<RadialEntry> BuildCandidateEntries(Slot targetSlot)
        {
            var entries = new List<RadialEntry>();
            DynamicThing current = targetSlot.Get();
            if (current != null)
            {
                entries.Add(new RadialEntry
                {
                    Label = "Eject " + current.DisplayName,
                    Sublabel = ToolbeltRadialFeature.DescribeState(current),
                    Icon = current.GetThumbnail(),
                    AccentOverride = Theme.Warn,
                    OnSelect = () => ItemActions.Eject(targetSlot),
                });
            }

            var candidates = InventoryScanner.FindCompatible(
                targetSlot, UIAConfig.ScanDepth.Value, UIAConfig.AllowToolSlotSources.Value);
            foreach (var candidate in candidates)
            {
                var c = candidate;
                entries.Add(new RadialEntry
                {
                    Label = c.Occupant.DisplayName,
                    Sublabel = c.Location,
                    Warning = InventoryScanner.ConsequenceOfRemoving(c),
                    Icon = c.Occupant.GetThumbnail(),
                    OnSelect = () => ItemActions.SwapIntoSlot(c, targetSlot),
                });
            }
            return entries;
        }

        /// <summary>
        /// Tap-R passthrough. Our Harmony prefix suppresses the vanilla poll for the active
        /// hand while we own R, so short taps re-dispatch the vanilla action here: toggling
        /// the active-hand item's inventory window.
        /// </summary>
        public void OnTap()
        {
            if (!OwnsVanillaKey) return;
            var slot = InventoryManager.ActiveHandSlot;
            var button = slot?.Button;
            if (slot?.Get() != null && button != null)
                button.PrimaryAction(true);
        }

        /// <summary>True when we suppress the vanilla ActiveHandSlot handling for our key.</summary>
        public bool OwnsVanillaKey =>
            Enabled && UIAConfig.ToolRadialTakeOverVanillaKey.Value && Key == KeyMap.ActiveHandSlot;
    }
}
