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
    /// <c>BepInEx/config/StationeersUIMod/Grid/&lt;saveKey&gt;.xml</c>.
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

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _expanded.Clear();
            // A pre-inversion file only has <Collapsed> elements, which no longer map to a member —
            // XmlSerializer ignores them, so Expanded is empty and the save loads as fully collapsed.
            // That is the intended graceful migration (no crash, no carry-over).
            var parsed = SaveScopedXmlStore.LoadPerSave<GridStateFile>(StoreFolder, key, StoreLabel);
            if (parsed?.Expanded == null) return;
            foreach (var id in parsed.Expanded)
                _expanded.Add(id);
            UIALog.Info($"Loaded {_expanded.Count} Grid expand state(s) for save '{key}'.");
        }

        private static void Save()
        {
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
