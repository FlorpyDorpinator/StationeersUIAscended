using System.Collections.Generic;
using Assets.Scripts;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The bottom-right vitals card: the game's live 3D player portrait as a cyan
    /// HOLOGRAM (tinted RenderTexture + scanlines) beside HEALTH / O₂ / POWER / TEMP
    /// rows, with DAY + time along the foot. Numbers are a suit instrument — the card
    /// only exists at SUITED/ROBOT tier (BARE gets the felt-sense words instead).
    ///
    /// The portrait RT is re-read EVERY frame (RefreshPortrait replaces it wholesale on
    /// any HUD-scale/settings change — caching the pointer shows a dead texture). If the
    /// player disabled the vanilla portrait, the camera is re-enabled while the hologram
    /// is on and vanilla truth is restored through the game's own RefreshPortrait().
    /// </summary>
    internal sealed class VitalsCard : HudPanel
    {
        public override string Id => "Vitals";
        public override bool SuitTier => true;
        public override ConfigEntry<bool> Toggle => HudConfig.ShowVitals;
        public override bool VisibleAt(HudTier tier) => tier != HudTier.Bare;

        private PanelGraphic _card;
        private RawImage _holo;
        private ScanlineGraphic _holoScan;
        private PanelGraphic _footLine;
        private TextMeshProUGUI _day, _time;
        private readonly TextMeshProUGUI[] _rowLabel = new TextMeshProUGUI[4];
        private readonly TextMeshProUGUI[] _rowValue = new TextMeshProUGUI[4];
        private readonly PanelGraphic[] _rowBar = new PanelGraphic[4];
        private static readonly string[] RowNames = { "HEALTH", "O2", "POWER", "TEMP" };

        private bool _forcedPortraitCam;
        private bool _hidVanillaPortrait;

        protected override void BuildContent(RectTransform root)
        {
            _card = MakePanel(root, "Card");

            var holoGo = new GameObject("Hologram", typeof(RectTransform));
            holoGo.transform.SetParent(root, false);
            _holo = holoGo.AddComponent<RawImage>();
            _holo.raycastTarget = false;
            holoGo.AddComponent<VisorWarp>();
            var hrt = _holo.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);

            var scanGo = new GameObject("HoloScan", typeof(RectTransform));
            scanGo.transform.SetParent(root, false);
            _holoScan = scanGo.AddComponent<ScanlineGraphic>();
            _holoScan.raycastTarget = false;
            scanGo.AddComponent<VisorWarp>(); // must bend WITH the hologram underneath
            var srt = (RectTransform)scanGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < 4; i++)
            {
                _rowLabel[i] = HudText.Make(root, "RowLabel" + i, 11f, TextAlignmentOptions.Left);
                _rowValue[i] = HudText.Make(root, "RowValue" + i, 13f, TextAlignmentOptions.Right);
                _rowBar[i] = MakeBar(root, "RowBar" + i);
            }
            _footLine = MakeBar(root, "FootLine");
            _day = HudText.Make(root, "Day", 12f, TextAlignmentOptions.Left);
            _time = HudText.Make(root, "Time", 12f, TextAlignmentOptions.Right);
        }

        public override void Layout(float scale)
        {
            float w = HudConfig.VitalsWidth.Value * scale;
            float h = HudConfig.VitalsHeight.Value * scale;
            float cx = Screen.width * 0.5f - 20f - w * 0.5f;
            float cy = -Screen.height * 0.5f + 18f + h * 0.5f;
            Root.anchoredPosition = Vector2.zero;

            ((RectTransform)_card.transform).anchoredPosition = new Vector2(cx, cy);
            _card.SetShape(w, h, HudConfig.CornerRadius.Value);

            float holoW = w * 0.30f;
            float holoH = h - 22f;
            float holoX = cx - w * 0.5f + 10f + holoW * 0.5f;
            _holo.rectTransform.sizeDelta = new Vector2(holoW, holoH);
            _holo.rectTransform.anchoredPosition = new Vector2(holoX, cy + 4f);
            ((RectTransform)_holoScan.transform).sizeDelta = new Vector2(holoW, holoH);
            ((RectTransform)_holoScan.transform).anchoredPosition = new Vector2(holoX, cy + 4f);

            float rowLeft = cx - w * 0.5f + holoW + 24f;
            float rowRight = cx + w * 0.5f - 12f;
            float rowW = rowRight - rowLeft;
            float footH = 24f;
            float rowsTop = cy + h * 0.5f - 16f;
            float rowsSpan = h - 16f - footH - 12f;
            for (int i = 0; i < 4; i++)
            {
                float y = rowsTop - rowsSpan * (i + 0.5f) / 4f;
                _rowLabel[i].rectTransform.sizeDelta = new Vector2(rowW * 0.5f, 14f);
                _rowLabel[i].rectTransform.anchoredPosition = new Vector2(rowLeft + rowW * 0.25f, y + 2f);
                _rowValue[i].rectTransform.sizeDelta = new Vector2(rowW * 0.55f, 16f);
                _rowValue[i].rectTransform.anchoredPosition = new Vector2(rowRight - rowW * 0.275f, y + 2f);
                ((RectTransform)_rowBar[i].transform).anchoredPosition = new Vector2(rowLeft + rowW * 0.5f, y - 8f);
                _rowBar[i].SetShape(rowW, 1.4f, 0.7f);
            }

            ((RectTransform)_footLine.transform).anchoredPosition = new Vector2(cx, cy - h * 0.5f + footH);
            _footLine.SetShape(w - 20f, 1.4f, 0.7f);
            _day.rectTransform.sizeDelta = new Vector2(w * 0.5f, 16f);
            _day.rectTransform.anchoredPosition = new Vector2(cx - w * 0.25f + 8f, cy - h * 0.5f + footH * 0.5f + 1f);
            _time.rectTransform.sizeDelta = new Vector2(w * 0.5f, 16f);
            _time.rectTransform.anchoredPosition = new Vector2(cx + w * 0.25f - 8f, cy - h * 0.5f + footH * 0.5f + 1f);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            _card.color = HudPalette.PanelFill.Value;
            _card.BorderColor = HudPalette.PanelBorder.Value;
            _card.BorderWidth = HudConfig.BorderWidth.Value;

            UpdateHologram();

            float fs = HudText.Size(HudConfig.VitalsRowFontSize.Value) * scale;

            SetRow(0, s.HealthRatio, Mathf.RoundToInt(s.HealthRatio * 100f) + " %",
                s.HealthRatio < 0.25f ? 2 : s.HealthRatio < 0.75f ? 1 : 0, fs);
            SetRow(1, s.O2Quality, Mathf.RoundToInt(s.O2Quality * 100f) + " %",
                s.O2Quality < 0.75f ? 2 : s.O2Quality < 1f ? 1 : 0, fs);
            float power = Mathf.Max(0, s.SuitBatteryPct) / 100f;
            SetRow(2, power, s.SuitBatteryPct >= 0 ? s.SuitBatteryPct + " %" : "--",
                s.SuitBatteryPct < 0 || s.SuitBatteryPct <= 10 ? 2 : s.SuitBatteryPct <= 25 ? 1 : 0, fs);
            SetRow(3, -1f, s.FeltValid ? s.FeltTempC.ToString("0.0") + " °C" : "--",
                !s.FeltValid ? 0
                : s.FeltTempC < -10f || s.FeltTempC > 80f ? 2
                : s.FeltTempC < 0f || s.FeltTempC > 50f ? 1 : 0, fs);

            var accent = HudPalette.LineAccent.Value;
            accent.a *= 0.5f;
            _footLine.color = accent;

            HudText.Sync(_day); HudText.Sync(_time);
            _day.fontSize = _time.fontSize = fs * 0.95f;
            _day.color = HudPalette.TextLabel.Value;
            _time.color = HudPalette.TextValue.Value;
            HudText.Set(_day, "DAY " + s.Day);
            HudText.Set(_time, s.TimeText);
        }

        private void SetRow(int i, float ratio, string value, int level, float fs)
        {
            HudText.Sync(_rowLabel[i]); HudText.Sync(_rowValue[i]);
            _rowLabel[i].fontSize = fs * 0.85f;
            _rowValue[i].fontSize = fs;
            _rowLabel[i].color = HudPalette.TextLabel.Value;
            HudText.Set(_rowLabel[i], i == 1 ? "O<size=70%>2</size>" : RowNames[i]);
            _rowValue[i].color = level >= 2 ? HudPalette.Critical.Value
                : level == 1 ? HudPalette.Warn.Value : HudPalette.TextValue.Value;
            HudText.Set(_rowValue[i], value);

            // The hairline under each row doubles as a mini bar: bright up to the ratio.
            var bar = _rowBar[i];
            var baseCol = HudPalette.PanelBorder.Value;
            baseCol.a *= 0.6f;
            bar.color = ratio < 0f ? baseCol
                : Color.Lerp(baseCol, HudPalette.LineAccent.Value, Mathf.Clamp01(ratio));
            bar.BorderColor = Color.clear;
        }

        private void UpdateHologram()
        {
            bool wantHolo = HudConfig.ShowHologram == null || HudConfig.ShowHologram.Value;
            Texture rt = null;
            if (wantHolo)
            {
                try
                {
                    var cc = CameraController.Instance;
                    var cam = cc != null ? cc.PortraitCamera : null;
                    if (cam != null)
                    {
                        rt = cam.targetTexture;                    // never cache — replaced on refresh
                        if (!cam.gameObject.activeSelf)
                        {
                            cam.gameObject.SetActive(true);        // vanilla RT stays wired even when off
                            _forcedPortraitCam = true;
                        }
                        // The vanilla portrait RawImage would double-show; hide it while
                        // the hologram owns the render (restored via RefreshPortrait()).
                        var display = cc.PortraitCameraDisplay;
                        if (display != null && display.activeSelf)
                        {
                            display.SetActive(false);
                            _hidVanillaPortrait = true;
                        }
                    }
                }
                catch { }
            }
            else if (_forcedPortraitCam || _hidVanillaPortrait)
            {
                RestorePortrait();
            }

            _holo.texture = rt;
            _holo.enabled = wantHolo && rt != null;
            _holo.color = HudPalette.HologramTint.Value;
            _holoScan.color = _holo.enabled ? HudPalette.Scanline.Value : Color.clear;
        }

        /// <summary>Give the portrait back to vanilla. Manual SetActives per the vanilla
        /// setting instead of RefreshPortrait(): vanilla's refresh allocates a brand-new
        /// RenderTexture on every call and never releases the old one — calling it per
        /// suit-on/off cycle would leak steadily. Falls back to the vanilla path if the
        /// manual route throws.</summary>
        public void RestorePortrait()
        {
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

        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            float w = HudConfig.VitalsWidth.Value * scale;
            float h = HudConfig.VitalsHeight.Value * scale;
            float cx = Screen.width * 0.5f - 20f - w * 0.5f;
            float cy = -Screen.height * 0.5f + 18f + h * 0.5f;
            into.Add(new HudEditTarget
            {
                Title = "Vitals card",
                Palette = new[] { "HudPanelFill", "HudPanelBorder", "HudLineAccent",
                    "HudHologramTint", "HudScanline", "HudTextLabel", "HudTextValue",
                    "HudWarn", "HudCritical" },
                Values = new ConfigEntryBase[] { HudConfig.ShowVitals, HudConfig.ShowHologram,
                    HudConfig.VitalsWidth, HudConfig.VitalsHeight, HudConfig.VitalsRowFontSize,
                    HudConfig.CornerRadius },
                CanvasRect = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h),
            });
        }
    }
}
