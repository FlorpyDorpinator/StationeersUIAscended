using System;
using StationeersUIMod.Core;       // UIALog, HudShaderStore
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Stage 2 HUD bloom (0.9.0) — bright HUD pixels light their neighbours. The HUD is rendered
    /// into a screen-sized RenderTexture (HudSystem routes EVERY curvature mode through one — the
    /// dome/curved RT modes natively, flat/vertex-warp are forced onto the RT path while bloom is
    /// on). Right after <c>_rtCam.Render()</c> fills that RT, <see cref="Dispatch"/> runs a
    /// bright-pass + dual-Kawase blur and composites the glow ADDITIVELY back onto the SAME RT.
    /// Because the bloom is baked into the HUD RT before presentation, every mode (dome grid,
    /// curved flat, forced flat) carries it, warped consistently with the HUD — no separate
    /// composite layer, no misalignment.
    ///
    /// Mirrors <see cref="HudBackdrop"/>'s discipline: persistent RT pyramid built with
    /// <c>new RenderTexture</c> and released in teardown; lazy materials; <see cref="RenderTextureReadWrite.Default"/>
    /// (gamma project — blur+additive in gamma read fine); zero steady-state GC alloc after warmup;
    /// every static reset in <see cref="Shutdown"/> for F6 hot-reload. Unlike HudBackdrop this owns
    /// NO camera hook, so there is no dead-assembly component to strip — Dispatch is pure on-demand
    /// blit, and simply not being called (world unload) is a complete stand-down.
    ///
    /// Fail-soft: no bloom shader OR no blur shader (bundle missing) ⇒ <see cref="Available"/> is
    /// false and the caller never routes to the RT for bloom's sake / never dispatches.
    ///
    /// MSAA: the additive composite writes back INTO the source RT. Blitting into an MSAA target is
    /// GPU-flaky in the Built-in RP, so HudSystem allocates the HUD RT WITHOUT MSAA whenever bloom is
    /// active (mode D drops its 4x MSAA while bloom runs — the glow softens edges anyway, and the HUD
    /// graphics carry their own feather AA). Dispatch therefore always receives a plain RT.
    /// </summary>
    public static class HudBloomFx
    {
        // Dual-Kawase spread per pass (matches HudBackdrop / plan §12.4). The pyramid DEPTH is the
        // user's FxBloomBlurSteps (1..5); the base level starts at half- or quarter-res of the HUD
        // RT: HALF (FxBloomFineDetail, default) keeps 1-2px borders/lines above the threshold —
        // at quarter-res a thin bright line averages to ~25% brightness before extraction and
        // silently drops out of the bloom (play-test round 15).
        private const float KawaseOffset = 1.5f;

        // Bloom bright-pass / composite material (from HudShaderStore.BloomShader).
        private static Material _bloomMat;
        // Dual-Kawase pyramid material — a SECOND instance of the shared BlurShader (HudBackdrop owns
        // its own; materials are independent, so reusing the shader is free and conflict-free).
        private static Material _blurMat;

        private static RenderTexture[] _down;   // [0] = bright base, each further level halves
        private static RenderTexture[] _up;      // upsample buffers matching _down[0..steps-1]
        private static int _baseW, _baseH, _steps;
        // Second bloom band ("border/highlight bloom") — its own pyramid so its reach/width are
        // independent. Same base dims as band 1 (shares the anamorphic squash).
        private static RenderTexture[] _down2;
        private static RenderTexture[] _up2;
        private static int _baseW2, _baseH2, _steps2;

        private static readonly int _idOffset = Shader.PropertyToID("_Offset");
        private static readonly int _idThreshold = Shader.PropertyToID("_BloomThreshold");
        private static readonly int _idKnee = Shader.PropertyToID("_BloomKnee");
        private static readonly int _idStrength = Shader.PropertyToID("_BloomStrength");
        private static readonly int _idTint = Shader.PropertyToID("_BloomTint");
        private static readonly int _idSaturation = Shader.PropertyToID("_BloomSaturation");
        // Saturation selectivity of the bright-pass (2026-07-15+ bundle; SetFloat on a missing
        // property is silently ignored, so an older resident bundle degrades to no-op).
        private static readonly int _idSatBias = Shader.PropertyToID("_BloomSatBias");

        // Tint parse cache: ColorUtility parses only when the config STRING changes (a per-frame
        // parse would allocate). White fallback on garbage input, matching FrostTint's behaviour.
        private static string _tintStr;
        private static Color _tint = Color.white;
        private static string _tint2Str;
        private static Color _tint2 = Color.white;

        // ── State-reactive inputs, pushed by HudSystem each frame right before Dispatch (the
        // snapshot lives there). Read-only here; folded into strength/tint only when the react
        // toggle is on. Reset in Shutdown so a hot-reload never carries stale state.
        /// <summary>Suit battery percent 0..100, or -1 when unknown/no suit.</summary>
        public static float ReactPowerPct = -1f;
        /// <summary>True while a critical state is active (low power / critical health).</summary>
        public static bool ReactAlarm;
        /// <summary>Boot-sequence envelope 1 → 0 while the boot dissolve plays, else 0.</summary>
        public static float BootFlare01;

        private const int PassBright = 0;
        private const int PassComposite = 1;

        /// <summary>Both shaders resolved (bundle loaded). The bright/composite shader AND the blur
        /// shader for the pyramid are required; either missing ⇒ bloom is silently unavailable.</summary>
        public static bool Available
        {
            get { return HudShaderStore.BloomShader != null && HudShaderStore.BlurShader != null; }
        }

        /// <summary>
        /// Bright-pass + blur the HUD RT and add the glow back onto it. Called ONCE per frame right
        /// after <c>_rtCam.Render()</c>, for every RT-routed curvature mode, when bloom is active.
        /// No-op (fail-soft) if the shaders are gone or the RT is null. Never throws to the caller.
        /// </summary>
        public static void Dispatch(RenderTexture hudRt)
        {
            if (hudRt == null || !Available) return;

            using (Profiling.ProfilicusUniversalis.Time("Fx.Bloom.Dispatch"))
            {
                try
                {
                    EnsureMaterials();
                    if (_bloomMat == null || _blurMat == null) return;

                    int steps = 3;
                    if (HudConfig.FxBloomBlurSteps != null)
                        steps = Mathf.Clamp(HudConfig.FxBloomBlurSteps.Value, 1, 5);

                    // Base resolution: the explicit mode wins (0 full / 1 half / 2 quarter);
                    // -1 keeps the legacy FineDetail toggle's half/quarter behaviour.
                    int res = HudConfig.FxBloomRes != null ? HudConfig.FxBloomRes.Value : -1;
                    float divisor = res == 0 ? 1f : res == 1 ? 2f : res == 2 ? 4f
                        : (HudConfig.FxBloomFineDetail == null || HudConfig.FxBloomFineDetail.Value ? 2f : 4f);

                    // Anamorphic streak: downsample ONE axis harder — each blur tap then covers
                    // more screen distance along it, so the glow smears into streaks. +1 =
                    // horizontal streaks (x squashed), -1 = vertical halation. Free: it is just
                    // an asymmetric pyramid; EnsurePyramid rebuilds only when the sizes change.
                    float ana = HudConfig.FxBloomAnamorph != null ? HudConfig.FxBloomAnamorph.Value : 0f;
                    float dx = divisor * (ana > 0f ? 1f + 2.2f * ana : 1f);
                    float dy = divisor * (ana < 0f ? 1f - 2.2f * ana : 1f);

                    EnsurePyramid(Mathf.Max(1, Mathf.RoundToInt(hudRt.width / dx)),
                        Mathf.Max(1, Mathf.RoundToInt(hudRt.height / dy)), steps);
                    if (_down == null || _down.Length == 0 || _down[0] == null) return;

                    // Uniforms (one SetFloat each — no per-frame allocation).
                    _bloomMat.SetFloat(_idThreshold, HudConfig.FxBloomThreshold != null ? HudConfig.FxBloomThreshold.Value : 0.55f);
                    _bloomMat.SetFloat(_idKnee, HudConfig.FxBloomKnee != null ? HudConfig.FxBloomKnee.Value : 0.5f);
                    _bloomMat.SetFloat(_idSaturation, HudConfig.FxBloomSaturation != null ? HudConfig.FxBloomSaturation.Value : 1f);
                    string ts = HudConfig.FxBloomTint != null ? HudConfig.FxBloomTint.Value : "#FFFFFF";
                    if (!ReferenceEquals(ts, _tintStr))
                    {
                        _tintStr = ts;
                        if (!ColorUtility.TryParseHtmlString(ts, out _tint)) _tint = Color.white;
                    }

                    // ── Dynamic strength/tint (0.9.0 round 2). Material params only — the HUD
                    // meshes never rebuild for these, so animating per frame is free. The factor
                    // is computed ONCE (dynMult + alarm tint pull) and applied to BOTH bands, so
                    // pulse/power/alarm/boot move the whole glow coherently.
                    float dynMult = 1f;
                    float alarmPull = 0f;
                    float now = Time.unscaledTime;

                    // Breathing pulse: a slow sine breathes away up to PulseDepth of the glow.
                    if (HudConfig.FxBloomPulseOn != null && HudConfig.FxBloomPulseOn.Value)
                    {
                        float spd = HudConfig.FxBloomPulseSpeed != null ? HudConfig.FxBloomPulseSpeed.Value : 0.25f;
                        float dep = HudConfig.FxBloomPulseDepth != null ? HudConfig.FxBloomPulseDepth.Value : 0.25f;
                        float ph = 0.5f + 0.5f * Mathf.Sin(now * spd * 2f * Mathf.PI);
                        dynMult *= 1f - Mathf.Clamp01(dep) * ph;
                    }

                    // State-reactive: suit power dims, critical alarms pulse red, boot flares.
                    if (HudConfig.FxBloomReactOn != null && HudConfig.FxBloomReactOn.Value)
                    {
                        float pAmt = HudConfig.FxBloomReactPower != null ? HudConfig.FxBloomReactPower.Value : 0.6f;
                        if (pAmt > 0.001f && ReactPowerPct >= 0f)
                            dynMult *= Mathf.Lerp(1f - pAmt, 1f, Mathf.Clamp01(ReactPowerPct / 100f));

                        float aAmt = HudConfig.FxBloomReactAlarm != null ? HudConfig.FxBloomReactAlarm.Value : 0.6f;
                        if (aAmt > 0.001f && ReactAlarm)
                        {
                            // ~1.1 Hz urgency pulse: brighter at the crest and pulled toward red.
                            float ap = 0.5f + 0.5f * Mathf.Sin(now * 1.1f * 2f * Mathf.PI);
                            dynMult *= 1f + aAmt * 0.8f * ap;
                            alarmPull = aAmt * 0.65f * ap;
                        }

                        float bAmt = HudConfig.FxBloomReactBoot != null ? HudConfig.FxBloomReactBoot.Value : 0.8f;
                        if (bAmt > 0.001f && BootFlare01 > 0.001f)
                            dynMult *= 1f + bAmt * 2.5f * Mathf.Clamp01(BootFlare01);
                    }

                    Color alarmRed = new Color(1f, 0.25f, 0.2f);
                    float strength1 = (HudConfig.FxBloomStrength != null ? HudConfig.FxBloomStrength.Value : 0.8f) * dynMult;
                    Color tint1 = alarmPull > 0f ? Color.Lerp(_tint, alarmRed, alarmPull) : _tint;

                    // Second band ("border/highlight bloom"): its own threshold/strength/reach/tint.
                    bool band2 = HudConfig.FxBloom2On != null && HudConfig.FxBloom2On.Value;
                    if (!band2 && _down2 != null) ReleasePyramid2(); // free its RTs while off
                    if (band2)
                    {
                        int steps2 = HudConfig.FxBloom2Steps != null ? Mathf.Clamp(HudConfig.FxBloom2Steps.Value, 1, 5) : 2;
                        EnsurePyramid2(_baseW, _baseH, steps2); // same base dims — shares the anamorphic squash
                        if (_down2 == null || _down2.Length == 0 || _down2[0] == null) band2 = false;
                    }

                    // The Kawase sample offset is the CONTINUOUS width knob (play-test ask: "can bloom
                    // step be more adjustable than integers?" — steps double the reach; this slides
                    // smoothly between the doublings).
                    float spread = HudConfig.FxBloomSpread != null ? HudConfig.FxBloomSpread.Value : KawaseOffset;

                    // BOTH bright-passes extract from the CLEAN HUD RT before either composite
                    // writes back onto it — no feedback between the bands.

                    // Band 1: bright-pass + pyramid (uniforms already set: threshold/knee/sat).
                    _bloomMat.SetFloat(_idStrength, strength1);
                    _bloomMat.SetColor(_idTint, tint1);
                    _bloomMat.SetFloat(_idSatBias, HudConfig.FxBloomSatBias != null ? HudConfig.FxBloomSatBias.Value : 0f);
                    _blurMat.SetFloat(_idOffset, Mathf.Clamp(spread, 0.5f, 3f));
                    Graphics.Blit(hudRt, _down[0], _bloomMat, PassBright);
                    for (int i = 1; i < _down.Length; i++)
                        Graphics.Blit(_down[i - 1], _down[i], _blurMat, 0);
                    RenderTexture prev = _down[_down.Length - 1];
                    for (int i = _up.Length - 1; i >= 0; i--)
                    {
                        Graphics.Blit(prev, _up[i], _blurMat, 1);
                        prev = _up[i];
                    }
                    RenderTexture top = _up.Length > 0 ? _up[0] : _down[0];

                    // Band 2: bright-pass + pyramid with ITS uniforms (higher threshold etc).
                    RenderTexture top2 = null;
                    float strength2 = 0f;
                    Color tint2 = Color.white;
                    if (band2)
                    {
                        string ts2 = HudConfig.FxBloom2Tint != null ? HudConfig.FxBloom2Tint.Value : "#FFFFFF";
                        if (!ReferenceEquals(ts2, _tint2Str))
                        {
                            _tint2Str = ts2;
                            if (!ColorUtility.TryParseHtmlString(ts2, out _tint2)) _tint2 = Color.white;
                        }
                        strength2 = (HudConfig.FxBloom2Strength != null ? HudConfig.FxBloom2Strength.Value : 1.2f) * dynMult;
                        tint2 = alarmPull > 0f ? Color.Lerp(_tint2, alarmRed, alarmPull) : _tint2;
                        float spread2 = HudConfig.FxBloom2Spread != null ? HudConfig.FxBloom2Spread.Value : KawaseOffset;

                        _bloomMat.SetFloat(_idThreshold, HudConfig.FxBloom2Threshold != null ? HudConfig.FxBloom2Threshold.Value : 0.85f);
                        _bloomMat.SetFloat(_idStrength, strength2);
                        _bloomMat.SetColor(_idTint, tint2);
                        _bloomMat.SetFloat(_idSatBias, HudConfig.FxBloom2SatBias != null ? HudConfig.FxBloom2SatBias.Value : 0f);
                        _blurMat.SetFloat(_idOffset, Mathf.Clamp(spread2, 0.5f, 3f));
                        Graphics.Blit(hudRt, _down2[0], _bloomMat, PassBright);
                        for (int i = 1; i < _down2.Length; i++)
                            Graphics.Blit(_down2[i - 1], _down2[i], _blurMat, 0);
                        RenderTexture prev2 = _down2[_down2.Length - 1];
                        for (int i = _up2.Length - 1; i >= 0; i--)
                        {
                            Graphics.Blit(prev2, _up2[i], _blurMat, 1);
                            prev2 = _up2[i];
                        }
                        top2 = _up2.Length > 0 ? _up2[0] : _down2[0];
                    }

                    // Additive composites back onto the HUD RT (Blend One One in the shader), each
                    // band with its own strength/tint. hudRt is guaranteed non-MSAA by the caller.
                    _bloomMat.SetFloat(_idStrength, strength1);
                    _bloomMat.SetColor(_idTint, tint1);
                    Graphics.Blit(top, hudRt, _bloomMat, PassComposite);
                    if (band2 && top2 != null)
                    {
                        _bloomMat.SetFloat(_idStrength, strength2);
                        _bloomMat.SetColor(_idTint, tint2);
                        Graphics.Blit(top2, hudRt, _bloomMat, PassComposite);
                    }
                }
                catch (Exception e)
                {
                    UIALog.Warn("HudBloomFx.Dispatch failed: " + e.Message);
                }
            }
        }

        private static void EnsureMaterials()
        {
            if (_bloomMat == null)
            {
                var sh = HudShaderStore.BloomShader;
                if (sh != null)
                    _bloomMat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "UiaHudBloom" };
            }
            if (_blurMat == null)
            {
                var sh = HudShaderStore.BlurShader;
                if (sh != null)
                    _blurMat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "UiaHudBloomBlur" };
            }
        }

        /// <summary>(Re)builds the RT pyramid on first use and whenever the RT size or step count
        /// changes. Persistent RTs (held across frames) use <c>new RenderTexture</c>; released in
        /// <see cref="ReleasePyramid"/>. No depth/stencil (bloom is colour-only), no mips.</summary>
        private static void EnsurePyramid(int bw, int bh, int steps)
        {
            if (_down != null && _up != null && bw == _baseW && bh == _baseH && steps == _steps) return;

            ReleasePyramid();
            _baseW = bw;
            _baseH = bh;
            _steps = steps;

            _down = new RenderTexture[steps + 1];
            for (int i = 0; i <= steps; i++)
                _down[i] = NewRt(Mathf.Max(1, bw >> i), Mathf.Max(1, bh >> i));

            _up = new RenderTexture[steps];
            for (int i = 0; i < steps; i++)
                _up[i] = NewRt(Mathf.Max(1, bw >> i), Mathf.Max(1, bh >> i));
        }

        private static RenderTexture NewRt(int w, int h)
        {
            // ARGB32 + Default read/write (gamma project — no sRGB conversion surprises). Colour-only.
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                name = "UiaBloom",
                hideFlags = HideFlags.DontSave,
            };
            rt.Create();
            return rt;
        }

        private static void ReleasePyramid()
        {
            ReleaseArray(_down); _down = null;
            ReleaseArray(_up); _up = null;
            _baseW = 0;
            _baseH = 0;
            _steps = 0;
        }

        /// <summary>Band-2 pyramid, mirroring <see cref="EnsurePyramid"/> with its own statics
        /// (its step count is independent, so the arrays can't be shared).</summary>
        private static void EnsurePyramid2(int bw, int bh, int steps)
        {
            if (_down2 != null && _up2 != null && bw == _baseW2 && bh == _baseH2 && steps == _steps2) return;

            ReleasePyramid2();
            _baseW2 = bw;
            _baseH2 = bh;
            _steps2 = steps;

            _down2 = new RenderTexture[steps + 1];
            for (int i = 0; i <= steps; i++)
                _down2[i] = NewRt(Mathf.Max(1, bw >> i), Mathf.Max(1, bh >> i));

            _up2 = new RenderTexture[steps];
            for (int i = 0; i < steps; i++)
                _up2[i] = NewRt(Mathf.Max(1, bw >> i), Mathf.Max(1, bh >> i));
        }

        private static void ReleasePyramid2()
        {
            ReleaseArray(_down2); _down2 = null;
            ReleaseArray(_up2); _up2 = null;
            _baseW2 = 0;
            _baseH2 = 0;
            _steps2 = 0;
        }

        private static void ReleaseArray(RenderTexture[] arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                var rt = arr[i];
                if (rt == null) continue;
                try { rt.Release(); } catch { }
                try { UnityEngine.Object.DestroyImmediate(rt); } catch { }
                arr[i] = null;
            }
        }

        /// <summary>Full teardown for F6 hot-reload / HudSystem.Shutdown: release every RT, destroy
        /// both materials, reset every static. Cheap to re-enter; <see cref="Dispatch"/> rebuilds
        /// everything lazily on the next call.</summary>
        public static void Shutdown()
        {
            ReleasePyramid();
            ReleasePyramid2();
            if (_bloomMat != null) { try { UnityEngine.Object.DestroyImmediate(_bloomMat); } catch { } _bloomMat = null; }
            if (_blurMat != null) { try { UnityEngine.Object.DestroyImmediate(_blurMat); } catch { } _blurMat = null; }
            _tintStr = null;
            _tint = Color.white;
            _tint2Str = null;
            _tint2 = Color.white;
            ReactPowerPct = -1f;
            ReactAlarm = false;
            BootFlare01 = 0f;
        }
    }
}
