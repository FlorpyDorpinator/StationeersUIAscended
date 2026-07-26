using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model (design O5a: cross-save portability) ----------

    /// <summary>
    /// A LOADOUT: a small GLOBAL file mapping bag identity BY STRUCTURE (bag prefab + occurrence
    /// index in worn-scan order) to a profile name — "the first mining belt I wear: Ores". Unlike
    /// assignments (keyed on the save-scoped <c>Thing.ReferenceId</c>), a loadout survives new
    /// saves, replaced bags and friends' servers, because it never references an instance.
    ///
    /// One loadout per file under <c>config/StationeersUIMod/Loadouts/</c>, own XML root, so old
    /// mod builds never read or rewrite these files (the same isolation prefab-defaults.xml uses).
    /// Applying a loadout is ALWAYS a chosen, manual action (FlorpyDorp Q4 — no auto-apply on new
    /// saves, ever) and writes ONLY per-save assignments through <see cref="BagProfileStore.Assign"/>
    /// — pure local config, no game-state mutation, no MP concern.
    /// </summary>
    [XmlRoot("Loadout")]
    public class Loadout
    {
        [XmlAttribute("name")] public string Name;
        [XmlElement("Entry")] public List<LoadoutEntry> Entries = new List<LoadoutEntry>();

        /// <summary>The file this loadout was loaded from (set by the loader, never serialized).
        /// Delete targets it so a hand-renamed file is still removed correctly.</summary>
        [XmlIgnore] public string SourceFile;
    }

    public class LoadoutEntry
    {
        [XmlAttribute("prefab")] public string Prefab;            // bag PrefabName
        /// <summary>Which same-prefab bag this entry means: 0 = the first bag of this prefab in
        /// worn-scan order, 1 = the second, ... Counted over ALL worn bags of that prefab
        /// (assigned or not), so the index means "the Nth mining belt you wear" on both the save
        /// side and the apply side.</summary>
        [XmlAttribute("occurrence")] public int Occurrence;
        [XmlAttribute("profile")] public string ProfileName;
    }

    /// <summary>What <see cref="LoadoutStore.Apply"/> did, for the UI's result note.</summary>
    public struct LoadoutApplyResult
    {
        public int Applied;          // assignments written
        public int MissingBags;      // entries whose prefab+occurrence is not currently worn
        public int MissingProfiles;  // entries whose named profile does not exist (nothing assigned)
    }

    // ---------- Store ----------

    /// <summary>
    /// Disk I/O + matching for loadouts. STATELESS by design: no cached list, no Unity references
    /// held in statics — every UI access re-reads the directory (a handful of tiny XML files at
    /// F10-rebuild frequency), so there is nothing to reset on hot reload. All game reads are
    /// networked-only (PrefabName, the worn-inventory scan); the only writes are XML files and
    /// per-save assignments through the existing <see cref="BagProfileStore"/> funnel.
    /// </summary>
    public static class LoadoutStore
    {
        public static string LoadoutsDir => Path.Combine(BagProfileStore.ConfigDir, "Loadouts");

        // Path.GetInvalidFileNameChars() allocates per call; loadout ops are gesture-frequency,
        // but hold it anyway (same idiom as BagProfileStore).
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// THE canonical worn-bag enumeration — the one order both loadout capture and apply key
        /// their occurrence indices on, and the same list the F10 "Your bags" section shows (it
        /// delegates here), so the numbering the player can see is the numbering loadouts use.
        /// A "bag" is any occupant with 2+ slots within scan depth 2 (worn equipment + one level
        /// of nesting), first-seen order, de-duplicated. Read-only scan.
        /// </summary>
        public static void CollectWornBags(List<DynamicThing> into)
        {
            if (into == null) return;
            into.Clear();
            try
            {
                if (Guards.LocalHuman == null) return;
                foreach (var s in InventoryScanner.Scan(2, false))
                {
                    var b = s.Occupant;
                    if (b == null || b.Slots == null || b.Slots.Count < 2) continue;
                    if (!into.Contains(b)) into.Add(b);
                }
            }
            catch { }
        }

        /// <summary>Read every <c>Loadouts/*.xml</c> into <paramref name="into"/> (cleared first),
        /// file-name order, fail-soft per file. Nameless/broken files are skipped with a warning,
        /// never deleted.</summary>
        public static void LoadAll(List<Loadout> into)
        {
            if (into == null) return;
            into.Clear();
            try
            {
                Directory.CreateDirectory(LoadoutsDir);
                var files = Directory.GetFiles(LoadoutsDir, "*.xml");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    // TryLoad (not Load) because this store names the offending FILE in its warning
                    // rather than using the shared "<label> load failed" line.
                    Loadout parsed;
                    string error;
                    if (!SaveScopedXmlStore.TryLoad(file, out parsed, out error))
                    {
                        if (error != null)
                            UIALog.Warn($"Loadout file '{Path.GetFileName(file)}' failed to parse: {error}");
                        continue;
                    }
                    if (string.IsNullOrEmpty(parsed.Name)) continue;
                    if (parsed.Entries == null) parsed.Entries = new List<LoadoutEntry>();
                    parsed.SourceFile = file;
                    into.Add(parsed);
                }
            }
            catch (Exception e)
            {
                UIALog.Error("Loadout load failed: " + e);
            }
        }

        /// <summary>
        /// Capture the CURRENT worn arrangement as a new loadout: every worn bag that has an
        /// assignment becomes one entry (prefab + occurrence + assigned profile name). The
        /// assignment is captured even when the named profile is missing right now (the name is
        /// the identity; apply reports it as missing instead of silently dropping it). Auto-named
        /// "Loadout N" (first free N) and saved to its own file. Returns null when no worn bag
        /// carries an assignment — nothing worth saving.
        /// </summary>
        public static Loadout SaveCurrentAsNew()
        {
            var bags = new List<DynamicThing>();
            CollectWornBags(bags);
            if (bags.Count == 0) return null;

            var lo = new Loadout();
            var occurrenceOf = new Dictionary<string, int>();
            foreach (var bag in bags)
            {
                string prefab = null;
                try { prefab = bag.PrefabName; } catch { }
                if (string.IsNullOrEmpty(prefab)) continue;
                int n;
                occurrenceOf.TryGetValue(prefab, out n);
                occurrenceOf[prefab] = n + 1;   // EVERY bag of the prefab advances the index (see LoadoutEntry.Occurrence)
                string assigned = BagProfileStore.GetAssignedProfileName(bag);
                if (string.IsNullOrEmpty(assigned)) continue;
                lo.Entries.Add(new LoadoutEntry { Prefab = prefab, Occurrence = n, ProfileName = assigned });
            }
            if (lo.Entries.Count == 0) return null;

            lo.Name = NextAutoName();
            return Save(lo) ? lo : null;
        }

        /// <summary>
        /// MANUALLY apply a loadout (Q4: never automatic): re-scan the worn bags, match each entry
        /// by prefab + occurrence in the canonical order, and write the per-save assignment when
        /// the named profile exists. Entries whose bag is not worn right now, or whose profile is
        /// gone, are counted in the result and SKIPPED — apply never assigns a dangling name and
        /// never clears an assignment it has no entry for. Config writes only; nothing moves.
        /// </summary>
        public static LoadoutApplyResult Apply(Loadout lo)
        {
            var result = new LoadoutApplyResult();
            if (lo == null || lo.Entries == null) return result;

            var bags = new List<DynamicThing>();
            CollectWornBags(bags);
            foreach (var entry in lo.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Prefab) || string.IsNullOrEmpty(entry.ProfileName))
                    continue;
                DynamicThing bag = FindByOccurrence(bags, entry.Prefab, entry.Occurrence);
                if (bag == null) { result.MissingBags++; continue; }
                if (BagProfileStore.FindProfile(entry.ProfileName) == null) { result.MissingProfiles++; continue; }
                BagProfileStore.Assign(bag, entry.ProfileName);   // per-save config write, nothing else
                result.Applied++;
            }
            return result;
        }

        /// <summary>Write a loadout to <c>Loadouts/&lt;safe-name&gt;.xml</c> (overwrite by name).</summary>
        public static bool Save(Loadout lo)
        {
            if (lo == null || string.IsNullOrEmpty(lo.Name)) return false;
            try
            {
                string safe = SafeFileToken(lo.Name);
                if (safe.Length == 0) safe = "loadout";
                string path = Path.Combine(LoadoutsDir, safe + ".xml");
                if (!SaveScopedXmlStore.Save(path, lo, "Loadout")) return false;
                lo.SourceFile = path;
                UIALog.Info($"Saved loadout '{lo.Name}' ({lo.Entries.Count} entries) to {path}");
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Loadout save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Delete a loadout's file (the one it was loaded from when known). The in-memory
        /// object simply goes stale; the UI re-lists from disk on its next rebuild.</summary>
        public static bool Delete(Loadout lo)
        {
            if (lo == null) return false;
            try
            {
                string path = lo.SourceFile;
                if (string.IsNullOrEmpty(path))
                    path = Path.Combine(LoadoutsDir, SafeFileToken(lo.Name ?? string.Empty) + ".xml");
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Loadout delete failed: " + e.Message);
            }
            return false;
        }

        /// <summary>"Loadout 1" -> "Loadout 2" -> ... first name not already on disk.</summary>
        private static string NextAutoName()
        {
            var existing = new List<Loadout>();
            LoadAll(existing);
            for (int n = 1; n < 1000; n++)
            {
                string candidate = "Loadout " + n;
                bool taken = false;
                for (int i = 0; i < existing.Count; i++)
                    if (existing[i] != null && string.Equals(existing[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        taken = true;
                        break;
                    }
                if (!taken) return candidate;
            }
            return "Loadout " + DateTime.UtcNow.Ticks;   // degenerate fallback, never expected
        }

        private static DynamicThing FindByOccurrence(List<DynamicThing> bags, string prefab, int occurrence)
        {
            int seen = 0;
            for (int i = 0; i < bags.Count; i++)
            {
                var b = bags[i];
                string pn = null;
                try { pn = b != null ? b.PrefabName : null; } catch { }
                if (pn != prefab) continue;
                if (seen == occurrence) return b;
                seen++;
            }
            return null;
        }

        private static string SafeFileToken(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string name = raw;
            foreach (char c in InvalidNameChars) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
