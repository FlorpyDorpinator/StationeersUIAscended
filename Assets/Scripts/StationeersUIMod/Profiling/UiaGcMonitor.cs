using System;
using UnityEngine.Profiling;

namespace StationeersUIMod.Profiling
{
    /// <summary>
    /// GC / allocation telemetry for the profiler — added after Dean Hall's (RocketWerkz) public
    /// critique of timing-only profilers on Stationeers' old-mono runtime: "measuring timings on
    /// C# without considering garbage collection is not useful"; strings/allocations are the
    /// dominant cost, and GC pauses are what players feel. This feeds two rows into the
    /// Profilicus table every frame while the profiler is enabled:
    ///
    ///   • <c>GC.Alloc KB/frame</c> — the mono heap's positive growth per frame ≈ allocation
    ///     rate (between collections the heap only grows; the sawtooth slope IS the alloc rate).
    ///     Healthy idle HUD ≈ 0. Any effect that allocates per frame lights this up.
    ///   • <c>GC.Gen0 collect</c> — one sample per collection observed; the table's Calls/s
    ///     column reads directly as collections per second (what players feel as hitches).
    ///
    /// Costs two counter reads per frame (~ns). Heap source: Unity's mono-heap counter, falling
    /// back to <see cref="GC.GetTotalMemory(bool)"/> if it reports 0 (defensive — mono builds
    /// report fine). Attribution note: this is WHOLE-FRAME allocation; per-effect attribution
    /// comes from the A/B driver, which reports KB/frame deltas with an effect ON vs OFF.
    /// </summary>
    internal static class UiaGcMonitor
    {
        private static long _lastHeap;
        private static int _lastGen0;
        private static bool _primed;

        /// <summary>Current mono heap in bytes (profiler counter, GC fallback).</summary>
        public static long HeapBytes()
        {
            long h = 0;
            try { h = Profiler.GetMonoUsedSizeLong(); } catch { }
            if (h <= 0) { try { h = GC.GetTotalMemory(false); } catch { } }
            return h;
        }

        /// <summary>Called once per frame from the plugin Update. Inert (two early-out reads)
        /// while the profiler is hidden, so it can stay permanently wired.</summary>
        public static void Tick()
        {
            if (!ProfilicusUniversalis.Enabled) { _primed = false; return; }

            long heap = HeapBytes();
            int gen0 = GC.CollectionCount(0);
            if (_primed)
            {
                long delta = heap - _lastHeap;
                // Negative delta = a collection ran this frame; the alloc-rate sample would be
                // garbage (pun intended), so only positive growth is recorded.
                if (delta >= 0) ProfilicusUniversalis.Record("GC.Alloc KB/frame", delta / 1024.0);
                int collections = gen0 - _lastGen0;
                for (int i = 0; i < collections && i < 4; i++)
                    ProfilicusUniversalis.Record("GC.Gen0 collect", 1.0);
            }
            _lastHeap = heap;
            _lastGen0 = gen0;
            _primed = true;
        }

        /// <summary>Hot-reload / teardown / scenario reset.</summary>
        public static void Reset() => _primed = false;
    }
}
