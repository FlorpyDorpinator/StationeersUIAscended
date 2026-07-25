# 04 — Theming Architecture and the Profile Flow

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** "UI elements have this patchwork of effects that changed over and over between global and per-element… the profile flow only lets us Duplicate As — it needs a blank-slate 'New Theme' button too. So many colors and effects it's overwhelming; some colors seem redundant; there should be a better way to display it all."

**Verdict:** NEEDS WORK BEFORE RELEASE — the *rendering* architecture is in far better shape than it feels (the 2026-07-16/07-19 migrations really did kill the patchwork at the resolver level), but the *editing surface* exposes roughly 470 knobs across six differently-scoped theme systems with no layered presentation, and the profile flow is missing New / Rename / Delete / Revert. Those are UI affordance gaps, not architecture rewrites, and they're the difference between "workshop reviewers praise the theming" and "workshop reviewers bounce off it."

---

## Findings

### 1. The complete inventory — where every themable knob lives

There are **six theme sources** plus one hardcoded constant table. Counted from the live code:

| Layer | File | Knob count | Scope | Travels with a profile? |
|---|---|---|---|---|
| **HudConfig** globals (curvature, sizing, glass, Tier A/B/C effects, transitions, bloom, frost, alert pulse) | `Assets/Scripts/StationeersUIMod/UI/Hud/HudConfig.cs:37-244` (bind: 306-875) | ~160 entries | global | YES (minus a 14-key denylist, `UI/Hud/HudTheme.cs:31-36`) |
| **HudPalette** (named HUD colours) | `UI/Hud/HudPalette.cs:162-216` | 22 entries | global | YES (`pal:` keys, `UI/Hud/HudTheme.cs:64-65`) |
| **RadialPalette** (named radial colours) | `Overlay/RadialPalette.cs:93-158` | 23 entries | global | YES (`rad:` keys, `UI/Hud/HudTheme.cs:66-67`) |
| **UIAConfig** radial/hint-bar/Grid *style* knobs (sections "8. Radial Visuals", "10. Hint Bar", "9. The Grid" sizes) | `Assets/Scripts/StationeersUIMod/UIAConfig.cs:250-307, 171-233` | ~45 of its 133 entries are visual | global | **NO** |
| **GridTheme** (Universal Inventory override block) | `UI/Grid/GridTheme.cs:87-133` | 42 entries | global | **NO** |
| **UiaMenuTheme** (Control Center override block) | `UI/Menu/Kit/UiaMenuTheme.cs:43-63` | 19 entries | global | **NO** |
| **Per-element** style (ColorRefs, Custom snapshot keys, bare `b_` forks, 7 transition tri-states, widget colour params) | `UI/Hud/HudDocument.cs:311-331`, `UI/Hud/HudElementView.cs:932-1330` | 3 first-class ColorRefs + ~40 Custom keys + 7×2 transition keys per element; ~27 widget-specific colour props across widget types | per element | YES (it *is* the profile) |
| **Overlay.Theme** hardcoded constants (ImGui overlays, toasts, radial fallback colours) | `Overlay/Theme.cs:16-72` | ~30 constants | compile-time | n/a — not editable at all |

Ballpark total of *user-facing* colour/effect knobs: **~470**. That is the overwhelm, quantified. Nobody restructured this wrongly — it accreted, one play-test round at a time (the file comments are honest about this), and each individual addition was justified. But the sum has no layering in its presentation: F9's Theme tab dumps all 22 palette wheels in one scroll (`Windows/HudEditorWindow.cs:1674-1692`), the Effects tab is nine collapsing headers deep (`Windows/HudEditorWindow.cs:267-553`), and a Custom element's popup deliberately mirrors the entire Effects tab ("PARITY CONTRACT", `UI/Hud/HudElementView.cs:1093-1099`).

**Config-file section numbering is also colliding** in the shipped .cfg: RadialPalette binds "9. Radial Colours" while UIAConfig binds "9. The Grid"; UIAConfig "10. Hint Bar" sits beside HudConfig "10. Visor HUD"; HudConfig "11. HUD Effects (0.9.0)" beside HudPalette "11. HUD Colours"; GridTheme is "13. The Grid" — a *second* Grid section with a different number than UIAConfig's Grid sizes. Harmless to code, confusing to anyone who opens the cfg (which profile-sharing users will).

### 2. Precedence — the patchwork, verified

The good news first, because it matters for what NOT to do: **the resolver-level patchwork is gone.** The concern describes the 0.8-era reality — the "legacy mixed" state (followGlobal flag + `-1` sentinels + `fx*` multiplier keys) — and that state was regressed out of existence by `HudStyleMigration` (`UI/Hud/HudStyleMigration.cs:6-30`), which rewrites every loaded profile into a **coherent two-state contract**:

- **Global** (`styleSource=1`): the element reads the F9 globals raw — every global slider drives it 1:1 (`UI/Hud/HudElementView.cs:151-170`).
- **Custom** (`styleSource=2`): a *complete* snapshot frozen from the effective look at the moment of separation (`UI/Hud/HudElementView.cs:1523-1530`), so unchecking "Follow global" never jumps a pixel.
- **Transitions** (collapse/TV-off/dissolve/flicker/glitch/warp/pulse) are a deliberately independent third axis: per-element tri-state Inherit/On/Off against a global master + strength (`UI/Hud/HudTransitionFx.cs:121-156`, migrated by `UI/Hud/HudDocument.cs:216-233`).
- **Colours** are one path everywhere: element ColorRef → `HudPalette.Resolve` (palette name live-tracks, `#hex` stands alone, empty falls back — `UI/Hud/HudPalette.cs:102-118`). GridTheme, UiaMenuTheme and widget colour params all reuse the same resolver. This part is genuinely clean.

What *remains* patchwork — the honest list:

1. **Four different fallback conventions coexist inside a Custom element.** `OwnOrGlobal` (`-1`/missing → global, `UI/Hud/HudElementView.cs:544-549`), `NewSdfOwnOrGlobal` (missing → *neutral default*, deliberately NOT the global, for keys added after snapshots shipped — `:566-573`), `StyleFeatureOn` (bool default = the *current* global value — `:551-555`), and the squircle's special "`< 2` = inherit" band (`:687-693`; `rippleSmooth` additionally has no global at all, `:528`). Each convention is individually correct and comment-justified, but a contributor (or you in six months) must know which of four rules a given key follows. This is invisible to users; it is a maintenance tax, not a player-facing bug.
2. **Six theme sources have five different "follow" semantics.** Elements: two-state + tri-state. Grid: one Follow master + 42 `-1`-sentinel/tri-state overrides that re-assert *over* `HudGlobalGlass.Apply`'s output every frame (`UI/Grid/GridTheme.cs:614-635`). Menu: one Follow master + 18 flat colour overrides with *derivation ramps* (fill lifted by 5-24% per surface — `UI/Menu/Kit/UiaTheme.cs:25-41`). Radials: palette colours only, geometry/effects in UIAConfig with no follow concept. Hint bar: its own 14-knob block (`UIAConfig.cs:273-286`) plus 3 palette entries (`Overlay/RadialPalette.cs:153-158`). Same *idea* five ways.
3. **The profile-theme boundary is drawn at "HudConfig + two palettes" and nothing else.** `HudTheme.Snapshot` reflects only `HudConfig`'s fields plus the two palettes (`UI/Hud/HudTheme.cs:52-68`). So switching profiles retints a *following* Grid/menu (through the shared palette) but silently does NOT carry: Grid overrides, menu overrides, radial geometry/sheen/frost (`UIAConfig.cs:250-307`), or hint-bar styling. A shared "green terminal" profile will arrive with blue radial *shapes*, the author's Grid overrides missing, etc. Users will read this as a bug ("the theme didn't fully apply").
4. **Known bypasses are documented but real**: hand boxes / equipment column style sheen/spec by hand and re-enter through `ApplyMeshFx`/`ApplyFx` (`UI/Hud/HudElementView.cs:489-496, 626-630`); the alert pulse floors the glow over per-element values by design (`:504-517`); the legacy fixed panel set (`TopStatusBar.cs`, `CompassRibbon.cs` etc.) reads its own 20+ HudConfig knobs and the palette directly.
5. **Overlay.Theme constants are a fourth colour system nothing can retheme** — still read by 18 files including live UGUI surfaces (`Overlay/Toast.cs`, `UI/Menu/Kit/UiaControls.cs`, `Features/ItemMenuBuilder.cs`). A player who themes everything orange still gets hardcoded teal/cyan toasts.

**Verdict on the "patchwork" feeling:** it is 20% real renderer inconsistency (points 4-5), 80% *presentation* — the layers exist and mostly compose correctly, but the UI shows all ~470 leaves of the tree instead of the trunk.

### 3. Redundant / dead knobs — the deletion candidates

Concretely verified, not vibes:

| Knob(s) | Why redundant | Citation |
|---|---|---|
| `HudSlotNumber` palette entry | Only reader is the **legacy** fixed equipment column (`UI/Hud/EquipmentColumn.cs:106`). The shipped document widget defaults its slot numbers to `HudTextDim` instead (`UI/Hud/Widgets/EquipmentColumnWidget.cs:148`). On the default (document) HUD this wheel does nothing. | `UI/Hud/HudPalette.cs:201` |
| `HudAlertCaution` / `HudAlertCritical` | Same RGB as `HudWarn` / `HudCritical` (FFB13D / FF4A3D), differing only in alpha. The separation is deliberate and documented ("retinting readout text never silently retints the alarm", `UI/Hud/HudPalette.cs:63-66`) — keep the *capability*, but they don't deserve equal billing with the core palette. | `UI/Hud/HudPalette.cs:183-186` |
| Radial `TextPrimary` / `TextDim` / `TextAccent` | All three default `FFFFFFFF` — "TextDim" is not dim; three wheels, one value. | `Overlay/RadialPalette.cs:136-141` |
| `GroupWedgeFill` | Defaults to the exact hex of `WedgeBackground` (0E213373). | `Overlay/RadialPalette.cs:114-115` |
| The legacy glitch family: `GlitchEnabled/Duration/Intensity/OnPowerDown/OnPowerUp` (5 knobs) vs. the transition-registry `FxGlitchOn/FxGlitchAmt` | **Two live glitch systems.** HudGlitch's transform-jitter reads the first family (`UI/Hud/HudGlitch.cs:36-54`); the tri-state transition tear reads the second (`UI/Hud/HudConfig.cs:162-163, 719-723`). F9 exposes both ("Power-transition tear / shake" at `Windows/HudEditorWindow.cs:602-605` AND "Glitch tear" in the Effects registry), and F10's HudTab exposes the legacy one (`UI/Menu/Tabs/HudTab.cs:53`). A user who turns "glitch" off in one place and still sees the other will file it as the exact bug the tri-state refactor already fixed once. | — |
| Legacy fixed-panel surface: `ShowTopBar/ShowCompass/ShowEquipment/ShowHands/ShowVitals/ShowVignette/ShowHologram` + 11 size knobs + 5 legacy font-size knobs (~23 knobs) | Only consumed by the `UseDocumentHud=false` fallback panel set (e.g. `UI/Hud/TopStatusBar.cs:132,178`), which no shipped profile uses. They are still bound, still captured into every profile's theme snapshot, and still occupy config-file space. | `UI/Hud/HudConfig.cs:56-83, 96-103` |
| `HudProfileStore.Duplicate(from,to)` and `HudProfileStore.Delete(name)` | **Zero callers** — both UIs that duplicate do it inline (`Windows/HudEditorWindow.cs:809-831`, `UI/Menu/Tabs/ProfilesTab.cs:163-173`), and nothing calls Delete at all. Dead API. | `Features/HudProfileStore.cs:335-362` |
| GridTheme `FillRef/BorderRef/TextRef` defaults | Default to the same palette names the Follow path resolves — the override state is a byte-identical no-op until edited (by design, but it means 3 of the 42 knobs ship doing nothing). | `UI/Grid/GridTheme.cs:147-155` |

Also note the **theme snapshot captures performance knobs**: `FrostDownsample`, `FrostUpdateEveryNFrames`, `FxBloomRes`, `FxBloomBlurSteps` all travel in a shared profile (`UI/Hud/HudTheme.cs:52-62` reflects everything not in the denylist). A profile authored on a strong GPU will drag its frost/bloom cost settings onto a weak one.

### 4. The profile flow — the "Duplicate As only" claim, confirmed

The claim is accurate. The complete affordance audit:

| Affordance | F9 Designer | F10 Control Center | Exists in the store API? |
|---|---|---|---|
| Switch profile | combo (`Windows/HudEditorWindow.cs:775-796`) | cards + dropdown (`UI/Menu/Tabs/ProfilesTab.cs:47-55`) | `LoadActive` ✔ |
| Duplicate | "Duplicate as" + name field (`Windows/HudEditorWindow.cs:804-831`) | "Duplicate active" (auto-names " copy") (`UI/Menu/Tabs/ProfilesTab.cs:163-173`) | inline (the store's own `Duplicate` is uncalled) |
| **New (blank slate)** | **none** | **none** | none |
| **Rename** | **none** | **none** | none |
| **Delete** | **none** | **none** | `Delete` exists, uncalled (`Features/HudProfileStore.cs:335-352`) |
| **Revert shipped profile to shipped state** | **none** | **none** | none (SyncShipped only refreshes *untouched* files at launch — `Features/HudProfileStore.cs:55-143`; the moment a player edits "Stationeers Blue" it becomes theirs forever, with no way back but deleting the file by hand) |
| Open folder | ✔ (`:798-802`) | ✔ (`:84`) | — |
| Save theme into profile / clear theme | ✔ (`Windows/HudEditorWindow.cs:1643-1648`) | none | `CaptureThemeNow`/`ClearActiveTheme` ✔ |

So the only creation path is copy-the-current-document, and the only deletion/rename path is the file explorer. For a mod whose headline feature is "build your own HUD," that's the single most visible workflow gap.

**One subtle semantics wrinkle worth knowing before release:** any global edit made through F9's wrapped controls calls `MarkThemeChanged` (`Windows/HudEditorWindow.cs:1765-1775`), and the next autosave stamps a **full theme snapshot** into the active profile (`Features/HudProfileStore.cs:448`). So a previously *themeless* profile ("follows the live globals" — the F9 header text at `Windows/HudEditorWindow.cs:1640-1642` does explain this) silently acquires a frozen copy of *all* ~205 globals the first time you nudge any slider. That's the designed fix for "recolouring one profile recoloured them all," and the fix is correct — but combined with a blank-slate New button it means the New flow must decide the theme question explicitly (see Recommendations).

**What a blank slate should contain** (design proposal): NOT a fully empty document — an empty visor HUD looks like a broken mod, and the design philosophy makes the two hand boxes non-negotiable. Seed:
- `RefW/RefH` = current screen (the shareability contract, `UI/Hud/HudDocument.cs:93-102`),
- exactly one `HandBoxes` element, All-tier, follow-global, centered bottom (the philosophy's floor),
- `Theme = null` (themeless: the new profile inherits whatever look the player currently has, which is the least surprising start),
- nothing else. Everything is one "Add element" away in the same window, and the empty-but-alive HUD invites building rather than reading as failure.

### 5. GridTheme vs HudTheme — should the Grid fold into the profile?

Current truth: the Grid deliberately is not a HUD document element, and its own header comment concedes the gap — "'Follows the profile' therefore holds only insofar as switching profiles changes those globals" (`UI/Grid/GridTheme.cs:20-25`). Concretely: a *following* Grid retints on profile switch (palette + glass globals travel in the theme snapshot); a Grid with any override keeps that override across every profile, forever, because GridTheme's 42 entries live outside `HudTheme.Snapshot`'s reflection target (`UI/Hud/HudTheme.cs:44` reflects `typeof(HudConfig)` only).

**Yes, it should fold in — and it's cheap.** The snapshot/apply mechanism is already string-keyed and version-tolerant ("a key absent from the snapshot leaves that setting untouched" — `UI/Hud/HudTheme.cs:22-24`), so extending `Snapshot()`/`Apply()` to also reflect `GridTheme`'s public static `ConfigEntryBase` fields under a `grid:` prefix (and `UiaMenuTheme` under `menu:`) is ~30 lines, fully backward compatible: old profiles simply lack the keys and change nothing. The radial *style* block is the only awkward one — those ~30 knobs live inside `UIAConfig` mixed with behavior keys (`UIAConfig.cs:250-307`), so they need an explicit include-list rather than reflection (or a later move into their own class). The one real decision is philosophical, not technical: after unification, a player's Grid overrides become per-profile, and their existing global overrides should be migrated into the active profile's snapshot once (a `ConfigMigration` step — the uncommitted `Core/ConfigMigration.cs` scaffolding is exactly the right home for it).

---

## Severity — how bad is it really

- **The renderer architecture: healthy.** Two coherent states, one colour resolver, a real migration story with idempotent repairs and canonical-hash shipped-theme sync. This is *better* engineering than most shipped games' theming. Do not let the overwhelm feeling talk you into rearchitecting it.
- **The knob count: ugly and it will bite, but indirectly.** No knob is broken; the harm is first-session paralysis and review-video mockery ("there are nine glow sliders"). It suppresses the feature's reception rather than causing bugs.
- **The missing New/Delete/Rename/Revert: will bite users directly.** First thing a workshop user does is try to make their own theme; the current answer is "duplicate the default and mutate it," and the only undo for a wrecked shipped profile is the file system. This is the highest ROI fix in this report.
- **The theme boundary (Grid/radial-style/menu not traveling): will bite profile SHARING.** Solo it's invisible; the moment profiles circulate on the workshop, "the theme half-applied" reports start. Worth closing at least for the Grid before profiles become a community currency.
- **Two glitch systems / legacy panel knobs / dead palette entries: harmless clutter** — but cheap deletions that directly shrink the overwhelm, and deleting *after* release is harder (players' configs will carry values).
- **The four Custom fallback conventions: zero player impact,** pure future-maintainer tax. Document it, don't churn it pre-release.

Roughly: **half the overwhelm is deletable/hideable in days; the structural half (theme boundary, presentation regrouping) is a focused week.**

---

## Recommendations

1. **(P0, S) Add "New profile" (blank slate) beside Duplicate in both UIs.** Store side: a `NewProfile(name)` in `Features/HudProfileStore.cs` seeding the minimal document from §4 (RefW/RefH = current screen, one follow-global HandBoxes element, Theme = null); UI side: a name field + button in `Windows/HudEditorWindow.cs:804` and a "+ New" card in `UI/Menu/Tabs/ProfilesTab.cs`. Keep Duplicate exactly as is — the ask was explicitly both.
2. **(P0, S) Add Delete and Rename to the profile UI.** `Delete` already exists uncalled (`Features/HudProfileStore.cs:335`); Rename is `Duplicate` + `Delete` + retarget `HudActiveProfile` + move the `.png` preview. Guard rails: refuse deleting the active profile (or auto-fall back to the shipped default, the pattern `SyncShipped` already uses at `:123-124`), and confirm before deleting anything in the shipped manifest. ASCII-only labels in any TMP-rendered UI (the Control Center is TMP — "X" not "✕").
3. **(P0, S) Add "Restore shipped version" for shipped profiles.** The mod folder still holds the pristine source (`SyncShipped`'s `src`, `Features/HudProfileStore.cs:60`); a button that re-copies it over the player's edited copy (with confirm) closes the only unrecoverable state in the flow. Surface it on the profile card when the name is in `.shipped-manifest`.
4. **(P1, M) Palette-first presentation: split the Theme tab into "Palette" (8 core wheels) and "Advanced colours."** The code already knows the core set — `_boxColourNames` drives the GLOBAL BOX COLOURS section (`Windows/HudEditorWindow.cs:1675-1692`). Promote that to the whole story: core = PanelFill, PanelBorder, LineAccent, TextValue, TextLabel, TextDim, Good/Warn/Critical (one row), and collapse *everything else* (compass trio, alert pair, hologram, vignette, scanline, icon tint) under a default-closed "Advanced" header with one-line role descriptions. Same treatment in the Effects tab: a top "Masters" strip (Tier A/B/C + Glow + Edge energy + Transitions on/off + ~5 master strengths), all nine existing headers default-*closed* below it. Pure ImGui reshuffling — no schema, no resolver changes. The existing hover-to-highlight palette linking (`Windows/HudEditorWindow.cs:1705-1714`) is already the best discoverability feature in the editor; keep it and mention it in the UI.
5. **(P1, S) Retire the legacy glitch family from the UI.** Keep `HudGlitch` if you like the transform jitter, but drive it from the tri-state registry's `FxGlitchOn/FxGlitchAmt` and stop binding/showing the five `Glitch*` knobs (`UI/Hud/HudConfig.cs:114-118` already documents the precedent: the GlitchShader knob was retired the same way). Remove the duplicate exposure at `UI/Menu/Tabs/HudTab.cs:53` and `Windows/HudEditorWindow.cs:602-605`. One glitch concept, one switch.
6. **(P1, S) Prune the dead colour knobs while configs are still young.** Delete the `HudSlotNumber` palette entry (or make the widget default to it — either way, end the lie), fold radial `TextAccent` into `TextPrimary`, and move `AlertCaution`/`AlertCritical` + `Vignette` + `Scanline` + `HologramTint` into the Advanced group from rec 4. Use the new `Core/ConfigMigration.cs` step mechanism for any key removal so stranded values don't linger. Post-release this gets strictly harder.
7. **(P1, M) Fold GridTheme (and UiaMenuTheme) into the profile theme snapshot.** Extend `HudTheme.Snapshot/Apply` to reflect `GridTheme` + `UiaMenuTheme` public static ConfigEntry fields under `grid:`/`menu:` prefixes (`UI/Hud/HudTheme.cs:52-105`); add one `ConfigMigration` step stamping current overrides into the active profile. Version-tolerant by construction — old profiles lack the keys and change nothing. Defer the radial-style block (UIAConfig include-list) to P2 unless it's trivial in the same pass.
8. **(P1, S) Stop capturing performance knobs into themes.** Add `FrostDownsample`, `FrostUpdateEveryN`, `FxBloomRes`, `FxBloomBlurSteps` (and audit for others) to `HudTheme.Exclude` (`UI/Hud/HudTheme.cs:31-36`) so a shared profile carries a *look*, not a framerate. Also add the ~23 legacy fixed-panel knobs — a theme should not be flipping `ShowTopBar` on a HUD that doesn't render it.
9. **(P2, M) One "follow" vocabulary.** Grid/menu/radial popups already reuse `HudProp` descriptors; converge their inheritance controls on the element popup's patterns (tri-state combos + `-1` sliders with identical labels) and document the four Custom fallback conventions in one comment block in `HudElementView` so the next key added picks one deliberately.
10. **(P2, S) Retire `Overlay.Theme`'s UGUI constants.** Re-point the ~8 live UGUI readers (Toast, UiaControls accents, ItemMenuBuilder) at `HudPalette`/`UiaTheme` getters so no shipped surface is untheme-able; keep the ImGui constants for the legacy overlay until it dies.
11. **(P2, L) A "Simple mode" for theming** — one toggle in F9 that hides everything but: the 8 palette wheels, 5 master effect sliders (glow, edge energy, glass, frost, bloom), curvature choice, and the profile row. Everything else behind "Advanced". Do this after rec 4 ships and you see which knobs players actually touch.

## What NOT to do

- **Do not rewrite the two-state element style contract or the tri-state transitions.** They were each paid for with a real bug ("this element won't react to the global slider", "I turned off death collapse and it still does it") and the migrations that guard them are idempotent and battle-tested. The overwhelm is presentational; the semantics are right.
- **Do not merge the HUD and radial palettes into one.** `HudPalette`'s header names the reason they're separate (`UI/Hud/HudPalette.cs:10-12`): restyling the HUD must not disturb the radials. The theme snapshot already unifies them at the profile level, which is the correct join point.
- **Do not make the Grid a HUD document element** to get per-profile Grid theming. The GridTheme header's reasons stand (modal, raycasting, un-warped, own canvas — `UI/Grid/GridTheme.cs:21-23`); rec 7's snapshot extension gets the user-visible result for 5% of the cost.
- **Do not normalize the four Custom fallback conventions by rewriting stored profiles.** `NewSdfOwnOrGlobal`'s "missing = neutral, not global" exists precisely so new globals don't invade old snapshots; flattening the conventions would re-run the class of bug the 2026-07-16 migration fixed. Document, don't churn.
- **Do not build a theme marketplace/import-export UI pre-release.** Profiles are already single shareable XML files with embedded RefW/RefH and theme — copy-the-file *is* the sharing feature. Ship that story.
- **Do not delete the legacy fixed-panel *code path*** (`UseDocumentHud=false`) in the same pass as its knobs' UI — it's the fail-soft fallback if a game update breaks the document HUD. Hide its knobs from themes (rec 8) and leave the code until the document HUD has a release of public mileage.
