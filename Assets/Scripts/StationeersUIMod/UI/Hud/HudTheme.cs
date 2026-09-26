using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A THEME is the complete GLOBAL look: every global HUD setting (colours, effects, curvature,
    /// sizing) plus the HUD colour palette, the radial colour palette, the radial/hint-bar LOOK
    /// knobs, the Universal Inventory (Grid) skin and the Control Center (F10 menu) skin. A HUD
    /// profile stores its own theme snapshot so switching profiles restores that profile's whole
    /// look — not just its element layout. Without this, the globals live in ONE shared config that
    /// every profile reads from, so recolouring one profile silently recoloured them all (the bug
    /// that motivated this — and the same bug FlorpyDorp hit again with the Grid/menu look not
    /// travelling at all, which is what the 0.9.2.5 Wave C fold below fixes).
    ///
    /// Snapshot/apply is string-keyed and version-tolerant:
    ///   - HudConfig entries: reflected from its public static <see cref="ConfigEntryBase"/> fields
    ///     (so new settings are captured automatically), keyed "cfg:&lt;FieldName&gt;", MINUS a
    ///     denylist of meta / editor / per-machine / pure-PERFORMANCE settings that must NOT travel
    ///     with a theme (which profile is active, the editor keybind, the reference resolution,
    ///     debug toggles, frost/bloom RESOLUTION knobs that trade frame time, not look).
    ///   - HudPalette entries: keyed "pal:&lt;Name&gt;".
    ///   - RadialPalette entries: keyed "rad:&lt;Name&gt;" — so the radials follow the theme too,
    ///     whether or not the visor HUD itself is enabled.
    ///   - <see cref="UIAConfig"/> radial-visuals + hint-bar LOOK knobs: an explicit include-list
    ///     (see <see cref="RadialThemeKeys"/>) reflected by name, keyed "radial:&lt;FieldName&gt;".
    ///     An include-list (not a denylist like HudConfig's) because reflecting all of UIAConfig
    ///     would drag in keybinds, control schema and other per-machine behaviour.
    ///   - The Universal Inventory (Grid) skin: delegated to
    ///     <see cref="global::StationeersUIMod.UI.Grid.GridTheme"/>'s own SnapshotInto/ApplyFrom
    ///     (that class owns its own field list), keyed "grid:&lt;Name&gt;".
    ///   - The Control Center (F10 menu) skin: delegated to
    ///     <see cref="global::StationeersUIMod.UI.Menu.Kit.UiaMenuTheme"/>'s own
    ///     SnapshotInto/ApplyFrom, keyed "menu:&lt;Name&gt;".
    /// A key present in the snapshot but absent in this build is ignored; a key absent from the
    /// snapshot leaves that setting untouched — EXCEPT inside a family the snapshot carries at all
    /// ("cfg:" and "rad:"): there a missing key is a setting that did not exist when the theme was
    /// stamped, and it applies as that setting's DEFAULT (see <see cref="Apply"/>). So an older
    /// profile applies cleanly, never inherits a newer knob from the previous theme, and a themeless
    /// profile changes nothing.
    /// </summary>
    public static class HudTheme
    {
        // Global settings that are per-MACHINE, per-editor, meta, or pure PERFORMANCE — they
        // identify/configure the environment or trade frame time, not the look, so they must never
        // be captured into or restored from a theme. (HudActiveProfile especially: restoring it
        // would change WHICH profile is active mid-load.)
        //
        // The last five are Wave C additions (0.9.2.5): FrostDownsample/FrostUpdateEveryN/
        // FxBloomRes/FxBloomBlurSteps trade blur/bloom RESOLUTION for frame time on the player's
        // OWN machine — importing someone else's theme must never silently tank another player's
        // frame rate. FxBloomFineDetail rides along because it is the legacy alias FxBloomRes=-1
        // resolves through (HudEditorWindow's bloom-resolution combo); letting one travel without
        // the other would make an imported theme's bloom resolution non-deterministic. Frost/bloom
        // LOOK knobs (FrostStrength, FrostDepth, FrostTint, FrostDarken, FxBloomStrength,
        // FxBloomThreshold, FxBloomKnee, FxBloomSpread, FxBloomSaturation, FxBloomTint, …) are
        // deliberately NOT in this list and keep travelling.
        private static readonly HashSet<string> Exclude = new HashSet<string>
        {
            "VisorHudEnabled", "HudActiveProfile", "HudEditorKey",
            "GridSnapEnabled", "GridSnapSize", "ShowGrid", "DebugShowAll", "DebugShowAllBare",
            "HudScaleWithRes", "HudRefWidth", "HudRefHeight", "HudScaleMatch",
            "FrostDownsample", "FrostUpdateEveryN", "FxBloomRes", "FxBloomBlurSteps", "FxBloomFineDetail",
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

        // ---------------------------------------------------------------- radial: include-list
        // UIAConfig "8. Radial Visuals" (32) + "10. Hint Bar" (14) LOOK knobs — everything that
        // changes how a radial/hint-bar LOOKS. Deliberately excludes their behaviour siblings in
        // the same sections: RadialMaxWedges, the double-tap window, wedge sounds, hint-fade,
        // RadialHintBar (on/off) and HintBarPreview (an editor-preview toggle, same class of thing
        // as HudEditorKey) — those change WHAT HAPPENS, not what it looks like, and must not change
        // when a player merely tries a theme. An include-list (not a denylist) because reflecting
        // all of UIAConfig would also drag in keybinds, control-scheme and per-machine behaviour.
        private static readonly string[] RadialThemeKeys =
        {
            // "8. Radial Visuals"
            "RadialOuterRadius", "RadialInnerRadius", "IconFlipV", "RadialIconRatio",
            "RadialShineIntensity", "ParkedChipRadius", "RadialShowWedgeLabels", "RadialEdgeFeather",
            "RadialBorderWidth", "RadialSideBorders", "RadialSideWidthInner", "RadialSideWidthOuter",
            "RadialWedgeGapDeg", "RadialDimShading", "RadialDimStrength", "RadialFontName",
            "RadialUppercaseLabels", "RadialShowStateText", "RadialShowBindingLabels",
            "RadialSatelliteScale", "RadialHubTitleSize", "RadialTextVerb", "RadialTextLabel",
            "RadialTextSub", "RadialTextWarn", "RadialRotateLongLabels", "RadialSatelliteHubRatio",
            "RadialDynamicReadoutText", "RadialFrost", "RadialFrostStrength", "RadialSheen",
            "RadialEdgeLight",
            // "10. Hint Bar"
            "HintBarCorner", "HintBarBorderWidth", "HintBarFeather", "HintBarSheen", "HintBarSpec",
            "HintBarGlow", "HintBarGlowWidth", "HintBarFontSize", "HintBarBold", "HintBarHeight",
            "HintBarPadding", "HintBarDrop", "HintBarFrost", "HintBarFrostStrength",
        };

        private static FieldInfo[] _radialFields;   // parallel to RadialThemeKeys; null = unresolved
        private static bool _radialKeysValidated;

        private static FieldInfo[] RadialFields
        {
            get
            {
                if (_radialFields == null)
                {
                    var t = typeof(UIAConfig);
                    var fields = new FieldInfo[RadialThemeKeys.Length];
                    for (int i = 0; i < RadialThemeKeys.Length; i++)
                        fields[i] = t.GetField(RadialThemeKeys[i], BindingFlags.Public | BindingFlags.Static);
                    _radialFields = fields;
                }
                if (!_radialKeysValidated)
                {
                    // Validated once, lazily (on first Snapshot/Apply, not at class-init — no need
                    // to pay for it unless a theme is actually captured/restored). A rename in
                    // UIAConfig must never silently drop a knob from every theme, so an unresolved
                    // FIELD (the reflection lookup itself, independent of whether BepInEx has bound
                    // it yet) is a warning, not a silent skip.
                    _radialKeysValidated = true;
                    for (int i = 0; i < RadialThemeKeys.Length; i++)
                    {
                        var f = _radialFields[i];
                        if (f == null || !typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType))
                            UIALog.Warn("HudTheme: radial theme key '" + RadialThemeKeys[i] +
                                "' did not resolve to a UIAConfig ConfigEntry field (renamed or removed?).");
                    }
                }
                return _radialFields;
            }
        }

        /// <summary>Capture the current globals as a flat list of key/value pairs (the on-disk form,
        /// stored in the profile XML). Values are the config's own serialized form, so any type
        /// round-trips exactly.</summary>
        public static List<HudDocument.ThemeEntry> Snapshot()
        {
            var list = new List<HudDocument.ThemeEntry>(96 + RadialThemeKeys.Length + 60);
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
            SnapshotRadial(list);
            try { global::StationeersUIMod.UI.Grid.GridTheme.SnapshotInto(list, "grid:"); }
            catch (Exception e) { UIALog.Warn("HudTheme: Grid theme snapshot failed: " + e.Message); }
            try { global::StationeersUIMod.UI.Menu.Kit.UiaMenuTheme.SnapshotInto(list, "menu:"); }
            catch (Exception e) { UIALog.Warn("HudTheme: menu theme snapshot failed: " + e.Message); }
            return list;
        }

        /// <summary>The "radial:" family — <see cref="RadialThemeKeys"/> reflected off
        /// <see cref="UIAConfig"/> by name. An unresolved key (see <see cref="RadialFields"/>) is
        /// skipped, not thrown — fail-soft, same contract as every other family here.</summary>
        private static void SnapshotRadial(List<HudDocument.ThemeEntry> into)
        {
            var fields = RadialFields;
            for (int i = 0; i < RadialThemeKeys.Length; i++)
            {
                var f = fields[i];
                if (f == null) continue;
                var e = f.GetValue(null) as ConfigEntryBase;
                if (e == null) continue;
                try { into.Add(new HudDocument.ThemeEntry { K = "radial:" + RadialThemeKeys[i], V = e.GetSerializedValue() }); }
                catch { }
            }
        }

        /// <summary>Restore globals from a snapshot. Missing keys leave their setting untouched —
        /// except in the "cfg:" / "rad:" families when the snapshot carries that family at all, where
        /// a missing key applies as the setting's default (see the rule below); unknown keys are
        /// ignored. Fail-soft per entry — one bad value never aborts the load.</summary>
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

            // Does this theme carry the cfg family at all? A theme with ZERO cfg entries is a
            // palette-only / pre-fold snapshot and keeps the documented absent-key contract
            // (change nothing). But a theme WITH cfg entries took a FULL snapshot when it was
            // stamped — so a missing cfg key there means the KNOB DIDN'T EXIST YET. Render the
            // profile as it looked when authored: the knob's DEFAULT. Without this, a knob
            // added in an update bleeds the CURRENT value through every older profile
            // (FlorpyDorp's repro: corner Cut set under Zirillian Red survived a switch back
            // to Stationeers Blue, whose stored theme predates the corner-style knob).
            //
            // B2: the SAME rule for the radial palette ("rad:"). A theme carrying ANY rad: key took the
            // whole palette when it was stamped, so a missing rad: key there is an entry that did not
            // exist yet — it renders as that entry's DEFAULT (RadialPalette.Entry.DefaultValue): the
            // four curved-label colours (ArcPlateFill/ArcPlateBorder/ArcText/ArcAccent) go back to
            // AUTO. Without this, an Arc colour pinned under one theme stuck to EVERY theme forever —
            // no shipped profile carries those keys, so switching never touched them. A theme with no
            // rad: key at all (palette-less / pre-fold) still changes nothing.
            bool themedCfg = false, themedRad = false;
            foreach (var k in map.Keys)
            {
                if (!themedCfg && k.StartsWith("cfg:", StringComparison.Ordinal)) themedCfg = true;
                else if (!themedRad && k.StartsWith("rad:", StringComparison.Ordinal)) themedRad = true;
                if (themedCfg && themedRad) break;
            }

            foreach (var f in CfgFields)
            {
                if (!typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType)) continue;
                if (Exclude.Contains(f.Name)) continue;
                var e = f.GetValue(null) as ConfigEntryBase;
                if (e == null) continue;
                string v;
                if (!map.TryGetValue("cfg:" + f.Name, out v) || v == null)
                {
                    if (themedCfg)
                        try { e.BoxedValue = e.DefaultValue; } catch { }
                    continue;
                }
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
                else if (themedRad)
                    try { e.Config.Value = e.DefaultValue; } catch { }
            }
            ApplyRadial(map);
            try { global::StationeersUIMod.UI.Grid.GridTheme.ApplyFrom(map, "grid:"); }
            catch (Exception e) { UIALog.Warn("HudTheme: Grid theme apply failed: " + e.Message); }
            try { global::StationeersUIMod.UI.Menu.Kit.UiaMenuTheme.ApplyFrom(map, "menu:"); }
            catch (Exception e) { UIALog.Warn("HudTheme: menu theme apply failed: " + e.Message); }
        }

        /// <summary>Restore the "radial:" family from an already-indexed snapshot map. Same
        /// missing-key/unknown-key contract as <see cref="Apply"/>.</summary>
        private static void ApplyRadial(Dictionary<string, string> map)
        {
            var fields = RadialFields;
            for (int i = 0; i < RadialThemeKeys.Length; i++)
            {
                var f = fields[i];
                if (f == null) continue;
                string v;
                if (!map.TryGetValue("radial:" + RadialThemeKeys[i], out v) || v == null) continue;
                var e = f.GetValue(null) as ConfigEntryBase;
                if (e == null) continue;
                try { e.SetSerializedValue(v); } catch { }
            }
        }

        /// <summary>Add every key present in a fresh <see cref="Snapshot"/> but MISSING from
        /// <paramref name="theme"/>, leaving existing values untouched. Returns how many were
        /// added. Used by the v2-&gt;v3 config migration's one-shot top-up (see
        /// <see cref="Core.ConfigMigration.PendingThemeTopUp"/> /
        /// <see cref="Features.HudProfileStore.TopUpAllThemes"/>): when a new family of knobs
        /// starts travelling, an updating player's existing profiles carry none of its keys, and
        /// <see cref="Apply"/> leaving an absent key untouched means the fold would silently do
        /// nothing until the player happened to edit one of the new knobs. Stamping the CURRENT
        /// globals in as the "already captured" value makes the fold a no-op for their eyes on the
        /// very next launch, and the profile starts out holding the look they already have.</summary>
        public static int TopUp(List<HudDocument.ThemeEntry> theme)
        {
            if (theme == null) return 0;
            var have = new HashSet<string>();
            for (int i = 0; i < theme.Count; i++)
            {
                var t = theme[i];
                if (t != null && !string.IsNullOrEmpty(t.K)) have.Add(t.K);
            }
            var fresh = Snapshot();
            int added = 0;
            for (int i = 0; i < fresh.Count; i++)
            {
                var t = fresh[i];
                if (t == null || string.IsNullOrEmpty(t.K)) continue;
                if (have.Contains(t.K)) continue;
                theme.Add(t);
                added++;
            }
            return added;
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
