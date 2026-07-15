# HUD Visual Upgrade — Implementation Plan (what's possible, how, and in what order)

Synthesizes four verified research passes into a concrete plan for the **Stationeers UI Ascended**
visor HUD. Sources & evidence live in the companion docs:
[Glassy-HUD-Effects-Research.md](Glassy-HUD-Effects-Research.md),
[HUD-Rendering-Techniques-Research.md](HUD-Rendering-Techniques-Research.md), and the concept-art
breakdown. Everything here is **cosmetic / client-side / MP-safe** (display-only).

**Engine:** Unity 2022.3.7f1, **Built-in Render Pipeline**, gamma space, no SRP, UGUI/Canvas. The HUD
canvas is **Screen Space – Overlay** (`HudSystem`, `RenderMode.ScreenSpaceOverlay`) — this one fact
gates half the glass effects (see §0).

---

## 0. The decision that shapes everything: three implementation tiers

How an effect is built determines its cost, its risk, and whether it survives our **F6 code-only
hot-reload** workflow. Every feature below is tagged with its tier.

| Tier | What it is | Hot-reload (F6)? | Cost/risk |
|------|-----------|------------------|-----------|
| **A — Mesh / vertex-color** | Done in the existing procedural `Graphic` meshes (vertex colours, alpha, positions). No custom shader. | ✅ Works — pure code | Lowest. Fits current architecture exactly. |
| **B — Custom fragment shader** | A custom `.shader` + `Material` on a `MaskableGraphic` (smooth bands, noise dissolve, iridescence, per-pixel CA). | ⚠️ Shader must ship in an **AssetBundle** we build in this Unity project — F6 can't hot-reload a shader; changing one needs a bundle rebuild + repackage | Medium. Adds the shader-shipping gotchas (below). |
| **C — Backdrop capture** | Anything that reads the 3D scene *behind* the HUD (frosted blur, refraction, backdrop CA, UI bloom). | ⚠️ Same as B + a per-frame capture | Highest. See the overlay-canvas gate. |

**Tier C gate (critical):** because our canvas is **Screen Space – Overlay**, a naïve **GrabPass
cannot see the game scene** (overlay composites *after* the world). So every "true glass" effect must
use a **CommandBuffer that captures the camera colour before UI** into a shared RenderTexture — *not*
GrabPass. This is confirmed by Unity's own docs (GrabPass is Built-in-RP-only, "can significantly
increase CPU and GPU frame times," and does not capture an overlay backdrop).

**Tier B shader-shipping gotchas (verified against the SLP source + Beef's shipped mods):**
`Shader.Find()` fails for AssetBundle shaders — load the shader **directly** from the bundle
(`bundle.LoadAsset<Shader>(name)` → `new Material(shader)`); pack a **ShaderVariantCollection** in the
same bundle. **StationeersLaunchPad (SLP) does NOT pack assets for us** (that was the deprecated
StationeersMods framework): we build the bundle ourselves in this Unity **2022.3.7f1** project
(`BuildPipeline.BuildAssetBundles`, `StandaloneWindows64`) and ship the file inside the mod folder.
⚠️ Name it **without** the `.assets` extension (e.g. `uia_effects.bundle`) — SLP auto-loads every
`*.assets` file it finds, and a failed auto-load marks the WHOLE mod `LoadFailed` (our `OnLoaded`
never runs). Locate the mod folder via the `ModData` parameter SLP injects into `OnLoaded`
(`ModData.DirectoryPath` — works for local and Workshop; `Assembly.Location` breaks under the F6
ScriptEngine flow). TheRealBeef's mods prove the whole recipe in this exact game: bundles built with a
*newer* 2022.3.x editor load fine, `LoadFromFile` + `LoadAsset<Shader>`, fail-soft when missing.

---

## 1. Possible vs Not-Possible on our engine (the honest matrix)

### ✅ Possible & cheap — Tier A (no shader, survives hot-reload)
- **Hairline lines that fade smoothly** (fixes your "lines vanish" bug) — phone-wire coverage.
- **Gradient / directional edge-light on lines** (the "border lighter in some places" look) — via a
  synthetic edge normal.
- **Breathing / pulse glow** — animate colour/alpha over time.
- (Coarse) shine sweep and fresnel edge, if the mesh is finely tessellated.

### ✅ Possible, smoother — Tier B (custom shader in the AssetBundle)
- **Shine / light SWEEP** travelling along an edge (arc-length smoothstep band). Self-contained.
- **Dissolve / CRT boot-up reveal** (noise + step edge-glow frontier). Self-contained.
- **Iridescence / thin-film shimmer** on edges (`spectral_zucconi6`, branchless). Self-contained.
- **Chromatic aberration** on an element's *own* texture (3-tap). Self-contained.
- **Crisp SDF shapes + `fwidth` AA** and **MSDF icons** (the crispness backbone rewrite).

### ⚠️ Possible but costly & conditional — Tier C (needs the CommandBuffer capture)
- **Frosted glass fill** = blur + tint + darken the scene behind panels (dual-Kawase).
- **Backdrop refraction** (offset the grabbed UVs by a normal map).
- **Backdrop chromatic aberration** (per-channel offset on the grabbed texture).
- **UI bloom** (bright-pass the HUD → blur → add).
- **Adaptive background-aware contrast** (sample scene luminance behind panels).

### ❌ Not possible / not worth it on our engine
- **GrabPass reading the overlay backdrop** — physically can't; must use the CommandBuffer route.
- **Physically-real, view-angle Fresnel/iridescence border variation** — a flat, head-on visor panel
  has a near-constant view angle, so real view-driven variation is ~invisible. *Two claims to the
  contrary were refuted in verification.* We **fake** it with a synthetic edge normal / fixed virtual
  light — which looks right, but is a stylisation, not physics.
- **URP/HDRP renderer features / post-process stack** — we're Built-in RP; none of that is available.
- **Full physical thin-film / refraction** — only the cheap approximations above.

---

## 2. Feature-by-feature

For each: **Goal · Tier · Method · Reference · Confirmed vs Recommendation · Files · Risk.**

### 2.1 Hairline lines that fade (your thin-line fix)
- **Goal:** razor-thin lines that dissolve to nothing instead of snapping off/flickering.
- **Tier A.** **Method:** never render thinner than 1px; fade alpha by coverage —
  `drawW = max(width, 1px); alpha *= saturate(width/drawW)`. **Reference:** phone-wire AA
  (Persson/Humus via Golus). **Confirmed:** the technique + formula. **Recommendation:** apply in the
  existing mesh path (scale vertex alpha), drop the `Width` 0.5px clamp and the `bw > 0.05` cutoff.
- **Files:** `UI/Hud/PolylineGraphic.cs`, `UI/Hud/PanelGraphic.cs`. **Risk:** none material; a small,
  contained mesh-math change.

### 2.2 Gradient / directional edge-light on lines
- **Goal:** the outline is lighter where it "faces the light", darker elsewhere.
- **Tier A.** **Method:** per-vertex brightness `base + intensity*saturate(dot(edgeNormal, lightDir))`,
  driven by a **synthetic** normal (real Fresnel is invisible on a flat panel — refuted). **Reference:**
  Ronja #012 (fixed-direction variant). **Confirmed:** the Fresnel term; **Recommendation:** the fake
  normal/fixed-light model (design choice). Panels already do this via `BorderAt` — just expose/turn it
  up; polylines need it added.
- **Files:** `PolylineGraphic.cs` (add), `PanelGraphic.cs` (tune/expose), `HudElementDef` params +
  `HudPropDrawer` (F9 controls). **Risk:** purely design tuning.

### 2.3 Breathing / pulse glow
- **Goal:** panels/warnings "breathe" (bright↔dim).
- **Tier A.** **Method:** `intensity = sin(t)*0.5+0.5` as a scalar multiplier on the element's emissive
  add. **Reference:** Halisavakis ShaderQuest 6 (+ every engine). **Confirmed.** **Recommendation:**
  drive it from `HudAnimator` (one scalar per element), no mesh rebuild.
- **Files:** `HudAnimator.cs` (+ a per-element "pulse" param). **Risk:** none.

### 2.4 Light / shine SWEEP along an edge
- **Goal:** a bright band that slides along a stroke/border (the "energy trace").
- **Tier B** (smooth) or coarse Tier A. **Method:** `1 - smoothstep(0, w, abs(projected - bandPos))`
  where `projected = dot(uv, dir)`; add to rgb scaled by the pixel's own alpha (never spills outside).
  **Reference:** mob-sakai/ShinyEffectForUGUI (shipped code) + Galbartouv. **Confirmed from source.**
  **Recommendation:** extend the existing sheen infra rather than a second system; `HudAnimator` drives
  `bandPos`.
- **Files:** new `Shaders/HudEdgeFX.shader` (+ material), `PanelGraphic`/`PolylineGraphic` material hook,
  `HudAnimator`. **Risk:** introduces Tier B (shader-in-bundle, no F6 for the shader).

### 2.5 Dissolve / CRT boot-up reveal
- **Goal:** elements "power on" with a travelling bright frontier / staggered reveal (Alien: Isolation
  / Dead Space feel).
- **Tier B.** **Method:** `clip(noise - amount)` + `emission = band * step(noise - amount, 0.05)`;
  animate `amount`, stagger per element. **Reference:** Febucci dissolve (2018). **Confirmed** (needs an
  **unlit UGUI port** — the source is a lit Surface shader). A true CRT collapse/expand also drives
  vertical scale (design extrapolation, not in-source). **Recommendation:** fold into the existing
  power-death/boot machinery + `HudAnimator` staggered timeline.
- **Files:** `Shaders/HudEdgeFX.shader` (dissolve variant), `HudAnimator`, tie into the existing
  boot/PowerDeath sequencing. **Risk:** port work; Tier B.

### 2.6 Iridescent / thin-film edge shimmer
- **Goal:** subtle rainbow sheen on glass edges.
- **Tier B.** **Method:** `spectral_zucconi6()` (branchless six-bump spectral→RGB), hue driven by a
  **synthetic** edge normal (flat-panel caveat — same as 2.2). **Reference:** Alan Zucconi iridescence
  series. **Confirmed** the function; **Refuted** the "swap for a 1D LUT" shortcut (use the analytic
  form). **Recommendation:** a tasteful low-intensity edge accent, not a full rainbow.
- **Files:** `Shaders/HudEdgeFX.shader`. **Risk:** Tier B; easy to overdo (keep subtle).

### 2.7 Chromatic aberration (edge fringing)
- **Goal:** faint colour fringing at panel rims.
- **Tier B** (own texture) / **Tier C** (backdrop). **Method:** 3 samples R/G/B at offset UVs; radial
  variant `mix(uv, 0.5, (i-0.5)*offset)` → zero at centre, strongest at rim. **Reference:** Halisavakis
  / GM Shaders / GodotShaders. **Confirmed.** Self-contained on the element's own texture; needs the
  capture (2.9) to fringe the *backdrop*.
- **Files:** `Shaders/HudEdgeFX.shader` or the backdrop shader. **Risk:** banding with large offsets +
  few taps.

### 2.8 Crisp SDF shapes + `fwidth` AA + MSDF icons (the crispness backbone)
- **Goal:** resolution-independent razor-crisp shapes, outlines, and icons.
- **Tier B** (bigger rewrite). **Method:** analytic 2D SDFs (`sdf-r` round, `abs(sdf)-r` outline),
  `clamp(0.5 + d/fwidth(d))` AA; MSDF median-of-three icon atlas. **References:** Inigo Quilez;
  Golus/pkh.me; Chlumský MSDF. **Confirmed.** **Recommendation:** don't rip out the working mesh
  renderer — prototype **one** SDF `MaskableGraphic`, compare crispness/perf, migrate element-by-element
  only if it wins.
- **Files:** new `Shaders/HudSDF.shader`, new `UI/Hud/SdfPanelGraphic.cs`; MSDF atlas + `HudIconGraphic`
  path. **Risk:** largest lift; Tier B; gamma-vs-linear AA nuance.

### 2.9 Frosted glass (blur + tint + darken the backdrop) — the headline
- **Goal:** panels blur, darken, and cool the scene behind them.
- **Tier C.** **Method:** a **CommandBuffer** captures camera colour pre-UI → one **downsampled
  dual-Kawase** blur RT → every panel samples the shared `_BlurTex`, then
  `rgb = blurred*tint*darken + fill`. **References:** ARM SIGGRAPH 2015 (dual-Kawase); Intel
  (downsampling ~8×). **Confirmed** perf ordering (absolute ms are mobile — measure). **Recommendation:**
  **blur once, sample many**; downsample to ¼; throttle the RT to every N frames.
- **Files:** new `UI/Hud/HudBlurSystem.cs` (CommandBuffer + RT), new `Core/HudShaderStore.cs`
  (load shaders from the bundle), `Shaders/HudGlass.shader` + `HudBlur.shader`, `PanelGraphic` sampling
  hook, `HudSystem` (hook/unhook the CommandBuffer on setup/teardown — hot-reload discipline).
- **Risk:** highest — per-frame cost (gate to hero panels; measure on min-spec), gamma-space RT
  handling, teardown hygiene (unhook on F6/shutdown or it calls a dead assembly).

### 2.10 Backdrop refraction / UI bloom / adaptive contrast
- All **Tier C**, riding the same 2.9 capture: refraction = offset grab UVs by a normal map; bloom =
  bright-pass the HUD → reuse the blur → add; adaptive contrast = sample scene luminance behind panels.
  Build only after 2.9 exists and proves cheap enough.

### 2.11 Legibility (cheap, high-value)
- **Goal:** guaranteed readability over bright/noisy terrain.
- **Tier A.** **Method:** opaque **billboard** backing plate behind critical readouts (best in AR
  research; occlusion is a *feature* for an opaque visor); **dual black+white outline** on symbols;
  a **high-contrast mode** (≥7:1). Don't combine billboard + outline. **References:** Gabbard/Swan/Hix
  2006; Xbox AG 102 / WCAG 2. **Confirmed.** **Recommendation:** a global "high-contrast" toggle + a
  per-element "backing plate" option.
- **Files:** `HudElementDef` params, `PanelGraphic`/text widgets, `HudConfig` toggle. **Risk:** none.

---

## 3. Phased rollout (each phase ships & is testable on its own)

- **Phase 0 — Cheap Tier-A wins (no shaders, full F6):** 2.1 hairlines, 2.2 gradient edge-light,
  2.3 breathing pulse, 2.11 legibility. *Solves your thin-line + gradient-border asks immediately.*
- **Phase 1 — Tier-B self-contained FX pack (one shader, in the bundle):** 2.4 shine sweep, 2.5 dissolve
  reveal, 2.6 iridescence, 2.7 CA-own-texture — all in one `HudEdgeFX.shader`, driven by `HudAnimator`.
  *First introduction of a shipped shader → establishes `HudShaderStore` + the AssetBundle shader flow.*
- **Phase 2 — Tier-C backdrop system (the big one):** 2.9 frosted glass via `HudBlurSystem`, then
  optionally 2.10 refraction/bloom. *Gate to hero panels; measure.*
- **Phase 3 — Crispness backbone (optional, biggest):** 2.8 SDF prototype → migrate if it wins; MSDF
  icons.

---

## 4. Files & components at a glance

**Change:** `UI/Hud/PolylineGraphic.cs`, `UI/Hud/PanelGraphic.cs` (hairline, gradient edge-light),
`UI/Hud/HudAnimator.cs` (pulse/shine/dissolve timelines), `UI/Hud/HudSystem.cs` (CommandBuffer
hook/unhook), `UI/Hud/HudElementDef.cs` + `Windows/HudPropDrawer.cs` (new F9 params),
`UI/Hud/HudConfig.cs` (global toggles).

**New:** `UI/Hud/HudBlurSystem.cs` (Tier C capture + blur RT), `Core/HudShaderStore.cs` (load shaders
from the AssetBundle — `Shader.Find` won't work), `Shaders/HudEdgeFX.shader` (shine/dissolve/iridescence/
CA), `Shaders/HudGlass.shader` + `HudBlur.shader` (Tier C), later `Shaders/HudSDF.shader` +
`UI/Hud/SdfPanelGraphic.cs`, and an MSDF icon atlas asset.

**Workflow impact:** Phase 0 stays pure code (F6 hot-reload as today). Phase 1+ introduces shaders that
live in an AssetBundle we build in this Unity project — those change via a bundle rebuild + repackage,
not F6. Keep effect *logic* in code (params, animator) so only the shader itself is bundle-bound.

---

## 5. Risks & open questions (resolve in-engine)
- **Backdrop cost:** measure a downsampled dual-Kawase on real min-spec desktop GPUs; gate Tier C to a
  few hero panels, not HUD-wide.
- **Gamma vs linear:** sampling a scene RT into a gamma-space UI material can wash out / over-darken —
  verify sRGB RT flags.
- **Teardown hygiene:** the CommandBuffer/camera hook MUST unhook on F6 reload & shutdown or it calls
  into a dead assembly (existing hot-reload discipline).
- **Flat-panel physics:** Fresnel/iridescence must be driven by a synthetic normal — pick the fake
  light model that best sells the look.
- **Shader-in-bundle:** ✅ RESOLVED — verified against the SLP source and TheRealBeef's shipped mods
  (see the Tier B gotchas box in §0 and the 0.9.0 Master Plan). Bundle built in this project, named
  non-`.assets`, loaded fail-soft from `ModData.DirectoryPath`.

*Everything display-only / client-side / MP-safe. Confirmed facts vs our recommendations are separated
per feature; full evidence & source links in the two research docs.*
