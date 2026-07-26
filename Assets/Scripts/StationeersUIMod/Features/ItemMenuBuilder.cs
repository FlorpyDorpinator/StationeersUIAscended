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

            // A sealed package (cereal / water / kit box) is unpack-only: no bag view, no slot
            // wedges, no controls. Take-to-hand (above, when applicable) plus one Unpack action.
            if (IsUnpackBox(thing))
            {
                entries.AddRange(BuildUnpackLevel(thing));
                return entries;
            }

            // A PLAIN stack (no storage slots) has nothing to manage but splitting — put the
            // split choices straight on the ring (Split one / half / count) instead of an extra
            // "Split" branch you'd have to click through. Swiping a stack lands on them directly.
            if (IsPlainStack(thing))
            {
                entries.AddRange(BuildSplitLevel(thing));
                return entries;
            }

            // A stack that ALSO has storage keeps a SPLIT branch alongside its slots.
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

            // Option A rule (parity everywhere): a SETTINGS wedge only when the item has MORE
            // than two controls — one or two settings always go straight into the radial, even
            // alongside slot wedges (sensor lenses just say "On"; a welder shows On + its slots).
            // Three or more controls still collapse into a SETTINGS wedge so the item grid never
            // drowns in toggles.
            if (UIAConfig.IsA && UseSettingsWedge(controls.Count, slotEntries.Count))
            {
                entries.Add(BuildSettingsWedge(thing, controls.Count));
            }
            else
            {
                entries.AddRange(controls);
            }
            entries.AddRange(slotEntries);
            var sort = BuildSortEntry(thing); // #6: Sort wedge for a jetpack/backpack's storage
            if (sort != null) entries.Add(sort);
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

        /// <summary>Collapse controls into a SETTINGS wedge only past two of them. One or two
        /// settings stay inline in the radial regardless of how many slot wedges sit beside
        /// them (<paramref name="slotWedgeCount"/> is kept for call-site clarity/history).</summary>
        public static bool UseSettingsWedge(int controlCount, int slotWedgeCount)
            => controlCount > 2;

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
            if (IsUnpackBox(thing)) return false; // a sealed package's slots are not user-openable innards
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

        /// <summary>#10 — COLOUR classification only. True when this slot is a real component
        /// SOCKET (a functional part the parent device consumes), as opposed to a Tool/Belt/Ore/
        /// None slot that merely HOLDS an item. Only sockets get the DeviceSlotBorderColor edge;
        /// this is an explicit allow-list rather than a "not-None" test, because a tool-belt Tool
        /// slot is typed yet is a holder — the old predicate painted every occupied tool wedge
        /// bright blue. Does NOT affect the empty-slot Install swipe (:301) or Replace satellite
        /// (:355), which keep the broader typed-slot gate on purpose.</summary>
        private static bool IsComponentSocket(Slot slot)
        {
            if (slot == null) return false;
            switch (slot.Type)
            {
                case Slot.Class.GasCanister:
                case Slot.Class.GasFilter:
                case Slot.Class.Battery:
                case Slot.Class.Cartridge:
                case Slot.Class.Motherboard:
                case Slot.Class.Circuitboard:
                case Slot.Class.DataDisk:
                case Slot.Class.LiquidCanister:
                case Slot.Class.DirtCanister:
                case Slot.Class.SensorProcessingUnit:
                case Slot.Class.Organ:
                case Slot.Class.ProgrammableChip:
                    return true;
                default:
                    return false;
            }
        }

        private static RadialEntry BuildSlotEntryA(Slot slot)
        {
            DynamicThing occ = slot.Get();
            DynamicThing held = InventoryManager.ActiveHandSlot?.Get();
            string slotName = string.IsNullOrEmpty(slot.DisplayName) ? slot.Type.ToString() : slot.DisplayName;

            if (occ == null)
                return BuildStowEntry(slot, slotName, held);

            var source = new ScannedSlot { Slot = slot, Holder = slot.Parent, Location = slotName }.Pin();
            // Sealed package sitting in a slot: click takes the box to hand, child = Unpack only.
            if (IsUnpackBox(occ)) return BuildUnpackBoxEntry(occ, source);
            var thing = occ;
            var entry = new RadialEntry
            {
                Label = occ.DisplayName,
                Sublabel = slotName,
                StateText = StateText.For(occ),
                Icon = occ.GetThumbnail(),
                DragSource = source,
                Tag = slot,
                // A real component SOCKET (propellant canister, battery, filter, cartridge…) is a
                // device's functional slot, not generic storage — its occupant is "in use by the
                // device", so the wedge wears the DeviceSlotBorderColor edge. #10: this is an
                // ALLOW-LIST of socket classes, NOT a "not-None" test — a tool-belt Tool slot is
                // typed but is a holder, not a socket, and must keep the plain wedge border.
                DeviceSlotStyle = IsComponentSocket(slot),
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
                if (IsBindableBag(occ)) entry.BindableBag = thing; // Ctrl+number bindable (#3)
            }
            else
            {
                entry.ActionText = "Take to hand";
                entry.OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(thing); };
                // Swipe over a slotted component (battery, canister, cartridge, tool...):
                // TAKE / REPLACE (+ its settings). A stack instead swipes to its split choices.
                entry.SlideOutProvider = () => BuildComponentSatellite(thing, source);
                entry.SlideOutLabel = IsPlainStack(occ) ? "Split" : "Options";
                // Accept a DROP that SWAPS. An occupied component slot (a suit's battery, a tank's
                // canister…) used to be a drag SOURCE only, so dragging a replacement from the hand
                // ONTO it did nothing — while the reverse (slot -> hand) worked, because the hand box
                // is a drop zone. Return this slot when the dragged item may land here; the radial's
                // drop handler then calls SwapIntoSlot (slot-sourced; occupied branch = OnServer.
                // SwapSlots) or WorldDragTo (world-sourced; vanilla's insert/merge/swap-to-world
                // ladder) — every gate re-checked at execute time, MP-safe. A matching stack merges
                // first (TryStackMerge), and an incompatible item resolves to null here, so the wedge
                // only lights green for a real landing. Not on the container branch above: a bag
                // takes drops by NESTING them (FirstFreeSlot), not swapping.
                // PLAY-TEST FIX (2026-07-26 round 2): a WORLD item has ParentSlot == null, so the
                // old slot-sourced-only resolver refused the "ground canister onto the suit's
                // canister wedge" swap even after the executors learned WorldDragTo — ask vanilla's
                // own ladder for the world case, exactly like RadialEntry.ResolveDrop's DropSlot rung.
                entry.DropResolver = dragged =>
                {
                    if (dragged == null || slot == null) return null;
                    Slot from = dragged.ParentSlot;
                    if (from != null)
                        return Slot.AllowSwap(from, slot) ? slot : null;
                    switch (Assets.Scripts.UI.InputMouse.IsValid(dragged, slot))
                    {
                        case Assets.Scripts.UI.DragResult.Swap:
                        case Assets.Scripts.UI.DragResult.Valid:
                        case Assets.Scripts.UI.DragResult.Merge:
                        case Assets.Scripts.UI.DragResult.Insert:
                            return slot;
                        default:
                            return null;
                    }
                };
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
            // A stack sitting in a slot (a cable coil on the toolbelt, sheets in a locker) manages
            // by SPLITTING, not Take/Replace/settings — swiping it lands straight on the split
            // choices. (Taking the whole stack is still the wedge's own click action.)
            if (IsPlainStack(thing))
                return BuildSplitLevel(thing);

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
            };

            // REPLACE (swap with a like-of-kind component) only belongs to a DEVICE slot — a typed
            // functional socket (canister, battery, filter, cartridge). A generic None storage slot
            // is NOT a component socket, and its candidate list would be your whole reachable
            // inventory (right down to the player's brain/lungs). So offer Replace for typed slots
            // only; a stored item just gets Take (+ its own settings/slots).
            var slot = source.Slot;
            bool deviceSlot = slot != null && (slot.Type != Slot.Class.None || slot.SpecificTypePrefabHash != -1);
            if (deviceSlot)
            {
                entries.Add(new RadialEntry
                {
                    Label = "Replace",
                    ActionText = "Replace",
                    Sublabel = "swap with another",
                    ChildProvider = () => BuildSlotCandidateEntries(slot),
                });
            }

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

        /// <summary>A stack whose ONLY management is splitting — splittable, with no storage slots of
        /// its own. Anywhere one is swiped/opened it should land straight on the split choices, never
        /// a Take/Replace/settings satellite (a cable coil is not a device component).</summary>
        public static bool IsPlainStack(DynamicThing thing)
            => CanSplit(thing) && (thing?.Slots == null || thing.Slots.Count == 0);

        /// <summary>The split level: SPLIT ONE and SPLIT HALF (both vanilla's own MP-safe
        /// interactions), plus — host/single-player only — SPLIT COUNT with a scroll wheel
        /// that sets how many to peel off. Scrolling only changes the number between the
        /// triangles; the CLICK does the split. (Arbitrary count has no networked vanilla
        /// path, so the count wedge is hidden on multiplayer clients — never a client-side
        /// Quantity mutation.)</summary>
        /// <summary>The scroll-chosen split amount, kept OUTSIDE the entry so it survives the
        /// sticky radial's periodic rebuild (a fresh local counter reset to the default every
        /// refresh, so the number you scrolled to was lost by the time you clicked).</summary>
        private static int _splitCount = 1;

        public static List<RadialEntry> BuildSplitLevel(DynamicThing thing)
        {
            var entries = new List<RadialEntry>();
            var s = thing as Assets.Scripts.Objects.Items.Stackable;
            if (s == null) return entries;
            var t = thing;

            // Text-only wedges — the SPLIT level names the actions ("SPLIT ONE" / "SPLIT HALF" /
            // "SPLIT COUNT"), it doesn't re-show the item you're already splitting.
            entries.Add(new RadialEntry
            {
                Label = "Split one",
                ActionText = "Take 1 to hand",
                Sublabel = "peel one off",
                OnSelect = () => ItemActions.SplitStack(t, half: false),
            });
            entries.Add(new RadialEntry
            {
                Label = "Split half",
                ActionText = "Take half to hand",
                Sublabel = "split the stack in two",
                OnSelect = () => ItemActions.SplitStack(t, half: true),
            });
            if (ItemActions.CanSplitCount(t))
            {
                int max = System.Math.Max(1, s.Quantity - 1);
                _splitCount = UnityEngine.Mathf.Clamp(_splitCount, 1, max);
                entries.Add(new RadialEntry
                {
                    Label = "Split count",
                    ActionText = "Split off this many",
                    Sublabel = "scroll to choose",
                    OnScroll = d => _splitCount = UnityEngine.Mathf.Clamp(_splitCount + d, 1, max),
                    ValueText = () => _splitCount.ToString(),
                    OnSelect = () => ItemActions.SplitStackCount(t, _splitCount),
                });
            }
            return entries;
        }

        /// <summary>#6: the vanilla Sort/organise button as a wedge, for a container vanilla would
        /// let you sort (generic None/Ore storage with 2+ items) — bags, boxes, crates, ore belts,
        /// jetpack/backpack storage. Null when it doesn't qualify.</summary>
        public static RadialEntry BuildSortEntry(DynamicThing container)
        {
            if (!ItemActions.CanSortContainer(container)) return null;
            var c = container;
            return new RadialEntry
            {
                Label = "Sort",
                ActionText = "Sort contents",
                Sublabel = "organise",
                AccentOverride = Theme.Accent,
                OnSelect = () => ItemActions.SortContainer(c),
            };
        }

        /// <summary>Heuristic: bags are navigated (click enters), devices are taken (click) and opened (slide-out).</summary>
        public static bool LooksLikeContainer(DynamicThing thing)
        {
            if (thing?.Slots == null) return false;
            if (IsUnpackBox(thing)) return false; // a sealed package is NOT a bag, despite its slots
            return thing.Slots.Count >= 4 && thing.InteractOnOff == null;
        }

        /// <summary>A storage container the player may bind to a Ctrl+number hotkey (#3): it has
        /// slots and NO device controls (on/off, mode) — bags, boxes, crates, backpacks, but not
        /// tools or jetpacks. Broader than <see cref="LooksLikeContainer"/> (no 4-slot floor).</summary>
        public static bool IsBindableBag(DynamicThing thing)
            => thing?.Slots != null && thing.Slots.Count > 0 && !IsUnpackBox(thing)
               && thing.InteractOnOff == null && thing.InteractMode == null;

        /// <summary>
        /// A sealed disposable package — the starting cereal / water-bottle boxes and every "kit"
        /// package — which vanilla lets you ONLY unpack, never open and store into. They carry six
        /// generic slots pre-filled with their contents, so slot-presence alone reads them as bags;
        /// the vanilla distinction is purely the <c>DisposableCardboardBox</c> class (no interface
        /// exists). Treat these as a plain item that goes to hand on click, with one "Unpack" child.
        /// </summary>
        public static bool IsUnpackBox(DynamicThing thing)
            => thing is Assets.Scripts.Objects.Items.DisposableCardboardBox;

        /// <summary>The wedge for a sealed package: click takes the WHOLE box to hand; its one child
        /// is "Unpack" (vanilla's Button1), which pops a single item out. No bag view, no slots.</summary>
        public static RadialEntry BuildUnpackBoxEntry(DynamicThing box, ScannedSlot source)
        {
            var b = box;
            return new RadialEntry
            {
                Label = box.DisplayName,
                ActionText = "Take to hand",
                Sublabel = "sealed package — unpack only",
                StateText = StateText.For(box),
                Icon = box.GetThumbnail(),
                DragSource = source,
                OnSelect = () => { if (ItemActions.EquipToActiveHand(source)) RetrievalMemory.Record(b); },
                ChildProvider = () => BuildUnpackLevel(b),
                SlideOutProvider = () => BuildUnpackLevel(b),
                SlideOutLabel = "Unpack",
            };
        }

        /// <summary>The single "Unpack" action for a sealed package.</summary>
        public static List<RadialEntry> BuildUnpackLevel(DynamicThing box)
        {
            var b = box;
            return new List<RadialEntry>
            {
                new RadialEntry
                {
                    Label = "Unpack",
                    ActionText = "Unpack one",
                    Sublabel = "take an item out (hold it first)",
                    AccentOverride = Theme.Accent,
                    Icon = box.GetThumbnail(),
                    OnSelect = () => ItemActions.Unpack(b),
                },
            };
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
