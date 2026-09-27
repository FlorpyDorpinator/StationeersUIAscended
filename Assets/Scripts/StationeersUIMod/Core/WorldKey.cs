using System;
using System.IO;
using Assets.Scripts;              // GameManager (fix-wave finding 3: gate on GameState == Running)
using Assets.Scripts.GridSystem;   // GameState
using StationeersUIMod.Features;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The per-WORLD identity every new save-scoped store should key its file on
    /// (SmartStow-Simple-Refactor-Plan §3.1 / §7.1).
    ///
    /// <para><b>Why not the station name.</b> <see cref="BagProfileStore.CurrentSaveKey"/> returns
    /// <c>XmlSaveLoad.CurrentStationName</c>, which is NULL on every multiplayer client (there is no
    /// setter on the join path), so every server a client joins shares the key <c>"unsaved"</c> —
    /// and ReferenceIds restart at 1 per world, so a per-item file keyed that way would mix items
    /// from different servers. The game's own per-world client-pref key is
    /// <c>World.CurrentId</c>, a GUID string: generated when a new world is created, saved into and
    /// restored from the save file, replicated to clients on join, and nulled on leave/quit.
    /// Verified in the 27798 decompile: <c>Assets.Scripts.Objects/World.cs:28</c>
    /// (<c>public static string CurrentId</c>), <c>:167</c> (new world → <c>GenerateWorldId()</c>),
    /// <c>:175-178</c> (<c>PopulateEmptyId</c> back-fills a legacy save), and
    /// <c>Assets.Scripts/GameManager.cs:865, 1004</c> (nulled on leave).</para>
    ///
    /// <para><b>Fallback.</b> A legacy pure-SP save where <c>PopulateEmptyId</c> never ran can reach
    /// us with an empty id; those fall back to <c>"station-&lt;CurrentSaveKey()&gt;"</c>. When even
    /// that is the broken <c>"unsaved"</c> placeholder, <see cref="TryGet"/> reports NO key, and the
    /// consuming store must hold its table in memory only (never write a file) until a key exists.</para>
    ///
    /// <para><see cref="TryGet"/> only ever resolves a key while
    /// <c>GameManager.GameState == GameState.Running</c> — see its own doc comment (fix-wave finding
    /// 3). Effectively stateless otherwise: every call reads the live game statics (plus two small
    /// input-keyed memo caches for allocation, never a source of staleness), so there is nothing to
    /// reset on a hot reload.</para>
    /// </summary>
    public static class WorldKey
    {
        /// <summary>The station-fallback prefix, so a fallback file is recognisable on disk.
        /// Internal (not private): <c>SaveScopedXmlStore.AdoptLegacyFile</c> needs the exact same
        /// literal to look for a station-era fallback file, and must never risk it drifting out of
        /// sync with a hand-copied duplicate (fix-wave finding 2).</summary>
        internal const string StationPrefix = "station-";

        /// <summary>The placeholder <see cref="BagProfileStore.CurrentSaveKey"/> returns when it
        /// knows nothing (MP client, or an SP world before its first save).</summary>
        private const string BrokenStationKey = "unsaved";

        // Input-keyed memo: TryGet is called from a per-frame pump AND the landing observer, and
        // Trim/Sanitize would otherwise allocate every call. Keyed by the RAW CurrentId string, so
        // a stale value is impossible; plain strings, so nothing here needs a hot-reload teardown.
        private static string _memoRaw, _memoKey;

        // Fallback-path memo (fix-wave finding 4): BagProfileStore.CurrentSaveKey() allocates a
        // fresh string per invalid-filename char it replaces, on EVERY call, even when the station
        // name has not changed — and the fallback branch below is walked every call in a
        // fallback-keyed world (e.g. once a frame via StowHomeStore.Tick). Keyed on the RAW station
        // string, exactly like the CurrentId memo above.
        private static string _fallbackMemoRaw, _fallbackMemoKey;

        private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();

        /// <summary>The current world's file key, or false when NO trustworthy key exists yet
        /// (main menu, loading/joining, or an unsaved legacy world) — the caller must then keep its
        /// state in memory only and retry later. Never returns null/empty with true.
        ///
        /// <para><b>Fix-wave finding 3.</b> Refuses to resolve ANY key unless
        /// <c>GameManager.GameState == GameState.Running</c>. Between worlds (main menu, loading,
        /// joining) <c>World.CurrentId</c> is already null, but <c>CurrentStationName</c> (the
        /// fallback branch's input) can still hold the PREVIOUS world's name for a few frames —
        /// <c>GameManager.LeaveGame</c> nulls the former before the latter clears — so without this
        /// gate a caller at the menu could resolve a stale fallback key, trigger a junk legacy-
        /// adoption copy, and load a dead world's table at the menu. This also closes most of finding
        /// 1 (a store serving/saving a previous world's data into the next one) at the source: the
        /// transition between any two worlds passes through a non-Running state, so every consuming
        /// store already sees <c>TryGet</c> fail during that window and can stand down.</para>
        /// </summary>
        public static bool TryGet(out string key)
        {
            key = null;
            try
            {
                if (GameManager.GameState != GameState.Running) return false;

                string id = Assets.Scripts.Objects.World.CurrentId; // [27798] Objects/World.cs:28
                if (!string.IsNullOrEmpty(id))
                {
                    if (ReferenceEquals(id, _memoRaw) || string.Equals(id, _memoRaw, StringComparison.Ordinal))
                    {
                        key = _memoKey;                 // the per-frame path: no allocation
                        return key != null;
                    }
                    string trimmed = id.Trim();         // Trim/Replace return `this` when unchanged
                    key = trimmed.Length > 0 ? Sanitize(trimmed) : null;
                    _memoRaw = id;
                    _memoKey = string.IsNullOrEmpty(key) ? null : key;
                    return _memoKey != null;
                }

                // Legacy fallback: an old SP save with no id yet. The station name is only usable
                // once it is a real name (i.e. the save has been written at least once).
                string station = BagProfileStore.CurrentSaveKey();
                if (ReferenceEquals(station, _fallbackMemoRaw) || string.Equals(station, _fallbackMemoRaw, StringComparison.Ordinal))
                {
                    key = _fallbackMemoKey;             // the per-frame path: no allocation
                    return key != null;
                }
                _fallbackMemoRaw = station;
                if (string.IsNullOrEmpty(station)
                    || string.Equals(station, BrokenStationKey, StringComparison.OrdinalIgnoreCase))
                {
                    _fallbackMemoKey = null;
                    return false;
                }
                key = Sanitize(StationPrefix + station);
                _fallbackMemoKey = string.IsNullOrEmpty(key) ? null : key;
                return _fallbackMemoKey != null;
            }
            catch
            {
                key = null;
                return false;
            }
        }

        /// <summary>Human-readable description of the current key state for diagnostics
        /// (`stowhomes`). ASCII only.</summary>
        public static string Describe()
        {
            try
            {
                string key;
                if (!TryGet(out key)) return "(no world key yet - holding in memory only)";
                bool fallback = key.StartsWith(StationPrefix, StringComparison.Ordinal);
                return "'" + key + "'" + (fallback ? " (legacy station-name fallback)" : " (world GUID)");
            }
            catch { return "(unavailable)"; }
        }

        /// <summary>Make the key safe as a file name (a GUID already is; the station fallback may
        /// not be). Mirrors the replacement CurrentSaveKey itself performs.</summary>
        private static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            for (int i = 0; i < InvalidFileChars.Length; i++)
                raw = raw.Replace(InvalidFileChars[i], '_');
            // Keep pathological lengths out of the filesystem; a GUID is 36 chars.
            if (raw.Length > 96) raw = raw.Substring(0, 96);
            return raw;
        }
    }
}
