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

    [XmlRoot("TutorialText")]
    public class TutorialTextFile
    {
        [XmlElement("Step")] public List<TutorialTextEntry> Steps = new List<TutorialTextEntry>();
    }

    public class TutorialTextEntry
    {
        [XmlAttribute("id")] public string Id;
        [XmlElement("Heading")] public string Heading;
        [XmlElement("Body")] public string Body;
    }

    /// <summary>
    /// The DEV EDIT layer for the first-run coach: per-step heading/body overrides that sit on top
    /// of the shipped copy in <see cref="TutorialSteps"/>, persisted to
    /// <c>BepInEx/config/StationeersUIMod/Tutorial/TutorialText.xml</c>.
    ///
    /// <para>Reached through <c>uiatutorial edit</c> -&gt; <see cref="TutorialCoach"/>'s edit mode:
    /// FlorpyDorp can retype a step in-game, eyeball it in the real window, and Save. Nothing here
    /// touches game state; a missing or corrupt file simply means "no overrides".</para>
    ///
    /// <para><b>ASCII SANITISER (both directions).</b> The file is meant to be hand-editable, and a
    /// text editor will happily paste an em-dash or a curly quote - which TMP renders as tofu.
    /// Every heading/body is scrubbed on LOAD and on SAVE: any char above 0x7E becomes '?', and CR
    /// is dropped (LF survives, so the body can be multi-line). A hand-edited file can therefore
    /// never tofu the window.</para>
    ///
    /// <para>Hot-reload: the whole cache lives in two statics that <see cref="Shutdown"/> clears
    /// (called from <c>TutorialCoach.Shutdown</c>), so a double-F6 re-reads from disk.</para>
    /// </summary>
    internal static class TutorialTextStore
    {
        private static readonly Dictionary<string, TutorialTextEntry> _overrides =
            new Dictionary<string, TutorialTextEntry>(StringComparer.Ordinal);

        private static bool _loaded;

        /// <summary>Config root, resolved exactly like <c>Features/BagProfiles.ConfigDir</c>.</summary>
        internal static string ConfigDir { get { return Path.Combine(Paths.ConfigPath, "StationeersUIMod"); } }
        internal static string TutorialDir { get { return Path.Combine(ConfigDir, "Tutorial"); } }
        internal static string FilePath { get { return Path.Combine(TutorialDir, "TutorialText.xml"); } }

        // ---------- load / save ----------

        /// <summary>Lazy, idempotent, and tolerant: a missing file is the normal case (no
        /// overrides), a corrupt one is logged once and ignored rather than throwing into whatever
        /// called us.</summary>
        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _overrides.Clear();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(TutorialTextFile));
                TutorialTextFile file;
                using (var stream = File.OpenRead(path))
                    file = serializer.Deserialize(stream) as TutorialTextFile;
                if (file == null || file.Steps == null) return;
                for (int i = 0; i < file.Steps.Count; i++)
                {
                    var e = file.Steps[i];
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    e.Heading = Ascii(e.Heading);
                    e.Body = Ascii(e.Body);
                    _overrides[e.Id] = e;   // last wins on a duplicated id
                }
                UIALog.Info("Tutorial text overrides loaded: " + _overrides.Count + " step(s) from " + path);
            }
            catch (Exception e)
            {
                // Corrupt/half-written file: ignore it entirely (we keep whatever parsed before the
                // throw out of the cache by clearing) and leave the shipped copy in charge.
                _overrides.Clear();
                UIALog.Warn("TutorialTextStore.Load failed (using shipped copy): " + e.Message);
            }
        }

        /// <summary>Write the in-memory overrides to disk (creating the folder). Called by the edit
        /// mode's Save button only - navigating between steps updates memory, never the file.</summary>
        internal static bool Save()
        {
            Load();
            try
            {
                Directory.CreateDirectory(TutorialDir);
                var file = new TutorialTextFile();
                foreach (var kv in _overrides)
                {
                    if (kv.Value == null) continue;
                    file.Steps.Add(new TutorialTextEntry
                    {
                        Id = kv.Key,
                        Heading = Ascii(kv.Value.Heading),
                        Body = Ascii(kv.Value.Body),
                    });
                }
                var serializer = new XmlSerializer(typeof(TutorialTextFile));
                // Stream overload -> UTF-8, matching every other store in the mod.
                using (var stream = File.Create(FilePath))
                    serializer.Serialize(stream, file);
                UIALog.Info("Saved tutorial text overrides (" + file.Steps.Count + ") to " + FilePath);
                return true;
            }
            catch (Exception e)
            {
                // Read-only dir, disk full, a second game instance holding the file - the caller
                // must NOT toast "saved" for this.
                UIALog.Error("TutorialTextStore.Save failed: " + e);
                return false;
            }
        }

        // ---------- reads ----------

        /// <summary>The heading to display for a step: the override when one exists and is not
        /// blank, otherwise the shipped default. Never null.</summary>
        internal static string Heading(TutorialStep s)
        {
            if (s == null) return "";
            Load();
            TutorialTextEntry e;
            if (_overrides.TryGetValue(s.Id, out e) && e != null && !string.IsNullOrEmpty(e.Heading))
                return e.Heading;
            return s.DefaultHeading ?? "";
        }

        /// <summary>The body copy to display for a step (override or shipped default). Never null.</summary>
        internal static string Body(TutorialStep s)
        {
            if (s == null) return "";
            Load();
            TutorialTextEntry e;
            if (_overrides.TryGetValue(s.Id, out e) && e != null && !string.IsNullOrEmpty(e.Body))
                return e.Body;
            return s.DefaultBody ?? "";
        }

        internal static bool HasOverride(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            Load();
            return _overrides.ContainsKey(id);
        }

        // ---------- writes (memory only until Save) ----------

        internal static void SetOverride(string id, string heading, string body)
        {
            if (string.IsNullOrEmpty(id)) return;
            Load();
            _overrides[id] = new TutorialTextEntry
            {
                Id = id,
                Heading = Ascii(heading),
                Body = Ascii(body),
            };
        }

        internal static void ClearOverride(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Load();
            _overrides.Remove(id);
        }

        /// <summary>Back to the shipped copy for every step: forget the cache AND delete the file,
        /// so a later Save cannot resurrect it.</summary>
        internal static void ResetAll()
        {
            Load();
            _overrides.Clear();
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                UIALog.Warn("TutorialTextStore.ResetAll could not delete " + FilePath + ": " + e.Message);
            }
        }

        /// <summary>Hot-reload / teardown: drop the cache so the next Load re-reads disk.</summary>
        internal static void Shutdown()
        {
            _overrides.Clear();
            _loaded = false;
        }

        // ---------- the sanitiser ----------

        /// <summary>Force a string into the ASCII range TMP can actually draw: anything above 0x7E
        /// becomes '?', CR is dropped (LF kept so bodies stay multi-line), and control chars below
        /// 0x20 (other than LF and TAB) are dropped outright - XML 1.0 cannot carry them, so one
        /// pasted control char would make XmlSerializer throw and lose the whole save. Null-safe.</summary>
        private static string Ascii(string s)
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
