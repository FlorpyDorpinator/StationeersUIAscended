using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// A heading ribbon: cardinal letters and tick marks slide past a fixed centre caret,
    /// with the exact degrees read out underneath. Heading numbers match the vanilla
    /// Navigation readout ((yaw + 180) % 360, sampled from the camera so it follows
    /// free-look) — the value arrives pre-computed in <see cref="HudSnapshot.HeadingDeg"/>.
    ///
    /// The ribbon fills the element rect: width and height come from the document, and the
    /// ticks/labels/caret/degrees are laid out inside it so resizing in the designer scales
    /// the whole strip sensibly.
    ///
    /// Ribbon content moves every frame, so it is exempt from the vertex warp: a position
    /// change doesn't rebuild the mesh and would otherwise leave a stale warp offset baked
    /// in. Only the backing strip warps (it holds still); the ticks are plain Images and the
    /// labels/degrees are created with warp:false. At top-centre the difference is sub-pixel.
    /// </summary>
    internal sealed class CompassWidget : HudElementView
    {
        // Sized for the WORST the props allow (fov 200° / tick 5° -> 41 visible + edge
        // slack; cardinals every 15° -> 15). An undersized pool truncates ONE edge of
        // the ribbon asymmetrically, which reads as a broken compass.
        private const int TickPool = 48;
        private const int LabelPool = 16;
        private static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private PanelGraphic _back;
        private RectTransform _mask;
        private readonly List<Image> _ticks = new List<Image>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private TriangleGraphic _caret;
        private TextMeshProUGUI _degrees;
        private Vector2 _center; // element centre captured in Layout (for per-frame warp)

        // The degree readout string rebuilds only when the rounded heading changes — a
        // per-frame "<n>°" concat is otherwise pure garbage.
        private int _lastDeg = int.MinValue;
        private string _degStr;

        // The backing strip takes the trapezoid insets (base supplies the sliders).
        protected override bool SupportsTrapezoid => true;

        /// <summary>Place a ribbon child at the given LOCAL offset from the element centre,
        /// bent onto the visor curve. The ribbon moves every frame, so its mesh is never
        /// re-warped by VisorWarp — instead we warp its POSITION here: the barrel modes bend
        /// x/y, mode C's cylinder pushes z, so the ticks/labels ride the same curve the
        /// backing strip does (which the play-test found they weren't). Parent = _mask, so
        /// we subtract the mask's own anchor.</summary>
        private void PlaceOnCurve(RectTransform rt, Vector2 localOffset)
        {
            Vector3 abs = new Vector3(_center.x + localOffset.x, _center.y + localOffset.y, 0f);
            if (HudWarp.Enabled) abs = HudWarp.Warp(abs);
            rt.anchoredPosition3D = abs - new Vector3(_mask.anchoredPosition.x, _mask.anchoredPosition.y, 0f);
        }

        /// <summary>Same, but for children parented directly under Root (caret/degrees).</summary>
        private void PlaceOnCurveRoot(RectTransform rt, Vector2 localOffset)
        {
            Vector3 abs = new Vector3(_center.x + localOffset.x, _center.y + localOffset.y, 0f);
            if (HudWarp.Enabled) abs = HudWarp.Warp(abs);
            rt.anchoredPosition3D = abs;
        }

        protected override void BuildContent(RectTransform root)
        {
            _back = MakePanel(root, "Back");

            var maskGo = new GameObject("Mask", typeof(RectTransform));
            maskGo.transform.SetParent(root, false);
            maskGo.AddComponent<RectMask2D>();
            _mask = (RectTransform)maskGo.transform;
            _mask.anchorMin = _mask.anchorMax = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < TickPool; i++)
            {
                var go = new GameObject("Tick" + i, typeof(RectTransform));
                go.transform.SetParent(_mask, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                _ticks.Add(img);
            }
            for (int i = 0; i < LabelPool; i++)
                _labels.Add(HudText.Make(_mask, "Cardinal" + i, 12f, TextAlignmentOptions.Center, warp: false));

            var caretGo = new GameObject("Caret", typeof(RectTransform));
            caretGo.transform.SetParent(root, false);
            _caret = caretGo.AddComponent<TriangleGraphic>();
            _caret.raycastTarget = false;
            var crt = _caret.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(14f, 12f);

            _degrees = HudText.Make(root, "Degrees", 12f, TextAlignmentOptions.Center, warp: false);
        }

        public override void Layout(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float w = s.x;
            float h = s.y;
            Root.anchoredPosition = Vector2.zero;
            _center = c;

            ((RectTransform)_back.transform).anchoredPosition = c;
            _back.SetShape(w, h, Mathf.Min(Radius(Def.RTL), h * 0.4f),
                InsetTop(scale), InsetBottom(scale));

            // The mask stays at the (unwarped) centre; its children are individually bent
            // onto the curve every frame. Taller than the strip so a bent tick isn't clipped.
            _mask.anchoredPosition = c + new Vector2(0f, 2f * scale);
            _mask.sizeDelta = new Vector2(w - 8f * scale, h * 1.6f);

            _degrees.rectTransform.sizeDelta = new Vector2(90f * scale, 14f * scale);
            // Caret + degrees are bent per frame in UpdatePanel (PlaceOnCurveRoot).
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var sz = SizeFor(scale);
            float w = sz.x;
            float h = sz.y;
            float span = Def.GetF("fov", 90f);
            float tickEvery = Mathf.Max(1f, Def.GetF("tickDeg", 15f));
            float cardinalEvery = Mathf.Max(tickEvery, Def.GetF("cardinalDeg", 45f));
            bool showDegrees = Def.GetB("degrees", true);
            float heading = s.HeadingDeg;

            // Boxless mode (Glassy 2.0 top bar): the strip fades straight into whatever
            // it sits on — no backdrop, no border.
            bool showBox = Def.GetB("box", true);
            _back.enabled = showBox;
            if (showBox)
            {
                _back.color = FillColor();
                _back.BorderColor = BorderColor();
                _back.BorderWidth = BorderWidthFor();
                ApplyGlass(_back);
            }

            _caret.Configure(pointsUp: false, size: 9f);
            _caret.color = HudPalette.CompassNeedle.Value;
            // No per-frame SetVerticesDirty: the caret geometry never moves, and Configure /
            // the color setter already dirty the mesh on any actual change. Position (below)
            // is a transform update that needs no vertex rebuild.
            PlaceOnCurveRoot((RectTransform)_caret.transform, new Vector2(0f, h * 0.5f + 7f * scale));

            if (showDegrees)
            {
                _degrees.gameObject.SetActive(true);
                PlaceOnCurveRoot(_degrees.rectTransform, new Vector2(0f, -h * 0.5f - 9f * scale));
                HudText.Sync(_degrees);
                _degrees.fontSize = HudText.Size(12f * Def.FontScale) * scale;
                _degrees.color = HudPalette.CompassCardinal.Value;
                int hd = Mathf.RoundToInt(heading);
                if (hd != _lastDeg || _degStr == null) { _lastDeg = hd; _degStr = hd + "°"; }
                HudText.Set(_degrees, _degStr);
            }
            else
            {
                _degrees.gameObject.SetActive(false);
            }

            // Which ticks read as cardinals: the ratio of cardinal spacing to tick spacing
            // (3 for the vanilla 45°/15°), so the tall/labelled marks land on the multiples
            // of cardinalEvery regardless of how the two knobs are set.
            int cardStep = Mathf.Max(1, Mathf.RoundToInt(cardinalEvery / tickEvery));

            float pxPerDeg = (w - 12f * scale) / span;
            var tickColor = HudPalette.CompassTick.Value;
            var cardColor = HudPalette.CompassCardinal.Value;
            int tick = 0, label = 0;

            int first = Mathf.CeilToInt((heading - span * 0.5f) / tickEvery);
            int last = Mathf.FloorToInt((heading + span * 0.5f) / tickEvery);
            for (int d = first; d <= last && tick < TickPool; d++)
            {
                float deg = d * tickEvery;
                float offset = Mathf.DeltaAngle(heading, deg) * pxPerDeg;
                bool cardinal = ((d % cardStep) + cardStep) % cardStep == 0;
                var img = _ticks[tick++];
                img.gameObject.SetActive(true);
                img.color = cardinal ? cardColor : tickColor;
                img.rectTransform.sizeDelta = new Vector2(
                    (cardinal ? 2f : 1f) * scale, cardinal ? h * 0.34f : h * 0.2f);
                // Offset is relative to the ELEMENT centre; the mask sits +2*scale above it.
                PlaceOnCurve(img.rectTransform, new Vector2(
                    offset, 2f * scale - h * 0.5f + img.rectTransform.sizeDelta.y * 0.5f + 3f * scale));

                if (cardinal && label < LabelPool)
                {
                    // Name the mark by its true 8-point bearing, so the letters stay N/E/S/W
                    // even if the cardinal spacing is retuned away from 45°.
                    int ci = ((Mathf.RoundToInt(deg / 45f)) % 8 + 8) % 8;
                    var t = _labels[label++];
                    t.gameObject.SetActive(true);
                    HudText.Sync(t);
                    t.fontSize = HudText.Size(12f * Def.FontScale) * scale;
                    t.color = cardColor;
                    HudText.Set(t, Cardinals[ci]);
                    t.rectTransform.sizeDelta = new Vector2(40f * scale, 16f * scale);
                    PlaceOnCurve(t.rectTransform, new Vector2(offset, 2f * scale + h * 0.16f));
                }
            }
            for (int i = tick; i < TickPool; i++) _ticks[i].gameObject.SetActive(false);
            for (int i = label; i < LabelPool; i++) _labels[i].gameObject.SetActive(false);
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Backdrop box", () => d.GetB("box", true),
                v => d.SetB("box", v)));
            into.Add(HudProp.F("Compass FOV°", () => d.GetF("fov", 90f),
                v => d.SetF("fov", Mathf.Clamp(v, 40f, 200f)), 40f, 200f));
            into.Add(HudProp.Bool("Show degrees", () => d.GetB("degrees", true),
                v => d.SetB("degrees", v)));
            into.Add(HudProp.F("Tick spacing°", () => d.GetF("tickDeg", 15f),
                v => d.SetF("tickDeg", Mathf.Clamp(v, 5f, 45f)), 5f, 45f));
            into.Add(HudProp.F("Cardinal spacing°", () => d.GetF("cardinalDeg", 45f),
                v => d.SetF("cardinalDeg", Mathf.Clamp(v, 15f, 180f)), 15f, 180f));
        }
    }
}
