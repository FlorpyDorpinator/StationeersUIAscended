using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.Overlay
{
    /// <summary>
    /// Every colour the radial can draw, live-editable from the F10 settings window and
    /// persisted to the BepInEx config as "RRGGBBAA" hex.
    ///
    /// Colours live here rather than as consts in Theme so the F10 colour wheels can drive
    /// them at runtime. Theme keeps the ImGui-packed uints for the ImGui renderer and the HUD.
    /// </summary>
    public static class RadialPalette
    {
        public sealed class Entry
        {
            public readonly string Name;
            public readonly ConfigEntry<string> Config;
            private Color _cached;
            private string _cachedFrom;

            public Entry(ConfigFile cfg, string name, Color fallback, string description)
            {
                Name = name;
                Config = cfg.Bind("9. Radial Colours", name, ToHex(fallback), description);
                _cachedFrom = null;
            }

            public Color Value
            {
                get
                {
                    if (!ReferenceEquals(_cachedFrom, Config.Value))
                    {
                        _cached = FromHex(Config.Value);
                        _cachedFrom = Config.Value;
                    }
                    return _cached;
                }
                set => Config.Value = ToHex(value);
            }
        }

        public static readonly List<Entry> All = new List<Entry>();

        // Wedges
        public static Entry WedgeBg;
        public static Entry WedgeHover;
        public static Entry WedgeDisabled;
        public static Entry WedgeStow;
        public static Entry WedgeStowHover;

        // Borders / accents
        public static Entry WedgeBorder;
        public static Entry WedgeBorderHover;
        public static Entry RimShine;

        // Hub (the circle in the middle)
        public static Entry HubFill;
        public static Entry HubBorder;

        // Text
        public static Entry TextPrimary;
        public static Entry TextDim;
        public static Entry TextAccent;

        public static void Bind(ConfigFile cfg)
        {
            All.Clear();

            // Stationeers-ish palette: cool blue glass, orange only for what is SELECTED.
            WedgeBg = Add(cfg, "WedgeBackground", new Color(0.055f, 0.13f, 0.20f, 0.45f),
                "Unselected wedge fill. Darker blue, high transparency.");
            WedgeHover = Add(cfg, "WedgeSelected", new Color(0.30f, 0.62f, 0.80f, 0.72f),
                "Selected/hovered wedge fill. Lighter blue.");
            WedgeDisabled = Add(cfg, "WedgeDisabled", new Color(0.10f, 0.12f, 0.14f, 0.40f),
                "Wedge that cannot be chosen.");
            WedgeStow = Add(cfg, "WedgeStowTarget", new Color(0.85f, 0.45f, 0.10f, 0.42f),
                "Wedge meaning 'put the held item here'.");
            WedgeStowHover = Add(cfg, "WedgeStowTargetSelected", new Color(1.00f, 0.55f, 0.16f, 0.80f),
                "Selected stow-target wedge.");

            WedgeBorder = Add(cfg, "WedgeBorder", new Color(0.35f, 0.72f, 0.85f, 0.28f),
                "Thin border on every wedge.");
            WedgeBorderHover = Add(cfg, "WedgeBorderSelected", new Color(1.00f, 0.55f, 0.16f, 1.00f),
                "Border around the SELECTED wedge (orange).");
            RimShine = Add(cfg, "RimShine", new Color(1f, 1f, 1f, 0.10f),
                "Subtle gloss blended into the outer rim.");

            HubFill = Add(cfg, "HubFill", new Color(0.85f, 0.42f, 0.10f, 0.55f),
                "Centre circle background (orange).");
            HubBorder = Add(cfg, "HubBorder", new Color(1.00f, 0.55f, 0.16f, 0.85f),
                "Centre circle rim.");

            TextPrimary = Add(cfg, "TextPrimary", new Color(0.82f, 0.96f, 1.00f, 1.00f),
                "Main readout text.");
            TextDim = Add(cfg, "TextDim", new Color(0.58f, 0.86f, 0.94f, 1.00f),
                "Secondary readout text.");
            TextAccent = Add(cfg, "TextAccent", new Color(1.00f, 0.60f, 0.20f, 1.00f),
                "The action verb in the hub.");
        }

        private static Entry Add(ConfigFile cfg, string name, Color fallback, string desc)
        {
            var e = new Entry(cfg, name, fallback, desc);
            All.Add(e);
            return e;
        }

        public static void ResetToDefaults()
        {
            foreach (var e in All)
                e.Config.Value = (string)e.Config.DefaultValue;
        }

        // ---------- hex <-> Color ----------

        public static string ToHex(Color c)
        {
            var c32 = (Color32)c;
            return $"{c32.r:X2}{c32.g:X2}{c32.b:X2}{c32.a:X2}";
        }

        public static Color FromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            hex = hex.TrimStart('#');
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                byte a = hex.Length >= 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
                return new Color32(r, g, b, a);
            }
            catch
            {
                return Color.magenta; // obviously wrong beats silently black
            }
        }
    }
}
