using System.Collections.Generic;
using Assets.Scripts;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The live 3D player portrait, shown inside the visor as a round cyan HOLOGRAM: the
    /// vanilla portrait camera's RenderTexture clipped to a circle, tinted, and dressed
    /// with scanlines, with a thin rim in the element's border colour. Geometry comes from
    /// the element rect — the circle fills the rect's short side and scales with it.
    ///
    /// The camera's targetTexture is RE-READ EVERY FRAME: vanilla's RefreshPortrait
    /// replaces the RenderTexture wholesale on any HUD-scale or settings change and leaks
    /// the old one, so caching the pointer would show a dead texture. While the hologram is
    /// live the portrait camera GameObject is forced on and the vanilla on-screen portrait
    /// is hidden; <see cref="RestorePortrait"/> hands both back the moment the widget stops
    /// being wanted.
    /// </summary>
    internal sealed class PortraitWidget : HudElementView
    {
        // Fully qualified: `using Assets.Scripts` pulls in the game's own Mask type,
        // which would otherwise win the lookup and has no stencil semantics.
        private UnityEngine.UI.Mask _mask;
        private CircleGraphic _maskShape;   // stencil-only disc that clips the portrait
        private RawImage _holo;             // the portrait RenderTexture, masked to the circle
        private ScanlineGraphic _holoScan;  // CRT dressing, clipped with the portrait
        private CircleGraphic _ring;        // rim border, drawn on top and NOT clipped

        private bool _forcedPortraitCam;
        private bool _hidVanillaPortrait;

        // Portrait camera zoom: FlorpyDorp's F9 controls. FOV is the robust knob (vanilla
        // never rewrites it); a distance nudge along the camera's local forward is offered
        // too but vanilla's SetPortrait re-asserts localPosition on helmet/parent changes,
        // so we RE-APPLY it every frame from the captured base. Originals captured once so
        // teardown restores exactly what vanilla had.
        private bool _camCaptured;
        private float _origFov;
        private Vector3 _origLocalPos;
        private bool _adjustedCam;

        protected override void BuildContent(RectTransform root)
        {
            // The mask disc: a filled CircleGraphic whose only job is to write the circular
            // stencil. showMaskGraphic = false keeps the disc itself invisible; the RawImage
            // and scanlines parented under it are clipped to it.
            var maskGo = new GameObject("PortraitMask", typeof(RectTransform));
            maskGo.transform.SetParent(root, false);
            _maskShape = maskGo.AddComponent<CircleGraphic>();
            _maskShape.raycastTarget = false;
            _maskShape.color = Color.white; // opaque so the stencil is written
            _mask = maskGo.AddComponent<UnityEngine.UI.Mask>();
            _mask.showMaskGraphic = false;
            maskGo.AddComponent<VisorWarp>(); // the stencil must bend WITH the portrait
            var mrt = (RectTransform)maskGo.transform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);

            var holoGo = new GameObject("Hologram", typeof(RectTransform));
            holoGo.transform.SetParent(mrt, false);
            _holo = holoGo.AddComponent<RawImage>();
            _holo.raycastTarget = false;
            holoGo.AddComponent<VisorWarp>();
            var hrt = _holo.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);

            var scanGo = new GameObject("HoloScan", typeof(RectTransform));
            scanGo.transform.SetParent(mrt, false);
            _holoScan = scanGo.AddComponent<ScanlineGraphic>();
            _holoScan.raycastTarget = false;
            scanGo.AddComponent<VisorWarp>(); // bends WITH the hologram underneath
            var srt = (RectTransform)scanGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);

            // The rim sits OUTSIDE the mask subtree so it draws over the clipped edge as a
            // ring: a transparent-fill CircleGraphic that shows only its border.
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            ringGo.transform.SetParent(root, false);
            _ring = ringGo.AddComponent<CircleGraphic>();
            _ring.raycastTarget = false;
            ringGo.AddComponent<VisorWarp>();
            var rrt = (RectTransform)ringGo.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
        }

        public override void Layout(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            Root.anchoredPosition = Vector2.zero;

            // The circle fills the rect's short side and centres in it.
            float d = Mathf.Min(s.x, s.y);
            float r = d * 0.5f;

            var mrt = (RectTransform)_mask.transform;
            mrt.anchoredPosition = c;
            mrt.sizeDelta = new Vector2(d, d);
            _maskShape.SetRadius(r);

            // The UI stencil survives everywhere EXCEPT the dome RT camera (mode B), now
            // that ApplyWorldMaterials exempts mask-clipped graphics from the material
            // swap — so mode C keeps a true circle. Mode B still falls back to the
            // inscribed square (no bleed past the ring).
            var mode = HudConfig.Curvature != null ? HudConfig.Curvature.Value : HudCurvature.Flat;
            bool stencilOk = mode != HudCurvature.DomeProjection;
            _mask.enabled = stencilOk;
            _maskShape.color = stencilOk ? Color.white : Color.clear;
            float side = stencilOk ? d : d * 0.7071f;

            var sq = new Vector2(side, side);
            _holo.rectTransform.anchoredPosition = Vector2.zero;
            _holo.rectTransform.sizeDelta = sq;
            ((RectTransform)_holoScan.transform).anchoredPosition = Vector2.zero;
            ((RectTransform)_holoScan.transform).sizeDelta = sq;

            ((RectTransform)_ring.transform).anchoredPosition = c;
            _ring.SetRadius(r);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            // Re-read the portrait RT every frame; never cache it across frames.
            Texture rt = null;
            try
            {
                var cc = CameraController.Instance;
                var cam = cc != null ? cc.PortraitCamera : null;
                if (cam != null)
                {
                    rt = cam.targetTexture;                     // replaced on every refresh
                    if (!cam.gameObject.activeSelf)
                    {
                        cam.gameObject.SetActive(true);         // vanilla RT stays wired even when off
                        _forcedPortraitCam = true;
                    }
                    ApplyCameraZoom(cam);
                    // The vanilla on-screen portrait would double-show; hide it while the
                    // hologram owns the render (restored via RestorePortrait()).
                    var display = cc.PortraitCameraDisplay;
                    if (display != null && display.activeSelf)
                    {
                        display.SetActive(false);
                        _hidVanillaPortrait = true;
                    }
                }
            }
            catch { }

            // Natural-colour portrait by default; the cyan hologram look is opt-in.
            bool holo = Def.GetB("holo", false);
            bool scanlines = Def.GetB("scanlines", holo);

            _holo.texture = rt;
            _holo.enabled = rt != null;
            // The vanilla portrait RT is NOT square — stretching it into the round frame
            // distorts the character. Center-crop the wider axis via UVs instead.
            if (rt != null && rt.width > 0 && rt.height > 0)
            {
                float aspect = rt.width / (float)rt.height;
                _holo.uvRect = aspect >= 1f
                    ? new Rect(0.5f - 0.5f / aspect, 0f, 1f / aspect, 1f)
                    : new Rect(0f, 0.5f - 0.5f * aspect, 1f, aspect);
            }
            _holo.color = holo ? HudPalette.HologramTint.Value : Color.white;
            _holoScan.color = (_holo.enabled && scanlines) ? HudPalette.Scanline.Value : Color.clear;

            // Rim chrome: transparent fill (alpha 0) so only the border draws as a ring.
            var fill = FillColor();
            fill.a = 0f;
            _ring.color = fill;
            _ring.BorderColor = BorderColor();
            _ring.BorderWidth = BorderWidthFor();
        }

        /// <summary>Apply the F9 zoom knobs to the shared portrait camera. FOV is set
        /// directly (vanilla leaves it alone); a distance nudge pushes the camera along its
        /// own forward from the base local position, re-applied each frame because vanilla's
        /// SetPortrait re-writes localPosition on parent/helmet changes. Both no-op at their
        /// defaults so an untouched portrait keeps vanilla's exact framing.</summary>
        private void ApplyCameraZoom(Camera cam)
        {
            try
            {
                float fov = Def.GetF("camFov", 0f);       // 0 = leave vanilla's FOV
                float dist = Def.GetF("camDistance", 0f); // 0 = leave vanilla's distance
                bool want = (fov > 0f) || !Mathf.Approximately(dist, 0f);
                if (!want && !_adjustedCam) return;

                if (!_camCaptured)
                {
                    _origFov = cam.fieldOfView;
                    _origLocalPos = cam.transform.localPosition;
                    _camCaptured = true;
                }
                if (want)
                {
                    cam.fieldOfView = fov > 0f ? Mathf.Clamp(fov, 3f, 60f) : _origFov;
                    // Positive distance pulls the camera BACK (zoom out) along its forward.
                    cam.transform.localPosition = _origLocalPos - cam.transform.forward * dist;
                    _adjustedCam = true;
                }
                else
                {
                    // Reverted to defaults in the editor — hand the camera back.
                    cam.fieldOfView = _origFov;
                    cam.transform.localPosition = _origLocalPos;
                    _adjustedCam = false;
                }
            }
            catch { }
        }

        /// <summary>Give the portrait back to vanilla. Manual SetActives per the vanilla
        /// setting instead of RefreshPortrait(): vanilla's refresh allocates a brand-new
        /// RenderTexture on every call and never releases the old one — calling it per
        /// show/hide cycle would leak steadily. Falls back to the vanilla path if the manual
        /// route throws. Public because the HUD orchestrator calls it the moment the widget
        /// stops being wanted (tier drop, toggle, or teardown).</summary>
        /// <summary>Self-heal: hand the portrait camera back if our Root is destroyed.</summary>
        protected override void OnBeforeDestroy() => RestorePortrait();

        public void RestorePortrait()
        {
            // Hand back the camera zoom first (independent of the show/hide borrow state).
            if (_adjustedCam && _camCaptured)
            {
                try
                {
                    var cam0 = CameraController.Instance != null ? CameraController.Instance.PortraitCamera : null;
                    if (cam0 != null)
                    {
                        cam0.fieldOfView = _origFov;
                        cam0.transform.localPosition = _origLocalPos;
                    }
                }
                catch { }
                _adjustedCam = false;
            }

            if (!_forcedPortraitCam && !_hidVanillaPortrait) return;
            _forcedPortraitCam = false;
            _hidVanillaPortrait = false;
            try
            {
                var cc = CameraController.Instance;
                if (cc == null) return;
                bool wantOn = true;
                try { wantOn = Assets.Scripts.Serialization.Settings.CurrentData.IngamePortrait; } catch { }
                wantOn &= Core.Guards.LocalHuman != null;
                if (cc.PortraitCamera != null) cc.PortraitCamera.gameObject.SetActive(wantOn);
                if (cc.PortraitCameraDisplay != null) cc.PortraitCameraDisplay.SetActive(wantOn);
            }
            catch
            {
                try { CameraController.RefreshPortrait(); } catch { }
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Hologram tint", () => d.GetB("holo", true), v => d.SetB("holo", v)));
            into.Add(HudProp.Bool("Scanlines", () => d.GetB("scanlines", true), v => d.SetB("scanlines", v)));
            into.Add(HudProp.F("Camera FOV (0 = vanilla)", () => d.GetF("camFov", 0f), v => d.SetF("camFov", Mathf.Clamp(v, 0f, 60f)), 0f, 60f));
            into.Add(HudProp.F("Camera zoom-out", () => d.GetF("camDistance", 0f), v => d.SetF("camDistance", v), -1f, 3f));
        }
    }
}
