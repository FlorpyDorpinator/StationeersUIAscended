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
        /// in-house — we ship no dependencies). True perspective curvature; experimental.</summary>
        CurvedWorldCanvas,
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
            HudActiveProfile = cfg.Bind(S, "HudActiveProfile", "Default",
                "Which HUD layout profile to render (a .xml in config/StationeersUIMod/" +
                "HudProfiles). Missing profiles are recreated from the shipped default.");
            GridSnapEnabled = cfg.Bind(S, "GridSnapEnabled", true,
                "HUD editor: snap dragged/resized elements to the grid. Toggleable live in " +
                "the F9 window; hold Alt while dragging for temporary freeform.");
            GridSnapSize = cfg.Bind(S, "GridSnapSize", 8f,
                new ConfigDescription("HUD editor: grid cell size in reference pixels.",
                    new AcceptableValueRange<float>(2f, 64f)));
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

            HudPalette.Bind(cfg);
        }
    }
}
