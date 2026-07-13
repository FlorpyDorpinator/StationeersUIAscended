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

            // A stack of more than one gets a SPLIT branch (peel items off into a hand).
            if (CanSplit(thing))
            {
                var stackThing = thing;
                int qty = (thing as Assets.Scripts.Objects.Items.Stackable).Quantity;
                entries.Add(new RadialEntry
                {
                    Label = "Split",
                    ActionText = "Split stack",
                    Sublabel = "x" + qty,
                    ChildProvider = () => BuildSplitLevel(stackThing),
                });
            }

            var controls = BuildControlsList(thing);
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

            // Option A rule (parity everywhere): a SETTINGS wedge whenever the item has more
            // than two controls, or any controls alongside slot wedges (a backpack shows
            // SETTINGS + its slots, never three settings mixed into the item grid). One or
            // two controls with no slots stay inline (a canister just says OPEN).
            if (UIAConfig.IsA && UseSettingsWedge(controls.Count, slotEntries.Count))
            {
                entries.Add(BuildSettingsWedge(thing, controls.Count));
            }
            else
            {
                entries.AddRange(controls);
            }
            entries.AddRange(slotEntries);
            return entries;
        }

        /// <summary>The controls level: every setting as its own wedge. Option A enumerates
        /// generically (everything vanilla's inventory window would show, with vanilla's
        /// live state-baked labels); Option D keeps the classic hand-picked four.</summary>
        public static List<RadialEntry> BuildControlsList(DynamicThing thing)
        {
            if (thing == null) return new List<RadialEntry>();
            if (UIAConfig.IsA) return DeviceControls.BuildEntries(thing);
            var controls = new List<RadialEntry>();
            AddControlEntries(controls, thing);
            return controls;
        }

        public static bool UseSettingsWedge(int controlCount, int slotWedgeCount)
            => controlCount > 2 || (controlCount > 0 && slotWedgeCount > 0);

        public static RadialEntry BuildSettingsWedge(DynamicThing thing, int controlCount)
        {
            var t = thing;
            return new RadialEntry
            {
                Label = "Settings",
                ActionText = "Open",
                Sublabel = controlCount + " setting(s)",
                ChildProvider = () => BuildControlsList(t),
            };
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
            if (UIAConfig.IsA)
            {
                // Match the generic enumeration, or wedges and satellites disagree.
                if (thing.Interactables != null)
                    foreach (var i in thing.Interactables)
                        if (IsRealControl(i)) return true;
                return false;
            }
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
                // Swipe over a slotted component (battery, canister, cartridge, tool...):
                // TAKE / REPLACE (+ its settings, per the settings-wedge rule).
                entry.SlideOutProvider = () => BuildComponentSatellite(thing, source);
                entry.SlideOutLabel = "Options";
            }
            return entry;
        }

        /// <summary>The Option A STOW wedge: blank slot icon + "STOW"; hovering previews the
        /// held item on the orange fill. Also a drop target for parked items.</summary>
        public static RadialEntry BuildStowEntry(Slot slot, string slotName, DynamicThing held)
        {
            bool canStow = held != null && Slot.AllowMove(held, slot);
            Slot target = slot;
            // Bug 3: swiping past the rim on an empty TYPED slot (a tool's battery/filter
            // slot, a suit's canister slot) opens the list of items you could install there
            // — the same eject/insert/swap candidates the classic slot wedge offers, even
            // with an empty hand. Skipped for generic (None) storage slots, whose candidate
            // list would be your entire inventory.
            bool typed = slot.Type != Slot.Class.None || slot.SpecificTypePrefabHash != -1;
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
                SlideOutProvider = typed ? () => BuildSlotCandidateEntries(target) : (System.Func<List<RadialEntry>>)null,
                SlideOutLabel = "Install",
                Tag = slot,
            };
        }

        /// <summary>
        /// Satellite for a slotted component (battery in a suit, canister in a tank slot,
        /// cartridge in a tablet...): TAKE / REPLACE / its settings. REPLACE branches into
        /// the swap list — every compatible item you can reach, with consequence warnings.
        /// Settings follow the shared rule: 1-2 controls inline (a canister just shows
        /// OPEN/CLOSE), more (or controls plus own slots) collapse into a SETTINGS wedge.
        /// Clicking any branch promotes it to the main radial; RMB backtracks.
        /// </summary>
        public static List<RadialEntry> BuildComponentSatellite(DynamicThing thing, ScannedSlot source)
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
                    Label = "Replace",
                    ActionText = "Replace",
                    Sublabel = "swap with another",
                    ChildProvider = () => BuildSlotCandidateEntries(source.Slot),
                },
            };

            var controls = BuildControlsList(thing);
            bool hasOwnSlots = thing.Slots != null && thing.Slots.Count > 0;
            if (UseSettingsWedge(controls.Count, hasOwnSlots ? 1 : 0))
                entries.Add(BuildSettingsWedge(thing, controls.Count));
            else
                entries.AddRange(controls);

            if (hasOwnSlots)
            {
                entries.Add(new RadialEntry
                {
                    Label = "Open",
                    ActionText = "Open " + thing.DisplayName,
                    ChildProvider = () => BuildManageEntries(thing, source.Slot, includeTakeEntry: false),
                });
            }
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
                    StateText = StateText.For(current),
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
                    StateText = StateText.For(held),
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
                    // The stat every candidate carries (canister kPa, battery %, filter/dirt %)
                    // so a swap/Replace list shows what you're choosing BETWEEN, not just names.
                    StateText = StateText.For(c.Occupant),
                    Warning = InventoryScanner.ConsequenceOfRemoving(c),
                    Icon = c.Occupant.GetThumbnail(),
                    OnSelect = () => ItemActions.SwapIntoSlot(c, targetSlot),
                });
            }
            return entries;
        }

        /// <summary>A stack with more than one item can be split.</summary>
        public static bool CanSplit(DynamicThing thing)
        {
            var s = thing as Assets.Scripts.Objects.Items.Stackable;
            return s != null && s.Quantity > 1;
        }

        /// <summary>The split level: SPLIT ONE and SPLIT HALF (both vanilla's own MP-safe
        /// interactions), plus — host/single-player only — SPLIT COUNT with a scroll wheel
        /// that sets how many to peel off. Scrolling only changes the number between the
        /// triangles; the CLICK does the split. (Arbitrary count has no networked vanilla
        /// path, so the count wedge is hidden on multiplayer clients — never a client-side
        /// Quantity mutation.)</summary>
        public static List<RadialEntry> BuildSplitLevel(DynamicThing thing)
        {
            var entries = new List<RadialEntry>();
            var s = thing as Assets.Scripts.Objects.Items.Stackable;
            if (s == null) return entries;
            var t = thing;

            entries.Add(new RadialEntry
            {
                Label = "Split one",
                ActionText = "Take 1 to hand",
                Sublabel = "peel one off",
                Icon = t.GetThumbnail(),
                OnSelect = () => ItemActions.SplitStack(t, half: false),
            });
            entries.Add(new RadialEntry
            {
                Label = "Split half",
                ActionText = "Take half to hand",
                Sublabel = "split the stack in two",
                Icon = t.GetThumbnail(),
                OnSelect = () => ItemActions.SplitStack(t, half: true),
            });
            if (ItemActions.CanSplitCount(t))
            {
                int max = System.Math.Max(1, s.Quantity - 1);
                var box = new int[] { UnityEngine.Mathf.Clamp(max / 2, 1, max) };
                entries.Add(new RadialEntry
                {
                    Label = "Split count",
                    ActionText = "Split off this many",
                    Sublabel = "scroll to choose",
                    OnScroll = d => box[0] = UnityEngine.Mathf.Clamp(box[0] + d, 1, max),
                    ValueText = () => box[0].ToString(),
                    OnSelect = () => ItemActions.SplitStackCount(t, box[0]),
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
