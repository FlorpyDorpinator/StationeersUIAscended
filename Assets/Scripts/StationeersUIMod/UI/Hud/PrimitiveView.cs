using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Renders the four primitive element types straight from the document — Box (per-
    /// corner rounded panel), Label (user text), Polyline (drawn line work) and Icon
    /// (built-in glyph or PNG override). Also the fallback for widget types whose view
    /// hasn't been registered: those draw as a dim placeholder naming the type, so a
    /// document from a newer profile degrades visibly instead of silently.
    /// </summary>
    internal sealed class PrimitiveView : HudElementView
    {
        private PanelGraphic _box;
        private TextMeshProUGUI _text;
        private PolylineGraphic _line;
        private PolygonPanelGraphic _shape;   // freeform pen shape (HudElementType.Shape)
        private HudIconGraphic _glyph;
        private Image _sprite;          // PNG-override icons render as a plain Image
        private TextMeshProUGUI _placeholderText;
        private bool _placeholder;

        private static readonly List<Vector2> _pointScratch = new List<Vector2>(16);
        private static readonly List<Vector2> _curveScratch = new List<Vector2>(128); // smoothed spline
        private static readonly List<Vector2> _hinScratch = new List<Vector2>(16);    // Shape Bézier handles
        private static readonly List<Vector2> _houtScratch = new List<Vector2>(16);

        // Only the Box primitive draws a framed panel the trapezoid insets can shape (Label/
        // Polyline/Icon have no background box); the base supplies the two sliders for it.
        protected override bool SupportsTrapezoid => Def.Type == HudElementType.Box;

        protected override void BuildContent(RectTransform root)
        {
            switch (Def.Type)
            {
                case HudElementType.Box:
                    _box = MakePanel(root, "Box");
                    break;

                case HudElementType.Label:
                    // Wrap to the element's width instead of spilling past it — the rect is sized
                    // to the box in Layout, so a long label stacks its words within the box.
                    _text = HudText.Make(root, "Text", 14f, AlignFor(Def.Align), wrap: true);
                    break;

                case HudElementType.Polyline:
                {
                    var go = new GameObject("Line", typeof(RectTransform));
                    go.transform.SetParent(root, false);
                    _line = go.AddComponent<PolylineGraphic>();
                    _line.raycastTarget = false;
                    go.AddComponent<VisorWarp>();
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    break;
                }

                case HudElementType.Shape:
                {
                    // A freeform FILLED glass shape from the pen tool. Same rig as the polyline
                    // (point list + warp), but a PolygonPanelGraphic that fills the closed contour.
                    var go = new GameObject("Shape", typeof(RectTransform));
                    go.transform.SetParent(root, false);
                    _shape = go.AddComponent<PolygonPanelGraphic>();
                    _shape.raycastTarget = false;
                    go.AddComponent<VisorWarp>();
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    break;
                }

                case HudElementType.Icon:
                {
                    // A PNG override wins over the built-in glyph (drop a file in
                    // config/StationeersUIMod/HudIcons, no rebuild). Both paths warp.
                    var sprite = Core.HudIconStore.TryGet(Def.Icon);
                    if (sprite != null)
                    {
                        _sprite = MakeIcon(root, "IconPng");
                        _sprite.sprite = sprite;
                    }
                    else
                    {
                        var go = new GameObject("IconGlyph", typeof(RectTransform));
                        go.transform.SetParent(root, false);
                        _glyph = go.AddComponent<HudIconGraphic>();
                        _glyph.raycastTarget = false;
                        go.AddComponent<VisorWarp>();
                        var rt = (RectTransform)go.transform;
                        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                        HudIconKind kind;
                        if (System.Enum.TryParse(Def.Icon, true, out kind))
                            _glyph.Kind = kind;
                    }
                    break;
                }

                default:
                    // Widget type without a registered view (older build reading a newer
                    // profile, or a phase not yet shipped).
                    _placeholder = true;
                    _box = MakePanel(root, "Placeholder");
                    _placeholderText = HudText.Make(root, "PlaceholderText", 11f, TextAlignmentOptions.Center);
                    break;
            }
        }

        public override void Layout(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            Root.anchoredPosition = Vector2.zero;

            if (_box != null)
            {
                ((RectTransform)_box.transform).anchoredPosition = c;
                // Insets pull the top/bottom corners inward — a positive bottom inset
                // makes the visor-bar trapezoid (top edge wider, angled sides).
                _box.SetShape(s.x, s.y,
                    Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)), Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)),
                    InsetTop(scale), InsetBottom(scale));
            }
            if (_text != null)
            {
                _text.rectTransform.anchoredPosition = c;
                _text.rectTransform.sizeDelta = s;
                _text.alignment = AlignFor(Def.Align);
                bool wrapText = Def.GetB("wrap", true); // per-element "Wrap text"
                if (_text.enableWordWrapping != wrapText) _text.enableWordWrapping = wrapText;
            }
            if (_line != null)
            {
                ((RectTransform)_line.transform).anchoredPosition = c;
                var pts = Def.GetPoints("pts");
                _pointScratch.Clear();
                for (int i = 0; i < pts.Length; i++) _pointScratch.Add(pts[i] * scale);
                bool closed = Def.GetB("closed", false);
                // "Smooth" turns the drawn points into control points of a Catmull-Rom spline: the
                // curve passes THROUGH each point, and the existing straight-segment renderer draws
                // the densely-subdivided result (the visor warp bends it like any other line). Needs
                // ≥3 points to define a curve; below that it stays a plain segment.
                if (Def.GetB("smooth", false) && _pointScratch.Count >= 3)
                {
                    int steps = Mathf.Clamp(Def.GetI("curveSteps", 12), 2, 32);
                    BuildSmooth(_pointScratch, closed, steps, _curveScratch);
                    _line.SetPoints(_curveScratch, closed);
                }
                else
                {
                    _line.SetPoints(_pointScratch, closed);
                }
                // Hairlines: the floor is the global knob (default 0.15px) — below 1px the
                // renderer holds 1px and fades alpha by coverage instead of vanishing.
                // Hairline floor: per-element override with the -1 = global convention (the
                // F9 "thinnest line" slider). State-independent like width itself — it is
                // stroke geometry, not theme.
                float minW = Def.GetFFor(LayoutBare, "hairlineMin", -1f);
                if (minW < 0f)
                    minW = HudConfig.FxHairlineMin != null ? HudConfig.FxHairlineMin.Value : 0.15f;
                _line.Width = Mathf.Max(minW, Def.GetFFor(LayoutBare, "width", 2f) * scale);
                _line.FadeEnds = Def.GetFFor(LayoutBare, "fadeEnds", 0f);
            }
            if (_shape != null)
            {
                ((RectTransform)_shape.transform).anchoredPosition = c;
                var pts = Def.GetPoints("pts");
                _pointScratch.Clear();
                for (int i = 0; i < pts.Length; i++) _pointScratch.Add(pts[i] * scale);
                // curveMode: explicit (0 straight / 1 smooth / 2 Bézier) or derived from the legacy
                // "smooth" bool. Bézier handle data (hin/hout) is threaded in a later phase.
                int curveMode = Def.GetI("curveMode", Def.GetB("smooth", false) ? 1 : 0);
                int steps = Mathf.Clamp(Def.GetI("curveSteps", 12), 2, 32);
                _hinScratch.Clear(); _houtScratch.Clear();
                if (curveMode == 2)
                {
                    var hin = Def.GetPoints("hin");
                    var hout = Def.GetPoints("hout");
                    for (int i = 0; i < hin.Length; i++) _hinScratch.Add(hin[i] * scale);
                    for (int i = 0; i < hout.Length; i++) _houtScratch.Add(hout[i] * scale);
                }
                _shape.SetPoints(_pointScratch, _hinScratch, _houtScratch, curveMode, steps);
            }
            if (_sprite != null)
            {
                _sprite.rectTransform.anchoredPosition = c;
                _sprite.rectTransform.sizeDelta = s;
            }
            if (_glyph != null)
            {
                ((RectTransform)_glyph.transform).anchoredPosition = c;
                ((RectTransform)_glyph.transform).sizeDelta = s;
            }
            if (_placeholderText != null)
            {
                _placeholderText.rectTransform.anchoredPosition = c;
                _placeholderText.rectTransform.sizeDelta = new Vector2(s.x + 40f, 14f);
            }
        }

        public override void UpdatePanel(HudSnapshot snap, float scale)
        {
            if (_box != null)
            {
                var fill = FillColor();
                var border = BorderColor();
                if (_placeholder) { fill.a *= 0.35f; border.a *= 0.5f; }
                _box.color = fill;
                _box.BorderColor = border;
                _box.BorderWidth = BorderWidthFor();
                // Per-side border visibility ("make one of the 4 sides transparent"): bits
                // 1=Top 2=Right 4=Bottom 8=Left; a disabled side melts away around its corners.
                int sides = (Def.GetBFor(LayoutBare, "bTop", true) ? 1 : 0) | (Def.GetBFor(LayoutBare, "bRight", true) ? 2 : 0)
                          | (Def.GetBFor(LayoutBare, "bBottom", true) ? 4 : 0) | (Def.GetBFor(LayoutBare, "bLeft", true) ? 8 : 0);
                _box.BorderSides = sides;
                ApplyGlass(_box);
            }
            if (_shape != null)
            {
                _shape.color = FillColor();
                _shape.BorderColor = BorderColor();
                _shape.BorderWidth = BorderWidthFor();
                int sides = (Def.GetBFor(LayoutBare, "bTop", true) ? 1 : 0) | (Def.GetBFor(LayoutBare, "bRight", true) ? 2 : 0)
                          | (Def.GetBFor(LayoutBare, "bBottom", true) ? 4 : 0) | (Def.GetBFor(LayoutBare, "bLeft", true) ? 8 : 0);
                _shape.BorderSides = sides;
                ApplyGlass(_shape);   // sheen/spec/border-fade/soft-edge/ripple/FX, same as a Box
            }
            if (_text != null)
            {
                HudText.Sync(_text);
                _text.fontSize = HudText.Size(Def.GetFFor(LayoutBare, "size", 14f) * Def.FontScaleFor(LayoutBare)) * scale;
                _text.color = TextColor();
                HudText.Set(_text, Def.Text ?? "");
            }
            if (_line != null)
            {
                // PolylineGraphic has NO BorderColor — its halo hue IS the stroke colour, so this is
                // the only way a drawn line joins the alert. Note this tints the line STROKE, which
                // is right for lines; TextColor() itself is deliberately NOT hooked, or every
                // readout's numbers would go amber and become unreadable mid-emergency.
                _line.color = HudAlertPulse.Tint(TextColor(), AlertSeed);
                // The line resolves through the SAME two-state contract as the panels (the
                // 2026-07-16 standardisation — it was the last surface on raw -1 sentinels,
                // blind to styleSource, which is why a "separated" polyline kept rendering the
                // global halo it could not turn off). Global = pure F9 values; Custom = the
                // seeded snapshot, gated by the same custom*On checkboxes as every panel.
                bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
                bool rippleOn = tierA && StyleFeatureOn("customRippleOn", HudConfig.FxEdgeLightOn);
                _line.EdgeLight = rippleOn ? OwnOrGlobal("edgeLight", HudConfig.FxEdgeLight) : 0f;
                // Track the configurable key-light DIRECTION so lines catch light from the same
                // angle as the borders (the default reproduces the old upper-left direction).
                _line.EdgeLightDir = new Vector2(PanelGraphic.LightX, PanelGraphic.LightY);
                _line.EdgeRipple = rippleOn ? OwnOrGlobal("ripple", HudConfig.FxEdgeRipple) : 0f;
                _line.EdgeRippleFreq = RippleFreqFor(OwnOrGlobal("rippleFreq", HudConfig.FxEdgeRippleFreq));
                _line.RippleSmooth = UsesGlobalStyle ? 0f : Def.GetFFor(LayoutBare, "rippleSmooth", 0f);
                bool glowOn = tierA && StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn);
                // Same Tier-A-gated constant floor as the panels (see ApplyMeshFx).
                _line.Glow = tierA ? HudAlertPulse.Glow(glowOn ? OwnOrGlobal("glow", HudConfig.FxGlow) : 0f) : 0f;
                _line.GlowWidth = OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth);
                _line.GlowDiffuse = OwnOrGlobal("glowDiffuse", HudConfig.FxGlowDiffuse);
                _line.GlowExtraDiffuse = Mathf.Clamp01(
                    NewSdfOwnOrGlobal("glowExtraDiffuse", HudConfig.FxGlowExtraDiffuse, 0f));
                _line.FxStrength = FxStrengthFor();
                // Moving flow (Tier B): the travelling edge-energy wave. Gated on the PROVEN
                // flow ABI — an old resident bundle after F6 ignores uv1 — AND the ripple gate
                // (the wave IS the ripple, animated). Binding the edgefx material lets HudEdgeFX
                // animate the baked uv1 payload; with no bound bundle material the payload is
                // inert, so this fails soft to the static ripple bake. Mirrors the Shape path
                // (HudElementView.ApplyFx meshFlow) since a line is not an IGlassSurface.
                bool flowWanted = rippleOn && Core.HudShaderStore.FlowAbiAvailable;
                _line.FlowSpeed = flowWanted ? RippleFlowSpeedFor(OwnOrGlobal("edgeFlow", HudConfig.FxEdgeFlowSpeed)) : 0f;
                bool meshFlow = _line.FlowSpeed > 0.004f && _line.EdgeRipple > 0.004f;
                if (meshFlow)
                {
                    if (!HudFxMaterials.Assign(_line, "edgefx")) HudFxMaterials.Unassign(_line);
                }
                else HudFxMaterials.Unassign(_line);
            }
            if (_sprite != null) _sprite.color = TextColor();
            if (_glyph != null)
            {
                _glyph.color = TextColor();
                _glyph.StrokeScale = Def.GetFFor(LayoutBare, "stroke", 1f);
            }
            if (_placeholderText != null)
            {
                HudText.Sync(_placeholderText);
                _placeholderText.fontSize = HudText.Size(10f) * scale;
                var dim = HudPalette.TextDim.Value;
                _placeholderText.color = dim;
                HudText.Set(_placeholderText, Def.Type.ToString().ToUpperInvariant());
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            switch (d.Type)
            {
                // Trapezoid insets come from the base (SupportsTrapezoid). Box's own extras:
                // per-side border visibility ("make one of the 4 sides transparent").
                case HudElementType.Box:
                {
                    int appearanceStart = into.Count;
                    into.Add(HudProp.Bool("Border: top", () => d.GetBFor(EditBare(d), "bTop", true), v => d.SetBFor(EditBare(d), "bTop", v)));
                    into.Add(HudProp.Bool("Border: right", () => d.GetBFor(EditBare(d), "bRight", true), v => d.SetBFor(EditBare(d), "bRight", v)));
                    into.Add(HudProp.Bool("Border: bottom", () => d.GetBFor(EditBare(d), "bBottom", true), v => d.SetBFor(EditBare(d), "bBottom", v)));
                    into.Add(HudProp.Bool("Border: left", () => d.GetBFor(EditBare(d), "bLeft", true), v => d.SetBFor(EditBare(d), "bLeft", v)));
                    for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
                    break;
                }
                case HudElementType.Label:
                {
                    into.Add(HudProp.Text("Text", () => d.Text ?? "", v => d.Text = v));

                    int layoutStart = into.Count;
                    into.Add(HudProp.Enum("Align", () => AlignIndex(d.Align),
                        v => d.Align = AlignNames[Mathf.Clamp(v, 0, AlignNames.Length - 1)], AlignNames));
                    into.Add(HudProp.Bool("Wrap text", () => d.GetB("wrap", true), v => d.SetB("wrap", v)));
                    for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

                    int appearanceStart = into.Count;
                    into.Add(HudProp.F("Text size", () => d.GetFFor(EditBare(d), "size", 14f), v => d.SetFFor(EditBare(d), "size", v), 6f, 64f));
                    for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
                    break;
                }
                case HudElementType.Polyline:
                {
                    int layoutStart = into.Count;
                    into.Add(HudProp.Bool("Closed loop", () => d.GetB("closed", false), v => d.SetB("closed", v)));
                    into.Add(HudProp.Bool("Smooth (curved)", () => d.GetB("smooth", false), v => d.SetB("smooth", v)));
                    into.Add(HudProp.I("Curve smoothness", () => d.GetI("curveSteps", 12), v => d.SetI("curveSteps", Mathf.Clamp(v, 2, 32)), 2, 32));
                    for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

                    int appearanceStart = into.Count;
                    into.Add(HudProp.F("Line width", () => d.GetFFor(EditBare(d), "width", 2f), v => d.SetFFor(EditBare(d), "width", Mathf.Max(0.05f, v)), 0.05f, 24f));
                    for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;

                    // The light/ripple/halo family now comes from the base inspector's
                    // F9-mirrored line block (HudElementView.AddUnifiedEffectProps), governed
                    // by the same follow-global checkbox as every panel. Only the line's own
                    // geometry-participation knobs live here.
                    int effectsStart = into.Count;
                    into.Add(HudProp.F("Fade ends (0=off)", () => d.GetFFor(EditBare(d), "fadeEnds", 0f), v => d.SetFFor(EditBare(d), "fadeEnds", Mathf.Clamp(v, 0f, 0.49f)), 0f, 0.49f));
                    into.Add(HudProp.F("Hairline floor px (-1 = global)", () => d.GetFFor(EditBare(d), "hairlineMin", -1f),
                        v => d.SetFFor(EditBare(d), "hairlineMin", v < 0f ? -1f : Mathf.Clamp(v, 0.05f, 1f)), -1f, 1f));
                    for (int i = effectsStart; i < into.Count; i++) into[i].Group = HudPropGroup.Effects;
                    break;
                }
                case HudElementType.Icon:
                    into.Add(HudProp.Text("Icon (glyph or PNG name)", () => d.Icon ?? "", v => d.Icon = v));
                    into.Add(HudProp.F("Stroke scale", () => d.GetFFor(EditBare(d), "stroke", 1f), v => d.SetFFor(EditBare(d), "stroke", Mathf.Clamp(v, 0.4f, 3f)), 0.4f, 3f));
                    into[into.Count - 1].Group = HudPropGroup.Appearance;
                    break;
                case HudElementType.Shape:
                {
                    // The silhouette itself is drawn/edited with the pen tool (F9 gizmo layer); these
                    // tune it. Fill/border/glass come from the base props (it is a glass panel).
                    // Curve style: Straight corners / Smooth (Catmull through the points) / Bézier.
                    // Choosing Bézier changes NOTHING visually (handles start zero = straight); in
                    // Edit-points mode you PULL a segment to curve it, Illustrator-style, so curves
                    // are chosen per segment, never imposed on the whole shape.
                    int layoutStart = into.Count;
                    into.Add(HudProp.Enum("Curve style",
                        () => d.GetI("curveMode", d.GetB("smooth", false) ? 1 : 0),
                        v => { d.SetI("curveMode", Mathf.Clamp(v, 0, 2)); if (v == 2) EnsureBezierHandles(d); },
                        CurveStyleNames));
                    into.Add(HudProp.I("Curve smoothness", () => d.GetI("curveSteps", 12), v => d.SetI("curveSteps", Mathf.Clamp(v, 2, 32)), 2, 32));
                    for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

                    int appearanceStart = into.Count;
                    into.Add(HudProp.Bool("Border: top", () => d.GetBFor(EditBare(d), "bTop", true), v => d.SetBFor(EditBare(d), "bTop", v)));
                    into.Add(HudProp.Bool("Border: right", () => d.GetBFor(EditBare(d), "bRight", true), v => d.SetBFor(EditBare(d), "bRight", v)));
                    into.Add(HudProp.Bool("Border: bottom", () => d.GetBFor(EditBare(d), "bBottom", true), v => d.SetBFor(EditBare(d), "bBottom", v)));
                    into.Add(HudProp.Bool("Border: left", () => d.GetBFor(EditBare(d), "bLeft", true), v => d.SetBFor(EditBare(d), "bLeft", v)));
                    for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
                    break;
                }
            }
        }

        private static readonly string[] AlignNames = { "Left", "Center", "Right" };
        private static readonly string[] CurveStyleNames = { "Straight", "Smooth", "Bezier" };

        /// <summary>Size a shape's Bézier handle arrays to its point count with ZERO handles
        /// (a zero-handle cubic degenerates to a straight line). Switching to Bézier therefore
        /// changes nothing visually — every segment stays straight until the user PULLS it into
        /// a curve in Edit-points mode (the Illustrator model: straight lines first, curvature
        /// only where you ask for it). A shape whose handles are already point-aligned is left
        /// alone so existing curves survive the mode being re-selected.</summary>
        internal static void EnsureBezierHandles(HudElementDef d)
        {
            var pts = d.GetPoints("pts");
            int m = pts.Length;
            if (m < 2) return;
            if (d.GetPoints("hout").Length == m && d.GetPoints("hin").Length == m) return; // already set
            var hin = new List<Vector2>(m);
            var hout = new List<Vector2>(m);
            for (int i = 0; i < m; i++)
            {
                hin.Add(Vector2.zero);
                hout.Add(Vector2.zero);
            }
            d.SetPoints("hin", hin);
            d.SetPoints("hout", hout);
        }

        private static int AlignIndex(string align)
        {
            if (string.Equals(align, "Left", System.StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(align, "Right", System.StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static TextAlignmentOptions AlignFor(string align)
        {
            switch (AlignIndex(align))
            {
                case 0: return TextAlignmentOptions.MidlineLeft;
                case 2: return TextAlignmentOptions.MidlineRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        // ---- curve smoothing ----

        /// <summary>Expand control points into a Catmull-Rom spline that passes through every one
        /// of them: <paramref name="steps"/> samples per span, written to <paramref name="outPts"/>.
        /// Open curves clamp the phantom end tangents (natural ends); closed curves wrap. The final
        /// control point is appended for open curves so the last span reaches its endpoint.</summary>
        private static void BuildSmooth(List<Vector2> cp, bool closed, int steps, List<Vector2> outPts)
        {
            outPts.Clear();
            int n = cp.Count;
            if (n < 3) { for (int i = 0; i < n; i++) outPts.Add(cp[i]); return; }

            int spans = closed ? n : n - 1;
            for (int i = 0; i < spans; i++)
            {
                Vector2 p0 = cp[WrapIndex(i - 1, n, closed)];
                Vector2 p1 = cp[WrapIndex(i, n, closed)];
                Vector2 p2 = cp[WrapIndex(i + 1, n, closed)];
                Vector2 p3 = cp[WrapIndex(i + 2, n, closed)];
                for (int s = 0; s < steps; s++)
                    outPts.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }
            if (!closed) outPts.Add(cp[n - 1]); // land exactly on the last drawn point
        }

        private static int WrapIndex(int i, int n, bool closed)
            => closed ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);

        // Uniform Catmull-Rom (tension 0.5): interpolates between p1 and p2 using p0/p3 as tangents.
        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
