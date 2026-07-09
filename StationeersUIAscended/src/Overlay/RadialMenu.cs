using System;
using System.Collections.Generic;
using ImGuiNET;
using StationeersUIAscended.Core;
using UnityEngine;

namespace StationeersUIAscended.Overlay
{
    /// <summary>One selectable wedge of a radial menu.</summary>
    public sealed class RadialEntry
    {
        public string Label;
        public string Sublabel;             // location / state — shown in the center readout
        public string Warning;              // consequence line ("Heavy Miner will be unpowered")
        public string ActionText;           // verb for the center readout ("Equip", "Take to hand", ...)
        public Sprite Icon;
        public bool Enabled = true;
        public string DisabledReason;
        public uint? AccentOverride;
        public uint? FillOverride;          // colors the whole wedge (e.g. orange stow slices)
        public Action OnSelect;                        // primary action (click / release)
        public Func<List<RadialEntry>> ChildProvider;  // branch entered by click/release (when no OnSelect)
        public Func<List<RadialEntry>> SlideOutProvider; // satellite ring opened by sliding past the outer edge
        public string SlideOutLabel;                   // hint: "Open", "Swap with…"
        public object Tag;

        public bool IsBranch => ChildProvider != null && OnSelect == null;
        public bool HasSlideOut => SlideOutProvider != null;
    }

    /// <summary>
    /// Nested pie menu drawn with ImGui draw lists.
    ///
    /// Interaction model:
    ///  - HOLD mode (opened by holding a key): release over an entry runs its primary
    ///    action and closes; release over a pure branch enters it and goes STICKY.
    ///  - STICKY mode: left-click = primary action (menu stays open and refreshes),
    ///    right-click = back / close satellite, Escape or radial key = close.
    ///  - SLIDE-OUT: pushing the cursor past the outer rim over an entry that has a
    ///    slide-out opens a smaller satellite ring next to that wedge (e.g. "open" a
    ///    tool to see its controls/slots, or list swap candidates for a slot).
    ///    Pulling the cursor back toward the center closes the satellite.
    ///  - The center always reads out what the hovered entry will do.
    /// </summary>
    public sealed class RadialMenu
    {
        private sealed class Level
        {
            public string Title;
            public Func<List<RadialEntry>> Provider;
            public List<RadialEntry> Entries;

            public void Refresh()
            {
                try { Entries = Provider?.Invoke() ?? Entries ?? new List<RadialEntry>(); }
                catch (Exception e)
                {
                    UIALog.Warn("Radial level refresh failed: " + e.Message);
                    Entries = Entries ?? new List<RadialEntry>();
                }
            }
        }

        private sealed class SatelliteRing
        {
            public string Title;
            public Func<List<RadialEntry>> Provider;
            public List<RadialEntry> Entries;
            public Vector2 Center;
            public float OuterR;
            public float InnerR;
            public int SourceIndex;
        }

        private readonly List<Level> _stack = new List<Level>();
        private SatelliteRing _satellite;
        private bool _sticky;
        private int _hovered = -1;          // index on the main ring
        private int _satHovered = -1;       // index on the satellite ring
        private float _mainDist;
        private float _satDist;
        private int _slideOutCandidate = -1;      // wedge the cursor is dwelling past the rim on
        private float _slideOutCandidateSince;
        private float _satOpenedAt;
        private const float SlideOutDwellSec = 0.18f;   // dwell before a satellite opens
        private const float SatGraceSec = 0.25f;        // fresh satellites don't steal hold-releases

        public bool IsOpen => _stack.Count > 0;
        public bool IsSticky => _sticky;

        public void Open(string title, Func<List<RadialEntry>> provider, bool sticky = false)
        {
            _stack.Clear();
            var level = new Level { Title = title, Provider = provider };
            level.Refresh();
            _stack.Add(level);
            _satellite = null;
            _sticky = sticky;
            _hovered = -1;
            _satHovered = -1;
        }

        public void Close()
        {
            _stack.Clear();
            _satellite = null;
            _sticky = false;
            _hovered = -1;
            _satHovered = -1;
        }

        /// <summary>Hold-mode release. Returns true if the menu should stay open (went sticky).</summary>
        public bool OnHoldReleased()
        {
            if (!IsOpen) return false;
            // A satellite that just auto-opened must not steal a fast flick-release: unless it
            // has been open long enough to be deliberate, the release means the SOURCE wedge.
            RadialEntry entry;
            if (_satellite != null && _satHovered >= 0 && Time.unscaledTime - _satOpenedAt >= SatGraceSec)
                entry = SatEntry(_satHovered);
            else if (_satellite != null)
                entry = MainEntry(_satellite.SourceIndex);
            else
                entry = MainEntry(_hovered);
            if (entry == null || !entry.Enabled)
            {
                Close();
                return false;
            }
            if (entry.IsBranch)
            {
                if (_satellite != null && _satHovered >= 0) PromoteSatellite();
                PushBranch(entry);
                _sticky = true;
                return true;
            }
            Execute(entry);
            Close();
            return false;
        }

        /// <summary>Per-frame input while sticky. Call from Update (outside the ImGui frame).</summary>
        public void UpdateSticky()
        {
            if (!IsOpen || !_sticky) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                if (_satellite != null) { _satellite = null; return; }
                if (_stack.Count > 1)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                    Top().Refresh(); // the re-exposed level may be stale (items moved since)
                    _hovered = -1;
                    return;
                }
                Close();
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                try { if (ImGui.GetIO().WantCaptureMouse) return; } catch { }

                RadialEntry entry = null;
                bool fromSatellite = false;
                if (_satellite != null && _satHovered >= 0)
                {
                    entry = SatEntry(_satHovered);
                    fromSatellite = true;
                }
                else if (_hovered >= 0 && _mainDist <= UIAConfig.RadialOuterRadius.Value * 1.2f)
                {
                    entry = MainEntry(_hovered);
                }
                if (entry == null || !entry.Enabled) return; // clicks elsewhere keep the menu

                if (entry.IsBranch)
                {
                    if (fromSatellite) PromoteSatellite();
                    PushBranch(entry);
                    return;
                }
                Execute(entry);
                // Sticky "shopping": stay open, drop the satellite, refresh what we're looking at.
                _satellite = null;
                Top().Refresh();
                _hovered = -1;
            }
        }

        // ---------- internals ----------

        private Level Top() => _stack[_stack.Count - 1];

        private RadialEntry MainEntry(int i)
        {
            var entries = Top().Entries;
            return i >= 0 && i < entries.Count ? entries[i] : null;
        }

        private RadialEntry SatEntry(int i)
        {
            if (_satellite?.Entries == null) return null;
            return i >= 0 && i < _satellite.Entries.Count ? _satellite.Entries[i] : null;
        }

        private void PushBranch(RadialEntry branch)
        {
            var level = new Level { Title = branch.Label, Provider = branch.ChildProvider };
            level.Refresh();
            _stack.Add(level);
            _satellite = null;
            _hovered = -1;
        }

        /// <summary>The satellite's level becomes the main ring (used when navigating deeper from a satellite).</summary>
        private void PromoteSatellite()
        {
            if (_satellite == null) return;
            var level = new Level { Title = _satellite.Title, Provider = _satellite.Provider, Entries = _satellite.Entries };
            _stack.Add(level);
            _satellite = null;
            _hovered = -1;
        }

        private void Execute(RadialEntry entry)
        {
            try { entry.OnSelect?.Invoke(); }
            catch (Exception e) { UIALog.Error($"Radial action '{entry.Label}' failed: {e}"); }
        }

        private void OpenSatellite(RadialEntry entry, int sourceIndex, Vector2 mainCenter, float mainOuterR, int mainCount)
        {
            List<RadialEntry> entries;
            try { entries = entry.SlideOutProvider() ?? new List<RadialEntry>(); }
            catch (Exception e)
            {
                UIALog.Warn($"Slide-out '{entry.Label}' failed: {e.Message}");
                entry.SlideOutProvider = null; // don't re-throw every frame while the cursor dwells
                return;
            }
            _satOpenedAt = Time.unscaledTime;
            float sector = Mathf.PI * 2f / Mathf.Max(1, mainCount);
            float aMid = -Mathf.PI * 0.5f + sector * sourceIndex;
            float outer = Mathf.Clamp(mainOuterR * 0.55f, 100f, 190f);
            var dir = new Vector2(Mathf.Cos(aMid), Mathf.Sin(aMid));
            _satellite = new SatelliteRing
            {
                Title = string.IsNullOrEmpty(entry.SlideOutLabel) ? entry.Label : entry.SlideOutLabel + ": " + entry.Label,
                Provider = entry.SlideOutProvider,
                Entries = entries,
                Center = mainCenter + dir * (mainOuterR + outer * 0.75f + 16f),
                OuterR = outer,
                InnerR = outer * 0.34f,
                SourceIndex = sourceIndex,
            };
            _satHovered = -1;
        }

        private static int SectorFromMouse(Vector2 delta, int count)
        {
            if (count <= 0) return -1;
            float angle = Mathf.Atan2(delta.y, delta.x);
            float sector = Mathf.PI * 2f / count;
            float rel = DrawUtil.NormalizeAngle(angle + Mathf.PI * 0.5f + sector * 0.5f);
            int idx = Mathf.FloorToInt(rel / sector);
            return idx >= count ? count - 1 : idx;
        }

        // ---------- drawing ----------

        public void Draw()
        {
            if (!IsOpen) return;
            var dl = ImGui.GetForegroundDrawList();
            var center = DrawUtil.ScreenCenter;
            float outerR = UIAConfig.RadialOuterRadius.Value;
            float innerR = Mathf.Min(UIAConfig.RadialInnerRadius.Value, outerR - 30f);
            var level = Top();
            int count = level.Entries.Count;
            var mouse = DrawUtil.MousePos();

            // --- hover state: main ring ---
            var delta = mouse - center;
            _mainDist = delta.magnitude;
            _hovered = _mainDist >= innerR * 0.9f ? SectorFromMouse(delta, count) : -1;

            // --- hover state: satellite ring ---
            _satHovered = -1;
            if (_satellite != null)
            {
                var satDelta = mouse - _satellite.Center;
                _satDist = satDelta.magnitude;
                if (_satDist <= _satellite.OuterR + 24f)
                {
                    if (_satDist >= _satellite.InnerR * 0.85f)
                        _satHovered = SectorFromMouse(satDelta, _satellite.Entries.Count);
                }
                // Pulling back toward the main ring closes the satellite.
                else if (_mainDist < outerR * 0.8f)
                {
                    _satellite = null;
                }
                // Circling past the rim to a DIFFERENT wedge retargets the slide-out.
                else if (_mainDist > outerR + 14f && _hovered != _satellite.SourceIndex)
                {
                    _satellite = null;
                }
                // While a satellite is open, the main ring never owns the pointer:
                // highlight/clicks would disagree with what the satellite shows.
                if (_satellite != null) _hovered = -1;
            }

            // --- slide-out trigger (dwell-gated so fast flick-releases aren't hijacked) ---
            if (_satellite == null && _hovered >= 0 && _mainDist > outerR + 14f)
            {
                var hoveredEntry = MainEntry(_hovered);
                if (hoveredEntry != null && hoveredEntry.HasSlideOut)
                {
                    if (_slideOutCandidate != _hovered)
                    {
                        _slideOutCandidate = _hovered;
                        _slideOutCandidateSince = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - _slideOutCandidateSince >= SlideOutDwellSec)
                    {
                        OpenSatellite(hoveredEntry, _hovered, center, outerR, count);
                        _slideOutCandidate = -1;
                    }
                }
                else
                {
                    _slideOutCandidate = -1;
                }
            }
            else if (_satellite != null || _mainDist <= outerR + 14f)
            {
                _slideOutCandidate = -1;
            }

            DrawRing(dl, center, innerR, outerR, level.Entries,
                _satellite == null ? _hovered : (_satellite != null ? _satellite.SourceIndex : -1),
                _satellite != null);
            if (_satellite != null)
                DrawRing(dl, _satellite.Center, _satellite.InnerR, _satellite.OuterR, _satellite.Entries, _satHovered, false);

            DrawCenterReadout(dl, center, level);
        }

        private static void DrawRing(ImDrawListPtr dl, Vector2 center, float innerR, float outerR,
            List<RadialEntry> entries, int hovered, bool dimmed)
        {
            int count = entries.Count;
            float bgAlpha = dimmed ? 0.14f : 0.25f;
            dl.AddCircleFilled(center, outerR + 6f, Theme.C(0f, 0f, 0f, bgAlpha), 64);
            dl.AddCircle(center, outerR + 6f, Theme.PanelBorder, 64, 1.5f);
            dl.AddCircle(center, innerR - 6f, Theme.PanelBorder, 48, 1.5f);

            if (count == 0)
            {
                DrawUtil.TextShadowCentered(dl, center, Theme.TextDim, "(empty)");
                return;
            }

            float sectorSize = Mathf.PI * 2f / count;
            float contentAlpha = dimmed ? 0.45f : 1f;
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                float a0 = -Mathf.PI * 0.5f - sectorSize * 0.5f + sectorSize * i;
                float a1 = a0 + sectorSize;
                const float gap = 0.012f;

                uint fill = !entry.Enabled ? Theme.RingDisabled
                          : entry.FillOverride.HasValue ? (i == hovered ? Theme.RingStowHover : entry.FillOverride.Value)
                          : i == hovered ? Theme.RingHover
                          : Theme.RingBg;
                DrawUtil.RingSector(dl, center, innerR, outerR, a0 + gap, a1 - gap, fill);
                if (i == hovered && entry.Enabled && !dimmed)
                    DrawUtil.ArcLine(dl, center, outerR - 2f, a0 + gap, a1 - gap,
                        entry.AccentOverride ?? Theme.RingHoverRim, 3f);

                // Content: icon (aspect preserved) with one centered, width-fitted label under it.
                float aMid = (a0 + a1) * 0.5f;
                var dir = new Vector2(Mathf.Cos(aMid), Mathf.Sin(aMid));
                float midRadius = (innerR + outerR) * 0.5f;
                var slotCenter = center + dir * midRadius;

                float ringWidth = outerR - innerR;
                float iconSize = Mathf.Clamp(ringWidth * 0.48f, 22f, 56f);
                // Room available across the wedge at mid radius, minus padding.
                float maxTextWidth = Mathf.Clamp(2f * midRadius * Mathf.Sin(sectorSize * 0.5f) - 14f, 42f, ringWidth * 1.6f);

                float iconAlpha = (entry.Enabled ? 1f : 0.35f) * contentAlpha;
                if (entry.Icon != null)
                {
                    DrawUtil.Icon(dl, entry.Icon, slotCenter - new Vector2(0f, 9f), iconSize, iconAlpha);
                    string label = DrawUtil.FitText(entry.Label, maxTextWidth);
                    DrawUtil.TextShadowCentered(dl, slotCenter + new Vector2(0f, iconSize * 0.5f + 1f),
                        entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled, label);
                }
                else
                {
                    string label = DrawUtil.FitText(entry.Label, maxTextWidth);
                    DrawUtil.TextShadowCentered(dl, slotCenter,
                        entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled, label);
                }

                // ASCII only: the game's ImGui font atlas has no glyphs for fancy arrows.
                if (entry.HasSlideOut)
                    DrawUtil.TextShadowCentered(dl, center + dir * (outerR - 10f), Theme.Accent, ">");
                else if (entry.IsBranch)
                    DrawUtil.TextShadowCentered(dl, center + dir * (outerR - 10f), Theme.TextDim, "+");
            }
        }

        /// <summary>The center always says what the hovered entry will do.</summary>
        private void DrawCenterReadout(ImDrawListPtr dl, Vector2 center, Level level)
        {
            DrawUtil.TextShadowCentered(dl, center - new Vector2(0f, 34f), Theme.TextDim, level.Title);

            RadialEntry hovered = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                                : _hovered >= 0 ? MainEntry(_hovered)
                                : null;
            if (hovered == null)
            {
                DrawUtil.TextShadowCentered(dl, center - new Vector2(0f, 10f), Theme.TextDisabled,
                    _sticky ? "click: select | right-click: back" : "release to cancel");
                return;
            }

            string verb = hovered.ActionText ?? (hovered.IsBranch ? "Open" : "Select");
            DrawUtil.TextShadowCentered(dl, center - new Vector2(0f, 10f),
                hovered.Enabled ? Theme.Accent : Theme.TextDisabled, verb);
            DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 8f),
                hovered.Enabled ? Theme.TextPrimary : Theme.TextDisabled, hovered.Label);
            if (!string.IsNullOrEmpty(hovered.Sublabel))
                DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 26f), Theme.TextDim, hovered.Sublabel);
            if (!hovered.Enabled && !string.IsNullOrEmpty(hovered.DisabledReason))
                DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 44f), Theme.Critical, hovered.DisabledReason);
            else if (!string.IsNullOrEmpty(hovered.Warning))
                DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 44f), Theme.Warn, hovered.Warning);
            else if (hovered.HasSlideOut && _satellite == null)
                DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 44f), Theme.TextDim,
                    "slide out > " + (hovered.SlideOutLabel ?? "more"));
        }
    }
}
