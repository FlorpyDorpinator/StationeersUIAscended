using ImGuiNET;
using UnityEngine;

namespace StationeersUIMod.Overlay
{
    /// <summary>
    /// Stationeers UI Ascended visual language, from the proposal's Figure 1: dark translucent teal glass
    /// panels, cyan primary text, orange accents, high contrast for dark environments.
    /// </summary>
    public static class Theme
    {
        public static uint C(float r, float g, float b, float a) =>
            ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

        // Panels
        public static readonly uint PanelBg      = C(0.03f, 0.11f, 0.13f, 0.62f);
        public static readonly uint PanelBgSolid = C(0.03f, 0.11f, 0.13f, 0.88f);
        public static readonly uint PanelBorder  = C(0.28f, 0.75f, 0.80f, 0.55f);

        // Text — no greys: secondary text is dimmer CYAN so everything stays on-palette.
        public static readonly uint TextPrimary  = C(0.78f, 0.96f, 0.98f, 1.00f);
        public static readonly uint TextDim      = C(0.55f, 0.88f, 0.92f, 1.00f);
        public static readonly uint TextDisabled = C(0.42f, 0.72f, 0.78f, 0.80f);

        // Accents
        public static readonly uint Accent       = C(1.00f, 0.55f, 0.16f, 1.00f);
        public static readonly uint AccentDim    = C(1.00f, 0.55f, 0.16f, 0.45f);
        public static readonly uint Good         = C(0.35f, 0.90f, 0.55f, 1.00f);
        public static readonly uint Warn         = C(1.00f, 0.75f, 0.20f, 1.00f);
        public static readonly uint Critical     = C(1.00f, 0.25f, 0.20f, 1.00f);

        // Radial
        public static readonly uint RingBg       = C(0.03f, 0.11f, 0.13f, 0.55f);
        public static readonly uint RingHover    = C(0.10f, 0.42f, 0.46f, 0.80f);
        public static readonly uint RingHoverRim = C(1.00f, 0.55f, 0.16f, 0.95f);
        public static readonly uint RingSep      = C(0.28f, 0.75f, 0.80f, 0.35f);
        public static readonly uint RingDisabled = C(0.10f, 0.14f, 0.15f, 0.45f);
        // Stow slices: the whole wedge reads orange so "put the held item HERE" is unmistakable.
        public static readonly uint RingStow      = C(0.85f, 0.45f, 0.10f, 0.50f);
        public static readonly uint RingStowHover = C(1.00f, 0.55f, 0.16f, 0.85f);
        // Hub (dead zone) backing: dark enough that the center readout is always readable.
        public static readonly uint HubBg         = C(0.04f, 0.07f, 0.08f, 0.80f);

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

        public static readonly Color UguiBg = new Color(0.078f, 0.078f, 0.078f, 0.76f); // #141414

        // Blue used on selected/hovered wedges - richer saturation + tuned brightness so it pops more as the selected state (not too bright for backgrounds)
        public static readonly Color UguiSelectedBlue      = new Color(0.16f, 0.38f, 0.52f, 0.84f);
        public static readonly Color UguiSelectedBlueBright = new Color(0.22f, 0.50f, 0.65f, 0.92f);

        // Orange for borders, highlights/rim shine, and stow fills
        public static readonly Color UguiOrange       = new Color(0.988f, 0.455f, 0.016f, 0.82f);
        public static readonly Color UguiOrangeBright = new Color(1.00f, 0.58f, 0.12f, 0.96f);

        public static readonly Color UguiDisabled = new Color(0.12f, 0.12f, 0.12f, 0.50f);

        // Subtle outer rim shine (mixed into outer verts)
        public static readonly Color UguiShine = new Color(1f, 1f, 1f, 0.15f);
    }
}

