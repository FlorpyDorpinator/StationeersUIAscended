using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.Features
{
    // ---------- XML model ----------

    /// <summary>One pinned container: its persistent ReferenceId plus the screen geometry of the
    /// window it was torn off into. Serialized as flat floats (Unity's Rect is not XML-friendly).</summary>
    public class GridPinEntry
    {
        [XmlAttribute("refId")] public long RefId;
        [XmlAttribute("x")] public float X;
        [XmlAttribute("y")] public float Y;
        [XmlAttribute("w")] public float W;
        [XmlAttribute("h")] public float H;
    }

    [XmlRoot("GridPins")]
    public class GridPinsFile
    {
        [XmlElement("Pin")] public List<GridPinEntry> Pins = new List<GridPinEntry>();
    }

    /// <summary>
    /// Per-save PIN state for "The Grid": which containers the player has torn out of the Universal
    /// Inventory window into their own pinned window, and where that window sits. Keyed by the
    /// container's persistent <see cref="Assets.Scripts.Objects.Thing.ReferenceId"/> — a stable but
    /// SAVE-SCOPED long, so state is stored per save exactly like <see cref="GridCollapseStore"/>
    /// (a new save starts with nothing pinned). Persisted to
    /// <c>BepInEx/config/StationeersUIMod/GridPins/&lt;saveKey&gt;.xml</c>.
    ///
    /// This store is VIEW-ONLY: pinning mutates no game state whatsoever. It only records where the
    /// player wants a container drawn.
    /// </summary>
    public static class GridPinStore
    {
        // Geometry by RefId + a parallel insertion-ordered id list, so callers can enumerate pins
        // in a stable order without allocating an enumerator over the dictionary each frame.
        private static readonly Dictionary<long, Rect> _pins = new Dictionary<long, Rect>();
        private static readonly List<long> _order = new List<long>();
        private static string _loadedSaveKey;

        /// <summary>Bumped on every pin/unpin (NOT on a pure geometry move). Cheap to fold into
        /// <c>GridModel.ComputeSignature</c> so pin/unpin forces a tree rebuild without walking
        /// the pin set.</summary>
        public static int PinVersion { get; private set; }

        /// <summary>How many containers are currently pinned.</summary>
        public static int Count { get { EnsureSaveLoaded(); return _order.Count; } }

        private static string PinDir { get { return Path.Combine(BagProfileStore.ConfigDir, "GridPins"); } }

        // --- per-save load/save (keyed off the same save name BagProfileStore uses) ---

        public static void EnsureSaveLoaded()
        {
            string key = BagProfileStore.CurrentSaveKey();
            if (key == _loadedSaveKey) return;
            _loadedSaveKey = key;
            _pins.Clear();
            _order.Clear();
            PinVersion++;
            try
            {
                var path = Path.Combine(PinDir, key + ".xml");
                if (!File.Exists(path)) return;
                var serializer = new XmlSerializer(typeof(GridPinsFile));
                using (var stream = File.OpenRead(path))
                {
                    var parsed = (GridPinsFile)serializer.Deserialize(stream);
                    if (parsed != null && parsed.Pins != null)
                    {
                        for (int i = 0; i < parsed.Pins.Count; i++)
                        {
                            var e = parsed.Pins[i];
                            if (e == null || e.RefId == 0L || _pins.ContainsKey(e.RefId)) continue;
                            _pins[e.RefId] = Sane(new Rect(e.X, e.Y, e.W, e.H));
                            _order.Add(e.RefId);
                        }
                    }
                }
                UIALog.Info($"Loaded {_order.Count} Grid pin(s) for save '{key}'.");
            }
            catch (Exception e)
            {
                UIALog.Warn("Grid pin load failed: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(PinDir);
                var path = Path.Combine(PinDir, (_loadedSaveKey ?? BagProfileStore.CurrentSaveKey()) + ".xml");
                var file = new GridPinsFile();
                for (int i = 0; i < _order.Count; i++)
                {
                    long id = _order[i];
                    Rect r;
                    if (!_pins.TryGetValue(id, out r)) continue;
                    file.Pins.Add(new GridPinEntry { RefId = id, X = r.x, Y = r.y, W = r.width, H = r.height });
                }
                var serializer = new XmlSerializer(typeof(GridPinsFile));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, file);
            }
            catch (Exception e)
            {
                UIALog.Warn("Grid pin save failed: " + e.Message);
            }
        }

        /// <summary>Fail-soft repair of a geometry read off disk (or handed in by a caller): NaN /
        /// infinite / degenerate sizes become a sensible default so a corrupt file can never spawn
        /// a zero-sized or off-screen-only window.</summary>
        private static Rect Sane(Rect r)
        {
            if (float.IsNaN(r.x) || float.IsInfinity(r.x)) r.x = 0f;
            if (float.IsNaN(r.y) || float.IsInfinity(r.y)) r.y = 0f;
            if (float.IsNaN(r.width) || float.IsInfinity(r.width) || r.width < MinSize) r.width = DefaultWidth;
            if (float.IsNaN(r.height) || float.IsInfinity(r.height) || r.height < MinSize) r.height = DefaultHeight;
            return r;
        }

        private const float MinSize = 32f;
        private const float DefaultWidth = 360f;
        private const float DefaultHeight = 260f;

        // --- public API ---

        /// <summary>True if this container is currently torn out into a pinned window.</summary>
        public static bool IsPinned(long containerRefId)
        {
            EnsureSaveLoaded();
            return _pins.ContainsKey(containerRefId);
        }

        /// <summary>Raw pin lookup that does NOT trigger a save-load — the caller MUST have called
        /// <see cref="EnsureSaveLoaded"/> first. Mirrors <c>GridCollapseStore.IsCollapsedLoaded</c>
        /// for GridModel's per-frame signature/tree walk (no key re-derivation per node).</summary>
        public static bool IsPinnedLoaded(long containerRefId)
        {
            return _pins.ContainsKey(containerRefId);
        }

        /// <summary>Pin a container, recording the geometry of its window. Re-pinning an already
        /// pinned container just updates its geometry.</summary>
        public static void Pin(long containerRefId, Rect geom)
        {
            EnsureSaveLoaded();
            if (containerRefId == 0L) return;
            bool isNew = !_pins.ContainsKey(containerRefId);
            _pins[containerRefId] = Sane(geom);
            if (isNew)
            {
                _order.Add(containerRefId);
                PinVersion++;
            }
            Save();
        }

        /// <summary>Un-pin a container (it returns to the Universal Inventory tree). No-op if it
        /// was not pinned. Geometry is forgotten with the pin.</summary>
        public static void Unpin(long containerRefId)
        {
            EnsureSaveLoaded();
            if (!_pins.Remove(containerRefId)) return;
            _order.Remove(containerRefId);
            PinVersion++;
            Save();
        }

        /// <summary>Geometry of a pinned container's window. False if it is not pinned.</summary>
        public static bool TryGetGeom(long containerRefId, out Rect g)
        {
            EnsureSaveLoaded();
            return _pins.TryGetValue(containerRefId, out g);
        }

        /// <summary>Update a pinned window's geometry (move / resize end). No-op if not pinned;
        /// persists only when the geometry actually moved, so a drag that ends where it started
        /// does not touch the disk.</summary>
        public static void SetGeom(long containerRefId, Rect g)
        {
            EnsureSaveLoaded();
            Rect cur;
            if (!_pins.TryGetValue(containerRefId, out cur)) return;
            Rect next = Sane(g);
            if (cur == next) return;
            _pins[containerRefId] = next;
            Save();
        }

        /// <summary>Fill <paramref name="dest"/> with the pinned container ids in pin order.
        /// Allocation-free for a caller that reuses its list (no IEnumerable, no LINQ) so this is
        /// safe to call every Tick.</summary>
        public static void GetPinned(List<long> dest)
        {
            if (dest == null) return;
            EnsureSaveLoaded();
            dest.Clear();
            for (int i = 0; i < _order.Count; i++)
                dest.Add(_order[i]);
        }

        /// <summary>Hot-reload / mod-off: forget the in-memory state (disk file is untouched).</summary>
        public static void Reset()
        {
            _pins.Clear();
            _order.Clear();
            _loadedSaveKey = null;
            PinVersion = 0;
        }
    }
}
