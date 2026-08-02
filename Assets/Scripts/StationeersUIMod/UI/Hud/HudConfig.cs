using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>How the visor HUD gets its curvature — all three approaches from the
    /// feasibility report are implemented and switchable live in the F9 editor.</summary>
    public enum HudCurvature
    {
        /// <summary>No curvature. The baseline.</summary>
        Flat,
        /// <summary>Option A — a BaseMeshEffect displaces every HUD vertex toward the
        /// screen's horizontal axis (barrel curve). Cheap, crisp text, per-element.</summary>
        VertexWarp,
        /// <summary>Option B — the whole HUD renders into a RenderTexture and is displayed
        /// on a dome-warped grid mesh. One true projection for everything, plus scanlines.</summary>
        DomeProjection,
        /// <summary>Option C — the HUD canvas lives in WORLD space, bent onto a cylinder
        /// in front of the camera (what the third-party curved-canvas assets do, written
        /// in-house — we ship no dependencies). True perspective curvature; experimental.
        /// KNOWN: swims when you move — it renders at the player's far-from-origin world
        /// position (float precision). Kept as-is; use D for the same look, stable.</summary>
        CurvedWorldCanvas,
        /// <summary>Option D — same cylinder curve as C, but rendered through a FIXED perspective
        /// camera at the origin into a RenderTexture and composited full-screen. Nothing moves with
        /// the player, so it is head-locked by construction and immune to the far-from-origin
        /// precision swim that afflicts C. The steady way to get C's look.</summary>
        CurvedRt,
    }

    /// <summary>
    /// Every value knob on the visor HUD. Bound from UIAConfig.Bind; all of it is
    /// live-editable in the F9 HUD editor (colours live in <see cref="HudPalette"/>).
    /// </summary>
    public static class HudConfig
    {
        public static ConfigEntry<bool> VisorHudEnabled;
        public static ConfigEntry<KeyCode> HudEditorKey;
        public static ConfigEntry<string> HudActiveProfile;
        public static ConfigEntry<bool> GridSnapEnabled;
        public static ConfigEntry<float> GridSnapSize;
        public static ConfigEntry<bool> ShowGrid;
        public static ConfigEntry<bool> DebugShowAll;
        public static ConfigEntry<bool> DebugShowAllBare;

        // Curvature
        public static ConfigEntry<HudCurvature> Curvature;
        public static ConfigEntry<float> CurveStrength;
        public static ConfigEntry<bool> CurveInvert;
        public static ConfigEntry<float> WorldCanvasDistance;
        public static ConfigEntry<bool> BareFlattens;

        // Screen-wide overlays. (The six per-panel Show* toggles — top bar, compass, equipment,
        // hands, vitals, hologram — went with the pre-document fixed panel set in 0.9.2.5:
        // element visibility now lives in the profile document, per element.)
        public static ConfigEntry<bool> ShowVignette;
        public static ConfigEntry<bool> ShowScanlines;   // global CRT/projector scan-line overlay over the HUD canvas
        public static ConfigEntry<bool> TintItemIcons;    // multiply a tint (HudItemIconTint) over every item icon

        // Layout / sizes. (The twelve fixed-panel size sliders — TopBar*/Compass*/Equip*/
        // HandBox*/Vitals* — were deleted with the legacy panels; the document HUD sizes
        // every element itself.)
        public static ConfigEntry<float> HudScale;
        public static ConfigEntry<bool> HudScaleWithRes;   // scale the HUD with the screen resolution
        public static ConfigEntry<float> HudRefWidth;      // the resolution the layout was authored at
        public static ConfigEntry<float> HudRefHeight;
        public static ConfigEntry<float> HudScaleMatch;    // 0 = follow width, 1 = follow height, 0.5 = blend

        // Panel styling
        public static ConfigEntry<float> CornerRadius;
        public static ConfigEntry<int> HudCornerStyle;      // 0 = rounded arcs, 1 = cut (45° chamfer)
        public static ConfigEntry<float> BorderWidth;
        public static ConfigEntry<float> EdgeFeather;
        public static ConfigEntry<float> GlassSheen;
        public static ConfigEntry<float> GlassEdge;
        public static ConfigEntry<bool> SdfPanels;          // analytic fragment-space panel renderer
        public static ConfigEntry<float> SdfSquircle;       // 2=circular corners, >2=squircle
        public static ConfigEntry<bool> SdfGaussianHalo;    // higher-quality analytic halo

        // Text
        public static ConfigEntry<string> FontName;
        public static ConfigEntry<float> FontScale;
        public static ConfigEntry<float> LabelFontSize;
        // ValueFontSize / CompassFontSize / BareWordFontSize / VitalsRowFontSize were read only
        // by the legacy fixed panels and went with them (0.9.2.5); document elements carry their
        // own FontScale. LabelFontSize survives — HandBoxesWidget still reads it.

        // Diegetic behavior
        public static ConfigEntry<bool> DiegeticTiers;
        /// <summary>Shared tooltip content rather than a per-tier visual style. It still travels
        /// with the profile theme, as every non-performance HUD setting must.</summary>
        public static ConfigEntry<bool> DetailedVitalsTooltips;
        public static ConfigEntry<bool> FlickerAnimations;
        public static ConfigEntry<bool> LowPowerDropouts;
        public static ConfigEntry<float> LowPowerThreshold;

        // Power-transition glitch (transform-jitter envelope; see HudGlitch). Unified onto the
        // fxGlitch tri-state family (0.9.2.5, audit 04 #7): the master on/off and the severity
        // are now HudConfig.FxGlitchOn / FxGlitchAmt (FX section below) — the same globals the
        // per-element tri-state resolver uses — instead of a second, easy-to-forget switch that
        // left the F9 "Glitch tear" row inert by default. GlitchEnabled/GlitchIntensity are gone;
        // duration has no tri-state analogue and moved to FxGlitchDuration. These two per-event
        // gates survive because a user may want the tear on only ONE side of the transition
        // (e.g. suit death but not boot) — the tri-state has no way to express that.
        public static ConfigEntry<bool> GlitchOnPowerDown;
        public static ConfigEntry<bool> GlitchOnPowerUp;

        // ---- 0.9.0 effect tiers (master switches + effect globals; per-element overrides
        // live in each element's Params bag with the "-1/absent = follow global" contract) ----
        public static ConfigEntry<bool> FxTierA;            // mesh effects master
        public static ConfigEntry<bool> FxTierB;            // bundle-shader effects master
        public static ConfigEntry<bool> FxTierC;            // backdrop (frost) master — EXPERIMENTAL
        // Per-effect checkboxes (play-test: "add checkboxes to each individual feature —
        // right now they are all always on when I check tier a/b/c").
        public static ConfigEntry<bool> FxHairlinesOn;
        public static ConfigEntry<bool> FxEdgeLightOn;
        public static ConfigEntry<bool> FxPulseOn;
        public static ConfigEntry<bool> FxShineOn;
        public static ConfigEntry<bool> FxIridOn;
        public static ConfigEntry<bool> FxChromaOn;
        public static ConfigEntry<float> FxHairlineMin;     // 0 = true hairlines allowed
        public static ConfigEntry<float> FxEdgeLight;       // directional edge-light strength (lines + borders)
        public static ConfigEntry<string> FxEdgeLightColor; // #RRGGBB the border/line whitens TOWARD (default white)
        public static ConfigEntry<float> FxEdgeLightAngle;  // key-light direction, deg (0=right, 90=top, 180=left)
        public static ConfigEntry<float> FxEdgeLightRim;    // opposing-rim catch on the far side (0..2, def 0.5)
        public static ConfigEntry<float> FxEdgeLightSharp;  // falloff exponent — high=tight catch, low=broad wash
        public static ConfigEntry<float> FxEdgeFadeCurve;   // end-fade ramp shape (<1 hard edge, >1 long tail)
        public static ConfigEntry<float> FxEdgeFadeBorder;  // how much the border joins the end fade (1 = with the box)
        public static ConfigEntry<float> FxEdgeRipple;      // irregular light/dark shimmer along edges (0 = smooth)
        public static ConfigEntry<float> FxEdgeRippleFreq;  // shimmer frequency, cycles per ~100px
        public static ConfigEntry<float> FxEdgeFlowSpeed;   // shader-time edge-energy animation speed
        public static ConfigEntry<bool> FxRippleDesync;     // per-element ripple-frequency jitter (break lockstep)
        public static ConfigEntry<float> FxRippleDesyncAmount; // how far each element's frequency may wander
        public static ConfigEntry<float> FxPulseSpeed;      // breathing pulse rate, Hz
        public static ConfigEntry<float> FxPulseDepth;      // 0..1 how deep the dim half of the pulse goes
        public static ConfigEntry<float> FxShine;           // shine sweep global strength (Tier B)
        public static ConfigEntry<float> FxShinePeriod;     // seconds between sweeps
        public static ConfigEntry<float> FxIridescence;     // iridescent edge global strength (Tier B)
        public static ConfigEntry<float> FxChroma;          // chromatic-aberration strength (Tier B)
        public static ConfigEntry<bool> FxDissolveBoot;     // dissolve reveal on boot/power transitions

        // --- Power-down / transition animation (global masters) ---
        // These exist because "follow the globals" previously meant "forced fully ON": EffectAmt
        // returned a hard 1f for any global-styled element, so a per-element switch was ignored and
        // there was no global switch either. Every transition now has a real master + strength.
        public static ConfigEntry<bool> FxPowerDownMirrorsBoot;
        public static ConfigEntry<bool> FxDissolveOnPowerDown;
        public static ConfigEntry<bool> FxCollapseOn;
        public static ConfigEntry<float> FxCollapseAmt;
        public static ConfigEntry<bool> FxGlitchOn;
        public static ConfigEntry<float> FxGlitchAmt;
        public static ConfigEntry<float> FxGlitchDuration; // seconds the tear envelope runs (no tri-state analogue)
        public static ConfigEntry<bool> FxWarpOn;
        public static ConfigEntry<float> FxWarpAmt;
        // The rest of the transition registry (see HudTransitionFx): every effect owns a MASTER
        // plus a default STRENGTH, and every element resolves against them through one tri-state
        // (Inherit / On / Off). Masters that already existed are reused rather than duplicated —
        // Flicker's master is FlickerAnimations, Dissolve's is FxDissolveBoot, Pulse's is FxPulseOn.
        public static ConfigEntry<bool> FxTvOffOn;          // NEW: the classic CRT horizontal collapse
        public static ConfigEntry<float> FxTvOffAmt;
        public static ConfigEntry<float> FxFlickerAmt;      // strength for the existing FlickerAnimations
        public static ConfigEntry<float> FxDissolveAmt;     // strength for the existing FxDissolveBoot
        public static ConfigEntry<float> FxPulseAmt;        // default 0 — pulse stays opt-in per element
        // The concept-art trio (global defaults; per-element params override with -1 = these):
        public static ConfigEntry<bool> FxBorderFadeOn;
        public static ConfigEntry<bool> FxSoftEdgeOn;
        public static ConfigEntry<bool> FxGlowOn;
        public static ConfigEntry<float> FxBorderFade;      // 0 = solid outline, 1 = unlit border sections dissolve
        public static ConfigEntry<float> FxSoftEdge;        // px of soft outer fill fade ("boxes blur into each other")
        public static ConfigEntry<float> FxGlow;            // halo intensity OUTSIDE frames
        public static ConfigEntry<float> FxGlowInner;       // glow intensity INTO the glass
        public static ConfigEntry<float> FxGlowWidth;       // halo width, px (shared by both sides)
        public static ConfigEntry<float> FxGlowDiffuse;     // halo shape: 0 tight rim, 1 wide soft haze
        public static ConfigEntry<float> FxGlowExtraDiffuse; // softness beyond GlowDiffuse's ceiling (both glow bands)
        public static ConfigEntry<float> FxGlowHaze;        // faint long-tail atmosphere beyond the core falloff
        public static ConfigEntry<bool> FxGlowBreathOn;     // halo/aura-only breathing (does not tint the whole element)
        public static ConfigEntry<float> FxGlowBreath;      // 0..1 halo/aura-only breath depth
        public static ConfigEntry<float> FxGlowBreathSpeed; // shared unscaled halo breath rate, Hz
        public static ConfigEntry<bool> FxGlowUnevenOn;     // stable local-space breakup of halo reach/energy
        public static ConfigEntry<float> FxGlowUneven;      // 0..1 unevenness amount
        public static ConfigEntry<float> FxGlowOrganicScale; // unevenness noise footprint, 0.25x..4x (1 = classic)
        public static ConfigEntry<bool> FxGlowFlowAuraOn;   // moving edge-energy crests emit their own aura
        public static ConfigEntry<float> FxGlowFlowAura;    // 0..2 flowing aura strength
        // Status alert pulse: the game's own suit warnings tint + breathe the halo / edge ripple.
        public static ConfigEntry<bool> FxAlertPulseOn;
        public static ConfigEntry<float> FxAlertBreathSeconds; // duration of ONE breath, seconds
        public static ConfigEntry<int> FxAlertCautionBreaths;  // how many flashes a caution runs
        public static ConfigEntry<float> FxAlertPulseStrength; // 0..1 colour + brightness swing
        // Per-level brightness gain on the alert hue. The COLOURS themselves are palette entries
        // (HudAlertCaution / HudAlertCritical) so they get the normal picker + undo treatment.
        public static ConfigEntry<float> FxAlertCautionBright;
        public static ConfigEntry<float> FxAlertCriticalBright;
        public static ConfigEntry<float> FrostStrength;     // 0..1 global frost gate (scales every element's frost)
        public static ConfigEntry<float> FrostDepth;        // 0..1 blur-pyramid depth (per-element overridable)
        public static ConfigEntry<float> FrostDownsample;   // blur RT divisor (2/4/8)
        public static ConfigEntry<int> FrostUpdateEveryN;   // re-blur throttle, frames
        public static ConfigEntry<float> FrostDarken;       // 0..1 backdrop darkening
        public static ConfigEntry<string> FrostTint;        // #RRGGBB gray-leaning tint (FlorpyDorp: grayer than concept)

        // Stage 2 HUD bloom — bright HUD pixels light their neighbours. Routes the HUD through a
        // render texture (forces the flat/vertex-warp path onto the RT rig while on).
        public static ConfigEntry<bool> FxBloomOn;
        public static ConfigEntry<float> FxBloomStrength;   // additive composite strength, 0..3
        public static ConfigEntry<float> FxBloomThreshold;  // bright cutoff on max(r,g,b), 0..1.5
        public static ConfigEntry<float> FxBloomKnee;       // soft-knee width as a fraction of threshold, 0..1
        public static ConfigEntry<int> FxBloomBlurSteps;    // dual-Kawase pyramid depth, 1..5
        public static ConfigEntry<float> FxBloomSpread;     // Kawase sample offset — CONTINUOUS width between steps
        public static ConfigEntry<bool> FxBloomFineDetail;  // half-res bright pass: thin borders/lines survive into bloom
        public static ConfigEntry<float> FxBloomSaturation; // 0 white-hot .. 1 source hues .. 2 oversaturated
        public static ConfigEntry<string> FxBloomTint;      // #RRGGBB multiply on the glow
        // Dynamic bloom (0.9.0 round 2) — every one optional, default off/neutral:
        public static ConfigEntry<float> FxBloomAnamorph;   // -1 vertical .. 0 round .. +1 horizontal streak
        public static ConfigEntry<int> FxBloomRes;          // -1 legacy(FineDetail) / 0 full / 1 half / 2 quarter
        public static ConfigEntry<bool> FxBloomPulseOn;     // slow breathing on the glow strength
        public static ConfigEntry<float> FxBloomPulseSpeed; // Hz
        public static ConfigEntry<float> FxBloomPulseDepth; // 0..1 fraction of strength breathed away
        public static ConfigEntry<bool> FxBloomReactOn;     // state-reactive master (power / alarm / boot)
        public static ConfigEntry<float> FxBloomReactPower; // 0..1 how much low suit power dims the glow
        public static ConfigEntry<float> FxBloomReactAlarm; // 0..1 red alarm pulse on critical states
        public static ConfigEntry<float> FxBloomReactBoot;  // 0..1 boot-sequence flare
        // Second bloom band ("border/highlight bloom"): an independent glow layer only the
        // BRIGHTEST pixels reach — style borders above its threshold (edge light/spec) and the
        // frame lines carry their own glow, separate from the general bloom.
        public static ConfigEntry<bool> FxBloom2On;
        public static ConfigEntry<float> FxBloom2Threshold; // higher than band 1 — highlights only
        public static ConfigEntry<float> FxBloom2Strength;
        public static ConfigEntry<int> FxBloom2Steps;       // its own pyramid depth (reach)
        public static ConfigEntry<float> FxBloom2Spread;    // its own continuous width
        public static ConfigEntry<string> FxBloom2Tint;     // #RRGGBB on the highlight glow only
        // Saturation selectivity (needs the 2026-07-15+ shader bundle; older bundles ignore it):
        // + = only SATURATED pixels bloom (coloured borders, not white text), - = only unsaturated.
        public static ConfigEntry<float> FxBloomSatBias;    // band 1
        public static ConfigEntry<float> FxBloom2SatBias;   // band 2 — THE borders-not-text knob

        /// <summary>The scale EVERY HUD layout computation must use: the user's <see cref="HudScale"/>
        /// multiplied by the RESOLUTION factor, so a layout authored at the reference resolution keeps
        /// the same relative position and size on any screen.
        ///
        /// Element ANCHORS already track the screen (they're derived from Screen.width/height), but the
        /// per-element offsets and sizes are reference pixels — without this factor they stay a fixed
        /// pixel count, so on a bigger screen the HUD shrinks and creeps toward the corners. Multiplying
        /// the same scale the offsets/sizes already ride keeps the whole layout proportional; the
        /// screen-percent (WPct/HPct) fields were always proportional and are unaffected.
        ///
        /// The width/height blend is the same log-interpolation Unity's CanvasScaler.ScaleWithScreenSize
        /// uses, so the behaviour matches what a UGUI layout would do. Every caller MUST go through this
        /// (render, relayout, edit-targets, editor hit-test and gizmos) or the editor desyncs from the
        /// rendered HUD. It also feeds LayoutHash, so changing resolution re-lays-out automatically.</summary>
        public static float EffectiveHudScale()
        {
            float s = HudScale != null ? HudScale.Value : 1f;
            if (HudScaleWithRes == null || !HudScaleWithRes.Value) return s;
            int sw = Screen.width, sh = Screen.height;
            if (sw <= 0 || sh <= 0) return s;
            float rw, rh;
            ReferenceResolution(out rw, out rh);
            float m = HudScaleMatch != null ? Mathf.Clamp01(HudScaleMatch.Value) : 0.5f;
            return s * Mathf.Pow(sw / rw, 1f - m) * Mathf.Pow(sh / rh, m);
        }

        /// <summary>The resolution the ACTIVE layout was designed at. The profile's own RefW/RefH win
        /// (they ship inside the shareable XML, so a layout built on a 1440p screen scales correctly
        /// on someone else's 1080p one); a profile that doesn't declare them falls back to this
        /// player's global reference, then to 1920x1080.</summary>
        public static void ReferenceResolution(out float w, out float h)
        {
            w = 0f; h = 0f;
            try
            {
                var doc = Features.HudProfileStore.Active;
                if (doc != null) { w = doc.RefW; h = doc.RefH; }
            }
            catch { }
            if (w < 320f || h < 240f)
            {
                w = HudRefWidth != null ? HudRefWidth.Value : 1920f;
                h = HudRefHeight != null ? HudRefHeight.Value : 1080f;
            }
            w = Mathf.Max(320f, w);
            h = Mathf.Max(240f, h);
        }

        /// <summary>Multiply the global item-icon tint (the HudItemIconTint palette colour) over an
        /// icon colour when "TintItemIcons" is on, preserving the icon's own alpha (so an empty-slot
        /// fade stays invisible). Off, or before Bind, returns the colour unchanged. Called at every
        /// item-icon draw site (hand boxes, the 1-6 equipment column, the inventory/bag grid cells)
        /// so ONE toggle recolours them all — e.g. a green wash for the projected-lens theme.</summary>
        public static Color TintIcon(Color c)
        {
            if (TintItemIcons == null || !TintItemIcons.Value || HudPalette.ItemIconTint == null) return c;
            Color t = HudPalette.ItemIconTint.Value;
            return new Color(c.r * t.r, c.g * t.g, c.b * t.b, c.a);
        }

        public static void Bind(ConfigFile cfg)
        {
            const string S = "10. Visor HUD";

            VisorHudEnabled = cfg.Bind(S, "VisorHudEnabled", true,
                "The UGUI visor HUD, rendered from the active layout profile (a document of " +
                "movable, restylable elements — the HUD Designer). Off = no HUD at all and " +
                "vanilla's own panels come back.");
            HudActiveProfile = cfg.Bind(S, "HudActiveProfile", "Stationeers Blue",
                "Which HUD layout profile to render (a .xml in config/StationeersUIMod/" +
                "HudProfiles). 'Stationeers Blue' is the shipped default; shipped profiles " +
                "(Stationeers Blue + Pure HUD) import from the mod folder on first run.");
            GridSnapEnabled = cfg.Bind(S, "GridSnapEnabled", true,
                "HUD editor: snap dragged/resized elements to the grid. Toggleable live in " +
                "the F9 window; hold Alt while dragging for temporary freeform.");
            GridSnapSize = cfg.Bind(S, "GridSnapSize", 8f,
                new ConfigDescription("HUD editor: grid cell size in reference pixels.",
                    new AcceptableValueRange<float>(2f, 64f)));
            ShowGrid = cfg.Bind(S, "ShowGrid", false,
                "HUD editor: draw the snap grid as a faint overlay while the F9 designer is open.");
            DebugShowAll = cfg.Bind(S, "DebugShowAll", false,
                "F9 DEBUG: force every HUD element to show its content at once (all vitals " +
                "rows, all moodlets, all instruments) with dummy data, to arrange the layout " +
                "for the worst case. Display-only; turn off when done.");
            DebugShowAllBare = cfg.Bind(S, "DebugShowAllBare", false,
                "F9 DEBUG: same as DebugShowAll but forces the POWER-OFF (bare) tier, to lay " +
                "out the suit-off HUD full.");
            HudEditorKey = cfg.Bind(S, "HudEditorKey", KeyCode.F9,
                "Key that opens the HUD editor: click any HUD element to edit its colours, " +
                "fonts and sizes in place. NOTE: vanilla binds F9 to CREATIVE spawn-item; " +
                "while the keys collide the vanilla spawn is auto-suppressed (F9 = editor). " +
                "With the creative spawn menu OPEN, F9 spawns as vanilla intends and the " +
                "editor stands down; rebind this key to get both at once.");

            Curvature = cfg.Bind(S, "Curvature", HudCurvature.VertexWarp,
                "How the HUD curves like a visor. Flat: none. VertexWarp (A): per-element " +
                "mesh bend — cheap and crisp. DomeProjection (B): the HUD renders to a " +
                "texture shown on a dome grid — one true projection, scanline option. " +
                "CurvedWorldCanvas (C): the canvas physically curves in world space in " +
                "front of the camera — true perspective, experimental.");
            CurveStrength = cfg.Bind(S, "CurveStrength", 0.25f,
                new ConfigDescription("How much the visor bows. 0 = flat; the curve is a gentle " +
                    "bow now (the old values funnelled the top bar).", new AcceptableValueRange<float>(0f, 1f)));
            CurveInvert = cfg.Bind(S, "CurveInvert", false,
                "Flip the bend direction. Default (off): edges flare AWAY from the screen " +
                "centre (the play-tested visor look). On: the original pinch-inward bend.");
            WorldCanvasDistance = cfg.Bind(S, "WorldCanvasDistance", 0.6f,
                new ConfigDescription("CurvedWorldCanvas mode: how far in front of the camera " +
                    "the visor floats (metres). 0.6 fills the view; higher recedes it.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            BareFlattens = cfg.Bind(S, "BareFlattens", true,
                "When the suit is off or powered down (BARE), drop all visor curvature so the " +
                "remaining HUD reads flat. The top bar hides itself by tier regardless.");

            ShowVignette = cfg.Bind(S, "ShowVignette", true, "Visor-edge darkening.");
            ShowScanlines = cfg.Bind(S, "ShowScanlines", false,
                "Projector scan-lines: horizontal CRT/HUD-lens lines across the whole visor HUD " +
                "canvas (colour = the HudScanline palette entry; alpha 0 = invisible). Covers the " +
                "visor HUD; the radial menus and inventory grid are separate canvases.");
            TintItemIcons = cfg.Bind(S, "TintItemIcons", false,
                "Tint every ITEM icon (hands, the 1-6 equipment slots, and the inventory/bag " +
                "grids) by the HudItemIconTint palette colour — e.g. a green wash for a projected-" +
                "lens theme. Off = native item art. The tint multiplies RGB and keeps each icon's " +
                "own alpha.");

            HudScale = cfg.Bind(S, "VisorHudScale", 1.0f,
                new ConfigDescription("Overall visor HUD scale.", new AcceptableValueRange<float>(0.6f, 1.6f)));
            HudScaleWithRes = cfg.Bind(S, "ScaleWithResolution", true,
                "Scale the whole HUD with the screen resolution, so a layout keeps the SAME relative " +
                "position and size on any monitor (the CanvasScaler idea, done in our own layout math). " +
                "Off = element offsets/sizes stay fixed pixels, so the HUD shrinks toward the corners on " +
                "a bigger screen. Anchors track the screen either way.");
            HudRefWidth = cfg.Bind(S, "ScaleReferenceWidth", 1920f,
                new ConfigDescription("The screen WIDTH your layout was designed at. At this resolution the " +
                    "HUD renders 1:1; other resolutions scale from it. Use F9 > Global style > 'Use current " +
                    "resolution' if you built your layout on a different screen.",
                    new AcceptableValueRange<float>(640f, 7680f)));
            HudRefHeight = cfg.Bind(S, "ScaleReferenceHeight", 1080f,
                new ConfigDescription("The screen HEIGHT your layout was designed at. See ScaleReferenceWidth.",
                    new AcceptableValueRange<float>(480f, 4320f)));
            HudScaleMatch = cfg.Bind(S, "ScaleMatchWidthOrHeight", 0.5f,
                new ConfigDescription("How the resolution scale blends: 0 = follow WIDTH only, 1 = follow " +
                    "HEIGHT only, 0.5 = blend both (same curve as Unity's CanvasScaler). Height-match keeps " +
                    "text size steady on ultrawide; width-match keeps full-width bars proportional.",
                    new AcceptableValueRange<float>(0f, 1f)));

            CornerRadius = cfg.Bind(S, "CornerRadius", 10f,
                new ConfigDescription("Panel corner rounding (px).", new AcceptableValueRange<float>(0f, 28f)));
            HudCornerStyle = cfg.Bind(S, "CornerStyle", 0,
                new ConfigDescription("Panel corner SHAPE: 0 = rounded (an arc), 1 = cut (a flat 45-degree " +
                    "chamfer straight across the corner). CornerRadius still sets the SIZE of either, and " +
                    "0 radius is a square corner in both styles. Elements can override this in their own " +
                    "popup. Both styles draw on the analytic sharp-panel shader (its superellipse " +
                    "exponent drops to 1, whose L1 zero contour IS the chamfer), so a cut panel keeps " +
                    "the full effect set; only an out-of-date effects bundle falls back to the mesh renderer.",
                    new AcceptableValueRange<int>(0, 1)));
            BorderWidth = cfg.Bind(S, "PanelBorderWidth", 1.4f,
                new ConfigDescription("Panel outline thickness (px) — the thin cyan lines.",
                    new AcceptableValueRange<float>(0f, 6f)));
            EdgeFeather = cfg.Bind(S, "EdgeFeather", 1.25f,
                new ConfigDescription("Anti-aliasing ramp width (px) on every HUD edge.",
                    new AcceptableValueRange<float>(0f, 4f)));
            GlassSheen = cfg.Bind(S, "GlassSheen", 0f,
                new ConfigDescription("Default glass sheen (the soft top-down light gradient) on " +
                    "every panel. Elements whose own 'Glass sheen' is -1 inherit this.",
                    new AcceptableValueRange<float>(0f, 1f)));
            GlassEdge = cfg.Bind(S, "GlassEdge", 0f,
                new ConfigDescription("Default glass edge light (the bright rim highlight) on every " +
                    "panel. Elements whose own 'Glass edge light' is -1 inherit this.",
                    new AcceptableValueRange<float>(0f, 1f)));
            SdfPanels = cfg.Bind(S, "SdfPanels", true,
                "Render rectangular/trapezoid panels analytically in the UIA/SdfGlass shader. " +
                "Requires uia_effects.bundle and fails soft to the existing PanelGraphic mesh.");
            SdfSquircle = cfg.Bind(S, "SdfSquircleExponent", 2f,
                new ConfigDescription("Global SDF corner exponent: 2 = circular rounded corners; " +
                    "higher values produce a squircle/superellipse shoulder. Ignored while CornerStyle " +
                    "is Cut — the chamfer is exponent 1, a different corner geometry — and restored " +
                    "untouched when Cut is switched off.",
                    new AcceptableValueRange<float>(2f, 8f)));
            SdfGaussianHalo = cfg.Bind(S, "SdfGaussianHalo", false,
                "Use the shader's Gaussian distance falloff instead of the cheaper smooth distance ramp. " +
                "This is a quality/taste option, not an exact convolution for asymmetric trapezoids; " +
                "elements can override it when they use their own appearance.");

            FontName = cfg.Bind(S, "HudFontName", "",
                "TMP font for all HUD text (substring match; empty = the game's default " +
                "face). The F9 editor lists every loaded font.");
            FontScale = cfg.Bind(S, "HudFontScale", 1.0f,
                new ConfigDescription("Multiplies every HUD font size.", new AcceptableValueRange<float>(0.6f, 1.8f)));
            LabelFontSize = cfg.Bind(S, "LabelFontSize", 11f,
                new ConfigDescription("Small caps labels (PRESSURE, HELMET...).", new AcceptableValueRange<float>(7f, 22f)));

            DiegeticTiers = cfg.Bind(S, "DiegeticTiers", true,
                "The HUD is the suit's HUD: full readout only with a powered suit; without " +
                "one you get felt-sense WORDS (WARM, HUNGRY) instead of numbers; the robot " +
                "always sees everything. Off = always show the full readout.");
            DetailedVitalsTooltips = cfg.Bind(S, "DetailedVitalsTooltips", true,
                "Show a hover tooltip on UI Ascended's vitals panel and add exact mood, hygiene, " +
                "and per-minute rates to the game's player-stats tooltip. Shared HUD content " +
                "rather than a per-tier visual style. Default ON and stored with the profile theme.");
            FlickerAnimations = cfg.Bind(S, "FlickerAnimations", true,
                "Panels flicker out when they turn off and boot in with a stagger when the " +
                "suit powers up; suit death collapses the HUD.");
            LowPowerDropouts = cfg.Bind(S, "LowPowerDropouts", true,
                "Below the low-power threshold the HUD suffers occasional single-frame dropouts.");
            LowPowerThreshold = cfg.Bind(S, "LowPowerThreshold", 10f,
                new ConfigDescription("Suit battery % under which the HUD starts glitching.",
                    new AcceptableValueRange<float>(0f, 40f)));

            GlitchOnPowerDown = cfg.Bind(S, "GlitchOnPowerDown", true,
                "Play the glitch when the suit powers down / is taken off (HUD collapses).");
            GlitchOnPowerUp = cfg.Bind(S, "GlitchOnPowerUp", true,
                "Play the glitch when the suit powers on / boots up.");

            // ---- 0.9.0 effect tiers. Defaults per FlorpyDorp (2026-07-13): Tier A ON,
            // Tier B ON (auto-degrades when the bundle is missing), Tier C OFF (experimental). ----
            const string FX = "11. HUD Effects (0.9.0)";
            FxTierA = cfg.Bind(FX, "TierA_MeshEffects", true,
                "Master switch for the mesh effects (hairline fades, edge light, pulse). Cheap; on by default.");
            FxTierB = cfg.Bind(FX, "TierB_ShaderEffects", true,
                "Master switch for the bundle-shader effects (shine sweep, dissolve reveal, iridescence, " +
                "chromatic aberration). Requires uia_effects.bundle in the mod folder; silently degrades " +
                "to Tier A looks when missing.");
            FxTierC = cfg.Bind(FX, "TierC_FrostedGlass_EXPERIMENTAL", false,
                "Master switch for the frosted-glass backdrop (blur+tint of the world behind panels). " +
                "EXPERIMENTAL: costs a scene capture + blur per frame. Flat/VertexWarp curvature only.");
            FxHairlinesOn = cfg.Bind(FX, "HairlinesOn", true, "Sub-1px lines fade by coverage instead of vanishing.");
            FxEdgeLightOn = cfg.Bind(FX, "EdgeLightOn", true, "Directional edge light on borders and drawn lines.");
            FxPulseOn = cfg.Bind(FX, "PulseOn", true, "Allow the per-element breathing pulse (elements still opt in individually).");
            FxShineOn = cfg.Bind(FX, "ShineOn", true, "The travelling light sweep (Tier B).");
            FxIridOn = cfg.Bind(FX, "IridescenceOn", true, "The thin-film rainbow on rims (Tier B).");
            FxChromaOn = cfg.Bind(FX, "ChromaticAberrationOn", true, "Colour fringing of the frosted backdrop (needs Tier C).");
            FxHairlineMin = cfg.Bind(FX, "HairlineMinWidth", 0.15f,
                new ConfigDescription("Thinnest drawable line width in px. Below 1px lines render 1px wide " +
                    "and FADE by coverage instead of vanishing (phone-wire AA).",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            FxEdgeLight = cfg.Bind(FX, "EdgeLightStrength", 0.55f,
                new ConfigDescription("Directional edge-light on drawn lines: brighter where the stroke " +
                    "faces the key light, matching the panel borders. 0 = flat strokes.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxEdgeLightColor = cfg.Bind(FX, "EdgeLightColor", "#FFFFFF",
                "The colour a border/line's edge light whitens TOWARD, #RRGGBB. White = the classic " +
                "specular look; a tint (e.g. cyan #35C8E8) gives coloured edge highlights.");
            FxEdgeLightAngle = cfg.Bind(FX, "EdgeLightAngleDeg", 116.565f,
                new ConfigDescription("Direction the key light comes FROM, in degrees (0 = right, 90 = top, " +
                    "180 = left, 270 = bottom). The edges facing this way catch the light. Default ~117 = " +
                    "upper-left (the shipped look).", new AcceptableValueRange<float>(0f, 360f)));
            FxEdgeLightRim = cfg.Bind(FX, "EdgeLightRim", 0.5f,
                new ConfigDescription("How strongly the OPPOSITE (far) edge catches a faint counter-light. " +
                    "0 = only the lit side glows; 1+ = a bright rim on both sides.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxEdgeLightSharp = cfg.Bind(FX, "EdgeLightSharpness", 3f,
                new ConfigDescription("Falloff of the light along the border: HIGH = a tight catch on the " +
                    "edges most square-on to the light; LOW = a broad wash spread around the frame.",
                    new AcceptableValueRange<float>(1f, 8f)));
            FxEdgeFadeCurve = cfg.Bind(FX, "EdgeFadeCurve", 1f,
                new ConfigDescription("Shape of the per-element end fade ('Fade box ends L/R' / 'top/bottom' " +
                    "say WHERE it fades; this says HOW). Below 1 the box holds its colour further out then " +
                    "drops away hard (a crisp end); above 1 it starts fading sooner and trails off into a " +
                    "long atmospheric tail. 1 = the classic ramp.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            FxEdgeFadeBorder = cfg.Bind(FX, "EdgeFadeBorder", 1f,
                new ConfigDescription("How much the BORDER line joins the end fade. 1 = it fades with the box " +
                    "(the classic look). Below 1 the border keeps its colour while the fill melts out from " +
                    "under it, so the outline survives further into the fade. Above 1 the border surrenders " +
                    "first and the plate outlives its own outline.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxEdgeRipple = cfg.Bind(FX, "EdgeRipple", 0.45f,
                new ConfigDescription("Irregular light/dark shimmer ALONG borders and lines (the concept art's " +
                    "'random glowy' look). 0 = the smooth single-light run. Above ~1.4 the shimmer " +
                    "overdrives: dark troughs clip to fully dark and bright crests overshoot.",
                    new AcceptableValueRange<float>(0f, 2.5f)));
            FxEdgeRippleFreq = cfg.Bind(FX, "EdgeRippleFrequency", 2f,
                new ConfigDescription("Shimmer frequency — higher = light/dark repeats more often along the edge; " +
                    "LOW (down to 0.05) = a single wide, slow light→dark sweep. Pair a low value with a high " +
                    "per-element 'Ripple gradient' for one broad soft gradient.",
                    new AcceptableValueRange<float>(0.05f, 8f)));
            FxEdgeFlowSpeed = cfg.Bind(FX, "EdgeFlowSpeed", 0.22f,
                new ConfigDescription("Animation speed of SDF edge energy. 0 freezes the pattern; " +
                    "higher values move luminous detail through the border band without rebuilding the Canvas.",
                    new AcceptableValueRange<float>(0f, 4f)));
            FxRippleDesync = cfg.Bind(FX, "RippleDesync", false,
                "Give every element a slightly different edge-ripple FREQUENCY and flow TEMPO, seeded " +
                "stably per element, so identical settings no longer shimmer or flow in perfect " +
                "lockstep — the pattern drifts apart and stays organic. The tempo nudge is what breaks " +
                "the moving flow's unison even on small pips (frequency alone only varies the static " +
                "wavelength). Off = every element in sync (the classic look). Applies to panels, pen " +
                "shapes and lines; no restart needed.");
            FxRippleDesyncAmount = cfg.Bind(FX, "RippleDesyncAmount", 0.4f,
                new ConfigDescription("How far each element's ripple frequency and flow tempo may wander " +
                    "from the authored value: 0 = no variation (locked), 1 = up to about a third " +
                    "faster/slower per element (super organic). Only applies when RippleDesync is on.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxBorderFadeOn = cfg.Bind(FX, "BorderFadeOn", true, "Unlit border sections dissolve away.");
            FxSoftEdgeOn = cfg.Bind(FX, "SoftEdgeOn", true, "Panel fills melt softly outward.");
            FxGlowOn = cfg.Bind(FX, "GlowOn", false,
                "Luminous halo around frames. OFF by default: halos of overlapping elements stack, " +
                "so it reads best enabled per-element (or globally on sparse layouts) at low strength.");
            FxBorderFade = cfg.Bind(FX, "BorderFade", 0.35f,
                new ConfigDescription("How much unlit border sections DISSOLVE: 0 = solid outline everywhere, " +
                    "1 = only the lit parts of the frame exist (concept-art look).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxSoftEdge = cfg.Bind(FX, "SoftEdgePx", 0f,
                new ConfigDescription("Soft outer fade of panel fills, px — boxes melt into each other " +
                    "instead of ending crisply. 0 = classic crisp AA edge.",
                    new AcceptableValueRange<float>(0f, 48f)));
            FxGlow = cfg.Bind(FX, "GlowStrength", 0.3f,
                new ConfigDescription("Luminous halo OUTSIDE frames, tinted by each element's own accent — " +
                    "warning chips bleed their colour onto the glass around them.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxGlowInner = cfg.Bind(FX, "GlowInnerStrength", 0.25f,
                new ConfigDescription("The same glow mirrored INTO the box: the frame's light bleeds " +
                    "onto the glass inside it (the concept art's lines that glow both ways). " +
                    "Independent of the outward strength.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxGlowWidth = cfg.Bind(FX, "GlowWidthPx", 14f,
                new ConfigDescription("Halo / flowing-aura reach in px. Wide halos on adjacent elements " +
                    "merge into grey wash and increase transparent overdraw — keep extreme values for " +
                    "sparse layouts.", new AcceptableValueRange<float>(6f, 320f)));
            FxGlowDiffuse = cfg.Bind(FX, "GlowDiffuse", 0.5f,
                new ConfigDescription("How DIFFUSE the halo is: 0 hugs the frame as a tight rim glow, " +
                    "1 spreads it into a wide soft haze — dimmer at the frame, reaching further out, " +
                    "wrapping more of the perimeter, and free of the ripple's radial streaks. " +
                    "Pair high values with a larger GlowWidthPx.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxGlowExtraDiffuse = cfg.Bind(FX, "GlowExtraDiffuse", 0f,
                new ConfigDescription("Softness BEYOND GlowDiffuse's ceiling, for both the outer halo " +
                    "and the inner glow: the falloff flattens toward the smoothest artifact-free curve " +
                    "and the halo wraps nearly the whole perimeter. 0 = classic. Applies on every " +
                    "render path (panels, pen shapes, lines).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxGlowHaze = cfg.Bind(FX, "GlowExtendedHaze", 0f,
                new ConfigDescription("Adds a second, very faint long-tail atmosphere across the authored " +
                    "halo radius. Unlike Diffuse this does not replace the core falloff. SDF panels only; " +
                    "0 = off.", new AcceptableValueRange<float>(0f, 1f)));
            FxGlowBreathOn = cfg.Bind(FX, "GlowBreathingOn", false,
                "Let the normal halo and/or flowing edge aura breathe without dimming the panel, " +
                "text, or border. SDF panels only.");
            FxGlowBreath = cfg.Bind(FX, "GlowBreathDepth", 0.35f,
                new ConfigDescription("Depth of halo/aura-only breathing. 0 = steady; 1 = a pronounced " +
                    "but bounded inhale/exhale.", new AcceptableValueRange<float>(0f, 1f)));
            FxGlowBreathSpeed = cfg.Bind(FX, "GlowBreathSpeedHz", 0.25f,
                new ConfigDescription("Shared halo breathing rate in cycles per second. Uses unscaled UI " +
                    "time so authoring remains visible while F9 is open.",
                    new AcceptableValueRange<float>(0.03f, 2f)));
            FxGlowUnevenOn = cfg.Bind(FX, "GlowUnevennessOn", false,
                "Break up the normal halo and/or flowing aura with stable local-space variation so its " +
                "reach and brightness are not perfectly straight/even. SDF panels only.");
            FxGlowUneven = cfg.Bind(FX, "GlowUnevenness", 0.5f,
                new ConfigDescription("Amount of stable organic variation in halo reach and energy. " +
                    "This pattern does not shimmer or swim with the screen.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxGlowOrganicScale = cfg.Bind(FX, "GlowOrganicScale", 1f,
                new ConfigDescription("Footprint of the unevenness pattern: above 1 the halo's reach " +
                    "wanders in large, slow panel-scale blotches (super organic); below 1 it becomes " +
                    "finer texture. 1 = the classic look. SDF panels only.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            FxGlowFlowAuraOn = cfg.Bind(FX, "FlowingEdgeAuraOn", false,
                "Emit a separate halo from the animated edge-energy crests. It can work with the normal " +
                "outward halo at zero and reuses GlowWidthPx / GlowDiffuse. SDF panels only.");
            FxGlowFlowAura = cfg.Bind(FX, "FlowingEdgeAuraStrength", 0.6f,
                new ConfigDescription("Strength of the aura emitted by moving edge-energy crests. " +
                    "Irregular energy controls its contrast; EdgeFlowSpeed controls its motion.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxAlertPulseOn = cfg.Bind(FX, "AlertPulseOn", true,
                "Suit warnings colour the HUD. A CAUTION (low air, hunger, battery...) makes the halo " +
                "and edge ripple flash amber a few times and then clear completely - it annunciates " +
                "and gets out of the way, and does not come back for that same warning. A CRITICAL " +
                "warning breathes red indefinitely, until the warning itself clears. Read-only - it only reads the " +
                "warnings the game already shows you. Suited (or robot) only - never in bare mode, " +
                "which has no powered visor to carry it. Needs TierA_MeshEffects on for the halo; " +
                "with Tier A off only the border line itself recolours.");
            FxAlertBreathSeconds = cfg.Bind(FX, "AlertBreathSeconds", 1.4f,
                new ConfigDescription("How long ONE breath lasts, in seconds (a full fade in and back " +
                    "out). Applies to both the caution flashes and the critical breath. The 0.35s " +
                    "floor is deliberate: faster than about 3 per second enters the photosensitivity " +
                    "band - do not lower it. Total caution flash time = this x AlertCautionBreaths.",
                    new AcceptableValueRange<float>(0.35f, 5f)));
            FxAlertCautionBreaths = cfg.Bind(FX, "AlertCautionBreaths", 3,
                new ConfigDescription("How many times a CAUTION breathes before it clears and gets out " +
                    "of the way. Does not affect CRITICAL, which breathes for as long as it stands.",
                    new AcceptableValueRange<int>(1, 10)));
            FxAlertPulseStrength = cfg.Bind(FX, "AlertPulseStrength", 0.6f,
                new ConfigDescription("How hard the alert reads. For the CAUTION flash " +
                    "this scales its peak (it always fades back to nothing, so it can never become " +
                    "invisible). For the persistent CRITICAL breath it scales the swing: 0 = a steady " +
                    "red with no motion at all (the colour cue is kept), 1 = a hard bright/dark throb.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxAlertCautionBright = cfg.Bind(FX, "AlertCautionBrightness", 1f,
                new ConfigDescription("Brightness gain on the CAUTION alert colour. 1 = the palette " +
                    "colour as picked; above 1 drives it toward a hot, blown-out amber; below 1 " +
                    "gives a subdued tint. Pairs with the HudAlertCaution palette entry.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            FxAlertCriticalBright = cfg.Bind(FX, "AlertCriticalBrightness", 1f,
                new ConfigDescription("Brightness gain on the CRITICAL alert colour. 1 = the palette " +
                    "colour as picked; above 1 drives it toward a hot, blown-out red; below 1 gives " +
                    "a subdued tint. Pairs with the HudAlertCritical palette entry.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            FxPulseSpeed = cfg.Bind(FX, "PulseSpeedHz", 0.5f,
                new ConfigDescription("Breathing-pulse rate for elements that opt in, cycles per second.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
            FxPulseDepth = cfg.Bind(FX, "PulseDepth", 0.25f,
                new ConfigDescription("How deep the dim half of the pulse goes (0 = imperceptible, 1 = to black).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxShine = cfg.Bind(FX, "ShineStrength", 0.6f,
                new ConfigDescription("Global strength of the light sweep that travels across panels.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxShinePeriod = cfg.Bind(FX, "ShinePeriodSeconds", 9f,
                new ConfigDescription("Seconds between shine sweeps.",
                    new AcceptableValueRange<float>(2f, 60f)));
            FxIridescence = cfg.Bind(FX, "IridescenceStrength", 0.25f,
                new ConfigDescription("Subtle thin-film rainbow on glass edges. Keep low — it's a whisper.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxChroma = cfg.Bind(FX, "ChromaticAberration", 0.3f,
                new ConfigDescription("Colour fringing toward panel rims (0 = off).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxPowerDownMirrorsBoot = cfg.Bind(FX, "PowerDownMirrorsBoot", true,
                "Power DOWN plays as the reverse of power UP: the same seeded, staggered flicker, " +
                "element by element. Off = the old all-at-once CRT death.");
            FxDissolveOnPowerDown = cfg.Bind(FX, "DissolveOnPowerDown", true,
                "Play the boot dissolve frontier in REVERSE on power-down, so the HUD dissolves away " +
                "instead of just flickering off. Needs the same shader bundle as the boot dissolve.");
            FxCollapseOn = cfg.Bind(FX, "DeathCollapseOn", false,
                "Master for the CRT 'death collapse' squash on power loss. OFF by default — it is an " +
                "opt-in flourish, not the standard way the HUD leaves.");
            FxCollapseAmt = cfg.Bind(FX, "DeathCollapseStrength", 1f,
                new ConfigDescription("Default collapse strength for elements that follow the globals.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxGlitchOn = cfg.Bind(FX, "GlitchTearOn", true,
                "Master for the glitch tear on power transitions. Also the master for HudGlitch's " +
                "own transform-jitter envelope (fire gate) — the two systems were unified 0.9.2.5.");
            FxGlitchAmt = cfg.Bind(FX, "GlitchTearStrength", 1f,
                new ConfigDescription("Default glitch strength for elements that follow the globals; " +
                    "also HudGlitch's peak envelope severity.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxGlitchDuration = cfg.Bind(FX, "GlitchTearDuration", 1.1f,
                new ConfigDescription("How long the glitch tear envelope lasts, seconds.",
                    new AcceptableValueRange<float>(0.1f, 4f)));
            FxWarpOn = cfg.Bind(FX, "WarpParticipationOn", true,
                "Master for whether elements take the visor warp/curve at all.");
            FxWarpAmt = cfg.Bind(FX, "WarpStrength", 1f,
                new ConfigDescription("Default warp strength for elements that follow the globals.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxTvOffOn = cfg.Bind(FX, "TvOffOn", false,
                "Master for the CRT 'TV off' exit: the element squashes to a horizontal LINE and " +
                "then pinches to a dot before blinking out. Off by default — like the death " +
                "collapse it is an opt-in flourish, and the two are different exits for the same " +
                "moment. Elements may force it on or off individually.");
            FxTvOffAmt = cfg.Bind(FX, "TvOffStrength", 1f,
                new ConfigDescription("Default TV-off strength for elements that follow the globals " +
                    "(0 = no pinch, 1 = the full line-then-dot, 2 = an exaggerated snap).",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxFlickerAmt = cfg.Bind(FX, "FlickerStrength", 1f,
                new ConfigDescription("Default flicker strength for elements that follow the globals. " +
                    "FlickerAnimations is the MASTER; this is how hard the strike-on / gutter-out " +
                    "reads (0 = a clean fade with no flicker, 2 = a badly-wired tube).",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxDissolveAmt = cfg.Bind(FX, "DissolveStrength", 1f,
                new ConfigDescription("Default dissolve strength for elements that follow the globals. " +
                    "DissolveOnBoot is the MASTER; this scales how pronounced the travelling " +
                    "frontier is (0 = no dissolve, 2 = a wide, hot edge).",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxPulseAmt = cfg.Bind(FX, "PulseStrength", 0f,
                new ConfigDescription("Default breathing-pulse strength for elements that follow the " +
                    "globals. DEFAULT 0 on purpose: the pulse has always been an opt-in per-element " +
                    "accent, so inheriting elements must not start breathing on their own. Raise it " +
                    "to make the whole HUD breathe; individual elements can still force their own " +
                    "strength. Speed and depth remain the shared PulseSpeedHz / PulseDepth globals.",
                    new AcceptableValueRange<float>(0f, 2f)));

            FxDissolveBoot = cfg.Bind(FX, "DissolveOnBoot", true,
                "Elements power on with a travelling dissolve frontier during boot/power transitions " +
                "(needs the shader bundle; falls back to the classic flicker otherwise).");
            FrostStrength = cfg.Bind(FX, "FrostStrength", 1f,
                new ConfigDescription("Global frost strength (0..1): scales the frosted-glass blur/tint on " +
                    "EVERY element at once. Each element's own 'frost strength' multiplies on top of this " +
                    "(1 = full per-element look; 0 = no frost anywhere).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FrostDepth = cfg.Bind(FX, "FrostDepth", 1f,
                new ConfigDescription("Backdrop blur depth: 0 uses the shallowest pyramid level, " +
                    "1 preserves the current full dual-Kawase result. Elements may override it.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FrostDownsample = cfg.Bind(FX, "FrostDownsample", 4f,
                new ConfigDescription("Frost blur RT divisor: 4 = quarter resolution (cheapest good look).",
                    new AcceptableValueList<float>(2f, 4f, 8f)));
            FrostUpdateEveryN = cfg.Bind(FX, "FrostUpdateEveryNFrames", 2,
                new ConfigDescription("Re-blur the backdrop every N frames (the world changes slowly).",
                    new AcceptableValueRange<int>(1, 8)));
            FrostDarken = cfg.Bind(FX, "FrostDarken", 0.75f,
                new ConfigDescription("How much the world behind glass panels darkens (0..1).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FrostTint = cfg.Bind(FX, "FrostTint", "#B6BCC2",
                "Tint of the frosted backdrop, #RRGGBB. Default is a cool GRAY (FlorpyDorp: grayer " +
                "than the concept art's blue-black); edit freely.");

            FxBloomOn = cfg.Bind(FX, "BloomOn", false,
                "HUD light bleed — bright borders/text/chips glow onto neighbouring elements; routes " +
                "the HUD through a render texture. Works in every curvature mode (flat/vertex-warp " +
                "are forced onto the RT path while it is on).");
            FxBloomStrength = cfg.Bind(FX, "BloomStrength", 0.8f,
                new ConfigDescription("How strongly the extracted glow is added back onto the HUD.",
                    new AcceptableValueRange<float>(0f, 3f)));
            FxBloomThreshold = cfg.Bind(FX, "BloomThreshold", 0.55f,
                new ConfigDescription("Brightness (max of R/G/B) a HUD pixel must exceed to bloom. " +
                    "Lower = more of the HUD glows; higher = only the brightest accents.",
                    new AcceptableValueRange<float>(0f, 1.5f)));
            FxBloomKnee = cfg.Bind(FX, "BloomKnee", 0.5f,
                new ConfigDescription("Soft-knee width as a fraction of the threshold: 0 = a hard " +
                    "cutoff, 1 = a wide smooth ramp so edges fade into bloom instead of popping.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxBloomBlurSteps = cfg.Bind(FX, "BloomBlurSteps", 3,
                new ConfigDescription("Blur pyramid depth: higher = a wider, softer, more expensive " +
                    "halo. Each step DOUBLES the reach (it halves the resolution once more) — for " +
                    "fine width control between steps use BloomSpread, which is continuous.",
                    new AcceptableValueRange<int>(1, 5)));
            FxBloomFineDetail = cfg.Bind(FX, "BloomFineDetail", true,
                "Thin borders and drawn LINES survive into the bloom: the bright-pass runs at half " +
                "resolution instead of quarter, so 1-2px features aren't averaged below the " +
                "threshold before extraction (play-test: 'border edges of boxes don't get bloom'). " +
                "Slightly more expensive; the glow's reach also tightens — add a blur step to match.");
            FxBloomSpread = cfg.Bind(FX, "BloomSpread", 1.5f,
                new ConfigDescription("Continuous glow width WITHIN a step count (the blur's sample " +
                    "spread): the fine-adjust between the doubling jumps of BloomBlurSteps. Above ~2.2 " +
                    "on few steps the blur can shimmer slightly on thin lines.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            FxBloomSaturation = cfg.Bind(FX, "BloomSaturation", 1f,
                new ConfigDescription("Glow colour: 0 = white-hot monochrome halo, 1 = the HUD's own " +
                    "hues, up to 2 = oversaturated neon.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FxBloomTint = cfg.Bind(FX, "BloomTint", "#FFFFFF",
                "Tint multiplied into the glow, #RRGGBB. White = untinted; try a pale cyan for a " +
                "hologram cast.");
            FxBloomAnamorph = cfg.Bind(FX, "BloomAnamorphic", 0f,
                new ConfigDescription("Streak the glow: +1 = wide HORIZONTAL sci-fi visor streaks, " +
                    "-1 = vertical halation, 0 = round bloom. Done by blurring an asymmetric-" +
                    "resolution pyramid — free.", new AcceptableValueRange<float>(-1f, 1f)));
            FxBloomRes = cfg.Bind(FX, "BloomResolution", -1,
                new ConfigDescription("Bright-pass base resolution: 0 = FULL (crisp hairline glow, " +
                    "priciest), 1 = HALF, 2 = QUARTER (soft dreamy haze, cheapest). -1 = legacy: " +
                    "follow the BloomFineDetail toggle.", new AcceptableValueRange<int>(-1, 2)));
            FxBloomPulseOn = cfg.Bind(FX, "BloomPulseOn", false,
                "The whole glow breathes: a slow sine on bloom strength.");
            FxBloomPulseSpeed = cfg.Bind(FX, "BloomPulseSpeed", 0.25f,
                new ConfigDescription("Breaths per second.", new AcceptableValueRange<float>(0.05f, 2f)));
            FxBloomPulseDepth = cfg.Bind(FX, "BloomPulseDepth", 0.25f,
                new ConfigDescription("Fraction of the glow breathed away at the low point.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxBloomReactOn = cfg.Bind(FX, "BloomReactOn", false,
                "State-reactive glow: suit power dims it, critical alarms pulse it red, the boot " +
                "sequence flares it. Each reaction has its own strength below.");
            FxBloomReactPower = cfg.Bind(FX, "BloomReactPower", 0.6f,
                new ConfigDescription("How much a draining suit battery dims the glow (0 = ignore " +
                    "power, 1 = fully dark at 0%).", new AcceptableValueRange<float>(0f, 1f)));
            FxBloomReactAlarm = cfg.Bind(FX, "BloomReactAlarm", 0.6f,
                new ConfigDescription("Red pulse strength while a critical state is active (low " +
                    "power / critical health).", new AcceptableValueRange<float>(0f, 1f)));
            FxBloomReactBoot = cfg.Bind(FX, "BloomReactBoot", 0.8f,
                new ConfigDescription("Glow flare while the boot sequence plays.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FxBloom2On = cfg.Bind(FX, "Bloom2On", false,
                "SECOND bloom band — an independent glow layer only pixels above ITS threshold " +
                "reach. Style borders brighter than text (edge light / spec / border colour) and " +
                "the frame lines get their own glow, tuned separately from the general bloom.");
            FxBloom2Threshold = cfg.Bind(FX, "Bloom2Threshold", 0.85f,
                new ConfigDescription("Brightness cutoff for the highlight band. Set BETWEEN your " +
                    "border brightness and everything else's.", new AcceptableValueRange<float>(0f, 1.5f)));
            FxBloom2Strength = cfg.Bind(FX, "Bloom2Strength", 1.2f,
                new ConfigDescription("Highlight-band glow strength (adds on top of band 1).",
                    new AcceptableValueRange<float>(0f, 3f)));
            FxBloom2Steps = cfg.Bind(FX, "Bloom2BlurSteps", 2,
                new ConfigDescription("Highlight-band blur depth — small = a tight hot rim around " +
                    "the lines, large = a wide aura.", new AcceptableValueRange<int>(1, 5)));
            FxBloom2Spread = cfg.Bind(FX, "Bloom2Spread", 1.5f,
                new ConfigDescription("Highlight-band continuous width fine-adjust.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            FxBloom2Tint = cfg.Bind(FX, "Bloom2Tint", "#FFFFFF",
                "Tint on the highlight glow only, #RRGGBB — e.g. cyan frames over a neutral base bloom.");
            FxBloomSatBias = cfg.Bind(FX, "BloomSatBias", 0f,
                new ConfigDescription("Saturation selectivity of the base bloom: +1 = only COLOURED " +
                    "pixels bloom (white text stays dark), -1 = only white/grey pixels. 0 = off. " +
                    "Needs the 2026-07-15+ effects bundle; older bundles ignore it.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            FxBloom2SatBias = cfg.Bind(FX, "Bloom2SatBias", 0f,
                new ConfigDescription("Saturation selectivity of the highlight band — set POSITIVE " +
                    "so coloured BORDER lines get this glow while white text does not (the " +
                    "borders-not-text knob). Needs the 2026-07-15+ effects bundle.",
                    new AcceptableValueRange<float>(-1f, 1f)));

            HudPalette.Bind(cfg);
        }
    }
}
