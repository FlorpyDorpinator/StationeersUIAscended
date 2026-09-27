using System;
using System.Collections.Generic;
using System.IO;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Tutorial;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// One-click repair of the mod's on-disk config tree (<c>BepInEx/config/StationeersUIMod/</c>) —
    /// F10 -> HUD -> Maintenance -> "Repair config folders".
    ///
    /// <para><b>Why this exists.</b> A playtester deleted parts of that tree WHILE THE GAME WAS
    /// RUNNING. Every store is fail-soft about that on its own (a missing folder reads as "no state
    /// yet", and every write path creates its folder immediately before writing), so nothing throws
    /// into a frame — but two things do NOT come back until a relaunch: the SHIPPED content that is
    /// seeded once per launch (UI themes via <see cref="HudProfileStore.SyncShipped"/>, Stow
    /// Profiles via <c>StowProfileStore.SeedShipped</c>), and any live document whose store has
    /// nothing dirty to autosave. This closes both without a restart.</para>
    ///
    /// <para><b>Strictly additive — which is why it needs no confirm.</b> Nothing here deletes,
    /// overwrites or resets: folders are created only when absent, live documents are written back
    /// under their own names, and the two seeders are the ordinary launch-time ones, which never
    /// touch a file the player made or edited. Contrast the "Restore shipped themes" nuke it sits
    /// beside, which is destructive and therefore two-click armed.</para>
    ///
    /// <para><b>Order matters.</b> Live documents are written BEFORE the seeders run. A player whose
    /// active UI theme exists only in memory (they deleted HudProfiles/ and have not touched F9
    /// since, so nothing is dirty and no autosave will ever fire) gets it back on disk under its own
    /// name first; otherwise SyncShipped would seed a pristine shipped copy over the name they were
    /// actually using, and a CUSTOM active profile would simply be lost at the next restart with the
    /// button having reported success.</para>
    ///
    /// <para>Wholly fail-soft, per step: one unwritable folder or one failing seeder is logged and
    /// skipped, never abandoning the rest of the repair. The single static
    /// (<see cref="LastResult"/>) is a plain string holding no Unity or game reference, so it needs
    /// no teardown hook — a stale label across a hot reload is harmless and the next run replaces
    /// it.</para>
    /// </summary>
    public static class ConfigTreeRepair
    {
        /// <summary>Short ASCII outcome of the most recent <see cref="Run"/> this session, or null
        /// before the button has ever been pressed. Single-sourced here so the transient toast and
        /// the persistent note in the F10 Maintenance block can never disagree.</summary>
        public static string LastResult { get; private set; }

        /// <summary>Recreate every missing folder in the config tree, write the live documents back
        /// to disk, then re-run the two shipped-content seeders. Returns how many folders were
        /// actually CREATED (0 = the tree was already intact); <see cref="LastResult"/> carries the
        /// player-facing wording. Never throws.</summary>
        public static int Run()
        {
            int created = 0;
            List<string> folders = Folders();
            for (int i = 0; i < folders.Count; i++)
            {
                // Per-folder fail-soft: one unwritable path must never abort the rest of the repair.
                try
                {
                    string dir = folders[i];
                    if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) continue;
                    Directory.CreateDirectory(dir);
                    created++;
                }
                catch (Exception e)
                {
                    UIALog.Warn("Config repair could not create '" + folders[i] + "': " + e.Message);
                }
            }

            // ---- live documents first (see the class remarks on ordering) ----
            try { HudProfileStore.SaveActiveNow(); }
            catch (Exception e) { UIALog.Warn("Config repair could not write the active UI Theme: " + e.Message); }

            try
            {
                // Only when the Stow Profile model is actually in charge: on the degraded legacy
                // path SaveProfiles writes Profiles/profiles.xml instead, which is already covered
                // by the folder pass above and must not be forced from here.
                if (StowProfileStore.Available)
                {
                    BagProfileStore.SaveProfiles();                          // active set back to disk
                    StowProfileStore.SetActive(StowProfileStore.ActiveName); // and rewrite the .active marker
                }
            }
            catch (Exception e) { UIALog.Warn("Config repair could not write the active Stow Profile: " + e.Message); }

            // ---- then the launch-time shipped-content seeders ----
            // Both are seed-if-absent and both are inert when their source is unavailable:
            // SyncShipped returns immediately with no mod folder (the F6 ScriptEngine dev flow),
            // and SeedShipped skips every name its .shipped marker already records.
            try { HudProfileStore.SyncShipped(global::StationeersUIMod.StationeersUIMod.ModDirectory); }
            catch (Exception e) { UIALog.Warn("Config repair could not re-seed the shipped UI Themes: " + e.Message); }

            try { StowProfileStore.SeedShipped(); }
            catch (Exception e) { UIALog.Warn("Config repair could not re-seed the shipped Stow Profiles: " + e.Message); }

            LastResult = created > 0
                ? "Config folders repaired (" + created + " created)."
                : "Config folders already intact.";
            UIALog.Info("Config folder repair: " + created + " folder(s) created; shipped content re-checked.");
            return created;
        }

        /// <summary>Every folder the mod keeps state in, config ROOT included (deleting the whole
        /// tree is the case this exists for, so the root itself must be recreatable).
        ///
        /// <para>Taken from each store's OWN path property wherever it exposes one, so a folder
        /// rename cannot drift from its owner. The five save-scoped stores keep their folder name in
        /// a private const, so those go through <see cref="SaveScopedXmlStore.DirFor"/> — the same
        /// path builder they use — with the literal each store declares, named in the comment beside
        /// it. Add a line here whenever a new store gains a folder.</para></summary>
        private static List<string> Folders()
        {
            return new List<string>
            {
                BagProfileStore.ConfigDir,                   // the tree root itself
                HudProfileStore.Dir,                         // HudProfiles/  (+ .shipped-manifest, preview .png)
                BagProfileStore.ProfilesDir,                 // Profiles/     (legacy store + share drop-box)
                StowProfileStore.Dir,                        // StowProfiles/ (+ .active/.shipped/.migrated)
                BagProfileStore.AssignmentsDir,              // Assignments/
                SaveScopedXmlStore.DirFor("Grid"),           // GridCollapseStore.StoreFolder
                SaveScopedXmlStore.DirFor("GridPins"),       // GridPinStore.StoreFolder
                SaveScopedXmlStore.DirFor("BeltBindings"),   // BeltBindingStore.StoreFolder
                SaveScopedXmlStore.DirFor("Hotkeys"),        // BagHotkeyStore.StoreFolder
                SaveScopedXmlStore.DirFor("HintUsage"),      // HintUsageStore.StoreFolder
                SaveScopedXmlStore.DirFor("StowHomes"),      // StowHomeStore.StoreFolder (per-world Simple SmartStow homes)
                LoadoutStore.LoadoutsDir,                    // Loadouts/
                HudIconStore.Dir,                            // HudIcons/
                // Profiling/ProfilicusUniversalis keeps its folder name in a private field and its
                // writer creates it on demand; the literal is repeated here so a wiped tree still
                // comes back complete.
                Path.Combine(BagProfileStore.ConfigDir, "ProfilerSnapshots"),
                TutorialTextStore.TutorialDir,               // Tutorial/
            };
        }
    }
}
