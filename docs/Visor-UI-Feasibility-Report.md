# Full UI Replacement — Feasibility Report ("Visor HUD")

*Stationeers UI Ascended — 2026-07-10, for FlorpyDorp & JacksonTheMaster*
*Subject: the target mockup (curved top status bar, left equipment column, center radial,
bottom hand boxes, bottom-right vitals card) and the suit-powered "diegetic HUD" concept.*

---

## 1. Verdict

**Yes — everything in the mockup is buildable with the procedural UGUI stack we already
use for the radials, including the curvature.** Nothing in the image needs assets,
shaders we can't write, or engine features the game doesn't already load. The radials
were the hard part: runtime meshes, per-vertex anti-aliasing, live theming, hot reload.
Panels, bars, item boxes and text cards are strictly easier versions of the same tricks.
The two genuinely new pieces of technology are (a) a **vertex-warp component** for the
curved-visor look and (b) a **HUD state machine** for the powered/unpowered/robot tiers —
both modest, both detailed below.

The mockup's element inventory, mapped to what we already have:

| Mockup element | Tech needed | Status |
|---|---|---|
| Curved top status bar (UTC, pressure, O₂, temp, power, water, suit status) | One long panel mesh + TMP labels + icons | Data already read by our ImGui HUD (`WorldAtmosphere`, suit battery); panel mesh is a `RadialWedgeGraphic` cousin — an arc band IS our wedge with a huge radius. The curve in the mockup is literally an annular arc: we can draw it *exactly* with existing code. |
| Left equipment column (1–6: helmet, glasses, suit, back, uniform, belt) | Rounded-rect boxes + item thumbnails + labels | `GetThumbnail()` + `Image` (preserveAspect) — same as radial icons. Click/hover can reuse the radial hit-testing pattern. Numbers map 1:1 to our existing equipment-key radials. |
| Center radial with category wedges (Backpack/Tools/Devices/Atmospherics/Clothing) | Existing radial renderer | Done. Note: the mockup's root is *category-based* (our new UIA sorting classes are exactly the right data source for those wedges). |
| Bottom hand boxes (LEFT HAND / RIGHT HAND) | Two panels + thumbnails + active-hand highlight | Our ImGui HUD already draws these; port to UGUI. |
| Bottom-right vitals card (body icon, health/O₂/power/temp, day/time) | Panel + rows | Data proven (`Hydration`, `NutritionRatio`, day/time). |
| The dark vignette/glass look | A full-screen `Image` with a radial-gradient generated texture, or vertex-colored quad ring | Trivial; also gives us the "visor edge" darkening for free. |

## 2. The curvature ("projected on a visor")

Three real options, in order of recommendation:

**A. Vertex-warp component (recommended).** A `BaseMeshEffect` (`VisorWarp`) added to any
Graphic — including TMP text — that displaces vertices in `ModifyMesh`: `y += k * f(x)`
(barrel curve), plus optional inward tilt near the screen edges. This is exactly the
mesh-level game we already play in `RadialWedgeGraphic`; TMP text warps per-glyph-quad and
stays crisp because the glyphs are SDF — curvature doesn't blur them. Cost ≈ zero (runs
only on mesh rebuilds). **Curvature becomes a single config slider (0 = flat, 1 = fishbowl)
editable live in the radial editor**, per-panel overridable. This is how we "make them
bend (and make it changeable)".
- Caveat: elements are warped individually, so a very long straight bar bends as a bar
  (good — that's the visor look), but hit-testing must use the *unwarped* rects (fine:
  we drive hover ourselves, same as the radials).

**B. Render-texture dome (the "true" projection).** Render the whole HUD canvas to a
RenderTexture and display it on a curved mesh in front of the camera. One warp for
everything, physically honest, and enables cool extras (chromatic fringe, scanlines,
refresh flicker as a *material* effect). Costs: one RT (memory + a blit), slight text
softening at 1080p, and interaction math has to invert the warp for the cursor. I'd hold
this as a **phase-2 upgrade** — the architecture below keeps it possible (everything on
one HUD canvas) without committing now.

**C. Curved-canvas third-party assets** — not an option; we ship no dependencies.

Practical note from the radial work: ScreenSpaceOverlay canvases get **no MSAA**, so every
curved edge needs the same baked alpha-fringe we already do. `VisorWarp` doesn't change
that — the fringes warp with the mesh and keep working.

## 3. The diegetic tiers (this is a great design — build it as a state machine)

One `HudState` resolved every frame, driving every panel:

| State | Condition (all client-readable) | What shows |
|---|---|---|
| **BARE** | No suit worn, or suit unpowered | Body-feel only, in WORDS (see table below). No numbers, no atmosphere data, no day counter (you'd know roughly — "morning"), no suit status. |
| **SUITED** | Suit worn + suit battery > 0 | The full mockup: numbers, atmosphere, suit status, filters, clock. |
| **ROBOT** | Playing the robot character | Always full readout (you ARE the computer) — plus room for a distinct skin later (mono-color, denser, no "suit" concept). |

**The "feelable senses" table for BARE** (word bands instead of numbers — each is a
threshold mapping over data we already read):

| Sense | Source | Words |
|---|---|---|
| Temperature | `WorldAtmosphere.Temperature` | FREEZING / COLD / NOMINAL / WARM / HOT / BURNING |
| Air | breathability of current atmosphere | CHOKING / THIN / STALE / BREATHABLE |
| Hunger | `NutritionRatio` bands | STARVING / HUNGRY / FED / FULL |
| Thirst | `Hydration` bands | PARCHED / THIRSTY / FINE |
| Health | damage ratio bands | DYING / HURT / BRUISED / OK |
| Pressure | only extremes (you'd feel your ears) | VACUUM! / LOW / — (nothing in normal band) |

Bands over ratios are one small pure function each; the vagueness *is* the feature, so
imprecision costs nothing. One design suggestion: make BARE's words **event-y** — only
show a word when its band *changes* (fade in, linger, fade out) — so the bare HUD feels
like noticing sensations rather than reading a dashboard.

**Transitions & animations** — all cheap with UGUI + CanvasGroup:
- *Power dies*: 0.8s flicker-out — CanvasGroup alpha driven by a decaying square-ish noise
  (`alpha = decay * (noise > cutoff ? 1 : 0.2)`), plus one horizontal "collapse" scale
  tween. Then BARE fades in slowly (your eyes adjust).
- *Suit boots*: reverse — panels flicker in one by one with slight stagger (top bar,
  then vitals, then hands). This sells the fantasy hard for ~20 lines of lerp code.
- *Low power*: below ~10% suit charge, occasional single-frame dropouts. Free drama.
- These run on `Time.unscaledDeltaTime` like the radial animations; no coroutines needed.

**Data sources to verify before building** (the only recon this needs):
- Suit powered: worn `SuitBase` + its battery (`CurrentPowerPercentage` — the client-synced
  byte we already use). Also whether vanilla has an explicit "suit powered" bool worth
  mirroring.
- Robot detection: the playable robot's entity class/flag (needs a decompile check).
- Breathability: which atmosphere values vanilla's own choking logic uses, so our words
  agree with what actually kills you.

## 4. Architecture

```
UI/Hud/HudSystem.cs        one canvas (sortingOrder ~4000, under radials), state machine,
                           flicker/boot animator, panel registry
UI/Hud/VisorWarp.cs        BaseMeshEffect: curvature for ANY graphic incl. TMP
UI/Hud/PanelGraphic.cs     rounded/arc panel mesh w/ baked AA (RadialWedgeGraphic cousin)
UI/Hud/TopStatusBar.cs     the curved bar (arc band + N stat cells)
UI/Hud/EquipmentColumn.cs  six slots, thumbnails, key hints; click = same as tapping 1-6
UI/Hud/HandBoxes.cs        two boxes + active-hand highlight (shared w/ radial hand logic)
UI/Hud/VitalsCard.cs       numbers when SUITED/ROBOT, words when BARE
```

- **One canvas** for the whole HUD (so option B's render-texture upgrade stays a drop-in).
- Everything themed through `RadialPalette` (extended with HUD entries) and tuned in the
  **radial editor**, which grows a "HUD" tab — same live-editing workflow you have now.
- Vanilla panels stay alive and hidden via the existing `SetUIPanelVisibility` path
  (hide-never-destroy, per project rules); the mockup's equipment column *replaces* the
  vanilla slot strip visually while vanilla logic keeps running.
- The current ImGui HUD (`HudOverlayFeature`) stays as the fallback during the port, then
  retires — same playbook as the ImGui→UGUI radial migration, which worked.

## 5. Effort & phasing (honest sizes)

| Phase | Contents | Size |
|---|---|---|
| 1 | `VisorWarp` + `PanelGraphic` + HUD canvas + top status bar (numbers only) | ~1 session |
| 2 | Hand boxes + equipment column + vitals card; retire ImGui HUD | ~1 session |
| 3 | HudState machine: BARE/SUITED/ROBOT + word bands + power flicker/boot animations | ~1 session |
| 4 | Polish: editor "HUD" tab, per-panel curvature, vignette, low-power dropouts | ~1 session |
| (5) | Optional render-texture dome upgrade | when it earns it |

"Session" = one of our build-test-iterate evenings with F6 reloads. Phases 1–2 are
low-risk (pure rendering). Phase 3's only real work is the data verification above.

## 6. Risks (all manageable)

- **Screen-size variance**: arc geometry must derive from `Screen.width/height` each frame
  (the radials already do this) — ultrawide monitors will show more curve, which is fine
  and actually looks right on a visor.
- **Vanilla HUD interop**: hiding the vanilla hands panel while our hand boxes take over
  must keep the *active-hand indicator* semantics visible (we're touching this in the
  current iteration anyway).
- **Hardcoded vanilla popups** (interactable tooltips, warnings) will still render in
  vanilla style until we get to them — the replacement is incremental by design; nothing
  breaks in the meantime.
- **Performance**: the whole HUD is a few hundred vertices + ~40 TMP labels. The radials
  are already heavier. Non-issue.

## 7. Design thoughts you didn't ask for (but should consider)

- **The mockup's category-root radial** (Backpack/Tools/Devices/Atmospherics/Clothing) is
  the best version of Tab yet — and our UIA sorting classes are literally that taxonomy.
  When we build the HUD, Tab's root should become these categories with the worn-equipment
  entries beneath.
- **Suit status word** (the mockup's "NOMINAL") is a great top-level abstraction: derive
  it as worst-of(filters, battery, tank pressure, waste) → NOMINAL / CHECK / WARNING /
  CRITICAL, and let hovering it open the suit radial.
- **Helmet off but suit on** could be a half-tier: suit data (power, tanks) but no
  atmosphere numbers (sensors live in the helmet). You already have `HardcoreGating`
  pointing this direction; the state machine makes it a one-line rule.
- **Robot skin**: same panels, different palette preset + no BARE tier + maybe intentional
  raster artifacts. Cheap once the state machine exists — a palette preset + font swap.
- **Every panel should be optional** (config bools like the current HUD) so players can
  mix our HUD with vanilla pieces during the alpha.

**Bottom line: green light.** The UGUI stack has already proven out the hardest parts on
the radials, the curvature has a cheap correct answer, and the diegetic tier system is
mostly threshold functions over data we already read. The mockup as drawn is reachable in
~4 focused sessions, incrementally, without ever breaking the playable build.
