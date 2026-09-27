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
        // ---- surfaces (combined visual round 2026-09-26): the concept's dark family is the
        // ACCENT HUE at low value (measured S 0.6-0.95) — one teal-black ladder per theme,
        // derived from that theme's own accent, replacing the old lift-from-PanelFill ramp
        // (which produced the warm grey-brown chrome the comparison flagged). Verified on the
        // shipped accent: Bar resolves #0C1A1E, Panel #011014-#011317 family, Window ~#010E12 —
        // each within a couple RGB counts of the concept samples. ----
        public static Color Scrim       => M.Following ? new Color(0.02f, 0.02f, 0.02f, 0.50f) : M.Ov("Scrim");
        public static Color Window      => M.Following ? WithA(AccentHsv(0.95f, 0.071f), 0.97f) : M.Ov("Window");
        public static Color Bar         => M.Following ? AccentHsv(0.60f, 0.118f) : M.Ov("Bar");
        public static Color Panel       => M.Following ? AccentHsv(0.95f, 0.08f) : M.Ov("Panel");
        public static Color PanelRaised => M.Following ? AccentHsv(0.60f, 0.118f) : M.Ov("PanelRaised");
        public static Color PanelHover  => M.Following ? AccentHsv(0.55f, 0.165f) : M.Ov("PanelHover");
        public static Color Track       => M.Following ? AccentHsv(0.50f, 0.20f) : M.Ov("Track");
        public static Color Divider     => M.Following ? WithA(Text, 0.08f) : M.Ov("Divider");

        // ---- structural border: the concept's uniform deep cyan (R=0 family, measured
        // #008195) — the accent at full saturation, mid value — replacing the pale-steel
        // HUD-border read the comparison flagged as washed and inconsistent. ----
        public static Color Border      => M.Following ? AccentHsv(1f, 0.585f) : M.Ov("Border");

        // ---- accents: the HUD line accent (cyan) + active-hand accent (orange) ----
        public static Color Accent      => M.Following ? Opaque(HAccent) : M.Ov("Accent");
        public static Color AccentDim   => WithA(Accent, 0.35f);
        public static Color Selected    => M.Following ? Opaque(HSelected) : M.Ov("Selected");
        public static Color SelectedDim => WithA(Selected, 0.30f);
        /// <summary>Section-header cyan (the concept's near-full-saturation #0AF4FA family) —
        /// deliberately brighter than <see cref="Accent"/>, which the comparison measured as
        /// too washed for headers. Derived from the accent hue in both modes.</summary>
        public static Color HeaderText  => AccentHsv(0.97f, 0.99f);
        public static Color On          => M.Following ? MintGreen(Opaque(HGood)) : M.Ov("On");
        public static Color Off         => M.Following ? AccentHsv(0.45f, 0.20f) : M.Ov("Off");

        // ---- text: values stay near-white; the SECONDARY text joins the accent family
        // (measured #8AD8E6-ish light teal — the old grey-slate read ~32% darker). The pale-
        // theme ink fallback stays, so a light theme never goes white-on-white. ----
        public static Color Text     => M.Following ? (LightSurface ? Ink(0.10f) : Opaque(HText))  : M.Ov("Text");
        public static Color TextDim  => M.Following ? (LightSurface ? Ink(0.28f) : AccentHsv(0.38f, 0.88f)) : M.Ov("TextDim");
        public static Color TextMute => M.Following ? (LightSurface ? Ink(0.42f) : AccentHsv(0.40f, 0.70f)) : M.Ov("TextMute");

        /// <summary>A colour at the theme ACCENT's hue with explicit saturation/value — the
        /// combined round's one derivation for the whole dark family (and its bright text
        /// cuts), so every theme tints its own ladder from its own accent.</summary>
        internal static Color AccentHsv(float s, float v)
        {
            float h, s0, v0;
            Color.RGBToHSV(Accent, out h, out s0, out v0);
            Color c = Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));
            c.a = 1f;
            return c;
        }

        /// <summary>The combined round's state green: a GREEN theme-good normalises to the
        /// concept's cool mint (#15ED47 family — blue channel ~70, not neon B=0); a theme
        /// whose good is not green keeps its own hue, capped off the neon ceiling.</summary>
        private static Color MintGreen(Color good)
        {
            float h, s, v;
            Color.RGBToHSV(good, out h, out s, out v);
            if (h > 0.25f && h < 0.42f) h = 0.372f;
            Color c = Color.HSVToRGB(h, Mathf.Min(s, 0.91f), Mathf.Min(v, 0.93f));
            c.a = 1f;
            return c;
        }

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
        // 0.9.8.0 visual language (FlorpyDorp: manila-folder chrome, "sharper corners, more
        // futuristic"): the kit-wide radius dropped 10 -> 4. UiaImages' 9-slice sprite bakes the
        // SAME radius — change them together or every Image corner disagrees with every
        // PanelGraphic corner.
        public const float Corner    = 4f;   // sharp-rounded — the kit-wide 9-slice sprite radius (UiaImages.Rounded)

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
