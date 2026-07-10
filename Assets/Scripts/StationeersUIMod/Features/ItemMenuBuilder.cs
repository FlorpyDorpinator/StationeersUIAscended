using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Builds the "manage this item" radial level used everywhere an item can be opened:
    /// the tool radial (hold R), toolbelt slide-outs, Tab device slide-outs, and the
    /// 1–6 equipment key radials.
    ///
    /// Option A: controls (on/off, mode, scroll-adjustable values) + slots; empty slots
    /// are STOW wedges (blank slot + "STOW", the held item previews on hover); occupied
    /// slots slide out to TAKE / OPEN. Big devices (hardsuit) branch into SETTINGS and
    /// SLOTS. The old swap-with-everything satellite is GONE in Option A.
    /// Option D keeps the classic behavior (click takes/inserts, slide-out = swap list).
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
                    StateText = StateText.For(thing),
                    Icon = thing.GetThumbnail(),
                    AccentOverride = Theme.Good,
                    DragSource = source,
                    OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(thing); },
                });
            }

            var controls = new List<RadialEntry>();
            AddControlEntries(controls, thing);
            if (UIAConfig.IsA) DeviceControls.AddValueControls(controls, thing);

            var slotEntries = new List<RadialEntry>();
            if (thing.Slots != null)
            {
                foreach (Slot slot in thing.Slots)
                {
                    if (slot == null || slot.IsLocked) continue; // locked slots are a vanilla hard gate
                    var e = BuildSlotEntry(slot);
                    if (e != null) slotEntries.Add(e);
                }
            }

            // Option A: a hardsuit-sized device would be a 14-wedge soup — branch into
            // SETTINGS and SLOTS instead ("slide over the hardsuit body: settings | slots").
            if (UIAConfig.IsA && controls.Count >= 2 && slotEntries.Count >= 2
                && controls.Count + slotEntries.Count > 6)
            {
                var t = thing;
                entries.Add(new RadialEntry
                {
                    Label = "Settings",
                    ActionText = "Open",
                    Sublabel = controls.Count + " control(s)",
                    ChildProvider = () =>
                    {
                        var l = new List<RadialEntry>();
                        AddControlEntries(l, t);
                        DeviceControls.AddValueControls(l, t);
                        return l;
                    },
                });
                entries.Add(new RadialEntry
                {
                    Label = "Slots",
                    ActionText = "Open",
                    Sublabel = slotEntries.Count + " slot(s)",
                    ChildProvider = () => BuildSlotsLevel(t),
                });
            }
            else
            {
                entries.AddRange(controls);
                entries.AddRange(slotEntries);
            }
            return entries;
        }

        private static List<RadialEntry> BuildSlotsLevel(DynamicThing thing)
        {
            var entries = new List<RadialEntry>();
            if (thing?.Slots == null) return entries;
            foreach (Slot slot in thing.Slots)
            {
                if (slot == null || slot.IsLocked) continue;
                var e = BuildSlotEntry(slot);
                if (e != null) entries.Add(e);
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

        /// <summary>Does this thing offer anything beyond take-to-hand (controls or slots)?</summary>
        public static bool HasInnards(DynamicThing thing)
        {
            if (thing == null) return false;
            if (thing.Slots != null && thing.Slots.Count > 0) return true;
            return IsRealControl(thing.InteractOnOff) || IsRealControl(thing.InteractMode)
                || IsRealControl(thing.InteractOpen) || IsRealControl(thing.InteractActivate);
        }

        public static void AddControlEntries(List<RadialEntry> entries, DynamicThing thing)
        {
            if (IsRealControl(thing.InteractOnOff))
            {
                // Option A: the wedge names the CURRENT state ("the child radial says ON"),
                // clicking flips it. Option D: the wedge names the action.
                bool on = thing.OnOff;
                entries.Add(new RadialEntry
                {
                    Label = UIAConfig.IsA ? (on ? "On" : "Off") : (on ? "Turn Off" : "Turn On"),
                    ActionText = on ? "Turn OFF" : "Turn ON",
                    Sublabel = thing.DisplayName + (on ? " - currently on" : " - currently off"),
                    AccentOverride = on ? Theme.Warn : Theme.Good,
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
                    StateText = UIAConfig.IsA ? current : null,
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

        /// <summary>One slot of a managed item — dispatches on the active schema.</summary>
        public static RadialEntry BuildSlotEntry(Slot slot)
            => UIAConfig.IsA ? BuildSlotEntryA(slot) : BuildSlotEntryClassic(slot);

        // ---------- Option A ----------

        private static RadialEntry BuildSlotEntryA(Slot slot)
        {
            DynamicThing occ = slot.Get();
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            string slotName = string.IsNullOrEmpty(slot.DisplayName) ? slot.Type.ToString() : slot.DisplayName;

            if (occ == null)
                return BuildStowEntry(slot, slotName, held);

            var source = new ScannedSlot { Slot = slot, Holder = slot.Parent, Location = slotName }.Pin();
            var thing = occ;
            var entry = new RadialEntry
            {
                Label = occ.DisplayName,
                Sublabel = slotName,
                StateText = StateText.For(occ),
                Icon = occ.GetThumbnail(),
                DragSource = source,
                Tag = slot,
            };

            if (LooksLikeContainer(occ))
            {
                // Nested bag: click OPENS it (the bag becomes the main radial, RMB backtracks);
                // the slide-out offers stow-from-hand / take-that-bag.
                entry.ActionText = "Open";
                entry.Sublabel = slotName + " - " + CountSlots(occ);
                entry.ChildProvider = () => BagRadialFeature.BuildBagLevel(thing);
                entry.SlideOutProvider = () => BuildTakeOrStowEntries(thing, source);
                entry.SlideOutLabel = "More";
                entry.DropResolver = dragged => FirstFreeSlot(thing, dragged);
            }
            else
            {
                entry.ActionText = "Take to hand";
                entry.OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(thing); };
                if (HasInnards(occ))
                {
                    // "Swipe over an object: take it, or open it if it is openable."
                    entry.SlideOutProvider = () => BuildTakeOpenEntries(thing, source);
                    entry.SlideOutLabel = "Open";
                }
            }
            return entry;
        }

        /// <summary>The Option A STOW wedge: blank slot icon + "STOW"; hovering previews the
        /// held item on the orange fill. Also a drop target for parked items.</summary>
        public static RadialEntry BuildStowEntry(Slot slot, string slotName, DynamicThing held)
        {
            bool canStow = held != null && Slot.AllowMove(held, slot);
            Slot target = slot;
            return new RadialEntry
            {
                Label = "Stow",
                Sublabel = slotName + " (empty)",
                ActionText = canStow ? "Stow " + held.DisplayName : null,
                Icon = slot.SlotTypeIcon,
                HoverIcon = canStow ? held.GetThumbnail() : null,
                StowStyle = true,
                Enabled = canStow,
                DisabledReason = held == null ? "Nothing in hand" : "Held item doesn't fit",
                DropSlot = target,
                OnSelect = () => ItemActions.StowActiveHandTo(target),
                Tag = slot,
            };
        }

        /// <summary>Satellite for a non-container item: TAKE, and OPEN as a branch — clicking
        /// OPEN promotes the item's manage level to the main radial (RMB backtracks).</summary>
        public static List<RadialEntry> BuildTakeOpenEntries(DynamicThing thing, ScannedSlot source)
        {
            var entries = new List<RadialEntry>
            {
                new RadialEntry
                {
                    Label = "Take",
                    ActionText = "Take to hand",
                    Icon = thing.GetThumbnail(),
                    StateText = StateText.For(thing),
                    DragSource = source,
                    OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(thing); },
                },
                new RadialEntry
                {
                    Label = "Open",
                    ActionText = "Open " + thing.DisplayName,
                    ChildProvider = () => BuildManageEntries(thing, source.Slot, includeTakeEntry: false),
                },
            };
            return entries;
        }

        /// <summary>Satellite for a nested bag: stow the held item into it / take the bag.</summary>
        public static List<RadialEntry> BuildTakeOrStowEntries(DynamicThing bag, ScannedSlot source)
        {
            var entries = new List<RadialEntry>();
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            if (held != null)
            {
                Slot free = FirstFreeSlot(bag, held);
                entries.Add(new RadialEntry
                {
                    Label = "Stow",
                    ActionText = "Stow " + held.DisplayName,
                    Sublabel = "into " + bag.DisplayName,
                    Icon = held.GetThumbnail(),
                    StowStyle = true,
                    Enabled = free != null,
                    DisabledReason = free == null ? "No room" : null,
                    OnSelect = () => { var f = FirstFreeSlot(bag, InventoryManager.ActiveHandSlot?.Get()); if (f != null) ItemActions.StowActiveHandTo(f); },
                });
            }
            entries.Add(new RadialEntry
            {
                Label = "Take",
                ActionText = "Take to hand",
                Sublabel = bag.DisplayName,
                Icon = bag.GetThumbnail(),
                DragSource = source,
                OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(bag); },
            });
            return entries;
        }

        /// <summary>First unlocked empty slot of <paramref name="bag"/> that accepts the item
        /// (checked again with Slot.AllowMove at execute time by the actual mutation).</summary>
        public static Slot FirstFreeSlot(DynamicThing bag, DynamicThing item)
        {
            if (bag?.Slots == null || item == null) return null;
            foreach (Slot s in bag.Slots)
            {
                if (s == null || s.IsLocked || s.Get() != null) continue;
                if (Slot.AllowMove(item, s)) return s;
            }
            return null;
        }

        private static string CountSlots(DynamicThing bag)
        {
            int used = 0, total = 0;
            foreach (Slot s in bag.Slots)
            {
                if (s == null) continue;
                total++;
                if (s.Get() != null) used++;
            }
            return used + "/" + total;
        }

        // ---------- Option D (classic) ----------

        /// <summary>
        /// One slot of a managed item, classic semantics. Click: take the occupant to hand
        /// (or insert the held item when empty). Slide-out: the swap list — every compatible
        /// item you can reach, labelled "[Item]: [Location]" with consequence warnings.
        /// </summary>
        private static RadialEntry BuildSlotEntryClassic(Slot slot)
        {
            DynamicThing occ = slot.Get();
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            string slotName = string.IsNullOrEmpty(slot.DisplayName) ? slot.Type.ToString() : slot.DisplayName;

            var entry = new RadialEntry
            {
                Label = occ != null ? occ.DisplayName : slotName,
                Sublabel = occ != null ? slotName + " - " + (ToolbeltRadialFeature.DescribeState(occ) ?? "") : "(empty)",
                StateText = occ != null ? StateText.For(occ) : null,
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
