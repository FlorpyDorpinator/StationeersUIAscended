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
        public static ConfigEntry<bool> LegacyImGuiHud;
        public static ConfigEntry<KeyCode> HudEditorKey;
        public static ConfigEntry<bool> UseDocumentHud;
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

        // Panels
        public static ConfigEntry<bool> ShowTopBar;
        public static ConfigEntry<bool> ShowCompass;
        public static ConfigEntry<bool> ShowEquipment;
        public static ConfigEntry<bool> ShowHands;
        public static ConfigEntry<bool> ShowVitals;
        public static ConfigEntry<bool> ShowVignette;
        public static ConfigEntry<bool> ShowHologram;

        // Layout / sizes
        public static ConfigEntry<float> HudScale;
        public static ConfigEntry<float> TopBarHeight;
        public static ConfigEntry<float> TopBarCurve;
        public static ConfigEntry<float> TopBarWidthPct;
        public static ConfigEntry<float> CompassWidthPct;
        public static ConfigEntry<float> CompassHeight;
        public static ConfigEntry<float> CompassFovDeg;
        public static ConfigEntry<float> EquipBoxSize;
        public static ConfigEntry<float> EquipSpacing;
        public static ConfigEntry<float> HandBoxWidth;
        public static ConfigEntry<float> HandBoxHeight;
        public static ConfigEntry<float> VitalsWidth;
        public static ConfigEntry<float> VitalsHeight;

        // Panel styling
        public static ConfigEntry<float> CornerRadius;
        public static ConfigEntry<float> BorderWidth;
        public static ConfigEntry<float> EdgeFeather;
        public static ConfigEntry<float> GlassSheen;
        public static ConfigEntry<float> GlassEdge;

        // Text
        public static ConfigEntry<string> FontName;
        public static ConfigEntry<float> FontScale;
        public static ConfigEntry<float> LabelFontSize;
        public static ConfigEntry<float> ValueFontSize;
        public static ConfigEntry<float> CompassFontSize;
        public static ConfigEntry<float> BareWordFontSize;
        public static ConfigEntry<float> VitalsRowFontSize;

        // Diegetic behavior
        public static ConfigEntry<bool> DiegeticTiers;
        public static ConfigEntry<bool> FlickerAnimations;
        public static ConfigEntry<bool> LowPowerDropouts;
        public static ConfigEntry<float> LowPowerThreshold;

        // Power-transition glitch (transform-jitter envelope; see HudGlitch)
        // NOTE: the old GlitchShader knob was RETIRED in 0.9.0 — it was bound but read by
        // nothing (a leftover from the abandoned CameraFilterPack screen-shader approach);
        // its orphaned value in existing cfg files is harmless (FlorpyDorp-approved call).
        public static ConfigEntry<bool> GlitchEnabled;
        public static ConfigEntry<float> GlitchDuration;
        public static ConfigEntry<float> GlitchIntensity;
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
        public static ConfigEntry<float> FxEdgeRipple;      // irregular light/dark shimmer along edges (0 = smooth)
        public static ConfigEntry<float> FxEdgeRippleFreq;  // shimmer frequency, cycles per ~100px
        public static ConfigEntry<float> FxPulseSpeed;      // breathing pulse rate, Hz
        public static ConfigEntry<float> FxPulseDepth;      // 0..1 how deep the dim half of the pulse goes
        public static ConfigEntry<float> FxShine;           // shine sweep global strength (Tier B)
        public static ConfigEntry<float> FxShinePeriod;     // seconds between sweeps
        public static ConfigEntry<float> FxIridescence;     // iridescent edge global strength (Tier B)
        public static ConfigEntry<float> FxChroma;          // chromatic-aberration strength (Tier B)
        public static ConfigEntry<bool> FxDissolveBoot;     // dissolve reveal on boot/power transitions
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

        public static void Bind(ConfigFile cfg)
        {
            const string S = "10. Visor HUD";

            VisorHudEnabled = cfg.Bind(S, "VisorHudEnabled", true,
                "The UGUI visor HUD: curved top status bar, compass, equipment column, hand " +
                "boxes, vitals card, diegetic power tiers. Replaces the legacy ImGui overlay.");
            UseDocumentHud = cfg.Bind(S, "UseDocumentHud", true,
                "Render the HUD from the active layout profile (a document of movable, " +
                "restylable elements — the HUD Designer). Off = the fixed 0.5.0 panel set, " +
                "kept as a fallback during the transition.");
            HudActiveProfile = cfg.Bind(S, "HudActiveProfile", "Smaller Test",
                "Which HUD layout profile to render (a .xml in config/StationeersUIMod/" +
                "HudProfiles). 'Smaller Test' is the shipped default for the 0.9.0 play-test " +
                "(FlorpyDorp's pick); shipped profiles import from the mod folder on first run.");
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
            LegacyImGuiHud = cfg.Bind(S, "LegacyImGuiHud", false,
                "Draw the old 0.1.0 ImGui HUD instead (kept as a fallback during the port).");
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

            ShowTopBar = cfg.Bind(S, "ShowTopBar", true, "The curved top status bar.");
            ShowCompass = cfg.Bind(S, "ShowCompass", true, "Compass ribbon under the top bar.");
            ShowEquipment = cfg.Bind(S, "ShowEquipment", true, "Left equipment column (1-6).");
            ShowHands = cfg.Bind(S, "ShowHands", true, "Bottom-centre hand boxes. Never a hotbar.");
            ShowVitals = cfg.Bind(S, "ShowVitals", true, "Bottom-right vitals card.");
            ShowVignette = cfg.Bind(S, "ShowVignette", true, "Visor-edge darkening.");
            ShowHologram = cfg.Bind(S, "ShowHologram", true,
                "The live 3D character hologram inside the vitals card.");

            HudScale = cfg.Bind(S, "VisorHudScale", 1.0f,
                new ConfigDescription("Overall visor HUD scale.", new AcceptableValueRange<float>(0.6f, 1.6f)));
            TopBarHeight = cfg.Bind(S, "TopBarHeight", 64f,
                new ConfigDescription("Top bar band thickness (px).", new AcceptableValueRange<float>(36f, 120f)));
            TopBarCurve = cfg.Bind(S, "TopBarCurve", 26f,
                new ConfigDescription("How far the top bar's ends sweep down (px). 0 = straight bar.",
                    new AcceptableValueRange<float>(0f, 120f)));
            TopBarWidthPct = cfg.Bind(S, "TopBarWidth", 0.98f,
                new ConfigDescription("Top bar width as a fraction of the screen.",
                    new AcceptableValueRange<float>(0.5f, 1f)));
            CompassWidthPct = cfg.Bind(S, "CompassWidth", 0.12f,
                new ConfigDescription("Compass ribbon width as a fraction of the screen (the " +
                    "'about 10% of the top centre' ask).", new AcceptableValueRange<float>(0.05f, 0.4f)));
            CompassHeight = cfg.Bind(S, "CompassHeight", 34f,
                new ConfigDescription("Compass ribbon height (px).", new AcceptableValueRange<float>(20f, 80f)));
            CompassFovDeg = cfg.Bind(S, "CompassSpanDegrees", 90f,
                new ConfigDescription("How many compass degrees the ribbon spans.",
                    new AcceptableValueRange<float>(40f, 200f)));
            EquipBoxSize = cfg.Bind(S, "EquipBoxSize", 76f,
                new ConfigDescription("Equipment column box size (px).", new AcceptableValueRange<float>(44f, 128f)));
            EquipSpacing = cfg.Bind(S, "EquipSpacing", 10f,
                new ConfigDescription("Gap between equipment boxes (px).", new AcceptableValueRange<float>(2f, 30f)));
            HandBoxWidth = cfg.Bind(S, "HandBoxWidth", 132f,
                new ConfigDescription("Hand box width (px).", new AcceptableValueRange<float>(80f, 240f)));
            HandBoxHeight = cfg.Bind(S, "HandBoxHeight", 92f,
                new ConfigDescription("Hand box height (px).", new AcceptableValueRange<float>(56f, 160f)));
            VitalsWidth = cfg.Bind(S, "VitalsWidth", 250f,
                new ConfigDescription("Vitals card width (px).", new AcceptableValueRange<float>(160f, 420f)));
            VitalsHeight = cfg.Bind(S, "VitalsHeight", 158f,
                new ConfigDescription("Vitals card height (px).", new AcceptableValueRange<float>(100f, 300f)));

            CornerRadius = cfg.Bind(S, "CornerRadius", 10f,
                new ConfigDescription("Panel corner rounding (px).", new AcceptableValueRange<float>(0f, 28f)));
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

            FontName = cfg.Bind(S, "HudFontName", "",
                "TMP font for all HUD text (substring match; empty = the game's default " +
                "face). The F9 editor lists every loaded font.");
            FontScale = cfg.Bind(S, "HudFontScale", 1.0f,
                new ConfigDescription("Multiplies every HUD font size.", new AcceptableValueRange<float>(0.6f, 1.8f)));
            LabelFontSize = cfg.Bind(S, "LabelFontSize", 11f,
                new ConfigDescription("Small caps labels (PRESSURE, HELMET...).", new AcceptableValueRange<float>(7f, 22f)));
            ValueFontSize = cfg.Bind(S, "ValueFontSize", 17f,
                new ConfigDescription("The big readout values.", new AcceptableValueRange<float>(10f, 30f)));
            CompassFontSize = cfg.Bind(S, "CompassFontSize", 12f,
                new ConfigDescription("Compass letters/degrees.", new AcceptableValueRange<float>(8f, 22f)));
            BareWordFontSize = cfg.Bind(S, "BareWordFontSize", 20f,
                new ConfigDescription("The felt-sense words (WARM, HUNGRY...).", new AcceptableValueRange<float>(12f, 36f)));
            VitalsRowFontSize = cfg.Bind(S, "VitalsRowFontSize", 13f,
                new ConfigDescription("Vitals card rows.", new AcceptableValueRange<float>(9f, 22f)));

            DiegeticTiers = cfg.Bind(S, "DiegeticTiers", true,
                "The HUD is the suit's HUD: full readout only with a powered suit; without " +
                "one you get felt-sense WORDS (WARM, HUNGRY) instead of numbers; the robot " +
                "always sees everything. Off = always show the full readout.");
            FlickerAnimations = cfg.Bind(S, "FlickerAnimations", true,
                "Panels flicker out when they turn off and boot in with a stagger when the " +
                "suit powers up; suit death collapses the HUD.");
            LowPowerDropouts = cfg.Bind(S, "LowPowerDropouts", true,
                "Below the low-power threshold the HUD suffers occasional single-frame dropouts.");
            LowPowerThreshold = cfg.Bind(S, "LowPowerThreshold", 10f,
                new ConfigDescription("Suit battery % under which the HUD starts glitching.",
                    new AcceptableValueRange<float>(0f, 40f)));

            GlitchEnabled = cfg.Bind(S, "GlitchEnabled", false,
                "A momentary screen distortion when the suit powers down / off / on, using one of " +
                "the game's own CameraFilterPack shaders. Distorts the rendered VIEW during the " +
                "transition (the overlay HUD is composited after the camera; a true HUD-only " +
                "version needs the RenderTexture route).");
            GlitchDuration = cfg.Bind(S, "GlitchDuration", 1.1f,
                new ConfigDescription("How long the glitch lasts, seconds.",
                    new AcceptableValueRange<float>(0.1f, 4f)));
            GlitchIntensity = cfg.Bind(S, "GlitchIntensity", 0.85f,
                new ConfigDescription("Peak severity of the glitch (0-1).",
                    new AcceptableValueRange<float>(0f, 1f)));
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
            FxEdgeRipple = cfg.Bind(FX, "EdgeRipple", 0.45f,
                new ConfigDescription("Irregular light/dark shimmer ALONG borders and lines (the concept art's " +
                    "'random glowy' look). 0 = the smooth single-light run. Above ~1.4 the shimmer " +
                    "overdrives: dark troughs clip to fully dark and bright crests overshoot.",
                    new AcceptableValueRange<float>(0f, 2.5f)));
            FxEdgeRippleFreq = cfg.Bind(FX, "EdgeRippleFrequency", 2f,
                new ConfigDescription("Shimmer frequency — higher = light/dark repeats more often along the edge.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
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
                new ConfigDescription("Halo width in px. Wide halos on adjacent elements merge into " +
                    "grey wash — keep modest on dense layouts.", new AcceptableValueRange<float>(6f, 160f)));
            FxGlowDiffuse = cfg.Bind(FX, "GlowDiffuse", 0.5f,
                new ConfigDescription("How DIFFUSE the halo is: 0 hugs the frame as a tight rim glow, " +
                    "1 spreads it into a wide soft haze — dimmer at the frame, reaching further out, " +
                    "wrapping more of the perimeter, and free of the ripple's radial streaks. " +
                    "Pair high values with a larger GlowWidthPx.",
                    new AcceptableValueRange<float>(0f, 1f)));
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
            FxDissolveBoot = cfg.Bind(FX, "DissolveOnBoot", true,
                "Elements power on with a travelling dissolve frontier during boot/power transitions " +
                "(needs the shader bundle; falls back to the classic flicker otherwise).");
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

            HudPalette.Bind(cfg);
        }
    }
}
