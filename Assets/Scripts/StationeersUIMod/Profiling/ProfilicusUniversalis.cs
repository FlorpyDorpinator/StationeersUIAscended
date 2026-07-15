// ---------------------------------------------------------------------------
// ProfilicusUniversalis — vendored profiler.
//
// Based on "Profilicus Universalis" by JacksonTheMaster (extracted from his
// TrainMod monorail profiler and generalized into a standalone utility).
// Vendored with permission into Stationeers UI Ascended on 2026-07-13.
//
// Only the profiling CORE is vendored here. The integration hooks — the ImGui
// draw-hook Harmony patch (ProfilicusUniversalisImGuiPatch.cs) and the console
// command registration (ProfilicusCommand.cs) — are intentionally NOT vendored:
// they are hot-reload hazards, so the mod hosts the per-frame Draw() call and
// command wiring inside its OWN existing ImGui and console hooks instead.
//
// Deviations from the original source: namespace (Profilicus ->
// StationeersUIMod.Profiling), this header, and the snapshot output folder
// (now BepInEx/config/StationeersUIMod/ProfilerSnapshots). The public API is
// otherwise unchanged.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

#if !RW_SERVER
using ImGuiNET;
#endif

namespace StationeersUIMod.Profiling
{
    /// <summary>
    /// Small, source-only rolling runtime profiler for Stationeers/Unity mods.
    /// It is inert while its window is hidden. Samples may be submitted from any thread.
    /// </summary>
    public static class ProfilicusUniversalis
    {
        private const float WindowSeconds = 10f;
        private const float RecentSeconds = 2f;
        private const float SortRefreshSeconds = 0.5f;
        private const double SpikeRatio = 2.0;
        private const double SpikeFloorMs = 0.20;

        private static readonly ConcurrentQueue<PendingSample> Pending = new ConcurrentQueue<PendingSample>();
        private static readonly Dictionary<string, FrameMetric> CurrentFrame = new Dictionary<string, FrameMetric>(64);
        private static readonly Dictionary<string, MetricLine> Lines = new Dictionary<string, MetricLine>(64);
        private static readonly List<string> StableRows = new List<string>(64);
        private static readonly List<string> ScratchRows = new List<string>(64);
        private static readonly object Sync = new object();

        private static int _lastDrawFrame = -1;
        private static float _nextSortRefresh;
        private static string _title = "Profilicus Universalis";
        private static string _snapshotFolderName = "ProfilerSnapshots";
        private static string _scenario = "unlabelled";
        private static string _lastSaveMessage;
        private static float _lastSaveMessageUntil;

        public static bool IsVisible { get; private set; }
        public static bool Enabled => IsVisible;
        public static string Scenario => _scenario;

        /// <summary>Call once during mod initialization, before showing the window.</summary>
        public static void Configure(string title, string snapshotFolderName = null)
        {
            if (!string.IsNullOrWhiteSpace(title))
                _title = title.Trim();
            if (!string.IsNullOrWhiteSpace(snapshotFolderName))
                _snapshotFolderName = snapshotFolderName.Trim();
        }

        /// <summary>
        /// Labels the current test configuration in the UI and exported snapshots.
        /// For a fair A/B run: select one scenario, Clear(), warm up, then capture.
        /// </summary>
        public static void SetScenario(string scenario)
        {
            _scenario = string.IsNullOrWhiteSpace(scenario) ? "unlabelled" : scenario.Trim();
        }

        public static string Toggle()
        {
            return SetVisible(!IsVisible);
        }

        public static string SetVisible(bool visible)
        {
            IsVisible = visible;
            return _title + (visible ? " enabled." : " disabled.");
        }

        /// <summary>
        /// Times a synchronous block. The result is CPU wall-clock time, not GPU completion time.
        /// </summary>
        public static Scope Time(string metricName)
        {
            return new Scope(metricName, Enabled);
        }

        /// <summary>
        /// Records an already measured duration. Safe to call from worker threads.
        /// </summary>
        public static void Record(string metricName, double milliseconds)
        {
            if (!Enabled || !IsValidSample(metricName, milliseconds))
                return;
            Pending.Enqueue(new PendingSample(metricName, Math.Max(0.0, milliseconds)));
        }

        public static void Clear()
        {
            lock (Sync)
            {
                while (Pending.TryDequeue(out _)) { }
                CurrentFrame.Clear();
                Lines.Clear();
                StableRows.Clear();
                ScratchRows.Clear();
                _lastDrawFrame = -1;
                _nextSortRefresh = 0f;
            }
        }

        /// <summary>
        /// Called by ProfilicusUniversalisImGuiPatch. You may call it from your own ImGui
        /// draw hook instead, but it must be called once per frame while visible.
        /// </summary>
        public static void Draw()
        {
            if (!IsVisible)
                return;

            FlushOneFrame();
            RefreshRows(false);

#if !RW_SERVER
            ImGui.SetNextWindowSize(new Vector2(1060f, 610f), ImGuiCond.FirstUseEver);
            ImGui.Begin(_title, ImGuiWindowFlags.NoSavedSettings);
            DrawHeader();
            DrawMetricTable();
            ImGui.End();
#endif
        }

        public static string SaveSnapshot()
        {
            try
            {
                FlushOneFrame();
                RefreshRows(true);
                string folder = GetSnapshotFolder();
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder,
                    "Profilicus_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md");
                File.WriteAllText(path, BuildSnapshotMarkdown(DateTime.Now), Encoding.UTF8);
                _lastSaveMessage = "Saved " + path;
                _lastSaveMessageUntil = UnityEngine.Time.realtimeSinceStartup + 8f;
                return _lastSaveMessage;
            }
            catch (Exception ex)
            {
                _lastSaveMessage = "Snapshot failed: " + ex.Message;
                _lastSaveMessageUntil = UnityEngine.Time.realtimeSinceStartup + 8f;
                return _lastSaveMessage;
            }
        }

        private static void FlushOneFrame()
        {
            lock (Sync)
            {
                int frame = UnityEngine.Time.frameCount;
                if (_lastDrawFrame == frame)
                    return;

                while (Pending.TryDequeue(out PendingSample sample))
                    AddToCurrentFrame(sample.Name, sample.Milliseconds);

                float now = UnityEngine.Time.realtimeSinceStartup;
                foreach (KeyValuePair<string, FrameMetric> pair in CurrentFrame)
                {
                    if (!Lines.TryGetValue(pair.Key, out MetricLine line))
                    {
                        line = new MetricLine(pair.Key);
                        Lines.Add(pair.Key, line);
                    }
                    line.AddFrame(now, pair.Value.TotalMs, pair.Value.Count, pair.Value.MaxCallMs);
                }

                foreach (KeyValuePair<string, MetricLine> pair in Lines)
                {
                    if (!CurrentFrame.ContainsKey(pair.Key))
                        pair.Value.AddFrame(now, 0.0, 0, 0.0);
                }

                CurrentFrame.Clear();
                _lastDrawFrame = frame;
            }
        }

        private static void AddToCurrentFrame(string name, double milliseconds)
        {
            if (!CurrentFrame.TryGetValue(name, out FrameMetric metric))
                metric = default;
            metric.TotalMs += milliseconds;
            metric.Count++;
            metric.MaxCallMs = Math.Max(metric.MaxCallMs, milliseconds);
            CurrentFrame[name] = metric;
        }

        private static void RefreshRows(bool force)
        {
            lock (Sync)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (!force && now < _nextSortRefresh)
                    return;
                _nextSortRefresh = now + SortRefreshSeconds;

                ScratchRows.Clear();
                foreach (KeyValuePair<string, MetricLine> pair in Lines)
                {
                    pair.Value.Refresh(now);
                    if (pair.Value.HasUsefulData)
                        ScratchRows.Add(pair.Key);
                }

                ScratchRows.Sort((a, b) =>
                {
                    int score = Lines[b].SortScore.CompareTo(Lines[a].SortScore);
                    return score != 0 ? score : string.Compare(a, b, StringComparison.Ordinal);
                });

                StableRows.Clear();
                StableRows.AddRange(ScratchRows);
            }
        }

        private static bool IsValidSample(string name, double milliseconds)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   !double.IsNaN(milliseconds) &&
                   !double.IsInfinity(milliseconds);
        }

#if !RW_SERVER
        private static readonly Vector4 TextNormal = new Vector4(0.86f, 0.92f, 1f, 1f);
        private static readonly Vector4 TextMuted = new Vector4(0.55f, 0.68f, 0.86f, 1f);
        private static readonly Vector4 TextWarn = new Vector4(1f, 0.78f, 0.28f, 1f);
        private static readonly Vector4 TextBad = new Vector4(1f, 0.34f, 0.34f, 1f);
        private static readonly Vector4 RowA = new Vector4(0.025f, 0.065f, 0.12f, 1f);
        private static readonly Vector4 RowB = new Vector4(0.04f, 0.10f, 0.18f, 1f);
        private static readonly Vector4 RowHot = new Vector4(0.20f, 0.055f, 0.06f, 1f);
        private static readonly Vector4 RowWarm = new Vector4(0.18f, 0.12f, 0.035f, 1f);

        // The scenario/metric-count line only changes when a metric is first seen or the
        // scenario is relabelled; cache it so DrawHeader doesn't concat (and box the int) every frame.
        private static string _headerCache;
        private static string _headerScenario;
        private static int _headerCount = -1;

        private static void DrawHeader()
        {
            ImGui.TextColored(TextNormal, _title);
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
                Clear();
            ImGui.SameLine();
            if (ImGui.Button("Save Snapshot"))
                SaveSnapshot();
            ImGui.SameLine();
            if (ImGui.Button("Close"))
                SetVisible(false);

            if (_headerCache == null || !ReferenceEquals(_scenario, _headerScenario) || Lines.Count != _headerCount)
            {
                _headerScenario = _scenario;
                _headerCount = Lines.Count;
                _headerCache = "Scenario: " + _scenario + " | " + Lines.Count + " metrics | rolling 10 seconds";
            }
            ImGui.TextColored(TextMuted, _headerCache);
            ImGui.TextColored(TextMuted,
                "Frame avg is budget impact. Active avg and Avg/call exclude frames where a method did not run.");
            ImGui.TextColored(TextWarn,
                "GPU note: command submission is timed here; use Unity's GPU Profiler/Recorder for GPU completion cost.");

            if (!string.IsNullOrEmpty(_lastSaveMessage) &&
                UnityEngine.Time.realtimeSinceStartup < _lastSaveMessageUntil)
                ImGui.TextColored(TextMuted, _lastSaveMessage);

            ImGui.Separator();
        }

        private static void DrawMetricTable()
        {
            if (!ImGui.BeginTable("ProfilicusUniversalisMetrics", 10,
                    ImGuiTableFlags.SizingFixedFit |
                    ImGuiTableFlags.NoBordersInBody |
                    ImGuiTableFlags.NoSavedSettings))
                return;

            ImGui.TableSetupColumn("Metric", init_width_or_weight: 330f);
            ImGui.TableSetupColumn("Now");
            ImGui.TableSetupColumn("Frame avg");
            ImGui.TableSetupColumn("Active avg");
            ImGui.TableSetupColumn("Avg/call");
            ImGui.TableSetupColumn("Peak frame");
            ImGui.TableSetupColumn("Max call");
            ImGui.TableSetupColumn("Calls/s");
            ImGui.TableSetupColumn("Max/frame");
            ImGui.TableSetupColumn("Jump");
            ImGui.TableHeadersRow();

            int rowIndex = 0;
            foreach (string key in StableRows)
            {
                if (!Lines.TryGetValue(key, out MetricLine line))
                    continue;
                DrawMetricRow(line, rowIndex++);
            }

            ImGui.EndTable();
        }

        private static void DrawMetricRow(MetricLine line, int rowIndex)
        {
            bool hot = line.IsSpiking || line.TenSecondAverageMs > 8.0;
            bool warm = !hot && line.TenSecondAverageMs > 2.0;
            Vector4 color = hot ? TextBad : warm ? TextWarn : TextNormal;

            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                ImGui.GetColorU32(hot ? RowHot : warm ? RowWarm : (rowIndex & 1) == 0 ? RowA : RowB));

            ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(color, line.Name);
            ImGui.TableSetColumnIndex(1);
            DrawMs(line.CurrentMs, line.NowStr, hot);
            ImGui.TableSetColumnIndex(2);
            DrawMs(line.TenSecondAverageMs, line.AvgStr, hot);
            ImGui.TableSetColumnIndex(3);
            DrawMs(line.ActiveFrameAverageMs, line.ActiveAvgStr, false);
            ImGui.TableSetColumnIndex(4);
            DrawMs(line.AverageCallMs, line.PerCallStr, false);
            ImGui.TableSetColumnIndex(5);
            DrawMs(line.PeakFrameMs, line.PeakStr, hot);
            ImGui.TableSetColumnIndex(6);
            DrawMs(line.MaxCallMs, line.MaxCallStr, false);
            ImGui.TableSetColumnIndex(7);
            ImGui.TextColored(TextNormal, line.CpsStr);
            ImGui.TableSetColumnIndex(8);
            ImGui.TextColored(TextNormal, line.MaxPerFrameStr);
            ImGui.TableSetColumnIndex(9);
            ImGui.TextColored(line.IsSpiking ? TextBad : TextMuted, line.JumpStr);
        }

        // Text is precomputed (0.5s refresh); the threshold color still reads the live value,
        // so cell coloring is unchanged from the per-frame version.
        private static void DrawMs(double value, string text, bool forceRed)
        {
            Vector4 color = forceRed ? TextBad : value > 8.0 ? TextBad : value > 2.0 ? TextWarn : TextNormal;
            ImGui.TextColored(color, text);
        }
#endif

        private static string BuildSnapshotMarkdown(DateTime capturedAt)
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine("# " + EscapeMarkdown(_title) + " Snapshot");
            sb.AppendLine();
            sb.AppendLine("- Captured: " + capturedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("- Scenario: " + EscapeMarkdown(_scenario));
            sb.AppendLine("- Rolling window: 10 seconds");
            sb.AppendLine("- Metrics: " + StableRows.Count);
            sb.AppendLine();
            sb.AppendLine("> CPU wall-clock timings measure the profiled C# scope. For rendering work, they usually measure command submission, not completion on the GPU.");
            sb.AppendLine();
            sb.AppendLine("| Metric | Now ms | Frame avg ms | Active avg ms | Avg/call ms | Peak frame ms | Max call ms | Calls/s | Max/frame | Jump |");
            sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (string key in StableRows)
            {
                if (!Lines.TryGetValue(key, out MetricLine line))
                    continue;
                sb.Append("| ").Append(EscapeMarkdown(line.Name));
                sb.Append(" | ").Append(FormatMs(line.CurrentMs));
                sb.Append(" | ").Append(FormatMs(line.TenSecondAverageMs));
                sb.Append(" | ").Append(FormatMs(line.ActiveFrameAverageMs));
                sb.Append(" | ").Append(FormatMs(line.AverageCallMs));
                sb.Append(" | ").Append(FormatMs(line.PeakFrameMs));
                sb.Append(" | ").Append(FormatMs(line.MaxCallMs));
                sb.Append(" | ").Append(line.CallsPerSecond.ToString("0.0"));
                sb.Append(" | ").Append(line.MaxCallsPerFrame);
                sb.Append(" | ").Append(line.IsSpiking ? "x" + line.SpikeMultiplier.ToString("0.0") : "-");
                sb.AppendLine(" |");
            }
            return sb.ToString();
        }

        private static string GetSnapshotFolder()
        {
            return Path.Combine(Paths.ConfigPath, "StationeersUIMod", _snapshotFolderName);
        }

        private static string FormatMs(double value)
        {
            return value.ToString(value >= 10.0 ? "0.0" : "0.000");
        }

        private static string EscapeMarkdown(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value.Replace("|", "\\|");
        }

        public readonly struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long _start;
            private readonly bool _enabled;

            internal Scope(string name, bool enabled)
            {
                _name = name;
                _enabled = enabled && !string.IsNullOrWhiteSpace(name);
                _start = _enabled ? Stopwatch.GetTimestamp() : 0L;
            }

            public void Dispose()
            {
                if (!_enabled)
                    return;
                long elapsed = Stopwatch.GetTimestamp() - _start;
                Record(_name, elapsed * 1000.0 / Stopwatch.Frequency);
            }
        }

        private readonly struct PendingSample
        {
            public readonly string Name;
            public readonly double Milliseconds;

            public PendingSample(string name, double milliseconds)
            {
                Name = name;
                Milliseconds = milliseconds;
            }
        }

        private struct FrameMetric
        {
            public double TotalMs;
            public double MaxCallMs;
            public int Count;
        }

        private struct FrameSample
        {
            public float Time;
            public double TotalMs;
            public double MaxCallMs;
            public int Calls;
        }

        private sealed class MetricLine
        {
            private readonly List<FrameSample> _samples = new List<FrameSample>(900);

            public string Name { get; }
            public double CurrentMs { get; private set; }
            public double TenSecondAverageMs { get; private set; }
            public double ActiveFrameAverageMs { get; private set; }
            public double AverageCallMs { get; private set; }
            public double PeakFrameMs { get; private set; }
            public double MaxCallMs { get; private set; }
            public double CallsPerSecond { get; private set; }
            public int MaxCallsPerFrame { get; private set; }
            public double SpikeMultiplier { get; private set; }
            public bool IsSpiking { get; private set; }
            public bool HasUsefulData => TenSecondAverageMs > 0.0001 || PeakFrameMs > 0.0001 || CallsPerSecond > 0.0;
            public double SortScore => TenSecondAverageMs + PeakFrameMs * 0.20 + (IsSpiking ? ActiveFrameAverageMs : 0.0);

            // Cached cell strings, rebuilt inside the 0.5s-throttled Refresh so the per-frame
            // Draw allocates nothing. Formatting every cell every frame was the profiler's own
            // dominant allocation — it polluted Frame.Total and GC.Alloc KB/frame (the observer
            // effect). "Now ms" now updates at the 0.5s cadence too; that is intended.
            public string NowStr { get; private set; } = "";
            public string AvgStr { get; private set; } = "";
            public string ActiveAvgStr { get; private set; } = "";
            public string PerCallStr { get; private set; } = "";
            public string PeakStr { get; private set; } = "";
            public string MaxCallStr { get; private set; } = "";
            public string CpsStr { get; private set; } = "";
            public string MaxPerFrameStr { get; private set; } = "";
            public string JumpStr { get; private set; } = "-";

            public MetricLine(string name)
            {
                Name = name;
            }

            public void AddFrame(float now, double totalMs, int calls, double maxCallMs)
            {
                CurrentMs = totalMs;
                _samples.Add(new FrameSample
                {
                    Time = now,
                    TotalMs = totalMs,
                    Calls = calls,
                    MaxCallMs = maxCallMs,
                });
                Trim(now);
            }

            public void Refresh(float now)
            {
                Trim(now);
                double total = 0.0;
                double activeTotal = 0.0;
                double recentTotal = 0.0;
                double previousTotal = 0.0;
                double peak = 0.0;
                double maxCall = 0.0;
                int totalCalls = 0;
                int activeFrames = 0;
                int recentFrames = 0;
                int previousFrames = 0;
                int maxCalls = 0;

                for (int i = 0; i < _samples.Count; i++)
                {
                    FrameSample sample = _samples[i];
                    total += sample.TotalMs;
                    totalCalls += sample.Calls;
                    peak = Math.Max(peak, sample.TotalMs);
                    maxCall = Math.Max(maxCall, sample.MaxCallMs);
                    maxCalls = Math.Max(maxCalls, sample.Calls);
                    if (sample.Calls > 0)
                    {
                        activeTotal += sample.TotalMs;
                        activeFrames++;
                    }
                    if (now - sample.Time <= RecentSeconds)
                    {
                        recentTotal += sample.TotalMs;
                        recentFrames++;
                    }
                    else
                    {
                        previousTotal += sample.TotalMs;
                        previousFrames++;
                    }
                }

                int frameCount = Math.Max(_samples.Count, 1);
                TenSecondAverageMs = total / frameCount;
                ActiveFrameAverageMs = activeFrames > 0 ? activeTotal / activeFrames : 0.0;
                AverageCallMs = totalCalls > 0 ? total / totalCalls : 0.0;
                PeakFrameMs = peak;
                MaxCallMs = maxCall;
                MaxCallsPerFrame = maxCalls;

                float observedSeconds = _samples.Count > 1
                    ? Math.Max(1f, now - _samples[0].Time)
                    : 1f;
                CallsPerSecond = totalCalls / Math.Min(WindowSeconds, observedSeconds);

                double recentAverage = recentFrames > 0 ? recentTotal / recentFrames : 0.0;
                double previousAverage = previousFrames > 0 ? previousTotal / previousFrames : 0.0;
                SpikeMultiplier = recentAverage / Math.Max(previousAverage, 0.001);
                IsSpiking = recentAverage >= SpikeFloorMs &&
                            recentFrames > 4 &&
                            previousFrames > 4 &&
                            SpikeMultiplier >= SpikeRatio;

                // Rebuild the cell strings once per refresh (same formats as the old per-frame draw).
                NowStr = FormatMs(CurrentMs);
                AvgStr = FormatMs(TenSecondAverageMs);
                ActiveAvgStr = FormatMs(ActiveFrameAverageMs);
                PerCallStr = FormatMs(AverageCallMs);
                PeakStr = FormatMs(PeakFrameMs);
                MaxCallStr = FormatMs(MaxCallMs);
                CpsStr = CallsPerSecond.ToString("0.0");
                MaxPerFrameStr = MaxCallsPerFrame.ToString();
                JumpStr = IsSpiking ? "x" + SpikeMultiplier.ToString("0.0") : "-";
            }

            private void Trim(float now)
            {
                float cutoff = now - WindowSeconds;
                int removeCount = 0;
                while (removeCount < _samples.Count && _samples[removeCount].Time < cutoff)
                    removeCount++;
                if (removeCount > 0)
                    _samples.RemoveRange(0, removeCount);
            }
        }
    }
}
