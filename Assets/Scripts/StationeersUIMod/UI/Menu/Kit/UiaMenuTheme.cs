using System.Collections.Generic;
using BepInEx.Configuration;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// The F10 Control Center's THEME SOURCE (2026-07-17). Two states, exactly like every HUD
    /// element after the style standardisation:
    ///  • FOLLOW (default): the menu derives its whole palette from the active F9 HUD profile —
    ///    <see cref="HudPalette"/> fills/accents/text + <see cref="HudConfig"/> glass — so re-theming
    ///    the visor re-skins the menu live. <see cref="UiaTheme"/>'s colour getters read the
    ///    derivation; this class only owns the follow flag + change detection.
    ///  • OVERRIDE: per-colour config strings (a palette-entry NAME or a "#RRGGBBAA" literal,
    ///    resolved by <see cref="HudPalette.Resolve"/> — so an override can itself LINK to a HUD
    ///    palette entry). Edited through the F9 editor's menu popup, mirroring the element popup.
    ///
    /// Config only — no Unity objects, nothing to reset on F6 beyond the re-Bind. The colours are
    /// live getters, so the built menu restyles whenever <see cref="StyleHash"/> changes
    /// (UiaControlCenter polls it).
    /// </summary>
    public static class UiaMenuTheme
    {
        private const string Section = "12. Control Center";

        public static ConfigEntry<bool> Follow;

        /// <summary>One overridable menu colour: its stable key, popup label, hardcoded default
        /// (the pre-2026-07-17 grey look), and the bound config entry.</summary>
        private sealed class Ovr
        {
            public readonly string Name;
            public readonly string Label;
            public readonly Color Def;
            public ConfigEntry<string> Entry;
            public Ovr(string name, string label, Color def) { Name = name; Label = label; Def = def; }
        }

        // The override set = every UiaTheme surface colour except the two derived *Dim accents.
        // Defaults reproduce the original hand-picked grey-glass constants, so unchecking "Follow"
        // lands exactly on the look the menu shipped with.
        private static readonly Ovr[] _ov =
        {
            new Ovr("Scrim",       "Screen dim (scrim)",   new Color(0.02f, 0.02f, 0.02f, 0.50f)),
            new Ovr("Window",      "Window background",     new Color(0.16f, 0.17f, 0.18f, 0.90f)),
            new Ovr("Bar",         "Title / tab strip",     new Color(0.20f, 0.21f, 0.22f, 0.95f)),
            new Ovr("Panel",       "Panel",                 new Color(0.21f, 0.22f, 0.23f, 0.92f)),
            new Ovr("PanelRaised", "Panel (raised)",        new Color(0.27f, 0.28f, 0.29f, 0.98f)),
            new Ovr("PanelHover",  "Panel (hover)",         new Color(0.33f, 0.34f, 0.36f, 1.00f)),
            new Ovr("Track",       "Slider / toggle track", new Color(0.36f, 0.37f, 0.39f, 1.00f)),
            new Ovr("Divider",     "Divider line",          new Color(1f, 1f, 1f, 0.08f)),
            new Ovr("Border",      "Border (structural)",   new Color(0.18f, 0.48f, 0.58f, 0.67f)),
            new Ovr("Accent",      "Accent (primary)",      new Color(0.35f, 0.78f, 0.90f, 1.00f)),
            new Ovr("Selected",    "Selected (active)",     new Color(1.00f, 0.55f, 0.16f, 1.00f)),
            new Ovr("On",          "Toggle ON",             new Color(0.36f, 0.82f, 0.48f, 1.00f)),
            new Ovr("Off",         "Toggle OFF",            new Color(0.35f, 0.40f, 0.45f, 1.00f)),
            new Ovr("Text",        "Text",                  new Color(0.94f, 0.97f, 1.00f, 1.00f)),
            new Ovr("TextDim",     "Text (dim)",            new Color(0.72f, 0.80f, 0.87f, 1.00f)),
            new Ovr("TextMute",    "Text (muted)",          new Color(0.52f, 0.60f, 0.68f, 1.00f)),
            new Ovr("Good",        "Good / ok",             new Color(0.42f, 0.85f, 0.52f, 1.00f)),
            new Ovr("Warn",        "Warning",               new Color(0.98f, 0.78f, 0.30f, 1.00f)),
            new Ovr("Critical",    "Critical",              new Color(0.95f, 0.40f, 0.38f, 1.00f)),
        };

        /// <summary>The "menu:" theme family (see <see cref="UI.Hud.HudTheme"/>): the follow flag
        /// plus every override, keyed by this class's own stable <see cref="Ovr.Name"/> — not
        /// reflection, since the overrides live in a private array, not public static fields.
        /// Called by <c>HudTheme.Snapshot</c>; this class owns its own field list so
        /// <c>HudTheme</c> never reflects over another class's private layout.</summary>
        public static void SnapshotInto(List<HudDocument.ThemeEntry> into, string prefix)
        {
            if (into == null) return;
            if (Follow != null) into.Add(new HudDocument.ThemeEntry { K = prefix + "Follow", V = Follow.Value ? "true" : "false" });
            for (int i = 0; i < _ov.Length; i++)
            {
                var o = _ov[i];
                if (o.Entry == null) continue;
                into.Add(new HudDocument.ThemeEntry { K = prefix + o.Name, V = o.Entry.Value });
            }
        }

        /// <summary>Restore the "menu:" family from an already-indexed snapshot map. Same
        /// missing-key/unknown-key contract as <see cref="UI.Hud.HudTheme.Apply"/>: a key absent
        /// from <paramref name="map"/> leaves that setting untouched.</summary>
        public static void ApplyFrom(Dictionary<string, string> map, string prefix)
        {
            if (map == null) return;
            string v;
            if (Follow != null && map.TryGetValue(prefix + "Follow", out v) && v != null)
            {
                bool b;
                if (bool.TryParse(v, out b)) Follow.Value = b;
            }
            for (int i = 0; i < _ov.Length; i++)
            {
                var o = _ov[i];
                if (o.Entry == null) continue;
                if (map.TryGetValue(prefix + o.Name, out v) && v != null) o.Entry.Value = v;
            }
        }

        public static void Bind(ConfigFile cfg)
        {
            Follow = cfg.Bind(Section, "FollowHudTheme", true,
                "Skin the F10 Control Center from the active F9 HUD profile's theme (palette + " +
                "glass). Off = use the per-colour overrides below. Edit visually in the F9 HUD " +
                "editor: open F10 while the editor is active and click the menu.");
            for (int i = 0; i < _ov.Length; i++)
            {
                var o = _ov[i];
                o.Entry = cfg.Bind(Section, "Menu" + o.Name, HudPalette.ToHexRef(o.Def),
                    "Menu " + o.Label + " when NOT following the UI theme. A palette entry name " +
                    "(e.g. HudPanelFill) or a #RRGGBBAA literal.");
            }
        }

        public static bool Following => Follow != null && Follow.Value;

        /// <summary>Resolve one overridable colour by its key (used by <see cref="UiaTheme"/> in
        /// the non-following state). A palette name live-tracks the HUD palette; a hex literal
        /// stands alone; anything unresolvable falls back to the hardcoded default.</summary>
        public static Color Ov(string name)
        {
            for (int i = 0; i < _ov.Length; i++)
            {
                var o = _ov[i];
                if (o.Name == name)
                    return o.Entry != null ? HudPalette.Resolve(o.Entry.Value, o.Def) : o.Def;
            }
            return Color.magenta; // programmer error — an unknown key
        }

        /// <summary>A change signal for the built menu: hashes the ACTUAL resolved output colours
        /// (so it fires for both a follow-mode HUD palette drag and an override edit). O(18) cheap
        /// palette reads; UiaControlCenter polls it and rebuilds only on a real change.</summary>
        public static int StyleHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Following ? 1 : 0);
                h = Comb(h, UiaTheme.Scrim);
                h = Comb(h, UiaTheme.Window);
                h = Comb(h, UiaTheme.Bar);
                h = Comb(h, UiaTheme.Panel);
                h = Comb(h, UiaTheme.PanelRaised);
                h = Comb(h, UiaTheme.PanelHover);
                h = Comb(h, UiaTheme.Track);
                h = Comb(h, UiaTheme.Divider);
                h = Comb(h, UiaTheme.Border);
                h = Comb(h, UiaTheme.Accent);
                h = Comb(h, UiaTheme.Selected);
                h = Comb(h, UiaTheme.On);
                h = Comb(h, UiaTheme.Off);
                h = Comb(h, UiaTheme.Text);
                h = Comb(h, UiaTheme.TextDim);
                h = Comb(h, UiaTheme.TextMute);
                h = Comb(h, UiaTheme.Good);
                h = Comb(h, UiaTheme.Warn);
                h = Comb(h, UiaTheme.Critical);
                return h;
            }
        }

        private static int Comb(int h, Color c)
        {
            unchecked
            {
                int q = (int)(Mathf.Clamp01(c.r) * 255f)
                    | ((int)(Mathf.Clamp01(c.g) * 255f) << 8)
                    | ((int)(Mathf.Clamp01(c.b) * 255f) << 16)
                    | ((int)(Mathf.Clamp01(c.a) * 255f) << 24);
                return h * 31 + q;
            }
        }

        /// <summary>Build the F9-style property list for the menu-theme popup (the same
        /// <see cref="HudProp"/> descriptors an element uses, so the drawer renders identical
        /// colour controls). Config-backed — no undo, BepInEx persists on write.</summary>
        public static void DescribeProps(List<HudProp> into)
        {
            into.Add(HudProp.Header("Menu theme"));
            into.Add(HudProp.Bool("Follow the HUD's F9 theme (palette + glass)",
                () => Following, v => { if (Follow != null) Follow.Value = v; }));

            if (Following)
            {
                into.Add(HudProp.Header("Colours follow F9 -> Palette + glass."));
                into.Add(HudProp.Header("Uncheck above to override each colour."));
                return;
            }

            into.Add(HudProp.Header("Per-colour overrides (name a palette entry or type #RRGGBBAA)"));
            for (int i = 0; i < _ov.Length; i++)
            {
                var o = _ov[i];
                Color def = o.Def;
                into.Add(HudProp.Color(o.Label,
                    () => o.Entry != null ? o.Entry.Value : "",
                    v => { if (o.Entry != null) o.Entry.Value = v ?? ""; },
                    () => def));
            }
        }
    }
}
