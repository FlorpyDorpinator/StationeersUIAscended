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
        public override void OnClose() { }

        private static string _lastHotSig = "";
        private static Dictionary<string, string> _frameSnapshot;
        private static Dictionary<string, string> _pendingUndo;
        // Bloom-tint swatch cache: config stores a #RRGGBB string, the wheel wants a vector.
        private static string _bloomTintStr;
        private static Vector3 _bloomTintVec = Vector3.one;

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
            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "VISOR HUD EDITOR");
            ImGui.TextDisabled("Click any HUD element on screen to edit it in place.");
            ImGui.TextDisabled("Hover an element - its colours light up ORANGE below.");
            ImGui.Spacing();
            if (ImGui.Button("Exit editor  (or press Escape)"))
                StationeersUIMod.Instance?.ToggleHudEditor();
            ImGui.Separator();

            PreviewTierCombo();
            if (ImGui.Button("Test power-death flicker"))
                HudSystem.TestPowerDeath();
            ImGui.SameLine();
            if (ImGui.Button("Test boot sequence"))
                HudSystem.TestBoot();
            ImGui.Spacing();

            if (HudSystem.DocumentMode)
                DrawDesignerSection();

            if (ImGui.CollapsingHeader("Curvature (A / B / C)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                CurvatureCombo();
                FloatSlider(HudConfig.CurveStrength, "Curve strength (0 flat - 1 fishbowl)", 0f, 1f);
                Toggle(HudConfig.CurveInvert, "Invert curve direction");
                if (HudConfig.Curvature.Value == HudCurvature.CurvedWorldCanvas
                    || HudConfig.Curvature.Value == HudCurvature.CurvedRt)
                    FloatSlider(HudConfig.WorldCanvasDistance, "Visor distance (m)", 0.25f, 2f);
            }

            if (ImGui.CollapsingHeader("Panels & behavior"))
            {
                Toggle(HudConfig.VisorHudEnabled, "Visor HUD enabled");
                Toggle(HudConfig.UseDocumentHud, "Document HUD (the designer; off = legacy 0.5.0 panels)");
                if (!HudSystem.DocumentMode)
                {
                    Toggle(HudConfig.ShowTopBar, "Top status bar");
                    Toggle(HudConfig.ShowCompass, "Compass ribbon");
                    Toggle(HudConfig.ShowEquipment, "Equipment column (1-6)");
                    Toggle(HudConfig.ShowHands, "Hand boxes");
                    Toggle(HudConfig.ShowVitals, "Vitals card / felt senses");
                    Toggle(HudConfig.ShowHologram, "Player hologram in vitals card");
                }
                Toggle(HudConfig.ShowVignette, "Visor-edge vignette");
                ImGui.Separator();
                Toggle(HudConfig.DiegeticTiers, "Diegetic tiers (no suit power = words only)");
                Toggle(HudConfig.FlickerAnimations, "Flicker animations (off/boot/death)");
                Toggle(HudConfig.LowPowerDropouts, "Low-power dropout glitches");
                FloatSlider(HudConfig.LowPowerThreshold, "Low-power threshold (%)", 0f, 40f);
                ImGui.Separator();
                ImGui.TextDisabled("Vanilla panels (hidden, never destroyed):");
                Toggle(UIAConfig.HideVanillaHands, "Hide vanilla hands panel");
                Toggle(UIAConfig.HideVanillaClothing, "Hide vanilla clothing panel");
                Toggle(UIAConfig.HideVanillaStatus, "Hide vanilla status panel");
                Toggle(UIAConfig.HideVanillaPlayerState, "Hide vanilla instrument cluster (bottom-right)");
                Toggle(HudConfig.LegacyImGuiHud, "Use the legacy ImGui HUD instead");
            }

            if (ImGui.CollapsingHeader("Power-transition glitch (global)"))
            {
                Toggle(HudConfig.GlitchEnabled, "Enable glitch (tear/shake) on power down / off / on");
                FloatSlider(HudConfig.GlitchDuration, "Duration (seconds)", 0.1f, 4f);
                FloatSlider(HudConfig.GlitchIntensity, "Severity", 0f, 1f);
                Toggle(HudConfig.GlitchOnPowerDown, "Fire on power DOWN / suit removed");
                Toggle(HudConfig.GlitchOnPowerUp, "Fire on power UP / boot");
                if (ImGui.Button("Test glitch now"))
                    HudGlitch.TriggerTest();
                ImGui.TextDisabled("Per-element opt-in + strength: click an element, see its");
                ImGui.TextDisabled("'Effects' section (also controls Death collapse and Warp).");
            }

            if (ImGui.CollapsingHeader("Effects (global) — 0.9.0"))
            {
                ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "TIER A — mesh effects (cheap)");
                Toggle(HudConfig.FxTierA, "Enable Tier A (all mesh effects)");
                Toggle(HudConfig.FxHairlinesOn, "Hairlines (sub-1px lines fade, not vanish)");
                FloatSlider(HudConfig.FxHairlineMin, "  thinnest line (px)", 0.05f, 1f);
                Toggle(HudConfig.FxEdgeLightOn, "Edge light (borders + lines)");
                FloatSlider(HudConfig.FxEdgeLight, "  edge-light strength", 0f, 2f);
                FloatSlider(HudConfig.FxEdgeRipple, "  ripple (irregular shimmer)", 0f, 2.5f);
                FloatSlider(HudConfig.FxEdgeRippleFreq, "  ripple frequency", 0.5f, 8f);
                Toggle(HudConfig.FxPulseOn, "Pulse (elements still opt in individually)");
                FloatSlider(HudConfig.FxPulseSpeed, "  pulse speed (Hz)", 0.05f, 3f);
                FloatSlider(HudConfig.FxPulseDepth, "  pulse depth", 0f, 1f);
                ImGui.TextDisabled("The concept-art look (global defaults; per-element overrides in each popup):");
                Toggle(HudConfig.FxBorderFadeOn, "Border fade (unlit sections dissolve)");
                FloatSlider(HudConfig.FxBorderFade, "  border fade amount", 0f, 1f);
                Toggle(HudConfig.FxSoftEdgeOn, "Soft edge (boxes melt together)");
                FloatSlider(HudConfig.FxSoftEdge, "  soft edge width (px)", 0f, 48f);
                Toggle(HudConfig.FxGlowOn, "Glow halo");
                FloatSlider(HudConfig.FxGlow, "  glow outward strength", 0f, 2f);
                FloatSlider(HudConfig.FxGlowInner, "  glow inward strength (into the box)", 0f, 2f);
                FloatSlider(HudConfig.FxGlowWidth, "  glow width (px)", 6f, 160f);
                FloatSlider(HudConfig.FxGlowDiffuse, "  glow diffuseness (haze)", 0f, 1f);

                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f),
                    Core.HudShaderStore.TierBAvailable
                        ? "TIER B — shader effects"
                        : "TIER B — shader effects (bundle not loaded — inactive)");
                Toggle(HudConfig.FxTierB, "Enable Tier B (all shader effects)");
                Toggle(HudConfig.FxShineOn, "Shine sweep");
                FloatSlider(HudConfig.FxShine, "  shine strength", 0f, 2f);
                FloatSlider(HudConfig.FxShinePeriod, "  shine period (seconds)", 2f, 60f);
                Toggle(HudConfig.FxIridOn, "Iridescence (rim rainbow)");
                FloatSlider(HudConfig.FxIridescence, "  iridescence strength", 0f, 1f);
                Toggle(HudConfig.FxChromaOn, "Chromatic aberration (needs Tier C frost)");
                FloatSlider(HudConfig.FxChroma, "  chroma strength", 0f, 1f);
                Toggle(HudConfig.FxDissolveBoot, "Dissolve reveal on boot/power transitions");

                ImGui.Separator();
                ImGui.TextColored(new Vector4(1f, 0.72f, 0.25f, 1f), "TIER C — frosted glass (EXPERIMENTAL)");
                Toggle(HudConfig.FxTierC, "Enable frosted-glass backdrop (Flat/Warp curvature only)");
                FloatSlider(HudConfig.FrostDarken, "Frost darkening", 0f, 1f);
                IntSliderCfg(HudConfig.FrostUpdateEveryN, "Re-blur every N frames", 1, 8);
                ImGui.TextDisabled("Downsample + tint live in the config (F10) / cfg file.");

                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f),
                    HudBloomFx.Available
                        ? "HUD BLOOM — elements light each other"
                        : "HUD BLOOM — elements light each other (bundle not loaded — inactive)");
                Toggle(HudConfig.FxBloomOn, "Enable HUD bloom (routes the HUD through a render texture)");
                FloatSlider(HudConfig.FxBloomStrength, "  bloom strength", 0f, 3f);
                FloatSlider(HudConfig.FxBloomThreshold, "  bright threshold", 0f, 1.5f);
                FloatSlider(HudConfig.FxBloomKnee, "  soft knee", 0f, 1f);
                IntSliderCfg(HudConfig.FxBloomBlurSteps, "  blur steps (reach doubles per step)", 1, 5);
                FloatSlider(HudConfig.FxBloomSpread, "  spread (continuous width fine-adjust)", 0.5f, 3f);
                Toggle(HudConfig.FxBloomFineDetail, "  fine-detail bloom (thin borders + lines glow too)");
                FloatSlider(HudConfig.FxBloomSaturation, "  glow saturation (0 white-hot, 1 own hues)", 0f, 2f);
                // Glow tint: the standard hue-wheel swatch (same control as the palette rows).
                // Stored as #RRGGBB in the cfg so the file stays hand-editable; the cache keeps
                // the per-frame path parse-free while the header is open.
                if (HudConfig.FxBloomTint != null)
                {
                    string ts = HudConfig.FxBloomTint.Value ?? "#FFFFFF";
                    if (!ReferenceEquals(ts, _bloomTintStr))
                    {
                        _bloomTintStr = ts;
                        Color tc;
                        if (!ColorUtility.TryParseHtmlString(ts, out tc)) tc = Color.white;
                        _bloomTintVec = new Vector3(tc.r, tc.g, tc.b);
                    }
                    if (ImGui.ColorEdit3("  glow tint", ref _bloomTintVec, ImGuiColorEditFlags.PickerHueWheel))
                    {
                        string hex = "#" + ColorUtility.ToHtmlStringRGB(
                            new Color(_bloomTintVec.x, _bloomTintVec.y, _bloomTintVec.z, 1f));
                        HudConfig.FxBloomTint.Value = hex;
                        _bloomTintStr = hex;
                    }
                }

                ImGui.TextDisabled("Measure any of this with the Profiler below (uiaprof ab <effect>).");
                if (ImGui.Button("Reset ALL per-element effect overrides"))
                    HudEditorMode.ResetAllElementEffects();
                ImGui.TextDisabled("Removes every element's own fx settings (one undo step).");
            }

            if (ImGui.CollapsingHeader("Profiler"))
            {
                bool vis = Profiling.ProfilicusUniversalis.IsVisible;
                if (ImGui.Button(vis ? "Hide profiler window" : "Show profiler window"))
                    Profiling.ProfilicusUniversalis.SetVisible(!vis);
                ImGui.SameLine();
                if (ImGui.Button("Snapshot##prof"))
                    Profiling.ProfilicusUniversalis.SaveSnapshot();
                ImGui.TextDisabled("Console: uiaprof [on|off|clear|save|ab <effect>] — ab measures an");
                ImGui.TextDisabled("effect's real frame cost (ON vs OFF) and prints the delta.");
            }

            if (HudSystem.DocumentMode)
            {
                if (ImGui.CollapsingHeader("Global style"))
                {
                    FloatSlider(HudConfig.HudScale, "Overall HUD scale", 0.6f, 1.6f);
                    FloatSlider(HudConfig.CornerRadius, "Default corner rounding (px)", 0f, 28f);
                    FloatSlider(HudConfig.BorderWidth, "Default line thickness (px)", 0f, 6f);
                    FloatSlider(HudConfig.EdgeFeather, "Edge softness / AA (px)", 0f, 4f);
                    FloatSlider(HudConfig.GlassSheen, "Default glass sheen", 0f, 1f);
                    FloatSlider(HudConfig.GlassEdge, "Default glass edge light", 0f, 1f);
                    ImGui.TextDisabled("Per-element glass overrides this (set it to -1 to follow these).");
                    if (ImGui.Button("Make ALL elements follow global glass"))
                        HudEditorMode.ResetAllGlassToGlobal();
                    ImGui.TextDisabled("Clears every element's own sheen/edge (colours & layout untouched).");
                }
            }
            else if (ImGui.CollapsingHeader("Layout & sizes"))
            {
                FloatSlider(HudConfig.HudScale, "Overall HUD scale", 0.6f, 1.6f);
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
                FloatSlider(HudConfig.CornerRadius, "Panel corner rounding (px)", 0f, 28f);
                FloatSlider(HudConfig.BorderWidth, "Panel line thickness (px)", 0f, 6f);
                FloatSlider(HudConfig.EdgeFeather, "Edge softness / AA (px)", 0f, 4f);
                FloatSlider(HudConfig.GlassSheen, "Default glass sheen", 0f, 1f);
                FloatSlider(HudConfig.GlassEdge, "Default glass edge light", 0f, 1f);
            }

            if (ImGui.CollapsingHeader("Text"))
            {
                FontCombo();
                FloatSlider(HudConfig.FontScale, "Font scale (all HUD text)", 0.6f, 1.8f);
                FloatSlider(HudConfig.LabelFontSize, "Label size (PRESSURE, HELMET...)", 7f, 22f);
                FloatSlider(HudConfig.ValueFontSize, "Value size (101 kPa...)", 10f, 30f);
                FloatSlider(HudConfig.CompassFontSize, "Compass text size", 8f, 22f);
                FloatSlider(HudConfig.VitalsRowFontSize, "Vitals row size", 9f, 22f);
                FloatSlider(HudConfig.BareWordFontSize, "Felt-sense word size", 12f, 36f);
            }

            if (ImGui.CollapsingHeader("Colours", ImGuiTreeNodeFlags.DefaultOpen))
                DrawColourControls();
        }

        // ------------------------------------------------------------------ designer

        private static int _addTypeIndex;
        private static string _saveAsName = "";
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

        /// <summary>The HUD Designer controls: grid, add/draw, undo, selection actions,
        /// and the layout-profile manager. Only shown in document mode.</summary>
        private void DrawDesignerSection()
        {
            if (!ImGui.CollapsingHeader("Designer", ImGuiTreeNodeFlags.DefaultOpen)) return;

            ImGui.TextDisabled("Click an element to select - drag to move, corners resize.");
            ImGui.TextDisabled("Del removes - Ctrl+D duplicates - Ctrl+Z / Ctrl+Y undo/redo.");
            ImGui.TextDisabled("Ctrl+drag = box-select many - arrow keys nudge (Shift = grid).");
            ImGui.Spacing();

            // Debug previews: fill the HUD so you can arrange the worst case.
            Toggle(HudConfig.DebugShowAll, "DEBUG: show everything (all vitals/moodlets/instruments)");
            Toggle(HudConfig.DebugShowAllBare, "DEBUG: show everything, power-off (bare) layout");
            ImGui.Spacing();

            Toggle(HudConfig.GridSnapEnabled, "Snap to grid (hold Alt to bypass)");
            Toggle(HudConfig.ShowGrid, "Show grid");
            FloatSlider(HudConfig.GridSnapSize, "Grid size (px)", 2f, 64f);
            ImGui.Spacing();

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
            ImGui.Spacing();

            // (No BeginDisabled in the game's ImGui binding — dead buttons just no-op.)
            if (ImGui.Button("Undo##doc") && UI.Hud.HudDocumentHistory.CanUndo)
                HudEditorMode.DoUndo();
            ImGui.SameLine();
            if (ImGui.Button("Redo##doc") && UI.Hud.HudDocumentHistory.CanRedo)
                HudEditorMode.DoRedo();

            var sel = HudEditorMode.SelectedElement;
            if (sel != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Duplicate")) HudEditorMode.DuplicateSelected();
                ImGui.SameLine();
                if (ImGui.Button("Delete")) HudEditorMode.DeleteSelected();

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
            ImGui.Spacing();
            DrawProfilesSection();
            ImGui.Separator();
        }

        private void DrawProfilesSection()
        {
            ImGui.TextDisabled("Layout profiles (shareable XML):");
            string active = HudEditorMode.ActiveProfileName();
            var names = Features.HudProfileStore.ListProfiles();
            ImGui.SetNextItemWidth(200f);
            if (ImGui.BeginCombo("##profile", active))
            {
                foreach (var n in names)
                {
                    if (ImGui.Selectable(n, string.Equals(n, active, System.StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(n, active, System.StringComparison.OrdinalIgnoreCase))
                    {
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
            ImGui.SameLine();
            if (ImGui.Button("Open folder"))
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Features.HudProfileStore.Dir); }
                catch { }
            }

            ImGui.SetNextItemWidth(200f);
            ImGui.InputText("##saveas", ref _saveAsName, 48);
            ImGui.SameLine();
            if (ImGui.Button("Save as") && !string.IsNullOrEmpty(_saveAsName))
            {
                // Sanitize BEFORE remembering the name: the store strips illegal chars
                // for the file, and a config name that kept them would miss the file on
                // the next launch and silently regenerate the default.
                string clean = _saveAsName;
                foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                    clean = clean.Replace(bad.ToString(), "");
                clean = clean.Trim();
                var doc = Features.HudProfileStore.Active;
                if (!string.IsNullOrEmpty(clean) && doc != null
                    && Features.HudProfileStore.Save(doc, clean))
                {
                    if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = clean;
                    Features.HudProfileStore.SetActive(doc, clean);
                    _saveAsName = "";
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
                OutlineRect(dl, r, selCol, 1.8f);
                for (int i = 0; i < 8; i++)
                {
                    var s = ToImGui(HudEditorMode.HandlePoint(r, i));
                    dl.AddRectFilled(new Vector2(s.x - 4f, s.y - 4f), new Vector2(s.x + 4f, s.y + 4f), handleCol);
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

            if (HudEditorMode.DrawingLine)
            {
                var pts = HudEditorMode.DrawPoints;
                for (int i = 0; i < pts.Count; i++)
                {
                    var s = ToImGui(pts[i]);
                    dl.AddCircleFilled(s, 3.5f, selCol, 12);
                    if (i > 0) dl.AddLine(ToImGui(pts[i - 1]), s, selCol, 2f);
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

        public static void DrawPopupOverlay()
        {
            if (!HudEditorMode.Active) return;

            DrawGizmos();

            // Document mode: the selected ELEMENT gets the generic property popup.
            if (HudSystem.DocumentMode)
            {
                var el = HudEditorMode.SelectedElement;
                if (el == null || el.Def == null) return;
                bool elMoved = _elementPopupStamp != HudEditorMode.ElementStamp;
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
                    if (elMoved) _pendingElementUndo = null;
                    HudPropDrawer.DrawAll(_propScratch,
                        onBeginEdit: () =>
                        {
                            var doc = Features.HudProfileStore.Active;
                            _pendingElementUndo = doc != null ? doc.Clone() : null;
                        },
                        onCommitted: () =>
                        {
                            if (_pendingElementUndo != null)
                            {
                                UI.Hud.HudDocumentHistory.Push(_pendingElementUndo);
                                _pendingElementUndo = null;
                            }
                            Features.HudProfileStore.MarkChanged();
                        });
                    // Live feedback only while a widget is actually being edited — an
                    // idle popup must not rebuild the element's meshes every frame.
                    bool editing = false;
                    try { editing = ImGui.IsAnyItemActive(); } catch { }
                    if (editing) HudSystem.RelayoutElement(el);
                    ImGui.Separator();
                    if (ImGui.Button("Duplicate##pop")) HudEditorMode.DuplicateSelected();
                    ImGui.SameLine();
                    if (ImGui.Button("Delete##pop")) HudEditorMode.DeleteSelected();
                }
                ImGui.End();
                if (!elOpen) HudEditorMode.ClearElementSelection();
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

        private static void PreviewTierCombo()
        {
            string current = !HudSystem.ForceTier.HasValue ? "Live (whatever you wear)"
                : HudSystem.ForceTier.Value == HudTier.Bare ? "BARE (no suit power)"
                : HudSystem.ForceTier.Value == HudTier.Suited ? "SUITED (full readout)"
                : "ROBOT";
            if (ImGui.BeginCombo("Preview tier", current))
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
            // Per-tier layout: while previewing BARE, dragging/resizing (and the X/Y/W/H fields)
            // edit the BARE-mode layout of any element shown in bare — turn on "Separate bare-mode
            // layout" in an element's popup for it to diverge from its suit position. Gated on the
            // exact signal that routes writes, so the cue can never disagree with what's edited.
            if (UI.Hud.HudElementView.EditBareTier)
                ImGui.TextDisabled("Editing BARE layout — moves bare-mode positions.");
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

        private static void DrawColourControls()
        {
            ImGui.TextDisabled("Click a swatch for a colour wheel. The A slider is transparency.");
            if (ImGui.Button("Undo")) HudPalette.History.Undo();
            ImGui.SameLine();
            if (ImGui.Button("Redo")) HudPalette.History.Redo();
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
            if (HudSystem.DocumentMode)
            {
                if (ImGui.Button("Make ALL boxes follow global"))
                    HudEditorMode.SetAllFollowGlobal(true);
                ImGui.SameLine();
                if (ImGui.Button("Give each box its own"))
                    HudEditorMode.SetAllFollowGlobal(false);
            }
            ImGui.Separator();
            ImGui.TextDisabled("Other HUD colours (compass, hologram, edges, scanline...):");

            foreach (var entry in HudPalette.All)
            {
                if (_boxColourNames.Contains(entry.Name)) continue; // shown in the box section above
                ColorWheelHot(entry, ref scrollTo);
            }

            ImGui.Spacing();
            if (ImGui.Button("Reset all HUD colours to defaults"))
                HudPalette.ResetToDefaults();
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
            if (ImGui.ColorEdit4(entry.Name, ref v, flags))
                entry.Value = new Color(v.x, v.y, v.z, v.w);
            // One drag = one undo step (same contract as the radial editor).
            if (ImGui.IsItemActivated())
                _pendingUndo = _frameSnapshot;
            if (ImGui.IsItemDeactivatedAfterEdit() && _pendingUndo != null)
            {
                HudPalette.History.PushUndo(_pendingUndo);
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
