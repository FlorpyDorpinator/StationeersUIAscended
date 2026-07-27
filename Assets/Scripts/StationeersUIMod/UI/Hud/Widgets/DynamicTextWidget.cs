using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// One text-driven view serving the four single-value readouts that read straight from
    /// the snapshot: the ship <see cref="HudElementType.Clock"/>, the
    /// <see cref="HudElementType.WorldName"/> banner, the <see cref="HudElementType.DayCounter"/>,
    /// and the <see cref="HudElementType.ActiveHandBadge"/>. The concrete behaviour is chosen
    /// off <see cref="HudElementDef.Type"/> at build time; geometry is the element rect
    /// (<see cref="HudElementView.CenterFor"/>/<see cref="HudElementView.SizeFor"/>), so
    /// resizing in the designer scales the layout instead of moving a hard-coded anchor.
    /// </summary>
    internal sealed class DynamicTextWidget : HudElementView
    {
        private PanelGraphic _box;          // ActiveHandBadge chrome only
        private TextMeshProUGUI _primary;
        private TextMeshProUGUI _secondary; // Clock's optional date line only

        // Cached derived strings: rebuilt only when their source value changes so the draw
        // path never allocates for text that didn't move.
        private string _worldSrc;
        private string _worldUpper = "";
        private uint _dayVal = uint.MaxValue;
        private string _dayStr = "";

        // Only the ActiveHandBadge draws a framed box; the plain text readouts (clock, world,
        // day) have none, so the trapezoid sliders appear for the badge alone.
        protected override bool SupportsTrapezoid => Def.Type == HudElementType.ActiveHandBadge;

        // Same reasoning, same condition: only the badge owns a PanelGraphic to style.
        protected override bool SupportsPanelAppearance => Def.Type == HudElementType.ActiveHandBadge;

        protected override void BuildContent(RectTransform root)
        {
            // Box first so the glyph renders on top of it (sibling order = draw order).
            if (Def.Type == HudElementType.ActiveHandBadge)
                _box = MakePanel(root, "Badge");

            _primary = HudText.Make(root, "Text", 17f, AlignFor(Def.Align));

            if (Def.Type == HudElementType.Clock)
                _secondary = HudText.Make(root, "SubText", 17f, AlignFor(Def.Align));
        }

        public override void Layout(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            Root.anchoredPosition = Vector2.zero;
            var align = AlignFor(Def.Align);

            if (_box != null)
            {
                ((RectTransform)_box.transform).anchoredPosition = c;
                _box.SetShape(s.x, s.y,
                    RadiusTL(), RadiusTR(), RadiusBR(), RadiusBL(),
                    InsetTop(scale), InsetBottom(scale));
            }

            // A dated clock stacks two lines inside the rect; everything else is one centred line.
            bool twoLine = Def.Type == HudElementType.Clock && Def.GetB("date", false);
            float split = twoLine ? s.y * 0.24f : 0f;

            _primary.rectTransform.anchoredPosition = c + new Vector2(0f, split);
            _primary.rectTransform.sizeDelta = twoLine ? new Vector2(s.x, s.y * 0.5f) : s;
            _primary.alignment = align;

            if (_secondary != null)
            {
                _secondary.rectTransform.anchoredPosition = c - new Vector2(0f, split);
                _secondary.rectTransform.sizeDelta = new Vector2(s.x, s.y * 0.5f);
                _secondary.alignment = align;
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            float size = HudText.Size(Def.GetFFor(LayoutBare, "size", 17f) * Def.FontScaleFor(LayoutBare)) * scale;
            var textCol = TextColor();

            if (_box != null)
            {
                _box.color = FillColor();
                // This box IS the active-hand marker, so its border always carries the accent.
                _box.BorderColor = HudPalette.ActiveHandAccent.Value;
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }

            HudText.Sync(_primary);
            _primary.fontSize = size;
            _primary.color = textCol;
            HudText.Set(_primary, PrimaryText(s));

            if (_secondary != null)
            {
                HudText.Sync(_secondary);
                _secondary.fontSize = size * 0.8f;
                _secondary.color = textCol;
                HudText.Set(_secondary, SecondaryText(s));
            }
        }

        private string PrimaryText(HudSnapshot s)
        {
            switch (Def.Type)
            {
                case HudElementType.Clock:
                    // TimeText is always numeric; a bare (unpowered) visor shows the vague
                    // day-part word instead, matching the felt-sense clock the snapshot carries.
                    if (s.Tier == HudTier.Bare) return s.DayPartWord ?? "";
                    return Def.GetB("utc", true) ? (s.TimeText + " UTC") : (s.TimeText ?? "");

                case HudElementType.WorldName:
                    if (!string.Equals(s.WorldName, _worldSrc, System.StringComparison.Ordinal))
                    {
                        _worldSrc = s.WorldName;
                        _worldUpper = (s.WorldName ?? "").ToUpperInvariant();
                    }
                    return _worldUpper;

                case HudElementType.DayCounter:
                    if (s.Day != _dayVal)
                    {
                        _dayVal = s.Day;
                        _dayStr = "DAY " + s.Day;
                    }
                    return _dayStr;

                case HudElementType.ActiveHandBadge:
                    return Def.GetB("number", false)
                        ? (s.RightHandActive ? "2" : "1")
                        : (s.RightHandActive ? "R" : "L");

                default:
                    return "";
            }
        }

        private string SecondaryText(HudSnapshot s)
        {
            // Only the dated clock has a second line, and never in the bare tier (no numbers there).
            if (Def.Type != HudElementType.Clock || !Def.GetB("date", false) || s.Tier == HudTier.Bare)
                return "";
            return s.DateText ?? "";
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Text size", () => d.GetFFor(EditBare(d), "size", 17f), v => d.SetFFor(EditBare(d), "size", v), 6f, 64f));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            into.Add(HudProp.Enum("Align", () => AlignIndex(d.Align),
                v => d.Align = AlignNames[Mathf.Clamp(v, 0, AlignNames.Length - 1)], AlignNames));
            into[into.Count - 1].Group = HudPropGroup.Layout;

            switch (d.Type)
            {
                case HudElementType.Clock:
                    into.Add(HudProp.Bool("UTC suffix", () => d.GetB("utc", true), v => d.SetB("utc", v)));
                    into.Add(HudProp.Bool("Show date line", () => d.GetB("date", false), v => d.SetB("date", v)));
                    break;
                case HudElementType.ActiveHandBadge:
                    into.Add(HudProp.Bool("Number (1/2) instead of L/R",
                        () => d.GetB("number", false), v => d.SetB("number", v)));
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
