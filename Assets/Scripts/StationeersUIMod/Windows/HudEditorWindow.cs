using System.Collections.Generic;
using BepInEx.Configuration;
using ImGuiNET;
using StationeersUIMod.UI.Hud;
using UI.ImGuiUi.ImGuiWindows;
using UnityEngine;
using GameImGuiWindow = UI.ImGuiUi.ImGuiWindows.ImGuiWindow;

namespace StationeersUIMod.Windows
{
    /// <summary>
    /// The F9 HUD editor window — the visor HUD's counterpart to the F10 radial editor.
    /// Registered through the game's ImGuiWindowManager (Escape-to-close, cursor unlock
    /// for free). Every HUD colour, font, size and behavior knob lives here; hovering a
    /// HUD element highlights its colours ORANGE, and clicking one opens a floating edit
    /// popup next to it (drawn by <see cref="DrawPopupOverlay"/> from the ImGui hook).
    /// </summary>
    public sealed class HudEditorWindow : GameImGuiWindow
    {
        public HudEditorWindow() : base("HUD Editor - UI Ascended " + StationeersUIMod.VersionDisplay,
            new Vector2(470f, 640f)) { }

        public override void OnOpen() { }
        public override void OnClose()
        {
            FlushPendingElementEdit();
            FlushPendingPaletteEdit();
            _activeEditorTab = null;
        }

        private static string _lastHotSig = "";
        private static Dictionary<string, string> _frameSnapshot;
        private static Dictionary<string, string> _pendingUndo;
        private static string _activeEditorTab;
        // Bloom-tint swatch cache: config stores a #RRGGBB string, the wheel wants a vector.
        private static string _bloomTintStr;
        private static Vector3 _bloomTintVec = Vector3.one;
        private static string _bloom2TintStr;
        private static Vector3 _bloom2TintVec = Vector3.one;
        private static string _edgeTintStr;
        private static Vector3 _edgeTintVec = Vector3.one;
        private static string _frostTintStr;
        private static Vector3 _frostTintVec = Vector3.one;

        /// <summary>Keep the CURRENT ImGui window on screen: a window resized/dragged past the
        /// bottom edge became unreachable (play-test) — clamp size to the screen and keep the
        /// title bar grabbable. Call first thing inside DrawContent (we're inside Begin here,
        /// so SetWindowSize/Pos with no name act on the current window).</summary>
        internal static void ClampWindowToScreen()
        {
            var sz = ImGui.GetWindowSize();
            var pos = ImGui.GetWindowPos();
            float maxW = Screen.width * 0.95f, maxH = Screen.height * 0.92f;
            if (sz.x > maxW || sz.y > maxH)
            {
                sz = new Vector2(Mathf.Min(sz.x, maxW), Mathf.Min(sz.y, maxH));
                ImGui.SetWindowSize(sz);
            }
            float maxX = Mathf.Max(0f, Screen.width - sz.x);
            float maxY = Mathf.Max(0f, Screen.height - 40f); // keep at least the title bar reachable
            if (pos.x < 0f || pos.y < 0f || pos.x > maxX || pos.y > maxY)
                ImGui.SetWindowPos(new Vector2(Mathf.Clamp(pos.x, 0f, maxX), Mathf.Clamp(pos.y, 0f, maxY)));
        }

        public override void DrawContent()
        {
            ClampWindowToScreen();
            DrawEditorToolbar();

            if (!ImGui.BeginTabBar("##UIAHudEditorTabs")) return;
            if (ImGui.BeginTabItem("Build"))
            {
                ActivateEditorTab("Build");
                ImGui.BeginChild("##UIAHudBuildTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawBuildTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Theme"))
            {
                ActivateEditorTab("Theme");
                ImGui.BeginChild("##UIAHudThemeTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawThemeTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Effects"))
            {
                ActivateEditorTab("Effects");
                ImGui.BeginChild("##UIAHudEffectsTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawEffectsTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("View & Behavior"))
            {
                ActivateEditorTab("View & Behavior");
                ImGui.BeginChild("##UIAHudViewTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawViewBehaviorTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Diagnostics"))
            {
                ActivateEditorTab("Diagnostics");
                ImGui.BeginChild("##UIAHudDiagnosticsTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawDiagnosticsTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        /// <summary>A colour picker can disappear without an ImGui deactivation event when its
        /// top-level tab is changed. Commit that gesture before drawing the newly active tab so a
        /// later picker cannot overwrite its pre-edit snapshot.</summary>
        private static void ActivateEditorTab(string tab)
        {
            if (string.Equals(_activeEditorTab, tab, System.StringComparison.Ordinal)) return;
            FlushPendingPaletteEdit();
            _activeEditorTab = tab;
        }

        private void DrawEditorToolbar()
        {
            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "VISOR HUD DESIGNER");
            ImGui.SameLine();
            if (Features.HudProfileStore.HasPendingSave)
                ImGui.TextColored(new Vector4(1f, 0.72f, 0.25f, 1f), "PROFILE - Saving...");
            else
                ImGui.TextDisabled("PROFILE - Saved");

            DrawProfilesSection();

            ImGui.TextDisabled("Preview:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(175f);
            PreviewTierCombo();
            ImGui.SameLine();
            if (ImGui.Button("Undo##toolbar") && UI.Hud.HudDocumentHistory.CanUndo)
            {
                FlushPendingElementEdit();
                HudEditorMode.DoUndo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Redo##toolbar") && UI.Hud.HudDocumentHistory.CanRedo)
            {
                FlushPendingElementEdit();
                HudEditorMode.DoRedo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Exit##toolbar"))
                StationeersUIMod.Instance?.ToggleHudEditor();
            if (UI.Hud.HudElementView.EditBareTier)
                ImGui.TextDisabled("Editing BARE layout — moves write to bare-mode positions.");
            ImGui.Separator();
        }

        private void DrawBuildTab()
        {
            ImGui.TextDisabled("PROFILE — layout and element changes are saved to the active HUD profile.");
            if (!HudSystem.DocumentMode)
            {
                ImGui.TextColored(new Vector4(1f, 0.72f, 0.25f, 1f),
                    "The document HUD is off. Enable it under View & Behavior to use the designer.");
                return;
            }
            DrawDesignerSection();
        }

        private void DrawThemeTab()
        {
            ImGui.TextDisabled("GLOBAL — these defaults and palette colours apply across HUD profiles.");

            if (ImGui.CollapsingHeader("Panel surface", ImGuiTreeNodeFlags.DefaultOpen))
            {
                FloatSlider(HudConfig.HudScale, "Overall HUD scale", 0.6f, 1.6f);
                FloatSlider(HudConfig.CornerRadius, "Default corner rounding (px)", 0f, 28f);
                FloatSlider(HudConfig.BorderWidth, "Default line thickness (px)", 0f, 6f);
                FloatSlider(HudConfig.EdgeFeather, "Edge softness / AA (px)", 0f, 4f);
                FloatSlider(HudConfig.GlassSheen, "Default glass sheen", 0f, 1f);
                FloatSlider(HudConfig.GlassEdge, "Default glass edge light", 0f, 1f);
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "ANALYTIC SDF PANELS");
                Toggle(HudConfig.SdfPanels, "Use analytic SDF glass panels");
                if (HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value)
                {
                    FloatSlider(HudConfig.SdfSquircle, "  corner shape (2 round - 8 squircle)", 2f, 8f);
                    Toggle(HudConfig.SdfGaussianHalo, "  Gaussian distance falloff (not a blur convolution)");
                    if (!Core.HudShaderStore.SdfAvailable)
                        ImGui.TextColored(new Vector4(1f, 0.72f, 0.25f, 1f),
                            "  Shader bundle unavailable — panels fail soft to the mesh renderer.");
                }
                ImGui.Spacing();
                if (HudSystem.DocumentMode)
                {
                    ImGui.TextDisabled("Apply one coherent source mode to every element:");
                    if (ImGui.Button("ALL follow Theme + Effects globals"))
                        HudEditorMode.SetAllFollowGlobal(true);
                    ImGui.SameLine();
                    if (ImGui.Button("Snapshot ALL as Custom"))
                        HudEditorMode.SetAllFollowGlobal(false);
                    if (ImGui.Button("Flatten ALL boxes")) HudEditorMode.MakeAllFlat();
                }
            }

            if (!HudSystem.DocumentMode && ImGui.CollapsingHeader("Legacy panel sizes"))
            {
                FloatSlider(HudConfig.TopBarHeight, "Top bar height (px)", 36f, 120f);
                FloatSlider(HudConfig.TopBarCurve, "Top bar curve (end drop px)", 0f, 120f);
                FloatSlider(HudConfig.TopBarWidthPct, "Top bar width (fraction)", 0.5f, 1f);
                FloatSlider(HudConfig.CompassWidthPct, "Compass width (fraction)", 0.05f, 0.4f);
                FloatSlider(HudConfig.CompassHeight, "Compass height (px)", 20f, 80f);
                FloatSlider(HudConfig.CompassFovDeg, "Compass span (degrees)", 40f, 200f);
                FloatSlider(HudConfig.EquipBoxSize, "Equipment box size (px)", 44f, 128f);
                FloatSlider(HudConfig.EquipSpacing, "Equipment box gap (px)", 2f, 30f);
                FloatSlider(HudConfig.HandBoxWidth, "Hand box width (px)", 80f, 240f);
                FloatSlider(HudConfig.HandBoxHeight, "Hand box height (px)", 56f, 160f);
                FloatSlider(HudConfig.VitalsWidth, "Vitals card width (px)", 160f, 420f);
                FloatSlider(HudConfig.VitalsHeight, "Vitals card height (px)", 100f, 300f);
            }

            if (ImGui.CollapsingHeader("Typography", ImGuiTreeNodeFlags.DefaultOpen))
            {
                FontCombo();
                FloatSlider(HudConfig.FontScale, "Font scale (all HUD text)", 0.6f, 1.8f);
                FloatSlider(HudConfig.LabelFontSize, "Label size (PRESSURE, HELMET...)", 7f, 22f);
                FloatSlider(HudConfig.ValueFontSize, "Value size (101 kPa...)", 10f, 30f);
                FloatSlider(HudConfig.CompassFontSize, "Compass text size", 8f, 22f);
                FloatSlider(HudConfig.VitalsRowFontSize, "Vitals row size", 9f, 22f);
                FloatSlider(HudConfig.BareWordFontSize, "Felt-sense word size", 12f, 36f);
            }

            if (ImGui.CollapsingHeader("Palette", ImGuiTreeNodeFlags.DefaultOpen))
                DrawColourControls();
        }

        private void DrawEffectsTab()
        {
            ImGui.TextDisabled("GLOBAL — element popups can follow these defaults or override supported effects.");

            if (ImGui.CollapsingHeader("Edges, glow & pulse", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(HudConfig.FxTierA, "Enable surface and edge effects");
                if (HudConfig.FxTierA != null && HudConfig.FxTierA.Value)
                {
                    Toggle(HudConfig.FxHairlinesOn, "Hairlines (sub-1px lines fade, not vanish)");
                    if (HudConfig.FxHairlinesOn.Value)
                        FloatSlider(HudConfig.FxHairlineMin, "  thinnest line (px)", 0.05f, 1f);

                    Toggle(HudConfig.FxEdgeLightOn, "Edge energy (borders + lines)");
                    if (HudConfig.FxEdgeLightOn.Value)
                    {
                        FloatSlider(HudConfig.FxEdgeLight, "  Strength##edgeEnergyStrength", 0f, 2f);
                        DrawEdgeLightColour();
                        FloatSlider(HudConfig.FxEdgeLightAngle, "  light angle (0=R,90=top,180=L)", 0f, 360f);
                        FloatSlider(HudConfig.FxEdgeLightRim, "  opposing-rim catch", 0f, 2f);
                        FloatSlider(HudConfig.FxEdgeLightSharp, "  falloff (high=tight, low=broad)", 1f, 8f);
                        FloatSlider(HudConfig.FxEdgeRipple, "  irregular energy", 0f, 2.5f);
                        FloatSlider(HudConfig.FxEdgeRippleFreq, "  energy frequency", 0.05f, 8f);
                        FloatSlider(HudConfig.FxEdgeFlowSpeed, "  flow speed (0 = frozen)", 0f, 4f);
                    }

                    Toggle(HudConfig.FxBorderFadeOn, "Border fade (unlit sections dissolve)");
                    if (HudConfig.FxBorderFadeOn.Value)
                        FloatSlider(HudConfig.FxBorderFade, "  fade amount", 0f, 1f);
                    Toggle(HudConfig.FxSoftEdgeOn, "Soft edge (boxes melt together)");
                    if (HudConfig.FxSoftEdgeOn.Value)
                        FloatSlider(HudConfig.FxSoftEdge, "  Width (px)##softEdgeWidth", 0f, 48f);
                    Toggle(HudConfig.FxGlowOn, "Glow halo");
                    if (HudConfig.FxGlowOn.Value)
                    {
                        FloatSlider(HudConfig.FxGlow, "  outward strength", 0f, 2f);
                        FloatSlider(HudConfig.FxGlowInner, "  inward strength", 0f, 2f);
                        FloatSlider(HudConfig.FxGlowWidth, "  Width (px)##glowWidth", 6f, 160f);
                        FloatSlider(HudConfig.FxGlowDiffuse, "  diffuseness (haze)", 0f, 1f);
                    }
                    Toggle(HudConfig.FxPulseOn, "Allow per-element breathing pulse");
                    if (HudConfig.FxPulseOn.Value)
                    {
                        FloatSlider(HudConfig.FxPulseSpeed, "  pulse speed (Hz)", 0.05f, 3f);
                        FloatSlider(HudConfig.FxPulseDepth, "  pulse depth", 0f, 1f);
                    }
                }
            }

            if (ImGui.CollapsingHeader("Glass animation", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f),
                    Core.HudShaderStore.TierBAvailable
                        ? "SHADER BUNDLE READY"
                        : "SHADER BUNDLE NOT LOADED — THESE EFFECTS ARE INACTIVE");
                Toggle(HudConfig.FxTierB, "Enable glass animation effects");
                if (HudConfig.FxTierB.Value)
                {
                    Toggle(HudConfig.FxShineOn, "Shine sweep");
                    if (HudConfig.FxShineOn.Value)
                    {
                        FloatSlider(HudConfig.FxShine, "  Strength##shineStrength", 0f, 2f);
                        FloatSlider(HudConfig.FxShinePeriod, "  period (seconds)", 2f, 60f);
                    }
                    Toggle(HudConfig.FxIridOn, "Iridescent rim");
                    if (HudConfig.FxIridOn.Value)
                        FloatSlider(HudConfig.FxIridescence, "  Strength##iridescenceStrength", 0f, 1f);
                    Toggle(HudConfig.FxChromaOn, "Chromatic fringe (uses frosted backdrop)");
                    if (HudConfig.FxChromaOn.Value)
                        FloatSlider(HudConfig.FxChroma, "  Strength##chromaStrength", 0f, 1f);
                    Toggle(HudConfig.FxDissolveBoot, "Dissolve reveal on boot/power transitions");
                }
            }

            if (ImGui.CollapsingHeader("Frosted glass", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(HudConfig.FxTierC, "Enable frosted-glass backdrop (Flat/Warp curvature only)");
                if (HudConfig.FxTierC.Value)
                {
                    FloatSlider(HudConfig.FrostStrength, "Frost strength (all elements)", 0f, 1f);
                    FloatSlider(HudConfig.FrostDepth, "Blur depth (shallow - deep)", 0f, 1f);
                    FloatSlider(HudConfig.FrostDarken, "Backdrop darkening", 0f, 1f);
                    FrostDownsampleCombo();
                    IntSliderCfg(HudConfig.FrostUpdateEveryN, "Re-blur every N frames", 1, 8);
                    RgbConfig(HudConfig.FrostTint, "Frost tint", ref _frostTintStr, ref _frostTintVec);
                    ImGui.TextDisabled(HudBackdrop.Active ? "Backdrop capture active." : "Backdrop capture is idle or unavailable in this view mode.");
                }
            }

            if (ImGui.CollapsingHeader("HUD bloom", ImGuiTreeNodeFlags.DefaultOpen))
                DrawBloomControls();

            ImGui.Separator();
            if (ImGui.Button("Reset active per-element effects to current globals"))
                HudEditorMode.ResetAllElementEffects();
            ImGui.TextDisabled("Custom appearance stays custom; effect values receive a coherent global snapshot.");
        }

        private void DrawViewBehaviorTab()
        {
            ImGui.TextDisabled("GLOBAL — projection, suit behavior and vanilla-panel integration.");

            if (ImGui.CollapsingHeader("HUD renderer", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(HudConfig.VisorHudEnabled, "Visor HUD enabled");
                Toggle(HudConfig.UseDocumentHud, "Use profile-driven document HUD");
                if (!HudSystem.DocumentMode)
                {
                    ImGui.TextDisabled("Legacy fixed panels:");
                    Toggle(HudConfig.ShowTopBar, "  Top status bar");
                    Toggle(HudConfig.ShowCompass, "  Compass ribbon");
                    Toggle(HudConfig.ShowEquipment, "  Equipment column (1-6)");
                    Toggle(HudConfig.ShowHands, "  Hand boxes");
                    Toggle(HudConfig.ShowVitals, "  Vitals card / felt senses");
                    Toggle(HudConfig.ShowHologram, "  Player hologram in vitals card");
                }
                Toggle(HudConfig.ShowVignette, "Visor-edge vignette");
            }

            if (ImGui.CollapsingHeader("Curvature & projection", ImGuiTreeNodeFlags.DefaultOpen))
            {
                CurvatureCombo();
                FloatSlider(HudConfig.CurveStrength, "Curve strength (0 flat - 1 fishbowl)", 0f, 1f);
                Toggle(HudConfig.CurveInvert, "Invert curve direction");
                Toggle(HudConfig.BareFlattens, "Flatten the HUD when suit power is absent");
                if (HudConfig.Curvature.Value == HudCurvature.CurvedWorldCanvas
                    || HudConfig.Curvature.Value == HudCurvature.CurvedRt)
                    FloatSlider(HudConfig.WorldCanvasDistance, "Visor distance (m)", 0.25f, 2f);
            }

            if (ImGui.CollapsingHeader("Suit power & transitions", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(HudConfig.DiegeticTiers, "Diegetic tiers (no suit power = words only)");
                Toggle(HudConfig.FlickerAnimations, "Flicker animations (off/boot/death)");
                Toggle(HudConfig.LowPowerDropouts, "Low-power dropout glitches");
                if (HudConfig.LowPowerDropouts.Value)
                    FloatSlider(HudConfig.LowPowerThreshold, "  low-power threshold (%)", 0f, 40f);
                if (ImGui.Button("Test power-death flicker")) HudSystem.TestPowerDeath();
                ImGui.SameLine();
                if (ImGui.Button("Test boot sequence")) HudSystem.TestBoot();

                ImGui.Spacing();
                Toggle(HudConfig.GlitchEnabled, "Power-transition tear / shake");
                if (HudConfig.GlitchEnabled.Value)
                {
                    FloatSlider(HudConfig.GlitchDuration, "  duration (seconds)", 0.1f, 4f);
                    FloatSlider(HudConfig.GlitchIntensity, "  severity", 0f, 1f);
                    Toggle(HudConfig.GlitchOnPowerDown, "  fire on power DOWN / suit removed");
                    Toggle(HudConfig.GlitchOnPowerUp, "  fire on power UP / boot");
                    if (ImGui.Button("Test glitch now")) HudGlitch.TriggerTest();
                }
                ImGui.TextDisabled("Each element can opt out of collapse, glitch and warp in its inspector.");
            }

            if (ImGui.CollapsingHeader("Vanilla panels", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("Hidden through vanilla visibility paths; objects are never destroyed.");
                Toggle(UIAConfig.HideVanillaHands, "Hide vanilla hands panel");
                Toggle(UIAConfig.HideVanillaClothing, "Hide vanilla clothing panel");
                Toggle(UIAConfig.HideVanillaStatus, "Hide vanilla status panel");
                Toggle(UIAConfig.HideVanillaPlayerState, "Hide vanilla instrument cluster (bottom-right)");
            }
        }

        private void DrawDiagnosticsTab()
        {
            ImGui.TextDisabled("Developer previews, renderer fallback and performance tools.");

            if (ImGui.CollapsingHeader("Preview data", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(HudConfig.DebugShowAll, "Show everything (all vitals/moodlets/instruments)");
                Toggle(HudConfig.DebugShowAllBare, "Show everything in power-off / bare layout");
            }

            if (ImGui.CollapsingHeader("Renderer status", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text("Document HUD: " + (HudSystem.DocumentMode ? "ACTIVE" : "legacy fixed panels"));
                ImGui.Text("Effects bundle: " + (Core.HudShaderStore.TierBAvailable ? "READY" : "NOT LOADED"));
                ImGui.Text("Analytic panel shader: " + (Core.HudShaderStore.SdfAvailable ? "READY" : "FALLBACK MESH"));
                ImGui.Text("Frost capture: " + (HudBackdrop.Active ? "ACTIVE" : "IDLE"));
                ImGui.Text("Bloom: " + (HudBloomFx.Available ? "AVAILABLE" : "UNAVAILABLE"));
                Toggle(HudConfig.LegacyImGuiHud, "Draw legacy ImGui HUD diagnostics overlay");
            }

            if (ImGui.CollapsingHeader("Profiler", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool vis = Profiling.ProfilicusUniversalis.IsVisible;
                if (ImGui.Button(vis ? "Hide profiler window" : "Show profiler window"))
                    Profiling.ProfilicusUniversalis.SetVisible(!vis);
                ImGui.SameLine();
                if (ImGui.Button("Snapshot##prof"))
                    Profiling.ProfilicusUniversalis.SaveSnapshot();
                ImGui.TextDisabled("Console: uiaprof [on|off|clear|save|ab <effect>]");
                ImGui.TextDisabled("A/B measures an effect's real ON-vs-OFF frame cost.");
            }
        }

        // ------------------------------------------------------------------ designer

        private static int _addTypeIndex;
        private static string _saveAsName = "";

        // Profile-list cache: Directory.GetFiles is disk IO + allocation, and the header runs
        // it every frame. Re-scan only when the dropdown opens (edge-triggered) or after a save.
        private static List<string> _profileNamesCache = new List<string>();
        private static bool _profileComboOpen;
        private static readonly string[] AddableTypes =
        {
            "Box", "Label", "Polyline", "Icon", "Readout", "Clock", "WorldName",
            "DayCounter", "ActiveHandBadge", "Compass", "MoodletDashboard",
            "EquipmentColumn", "HandBoxes", "KeybindChips", "Portrait", "BodyDoll",
            "SuitChips", "BareSenses",
            // Glassy 2.0 widgets (the play-test found these missing — a deleted
            // speed/jetpack/vitals box couldn't be re-created):
            "VitalsPanel", "DamageDoll", "JetpackBox", "StateChips",
            // The PNG body doll (assembled from config/StationeersUIMod/HudIcons art):
            "PngDoll",
        };

        /// <summary>The HUD Designer controls: grid, add/draw, and selection actions.
        /// Profile and history controls stay in the persistent toolbar above the tabs.</summary>
        private void DrawDesignerSection()
        {
            ImGui.TextDisabled("Click an element to select - drag to move, corners resize.");
            ImGui.TextDisabled("Del removes - Ctrl+D duplicates - Ctrl+Z / Ctrl+Y undo/redo.");
            ImGui.TextDisabled("Ctrl+drag = box-select many - arrow keys nudge (Shift = grid).");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "CANVAS & GRID");
            Toggle(HudConfig.GridSnapEnabled, "Snap to grid (hold Alt to bypass)");
            Toggle(HudConfig.ShowGrid, "Show grid");
            FloatSlider(HudConfig.GridSnapSize, "Grid size (px)", 2f, 64f);
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "CREATE & EDIT GEOMETRY");
            ImGui.SetNextItemWidth(200f);
            if (ImGui.BeginCombo("##addtype", AddableTypes[_addTypeIndex]))
            {
                for (int i = 0; i < AddableTypes.Length; i++)
                    if (ImGui.Selectable(AddableTypes[i], i == _addTypeIndex))
                        _addTypeIndex = i;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Add element"))
            {
                UI.Hud.HudElementType t;
                if (System.Enum.TryParse(AddableTypes[_addTypeIndex], out t))
                    HudEditorMode.AddElement(t);
            }
            if (HudEditorMode.DrawingLine)
            {
                ImGui.TextColored(new Vector4(1f, 0.62f, 0.15f, 1f),
                    "DRAWING: click points - Enter/RMB finish - Esc cancel");
                if (ImGui.Button("Cancel line")) HudEditorMode.CancelDrawLine();
            }
            else if (ImGui.Button("Draw a line (click points on screen)"))
            {
                HudEditorMode.BeginDrawLine();
            }
            if (HudEditorMode.DrawingShape)
            {
                ImGui.TextColored(new Vector4(1f, 0.62f, 0.15f, 1f),
                    "PEN: click points - click the first dot / Enter / RMB to CLOSE - Esc cancel");
                if (ImGui.Button("Cancel shape")) HudEditorMode.CancelDrawShape();
            }
            else if (ImGui.Button("Draw a shape (pen: click points, close for a filled glass shape)"))
            {
                HudEditorMode.BeginDrawShape();
            }
            // Edit the anchors of a selected shape/line (drag / Alt+click delete / click-segment add).
            if (HudEditorMode.SelectedElement != null && HudEditorMode.IsPointEditable(HudEditorMode.SelectedElement.Def))
            {
                if (HudEditorMode.EditingPoints)
                {
                    ImGui.TextColored(new Vector4(1f, 0.62f, 0.15f, 1f),
                        "EDIT POINTS: drag anchors - Alt+click deletes - click a segment inserts");
                    ImGui.TextColored(new Vector4(1f, 0.62f, 0.15f, 1f),
                        "Bezier: Ctrl+click an anchor = CORNER point (straight) / smooth toggle");
                    if (ImGui.Button("Done editing points")) HudEditorMode.EndEditPoints();
                }
                else if (ImGui.Button("Edit points (drag / add / delete anchors)"))
                {
                    HudEditorMode.BeginEditPoints();
                }
            }
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "SELECTION");
            var sel = HudEditorMode.SelectedElement;
            if (sel != null)
            {
                if (ImGui.Button("Duplicate")) HudEditorMode.DuplicateSelected();
                ImGui.SameLine();
                if (ImGui.Button("Delete")) HudEditorMode.DeleteSelected();
                if (sel.CanFlatten)
                {
                    if (ImGui.Button("Make flat (strip optical effects)")) HudEditorMode.MakeSelectedFlat();
                    ImGui.TextDisabled("Snapshots this panel as Custom, then disables glass, glow and optical layers.");
                }

                // Per-curvature-mode placement: dragging writes to the CURRENT mode's own layout
                // for the LIVE HUD (bare stays shared). Show which, and let it snap back to base.
                var lm = UI.Hud.HudElementView.LayoutMode;
                string mn = lm == UI.Hud.HudCurvature.VertexWarp ? "A"
                    : lm == UI.Hud.HudCurvature.DomeProjection ? "B"
                    : lm == UI.Hud.HudCurvature.CurvedWorldCanvas ? "C"
                    : lm == UI.Hud.HudCurvature.CurvedRt ? "D" : null;
                if (mn == null)
                    ImGui.TextDisabled("Flat: editing the base placement (shared by all modes).");
                else
                {
                    bool has = sel.Def.HasModeLayout(lm);
                    ImGui.TextDisabled("Placement saved per mode - editing writes mode " + mn
                        + (has ? " (custom)." : " (= base)."));
                    if (has && ImGui.Button("Reset mode " + mn + " placement to base"))
                        HudEditorMode.ResetModeLayout();
                }
            }
            else
                ImGui.TextDisabled("Select an element on the HUD to show its document actions.");
        }

        private void DrawProfilesSection()
        {
            ImGui.TextDisabled("Active profile (shareable XML; edits autosave):");
            string active = HudEditorMode.ActiveProfileName();
            ImGui.SetNextItemWidth(200f);
            bool comboOpen = ImGui.BeginCombo("##profile", active);
            if (comboOpen)
            {
                // Re-scan disk only on the opening edge; reuse the cache while it stays open.
                if (!_profileComboOpen) _profileNamesCache = Features.HudProfileStore.ListProfiles();
                foreach (var n in _profileNamesCache)
                {
                    if (ImGui.Selectable(n, string.Equals(n, active, System.StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(n, active, System.StringComparison.OrdinalIgnoreCase))
                    {
                        FlushPendingElementEdit();
                        if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = n;
                        // Fallback factory guards a corrupt file: keep what we have.
                        var keep = Features.HudProfileStore.Active;
                        Features.HudProfileStore.LoadActive(n,
                            () => keep != null ? keep.Clone() : new UI.Hud.HudDocument { Name = n });
                        UI.Hud.HudDocumentHistory.Clear();
                    }
                }
                ImGui.EndCombo();
            }
            _profileComboOpen = comboOpen;
            ImGui.SameLine();
            if (ImGui.Button("Open folder"))
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Features.HudProfileStore.Dir); }
                catch { }
            }

            ImGui.TextDisabled("New copy:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(145f);
            ImGui.InputText("##saveas", ref _saveAsName, 48);
            ImGui.SameLine();
            if (ImGui.Button("Duplicate as") && !string.IsNullOrEmpty(_saveAsName))
            {
                FlushPendingElementEdit();
                // Sanitize BEFORE remembering the name: the store strips illegal chars
                // for the file, and a config name that kept them would miss the file on
                // the next launch and silently regenerate the default.
                string clean = _saveAsName;
                foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                    clean = clean.Replace(bad.ToString(), "");
                clean = clean.Trim();
                var doc = Features.HudProfileStore.Active;
                var copy = doc != null ? doc.Clone() : null;
                if (copy != null) copy.Name = clean;
                if (!string.IsNullOrEmpty(clean) && copy != null
                    && Features.HudProfileStore.Save(copy, clean))
                {
                    if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = clean;
                    Features.HudProfileStore.SetActive(copy, clean);
                    UI.Hud.HudDocumentHistory.Clear();
                    _saveAsName = "";
                    _profileNamesCache = Features.HudProfileStore.ListProfiles(); // new file: refresh the cache
                }
            }
        }

        // ------------------------------------------------------------------ gizmos

        /// <summary>Selection outline + handles + hover ghost + line-draw preview, drawn
        /// on the foreground list so they ride ABOVE the HUD. Corners are forward-warped
        /// (8 samples per edge) so the box hugs curved elements.</summary>
        private static void DrawGizmos()
        {
            if (!HudSystem.DocumentMode) return;
            var dl = ImGui.GetForegroundDrawList();
            float scale = HudConfig.HudScale.Value;
            uint selCol = ImGui.GetColorU32(new Vector4(1f, 0.62f, 0.15f, 0.95f));
            uint hovCol = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.55f));
            uint handleCol = ImGui.GetColorU32(new Vector4(1f, 0.62f, 0.15f, 1f));

            var hov = HudEditorMode.HoverElement;
            var sel = HudEditorMode.SelectedElement;

            // Snap-grid overlay (drawn first, behind the outlines). Snapping is measured from an
            // element's own anchor, so the grid is drawn from the SELECTED element's anchor (else
            // screen centre) — its centre then lands exactly on the intersections as you drag.
            if (HudConfig.ShowGrid != null && HudConfig.ShowGrid.Value && HudConfig.GridSnapSize != null)
                DrawGrid(dl, sel, scale);

            if (hov != null && !ReferenceEquals(hov, sel))
                OutlineRect(dl, hov.CanvasRect(scale), hovCol, 1.2f);

            // Marquee group members get their own outline (thinner than the primary).
            if (HudEditorMode.MultiCount > 1)
            {
                uint grpCol = ImGui.GetColorU32(new Vector4(1f, 0.62f, 0.15f, 0.6f));
                var views = new List<UI.Hud.HudElementView>();
                HudSystem.CollectElementViews(views);
                foreach (var v in views)
                    if (HudEditorMode.IsMultiSelected(v.Def.Id) && !ReferenceEquals(v, sel))
                        OutlineRect(dl, v.CanvasRect(scale), grpCol, 1.4f);
            }

            if (sel != null)
            {
                var r = sel.CanvasRect(scale);
                // Push the selection OUTLINE a few px OUTSIDE the element so it never sits on top of
                // the element's own border — you couldn't see the border you were tuning (FlorpyDorp:
                // "the orange box covers the borders"). The HANDLES stay on the TRUE rect corners
                // (that's where you grab) and are drawn HOLLOW so the border shows through their
                // centres. SampledEdge/ToImGui warp per-sample, so the inflated rect still bows.
                float m = 5f * scale;
                var ro = Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);
                OutlineRect(dl, ro, selCol, 1.4f);
                if (HudEditorMode.EditingPoints && HudEditorMode.IsPointEditable(sel.Def))
                {
                    // Point-edit sub-mode: draggable ANCHOR handles instead of the 8 resize handles.
                    uint ringCol = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1f));
                    var pts = sel.Def.GetPoints("pts");
                    bool bez = sel.Def.GetI("curveMode", sel.Def.GetB("smooth", false) ? 1 : 0) == 2;
                    if (bez)
                    {
                        // Bézier tangent handles (cyan): a line from each anchor to its in/out control.
                        uint hCol = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.95f));
                        uint hLine = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.5f));
                        var hin = sel.Def.GetPoints("hin");
                        var hout = sel.Def.GetPoints("hout");
                        for (int i = 0; i < pts.Length; i++)
                        {
                            var a = ToImGui(HudEditorMode.PointCanvas(sel, pts[i], scale));
                            if (i < hout.Length)
                            {
                                var ho = ToImGui(HudEditorMode.PointCanvas(sel, pts[i] + hout[i], scale));
                                dl.AddLine(a, ho, hLine, 1.4f); dl.AddCircleFilled(ho, 3.5f, hCol, 12);
                            }
                            if (i < hin.Length)
                            {
                                var hp = ToImGui(HudEditorMode.PointCanvas(sel, pts[i] + hin[i], scale));
                                dl.AddLine(a, hp, hLine, 1.4f); dl.AddCircleFilled(hp, 3.5f, hCol, 12);
                            }
                        }
                    }
                    for (int i = 0; i < pts.Length; i++) // anchors on top of the handle lines
                    {
                        var s = ToImGui(HudEditorMode.PointCanvas(sel, pts[i], scale));
                        dl.AddCircleFilled(s, 4.5f, handleCol, 16);
                        dl.AddCircle(s, 4.5f, ringCol, 16, 1f);
                    }
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var s = ToImGui(HudEditorMode.HandlePoint(r, i));
                        dl.AddRect(new Vector2(s.x - 4f, s.y - 4f), new Vector2(s.x + 4f, s.y + 4f),
                            handleCol, 0f, ImDrawFlags.None, 1.4f);
                    }
                }
            }

            // The live Ctrl+drag marquee box.
            if (HudEditorMode.MarqueeActive)
            {
                uint mqCol = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.9f));
                uint mqFill = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.12f));
                var mr = HudEditorMode.MarqueeRect;
                var a = ToImGui(new Vector2(mr.xMin, mr.yMin));
                var b = ToImGui(new Vector2(mr.xMax, mr.yMax));
                var mn = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
                var mx = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                dl.AddRectFilled(mn, mx, mqFill);
                dl.AddRect(mn, mx, mqCol, 0f, ImDrawFlags.None, 1.4f);
            }

            if (HudEditorMode.DrawingLine || HudEditorMode.DrawingShape)
            {
                var pts = HudEditorMode.DrawPoints;
                for (int i = 0; i < pts.Count; i++)
                {
                    var s = ToImGui(pts[i]);
                    dl.AddCircleFilled(s, 3.5f, selCol, 12);
                    if (i > 0) dl.AddLine(ToImGui(pts[i - 1]), s, selCol, 2f);
                }
                // Pen tool: hint the closing segment back to the first point + mark the close target.
                if (HudEditorMode.DrawingShape && pts.Count >= 2)
                {
                    uint closeCol = ImGui.GetColorU32(new Vector4(1f, 0.62f, 0.15f, 0.45f));
                    dl.AddLine(ToImGui(pts[pts.Count - 1]), ToImGui(pts[0]), closeCol, 1.6f);
                    dl.AddCircle(ToImGui(pts[0]), 7f, selCol, 16, 2f);
                }
            }
        }

        /// <summary>Draw the snap grid, WARPED to match the active curvature (each line is sampled
        /// through the same forward warp as the element outlines, so it bends with modes A/B and
        /// stays flat under C exactly like the outlines do). Origin = the selected element's anchor
        /// point so its centre lands on the intersections as it snaps (snapping is measured from
        /// each element's own anchor). When the snap cell is very fine, only every Nth snap line is
        /// drawn so the grid stays readable and cheap — every drawn line is still a snap line.</summary>
        private static void DrawGrid(ImGuiNET.ImDrawListPtr dl, UI.Hud.HudElementView sel, float scale)
        {
            float cell = Mathf.Max(1f, HudConfig.GridSnapSize.Value) * scale;
            float g = cell;
            while (g < 6f) g += cell;   // keep on-screen spacing >= ~6px (multiple of the snap cell)
            float hw = Screen.width * 0.5f, hh = Screen.height * 0.5f;
            var anchor = sel != null
                ? sel.Def.AnchorFor(UI.Hud.HudElementView.LayoutBare, UI.Hud.HudElementView.LayoutMode)
                : UI.Hud.HudAnchor.Center;
            Vector2 o = UI.Hud.HudElementDef.AnchorPoint(anchor, hw, hh);
            uint col = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.14f));
            uint axis = ImGui.GetColorU32(new Vector4(0.25f, 0.85f, 0.93f, 0.40f)); // the anchor's own row/column

            int nx = Mathf.CeilToInt((hw + Mathf.Abs(o.x)) / g) + 1;
            for (int k = -nx; k <= nx; k++)
            {
                float x = o.x + k * g;
                if (x < -hw || x > hw) continue;
                SampledEdge(dl, new Vector2(x, -hh), new Vector2(x, hh), k == 0 ? axis : col, 1f);
            }
            int ny = Mathf.CeilToInt((hh + Mathf.Abs(o.y)) / g) + 1;
            for (int k = -ny; k <= ny; k++)
            {
                float y = o.y + k * g;
                if (y < -hh || y > hh) continue;
                SampledEdge(dl, new Vector2(-hw, y), new Vector2(hw, y), k == 0 ? axis : col, 1f);
            }
        }

        /// <summary>Canvas point -> ImGui screen coords (y down), through the forward warp.</summary>
        private static Vector2 ToImGui(Vector2 canvas)
        {
            var s = HudEditorMode.CanvasToScreen(canvas);
            return new Vector2(s.x, Screen.height - s.y);
        }

        private static void OutlineRect(ImGuiNET.ImDrawListPtr dl, Rect r, uint col, float thick)
        {
            // 8 samples per edge: under curvature a straight screen line would cut the
            // corner of a warped element.
            SampledEdge(dl, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), col, thick);
            SampledEdge(dl, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), col, thick);
            SampledEdge(dl, new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), col, thick);
            SampledEdge(dl, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), col, thick);
        }

        private static void SampledEdge(ImGuiNET.ImDrawListPtr dl, Vector2 a, Vector2 b, uint col, float thick)
        {
            const int Steps = 8;
            var prev = ToImGui(a);
            for (int i = 1; i <= Steps; i++)
            {
                var cur = ToImGui(Vector2.Lerp(a, b, i / (float)Steps));
                dl.AddLine(prev, cur, col, thick);
                prev = cur;
            }
        }

        // ------------------------------------------------------------------ popup

        /// <summary>The click-to-edit popup, drawn inside the game's ImGui frame near
        /// wherever the user clicked. Static: called from the plugin draw hook.</summary>
        private static int _popupStamp = -1;

        private static int _elementPopupStamp = -1;
        private static readonly List<UI.Hud.HudProp> _propScratch = new List<UI.Hud.HudProp>();
        private static UI.Hud.HudDocument _pendingElementUndo;
        private static UI.Hud.HudDocument _pendingElementDocument;
        private static string _pendingElementProfile;
        private static bool _pendingElementChanged;
        private static UI.Hud.HudPropGroup? _activeElementPropGroup;

        /// <summary>Finish an in-flight property gesture before selection/profile/editor state
        /// changes can make ImGui omit its normal deactivation callback. The old document is still
        /// persisted if an external profile swap beat us here; its undo snapshot is intentionally
        /// not mixed into the new profile's history.</summary>
        private static void FlushPendingElementEdit()
        {
            if (_pendingElementUndo != null && _pendingElementChanged
                && _pendingElementDocument != null
                && !HudEditorMode.DocumentsEqual(_pendingElementDocument, _pendingElementUndo))
            {
                var active = Features.HudProfileStore.Active;
                if (ReferenceEquals(active, _pendingElementDocument))
                {
                    UI.Hud.HudDocumentHistory.Push(_pendingElementUndo);
                    Features.HudProfileStore.MarkChanged();
                }
                else if (_pendingElementDocument != null && !string.IsNullOrEmpty(_pendingElementProfile))
                {
                    Features.HudProfileStore.Save(_pendingElementDocument, _pendingElementProfile);
                }
            }
            _pendingElementUndo = null;
            _pendingElementDocument = null;
            _pendingElementProfile = null;
            _pendingElementChanged = false;
        }

        private static void CancelPendingElementEdit()
        {
            _pendingElementUndo = null;
            _pendingElementDocument = null;
            _pendingElementProfile = null;
            _pendingElementChanged = false;
        }

        public static void DrawPopupOverlay()
        {
            if (!HudEditorMode.Active) return;

            DrawGizmos();

            // Document mode: the selected ELEMENT gets the generic property popup.
            if (HudSystem.DocumentMode)
            {
                var el = HudEditorMode.SelectedElement;
                if (el == null || el.Def == null)
                {
                    FlushPendingElementEdit();
                    _activeElementPropGroup = null;
                    return;
                }
                bool elMoved = _elementPopupStamp != HudEditorMode.ElementStamp;
                if (elMoved)
                {
                    FlushPendingElementEdit();
                    _activeElementPropGroup = null;
                }
                _elementPopupStamp = HudEditorMode.ElementStamp;
                // Open CENTERED (FlorpyDorp: the click-point spawn kept landing bottom-
                // right) and freely resizable up to nearly the screen — the old 400x560
                // cap cut off long property lists.
                ImGui.SetNextWindowPos(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                    elMoved ? ImGuiCond.Always : ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowSizeConstraints(new Vector2(320f, 120f),
                    new Vector2(Screen.width * 0.92f, Screen.height * 0.92f));
                bool elOpen = true;
                if (ImGui.Begin("Edit: " + el.Def.Type + "###UIAHudElementPopup",
                    ref elOpen, ImGuiWindowFlags.NoCollapse))
                {
                    _propScratch.Clear();
                    try { el.DescribeProps(_propScratch); }
                    catch { }
                    // Undo is pushed on COMMIT, not on focus: IsItemActivated fires on a
                    // mere click into a widget, and pushing there wiped the redo stack
                    // with dead steps (review finding). The begin-stash keeps the
                    // pre-gesture state; only a real change spends it.
                    System.Action beginElementEdit = () =>
                    {
                        if (_pendingElementUndo != null) return;
                        var doc = Features.HudProfileStore.Active;
                        _pendingElementUndo = doc != null ? doc.Clone() : null;
                        _pendingElementDocument = doc;
                        _pendingElementProfile = HudEditorMode.ActiveProfileName();
                        _pendingElementChanged = false;
                    };
                    System.Action commitElementEdit = () =>
                    {
                        FlushPendingElementEdit();
                    };
                    System.Action cancelElementEdit = () =>
                    {
                        CancelPendingElementEdit();
                    };
                    System.Action noteElementChanged = () =>
                    {
                        if (_pendingElementUndo != null) _pendingElementChanged = true;
                    };

                    if (ImGui.BeginTabBar("##UIAHudElementTabs"))
                    {
                        DrawElementPropTab("Content", UI.Hud.HudPropGroup.Content,
                            beginElementEdit, commitElementEdit, cancelElementEdit, noteElementChanged);
                        DrawElementPropTab("Layout", UI.Hud.HudPropGroup.Layout,
                            beginElementEdit, commitElementEdit, cancelElementEdit, noteElementChanged);
                        DrawElementPropTab("Appearance", UI.Hud.HudPropGroup.Appearance,
                            beginElementEdit, commitElementEdit, cancelElementEdit, noteElementChanged);
                        DrawElementPropTab("Effects", UI.Hud.HudPropGroup.Effects,
                            beginElementEdit, commitElementEdit, cancelElementEdit, noteElementChanged);
                        DrawElementPropTab("Interaction", UI.Hud.HudPropGroup.Interaction,
                            beginElementEdit, commitElementEdit, cancelElementEdit, noteElementChanged);
                        ImGui.EndTabBar();
                    }
                    // Live feedback only while a widget is actually being edited — an
                    // idle popup must not rebuild the element's meshes every frame.
                    bool editing = false;
                    try { editing = ImGui.IsAnyItemActive(); } catch { }
                    if (editing) HudSystem.RelayoutElement(el);
                    ImGui.Separator();
                    if (ImGui.Button("Duplicate##pop")) HudEditorMode.DuplicateSelected();
                    ImGui.SameLine();
                    if (ImGui.Button("Delete##pop")) HudEditorMode.DeleteSelected();
                    if (el.CanFlatten)
                    {
                        if (ImGui.Button("Make flat (strip optical effects)##pop")) HudEditorMode.MakeSelectedFlat();
                        ImGui.TextDisabled("Snapshots this panel as Custom, then disables glass, glow and optical layers.");
                    }
                }
                ImGui.End();
                if (!elOpen)
                {
                    FlushPendingElementEdit();
                    _activeElementPropGroup = null;
                    HudEditorMode.ClearElementSelection();
                }
                return;
            }

            var target = HudEditorMode.Selected;
            if (target == null) return;

            // A NEW selection moves the popup to the new click; afterwards the user may
            // drag it wherever they like.
            bool moved = _popupStamp != HudEditorMode.SelectionStamp;
            _popupStamp = HudEditorMode.SelectionStamp;
            ImGui.SetNextWindowPos(new Vector2(HudEditorMode.PopupPos.x, HudEditorMode.PopupPos.y),
                moved ? ImGuiCond.Always : ImGuiCond.Appearing);
            ImGui.SetNextWindowSizeConstraints(new Vector2(300f, 0f), new Vector2(380f, 480f));
            bool open = true;
            if (ImGui.Begin("Edit: " + target.Title + "###UIAHudEditPopup",
                ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                _frameSnapshot = HudPalette.Snapshot();
                foreach (var name in target.Palette)
                {
                    var entry = HudPalette.All.Find(e => e.Name == name);
                    if (entry != null) ColorWheel(entry);
                }
                if (target.Values.Length > 0) ImGui.Separator();
                foreach (var v in target.Values)
                    DrawConfigWidget(v);
            }
            ImGui.End();
            if (!open) HudEditorMode.Selected = null;
        }

        private static void DrawElementPropTab(string title, UI.Hud.HudPropGroup group,
            System.Action beginEdit, System.Action commitEdit, System.Action cancelEdit,
            System.Action noteChanged)
        {
            if (!HudPropDrawer.HasGroup(_propScratch, group) || !ImGui.BeginTabItem(title)) return;
            if (!_activeElementPropGroup.HasValue || _activeElementPropGroup.Value != group)
            {
                // The old tab's active InputText/slider is no longer submitted, so ImGui cannot
                // report its normal deactivation. Finish a real edit (or discard an untouched
                // activation) before any control in this tab can start a new gesture.
                FlushPendingElementEdit();
                _activeElementPropGroup = group;
            }
            ImGui.BeginChild("##UIAHudElement" + title + "Scroll", new Vector2(0f, -82f),
                false, ImGuiWindowFlags.None);
            HudPropDrawer.DrawGroup(_propScratch, group, beginEdit, commitEdit, cancelEdit,
                noteChanged);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        /// <summary>Generic widget for any config entry — the popup doesn't know panels.</summary>
        private static void DrawConfigWidget(ConfigEntryBase e)
        {
            if (e == null) return;
            string label = e.Definition.Key;
            var asBool = e as ConfigEntry<bool>;
            if (asBool != null) { Toggle(asBool, label); return; }
            var asFloat = e as ConfigEntry<float>;
            if (asFloat != null)
            {
                var range = e.Description?.AcceptableValues as AcceptableValueRange<float>;
                FloatSlider(asFloat, label,
                    range != null ? range.MinValue : 0f, range != null ? range.MaxValue : 10f);
                return;
            }
            var asInt = e as ConfigEntry<int>;
            if (asInt != null)
            {
                var range = e.Description?.AcceptableValues as AcceptableValueRange<int>;
                int v = asInt.Value;
                if (ImGui.SliderInt(label, ref v, range != null ? range.MinValue : 0,
                        range != null ? range.MaxValue : 100))
                    asInt.Value = v;
                return;
            }
            var asString = e as ConfigEntry<string>;
            if (asString != null && label.IndexOf("Font", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                FontCombo();
                return;
            }
            var asCurve = e as ConfigEntry<HudCurvature>;
            if (asCurve != null) CurvatureCombo();
        }

        // ------------------------------------------------------------------ pieces

        private static void DrawEdgeLightColour()
        {
            RgbConfig(HudConfig.FxEdgeLightColor, "  Edge-light colour##edgeLightColour",
                ref _edgeTintStr, ref _edgeTintVec);
        }

        /// <summary>Draw a hue-wheel editor for a config value stored as a hand-editable #RRGGBB string.</summary>
        private static void RgbConfig(ConfigEntry<string> entry, string label,
            ref string cachedText, ref Vector3 cachedColour)
        {
            if (entry == null) return;

            string text = entry.Value ?? "#FFFFFF";
            if (!string.Equals(text, cachedText, System.StringComparison.Ordinal))
            {
                Color parsed;
                if (!ColorUtility.TryParseHtmlString(text, out parsed)) parsed = Color.white;
                cachedText = text;
                cachedColour = new Vector3(parsed.r, parsed.g, parsed.b);
            }

            if (ImGui.ColorEdit3(label, ref cachedColour, ImGuiColorEditFlags.PickerHueWheel))
            {
                string hex = "#" + ColorUtility.ToHtmlStringRGB(
                    new Color(cachedColour.x, cachedColour.y, cachedColour.z, 1f));
                entry.Value = hex;
                cachedText = hex;
            }
        }

        private static void FrostDownsampleCombo()
        {
            if (HudConfig.FrostDownsample == null) return;

            float value = HudConfig.FrostDownsample.Value;
            int divisor = value < 3f ? 2 : value < 6f ? 4 : 8;
            string current = divisor == 2 ? "Half resolution (sharpest)"
                : divisor == 4 ? "Quarter resolution (balanced)"
                : "Eighth resolution (softest / cheapest)";
            if (ImGui.BeginCombo("Backdrop blur resolution", current))
            {
                if (ImGui.Selectable("Half resolution (sharpest)", divisor == 2))
                    HudConfig.FrostDownsample.Value = 2f;
                if (ImGui.Selectable("Quarter resolution (balanced)", divisor == 4))
                    HudConfig.FrostDownsample.Value = 4f;
                if (ImGui.Selectable("Eighth resolution (softest / cheapest)", divisor == 8))
                    HudConfig.FrostDownsample.Value = 8f;
                ImGui.EndCombo();
            }
        }

        private static void DrawBloomControls()
        {
            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f),
                HudBloomFx.Available
                    ? "BLOOM RENDERER READY"
                    : "BLOOM SHADER NOT LOADED — THESE CONTROLS ARE INACTIVE");
            Toggle(HudConfig.FxBloomOn, "Enable HUD bloom (HUD elements light each other)");
            if (HudConfig.FxBloomOn == null || !HudConfig.FxBloomOn.Value) return;

            ImGui.TextDisabled("Base glow");
            FloatSlider(HudConfig.FxBloomStrength, "  Strength##bloomBaseStrength", 0f, 3f);
            FloatSlider(HudConfig.FxBloomThreshold, "  Bright threshold##bloomBaseThreshold", 0f, 1.5f);
            FloatSlider(HudConfig.FxBloomKnee, "  Soft knee##bloomBaseKnee", 0f, 1f);
            IntSliderCfg(HudConfig.FxBloomBlurSteps, "  Reach / blur steps##bloomBaseSteps", 1, 5);
            FloatSlider(HudConfig.FxBloomSpread, "  Width fine-adjust##bloomBaseSpread", 0.5f, 3f);
            BloomResCombo();
            FloatSlider(HudConfig.FxBloomAnamorph,
                "  Streak shape (-1 vertical, +1 horizontal)##bloomBaseAnamorph", -1f, 1f);
            FloatSlider(HudConfig.FxBloomSaturation,
                "  Saturation (0 white-hot, 1 source hues)##bloomBaseSaturation", 0f, 2f);
            FloatSlider(HudConfig.FxBloomSatBias,
                "  Colour bias (+ favours coloured pixels)##bloomBaseBias", -1f, 1f);
            RgbConfig(HudConfig.FxBloomTint, "  Glow tint##bloomBaseTint",
                ref _bloomTintStr, ref _bloomTintVec);

            ImGui.Spacing();
            Toggle(HudConfig.FxBloomPulseOn, "Breathing bloom");
            if (HudConfig.FxBloomPulseOn != null && HudConfig.FxBloomPulseOn.Value)
            {
                FloatSlider(HudConfig.FxBloomPulseSpeed, "  Breaths per second##bloomPulseSpeed", 0.05f, 2f);
                FloatSlider(HudConfig.FxBloomPulseDepth, "  Breath depth##bloomPulseDepth", 0f, 1f);
            }

            Toggle(HudConfig.FxBloomReactOn, "State-reactive bloom");
            if (HudConfig.FxBloomReactOn != null && HudConfig.FxBloomReactOn.Value)
            {
                FloatSlider(HudConfig.FxBloomReactPower, "  Low suit power dimming##bloomReactPower", 0f, 1f);
                FloatSlider(HudConfig.FxBloomReactAlarm, "  Critical alarm response##bloomReactAlarm", 0f, 1f);
                FloatSlider(HudConfig.FxBloomReactBoot, "  Boot flare##bloomReactBoot", 0f, 1f);
            }

            ImGui.Spacing();
            Toggle(HudConfig.FxBloom2On, "Border / highlight bloom (second bright band)");
            if (HudConfig.FxBloom2On != null && HudConfig.FxBloom2On.Value)
            {
                FloatSlider(HudConfig.FxBloom2Threshold,
                    "  Highlight threshold##bloomHighlightThreshold", 0f, 1.5f);
                FloatSlider(HudConfig.FxBloom2SatBias,
                    "  Colour bias (+ favours borders)##bloomHighlightBias", -1f, 1f);
                FloatSlider(HudConfig.FxBloom2Strength,
                    "  Strength##bloomHighlightStrength", 0f, 3f);
                IntSliderCfg(HudConfig.FxBloom2Steps,
                    "  Reach / blur steps##bloomHighlightSteps", 1, 5);
                FloatSlider(HudConfig.FxBloom2Spread,
                    "  Width fine-adjust##bloomHighlightSpread", 0.5f, 3f);
                RgbConfig(HudConfig.FxBloom2Tint, "  Highlight tint##bloomHighlightTint",
                    ref _bloom2TintStr, ref _bloom2TintVec);
                ImGui.TextDisabled("Raise panel edge light, then set this threshold between borders and text.");
            }

            ImGui.TextDisabled("Measure real frame cost in Diagnostics or with: uiaprof ab bloom");
        }

        private static void PreviewTierCombo()
        {
            string current = !HudSystem.ForceTier.HasValue ? "Live (whatever you wear)"
                : HudSystem.ForceTier.Value == HudTier.Bare ? "BARE (no suit power)"
                : HudSystem.ForceTier.Value == HudTier.Suited ? "SUITED (full readout)"
                : "ROBOT";
            if (ImGui.BeginCombo("##previewTier", current))
            {
                if (ImGui.Selectable("Live (whatever you wear)", !HudSystem.ForceTier.HasValue))
                    HudSystem.ForceTier = null;
                if (ImGui.Selectable("BARE (no suit power)", HudSystem.ForceTier == HudTier.Bare))
                    HudSystem.ForceTier = HudTier.Bare;
                if (ImGui.Selectable("SUITED (full readout)", HudSystem.ForceTier == HudTier.Suited))
                    HudSystem.ForceTier = HudTier.Suited;
                if (ImGui.Selectable("ROBOT", HudSystem.ForceTier == HudTier.Robot))
                    HudSystem.ForceTier = HudTier.Robot;
                ImGui.EndCombo();
            }
        }

        /// <summary>Bloom bright-pass base resolution: Full = crisp hairline glow (priciest),
        /// Quarter = soft dreamy haze (cheapest). -1 in the cfg = legacy "fine detail" toggle
        /// mapping (half when on, quarter when off) — shown as its resolved value here.</summary>
        private static void BloomResCombo()
        {
            if (HudConfig.FxBloomRes == null) return;
            int res = HudConfig.FxBloomRes.Value;
            if (res < 0) // legacy: resolve the FineDetail toggle for display
                res = HudConfig.FxBloomFineDetail == null || HudConfig.FxBloomFineDetail.Value ? 1 : 2;
            string current = res == 0 ? "Full (crisp hairlines, priciest)"
                : res == 1 ? "Half (balanced)"
                : "Quarter (soft haze, cheapest)";
            if (ImGui.BeginCombo("  glow resolution", current))
            {
                if (ImGui.Selectable("Full (crisp hairlines, priciest)", res == 0)) HudConfig.FxBloomRes.Value = 0;
                if (ImGui.Selectable("Half (balanced)", res == 1)) HudConfig.FxBloomRes.Value = 1;
                if (ImGui.Selectable("Quarter (soft haze, cheapest)", res == 2)) HudConfig.FxBloomRes.Value = 2;
                ImGui.EndCombo();
            }
        }

        private static void CurvatureCombo()
        {
            var mode = HudConfig.Curvature.Value;
            string current = mode == HudCurvature.Flat ? "Flat (no curve)"
                : mode == HudCurvature.VertexWarp ? "A - Vertex warp (recommended)"
                : mode == HudCurvature.DomeProjection ? "B - Dome projection (RenderTexture)"
                : mode == HudCurvature.CurvedWorldCanvas ? "C - Curved world canvas (experimental)"
                : "D - Curved, steady (RenderTexture)";
            if (ImGui.BeginCombo("Curvature mode", current))
            {
                if (ImGui.Selectable("Flat (no curve)", mode == HudCurvature.Flat))
                    HudConfig.Curvature.Value = HudCurvature.Flat;
                if (ImGui.Selectable("A - Vertex warp (recommended)", mode == HudCurvature.VertexWarp))
                    HudConfig.Curvature.Value = HudCurvature.VertexWarp;
                if (ImGui.Selectable("B - Dome projection (RenderTexture)", mode == HudCurvature.DomeProjection))
                    HudConfig.Curvature.Value = HudCurvature.DomeProjection;
                if (ImGui.Selectable("C - Curved world canvas (experimental)", mode == HudCurvature.CurvedWorldCanvas))
                    HudConfig.Curvature.Value = HudCurvature.CurvedWorldCanvas;
                if (ImGui.Selectable("D - Curved, steady (RenderTexture)", mode == HudCurvature.CurvedRt))
                    HudConfig.Curvature.Value = HudCurvature.CurvedRt;
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(mode == HudCurvature.DomeProjection
                ? "Whole HUD renders to a texture on a dome grid; scanline colour applies."
                : mode == HudCurvature.CurvedWorldCanvas
                ? "The visor physically floats in front of the camera. Text uses the\ngame's ZTest-Always TMP shader; panels may clip into very close walls.\nKNOWN: swims as you move (far-from-origin precision) — use D instead."
                : mode == HudCurvature.CurvedRt
                ? "Mode C's curve rendered to a texture, shown full-screen: STEADY when\nyou move (no swim), no wall clipping. Scanline colour applies."
                : mode == HudCurvature.VertexWarp
                ? "Every element mesh bends toward the screen axis. Crisp text, zero cost."
                : "The baseline. Pick A, B, C or D to compare the visor projections.");
        }

        // The shared colours that box elements draw when "Follow global colours" is ticked. Shown in
        // their own section at the top so changing all boxes at once is one edit; skipped in the full
        // list below so a swatch never appears (and fights for its ImGui id) twice.
        private static readonly System.Collections.Generic.HashSet<string> _boxColourNames =
            new System.Collections.Generic.HashSet<string>
            {
                "HudPanelFill", "HudPanelBorder", "HudTextValue", "HudTextLabel", "HudTextDim",
                "HudGood", "HudWarn", "HudCritical",
            };

        /// <summary>Commit an interrupted palette gesture only when its final config values differ
        /// from the activation snapshot. This preserves redo after a click-without-edit (or a drag
        /// returned to its starting colour) and also makes window/tab closure a valid commit edge.</summary>
        private static void FlushPendingPaletteEdit()
        {
            var snapshot = _pendingUndo;
            _pendingUndo = null;
            if (snapshot == null || !PaletteChangedSince(snapshot)) return;
            HudPalette.History.PushUndo(snapshot);
        }

        private static bool PaletteChangedSince(Dictionary<string, string> snapshot)
        {
            if (snapshot.Count != HudPalette.All.Count) return true;
            for (int i = 0; i < HudPalette.All.Count; i++)
            {
                var entry = HudPalette.All[i];
                string before;
                if (!snapshot.TryGetValue(entry.Name, out before)
                    || !string.Equals(before, entry.Config.Value, System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void DrawColourControls()
        {
            ImGui.TextDisabled("Click a swatch for a colour wheel. The A slider is transparency.");
            if (ImGui.Button("Undo"))
            {
                FlushPendingPaletteEdit();
                HudPalette.History.Undo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
            {
                FlushPendingPaletteEdit();
                HudPalette.History.Redo();
            }
            ImGui.SameLine();
            ImGui.TextDisabled(HudPalette.History.CanUndo ? "" : "(nothing to undo)");
            ImGui.Spacing();

            _frameSnapshot = HudPalette.Snapshot();
            string hotSig = HudEditorMode.Active ? HudEditorMode.HotSignature : "";
            bool scrollTo = hotSig != _lastHotSig && hotSig.Length > 0;
            _lastHotSig = hotSig;

            // --- Global box colours: change these to re-tint every box that follows global. ---
            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "GLOBAL BOX COLOURS");
            ImGui.TextDisabled("Every box set to \"Follow global colours\" uses these — one edit re-tints them all.");
            ColorWheelHot(HudPalette.PanelFill, ref scrollTo);
            ColorWheelHot(HudPalette.PanelBorder, ref scrollTo);
            ColorWheelHot(HudPalette.TextValue, ref scrollTo);
            ColorWheelHot(HudPalette.TextLabel, ref scrollTo);
            ColorWheelHot(HudPalette.TextDim, ref scrollTo);
            ColorWheelHot(HudPalette.Good, ref scrollTo);
            ColorWheelHot(HudPalette.Warn, ref scrollTo);
            ColorWheelHot(HudPalette.Critical, ref scrollTo);
            ImGui.Separator();
            ImGui.TextDisabled("Other HUD colours (compass, hologram, edges, scanline...):");

            foreach (var entry in HudPalette.All)
            {
                if (_boxColourNames.Contains(entry.Name)) continue; // shown in the box section above
                ColorWheelHot(entry, ref scrollTo);
            }

            ImGui.Spacing();
            if (ImGui.Button("Reset all HUD colours to defaults"))
            {
                FlushPendingPaletteEdit();
                HudPalette.ResetToDefaults();
            }
        }

        /// <summary>A palette swatch that highlights (and scrolls into view) when its element is
        /// hovered in the editor — the shared draw for both the box section and the full list.</summary>
        private static void ColorWheelHot(HudPalette.Entry entry, ref bool scrollTo)
        {
            bool hot = HudEditorMode.Active && HudEditorMode.HotPalette.Contains(entry.Name);
            if (hot)
            {
                if (scrollTo) { ImGui.SetScrollHereY(0.3f); scrollTo = false; }
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.16f, 1f));
            }
            ColorWheel(entry);
            if (hot) ImGui.PopStyleColor();
        }

        private static void FontCombo()
        {
            string current = HudConfig.FontName.Value;
            if (string.IsNullOrEmpty(current)) current = "(game default)";
            if (ImGui.BeginCombo("HUD font", current))
            {
                if (ImGui.Selectable("(game default)", string.IsNullOrEmpty(HudConfig.FontName.Value)))
                    HudConfig.FontName.Value = "";
                foreach (var name in UI.Hud.HudText.AllFontNames())
                {
                    if (ImGui.Selectable(name, name == HudConfig.FontName.Value))
                        HudConfig.FontName.Value = name;
                }
                ImGui.EndCombo();
            }
        }

        private static void ColorWheel(HudPalette.Entry entry)
        {
            Color c = entry.Value;
            var v = new Vector4(c.r, c.g, c.b, c.a);
            const ImGuiColorEditFlags flags = ImGuiColorEditFlags.AlphaBar
                                            | ImGuiColorEditFlags.AlphaPreviewHalf
                                            | ImGuiColorEditFlags.PickerHueWheel;
            bool changed = ImGui.ColorEdit4(entry.Name, ref v, flags);
            // One drag = one undo step (same contract as the radial editor).
            if (ImGui.IsItemActivated())
            {
                // A prior picker may have disappeared on a tab/window transition without
                // deactivation. Preserve its step before taking ownership of the shared slot.
                FlushPendingPaletteEdit();
                _pendingUndo = _frameSnapshot;
            }
            if (changed)
                entry.Value = new Color(v.x, v.y, v.z, v.w);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FlushPendingPaletteEdit();
            }
            else if (ImGui.IsItemDeactivated())
            {
                _pendingUndo = null;
            }
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            bool v = entry.Value;
            if (ImGui.Checkbox(label, ref v)) entry.Value = v;
        }

        private static void FloatSlider(ConfigEntry<float> entry, string label, float min, float max)
        {
            float v = entry.Value;
            if (ImGui.SliderFloat(label, ref v, min, max)) entry.Value = v;
        }

        private static void IntSliderCfg(ConfigEntry<int> entry, string label, int min, int max)
        {
            int v = entry.Value;
            if (ImGui.SliderInt(label, ref v, min, max)) entry.Value = v;
        }
    }
}
