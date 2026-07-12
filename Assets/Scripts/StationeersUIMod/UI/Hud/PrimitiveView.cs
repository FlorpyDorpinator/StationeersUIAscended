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

        protected override void BuildContent(RectTransform root)
        {
            switch (Def.Type)
            {
                case HudElementType.Box:
                    _box = MakePanel(root, "Box");
                    break;

                case HudElementType.Label:
                    _text = HudText.Make(root, "Text", 14f, AlignFor(Def.Align));
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
                _box.SetShape(s.x, s.y,
                    Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL));
            }
            if (_text != null)
            {
                _text.rectTransform.anchoredPosition = c;
                _text.rectTransform.sizeDelta = s;
                _text.alignment = AlignFor(Def.Align);
            }
            if (_line != null)
            {
                ((RectTransform)_line.transform).anchoredPosition = c;
                var pts = Def.GetPoints("pts");
                _pointScratch.Clear();
                for (int i = 0; i < pts.Length; i++) _pointScratch.Add(pts[i] * scale);
                _line.SetPoints(_pointScratch, Def.GetB("closed", false));
                _line.Width = Mathf.Max(0.5f, Def.GetF("width", 2f) * scale);
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
                case HudElementType.Label:
                    into.Add(HudProp.Text("Text", () => d.Text ?? "", v => d.Text = v));
                    into.Add(HudProp.F("Text size", () => d.GetF("size", 14f), v => d.SetF("size", v), 6f, 64f));
                    into.Add(HudProp.Enum("Align", () => AlignIndex(d.Align),
                        v => d.Align = AlignNames[Mathf.Clamp(v, 0, AlignNames.Length - 1)], AlignNames));
                    break;
                case HudElementType.Polyline:
                    into.Add(HudProp.F("Line width", () => d.GetF("width", 2f), v => d.SetF("width", Mathf.Max(0.5f, v)), 0.5f, 24f));
                    into.Add(HudProp.Bool("Closed loop", () => d.GetB("closed", false), v => d.SetB("closed", v)));
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
    }
}
