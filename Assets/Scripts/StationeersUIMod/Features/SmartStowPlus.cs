using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using HarmonyLib;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Tutorial;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// SmartStow+ (proposal §8, reworked per Documentation/SmartStow-Rework-Design-Options.md;
    /// Simple mode 0.9.8.0 per Documentation/0.9.8.0/SmartStow-Simple-Refactor-Plan.md): a
    /// Harmony prefix on the vanilla G-key stow. DECIDING lives in <see cref="StowRouter"/>,
    /// which since 0.9.8.0 forks on <see cref="StowModeConfig.Mode"/> (Simple: the item's own
    /// remembered HOME first, then a fixed fallback chain; Complex: today's profile chain); this
    /// class only EXECUTES the winning candidate through the game's authoritative funnel — one
    /// <c>ItemActions.MergeInto</c> (Thing.Merge) or one <c>ItemActions.StowRoutedTo</c>
    /// (OnServer.MoveToSlot) per user action, re-gated at execute time. This class itself never
    /// touches OnServer (CLAUDE.md rule 3 — every mutation lives in <see cref="Core.ItemActions"/>).
    /// A routed landing SEEDS the item's home (first home only — the store never lets a landing
    /// steal a live home). No winner -> vanilla SmartStow runs untouched.
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

        /// <summary>Execute one routed candidate through the MP funnel. Merge outcomes go through
        /// ItemActions.MergeInto (Thing.Merge); every move outcome is a single
        /// ItemActions.StowRoutedTo (Slot.AllowMove re-gated in there, at execute time, on this
        /// same call stack — rule 3: no OnServer call lives in this class). Returning false hands
        /// the G press to vanilla SmartStow — identical to the router having found nothing.</summary>
        private static bool Execute(DynamicThing held, StowCandidate winner)
        {
            if (winner.Slot == null) return false;

            // Simple-mode teaching note (plan §9.1 SimpleNotes): decide "did this item HAVE a live
            // home?" BEFORE anything moves — the landing itself seeds a first home, which would
            // otherwise turn every "no home yet" into a false "home full" when read afterwards.
            bool wantSimpleNote = false;
            bool hadLiveHome = false;
            if (winner.Stage != StowStage.Home)
            {
                try
                {
                    wantSimpleNote = StowModeConfig.Available && StowModeConfig.Mode == StowMode.Simple
                        && StowModeConfig.SimpleNotes;
                    if (wantSimpleNote)
                    {
                        Thing homeBag; Slot homeSlot;
                        hadLiveHome = StowHomeStore.TryGetLiveHome(held, out homeBag, out homeSlot);
                    }
                }
                catch { wantSimpleNote = false; }
            }

            // A HOME candidate is a move OR a merge depending on what occupies the remembered slot
            // (plan §6.1 a-c) — re-derived HERE, on the same synchronous call stack as the
            // decision, exactly like the CanMerge/AllowMove re-gates below.
            bool mergeKind = winner.Stage == StowStage.StackMerge
                || (winner.Stage == StowStage.Home && winner.Slot.Get() != null);

            if (mergeKind)
            {
                // Re-derive the target from the slot (same call stack as the decision, so it
                // cannot have changed; the null checks are pure fail-soft).
                var target = winner.Slot.Get() as IMergeable;
                var heldMergeable = held as IMergeable;
                if (target == null || heldMergeable == null) return false;
                UIALog.Debug($"SmartStow+ {winner.Stage} merge into slot of {(winner.Holder != null ? winner.Holder.DisplayName : "?")}");
                // Capture the icon BEFORE the merge — a merged stack may consume `held`.
                UnityEngine.Sprite mergeIcon = null;
                try { mergeIcon = held.GetThumbnail(); } catch { }
                Slot mergeSlot = winner.Slot;
                // Re-gate at execute time through the authoritative funnel, mirroring the move
                // path's Slot.AllowMove check below. Same synchronous call stack as the decision,
                // so it cannot have changed — this is hygiene/defense-in-depth, not the bug fix.
                if (!Slot.CanMerge(held, mergeSlot)) return false;
                // Routed-landing note (review fix): a pure stack merge itself fires NO Slot.Take
                // (the target never moves; a fully-absorbed child dies in the hand, a remainder
                // stays there) — this pre-merge note is defensive coverage on the one id that
                // COULD land, at the cost of one pooled slot. See NoteRoutedLanding.
                NoteRoutedLanding(held);
                bool merged = ItemActions.MergeInto(target, heldMergeable);
                if (merged)
                {
                    SlotFlash.OnStow(mergeSlot, mergeIcon);
                    StowToast(winner);
                    // Routed-stow seeding (plan §4.2, "seed only"): the REMAINDER left on `held`
                    // gets its first home at the stack it now tops up; a fully-consumed stack
                    // seeds nothing. Runs BEFORE the continuation, whose same-bag landing then
                    // only refreshes the slot index.
                    TrySeedRoutedHome(held, mergeSlot, true);
                    // #13: one G press fully stows the hand. Stackable.Merge tops the target to
                    // MaxQuantity and leaves the remainder on `held`; continue placing that
                    // remainder into the SAME bag (further partial stacks, then a free slot).
                    ContinueStackRemainder(held, winner.Holder, mergeSlot);
                    SimpleModeNote(winner, hadLiveHome, wantSimpleNote);
                }
                return merged; // a failed merge falls to VANILLA, never to a later stage (as before)
            }

            // Execute-time safety rail (2026-09-26, jetpacks + tool belts assignable): no BAG-level
            // stage may land an item in a wearable's device socket - a canister of the wrong gas in
            // a jetpack's propellant slot is burned as fuel. The router already skips those slots
            // at every such stage; this re-check on the same call stack makes it a hard invariant.
            // Only the dedicated FunctionalSocket stage (and a Home stage's exact remembered slot,
            // which the item itself came out of) may fill a socket. Declining hands G to vanilla.
            if (IsBagLevelStage(winner.Stage) && BagProfileGate.IsDeviceSlot(winner.Holder, winner.Slot))
            {
                UIALog.Debug("SmartStow+ " + winner.Stage + " declined: destination is a device socket");
                return false;
            }

            // Move-kind stages (home / belt tool / socket / profile / affinity / bag default /
            // memory / generic / belt fallback): ONE funnel move through ItemActions. The
            // routed-landing note goes out FIRST: on the host the Slot.Take landing fires
            // synchronously inside the call; on a client the echo lands within the note's window.
            NoteRoutedLanding(held);
            if (!ItemActions.StowRoutedTo(held, winner.Slot)) return false;
            UIALog.Debug($"SmartStow+ {winner.Stage} stow ({winner.Reason}, score {winner.Score})");
            SlotFlash.OnStow(winner.Slot, held);
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            TrySeedRoutedHome(held, winner.Slot, false);

            // Memory records on profile AND affinity stows (spec O2c): both are "this is where
            // these live" signals. Belt/default/memory stages record nothing — the belt has its
            // own home-slot table, and memory re-recording itself would just pin the past. These
            // stages exist only in the COMPLEX chain, so type memory never records in Simple.
            if ((winner.Stage == StowStage.Profile || winner.Stage == StowStage.Affinity) && winner.Holder != null)
                BagProfileStore.RememberStow(held, winner.Holder);
            StowToast(winner);
            SimpleModeNote(winner, hadLiveHome, wantSimpleNote);
            return true;
        }

        /// <summary>Stages that pick a STORAGE slot inside a bag (as opposed to the functional-socket
        /// stage, which exists to fill sockets, and the stack-merge stage, which never changes what
        /// sits in a slot). See the device-socket rail in <see cref="Execute"/>.</summary>
        private static bool IsBagLevelStage(StowStage stage)
        {
            switch (stage)
            {
                case StowStage.Profile:
                case StowStage.Affinity:
                case StowStage.BagDefault:
                case StowStage.Memory:
                case StowStage.GenericFallback:
                case StowStage.BeltTool:
                case StowStage.BeltFallback:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Review fix (0.9.8.0): mark the imminent ROUTED landing as MECHANICAL for the
        /// home store's observer. A live CONTAINER intent (bulk Shift-drag / vanilla MoveAll)
        /// rehomes EVERY landing in that container for ~3 s — so a G-routed fallback landing into
        /// that same bag inside the window would silently STEAL the item's home, breaking
        /// "visiting never steals a home" (plan §4.2). The pooled ~3 s note makes the observer
        /// treat this specific ReferenceId's next landing as mechanical (seed/refresh only, never
        /// an intent rehome). Called immediately before EVERY routed dispatch — the winner move,
        /// the winner merge, and each #13 continuation step — for whichever id can actually land:
        /// the held item on move paths; on merge paths a pure stack merge fires no Slot.Take at
        /// all (target in place, absorbed child destroyed in the hand, remainder stays in hand),
        /// and the surviving remainder KEEPS the held ReferenceId, so noting `held` covers every
        /// case. Fail-soft observation, zero behaviour change.</summary>
        private static void NoteRoutedLanding(DynamicThing item)
        {
            try
            {
                if (item == null) return;
                StowHomeStore.NoteRoutedLanding(item.ReferenceId);
            }
            catch { }
        }

        /// <summary>Seed-only home capture after a ROUTED stow, all modes (plan §4.2: a Smart Stow
        /// landing may give an item its FIRST home, never steal a live one — the store enforces
        /// exactly that, so calling this beside the Slot.Take landing observer is idempotent, and
        /// it keeps Simple mode working even if that Harmony patch failed to apply). For a merge
        /// (<paramref name="merge"/>), the surviving REMAINDER in the hand is what gets homed at
        /// the target stack's slot; a fully-consumed stack (Quantity 0, or already Unity-null on
        /// the host) seeds nothing. On an MP client the dispatch is send-only, so a server-refused
        /// move can leave a seeded home the item never visited — first-home-only, self-corrected
        /// by the item's next real landing being "visiting" or a same-bag refresh. Fail-soft
        /// observation; never mutates game state.</summary>
        private static void TrySeedRoutedHome(DynamicThing held, Slot landedSlot, bool merge)
        {
            try
            {
                if (merge)
                {
                    Stackable rem = held as Stackable;
                    if (rem == null || rem.Quantity <= 0) return;   // fully absorbed: nothing left to home
                }
                StowHomeStore.SeedHome(held, landedSlot, "seed");
            }
            catch { }
        }

        /// <summary>Simple-mode teaching note (plan §9.1 <c>SimpleNotes</c>, default ON — decision
        /// #25): shown only after a successful FALLBACK landing — a home stow gets the normal slot
        /// flash and no toast spam. "No home yet" teaches the first-landing rule; "Home full"
        /// explains why G ignored the home this press (<paramref name="hadLiveHome"/> was decided
        /// BEFORE the stow). Rides the existing center-screen <see cref="Overlay.Toast"/> surface
        /// exactly like <see cref="StowToast"/>; shown after it, so when both are enabled the
        /// teaching note wins the single toast line. Purely visual, fail-soft, ASCII only.</summary>
        private static void SimpleModeNote(StowCandidate winner, bool hadLiveHome, bool wanted)
        {
            if (!wanted || winner.Stage == StowStage.Home) return;
            try
            {
                string holder = null;
                if (winner.Holder != null)
                {
                    try { holder = StateText.Strip(winner.Holder.DisplayName); } catch { }
                }
                if (string.IsNullOrEmpty(holder)) holder = "inventory";
                Overlay.Toast.Show((hadLiveHome ? "Home full - put in " : "No home yet - put in ") + holder,
                    Overlay.Theme.TextPrimary, 2.5f);
            }
            catch { }
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
                // D-005 (A10): never feed a stack sitting in a hidden/stack slot — the same skip the
                // router's merge stage makes (StowRouter.TryStackMerge -> IsDestSlot) and phase 2
                // below makes for a free slot. A built-in part is not a stow target by merge either.
                if (Core.ItemActions.IsSealedSlot(s)) continue;
                var target = s.Get() as IMergeable;
                if (target == null || target.IsStackFull) continue;
                if (!target.CanStack(heldStack)) continue;
                if (!Slot.CanMerge(held, s)) continue;
                NoteRoutedLanding(heldStack); // routed-landing note before each dispatch (review fix)
                ItemActions.MergeInto(target, heldStack); // authoritative + synchronous here
            }

            // Phase 2: any remainder still in hand -> the first free compatible slot in this holder.
            if (heldStack == null || heldStack.Quantity <= 0) return;
            foreach (Slot s in holder.Slots)
            {
                if (s == null || s == firstSlot || s == leftHand || s == rightHand) continue;
                if (s.Get() != null) continue;
                if (Core.ItemActions.IsSealedSlot(s)) continue; // never stow into a hidden/stack slot (D-005)
                if (BagProfileGate.IsDeviceSlot(holder, s)) continue; // never a jetpack/survival-belt socket (2026-09-26)
                // Rule 3: through the ItemActions funnel (AllowMove re-gated in there); a refused
                // slot is simply skipped, exactly like the old inline AllowMove check. The
                // routed-landing note precedes the dispatch (this landing is the one the observer
                // actually sees, host-synchronously); a refused attempt's note just expires.
                NoteRoutedLanding(held);
                if (!ItemActions.StowRoutedTo(held, s)) continue;
                SlotFlash.OnStow(s, held);
                // Host-synchronous, so the Slot.Take observer already seeded/refreshed — this
                // idempotent call is the belt-and-braces half (see TrySeedRoutedHome).
                TrySeedRoutedHome(held, s, false);
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
        private static bool Prefix(Slot selectedSlot, out bool __state)
        {
            // Tutorial hook (Build Contract s4): did this G-press actually have something to stow?
            // Captured BEFORE either path runs, so the Postfix can tell "stowed" apart from "G on an
            // already-empty hand" (a no-op that must never read as a successful SmartStowed).
            __state = selectedSlot != null && selectedSlot.Get() != null;
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

        /// <summary>Runs after the stow — OURS or vanilla's fallthrough branch alike. If the
        /// stowed slot was the ACTIVE HAND and a build/placement hologram is up, tear it down
        /// (FlorpyDorp, 2026-08-06: "G on the item you're building with left the hologram, as if
        /// you could build, but you can't"). Vanilla only cancels from its own input paths, so
        /// even the pure-VANILLA G-branch ghosted — his "might even be a vanilla bug" was right;
        /// this postfix covers both routes at the one choke point. On host/SP the move applied
        /// synchronously, so a still-occupied hand means the stow FAILED (belt full) and the mode
        /// is kept; an MP client cannot see the echo yet, so it cancels optimistically — a failed
        /// G with a full inventory is the rare case, and re-entering build mode is one click.</summary>
        private static void Postfix(Slot selectedSlot, bool __state)
        {
            try
            {
                bool authoritative = false;
                try { authoritative = GameManager.RunSimulation; } catch { }
                bool stillHeld = selectedSlot != null && selectedSlot.Get() != null;
                // Tutorial hook: only when __state says there was something to stow. Host/SP knows the
                // truth at once (hand now empty = it landed); an MP client cannot see the echo yet, so
                // it confirms the GESTURE optimistically, same as every other do-step (B.2: the server
                // can still refuse afterwards).
                if (__state) TutorialSignals.Raise(TSignal.SmartStowed, authoritative ? (stillHeld ? 0 : 1) : 1);
                if (authoritative && stillHeld) return;
                Core.ItemActions.MaybeCancelPlacement(selectedSlot);
            }
            catch { }
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
