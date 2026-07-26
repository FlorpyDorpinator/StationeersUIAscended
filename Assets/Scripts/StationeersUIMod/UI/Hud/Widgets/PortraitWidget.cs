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
        // No UIA panel surface (a masked hologram + a ring), so no fill/glow/frost controls —
        // but the ring DOES draw a real border/colour the base's border-only chrome exposes.
        protected override bool SupportsPanelAppearance => false;
        protected override bool SupportsBorderOnlyChrome => true;

        // No text, no accent-tinted surface, no scalable text anywhere in this widget.
        protected override bool UsesAccentColor => false;
        protected override bool UsesFontScale => false;


        // Fully qualified: `using Assets.Scripts` pulls in the game's own Mask type,
        // which would otherwise win the lookup and has no stencil semantics.
        private UnityEngine.UI.Mask _mask;
        private PortraitMaskGraphic _maskShape;  // stencil-only silhouette that clips the portrait
                                                 // (circle OR one of the angular shapes). Invisible.
        private RawImage _holo;             // the portrait RenderTexture, masked to the silhouette
        private ScanlineGraphic _holoScan;  // CRT dressing, clipped with the portrait
        private PolygonPanelGraphic _ringPoly;   // angular rim: a border-only glass polygon that
                                                 // replaces the circle ring for non-round shapes,
                                                 // carrying the SAME glass edge a panel does.
        private CircleGraphic _ring;        // rim border, drawn on top and NOT clipped. Kept a
                                            // CircleGraphic on purpose: a PanelGraphic forced to a
                                            // full circle is the degenerate case its own mesh warns
                                            // about (corner centres converge -> "four corner bowties"
                                            // the instant any glass sheen is applied). The parity that
                                            // used to be missing was the EDGE TREATMENT, not the mesh:
                                            // CircleGraphic now runs the same sub-pixel policy, key
                                            // light, border fade and halo band a panel does (see
                                            // HudElementView.ApplyBorderOnlyEdge), so the ring reads as
                                            // the same material without inheriting the bowtie. Colours
                                            // still track the palette + alarm pulse via BorderColor().

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
            _maskShape = maskGo.AddComponent<PortraitMaskGraphic>();
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

            // The angular rim: a border-only PolygonPanelGraphic (same rig the pen-tool Shape uses),
            // enabled only for non-round shapes. Starts inactive so the default round portrait never
            // pays for it. Its glass edge is pushed by ApplyGlass() — identical to a following panel.
            var ringPolyGo = new GameObject("RingPoly", typeof(RectTransform));
            ringPolyGo.transform.SetParent(root, false);
            _ringPoly = ringPolyGo.AddComponent<PolygonPanelGraphic>();
            _ringPoly.raycastTarget = false;
            ringPolyGo.AddComponent<VisorWarp>();
            var prt = (RectTransform)ringPolyGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            ringPolyGo.SetActive(false);
        }

        // Scratch for the angular contour (single-threaded UGUI — one list, no per-frame alloc).
        private readonly System.Collections.Generic.List<UnityEngine.Vector2> _polyPts
            = new System.Collections.Generic.List<UnityEngine.Vector2>(8);

        private PortraitShape CurrentShape()
            => (PortraitShape)Mathf.Clamp(Def != null ? Def.GetIFor(LayoutBare, "shape", 0) : 0, 0, 4);

        public override void Layout(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            Root.anchoredPosition = Vector2.zero;

            float w = s.x, h = s.y;
            float d = Mathf.Min(w, h);
            float r = d * 0.5f;

            var shape = CurrentShape();
            bool round = shape == PortraitShape.Round;
            float top = Def != null ? Mathf.Clamp01(Def.GetFFor(LayoutBare, "shapeTop", 0.6f)) : 0.6f;
            float bot = Def != null ? Mathf.Clamp01(Def.GetFFor(LayoutBare, "shapeBot", 1f)) : 1f;

            // The UI stencil survives everywhere EXCEPT the dome RT camera (mode B), now
            // that ApplyWorldMaterials exempts mask-clipped graphics from the material
            // swap — so mode C keeps a true silhouette. Mode B still falls back to the
            // inscribed square (no bleed past the ring), for every shape.
            var mode = HudConfig.Curvature != null ? HudConfig.Curvature.Value : HudCurvature.Flat;
            bool stencilOk = mode != HudCurvature.DomeProjection;

            // Hologram footprint: the shape's bounding box while masked (so the portrait fills the
            // silhouette), else the inscribed square when the stencil is unavailable.
            Vector2 holoSize;
            if (!stencilOk) holoSize = new Vector2(d * 0.7071f, d * 0.7071f);
            else if (round || shape == PortraitShape.Square) holoSize = new Vector2(d, d);
            else holoSize = new Vector2(w, h);

            var mrt = (RectTransform)_mask.transform;
            mrt.anchoredPosition = c;
            mrt.sizeDelta = holoSize;
            _mask.enabled = stencilOk;
            _maskShape.color = stencilOk ? Color.white : Color.clear;
            if (round) _maskShape.SetRound(stencilOk ? r : 0f);
            else _maskShape.SetPolygon(shape, w, h, top, bot);

            _holo.rectTransform.anchoredPosition = Vector2.zero;
            _holo.rectTransform.sizeDelta = holoSize;
            ((RectTransform)_holoScan.transform).anchoredPosition = Vector2.zero;
            ((RectTransform)_holoScan.transform).sizeDelta = holoSize;

            // Exactly one rim is live: the circle for round, the polygon for the angular shapes.
            if (_ring.gameObject.activeSelf != round) _ring.gameObject.SetActive(round);
            if (_ringPoly.gameObject.activeSelf == round) _ringPoly.gameObject.SetActive(!round);

            if (round)
            {
                ((RectTransform)_ring.transform).anchoredPosition = c;
                _ring.SetRadius(r);
            }
            else
            {
                var prt = (RectTransform)_ringPoly.transform;
                prt.anchoredPosition = c;
                prt.sizeDelta = new Vector2(w, h);
                PortraitShapes.BuildPolygon(shape, w, h, top, bot, _polyPts);
                _ringPoly.SetPoints(_polyPts, null, null, 0, 2);
            }
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
            bool holo = Def.GetBFor(LayoutBare, "holo", false);
            bool scanlines = Def.GetBFor(LayoutBare, "scanlines", holo);

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
            // The hologram tint is now author-controlled: a per-element colour ref (falling back
            // to the global HudHologramTint) and a 0..1 strength. Lerping FROM white means
            // strength 0 = the opaque natural portrait and strength 1 = the full tint (which is
            // semi-transparent at the default alpha), so the old hard on/off is the strength-1 case.
            Color holoTint = GlobalOr(Def.GetSFor(LayoutBare, "holoTint", ""), HudPalette.HologramTint.Value);
            float holoStr = Mathf.Clamp01(Def.GetFFor(LayoutBare, "holoStrength", 1f));
            _holo.color = holo ? Color.Lerp(Color.white, holoTint, holoStr) : Color.white;
            _holoScan.color = (_holo.enabled && scanlines) ? HudPalette.Scanline.Value : Color.clear;

            // Rim chrome: transparent fill (alpha 0) so only the border draws as a ring. The rim
            // itself resolves through the base's border-only EDGE path, so under follow-global it
            // carries the theme's real edge treatment (edge light + border fade + halo) instead of
            // a raw sub-pixel border line no theme ever intended to be seen on its own.
            var fill = FillColor();
            fill.a = 0f;
            if (CurrentShape() == PortraitShape.Round)
            {
                _ring.color = fill;
                ApplyBorderOnlyEdge(_ring);
            }
            else
            {
                // The angular rim is a real glass polygon, so it takes the FULL panel edge
                // treatment (edge light / border fade / halo / energy) exactly as a following
                // Box would — border-only because its fill alpha is 0.
                _ringPoly.color = fill;
                _ringPoly.BorderColor = BorderColor();
                _ringPoly.BorderWidth = BorderWidthFor();
                _ringPoly.BorderSides = 15;
                ApplyGlass(_ringPoly);
            }
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
                float fov = Def.GetFFor(LayoutBare, "camFov", 0f);       // 0 = leave vanilla's FOV
                float dist = Def.GetFFor(LayoutBare, "camDistance", 0f); // 0 = leave vanilla's distance
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

            // These defaults MUST match the ones UpdatePanel reads (holo:false, scanlines:holo).
            // They previously both defaulted to true here, so on any element without the keys the
            // checkboxes read ON while the render was OFF — the inspector lying about live state.
            into.Add(HudProp.Bool("Hologram tint", () => d.GetBFor(EditBare(d), "holo", false), v => d.SetBFor(EditBare(d), "holo", v)));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            // The tint colour + strength (only visible while "Hologram tint" is on). Colour ref
            // falls back to the global HudHologramTint; strength default 1 matches the old full tint.
            into.Add(HudProp.Color("Hologram tint colour", () => d.GetSFor(EditBare(d), "holoTint", ""),
                v => d.SetSFor(EditBare(d), "holoTint", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.HologramTint.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.F("Hologram tint strength", () => d.GetFFor(EditBare(d), "holoStrength", 1f),
                v => d.SetFFor(EditBare(d), "holoStrength", Mathf.Clamp01(v)), 0f, 1f));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            into.Add(HudProp.Bool("Scanlines", () => d.GetBFor(EditBare(d), "scanlines", d.GetBFor(EditBare(d), "holo", false)),
                v => d.SetBFor(EditBare(d), "scanlines", v)));
            into[into.Count - 1].Group = HudPropGroup.Effects;

            int layoutStart = into.Count;
            // Frame silhouette. Round is the shipped default; the four angular shapes reuse the
            // pen-tool glass renderer so they carry the same edge as the round ring.
            into.Add(HudProp.Enum("Shape", () => Mathf.Clamp(d.GetIFor(EditBare(d), "shape", 0), 0, 4),
                v => d.SetIFor(EditBare(d), "shape", Mathf.Clamp(v, 0, 4)),
                new[] { "Round", "Triangle", "Trapezoid", "Square", "Rectangle" }));
            if (Mathf.Clamp(d.GetIFor(EditBare(d), "shape", 0), 0, 4) == (int)PortraitShape.Trapezoid)
            {
                // Width of each parallel edge as a fraction of the element width (1 = full width).
                into.Add(HudProp.F("  Top width", () => d.GetFFor(EditBare(d), "shapeTop", 0.6f),
                    v => d.SetFFor(EditBare(d), "shapeTop", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.F("  Bottom width", () => d.GetFFor(EditBare(d), "shapeBot", 1f),
                    v => d.SetFFor(EditBare(d), "shapeBot", Mathf.Clamp01(v)), 0f, 1f));
            }
            into.Add(HudProp.F("Camera FOV (0 = vanilla)", () => d.GetFFor(EditBare(d), "camFov", 0f), v => d.SetFFor(EditBare(d), "camFov", Mathf.Clamp(v, 0f, 60f)), 0f, 60f));
            into.Add(HudProp.F("Camera zoom-out", () => d.GetFFor(EditBare(d), "camDistance", 0f), v => d.SetFFor(EditBare(d), "camDistance", v), -1f, 3f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;
        }
    }
}
