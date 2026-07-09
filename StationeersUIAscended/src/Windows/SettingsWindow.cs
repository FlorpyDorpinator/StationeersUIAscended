using BepInEx.Configuration;
using ImGuiNET;
using StationeersUIAscended.Features;
using UI.ImGuiUi.ImGuiWindows;
using UnityEngine;
using GameImGuiWindow = UI.ImGuiUi.ImGuiWindows.ImGuiWindow;

namespace StationeersUIAscended.Windows
{
    /// <summary>
    /// In-game settings window, registered through the game's own ImGuiWindowManager so we
    /// inherit Escape-to-close, cursor unlock and key capture for free (Phase 0 exit test).
    /// </summary>
    public sealed class SettingsWindow : GameImGuiWindow
    {
        public SettingsWindow() : base("Stationeers UI Ascended " + UIAscendedPlugin.VersionDisplay, new Vector2(430f, 560f)) { }

        public override void OnOpen() { }
        public override void OnClose() { }

        public override void DrawContent()
        {
            ImGui.TextDisabled("Radials, radials, radials — hold the key, flick, release.");
            ImGui.Separator();

            Toggle(UIAConfig.MasterEnable, "Master enable");
            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Radials", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(UIAConfig.ToolbeltRadialEnabled, "Toolbelt radial  (hold " + UIAConfig.ToolbeltRadialKey.Value + ")");
                Toggle(UIAConfig.ToolRadialEnabled, "Tool radial  (hold " + UIAConfig.ToolRadialKey.Value + ")");
                Toggle(UIAConfig.BagRadialEnabled, "Bag radial  (hold " + UIAConfig.BagRadialKey.Value + ")");
                Toggle(UIAConfig.EquipmentKeyRadialsEnabled, "Equipment key radials  (tap 1-6)");
                Toggle(UIAConfig.ToolbeltShowStowEntries, "Show empty belt slots in the toolbelt radial");
                IntSlider(UIAConfig.HoldThresholdMs, "Hold threshold (ms)", 60, 600);
                FloatSlider(UIAConfig.RadialOuterRadius, "Radial size", 120f, 480f);
            }

            if (ImGui.CollapsingHeader("SmartStow+", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(UIAConfig.SmartStowPlusEnabled, "Enable SmartStow+ (G)");
                Toggle(UIAConfig.StowPreferStacks, "1. Prefer existing stacks");
                Toggle(UIAConfig.StowUseProfiles, "2. Use bag profiles");
                Toggle(UIAConfig.StowUseTypeMemory, "3. Use item-type memory");
                Toggle(UIAConfig.StowIntoNestedBags, "Allow nested-bag stow");
                if (ImGui.Button("Open profile editor"))
                    UIAscendedPlugin.Instance?.ToggleProfileEditor();
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
