using System.Collections.Generic;
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
    /// <c>BepInEx/config/StationeersUIMod/GridPins/&lt;worldKey&gt;.xml</c>, keyed via
    /// <see cref="SaveScopedXmlStore.ResolveSaveKey"/> (the per-world <c>World.CurrentId</c>, not the
    /// station name — SmartStow-Simple-Refactor-Plan §7.7). No key yet (main menu, or a legacy SP
    /// world before its first save) means memory-only — see <see cref="EnsureSaveLoaded"/>.
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

        // Disk plumbing lives in SaveScopedXmlStore (same folder/filename/serializer as before).
        private const string StoreFolder = "GridPins";
        private const string StoreLabel = "Grid pin";

        // --- per-save load/save (keyed off the per-world identity, plan §7.1/§7.7) ---

        /// <summary>(Re)load for the CURRENT world key. No key yet means: hold the in-memory pins
        /// as-is, touch nothing on disk — never "unsaved.xml". A memory-only set that GAINS a key
        /// mid-session (this session's own pins, made before any key existed) is carried into the
        /// newly-keyed file, mirroring <c>StowHomeStore.EnsureLoaded</c>.</summary>
        public static void EnsureSaveLoaded()
        {
            string key;
            if (!SaveScopedXmlStore.ResolveSaveKey(StoreFolder, _loadedSaveKey, out key)) { StandDown(); return; }
            if (key == _loadedSaveKey) return;

            Dictionary<long, Rect> carriedPins = null;
            List<long> carriedOrder = null;
            if (_loadedSaveKey == null && _order.Count > 0)
            {
                carriedPins = new Dictionary<long, Rect>(_pins);
                carriedOrder = new List<long>(_order);
            }

            _loadedSaveKey = key;
            _pins.Clear();
            _order.Clear();
            PinVersion++;
            LoadInto(key);

            // Fix-wave finding 5: an empty result under a brand-new key may mean adoption ran too
            // early (before the station name was set) and missed a legacy file that exists NOW.
            if (_order.Count == 0 && SaveScopedXmlStore.RetryAdoptionIfEmpty(StoreFolder, key))
                LoadInto(key);

            if (carriedOrder != null)
            {
                for (int i = 0; i < carriedOrder.Count; i++)
                {
                    long id = carriedOrder[i];
                    if (!_pins.ContainsKey(id)) _order.Add(id);   // new pin: keep insertion order in sync
                    _pins[id] = carriedPins[id];                   // this session's memory wins over disk
                }
                Save();
            }
        }

        /// <summary>The actual per-save deserialize-and-populate step, split out so
        /// <see cref="EnsureSaveLoaded"/> can retry it once after finding 5's adoption retry.</summary>
        private static void LoadInto(string key)
        {
            var parsed = SaveScopedXmlStore.LoadPerSave<GridPinsFile>(StoreFolder, key, StoreLabel);
            if (parsed == null || parsed.Pins == null) return;
            for (int i = 0; i < parsed.Pins.Count; i++)
            {
                var e = parsed.Pins[i];
                if (e == null || e.RefId == 0L || _pins.ContainsKey(e.RefId)) continue;
                _pins[e.RefId] = Sane(new Rect(e.X, e.Y, e.W, e.H));
                _order.Add(e.RefId);
            }
            UIALog.Info($"Loaded {_order.Count} Grid pin(s) for save '{key}'.");
        }

        /// <summary>Fix-wave finding 1: drop a dead world's pins+key the moment no key resolves any
        /// more (world left, or between-worlds transition), so a stray mutation can never keep
        /// writing into the PREVIOUS world's file under a stale <see cref="_loadedSaveKey"/>. No-op
        /// once already standing down (cheap to call on every failed resolve).</summary>
        private static void StandDown()
        {
            if (_loadedSaveKey == null) return;
            _loadedSaveKey = null;
            _pins.Clear();
            _order.Clear();
            PinVersion++;
        }

        /// <summary>Write the pins now. Memory-only (no world key resolved) NEVER writes — see
        /// <see cref="EnsureSaveLoaded"/>.</summary>
        private static void Save()
        {
            if (_loadedSaveKey == null) return;
            var file = new GridPinsFile();
            for (int i = 0; i < _order.Count; i++)
            {
                long id = _order[i];
                Rect r;
                if (!_pins.TryGetValue(id, out r)) continue;
                file.Pins.Add(new GridPinEntry { RefId = id, X = r.x, Y = r.y, W = r.width, H = r.height });
            }
            SaveScopedXmlStore.SavePerSave(StoreFolder, _loadedSaveKey, file, StoreLabel);
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
