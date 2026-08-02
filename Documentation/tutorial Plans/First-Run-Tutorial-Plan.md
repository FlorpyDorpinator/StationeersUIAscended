# First-Run Tutorial — Design & Implementation Plan (Rev 2)

**Status:** approved direction, expanded per FlorpyDorp's answers (2026-08-01). **Scope:** a
click-through "coach" tutorial that pops up on first world entry (pausing the game in
single-player), teaches the *real* interaction flow — wheels, child radials, component
swapping, the Universal Inventory, pinning, Smart-Stow, profiles — plus a **pause button in
the F10 header**, and the **Designer Handbook as a PDF** (readable in-game) instead of a
GitHub guide.

**Ground truth:** every step in §6 was verified against the code by a five-agent survey. The
full citation-level reference lives in **`Documentation/Interaction-Model-Reference.md`** —
read it before editing any step copy; if an interaction changes, update that file and the
affected step together.

**Decisions already made by FlorpyDorp (do not re-litigate):**
1. **Programmatic animations confirmed** — video is a per-step escape hatch only.
2. **Curriculum expanded** beyond the original 9 steps; **no curvature mention**.
3. **Auto-open on first world entry AND pause the game** (single-player), with a **pause
   button top-right in the F10 menu**; the first-run pause defaults ON so a fresh-save player
   isn't burning oxygen while reading.
4. **No GitHub guide** — the HUD-design deep-dive ships as a **PDF**, ideally readable
   in-game.

Rev 1's phasing survives (§10) but execution order is deliberately NOT finalized here —
FlorpyDorp wants to discuss phases separately after reading this.

---

## 1. What it teaches (and what it doesn't)

**Teaches:** the interaction grammar (tap = sticky wheel, hold = the quick action), freeing
the mouse (the latch), each wheel, child radials and the swipe, the Replace flow (battery /
canister swapping — the mod's signature move), value wedges, drag + park, the Universal
Inventory (both scroll regimes), pinning (all three ways), Smart-Stow, profiles/themes, and
where to learn more.

**Deliberately NOT taught:** designing your own HUD in F9 (that's the Designer Handbook, §9),
curvature (per decision 2), the SmartStow profile editor deep-dive (advanced; the Storage tab
and Handbook cover it), and anything marked UNVERIFIED in the reference doc (e.g. ammo
magazines) until play-tested.

---

## 2. Approach: programmatic coach (confirmed)

Unchanged from Rev 1, now a decision: the coach is a modal card built from the mod's own UGUI
kit; per-step looping **mock** animations built from our primitives; live key glyphs via
`UiaKeybinds.Glyph` so every "press X" shows the player's actual binding (a video lies the
moment someone rebinds; our zip stays ~1.5 MB; a code animation is edited in place when the UI
changes). Videos only if a specific demo proves impossible to mock.

The demos **never drive the real radial/inventory** — purpose-built mocks, zero game-state
risk, driven by `Time.unscaledTime` (they must animate while the game is PAUSED, which is now
the default first-run state).

---

## 3. Where it hooks in

- **First-run trigger:** `StationeersUIMod.cs:375-381` currently opens the Guide tab once
  (`GuideShown`). Repoint to `TutorialCoach.OpenFirstRun()` and **strengthen the gate**: the
  current `Guards.CanToggleMenus()` can fire while paused, over a vanilla menu (an MP client's
  Esc menu doesn't pause), or while unconscious (reference §20). Add:
  `!WorldManager.IsGamePaused` + `!Guards.VanillaMenuWantsFront()` + a responsive-parent
  check, plus a ~1 s settle delay after the gate first passes (don't pop on the world's
  fade-in frame).
- **Window + content kit:** reuse `UiaControlCenter`'s modal pattern (`IModal.UnlockCursor`,
  Typing input state, scrim, sortingOrder above the HUD), `UiaControls`
  (Header/Button/Switch), `UiaTheme`. No new windowing tech.
- **Key glyphs:** `UiaKeybinds.Glyph("UIA_...")` + live vanilla lookups (G, F, Alt, 1-6) via
  `KeyManager` — never hard-code a key name in step copy.
- **Replay:** a "Replay tutorial" button on the Guide tab + a `uiatutorial` console command
  (FinderCommands prefix pattern; opens UI only, no game-state mutation).

---

## 4. The coach window (UX)

```
+------------------------------------------------------+
|  GETTING STARTED                 [PAUSED]        [X] |
|                                                      |
|   +--------------------------------------------+     |
|   |          DEMO / DIAGRAM AREA               |     |   Phase 1: static drawn diagram
|   |   (looping mock: wheel, swipe, drag...)    |     |   Phase 2: looping animation
|   +--------------------------------------------+     |
|                                                      |
|   Step heading                                       |
|   Two or three short sentences, ASCII only, with     |
|   live key glyphs like [MMB] [B] [Alt].              |
|                                                      |
|   oooo*ooooooooo                     Step 5 of 15    |
|                                                      |
|  [ Skip tutorial ]              [ Back ]  [ Next ]   |
+------------------------------------------------------+
```

- **Nav:** Next (Enter), Back, Skip (any step), Esc = close + mark shown. Final step's Next =
  "Got it - start playing" (and unpauses if we hold the pause).
- **[PAUSED] chip** in the header while our pause latch is held (single-player). In
  multiplayer it reads "game is running" — there is no MP pause (reference §20) — so the
  player knows to Skip if they're in a hurry.
- **Backdrop:** dimmed full-screen scrim. The world is paused underneath in SP anyway.
- **ASCII only** in every displayed string (TMP tofu rule): "->", "1 - 6", never arrows or
  em-dashes.
- The static **Guide tab stays** as the always-there reference card (it needs a copy rewrite —
  §11 cleanup — its current text describes the pre-Hub scheme).

---

## 5. Pause: the first-run auto-pause + the F10 header button

All API facts verified against decompile V27758 (reference §20). Single-player only — vanilla
itself cannot pause a multiplayer session from Esc.

**5.1 Shared primitive — `Core/GamePause.cs` (new):** a small static latch used by BOTH the
tutorial and the F10 button.
- `GamePause.Hold(reason)` / `Release(reason)` / `bool Held`.
- Pause path: if `CanOwnPause()` (= `!NetworkManager.IsClient && NetworkBase.Clients.Count ==
  0 && !InventoryManager.Instance.InGameMenuOpen`) -> `WorldManager.SetGamePause(true)`, then
  **immediately re-assert our `Typing` input state** (SetGamePause stomps it with `Paused`,
  which would let Esc open the vanilla menu over us).
- Unpause path: only when `!NetworkBase.IsPaused` -> `SetGamePause(false)`, and always BEFORE
  releasing the modal input state (KeyManager unwind is order-sensitive).
- Subscribe `WorldManager.OnPaused`: if anything else unpauses (player round-trips Esc), drop
  our latch and repaint the button — never fight vanilla for the state.
- **`Guards.SelfPauseHeld`**: OR'd out of `Guards.VanillaMenuWantsFront()` while WE hold the
  pause — otherwise our own pause demotes the HUD, hides the Universal Inventory, and closes
  radials (they all key off that guard).
- **Hot-reload + transition safety:** clear the latch in `Shutdown()` and whenever
  `!Guards.CanDraw()` (world unload). A double-F6 while paused must never leave the game
  frozen; a world transition force-resets timeScale without clearing `IsGamePaused`, so the
  latch must not survive it.

**5.2 The F10 header button:** inserted in `UiaControlCenter.BuildTitleBar` between the
"Advanced" button (line 351) and the "X" (line 353) — top-right, exactly as requested.
- Visual: two small vertical bars drawn with `PanelGraphic` (the draw-don't-type rule for
  icons), or ASCII "||" text as the trivial fallback. `SetSelected(true)` while paused.
- Behavior: click toggles `GamePause.Hold/Release("f10")`. Repaint from the `OnPaused` event.
- **Multiplayer / host-with-clients:** `SetEnabled(false)` + a tooltip-style note ("pause is
  single-player only") — visible but honest, never a silent no-op.
- Wiring chores (verified attach points): a static field next to `_simpleBtn`
  (`UiaControlCenter.cs:55`), nulled in `RestyleCore()` (:224-226) and `Shutdown()`
  (:517-518), repainted alongside `RefreshDensityButtons()` (:440-444).

**5.3 Tutorial behavior:** `TutorialCoach.OpenFirstRun()` -> if `TutorialAutoPause` (new
config, default **true**) and `CanOwnPause()` -> `GamePause.Hold("tutorial")`. Released on
finish, skip, or close. Manual replays (`uiatutorial`, Guide-tab button) do NOT auto-pause —
the player can use the new F10 button if they want stillness.

---

## 6. The curriculum (15 steps, code-verified)

Each step = { heading, ASCII body copy with live glyphs, demo, citations }. Copy below is
draft v1 — tune tone freely, but the *facts* are locked to the reference doc. `[X]` denotes a
live glyph (`UiaKeybinds.Glyph` or `KeyManager` lookup), never literal text.

### Act I — the basics

**1. Welcome.**
> "Welcome to UI Ascended. Your bottom bar is two HANDS - there is no hotbar. Wheels and one
> universal window replace menu-diving. The game is paused while you read. Click Next - this
> takes about three minutes."
Demo: title card — two hand boxes + a wheel + the window silhouette. (MP variant of line 3:
"Your game is still running - Skip any time.")

**2. Free your mouse.** *(the gate skill — everything clickable depends on it)*
> "Your mouse normally aims. Hold [Alt] to free the cursor - or DOUBLE-TAP [Alt] to KEEP it
> free, and tap once more to go back to aiming. Windows can only be clicked while the cursor
> is free."
Demo: crosshair -> cursor swap, with the double-tap pulse highlighted.
Verified: CursorLatch double-tap default on, 250 ms; grid look-only trap (ref §11).

**3. Wheels: tap or hold.**
> "Every wheel key works two ways. TAP: the wheel opens and stays - point and LEFT-CLICK to
> act, RIGHT-CLICK to go back, [Esc] to close. HOLD the same key: a quick throwaway wheel -
> sweep to what you want and RELEASE to act."
Demo: the same mock wheel driven both ways, side by side loop.
Verified: 180 ms threshold; sticky vs transient dispatch (ref §2-3, §5).

### Act II — the wheels

**4. The belt wheel and The Hub.**
> "TAP [MMB]: your toolbelt as a wheel. Click a tool to take it in hand - its slot remembers
> it. The top wedge, THE HUB, leads to everything you carry. The wheel's center shows details;
> its bottom band is Close. [Q] swaps which belt you wear, [Tab] flips to your backpack."
Demo: belt ring with ghost labels; equip a wrench; dive The Hub.
Verified: opens beltless; hub wedge = inventory root; close band; Q belt-picker; Tab swap
(ref §3-5). Trap noted in copy footnote: [MMB] inside any open wheel just closes it.

**5. The bag wheel and search.**
> "TAP [Tab]: your bags as a wheel (HOLD [Tab] still shows the scoreboard). Open a bag wedge
> to dive inside. Big bags group by type. Pick SEARCH and just type to find anything you
> carry."
Demo: dive backpack -> group wedges -> search panel typing "cable".
Verified: BagRadialTapOpens default; grouping threshold 10; search panel (ref §3, §10).

**6. Equipment keys 1 - 6.**
> "TAP a number: that worn item's wheel - its settings, its slots, its swaps. HOLD the number:
> take it off into your hand (or put on what you are holding - tapping an EMPTY slot does that
> too). Inside any wheel, the numbers jump between wheels."
Demo: tap 3 -> suit wheel with settings; hold 3 -> suit to hand.
Verified: tap=manage / hold=equip; empty-slot fall-through; in-wheel digit jump (ref §3, §5).

**7. Child wheels: the swipe.**
> "A wedge with a little chevron holds more. Push THROUGH it, past the wheel's edge, and hold
> still a moment - a child wheel pops out beside it. Pull back to dismiss it, RIGHT-CLICK to
> go back."
Demo: THE core gesture loop — cursor pushes past the rim, satellite blooms.
Verified: outerR+14 px, 0.18 s dwell, both modes, RMB back (ref §6).

**8. Swap a battery.** *(the worked example — the mod's signature move)*
> "Tool in hand? TAP [R]. Its battery sits on the wheel with its charge. Swipe out through it:
> TAKE or REPLACE. Click REPLACE - every battery you carry appears, charge and warnings shown.
> Click one. Swapped, one step. Canisters and cartridges work exactly the same. Hold [Shift]
> to keep the wheel open."
Demo: the full loop, ending on the atomic swap flash. (Copy never says "refill" — no such
verb exists.)
Verified: walkthrough 7a/7b in the reference, incl. the hold-mode take-to-hand variant.

**9. Scroll wedges, drag, and parking.**
> "Wedges with a value (suit pressure, thrust) adjust with the SCROLL WHEEL - hold [C] for
> fine steps. DRAG any item off its wedge: drop it on another wedge, a hand box, or into a
> machine slot in the world - the game's colored box shows what will happen. Drop it on open
> screen to PARK it while you sort; the Close band drops parked items on the ground, [Esc]
> just cancels."
Demo: scroll a value; park two chips; dump via close band.
Verified: +-10/C +-1; park rules; dump-vs-cancel exits; placement box colors (ref §7d, §8).
Trap footnote: parked chips clear (harmlessly) if you jump to another wheel.

### Act III — one window for everything

**10. The Universal Inventory.**
> "HOLD [B]: a quick peek at everything you carry. TAP [B]: it stays open. Every container you
> wear is a folder - click its tab to open it. Remember step 2: free your mouse to click
> inside."
Demo: peek vs latch; a manila tab expanding.
Verified: hold-peek/tap-latch; collapsed-by-default; look-only trap (ref §11).

**11. Working the window.**
> "Mouse captured: the SCROLL WHEEL moves a highlight through every slot - press [F] to take
> the item, or to place what you are holding into an empty slot. Mouse freed: scroll pans,
> LEFT-CLICK takes to your hand, RIGHT-CLICK opens the item's wheel, and you can DRAG anything
> anywhere - other slots, your hands, the numbers, the ground."
Demo: split-screen: captured regime (highlight + F) / freed regime (click + drag).
Verified: the inverted regimes; bidirectional F; click/right-click/drag semantics (ref §12).

**12. Pin a bag.**
> "Any bag can live on screen. Drag its folder tab OUT of the window - now it is its own
> little window. Or press [Shift]+[1-6] for a worn container - or, with the mouse free, just
> CLICK its box on your HUD. Closing the big window leaves pins up; a pin's X tucks it back
> in."
Demo: tab tear-out -> pinned window; a click on the suit box pinning it.
Verified: all three entry points + unpin paths (ref §13).

**13. Smart-Stow.**
> "Hands full? Press [G]. The item routes itself - tools to their belt slot, stacks top up,
> batteries to empty sockets, then your bag rules - and the receiving box flashes. It works
> with a wheel open, and it learns: a tool goes back to the slot you gave it. Bind a bag to
> [Ctrl]+[1-0] by hovering its wedge in a wheel and pressing the number."
Demo: three-item stow montage with flashes.
Verified: routing order; consumable-box exclusions (not in copy, but demo never shows a cereal
box as a target); radial-open path; flash; Ctrl-digit binding (ref §17, §5).

### Act IV — make it yours

**14. Pick your look.**
> "Open [F10] -> PROFILES. Click a card and the whole mod re-skins - HUD, wheels, windows,
> this menu. Two looks ship: Stationeers Blue and Pure HUD. Everything about them is editable
> later."
Demo: theme crossfade between the two shipped themes.
Verified: click-to-apply cards; themes carry everything; two shipped themes (ref §21).

**15. Where to go next.**
> "The GUIDE tab is your reference card - replay this tutorial there any time. Rebind keys on
> the CONTROLS tab. Want to redesign the HUD itself? Press [F9] - and read the Designer
> Handbook."
Buttons: **[Open the Designer Handbook]** (§9 viewer), **[Replay tutorial]**, and Next
becomes **"Got it - start playing"** (+ "[ ] Do not show tips again"). Closing releases the
pause.

**Merge candidates if 15 feels long in play:** 4+5 (both "a wheel of your stuff"), 9 could
split its parking half into an "advanced" appendix step. The step LIST is FlorpyDorp's final
sign-off (§12).

---

## 7. The programmatic demo system

`TutorialDemoStage` — a self-contained RectTransform inside the card; one looping scripted
mock per step, built from `PanelGraphic`/`TriangleGraphic`/`PolygonPanelGraphic` + TMP; a tiny
state machine on `Time.unscaledTime` (must run while PAUSED); zero per-frame allocation; torn
down with the window. **Never touches the real radial/inventory/game state.**

Demo inventory (Phase 2, one per step; Phase 1 ships the same scenes as static diagrams):

| # | Demo | Teaches | Steps |
|---|---|---|---|
| 1 | `CursorLatch` | crosshair vs freed cursor, double-tap pulse | 2 |
| 2 | `TapVsHold` | sticky wheel vs flick-release, side by side | 3 |
| 3 | `BeltWheel` | ring + ghost labels + Hub dive + close band | 4, 5 |
| 4 | `SwipeChild` | push past rim -> satellite blooms | 7 |
| 5 | `BatterySwap` | wedge -> child -> Replace list -> swap flash | 8 |
| 6 | `ValueAndPark` | scroll wedge ticking; chips parked + dumped | 9 |
| 7 | `GridPeekLatch` | hold-peek vs tap-latch; tab expand | 10 |
| 8 | `GridRegimes` | highlight+F (captured) / click+drag (freed) | 11 |
| 9 | `PinFlow` | tab tear-out; click a HUD box -> pin | 12 |
| 10 | `StowFlash` | items routing + box flash | 13 |
| 11 | `ThemeSwitch` | Stationeers Blue <-> Pure HUD crossfade | 14 |

Steps 1, 6, 15 use static art only. Keep demos schematic (not pixel-clones) so minor UI
changes don't invalidate them; list them in the release checklist to eyeball after any
interaction change.

---

## 8. Config, commands, files

**New files** (`UI/Menu/Tutorial/`): `TutorialCoach.cs` (window + nav + pause latch calls),
`TutorialSteps.cs` (pure step data: heading, body, glyph ids, demo id — all copy centralized
here), `TutorialDemoStage.cs` (Phase 2). Plus `Core/GamePause.cs` (§5.1).

**Config (`UIAConfig`)**: keep `GuideShown` (semantic: "first-run coach shown once") — no
rename, no migration needed; add `TutorialCompleted` (bool, default false) and
`TutorialAutoPause` (bool, default true, section "1. General"). Any future key
removal/rename ships a `ConfigMigration` step per the standing rule.

**Console**: `uiatutorial` (replay; UI-only). Guide tab gains "Replay tutorial".

**Safety rails** (standing rules): client-only, zero game-state mutation (the ONLY new
game-state call in this whole feature is `WorldManager.SetGamePause`, latched per §5);
hot-reload — every static (coach open-state, demo stage, pause latch) resets in `Shutdown()`,
canvases destroyed, `OnPaused` unsubscribed; ASCII-only displayed strings; all clocks
`Time.unscaledTime`; honor the strengthened auto-open gate (§3).

---

## 9. The Designer Handbook — a PDF, in and out of game (replaces the GitHub guide)

Per decision 4. Three pieces:

**9.1 The document.** `Documentation/HUD-Designer-Handbook.md` is the SOURCE (I draft it,
FlorpyDorp edits). Content outline: the profile/element model, the F9 tour (tabs, sub-tabs,
per-element popup), per-tier style forks, per-category follow/inherit, effects (glass, edges,
glow, bloom, alerts, transitions), the Universal Inventory / radial / menu theming, sharing +
restoring profiles, and the gotchas (per the current 0.9.2.5+ systems). No interaction claims
that contradict `Interaction-Model-Reference.md`.

**9.2 The pipeline (dev machine, wired into `tools/package.ps1`).**
- Markdown -> **PDF** via headless Edge/Chrome `--print-to-pdf` (present on every Windows dev
  box; no new toolchain) with a print stylesheet matching the mod's look.
- PDF -> **page images** (JPG ~1400 px wide, ~100-150 KB each) via poppler `pdftoppm` (a
  pinned binary under `tools/`), staged as `Handbook/pages/NN.jpg` + the PDF itself as
  `Handbook/HUD-Designer-Handbook.pdf` in the mod folder.
- Expected weight: 12-20 pages = roughly 2-3 MB — acceptable (the zip is ~1.5 MB today).
  `package.ps1` must stage the `Handbook/` folder (remember: dist/ staging goes stale — sync
  before zipping).

**9.3 In-game reading — the honest design.** A true runtime PDF parser on Unity/net48 means a
native PDFium dependency (megabytes, platform risk, another thing SLP must load) — **rejected**.
Instead:
- **`HandbookViewer`** (new window on the existing kit): renders the pre-rendered page images
  — Prev/Next, page counter, fit-to-width; pages loaded lazily via `Texture2D.LoadImage` from
  `ModData.DirectoryPath/Handbook/pages/`, the current +-1 page kept, others destroyed
  (memory). To the player this IS "a PDF reader in the game"; implementation-wise it is an
  image flipbook with none of the risk.
- **"Open the PDF file" button** alongside it: `Application.OpenURL("file:///<mod>/Handbook/
  HUD-Designer-Handbook.pdf")` -> the system viewer, for reading outside the game / printing.
- Entry points: the tutorial's final step, and an **Advanced-menu "Designing your HUD"
  section** (3-4 sentences + the two buttons). The F10 Guide tab links it too.
- Degrade soft: if `Handbook/` is missing (hand-installed partial copy), the buttons show a
  "handbook not found in the mod folder" note instead of throwing.

---

## 10. Implementation phases (content only — execution order to be discussed)

- **Phase 1 — MVP coach:** the window, 15 steps with static diagrams + live glyphs,
  strengthened first-run trigger, **auto-pause + the F10 pause button (`GamePause`)**,
  Back/Next/Skip/replay, `uiatutorial`, config keys, the §11 stale-text cleanup rider.
  Useful with zero animation.
- **Phase 2 — the 11 demos**, landed incrementally (the "programmatic showing" the request
  centers on).
- **Phase 3 — the Handbook**: draft the markdown, build the render pipeline, ship the PDF +
  pages, build `HandbookViewer`, wire the Advanced-menu section. (Independent of Phases 1-2;
  can run in parallel or after.)
- **Phase 4 (optional, post-launch) — live practice**: convert 2-3 steps to "now you try"
  (detect a real wheel-open / B / G and auto-advance).

FlorpyDorp decides sequencing and what gates launch — parked for the follow-up discussion.

---

## 11. Cleanup rider (ship with Phase 1 — the mod must not contradict its own tutorial)

From the survey (`Interaction-Model-Reference.md` §22): fix the stale R-key config
description; rewrite the **Guide tab** copy to the Hub grammar + add a Universal Inventory
section; fix the Grid key "rebindable in game Controls" claim; fix the scroll-select and
"G stows into the highlighted cell" texts (StorageTab + config descriptions); delete dead
`BagGridCell.StowHereFromKeyboard`; fix the "MORE wedge" config text; correct the two stale
code comments; (optional) unify `KeybindChipsWidget`'s duplicate glyph formatter.

---

## 12. Remaining decisions for FlorpyDorp

1. **Step list sign-off** — are the 15 steps in §6 right (merge 4+5? split 9? anything
   missing)? Copy tone notes welcome; facts are locked to the reference doc.
2. **Handbook viewer scope for v1** — full in-game page viewer (recommended, §9.3) or ship
   PDF + "open in system viewer" button first and add the viewer later?
3. **Skip policy** — skipping counts as "shown" (never auto-prompts again; Replay button is
   the re-entry). Confirm.
4. **Handbook outline sign-off** (§9.1) — and whether I draft it now so review can overlap
   the coach build.

## 13. Rough effort

- Phase 1: ~2-3 focused days (window + 15 steps + diagrams + pause plumbing + cleanup rider +
  changes report).
- Phase 2: ~3-4 days (11 demos, incremental).
- Phase 3: ~2-3 days (handbook draft ~1, pipeline ~0.5-1, viewer ~1).
- Phase 4: ~2+ days, post-launch.
