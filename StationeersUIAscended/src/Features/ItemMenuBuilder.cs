using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;

namespace StationeersUIAscended.Features
{
    /// <summary>
    /// Builds the "manage this item" radial level used everywhere an item can be opened:
    /// the tool radial (hold R), toolbelt slide-outs, Tab device slide-outs, and the
    /// 1–6 equipment key radials. Entries: take-to-hand, controls (on/off, mode, open,
    /// activate), and one entry per slot — click takes/inserts, slide-out lists swap
    /// candidates from the whole accessible inventory.
    /// </summary>
    public static class ItemMenuBuilder
    {
        /// <param name="thing">The item being managed.</param>
        /// <param name="sourceSlot">Where it currently sits (null to omit the take-to-hand entry).</param>
        /// <param name="includeTakeEntry">Offer "Take to hand" (false when it is already in a hand).</param>
        public static List<RadialEntry> BuildManageEntries(DynamicThing thing, Slot sourceSlot, bool includeTakeEntry)
        {
            var entries = new List<RadialEntry>();
            if (thing == null) return entries;

            if (includeTakeEntry && sourceSlot != null)
            {
                var source = new ScannedSlot { Slot = sourceSlot, Holder = sourceSlot.Parent, Location = "" }.Pin();
                entries.Add(new RadialEntry
                {
                    Label = thing.DisplayName,
                    ActionText = "Take to hand",
                    Sublabel = ToolbeltRadialFeature.DescribeState(thing),
                    Icon = thing.GetThumbnail(),
                    AccentOverride = Theme.Good,
                    OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(thing); },
                });
            }

            AddControlEntries(entries, thing);

            if (thing.Slots != null)
            {
                foreach (Slot slot in thing.Slots)
                {
                    if (slot == null || slot.IsLocked) continue; // locked slots are a vanilla hard gate
                    entries.Add(BuildSlotEntry(slot));
                }
            }
            return entries;
        }

        /// <summary>
        /// Vanilla's own filter for "show this interaction to the player" (InventoryWindow.cs:220).
        /// Unity serializes EMPTY Interactable instances on prefabs that don't use them, so a
        /// null check alone produces phantom entries (e.g. "Activate" on a crowbar).
        /// </summary>
        public static bool IsRealControl(Interactable interactable) =>
            interactable != null && interactable.CanKeyInteract && interactable.Slot == null;

        public static void AddControlEntries(List<RadialEntry> entries, DynamicThing thing)
        {
            if (IsRealControl(thing.InteractOnOff))
            {
                // The wedge label is the ACTION (the item's name is already the ring's context).
                entries.Add(new RadialEntry
                {
                    Label = thing.OnOff ? "Turn Off" : "Turn On",
                    ActionText = thing.OnOff ? "Turn OFF" : "Turn ON",
                    Sublabel = thing.DisplayName + (thing.OnOff ? " - currently on" : " - currently off"),
                    AccentOverride = thing.OnOff ? Theme.Warn : Theme.Good,
                    OnSelect = () => ItemActions.ToggleOnOff(thing),
                });
            }
            if (IsRealControl(thing.InteractMode))
            {
                string current = null;
                try
                {
                    var modes = thing.ModeStrings;
                    if (modes != null && thing.Mode >= 0 && thing.Mode < modes.Length)
                        current = modes[thing.Mode];
                }
                catch { }
                entries.Add(new RadialEntry
                {
                    Label = "Mode",
                    ActionText = "Next mode",
                    Sublabel = current != null ? "Now: " + current : null,
                    OnSelect = () => ItemActions.CycleMode(thing),
                });
            }
            if (IsRealControl(thing.InteractOpen))
            {
                entries.Add(new RadialEntry
                {
                    Label = thing.InteractOpen.State == 1 ? "Close" : "Open",
                    ActionText = thing.InteractOpen.State == 1 ? "Close" : "Open",
                    OnSelect = () => ItemActions.ToggleInteractable(thing.InteractOpen),
                });
            }
            if (IsRealControl(thing.InteractActivate))
            {
                entries.Add(new RadialEntry
                {
                    Label = "Activate",
                    ActionText = thing.InteractActivate.State == 1 ? "Deactivate" : "Activate",
                    Sublabel = thing.InteractActivate.State == 1 ? "Active" : null,
                    OnSelect = () => ItemActions.ToggleInteractable(thing.InteractActivate),
                });
            }
        }

        /// <summary>
        /// One slot of a managed item. Click: take the occupant to hand (or insert the held
        /// item when empty). Slide-out: the swap list — every compatible item you can reach,
        /// labelled "[Item]: [Location]" with consequence warnings (proposal §7).
        /// </summary>
        public static RadialEntry BuildSlotEntry(Slot slot)
        {
            DynamicThing occ = slot.Get();
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            string slotName = string.IsNullOrEmpty(slot.DisplayName) ? slot.Type.ToString() : slot.DisplayName;

            var entry = new RadialEntry
            {
                Label = occ != null ? occ.DisplayName : slotName,
                Sublabel = occ != null ? slotName + " - " + (ToolbeltRadialFeature.DescribeState(occ) ?? "") : "(empty)",
                Icon = occ != null ? occ.GetThumbnail() : slot.SlotTypeIcon,
                SlideOutProvider = () => BuildSlotCandidateEntries(slot),
                SlideOutLabel = "Swap",
                Tag = slot,
            };

            if (occ != null)
            {
                entry.ActionText = "Take to hand";
                var source = new ScannedSlot { Slot = slot, Holder = slot.Parent, Location = slotName }.Pin();
                entry.OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(occ); };
            }
            else if (held != null && Slot.AllowMove(held, slot))
            {
                entry.ActionText = "Insert " + held.DisplayName;
                entry.AccentOverride = Theme.Accent;
                entry.FillOverride = Theme.RingStow;
                entry.OnSelect = () => ItemActions.StowActiveHandTo(slot);
            }
            else
            {
                entry.Enabled = false;
                entry.DisabledReason = held == null ? "Empty" : "Held item doesn't fit";
            }
            return entry;
        }

        /// <summary>The swap list for a slot: eject, insert-held, and every reachable compatible item.</summary>
        public static List<RadialEntry> BuildSlotCandidateEntries(Slot targetSlot)
        {
            var entries = new List<RadialEntry>();
            DynamicThing current = targetSlot.Get();
            if (current != null)
            {
                entries.Add(new RadialEntry
                {
                    Label = current.DisplayName,
                    ActionText = "Eject",
                    Sublabel = "to free hand / ground",
                    Icon = current.GetThumbnail(),
                    AccentOverride = Theme.Warn,
                    OnSelect = () => ItemActions.Eject(targetSlot),
                });
            }

            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            if (held != null && current == null && Slot.AllowMove(held, targetSlot))
            {
                entries.Add(new RadialEntry
                {
                    Label = held.DisplayName,
                    ActionText = "Insert from hand",
                    Icon = held.GetThumbnail(),
                    AccentOverride = Theme.Accent,
                    FillOverride = Theme.RingStow,
                    OnSelect = () => ItemActions.StowActiveHandTo(targetSlot),
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
                    ActionText = targetSlot.Get() != null ? "Swap in" : "Install",
                    Sublabel = c.Location,
                    Warning = InventoryScanner.ConsequenceOfRemoving(c),
                    Icon = c.Occupant.GetThumbnail(),
                    OnSelect = () => ItemActions.SwapIntoSlot(c, targetSlot),
                });
            }
            return entries;
        }

        /// <summary>Heuristic: bags are navigated (click enters), devices are taken (click) and opened (slide-out).</summary>
        public static bool LooksLikeContainer(DynamicThing thing)
        {
            if (thing?.Slots == null) return false;
            return thing.Slots.Count >= 4 && thing.InteractOnOff == null;
        }
    }

    /// <summary>Session memory of the last item type retrieved, powering "Grab another: X".</summary>
    public static class RetrievalMemory
    {
        public static int LastPrefabHash { get; private set; }
        public static string LastName { get; private set; }
        public static UnityEngine.Sprite LastIcon { get; private set; }

        public static void Record(DynamicThing thing)
        {
            if (thing == null) return;
            LastPrefabHash = thing.PrefabHash;
            LastName = thing.DisplayName;
            try { LastIcon = thing.GetThumbnail(); } catch { LastIcon = null; }
        }

        public static void Clear()
        {
            LastPrefabHash = 0;
            LastName = null;
            LastIcon = null;
        }
    }
}
