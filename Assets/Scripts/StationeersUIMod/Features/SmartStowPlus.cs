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
                    return ItemActions.MergeInto(target, heldMergeable);
                }
            }

            // 2) Bag profiles
            if (UIAConfig.StowUseProfiles.Value && BagProfileStore.Profiles.Count > 0)
            {
                slots = slots ?? InventoryScanner.Scan(depth, false);
                Slot best = null;
                Thing bestBag = null;
                int bestPriority = int.MinValue;
                foreach (var scanned in slots)
                {
                    if (scanned.Slot == selectedSlot || scanned.Occupant != null) continue;
                    if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                    var bag = scanned.Holder;
                    if (bag == null || bag == human) continue;
                    var profile = BagProfileStore.GetAssignedProfile(bag);
                    int? priority = profile?.Match(held);
                    if (!priority.HasValue || priority.Value <= bestPriority) continue;
                    if (!Slot.AllowMove(held, scanned.Slot)) continue;
                    best = scanned.Slot;
                    bestBag = bag;
                    bestPriority = priority.Value;
                }
                if (best != null)
                {
                    UIALog.Debug($"SmartStow+ profile stow into {bestBag.DisplayName} (prio {bestPriority})");
                    OnServer.MoveToSlot(held, best);
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
                        UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                        return true;
                    }
                }
            }

            return false; // vanilla takes over
        }
    }

    [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.SmartStow))]
    internal static class Patch_InventoryManager_SmartStow
    {
        private static bool Prefix(Slot selectedSlot)
        {
            try
            {
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
}

