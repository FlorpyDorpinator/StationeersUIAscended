using System.Collections.Generic;
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
    /// <c>BepInEx/config/StationeersUIMod/HintUsage/&lt;worldKey&gt;.xml</c>, keyed via
    /// <see cref="SaveScopedXmlStore.ResolveSaveKey"/> (the per-world <c>World.CurrentId</c>, not the
    /// station name - SmartStow-Simple-Refactor-Plan section 7.7). No key yet (main menu, or a
    /// legacy SP world before its first save) means memory-only - see <see cref="EnsureSaveLoaded"/>.
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

        // Disk plumbing lives in SaveScopedXmlStore (same folder/filename/serializer as before).
        private const string StoreFolder = "HintUsage";
        private const string StoreLabel = "Hint usage";

        // --- per-save load/save (keyed off the per-world identity, plan §7.1/§7.7) ---

        /// <summary>(Re)load for the CURRENT world key. No key yet means: hold the in-memory counts
        /// as-is, touch nothing on disk - never "unsaved.xml". A memory-only table that GAINS a key
        /// mid-session (this session's own counts, made before any key existed) is flushed into the
        /// newly-keyed file immediately, mirroring <c>StowHomeStore.EnsureLoaded</c> - rather than
        /// waiting on the batched <see cref="Flush"/> path, which might not fire before a crash/quit.</summary>
        public static void EnsureSaveLoaded()
        {
            string key;
            if (!SaveScopedXmlStore.ResolveSaveKey(StoreFolder, _loadedSaveKey, out key)) { StandDown(); return; }
            if (key == _loadedSaveKey) return;

            // Fix-wave finding 7: flush the OUTGOING key's pending counters before switching away —
            // Save() still reads the OLD _loadedSaveKey here (not yet overwritten below), and itself
            // no-ops when that key was already null (memory-only), so this is safe unconditionally.
            if (_dirty) Save();

            Dictionary<string, int> carried = null;
            if (_loadedSaveKey == null && _counts.Count > 0)
                carried = new Dictionary<string, int>(_counts);

            _loadedSaveKey = key;
            _counts.Clear();
            _dirty = false;
            LoadInto(key);

            // Fix-wave finding 5: an empty result under a brand-new key may mean adoption ran too
            // early (before the station name was set) and missed a legacy file that exists NOW.
            if (_counts.Count == 0 && SaveScopedXmlStore.RetryAdoptionIfEmpty(StoreFolder, key))
                LoadInto(key);

            if (carried != null)
            {
                foreach (var kv in carried) _counts[kv.Key] = kv.Value;   // this session's memory wins over disk
                Save();
            }
        }

        /// <summary>The actual per-save deserialize-and-populate step, split out so
        /// <see cref="EnsureSaveLoaded"/> can retry it once after finding 5's adoption retry.</summary>
        private static void LoadInto(string key)
        {
            var parsed = SaveScopedXmlStore.LoadPerSave<HintUsageFile>(StoreFolder, key, StoreLabel);
            if (parsed?.Hints == null) return;
            foreach (var h in parsed.Hints)
                if (h != null && !string.IsNullOrEmpty(h.Kind) && h.Count > 0) _counts[h.Kind] = h.Count;
        }

        /// <summary>Fix-wave finding 1: drop a dead world's counters+key the moment no key resolves
        /// any more (world left, or between-worlds transition) — flushing any pending counters to
        /// the OUTGOING key's file first — so a stray mutation can never keep writing into the
        /// PREVIOUS world's file under a stale <see cref="_loadedSaveKey"/>. No-op once already
        /// standing down (cheap to call on every failed resolve).</summary>
        private static void StandDown()
        {
            if (_loadedSaveKey == null) return;
            if (_dirty) Save();
            _counts.Clear();
            _loadedSaveKey = null;
            _dirty = false;
        }

        /// <summary>Write the counters now. Memory-only (no world key resolved) NEVER writes - see
        /// <see cref="EnsureSaveLoaded"/>.</summary>
        private static void Save()
        {
            if (_loadedSaveKey == null) return;
            var file = new HintUsageFile
            {
                Hints = _counts.Select(kv => new HintUsageEntry { Kind = kv.Key, Count = kv.Value }).ToList(),
            };
            // Only a write that actually landed clears the dirty flag — a failed save keeps the
            // pending increments so the next Flush retries them.
            if (SaveScopedXmlStore.SavePerSave(StoreFolder, _loadedSaveKey, file, StoreLabel)) _dirty = false;
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

        /// <summary>Hot-reload / mod-off (also called from RadialHintBar shutdown). Fix-wave finding
        /// 7: this used to drop any pending increments silently — now it flushes them first (Save()
        /// still has the live <see cref="_loadedSaveKey"/> at this point), then forgets the in-memory
        /// state (disk file otherwise untouched).</summary>
        public static void Reset()
        {
            if (_dirty) Save();
            _counts.Clear();
            _loadedSaveKey = null;
            _dirty = false;
        }
    }
}
