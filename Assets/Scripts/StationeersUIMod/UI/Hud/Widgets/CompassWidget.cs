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

        // The backing strip is a real PanelGraphic (fill/border/glass all apply).
        protected override bool SupportsPanelAppearance => true;

        // Compass never reads TextColor()/TextColorFor — cardinal labels, ticks, caret and the
        // degree readout all resolve through their own dedicated colour refs (see UpdatePanel /
        // DescribeProps below), so the universal "Text / accent" row can never touch a pixel here.
        protected override bool UsesAccentColor => false;

        // The backdrop box is optional ("box" key, default ON) — surface the same "effects are
        // saved but invisible" hint the base offers, under Compass's own inspector name for it.
        protected override bool OptionalPanelBackgroundIsOff => !Def.GetBFor(EditBare(Def), "box", true);
        protected override string OptionalPanelBackgroundToggleName => "Backdrop box";

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
            _back.SetShape(w, h, Mathf.Min(RadiusTL(), h * 0.4f),
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

            // The clip WINDOW must ride the same bend as the content: the barrel modes (and
            // their bloom-forced RT variants) displace and FIT-SCALE x/y — at the top of the
            // screen that is tens of px of drift at full strength — and a static mask window
            // then clips the whole bent ribbon away (play-test: "compass gets hid when I
            // curve the screen"). Warp the mask's own anchor per frame; PlaceOnCurve subtracts
            // the CURRENT mask anchor, so children keep landing at their correct absolute spot.
            Vector2 maskC = _center + new Vector2(0f, 2f * scale);
            if (HudWarp.Enabled)
            {
                var wm = HudWarp.Warp(new Vector3(maskC.x, maskC.y, 0f));
                maskC = new Vector2(wm.x, wm.y);
            }
            _mask.anchoredPosition = maskC;
            float span = Def.GetFFor(LayoutBare, "fov", 90f);
            float tickEvery = Mathf.Max(1f, Def.GetFFor(LayoutBare, "tickDeg", 15f));
            float cardinalEvery = Mathf.Max(tickEvery, Def.GetFFor(LayoutBare, "cardinalDeg", 45f));
            bool showDegrees = Def.GetBFor(LayoutBare, "degrees", true);
            float heading = s.HeadingDeg;

            // Boxless mode (Glassy 2.0 top bar): the strip fades straight into whatever
            // it sits on — no backdrop, no border.
            bool showBox = Def.GetBFor(LayoutBare, "box", true);
            _back.enabled = showBox;
            if (showBox)
            {
                _back.color = FillColor();
                _back.BorderColor = BorderColor();
                _back.BorderWidth = BorderWidthFor();
                ApplyGlass(_back);
            }

            _caret.Configure(pointsUp: false, size: 9f);
            _caret.color = GlobalOr(Def.GetSFor(LayoutBare, "needleColor", ""), HudPalette.CompassNeedle.Value);
            // No per-frame SetVerticesDirty: the caret geometry never moves, and Configure /
            // the color setter already dirty the mesh on any actual change. Position (below)
            // is a transform update that needs no vertex rebuild.
            PlaceOnCurveRoot((RectTransform)_caret.transform, new Vector2(0f, h * 0.5f + 7f * scale));

            if (showDegrees)
            {
                _degrees.gameObject.SetActive(true);
                PlaceOnCurveRoot(_degrees.rectTransform, new Vector2(0f, -h * 0.5f - 9f * scale));
                HudText.Sync(_degrees);
                _degrees.fontSize = HudText.Size(12f * Def.FontScaleFor(LayoutBare)) * scale;
                _degrees.color = GlobalOr(Def.GetSFor(LayoutBare, "degreesColor", ""), HudPalette.CompassCardinal.Value);
                // % 360 so 359.7 reads "0°", never "360°" — the readout must agree with the tick
                // strip about where north is (the tester's screenshot showed "360°" with the caret
                // visibly right of N; half of that confusion was this label).
                int hd = Mathf.RoundToInt(heading) % 360;
                if (hd != _lastDeg || _degStr == null) { _lastDeg = hd; _degStr = hd + "°"; }
                HudText.Set(_degrees, _degStr);
            }
            else
            {
                _degrees.gameObject.SetActive(false);
            }

            // TWO INDEPENDENT LATTICES. History, because this block has now been wrong twice:
            // (1) originally ticks AND letters lived on one unwrapped lattice (deg = d * tickEvery)
            //     — a non-divisor spacing (shipped 14.597) left a 360 % step ≈ 9.7° SEAM at the
            //     0/360 wrap ("N is 10° off from one side", playtest 2026-08-03);
            // (2) the first fix snapped that lattice and derived cardinals as every Nth tick with
            //     N walked DOWN until it divided ticksPerTurn — but 14.597 snaps to 25 ticks/turn,
            //     25 = 5², so N fell 4→3→2→1 and EVERY tick became a nearest-45°-named letter
            //     ("NW NWN N N NE NE NE", playtest 2026-08-06).
            // The truth a compass owes the player: LETTERS sit on their true bearings, always.
            // So the letters get their OWN lattice — the eight fixed compass points, whose 45°
            // spacing divides 360 by construction (no seam possible) — and the minor ticks keep
            // the snapped divisor lattice purely as a density/texture knob. The two never mix, so
            // no tick spacing can ever misplace or spam a letter again.
            int ticksPerTurn = Mathf.Max(1, Mathf.RoundToInt(360f / tickEvery));
            float step = 360f / ticksPerTurn;

            // cardinalDeg now selects HOW MANY of the eight points show: below ~67.5 = all eight
            // (N NE E SE S SW W NW), above = the four majors only. (Free values like the shipped
            // 62.535 land on "all eight" — visually identical to the pre-regression intent.)
            int cardStride = cardinalEvery < 67.5f ? 1 : 2;

            float pxPerDeg = (w - 12f * scale) / span;
            float halfSpan = span * 0.5f;
            var tickColor = GlobalOr(Def.GetSFor(LayoutBare, "tickColor", ""), HudPalette.CompassTick.Value);
            var cardColor = GlobalOr(Def.GetSFor(LayoutBare, "cardinalColor", ""), HudPalette.CompassCardinal.Value);
            int tick = 0, label = 0;

            // ---- cardinal pass: tall marks + letters on the true 45° points ----
            for (int ci = 0; ci < 8 && tick < TickPool && label < LabelPool; ci += cardStride)
            {
                float delta = Mathf.DeltaAngle(heading, ci * 45f);
                if (Mathf.Abs(delta) > halfSpan) continue;   // off the visible arc
                float offset = delta * pxPerDeg;

                var img = _ticks[tick++];
                img.gameObject.SetActive(true);
                img.color = cardColor;
                img.rectTransform.sizeDelta = new Vector2(2f * scale, h * 0.34f);
                // Offset is relative to the ELEMENT centre; the mask sits +2*scale above it.
                PlaceOnCurve(img.rectTransform, new Vector2(
                    offset, 2f * scale - h * 0.5f + img.rectTransform.sizeDelta.y * 0.5f + 3f * scale));

                var t = _labels[label++];
                t.gameObject.SetActive(true);
                HudText.Sync(t);
                t.fontSize = HudText.Size(12f * Def.FontScaleFor(LayoutBare)) * scale;
                t.color = cardColor;
                HudText.Set(t, Cardinals[ci]);
                t.rectTransform.sizeDelta = new Vector2(40f * scale, 16f * scale);
                PlaceOnCurve(t.rectTransform, new Vector2(offset, 2f * scale + h * 0.16f));
            }

            // ---- minor-tick pass: the snapped divisor lattice, no letters ----
            int first = Mathf.CeilToInt((heading - halfSpan) / step);
            int last = Mathf.FloorToInt((heading + halfSpan) / step);
            for (int d = first; d <= last && tick < TickPool; d++)
            {
                float deg = d * step;
                // A minor mark sitting on (or visually crowding) a shown cardinal is clutter — a
                // doubled line a few px from the tall one. Skip any minor within 40% of a step of
                // a cardinal point this frame is drawing.
                float nearest = Mathf.Repeat(deg, 45f * cardStride);
                float toCardinal = Mathf.Min(nearest, 45f * cardStride - nearest);
                if (toCardinal < step * 0.4f) continue;

                var img = _ticks[tick++];
                img.gameObject.SetActive(true);
                img.color = tickColor;
                img.rectTransform.sizeDelta = new Vector2(1f * scale, h * 0.2f);
                PlaceOnCurve(img.rectTransform, new Vector2(
                    Mathf.DeltaAngle(heading, deg) * pxPerDeg,
                    2f * scale - h * 0.5f + img.rectTransform.sizeDelta.y * 0.5f + 3f * scale));
            }
            for (int i = tick; i < TickPool; i++) _ticks[i].gameObject.SetActive(false);
            for (int i = label; i < LabelPool; i++) _labels[i].gameObject.SetActive(false);
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Backdrop box", () => d.GetBFor(EditBare(d), "box", true),
                v => d.SetBFor(EditBare(d), "box", v)));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            // Per-element colours for each ribbon sub-element. Each falls back to its global
            // HudCompass* palette entry (so leaving them blank keeps the shipped look), and the
            // degree readout is its OWN ref so it can differ from the cardinal letters/ticks.
            into.Add(HudProp.Color("Needle colour", () => d.GetSFor(EditBare(d), "needleColor", ""),
                v => d.SetSFor(EditBare(d), "needleColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.CompassNeedle.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Tick colour", () => d.GetSFor(EditBare(d), "tickColor", ""),
                v => d.SetSFor(EditBare(d), "tickColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.CompassTick.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Cardinal colour (ticks + letters)", () => d.GetSFor(EditBare(d), "cardinalColor", ""),
                v => d.SetSFor(EditBare(d), "cardinalColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.CompassCardinal.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Degrees colour", () => d.GetSFor(EditBare(d), "degreesColor", ""),
                v => d.SetSFor(EditBare(d), "degreesColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.CompassCardinal.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            // Tier-aware (Wave C): a bare compass can read wider and sparser than the suited one.
            into.Add(HudProp.F("Compass FOV°", () => d.GetFFor(EditBare(d), "fov", 90f),
                v => d.SetFFor(EditBare(d), "fov", Mathf.Clamp(v, 40f, 200f)), 40f, 200f));
            into.Add(HudProp.Bool("Show degrees", () => d.GetBFor(EditBare(d), "degrees", true),
                v => d.SetBFor(EditBare(d), "degrees", v)));
            into.Add(HudProp.F("Tick spacing°", () => d.GetFFor(EditBare(d), "tickDeg", 15f),
                v => d.SetFFor(EditBare(d), "tickDeg", Mathf.Clamp(v, 5f, 45f)), 5f, 45f));
            into.Add(HudProp.F("Cardinal spacing°", () => d.GetFFor(EditBare(d), "cardinalDeg", 45f),
                v => d.SetFFor(EditBare(d), "cardinalDeg", Mathf.Clamp(v, 15f, 180f)), 15f, 180f));
        }
    }
}
