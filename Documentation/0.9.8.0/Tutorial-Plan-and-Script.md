# UI Ascended - In-Game Tutorial: Plan and Complete Script

> **Wave:** 0.9.8.0 - D-025 (onboarding), folds in D-024 (tablet / cartridge friction).
> **Status:** plan only. No code was changed to write this.
> **For:** FlorpyDorp - read end to end, then line-edit Part C. Every string a player sees is
> written out verbatim there; nothing says "explain X here".
> **Supersedes:** the curriculum and pacing of `Documentation/tutorial Plans/First-Run-Tutorial-Plan.md`
> (Rev 2). It KEEPS that plan's pause, modal and demo-stage engineering, which shipped in 0.9.7.0.
> **Ground truth:** code as of `71648eb` (0.9.7.4), the in-flight wave list in
> `Documentation/0.9.8.0/README.md`, decompile V27758, and the game's own new-player kit
> (`StreamingAssets/Data/startconditions.xml`).

## The short version

1. **First world entry with the mod, single-player:** the game pauses and a Welcome card offers
   *Start (3 minutes)*, *Teach me as I go*, or *No lessons, thanks*.
2. **Start = 8 hands-on steps.** Each is one short line that fades in at the top of the screen
   with a tiny looping animation, tells the player what to do, waits until they actually do it,
   confirms, and fades to the next. The game runs while they practise - wheels and the
   Universal Inventory cannot open on a paused world (verified in code, A.5).
3. **Everything else is taught just in time:** 17 short lessons that fire the first time they
   matter - first tablet in hand, a second belt in the bag, suit power lost, first F10 - instead
   of 15 cards up front (Dipole: "front-heavy... I don't feel like I retained much").
4. **Multiplayer can't pause**, so there the first run is a quiet invitation, not a modal.
5. **Build on `TutorialCoach`** (its modal / pause / token / text-override plumbing is right);
   add a director, a fade-in strip, a signal bus, a spotlight and a progress store.
6. **19 lessons (0-18), 82 steps** plus 7 one-line "as I go" tips, about 15-20 dev-days in 5
   phases. Decisions needed: Part E.

## Contents

- [How to read and edit Part C](#how-to-read-and-edit-part-c)
- [Part A - Design plan](#part-a---design-plan)
- [Part B - Research](#part-b---research-6-point-format)
- [Part C - The complete script](#part-c---the-complete-script)
- [Part D - Engineering](#part-d---engineering)
- [Part E - Open questions](#part-e---open-questions-for-florpydorp)

---

## How to read and edit Part C

**Quoted text is exactly what the player reads** (with default key bindings). Edit it freely.
Rules that keep it working:

1. **ASCII only.** The game's TextMeshPro font draws Basic Latin only - no curly quotes, no
   em-dashes, no arrows, no degree sign. Use `'`, `"`, ` - `, `>`. (The in-game editor and the
   text store already force this; `uiatutorial lint` would check it - D.2.)
2. **`[Key]` is a live key glyph, not text.** The code swaps each bracket for the player's
   CURRENT binding, so the lesson can never lie after a rebind. Mapping:

   | You write | Token in code | Default | Notes |
   |---|---|---|---|
   | `[MMB]` | `{UIA_ToolbeltRadial}` | Middle mouse | belt wheel |
   | `[R]` | `{UIA_ToolRadial}` | R | held-item wheel; also "open device" in the big window |
   | `[Tab]` | `{UIA_BagRadial}` | Tab | bag wheel / The Hub root |
   | `[B]` | `{UIA_Grid}` | B | Universal Inventory |
   | `[F10]` | `{UIA_Menu}` | F10 | Control Center |
   | `[F9]` | `{UIA_HudDesigner}` | F9 | HUD Designer |
   | `[Q]` | `{UIA_Page}` | Q | only while a wheel is open (page / belt chooser) |
   | `[C]` | `{UIA_FineAdjust}` | C | fine value steps |
   | `[E]` | `{V:SwapHands}` | E | gameplay hand swap (vanilla key). Inside a wheel the same default is `{UIA_HandSwap}` - each step's **Keys** line says which |
   | `[G]` | `{V:SmartStow}` | G | vanilla key, SmartStow+ behaviour |
   | `[F]` | `{V:InventorySelect}` | F | vanilla key |
   | `[Alt]` | `{V:MouseControl}` | Left Alt | free the mouse (vanilla key) |
   | `[1]` .. `[6]` | `{V:HelmetSlot}` `{V:GlassesSlot}` `{V:SuitSlot}` `{V:BackSlot}` `{V:UniformSlot}` `{V:ToolBeltSlot}` | 1 .. 6 | a single vanilla gear key, read live (the script uses `[3]`, `[4]`, `[5]`, `[6]`) |

   Written literally (never bracketed, not rebindable or not a single key): `Shift`, `Ctrl`,
   `Esc`, `LEFT-CLICK`, `RIGHT-CLICK`, `SCROLL WHEEL`, the ranges `1 - 6` and `Ctrl + 1 - 0`,
   and digits pressed *inside* a wheel (hard-wired number row, `BagHotkeyStore.cs:49-50`).
   All vanilla names verified: `KeyManager.cs:559-601` (V27758).
3. **Step ids** (`core.take`) key your saved text overrides. Change the words, not the ids.
4. **Budgets:** strip line (**Says**) <= ~125 characters (two lines); confirmation (**Then**)
   <= ~100; F10 callout <= ~160; card body <= ~280; titles <= 28. Over budget the text
   auto-shrinks, but reads worse. A line break inside a quoted card body is a real line break
   on screen (`\n` in code).
5. **The bold step title is on screen too** - it is the heading when a lesson is replayed from
   the GUIDE tab, and the row name in the lesson list.

**Presentation types**

| Type | What it is | Game |
|---|---|---|
| **CARD** | Today's coach window: title, looping demo, body, buttons. Modal. | PAUSED in single-player |
| **STRIP** | New: one slim glass line that fades in top-centre, with a small looping demo on its left. Never takes the mouse or keyboard, never blocks a click. | RUNNING |
| **CALLOUT** | A small card pinned next to part of the F10 menu (tour only). | PAUSED in single-player |
| **SPOT** | A pulsing outline drawn around a REAL HUD element (hand boxes, compass...). | either |

**Skip patterns** (referenced per step)

- **S1 self-skip:** a do-step not done after 45 s adds the line "No rush - this step skips
  itself in 15 s." then advances. The step counts as *shown*, not *learned*, so its
  just-in-time twin can fire later.
- **S2 timed read:** a read-only step advances by itself after its reading time
  (2.5 s + 0.06 s per character, 5-12 s), shown as a thin line draining under the text.
- **S3 card:** Next / Back / Skip lesson buttons; Enter = Next; Esc = Later.
- **S4 context lost:** if the reason for the lesson disappears (tablet put away), the lesson
  pauses and is offered again later (at most 3 offers, 10+ minutes apart).
- **S5 global:** F10 > GUIDE always has *Skip this lesson* and *Stop all lessons*.

**Flags:** `[NEW THIS WAVE]` depends on a 0.9.8.0 fix-wave change - copy is written to the wave's
stated behaviour; `[VERIFY]` check once that change lands; `[PENDING D-016]` depends on the
Simple SmartStow plan; `[DECISION]` your call.

---

## Part A - Design plan

### A.1 Goals

1. **Retention over coverage.** Teach the few moves a player needs in the first ten minutes by
   having them *do* them; teach the rest at the moment it becomes useful.
2. **Pause to read, play to learn.** Every card pauses single-player. Every "now you try" runs
   on the live world, because that is the only world the wheels work in.
3. **Fix the three named complaints directly:** the swipe-vs-click guess and the tablet flow
   (D-024, Dipole), "sorting with only the wheel" and "feels like a full overhaul" (Ningy -
   answered by teaching the vanilla-style window in the core and the two master switches in
   the F10 tour), "the bare HUD feels empty" (StormCircuit - a lesson that explains the senses).
4. **Never lie.** Live key glyphs everywhere (including inside the animations, which today
   hard-code `Alt`, `B`, `F`, `G`, `R`, `C`), copy written from the code, `[VERIFY]` wherever
   the wave is still landing.
5. **Zero game-state risk.** The tutorial never moves an item. The player's own actions go
   through the normal funnels; the tutorial only watches and talks. Its only game call stays
   `WorldManager.SetGamePause`, single-player only, through `Core/GamePause.cs`.

**Non-goals:** voice-over, video, controller prompts, localisation (English ASCII only),
teaching F9 in depth (that is the Designer Handbook).

### A.2 What players told us (data, not instructions)

| Id | Who | What | Where it lands |
|---|---|---|---|
| D-025 | Dipole | "The tutorial is definitely a front-heavy experience. I don't feel like I retained much" | A.7 pacing model |
| D-025 | Ningy | Popup opened unpaused (fixed 0.9.7.0); defaults feel like a full overhaul; "SUPER confused as to how to sort my inventory using only radial menu" | core 1.8 (the window), lesson 16 (master switches) |
| D-025 | StormCircuit | Without a hardsuit the HUD "was very empty"; "imagine I begin to suffocate" | lesson 15 (senses), safety gate A.5 |
| D-024 | Dipole | Accidental cartridge ejects; `R` on a held tablet not discoverable; can't guess swipe vs click; "a delay in opening the arrow menu" made him second-guess; tried clicking and right-clicking to open | core 1.3 + 1.7, lesson 3 tablet variant; copy says "slide out and stop" not "swipe" |
| D-004 / D-021 / D-022 / D-023 / D-007 / D-064 / D-018 / D-005 | various | drop-on-close, hint ring, Take/Open word, recent item, Q from 6, RMB back, Shift+drag, split popup | `[NEW THIS WAVE]` steps |

Also from the same thread: Dipole read the old "park" copy as "a sticky reference which would
let me quickly come back to an item" and found "needing to close the menu to drop items is not
very guessable" - lesson 5 states the rule outright.

### A.3 What exists today (honest inventory)

| Piece | File | What it really does | Keep? |
|---|---|---|---|
| Coach window | `UI/Menu/Tutorial/TutorialCoach.cs` | Modal card (sort 5600) over a click-absorbing scrim: title, step heading, 680x300 demo area, auto-shrinking body, progress dots, Skip / Back / Next (Enter) / Esc; header pause button; theme-following rebuild; dev edit mode. Opens at step 1 only. | Yes - extend |
| Pause | `Core/GamePause.cs` | Named-reason SP latch over `WorldManager.SetGamePause`; `CanOwnPause()` false in MP / host-with-clients / vanilla menu (`:79-91`); drops its latch if vanilla unpauses; unwind order documented and enforced in `TutorialCoach.Close` (`:137-155`). | Yes, as-is |
| First-run trigger | `StationeersUIMod.cs:395-420` | Opens the coach once per install when: in world, menus toggleable, nothing else open, input state Game, not paused, no vanilla menu, responsive body, held for 1.0 s (`:262`). Consumes `GuideShown` only if the coach really opened. | Yes - moves into the director |
| Steps | `UI/Menu/Tutorial/TutorialSteps.cs` | Flat list of 15 steps (id, heading, body, demo id). Tokens `{UIA_*}` / `{V:*}` resolved at display time (`TutorialCoach.cs:644-690`). | Replaced by chapters |
| Text overrides | `UI/Menu/Tutorial/TutorialTextStore.cs` | Per-step heading/body overrides in `config/StationeersUIMod/Tutorial/TutorialText.xml`, ASCII-sanitised on load and save (`:214-232`); `uiatutorial edit/reset`. | Yes - add a Confirm field |
| Demo stage | `UI/Menu/Tutorial/TutorialDemoStage.cs` | 14 looping schematic scenes on a 440x220 design box, uniformly scaled; `Time.unscaledTime` (animates while paused); zero per-frame allocation; toolkit: Panel, Trapezoid, Label, Triangle, Circle, Wedge, drawn X, Crosshair, Keycap (press state), fake Cursor (click state), Wheel (N wedges + hub readout + Close band, bloom, tint), Cells (slot grid). | Yes - extend |
| Replay | `Core/FinderCommands.cs:209-244`, `UI/Menu/Tabs/GuideTab.cs:33-36` | `uiatutorial [edit\|reset]`; Guide tab "Replay the tutorial" button. | Yes - extend |
| Config | `UIAConfig.cs:325-333` | `GuideShown`, `TutorialCompleted`, `TutorialAutoPause`. | Yes, same meanings |

**What it cannot do today:** only one presentation (a modal card, so nothing can be practised
while it is up); no chapters, no triggers, no idea whether the player actually did anything;
no persistence beyond two booleans; no way to point at the real HUD; a mini demo would be
unreadable (labels are 7-9 px in design space, so at strip size they shrink to ~3 px - see
A.9); MP handled only by swapping one sentence.

**Already stale, fix whatever else happens:**

1. Step `pin` says Shift + 1 - 6 and clicking a HUD box *pin* a bag. Since 0.9.7.4 both open the
   bag *inside* the Universal Inventory; only tearing a tab out pins
   (`StationeersUIMod.cs:856-891`). The `pinflow` demo's second beat animates the old behaviour.
2. Step `scrollpark` and the Guide tab say the Close band drops parked items and Esc cancels.
   D-004 makes drag-out + close = drop on every close route.
3. Demo captions hard-code key names (`TutorialDemoStage.cs:779-785, 806, 1279, 1425-1428,
   1560-1566, 1594, 1659, 1696, 1727-1729, 1963, 1991`) - they lie after a rebind, against the
   token rule in `TutorialSteps.cs:26-44`.
4. Guide tab: "Pick a ready-made look on the Profiles tab" (renamed HUD Themes in 0.9.7.0);
   "Shift + 1 - 6 ... pins a worn container too" (see 1).
5. The Rev 2 plan's "[ ] Do not show tips again" was never built (no config for it).

### A.4 Audiences and entry points

| Player | What they get |
|---|---|
| **Fresh install, new game** | Spawns seated in a descending `LanderCapsule` (`Assets/Scripts/Objects/LanderCapsule.cs:92` `IsDescending`) with the default kit: EVA suit, jetpack holding a **tablet + 3 loose cartridges + a mining belt + 20 flares**, a toolbelt of 8 (Wrench, Crowbar, **Drill**, Wire Cutters, **Welding Torch**, Screwdriver, **Angle Grinder**, **Cable Coil x50**), duct tape in a hand (`startconditions.xml:1504-1591, 1635-1645, 1816-1835`). The sealed descent is the safest minute in the game - the core block is sized to fit it. The copy leans on this kit (the Drill has a battery arrow, the tablet ships with *no* cartridge in it). |
| **Fresh install, existing save** | Same Welcome, but they may be anywhere - hence the safety gate on Start (A.5). Copy never assumes the default kit; kit items are only ever examples. |
| **Multiplayer client, or host with players** | No modal. A 12 s invitation strip (0.2). `GuideShown` is not consumed, so their first single-player world still gets the paused Welcome. |
| **Updater from 0.9.7.x** (`GuideShown=true`, no progress file) | No Welcome. A one-time "What's new" card (lesson 18). Just-in-time lessons ON by default `[DECISION]`, each turning itself off if the player already shows the skill (A.7). |
| **Quit halfway through the core** | "Welcome back" card (0.3) on the next world entry. |

### A.5 Trigger, pause, and why practice runs on the live world

**Trigger.** Keep today's gate and 1 s settle exactly (`StationeersUIMod.cs:400-419`) - it
already means "first time safely in control of a character", which covers both "first new
game" and "first save opened with the mod". The block moves into `TutorialDirector.Tick()`.
"Later" re-arms it for the next world entry (at most twice, then it becomes Teach me as I go).

**The pause rule - read this before approving.** Wheels and the Universal Inventory are
gameplay. On a paused world they cannot open, and an open wheel closes:

- `Guards.CanAcceptGameplayInput()` refuses on `WorldManager.IsGamePaused` (`Core/Guards.cs:40`)
  and on any input state but Game (`:45`); `RadialController.Update` then drops the key
  (`Features/RadialController.cs:190-194`).
- `Guards.CanKeepRadialOpenWhy()` returns `"paused"` (`Guards.cs:131`) and the open wheel
  closes (`RadialController.cs:305-310`).
- `Guards.CanOpenUniversalInventory()` refuses when paused (`Guards.cs:65`).
- Vanilla `SetGamePause(true)` pushes `KeyInputState.Paused`, sets `Time.timeScale = 0` and
  pauses the Room, Occlusion, Electricity, Atmospherics and Light managers (`WorldManager.SetGamePause`, V27758).

So: **cards pause, strips don't.** The Welcome, Finish, Welcome-back, Hold-on, F10-tour and F9
cards all hold the pause; every "now you try" runs live. A "practice freeze" (wheels usable on
a frozen world) is possible in principle - `InventoryManager` is not in vanilla's paused set -
but it needs new exceptions in three guards, a per-mutation audit at timeScale 0 (drops to the
floor spawn physics objects that hang in the air, world-reach and the placement cue need
physics), and it breaks `GamePause`'s "never fight vanilla" contract. Recommended: don't,
unless play-tests demand it (Part E, Q1).

**Safety gate.** Before practice starts, and while it runs, `TutorialSafety.IsCalm()` must be
true: HUD snapshot valid, `SuitStatusLevel < 3` (`UI/Hud/HudSampler.cs:45`, set by `Worst()`
`:703-706`), and no *Critical* felt-sense band active (`UI/Hud/SenseCatalog.cs` bands such as
CHOKING, VACUUM, FREEZING). Not calm on Start -> card 0.4. Not calm for 2 s mid-lesson -> the
strip turns to "Lesson on hold..." and resumes 5 s after calm returns.

```
first world entry with the mod
  |  (existing gate holds 1 s)
  |-- MP client / host with players --> 0.2 invite strip (GuideShown untouched)
  v single-player
[CARD 0.1 Welcome]  ...................................... game PAUSED
  |-- Start ------------> calm? --no--> [CARD 0.4 Hold on]
  |                         | yes
  |                         v unpause
  |                   STRIPS 1.1 .. 1.8  ................... game RUNNING
  |                         v
  |                   [CARD 1.9 Finish] .................... game PAUSED
  |-- Teach me as I go --> unpause; core becomes one-line tips (table in Lesson 1)
  |-- No lessons -------> unpause; lessons off (GUIDE tab turns them back on)
  '-- Esc / X = Later --> unpause; ask again next world entry (max 2)
afterwards:
  lessons 2-15  -> fire on their triggers as STRIPS (running)
  lesson 16     -> first F10 open: CALLOUTS (paused)
  lesson 17     -> first F9 press: CARD (paused)
  lesson 18     -> updaters only: CARD (paused)
```

### A.6 Multiplayer

**Proposal:** on an MP client, or a host with anyone connected (`GamePause.CanOwnPause()` false),
never auto-open a modal. Show the invitation strip once per session; lessons stay available
from F10 > GUIDE; just-in-time strips work normally (they never take input).

**Why:** vanilla has no multiplayer pause - `NetworkBase.PauseEvent` is protected, Esc does not
pause a client, a host with players gets a local-only pause that desyncs prediction
(`Documentation/Interaction-Model-Reference.md` 20). A full-screen modal that grabs the cursor
while your body stands in a live, shared world is precisely Ningy's complaint. An invitation
respects the player's timing; the strip layer is safe by construction because it owns no
input. If an MP player starts a lesson themselves, cards show the running-game sentence
(today's `RunningLine` swap, `TutorialCoach.cs:60-61`) and the header pause button is greyed,
as now.

### A.7 Pacing model (the retention fix)

- **Core block, ~3 minutes, 8 do-steps** (Lesson 1). Only the grammar every other lesson
  builds on: two hands, open a wheel, read the TAKE/OPEN word, take, put back with G, open and
  step back, slide out for more, the one big window. Tap only - hold-to-flick is taught later.
- **Just-in-time lessons** (2-15), each 2-6 steps, fired by the moment of need. A lesson is
  offered only when: lessons are on; the core is done, skipped or in "as I go" mode; no F9,
  F10, console or vanilla menu is up; `IsCalm()`; 60 s since the last lesson ended; 20 s since
  world load. One lesson at a time; triggers that arrive meanwhile queue (max 3, dropped if their
  context is gone). A strip may appear WITH a wheel open - that is usually the moment of need.
- **Skill evidence:** if the player already does the thing, the lesson marks itself *learned*
  and never shows (e.g. a successful REPLACE before lesson 3 fires). Listed per lesson.
- **Spaced reinforcement:** the wheel's hint ring already fades each hint after 25 wheel-opens
  (`UI/RadialHintBar.cs:47`); core steps skipped by S1 fire their twin later; ignored offers
  come back up to 3 times, 10+ minutes apart; the "Faster wheels" lesson waits for the 20th
  belt-wheel open. (Hodent: spacing beats massing; Andersen: context beats up-front.)
- **Teach me as I go** turns the core itself into one-line tips (table in Lesson 1).

### A.8 Presentation - the fade-in strip, cards, spotlight

**Strip** (new `TutorialStrip`): own ScreenSpaceOverlay canvas at sort **5150** - above the HUD
(3800), hint ring (4999), wheel (5000), search (5005), parked items (5010), Universal Inventory
(5020) and pins (5030); below F10 (5200), drag ghosts (5250), the coach (5600), the Handbook
(5700) and tooltips (6000). No GraphicRaycaster: it can never eat a click.

```
+---------------------------------------------------------------------+
| [mini ]  FIRST STEPS - 3/8                                          |
| [demo ]  Point at a few wedges. The word at the top of the wheel    |
| [150x76]  says what a click will do: TAKE or OPEN.                  |
+---------------------------------------------------------------------+
```

- Place: top-centre, 170 px below the top edge at 1080p, 560-760 px wide, 64-96 px tall. All four
  shipped themes share one top layout (top bar at y -38, moodlet row at y -105, h 64 - ends
  ~137 px down), so this spot is clear in every one of them.
- Dodge rules: (a) while a wheel is open the strip rises to 100 px from the top (over the
  moodlet row, which is look-only) - a child wheel off the top wedge reaches ~220 px down at
  default size and the new top word sits just above the ring; (b) if the Universal Inventory or a
  pinned window covers the spot (default window 560x760, centred, top edge ~160 px), the strip
  slides beside that window.
- Enter: alpha 0 -> 1 over 0.35 s (smooth step) while rising 8 px; the demo follows 0.15 s later.
- Change step: old text fades out 0.2 s, new fades in 0.35 s.
- Success: border flashes the theme's Good colour (0.45 s), a drawn tick grows in (two panels -
  never a glyph), the **Then** line shows for its reading time (min 1.4 s), then next step.
- Waiting: after 20 s without progress the `[Key]` in the text pulses once every 4 s.
- On hold / waiting: text swaps to the chrome lines in C.19.
- Looks: `UiaTheme` menu skin + the same `HudGlobalGlass.Apply` the coach uses, so it follows the
  active HUD Theme with no new knobs (CLAUDE.md rule 8: shared, travels with the theme's
  `menu:` family; nothing per-tier; nothing to migrate).
- Clocks unscaled, text built only on step change, zero allocation per frame.

**Cards** reuse the coach with three additions: open at any step; a per-card button row
(Welcome's three choices, Finish's two); and an optional *scrim with a hole* (four dark panels
around a target rect) so a card can show a SPOT on the real HUD.

**Callouts** (F10 tour only): a 380x200 coach card pinned beside an F10 anchor rect, sort 5300,
no scrim of its own (F10 has one), Back / Next / Skip tour.

**Spot** (new `TutorialSpotlight`, sort 5140): finds live element views by type
(`HudSystem.CollectElementViews`, `UI/Hud/HudSystem.cs:2110`), forward-warps their corners
(`HudWarp.WarpPoint`, `UI/Hud/VisorWarp.cs:54`) exactly as the F9 handles do, and pulses an
accent outline 8 px outside them (alpha 0.35-1.0 at 1.2 Hz). If the element is not in the
player's profile or hidden at this tier, the spot is skipped and the copy still stands
(copy names elements, never screen positions).

### A.9 Animation approach

**What the stage can already do** (`TutorialDemoStage.cs`): build schematic shapes from the mod's
own graphics, then move / scale / tint / fade them on an unscaled clock with zero allocation.
The toolkit already covers wheels (with hub readout and Close band), cursors with click state,
keycaps with press state, slot grids, item hops (`Arc`), flashes (`Bump`), crossfades.

**Two limits that shape the plan:**

1. **Size.** Everything is authored in a 440x220 box and uniformly scaled
   (`FitStage`, `:155-164`, clamp 0.15-3). In a 150x76 strip slot that is x0.34 - fine for shapes,
   useless for the 7-9 px labels most scenes rely on. Strip demos therefore need their own
   *mini* layouts: big shapes, no words except keycaps.
2. **Hard-coded key text** (A.3 item 3). Fix: `Show(id, glyphResolver)` - scenes ask for
   `"{V:MouseControl}"` and get `Alt` or whatever the player bound.

**New workhorse - `keys` micro-scene.** One parameterised scene that draws a keycap or mouse and
plays a pattern with the LIVE glyph: `tap`, `hold`, `double`, `chord` (Shift/Ctrl + key),
`lmb`, `rmb`, `mmb`, `scroll`, `drag`, `shiftdrag`. It gives most strips a correct, rebind-safe
demo on day one; bespoke scenes come after.

| Scene | Status | What moves | Used by |
|---|---|---|---|
| `keys:<pattern>:<token>` | NEW | keycap/mouse presses in the named pattern, live glyph | most strips |
| `welcome` | exists | wheel / window / two hands light in turn | 0.1 |
| `finish` | modify | rows become GUIDE / CONTROLS / `[F9]` + Handbook | 1.9 |
| `themeswitch` | exists | Stationeers Blue <-> Pure HUD crossfade | 16.3 |
| `handswap` | NEW | two hand boxes; `[E]` keycap presses; the orange active border and side bar hop across | 1.1 |
| `actionword` | NEW `[NEW THIS WAVE]` | cursor glides tool wedge -> bag wedge; curved word over the ring flips TAKE <-> OPEN (curved with `Overlay/RadialArcText.Curve`, once at build); doubled chevron pulses | 1.3, 18.2 |
| `takeandstow` | NEW (beltwheel + stowflash parts) | click a tool; icon arcs into the lit hand box; `[G]` keycap; icon arcs back to the BELT box, which flashes | 1.4, 1.5 |
| `hubback` | modify beltwheel | click THE HUB, ring relabels; a right-button badge lights; ring relabels back; Close band flashes | 1.6 |
| `pushout` | modify swipechild | slower approach, doubled chevron, cursor STOPS past the rim while a dwell ring fills, child blooms, pull-back dismisses | 1.7, 18.2 |
| `gridintro` | modify gridpeeklatch | `[B]` tap -> window slides up; `[Alt]` double-tap -> crosshair becomes cursor; a folder tab unfolds | 1.8 |
| `readout` | NEW | hub lines appear one by one (name / action / detail / red warning); a grey wedge with its red reason | 2.1, 2.2 |
| `stowwedge` | NEW | empty STOW wedge; hover shows the held item's ghost; click slides it in | 2.3 |
| `hintring` | NEW `[NEW THIS WAVE]` | curved hint text under a wheel; items fade out one by one | 2.4 |
| `toolreplace` | modify batteryswap | adds the warning beat: hover battery -> top word TAKE in warn colour -> cursor goes outward instead | 3.1-3.4 |
| `tabletinstall` | NEW (from batteryswap) | tablet ring; empty CARTRIDGE wedge; push out; three cartridges bloom; one flies in | 3.1T-3.3T |
| `devicewindow` | NEW (pinflow parts) | window cell with a tablet; click; a small slots window pops beside it | 3.4T, 12.3 |
| `split` | NEW | stack wedge; push out; SPLIT ONE / HALF / COUNT child; count ticks with a scroll glyph; chip hops to a hand | 4.1, 4.2 |
| `splitpopup` | NEW `[NEW THIS WAVE]` | window cell `x17`; click; compact square buttons pop; the number button ticks | 4.3 |
| `dragpark` | modify valueandpark (right half) | drag two chips out; RMB badge closes the wheel; chips fall | 5.1, 5.2, 18.3 |
| `worldslot` | NEW | chip over a machine-slot icon; placement box cycles green / yellow / blue / red | 5.3 |
| `altreach` | NEW | `[Alt]` held, ring dims; cursor lifts an item off a floor line into a wedge | 5.4 |
| `equiptaphold` | modify static.equipkeys | `[3]` tap -> small suit wheel blooms; `[3]` held -> suit icon hops to a hand | 6.1, 6.2 |
| `ingrid` | NEW | `Shift` + `[4]` -> a BACK region unfolds in a window; again -> folds | 6.4 |
| `beltswap` | NEW `[NEW THIS WAVE]` | belt ring; `[Q]`; ring relabels to belts; click swaps; RMB badge -> back to the belt ring | 7.1, 7.2, 18.4 |
| `ghostlabel` | NEW | empty belt wedge shows a grey tool name along its inner edge | 7.3 |
| `hubroot` | NEW `[NEW THIS WAVE]` | hub ring: RECENT ITEM (labelled), SEARCH, worn gear; a bag wedge dives | 8.1, 8.2, 8.4 |
| `search` | NEW | SEARCH box types `c`, `ca`, `cab` (cached strings); results fill the lower half; click | 8.3 |
| `bindbag` | NEW | hover a bag + `7` -> digit badge; later Ctrl + `7` -> that bag's wheel blooms | 8.5 |
| `stowroute` | modify stowflash | four chips BELT SLOT > STACK > SOCKET > BAG RULES light in order before the item flies | 9.1 |
| `valuescroll` | exists (valueandpark left half, own id, live `[C]`) | value between two arrows ticks with scroll; `[C]` makes it tick by one | 10.1 |
| `hotkey` | NEW | hover a setting wedge + letter keycap -> letter badge; outside the wheel the letter flips an icon | 10.2 |
| `shiftkeep` | NEW | click with Shift held -> wheel stays; without -> wheel closes | 10.3 |
| `holdflick` | modify tapvshold (right half) | key held, sweep, release on a wedge | 11.1, 11.2 |
| `hubdrag` | NEW | cursor drags the hub; the whole wheel slides | 11.3 |
| `gridmoves` | modify gridregimes (freed half) | click takes; right-click blooms a small wheel; drag moves | 12.2 |
| `shiftdrag` | NEW `[NEW THIS WAVE]` | Shift held + drag one cable cell; all cable cells lift and fly together | 12.4, 18.5 |
| `pintear` | modify pinflow | keep the tab tear-out and the pin's X; DELETE the stale click-a-HUD-box-to-pin beat | 12.5 |
| `scrollselect` | modify gridregimes (captured half) | highlight steps with scroll; `[F]` takes; `[R]` opens a device | 12.6 |
| `bagnames` | NEW | two identical BACKPACK wedges; one becomes TOOLS in blue, the other ORES in orange | 13.1, 13.2 |
| `baresenses` | NEW | an empty visor; COLD fades in bright, settles dim; THIRSTY joins; both fade away | 15.2 |
| `lowpower` | NEW | a HUD box flickers and drops out; battery reads 9% | 14.6 |
| SPOT only | - | real HUD elements outlined | 1.1, 14.x, 15.1 |

### A.10 Skip, replay, resume

- **Precedent:** the `B` key. The Universal Inventory never takes the mouse or head-look and Esc
  does not close it (`StationeersUIMod.cs:540-545`, `TheGridPanel` Esc falls through to vanilla).
  The strip follows the same contract: it never captures input, never frees the cursor, never
  blocks a click and never listens to Esc - so no new key is needed. Its controls live in
  F10 > GUIDE, and steps finish by being done (or S1 / S2).
- **Cards** keep today's grammar: Next / Enter, Back, Skip, Esc (with the existing
  `ModalInputChain` Esc-swallow so the vanilla menu never pops on the key-up).
- **Replay:** the GUIDE tab lists every lesson with its state. *Watch* replays it as cards
  (paused SP): each step's title, **Says**, **Then** (dim) and its demo; the last card offers
  *Try it now* (live strips) when the lesson's context exists. `uiatutorial` gains
  `list`, `play <lesson>`, `restart`, `tips on|off`, `lint`; `edit` and `reset` keep today's
  meaning (text overrides). Bare `uiatutorial` still replays First Steps.
- **Resume:** an interrupted core resumes at its current step after the Welcome-back card (0.3).
  Other lessons simply re-offer later (S4).

### A.11 Build on TutorialCoach, don't replace it

Recommendation: **build on it.** The coach already solves every hard, already-reviewed problem:
modal input with the order-sensitive pause unwind, the Esc key-up swallow, stacked-modal
Typing-state hand-back (`Core/ModalInputChain.cs`), theme-following rebuilds, hot-reload
teardown in the un-skippable `finally`, live glyph tokens, the ASCII-sanitised override store
and in-game editing, and a zero-allocation demo stage with 14 scenes and a reusable toolkit.
What is missing is additive: a second presentation (strip), a scheduler (director), eyes
(signals), a pointer (spotlight) and memory (progress store). A rewrite would re-open the
problems the two 2026-08-01 adversarial reviewers closed, for no user-visible gain.

### A.12 Persistence and "first run"

| Data | Where | Scope | Meaning |
|---|---|---|---|
| `GuideShown` (existing) | `.cfg` "1. General" | per install | Welcome card has been opened (unchanged meaning) |
| `TutorialCompleted` (existing) | `.cfg` | per install | First Steps finished (was: 15 steps read) - informational only |
| `TutorialAutoPause` (existing) | `.cfg` | per install | cards pause single-player (now covers every card, not just first run) |
| `TutorialTips` (NEW, bool, default true) | `.cfg` "1. General" | per install | just-in-time lessons on / off - a personal preference like `ShiftKeepsRadialOpen`: not theme, not per-tier (rule 8) |
| `Tutorial/Progress.xml` (NEW) | `config/StationeersUIMod/` | per install | per lesson: state (New / Offered / Active@step / Done / Skipped / Learned / Later), offer count, last offered (UTC), script version; counters (belt-wheel opens, G presses, setting clicks) |
| `Tutorial/TutorialText.xml` (existing) | same folder | per install | your text overrides (+ new Confirm field) |

- **Per install, not per save:** you learn the mod once. (Contrast `HintUsageStore`, which is per
  save by design - keep it that way.)
- **Updaters:** `GuideShown=true` and no Progress.xml -> seed First Steps as Done (if
  `TutorialCompleted`) or Skipped; offer lesson 18 once. Fresh installs mark 18 Learned.
- **Script version:** a later release that adds a lesson bumps it; the director offers only the new
  lesson to players who finished First Steps.
- **`uiareset`** deletes the whole config tree and both `.cfg` names (`FinderCommands.cs:292-360`),
  so after a restart the player gets the Welcome again - the right "fresh install" test.
  `uiatutorial restart` is the narrow version (progress only, text overrides kept).
- **Config migration:** nothing removed or renamed, so no `ConfigMigration` step. If a later change
  renames `GuideShown`/`TutorialCompleted`, it must ship one (rule 8(3)); the counter is
  `ConfigMigration.CurrentVersion = 4` today and the D-020 fix in this wave may take 5.

---

## Part B - Research (6-point format)

References (verified 2026-09-25):

- **[R1]** Andersen, O'Rourke, Liu et al., "The Impact of Tutorials on Games of Varying Complexity",
  CHI 2012 - 45,000+ players, 3 games, 8 tutorial designs: tutorials paid off only in the most
  complex game (up to +29% play time); context-sensitive delivery improved engagement; forcing
  the player through a tutorial ("freedom") made no difference; on-demand help both helped and
  hurt. https://grail.cs.washington.edu/projects/game-abtesting/chi2012/chi2012.pdf
- **[R2]** Page Laubheimer, "Onboarding Tutorials vs. Contextual Help", Nielsen Norman Group, 2023 -
  walkthroughs are skipped, forgotten and interruptive; prefer "pull revelations" triggered by a
  signal the user needs it; exception: onboarding to a novel interaction paradigm.
  https://www.nngroup.com/articles/onboarding-tutorials/
- **[R3]** Factorio Friday Facts #208 (2017) and #361 (2020) - the old start-of-game tips failed
  ("only shown at the start of a new game, when many of the tips are not relevant", not
  reopenable); the redesign unlocks tips by progress and "suggests" them when the player performs
  certain actions, adds "Mark as read", and plays live scripted simulations.
  https://www.factorio.com/blog/post/fff-208 , https://www.factorio.com/blog/post/fff-361
- **[R4]** Celia Hodent, "The Gamer's Brain, Part 2: UX of Onboarding and Player Engagement",
  GDC 2016 (and *The Gamer's Brain*, CRC Press 2017) - learning by doing; distribute learning over
  time (spacing effect); guard cognitive load. https://www.gdcvault.com/play/1023231/
- **[R5]** Dan Cook, "The Chemistry of Game Design", Gamasutra 2007 - skill atoms: action ->
  simulation -> feedback -> updated mental model; skills chain.
  https://www.gamedeveloper.com/design/the-chemistry-of-game-design

**B.1 Staged pacing (core + just-in-time lessons)**
1. *Goal:* the player keeps what they learn; no 15-card wall.
2. *Reference:* R1 (UIA is a complex game - a tutorial is justified, but context-sensitive
   delivery is what moves engagement); R2 (pull revelations; a short walkthrough is still right
   for a novel paradigm - radials are one); R3 (tips unlocked by progress and actions).
3. *Route:* `TutorialDirector` + `TutorialSignals`; lessons as data in `TutorialChapters.cs`.
4. *Facts:* R1-R3 findings above; Discord D-025. *Recommendation:* the core stays at the
   grammar (8 steps); everything else waits for its trigger.
5. *Risks:* a trigger firing at a bad moment (safety gate, rate limits); lessons nobody triggers
   (GUIDE list + "as I go" timers).
6. *Files:* D.2 rows 1-5.

**B.2 Show - do - confirm loops**
1. *Goal:* every step is a skill atom: see it (mini demo), do it (real UI), see the result
   (real feedback + strip confirmation).
2. *Reference:* R5 skill atoms; R4 learning by doing; R1 "freedom" did not matter - so never
   lock input to force the step.
3. *Route:* strip + signals raised by the real code paths after they act; no fake input.
4. *Facts:* the mod's own feedback already exists (belt-box stow flash `Core/SlotFlash.cs`,
   placement box `Core/WorldSlotCue.cs`, wheel sounds). *Recommendation:* confirm on the signal,
   never on a timer, for do-steps.
5. *Risks:* on MP clients the server can refuse after the local gesture - the lesson confirms the
   gesture, which is what it teaches (D.6).
6. *Files:* D.2 rows 3, 4, 13-27.

**B.3 Fade-in strip + spotlight**
1. *Goal:* text that "fades in and tells the player what to do next" without covering the game.
2. *Reference:* R2 (unintrusive, contextual); R3 (live simulations rather than stills).
3. *Route:* `TutorialStrip` (sort 5150, no raycaster) hosting a second `TutorialDemoStage`;
   `TutorialSpotlight` over real HUD elements.
4. *Facts:* canvas orders and warp API cited in A.8. *Recommendation:* no new look knobs - follow
   the menu skin so every HUD Theme restyles it for free.
5. *Risks:* custom profiles with elements where the strip sits (dodge rule covers the windows,
   not arbitrary HUD elements - Part E Q7).
6. *Files:* D.2 rows 6-7.

**B.4 Spaced reinforcement**
1. *Goal:* skills skipped or half-learned come back once, later - not every time.
2. *Reference:* R4 spacing effect; R1 mixed results for on-demand help (so don't rely on the
   GUIDE list alone).
3. *Route:* Progress.xml counters + offer spacing; the existing hint-ring fade
   (`UI/RadialHintBar.cs:47`, per save) as the lowest layer.
4. *Facts:* hint fade exists and resets from F10 > RADIAL. *Recommendation:* max 3 offers, 10+
   minutes apart; skill evidence silences a lesson for good.
5. *Risks:* nagging - `TutorialTips` off switch on every path.
6. *Files:* D.2 rows 2, 5.

---

## Part C - The complete script

### C.0 Lesson map

| # | Id | On-screen title | Fires when | Steps | Type | Priority |
|---|---|---|---|---|---|---|
| 0 | `entry` | (entry cards) | first world entry / resume / emergency / MP | 4 | CARD, STRIP | - |
| 1 | `core` | FIRST STEPS | Welcome > Start | 9 | STRIP + CARD | - |
| 2 | `readwheel` | READING A WHEEL | 8th wheel opened, or a grey wedge clicked | 4 | STRIP | 4 |
| 3 | `tools` | TOOLS AND BATTERIES / TABLETS AND CARTRIDGES | a tool with a battery, canister or cartridge slot in hand 3 s; a held tool <= 20% battery; a tablet in hand | 4 (+4 tablet) | STRIP | 1 |
| 4 | `split` | SPLITTING STACKS | pointer rests on a stack wedge 0.5 s; a stack clicked in the big window | 3 | STRIP | 2 |
| 5 | `dragdrop` | DRAG AND DROP | first item dragged off a wedge; the Drop key pressed in a wheel where it does nothing | 4 | STRIP | 1 |
| 6 | `gear` | GEAR KEYS 1 - 6 | first tap of a gear key | 4 | STRIP | 2 |
| 7 | `belts` | BELTS | belt wheel open while another belt is carried | 3 | STRIP | 2 |
| 8 | `bags` | BAGS AND SEARCH | first [Tab] wheel, or first dive into THE HUB after the core | 5 | STRIP | 2 |
| 9 | `stow` | SMART STOW | 5th successful [G] | 3 | STRIP | 3 |
| 10 | `settings` | SETTINGS AND HOTKEYS | first scroll on a value wedge, 5th setting click, or first suit wheel | 3 | STRIP | 3 |
| 11 | `speed` | FASTER WHEELS | 20th belt wheel opened by tapping | 3 | STRIP | 5 |
| 12 | `window` | THE BIG WINDOW | big window open with the mouse free for 5 s (after the core) | 6 | STRIP | 2 |
| 13 | `names` | NAME YOUR BAGS | two carried containers share a name, 3+ bag wheels opened | 2 | STRIP | 5 |
| 14 | `visor` | YOUR VISOR | suit powered (or robot) for 10 s, 3+ min after the core | 6 | STRIP + SPOT | 3 |
| 15 | `senses` | YOUR SENSES | no suit power for 5 s | 3 | STRIP + SPOT | 1 |
| 16 | `menu` | THE MENU | first F10 open outside a card | 6 | CALLOUT | - |
| 17 | `designer` | THE HUD DESIGNER | first F9 press | 1 | CARD | - |
| 18 | `whatsnew` | WHAT'S NEW | updaters, first world entry after updating | 5 | CARD | - |

82 steps (the tablet variant counted separately) plus the 7 "as I go" tips in Lesson 1.
Priority breaks ties in the queue (1 = first).

---

### Lesson 0 - Entry cards

**0.1 - Welcome to UI Ascended** `entry.welcome`
- When: first-run gate passes (A.5), single-player. Also `uiatutorial play entry`.
- Heading: "Welcome to UI Ascended"
- Body: "Your inventory now runs on wheels and one big window. Your bottom bar is just your two hands - there is no hotbar.
  The game is paused. Next you'll try each move for real, about three minutes - the game runs while you practice."
- Body, when the pause can't be taken (replay in MP): second paragraph's first sentence becomes "Multiplayer can't pause - start when you're safe."
- Buttons: **[Start - 3 minutes]** (primary) - **[Teach me as I go]** - **[No lessons, thanks]**. X and Esc = Later.
- Shows: CARD, pauses (SP). Scene `welcome` (exists: the wheel, the window, then the two hands light in turn).
- Done: any button. Start -> safety check -> 1.1. Teach me as I go -> "as I go" tips (Lesson 1 table). No lessons -> `TutorialTips=false`, strip C.19 "Lessons are off...".
- Skip: S3. Later re-offers at the next world entry, twice at most, then Teach me as I go.

**0.2 - Invitation (multiplayer)** `entry.invite`
- When: first-run gate passes but `GamePause.CanOwnPause()` is false (MP client, or host with players). Once per session. `GuideShown` untouched.
- Says: "Lessons are ready. Multiplayer can't pause, so start one when you're safe: [F10] > GUIDE."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP for 12 s, mini `keys:tap:{UIA_Menu}`.
- Done: 12 s, or F10 opened.
- Skip: S2.

**0.3 - Welcome back** `entry.resume`
- When: world entry while First Steps is Active (quit mid-lesson), single-player.
- Heading: "Welcome back"
- Body: "You were partway through the first lessons. Pick up where you left off? It only takes a couple of minutes."
- Buttons: **[Continue]** (primary) - **[Later]** - **[Stop lessons]**.
- Shows: CARD, pauses. Scene `welcome`.
- Done: button. Continue -> safety check -> the interrupted step.
- Skip: S3. Stop lessons = No lessons.

**0.4 - Hold on** `entry.hold`
- When: Start / Continue pressed while `TutorialSafety.IsCalm()` is false.
- Heading: "Hold on"
- Body: "Your suit or body is in trouble right now. Deal with that first - the lessons will wait for you."
- Buttons: **[Remind me in 5 minutes]** (primary) - **[Start anyway]**.
- Shows: CARD, pauses. No demo (static frame).
- Done: button. Remind -> re-offer 0.3 after 5 minutes of calm play.
- Skip: S3.

---

### Lesson 1 - FIRST STEPS (the core)

Game runs. Strip header: "FIRST STEPS - n/8". Priority over everything; no other lesson fires
until it ends.

**Teach me as I go** - the core as single tips (one strip each, S2, fired once):

| Core step | Fires when | Tip strip (verbatim) |
|---|---|---|
| 1.2 / 1.3 | 2 min of play, no wheel opened yet | "Tip: tap [MMB] - your toolbelt opens as a wheel." |
| 1.3 | first wheel opened | "Point at a wedge: the word at the top says what a click will do." |
| 1.5 | first item taken from a wheel | "Done with it? [G] puts it back where it belongs." |
| 1.6 | first OPEN (branch) in a wheel | "RIGHT-CLICK steps back out. Esc closes the wheel." |
| 1.7 | pointer rests 0.8 s on a wedge with an arrow | "That arrow means more inside: slide out past the edge and stop." |
| 1.8 | 5 min of play, [B] never used | "Tip: [B] shows every bag in one window, vanilla-style." |
| 1.8 | [B] window open 8 s, mouse never freed | "Double-tap [Alt] to free the mouse and click inside." |

**1.1 - Your two hands** `core.hands`
- When: Start pressed, calm; pause released.
- Says (active hand full, other empty): "These two boxes are your hands - the lit one is active. Press [E] to switch to your empty hand."
- Says (active hand already empty): "These two boxes are your hands. The lit one is active - and empty, ready to grab. [E] switches hands."
- Says (both full): "These two boxes are your hands, both full right now. [E] switches which one is active."
- Then: "Everything you grab lands in the lit hand. No hotbar - just two hands."
- Keys: [E] = `{V:SwapHands}` (gameplay; vanilla SwapHands, untouched by the mod).
- Shows: STRIP + mini `handswap` + SPOT on `HandBoxes`.
- Done: first variant - the active hand is empty; second - after its reading time; third - hands swapped once.
- Skip: S1 (first and third), S2 (second).

**1.2 - Open the belt wheel** `core.beltopen`
- When: 1.1 done.
- Says: "Tap [MMB]: your toolbelt opens as a wheel and stays open."
- Then: "You can still walk while a wheel is open."
- Keys: [MMB] = `{UIA_ToolbeltRadial}`
- Shows: STRIP + mini `keys:tap:{UIA_ToolbeltRadial}`.
- Done: `WheelOpened(Belt)`.
- Oops (held past 180 ms, wheel vanished on release): "Just a quick tap - holding is a faster trick for later."
- Skip: S1.

**1.3 - Read before you click** `core.read` `[NEW THIS WAVE]` (D-022, D-021)
- When: 1.2 done, belt wheel open (if it closed, strip shows C.19 "reopen" line first).
- Says: "Point at a few wedges. The word at the top of the wheel says what a click will do: TAKE or OPEN."
- Then: "The curved line under the wheel lists the keys you can use right now."
- Keys: none.
- Shows: STRIP + mini `actionword`.
- Done: pointer rests >= 0.4 s on two different wedges, or 10 s.
- Skip: S2 (12 s).
- `[VERIFY]` the exact words the wheel shows (copy assumes TAKE and OPEN) and that the hint line is curved under the wheel.

**1.4 - Take a tool** `core.take`
- When: 1.3 done, belt wheel open.
- Says: "Click a tool: it goes into your hand and the wheel closes."
- Then: "It's in your hand. Its belt slot keeps its name, so it can find its way home."
- Keys: none.
- Shows: STRIP + mini `takeandstow` (first half).
- Done: `WedgeCommitted(Equip)` on the belt wheel and the active hand now holds that tool.
- Branch (no belt, or no tools on it): Says "No tools on your belt? Click THE HUB at the top instead." Done: THE HUB opened. Step 1.5 is then skipped.
- Oops (THE HUB opened instead): "That OPENED The Hub. RIGHT-CLICK to step back, then click a tool."
- Skip: S1.

**1.5 - Put it back with G** `core.stow`
- When: 1.4 done and the active hand holds something.
- Says: "Press [G]: the tool flies back to its own belt slot. Watch the belt box flash."
- Then: "That's Smart Stow. [G] puts away whatever is in your hand - even with a wheel open."
- Then (it went somewhere else): "Smart Stow found it a spot. The box that flashes is where it went."
- Keys: [G] = `{V:SmartStow}`
- Shows: STRIP + mini `takeandstow` (second half). The real belt box flash (`Core/SlotFlash.cs`) is the confirmation.
- Done: `SmartStowed(ok)` - the item left the active hand.
- Skip: S1.
- `[PENDING D-016]` If Simple SmartStow ships as default, Says becomes: "Press [G]: the tool goes back to the slot you took it from. Watch the belt box flash."

**1.6 - Open and step back** `core.back`
- When: 1.5 done (or skipped).
- Says: "Tap [MMB] and click THE HUB at the top: it OPENS into everything you carry. RIGHT-CLICK steps back."
- Then: "Right-click again closes the wheel. So do Esc, [MMB], and the Close band in the middle."
- Keys: [MMB] = `{UIA_ToolbeltRadial}`
- Shows: STRIP + mini `hubback`.
- Done: THE HUB opened, then `BackedOut` to the belt level, or the wheel closed after the dive.
- Skip: S1.

**1.7 - Slide out for more** `core.pushout`
- When: 1.6 done.
- Says: "Tap [MMB]. Wedges with an arrow on the edge have more inside: slide out past that edge and stop for a moment."
- Then: "No need to be quick. Pull back to the middle to close it."
- Keys: [MMB] = `{UIA_ToolbeltRadial}`
- Shows: STRIP + mini `pushout` (slow cursor, it stops, dwell ring fills, child wheel blooms).
- Done: `ChildWheelOpened`.
- Branch (20 s with no arrowed wedge hovered): Says "No arrows on this wheel? Tools like the Drill have one - or look inside a bag with [Tab]." (`[Tab]` = `{UIA_BagRadial}`)
- Skip: S1 -> counted *shown*; its as-I-go twin still fires later.
- `[VERIFY]` doubled chevron size (D-022) matches the demo.

**1.8 - One window for everything** `core.window`
- When: 1.7 done.
- Says: "Close the wheel and tap [B]: every bag in one window, vanilla-style. Double-tap [Alt] to free the mouse."
- Then: "Click a folder tab to open a bag. [B] closes the window, one tap of [Alt] gets your aim back."
- Keys: [B] = `{UIA_Grid}`, [Alt] = `{V:MouseControl}`
- Shows: STRIP + mini `gridintro`.
- Done: window open (not a peek) and the mouse free (held or latched) for 1 s.
- Oops (Esc pressed while the window is open; vanilla menu appeared): after it closes, "[B] closes the window - Esc opens the game menu."
- Skip: S1. Then waits until the window is closed and the mouse captured (max 30 s, then the strip says "Close the window with [B] when you're done.") before 1.9.

**1.9 - That's the core** `core.finish`
- When: 1.8 done and window closed (or 30 s).
- Heading: "That's the core"
- Body: "Two hands, wheels, and one window - that's the whole idea.
  The rest comes to you: the first time you hold a tablet, carry a second belt or take off your suit, a short lesson appears right then. Replay any lesson from [F10] > GUIDE."
- Buttons: **[Start playing]** (primary) - **[See all lessons]** (opens F10 on the GUIDE tab).
- Keys: [F10] = `{UIA_Menu}`
- Shows: CARD, pauses. Scene `finish` (rows GUIDE / CONTROLS / [F9] pulse in turn).
- Done: button. Sets `TutorialCompleted=true`.
- Skip: S3.

---

### Lesson 2 - READING A WHEEL

- Fires: 8th wheel opened after the core, or the first click on a grey wedge (fail sound).
- Needs: core done or "as I go". Skill evidence: none (read-only lesson).

**2.1 - The middle tells you** `readwheel.middle`
- Says: "The middle of the wheel describes what you point at: its name, what a click does, and any warning."
- Shows: STRIP + mini `readout` (lines appear one by one).
- Done: S2. Skip: S2.

**2.2 - Grey means not now** `readwheel.grey`
- Says: "Grey wedges can't be used right now. Point at one and the middle tells you why, in red."
- Shows: STRIP + mini `readout` (grey wedge beat).
- Done: a grey wedge hovered, or S2. Skip: S2.

**2.3 - STOW wedges** `readwheel.stow`
- Says: "A STOW wedge is an empty slot. Point at it to preview your held item there; click to put it in."
- Shows: STRIP + mini `stowwedge`.
- Done: `WedgeCommitted(Stow)`, or S2. Skip: S2.

**2.4 - The hint ring** `readwheel.hints` `[NEW THIS WAVE]` (D-021)
- Says: "The curved line under the wheel lists the keys that work right now. Once you know them, it fades away."
- Then: "Want the hints back? [F10] > RADIAL > Reset hint counters."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP + mini `hintring`.
- Done: S2. Skip: S2.
- Fact: the fade is per hint, per save, after 25 wheel-opens (`UI/RadialHintBar.cs:47`); the reset button is `UI/Menu/Tabs/RadialTab.cs:58`.

---

### Lesson 3 - TOOLS AND BATTERIES (tablet variant: TABLETS AND CARTRIDGES)

- Fires (pick one variant): **Tablet** - a `Tablet` in the active hand 3 s (`Assets/Scripts/Objects/Items/Tablet.cs:14`). **Low** - a held `PowerTool` at <= 20% battery. **Tool** - any held item with a component socket (battery, canister, filter, cartridge: `ItemMenuBuilder.IsComponentSocket`) for 3 s.
- Needs: core done or "as I go". Skill evidence: a REPLACE or INSTALL commit before it fires -> Learned.
- Context lost: the item leaves the hand -> S4.

**3.1 - The tool's own wheel** `tools.open`
- Says (Tool): "Holding a tool? Tap [R]: it gets its own wheel - its settings and the parts inside it."
- Says (Low): "This tool is low on power. Tap [R] while holding it - you can swap the battery right here."
- Keys: [R] = `{UIA_ToolRadial}`
- Shows: STRIP + mini `toolreplace` (opening beat).
- Done: `WheelOpened(Tool)`. Skip: S1.

**3.2 - Click takes, slide out chooses** `tools.careful`
- Says: "Careful: clicking the battery TAKES it out. To swap it, slide out past the battery's edge instead."
- Says (the part is a gas canister - welder, jetpack): "Careful: clicking the canister TAKES it out. To swap it, slide out past the canister's edge instead."
- Shows: STRIP + mini `toolreplace` (TAKE warning beat).
- Done: `ChildWheelOpened` on a component wedge.
- Oops (the battery was taken out instead): "That took it out - it's in your other hand. Drag it from that hand box back onto the empty wedge."
- Skip: S1.

**3.3 - Replace** `tools.replace`
- Says: "Click REPLACE: every battery you carry shows up, with its charge."
- Says (canister): "Click REPLACE: every canister you carry shows up, with its pressure."
- Shows: STRIP + mini `toolreplace` (list beat).
- Done: REPLACE opened. Skip: S1.

**3.4 - Pick one** `tools.pick`
- Says: "Click the one you want. Swapped in one step - the old one goes where the new one was."
- Then: "Canisters, filters and cartridges swap the same way. Empty slot? Slide out to INSTALL one."
- Shows: STRIP + mini `toolreplace` (swap flash).
- Done: `WedgeCommitted(SwapIn)`. Skip: S1.
- Fact: one atomic `OnServer.SwapSlots` (`Core/ItemActions.cs` `SwapIntoSlot`). The list's first entry is EJECT (take the old one out).

**Tablet variant** (strip header "TABLETS AND CARTRIDGES - n/4"; addresses D-024):

**3.1T - Open the tablet** `tools.tablet.open`
- Says: "Holding the tablet? Tap [R]: its battery and cartridge slot show up as wedges."
- Keys: [R] = `{UIA_ToolRadial}`
- Shows: STRIP + mini `tabletinstall` (opening beat).
- Done: `WheelOpened(Tool)` with the tablet. Skip: S1.

**3.2T - Put a cartridge in** `tools.tablet.install`
- Says: "No cartridge in it? Slide out past the empty slot's edge: every cartridge you carry appears. Click one."
- Shows: STRIP + mini `tabletinstall`.
- Done: `WedgeCommitted(Install)` into the cartridge slot. If the slot is already full, skip straight to 3.3T.
- Skip: S1.
- Fact: a fresh game's tablet has a battery but no cartridge; the three cartridges ride loose in the jetpack.

**3.3T - Change cartridges** `tools.tablet.replace`
- Says: "To change it later, slide out on the cartridge and click REPLACE. A plain click takes it out."
- Shows: STRIP + mini `toolreplace` (labels CARTRIDGE).
- Done: `WedgeCommitted(SwapIn)`, or S2. Skip: S2.

**3.4T - Or click it, like vanilla** `tools.tablet.window`
- Says: "Rather click, like vanilla? Tap [B], free the mouse, and click the tablet: its slots open in a small window."
- Keys: [B] = `{UIA_Grid}`
- Shows: STRIP + mini `devicewindow`.
- Done: `DeviceWindowOpened`, or S2. Skip: S2.
- Fact: `Core/DeviceWindow.cs` - click toggles the item's own themed window; plain items still go to the hand.

---

### Lesson 4 - SPLITTING STACKS

- Fires: pointer rests 0.5 s on a stack wedge (a fresh game's Cable Coil x50 on the belt), or a stack clicked in the big window.
- Skill evidence: any split commit -> Learned.

**4.1 - Stacks split** `split.open`
- Says: "Stacks split. Slide out on a stack to get SPLIT ONE, SPLIT HALF and SPLIT COUNT."
- Shows: STRIP + mini `split` (child beat).
- Done: `ChildWheelOpened` on a stack. Skip: S1.

**4.2 - Pick how many** `split.count`
- Says (host / single-player): "For SPLIT COUNT, scroll to set the number, then click. The split part lands in your free hand."
- Says (MP client, count hidden - `ItemMenuBuilder.cs:657`, gate `ItemActions.CanSplitCount` = `GameManager.RunSimulation`, `Core/ItemActions.cs:973-978`): "SPLIT ONE and SPLIT HALF put the split part in your free hand."
- Shows: STRIP + mini `split` (count beat) / `keys:scroll`.
- Done: any split commit, or S2. Skip: S2.

**4.3 - In the big window** `split.window` `[NEW THIS WAVE]` (D-005, D-006)
- Says: "In the big window, click a stack with the mouse free: split ONE, HALF, or pick a NUMBER."
- Then: "Two popups on top of each other? Click the one you want to bring it to the front."
- Shows: STRIP + mini `splitpopup`.
- Done: `GridSplitDone`, or S2. Skip: S2.
- `[VERIFY]` button labels (they become compact squares - copy names what they do, not their art); whether NUMBER is hidden on MP clients like SPLIT COUNT.

---

### Lesson 5 - DRAG AND DROP

- Fires: first item dragged off a wedge; or the Drop key (`{V:Drop}`) pressed with a wheel open where it has no page to turn and no second belt - the player was probably trying to drop something (Dipole, 08-08).
- Skill evidence: an item dropped on a target, or dropped by closing -> Learned.

**5.1 - Set it down on screen** `dragdrop.park`
- Says: "Drag an item off its wedge and let go on open screen. It waits there - still in your bag."
- Shows: STRIP + mini `dragpark` (drag-out beat).
- Done: `ChipParked`. Skip: S1.

**5.2 - Drop it, or close to drop** `dragdrop.drop` `[NEW THIS WAVE]` (D-004)
- Says: "Drop it on another wedge or a hand box to move it there. Or close the wheel: it drops at your feet."
- Then: "Right-click, the wheel's key, the Close band - however you close it, dragged-out items drop."
- Shows: STRIP + mini `dragpark` (close-and-fall beat).
- Done: `ChipDroppedOnTarget` or `ChipsDroppedOnClose`. Skip: S1.
- `[VERIFY]` whether Esc also drops (README lists RMB, Tab, opener key). If Esc still cancels, add to Then: " Esc puts them back." Also confirm what jumping to another wheel (1 - 6, Tab) does to dragged-out items - today they are silently put back.

**5.3 - The colored box** `dragdrop.worldslot`
- Says: "Dragging over a machine or locker slot within reach shows the game's colored box. Green means it fits."
- Shows: STRIP + mini `worldslot`.
- Done: `WorldSlotCueShown`, or S2. Skip: S2.
- Fact: `Core/WorldSlotCue.cs:94-108` (green place/swap, yellow merge, blue insert, red refused); D-008 makes it wait until the drag has left the wheel.

**5.4 - Reach into the world** `dragdrop.reach`
- Says: "Hold [Alt] with a wheel open to reach into the world: grab an item off the ground or out of a machine."
- Then: "Let go over a wedge to put it there."
- Keys: [Alt] = `{V:MouseControl}`
- Shows: STRIP + mini `altreach`.
- Done: `WorldReachGrab`, or S2. Skip: S2.

---

### Lesson 6 - GEAR KEYS 1 - 6

- Fires: first tap of a gear key (the gear wheel is already open).
- Skill evidence: tap and hold both used -> Learned.

**6.1 - Your worn gear** `gear.tap`
- Says: "Numbers 1 - 6 are your worn gear: helmet, glasses, suit, back, uniform, belt. TAP one for its wheel."
- Shows: STRIP + mini `equiptaphold` (tap beat).
- Done: S2. Skip: S2.

**6.2 - Hold to take off or put on** `gear.hold`
- Says: "HOLD a number to take that item off into your hand. Holding gear that fits? Its number puts it on."
- Shows: STRIP + mini `equiptaphold` (hold beat).
- Done: `GearHeld`, or S2. Skip: S2.
- Fact: tap on an empty slot with fitting gear in hand dons it too (`RadialController.cs:284-292`).

**6.3 - Jump between wheels** `gear.jump`
- Says: "With any wheel open, a number jumps to that gear's wheel. The same number again closes it."
- Then: "Pointing at a bag? Then a number binds the bag instead - see the bags lesson."
- Shows: STRIP + mini `keys:tap` (digit).
- Done: `InWheelDigitJump`, or S2. Skip: S2.

**6.4 - Open a worn bag in the window** `gear.ingrid`
- Says: "Shift + a number opens that bag inside the big window; again folds it away. Clicking its HUD box (mouse free) works too."
- Then: "Gave a bag its own window? Then its number opens that window instead."
- Shows: STRIP + mini `ingrid`.
- Done: `InGridOpened`, or S2. Skip: S2.
- Fact: 0.9.7.4 behaviour, `StationeersUIMod.cs:856-891`.

---

### Lesson 7 - BELTS

- Fires: belt wheel open (by [MMB] or [6]) while another belt is reachable (`ToolbeltRadialFeature.BuildBeltPicker()` returns 2+ entries - computed once on the open edge). A fresh game's mining belt in the jetpack qualifies.
- Skill evidence: a belt swap -> Learned.

**7.1 - The belt chooser** `belts.q` `[NEW THIS WAVE]` (D-007)
- Says: "You carry another belt. With the belt wheel open, press [Q]: every belt you have appears."
- Then: "[Q] works the same when you open your belt with [6]."
- Keys: [Q] = `{UIA_Page}`, [6] = `{V:ToolBeltSlot}`
- Shows: STRIP + mini `beltswap` (Q beat).
- Done: `BeltPickerOpened`. Skip: S1.

**7.2 - Wear it, or go back** `belts.swap` `[NEW THIS WAVE]` (D-064)
- Says: "Click a belt to wear it - the old one takes its place. RIGHT-CLICK goes back without swapping."
- Shows: STRIP + mini `beltswap` (swap and back beats).
- Done: `BeltSwapped` or `BeltPickerBack`. Skip: S1.

**7.3 - Tools know their slots** `belts.home`
- Says: "Tools remember their slots. An empty slot shows its tool's name in grey, so everything finds its way home."
- Shows: STRIP + mini `ghostlabel`.
- Done: S2. Skip: S2.
- Fact: home slots and ghost labels on by default (`UIAConfig.cs:359, 624`); since 0.9.7.3 only a mouse drag re-homes a tool.

---

### Lesson 8 - BAGS AND SEARCH

- Fires: first [Tab] wheel, or first dive into THE HUB after the core.
- Skill evidence: a SEARCH take and a bag binding -> Learned.

**8.1 - One place for everything** `bags.root` `[NEW THIS WAVE]` (D-023)
- Says: "[Tab] opens the same place as THE HUB: SEARCH, RECENT ITEM and all the gear you wear."
- Then (bag key is still the game's scoreboard key): "Holding [Tab] still shows the scoreboard. Inside the belt wheel, Tab jumps to your back gear."
- Then (bag key rebound): "Inside the belt wheel, Tab jumps to your back gear and back."
- Keys: [Tab] = `{UIA_BagRadial}`. The in-wheel swap is a literal Tab (`KeyCode.Tab`, `RadialController.cs:553`), and the scoreboard hold only applies while the bag key IS vanilla's scoreboard key (`BagRadialFeature.cs:318`) - hence the two variants.
- Shows: STRIP + mini `hubroot`.
- Done: S2. Skip: S2.
- `[VERIFY]` the recent-item wedge's final label text (copy assumes RECENT ITEM).

**8.2 - Look inside** `bags.inside`
- Says: "Click any bag to look inside, then click an item to take it. Full bags group items by type."
- Shows: STRIP + mini `hubroot` (dive beat).
- Done: a bag level opened, or S2. Skip: S2.
- Fact: grouping above 10 items (`UIAConfig.BagRadialGroupThreshold`); a SORT wedge appears in sortable bags.

**8.3 - Search** `bags.search`
- Says: "Click SEARCH and just type - matches from every bag appear. Click one to take it."
- Then: "Esc or RIGHT-CLICK leaves the search."
- Shows: STRIP + mini `search`.
- Done: `SearchTook`, or 40 s. Skip: S2 (40 s).

**8.4 - Recent item** `bags.recent` `[NEW THIS WAVE]` (D-023)
- Says: "RECENT ITEM hands you another one of the last thing you took from a bag."
- Shows: STRIP + mini `hubroot` (recent beat).
- Done: recent item committed, or S2. Skip: S2.
- Fact: finds another item of the same type, shallowest first (`BagRadialFeature.cs:69-83, 127-134`).

**8.5 - Bag hotkeys** `bags.bind`
- Says: "Point at a bag and press a number (1 - 0). From now on, Ctrl + that number opens it from anywhere."
- Then: "Saved with this world. It works with a wheel open, too."
- Shows: STRIP + mini `bindbag`.
- Done: `BagBound`, or S2. Skip: S2.

---

### Lesson 9 - SMART STOW

- Fires: 5th successful [G].
- Skill evidence: none.

**9.1 - Where [G] sends things** `stow.order`
- Says: "[G] tries, in order: the item's own belt slot, a matching stack, an empty socket, then your bag rules."
- Keys: [G] = `{V:SmartStow}`
- Shows: STRIP + mini `stowroute`.
- Done: S2. Skip: S2.
- Fact: current chain `Core/StowRouter.cs:14-26` (belt home > stack > socket > profile > affinity > bag default > memory > general bag > belt fallback > vanilla); never into water, cereal or supply boxes.
- `[PENDING D-016]` Simple SmartStow version: "[G] puts an item back where it came from. New items try their belt slot, a matching stack, then an empty socket."

**9.2 - Where did it go?** `stow.where`
- Says: "Lost track of something? The box that flashes is where it went."
- Then: "[F10] > STORAGE > Settings can also show a note saying where and why (Advanced)."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP + mini `keys:tap:{V:SmartStow}`.
- Done: S2. Skip: S2.

**9.3 - Bags with a job** `stow.profiles`
- Says: "Want one bag for ores and another for cables? Give each a Bag Profile in [F10] > STORAGE > Bags."
- Shows: STRIP + mini `stowroute` (bag-rules beat).
- Done: S2. Skip: S2.
- `[PENDING D-016]` becomes "Advanced SmartStow" - retitle the path once the F10 layout is known.

---

### Lesson 10 - SETTINGS AND HOTKEYS

- Fires: first scroll on a value wedge, the 5th device-setting click, or the first suit wheel ([3]).
- Skill evidence: a wedge hotkey bound -> Learned.

**10.1 - Scroll a value** `settings.value`
- Says: "Wedges with a number between two arrows change with the SCROLL WHEEL. Hold [C] for small steps."
- Then: "A plain click nudges it up one step."
- Keys: [C] = `{UIA_FineAdjust}`
- Shows: STRIP + mini `valuescroll`.
- Done: `ValueScrolled`, or S2. Skip: S2.
- Fact: suit pressure / temperature +-10 per notch, +-1 with C (`Features/DeviceControls.cs:125-223`).

**10.2 - One-key settings** `settings.hotkey`
- Says: "Point at a setting and press a letter the game doesn't use: that letter now flips it from anywhere."
- Then: "Press the same letter on it again to remove it. It isn't saved - it lasts this session."
- Shows: STRIP + mini `hotkey`.
- Done: `HotkeyBound`, or S2. Skip: S2.
- Fact: settings only, never a tool - the anti-hotbar guard (`RadialMenu.cs:1252-1257`); session-only (`Core/WedgeHotkeys.cs`).

**10.3 - Keep it open** `settings.keepopen`
- Says: "Need several changes? Hold Shift as you click and the wheel stays open."
- Then: "Rather it always stayed open? Flip that in [F10] > RADIAL."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP + mini `shiftkeep`.
- Done: `KeepOpenUsed`, or S2. Skip: S2.
- Variant (player already inverted the setting, `ShiftKeepsRadialOpen=false`): Says "Your wheels stay open after each action. Hold Shift as you click to close it after that one."

---

### Lesson 11 - FASTER WHEELS

- Fires: 20th belt wheel opened by tapping (spaced on purpose).
- Skill evidence: 3 hold-mode commits -> Learned.

**11.1 - Hold, point, let go** `speed.hold`
- Says: "Faster: HOLD [MMB], move onto a tool and let go. It's in your hand - no click needed."
- Keys: [MMB] = `{UIA_ToolbeltRadial}`
- Shows: STRIP + mini `holdflick`.
- Done: a hold-mode commit. Skip: S1.

**11.2 - Holding goes deeper** `speed.dive`
- Says: "While holding, rest on THE HUB for a moment to go inside, then let go on what you want."
- Then: "Letting go in the middle cancels."
- Shows: STRIP + mini `holdflick` (dwell beat).
- Done: hold-mode branch dive then commit, or S2. Skip: S2.
- Fact: 0.25 s dwell enters a branch; releasing within 0.3 s of entering one cancels (`RadialMenu.cs:276-280`).

**11.3 - Move the wheel** `speed.move`
- Says: "Wheel in your way? Drag the middle of it to move it. It re-centers the next time."
- Then: "More speed options, like double-tap to repeat, live in [F10] > RADIAL."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP + mini `hubdrag`.
- Done: `HubMoved`, or S2. Skip: S2.

---

### Lesson 12 - THE BIG WINDOW

- Fires: the Universal Inventory open (not a peek) with the mouse free for 5 s, after the core.
- Skill evidence: a pin and a drag-move -> Learned.

**12.1 - Folder tabs** `window.tabs`
- Says: "Click a folder tab to open or close that bag. SORT tidies it up."
- Shows: STRIP + mini `gridintro` (tab beat).
- Done: `GridTabToggled`, or S2. Skip: S2.
- Fact: bags start folded on a new save (`Features/GridCollapseStore.cs:23-44`).

**12.2 - Click, right-click, drag** `window.moves` `[NEW THIS WAVE]` (D-002)
- Says: "Mouse free: click an item to take it, RIGHT-CLICK for its wheel, or drag it anywhere - a bag, a hand, a locker, the floor."
- Shows: STRIP + mini `gridmoves`.
- Done: `GridDragMoved`, `GridItemWheelOpened` or `GridCellTaken`, or S2. Skip: S2.
- `[VERIFY]` dragging into an open locker's slots works after the D-002 fix (today it drops on the ground).

**12.3 - Tools open up** `window.devices`
- Says: "Tools and tablets open a small window of their parts when clicked. Drag one to take it instead."
- Shows: STRIP + mini `devicewindow`.
- Done: `DeviceWindowOpened`, or S2. Skip: S2.

**12.4 - Move a whole type** `window.shiftdrag` `[NEW THIS WAVE]` (D-018)
- Says: "Hold Shift while you drag an item: every item of that type comes along."
- Shows: STRIP + mini `shiftdrag`.
- Done: `GridShiftDragMoved`, or S2. Skip: S2.
- `[VERIFY]` the scope ("that type in the same bag" vs "everything you carry") and add it to the copy once known.

**12.5 - A bag of its own** `window.pin`
- Says: "Drag a folder tab out of the window: that bag gets its own window. [B] leaves it up; its X folds it back."
- Keys: [B] = `{UIA_Grid}`
- Shows: STRIP + mini `pintear`.
- Done: `PinCreated`, or S2. Skip: S2.

**12.6 - No mouse needed** `window.keyboard`
- Says: "Mouse not free? SCROLL moves a highlight through your bags. [F] takes or places; [R] opens a tool."
- Keys: [F] = `{V:InventorySelect}`, [R] = `{UIA_ToolRadial}`
- Shows: STRIP + mini `scrollselect`.
- Done: `KeyboardTake` or `ScrollHighlightMoved`, or S2. Skip: S2.

---

### Lesson 13 - NAME YOUR BAGS

- Fires: two carried containers share a display name (checked on a bag-wheel open) and 3+ bag wheels opened.
- Skill evidence: a bag renamed (Storage tab rename or Labeller) -> Learned.

**13.1 - Why names matter here** `names.why`
- Says: "Wheels show what's inside, so things shift around. Bags with the same name are hard to tell apart."
- Shows: STRIP + mini `bagnames`.
- Done: S2. Skip: S2.

**13.2 - Names, colors, or places** `names.how`
- Says: "Give them names and colors: rename in [F10] > STORAGE > Bags or with a Labeller, and paint them."
- Then: "Prefer knowing things by place? The big window keeps every bag in the same spot."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP + mini `bagnames`.
- Done: S2. Skip: S2.
- Fact: wheels and tabs use each bag's live thumbnail, so paint shows (0.9.7.0 bag cards); renames use the Labeller's MP-safe path. `[VERIFY]` how a player paints a carried bag in vanilla, and word "paint them" to match.

---

### Lesson 14 - YOUR VISOR (suit powered)

- Fires: tier Suited (or Robot) for 10 s, 3+ minutes after the core ended (`HudSnapshot.Tier`, `HudSampler.cs:226-229`); waits 2 s after any boot animation.
- Spots use the shipped element types; missing elements just skip their outline.

**14.1 - The compass** `visor.compass`
- Says: "The compass shows which way you face. Turn until N sits under the marker."
- Says (robot): "You're a robot, so your visor always shows everything. Turn until N sits under the compass marker."
- Shows: STRIP + SPOT `Compass`.
- Done: heading within 7.5 deg of the compass's own N for 0.4 s (`HudSnapshot.HeadingDeg`, vanilla-parity `(yaw + 180) % 360`, `HudSampler.cs:323` - use the bearing `CompassWidget` draws N at), or 30 s. Skip: S1.

**14.2 - Outside air** `visor.outside`
- Says: "Next to the compass: the air pressure and temperature outside."
- Shows: STRIP + SPOT `Readout(ExternalPressure)`, `Readout(ExternalTemp)`.
- Done: S2. Skip: S2.

**14.3 - Inside your suit** `visor.inside`
- Says: "Your suit's own pressure and temperature sit with your needs: food, water and more."
- Shows: STRIP + SPOT `Readout(InternalPressure)`, `Readout(InternalTemp)`, `VitalsPanel`.
- Done: S2. Skip: S2.
- Fact: vitals rows appear only when they matter - food and water always, toilet above 25%, health when hurt, cognition when stunned (`VitalsPanelWidget.cs` summary).

**14.4 - Exact numbers** `visor.hover`
- Says: "Free the mouse ([Alt]) and hover your needs: exact numbers, and how fast they're changing."
- Keys: [Alt] = `{V:MouseControl}`
- Shows: STRIP + SPOT `VitalsPanel` + mini `keys:double:{V:MouseControl}`.
- Done: vitals tooltip shown, or 25 s. Skip: S1.

**14.5 - Status alerts** `visor.moodlets`
- Says: "The icons along the top are the game's own status alerts, moved into your visor."
- Shows: STRIP + SPOT `MoodletDashboard`.
- Done: S2. Skip: S2.

**14.6 - Low power** `visor.lowpower`
- Says: "When your suit battery runs low, the visor flickers. That's your cue: tap [3], slide out on the battery, REPLACE."
- Says (robot): "When your battery runs low, the visor flickers. That's your cue to find a charged battery."
- Then: "Don't want the flicker? [F10] > HUD."
- Keys: [3] = `{V:SuitSlot}`, [F10] = `{UIA_Menu}`
- Why the robot line differs: a suit's battery sits in a slot INSIDE the suit, so the suit wheel offers REPLACE (`ItemMenuBuilder.BuildComponentSatellite`); a robot's battery IS the worn item in its uniform slot, which has no REPLACE path. `[VERIFY]` the cleanest robot swap (drag a charged battery onto the [5] HUD box?) before teaching it.
- Shows: STRIP + mini `lowpower`.
- Done: S2. Skip: S2.
- `[NEW THIS WAVE]` D-020 moves Power & Glitch under Diegetics in the HUD tab and drops the default threshold to 10%; the path stays "[F10] > HUD".

---

### Lesson 15 - YOUR SENSES (no suit power)

- Fires: tier Bare for 5 s (suit off, or its battery dead) - never for robots.
- Addresses StormCircuit's "very empty" HUD.

**15.1 - What changed** `senses.why`
- Says: "No suit power, no sensors. Your visor now shows only what your body feels."
- Shows: STRIP + SPOT `BareSenses` (else `HandBoxes`).
- Done: S2. Skip: S2.

**15.2 - Words, not numbers** `senses.words`
- Says: "Words like COLD, THIRSTY or CHOKING appear when something is wrong and fade when it passes. No words means you're fine."
- Shows: STRIP + mini `baresenses`.
- Done: S2. Skip: S2.
- Fact: words exist only while a band is out of nominal; they flare on change, then settle dim; critical ones stay bright (`BareSensesWidget.cs` summary; words from `SenseCatalog.cs:101-193`).

**15.3 - Getting numbers back** `senses.numbers`
- Says: "Power a suit to get your numbers back. Want numbers all the time? [F10] > HUD > Diegetic tiers."
- Keys: [F10] = `{UIA_Menu}`
- Shows: STRIP + mini `keys:tap:{UIA_Menu}`.
- Done: S2. Skip: S2.
- `[VERIFY]` what the suited readouts show with Diegetic tiers off and no suit (the tier logic forces Suited, `HudSampler.cs:226`; values may read "--").

---

### Lesson 16 - THE MENU (F10 tour, Simple mode)

- Fires: first F10 open that isn't from a card. The tour first asks inside F10 (callout): "First time here? A 30-second tour." **[Take the tour]** **[No thanks]**.
- Game: tutorial pause reason held for the tour (SP), released at the end; F10's own pause button still works.
- Each callout: **[Back]** **[Next]** (last: **[Done]**) and a small **[Skip tour]**. The tour selects each tab for the player.
- `[DECISION]` build now, or after the D-009 F10 refactor (anchors and tab names will move).

**16.1 - Simple and Advanced** `menu.density` - anchor: Simple / Advanced buttons
- Says: "This is the UI Ascended menu. Simple shows what most players need; Advanced shows everything."

**16.2 - Two halves** `menu.halves` - anchor: master strip
- Says: "Two halves. Want it closer to vanilla? Switch off RADIAL MENUS or VISOR HUD - each works without the other."

**16.3 - Pick your look** `menu.themes` - anchor: HUD Themes tab (selected), scene `themeswitch` optional
- Says: "Click a card to restyle everything at once - HUD, wheels and windows. Shipped themes are read-only: duplicate one (Advanced) to change it."

**16.4 - The settings tabs** `menu.settings` - anchor: Radial, HUD and Storage tabs (Radial selected)
- Says: "RADIAL, HUD and STORAGE hold the settings for your wheels, your visor, and your bags and Smart Stow."

**16.5 - Your keys** `menu.controls` - anchor: Controls tab (selected)
- Says: "Rebind any UI Ascended key here. Lessons always show your current keys."

**16.6 - Your reference card** `menu.guide` - anchor: Guide tab (selected), then the pause button
- Says: "GUIDE is your reference card - replay any lesson here. The pause button up top holds single-player still while you browse."

- Done (all): Next / Done. Skip: Skip tour = Skipped (replayable from GUIDE).
- Facts: tabs HUD Themes / Radial / HUD / Storage / Controls / Guide (`UiaControlCenter.cs:312-320`); master switches RADIAL MENUS / VISOR HUD (`:449-451`); four read-only shipped themes (0.9.7.4).

---

### Lesson 17 - THE HUD DESIGNER (one step)

**17.1 - The HUD Designer** `designer.card`
- When: the first [F9] press while lessons are on. The card opens INSTEAD of F9; its first button opens F9.
- Heading: "The HUD Designer"
- Body: "Move, resize and restyle every piece of your HUD - even a separate look for suit and no suit. The four shipped themes are read-only, so duplicate one first. The Designer Handbook explains it all."
- Optional extra line `[NEW THIS WAVE]` `[DECISION]` (D-019, not in shipped themes): "New: a Rangefinder element shows the distance to what you aim at."
- Buttons: **[Open the Designer]** (primary) - **[Read the Handbook]** - **[Not now]**.
- Shows: CARD, pauses. Scene `finish` (F9 row pulses) or static.
- Done: button. Skip: S3; any choice marks it Done (never shown again).

---

### Lesson 18 - WHAT'S NEW (updaters only)

**18.1 - What's new** `whatsnew.card`
- When: first world entry after updating from 0.9.7.x (A.12). SP: card; MP: invitation strip "UI Ascended updated - see what's new in [F10] > GUIDE."
- Heading: "What's new"
- Body: "- Wheels now say TAKE or OPEN at the top before you click.
  - Close a wheel and anything you dragged out drops at your feet.
  - [Q] swaps belts from the [6] wheel too; RIGHT-CLICK backs out.
  - Big window: Shift + drag moves a whole type; stacks split to any number."
- Keys: [Q] = `{UIA_Page}`, [6] = `{V:ToolBeltSlot}`
- Buttons: **[Show me]** (primary, plays 18.2-18.5 as cards) - **[Got it]**.
- Shows: CARD, pauses. Scene cycles `actionword` / `dragpark` / `beltswap` / `shiftdrag`.
- `[VERIFY]` every bullet against the landed wave (D-022, D-004, D-007/D-064, D-018/D-005).

**18.2 - Read before you click** `whatsnew.word` - CARD, scene `actionword`
- Says: "Point at a wedge and read the word at the top of the wheel: TAKE puts it in your hand, OPEN looks inside."

**18.3 - Close to drop** `whatsnew.drop` - CARD, scene `dragpark`
- Says: "Drag an item out of a wheel, then close the wheel: the item drops at your feet."

**18.4 - Belts** `whatsnew.belts` - CARD, scene `beltswap`
- Says: "[Q] opens the belt chooser from the [6] wheel as well as [MMB]. RIGHT-CLICK takes you back to the belt you came from."

**18.5 - The big window** `whatsnew.window` - CARD, scene `shiftdrag`
- Says: "Hold Shift while you drag to move every item of that type. Click a stack to split one, half, or a number you pick."

- Done: Next / Got it. Skip: S3.

---

### C.19 Supporting on-screen strings

**Strip chrome**
- Header: "<LESSON TITLE> - <n>/<total>" (e.g. "FIRST STEPS - 3/8").
- Self-skip (S1): "No rush - this step skips itself in 15 s."
- Wheel closed during a wheel step: "The wheel closed - tap [MMB] to open it again." (the lesson's own opener token)
- On hold: "Lesson on hold - deal with the emergency first."
- Waiting chip (no progress 90 s, strip shrinks to the top edge): "Lesson waiting - [F10] > GUIDE"
- Lessons turned off: "Lessons are off. Turn them back on in [F10] > GUIDE."
- 1.8 overrun: "Close the window with [B] when you're done."

**GUIDE tab - new "Lessons" section** (top of the tab, above today's reference card)
- Header: "Lessons"
- Note: "Short hands-on lessons. Each one also shows up by itself the first time you need it."
- Row: lesson title + state chip "NEW" / "IN PROGRESS" / "DONE" / "SKIPPED" + button **[Watch]** (+ **[Try it]** when its context exists).
- Toggle: "Show lessons when something new comes up" (`TutorialTips`)
- Buttons: **[Continue lesson]** - **[Skip this lesson]** - **[Stop all lessons]** - **[Restart from the beginning]**
- Watch-mode last card extra button: **[Try it now]**.
- F10 tour opener callout: "First time here? A 30-second tour." **[Take the tour]** **[No thanks]**

**Console** (`uiatutorial`)
- `uiatutorial` -> "uiatutorial: replaying First Steps (close the console to see it). Also: list, play <lesson>, restart, tips on|off, edit, reset, lint."
- `uiatutorial list` -> one line per lesson: "  core        FIRST STEPS          DONE" ... then "Play one with: uiatutorial play <id>"
- `uiatutorial play <id>` -> "uiatutorial: playing <TITLE>." / "uiatutorial: no lesson called '<id>' - try uiatutorial list."
- `uiatutorial restart` -> "uiatutorial: lesson progress cleared. The welcome card shows on your next world entry."
- `uiatutorial tips on|off` -> "uiatutorial: lessons are now ON." / "... OFF."
- `uiatutorial lint` -> "uiatutorial lint: <n> strings checked, <k> problem(s)." then one line per problem (non-ASCII char, unknown token, over budget).
- `edit`, `reset`: unchanged from today (`FinderCommands.cs:222-238`).

---

## Part D - Engineering

### D.1 Shape

```
StationeersUIMod.Update
  |-- TutorialDirector.Tick()   first-run gate, lesson queue, triggers (4 Hz polls + signals),
  |       |                     safety gate, rate limits, progress writes
  |       |-- TutorialCoach      CARDS + CALLOUTS (modal, pause via GamePause) - extended
  |       |-- TutorialStrip      STRIP (non-modal fade-in line + mini demo stage) - new
  |       '-- TutorialSpotlight  SPOT outlines on real HUD / F10 rects - new
  |-- (game code) ---- TutorialSignals.Raise(...) one-liners after real actions - new
  '-- TutorialProgressStore      Tutorial/Progress.xml, per install - new
```

The tutorial never calls `ItemActions`, never synthesises input, never moves an item.

### D.2 File-level changes

| # | File | Change | Size |
|---|---|---|---|
| 1 | `UI/Menu/Tutorial/TutorialChapters.cs` (new; `TutorialSteps.cs` retired or kept as shim) | Pure data: lessons (id, title, trigger id, needs, priority, skill evidence) and steps (id, title, Says + variants, Then, token list, presentation, demo id + params, spot targets, done-signal id, timeouts, oops lines). All copy from Part C. | ~900 |
| 2 | `UI/Menu/Tutorial/TutorialDirector.cs` (new) | Owns the first-run block (moved from `StationeersUIMod.cs:400-420` unchanged in logic), Welcome choices, resume, MP invite, safety gate, trigger evaluation, queue, rate limits, skill evidence, S1/S2/S4 timers, "as I go" tips. Static; every field reset in `Shutdown`. | ~650 |
| 3 | `UI/Menu/Tutorial/TutorialSignals.cs` (new) | Allocation-free bus: `enum TSignal`, `Raise(TSignal, int arg = 0)`, per-signal frame stamp + count, `Seen(TSignal, sinceFrame)`. Cleared in `Shutdown`. | ~120 |
| 4 | `UI/Menu/Tutorial/TutorialProgressStore.cs` (new) | `Tutorial/Progress.xml` (XmlSerializer, public DTOs like `TutorialTextStore`); batched writes on lesson end and world unload; tolerant load; updater seeding (A.12). | ~200 |
| 5 | `UI/Menu/Tutorial/TutorialSafety.cs` (new) | `IsCalm()` from `HudSystem.LastSnapshot` (`HudSystem.cs:76`): valid, `SuitStatusLevel < 3`, and no `SenseSev.Critical` band of `SenseCatalog.Senses` tripped when evaluated with `SenseCatalog.Driver(snap, ...)` at catalog thresholds (the widget's per-element overrides don't apply), honouring the same FeltValid / SanitationValid MP guards the widget uses. | ~80 |
| 6 | `UI/Menu/Tutorial/TutorialStrip.cs` (new) | A.8 spec: own canvas 5150, no raycaster, glass panel, header + body TMP, 150x76 stage host, success tick, drain line, dodge rules (wheel open -> rise over the moodlet row via `RadialController.AnyRadialOpen`; big window / pins via `TheGridPanel.HitTestWindow` and the pinned windows' rects), theme poll like the coach. | ~450 |
| 7 | `UI/Menu/Tutorial/TutorialSpotlight.cs` (new) | Outline(s) around `HudElementView`s by type (+ Readout `src`), forward-warped; optional scrim-with-hole for cards; F10 anchor rects for callouts. | ~250 |
| 8 | `UI/Menu/Tutorial/TutorialCoach.cs` | `OpenCard(stepId, buttons)`, open at any step, watch-mode chapters, per-card button rows (replaces `BuildFinishExtras`), callout mode (anchored 380x200, sort 5300, no scrim), scrim-with-hole option, running-game sentence per card. Pause / modal / unwind code untouched. | +250 |
| 9 | `UI/Menu/Tutorial/TutorialDemoStage.cs` | `Show(id, Func<string,string> glyph)`; all captions via tokens; `keys:*` micro-scene; mini layouts; new and modified scenes (A.9); `RadialArcText.Curve` for curved words (at build only). | +2000 |
| 10 | `UI/Menu/Tutorial/TutorialTextStore.cs` | Add `<Confirm>` (Then) and lesson-title overrides; ids from `TutorialChapters`. Old 15 ids become harmless orphans. | +40 |
| 11 | `StationeersUIMod.cs` | First-run block -> `TutorialDirector.Tick()`; tick strip/spotlight after the Control Center pump (`:391-393`); `Shutdown` calls next to `TutorialCoach.Shutdown` in the `finally` (`:1129`). | +15 / -25 |
| 12 | `UIAConfig.cs` | `TutorialTips` bool, default true, "1. General". No migration step (A.12). | +6 |
| 13 | `Features/RadialController.cs` | Signals: `WheelOpened(kind, sticky)` in `OpenRadial`/`SwitchToFeature`/`OpenBagRadial`/`OpenBeltPicker`; `WheelClosed(route)` in `CloseAll(reason)`; `InWheelDigitJump`, `BagBound`, `BoundBagOpened` in `UpdateRadialShortcuts` / `TryOpenBoundBagFromGameplay`. Expose the active feature kind (read-only). | +20 |
| 14 | `Overlay/RadialMenu.cs` | Signals: hover change (kind, has-arrow, disabled - reuse the D-022 TAKE/OPEN classifier so lesson and wheel can't disagree), commit (kind), child wheel opened, branch dive / `BackedOut`, chip drag / park / drop-on-target / drop-on-close (inside the D-004 shared close-with-item handler) / cancel, world-reach grab, hub drag, keep-open used, value scrolled, grey click. | +30 |
| 15 | `UI/SearchPanelView.cs` | `SearchOpened`, `SearchTook`. | +3 |
| 16 | `Features/SmartStowPlus.cs` | `SmartStowed(ok)` where the stow flash fires. | +3 |
| 17 | `Features/EquipmentKeyRadialFeature.cs`, `Features/ToolbeltRadialFeature.cs` | `GearHeld` in `OnHold`; `BeltSwapped` in the picker's select; `BeltPickerBack` in the D-064 RMB path. | +6 |
| 18 | `Features/BagHotkeyStore.cs`, `Core/WedgeHotkeys.cs` | `BagBound`, `HotkeyBound`. | +4 |
| 19 | `UI/Grid/TheGridPanel.cs`, `GridTab.cs`, `BagGridCell.cs`, `GridSelection.cs`, `Core/DeviceWindow.cs` | `GridOpened(peek/latched)`, `GridClosed`, `GridTabToggled`, `PinCreated/Closed`, `InGridOpened`, `GridCellTaken`, `GridItemWheelOpened`, `GridDragMoved`, `GridShiftDragMoved` (D-018), `GridStackPopupOpened` / `GridSplitDone` (D-005), `ScrollHighlightMoved`, `KeyboardTake`, `DeviceWindowOpened`. | +25 |
| 20 | `Core/WorldSlotCue.cs` | `WorldSlotCueShown`. | +2 |
| 21 | `UI/Hud/HudSystem.cs` | `TryGetElementScreenRect(HudElementType, string srcParam, out Rect)` over `CollectElementViews` + `HudWarp.WarpPoint`. Tier is polled, not hooked (the flicker-transition block at `:1127-1150` stays untouched). | +50 |
| 22 | `UI/Hud/Widgets/VitalsPanelWidget.cs` | `VitalsTooltipShown` when its tooltip opens. | +2 |
| 23 | `UI/Menu/UiaControlCenter.cs` | `MenuOpened`, `MenuTabSelected`; `internal SelectTab(string title)`; `TryGetAnchorRect(string id)` for density buttons, master strip, each tab, pause. | +60 |
| 24 | `UI/Menu/Tabs/GuideTab.cs` | Lessons section (C.19); fix the stale lines (A.3 item 4). | +120 |
| 25 | `Core/FinderCommands.cs` | `uiatutorial list / play / restart / tips / lint`. | +120 |
| 26 | `StationeersUIMod.ToggleHudEditor` path | First-press gate for lesson 17 (open the card instead of F9 once). | +10 |
| 27 | `Documentation/Interaction-Model-Reference.md` | Update for the wave (maintenance rule in its header) - the script cites it. | docs |

Signals are raised AFTER the real action returns, from the UI call sites - never inside
`ItemActions` (keep the mutation funnel pure).

### D.3 Signal catalogue

`HandSwapped` (poll active-hand slot) - `WheelOpened(kind, sticky)` - `WheelClosed(route)` -
`WedgeHovered(kind, arrow, grey)` - `WedgeCommitted(kind)` - `ChildWheelOpened(kind)` -
`BackedOut` - `ChipParked` - `ChipDroppedOnTarget` - `ChipsDroppedOnClose` - `ChipsCancelled` -
`WorldReachGrab` - `WorldSlotCueShown` - `HubMoved` - `KeepOpenUsed` - `ValueScrolled` -
`GreyClicked` - `HotkeyBound` - `BagBound` - `BoundBagOpened` - `InWheelDigitJump` - `GearHeld` -
`BeltPickerOpened` - `BeltSwapped` - `BeltPickerBack` - `SearchOpened` - `SearchTook` -
`SmartStowed(ok)` - `GridOpened(latched)` - `GridClosed` - `GridTabToggled` - `GridCellTaken` -
`GridItemWheelOpened` - `GridDragMoved(target)` - `GridShiftDragMoved` - `GridStackPopupOpened` -
`GridSplitDone` - `DeviceWindowOpened` - `PinCreated` - `PinClosed` - `InGridOpened(via)` -
`ScrollHighlightMoved` - `KeyboardTake` - `VitalsTooltipShown` - `MenuOpened` -
`MenuTabSelected(title)` - `DesignerOpening`. Polled states: cursor free / latched
(`CursorLatch.Active`, `Cursor.visible`), tier, heading, held item class and battery %.

Performance: polls at 4 Hz; inventory scans (belt picker count, same-name bags) only on the
open edge of a wheel; strip tick zero-allocation.

### D.4 Phases

| Phase | What | Days |
|---|---|---|
| 0 (only if 0.9.8.0 ships first) | Patch today's coach copy: step `pin` (in-grid behaviour), step `scrollpark` (D-004), drop the stale `pinflow` beat. | 0.5 |
| 1 | Data model, director (first-run, Welcome choices, resume, invite, safety, rate limits, persistence), strip, signals + raise sites, config key, `uiatutorial` subcommands, GUIDE Lessons section. | 3-4 |
| 2 | First Steps end to end: cards 0.1-0.4 and 1.9, strips 1.1-1.8, `keys` micro-scene, `handswap` `actionword` `takeandstow` `hubback` `pushout` `gridintro`, spotlight on hand boxes. Build AFTER the radial workstream (D-021/022/023/004) lands, so the mocks match the real wheel. | 2-3 |
| 3 | Lessons 2-15 in priority order - 3 (tablet first), 5, 4, 7, 8, 12, 6, 2, 9, 15, 14, 10, 11, 13 - each shipped with `keys` demos, then bespoke scenes. | 5-7 |
| 4 | F10 tour callouts + anchors, F9 card, What's new. | 2 |
| 5 | Oops lines, skill evidence tuning, `lint`, adversarial review vs decompile, Changes Report. | 2-3 |

Total about 15-20 dev-days. Each phase is usable on its own: Phase 2 alone already replaces the
15-card wall.

### D.5 Test checklist

1. Fresh install (`uiareset confirm` + restart), **new game**: Welcome appears ~1 s after control in the landing capsule, game paused; Start unpauses; 1.1-1.8 complete by doing; Finish pauses; `TutorialCompleted=true`.
2. Fresh install, **existing save** in a dangerous spot (low O2): Start -> "Hold on" card; practise later resumes via 0.3.
3. **MP client** and **host with a client**: no modal; invite strip once; GUIDE > Watch shows the running-game sentence and a greyed pause button.
4. **Updater** (`GuideShown=true`, no Progress.xml): no Welcome; What's new card once; lessons fire; a player who already swaps batteries never sees lesson 3.
5. **Rebinds:** MMB -> a keyboard key, Alt -> another key, gear keys moved in the game's Controls: every strip, card and demo keycap shows the new keys.
6. **Teach me as I go:** no core; each tip fires once on its trigger.
7. **No lessons:** nothing fires; GUIDE toggle turns lessons back on.
8. Quit mid-core -> re-enter -> 0.3 resumes at the same step.
9. **Safety:** drain suit O2 during a strip -> "Lesson on hold"; recover -> resumes.
10. **Overlap:** strip vs the big window (dodges), vs an open wheel (no overlap with the top word or hint ring at 1080p and 1440p, 16:9 and 21:9), vs F10 (hidden behind), vs vanilla menus (hidden, lesson paused).
11. **Theme switch** mid-strip and mid-card: both restyle.
12. **Hot reload:** double F6 during a card (paused), a strip, the F10 tour: no stale canvases, pause released, no stuck Typing state, signals cleared.
13. **Esc** from every card: vanilla menu never pops on key-up.
14. `uiatutorial list/play/restart/tips/lint/edit/reset` outputs as C.19; `edit` covers Then lines.
15. **MP safety audit:** grep `UI/Menu/Tutorial/` for `ItemActions`, `OnServer`, `MoveToSlot` - zero hits.
16. **Performance:** profiler shows no per-frame GC from the strip/director over 5 minutes of play with lessons active.
17. **ASCII:** `uiatutorial lint` = 0 problems on shipped copy.
18. Every `[VERIFY]` in Part C resolved against the landed wave.

### D.6 Risks

- **Moving target:** the wheel's words, hint ring and drop rule are landing in the same wave; the
  demos must be built after them (Phase 2 ordering) and every `[VERIFY]` checked.
- **F10 refactor (D-009) and Simple SmartStow (D-016)** will change lesson 16 anchors and the G
  copy; both are isolated in data (`TutorialChapters.cs`) and callout anchor ids.
- **Custom profiles:** a player's own HUD may put an element where the strip sits, or remove an
  element a SPOT wants - copy never depends on the spot; see Part E Q7 for placement.
- **MP clients:** a gesture can succeed locally and be refused by the server; lessons confirm the
  gesture, and the pending-dim feedback the mod already shows covers the rest.
- **Nagging:** rate limits, skill evidence, 3-offer cap and the `TutorialTips` switch.

---

## Part E - Open questions for FlorpyDorp

1. **Pause model.** OK that practice runs on the live world (cards pause, strips don't), with the
   safety gate? The alternative - a "practice freeze" where wheels work on a paused world - means
   new guard exceptions and a timeScale-0 audit of every mutation (A.5).
2. **Updaters.** Should players coming from 0.9.7.x get the just-in-time lessons by default
   (Dipole would have), or only the What's new card?
3. **No new key.** Lessons finish by being done, skip themselves, and are controlled from
   F10 > GUIDE. Fine, or do you want a key to open / skip the current lesson (e.g. F10 opens the
   lesson card while a strip is showing)?
4. **Welcome choices.** Start / Teach me as I go / No lessons - or also a "lighter start" that
   switches a half off for Ningy-type players?
5. **Words.** On screen we say "slide out past the edge and stop", not "swipe" - Dipole reads
   swipe as fast and flicky. Keep?
6. **Sequencing.** Build the F10 tour now against today's tabs, or after the F10 refactor? Ship
   the G copy for today's SmartStow, or wait for Simple SmartStow?
7. **Strip placement.** Top-centre under the moodlet row fits all four shipped themes - OK as the
   one fixed spot, or should the director avoid any HUD element there (more code)?
8. **Rangefinder.** Mention it in the F9 card (it isn't in the shipped themes)?
