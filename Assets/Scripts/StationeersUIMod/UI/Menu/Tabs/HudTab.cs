using System.Collections.Generic;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>HUD-half comfort settings. Simple = scale/font/curvature/show-hide + the
    /// hide-vanilla group; Advanced adds low-power/glitch and shortcuts into the F9 Designer
    /// (element authoring + the effect tiers live there).</summary>
    public sealed class HudTab : IUiaTab
    {
        public string Title => "HUD";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            UiaControls.Header(col, "Look");
            UiaControls.SliderRow(col, "HUD scale", 0.6f, 1.6f, HudConfig.HudScale.Value, v => HudConfig.HudScale.Value = v, "0.00");
            var fonts = new List<string> { "(game default)" };
            fonts.AddRange(HudText.AllFontNames());
            int fi = string.IsNullOrEmpty(HudConfig.FontName.Value) ? 0 : Mathf.Max(0, fonts.IndexOf(HudConfig.FontName.Value));
            UiaControls.DropdownRow(col, "Global HUD font", fonts, fi, i => HudConfig.FontName.Value = i <= 0 ? "" : fonts[i]);
            UiaControls.SliderRow(col, "Font scale", 0.6f, 1.8f, HudConfig.FontScale.Value, v => HudConfig.FontScale.Value = v, "0.00");

            var curves = new List<string> { "Flat", "Curved (visor)" };
            int ci = HudConfig.Curvature.Value == HudCurvature.Flat ? 0 : 1;
            UiaControls.DropdownRow(col, "Curvature", curves, ci,
                i => HudConfig.Curvature.Value = i == 0 ? HudCurvature.Flat : HudCurvature.VertexWarp);
            UiaControls.SliderRow(col, "Curve strength", 0f, 1f, HudConfig.CurveStrength.Value, v => HudConfig.CurveStrength.Value = v, "0.00");

            UiaControls.Header(col, "Show");
            UiaControls.ToggleRow(col, "Visor-edge vignette", HudConfig.ShowVignette.Value, v => HudConfig.ShowVignette.Value = v);
            UiaControls.ToggleRow(col, "Player hologram", HudConfig.ShowHologram.Value, v => HudConfig.ShowHologram.Value = v);
            UiaControls.ToggleRow(col, "Diegetic tiers (words when unpowered)", HudConfig.DiegeticTiers.Value, v => HudConfig.DiegeticTiers.Value = v);
            UiaControls.ToggleRow(col, "Flicker / boot animations", HudConfig.FlickerAnimations.Value, v => HudConfig.FlickerAnimations.Value = v);

            UiaControls.Header(col, "Hide vanilla panels");
            UiaControls.ToggleRow(col, "Hands panel", UIAConfig.HideVanillaHands.Value, v => UIAConfig.HideVanillaHands.Value = v);
            UiaControls.ToggleRow(col, "Clothing panel", UIAConfig.HideVanillaClothing.Value, v => UIAConfig.HideVanillaClothing.Value = v);
            UiaControls.ToggleRow(col, "Status / moodlet panel", UIAConfig.HideVanillaStatus.Value, v => UIAConfig.HideVanillaStatus.Value = v);
            UiaControls.ToggleRow(col, "Instrument cluster (bottom-right)", UIAConfig.HideVanillaPlayerState.Value, v => UIAConfig.HideVanillaPlayerState.Value = v);

            if (!advanced) return;

            UiaControls.Header(col, "Power & glitch (advanced)");
            UiaControls.ToggleRow(col, "Low-power dropouts", HudConfig.LowPowerDropouts.Value, v => HudConfig.LowPowerDropouts.Value = v);
            UiaControls.SliderRow(col, "Low-power threshold (%)", 0f, 40f, HudConfig.LowPowerThreshold.Value, v => HudConfig.LowPowerThreshold.Value = v, "0");
            UiaControls.ToggleRow(col, "Power-transition glitch", HudConfig.GlitchEnabled.Value, v => HudConfig.GlitchEnabled.Value = v);

            var curveAll = new List<string> { "Flat", "Vertex warp (A)", "Dome (B)", "World canvas (C)", "Curved RT (D)" };
            UiaControls.DropdownRow(col, "Curvature mode (full)", curveAll, (int)HudConfig.Curvature.Value, i => HudConfig.Curvature.Value = (HudCurvature)i);

            UiaUi.Go("sp", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Note(col, "Build your own layout and tune the glass effects in the HUD Designer.");
            UiaControls.Button(col, "Open the HUD Designer (F9)", OpenDesigner, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
        }

        private static void OpenDesigner()
        {
            try
            {
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) { UiaControlCenter.Close(); inst.ToggleHudEditor(); }
            }
            catch { }
        }
    }
}
