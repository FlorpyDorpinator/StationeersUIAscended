using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model ----------

    [XmlRoot("HintUsage")]
    public class HintUsageFile
    {
        [XmlElement("Hint")] public List<HintUsageEntry> Hints = new List<HintUsageEntry>();
    }

    public class HintUsageEntry
    {
        [XmlAttribute("kind")] public string Kind;
        [XmlAttribute("count")] public int Count;
    }

    /// <summary>
    /// Progressive-fade counters for the contextual key-hint bar (feature R10). Each hint "kind"
    /// (select / back / reach / swap-hand / page / world-grab / world-place) carries a small
    /// exposure count; once a kind passes the fade threshold the bar stops drawing that token, so
    /// the hints teach then get out of the way. Counters are PER SAVE — mirroring
    /// <see cref="BagHotkeyStore"/>/<see cref="BagProfileStore"/> — because a fresh save should
    /// start teaching again. Persisted to
    /// <c>BepInEx/config/StationeersUIMod/HintUsage/&lt;saveKey&gt;.xml</c>.
    ///
    /// This store only records exposures the bar itself observes (no game-state mutation, MP-safe
    /// by construction). Writes are batched: <see cref="Add"/> increments in memory and marks the
    /// table dirty; <see cref="Flush"/> persists once at the end of a radial session.
    /// </summary>
    public static class HintUsageStore
    {
        private static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        private static string _loadedSaveKey;
        private static bool _dirty;

        private static string HintUsageDir => Path.Combine(BagProfileStore.ConfigDir, "HintUsage");

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _counts.Clear();
            _dirty = false;
            try
            {
                var path = Path.Combine(HintUsageDir, key + ".xml");
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(HintUsageFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (HintUsageFile)serializer.Deserialize(stream);
                    if (parsed?.Hints != null)
                        foreach (var h in parsed.Hints)
                            if (!string.IsNullOrEmpty(h.Kind) && h.Count > 0) _counts[h.Kind] = h.Count;
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Hint usage load failed: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(HintUsageDir);
                var path = Path.Combine(HintUsageDir, (_loadedSaveKey ?? BagProfileStore.CurrentSaveKey()) + ".xml");
                var file = new HintUsageFile
                {
                    Hints = _counts.Select(kv => new HintUsageEntry { Kind = kv.Key, Count = kv.Value }).ToList(),
                };
                var serializer = new XmlSerializer(typeof(HintUsageFile));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, file);
                _dirty = false;
            }
            catch (Exception e)
            {
                UIALog.Warn("Hint usage save failed: " + e.Message);
            }
        }

        // --- public API ---

        /// <summary>Successful-exposure count for a hint kind on the current save (0 if unseen).</summary>
        public static int Get(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return 0;
            EnsureSaveLoaded();
            return _counts.TryGetValue(kind, out var c) ? c : 0;
        }

        /// <summary>Record one exposure of a hint kind. In-memory only — call <see cref="Flush"/>
        /// once to persist a batch (avoids a disk write per token per session).</summary>
        public static void Add(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return;
            EnsureSaveLoaded();
            _counts[kind] = (_counts.TryGetValue(kind, out var c) ? c : 0) + 1;
            _dirty = true;
        }

        /// <summary>Persist pending increments if any were recorded since the last save.</summary>
        public static void Flush()
        {
            if (_dirty) Save();
        }

        /// <summary>User-initiated wipe (the F10 "reset hint counters" button): zero every counter
        /// on the current save so the hints start teaching again, and persist immediately.</summary>
        public static void ResetCounters()
        {
            EnsureSaveLoaded();
            _counts.Clear();
            Save();
        }

        /// <summary>Hot-reload / mod-off: forget the in-memory counters (disk file is untouched).</summary>
        public static void Reset()
        {
            _counts.Clear();
            _loadedSaveKey = null;
            _dirty = false;
        }
    }
}
