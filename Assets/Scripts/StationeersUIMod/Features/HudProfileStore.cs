using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        /// <summary>Provenance of the profiles WE shipped, so an update can refresh / retire a
        /// shipped theme the player never touched WITHOUT harming their own profiles. One line per
        /// shipped file: <c>&lt;filename&gt;|&lt;hash&gt;</c>. Extension-less so <see cref="ListProfiles"/>
        /// (globs <c>*.xml</c>) never shows it. See Documentation/Config-and-Theme-Migration.md.</summary>
        private static string ManifestPath => Path.Combine(Dir, ".shipped-manifest");

        /// <summary>Sync the shipped profile set from the mod folder into the player's config, every
        /// launch. Three moves, and NONE of them ever touches a profile the player created or edited:
        ///  - SEED    a shipped profile that is absent.
        ///  - REFRESH a shipped profile we shipped before, that the player never edited, when we
        ///            ship a NEW version of it (so shipped-theme fixes actually reach existing players).
        ///  - PRUNE   a shipped profile we RETIRED, only if the player never edited it (a retired
        ///            theme the player customised becomes theirs and is kept).
        /// "Never edited" = the on-disk bytes still match exactly what we last shipped (recorded in
        /// the manifest). A player edit, a rename, or a hand-made profile has a different hash / no
        /// record, so it is left alone. If a pruned profile was the active one, the active profile
        /// falls back to the shipped default. Fail-soft; inert under F6 (modDirectory null).
        ///
        /// LIMITATION: this manages the set from THIS version forward. Junk profiles a player already
        /// has from a PRE-manifest version have no record, so they are never auto-pruned (we can't
        /// prove they're pristine) — only future retirements are swept. A pristine copy of a CURRENT
        /// shipped theme IS adopted into management on first sync.</summary>
        public static void SyncShipped(string modDirectory)
        {
            try
            {
                if (string.IsNullOrEmpty(modDirectory)) return;
                string src = Path.Combine(modDirectory, "HudProfiles");
                if (!Directory.Exists(src)) return;
                Directory.CreateDirectory(Dir);

                // Current shipped set: filename -> CANONICAL hash of the shipped source (null if it
                // won't parse). Canonical (not raw bytes) so Load's idempotent rewrite-on-open — a
                // shipped theme with legacy fx bools is re-serialized in canonical form the first time
                // it's opened — does NOT read as a player edit. See CanonHash.
                var shipped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(src, "*.xml"))
                    shipped[Path.GetFileName(f)] = CanonHash(f);

                var manifest = LoadManifest(); // filename -> canonical hash we last shipped

                // ---- SEED + REFRESH ----
                foreach (var kv in shipped)
                {
                    string name = kv.Key, shipHash = kv.Value;
                    string dst = Path.Combine(Dir, name);
                    string srcPath = Path.Combine(src, name);

                    if (!File.Exists(dst))
                    {
                        File.Copy(srcPath, dst);          // seed an absent shipped theme
                        if (shipHash != null) manifest[name] = shipHash; // manage it (skip if unhashable)
                        continue;
                    }

                    if (shipHash == null) continue;       // our own source won't parse → seed-only, don't manage
                    string diskHash = CanonHash(dst);
                    if (diskHash == null) continue;       // player's file won't parse → leave it, never touch

                    string prev;
                    bool known = manifest.TryGetValue(name, out prev);
                    // Untouched = canonically identical to what we CURRENTLY ship (always wins, known
                    // or not — this is what lets a pristine file self-heal into management even if a
                    // past mod version's canonical serialization drifted and the manifest hash is
                    // stale), or — for a pre-manifest install, or a manifest recorded under a stale
                    // serialization — matches what we last shipped (known).
                    bool untouched = diskHash == shipHash || (known && diskHash == prev);

                    if (untouched)
                    {
                        if (diskHash != shipHash) File.Copy(srcPath, dst, true); // refresh to new version
                        manifest[name] = shipHash;         // now managed at the current shipped hash
                    }
                    else if (!known)
                    {
                        manifest.Remove(name);             // player-owned copy of a shipped name → leave it
                    }
                    // (known && edited): keep manifest[name] as-is; on-disk != it, so we never touch it.
                }

                // ---- PRUNE retired shipped themes (in manifest, no longer shipped) ----
                foreach (var name in new List<string>(manifest.Keys))
                {
                    if (shipped.ContainsKey(name)) continue; // still shipped
                    string dst = Path.Combine(Dir, name);
                    if (File.Exists(dst))
                    {
                        string dh = CanonHash(dst);
                        if (dh != null && dh == manifest[name]) // untouched since we shipped it
                        {
                            string profName = Path.GetFileNameWithoutExtension(name);
                            File.Delete(dst);
                            // If the pruned theme was active, fall back to the shipped default.
                            if (string.Equals(HudConfig.HudActiveProfile.Value, profName, StringComparison.OrdinalIgnoreCase))
                                HudConfig.HudActiveProfile.Value = (string)HudConfig.HudActiveProfile.DefaultValue;
                            UIALog.Info("Retired shipped HUD theme '" + profName + "' (untouched) removed on update.");
                        }
                        // else: edited by the player (or unparseable) → keep it (it's theirs now)
                    }
                    manifest.Remove(name); // stop tracking (deleted, or kept because the player edited it)
                }

                // Preview art: a <name>.png thumbnail travels beside the profile. Seed-if-absent only
                // (don't churn art) — same no-overwrite rule as before.
                foreach (string f in Directory.GetFiles(src, "*.png"))
                {
                    string dst = Path.Combine(Dir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst);
                }

                SaveManifest(manifest);
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore.SyncShipped: " + e.Message); }
        }

        /// <summary>One-shot (see <see cref="ConfigMigration.PendingThemeTopUp"/>): for EVERY
        /// profile on disk that already carries a theme, append any theme key missing from its
        /// snapshot using the CURRENT global value (<see cref="HudTheme.TopUp"/>), then save. A
        /// THEMELESS profile is left alone — it deliberately means "use whatever globals are
        /// current", and topping it up would silently turn it into a themed profile that stops
        /// following live global edits. Idempotent: re-running adds nothing once every profile's
        /// snapshot already has every key. Deliberately does NOT call <see cref="HudDocument.Sanitize"/>
        /// — this is a narrow theme-dictionary patch, not a full profile load; schema repair still
        /// happens exactly once, in the normal <see cref="Load"/> path, right after this runs.</summary>
        internal static void TopUpAllThemes()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                int filesTouched = 0, keysAdded = 0;
                foreach (string path in Directory.GetFiles(Dir, "*.xml"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    try
                    {
                        var serializer = new XmlSerializer(typeof(HudDocument));
                        HudDocument doc;
                        using (var stream = File.OpenRead(path)) doc = (HudDocument)serializer.Deserialize(stream);
                        if (doc == null || doc.Theme == null || doc.Theme.Count == 0) continue;
                        int added = HudTheme.TopUp(doc.Theme);
                        if (added <= 0) continue;
                        doc.Name = name;
                        if (Save(doc, name)) { filesTouched++; keysAdded += added; }
                    }
                    catch (Exception e)
                    {
                        UIALog.Warn("HudProfileStore theme top-up failed for '" + name + "': " + e.Message);
                    }
                }
                if (filesTouched > 0)
                    UIALog.Info("HUD theme fold (v2->v3): topped up " + filesTouched +
                        " profile(s) with " + keysAdded + " new theme key(s) total.");
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore.TopUpAllThemes: " + e.Message); }
        }

        /// <summary>Hash of a profile's CANONICAL content — deserialize, <see cref="HudDocument.Sanitize"/>
        /// (the exact repair <see cref="Load"/> applies), then reserialize — so Load's idempotent
        /// rewrite-on-open and any cosmetic serialization difference do NOT read as a player edit.
        /// The name is normalized out so a rename can't skew it. Returns null if the file won't
        /// parse; the caller then leaves that file alone (never refreshed, never pruned). Sanitize
        /// is self-contained (no runtime singletons) so this is safe at OnLoaded time.</summary>
        private static string CanonHash(string path)
        {
            try
            {
                var ser = new XmlSerializer(typeof(HudDocument));
                HudDocument doc;
                using (var s = File.OpenRead(path)) doc = (HudDocument)ser.Deserialize(s);
                if (doc == null) return null;
                doc.Sanitize();
                doc.Name = Path.GetFileNameWithoutExtension(path); // name never skews the content hash
                using (var ms = new MemoryStream())
                {
                    ser.Serialize(ms, doc);
                    return Hash(ms.ToArray());
                }
            }
            catch { return null; }
        }

        /// <summary>FNV-1a 64-bit. A change-detection hash, NOT security — chosen over MD5 so it never
        /// throws under a Windows FIPS policy and needs no System.Security.Cryptography.</summary>
        private static string Hash(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        private static Dictionary<string, string> LoadManifest()
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string p = ManifestPath;
                if (!File.Exists(p)) return m;
                foreach (var raw in File.ReadAllLines(p))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int i = line.IndexOf('|');
                    if (i <= 0) continue;
                    string name = line.Substring(0, i).Trim();
                    string hash = line.Substring(i + 1).Trim();
                    if (name.Length > 0 && hash.Length > 0) m[name] = hash;
                }
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore manifest read failed: " + e.Message); }
            return m;
        }

        private static void SaveManifest(Dictionary<string, string> m)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("# Stationeers UI Ascended - shipped-profile provenance. Do not edit.\n");
                sb.Append("# <filename>|<hash>: what the mod SHIPPED, so updates can refresh/retire\n");
                sb.Append("# shipped themes you never touched while leaving your own profiles alone.\n");
                foreach (var kv in m) sb.Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
                File.WriteAllText(ManifestPath, sb.ToString());
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore manifest write failed: " + e.Message); }
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
        private static bool _themeDirty;   // a GLOBAL (theme) setting changed → recapture on next save
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
            FlushNow();          // persist the document we are leaving, under its own name (+ its theme)
            Active = doc;
            _activeName = profileName;
            _dirty = false;      // the incoming document starts clean
            _themeDirty = false;
            _rearm = false;
            // Restore THIS profile's global look — colours, effects, curvature, AND the radial
            // palette — before views rebuild. A themeless profile (null/empty Theme) leaves the
            // globals exactly as they are (the pre-theme behaviour), so nothing regresses.
            try { HudTheme.Apply(doc != null ? doc.Theme : null); }
            catch (Exception e) { UIALog.Warn("HUD theme apply failed: " + e.Message); }
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

        /// <summary>Signal that a GLOBAL setting (HUD palette, radial palette, an effect, curvature,
        /// sizing…) changed while this profile is active, so the next save recaptures the profile's
        /// <see cref="HudDocument.Theme"/> snapshot. Deliberately SEPARATE from
        /// <see cref="MarkChanged"/>: a mere LAYOUT edit must NEVER restamp the theme, or a profile
        /// would silently capture whatever globals happen to be current (e.g. stamping a green theme
        /// onto a blue profile just because you nudged an element). Only an actual global edit — or
        /// the explicit "save theme into profile" button — (re)captures the look.</summary>
        public static void MarkThemeChanged()
        {
            _themeDirty = true;
            _rearm = true;
        }

        /// <summary>Whether the active profile carries its own captured theme (global look).</summary>
        public static bool ActiveHasTheme => Active != null && Active.Theme != null && Active.Theme.Count > 0;

        /// <summary>Make <paramref name="profileName"/> the live document, self-healing a missing or
        /// corrupt file: the starter factory builds the shipped default and it is written back, so an
        /// empty (or damaged) HudProfiles folder always ends up with a usable profile on disk.</summary>
        public static void LoadActive(string profileName, Func<HudDocument> starterFactory)
        {
            // One-shot v2->v3 theme-fold top-up (see ConfigMigration.PendingThemeTopUp): consumed
            // here, on the very first LoadActive of the session, rather than at a fixed point right
            // after SyncShipped — LoadActive is called from more than one place (HudSystem's lazy
            // EnsureActiveDocument, the F9 profile switcher, ProfilesTab), and gating on "the first
            // LoadActive call, whichever caller makes it" guarantees the top-up always runs before
            // ANY profile's theme is captured/applied, without this file needing to know which
            // caller runs first.
            if (ConfigMigration.PendingThemeTopUp)
            {
                ConfigMigration.PendingThemeTopUp = false;
                TopUpAllThemes();
            }
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
            if ((!_dirty && !_themeDirty) || Active == null || string.IsNullOrEmpty(_activeName)) return;
            if (unscaledNow < _saveAt) return;
            try
            {
                if (_themeDirty) Active.Theme = HudTheme.Snapshot(); // recapture the global look
                if (Save(Active, _activeName)) { _dirty = false; _themeDirty = false; }
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
            if ((!_dirty && !_themeDirty) || Active == null || string.IsNullOrEmpty(_activeName)) return;
            try
            {
                if (_themeDirty) Active.Theme = HudTheme.Snapshot();
                if (Save(Active, _activeName)) { _dirty = false; _themeDirty = false; }
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD flush-save failed: " + e.Message);
            }
        }

        /// <summary>Explicitly capture the CURRENT globals into the active profile's theme and save
        /// it now — the F9 "Save theme into this profile" button. Lets a player stamp the look they
        /// have set up onto the active profile without nudging a setting first (the recovery path:
        /// dial in blue globals, click save, and Blue owns them forever after).</summary>
        public static void CaptureThemeNow()
        {
            if (Active == null || string.IsNullOrEmpty(_activeName)) return;
            _themeDirty = true;
            FlushNow();
        }

        /// <summary>Drop the active profile's stored theme so it follows the live globals again
        /// (the pre-theme behaviour) — the F9 "Clear saved theme" action.</summary>
        public static void ClearActiveTheme()
        {
            if (Active == null) return;
            Active.Theme = null;
            _dirty = true;
            _rearm = true;
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
            _themeDirty = false;
            _rearm = false;
            _saveAt = 0f;
            _warned.Clear();
            ActiveReplaced = null;
            // Re-arm the one-shot v2->v3 theme top-up for the NEXT LoadActive if a reload happens
            // to catch it mid-flight (between ConfigMigration.Run and the first LoadActive) — a
            // reload this early in boot is a corner case, but leaving it stuck at false would skip
            // the top-up entirely for the reloaded session. TopUp is additive/idempotent, so running
            // it an extra time (if it had already completed before the reload) costs nothing.
            if (ConfigMigration.Version != null && ConfigMigration.Version.Value < ConfigMigration.CurrentVersion)
                ConfigMigration.PendingThemeTopUp = true;
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
