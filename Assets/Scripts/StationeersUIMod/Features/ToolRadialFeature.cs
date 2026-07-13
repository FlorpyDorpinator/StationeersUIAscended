using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Hold R while holding a tool: its controls (on/off, mode, open, activate) and one
    /// entry per slot. Clicking a slot takes/inserts; sliding out on a slot opens the
    /// swap list (every reachable compatible item, with consequence warnings — the
    /// welder-battery flow from proposal §14.2 in two gestures).
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
        public bool OpenOnTap => false;
        /// <summary>TAP R opens the in-hand item's radial LATCHED (sticky — it persists until
        /// closed or an action is picked); HOLD R opens it transient (release to act). Either
        /// way OnTap never runs, so R no longer toggles the vanilla active-hand inventory
        /// window (its poll is already suppressed while we own the key — see OwnsVanillaKey).</summary>
        public bool OpensOnBoth => true;

        public bool CanOpen() => InventoryManager.ActiveHandSlot?.Get() != null;

        public List<RadialEntry> BuildRoot()
        {
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            if (held == null) return new List<RadialEntry>();
            // Already in hand, so no take-to-hand entry.
            return ItemMenuBuilder.BuildManageEntries(held, InventoryManager.ActiveHandSlot, includeTakeEntry: false);
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

        public void OnHold() { }

        /// <summary>True when we suppress the vanilla ActiveHandSlot handling for our key.</summary>
        public bool OwnsVanillaKey =>
            Enabled && UIAConfig.ToolRadialTakeOverVanillaKey.Value && Key == KeyMap.ActiveHandSlot;
    }
}

