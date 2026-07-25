# 05 — The F10 Control Center

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** "The F10 menu is good but unfinished. Needs polish. I noticed it didn't fully match the Pure HUD theme colors when I switched to that theme. It probably needs its own editor honestly."

**Verdict:** NEEDS WORK BEFORE RELEASE — but the work is small (one data fix in `Pure HUD.xml` + one small color-source fix in the menu kit); it does **not** need its own editor.

## Findings

### 1. What the F10 Control Center is today

`UI/Menu/UiaControlCenter.cs` is a fully code-built, hot-reload-safe UGUI window (no prefabs, no sprite assets beyond one generated rounded-rect — `UI/Menu/Kit/UiaImages.cs:41-68`) on its own ScreenSpaceOverlay canvas at sorting order 5200 (`UI/Menu/UiaControlCenter.cs:288`). Structure: title bar with a Simple/Advanced density toggle (`UiaControlCenter.cs:336-355`), two master switches (Radial / Visor HUD, `UiaControlCenter.cs:357-367`), a six-tab bar, a content area, and a popup layer for dropdowns/pickers (`UiaControlCenter.cs:325-330`).

The six tabs (`UiaControlCenter.cs:274-282`):

| Tab | File | State |
|---|---|---|
| Profiles | `UI/Menu/Tabs/ProfilesTab.cs` | Complete: featured cards w/ preview PNG + placeholder, dropdown, per-profile font, advanced author row |
| Radial | `UI/Menu/Tabs/RadialTab.cs` | Complete; ends in a hand-off button to the **legacy ImGui settings window** for colors (`RadialTab.cs:98-99`) |
| HUD | `UI/Menu/Tabs/HudTab.cs` | Complete for comfort settings; hands element/effect authoring to F9 (`HudTab.cs:59-60`) |
| Storage | `UI/Menu/Tabs/StorageTab.cs` | The deepest tab: SmartStow+ chain, bag assignments, loadouts, rule editor, dry-run test box — finished and well-engineered |
| Controls | `UI/Menu/Tabs/ControlsTab.cs` | Complete: live rebinding via `UiaRebindCapture`, chord reference |
| Guide | `UI/Menu/Tabs/GuideTab.cs` | Complete: text + live key chips; auto-opens once on first run (`StationeersUIMod.cs:362-367`) |

"Real HUD glass" (0.9.1.0) is literal and verified: the window background is a HUD `PanelGraphic` (not an `Image`), restyled **every frame** with the theme fill/border, the global corner radius/border width, and the full F9 effect stack — glow halo, edge light, ripple, sheen, frost — via `HudGlobalGlass.Apply` (`UiaControlCenter.cs:87-105, 309-313`). Interior buttons get an additive glass **border-only** skin, deliberately without the halo (`UI/Menu/Kit/UiaGlassSkin.cs:6-15, 41-55`). Effect tiers B/C are correctly gated on the HUD FX clock so a disabled Visor HUD can't freeze the shine mid-sweep (`UiaControlCenter.cs:97-104`).

"Follows F9 theme": every kit color is a **live getter** on `UI/Menu/Kit/UiaTheme.cs` that, in the default Follow state, derives from `HudPalette` (`UiaTheme.cs:25-71`); `UiaControlCenter.Update` polls `UiaMenuTheme.StyleHash()` and rebuilds the whole window on change, throttled to ~7/s (`UiaControlCenter.cs:168-170, 190-240` — scroll position is preserved across the rebuild, `UiaControlCenter.cs:210-238`). Since per-profile themes land by *applying the snapshot into the global `HudConfig`/`HudPalette`* on profile switch (`Features/HudProfileStore.cs:376-381`, `UI/Hud/HudTheme.cs:73-105`), the menu's follow machinery **does** track profile switches. The plumbing is sound.

"Click-to-edit": with F9 active, F10 opens in *edit-preview* (raycaster off, no modal — `UiaControlCenter.cs:76-80, 107-115`); a click inside the window rect selects the MENU as the edit target (`Windows/HudEditorMode.cs:363-378, 487`) and opens the "Edit: Control Center menu" ImGui popup (`Windows/HudEditorWindow.cs:1045-1069, 1141-1149`), which draws `UiaMenuTheme.DescribeProps` — the Follow toggle plus, when following is off, 18 per-color overrides that accept a palette name or `#RRGGBBAA` (`UI/Menu/Kit/UiaMenuTheme.cs:139-165`).

No TODO/FIXME markers exist anywhere in `UI/Menu/` — the unfinishedness is not stubs, it is the specific gaps below.

### 2. Root cause of "didn't fully match Pure HUD" — three stacked mechanisms

**Mechanism A (the big one): the menu's accent is single-sourced from `HudPalette.LineAccent`, and Pure HUD never retints LineAccent.** `UiaTheme.Accent` = `HudPalette.LineAccent` (`UiaTheme.cs:35, 64`). But the shipped Pure HUD theme snapshot carries its green identity in `PanelBorder`/`TextLabel`/`TextValue` (`33CE00E3`) while **leaving `pal:HudLineAccent` at the default cyan `35C8E8CC`** (`HudProfiles/Pure HUD.xml:1209-1213`). Pure HUD's *layout* never draws with LineAccent at all — the only `HudLineAccent` reference in the whole file is the theme snapshot line itself — so the stale cyan is invisible on the HUD but becomes **the** accent for the entire F10 menu. Everything accent-derived stays cyan while the text goes green:

- the window's glass border (`UiaControlCenter.cs:92` — `_windowPanel.BorderColor = UiaTheme.Accent`),
- every interior button's glass border (`UiaGlassSkin.cs:50`),
- section header text + underline (`UI/Menu/Kit/UiaControls.cs:271`),
- slider fill (`UiaControls.cs:362`),
- dropdown/input outlines (`UiaControls.cs:211, 383`; `UI/Menu/Kit/UiaUi.cs:134`),
- Primary buttons (`UiaControls.cs:406-408`),
- Guide key chips and Storage rule tags (`GuideTab.cs:86-87`, `StorageTab.cs:386-387`).

Note the intent-vs-reality gap: the window comment says the glass is "cornered to the global HUD radius so it matches a HUD box" (`UiaControlCenter.cs:84-85`), but a HUD box's border is `HudPanelBorder` (`UI/Hud/HudElementView.cs:364`, `UI/Hud/HudDocument.cs:291`, `UI/Hud/HandBoxes.cs:94`), which the menu **never reads anywhere**. Under Stationeers Blue this is invisible (its palette keeps the default cyan family — `HudProfiles/Stationeers Blue.xml`: `LineAccent 35C8E8CC`, `PanelBorder 2E7A94AA`), which is exactly why the bug only surfaced on switching to Pure HUD.

**Mechanism B: hardcoded follow-mode colors that ignore the theme.** In Follow mode, `Scrim` and `Divider` are fixed constants (`UiaTheme.cs:25, 32`), the Danger button is hardcoded red (`UiaControls.cs:409-411`), the toggle knob and slider handle are hardcoded white (`UiaControls.cs:84, 89, 326, 366`), the profile-card preview placeholder and item-picker icon placeholders are fixed dark blue (`ProfilesTab.cs:122`, `UI/Menu/Kit/UiaItemPicker.cs:289, 316`). Mostly defensible (danger should stay red; white knobs are fine), but the divider is white-on-anything and the scrim never follows.

**Mechanism C: surfaces derive from PanelFill by lifting toward white, and Pure HUD's fill is transparent neutral gray.** Pure HUD's `PanelFill` is `27272700` — alpha **zero**, hue-less gray (`Pure HUD.xml:1209`). The menu floors the window alpha at 0.85 for readability (`UiaTheme.cs:26` — deliberate, correct) and builds every interior surface by `Lift(HFill, …)` toward white (`UiaTheme.cs:27-31, 74-75`). Result: with Pure HUD active the menu is a solid neutral-gray slab with green text and cyan accents, while the HUD it sits over is borderless floating green text. Some of this is irreducible (an unreadable transparent menu would be worse), but it compounds the perceived mismatch. Also `TextDim`/`TextMute` map to `HudTextLabel`/`HudTextDim`, and Pure HUD leaves `HudTextDim` at the default blue-gray `6A8E9BA8` (`Pure HUD.xml:1214`) — so secondary text stays blue-gray next to green primary text.

Summary of the bug in one sentence: **the follow machinery works; the mismatch is (a) Pure HUD's theme data leaving `LineAccent`/`TextDim` at cyan defaults it never uses itself, and (b) the menu ignoring `PanelBorder` — the color that actually defines a Pure HUD box edge — in favor of `LineAccent` for every accent surface.**

### 3. "It probably needs its own editor" — assessment: no

What click-to-edit already gives (verified above, §1): the Follow toggle, and 18 per-color overrides rendered with the same swatch/palette-combo/hue-wheel drawer an F9 element gets (`UiaMenuTheme.cs:139-165`, `HudEditorWindow.cs:1060-1065`). An override can *name a palette entry*, so a power user can already fix Pure HUD themselves today by unchecking Follow and typing `HudPanelBorder` into "Accent (primary)" (`UiaMenuTheme.cs:82-94`).

What is hardcoded and NOT reachable by any editor: the follow-mode derivation ramps (the `Lift` percentages, `UiaTheme.cs:27-31`), the Scrim/Divider constants (Mechanism B), all metrics (`UiaTheme.cs:81-90`), the fixed 980×660 window (`UiaControlCenter.cs:304`), and the menu font (deliberately independent of the HUD font so a broken profile can't take the menu down with it — `UiaTheme.cs:94-106`, a good call).

A dedicated F10 editor would add a third theming surface (F9 element popups, the menu popup, and it) for a window whose entire job is *settings*, to solve a problem that is actually two wrong hex values in a shipped XML plus one wrong color-source constant. The existing architecture — one resolver (`UiaTheme`), one change signal (`StyleHash`), one editor surface (the F9 popup) — is the right shape. Finish it; don't fork it.

### 4. Polish inventory — what a first-time user hits

1. **The Pure HUD mismatch itself** (§2) — front-door visible: Pure HUD is one of only two shipped cards on the first tab (`ProfilesTab.cs:22-23`).
2. **Stale label "Open all radial settings (legacy F10 panel)"** (`RadialTab.cs:99`): F10 *is* the Control Center now (`StationeersUIMod.cs:372-374`); the button opens the old ImGui `SettingsWindow` (`RadialTab.cs:102-108`, `StationeersUIMod.cs:926-931`). A new user reads "F10 panel", presses F10, and lands back here. Also an honesty gap: radial *colors* are only editable in that legacy window — the one part of the mod's look the Control Center + F9 pair doesn't cover.
3. **Dropdown popups can run off-screen**: `UiaDropdown.OpenPopup` positions the list under the field with no screen clamping (`UiaControls.cs:214-230`); a 10-row list (`UiaControls.cs:223`) opened from a low row (e.g. Storage's "By slot class", `StorageTab.cs:452-454`) extends past the window/screen bottom.
4. **Advanced curvature dropdown exposes experimental modes** — "Dome (B)", "World canvas (C)", "Curved RT (D)" (`HudTab.cs:55-56`) — with no hint which are production-quality; the Simple dropdown correctly offers only Flat/VertexWarp (`HudTab.cs:30-33`).
5. **Master switches go stale**: `MasterSwitch` captures `get()` at build (`UiaControlCenter.cs:369-397`); if a config value changes from outside (legacy window, F9), the F10 switch shows the old state until a tab rebuild.
6. **Theme restyle resets transient tab state**: a restyle recreates tab instances, so `StorageTab._selected` (the profile being edited) silently jumps back to the first profile mid-color-drag (`UiaControlCenter.cs:274-282` rebuild path; `StorageTab.cs:23-29` acknowledges this for `_testPrefab`). Cosmetic-adjacent, only bites while actively theming.
7. **Chrome inconsistency in the popups**: the item picker and rebind prompt use the flat rounded-Image + Outline look (`UiaItemPicker.cs:84-86`, `UiaRebindCapture.cs:84-85`), not the `PanelGraphic` glass the main window and buttons carry — visible as a style break when the picker opens over the glass window.
8. **No window drag/resize** — fixed centered 980×660 (`UiaControlCenter.cs:302-304`). Acceptable for a settings window; listed for completeness.
9. Good marks worth keeping: scroll position preserved across refresh/restyle (`UiaControlCenter.cs:448-495`), dropdown click-catcher leak fixed (`UiaControls.cs:250-259`), tab build failures are caught and logged instead of killing the window (`UiaControlCenter.cs:467-468`), first-run guide auto-open (`StationeersUIMod.cs:362-367`), ASCII-only glyphs throughout ("X", "v" — `UiaControlCenter.cs:353`, `UiaControls.cs:387`), and `Shutdown` resets every static including the popup layer, image cache, and font (`UiaControlCenter.cs:497-524`).

## Severity — how bad is it really

- **The theme mismatch is real, shipped-theme-visible, and cheap to fix.** It is not an architecture failure — the follow/restyle machinery is genuinely good — it is *content* (two stale palette entries in `Pure HUD.xml`) plus *one wrong constant* (accent sourced from `LineAccent` instead of the border color that actually defines the HUD's look). Because Pure HUD is one of the two first-screen cards, a curious first-day user will see it. That earns "needs work before release", but the work is measured in hours.
- **The stale "legacy F10 panel" label** is a two-minute fix that removes a genuine first-session confusion.
- **Everything else in the polish list is "ugly but harmless"**: off-screen dropdowns and stale master switches are annoyances with easy workarounds; the experimental curvature modes only exist behind Advanced; the restyle state reset only occurs while actively dragging a color wheel with F9+F10 both open.
- **"Needs its own editor" is a misdiagnosis** of the Pure HUD symptom. The editor already exists (the F9 click-to-edit popup) and reaches everything a user should reasonably retheme. Building another one before release would be pure schedule risk.

## Recommendations

1. **P0 (S) — Fix the Pure HUD theme data.** In `HudProfiles/Pure HUD.xml`, set `pal:HudLineAccent` to the theme's green (match `pal:HudPanelBorder`, `33CE00E3`, at LineAccent's original alpha), and retint `pal:HudTextDim` off the default blue-gray toward a dimmed green/neutral. Re-run the game once so `HudProfileStore.SyncShipped`'s canonical hash refreshes untouched installs (`Features/HudProfileStore.cs:74-108`); copy the same fix to `dist/StationeersUIMod/HudProfiles/Pure HUD.xml` (the dist staging goes stale — known packaging trap). Sanity-check every other `pal:` entry in the file against the green identity while in there.
2. **P0 (S) — Source the menu's structural border from `HudPalette.PanelBorder`.** Add `UiaTheme.Border` (follow: `Opaque(HudPalette.PanelBorder.Value)`; override: a new 19th `Ovr("Border", …)` in `UiaMenuTheme._ov`) and use it for the window glass border (`UiaControlCenter.cs:92`) and the `UiaGlassSkin` edge (`UiaGlassSkin.cs:50`) — making the "matches a HUD box" comment true. Keep `Accent` (LineAccent) for line-work accents: header underlines, slider fills, Primary buttons — that mirrors exactly how the HUD itself splits the two roles. Add the new getter to `StyleHash()` (`UiaMenuTheme.cs:99-125`) so the restyle poll fires on border-only theme edits, and to `DescribeProps` so click-to-edit reaches it.
3. **P1 (S) — Derive Divider (and optionally Scrim) from the theme.** Divider: text color at ~8% alpha instead of fixed white (`UiaTheme.cs:32`) — on a light theme the current white hairlines vanish. Scrim can stay a dark constant; it reads as dimming, not chrome.
4. **P1 (S) — Rename the legacy hand-off.** `RadialTab.cs:99` → "Open the full radial color panel (legacy)". While there, decide the legacy window's release story (it is now the only home of radial colors — cross-references audit area on the legacy ImGui surfaces).
5. **P1 (S) — Label the experimental curvature modes** in the Advanced dropdown (`HudTab.cs:55-56`): suffix " (experimental)" on Dome/World canvas/Curved RT, or drop them from the menu and leave them config-only.
6. **P2 (S) — Clamp dropdown popups to the screen** in `UiaDropdown.OpenPopup` (`UiaControls.cs:214-230`): if the list would cross the bottom edge, open it upward (pivot 0,0 at the field's top-left).
7. **P2 (S) — Re-read master-switch state on a signal.** Cheapest: fold both config bools into `StyleHash()` so the existing poll repaints them; or repaint in `Update` directly (`UiaControlCenter.cs:369-397`).
8. **P2 (M) — Unify popup chrome**: give the item picker and rebind prompt the same `PanelGraphic` + `HudGlobalGlass` treatment as the main window (`UiaItemPicker.cs:79-89`, `UiaRebindCapture.cs:79-85`).
9. **P2 (S) — Preserve `StorageTab._selected` across restyles** (promote to a static beside the existing scratch caches, `StorageTab.cs:38-41`).

Finish line: items 1+2 close FlorpyDorp's reported bug outright; 3-5 are the rest of the "needs polish" that a first-time user can hit; 6-9 are quality-of-life that can ride a post-release patch. Total pre-release effort: roughly one day.

## What NOT to do

- **Do not build a dedicated F10 editor.** The F9 click-to-edit popup + the override system already covers user-facing theming; recommendation 2 closes the one real derivation gap. A second editor doubles the surface to maintain for zero new capability.
- **Do not convert the menu into HUD document elements** (making F10 a `HudDocument` so F9 can drag its parts). The menu is a settings dialog, not HUD; per-element authoring of it adds schema/migration weight and new failure modes (a profile that breaks its own settings window).
- **Do not replace the poll-and-rebuild restyle with per-widget reactive bindings.** The throttled `StyleHash` rebuild (`UiaControlCenter.cs:164-170`) is simple, allocation-bounded, and already preserves scroll; a reactive system is a large refactor to remove a ~7/s rebuild nobody perceives.
- **Do not "smart-derive" the menu accent (e.g. auto-picking the most saturated palette entry).** Heuristics will mismatch some future theme in a new way; explicit sourcing (Border ← PanelBorder, Accent ← LineAccent) plus correct shipped theme data is predictable and debuggable.
- **Do not floor-remove the window alpha to honor Pure HUD's transparent fill.** The 0.85 floor (`UiaTheme.cs:26`) is what keeps the settings readable over a bright scene; a see-through settings window is a worse bug than a color mismatch.
