using System;
using System.Collections.Generic;
using Assets.Scripts;               // ConsoleWindow
using BepInEx.Configuration;
using StationeersUIMod.UI.Hud;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The `uiaprof ab &lt;effect&gt;` state machine: measures one effect's real frame cost by
    /// toggling it ON and OFF around two profiled capture windows and reporting the
    /// Frame.Total delta. CPU scopes can't see GPU work, so this whole-frame A/B is the
    /// mod's canonical cost methodology (a NOISY PROXY for GPU cost — accurate when
    /// GPU-bound with vsync off; see the 0.9.0 master plan §6.3/§12.8).
    ///
    /// Protocol per phase: set effect → SetScenario → Clear → WARMUP frames (relayout +
    /// caches settle, samples discarded by the Clear that follows) → Clear → CAPTURE frames
    /// (we accumulate unscaled frame time ourselves for the summary) → SaveSnapshot.
    /// Phase 1 runs the effect ON, phase 2 OFF, then the original value is restored and the
    /// delta printed to the console. Inert unless a run is active — Tick() is one branch.
    /// </summary>
    public static class UiaAbDriver
    {
        private const int WarmupFrames = 120;
        private const int CaptureFrames = 600;

        // ---- the effect registry: name -> toggle. Bools flip; floats zero-and-restore. ----
        private sealed class Fx
        {
            public Func<ConfigEntry<bool>> Bool;
            public Func<ConfigEntry<float>> Float;
        }

        private static readonly Dictionary<string, Fx> _registry =
            new Dictionary<string, Fx>(StringComparer.OrdinalIgnoreCase)
        {
            { "tiera",  new Fx { Bool = () => HudConfig.FxTierA } },
            { "tierb",  new Fx { Bool = () => HudConfig.FxTierB } },
            { "tierc",  new Fx { Bool = () => HudConfig.FxTierC } },
            { "frost",  new Fx { Bool = () => HudConfig.FxTierC } },
            { "dissolve", new Fx { Bool = () => HudConfig.FxDissolveBoot } },
            { "shine",  new Fx { Float = () => HudConfig.FxShine } },
            { "edgelight", new Fx { Float = () => HudConfig.FxEdgeLight } },
            { "irid",   new Fx { Float = () => HudConfig.FxIridescence } },
            { "chroma", new Fx { Float = () => HudConfig.FxChroma } },
            { "pulse",  new Fx { Float = () => HudConfig.FxPulseDepth } },
        };

        public static string KnownEffects => string.Join(", ", new List<string>(_registry.Keys).ToArray());

        // ---- run state ----
        private static bool _active;
        private static string _name;
        private static Fx _fx;
        private static bool _origBool;
        private static float _origFloat;
        private static int _phase;          // 0 = ON, 1 = OFF
        private static int _frames;         // frames left in the current stage
        private static bool _warming;
        private static double _accumMs;     // captured frame-time sum for the current phase
        private static int _accumN;
        private static readonly double[] _phaseAvg = new double[2];
        // Allocation A/B (Dean Hall: timing without GC is not useful on old mono): mono-heap
        // positive growth accumulated over each capture window -> KB/frame per phase.
        private static long _heapLast;
        private static double _accumAllocKb;
        private static readonly double[] _phaseAllocKb = new double[2];

        public static bool Active => _active;

        public static bool Start(string effectName)
        {
            if (_active) { ConsoleWindow.Print("uiaprof ab: a run is already active.", ConsoleColor.Yellow); return false; }
            Fx fx;
            if (!_registry.TryGetValue(effectName ?? "", out fx))
            {
                ConsoleWindow.Print("uiaprof ab: unknown effect '" + effectName + "'. Known: " + KnownEffects, ConsoleColor.Yellow);
                return false;
            }

            _name = effectName.ToLowerInvariant();
            _fx = fx;
            try
            {
                if (_fx.Bool != null) _origBool = _fx.Bool().Value;
                else _origFloat = _fx.Float().Value;
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("uiaprof ab: effect not available (" + e.Message + ")", ConsoleColor.Red);
                return false;
            }

            Profiling.ProfilicusUniversalis.SetVisible(true);
            _active = true;
            _phase = 0;
            BeginPhase();
            ConsoleWindow.Print(
                string.Format("uiaprof ab {0}: ON({1}f warm + {2}f capture) then OFF, ~{3:0}s total. Don't touch the HUD.",
                    _name, WarmupFrames, CaptureFrames, 2 * (WarmupFrames + CaptureFrames) / 60f),
                ConsoleColor.Cyan);
            return true;
        }

        private static void BeginPhase()
        {
            bool on = _phase == 0;
            Apply(on);
            Profiling.ProfilicusUniversalis.SetScenario(_name + (on ? " ON" : " OFF"));
            Profiling.ProfilicusUniversalis.Clear();
            _warming = true;
            _frames = WarmupFrames;
            _accumMs = 0; _accumN = 0;
        }

        private static void Apply(bool on)
        {
            try
            {
                if (_fx.Bool != null) _fx.Bool().Value = on;
                else _fx.Float().Value = on ? _origFloat : 0f;
            }
            catch { }
        }

        /// <summary>Called once per frame from the plugin Update. One branch when idle.</summary>
        public static void Tick()
        {
            if (!_active) return;

            if (_warming)
            {
                if (--_frames > 0) return;
                // Warmup done — drop its samples, start the clean capture window.
                Profiling.ProfilicusUniversalis.Clear();
                _warming = false;
                _frames = CaptureFrames;
                _heapLast = Profiling.UiaGcMonitor.HeapBytes();
                _accumAllocKb = 0;
                return;
            }

            _accumMs += UnityEngine.Time.unscaledDeltaTime * 1000.0;
            _accumN++;
            long heap = Profiling.UiaGcMonitor.HeapBytes();
            if (heap > _heapLast) _accumAllocKb += (heap - _heapLast) / 1024.0; // negative = a GC ran; skip
            _heapLast = heap;
            if (--_frames > 0) return;

            // Capture window complete.
            _phaseAvg[_phase] = _accumN > 0 ? _accumMs / _accumN : 0;
            _phaseAllocKb[_phase] = _accumN > 0 ? _accumAllocKb / _accumN : 0;
            try { Profiling.ProfilicusUniversalis.SaveSnapshot(); } catch { }

            if (_phase == 0) { _phase = 1; BeginPhase(); return; }

            // Both phases done: restore + report.
            RestoreOriginal();
            _active = false;
            double delta = _phaseAvg[0] - _phaseAvg[1];
            double allocDelta = _phaseAllocKb[0] - _phaseAllocKb[1];
            ConsoleWindow.Print(
                string.Format("uiaprof ab {0}: ON {1:0.000} ms/frame, OFF {2:0.000} ms/frame -> cost {3:+0.000;-0.000} ms ({4:0.0}% of the OFF frame). Snapshots saved.",
                    _name, _phaseAvg[0], _phaseAvg[1], delta,
                    _phaseAvg[1] > 0.001 ? delta / _phaseAvg[1] * 100.0 : 0.0),
                ConsoleColor.Cyan);
            ConsoleWindow.Print(
                string.Format("  memory: ON {0:0.00} KB/frame allocated, OFF {1:0.00} KB/frame -> {2:+0.00;-0.00} KB/frame (GC churn is what players feel as hitches).",
                    _phaseAllocKb[0], _phaseAllocKb[1], allocDelta),
                ConsoleColor.Cyan);
        }

        private static void RestoreOriginal()
        {
            try
            {
                if (_fx == null) return;
                if (_fx.Bool != null) _fx.Bool().Value = _origBool;
                else _fx.Float().Value = _origFloat;
            }
            catch { }
        }

        /// <summary>Hot-reload / shutdown: abort any run and put the effect back.</summary>
        public static void Reset()
        {
            if (_active) RestoreOriginal();
            _active = false;
            _fx = null;
            _name = null;
        }
    }
}
