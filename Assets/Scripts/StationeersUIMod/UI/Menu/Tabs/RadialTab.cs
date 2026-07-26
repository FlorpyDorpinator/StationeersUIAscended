using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>Radial-half comfort settings. Simple = the handful that matter; Advanced adds the
    /// full appearance/tuning set and a shortcut into the radial editor (the ImGui window that
    /// owns wheel colours and the rest of the wedge look).</summary>
    public sealed class RadialTab : IUiaTab
    {
        public string Title => "Radial";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            // the post-0.9.2.5 play-test round: the "Control scheme" chooser and the four per-wheel enable toggles are gone.
            // The Hub is THE schema, and with the radial half switched on (F10 > General) the
            // toolbelt / tool / bag / equipment wheels ARE the mod — not options to switch off.
            UiaControls.Header(col, "Behaviour");
            UiaControls.ToggleRow(col, "Keep moving while a wheel is open", UIAConfig.RadialMovementEnabled.Value, v => UIAConfig.RadialMovementEnabled.Value = v);
            UiaControls.SliderRow(col, "Open hold time", 60f, 600f, UIAConfig.HoldThresholdMs.Value,
                v => UIAConfig.HoldThresholdMs.Value = Mathf.RoundToInt(v), "0", 10f);

            UiaControls.Header(col, "Size & readout");
            // These are all part of the radial theme's LOOK ("radial:" family, HudTheme) — each
            // setter marks the active profile's theme dirty so the value travels with the profile
            // and switching profiles restores it, the same contract HUD colours already have.
            UiaControls.SliderRow(col, "Wheel size", 120f, 480f, UIAConfig.RadialOuterRadius.Value, v => { UIAConfig.RadialOuterRadius.Value = v; HudProfileStore.MarkThemeChanged(); }, "0", 1f);
            UiaControls.SliderRow(col, "Hub size", 60f, 260f, UIAConfig.RadialInnerRadius.Value, v => { UIAConfig.RadialInnerRadius.Value = v; HudProfileStore.MarkThemeChanged(); }, "0", 1f);
            UiaControls.ToggleRow(col, "ALL CAPS labels", UIAConfig.RadialUppercaseLabels.Value, v => { UIAConfig.RadialUppercaseLabels.Value = v; HudProfileStore.MarkThemeChanged(); });
            UiaControls.ToggleRow(col, "Live state under icons (%, kPa, counts)", UIAConfig.RadialShowStateText.Value, v => { UIAConfig.RadialShowStateText.Value = v; HudProfileStore.MarkThemeChanged(); });

            // "Flick to commit" + its window died in the post-0.9.2.5 play-test round — a second, racier route into
            // "commit a wedge" that intermittently ate MMB. Hold-open, point, release IS that
            // gesture already, and that path is untouched.
            UiaControls.Header(col, "Radial feel");
            UiaControls.ToggleRow(col, "Double-tap repeats last pick", UIAConfig.RadialDoubleTapRepeat.Value, v => UIAConfig.RadialDoubleTapRepeat.Value = v);
            UiaControls.SliderRow(col, "Double-tap window", 120f, 500f, UIAConfig.RadialDoubleTapMs.Value,
                v => UIAConfig.RadialDoubleTapMs.Value = Mathf.RoundToInt(v), "0", 10f);
            UiaControls.ToggleRow(col, "Wedge hover / click sounds", UIAConfig.RadialWedgeSounds.Value, v => UIAConfig.RadialWedgeSounds.Value = v);
            UiaControls.ToggleRow(col, "Fade key-hints as you learn them", UIAConfig.RadialHintFade.Value, v => UIAConfig.RadialHintFade.Value = v);
            UiaControls.Button(col, "Reset hint counters", HintUsageStore.ResetCounters, -1f, UiaTheme.RowH);

            UiaControls.Header(col, "Toolbelt wheel");
            UiaControls.ToggleRow(col, "Remember each tool's home slot", UIAConfig.ToolbeltHomeSlots.Value, v => UIAConfig.ToolbeltHomeSlots.Value = v);
            UiaControls.ToggleRow(col, "Stable layout: reserve empty slots + show binding labels", UIAConfig.ToolbeltStableGeometry.Value, v => UIAConfig.ToolbeltStableGeometry.Value = v);
            // the post-0.9.2.5 play-test round: the curved/straight choice is gone (curved is the only rendering — it was
            // always the intended look). What replaced it is an on/off for the labels themselves.
            UiaControls.ToggleRow(col, "Show bound-tool labels",
                UIAConfig.RadialShowBindingLabels.Value, v => { UIAConfig.RadialShowBindingLabels.Value = v; HudProfileStore.MarkThemeChanged(); });
            UiaControls.Note(col,
                "The bound-tool name is the dim grey ghost on a reserved (empty-but-bound) belt " +
                "slot - it sits at the bottom of its wedge, bent around the arc where the hub " +
                "starts. Off hides it entirely. Its colour is \"TextBindingLabel\" in the palette.");

            // The Universal Inventory group moved to F10 > Storage in the post-0.9.2.5 play-test round (FlorpyDorp: "it has
            // nothing to do with the radials").

            if (!advanced) return;

            UiaControls.Header(col, "Appearance (advanced)");
            UiaControls.SliderRow(col, "Icon size (frac of wedge)", 0.15f, 1.1f, UIAConfig.RadialIconRatio.Value, v => { UIAConfig.RadialIconRatio.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            UiaControls.SliderRow(col, "Border thickness", 0f, 10f, UIAConfig.RadialBorderWidth.Value, v => { UIAConfig.RadialBorderWidth.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.0");
            UiaControls.SliderRow(col, "Edge softness (AA)", 0f, 4f, UIAConfig.RadialEdgeFeather.Value, v => { UIAConfig.RadialEdgeFeather.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            UiaControls.SliderRow(col, "Shine", 0f, 2f, UIAConfig.RadialShineIntensity.Value, v => { UIAConfig.RadialShineIntensity.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            UiaControls.SliderRow(col, "Wedge gap (deg)", 0f, 6f, UIAConfig.RadialWedgeGapDeg.Value, v => { UIAConfig.RadialWedgeGapDeg.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.0");
            UiaControls.SliderRow(col, "Dim other wedges", 0f, 1f, UIAConfig.RadialDimStrength.Value, v => { UIAConfig.RadialDimStrength.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            UiaControls.SliderRow(col, "Child wheel size", 0.5f, 1.6f, UIAConfig.RadialSatelliteScale.Value, v => { UIAConfig.RadialSatelliteScale.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            // Max wedges is BEHAVIOUR (overflow -> MORE), not look — deliberately excluded from
            // the "radial:" theme family (design §5.2), so it does NOT mark the theme dirty.
            UiaControls.SliderRow(col, "Max wedges", 6f, 32f, UIAConfig.RadialMaxWedges.Value, v => UIAConfig.RadialMaxWedges.Value = Mathf.RoundToInt(v), "0", 1f);

            UiaControls.Header(col, "Hub readout text sizes (advanced)");
            UiaControls.SliderRow(col, "1 - Title (bold)", 9f, 32f, UIAConfig.RadialHubTitleSize.Value, v => { UIAConfig.RadialHubTitleSize.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            UiaControls.SliderRow(col, "2 - Action verb", 8f, 26f, UIAConfig.RadialTextVerb.Value, v => { UIAConfig.RadialTextVerb.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            UiaControls.SliderRow(col, "3 - Item name", 8f, 26f, UIAConfig.RadialTextLabel.Value, v => { UIAConfig.RadialTextLabel.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            UiaControls.SliderRow(col, "4 - Detail line", 8f, 24f, UIAConfig.RadialTextSub.Value, v => { UIAConfig.RadialTextSub.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            UiaControls.SliderRow(col, "5 - Stat / warning", 8f, 24f, UIAConfig.RadialTextWarn.Value, v => { UIAConfig.RadialTextWarn.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");

            UiaControls.Header(col, "Bag wheels (advanced)");
            UiaControls.ToggleRow(col, "Group crowded bags by category", UIAConfig.BagGrouping.Value, v => UIAConfig.BagGrouping.Value = v);
            var empty = new List<string> { "Empty slots only", "Stow wedge + empty slots", "Stow wedge only" };
            UiaControls.DropdownRow(col, "Free space shown as", empty, (int)UIAConfig.BagEmptySlots.Value, i => UIAConfig.BagEmptySlots.Value = (EmptySlotMode)i);

            UiaUi.Go("sp", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            // "Legacy" was a lie: this IS the radial editor — the only place wheel colours, the
            // wedge glass and the radial palette are edited (0.9.2.5 Wave D wording pass).
            UiaControls.Note(col, "Wheel colours, glass effects and the full palette live in the radial editor.");
            UiaControls.Button(col, "Open the radial editor", OpenRadialEditor, -1f, UiaTheme.RowH);
        }

        private static void OpenRadialEditor()
        {
            try
            {
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) { UiaControlCenter.Close(); inst.ToggleSettingsWindow(); }
            }
            catch { }
        }
    }
}
