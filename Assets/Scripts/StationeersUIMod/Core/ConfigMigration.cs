using System;
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
        public const int CurrentVersion = 1;

        private const string Section = "0. Internal";

        public static ConfigEntry<int> Version;

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
                // ---------------------------------------------------------------------------------
                // TEMPLATE — the next time you change a shipped default or rename a key, bump
                // CurrentVersion to 2 and fill in this case (then add case 2 for the one after, etc.):
                //
                // case 1: // v1 -> v2
                //     // A default was WRONG: push the corrected value only to players who never
                //     // changed it (anyone who set it on purpose keeps their choice).
                //     ForceIfDefault(UI.Hud.HudConfig.FxGlow, oldDefault: 0.50f, corrected: 0.80f);
                //
                //     // A key was RENAMED: carry the old value across. (Rename, never repurpose —
                //     // reusing a key with new meaning silently misreads the stored value.)
                //     // Bind the old key to read its stored value, copy it to the new entry:
                //     // var old = cfg.Bind("10. Visor HUD", "OldKey", 0f, "(migrated)");
                //     // if (!Equals(old.Value, 0f)) UI.Hud.HudConfig.NewKey.Value = old.Value;
                //     break;
                // ---------------------------------------------------------------------------------
                default:
                    break; // baseline / unknown source version → no-op
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
