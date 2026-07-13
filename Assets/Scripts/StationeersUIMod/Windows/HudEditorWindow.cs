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

        public override void DrawContent()
        {
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
                if (HudConfig.Curvature.Value == HudCurvature.CurvedWorldCanvas)
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

            if (HudSystem.DocumentMode)
            {
                if (ImGui.CollapsingHeader("Global style"))
                {
                    FloatSlider(HudConfig.HudScale, "Overall HUD scale", 0.6f, 1.6f);
                    FloatSlider(HudConfig.CornerRadius, "Default corner rounding (px)", 0f, 28f);
                    FloatSlider(HudConfig.BorderWidth, "Default line thickness (px)", 0f, 6f);
                    FloatSlider(HudConfig.EdgeFeather, "Edge softness / AA (px)", 0f, 4f);
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
        };

        /// <summary>The HUD Designer controls: grid, add/draw, undo, selection actions,
        /// and the layout-profile manager. Only shown in document mode.</summary>
        private void DrawDesignerSection()
        {
            if (!ImGui.CollapsingHeader("Designer", ImGuiTreeNodeFlags.DefaultOpen)) return;

            ImGui.TextDisabled("Click an element to select - drag to move, corners resize.");
            ImGui.TextDisabled("Del removes - Ctrl+D duplicates - Ctrl+Z / Ctrl+Y undo/redo.");
            ImGui.Spacing();

            Toggle(HudConfig.GridSnapEnabled, "Snap to grid (hold Alt to bypass)");
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
        }

        private static void CurvatureCombo()
        {
            var mode = HudConfig.Curvature.Value;
            string current = mode == HudCurvature.Flat ? "Flat (no curve)"
                : mode == HudCurvature.VertexWarp ? "A - Vertex warp (recommended)"
                : mode == HudCurvature.DomeProjection ? "B - Dome projection (RenderTexture)"
                : "C - Curved world canvas (experimental)";
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
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(mode == HudCurvature.DomeProjection
                ? "Whole HUD renders to a texture on a dome grid; scanline colour applies."
                : mode == HudCurvature.CurvedWorldCanvas
                ? "The visor physically floats in front of the camera. Text uses the\ngame's ZTest-Always TMP shader; panels may clip into very close walls."
                : mode == HudCurvature.VertexWarp
                ? "Every element mesh bends toward the screen axis. Crisp text, zero cost."
                : "The baseline. Pick A, B or C to compare the three visor projections.");
        }

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

            foreach (var entry in HudPalette.All)
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

            ImGui.Spacing();
            if (ImGui.Button("Reset all HUD colours to defaults"))
                HudPalette.ResetToDefaults();
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
    }
}
