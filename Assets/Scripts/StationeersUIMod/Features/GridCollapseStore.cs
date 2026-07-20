using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model ----------

    [XmlRoot("GridState")]
    public class GridStateFile
    {
        /// <summary>Containers the player has collapsed in The Grid (keyed by Thing.ReferenceId).</summary>
        [XmlElement("Collapsed")] public List<long> Collapsed = new List<long>();

        // Stage 2: pinned-window positions/state persist here too (add a <Pin> element list).
    }

    /// <summary>
    /// Per-save collapse state for "The Grid" (#5). A container section is EXPANDED by default;
    /// once the player collapses it we remember that by the container's persistent
    /// <see cref="Assets.Scripts.Objects.Thing.ReferenceId"/> (a stable save-scoped long). State is
    /// PER SAVE — ReferenceIds are save-scoped, so a new save starts fully expanded — mirroring the
    /// per-save storage in <see cref="BagHotkeyStore"/> / <see cref="BagProfileStore"/>. Persisted to
    /// <c>BepInEx/config/StationeersUIMod/Grid/&lt;saveKey&gt;.xml</c>.
    ///
    /// Stage-2 pin state (pinned windows + positions) will live in the same file/store; the XML
    /// model already leaves a seam for it.
    /// </summary>
    public static class GridCollapseStore
    {
        // Only COLLAPSED containers are stored; absence == expanded (the default).
        private static readonly HashSet<long> _collapsed = new HashSet<long>();
        private static string _loadedSaveKey;

        private static string GridDir => Path.Combine(BagProfileStore.ConfigDir, "Grid");

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _collapsed.Clear();
            try
            {
                var path = Path.Combine(GridDir, key + ".xml");
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(GridStateFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (GridStateFile)serializer.Deserialize(stream);
                    if (parsed?.Collapsed != null)
                        foreach (var id in parsed.Collapsed)
                            _collapsed.Add(id);
                }
                UIALog.Info($"Loaded {_collapsed.Count} Grid collapse state(s) for save '{key}'.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Grid collapse load failed: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(GridDir);
                var path = Path.Combine(GridDir, (_loadedSaveKey ?? BagProfileStore.CurrentSaveKey()) + ".xml");
                var file = new GridStateFile { Collapsed = _collapsed.ToList() };
                var serializer = new XmlSerializer(typeof(GridStateFile));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("Grid collapse save failed: " + e.Message);
            }
        }

        // --- public API ---

        /// <summary>True if the container with this ReferenceId is collapsed. Default (unknown) = expanded.</summary>
        public static bool IsCollapsed(long containerRefId)
        {
            EnsureSaveLoaded();
            return _collapsed.Contains(containerRefId);
        }

        /// <summary>Raw collapse lookup that does NOT trigger a save-load — the caller MUST have
        /// called <see cref="EnsureSaveLoaded"/> first. Used by GridModel's per-frame signature
        /// walk so the per-node lookups don't re-derive the save key each call. Default = expanded.</summary>
        public static bool IsCollapsedLoaded(long containerRefId)
        {
            return _collapsed.Contains(containerRefId);
        }

        /// <summary>Set the collapsed/expanded state for a container; persists only on an actual change.</summary>
        public static void SetCollapsed(long containerRefId, bool collapsed)
        {
            EnsureSaveLoaded();
            bool changed = collapsed ? _collapsed.Add(containerRefId) : _collapsed.Remove(containerRefId);
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
            _collapsed.Clear();
            _loadedSaveKey = null;
        }
    }
}
