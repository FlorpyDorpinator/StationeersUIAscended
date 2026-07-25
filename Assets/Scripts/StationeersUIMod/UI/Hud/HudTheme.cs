using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A THEME is the complete GLOBAL look: every global HUD setting (colours, effects, curvature,
    /// sizing) plus the HUD colour palette AND the radial colour palette. A HUD profile stores its
    /// own theme snapshot so switching profiles restores that profile's whole look — not just its
    /// element layout. Without this, the globals live in ONE shared config that every profile reads
    /// from, so recolouring one profile silently recoloured them all (the bug that motivated this).
    ///
    /// Snapshot/apply is string-keyed and version-tolerant:
    ///   - HudConfig entries: reflected from its public static <see cref="ConfigEntryBase"/> fields
    ///     (so new settings are captured automatically), keyed "cfg:&lt;FieldName&gt;", MINUS a
    ///     denylist of meta / editor / per-machine settings that must NOT travel with a theme
    ///     (which profile is active, the editor keybind, the reference resolution, debug toggles).
    ///   - HudPalette entries: keyed "pal:&lt;Name&gt;".
    ///   - RadialPalette entries: keyed "rad:&lt;Name&gt;" — so the radials follow the theme too,
    ///     whether or not the visor HUD itself is enabled.
    /// A key present in the snapshot but absent in this build is ignored; a key absent from the
    /// snapshot leaves that setting untouched. So an older profile applies cleanly and a themeless
    /// profile changes nothing.
    /// </summary>
    public static class HudTheme
    {
        // Global settings that are per-MACHINE, per-editor or meta — they identify or configure the
        // environment, not the look, so they must never be captured into or restored from a theme.
        // (HudActiveProfile especially: restoring it would change WHICH profile is active mid-load.)
        private static readonly HashSet<string> Exclude = new HashSet<string>
        {
            "VisorHudEnabled", "UseDocumentHud", "LegacyImGuiHud", "HudActiveProfile", "HudEditorKey",
            "GridSnapEnabled", "GridSnapSize", "ShowGrid", "DebugShowAll", "DebugShowAllBare",
            "HudScaleWithRes", "HudRefWidth", "HudRefHeight", "HudScaleMatch",
        };

        private static FieldInfo[] _cfgFields;
        private static FieldInfo[] CfgFields
        {
            get
            {
                if (_cfgFields == null)
                    _cfgFields = typeof(HudConfig).GetFields(BindingFlags.Public | BindingFlags.Static);
                return _cfgFields;
            }
        }

        /// <summary>Capture the current globals as a flat list of key/value pairs (the on-disk form,
        /// stored in the profile XML). Values are the config's own serialized form, so any type
        /// round-trips exactly.</summary>
        public static List<HudDocument.ThemeEntry> Snapshot()
        {
            var list = new List<HudDocument.ThemeEntry>(96);
            foreach (var f in CfgFields)
            {
                if (!typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType)) continue;
                if (Exclude.Contains(f.Name)) continue;
                var e = f.GetValue(null) as ConfigEntryBase;
                if (e == null) continue;
                try { list.Add(new HudDocument.ThemeEntry { K = "cfg:" + f.Name, V = e.GetSerializedValue() }); }
                catch { }
            }
            foreach (var e in HudPalette.All)
                list.Add(new HudDocument.ThemeEntry { K = "pal:" + e.Name, V = e.Config.Value });
            foreach (var e in Overlay.RadialPalette.All)
                list.Add(new HudDocument.ThemeEntry { K = "rad:" + e.Name, V = e.Config.Value });
            return list;
        }

        /// <summary>Restore globals from a snapshot. Missing keys leave their setting untouched;
        /// unknown keys are ignored. Fail-soft per entry — one bad value never aborts the load.</summary>
        public static void Apply(List<HudDocument.ThemeEntry> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0) return;

            // Index the snapshot once for O(1) lookups.
            var map = new Dictionary<string, string>(snapshot.Count);
            for (int i = 0; i < snapshot.Count; i++)
            {
                var t = snapshot[i];
                if (t != null && !string.IsNullOrEmpty(t.K)) map[t.K] = t.V;
            }

            foreach (var f in CfgFields)
            {
                if (!typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType)) continue;
                if (Exclude.Contains(f.Name)) continue;
                string v;
                if (!map.TryGetValue("cfg:" + f.Name, out v) || v == null) continue;
                var e = f.GetValue(null) as ConfigEntryBase;
                if (e == null) continue;
                try { e.SetSerializedValue(v); } catch { }
            }
            foreach (var e in HudPalette.All)
            {
                string v;
                if (map.TryGetValue("pal:" + e.Name, out v) && v != null) e.Config.Value = v;
            }
            foreach (var e in Overlay.RadialPalette.All)
            {
                string v;
                if (map.TryGetValue("rad:" + e.Name, out v) && v != null) e.Config.Value = v;
            }
        }

        /// <summary>Deep-copy a theme list (Clone / undo snapshots must not alias the entries).</summary>
        public static List<HudDocument.ThemeEntry> CopyOf(List<HudDocument.ThemeEntry> src)
        {
            if (src == null) return null;
            var dst = new List<HudDocument.ThemeEntry>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                var t = src[i];
                if (t != null) dst.Add(new HudDocument.ThemeEntry { K = t.K, V = t.V });
            }
            return dst;
        }
    }
}
