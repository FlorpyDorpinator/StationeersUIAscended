# The HUD Designer — Proposal

*Full in-game UI editor + concept-art HUD rebuild. Drafted 2026-07-11 for FlorpyDorp &
JacksonTheMaster. Status: **for consideration — not yet implemented.***

---

## 1. Why

FlorpyDorp's new concept art (see the mockup screenshot) shows where the visor HUD should
go: a single full-width top bar (clock · external pressure · compass · external temp ·
day · world name), car-dashboard **moodlet chips** that appear centered under it, the 1-6
equipment column, hand boxes with keybind chips, and a bottom-right cluster (needs %,
internal pressure/temp boxes with **target + threshold bars**, jetpack box, circular
portrait, suit-status chips, body-damage doll). The problem: all this data reads as
clutter unless *you* can arrange it. So the deliverable is two things at once:

1. **A full in-game UI editor** (F9): click any HUD element → selection box with 8 drag
   handles; move and resize everything — panels *and* the pieces inside them; draw new
   boxes (per-corner rounding), type custom text into them; place any data widget; all of
   it under the existing curvature system (A/B/C), saved to shareable profiles.
2. **The concept layout, premade**, shipping as the new default profile.

## 2. What was verified (decompile 27701, three recon passes)

Client-safe reads (MP-client baseline holds — the HUD stays display-only):

| Data | Source | Client-safe |
|---|---|---|
| Suit target pressure / temp | `ISuit.OutputSetting` (kPa) / `OutputTemperature` (K), sync flag 256 | ✔ |
| Internal pressure / temp | `Human.BreathingAtmosphere` (already sampled) | ✔ |
| Jetpack thrust / propellant | `Jetpack.OutputSetting × 100`; canister pressure − `WorldAtmosphere.PressureGasses` (vanilla shows the delta; low < 500, crit < 100 kPa) | ✔ |
| Needs | `NutritionRatio`, `HydrationRatio`, `HygieneRatio` | ✔ |
| **Toilet** | `SanitationRatio` — **server-only**; MP clients show `--` (same pattern as felt temperature) | ✖ |
| Suit chips | helmet closed (`GasMask.IsOpen`), helmet light (`OnOff`), suit AC (`Suit.OnOff`), internals — all synced Interactable states | ✔ |
| Body doll | 7-part data: `DamageState.TotalRatio`, head = max(total, brain), chest = max(total, lungs) — vanilla's own doll (in `StatusUpdates`) uses exactly these | ✔ |
| World name / day | `WorldManager.CurrentWorldName` / `DaysPast` | ✔ |
| Moodlets | `StatusUpdates.AllStatusUpdates`: active = `Image.gameObject.activeSelf`, caution/critical = public `_lastStaticState`/`_lastFlashState`, skip `UsesDedicatedDisplay`, fallback icon = `su.Icon` | ✔ |

Vanilla hiding (hide-never-destroy): the moodlet strip lives under
`InventoryManager.StatusPanel` — already covered by our `HideVanillaStatus`
reconciliation. The vanilla bottom-right cluster = `PlayerStateWindow.Instance.
InfoInternal/InfoExternal/InfoJetpack/InfoHealth` (`UserInterfaceBase`) — each hides via
its game-managed `.SetVisible(false)` (alpha-based), reconciled per-frame like the
existing panels, restored on mod-off. Vanilla truncates displayed numbers to int — we
match for parity.

## 3. Architecture — a data-driven HUD document

**One document, one view per element.** The HUD becomes a `HudDocument`: a flat,
XML-serialized list of elements. Every element gets its own view instantiated by
`HudSystem`, which means the ENTIRE existing machinery is reused unchanged — per-element
flicker faders, diegetic tiers (SUITED/BARE/ROBOT), power-death CRT collapse, boot
stagger, low-power dropouts, `VisorWarp`/`TmpWarp` curvature, mode-C material swap.

**Element schema** (flat DTO — no XML polymorphism, C# 7.3-safe):

- `Id` (GUID) · `Type` · **anchor + offset geometry** (9 screen anchors; `X,Y,W,H` in
  reference px, scaled by `HudScale` — resolution-proof, and drag math is trivial)
- `Z` draw order · `Tiers` visibility flags (Bare/Suited/Robot)
- Style: **ColorRef strings** — a palette entry NAME (theme follows the F9 colour wheels)
  *or* a literal `#RRGGBBAA`; per-corner radii; border width; font scale
- `Text` (labels), `Icon` (glyph name), and a K/V param bag for widget tunables
  (e.g. `Readout` stores its data source; polylines store their points)

**Element types:**

- *Primitives* — `Box` (per-corner rounding), `Label` (your text), `Polyline` (drawn
  line work), `Icon`
- *`Readout`* — the workhorse: pick any of ~17 data sources (external/internal
  pressure & temp, suit targets, needs, jetpack, power, health, O₂…), with optional
  icon, unit, and a **threshold bar** (colored zones + target caret)
- *Bespoke widgets* — Compass strip, Moodlet Dashboard, Equipment Column, Hand Boxes,
  Keybind Chips, circular Portrait (the existing hologram RT, leak-safe logic ported
  verbatim), Body Doll, Suit Chips, Clock, Day Counter, World Name, Active-Hand Badge,
  Bare-Senses words, Vignette

**Moodlet dashboard** mirrors vanilla's status list every frame and lays chips out
center-out exactly like the mockup: one chip = centered, two = split around center, N =
even spread; re-layout only when the active set changes. Caution vs critical states color
the chip; vanilla's strip is hidden.

**New graphic primitives** (all procedural meshes, baked-fringe AA, dirty-on-change —
the established idiom): 4-corner-radius upgrade to `PanelGraphic`; `ThresholdBarGraphic`
(track, zone-tinted fill, target caret, horizontal/vertical); `PolylineGraphic` (stroked
segments, bevel joins); `HudIconGraphic` + a hand-authored glyph table (flame, droplet,
toilet, bolt, burger, chair, helmet, jetpack, sun, thermometer, lungs, heart, gauge,
hands, backpack, glasses, belt, suit…). **PNG override folder**
(`BepInEx/config/StationeersUIMod/HudIcons/<name>.png`) — drop in your own art and it
wins over the built-in glyph, no rebuild.

## 4. The editor (F9)

- **Select**: click any element → outline + 8 resize handles, drawn on a dedicated
  overlay canvas and positioned by *forward-warping* the element's corners — so the
  selection box hugs the element correctly even under curvature. Smallest-rect hover
  logic and the inverse-warped mouse already exist and are reused.
- **Move/resize**: drags edit the element's stored (unwarped) geometry — no curvature
  drift; opposite edge stays pinned on handle drags; grid snap (Alt = freeform).
- **Create**: Add buttons (Box / Text / Line / Icon / widget picker); a new Label
  auto-focuses its text input — draw a box, type into it. Delete key removes; Ctrl+D
  duplicates.
- **Properties**: the popup renders each element's properties generically (sliders,
  combos, tier checkboxes, palette-link dropdown + colour wheel) — same pattern as
  today's config popup, extended to document fields.
- **Undo/redo**: document snapshots (cap 50, one drag = one step) — mirrors the palette
  history that already ships.
- **Profiles**: autosaved (1.5 s debounce + on close), named profiles in
  `BepInEx/config/StationeersUIMod/HudProfiles/*.xml` — plain XML, shareable; a starter
  `Default.xml` (the concept layout) regenerates if the folder is empty.

## 5. Migration & compatibility

- Compass/Equipment/Hands/Bare-senses logic is *refactored into widgets* (pooling, warp
  exemptions, felt-word behavior all preserved); the TopStatusBar/VitalsCard monoliths
  are deleted only after a parity check.
- Old per-panel size config entries are deprecated (left orphaned in the .cfg — no data
  loss); geometry lives in the document now. Global knobs stay config: curvature, scale,
  fonts, palette, tiers, flicker.
- Hot-reload safe (every new store/canvas resets in Shutdown), fail-soft per element
  (a bad element or bad profile file logs and skips, never kills the HUD).

## 6. Phases (each shippable + play-testable, each with its own Changes Report)

| Phase | Contents | Visible change |
|---|---|---|
| 0 | New graphic primitives + all new sampler reads | none (foundations) |
| 1 | Document model, profile store, view host — behind a flag, with a parity document reproducing today's 0.5.0 layout | none unless flag flipped |
| 2 | Widget refactor, document HUD becomes the real one, monoliths deleted | HUD identical, now data-driven |
| 3 | Concept widgets (moodlet dashboard, body doll, threshold-bar readouts, suit chips, portrait circle…) + the concept `Default.xml` + vanilla bottom-right hiding | **the new look** |
| 4 | The full editor: handles, drag/resize, draw boxes/text/lines/icons, properties, undo, profiles UI | **the designer** |

Version numbers per phase: FlorpyDorp decides before each push.

## 7. Top risks

1. Curvature mode C (world canvas) material swap vs the portrait RenderTexture —
   special-cased and tested in phase 3.
2. Editor-time relayout hitches with ~100 elements — targeted single-element relayout
   during drags; zero steady-state mesh rebuilds.
3. XML drift / hand-edited profiles — per-element fail-soft load + schema version int.
4. Vanilla `PlayerStateWindow` re-asserting its panels — per-frame actual-state
   reconciliation (the pattern that already survived the unconscious-watcher fight).
5. Moodlet internals are undocumented vanilla — public fields verified in 27701; guarded
   reads; a game update degrades the dashboard, never the HUD (fail-soft rule).

## 8. Open questions for review

- Should the moodlet chips show text labels (HUNGRY) like the mockup, or icon-only with
  hover/text on critical? (Mockup shows labels — default to labels, toggleable.)
- Body doll placement: inside the portrait circle vs a separate element (default:
  separate element, appears only when damaged).
- Keybind chips: read live key bindings from `KeyManager` vs static text (default: live).
- Do we keep the old arc-curved top bar as an optional widget style? (`ArcBandGraphic`
  survives either way.)
