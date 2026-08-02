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
        /// <summary>The 4th rule kind (SmartStow redesign plan §3.2, FlorpyDorp Q1): match on the
        /// MOD's own 22-class <see cref="Core.UIAClass"/> taxonomy instead of the game's coarse
        /// 11-value SortingClass. Purely additive on disk — XmlSerializer omits an empty list, so
        /// old profiles round-trip byte-identically and an OLD mod build re-saving profiles.xml
        /// merely drops the element (the same accepted risk documented for <see cref="Badge"/>).</summary>
        [XmlElement("Class")] public List<UIAClassRule> UIAClasses = new List<UIAClassRule>();

        /// <summary>Best-match priority for a thing, or null when nothing matches.
        /// Precedence: explicit item > UIA class > slot class > sorting category (ties by priority
        /// value). Runs at resolve frequency (router stages, the ~4 Hz ghost-hint dry-run) AND
        /// inside the profile-aware sort comparator, so the rule-name enum parses are CACHED per
        /// rule — on net48 Mono <c>Enum.TryParse</c> allocates (split scratch + boxing) on every
        /// call. <see cref="Core.UIASort.Classify"/> is resolved ONCE per Match (a PrefabHash
        /// dictionary hit plus a type-switch fallback), never once per rule, and only when the
        /// profile actually carries class rules.</summary>
        public int? Match(DynamicThing thing)
        {
            if (thing == null) return null;
            int? best = null;
            foreach (var rule in Items)
                if (!string.IsNullOrEmpty(rule.Prefab) && rule.Prefab == thing.PrefabName)
                    best = Max(best, rule.Priority + 20000);
            if (UIAClasses.Count > 0)
            {
                Core.UIAClass actual = Core.UIASort.Classify(thing);
                foreach (var rule in UIAClasses)
                    if (rule.TryGetUIAClass(out Core.UIAClass want) && actual == want)
                        best = Max(best, rule.Priority + 15000);
            }
            foreach (var rule in SlotClasses)
                if (rule.TryGetSlotClass(out Slot.Class cls) && thing.SlotType == cls)
                    best = Max(best, rule.Priority + 10000);
            foreach (var rule in Categories)
                if (rule.TryGetSortingClass(out SortingClass sc) && thing.SortingClass == sc)
                    best = Max(best, rule.Priority);
            return best;
        }

        /// <summary>Total rule count across all four kinds (UI listing / "N rules" labels).</summary>
        public int RuleCount
        {
            get { return Items.Count + Categories.Count + SlotClasses.Count + UIAClasses.Count; }
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

    /// <summary>Matches the mod's own <see cref="Core.UIAClass"/> taxonomy (22 classes, backed by
    /// the generated 785-prefab table in <c>Core/UIASortingData.g.cs</c> plus type fallbacks).
    /// The class is stored as its enum NAME, never its ordinal, so the taxonomy can be reordered
    /// or extended without silently re-pointing every player's rules; an unknown/renamed name
    /// fail-softs to "this rule never matches" instead of throwing or matching the wrong class.</summary>
    public class UIAClassRule
    {
        [XmlAttribute("name")] public string Name;   // UIAClass name
        /// <summary>Between CategoryRule (50) and SlotClassRule (60) in raw value; the +15000
        /// offset in <see cref="BagProfile.Match"/> is what actually orders the kinds.</summary>
        [XmlAttribute("priority")] public int Priority = 55;

        // Cached parse of Name — same rationale/semantics as CategoryRule's cache.
        private string _parsedFor;
        private bool _parsedOk;
        private Core.UIAClass _parsed;

        public bool TryGetUIAClass(out Core.UIAClass cls)
        {
            if (!ReferenceEquals(_parsedFor, Name))
            {
                _parsedOk = !string.IsNullOrEmpty(Name) && Enum.TryParse(Name, true, out _parsed);
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
        /// <summary>Per-container "never smart-stow into this" flags. Additive element: an old
        /// file simply has none, and an OLD mod build re-saving this file drops them (accepted,
        /// same posture as <see cref="BagProfile.Badge"/>). Keyed on ReferenceId like
        /// <see cref="Assignment"/>, so it is per SAVE by construction.</summary>
        [XmlElement("Exclude")] public List<ContainerExclude> Excludes = new List<ContainerExclude>();
    }

    /// <summary>"Smart Stow must never put anything in this container" (redesign plan Q5). Stored
    /// as its own element rather than a flag on <see cref="Assignment"/> because a container can
    /// be excluded WITHOUT carrying a profile (and keeping an empty assignment alive just to hold
    /// a bool would resurrect dangling profile names).</summary>
    public class ContainerExclude
    {
        [XmlAttribute("bagRef")] public long BagReferenceId;
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
        // Per-save "never smart-stow into this container" set (design Q5). Same lifetime and file
        // as Assignments; the router consults it at EVERY stage.
        private static readonly HashSet<long> Excludes = new HashSet<long>();

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

        /// <summary>
        /// Fill <see cref="Profiles"/> from the ACTIVE Stow Profile (SmartStow B2) — or, when that
        /// model is unavailable for any reason, from the legacy <c>Profiles/</c> folder exactly as
        /// pre-B2 builds did.
        ///
        /// <para>This is the ONE entry point for both launch and a Stow Profile switch: switching
        /// writes the marker and calls back in here, so there is a single load path to reason about.
        /// The legacy folder is never read once a Stow Profile exists — no double-loading.</para>
        /// </summary>
        public static void LoadProfiles()
        {
            Profiles.Clear();
            _tagCache.Clear();
            // The `stowprofiles` console diagnostic self-installs here because this is the one
            // init entry point this subsystem owns; see StowCommands for the wiring note.
            try { Core.StowCommands.Install(); } catch { }
            // Same reason: bind B3's rename-on-assign key here so it exists in the .cfg from launch
            // instead of appearing the first time F10 > Storage is opened. See StowRenameConfig for
            // why the key does not live in UIAConfig (yet).
            try { StowRenameConfig.EnsureBound(); } catch { }
            try
            {
                if (!StowProfileStore.LoadActiveInto(Profiles))
                {
                    bool readFailure;
                    LoadLegacyInto(Profiles, true, false, out readFailure);
                    UIALog.Info($"Loaded {Profiles.Count} bag profile(s) from the legacy Profiles folder.");
                }
            }
            catch (Exception e)
            {
                UIALog.Error("LoadProfiles failed: " + e);
            }
            LoadPrefabDefaults(); // clear + reload alongside profiles (hot-reload self-heal)
            // A different set of profile names is now live: let the router re-report any assignment
            // that names a profile this Stow Profile does not have.
            try { Core.StowRouter.ForgetAssignmentWarnings(); } catch { }
        }

        /// <summary>
        /// The pre-B2 loader, kept whole: merge every <c>Profiles/*.xml</c> into
        /// <paramref name="into"/>. Used for two things now — the fail-soft fallback when the Stow
        /// Profile model is unusable (<paramref name="seedStarters"/> true, matching old behaviour
        /// exactly) and the one-shot read that MIGRATION carries into the first Stow Profile
        /// (<paramref name="seedStarters"/> false so migration never writes a legacy file, and
        /// <paramref name="quarantine"/> true so a corrupt legacy file is preserved as
        /// <c>&lt;name&gt;.broken.xml</c> rather than silently vanishing from the new document).
        /// Returns how many profiles were read.
        ///
        /// <para><paramref name="readFailure"/> reports that a file could not be OPENED (an IO or
        /// permission problem) as opposed to being malformed. Migration is a one-shot, stamped
        /// operation, so it must not carry a partial set forward and then declare itself done: a
        /// transient lock on one legacy file would silently drop those profiles from the new
        /// document forever. The caller aborts and retries next launch instead.</para>
        /// </summary>
        internal static int LoadLegacyInto(List<BagProfile> into, bool seedStarters, bool quarantine,
            out bool readFailure)
        {
            readFailure = false;
            if (into == null) return 0;
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                var files = Directory.GetFiles(ProfilesDir, "*.xml");
                if (files.Length == 0)
                {
                    if (!seedStarters) return 0;
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
                    // A quarantined copy is not a profile file (it is the unreadable original,
                    // kept for the player) — never try to parse one back in.
                    if (StowProfileStore.IsBrokenBackupName(Path.GetFileNameWithoutExtension(file))) continue;
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
                                    AddOrReplaceProfile(into, p, canonical, mtime, sourceTimes);
                        }
                    }
                    catch (Exception e)
                    {
                        // Same discipline as the Stow Profile loader: a MALFORMED file is preserved
                        // as a .broken.xml copy and skipped; a file that would not OPEN is reported
                        // to the caller instead, because acting on it (quarantining it, or carrying
                        // a set that is missing it into a stamped migration) turns a two-second lock
                        // into permanent divergence.
                        bool broken = SaveScopedXmlStore.IsParseFailure(e);
                        UIALog.Warn($"Bag profile file '{Path.GetFileName(file)}' could not be read"
                            + (broken ? " (bad XML): " : " (skipped): ") + e.Message);
                        if (!broken) readFailure = true;
                        else if (quarantine) StowProfileStore.QuarantineFile(file, false);
                    }
                }
            }
            catch (Exception e)
            {
                readFailure = true;
                UIALog.Error("Legacy bag profile read failed: " + e);
            }
            return into.Count;
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
        private static void AddOrReplaceProfile(List<BagProfile> into, BagProfile profile, bool canonical,
            DateTime mtime, Dictionary<string, DateTime> sourceTimes)
        {
            for (int i = 0; i < into.Count; i++)
            {
                var existing = into[i];
                if (existing != null && existing.Name == profile.Name)
                {
                    DateTime prev;
                    if (canonical && sourceTimes.TryGetValue(profile.Name, out prev) && prev > mtime)
                    {
                        // The drop-in copy is newer than profiles.xml: keep the import.
                        UIALog.Info($"Profile '{profile.Name}': drop-in copy is newer than profiles.xml - using the import (the next save makes it canonical).");
                        return;
                    }
                    into[i] = profile; // later file wins; list order stays stable
                    sourceTimes[profile.Name] = mtime;
                    return;
                }
            }
            into.Add(profile);
            sourceTimes[profile.Name] = mtime;
        }

        /// <summary>Persist every loaded Bag Profile. Post-B2 this writes the ACTIVE Stow Profile
        /// document; the legacy <c>profiles.xml</c> write survives only for the degraded path (no
        /// usable <c>StowProfiles/</c> folder), so a player who somehow loses that folder still gets
        /// their edits saved somewhere they will be read back from.
        ///
        /// <para>Returns FALSE when the write did not happen. Callers that show the player a result
        /// must say so rather than refreshing as if it had — a read-only config folder used to look
        /// exactly like a successful save until the next launch threw the edit away.</para></summary>
        public static bool SaveProfiles()
        {
            _tagCache.Clear(); // names/badges may have changed; tags re-derive lazily
            try
            {
                if (StowProfileStore.Available)
                {
                    if (!StowProfileStore.SaveActive(Profiles)) return false;
                    UIALog.Info("Saved " + Profiles.Count + " bag profile(s) to Stow Profile '"
                        + StowProfileStore.ActiveName + "'.");
                    return true;
                }
                Directory.CreateDirectory(ProfilesDir);
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                var path = Path.Combine(ProfilesDir, "profiles.xml");
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, new BagProfileFile { Profiles = Profiles.ToList() });
                UIALog.Info("Saved bag profiles to " + path);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Error("SaveProfiles failed: " + e);
                return false;
            }
        }

        /// <summary>
        /// The SHARING import path, post-B2. Exporting a Bag Profile still writes a stand-alone
        /// <c>Profiles/&lt;name&gt;.xml</c> (see <see cref="ExportProfile"/>) and receiving one is
        /// still "drop the file in that folder" — but that folder is no longer LOADED, so the import
        /// is now an explicit gesture instead of a side effect of every launch. Each
        /// <c>&lt;BagProfile&gt;</c> found is merged into the ACTIVE Stow Profile: a new name is
        /// added, an existing name is REPLACED (the same "the drop-in you just received wins"
        /// semantics the old loader had).
        ///
        /// <para>The canonical legacy <c>profiles.xml</c> is deliberately skipped — it is the
        /// dormant old store, not a shared file, and pulling it in would flood a curated Stow
        /// Profile with a copy of everything the player ever had.</para>
        ///
        /// <para><b>Newer-wins on collision</b>, the pre-B2 <c>AddOrReplaceProfile</c> rule, kept
        /// verbatim because dropping it is a silent data-loss bug: <see cref="ExportProfile"/>
        /// leaves its file in that folder forever, so a self-export from last month would otherwise
        /// REVERT every edit made since, on a button press that reports it as an "update". A profile
        /// whose name is NOT in the active set is still added regardless of age (an old shared file
        /// is still a new profile to you); a name that IS present is replaced only by a file written
        /// after the active document.</para>
        /// </summary>
        public static int ImportSharedProfiles(List<string> addedNames, List<string> replacedNames,
            out int skippedStale)
        {
            skippedStale = 0;
            if (addedNames != null) addedNames.Clear();
            if (replacedNames != null) replacedNames.Clear();
            int filesRead = 0;
            try
            {
                if (!Directory.Exists(ProfilesDir)) return 0;

                // The active document's write time is the "how current is what I already have"
                // reference the old loader took from profiles.xml.
                DateTime activeStamp = DateTime.MinValue;
                try
                {
                    string activeFile = StowProfileStore.ActiveFile;
                    if (!string.IsNullOrEmpty(activeFile) && File.Exists(activeFile))
                        activeStamp = File.GetLastWriteTimeUtc(activeFile);
                }
                catch { }

                var serializer = new XmlSerializer(typeof(BagProfileFile));
                foreach (string file in Directory.GetFiles(ProfilesDir, "*.xml"))
                {
                    if (string.Equals(Path.GetFileName(file), "profiles.xml", StringComparison.OrdinalIgnoreCase)) continue;
                    if (StowProfileStore.IsBrokenBackupName(Path.GetFileNameWithoutExtension(file))) continue;
                    try
                    {
                        DateTime mtime;
                        try { mtime = File.GetLastWriteTimeUtc(file); }
                        catch { mtime = DateTime.MinValue; }

                        BagProfileFile parsed;
                        using (var stream = File.OpenRead(file))
                            parsed = (BagProfileFile)serializer.Deserialize(stream);
                        if (parsed?.Profiles == null) continue;
                        filesRead++;
                        foreach (var p in parsed.Profiles)
                        {
                            if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                            int at = -1;
                            for (int i = 0; i < Profiles.Count; i++)
                                if (Profiles[i] != null && Profiles[i].Name == p.Name) { at = i; break; }
                            if (at < 0)
                            {
                                Profiles.Add(p);
                                if (addedNames != null) addedNames.Add(p.Name);
                                continue;
                            }
                            if (mtime <= activeStamp) { skippedStale++; continue; }   // yours is newer: keep it
                            Profiles[at] = p;
                            if (replacedNames != null) replacedNames.Add(p.Name);
                        }
                    }
                    catch (Exception e)
                    {
                        UIALog.Warn("Shared profile file '" + Path.GetFileName(file) + "' failed to parse: " + e.Message);
                    }
                }
                int added = addedNames != null ? addedNames.Count : 0;
                int replaced = replacedNames != null ? replacedNames.Count : 0;
                if (added > 0 || replaced > 0)
                {
                    SaveProfiles();
                    UIALog.Info("Imported " + added + " new and " + replaced + " updated bag profile(s) into Stow Profile '"
                        + (StowProfileStore.ActiveName ?? "?") + "'"
                        + (skippedStale > 0 ? " (" + skippedStale + " older copy/copies skipped)." : "."));
                }
                else if (skippedStale > 0)
                {
                    UIALog.Info("Nothing imported: " + skippedStale
                        + " shared file(s) are older than what you already have.");
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Shared profile import failed: " + e.Message);
            }
            return filesRead;
        }

        /// <summary>O5c share: write ONE profile as its own drop-in file
        /// <c>Profiles/&lt;safe-name&gt;.xml</c>. Post-B2 that folder is no longer LOADED (the
        /// active Stow Profile is), so the round trip is export -> send the file -> the recipient
        /// drops it in the same folder and presses <b>Import shared profiles</b>
        /// (<see cref="ImportSharedProfiles"/>), which merges it into their active Stow Profile with
        /// the same replace-by-name semantics the old loader had. Guard: an
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

        /// <summary>Rename a profile AND every pointer to it, everywhere this machine stores one:
        /// the CURRENT save's assignments (in memory), EVERY OTHER save's assignment file on disk,
        /// the global prefab defaults, every Loadout entry, and any drop-in <c>Profiles/*.xml</c>
        /// copy (whose stale old-named profile would otherwise be re-loaded as a SECOND profile on
        /// the next launch — the reason this method's original, never-shipped implementation was
        /// not safe to wire up as-is). False on missing source name, empty/whitespace target, or a
        /// name collision (caller should pick another, e.g. via <see cref="UniqueProfileName"/>).
        /// Fail-soft per file: a broken assignment/loadout file is skipped with a warning, never
        /// deleted, and never aborts the rest of the cascade.</summary>
        public static bool RenameProfile(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return false;
            var profile = FindProfile(oldName);
            if (profile == null || FindProfile(newName) != null) return false;
            profile.Name = newName;
            CascadeProfileName(oldName, newName);
            SaveProfiles();
            UIALog.Info("Renamed bag profile '" + oldName + "' -> '" + newName + "'.");
            return true;
        }

        /// <summary>Delete a profile and clear every reference to it, so nothing is left pointing
        /// at a name that no longer exists: assignments (current save AND every other save's file)
        /// fall back to UNASSIGNED, prefab defaults naming it are removed, and loadout entries
        /// naming it are dropped.
        ///
        /// <para>Any exported copy in <c>Profiles/</c> is deliberately LEFT ALONE (it is the
        /// player's own backup and the share drop-box, not our bookkeeping). It cannot resurrect the
        /// profile: that folder is not loaded at launch, and an explicit import only replaces a name
        /// you still have from a file newer than your active document.</para>
        /// False when the name is not loaded.</summary>
        public static bool DeleteProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int removed = 0;
            for (int i = Profiles.Count - 1; i >= 0; i--)
                if (Profiles[i] != null && Profiles[i].Name == name) { Profiles.RemoveAt(i); removed++; }
            if (removed == 0) return false;
            CascadeProfileName(name, null);   // null target = drop the reference entirely
            SaveProfiles();
            UIALog.Info("Deleted bag profile '" + name + "'.");
            return true;
        }

        /// <summary>Point every stored reference to <paramref name="oldName"/> at
        /// <paramref name="newName"/>, or DROP it when that is null. Shared by rename and delete
        /// so the two can never cover different sets of stores.</summary>
        private static void CascadeProfileName(string oldName, string newName)
        {
            // 1) current save's assignments (authoritative in memory; the file is rewritten below)
            EnsureSaveLoaded();
            List<long> assignKeys = null;
            foreach (var kv in Assignments)
                if (kv.Value == oldName)
                    (assignKeys ?? (assignKeys = new List<long>())).Add(kv.Key);
            if (assignKeys != null)
            {
                foreach (long key in assignKeys)
                {
                    if (newName == null) Assignments.Remove(key);
                    else Assignments[key] = newName;
                }
                SaveAssignments();
            }

            // 2) every OTHER save's assignment file (skip the loaded one — step 1 owns it)
            CascadeOtherSaveAssignments(oldName, newName);

            // 3) global prefab defaults
            EnsurePrefabDefaultsLoaded();
            List<string> defaultKeys = null;
            foreach (var kv in _prefabDefaults)
                if (kv.Value == oldName)
                    (defaultKeys ?? (defaultKeys = new List<string>())).Add(kv.Key);
            if (defaultKeys != null)
            {
                foreach (string key in defaultKeys)
                {
                    if (newName == null) _prefabDefaults.Remove(key);
                    else _prefabDefaults[key] = newName;
                }
                SavePrefabDefaults();
            }

            // 4) loadouts (their own files, own root)
            try { LoadoutStore.CascadeProfileName(oldName, newName); }
            catch (Exception e) { UIALog.Warn("Loadout profile-name cascade failed: " + e.Message); }

            // 5) drop-in Profiles/*.xml copies — RENAME ONLY.
            //
            // A delete deliberately does NOT touch that folder any more. Post-B2 it is the share
            // drop-box AND the only backup a player has ("Export it first, then delete it" is the
            // obvious way to try a change) — and the old cascade deleted the exported file along
            // with the profile, so both copies vanished on one click. It was there to stop a stale
            // copy resurrecting the profile at the next launch; that folder is no longer loaded at
            // launch at all, and ImportSharedProfiles only replaces an existing name from a file
            // NEWER than the active document, so the resurrection path it guarded is closed.
            if (newName != null) CascadeDropInFiles(oldName, newName);
        }

        /// <summary>Rewrite the profile name inside every assignment file that is NOT the currently
        /// loaded save's. Fail-soft per file; untouched files are not rewritten at all.</summary>
        private static void CascadeOtherSaveAssignments(string oldName, string newName)
        {
            try
            {
                if (!Directory.Exists(AssignmentsDir)) return;
                string current = (_loadedSaveKey ?? string.Empty) + ".xml";
                var serializer = new XmlSerializer(typeof(AssignmentFile));
                foreach (string file in Directory.GetFiles(AssignmentsDir, "*.xml"))
                {
                    if (string.Equals(Path.GetFileName(file), current, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        AssignmentFile parsed;
                        using (var stream = File.OpenRead(file))
                            parsed = (AssignmentFile)serializer.Deserialize(stream);
                        if (parsed?.Assignments == null) continue;
                        bool dirty = false;
                        for (int i = parsed.Assignments.Count - 1; i >= 0; i--)
                        {
                            var a = parsed.Assignments[i];
                            if (a == null || a.ProfileName != oldName) continue;
                            if (newName == null) parsed.Assignments.RemoveAt(i);
                            else a.ProfileName = newName;
                            dirty = true;
                        }
                        if (!dirty) continue;
                        using (var stream = File.Create(file))
                            serializer.Serialize(stream, parsed);
                        UIALog.Info("Updated bag profile reference in " + Path.GetFileName(file) + ".");
                    }
                    catch (Exception e)
                    {
                        UIALog.Warn("Assignment file '" + Path.GetFileName(file) + "' not updated: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Assignment cascade failed: " + e.Message);
            }
        }

        /// <summary>RENAME the profile inside every drop-in <c>Profiles/*.xml</c> (never
        /// profiles.xml, which the legacy <see cref="SaveProfiles"/> path rewrites wholesale), so a
        /// later import of your own export does not re-introduce the OLD name as a second profile.
        /// Only ever called with a non-null target — a delete leaves that folder untouched (see
        /// <see cref="DeleteProfile"/>). The empty-file branch below therefore no longer fires in
        /// practice and is kept only as a guard. Fail-soft per file.</summary>
        private static void CascadeDropInFiles(string oldName, string newName)
        {
            try
            {
                if (!Directory.Exists(ProfilesDir)) return;
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                foreach (string file in Directory.GetFiles(ProfilesDir, "*.xml"))
                {
                    if (string.Equals(Path.GetFileName(file), "profiles.xml", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        BagProfileFile parsed;
                        using (var stream = File.OpenRead(file))
                            parsed = (BagProfileFile)serializer.Deserialize(stream);
                        if (parsed?.Profiles == null) continue;
                        bool dirty = false;
                        for (int i = parsed.Profiles.Count - 1; i >= 0; i--)
                        {
                            var p = parsed.Profiles[i];
                            if (p == null || p.Name != oldName) continue;
                            if (newName == null) parsed.Profiles.RemoveAt(i);
                            else p.Name = newName;
                            dirty = true;
                        }
                        if (!dirty) continue;
                        if (parsed.Profiles.Count == 0)
                        {
                            File.Delete(file);
                            UIALog.Info("Removed now-empty profile file Profiles/" + Path.GetFileName(file) + ".");
                            continue;
                        }
                        using (var stream = File.Create(file))
                            serializer.Serialize(stream, parsed);
                        UIALog.Info("Updated Profiles/" + Path.GetFileName(file) + " for the profile change.");
                    }
                    catch (Exception e)
                    {
                        UIALog.Warn("Profile file '" + Path.GetFileName(file) + "' not updated: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Drop-in profile cascade failed: " + e.Message);
            }
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
            Excludes.Clear();
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
                    if (parsed.Excludes != null)
                        foreach (var x in parsed.Excludes)
                            if (x != null) Excludes.Add(x.BagReferenceId);
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
                    Excludes = Excludes.Select(id => new ContainerExclude { BagReferenceId = id }).ToList(),
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

        /// <summary>Copy this save's <c>bagRef -&gt; profileName</c> table into
        /// <paramref name="into"/> (cleared first) for read-only diagnostics — the
        /// <c>stowprofiles</c> console dump uses it to count assignments that name a profile the
        /// ACTIVE Stow Profile does not contain, including ones on containers that are not reachable
        /// right now. The internal dictionary is never handed out.</summary>
        public static void GetAssignments(IDictionary<long, string> into)
        {
            if (into == null) return;
            EnsureSaveLoaded();
            into.Clear();
            foreach (var kv in Assignments) into[kv.Key] = kv.Value;
        }

        // --- per-container stow exclusion (design Q5: "never smart-stow into this") ---

        /// <summary>True when the player has told Smart Stow to never route anything into this
        /// container. Consulted by EVERY <c>StowRouter</c> stage, so it must stay cheap: one
        /// save-key check plus (only when the set is non-empty) one hash lookup. The flag is
        /// INDEPENDENT of any profile assignment — an excluded bag may still carry a profile,
        /// which simply stops being routed to until the exclusion is lifted.</summary>
        public static bool IsStowExcluded(Thing bag)
        {
            if (bag == null) return false;
            EnsureSaveLoaded();
            if (Excludes.Count == 0) return false;   // fast path: nothing excluded this save
            return Excludes.Contains(bag.ReferenceId);
        }

        /// <summary>Set/clear the exclusion for one container (save-through, idempotent).</summary>
        public static void SetStowExcluded(Thing bag, bool excluded)
        {
            if (bag == null) return;
            EnsureSaveLoaded();
            bool changed = excluded ? Excludes.Add(bag.ReferenceId) : Excludes.Remove(bag.ReferenceId);
            if (changed) SaveAssignments();
        }

        /// <summary>How many containers are excluded in the current save (UI status lines).</summary>
        public static int ExcludedCount
        {
            get { EnsureSaveLoaded(); return Excludes.Count; }
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
            StowProfileStore.Reset();          // drop the loaded Stow Profile document
            try { Core.StowCommands.Uninstall(); } catch { }  // unpatch our own console hook
        }

        /// <summary>The four legacy starter Bag Profiles, as data. Split out from
        /// <see cref="WriteStarterProfiles"/> so a FRESH install can seed the first Stow Profile
        /// with them directly, without first writing a legacy <c>profiles.xml</c> the new model
        /// would never read.</summary>
        internal static List<BagProfile> BuildStarterProfiles()
        {
            return new List<BagProfile>
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
                };
        }

        /// <summary>Legacy-path seed only (an empty <c>Profiles/</c> folder on the degraded loader).
        /// The Stow Profile model seeds a fresh install from <see cref="BuildStarterProfiles"/>
        /// directly and never writes this file.</summary>
        private static void WriteStarterProfiles()
        {
            var starters = new BagProfileFile { Profiles = BuildStarterProfiles() };
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

