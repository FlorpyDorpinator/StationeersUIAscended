[text](00-Overview-and-Release-Plan.md)# 03 — The F9 Editor: UX Roughness + Universal Knob Parity

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** (a) The F9 build menu works for me but is too rough for users — deprecated features, effects we don't use, and the tabbed remake "feels quite messy again." (b) I want every GLOBAL knob to exist on every element that uses it, so any element can be edited fully — and I'm worried the menus are spaghetti code.

**Verdict:** ACCEPTABLE FOR RELEASE, IMPROVE AFTER — the architecture is *not* spaghetti (one descriptor model, one generic drawer, a registry already enforces transition-knob parity), and per-element knob parity is ~90% real; what's left is a day or two of dead-control pruning, one duplicated tab section, one genuine reset-button bug, and a set of universally-offered knobs that silently do nothing on ~8 element types.

---

## Findings

### 1. The actual structure — better than its reputation

The F9 editor is two files plus one shared property system:

- `Assets/Scripts/StationeersUIMod/Windows/HudEditorWindow.cs` (1,806 lines) — the window: a persistent toolbar (profile combo/save-as, preview-tier combo, Undo/Redo/Exit — `DrawProfilesSection` :770-832, :124-157) over five tabs (`DrawContent` :65-112): **Build / Theme / Effects / View & Behavior / Diagnostics**.
- `Assets/Scripts/StationeersUIMod/Windows/HudEditorMode.cs` (1,793 lines) — the screen state: dim backdrop, hover/click hit-testing through the inverse warp, drag/resize/marquee/point-edit gestures, undo bracketing, and all structural edits (add/duplicate/delete/flatten/bulk ops).
- `Assets/Scripts/StationeersUIMod/UI/Hud/HudProp.cs` + `Windows/HudPropDrawer.cs` — the descriptor model and the ONE generic renderer.

**Tab inventory and what each edits:**

| Tab | Scope | Contents |
|---|---|---|
| Build (`DrawBuildTab` :159, `DrawDesignerSection` :682-768) | PROFILE document | grid snap/show/size; add-element combo (23 `AddableTypes` :667-678); line tool; pen tool; selection actions (duplicate/delete/make-flat/per-curvature-mode placement reset) |
| Theme (`DrawThemeTab` :171-261) | GLOBAL config | Panel surface (scale, scale-with-res + profile resolution stamp, corner/border/feather/sheen/edge defaults, SDF panels ×3, three bulk buttons); **Legacy panel sizes** (12 sliders, legacy mode only :232-246); Typography (font combo + 6 size sliders :248-257); Palette (22 `HudPalette` entries, per-profile theme save/clear, undo/redo, reset :1634-1701) |
| Effects (`DrawEffectsTab` :263-550) | GLOBAL config | Edges/glow/pulse (~35 controls :267-382); Alert pulse (~12 + live preview :386-453); Glass animation (~8 :455-480); **Suit power & transitions** — registry-driven, one row per `HudTransitionFx.All` effect (:487-526); Frosted glass (~8 :528-541); HUD bloom (~28, `DrawBloomControls` :1462-1523); "Reset active per-element effects" button (:547-549) |
| View & Behavior (`DrawViewBehaviorTab` :552-623) | GLOBAL config | HUD renderer on/off + legacy-panel toggles (legacy mode only :562-569) + scanlines/vignette/icon tint; Curvature (~5); **Suit power & transitions AGAIN** (:588-613) — diegetic tiers, dropouts, test buttons, tear/shake glitch; Vanilla panels (4 hide toggles) |
| Diagnostics (`DrawDiagnosticsTab` :625-656) | GLOBAL config | DebugShowAll ×2; renderer status readouts; **LegacyImGuiHud** toggle; profiler |

The tabs are already scoped roughly the way a redesign would want them (document / theme / effects / behavior / debug). The messiness is *within* tabs (the Effects tab is ~90 controls deep with every `CollapsingHeader` DefaultOpen), not the tab decomposition.

### 2. The per-element editing path — centralized, descriptor-driven, NOT hand-written ImGui per widget

Clicking an element in edit mode: `HudEditorMode.UpdateDesigner` resolves the selection by element Id (survives view rebuilds, `HudEditorMode.cs:224-239`), then the plugin draw hook calls `HudEditorWindow.DrawPopupOverlay` (:1131-1307), which:

1. calls `el.DescribeProps(_propScratch)` (:1217) — the element *describes* its knobs as a flat `List<HudProp>` (typed get/set delegates, no ImGui — `UI/Hud/HudProp.cs:39-196`);
2. renders them with the single generic drawer `HudPropDrawer.DrawGroup` per popup tab **Content / Layout / Appearance / Effects / Interaction** (:1245-1257, groups from `HudPropGroup`, `HudProp.cs:26`);
3. brackets undo via begin/commit/cancel callbacks with correct gesture semantics (one drag = one undo step — `HudPropDrawer.cs:33-49`, `BeginContinuous`/`EndContinuous` :385-394), including the tab-switch and nested-tab-page interruption flushes (`DrawElementPropTab` :1353-1372, `DrawTabGroup` `HudPropDrawer.cs:113-142`).

Every widget's props are `base.DescribeProps(into)` + appended extras — all 17 widgets plus `PrimitiveView` follow the pattern (grep: every `DescribeProps` override in `UI/Hud/Widgets/*` starts with the base call). The base contributes the universal set in three blocks: `AddUnifiedLayoutProps` (`HudElementView.cs:960-983`), `AddUnifiedAppearanceProps` (:985-1066), `AddUnifiedEffectProps` (:1068-1354). The same drawer surface is reused for the F10 Control Center theme popup (:1048-1069) and the Universal Inventory style popup (:1075-1094) — three consumers, one renderer.

**This is the opposite of spaghetti.** The concern "they're a bunch of spaghetti code" is wrong about the *drawing* layer. The genuine duplication lives one level down — see finding 5.

### 3. Parity matrix — what each element type actually exposes

25 `HudElementType` values (`UI/Hud/HudDocument.cs:31-45`); `Vignette` is a dead enum entry (the real vignette is a hard-coded overlay, `HudSystem.cs:162-165`; the type renders as a placeholder and is correctly not addable). 24 live types map through `HudSystem.CreateViewFor` (:391-424); `MoodletDashboard` maps to `MoodletBorrowWidget` (the borrowed vanilla strip).

**Universal on EVERY element** (the base blocks): follow-global style toggle; Anchor/X/Y/W/H/W%/H% (per-curvature-mode and per-Suited/Bare fork), Z order, tier mask (Bare/Suited pair, Robot hidden by design `HudPropDrawer.cs:271-292`); Text/accent colour; Font scale; and **all seven transition tri-states** (collapse, TV-off, dissolve, flicker, glitch, warp exemption, pulse — one row per `HudTransitionFx.All` entry, `HudElementView.cs:1346-1353`), each Inherit/On/Off with its own strength. The transitions block is deliberately NOT gated on style source (:1337-1345) and is generated from the same registry the F9 global tab reads (`HudEditorWindow.cs:502-519`) — **these two menus cannot drift by construction**. That is the parity mechanism FlorpyDorp is asking for, already shipped for the motion family.

Capability gates (all in `HudElementView.cs`): panel set = 15-type allow-list (`SupportsPanelAppearanceFor` :212-243); ring-only chrome = Portrait + BodyDoll (:252-254); analytic-SDF extras = panel minus Shape (:272-273); authored corners = analytic minus SuitChips (:321-323); border colour = all minus ActiveHandBadge (:326-327); trapezoid = virtual, overridden by 8 box-bearing views (:206, e.g. `ReadoutWidget.cs:64`).

| Type | Panel set (fill/border/width/feather/sheen/spec) | Corners | SDF extras | Trapezoid | Glow halo | Custom FX mirror | Widget-specific knobs | Inert universal knobs |
|---|---|---|---|---|---|---|---|---|
| Box | Y | Y | Y | Y | Y | Y | 4 per-side borders (`PrimitiveView.cs:305-314`) | Text colour, Font scale (no text) |
| Shape (pen) | Y | contour-derived (hint shown :1040-1042) | mesh only | – | Y (160 px cap :1189) | Y minus SDF rows | curve style/steps, side borders, point editor (`PrimitiveView.cs:358-381`) | Text colour, Font scale |
| Label | – | – | – | – | – | transitions only | text/align/wrap/size (:315-329) | none |
| Polyline | – | – | – | – | Y (line mirror :1302-1317) | Y line family (:1283-1301) | closed/smooth/steps/width/fadeEnds/hairline (:330-352) | Font scale |
| Icon | – | – | – | – | – | transitions only | glyph/PNG name, stroke (:353-357) | Font scale |
| Readout | Y | Y | Y | Y | Y | Y | ~24: source, bar style + 5 bar colour refs, label/target colours, icon, stacked rows (`ReadoutWidget.cs:701-758`) | none |
| Clock / WorldName / DayCounter | – | – | – | – | – | transitions only | size/align (+UTC/date) (`DynamicTextWidget.cs:148-170`) | none |
| ActiveHandBadge | Y (border colour derived, by design) | Y | Y | Y | Y | Y | number toggle | – |
| Compass | Y | Y | Y | Y | Y | Y | needle/tick/cardinal/degrees colours, FOV, spacing (`CompassWidget.cs:230-267`) | Text colour (own refs) |
| MoodletDashboard (borrow) | – (correct: vanilla strip, `HudElementView.cs:235-239`) | – | – | – | – | transitions only | words mode + scale/transparency/brightness (`MoodletBorrowWidget.cs:507-545`) | Text colour; Font scale only in words mode |
| EquipmentColumn | Y | Y | Y | – | Y | Y | 13 + 2 drop-highlight (`EquipmentColumnWidget.cs:281-320`) | Text colour (3 specific refs instead) |
| HandBoxes | Y | Y | Y | – | Y | Y | 11 + 2 drop-highlight (`HandBoxesWidget.cs:220-268`) | none |
| KeybindChips | Y | Y | Y | – | Y | Y | vertical + 8 chip labels (`KeybindChipsWidget.cs:156-172`) | none |
| Portrait | ring colour + width only (:1007-1016) | – | – | – | – | transitions only | holo tint ×3, scanlines, camera ×2 (`PortraitWidget.cs:275-305`) | Font scale |
| BodyDoll | ring colour + width only | – | – | – | – | transitions only | auto-hide (`BodyDollWidget.cs:132-137`) | Text colour, Font scale |
| SuitChips | Y | size-derived (pill hint :1040-1042) | Y | – | Y | Y | internals/vertical/gap (`SuitChipsWidget.cs:97-107`) | Font scale |
| BareSenses | Y (bg default off, hint :278-317) | Y | Y | Y | Y | Y | ~90 rows folded into per-sense nested tabs (`BareSensesWidget.cs:622-727`) | Text colour (wordColor instead) |
| VitalsPanel | Y | Y | Y | Y | Y | Y | 10 (`VitalsPanelWidget.cs:568-595`) | none |
| DamageDoll (borrow) | Y (box frame) | Y | Y | Y | Y | Y | frame + padding (`DamageDollBorrowWidget.cs:242-253`) | Text colour, Font scale |
| JetpackBox | Y | Y | Y | Y | Y | Y | box/value size/icon ×2 (`JetpackBoxWidget.cs:203-218`) | none |
| StateChips | Y | Y | Y | – | Y | Y | 9 incl. words mode + icon tint (`StateChipsWidget.cs:144-176`) | Text colour (wordColor instead) |
| PngDoll | Y | Y | Y | Y | Y | Y | 20: art keys, figure geometry, damage ramp colours (`PngDollWidget.cs:263-304`) | Font scale |
| Vignette | dead enum value — placeholder if hand-authored | | | | | | | |

**Quantified:** 15 of 24 live types carry the full panel/glass/effects set; of the 9 that don't, every absence is *correct by capability* (text-only, borrowed vanilla objects, or a line). Portrait and BodyDoll got the two ring knobs their widgets actually read (:247-254) — a past parity gap, already fixed. Recent widget work has been closing colour gaps element by element (Compass ribbon colours, EquipmentColumn text refs, HandBoxes accent refs, Readout label/target — all tagged as "were locked to the palette; expose" in comments).

**The real parity defect is inverted:** knobs that are OFFERED everywhere but DO nothing on some types. "Text / accent" is inert on ~8 types (Compass, EquipmentColumn, BareSenses, StateChips, MoodletDashboard, Portrait, BodyDoll, DamageDoll — none of those widgets read `TextColorFor`; verified by grep) and "Font scale" is inert on ~8 (Box, Icon, Shape, Portrait, BodyDoll, SuitChips, DamageDoll, PngDoll). A user drags a universal slider, nothing happens, and concludes the editor is broken. That damages trust more than a missing knob would.

Where a knob is *shared* rather than per-element (light angle/colour/rim, animation clocks, the single frost capture, fade-ramp shape), the inspector explicitly says so in place instead of omitting it (the "PARITY CONTRACT" block, `HudElementView.cs:1091-1099`, and e.g. :1119, :1159, :1215). That is the right pattern and should be kept.

### 4. Deprecated / vestigial controls — the actual inventory

Cross-checking `HudConfig` writers against non-editor readers:

1. **The whole legacy fixed-panel HUD** (`UseDocumentHud` off — "the fixed 0.5.0 panel set, kept as a fallback during the transition," `HudConfig.cs:313-316`). It drags along: 12 "Legacy panel sizes" sliders (`HudEditorWindow.cs:232-246`), 6 legacy Show* toggles (:562-569), the whole legacy click-to-config popup path (`HudEditTarget` selection loop `HudEditorMode.cs:122-155` + popup `HudEditorWindow.cs:1309-1335` + `CollectEditTargets` in every legacy panel), and the legacy panel classes themselves (`TopStatusBar.cs`, `CompassRibbon.cs`, `VitalsCard.cs`, `BareSensesPanel.cs`, legacy `HandBoxes.cs`/`EquipmentColumn.cs`). All correctly hidden while document mode is on — but it is a second full HUD renderer shipping in 0.9.2.0.
2. **Typography sliders that are dead in the default (document) mode**: `ValueFontSize` is read only by `TopStatusBar.cs`; `CompassFontSize` only by `CompassRibbon.cs`; `VitalsRowFontSize` only by `VitalsCard.cs`; `BareWordFontSize` only by `BareSensesPanel.cs` (+ a layout-hash term, `HudSystem.cs:1481`). Yet all four render, always, in Theme > Typography (`HudEditorWindow.cs:248-257`). Only `FontName`, `FontScale`, and (partially — hand boxes only, `HandBoxesWidget.cs:120`) `LabelFontSize` do anything on the document HUD. Four of six sliders in a DefaultOpen section are no-ops for every shipping user.
3. **`LegacyImGuiHud`** — the 0.1.0 ImGui overlay, still a Diagnostics toggle (:642) and a config entry (`HudConfig.cs:336-337`).
4. **`MoodletDashboardWidget.cs`** — 472 lines of dead code including its own 12-prop `DescribeProps` (:449-471); the registry never instantiates it (`HudSystem.cs:396-399`, no other `new MoodletDashboardWidget` anywhere).
5. **`FxBloomFineDetail`** — legacy knob superseded by `FxBloomRes` (-1 sentinel = follow it, `HudConfig.cs:822-825`); correctly not drawn in F9 (only resolved for display, `HudEditorWindow.cs:1548-1553`), but still bound.
6. **Stale config text**: `GlitchEnabled`'s description still claims it uses "one of the game's own CameraFilterPack shaders" (`HudConfig.cs:489-493`) — the CameraFilterPack approach was retired (the comment at :110-113 says so); `HudGlitch` is a transform-jitter. Cosmetic, but it ships in every user's cfg file.
7. **Duplicated section**: "Suit power & transitions" is a `CollapsingHeader` on BOTH the Effects tab (:487) and the View & Behavior tab (:588), each containing half the story and a TextDisabled pointer at the other half (:592-593, :375). A first-time user cannot tell which one is authoritative.
8. **Deliberate, fine duplication**: the two alert palette colours appear in the Effects tab and the Palette list (same entries, PushID'd, documented :402-419); the F10 Control Center HUD tab (`UI/Menu/Tabs/HudTab.cs`) duplicates ~15 comfort knobs as a simple surface with an "Open the HUD Designer (F9)" button — that is a sensible simple/advanced split, not rot.

### 5. Where the spaghetti actually is: the same ~30 effect keys hand-listed in five places

The one genuine maintenance hazard: every steady-state effect knob (`glow`, `glowIn`, `glowWidth`, `glowDiffuse`, `glowHaze`, `ripple`, `rippleFreq`, `edgeFlow`, `bfade`, `softEdge`, `frostDepth`, `customShine*`, `customIrid*`, `customChroma*`, `customFrost*`, …) is enumerated by hand in:

1. the F9 global Effects tab (`HudEditorWindow.cs:263-550`);
2. the per-element Custom mirror (`HudElementView.AddUnifiedEffectProps` :1091-1273 — the "PARITY CONTRACT" comment openly states it mirrors the tab "section-for-section, label-for-label and range-for-range" *by hand*);
3. `SeedCustomStyleFromEffective` (:1424-1518 — the Global→Custom snapshot);
4. `SetUnifiedStyleSourceWithoutView` (:1541-1688 — the same snapshot again, view-less, with legacy-era semantics);
5. `ResetAllElementEffects` (`HudEditorMode.cs:1292-1396`).

Adding one effect knob today means 5 coordinated edits (plus the renderer accessor and the shader plumbing). The codebase already contains the cure and proves it works: `HudTransitionFx.All` (`UI/Hud/HudTransitionFx.cs:119-160`) is a 7-entry registry that drives the F9 global rows, the per-element rows, the resolver, the migration, and the reset — and the reset comment (:1315-1320) records that the *hand-written list* version of that button had already silently rotted once. The transitions family cannot drift; the glow/glass/frost family can and will.

Special-case count in the base inspector: 7 capability axes, of which 4 are type-switch lists inside `HudElementView` rather than widget-declared virtuals (`SupportsPanelAppearanceFor` :215-242, `SupportsBorderOnlyChrome` :252-254, `OptionalPanelBackgroundIsOff` :278-299, `OptionalPanelBackgroundHint` :304-317) — a new widget must edit the base class in up to 4 places to be fully styleable. Plus 2 inline type checks (Shape at :1126/:1189, Polyline at :1280).

### 6. One real bug found while auditing: "Reset active per-element effects" re-pins transitions

`ResetAllElementEffects` first clears every registry effect's tri-state mode, strength, AND legacy mirror key for every element (`HudEditorMode.cs:1321-1331` — the comment says this "returns all seven effects to Inherit, which is exactly what reset should mean"). But the Custom branch then **re-writes the legacy keys** (:1384-1392): `customDissolve = FxDissolveBoot.Value`, `fxCollapse=true`, `fxGlitch=true`, `fxWarp=true`, `fxPulse=false`, plus stray `fx*Amt=1`. Because `HudTransitionFx.ModeOf`'s legacy fallback (`HudTransitionFx.cs:182-185`) maps a stored `customDissolve=false` (legacy-default-true effect) to an explicit **Off**, pressing this button while the dissolve master happens to be off pins every Custom element's dissolve to Off permanently — the exact conflation bug the tri-state refactor removed from the seeding path (see the long comment at `HudElementView.cs:1500-1508` explaining why motion must NOT be snapshotted). Lines 1384-1392 are a leftover from before that refactor and contradict the cleanup loop 60 lines above them.

### 7. UX assessment of the current editor

What works: the toolbar (profile/undo/preview) is persistent and clear; scope labels ("PROFILE —", "GLOBAL —") head every tab; shared-vs-per-element boundaries are stated in place; the popup's 5 sub-tabs plus nested sense tabs keep even BareSenses' ~90 rows navigable; masters that are globally off announce themselves ("Tier A is off — values are inactive", :1104, :1232); SDF-unavailable states are called out (:216-218, :361-370).

What's rough for a first-time user: (a) the Effects tab is ~90 controls with every header DefaultOpen — a wall; (b) advanced SDF-only esoterica (haze, uneven/organic reach, flow aura, breath, sat-bias ×2) sits at the same visual rank as "Glow halo on/off"; (c) the duplicated Suit-power section (finding 4.7); (d) dead typography sliders (4.2); (e) inert universal knobs (finding 3); (f) label jargon: "Tier A/B/C", "ABI-2 SDF bundle", "analytic SDF glass panels" leak implementation vocabulary into user UI.

---

## Severity — how bad is it really

- **Architecture (the stated worry): healthy.** One descriptor model, one drawer, base-plus-override composition, a registry for the newest knob family, correct undo bracketing throughout. Nobody needs to rewrite this. The "remake with tabs feels messy" impression comes from *content volume and dead weight*, not structure.
- **Parity: ~90% done, and the remaining direction is inverted.** Missing knobs are rare and mostly correct-by-capability; the real problem is universal knobs that are no-ops on ~8 types each. Ugly-and-will-mislead-users, but not data-destroying.
- **Dead controls: will bite users mildly.** Four dead typography sliders in a DefaultOpen section and a twice-appearing transitions section are exactly what makes a designer feel untrustworthy in the first hour. Cheap to fix.
- **The reset-button bug (finding 6): moderate.** It silently rewrites profile state against the design intent and can pin transitions Off. It's one button, but it's a button whose whole job is trust.
- **The 5-site effect-key duplication: no user impact today, compounding tax tomorrow.** It has already produced one rotted button (the pre-registry reset) and one class of seed-time bugs (the removed motion snapshot). Post-release refactor, not a blocker.
- **Legacy HUD renderer: zero user impact while hidden**, but it's ~2,000+ lines of shipping fallback for a transition that ended, and it is the *reason* the dead typography sliders and Show* toggles still exist.

## Recommendations

1. **[P0, S] Fix `ResetAllElementEffects`'s legacy re-write.** Delete `HudEditorMode.cs:1384-1392` (`customDissolve`/`fxCollapse`/`fxGlitch`/`fxWarp`/`fxPulse` + their Amt writes) — the registry loop at :1321-1331 already expresses "reset to Inherit" correctly, and `SetMode` keeps legacy mirrors for authored states. Re-test: master off → reset → master on → element must still dissolve.
2. **[P0, S] Merge the two "Suit power & transitions" sections.** Move DiegeticTiers, dropouts, tear/shake, and the two test buttons from View & Behavior (:588-613) into the Effects tab's registry section (:487-526); leave a one-line pointer behind. One section, one place, matching the popup's "Motion & power transitions".
3. **[P1, S] Gate the dead typography sliders.** Wrap `ValueFontSize`/`CompassFontSize`/`VitalsRowFontSize`/`BareWordFontSize` (and arguably `LabelFontSize`) in `if (!HudSystem.DocumentMode)` next to the Legacy panel sizes block (`HudEditorWindow.cs:248-257`); in document mode show "Text sizes are per element — click an element > Appearance." Do NOT delete the config entries (legacy mode still reads them).
4. **[P1, S] Stop offering inert universal knobs.** Add two virtuals on `HudElementView` — `UsesAccentColor` and `UsesFontScale` (default true) — and skip the two universal rows when false; override false in the ~8 widgets each that never read them (finding 3). This is the cheap version of "capability-driven parity" and kills the worst trust-breaker.
5. **[P1, S] Delete `MoodletDashboardWidget.cs`** (dead 472 lines) and fix the `GlitchEnabled` cfg description text (`HudConfig.cs:489-493`). Zero risk, removes real confusion for anyone reading the cfg or the code.
6. **[P1, M] Progressive disclosure on the Effects tab.** Collapse all headers by default except "Edges, glow & pulse"; add one "Show advanced (SDF) controls" checkbox that gates haze/uneven/organic/flow-aura/breath/sat-bias rows (they are already individually identifiable); rename user-facing "Tier A/B/C" to "Surface effects / Glass animation / Frosted glass" (labels only — cfg keys stay; TMP ASCII rule irrelevant here since ImGui, not TMP, renders this window — arrows/glyphs are safe in ImGui but keep ASCII anyway for font-atlas safety).
7. **[P2, L] The knob registry — make parity fall out automatically.** Extend the `HudTransitionFx` pattern to the steady-state families: a `HudStyleFx` table (key, label, range, global entry, master entry, param key, SDF-only flag, surface mask) consumed by (a) the F9 Effects tab rows, (b) the per-element Custom mirror, (c) both snapshot functions, (d) the reset button. This collapses the 5-site duplication (finding 5) to one table + one loop each, and makes "every global knob on every element that uses it" true by construction. Do it AFTER release — it touches profile-compat surfaces (seed/snapshot behaviour) and deserves its own adversarial review.
8. **[P2, M] Move the 4 base-class type-switch capability lists to widget virtuals** (`SupportsPanelAppearanceFor`, `SupportsBorderOnlyChrome`, `OptionalPanelBackgroundIsOff/Hint` → virtual properties like `SupportsTrapezoid` already is), so a new widget declares its whole editing surface in its own file. Note `CanFlattenDefinition`/bulk ops need a def-only path — keep a small static map for that one case.
9. **[P2, M] Retire the legacy fixed-panel HUD** (`UseDocumentHud=false` path) one release after launch if telemetry/reports show no one uses it: remove the legacy panels, the `HudEditTarget` popup path, the Legacy panel sizes section, the Show* toggles, and `LegacyImGuiHud`. That deletes more "deprecated features and effects we don't use" than any editor restructure — it is where most of them live.

**Is the flatten/standardize refactor a pre-release must?** No. Ship with P0+P1 (roughly two to three days: one bug fix, one section merge, dead-knob pruning, inert-knob gating, one file deletion, Effects-tab disclosure). The full registry flatten (P2 #7) is the right long-term move and the codebase has already proven the pattern on transitions — but it rewrites snapshot semantics that recent hard-won bug fixes (2026-07-17/19/20 review notes throughout `HudElementView.cs`) depend on, and doing it under release pressure is how those regressions come back.

## What NOT to do

- **Do not rewrite the F9 window in UGUI** or move element editing into the F10 Control Center. The ImGui editor is the POC-first philosophy working as intended; the Control Center already links to it for the deep work.
- **Do not build a reflection-based auto-inspector** over `HudElementDef`/Params. The typed descriptor + capability gates exist precisely so the editor never offers a key the renderer ignores — reflection would resurrect the inert-knob problem at scale.
- **Do not "unify" the per-element Custom mirror by deleting it** in favour of only-global editing, or by making every shared knob (light angle, clocks, frost capture) per-element. Several are shared material uniforms or single captures — per-element versions would require per-element materials/captures and a real GPU cost. The current "named as shared, in place" pattern is correct.
- **Do not remove the two-state Global/Custom style source** in favour of per-knob inherit tri-states for *appearance* (the transitions already have tri-states because motion needed "Off"; appearance doesn't have that semantic gap). Thirty appearance tri-states per element would triple the popup's row count for no user gain.
- **Do not delete "deprecated" config entries** (`FxBloomFineDetail`, legacy sizes, `LegacyImGuiHud`) before the legacy *code paths* go — orphaned cfg values are harmless (precedent: the retired GlitchShader knob, `HudConfig.cs:110-113`); dangling readers are not.
- **Do not reorganize the five top-level tabs.** Build/Theme/Effects/View/Diagnostics already matches the Layout/Style/Effects/Profiles mental model users need; the wins are all one level down.
