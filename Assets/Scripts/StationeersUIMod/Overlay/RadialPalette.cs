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

            public Entry(ConfigFile cfg, string name, string defaultHex, string description)
            {
                Name = name;
                Config = cfg.Bind("9. Radial Colours", name, defaultHex, description);
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

            // Defaults are FlorpyDorp's hand-tuned palette (picked in-game 2026-07-09):
            // deep navy glass, orange reserved for borders and whatever is selected.
            WedgeBg = Add(cfg, "WedgeBackground", "0E213373",
                "Unselected wedge fill. Deep navy, translucent.");
            WedgeHover = Add(cfg, "WedgeSelected", "0F296867",
                "Selected/hovered wedge fill. Lighter blue.");
            WedgeDisabled = Add(cfg, "WedgeDisabled", "10305366",
                "Wedge that cannot be chosen.");
            WedgeStow = Add(cfg, "WedgeStowTarget", "D9731A6B",
                "Wedge meaning 'put the held item here'.");
            WedgeStowHover = Add(cfg, "WedgeStowTargetSelected", "FF8C29AF",
                "Selected stow-target wedge.");

            WedgeBorder = Add(cfg, "WedgeBorder", "FF6900C9",
                "Thin border on every wedge.");
            WedgeBorderHover = Add(cfg, "WedgeBorderSelected", "FF8C299C",
                "Border around the SELECTED wedge.");
            RimShine = Add(cfg, "RimShine", "FFFFFF1A",
                "Subtle gloss blended into the outer rim.");

            HubFill = Add(cfg, "HubFill", "D91A3400",
                "Centre circle background (alpha 0 = invisible by default).");
            HubBorder = Add(cfg, "HubBorder", "FF8C29AE",
                "Centre circle rim.");

            TextPrimary = Add(cfg, "TextPrimary", "FFFFFFFF",
                "Main readout text.");
            TextDim = Add(cfg, "TextDim", "FFFFFFFF",
                "Secondary readout text.");
            TextAccent = Add(cfg, "TextAccent", "FFFFFFFF",
                "The action verb in the hub.");
        }

        private static Entry Add(ConfigFile cfg, string name, string defaultHex, string desc)
        {
            var e = new Entry(cfg, name, defaultHex, desc);
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
