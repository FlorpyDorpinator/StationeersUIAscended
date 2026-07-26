# Glassy HUD Effects — replicating the concept art

**Purpose:** how to reproduce the frosted-glass / glowing "1-for-1" HUD look from FlorpyDorp's
concept art in **Stationeers UI Ascended**, on the game's actual engine, as a client-side
BepInEx/StationeersLaunchPad mod. Combines a deep-research pass (fanned-out web search + 3-vote
adversarial verification: 21 sources fetched, 96 claims extracted, 25 verified → 24 confirmed / 1
refuted) with a shot-by-shot breakdown of the concept art and the effect vocabulary.

**Engine constraints (verified from the project):** Unity **2022.3.7f1**, **Built-in Render
Pipeline** (no SRP), **gamma** color space, **forward** rendering. The HUD canvas is
**Screen Space – Overlay** (`HudSystem` uses `RenderMode.ScreenSpaceOverlay`, sortingOrder 3800).
Everything here is **cosmetic / client-side / display-only** — no game-state mutation, MP-safe by
construction.

---

## 1. The look, decomposed

The concept art is the *same* HUD shown twice — once clean, once composited over a real game frame
(moon rover cockpit). The composited shot is the useful one: the terrain shows **through** the top
bar, visibly **blurred + darkened + shifted toward blue-black**. That tells us the look is **five
stacked effects**, not one shader:

| # | Effect | What it produces | Status in the mod |
|---|--------|------------------|-------------------|
| **A** | **Frosted glass fill** = backdrop **blur** + **dark tint** + **cool colour shift** | Terrain behind the bar reads blurred, darkened, blue-shifted | ❌ New (needs a blur capture) |
| **B** | **Edge-light / rim glow** — thin bright cyan-white outline, **brightest along the top edge**, fading down the sides | The "lit rim of glass" outline | ✅ Have (`PanelGraphic` edge-light/border) |
| **C** | **Inner sheen gradient** — soft top-lit vertical gradient across the face | Glassy sheen on the panel surface | ✅ Have (`sheen` param) |
| **D** | **Bloom / glow** on emissive pixels — borders, icons, text bleed a soft halo | "The UI glowing a bit" | ❌ New-ish |
| **E** | **Colour-driven glow** — red halo on POWER CRITICAL, amber on POWER LOW, cyan elsewhere | Warning chips glow *their own* colour | ❌ (falls out of D) |
| **F** | Curvature + trapezoid frames + nested inner lines | Bowed bar, angled ends, double-outline frames | ✅ Have (VisorWarp, insets, polylines) |

**Key insight:** the two genuinely-new GPU pieces — **A (frost)** and **D (bloom)** — share the
**same machinery** (a downsampled blur). Build one blur system and it powers both. That's why the
whole look is cheaper than it appears.

### Per-region tuning read (from the pixels)
- **Blur:** light-to-moderate — large terrain shapes survive → ~¼-res, ~1–2 blur iterations, not a
  heavy frost.
- **Tint:** dark, cool blue-black; ~70–85 % darkening plus a slight blue add. Terrain is *visible
  but muted*.
- **Edge light:** ~1–2 px, cyan-white, **top-biased**, soft ~2–3 px halo.
- **Bloom:** *gentle* — small radius, low intensity. A whisper of glow, not neon.
- **Text:** crisp core + faint glow (TextMesh Pro glow underlay nails this).

---

## 2. Effect A — Frosted tinted glass (the headline new effect)

**Goal:** blur, darken, and colour-shift whatever 3D scene is behind each panel.

**Why not the obvious approaches:**
- **GrabPass** still works in Built-in RP 2022.3 (the compatibility table lists GrabPass = *Yes* for
  Built-in, *No* for URP/HDRP), and a UGUI `Image` material *can* read it — but Unity's own manual
  says it "can significantly increase both CPU and GPU frame times" and to "generally avoid using
  this command other than for quick prototyping." **Not** for per-panel/per-frame HUD use.
  *(high confidence, 5 claims 3-0)*
- On the Built-in RP **all UI renders in one stage**, so you can't cheaply blur the backdrop of an
  *arbitrary individual* element — the performant pattern is **grab/blur once, sample many**. This
  is exactly how the commercial *Translucent Image* asset works (a camera captures a Screen Space –
  Camera canvas, blurs it, and that becomes the shared backdrop). *(high, 2 claims 3-0)*
- **Overlay-canvas caveat (load-bearing):** a naïve GrabPass on a **Screen Space – Overlay** HUD may
  **not** contain the 3D scene, because overlay UI composites *after* everything. Since our HUD *is*
  overlay, the safe route is a **CommandBuffer that captures the camera colour *before* UI
  composition** into our own RenderTexture. *(flagged as an open question by the research; resolved
  here by the known canvas mode.)*

**Recommended pipeline:**
1. A `CommandBuffer` blits the camera colour into a small **downsampled** RenderTexture, once/frame.
2. **Dual-Kawase blur** that RT (see §7).
3. Every `PanelGraphic` samples the shared `_BlurTex`.
4. The **"changing the colour of what's behind it"** is one line in the panel shader:
   `finalRGB = blurredBackdrop × tint × darken + panelFill`
   — from the art, `tint` ≈ cool blue-black, `darken` ≈ 0.15–0.30.

---

## 3. Effects B + C — Edge light + sheen (already shipped, just tune)

These are the `PanelGraphic` sheen/edge-light in the mesh — the research confirmed **mesh-based
sheen/fresnel/edge-light is the correct cheap home** for this (no framebuffer grab needed). To match
the art 1-for-1 it's mostly a **tuning** job:
- Push the rim brighter and **bias it to the top edge** (light-from-above).
- Keep the face **sheen** subtle (soft vertical gradient).

See §5 for the *variation along the edge* the art shows.

---

## 4. Effects D + E — The glow

Three routes, cheapest → best:

1. **TMP glow / underlay** for text — free, per-font-material; handles the soft **text** halo alone.
2. **Cheap per-element glow** — a blurred, brightened, **additive** duplicate of the graphic drawn
   behind itself; good for icons/borders, no full-screen pass.
3. **Real UI bloom (best 1-for-1)** — bright-pass the HUD render → dual-Kawase blur → add back on
   top. Borders/icons/text *all* bleed, and each glows **its own colour** automatically, so POWER
   CRITICAL's red halo and POWER LOW's amber halo come for free (**E**). Reuses the §2 blur system.

---

## 5. The edge lines that "fade in and out" — terminology

The panel outlines aren't a flat uniform border — their **brightness/opacity ramp**. This is two
different effects depending on whether the variation is **spatial** (along the edge) or **temporal**
(over time). The concept art reads as spatial.

### Spatial (what the art shows)
- **Gradient stroke / gradient edge-light** — the outline's colour/alpha is a gradient, not a
  constant.
- **Edge falloff** — brightness falls off along the perimeter.
- **Directional rim light** (a.k.a. **fresnel edge light**) — brightest **where the surface faces
  the light** (here the **top edges** are hot cyan-white; sides/bottom fade toward the panel
  colour). This top-biased "light catching the rim of glass" is the dominant read.
- **End fade / feathered ends** — where a line **dissolves to nothing at its tips**. Already
  implemented as `PolylineGraphic.FadeEnds`.

→ Precise description of the art: **a directional, top-biased rim light with feathered ends** — a
gradient stroke, not a solid border.

### Temporal (if you want it to literally animate)
- **Pulse / "breathing" glow** — the edge's emissive intensity oscillates on a sine (bright↔dim).
- **Light sweep / shine sweep** (a.k.a. **specular sweep**, **border shimmer**, **running light**) —
  a bright highlight that *travels along* the outline.
- **Scanline / energy trace** — a moving band along the edge.

### How we'd build it
- **Static (now):** drive edge-light **alpha by position/normal** (hot at top, fading around) and
  reuse `FadeEnds` on line segments — cheap, no new system.
- **Animated:** add a **pulse** via the existing `HudAnimator` (a sine on edge-light
  intensity/colour), or a **light sweep** by animating a bright band's position along the stroke's
  **arc length** — `PolylineGraphic` already computes arc length for `FadeEnds`, so a sweep is a
  small extension.

---

## 6. Recommended architecture — one shared blur system

Build a single `HudBlurSystem`:
- `CommandBuffer` → capture camera colour (pre-UI) → **one downsampled shared RT** → dual-Kawase
  blur.
- Expose `_BlurTex` (+ tint/darken uniforms) that `PanelGraphic` samples for **frost (A)**.
- Reuse the same blur for a **UI bloom (D/E)** pass (bright-pass → blur → additive).
- Cost is **near-constant regardless of panel count** (blur once, sample many).
- Every new static resets in teardown (hot-reload discipline); unhook the CommandBuffer/`onPreRender`
  hooks on shutdown or they call into a dead assembly after an F6 reload.

---

## 7. Performance (verified research)

**Dual-Kawase / "dual filtering"** (Marius Bjørge, ARM SIGGRAPH 2015) is the fastest quality blur for
weak GPUs — a down/up-sample pyramid with a 5-tap downsample kernel (centre ×4 + four diagonal
corners ×1, i.e. weights ½ and ⅛). Measured on a 2015 Mali-T760 MP8 mobile GPU:

| Blur | 1080p | 720p |
|------|-------|------|
| Full Gaussian | 41.9 ms | 19.2 ms |
| Box | 21.4 ms | 9.9 ms |
| Kawase | 4.5 ms | 2.8 ms |
| **Dual-Kawase** | **2.8 ms** | **1.7 ms** |

*(high confidence, 6 claims; kernel-shape sub-claim 2-1)*. Corroborated: Intel found Kawase uses
"1.5×–3.0× less computation time than optimized Gaussian"; UWA independently ranked dual blur
fastest on the same GPU.

**Downsampling is the single biggest lever.** Blurring into a **½×½** buffer cuts cost ~**8×** at
near-identical quality; measured on an AMD R9-290X a big blur went ~3 ms full-res → **0.5 ms at ½** →
**0.17 ms at ¼**. *(high, 3-0)*. Combined with dual-Kawase, a full-screen backdrop blur lands in the
**sub-ms-to-few-ms** range.

**Tactics:** downsample to ¼; **throttle** the blur RT (update every N frames — the backdrop changes
slowly); **reuse one blurred RT for all panels**; never per-panel/per-frame GrabPass.

**Refuted:** the specific "**4 down + 4 up = 8 passes**" dual-Kawase config was **killed** (1-2
vote). No verified optimal iteration count emerged — **tune empirically** at ½ vs ¼ res.

---

## 8. Mod / AssetBundle constraints (verified)

- ~~**StationeersMods auto-packs every project asset** into the AssetBundle~~ *(high, 3-0, but
  **SUPERSEDED**: StationeersMods is deprecated — we use **StationeersLaunchPad (SLP)**, which packs
  NOTHING for us. Verified against the SLP source 2026-07-13: we build the bundle ourselves in this
  Unity 2022.3.7f1 project, name it non-`.assets` — SLP auto-loads every `*.assets` file and a failed
  auto-load kills the whole mod's load — and load it fail-soft at runtime from
  `ModData.DirectoryPath`. See the 0.9.0 Master Plan §Tier B.)*
- ⚠️ **`Shader.Find()` does NOT work for shaders loaded from an AssetBundle** (Unity staff
  confirmed). Load the bundle and pull the shader directly: `AssetBundle.LoadFromFile` →
  `LoadAllAssets(typeof(Shader))` → `new Material(shader)`. *(high, 3-0)*
- ⚠️ **Shader-variant stripping** — `shader_feature` variants get dropped from bundles; pack a
  **ShaderVariantCollection** (with the required variant tags) in the *same* bundle as the shader.
  *(high, 3-0)*
- ⚠️ **Shader duplication** — referencing one shader from multiple objects can compile duplicates
  and break batching; **load the shader bundle *first*, before bundles that depend on it.** *(high,
  3-0)*
- ❓ Reusing the game's bundled **CameraFilterPack** blur (via `Shader.Find`) is **unverified** — no
  source confirmed its quality/cost or `Shader.Find` reliability for it. Prefer a custom bundled
  dual-Kawase shader; treat CameraFilterPack as a fallback to test.

---

## 9. Ready-made code & references

- ✅ **aki-null/DynamicDualFiltering** — **MIT-licensed** dual-Kawase for **Built-in RP** (attach
  `CameraDualFilteringBlur` to a camera; "very fast and memory bandwidth friendly"). Portable into
  the mod. *But* it's a full-screen **camera** blur, not a UGUI-behind-panel grab — combine it with
  the shared-RT-that-panels-sample pattern. *(high, 3-0)*
- ❌ **Translucent Image** (leloctai.com, Asset Store 78464) — best architecture reference,
  BiRP-compatible, but **$24.99 proprietary Standard Unity Asset Store EULA → cannot ship in a free
  mod.** Reference only. *(high, 3-0)*
- ❌ **Unified-Universal-Blur** — **URP-only** (badges: URP Yes, BiRP No, HDRP No); unusable on
  Built-in RP. *(high, 3-0)*
- ⚠️ **Fake Glass** (s-ilent) — clean glass with **no GrabPass**, but its frosted content comes from
  a **reflection probe** (static cubemap), which **cannot capture the moving 2D screen-space
  backdrop** of a HUD panel. Good for stylized fake, wrong for real backdrop blur. Confirms
  fresnel/sheen/pre-blur/refraction are cheaper *approximations* — only a real grab shows the actual
  scene. *(high, 2 claims 3-0)*

---

## 10. Open questions — resolve in-engine (not answerable from the web)

1. **Confirmed:** the HUD canvas is Screen Space – Overlay → use the **CommandBuffer camera-colour
   capture**, not GrabPass. (Was the research's #1 open question; settled by our own code.)
2. **Downsample factor + iteration count** — tune ½ vs ¼ res empirically (the 8-pass number was
   refuted; no verified optimum).
3. **Measure on real min-spec desktop GPUs** — the headline ms are from a 2015 *mobile* GPU. The
   *relative* orderings (dual-Kawase fastest; downsampling compounds ~8×) are architecture-independent
   and robust; the *absolute* milliseconds are not — profile in-game.
4. **CameraFilterPack reuse** — test whether its bundled blur is BiRP/gamma-usable before committing
   to a custom shader.

---

## 11. Caveats & confidence

- Frame-time numbers come from a **2015 Mali-T760** (mobile) and a **2013 AMD R9-290X** — relative
  orderings robust, absolutes must be measured on Stationeers' min-spec desktop GPUs.
- Several primary pages (Intel, leloctai.com, Asset Store) returned **HTTP 403** to direct fetch and
  were verified via independent search-engine extraction + first-party corroboration, not a direct
  render — quotes matched across independent returns.
- "GrabPass is deprecated" (stated by the Translucent Image vendor) is a **loose overstatement** —
  GrabPass is legacy/prototyping-only but **still functions** in Built-in RP 2022.3.
- Gamma color space + UGUI: watch for a linear/gamma mismatch when sampling the blurred RT into a
  gamma-space UI material — verify the frost doesn't wash out or over-darken; may need an explicit
  colour-space handling on the RT.

---

## 12. Sources

Primary / high-value (verified, 3-0 unless noted):
- Unity Manual — GrabPass (Built-in RP): https://docs.unity3d.com/6000.0/Documentation/Manual/writing-shader-grabpass.html · https://docs.unity3d.com/Manual/SL-GrabPass.html
- Translucent Image — "Blurring UI" architecture: https://leloctai.com/asset/translucentimage/docs/articles/blurring-ui.html · listing https://assetstore.unity.com/packages/tools/gui/translucent-image-fast-ui-background-blur-78464
- ARM / Marius Bjørge, *Bandwidth-Efficient Rendering* (SIGGRAPH 2015, dual filtering + the perf table): https://community.arm.com/cfs-file/__key/communityserver-blogs-components-weblogfiles/00-00-00-20-66/siggraph2015_2D00_mmg_2D00_marius_2D00_slides.pdf
- Intel / Filip Strugar, *An Investigation of Fast Real-Time GPU-Based Image Blur Algorithms* (downsampling cost, Kawase vs Gaussian): https://www.intel.com/content/www/us/en/developer/articles/technical/an-investigation-of-fast-real-time-gpu-based-image-blur-algorithms.html
- UWA, *Dual Blur and its implementation in Unity*: https://medium.com/@uwa4d/dual-blur-and-its-implementation-in-unity-c2cd77c90771
- aki-null/DynamicDualFiltering (MIT, BiRP dual-Kawase): https://github.com/aki-null/DynamicDualFiltering
- lukakldiashvili/Unified-Universal-Blur (URP-only): https://github.com/lukakldiashvili/Unified-Universal-Blur
- StationeersMods framework (DEPRECATED — we use StationeersLaunchPad; kept as the source the
  original claim came from): https://github.com/AlexStz/StationeersMods
- Unity — Shader Features with Asset Bundles (ShaderVariantCollection): https://support.unity.com/hc/en-us/articles/115001971483
- Unity 2022.3 — Shader compilation (duplicate-shader load order): https://docs.unity3d.com/2022.3/Documentation//Manual/shader-compilation.html
- Unity forum — Shader.Find returns null for AssetBundles (staff-confirmed): https://forum.unity.com/threads/shader-find-returns-null-for-asset-bundles-repro-included.573856/
- s-ilent/Fake Glass (grabpass-free, probe-based): https://gitlab.com/s-ilent/fake-glass/-/tree/master
- lexdev — UI blur tutorial (GrabPass + UGUI): https://lexdev.net/tutorials/ui/blur.html

Supporting: blog.frost.kiwi (dual-Kawase), keijiro/UnityRefractionShader, 80.lv glass refraction,
FomTarro/ShaderLoader (runtime shader loading in mods).

---

*Research + analysis: Claude (with FlorpyDorp), 2026-07-13. Client-side cosmetic; MP-safe.
Everything here is display-only — no game-state mutation.*
