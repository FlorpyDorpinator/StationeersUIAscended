# Glassy 2.0 — Element Tracker

Source spec: `docs/Big Prompt` (FlorpyDorp, 2026-07-12). This file tracks every element
until each is confirmed good in-game. Statuses: ⬜ not started · 🔨 building · ✅ built
(awaiting play-test) · 🟢 play-test approved · ❌ blocked/issue.

> RECON CORRECTION: vanilla's bar has **no UTC clock and no world name** (mis-recollection
> or a different overlay). Our Clock/DayCounter are our own widgets, so "Time | … | Day #"
> still works. Temp icons: hot >50 °C / cold <0 °C, hidden between (2 sprites, NO fire icon).

## 1. Top bar
| Item | Status | Notes |
|---|---|---|
| 1a. Shape: full-width inverted trapezoid (top wider, angled sides, curved corners) | ✅ | PanelGraphic insets wired as Box params `insetTop`/`insetBottom`; bar uses `insetBottom` 46 |
| 1b. Order: Time \| EXT PRESSURE + game ramp UNDER \| boxless compass (° below) \| EXT TEMP + hot/cold icon \| DAY # | ✅ | Readout `stack`/`tempIcon` + compass `box=false` + ramp fix; built in Glassy 2.0 doc |
| 1c. Curved corners on the trapezoid | ✅ | per-corner radii on the bar |

## 2. Moodlet bar
| Item | Status | Notes |
|---|---|---|
| 2a. Subtle \\____/ backdrop, barely-visible line, vanilla icons | ✅ | box opt-in (default bare icons); MoodletDashboard |
| 2b-1. Power moodlet dedupe (only what vanilla would show) | ✅ | group by shared Image.gameObject; winner by DISPLAYED LEVEL then Type (review-fixed: was Type-only → showed CRITICAL name during caution) |
| 2b-2. Icon much larger, text smaller, F9 size controls, stacked words | ✅ | `iconScale`/`textScale`/`stackWords` params; two-word names split to 2 lines |
| 2b-3. Overflow → stack into rows; width controllable, dynamic | ✅ | wraps downward from element top; `chipWidth`/`colGap`/`rowGap` |

## 3. Bottom boxes (hands + 1-6)
| Item | Status | Notes |
|---|---|---|
| 3a. Slot numbers: unclip (down+right), more grey | ✅ | nudged +x/−y off the corner; recolored to TextDim |
| 3b. Visor-bottom arc over hand area, fading ends | ✅ | Polyline `fadeEnds`; `g2-visor-arc` in the doc. (Procedural — if it reads thin, prefab is the fallback) |

## 4. Bottom right
| Item | Status | Notes |
|---|---|---|
| 4a. Portrait camera: F9 distance + FOV controls | ✅ | `camFov` (robust — vanilla never rewrites FOV) + `camDistance`; captured/restored |
| 4b. Jetpack box: JETPACK / THRUST # / #### kPa + vanilla green canister icon | ✅ | JetpackBoxWidget; `VanillaIcons.JetpackCanister()` (icon-jetpackpressure) |
| 4c. 3 on/off chips (helmet/light/jetpack), vanilla PNGs + logic, adjacent above portrait | ✅ | StateChipsWidget; presence-gated (review-fixed: sprites are always present so gate on HelmetPresent/JetpackPresent). Helmet/jetpack index mapping = PLAY-TEST |
| 4d. Internal Pressure box: title / TARGET / # kPa / vanilla bar | ✅ | Readout `stack`+`target`+`barStyle=game` |
| 4e. Internal Temp box: title / TARGET / #°C + hot/cold icon left | ✅ | Readout `stack`+`tempIcon` (>50 hot / <0 cold; **NO fire icon exists** in vanilla — 2 sprites only) |
| 4f. Vitals box: vanilla icons + %, food-quality badge, dynamic membership | ✅ | VitalsPanelWidget; hunger/water always, toilet>0.25, health on body-OR-organ damage (review-fixed); badge is a single composite (review-fixed from 4 tiled) |
| 4g. Player speed box: speed + # m/s | ✅ | Readout `Speed` source (Human.VelocityMagnitude). Icon is a stand-in — PLAY-TEST wants a velocity glyph |
| 4h. Body map: VANILLA doll in our box (no vanilla bg), only when injured | ✅ | DamageDollBorrowWidget reparents PersonDamageObject; disables its bg Image; restore wired |

## 5. Suit off / power off (Bare tier)
| Item | Status | Notes |
|---|---|---|
| 5a. Top bar gone; everything FLAT; vitals icons + WORDS; borderless grey panel | ✅ | top bar is SuitOnly (auto-hides); `BareFlattens` drops curvature; `g2-bare-vitals` words-mode borderless panel |
| 5b. Pressure row: vanilla pressure symbol + words | ✅ | VitalsPanel `rowPressure` (THIN AIR / PRESSURIZED / HIGH PRESSURE) |
| 5c. Temp row: thermometer symbol + hot/cold words, color-coded | ✅ | VitalsPanel `rowTemp` (red hot / blue cold) |

## F9 editor bugs
| Item | Status | Notes |
|---|---|---|
| E1. Curvature clips content at screen edges ("too close to screen") | ✅ | `Barrel` fit-scale `1/(1+|k|·0.30)` keeps flared corners on-screen; Unwarp tracks it |
| E2. Panel internals (top bar contents, compass bg) don't curve in vertex mode | ✅ | `CenterFor` now position-warps off-axis elements; compass goes boxless (its bg sat on the axis where barrel≈0) |
| E3. Element popup appears bottom-right → open centered | ✅ | popup opens screen-centered |
| E4. Element popup size-locked, cuts off content → freely resizable | ✅ | size cap raised to 0.92× screen |

## Carry-over bugs (from 2026-07-12 play-test)
| Item | Status | Notes |
|---|---|---|
| C1. White square at screen center (ramp front never laid out, null sprite) | ✅ | ramp Images born disabled; hidden until vanilla sprites resolve |
| C2. Gauges render solid white bars (track drawn 100% white, vanilla uses 25% + lit child art) | ✅ | harvest lit CHILD sprite + vanilla's 0.251 track tint + `Image.fillAmount` reveal |

## Process
- Build agents: **fable** · Final adversarial review: **opus 4.8** (per FlorpyDorp)
- Changes Report at the end + final report to FlorpyDorp.

## Review outcome (opus, 6 lenses + refuters — 2026-07-12)
7 findings confirmed and FIXED before ship:
1. **CRITICAL** — `CenterFor` position-warp double-warped every element (`Barrel(Barrel(x))`)
   and desynced the F9 editor → reverted to logical placement; the mesh warp does the single
   curve. (E2 "top-bar internals ride the curve" is therefore only partial — see Known limits.)
2. Moodlet dedupe tie-break (Type-only) → showed "POWER CRITICAL" during a caution-only state.
   Now ranks by displayed level first.
3. State chips never hid (vanilla sprites always present) → presence-gated on worn gear.
4. Food-quality: drew the composite badge 4× → shows one badge.
5. Health row missed organ-only damage → gated on body-OR-organ.
6. `tempIcon` inert without an `Icon` → slot now builds when the toggle is on.
7. `HudWarp.BareFlat` not reset on teardown → reset in Shutdown.

## Known limitations / play-test flags
- **E2 partial**: small centred elements only micro-bend (fixing that fully needs stripping the
  per-graphic mesh warp — deferred; the double-warp bug made the shortcut unsafe).
- Helmet/jetpack chip on/off **index mapping** is a guess → verify in-game, may need a flip.
- Speed readout uses a stand-in icon (no dedicated velocity glyph wired yet).
- Doll doesn't warp with curvature (vanilla children carry no VisorWarp) — sits flat.
- `JetpackCanister()` grabs the first sprite-bearing child of the delta panel — confirm it's the
  green canister, not a panel background.
