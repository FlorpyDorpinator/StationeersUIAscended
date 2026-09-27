using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model ----------

    [XmlRoot("GridState")]
    public class GridStateFile
    {
        /// <summary>Containers the player has EXPANDED in The Grid (keyed by Thing.ReferenceId).
        /// Absence == collapsed (the default) — see <see cref="GridCollapseStore"/>. Pre-inversion
        /// saves stored a &lt;Collapsed&gt; element instead; that element no longer maps to a member,
        /// so XmlSerializer silently ignores it and such a file loads as an empty expanded set (i.e.
        /// everything collapsed) — the graceful legacy migration.</summary>
        [XmlElement("Expanded")] public List<long> Expanded = new List<long>();

        // Stage 2: pinned-window positions/state persist here too (add a <Pin> element list).
    }

    /// <summary>
    /// Per-save collapse state for "The Grid" (#5). A nested container section is COLLAPSED by
    /// default; once the player expands it we remember that by the container's persistent
    /// <see cref="Assets.Scripts.Objects.Thing.ReferenceId"/> (a stable save-scoped long). State is
    /// PER SAVE — ReferenceIds are save-scoped, so a new save starts fully collapsed (new/unseen bags
    /// are always closed) — mirroring the per-save storage in <see cref="BagHotkeyStore"/> /
    /// <see cref="BagProfileStore"/>. The whole-inventory root is ALWAYS expanded and never keyed
    /// here (its callers guard on <c>_isRoot</c>). Persisted to
    /// <c>BepInEx/config/StationeersUIMod/Grid/&lt;worldKey&gt;.xml</c>, keyed via
    /// <see cref="SaveScopedXmlStore.ResolveSaveKey"/> (the per-world <c>World.CurrentId</c>, not the
    /// station name — SmartStow-Simple-Refactor-Plan §7.7). No key yet (main menu, or a legacy SP
    /// world before its first save) means memory-only — see <see cref="EnsureSaveLoaded"/>.
    ///
    /// NOTE: the public method names are still phrased around "collapsed" (they answer "does this
    /// section render collapsed?"), so no caller changed when the DEFAULT and on-disk set were
    /// inverted here; only the sentinel flipped (absence now means collapsed, and the persisted set
    /// is the EXPANDED one).
    ///
    /// Stage-2 pin state (pinned windows + positions) will live in the same file/store; the XML
    /// model already leaves a seam for it.
    /// </summary>
    public static class GridCollapseStore
    {
        // Only EXPANDED containers are stored; absence == collapsed (the default).
        private static readonly HashSet<long> _expanded = new HashSet<long>();
        private static string _loadedSaveKey;

        // Disk plumbing lives in SaveScopedXmlStore (same folder/filename/serializer as before).
        private const string StoreFolder = "Grid";
        private const string StoreLabel = "Grid collapse";

        // --- per-save load/save (keyed off the per-world identity, plan §7.1/§7.7) ---

        /// <summary>(Re)load for the CURRENT world key. No key yet means: hold the in-memory set
        /// as-is, touch nothing on disk — never "unsaved.xml". A memory-only set that GAINS a key
        /// mid-session (this session's own expand-state, made before any key existed) is carried
        /// into the newly-keyed file, mirroring <c>StowHomeStore.EnsureLoaded</c>.</summary>
        public static void EnsureSaveLoaded()
        {
            string key;
            if (!SaveScopedXmlStore.ResolveSaveKey(StoreFolder, _loadedSaveKey, out key)) { StandDown(); return; }
            if (key == _loadedSaveKey) return;

            HashSet<long> carried = null;
            if (_loadedSaveKey == null && _expanded.Count > 0)
                carried = new HashSet<long>(_expanded);

            _loadedSaveKey = key;
            _expanded.Clear();
            LoadInto(key);

            // Fix-wave finding 5: an empty result under a brand-new key may mean adoption ran too
            // early (before the station name was set) and missed a legacy file that exists NOW.
            if (_expanded.Count == 0 && SaveScopedXmlStore.RetryAdoptionIfEmpty(StoreFolder, key))
                LoadInto(key);

            if (carried != null)
            {
                foreach (var id in carried) _expanded.Add(id);   // this session's memory wins over disk
                Save();
            }
        }

        /// <summary>The actual per-save deserialize-and-populate step, split out so
        /// <see cref="EnsureSaveLoaded"/> can retry it once after finding 5's adoption retry.
        /// A pre-inversion file only has &lt;Collapsed&gt; elements, which no longer map to a member —
        /// XmlSerializer ignores them, so Expanded is empty and the save loads as fully collapsed.
        /// That is the intended graceful migration (no crash, no carry-over).</summary>
        private static void LoadInto(string key)
        {
            var parsed = SaveScopedXmlStore.LoadPerSave<GridStateFile>(StoreFolder, key, StoreLabel);
            if (parsed?.Expanded == null) return;
            foreach (var id in parsed.Expanded)
                _expanded.Add(id);
            UIALog.Info($"Loaded {_expanded.Count} Grid expand state(s) for save '{key}'.");
        }

        /// <summary>Fix-wave finding 1: drop a dead world's set+key the moment no key resolves any
        /// more (world left, or between-worlds transition), so a stray mutation can never keep
        /// writing into the PREVIOUS world's file under a stale <see cref="_loadedSaveKey"/>. No-op
        /// once already standing down (cheap to call on every failed resolve).</summary>
        private static void StandDown()
        {
            if (_loadedSaveKey == null) return;
            _loadedSaveKey = null;
            _expanded.Clear();
        }

        /// <summary>Write the set now. Memory-only (no world key resolved) NEVER writes — see
        /// <see cref="EnsureSaveLoaded"/>.</summary>
        private static void Save()
        {
            if (_loadedSaveKey == null) return;
            var file = new GridStateFile { Expanded = _expanded.ToList() };
            SaveScopedXmlStore.SavePerSave(StoreFolder, _loadedSaveKey, file, StoreLabel);
        }

        // --- public API ---

        /// <summary>True if the container with this ReferenceId is collapsed. Default (unknown) = collapsed
        /// (new/unseen bags start closed); only containers the player has explicitly expanded return false.</summary>
        public static bool IsCollapsed(long containerRefId)
        {
            EnsureSaveLoaded();
            return !_expanded.Contains(containerRefId);
        }

        /// <summary>Raw collapse lookup that does NOT trigger a save-load — the caller MUST have
        /// called <see cref="EnsureSaveLoaded"/> first. Used by GridModel's per-frame signature
        /// walk so the per-node lookups don't re-derive the save key each call. Default = collapsed.</summary>
        public static bool IsCollapsedLoaded(long containerRefId)
        {
            return !_expanded.Contains(containerRefId);
        }

        /// <summary>Set the collapsed/expanded state for a container; persists only on an actual change.
        /// The persisted set is the EXPANDED one, so collapsing removes the id and expanding adds it.</summary>
        public static void SetCollapsed(long containerRefId, bool collapsed)
        {
            EnsureSaveLoaded();
            bool changed = collapsed ? _expanded.Remove(containerRefId) : _expanded.Add(containerRefId);
            if (changed) Save();
        }

        /// <summary>Flip a container between collapsed and expanded; returns the new collapsed state.</summary>
        public static bool ToggleCollapsed(long containerRefId)
        {
            bool next = !IsCollapsed(containerRefId);
            SetCollapsed(containerRefId, next);
            return next;
        }

        /// <summary>Hot-reload / mod-off: forget the in-memory state (disk file is untouched).</summary>
        public static void Reset()
        {
            _expanded.Clear();
            _loadedSaveKey = null;
        }
    }
}
