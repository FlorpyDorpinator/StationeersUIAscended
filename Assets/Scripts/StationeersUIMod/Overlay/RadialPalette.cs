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
        /// <summary>The stored value meaning "follow the theme": an entry bound with an inheritance
        /// rule resolves it LIVE from other palette entries. ASCII, case-insensitive.</summary>
        public const string Auto = "AUTO";

        public sealed class Entry
        {
            public readonly string Name;
            public readonly ConfigEntry<string> Config;
            private Color _cached;
            private string _cachedFrom;
            // Non-null only for theme-following entries (bound with a default of AUTO): how the
            // colour is derived from the rest of the palette while the stored value is AUTO.
            private readonly Func<Color> _inherit;

            public Entry(ConfigFile cfg, string name, string defaultHex, string description)
                : this(cfg, name, defaultHex, description, null) { }

            public Entry(ConfigFile cfg, string name, string defaultHex, string description, Func<Color> inherit)
            {
                Name = name;
                Config = cfg.Bind("9. Radial Colours", name, defaultHex, description);
                _cachedFrom = null;
                _inherit = inherit;
            }

            /// <summary>True while this entry follows the theme (stored value AUTO). Editing the
            /// colour writes a hex and pins it; resetting colours to defaults restores AUTO.</summary>
            public bool IsAuto => _inherit != null && IsAutoValue(Config.Value);

            /// <summary>Can this entry follow the theme at all (bound with an AUTO inheritance rule)?
            /// True for exactly the entries whose <see cref="DefaultValue"/> is <see cref="Auto"/>.</summary>
            public bool FollowsTheme => _inherit != null;

            /// <summary>The value this entry had out of the box, as stored: <see cref="Auto"/> for the
            /// theme-following entries, the hand-tuned "RRGGBBAA" hex for the rest. What "reset to
            /// defaults" writes — and what a theme that predates the entry means for it (B2,
            /// <c>HudTheme.Apply</c>).</summary>
            public string DefaultValue => (string)Config.DefaultValue;

            public Color Value
            {
                get
                {
                    if (_inherit != null && IsAutoValue(Config.Value))
                    {
                        try { return _inherit(); }
                        catch { return Color.magenta; } // obviously wrong beats silently black
                    }
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

        private static bool IsAutoValue(string v)
            => v != null && v.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase);

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

        // Sorting-class GROUP wedges (crowded bags grouped by category: Storage, Power Cells…)
        public static Entry GroupWedgeFill;
        public static Entry GroupWedgeBorder;

        // DEVICE-SLOT wedges: an item that is installed in a device's functional slot (the
        // propellant canister in a jetpack, a battery in a tool) rather than just stored.
        public static Entry DeviceSlotBorderColor;

        // Hub (the circle in the middle)
        public static Entry HubFill;
        public static Entry HubBorder;
        public static Entry HubCloseFill;
        public static Entry HubCloseHover;
        public static Entry HubCloseText;

        // Text
        public static Entry TextPrimary;
        public static Entry TextDim;
        public static Entry TextAccent;
        public static Entry TextDisabled;
        public static Entry TextBinding;

        // Hint bar (the contextual key-hint strip under an open wheel)
        public static Entry HintBarFill;
        public static Entry HintBarBorder;
        public static Entry HintBarText;

        // Curved labels hugging the wheel from OUTSIDE (D-021 key hints under it, D-022 action word
        // over it). All four default to AUTO — derived live from the theme's own wedge colours, so
        // every theme (shipped or custom) gets matching curved labels without being re-stamped.
        public static Entry ArcPlateFill;
        public static Entry ArcPlateBorder;
        public static Entry ArcText;
        public static Entry ArcAccent;

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

            // Category/sorting-class group wedges (a crowded bag grouped into Storage / Power
            // Cells / … wedges). The EDGE reads a distinct lighter blue so grouping is obvious;
            // the FILL defaults to the normal wedge background (its own colour so it's tunable).
            GroupWedgeFill = Add(cfg, "GroupWedgeFill", "0E213373",
                "Fill of a sorting-class GROUP wedge (crowded bags grouped by category). Defaults to the normal wedge fill.");
            GroupWedgeBorder = Add(cfg, "GroupWedgeBorder", "5AA0F0E6",
                "Edge of a sorting-class GROUP wedge. A lighter blue so category wedges stand out.");

            // An item installed in a device's functional slot (jetpack propellant, tool battery)
            // rather than merely stored — its wedge wears this edge so 'in use by the device' reads
            // at a glance. Default blue.
            DeviceSlotBorderColor = Add(cfg, "DeviceSlotBorderColor", "3D7BE6FF",
                "Edge of a wedge for an item installed in a DEVICE slot (propellant canister, battery, filter). Default blue.");

            HubFill = Add(cfg, "HubFill", "D91A3400",
                "Centre circle background (alpha 0 = invisible by default).");
            HubBorder = Add(cfg, "HubBorder", "FF8C29AE",
                "Centre circle rim.");
            HubCloseFill = Add(cfg, "HubCloseButton", "0E213380",
                "The CLOSE button band at the bottom of the hub.");
            HubCloseHover = Add(cfg, "HubCloseButtonHover", "FF6900A0",
                "CLOSE button while the mouse is on it.");
            HubCloseText = Add(cfg, "HubCloseText", "FFFFFFE6",
                "The word CLOSE on the hub button.");

            TextPrimary = Add(cfg, "TextPrimary", "FFFFFFFF",
                "Main readout text.");
            TextDim = Add(cfg, "TextDim", "FFFFFFFF",
                "Secondary readout text.");
            // Default changed WHITE -> the legacy accent ORANGE in 0.9.2.5 (Wave C): this entry now
            // also backs Theme.Accent (the stow/equip wedge-label highlight that used to be a
            // hardcoded constant), so white would have silently killed the orange accents. Existing
            // players who never touched it are moved to the new default by ConfigMigration v2->v3.
            TextAccent = Add(cfg, "TextAccent", "FF8C29FF",
                "Accent colour: the action verb in the hub + highlighted wedge labels (stow/equip).");
            TextDisabled = Add(cfg, "TextDisabled", "7FA6BBAA",
                "Labels on DISABLED wedges (was tied to the wedge fill — now its own colour).");
            // The "this tool belongs in this slot" name on a toolbelt wedge (stable geometry). It
            // used to borrow TextDim, whose default is plain WHITE — so the binding name read as
            // loud as a real readout. Its own entry, default GREY, so it sits back as a hint.
            TextBinding = Add(cfg, "TextBindingLabel", "9FA6ADFF",
                "The bound-tool name on a toolbelt wedge (which tool belongs in that slot). Default grey.");

            // The key-hint strip under an open wheel. Its colours live HERE rather than as hand-made
            // widgets in the F10 tab so they inherit the colour wheels, undo/redo, snapshot and
            // reset-to-defaults that every other radial colour already has.
            HintBarFill = Add(cfg, "HintBarFill", "000000E6",
                "Hint bar background. Default black at alpha 230.");
            HintBarBorder = Add(cfg, "HintBarBorder", "00000000",
                "Hint bar rim. Default fully transparent (no border) — raise the alpha to get one.");
            HintBarText = Add(cfg, "HintBarText", "FFFFFFFF",
                "Hint bar text. Default white.");

            // D-021 / D-022 (2026-09-25): the curved labels that hug the wheel from outside — the key
            // hints wrapped under it and the action word ("TAKE", "OPEN") over it. They default to
            // AUTO so they wear whatever theme is active: a theme stamps rad:* values for the entries
            // it knows, and these new ones stay AUTO until someone picks a colour, so every existing
            // theme gets matching labels with no re-stamp and no migration. A colour pinned under one
            // theme never bleeds into another: applying a theme that predates these entries (no
            // "rad:Arc*" key, e.g. every shipped theme) puts them back to AUTO (B2 — HudTheme.Apply's
            // missing-key rule). Shared radial colours (the radial is not a per-tier HUD element) —
            // they travel with the profile theme as "rad:<Name>" like every other entry here.
            ArcPlateFill = AddAuto(cfg, "ArcPlateFill", ArcPlateFillAuto,
                "Glass plate behind the curved hint text and action word. AUTO = the theme's wedge fill, a little more opaque so text reads over the world.");
            ArcPlateBorder = AddAuto(cfg, "ArcPlateBorder", ArcPlateBorderAuto,
                "Rim of the curved plates. AUTO = the theme's wedge border, slightly softened. Alpha 0 = no rim.");
            ArcText = AddAuto(cfg, "ArcText", () => TextPrimary.Value,
                "Words on the curved hint plate (what each key does). AUTO = the theme's primary text colour.");
            ArcAccent = AddAuto(cfg, "ArcAccent", ArcAccentAuto,
                "The curved action word over the wheel and the key names in the hints. AUTO = the theme's wedge border colour, fully opaque (falls back to the primary text colour when that border is too dark to read).");
        }

        private static Entry Add(ConfigFile cfg, string name, string defaultHex, string desc)
        {
            var e = new Entry(cfg, name, defaultHex, desc);
            All.Add(e);
            return e;
        }

        /// <summary>A theme-following entry: stored default is <see cref="Auto"/>, resolved live by
        /// <paramref name="inherit"/> until the player pins a colour.</summary>
        private static Entry AddAuto(ConfigFile cfg, string name, Func<Color> inherit, string desc)
        {
            var e = new Entry(cfg, name, Auto, desc + " (Type AUTO to follow the theme again.)", inherit);
            All.Add(e);
            return e;
        }

        // ---- AUTO rules (read the live palette; never write it) ----

        private static Color ArcPlateFillAuto()
        {
            Color c = WedgeBg.Value;
            c.a = Mathf.Clamp(c.a * 1.7f, 0.55f, 0.9f);
            return c;
        }

        private static Color ArcPlateBorderAuto()
        {
            Color c = WedgeBorder.Value;
            c.a *= 0.75f;
            return c;
        }

        private static Color ArcAccentAuto()
        {
            Color c = WedgeBorder.Value;
            c.a = 1f;
            // A theme with a near-black border would put dark words on a dark plate: read the
            // primary text colour instead (perceived luminance, sRGB weights — a legibility floor,
            // not colour science).
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return lum < 0.2f ? TextPrimary.Value : c;
        }

        public static void ResetToDefaults()
        {
            History.PushUndo(Snapshot());
            foreach (var e in All)
                e.Config.Value = e.DefaultValue;
        }

        public static Dictionary<string, string> Snapshot()
        {
            var s = new Dictionary<string, string>(All.Count);
            foreach (var e in All) s[e.Name] = e.Config.Value;
            return s;
        }

        public static void Apply(Dictionary<string, string> snapshot)
        {
            if (snapshot == null) return;
            foreach (var e in All)
            {
                string hex;
                if (snapshot.TryGetValue(e.Name, out hex)) e.Config.Value = hex;
            }
        }

        /// <summary>
        /// Undo/redo for the colour editor. The editor captures a snapshot when a colour
        /// widget is picked up and commits it when the edit ends, so one drag across the
        /// hue wheel is ONE undo step, not five hundred.
        /// </summary>
        public static class History
        {
            private const int Cap = 50;
            private static readonly List<Dictionary<string, string>> _undo = new List<Dictionary<string, string>>();
            private static readonly List<Dictionary<string, string>> _redo = new List<Dictionary<string, string>>();

            public static bool CanUndo => _undo.Count > 0;
            public static bool CanRedo => _redo.Count > 0;

            public static void PushUndo(Dictionary<string, string> preEditState)
            {
                if (preEditState == null) return;
                _undo.Add(preEditState);
                if (_undo.Count > Cap) _undo.RemoveAt(0);
                _redo.Clear();
            }

            public static void Undo()
            {
                if (_undo.Count == 0) return;
                var state = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);
                _redo.Add(Snapshot());
                Apply(state);
            }

            public static void Redo()
            {
                if (_redo.Count == 0) return;
                var state = _redo[_redo.Count - 1];
                _redo.RemoveAt(_redo.Count - 1);
                _undo.Add(Snapshot());
                Apply(state);
            }
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
