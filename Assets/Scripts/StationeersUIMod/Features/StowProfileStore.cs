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
        /// <summary>The SHIPPED-PRESET marker (B4). One line per shipped set name we have ever
        /// handed this player, <c>&lt;name&gt;|&lt;revision&gt;</c>. Its presence — not the folder's
        /// contents — is the seed gate, which is what makes "delete it and it STAYS deleted" true.
        /// Not a .xml, so it is never enumerated as a document.</summary>
        private const string ShippedMarkerFile = ".shipped";
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

        /// <summary>Bumped on every successful Stow Profile document write, active or not (inside
        /// <see cref="SaveDoc"/>, the one write funnel every path here — migration, seeding,
        /// transfers, and the FlorpyDorp 2026-09-25 inactive-set editors below — already goes
        /// through). Purely a cheap "did anything change on disk" signal for UI that caches a
        /// listing (the F10 Organizer) to diff against, rather than re-deserializing every document
        /// every frame. Reset to 0 in <see cref="Reset"/> so a hot-reloaded mod does not confuse a
        /// fresh session with a stale one.</summary>
        public static int EditVersion;

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
            EditVersion = 0;
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
                // Shipped presets are seeded AFTER migration, never before: EnsureMigrated decides
                // "is this a first run?" from whether the folder holds a document, so seeding first
                // would make every legacy player look already-migrated and strand their old
                // Profiles/ folder. Seeding never touches the ACTIVE marker either — a returning
                // player stays on the Stow Profile they were using.
                if (SeedShipped() > 0)
                {
                    files = DocFiles(out ok);
                    if (!ok) return false;
                }
                if (files.Count == 0) return false;

                string want = ReadActiveMarker();
                StowProfileDoc doc = null;
                if (!string.IsNullOrEmpty(want)) doc = LoadByName(files, want, true);
                // Belt-and-braces for the 2026-09-26 shipped-layout rename: SeedShipped (above) has
                // already re-pointed a .active that named the OLD shipped name, but if only that one
                // marker write failed, follow the layout to its new name rather than dropping the
                // player onto whichever document happens to sort first.
                if (doc == null && string.Equals(want, ShippedStowProfiles.LegacyAscendedName, StringComparison.OrdinalIgnoreCase))
                    doc = LoadByName(files, ShippedStowProfiles.Ascended, true);
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

        /// <summary>One row of the B4 manager list: everything the set list shows without having to
        /// re-open the document per cell.</summary>
        public sealed class StowSetInfo
        {
            public string Name;
            public string Description;
            public int ProfileCount;
            public int RuleCount;
            public bool IsActive;
            public bool IsShipped;
        }

        /// <summary>Every Stow Profile on disk with the counts the manager renders. Deserializes each
        /// document, so this is a GESTURE-frequency call — the F10 tab caches the result and skips it
        /// entirely on a theme-restyle build, exactly like its worn-bag scan.
        ///
        /// <para>The ACTIVE set reports from <see cref="BagProfileStore.Profiles"/> (the live list)
        /// rather than from its file, so an unsaved-to-disk edit can never make the manager disagree
        /// with the editor one tab over.</para></summary>
        public static List<StowSetInfo> ListSets()
        {
            var rows = new List<StowSetInfo>();
            bool ok;
            List<string> files = DocFiles(out ok);
            string active = ActiveName;
            for (int i = 0; i < files.Count; i++)
            {
                StowProfileDoc doc = LoadDocFile(files[i], false);
                if (doc == null || string.IsNullOrEmpty(doc.Name)) continue;
                if (IndexOfName(rows, doc.Name) >= 0) continue;   // case-insensitive dedupe, as ListNames
                bool isActive = Available && string.Equals(doc.Name, active, StringComparison.OrdinalIgnoreCase);
                var row = new StowSetInfo
                {
                    Name = doc.Name,
                    Description = doc.Description,
                    IsActive = isActive,
                    IsShipped = IsShippedName(doc.Name),
                };
                List<BagProfile> list = isActive ? BagProfileStore.Profiles : doc.Profiles;
                row.ProfileCount = list != null ? list.Count : 0;
                if (list != null)
                    for (int p = 0; p < list.Count; p++)
                        if (list[p] != null) row.RuleCount += list[p].RuleCount;
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The Bag Profiles inside one Stow Profile — the ACTIVE one served live from
        /// <see cref="BagProfileStore.Profiles"/>, any other read from its file. Returns an empty
        /// list (never null) for an unknown/unreadable name.</summary>
        public static List<BagProfile> ProfilesOf(string setName)
        {
            if (string.IsNullOrEmpty(setName)) return new List<BagProfile>();
            if (Available && string.Equals(setName, ActiveName, StringComparison.OrdinalIgnoreCase))
                return new List<BagProfile>(BagProfileStore.Profiles);
            bool ok;
            StowProfileDoc doc = LoadByName(DocFiles(out ok), setName, false);
            if (doc == null || doc.Profiles == null) return new List<BagProfile>();
            return new List<BagProfile>(doc.Profiles);
        }

        /// <summary>Is there a Stow Profile with this display name on disk?</summary>
        public static bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return ContainsName(ListNames(), name);
        }

        private static int IndexOfName(List<StowSetInfo> rows, string name)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
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

        /// <summary>Rename a Stow Profile (B4). The FILE keeps its old name; only the
        /// <c>&lt;StowProfile name=".."&gt;</c> attribute changes, because the display name is the
        /// authority everywhere (<see cref="LoadByName"/> falls through to a by-name scan for exactly
        /// this case) and renaming files is the one part of this that can lose a document. A later
        /// "Restore shipped Stow Profiles" therefore lands in a FRESH file rather than clobbering the
        /// renamed one — <see cref="FreeFilePath"/> probes for a free slot.
        ///
        /// <para>Bag-profile ASSIGNMENTS are untouched: they name Bag Profiles, not Stow Profiles.
        /// Only the ACTIVE marker points at a Stow Profile by name, and it is re-pointed here.</para>
        /// Returns the final (sanitised) name, or null when nothing was renamed.</summary>
        public static string RenameSet(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName)) return null;
            string wanted = ProfileCapture.SanitizeName(newName);
            if (string.IsNullOrEmpty(wanted)) return null;
            if (string.Equals(wanted, oldName, StringComparison.Ordinal)) return null;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return null;
                // Collision check must ignore the document we are renaming (a case-only rename —
                // "starter" -> "Starter" — is legitimate and must not trip its own name).
                List<string> taken = ListNames();
                for (int i = taken.Count - 1; i >= 0; i--)
                    if (string.Equals(taken[i], oldName, StringComparison.OrdinalIgnoreCase)) taken.RemoveAt(i);
                if (ContainsName(taken, wanted)) return null;

                StowProfileDoc doc = LoadByName(files, oldName, false);
                if (doc == null) return null;
                doc.Name = wanted;
                if (!SaveDoc(doc)) return null;
                bool wasActive = string.Equals(oldName, ReadActiveMarker(), StringComparison.OrdinalIgnoreCase);
                if (wasActive) WriteActiveMarker(wanted);
                if (_active != null && string.Equals(_active.Name, oldName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(_active.SourceFile, doc.SourceFile, StringComparison.OrdinalIgnoreCase))
                    _active.Name = wanted;   // keep the in-memory active document in step
                UIALog.Info("Renamed Stow Profile '" + oldName + "' -> '" + wanted + "'.");
                return wanted;
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile rename failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Delete a Stow Profile FILE (B4). Refuses to delete the last one — a folder with no
        /// documents is the state the migration stamp exists to keep harmless, and there is no reason
        /// to walk a player into it from a button.
        ///
        /// <para>Deleting the ACTIVE set re-points the marker at another document; the caller must
        /// follow with <see cref="BagProfileStore.LoadProfiles"/> (one load path, as with a switch).
        /// A shipped set deleted here STAYS deleted: the <c>.shipped</c> marker already records that
        /// we gave it to this player, and seeding only ever consults the marker.</para>
        /// Returns true when a file was removed.</summary>
        public static bool DeleteSet(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return false;
                StowProfileDoc doc = LoadByName(files, name, false);
                if (doc == null || string.IsNullOrEmpty(doc.SourceFile)) return false;
                if (ListNames().Count <= 1) return false;   // never leave the folder empty

                bool wasActive = Available && string.Equals(doc.Name, ActiveName, StringComparison.OrdinalIgnoreCase);
                File.Delete(doc.SourceFile);
                UIALog.Info("Deleted Stow Profile '" + doc.Name + "'.");
                if (wasActive)
                {
                    _active = null;
                    _available = false;
                    List<string> left = ListNames();
                    if (left.Count > 0) WriteActiveMarker(left[0]);
                }
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile delete failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Write a document that came from OUTSIDE (a pasted share code, B4) as a brand new
        /// Stow Profile. Never overwrites and never switches the active set — import always lands as
        /// an inert new file the player then chooses to use (plan SS9.4). Returns the de-collided name
        /// it landed under, or null.</summary>
        public static string ImportDoc(StowProfileDoc doc)
        {
            if (doc == null) return null;
            try
            {
                doc.SourceFile = null;                    // force a fresh file, never an overwrite
                Sanitize(doc, null);
                doc.Name = UniqueStowName(doc.Name);
                doc.Schema = CurrentSchema;
                if (!SaveDoc(doc)) return null;
                UIALog.Info("Imported Stow Profile '" + doc.Name + "' (" + doc.Profiles.Count
                    + " bag profile(s)).");
                return doc.Name;
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile import failed: " + e.Message);
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
            return TransferProfile(null, profileName, targetStowName, false);
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
            return TransferProfile(null, profileName, targetStowName, true);
        }

        /// <summary>
        /// The general cross-set transfer B4's manager drives: copy/move ONE Bag Profile from any
        /// Stow Profile to any other. <paramref name="sourceStowName"/> null/empty means the ACTIVE
        /// one (which is what B2's <see cref="CopyProfileTo"/>/<see cref="MoveProfileTo"/> pass).
        ///
        /// <para><b>Either end may be the ACTIVE set, and that changes which store owns the write.</b>
        /// The active set's Bag Profiles live in <see cref="BagProfileStore.Profiles"/>, NOT in a
        /// document we may re-read: loading its file into a second <see cref="StowProfileDoc"/> and
        /// saving that would leave <c>_active</c> stale, and the next in-editor save would silently
        /// throw the transfer away. So the active end always goes through
        /// <see cref="BagProfileStore.SaveProfiles"/> and every other end through
        /// <see cref="SaveDoc"/>.</para>
        ///
        /// <para>MOVE deliberately does NOT run the rename/delete cascade (see
        /// <see cref="MoveProfileTo"/>): a moved profile is not a deleted one.</para>
        /// Returns the name the profile landed under in the target ("Ores (2)" on a collision), or
        /// null when nothing was transferred.</summary>
        public static string TransferProfile(string sourceStowName, string profileName,
            string targetStowName, bool move)
        {
            bool sourceKept;
            return TransferProfile(sourceStowName, profileName, targetStowName, move, out sourceKept);
        }

        /// <summary>As <see cref="TransferProfile(string,string,string,bool)"/>, but reports the one
        /// outcome a bare name cannot: a MOVE whose copy landed and whose SOURCE REMOVAL then failed
        /// to write. That is not a failure (the profile is safely in the target) and not a clean move
        /// either — the profile now exists in BOTH sets, and on the next load the source's copy comes
        /// straight back. Swallowing it made a move look successful while quietly duplicating the
        /// profile, so the flag exists purely so the UI can say so out loud.
        ///
        /// <para><paramref name="sourceKept"/> is true ONLY in that case: copy written, source not.
        /// A total failure still returns null with the flag false.</para></summary>
        public static string TransferProfile(string sourceStowName, string profileName,
            string targetStowName, bool move, out bool sourceKept)
        {
            sourceKept = false;
            if (string.IsNullOrEmpty(profileName) || string.IsNullOrEmpty(targetStowName)) return null;
            string source = string.IsNullOrEmpty(sourceStowName) ? ActiveName : sourceStowName;
            if (string.IsNullOrEmpty(source)) return null;
            if (string.Equals(source, targetStowName, StringComparison.OrdinalIgnoreCase))
                return null;   // same folder: nothing to do (and a "(2)" duplicate would be a bug, not a feature)
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return null;

                bool sourceIsActive = Available && string.Equals(source, ActiveName, StringComparison.OrdinalIgnoreCase);
                bool targetIsActive = Available && string.Equals(targetStowName, ActiveName, StringComparison.OrdinalIgnoreCase);

                StowProfileDoc sourceDoc = null;
                BagProfile from;
                if (sourceIsActive)
                {
                    from = BagProfileStore.FindProfile(profileName);
                }
                else
                {
                    sourceDoc = LoadByName(files, source, false);
                    if (sourceDoc == null) return null;
                    from = FindProfileIn(sourceDoc, profileName);
                }
                if (from == null) return null;

                BagProfile copy = CloneProfile(from);
                if (copy == null) return null;

                string newName;
                if (targetIsActive)
                {
                    newName = UniqueNameInList(BagProfileStore.Profiles, profileName);
                    copy.Name = newName;
                    BagProfileStore.Profiles.Add(copy);
                    if (!BagProfileStore.SaveProfiles())
                    {
                        BagProfileStore.Profiles.Remove(copy);   // do not leave a half-done transfer in memory
                        return null;
                    }
                }
                else
                {
                    StowProfileDoc target = LoadByName(files, targetStowName, false);
                    if (target == null) return null;
                    newName = UniqueNameIn(target, profileName);
                    copy.Name = newName;
                    target.Profiles.Add(copy);
                    if (!SaveDoc(target)) return null;
                }

                if (move)
                {
                    // The copy is on disk. The source-side removal is a SECOND write and can fail on
                    // its own (read-only file, full disk, a locked handle) — ignoring its result was
                    // how a "move" silently became a duplicate that reappeared on the next load.
                    bool removed;
                    if (sourceIsActive)
                    {
                        int at = BagProfileStore.Profiles.IndexOf(from);
                        if (at >= 0) BagProfileStore.Profiles.RemoveAt(at);
                        removed = BagProfileStore.SaveProfiles();
                        // Memory must never outrun disk: if the write failed the file still holds
                        // this profile, so the in-memory list has to hold it too (at its old index,
                        // so the shelf does not reorder itself) — otherwise the next unrelated save
                        // would quietly finish a move the player was told had failed.
                        if (!removed && at >= 0) BagProfileStore.Profiles.Insert(at, from);
                    }
                    else
                    {
                        sourceDoc.Profiles.Remove(from);
                        removed = SaveDoc(sourceDoc);
                        // sourceDoc is a throwaway load, so a failed write leaves nothing stale in
                        // memory — the untouched file on disk IS the surviving truth.
                    }
                    if (!removed)
                    {
                        sourceKept = true;
                        UIALog.Warn("Moved bag profile '" + profileName + "' into Stow Profile '" + targetStowName
                            + "', but could not remove it from '" + source + "' - it now exists in both.");
                        return newName;
                    }
                }
                UIALog.Info((move ? "Moved" : "Copied") + " bag profile '" + profileName + "' from Stow Profile '"
                    + source + "' to '" + targetStowName + "'"
                    + (newName == profileName ? "." : " as '" + newName + "'."));
                return newName;
            }
            catch (Exception e)
            {
                UIALog.Warn("Bag profile transfer failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Bulk COPY of EVERY Bag Profile in one Stow Profile into another — the "duplicate a
        /// set" path. One load of each end, one write at the end.
        ///
        /// <para>WHY THIS EXISTS rather than a loop over <see cref="TransferProfile"/>: that call is
        /// self-contained by design (it re-reads both ends and re-serializes the target every time),
        /// which is right for one profile off a shelf and quadratic for a whole set — duplicating the
        /// shipped 14-profile "By Printer" set meant fourteen full re-loads and fourteen full XML
        /// writes of a document that grew with each pass. Here the clones are staged into the target
        /// document in memory, colliding against what has ALREADY been staged (so two source profiles
        /// that both want "Ores" still land as "Ores" and "Ores (2)"), and the file is written once.</para>
        ///
        /// <para>Copy only, never move: the caller is duplicating, and a half-written bulk MOVE is a
        /// far worse failure mode than a duplicate. Returns how many profiles landed (0 on any
        /// failure — a failed write stages nothing, and the active-set branch rolls its in-memory
        /// additions back so memory never outruns disk).</para></summary>
        public static int TransferMany(string sourceStowName, string targetStowName)
        {
            if (string.IsNullOrEmpty(targetStowName)) return 0;
            string source = string.IsNullOrEmpty(sourceStowName) ? ActiveName : sourceStowName;
            if (string.IsNullOrEmpty(source)) return 0;
            if (string.Equals(source, targetStowName, StringComparison.OrdinalIgnoreCase)) return 0;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return 0;

                bool sourceIsActive = Available && string.Equals(source, ActiveName, StringComparison.OrdinalIgnoreCase);
                bool targetIsActive = Available && string.Equals(targetStowName, ActiveName, StringComparison.OrdinalIgnoreCase);

                // Snapshot the source first: if the target IS the active set we are about to append to
                // that very list, and iterating it while it grows would copy our own copies.
                List<BagProfile> from;
                if (sourceIsActive) from = new List<BagProfile>(BagProfileStore.Profiles);
                else
                {
                    StowProfileDoc sourceDoc = LoadByName(files, source, false);
                    if (sourceDoc == null || sourceDoc.Profiles == null) return 0;
                    from = new List<BagProfile>(sourceDoc.Profiles);
                }
                if (from.Count == 0) return 0;

                StowProfileDoc target = null;
                List<BagProfile> into;
                if (targetIsActive) into = BagProfileStore.Profiles;
                else
                {
                    target = LoadByName(files, targetStowName, false);
                    if (target == null) return 0;
                    into = target.Profiles;
                }

                int mark = into.Count;
                int added = 0;
                for (int i = 0; i < from.Count; i++)
                {
                    BagProfile src = from[i];
                    if (src == null || string.IsNullOrEmpty(src.Name)) continue;
                    BagProfile copy = CloneProfile(src);
                    if (copy == null) continue;
                    copy.Name = UniqueNameInList(into, src.Name);
                    into.Add(copy);
                    added++;
                }
                if (added == 0) return 0;

                bool saved = targetIsActive ? BagProfileStore.SaveProfiles() : SaveDoc(target);
                if (!saved)
                {
                    if (targetIsActive && into.Count > mark) into.RemoveRange(mark, into.Count - mark);
                    return 0;
                }
                UIALog.Info("Copied " + added + " bag profile(s) from Stow Profile '" + source
                    + "' to '" + targetStowName + "'.");
                return added;
            }
            catch (Exception e)
            {
                UIALog.Warn("Bulk bag profile transfer failed: " + e.Message);
                return 0;
            }
        }

        // ---------- editing an INACTIVE set (F10 Organizer, FlorpyDorp ruling 2026-09-25) ----------
        //
        // Until now a non-active Stow Profile's document was read-only outside of the whole-set
        // operations above (transfer/rename-the-set/delete-the-set). The Organizer needs to edit
        // whichever layout is being BROWSED, and only a "Use" button (SetActive) should switch which
        // one is live. The six calls below are that editor surface.
        //
        // THE ACTIVE-SET RULE, once, for all six: none of them may touch the ACTIVE Stow Profile.
        // Its Bag Profiles live in BagProfileStore.Profiles, not in a document this store may load
        // fresh from disk — that in-memory list is the only truth, an unsaved edit there is real and
        // this store cannot see it, and a load-here/save-here pair would either miss it or, worse,
        // get raced by the next BagProfileStore.SaveProfiles()/SaveActive() and have its own write
        // silently overwritten. So every one of these calls checks IsActive(setName) first and backs
        // off with null/false; the caller routes an active-set edit through BagProfileStore instead
        // (BagProfileStore.FindProfile/RenameProfile/Profiles.Add/SaveProfiles, or SaveActive).

        /// <summary>Is <paramref name="setName"/> the ACTIVE Stow Profile? The shared guard for
        /// every editor below — see the region comment for why they all refuse the active set.</summary>
        private static bool IsActive(string setName)
        {
            return Available && !string.IsNullOrEmpty(setName)
                && string.Equals(setName, ActiveName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Append a COPY of <paramref name="profile"/> to an INACTIVE Stow Profile's
        /// document — de-collided against what that document already has (the set-crossing "(N)"
        /// shape used by <see cref="TransferProfile"/>, via the same <see cref="UniqueNameInList"/>).
        /// Returns the final name it landed under, or null on any failure, including when
        /// <paramref name="setName"/> resolves to the ACTIVE set (see the region comment above) — the
        /// caller must add to the active set through <c>BagProfileStore.Profiles</c> +
        /// <c>BagProfileStore.SaveProfiles</c> instead.</summary>
        public static string AddProfileTo(string setName, BagProfile profile)
        {
            if (string.IsNullOrEmpty(setName) || profile == null || string.IsNullOrEmpty(profile.Name)) return null;
            if (IsActive(setName)) return null;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return null;
                StowProfileDoc doc = LoadByName(files, setName, false);
                if (doc == null) return null;
                BagProfile copy = CloneProfile(profile);
                if (copy == null) return null;
                string finalName = UniqueNameIn(doc, profile.Name);
                copy.Name = finalName;
                doc.Profiles.Add(copy);
                if (!SaveDoc(doc)) return null;
                UIALog.Info("Added bag profile '" + finalName + "' to Stow Profile '" + doc.Name + "'.");
                return finalName;
            }
            catch (Exception e)
            {
                UIALog.Warn("Add bag profile to Stow Profile failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Remove ONE Bag Profile from an INACTIVE Stow Profile's document, matched
        /// ORDINALLY (Bag Profile identity is the raw string, exactly as
        /// <see cref="BagProfileStore.FindProfile"/> compares it — never case-insensitive).
        ///
        /// <para>No cascade: an inactive set's Bag Profiles have no live assignments (assignments key
        /// on <c>ReferenceId -&gt; profileName</c> and are resolved against whichever Stow Profile is
        /// ACTIVE), so removing a name here cannot dangle anything today. If the removed set later
        /// becomes active, its assignments simply resolve as "unassigned" for that name — the same
        /// fail-soft <c>StowRouter.EffectiveAssignedName</c> path a deleted/renamed active profile
        /// already takes.</para>
        /// False on any failure, including when <paramref name="setName"/> is the ACTIVE set (see the
        /// region comment above).</summary>
        public static bool RemoveProfileFrom(string setName, string profileName)
        {
            if (string.IsNullOrEmpty(setName) || string.IsNullOrEmpty(profileName)) return false;
            if (IsActive(setName)) return false;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return false;
                StowProfileDoc doc = LoadByName(files, setName, false);
                if (doc == null) return false;
                BagProfile hit = FindProfileIn(doc, profileName);
                if (hit == null) return false;
                doc.Profiles.Remove(hit);
                if (!SaveDoc(doc)) return false;
                UIALog.Info("Removed bag profile '" + profileName + "' from Stow Profile '" + doc.Name + "'.");
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Remove bag profile from Stow Profile failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Rename ONE Bag Profile inside an INACTIVE Stow Profile's document. The new name
        /// is sanitized via <see cref="ProfileCapture.SanitizeName"/>, then collision-checked
        /// ORDINALLY against every OTHER profile already in that same document (a case-only rename —
        /// "ores" -&gt; "Ores" — is legitimate, mirroring how <see cref="RenameSet"/> excludes its own
        /// old name from its collision check).
        ///
        /// <para>No cascade, for the same reason as <see cref="RemoveProfileFrom"/>: an inactive set's
        /// profile names carry no live assignments to fix up.</para>
        /// Returns the final (sanitized) name, or null on any failure — including a collision, a
        /// missing <paramref name="oldName"/>, or when <paramref name="setName"/> is the ACTIVE set
        /// (see the region comment above; route that rename through
        /// <see cref="BagProfileStore.RenameProfile"/>, which owns the live cascade).</summary>
        public static string RenameProfileIn(string setName, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(setName) || string.IsNullOrEmpty(oldName)) return null;
            if (IsActive(setName)) return null;
            string wanted = ProfileCapture.SanitizeName(newName);
            if (string.IsNullOrEmpty(wanted)) return null;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return null;
                StowProfileDoc doc = LoadByName(files, setName, false);
                if (doc == null) return null;
                BagProfile hit = FindProfileIn(doc, oldName);
                if (hit == null) return null;
                if (!string.Equals(oldName, wanted, StringComparison.Ordinal))
                {
                    for (int i = 0; i < doc.Profiles.Count; i++)
                    {
                        BagProfile p = doc.Profiles[i];
                        if (p != null && p != hit && string.Equals(p.Name, wanted, StringComparison.Ordinal))
                            return null;   // collision inside the same document
                    }
                }
                hit.Name = wanted;
                if (!SaveDoc(doc)) return null;
                UIALog.Info("Renamed bag profile '" + oldName + "' -> '" + wanted + "' in Stow Profile '"
                    + doc.Name + "'.");
                return wanted;
            }
            catch (Exception e)
            {
                UIALog.Warn("Rename bag profile in Stow Profile failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Overwrite ONE Bag Profile inside an INACTIVE Stow Profile's document IN PLACE —
        /// replace-by-Name, ordinal. The incoming <paramref name="updated"/> is deep-cloned before it
        /// is stored, so the caller's own edit-buffer instance (an F10 Organizer field group, say)
        /// stays independent of what was just saved. This is a REPLACE, not an add-or-replace: a
        /// name that is not already present fails — use <see cref="AddProfileTo"/> for a new one.
        /// False on any failure, including when <paramref name="setName"/> is the ACTIVE set (see the
        /// region comment above).</summary>
        public static bool ReplaceProfileIn(string setName, BagProfile updated)
        {
            if (string.IsNullOrEmpty(setName) || updated == null || string.IsNullOrEmpty(updated.Name)) return false;
            if (IsActive(setName)) return false;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return false;
                StowProfileDoc doc = LoadByName(files, setName, false);
                if (doc == null) return false;
                int at = -1;
                for (int i = 0; i < doc.Profiles.Count; i++)
                {
                    BagProfile p = doc.Profiles[i];
                    if (p != null && string.Equals(p.Name, updated.Name, StringComparison.Ordinal)) { at = i; break; }
                }
                if (at < 0) return false;   // replace-by-name: absent means no-op, not an add
                BagProfile copy = CloneProfile(updated);
                if (copy == null) return false;
                doc.Profiles[at] = copy;
                if (!SaveDoc(doc)) return false;
                UIALog.Info("Updated bag profile '" + updated.Name + "' in Stow Profile '" + doc.Name + "'.");
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Replace bag profile in Stow Profile failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Deep clone of ONE Bag Profile read out of an INACTIVE Stow Profile's document —
        /// a safe working copy for an editor to mutate; nothing is written back until a later
        /// <see cref="ReplaceProfileIn"/> call. Null on any failure, including when
        /// <paramref name="setName"/> is the ACTIVE set (see the region comment above — read the live
        /// copy via <see cref="BagProfileStore.FindProfile"/> against <c>BagProfileStore.Profiles</c>
        /// instead, since that in-memory list, not this store's document, is that set's truth).</summary>
        public static BagProfile GetProfileCopy(string setName, string profileName)
        {
            if (string.IsNullOrEmpty(setName) || string.IsNullOrEmpty(profileName)) return null;
            if (IsActive(setName)) return null;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return null;
                StowProfileDoc doc = LoadByName(files, setName, false);
                if (doc == null) return null;
                BagProfile hit = FindProfileIn(doc, profileName);
                return hit != null ? CloneProfile(hit) : null;
            }
            catch (Exception e)
            {
                UIALog.Warn("Read bag profile from Stow Profile failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Set the free-text <c>&lt;Description&gt;</c> (the B4/F10 manager blurb) of an
        /// INACTIVE Stow Profile's document. An empty/null <paramref name="description"/> clears it
        /// (XmlSerializer then omits the element entirely, matching a hand-written file). False on
        /// any failure, including when <paramref name="setName"/> is the ACTIVE set (see the region
        /// comment above) — there is deliberately no active-set path for this one either, since the
        /// active document's own <c>Description</c> field is otherwise untouched by any editor
        /// today.</summary>
        public static bool SetDescription(string setName, string description)
        {
            if (string.IsNullOrEmpty(setName)) return false;
            if (IsActive(setName)) return false;
            try
            {
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return false;
                StowProfileDoc doc = LoadByName(files, setName, false);
                if (doc == null) return false;
                doc.Description = string.IsNullOrEmpty(description) ? null : description;
                if (!SaveDoc(doc)) return false;
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Set Stow Profile description failed: " + e.Message);
                return false;
            }
        }

        private static BagProfile FindProfileIn(StowProfileDoc doc, string name)
        {
            if (doc == null || doc.Profiles == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < doc.Profiles.Count; i++)
            {
                BagProfile p = doc.Profiles[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        /// <summary>"Ores" -> "Ores (2)" -> "Ores (3)"... first name free inside one document.
        /// (The in-set collider, <see cref="BagProfileStore.UniqueProfileName"/>, uses "Ores 2"
        /// instead — deliberately a different shape, so a name that gained a suffix by CROSSING sets
        /// is visually distinguishable from one that collided inside a set.)</summary>
        private static string UniqueNameIn(StowProfileDoc doc, string baseName)
        {
            if (doc == null || doc.Profiles == null) return baseName;
            return UniqueNameInList(doc.Profiles, baseName);
        }

        /// <summary>The set-CROSSING collider, over any Bag Profile list (a document's, or the active
        /// in-memory one).</summary>
        private static string UniqueNameInList(List<BagProfile> list, string baseName)
        {
            if (list == null) return baseName;
            if (!HasProfileNamed(list, baseName)) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = baseName + " (" + n + ")";
                if (!HasProfileNamed(list, candidate)) return candidate;
            }
            return baseName + " (" + DateTime.UtcNow.Ticks + ")";
        }

        private static bool HasProfileNamed(List<BagProfile> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                BagProfile p = list[i];
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

        // ---------- shipped presets (B4) ----------

        /// <summary>
        /// SEED-IF-ABSENT delivery of the four shipped preset Stow Profiles
        /// (<see cref="ShippedStowProfiles"/>). Runs once per set, ever.
        ///
        /// <para><b>The gate is the <c>.shipped</c> marker, not the folder's contents.</b> A set whose
        /// name appears in that file is never written again — so a player who deletes "By Printer"
        /// keeps it deleted, and one who edits "Starter" keeps their edit. That is the whole
        /// difference between this and <c>HudProfileStore.SyncShipped</c>, which hashes every file so
        /// it can also REFRESH and PRUNE. The simpler scheme is a deliberate tradeoff: a Stow Profile
        /// that is missing or edited degrades gracefully (the router just falls through to affinity
        /// and bag defaults), whereas a missing UI theme leaves a player with no HUD. The cost is
        /// that shipped content never updates itself — the explicit
        /// <see cref="RestoreShipped"/> button is the only way a player takes a newer vintage.</para>
        ///
        /// <para>A name that is ALREADY on disk when we first look (the player made their own
        /// "Starter", or an older build seeded it) is ADOPTED into the marker rather than overwritten
        /// — recorded at revision 0 so it reads as "not ours" in a bug report.</para>
        ///
        /// <para><b>A renamed shipped set is migrated FIRST</b> (<see cref="MigrateRenamedShipped"/>),
        /// inside this method rather than beside it, so BOTH callers — the launch path
        /// (<see cref="LoadActiveInto"/>) and F10's "Repair config folders"
        /// (<c>ConfigTreeRepair.Run</c>) — can never seed a fresh copy under the new name ahead of
        /// the rename (which would leave the player with a pristine duplicate beside their edited
        /// original).</para>
        /// </summary>
        /// <returns>How many documents were written, a rename migration included (the caller must
        /// re-enumerate when &gt; 0).</returns>
        internal static int SeedShipped()
        {
            int seeded = 0;
            int migrated = 0;
            try
            {
                Dictionary<string, int> marker = ReadShippedMarker();
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return 0;
                List<string> present = null;
                bool dirty = false;

                bool holdAscended;
                migrated = MigrateRenamedShipped(marker, files, ref dirty, out holdAscended);
                if (migrated > 0)
                {
                    files = DocFiles(out ok);
                    if (!ok)
                    {
                        // The rename landed; record it and let the caller re-enumerate.
                        WriteShippedMarker(marker);
                        return migrated;
                    }
                }

                for (int i = 0; i < ShippedStowProfiles.Names.Length; i++)
                {
                    string name = ShippedStowProfiles.Names[i];
                    if (marker.ContainsKey(name)) continue;   // handed over before: never again
                    // The rename could not be settled this pass (the old document is unreadable
                    // right now, or its rewrite failed). Seeding "Ascended" now would strand the
                    // player's edited original beside a pristine copy — wait for the next pass.
                    if (holdAscended && string.Equals(name, ShippedStowProfiles.Ascended, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (present == null)
                    {
                        present = new List<string>();
                        for (int f = 0; f < files.Count; f++)
                        {
                            StowProfileDoc existing = LoadDocFile(files[f], false);
                            if (existing != null && !string.IsNullOrEmpty(existing.Name)) present.Add(existing.Name);
                        }
                    }
                    if (ContainsName(present, name))
                    {
                        marker[name] = 0;                     // adopt: it is theirs, not ours
                        dirty = true;
                        continue;
                    }

                    StowProfileDoc doc = ShippedStowProfiles.Build(name);
                    if (doc == null) continue;
                    if (!SaveDoc(doc)) continue;              // write failed: retry next launch
                    marker[name] = ShippedStowProfiles.Revision;
                    present.Add(name);
                    dirty = true;
                    seeded++;
                }
                if (dirty) WriteShippedMarker(marker);
                if (seeded > 0)
                    UIALog.Info("Seeded " + seeded + " shipped Stow Profile(s). Delete any of them and they stay deleted.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Shipped Stow Profile seeding failed: " + e.Message);
            }
            return seeded + migrated;
        }

        /// <summary>
        /// ONE-TIME rename of the shipped layout "Stationpedia Ascended" to "Ascended"
        /// (FlorpyDorp 2026-09-26: the long name ellipsized in the F10 layout cards). Seeding is
        /// keyed BY NAME, so without this an existing player would simply be handed a second,
        /// pristine "Ascended" beside the "Stationpedia Ascended" they may have edited and be using.
        ///
        /// <para><b>The gate is the <c>.shipped</c> marker</b>, like seeding itself: it acts only
        /// while the marker records the OLD name and has never heard of the new one. Every settled
        /// branch removes the old key and leaves the new one recorded (here, or by
        /// <see cref="SeedShipped"/>'s adopt-at-0 in the same pass), so it cannot re-open; an
        /// unsettled branch changes nothing and sets <paramref name="holdNewName"/> so the caller
        /// seeds nothing under the new name this pass.</para>
        ///
        /// <list type="bullet">
        /// <item><b>Old on disk, new absent</b> — RENAME, preserving every player edit: the loaded
        /// document is re-saved IN PLACE with the new internal name (one atomic
        /// <c>File.Replace</c> via <see cref="SaveDoc"/> — the only step that matters; on failure
        /// nothing has changed and the rename retries next pass). Then, cosmetically, the file is
        /// moved to <c>Ascended.xml</c> when it still carries the name we gave it and that file name
        /// is free (a failed move leaves a correctly-named document under its old file name, which
        /// the by-name loader already handles). <c>.active</c> is re-pointed when it named the old
        /// layout, the in-memory active document is kept in step (the Repair-config path can run
        /// this mid-session), and the marker entry moves to the new name at the SAME revision.</item>
        /// <item><b>Both on disk</b> (the player made their own "Ascended") — clobber nothing: the old
        /// document becomes an ordinary player-owned layout (its marker key is dropped), their
        /// "Ascended" is adopted at revision 0 by the seeding loop, one warning is logged.</item>
        /// <item><b>Old absent</b> — the player deleted it, and a deleted shipped set STAYS deleted:
        /// the marker entry moves to the new name so "Ascended" is never seeded. (If they happen to
        /// own an "Ascended", the seeding loop adopts it at 0 instead.) If a file with the old
        /// file name is present but unreadable this pass, nothing is decided (hold).</item>
        /// </list>
        /// Fail-soft: an exception is logged and holds the new name for this pass (the caller's own
        /// catch never sees it). Every half-done state self-heals on the next pass: a document
        /// already rewritten to the new name is found by name, the old key is dropped, and the
        /// seeding loop adopts it rather than seeding over it.
        /// </summary>
        /// <returns>1 when a document was rewritten (the caller must re-enumerate), else 0.</returns>
        private static int MigrateRenamedShipped(Dictionary<string, int> marker, List<string> files,
            ref bool markerDirty, out bool holdNewName)
        {
            holdNewName = false;
            string oldName = ShippedStowProfiles.LegacyAscendedName;
            string newName = ShippedStowProfiles.Ascended;
            if (marker == null || files == null) return 0;
            if (!marker.ContainsKey(oldName) || marker.ContainsKey(newName)) return 0;   // nothing to do / done
            try
            {
                int rev = marker[oldName];
                StowProfileDoc oldDoc = LoadByName(files, oldName, false);
                StowProfileDoc newDoc = LoadByName(files, newName, false);

                if (oldDoc == null)
                {
                    // Deleted — or merely unreadable right now? Only the file name we wrote it under
                    // can tell us; if that file is there and will not load, decide nothing this pass.
                    string canonicalOld = Path.Combine(Dir, SafeFileToken(oldName) + ".xml");
                    if (File.Exists(canonicalOld) && LoadDocFile(canonicalOld, false) == null)
                    {
                        holdNewName = true;
                        UIALog.Warn("Stow Profile '" + oldName + "' could not be read this launch, so its rename to '"
                            + newName + "' is postponed (nothing was changed).");
                        return 0;
                    }
                    marker.Remove(oldName);
                    if (newDoc == null) marker[newName] = rev;   // stays deleted under its new name
                    markerDirty = true;
                    UIALog.Info("Shipped Stow Profile '" + oldName + "' was renamed to '" + newName
                        + "'; you had deleted it, so it stays deleted.");
                    return 0;
                }

                if (newDoc != null)
                {
                    marker.Remove(oldName);
                    markerDirty = true;
                    UIALog.Warn("The shipped Stow Profile '" + oldName + "' is now called '" + newName
                        + "', but you already have a layout called '" + newName + "'. Nothing was renamed or "
                        + "overwritten: '" + oldName + "' is kept as one of your own layouts.");
                    return 0;
                }

                // The mirror of the unreadable-old check above: an "Ascended.xml" that is present but
                // will not load this pass (locked by a sync client, bad XML) may be the player's own
                // "Ascended". Renaming now would give them two layouts of that name once it loads
                // again, so decide nothing this pass.
                string canonicalNew = Path.Combine(Dir, SafeFileToken(newName) + ".xml");
                if (File.Exists(canonicalNew) && LoadDocFile(canonicalNew, false) == null)
                {
                    holdNewName = true;
                    UIALog.Warn("Stow Profile file '" + Path.GetFileName(canonicalNew) + "' could not be read this launch, so the rename of '"
                        + oldName + "' to '" + newName + "' is postponed (nothing was changed).");
                    return 0;
                }

                // ---- rename: the in-place rewrite is the one step that must succeed ----
                string oldPath = oldDoc.SourceFile;
                oldDoc.Name = newName;
                if (!SaveDoc(oldDoc))
                {
                    holdNewName = true;
                    UIALog.Warn("Could not rename Stow Profile '" + oldName + "' to '" + newName
                        + "' this launch (nothing was changed); will retry next launch.");
                    return 0;
                }

                // Cosmetic: move the file to the new name when it still carries the one we gave it.
                string finalPath = oldDoc.SourceFile;
                try
                {
                    string target = Path.Combine(Dir, SafeFileToken(newName) + ".xml");
                    if (!string.IsNullOrEmpty(finalPath)
                        && string.Equals(Path.GetFileNameWithoutExtension(finalPath), SafeFileToken(oldName),
                            StringComparison.OrdinalIgnoreCase)
                        && !File.Exists(target))
                    {
                        File.Move(finalPath, target);
                        finalPath = target;
                    }
                }
                catch (Exception e)
                {
                    UIALog.Warn("Renamed Stow Profile '" + oldName + "' to '" + newName
                        + "' but kept its old file name: " + e.Message);
                }

                if (string.Equals(ReadActiveMarker(), oldName, StringComparison.OrdinalIgnoreCase))
                    WriteActiveMarker(newName);
                if (_active != null && !string.IsNullOrEmpty(oldPath)
                    && string.Equals(_active.SourceFile, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    _active.Name = newName;
                    _active.SourceFile = finalPath;
                }

                marker.Remove(oldName);
                marker[newName] = rev;
                markerDirty = true;
                UIALog.Info("Renamed the shipped Stow Profile '" + oldName + "' to '" + newName
                    + "' (your edits are kept).");
                return 1;
            }
            catch (Exception e)
            {
                holdNewName = true;
                UIALog.Warn("Stow Profile rename '" + oldName + "' -> '" + newName + "' failed: " + e.Message);
                return 0;
            }
        }

        /// <summary>The maintenance affordance (F10 -> Storage -> Stow Profiles): re-write ALL four
        /// shipped sets from code, OVERWRITING a player's edits to those names and re-creating any
        /// they deleted. Deliberately destructive and therefore arm-confirmed in the UI — this is the
        /// only path by which shipped content ever changes on an existing install.
        ///
        /// <para>If the ACTIVE set is one of the four, the in-memory document is now stale — and a
        /// stale <c>_active</c> is not a cosmetic problem: <see cref="Available"/> would still be
        /// true while <c>SaveActive</c> wrote the OLD content back over the restore. So this method
        /// drops the loaded document AND re-runs <see cref="BagProfileStore.LoadProfiles"/> itself,
        /// rather than leaving the store in a state only a well-behaved caller can survive.</para></summary>
        /// <returns>How many sets were written.</returns>
        public static int RestoreShipped()
        {
            int written = 0;
            try
            {
                Dictionary<string, int> marker = ReadShippedMarker();
                bool ok;
                List<string> files = DocFiles(out ok);
                if (!ok) return 0;
                for (int i = 0; i < ShippedStowProfiles.Names.Length; i++)
                {
                    string name = ShippedStowProfiles.Names[i];
                    StowProfileDoc doc = ShippedStowProfiles.Build(name);
                    if (doc == null) continue;
                    StowProfileDoc existing = LoadByName(files, name, false);
                    // Overwrite IN PLACE when the name is already on disk (even under a hand-renamed
                    // file), so restoring does not leave a second copy behind.
                    if (existing != null) doc.SourceFile = existing.SourceFile;
                    if (!SaveDoc(doc)) continue;
                    marker[name] = ShippedStowProfiles.Revision;
                    written++;
                }
                WriteShippedMarker(marker);
                _active = null;
                _available = false;
                UIALog.Info("Restored " + written + " shipped Stow Profile(s).");
            }
            catch (Exception e)
            {
                UIALog.Warn("Restore of the shipped Stow Profiles failed: " + e.Message);
            }
            try
            {
                // Re-resolve from disk in the same call, so there is no window in which Available is
                // false and a save would silently fall back to the legacy Profiles/ folder.
                BagProfileStore.LoadProfiles();
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not reload bag profiles after the restore: " + e.Message);
            }
            return written;
        }

        /// <summary>Is this one of the four names we ship? (Used only to label the manager list.)</summary>
        internal static bool IsShippedName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < ShippedStowProfiles.Names.Length; i++)
                if (string.Equals(ShippedStowProfiles.Names[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ShippedMarkerPath { get { return Path.Combine(Dir, ShippedMarkerFile); } }

        /// <summary>name -> revision, case-insensitive. A missing/garbled marker reads as EMPTY,
        /// which re-seeds — the safe direction: a duplicate shipped set is a nuisance, a player who
        /// silently never receives the presets is a support ticket.</summary>
        private static Dictionary<string, int> ReadShippedMarker()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = ShippedMarkerPath;
                if (!File.Exists(path)) return map;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = (lines[i] ?? string.Empty).Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int bar = line.LastIndexOf('|');
                    string name = bar > 0 ? line.Substring(0, bar) : line;
                    int rev = 0;
                    if (bar > 0) int.TryParse(line.Substring(bar + 1), out rev);
                    if (name.Length > 0) map[name] = rev;
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not read the shipped Stow Profile marker: " + e.Message);
            }
            return map;
        }

        private static void WriteShippedMarker(Dictionary<string, int> map)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new System.Text.StringBuilder();
                sb.Append("# Shipped Stow Profiles this install has handed over. Presence = do not seed again.")
                  .Append(Environment.NewLine);
                foreach (var kv in map)
                    sb.Append(kv.Key).Append('|').Append(kv.Value).Append(Environment.NewLine);
                File.WriteAllText(ShippedMarkerPath, sb.ToString());
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not record the shipped Stow Profiles: " + e.Message);
            }
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
            EditVersion++;
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
