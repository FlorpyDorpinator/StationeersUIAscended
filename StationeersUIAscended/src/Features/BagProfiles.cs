using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Assets.Scripts.Objects;
using Assets.Scripts.Serialization;
using BepInEx;
using StationeersUIAscended.Core;

namespace StationeersUIAscended.Features
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
        [XmlElement("Item")] public List<ItemRule> Items = new List<ItemRule>();
        [XmlElement("Category")] public List<CategoryRule> Categories = new List<CategoryRule>();
        [XmlElement("SlotClass")] public List<SlotClassRule> SlotClasses = new List<SlotClassRule>();

        /// <summary>Best-match priority for a thing, or null when nothing matches.
        /// Precedence: explicit item > slot class > sorting category (ties by priority value).</summary>
        public int? Match(DynamicThing thing)
        {
            if (thing == null) return null;
            int? best = null;
            foreach (var rule in Items)
                if (!string.IsNullOrEmpty(rule.Prefab) && rule.Prefab == thing.PrefabName)
                    best = Max(best, rule.Priority + 20000);
            foreach (var rule in SlotClasses)
                if (Enum.TryParse(rule.Name, out Slot.Class cls) && thing.SlotType == cls)
                    best = Max(best, rule.Priority + 10000);
            foreach (var rule in Categories)
                if (Enum.TryParse(rule.Name, out SortingClass sc) && thing.SortingClass == sc)
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
    }

    public class SlotClassRule
    {
        [XmlAttribute("name")] public string Name; // Slot.Class name
        [XmlAttribute("priority")] public int Priority = 60;
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

        private static string _loadedSaveKey;

        public static string ConfigDir => Path.Combine(Paths.ConfigPath, "StationeersUIAscended");
        public static string ProfilesDir => Path.Combine(ConfigDir, "Profiles");
        public static string AssignmentsDir => Path.Combine(ConfigDir, "Assignments");

        public static void LoadProfiles()
        {
            Profiles.Clear();
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                var files = Directory.GetFiles(ProfilesDir, "*.xml");
                if (files.Length == 0)
                {
                    WriteStarterProfiles();
                    files = Directory.GetFiles(ProfilesDir, "*.xml");
                }
                var serializer = new XmlSerializer(typeof(BagProfileFile));
                foreach (var file in files)
                {
                    try
                    {
                        using (var stream = File.OpenRead(file))
                        {
                            var parsed = (BagProfileFile)serializer.Deserialize(stream);
                            if (parsed?.Profiles != null)
                                Profiles.AddRange(parsed.Profiles.Where(p => !string.IsNullOrEmpty(p.Name)));
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
        }

        public static void SaveProfiles()
        {
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

        // --- per-save state ---

        public static string CurrentSaveKey()
        {
            try
            {
                var save = XmlSaveLoad.Instance;
                string name = save?.CurrentStationName;
                if (string.IsNullOrEmpty(name)) name = save?.CurrentWorldSave?.Name;
                if (string.IsNullOrEmpty(name)) name = "unsaved";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
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
            return Profiles.FirstOrDefault(p => p.Name == name);
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
