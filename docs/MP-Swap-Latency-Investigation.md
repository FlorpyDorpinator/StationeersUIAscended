# MP Radial Swap Latency — Investigation, Testing Plan, and Planned Fixes

**Status: PARKED (2026-07-15).** Research done (two decompile-verified agent passes),
causes ranked, fixes designed but NOT implemented. Pick this file up to resume.

## The symptom (FlorpyDorp's report)

On **multiplayer**, swapping tools via the radial sometimes shows a pause before the tool
visually swaps. Feels like it happens less with the mod disabled. Needs careful A/B testing.

## What the research established (decompile 27701, verified)

### Vanilla has NO client-side prediction for item moves
- `OnServer.MoveToSlot` / `OnServer.SwapSlots` on an MP client are **send-only**: they emit
  a message and mutate nothing locally. The hand icon/model changes only when the server's
  authoritative state round-trips back. No prediction, no rollback, anywhere.
- **BUT vanilla's "swap active hand" (E key / clicking the other hand box) moves NO item** —
  it just re-labels which hand is active, purely locally, **instant**. This is the trap in
  any A/B test: E-swap (free, local) vs radial belt-equip (a real item move, one RTT) are
  different operations. For genuine item moves, vanilla's own UI eats the same RTT we do —
  **there is no faster funnel we're missing.**
- Latency model for any real item move: **one RTT + up to one ~50ms server state tick.**

### Our path is architecturally clean — but adds friction on top
Verified: every hand/tool action in `Core/ItemActions.cs` sends exactly **one** message
through the vanilla funnel; no stow-then-equip double message; no callback waits; dispatch
is synchronous on the commit frame; no coroutines/delays in radial→ItemActions.
Wedge → funnel map (citations in the workflow output, see bottom):
- Toolbelt occupied wedge → `EquipToActiveHand` → `OnServer.MoveToSlot` (hand empty) or
  `Slot.PlayerSwapToSlot` (hand occupied — atomic vanilla swap).
- Empty-belt stow wedge → `StowActiveHandTo` → `OnServer.MoveToSlot`.
- ItemMenuBuilder "Take to hand" entries → `EquipToActiveHand`.

## Ranked causes of the perceived pause

1. **Comparison artifact (likely the biggest share):** no-mod testing tends to use E
   (instant local flip); modded testing uses radial belt-equips (true RTT). Apples/oranges.
2. **Silent drops — the "sometimes" component.** At execute time we re-gate on
   `Slot.AllowMove/AllowSwap` **and** a pinned-occupant check
   (`source.Expected != item → Fail()`, ItemActions.cs:41). On a client with stale
   networked state (even your OWN previous swap still replicating), this fails → fail-beep,
   **no message sent**, radial closes anyway. Reads as lag; is actually a drop + manual
   retry. No auto-retry exists.
3. **Structural input floor on the toolbelt radial:** opens only after a **180ms hold**
   (`UIAConfig.HoldThresholdMs`, default 180; tap is a deliberate no-op for MMB), and the
   action fires on key-RELEASE (`RadialMenu.OnHoldReleased` → `Execute`, ~line 365/1240).
   Total feel: 180ms + flick + release + RTT, vs vanilla E ≈ 0.
4. **Drag conversion eats equips:** tool wedges are drag sources — holding >0.25s
   (`DragHoldSec`) or drifting >14px (`DragMovePx`, RadialMenu.cs:188-189) converts the
   press into a parking drag; the equip never fires. Another "nothing happened."
5. Minor: keep-open paths (Shift-swap, scroll wedges) repaint on a **0.6s timer**
   (`_pendingRefreshAt`) — pure display lag of the wedge labels, mutation unaffected.
   Deferred modal close holds cursor re-lock ~2 frames.

Ruled out: GC/perf work (would stutter frames, not delay one action), HUD rendering
(display-only), double round-trips (verified none).

## The A/B testing protocol (do this before/with the fixes)

1. **Compare like with like**: radial belt-equip vs vanilla-window belt-slot CLICK (a real
   item move) — NOT vs the E key. Also record E-swap separately as the "instant" baseline.
2. **Listen for the fail-beep** on slow swaps: beep = cause #2 (a drop, not latency).
3. Note ping + whether the pause correlates with rapid successive swaps (replication-lag
   window → Expected-pin failures).
4. ~20 samples per condition; with the planned `Swap.RoundTrip` metric this becomes a
   distribution in the profiler table instead of a feel.

## Planned fixes (designed, not built — all MP-safe, display-only or client-input-side)

Priority order:
1. **Make drops loud + self-healing**: on execute-time gate failure show a toast
   ("Blocked — slot changed") and refresh the ring instead of closing silently. A drop must
   never masquerade as lag.
2. **Re-resolve before send**: if the pinned item moved slots between ring-build and
   release, re-locate it (same container walk the builder used) and send the correct SINGLE
   message instead of failing. Keeps one-action-one-message.
3. **Pending affordance**: the frame a swap is sent, ghost the incoming item's icon (low
   alpha) in the hand box until the server-confirmed occupant arrives. No state prediction —
   pure display. This is 90% of why vanilla E "feels" instant.
4. **`Swap.RoundTrip` profiler metric**: timestamp at dispatch (ItemActions) → timestamp
   when the hand slot's occupant reference actually changes (HudSampler sees it) → Record()
   the delta. Gives RTT vs drop separation for free in `uiaprof`.
5. **Input tuning (optional, FlorpyDorp's call)**: lower `HoldThresholdMs` default and/or
   `OpenOnTap` for the toolbelt; consider whether tool wedges need the full 0.25s/14px drag
   thresholds or a slightly more forgiving equip bias.

## Where the full evidence lives

- Workflow output (both agent reports, all file:line citations):
  `C:\Users\<user>\AppData\Local\Temp\claude\c--Dev-Stationeers-UI-Ascended\e3d725da-edaf-4853-a620-a404f3eee8ea\tasks\w7lbdbgg0.output`
  (temp dir — may not survive; the key facts are all reproduced above).
- Key mod files: `Core/ItemActions.cs` (funnel, Expected pin at :41, gates :59/:63),
  `Overlay/RadialMenu.cs` (dispatch timing :339-365, :691-736, :1238-1242; drag thresholds
  :188-189; refresh timer :380 etc.), `Features/ToolbeltRadialFeature.cs` (:68-82, :109,
  :116-119), `UIAConfig.cs` (HoldThresholdMs :154, toolbelt key :181).
- Vanilla facts: `OnServer.MoveToSlot/SwapSlots` send-only on client; `Human.SwapHands`
  (E) is the local no-item flip — verify in the newest decompile before building fix #3.
