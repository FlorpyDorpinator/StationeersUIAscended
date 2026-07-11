# The Visor HUD

*Stationeers UI Ascended 0.5.0 Alpha — 2026-07-10. The full-UI replacement from the
feasibility report (`docs/Visor-UI-Feasibility-Report.md`), built to the concept art.*

The whole game HUD is redrawn as a curved, suit-projected visor interface: thin cyan
line-work on dark glass, procedural UGUI (no assets, no shaders shipped), themed and
resizable live. The radials are untouched — this is everything AROUND them.

## The panels

- **Curved top status bar** — UTC date/clock on the left (mission epoch 2080 + days
  survived; the clock maps sunrise to ~06:00), then PRESSURE · O₂ · TEMP · POWER ·
  WATER cells, then SUIT STATUS on the right. The status word is worst-of everything
  the suit knows: battery, air tank, filters, waste — NOMINAL / CHECK / WARNING /
  CRITICAL. Values color-shift at the game's REAL warning thresholds (the same numbers
  that damage you, read from the decompile).
- **Compass ribbon** under the bar's center (~12 % of the screen, adjustable): cardinal
  letters and ticks slide past a fixed caret, exact degrees underneath. The number
  matches vanilla's Navigation readout formula exactly, but follows the CAMERA, so
  free-look reads true.
- **Left equipment column** — six boxes = keys 1-6 (helmet, glasses, suit, back,
  uniform, belt) with live thumbnails. The robot's #5 reads BATTERY, like its real slot.
- **Bottom hand tray** — LEFT HAND / RIGHT HAND on a trapezoid shelf. The active hand
  wears the orange accent. Never a hotbar.
- **Vitals card** (bottom-right) — the live 3D player render as a cyan **hologram**
  (the game's own portrait camera, tinted + scanlines) beside HEALTH / O₂ / POWER /
  TEMP rows and a DAY + time footer.
- **Vignette** — the visor-edge darkening.

## The diegetic tiers

The HUD *is your suit's HUD* (config `DiegeticTiers`, on by default):

| Tier | When | You see |
|---|---|---|
| **SUITED** | suit worn and powered (the game's own `Powered` bit) | everything above |
| **BARE** | no suit, or battery dead | hands + equipment only, plus felt-sense **WORDS** — WARM, COLD, THIN AIR, HUNGRY, PARCHED, HURT… — that flare when a sensation changes and fade when it passes. The clock becomes a vague day-part word (MORNING, DUSK). Word bands sit on the real damage thresholds. |
| **ROBOT** | playing the robot | always the full readout — you ARE the computer. POWER reads your body battery. |

**When suit power dies, the HUD dies with it**: 0.8 s of decaying flicker and a CRT-style
vertical collapse, then your bare senses fade in slowly. Putting a powered suit on boots
the panels back one by one. Below 10 % battery (adjustable) the HUD suffers single-frame
dropout glitches. Toggling any panel off flickers it out. All of it under
`FlickerAnimations` if you'd rather it just switch.

## The curvature — all three implementations (A/B/C)

Pick in the F9 editor; `CurveStrength` 0–1 applies to all, and **Invert curve
direction** flips the bend (the default was flipped after play-testing — edges flare
away from the screen centre):

- **A — Vertex warp** (default): every element's mesh bends toward the screen's axis.
  Zero cost, SDF-crisp text.
- **B — Dome projection**: the whole HUD renders into a RenderTexture drawn on a
  dome-warped grid — one true projection, and the scanline color (`HudScanline`)
  dresses the entire visor. Uses a disabled off-screen camera rendered manually (the
  game's own pattern for off-screen rigs). Known quirk: translucent panels read a
  touch fainter here (alpha composites twice through the texture) — raise
  `HudPanelFill`'s alpha if you live in dome mode.
- **C — Curved world canvas** (experimental): the canvas physically floats in front of
  the camera, cylinder-bent — true perspective curvature. Text switches to the game's
  ZTest-Always TMP shader; panels may clip into geometry closer than the visor distance.

## The F9 HUD editor

Press **F9** (configurable — note vanilla binds F9 to the CREATIVE spawn-item menu;
in creative mode rebind one of the two). The game dims, the HUD stays live, and:

- **Click any element on screen** → an edit popup opens right there with exactly its
  colors and sliders. Click empty space to dismiss.
- **Hover any element** → its palette entries light up orange in the F9 window and the
  list scrolls to them.
- The F9 window holds everything: curvature mode + strength, every panel toggle, every
  size, font (any TMP font the game loaded) + per-role font sizes, the diegetic/flicker
  behavior, and all 19 HUD colors with hue wheels, **Undo/Redo**, and reset.
- **Preview tier** combo forces BARE / SUITED / ROBOT so you can style states you
  aren't in, and **Test power-death / Test boot** buttons play the transitions.

HUD colors are their own palette (config section `11. HUD Colours`) — restyling the
visor never touches the radials.

## Vanilla stays alive

Vanilla hands/clothing/status panels are hidden only through the game's own
`SetUIPanelVisibility` (opt-in, restored on exit). The hologram borrows the vanilla
portrait camera's RenderTexture — re-read every frame, restored through the game's own
`RefreshPortrait()`. The legacy 0.1.0 ImGui HUD remains as `LegacyImGuiHud` fallback.

## What's next (not in 0.5.0)

Moodlet coverage beyond the built-ins (leak, coolant, jetpack, G-force, medical timers
— see `docs/Moodlets-Reference.md` for the full menu), the reticle/interaction text
restyle, and the render-texture extras (chromatic fringe) if dome mode earns them.
