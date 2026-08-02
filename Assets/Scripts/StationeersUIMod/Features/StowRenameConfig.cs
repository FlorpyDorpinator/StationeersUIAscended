using BepInEx.Configuration;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The one config key SmartStow B3 adds: "rename the container when a Bag Profile is assigned"
    /// (FlorpyDorp's directive, redesign plan §14 — assigning a profile labels the bag exactly as a
    /// labeller would, through <see cref="ItemActions.RenameThing"/>).
    ///
    /// <para><b>Why it is not in <c>UIAConfig</c>.</b> That file was owned by a CONCURRENT work
    /// stream while B3 was built, and editing it would have collided. The key is bound here instead,
    /// against the very same <c>ConfigFile</c> — <see cref="ConfigEntryBase.ConfigFile"/> off an
    /// already-bound <c>UIAConfig</c> entry — so it lands in the same section of the same
    /// <c>.cfg</c> as if <c>UIAConfig.Bind</c> had written it, and moving the two lines back into
    /// <c>UIAConfig</c> later is a pure relocation with no key change and therefore no
    /// <c>ConfigMigration</c> step (adding a key never needs one; only removal/rename does —
    /// <c>Documentation/Config-and-Theme-Migration.md</c> §2).</para>
    ///
    /// <para>Bound lazily and idempotently: <c>ConfigFile.Bind</c> returns the existing entry when
    /// the definition is already present, so a double call (launch + first F10) is harmless.
    /// <see cref="BagProfileStore.LoadProfiles"/> calls <see cref="EnsureBound"/> at launch purely so
    /// the key APPEARS in the .cfg for a player who reads it, rather than materialising the first
    /// time the Storage tab is opened.</para>
    ///
    /// <para>Hot reload: the static entry reference dies with the assembly; <see cref="Reset"/>
    /// exists for an explicit teardown and is safe to call any number of times.</para>
    /// </summary>
    public static class StowRenameConfig
    {
        // Same section the rest of the Smart Stow chain lives in, so the key sorts with its family.
        private const string Section = "6. SmartStow+";

        private static ConfigEntry<bool> _renameOnAssign;

        /// <summary>Bind (once) against whatever ConfigFile UIAConfig was bound to. No-op and
        /// harmless before UIAConfig.Bind has run — the next caller simply tries again.</summary>
        public static void EnsureBound()
        {
            if (_renameOnAssign != null) return;
            try
            {
                var host = UIAConfig.MasterEnable;      // any already-bound entry knows its file
                if (host == null) return;
                ConfigFile cfg = host.ConfigFile;
                if (cfg == null) return;
                _renameOnAssign = cfg.Bind(Section, "RenameBagOnProfileAssign", true,
                    "Assigning a Bag Profile to a container also RENAMES it to that profile's name, " +
                    "exactly as if you had used a Labeller on it (same authoritative path, so it is " +
                    "multiplayer-safe and everyone sees the new name). Only containers you are " +
                    "carrying are ever renamed. Clearing a profile does NOT rename anything back - " +
                    "use the Rename box on the bag card in F10 > Storage > Bags.");
            }
            catch (System.Exception e)
            {
                UIALog.Warn("Could not bind RenameBagOnProfileAssign: " + e.Message);
            }
        }

        /// <summary>Rename a container when a Bag Profile is assigned to it? Defaults to ON —
        /// FlorpyDorp asked for the BEHAVIOUR and the checkbox is the escape hatch, not the
        /// feature. Reads false (feature off) if the key could not be bound at all.</summary>
        public static bool RenameOnAssign
        {
            get
            {
                EnsureBound();
                return _renameOnAssign != null && _renameOnAssign.Value;
            }
            set
            {
                EnsureBound();
                if (_renameOnAssign != null) _renameOnAssign.Value = value;
            }
        }

        /// <summary>False when the key could not be bound (UIAConfig not up yet) — the UI shows the
        /// switch disabled and says why rather than silently doing nothing.</summary>
        public static bool Available
        {
            get { EnsureBound(); return _renameOnAssign != null; }
        }

        /// <summary>Teardown hook (hot reload / shutdown): drop the entry reference so a reloaded
        /// assembly re-binds against the live ConfigFile instead of a dead one.</summary>
        public static void Reset()
        {
            _renameOnAssign = null;
        }
    }
}
