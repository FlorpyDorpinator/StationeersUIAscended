using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Phase 4 — SmartStow+ (proposal §8): a Harmony prefix on the vanilla G-key stow that
    /// runs three extra steps before falling through to vanilla behavior:
    ///   1. merge into a matching partial stack anywhere accessible (vanilla lacks this),
    ///   2. stow into the bag whose assigned profile matches the item,
    ///   3. stow where this item type was last stowed (per-save memory).
    /// No match → vanilla SmartStow runs untouched.
    /// </summary>
    public static class SmartStowPlus
    {
        /// <summary>Core logic, exposed for testing/diagnostics. Returns true when handled.</summary>
        public static bool TryStow(Slot selectedSlot)
        {
            if (!UIAConfig.MasterEnable.Value || !UIAConfig.SmartStowPlusEnabled.Value) return false;
            if (GameManager.IsBatchMode) return false;

            var human = InventoryManager.ParentHuman;
            if (human == null || selectedSlot == null) return false;

            DynamicThing held = selectedSlot.Get();
            if (held == null) return false;

            // Only augment the "item is in a hand" branch; hand-summon behavior stays vanilla.
            if (selectedSlot != human.LeftHandSlot && selectedSlot != human.RightHandSlot) return false;

            int depth = UIAConfig.StowIntoNestedBags.Value ? UIAConfig.ScanDepth.Value : 1;
            List<ScannedSlot> slots = null;

            // 0) A tool belongs on a DIRECTLY-WORN belt/container before it ever nests into a
            //    belt tucked inside another bag (play-test bug: a tool smart-stowed past a
            //    non-full equipped belt into a mining belt nested in the backpack). Priority,
            //    all at "depth 1" (worn on the body, not inside another container):
            //      a) the equipped waist tool belt (equip slot 6),
            //      b) then the worn Back container (jetpack / backpack) itself.
            //    Only if BOTH genuinely reject the tool (type-gated by Slot.AllowMove) does it
            //    fall through to the stack/profile/memory rules, which MAY nest. One move msg.
            if (UIAConfig.StowToolsToToolbeltFirst.Value && held.SlotType == Slot.Class.Tool)
            {
                // On the worn belt, prefer the tool's remembered HOME slot (feature 1B.2) so a
                // tool always flies back to the same wedge; only then fall back to any free slot.
                // The back container is not a home-slot belt — plain best-free-slot for it.
                Slot dest = HomeOrBestDirectSlot(human.ToolbeltSlot?.Get(), held);
                string where = "toolbelt";
                if (dest == null) { dest = BestDirectSlot(human.BackpackSlot?.Get(), held); where = "back container"; }
                if (dest != null)
                {
                    UIALog.Debug($"SmartStow+ tool -> directly-worn {where} slot (before any nested belt)");
                    OnServer.MoveToSlot(held, dest);
                    SlotFlash.OnStow(dest, held);
                    UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                    return true;
                }
            }

            // 1) Stack merge
            if (UIAConfig.StowPreferStacks.Value && held is IMergeable heldMergeable)
            {
                slots = InventoryScanner.Scan(depth, false);
                foreach (var scanned in slots)
                {
                    if (scanned.Slot == selectedSlot) continue;
                    // Hands are never stow destinations ("stowing" into your other hand).
                    if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                    DynamicThing occ = scanned.Occupant;
                    if (occ == null || !(occ is IMergeable target)) continue;
                    if (!target.CanStack(heldMergeable) || target.IsStackFull) continue;
                    if (!Slot.CanMerge(held, scanned.Slot)) continue;
                    UIALog.Debug($"SmartStow+ stack-merge into {occ.DisplayName} ({scanned.Location})");
                    // Capture the icon BEFORE the merge — a merged stack may consume `held`.
                    UnityEngine.Sprite mergeIcon = null;
                    try { mergeIcon = held.GetThumbnail(); } catch { }
                    Slot mergeSlot = scanned.Slot;
                    bool merged = ItemActions.MergeInto(target, heldMergeable);
                    if (merged) SlotFlash.OnStow(mergeSlot, mergeIcon);
                    return merged;
                }
            }

            // 2) Bag profiles
            if (UIAConfig.StowUseProfiles.Value && BagProfileStore.Profiles.Count > 0)
            {
                slots = slots ?? InventoryScanner.Scan(depth, false);
                Slot best = null;
                Thing bestBag = null;
                int bestPriority = int.MinValue;
                int bestDepth = int.MaxValue;
                foreach (var scanned in slots)
                {
                    if (scanned.Slot == selectedSlot || scanned.Occupant != null) continue;
                    if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                    var bag = scanned.Holder;
                    if (bag == null || bag == human) continue;
                    var profile = BagProfileStore.GetAssignedProfile(bag);
                    int? priority = profile?.Match(held);
                    if (!priority.HasValue) continue;
                    // Higher profile priority wins; on a TIE prefer the shallower bag, so a
                    // directly-worn bag beats an equally-matching one nested inside another
                    // (the same shallow-before-nested intent as step 0, generalised).
                    if (priority.Value < bestPriority) continue;
                    if (priority.Value == bestPriority && scanned.Depth >= bestDepth) continue;
                    if (!Slot.AllowMove(held, scanned.Slot)) continue;
                    best = scanned.Slot;
                    bestBag = bag;
                    bestPriority = priority.Value;
                    bestDepth = scanned.Depth;
                }
                if (best != null)
                {
                    UIALog.Debug($"SmartStow+ profile stow into {bestBag.DisplayName} (prio {bestPriority})");
                    OnServer.MoveToSlot(held, best);
                    SlotFlash.OnStow(best, held);
                    UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                    BagProfileStore.RememberStow(held, bestBag);
                    return true;
                }
            }

            // 3) Item-type memory
            if (UIAConfig.StowUseTypeMemory.Value)
            {
                long? bagRef = BagProfileStore.RecallStow(held);
                if (bagRef.HasValue)
                {
                    slots = slots ?? InventoryScanner.Scan(depth, false);
                    var target = slots.FirstOrDefault(s =>
                        s.Occupant == null &&
                        s.Slot != human.LeftHandSlot && s.Slot != human.RightHandSlot &&
                        s.Holder != null &&
                        s.Holder.ReferenceId == bagRef.Value &&
                        Slot.AllowMove(held, s.Slot));
                    if (target != null)
                    {
                        UIALog.Debug($"SmartStow+ memory stow into {target.Holder.DisplayName}");
                        OnServer.MoveToSlot(held, target.Slot);
                        SlotFlash.OnStow(target.Slot, held);
                        UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                        return true;
                    }
                }
            }

            return false; // vanilla takes over
        }

        /// <summary>Home-slot-aware pick for the worn tool belt (feature 1B.2). When ToolbeltHomeSlots
        /// is on, seed the belt's binding table on first sight and, if this tool TYPE has a remembered
        /// home slot index that is free and accepts the tool, return exactly that slot so the tool flies
        /// back to its own wedge. Otherwise (home taken, unknown, or feature off) fall back to the best
        /// free direct slot. The placement is auto-recorded by BeltBindingStore's Slot.Take postfix once
        /// the move settles, so a tool that lands in a new slot rebinds its home — no explicit record here.</summary>
        private static Slot HomeOrBestDirectSlot(DynamicThing belt, DynamicThing held)
        {
            if (belt?.Slots == null || held == null) return null;
            if (UIAConfig.ToolbeltHomeSlots.Value)
            {
                BeltBindingStore.SeedIfNew(belt);
                int home = BeltBindingStore.HomeSlotFor(belt, held);
                if (home >= 0)
                {
                    foreach (Slot s in belt.Slots)
                    {
                        if (s == null || s.SlotIndex != home) continue;
                        // Home slot found: use it only if genuinely free and type-gated open; else
                        // break out and let BestDirectSlot pick another (the postfix rebinds home).
                        if (!s.IsLocked && s.Get() == null && Slot.AllowMove(held, s)) return s;
                        break;
                    }
                }
            }
            return BestDirectSlot(belt, held);
        }

        /// <summary>The best empty, movable direct slot of a worn container for <paramref name="held"/>:
        /// a slot whose Class matches the item's SlotType wins over a generic (None) slot, so a tool
        /// lands in a real tool slot when one is free but still accepts an open normal slot otherwise
        /// (the user's "tool slot OR normal slot"). Only this container's OWN slots — never nested.
        /// Returns null when the container is absent or has no compatible free slot.</summary>
        private static Slot BestDirectSlot(DynamicThing container, DynamicThing held)
        {
            if (container?.Slots == null || held == null) return null;
            Slot generic = null;
            foreach (Slot s in container.Slots)
            {
                if (s == null || s.IsLocked || s.Get() != null) continue;
                if (!Slot.AllowMove(held, s)) continue;
                if (s.Type == held.SlotType) return s;   // exact type match — the tool's real home
                if (generic == null) generic = s;        // a None/other-compatible slot, kept as fallback
            }
            return generic;
        }
    }

    [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.SmartStow))]
    internal static class Patch_InventoryManager_SmartStow
    {
        private static bool Prefix(Slot selectedSlot)
        {
            try
            {
                // Snapshot the outgoing item BEFORE anything moves. This is what makes the "it went
                // in here" flash reliable: it no longer matters whether SmartStowPlus or vanilla
                // places the item, nor which of vanilla's branches runs (only one of them fires the
                // PerformHiddenSlotMoveToAnimation coroutine we also hook). SlotFlash.Tick watches
                // for the landing and flashes the worn box then.
                try
                {
                    var human = InventoryManager.ParentHuman;
                    if (human != null && selectedSlot != null
                        && (selectedSlot == human.LeftHandSlot || selectedSlot == human.RightHandSlot))
                        SlotFlash.PendStow(selectedSlot.Get(), selectedSlot);
                }
                catch { }

                if (SmartStowPlus.TryStow(selectedSlot))
                    return false; // handled; skip vanilla
            }
            catch (System.Exception e)
            {
                UIALog.Error("SmartStow+ failed, falling back to vanilla: " + e);
            }
            return true;
        }
    }

    /// <summary>
    /// When VANILLA smart-stow (the case SmartStow+ declined) routes an item into a hidden nested
    /// slot, it flashes the visible container's own slot with the stowed item's icon for 0.5 s
    /// (InventoryManager.PerformHiddenSlotMoveToAnimation). Vanilla's slot lives on the now-hidden
    /// vanilla HUD, so mirror that same confirmation onto our HUD boxes. Read-only observation:
    /// <c>freeSlotParent</c> is already vanilla's resolved worn slot, so post it directly.
    /// </summary>
    [HarmonyPatch(typeof(InventoryManager), "PerformHiddenSlotMoveToAnimation")]
    internal static class Patch_InventoryManager_HiddenSlotMoveAnim
    {
        private static void Prefix(Slot freeSlotParent, DynamicThing dynamicThing)
        {
            try
            {
                if (freeSlotParent != null && dynamicThing != null)
                    SlotFlash.Post(freeSlotParent, dynamicThing.GetThumbnail());
            }
            catch { }
        }
    }
}

