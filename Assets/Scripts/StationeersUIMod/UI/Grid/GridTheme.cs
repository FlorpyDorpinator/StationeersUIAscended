using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The Universal Inventory window's THEME SOURCE — the <see cref="Menu.Kit.UiaMenuTheme"/>
    /// pattern applied to the Grid. Two states:
    ///  • FOLLOW (default): the window is skinned from the GLOBAL box theme — the F9 palette's
    ///    <see cref="HudPalette.PanelFill"/> / <see cref="HudPalette.PanelBorder"/> /
    ///    <see cref="HudPalette.TextValue"/> plus <see cref="HudConfig"/>'s glass globals
    ///    (CornerRadius / GlassSheen / GlassEdge / BorderWidth) — so re-theming the visor
    ///    re-skins the Grid live, exactly like a follow-global Box.
    ///  • OVERRIDE: per-value config. Colours are ColorRef strings (a palette entry NAME or a
    ///    "#RRGGBBAA" literal, resolved by <see cref="HudPalette.Resolve"/>, so an override can
    ///    itself LINK to a palette entry). Floats use the HUD's -1 = "inherit the global"
    ///    convention, so a single knob can be overridden without pinning the rest.
    ///
    /// NOTE the Grid window is deliberately NOT a HUD document element (it is a modal,
    /// raycasting, un-warped, on-demand overlay on its own canvas), so its style cannot live in
    /// a profile's element list; and the palette/glass globals are SHARED config rather than
    /// per-profile. "Follows the profile" therefore holds only insofar as switching profiles
    /// changes those globals.
    ///
    /// Config only — no Unity objects, nothing to reset on F6 beyond the re-<see cref="Bind"/>.
    /// The getters are live, so the built window restyles whenever <see cref="StyleHash"/>
    /// changes (TheGridPanel polls it).
    /// </summary>
    public static class GridTheme
    {
        private const string Section = "13. The Grid";

        // ---- Surface variants -----------------------------------------------------------------
        // Every Grid box is one of these. A variant NEVER carries an absolute style value: it only
        // carries SCALES applied to the inherited theme (border width, corner radius) plus which
        // parts of the glass stack it opts into. A thin global border therefore stays thin here,
        // and a fat one stays fat, in every variant and in every hover state.

        /// <summary>Which Grid box is being painted. Drives only relative scales on the inherited
        /// theme — see <see cref="ApplyBox"/>.</summary>
        public enum GridSurface
        {
            /// <summary>The outer window shell (main window / a pinned window). The only surface
            /// that takes the glow halo and asks for the frosted backdrop, exactly as an outer HUD
            /// box does.</summary>
            Window,
            /// <summary>Small chrome buttons inside a window (close, shrink, sort) and the resize
            /// grip's plate: a tighter corner and a slightly finer line than the shell.</summary>
            Button,
            /// <summary>A top-level region box inside the list.</summary>
            Region,
            /// <summary>A nested (depth &gt; 0) region box — the same line, scaled down so nesting
            /// reads as recession rather than as a second, heavier frame.</summary>
            RegionNested,
            /// <summary>A tab in the tab strip.</summary>
            Tab,
            /// <summary>The tab drag ghost.</summary>
            TabGhost,
            /// <summary>One inventory CELL. The most numerous surface on screen, so it takes the
            /// finest line of all — and its four interaction states are scales on that line
            /// (<see cref="CellState"/>), never absolute widths.</summary>
            Cell
        }

        /// <summary>A cell's interaction state. Each maps to a SCALE on the inherited
        /// <see cref="BorderWidth"/> plus a palette colour — see <see cref="ApplyBox(PanelGraphic,
        /// float, float, CellState)"/>. A thin global border therefore stays thin in every state.</summary>
        public enum CellState
        {
            /// <summary>Nothing pointing at it: the inherited line, unscaled.</summary>
            Idle,
            /// <summary>Pointer over the cell.</summary>
            Hover,
            /// <summary>A move is in flight / this cell is the hovered drop target of a live
            /// drag — the transient "something is about to land here" cue.</summary>
            Pending,
            /// <summary>The local player's ACTIVE hand — a persistent, always-on identity cue.</summary>
            ActiveHand,
            /// <summary>The keyboard/scroll-select CURSOR (#4) is on this cell — the heaviest,
            /// most prominent line of all, so the wheel cursor reads at a glance over hover and
            /// even the active-hand accent.</summary>
            Selected
        }

        public static ConfigEntry<bool> Follow;

        // Colour overrides (ColorRef strings). Used only when NOT following.
        public static ConfigEntry<string> FillRef;
        public static ConfigEntry<string> BorderRef;
        public static ConfigEntry<string> TextRef;

        // Float overrides. -1 = follow the global (works in either state, so a follower can
        // still be nudged only by unchecking Follow — kept uniform with the HUD's convention).
        public static ConfigEntry<float> CornerRadiusOv;
        public static ConfigEntry<float> SheenOv;
        public static ConfigEntry<float> SpecOv;

        // --- The FULL override block. Same -1 = inherit convention throughout; the tri-state
        // modes are 0 = Inherit / 1 = On / 2 = Off (an int, because a bool cannot carry
        // "inherit" — the element popup's transition tri-state pattern). Every knob is inert
        // while Following AND at its sentinel, so the default state stays byte-identical to a
        // follow-global HUD box. Amount overrides >= 0 replace their global toggle+amount PAIR
        // (0 = force off), so one slider covers both without a second checkbox.
        public static ConfigEntry<float> BorderWidthOv;
        public static ConfigEntry<float> FeatherOv;
        public static ConfigEntry<float> SquircleOv;
        public static ConfigEntry<int> GaussianMode;
        public static ConfigEntry<float> BorderFadeOv;
        public static ConfigEntry<float> SoftEdgeOv;

        public static ConfigEntry<int> GlowMode;
        public static ConfigEntry<float> GlowOv;
        public static ConfigEntry<float> GlowInnerOv;
        public static ConfigEntry<float> GlowHazeOv;
        public static ConfigEntry<float> GlowWidthOv;
        public static ConfigEntry<float> GlowDiffuseOv;
        public static ConfigEntry<float> GlowExtraDiffuseOv;

        public static ConfigEntry<int> RippleMode;
        public static ConfigEntry<float> RippleOv;
        public static ConfigEntry<float> RippleFreqOv;
        public static ConfigEntry<float> RippleSmoothOv;
        public static ConfigEntry<float> FlowOv;

        public static ConfigEntry<bool> FrostOn;
        public static ConfigEntry<float> FrostOv;
        public static ConfigEntry<float> FrostDepthOv;

        public static ConfigEntry<float> ShineOv;
        public static ConfigEntry<float> IridOv;
        public static ConfigEntry<float> ChromaOv;

        // ---- Per-tier (suited vs bare) -------------------------------------------------------
        // FlorpyDorp, 0.9.2.5: "The Universal Inventory F9 menu needs a suit/bare mode setting as
        // well." Deliberately a SMALL subset rather than a bare twin for all 31 knobs: the real ask
        // is "the inventory shouldn't look powered when my suit isn't", and doubling a 31-row popup
        // for a modal window the player opens on purpose buys nothing. The vocabulary is the HUD
        // element model's — ColorRef strings, -1 = inherit, 0/1/2 tri-state — so it reads as one
        // system. Every entry is inert at its default, so an untouched install is unchanged.
        public static ConfigEntry<bool> PerTier;
        public static ConfigEntry<string> BareFillRef;
        public static ConfigEntry<string> BareBorderRef;
        public static ConfigEntry<string> BareTextRef;
        public static ConfigEntry<float> BareOpacity;
        public static ConfigEntry<int> BareFrostMode;

        /// <summary>The tier the Grid paints for. Fed each frame by <c>TheGridPanel.Tick</c> from
        /// <c>HudSystem.LastSnapshot.Tier</c> (Bare -> Bare, everything else -> Base); Base while
        /// the HUD is unavailable. A plain static, reset in HudSystem.Shutdown (hot-reload rule).</summary>
        public static HudStyleSlot Slot { get; set; }

        /// <summary>Whether the bare overrides are live THIS frame.</summary>
        private static bool BareActive
        {
            get { return PerTier != null && PerTier.Value && Slot == HudStyleSlot.Bare; }
        }

        /// <summary>Tri-state combo captions. A readonly literal, so an F6 reload re-initialises
        /// it with the class — no Unity state, nothing to tear down.</summary>
        private static readonly string[] InheritOnOff = { "Inherit", "On", "Off" };

        public static void Bind(ConfigFile cfg)
        {
            Follow = cfg.Bind(Section, "FollowHudTheme", true,
                "Skin the Universal Inventory window from the global HUD box theme (palette " +
                "panel fill/border/text + the glass globals). Off = use the overrides below. " +
                "Edit visually in the F9 HUD editor: open the Grid while the editor is active " +
                "and click the window.");

            FillRef = cfg.Bind(Section, "GridFill", "HudPanelFill",
                "Window background when NOT following the HUD theme. A palette entry name " +
                "(e.g. HudPanelFill) or a #RRGGBBAA literal.");
            BorderRef = cfg.Bind(Section, "GridBorder", "HudPanelBorder",
                "Window outline when NOT following the HUD theme. A palette entry name or a " +
                "#RRGGBBAA literal.");
            TextRef = cfg.Bind(Section, "GridText", "HudTextValue",
                "Window text when NOT following the HUD theme. A palette entry name or a " +
                "#RRGGBBAA literal.");

            CornerRadiusOv = cfg.Bind(Section, "GridCornerRadius", -1f,
                new ConfigDescription("Window corner rounding (px). -1 = follow the global HUD " +
                    "CornerRadius.", new AcceptableValueRange<float>(-1f, 28f)));
            SheenOv = cfg.Bind(Section, "GridGlassSheen", -1f,
                new ConfigDescription("Glass sheen (the soft top-down light gradient) on the " +
                    "window. -1 = follow the global HUD GlassSheen.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            SpecOv = cfg.Bind(Section, "GridGlassEdge", -1f,
                new ConfigDescription("Glass edge light (the bright rim highlight) on the " +
                    "window. -1 = follow the global HUD GlassEdge.",
                    new AcceptableValueRange<float>(-1f, 1f)));

            BorderWidthOv = cfg.Bind(Section, "GridBorderWidth", -1f,
                new ConfigDescription("Outline thickness (px) when NOT following the HUD " +
                    "theme. -1 = follow the global HUD BorderWidth.",
                    new AcceptableValueRange<float>(-1f, 6f)));
            FeatherOv = cfg.Bind(Section, "GridEdgeFeather", -1f,
                new ConfigDescription("Edge softness / AA (px). -1 = follow the global HUD " +
                    "EdgeFeather.", new AcceptableValueRange<float>(-1f, 4f)));
            SquircleOv = cfg.Bind(Section, "GridSquircle", -1f,
                new ConfigDescription("Corner shape on the analytic SDF window shells (2 = " +
                    "round, 8 = squircle). Anything below 2 (including -1) = follow the global " +
                    "SdfSquircleExponent — 2 is the roundest renderable shape, so the whole " +
                    "sub-2 band is the inherit sentinel, matching the per-element squircle " +
                    "contract (HudElementView.SdfSquircleFor).",
                    new AcceptableValueRange<float>(-1f, 8f)));
            GaussianMode = cfg.Bind(Section, "GridGaussianHalo", 0,
                new ConfigDescription("Gaussian distance falloff on the analytic SDF window " +
                    "shells' halo (not a blur convolution). 0 = inherit the global " +
                    "SdfGaussianHalo toggle, 1 = force on, 2 = force off.",
                    new AcceptableValueRange<int>(0, 2)));
            BorderFadeOv = cfg.Bind(Section, "GridBorderFade", -1f,
                new ConfigDescription("Border fade (unlit sections dissolve). 0 = force off, " +
                    "-1 = follow the global toggle + amount.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            SoftEdgeOv = cfg.Bind(Section, "GridSoftEdge", -1f,
                new ConfigDescription("Soft edge width (px). 0 = force off, -1 = follow the " +
                    "global toggle + amount.", new AcceptableValueRange<float>(-1f, 48f)));

            GlowMode = cfg.Bind(Section, "GridGlowMode", 0,
                new ConfigDescription("Glow halo on the window shells. 0 = inherit the global " +
                    "Glow halo toggle, 1 = force on, 2 = force off. The Tier A master still " +
                    "gates.", new AcceptableValueRange<int>(0, 2)));
            GlowOv = cfg.Bind(Section, "GridGlowStrength", -1f,
                new ConfigDescription("Glow halo outward strength. -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 2f)));
            GlowInnerOv = cfg.Bind(Section, "GridGlowInner", -1f,
                new ConfigDescription("Glow halo inward strength. -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 2f)));
            GlowHazeOv = cfg.Bind(Section, "GridGlowHaze", -1f,
                new ConfigDescription("Extended atmospheric haze (analytic SDF shells only). " +
                    "-1 = follow the global.", new AcceptableValueRange<float>(-1f, 1f)));
            GlowWidthOv = cfg.Bind(Section, "GridGlowWidth", -1f,
                new ConfigDescription("Halo / aura radius (px). -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 320f)));
            GlowDiffuseOv = cfg.Bind(Section, "GridGlowDiffuse", -1f,
                new ConfigDescription("Halo spread (tight rim to diffuse). -1 = follow the " +
                    "global.", new AcceptableValueRange<float>(-1f, 1f)));
            GlowExtraDiffuseOv = cfg.Bind(Section, "GridGlowExtraDiffuse", -1f,
                new ConfigDescription("Halo extra diffuse (beyond max spread). -1 = follow " +
                    "the global.", new AcceptableValueRange<float>(-1f, 1f)));

            RippleMode = cfg.Bind(Section, "GridEdgeEnergyMode", 0,
                new ConfigDescription("Edge energy (the irregular border ripple). 0 = inherit " +
                    "the global Edge energy toggle, 1 = force on, 2 = force off. The Tier A " +
                    "master still gates; light angle/colour stay global.",
                    new AcceptableValueRange<int>(0, 2)));
            RippleOv = cfg.Bind(Section, "GridEdgeRipple", -1f,
                new ConfigDescription("Irregular edge energy amount. -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 2.5f)));
            RippleFreqOv = cfg.Bind(Section, "GridEdgeRippleFreq", -1f,
                new ConfigDescription("Edge energy frequency. -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 8f)));
            RippleSmoothOv = cfg.Bind(Section, "GridRippleSmooth", -1f,
                new ConfigDescription("Edge energy smoothness (the global path forces the " +
                    "hard, un-smoothed ripple). -1 = follow the global (0).",
                    new AcceptableValueRange<float>(-1f, 1f)));
            FlowOv = cfg.Bind(Section, "GridEdgeFlowSpeed", -1f,
                new ConfigDescription("Edge flow speed (analytic SDF shells; mesh surfaces " +
                    "follow the shared global clock). -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 4f)));

            FrostOn = cfg.Bind(Section, "GridFrostOn", true,
                "The window shells take the Tier C frosted backdrop (when the global master " +
                "and the backdrop capture are live). Off = the Grid opts out of frost.");
            FrostOv = cfg.Bind(Section, "GridFrostStrength", -1f,
                new ConfigDescription("Frost strength (analytic SDF shells; the mesh " +
                    "fallback's frost strength is a shared global). -1 = follow the global.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            FrostDepthOv = cfg.Bind(Section, "GridFrostDepth", -1f,
                new ConfigDescription("Frost blur depth (analytic SDF shells). -1 = follow " +
                    "the global.", new AcceptableValueRange<float>(-1f, 1f)));

            ShineOv = cfg.Bind(Section, "GridShine", -1f,
                new ConfigDescription("Shine sweep strength (analytic SDF shells). 0 = force " +
                    "off, -1 = follow the global toggle + strength.",
                    new AcceptableValueRange<float>(-1f, 2f)));
            IridOv = cfg.Bind(Section, "GridIridescence", -1f,
                new ConfigDescription("Iridescent rim strength (analytic SDF shells). 0 = " +
                    "force off, -1 = follow the global toggle + strength.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            ChromaOv = cfg.Bind(Section, "GridChroma", -1f,
                new ConfigDescription("Chromatic fringe strength (analytic SDF shells; needs " +
                    "live frost). 0 = force off, -1 = follow the global toggle + strength.",
                    new AcceptableValueRange<float>(-1f, 1f)));

            PerTier = cfg.Bind(Section, "GridPerTierStyle", false,
                "Give the Universal Inventory a SEPARATE look while you are bare (suit power off / " +
                "no helmet). Off = one skin at every tier. The five overrides below are inert " +
                "until this is on, and each one falls back to the normal skin when left blank/-1.");
            BareFillRef = cfg.Bind(Section, "GridBareFill", "",
                "Window background while BARE. A palette entry name or a #RRGGBBAA literal. " +
                "Empty = inherit the normal window fill.");
            BareBorderRef = cfg.Bind(Section, "GridBareBorder", "",
                "Window outline while BARE. Empty = inherit the normal window border.");
            BareTextRef = cfg.Bind(Section, "GridBareText", "",
                "Window text while BARE. Empty = inherit the normal window text colour.");
            BareOpacity = cfg.Bind(Section, "GridBareOpacity", -1f,
                new ConfigDescription("Multiplies the RESOLVED alpha of the bare fill/border/text " +
                    "(0 = invisible, 1 = unchanged). -1 = inherit, i.e. no multiply. The cheap way " +
                    "to say 'the same window, unpowered'.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            BareFrostMode = cfg.Bind(Section, "GridBareFrost", 0,
                new ConfigDescription("Frosted backdrop while BARE. 0 = inherit the normal Grid " +
                    "frost setting, 1 = force on, 2 = force off. The Tier C master and a live " +
                    "backdrop capture still gate.", new AcceptableValueRange<int>(0, 2)));

            // The cached F9 popup descriptor lists close over ConfigEntry references from THIS
            // Bind — drop them so a re-Bind can never serve closures over stale entries.
            _propsFollow = null;
            _propsOverride = null;
        }

        public static bool Following { get { return Follow == null || Follow.Value; } }

        // ---- Hardcoded last-resort defaults (used before Bind and when a ref won't resolve) --

        private static readonly Color _defFill = new Color(0.05f, 0.07f, 0.09f, 0.88f);
        private static readonly Color _defBorder = new Color(0.35f, 0.78f, 0.90f, 0.65f);
        private static readonly Color _defText = new Color(0.94f, 0.97f, 1.00f, 1.00f);

        private static Color GlobalFill
        {
            get { return HudPalette.PanelFill != null ? HudPalette.PanelFill.Value : _defFill; }
        }

        private static Color GlobalBorder
        {
            get { return HudPalette.PanelBorder != null ? HudPalette.PanelBorder.Value : _defBorder; }
        }

        private static Color GlobalText
        {
            get { return HudPalette.TextValue != null ? HudPalette.TextValue.Value : _defText; }
        }

        // ---- Resolved getters (what TheGridPanel actually paints with) ----------------------

        /// <summary>Apply the bare tier's alpha multiplier, if one is set. Separate from the ref
        /// override so "the same colours, dimmer" needs one slider and no colour picking.</summary>
        private static Color BareAlpha(Color c)
        {
            if (!BareActive || BareOpacity == null || BareOpacity.Value < 0f) return c;
            return new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(BareOpacity.Value));
        }

        /// <summary>The bare override for one colour: the ref when per-tier is live AND the ref is
        /// non-empty, else the normal resolution. Alpha multiply applies either way.</summary>
        private static Color BareOr(ConfigEntry<string> bareRef, Color normal)
        {
            if (BareActive && bareRef != null && !string.IsNullOrEmpty(bareRef.Value))
                return BareAlpha(HudPalette.Resolve(bareRef.Value, normal));
            return BareAlpha(normal);
        }

        public static Color Fill
        {
            get
            {
                Color normal = (Following || FillRef == null)
                    ? GlobalFill : HudPalette.Resolve(FillRef.Value, GlobalFill);
                return BareOr(BareFillRef, normal);
            }
        }

        public static Color Border
        {
            get
            {
                Color normal = (Following || BorderRef == null)
                    ? GlobalBorder : HudPalette.Resolve(BorderRef.Value, GlobalBorder);
                return BareOr(BareBorderRef, normal);
            }
        }

        public static Color Text
        {
            get
            {
                Color normal = (Following || TextRef == null)
                    ? GlobalText : HudPalette.Resolve(TextRef.Value, GlobalText);
                return BareOr(BareTextRef, normal);
            }
        }

        public static float CornerRadius
        {
            get { return Flt(CornerRadiusOv, HudConfig.CornerRadius, 10f); }
        }

        public static float Sheen
        {
            get { return Flt(SheenOv, HudConfig.GlassSheen, 0f); }
        }

        /// <summary>The rim/edge highlight — maps to <see cref="IGlassSurface.Spec"/>, whose
        /// global is <c>HudConfig.GlassEdge</c>.</summary>
        public static float Spec
        {
            get { return Flt(SpecOv, HudConfig.GlassEdge, 0f); }
        }

        /// <summary>Outline thickness: the -1-sentinel override when NOT following, else the
        /// global <c>HudConfig.BorderWidth</c>. Every surface's width is a SCALE on this.</summary>
        public static float BorderWidth
        {
            get { return Flt(BorderWidthOv, HudConfig.BorderWidth, 1.4f); }
        }

        /// <summary>Whether the window shells opt into the Tier C frosted backdrop. A plain bool
        /// rather than a tri-state: a shell already always WANTS frost while following, so
        /// "inherit" and "on" would be the same state — the only meaningful override is opting
        /// out. The global Tier C master and a live backdrop capture still gate.</summary>
        public static bool FrostParticipates
        {
            get
            {
                if (BareActive && BareFrostMode != null && BareFrostMode.Value != 0)
                    return BareFrostMode.Value == 1;
                return Following || FrostOn == null || FrostOn.Value;
            }
        }

        private static float Flt(ConfigEntry<float> ov, ConfigEntry<float> global, float hard)
        {
            float g = global != null ? global.Value : hard;
            if (Following || ov == null || ov.Value < 0f) return g;
            return ov.Value;
        }

        /// <summary>Resolve a 0/1/2 Inherit/On/Off tri-state against its global toggle. Inert
        /// (the global) while Following, like every other override.</summary>
        private static bool ModeOn(ConfigEntry<int> mode, ConfigEntry<bool> global)
        {
            bool g = global != null && global.Value;
            if (Following || mode == null) return g;
            if (mode.Value == 1) return true;
            if (mode.Value == 2) return false;
            return g;
        }

        /// <summary>Resolve a strength the GLOBAL side expresses as a toggle + an amount. An
        /// override >= 0 replaces BOTH (0 = force off, so one slider covers the pair); -1 (or
        /// Following) resolves the global's toggle-gated amount.</summary>
        private static float GatedAmt(ConfigEntry<float> ov, ConfigEntry<bool> gOn,
            ConfigEntry<float> gAmt, float hard)
        {
            if (!Following && ov != null && ov.Value >= 0f) return ov.Value;
            if (gOn == null || !gOn.Value) return 0f;
            return gAmt != null ? gAmt.Value : hard;
        }

        /// <summary>The Tier-A edge-light spec boost (the same +FxEdgeLight*0.45 a following HUD
        /// box gets from <see cref="HudGlobalGlass.Apply"/>), gated on the RESOLVED edge-energy
        /// tri-state rather than the raw global toggle: forcing 'Edge energy' Off in the Grid
        /// popup removes the rim brightening along with the ripple, so a 'Glass edge light'
        /// override of 0 really renders a flat border. While Following, <see cref="ModeOn"/>
        /// collapses to the global toggle and the boost is byte-identical to Apply's.</summary>
        private static float EdgeLightBoost()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            if (!tierA || !ModeOn(RippleMode, HudConfig.FxEdgeLightOn)) return 0f;
            float s = HudConfig.FxEdgeLight != null ? HudConfig.FxEdgeLight.Value : 0f;
            return s > 0f ? s * 0.45f : 0f;
        }

        /// <summary>The hover/active line is a DELTA on whatever the theme resolved, never an
        /// absolute: the inherited width times this. (A user on a 0.6px global border gets a 0.75px
        /// hover; on a 3px global border, 3.75px.)</summary>
        private const float HoverWidthScale = 1.25f;

        /// <summary>The interaction accent (hover / drag), palette-driven so it stays legible
        /// against any override. Falls back to the resolved border colour before the palette binds.</summary>
        private static Color Accent
        {
            get { return HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : Border; }
        }

        /// <summary>Corner-radius scale per surface. Relative only — the radius itself is always
        /// <see cref="CornerRadius"/> (the Grid override, else the global HudConfig.CornerRadius).</summary>
        private static float RadiusScale(GridSurface s)
        {
            switch (s)
            {
                case GridSurface.Button: return 0.5f;
                case GridSurface.Region:
                case GridSurface.RegionNested: return 0.75f;
                case GridSurface.Tab:
                case GridSurface.TabGhost: return 0.5f;
                case GridSurface.Cell: return 0.5f;
                default: return 1f;   // Window
            }
        }

        /// <summary>How much of the box's SHORT side a corner may eat. A rect can round at most
        /// half its short side; a cell is small and square, so it keeps the softer 0.2 cap the
        /// cells were drawn with before the theme owned them (a half-round cell reads as a pill).</summary>
        private static float RadiusLimitFraction(GridSurface s)
        {
            return s == GridSurface.Cell ? 0.2f : 0.5f;
        }

        /// <summary>Border-width scale per CELL state. Relative only — see <see cref="CellState"/>.
        /// Ordering is deliberate: the active hand is the one persistent identity cue and so takes
        /// the heaviest line; hover/pending are transient and lean on COLOUR (the accent) rather
        /// than on weight.</summary>
        private static float CellWidthScale(CellState s)
        {
            switch (s)
            {
                case CellState.Selected: return 1.9f;   // the keyboard cursor: the heaviest line
                case CellState.ActiveHand: return 1.6f;
                case CellState.Pending: return 1.35f;
                case CellState.Hover: return 1.25f;
                default: return 1f;   // Idle
            }
        }

        /// <summary>The palette colour a cell state paints its line with. Inherited theme border
        /// when idle, so a re-skin carries; accents otherwise.</summary>
        private static Color CellBorder(CellState s)
        {
            switch (s)
            {
                case CellState.ActiveHand:
                    return HudPalette.ActiveHandAccent != null
                        ? HudPalette.ActiveHandAccent.Value : Accent;
                case CellState.Selected:
                case CellState.Pending:
                case CellState.Hover:
                    return Accent;
                default:
                    return Border;
            }
        }

        /// <summary>Which surfaces need <see cref="PanelGraphic.DenseFill"/>. A PanelGraphic's
        /// interior is a single triangle fan from one centre vertex, so ANY value that varies
        /// across the interior (the Sheen fill gradient, HudEdgeFade's alpha ramp) interpolates
        /// RADIALLY along the corner-to-centre triangles and paints a bowtie X — worst on a large,
        /// extreme-aspect, gradient-filled box (play-test: the Universal Inventory sized tall and
        /// narrow). Dense interior RINGS sample the ramp every ~48px so the ramp reproduces
        /// exactly. EVERY surface opts in: FlorpyDorp reported the X on ALL the inventory boxes "in
        /// differing amounts" in-game, which refutes the earlier assumption that small chrome's fan
        /// triangles are too short to show it — the error scales with size AND gradient strength, so
        /// a small box shows a small X, not none. This is cheap to make universal: PanelGraphic
        /// already forces the dense interior whenever Sheen is on
        /// (<c>_sheen &gt; 0.004f || _denseFill</c>), so on a sheened box this flag changes nothing,
        /// and it only adds rings where sheen is OFF and some other ramp (HudEdgeFade) would
        /// otherwise bowtie. The engine's own guard <c>minHalf &gt;= 8f &amp;&amp; columns &gt;= 3</c> already
        /// skips boxes too small to ring, so a cell (half ~23px) still qualifies while trivial chrome
        /// costs nothing. (The tab's <see cref="PolygonPanelGraphic"/> twin needs no equivalent: it
        /// EAR-CLIPS its interior rather than single-fanning it, so the bowtie mechanism does not
        /// exist there.)</summary>
        private static bool WantsDenseFill(GridSurface s)
        {
            return true;
        }

        /// <summary>Border-width scale per surface. Relative only — the width itself is always
        /// <see cref="BorderWidth"/> (the global HudConfig.BorderWidth).</summary>
        private static float WidthScale(GridSurface s)
        {
            switch (s)
            {
                case GridSurface.Button: return 0.8f;
                case GridSurface.Region: return 0.8f;
                case GridSurface.RegionNested: return 0.6f;   // 0.75x a top-level region
                case GridSurface.Tab:
                case GridSurface.TabGhost: return 0.8f;
                default: return 1f;   // Window
            }
        }

        /// <summary>
        /// THE one styling path for every box in UI/Grid — the window shells, their chrome buttons,
        /// region boxes and tabs. Resolves fill, border colour, border WIDTH, the corner shape
        /// (<see cref="PanelGraphic.SetShape(float,float,float,float,float)"/>), Sheen, Spec AND the
        /// full glass/frost stack <see cref="HudGlobalGlass.Apply"/> gives a HUD box — all from this
        /// theme (which follows the F9 palette + glass globals unless overridden). No caller may
        /// hardcode a width or a radius.
        ///
        /// <para><paramref name="hover"/> (hover / active / dragging) is applied as a DELTA on the
        /// inherited values — the accent border colour and <see cref="HoverWidthScale"/> times the
        /// inherited width — so a thin global border stays thin even when hovered.</para>
        ///
        /// <para>Per-frame safe and allocation-free: every <see cref="PanelGraphic"/> setter is
        /// dirty-guarded, <c>HudGlobalGlass.Apply</c>'s reads are null-guarded, and
        /// <c>HudFxMaterials.Assign</c> is idempotent. Owns no state, so nothing to reset on F6.</para>
        /// </summary>
        /// <param name="w">Laid-out width of the box, for the corner sweep.</param>
        /// <param name="h">Laid-out height of the box, for the corner sweep.</param>
        /// <param name="accent">Optional replacement for the hover accent (e.g. a tab dragged
        /// outside the strip). Ignored unless <paramref name="hover"/> is true.</param>
        public static void ApplyBox(PanelGraphic bg, float w, float h, GridSurface surface,
            bool hover, Color? accent = null)
        {
            if (bg == null) return;

            // --- colour + line, inherited then nudged -------------------------------------------
            float scale = WidthScale(surface);
            Color border = Border;
            if (hover)
            {
                scale *= HoverWidthScale;
                border = accent.HasValue ? accent.Value : Accent;
            }

            ApplyCore(bg, w, h, surface, scale, border);
        }

        /// <summary>The CELL entry point: same single styling path, with the four interaction
        /// states resolved as scales on the inherited line (<see cref="CellWidthScale"/>) and
        /// palette colours (<see cref="CellBorder"/>). Cells pass one square size; the corner is
        /// the inherited radius scaled and capped for a small box. No caller may hardcode a width
        /// or a radius.</summary>
        public static void ApplyBox(PanelGraphic bg, float w, float h, CellState state)
        {
            if (bg == null) return;
            ApplyCore(bg, w, h, GridSurface.Cell,
                WidthScale(GridSurface.Cell) * CellWidthScale(state), CellBorder(state));
        }

        private static void ApplyCore(PanelGraphic bg, float w, float h, GridSurface surface,
            float widthScale, Color border)
        {
            bool shell = surface == GridSurface.Window;

            float width = Mathf.Max(0f, BorderWidth) * widthScale;

            bg.color = Fill;
            bg.BorderColor = border;
            bg.BorderWidth = width;

            // --- shape: the inherited radius, scaled per surface and clamped to what this box can
            // actually draw (a half-height corner is the most a rect can round).
            float r = CornerRadius;
            if (float.IsNaN(r) || r < 0f) r = 0f;
            r *= RadiusScale(surface);
            float lim = Mathf.Min(Mathf.Abs(w), Mathf.Abs(h)) * RadiusLimitFraction(surface);
            if (r > lim) r = lim;
            bg.SetShape(w, h, r);

            // --- interior tessellation: kill the bowtie X on the big gradient surfaces ----------
            bg.DenseFill = WantsDenseFill(surface);

            // --- the HUD's own glass stack ------------------------------------------------------
            // The shell takes the glow halo and asks for the frosted backdrop (as an outer HUD box
            // does); interior surfaces follow sheen/edge-light/ripple but not the halo, so a window
            // full of regions doesn't bloom into a smear.
            // NOTE we deliberately do NOT write HudGlobalGlass.FrostDemand: it is a shared,
            // per-frame-owned flag (the Control Center assigns it every frame and clears it on
            // close), so a second writer would fight it. We consume the backdrop when it is live
            // and fall through to the edge-fx material when it is not.
            if (HudFxMaterials.Available)
            {
                // --- the analytic SDF panel path ------------------------------------------------
                // The WINDOW shells ride the same fragment-space SDF renderer the HUD boxes use,
                // whenever the HUD does (SdfPanels + a loaded ABI-2 bundle): the shell is the big,
                // gradient-filled, glow-carrying surface where the mesh fan's sparse columns paint
                // the corner/edge creases the play-tests keep finding. Interior surfaces (regions,
                // cells, sort/chrome buttons, tabs) deliberately STAY on the mesh path for now:
                // they live inside the scroll RectMask2D, and while the sdfglass shader implements
                // the _ClipRect path, it has never rendered under a mask in-game — do not gamble
                // the scroll view on it. Assign runs FIRST (HudElementView.ApplyFx's fail-soft
                // invariant: an SDF parameter mesh under a non-SDF material draws as a solid
                // grid), and Apply is told to leave the material slot alone on success so the two
                // never reassign the slot against each other per frame (a per-frame rebatch).
                //
                // ALL the shared-material paths (sdfglass shell, glass frost, edgefx shine) ride
                // clock uniforms fed only by HudSystem.UpdateFxUniforms, which stops when the
                // visor HUD stands down (VisorHudEnabled off) while the Grid
                // keeps ticking. Gate on FxClockLive so a dead clock degrades every Grid surface
                // to the static Tier-A mesh look instead of freezing a shine band / halo breath /
                // edge light mid-animation — the UiaControlCenter.StyleWindowPanel contract
                // (adversarial review 2026-07-17; Grid finding 2026-07-20). StyleHash folds
                // FxClockLive, so the hash-gated surfaces repaint on the flip too.
                bool fxLive = HudSystem.FxClockLive;
                // CUT corners (the visor's global corner-style knob, which the Grid inherits along
                // with the radius) are drawn natively by an ABI-3 sdfglass shader — the packed
                // superellipse exponent drops to 1, whose L1 zero contour IS the chamfer. Only an
                // OLDER bundle still rebuilds a strictly ROUNDED box, and there this gate stands:
                // without it the window shell would stay rounded while every cell inside it
                // chamfered. Same fail-soft shape as an unavailable bundle — ResetSdf below
                // already handles the mesh fallback.
                bool sdfAssigned = shell && fxLive
                    && (!bg.CornersAreCut || Core.HudShaderStore.SdfCutAvailable)
                    && HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                    && Core.HudShaderStore.SdfAvailable
                    && HudFxMaterials.Assign(bg, "sdfglass");

                HudGlobalGlass.Apply(bg, includeGlow: shell,
                    wantFrost: shell && fxLive && FrostParticipates, wantTierB: fxLive,
                    externalMaterial: sdfAssigned);

                // Apply resolves Sheen/Spec from the GLOBALS; re-assert this theme's resolved
                // values so a Grid override still wins. The Tier-A edge-light boost is recomputed
                // through the RESOLVED edge-energy tri-state (EdgeLightBoost) rather than carried
                // over as a delta from Apply: forcing 'Edge energy' Off must remove the rim
                // brightening too, or a 'Glass edge light' override of 0 keeps an un-removable
                // ~FxEdgeLight*0.45 floor (editor-parity finding, 2026-07-20). While Following
                // the tri-state collapses to the global toggle, so the boost is byte-identical
                // to Apply's. (The SDF vertex packer reads these same fields, so the Grid
                // overrides flow into the analytic path untouched.)
                bg.Sheen = Sheen;
                bg.Spec = Mathf.Clamp01(Spec + EdgeLightBoost());

                // The rest of the override block, re-asserted the same way over Apply's globals.
                // Only when NOT following: at the sentinels this recomputes exactly what Apply
                // wrote, and while following the state must stay byte-identical to a HUD box.
                if (!Following) ApplyMeshOverrides(bg, shell);

                if (sdfAssigned) ApplySdfShell(bg);
                else ResetSdf(bg);
            }
            else
            {
                bg.Sheen = Sheen;
                bg.Spec = Spec;
                ResetSdf(bg);
                HudFxMaterials.Unassign(bg);
            }
        }

        /// <summary>Neutral SDF reset — <c>HudElementView.ApplyFx</c>'s exact fallback arguments.
        /// A stale SDF mode under the glass/edgefx/default material would draw the SDF parameter
        /// grid as solid garbage, so every non-SDF ApplyCore pass re-asserts mesh mode. The setter
        /// is dirty-guarded, so this is free on a panel already on the mesh path (every masked
        /// surface, every frame).</summary>
        private static void ResetSdf(PanelGraphic bg)
        {
            bg.SetSdfStyle(false, 2f, false, 0f, 0f, 1f, 0f, 0f, 0f,
                false, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
        }

        /// <summary>The override half of the mesh-field stack: re-asserts every -1-sentinel /
        /// tri-state knob OVER what <see cref="HudGlobalGlass.Apply"/> just resolved from the
        /// globals — the Sheen/Spec re-assert pattern in <see cref="ApplyCore"/>, extended to the
        /// full block. Called only when NOT following; each knob at its sentinel recomputes
        /// exactly Apply's own value, so flipping Follow off changes nothing until a slider
        /// moves. Every setter is dirty-guarded, so this is per-frame free in steady state. The
        /// SDF vertex packer reads these same fields, so the overrides flow into the analytic
        /// shell path untouched — and the mesh fallback honours them identically.</summary>
        private static void ApplyMeshOverrides(PanelGraphic bg, bool shell)
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;

            // Edge softness: FeatherOverride's own semantics already ARE the sentinel
            // (-1 = the global EdgeFeather, which Apply just wrote), so it maps straight through.
            if (FeatherOv != null && FeatherOv.Value >= 0f)
                bg.FeatherOverride = Mathf.Clamp(FeatherOv.Value, 0f, 4f);

            // Amount overrides replace their global toggle+amount pair (0 = force off).
            // The Tier A master still wins, as it does everywhere.
            if (BorderFadeOv != null && BorderFadeOv.Value >= 0f)
                bg.BorderFade = tierA ? Mathf.Clamp01(BorderFadeOv.Value) : 0f;
            if (SoftEdgeOv != null && SoftEdgeOv.Value >= 0f)
                bg.SoftEdge = tierA ? Mathf.Clamp(SoftEdgeOv.Value, 0f, 48f) : 0f;

            // The glow halo rides only the window shells (Apply's includeGlow) — the tri-state
            // re-decides the shells, it cannot bloom the interior surfaces.
            if (shell)
            {
                bool glow = tierA && ModeOn(GlowMode, HudConfig.FxGlowOn);
                bg.Glow = glow
                    ? Mathf.Clamp(Flt(GlowOv, HudConfig.FxGlow, 0f), 0f, 2f) : 0f;
                bg.GlowInner = glow
                    ? Mathf.Clamp(Flt(GlowInnerOv, HudConfig.FxGlowInner, 0f), 0f, 2f) : 0f;
                bg.GlowWidth = Mathf.Max(0f, Flt(GlowWidthOv, HudConfig.FxGlowWidth, 14f));
                bg.GlowDiffuse = Mathf.Clamp01(Flt(GlowDiffuseOv, HudConfig.FxGlowDiffuse, 0.5f));
                bg.GlowExtraDiffuse = Mathf.Clamp01(
                    Flt(GlowExtraDiffuseOv, HudConfig.FxGlowExtraDiffuse, 0f));
            }

            bool ripple = tierA && ModeOn(RippleMode, HudConfig.FxEdgeLightOn);
            bg.EdgeRipple = ripple
                ? Mathf.Clamp(Flt(RippleOv, HudConfig.FxEdgeRipple, 0f), 0f, 2.5f) : 0f;
            bg.EdgeRippleFreq = Mathf.Clamp(
                Flt(RippleFreqOv, HudConfig.FxEdgeRippleFreq, 2f), 0.05f, 8f);
            if (RippleSmoothOv != null && RippleSmoothOv.Value >= 0f)
                bg.RippleSmooth = Mathf.Clamp01(RippleSmoothOv.Value);
        }

        /// <summary>The INTERIOR-surface glass stack for an <see cref="IGlassSurface"/> the
        /// PanelGraphic-typed <see cref="HudGlobalGlass.Apply"/> cannot take — the seated tab's
        /// <c>PolygonPanelGraphic</c>. One resolution path: sheen/spec (with the tri-state-gated
        /// <see cref="EdgeLightBoost"/>), feather, border fade / soft edge, and the ripple block
        /// all come through this theme's override getters, so every knob in the F9 Grid popup
        /// styles the tab exactly like its sibling surfaces (the tab used to mirror only the OLD
        /// ApplyCore and ignored the override half — editor-parity finding, 2026-07-20). No glow
        /// halo and no frost: interior surface, exactly as ApplyCore resolves an inner box. The
        /// Tier B edgefx material gates on <see cref="HudSystem.FxClockLive"/> (its clock
        /// uniforms stop with the visor HUD — the UiaControlCenter contract). Per-frame safe:
        /// every setter is dirty-guarded and Assign/Unassign are idempotent.</summary>
        public static void ApplyInteriorGlass(IGlassSurface g)
        {
            if (g == null) return;
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;

            g.Sheen = Sheen;
            g.Spec = Mathf.Clamp01(Spec + EdgeLightBoost());

            // Feather: -1 = the global EdgeFeather; the override maps straight through.
            g.FeatherOverride = !Following && FeatherOv != null && FeatherOv.Value >= 0f
                ? Mathf.Clamp(FeatherOv.Value, 0f, 4f) : -1f;

            g.BorderFade = tierA
                ? Mathf.Clamp01(GatedAmt(BorderFadeOv,
                    HudConfig.FxBorderFadeOn, HudConfig.FxBorderFade, 0f))
                : 0f;
            g.SoftEdge = tierA
                ? Mathf.Clamp(GatedAmt(SoftEdgeOv,
                    HudConfig.FxSoftEdgeOn, HudConfig.FxSoftEdge, 0f), 0f, 48f)
                : 0f;

            g.Glow = 0f;        // interior surface: the halo is reserved for the window shells
            g.GlowInner = 0f;

            bool ripple = tierA && ModeOn(RippleMode, HudConfig.FxEdgeLightOn);
            g.EdgeRipple = ripple
                ? Mathf.Clamp(Flt(RippleOv, HudConfig.FxEdgeRipple, 0f), 0f, 2.5f) : 0f;
            g.EdgeRippleFreq = Mathf.Clamp(
                Flt(RippleFreqOv, HudConfig.FxEdgeRippleFreq, 2f), 0.05f, 8f);
            g.RippleSmooth = !Following && RippleSmoothOv != null && RippleSmoothOv.Value >= 0f
                ? Mathf.Clamp01(RippleSmoothOv.Value)
                : 0f;           // the global path forces the un-smoothed ripple

            // Tier B shine/irid via the shared edgefx material — only while the FX clock runs.
            bool tierB = HudSystem.FxClockLive
                && HudConfig.FxTierB != null && HudConfig.FxTierB.Value
                && Core.HudShaderStore.TierBAvailable;
            float shine = tierB
                ? Mathf.Clamp(GatedAmt(ShineOv, HudConfig.FxShineOn, HudConfig.FxShine, 0f),
                    0f, 2f)
                : 0f;
            float irid = tierB
                ? Mathf.Clamp01(GatedAmt(IridOv, HudConfig.FxIridOn, HudConfig.FxIridescence, 0f))
                : 0f;

            if (HudFxMaterials.Available && (shine > 0.001f || irid > 0.001f))
            {
                g.FxStrength = 1f;
                if (!HudFxMaterials.Assign(g.AsGraphic, "edgefx"))
                    HudFxMaterials.Unassign(g.AsGraphic);
            }
            else
            {
                g.FxStrength = 0f;
                HudFxMaterials.Unassign(g.AsGraphic);
            }
        }

        /// <summary>The GLOBAL branch of the HUD's SDF-exclusive extras, applied to a Grid window
        /// shell: squircle exponent, halo quality, edge flow speed, frost/chroma, shine/irid and
        /// the organic-halo family — the same values a follow-global HUD box resolves in
        /// <c>HudElementView.ApplyFx</c>, with the same feature gates <see cref="HudGlobalGlass"/>
        /// uses for the mesh half of the stack. The Grid rides no HUD boot-dissolve clock and has
        /// no per-element edge fades, so those stay off. Frost is consumed only while the backdrop
        /// capture is LIVE (we never write <see cref="HudGlobalGlass.FrostDemand"/> — see the note
        /// above ApplyCore's Apply call); a dead capture samples garbage. Every read is
        /// null-guarded (config may be unbound around F6) and SetSdfStyle is dirty-guarded, so
        /// this is per-frame cheap.
        /// Every value below resolves through the Grid's own override getters
        /// (<see cref="Flt"/> / <see cref="ModeOn"/> / <see cref="GatedAmt"/>), all of which
        /// collapse to the raw global while Following or at their sentinels — so this stays the
        /// GLOBAL branch by default and honours the F9 Grid popup when overridden.</summary>
        private static void ApplySdfShell(PanelGraphic bg)
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            // Tier B rides the shared sdfglass clock uniforms. ApplyCore only routes here while
            // HudSystem.FxClockLive, but the gate is kept local too so shine/irid/chroma can
            // never pack against a stopped clock (the UiaControlCenter contract).
            bool tierB = HudSystem.FxClockLive
                && HudConfig.FxTierB != null && HudConfig.FxTierB.Value;

            // Squircle: the ELEMENT contract's inherit sentinel (HudElementView.SdfSquircleFor)
            // is "own < 2", not the Grid's usual -1 — an exponent below 2 is not a renderable
            // shape, so the whole [-1, 2) band follows the global instead of snapping to plain
            // round (same knob, same semantics as an element's squircle; parity finding
            // 2026-07-20).
            float squircleGlobal = HudConfig.SdfSquircle != null ? HudConfig.SdfSquircle.Value : 2f;
            float squircleOwn = !Following && SquircleOv != null ? SquircleOv.Value : -1f;
            float squircle = Mathf.Clamp(
                squircleOwn >= 2f ? squircleOwn : squircleGlobal, 2f, 8f);

            bool gaussian = ModeOn(GaussianMode, HudConfig.SdfGaussianHalo);
            float flow = Mathf.Clamp(Flt(FlowOv, HudConfig.FxEdgeFlowSpeed, 0.22f), 0f, 4f);

            bool frostOk = HudBackdrop.Active
                && HudConfig.FxTierC != null && HudConfig.FxTierC.Value
                && FrostParticipates;
            float frost = frostOk
                ? Mathf.Clamp01(Flt(FrostOv, HudConfig.FrostStrength, 1f))
                : 0f;
            float frostDepth = Mathf.Clamp01(Flt(FrostDepthOv, HudConfig.FrostDepth, 1f));
            float chroma = frost > 0.001f && tierB
                ? Mathf.Clamp01(GatedAmt(ChromaOv, HudConfig.FxChromaOn, HudConfig.FxChroma, 0f))
                : 0f;

            float shine = tierB
                ? Mathf.Clamp(GatedAmt(ShineOv, HudConfig.FxShineOn, HudConfig.FxShine, 0f), 0f, 2f)
                : 0f;
            float irid = tierB
                ? Mathf.Clamp01(GatedAmt(IridOv, HudConfig.FxIridOn, HudConfig.FxIridescence, 0f))
                : 0f;

            // The organic-halo family. The shell is the only Grid surface that takes the halo
            // (ApplyCore passes includeGlow: shell), so these mirror the GLOBAL branch of
            // HaloHazeFor / HaloBreathFor / HaloUnevenFor / HaloFlowAuraFor verbatim: haze needs
            // the glow itself; breath/uneven ride the halo OR the flowing aura; the aura needs
            // the edge light it crests on; the organic scale is neutral unless unevenness is live.
            bool glowOn = tierA && ModeOn(GlowMode, HudConfig.FxGlowOn);
            bool edgeOn = tierA && ModeOn(RippleMode, HudConfig.FxEdgeLightOn);
            float aura = edgeOn
                && HudConfig.FxGlowFlowAuraOn != null && HudConfig.FxGlowFlowAuraOn.Value
                ? Mathf.Clamp(HudConfig.FxGlowFlowAura != null
                    ? HudConfig.FxGlowFlowAura.Value : 0.6f, 0f, 2f)
                : 0f;
            bool auraOn = aura > 0.001f;
            float haze = glowOn
                ? Mathf.Clamp01(Flt(GlowHazeOv, HudConfig.FxGlowHaze, 0f))
                : 0f;
            float breath = (glowOn || auraOn)
                && HudConfig.FxGlowBreathOn != null && HudConfig.FxGlowBreathOn.Value
                ? Mathf.Clamp01(HudConfig.FxGlowBreath != null
                    ? HudConfig.FxGlowBreath.Value : 0.35f)
                : 0f;
            float uneven = (glowOn || auraOn)
                && HudConfig.FxGlowUnevenOn != null && HudConfig.FxGlowUnevenOn.Value
                ? Mathf.Clamp01(HudConfig.FxGlowUneven != null
                    ? HudConfig.FxGlowUneven.Value : 0.5f)
                : 0f;
            float organic = 1f;
            if (uneven > 0.001f)
            {
                float v = HudConfig.FxGlowOrganicScale != null
                    ? HudConfig.FxGlowOrganicScale.Value : 1f;
                organic = v < 0.2f ? 1f : Mathf.Clamp(v, 0.25f, 4f);
            }

            bg.SetSdfStyle(true, squircle, gaussian, flow,
                frost, frostDepth, chroma, shine, irid,
                false,      // dissolve: the Grid does not ride the HUD's boot-dissolve clock
                0f, 0f,     // edgeFadeX/Y: a HUD-element feature, not a Grid one
                haze, breath, uneven, aura, organic);
            // The SDF ABI carries independent final strengths; uv0.x's combined volume is unused
            // there and must not force legacy mesh semantics (mirrors HudElementView.ApplyFx).
            bg.FxStrength = 0f;
        }

        /// <summary>A change signal for the built window: hashes the ACTUAL resolved output (so
        /// it fires for both a follow-mode palette drag and an override edit). Cheap cached
        /// palette reads; TheGridPanel polls it and restyles only on a real change.</summary>
        public static int StyleHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Following ? 1 : 0);
                h = Comb(h, Fill);
                h = Comb(h, Border);
                h = Comb(h, Text);
                h = h * 31 + Mathf.RoundToInt(CornerRadius * 16f);
                // The Grid inherits the visor's corner SHAPE the same way it inherits the radius
                // (PanelGraphic resolves the -1 sentinel inside OnPopulateMesh). Fold it in or the
                // hash-gated surfaces keep a stale rounded mesh after an F9 rounded/cut flip.
                h = HI(h, HudConfig.HudCornerStyle);
                h = h * 31 + Mathf.RoundToInt(Sheen * 512f);
                h = h * 31 + Mathf.RoundToInt(Spec * 512f);
                h = h * 31 + Mathf.RoundToInt(BorderWidth * 64f);
                // The SDF-visible globals: the shells restyle every frame anyway, but the
                // hash-gated consumers (TheGridPanel's LayoutChrome shape pass, the region/sort
                // styling) must see an F9 SdfPanels/squircle edit as a theme change too.
                h = h * 31 + (HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value ? 2 : 1);
                h = h * 31 + Mathf.RoundToInt(
                    (HudConfig.SdfSquircle != null ? HudConfig.SdfSquircle.Value : 2f) * 64f);
                h = h * 31 + (HudConfig.SdfGaussianHalo != null
                    && HudConfig.SdfGaussianHalo.Value ? 2 : 1);
                // Mesh-path globals that are read INSIDE OnPopulateMesh (EdgeFeather, via the
                // -1 sentinel) or applied only by the hash-gated consumers (the region/sort
                // surfaces resolve the Tier-A toggle family through ApplyBox only when this
                // hash moves): a global Theme/Effects-tab drag must read as a theme change or
                // those surfaces keep the stale mesh while every HUD box rebuilds
                // (follow-mode tracking finding, 2026-07-20). TheGridPanel force-dirties the
                // glass meshes on a hash change, which is what actually rebuilds the feather
                // ramp — no per-graphic field carries it. FxClockLive folds in for the same
                // reason: the SDF/TierB routing flips with the clock (ApplyCore), and the
                // hash-gated surfaces must repaint once on a visor-HUD stand-down/return.
                h = HF(h, HudConfig.EdgeFeather);
                h = HB(h, HudConfig.FxTierA);
                h = HB(h, HudConfig.FxBorderFadeOn); h = HF(h, HudConfig.FxBorderFade);
                h = HB(h, HudConfig.FxSoftEdgeOn); h = HF(h, HudConfig.FxSoftEdge);
                h = HB(h, HudConfig.FxEdgeLightOn); h = HF(h, HudConfig.FxEdgeRipple);
                h = HF(h, HudConfig.FxEdgeRippleFreq);
                h = HB(h, HudConfig.FxGlowOn);
                h = h * 31 + (HudSystem.FxClockLive ? 2 : 1);
                // The override block, hashed RAW: a Grid popup edit is exactly a raw-value
                // change, every knob is inert while Following (the popup hides them then), and
                // the hash-gated consumers must repaint the moment a slider moves. Global
                // Effects-tab edits reach the per-frame surfaces (shells, cells) regardless.
                h = HF(h, FeatherOv); h = HF(h, SquircleOv); h = HI(h, GaussianMode);
                h = HF(h, BorderFadeOv); h = HF(h, SoftEdgeOv);
                h = HI(h, GlowMode); h = HF(h, GlowOv); h = HF(h, GlowInnerOv);
                h = HF(h, GlowHazeOv); h = HF(h, GlowWidthOv);
                h = HF(h, GlowDiffuseOv); h = HF(h, GlowExtraDiffuseOv);
                h = HI(h, RippleMode); h = HF(h, RippleOv); h = HF(h, RippleFreqOv);
                h = HF(h, RippleSmoothOv); h = HF(h, FlowOv);
                h = h * 31 + (FrostParticipates ? 2 : 1);
                h = HF(h, FrostOv); h = HF(h, FrostDepthOv);
                h = HF(h, ShineOv); h = HF(h, IridOv); h = HF(h, ChromaOv);
                // Per-tier: the resolved Fill/Border/Text above already carry the bare refs and the
                // alpha multiply, but the SLOT and the raw knobs must fold in too — the hash is
                // POLLED, so forgetting them would leave an open window on a stale skin across a
                // tier flip (a silent, hard-to-notice failure).
                h = h * 31 + (int)Slot;
                h = h * 31 + (PerTier != null && PerTier.Value ? 2 : 1);
                h = HF(h, BareOpacity); h = HI(h, BareFrostMode);
                h = HS(h, BareFillRef); h = HS(h, BareBorderRef); h = HS(h, BareTextRef);
                return h;
            }
        }

        private static int HF(int h, ConfigEntry<float> e)
        {
            unchecked { return h * 31 + Mathf.RoundToInt((e != null ? e.Value : -1f) * 64f); }
        }

        private static int HB(int h, ConfigEntry<bool> e)
        {
            unchecked { return h * 31 + (e != null && e.Value ? 2 : 1); }
        }

        private static int HI(int h, ConfigEntry<int> e)
        {
            unchecked { return h * 31 + (e != null ? e.Value : 0); }
        }

        /// <summary>Fold a ColorRef string in. Deliberately NOT string.GetHashCode: that is
        /// randomised per process on some .NET runtimes, and while this hash is only compared
        /// against its own previous value, an F6 hot-reload would then read as a theme change on
        /// every launch. A tiny FNV-1a keeps it deterministic and allocation-free.</summary>
        private static int HS(int h, ConfigEntry<string> e)
        {
            unchecked
            {
                string v = e != null ? e.Value : null;
                uint f = 2166136261u;
                if (!string.IsNullOrEmpty(v))
                    for (int i = 0; i < v.Length; i++) f = (f ^ v[i]) * 16777619u;
                return h * 31 + (int)f;
            }
        }

        private static int Comb(int h, Color c)
        {
            unchecked
            {
                int q = (int)(Mathf.Clamp01(c.r) * 255f)
                    | ((int)(Mathf.Clamp01(c.g) * 255f) << 8)
                    | ((int)(Mathf.Clamp01(c.b) * 255f) << 16)
                    | ((int)(Mathf.Clamp01(c.a) * 255f) << 24);
                return h * 31 + q;
            }
        }

        /// <summary>Serve the F9-style property list for the Grid's style popup (the same
        /// <see cref="HudProp"/> descriptors an element uses, so the drawer renders the identical
        /// palette dropdown + colour wheel a Box shows). Config-backed — no undo, BepInEx
        /// persists on write. Labels deliberately reuse the global Effects/Theme tab vocabulary
        /// so the Grid never presents differently-named knobs; shared material uniforms (light
        /// angle/colour/rim/falloff, halo motion, the clocks) are NAMED as shared in header
        /// rows rather than silently omitted, per the element popup's parity contract.
        ///
        /// <para>The drawer rebuilds its scratch every ImGui frame, but the descriptors are
        /// immutable delegate holders over live config — so the two list shapes (Following /
        /// overriding) are BUILT ONCE and re-served identity-stable, instead of re-allocating
        /// ~120 small objects per rendered frame while the popup is open (concurrency-hygiene
        /// finding, 2026-07-20). The caches are dropped in <see cref="Bind"/> (the closures
        /// capture that Bind's ConfigEntry references) and die with the assembly on F6.</para></summary>
        public static void DescribeProps(List<HudProp> into)
        {
            List<HudProp> src = Following
                ? (_propsFollow ?? (_propsFollow = BuildProps(true)))
                : (_propsOverride ?? (_propsOverride = BuildProps(false)));
            into.AddRange(src);
        }

        private static List<HudProp> _propsFollow;
        private static List<HudProp> _propsOverride;

        private static List<HudProp> BuildProps(bool following)
        {
            List<HudProp> into = new List<HudProp>(8);
            into.Add(HudProp.Header("Universal Inventory style"));
            into.Add(HudProp.Bool("Follow the HUD's global box theme",
                () => Following, v => { if (Follow != null) Follow.Value = v; }));

            // Sizes — ABSOLUTE px / scale / count values (NOT the -1 inherit sentinel the style knobs
            // use), so they read and write identically whether or not the window follows the global
            // theme. Added BEFORE the follow early-out so a follower can still resize cells and tabs.
            // Config-backed (UIAConfig section "9. The Grid"); TheGridPanel's size-hash poll relayouts
            // the open window (main + pinned) live, exactly as the F10 cell-size slider does.
            List<HudProp> sizes = new List<HudProp>(11);
            sizes.Add(Fs("Cell size (px)", UIAConfig.GridCellSize, 28f, 80f, 46f,
                "Edge length of one inventory cell. The item icon scales with it."));
            sizes.Add(Fs("Item icon scale", UIAConfig.GridIconScale, 0.4f, 1f, 0.70f,
                "Fraction of a cell the item thumbnail fills (0.70 = the default)."));
            sizes.Add(Fs("Bag tab height (px)", UIAConfig.GridTabHeight, 14f, 36f, 20f,
                "Height of a bag's manila tab."));
            sizes.Add(Fs("Bag tab text size", UIAConfig.GridTabTextSize, 8f, 20f, 11f,
                "Font size of the bag name on its tab (before the global font scale)."));
            sizes.Add(Fs("Bag tab icon size (px)", UIAConfig.GridTabIconSize, 8f, 28f, 14f,
                "The little container icon on a bag tab (not the item-thumbnail scale inside a cell)."));
            sizes.Add(Fs("Sort button scale", UIAConfig.GridSortButtonScale, 0.5f, 2f, 1f,
                "Size of the per-bag SORT button in the tab band (1.0 = default; keeps aspect)."));
            sizes.Add(Fs("Window button size (px)", UIAConfig.GridChromeButtonSize, 12f, 40f, 20f,
                "The close (X) and shrink/restore buttons on the main + pinned windows (not Sort)."));
            sizes.Add(Fs("Pinned title icon size (px)", UIAConfig.GridPinTitleIconSize, 8f, 32f, 16f,
                "The thumbnail icon on a pinned window's title bar (not the bag tab icon)."));
            sizes.Add(Fs("Pinned title text size", UIAConfig.GridPinTitleTextSize, 8f, 24f, 12f,
                "Font size of the container name on a pinned window's title bar."));
            sizes.Add(Is("Cells per row (max)", UIAConfig.GridCellCols, 1, 10, 5,
                "Item cells a bag shows per row at full width; a narrow window/column wraps " +
                "tighter but never wider."));
            sizes.Add(Is("Bag columns (max)", UIAConfig.GridMaxBagCols, 1, 5, 2,
                "Maximum side-by-side bag columns; fewer when the window is narrow, more as it is " +
                "dragged wider."));
            into.Add(HudProp.TabGroup("gridsizes",
                new List<HudProp> { HudProp.TabPage("Sizes", sizes) }));

            // Per-tier lives OUTSIDE the follow early-out on purpose: "unpowered inventory" is
            // meaningful whether or not the window follows the global box theme, and the rows read
            // PerTier live, so both cached list shapes can carry the identical block without a
            // third cache key.
            AddPerTierProps(into);

            if (following)
            {
                into.Add(HudProp.Header("Colours + glass + effects follow F9's global tabs."));
                into.Add(HudProp.Header("Uncheck above for the full override block."));
                return into;
            }

            into.Add(HudProp.Header("Overrides: -1 on a slider = follow the global value."));
            into.Add(HudProp.Header("Styles the whole Grid family (main + pinned windows)."));

            List<HudProp> colours = new List<HudProp>();
            colours.Add(HudProp.Header("Name a palette entry or type #RRGGBBAA"));
            colours.Add(HudProp.Color("Window fill",
                () => FillRef != null ? FillRef.Value : "",
                v => { if (FillRef != null) FillRef.Value = v ?? ""; },
                () => GlobalFill));
            colours.Add(HudProp.Color("Window border",
                () => BorderRef != null ? BorderRef.Value : "",
                v => { if (BorderRef != null) BorderRef.Value = v ?? ""; },
                () => GlobalBorder));
            colours.Add(HudProp.Color("Text",
                () => TextRef != null ? TextRef.Value : "",
                v => { if (TextRef != null) TextRef.Value = v ?? ""; },
                () => GlobalText));
            colours.Add(HudProp.Header(
                "Hover / active-hand accents follow F9 -> Palette."));

            List<HudProp> glass = new List<HudProp>();
            glass.Add(Fo("Corner radius", CornerRadiusOv, 28f,
                "Interior surfaces (regions, cells, chrome) scale this down automatically."));
            glass.Add(Fo("Line thickness (px)", BorderWidthOv, 6f,
                "The base width; every surface and hover state stays a scale on it."));
            glass.Add(Fo("Edge softness / AA (px)", FeatherOv, 4f, null));
            glass.Add(Fo("Glass sheen", SheenOv, 1f, null));
            glass.Add(Fo("Glass edge light", SpecOv, 1f,
                "Strength on the boxes. While edge energy is on (the tri-state under Glow & " +
                "energy), the global Edge energy Strength adds a shared rim boost on top — " +
                "forcing that tri-state Off removes it. Light angle, colour, rim and falloff " +
                "are shared globals (F9 -> Effects)."));
            glass.Add(Fo("Corner shape (2 round - 8 squircle)", SquircleOv, 8f,
                "Analytic SDF window shells only. Values below 2 (including -1) follow the " +
                "global corner shape."));
            glass.Add(TriState("Gaussian distance falloff", GaussianMode,
                "Halo falloff on the analytic SDF window shells (not a blur convolution, " +
                "F9 -> Theme). Inherit follows the global toggle."));
            glass.Add(Fo("Border fade (unlit sections dissolve)", BorderFadeOv, 1f,
                "0 forces it off even when the global toggle is on."));
            glass.Add(Fo("Soft edge (boxes melt together, px)", SoftEdgeOv, 48f,
                "0 forces it off even when the global toggle is on."));

            List<HudProp> glow = new List<HudProp>();
            glow.Add(TriState("Glow halo", GlowMode,
                "Window shells only. Inherit follows the global Glow halo toggle; the Tier A " +
                "master still gates."));
            glow.Add(Fo("  outward strength", GlowOv, 2f, null));
            glow.Add(Fo("  inward strength", GlowInnerOv, 2f, null));
            glow.Add(Fo("  extended atmospheric haze (SDF)", GlowHazeOv, 1f, null));
            glow.Add(Fo("  Halo / aura radius (px)", GlowWidthOv, 320f, null));
            glow.Add(Fo("  spread (tight rim -> diffuse)", GlowDiffuseOv, 1f, null));
            glow.Add(Fo("  extra diffuse (beyond max spread)", GlowExtraDiffuseOv, 1f, null));
            glow.Add(TriState("Edge energy (borders + lines)", RippleMode,
                "Inherit follows the global Edge energy toggle; the Tier A master still " +
                "gates. Off also removes the shared rim boost on 'Glass edge light'. Light " +
                "angle and colour stay global."));
            glow.Add(Fo("  irregular energy", RippleOv, 2.5f, null));
            glow.Add(Fo("  energy frequency", RippleFreqOv, 8f, null));
            glow.Add(Fo("  energy smoothness", RippleSmoothOv, 1f,
                "The global path forces the hard, un-smoothed ripple (0)."));
            glow.Add(Fo("  flow speed (0 = frozen)", FlowOv, 4f,
                "Per-window on the analytic SDF shells; mesh surfaces ride the shared clock."));
            glow.Add(HudProp.Header(
                "Halo motion (breath / uneven / aura) follows F9 -> Effects."));

            List<HudProp> frost = new List<HudProp>();
            frost.Add(HudProp.Bool("Frosted glass backdrop (window shell)",
                () => FrostOn == null || FrostOn.Value,
                v => { if (FrostOn != null) FrostOn.Value = v; }));
            frost.Add(Fo("  frost strength (SDF)", FrostOv, 1f,
                "The mesh fallback's frost strength is a shared global."));
            frost.Add(Fo("  blur depth (SDF)", FrostDepthOv, 1f, null));
            frost.Add(Fo("Shine sweep strength (SDF)", ShineOv, 2f,
                "0 forces it off; -1 follows the global toggle + strength."));
            frost.Add(Fo("Iridescent rim strength (SDF)", IridOv, 1f,
                "0 forces it off; -1 follows the global toggle + strength."));
            frost.Add(Fo("Chromatic fringe strength (SDF)", ChromaOv, 1f,
                "Needs live frost. 0 forces it off; -1 follows the global."));
            frost.Add(HudProp.Header(
                "Tier masters + clocks (F9 -> Effects) still gate these blocks."));

            List<HudProp> pages = new List<HudProp>();
            pages.Add(HudProp.TabPage("Colours", colours));
            pages.Add(HudProp.TabPage("Glass", glass));
            pages.Add(HudProp.TabPage("Glow & energy", glow));
            pages.Add(HudProp.TabPage("Frost & anim", frost));
            into.Add(HudProp.TabGroup("gridstyle", pages));
            return into;
        }

        /// <summary>The "Per tier (suited vs bare)" block. Appended to BOTH cached list shapes;
        /// every row reads its ConfigEntry live, so nothing needs a third cache key.</summary>
        private static void AddPerTierProps(List<HudProp> into)
        {
            List<HudProp> tier = new List<HudProp>(7);
            tier.Add(HudProp.Header("A separate skin while your suit has no power."));
            tier.Add(HudProp.Bool("Separate BARE style",
                () => PerTier != null && PerTier.Value,
                v => { if (PerTier != null) PerTier.Value = v; }));
            tier.Add(HudProp.Color("Bare window fill (empty = inherit)",
                () => BareFillRef != null ? BareFillRef.Value : "",
                v => { if (BareFillRef != null) BareFillRef.Value = v ?? ""; },
                () => Fill));
            tier.Add(HudProp.Color("Bare window border (empty = inherit)",
                () => BareBorderRef != null ? BareBorderRef.Value : "",
                v => { if (BareBorderRef != null) BareBorderRef.Value = v ?? ""; },
                () => Border));
            tier.Add(HudProp.Color("Bare text (empty = inherit)",
                () => BareTextRef != null ? BareTextRef.Value : "",
                v => { if (BareTextRef != null) BareTextRef.Value = v ?? ""; },
                () => Text));
            tier.Add(Fo("Bare opacity (-1 = inherit)", BareOpacity, 1f,
                "Multiplies the resolved alpha of the three colours above. The cheap way to say " +
                "'the same window, unpowered'."));
            tier.Add(TriState("Bare frosted backdrop", BareFrostMode,
                "Inherit follows the Grid's own frost setting; the Tier C master still gates."));
            into.Add(HudProp.TabGroup("gridpertier",
                new List<HudProp> { HudProp.TabPage("Per tier", tier) }));
        }

        // ---- theme travel (called by HudTheme.Snapshot / Apply) -----------------------------

        /// <summary>Capture every Grid theme setting into a profile's theme snapshot, keyed
        /// <c>&lt;prefix&gt;&lt;field name&gt;</c>. Reflects over THIS class's own public static
        /// ConfigEntry fields, so a knob added above travels with no second edit. Fail-soft per
        /// entry — a value that will not serialize is skipped, never thrown.</summary>
        public static void SnapshotInto(List<HudDocument.ThemeEntry> into, string prefix)
        {
            if (into == null) return;
            string p = prefix ?? "";
            var fields = typeof(GridTheme).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (!typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType)) continue;
                try
                {
                    var e = f.GetValue(null) as ConfigEntryBase;
                    if (e == null) continue;
                    into.Add(new HudDocument.ThemeEntry { K = p + f.Name, V = e.GetSerializedValue() });
                }
                catch { }
            }
        }

        /// <summary>Restore what <see cref="SnapshotInto"/> captured. A key ABSENT from the map
        /// leaves that setting alone — which is what makes the fold non-breaking for profiles
        /// saved before the Grid theme travelled.</summary>
        public static void ApplyFrom(Dictionary<string, string> map, string prefix)
        {
            if (map == null) return;
            string p = prefix ?? "";
            var fields = typeof(GridTheme).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (!typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType)) continue;
                string v;
                if (!map.TryGetValue(p + f.Name, out v) || v == null) continue;
                try
                {
                    var e = f.GetValue(null) as ConfigEntryBase;
                    if (e != null) e.SetSerializedValue(v);
                }
                catch { }
            }
        }

        /// <summary>A standard -1-sentinel override slider bound straight to its ConfigEntry
        /// (min is always -1 = follow the global; BepInEx persists on write).</summary>
        private static HudProp Fo(string label, ConfigEntry<float> e, float max, string help)
        {
            HudProp p = HudProp.F(label,
                () => e != null ? e.Value : -1f,
                v => { if (e != null) e.Value = v; }, -1f, max);
            p.Help = help;
            return p;
        }

        /// <summary>An ABSOLUTE float slider bound straight to its ConfigEntry — for the Sizes page,
        /// where the value is a real px/scale (no -1 = inherit sentinel). BepInEx clamps to the
        /// entry's own range on write and persists it; TheGridPanel's size-hash poll relayouts.</summary>
        private static HudProp Fs(string label, ConfigEntry<float> e, float min, float max,
            float fallback, string help)
        {
            HudProp p = HudProp.F(label,
                () => e != null ? e.Value : fallback,
                v => { if (e != null) e.Value = v; }, min, max);
            p.Help = help;
            return p;
        }

        /// <summary>An ABSOLUTE int slider bound straight to its ConfigEntry — the Sizes page's
        /// column-count knobs. Same contract as <see cref="Fs"/>.</summary>
        private static HudProp Is(string label, ConfigEntry<int> e, int min, int max,
            int fallback, string help)
        {
            HudProp p = HudProp.I(label,
                () => e != null ? e.Value : fallback,
                v => { if (e != null) e.Value = v; }, min, max);
            p.Help = help;
            return p;
        }

        /// <summary>An Inherit/On/Off tri-state combo bound to a 0/1/2 ConfigEntry.</summary>
        private static HudProp TriState(string label, ConfigEntry<int> e, string help)
        {
            HudProp p = HudProp.Enum(label,
                () => e != null ? Mathf.Clamp(e.Value, 0, 2) : 0,
                v => { if (e != null) e.Value = v; }, InheritOnOff);
            p.Help = help;
            return p;
        }
    }
}
