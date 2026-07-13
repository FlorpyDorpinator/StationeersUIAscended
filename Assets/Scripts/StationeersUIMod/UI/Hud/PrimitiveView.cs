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
        private HudIconGraphic _glyph;
        private Image _sprite;          // PNG-override icons render as a plain Image
        private TextMeshProUGUI _placeholderText;
        private bool _placeholder;

        private static readonly List<Vector2> _pointScratch = new List<Vector2>(16);
        private static readonly List<Vector2> _curveScratch = new List<Vector2>(128); // smoothed spline

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
                    Def.GetF("insetTop", 0f) * scale, Def.GetF("insetBottom", 0f) * scale);
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
                _line.Width = Mathf.Max(0.5f, Def.GetF("width", 2f) * scale);
                _line.FadeEnds = Def.GetF("fadeEnds", 0f);
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
                ApplyGlass(_box);
            }
            if (_text != null)
            {
                HudText.Sync(_text);
                _text.fontSize = HudText.Size(Def.GetF("size", 14f) * Def.FontScale) * scale;
                _text.color = TextColor();
                HudText.Set(_text, Def.Text ?? "");
            }
            if (_line != null) _line.color = TextColor();
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
                case HudElementType.Box:
                    into.Add(HudProp.F("Top inset (trapezoid)", () => d.GetF("insetTop", 0f), v => d.SetF("insetTop", Mathf.Max(0f, v)), 0f, 400f));
                    into.Add(HudProp.F("Bottom inset (trapezoid)", () => d.GetF("insetBottom", 0f), v => d.SetF("insetBottom", Mathf.Max(0f, v)), 0f, 400f));
                    break;
                case HudElementType.Label:
                    into.Add(HudProp.Text("Text", () => d.Text ?? "", v => d.Text = v));
                    into.Add(HudProp.F("Text size", () => d.GetF("size", 14f), v => d.SetF("size", v), 6f, 64f));
                    into.Add(HudProp.Enum("Align", () => AlignIndex(d.Align),
                        v => d.Align = AlignNames[Mathf.Clamp(v, 0, AlignNames.Length - 1)], AlignNames));
                    into.Add(HudProp.Bool("Wrap text", () => d.GetB("wrap", true), v => d.SetB("wrap", v)));
                    break;
                case HudElementType.Polyline:
                    into.Add(HudProp.F("Line width", () => d.GetF("width", 2f), v => d.SetF("width", Mathf.Max(0.5f, v)), 0.5f, 24f));
                    into.Add(HudProp.Bool("Closed loop", () => d.GetB("closed", false), v => d.SetB("closed", v)));
                    into.Add(HudProp.F("Fade ends (0=off)", () => d.GetF("fadeEnds", 0f), v => d.SetF("fadeEnds", Mathf.Clamp(v, 0f, 0.49f)), 0f, 0.49f));
                    into.Add(HudProp.Bool("Smooth (curved)", () => d.GetB("smooth", false), v => d.SetB("smooth", v)));
                    into.Add(HudProp.I("Curve smoothness", () => d.GetI("curveSteps", 12), v => d.SetI("curveSteps", Mathf.Clamp(v, 2, 32)), 2, 32));
                    break;
                case HudElementType.Icon:
                    into.Add(HudProp.Text("Icon (glyph or PNG name)", () => d.Icon ?? "", v => d.Icon = v));
                    into.Add(HudProp.F("Stroke scale", () => d.GetF("stroke", 1f), v => d.SetF("stroke", Mathf.Clamp(v, 0.4f, 3f)), 0.4f, 3f));
                    break;
            }
        }

        private static readonly string[] AlignNames = { "Left", "Center", "Right" };

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
