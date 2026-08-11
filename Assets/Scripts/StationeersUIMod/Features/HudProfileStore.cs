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
    ///
    /// DEV WORKFLOW — "which HUD profile gets shipped": it IS a folder, on purpose. The repo's
    /// <c>HudProfiles/</c> directory (sibling of <c>Assets/</c>) is the single source of truth for
    /// the shipped theme set — <see cref="SyncShipped"/> reads it every launch and every profile
    /// (+ its <c>.png</c> preview) sitting in it ships. To retire a shipped theme, delete it from
    /// that folder (SyncShipped prunes it from players who never edited it — see the method doc).
    /// To add or update one, drop/overwrite the XML (+ optional PNG) there and bump its <c>Schema</c>
    /// if the shape changed. <see cref="ExportToShippedFolder"/> is the author-side convenience: it
    /// copies the ACTIVE profile straight into that folder from the F9/F10 UI, but only when a repo
    /// checkout is actually detectable next to the running mod (never on a Workshop/SLP install —
    /// see that method's doc for the exact probe). Every curated shipped theme ALSO lives embedded
    /// verbatim in <see cref="ShippedProfiles"/> — one <c>BuildXxx</c> factory each, registered in
    /// <see cref="ShippedFactories"/> — as a last-resort self-heal (see <see cref="LoadActive"/>)
    /// for when no mod folder is reachable at all (the F6 ScriptEngine dev flow). Keep those embeds
    /// in sync BY HAND whenever you change a shipped theme's content, and add a factory line for
    /// every theme you add (there is no build step that does either for you).
    ///
    /// READ-ONLY FOR PLAYERS: a theme this mod ships cannot be overwritten from the UI — see
    /// <see cref="Save"/> and <see cref="ShippedReadOnly"/> for the gate, and <c>Core.UiaDevMode</c>
    /// (the <c>uiadev</c> console command) for the author-side unlock.
    /// </summary>
    public static class HudProfileStore
    {
        /// <summary>Seconds of quiet after the last edit before the debounced autosave fires.</summary>
        private const float AutosaveDebounceSeconds = 1.5f;

        /// <summary>Shareable profile folder — global (not per-save), mirrors BagProfileStore's roots.</summary>
        public static string Dir => Path.Combine(Paths.ConfigPath, "StationeersUIMod", "HudProfiles");

        /// <summary>The <c>modDirectory</c> last passed to <see cref="SyncShipped"/> — SLP's install
        /// folder for this mod (local or Workshop), or null under the F6 ScriptEngine dev flow (no
        /// ModData). Cached here (rather than re-threaded through every method) so
        /// <see cref="IsShippedName"/>, <see cref="FeaturedNames"/>, <see cref="RestoreShipped"/> and
        /// the F10 "Restore shipped themes" button can all ask "is the mod folder available right
        /// now" without their callers having to know or pass it.</summary>
        private static string _modDirectory;

        /// <summary>Every shipped theme, embedded verbatim (see <see cref="ShippedProfiles"/>) so
        /// a missing/corrupt config copy self-heals from the REAL design instead of the generic
        /// blank/starter — closes the "broken Stationeers Blue forever" hole (audit 06 P1): before
        /// this, a missing active profile regenerated as the generic starter PERSISTED under the
        /// shipped name, permanently player-owned (SyncShipped would never again touch a file with
        /// that name, because as far as it knew nothing shipped it). Consulted by
        /// <see cref="LoadActive"/>'s self-heal branch ONLY — an ordinary launch gets these files
        /// from the real mod folder via <see cref="SyncShipped"/> instead.</summary>
        private static readonly Dictionary<string, Func<HudDocument>> ShippedFactories =
            new Dictionary<string, Func<HudDocument>>(StringComparer.OrdinalIgnoreCase)
            {
                { ShippedProfiles.StationeersBlueName, ShippedProfiles.BuildStationeersBlue },
                { ShippedProfiles.PureHudName, ShippedProfiles.BuildPureHud },
                // Add a line here for EVERY theme that gains an embed in ShippedProfiles: a shipped
                // theme with no factory regenerates as the generic starter under its own name and is
                // then permanently player-owned (audit 06 P1). One entry per BuildXxx there.
                { ShippedProfiles.StationeersBlueMinimalistName, ShippedProfiles.BuildStationeersBlueMinimalist },
                { ShippedProfiles.ZirillianRedName, ShippedProfiles.BuildZirillianRed },
            };

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
            _modDirectory = modDirectory; // cache regardless of outcome below — IsShippedName/
                                           // FeaturedNames/RestoreShipped read this even when the
                                           // folder turns out to be missing (Directory.Exists guards
                                           // each of those individually; a null/absent value here is
                                           // exactly what tells them to fall back).
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
                    if (IsBrokenBackupName(name)) continue; // quarantined corruption, not a profile
                    try
                    {
                        var serializer = new XmlSerializer(typeof(HudDocument));
                        HudDocument doc;
                        using (var stream = File.OpenRead(path)) doc = (HudDocument)serializer.Deserialize(stream);
                        if (doc == null || doc.Theme == null || doc.Theme.Count == 0) continue;
                        int added = HudTheme.TopUp(doc.Theme);
                        if (added <= 0) continue;
                        doc.Name = name;
                        // SaveFile, not Save: this is OUR one-shot migration, and it has to reach
                        // the shipped themes too (a shipped profile left un-topped-up would follow
                        // whatever globals happen to be live). Not a player edit, so not gated.
                        if (SaveFile(doc, name)) { filesTouched++; keysAdded += added; }
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
                // The folder can vanish UNDER a running session (a player "cleaning up" their config
                // tree with the game open — the 2026-08 playtester incident). Every other write in
                // this file already creates it first; this one relied on its callers having done so,
                // which AdoptShippedIntoManifest only does by accident of call order. Idempotent and
                // cheap, so it belongs here rather than in three call sites.
                Directory.CreateDirectory(Dir);
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
                {
                    string baseName = Path.GetFileNameWithoutExtension(path);
                    // "<name>.broken.xml" matches the *.xml glob but is a QUARANTINED copy of a file
                    // that would not parse (see BackupCorruptProfile), not a profile — never offer it
                    // as loadable, or the switcher fills up with unloadable ghosts of past corruption.
                    if (IsBrokenBackupName(baseName)) continue;
                    list.Add(baseName);
                }
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
                    // SaveFile, not Save: schema repair is OUR write, and it must land on a shipped
                    // theme as readily as on a player's own (a gated call here would leave every
                    // shipped theme re-migrating on every single load, for ever).
                    SaveFile(doc, name);
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

        /// <summary>The Wave C "blank slate": a NEW profile holding nothing but the one element a
        /// layout cannot do without — a follow-global <see cref="HudElementType.HandBoxes"/> (its
        /// Fill/Border/TextColor are left at <see cref="HudElementDef"/>'s own palette-name defaults,
        /// never a literal colour, so it always matches whatever theme is live — rule 2: the bottom
        /// UI is exactly two hand boxes, never more, never fewer). <see cref="HudDocument.RefW"/>/
        /// <see cref="HudDocument.RefH"/> are stamped to the CURRENT screen resolution rather than
        /// left at 0 (which falls back to the 1920x1080 global default) — a fresh blank profile then
        /// starts in perfect 1:1 proportion to whatever the author is actually looking at right now.
        /// Schema is stamped to <see cref="HudDocument.CurrentSchema"/>; Theme is left null (a
        /// themeless profile follows the live globals, which is exactly what "blank" should mean —
        /// nothing captured yet). Returns false (never throws, never overwrites) on an invalid/empty
        /// name or when a profile by that name already exists — this method only ever CREATES.</summary>
        public static bool CreateBlank(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return false;
            try
            {
                if (File.Exists(Path.Combine(Dir, file + ".xml"))) return false; // never clobber

                var doc = new HudDocument
                {
                    Name = file,
                    Schema = HudDocument.CurrentSchema,
                    RefW = UnityEngine.Screen.width,
                    RefH = UnityEngine.Screen.height,
                };
                var hands = new HudElementDef
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = HudElementType.HandBoxes,
                    Anchor = HudAnchor.BottomCenter,
                    X = 0f, Y = 70f, W = 380f, H = 110f,
                    Tiers = HudTierMask.All,
                };
                hands.SetB("tray", false); // just the two boxes, no trapezoid shelf
                doc.Elements.Add(hands);

                // SaveFile: this method only ever CREATES (the never-clobber guard above), so there
                // is no player edit of a shipped theme to refuse — and a blank slate that silently
                // failed to be created would be a worse outcome than a squatted name.
                return SaveFile(doc, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore.CreateBlank('" + name + "') failed: " + e.Message);
                return false;
            }
        }

        /// <summary>PLAYER-FACING save: serialize a document to &lt;name&gt;.xml — unless
        /// <paramref name="name"/> is a theme WE ship and author mode is off, in which case it
        /// refuses cleanly (false, nothing written, one centre-screen notice per theme per session —
        /// see <see cref="ShippedReadOnly"/>).
        ///
        /// <para>THE GATE LIVES HERE, on the one public writer, rather than being sprinkled across
        /// the editor: the F9 element-property commit and the drag-commit fallback
        /// (<c>HudEditorWindow.FlushPendingElementEdit</c>, <c>HudEditorMode.CommitActiveDrag</c>)
        /// write a document DIRECTLY through this method, bypassing the dirty flag entirely, so a
        /// gate that only covered <see cref="Tick"/>/<see cref="FlushNow"/> would leak those two
        /// paths straight onto a shipped file. Both ignore the return value and neither retries, so
        /// refusing is safe there.</para>
        ///
        /// <para>The store's OWN shipped-lifecycle writes deliberately do NOT come through here —
        /// they call <see cref="SaveFile"/> instead (schema repair on load, the embedded-theme
        /// self-heal, the one-shot theme top-up, blank creation, the rename re-stamp). Those write
        /// what we ship, not what a player edited, and gating them would break exactly the
        /// self-healing this file exists to guarantee.</para></summary>
        public static bool Save(HudDocument doc, string name)
        {
            if (ShippedReadOnly(name)) { NoticeReadOnly(name); return false; }
            return SaveFile(doc, name);
        }

        /// <summary>The real writer, UNGATED: serialize a document to &lt;name&gt;.xml, creating the
        /// folder first. Returns false (never throws) on any failure so an autosave tick can degrade
        /// quietly. Private on purpose — everything outside this file goes through
        /// <see cref="Save"/> and is therefore gated; inside it, a caller picks this one only when
        /// it is writing OUR content (see <see cref="Save"/>'s remarks).</summary>
        private static bool SaveFile(HudDocument doc, string name)
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
                _warned.Remove(SaveWarnKey(file)); // a write that lands re-arms the one-shot warning
                return true;
            }
            catch (Exception e)
            {
                // A failed autosave RETRIES every AutosaveDebounceSeconds (see Tick), so warning on
                // every attempt turns a read-only or full config folder into a steady log drip for
                // the whole session. Same WarnOnce idiom Load uses, under its own key so a save
                // problem never silences a load warning for the same profile (or vice versa).
                WarnOnce(SaveWarnKey(file), "HUD profile '" + file + "' failed to save: " + e.Message);
                return false;
            }
        }

        /// <summary>Warn-once key for the SAVE path, deliberately distinct from the LOAD path's bare
        /// file name so one failing direction never suppresses the other's first report.</summary>
        private static string SaveWarnKey(string file)
        {
            return "save:" + file;
        }

        /// <summary>Delete a profile file (+ its preview .png, if any). Hardened with an
        /// ACTIVE-PROFILE GUARD at the store level (audit finding: the old version relied entirely on
        /// the calling UI remembering to check first) — deleting the file backing the LIVE document
        /// out from under it would leave <see cref="Tick"/>/<see cref="FlushNow"/> writing to a name
        /// with nothing behind it and the F9 editor mutating a document with no home. Checked against
        /// both the live <see cref="_activeName"/> AND the config's stored active-profile name (the
        /// two can differ for one frame around a profile switch), so this refuses even if a caller
        /// races the switch. A caller that genuinely wants to delete the active profile must switch
        /// away first (SetActive/LoadActive to something else), then delete.</summary>
        public static bool Delete(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return false;
            try
            {
                if (string.Equals(file, _activeName, StringComparison.OrdinalIgnoreCase)) return false;
                if (HudConfig.HudActiveProfile != null
                    && string.Equals(file, HudConfig.HudActiveProfile.Value, StringComparison.OrdinalIgnoreCase))
                    return false;

                string path = Path.Combine(Dir, file + ".xml");
                if (!File.Exists(path)) return false;
                File.Delete(path);
                _warned.Remove(file);

                string png = Path.Combine(Dir, file + ".png");
                if (File.Exists(png))
                {
                    try { File.Delete(png); }
                    catch (Exception e) { UIALog.Warn("HudProfileStore.Delete: preview art cleanup for '" + file + "' failed: " + e.Message); }
                }

                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD profile '" + file + "' failed to delete: " + e.Message);
                return false;
            }
        }

        /// <summary>Rename a profile on disk: moves &lt;from&gt;.xml (+ its .png preview, if present)
        /// to &lt;to&gt;.xml, re-stamps the document's internal Name, and fixes up the config's active-
        /// profile key plus the live <see cref="Active"/>/<see cref="_activeName"/> pointers when the
        /// renamed file was the active one (so the HUD doesn't silently start pointing at a filename
        /// that no longer exists). Returns false (never throws, never overwrites) on an invalid/empty
        /// name, a missing source, a same-name no-op, or a destination that already exists.
        ///
        /// READ-ONLY GATE: renaming a theme WE ship is a player edit of the shipped set (it moves our
        /// file out from under its own name), so it is refused unless author mode is on — the caller
        /// gets false and the player gets the one-per-theme notice. The paragraph below therefore
        /// describes what happens when FlorpyDorp does it with `uiadev` on, or what happened before
        /// this gate existed. Renaming a profile of the player's OWN is untouched.
        ///
        /// MANIFEST: deliberately left UNTOUCHED. If &lt;from&gt; happened to be a shipped profile name
        /// <see cref="SyncShipped"/> is managing (e.g. "Stationeers Blue"), its manifest entry still
        /// points at that name — but the file there is now gone, so the NEXT SyncShipped pass reads
        /// that as "a managed shipped theme went missing" and reseeds it fresh (the SEED branch).
        /// That is exactly the wanted outcome: the renamed copy at &lt;to&gt; becomes an ordinary,
        /// unmanaged profile (unambiguously the player's own), while the shipped name keeps existing
        /// for next launch instead of vanishing because someone renamed their copy of it. Renaming a
        /// shipped file is therefore never destructive to the shipped set — only to the OLD file's
        /// provenance record, which is the correct thing to lose.</summary>
        public static bool Rename(string from, string to)
        {
            string fFrom = SafeFileName(from);
            string fTo = SafeFileName(to);
            if (fFrom == null || fTo == null) return false;
            if (string.Equals(fFrom, fTo, StringComparison.OrdinalIgnoreCase)) return false;
            if (ShippedReadOnly(fFrom)) { NoticeReadOnly(fFrom); return false; }
            try
            {
                string pathFrom = Path.Combine(Dir, fFrom + ".xml");
                string pathTo = Path.Combine(Dir, fTo + ".xml");
                if (!File.Exists(pathFrom)) return false;
                if (File.Exists(pathTo)) return false; // never clobber an existing profile

                File.Move(pathFrom, pathTo);

                string pngFrom = Path.Combine(Dir, fFrom + ".png");
                string pngTo = Path.Combine(Dir, fTo + ".png");
                if (File.Exists(pngFrom) && !File.Exists(pngTo))
                {
                    try { File.Move(pngFrom, pngTo); }
                    catch (Exception e) { UIALog.Warn("HudProfileStore.Rename: preview art move failed: " + e.Message); }
                }

                // Re-stamp the document's internal Name so a hand-shared file's XML matches its
                // filename (Load() already hands back the FILENAME as Name regardless — this just
                // keeps the on-disk XML itself honest for anyone reading it directly).
                try
                {
                    var doc = Load(fTo);
                    // SaveFile: a bookkeeping re-stamp of the file we just moved, not a player edit
                    // of a shipped name — and fTo is by definition NOT a shipped file (it did not
                    // exist a moment ago), so the gate would be inert here anyway.
                    if (doc != null) SaveFile(doc, fTo);
                }
                catch (Exception e) { UIALog.Warn("HudProfileStore.Rename: internal re-stamp failed: " + e.Message); }

                _warned.Remove(fFrom);
                _warned.Remove(fTo);

                bool wasActive = string.Equals(_activeName, fFrom, StringComparison.OrdinalIgnoreCase)
                    || (HudConfig.HudActiveProfile != null
                        && string.Equals(HudConfig.HudActiveProfile.Value, fFrom, StringComparison.OrdinalIgnoreCase));

                if (HudConfig.HudActiveProfile != null
                    && string.Equals(HudConfig.HudActiveProfile.Value, fFrom, StringComparison.OrdinalIgnoreCase))
                    HudConfig.HudActiveProfile.Value = fTo;

                if (wasActive)
                {
                    _activeName = fTo;
                    if (Active != null) Active.Name = fTo;
                }

                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore.Rename('" + from + "' -> '" + to + "') failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Copy a profile under a new name. Routed through Load/Save (not a raw file copy)
        /// so the duplicate is sanitized and re-stamped rather than inheriting stale XML.
        /// Duplicating a shipped theme is the SUPPORTED way to make it yours, so the source is never
        /// gated; the DESTINATION goes through the gated <see cref="Save"/>, which is what refuses a
        /// duplicate aimed AT one of our names (that would overwrite a shipped theme with other
        /// content under its own name — the same thing the gate stops everywhere else).</summary>
        public static bool Duplicate(string from, string to)
        {
            var doc = Load(from);
            if (doc == null) return false;
            doc.Name = to;
            return Save(doc, to);
        }

        // ---------- shipped-theme management ----------

        /// <summary>Is <paramref name="name"/> one of the themes THIS mod ships? Single source of
        /// truth is the MOD FOLDER itself (the audit's "triplicated shipped-names" complaint — the
        /// old code re-derived this list in three different places, each capable of drifting from
        /// the others): if the mod folder is reachable, the answer is exactly "does it have a file by
        /// this name". When the mod folder is unavailable (F6 ScriptEngine dev flow — no ModData),
        /// falls back to the <see cref="ManifestPath"/> — the record of what a PAST launch's
        /// SyncShipped (or a self-heal write, see <see cref="LoadActive"/>) last shipped/regenerated
        /// under this name, which is the best available answer without the real folder to ask.</summary>
        public static bool IsShippedName(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return false;
            try
            {
                if (!string.IsNullOrEmpty(_modDirectory))
                {
                    string src = Path.Combine(_modDirectory, "HudProfiles");
                    if (Directory.Exists(src))
                        return File.Exists(Path.Combine(src, file + ".xml"));
                }
                var manifest = LoadManifest();
                return manifest.ContainsKey(file + ".xml");
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore.IsShippedName('" + name + "') failed: " + e.Message);
                return false;
            }
        }

        // ---------- the read-only gate for shipped themes ----------

        /// <summary>One notice per THEME per session, so a player who keeps nudging a shipped layout
        /// is told once and then left alone (the same WarnOnce discipline <see cref="_warned"/> uses
        /// for IO failures — a refusal that re-fires every 1.5s autosave window would be a toast
        /// storm). Cleared by <see cref="Shutdown"/>, and by <see cref="ClearReadOnlyNotices"/> when
        /// author mode is switched back off.</summary>
        private static readonly HashSet<string> _readOnlyNoticed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>THE GATE's predicate: is <paramref name="profileName"/> a theme WE ship that the
        /// player may therefore not overwrite? False while <see cref="Core.UiaDevMode.Active"/> (the
        /// `uiadev` author unlock), and false for every profile of the player's own.
        ///
        /// <para>Keyed on <see cref="IsShippedName"/> — the mod folder, falling back to the shipped
        /// manifest — so the protected set is whatever we actually ship RIGHT NOW and no hand-kept
        /// name list can drift from it. Deliberately fail-OPEN: IsShippedName swallows its own IO
        /// errors and answers false, so a probe we cannot make can only ever cost protection, never
        /// make a player's own profiles unsavable.</para>
        ///
        /// <para>Under the F6 ScriptEngine dev flow (no mod folder) the answer comes from the
        /// manifest, which exists only if a normal install ever ran against this config tree or a
        /// self-heal adopted a name — so the gate can be live under F6 too. That is intentional:
        /// `uiadev` is the answer for the one person who edits shipped themes.</para></summary>
        private static bool ShippedReadOnly(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return false;
            if (Core.UiaDevMode.Active) return false;
            return IsShippedName(profileName);
        }

        /// <summary>Tell the player WHY nothing saved — once per theme per session, centre-screen
        /// through the mod's existing toast (a refusal a player cannot see is indistinguishable from
        /// a bug), plus one log line for a report. Never throws: the toast reads Unity's clock, and
        /// this can be reached from a teardown/flush path where that is not worth risking.</summary>
        private static void NoticeReadOnly(string profileName)
        {
            try
            {
                if (!_readOnlyNoticed.Add(profileName ?? string.Empty)) return;
                UIALog.Info("HUD theme '" + profileName + "' is one we ship, so it is read-only: "
                    + "that edit was not saved. Duplicate it to make it yours (`uiadev` unlocks "
                    + "shipped themes for this session).");
                Overlay.Toast.Show("Shipped theme - read-only. Duplicate it to make it yours.",
                    Overlay.Theme.Warn, 4.5f);
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore read-only notice failed: " + e.Message); }
        }

        /// <summary>Re-arm the once-per-theme notice above. Called when `uiadev` turns author mode
        /// back OFF, so the next refused edit explains itself again instead of failing silently
        /// because this session already spent that theme's one toast.</summary>
        internal static void ClearReadOnlyNotices()
        {
            _readOnlyNoticed.Clear();
        }

        /// <summary>THE GATE for the three ACTIVE-DOCUMENT persistence paths (<see cref="Tick"/>,
        /// <see cref="FlushNow"/>, <see cref="SaveActiveNow"/>). Refusing is not enough on its own:
        /// those paths are driven by <see cref="_dirty"/>/<see cref="_themeDirty"/>, and a refusal
        /// that left the flags standing would have Tick re-attempt the same blocked write every
        /// <see cref="AutosaveDebounceSeconds"/> for the rest of the session — a retry loop, and a
        /// disk probe with it. So the pending write is DROPPED here, exactly as if the edit had been
        /// saved and reloaded away.
        ///
        /// <para>The live document keeps the edit IN MEMORY (the HUD goes on showing it until the
        /// next load), which is what makes "duplicate it" the natural next step: Duplicate clones the
        /// LIVE document, so the change the player just made travels into their own copy rather than
        /// being thrown away in front of them.</para>
        ///
        /// <para>Called only AFTER each path's own dirty/deadline checks, so the IsShippedName disk
        /// probe behind it runs at most once per debounce window, never per frame.</para></summary>
        private static bool RefuseActiveSave()
        {
            if (!ShippedReadOnly(_activeName)) return false;
            _dirty = false;
            _themeDirty = false;
            _rearm = false;
            NoticeReadOnly(_activeName);
            return true;
        }

        /// <summary>Can <see cref="RestoreShipped"/> / <see cref="ResetShippedProfiles"/> actually do
        /// anything right now? They copy FROM the mod's installed <c>HudProfiles/</c> folder, which
        /// does not exist under the F6 ScriptEngine dev flow (no ModData, so no
        /// <see cref="_modDirectory"/>). Exposed so the F9/F10 UI can say so BEFORE the player commits
        /// to a two-step confirm, instead of arming a destructive action that can only fail — the
        /// store stays the single source of truth for "is the shipped set reachable" (the UI must not
        /// re-derive it from <c>StationeersUIMod.ModDirectory</c> and drift).</summary>
        public static bool ShippedFolderAvailable
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(_modDirectory)) return false;
                    return Directory.Exists(Path.Combine(_modDirectory, "HudProfiles"));
                }
                catch { return false; }
            }
        }

        /// <summary>The names the Control Center's Profiles tab should feature as cards, single-
        /// sourced here instead of a second hand-maintained list living in the UI (ProfilesTab used
        /// to hardcode "Stationeers Blue"/"Pure HUD" itself). Mod-folder scan first (every profile
        /// the mod actually ships, alphabetical); when that folder is unavailable (F6 dev flow) falls
        /// back to the embedded factory names (<see cref="ShippedFactories"/>) so the featured row is
        /// never empty even without a mod folder to scan.</summary>
        public static List<string> FeaturedNames
        {
            get
            {
                var list = new List<string>();
                try
                {
                    if (!string.IsNullOrEmpty(_modDirectory))
                    {
                        string src = Path.Combine(_modDirectory, "HudProfiles");
                        if (Directory.Exists(src))
                        {
                            foreach (var f in Directory.GetFiles(src, "*.xml"))
                                list.Add(Path.GetFileNameWithoutExtension(f));
                        }
                    }
                }
                catch (Exception e) { UIALog.Warn("HudProfileStore.FeaturedNames mod-folder scan failed: " + e.Message); }

                if (list.Count == 0) list.AddRange(ShippedFactories.Keys);

                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }
        }

        /// <summary>Explicit "reset THIS one shipped theme" — puts &lt;name&gt;'s config copy back to
        /// EXACTLY what the mod folder ships right now (XML + its .png preview, if any), overwriting
        /// any edits the player made to it, and reloads it live if it is the active profile. This is
        /// (along with <see cref="ResetShippedProfiles"/>) one of only two paths in this file allowed
        /// to overwrite a profile the player may have edited — both are explicit, confirmed user
        /// actions, never something a quiet per-launch heuristic would do on its own (that is what
        /// <see cref="SyncShipped"/>'s untouched-only checks are for).
        ///
        /// Fails soft (false, nothing written) when the mod folder is unavailable (F6 dev flow — see
        /// <see cref="_modDirectory"/>) or the mod does not actually ship a profile by this name. This
        /// is a RESTORE from the real folder, not a factory fallback — a player wanting the two
        /// embedded themes reconstructed with NO mod folder at all gets that from
        /// <see cref="LoadActive"/>'s self-heal path instead, the moment they switch (or reload) to
        /// that name.</summary>
        public static bool RestoreShipped(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return false;
            try
            {
                if (string.IsNullOrEmpty(_modDirectory)) return false;
                string src = Path.Combine(_modDirectory, "HudProfiles");
                string srcXml = Path.Combine(src, file + ".xml");
                if (!File.Exists(srcXml)) return false;

                Directory.CreateDirectory(Dir);
                string dstXml = Path.Combine(Dir, file + ".xml");
                File.Copy(srcXml, dstXml, true);

                string srcPng = Path.Combine(src, file + ".png");
                if (File.Exists(srcPng))
                {
                    try { File.Copy(srcPng, Path.Combine(Dir, file + ".png"), true); }
                    catch (Exception e) { UIALog.Warn("HudProfileStore.RestoreShipped: preview art copy for '" + file + "' failed: " + e.Message); }
                }

                string hash = CanonHash(dstXml);
                if (hash != null)
                {
                    var manifest = LoadManifest();
                    manifest[file + ".xml"] = hash;
                    SaveManifest(manifest);
                }
                _warned.Remove(file);

                bool isActive = string.Equals(file, _activeName, StringComparison.OrdinalIgnoreCase)
                    || (HudConfig.HudActiveProfile != null
                        && string.Equals(file, HudConfig.HudActiveProfile.Value, StringComparison.OrdinalIgnoreCase));
                if (isActive)
                {
                    var doc = Load(file);
                    if (doc != null) SetActive(doc, file);
                }

                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore.RestoreShipped('" + name + "') failed: " + e.Message);
                return false;
            }
        }

        /// <summary>The F10 "Restore shipped themes" nuke — deliberately more drastic than
        /// <see cref="RestoreShipped"/>, and reserved for that ONE confirmed button (its own two-step
        /// confirm lives upstream in the UI; arriving here already means the player said yes twice).
        /// Deletes every file <see cref="ManifestPath"/> currently tracks (every shipped theme, this
        /// version's and any future one) plus its preview art, deletes the manifest itself, then
        /// immediately re-runs <see cref="SyncShipped"/> so the wiped set reseeds fresh from the mod
        /// folder in the very same call — the player is never left mid-action with no shipped themes
        /// on disk at all. If the active profile is one of the wiped names, its config entry falls
        /// back to the shipped default (the caller is responsible for reloading the live document —
        /// see HudTab's "Restore shipped themes" handler — this method only touches disk + config).
        /// Fail-soft per file: one delete failure is logged and skipped, the rest (and the reseed)
        /// still proceed.</summary>
        public static void ResetShippedProfiles(string modDirectory)
        {
            try
            {
                // NEVER delete what we cannot put back. The reseed at the bottom is SyncShipped,
                // which returns immediately when the mod's HudProfiles folder is missing — without
                // this guard, a modDirectory that exists but has no HudProfiles subfolder (or an F6
                // session, where there is no mod folder at all) would wipe every tracked theme and
                // reseed nothing, leaving the player with no Pure HUD at all. The UI dims/explains
                // this case up front (see ShippedFolderAvailable); this is the store-level backstop.
                string srcProbe = string.IsNullOrEmpty(modDirectory)
                    ? null : Path.Combine(modDirectory, "HudProfiles");
                if (srcProbe == null || !Directory.Exists(srcProbe))
                {
                    UIALog.Warn("HudProfileStore.ResetShippedProfiles: the mod's HudProfiles folder is "
                        + "unavailable, so nothing was deleted (there would be nothing to restore from).");
                    return;
                }

                var manifest = LoadManifest();
                foreach (var fileName in manifest.Keys)
                {
                    try
                    {
                        string path = Path.Combine(Dir, fileName);
                        string baseName = Path.GetFileNameWithoutExtension(fileName);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            if (HudConfig.HudActiveProfile != null
                                && string.Equals(HudConfig.HudActiveProfile.Value, baseName, StringComparison.OrdinalIgnoreCase))
                                HudConfig.HudActiveProfile.Value = (string)HudConfig.HudActiveProfile.DefaultValue;
                        }
                        string png = Path.Combine(Dir, baseName + ".png");
                        if (File.Exists(png)) File.Delete(png);
                    }
                    catch (Exception e) { UIALog.Warn("HudProfileStore.ResetShippedProfiles: delete '" + fileName + "' failed: " + e.Message); }
                }

                try { if (File.Exists(ManifestPath)) File.Delete(ManifestPath); }
                catch (Exception e) { UIALog.Warn("HudProfileStore.ResetShippedProfiles: manifest delete failed: " + e.Message); }

                SyncShipped(modDirectory); // immediate reseed
            }
            catch (Exception e) { UIALog.Warn("HudProfileStore.ResetShippedProfiles failed: " + e.Message); }
        }

        /// <summary>LAST-resort dev marker: FlorpyDorp's own repo checkout, exactly the same
        /// convention <c>Core.HudShaderStore</c>'s own hardcoded dev bundle path already uses
        /// ("hardcode acceptable ONLY as the final dev fallback"). Neither a Workshop subscription's
        /// cache directory nor the "Install"-flag test copy under
        /// <c>Documents\My Games\Stationeers\mods\</c> (see <c>tools/package.ps1</c>) is ever near
        /// this path, so gating on its existence can never fire on a real player's machine.</summary>
        private const string DevRepoHudProfilesPath = @"C:\Dev\Stationeers UI Ascended\HudProfiles";

        /// <summary>The dev-side half of "which HUD profile gets shipped — could it be as simple as a
        /// folder?" (FlorpyDorp). Copies the profile's config XML (+ its .png preview, if any)
        /// straight into the REPO's <c>HudProfiles/</c> folder, stamping the copy's Schema to
        /// <see cref="HudDocument.CurrentSchema"/> first — the same folder <see cref="SyncShipped"/>
        /// reads every launch, so "export" and "ship it" are the same action.
        ///
        /// The repo folder is located by TWO probes, tried in order, EITHER of which is safe to fire
        /// on a real player's machine only by coincidence-proof construction:
        ///  1. <c>..\..\HudProfiles</c> relative to the running mod's own folder
        ///     (<see cref="_modDirectory"/>) — covers a future/alternate dev flow that loads the mod
        ///     straight out of a repo checkout. Guarded by <c>Directory.Exists</c>, so today's actual
        ///     dev flows (F6 has no ModData at all; <c>tools/package.ps1 -Install</c> copies into
        ///     <c>Documents\My Games\Stationeers\mods\</c>, nowhere near a repo) simply don't match it
        ///     — and neither does a Workshop subscription's isolated cache directory.
        ///  2. <see cref="DevRepoHudProfilesPath"/> — the hardcoded fallback that actually covers
        ///     BOTH of today's real dev flows (F6 dev, and the packaged install-for-testing copy).
        /// Returns the destination path on success, or null (never throws) when neither probe
        /// resolves to a real directory — i.e. on every non-dev install.</summary>
        public static string ExportToShippedFolder(string name)
        {
            string file = SafeFileName(name);
            if (file == null) return null;
            try
            {
                string repoProbe = null;
                if (!string.IsNullOrEmpty(_modDirectory))
                {
                    try
                    {
                        string probe = Path.GetFullPath(Path.Combine(_modDirectory, "..", "..", "HudProfiles"));
                        if (Directory.Exists(probe)) repoProbe = probe;
                    }
                    catch (Exception e) { UIALog.Warn("HudProfileStore.ExportToShippedFolder: modDirectory probe failed: " + e.Message); }
                }
                if (repoProbe == null && Directory.Exists(DevRepoHudProfilesPath))
                    repoProbe = DevRepoHudProfilesPath;
                if (repoProbe == null) return null;

                var doc = Load(file);
                if (doc == null) return null;
                doc.Schema = HudDocument.CurrentSchema;

                var serializer = new XmlSerializer(typeof(HudDocument));
                string dstXml = Path.Combine(repoProbe, file + ".xml");
                using (var stream = File.Create(dstXml))
                    serializer.Serialize(stream, doc);

                string srcPng = Path.Combine(Dir, file + ".png");
                if (File.Exists(srcPng))
                {
                    try { File.Copy(srcPng, Path.Combine(repoProbe, file + ".png"), true); }
                    catch (Exception e) { UIALog.Warn("HudProfileStore.ExportToShippedFolder: preview art copy for '" + file + "' failed: " + e.Message); }
                }

                return dstXml;
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore.ExportToShippedFolder('" + name + "') failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Stamp the manifest entry for a just-self-healed SHIPPED profile directly, instead
        /// of waiting for the next <see cref="SyncShipped"/> pass to notice it (which, this launch,
        /// already ran — before any profile loaded — and will not run again until the NEXT launch).
        /// Without this, a same-session self-heal (see <see cref="LoadActive"/>) would write a file
        /// the manifest has never heard of: untracked, and therefore permanently player-owned — the
        /// EXACT hole this whole change closes. The hash recorded here is CanonHash-identical to
        /// whatever the mod folder ships (same embedded XML, round-tripped through the same
        /// Sanitize+reserialize path CanonHash itself uses), so this is exactly what the next real
        /// SyncShipped pass would have concluded on its own.</summary>
        private static void AdoptShippedIntoManifest(string profileFileName)
        {
            try
            {
                string path = Path.Combine(Dir, profileFileName + ".xml");
                string hash = CanonHash(path);
                if (hash == null) return; // shouldn't happen (we just wrote it) — fail-soft anyway
                var manifest = LoadManifest();
                manifest[profileFileName + ".xml"] = hash;
                SaveManifest(manifest);
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore: manifest adoption for '" + profileFileName + "' failed: " + e.Message);
            }
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
        /// corrupt file. When <paramref name="profileName"/> matches an EMBEDDED shipped theme
        /// (<see cref="ShippedFactories"/> — one entry per theme we ship), it is
        /// regenerated from that REAL factory instead of the caller's generic
        /// <paramref name="starterFactory"/>, and the manifest is stamped directly so it reads as
        /// pristine (see <see cref="AdoptShippedIntoManifest"/>) — audit 06 P1, "broken Stationeers
        /// Blue forever": before this, a missing active profile regenerated as the generic starter
        /// PERSISTED under the shipped name, permanently player-owned, because nothing ever recorded
        /// it as shipped. For any other name, the caller's starter factory builds whatever it
        /// considers the default and it is written back, so an empty (or damaged) HudProfiles folder
        /// always ends up with a usable profile on disk.
        ///
        /// A file that EXISTS but will not parse is copied aside to <c>&lt;name&gt;.broken.xml</c>
        /// before that write lands (<see cref="BackupCorruptProfile"/>) — the mod's hide-never-destroy
        /// rule applies to a player's damaged file too, so self-healing can never eat a layout.</summary>
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
                // Pre-initialised (not just declared) because the `out` is short-circuited away
                // when profileName is null — C# definite-assignment would otherwise reject the
                // read below even though isShippedName guards it.
                Func<HudDocument> shippedFactory = null;
                bool isShippedName = profileName != null
                    && ShippedFactories.TryGetValue(profileName, out shippedFactory);
                bool usedShippedFactory = false;
                if (isShippedName)
                {
                    doc = shippedFactory();
                    usedShippedFactory = doc != null; // only true when the embed actually parsed
                }
                // No shipped factory matched, OR its embed failed to parse: degrade to the caller's
                // generic starter rather than leaving the HUD with nothing at all.
                if (doc == null) doc = starterFactory != null ? starterFactory() : null;
                if (doc == null)
                {
                    UIALog.Warn("HUD profile '" + profileName + "' missing and no starter available.");
                    return;
                }
                doc.Sanitize();
                doc.Name = profileName;
                // Load() returns null for MISSING and for CORRUPT alike. If a file is actually
                // sitting there, it is the second case — quarantine it before this Save overwrites
                // it (no-op when there is nothing on disk). See BackupCorruptProfile.
                BackupCorruptProfile(profileName);
                // SaveFile, and this one is load-bearing: the self-heal writes OUR embedded shipped
                // theme back under its own (shipped) name. Routing it through the gated Save would
                // refuse the write for every player, leaving a missing/corrupt shipped theme with
                // nothing on disk and re-heals it from memory on every single load — audit 06 P1,
                // reopened. This is a shipped-lifecycle write, not a player edit.
                SaveFile(doc, profileName);
                if (usedShippedFactory)
                {
                    string file = SafeFileName(profileName);
                    if (file != null) AdoptShippedIntoManifest(file);
                }
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
            // Read-only gate LAST, so its disk probe only runs on a frame that was about to write.
            // It drops the pending write itself (see RefuseActiveSave) — without that, this deadline
            // would come round every 1.5s for the rest of the session on a blocked profile.
            if (RefuseActiveSave()) return;
            try
            {
                if (_themeDirty) Active.Theme = HudTheme.Snapshot(); // recapture the global look
                if (SaveFile(Active, _activeName)) { _dirty = false; _themeDirty = false; }
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
            // Same gate as Tick. It matters most on the paths that flush BEFORE replacing a file —
            // SetActive, and the UI's flush-then-restore sequence: refusing here is precisely what
            // stops a shipped theme's pending player edits from landing on top of the copy we just
            // restored (the reason those call sites had to flush first in the first place).
            if (RefuseActiveSave()) return;
            try
            {
                if (_themeDirty) Active.Theme = HudTheme.Snapshot();
                if (SaveFile(Active, _activeName)) { _dirty = false; _themeDirty = false; }
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD flush-save failed: " + e.Message);
            }
        }

        /// <summary>Write the live document to disk RIGHT NOW, dirty or not — the repair path (F10 ->
        /// HUD -> Maintenance -> "Repair config folders", see <see cref="ConfigTreeRepair"/>).
        ///
        /// <para>Deliberately NOT <see cref="FlushNow"/>: that returns immediately when nothing is
        /// pending, which is exactly the state a player is in after deleting HudProfiles/ mid-session
        /// without having touched the F9 editor since. Their active profile then lives ONLY in
        /// memory, and no autosave will ever fire for it because there is nothing dirty to save — so
        /// a custom (non-shipped) active profile would be silently lost at the next restart, with the
        /// repair button having reported success. Theme handling matches FlushNow exactly; only the
        /// dirty gate is dropped. Returns false (never throws) when there is no live document.</para></summary>
        public static bool SaveActiveNow()
        {
            if (Active == null || string.IsNullOrEmpty(_activeName)) return false;
            // Gated like the other two. The repair still completes for a shipped active theme: the
            // caller re-runs SyncShipped straight after this, which reseeds a pristine copy of it —
            // a better outcome than writing the player's in-memory edits back under our name. And in
            // the case this button exists for (the whole config tree deleted mid-session) the
            // manifest went with it, so under F6 the gate reads "not shipped" and the write lands.
            if (RefuseActiveSave()) return false;
            try
            {
                if (_themeDirty) Active.Theme = HudTheme.Snapshot();
                if (!SaveFile(Active, _activeName)) return false;
                _dirty = false;
                _themeDirty = false;
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD profile force-save failed: " + e.Message);
                return false;
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
            _readOnlyNoticed.Clear();  // the shipped read-only notice is once per session, and a
                                       // hot reload starts a new one (author mode resets with it)
            ActiveReplaced = null;
            _modDirectory = null; // re-cached by the next SyncShipped call (OnLoaded runs it every
                                   // session, F6 included); cleared here so no narrow boot-time
                                   // window between a reload and that call reads a stale prior
                                   // session's mod-folder path as "still available".
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

        // ---------- corrupt-file quarantine ----------

        /// <summary>Filename marker for a quarantined copy of a profile that would not parse:
        /// <c>&lt;name&gt;.broken.xml</c>, then <c>&lt;name&gt;.broken-2.xml</c>, -3, … for repeats.</summary>
        private const string BrokenMarker = ".broken";

        /// <summary>Does this file BASENAME (no extension) name a quarantine copy — <c>x.broken</c> or
        /// <c>x.broken-7</c>? Used to keep those out of every "*.xml means a profile" enumeration.
        /// (A profile the player deliberately named "something.broken" would be hidden too; that is an
        /// acceptable trade for never listing an unloadable file as loadable.)</summary>
        private static bool IsBrokenBackupName(string fileBaseName)
        {
            if (string.IsNullOrEmpty(fileBaseName)) return false;
            int i = fileBaseName.LastIndexOf(BrokenMarker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            string tail = fileBaseName.Substring(i + BrokenMarker.Length);
            if (tail.Length == 0) return true;          // "<name>.broken"
            if (tail[0] != '-' || tail.Length < 2) return false;
            for (int k = 1; k < tail.Length; k++)
                if (!char.IsDigit(tail[k])) return false;
            return true;                                 // "<name>.broken-2"
        }

        /// <summary>HIDE, NEVER DESTROY — extended to corrupt files. <see cref="LoadActive"/>'s
        /// self-heal writes a fresh document under the failing profile's name, which (when a file
        /// really is there, just unparseable) would silently destroy whatever the player had: a
        /// hand-edit with one bad tag, a half-copied share, a file a disk fault chewed. Copy it to
        /// <c>&lt;name&gt;.broken.xml</c> first (numbered if that exists, so repeated boots never
        /// overwrite the FIRST — most likely still-good — capture) and say so in the log, so a
        /// player who lost work has something to hand back to us.
        ///
        /// No-ops when the file is simply ABSENT (the ordinary fresh-install seed path, nothing to
        /// preserve) or when the name is itself a quarantine copy. Wholly fail-soft: a failure here
        /// warns and returns, never blocking the self-heal that follows.</summary>
        private static void BackupCorruptProfile(string name)
        {
            try
            {
                string file = SafeFileName(name);
                if (file == null || IsBrokenBackupName(file)) return;

                string path = Path.Combine(Dir, file + ".xml");
                if (!File.Exists(path)) return;   // missing, not corrupt — nothing to keep

                string dst = Path.Combine(Dir, file + BrokenMarker + ".xml");
                if (File.Exists(dst))
                {
                    dst = null;
                    for (int n = 2; n <= 20 && dst == null; n++)
                    {
                        string candidate = Path.Combine(Dir, file + BrokenMarker + "-" + n + ".xml");
                        if (!File.Exists(candidate)) dst = candidate;
                    }
                    // Twenty quarantined copies of one profile already: fall back to a unique
                    // timestamp rather than giving up (or clobbering one of the twenty).
                    if (dst == null)
                        dst = Path.Combine(Dir, file + BrokenMarker + "-" + DateTime.UtcNow.Ticks + ".xml");
                }

                File.Copy(path, dst);
                UIALog.Warn("HUD profile '" + file + "' could not be read; the unreadable file was kept as '"
                    + Path.GetFileName(dst) + "' before a fresh one was written in its place.");
            }
            catch (Exception e)
            {
                UIALog.Warn("HudProfileStore: could not back up the unreadable profile '" + name + "': " + e.Message);
            }
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
