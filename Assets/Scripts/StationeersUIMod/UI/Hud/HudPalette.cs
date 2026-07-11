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

        // Screen dressing
        public static Entry Vignette;
        public static Entry Scanline;

        public static void Bind(ConfigFile cfg)
        {
            All.Clear();

            PanelFill = Add(cfg, "HudPanelFill", "060E1299",
                "HUD panel background — the dark visor glass.");
            PanelBorder = Add(cfg, "HudPanelBorder", "3FD9EC66",
                "The thin cyan outline on every HUD panel.");
            LineAccent = Add(cfg, "HudLineAccent", "3FD9ECCC",
                "Bright line-work: separators, underlines, the top bar's edge line.");

            TextLabel = Add(cfg, "HudTextLabel", "9FD3DFC8",
                "Small caps labels (PRESSURE, LEFT HAND, HELMET...).");
            TextValue = Add(cfg, "HudTextValue", "E9FBFFF2",
                "The big value text (101 kPa, 20.9 %...).");
            TextDim = Add(cfg, "HudTextDim", "7BA9B4A0",
                "Tertiary text: units, hints, the UTC prefix.");

            Good = Add(cfg, "HudGood", "58E8A8E6",
                "Healthy/OK readings.");
            Warn = Add(cfg, "HudWarn", "FFC257E6",
                "Readings drifting out of the safe band.");
            Critical = Add(cfg, "HudCritical", "FF5F5FF0",
                "Dangerous readings; also the DYING/CHOKING words.");

            CompassTick = Add(cfg, "HudCompassTick", "3FD9EC80",
                "Compass ribbon tick marks.");
            CompassCardinal = Add(cfg, "HudCompassCardinal", "CFF6FFE6",
                "The N / NE / E ... letters on the compass.");
            CompassNeedle = Add(cfg, "HudCompassNeedle", "FF8C29D9",
                "The centre caret marking your exact heading.");

            HologramTint = Add(cfg, "HudHologramTint", "41E0F0A0",
                "Tint over the 3D character hologram in the vitals card.");
            BareWord = Add(cfg, "HudBareWord", "D8F4FAE0",
                "The felt-sense words (WARM, HUNGRY...) shown without a powered suit.");

            SlotNumber = Add(cfg, "HudSlotNumber", "3FD9ECD9",
                "The 1-6 key numbers on the equipment column.");
            ActiveHandAccent = Add(cfg, "HudActiveHand", "FF8C29E6",
                "Border/edge accent marking the ACTIVE hand box.");

            Vignette = Add(cfg, "HudVignette", "01070AB8",
                "Darkening around the screen edges — the visor rim shadow.");
            Scanline = Add(cfg, "HudScanline", "0A20281C",
                "Scanline shading in Dome projection mode (alpha 0 = off).");
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
