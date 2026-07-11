using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The band of the curved top bar: an annular arc with a bright accent line on its
    /// lower edge and a soft outer edge, ends fading out. The curve is real geometry
    /// (radius from the configured dip), independent of the global visor warp.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArcBandGraphic : MaskableGraphic
    {
        private const int Columns = 48;

        private float _halfWidth = 900f, _thickness = 64f, _dip = 26f, _edgeWidth = 1.6f;
        private Color _edgeInner = Color.clear, _edgeOuter = Color.clear;

        // All dirty-on-change: the bar is rebuilt only when something really moved.
        public float HalfWidth { get => _halfWidth; set => SetF(ref _halfWidth, value); }
        public float Thickness { get => _thickness; set => SetF(ref _thickness, value); }
        /// <summary>How far the ends drop below the middle (px). ~0 = straight bar.</summary>
        public float Dip { get => _dip; set => SetF(ref _dip, value); }
        public float EdgeWidth { get => _edgeWidth; set => SetF(ref _edgeWidth, value); }
        public Color EdgeInner { get => _edgeInner; set => SetC(ref _edgeInner, value); }
        public Color EdgeOuter { get => _edgeOuter; set => SetC(ref _edgeOuter, value); }

        private void SetF(ref float field, float value)
        {
            if (Mathf.Approximately(field, value)) return;
            field = value;
            SetVerticesDirty();
        }

        private void SetC(ref Color field, Color value)
        {
            if (field == value) return;
            field = value;
            SetVerticesDirty();
        }

        private static readonly float[] _stopR = new float[8];
        private static readonly Color[] _stopC = new Color[8];

        /// <summary>Vertical drop of the band midline at horizontal offset x (px, ≥0).</summary>
        public float DropAt(float x)
        {
            float r = Radius;
            float cl = Mathf.Min(Mathf.Abs(x), HalfWidth);
            float inside = r * r - cl * cl;
            return inside <= 0f ? Dip : r - Mathf.Sqrt(inside);
        }

        private float Radius
        {
            get
            {
                float d = Mathf.Max(Dip, 0.5f);
                return (HalfWidth * HalfWidth + d * d) / (2f * d);
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (HalfWidth < 10f || Thickness < 2f) return;

            float r = Radius;
            float thetaMax = Mathf.Asin(Mathf.Clamp01(HalfWidth / r));
            float f = HudConfig.EdgeFeather != null ? HudConfig.EdgeFeather.Value : 1.25f;
            float ew = Mathf.Max(0.4f, EdgeWidth);
            float rIn = r - Thickness * 0.5f;
            float rOut = r + Thickness * 0.5f;

            // Radial stops bottom -> top (colour ramps bake the AA).
            Color fill = color;
            var stopR = _stopR;
            var stopC = _stopC;
            int stops = 0;
            stopR[stops] = rIn - ew - f; stopC[stops++] = Fade(_edgeInner);
            stopR[stops] = rIn - ew; stopC[stops++] = _edgeInner;
            stopR[stops] = rIn; stopC[stops++] = _edgeInner;
            stopR[stops] = rIn + f; stopC[stops++] = fill;
            stopR[stops] = rOut - f; stopC[stops++] = fill;
            stopR[stops] = rOut; stopC[stops++] = _edgeOuter.a > 0.004f ? _edgeOuter : fill;
            stopR[stops] = rOut + ew; stopC[stops++] = _edgeOuter.a > 0.004f ? _edgeOuter : Fade(fill);
            stopR[stops] = rOut + ew + f; stopC[stops++] = Fade(_edgeOuter.a > 0.004f ? _edgeOuter : fill);

            // Columns across the sweep. Ends fade out over the last ~8% of the arc.
            for (int c = 0; c <= Columns; c++)
            {
                float t = c / (float)Columns * 2f - 1f;         // -1..1
                float theta = t * thetaMax;
                var dir = new Vector2(Mathf.Sin(theta), Mathf.Cos(theta));
                float endFade = Mathf.Clamp01((1f - Mathf.Abs(t)) / 0.08f);
                for (int s2 = 0; s2 < stops; s2++)
                {
                    var col = stopC[s2];
                    col.a *= endFade;
                    // Local origin sits at the band midline's top point.
                    var p = dir * stopR[s2] - new Vector2(0f, r);
                    vh.AddVert(p, col, Vector2.one);
                }
            }
            for (int c = 0; c < Columns; c++)
            {
                int a = c * stops;
                int b = (c + 1) * stops;
                for (int s2 = 0; s2 < stops - 1; s2++)
                {
                    vh.AddTriangle(a + s2, a + s2 + 1, b + s2 + 1);
                    vh.AddTriangle(a + s2, b + s2 + 1, b + s2);
                }
            }
        }

        private static Color Fade(Color c) { c.a = 0f; return c; }
    }

    /// <summary>
    /// The curved top status bar from the concept art: UTC clock on the left, stat cells
    /// (PRESSURE / O₂ / TEMP / POWER / WATER) across the middle, SUIT STATUS on the
    /// right. Content follows the arc's sag so it sits ON the band.
    /// </summary>
    internal sealed class TopStatusBar : HudPanel
    {
        public override string Id => "TopBar";
        public override bool SuitTier => true;
        public override ConfigEntry<bool> Toggle => HudConfig.ShowTopBar;
        public override bool VisibleAt(HudTier tier) => tier != HudTier.Bare;

        private ArcBandGraphic _band;
        private TextMeshProUGUI _utcLabel, _utcValue;
        private TextMeshProUGUI _statusLabel, _statusValue;

        private sealed class Cell
        {
            public TextMeshProUGUI Label, Value;
            public RectTransform LabelRt, ValueRt;
        }

        private static readonly string[] CellNames = { "PRESSURE", "O2", "TEMP", "POWER", "WATER" };
        private readonly List<Cell> _cells = new List<Cell>();

        protected override void BuildContent(RectTransform root)
        {
            var bandGo = new GameObject("Band", typeof(RectTransform));
            bandGo.transform.SetParent(root, false);
            _band = bandGo.AddComponent<ArcBandGraphic>();
            _band.raycastTarget = false;
            bandGo.AddComponent<VisorWarp>();
            var brt = (RectTransform)bandGo.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);

            _utcLabel = HudText.Make(root, "UtcLabel", 11f, TextAlignmentOptions.Left);
            _utcValue = HudText.Make(root, "UtcValue", 13f, TextAlignmentOptions.Left);
            _statusLabel = HudText.Make(root, "StatusLabel", 11f, TextAlignmentOptions.Right);
            _statusValue = HudText.Make(root, "StatusValue", 15f, TextAlignmentOptions.Right);

            for (int i = 0; i < CellNames.Length; i++)
            {
                var c = new Cell();
                c.Label = HudText.Make(root, "CellLabel" + i, 11f, TextAlignmentOptions.Center);
                c.Value = HudText.Make(root, "CellValue" + i, 17f, TextAlignmentOptions.Center);
                c.LabelRt = c.Label.rectTransform;
                c.ValueRt = c.Value.rectTransform;
                _cells.Add(c);
            }
        }

        public override void Layout(float scale)
        {
            float sw = Screen.width;
            float halfW = sw * (HudConfig.TopBarWidthPct != null ? HudConfig.TopBarWidthPct.Value : 0.98f) * 0.5f;
            float h = HudConfig.TopBarHeight.Value * scale;
            float dip = HudConfig.TopBarCurve.Value;

            // Band midline top point sits h/2 + margin under the screen top.
            float topY = Screen.height * 0.5f - 10f - h * 0.5f;
            Root.anchoredPosition = new Vector2(0f, topY);

            _band.HalfWidth = halfW;
            _band.Thickness = h;
            _band.Dip = dip;

            // Cells across the middle 55% of the bar, following the sag.
            float cellSpan = halfW * 1.1f;
            for (int i = 0; i < _cells.Count; i++)
            {
                float x = -cellSpan * 0.5f + cellSpan * (i + 0.5f) / _cells.Count;
                float y = -_band.DropAt(x);
                var c = _cells[i];
                c.LabelRt.sizeDelta = new Vector2(150f, 14f);
                c.ValueRt.sizeDelta = new Vector2(170f, 22f);
                c.LabelRt.anchoredPosition = new Vector2(x, y + h * 0.17f);
                c.ValueRt.anchoredPosition = new Vector2(x, y - h * 0.15f);
            }

            float edgeX = halfW * 0.88f;
            float edgeDrop = -_band.DropAt(edgeX);
            _utcLabel.rectTransform.sizeDelta = new Vector2(260f, 14f);
            _utcValue.rectTransform.sizeDelta = new Vector2(260f, 18f);
            _utcLabel.rectTransform.anchoredPosition = new Vector2(-edgeX + 130f, edgeDrop + h * 0.16f);
            _utcValue.rectTransform.anchoredPosition = new Vector2(-edgeX + 130f, edgeDrop - h * 0.14f);
            _statusLabel.rectTransform.sizeDelta = new Vector2(240f, 14f);
            _statusValue.rectTransform.sizeDelta = new Vector2(240f, 20f);
            _statusLabel.rectTransform.anchoredPosition = new Vector2(edgeX - 120f, edgeDrop + h * 0.16f);
            _statusValue.rectTransform.anchoredPosition = new Vector2(edgeX - 120f, edgeDrop - h * 0.14f);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var band = _band;
            band.color = HudPalette.PanelFill.Value;
            band.EdgeInner = HudPalette.LineAccent.Value;
            var outer = HudPalette.PanelBorder.Value;
            outer.a *= 0.6f;
            band.EdgeOuter = outer;
            band.EdgeWidth = Mathf.Max(0.6f, HudConfig.BorderWidth.Value);

            Color label = HudPalette.TextLabel.Value;
            Color dim = HudPalette.TextDim.Value;
            float fs = HudText.Size(HudConfig.LabelFontSize.Value) * scale;
            float vs = HudText.Size(HudConfig.ValueFontSize.Value) * scale;

            Style(_utcLabel, label, fs); _utcLabel.text = "UTC";
            Style(_utcValue, dim, fs * 1.1f);
            HudText.Set(_utcValue, s.DateText + "  " + s.TimeText);

            Style(_statusLabel, label, fs);
            HudText.Set(_statusLabel, s.IsRobot ? "CORE STATUS" : "SUIT STATUS");
            Color statusColor = s.SuitStatusLevel >= 3 ? HudPalette.Critical.Value
                : s.SuitStatusLevel == 2 ? HudPalette.Warn.Value
                : s.SuitStatusLevel == 1 ? HudPalette.Warn.Value
                : HudPalette.TextValue.Value;
            Style(_statusValue, statusColor, vs * 0.9f);
            HudText.Set(_statusValue, s.SuitStatus);

            // PRESSURE
            SetCell(0, s.HasAtmosphere ? s.PressureKPa.ToString("0") + Small(" kPa") : "--",
                s.HasAtmosphere ? PressureLevel(s.PressureKPa) : 0, label, fs, vs);
            // O2 fraction of ambient
            SetCell(1, s.HasAtmosphere ? (s.O2Fraction * 100f).ToString("0.0") + Small(" %") : "--",
                s.HasAtmosphere ? (s.O2Fraction < 0.10f ? 2 : s.O2Fraction < 0.18f ? 1 : 0) : 0,
                label, fs, vs);
            // TEMP
            SetCell(2, s.HasAtmosphere ? s.TempC.ToString("0.0") + Small(" °C") : "--",
                s.HasAtmosphere ? TempLevel(s.TempC) : 0, label, fs, vs);
            // POWER (suit / robot battery)
            SetCell(3, s.SuitBatteryPct >= 0 ? s.SuitBatteryPct + Small(" %") : "--",
                s.SuitBatteryPct < 0 ? 2 : s.SuitBatteryPct <= 10 ? 2 : s.SuitBatteryPct <= 25 ? 1 : 0,
                label, fs, vs);
            // WATER (hydration; robots have none)
            SetCell(4, s.IsRobot ? "--" : Mathf.RoundToInt(s.WaterRatio * 100f) + Small(" %"),
                s.IsRobot ? 0 : s.WaterRatio <= 0.2f ? 2 : s.WaterRatio <= 0.4f ? 1 : 0,
                label, fs, vs);
        }

        private void SetCell(int i, string value, int level, Color labelColor, float fs, float vs)
        {
            var c = _cells[i];
            Style(c.Label, labelColor, fs);
            HudText.Set(c.Label, i == 1 ? "O<size=70%>2</size>" : CellNames[i]);
            Color v = level >= 2 ? HudPalette.Critical.Value
                : level == 1 ? HudPalette.Warn.Value
                : HudPalette.TextValue.Value;
            Style(c.Value, v, vs);
            HudText.Set(c.Value, value);
        }

        private static int PressureLevel(float kPa)
            => kPa < 6.3f || kPa > 607.95f ? 2 : kPa < 20f || kPa > 303.97f ? 1 : 0;

        private static int TempLevel(float c)
            => c < -10f || c > 80f ? 2 : c < 0f || c > 50f ? 1 : 0;

        private static string Small(string unit) => "<size=62%>" + unit + "</size>";

        private static void Style(TextMeshProUGUI t, Color c, float size)
        {
            HudText.Sync(t);
            t.color = c;
            t.fontSize = size;
        }

        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            float halfW = Screen.width * HudConfig.TopBarWidthPct.Value * 0.5f;
            float h = HudConfig.TopBarHeight.Value * scale;
            var pos = Root.anchoredPosition;
            into.Add(new HudEditTarget
            {
                Title = "Top status bar",
                Palette = new[] { "HudPanelFill", "HudLineAccent", "HudPanelBorder",
                    "HudTextLabel", "HudTextValue", "HudTextDim", "HudWarn", "HudCritical" },
                Values = new ConfigEntryBase[] { HudConfig.ShowTopBar, HudConfig.TopBarHeight,
                    HudConfig.TopBarCurve, HudConfig.TopBarWidthPct, HudConfig.LabelFontSize,
                    HudConfig.ValueFontSize, HudConfig.BorderWidth },
                CanvasRect = new Rect(pos.x - halfW, pos.y - h * 0.5f - HudConfig.TopBarCurve.Value,
                    halfW * 2f, h + HudConfig.TopBarCurve.Value + 6f),
            });
        }
    }
}
