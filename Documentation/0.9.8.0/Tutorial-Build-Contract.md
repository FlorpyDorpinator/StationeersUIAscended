# Tutorial build contract (0.9.8.0)

**For:** the build agents implementing `Tutorial-Plan-and-Script.md`, and whoever maintains the
tutorial afterwards. This file pins every interface BETWEEN agents. Anything not pinned here is
the owning agent's call: read the plan, read the code, decide, and report it.

**Script of record:** `Tutorial-Plan-and-Script.md` Part C, copied verbatim (ASCII). Design: Parts
A and D. Where this contract and the plan disagree, this contract wins (it was written later,
against the code as it stands on 2026-09-26).

---

## 0. Decisions already made

**Part E, answered by FlorpyDorp 2026-09-26 ("build it as designed" = the plan's recommendations):**
Q1 cards pause, practice runs live, safety gate on. Q2 updaters get the What's-new card AND
just-in-time lessons (skill evidence suppresses lessons they already know). Q3 no new key; lessons
finish by being done / S1 / S2, controlled from F10's Guide tab. Q4 three Welcome choices. Q5
keep "slide out past the edge and stop". Q6 the F10 tour is written against the NEW F10 (see
below). Q7 fixed top-centre strip. Q8 yes, mention the Rangefinder in the F9 card.

**Also decided:** in-game lesson editor = an ImGui dev window (like F9) plus edit-while-playing,
unlocked by `uiadev` (`Core/UiaDevMode.Active`). Edits save locally at once; "bake" folds them
into the shipped source (section 7). Build everything now, including the small hooks in shared
files, even though another session's uncommitted work is in the tree (section 1).

**The code moved since the plan was written. Resolve every `[VERIFY]` / `[PENDING D-016]` flag
against the code as it is now:**
- **F10 was rebuilt** (uncommitted SmartStow/F10 overhaul): a ~1450x950 folder-tab shell, Kit v2,
  settings search, the Simple/Advanced density toggle REMOVED (replaced by per-section "More
  options"), "HUD Themes" renamed to **"UI Themes"** for players, a SmartStow tab with
  **Simple / Complex** modes, and a Suggestions/Bugs tab. Lesson 16 (the F10 tour) must be
  REWRITTEN for this F10, same intent: 5-6 callouts on the real tabs. Any copy mentioning the
  density buttons, "Simple mode" as an F10 toggle, or "HUD Themes" must change. Report every
  line you rewrote, so FlorpyDorp can review it.
- **Simple SmartStow is the default:** use the `[PENDING D-016]` variant copy (lesson 1.5,
  lesson 9).
- **Fix wave 1 landed** (commit 51469e0): the curved TAKE/OPEN/SWAP/STOW action word, the curved
  hint ring, the "recent item" wedge tag and hub line, the doubled chevron, D-004 (every close
  route drops dragged-out/parked items), the 6-key ring equal to the MMB ring, RMB-back from the
  Q picker, the Rangefinder element, Shift+drag moves all of a type (Universal Inventory), and
  square split buttons with a choose-count square.
- Step ids stay exactly as in Part C (overrides and progress key on them).

## 1. Ground rules for every agent

1. **Namespace** `StationeersUIMod.UI.Menu.Tutorial` for every tutorial file.
2. **The tutorial never mutates game state and never synthesises input.** Zero references to
   `ItemActions`, `OnServer`, `MoveToSlot` or input injection anywhere under `UI/Menu/Tutorial/`.
3. **C# 7.3 / net48.** ASCII only in every string a player sees (TMP tofu rule; comments may be
   non-ASCII). Hot reload: every static resets in the owner's `Shutdown()`, and every static event
   hook is unhooked there. Zero per-frame allocation in any `Tick()`; text is built on change only.
4. **Concurrency.** Another session ("UI Ascended Third Dev") may be editing this tree right now,
   and other agents in this wave are editing it too.
   - Re-read any file IMMEDIATELY before editing it.
   - Shared (non-tutorial) files get small, additive, targeted `Edit`s only. Never `Write` over an
     existing shared file.
   - No git state changes and no `dotnet build`: the orchestrator builds after the wave.
   - **Keep these Feedback-session hooks intact** (its uncommitted work; confirmed by that session
     2026-09-26):
     - `UiaControlCenter`: `new FeedbackTab(),` stays the LAST entry of `_tabs` in `EnsureBuilt`.
       Never rely on tab indices; use titles.
     - `UIAConfig`: the `Core.FeedbackService.Bind(cfg)` call at the end of `Bind`.
     - `StationeersUIMod.cs`: `FeedbackService.Init()` after `TryPatchAll`, and
       `FeedbackService.Shutdown()` in `OnDestroy`'s `finally`.
     - `FinderCommands`: the `uiafeedback` line, and the Feedback/ entry in the `uiareset` preview.
5. **Game APIs:** verify against the decompile with the Bash tool's grep over
   `Reference/StationeersGameVersions/` (newest snapshot; the Grep tool skips Reference/). Cite it.
6. **Report back:** files touched (exact), every deviation from this contract and why, open
   `[VERIFY]` items you could not resolve, and your play-test steps. A deviation that's reported is
   fine; a silent one is a bug.

## 2. Ownership

| Agent | Owns (creates or edits) | Shared-file edits allowed |
|---|---|---|
| **A Brain** | `TutorialSignals.cs`, `TutorialTokens.cs`, `TutorialChapters.cs`, `TutorialCopy.g.cs`, `TutorialProgressStore.cs`, `TutorialSafety.cs`, `TutorialDirector.cs`, `TutorialLint.cs`, `TutorialTextStore.cs` (v2). Keep `TutorialSteps.cs` and don't delete it: the orchestrator removes it once nothing references it. | `StationeersUIMod.cs` (section 8 wiring), `UIAConfig.cs` (`TutorialTips`), `Core/FinderCommands.cs` (the `uiatutorial` block only) |
| **B Presentation** | `TutorialStrip.cs`, `TutorialSpotlight.cs`, `TutorialSpecs.cs` (card spec + anchors), `TutorialCoach.cs` (extend) | `UI/Hud/HudSystem.cs` (`TryGetElementScreenRect`), `UI/Menu/UiaControlCenter.cs` (anchors, `SelectTab`, `OpenOnTab`, 2 signal raises), `UI/Menu/Tabs/GuideTab.cs` (Lessons section + stale-line fixes) |
| **C Demos** | `TutorialDemoStage.cs` (+ optional new `TutorialDemoScenes*.cs` partials) | none |
| **D Hooks** | none | the raise sites in section 4 only, plus one read-only property on `RadialController` |
| **E Editor** | `TutorialEditorWindow.cs`, `TutorialCopyExport.cs`, `tools/bake-tutorial.ps1` | none. Brain adds the ImGui draw call and shutdown call. |

## 3. Signals: `TutorialSignals.cs` (Brain creates, Hooks raises, Brain consumes)

```csharp
internal enum TSignal : byte
{
    None = 0,
    HandSwapped, WheelOpened, WheelClosed, WedgeHovered, WedgeCommitted, ChildWheelOpened,
    BackedOut, ChipParked, ChipDroppedOnTarget, ChipsDroppedOnClose, ChipsCancelled,
    WorldReachGrab, WorldSlotCueShown, HubMoved, KeepOpenUsed, ValueScrolled, GreyClicked,
    HotkeyBound, BagBound, BoundBagOpened, InWheelDigitJump, GearHeld, BeltPickerOpened,
    BeltSwapped, BeltPickerBack, SearchOpened, SearchTook, SmartStowed, GridOpened, GridClosed,
    GridTabToggled, GridCellTaken, GridItemWheelOpened, GridDragMoved, GridShiftDragMoved,
    GridStackPopupOpened, GridSplitDone, DeviceWindowOpened, PinCreated, PinClosed,
    InGridOpened, ScrollHighlightMoved, KeyboardTake, VitalsTooltipShown, MenuOpened,
    MenuTabSelected, DesignerOpening,
    Count
}
internal enum TWheelKind : byte { Other = 0, Belt = 1, HeldTool = 2, Bag = 3, Gear = 4, BoundBag = 5, BeltPicker = 6, Search = 7, Hub = 8 }
internal enum TWedgeKind : byte { Other = 0, Take = 1, Open = 2, Stow = 3, Swap = 4, Setting = 5, Value = 6, Hub = 7, Close = 8 }
internal enum TCloseRoute : byte { Other = 0, Rmb = 1, Esc = 2, OpenerKey = 3, Mmb = 4, CloseBand = 5, AfterAction = 6, Switch = 7, Guard = 8 }

internal static class TutorialSignals
{
    internal static void Raise(TSignal s, int arg = 0, string text = null); // never throws, zero-alloc
    internal static int Frame(TSignal s);   // Time.frameCount of the last raise, -1 = never
    internal static int Arg(TSignal s);
    internal static string Text(TSignal s); // stored reference, no copy
    internal static int Count(TSignal s);   // raises since load (resets in Shutdown)
    internal static bool Since(TSignal s, int frame); // raised at or after frame
    internal static int Pack(int kind, int flags) => kind | (flags << 8);
    internal static void Shutdown();
}
```

**Arg meanings** (flags go in bits 8 and up):

| Signal | arg | text |
|---|---|---|
| `WheelOpened` | `Pack((int)TWheelKind, sticky ? 1 : 0)` | - |
| `WheelClosed` | `(int)TCloseRoute` | - |
| `WedgeHovered` | `Pack((int)TWedgeKind, (hasArrow?1:0) \| (grey?2:0))` | - |
| `WedgeCommitted` | `(int)TWedgeKind` | - |
| `SmartStowed` | `ok ? 1 : 0` | - |
| `GridOpened` | `latched ? 1 : 0` | - |
| `GridDragMoved` | 0 other, 1 bag cell, 2 HUD box, 3 world slot, 4 ground | - |
| `InGridOpened` | 1 gear key, 2 HUD box, 3 other | - |
| `MenuTabSelected` | tab index | tab title (existing string) |
| everything else | 0 | - |

The Director polls some states itself instead of receiving signals: the active hand's contents,
cursor free/latched (`CursorLatch.Active`, `Cursor.visible`), tier, heading, the held item's
class and battery %.

## 4. Raise sites (Hooks agent)

Plan D.2 rows 13-22 and D.3, re-mapped onto the current code:
- `Features/RadialController.cs`: wheel open/close + kind, in-wheel digit jump, bag bound,
  bound bag opened, belt picker opened/back.
- `Overlay/RadialMenu.cs`: hover change (reuse `RadialEntry.ClickVerb()` / the D-022 classifier
  for the kind, so a lesson and the wheel can never disagree), commit, child wheel, BackedOut,
  chip park, drop-on-target, drop-on-close (inside the D-004 shared handler, only when at least
  one chip actually dropped), cancel, world-reach grab, hub drag, keep-open used, value
  scrolled, grey click.
- `UI/SearchPanelView.cs`: search opened, took.
- `Features/SmartStowPlus.cs` (or the current stow-flash site): `SmartStowed(ok)`.
- `Features/EquipmentKeyRadialFeature.cs`: `GearHeld`.
- `Features/ToolbeltRadialFeature.cs` (or the picker select site): `BeltSwapped`.
- `Features/BagHotkeyStore.cs`: `BagBound`.
- `Core/WedgeHotkeys.cs`: `HotkeyBound`.
- The grid: `UI/Grid/TheGridPanel.cs`, `GridTab.cs`, `BagGridCell.cs`, `GridSelection.cs`,
  plus `Core/DeviceWindow.cs`.
- `Core/WorldSlotCue.cs`: `WorldSlotCueShown`.
- `UI/Hud/Widgets/VitalsPanelWidget.cs`: `VitalsTooltipShown`.

**Rules:**
- Raise AFTER the real action returns successfully, from the UI call site. Never raise inside
  `ItemActions`.
- One line per raise. `Raise` never throws, so it needs no try/catch.
- Expose the active radial's kind read-only on `RadialController`
  (`internal static TWheelKind ActiveWheelKind`).

## 5. Presentation APIs (Presentation agent implements, Brain calls)

```csharp
// TutorialSpecs.cs (B)
internal sealed class TCardSpec
{
    internal string StepId;        // editor jump + inline edit keys
    internal string Heading;       // already resolved (tokens -> glyphs)
    internal string Body;          // resolved, may contain \n
    internal string DemoId;        // null = static frame
    internal string[] Buttons;     // resolved labels
    internal int PrimaryIndex;     // default 0
    internal bool Pause;           // hold GamePause while open (SP only; coach checks CanOwnPause)
    internal bool HasHole; internal Rect HoleScreenRect;   // scrim-with-hole around a SPOT
    internal string RunningLine;   // replaces the pause sentence when no pause can be taken
    internal System.Action<int> OnButton;   // button index
    internal System.Action OnLater;          // X / Esc
    // Added in review fixes (2026-09-26): the copy keys the dev inline editor edits. null = default
    // "<StepId>|heading" (falls back to "|title") / "<StepId>|body"; "" = composed text, read-only.
    // The Director sets these whenever a card shows a VARIANT (e.g. body@mp, body@running).
    internal string HeadingKey = null;
    internal string BodyKey = null;
}
internal static class TutorialAnchors
{
    internal const string GuideTab = "<the Guide tab's exact title in the new F10>";
    // anchor id grammar for TryGetAnchorRect: "tab:<exact tab title>", "search", "pause", "close"
}

// TutorialCoach (B extends; the existing pause / modal / Esc / unwind plumbing stays)
internal static void OpenCard(TCardSpec spec);
internal static void OpenCallout(TCardSpec spec, Rect anchorScreenRect); // 380x200 beside anchor, sort 5300, no scrim
internal static void CloseCard();
internal static bool CardOpen { get; }

// TutorialStrip (B): sort 5150, no raycaster, never takes input
internal static void Show(string stepId, string header, string bodyResolved, string demoId);
internal static void Confirm(string thenResolved, float minSeconds, System.Action onDone);
internal static void SetChrome(string lineResolved);   // null clears (on-hold / reopen lines)
internal static void SetDrain(float fraction01);       // S2 drain line; < 0 hides
internal static void PulseKeys();                      // one waiting pulse
internal static void Hide(bool fade = true);
// Added in review fixes: live-edit refresh. Same step showing steadily -> swap text in place (no
// fade, no demo restart, confirm/chrome/drain/pulse untouched); otherwise behaves like Show.
internal static void UpdateText(string stepId, string header, string bodyResolved);
// SetChrome gained an optional flag: SetChrome(string line, bool compact = false) - compact is
// the C.19 "Lesson waiting" chip.
internal static bool IsVisible { get; }
internal static string CurrentStepId { get; }
internal static void Tick(); internal static void Shutdown();

// TutorialSpotlight (B): sort 5140
internal static bool Show(HudElementType type, string srcParam = null); // false = not on screen; copy stands
internal static void ShowRect(Rect screenRect);
internal static void Clear(); internal static void Tick(); internal static void Shutdown();

// TutorialDemoStage (C) - one instance per host (the coach's demo area, the strip's mini slot)
internal static TutorialDemoStage Create(RectTransform parent);                    // exists
internal void Show(string demoId);                                                 // exists: keep working = Show(id, TutorialTokens.Glyph, false)
internal void Show(string demoId, System.Func<string, string> glyph, bool mini);   // NEW: mini = 150x76 strip layout; unknown id -> blank, never throws
internal void Tick(); internal void Destroy();                                     // exist
internal static bool IsKnownDemo(string demoId);   // NEW: includes the "keys:<pattern>:<token>" grammar
// keys patterns: tap, hold, double, chord, lmb, rmb, mmb, scroll, drag, shiftdrag

// HudSystem (B)
internal static bool TryGetElementScreenRect(HudElementType type, string srcParam, out Rect rect);

// UiaControlCenter (B)
internal static void OpenOnTab(string title);   // opens F10 on that tab
internal static bool SelectTab(string title);
internal static bool TryGetAnchorRect(string anchorId, out Rect screenRect);
```

**Cards and the old step list:**
- The coach becomes a single-card presenter; the Director does the sequencing (including Watch
  mode's Back/Next).
- B removes the coach's dependency on `TutorialSteps`.
- When `UiaDevMode.Active`, cards keep in-place heading/body editing (the existing
  `TutorialTextField` plumbing), writing through `TutorialTextStore.Set("<StepId>|heading")` and
  `"...|body"`, and show a small EDIT button that calls `TutorialEditorWindow.OpenAt(spec.StepId)`.

**GUIDE tab Lessons section (B):**
- Uses `TutorialDirector.Lessons`, `StateOf`, `PlayLesson(id, watch)`, `SkipCurrentLesson`,
  `StopAllLessons`, `Restart`, `UIAConfig.TutorialTips`.
- Supporting copy from Part C.19.

## 6. Data, text and director APIs (Brain implements; Presentation / Editor call)

**Copy keys** (one grammar for everything editable):
`"<stepId>|<field>"` or `"<stepId>|<field>@<variant>"`; lesson titles `"lesson:<lessonId>|title"`;
chrome lines (C.19) `"chrome|<name>"`; the as-I-go tips `"tip.<n>|says"`.

**Fields and budgets:**

| Field | Budget (characters) |
|---|---|
| `title` (step title, list row / replay heading) | 28 |
| `heading` (cards) | 28 |
| `body`, `body@mp` | 280 |
| `says`, `says@<variant>` | 125 |
| `then`, `then@<variant>` | 100 |
| `oops@<name>` | 125 |
| `branch@<name>` | 125 |
| `callout` | 160 |
| `button@<i>` | 24 |
| tips | 125 |
| lesson title | 28 |

Stored text uses code tokens (`{UIA_ToolbeltRadial}`, `{V:SwapHands}` - the plan's "How to read
Part C" table), never `[Key]`.

```csharp
internal enum TPresentation : byte { Card, Strip, Callout, Tip, Chrome }
internal struct TField { internal string Key; internal string Label; internal int Budget; }
internal sealed class TStep   { internal string Id; internal TPresentation Kind; internal string DemoId; internal TField[] Fields; /* + Brain's own: done-condition id, spots, timers... */ }
internal sealed class TLesson { internal string Id; internal int Priority; internal TStep[] Steps; internal string TitleKey; /* + triggers, needs, skill evidence... */ }
internal static class TutorialChapters
{
    internal const int ScriptVersion = 1;
    internal static readonly TLesson[] Lessons;
    internal static readonly string[] ChromeKeys;   // C.19 keys, export order
    internal static readonly string[] TipKeys;
    internal static TStep FindStep(string stepId);
}

internal static class TutorialTokens
{
    internal static string Resolve(string raw);   // tokens -> live glyphs; lifted out of TutorialCoach.Resolve/GlyphFor
    internal static string Glyph(string token);
}

internal static class TutorialTextStore   // v2 - same file config/StationeersUIMod/Tutorial/TutorialText.xml
{
    internal static string Get(string key);       // override, else shipped default from TutorialCopy.Pairs
    internal static string Default(string key);
    internal static bool IsOverridden(string key);
    internal static void Set(string key, string text);   // ASCII-sanitised; empty / == default -> revert
    internal static void Revert(string key);
    internal static void Save();
    internal static int OverrideCount { get; }
    internal static System.Collections.Generic.IEnumerable<string> OverriddenKeys { get; }
    internal static string Sanitize(string s);
    // Added in review fixes (2026-09-26). Save is now atomic (tmp + swap) and returns bool (false
    // = NOT saved; callers show why).
    internal static bool Reload();                       // retry after a failed load
    internal static bool LoadFailed { get; }             // parse failure -> .bad copy, read-only
    internal static string SaveBlockedReason { get; }    // null = saving works
    internal static void SuppressWritesUntilRestart();   // uiareset: no writes for the session
}   // the old 15-step entries load without error and are ignored (harmless orphans);
    // TutorialSteps.cs was deleted at integration
// TutorialProgressStore also gains SuppressWritesUntilRestart() (uiareset).

internal static class TutorialLint
{
    internal static string CheckField(string key, string text, int budget); // null = OK, else one short ASCII problem line
    internal static System.Collections.Generic.List<string> RunAll();       // uiatutorial lint
    // Added in tour review fixes: the ONE place placeholders become budget stand-ins ({OPENER} in
    // chrome|reopen; {N}/{TOTAL}/{TITLE} in chrome|tourheader). Lint AND the editor counter use it.
    internal static string ShownForBudget(string key, string text);
}

internal enum TLessonState : byte { New, Offered, Active, Done, Skipped, Learned, Later }
internal static class TutorialDirector
{
    internal static void Tick(); internal static void Shutdown();
    internal static TLesson[] Lessons { get; }
    internal static TLessonState StateOf(string lessonId);
    internal static bool PlayLesson(string lessonId, bool watch);
    internal static void SkipCurrentLesson(); internal static void StopAllLessons(); internal static void Restart();
    internal static string ActiveLessonId { get; } internal static string ActiveStepId { get; }
    internal static bool PreviewStep(string stepId);   // editor: show that step's presentation now;
                                                       // no progress writes, no done-wait
    internal static void StopPreview();
    internal static void RefreshActiveText();          // re-resolve the showing strip/card from the text store now
    // Added for tour mode (2026-09-26):
    internal static void StopTour();              // end a running/paused tour WITHOUT turning lessons off;
                                                  // unfinished tour lessons keep one just-in-time comeback
    internal static bool MenuClosedThisFrame { get; }  // the director closed F10 this frame (plugin toggle skips it)
}
// Tour state for UI: TutorialProgressStore.TourState == TTourState.Running (started, not finished).
```

## 7. Shipped copy file and bake (Brain writes the first version, Editor's exporter keeps it)

`UI/Menu/Tutorial/TutorialCopy.g.cs`, exactly this shape: ASCII, CRLF, escapes only `\\`,
`\"`, `\n`.

```csharp
// <auto-generated> UIA lesson copy. Edit in game (uiadev, then uiatutorial edit), then run
// tools/bake-tutorial.ps1. Hand edits here are fine but get reformatted by the next export.
// ASCII only. Pairs: key, text. Key grammar: see Documentation/0.9.8.0/Tutorial-Build-Contract.md s6.
namespace StationeersUIMod.UI.Menu.Tutorial
{
    internal static class TutorialCopy
    {
        internal static readonly string[] Pairs =
        {
            "lesson:core|title", "FIRST STEPS",
            "core.hands|title", "Your two hands",
        };
    }
}
```

**Order:**
1. Lessons in `TutorialChapters.Lessons` order. Each lesson's title key comes first, then its steps
   in order, each step's `Fields` in array order.
2. Then `TipKeys`.
3. Then `ChromeKeys`.
4. A comment line `// <lessonId> - <title>` before each lesson.

**Exporter** (`TutorialCopyExport`, Editor agent):
- Iterates the same order and writes `TutorialTextStore.Get(key)` for each key to
  `config/StationeersUIMod/Tutorial/Export/TutorialCopy.g.cs`.
- With zero overrides it must reproduce the repo file byte for byte. That makes a good test, and
  either side may adjust formatting to reach it; report which.

**`tools/bake-tutorial.ps1`:**
- Copies the export into the repo path.
- Validates it: ASCII, balanced braces, even pair count, no duplicate keys.
- `-ClearOverrides` backs up, then clears, `TutorialText.xml`.
- Uses .NET file APIs with explicit UTF-8, never `Get-Content`/`Set-Content` round-trips, and
  does no regex editing of source (CLAUDE.md trap).

## 8. Wiring (Brain, in `StationeersUIMod.cs`)

- The first-run block (currently ~`:426-443`) moves into `TutorialDirector.Tick()`, same gate and
  1 s settle.
- Tick `TutorialStrip.Tick()` and `TutorialSpotlight.Tick()` after the Control Center pump.
- Add `TutorialEditorWindow.Draw()` in the ImGui pass (next to `RadialEditorMode.Draw` /
  `HudEditorWindow.DrawPopupOverlay`, ~`:940-970`).
- `Shutdown` in the un-skippable `finally`, each in its own try/catch, next to
  `TutorialCoach.Shutdown`: Director, Strip, Spotlight, Signals, ProgressStore,
  `TutorialEditorWindow.Shutdown()`.
- The F9 first-press gate for lesson 17 goes on the `ToggleHudEditor` path.
- `UIAConfig.TutorialTips`: bool, default true, "1. General". No ConfigMigration step (the
  SmartStow overhaul took v5; the Feedback work may take v6; the tutorial takes none).

## 9. Editor (Editor agent)

- `TutorialEditorWindow`: `Toggle()`, `OpenAt(string stepId)`, `Draw()`, `Shutdown()`.
- Refuses unless `UiaDevMode.Active`, with the hint "type uiadev first".
- Opened by `uiatutorial edit`, by the card EDIT button, and by a dev hotkey while a lesson strip or
  card is showing. That hotkey jumps to the live step: "edit while playing". The Editor agent
  picks a key that is free in vanilla and the mod, active only in dev mode, and reports it.

**Window contents:**
- A lesson list (with state) that expands into steps.
- Per step, every `TField`:
  - a multi-line input;
  - a live character count against the budget;
  - the `TutorialLint.CheckField` result;
  - token insert buttons (the [Key] table), and a resolved preview line;
  - an edited marker, with Revert (field / step).
- Preview (`TutorialDirector.PreviewStep`) and Stop.
- Typing into the live step's field calls `RefreshActiveText()` so the on-screen strip/card
  updates as you type.
- Header: a search/filter ("edited only"), override count, Save, and Export (section 7) with a
  one-line "then run tools/bake-tutorial.ps1" instruction.
- Verify ImGui input works while the coach's modal card is open (TypingState / scrim) and fix or
  report.
