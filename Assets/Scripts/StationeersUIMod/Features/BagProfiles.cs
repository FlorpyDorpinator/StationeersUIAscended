using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Assets.Scripts.Objects;
using Assets.Scripts.Serialization;
using BepInEx;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML models (proposal §9.3, extended) ----------

    [XmlRoot("BagProfiles")]
    public class BagProfileFile
    {
        [XmlElement("BagProfile")] public List<BagProfile> Profiles = new List<BagProfile>();
    }

    public class BagProfile
    {
        [XmlAttribute("name")] public string Name;
        /// <summary>Optional hand-set manila-tab badge override (2-4 uppercase ASCII chars,
        /// design Q5). Null = derive from the name via <see cref="BagProfileStore.ProfileTag"/>.
        /// XmlSerializer omits the attribute when null, so old files round-trip unchanged;
        /// an OLD mod build re-saving profiles.xml silently drops it (known, accepted).</summary>
        [XmlAttribute("badge")] public string Badge;
        [XmlElement("Item")] public List<ItemRule> Items = new List<ItemRule>();
        [XmlElement("Category")] public List<CategoryRule> Categories = new List<CategoryRule>();
        [XmlElement("SlotClass")] public List<SlotClassRule> SlotClasses = new List<SlotClassRule>();

        /// <summary>Best-match priority for a thing, or null when nothing matches.
        /// Precedence: explicit item > slot class > sorting category (ties by priority value).
        /// Runs at resolve frequency (router stages, the ~4 Hz ghost-hint dry-run) AND inside the
        /// profile-aware sort comparator, so the rule-name enum parses are CACHED per rule — on
        /// net48 Mono <c>Enum.TryParse</c> allocates (split scratch + boxing) on every call.</summary>
        public int? Match(DynamicThing thing)
        {
            if (thing == null) return null;
            int? best = null;
            foreach (var rule in Items)
                if (!string.IsNullOrEmpty(rule.Prefab) && rule.Prefab == thing.PrefabName)
                    best = Max(best, rule.Priority + 20000);
            foreach (var rule in SlotClasses)
                if (rule.TryGetSlotClass(out Slot.Class cls) && thing.SlotType == cls)
                    best = Max(best, rule.Priority + 10000);
            foreach (var rule in Categories)
                if (rule.TryGetSortingClass(out SortingClass sc) && thing.SortingClass == sc)
                    best = Max(best, rule.Priority);
            return best;
        }

        private static int? Max(int? a, int b) => a.HasValue ? Math.Max(a.Value, b) : b;
    }

    public class ItemRule
    {
        [XmlAttribute("prefab")] public string Prefab;
        [XmlAttribute("priority")] public int Priority = 100;
    }

    public class CategoryRule
    {
        [XmlAttribute("name")] public string Name; // SortingClass name
        [XmlAttribute("priority")] public int Priority = 50;

        // Cached parse of Name (see BagProfile.Match — Enum.TryParse allocates per call on
        // net48 Mono). Keyed on the string INSTANCE, so a re-loaded or re-assigned Name
        // re-parses automatically; ignore-case XML tolerance is unchanged.
        private string _parsedFor;
        private bool _parsedOk;
        private SortingClass _parsed;

        public bool TryGetSortingClass(out SortingClass sc)
        {
            if (!ReferenceEquals(_parsedFor, Name))
            {
                _parsedOk = Enum.TryParse(Name, true, out _parsed);
                _parsedFor = Name;
            }
            sc = _parsed;
            return _parsedOk;
        }
    }

    public class SlotClassRule
    {
        [XmlAttribute("name")] public string Name; // Slot.Class name
        [XmlAttribute("priority")] public int Priority = 60;

        // Cached parse of Name — same rationale/semantics as CategoryRule's cache.
        private string _parsedFor;
        private bool _parsedOk;
        private Slot.Class _parsed;

        public bool TryGetSlotClass(out Slot.Class cls)
        {
            if (!ReferenceEquals(_parsedFor, Name))
            {
                _parsedOk = Enum.TryParse(Name, true, out _parsed);
                _parsedFor = Name;
            }
            cls = _parsed;
            return _parsedOk;
        }
    }

    // ---------- priority tri-state (design O6) ----------

    /// <summary>The player-facing view of a rule's Priority int. Full numeric editing stays
    /// XML-only; the F10 UI exposes only this tri-state.</summary>
    public enum RuleTier
    {
        Low,    // 10
        Normal, // 50
        High,   // 100
    }

    /// <summary>High/Normal/Low &lt;-&gt; Priority int mapping. Tolerant of hand-edited XML:
    /// any int snaps to the nearest bucket (midpoints 30 and 75, ties round up), so the
    /// existing defaults read sensibly (Item 100 = High, Category 50 = Normal, SlotClass
    /// 60 = Normal) and a hand-tuned 95 still displays as High without being rewritten
    /// until the player actually clicks the tri-state.</summary>
    public static class RuleTiers
    {
        public const int LowValue = 10;
        public const int NormalValue = 50;
        public const int HighValue = 100;

        /// <summary>Nearest bucket for any Priority int (hand-edited values included).</summary>
        public static RuleTier TierOf(int priority)
        {
            if (priority < 30) return RuleTier.Low;
            if (priority < 75) return RuleTier.Normal;
            return RuleTier.High;
        }

        public static int ValueOf(RuleTier tier)
        {
            if (tier == RuleTier.Low) return LowValue;
            if (tier == RuleTier.High) return HighValue;
            return NormalValue;
        }

        /// <summary>Click-to-cycle order for a compact UI: Low -&gt; Normal -&gt; High -&gt; Low.</summary>
        public static RuleTier Next(RuleTier tier)
        {
            if (tier == RuleTier.Low) return RuleTier.Normal;
            if (tier == RuleTier.Normal) return RuleTier.High;
            return RuleTier.Low;
        }

        public static string Label(RuleTier tier)
        {
            if (tier == RuleTier.Low) return "Low";
            if (tier == RuleTier.High) return "High";
            return "Normal";
        }
    }

    // ---------- prefab defaults (design O5b: "all bags of this prefab") ----------

    /// <summary>Own root + OWN FILE (config/StationeersUIMod/prefab-defaults.xml — deliberately
    /// OUTSIDE the Profiles dir, whose *.xml glob loader would warn on the foreign root every
    /// start; also confines the old-build round-trip risk: old builds never write this file).</summary>
    [XmlRoot("PrefabDefaults")]
    public class PrefabDefaultFile
    {
        [XmlElement("Default")] public List<PrefabDefault> Defaults = new List<PrefabDefault>();
    }

    public class PrefabDefault
    {
        [XmlAttribute("prefab")] public string Prefab;        // bag PrefabName
        [XmlAttribute("profile")] public string ProfileName;  // profile it should behave like
    }

    [XmlRoot("BagAssignments")]
    public class AssignmentFile
    {
        [XmlElement("Assign")] public List<Assignment> Assignments = new List<Assignment>();
        [XmlElement("Memory")] public List<TypeMemory> Memories = new List<TypeMemory>();
    }

    public class Assignment
    {
        [XmlAttribute("bagRef")] public long BagReferenceId;
        [XmlAttribute("profile")] public string ProfileName;
    }

    public class TypeMemory
    {
        [XmlAttribute("prefabHash")] public int PrefabHash;
        [XmlAttribute("bagRef")] public long BagReferenceId;
    }

    // ---------- Store ----------

    /// <summary>
    /// Profile definitions are GLOBAL (shareable XML files under BepInEx/config), while
    /// bag assignments and item-type memory are PER SAVE (bag ReferenceIds are save-scoped).
    /// </summary>
    public static class BagProfileStore
    {
        public static readonly List<BagProfile> Profiles = new List<BagProfile>();
        private static readonly Dictionary<long, string> Assignments = new Dictionary<long, string>();
        private static readonly Dictionary<int, long> Memory = new Dictionary<int, long>();

        // Global bag-prefab -> profile-name defaults (own file; see PrefabDefaultFile).
        private static readonly Dictionary<string, string> _prefabDefaults = new Dictionary<string, string>();
        private static bool _prefabDefaultsLoaded;

        // profileName -> manila-tab badge tag; rebuilt lazily, cleared on every profile
        // mutation (Load/Save/Rename all clear it). Cached so per-frame tab rendering is
        // a plain dictionary hit with zero allocation.
        private static readonly Dictionary<string, string> _tagCache = new Dictionary<string, string>();

        private static string _loadedSaveKey;

        // Path.GetInvalidFileNameChars() allocates a fresh char[] on every call; the save key
        // is re-derived on a per-frame path (DigitForBag while a bag radial is open), so hold it.
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

        public static string ConfigDir => Path.Combine(Paths.ConfigPath, "StationeersUIMod");
        public static string ProfilesDir => Path.Combine(ConfigDir, "Profiles");
        public static string AssignmentsDir => Path.Combine(ConfigDir, "Assignments");

        public static void LoadProfiles()
        {
            Profiles.Clear();
            _tagCache.Clear();
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                var files = Directory.GetFiles(ProfilesDir, "*.xml");
                if (files.Length == 0)
                {
                    WriteStarterProfiles();
                    files = Directory.GetFiles(ProfilesDir, "*.xml");
                }
                // Canonical-last + dedupe-by-name: SaveProfiles serialises the whole merged
                // list into profiles.xml, so any profile that ALSO lives in a shared drop-in
                // file used to duplicate on every load+save cycle (latent while saves were
                // rare; capture makes SaveProfiles routine). Loading profiles.xml LAST with
                // same-name-replaces semantics keeps exactly one copy per name, and a STALE
                // drop-in copy (your own leftover export) loses to the canonical file.
                // IMPORT EXCEPTION: a drop-in file that is NEWER than profiles.xml wins over
                // the canonical copy — that is the F10 sharing flow ("drop a received profile
                // file in the folder and hit Reload profiles"), which canonical-always-wins
                // silently discarded (verified defect, 2026-07-20). The next SaveProfiles then
                // serialises the imported version into profiles.xml, making it canonical.
                // Drop-in files themselves are never written or deleted.
                Array.Sort(files, CompareProfileFiles);
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                var sourceTimes = new Dictionary<string, DateTime>(); // profile name -> mtime of the file it came from
                foreach (var file in files)
                {
                    try
                    {
                        bool canonical = string.Equals(Path.GetFileName(file), "profiles.xml", StringComparison.OrdinalIgnoreCase);
                        DateTime mtime;
                        try { mtime = File.GetLastWriteTimeUtc(file); }
                        catch { mtime = DateTime.MinValue; }
                        using (var stream = File.OpenRead(file))
                        {
                            var parsed = (BagProfileFile)serializer.Deserialize(stream);
                            if (parsed?.Profiles == null) continue;
                            foreach (var p in parsed.Profiles)
                                if (p != null && !string.IsNullOrEmpty(p.Name))
                                    AddOrReplaceProfile(p, canonical, mtime, sourceTimes);
                        }
                    }
                    catch (Exception e)
                    {
                        UIALog.Warn($"Bag profile file '{Path.GetFileName(file)}' failed to parse: {e.Message}");
                    }
                }
                UIALog.Info($"Loaded {Profiles.Count} bag profile(s).");
            }
            catch (Exception e)
            {
                UIALog.Error("LoadProfiles failed: " + e);
            }
            LoadPrefabDefaults(); // clear + reload alongside profiles (hot-reload self-heal)
        }

        private static int CompareProfileFiles(string a, string b)
        {
            bool aCanon = string.Equals(Path.GetFileName(a), "profiles.xml", StringComparison.OrdinalIgnoreCase);
            bool bCanon = string.Equals(Path.GetFileName(b), "profiles.xml", StringComparison.OrdinalIgnoreCase);
            if (aCanon != bCanon) return aCanon ? 1 : -1; // profiles.xml sorts last (it wins)
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Merge one loaded profile into <see cref="Profiles"/>. Later files replace
        /// same-name earlier ones (list order stays stable) — EXCEPT the canonical profiles.xml,
        /// which only replaces a drop-in copy that is NOT newer than itself: a freshly received
        /// drop-in (mtime after the last save) is an IMPORT and survives the canonical pass.</summary>
        private static void AddOrReplaceProfile(BagProfile profile, bool canonical, DateTime mtime,
            Dictionary<string, DateTime> sourceTimes)
        {
            for (int i = 0; i < Profiles.Count; i++)
            {
                var existing = Profiles[i];
                if (existing != null && existing.Name == profile.Name)
                {
                    DateTime prev;
                    if (canonical && sourceTimes.TryGetValue(profile.Name, out prev) && prev > mtime)
                    {
                        // The drop-in copy is newer than profiles.xml: keep the import.
                        UIALog.Info($"Profile '{profile.Name}': drop-in copy is newer than profiles.xml - using the import (the next save makes it canonical).");
                        return;
                    }
                    Profiles[i] = profile; // later file wins; list order stays stable
                    sourceTimes[profile.Name] = mtime;
                    return;
                }
            }
            Profiles.Add(profile);
            sourceTimes[profile.Name] = mtime;
        }

        public static void SaveProfiles()
        {
            _tagCache.Clear(); // names/badges may have changed; tags re-derive lazily
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                var path = Path.Combine(ProfilesDir, "profiles.xml");
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, new BagProfileFile { Profiles = Profiles.ToList() });
                UIALog.Info("Saved bag profiles to " + path);
            }
            catch (Exception e)
            {
                UIALog.Error("SaveProfiles failed: " + e);
            }
        }

        /// <summary>O5c share: write ONE profile as its own drop-in file
        /// <c>Profiles/&lt;safe-name&gt;.xml</c> — the multi-file loader already reads it back, and
        /// the dedupe-by-name merge means re-importing an export of a profile you still have never
        /// duplicates it (whichever copy wins by the canonical/newer rule, there is one per name;
        /// a stale export loses to profiles.xml, a fresh one is content-identical). Guard: an
        /// export may NEVER land on the canonical
        /// profiles.xml itself (a profile literally named "profiles" would overwrite the whole
        /// store). Returns the written file NAME, or null on failure/unknown profile.</summary>
        public static string ExportProfile(string profileName)
        {
            var profile = FindProfile(profileName);
            if (profile == null) return null;
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                string safe = profileName;
                foreach (char c in InvalidNameChars) safe = safe.Replace(c, '_');
                safe = safe.Trim();
                if (safe.Length == 0) safe = "profile";
                if (string.Equals(safe, "profiles", StringComparison.OrdinalIgnoreCase))
                    safe = "profiles-export";
                string file = safe + ".xml";
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                using (var stream = File.Create(Path.Combine(ProfilesDir, file)))
                    serializer.Serialize(stream, new BagProfileFile { Profiles = new List<BagProfile> { profile } });
                UIALog.Info("Exported profile '" + profileName + "' to Profiles/" + file);
                return file;
            }
            catch (Exception e)
            {
                UIALog.Warn("Profile export failed: " + e.Message);
                return null;
            }
        }

        /// <summary>First loaded profile with this exact name, or null. Allocation-free
        /// (no LINQ) — safe at resolve frequency, though still not for per-frame loops.</summary>
        public static BagProfile FindProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < Profiles.Count; i++)
            {
                var p = Profiles[i];
                if (p != null && p.Name == name) return p;
            }
            return null;
        }

        /// <summary>"Mining Belt" -> "Mining Belt 2" -> "Mining Belt 3"... until unused.
        /// Profile identity IS the raw name string (assignments and prefab defaults point
        /// at it), so anything creating profiles must come through here.</summary>
        public static string UniqueProfileName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Profile";
            if (FindProfile(baseName) == null) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = baseName + " " + n;
                if (FindProfile(candidate) == null) return candidate;
            }
            return baseName + " " + DateTime.UtcNow.Ticks; // degenerate fallback, never expected
        }

        /// <summary>Rename a profile AND every pointer to it that this machine can see:
        /// current-save assignments and global prefab defaults. Assignments in OTHER saves'
        /// files keep the old name and dangle — fail-soft (the profile stage just skips
        /// them), same scoping as the rest of the store. False on missing source name or
        /// name collision (caller should pick another name, e.g. via UniqueProfileName).</summary>
        public static bool RenameProfile(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return false;
            var profile = FindProfile(oldName);
            if (profile == null || FindProfile(newName) != null) return false;
            profile.Name = newName;

            EnsureSaveLoaded();
            List<long> assignKeys = null;
            foreach (var kv in Assignments)
                if (kv.Value == oldName)
                    (assignKeys ?? (assignKeys = new List<long>())).Add(kv.Key);
            if (assignKeys != null)
            {
                foreach (long key in assignKeys) Assignments[key] = newName;
                SaveAssignments();
            }

            EnsurePrefabDefaultsLoaded();
            List<string> defaultKeys = null;
            foreach (var kv in _prefabDefaults)
                if (kv.Value == oldName)
                    (defaultKeys ?? (defaultKeys = new List<string>())).Add(kv.Key);
            if (defaultKeys != null)
            {
                foreach (string key in defaultKeys) _prefabDefaults[key] = newName;
                SavePrefabDefaults();
            }

            SaveProfiles();
            return true;
        }

        // --- per-save state ---

        public static string CurrentSaveKey()
        {
            try
            {
                var save = XmlSaveLoad.Instance;
                string name = save?.CurrentStationName;
                if (string.IsNullOrEmpty(name)) name = save?.CurrentWorldSave?.Name;
                if (string.IsNullOrEmpty(name)) name = "unsaved";
                foreach (char c in InvalidNameChars) name = name.Replace(c, '_');
                return name;
            }
            catch
            {
                return "unsaved";
            }
        }

        public static void EnsureSaveLoaded()
        {
            string key = CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            Assignments.Clear();
            Memory.Clear();
            try
            {
                var path = Path.Combine(AssignmentsDir, key + ".xml");
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(AssignmentFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (AssignmentFile)serializer.Deserialize(stream);
                    foreach (var a in parsed.Assignments)
                        Assignments[a.BagReferenceId] = a.ProfileName;
                    foreach (var m in parsed.Memories)
                        Memory[m.PrefabHash] = m.BagReferenceId;
                }
                UIALog.Info($"Loaded {Assignments.Count} bag assignment(s) for save '{key}'.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Assignment load failed: " + e.Message);
            }
        }

        public static void SaveAssignments()
        {
            try
            {
                Directory.CreateDirectory(AssignmentsDir);
                var path = Path.Combine(AssignmentsDir, (_loadedSaveKey ?? CurrentSaveKey()) + ".xml");
                var file = new AssignmentFile
                {
                    Assignments = Assignments.Select(kv => new Assignment { BagReferenceId = kv.Key, ProfileName = kv.Value }).ToList(),
                    Memories = Memory.Select(kv => new TypeMemory { PrefabHash = kv.Key, BagReferenceId = kv.Value }).ToList(),
                };
                var serializer = new XmlSerializer(typeof(AssignmentFile));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("Assignment save failed: " + e.Message);
            }
        }

        public static BagProfile GetAssignedProfile(Thing bag)
        {
            if (bag == null) return null;
            EnsureSaveLoaded();
            if (!Assignments.TryGetValue(bag.ReferenceId, out var name)) return null;
            return FindProfile(name);
        }

        public static string GetAssignedProfileName(Thing bag)
        {
            if (bag == null) return null;
            EnsureSaveLoaded();
            Assignments.TryGetValue(bag.ReferenceId, out var name);
            return name;
        }

        public static void Assign(Thing bag, string profileName)
        {
            if (bag == null) return;
            EnsureSaveLoaded();
            if (string.IsNullOrEmpty(profileName)) Assignments.Remove(bag.ReferenceId);
            else Assignments[bag.ReferenceId] = profileName;
            SaveAssignments();
        }

        public static void RememberStow(DynamicThing item, Thing bag)
        {
            if (item == null || bag == null) return;
            EnsureSaveLoaded();
            Memory[item.PrefabHash] = bag.ReferenceId;
            SaveAssignments();
        }

        public static long? RecallStow(DynamicThing item)
        {
            if (item == null) return null;
            EnsureSaveLoaded();
            return Memory.TryGetValue(item.PrefabHash, out var bagRef) ? bagRef : (long?)null;
        }

        // --- prefab defaults (global; consulted by StowRouter's bag-type-default stage
        //     BEFORE its shipped table, so user choices override shipped ones) ---

        public static string PrefabDefaultsPath => Path.Combine(ConfigDir, "prefab-defaults.xml");

        private static void EnsurePrefabDefaultsLoaded()
        {
            if (!_prefabDefaultsLoaded) LoadPrefabDefaults();
        }

        public static void LoadPrefabDefaults()
        {
            _prefabDefaults.Clear();
            _prefabDefaultsLoaded = true;
            try
            {
                var path = PrefabDefaultsPath;
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(PrefabDefaultFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (PrefabDefaultFile)serializer.Deserialize(stream);
                    if (parsed?.Defaults == null) return;
                    foreach (var d in parsed.Defaults)
                        if (d != null && !string.IsNullOrEmpty(d.Prefab) && !string.IsNullOrEmpty(d.ProfileName))
                            _prefabDefaults[d.Prefab] = d.ProfileName;
                }
                UIALog.Info($"Loaded {_prefabDefaults.Count} prefab default(s).");
            }
            catch (Exception e)
            {
                UIALog.Warn("Prefab-default load failed: " + e.Message);
            }
        }

        private static void SavePrefabDefaults()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var file = new PrefabDefaultFile();
                foreach (var kv in _prefabDefaults)
                    file.Defaults.Add(new PrefabDefault { Prefab = kv.Key, ProfileName = kv.Value });
                var serializer = new XmlSerializer(typeof(PrefabDefaultFile));
                using (var stream = File.Create(PrefabDefaultsPath))
                    serializer.Serialize(stream, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("Prefab-default save failed: " + e.Message);
            }
        }

        /// <summary>The user's chosen default profile name for a bag prefab, or null.
        /// Zero-allocation after first load (one dictionary hit) — router-safe. NOTE: a
        /// set default deliberately stands even when the named profile no longer exists
        /// (the router stage fail-softs to "no default"); it does NOT fall back to the
        /// shipped table — an explicit user choice always wins.</summary>
        public static string GetPrefabDefaultProfileName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            EnsurePrefabDefaultsLoaded();
            string name;
            return _prefabDefaults.TryGetValue(prefabName, out name) ? name : null;
        }

        public static void SetPrefabDefault(string prefabName, string profileName)
        {
            if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(profileName)) return;
            EnsurePrefabDefaultsLoaded();
            _prefabDefaults[prefabName] = profileName;
            SavePrefabDefaults();
        }

        public static void ClearPrefabDefault(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return;
            EnsurePrefabDefaultsLoaded();
            if (_prefabDefaults.Remove(prefabName)) SavePrefabDefaults();
        }

        /// <summary>Copy the prefab->profile default table into <paramref name="into"/>
        /// (cleared first) for UI listing. The internal dictionary is never handed out.</summary>
        public static void GetPrefabDefaults(IDictionary<string, string> into)
        {
            if (into == null) return;
            EnsurePrefabDefaultsLoaded();
            into.Clear();
            foreach (var kv in _prefabDefaults) into[kv.Key] = kv.Value;
        }

        // --- manila-tab badge tags (design Q5) ---

        /// <summary>The 2-4 char uppercase ASCII badge for a profile: its hand-set Badge
        /// override when valid, else the first word of its name (alnum-only, max 4 chars),
        /// deduped across loaded profiles by digit suffix in list order ("Tools"/"Tooling"
        /// -> TOOL/TOO2). Cached per name — per-frame calls for LOADED profiles are one
        /// dictionary hit, zero allocation; unknown names derive uncached (don't do that
        /// per frame). Never returns null; unknown/empty input yields "BAG".</summary>
        public static string ProfileTag(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return "BAG";
            if (_tagCache.Count == 0 && Profiles.Count > 0) RebuildTagCache();
            string tag;
            if (_tagCache.TryGetValue(profileName, out tag)) return tag;
            return DeriveTag(profileName);
        }

        private static void RebuildTagCache()
        {
            _tagCache.Clear();
            var used = new HashSet<string>();
            for (int i = 0; i < Profiles.Count; i++)
            {
                var p = Profiles[i];
                if (p == null || string.IsNullOrEmpty(p.Name) || _tagCache.ContainsKey(p.Name)) continue;
                string tag = SanitizeTag(p.Badge);
                if (tag.Length < 2) tag = DeriveTag(p.Name);
                if (!used.Add(tag))
                {
                    for (int n = 2; n < 100; n++)
                    {
                        string head = tag.Substring(0, Math.Min(tag.Length, n < 10 ? 3 : 2));
                        string candidate = head + n;
                        if (used.Add(candidate)) { tag = candidate; break; }
                    }
                }
                _tagCache[p.Name] = tag;
            }
        }

        /// <summary>Uppercase A-Z/0-9 only, max 4 chars; anything else (including the em-dash
        /// class of TMP tofu) is stripped. Empty/short results are the caller's cue to derive.</summary>
        private static string SanitizeTag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder(4);
            for (int i = 0; i < raw.Length && sb.Length < 4; i++)
            {
                char c = raw[i];
                if (c >= 'a' && c <= 'z') c = (char)(c - 32);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

        private static string DeriveTag(string name)
        {
            if (string.IsNullOrEmpty(name)) return "BAG";
            var sb = new System.Text.StringBuilder(4);
            bool started = false;
            for (int i = 0; i < name.Length && sb.Length < 4; i++)
            {
                char c = name[i];
                if (c == ' ' && started) break; // first word only
                if (c >= 'a' && c <= 'z') c = (char)(c - 32);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { sb.Append(c); started = true; }
            }
            if (sb.Length < 2)
            {
                // First word too thin ("A Bag") -> take alnum chars from the whole name.
                sb.Length = 0;
                for (int i = 0; i < name.Length && sb.Length < 4; i++)
                {
                    char c = name[i];
                    if (c >= 'a' && c <= 'z') c = (char)(c - 32);
                    if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
                }
            }
            return sb.Length >= 2 ? sb.ToString() : "BAG";
        }

        /// <summary>Hot-reload teardown (mod Shutdown): drop the new static caches so a
        /// double-F6 starts clean. Profiles/Assignments/Memory keep their existing
        /// self-heal (LoadProfiles at init + save-key check) — unchanged.</summary>
        public static void ResetRuntimeCaches()
        {
            _tagCache.Clear();
            _prefabDefaults.Clear();
            _prefabDefaultsLoaded = false;
        }

        private static void WriteStarterProfiles()
        {
            var starters = new BagProfileFile
            {
                Profiles = new List<BagProfile>
                {
                    new BagProfile
                    {
                        Name = "Construction",
                        Items = new List<ItemRule>
                        {
                            new ItemRule { Prefab = "ItemSteelSheets", Priority = 100 },
                            new ItemRule { Prefab = "ItemIronSheets", Priority = 95 },
                            new ItemRule { Prefab = "ItemIronFrames", Priority = 90 },
                            new ItemRule { Prefab = "ItemSteelFrames", Priority = 90 },
                            new ItemRule { Prefab = "ItemGlassSheets", Priority = 85 },
                            new ItemRule { Prefab = "ItemCableCoil", Priority = 80 },
                        },
                        Categories = new List<CategoryRule>
                        {
                            new CategoryRule { Name = "Kits", Priority = 60 },
                            new CategoryRule { Name = "Resources", Priority = 40 },
                        },
                    },
                    new BagProfile
                    {
                        Name = "Atmospherics",
                        SlotClasses = new List<SlotClassRule>
                        {
                            new SlotClassRule { Name = "GasFilter", Priority = 80 },
                            new SlotClassRule { Name = "GasCanister", Priority = 75 },
                            new SlotClassRule { Name = "LiquidCanister", Priority = 70 },
                        },
                        Categories = new List<CategoryRule>
                        {
                            new CategoryRule { Name = "Atmospherics", Priority = 60 },
                        },
                    },
                    new BagProfile
                    {
                        Name = "Electrical",
                        Items = new List<ItemRule>
                        {
                            new ItemRule { Prefab = "ItemCableCoil", Priority = 100 },
                            new ItemRule { Prefab = "ItemCableCoilHeavy", Priority = 95 },
                        },
                        SlotClasses = new List<SlotClassRule>
                        {
                            new SlotClassRule { Name = "Battery", Priority = 70 },
                            new SlotClassRule { Name = "Circuitboard", Priority = 60 },
                            new SlotClassRule { Name = "Motherboard", Priority = 60 },
                            new SlotClassRule { Name = "ProgrammableChip", Priority = 55 },
                        },
                    },
                    new BagProfile
                    {
                        Name = "Farming",
                        Items = new List<ItemRule>
                        {
                            new ItemRule { Prefab = "ItemFertilizer", Priority = 80 },
                        },
                        SlotClasses = new List<SlotClassRule>
                        {
                            new SlotClassRule { Name = "Plant", Priority = 75 },
                            new SlotClassRule { Name = "Egg", Priority = 60 },
                            new SlotClassRule { Name = "Bottle", Priority = 40 },
                        },
                        Categories = new List<CategoryRule>
                        {
                            new CategoryRule { Name = "Food", Priority = 50 },
                        },
                    },
                },
            };
            try
            {
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                using (var stream = File.Create(Path.Combine(ProfilesDir, "profiles.xml")))
                    serializer.Serialize(stream, starters);
                UIALog.Info("Wrote starter bag profiles (Construction/Atmospherics/Electrical/Farming).");
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not write starter profiles: " + e.Message);
            }
        }
    }
}

