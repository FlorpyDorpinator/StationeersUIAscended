using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// The one visual language for the UGUI Control Center: a flat, high-contrast sci-fi
    /// terminal look that sits comfortably next to the visor HUD (cool glass, cyan accents,
    /// orange for the current selection — the same accent logic the radial uses). Colours and
    /// metrics live here so every control reads from one place and the menu stays cohesive.
    /// </summary>
    public static class UiaTheme
    {
        // ---- surfaces (frosted grey glass — play-test ask: "grey background with a slight
        // frosted glass effect"; the window is translucent so the world greys through it) ----
        public static readonly Color Scrim       = new Color(0.02f, 0.02f, 0.02f, 0.50f); // full-screen dim behind the window
        public static readonly Color Window      = new Color(0.16f, 0.17f, 0.18f, 0.90f);
        public static readonly Color Bar         = new Color(0.20f, 0.21f, 0.22f, 0.95f); // title / tab strip
        public static readonly Color Panel       = new Color(0.21f, 0.22f, 0.23f, 0.92f);
        public static readonly Color PanelRaised  = new Color(0.27f, 0.28f, 0.29f, 0.98f);
        public static readonly Color PanelHover   = new Color(0.33f, 0.34f, 0.36f, 1.00f);
        public static readonly Color Track        = new Color(0.36f, 0.37f, 0.39f, 1.00f); // slider/toggle track
        public static readonly Color Divider      = new Color(1f, 1f, 1f, 0.08f);

        // ---- accents ----
        public static readonly Color Accent      = new Color(0.35f, 0.78f, 0.90f, 1.00f); // cyan — primary
        public static readonly Color AccentDim   = new Color(0.35f, 0.78f, 0.90f, 0.35f);
        public static readonly Color Selected    = new Color(1.00f, 0.55f, 0.16f, 1.00f); // orange — active/selected
        public static readonly Color SelectedDim = new Color(1.00f, 0.55f, 0.16f, 0.30f);
        public static readonly Color On          = new Color(0.36f, 0.82f, 0.48f, 1.00f); // toggle ON
        public static readonly Color Off          = new Color(0.35f, 0.40f, 0.45f, 1.00f); // toggle OFF

        // ---- text ----
        public static readonly Color Text     = new Color(0.94f, 0.97f, 1.00f, 1.00f);
        public static readonly Color TextDim  = new Color(0.72f, 0.80f, 0.87f, 1.00f);
        public static readonly Color TextMute = new Color(0.52f, 0.60f, 0.68f, 1.00f);
        public static readonly Color Good     = new Color(0.42f, 0.85f, 0.52f, 1.00f);
        public static readonly Color Warn     = new Color(0.98f, 0.78f, 0.30f, 1.00f);
        public static readonly Color Critical = new Color(0.95f, 0.40f, 0.38f, 1.00f);

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
