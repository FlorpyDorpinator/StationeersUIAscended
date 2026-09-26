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
            HudEditorWindow.ClampWindowToScreen(); // never let the window escape the screen
            if (RadialEditorMode.Active)
            {
                DrawEditorPanel();
                return;
            }

            ImGui.TextDisabled("Radials, radials, radials - hold the key, point, release.");
            ImGui.Separator();

            Toggle(UIAConfig.MasterEnable, "Master enable");
            ImGui.Spacing();

            if (ImGui.Button("Open radial editor"))
                RadialEditorMode.Enter();
            ImGui.SameLine();
            ImGui.TextDisabled("black screen + live example radials");
            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Radials", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // The per-wheel enable toggles retired in the post-0.9.2.5 play-test round — with the radial half on, the
                // toolbelt / tool / bag / equipment wheels are core functionality, not options.
                ImGui.TextDisabled("Toolbelt: hold " + UIAConfig.ToolbeltRadialKey.Value
                    + "   |   Tool: hold " + UIAConfig.ToolRadialKey.Value
                    + "   |   Bags: " + (UIAConfig.BagRadialTapOpens.Value ? "tap " : "hold ")
                    + UIAConfig.BagRadialKey.Value + "   |   Equipment: tap 1-6");
                Toggle(UIAConfig.BagRadialTapOpens, "Tap opens bag radial / hold shows scoreboard");
                Toggle(UIAConfig.ToolbeltShowStowEntries, "Show empty belt slots in the toolbelt radial");
                Toggle(UIAConfig.RadialMovementEnabled, "Keep moving (WASD + Space) while a radial is open");
                IntSlider(UIAConfig.HoldThresholdMs, "Hold threshold (ms)", 60, 600);
                ImGui.Separator();
                ImGui.TextDisabled("Bag presentation:");
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

            if (ImGui.CollapsingHeader("Radial Effects (0.9.0)"))
            {
                DrawEffectControls();
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
                ImGui.TextDisabled("Vanilla panels (hidden, never destroyed):");
                Toggle(UIAConfig.HideVanillaHands, "Hide vanilla hands panel");
                Toggle(UIAConfig.HideVanillaClothing, "Hide vanilla clothing panel");
                Toggle(UIAConfig.HideVanillaStatus, "Hide vanilla status panel");
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

            if (ImGui.CollapsingHeader("Effects (0.9.0)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawEffectControls();

            if (ImGui.CollapsingHeader("Hint bar (tooltip strip)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawHintBarControls();

            if (ImGui.CollapsingHeader("Colours", ImGuiTreeNodeFlags.DefaultOpen))
                DrawColourControls();
        }

        /// <summary>Everything the key-hint strip can be styled with, in ONE place: colour +
        /// transparency, shape, glass, frost and text. It lives in the radial EDITOR (not the UGUI
        /// menu) because that is where the strip is previewed live — the editor blacks the screen
        /// out, so the strip is pinned over a white swatch automatically while you are in here.</summary>
        private static void DrawHintBarControls()
        {
            ImGui.TextDisabled("Previewed over a white box while this editor is open.");
            ImGui.TextDisabled("(the editor blacks the screen out, so white is the honest backdrop)");
            ImGui.Spacing();

            // Own ImGui ID scope: every colour below ALSO appears in the "Colours" list further down
            // this same window, and two widgets with one label would share one ImGui ID.
            ImGui.PushID("hintbar");
            // Pre-edit state for the undo step (ColorWheel), taken HERE: this section draws before
            // the Colours list, so the snapshot that list takes is a frame old — or never taken at all
            // while that header is collapsed.
            _frameSnapshot = Overlay.RadialPalette.Snapshot();
            ImGui.TextDisabled("Colour + transparency (the A slider IS the transparency):");
            ColorWheel(Overlay.RadialPalette.HintBarFill);
            ColorWheel(Overlay.RadialPalette.HintBarBorder);
            ColorWheel(Overlay.RadialPalette.HintBarText);

            // B14: the LIVE strip is the CURVED one (under the wheel) and the action word over it —
            // their four colours belong here, where players look for the hint look, not only in the
            // long general list. The flat colours above style the flat fallback / this preview.
            ImGui.Spacing();
            ImGui.TextDisabled("Curved hint strip + action word - AUTO follows the theme:");
            ColorWheel(Overlay.RadialPalette.ArcPlateFill);
            ColorWheel(Overlay.RadialPalette.ArcPlateBorder);
            ColorWheel(Overlay.RadialPalette.ArcText);
            ColorWheel(Overlay.RadialPalette.ArcAccent);
            ImGui.PopID();

            ImGui.Spacing();
            ImGui.TextDisabled("Shape:");
            FloatSlider(UIAConfig.HintBarCorner, "Corner rounding (half the height = a pill)", 0f, 24f, true);
            FloatSlider(UIAConfig.HintBarHeight, "Height (px)", 16f, 56f, true);
            FloatSlider(UIAConfig.HintBarPadding, "Side padding (px)", 0f, 80f, true);
            FloatSlider(UIAConfig.HintBarDrop, "Drop below the wheel (px)", 0f, 200f, true);
            FloatSlider(UIAConfig.HintBarBorderWidth, "Border width (0 = none)", 0f, 6f, true);
            FloatSlider(UIAConfig.HintBarFeather, "Edge softness / AA (px)", 0f, 4f, true);

            ImGui.Spacing();
            ImGui.TextDisabled("Glass + effects:");
            Toggle(UIAConfig.HintBarFrost, "Frosted glass (blur the screen behind it)", true);
            FloatSlider(UIAConfig.HintBarFrostStrength, "  frost strength", 0f, 1f, true);
            if (UIAConfig.HintBarFrost.Value)
            {
                // Same diagnosis the radial's own frost prints, so an inactive blur is never a mystery.
                var warn = new Vector4(1f, 0.72f, 0.25f, 1f);
                bool tierC = UI.Hud.HudConfig.FxTierC != null && UI.Hud.HudConfig.FxTierC.Value;
                if (!tierC)
                    ImGui.TextColored(warn, "Frost inactive: turn ON HUD Tier C (F9 > Effects).");
                else if (!Core.HudShaderStore.TierBAvailable)
                    ImGui.TextColored(warn, "Frost inactive: shader bundle not loaded (restart after a rebuild).");
                else if (!UI.Hud.HudBackdrop.Active)
                    ImGui.TextColored(warn, "Frost warming up: needs the Visor HUD running (Flat/VertexWarp).");
                else
                    ImGui.TextDisabled("Frost active.");
            }
            FloatSlider(UIAConfig.HintBarSheen, "Glass sheen (surface top-light)", 0f, 1f, true);
            FloatSlider(UIAConfig.HintBarSpec, "Glass edge light", 0f, 1f, true);
            ImGui.TextDisabled("  edge light lights the BORDER RUN - it can read as a rim at width 0.");
            FloatSlider(UIAConfig.HintBarGlow, "Glow (0 = off)", 0f, 2f, true);
            FloatSlider(UIAConfig.HintBarGlowWidth, "  glow radius (px)", 6f, 160f, true);

            ImGui.Spacing();
            ImGui.TextDisabled("Text:");
            FloatSlider(UIAConfig.HintBarFontSize, "Text size - ALSO sets the strip's length", 7f, 24f, true);
            Toggle(UIAConfig.HintBarBold, "Bold", true);
            ImGui.TextDisabled("  the strip auto-sizes to its text, so there is no separate length.");
        }

        /// <summary>The 0.9.0 radial effect controls (frosted-glass backdrop + glass mesh look),
        /// shared by the main window and the editor panel so the two can never drift. Mirrors the
        /// HUD editor's "(inactive)" language so it's obvious WHY frost isn't showing.</summary>
        private static void DrawEffectControls()
        {
            bool tierC = UI.Hud.HudConfig.FxTierC != null && UI.Hud.HudConfig.FxTierC.Value;
            bool bundle = Core.HudShaderStore.TierBAvailable;

            Toggle(UIAConfig.RadialFrost, "Frosted glass behind wedges (blurred screen)", true);
            FloatSlider(UIAConfig.RadialFrostStrength, "  frost strength", 0f, 1f, true);
            if (UIAConfig.RadialFrost.Value)
            {
                var warn = new Vector4(1f, 0.72f, 0.25f, 1f);
                if (!tierC)
                    ImGui.TextColored(warn, "Frost inactive: turn ON HUD Tier C (F9 > Effects / Visor HUD editor).");
                else if (!bundle)
                    ImGui.TextColored(warn, "Frost inactive: shader bundle not loaded (restart the game after a rebuild).");
                else if (!UI.Hud.HudBackdrop.Active)
                    ImGui.TextColored(warn, "Frost warming up: needs Flat/VertexWarp HUD curvature + the Visor HUD running.");
                else
                    ImGui.TextDisabled("Frost active.");
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Glass look (pure vertex colour — works without frost):");
            FloatSlider(UIAConfig.RadialSheen, "Glass sheen (whiten toward the rim)", 0f, 1f, true);
            FloatSlider(UIAConfig.RadialEdgeLight, "Edge light (rim faces the key light)", 0f, 1f, true);
        }

        /// <summary>Every live-tunable visual: shared between the normal Radials section and
        /// the editor panel so the two can never drift apart.</summary>
        private static void DrawVisualControls()
        {
            // Every knob here is in HudTheme's "radial:" include-list (design §5.2) — all LOOK,
            // no behaviour — so every one of them marks the active profile's theme dirty.
            FloatSlider(UIAConfig.RadialOuterRadius, "Radial size", 120f, 480f, true);
            FloatSlider(UIAConfig.RadialInnerRadius, "Hub (center circle) size", 60f, 260f, true);
            FloatSlider(UIAConfig.RadialSatelliteScale, "Child radial size", 0.5f, 1.6f, true);
            FloatSlider(UIAConfig.RadialIconRatio, "Icon size (fraction of wedge)", 0.15f, 1.1f, true);
            FloatSlider(UIAConfig.RadialBorderWidth, "Border thickness (px)", 0f, 10f, true);
            Toggle(UIAConfig.RadialSideBorders, "Borders on wedge SIDE edges (full outline)", true);
            FloatSlider(UIAConfig.RadialSideWidthInner, "Side line width at hub (px)", 0.5f, 12f, true);
            FloatSlider(UIAConfig.RadialSideWidthOuter, "Side line width at rim (px)", 0.5f, 12f, true);
            FloatSlider(UIAConfig.RadialWedgeGapDeg, "Gap between wedges (deg)", 0f, 6f, true);
            FloatSlider(UIAConfig.RadialEdgeFeather, "Edge softness / anti-aliasing (px)", 0f, 4f, true);
            FloatSlider(UIAConfig.RadialShineIntensity, "Shine intensity (0 = flat)", 0f, 2f, true);
            Toggle(UIAConfig.RadialDimShading, "Dim other wedges while one is highlighted", true);
            FloatSlider(UIAConfig.RadialDimStrength, "Dim strength", 0f, 1f, true);
            FloatSlider(UIAConfig.ParkedChipRadius, "Dragged-out item bubble size (px)", 16f, 80f, true);
            Toggle(UIAConfig.RadialUppercaseLabels, "ALL CAPS wedge labels", true);
            Toggle(UIAConfig.RadialShowStateText, "State under icons (battery %, kPa, counts)", true);
            Toggle(UIAConfig.RadialShowWedgeLabels, "Show item name under each icon", true);
            Toggle(UIAConfig.RadialShowBindingLabels, "Show bound-tool labels on reserved belt slots", true);
            FloatSlider(UIAConfig.RadialHubTitleSize, "Readout 1: title (bold)", 9f, 32f, true);
            FloatSlider(UIAConfig.RadialTextVerb, "Readout 2: action verb", 8f, 26f, true);
            FloatSlider(UIAConfig.RadialTextLabel, "Readout 3: item name", 8f, 26f, true);
            FloatSlider(UIAConfig.RadialTextSub, "Readout 4: detail / location", 8f, 24f, true);
            FloatSlider(UIAConfig.RadialTextWarn, "Readout 5: stat / warning", 8f, 24f, true);
            Toggle(UIAConfig.RadialRotateLongLabels, "Angle long wedge labels so they fit", true);
            FloatSlider(UIAConfig.RadialSatelliteHubRatio, "Child radial hub ratio", 0.2f, 0.6f, true);
            Toggle(UIAConfig.RadialDynamicReadoutText, "Dynamic child-hub text (no overlap)", true);
            FontCombo();
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
            {
                Overlay.RadialPalette.History.Undo();
                Features.HudProfileStore.MarkThemeChanged();
            }
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
            {
                Overlay.RadialPalette.History.Redo();
                Features.HudProfileStore.MarkThemeChanged();
            }
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
            {
                Overlay.RadialPalette.ResetToDefaults(); // pushes its own undo step
                Features.HudProfileStore.MarkThemeChanged();
            }
        }

        // SchemaCombo() retired in the post-0.9.2.5 play-test round: The Hub is THE control schema (FlorpyDorp — "there is
        // only one radial choice that makes sense"), so there is nothing left to choose. Tap MMB
        // opens the toolbelt sticky; hold MMB stays transient (LMB dives into branches, release
        // runs the hovered action).

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
                // RadialFontName is a "radial:" theme key (design §5.2) — an explicit pick marks
                // the active profile's theme dirty, same as every slider above.
                if (ImGui.Selectable("(auto: prefer bold)", string.IsNullOrEmpty(UIAConfig.RadialFontName.Value)))
                {
                    UIAConfig.RadialFontName.Value = "";
                    Features.HudProfileStore.MarkThemeChanged();
                }
                foreach (var name in UI.UnityRadialView.AllFontNames())
                {
                    if (ImGui.Selectable(name, name == UIAConfig.RadialFontName.Value))
                    {
                        UIAConfig.RadialFontName.Value = name;
                        Features.HudProfileStore.MarkThemeChanged();
                    }
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
            {
                entry.Value = new Color(v.x, v.y, v.z, v.w);
                // Radial colours belong to the active HUD profile's theme too (they follow the
                // chosen UI theme even when the visor HUD itself is turned off).
                Features.HudProfileStore.MarkThemeChanged();
            }
            // One drag = one undo step: capture the pre-edit state when the widget is
            // picked up, commit it when the edit finishes.
            if (ImGui.IsItemActivated())
                _pendingUndo = _frameSnapshot;
            if (ImGui.IsItemDeactivatedAfterEdit() && _pendingUndo != null)
            {
                Overlay.RadialPalette.History.PushUndo(_pendingUndo);
                _pendingUndo = null;
            }

            // B14: a theme-following entry (the curved hint / action-word colours) says whether it is
            // following the theme, and a pinned one gets a one-click way back (one undo step).
            if (entry.FollowsTheme)
            {
                ImGui.SameLine();
                if (entry.IsAuto)
                    ImGui.TextDisabled("(AUTO)");
                else
                {
                    ImGui.PushID(entry.Name); // one "AUTO" button per entry, no per-frame label concat
                    if (ImGui.SmallButton("AUTO"))
                    {
                        Overlay.RadialPalette.History.PushUndo(Overlay.RadialPalette.Snapshot());
                        entry.Config.Value = Overlay.RadialPalette.Auto;
                        Features.HudProfileStore.MarkThemeChanged();
                    }
                    ImGui.PopID();
                }
            }
        }

        /// <summary><paramref name="marksTheme"/> = this entry belongs to the "radial:"/hint-bar
        /// theme family (see <see cref="UI.Hud.HudTheme"/>'s include-list) — an edit restamps the
        /// active HUD profile's theme so it travels with the profile, the same contract HUD
        /// colours already have. False (default) for the many BEHAVIOUR toggles this same helper
        /// draws (master enable, per-wheel enables, hint-fade, …), which must never restamp a
        /// theme on their own.</summary>
        private static void Toggle(ConfigEntry<bool> entry, string label, bool marksTheme = false)
        {
            bool v = entry.Value;
            if (ImGui.Checkbox(label, ref v))
            {
                entry.Value = v;
                if (marksTheme) Features.HudProfileStore.MarkThemeChanged();
            }
        }

        private static void IntSlider(ConfigEntry<int> entry, string label, int min, int max)
        {
            int v = entry.Value;
            if (ImGui.SliderInt(label, ref v, min, max)) entry.Value = v;
        }

        /// <summary>See <see cref="Toggle"/>'s <c>marksTheme</c> remarks — same contract.</summary>
        private static void FloatSlider(ConfigEntry<float> entry, string label, float min, float max, bool marksTheme = false)
        {
            float v = entry.Value;
            if (ImGui.SliderFloat(label, ref v, min, max))
            {
                entry.Value = v;
                if (marksTheme) Features.HudProfileStore.MarkThemeChanged();
            }
        }
    }
}
