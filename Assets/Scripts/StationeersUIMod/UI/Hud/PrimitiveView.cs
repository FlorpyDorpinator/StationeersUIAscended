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
                    Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL),
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
                float minW = HudConfig.FxHairlineMin != null ? HudConfig.FxHairlineMin.Value : 0.15f;
                _line.Width = Mathf.Max(minW, Def.GetF("width", 2f) * scale);
                _line.FadeEnds = Def.GetF("fadeEnds", 0f);
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
                int sides = (Def.GetB("bTop", true) ? 1 : 0) | (Def.GetB("bRight", true) ? 2 : 0)
                          | (Def.GetB("bBottom", true) ? 4 : 0) | (Def.GetB("bLeft", true) ? 8 : 0);
                _box.BorderSides = sides;
                ApplyGlass(_box);
            }
            if (_shape != null)
            {
                _shape.color = FillColor();
                _shape.BorderColor = BorderColor();
                _shape.BorderWidth = BorderWidthFor();
                int sides = (Def.GetB("bTop", true) ? 1 : 0) | (Def.GetB("bRight", true) ? 2 : 0)
                          | (Def.GetB("bBottom", true) ? 4 : 0) | (Def.GetB("bLeft", true) ? 8 : 0);
                _shape.BorderSides = sides;
                ApplyGlass(_shape);   // sheen/spec/border-fade/soft-edge/ripple/FX, same as a Box
            }
            if (_text != null)
            {
                HudText.Sync(_text);
                _text.fontSize = HudText.Size(Def.GetF("size", 14f) * Def.FontScale) * scale;
                _text.color = TextColor();
                HudText.Set(_text, Def.Text ?? "");
            }
            if (_line != null)
            {
                _line.color = TextColor();
                // Directional edge-light (Tier A): global strength × the per-element master;
                // the light direction is the shared panel key light, so lines and borders agree.
                bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
                bool edgeOn = tierA && HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value;
                _line.EdgeLight = edgeOn && HudConfig.FxEdgeLight != null ? HudConfig.FxEdgeLight.Value : 0f;
                // Track the configurable key-light DIRECTION so lines catch light from the same
                // angle as the borders (the default reproduces the old upper-left direction).
                _line.EdgeLightDir = new Vector2(PanelGraphic.LightX, PanelGraphic.LightY);
                _line.EdgeRipple = edgeOn && HudConfig.FxEdgeRipple != null ? HudConfig.FxEdgeRipple.Value : 0f;
                _line.EdgeRippleFreq = HudConfig.FxEdgeRippleFreq != null ? HudConfig.FxEdgeRippleFreq.Value : 2f;
                _line.RippleSmooth = Def.GetF("rippleSmooth", 0f);
                _line.FxStrength = FxStrengthFor();
            }
            if (_sprite != null) _sprite.color = TextColor();
            if (_glyph != null)
            {
                _glyph.color = TextColor();
                _glyph.StrokeScale = Def.GetF("stroke", 1f);
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
                    into.Add(HudProp.Bool("Border: top", () => d.GetB("bTop", true), v => d.SetB("bTop", v)));
                    into.Add(HudProp.Bool("Border: right", () => d.GetB("bRight", true), v => d.SetB("bRight", v)));
                    into.Add(HudProp.Bool("Border: bottom", () => d.GetB("bBottom", true), v => d.SetB("bBottom", v)));
                    into.Add(HudProp.Bool("Border: left", () => d.GetB("bLeft", true), v => d.SetB("bLeft", v)));
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
                    into.Add(HudProp.F("Text size", () => d.GetF("size", 14f), v => d.SetF("size", v), 6f, 64f));
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
                    into.Add(HudProp.F("Line width", () => d.GetF("width", 2f), v => d.SetF("width", Mathf.Max(0.05f, v)), 0.05f, 24f));
                    for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;

                    int effectsStart = into.Count;
                    into.Add(HudProp.F("Fade ends (0=off)", () => d.GetF("fadeEnds", 0f), v => d.SetF("fadeEnds", Mathf.Clamp(v, 0f, 0.49f)), 0f, 0.49f));
                    for (int i = effectsStart; i < into.Count; i++) into[i].Group = HudPropGroup.Effects;
                    break;
                }
                case HudElementType.Icon:
                    into.Add(HudProp.Text("Icon (glyph or PNG name)", () => d.Icon ?? "", v => d.Icon = v));
                    into.Add(HudProp.F("Stroke scale", () => d.GetF("stroke", 1f), v => d.SetF("stroke", Mathf.Clamp(v, 0.4f, 3f)), 0.4f, 3f));
                    into[into.Count - 1].Group = HudPropGroup.Appearance;
                    break;
                case HudElementType.Shape:
                {
                    // The silhouette itself is drawn/edited with the pen tool (F9 gizmo layer); these
                    // tune it. Fill/border/glass come from the base props (it is a glass panel).
                    // Curve style: Straight corners / Smooth (Catmull through the points) / Bézier
                    // (drag per-point handles in Edit-points mode). Choosing Bézier seeds smooth
                    // handles from the point tangents so it curves immediately.
                    int layoutStart = into.Count;
                    into.Add(HudProp.Enum("Curve style",
                        () => d.GetI("curveMode", d.GetB("smooth", false) ? 1 : 0),
                        v => { d.SetI("curveMode", Mathf.Clamp(v, 0, 2)); if (v == 2) EnsureBezierHandles(d); },
                        CurveStyleNames));
                    into.Add(HudProp.I("Curve smoothness", () => d.GetI("curveSteps", 12), v => d.SetI("curveSteps", Mathf.Clamp(v, 2, 32)), 2, 32));
                    for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

                    int appearanceStart = into.Count;
                    into.Add(HudProp.Bool("Border: top", () => d.GetB("bTop", true), v => d.SetB("bTop", v)));
                    into.Add(HudProp.Bool("Border: right", () => d.GetB("bRight", true), v => d.SetB("bRight", v)));
                    into.Add(HudProp.Bool("Border: bottom", () => d.GetB("bBottom", true), v => d.SetB("bBottom", v)));
                    into.Add(HudProp.Bool("Border: left", () => d.GetB("bLeft", true), v => d.SetB("bLeft", v)));
                    for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
                    break;
                }
            }
        }

        private static readonly string[] AlignNames = { "Left", "Center", "Right" };
        private static readonly string[] CurveStyleNames = { "Straight", "Smooth", "Bezier" };

        /// <summary>Seed a shape's Bézier handles (if not already sized to the points) with smooth
        /// tangents derived from each point's neighbours, so switching to Bézier immediately reads
        /// as a smooth curve the user can then reshape by dragging the handles.</summary>
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
                Vector2 prev = pts[(i - 1 + m) % m];
                Vector2 next = pts[(i + 1) % m];
                Vector2 tan = (next - prev) * 0.16f; // fraction of the local tangent = gentle curve
                hout.Add(tan);
                hin.Add(-tan);
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
