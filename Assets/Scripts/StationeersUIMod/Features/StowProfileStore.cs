using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model (SmartStow redesign plan §8.1, FlorpyDorp Q2/Q3) ----------

    /// <summary>
    /// ONE **Stow Profile**: a named folder of <see cref="BagProfile"/>s ("Bag Profiles"), stored as
    /// a single SELF-CONTAINED document at
    /// <c>config/StationeersUIMod/StowProfiles/&lt;name&gt;.xml</c>.
    ///
    /// <para>Exactly one Stow Profile is ACTIVE at a time (FlorpyDorp Q3). Only the active one's
    /// Bag Profiles are in <see cref="BagProfileStore.Profiles"/>, so profile names stay unqualified
    /// and nothing downstream — <c>Assignment</c>, <c>BagProfile.Match</c>, <c>StowRouter</c> — had
    /// to learn about sets. Switching Stow Profiles is a LOAD, not a merge.</para>
    ///
    /// <para>The child element is <c>&lt;BagProfile&gt;</c>, byte-identical in shape to the one in
    /// the legacy <see cref="BagProfileFile"/> drop-in format, so a shared/exported bag profile can
    /// be pasted straight into a Stow Profile file by hand (and
    /// <see cref="BagProfileStore.ImportSharedProfiles"/> does it for you).</para>
    /// </summary>
    [XmlRoot("StowProfile")]
    public class StowProfileDoc
    {
        [XmlAttribute("name")] public string Name;
        /// <summary>Document schema. Bumped only when the SHAPE changes; a document from a newer
        /// build is loaded as-is rather than rejected (unknown elements are simply dropped by
        /// XmlSerializer, exactly like every other store here).</summary>
        [XmlAttribute("schema")] public int Schema = StowProfileStore.CurrentSchema;
        /// <summary>Free text shown in the manager (B4). Optional — XmlSerializer omits it when
        /// null, so a hand-written file need not carry one.</summary>
        [XmlElement("Description")] public string Description;
        [XmlElement("BagProfile")] public List<BagProfile> Profiles = new List<BagProfile>();

        /// <summary>The file this document was loaded from (set by the loader, never serialized),
        /// so a hand-renamed file is written back in place instead of forked.</summary>
        [XmlIgnore] public string SourceFile;
    }

    // ---------- Store ----------

    /// <summary>
    /// Disk I/O, the ACTIVE choice, migration and cross-set transfers for Stow Profiles.
    ///
    /// <para><b>Where the active choice lives.</b> In a small marker file
    /// <c>StowProfiles/.active</c> (one line, the Stow Profile's display name) rather than a
    /// <c>.cfg</c> key. Reason: <c>UIAConfig.cs</c> was owned by a concurrent work stream when this
    /// landed and could not be edited. The marker is deliberately NOT a <c>.xml</c>, so it never
    /// shows up in the folder's own <c>*.xml</c> enumeration. If it later moves to a
    /// <c>StowActiveProfile</c> config key, that is an additive key (no ConfigMigration step is
    /// needed for an ADD — see Documentation/Config-and-Theme-Migration.md §2) plus a one-time read
    /// of this file.</para>
    ///
    /// <para><b>Fail-soft everywhere.</b> A missing folder, an unreadable document or a marker
    /// naming a Stow Profile that is gone must never break stowing: the caller
    /// (<see cref="BagProfileStore.LoadProfiles"/>) falls back to the pre-B2 legacy
    /// <c>Profiles/*.xml</c> loader, and a bad marker falls back to the first readable document.</para>
    ///
    /// <para><b>Statics.</b> Only the active document + a bool. Both are plain data (no Unity or
    /// game references), and both are dropped by <see cref="Reset"/>, which
    /// <see cref="BagProfileStore.ResetRuntimeCaches"/> calls from the mod's Shutdown path.</para>
    /// </summary>
    public static class StowProfileStore
    {
        public const int CurrentSchema = 1;

        /// <summary>The name migration gives the player's existing bag profiles (plan §12.1).</summary>
        public const string DefaultStowProfileName = "My Stow Profile";

        private const string ActiveMarkerFile = ".active";
        /// <summary>The migration stamp. Its EXISTENCE is the run-once gate — a FACT written on
        /// disk, never an inference from what the folder currently contains. Inferring the gate
        /// from "the folder holds a document" is re-openable: anything that removes the last
        /// document (a quarantine, a hand-delete, a failed sync) makes the next launch look like a
        /// first run and silently re-imports the FROZEN legacy <c>profiles.xml</c>, reverting the
        /// player to a months-old snapshot. Not a .xml, so it is never enumerated as a document.</summary>
        private const string MigrationStampFile = ".migrated";
        private const string BrokenMarker = ".broken";

        public static string Dir { get { return Path.Combine(BagProfileStore.ConfigDir, "StowProfiles"); } }

        // Path.GetInvalidFileNameChars() allocates a fresh char[] per call; hold it (same idiom
        // as BagProfileStore/LoadoutStore).
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

        private static StowProfileDoc _active;
        private static bool _available;

        /// <summary>True when the Stow Profile model is in charge (a document is loaded). False
        /// means the mod degraded to the legacy <c>Profiles/</c> folder and every write must go
        /// there instead.</summary>
        public static bool Available { get { return _available && _active != null; } }

        public static string ActiveName { get { return _active != null ? _active.Name : null; } }

        public static StowProfileDoc Active { get { return _active; } }

        /// <summary>The file the ACTIVE document was loaded from, or null. Read by
        /// <see cref="BagProfileStore.ImportSharedProfiles"/> for its newer-wins timestamp
        /// comparison.</summary>
        public static string ActiveFile { get { return _active != null ? _active.SourceFile : null; } }

        /// <summary>Hot-reload teardown (mod Shutdown, via
        /// <see cref="BagProfileStore.ResetRuntimeCaches"/>): drop the loaded document so a
        /// double-F6 re-resolves from disk instead of trusting a stale one.</summary>
        public static void Reset()
        {
            _active = null;
            _available = false;
        }

        // ---------- load / save ----------

        /// <summary>Resolve the ACTIVE Stow Profile and fill <paramref name="into"/> with its Bag
        /// Profiles. Runs the one-way migration first (idempotent, gated on the folder already
        /// holding a document). Returns false when the model is not usable at all — the caller then
        /// falls back to the legacy loader and nothing is lost.</summary>
        public static bool LoadActiveInto(List<BagProfile> into)
        {
            _active = null;
            _available = false;
            if (into == null) return false;
            try
            {
                // The enumeration must SUCCEED before anything is written. An exception here (a
                // locked folder, a sync client mid-operation) is indistinguishable from "the folder
                // is empty" if it is swallowed into an empty list — and "empty" is the input that
                // makes migration run and overwrite. Abort the whole attempt instead: the caller
                // degrades to the legacy loader for this launch and nothing on disk is touched.
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return false;

                if (EnsureMigrated(files))
                {
                    files = DocFiles(out ok);
                    if (!ok) return false;
                }
                if (files.Count == 0) return false;

                string want = ReadActiveMarker();
                StowProfileDoc doc = null;
                if (!string.IsNullOrEmpty(want)) doc = LoadByName(files, want, true);
                if (doc == null)
                {
                    doc = LoadFirstUsable(files);
                    if (doc != null && !string.IsNullOrEmpty(want))
                        UIALog.Warn("Stow Profile '" + want + "' is missing or unreadable - using '"
                            + doc.Name + "' instead.");
                }
                if (doc == null) return false;

                into.Clear();
                for (int i = 0; i < doc.Profiles.Count; i++)
                {
                    BagProfile p = doc.Profiles[i];
                    if (p != null && !string.IsNullOrEmpty(p.Name)) into.Add(p);
                }
                _active = doc;
                _available = true;
                if (!string.Equals(want, doc.Name, StringComparison.Ordinal)) WriteActiveMarker(doc.Name);
                UIALog.Info("Stow Profile '" + doc.Name + "': " + into.Count + " bag profile(s) loaded.");
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile load failed (falling back to the old Profiles folder): " + e.Message);
                _active = null;
                _available = false;
                return false;
            }
        }

        /// <summary>Write <paramref name="from"/> back into the ACTIVE document (the save path for
        /// every Bag Profile edit in the mod — <see cref="BagProfileStore.SaveProfiles"/> funnels
        /// here). The list is COPIED, never aliased, so a later <c>Profiles.Clear()</c> cannot empty
        /// the document behind our back.</summary>
        public static bool SaveActive(List<BagProfile> from)
        {
            if (_active == null) return false;
            _active.Profiles = from != null ? new List<BagProfile>(from) : new List<BagProfile>();
            return SaveDoc(_active);
        }

        /// <summary>Every Stow Profile's display name, file order (case-insensitive). Deserializes
        /// each document, so call it at GESTURE frequency only — the F10 tab caches the result and
        /// re-reads it on non-restyle builds, exactly like its worn-bag scan.</summary>
        public static List<string> ListNames()
        {
            var names = new List<string>();
            bool ok;
            List<string> files = DocFiles(out ok);
            for (int i = 0; i < files.Count; i++)
            {
                // Never quarantines: this is a pure listing (it runs on every non-restyle F10 build),
                // and a listing has no business renaming the player's files.
                StowProfileDoc doc = LoadDocFile(files[i], false);
                if (doc == null || string.IsNullOrEmpty(doc.Name)) continue;
                // Case-INSENSITIVE dedupe, matching how SetActive/LoadByName resolve a name: two
                // hand-made documents called "Printers" and "printers" both resolve to the first, so
                // listing them twice would offer a switch that silently lands somewhere else.
                if (!ContainsName(names, doc.Name)) names.Add(doc.Name);
            }
            return names;
        }

        /// <summary>Point the ACTIVE marker at <paramref name="name"/>. Does NOT swap the in-memory
        /// profile list — the caller follows with <see cref="BagProfileStore.LoadProfiles"/>, which
        /// re-resolves everything from disk (one code path for launch and for switching).</summary>
        public static bool SetActive(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool ok;
            StowProfileDoc doc = LoadByName(DocFiles(out ok), name, false);
            if (doc == null) return false;
            WriteActiveMarker(doc.Name);
            return true;
        }

        /// <summary>Create a new (empty, or copy-of-active) Stow Profile file. Returns its final
        /// name — de-collided against the names already on disk — or null on failure.</summary>
        public static string Create(string name, bool copyActive)
        {
            try
            {
                string wanted = ProfileCapture.SanitizeName(name);
                if (string.IsNullOrEmpty(wanted)) wanted = "Stow Profile";
                wanted = UniqueStowName(wanted);

                var doc = new StowProfileDoc { Name = wanted, Schema = CurrentSchema };
                if (copyActive && _active != null)
                    for (int i = 0; i < _active.Profiles.Count; i++)
                    {
                        BagProfile clone = CloneProfile(_active.Profiles[i]);
                        if (clone != null) doc.Profiles.Add(clone);
                    }
                if (!SaveDoc(doc)) return null;
                UIALog.Info("Created Stow Profile '" + wanted + "' (" + doc.Profiles.Count + " bag profile(s)).");
                return wanted;
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile create failed: " + e.Message);
                return null;
            }
        }

        // ---------- cross-set transfers (FlorpyDorp Q3: "mix and match") ----------

        /// <summary>COPY a Bag Profile out of the ACTIVE Stow Profile into another one. The source
        /// keeps its copy. Returns the name the copy landed under in the target ("Ores (2)" when
        /// that target already had an "Ores"), or null when nothing was copied. The manager UI that
        /// drives this is B4; the model op exists now so the two can be built and tested apart.</summary>
        public static string CopyProfileTo(string profileName, string targetStowName)
        {
            return TransferProfile(profileName, targetStowName, false);
        }

        /// <summary>MOVE a Bag Profile out of the ACTIVE Stow Profile into another one.
        ///
        /// <para>Deliberately does NOT run <c>BagProfileStore</c>'s rename/delete cascade: a moved
        /// profile is not a deleted one, so assignments, prefab defaults and loadout entries keep
        /// naming it. In the active set the name now resolves to nothing, which every stage already
        /// treats as "unassigned" (fail-soft, one log line — see
        /// <c>StowRouter.EffectiveAssignedName</c>); switching to the target Stow Profile makes
        /// those same assignments resolve again. Hide, never destroy.</para></summary>
        public static string MoveProfileTo(string profileName, string targetStowName)
        {
            return TransferProfile(profileName, targetStowName, true);
        }

        private static string TransferProfile(string profileName, string targetStowName, bool move)
        {
            if (!Available || string.IsNullOrEmpty(profileName) || string.IsNullOrEmpty(targetStowName))
                return null;
            if (string.Equals(targetStowName, ActiveName, StringComparison.OrdinalIgnoreCase))
                return null;   // same folder: nothing to do (and a "(2)" duplicate would be a bug, not a feature)
            try
            {
                BagProfile source = BagProfileStore.FindProfile(profileName);
                if (source == null) return null;

                bool ok;
                StowProfileDoc target = LoadByName(DocFiles(out ok), targetStowName, false);
                if (target == null) return null;

                string newName = UniqueNameIn(target, profileName);
                BagProfile copy = CloneProfile(source);
                if (copy == null) return null;
                copy.Name = newName;
                target.Profiles.Add(copy);
                if (!SaveDoc(target)) return null;

                if (move)
                {
                    BagProfileStore.Profiles.Remove(source);
                    BagProfileStore.SaveProfiles();
                }
                UIALog.Info((move ? "Moved" : "Copied") + " bag profile '" + profileName + "' to Stow Profile '"
                    + target.Name + "'" + (newName == profileName ? "." : " as '" + newName + "'."));
                return newName;
            }
            catch (Exception e)
            {
                UIALog.Warn("Bag profile transfer failed: " + e.Message);
                return null;
            }
        }

        /// <summary>"Ores" -> "Ores (2)" -> "Ores (3)"... first name free inside one document.
        /// (The in-set collider, <see cref="BagProfileStore.UniqueProfileName"/>, uses "Ores 2"
        /// instead — deliberately a different shape, so a name that gained a suffix by CROSSING sets
        /// is visually distinguishable from one that collided inside a set.)</summary>
        private static string UniqueNameIn(StowProfileDoc doc, string baseName)
        {
            if (doc == null || doc.Profiles == null) return baseName;
            if (!HasProfileNamed(doc, baseName)) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = baseName + " (" + n + ")";
                if (!HasProfileNamed(doc, candidate)) return candidate;
            }
            return baseName + " (" + DateTime.UtcNow.Ticks + ")";
        }

        private static bool HasProfileNamed(StowProfileDoc doc, string name)
        {
            for (int i = 0; i < doc.Profiles.Count; i++)
            {
                BagProfile p = doc.Profiles[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string UniqueStowName(string baseName)
        {
            List<string> taken = ListNames();
            if (!ContainsName(taken, baseName)) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = baseName + " (" + n + ")";
                if (!ContainsName(taken, candidate)) return candidate;
            }
            return baseName + " (" + DateTime.UtcNow.Ticks + ")";
        }

        /// <summary>Case-INSENSITIVE membership — for STOW PROFILE names, which resolve that way
        /// (and map to Windows file names, which are themselves case-insensitive).</summary>
        private static bool ContainsName(List<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Case-SENSITIVE membership — for BAG PROFILE names, whose identity is the raw
        /// string every Assignment stores and <see cref="BagProfileStore.FindProfile"/> compares
        /// ordinally.</summary>
        private static bool ContainsNameOrdinal(List<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Deep copy of a Bag Profile — rule objects included, so a transferred profile in
        /// one document can never share a mutable rule with the one left in another.</summary>
        private static BagProfile CloneProfile(BagProfile source)
        {
            if (source == null) return null;
            var copy = new BagProfile { Name = source.Name, Badge = source.Badge };
            for (int i = 0; i < source.Items.Count; i++)
            {
                ItemRule r = source.Items[i];
                if (r != null) copy.Items.Add(new ItemRule { Prefab = r.Prefab, Priority = r.Priority });
            }
            for (int i = 0; i < source.Categories.Count; i++)
            {
                CategoryRule r = source.Categories[i];
                if (r != null) copy.Categories.Add(new CategoryRule { Name = r.Name, Priority = r.Priority });
            }
            for (int i = 0; i < source.SlotClasses.Count; i++)
            {
                SlotClassRule r = source.SlotClasses[i];
                if (r != null) copy.SlotClasses.Add(new SlotClassRule { Name = r.Name, Priority = r.Priority });
            }
            for (int i = 0; i < source.UIAClasses.Count; i++)
            {
                UIAClassRule r = source.UIAClasses[i];
                if (r != null) copy.UIAClasses.Add(new UIAClassRule { Name = r.Name, Priority = r.Priority });
            }
            return copy;
        }

        // ---------- migration (plan §12.1) ----------

        /// <summary>
        /// ONE-WAY, run-once, fail-soft migration of the legacy flat store into a first Stow Profile
        /// named <see cref="DefaultStowProfileName"/>.
        ///
        /// <para><b>Gate:</b> the <c>.migrated</c> stamp file (see <see cref="MigrationStampFile"/>).
        /// A stamp is a FACT; "the folder holds a document" is an INFERENCE, and a re-openable one —
        /// quarantining or hand-deleting the last document would make the next launch look like a
        /// first run and silently re-import the frozen legacy <c>profiles.xml</c>, time-travelling
        /// the player back to a months-old snapshot with nothing but a first-run log line to show
        /// for it. With the stamp: a stamped-but-empty folder gets a fresh EMPTY Stow Profile
        /// instead, which is honest. A folder that already holds documents but predates the stamp
        /// (an early-B2 install) is ADOPTED — stamped, never re-migrated.</para>
        ///
        /// <para><b>The legacy files are left in place, untouched</b> (hide, never destroy). They
        /// simply stop being read: once a Stow Profile exists, <c>Profiles/*.xml</c> is dormant, so
        /// nothing is ever double-loaded. A player can still pull a shared file in from there
        /// deliberately with <see cref="BagProfileStore.ImportSharedProfiles"/>.</para>
        ///
        /// <para><b>Assignments are untouched and keep working</b>: they key on
        /// <c>ReferenceId -&gt; profileName</c>, and every name is migrated verbatim.</para>
        /// </summary>
        /// <returns>True when a document was WRITTEN (the caller must re-enumerate).</returns>
        private static bool EnsureMigrated(List<string> existing)
        {
            if (StampExists())
            {
                if (existing.Count > 0) return false;   // the normal path: already migrated
                // Stamped but empty: the player (or a quarantine) removed every Stow Profile. Give
                // them a fresh EMPTY one. Re-running the migration here is what would time-travel
                // them back to the frozen legacy snapshot, so it is exactly what must NOT happen.
                var blank = new StowProfileDoc
                {
                    Name = DefaultStowProfileName,
                    Schema = CurrentSchema,
                    Description = "Empty - every Stow Profile that was here has been removed.",
                };
                if (!SaveDoc(blank)) return false;
                WriteActiveMarker(blank.Name);
                UIALog.Warn("No Stow Profiles were left on disk; created an empty '" + blank.Name
                    + "'. Your old Profiles folder was NOT re-imported (use Import shared profiles if you want it back).");
                return true;
            }
            if (existing.Count > 0)
            {
                // An early-B2 folder from before the stamp existed: adopt it as migrated. Never
                // re-migrate over documents that are already there.
                WriteStamp("adopted");
                return false;
            }

            var carried = new List<BagProfile>();
            bool fromLegacy = false;
            try
            {
                // quarantine: TRUE — a legacy file that will not parse is copied aside as
                // <name>.broken.xml before we move on, so a hand-edit with one bad tag is
                // recoverable instead of silently absent from the new Stow Profile. This read
                // happens exactly once, so the copy cannot pile up.
                bool readFailure;
                fromLegacy = BagProfileStore.LoadLegacyInto(carried, false, true, out readFailure) > 0;
                if (readFailure)
                {
                    // A legacy file exists but could not be opened. Migration is stamped and
                    // one-shot, so carrying a knowingly-partial set forward would drop those
                    // profiles forever. Write nothing, stamp nothing, retry next launch; the caller
                    // degrades to the legacy loader for now, which is exactly the old behaviour.
                    UIALog.Warn("Postponing the Stow Profile migration: part of the Profiles folder could not be read this launch.");
                    return false;
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not read the old Profiles folder during migration: " + e.Message);
                return false;
            }
            if (!fromLegacy)
            {
                carried.Clear();
                carried.AddRange(BagProfileStore.BuildStarterProfiles());
            }

            var doc = new StowProfileDoc
            {
                Name = DefaultStowProfileName,
                Schema = CurrentSchema,
                Description = fromLegacy
                    ? "Your existing bag profiles. The old Profiles folder is left in place but is no longer read."
                    : "Starter bag profiles.",
                Profiles = carried,
            };
            if (!SaveDoc(doc)) return false;    // failed write: caller degrades to the legacy loader
            WriteActiveMarker(doc.Name);
            WriteStamp(fromLegacy ? "migrated" : "seeded");
            UIALog.Info("Created your first Stow Profile '" + doc.Name + "' with " + carried.Count
                + " bag profile(s)" + (fromLegacy
                    ? " carried over from the Profiles folder (which is left untouched)."
                    : " (starter set)."));
            return true;
        }

        private static string StampPath { get { return Path.Combine(Dir, MigrationStampFile); } }

        private static bool StampExists()
        {
            try { return File.Exists(StampPath); }
            catch { return false; }
        }

        /// <summary>Write the run-once stamp. Deliberately written AFTER the first document, so a
        /// failure between the two re-runs the migration (safe, idempotent) rather than skipping it
        /// forever (data stranded). Contents are informational only — existence is the gate.</summary>
        private static void WriteStamp(string how)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(StampPath,
                    "schema=" + CurrentSchema + " how=" + how + " utc="
                    + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not stamp the Stow Profile migration: " + e.Message);
            }
        }

        // ---------- file plumbing ----------

        /// <summary>Every loadable document file in the folder, name order. Quarantine copies
        /// (<c>x.broken.xml</c>, <c>x.broken-2.xml</c>) are never listed.
        /// <paramref name="ok"/> distinguishes "the folder is empty" from "the folder could not be
        /// read" — the two are the same empty list, but only one of them may be allowed to look
        /// like a first run to <see cref="EnsureMigrated"/>.</summary>
        private static List<string> DocFiles(out bool ok)
        {
            ok = true;
            var files = new List<string>();
            try
            {
                if (!Directory.Exists(Dir)) return files;   // genuinely absent: a real first run
                string[] found = Directory.GetFiles(Dir, "*.xml");
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < found.Length; i++)
                    if (!IsBrokenBackupName(Path.GetFileNameWithoutExtension(found[i])))
                        files.Add(found[i]);
            }
            catch (Exception e)
            {
                ok = false;
                files.Clear();
                UIALog.Warn("Stow Profile folder unreadable: " + e.Message);
            }
            return files;
        }

        /// <param name="forUse">True only on the path that resolves the document we are about to
        /// LOAD AND USE — the one place a genuinely corrupt file may be quarantined. Pure listings
        /// and lookups pass false so they can never rename a player's file.</param>
        private static StowProfileDoc LoadByName(List<string> files, string name, bool forUse)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Fast path: the file name we would have written it under.
            string token = SafeFileToken(name);
            if (token.Length > 0)
            {
                string direct = Path.Combine(Dir, token + ".xml");
                for (int i = 0; i < files.Count; i++)
                    if (string.Equals(files[i], direct, StringComparison.OrdinalIgnoreCase))
                    {
                        StowProfileDoc hit = LoadDocFile(files[i], forUse);
                        if (hit != null && string.Equals(hit.Name, name, StringComparison.OrdinalIgnoreCase)) return hit;
                        break;
                    }
            }
            // Slow path: a hand-renamed file still counts, its <StowProfile name=".."> wins.
            for (int i = 0; i < files.Count; i++)
            {
                StowProfileDoc doc = LoadDocFile(files[i], forUse);
                if (doc != null && string.Equals(doc.Name, name, StringComparison.OrdinalIgnoreCase)) return doc;
            }
            return null;
        }

        private static StowProfileDoc LoadFirstUsable(List<string> files)
        {
            for (int i = 0; i < files.Count; i++)
            {
                StowProfileDoc doc = LoadDocFile(files[i], true);
                if (doc != null) return doc;
            }
            return null;
        }

        /// <summary>Deserialize one document, repairing it in memory.
        ///
        /// <para>A file that will not PARSE is preserved as a <c>&lt;name&gt;.broken.xml</c> COPY —
        /// the <c>HudProfileStore.BackupCorruptProfile</c> convention — and only when
        /// <paramref name="allowQuarantine"/> says this is the load-for-use path. A file that merely
        /// would not OPEN (an <c>IOException</c> from a sync client or scanner holding it for a
        /// moment, a permission blip) is skipped for this pass and left completely alone: renaming a
        /// healthy document because of a transient lock would remove it from the folder, and a
        /// folder that loses its last document is exactly the state the migration stamp exists to
        /// keep harmless.</para></summary>
        private static StowProfileDoc LoadDocFile(string path, bool allowQuarantine)
        {
            StowProfileDoc doc;
            Exception error;
            if (!SaveScopedXmlStore.TryLoad(path, out doc, out error))
            {
                if (error != null)
                {
                    bool broken = SaveScopedXmlStore.IsParseFailure(error);
                    UIALog.Warn("Stow Profile file '" + Path.GetFileName(path) + "' could not be read"
                        + (broken ? " (bad XML): " : " (skipped this time): ") + error.Message);
                    if (broken && allowQuarantine) QuarantineFile(path, false);
                }
                return null;
            }
            Sanitize(doc, path);
            return doc;
        }

        /// <summary>Fail-soft repair of a loaded document: a usable name, a non-null profile list,
        /// no null/nameless entries, and no duplicate names (a duplicate is RENAMED, never dropped —
        /// losing a player's rules to a name clash would be the worst possible "repair").</summary>
        private static void Sanitize(StowProfileDoc doc, string path)
        {
            if (doc == null) return;
            doc.SourceFile = path;
            if (doc.Profiles == null) doc.Profiles = new List<BagProfile>();
            if (doc.Schema <= 0) doc.Schema = CurrentSchema;

            string name = ProfileCapture.SanitizeName(doc.Name);
            if (string.IsNullOrEmpty(name))
                name = ProfileCapture.SanitizeName(Path.GetFileNameWithoutExtension(path ?? string.Empty));
            if (string.IsNullOrEmpty(name)) name = "Stow Profile";
            doc.Name = name;

            var seen = new List<string>();
            for (int i = doc.Profiles.Count - 1; i >= 0; i--)
            {
                BagProfile p = doc.Profiles[i];
                if (p == null || string.IsNullOrEmpty(p.Name)) { doc.Profiles.RemoveAt(i); continue; }
                if (p.Items == null) p.Items = new List<ItemRule>();
                if (p.Categories == null) p.Categories = new List<CategoryRule>();
                if (p.SlotClasses == null) p.SlotClasses = new List<SlotClassRule>();
                if (p.UIAClasses == null) p.UIAClasses = new List<UIAClassRule>();
            }
            for (int i = 0; i < doc.Profiles.Count; i++)
            {
                BagProfile p = doc.Profiles[i];
                // ORDINAL, matching BagProfileStore.FindProfile (and therefore every Assignment
                // lookup). Deduping case-INSENSITIVELY here would rename a legitimate "ores" that
                // sits beside "Ores" — a pair that works perfectly all session, because the router
                // resolves them ordinally — and the rename would land at the NEXT launch, silently
                // dangling whatever was assigned to it.
                if (!ContainsNameOrdinal(seen, p.Name)) { seen.Add(p.Name); continue; }
                string fixedName = p.Name;
                for (int n = 2; n < 1000 && ContainsNameOrdinal(seen, fixedName); n++)
                    fixedName = p.Name + " (" + n + ")";
                UIALog.Warn("Stow Profile '" + doc.Name + "' had two bag profiles called '" + p.Name
                    + "'; the second is now '" + fixedName + "'.");
                p.Name = fixedName;
                seen.Add(fixedName);
            }
        }

        /// <summary>Write a document. For a NEW one the file name is probed for a free slot rather
        /// than assumed: display names are de-collided, but file names are a lossy projection of
        /// them (<see cref="SafeFileToken"/> maps "A/B" and "A_B" onto one token) and a hand-renamed
        /// file legitimately has a name unrelated to its display name — both of which let a blind
        /// <c>File.Create</c> overwrite an unrelated Stow Profile.</summary>
        private static bool SaveDoc(StowProfileDoc doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.Name)) return false;
            string path = doc.SourceFile;
            if (string.IsNullOrEmpty(path))
            {
                string token = SafeFileToken(doc.Name);
                if (token.Length == 0) token = "stow-profile";
                path = FreeFilePath(token);
                if (path == null) return false;
            }
            if (!SaveScopedXmlStore.Save(path, doc, "Stow Profile")) return false;
            doc.SourceFile = path;
            return true;
        }

        /// <summary>"Printers.xml", then "Printers-2.xml", "Printers-3.xml"... first path that does
        /// not already exist. Null only if the folder cannot be probed at all.</summary>
        private static string FreeFilePath(string token)
        {
            try
            {
                string first = Path.Combine(Dir, token + ".xml");
                if (!File.Exists(first)) return first;
                for (int n = 2; n < 1000; n++)
                {
                    string candidate = Path.Combine(Dir, token + "-" + n + ".xml");
                    if (!File.Exists(candidate)) return candidate;
                }
                return Path.Combine(Dir, token + "-" + DateTime.UtcNow.Ticks + ".xml");
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not pick a file name for Stow Profile '" + token + "': " + e.Message);
                return null;
            }
        }

        private static string ReadActiveMarker()
        {
            try
            {
                string path = Path.Combine(Dir, ActiveMarkerFile);
                if (!File.Exists(path)) return null;
                string raw = File.ReadAllText(path);
                return raw != null ? raw.Trim() : null;
            }
            catch { return null; }
        }

        private static void WriteActiveMarker(string name)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, ActiveMarkerFile), name ?? string.Empty);
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not record the active Stow Profile: " + e.Message);
            }
        }

        // ---------- quarantine (the existing .broken.xml convention) ----------

        /// <summary>Does this file BASENAME name a quarantine copy — <c>x.broken</c> or
        /// <c>x.broken-7</c>? Mirrors <c>HudProfileStore.IsBrokenBackupName</c> so both folders use
        /// one convention.</summary>
        internal static bool IsBrokenBackupName(string fileBaseName)
        {
            if (string.IsNullOrEmpty(fileBaseName)) return false;
            int i = fileBaseName.LastIndexOf(BrokenMarker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            string tail = fileBaseName.Substring(i + BrokenMarker.Length);
            if (tail.Length == 0) return true;              // "<name>.broken"
            if (tail[0] != '-' || tail.Length < 2) return false;
            for (int k = 1; k < tail.Length; k++)
                if (!char.IsDigit(tail[k])) return false;
            return true;                                     // "<name>.broken-2"
        }

        /// <summary>HIDE, NEVER DESTROY for a file whose XML will not parse: keep a
        /// <c>&lt;name&gt;.broken.xml</c> COPY beside it and leave the original exactly where it is.
        ///
        /// <para>Copy, never move: the original is the player's file, it is the only evidence of
        /// what they had, and removing it from the folder is a destructive act to take on the word
        /// of one failed parse. The cost of leaving it is that the next launch parses it again — so
        /// the copy is made ONCE (if <c>&lt;name&gt;.broken.xml</c> already exists we have already
        /// preserved it and do nothing), which stops the <c>-2</c>, <c>-3</c>, <c>-4</c> churn a
        /// blind numbering scheme would produce on every start-up.</para>
        ///
        /// <para><paramref name="move"/> is kept for the (currently unused) case of a store that
        /// rewrites a fresh file over the bad one, where the original really must leave the
        /// enumeration; it numbers repeats like <c>HudProfileStore</c> does.</para>
        /// Wholly fail-soft.</summary>
        internal static void QuarantineFile(string path, bool move)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string dir = Path.GetDirectoryName(path);
                string baseName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(dir) || IsBrokenBackupName(baseName)) return;

                string dst = Path.Combine(dir, baseName + BrokenMarker + ".xml");
                if (File.Exists(dst))
                {
                    if (!move) return;   // already preserved; do not pile up copies of the same file
                    dst = null;
                    for (int n = 2; n <= 20 && dst == null; n++)
                    {
                        string candidate = Path.Combine(dir, baseName + BrokenMarker + "-" + n + ".xml");
                        if (!File.Exists(candidate)) dst = candidate;
                    }
                    if (dst == null)
                        dst = Path.Combine(dir, baseName + BrokenMarker + "-" + DateTime.UtcNow.Ticks + ".xml");
                }

                if (move) File.Move(path, dst);
                else File.Copy(path, dst);
                UIALog.Warn("'" + Path.GetFileName(path) + "' could not be read; a copy was kept as '"
                    + Path.GetFileName(dst) + "'.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not quarantine the unreadable file '" + path + "': " + e.Message);
            }
        }

        // ---------- helpers ----------

        /// <summary>Substitute OS-illegal filename characters so a Stow Profile called "A/B" maps to
        /// a stable file name. The display name inside the document stays authoritative.</summary>
        private static string SafeFileToken(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string name = raw;
            foreach (char c in InvalidNameChars) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
