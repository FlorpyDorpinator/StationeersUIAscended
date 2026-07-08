using ImGuiNET;
using UnityEngine;

namespace StationeersUIAscended.Overlay
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

        // Text
        public static readonly uint TextPrimary  = C(0.72f, 0.93f, 0.95f, 1.00f);
        public static readonly uint TextDim      = C(0.48f, 0.66f, 0.68f, 0.85f);
        public static readonly uint TextDisabled = C(0.40f, 0.50f, 0.52f, 0.55f);

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

        public static uint StateColor(float ratio01)
        {
            if (ratio01 <= 0.15f) return Critical;
            if (ratio01 <= 0.40f) return Warn;
            return Good;
        }
    }
}
