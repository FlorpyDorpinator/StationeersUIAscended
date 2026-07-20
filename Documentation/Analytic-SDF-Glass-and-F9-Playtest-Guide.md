# Analytic SDF Glass and F9 Play-test Guide

This guide covers the experimental work on branch `Imgui-Unity-Sol-0.9.0.5`. The branch name is
an experiment identifier, not a release version or tag. F10 and the experimental UGUI radial
editor were intentionally left unchanged.

## What this branch is trying to prove

The branch tests whether UI Ascended can use one coherent, authorable GPU panel language for the
Stationeers visor. The intended result is not generic glassmorphism: panels should feel like light
and information projected into a curved helmet, while remaining readable and preserving the
game's physical two-hand interaction model.

There are two linked experiments:

1. Replace appearance-painted panel geometry with an analytic shader that evaluates the panel
   contour and effects per pixel.
2. Replace F9's prototype-era settings layout with a predictable global-versus-per-element
   authoring model.

## The most important testing rule: establish the style source first

Every element has one of three style sources:

| Style source | Meaning |
|---|---|
| `Legacy mixed` | Preserves the old profile's mixture of local values, `-1` sentinels, and old follow flags. A global slider may not control every effect on that element. |
| `Follow global theme + effects` | Theme and effect values come from the global F9 controls. Use this for reliable global-effect testing. |
| `Custom` | The element keeps a complete local snapshot of the effects it supports. Global strength sliders do not replace those local values. Shared environment controls such as light direction, effect timing, capture, tint, and bloom remain global. |

Therefore, a working global effect can appear inert when the test element is `Custom` or is using
legacy local values. This was the explanation for the initial "Glow Halo does nothing" report:
the tested elements were not following the global effect settings. The halo rendered once their
inheritance was corrected.

For a controlled global test, duplicate the profile and choose:

`F9 > Theme > Panel surface > ALL follow Theme + Effects globals`

For a controlled local test, select one element, switch its style source to `Custom`, and change
the corresponding control in that element's Effects tab.

The global `Enable surface and edge effects` master switch must remain on for both Global and
Custom halo settings. Custom controls choose local values; they do not bypass the renderer's
master safety switch.

## What changed in the renderer

### Analytic rounded and trapezoid panels

- Rectangular and trapezoid `PanelGraphic` surfaces can now be rendered by `UIA/HudPanelSdf`.
- Fill, four independent corner radii, round-to-squircle contour, trapezoid insets, border sides,
  feathering, soft edge, halo, frost, chroma, shine, iridescence, dissolve, and end fades are
  evaluated in the fragment shader.
- The shader computes antialiasing from the warped on-screen distance field, so curved panels
  should retain more consistent edge weight.
- A small regular mesh remains for visor-warp fidelity, but it no longer paints all appearance
  bands into vertices.
- Unsupported surfaces, masks, missing bundles, and freeform pen polygons fail soft to the proven
  mesh renderer. Freeform polygons were deliberately not forced into this rounded-panel shader.

### Animated edge energy

- The moving light/dark energy on borders is now generated continuously in local 2D shader space.
- It no longer depends on a wrapped perimeter scalar, so light should travel through corners
  without a seam.
- Animation is shader-time. A moving pattern should not cause a per-frame
  `Hud.Mesh.SdfPanel` rebuild storm.

### Halo and edge bands

- Panel bands now have an explicit order: fill/feather, border, outer feather or soft edge, then
  outward halo.
- The mesh skirt allocates the combined width, preventing soft edge plus halo from clipping each
  other.
- Inner and outward halo strengths are independent.
- Halo / aura radius now reaches 320 px on analytic panels. The mesh fallback deliberately caps
  its old tessellated halo at 160 px to avoid magnifying its radial banding.
- The optional `Gaussian distance falloff` changes the halo's distance curve. It is not a blurred
  shadow or an exact Gaussian convolution.

### Organic and animated halo controls

- `Extended atmospheric haze` adds a second faint long-tail lobe without replacing or flattening
  the core halo falloff.
- `Uneven / organic reach` applies stable panel-local variation to halo/aura reach and energy. The pattern is
  stationary relative to the panel: it should not boil, flicker, or swim as the camera moves.
- `Halo / aura breathing` modulates only outward halo/aura energy. It does not pulse the fill, text,
  border, frost, or inner glow. All breathing panels share one unscaled global rhythm; Custom
  elements store their own participation and depth. Both controls work for an aura-only setup;
  ordinary Glow does not have to be secretly enabled.
- `Flowing edge aura` is a separate luminous lobe emitted by the broad primary wave of animated
  edge energy. It can render while ordinary outward strength is zero, reuses the halo radius and
  spread, and avoids projecting high-frequency border detail into radial spokes.
- Every new contribution defaults off/zero. Existing Custom profiles without the new keys remain
  neutral instead of silently inheriting newly enabled globals.
- A conditional ABI-v2 parameter encoding preserves the exact old stream for untouched panels and
  degrades safely with an old resident bundle. The runtime rejects an ABI-1 SDF bundle and uses the
  mesh fallback until a full restart loads the rebuilt bundle.

### Frost and chromatic fringe

- The existing final-frame capture and dual-Kawase blur pipeline remains in place.
- Its already-computed intermediate levels are now exposed to analytic panels.
- Each panel can select its own frost depth without adding another capture or blur pass.
- Chromatic fringe uses offset samples from that shared blurred backdrop. Near an edge it follows
  the panel normal; in the deep interior it changes to a stable direction to avoid a chromatic X.

### Runtime and curvature safety

- The SDF shader is a separate material family and reports readiness independently in F9
  Diagnostics.
- SDF mode is enabled only after the material is assigned successfully.
- CurvedWorldCanvas receives a canvas-scoped material clone with the required Z test; F10's radial
  canvas keeps its own existing material behavior.
- New material, texture, and shader state is cleared during teardown and F6 reload.

## What changed in F9

The main window is divided into five purpose-based tabs:

| Tab | Purpose |
|---|---|
| `Build` | Profile structure, element creation, selection, layout, and the canvas designer. |
| `Theme` | Shared panel surface, typography, palette, analytic-panel options, and bulk style-source actions. |
| `Effects` | Edge light, animated energy, glow, pulse, shader animation, frost, and bloom. |
| `View & Behavior` | Projection, curvature, HUD visibility, interaction, and behavior controls. |
| `Diagnostics` | Shader readiness and renderer/profiler information. |

Profile, preview-tier, undo/redo, save state, and exit controls remain visible above the tabs.
Effect subcontrols appear only when their parent feature is enabled.

Element popups are similarly divided into `Content`, `Layout`, `Appearance`, `Effects`, and
`Interaction`. Controls are capability-aware: an element should only expose settings its widget
can actually render. Freeform Shapes no longer expose halo controls, and optional-background
widgets that disable their panel explain when saved effects are invisible because the box is off.
Popups and the global Effects tab also call out an unavailable/disabled SDF renderer and the
mesh fallback's 160 px radius cap.

Other authoring changes include:

- The first transition to `Custom` snapshots the currently rendered appearance without a visual
  jump.
- Switching from Custom to Global and back restores the saved Custom design.
- `Snapshot ALL as Custom` deliberately refreshes every local snapshot, including hidden tiers.
- `Make flat` changes a panel to Custom and disables its optical layers while preserving its base
  layout and colors.
- `Reset effects` gives an active Custom element a complete snapshot of the current global effect
  design. If Global/Legacy is active, any dormant Custom snapshot is invalidated as a whole before
  inherited keys are cleared, so returning to Custom seeds a fresh complete design rather than a
  half-stale mixture.
- Continuous sliders, color edits, canvas drags, popup changes, and interrupted edits now use
  coherent undo transactions. Net-zero and repeated no-op operations should not clear redo.
- The toolbar reports pending autosave state.

## Effects that are easy to confuse

| Control | Where the result appears | What it does not do |
|---|---|---|
| `Edge energy` | Inside the border band | It does not spread light outside the panel. |
| `Glow halo > outward strength` | Outside the panel contour; directionally lit sides are strongest | It uses source-over blending and does not add post-process bloom by itself. |
| `Glow halo > inward strength` | From the frame into the panel fill | It does not change the outward halo. |
| `Glow width` | Distance the inner/outward glow can cover | Width has no visible effect when the corresponding strength is zero. |
| `Glow diffuseness` | Reshapes the halo from a tight rim toward a broad haze | It is not blur radius and can lower the peak while spreading the light. |
| `Extended atmospheric haze` | Adds a faint long tail behind the core outward glow | It has no source when ordinary outward strength is zero. |
| `Uneven / organic reach` | Breaks up outer halo/aura reach and brightness in stable local space | It is not animated noise and does not affect inner glow. |
| `Halo / aura breathing` | Breathes outward halo and flowing aura | It is separate from whole-element Breathing Pulse and global bloom breathing. |
| `Flowing edge aura` | Makes moving edge-energy crests emit outside the panel | It requires animated edge-energy participation but does not require ordinary outward glow. |
| `Gaussian distance falloff` | Changes how the outward halo decays across its authored width | It is not a captured or convolved shadow. |
| `Bloom` | Screen-space spread from bright rendered HUD pixels | It is a separate later-stage effect and can make edge energy or halo appear larger. |
| `Soft edge` | Fades the panel surface beyond its contour | It is not emitted light. |

## Recommended test sequence

### 1. Preflight

1. Fully restart Stationeers after rebuilding `uia_effects.bundle`; F6 does not reliably replace
   an already-loaded asset bundle.
2. In `F9 > Diagnostics`, confirm the effects bundle is ready and the analytic panel shader says
   `READY`, not `FALLBACK MESH`.
3. Duplicate the profile so destructive bulk tests do not alter the preferred layout.
4. Use `ALL follow Theme + Effects globals` before judging a global control.

### 2. Establish SDF parity

Toggle `Use analytic SDF glass panels` off and on using the same profile. Compare fill, border
width, unequal corner radii, trapezoid shoulders, border-side masks, feather, soft edge, glow, and
end fades. Panels must never turn into solid parameter grids or disappear.

### 3. Stress the contour and warp

- Move the corner exponent from 2 through 8 on large panels with unequal radii.
- Test Flat and VertexWarp from zero through strong curvature.
- Look for an interior X, quadrant seams, corner popping, grid-cell seams, diagonal edge-weight
  changes, or changing border/halo thickness.
- In CurvedWorldCanvas, toggle SDF and glass tiers live. Panels must remain in front of the world.
- Open F10 while CurvedWorldCanvas is active; radial appearance must remain unchanged.

### 4. Isolate edge energy

- Disable bloom and halo first.
- Vary light angle, edge strength, irregular energy, frequency, and flow speed.
- `Flow speed = 0` should freeze the pattern.
- Watch corners for cracks or jumps and Diagnostics for repeated SDF mesh rebuilds.

### 5. Isolate halo

- Confirm the element is `Follow global theme + effects`, or test its local Custom controls.
- Confirm `Enable surface and edge effects` is on and choose an element with a visible panel
  background. Readout/compass-style widgets disable their panel when the box is off and cannot
  draw glow. Moodlet pills are different: their unboxed mode makes the chrome transparent without
  disabling the pill graphic, so an intentionally configured halo can remain visible.
- Do not use a freeform pen `Shape` as the halo reference. Its current polygon renderer does not
  implement inner or outward halo; its popup now states that limitation instead of exposing dead
  halo sliders.
- Disable bloom so the authored halo can be seen directly.
- Set outward and inward strength to zero, then raise one at a time.
- Test width at a visible nonzero strength.
- Compare diffuseness 0 and 1; expect a tighter/brighter rim versus a broader/lower haze.
- Raise radius from the legacy 160 px limit through 320 px on sparse and dense layouts. Watch for
  rectangular clipping, excessive wash, and GPU cost.
- Raise extended haze with a stable core strength. It should lengthen the faint tail without
  erasing the bright rim.
- Toggle unevenness at amount 0 and 1 on large rounded and trapezoid panels. Expect organic,
  continuous breakup with no corner seam, screen-space swimming, or hard outer rectangle.
- Enable halo/aura breathing and compare it with whole-element Breathing Pulse. Only the outer
  halo/aura should breathe, while all participating panels remain phase-coherent.
- With ordinary Glow still off, enable Flowing Edge Aura plus unevenness and breathing. Both shared
  envelope modifiers must remain visible and editable without turning Glow on.
- Set ordinary outward strength to zero, enable animated edge energy plus Flowing Edge Aura, and
  vary irregular energy, frequency, and flow speed. Bright aura regions should follow the same
  broad moving wave as the border; flow speed 0 should freeze both.
- Compare cheap and Gaussian-distance falloff at equal values. Only the decay character should
  change.
- Repeat with overlapping panels to assess wash and GPU overdraw.

### 6. Test frost and chroma

- In Flat or VertexWarp, compare frost depth 0, 0.33, 0.66, and 1.
- Check for a useful shallow-to-deep progression, correct orientation, no black/stale textures,
  and no helmet X-ray.
- Raise chroma from subtle to extreme and inspect corners, centers, screen edges, and flipped-UV
  modes for an X, inverted channels, or sampling streaks.

### 7. Test inheritance and history separately

- With all elements following global, change palette, glass, halo, frost, and motion and verify
  hidden tiers follow when made visible.
- Switch one element to Custom, edit every supported effect, switch to Global, then back to
  Custom. The saved Custom design should return.
- While that saved Custom design is dormant under Global, run Reset Effects and switch back to
  Custom. It should seed one coherent current-global snapshot; no old enable flags or defaulted
  halo amounts should survive as a partial design.
- Test `Snapshot ALL as Custom`, `Make flat`, and effect reset.
- Interrupt slider, color, text, and canvas gestures with tab changes, profile changes, Undo,
  Redo, Delete, Duplicate, arrow nudges, and F9 close. One gesture should equal one undo step and
  autosave should return to `Saved`.

### 8. Lifecycle and performance

- Resize, change resolution, toggle frost, and perform two F6 reloads.
- Stress overlapping large halos, deep frost, chroma, Gaussian falloff, and bloom.
- Look for stale/black textures, native texture warnings, leaked materials or callbacks, and
  rising CPU/GPU time.

## Known boundaries

- F10 radial authoring and rendering were not changed by this branch.
- Freeform pen polygons still use their existing polygon renderer. That renderer currently ignores
  inner/outward halo values, so the F9 popup suppresses those unsupported controls.
- Optional-background widgets such as readouts or compass elements only show a panel halo while
  their box/background is actually enabled; F9 reports that inactive state inline. Moodlet pills
  retain a live transparent surface in unboxed mode and are deliberately exempt from this warning.
- An ancestor `RectMask2D` can clip an outward halo at the mask boundary. The panel does not clip
  the halo to its own RectTransform.
- Haze, breathing, unevenness, flowing aura, and the 160–320 px radius extension are analytic-SDF
  features. The mesh fallback retains its established static halo and caps its reach at 160 px.
- The legacy renderer cannot represent every independent Custom optical strength exactly; its
  degraded behavior is intentionally approximate.
- A compile-clean shader is not a performance result. Transparent overdraw and texture sampling
  must be measured in representative HUD layouts.
- The generated shader bundle is ignored and shared between branches. Rebuild it and restart the
  game before a fair branch comparison.
