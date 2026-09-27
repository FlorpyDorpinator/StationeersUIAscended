# 0.9.8.0 — roadmap and tracking

Started 2026-09-25 from FlorpyDorp's item-by-item disposition of the full Discord triage
(`Discord/triage/2026-09-25 1453.md`, running list `Discord/triage/issues.md`; Discord/ is
git-ignored — this folder is the in-repo record). 0.9.8.0 is FlorpyDorp's number; no version
bump happens until he cuts the release.

## Decisions made (FlorpyDorp, 2026-09-25)

- **Drop rule (D-004):** drag-out + close = ALWAYS drop, identical across every menu and every
  close route (RMB, Tab, opener key). One shared close-with-item handler.
- **F10 menus (D-009):** targeted rename-textbox patch now; the full F10/settings redesign is a
  0.9.8.0 plan (`F10-Settings-Refactor-Plan.md`) and waits for his ChatGPT UI concept images.
- **Universal Inventory buttons (D-005 scope):** ALL full-width control buttons become compact
  icon-sized squares (including the Split One / Split Half / new Choose Number row).
- **SmartStow (D-016):** "Simple SmartStow" — per-save, item-instance return-to-where-it-came-from
  memory — becomes the mod default; today's Stow Profiles system becomes "Advanced SmartStow".
  Plan first (`SmartStow-Simple-Refactor-Plan.md`); Advanced UI redesign deferred to his images.
- Closed without work: D-001, D-003 (fixed per his play-test), D-010, D-012 (disregard), D-011,
  D-015 (fine as-is), D-013, D-014 (already shipped), D-024 (tutorial covers it), D-026 (done).

## Fix wave — 2026-09-25 (BUILT + REVIEWED, awaiting FlorpyDorp's play-test)

Status: all 5 workstreams + 6 integration hooks landed; adversarial review (2 reviewers vs the
27758 decompile) found 28 issues incl. 1 HIGH (the unified close-drop could eject items from
world containers — fixed at chip level + a DropToWorld carried-by-local-player gate); all fixed;
final build 0 warnings / 0 errors, auto-deployed to BepInEx/scripts (F6). Play-test checklist:
the 2026-09-25 "Triage fix wave 1" Changes Report.

| Workstream | Items | Owner files |
|---|---|---|
| Grid/drag | D-002 UI->world drag, D-005 phantom grid + Choose Number + compact buttons, D-006 popup focus, D-018 Shift+drag-all core + grid gesture | UI/Grid/**, Core/ItemActions.cs |
| Radial | D-004 unified drop-on-close, D-008 highlight mask, D-017 bold item names, D-021 curved reworded hint ring, D-022 chevron x2 + curved Take/Open word, D-023 recent-item labels | Overlay/**, UI/UnityRadialView.cs, UI/RadialHintBar.cs |
| Belt | D-007 Q from 6-key + The Hub there, D-064 RMB returns from Q-chooser to origin belt | Features/*Radial*, BeltBindingStore |
| HUD element | D-019 Rangefinder element (m/ft per-element; NOT in shipped themes — his placement call) | UI/Hud/** |
| F10 patches | D-020 flicker 27->10 + Power & Glitch under Diegetics + simple-mode visibility + scroll fix; D-009 textbox patch | UI/Menu/**, UIAConfig, ConfigMigration |

After the wave: integration pass (cross-agent hooks), adversarial review vs the decompile,
clean build, Changes Report, then FlorpyDorp play-tests against the play-test steps in the
Changes Report. **Deferred small follow-up:** wire the radial-side Shift+drag gesture to the
new bulk action (kept out of the wave to avoid file conflicts).

## 2026-09-25 evening — THE OVERHAUL IS BUILT (uncommitted, awaiting play-test)

FlorpyDorp answered the decision sheet, chose mockup 3, and asked for the full ultracode
implementation. It shipped the same evening: Simple SmartStow (default, per-item homes) +
Complex (today's system) behind a mode bar; the SmartStow tab = mockup 3's Organizer +
Routing + Universal Inventory + Return Home; Kit v2 + the 1450x950 folder-tab shell;
"UI Themes" rename; radial-knob port; the per-save-store MP key fix; all Part 3 extras.
Three adversarial reviewers (vs a fresh LIVE-build 27798 decompile) found no catastrophic
defect; every confirmed finding was fixed. Build 0 warnings / 0 errors, auto-deployed to
scripts (F6). **Master record: `Changes Reports/2026-09-25 - 0.9.8.0 SmartStow + F10
overhaul (orchestrated wave).md`. Play-tests: `Play-Test-SIMPLE.md` (<5 min) and
`Play-Test-FULL.md`.** Open: MasterEnable's F10 home (his call), commit/push + version cut.

## 2026-09-26 — THE TUTORIAL IS BUILT (uncommitted; tour mode in progress)

Built from `Tutorial-Plan-and-Script.md` by an orchestrated 7-agent wave against
`Tutorial-Build-Contract.md` (read it before touching `UI/Menu/Tutorial/`), reviewed by 2
adversarial reviewers, fixes in flight. Master record: `Changes Reports/2026-09-26 - In-game
tutorial built (19 lessons) + lesson editor.md`; copy deltas for his review:
`Tutorial-Copy-Changes.md`. In-game editor: `uiadev`, then F8 on a live lesson (or `uiatutorial
edit`); Export + `tools/bake-tutorial.ps1` ships edits.

**Decisions (FlorpyDorp, 2026-09-26):**
- Part E answered "as designed" (the plan's recommendations).
- Editor: an ImGui dev window plus editing while a lesson plays, unlocked by `uiadev`.
- **TOUR MODE (replaces the plan's first-run pacing):**
  - "Show all the lessons in order one after another when a player first opens the mod."
  - Welcome > Start runs an OVERVIEW first ("explain the Universal Inventory, radials etc. right
    off the bat": hands / wheels / Universal Inventory / Smart Stow / F10). Then lessons 1-17
    back to back, in C.0 order.
  - **Always guide the player into each lesson's situation** (setup steps, safety-phrased). An
    unmet setup self-skips that lesson.
  - **After the tour:** only SKIPPED lessons come back just in time, once each.
  - **Updaters get the full tour too.** Lesson 18 What's-new is not auto-shown when the tour runs.

## Plans in this folder

- `Tutorial-Plan-and-Script.md` — full in-game tutorial redesign + complete step-by-step script
  (auto-pause on first run, fade-in text, animated demos, feature-complete). FlorpyDorp
  line-edits the copy, then implementation is scheduled.
- `SmartStow-Simple-Refactor-Plan.md` — Simple SmartStow data model, capture/resolution rules,
  per-save persistence, MP safety, mode switch, migration options. He approves before code.
- `F10-Settings-Refactor-Plan.md` — menu redesign plan; absorbs his ChatGPT UI images when ready.
- `Advanced-SmartStow-UI-Mockup-Review.md` — review of his SmartStow Organizer concept images,
  build notes (every control mapped to existing store calls) and build-time fixes (Part D).
- `SmartStow-Build-Decisions.md` — the three plans boiled down to 33 short yes/no questions with
  recommended answers (answer by listing the ones you disagree with); answers get logged there.
- `concepts/` — his ChatGPT concept images. `smartstow-organizer-3.png` is the **chosen** SmartStow
  Organizer design (2026-09-25); `-1`/`-2` are superseded drafts. Logged in F10 plan §9 and
  Simple plan §10.3.

## Waiting on FlorpyDorp

- Play-test the fix wave once built (checklist will be in the wave's Changes Report).
- Rangefinder: try the element in F9, decide if/where it joins shipped themes (units: m/ft).
- Read/edit the tutorial script; approve the SmartStow plan's open questions (capture events,
  fallback chain, existing-user migration choice); supply the ChatGPT UI images for Advanced
  SmartStow + F10 redesign. *(2026-09-25: the SmartStow Organizer image is in and chosen —
  `concepts/smartstow-organizer-3.png`; it sets the tab name "SmartStow" and the modes
  "Simple / Complex". Still to come: the Simple-mode page and the other F10 screens. It implies
  top tabs and a bigger window — confirm F10 plan Q1/Q2.)*
- Version/tag call for 0.9.8.0 when he decides the wave ships.
