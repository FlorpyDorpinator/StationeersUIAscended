using ImGuiNET;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.Overlay
{
    /// <summary>
    /// Stationeers UI Ascended visual language, from the proposal's Figure 1: dark translucent teal glass
    /// panels, cyan primary text, orange accents, high contrast for dark environments.
    ///
    /// Wave C (0.9.2.5): every member below now RESOLVES LIVE from <see cref="HudPalette"/> /
    /// <see cref="RadialPalette"/> instead of being a compile-time constant, so Toast, the item
    /// radial's stow wedge and friends retint with the active profile's theme (pal:/rad: already
    /// travel with a profile, so this follows a profile switch for free). Names and types are
    /// UNCHANGED — every call site still reads `Theme.Accent` etc. and gets a `uint`/`Color`. The
    /// original constants are kept PRIVATE as the fail-soft fallback for when a palette entry is
    /// null (before Bind(), or mid F6-reload window) and as the record of the shipped look.
    /// </summary>
    public static class Theme
    {
        public static uint C(float r, float g, float b, float a) =>
            ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

        /// <summary>Resolve a HudPalette entry to an ImGui-packed ABGR colour; falls back when the
        /// entry is null (palette not yet Bind()'d, or a stale reference mid F6-reload).</summary>
        private static uint U(HudPalette.Entry e, uint fallback)
        {
            if (e == null) return fallback;
            Color c = e.Value;
            return ImGui.ColorConvertFloat4ToU32(new Vector4(c.r, c.g, c.b, c.a));
        }

        /// <summary>Same as the HudPalette overload, for RadialPalette entries.</summary>
        private static uint U(RadialPalette.Entry e, uint fallback)
        {
            if (e == null) return fallback;
            Color c = e.Value;
            return ImGui.ColorConvertFloat4ToU32(new Vector4(c.r, c.g, c.b, c.a));
        }

        /// <summary>Re-pack an already-resolved colour with a different alpha — used for the
        /// PanelBgSolid / AccentDim "derived" forms so they keep tracking the base colour's RGB.</summary>
        private static uint WithAlpha(uint packed, float alpha)
        {
            Vector4 v = ImGui.ColorConvertU32ToFloat4(packed);
            v.w = alpha;
            return ImGui.ColorConvertFloat4ToU32(v);
        }

        // ---------- shipped-look fallbacks (private; used only when the palette entry is null) ----------

        // Panels
        private static readonly uint _cPanelBg      = C(0.03f, 0.11f, 0.13f, 0.62f);
        private static readonly uint _cPanelBorder  = C(0.28f, 0.75f, 0.80f, 0.55f);

        // Text — no greys: secondary text is dimmer CYAN so everything stays on-palette.
        private static readonly uint _cTextPrimary  = C(0.78f, 0.96f, 0.98f, 1.00f);
        private static readonly uint _cTextDim      = C(0.55f, 0.88f, 0.92f, 1.00f);
        private static readonly uint _cTextDisabled = C(0.42f, 0.72f, 0.78f, 0.80f);

        // Accents
        private static readonly uint _cAccent       = C(1.00f, 0.55f, 0.16f, 1.00f);
        private static readonly uint _cGood         = C(0.35f, 0.90f, 0.55f, 1.00f);
        private static readonly uint _cWarn         = C(1.00f, 0.75f, 0.20f, 1.00f);
        private static readonly uint _cCritical     = C(1.00f, 0.25f, 0.20f, 1.00f);

        // Radial
        private static readonly uint _cRingBg       = C(0.03f, 0.11f, 0.13f, 0.55f);
        private static readonly uint _cRingHover    = C(0.10f, 0.42f, 0.46f, 0.80f);
        private static readonly uint _cRingHoverRim = C(1.00f, 0.55f, 0.16f, 0.95f);
        private static readonly uint _cRingSep      = C(0.28f, 0.75f, 0.80f, 0.35f);
        private static readonly uint _cRingDisabled = C(0.10f, 0.14f, 0.15f, 0.45f);
        // Stow slices: the whole wedge reads orange so "put the held item HERE" is unmistakable.
        private static readonly uint _cRingStow      = C(0.85f, 0.45f, 0.10f, 0.50f);
        private static readonly uint _cRingStowHover = C(1.00f, 0.55f, 0.16f, 0.85f);
        // Hub (dead zone) backing: dark enough that the center readout is always readable.
        private static readonly uint _cHubBg         = C(0.04f, 0.07f, 0.08f, 0.80f);

        // =====================================================
        // Modern UGUI Radial Theme (revised) fallbacks
        // =====================================================
        private static readonly Color _cUguiBg = new Color(0.078f, 0.078f, 0.078f, 0.76f); // #141414
        private static readonly Color _cUguiSelectedBlue       = new Color(0.16f, 0.38f, 0.52f, 0.84f);
        private static readonly Color _cUguiSelectedBlueBright = new Color(0.22f, 0.50f, 0.65f, 0.92f);
        private static readonly Color _cUguiOrange       = new Color(0.988f, 0.455f, 0.016f, 0.82f);
        private static readonly Color _cUguiOrangeBright = new Color(1.00f, 0.58f, 0.12f, 0.96f);
        private static readonly Color _cUguiDisabled = new Color(0.12f, 0.12f, 0.12f, 0.50f);
        private static readonly Color _cUguiShine = new Color(1f, 1f, 1f, 0.15f);

        // ---------- live properties (palette-driven; same names/types as before) ----------

        // Panels
        public static uint PanelBg      => U(HudPalette.PanelFill,   _cPanelBg);
        public static uint PanelBgSolid => WithAlpha(PanelBg, 0.88f);
        public static uint PanelBorder  => U(HudPalette.PanelBorder, _cPanelBorder);

        // Text — no greys: secondary text is dimmer CYAN so everything stays on-palette.
        public static uint TextPrimary  => U(HudPalette.TextValue, _cTextPrimary);
        public static uint TextDim      => U(HudPalette.TextLabel, _cTextDim);
        public static uint TextDisabled => U(HudPalette.TextDim,   _cTextDisabled);

        // Accents
        public static uint Accent       => U(RadialPalette.TextAccent, _cAccent);
        public static uint AccentDim    => WithAlpha(Accent, 0.45f);
        public static uint Good         => U(HudPalette.Good,     _cGood);
        public static uint Warn         => U(HudPalette.Warn,     _cWarn);
        public static uint Critical     => U(HudPalette.Critical, _cCritical);

        // Radial
        public static uint RingBg       => U(RadialPalette.WedgeBg,          _cRingBg);
        public static uint RingHover    => U(RadialPalette.WedgeHover,       _cRingHover);
        public static uint RingHoverRim => U(RadialPalette.WedgeBorderHover, _cRingHoverRim);
        public static uint RingSep      => U(RadialPalette.WedgeBorder,      _cRingSep);
        public static uint RingDisabled => U(RadialPalette.WedgeDisabled,    _cRingDisabled);
        // Stow slices: the whole wedge reads orange so "put the held item HERE" is unmistakable.
        public static uint RingStow      => U(RadialPalette.WedgeStow,      _cRingStow);
        public static uint RingStowHover => U(RadialPalette.WedgeStowHover, _cRingStowHover);
        // Hub (dead zone) backing: dark enough that the center readout is always readable.
        public static uint HubBg         => U(RadialPalette.HubFill, _cHubBg);

        public static uint StateColor(float ratio01)
        {
            if (ratio01 <= 0.15f) return Critical;
            if (ratio01 <= 0.40f) return Warn;
            return Good;
        }

        // =====================================================
        // Modern UGUI Radial Theme (revised)
        // - Backgrounds dynamically shade when a wedge is highlighted (others recede)
        // - Orange for borders + rim highlights (powerful, works great)
        // - Richer darker blue for selected/hovered state (pops via contrast, not too bright as bg)
        // - #141414 bases, good transparency
        // =====================================================

        public static Color UguiBg
        {
            get { return RadialPalette.WedgeBg != null ? RadialPalette.WedgeBg.Value : _cUguiBg; }
        }

        // Blue used on selected/hovered wedges - richer saturation + tuned brightness so it pops more as the selected state (not too bright for backgrounds)
        public static Color UguiSelectedBlue
        {
            get { return RadialPalette.WedgeHover != null ? RadialPalette.WedgeHover.Value : _cUguiSelectedBlue; }
        }

        public static Color UguiSelectedBlueBright
        {
            get { return RadialPalette.WedgeBorderHover != null ? RadialPalette.WedgeBorderHover.Value : _cUguiSelectedBlueBright; }
        }

        // Orange for borders, highlights/rim shine, and stow fills
        public static Color UguiOrange
        {
            get { return RadialPalette.WedgeBorder != null ? RadialPalette.WedgeBorder.Value : _cUguiOrange; }
        }

        public static Color UguiOrangeBright
        {
            get { return RadialPalette.WedgeBorderHover != null ? RadialPalette.WedgeBorderHover.Value : _cUguiOrangeBright; }
        }

        public static Color UguiDisabled
        {
            get { return RadialPalette.WedgeDisabled != null ? RadialPalette.WedgeDisabled.Value : _cUguiDisabled; }
        }

        // Subtle outer rim shine (mixed into outer verts)
        public static Color UguiShine
        {
            get { return RadialPalette.RimShine != null ? RadialPalette.RimShine.Value : _cUguiShine; }
        }
    }
}
