using System;
using System.IO;
using System.Xml;
using BepInEx.Configuration;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>The two Smart Stow modes (0.9.8.0). Player-facing labels are Simple / Complex
    /// (FlorpyDorp 2026-09-25, mockup 3): <b>Simple</b> = "back where you took it from"
    /// (instance homes, <see cref="StowHomeStore"/>); <b>Complex</b> = today's Bag/Stow Profile
    /// system, intact.</summary>
    public enum StowMode
    {
        Simple,
        Complex,
    }

    /// <summary>
    /// The Simple-SmartStow config keys (plan §9.1). Bound LAZILY against the same
    /// <c>ConfigFile</c> UIAConfig was bound to — the <see cref="StowRenameConfig"/> idiom, copied
    /// exactly, because <c>UIAConfig.cs</c> is owned by a concurrent work stream: the keys land in
    /// the same sections of the same .cfg as if <c>UIAConfig.Bind</c> had written them, and moving
    /// them into <c>UIAConfig</c> later is a pure relocation with no key change and therefore no
    /// <c>ConfigMigration</c> step (adding a key never needs one — only removal/rename does,
    /// Documentation/Config-and-Theme-Migration.md §2).
    ///
    /// <para>In THIS (observe-only) phase nothing reads <see cref="Mode"/> to change behaviour —
    /// the keys exist so the S-8C migration step can stamp existing installs and so the .cfg shows
    /// them from launch. The P2 resolver phase branches on <see cref="Mode"/>.</para>
    ///
    /// <para>CLAUDE.md rule 8: these are BEHAVIOUR settings with no HUD visual — shared, not
    /// per-tier, and deliberately not part of any UI theme (the <c>RadialShiftKeepsOpen</c>
    /// precedent: a theme must never flip a player's muscle memory).</para>
    /// </summary>
    public static class StowModeConfig
    {
        // Same section the rest of the Smart Stow chain lives in, so the keys sort with family.
        private const string Section = "6. SmartStow+";
        private const string InternalSection = "0. Internal";

        private static ConfigEntry<StowMode> _mode;
        private static ConfigEntry<bool> _simpleNotes;
        private static ConfigEntry<bool> _homeFirstInComplex;
        private static ConfigEntry<bool> _noticeShown;

        /// <summary>Warn-once latch: EnsureBound is called from per-frame paths (the resolver and
        /// the ghost hint go through the Mode accessor at up to ~4 Hz), so a persistent bind
        /// failure must log ONCE per session, not spam the disk. Cleared by <see cref="Reset"/> so
        /// a hot reload may report again.</summary>
        private static bool _bindWarned;

        /// <summary>Bind (once) against whatever ConfigFile UIAConfig was bound to. No-op and
        /// harmless before UIAConfig.Bind has run — the next caller simply tries again.
        /// Idempotent: <c>ConfigFile.Bind</c> returns the existing entry when already present.</summary>
        public static void EnsureBound()
        {
            if (_mode != null) return;
            try
            {
                ConfigEntry<bool> host = UIAConfig.MasterEnable;   // any bound entry knows its file
                if (host == null) return;
                ConfigFile cfg = host.ConfigFile;
                if (cfg == null) return;

                _mode = cfg.Bind(Section, "Mode", StowMode.Simple,
                    "Which Smart Stow (G) system runs. Simple: every item goes back to the bag and "
                    + "slot you last put it in - move it by hand and its home moves; new items are "
                    + "routed once and that landing becomes their home. Complex: the Bag Profile / "
                    + "Storage Layout rule system (everything under F10 > Storage). Switching modes "
                    + "loses nothing - both keep their data on disk.");
                _simpleNotes = cfg.Bind(Section, "SimpleNotes", true,
                    "Show short tips in Simple mode, only for the exceptions: 'no home yet - put in "
                    + "X' and 'home full - put in X'. They teach how homes work; switch off any time.");
                _homeFirstInComplex = cfg.Bind(Section, "HomeFirstInComplex", false,
                    "Complex mode only: check an item's remembered Simple-mode home BEFORE the Bag "
                    + "Profile rules run. Off by default - profiles stay fully in charge unless you "
                    + "opt in.");
                _noticeShown = cfg.Bind(InternalSection, "SmartStowModeNoticeShown", false,
                    "Internal: the one-time Smart Stow mode notice after the 0.9.8.0 update has been "
                    + "shown. Do not edit.");
            }
            catch (Exception e)
            {
                if (!_bindWarned)
                {
                    _bindWarned = true;
                    UIALog.Warn("Could not bind SmartStow mode keys: " + e.Message);
                }
            }
        }

        /// <summary>The active Smart Stow mode. Reads Simple (the shipped default) when the keys
        /// could not bind at all.</summary>
        public static StowMode Mode
        {
            get { EnsureBound(); return _mode != null ? _mode.Value : StowMode.Simple; }
            set { EnsureBound(); if (_mode != null) _mode.Value = value; }
        }

        /// <summary>Simple-mode exception toasts ("no home yet - put in X"). Default ON.</summary>
        public static bool SimpleNotes
        {
            get { EnsureBound(); return _simpleNotes == null || _simpleNotes.Value; }
            set { EnsureBound(); if (_simpleNotes != null) _simpleNotes.Value = value; }
        }

        /// <summary>Complex-mode opt-in "home first" stage (plan P5a). Default OFF.</summary>
        public static bool HomeFirstInComplex
        {
            get { EnsureBound(); return _homeFirstInComplex != null && _homeFirstInComplex.Value; }
            set { EnsureBound(); if (_homeFirstInComplex != null) _homeFirstInComplex.Value = value; }
        }

        /// <summary>Has the one-time post-update mode notice been shown?</summary>
        public static bool ModeNoticeShown
        {
            get { EnsureBound(); return _noticeShown != null && _noticeShown.Value; }
            set { EnsureBound(); if (_noticeShown != null) _noticeShown.Value = value; }
        }

        /// <summary>False when the keys could not be bound (UIAConfig not up yet) — UI surfaces
        /// show the switch disabled and say why rather than silently doing nothing.</summary>
        public static bool Available
        {
            get { EnsureBound(); return _mode != null; }
        }

        /// <summary>
        /// The S-8C evidence-based migration for EXISTING installs (plan §9.3, decision locked
        /// 2026-09-25), called from the matching <see cref="Core.ConfigMigration"/> step: an install
        /// that has ever assigned a Bag Profile to a container — any <c>&lt;Assign&gt;</c> element in
        /// any <c>Assignments/*.xml</c> — keeps today's behaviour (Complex); everyone else gets the
        /// new Simple default. Any IO/parse failure counts as evidence FOR Complex (that is today's
        /// behaviour, the safe direction). Fresh installs never get here — ConfigMigration stamps
        /// them to current and runs no steps, so they keep the Simple default.
        /// </summary>
        public static void MigrateExistingInstall()
        {
            EnsureBound();
            if (_mode == null) return;   // could not bind; nothing to stamp (fail-soft)

            bool complex = false;
            try
            {
                string dir = BagProfileStore.AssignmentsDir;
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.xml");
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            XmlDocument doc = new XmlDocument();
                            doc.Load(files[i]);
                            if (doc.GetElementsByTagName("Assign").Count > 0)
                            {
                                complex = true;
                                break;
                            }
                        }
                        catch
                        {
                            complex = true;   // unreadable evidence → keep today's behaviour
                            break;
                        }
                    }
                }
            }
            catch
            {
                complex = true;               // could not even look → keep today's behaviour
            }

            if (complex)
            {
                _mode.Value = StowMode.Complex;
                UIALog.Info("SmartStow migration: existing Bag Profile assignments found - Mode stays Complex.");
            }
            else
            {
                UIALog.Info("SmartStow migration: no Bag Profile assignments - Mode defaults to Simple.");
            }
        }

        /// <summary>Teardown hook (hot reload / shutdown): drop the entry references so a reloaded
        /// assembly re-binds against the live ConfigFile instead of a dead one.</summary>
        public static void Reset()
        {
            _mode = null;
            _simpleNotes = null;
            _homeFirstInComplex = null;
            _noticeShown = null;
            _bindWarned = false;
        }
    }
}
