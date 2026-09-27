using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using BepInEx;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    // ---------- XML models ----------
    // Public by necessity: XmlSerializer emits a generated assembly that must be able to SEE the
    // types it serializes, so an internal DTO throws at construction time. Same shape/idiom as
    // Features/BagProfiles.cs (BagProfileFile / BagProfile).

    /// <summary>The override file. v2 stores one <c>&lt;Text key="..."&gt;</c> per edited copy key
    /// (the key grammar in Documentation/0.9.8.0/Tutorial-Build-Contract.md s6). The v1
    /// <c>&lt;Step id=...&gt;&lt;Heading/&gt;&lt;Body/&gt;&lt;/Step&gt;</c> entries of the retired 15-step coach
    /// still deserialize (so an old file loads without error) and are written back untouched -
    /// harmless orphans that no lesson reads.</summary>
    [XmlRoot("TutorialText")]
    public class TutorialTextFile
    {
        [XmlAttribute("version")] public int Version = 2;
        [XmlElement("Text")] public List<TutorialTextKV> Texts = new List<TutorialTextKV>();
        [XmlElement("Step")] public List<TutorialTextEntry> Steps = new List<TutorialTextEntry>();
    }

    /// <summary>One v2 override: a copy key and its text (code tokens, ASCII).</summary>
    public class TutorialTextKV
    {
        [XmlAttribute("key")] public string Key;
        [XmlText] public string Value;
    }

    /// <summary>A v1 (15-step coach) override. Kept only so old files load and round-trip.</summary>
    public class TutorialTextEntry
    {
        [XmlAttribute("id")] public string Id;
        [XmlElement("Heading")] public string Heading;
        [XmlElement("Body")] public string Body;
    }

    /// <summary>
    /// v2 of the tutorial text store: every editable lesson string, keyed
    /// <c>"&lt;stepId&gt;|&lt;field&gt;"</c> / <c>"lesson:&lt;id&gt;|title"</c> / <c>"tip.&lt;n&gt;|says"</c> /
    /// <c>"chrome|&lt;name&gt;"</c>. <see cref="Get"/> returns the player/dev override when one exists,
    /// else the shipped default from <see cref="TutorialCopy.Pairs"/>. Overrides persist to the SAME
    /// file as v1: <c>BepInEx/config/StationeersUIMod/Tutorial/TutorialText.xml</c>.
    ///
    /// <para><b>Card aliases.</b> The coach edits every card in place through <c>|heading</c> and
    /// <c>|body</c>. A step that shows as a card without declaring those fields (an F10 callout, a
    /// strip replayed as a Watch card) maps them onto what it does declare: <c>|heading</c> -&gt;
    /// <c>|title</c>, <c>|body</c> -&gt; <c>|callout</c>, else <c>|says</c>. So in-place editing always
    /// lands on the field the card actually shows (see <see cref="Canonical"/>).</para>
    ///
    /// <para><b>ASCII SANITISER (both directions).</b> The file is hand-editable and a text editor
    /// happily pastes an em-dash or a curly quote - which TMP renders as tofu. Every value is
    /// scrubbed on LOAD, on SET and on SAVE: anything above 0x7E becomes '?', CR is dropped (LF
    /// survives, so text can be multi-line) and other control chars are dropped (XML 1.0 cannot carry
    /// them). A hand-edited file can therefore never tofu the game.</para>
    ///
    /// <para>Writes are memory-only until <see cref="Save"/>. Hot reload: <see cref="Shutdown"/>
    /// clears every cache, so a double-F6 re-reads from disk.</para>
    ///
    /// <para><b>The file is FlorpyDorp's authoring work - it is never lost to a bad read or a crash.</b>
    /// <see cref="Save"/> writes <c>TutorialText.xml.tmp</c> in full and only then swaps it in
    /// (delete + move, the <c>TutorialProgressStore</c> / exporter pattern); <see cref="Load"/>
    /// reads the temp copy when a crash interrupted the swap. A file that exists but cannot be
    /// read is NOT treated as "no overrides": a <c>.bad</c> copy is kept beside it, the error is
    /// logged loudly, the shipped copy shows, and the store goes READ-ONLY (<see cref="LoadFailed"/>,
    /// <see cref="SaveBlockedReason"/>) - every Save refuses and nothing is deleted until a good
    /// <see cref="Reload"/> (or a restart). <see cref="SuppressWritesUntilRestart"/> (uiareset)
    /// turns every later Save into a no-op for the session.</para>
    /// </summary>
    internal static class TutorialTextStore
    {
        private static readonly Dictionary<string, string> _overrides =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // v1 entries, preserved verbatim (by old step id) so a Save never destroys them.
        private static readonly Dictionary<string, TutorialTextEntry> _legacy =
            new Dictionary<string, TutorialTextEntry>(StringComparer.Ordinal);
        private static Dictionary<string, string> _defaults;   // lazily built from TutorialCopy.Pairs
        private static bool _loaded;
        private static bool _loadFailed;         // the file exists but could not be read: read-only until a good load
        private static string _loadFailReason;   // that state's status line (carries the read error)
        private static bool _writesSuppressed;   // uiareset deleted the config tree; a restart follows
        private static string _blockedReason;    // cached status line while Save refuses (null = Save writes)
        private static bool _blockWarned;        // the refusal is logged once per blocked state, not per Save

        private const string SuppressedReason =
            "Saving is off until you restart the game (uiareset deleted the config folder).";

        /// <summary>Config root, resolved exactly like <c>Features/BagProfiles.ConfigDir</c>.</summary>
        internal static string ConfigDir { get { return Path.Combine(Paths.ConfigPath, "StationeersUIMod"); } }
        internal static string TutorialDir { get { return Path.Combine(ConfigDir, "Tutorial"); } }
        internal static string FilePath { get { return Path.Combine(TutorialDir, "TutorialText.xml"); } }

        // ---------- load / save ----------

        /// <summary>Lazy, idempotent, never throws. A missing (or zero-byte) file is the normal case: no
        /// overrides. A file that exists but cannot be read puts the store in the READ-ONLY failed-load
        /// state (see the class remarks) instead of pretending it was empty - the next Save would
        /// otherwise write that empty/partial state over the real file, or delete it.</summary>
        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _loadFailed = false;
            _loadFailReason = null;
            _overrides.Clear();
            _legacy.Clear();
            RefreshBlockedReason();

            string path = FilePath, tmp = path + ".tmp";
            // Save writes TutorialText.xml.tmp in full, THEN deletes TutorialText.xml and moves the
            // temp in. A crash between those two steps leaves only the (complete) temp copy: read it.
            string src = null;
            try { src = File.Exists(path) ? path : (File.Exists(tmp) ? tmp : null); }
            catch { src = null; }
            if (src == null) return;
            try
            {
                // The pre-0.9.8.0 save truncated the file in place, so a crash mid-write could leave
                // it empty: nothing in it to lose, so it reads as "no overrides" and saving stays on.
                if (new FileInfo(src).Length == 0)
                {
                    UIALog.Warn("Tutorial text overrides: " + src + " is empty - treated as no overrides.");
                    return;
                }
                var serializer = new XmlSerializer(typeof(TutorialTextFile));
                TutorialTextFile file;
                using (var stream = File.OpenRead(src))
                    file = serializer.Deserialize(stream) as TutorialTextFile;
                if (file == null) throw new InvalidDataException("the XML has no TutorialText root");
                if (file.Texts != null)
                {
                    for (int i = 0; i < file.Texts.Count; i++)
                    {
                        var kv = file.Texts[i];
                        if (kv == null || string.IsNullOrEmpty(kv.Key)) continue;
                        string text = Sanitize(kv.Value);
                        if (string.IsNullOrEmpty(text)) continue;   // an empty override means "shipped copy"
                        _overrides[kv.Key] = text;                  // last wins on a duplicated key
                    }
                }
                if (file.Steps != null)
                {
                    for (int i = 0; i < file.Steps.Count; i++)
                    {
                        var e = file.Steps[i];
                        if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                        e.Heading = Sanitize(e.Heading);
                        e.Body = Sanitize(e.Body);
                        _legacy[e.Id] = e;
                    }
                }
                UIALog.Info("Tutorial text overrides loaded: " + _overrides.Count + " key(s)"
                    + (_legacy.Count > 0 ? " (+" + _legacy.Count + " retired v1 step entr" + (_legacy.Count == 1 ? "y" : "ies") + ", ignored)" : "")
                    + " from " + src);
                if (!string.Equals(src, path, StringComparison.Ordinal))
                {
                    // Recovered an interrupted swap: put the file back under its real name now, so a
                    // stale temp copy can never be "recovered" again over a later reset or bake.
                    try { File.Move(tmp, path); } catch { }
                    UIALog.Warn("Tutorial text overrides recovered from " + tmp + " (a save was interrupted).");
                }
            }
            catch (Exception e)
            {
                // NEVER fall back to "no overrides" here: the next Save would write that over the file
                // we could not read (or delete it). The shipped copy shows, a .bad copy is kept beside
                // the file, and every Save refuses until a good Reload / restart.
                _overrides.Clear();
                _legacy.Clear();
                _loadFailed = true;
                string bad = path + ".bad";
                bool copied = false;
                try { File.Copy(src, bad, true); copied = true; } catch { }
                // XmlSerializer's own message is only "There is an error in XML document (3, 5)." - the
                // inner exception says what is actually wrong.
                string msg = e.Message ?? e.GetType().Name;
                if (e.InnerException != null && !string.IsNullOrEmpty(e.InnerException.Message))
                    msg += " " + e.InnerException.Message;
                string why = Sanitize(msg);
                _loadFailReason = "TutorialText.xml could not be read (" + why + ") - saving is OFF so the file is not overwritten"
                    + (copied ? "; a copy is kept as TutorialText.xml.bad" : "")
                    + ". Fix or delete it, then Reload.";
                _blockWarned = false;
                RefreshBlockedReason();
                UIALog.Error("TutorialTextStore: " + src + " could not be read - " + msg
                    + ". The SHIPPED copy is showing and SAVING IS OFF, so the file is not overwritten"
                    + (copied ? " (a copy is kept as " + bad + ")" : "")
                    + ". Fix or delete it, then Reload in the Lesson Editor (or restart the game).");
            }
        }

        /// <summary>Drop the in-memory overrides and read the file again - the way out of the
        /// failed-load state once the file is fixed or deleted (the Lesson Editor's "Reload from
        /// disk"). Unsaved in-memory edits are discarded. True when the file read cleanly (or is
        /// gone) - saving can still be off after uiareset (see <see cref="SaveBlockedReason"/>).</summary>
        internal static bool Reload()
        {
            _loaded = false;
            Load();
            return !_loadFailed;
        }

        /// <summary>True while the file exists but could not be read (see the class remarks).</summary>
        internal static bool LoadFailed { get { Load(); return _loadFailed; } }

        /// <summary>Null while <see cref="Save"/> writes; otherwise one ASCII sentence saying why every
        /// Save refuses (the failed-load state, or <see cref="SuppressWritesUntilRestart"/>). Cached -
        /// safe to read every frame.</summary>
        internal static string SaveBlockedReason { get { Load(); return _blockedReason; } }

        /// <summary>uiareset: the whole config tree was just deleted and a restart follows. Every later
        /// <see cref="Save"/> is a no-op (writes nothing, returns false) for the rest of the session, so
        /// a field commit or an editor close cannot re-create Tutorial/TutorialText.xml behind the
        /// reset. In-memory text keeps working until the restart. (A hot reload's new assembly starts
        /// with fresh statics - a new session.)</summary>
        internal static void SuppressWritesUntilRestart()
        {
            _writesSuppressed = true;
            _blockWarned = false;
            RefreshBlockedReason();
            UIALog.Info("TutorialTextStore: writes suppressed until restart (uiareset).");
        }

        /// <summary>Recompute the cached <see cref="SaveBlockedReason"/> (uiareset wins over a failed
        /// load: a restart is what ends both).</summary>
        private static void RefreshBlockedReason()
        {
            _blockedReason = _writesSuppressed ? SuppressedReason : (_loadFailed ? _loadFailReason : null);
        }

        /// <summary>Write the in-memory overrides to disk (creating the folder). Returns false - and
        /// writes NOTHING - on an IO failure (read-only dir, disk full, a second game instance holding
        /// the file) and while <see cref="SaveBlockedReason"/> is set; the caller must not claim
        /// "saved" then. Atomic: the temp copy is written in full, then swapped in. An empty store
        /// (every line reverted, after a GOOD load) deletes the file instead of writing an empty one
        /// (and keeps any v1 entries by writing them back when there are some).</summary>
        internal static bool Save()
        {
            if (!_writesSuppressed) Load();
            if (_writesSuppressed || _loadFailed)
            {
                if (!_blockWarned)
                {
                    _blockWarned = true;
                    UIALog.Warn("TutorialTextStore.Save refused: " + (_blockedReason ?? "saving is off"));
                }
                return false;
            }
            string path = FilePath, tmp = path + ".tmp";
            bool writingTmp = false, tmpComplete = false;
            try
            {
                // An earlier swap was interrupted and the temp copy is the only complete file: promote
                // it before writing a new temp over it (never truncate the last good copy). If that
                // throws, the catch below leaves the temp copy alone (writingTmp is still false).
                if (!File.Exists(path) && File.Exists(tmp)) File.Move(tmp, path);
                if (_overrides.Count == 0 && _legacy.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    // Any temp copy left now is stale (the promote above ran): Load must not
                    // "recover" it and resurrect the lines just reverted.
                    if (File.Exists(tmp)) File.Delete(tmp);
                    return true;
                }
                Directory.CreateDirectory(TutorialDir);
                var file = new TutorialTextFile();
                // Stable order (by key) so the file diffs cleanly between saves.
                var keys = new List<string>(_overrides.Keys);
                keys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count; i++)
                    file.Texts.Add(new TutorialTextKV { Key = keys[i], Value = Sanitize(_overrides[keys[i]]) });
                foreach (var kv in _legacy)
                {
                    if (kv.Value == null) continue;
                    file.Steps.Add(new TutorialTextEntry
                    {
                        Id = kv.Key,
                        Heading = Sanitize(kv.Value.Heading),
                        Body = Sanitize(kv.Value.Body),
                    });
                }
                var serializer = new XmlSerializer(typeof(TutorialTextFile));
                // Stream overload -> UTF-8, matching every other store in the mod. Temp first, then
                // swap: a crash or a full disk mid-write can never leave half a file behind.
                writingTmp = true;
                using (var stream = File.Create(tmp))
                    serializer.Serialize(stream, file);
                tmpComplete = true;
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                UIALog.Info("Saved tutorial text overrides (" + file.Texts.Count + ") to " + path);
                return true;
            }
            catch (Exception e)
            {
                // OUR temp copy that never finished is garbage - but once complete it may be the ONLY
                // copy (the old file already deleted), so then it stays for Load to recover. A temp
                // copy we did not start writing (the promote above failed) is never touched.
                if (writingTmp && !tmpComplete) { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                UIALog.Error("TutorialTextStore.Save failed: " + e);
                return false;
            }
        }

        // ---------- reads ----------

        /// <summary>The text for a copy key: the override when one exists, else the shipped default.
        /// Raw code-token form (resolve with <see cref="TutorialTokens.Resolve"/>). Never null.</summary>
        internal static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            Load();
            key = Canonical(key);
            string s;
            if (_overrides.TryGetValue(key, out s) && !string.IsNullOrEmpty(s)) return s;
            return Default(key);
        }

        /// <summary>The shipped text for a copy key (<see cref="TutorialCopy.Pairs"/>), "" if none.</summary>
        internal static string Default(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            EnsureDefaults();
            key = Canonical(key);
            string s;
            return _defaults.TryGetValue(key, out s) && s != null ? s : "";
        }

        /// <summary>True when the key has a shipped default (i.e. it is part of the script).</summary>
        internal static bool HasDefault(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureDefaults();
            return _defaults.ContainsKey(Canonical(key));
        }

        internal static bool IsOverridden(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            Load();
            return _overrides.ContainsKey(Canonical(key));
        }

        internal static int OverrideCount { get { Load(); return _overrides.Count; } }

        /// <summary>The overridden keys - a snapshot, sorted, so a caller may Revert while iterating it.
        /// Allocates (editor / lint only).</summary>
        internal static IEnumerable<string> OverriddenKeys
        {
            get
            {
                Load();
                var keys = new List<string>(_overrides.Keys);
                keys.Sort(StringComparer.Ordinal);
                return keys;
            }
        }

        // ---------- writes (memory only until Save) ----------

        /// <summary>Override a key's text. ASCII-sanitised; text that is empty or equal to the shipped
        /// default reverts the key instead, so paging through an editor never bloats the file.</summary>
        internal static void Set(string key, string text)
        {
            if (string.IsNullOrEmpty(key)) return;
            Load();
            key = Canonical(key);
            string clean = Sanitize(text ?? "");
            if (string.IsNullOrEmpty(clean) || string.Equals(clean, Default(key), StringComparison.Ordinal))
            {
                _overrides.Remove(key);
                return;
            }
            _overrides[key] = clean;
        }

        /// <summary>Back to the shipped copy for this key (memory only until Save).</summary>
        internal static void Revert(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            Load();
            _overrides.Remove(Canonical(key));
        }

        /// <summary>Back to the shipped copy for EVERYTHING: forget the cache AND delete the file, so a
        /// later Save cannot resurrect it (the <c>uiatutorial reset</c> path). An explicit reset also
        /// ends a failed-load state - there is no unread file left to protect (a <c>.bad</c> copy of
        /// it, if one was kept, stays).</summary>
        internal static void ResetAll()
        {
            Load();
            _overrides.Clear();
            _legacy.Clear();
            _loadFailed = false;
            _loadFailReason = null;
            _blockWarned = false;
            RefreshBlockedReason();
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                // A leftover temp copy would otherwise be "recovered" by the next Load (see Load).
                if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp");
            }
            catch (Exception e)
            {
                UIALog.Warn("TutorialTextStore.ResetAll could not delete " + FilePath + ": " + e.Message);
            }
        }

        /// <summary>Hot-reload / teardown: drop the caches and every state flag so the next read
        /// re-reads disk. (The plugin teardown runs this AFTER the editor's final save, so the
        /// uiareset suppression is still in force for that save.)</summary>
        internal static void Shutdown()
        {
            _overrides.Clear();
            _legacy.Clear();
            _defaults = null;
            _loaded = false;
            _loadFailed = false;
            _loadFailReason = null;
            _writesSuppressed = false;
            _blockedReason = null;
            _blockWarned = false;
        }

        // ---------- card aliases ----------

        /// <summary>Map a card's generic <c>|heading</c> / <c>|body</c> key onto the field its step really
        /// declares, when it does not declare that one itself (see the class remarks). Every other key
        /// is returned unchanged. Cheap: one dictionary probe via <see cref="TutorialChapters.FindStep"/>,
        /// and only for keys ending in those two field names.</summary>
        internal static string Canonical(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            int bar = key.IndexOf('|');
            if (bar <= 0 || bar >= key.Length - 1) return key;
            int flen = key.Length - bar - 1;
            bool heading = flen == 7 && string.CompareOrdinal(key, bar + 1, "heading", 0, 7) == 0;
            bool body = flen == 4 && string.CompareOrdinal(key, bar + 1, "body", 0, 4) == 0;
            if (!heading && !body) return key;
            TStep step;
            try { step = TutorialChapters.FindStep(key.Substring(0, bar)); }
            catch { step = null; }
            if (step == null || step.Fields == null) return key;
            if (step.Has(heading ? "heading" : "body")) return key;
            if (heading) return step.Has("title") ? step.Id + "|title" : key;
            if (step.Has("callout")) return step.Id + "|callout";
            if (step.Has("says")) return step.Id + "|says";
            return key;
        }

        private static void EnsureDefaults()
        {
            if (_defaults != null) return;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var p = TutorialCopy.Pairs;
                for (int i = 0; p != null && i + 1 < p.Length; i += 2)
                {
                    if (string.IsNullOrEmpty(p[i])) continue;
                    d[p[i]] = p[i + 1] ?? "";
                }
            }
            catch (Exception e) { UIALog.Warn("TutorialCopy defaults could not be read: " + e.Message); }
            _defaults = d;
        }

        // ---------- legacy (v1 15-step coach) entries ----------
        // The pre-0.9.8.0 coach's 15 step ids. Their Heading(TutorialStep)/Body(TutorialStep) readers
        // were removed with TutorialSteps.cs (0.9.8.0 integration); the entries themselves are still
        // loaded and saved back so an older override file loses nothing. Nothing v2 shows reads them.

        internal static bool HasOverride(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            Load();
            return _legacy.ContainsKey(id);
        }

        internal static void SetOverride(string id, string heading, string body)
        {
            if (string.IsNullOrEmpty(id)) return;
            Load();
            _legacy[id] = new TutorialTextEntry { Id = id, Heading = Sanitize(heading), Body = Sanitize(body) };
        }

        internal static void ClearOverride(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Load();
            _legacy.Remove(id);
        }

        // ---------- the sanitiser ----------

        /// <summary>Force a string into the ASCII range TMP can actually draw: anything above 0x7E
        /// becomes '?', CR is dropped (LF kept so text stays multi-line), and control chars below
        /// 0x20 (other than LF and TAB) are dropped outright - XML 1.0 cannot carry them, so one
        /// pasted control char would make XmlSerializer throw and lose the whole save. Null-safe;
        /// allocation-free for already-clean text.</summary>
        internal static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c > 0x7E || (c < 0x20 && c != '\n' && c != '\t')) { clean = false; break; }
            }
            if (clean) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 && c != '\n' && c != '\t') continue;   // includes CR
                sb.Append(c > 0x7E ? '?' : c);
            }
            return sb.ToString();
        }
    }
}
