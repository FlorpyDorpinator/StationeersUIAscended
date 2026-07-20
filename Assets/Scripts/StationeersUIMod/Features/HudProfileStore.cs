using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using BepInEx;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Hud;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Disk home and live-document registry for HUD Designer profiles.
    ///
    /// Profiles are plain, shareable XML files (one <see cref="HudDocument"/> per file) so a
    /// player can hand a layout to a friend by copying a single file — the same "global,
    /// human-editable config" contract as bag profiles. Nothing here reaches into game state;
    /// the store only reads/writes files and hands out the one document the HUD currently draws.
    ///
    /// The active-document plumbing exists so exactly ONE object is ever live: editor widgets
    /// mutate it in place and call <see cref="MarkChanged"/>, which both bumps <see cref="Version"/>
    /// (HudSystem folds this into its layout hash) and arms a debounced autosave so a drag storm
    /// costs one file write, not one per frame.
    /// </summary>
    public static class HudProfileStore
    {
        /// <summary>Seconds of quiet after the last edit before the debounced autosave fires.</summary>
        private const float AutosaveDebounceSeconds = 1.5f;

        /// <summary>Shareable profile folder — global (not per-save), mirrors BagProfileStore's roots.</summary>
        public static string Dir => Path.Combine(Paths.ConfigPath, "StationeersUIMod", "HudProfiles");

        /// <summary>First-run import of the profile XMLs shipped INSIDE the mod folder (zip layout:
        /// <c>StationeersUIMod/HudProfiles/*.xml</c>) into the live config folder. NEVER overwrites —
        /// a player's edited copy of a shipped profile always wins over the shipped one. This is what
        /// makes the shipped default ("Smaller Test") actually exist on disk for a fresh install:
        /// the 0.8.0 zip carried the folder but nothing imported it, so it sat inert. Fail-soft.</summary>
        public static void ImportShipped(string modDirectory)
        {
            try
            {
                if (string.IsNullOrEmpty(modDirectory)) return;
                string src = Path.Combine(modDirectory, "HudProfiles");
                if (!Directory.Exists(src)) return;
                Directory.CreateDirectory(Dir);
                foreach (string f in Directory.GetFiles(src, "*.xml"))
                {
                    string dst = Path.Combine(Dir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst);
                }
                // Preview art travels beside the profile as a <name>.png sidecar so the Control
                // Center can show a thumbnail for each shipped "default UI". Same no-overwrite rule.
                foreach (string f in Directory.GetFiles(src, "*.png"))
                {
                    string dst = Path.Combine(Dir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst);
                }
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore.ImportShipped: " + e.Message); }
        }

        // --- active document ---

        public static HudDocument Active { get; private set; }

        /// <summary>Monotonic edit counter. Bumped on every mutation (via <see cref="MarkChanged"/>)
        /// and on every document swap, so HudSystem can cheaply detect "something changed" without
        /// diffing the tree.</summary>
        public static int Version { get; private set; }

        /// <summary>Raised when a DIFFERENT document object becomes active (load/switch) — NOT on
        /// in-place edits. Views tear down and rebuild against the new tree only on this signal.</summary>
        public static event Action ActiveReplaced;

        private static string _activeName;
        private static bool _dirty;
        private static bool _rearm;
        private static float _saveAt;

        /// <summary>Editor-facing autosave state. Read-only: persistence remains owned by Tick/
        /// FlushNow, but F9 can tell the author whether the active profile is still pending.</summary>
        public static bool HasPendingSave => _dirty;

        // A load/save failure warns once per file name, not once per frame — a corrupt profile that
        // a Tick keeps retrying must not flood the log. Cleared for a name on any clean load/delete.
        private static readonly HashSet<string> _warned = new HashSet<string>();

        // ---------- file store ----------

        /// <summary>Profile names (file basenames, no extension), case-insensitively sorted.
        /// Fail-soft: any IO trouble yields an empty list rather than throwing at a menu.</summary>
        public static List<string> ListProfiles()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                foreach (var path in Directory.GetFiles(Dir, "*.xml"))
                    list.Add(Path.GetFileNameWithoutExtension(path));
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD profile listing failed: " + e.Message);
            }
            return list;
        }

        /// <summary>Deserialize a profile by name, or null if it is missing/corrupt (warned once).
        /// The document is sanitized (schema-repaired) and stamped with its name before it is handed
        /// back, so callers never have to trust hand-edited XML.</summary>
        public static HudDocument Load(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return null;
            try
            {
                string path = Path.Combine(Dir, file + ".xml");
                if (!File.Exists(path))
                {
                    WarnOnce(file, "HUD profile '" + file + "' not found.");
                    return null;
                }
                var serializer = new XmlSerializer(typeof(HudDocument));
                HudDocument doc;
                using (var stream = File.OpenRead(path))
                    doc = (HudDocument)serializer.Deserialize(stream);
                if (doc == null)
                {
                    WarnOnce(file, "HUD profile '" + file + "' deserialized to nothing.");
                    return null;
                }
                doc.Sanitize();
                doc.Name = name;
                // The style migration is a one-time regression: persist it immediately so the
                // on-disk XML stops being legacy (else every load re-migrates and re-freezes
                // Custom snapshots from THAT session's globals — the values would drift).
                // Idempotent: the written file migrates to nothing on the next load.
                if (doc.RepairedOnLoad)
                {
                    doc.RepairedOnLoad = false;
                    Save(doc, name);
                }
                _warned.Remove(file); // a clean read re-arms the one-shot warning for next time
                return doc;
            }
            catch (Exception e)
            {
                WarnOnce(file, "HUD profile '" + file + "' failed to load: " + e.Message);
                return null;
            }
        }

        /// <summary>Serialize a document to &lt;name&gt;.xml, creating the folder first. Returns false
        /// (never throws) on any failure so an autosave tick can degrade quietly.</summary>
        public static bool Save(HudDocument doc, string name)
        {
            if (doc == null) return false;
            string file = SafeFileName(name);
            if (file == null)
            {
                UIALog.Warn("HUD profile save rejected: empty/invalid name.");
                return false;
            }
            try
            {
                Directory.CreateDirectory(Dir);
                var serializer = new XmlSerializer(typeof(HudDocument));
                string path = Path.Combine(Dir, file + ".xml");
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, doc);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD profile '" + file + "' failed to save: " + e.Message);
                return false;
            }
        }

        public static bool Delete(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return false;
            try
            {
                string path = Path.Combine(Dir, file + ".xml");
                if (!File.Exists(path)) return false;
                File.Delete(path);
                _warned.Remove(file);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD profile '" + file + "' failed to delete: " + e.Message);
                return false;
            }
        }

        /// <summary>Copy a profile under a new name. Routed through Load/Save (not a raw file copy)
        /// so the duplicate is sanitized and re-stamped rather than inheriting stale XML.</summary>
        public static bool Duplicate(string from, string to)
        {
            var doc = Load(from);
            if (doc == null) return false;
            doc.Name = to;
            return Save(doc, to);
        }

        // ---------- active document plumbing ----------

        /// <summary>Install a new live document. Any pending edits on the outgoing document are
        /// flushed first (switching never silently drops an unsaved change), then the swap fires
        /// <see cref="ActiveReplaced"/> so views rebuild against the new tree.</summary>
        public static void SetActive(HudDocument doc, string profileName)
        {
            FlushNow();          // persist the document we are leaving, under its own name
            Active = doc;
            _activeName = profileName;
            _dirty = false;      // the incoming document starts clean
            _rearm = false;
            Version++;
            var handler = ActiveReplaced;
            if (handler != null) handler();
        }

        /// <summary>Signal that the live document was mutated in place. Bumps <see cref="Version"/>
        /// and (re)arms the autosave debounce — the deadline is stamped by the next <see cref="Tick"/>
        /// against the caller's clock, which is why no game clock is read here.</summary>
        public static void MarkChanged()
        {
            Version++;
            _dirty = true;
            _rearm = true;
        }

        /// <summary>Make <paramref name="profileName"/> the live document, self-healing a missing or
        /// corrupt file: the starter factory builds the shipped default and it is written back, so an
        /// empty (or damaged) HudProfiles folder always ends up with a usable profile on disk.</summary>
        public static void LoadActive(string profileName, Func<HudDocument> starterFactory)
        {
            var doc = Load(profileName);
            if (doc == null)
            {
                doc = starterFactory != null ? starterFactory() : null;
                if (doc == null)
                {
                    UIALog.Warn("HUD profile '" + profileName + "' missing and no starter available.");
                    return;
                }
                doc.Sanitize();
                doc.Name = profileName;
                Save(doc, profileName);
            }
            SetActive(doc, profileName);
        }

        /// <summary>Drive the debounced autosave. Called every frame with the caller's unscaled clock
        /// (this file deliberately owns no clock of its own). A failed write backs the deadline off by
        /// the debounce window instead of retrying every frame, and never throws into the frame.</summary>
        public static void Tick(float unscaledNow)
        {
            if (_rearm)
            {
                _saveAt = unscaledNow + AutosaveDebounceSeconds;
                _rearm = false;
            }
            if (!_dirty || Active == null || string.IsNullOrEmpty(_activeName)) return;
            if (unscaledNow < _saveAt) return;
            try
            {
                if (Save(Active, _activeName)) _dirty = false;
                else _saveAt = unscaledNow + AutosaveDebounceSeconds; // back off; don't hammer
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD autosave failed: " + e.Message);
                _saveAt = unscaledNow + AutosaveDebounceSeconds;
            }
        }

        /// <summary>Persist the live document immediately if it has unsaved edits (on-close save).</summary>
        public static void FlushNow()
        {
            if (!_dirty || Active == null || string.IsNullOrEmpty(_activeName)) return;
            try
            {
                if (Save(Active, _activeName)) _dirty = false;
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD flush-save failed: " + e.Message);
            }
        }

        /// <summary>Hot-reload teardown: flush, drop the live document, and reset every static
        /// (including subscribers) so a reloaded assembly starts from a clean slate.</summary>
        public static void Shutdown()
        {
            FlushNow();
            Active = null;
            _activeName = null;
            Version = 0;
            _dirty = false;
            _rearm = false;
            _saveAt = 0f;
            _warned.Clear();
            ActiveReplaced = null;
        }

        /// <summary>Full path to a profile's preview image (<c>&lt;name&gt;.png</c> beside its xml),
        /// or null if none exists. Used by the Control Center to show a thumbnail on each card.</summary>
        public static string PreviewPath(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return null;
            try
            {
                string path = Path.Combine(Dir, file + ".png");
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        // ---------- helpers ----------

        // Strip (not substitute) OS-illegal filename characters so a profile named "O2 / N2" maps to a
        // stable "O2  N2.xml" rather than gaining underscores the user never typed. Empty after
        // stripping means "no valid name" — callers treat that as a no-op.
        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), string.Empty);
            name = name.Trim();
            return name.Length == 0 ? null : name;
        }

        private static void WarnOnce(string key, string message)
        {
            if (key == null) key = string.Empty;
            if (_warned.Add(key)) UIALog.Warn(message);
        }
    }
}
