using TMPro;
using StationeersUIMod.UI.Hud;
using UnityEngine;
using M = StationeersUIMod.UI.Menu.Kit.UiaMenuTheme;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// The one visual language for the UGUI Control Center. As of 2026-07-17 the colours are
    /// LIVE getters, not constants: by default they DERIVE from the active F9 HUD profile's theme
    /// (<see cref="HudPalette"/> + <see cref="HudConfig"/> glass) so the menu follows the visor,
    /// and fall back to per-user overrides when <see cref="UiaMenuTheme"/> is not following. Every
    /// read site (`UiaTheme.Window`, …) is unchanged — the fields simply became properties. Metrics
    /// stay constant. See <see cref="UiaMenuTheme"/> for the two-state contract and change signal.
    ///
    /// NOTE: several kit widgets FREEZE these colours at build time (button _normal/_hover, Outline
    /// effectColor), so a live theme change needs a menu rebuild — UiaControlCenter polls
    /// <see cref="UiaMenuTheme.StyleHash"/> and restyles.
    /// </summary>
    public static class UiaTheme
    {
        // ---- surfaces: the window IS the HUD panel fill (so the menu background matches the
        // themed HUD boxes exactly); interior surfaces lighten from that same base by a small ramp
        // for depth. Only the window alpha is floored so it stays a readable solid panel. ----
        public static Color Scrim       => M.Following ? new Color(0.02f, 0.02f, 0.02f, 0.50f) : M.Ov("Scrim");
        public static Color Window      => M.Following ? new Color(HFill.r, HFill.g, HFill.b, Mathf.Max(HFill.a, 0.85f)) : M.Ov("Window");
        public static Color Bar         => M.Following ? Lift(HFill, 0.05f, 0.95f) : M.Ov("Bar");
        public static Color Panel       => M.Following ? Lift(HFill, 0.06f, 0.92f) : M.Ov("Panel");
        public static Color PanelRaised => M.Following ? Lift(HFill, 0.13f, 0.98f) : M.Ov("PanelRaised");
        public static Color PanelHover  => M.Following ? Lift(HFill, 0.20f, 1.00f) : M.Ov("PanelHover");
        public static Color Track       => M.Following ? Lift(HFill, 0.24f, 1.00f) : M.Ov("Track");
        public static Color Divider     => M.Following ? WithA(Text, 0.08f) : M.Ov("Divider");

        // ---- structural border: the HUD box border colour, so the window/button glass edges
        // actually match a HUD box's edge (not the line accent, which is a different palette
        // entry on any theme that splits the two — see Pure HUD). ----
        public static Color Border      => M.Following ? Opaque(HBorder) : M.Ov("Border");

        // ---- accents: the HUD line accent (cyan) + active-hand accent (orange) ----
        public static Color Accent      => M.Following ? Opaque(HAccent) : M.Ov("Accent");
        public static Color AccentDim   => WithA(Accent, 0.35f);
        public static Color Selected    => M.Following ? Opaque(HSelected) : M.Ov("Selected");
        public static Color SelectedDim => WithA(Selected, 0.30f);
        public static Color On          => M.Following ? Opaque(HGood) : M.Ov("On");
        public static Color Off         => M.Following ? Lift(HFill, 0.16f, 1.00f) : M.Ov("Off");

        // ---- text: the HUD text palette, but if the (rare) theme fill is LIGHT, swap to dark ink
        // so a pale theme stays readable instead of white-on-white (the light-theme case the
        // adversarial review flagged) — a no-op on any normal dark theme, so the match holds. ----
        public static Color Text     => M.Following ? (LightSurface ? Ink(0.10f) : Opaque(HText))  : M.Ov("Text");
        public static Color TextDim  => M.Following ? (LightSurface ? Ink(0.28f) : Opaque(HLabel)) : M.Ov("TextDim");
        public static Color TextMute => M.Following ? (LightSurface ? Ink(0.42f) : Opaque(HDim))   : M.Ov("TextMute");

        // The window surface is light enough that near-white text would wash out.
        private static bool LightSurface
        {
            get
            {
                Color w = new Color(HFill.r, HFill.g, HFill.b, 1f);
                return (0.299f * w.r + 0.587f * w.g + 0.114f * w.b) > 0.60f;
            }
        }
        public static Color Good     => M.Following ? Opaque(HGood)  : M.Ov("Good");
        public static Color Warn     => M.Following ? Opaque(HWarn)  : M.Ov("Warn");
        public static Color Critical => M.Following ? Opaque(HCrit)  : M.Ov("Critical");

        // ---- derivation helpers (M = UiaMenuTheme, aliased above for the compact getter table) ----
        private static Color HFill => Pal(HudPalette.PanelFill, new Color(0.08f, 0.09f, 0.11f, 0.90f));
        private static Color HAccent => Pal(HudPalette.LineAccent, new Color(0.35f, 0.78f, 0.90f, 1f));
        private static Color HBorder => Pal(HudPalette.PanelBorder, new Color(0.18f, 0.48f, 0.58f, 0.67f));
        private static Color HSelected => Pal(HudPalette.ActiveHandAccent, new Color(1.00f, 0.55f, 0.16f, 1f));
        private static Color HText => Pal(HudPalette.TextValue, new Color(0.94f, 0.97f, 1.00f, 1f));
        private static Color HLabel => Pal(HudPalette.TextLabel, new Color(0.72f, 0.80f, 0.87f, 1f));
        private static Color HDim => Pal(HudPalette.TextDim, new Color(0.52f, 0.60f, 0.68f, 1f));
        private static Color HGood => Pal(HudPalette.Good, new Color(0.42f, 0.85f, 0.52f, 1f));
        private static Color HWarn => Pal(HudPalette.Warn, new Color(0.98f, 0.78f, 0.30f, 1f));
        private static Color HCrit => Pal(HudPalette.Critical, new Color(0.95f, 0.40f, 0.38f, 1f));

        private static Color Pal(HudPalette.Entry e, Color fallback) => e != null ? e.Value : fallback;
        private static Color Lift(Color c, float t, float a)
            => new Color(Mathf.Lerp(c.r, 1f, t), Mathf.Lerp(c.g, 1f, t), Mathf.Lerp(c.b, 1f, t), a);
        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);
        private static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);
        private static Color Ink(float v) => new Color(v, v, v, 1f); // neutral dark text on a light theme

        // ---- metrics (reference px at 1080p; the canvas scales) ----
        public const float Pad       = 14f;
        public const float Gap       = 8f;
        public const float RowH      = 30f;
        public const float TabH      = 40f;
        public const float TitleH    = 46f;
        public const float LabelSize = 15f;
        public const float TitleSize = 22f;
        public const float SmallSize = 13f;
        public const float BigSize   = 28f;
        public const float Corner    = 10f;  // rounded — the kit-wide 9-slice sprite radius (UiaImages.Rounded)

        private static TMP_FontAsset _font;

        /// <summary>A stable, readable font for the menu — deliberately independent of the active
        /// HUD/radial font so a profile shipping a decorative face can't render the menu unusable.</summary>
        public static TMP_FontAsset Font()
        {
            if (_font != null) return _font;
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null && all.Length > 0) _font = all[0];
            }
            return _font;
        }

        public static void Shutdown() { _font = null; }
    }
}
