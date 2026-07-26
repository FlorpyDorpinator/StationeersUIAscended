# UIA 0.9.0 "Experimental" — Master Plan: all three effect tiers + the profiler

**What this is:** the execution plan for shipping **Tier A (mesh effects), Tier B (bundle shaders),
and Tier C (backdrop capture) together** in one experimental version, every effect **togglable
globally and per-element from the F9 designer**, instrumented by **Jackson's profiler** so we can
prove nothing hurts the game. Supersedes the phased rollout in
[HUD-Visual-Upgrade-Plan.md](HUD-Visual-Upgrade-Plan.md) (which remains the per-effect technique
reference); this doc adds what we learned from **TheRealBeef's shipped shader mods**, the
**StationeersLaunchPad (SLP) source**, **Jackson's Profilicus Universalis**, and a line-level audit
of our own codebase.

**Version:** 0.9.0 Experimental (FlorpyDorp's call). Everything display-only / client-side / MP-safe.

---

## 0. What the new evidence changed (deltas from the previous plan)

### From TheRealBeef's mods (Reference/BeefsMods/ — SEGI-Plus, Shader-Changes, Sunglasses, Defogger, ModProfile-Lib)
The single most important outcome: **the entire Tier B/C pipeline is already proven in this exact
game.** Concretely:

1. **Bundle shaders work, today, in Stationeers.** His mods ship UnityFS AssetBundles (built with a
   *newer* 2022.3.x editor than the game's 2022.3.7f1 — same-minor-LTS bundles are compatible),
   loaded via `AssetBundle.LoadFromFile(<mod folder>/Content/<name>.asset)` →
   `bundle.LoadAsset<Shader>(name)`; materials created at runtime with `HideFlags.DontSave`,
   `Shader.PropertyToID` cached as static ints, `DestroyImmediate` on teardown
   (Sunglasses `SunglassesShaderPlugin.cs:245-308` is the minimal template). **No `Shader.Find`, no
   embedded resources.** Fail-soft: a missing bundle logs a warning and the effect degrades.
2. **The game's main camera accepts mod-added render hooks.** All his effects are MonoBehaviours
   `AddComponent`'ed onto `CameraController.Instance.MainCamera` using `OnRenderImage` +
   `Graphics.Blit` (and SEGI uses `OnPreRender`/`OnPostRender` + helper cameras). **Nobody uses
   CommandBuffers — that injection surface is unclaimed**, so our Tier C CommandBuffer won't collide
   with any existing mod.
3. **The game's post stack is classic per-camera image effects** (SSAOPro, UltimateBloom,
   Antialiasing, TAASimple, VolumetricLightRenderer) — **not** Post Processing Stack v2. The
   CameraFilterPack suite is present but dormant. Vanilla hard-disables SSAO
   (`CameraController.SetAmbientOcclusion` just sets `enabled=false`).
4. **The game already ships a chromatic-aberration + distortion post effect**
   (`CameraController.SolarStormChromaticAberration`, `SolarStormDistortionFX`) — worth studying
   before writing our own CA (Tier B §4.4).
5. **Stationeers lifecycle patterns to copy:** coroutine-polling for the camera, re-applying on
   `CameraController.ManagerAwake` (cameras rebuild on world load), `sceneLoaded` cleanup, meticulous
   `RenderTexture.Release()+DestroyImmediate` teardown, headless-server guard.
6. **A warning to heed:** Beef's lazy-static bundle caching (`??=`) is **not hot-reload safe** — fine
   for his mods (no hot reload), fatal for our F6 flow. Our loader must handle the
   "bundle already loaded" case (§4.1).
7. His **ModProfile-Lib** is a compile-time Stopwatch scope logger — a good dev pattern but **not** a
   substitute for Jackson's profiler (no aggregation, no UI, log-spam output). SEGI-Plus's internal
   ablation profiler (per-stage skip flags + trimmed-mean frame meter) validates our A/B methodology.

### From the SLP source (Reference/StationeersLaunchPad-master/)
1. **SLP packs nothing** — the old "StationeersMods auto-packs assets" story is dead. We build the
   AssetBundle ourselves (this project **is** Unity 2022.3.7f1 — byte-exact match with the game) via
   a new `Assets/Editor/` build script.
2. **⚠️ Never name the bundle `*.assets`.** SLP auto-loads every `*.assets` file found anywhere in
   the mod folder; a failed load marks the **entire mod** `LoadFailed` and `OnLoaded` never runs. We
   name ours **`uia_effects.bundle`** and load it ourselves, fail-soft.
3. **Locating the bundle:** add a `ModData` parameter to `StationeersUIMod.OnLoaded` (SLP's
   DefaultEntrypoint injects it) — `ModData.DirectoryPath` is correct for local AND Workshop
   installs. `Assembly.Location` is WRONG under the F6 ScriptEngine flow (points at a dumped temp
   copy) — the dev path comes from config instead (§4.1).
4. **🐛 CRITICAL PACKAGING BUG (pre-existing, found during this audit):** the shipped 0.8.0 zip
   contains the **Dev build DLL including the ScriptEngineLoader shim**. Under SLP the shim's Awake
   initializes the mod itself with an **empty prefab list** and binds config to
   `com.stationeersuimod.ui.scriptengine.cfg`, and SLP's own `OnLoaded` is then ignored by the
   `_loaded` guard. Everything *works* today only because we don't yet consume SLP's injected
   parameters — **0.9.0's `ModData` dependency makes fixing this a hard prerequisite** (§8.1).
5. The `Assets/About/stationeersmods` marker file is referenced by **nothing** in SLP or the game —
   inert legacy, can be deleted. `About.xml` lacks `<ModID>`, and its `<Dependencies/>`/
   `<LoadBefore/>`/`<LoadAfter/>` elements don't match SLP's actual names (`DependsOn`/`OrderBefore`/
   `OrderAfter`) — harmless while empty; fix while we're in there (§8.3).
6. SLP never unloads assemblies or bundles at runtime (mod disable = next launch). The only unload
   discipline that matters is our own F6 flow.

### From Jackson's profiler (Reference/Jackson's Stationeers Profiler/ProfilicusUniversalis/)
It's exactly what we need and clearly built for us (the README literally says "Florpy Blur
Profiler"): a **vendor-in source** (3 files, ~600 LOC, C# ≤7.2 — compiles under our 7.3/net48),
zero-config scope profiler — `using (Profiler.Time("name"))` → per-frame aggregation → 10-second
rolling window → color-coded ImGui table → Markdown snapshot export, with `SetScenario`/`Clear`/
`SaveSnapshot` primitives designed for A/B runs. Integration verdicts (all verified against the
27701 decompile):
- **Vendor ONLY `ProfilicusUniversalis.cs`** into `Assets/Scripts/StationeersUIMod/Profiling/`
  (namespace → `StationeersUIMod.Profiling`). **Drop his two integration files**: his console
  registration (`CommandLine.AddCommand`) is **hot-reload-fatal** (the game's `_commandsMap` is a
  static that survives F6, rejects duplicates, and has no Remove — a stale command would drive the
  dead assembly forever); his ImGui Harmony patch is redundant — we call `Draw()` from our existing
  `Patch_ImGuiWindowManager_Draw` postfix. Zero new Harmony surface.
- **It measures CPU only** (Stopwatch submission time). Tier C GPU cost is invisible to it by
  design — the A/B **frame-time-delta methodology** is mandatory (§6.3), with
  `FrameTimingManager.gpuFrameTime` as a runtime-probed optional extra.
- **Gate:** no license file exists — get **Jackson's written OK** + attribution headers before
  shipping vendored source. (It was prepared for us, but get it on record.)

### From our own codebase audit
- The per-element settings seam **already exists**: `HudElementView.DescribeProps` ends with an
  `"Effects (this element)"` header + three checkbox/strength pairs (`fxCollapse`/`fxGlitch`/
  `fxWarp`) resolved by `EffectAmt()`. New effects extend this list — **the mapping onto existing
  elements is already solved by architecture** (§3).
- The global settings seam already exists: `HudConfig` ConfigEntries + the F9 window's
  CollapsingHeader blocks, with the glass pair (`GlassSheen`/`GlassEdge`) as the exact
  "-1 on the element = follow global" precedent, and `ResetAllGlassToGlobal` as the bulk-apply
  precedent.
- **Profiles are format-safe for everything we're adding**: unknown `Params` K/V pairs survive an
  old build's load→save cycle verbatim; defaults apply when keys are absent. The ONE forbidden move:
  **new enum tokens or new first-class XML attributes** (an old build's XmlSerializer throws, and
  `HudProfileStore.LoadActive` then **overwrites the user's file with the starter**). 0.9.0 adds
  **zero enum members, zero attributes — Params-bag keys only**, plus a LoadActive hardening (§7.2).
- **Mode C's material swap is the #1 breakage hazard**: `ApplyWorldMaterials` blindly assigns
  `_ztestMat` to every non-TMP graphic and restores to `null` — it would clobber any Tier B/C custom
  material. Needs an exemption + re-apply mechanism (§4.2).
- Modes B/D already render the HUD into their own RT via a disabled camera — Tier C is **gated to
  Flat/VertexWarp curvature initially** (§5.4).
- `HudConfig.GlitchShader` is a dead knob (bound, never read) — retire or wire it in 0.9.0.

---

## 1. Deliverable shape: what ships in 0.9.0

| Piece | New/changed | Tier |
|---|---|---|
| Hairline lines (coverage-fade), polyline directional edge-light, pulse/breathing | `PanelGraphic`, `PolylineGraphic`, `HudAnimator` | A |
| Shine sweep, dissolve/boot reveal, iridescent edge, chromatic aberration (own-texture) | `uia_effects.bundle` (`HudEdgeFX.shader`) + `HudFxMaterials` | B |
| Frosted-glass backdrop (blur+tint+darken), optional UI bloom | `HudBackdrop` (CommandBuffer + dual-Kawase) + `HudGlass.shader` | C |
| Profiler (vendored Profilicus + UIA additions) | `Profiling/` + F9 section + `uiaprof` command | — |
| F9 "Effects (global)" section + per-element effect rows + bulk-apply | `HudEditorWindow`, `HudElementView` | — |
| Packaging fix (release DLL without dev shim), bundle build script, About.xml ModID | `Dev/`, `Assets/Editor/`, `Assets/About/` | — |

**Default states (proposed — FlorpyDorp to confirm):** Tier A effects ON at subtle strengths (they
cost ~nothing and replace broken behaviour like vanishing hairlines); Tier B effects ON where the
bundle loads (self-contained, cheap), auto-degrading to Tier A looks if the bundle is missing;
**Tier C OFF by default** (opt-in "Experimental frosted glass" toggle) until profiled on more
hardware. Every effect individually toggleable; each tier has a **master kill-switch**; all of it in
F9 → "Effects (global)".

---

## 2. Settings model — how effects map onto the elements you already designed

This was FlorpyDorp's direct question, so precisely:

**Nothing about existing elements changes.** Effects are *properties*, not new element types. Every
existing element (the top bar, hand boxes, vitals panel, your custom boxes/lines — anything in any
profile, including old ones) picks up the new capabilities through the same two layers that glass
sheen already uses:

1. **Global layer** — new entries in `HudConfig` (declared beside `GlassSheen`/`GlassEdge`,
   bound in `Bind()` under "10. Visor HUD"): per-tier master switches + per-effect
   enable/strength/parameters (e.g. `FxHairlines`, `FxEdgeLightDir`, `FxPulseSpeed`, `FxShine`,
   `FxDissolveOnBoot`, `FxIridescence`, `FrostEnabled`, `FrostDownsample`, `FrostUpdateEveryN`,
   `FrostTint`, `ProfilerVisible`). Surfaced in F9 → **"Effects (global)"** CollapsingHeader
   (inserted between the existing glitch block and "Global style"), using the existing
   Toggle/FloatSlider helpers.
2. **Per-element layer** — new K/V pairs in each element's `Params` bag, exposed under the existing
   **"Effects (this element)"** header in the element popup: checkbox + strength pairs following the
   `fxGlitch`/`fxGlitchAmt` template (`fxPulse`/`fxPulseAmt`, `fxShine`/`fxShineAmt`,
   `fxIrid`/`fxIridAmt`, `fxFrost`/`fxFrostAmt`, `fxDissolve`/`fxDissolveAmt`,
   `fxHairline` where meaningful), plus `-1 = follow global` floats for tunables that have a global
   default (the `sheen`/`spec` pattern). Resolution order (already implemented for glass):
   **element `followGlobal` → global; element value ≥ 0 → element; else → global.**
3. **Bulk-apply** — "Make ALL elements follow global ‹effect›" buttons modeled on
   `ResetAllGlassToGlobal` (PushUndoNow → loop `doc.Elements` → MarkChanged), so you can retrofit an
   entire existing profile in one click, undoably.

**Old profiles:** open unchanged — absent keys mean "defaults" (`GetF`/`GetB` return the caller
default). **New profiles on old builds:** unknown `P` keys deserialize as ordinary params, survive
autosave round-trips, and reappear intact on the new build. **Guarantee we must keep:** zero new
enum tokens, zero new XML attributes (§7.2).

**Which effects make sense per element type** (the editor shows only what applies): frost/shine/
iridescence/dissolve on framed widgets (anything drawing a `PanelGraphic`); hairline + edge-light on
Polylines and borders; pulse on anything with a fader. `DescribeProps` overrides let widgets opt out
of rows that make no sense for them (same as today).

---

## 3. Tier A workstream (pure code — F6-iterable)

| Effect | Where | Mechanics |
|---|---|---|
| **Hairline coverage-fade** | `PolylineGraphic.Width` setter (replace the 0.5px clamp) + `PanelGraphic` border (replace the `bw > 0.05` cutoff) | render width `max(w, 1px)`, multiply stroke alpha by `w/drawW`; sub-pixel lines dim smoothly to nothing |
| **Polyline directional edge-light** | `PolylineGraphic.SolidAt` (per-segment `_dir`/`_nrm` already computed) | vertex-colour `base + intensity·saturate(dot(nrm, lightDir))`, reusing `PanelGraphic.BorderAt`'s key-light math/constant so panels and lines agree on the light |
| **Pulse / breathing** | `HudAnimator` (new per-panel scalar, synced from params like `CollapseAmt` is at `HudSystem.Update`) | `sin(t)·0.5+0.5` multiplier onto the element's emissive/alpha; per-element `fxPulse`/`fxPulseAmt` |

Rules: follow the `Sheen`/`Spec` property template exactly (NaN-guard + clamp + `Approximately`
dirty-guard) so idle panels rebuild nothing; **any new global that `OnPopulateMesh` reads must fold
into `LayoutHash`** (the EdgeFeather precedent) or its slider will appear dead; per-frame-read
values pushed through dirty-guarded setters need no hash entry.

Profiler tripwire: an animated vertex effect that re-dirties meshes every frame will light up
`Hud.Mesh.*` Calls/s — pulse must modulate via `CanvasRenderer`/vertex-colour path with dirty-guards,
not wholesale re-mesh (§6.2 catches it if we get this wrong).

## 4. Tier B workstream (the shader bundle)

### 4.1 `HudShaderStore` (new, `Core/HudShaderStore.cs`)
- Resolves the bundle path: **SLP path** = `ModData.DirectoryPath` (new `OnLoaded(…, ModData)`
  parameter) + `uia_effects.bundle`; **F6 dev path** = a `UIAConfig` string (defaults to this repo's
  bundle build output). Never `Assembly.Location`.
- Loads with `AssetBundle.LoadFromFile` in a try/catch; on ANY failure → log once, set
  `TierBAvailable=false`, every Tier B effect silently renders its Tier A fallback. On success,
  `LoadAsset<Shader>` by explicit name (never `Shader.Find`), `Shader.PropertyToID` cached.
- **Hot-reload safety (the Beef gap):** on load, first check
  `AssetBundle.GetAllLoadedAssetBundles()` for a resident copy (a prior F6 life) and reuse it;
  `Shutdown` → `bundle.Unload(true)` + null the statics + destroy materials. Double-F6 must be
  clean.
- Ships with a `ShaderVariantCollection` in the same bundle (variant-stripping insurance).

### 4.2 `HudFxMaterials` (new) — materials without breaking batching or mode C
- **One shared material per effect family** (not per element), with per-element data split by how
  often it changes *(inline-verified against the mesh code 2026-07-13)*:
  - **Static per-element strengths → packed into `uv0`.** Our graphics never texture — every
    `AddVert` today passes a dummy `Vector2.zero/one` uv0 (verified: `PanelGraphic`,
    `PolylineGraphic`, `HudGlyphs`) — so uv0 is free real estate our custom shader defines, is
    **never stripped by the Canvas** (unlike UV1, which would require
    `canvas.additionalShaderChannels |= TexCoord1` on every canvas incl. the dome), and rewrites
    only on edit via the existing `RelayoutElement` re-mesh. UV1 + additionalShaderChannels stays
    the documented escape hatch if we ever need more channels.
  - **Globally-synchronized animation** (shine-sweep phase, shader-side pulse clock) → one shared
    `Material.SetFloat` per frame — all elements share the clock, per-element strength modulates
    from uv0, batching preserved.
  - **Genuinely per-element animation** (the staggered boot dissolve) → a **transient clone pool**:
    only elements mid-transition get a temporary material clone (or a per-frame uv0 rewrite during
    the transition window); steady state returns everyone to the shared material, restoring
    batching. Transitions last ~2 s, so the extra draw calls are a blip the profiler will show.
  - (`CanvasRenderer` has no MaterialPropertyBlock path — permanent per-element clones would be the
    only alternative, and are avoided by design.)
- Bookkeeping modeled line-for-line on the mode C `_ztestMat`/`_overlayFontMats` machinery:
  create-once, destroy-all in `HudSystem.Shutdown`.
- **Mode C exemption (mandatory):** `ApplyWorldMaterials` must skip graphics carrying a HudFx
  material (or re-wrap them with a ZTest-adjusted clone) and `RestoreWorldMaterials` must restore
  *our* material, not `null`. `RebuildViews`' re-apply path must do the same. Without this, entering
  curvature mode C silently strips every Tier B effect — the audit's top breakage hazard.

### 4.3 The shader (`HudEdgeFX.shader`, UGUI-compatible unlit)
One über-shader with feature toggles (+ variant collection): shine sweep band
(`1-smoothstep(0,w,|proj-pos|)` added ×ownAlpha), dissolve/boot frontier (`clip(noise-amount)` +
`step` edge-glow — ported to unlit UGUI from the lit-surface original), iridescent edge
(`spectral_zucconi6`, synthetic edge normal), radial CA (3-tap, element-UV-centred). Band positions/
amounts/strengths driven per-frame by `HudAnimator` timelines through the shared material + vertex
channel.

### 4.4 Study-first note
Before writing CA from scratch, inspect the game's own `SolarStormChromaticAberration` component
(Beef manipulates it from a mod) — if its shader is reachable and gamma-correct we may reuse the
approach 1:1.

## 5. Tier C workstream (frosted glass)

### 5.1 `HudBackdrop` (new, `UI/Hud/HudBackdrop.cs`)
Static owner of the scene capture + one **downsampled** RT (¼ res default, config) + a
**dual-Kawase** chain (shader in the same bundle), producing one shared `_UiaBlurTex`.

**Capture point = a decision gate, not a commitment.** Two candidates, and step 1 of Tier C is a
trivial tint-blit probe that picks empirically:
1. **CommandBuffer at `CameraEvent.AfterImageEffects`** on the main camera
   (`CameraController.CurrentCamera`, the try/catch acquisition mode C already uses) — surface is
   unclaimed by other mods, but Built-in-RP ordering vs the game's `OnRenderImage` effect chain
   (SSAOPro/UltimateBloom/AA) and the final-blit backbuffer semantics are subtle enough that "sees
   the FINAL post-processed scene" must be proven in-game, not assumed.
2. **A last-ordered `OnRenderImage` component** (pass-through `Blit(src,dst)` + downsample from
   `src`) — the exact mechanism Beef's mods prove works on this game's camera, and it guarantees
   seeing everything rendered before it by construction.
- **Idempotent attach every frame** (the `EnsureWorldCanvasParented` pattern) — cameras rebuild on
  world load; re-attach after `CameraController.ManagerAwake` (Beef's re-apply precedent).
- **Throttle:** re-blur every N frames (config, default 2–3); RT recreated on resolution change
  (copy the `UpdateDome` pattern).
- **Teardown in BOTH paths:** `HudSystem.Shutdown` AND the `!enabled/!CanDraw` stand-down branch
  (which today doesn't release RTs) → remove CommandBuffer from every camera it touched, release +
  destroy RTs, null statics. A CommandBuffer left on the vanilla camera after F6 references dead RTs
  every frame — same failure class as the documented `Camera.onPreCull` rule.

### 5.2 Frost consumption
`PanelGraphic` gains a frost mode: when active, the panel's material is the shared
`HudGlass.shader` material sampling `_UiaBlurTex` by screen position ×tint×darken + fill (per-element
strength via the §4.2 uv0 channel). Per-element `fxFrost`/`fxFrostAmt`; global master
`FrostEnabled` + tint/darken/downsample/throttle knobs.

**Curvature bonus (verified reasoning):** `VisorWarp` displaces vertices *before* the shader runs,
so the shader's screen position is the WARPED position — frost automatically samples the blur RT at
the visually-correct spot under vertex-warp curvature. No double-warp handling needed in mode A.

### 5.3 Gamma correctness
Project is **gamma** color space: the captured RT must be created without sRGB conversion surprises
(`RenderTextureReadWrite.Default` in gamma space) — the §6.3 A/B protocol includes a visual
wash-out/over-darken check; if it looks wrong, flip to explicit `Linear`/`sRGB` and re-verify.

### 5.4 Curvature gating
0.9.0 ships Tier C **only in Flat and VertexWarp (A) modes**. Modes B/D render the HUD into their own
RT via `_rtCam` (frost UVs would be re-warped by the dome composite; plus two more screen-sized RTs
stack on the existing one) and mode C is world-space — deferred, listed as a known limitation in the
F9 UI (frost toggle shows "(Flat/Warp only)" when another mode is live).

## 6. Profiler workstream

### 6.1 Vendor + wire (prerequisite: Jackson's written OK — ask him now)
- `Source/ProfilicusUniversalis.cs` → `Assets/Scripts/StationeersUIMod/Profiling/` with attribution
  header; namespace → `StationeersUIMod.Profiling`; `Configure("UIA Profiler", "StationeersUIMod")`
  in `OnLoaded` (unique title avoids ImGui window collision if TrainMod ever runs alongside).
- `Draw()` called from our existing `Patch_ImGuiWindowManager_Draw` postfix. Console:
  `uiaprof [on|off|clear|save|ab <effect>]` added to our existing `Patch_CommandLine_Process`
  prefix (his `AddCommand` file dropped — hot-reload-fatal). Teardown: `SetVisible(false)` +
  `Clear()` in `OnDestroy` before `UnpatchSelf`.
- F9 gets a **"Profiler"** CollapsingHeader (show/hide button + live one-line HUD-total readout);
  snapshots redirect to `BepInEx/config/StationeersUIMod/ProfilerSnapshots/`.

### 6.2 Instrumentation map (static metric names only — never per-element)
- `Frame.Total` — auto-recorded every frame while enabled (`Time.unscaledDeltaTime·1000`) — **the
  baseline every effect is judged against** (must be ADDED; the profiler doesn't self-record it).
- `Hud.Update.Total` + phase scopes at the existing boundaries in `HudSystem.Update`: `Hud.Sample`,
  `Hud.Visibility`, `Hud.Relayout` (the hitch candidate), `Hud.Content` (+ per-widget-TYPE names
  cached on `HudPanel` at construction, e.g. `Hud.Widget.CompassWidget`), `Hud.Anim`.
- `Hud.Mesh.Panel` / `Hud.Mesh.Polyline` in the two `OnPopulateMesh` bodies (Calls/s + Max/frame are
  purpose-built to expose rebuild storms from careless Tier A animation; note: these attribute to
  the next frame — irrelevant for 10s averages, commented in code).
- `Fx.Blur.CmdBuild` / `Fx.Blur.Dispatch` (Tier C CPU side), `Fx.Shine`, `Fx.Dissolve` etc. around
  any per-frame C# the effects add. `RtCam.Render` around the existing `_rtCam.Render()`.

### 6.3 The A/B protocol (how we prove "not hurting the game")
CPU scopes can't see GPU cost, so every effect's cost claim comes from **frame-time deltas**:
`uiaprof ab <effect>` drives: toggle effect → `SetScenario("X on")` → `Clear()` → warm up N frames →
capture M frames → `SaveSnapshot()` → flip → repeat → emit a combined delta table. The
`Frame.Total` difference between scenarios (vsync off) IS the end-to-end cost including GPU. Bonus:
runtime-probe `FrameTimingManager.gpuFrameTime` for a real GPU row where the player build allows it;
fall back silently. **Budgets (proposed):** `Hud.Update.Total` ≤ 1.0 ms avg; each Tier B effect
≤ 0.2 ms frame-delta; Tier C frost ≤ 1.5 ms frame-delta at ¼ res on the dev machine — numbers
recorded per Changes Report, revisited on min-spec.

## 7. No-breakage guarantees (the "make sure it won't totally break everything" section)

1. **Kill-switches at three levels:** per-element checkbox → per-effect global → per-tier master →
   plus the existing HUD master. Any regression is one toggle away from vanilla-0.8.0 behaviour;
   defaults chosen so a fresh install without the bundle behaves exactly like 0.8.0 + Tier A
   niceties.
2. **Profile format:** Params-bag keys only; zero enum/attribute changes; old↔new build round-trips
   verified in §9. **Plus hardening:** `HudProfileStore.LoadActive` renames an unparseable profile
   to `<name>.bak` before writing the starter — converting the audit's "old build destroys the
   user's file" hazard into a recoverable one.
3. **Bundle-missing path:** Tier B/C never assume the bundle. `TierBAvailable=false` → Tier A
   fallbacks everywhere; the F9 rows grey out with "(bundle not loaded)". A future game Unity
   upgrade that invalidates the bundle degrades the mod, never breaks it.
4. **Mode C/B/D coexistence:** the §4.2 exemption contract + §5.4 gating; entering/leaving every
   curvature mode with every effect on is an explicit test row.
5. **Hot-reload matrix (double-F6):** every new static (`HudShaderStore`, `HudFxMaterials`,
   `HudBackdrop`, profiler, animator timelines) resets in `HudSystem.Shutdown`/`OnDestroy`;
   CommandBuffer removed; RTs released; bundle Unloaded-or-reused. Shutdown is re-entered
   mid-session by the DocumentMode flip — everything must rebuild lazily (the `EnsureBuilt`
   pattern).
6. **MP-safety:** unchanged by construction — all of this is presentation; zero game-state paths
   touched.
7. **Fail-soft patches:** any new Harmony surface goes through `PatchHarness.TryPatchAll` (currently
   the plan needs zero new game patches — profiler and effects ride existing hooks).

## 8. Packaging & release engineering (prerequisites, in order)

1. **Fix the dev-shim bug:** `#if`-gate `Dev/ScriptEngineLoader.cs` out of release builds (or
   package the Unity asmdef build) so SLP's DefaultEntrypoint — with `ModData` — initializes the mod
   in production. Plan the one-time config migration from `com.stationeersuimod.ui.scriptengine.cfg`.
2. **Bundle build script:** `Assets/Editor/BuildUiaEffectsBundle.cs` —
   `BuildPipeline.BuildAssetBundles(out, ChunkBasedCompression, StandaloneWindows64)`, output named
   `uia_effects.bundle`, containing `HudEdgeFX.shader`, `HudGlass.shader`, `HudBlur.shader`, the
   `ShaderVariantCollection`, (later) noise textures. Add `UnityEngine.AssetBundleModule` to the Dev
   csproj refs when the loading code lands.
3. **About.xml:** add `<ModID>` (stable SLP config identity + dependency addressability); delete the
   inert `stationeersmods` marker; leave the legacy dependency elements or rename to SLP's
   (`DependsOn`/`OrderBefore`/`OrderAfter`) after one check against SLP docs (the source dump was
   incomplete there).
4. **Package layout:** `StationeersUIMod/{About/, StationeersUIMod.dll, uia_effects.bundle,
   HudProfiles/}` — SLP ignores unknown non-`.assets`/non-`.dll` files, and `slp modpkg` zips the
   whole folder for server packages automatically.

## 9. Test plan (0.9.0 exit criteria)

**Automated-ish (dev):** clean build 0/0; double-F6 with every effect enabled (no stale canvases,
CommandBuffers, bundles, or profiler windows); profile round-trip old↔new build (params preserved,
`.bak` hardening fires on a poisoned file); bundle-missing run (rename it) → Tier A fallback, no
errors; `uiaprof ab` on every effect → snapshot deltas within §6.3 budgets.

**Play-test checklist (FlorpyDorp):**
1. F9 → Effects (global): every toggle flips its effect live; per-element rows override; bulk-apply
   buttons undo correctly.
2. Existing profiles (4.0, Glassy 4.0, custom) look **identical** with all new toggles off; identical-
   but-crisper with Tier A on (hairlines now fade instead of vanish).
3. Frost on (Flat + mode A): panels blur/tint the world; compass/moodlets/text stay legible; toggle
   updates within a frame or two of the throttle; FPS delta reported by the profiler matches feel.
4. Curvature tour: A→B→C→D→Flat with all effects on — no material clobber (Tier B looks survive C),
   frost auto-disables gracefully in B/C/D, return to A restores everything.
5. Suit death / boot with dissolve reveal + glitch + collapse all enabled — sequencing sane at
   per-element strengths, profiler shows the transient not a steady cost.
6. MP client smoke test: join a server, HUD identical, no new traffic (there is none by design).
7. World unload → main menu → reload: CommandBuffer/camera hooks reattach; no NRE spam; borrowed
   vanilla objects still restore (0.8.0 invariant).

**Version/process:** 0.9.0 Experimental tag only after FlorpyDorp signs off the checklist; Changes
Report per workstream (mandatory); every effect's measured cost table goes in the report.

## 10. Order of implementation

1. **Packaging prerequisites** (§8.1–8.3) + **profiler vendor** (§6.1) — the measuring stick comes
   first, and Tier A needs it on day one.
2. **Tier A** (all three effects) + F9 Effects section + per-element rows — F6-iterable, immediately
   testable, instrumented from birth.
3. **Bundle infrastructure** (§4.1–4.2, build script, mode C exemption) with a trivial test shader —
   prove load/fallback/hot-reload before any real effect.
4. **Tier B effects** (shine → dissolve → iridescence → CA), one at a time, each A/B-profiled.
5. **Tier C** (`HudBackdrop` → frost consumption → optional bloom), gated, profiled, then tuned
   (downsample/throttle) against the budget.
6. **Exit criteria** (§9) → FlorpyDorp play-test → 0.9.0 Experimental release call.

## Verification status

- Foundations: **5-agent line-level audit** (Beef's mods, Jackson's profiler, SLP source, our
  codebase) — every §0 claim carries file:line citations.
- **Full 3-lens adversarial verify COMPLETED 2026-07-13** (opus fleet: claim accuracy / missed
  interactions / feasibility attack). Verdict: *"not fatal — the plan is unusually well-grounded
  … no technical blocker refutes the version"*, with binding corrections consolidated in **§12**
  below. §12 OVERRIDES earlier sections where they conflict.

## 12. Adversarial-verify results — BINDING AMENDMENTS (override earlier sections)

### 12.1 Owned-graphic scoping (CRITICAL — protects the borrow widgets)
The uv0/material scheme applies **ONLY to graphics our widgets create** — concretely
`PanelGraphic`/`PolylineGraphic` (and, if ever needed, our other custom mesh graphics), **never via
a `GetComponentsInChildren<Graphic>` subtree walk**. Excluded by construction: **borrowed vanilla
graphics** (moodlet cells — their uv0 IS the sprite-atlas UV; damage-doll parts; the CLAUDE.md
BORROW rule forbids touching their materials at all), vanilla `Image` icons (HandBoxes /
EquipmentColumn / PrimitiveView icons), `RawImage` (portrait holo, RtFlat), `ScanlineGraphic`,
`DomeDisplayGraphic` (real texture UVs). Mechanism: a **marker interface/component on UIA-owned
effect-capable graphics**; both the effect assignment AND mode C's `ApplyWorldMaterials` walk skip
anything unmarked. Borrow round-trip tests assert material + uv0 untouched. F9 hides effect rows an
element can't honor.

### 12.2 uv0 is NOT free real estate — two-channel split
`PanelGraphic` uses uv0 as a center/edge marker (`Vector2.zero/one`), `RadialWedgeGraphic` encodes a
stop index in uv0.y, `DomeDisplayGraphic`/`Image`s carry real UVs. Amended scheme: **uv0.x = effect
strength, uv0.y = the legacy marker value (preserved)** — applied only within the §12.1 scope.
Threading the strength scalar through every `AddVert` in PanelGraphic/PolylineGraphic/HudGlyphs/
ThresholdBar is an error-prone edit: do it via a single shared emit-helper per file, not per-line.

### 12.3 Animated-params architecture (prescriptive, replaces §4.3's loose wording)
- **Global clock** (shine sweep phase): ONE shared `Material.SetFloat` per frame — never per-element.
- **Per-element static strength**: uv0.x, rewritten only on edit-time re-mesh (`RelayoutElement`).
- **Per-element phase** (staggered boot dissolve): **transient clone pool** during transitions only.
- **Pulse**: `canvasRenderer.SetColor` uniform multiply — **no re-mesh, and NOT
  `CanvasGroup.alpha`** (HudAnimator owns it), **not `localScale`** (CRT collapse owns it), **not
  `anchoredPosition`** (HudGlitch owns it). One writer per channel, always.

### 12.4 Tier C capture: OnRenderImage is PRIMARY (CommandBuffer demoted to experiment)
The game's post-FX are all legacy `OnRenderImage` components; at `AfterImageEffects` the bound
target is the back buffer — **not reliably sample-able across GPUs**. A **last-ordered
`OnRenderImage` component** (Beef-proven on this exact camera) reads a guaranteed sample-able RT
containing the final post-FX. "Unclaimed CommandBuffer surface" was the wrong selection reason —
back-buffer readback is the real risk. Multi-mod chain ordering (Beef's Sunglasses/DOF/SEGI live on
the same camera): keep our capture last via idempotent re-attach (the `EnsureWorldCanvasParented`
pattern); coexistence test with Beef's mods installed.

### 12.5 Y-flip reconciliation (the "most likely first-try failure")
Built-in-RP captures are commonly Y-flipped vs screen UVs. The frost shader derives screen UV from
`ComputeScreenPos`/clip-space (NOT uv0), and must handle `UNITY_UV_STARTS_AT_TOP` /
`_ProjectionParams.x` / `_MainTex_TexelSize.y<0` between the captured RT and the overlay sample.
**"Orientation correct" is an explicit exit criterion of the §5.1 tint-blit probe**, alongside
"sees the final scene".

### 12.6 Mode C contract — fix the latent bug first, then the exemption
Pre-existing bug found: `RebuildViews`' `ApplyWorldMaterials()` call is a **guarded no-op**
(`_worldMatsApplied` is never reset), so structural edits in mode C leave new elements unswapped
(z-fighting) — TODAY, without any of our changes. Fix order: (1) make RebuildViews actually
re-apply (reset the guard or Restore+Apply); (2) the §12.1 marker exemption; (3) Tier B in mode C
uses **per-family ZTest-Always clones** tracked and destroyed like `_overlayFontMats` — accept that
**batching is not preserved for Tier B while in mode C** (documented, profiler-measured); (4)
`HudEdgeFX.shader` must author a **`unity_GUIZTestMode` property** (like UI/Default) or ungated
effects z-fight in world space regardless.

### 12.7 Backdrop teardown on live curvature switch
§5.1's Shutdown + stand-down triggers do NOT fire on a live Flat→B/C/D switch. Add backdrop
stand-down (remove capture component/CB, release blur RTs, swap frosted panels to default material)
**inside `ApplyCurvature`'s `mode != _appliedMode` block**, and expose a proper applied-mode
accessor (the F9 window currently reads the *desired* `HudConfig.Curvature`, which can lag).

### 12.8 Profiler corrections
- **`Frame.Total` records from the plugin `Update()` path** (guarded by `CanDraw`), NOT the ImGui
  postfix — else hiding ImGui (F1) silently breaks `uiaprof ab` captures.
- A/B wording: frame-delta is a **noisy proxy** for GPU cost (accurate when GPU-bound, vsync off);
  the optional API is `FrameTimingManager.CaptureFrameTimings()` + `GetLatestTimings()` →
  `FrameTiming.gpuFrameTime`, which may return 0 on shipped builds — silent fallback stands.
- Profiler window is suppressed on loading screens (DrawOverlay early-out) — acceptable, noted.

### 12.9 Misc corrections
- **Headless server:** gate `HudShaderStore.Load` (and all material creation) behind
  `GameManager.IsBatchMode` — `OnLoaded` runs on dedicated servers too.
- **Mask safety:** effect materials must skip mask-clipped graphics (`GetComponentInParent<Mask>`,
  the mode-C precedent) — the Portrait's circular clip is the live case. Portrait-under-effects is
  a test row.
- **Config identity after the shim fix:** with no `<ModID>`, SLP binds config to
  `StationeersUIMod.StationeersUIMod.cfg` (Type.FullName) — adding `<ModID>` (§8.3) makes it
  `<ModID>.cfg`; the §8.1 migration targets THAT name, not the scriptengine one.
- **Bundle build reality:** local editors are 2022.3.62f3/6000.3 — NOT 2022.3.7f1. We do NOT open
  the main project with a newer editor. The bundle builds from a dedicated mini content-project
  under `Dev/` with 2022.3.62f3, resting on Beef's shipped proof that .62-built bundles load in the
  7f1 game; installing 2022.3.7f1 via Unity Hub for byte-exact builds is the optional
  belt-and-braces upgrade. Fail-soft loading protects us either way.

## 11. Open questions — ANSWERED by FlorpyDorp (2026-07-13)

1. **Jackson's permission:** ✅ GRANTED — Jackson gave Profilicus to FlorpyDorp specifically for this
   mod; they are working together on it. Attribution headers + About.xml/CHANGELOG credit still go
   in as a courtesy.
2. **Default states:** ✅ AGREED — Tier A on / Tier B on-when-available / Tier C off.
3. **Profiler snapshots:** ✅ `BepInEx/config/StationeersUIMod/ProfilerSnapshots/` (not Documents).
4. **GlitchShader knob:** FlorpyDorp delegated → decision: **RETIRE it** (dead since the
   CameraFilterPack screen-shader approach was abandoned; the working HudGlitch knobs and the new
   effect system's own settings supersede it; the orphaned cfg value is harmless).
5. **Frost defaults:** ✅ ~70–85% darken but **more GRAY than blue-black** (less blue tint than the
   concept art); fully adjustable via the F9 knobs regardless.
