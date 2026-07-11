using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The heading ribbon at top-centre: cardinal letters and tick marks slide past a
    /// fixed centre caret; the exact degrees read out underneath. Heading numbers match
    /// the vanilla Navigation readout exactly ((yaw + 180) % 360), computed from the
    /// camera so it follows free-look.
    ///
    /// Ribbon content moves every frame, so it is exempt from the vertex warp (position
    /// changes don't rebuild meshes and would leave stale warp offsets); only the
    /// backdrop panel warps. At top-centre the difference is sub-pixel.
    /// </summary>
    internal sealed class CompassRibbon : HudPanel
    {
        public override string Id => "Compass";
        public override bool SuitTier => true;
        public override ConfigEntry<bool> Toggle => HudConfig.ShowCompass;
        public override bool VisibleAt(HudTier tier) => tier != HudTier.Bare;

        private const int TickPool = 24;
        private const int LabelPool = 8;
        private static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private PanelGraphic _back;
        private RectTransform _mask;
        private readonly List<Image> _ticks = new List<Image>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private TriangleGraphic _caret;
        private TextMeshProUGUI _degrees;

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
            float w = Screen.width * HudConfig.CompassWidthPct.Value;
            float h = HudConfig.CompassHeight.Value * scale;
            float barH = HudConfig.TopBarHeight.Value * scale;
            float topY = Screen.height * 0.5f - 10f - barH - HudConfig.TopBarCurve.Value * 0.35f - h * 0.5f - 8f;
            Root.anchoredPosition = new Vector2(0f, topY);

            var brt = (RectTransform)_back.transform;
            brt.anchoredPosition = Vector2.zero;
            _back.SetShape(w, h, Mathf.Min(HudConfig.CornerRadius.Value, h * 0.4f));
            _mask.sizeDelta = new Vector2(w - 8f, h);
            _mask.anchoredPosition = new Vector2(0f, 2f);
            ((RectTransform)_caret.transform).anchoredPosition = new Vector2(0f, h * 0.5f + 7f);
            _degrees.rectTransform.sizeDelta = new Vector2(90f, 14f);
            _degrees.rectTransform.anchoredPosition = new Vector2(0f, -h * 0.5f - 9f);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            float w = Screen.width * HudConfig.CompassWidthPct.Value;
            float h = HudConfig.CompassHeight.Value * scale;
            float span = HudConfig.CompassFovDeg.Value;
            float heading = s.HeadingDeg;

            _back.color = HudPalette.PanelFill.Value;
            _back.BorderColor = HudPalette.PanelBorder.Value;
            _back.BorderWidth = HudConfig.BorderWidth.Value;

            _caret.Configure(pointsUp: false, size: 9f);
            _caret.color = HudPalette.CompassNeedle.Value;
            _caret.SetVerticesDirty();

            HudText.Sync(_degrees);
            _degrees.fontSize = HudText.Size(HudConfig.CompassFontSize.Value) * scale;
            _degrees.color = HudPalette.CompassCardinal.Value;
            HudText.Set(_degrees, Mathf.RoundToInt(heading) + "°");

            // Ticks every 15°, tall at the 45° cardinal marks.
            float pxPerDeg = (w - 12f) / span;
            var tickColor = HudPalette.CompassTick.Value;
            var cardColor = HudPalette.CompassCardinal.Value;
            int tick = 0, label = 0;

            int first = Mathf.CeilToInt((heading - span * 0.5f) / 15f);
            int last = Mathf.FloorToInt((heading + span * 0.5f) / 15f);
            for (int d = first; d <= last && tick < TickPool; d++)
            {
                float deg = d * 15f;
                float offset = Mathf.DeltaAngle(heading, deg) * pxPerDeg;
                bool cardinal = ((d % 3) + 3) % 3 == 0;   // every 45°
                var img = _ticks[tick++];
                img.gameObject.SetActive(true);
                img.color = cardinal ? cardColor : tickColor;
                img.rectTransform.sizeDelta = new Vector2(cardinal ? 2f : 1f, cardinal ? h * 0.34f : h * 0.2f);
                img.rectTransform.anchoredPosition = new Vector2(offset, -h * 0.5f + img.rectTransform.sizeDelta.y * 0.5f + 3f);

                if (cardinal && label < LabelPool)
                {
                    int ci = (((int)deg / 45) % 8 + 8) % 8;
                    var t = _labels[label++];
                    t.gameObject.SetActive(true);
                    HudText.Sync(t);
                    t.fontSize = HudText.Size(HudConfig.CompassFontSize.Value) * scale;
                    t.color = cardColor;
                    HudText.Set(t, Cardinals[ci]);
                    t.rectTransform.sizeDelta = new Vector2(40f, 16f);
                    t.rectTransform.anchoredPosition = new Vector2(offset, h * 0.16f);
                }
            }
            for (int i = tick; i < TickPool; i++) _ticks[i].gameObject.SetActive(false);
            for (int i = label; i < LabelPool; i++) _labels[i].gameObject.SetActive(false);
        }

        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            float w = Screen.width * HudConfig.CompassWidthPct.Value;
            float h = HudConfig.CompassHeight.Value * scale;
            var pos = Root.anchoredPosition;
            into.Add(new HudEditTarget
            {
                Title = "Compass",
                Palette = new[] { "HudCompassTick", "HudCompassCardinal", "HudCompassNeedle",
                    "HudPanelFill", "HudPanelBorder" },
                Values = new ConfigEntryBase[] { HudConfig.ShowCompass, HudConfig.CompassWidthPct,
                    HudConfig.CompassHeight, HudConfig.CompassFovDeg, HudConfig.CompassFontSize },
                CanvasRect = new Rect(pos.x - w * 0.5f, pos.y - h * 0.5f - 14f, w, h + 24f),
            });
        }
    }
}
