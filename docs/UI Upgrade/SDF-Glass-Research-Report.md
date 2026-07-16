# SDF Panel Shader & Next-Gen Glass — Deep Research Report

**Date:** 2026-07-16
**Produced by:** multi-agent deep-research workflow (105 agents: 5 search angles → 24 sources
fetched → 118 claims extracted → top 25 adversarially verified by 3 independent judges each).
The final synthesis agent was killed twice by Anthropic server outages, so this report was
synthesized by the main session directly from the verified-claim record.

**Confidence labels used throughout:**
- **[VERIFIED]** — survived 3-vote adversarial verification (all 25 passed; one at 2-1, noted).
- **[EXTRACTED]** — pulled from a fetched source with a supporting quote, but not run through
  the adversarial pass.
- **[MY ASSESSMENT]** — my recommendation or inference; not a sourced fact.

---

## TL;DR

1. **The SDF panel shader is not a research risk — it is a recipe.** Every ingredient is
   published, production-proven, and verified: the exact per-corner-radius rounded-box
   distance function, the UGUI parameter-packing scheme (extra UV channels + one shared
   material), the antialiasing formula, and border/glow/soft-edge as one-line distance bands.
   A shipped MIT-licensed Unity plugin proves the whole stack works in UGUI.
2. **The antialiasing-under-VisorWarp question has a clean answer**: screen-space-derivative
   AA (`fwidth`) self-corrects under arbitrary geometric transforms including full 3D
   perspective, because it measures the field's rate of change per *screen* pixel. The known
   pitfalls (2×2-quad granularity, diagonal bias, API differences) all have cheap documented
   fixes.
3. **Our bowtie has a famous cousin.** Raph Levien (Xi/Ghostscript author) documents that even
   a *mathematically perfect* rounded-rect distance field develops a visible **interior "X"
   structure** when you run a large blur/glow over it — the fix is a superellipse (squircle)
   exponent. The X we fought in vertex land re-emerges in analytic land for big soft glows,
   and the literature already solved it.
4. **Edge light/ripple stops being limiting.** In an SDF shader, the edge band is a per-pixel
   *function* — animate it with flow-mapped noise, scrolling phase, per-pixel width, colour
   ramps, all driven by a time uniform with **zero** canvas rebuild cost. This is the direct
   upgrade for the current vertex-painted ripple/spec.
5. **Our shipped frost is literature-validated.** The Tier C backdrop system (`HudBackdrop`:
   capture the final frame once, dual-Kawase pyramid, publish a global texture all panels
   sample) is exactly the architecture the sources describe, and dual-Kawase is the
   measured-fastest blur for it (≈15× faster than Gaussian). The research adds only two
   incremental upgrades: publish multiple pyramid *levels* so frost depth can vary per panel
   (lerp between levels) instead of one global strength, and cheap chromatic fringe taps.
6. **Halo glow can come back** — as Evan Wallace's closed-form analytic Gaussian (used by
   Figma and Zed), computed entirely in the fragment shader with no extra geometry and no
   neighbour sampling. Combined with the squircle fix, this restores the look we lost without
   the geometry that caused the X.

---

# PART A — The SDF panel shader (implementation-grade)

### What it replaces (recap of the architecture discussion)

The paint, not the plumbing: `PanelGraphic`'s per-column colour stops, baked AA feather,
inner-glow/halo bands, sheen/spec vertex painting, and the entire dense-interior bowtie
apparatus become per-pixel math on a simple warp-subdivided quad. The document system, F9
designer, VisorWarp, bloom, and the current mesh renderer (as the fail-soft fallback when the
effects bundle is absent) all stay.

## A1. The shape functions — verbatim, verified

**[VERIFIED 3-0]** Inigo Quilez's catalogue gives the exact closed-form rounded rectangle
with **independent per-corner radii** — two sign-selects collapse a `vec4` of radii to the
active corner before the standard box distance
([iquilezles.org/articles/distfunctions2d](https://iquilezles.org/articles/distfunctions2d/)):

```glsl
float sdRoundedBox( in vec2 p, in vec2 b, in vec4 r ) {
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}
```

**[VERIFIED 3-0]** The same catalogue has closed forms for the other shape families we need:
`sdTrapezoid(p, r1, r2, he)` (two half-widths + half-height — our trapezoid insets, exact),
`sdBezier(pos, A, B, C)` (quadratic Bézier segment), and `sdPolygon(v[N], p)` (arbitrary
closed polygon). Arbitrary panel outlines *can* be evaluated per-pixel instead of tessellated.

**[VERIFIED 3-0]** Two one-liners generate whole effect families from any base SDF:
`d - r` rounds a shape; `abs(d) - r` makes an annular ring (the "onion" operator). Borders,
inner/outer glow bands, and soft edges are all built from these.

**[VERIFIED 3-0]** Quilez also publishes **analytic distance-AND-gradient** variants
(`sdgBox` etc., [distgradfunctions2d](https://iquilezles.org/articles/distgradfunctions2d/))
where the gradient costs almost nothing because it shares intermediate terms — an escape
hatch giving exact per-pixel derivatives *without* `fwidth`, if screen-space derivatives ever
prove unreliable under our warp.

**[VERIFIED 3-0 / 2-1]** Valve's SIGGRAPH 2007 paper (Green, "Improved Alpha-Tested
Magnification") is the canonical ancestor: distance stored with edge at threshold (the 2-1
vote was only about the 0.5-encoding detail, which applies to *texture-baked* SDFs, not our
analytic case), smoothstep between two distance thresholds for AA, and — critically —
**outlines, glows, and drop shadows implemented as functions of the same distance value,
dynamically animatable via shader constants**. That 2007 sentence is the license for
everything in Part B item 2.

## A2. Parameter packing — one material, many shapes

The goal: every panel, whatever its size/radii/border/glow settings, renders with ONE shared
material so UGUI batches them.

**[VERIFIED 3-0]** The UGUI-specific recipe (Daniel Goodnow,
[danielgoodnow.com/blog/unity-sdf-ui](https://danielgoodnow.com/blog/unity-sdf-ui/)): pack
width/height/borderWidth into a `float4` in **TEXCOORD1** and the four corner radii into
**TEXCOORD2**, written identically to all four verts in `OnPopulateMesh`.

**[VERIFIED 3-0]** Hard configuration requirement: the Canvas must have **Additional Shader
Channels** set to include TexCoord1/TexCoord2 or the data never reaches the shader. We create
our own HUD canvas in `HudSystem`, so this is one line at canvas creation
(`canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | ...TexCoord2`).

**[VERIFIED 3-0]** Unity's own batching rules confirm why this matters: UGUI batches elements
sharing the **same material AND texture** in hierarchy order; an overlapping element with a
different material breaks the batch ([Unity, Optimizing Unity UI](https://learn.unity.com/tutorial/optimizing-unity-ui)).
Per-element `MaterialPropertyBlock`-style instances would destroy batching; per-vertex packing
preserves it.

**[VERIFIED 3-0, negative example]** The existing MIT Unity plugin
([TLabAltoh/Unity-SDF-UI-Toolkit](https://github.com/TLabAltoh/Unity-SDF-UI-Toolkit)) proves
the whole SDF-in-UGUI stack ships — but it puts parameters at the *material* level and can
only batch identically-styled elements. We should do better than the reference plugin by
packing per-vertex, exactly as Goodnow does.

**[EXTRACTED]** Production precedents for the packing pattern, across three independent UI
renderers: Zed's GPUI packs origin/size/colour/radius as flat per-instance attributes
([zed.dev/blog/videogame](https://zed.dev/blog/videogame)); Firefox WebRender packs each
primitive into a single `ivec4` of offsets/flags and draws instanced unit quads
([nical.github.io](https://nical.github.io/drafts/gui-gpu-notes.html)); Warp's terminal
renderer passes per-rect uniform structs. Convergent evolution — this is simply how modern UI
renderers work.

**[MY ASSESSMENT]** Our channel budget: `uv0` already carries FxStrength/marker; UV1 + UV2
give 8 floats for half-size, 4 radii, border width, softness; colour + a third channel
(TexCoord3, also enableable) cover fill/glow tints and animation phase. Shape *type* can ride
in a spare component and branch in the fragment shader — **[VERIFIED 3-0]** the
type-as-normalized-vertex-attribute branching pattern is documented working practice
([randygaul.github.io](https://randygaul.github.io/graphics/2025/03/04/2D-Rendering-SDF-and-Atlases.html)).

## A3. Antialiasing on warped quads — the question that decides feasibility

This was the make-or-break unknown: VisorWarp bends vertices non-affinely, and the SDF is
evaluated from *interpolated local coordinates*. Does AA survive?

**Answer: yes, with the L2 form of derivative AA. The verified facts:**

- **[VERIFIED as central, multiple sources]** Screen-space-derivative AA is *self-correcting
  under geometric transforms*: "fwidth(d) is the rate of change of the signed distance **as
  seen from the flat pixel screen perspective**"
  ([pkh.me](https://blog.pkh.me/p/44-perfecting-anti-aliasing-on-signed-distance-functions.html));
  "It properly respects any transformations we might throw at it, **including 3D
  perspective**" ([frost.kiwi analytical AA](https://blog.frost.kiwi/analytical-anti-aliasing/)).
  The pixel-footprint compensation is automatic — precisely what a warped quad needs.
- The recommended base formula **[EXTRACTED, pkh.me]**:
  `coverage = clamp(0.5 + d / fwidth(d), 0.0, 1.0)` — a centred linear ramp, correct in 2D
  and 3D.

**The pitfalls, each with a documented fix:**

| Pitfall | Source | Fix |
|---|---|---|
| `fwidth` = L1 norm → diagonal bias, circles go slightly rhombous | frost.kiwi (Freya Holmér's Shapes calls the fixed mode "Corrected LAA") | use `length(vec2(ddx(d), ddy(d)))` — slightly costlier, exact |
| Derivatives computed per 2×2 pixel quad, not per pixel | julhe.github.io, bgolus | take derivatives of the *smoothly-varying* input (local pos / d), never of a post-threshold mask; quad-granularity error on a smooth d is sub-pixel |
| DirectX mandates coarse derivatives; GL leaves it vendor-chosen | bgolus | same rule as above; we ship D3D11-only in practice, so behaviour is uniform |
| Non-uniform stretch breaks the unit-gradient (Eikonal) property of the SDF | pkh.me | evaluate the SDF in *unstretched local pixels* (pass true half-size, don't normalize into a stretched 0-1 UV); Jacobian-determinant footprint (julhe) or analytic gradients (iq sdgBox) as the heavy-duty fallback |

**[MY ASSESSMENT — the warp verdict]** VisorWarp is mild (barrel k ≈ ±0.2 range), and our
meshes are already subdivided ~48px for the warp. Within one subdivided cell the warp is
near-affine, so interpolated local coordinates carry sub-pixel error, and the screen-space
derivative AA absorbs the rest by construction. Keep the existing subdivision, use the L2
formula, take derivatives of the local position (not the mask), and this is a solved problem.
If artifacts ever show at extreme curvature, the two documented escalations (Jacobian
footprint, analytic gradient) are drop-in.

## A4. Borders, glows, soft edges, sheen — distance bands

**[VERIFIED 3-0]** Valve 2007: outline = recolour between two distance thresholds; glow =
smoothstep over the outside band; drop shadow = second evaluation at an offset; all
parameters (width, colour, direction, opacity) animatable via shader constants.

**[VERIFIED 3-0]** Goodnow's UGUI shader shows the concrete band construction:
`color.a *= 1 - smoothstep(-_Softness, 0, d)` for the soft edge and a
`[-borderWidth, -(borderWidth+_Softness)]` band for the border. (He uses fixed softness; we
should use the derivative width from A3 instead.)

**Our specific effects map 1:1:**

| Current (vertex-painted) | SDF equivalent |
|---|---|
| Border-in / border-solid / fade stops | one smoothstep band over `abs(d + bw/2) - bw/2` |
| Sheen (quadratic in y) | evaluated per-pixel from local y — smooth at any size |
| Spec / BorderLight (directional) | per-pixel: `dot(normalize(grad d), lightDir)` weighting on the border band — with analytic or derivative gradient, this becomes *true* directional edge lighting instead of a vertex approximation |
| Inner glow band | smoothstep band just inside d=0 |
| Outer halo | see the two options below |
| SoftEdge / HudEdgeFade end-fade | multiply coverage by a per-pixel ramp in local x — two more packed params |
| EdgeRipple | Part B item 2 — the big upgrade |

**Outer halo, two grades:**
- Cheap: smoothstep band outside d=0 (needs the quad expanded by halo width — geometry we
  control in `OnPopulateMesh`).
- Beautiful: **[EXTRACTED]** Evan Wallace's (Figma) closed-form Gaussian blur of a rounded
  rect — exact `erf` integral along one axis, 4-sample numeric integration along the other,
  entirely in-fragment, no neighbour sampling. Zed uses it in production for shadows
  ([zed.dev/blog/videogame](https://zed.dev/blog/videogame),
  [raphlinus.github.io](https://raphlinus.github.io/graphics/2020/04/21/blurred-rounded-rects.html)).

**⚠ The X returns — and its published fix. [EXTRACTED, raphlinus]** A *pure* rounded-rect
distance field under a large blur exhibits "sharp interior corners (which generate a visible
'x' structure) and abrupt straight-to-curved transitions". This is our bowtie reborn as
analytic geometry: the interior distance field creases along the diagonals, and any wide soft
band reveals the crease. The documented fix is a **superellipse/squircle** (raise the corner
exponent), which rounds the interior crease away. We should ship the squircle exponent as a
per-element option both for this reason and because squircle corners simply look more
"designed" (it's the iOS icon curve).

**Banding: [MY ASSESSMENT]** Vertex-colour banding dies because nothing is interpolated
through 8-bit `Color32` any more — bands are computed in float per pixel and quantize only at
framebuffer write. For very dark wide gradients on 8-bit swapchains a 1-LSB blue-noise dither
in the shader is the standard belt-and-braces addition (cheap, one line); not sourced from
the claim set, flagging it as my recommendation.

## A5. Local-space evaluation under the warp — architecture confirmation

**[EXTRACTED, zaggoth.wordpress.com]** The alternative architecture — render the flat HUD to
a texture and warp *the texture* (UE4 Retainer Box, one distortion material; how you'd fake
Halo/Destiny curvature) — is documented, works, and has a confirmed fatal flaw for us: "the
button boundaries will NOT be in the same place", i.e. hit-testing desyncs from visuals
unless input is inverse-warped, plus it requires premultiplied-alpha recomposition and costs
a full-res render target. Our CPU mesh warp keeps geometry, hit-testing, and the F9 editor in
one coordinate story. **[MY ASSESSMENT]** Keep the mesh warp; the SDF rides on it via
interpolated local coordinates as per A3. (Interesting observational note from the same
source: Destiny 2 appears to curve different HUD panels by different amounts for readability
— a per-element curvature-strength knob would be a cheap F9 addition someday.)

## A6. Performance — what actually costs

- **[VERIFIED 3-0]** ALL Canvas geometry renders in the transparent queue, back-to-front,
  alpha-blended — panels pay blend cost even where they look opaque.
- **[VERIFIED 3-0]** Overdraw/fill-rate is the risk dimension, not vertex count: every
  rasterized pixel samples even when covered.
- **[EXTRACTED, Unity UI Toolkit docs]** A heavier per-pixel "uber shader" amplifies the cost
  of each overlapping translucent layer — the SDF shader makes *overlap* costlier while
  making *geometry* nearly free.
- **[VERIFIED 3-0, the sleeper win]** "Whenever any drawable UI element on a given Canvas
  changes, the Canvas must re-run the batch building process." Animated effects must be
  driven by **time uniforms in the shader, not per-frame vertex dirtying**. Today, ripple
  animation re-paints vertex colours; post-SDF, animation costs zero CPU and zero canvas
  rebuild.
- **[EXTRACTED, WebRender]** The heavy-duty mitigation if we ever need it: split panels into
  an opaque interior drawn front-to-back with z, plus blended edges. Noted for completeness;
  our panel count is small enough that this is premature.

**[MY ASSESSMENT]** Net for our HUD (a dozen-odd panels, desktop GPUs): the SDF path wins.
Thousands of interior verts per glassy panel disappear; the fragment cost rises modestly on
panel-covered pixels only; the per-frame CPU cost of animated vertex repainting goes to zero.
The one behaviour to keep an eye on is stacking several full-width translucent panels.

## Arbitrary shapes (the pen tool) in SDF land

- **[VERIFIED 3-0]** `sdPolygon` evaluates any closed polygon per-pixel (O(n) loop per pixel).
- **[EXTRACTED]** TLabAltoh's toolkit does runtime splines via a `StructuredBuffer` of control
  points (D3D11-fine; their only noted constraint is WebGL, irrelevant to us), and offers an
  edit-time bake path: cubic→quadratic Bézier conversion (Google Fonts' cu2qu algorithm) into
  an SDF texture asset.
- **[MY ASSESSMENT]** Phase the pen-tool `Shape` renderer *after* the box shader ships: keep
  `PolygonPanelGraphic` (ear-clip mesh) as-is initially, then evaluate StructuredBuffer
  polygon SDF (nicest: true glass parity with boxes) vs baked SDF textures (cheapest at high
  point counts). Not on the critical path.

---

# PART B — Beyond SDF: the ranked menu

Ranked by payoff-per-effort *for our specific concept-art gap* (deep blended gradients,
luminous animated edges, frosted translucency).

## 1. The SDF panel shader itself — the centerpiece

Covered in Part A. **Goal:** banding-free deep gradients, resolution-independent corners,
true directional edge light, restored halo. **Effort:** M–L (a version-sized project).
**Risk:** low — every component verified; mesh path stays as fail-soft fallback per house
rules. **Payoff:** the single largest step toward the comps.

## 2. Shader-time animated edge energy — the edge-light/ripple fix

**Goal:** the "luminous animated edges" in the comps — energy that flows along the border,
shimmers, desynchronizes organically — instead of a fixed sine ripple in vertex colours.

**Confirmed technique set:**
- **[VERIFIED 3-0, catlikecoding.com]** Flow-map animation: `uv - flowVector * time`; reset
  drift with a sawtooth (`frac(time)`); hide the reset pulse by cross-fading two half-period-
  offset samples with a triangle-wave weight `w(p) = 1 - |1 - 2p|`; kill the global-pulse
  artifact by packing per-texel noise in the flow map's alpha as a time offset — the pulse
  becomes a wave that spreads across the surface.
- **[VERIFIED 3-0, danielilett.com energy-shield]** Edge glow as a *fragment-space
  distance-band* shaped by smoothstep, with scrolling procedural noise driving the intensity
  — the exact precursor pattern; in our case the band coordinate is the SDF distance and the
  along-edge coordinate parameterizes the flow.
- **[VERIFIED 3-0, Unity]** Because it's all time-uniform-driven, none of it dirties the
  canvas (A6).

**In-mod route:** once the SDF shader exists, edge energy = (noise texture or cheap hash
noise) sampled along the border band, flow-mapped with the catlikecoding loop, multiplied
into the border/glow band colour. Per-element speed/width/intensity ride the packed params;
global time is a material uniform. **Effort:** S (given #1). **Risk:** low.
**This is the direct answer to "edge light/ripple feels very limiting".**

## 3. Frosted backdrop blur — ALREADY SHIPPED (Tier C); research validates it, plus two upgrades

**Status correction:** the mod already has this. `HudBackdrop` (Tier C, 0.9.0) captures the
final rendered frame via a last-ordered `OnRenderImage` hook on the camera that actually sees
the finished image (the helmet's ForegroundCamera — main-camera capture X-rayed the visor,
play-test round 12), runs a dual-Kawase pyramid, and publishes the blurred result as the
global `_UiaBlurTex` that the frost material on `PanelGraphic` samples by screen position.
That is precisely the capture-once-share-all architecture the sources describe.

**What the research confirms about our existing choices:**
- **[VERIFIED 3-0, ARM SIGGRAPH 2015 (Bjørge)]** Dual-Kawase/dual-filter is the
  measured-fastest large blur: 2.8 ms at 1080p on a *2015 mobile GPU* vs 41.9 ms full
  Gaussian (~15×) — our kernel choice is the literature's winner. The same deck endorses
  threshold→blur→composite mixed-resolution bloom ("wide + thin") — `HudBloomFx` too is
  literature-blessed.
- **[VERIFIED 3-0, andydbc + Doppelkeks]** One shared capture feeding all glass (vs
  per-object GrabPass) is the established pattern. These repos use a CommandBuffer at
  `BeforeForwardAlpha`; we deliberately use `OnRenderImage` instead — the Beef-proven
  mechanism on this exact camera after CommandBuffer/back-buffer readback proved unreliable
  across GPUs (documented in `HudBackdrop`). The play-tested decision stands; the research
  does not overturn it.
- **[EXTRACTED, negative]** The popular glassmorphism tutorials are URP-only — there was no
  easier route we missed.

**The two genuinely new upgrades on offer:**
1. **Per-panel frost depth** **[VERIFIED 3-0, andydbc]**: publish 2–4 pyramid *levels* as
   globals (`_UiaBlurTex0..3` — we already build the intermediate levels, they're just not
   exposed) and let each panel lerp between them by its frost param — variable frost per
   panel, even per pixel across one panel, from the same single capture. Today strength is
   effectively global.
2. **Chromatic fringe** (item 6) — extra taps of the already-resident texture.

**Effort:** S (the pyramid already exists; this is exposure + a shader lerp). **Risk:** low.

## 4. Analytic Gaussian halo (the glow that doesn't X)

**Goal:** restore the outer halo we turned off, with mathematically smooth falloff.
**[EXTRACTED]** Evan Wallace's erf-based closed-form Gaussian of a rounded rect (production:
Figma, Zed) — no geometry skirt, no neighbour taps. **⚠** must pair with the squircle
exponent, or the analytic X-structure appears under wide blurs (raphlinus, A4). **Effort:** S
(a function inside shader #1). **Risk:** low. **[MY ASSESSMENT]** Between this and bloom
band 2, halo becomes a per-element *choice* again rather than a banned effect.

## 5. Squircle / superellipse corners

**Goal:** kill the interior-crease X under any wide soft band; nicer "designed" corner
character. **[EXTRACTED, raphlinus]** Increasing the corner exponent "clearly solves … the
sharp interior corners (which generate a visible 'x' structure) and the abrupt
straight-to-curved transitions". **Effort:** XS inside shader #1 (exponent as a packed param;
default 2.0 = current look). **Risk:** none.

## 6. Chromatic fringe on frost — cheap "expensive glass" tell

**[MY ASSESSMENT — recommendation, not sourced from the claim set]** The blurred backdrop
already exists (Tier C's `_UiaBlurTex`): sample it three times with tiny per-channel UV
offsets scaled by distance-from-centre (and/or the SDF gradient direction at the edge) and
it reads as dispersion/refraction. Three taps of an already-resident texture. **Effort:** XS.
**Risk:** taste — keep subtle.
True thin-film iridescence: no verified sources surfaced; park it.

## 7. Premultiplied alpha / dual-source blending

**[EXTRACTED, zaggoth]** Premultiplied-alpha compositing (UE4 "Alpha Composite") is what
fixes edge jaggies when *recompositing UI from a render texture* — relevant only if we ever
adopt an RT stage. **[MY ASSESSMENT]** Direct-to-backbuffer UGUI doesn't need the switch;
dual-source blending is exotic per-API plumbing with no verified payoff for us. **Skip both**
unless an RT compositing stage appears.

## 8. Shipped-game curvature presentation — confirmation, not change

**[EXTRACTED, zaggoth]** The RT-warp technique (Halo/Destiny look) confirms our CPU mesh warp
was the right call for an *interactive, editor-driven* HUD (hit-testing stays true). The
Cyberpunk 2077 GDC talk is paywalled beyond its abstract **[EXTRACTED]** — confirmed only:
in-house UI engine, thousands of diegetic instances, heavy perf-budget discipline. Nothing
actionable beyond "respect fill rate", which A6 already encodes. Optional cheap idea from the
Destiny observation: per-element curvature-strength multiplier in F9.

---

# Recommended sequencing

**[MY ASSESSMENT]**

1. **Phase 1 — `HudPanelSdf.shader` in the UiaEffectsBundle** (rounded box + per-corner radii
   + squircle exponent + trapezoid insets; fill/sheen/spec/border/inner-glow/soft-edge/
   end-fade as distance bands; L2 derivative AA; params packed UV1/UV2/UV3). New
   `SdfPanelGraphic : MaskableGraphic, IGlassSurface` emitting the warp-subdivided quad with
   packed UVs; `HudElementView.ApplyGlass` targets it via the existing `IGlassSurface`
   seam; enable Additional Shader Channels on our canvas; bundle-absent ⇒ current
   `PanelGraphic` (fail-soft, unchanged).
2. **Phase 2 — edge energy** (flow-mapped noise on the border band; F9 sliders reuse the
   existing ripple/glow params plus speed/scale).
3. **Phase 3 — frost upgrades (optional; frost itself already ships as Tier C)**: expose the
   existing `HudBackdrop` pyramid levels as multiple globals for per-panel frost depth, and
   add chromatic-fringe taps in the panel shader.
4. **Later** — analytic-Gaussian halo option, pen-tool Shape SDF path, per-element curvature
   multiplier.

Each phase is independently shippable and play-testable; Phase 1 alone removes the entire
class of vertex-interpolation artifacts on the primary path.

---

# Sources

**Adversarially verified (claims above marked VERIFIED):**
[Inigo Quilez, 2D distance functions](https://iquilezles.org/articles/distfunctions2d/) ·
[Quilez, distance+gradient functions](https://iquilezles.org/articles/distgradfunctions2d/) ·
[Green (Valve), SIGGRAPH 2007](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) ([ACM](https://dl.acm.org/doi/10.1145/1281500.1281665)) ·
[Bjørge (ARM), SIGGRAPH 2015 mobile bandwidth slides](https://community.arm.com/cfs-file/__key/communityserver-blogs-components-weblogfiles/00-00-00-20-66/siggraph2015_2D00_mmg_2D00_marius_2D00_slides.pdf) ·
[Unity, Optimizing Unity UI](https://learn.unity.com/tutorial/optimizing-unity-ui) ·
[Goodnow, SDF-based UI in Unity](https://danielgoodnow.com/blog/unity-sdf-ui/) ·
[TLabAltoh, Unity-SDF-UI-Toolkit](https://github.com/TLabAltoh/Unity-SDF-UI-Toolkit) ·
[andydbc, unity-frosted-glass](https://github.com/andydbc/unity-frosted-glass) ·
[Doppelkeks, Unity-CommandBufferRefraction](https://github.com/Doppelkeks/Unity-CommandBufferRefraction) ·
[Catlike Coding, Texture Distortion](https://catlikecoding.com/unity/tutorials/flow/texture-distortion/) ·
[Ilett, Energy Shield](https://danielilett.com/2023-02-09-tut6-3-energy-shield/) ·
[randygaul, 2D rendering with SDFs](https://randygaul.github.io/graphics/2025/03/04/2D-Rendering-SDF-and-Atlases.html) ·
[Levien, blurred rounded rects](https://raphlinus.github.io/graphics/2020/04/21/blurred-rounded-rects.html)

**Extracted (fetched + quoted, not adversarially verified):**
[pkh.me, perfecting SDF AA](https://blog.pkh.me/p/44-perfecting-anti-aliasing-on-signed-distance-functions.html) ·
[frost.kiwi, analytical AA](https://blog.frost.kiwi/analytical-anti-aliasing/) ·
[julhe, always-sharp SDF textures](https://julhe.github.io/posts/always_sharp_sdf_textures/) ·
[Golus, derivative differences](https://bgolus.medium.com/distinctive-derivative-differences-cce38d36797b) ·
[Zed, videogame-style rendering](https://zed.dev/blog/videogame) ·
[nical, GPU GUI notes (WebRender)](https://nical.github.io/drafts/gui-gpu-notes.html) ·
[Baedrick, Dual-Kawase demo (URP)](https://github.com/Baedrick/Dual-Kawase-Blur-Demo) ·
[zaggoth, UE4 curved HUD](https://zaggoth.wordpress.com/2018/11/08/ue4-tutorial-creating-a-parabolic-curved-2d-widget-hud-like-halo-destiny/) ·
[GDC 2023, Cyberpunk 2077 UI (abstract only)](https://gdcvault.com/play/1034160/User-Interface-in-Cyberpunk-2077) ·
[chiewjh, glassmorphism in Unity (URP-only)](https://medium.com/@chiewjh2009/glassmorphism-ui-in-unity-9b563293ed11)

**Method:** 5 parallel search angles → 24 sources fetched with quote extraction (118 claims)
→ top-25 claims each judged by 3 independent adversarial verifiers (kill on 2/3 refute):
25/25 confirmed, 24 at 3-0, one at 2-1. Raw record:
`subagents/workflows/wf_23ab09a0-0dc/journal.jsonl` in the session directory.
