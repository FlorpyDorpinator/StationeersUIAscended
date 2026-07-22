using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using HarmonyLib;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// SmartStow+ (proposal §8, reworked per docs/SmartStow-Rework-Design-Options.md): a
    /// Harmony prefix on the vanilla G-key stow. DECIDING lives in <see cref="StowRouter"/>
    /// (worn-belt tools -> stack merge -> explicit profile -> content affinity -> bag-type
    /// default -> type memory); this class only EXECUTES the winning candidate through the
    /// game's authoritative funnel — one Thing.Merge or one OnServer.MoveToSlot per user
    /// action, re-gated at execute time. No winner -> vanilla SmartStow runs untouched.
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

            StowCandidate winner;
            if (!StowRouter.ResolveBest(held, selectedSlot, StowRouter.ConfiguredDepth(), out winner))
                return false; // vanilla takes over

            return Execute(held, winner);
        }

        /// <summary>Execute one routed candidate through the MP funnel. Merge stages go through
        /// ItemActions.MergeInto (Thing.Merge); every move stage is a single OnServer.MoveToSlot
        /// gated by Slot.AllowMove HERE, at execute time. Returning false hands the G press to
        /// vanilla SmartStow — identical to the router having found nothing.</summary>
        private static bool Execute(DynamicThing held, StowCandidate winner)
        {
            if (winner.Slot == null) return false;

            if (winner.Stage == StowStage.StackMerge)
            {
                // Re-derive the target from the slot (same call stack as the decision, so it
                // cannot have changed; the null checks are pure fail-soft).
                var target = winner.Slot.Get() as IMergeable;
                var heldMergeable = held as IMergeable;
                if (target == null || heldMergeable == null) return false;
                UIALog.Debug($"SmartStow+ stack-merge into slot of {(winner.Holder != null ? winner.Holder.DisplayName : "?")}");
                // Capture the icon BEFORE the merge — a merged stack may consume `held`.
                UnityEngine.Sprite mergeIcon = null;
                try { mergeIcon = held.GetThumbnail(); } catch { }
                Slot mergeSlot = winner.Slot;
                // Re-gate at execute time through the authoritative funnel, mirroring the move
                // path's Slot.AllowMove check below. Same synchronous call stack as the decision,
                // so it cannot have changed — this is hygiene/defense-in-depth, not the bug fix.
                if (!Slot.CanMerge(held, mergeSlot)) return false;
                bool merged = ItemActions.MergeInto(target, heldMergeable);
                if (merged)
                {
                    SlotFlash.OnStow(mergeSlot, mergeIcon);
                    StowToast(winner);
                    // #13: one G press fully stows the hand. Stackable.Merge tops the target to
                    // MaxQuantity and leaves the remainder on `held`; continue placing that
                    // remainder into the SAME bag (further partial stacks, then a free slot).
                    ContinueStackRemainder(held, winner.Holder, mergeSlot);
                }
                return merged; // a failed merge falls to VANILLA, never to a later stage (as before)
            }

            // Move-kind stages (belt tool / profile / affinity / bag default / memory).
            if (!Slot.AllowMove(held, winner.Slot)) return false;
            UIALog.Debug($"SmartStow+ {winner.Stage} stow ({winner.Reason}, score {winner.Score})");
            OnServer.MoveToSlot(held, winner.Slot);
            SlotFlash.OnStow(winner.Slot, held);
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);

            // Memory records on profile AND affinity stows (spec O2c): both are "this is where
            // these live" signals. Belt/default/memory stages record nothing — the belt has its
            // own home-slot table, and memory re-recording itself would just pin the past.
            if ((winner.Stage == StowStage.Profile || winner.Stage == StowStage.Affinity) && winner.Holder != null)
                BagProfileStore.RememberStow(held, winner.Holder);
            StowToast(winner);
            return true;
        }

        /// <summary>#13 bounded stack continuation. After the router's ONE stack merge tops a
        /// stack to its cap and leaves a remainder on <paramref name="held"/>, keep placing that
        /// remainder into the SAME holder: first every other partial stack of the item, then a
        /// free slot there. This is a bounded continuation of a SINGLE stow (one pass over one
        /// bag's own slots), NOT a loop over the inventory, and every step re-gates through the
        /// MP funnel (Slot.CanMerge / Slot.AllowMove -> Thing.Merge / OnServer.MoveToSlot).
        ///
        /// Runs ONLY where we are the simulation authority (single-player / host): there each
        /// merge and move applies SYNCHRONOUSLY, so the held stack's Quantity is truthful between
        /// steps and we never issue a message against a remainder a queued network packet has
        /// already consumed. On a multiplayer CLIENT the intermediate state is not yet applied,
        /// so we deliberately stop after the caller's single merge (unchanged pre-#13 behaviour)
        /// and the next G press continues - never a speculative multi-message dump. This mirrors
        /// ItemActions.SplitStackCount, which gates its multi-step path the same way.</summary>
        private static void ContinueStackRemainder(DynamicThing held, Thing holder, Slot firstSlot)
        {
            if (holder == null || holder.Slots == null) return;
            bool authoritative = false;
            try { authoritative = GameManager.RunSimulation; } catch { }
            if (!authoritative) return; // MP client: one stack per press, as before

            Stackable heldStack = held as Stackable;
            if (heldStack == null) return;

            var human = InventoryManager.ParentHuman;
            Slot leftHand = human != null ? human.LeftHandSlot : null;
            Slot rightHand = human != null ? human.RightHandSlot : null;

            // Phase 1: top up every OTHER partial stack of the same item in this holder.
            foreach (Slot s in holder.Slots)
            {
                // heldStack goes Unity-null the instant a merge consumes the last of it.
                if (heldStack == null || heldStack.Quantity <= 0) return;
                if (s == null || s == firstSlot || s == leftHand || s == rightHand) continue;
                var target = s.Get() as IMergeable;
                if (target == null || target.IsStackFull) continue;
                if (!target.CanStack(heldStack)) continue;
                if (!Slot.CanMerge(held, s)) continue;
                ItemActions.MergeInto(target, heldStack); // authoritative + synchronous here
            }

            // Phase 2: any remainder still in hand -> the first free compatible slot in this holder.
            if (heldStack == null || heldStack.Quantity <= 0) return;
            foreach (Slot s in holder.Slots)
            {
                if (s == null || s == firstSlot || s == leftHand || s == rightHand) continue;
                if (s.Get() != null) continue;
                if (!Slot.AllowMove(held, s)) continue;
                OnServer.MoveToSlot(held, s);
                SlotFlash.OnStow(s, held);
                return;
            }
        }

        /// <summary>O7 stow toast (config-gated, DEFAULT OFF): one short ASCII line naming the
        /// destination and the router's reason — "-&gt; Mining Belt (profile: Ores)". Rides the
        /// mod's EXISTING center-screen <see cref="Overlay.Toast"/> surface (the ImGui one-liner
        /// already pumped every frame from DrawOverlay) — no new HUD widget, no new canvas.
        /// Reasons exist only for ROUTED stows (the router decided), which is exactly when this
        /// runs; vanilla fallthrough stows have no reason and toast nothing. Purely visual,
        /// fail-soft.</summary>
        private static void StowToast(StowCandidate winner)
        {
            try
            {
                if (UIAConfig.StowToastEnabled == null || !UIAConfig.StowToastEnabled.Value) return;
                string holder = null;
                if (winner.Holder != null)
                {
                    try { holder = StateText.Strip(winner.Holder.DisplayName); } catch { }
                }
                if (string.IsNullOrEmpty(holder)) holder = "inventory";
                string reason = string.IsNullOrEmpty(winner.Reason) ? "stowed" : winner.Reason;
                Overlay.Toast.Show("-> " + holder + "  (" + reason + ")", Overlay.Theme.TextPrimary, 2f);
            }
            catch { }
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
