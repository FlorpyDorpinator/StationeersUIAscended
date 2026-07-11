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
                ImGui.Separator();
                ImGui.TextDisabled("Option A bag presentation (playtest options):");
                Toggle(UIAConfig.BagGrouping, "Group crowded bags by sorting class");
                EmptySlotCombo();
                IntSlider(UIAConfig.RadialMaxWedges, "Max wedges per radial (overflow -> MORE)", 6, 32);
                ImGui.Separator();
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
                Toggle(UI.Hud.HudConfig.VisorHudEnabled, "Visor HUD (UGUI: curved bar, compass, vitals)");
                if (ImGui.Button("Open HUD editor  (" + UI.Hud.HudConfig.HudEditorKey.Value + ")"))
                    StationeersUIMod.Instance?.ToggleHudEditor();
                ImGui.SameLine();
                ImGui.TextDisabled("click HUD elements to edit them");
                ImGui.Separator();
                ImGui.TextDisabled("Legacy ImGui overlay (0.1.0 fallback):");
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
            FloatSlider(UIAConfig.RadialIconRatio, "Icon size (fraction of wedge)", 0.15f, 1.1f);
            FloatSlider(UIAConfig.RadialBorderWidth, "Border thickness (px)", 0f, 10f);
            Toggle(UIAConfig.RadialSideBorders, "Borders on wedge SIDE edges (full outline)");
            FloatSlider(UIAConfig.RadialSideWidthInner, "Side line width at hub (px)", 0.5f, 12f);
            FloatSlider(UIAConfig.RadialSideWidthOuter, "Side line width at rim (px)", 0.5f, 12f);
            FloatSlider(UIAConfig.RadialWedgeGapDeg, "Gap between wedges (deg)", 0f, 6f);
            FloatSlider(UIAConfig.RadialEdgeFeather, "Edge softness / anti-aliasing (px)", 0f, 4f);
            FloatSlider(UIAConfig.RadialShineIntensity, "Shine intensity (0 = flat)", 0f, 2f);
            Toggle(UIAConfig.RadialDimShading, "Dim other wedges while one is highlighted");
            FloatSlider(UIAConfig.RadialDimStrength, "Dim strength", 0f, 1f);
            FloatSlider(UIAConfig.ParkedChipRadius, "Dragged-out item bubble size (px)", 16f, 80f);
            Toggle(UIAConfig.RadialUppercaseLabels, "ALL CAPS wedge labels");
            Toggle(UIAConfig.RadialShowStateText, "State under icons (battery %, kPa, counts)");
            Toggle(UIAConfig.RadialShowWedgeLabels, "Show item name under each icon");
            FontCombo();
            Toggle(UIAConfig.UseUnityRadial, "Unity UGUI renderer (procedural wedges, TMP, animations)");
        }

        private static string _lastHotSig = "";
        private static System.Collections.Generic.Dictionary<string, string> _frameSnapshot;
        private static System.Collections.Generic.Dictionary<string, string> _pendingUndo;

        private static void DrawColourControls()
        {
            ImGui.TextDisabled("Click a swatch for a colour wheel. The A slider is transparency.");
            if (RadialEditorMode.Active)
                ImGui.TextDisabled("Hover an element in the preview - its colours light up ORANGE here.");

            if (ImGui.Button("Undo"))
                Overlay.RadialPalette.History.Undo();
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
                Overlay.RadialPalette.History.Redo();
            ImGui.SameLine();
            ImGui.TextDisabled(Overlay.RadialPalette.History.CanUndo ? "" : "(nothing to undo)");
            ImGui.Spacing();

            // Pre-widget state this frame: becomes the undo step if an edit starts below.
            _frameSnapshot = Overlay.RadialPalette.Snapshot();
            string hotSig = RadialEditorMode.Active ? RadialEditorMode.HotSignature : "";
            bool scrollTo = hotSig != _lastHotSig && hotSig.Length > 0;
            _lastHotSig = hotSig;

            foreach (var entry in Overlay.RadialPalette.All)
            {
                bool hot = RadialEditorMode.Active && RadialEditorMode.HotPalette.Contains(entry.Name);
                if (hot)
                {
                    if (scrollTo) { ImGui.SetScrollHereY(0.3f); scrollTo = false; }
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.16f, 1f));
                }
                ColorWheel(entry);
                if (hot) ImGui.PopStyleColor();
            }

            ImGui.Spacing();
            if (ImGui.Button("Reset all colours to defaults"))
                Overlay.RadialPalette.ResetToDefaults(); // pushes its own undo step
        }

        private static void SchemaCombo()
        {
            var schema = UIAConfig.Schema.Value;
            string current = schema == ControlSchema.OptionA ? "Option A (new)"
                : schema == ControlSchema.OptionB ? "Option B (The Hub)"
                : "Option D (classic)";
            if (ImGui.BeginCombo("Control schema", current))
            {
                if (ImGui.Selectable("Option A (new)", schema == ControlSchema.OptionA))
                    UIAConfig.Schema.Value = ControlSchema.OptionA;
                if (ImGui.Selectable("Option B (The Hub)", schema == ControlSchema.OptionB))
                    UIAConfig.Schema.Value = ControlSchema.OptionB;
                if (ImGui.Selectable("Option D (classic)", schema == ControlSchema.OptionD))
                    UIAConfig.Schema.Value = ControlSchema.OptionD;
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(UIAConfig.IsB
                ? "B: everything A does + The Hub on the toolbelt radial. Tap MMB = sticky\n" +
                  "(tap a wedge to select), hold MMB = transient (LMB dives, release runs)."
                : UIAConfig.IsA
                ? "A: STOW wedges, device satellites, search panel, drag-out parking, auto-close."
                : "D: the classic pre-overhaul behavior (swap lists, click executes immediately).");
        }

        private static void EmptySlotCombo()
        {
            var mode = UIAConfig.BagEmptySlots.Value;
            string current = mode == EmptySlotMode.EmptySlots ? "Empty slots (no stow wedge)"
                : mode == EmptySlotMode.StowAndEmptySlots ? "Stow wedge + empty slots"
                : "Stow wedge only";
            if (ImGui.BeginCombo("Free space in bags", current))
            {
                if (ImGui.Selectable("Empty slots (no stow wedge)", mode == EmptySlotMode.EmptySlots))
                    UIAConfig.BagEmptySlots.Value = EmptySlotMode.EmptySlots;
                if (ImGui.Selectable("Stow wedge + empty slots", mode == EmptySlotMode.StowAndEmptySlots))
                    UIAConfig.BagEmptySlots.Value = EmptySlotMode.StowAndEmptySlots;
                if (ImGui.Selectable("Stow wedge only", mode == EmptySlotMode.StowOnly))
                    UIAConfig.BagEmptySlots.Value = EmptySlotMode.StowOnly;
                ImGui.EndCombo();
            }
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
            // One drag = one undo step: capture the pre-edit state when the widget is
            // picked up, commit it when the edit finishes.
            if (ImGui.IsItemActivated())
                _pendingUndo = _frameSnapshot;
            if (ImGui.IsItemDeactivatedAfterEdit() && _pendingUndo != null)
            {
                Overlay.RadialPalette.History.PushUndo(_pendingUndo);
                _pendingUndo = null;
            }
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
