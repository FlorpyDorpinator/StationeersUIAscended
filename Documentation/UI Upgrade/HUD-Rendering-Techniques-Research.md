# HUD Rendering Techniques — literature research (SDF crispness, glass, legibility)

**Purpose:** a durable, sourced record of the real-time-rendering literature relevant to the
**Stationeers UI Ascended** visor HUD, so future work (and future LLM sessions) can trace every
claim to a primary source and read the papers directly. Companion to
[`Glassy-HUD-Effects-Research.md`](Glassy-HUD-Effects-Research.md) (which covers the frosted-glass
backdrop-blur pipeline in depth).

**Method:** deep-research harness — fanned-out web search across the requested venues (Advances in
Real-Time Rendering, SIGGRAPH, JCGT, Eurographics, HPG, NVIDIA, AMD GPUOpen, arXiv cs.GR, Scholar/
Semantic Scholar + production write-ups) → 23 sources fetched → 109 claims extracted → **25 verified
by 3-vote adversarial checking (23 confirmed, 2 refuted).** Confidence and vote counts are recorded
per finding below. **Confirmed** = attributable to the cited source; **Recommendation** = our
translation to the mod, not claimed by the source.

**Engine target:** Unity **2022.3.7f1**, **Built-in Render Pipeline**, **gamma** color space, no SRP,
**UGUI/Canvas**. Client-side BepInEx/StationeersLaunchPad mod — cosmetic, display-only, MP-safe.

**Already implemented in the mod** (so these findings are the *net-new* ideas): procedural
rounded-rect panels with baked-fringe AA, mesh sheen + directional edge-light, per-corner radii,
trapezoid insets, stroked polylines with arc-length end-fade, a per-graphic vertex mesh-warp for
visor curvature, procedural line glyphs, a HUD animator.

---

## 1. Analytic 2D SDF shapes — resolution-independent, one-line rounding & outlining
- **Confidence:** high · **Vote:** 3-0
- **Visual goal:** crisp, non-rectangular HUD shapes at any scale/curvature; hairline outlines and
  per-corner rounding from a distance field rather than built geometry.
- **Confirmed:** Inigo Quilez's catalog gives exact fragment-shader SDFs for 50 primitives (box,
  rounded box, oriented box, segment/line, arc, ellipse, quadratic Bézier, trapezoid, ring, pie,
  vesica…). **Any exact SDF rounds by `sdf - r`** (one radius parameter) and becomes a **crisp hollow
  outline/stroke of half-width r by `abs(sdf) - r`** (`opRound` / `opOnion`, stated verbatim).
- **Recommendation / translation:** a single SDF shader family (a `MaskableGraphic` drawing a quad +
  fragment SDF) could render every framed element, per-corner radii, *and* hairline outlines —
  a cleaner successor to the current `PanelGraphic` mesh + `PolylineGraphic` stroking.
- **Risks:** rounding/outlining are exact only on a *true Euclidean* SDF (approximate/mitred fields
  keep sharp corners); `sdf - r` inflates outward, so shrink the source by r to preserve size. It's a
  shader rewrite (bigger lift than tuning). fwidth AA blends ideally in linear, not gamma.
- **Source:** Inigo Quilez, *2D distance functions* — https://iquilezles.org/articles/distfunctions2d/

## 2. Multi-channel SDF (MSDF) icon/glyph atlases — crisp corners when scaled
- **Confidence:** high · **Vote:** 3-0
- **Visual goal:** razor-crisp status icons/line-glyphs at any HUD size without corner-rounding.
- **Confirmed:** single-channel SDF and soft-mask atlases round corners (bilinear interpolation only
  reconstructs distance where the gradient is constant); **MSDF encodes edges across RGB and takes a
  median-of-three in the shader to rebuild sharp corners** at low texture resolution. The
  msdf-atlas-gen comparison table lists "sharp corners preserved on upscale" only for MSDF/MTSDF.
- **Recommendation:** generate an **MSDF (or MTSDF if glow/shadow is layered on) icon atlas** for the
  HUD's status glyphs.
- **Risks:** median artifacts at very small on-screen sizes → may force MTSDF; adds an atlas asset +
  a sampling shader.
- **Sources:** Chlumský, *msdf-atlas-gen* — https://github.com/Chlumsky/msdf-atlas-gen ·
  Chlumský, Sloup & Šimeček, *Improved Corners with Multi-Channel Signed Distance Fields*,
  Computer Graphics Forum 37(1), 2018 (Eurographics/Wiley).

## 3. Analytic anti-aliasing via screen-space derivatives (`fwidth`)
- **Confidence:** high · **Vote:** 3-0
- **Visual goal:** AA that's exactly one pixel wide regardless of resolution, distance, or view angle
  (no fixed feather constant) — MSAA-free.
- **Confirmed:** size the AA band with the per-pixel rate of change of the signed distance. General
  "good enough" coverage recipe: **`clamp(0.5 + d/fwidth(d), 0, 1)`** (≡ `smoothstep(w,-w,d)`,
  `w=fwidth(d)`). Fully supported in Built-in RP 2022.3 gamma-space fragment shaders (SM3.0+).
- **Recommendation:** standardize every SDF/line shader on fwidth-based coverage.
- **Risks:** fwidth is the L1 approximation (`length(vec2(dFdx,dFdy))` is more exact, costlier),
  computed per 2×2 quad; ideal AA blends in linear not gamma (hence "good enough").
- **Sources:** Ben Golus, *The Best Darn Grid Shader (Yet)* —
  https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8 ·
  pkh.me, *Perfecting anti-aliasing on signed distance functions* —
  https://blog.pkh.me/p/44-perfecting-anti-aliasing-on-signed-distance-functions.html
  (lineage: Valve/Green 2007 SDF text; madebyevan; Ronja; numb3r23).

## 4. Phone-wire hairline AA — sub-pixel lines that FADE instead of vanishing ★
- **Confidence:** medium (one authoritative blog; field-standard since 2012) · **Vote:** 3-0
- **Visual goal:** the exact problem in our HUD — thin lines that dissolve smoothly to nothing rather
  than snapping off / flickering at sub-pixel widths.
- **Confirmed:** **never let a line get thinner than one pixel; instead fade its alpha by coverage.**
  ```
  drawWidth = max(lineWidth, fwidth(d));   // stay >= 1px on screen
  alpha    *= saturate(lineWidth / drawWidth);   // a 0.5px wire = 1px @ 0.5 alpha
  ```
- **Recommendation:** apply this to `PolylineGraphic` (drop the hard `Width` 0.5px clamp) and
  `PanelGraphic` border (replace the `bw > 0.05` on/off cutoff). Cheap; no shader rewrite needed —
  works in the existing baked-fringe mesh path by scaling vertex alpha.
- **Risks:** rests on one primary write-up (blog-tier), but the technique is well established and
  unchanged since 2012.
- **Sources:** Ben Golus (above), attributing **Emil Persson / Humus**; Geeks3D "Phone-Wire
  Anti-Aliasing" demo (2012); Humus articles.

## 5. Fast prefiltered lines (GPU Gems 2, Ch. 22) — LUT-based line AA
- **Confidence:** medium (single primary source) · **Vote:** 3-0 (a sub-claim refuted, see §Refuted)
- **Visual goal:** fixed-cost line AA with an arbitrary (e.g. soft-glow) filter profile.
- **Confirmed:** precompute a symmetric AA filter convolved with a line at several distances into a
  ~32-entry **1D lookup texture**; per-pixel cost = a distance eval + a table lookup, independent of
  kernel shape.
- **Recommendation:** an alternative to fwidth AA *only* when you want a specific non-box stroke
  falloff (glow-like) at fixed cost. For a modern UGUI HUD, fwidth AA is usually simpler.
- **⚠️ Refuted extension:** it does **not** inherently make sub-pixel hairlines fade — pair it with §4.
- **Source:** Chan & Durand, *Fast Prefiltered Lines*, GPU Gems 2 Ch. 22 (MIT/NVIDIA) —
  https://developer.nvidia.com/gpugems/gpugems2/part-iii-high-quality-rendering/chapter-22-fast-prefiltered-lines

## 6. Frosted-glass backdrop blur — dual-Kawase / "dual filtering"
- **Confidence:** high · **Vote:** 3-0
- **Visual goal:** cheap near-Gaussian backdrop blur behind glass panels.
- **Confirmed:** alternate a downsample and upsample filter across a resolution pyramid. Near-lossless
  vs Gaussian (**97×97 blur: Dual 49.78 dB PSNR** vs Kawase 50.02 dB) and fastest of all tested
  (Mali-T760 MP8: **Dual 2.8 ms @1080p / 1.7 ms @720p** vs full Gaussian 41.9 ms — ~15×). Intel
  independently: Kawase-family 1.5×–3.0× faster than optimized separable Gaussian. Production standard
  (KDE KWin, GNOME).
- **Recommendation:** **one shared blur pyramid feeding many panels** (see the glass doc for the
  Built-in-RP CommandBuffer capture route, since our HUD canvas is Screen Space – Overlay).
- **Risks:** perf numbers are 2015 mobile hardware — measure on desktop min-spec; needs a
  GrabPass/camera-texture backdrop source; Kawase is a Gaussian *approximation*.
- **Sources:** Marius Bjørge, *Bandwidth-Efficient Rendering* (ARM, SIGGRAPH 2015) —
  https://community.arm.com/cfs-file/__key/communityserver-blogs-components-weblogfiles/00-00-00-20-66/siggraph2015_2D00_mmg_2D00_marius_2D00_slides.pdf ·
  Intel/Strugar, *An Investigation of Fast Real-Time GPU-Based Image Blur Algorithms* —
  https://www.intel.com/content/www/us/en/developer/articles/technical/an-investigation-of-fast-real-time-gpu-based-image-blur-algorithms.html ·
  UWA, *Dual Blur and its implementation in Unity* — https://medium.com/@uwa4d/dual-blur-and-its-implementation-in-unity-c2cd77c90771

## 7. Legibility over the noisy game world — the "billboard" backing plate
- **Confidence:** high · **Vote:** 3-0 (claims 8/9/17); 2-1 (claim 16)
- **Visual goal:** keep HUD text/gauges readable over bright/busy terrain.
- **Confirmed:** an **opaque "billboard" backing plate** behind text was the fastest to read and
  essentially immune to ambient-brightness washout (Gabbard/Swan/Hix 2006, outdoor optical-see-through
  AR, 18 subjects / 7776 trials; reaffirmed by Debernardis TVCG 2014 and a 2024 review). A thin ~1px
  outline helps only marginally; **outline + billboard should NOT be combined** — use one. In AR the
  billboard's occlusion of the see-through world is its drawback — **for an opaque game visor that
  occlusion is a *feature*.**
- **Recommendation:** back critical readouts with an opaque (or near-opaque) plate; don't stack a
  billboard *and* an outline.
- **Risks:** findings are somewhat color-specific (white-on-blue/solid box); "immune to brightness" is
  failure-to-reject, i.e. a mild overstatement.
- **Sources:** Gabbard, Swan & Hix, *The Effects of Text Drawing Styles… in Outdoor AR* (Presence
  2006) — https://www.researchgate.net/publication/220089805 · Debernardis et al., IEEE TVCG 2014
  (PMID 24201331) · 2024 multivocal review — https://link.springer.com/article/10.1007/s10055-024-00949-6 ·
  related HUD legibility (the "Double 007 Rule") — https://www.researchgate.net/publication/352802179

## 8. Concrete, testable contrast rules
- **Confidence:** high · **Vote:** 3-0
- **Visual goal:** enforce readable contrast with numbers you can measure.
- **Confirmed:** targets (Xbox Accessibility Guideline 102, aligned with WCAG 2): **≥4.5:1** standard
  text/important elements, **≥3:1** large text/elements, **≥7:1** high-contrast mode; MS names HUD
  health meters/directional cues/map elements as targets. A **dual black+white outline** guarantees
  contrast over both light *and* dark scenes at once. Over a non-solid background, measure contrast
  against the **worst-case region**, not the average. Counterpoint: for near-eye screens avoid
  *excessive* contrast.
- **Recommendation:** offer a high-contrast HUD mode (≥7:1); use dual outlines on symbols that must
  read over unknown terrain.
- **Risks:** these are "should"-level recommendations.
- **Sources:** Microsoft, *Xbox Accessibility Guideline 102* —
  https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/102 ·
  W3C, *WCAG 2.1 Understanding Contrast (Enhanced)* — https://www.w3.org/WAI/WCAG21/Understanding/contrast-enhanced.html

## 9. Adaptive, background-aware contrast (promising, costlier)
- **Confidence:** medium · **Vote:** 3-0
- **Visual goal:** raise the overlay's *local* contrast against whatever live scene sits behind it.
- **Confirmed:** made real-time-cheap by computing saliency only for the overlay's single
  representative color and searching a small set of brightness levels for the highest-contrast one
  (Ahn, Lee & Kim, *Virtual Reality* 22, 2018).
- **Recommendation:** a per-frame UGUI component sampling scene luminance behind each panel — but in
  Built-in RP this needs a GrabPass/backdrop sample (same cost as the glass blur), so only worth it if
  the backdrop is already captured. The cheaper guaranteed-contrast option is the §7 billboard.
- **Source:** Ahn, Lee & Kim, *Real-time adjustment of contrast saliency…* —
  https://www.researchgate.net/publication/318230523 (DOI 10.1007/s10055-017-0319-y).

---

## Refuted in verification (do not rely on these)
1. **Prefiltered lines "handle sub-pixel lines for free"** — REFUTED **0-3**. The LUT math does not
   itself fade hairlines; pair with the phone-wire clamp (§4).
   Source: GPU Gems 2 Ch. 22 (as misapplied).
2. **Adaptive text *polarity* by ambient brightness** (white-on-dark in dark surroundings vs
   black-on-white in daylight) — REFUTED **1-2**.
   Source: https://www.mdpi.com/2076-3417/13/10/6037

## Gaps — two requested areas returned ZERO verified claims (open)
1. **Animation** — light/shine sweeps along strokes, arc-length-parameterized stroke effects,
   breathing/pulse glows, boot-up/CRT/reveal transitions, easing/procedural motion.
2. **Glass optics beyond blur** — cheap refraction/normal distortion, fresnel/directional rim-light
   gradients tied to element angle, thin-film/iridescence for a UGUI shader.

→ These need a **dedicated second research pass** into motion-design / shader-animation / game-feel
talks rather than academic rendering venues. Candidate leads already surfaced (not yet verified):
easing-function reference (https://blog.febucci.com/2018/08/easing-functions/), thin-film BRDF
(https://belcour.github.io/blog/slides/2017-brdf-thin-film/slides.html), a liquid/glass shader
(https://github.com/Pondot/liquidDX11).

## Open architecture questions (resolve in-engine)
- The concrete Built-in-RP pattern for **one shared backdrop-blur texture consumed by many
  MaskableGraphics** (one capture + dual-Kawase pyramid, sampled by N panels) — gamma-space + Canvas
  draw-order caveats.
- Whether MSDF median artifacts at small HUD icon sizes force MTSDF, and the best batching/instancing
  approach for many small procedural UI meshes (compass ticks, moodlet cells, gauge segments) without
  breaking UGUI dynamic batching.

---

## Recommended adoption order (our opinion, not the sources)
1. **Now, cheap:** phone-wire hairline coverage (§4) in `PolylineGraphic`/`PanelGraphic` — fixes the
   vanishing thin-line problem today; add directional edge-light to polylines for gradient borders.
2. **Next:** prototype **one** SDF `MaskableGraphic` (§1 + §3) for a single element type, compare
   crispness/perf vs the mesh version, migrate element-by-element only if it wins. MSDF icons (§2) in
   parallel.
3. **Glass:** the shared dual-Kawase pyramid (§6) per the glass doc.
4. **Legibility:** billboard backing + high-contrast mode + dual outlines (§7, §8).

---

*Research + synthesis: deep-research harness (105 agents, 3-vote adversarial verification) with
Claude, 2026-07-13. All findings display-only / client-side / MP-safe. Verify perf on desktop
min-spec; blog-tier sources (Golus, pkh.me) are field-standard but not peer-reviewed.*
