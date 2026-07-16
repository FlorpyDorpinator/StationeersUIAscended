using System;
using Assets.Scripts;              // CameraController (namespace Assets.Scripts — verified 27701 decompile)
using StationeersUIMod.Core;       // UIALog, HudShaderStore
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Tier C (0.9.0 "Experimental") — the frosted-glass BACKDROP capture.
    ///
    /// Owns a hidden <see cref="UiaBackdropCapture"/> render hook on the game's MAIN camera that,
    /// throttled to every N frames, downsamples the final rendered scene and runs a small
    /// dual-Kawase blur pyramid. The blurred result is published as the GLOBAL shader texture
    /// <c>_UiaBlurTex</c> (with a <c>_UiaBlurTexFlip</c> orientation flag), which the frost
    /// material on <c>PanelGraphic</c> samples by screen position × tint × darken (see plan §5.2).
    ///
    /// Contract (Master-Plan-0.9.0 §12.4/§12.5/§12.7):
    ///  • Capture via a LAST-ordered <c>OnRenderImage</c> component — the Beef-proven mechanism on
    ///    this exact camera (CommandBuffer/back-buffer readback demoted; not reliably sample-able
    ///    across GPUs). It reads whatever rendered before it; Unity offers no public re-order, so if
    ///    another mod adds its own image effect AFTER ours we cannot detect or fix that — we simply
    ///    document it and re-acquire when the camera itself changes (world reload).
    ///  • ALWAYS pass the game frame through untouched first — Tier C never darkens the scene, even
    ///    when the effect itself no-ops or the owner has been torn down.
    ///  • Fail-soft: no <see cref="HudShaderStore.BlurShader"/> (bundle missing) ⇒ <see cref="Tick"/>
    ///    is a no-op and <see cref="Active"/> stays false. Tier C is silently unavailable.
    ///  • Hot-reload discipline (CLAUDE.md): every static resets in <see cref="Shutdown"/>; the
    ///    render hook is removed from the vanilla camera so a stale component from a dead assembly
    ///    never calls into it. A static <c>_alive</c> flag is belt-and-braces on top of removal:
    ///    a surviving component degrades to a plain pass-through blit rather than NRE-spamming.
    ///
    /// Gamma project: the capture RT is created with <see cref="RenderTextureReadWrite.Default"/>
    /// (no sRGB conversion surprises — plan §5.3). Gated to Flat/VertexWarp curvature by the caller
    /// (HudSystem); modes B/D/C stand this system down via <see cref="StandDown"/> (plan §5.4/§12.7).
    /// </summary>
    public static class HudBackdrop
    {
        // --- dual-Kawase pyramid shape -------------------------------------------------------
        // DownSteps downsample passes + DownSteps upsample passes on top of the base (¼-res default)
        // capture. 2 keeps within the plan's "2–3 iterations" while staying cheap; the base divisor
        // (HudConfig.FrostDownsample, 2/4/8) does the heavy lifting.
        private const int DownSteps = 2;
        private const float KawaseOffset = 1.5f; // texel spread per pass (~1.5, plan §12.4)

        // --- shared/blurred state (all reset in Shutdown) ------------------------------------
        private static bool _alive;                    // false ⇒ capture degrades to pass-through
        private static Camera _camera;                 // camera the hook lives on (identity → reload detect)
        private static GameObject _cameraGO;           // its GO, remembered so we can detach even if _camera is gone
        private static UiaBackdropCapture _capture;    // our render hook
        private static Material _mat;                  // dual-Kawase material (lazy, from BlurShader)
        private static RenderTexture[] _down;          // pyramid: [0] = base (screen/downsample), each halves
        private static RenderTexture[] _up;            // upsample buffers matching _down[0..DownSteps-1]
        private static RenderTexture _result;          // the exposed blurred backdrop (== _up[0] after a dispatch)
        private static int _baseW, _baseH;             // cached base dims → recreate on resolution/downsample change
        private static Camera _lastMain;               // main camera the cached capture-camera resolve was keyed on
        private static Camera _resolvedCam;            // cached ResolveCaptureCamera result (positive matches only)

        private static readonly int _idOffset = Shader.PropertyToID("_Offset");
        private static readonly int _idBlurTex = Shader.PropertyToID("_UiaBlurTex");
        private static readonly int _idBlurTex0 = Shader.PropertyToID("_UiaBlurTex0");
        private static readonly int _idBlurTex1 = Shader.PropertyToID("_UiaBlurTex1");
        private static readonly int _idBlurTex2 = Shader.PropertyToID("_UiaBlurTex2");
        private static readonly int _idBlurTex3 = Shader.PropertyToID("_UiaBlurTex3");
        private static readonly int _idFlip = Shader.PropertyToID("_UiaBlurTexFlip");

        // ---------------------------------------------------------------- public surface

        /// <summary>Capture hook attached, blur material live, and a blurred frame produced.</summary>
        public static bool Active => _alive && _capture != null && _mat != null && _result != null;

        /// <summary>The blurred backdrop texture (null when inactive). Consumers should prefer the
        /// global <c>_UiaBlurTex</c>; this is exposed for diagnostics / the F9 window.</summary>
        public static Texture BlurTex => Active ? _result : null;

        /// <summary>
        /// Y-orientation flag written to the global <c>_UiaBlurTexFlip</c> each dispatch (0 = no flip,
        /// 1 = flip V). Built-in-RP captures are commonly Y-flipped vs screen UVs (plan §12.5, "the most
        /// likely first-try failure"). Heuristic per the audit: an <c>OnRenderImage</c> src on D3D11
        /// matches screen UVs in the common case, so the default (0) is usually correct; other backends
        /// may need 1. This is a public runtime knob a console/debug toggle can flip — the F9 probe
        /// checklist verifies orientation visually and flips this if the frost samples upside-down.
        /// </summary>
        public static float BlurTexFlip { get; set; }

        /// <summary>
        /// Called every frame from HudSystem.Update WHEN Tier C should run (Flat/VertexWarp only).
        /// Idempotent: acquires the main camera, ensures the capture hook exists on it, and re-acquires
        /// when the camera changes (world reload). Fail-soft on a missing bundle or during load.
        /// </summary>
        public static void Tick()
        {
            // Fail-soft: no blur shader (bundle missing) ⇒ Tier C silently unavailable.
            if (HudShaderStore.BlurShader == null) return;
            // Headless server: OnLoaded runs there too, but there is no camera to capture (plan §12.9).
            if (Application.isBatchMode) return;

            try
            {
                Camera main = null;
                try { main = CameraController.CurrentCamera; } catch { main = null; } // GLOBAL-ish; null during load
                if (main == null) return; // still loading — retry next frame (idempotent)

                // Hook the LAST screen-targeted camera, not the main one: the first-person
                // helmet frame is layer-26 geometry drawn by the stacked "ForegroundCamera"
                // AFTER the main camera, so a main-camera capture X-rays the visor (play-test
                // round 12 — frost showed the terrain THROUGH the helmet).
                Camera cam = ResolveCaptureCamera(main);

                // Cameras rebuild on world load (and the rig can change); move our hook.
                if (cam != _camera)
                {
                    DetachComponent();
                    _camera = cam;
                    _cameraGO = cam.gameObject;
                }

                // Ensure the capture component is present (GetComponent ?? AddComponent). Adding last
                // makes us last in the current OnRenderImage chain (see UiaBackdropCapture remarks).
                if (_capture == null)
                {
                    _capture = cam.GetComponent<UiaBackdropCapture>();
                    if (_capture == null) _capture = cam.gameObject.AddComponent<UiaBackdropCapture>();
                    _capture.hideFlags = HideFlags.HideAndDontSave;
                }

                EnsureMaterial();
                _alive = _mat != null; // live only once the blur material exists (else pass-through)
            }
            catch (Exception e)
            {
                UIALog.Warn("HudBackdrop.Tick failed: " + e.Message);
            }
        }

        /// <summary>
        /// Remove the capture hook from ANY camera it was attached to, release + destroy all RTs, null
        /// the statics, and clear the global texture. Called on: curvature switch away from Flat/
        /// VertexWarp, the Tier C config toggle going off, and HudSystem stand-down. Cheap to re-enter;
        /// <see cref="Tick"/> rebuilds everything lazily.
        /// </summary>
        public static void StandDown()
        {
            _alive = false;
            DetachComponent();
            _camera = null;
            _cameraGO = null;
            _lastMain = null;
            _resolvedCam = null;
            ReleasePyramid();
            if (_mat != null) { try { UnityEngine.Object.DestroyImmediate(_mat); } catch { } _mat = null; }
        }

        /// <summary>
        /// Full teardown for F6 hot-reload: <see cref="StandDown"/> plus reset of every remaining static.
        /// A capture component left on the vanilla camera would call into a dead assembly forever — the
        /// same failure class as the Camera.onPreCull rule in CLAUDE.md — so removal here is mandatory.
        /// </summary>
        public static void Shutdown()
        {
            StandDown();
            BlurTexFlip = 0f;
            _baseW = 0;
            _baseH = 0;
            // _alive is already false and all handles nulled by StandDown.
        }

        // ---------------------------------------------------------------- internals

        /// <summary>The camera whose OnRenderImage sees the MOST of the final frame. The game's
        /// main camera NEVER renders the first-person helmet: the visor mesh is forced onto the
        /// "FirstPerson" layer (decompile FirstPersonHelmetOverlay.SetHelmetLayers, layer 26) and
        /// neither of CameraController's runtime culling masks contains that bit — a dedicated
        /// child camera ("ForegroundCamera": depth above main, depth-only clear, culls ONLY that
        /// layer, no target texture) stacks the helmet over the finished world image. Its
        /// depth-only clear means an image-effect source on it is seeded with the prior cameras'
        /// composited color, so hooking THERE captures world + post FX + helmet. Identified
        /// structurally (child, enabled, perspective, on-screen, draws the helmet layer, renders
        /// after main) so a renamed camera still matches; falls back to the main camera when the
        /// rig doesn't match (fail-soft — worst case is the old X-ray behaviour, never a crash).</summary>
        private static Camera ResolveCaptureCamera(Camera main)
        {
            // GetComponentsInChildren allocates a Camera[] every call and this runs every Tick.
            // The rig only changes on world reload, so cache the pick keyed on `main` and re-scan
            // only when `main` changes or the cached camera died (Unity-null). A fallback to `main`
            // is NOT cached — the child capture camera can appear a frame after main, so we keep
            // scanning until it exists, matching the old per-frame upgrade minus the steady-state alloc.
            if (main == _lastMain && _resolvedCam != null) return _resolvedCam;
            Camera cam = ResolveCaptureCameraUncached(main);
            if (cam != main) { _lastMain = main; _resolvedCam = cam; }
            else { _lastMain = null; _resolvedCam = null; }
            return cam;
        }

        private static Camera ResolveCaptureCameraUncached(Camera main)
        {
            try
            {
                int fpBit = 1 << Assets.Scripts.FirstPerson.FirstPersonHelmetOverlay.FirstPersonLayer;
                var kids = main.GetComponentsInChildren<Camera>(false);
                for (int i = 0; i < kids.Length; i++)
                {
                    var k = kids[i];
                    if (k == null || k == main) continue;
                    if (!k.isActiveAndEnabled) continue;
                    if (k.targetTexture != null) continue;      // "ImGui Camera" renders offscreen
                    if (k.orthographic) continue;
                    if ((k.cullingMask & fpBit) == 0) continue; // must draw the helmet layer
                    if (k.depth <= main.depth) continue;        // must render AFTER the world
                    return k;
                }
            }
            catch { /* structure changed — fall back to main */ }
            return main;
        }

        /// <summary>Runs the blur; called from the capture hook's OnRenderImage on the throttle tick.</summary>
        private static void Dispatch(RenderTexture src)
        {
            if (!_alive || _mat == null || src == null) return;
            EnsurePyramid(src.width, src.height);
            if (_down == null || _down.Length == 0 || _down[0] == null) return;

            using (Profiling.ProfilicusUniversalis.Time("Fx.Blur.Dispatch"))
            {
                _mat.SetFloat(_idOffset, KawaseOffset);

                // Capture: bilinear box-downsample the final rendered frame into the base level.
                Graphics.Blit(src, _down[0]);

                // Dual-Kawase DOWN (Pass 0), successively halving.
                for (int i = 1; i < _down.Length; i++)
                    Graphics.Blit(_down[i - 1], _down[i], _mat, 0);

                // Dual-Kawase UP (Pass 1), back up to base resolution. _up[i] matches _down[i]'s size.
                RenderTexture prev = _down[_down.Length - 1];
                for (int i = _up.Length - 1; i >= 0; i--)
                {
                    Graphics.Blit(prev, _up[i], _mat, 1);
                    prev = _up[i];
                }

                _result = _up.Length > 0 ? _up[0] : _down[0];

                // Publish globally. The frost shader derives screen UV from clip-space (NOT uv0) and
                // reconciles orientation with _UiaBlurTexFlip (plan §12.5).
                Shader.SetGlobalTexture(_idBlurTex, _result);
                // Four-rung depth ladder for analytic panels. The legacy final texture above
                // remains the contract for HudGlass and the F10 radial wedges.
                Shader.SetGlobalTexture(_idBlurTex0, _down[0]);
                Shader.SetGlobalTexture(_idBlurTex1, _down.Length > 1 ? _down[1] : _down[0]);
                Shader.SetGlobalTexture(_idBlurTex2, _up.Length > 1 ? _up[1] : _result);
                Shader.SetGlobalTexture(_idBlurTex3, _up.Length > 0 ? _up[0] : _result);
                Shader.SetGlobalFloat(_idFlip, BlurTexFlip);
            }
        }

        private static void EnsureMaterial()
        {
            if (_mat != null) return;
            var sh = HudShaderStore.BlurShader;
            if (sh == null) return; // fail-soft: Active stays false
            _mat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "UiaBackdropBlur" };
        }

        /// <summary>(Re)builds the RT pyramid on first use and whenever the resolution or downsample
        /// divisor changes. Persistent RTs use <c>new RenderTexture</c> (GetTemporary is unsuitable for
        /// RTs held across frames) and are released in <see cref="ReleasePyramid"/> — Beef's teardown pattern.</summary>
        private static void EnsurePyramid(int srcW, int srcH)
        {
            int ds = 4;
            if (HudConfig.FrostDownsample != null)
                ds = Mathf.Clamp(Mathf.RoundToInt(HudConfig.FrostDownsample.Value), 1, 16);
            if (ds < 1) ds = 1;

            int bw = Mathf.Max(1, srcW / ds);
            int bh = Mathf.Max(1, srcH / ds);
            if (_down != null && _up != null && bw == _baseW && bh == _baseH) return; // unchanged

            ReleasePyramid();
            _baseW = bw;
            _baseH = bh;

            _down = new RenderTexture[DownSteps + 1];
            for (int i = 0; i <= DownSteps; i++)
                _down[i] = NewRt(Mathf.Max(1, bw >> i), Mathf.Max(1, bh >> i));

            _up = new RenderTexture[DownSteps];
            for (int i = 0; i < DownSteps; i++)
                _up[i] = NewRt(Mathf.Max(1, bw >> i), Mathf.Max(1, bh >> i));

            _result = null; // no valid blur until the next dispatch fills _up[0]
        }

        private static RenderTexture NewRt(int w, int h)
        {
            // ARGB32 + Default read/write (gamma project — no sRGB conversion surprises, plan §5.3).
            // No depth/stencil (blur is colour-only); no mips.
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                name = "UiaBlur",
                hideFlags = HideFlags.DontSave,
            };
            rt.Create();
            return rt;
        }

        private static void ReleasePyramid()
        {
            // Shader globals outlive the RT objects. Clear every binding BEFORE Release/Destroy so
            // no consumer can sample a dangling native texture during a resize, stand-down, or F6.
            ClearPublishedTextures();
            ReleaseArray(_down); _down = null;
            ReleaseArray(_up); _up = null;
            _result = null;
            _baseW = 0;
            _baseH = 0;
        }

        private static void ClearPublishedTextures()
        {
            try
            {
                Shader.SetGlobalTexture(_idBlurTex, null);
                Shader.SetGlobalTexture(_idBlurTex0, null);
                Shader.SetGlobalTexture(_idBlurTex1, null);
                Shader.SetGlobalTexture(_idBlurTex2, null);
                Shader.SetGlobalTexture(_idBlurTex3, null);
                Shader.SetGlobalFloat(_idFlip, 0f);
            }
            catch { /* fail-soft during graphics teardown */ }
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

        /// <summary>Destroys our render hook wherever it lives. DestroyImmediate so a lingering
        /// OnRenderImage on a swapped-out camera stops blitting THIS frame, not next.</summary>
        private static void DetachComponent()
        {
            if (_capture != null)
            {
                try { UnityEngine.Object.DestroyImmediate(_capture); } catch { }
                _capture = null;
            }
            // Belt-and-braces: sweep any stray copy still on the remembered camera GO.
            if (_cameraGO != null)
            {
                try
                {
                    var stray = _cameraGO.GetComponent<UiaBackdropCapture>();
                    if (stray != null) UnityEngine.Object.DestroyImmediate(stray);
                }
                catch { }
            }
        }

        // ---------------------------------------------------------------- the render hook

        /// <summary>
        /// The hidden image-effect MonoBehaviour on the main camera. Added LAST at <see cref="Tick"/>
        /// time so it sits at the end of the current OnRenderImage chain and thus reads the fully
        /// post-processed scene. Unity exposes no way to re-order image effects, so if another mod
        /// adds its own effect AFTER ours we can neither detect nor correct it — this hook simply reads
        /// whatever rendered before it (documented, accepted). The <c>!_alive</c> guard makes a stale
        /// component (F6 hot-reload) degrade to a plain pass-through blit rather than NRE-spam.
        /// </summary>
        private sealed class UiaBackdropCapture : MonoBehaviour
        {
            private int _frame;
            private bool _warned;

            private void OnRenderImage(RenderTexture src, RenderTexture dst)
            {
                // ALWAYS forward the game frame untouched FIRST — never darken the scene, even if the
                // owner has been torn down or the blur throws below.
                Graphics.Blit(src, dst);

                if (!_alive) return; // dead-assembly / stood-down guard ⇒ pure pass-through

                try
                {
                    int n = (HudConfig.FrostUpdateEveryN != null) ? HudConfig.FrostUpdateEveryN.Value : 2;
                    if (n < 1) n = 1;
                    if (++_frame >= n)
                    {
                        _frame = 0;
                        Dispatch(src);
                    }
                }
                catch (Exception e)
                {
                    if (!_warned) { _warned = true; UIALog.Warn("HudBackdrop capture failed: " + e.Message); }
                }
            }
        }
    }
}
