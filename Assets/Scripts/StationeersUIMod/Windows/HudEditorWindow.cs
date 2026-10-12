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
            CancelProfileAction();   // never re-open with a stale "are you sure" armed
            _profileCacheScanned = false;  // re-scan the folder once on the next open
            _shippedNameCache.Clear();     // and re-ask "is this one of ours" once on the next open
            _activeEditorTab = null;
            _activeEditorSubTab = null;
        }

        private static string _lastHotSig = "";
        private static Dictionary<string, string> _frameSnapshot;
        private static Dictionary<string, string> _pendingUndo;
        private static string _activeEditorTab;
        private static string _activeEditorSubTab;
        /// <summary>The two in-window text colours: a cyan section heading and an amber warning.</summary>
        private static readonly Vector4 Accent = new Vector4(0.25f, 0.85f, 0.93f, 1f);
        private static readonly Vector4 WarnCol = new Vector4(1f, 0.72f, 0.25f, 1f);
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

            // Push the active tab noticeably brighter than an idle/hovered one — the five tabs
            // (Build/Theme/Effects/View & Behavior/Diagnostics) otherwise read as near-identical
            // grey, which made the current tab hard to spot at a glance (FlorpyDorp). The SAME
            // colours are reused by every nested sub-tab bar (Theme / Effects) through
            // PushTabStyle, so a sub-tab is as easy to read as a top-level one.
            PushTabStyle();
            if (!ImGui.BeginTabBar("##UIAHudEditorTabs")) { PopTabStyle(); return; }
            if (ImGui.BeginTabItem("Build", TopTabFlags("Build")))
            {
                ActivateEditorTab("Build");
                ImGui.BeginChild("##UIAHudBuildTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawBuildTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Theme", TopTabFlags("Theme")))
            {
                ActivateEditorTab("Theme");
                ImGui.BeginChild("##UIAHudThemeTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawThemeTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Effects", TopTabFlags("Effects")))
            {
                ActivateEditorTab("Effects");
                ImGui.BeginChild("##UIAHudEffectsTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawEffectsTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("View & Behavior", TopTabFlags("View & Behavior")))
            {
                ActivateEditorTab("View & Behavior");
                ImGui.BeginChild("##UIAHudViewTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawViewBehaviorTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Diagnostics", TopTabFlags("Diagnostics")))
            {
                ActivateEditorTab("Diagnostics");
                ImGui.BeginChild("##UIAHudDiagnosticsTab", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
                DrawDiagnosticsTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
            PopTabStyle();
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

        /// <summary>Same contract as <see cref="ActivateEditorTab"/> for the NESTED sub-tab bars.
        /// It deliberately keeps its OWN field: sharing the top-level one would make the two bars
        /// disagree every frame (top-level writes "Theme", the sub-tab writes "Theme/Palette"),
        /// and the resulting flush-per-frame would split one colour drag into an undo step per
        /// frame instead of one step per gesture.</summary>
        private static void ActivateEditorSubTab(string tab)
        {
            if (string.Equals(_activeEditorSubTab, tab, System.StringComparison.Ordinal)) return;
            FlushPendingPaletteEdit();
            _activeEditorSubTab = tab;
        }

        // ---- jump-to-tab (0d) ---------------------------------------------------------------
        //
        // ActivateEditorSubTab only RECORDS which sub-tab is up; it cannot open one. The standard
        // ImGui way is a one-frame ImGuiTabItemFlags.SetSelected on BeginTabItem, and the game's
        // binding does expose it — verified 2026-07-26 by reflecting
        // rocketstation_Data/Managed/RG.ImGui.dll: ImGuiNET.ImGuiTabItemFlags.SetSelected = 2, and
        // both `BeginTabItem(string, ImGuiTabItemFlags)` and `BeginTabItem(string, ref bool,
        // ImGuiTabItemFlags)` exist.
        //
        // The request is consumed by the tab item it names, not by a frame counter, so a click in
        // the element popup while this window happens to be CLOSED still lands the moment the
        // window is next drawn. SetSelected makes BeginTabItem return true on the same frame, so
        // the top-level tab and its sub-tab resolve in one pass, outermost first.

        private static string _jumpTab;
        private static string _jumpSubTab;

        /// <summary>Ask the F9 window to open on <paramref name="tab"/> (and optionally the sub-tab
        /// registered under <paramref name="subTabId"/>). Called by the element popup's
        /// "Open F9 &gt; ..." rows. Holds no scene or document state, so it needs no hot-reload
        /// teardown beyond the window's own statics.</summary>
        internal static void RequestEditorTab(string tab, string subTabId)
        {
            _jumpTab = tab;
            _jumpSubTab = subTabId;
        }

        /// <summary>SetSelected exactly once, on the tab item that was asked for.</summary>
        private static ImGuiTabItemFlags JumpFlagsFor(ref string pending, string id)
        {
            if (pending == null || !string.Equals(pending, id, System.StringComparison.Ordinal))
                return ImGuiTabItemFlags.None;
            pending = null;
            return ImGuiTabItemFlags.SetSelected;
        }

        private static ImGuiTabItemFlags TopTabFlags(string tab)
            => JumpFlagsFor(ref _jumpTab, tab);

        private static ImGuiTabItemFlags SubTabFlags(string id)
            => JumpFlagsFor(ref _jumpSubTab, id);

        /// <summary>The tab colours shared by the top-level bar and every nested sub-tab bar.</summary>
        private static void PushTabStyle()
        {
            ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.10f, 0.11f, 0.14f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.20f, 0.48f, 0.66f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.16f, 0.60f, 0.84f, 1f));
        }

        private static void PopTabStyle() { ImGui.PopStyleColor(3); }

        /// <summary>Open a nested sub-tab and its own scrolling child, so a long group scrolls
        /// INSIDE the sub-tab instead of pushing the sub-tab bar off the top of the window.
        /// Returns false when the sub-tab is not the active one (nothing to draw, nothing to end).</summary>
        private static bool BeginSubTab(string title, string id)
        {
            if (!ImGui.BeginTabItem(title, SubTabFlags(id))) return false;
            ActivateEditorSubTab(id);
            ImGui.BeginChild(id + "Scroll", new Vector2(0f, 0f), false, ImGuiWindowFlags.None);
            return true;
        }

        private static void EndSubTab()
        {
            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        /// <summary>Null-safe read for the masters a sub-tab has to consult from OUTSIDE the block
        /// that owns them (the core-effects gate is now checked on three sub-tabs).</summary>
        private static bool On(ConfigEntry<bool> entry) { return entry != null && entry.Value; }

        private void DrawEditorToolbar()
        {
            ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), "VISOR HUD DESIGNER");
            ImGui.SameLine();
            if (Features.HudProfileStore.HasPendingSave)
                ImGui.TextColored(new Vector4(1f, 0.72f, 0.25f, 1f), "UI THEME - Saving...");
            else
                ImGui.TextDisabled("UI THEME - Saved");

            DrawProfilesSection();

            ImGui.TextDisabled("Preview:");
            ImGui.SameLine();
            // Fit the widest option ("Live (whatever you wear)") instead of a fixed 175f, which
            // clipped "SUITED (full readout)" (user screenshot). If the row is too narrow to also
            // fit Undo/Redo/Exit, drop only that FIRST join to SameLine so the buttons wrap to
            // their own line rather than overflowing the window.
            float tierComboW = ComboFitWidth(PreviewTierOptions, 220f);
            ImGui.SetNextItemWidth(tierComboW);
            PreviewTierCombo();
            float trailingBtnW = ButtonWidth("Undo##toolbar") + ButtonWidth("Redo##toolbar")
                + ButtonWidth("Exit##toolbar") + ImGui.GetStyle().ItemSpacing.x * 2f;
            if (ImGui.GetContentRegionAvail().x >= trailingBtnW) ImGui.SameLine();
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
            ImGui.SameLine();
            DrawPauseButton();
            DrawEditTargetLine();
            ImGui.Separator();
        }

        /// <summary>Freeze/thaw the world while editing so a moving HUD (vitals ticking, moodlets
        /// changing) does not fight the layout. Mirrors the F10 Control Center's pause button through the
        /// shared <see cref="Core.GamePause"/> latch (single-player only — <c>CanOwnPause</c> declines in
        /// multiplayer / under a vanilla menu, so the click is a safe no-op there). The pause is released
        /// automatically when the editor closes (see <see cref="HudEditorMode.Exit"/>).</summary>
        private static void DrawPauseButton()
        {
            bool held = Core.GamePause.Held;
            bool canToggle = held || (Core.GamePause.CanOwnPause() && !WorldManager.IsGamePaused);
            // BeginDisabled is unavailable in this ImGui.NET (see the pattern elsewhere in this file), so
            // the button always draws; the click just no-ops when a pause cannot be owned.
            if (ImGui.Button(held ? "Resume##pause" : "Pause##pause") && canToggle)
            {
                if (held) Core.GamePause.Release(HudEditorMode.PauseReason);
                else Core.GamePause.Hold(HudEditorMode.PauseReason, null);
            }
        }

        private void DrawBuildTab()
        {
            ImGui.TextDisabled("UI THEME — layout and element changes are saved to the active UI Theme.");
            DrawDesignerSection();
        }

        /// <summary>The Theme tab: profile-theme chrome up top (it covers colours AND effects, so it
        /// stays above the bar), then palette-first sub-tabs. The old three DefaultOpen headers put
        /// the ~30 colour wheels between the author and the type/box controls; the sub-tabs mean the
        /// handful of colours anyone actually retunes is the FIRST thing on the tab (FlorpyDorp:
        /// "tabs mean less scrolling").</summary>
        private void DrawThemeTab()
        {
            ImGui.TextDisabled("GLOBAL — these defaults and palette colours apply across UI Themes.");
            DrawProfileThemeBlock();

            PushTabStyle();
            if (!ImGui.BeginTabBar("##UIAThemeSubTabs")) { PopTabStyle(); return; }
            if (BeginSubTab("Palette", "##UIAThemePalette")) { DrawCorePaletteSection(); EndSubTab(); }
            if (BeginSubTab("All colours", "##UIAThemeAllColours")) { DrawAllColoursSection(); EndSubTab(); }
            if (BeginSubTab("Typography & boxes", "##UIAThemeBoxes")) { DrawTypographyBoxesSection(); EndSubTab(); }
            ImGui.EndTabBar();
            PopTabStyle();
        }

        /// <summary>Theme &gt; Typography &amp; boxes: everything about the SHAPE of a HUD box and
        /// the text inside it (the old "Panel surface" + "Typography" headers, one flow).</summary>
        private void DrawTypographyBoxesSection()
        {
            ImGui.TextColored(Accent, "TEXT");
            FontCombo();
            // The shared HUD text scale is a registry row (an element's own font scale multiplies
            // ON TOP of it — see HudStyleFx's "elFontScale"); the label size and the font name are
            // not style knobs and stay hand-written.
            FxRows(HudFxCategory.Surface, HudStyleFx.SecText);
            FloatSlider(HudConfig.LabelFontSize, "Label size (PRESSURE, HELMET...)", 7f, 22f);

            ImGui.Spacing();
            ImGui.TextColored(Accent, "SIZE");
            FloatSlider(HudConfig.HudScale, "Overall HUD scale", 0.6f, 1.6f);
            Toggle(HudConfig.HudScaleWithRes, "Scale HUD with resolution (keep the layout proportional)");
            if (HudConfig.HudScaleWithRes != null && HudConfig.HudScaleWithRes.Value)
            {
                float user = Mathf.Max(0.0001f, HudConfig.HudScale != null ? HudConfig.HudScale.Value : 1f);
                float resFactor = HudConfig.EffectiveHudScale() / user;
                float rw, rh;
                HudConfig.ReferenceResolution(out rw, out rh);
                var doc = Features.HudProfileStore.Active;
                bool stamped = doc != null && doc.RefW >= 320f && doc.RefH >= 240f;
                ImGui.TextDisabled(string.Format("  designed at {0}x{1} ({2})  ->  screen {3}x{4}  =  x{5:0.000}",
                    (int)rw, (int)rh, stamped ? "from this UI Theme" : "global default",
                    Screen.width, Screen.height, resFactor));
                // Stamps THIS UI Theme only. Deliberately does NOT touch the global fallback: the
                // shipped profiles (Glassy 4.0 etc.) declare no reference and were authored at
                // 1920x1080, so moving the global would make THEM scale wrong.
                if (ImGui.Button("Stamp THIS UI Theme as designed at my resolution") && doc != null)
                {
                    doc.RefW = Screen.width;
                    doc.RefH = Screen.height;
                    Features.HudProfileStore.MarkChanged();
                }
                ImGui.TextDisabled("  ^ saves your screen size INTO the UI Theme, so when you share it");
                ImGui.TextDisabled("    everyone gets your proportions (1080p players render it smaller).");
                FloatSlider(HudConfig.HudScaleMatch, "  match: 0 = width, 1 = height", 0f, 1f);
            }

            ImGui.Spacing();
            ImGui.TextColored(Accent, "BOX SHAPE");
            FxRows(HudFxCategory.Surface, HudStyleFx.SecBox);

            ImGui.Spacing();
            ImGui.TextColored(Accent, "SHARP GLASS PANELS");
            FxRows(HudFxCategory.Surface, HudStyleFx.SecSdfMaster);
            if (HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value)
            {
                FxRows(HudFxCategory.Surface, HudStyleFx.SecSdf);
                if (!Core.HudShaderStore.SdfAvailable)
                    ImGui.TextColored(WarnCol,
                        "  Shader bundle unavailable — panels fail soft to the mesh renderer.");
            }

            ImGui.Spacing();
            ImGui.TextColored(Accent, "APPLY TO EVERY ELEMENT");
            ImGui.TextDisabled("Apply one coherent source mode to every element:");
            if (ImGui.Button("ALL follow Theme + Effects globals"))
                HudEditorMode.SetAllFollowGlobal(true);
            ImGui.SameLine();
            if (ImGui.Button("Snapshot ALL as Custom"))
                HudEditorMode.SetAllFollowGlobal(false);
            if (ImGui.Button("Flatten ALL boxes")) HudEditorMode.MakeAllFlat();
        }

        /// <summary>The Effects tab. ~90 controls used to live in six DefaultOpen CollapsingHeaders
        /// — one continuous column you scrolled through for a minute to reach bloom. They are now
        /// nested sub-tabs, one per visual family, and every control has exactly ONE home
        /// (FlorpyDorp: "tabs mean less scrolling"). The three old "Tier A/B/C" masters keep their
        /// config keys unchanged and are simply labelled by what they buy.</summary>
        private void DrawEffectsTab()
        {
            ImGui.TextDisabled("GLOBAL — element popups can follow these defaults or override supported effects.");
            // Kept ABOVE the sub-tab bar: it acts on the whole document, not on one family, and a
            // sub-tab child fills the remaining height (a footer under the bar would be unreachable).
            if (ImGui.Button("Reset active per-element effects to current globals"))
                HudEditorMode.ResetAllElementEffects();
            ImGui.TextDisabled("Custom appearance stays custom; effect values receive a coherent global snapshot.");

            PushTabStyle();
            if (!ImGui.BeginTabBar("##UIAEffectsSubTabs")) { PopTabStyle(); return; }
            if (BeginSubTab("Glass", "##UIAFxGlass")) { DrawFxGlassSection(); EndSubTab(); }
            if (BeginSubTab("Edges", "##UIAFxEdges")) { DrawFxEdgesSection(); EndSubTab(); }
            if (BeginSubTab("Glow", "##UIAFxGlow")) { DrawFxGlowSection(); EndSubTab(); }
            if (BeginSubTab("Bloom", "##UIAFxBloom")) { DrawBloomControls(); EndSubTab(); }
            if (BeginSubTab("Alerts", "##UIAFxAlerts")) { DrawFxAlertsSection(); EndSubTab(); }
            if (BeginSubTab("Transitions", "##UIAFxTransitions")) { DrawFxTransitionsSection(); EndSubTab(); }
            if (BeginSubTab("Advanced", "##UIAFxAdvanced")) { DrawFxAdvancedSection(); EndSubTab(); }
            ImGui.EndTabBar();
            PopTabStyle();
        }

        /// <summary>Effects &gt; Glass: the plate itself. All three effect MASTERS live here — the
        /// core mesh effects (cfg TierA), the animated glass shaders (cfg TierB) and the frosted
        /// backdrop (cfg TierC) — plus the surface treatments that are neither edge light nor
        /// glow. The masters share one sub-tab on purpose: they are the "how much can the HUD
        /// afford" decision, and reading them side by side is the point.</summary>
        private void DrawFxGlassSection()
        {
            ImGui.TextColored(Accent, "CORE EFFECTS");
            FxRows(HudFxCategory.Glass, HudStyleFx.SecTierA);
            if (On(HudConfig.FxTierA))
            {
                FxRows(HudFxCategory.Glass, HudStyleFx.SecCore);

                // Shape of the per-element "Fade box ends L/R" / "top/bottom" ramps. The
                // amounts stay per-element; the shape is shared so a HUD full of faded bars
                // ends the same way. Both default to an exact no-op.
                ImGui.Spacing();
                ImGui.TextColored(Accent, "BOX END FADE (shape)");
                ImGui.TextDisabled("  Per-element sliders set WHERE a box fades; these set HOW.");
                FxRows(HudFxCategory.Glass, HudStyleFx.SecBoxEndFade);
                if (HudConfig.FxEdgeFadeBorder != null
                    && Mathf.Abs(HudConfig.FxEdgeFadeBorder.Value - 1f) > 0.01f)
                {
                    bool sdfOn = HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                        && Core.HudShaderStore.SdfAvailable;
                    ImGui.TextDisabled(HudConfig.FxEdgeFadeBorder.Value < 1f
                        ? "    Below 1: the outline keeps its colour while the fill melts away."
                        : "    Above 1: the border surrenders before the plate does.");
                    if (!sdfOn)
                        ImGui.TextColored(WarnCol,
                            "    Border influence needs the sharp panel shader — the mesh fallback fades all vertices alike.");
                }
            }
            else ImGui.TextDisabled("  (the surface, edge and glow controls stay hidden while this is off)");

            ImGui.Spacing();
            ImGui.TextColored(Accent, "ANIMATED GLASS");
            ImGui.TextColored(Core.HudShaderStore.TierBAvailable ? Accent : WarnCol,
                Core.HudShaderStore.TierBAvailable
                    ? "  Shader bundle ready."
                    : "  SHADER BUNDLE NOT LOADED — THESE EFFECTS ARE INACTIVE");
            FxRows(HudFxCategory.Glass, HudStyleFx.SecTierB);
            if (HudConfig.FxTierB.Value)
            {
                FxRows(HudFxCategory.Glass, HudStyleFx.SecAnimGlass);
                // Dissolve's master is a TRANSITION and lives (with its strength, and its
                // per-element Inherit/On/Off) on the Transitions sub-tab.
                ImGui.TextDisabled("Dissolve reveal: see the Transitions sub-tab.");
            }

            ImGui.Spacing();
            ImGui.TextColored(Accent, "FROSTED BACKDROP");
            FxRows(HudFxCategory.Glass, HudStyleFx.SecTierC);
            if (HudConfig.FxTierC.Value)
            {
                // frost strength / blur depth / backdrop darkening / frost tint — the tint is a
                // hue-wheel row and is dispatched inside the same loop, so order is preserved.
                FxRows(HudFxCategory.Glass, HudStyleFx.SecFrost);
                ImGui.TextDisabled(HudBackdrop.Active ? "Backdrop capture active." : "Backdrop capture is idle or unavailable in this view mode.");
                ImGui.TextDisabled("Blur resolution and re-blur rate: Advanced sub-tab.");
            }
        }

        /// <summary>Effects &gt; Edges: the edge-energy family — the lit border, its ripple, its
        /// flow and the flowing aura that rides the shared halo envelope.</summary>
        private void DrawFxEdgesSection()
        {
            if (!On(HudConfig.FxTierA)) { CoreEffectsOffNotice("edge energy"); return; }

            FxRows(HudFxCategory.Edges, HudStyleFx.SecEdgeMaster);
            if (HudConfig.FxEdgeLightOn.Value)
                FxRows(HudFxCategory.Edges, HudStyleFx.SecEdge);
        }

        /// <summary>Effects &gt; Glow: the halo, the envelope that halo and flowing aura share, and
        /// the breathing-pulse SHAPE (its master is a transition, so it lives one tab over).</summary>
        private void DrawFxGlowSection()
        {
            if (!On(HudConfig.FxTierA)) { CoreEffectsOffNotice("the glow halo"); return; }

            FxRows(HudFxCategory.Glow, HudStyleFx.SecHalo);
            bool haloEnvelopeOn = HudConfig.FxGlowOn.Value
                || (HudConfig.FxEdgeLightOn.Value && HudConfig.FxGlowFlowAuraOn.Value);
            if (haloEnvelopeOn)
            {
                ImGui.Spacing();
                ImGui.TextColored(Accent, "SHARED HALO / FLOWING-AURA ENVELOPE");
                FxRows(HudFxCategory.Glow, HudStyleFx.SecEnvelope);
                ImGui.TextDisabled("  Uneven / organic reach: Advanced sub-tab.");
                ImGui.TextDisabled("  Extreme radius increases transparent GPU overdraw.");
            }
            FxRows(HudFxCategory.Glow, HudStyleFx.SecBreathSpeed);
            DrawAdvancedHaloWarnings(haloEnvelopeOn);

            // The pulse MASTER lives on the Transitions sub-tab with the rest of the registry
            // effects (one switch, one place — two live checkboxes on the same ConfigEntry read
            // as two settings). Its shape knobs stay here, where the per-element inspector still
            // points for them.
            ImGui.Spacing();
            ImGui.TextColored(Accent, "BREATHING PULSE (shape)");
            ImGui.TextDisabled("Breathing pulse master: see the Transitions sub-tab.");
            FxRows(HudFxCategory.Glow, HudStyleFx.SecPulseShape);
        }

        /// <summary>The one line every core-effects-gated sub-tab prints when the master is off —
        /// the master itself is a tab away, so say where it is instead of showing dead controls.</summary>
        private static void CoreEffectsOffNotice(string what)
        {
            ImGui.TextColored(WarnCol, "Core effects are off — " + what + " is inactive.");
            ImGui.TextDisabled("Switch \"Core effects\" back on in the Glass sub-tab.");
        }

        /// <summary>The "your advanced halo motion is not actually running" diagnostics, shared by
        /// the Glow sub-tab and by Advanced (which owns the uneven / organic reach knobs).</summary>
        private static void DrawAdvancedHaloWarnings(bool haloEnvelopeOn)
        {
            bool sdfEnabled = HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value;
            bool sdfReady = Core.HudShaderStore.SdfAvailable;
            bool advancedHaloRequested = (HudConfig.FxGlowOn.Value && HudConfig.FxGlowHaze.Value > 0.001f)
                || (haloEnvelopeOn && (HudConfig.FxGlowBreathOn.Value || HudConfig.FxGlowUnevenOn.Value))
                || (HudConfig.FxEdgeLightOn.Value && HudConfig.FxGlowFlowAuraOn.Value);
            if (advancedHaloRequested && (!sdfEnabled || !sdfReady))
            {
                ImGui.TextColored(WarnCol,
                    !sdfEnabled
                        ? "  Advanced halo motion is inactive: switch on the sharp panel shader"
                        : "  Advanced halo motion needs a newer effects bundle, then a full restart.");
                if (!sdfEnabled) ImGui.TextColored(WarnCol, "  (Theme > Typography & boxes).");
                else ImGui.TextDisabled("  (the bundle's ABI-2 shader set)");
            }
            if (haloEnvelopeOn && HudConfig.FxGlowWidth.Value > 160f && (!sdfEnabled || !sdfReady))
                ImGui.TextColored(WarnCol,
                    "  Mesh fallback caps the visible halo radius at 160 px.");
        }

        /// <summary>Effects &gt; Alerts: the suit-warning pulse. Deliberately NOT gated by the core
        /// master — with core effects off the alarm still recolours border lines, so the controls
        /// must stay reachable.</summary>
        private void DrawFxAlertsSection()
        {
            // Being ON SCREEN is itself the preview request (HudAlertPulse.RequestAutoPreview):
            // opening this page used to leave the HUD's alert glow switched off, so the colour
            // wheels and brightness gains below had nothing to act on. Stamped every frame the
            // sub-tab draws, and consumed one frame later by HudAlertPulse.Tick.
            UI.Hud.HudAlertPulse.RequestAutoPreview();
            FxRows(HudFxCategory.Alerts, HudStyleFx.SecAlertMaster);
            if (HudConfig.FxAlertPulseOn.Value)
            {
                // The ##id suffixes are load-bearing: ImGui keys widgets by label, and a bare
                // "  breath speed (Hz)" would collide with the shared halo-breath slider. They are
                // part of each registry row's Label for exactly that reason.
                FxRows(HudFxCategory.Alerts, HudStyleFx.SecAlertShape);
                ImGui.TextDisabled("  A caution flash lasts "
                    + (HudConfig.FxAlertBreathSeconds.Value * HudConfig.FxAlertCautionBreaths.Value)
                        .ToString("0.0") + "s in total.");

                // The two alert hues, editable right here rather than only from the Palette tab —
                // tuning an alarm means watching it breathe while you drag. These are the same
                // HudPalette entries the Palette tab lists, so edits, undo and profile save all
                // behave identically; PushID keeps the shared entry.Name labels from colliding
                // with that tab's copies. Refresh the undo baseline first: _frameSnapshot is
                // otherwise only set while a palette sub-tab draws, so a picker here would push a
                // STALE undo step that reverts to whenever that sub-tab was last open.
                _frameSnapshot = HudPalette.Snapshot();
                ImGui.PushID("alertfx");

                // PARTIALLY HAND-WRITTEN ON PURPOSE: the two hues are HudPalette entries (not
                // HudConfig), and each brightness gain belongs directly under its own wheel. So the
                // two registry rows are drawn one at a time, in place, rather than as one run.
                ImGui.TextDisabled("  Caution (yellow) - flashes, then clears for good:");
                if (HudPalette.AlertCaution != null) ColorWheel(HudPalette.AlertCaution);
                FxRow("alertCautionBright");

                ImGui.TextDisabled("  Critical (red) - breathes until the warning clears:");
                if (HudPalette.AlertCritical != null) ColorWheel(HudPalette.AlertCritical);
                FxRow("alertCriticalBright");

                ImGui.PopID();

                ImGui.TextDisabled("  Brightness is a gain on the picked colour. The HUE is always");
                ImGui.TextDisabled("  kept: past full brightness the gain buys a bigger, more solid");
                ImGui.TextDisabled("  halo instead of washing the colour out to white.");
                ImGui.TextDisabled("  Below 1 gives a subdued tint.");
                ImGui.TextDisabled("  Strength scales the caution flash's peak, and the critical");
                ImGui.TextDisabled("  breath's swing (0 = steady red, no motion).");

                // The alert is normally suppressed while this designer is open, which would make
                // the colour pickers above impossible to judge. Preview forces a level so the HUD
                // breathes live while you drag. It breathes continuously rather than running the
                // caution burst and stopping, and only works while F9 is open.
                // "auto" is the default and simply means "while THIS page is open" — that is what
                // stops opening F9 from killing the alert glow you came here to tune. Picking a
                // level explicitly carries it to the other sub-tabs too.
                ImGui.Separator();
                ImGui.TextDisabled("  Live preview (designer only):");
                int pv = UI.Hud.HudAlertPulse.PreviewMode;
                if (ImGui.RadioButton("auto##alertPv", pv == 0)) UI.Hud.HudAlertPulse.PreviewMode = 0;
                ImGui.SameLine();
                if (ImGui.RadioButton("caution##alertPv", pv == 1)) UI.Hud.HudAlertPulse.PreviewMode = 1;
                ImGui.SameLine();
                if (ImGui.RadioButton("critical##alertPv", pv == 2)) UI.Hud.HudAlertPulse.PreviewMode = 2;
                ImGui.SameLine();
                if (ImGui.RadioButton("off##alertPv", pv < 0)) UI.Hud.HudAlertPulse.PreviewMode = -1;
                if (pv < 0)
                {
                    ImGui.TextDisabled("  Alerts stay suppressed while this designer is open -");
                    ImGui.TextDisabled("  switch back to auto to judge these colours.");
                }
                else if (pv == 0)
                {
                    ImGui.TextColored(WarnCol,
                        "  Auto: breathing the CRITICAL alarm while this page is open.");
                    ImGui.TextDisabled("  Pick caution/critical to keep it lit on the other tabs.");
                }
                else
                {
                    ImGui.TextColored(WarnCol,
                        "  Previewing - breathing continuously, real warnings ignored.");
                    ImGui.TextDisabled("  Stays lit on every tab; clears when this designer closes.");
                }
                if (!On(HudConfig.FxTierA))
                    ImGui.TextColored(WarnCol,
                        "  Core effects are off: alerts recolour the border line only, with no halo.");
            }
        }

        /// <summary>Effects &gt; Transitions: suit power, the boot/death sequences and the whole
        /// HudTransitionFx registry (dissolve, glitch, dropouts, the breathing-pulse master).
        ///
        /// These used to have NO global switch at all: EffectAmt returned a hard 1f for any
        /// element following the globals, so a transition could not be turned off from the
        /// element OR from here. Each one now has a real master + a default strength, and the
        /// per-element toggles are honoured in both style states.</summary>
        private void DrawFxTransitionsSection()
        {
            // Merged in from View & Behavior (2026-07-25): the section was duplicated across
            // two tabs (audit 03), so everything suit-power-and-transition-related now lives
            // here in one place; View & Behavior leaves a one-line pointer instead.
            Toggle(HudConfig.DiegeticTiers, "Diegetic tiers (no suit power = words only)");
            Toggle(HudConfig.FxPowerDownMirrorsBoot, "Power DOWN mirrors power UP (staggered flicker)");
            ImGui.TextDisabled("  On: the HUD leaves the same way it arrives, element by element.");
            ImGui.TextDisabled("  Off: the old all-at-once power-death.");
            Toggle(HudConfig.FxDissolveOnPowerDown, "Dissolve frontier also runs on power DOWN");
            ImGui.TextDisabled("  Off: the dissolve reveals on boot only; power-down just fades.");

            Toggle(HudConfig.LowPowerDropouts, "Low-power dropout glitches");
            if (HudConfig.LowPowerDropouts.Value)
                FloatSlider(HudConfig.LowPowerThreshold, "  low-power threshold (%)", 0f, 40f);

            ImGui.Spacing();
            if (ImGui.Button("Test power-death flicker")) HudSystem.TestPowerDeath();
            ImGui.SameLine();
            if (ImGui.Button("Test boot sequence")) HudSystem.TestBoot();

            ImGui.Spacing();
            ImGui.TextColored(Accent, "TRANSITION EFFECTS");

            // ONE row per registry effect rather than a hand-written list: the labels, the
            // masters and the strength entries all come from HudTransitionFx.All, which is the
            // same table the per-element inspector reads — so the two menus cannot drift, and a
            // new effect appears in both the moment it is added to the registry.
            for (int i = 0; i < HudTransitionFx.All.Length; i++)
            {
                HudTransitionFxDef fx = HudTransitionFx.All[i];
                if (fx == null) continue;
                // Unbound entry (config bind failed / very early frame): skip the row rather
                // than NRE inside Toggle and take the whole tab down with it.
                ConfigEntry<bool> master = fx.MasterEntry;
                if (master == null) continue;
                ImGui.PushID(fx.Key);          // every effect's "  Strength" shares a caption
                Toggle(master, fx.Label);
                if (master.Value)
                {
                    ConfigEntry<float> amt = fx.AmountEntry;
                    if (amt != null) FloatSlider(amt, "  Strength", 0f, 2f);
                    TipLines(fx.Tip);

                    // The glitch tear is also HudGlitch's transform-jitter envelope (the tear/
                    // shake on power transitions, not just the per-panel participation weight)
                    // — unified onto this ONE row 0.9.2.5 (audit 04 #7: two live glitch systems
                    // exposed twice). Duration and the per-event gates have no tri-state
                    // analogue, so they hang off this row rather than living on their own.
                    if (string.Equals(fx.Key, "fxGlitch", System.StringComparison.Ordinal))
                    {
                        if (HudConfig.FxGlitchDuration != null)
                            FloatSlider(HudConfig.FxGlitchDuration, "  duration (seconds)", 0.1f, 4f);
                        Toggle(HudConfig.GlitchOnPowerDown, "  fire on power DOWN / suit removed");
                        Toggle(HudConfig.GlitchOnPowerUp, "  fire on power UP / boot");
                        if (ImGui.Button("Test glitch now")) HudGlitch.TriggerTest();
                    }
                }
                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.TextDisabled("A master OFF here wins over every element's own setting:");
            ImGui.TextDisabled("an element set to On still stays still while its master is off.");
            ImGui.TextDisabled("Per element: click it, then its own Effects tab > Motion & power");
            ImGui.TextDisabled("transitions, then pick Inherit (follow these) / On / Off per effect.");
        }

        /// <summary>Effects &gt; Advanced: the knobs almost nobody touches — renderer performance
        /// and the fine-detail panel-shader esoterica — lifted OUT of the family sub-tabs so those
        /// stay short. Every row still obeys the master that owns it and says where that master
        /// lives, so nothing here can be edited into a state the author cannot see.</summary>
        private void DrawFxAdvancedSection()
        {
            ImGui.TextColored(Accent, "PERFORMANCE");
            // Every row here is machine-local (HudTheme.Exclude): it trades frame time on the
            // player's OWN machine, so an imported theme must never move it. The registry carries
            // that as a per-row DoesNotTravel flag.
            if (On(HudConfig.FxTierC)) FxRows(HudFxCategory.Glass, HudStyleFx.SecFrostPerf);
            else ImGui.TextDisabled("Frosted backdrop is off (Glass sub-tab) - its blur knobs are hidden.");
            if (On(HudConfig.FxBloomOn)) FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomRes);
            else ImGui.TextDisabled("HUD bloom is off (Bloom sub-tab) - its resolution knob is hidden.");

            ImGui.Spacing();
            ImGui.TextColored(Accent, "HALO FINE DETAIL");
            ImGui.TextDisabled("Needs the sharp panel shader (Theme > Typography & boxes).");
            bool haloEnvelopeOn = On(HudConfig.FxTierA)
                && (On(HudConfig.FxGlowOn)
                    || (On(HudConfig.FxEdgeLightOn) && On(HudConfig.FxGlowFlowAuraOn)));
            if (haloEnvelopeOn)
            {
                // Category = Glow (these ARE halo knobs and the element popup pages them there),
                // Section = the Advanced home F9 gives them. Plan §3.4: "Effects > Glow (+ the
                // Advanced halo rows)".
                FxRows(HudFxCategory.Glow, HudStyleFx.SecAdvancedHalo);
                DrawAdvancedHaloWarnings(true);
            }
            else ImGui.TextDisabled("No halo or flowing aura is on (Glow sub-tab) - reach knobs hidden.");

            ImGui.Spacing();
            ImGui.TextColored(Accent, "BLOOM COLOUR BIAS");
            if (On(HudConfig.FxBloomOn))
            {
                FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomBias);
                if (!On(HudConfig.FxBloom2On))
                    ImGui.TextDisabled("  Border / highlight bloom is off (Bloom sub-tab).");
            }
            else ImGui.TextDisabled("HUD bloom is off (Bloom sub-tab).");
        }

        private void DrawViewBehaviorTab()
        {
            ImGui.TextDisabled("GLOBAL — projection, suit behavior and vanilla-panel integration.");

            if (ImGui.CollapsingHeader("HUD renderer", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Toggle(HudConfig.VisorHudEnabled, "Visor HUD enabled");
                Toggle(HudConfig.ShowVignette, "Visor-edge vignette");
                Toggle(HudConfig.ShowScanlines, "Projector scan-lines (whole HUD canvas)");
                ImGui.TextDisabled("  Colour: 'HudScanline' in Theme > All colours. Radials / Universal Inventory excluded.");
                Toggle(HudConfig.TintItemIcons, "Tint item icons green (hands / 1-6 / inventory)");
                ImGui.TextDisabled("  Colour: 'HudItemIconTint' in Theme > All colours (white = off).");
                ImGui.TextDisabled("  This is the INHERITED default: hands / 1-6 can override it per");
                ImGui.TextDisabled("  element (and per bare/suited) - see 'Item icon tint' in their popup.");
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

            // Merged into the Effects tab (2026-07-25, audit 03 P0): this section was duplicated
            // across both tabs with the same config bindings living in two places at once. The
            // pointer names the SUB-TAB it landed on (0.9.2.5 Wave D restructure).
            ImGui.TextDisabled("Suit power, transitions & dropouts: Effects tab > Transitions.");

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
                ImGui.Text("Effects bundle: " + (Core.HudShaderStore.TierBAvailable ? "READY" : "NOT LOADED"));
                ImGui.Text("Analytic panel shader: " + (Core.HudShaderStore.SdfAvailable ? "READY" : "FALLBACK MESH")
                    + "  (ABI " + Core.HudShaderStore.SdfAbi + ")");
                if (Core.HudShaderStore.SdfAvailable && !Core.HudShaderStore.SdfCutAvailable)
                    ImGui.TextColored(WarnCol,
                        "  ABI 3 adds cut corners on this shader - rebuild uia_effects.bundle, then restart.");
                ImGui.Text("Frost capture: " + (HudBackdrop.Active ? "ACTIVE" : "IDLE"));
                ImGui.Text("Bloom: " + (HudBloomFx.Available ? "AVAILABLE" : "UNAVAILABLE"));
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

        // ---- profile CRUD state (New / Duplicate / Rename / Delete / Restore shipped) ----
        // Exactly ONE flow is armed at a time so the section stays a single compact button row
        // plus (at most) one inline strip. Null = nothing armed. All of these are cleared by
        // CancelProfileAction, which OnClose calls — a reopened window never shows a stale
        // confirm, and a hot reload starts from a blank slate.
        private const string ProfActNew = "new";
        private const string ProfActDup = "dup";
        private const string ProfActRename = "rename";
        private const string ProfActDelete = "delete";
        private const string ProfActRestore = "restore";
        private static string _profileAction;
        private static string _profileNameField = "";
        private static string _deleteTarget;     // the chosen victim; never the active profile
        private static string _profileNotice;    // inline failure text for the armed flow

        // Profile-list cache: Directory.GetFiles is disk IO + allocation, and the header runs
        // it every frame. Re-scan only when the dropdown opens (edge-triggered) or after a save.
        private static List<string> _profileNamesCache = new List<string>();
        private static bool _profileComboOpen;
        private static bool _profileCacheScanned;   // see EnsureProfileCache (one latched first scan)

        /// <summary>Memo for <see cref="Features.HudProfileStore.IsShippedName"/>, which touches the
        /// DISK on every call (a mod-folder probe, or a full manifest read under the F6 dev flow) —
        /// and the CRUD row asks it every frame the Profiles section is drawn, for the active profile
        /// and again for the delete victim. Same reason <see cref="_profileNamesCache"/> exists.
        /// Cleared at the same points that refresh that cache (arm an action, switch profile, close
        /// the window), so the only thing a stale entry can miss is a name that self-heals into the
        /// manifest mid-session — reopening F9 picks it up.</summary>
        private static readonly Dictionary<string, bool> _shippedNameCache =
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
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
            // Crosshair distance readout (D-019 - FlorpyDorp evaluates placement himself):
            "Rangefinder",
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
            // Point editing lives in the ELEMENT POPUP (the whole context for editing one
            // element is there); the F9 window keeps only the global creation tools above.
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
            ImGui.TextDisabled("Active UI Theme (shareable XML; edits autosave):");
            string active = HudEditorMode.ActiveProfileName();
            // Fit the row instead of a fixed 200f, which clipped longer profile names ("Stationeers
            // Blue" — user screenshot). If the "Open folder" button no longer fits beside a combo
            // widened to its 220f floor, it wraps to its own line rather than overflowing the window.
            float openFolderW = ButtonWidth("Open folder");
            float avail = ImGui.GetContentRegionAvail().x;
            float spacing = ImGui.GetStyle().ItemSpacing.x;
            float profileComboW = Mathf.Max(220f, avail - openFolderW - spacing);
            ImGui.SetNextItemWidth(profileComboW);
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
                        // One switch path for the combo and every CRUD flow (it still guards a
                        // corrupt file by keeping the document we already have).
                        SwitchToProfile(n);
                        CancelProfileAction();   // an armed flow named the profile we just left
                    }
                }
                ImGui.EndCombo();
            }
            _profileComboOpen = comboOpen;
            if (avail - profileComboW - spacing >= openFolderW) ImGui.SameLine();
            if (ImGui.Button("Open folder"))
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Features.HudProfileStore.Dir); }
                catch { }
            }

            DrawShippedReadOnlyNotice(active);
            DrawProfileCrudRow(active);
        }

        /// <summary>The one persistent line that says whether the active UI Theme can be edited at
        /// all. A shipped theme is READ-ONLY for a player (the store refuses every player-edit write
        /// — see <c>HudProfileStore.Save</c>), and the only in-game symptom of that is a toast the
        /// first time it happens, which is not enough for a window whose whole job is editing. So it
        /// is stated up front, right under the profile picker, for as long as that theme is active.
        ///
        /// In author mode (`uiadev`) the line flips rather than disappearing: FlorpyDorp edits
        /// shipped themes ON PURPOSE, and the dangerous state is not knowing WHICH mode a session is
        /// in — an unlocked session looks exactly like the old build until something saves.</summary>
        private static void DrawShippedReadOnlyNotice(string active)
        {
            if (!IsShippedCached(active)) return;
            if (Core.UiaDevMode.Active)
                ImGui.TextColored(Accent, "DEV MODE - editing a shipped theme.");
            else
                ImGui.TextColored(WarnCol, "SHIPPED THEME - read-only. Duplicate it to edit.");
        }

        /// <summary>New / Duplicate / Rename / Delete (plus "Restore shipped version" when the
        /// active profile carries one of OUR names), as one compact row under the profile combo.
        /// A button ARMS its flow; the name field or the confirm step then appears inline beneath
        /// the row, and only one flow can be armed at a time (FlorpyDorp: a New Theme button that
        /// opens a blank slate AS WELL AS a duplicate button).
        ///
        /// Two rules every flow obeys. (1) Commit the in-flight element gesture first, exactly like
        /// the profile combo does — a property drag that never got its ImGui deactivation callback
        /// would otherwise be flushed into the WRONG document. (2) Any flow that moves a FILE
        /// (rename, restore) forces the debounced autosave out under the OLD name BEFORE the file
        /// moves, so a pending write can neither resurrect the renamed-away file nor land on top of
        /// a freshly restored one.</summary>
        private static void DrawProfileCrudRow(string active)
        {
            EnsureProfileCache();
            bool canDelete = HasOtherProfile(active);

            float spacing = ImGui.GetStyle().ItemSpacing.x;
            float rowW = ButtonWidth("New") + ButtonWidth("Duplicate") + ButtonWidth("Rename")
                + ButtonWidth("Delete") + spacing * 3f;
            // Wave A width fitting: keep the four on one line only while they actually FIT;
            // otherwise let them stack rather than run off the edge of a narrowed window.
            bool oneRow = ImGui.GetContentRegionAvail().x >= rowW;

            if (ImGui.Button("New")) ArmProfileAction(ProfActNew, UniqueProfileName("New UI Theme"));
            if (oneRow) ImGui.SameLine();
            if (ImGui.Button("Duplicate")) ArmProfileAction(ProfActDup, UniqueProfileName(active + " copy"));
            if (oneRow) ImGui.SameLine();
            if (ImGui.Button("Rename")) ArmProfileAction(ProfActRename, active);
            if (oneRow) ImGui.SameLine();
            if (canDelete)
            {
                if (ImGui.Button("Delete")) ArmProfileAction(ProfActDelete, "");
            }
            else
            {
                // Dimmed in place rather than hidden: the button has to stay where the eye expects
                // it, and "it's the one you're using" is the useful half of the message. (Drawn as
                // a real button with its result ignored — ImGui.NET's BeginDisabled is not
                // guaranteed present in the game's binding, PushStyleColor is used all over this
                // file.)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.17f, 0.19f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.16f, 0.17f, 0.19f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.16f, 0.17f, 0.19f, 1f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.56f, 0.58f, 1f));
                ImGui.Button("Delete");
                ImGui.PopStyleColor(4);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The UI Theme you are editing can't be deleted - switch to another one first.");
            }

            // Restore is only meaningful for a name WE ship, so it only exists then.
            if (IsShippedCached(active) && _profileAction != ProfActRestore)
            {
                if (ImGui.Button("Restore shipped version")) ArmProfileAction(ProfActRestore, "");
                ImGui.TextDisabled("  Re-copies our pristine '" + active + "' over your edited copy.");
            }

            switch (_profileAction)
            {
                case ProfActNew: DrawNewProfileStrip(); break;
                case ProfActDup: DrawDuplicateStrip(); break;
                case ProfActRename: DrawRenameStrip(active); break;
                case ProfActDelete: DrawDeleteStrip(active); break;
                case ProfActRestore: DrawRestoreStrip(active); break;
            }
            if (!string.IsNullOrEmpty(_profileNotice)) ImGui.TextColored(WarnCol, _profileNotice);
        }

        /// <summary>The shared "[name field] [commit] [Cancel]" strip. Returns true on the frame the
        /// commit button is pressed. Only one strip draws per frame, so the widget ids can be shared.</summary>
        private static bool NameFieldRow(string commitLabel)
        {
            float spacing = ImGui.GetStyle().ItemSpacing.x;
            float btnW = ButtonWidth(commitLabel) + ButtonWidth("Cancel") + spacing * 2f;
            ImGui.SetNextItemWidth(Mathf.Clamp(ImGui.GetContentRegionAvail().x - btnW, 145f, 260f));
            ImGui.InputText("##profilename", ref _profileNameField, 48);
            ImGui.SameLine();
            bool go = ImGui.Button(commitLabel);
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) { CancelProfileAction(); return false; }
            return go;
        }

        /// <summary>NEW: a blank slate. The document itself is built by the store (current screen
        /// RefW/RefH, ONE follow-global HandBoxes element = the minimum playable HUD, and NO theme
        /// snapshot) — a themeless profile deliberately keeps whatever global look is current until
        /// the author changes a global, and THAT edit stamps the theme through MarkThemeChanged
        /// exactly as it does for any other profile.</summary>
        private static void DrawNewProfileStrip()
        {
            ImGui.TextDisabled("New UI Theme - a blank slate to build on:");
            if (NameFieldRow("Create"))
            {
                string clean = CleanProfileName(_profileNameField);
                if (string.IsNullOrEmpty(clean)) { _profileNotice = "Type a name first."; }
                else if (ProfileExists(clean))
                    _profileNotice = "'" + clean + "' already exists - pick another name.";
                else
                {
                    FlushPendingElementEdit();
                    if (Features.HudProfileStore.CreateBlank(clean))
                    {
                        SwitchToProfile(clean);
                        CancelProfileAction();
                        return;
                    }
                    _profileNotice = "Could not create '" + clean + "'.";
                }
            }
            ImGui.TextDisabled("  Starts at your screen size with ONE hand-boxes element (the minimum");
            ImGui.TextDisabled("  playable HUD) and no saved theme, so it keeps the look you have now");
            ImGui.TextDisabled("  until you change a global - that first change stamps its own theme.");
        }

        /// <summary>DUPLICATE: unchanged behaviour from the old "New copy / Duplicate as" row —
        /// the LIVE document is cloned (so unsaved edits travel), written under the new name, and
        /// made active from memory rather than re-read from disk.</summary>
        private static void DrawDuplicateStrip()
        {
            ImGui.TextDisabled("Copy the active UI Theme (layout + its saved theme) under a new name:");
            if (!NameFieldRow("Duplicate as")) return;
            string clean = CleanProfileName(_profileNameField);
            if (string.IsNullOrEmpty(clean)) { _profileNotice = "Type a name first."; return; }
            if (ProfileExists(clean)) { _profileNotice = "'" + clean + "' already exists - pick another name."; return; }
            FlushPendingElementEdit();
            var doc = Features.HudProfileStore.Active;
            var copy = doc != null ? doc.Clone() : null;
            if (copy == null) { _profileNotice = "No active UI Theme to copy."; return; }
            copy.Name = clean;
            if (!Features.HudProfileStore.Save(copy, clean))
            {
                _profileNotice = "Could not save '" + clean + "'.";
                return;
            }
            if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = clean;
            Features.HudProfileStore.SetActive(copy, clean);
            UI.Hud.HudDocumentHistory.Clear();
            HudEditorMode.ClearElementSelection();
            _profileNamesCache = Features.HudProfileStore.ListProfiles(); // new file: refresh the cache
            CancelProfileAction();
        }

        /// <summary>RENAME the ACTIVE profile.
        ///
        /// A theme WE ship is READ-ONLY: the store refuses to move our file out from under its own
        /// name (author mode aside), so the commit button is not drawn at all rather than armed and
        /// guaranteed to fail — the same rule <see cref="DrawRestoreStrip"/> follows when the shipped
        /// folder is missing, and it keeps the inline notice honest (a refused rename would otherwise
        /// report "that name may be taken", which is simply untrue). Duplicate is the way across.
        ///
        /// With author mode on, the old behaviour applies and is explained instead: renaming one of
        /// OUR names detaches the file from shipped management, because SyncShipped keys the manifest
        /// by FILE NAME — the renamed copy is an unknown (player-owned) profile from then on and a
        /// pristine original is re-seeded on the next launch. Say so before the click, not after.</summary>
        private static void DrawRenameStrip(string active)
        {
            ImGui.TextDisabled("Rename '" + active + "':");
            if (IsShippedCached(active))
            {
                if (!Core.UiaDevMode.Active)
                {
                    ImGui.TextColored(WarnCol, "  '" + active + "' is a UI Theme WE ship, so it is read-only.");
                    ImGui.TextDisabled("  Duplicate it instead - the copy is yours, name and all, and it keeps");
                    ImGui.TextDisabled("  every edit you have made in this session.");
                    if (ImGui.Button("Cancel")) CancelProfileAction();
                    return;
                }
                ImGui.TextColored(WarnCol, "  DEV MODE: renaming a UI Theme WE ship detaches your copy from");
                ImGui.TextColored(WarnCol, "  shipped updates (it becomes yours forever), and a fresh");
                ImGui.TextColored(WarnCol, "  pristine '" + active + "' appears again on the next launch.");
            }
            if (!NameFieldRow("Rename")) return;
            string clean = CleanProfileName(_profileNameField);
            if (string.IsNullOrEmpty(clean)) { _profileNotice = "Type a name first."; return; }
            if (string.Equals(clean, active, System.StringComparison.OrdinalIgnoreCase)) { CancelProfileAction(); return; }
            if (ProfileExists(clean)) { _profileNotice = "'" + clean + "' already exists - pick another name."; return; }
            FlushPendingElementEdit();
            // Persist under the OLD name BEFORE the file moves: a debounced autosave landing after
            // the rename would write the old file straight back and leave a duplicate behind.
            Features.HudProfileStore.FlushNow();
            if (!Features.HudProfileStore.Rename(active, clean))
            {
                _profileNotice = "Could not rename '" + active + "' - that name may be taken.";
                return;
            }
            // The store re-points the ACTIVE profile itself (config value, _activeName and the live
            // document's Name), so the HUD needs no reload and the undo history survives the rename.
            // Re-asserting the config value costs nothing and guarantees the editor cannot be left
            // showing the old name. Only the name cache has to catch up.
            if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = clean;
            _profileNamesCache = Features.HudProfileStore.ListProfiles();
            CancelProfileAction();
        }

        /// <summary>DELETE, two-step: the row button arms this strip, and only "Delete permanently"
        /// touches the disk. The victim is picked from every profile EXCEPT the active one — the
        /// editor must never be left pointing at a file that no longer exists.</summary>
        private static void DrawDeleteStrip(string active)
        {
            EnsureProfileCache();
            if (string.IsNullOrEmpty(_deleteTarget) || !ProfileExists(_deleteTarget)
                || string.Equals(_deleteTarget, active, System.StringComparison.OrdinalIgnoreCase))
                _deleteTarget = FirstOtherProfile(active);
            if (string.IsNullOrEmpty(_deleteTarget))
            {
                ImGui.TextColored(WarnCol, "Nothing to delete - '" + active + "' is the only UI Theme.");
                if (ImGui.Button("Cancel")) CancelProfileAction();
                return;
            }

            ImGui.TextDisabled("Delete which UI Theme? (not the one you are editing)");
            float spacing = ImGui.GetStyle().ItemSpacing.x;
            ImGui.SetNextItemWidth(Mathf.Max(220f,
                ImGui.GetContentRegionAvail().x - ButtonWidth("Delete permanently") - spacing));
            if (ImGui.BeginCombo("##delvictim", _deleteTarget))
            {
                for (int i = 0; i < _profileNamesCache.Count; i++)
                {
                    string n = _profileNamesCache[i];
                    if (string.Equals(n, active, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (ImGui.Selectable(n, string.Equals(n, _deleteTarget, System.StringComparison.OrdinalIgnoreCase)))
                        _deleteTarget = n;
                }
                ImGui.EndCombo();
            }
            if (IsShippedCached(_deleteTarget))
            {
                ImGui.TextDisabled("  '" + _deleteTarget + "' is one of ours, so deleting it IS the restore");
                ImGui.TextDisabled("  path: a pristine copy is re-seeded on the next launch.");
            }
            ImGui.TextColored(WarnCol, "Delete '" + _deleteTarget + "' from disk? This cannot be undone.");
            if (ImGui.Button("Delete permanently"))
            {
                FlushPendingElementEdit();
                string victim = _deleteTarget;
                if (Features.HudProfileStore.Delete(victim))
                {
                    _profileNamesCache = Features.HudProfileStore.ListProfiles();
                    CancelProfileAction();
                    return;
                }
                _profileNotice = "Could not delete '" + victim + "'.";
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) CancelProfileAction();
        }

        /// <summary>RESTORE SHIPPED, two-step: re-copy our pristine file over the player's edited
        /// copy of a shipped NAME, then reload it (it is always the active profile here, so the live
        /// HUD has to follow). Offered for the active profile only — a shipped profile that is not
        /// active is restored by deleting it and relaunching, which the delete strip says.</summary>
        private static void DrawRestoreStrip(string active)
        {
            // The restore copies FROM the mod's installed folder. Under the F6 ScriptEngine dev flow
            // there is no such folder, so the commit button is not drawn at all rather than armed
            // and guaranteed to fail — a two-step confirm that cannot succeed is worse than no
            // button. (The row-level button still exists so the affordance stays discoverable and
            // this strip can explain itself.)
            if (!Features.HudProfileStore.ShippedFolderAvailable)
            {
                ImGui.TextColored(WarnCol, "The mod's installed folder isn't available right now, so there is");
                ImGui.TextColored(WarnCol, "no shipped copy to restore from (the F6 dev flow has none).");
                ImGui.TextDisabled("  Delete this UI Theme instead and relaunch - a pristine copy comes back.");
                if (ImGui.Button("Cancel")) CancelProfileAction();
                return;
            }

            ImGui.TextColored(WarnCol, "Replace '" + active + "' with the version we ship?");
            ImGui.TextDisabled("  Your changes to THIS UI Theme are overwritten. No other UI Theme is touched.");
            if (ImGui.Button("Restore permanently"))
            {
                FlushPendingElementEdit();
                // Clear the pending autosave BEFORE the file is replaced, or the debounced write
                // would land on top of the freshly restored copy and undo the restore.
                Features.HudProfileStore.FlushNow();
                if (Features.HudProfileStore.RestoreShipped(active))
                {
                    SwitchToProfile(active);
                    CancelProfileAction();
                    return;
                }
                _profileNotice = "Could not restore '" + active + "' - no shipped copy is available.";
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) CancelProfileAction();
        }

        /// <summary>Make <paramref name="name"/> the live profile from an editor action — the same
        /// sequence the profile combo runs (config, load with a keep-what-we-have fallback so a
        /// corrupt file cannot blank the HUD, clear history), plus a selection reset because element
        /// ids do not survive a document swap.</summary>
        private static void SwitchToProfile(string name)
        {
            FlushPendingElementEdit();
            if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = name;
            var keep = Features.HudProfileStore.Active;
            Features.HudProfileStore.LoadActive(name,
                () => keep != null ? keep.Clone() : new UI.Hud.HudDocument { Name = name });
            UI.Hud.HudDocumentHistory.Clear();
            HudEditorMode.ClearElementSelection();
            _profileNamesCache = Features.HudProfileStore.ListProfiles();
            _shippedNameCache.Clear();   // a switch can self-heal a shipped name into the manifest
        }

        private static void ArmProfileAction(string action, string seedName)
        {
            FlushPendingElementEdit();
            _profileAction = action;
            _profileNameField = seedName ?? "";
            _profileNotice = null;
            _deleteTarget = null;
            // Arm against FRESH names: the "already exists" guards and the delete picker both read
            // the cache, which is otherwise only refreshed when the profile combo opens.
            _profileNamesCache = Features.HudProfileStore.ListProfiles();
            _shippedNameCache.Clear();
        }

        private static void CancelProfileAction()
        {
            _profileAction = null;
            _profileNameField = "";
            _profileNotice = null;
            _deleteTarget = null;
        }

        /// <summary>Fill the profile-name cache the FIRST time the CRUD row draws (the combo only
        /// re-scans on its opening edge, and the row needs names before anyone opens it). Latched,
        /// not count-based: an empty HudProfiles folder must not re-stat the disk every frame. The
        /// row's other callers keep replacing the list outright, which stays fresher than this.</summary>
        private static void EnsureProfileCache()
        {
            if (_profileNamesCache == null) _profileNamesCache = new List<string>();
            if (_profileCacheScanned) return;
            _profileNamesCache = Features.HudProfileStore.ListProfiles();
            _profileCacheScanned = true;
        }

        /// <summary>Is this one of OUR profile names? Memoised — see <see cref="_shippedNameCache"/>.</summary>
        private static bool IsShippedCached(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool v;
            if (_shippedNameCache.TryGetValue(name, out v)) return v;
            v = Features.HudProfileStore.IsShippedName(name);
            _shippedNameCache[name] = v;
            return v;
        }

        private static bool ProfileExists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            EnsureProfileCache();
            for (int i = 0; i < _profileNamesCache.Count; i++)
                if (string.Equals(_profileNamesCache[i], name, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool HasOtherProfile(string active)
        {
            return !string.IsNullOrEmpty(FirstOtherProfile(active));
        }

        private static string FirstOtherProfile(string active)
        {
            EnsureProfileCache();
            for (int i = 0; i < _profileNamesCache.Count; i++)
            {
                string n = _profileNamesCache[i];
                if (!string.IsNullOrEmpty(n)
                    && !string.Equals(n, active, System.StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            return null;
        }

        /// <summary>"New profile", then "New profile 2"... — a seed the author can accept as typed
        /// instead of having to invent a free name.</summary>
        private static string UniqueProfileName(string baseName)
        {
            string clean = CleanProfileName(baseName);
            if (string.IsNullOrEmpty(clean)) clean = "New UI Theme";
            if (!ProfileExists(clean)) return clean;
            for (int n = 2; n < 500; n++)
            {
                string candidate = clean + " " + n;
                if (!ProfileExists(candidate)) return candidate;
            }
            return clean;
        }

        /// <summary>Strip exactly what the store strips for the FILE name, so the name written into
        /// HudActiveProfile is the one that will actually be found on the next launch (a config name
        /// that kept an illegal character would miss its file and silently regenerate the default).</summary>
        private static string CleanProfileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string clean = name;
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                clean = clean.Replace(bad.ToString(), string.Empty);
            return clean.Trim();
        }

        // ------------------------------------------------------------------ gizmos

        /// <summary>Selection outline + handles + hover ghost + line-draw preview, drawn
        /// on the foreground list so they ride ABOVE the HUD. Corners are forward-warped
        /// (8 samples per edge) so the box hugs curved elements.</summary>
        private static void DrawGizmos()
        {
            var dl = ImGui.GetForegroundDrawList();
            float scale = HudConfig.EffectiveHudScale();
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

            // DONOR PICK CUE (style parity Phase 4, plan §5.3). The normal hover outline is kept —
            // it is how you already know what is under the cursor — and recoloured to say whether
            // this target can be taken: green accepts, red rejects (self, or an element that is
            // itself inheriting this family, which the depth-1 rule forbids). The caption rides the
            // cursor so the gesture explains itself without a modal.
            if (HudEditorMode.PickingDonor)
            {
                bool legal = hov != null && HudEditorMode.DonorPickLegal(hov);
                uint okCol = ImGui.GetColorU32(new Vector4(0.35f, 0.95f, 0.45f, 0.95f));
                uint noCol = ImGui.GetColorU32(new Vector4(0.95f, 0.35f, 0.3f, 0.95f));
                if (hov != null) OutlineRect(dl, hov.CanvasRect(scale), legal ? okCol : noCol, 2f);
                string what = UI.Hud.HudStyleFx.CategoryName(HudEditorMode.DonorPickCategory);
                string verb = HudEditorMode.DonorPickCopyOnly ? "COPY " + what + " FROM" : "INHERIT " + what + " FROM";
                string msg = hov == null
                    ? verb + ": click an element  (Esc / right-click cancels)"
                    : legal
                        ? verb + ": " + hov.Def.Type + "  (Esc / right-click cancels)"
                        : "CANNOT use this element (itself inheriting, or the same element)";
                var mp = (Vector2)Input.mousePosition;
                Overlay.DrawUtil.TextShadowCentered(dl,
                    new Vector2(mp.x, Screen.height - mp.y + 26f),
                    legal || hov == null ? okCol : noCol, msg);
            }
            else if (hov != null && !ReferenceEquals(hov, sel))
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
                            // Zero-length handles are STRAIGHT segments — drawing their dots on
                            // top of the anchor would just bury it (and the hit-test skips them).
                            if (i < hout.Length && hout[i].sqrMagnitude > 0.25f)
                            {
                                var ho = ToImGui(HudEditorMode.PointCanvas(sel, pts[i] + hout[i], scale));
                                dl.AddLine(a, ho, hLine, 1.4f); dl.AddCircleFilled(ho, 3.5f, hCol, 12);
                            }
                            if (i < hin.Length && hin[i].sqrMagnitude > 0.25f)
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
            // The snap lattice of the SELECTED element: its offsets snap in reference px at its own
            // effective (group) scale, measured from its anchor plus its group shift.
            float es = sel != null ? sel.ScaleFor(scale) : scale;
            float cell = Mathf.Max(1f, HudConfig.GridSnapSize.Value) * es;
            float g = cell;
            while (g < 6f) g += cell;   // keep on-screen spacing >= ~6px (multiple of the snap cell)
            float hw = Screen.width * 0.5f, hh = Screen.height * 0.5f;
            var anchor = sel != null
                ? sel.Def.AnchorFor(UI.Hud.HudElementView.LayoutBare, UI.Hud.HudElementView.LayoutMode)
                : UI.Hud.HudAnchor.Center;
            Vector2 o = UI.Hud.HudElementDef.AnchorPoint(anchor, hw, hh) + (sel != null ? sel.GroupOffset : Vector2.zero);
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
        private static int _elementPopupStamp = -1;
        private static int _menuPopupStamp = -1;
        private static readonly List<UI.Hud.HudProp> _propScratch = new List<UI.Hud.HudProp>();
        private static readonly List<UI.Hud.HudProp> _menuPropScratch = new List<UI.Hud.HudProp>();
        private static int _gridPopupStamp = -1;
        private static readonly List<UI.Hud.HudProp> _gridPropScratch = new List<UI.Hud.HudProp>();
        private static UI.Hud.HudDocument _pendingElementUndo;
        private static UI.Hud.HudDocument _pendingElementDocument;
        private static string _pendingElementProfile;
        private static bool _pendingElementChanged;
        private static UI.Hud.HudPropGroup? _activeElementPropGroup;

        /// <summary>The F10 Control Center theme popup: the same HudPropDrawer surface an element
        /// uses, built over the menu's config (follow toggle + per-colour overrides). Config-backed,
        /// so no undo — BepInEx persists on write and UiaControlCenter restyles from the change.</summary>
        private static void DrawMenuThemePopup()
        {
            bool moved = _menuPopupStamp != HudEditorMode.ElementStamp;
            _menuPopupStamp = HudEditorMode.ElementStamp;
            ImGui.SetNextWindowPos(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                moved ? ImGuiCond.Always : ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSizeConstraints(new Vector2(320f, 120f),
                new Vector2(Screen.width * 0.6f, Screen.height * 0.92f));
            bool open = true;
            if (ImGui.Begin("Edit: Control Center menu###UIAMenuThemePopup",
                ref open, ImGuiWindowFlags.NoCollapse))
            {
                _menuPropScratch.Clear();
                try { UI.Menu.Kit.UiaMenuTheme.DescribeProps(_menuPropScratch); }
                catch { }
                // No undo callbacks: config writes persist immediately, and the live restyle poll
                // in UiaControlCenter.Update repaints the menu when the theme hash changes. The
                // onChanged hook is NOT cosmetic: the Control Center theme travels inside a HUD
                // profile now (Wave C), so an edit here must arm the theme restamp or the change
                // would be lost the next time the player switches profiles.
                HudPropDrawer.DrawAll(_menuPropScratch, null, null, null,
                    () => Features.HudProfileStore.MarkThemeChanged());
            }
            ImGui.End();
            if (!open) HudEditorMode.MenuSelected = false;
        }

        /// <summary>The Universal Inventory (Grid) style popup: the same HudPropDrawer surface an
        /// element uses, built over <see cref="UI.Grid.GridTheme"/>'s config (follow-the-global-box-
        /// theme toggle + per-value overrides). Config-backed, so no undo brackets — BepInEx
        /// persists on write and TheGridPanel restyles from the StyleHash poll in its tick.</summary>
        private static void DrawGridStylePopup()
        {
            bool moved = _gridPopupStamp != HudEditorMode.ElementStamp;
            _gridPopupStamp = HudEditorMode.ElementStamp;
            ImGui.SetNextWindowPos(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                moved ? ImGuiCond.Always : ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSizeConstraints(new Vector2(320f, 120f),
                new Vector2(Screen.width * 0.6f, Screen.height * 0.92f));
            bool open = true;
            if (ImGui.Begin("Edit: Universal Inventory###UIAGridStylePopup",
                ref open, ImGuiWindowFlags.NoCollapse))
            {
                _gridPropScratch.Clear();
                try { UI.Grid.GridTheme.DescribeProps(_gridPropScratch); }
                catch { }
                // Same reasoning as DrawMenuThemePopup: the Grid theme travels with a HUD profile
                // since Wave C, so an override edit has to arm the theme restamp.
                HudPropDrawer.DrawAll(_gridPropScratch, null, null, null,
                    () => Features.HudProfileStore.MarkThemeChanged());
            }
            ImGui.End();
            if (!open) HudEditorMode.GridSelected = false;
        }

        /// <summary>Finish an in-flight property gesture before selection/profile/editor state
        /// changes can make ImGui omit its normal deactivation callback. The old document is still
        /// persisted if an external profile swap beat us here; its undo snapshot is intentionally
        /// not mixed into the new profile's history.</summary>
        internal static void FlushPendingElementEdit()
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

            // The F10 Control Center window, clicked while open behind the editor, gets its own
            // theme popup — the same HudPropDrawer surface an element uses, over the menu's config.
            // Guard on IsOpen so the popup can never strand over a closed/hidden menu (the menu
            // closing, or the document HUD being switched off, would otherwise leave it drawing).
            if (HudEditorMode.MenuSelected)
            {
                if (UI.Menu.UiaControlCenter.IsOpen)
                {
                    DrawMenuThemePopup();
                    return;
                }
                HudEditorMode.MenuSelected = false;
            }

            // The Universal Inventory window, clicked while open behind the editor, gets its own
            // style popup. Same strand guard, widened to the whole Grid FAMILY: pinned windows
            // outlive the main window by design and are styled by the same theme this popup
            // edits, so the popup stays up while ANY of them is alive (closing the main window
            // with pins selected must not kill the editing session — editor-access finding,
            // 2026-07-20). It still can never strand over a fully closed Grid.
            if (HudEditorMode.GridSelected)
            {
                if (UI.Grid.TheGridPanel.IsOpen || UI.Grid.PinnedInventoryWindow.LiveCount > 0)
                {
                    DrawGridStylePopup();
                    return;
                }
                HudEditorMode.GridSelected = false;
            }

            // The selected ELEMENT gets the generic property popup.
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

                DrawPerTierStyleBlock(el, beginElementEdit, commitElementEdit, noteElementChanged);

                _propScratch.Clear();
                try { el.DescribeProps(_propScratch); }
                catch { }

                PushTabStyle();   // same active-tab contrast as the F9 window's own bars
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
                PopTabStyle();
                // Live feedback only while a widget is actually being edited — an
                // idle popup must not rebuild the element's meshes every frame.
                bool editing = false;
                try { editing = ImGui.IsAnyItemActive(); } catch { }
                if (editing) HudSystem.RelayoutElement(el);
                ImGui.Separator();
                // Point editing for shapes/lines rides the element popup — the whole context
                // for editing THIS element is here (moved out of the F9 window, play-test ask).
                if (HudEditorMode.IsPointEditable(el.Def))
                {
                    if (HudEditorMode.EditingPoints)
                    {
                        var hintCol = new Vector4(1f, 0.62f, 0.15f, 1f);
                        ImGui.TextColored(hintCol,
                            "EDIT POINTS: drag anchors - Alt+click deletes - click anywhere adds");
                        bool bez = el.Def.GetI("curveMode", el.Def.GetB("smooth", false) ? 1 : 0) == 2;
                        if (bez)
                        {
                            ImGui.TextColored(hintCol,
                                "Bezier: PULL a segment to curve it - Ctrl+click a segment = straight");
                            ImGui.TextColored(hintCol,
                                "Ctrl+click an anchor = corner/smooth - Alt+drag a handle = cusp");
                        }
                        if (ImGui.Button("Done editing points##pop")) HudEditorMode.EndEditPoints();
                    }
                    else if (ImGui.Button("Edit points (drag / add / delete / curve)##pop"))
                    {
                        HudEditorMode.BeginEditPoints();
                    }
                    ImGui.Separator();
                }
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
        }

        /// <summary>The per-tier STYLE block at the top of the element popup (0.9.2.5, Wave C).
        ///
        /// Two separate things live here, in this order:
        ///  1. the OPT-IN — "Separate BARE style". Off (the default) means every tier shares ONE
        ///     design, which is why an untouched profile gains no key and looks identical. Ticking
        ///     it SEEDS the fork with a complete copy of the base's stored values
        ///     (<see cref="UI.Hud.HudElementView.SeedSlotFromBase"/>), so from that instant a later
        ///     suited edit can no longer leak into bare — the defect this block exists to fix.
        ///  2. the MODE TABS — which tier the Layout / Appearance / Effects values below read and
        ///     write. They drive the live preview tier (HudSystem.ForceTier) so what you SEE is
        ///     what you are editing, and the edit slot follows it.
        ///
        /// The tabs stay available with the fork OFF: they still pick the previewed tier, and the
        /// line underneath says plainly that edits are landing on the shared base, rather than
        /// letting the author believe a bare-only change was saved.</summary>
        private static void DrawPerTierStyleBlock(UI.Hud.HudElementView el,
            System.Action beginEdit, System.Action commitEdit, System.Action noteChanged)
        {
            var d = el != null ? el.Def : null;
            if (d == null) return;
            bool both = UI.Hud.HudElementView.IsBoth(d.Tiers);
            if (!both) return;   // single-mode element: nothing to fork, every edit hits the base

            bool forkBare = d.ForksSlot(UI.Hud.HudStyleSlot.Bare);
            bool wantFork = forkBare;
            if (ImGui.Checkbox("Separate BARE style##uiaPerTier", ref wantFork) && wantFork != forkBare)
            {
                HudEditorMode.CommitActiveDrag();   // never fold a live gizmo drag into this step
                FlushPendingElementEdit();
                beginEdit();
                if (wantFork) UI.Hud.HudElementView.SeedSlotFromBase(el, UI.Hud.HudStyleSlot.Bare);
                else d.SetForkSlot(UI.Hud.HudStyleSlot.Bare, false);
                noteChanged();
                commitEdit();
                HudSystem.RelayoutElement(el);
                forkBare = wantFork;
            }
            ImGui.TextDisabled(forkBare
                ? "  Bare keeps its OWN copy of every look value."
                : "  Off = one shared look for bare and suited.");

            bool editingBare = HudSystem.ForceTier.HasValue
                && HudSystem.ForceTier.Value == HudTier.Bare;
            ImGui.TextDisabled("Editing mode:");
            ImGui.SameLine();
            if (ModeTabButton("SUITED", !editingBare))
            {
                // Flush FIRST: a value gesture must never span a slot change, or its undo bracket
                // (keyed on ElementStamp, not the slot) would commit into the wrong tier.
                if (editingBare) FlushPendingElementEdit();
                HudSystem.ForceTier = HudTier.Suited;
            }
            ImGui.SameLine();
            if (ModeTabButton("BARE", editingBare))
            {
                if (!editingBare) FlushPendingElementEdit();
                HudSystem.ForceTier = HudTier.Bare;
            }
            DrawEditTargetLine();
            if (editingBare)
                ImGui.TextDisabled(forkBare
                    ? "Look values write BARE's own slot; layout writes the bare positions."
                    : "Look values write the SHARED base - tick above to give bare its own.");
            else
                ImGui.TextDisabled(forkBare
                    ? "Look values write SUITED only - bare has its own copy."
                    : "Look values write the shared base (bare inherits it).");
            ImGui.Separator();
        }

        /// <summary>A segmented-style button for the per-mode edit selector: the ACTIVE mode is
        /// drawn filled (accent blue), the inactive one flat, so the pair reads as a two-tab toggle.
        /// Returns true on click.</summary>
        private static bool ModeTabButton(string label, bool active)
        {
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.45f, 0.62f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.52f, 0.70f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.24f, 0.58f, 0.78f, 1f));
            }
            bool clicked = ImGui.Button(label + "##uiaModeTab");
            if (active) ImGui.PopStyleColor(3);
            return clicked;
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

        // ------------------------------------------------------------------ pieces

        // The edge-light colour wheel is now dispatched from FxColorRow (registry key
        // "edgeLightColour"), which owns the caption so the label cannot drift from the popup's.

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
            FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomMaster);
            if (HudConfig.FxBloomOn == null || !HudConfig.FxBloomOn.Value) return;

            ImGui.TextDisabled("Base glow");
            FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomBase);
            // Glow resolution and the colour-bias pair moved to the Advanced sub-tab (perf +
            // esoterica), so this tab stays the shape-and-strength one.
            ImGui.TextDisabled("  Glow resolution and colour bias: Advanced sub-tab.");

            ImGui.Spacing();
            FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomPulse);

            FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomReact);

            ImGui.Spacing();
            FxRows(HudFxCategory.Bloom, HudStyleFx.SecBloomBand2);

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

        /// <summary>The combo's own option labels, so its fit-width can be measured without
        /// duplicating the literal strings drawn inside <see cref="PreviewTierCombo"/>.</summary>
        private static readonly string[] PreviewTierOptions =
        {
            "Live (whatever you wear)", "BARE (no suit power)", "SUITED (full readout)", "ROBOT",
        };

        /// <summary>Sizes a combo to fit its widest option (a fixed pixel width silently clipped
        /// "Stationeers Blue" / "SUITED (full readout)" — user screenshot), clamped to a sane
        /// minimum so a short current value never shrinks the control to nothing.</summary>
        private static float ComboFitWidth(string[] options, float minWidth)
        {
            float widest = 0f;
            if (options != null)
                for (int i = 0; i < options.Length; i++)
                    if (!string.IsNullOrEmpty(options[i]))
                        widest = Mathf.Max(widest, ImGui.CalcTextSize(options[i]).x);
            // Frame padding both sides + room for the dropdown arrow glyph.
            float w = widest + ImGui.GetStyle().FramePadding.x * 2f + 28f;
            return Mathf.Max(minWidth, w);
        }

        /// <summary>A button's on-screen width for the SAME label ImGui will draw, so a caller can
        /// decide whether a row of buttons still fits before committing to SameLine.</summary>
        private static float ButtonWidth(string label)
        {
            return ImGui.CalcTextSize(label).x + ImGui.GetStyle().FramePadding.x * 2f;
        }

        /// <summary>Per-tier colour so the edit target (which layout your drags actually write to)
        /// is unmistakable at a glance — upgrades the old plain "Editing BARE layout" line, which
        /// only ever appeared for Bare, to a coloured line that is always visible and follows
        /// whichever tier the Preview combo above is currently forcing.</summary>
        private static void DrawEditTargetLine()
        {
            HudTier? forced = HudSystem.ForceTier;
            if (!forced.HasValue)
            {
                ImGui.TextDisabled("Editing: LIVE layout - moves write to whatever tier you're wearing.");
                return;
            }
            if (forced.Value == HudTier.Bare)
                ImGui.TextColored(new Vector4(1f, 0.72f, 0.25f, 1f),
                    "Editing: BARE layout - moves write to bare-mode positions.");
            else if (forced.Value == HudTier.Suited)
                ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f),
                    "Editing: SUITED layout - moves write to the base (suited) positions.");
            else
                ImGui.TextColored(new Vector4(0.35f, 0.9f, 0.45f, 1f),
                    "Editing: ROBOT layout - moves write to the base (suited) positions, shared with Suited.");
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

        /// <summary>Default corner SHAPE for every box that follows the theme: rounded arcs, or a
        /// flat 45-degree chamfer. The rounding slider above keeps sizing it either way, so
        /// radius 0 is still a square corner. Marks the theme like its neighbours (the two helpers
        /// FloatSlider/Toggle do it inline; a combo has to do it per selection).</summary>
        private static void CornerStyleCombo()
        {
            if (HudConfig.HudCornerStyle == null) return;
            const string round = "Rounded - the corner curves";
            const string cut = "Cut - the corner is sliced flat";
            int style = HudConfig.HudCornerStyle.Value == 1 ? 1 : 0;
            if (ImGui.BeginCombo("Corner style", style == 1 ? cut : round))
            {
                if (ImGui.Selectable(round, style == 0) && style != 0)
                {
                    HudConfig.HudCornerStyle.Value = 0;
                    Features.HudProfileStore.MarkThemeChanged();
                }
                if (ImGui.Selectable(cut, style == 1) && style != 1)
                {
                    HudConfig.HudCornerStyle.Value = 1;
                    Features.HudProfileStore.MarkThemeChanged();
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(style == 1
                ? "  The rounding slider now sets how DEEP the cut bites."
                : "  Elements can override this in their own popup (Appearance).");
            // Cut corners are native to the sharp panel shader from ABI 3 on (exponent 1 = the L1
            // norm, whose zero contour IS the chamfer), so only say something when the OLD
            // fallback is actually live.
            if (style == 1 && HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                && Core.HudShaderStore.SdfAvailable && !Core.HudShaderStore.SdfCutAvailable)
                ImGui.TextColored(WarnCol,
                    "  This effects bundle cannot cut corners on the sharp panel shader - cut boxes"
                    + " keep their shape on the classic renderer (rebuild the bundle, then restart).");
        }

        private static void CurvatureCombo()
        {
            // "Experimental" is gone from every option (0.9.2.5): these are just the modes now.
            // Mode C keeps its plain-language KNOWN-issue line below instead of a scare word.
            var mode = HudConfig.Curvature.Value;
            string current = mode == HudCurvature.Flat ? "Flat - no curvature"
                : mode == HudCurvature.VertexWarp ? "A - Vertex warp (recommended)"
                : mode == HudCurvature.DomeProjection ? "B - Dome projection (RenderTexture)"
                : mode == HudCurvature.CurvedWorldCanvas ? "C - Curved world canvas"
                : "D - Curved, steady (RenderTexture)";
            if (ImGui.BeginCombo("Curvature mode", current))
            {
                if (ImGui.Selectable("Flat - no curvature", mode == HudCurvature.Flat))
                    HudConfig.Curvature.Value = HudCurvature.Flat;
                if (ImGui.Selectable("A - Vertex warp (recommended)", mode == HudCurvature.VertexWarp))
                    HudConfig.Curvature.Value = HudCurvature.VertexWarp;
                if (ImGui.Selectable("B - Dome projection (RenderTexture)", mode == HudCurvature.DomeProjection))
                    HudConfig.Curvature.Value = HudCurvature.DomeProjection;
                if (ImGui.Selectable("C - Curved world canvas", mode == HudCurvature.CurvedWorldCanvas))
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

        /// <summary>The per-profile THEME chrome, drawn ABOVE the Theme sub-tab bar: the whole
        /// global look (HUD colours + effects + curvature + radial palette) belongs to the active
        /// profile, so switching profiles restores it — that is true of every sub-tab, not just
        /// the colour ones, which is why it does not live inside one of them.</summary>
        private static void DrawProfileThemeBlock()
        {
            bool hasTheme = Features.HudProfileStore.ActiveHasTheme;
            ImGui.TextColored(Accent, "UI THEME");
            ImGui.TextDisabled(hasTheme
                ? "This UI Theme stores its own look (colours + effects + radial); switching restores it."
                : "No saved theme yet - this UI Theme follows the live globals until you save one.");
            if (ImGui.Button("Save theme into this UI Theme")) Features.HudProfileStore.CaptureThemeNow();
            if (hasTheme)
            {
                ImGui.SameLine();
                if (ImGui.Button("Clear saved theme")) Features.HudProfileStore.ClearActiveTheme();
            }
            ImGui.Separator();
        }

        /// <summary>The colour Undo/Redo pair. Shown on BOTH colour sub-tabs on purpose: it is a
        /// history action over one shared stack, not a per-tab setting, and a swatch dragged on
        /// either tab must be revertible without hunting for the other one.</summary>
        private static void DrawPaletteHistoryRow()
        {
            if (ImGui.Button("Undo##palette"))
            {
                FlushPendingPaletteEdit();
                HudPalette.History.Undo();
                Features.HudProfileStore.MarkThemeChanged();
            }
            ImGui.SameLine();
            if (ImGui.Button("Redo##palette"))
            {
                FlushPendingPaletteEdit();
                HudPalette.History.Redo();
                Features.HudProfileStore.MarkThemeChanged();
            }
            ImGui.SameLine();
            ImGui.TextDisabled(HudPalette.History.CanUndo ? "" : "(nothing to undo)");
            ImGui.Spacing();
        }

        /// <summary>Per-frame palette bookkeeping shared by both colour sub-tabs: refresh the undo
        /// baseline, and report whether the hovered element's colour set CHANGED this frame (so a
        /// newly hot swatch scrolls itself into view exactly once). Only one sub-tab draws per
        /// frame, so the single _lastHotSig slot still sees one call per frame.</summary>
        private static bool BeginPaletteFrame()
        {
            _frameSnapshot = HudPalette.Snapshot();
            string hotSig = HudEditorMode.Active ? HudEditorMode.HotSignature : "";
            bool scrollTo = hotSig != _lastHotSig && hotSig.Length > 0;
            _lastHotSig = hotSig;
            return scrollTo;
        }

        /// <summary>Theme &gt; Palette: the semantic colours a HUD is actually designed in. Every
        /// box set to "Follow global colours" draws from these, so one edit re-tints the whole
        /// visor — which is why they come FIRST instead of being buried in the ~25-entry list.</summary>
        private static void DrawCorePaletteSection()
        {
            ImGui.TextDisabled("Every box set to \"Follow global colours\" uses these — one edit re-tints them all.");
            ImGui.TextDisabled("Click a swatch for a colour wheel. The A slider is transparency.");
            DrawPaletteHistoryRow();

            bool scrollTo = BeginPaletteFrame();
            ColorWheelHot(HudPalette.PanelFill, ref scrollTo);
            ColorWheelHot(HudPalette.PanelBorder, ref scrollTo);
            ColorWheelHot(HudPalette.LineAccent, ref scrollTo);
            ColorWheelHot(HudPalette.TextLabel, ref scrollTo);
            ColorWheelHot(HudPalette.TextValue, ref scrollTo);
            ColorWheelHot(HudPalette.TextDim, ref scrollTo);
            ColorWheelHot(HudPalette.Good, ref scrollTo);
            ColorWheelHot(HudPalette.Warn, ref scrollTo);
            ColorWheelHot(HudPalette.Critical, ref scrollTo);

            ImGui.Spacing();
            ImGui.TextDisabled("Hovering a HUD element turns the colours it uses orange. If nothing");
            ImGui.TextDisabled("lights up here, it draws from the All colours sub-tab.");
        }

        /// <summary>Theme &gt; All colours: the complete palette, in registration order. The core
        /// ones from the Palette sub-tab are included (it is the FULL list, and the two sub-tabs
        /// never draw in the same frame, so no swatch can fight another for its ImGui id).</summary>
        private static void DrawAllColoursSection()
        {
            ImGui.TextDisabled("Every HUD colour: compass, hologram, edges, scanline, alerts...");
            ImGui.TextDisabled("The nine you retune most also sit on the Palette sub-tab.");
            DrawPaletteHistoryRow();

            bool scrollTo = BeginPaletteFrame();
            foreach (var entry in HudPalette.All)
                ColorWheelHot(entry, ref scrollTo);

            ImGui.Spacing();
            if (ImGui.Button("Reset all HUD colours to defaults"))
            {
                FlushPendingPaletteEdit();
                HudPalette.ResetToDefaults();
                Features.HudProfileStore.MarkThemeChanged();
            }
        }

        /// <summary>A palette swatch that highlights (and scrolls into view) when its element is
        /// hovered in the editor — the shared draw for both the box section and the full list.</summary>
        private static void ColorWheelHot(HudPalette.Entry entry, ref bool scrollTo)
        {
            // The core list names its entries explicitly; a bind that failed must skip its row
            // rather than take the whole sub-tab down with an NRE.
            if (entry == null) return;
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
            {
                entry.Value = new Color(v.x, v.y, v.z, v.w);
                Features.HudProfileStore.MarkThemeChanged(); // a palette colour is part of the theme
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FlushPendingPaletteEdit();
            }
            else if (ImGui.IsItemDeactivated())
            {
                _pendingUndo = null;
            }
        }

        // ---- registry-driven global rows (style-parity Phase 1) -----------------------------
        //
        // The steady-state style knobs are described ONCE, in UI/Hud/HudStyleFx.cs. These helpers
        // are the only thing that turns a registry row into an ImGui widget, so the label, the
        // range, the companion-checkbox gating and the dimmed note lines under a row all come from
        // the table — this menu and the per-element popup (Phase 2) cannot present different
        // controls, different captions or different ranges.
        //
        // Nothing about the OUTPUT changed: every widget below is the same Toggle / FloatSlider /
        // IntSliderCfg / RgbConfig / <family>Combo call the hand-written line made, in the same
        // order, under the same conditions. The table simply supplies the arguments.
        //
        // Colour and Combo rows keep their bespoke drawers (a hue wheel needs the parse cache that
        // lives in this window's statics; a combo needs its option strings), so they are dispatched
        // by key rather than drawn generically. They stay IN the loop so row order is preserved.

        private static void FxRow(HudStyleFxDef def)
        {
            if (def == null || !def.MasterOn) return;
            switch (def.Kind)
            {
                case HudFxKind.Bool:
                {
                    ConfigEntry<bool> e = def.BoolEntry;
                    if (e == null) return;
                    Toggle(e, def.Label);
                    break;
                }
                case HudFxKind.Float:
                {
                    ConfigEntry<float> e = def.FloatEntry;
                    if (e == null) return;
                    FloatSlider(e, def.Label, def.Min, def.Max);
                    break;
                }
                case HudFxKind.Int:
                {
                    ConfigEntry<int> e = def.IntEntry;
                    if (e == null) return;
                    IntSliderCfg(e, def.Label, (int)def.Min, (int)def.Max);
                    break;
                }
                case HudFxKind.Color:
                    if (!FxColorRow(def)) return;
                    break;
                case HudFxKind.Combo:
                    if (!FxComboRow(def)) return;
                    break;
                default: return;
            }
            if (def.Notes == null) return;
            for (int i = 0; i < def.Notes.Length; i++) ImGui.TextDisabled(def.Notes[i]);
        }

        /// <summary>Hue-wheel rows. The swatch caches are per-entry statics in this window, so the
        /// dispatch is by key; a registry colour row with no global wheel (the portrait ring's
        /// per-element halo colour) draws nothing here and says so by returning false.</summary>
        private static bool FxColorRow(HudStyleFxDef def)
        {
            switch (def.Key)
            {
                case "edgeLightColour":
                    RgbConfig(HudConfig.FxEdgeLightColor, def.Label, ref _edgeTintStr, ref _edgeTintVec);
                    return true;
                case "frostTint":
                    RgbConfig(HudConfig.FrostTint, def.Label, ref _frostTintStr, ref _frostTintVec);
                    return true;
                case "bloomTint":
                    RgbConfig(HudConfig.FxBloomTint, def.Label, ref _bloomTintStr, ref _bloomTintVec);
                    return true;
                case "bloom2Tint":
                    RgbConfig(HudConfig.FxBloom2Tint, def.Label, ref _bloom2TintStr, ref _bloom2TintVec);
                    return true;
                default: return false;
            }
        }

        /// <summary>Named-option rows. Each combo owns its option strings (and, for corner style,
        /// its own theme-dirty marking per selection), so it keeps its bespoke drawer.</summary>
        private static bool FxComboRow(HudStyleFxDef def)
        {
            switch (def.Key)
            {
                case "cornerStyle": CornerStyleCombo(); return true;
                case "frostDownsample": FrostDownsampleCombo(); return true;
                case "bloomRes": BloomResCombo(); return true;
                default: return false;
            }
        }

        /// <summary>Draw one registry row by key. Used where a section is interrupted by content
        /// the table does not describe.</summary>
        private static void FxRow(string key) { FxRow(HudStyleFx.Find(key)); }

        /// <summary>Draw every registry row in one (category, section) group, in table order.
        /// Table order IS menu order — re-ordering the table moves the controls.</summary>
        private static void FxRows(HudFxCategory cat, string section)
        {
            HudStyleFxDef[] all = HudStyleFx.All;
            for (int i = 0; i < all.Length; i++)
                if (HudStyleFx.InSection(all[i], cat, section)) FxRow(all[i]);
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            bool v = entry.Value;
            if (ImGui.Checkbox(label, ref v)) { entry.Value = v; Features.HudProfileStore.MarkThemeChanged(); }
        }

        private static void FloatSlider(ConfigEntry<float> entry, string label, float min, float max)
        {
            float v = entry.Value;
            if (ImGui.SliderFloat(label, ref v, min, max)) { entry.Value = v; Features.HudProfileStore.MarkThemeChanged(); }
        }

        /// <summary>Print an explanatory blurb as indented, word-wrapped TextDisabled lines.
        /// ImGui's TextDisabled does not wrap, and the registry's tips are full sentences that
        /// would otherwise run off the 470px window; hard-wrapping here keeps the tip text a
        /// property of the effect (one string, shared with the inspector) instead of forcing every
        /// registry row to pre-split itself into display-width fragments.</summary>
        private static void TipLines(string text, int width = 62)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] words = text.Split(' ');
            string line = "";
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                if (line.Length > 0 && line.Length + 1 + words[i].Length > width)
                {
                    ImGui.TextDisabled("  " + line);
                    line = words[i];
                }
                else line = line.Length == 0 ? words[i] : line + " " + words[i];
            }
            if (line.Length > 0) ImGui.TextDisabled("  " + line);
        }

        private static void IntSliderCfg(ConfigEntry<int> entry, string label, int min, int max)
        {
            int v = entry.Value;
            if (ImGui.SliderInt(label, ref v, min, max)) entry.Value = v;
        }
    }
}
