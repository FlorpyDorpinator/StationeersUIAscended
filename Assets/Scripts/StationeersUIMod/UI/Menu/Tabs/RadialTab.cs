using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Grid;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>Radial-half comfort settings. Simple = the handful that matter; Advanced adds the
    /// full appearance/tuning set and a shortcut into the legacy ImGui firehose (colours live there).</summary>
    public sealed class RadialTab : IUiaTab
    {
        public string Title => "Radial";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            UiaControls.Header(col, "Behaviour");
            var schemas = new List<string> { "New (A)", "The Hub (B)", "Classic (D)" };
            UiaControls.DropdownRow(col, "Control scheme", schemas, (int)UIAConfig.Schema.Value,
                i => UIAConfig.Schema.Value = (ControlSchema)i);
            UiaControls.ToggleRow(col, "Toolbelt wheel", UIAConfig.ToolbeltRadialEnabled.Value, v => UIAConfig.ToolbeltRadialEnabled.Value = v);
            UiaControls.ToggleRow(col, "Tool / device wheel", UIAConfig.ToolRadialEnabled.Value, v => UIAConfig.ToolRadialEnabled.Value = v);
            UiaControls.ToggleRow(col, "Bag / backpack wheel", UIAConfig.BagRadialEnabled.Value, v => UIAConfig.BagRadialEnabled.Value = v);
            UiaControls.ToggleRow(col, "Equipment wheels (1-6)", UIAConfig.EquipmentKeyRadialsEnabled.Value, v => UIAConfig.EquipmentKeyRadialsEnabled.Value = v);
            UiaControls.ToggleRow(col, "Keep moving while a wheel is open", UIAConfig.RadialMovementEnabled.Value, v => UIAConfig.RadialMovementEnabled.Value = v);
            UiaControls.SliderRow(col, "Open hold time", 60f, 600f, UIAConfig.HoldThresholdMs.Value,
                v => UIAConfig.HoldThresholdMs.Value = Mathf.RoundToInt(v), "0", 10f);

            UiaControls.Header(col, "Size & readout");
            UiaControls.SliderRow(col, "Wheel size", 120f, 480f, UIAConfig.RadialOuterRadius.Value, v => UIAConfig.RadialOuterRadius.Value = v, "0", 1f);
            UiaControls.SliderRow(col, "Hub size", 60f, 260f, UIAConfig.RadialInnerRadius.Value, v => UIAConfig.RadialInnerRadius.Value = v, "0", 1f);
            UiaControls.ToggleRow(col, "ALL CAPS labels", UIAConfig.RadialUppercaseLabels.Value, v => UIAConfig.RadialUppercaseLabels.Value = v);
            UiaControls.ToggleRow(col, "Live state under icons (%, kPa, counts)", UIAConfig.RadialShowStateText.Value, v => UIAConfig.RadialShowStateText.Value = v);

            UiaControls.Header(col, "Radial feel");
            UiaControls.ToggleRow(col, "Flick to commit (fast tap + flick picks a wedge)", UIAConfig.RadialFlickCommit.Value, v => UIAConfig.RadialFlickCommit.Value = v);
            UiaControls.SliderRow(col, "Flick window", 80f, 400f, UIAConfig.RadialFlickMs.Value,
                v => UIAConfig.RadialFlickMs.Value = Mathf.RoundToInt(v), "0", 10f);
            UiaControls.ToggleRow(col, "Double-tap repeats last pick", UIAConfig.RadialDoubleTapRepeat.Value, v => UIAConfig.RadialDoubleTapRepeat.Value = v);
            UiaControls.SliderRow(col, "Double-tap window", 120f, 500f, UIAConfig.RadialDoubleTapMs.Value,
                v => UIAConfig.RadialDoubleTapMs.Value = Mathf.RoundToInt(v), "0", 10f);
            UiaControls.ToggleRow(col, "Wedge hover / click sounds", UIAConfig.RadialWedgeSounds.Value, v => UIAConfig.RadialWedgeSounds.Value = v);
            UiaControls.ToggleRow(col, "Fade key-hints as you learn them", UIAConfig.RadialHintFade.Value, v => UIAConfig.RadialHintFade.Value = v);
            UiaControls.Button(col, "Reset hint counters", HintUsageStore.ResetCounters, -1f, UiaTheme.RowH);

            UiaControls.Header(col, "Toolbelt wheel");
            UiaControls.ToggleRow(col, "Remember each tool's home slot", UIAConfig.ToolbeltHomeSlots.Value, v => UIAConfig.ToolbeltHomeSlots.Value = v);
            UiaControls.ToggleRow(col, "Stable layout: reserve empty slots + show binding labels", UIAConfig.ToolbeltStableGeometry.Value, v => UIAConfig.ToolbeltStableGeometry.Value = v);
            UiaControls.ToggleRow(col, "Curve the bound-tool label around the hub",
                UIAConfig.RadialBindingCurved.Value, v => UIAConfig.RadialBindingCurved.Value = v);
            UiaControls.Note(col,
                "The bound-tool name sits at the bottom of its wedge, along the arc where the hub " +
                "starts. Curved bends it letter by letter to follow that arc; off draws it as one " +
                "straight line on the same spot. Its colour is \"TextBindingLabel\" in the palette.");

            UiaControls.Header(col, "Universal Inventory");
            // Layout mode selector removed: the flat pack (Grid) renderer is now the only one.
            UiaControls.SliderRow(col, "Cell size", 28f, 80f, UIAConfig.GridCellSize.Value,
                v => { UIAConfig.GridCellSize.Value = v; TheGridPanel.Relayout(); }, "0", 1f);
            // Live toggles: badges refresh via GridProfileMode.ChromeStamp (folds the config
            // bit), hints are re-read by GridGhostHint.Tick — no extra plumbing needed.
            UiaControls.ToggleRow(col, "Show profile tags on bag tabs",
                UIAConfig.GridProfileBadges.Value, v => UIAConfig.GridProfileBadges.Value = v);
            UiaControls.ToggleRow(col, "Glow the bag Smart Stow would pick while dragging",
                UIAConfig.GridGhostHints.Value, v => UIAConfig.GridGhostHints.Value = v);

            if (!advanced) return;

            UiaControls.Header(col, "Appearance (advanced)");
            UiaControls.SliderRow(col, "Icon size (frac of wedge)", 0.15f, 1.1f, UIAConfig.RadialIconRatio.Value, v => UIAConfig.RadialIconRatio.Value = v, "0.00");
            UiaControls.SliderRow(col, "Border thickness", 0f, 10f, UIAConfig.RadialBorderWidth.Value, v => UIAConfig.RadialBorderWidth.Value = v, "0.0");
            UiaControls.SliderRow(col, "Edge softness (AA)", 0f, 4f, UIAConfig.RadialEdgeFeather.Value, v => UIAConfig.RadialEdgeFeather.Value = v, "0.00");
            UiaControls.SliderRow(col, "Shine", 0f, 2f, UIAConfig.RadialShineIntensity.Value, v => UIAConfig.RadialShineIntensity.Value = v, "0.00");
            UiaControls.SliderRow(col, "Wedge gap (deg)", 0f, 6f, UIAConfig.RadialWedgeGapDeg.Value, v => UIAConfig.RadialWedgeGapDeg.Value = v, "0.0");
            UiaControls.SliderRow(col, "Dim other wedges", 0f, 1f, UIAConfig.RadialDimStrength.Value, v => UIAConfig.RadialDimStrength.Value = v, "0.00");
            UiaControls.SliderRow(col, "Child wheel size", 0.5f, 1.6f, UIAConfig.RadialSatelliteScale.Value, v => UIAConfig.RadialSatelliteScale.Value = v, "0.00");
            UiaControls.SliderRow(col, "Max wedges", 6f, 32f, UIAConfig.RadialMaxWedges.Value, v => UIAConfig.RadialMaxWedges.Value = Mathf.RoundToInt(v), "0", 1f);

            UiaControls.Header(col, "Hub readout text sizes (advanced)");
            UiaControls.SliderRow(col, "1 - Title (bold)", 9f, 32f, UIAConfig.RadialHubTitleSize.Value, v => UIAConfig.RadialHubTitleSize.Value = v, "0");
            UiaControls.SliderRow(col, "2 - Action verb", 8f, 26f, UIAConfig.RadialTextVerb.Value, v => UIAConfig.RadialTextVerb.Value = v, "0");
            UiaControls.SliderRow(col, "3 - Item name", 8f, 26f, UIAConfig.RadialTextLabel.Value, v => UIAConfig.RadialTextLabel.Value = v, "0");
            UiaControls.SliderRow(col, "4 - Detail line", 8f, 24f, UIAConfig.RadialTextSub.Value, v => UIAConfig.RadialTextSub.Value = v, "0");
            UiaControls.SliderRow(col, "5 - Stat / warning", 8f, 24f, UIAConfig.RadialTextWarn.Value, v => UIAConfig.RadialTextWarn.Value = v, "0");

            UiaControls.Header(col, "Bags (advanced)");
            UiaControls.ToggleRow(col, "Group crowded bags by category", UIAConfig.BagGrouping.Value, v => UIAConfig.BagGrouping.Value = v);
            var empty = new List<string> { "Empty slots only", "Stow wedge + empty slots", "Stow wedge only" };
            UiaControls.DropdownRow(col, "Free space shown as", empty, (int)UIAConfig.BagEmptySlots.Value, i => UIAConfig.BagEmptySlots.Value = (EmptySlotMode)i);

            UiaUi.Go("sp", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Note(col, "Wheel colours, glass effects and the full palette live in the legacy panel below.");
            UiaControls.Button(col, "Open all radial settings (legacy F10 panel)", OpenLegacy, -1f, UiaTheme.RowH);
        }

        private static void OpenLegacy()
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
