using System.Collections.Generic;
using StationeersUIMod.Overlay;
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

        // Two-click confirm for the destructive "Restore shipped themes" button below — an
        // INSTANCE field (not static) so it dies with this tab object rather than needing its own
        // Shutdown reset; HudTab is rebuilt fresh whenever the Control Center's tab list is.
        private float _restoreShippedArmUntil;

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

            // The quick two-way switch. Its option text matches the F9 editor's wording so the
            // same mode is never called two different things in two windows.
            var curves = new List<string> { "Flat - no curvature", "Curved (visor)" };
            int ci = HudConfig.Curvature.Value == HudCurvature.Flat ? 0 : 1;
            UiaControls.DropdownRow(col, "Curvature", curves, ci,
                i => HudConfig.Curvature.Value = i == 0 ? HudCurvature.Flat : HudCurvature.VertexWarp);
            UiaControls.SliderRow(col, "Curve strength", 0f, 1f, HudConfig.CurveStrength.Value, v => HudConfig.CurveStrength.Value = v, "0.00");

            UiaControls.Header(col, "Show");
            UiaControls.ToggleRow(col, "Visor-edge vignette", HudConfig.ShowVignette.Value, v => HudConfig.ShowVignette.Value = v);
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
            UiaControls.ToggleRow(col, "Power-transition glitch", HudConfig.FxGlitchOn.Value, v => HudConfig.FxGlitchOn.Value = v);

            // Every curvature mode, in HudCurvature order (the index IS the enum value). No mode
            // is labelled "experimental" any more — they are simply the modes, named the way the
            // F9 editor names them; mode C's known swim is documented there, not in a scare word.
            var curveAll = new List<string>
            {
                "Flat - no curvature",
                "A - Vertex warp (recommended)",
                "B - Dome projection",
                "C - Curved world canvas",
                "D - Curved, steady",
            };
            UiaControls.DropdownRow(col, "Curvature mode (all)", curveAll, (int)HudConfig.Curvature.Value, i => HudConfig.Curvature.Value = (HudCurvature)i);

            UiaUi.Go("sp", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Note(col, "Build your own layout and tune the glass effects in the HUD Designer.");
            UiaControls.Button(col, "Open the HUD Designer (F9)", OpenDesigner, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);

            // ---- Maintenance: the one home for the shipped-theme reset affordance (audit 06 P1 +
            // the playtester incident — "my shipped theme is a mess and I don't know how to fix it").
            UiaUi.Go("sp2", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Header(col, "Maintenance (advanced)");
            // Ask the STORE, not StationeersUIMod.ModDirectory: a mod folder that exists but has no
            // HudProfiles subfolder is just as unrestorable as no mod folder at all, and only the
            // store knows the shape it needs. (The store refuses the wipe in that case too.)
            bool devOffline = !Features.HudProfileStore.ShippedFolderAvailable;
            UiaControls.Note(col, devOffline
                ? "Restore shipped themes needs the mod's installed folder, which isn't available right now (the F6 dev flow has none) - the button below will just explain that if you click it."
                : "Puts Stationeers Blue and Pure HUD back exactly as shipped, undoing any edits you made to either. Your own profiles are never touched. Click twice to confirm.");
            UiaControls.Button(col, "Restore shipped themes", RestoreShippedThemes, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
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

        /// <summary>The F10 half of the shipped-theme reset affordance (the console has its own
        /// general-purpose `uiareset` nuke — see Core/FinderCommands.cs — this is the scoped,
        /// discoverable, in-menu version that touches ONLY the two shipped themes). Two clicks: the
        /// first arms a 5-second confirm window and toasts what is about to happen; the second,
        /// within that window, actually wipes the shipped set and immediately re-seeds it fresh from
        /// the mod folder (<see cref="Features.HudProfileStore.ResetShippedProfiles"/>). Reloads the
        /// live document afterward if the active profile was one of the wiped names, so the change is
        /// visible immediately rather than after a restart.</summary>
        private void RestoreShippedThemes()
        {
            string modDir = global::StationeersUIMod.StationeersUIMod.ModDirectory;
            if (!Features.HudProfileStore.ShippedFolderAvailable)
            {
                Toast.Show("Restore shipped themes needs the mod's installed folder - unavailable under the F6 dev flow.", Theme.Critical, 3.5f);
                return;
            }

            float now = Time.unscaledTime;
            if (now > _restoreShippedArmUntil)
            {
                _restoreShippedArmUntil = now + 5f;
                Toast.Show("Click 'Restore shipped themes' again within 5s to confirm - this reverts any edits to Stationeers Blue / Pure HUD.", Theme.Critical, 5f);
                return;
            }
            _restoreShippedArmUntil = 0f;

            try
            {
                string activeBefore = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
                Features.HudProfileStore.ResetShippedProfiles(modDir);
                string activeAfter = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
                if (!string.IsNullOrEmpty(activeAfter)
                    && !string.Equals(activeBefore, activeAfter, System.StringComparison.OrdinalIgnoreCase))
                {
                    var keep = Features.HudProfileStore.Active;
                    Features.HudProfileStore.LoadActive(activeAfter, () => keep != null ? keep.Clone() : new HudDocument { Name = activeAfter });
                    HudDocumentHistory.Clear();
                }
                Toast.Show("Shipped themes restored.", Theme.TextPrimary, 3f);
                UiaControlCenter.Refresh();
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("Restore shipped themes failed: " + e.Message);
                Toast.Show("Restore shipped themes failed - see the log.", Theme.Critical, 3.5f);
            }
        }
    }
}
