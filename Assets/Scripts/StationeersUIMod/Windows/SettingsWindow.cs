using BepInEx.Configuration;
using ImGuiNET;
using StationeersUIMod.Features;
using UI.ImGuiUi.ImGuiWindows;
using UnityEngine;
using GameImGuiWindow = UI.ImGuiUi.ImGuiWindows.ImGuiWindow;

namespace StationeersUIMod.Windows
{
    /// <summary>
    /// In-game settings window, registered through the game's own ImGuiWindowManager so we
    /// inherit Escape-to-close, cursor unlock and key capture for free (Phase 0 exit test).
    /// While the radial editor is active, this window IS the editor's control panel.
    /// </summary>
    public sealed class SettingsWindow : GameImGuiWindow
    {
        public SettingsWindow() : base("Stationeers UI Mod " + StationeersUIMod.VersionDisplay, new Vector2(430f, 560f)) { }

        public override void OnOpen() { }
        public override void OnClose() { }

        public override void DrawContent()
        {
            if (RadialEditorMode.Active)
            {
                DrawEditorPanel();
                return;
            }

            ImGui.TextDisabled("Radials, radials, radials - hold the key, flick, release.");
            ImGui.Separator();

            Toggle(UIAConfig.MasterEnable, "Master enable");
            SchemaCombo();
            ImGui.Spacing();

            if (ImGui.Button("Open radial editor"))
                RadialEditorMode.Enter();
            ImGui.SameLine();
            ImGui.TextDisabled("black screen + live example radials");
            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Radials", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(UIAConfig.ToolbeltRadialEnabled, "Toolbelt radial  (hold " + UIAConfig.ToolbeltRadialKey.Value + ")");
                Toggle(UIAConfig.ToolRadialEnabled, "Tool radial  (hold " + UIAConfig.ToolRadialKey.Value + ")");
                Toggle(UIAConfig.BagRadialEnabled, "Bag radial  ("
                    + (UIAConfig.BagRadialTapOpens.Value ? "tap " : "hold ") + UIAConfig.BagRadialKey.Value + ")");
                Toggle(UIAConfig.BagRadialTapOpens, "Tap opens bag radial / hold shows scoreboard");
                Toggle(UIAConfig.EquipmentKeyRadialsEnabled, "Equipment key radials  (tap 1-6)");
                Toggle(UIAConfig.ToolbeltShowStowEntries, "Show empty belt slots in the toolbelt radial");
                IntSlider(UIAConfig.HoldThresholdMs, "Hold threshold (ms)", 60, 600);
                DrawVisualControls();
            }

            if (ImGui.CollapsingHeader("Radial Colours"))
            {
                DrawColourControls();
            }

            if (ImGui.CollapsingHeader("SmartStow+", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(UIAConfig.SmartStowPlusEnabled, "Enable SmartStow+ (G)");
                Toggle(UIAConfig.StowPreferStacks, "1. Prefer existing stacks");
                Toggle(UIAConfig.StowUseProfiles, "2. Use bag profiles");
                Toggle(UIAConfig.StowUseTypeMemory, "3. Use item-type memory");
                Toggle(UIAConfig.StowIntoNestedBags, "Allow nested-bag stow");
                if (ImGui.Button("Open profile editor"))
                    StationeersUIMod.Instance?.ToggleProfileEditor();
                ImGui.SameLine();
                if (ImGui.Button("Reload profiles"))
                    BagProfileStore.LoadProfiles();
            }

            if (ImGui.CollapsingHeader("Visor HUD", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(UIAConfig.HudEnabled, "Enable HUD overlay");
                Toggle(UIAConfig.HudHandBoxes, "Two-hand boxes");
                Toggle(UIAConfig.HudStatusStrip, "Top status strip");
                Toggle(UIAConfig.HudVitals, "Vitals panel");
                Toggle(UIAConfig.HudClock, "Day / time");
                Toggle(UIAConfig.HudContextPanel, "Context panel (look-at)");
                Toggle(UIAConfig.HudVisorArcs, "Visor edge arcs");
                FloatSlider(UIAConfig.HudScale, "HUD scale", 0.6f, 1.6f);
                ImGui.Separator();
                ImGui.TextDisabled("Vanilla panels (hidden, never destroyed):");
                Toggle(UIAConfig.HideVanillaHands, "Hide vanilla hands panel");
                Toggle(UIAConfig.HideVanillaClothing, "Hide vanilla clothing panel");
                Toggle(UIAConfig.HideVanillaStatus, "Hide vanilla status panel");
                Toggle(UIAConfig.HardcoreGating, "Hardcore gating (helmet / sensors)");
            }

            if (ImGui.CollapsingHeader("Slot finder"))
            {
                IntSlider(UIAConfig.ScanDepth, "Scan depth", 1, 5);
                Toggle(UIAConfig.AllowToolSlotSources, "Offer parts from inside other tools");
            }

            ImGui.Separator();
            ImGui.TextDisabled("All settings persist to BepInEx config and are editable in");
            ImGui.TextDisabled("StationeersLaunchPad's mod config panel as well.");
        }

        /// <summary>The window's contents while the radial editor owns the screen.</summary>
        private void DrawEditorPanel()
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.16f, 1f), "RADIAL EDITOR");
            ImGui.TextDisabled("Live example radials on the left. Hover wedges to preview");
            ImGui.TextDisabled("states; scroll over TEMPERATURE to test the wheel.");
            ImGui.Spacing();
            if (ImGui.Button("Exit editor  (or press Escape)"))
                RadialEditorMode.Exit();
            ImGui.Separator();

            if (ImGui.CollapsingHeader("Layout & sizes", ImGuiTreeNodeFlags.DefaultOpen))
                DrawVisualControls();

            if (ImGui.CollapsingHeader("Colours", ImGuiTreeNodeFlags.DefaultOpen))
                DrawColourControls();
        }

        /// <summary>Every live-tunable visual: shared between the normal Radials section and
        /// the editor panel so the two can never drift apart.</summary>
        private static void DrawVisualControls()
        {
            FloatSlider(UIAConfig.RadialOuterRadius, "Radial size", 120f, 480f);
            FloatSlider(UIAConfig.RadialInnerRadius, "Hub (center circle) size", 60f, 260f);
            FloatSlider(UIAConfig.RadialSatelliteScale, "Child radial size", 0.5f, 1.6f);
            FloatSlider(UIAConfig.RadialIconScale, "Icon size", 0.5f, 2.5f);
            FloatSlider(UIAConfig.RadialBorderWidth, "Border thickness (px)", 0f, 10f);
            Toggle(UIAConfig.RadialSideBorders, "Borders on wedge SIDE edges (full outline)");
            FloatSlider(UIAConfig.RadialWedgeGapDeg, "Gap between wedges (deg)", 0f, 6f);
            FloatSlider(UIAConfig.RadialEdgeFeather, "Edge softness / anti-aliasing (px)", 0f, 4f);
            Toggle(UIAConfig.RadialDimShading, "Dim other wedges while one is highlighted");
            FloatSlider(UIAConfig.RadialDimStrength, "Dim strength", 0f, 1f);
            Toggle(UIAConfig.RadialUppercaseLabels, "ALL CAPS wedge labels");
            Toggle(UIAConfig.RadialShowStateText, "State under icons (battery %, kPa, counts)");
            Toggle(UIAConfig.RadialShowWedgeLabels, "Show item name under each icon");
            FontCombo();
            Toggle(UIAConfig.UseUnityRadial, "Unity UGUI renderer (procedural wedges, TMP, animations)");
        }

        private static void DrawColourControls()
        {
            ImGui.TextDisabled("Click a swatch for a colour wheel. The A slider is transparency.");
            ImGui.TextDisabled("Changes apply live and persist to the config file.");
            ImGui.Spacing();

            foreach (var entry in Overlay.RadialPalette.All)
                ColorWheel(entry);

            ImGui.Spacing();
            if (ImGui.Button("Reset all colours to defaults"))
                Overlay.RadialPalette.ResetToDefaults();
        }

        private static void SchemaCombo()
        {
            var schema = UIAConfig.Schema.Value;
            string current = schema == ControlSchema.OptionA ? "Option A (new)" : "Option D (classic)";
            if (ImGui.BeginCombo("Control schema", current))
            {
                if (ImGui.Selectable("Option A (new)", schema == ControlSchema.OptionA))
                    UIAConfig.Schema.Value = ControlSchema.OptionA;
                if (ImGui.Selectable("Option D (classic)", schema == ControlSchema.OptionD))
                    UIAConfig.Schema.Value = ControlSchema.OptionD;
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(UIAConfig.IsA
                ? "A: STOW wedges, device satellites, search panel, drag-out parking, auto-close."
                : "D: the classic pre-overhaul behavior (swap lists, click executes immediately).");
        }

        private static void FontCombo()
        {
            string current = UIAConfig.RadialFontName.Value;
            if (string.IsNullOrEmpty(current)) current = "(auto)";
            if (ImGui.BeginCombo("Radial font", current))
            {
                if (ImGui.Selectable("(auto: prefer bold)", string.IsNullOrEmpty(UIAConfig.RadialFontName.Value)))
                    UIAConfig.RadialFontName.Value = "";
                foreach (var name in UI.UnityRadialView.AllFontNames())
                {
                    if (ImGui.Selectable(name, name == UIAConfig.RadialFontName.Value))
                        UIAConfig.RadialFontName.Value = name;
                }
                ImGui.EndCombo();
            }
        }

        /// <summary>
        /// A swatch that opens ImGui's hue-wheel picker with an alpha bar. Colours round-trip
        /// through the config as hex, so they persist and survive a hot reload.
        /// </summary>
        private static void ColorWheel(Overlay.RadialPalette.Entry entry)
        {
            Color c = entry.Value;
            var v = new Vector4(c.r, c.g, c.b, c.a);
            const ImGuiColorEditFlags flags = ImGuiColorEditFlags.AlphaBar
                                            | ImGuiColorEditFlags.AlphaPreviewHalf
                                            | ImGuiColorEditFlags.PickerHueWheel;
            if (ImGui.ColorEdit4(entry.Name, ref v, flags))
                entry.Value = new Color(v.x, v.y, v.z, v.w);
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            bool v = entry.Value;
            if (ImGui.Checkbox(label, ref v)) entry.Value = v;
        }

        private static void IntSlider(ConfigEntry<int> entry, string label, int min, int max)
        {
            int v = entry.Value;
            if (ImGui.SliderInt(label, ref v, min, max)) entry.Value = v;
        }

        private static void FloatSlider(ConfigEntry<float> entry, string label, float min, float max)
        {
            float v = entry.Value;
            if (ImGui.SliderFloat(label, ref v, min, max)) entry.Value = v;
        }
    }
}
