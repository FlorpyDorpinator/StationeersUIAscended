using System;
using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>Radial-half comfort settings — Kit v2 conversion: Sections replace the old global
    /// Simple/Advanced split (the previously-advanced-only sliders now live behind each Section's
    /// "More options"), and the radial editor's own wheel-colour/glass/appearance knobs are ported
    /// in here directly (0.9.8.0 F10 overhaul, radial-port item). Whatever genuinely cannot be
    /// ported (the black-screen live preview, hover-highlighted palette entries, the hint-bar
    /// styling that only makes sense under that preview) stays reachable through ONE "Open the
    /// legacy radial editor" button in Wheel colours &amp; glass's More options.</summary>
    public sealed class RadialTab : IUiaTab
    {
        public string Title => "Radial";

        // Transient colour-swatch popup (this tab's own — NOT UiaComposite's kebab/popover shell,
        // which is private to that class). Built on open, destroyed on close/re-open, statics
        // nulled — the same GridProfilePopup lifetime pattern every other kit transient uses.
        // Swept for free when the window Closes/Restyles (both destroy every PopupLayer child) —
        // the undo commit rides that same destruction via ColorPopupCommitOnDestroy, so it can
        // never be dropped regardless of who/what destroys the popup.
        private static GameObject _colorPopup;
        private static GameObject _colorCatcher;

        // Esc-while-popup-open interceptor (registered with UiaControlCenter so Esc closes just
        // the popup — committing its undo step — instead of all of F10 out from under an
        // in-progress edit). A cached delegate instance so Register/Unregister always match by
        // reference regardless of how the callee's List<Func<bool>> compares them.
        private static bool _escRegistered;
        private static readonly Func<bool> _escInterceptor = TryConsumeEsc;

        // Gesture cache for the radial font list (Resources.FindObjectsOfTypeAll is not cheap and
        // this tab's Hub readout text Section rebuilds its (collapsed-but-still-built) Extras on
        // every Build — including an F9 colour-drag Restyle, ~7x/s). Reused while
        // UiaControlCenter.IsRestyling; refreshed on every real gesture build. No Shutdown-reset
        // needed beyond what ResetTransients already does: plain strings, no Unity object refs,
        // and a hot reload replaces the type (fresh statics) regardless.
        private static List<string> _fontNamesCache;

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            BuildBehaviour(col);
            BuildSizeAndReadout(col);
            BuildFeel(col);
            BuildToolbeltWheel(col);
            BuildBagWheel(col);
            BuildHubText(col);
            BuildColoursAndGlass(col);
        }

        // ---------- Behaviour ----------

        private void BuildBehaviour(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.behaviour", "Behaviour");

            var move = UiaControls.ToggleRow(sec.Body, "Keep moving while a wheel is open",
                UIAConfig.RadialMovementEnabled.Value, v => UIAConfig.RadialMovementEnabled.Value = v);
            var hold = UiaControls.SliderRow(sec.Body, "Open hold time", 60f, 600f, UIAConfig.HoldThresholdMs.Value,
                v => UIAConfig.HoldThresholdMs.Value = Mathf.RoundToInt(v), "0", 10f);
            // Personal input preference — deliberately NOT part of the radial theme family (a
            // theme must never flip a player's Shift muscle memory), so no MarkThemeChanged.
            var shift = UiaControls.ToggleRow(sec.Body, "Holding Shift keeps the wheel open after an action",
                UIAConfig.RadialShiftKeepsOpen.Value, v => UIAConfig.RadialShiftKeepsOpen.Value = v);
            UiaControls.Note(sec.Body,
                "ON (default): a wheel closes after one action; hold Shift to keep it open for " +
                "more. OFF: the wheel STAYS open after every action instead, and holding Shift " +
                "closes it after that one - for players who like the wheel as a persistent panel. " +
                "Hold-to-open wheels still close on release either way.");

            UiaSearch.RegisterRow(Title, "radial.behaviour", "Keep moving while a wheel is open",
                UiaSearch.MakeJump(move.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.behaviour", "Open hold time",
                UiaSearch.MakeJump(hold.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.behaviour", "Holding Shift keeps the wheel open",
                UiaSearch.MakeJump(shift.transform.parent.gameObject));
        }

        // ---------- Size & readout ----------

        private void BuildSizeAndReadout(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.size", "Size & readout",
                "The exotic geometry knobs (icon ratio, border/edge tuning, child-wheel behaviour, " +
                "max wedges) live under More options. Everything here belongs to the radial theme " +
                "'radial:' family and travels with the profile, bar Max wedges (behaviour, not look).",
                true);

            // These are all part of the radial theme's LOOK ("radial:" family, HudTheme) — each
            // setter marks the active profile's theme dirty so the value travels with the profile
            // and switching profiles restores it, the same contract HUD colours already have.
            var outer = UiaControls.SliderRow(sec.Body, "Wheel size", 120f, 480f, UIAConfig.RadialOuterRadius.Value,
                v => { UIAConfig.RadialOuterRadius.Value = v; HudProfileStore.MarkThemeChanged(); }, "0", 1f);
            var inner = UiaControls.SliderRow(sec.Body, "Hub size", 60f, 260f, UIAConfig.RadialInnerRadius.Value,
                v => { UIAConfig.RadialInnerRadius.Value = v; HudProfileStore.MarkThemeChanged(); }, "0", 1f);
            var caps = UiaControls.ToggleRow(sec.Body, "ALL CAPS labels", UIAConfig.RadialUppercaseLabels.Value,
                v => { UIAConfig.RadialUppercaseLabels.Value = v; HudProfileStore.MarkThemeChanged(); });
            var stateTxt = UiaControls.ToggleRow(sec.Body, "Live state under icons (%, kPa, counts)",
                UIAConfig.RadialShowStateText.Value,
                v => { UIAConfig.RadialShowStateText.Value = v; HudProfileStore.MarkThemeChanged(); });
            var wedgeLbl = UiaControls.ToggleRow(sec.Body, "Show item name under each icon",
                UIAConfig.RadialShowWedgeLabels.Value,
                v => { UIAConfig.RadialShowWedgeLabels.Value = v; HudProfileStore.MarkThemeChanged(); });

            UiaSearch.RegisterRow(Title, "radial.size", "Wheel size", UiaSearch.MakeJump(outer.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.size", "Hub size", UiaSearch.MakeJump(inner.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.size", "ALL CAPS labels", UiaSearch.MakeJump(caps.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.size", "Live state under icons", UiaSearch.MakeJump(stateTxt.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.size", "Show item name under each icon", UiaSearch.MakeJump(wedgeLbl.transform.parent.gameObject));

            var ex = sec.Extras;
            if (ex == null) return;
            Action reveal = () => sec.SetExpanded(true);

            var iconRatio = UiaControls.SliderRow(ex, "Icon size (frac of wedge)", 0.15f, 1.1f, UIAConfig.RadialIconRatio.Value,
                v => { UIAConfig.RadialIconRatio.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var borderW = UiaControls.SliderRow(ex, "Border thickness", 0f, 10f, UIAConfig.RadialBorderWidth.Value,
                v => { UIAConfig.RadialBorderWidth.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.0");
            var edgeFeather = UiaControls.SliderRow(ex, "Edge softness (AA)", 0f, 4f, UIAConfig.RadialEdgeFeather.Value,
                v => { UIAConfig.RadialEdgeFeather.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var shine = UiaControls.SliderRow(ex, "Shine", 0f, 2f, UIAConfig.RadialShineIntensity.Value,
                v => { UIAConfig.RadialShineIntensity.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var gap = UiaControls.SliderRow(ex, "Wedge gap (deg)", 0f, 6f, UIAConfig.RadialWedgeGapDeg.Value,
                v => { UIAConfig.RadialWedgeGapDeg.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.0");
            var dimOn = UiaControls.ToggleRow(ex, "Dim other wedges while one is highlighted", UIAConfig.RadialDimShading.Value,
                v => { UIAConfig.RadialDimShading.Value = v; HudProfileStore.MarkThemeChanged(); });
            var dimStrength = UiaControls.SliderRow(ex, "Dim strength", 0f, 1f, UIAConfig.RadialDimStrength.Value,
                v => { UIAConfig.RadialDimStrength.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var satScale = UiaControls.SliderRow(ex, "Child wheel size", 0.5f, 1.6f, UIAConfig.RadialSatelliteScale.Value,
                v => { UIAConfig.RadialSatelliteScale.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var satHub = UiaControls.SliderRow(ex, "Child radial hub ratio", 0.2f, 0.6f, UIAConfig.RadialSatelliteHubRatio.Value,
                v => { UIAConfig.RadialSatelliteHubRatio.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var dynText = UiaControls.ToggleRow(ex, "Dynamic child-hub text (no overlap)", UIAConfig.RadialDynamicReadoutText.Value,
                v => { UIAConfig.RadialDynamicReadoutText.Value = v; HudProfileStore.MarkThemeChanged(); });
            var rotate = UiaControls.ToggleRow(ex, "Angle long wedge labels so they fit", UIAConfig.RadialRotateLongLabels.Value,
                v => { UIAConfig.RadialRotateLongLabels.Value = v; HudProfileStore.MarkThemeChanged(); });
            var chip = UiaControls.SliderRow(ex, "Dragged-out item bubble size (px)", 16f, 80f, UIAConfig.ParkedChipRadius.Value,
                v => { UIAConfig.ParkedChipRadius.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            // Max wedges is BEHAVIOUR (overflow -> MORE), not look — deliberately excluded from
            // the "radial:" theme family (design §5.2), so it does NOT mark the theme dirty.
            var maxW = UiaControls.SliderRow(ex, "Max wedges", 6f, 32f, UIAConfig.RadialMaxWedges.Value,
                v => UIAConfig.RadialMaxWedges.Value = Mathf.RoundToInt(v), "0", 1f);

            UiaSearch.RegisterRow(Title, "radial.size", "Icon size", UiaSearch.MakeJump(iconRatio.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Border thickness", UiaSearch.MakeJump(borderW.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Edge softness", UiaSearch.MakeJump(edgeFeather.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Shine", UiaSearch.MakeJump(shine.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Wedge gap", UiaSearch.MakeJump(gap.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Dim other wedges", UiaSearch.MakeJump(dimOn.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Dim strength", UiaSearch.MakeJump(dimStrength.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Child wheel size", UiaSearch.MakeJump(satScale.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Child radial hub ratio", UiaSearch.MakeJump(satHub.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Dynamic child-hub text", UiaSearch.MakeJump(dynText.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Angle long wedge labels", UiaSearch.MakeJump(rotate.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Dragged-out item bubble size", UiaSearch.MakeJump(chip.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.size", "Max wedges", UiaSearch.MakeJump(maxW.transform.parent.gameObject, reveal));
        }

        // ---------- Radial feel ----------

        private void BuildFeel(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.feel", "Radial feel");

            // "Flick to commit" + its window died in the post-0.9.2.5 play-test round — a second,
            // racier route into "commit a wedge" that intermittently ate MMB. Hold-open, point,
            // release IS that gesture already, and that path is untouched.
            var doubleTap = UiaControls.ToggleRow(sec.Body, "Double-tap repeats last pick", UIAConfig.RadialDoubleTapRepeat.Value,
                v => UIAConfig.RadialDoubleTapRepeat.Value = v);
            var doubleTapMs = UiaControls.SliderRow(sec.Body, "Double-tap window", 120f, 500f, UIAConfig.RadialDoubleTapMs.Value,
                v => UIAConfig.RadialDoubleTapMs.Value = Mathf.RoundToInt(v), "0", 10f);
            var sounds = UiaControls.ToggleRow(sec.Body, "Wedge hover / click sounds", UIAConfig.RadialWedgeSounds.Value,
                v => UIAConfig.RadialWedgeSounds.Value = v);
            var fade = UiaControls.ToggleRow(sec.Body, "Fade key-hints as you learn them", UIAConfig.RadialHintFade.Value,
                v => UIAConfig.RadialHintFade.Value = v);
            var resetHints = UiaControls.Button(sec.Body, "Reset hint counters", HintUsageStore.ResetCounters, -1f, UiaTheme.RowH);

            UiaSearch.RegisterRow(Title, "radial.feel", "Double-tap repeats last pick", UiaSearch.MakeJump(doubleTap.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.feel", "Double-tap window", UiaSearch.MakeJump(doubleTapMs.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.feel", "Wedge hover / click sounds", UiaSearch.MakeJump(sounds.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.feel", "Fade key-hints", UiaSearch.MakeJump(fade.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.feel", "Reset hint counters", UiaSearch.MakeJump(resetHints.gameObject));
        }

        // ---------- Toolbelt wheel ----------

        private void BuildToolbeltWheel(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.toolbelt", "Toolbelt wheel");

            var homeSlots = UiaControls.ToggleRow(sec.Body, "Remember each tool's home slot", UIAConfig.ToolbeltHomeSlots.Value,
                v => UIAConfig.ToolbeltHomeSlots.Value = v);
            var stableGeo = UiaControls.ToggleRow(sec.Body, "Stable layout: reserve empty slots + show binding labels",
                UIAConfig.ToolbeltStableGeometry.Value, v => UIAConfig.ToolbeltStableGeometry.Value = v);
            var showStow = UiaControls.ToggleRow(sec.Body, "Show empty belt slots in the toolbelt wheel",
                UIAConfig.ToolbeltShowStowEntries.Value, v => UIAConfig.ToolbeltShowStowEntries.Value = v);
            // the post-0.9.2.5 play-test round: the curved/straight choice is gone (curved is the
            // only rendering — it was always the intended look). What replaced it is an on/off for
            // the labels themselves.
            var bindLbl = UiaControls.ToggleRow(sec.Body, "Show bound-tool labels",
                UIAConfig.RadialShowBindingLabels.Value, v => { UIAConfig.RadialShowBindingLabels.Value = v; HudProfileStore.MarkThemeChanged(); });
            UiaControls.Note(sec.Body,
                "The bound-tool name is the dim grey ghost on a reserved (empty-but-bound) belt " +
                "slot - it sits at the bottom of its wedge, bent around the arc where the hub " +
                "starts. Off hides it entirely. Its colour is \"TextBindingLabel\" in the palette.");

            UiaSearch.RegisterRow(Title, "radial.toolbelt", "Remember each tool's home slot", UiaSearch.MakeJump(homeSlots.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.toolbelt", "Stable layout", UiaSearch.MakeJump(stableGeo.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.toolbelt", "Show empty belt slots", UiaSearch.MakeJump(showStow.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.toolbelt", "Show bound-tool labels", UiaSearch.MakeJump(bindLbl.transform.parent.gameObject));
        }

        // ---------- Bag wheel ----------
        // The Universal Inventory group moved to F10 > Storage in the post-0.9.2.5 play-test round
        // (FlorpyDorp: "it has nothing to do with the radials").

        private void BuildBagWheel(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.bags", "Bag wheel", null, true);

            var tapOpens = UiaControls.ToggleRow(sec.Body, "Tap opens bag wheel / hold shows scoreboard",
                UIAConfig.BagRadialTapOpens.Value, v => UIAConfig.BagRadialTapOpens.Value = v);
            var grouping = UiaControls.ToggleRow(sec.Body, "Group crowded bags by category", UIAConfig.BagGrouping.Value,
                v => UIAConfig.BagGrouping.Value = v);

            UiaSearch.RegisterRow(Title, "radial.bags", "Tap opens bag wheel", UiaSearch.MakeJump(tapOpens.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.bags", "Group crowded bags by category", UiaSearch.MakeJump(grouping.transform.parent.gameObject));

            var ex = sec.Extras;
            if (ex == null) return;
            Action reveal = () => sec.SetExpanded(true);

            var empty = new List<string> { "Empty slots only", "Stow wedge + empty slots", "Stow wedge only" };
            var freeSpace = UiaControls.DropdownRow(ex, "Free space shown as", empty, (int)UIAConfig.BagEmptySlots.Value,
                i => UIAConfig.BagEmptySlots.Value = (EmptySlotMode)i);
            UiaSearch.RegisterRow(Title, "radial.bags", "Free space shown as", UiaSearch.MakeJump(freeSpace.transform.parent.gameObject, reveal));
        }

        // ---------- Hub readout text ----------

        private void BuildHubText(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.text", "Hub readout text",
                "The five text sizes the hub can show, plus the radial's font. Niche tuning - " +
                "collapsed by default.", true);
            UiaControls.Note(sec.Body, "Text sizes and the radial font live under More options.");

            var ex = sec.Extras;
            if (ex == null) return;
            Action reveal = () => sec.SetExpanded(true);

            var s1 = UiaControls.SliderRow(ex, "1 - Title (bold)", 9f, 32f, UIAConfig.RadialHubTitleSize.Value,
                v => { UIAConfig.RadialHubTitleSize.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            var s2 = UiaControls.SliderRow(ex, "2 - Action verb", 8f, 26f, UIAConfig.RadialTextVerb.Value,
                v => { UIAConfig.RadialTextVerb.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            var s3 = UiaControls.SliderRow(ex, "3 - Item name", 8f, 26f, UIAConfig.RadialTextLabel.Value,
                v => { UIAConfig.RadialTextLabel.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            var s4 = UiaControls.SliderRow(ex, "4 - Detail line", 8f, 24f, UIAConfig.RadialTextSub.Value,
                v => { UIAConfig.RadialTextSub.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");
            var s5 = UiaControls.SliderRow(ex, "5 - Stat / warning", 8f, 24f, UIAConfig.RadialTextWarn.Value,
                v => { UIAConfig.RadialTextWarn.Value = v; HudProfileStore.MarkThemeChanged(); }, "0");

            var fonts = new List<string> { "(auto: prefer bold)" };
            fonts.AddRange(CachedFontNames());
            string curFont = UIAConfig.RadialFontName.Value;
            int fontIdx = string.IsNullOrEmpty(curFont) ? 0 : Mathf.Max(0, fonts.IndexOf(curFont));
            var fontDd = UiaControls.DropdownRow(ex, "Radial font", fonts, fontIdx, i =>
            {
                // RadialFontName is a "radial:" theme key (design §5.2) — an explicit pick marks
                // the active profile's theme dirty, same as every slider above.
                UIAConfig.RadialFontName.Value = i <= 0 ? "" : fonts[i];
                HudProfileStore.MarkThemeChanged();
            });

            UiaSearch.RegisterRow(Title, "radial.text", "Readout 1: title", UiaSearch.MakeJump(s1.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.text", "Readout 2: action verb", UiaSearch.MakeJump(s2.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.text", "Readout 3: item name", UiaSearch.MakeJump(s3.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.text", "Readout 4: detail line", UiaSearch.MakeJump(s4.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.text", "Readout 5: stat / warning", UiaSearch.MakeJump(s5.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.text", "Radial font", UiaSearch.MakeJump(fontDd.transform.parent.gameObject, reveal));
        }

        /// <summary>The radial font list, gesture-cached (see the field doc above).</summary>
        private static List<string> CachedFontNames()
        {
            if (_fontNamesCache != null && UiaControlCenter.IsRestyling) return _fontNamesCache;
            _fontNamesCache = UI.UnityRadialView.AllFontNames();
            return _fontNamesCache;
        }

        // ---------- Wheel colours & glass ----------

        private void BuildColoursAndGlass(Transform col)
        {
            var sec = UiaComposite.Section(col, "radial.colours", "Wheel colours & glass",
                "The full wedge/hub/text palette, border-line tuning and Undo/Redo live under " +
                "More options - a click opens a small colour editor next to the swatch.", true);

            bool tierC = HudConfig.FxTierC != null && HudConfig.FxTierC.Value;
            bool bundle = HudShaderStore.TierBAvailable;
            var frost = UiaControls.ToggleRow(sec.Body, "Frosted glass behind wedges (blurred screen)",
                UIAConfig.RadialFrost.Value, v =>
                {
                    UIAConfig.RadialFrost.Value = v;
                    HudProfileStore.MarkThemeChanged();
                    UiaControlCenter.Refresh(); // the status note below depends on this value
                });
            var frostStrength = UiaControls.SliderRow(sec.Body, "  frost strength", 0f, 1f, UIAConfig.RadialFrostStrength.Value,
                v => { UIAConfig.RadialFrostStrength.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            if (UIAConfig.RadialFrost.Value)
            {
                string status = !tierC ? "Frost inactive: turn ON HUD Tier C (F9 > Effects)."
                    : !bundle ? "Frost inactive: shader bundle not loaded (restart after a rebuild)."
                    : !HudBackdrop.Active ? "Frost warming up: needs the Visor HUD running (Flat/VertexWarp)."
                    : "Frost active.";
                UiaControls.Note(sec.Body, status);
            }
            var sheen = UiaControls.SliderRow(sec.Body, "Glass sheen (whiten toward the rim)", 0f, 1f, UIAConfig.RadialSheen.Value,
                v => { UIAConfig.RadialSheen.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");
            var edgeLight = UiaControls.SliderRow(sec.Body, "Edge light (rim faces the key light)", 0f, 1f, UIAConfig.RadialEdgeLight.Value,
                v => { UIAConfig.RadialEdgeLight.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.00");

            UiaSearch.RegisterRow(Title, "radial.colours", "Frosted glass behind wedges", UiaSearch.MakeJump(frost.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.colours", "Frost strength", UiaSearch.MakeJump(frostStrength.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.colours", "Glass sheen", UiaSearch.MakeJump(sheen.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "radial.colours", "Edge light", UiaSearch.MakeJump(edgeLight.transform.parent.gameObject));

            var ex = sec.Extras;
            if (ex == null) return;
            Action reveal = () => sec.SetExpanded(true);

            // Border-line tuning sits beside the colours it draws with.
            var sideOn = UiaControls.ToggleRow(ex, "Borders on wedge SIDE edges (full outline)", UIAConfig.RadialSideBorders.Value,
                v => { UIAConfig.RadialSideBorders.Value = v; HudProfileStore.MarkThemeChanged(); });
            var sideInner = UiaControls.SliderRow(ex, "Side line width at hub (px)", 0.5f, 12f, UIAConfig.RadialSideWidthInner.Value,
                v => { UIAConfig.RadialSideWidthInner.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.0");
            var sideOuter = UiaControls.SliderRow(ex, "Side line width at rim (px)", 0.5f, 12f, UIAConfig.RadialSideWidthOuter.Value,
                v => { UIAConfig.RadialSideWidthOuter.Value = v; HudProfileStore.MarkThemeChanged(); }, "0.0");
            UiaSearch.RegisterRow(Title, "radial.colours", "Borders on wedge side edges", UiaSearch.MakeJump(sideOn.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.colours", "Side line width at hub", UiaSearch.MakeJump(sideInner.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.colours", "Side line width at rim", UiaSearch.MakeJump(sideOuter.transform.parent.gameObject, reveal));

            UiaUi.Go("sp-colours", ex).AddComponent<LayoutElement>().preferredHeight = 4f;
            var undoRow = UiaUi.Go("undoredo", ex);
            UiaUi.Size(undoRow, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)undoRow.transform, UiaTheme.Gap);
            var undoBtn = UiaControls.Button(undoRow.transform, "Undo", () =>
            {
                RadialPalette.History.Undo();
                HudProfileStore.MarkThemeChanged();
            }, 90f, UiaTheme.RowH);
            var redoBtn = UiaControls.Button(undoRow.transform, "Redo", () =>
            {
                RadialPalette.History.Redo();
                HudProfileStore.MarkThemeChanged();
            }, 90f, UiaTheme.RowH);
            UiaSearch.RegisterRow(Title, "radial.colours", "Undo colour edit", UiaSearch.MakeJump(undoBtn.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "radial.colours", "Redo colour edit", UiaSearch.MakeJump(redoBtn.gameObject, reveal));

            var resetCb = UiaComposite.ConfirmButton(ex, "Reset all colours to defaults", () =>
            {
                RadialPalette.ResetToDefaults(); // pushes its own undo step
                HudProfileStore.MarkThemeChanged();
            }, -1f, UiaTheme.RowH);
            UiaSearch.RegisterRow(Title, "radial.colours", "Reset all colours to defaults", UiaSearch.MakeJump(resetCb.gameObject, reveal));

            UiaControls.Note(ex, "Click a swatch to open a small colour editor (R/G/B/A). One editing session is one Undo step.");
            foreach (var entry in RadialPalette.All)
            {
                string header = GroupHeaderFor(entry.Name);
                if (header != null) UiaControls.Header(ex, header);
                var row = ColorRow(ex, entry);
                UiaSearch.RegisterRow(Title, "radial.colours", entry.Name, UiaSearch.MakeJump(row, reveal));
            }

            UiaUi.Go("sp-legacy", ex).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Note(ex,
                "The legacy radial editor additionally previews colours live over a black screen " +
                "(with hover-highlighted swatches) and styles the key-hint strip - open it for those.");
            var legacyBtn = UiaControls.Button(ex, "Open the legacy radial editor", OpenRadialEditor, -1f, UiaTheme.RowH);
            UiaSearch.RegisterRow(Title, "radial.colours", "Open the legacy radial editor", UiaSearch.MakeJump(legacyBtn.gameObject, reveal));
        }

        /// <summary>A sub-header immediately before the first entry of a colour group (mirrors
        /// <see cref="RadialPalette.Bind"/>'s own grouping comments) — null for every entry that
        /// continues its group.</summary>
        private static string GroupHeaderFor(string entryName)
        {
            switch (entryName)
            {
                case "WedgeBackground": return "Wedges";
                case "WedgeBorder": return "Borders & rim";
                case "GroupWedgeFill": return "Category group wedges";
                case "DeviceSlotBorderColor": return "Device-slot wedges";
                case "HubFill": return "Hub";
                case "TextPrimary": return "Text";
                case "HintBarFill": return "Hint bar";
                case "ArcPlateFill": return "Curved wheel labels (follow the theme)";
                default: return null;
            }
        }

        /// <summary>One colour row: label + a live swatch that opens a small R/G/B/A editor
        /// popup on click. Returns the row's GameObject (for search registration).</summary>
        private static GameObject ColorRow(Transform parent, RadialPalette.Entry entry)
        {
            var row = UiaUi.Go("colorrow", parent);
            // Hand-built row: give it its own floor (Kit v2 visual fix wave) rather than rely
            // solely on whatever minimum UiaUi.Size/Button/Note get centrally.
            UiaUi.Size(row, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TMPro.TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.LabelSize; lbl.color = UiaTheme.Text;
            lbl.alignment = TMPro.TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = entry.Name;
            UiaControls.FitText(lbl, 9f);   // no ellipsis: shrink, then 2 lines in the RowH row
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (entry.FollowsTheme && entry.IsAuto)
            {
                var autoTxt = UiaUi.Text(row.transform, "AUTO", UiaTheme.SmallSize, UiaTheme.TextDim, TMPro.TextAlignmentOptions.Right);
                UiaUi.Size(autoTxt.gameObject, UiaTheme.RowH, 40f, flexW: 0f).minHeight = UiaTheme.RowH;
            }

            var swatchGo = UiaUi.Go("swatch", row.transform);
            UiaUi.Size(swatchGo, 22f, 44f, flexW: 0f).minHeight = 22f;
            var simg = swatchGo.AddComponent<Image>();
            simg.color = entry.Value;
            UiaImages.Round(simg);
            UiaUi.OutlineOf(simg, UiaTheme.Border, 1f);
            var driver = swatchGo.AddComponent<ColorSwatchDriver>();
            driver.Entry = entry;
            driver.Img = simg;

            return row;
        }

        /// <summary>Repaints its swatch from the live entry value every frame (so an edit in the
        /// popup shows immediately with no tab rebuild) and opens the colour popup on click.</summary>
        private sealed class ColorSwatchDriver : MonoBehaviour, IPointerClickHandler
        {
            public RadialPalette.Entry Entry;
            public Image Img;

            private void LateUpdate()
            {
                if (Img != null && Entry != null) Img.color = Entry.Value;
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (e != null && e.button != PointerEventData.InputButton.Left) return;
                OpenColorPopup((RectTransform)transform, Entry);
            }
        }

        // ---------- colour popup (this tab's own transient) ----------

        private static void OpenColorPopup(RectTransform anchor, RadialPalette.Entry entry)
        {
            var layer = UiaControls.PopupLayer;
            if (layer == null || anchor == null || entry == null) return;
            CloseColorPopup(); // commits any prior session's undo step first (via OnDestroy)

            _colorCatcher = UiaUi.Go("radial-color-catcher", layer);
            var cimg = _colorCatcher.AddComponent<Image>();
            cimg.color = new Color(0f, 0f, 0f, 0.001f);
            UiaUi.Fill((RectTransform)_colorCatcher.transform);
            _colorCatcher.transform.SetAsLastSibling();
            _colorCatcher.AddComponent<UiaControls.UiaButton>()
                .Init(cimg, cimg.color, cimg.color, cimg.color).OnClick = CloseColorPopup;

            _colorPopup = UiaUi.Go("radial-color-popup", layer);
            var prt = (RectTransform)_colorPopup.transform;
            var bg = _colorPopup.AddComponent<Image>();
            bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, UiaTheme.Border, 1f);
            prt.SetAsLastSibling();

            // Commits its ONE undo step on OnDestroy — fires no matter who destroys the popup
            // (our own CloseColorPopup, the window's Close()/Restyle() popup-layer sweep, or a
            // Shutdown/hot-reload root-destroy cascade), so an abrupt close can never drop the
            // session's undo step the way a plain static snapshot could.
            var lifetime = _colorPopup.AddComponent<ColorPopupCommitOnDestroy>();
            lifetime.PreEdit = RadialPalette.Snapshot();

            var body = UiaUi.Fill(prt, 8f);
            UiaUi.VLayout(body, 4f);

            var title = UiaUi.Text(body, entry.Name, UiaTheme.SmallSize, UiaTheme.Accent, TMPro.TextAlignmentOptions.Left);
            UiaUi.Size(title.gameObject, 18f).minHeight = 18f;

            UiaControls.SliderRow(body, "R", 0f, 255f, entry.Value.r * 255f, v => SetChannel(entry, 0, v), "0", 1f);
            UiaControls.SliderRow(body, "G", 0f, 255f, entry.Value.g * 255f, v => SetChannel(entry, 1, v), "0", 1f);
            UiaControls.SliderRow(body, "B", 0f, 255f, entry.Value.b * 255f, v => SetChannel(entry, 2, v), "0", 1f);
            UiaControls.SliderRow(body, "A (transparency)", 0f, 255f, entry.Value.a * 255f, v => SetChannel(entry, 3, v), "0", 1f);

            float height = 18f + 4f * (UiaTheme.RowH + 4f) + 20f;
            if (entry.FollowsTheme)
            {
                UiaControls.Button(body, entry.IsAuto ? "Following the theme (AUTO)" : "Use theme colour (AUTO)", () =>
                {
                    entry.Config.Value = RadialPalette.Auto;
                    HudProfileStore.MarkThemeChanged();
                    CloseColorPopup();
                }, -1f, UiaTheme.RowH);
                height += UiaTheme.RowH + 4f;
            }

            UiaComposite.PlacePopup(prt, anchor, layer, 250f, height);

            // Esc closes just the popup (committing its undo step) instead of all of F10 while
            // an edit is in progress.
            EnsureEscInterceptor();
        }

        private static void SetChannel(RadialPalette.Entry entry, int channel, float v255)
        {
            Color c = entry.Value;
            float v = Mathf.Clamp01(v255 / 255f);
            switch (channel)
            {
                case 0: c.r = v; break;
                case 1: c.g = v; break;
                case 2: c.b = v; break;
                default: c.a = v; break;
            }
            entry.Value = c;
            // Radial colours belong to the active HUD profile's theme too (they follow the chosen
            // UI theme even when the visor HUD itself is turned off).
            HudProfileStore.MarkThemeChanged();
        }

        /// <summary>Close the popup (idempotent — safe to call when nothing is open). The undo
        /// commit itself lives in <see cref="ColorPopupCommitOnDestroy.OnDestroy"/>, which fires
        /// on this Destroy call just the same as it would on a sweep-destroyed layer or a
        /// Shutdown root-destroy — one code path, no matter who tears the popup down.</summary>
        private static void CloseColorPopup()
        {
            if (_colorPopup != null) UnityEngine.Object.Destroy(_colorPopup);
            if (_colorCatcher != null) UnityEngine.Object.Destroy(_colorCatcher);
            _colorPopup = null;
            _colorCatcher = null;
            ReleaseEscInterceptor();
        }

        /// <summary>Lives on the popup GameObject; commits ONE <see cref="RadialPalette.History"/>
        /// undo step for its whole editing session on destroy, IF anything actually changed (a
        /// session that only opened and closed pushes nothing). Because this runs from
        /// <c>OnDestroy</c> rather than from a caller-driven "close" method, the commit survives
        /// every teardown path that can end this popup's life — our own <see cref="CloseColorPopup"/>,
        /// the window's Close()/Restyle() popup-layer sweep (which used to just Destroy() this
        /// GameObject directly and drop the pending undo silently), or a Shutdown/hot-reload
        /// root-destroy cascade.</summary>
        private sealed class ColorPopupCommitOnDestroy : MonoBehaviour
        {
            public Dictionary<string, string> PreEdit;

            private void OnDestroy()
            {
                if (PreEdit == null) return;
                var now = RadialPalette.Snapshot();
                bool changed = false;
                foreach (var kv in now)
                {
                    string before;
                    if (!PreEdit.TryGetValue(kv.Key, out before) || !string.Equals(before, kv.Value, StringComparison.Ordinal))
                    { changed = true; break; }
                }
                if (changed) RadialPalette.History.PushUndo(PreEdit);
            }
        }

        private static void EnsureEscInterceptor()
        {
            if (_escRegistered) return;
            _escRegistered = true;
            UiaControlCenter.RegisterEscInterceptor(_escInterceptor);
        }

        private static void ReleaseEscInterceptor()
        {
            if (!_escRegistered) return;
            _escRegistered = false;
            UiaControlCenter.UnregisterEscInterceptor(_escInterceptor);
        }

        /// <summary>The F10 Esc-close interceptor: while a colour popup is open, Esc closes just
        /// the popup (committing its undo step through <see cref="ColorPopupCommitOnDestroy"/>)
        /// and consumes the key, so it never closes the whole window out from under an
        /// in-progress edit. Self-heals the registration if the popup died some OTHER way (e.g.
        /// a live theme Restyle destroyed it mid-edit): the next Esc press after that finds
        /// <see cref="_colorPopup"/> already gone (Unity's destroyed-object-equals-null) and
        /// unregisters instead of consuming, so a stale registration can never eat a real
        /// window-close Esc.</summary>
        private static bool TryConsumeEsc()
        {
            if (_colorPopup == null) { ReleaseEscInterceptor(); return false; }
            CloseColorPopup();
            return true;
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

        /// <summary>Hot-reload / shutdown teardown (called from UiaControlCenter.Shutdown). The
        /// popup GameObjects already die when the window's root is destroyed (they are children
        /// of the popup layer, and that destroy commits any pending undo via
        /// <see cref="ColorPopupCommitOnDestroy"/>) — this drops the dangling static references
        /// AND unhooks the Esc interceptor, so a reloaded assembly never leaves a dead delegate
        /// registered in <see cref="UiaControlCenter"/>'s list (the hot-reload house rule for
        /// static event hooks).</summary>
        public static void ResetTransients()
        {
            ReleaseEscInterceptor();
            _colorPopup = null;
            _colorCatcher = null;
            _fontNamesCache = null;
        }
    }
}
