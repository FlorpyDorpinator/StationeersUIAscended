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

        private static readonly int _idOffset = Shader.PropertyToID("_Offset");
        private static readonly int _idThreshold = Shader.PropertyToID("_BloomThreshold");
        private static readonly int _idKnee = Shader.PropertyToID("_BloomKnee");
        private static readonly int _idStrength = Shader.PropertyToID("_BloomStrength");
        private static readonly int _idTint = Shader.PropertyToID("_BloomTint");
        private static readonly int _idSaturation = Shader.PropertyToID("_BloomSaturation");

        // Tint parse cache: ColorUtility parses only when the config STRING changes (a per-frame
        // parse would allocate). White fallback on garbage input, matching FrostTint's behaviour.
        private static string _tintStr;
        private static Color _tint = Color.white;

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
                    int divisor = HudConfig.FxBloomFineDetail == null || HudConfig.FxBloomFineDetail.Value ? 2 : 4;

                    EnsurePyramid(hudRt.width, hudRt.height, steps, divisor);
                    if (_down == null || _down.Length == 0 || _down[0] == null) return;

                    // Uniforms (one SetFloat each — no per-frame allocation).
                    _bloomMat.SetFloat(_idThreshold, HudConfig.FxBloomThreshold != null ? HudConfig.FxBloomThreshold.Value : 0.55f);
                    _bloomMat.SetFloat(_idKnee, HudConfig.FxBloomKnee != null ? HudConfig.FxBloomKnee.Value : 0.5f);
                    _bloomMat.SetFloat(_idStrength, HudConfig.FxBloomStrength != null ? HudConfig.FxBloomStrength.Value : 0.8f);
                    _bloomMat.SetFloat(_idSaturation, HudConfig.FxBloomSaturation != null ? HudConfig.FxBloomSaturation.Value : 1f);
                    string ts = HudConfig.FxBloomTint != null ? HudConfig.FxBloomTint.Value : "#FFFFFF";
                    if (!ReferenceEquals(ts, _tintStr))
                    {
                        _tintStr = ts;
                        if (!ColorUtility.TryParseHtmlString(ts, out _tint)) _tint = Color.white;
                    }
                    _bloomMat.SetColor(_idTint, _tint);
                    // The Kawase sample offset is the CONTINUOUS width knob (play-test ask: "can bloom
                    // step be more adjustable than integers?" — steps double the reach; this slides
                    // smoothly between the doublings).
                    float spread = HudConfig.FxBloomSpread != null ? HudConfig.FxBloomSpread.Value : KawaseOffset;
                    _blurMat.SetFloat(_idOffset, Mathf.Clamp(spread, 0.5f, 3f));

                    // Bright-pass: extract + downsample the HUD RT into the pyramid base.
                    Graphics.Blit(hudRt, _down[0], _bloomMat, PassBright);

                    // Dual-Kawase DOWN (blur pass 0), successively halving.
                    for (int i = 1; i < _down.Length; i++)
                        Graphics.Blit(_down[i - 1], _down[i], _blurMat, 0);

                    // Dual-Kawase UP (blur pass 1) back to base res. _up[i] matches _down[i]'s size.
                    RenderTexture prev = _down[_down.Length - 1];
                    for (int i = _up.Length - 1; i >= 0; i--)
                    {
                        Graphics.Blit(prev, _up[i], _blurMat, 1);
                        prev = _up[i];
                    }

                    RenderTexture top = _up.Length > 0 ? _up[0] : _down[0];

                    // Additive composite the blurred glow back onto the HUD RT (Blend One One in the
                    // shader). hudRt is guaranteed non-MSAA by the caller, so this write is safe.
                    Graphics.Blit(top, hudRt, _bloomMat, PassComposite);
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
        private static void EnsurePyramid(int rtW, int rtH, int steps, int divisor)
        {
            int bw = Mathf.Max(1, rtW / divisor);
            int bh = Mathf.Max(1, rtH / divisor);
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
            if (_bloomMat != null) { try { UnityEngine.Object.DestroyImmediate(_bloomMat); } catch { } _bloomMat = null; }
            if (_blurMat != null) { try { UnityEngine.Object.DestroyImmediate(_blurMat); } catch { } _blurMat = null; }
            _tintStr = null;
            _tint = Color.white;
        }
    }
}
