# 07 — Onboarding & Tutorial

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** "We need to ship a tutorial explaining all features + hotkeys — probably gifs or little videos per major feature. A popup when they first open the mod in a save; a special menu for choosing their first theme, asks if they want bare mode and diegetics etc."

**Verdict:** NEEDS WORK BEFORE RELEASE — the plumbing you'd need is already built and working (first-run trigger, live-bind Guide tab, hint fade, theme cards, PNG loaders), but the Guide's *content* stopped at ~0.9.0 and never mentions the Universal Inventory, pins, belt swap, drag flows or the cursor latch; the first-run flow lands on a text page instead of a theme choice; and the profile cards say "PREVIEW Coming soon" because no preview art ships.

## Findings

### 1. You already have a first-run tutorial system — more than you may remember

There is a complete, working chain today:

- **First-run trigger.** `Assets/Scripts/StationeersUIMod/StationeersUIMod.cs:362-367` — `Update()` checks `!UIAConfig.GuideShown.Value && Guards.CanToggleMenus() && !UiaControlCenter.IsOpen && !_radials.IsRadialOpen`, sets the flag, and calls `UI.Menu.UiaControlCenter.OpenGuide()`. `GuideShown` is a config-backed once-per-install flag (`UIAConfig.cs:348-349`, resettable by hand — the description even says so). `Guards.CanToggleMenus()` (Core/Guards.cs:50-55) requires `GameState.Running` and no console/input window/creative spawn, so this *is* the "first time safely inside a save" moment you asked for — no new WorldManager hook is needed. It polls every frame until conditions are right, so it cannot be missed by a loading screen.
- **A Guide window with live keybind chips.** `UI/Menu/Tabs/GuideTab.cs` is a full how-to page: two-halves explanation, radial gestures, SmartStow+, HUD designer — and its key chips read **live** binds via `UiaKeybinds.Glyph("UIA_ToolRadial")` etc. (GuideTab.cs:33-63), so rebinds can never desync the tutorial. This is exactly the "tutorial reads binds from config" pattern the release needs; it just needs to be extended, not invented.
- **A re-open path.** `UiaControlCenter.OpenGuide()` (UI/Menu/UiaControlCenter.cs:142-150) is public and the Guide tab is permanently reachable via F10. There is no visible "?" affordance anywhere on the HUD, but the hook exists.
- **Contextual first-use hints with progressive fade.** `Features/HintUsageStore.cs` (per-save XML counters, batched writes, hot-reload-safe `Reset()` at :130-135) + `UI/RadialHintBar.cs` (7 hint kinds — select/back/reach/swaphand/page/worldgrab/worldplace, `RadialHintBar.cs:51-58`; fade threshold 25 exposures at :47; context-sensitive Alt-held grab/place variants at :263-265). A reset button exists in F10 (`UI/Menu/Tabs/RadialTab.cs:50`). The pattern extends cleanly to a feature tour ("first time the Grid opens, show a one-line hint"), with one caveat: the store is keyed **per save** via `BagProfileStore.CurrentSaveKey()` (HintUsageStore.cs:49) — first-use *tour* nudges should probably be per-install instead, which is a one-line variation (fixed key), not a new system.
- **The keybind registry.** `Core/UiaKeybinds.cs:75-96` is the single source of truth for the 9 mod binds (+ glyph rendering at :244-269, ASCII-only output — "LMB", "MMB", "Alt" — so it is already TMP-safe). `ControlsTab.cs:43-50` renders rebind rows from it plus the fixed chorded actions. Any new tutorial surface should consume this registry, full stop.

**Bottom line: the request "a popup when they first open the mod in a save" already ships.** What's wrong is what the popup *shows*.

### 2. The Guide content is a version behind the mod — the flagship features are missing

`GuideTab.cs` (last substantive content ~0.9.0.1) covers radials, SmartStow, HUD profiles/F9/F10. It does **not** mention, at all:

| Missing feature | Where it lives | Bind (actual, from code) |
|---|---|---|
| The Universal Inventory ("The Grid") | `UIAConfig.GridEnabled/GridKey` (UIAConfig.cs:484-492), handler StationeersUIMod.cs:488-555 | **B** tap-toggle / hold-peek |
| Pin a bag to its own window | StationeersUIMod.cs:601-644 (`TryPinFromEquipmentKey`), :665-715 (mouse-mod + click) | **Shift+1–6** (reads live `KeyManager.GetKey` per slot, :637) / mouse-mod + click a HUD equipment box |
| Grid keyboard nav (wheel cursor, F act, G stow-to-cell) | `UIAConfig.GridKeyboardNav` (UIAConfig.cs:585-592) | wheel / **F** / **G** (vanilla binds reused) |
| Belt swap from the Belt Wheel | `RadialController.cs:559-580` (Q on the toolbelt ring opens the belt picker) → `ItemActions.SwapWornToolbelt` | **Q** while the Belt Wheel is open |
| Cursor latch | `UIAConfig.CursorLatchEnabled` (UIAConfig.cs:350-353) | double-tap the mouse modifier |
| Drag flows (HUD box → world, world → HUD slot, radial drag-park, drag into Grid/pins) | HudSlotDrag / Patch_InputMouse_Drag (StationeersUIMod.cs:212-214) | LMB drag |
| Wedge hotkeys (bind letters to device-setting wedges) | `Core/WedgeHotkeys.cs:8-20` | any bindable letter while hovering |
| Radial feel opt-ins (flick-commit, double-tap repeat) | UIAConfig.cs:358-371 | — |
| MMB now *closes* a radial (0.9.2 change) | CHANGELOG.md 0.9.2.0 "Middle mouse now only closes a radial" | MMB |

Also stale *inside* the Guide: "Equipment wheels" is a hard-coded `"1 - 6"` string (GuideTab.cs:36) and "Reach into the world" a hard-coded `"Alt"` (:39 — the real bind is vanilla `KeyMap.MouseControl`, which a player can rebind); "Swap toolbelt / backpack" is hard-coded `"Tab"` (:42). Same hard-codes in `ControlsTab.cs:47-49`. These are exactly the drift the live-glyph pattern exists to prevent.

The GuideTab's Q chip (`UiaKeybinds.Glyph("UIA_Page")`, GuideTab.cs:41) is now **overloaded**: Q pages a crowded ring (UIAConfig.cs:334-336) *and* opens the belt picker on the Belt Wheel (RadialController.cs:559-580). The guide explains only the former.

### 3. First-run lands on text, not on a choice

The concern asks for "a special menu for choosing their first theme, asks if they want bare mode and diegetics." Today `OpenGuide()` lands on the Guide tab — a wall of (good) text. Everything a Welcome/setup page needs already exists as parts:

- **Theme cards with click-to-apply and live preview**: `ProfilesTab.Card()` + `Apply()` (UI/Menu/Tabs/ProfilesTab.cs:101-161) — applying a profile restyles the HUD instantly (the F10 window itself is live HUD glass and re-skins via `StyleHash`, UiaControlCenter.cs:168-170). The shipped set is curated to "Stationeers Blue" + "Pure HUD" (ProfilesTab.cs:22-23).
- **The toggles the concern names all exist as config entries**: diegetics = `UIAConfig.HardcoreGating` (UIAConfig.cs:623-624, "Diegetic mode: status strip & vitals need a worn helmet…"); the two halves = `RadialEnabled` / `HudConfig.VisorHudEnabled` (already the master strip, UiaControlCenter.cs:357-367); effect tiers for perf = `HudConfig.FxTierA/B/C` (UI/Hud/HudConfig.cs:122-124, bound :508-514 — Tier C ships default-off as EXPERIMENTAL); bare-mode presentation = `HudConfig.BareFlattens` / bare-tier settings (HudConfig.cs:53, :361).
- **The widget kit** (UiaControls/UiaUi/UiaTheme) builds all of this in code, hot-reload-safe.

So the "special menu" is an afternoon-to-days *composition* job, not new infrastructure.

### 4. No preview art ships — the theme picker is currently blind

`ProfilesTab` looks for `<name>.png` beside each profile (`HudProfileStore.PreviewPath`, Features/HudProfileStore.cs:511-517; loader `UiaImages.Load`, UI/Menu/Kit/UiaImages.cs:15-33) and `SyncShipped` already copies shipped PNGs seed-if-absent (HudProfileStore.cs:132-138). But the shipped `HudProfiles/` folder contains only `Pure HUD.xml`, `Stationeers Blue.xml`, `README.md` — **zero PNGs** — so every card renders the "PREVIEW / Coming soon" placeholder (ProfilesTab.cs:120-125). For a first-run theme chooser this is the difference between a choice and a guess. Two screenshots fix it; the code path is done.

### 5. Media feasibility — what an in-game tutorial can actually show

Verified against the live game install and the codebase:

- **Animated GIF: not natively decodable.** Unity has no GIF decoder; `Texture2D.LoadImage` (ImageConversionModule) handles PNG/JPG only. Vendoring a managed GIF decoder into net48/C# 7.3 is possible but strictly worse than every alternative. Rule it out.
- **Real video is genuinely on the table.** The game ships `UnityEngine.VideoModule.dll` (verified in `rocketstation_Data/Managed/`), and Stationeers is a Mono build, so the engine's native video code is not stripped. That means `UnityEngine.Video.VideoPlayer` → RenderTexture → UGUI RawImage playing a loose `.mp4`/`.webm` from the mod folder (`StationeersUIMod.ModDirectory`, StationeersUIMod.cs:73) should work — no SLP `.assets` bundle involved (good: bundle auto-load is fail-fatal per the packaging notes; loose files fail soft). `Dev/StationeersUIMod.Dev.csproj` does not yet reference VideoModule (verified: refs at csproj lines 44-104) — a one-line addition. **This needs a half-day spike before you commit to it** (H.264 decode goes through Windows Media Foundation; test on the real machine, and give every clip a static-screenshot fallback for the case the player prepared won't play). Cost: ~2-4 MB per 10-15 s 720p clip; 8-10 clips ≈ 20-40 MB against today's ~2.4 MB package (`dist/StationeersUIMod`) — a 10-15x package growth, acceptable for Workshop but not free.
- **Sprite-sheet flipbooks: feasible but the worst deal.** The loaders exist (`HudIconStore.Scan`, Core/HudIconStore.cs:41-81, and `UiaImages.Load` — both track textures for hot-reload teardown, both fail soft per file). But the arithmetic is unkind for *gameplay* footage: a 4 s, 8 fps, 320×180 clip is 32 frames ≈ a 2560×720 sheet ≈ 7.4 MB VRAM decoded, and PNG compresses gameplay noise badly (2-6 MB on disk per clip). Ten features ≈ 20-60 MB disk plus VRAM churn, for 4 grainy seconds each. Flipbooks only make sense for tiny, synthetic loops (a 6-frame "hold key → ring opens" diagram), not recorded gameplay.
- **Static annotated screenshots: cheap, robust, boring.** One 960×540 PNG per feature ≈ 200-600 KB. Loaders exist. Zero risk. The right *floor* for v1.
- **Procedural demos: the mod can animate itself.** The radial renderer is code-built (`UI/UnityRadialView.cs`); `Windows/RadialEditorMode` already draws *example* radials on a black backdrop for the F10 editor (StationeersUIMod.cs:843-847). A tutorial page could drive a scripted fake ring the same way. High polish ceiling, but it is real engineering per feature — days each, and it competes with fixing actual bugs. Not v1 material except possibly for the radial page.
- **Link out: trivial and unbuilt.** No `Application.OpenURL` usage exists in the mod today (verified). A "Watch the 2-minute video" button on the Guide opening Steam's overlay browser at the Workshop page is ~5 lines and offloads all media weight to Steam, where the *real* trailer lives anyway.

**Honest weighing:** the Workshop page is the video channel; in-game, screenshots + live-bind text chips are the reliable v1, with the VideoPlayer spike as the high-value stretch. Do not build a GIF decoder, and do not ship 40 MB of PNG flipbooks.

### 6. Full feature/hotkey inventory the tutorial must cover (extracted from code, 0.9.2.0 + WIP)

Binds marked (cfg) are rebindable via `UiaKeybinds`/config — the tutorial must render them via `UiaKeybinds.Glyph`, never as string literals. Binds marked (vanilla) reuse the game's own keys and should be read via `KeyManager.GetKey` (as `KeybindChipsWidget.DropKey()` already does, UI/Hud/Widgets/KeybindChipsWidget.cs:118-123).

| # | Feature | Gesture / bind | Source |
|---|---|---|---|
| 1 | Belt Wheel (toolbelt radial) | hold **MMB** (cfg `UIA_ToolbeltRadial`); tap = vanilla; MMB again closes | UIAConfig.cs:381-382; CHANGELOG 0.9.2 |
| 2 | Belt swap (wear a different belt) | **Q** on the open Belt Wheel (cfg `UIA_Page`, overloaded) | RadialController.cs:559-580 |
| 3 | Tool/device radial | hold **R** (cfg `UIA_ToolRadial`); tap keeps vanilla slot window | UIAConfig.cs:394-397 |
| 4 | Bag radial | tap **Tab** (cfg `UIA_BagRadial`; hold = scoreboard, mode flag `BagRadialTapOpens`) | UIAConfig.cs:401-405 |
| 5 | Equipment radials | tap **1–6** opens manage-ring, hold equips (vanilla equipment buttons, read live) | UIAConfig.cs:421-423; EquipmentKeyRadialFeature |
| 6 | In-radial: select / back / reach / swap hand / page / swap belt-backpack | LMB / RMB / mouse-mod (vanilla `KeyMap.MouseControl`) / **E** (cfg) / **Q** (cfg) / **Tab** | UIAConfig.cs:331-336; RadialController.cs:586-602 |
| 7 | Bag hotkeys | bind: press **1–0** over a hovered bag; open: **Ctrl+1–0** | ControlsTab.cs:48-49; BagHotkeyStore |
| 8 | Wedge hotkeys (device settings) | press a letter over a setting wedge; same letter re-press clears | Core/WedgeHotkeys.cs:8-20 |
| 9 | Drag-out parking + world placement cue | drag a wedge/chip off the ring; coloured vanilla placement box | CHANGELOG 0.9.2; Core/WorldSlotCue.cs |
| 10 | SmartStow+ | **G** (vanilla SmartStow) — stacks, sockets, profiles, affinity, type memory; works with a radial open | UIAConfig.cs:431-482 |
| 11 | Universal Inventory | **B** (cfg `UIA_Grid`): tap toggle, hold peek; never steals the mouse (mouse-mod to click) | UIAConfig.cs:484-495; StationeersUIMod.cs:470-487 |
| 12 | Pin a bag | **Shift+1–6** toggle (vanilla equipment keys, read live) or mouse-mod + click a HUD equipment box; drag the manila tab | StationeersUIMod.cs:601-715 |
| 13 | Grid keyboard nav | wheel = cursor, **F** = act, **G** = stow to cell (vanilla binds reused) | UIAConfig.cs:585-592 |
| 14 | Drag flows (HUD↔world↔windows) | LMB drag from hand/1-6 boxes, into the Grid/pins, world item into a HUD slot | StationeersUIMod.cs:209-214; CHANGELOG 0.9.1/0.9.2 |
| 15 | Cursor latch | double-tap the mouse-mod key (cfg window ms) | UIAConfig.cs:350-356 |
| 16 | HUD Designer | **F9** (cfg `UIA_HudDesigner`) | UiaKeybinds.cs:89-90 |
| 17 | Control Center | **F10** (cfg `UIA_Menu`; the only native Controls-screen row — deliberate, see UiaKeybinds.cs:34-56 — do not "fix") | UiaKeybinds.cs:86-88 |
| 18 | Radial feel opt-ins | flick-commit, double-tap repeat, wedge sounds, hint fade | UIAConfig.cs:358-377 |
| 19 | Diegetics / bare mode / effect tiers | `HardcoreGating`, bare-tier senses, FxTierA/B/C | UIAConfig.cs:623-624; HudConfig.cs:122-124 |
| 20 | Console diagnostics | `finddead`, `findlargebox`, `uiaprof`, `uiaflash`, `uiadiag` (read-only; power-user page, not the main tour) | Core/FinderCommands.cs:258-300 |

Two collisions worth a sentence in the tutorial itself: B is vanilla's (dead) `InstantStop` (UIAConfig.cs:154-157 documents it), and Tab-hold vs scoreboard behaviour depends on `BagRadialTapOpens`.

## Severity — how bad is it really

- **This is not a systems gap; it is a content gap.** Nothing here is architecturally broken. The first-run trigger, live-bind chips, hint fade, and theme-apply are shipped, reviewed code. What would actually bite users on day one is: (a) they get a first-run guide that never mentions **B** — the single feature most likely to make them keep the mod — and (b) the theme picker shows placeholder art, which reads as "unfinished" in the first 60 seconds of a first session.
- **The hard-coded key strings are ugly-but-mostly-harmless** — until someone rebinds Tab or plays on a non-QWERTY layout, at which point the guide actively lies. Low frequency, high confusion when it hits.
- **The absence of gifs/videos in-game is NOT a release blocker.** Every successful UI mod ships with a Workshop page video + an in-game text/key reference. Blocking release on an in-game media pipeline would be the classic mistake here.
- **The Q overload (page vs belt-swap) is a real teaching problem**, not just a docs problem — two different actions on one key with context-dependent meaning needs one clear sentence, or players will "lose" their belt mid-ring and blame the mod.
- Risk of the recommended work: near zero. Everything proposed is display-only (no `ItemActions` involvement, MP-safety untouched), builds on the existing kit, and follows the established static-reset teardown pattern (`UiaControlCenter.Shutdown()` at UiaControlCenter.cs:497-524 is the template).

## Recommendations

1. **P0 (S) — Bring GuideTab up to 0.9.2.** Add sections for the Universal Inventory (B, hold-peek, mouse-mod to click, Shift+1-6 pins, keyboard nav), belt swap (Q on the wheel), drag flows, and the cursor latch; note MMB-closes; keep every key as a `UiaKeybinds.Glyph`/`KeyManager.GetKey` chip. Replace the hard-coded `"1 - 6"`, `"Alt"`, `"Tab"` strings in GuideTab.cs:36-42 and ControlsTab.cs:47-49 with live lookups (`KeyManager.GetKey(EquipmentKeyRadialFeature.ButtonNames[i])`, `KeyManager.GetKey(KeyMap.MouseControl)` — glyph helper already handles ASCII). This is text + existing helpers; half a day including reading it back in-game.
2. **P0 (S) — Ship preview PNGs for "Stationeers Blue" and "Pure HUD".** Two screenshots (≈600×320, cropped to the HUD) dropped into `HudProfiles/` beside the XMLs; `SyncShipped` (HudProfileStore.cs:132-138) and `ProfilesTab.Card` (ProfilesTab.cs:115-119) already do the rest. Also drop the same art into `dist/` staging (remember the staging-goes-stale trap from the packaging notes).
3. **P1 (M) — First-run Welcome page instead of landing on the Guide.** New `WelcomeTab` (or a pre-page inside `OpenGuide()` when `GuideShown` was false): title, the two theme cards (reuse `ProfilesTab.Card`/`Apply` — applying restyles the HUD live behind the window, which *is* the preview), three labeled toggles — Diegetic mode (`HardcoreGating`), Frosted glass / effects (map a single "Performance / Balanced / Fancy" choice onto FxTierA/B/C rather than exposing three tiers), and the two master switches — then a "Show me the controls" button into the Guide tab and a "You can reopen this any time with F10" footer line. All display-only; statics reset in `UiaControlCenter.Shutdown()`. 1-2 days including copy.
4. **P1 (S) — A visible re-open affordance.** Add a "?" button (drawn glyph or ASCII "?") to the Control Center title bar next to the X (UiaControlCenter.cs:336-355) routing to `OpenGuide()`, and mention F10 in the first-run window itself. Optionally a one-time toast on the *second* session ("Forgot a key? F10 > Guide" via `Overlay/Toast`).
5. **P1 (S) — Spike `VideoPlayer` playback of one mp4 from the mod folder.** Add the `UnityEngine.VideoModule` reference to Dev.csproj, RawImage + RenderTexture in a test tab, play a 10 s 720p H.264 clip from `ModDirectory`. If it works on the real machine, greenlight recommendation 7; if not, screenshots stand. Half a day, and it must fail soft (missing module/codec → show the static PNG).
6. **P1 (M) — Record the Workshop media set.** This is the real video channel, and only FlorpyDorp can do it. The clip list that covers the mod (10-20 s each): (1) hold MMB → Belt Wheel → release on a tool; (2) Q on the wheel → belt picker → whole-kit swap; (3) tap 1-6 equipment ring + hold-to-equip; (4) G SmartStow routing into a profiled bag (with the stow toast on); (5) B → Universal Inventory → drag tab out → pinned bag → Shift+# toggle; (6) drag hand item → world, world item → HUD slot; (7) F9: move a box, recolor, apply a theme; (8) the two shipped themes side by side; (9) bare → suited transition with diegetics on; (10) the hint bar fading as you learn. One 60-90 s trailer cut from these + the individual clips embedded per-section in the Workshop description. Also: the About.xml `<Description>` (Assets/About/About.xml:7-12) still says "ImGui HUD" and "POC integrated as Unity mod base" — rewrite it while you're in there (separate report's territory, but it is the tutorial's front door).
7. **P2 (M) — In-game media, in this order:** per-feature static annotated screenshots in the Guide (loader exists — `UiaImages.Load`); then, if the spike passed, mp4 clips for the 3 features where motion is the message (Belt Wheel, the Grid drag/pin, drag flows) — not all ten; a "Watch on Steam" `Application.OpenURL` link as the long-form fallback.
8. **P2 (S) — First-use nudges for non-radial features via the HintUsageStore pattern.** One-line hints on first Grid open ("Hold [mouse-mod] to click - Shift+1-6 pins a bag") and first pin ("Drag the tab out - X unpins"), counted per-install (fixed key rather than `CurrentSaveKey()`), threshold ~3 not 25. Keep kind strings stable (they key the persisted XML, HintUsageStore.cs:52-58).
9. **P2 (S) — Resolve the Q overload in teaching, or in fact.** Minimum: the Guide sentence "Q pages a crowded ring; on the Belt Wheel it opens the belt picker." Better post-release: give belt-swap its own default key in `UiaKeybinds` and keep Q as page everywhere (rebind plumbing already exists).

## What NOT to do

- **Do not build or vendor a GIF decoder.** Ruled out above; every alternative is cheaper and better.
- **Do not build an interactive step-by-step tutorial engine** ("now press B… good, now drag…"). That's weeks of state-machine work interacting with modal guards, radial ownership and MP edge cases, for a mod whose audience is Stationeers players — people who read a keybind table. The Guide + nudges pattern is the right weight.
- **Do not ship gameplay-footage flipbooks.** 20-60 MB of PNGs for grainy 4-second loops is the worst point on the cost/quality curve; if motion matters enough, the VideoPlayer path is strictly better, and screenshots are strictly cheaper.
- **Do not register the other 8 binds in the vanilla Controls screen** to make them "discoverable" — the conflict-banner analysis at UiaKeybinds.cs:34-56 is correct and hard-won; the Controls tab + Guide are the discoverability surface.
- **Do not make the Welcome window a separate new window system.** It's a tab (or pre-page) inside the existing Control Center — same modal, same teardown, same theme. A second modal window means a second set of cursor/modal/hot-reload liabilities for zero user benefit.
- **Do not gate release on per-feature videos in-game.** The Workshop page carries the video load; in-game needs correct text and live keys first.
- **Do not hand-write key names anywhere in tutorial copy.** Every key the player sees must come from `UiaKeybinds.Glyph` or `KeyManager.GetKey` — and stay ASCII (the TMP tofu rule: no `▸`/`✕` in displayed strings; draw arrows with `TriangleGraphic` or use ASCII `>`).
