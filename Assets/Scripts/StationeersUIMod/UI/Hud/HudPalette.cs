using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Every colour the visor HUD can draw, live-editable from the F9 HUD editor and
    /// persisted to the BepInEx config as "RRGGBBAA" hex — the RadialPalette pattern,
    /// kept as its OWN palette so restyling the HUD never disturbs the radials.
    ///
    /// Defaults are tuned to the concept art: thin cyan line-work on dark glass.
    /// </summary>
    public static class HudPalette
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
                Config = cfg.Bind("11. HUD Colours", name, defaultHex, description);
                _cachedFrom = null;
            }

            public Color Value
            {
                get
                {
                    if (!ReferenceEquals(_cachedFrom, Config.Value))
                    {
                        _cached = Overlay.RadialPalette.FromHex(Config.Value);
                        _cachedFrom = Config.Value;
                    }
                    return _cached;
                }
                set => Config.Value = Overlay.RadialPalette.ToHex(value);
            }
        }

        public static readonly List<Entry> All = new List<Entry>();

        // Panels (the dark glass)
        public static Entry PanelFill;
        public static Entry PanelBorder;
        public static Entry LineAccent;

        // Text
        public static Entry TextLabel;
        public static Entry TextValue;
        public static Entry TextDim;

        // Semantic states
        public static Entry Good;
        public static Entry Warn;
        public static Entry Critical;

        // Compass
        public static Entry CompassTick;
        public static Entry CompassCardinal;
        public static Entry CompassNeedle;

        // Vitals / hologram
        public static Entry HologramTint;
        public static Entry BareWord;

        // Equipment / hands
        public static Entry SlotNumber;
        public static Entry ActiveHandAccent;
        public static Entry DropHighlight;

        // Screen dressing
        public static Entry Vignette;
        public static Entry Scanline;

        // ---------- colour-ref resolution ----------
        //
        // A "colour ref" is either a palette entry Name (so re-tinting the palette re-tints
        // everything referencing it) or a literal "#RRGGBBAA" (an escape hatch for one-off
        // colours the palette doesn't carry). Widgets store the ref string; drawing code calls
        // Resolve every frame, so the common path — a palette-name hit — must not allocate.

        /// <summary>
        /// Turn a colour ref into a real <see cref="Color"/>. Empty -> <paramref name="fallback"/>;
        /// a '#'-prefixed or bare 6/8-char hex string -> parsed literal; otherwise the palette
        /// entry whose Name matches (ordinal); no match -> <paramref name="fallback"/>.
        /// The palette-name path is allocation-free (indexed scan, ordinal compare, cached Value).
        /// </summary>
        public static Color Resolve(string colorRef, Color fallback)
        {
            if (string.IsNullOrEmpty(colorRef)) return fallback;

            // RadialPalette.FromHex already TrimStart('#')s and reads 6 or 8 hex chars, so the
            // literal is handed off whole — no Substring here.
            if (LooksLikeHex(colorRef))
                return Overlay.RadialPalette.FromHex(colorRef);

            for (int i = 0; i < All.Count; i++)
            {
                Entry e = All[i];
                if (string.Equals(e.Name, colorRef, StringComparison.Ordinal))
                    return e.Value;
            }
            return fallback;
        }

        /// <summary>True when the ref names a palette entry rather than a hex literal — the editor
        /// uses this to choose between the palette dropdown and the free colour wheel.</summary>
        public static bool IsPaletteName(string colorRef)
        {
            return !string.IsNullOrEmpty(colorRef) && !LooksLikeHex(colorRef);
        }

        /// <summary>Format a colour as a "#RRGGBBAA" ref (RadialPalette.ToHex emits the bare
        /// hex, so we prepend the '#' that marks it as a literal rather than a palette name).</summary>
        public static string ToHexRef(Color c)
        {
            return "#" + Overlay.RadialPalette.ToHex(c);
        }

        /// <summary>Cheap classifier: a leading '#', or a pure-hex body of exactly 6 or 8 chars,
        /// means "hex literal". Char-by-char so it never allocates.</summary>
        private static bool LooksLikeHex(string s)
        {
            if (s[0] == '#') return true;
            int n = s.Length;
            if (n != 6 && n != 8) return false;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static ConfigEntry<int> _paletteVersion;

        /// <summary>Bump when the DEFAULTS change look. Saved configs keep old hexes
        /// forever, so a version below current applies the new defaults ONCE (the F9
        /// wheels then own them again). 2 = the 0.7.0 restyle: near-black glass, WHITE
        /// values, cyan demoted to accents only (play-test: "everything has a blue hue").</summary>
        private const int CurrentPaletteVersion = 2;

        public static void Bind(ConfigFile cfg)
        {
            All.Clear();

            PanelFill = Add(cfg, "HudPanelFill", "05090ECC",
                "HUD panel background — near-black visor glass.");
            PanelBorder = Add(cfg, "HudPanelBorder", "2E7A94AA",
                "The thin steel-blue outline on every HUD panel.");
            LineAccent = Add(cfg, "HudLineAccent", "35C8E8CC",
                "Bright line-work: separators, underlines, the top bar's edge line.");

            TextLabel = Add(cfg, "HudTextLabel", "8FB4C2D0",
                "Small caps labels (PRESSURE, LEFT HAND, HELMET...).");
            TextValue = Add(cfg, "HudTextValue", "FFFFFFF5",
                "The big value text (101 kPa, 20.9 %...) — white; colour belongs to accents.");
            TextDim = Add(cfg, "HudTextDim", "6A8E9BA8",
                "Tertiary text: units, hints, the UTC prefix.");

            Good = Add(cfg, "HudGood", "4CE07AE6",
                "Healthy/OK readings and gauge fills.");
            Warn = Add(cfg, "HudWarn", "FFB13DE6",
                "Readings drifting out of the safe band.");
            Critical = Add(cfg, "HudCritical", "FF4A3DF0",
                "Dangerous readings; also the DYING/CHOKING words.");

            CompassTick = Add(cfg, "HudCompassTick", "35C8E870",
                "Compass ribbon tick marks.");
            CompassCardinal = Add(cfg, "HudCompassCardinal", "D8F0F8EE",
                "The N / NE / E ... letters on the compass.");
            CompassNeedle = Add(cfg, "HudCompassNeedle", "FF8C29D9",
                "The centre caret marking your exact heading.");

            HologramTint = Add(cfg, "HudHologramTint", "8FE0F0B0",
                "Tint over the 3D character portrait WHEN its hologram look is enabled " +
                "(off by default — the portrait shows natural colours).");
            BareWord = Add(cfg, "HudBareWord", "D8F4FAE0",
                "The felt-sense words (WARM, HUNGRY...) shown without a powered suit.");

            SlotNumber = Add(cfg, "HudSlotNumber", "FFFFFFE6",
                "The 1-6 key numbers on the equipment column.");
            ActiveHandAccent = Add(cfg, "HudActiveHand", "FF8C29E6",
                "Border/edge accent marking the ACTIVE hand box.");
            DropHighlight = Add(cfg, "HudDropHighlight", "4CE07AF0",
                "Highlight on a hand / 1-6 equipment box while a dragged item is hovering it and " +
                "can be dropped there. Per-element you can pick border-only vs whole-box in F9.");

            Vignette = Add(cfg, "HudVignette", "01070AB8",
                "Darkening around the screen edges — the visor rim shadow.");
            Scanline = Add(cfg, "HudScanline", "0A20281C",
                "Scanline shading in Dome projection mode (alpha 0 = off).");

            // One-time restyle: configs saved before the current default set keep the old
            // look forever otherwise. Runs once, then the user's wheels rule again.
            _paletteVersion = cfg.Bind("11. HUD Colours", "PaletteVersion", 0,
                "Internal: which default palette this config was last synced to. " +
                "Do not edit — set it to 0 to re-apply the shipped defaults once.");
            if (_paletteVersion.Value < CurrentPaletteVersion)
            {
                ResetToDefaults();
                _paletteVersion.Value = CurrentPaletteVersion;
            }
        }

        private static Entry Add(ConfigFile cfg, string name, string defaultHex, string desc)
        {
            var e = new Entry(cfg, name, defaultHex, desc);
            All.Add(e);
            return e;
        }

        public static void ResetToDefaults()
        {
            History.PushUndo(Snapshot());
            foreach (var e in All)
                e.Config.Value = (string)e.Config.DefaultValue;
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

        /// <summary>Undo/redo for the F9 editor — one colour drag is one step (the editor
        /// captures the snapshot on widget pickup and commits on release).</summary>
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
    }
}
