using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Config-schema versioning + a migration hook for the mod's .cfg. See
    /// <c>Documentation/Config-and-Theme-Migration.md</c> for the how-and-when.
    ///
    /// WHY THIS EXISTS: BepInEx never overwrites a value already stored in a player's .cfg — on
    /// update it reads the STORED value, not the new code default. That is correct for genuine
    /// preferences, but it means a default we deliberately CHANGE (because the old one was wrong)
    /// never reaches an existing player, and repurposing / renaming a key strands the old value.
    /// This class is the surgical fix: a version stamp written into the file, plus an ordered list
    /// of one-time migration STEPS that run only when the stored version is behind. That lets us
    /// force a corrected default, rename a key, or remap a changed-meaning value on update — while
    /// leaving every untouched preference exactly as the player set it.
    ///
    /// Fail-soft throughout: any step that throws is logged and skipped; a broken migration never
    /// blocks load. Runs once per launch, right after every setting is bound.
    /// </summary>
    public static class ConfigMigration
    {
        /// <summary>Bump by ONE each time you add a migration STEP in <see cref="ApplyStep"/>.
        /// Fresh installs are stamped straight to this and run NO steps.</summary>
        public const int CurrentVersion = 3;

        private const string Section = "0. Internal";

        public static ConfigEntry<int> Version;

        /// <summary>Set by the v2-&gt;v3 step; consumed exactly once by
        /// <see cref="Features.HudProfileStore.LoadActive"/> the first time it runs after the
        /// profile folder is available (this class runs at BIND time, before any profile is
        /// loaded, so the top-up itself can't happen here). Never persisted — the
        /// <see cref="Version"/> stamp is the real record; a stale <c>true</c> surviving a crash
        /// mid-launch just means the (idempotent, TopUp-is-additive) top-up runs once more on the
        /// next launch, which costs nothing.</summary>
        public static bool PendingThemeTopUp;

        /// <summary>Run after all settings are bound. <paramref name="freshInstall"/> = the .cfg did
        /// not exist before this launch; those installs already carry current defaults, so they are
        /// stamped to <see cref="CurrentVersion"/> and NO step runs (a value-transforming step must
        /// never hit a fresh default and double-apply).</summary>
        public static void Run(ConfigFile cfg, bool freshInstall)
        {
            try
            {
                // Default resolves to CurrentVersion for a fresh install (skips every step) and 0 for
                // an existing PRE-versioning install (runs every step from the baseline). An install
                // that already carries the key ignores this default and returns its stored value.
                Version = cfg.Bind(Section, "ConfigVersion",
                    freshInstall ? CurrentVersion : 0,
                    "Internal schema version of this config file. Do NOT edit — the mod uses it to " +
                    "migrate your settings safely across updates.");

                int from = Version.Value;
                // At or AHEAD of current: never run a step, and never lower the stamp. Lowering a
                // higher value (e.g. a player who ran a newer mod then downgraded) would make a later
                // re-upgrade RE-RUN steps 1..n over settings that step already migrated — a
                // value-transforming step could then double-apply or stomp a preference. Leaving the
                // higher stamp means the older build simply does nothing, which is correct.
                if (from >= CurrentVersion) return;

                for (int v = from; v < CurrentVersion; v++)
                {
                    try { ApplyStep(cfg, v); }
                    catch (Exception e)
                    {
                        UIALog.Warn("ConfigMigration step v" + v + "->v" + (v + 1) + " failed: " + e.Message);
                    }
                }
                Version.Value = CurrentVersion;
                UIALog.Info("Config migrated from v" + from + " to v" + CurrentVersion + ".");
            }
            catch (Exception e) { UIALog.Warn("ConfigMigration.Run failed: " + e.Message); }
        }

        /// <summary>One migration step: bring the config from <paramref name="fromVersion"/> to
        /// <c>fromVersion + 1</c>. Add a <c>case</c> each time you bump <see cref="CurrentVersion"/>.
        /// Steps must be safe to run on any older config and must NEVER assume a value exists.</summary>
        private static void ApplyStep(ConfigFile cfg, int fromVersion)
        {
            switch (fromVersion)
            {
                case 1: // v1 -> v2 — 0.9.2.5 Wave B deleted every legacy render-path key (the 0.1.0
                    // ImGui HUD overlay, the legacy ImGui radial painter, the six fixed-panel Show*
                    // toggles + their size/font sliders now that layout lives per-element in the
                    // profile document) and folded the old standalone glitch system onto the tri-state
                    // FX family. Removing a `cfg.Bind` call does NOT remove the key from a player's
                    // .cfg — BepInEx keeps any key it didn't Bind this run as an ORPHANED entry (see
                    // ConfigFile.OrphanedEntries) and writes it straight back out on the next Save, so
                    // it would linger forever (and keep showing up in the SLP settings panel) unless we
                    // strip it here. See Documentation/Release Prep Reports/wave-b-removed-keys.md for
                    // the authoritative, independently-verified ledger this step implements.
                    MigrateGlitchKeys(cfg);
                    foreach (string[] kv in RemovedKeysV1ToV2) RemoveOrphaned(cfg, kv[0], kv[1]);
                    cfg.Save();
                    break;
                case 2: // v2 -> v3 — the 0.9.2.5 Wave C theme fold: the radial-visuals + hint-bar
                    // LOOK knobs, the Universal Inventory (Grid) skin and the Control Center (F10
                    // menu) skin now travel INSIDE a HUD profile's theme (see UI.Hud.HudTheme's
                    // "radial:"/"grid:"/"menu:" families), instead of being shared globals every
                    // profile silently read from. This step touches no .cfg key itself — it only
                    // arms a one-shot flag, because the actual work (reading/rewriting the
                    // HudProfiles/*.xml files) needs the profile folder, which is not available yet
                    // at config-bind time. HudProfileStore.LoadActive consumes the flag on its very
                    // first call and stamps the player's CURRENT globals into every profile that
                    // already carries a theme (see HudTheme.TopUp / HudProfileStore.TopUpAllThemes),
                    // so the fold is a no-op for their eyes: every profile starts out holding
                    // exactly the look they already have, and only diverges from there if they
                    // deliberately give two profiles different radial/Grid/menu looks.
                    PendingThemeTopUp = true;
                    // Also in v3: RadialPalette.TextAccent gained a real identity. It used to default
                    // to plain white (indistinguishable from TextPrimary/TextDim) while the stow/equip
                    // wedge-label highlight was a HARDCODED orange (Overlay.Theme.Accent). The re-point
                    // of Theme.Accent onto this entry would have silently turned those accents white,
                    // so the default is now the legacy orange — and players who never touched the old
                    // white default are moved onto it here (a deliberate white stays put). Runs BEFORE
                    // the theme top-up stamps globals into profiles, so profiles capture the orange.
                    ForceIfDefault(Overlay.RadialPalette.TextAccent.Config,
                        oldDefault: "FFFFFFFF", corrected: "FF8C29FF");
                    cfg.Save();
                    break;
                // ---------------------------------------------------------------------------------
                // TEMPLATE — the next time you change a shipped default or rename a key, bump
                // CurrentVersion again and fill in the next case:
                //
                // case 3: // v3 -> v4
                //     // A default was WRONG: push the corrected value only to players who never
                //     // changed it (anyone who set it on purpose keeps their choice).
                //     ForceIfDefault(UI.Hud.HudConfig.FxGlow, oldDefault: 0.50f, corrected: 0.80f);
                //
                //     // A key was RENAMED: carry the old value across. (Rename, never repurpose —
                //     // reusing a key with new meaning silently misreads the stored value.)
                //     // Bind the old key to read its stored value, copy it to the new entry:
                //     // var old = cfg.Bind("10. Visor HUD", "OldKey", 0f, "(migrated)");
                //     // if (!Equals(old.Value, 0f)) UI.Hud.HudConfig.NewKey.Value = old.Value;
                //
                //     // A key was DELETED (no rename): strip the orphan so it stops reappearing.
                //     // RemoveOrphaned(cfg, "10. Visor HUD", "DeadKey");
                //     // cfg.Save();
                //     break;
                // ---------------------------------------------------------------------------------
                default:
                    break; // baseline / unknown source version → no-op
            }
        }

        /// <summary>Every key Wave B's Bind-removal orphaned, deletes-only (renames are handled
        /// separately by <see cref="MigrateGlitchKeys"/> before this list runs). One-time ledger for
        /// the v1-&gt;v2 step; matches Documentation/Release Prep Reports/wave-b-removed-keys.md
        /// exactly (independently re-verified against `git diff` of the Wave B commit range).</summary>
        private static readonly string[][] RemovedKeysV1ToV2 =
        {
            // "7. HUD" (UIAConfig) — the 0.1.0 ImGui HUD overlay's knobs (HudOverlayFeature.cs, gone).
            new[] { "7. HUD", "Enabled" },
            new[] { "7. HUD", "HandBoxes" },
            new[] { "7. HUD", "StatusStrip" },
            new[] { "7. HUD", "Vitals" },
            new[] { "7. HUD", "Clock" },
            new[] { "7. HUD", "ContextPanel" },
            new[] { "7. HUD", "VisorArcs" },
            new[] { "7. HUD", "Scale" },
            // Gated only the ImGui overlay's helmet/sensor visibility; the live diegetic
            // system is HudConfig.DiegeticTiers. Orphaned when HudOverlayFeature died.
            new[] { "7. HUD", "HardcoreGating" },

            // "10. Visor HUD" (HudConfig) — legacy renderer gates.
            new[] { "10. Visor HUD", "LegacyImGuiHud" },
            new[] { "10. Visor HUD", "UseDocumentHud" },

            // "10. Visor HUD" — the six fixed-panel Show* toggles (per-element visibility now).
            new[] { "10. Visor HUD", "ShowTopBar" },
            new[] { "10. Visor HUD", "ShowCompass" },
            new[] { "10. Visor HUD", "ShowEquipment" },
            new[] { "10. Visor HUD", "ShowHands" },
            new[] { "10. Visor HUD", "ShowVitals" },
            new[] { "10. Visor HUD", "ShowHologram" },

            // "10. Visor HUD" — the twelve fixed-panel size sliders.
            new[] { "10. Visor HUD", "TopBarHeight" },
            new[] { "10. Visor HUD", "TopBarCurve" },
            new[] { "10. Visor HUD", "TopBarWidth" },
            new[] { "10. Visor HUD", "CompassWidth" },
            new[] { "10. Visor HUD", "CompassHeight" },
            new[] { "10. Visor HUD", "CompassSpanDegrees" },
            new[] { "10. Visor HUD", "EquipBoxSize" },
            new[] { "10. Visor HUD", "EquipSpacing" },
            new[] { "10. Visor HUD", "HandBoxWidth" },
            new[] { "10. Visor HUD", "HandBoxHeight" },
            new[] { "10. Visor HUD", "VitalsWidth" },
            new[] { "10. Visor HUD", "VitalsHeight" },

            // "10. Visor HUD" — the four document-mode-dead typography sliders (LabelFontSize SURVIVES
            // — HandBoxesWidget still reads it — so it is deliberately absent from this list).
            new[] { "10. Visor HUD", "ValueFontSize" },
            new[] { "10. Visor HUD", "CompassFontSize" },
            new[] { "10. Visor HUD", "BareWordFontSize" },
            new[] { "10. Visor HUD", "VitalsRowFontSize" },

            // "8. Radial Visuals" — the legacy ImGui radial renderer's gate + a pre-existing dead key.
            new[] { "8. Radial Visuals", "UseUnityRadial" },
            new[] { "8. Radial Visuals", "IconScale" }, // RadialIconScale — "LEGACY (unused since 0.3.1)"

            // "9. The Grid" — GridMode/DisplayMode: self-described "DEPRECATED - no longer used",
            // zero readers anywhere (GridModel.ActiveMode is hard-wired to Grid).
            new[] { "9. The Grid", "DisplayMode" },
        };

        /// <summary>v1-&gt;v2 rename: legacy <c>HudGlitch</c>'s own master/severity/duration
        /// (<c>GlitchEnabled</c>/<c>GlitchIntensity</c>/<c>GlitchDuration</c>, "10. Visor HUD") folded
        /// onto the tri-state FX family (<see cref="UI.Hud.HudConfig.FxGlitchOn"/>/
        /// <see cref="UI.Hud.HudConfig.FxGlitchAmt"/>/<see cref="UI.Hud.HudConfig.FxGlitchDuration"/>,
        /// "11. HUD Effects (0.9.0)") that <c>HudGlitch.Trigger</c>/<c>Fire</c> read directly now. Only
        /// carries a value over when the OLD key was actually present in the player's file (i.e. it
        /// isn't just the legacy default masquerading as a choice); for the master specifically, only
        /// when the surviving <c>FxGlitchOn</c> still holds ITS OWN default too, so a deliberate choice
        /// already made on the key that survives is never clobbered by the one being retired. See
        /// wave-b-removed-keys.md task B3 for the full rationale (including the differing value
        /// ranges/semantics that make this NOT a candidate for the generic delete list above).</summary>
        private static void MigrateGlitchKeys(ConfigFile cfg)
        {
            const string OldSection = "10. Visor HUD";

            // GlitchEnabled (old default false) -> FxGlitchOn (live tri-state master, default true).
            bool oldEnabled;
            if (TryReadOrphaned(cfg, OldSection, "GlitchEnabled", out oldEnabled))
            {
                ForceIfDefault(UI.Hud.HudConfig.FxGlitchOn, oldDefault: true, corrected: oldEnabled);
            }
            RemoveOrphaned(cfg, OldSection, "GlitchEnabled");

            // GlitchIntensity (0-1, default 0.85) -> FxGlitchAmt (0-2, default 1): straight copy, the
            // ranges overlap enough that a felt-severity rescale isn't needed.
            float oldIntensity;
            if (TryReadOrphaned(cfg, OldSection, "GlitchIntensity", out oldIntensity))
            {
                UI.Hud.HudConfig.FxGlitchAmt.Value = oldIntensity;
            }
            RemoveOrphaned(cfg, OldSection, "GlitchIntensity");

            // GlitchDuration (0.1-4, default 1.1) -> FxGlitchDuration (same range/default): direct copy.
            float oldDuration;
            if (TryReadOrphaned(cfg, OldSection, "GlitchDuration", out oldDuration))
            {
                UI.Hud.HudConfig.FxGlitchDuration.Value = oldDuration;
            }
            RemoveOrphaned(cfg, OldSection, "GlitchDuration");
        }

        /// <summary>BepInEx's own <c>ConfigFile.OrphanedEntries</c> — the dictionary of keys that
        /// exist in the .cfg but were NOT <c>Bind</c>-called this run, which is exactly why <c>Save()</c>
        /// silently writes a removed key straight back out forever — is a PRIVATE property on the
        /// BepInEx.dll this game ships (verified by reflecting its get-accessor's IL attributes:
        /// <c>Private</c>, not the public one upstream BepInEx documents; calling it directly is a
        /// straight CS1061 at this project's reference). Reflection sidesteps the C#-compile-time
        /// visibility check — the CLR itself does not restrict it for a full-trust in-process mod host
        /// — giving us the exact live dictionary <c>Save()</c> reads from, so a removal here is
        /// indistinguishable from what the public API would have done. Cached once; returns null
        /// (never throws) if a future BepInEx rebuild renames or removes the member — every caller
        /// already treats "can't touch this key" as a harmless no-op, so the step just stops deleting
        /// orphans rather than breaking anything.</summary>
        private static readonly PropertyInfo OrphanedEntriesProp =
            typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Dictionary<ConfigDefinition, string> GetOrphanedEntries(ConfigFile cfg)
        {
            try { return OrphanedEntriesProp != null ? OrphanedEntriesProp.GetValue(cfg, null) as Dictionary<ConfigDefinition, string> : null; }
            catch (Exception e) { UIALog.Warn("ConfigMigration: OrphanedEntries reflection failed: " + e.Message); return null; }
        }

        /// <summary>Reads the raw stored string for a key that is no longer <c>Bind</c>-called this
        /// run (so it lives in <see cref="GetOrphanedEntries"/>, not <c>Entries</c>) and converts it
        /// via BepInEx's own converter — the same one <c>ConfigEntry&lt;T&gt;</c> uses internally, so
        /// parsing exactly matches how the value was serialized. Returns false without touching
        /// <paramref name="value"/> if the key was never in the player's file at all (fresh installs,
        /// or anyone who never touched that section) — the caller does nothing in that case.</summary>
        private static bool TryReadOrphaned<T>(ConfigFile cfg, string section, string key, out T value)
        {
            value = default(T);
            try
            {
                Dictionary<ConfigDefinition, string> orphaned = GetOrphanedEntries(cfg);
                string raw;
                if (orphaned == null || !orphaned.TryGetValue(new ConfigDefinition(section, key), out raw)) return false;
                value = TomlTypeConverter.ConvertToValue<T>(raw);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("ConfigMigration: could not read orphaned key [" + section + "] " + key + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Deletes one dead key from the player's .cfg so it stops being written back out on
        /// the next <see cref="ConfigFile.Save"/> and stops reappearing in the SLP settings panel.
        /// Removing it from the live orphaned-entries dictionary (see <see cref="GetOrphanedEntries"/>)
        /// is enough — nothing is currently <c>Bind</c>-ing it this run, so it can't also be sitting in
        /// <c>Entries</c>. Harmless no-op if the key was never in the file.</summary>
        private static void RemoveOrphaned(ConfigFile cfg, string section, string key)
        {
            try
            {
                Dictionary<ConfigDefinition, string> orphaned = GetOrphanedEntries(cfg);
                if (orphaned != null) orphaned.Remove(new ConfigDefinition(section, key));
            }
            catch (Exception e)
            {
                UIALog.Warn("ConfigMigration: could not remove orphaned key [" + section + "] " + key + ": " + e.Message);
            }
        }

        /// <summary>Force <paramref name="entry"/> to <paramref name="corrected"/> ONLY if the player
        /// still holds the exact old default — i.e. they never chose a value. A deliberate default
        /// change then reaches existing players without stomping anyone who set it on purpose.</summary>
        private static void ForceIfDefault<T>(ConfigEntry<T> entry, T oldDefault, T corrected)
        {
            if (entry == null) return;
            if (Equals(entry.Value, oldDefault)) entry.Value = corrected;
        }
    }
}
