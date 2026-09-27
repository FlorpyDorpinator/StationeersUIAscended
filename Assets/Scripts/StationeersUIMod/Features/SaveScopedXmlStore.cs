using System;
using System.IO;
using System.Xml.Serialization;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The one XmlSerializer funnel every small config store in <c>Features/</c> uses.
    ///
    /// Six stores (bag hotkeys, belt bindings, Grid collapse, Grid pins, hint usage, loadouts)
    /// had each grown their OWN private copy of the same twenty lines: build the folder path,
    /// <c>Directory.CreateDirectory</c>, <c>new XmlSerializer(typeof(T))</c>, open/create a
    /// <see cref="FileStream"/>, deserialize-or-warn, serialize-or-warn. That is the shape this
    /// class owns instead — the stores keep their own in-memory models, their own XML DTOs and
    /// their own log wording, and only the plumbing moved here.
    ///
    /// ON-DISK COMPATIBILITY IS THE POINT: this helper deliberately does NOT introduce settings,
    /// namespaces, encodings or writers of its own. It constructs a plain
    /// <c>new XmlSerializer(typeof(T))</c> and calls <c>Serialize(Stream, object)</c> on a
    /// <c>File.Create</c> stream — byte-for-byte the same call the six stores each made before —
    /// so every existing player file keeps its root element, its UTF-8 declaration and its
    /// indentation, and round-trips identically. Do not "improve" that here: a stray
    /// XmlWriterSettings or XmlSerializerNamespaces would silently rewrite everyone's config.
    ///
    /// PER-SAVE KEYING: the save-scoped stores all key their file off the per-world identity
    /// resolved by <see cref="ResolveSaveKey"/> (see its doc comment — SmartStow-Simple-Refactor-Plan
    /// §7.1/§7.7), living at <c>BepInEx/config/StationeersUIMod/&lt;folder&gt;/&lt;worldKey&gt;.xml</c>.
    /// The load path passes the key it just resolved; the save path passes its remembered
    /// <c>_loadedSaveKey</c>, which every store's own <c>EnsureSaveLoaded</c> now guarantees is
    /// non-null before a save fires (a null key means memory-only — nothing is ever written under
    /// the old "unsaved" placeholder again). <see cref="FileFor"/>'s <c>saveKey ?? CurrentSaveKey()</c>
    /// fallback survives only as a defensive no-op for a stray direct caller; every in-tree caller
    /// now always passes a resolved key.
    ///
    /// Every method is fail-soft: a missing file, a corrupt file or a locked directory yields
    /// null/false and at most one warning line. Nothing here touches game state, so nothing here
    /// has an MP dimension. No statics are held, so there is nothing to reset on hot reload.
    /// </summary>
    internal static class SaveScopedXmlStore
    {
        /// <summary>Config sub-folder for a store, e.g. "Hotkeys" -> <c>…/StationeersUIMod/Hotkeys</c>.</summary>
        public static string DirFor(string folder)
        {
            return Path.Combine(BagProfileStore.ConfigDir, folder);
        }

        /// <summary>Per-save file path for a store. A null/empty <paramref name="saveKey"/> resolves
        /// to the CURRENT save key — the <c>_loadedSaveKey ?? CurrentSaveKey()</c> save-path idiom.</summary>
        public static string FileFor(string folder, string saveKey)
        {
            string key = string.IsNullOrEmpty(saveKey) ? BagProfileStore.CurrentSaveKey() : saveKey;
            return Path.Combine(DirFor(folder), key + ".xml");
        }

        /// <summary>Deserialize <paramref name="path"/> into <typeparamref name="T"/>. Returns false
        /// with <paramref name="error"/> null when the file simply is not there (the normal "no
        /// state yet" case, which must not log), and false with a message when it exists but will
        /// not parse. Callers that want the standard warning use <see cref="Load{T}"/> instead;
        /// this overload exists for stores whose log wording differs (LoadoutStore names the file).</summary>
        public static bool TryLoad<T>(string path, out T value, out string error) where T : class
        {
            Exception ex;
            bool ok = TryLoad(path, out value, out ex);
            error = ex != null ? ex.Message : null;
            return ok;
        }

        /// <summary>As <see cref="TryLoad{T}(string,out T,out string)"/>, but hands back the
        /// EXCEPTION rather than just its message. Callers that react destructively to a bad file
        /// (quarantine) must be able to tell a genuine parse failure — <c>XmlException</c>, or the
        /// <c>InvalidOperationException</c> XmlSerializer wraps it in — from a transient
        /// <c>IOException</c>/<c>UnauthorizedAccessException</c> (a sync client or AV holding the
        /// file open for a moment). Renaming a healthy file because of a two-second lock is not
        /// fail-soft, it is data loss with extra steps.</summary>
        public static bool TryLoad<T>(string path, out T value, out Exception error) where T : class
        {
            value = null;
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                var serializer = new XmlSerializer(typeof(T));
                using (var stream = File.OpenRead(path))
                    value = (T)serializer.Deserialize(stream);
                return value != null;
            }
            catch (Exception e)
            {
                value = null;
                error = e;
                return false;
            }
        }

        /// <summary>Is this a genuine "the XML is broken" failure (as opposed to a transient IO or
        /// permission problem)? <c>XmlSerializer.Deserialize</c> reports malformed documents as an
        /// <c>InvalidOperationException</c> wrapping an <c>XmlException</c>.</summary>
        public static bool IsParseFailure(Exception error)
        {
            return error is System.Xml.XmlException || error is InvalidOperationException;
        }

        /// <summary>Deserialize a file, or null if it is absent/corrupt. A corrupt file warns once as
        /// "&lt;label&gt; load failed: &lt;message&gt;" — the exact line each store logged before.</summary>
        public static T Load<T>(string path, string label) where T : class
        {
            T value;
            string error;
            if (TryLoad(path, out value, out error)) return value;
            if (error != null) UIALog.Warn(label + " load failed: " + error);
            return null;
        }

        /// <summary>Deserialize a store's per-save file, or null if absent/corrupt.</summary>
        public static T LoadPerSave<T>(string folder, string saveKey, string label) where T : class
        {
            return Load<T>(FileFor(folder, saveKey), label);
        }

        /// <summary>Serialize to <paramref name="path"/>, creating its folder first. Returns false
        /// (never throws) on any failure, warning as "&lt;label&gt; save failed: &lt;message&gt;".
        ///
        /// <para><b>Atomic (fix-wave finding 6).</b> Serializes to a sibling <c>.tmp</c> file first,
        /// then swaps it over the real path — a crash or power-loss mid-<c>Serialize</c> can only
        /// ever corrupt the throwaway temp file, never the ONE copy a player has on disk.
        /// <see cref="File.Replace(string,string,string)"/> does the swap atomically when
        /// <paramref name="path"/> already exists; a plain <see cref="File.Move(string,string)"/>
        /// covers the "first save ever" case where it does not. If <c>Replace</c> itself is
        /// unavailable or fails (a locked file, a filesystem without the Win32 <c>ReplaceFile</c>
        /// primitive), a delete-then-move fallback runs instead — still not perfectly atomic across
        /// that one instant, but strictly better than serializing directly over the live file, and
        /// only reached on that already-degraded path.</para></summary>
        public static bool Save<T>(string path, T value, string label) where T : class
        {
            if (value == null || string.IsNullOrEmpty(path)) return false;
            string tempPath = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var serializer = new XmlSerializer(typeof(T));
                using (var stream = File.Create(tempPath))
                    serializer.Serialize(stream, value);

                if (File.Exists(path))
                {
                    try { File.Replace(tempPath, path, null); }
                    catch { ReplaceByDeleteThenMove(tempPath, path); }   // fallback, see summary above
                }
                else
                {
                    File.Move(tempPath, path);
                }
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn(label + " save failed: " + e.Message);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        /// <summary>Non-atomic fallback swap for <see cref="Save{T}"/> when <see cref="File.Replace"/>
        /// itself is unavailable or refuses (e.g. a locked destination). There is a brief window
        /// between the delete and the move where neither file exists; any exception here is left to
        /// <see cref="Save{T}"/>'s own catch, which reports the failure and cleans up the temp file.</summary>
        private static void ReplaceByDeleteThenMove(string tempPath, string path)
        {
            File.Delete(path);
            File.Move(tempPath, path);
        }

        /// <summary>Serialize a store's per-save file. See <see cref="FileFor"/> for key handling.</summary>
        public static bool SavePerSave<T>(string folder, string saveKey, T value, string label) where T : class
        {
            return Save(FileFor(folder, saveKey), value, label);
        }

        /// <summary>
        /// Resolve the CURRENT per-world file key for a save-scoped store
        /// (SmartStow-Simple-Refactor-Plan §7.1/§7.7 — the latent MP bug shared by every store in
        /// this file's family).
        ///
        /// <para><b>Why not the station name.</b> Every save-scoped store used to key its file off
        /// <see cref="BagProfileStore.CurrentSaveKey"/> (the station name). That name is NULL on
        /// every multiplayer CLIENT (there is no setter on the join path), so every server a client
        /// joined shared one file, literally named "unsaved.xml", per store — and because
        /// ReferenceIds restart at 1 in every world, a bag assignment or wedge binding learned on
        /// server X could silently re-attach to whatever carries the same id on server Y. The fix is
        /// the game's own per-world GUID, <c>World.CurrentId</c>: generated when a world is created,
        /// saved into and restored from the save file, and — unlike the station name — REPLICATED TO
        /// CLIENTS ON JOIN (vanilla keys its own per-world client prefs on it the same way:
        /// <c>Networking/Servers/PlayerCookie.GetWorldPrefsInterfaceData</c>). Verified in the 27798
        /// decompile: <c>Reference/StationeersGameVersions/Stationeers 8-13-26 V27798 Orbital Update
        /// Beta/Assembly-CSharp/Assets.Scripts.Objects/World.cs:28</c> (<c>public static string
        /// CurrentId</c>; flat dot-folder decompile layout). See <see cref="Core.WorldKey"/> for the
        /// full citation set and its legacy-SP fallback (an old save with no id yet).</para>
        ///
        /// <para><b>"unsaved" is dead.</b> Returns false when <see cref="Core.WorldKey.TryGet"/>
        /// reports no usable key exists yet (main menu, or a legacy SP world before its first save).
        /// The caller must then hold its state IN MEMORY ONLY — load nothing, save nothing — and
        /// call back in here on its own EnsureLoaded-style entry point; when a key later appears
        /// mid-session, the caller's own compare against its remembered key sees the change and
        /// should flush whatever it accumulated in memory to the newly-keyed file (mirrors
        /// <c>StowHomeStore.EnsureLoaded</c>'s "carried" dictionary, which predates this helper and
        /// established the pattern). No store may ever read or write an "unsaved.xml" again.</para>
        ///
        /// <para><b>One-time legacy adoption per store per world.</b> Paid only when the resolved key
        /// actually differs from <paramref name="previousKey"/> — so a hot per-frame re-check that
        /// finds nothing changed costs one cheap, memoized <see cref="Core.WorldKey.TryGet"/> call and
        /// nothing else, never a disk hit. On a genuine switch: if
        /// <c>&lt;folder&gt;/&lt;key&gt;.xml</c> does not exist yet but the OLD station-name-keyed
        /// <c>&lt;folder&gt;/&lt;CurrentSaveKey()&gt;.xml</c> does (and that name is not the broken
        /// "unsaved" placeholder), the legacy file is COPIED — never moved, hide-never-destroy — to
        /// the new name, so an existing single-player save's data keeps working exactly as before.
        /// One <see cref="UIALog"/> line per adoption.</para>
        /// </summary>
        public static bool ResolveSaveKey(string folder, string previousKey, out string key)
        {
            if (!Core.WorldKey.TryGet(out key))
            {
                key = null;
                return false;
            }
            if (!string.Equals(key, previousKey, StringComparison.Ordinal))
                AdoptLegacyFile(folder, key);
            return true;
        }

        /// <summary>One-time-per-switch legacy-file copy for <see cref="ResolveSaveKey"/>. Fail-soft:
        /// any IO problem is logged and skipped, never blocking the caller from proceeding with an
        /// empty/fresh table under the new key (the same posture as every other method here).
        ///
        /// <para><b>Adoption order (fix-wave finding 2).</b> Tries the STATION-ERA fallback file
        /// first (<c>station-&lt;CurrentSaveKey()&gt;.xml</c> — the name a legacy no-id world ran
        /// under via <see cref="Core.WorldKey"/>'s OWN fallback branch, written by THIS refactor),
        /// then the older plain <c>&lt;CurrentSaveKey()&gt;.xml</c> (written before this refactor
        /// shipped, back when the station name was the primary key everywhere). A no-id world that
        /// later gains a real <c>World.CurrentId</c> (first host, or a "Save As" branch) must adopt
        /// its OWN more recent station-era file, not strand it behind an older plain one.</para>
        /// </summary>
        private static void AdoptLegacyFile(string folder, string worldKey)
        {
            try
            {
                string newPath = Path.Combine(DirFor(folder), worldKey + ".xml");
                if (File.Exists(newPath)) return;   // already adopted (or genuinely fresh) - nothing to do

                string sourcePath = FindLegacyCandidate(folder);
                if (sourcePath == null) return;   // nothing usable to adopt from

                Directory.CreateDirectory(DirFor(folder));
                File.Copy(sourcePath, newPath);   // COPY: the legacy file is left in place, untouched
                UIALog.Info("Adopted legacy '" + folder + "/" + Path.GetFileName(sourcePath) + "' as '"
                    + folder + "/" + worldKey + ".xml' (per-world key migration, SmartStow-Simple-Refactor-Plan section 7.7).");
            }
            catch (Exception e)
            {
                UIALog.Warn("Legacy '" + folder + "' adoption failed: " + e.Message);
            }
        }

        /// <summary>The legacy file <see cref="AdoptLegacyFile"/> should copy from, or null if
        /// neither candidate exists / the station name is unusable ("unsaved"). Station-era fallback
        /// file first, then the older plain station-name file (fix-wave finding 2 — see
        /// <see cref="AdoptLegacyFile"/>'s doc comment for why the order matters).</summary>
        private static string FindLegacyCandidate(string folder)
        {
            string legacyKey = BagProfileStore.CurrentSaveKey();
            if (string.IsNullOrEmpty(legacyKey)
                || string.Equals(legacyKey, "unsaved", StringComparison.OrdinalIgnoreCase))
                return null;

            string stationEraPath = Path.Combine(DirFor(folder), Core.WorldKey.StationPrefix + legacyKey + ".xml");
            if (File.Exists(stationEraPath)) return stationEraPath;

            string plainLegacyPath = Path.Combine(DirFor(folder), legacyKey + ".xml");
            return File.Exists(plainLegacyPath) ? plainLegacyPath : null;
        }

        /// <summary>
        /// Fix-wave finding 5 (hardening). If a store's normal load under a freshly (re)resolved key
        /// comes back EMPTY, call this once more: it bypasses <see cref="ResolveSaveKey"/>'s
        /// "only on key change" throttle and retries <see cref="AdoptLegacyFile"/> unconditionally.
        /// This guards the near-impossible-after-finding-3 case where the VERY FIRST resolve for a
        /// key ran before the station name was set (so adoption declined, finding nothing) — without
        /// this, that world's legacy file would be stranded forever, because a later call for the
        /// SAME key never re-runs adoption.
        ///
        /// <para>Safe to call unconditionally: no-ops immediately (and cheaply — one
        /// <see cref="File.Exists"/> check) once <c>&lt;folder&gt;/&lt;worldKey&gt;.xml</c> exists for
        /// real, which happens on the store's very first successful adoption OR its first genuine
        /// save under the new key — so it can never overwrite anything the player has actually
        /// written under this key.</para>
        ///
        /// <para>Returns true when a legacy file was just adopted, meaning the caller should re-run
        /// its own <see cref="LoadPerSave{T}"/> to pick it up.</para>
        /// </summary>
        public static bool RetryAdoptionIfEmpty(string folder, string worldKey)
        {
            try
            {
                string newPath = Path.Combine(DirFor(folder), worldKey + ".xml");
                if (File.Exists(newPath)) return false;   // a real file already exists - never touch it
                AdoptLegacyFile(folder, worldKey);
                return File.Exists(newPath);
            }
            catch
            {
                return false;
            }
        }
    }
}
