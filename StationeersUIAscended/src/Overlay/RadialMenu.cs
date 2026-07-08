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
        public string Sublabel;             // second line: location / state / consequence
        public string Warning;              // orange consequence line ("Heavy Miner will be unpowered")
        public Sprite Icon;
        public bool Enabled = true;
        public string DisabledReason;
        public uint? AccentOverride;
        public Action OnSelect;                       // leaf action; executed on confirm
        public Func<List<RadialEntry>> ChildProvider; // branch: produces the next level
        public object Tag;

        public bool IsBranch => ChildProvider != null;
    }

    /// <summary>
    /// Generic nested pie menu drawn with ImGui draw lists.
    ///
    /// Interaction model (proposal §5.2/§17 answers):
    ///  - Opened by holding a key. While the key is held, releasing over a LEAF confirms it.
    ///  - Releasing over a BRANCH enters it and the radial goes "sticky": left-click selects,
    ///    right-click backs out one level, Escape / re-pressing the radial key closes.
    ///  - The inner dead zone selects nothing; releasing there closes without action.
    /// </summary>
    public sealed class RadialMenu
    {
        private sealed class Level
        {
            public string Title;
            public List<RadialEntry> Entries;
        }

        private readonly List<Level> _stack = new List<Level>();
        private bool _sticky;
        private int _hovered = -1;
        private float _hoverDist;
        private Action<RadialEntry> _onLeaf;

        public bool IsOpen => _stack.Count > 0;
        public bool IsSticky => _sticky;

        public void Open(string title, List<RadialEntry> entries, Action<RadialEntry> onLeaf = null)
        {
            _stack.Clear();
            _stack.Add(new Level { Title = title, Entries = entries ?? new List<RadialEntry>() });
            _sticky = false;
            _hovered = -1;
            _onLeaf = onLeaf;
        }

        public void Close()
        {
            _stack.Clear();
            _sticky = false;
            _hovered = -1;
            _onLeaf = null;
        }

        /// <summary>The radial key was released while open. Returns true if the menu should stay open (sticky).</summary>
        public bool OnHoldReleased()
        {
            if (!IsOpen) return false;
            var entry = HoveredEntry();
            if (entry == null || !entry.Enabled)
            {
                Close();
                return false;
            }
            if (entry.IsBranch)
            {
                Push(entry);
                _sticky = true;
                return true;
            }
            Execute(entry);
            Close();
            return false;
        }

        /// <summary>Per-frame input while sticky (click/back/escape). Call before Draw.</summary>
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
                if (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1);
                else Close();
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                // Clicks on ImGui windows (settings/profile editor) belong to ImGui.
                try { if (ImGui.GetIO().WantCaptureMouse) return; } catch { }
                // Clicks far outside the ring keep the menu open (no accidental selects).
                if (_hoverDist > UIAConfig.RadialOuterRadius.Value * 1.15f) return;
                var entry = HoveredEntry();
                if (entry == null) return; // clicking dead zone/outside keeps the menu
                if (!entry.Enabled) return;
                if (entry.IsBranch)
                {
                    Push(entry);
                    return;
                }
                Execute(entry);
                Close();
            }
        }

        private void Push(RadialEntry branch)
        {
            List<RadialEntry> children;
            try { children = branch.ChildProvider() ?? new List<RadialEntry>(); }
            catch (Exception e)
            {
                UIALog.Warn($"Radial branch '{branch.Label}' failed: {e.Message}");
                children = new List<RadialEntry>();
            }
            _stack.Add(new Level { Title = branch.Label, Entries = children });
            _hovered = -1;
        }

        private void Execute(RadialEntry entry)
        {
            try
            {
                entry.OnSelect?.Invoke();
                _onLeaf?.Invoke(entry);
            }
            catch (Exception e)
            {
                UIALog.Error($"Radial action '{entry.Label}' failed: {e}");
            }
        }

        private RadialEntry HoveredEntry()
        {
            var level = _stack[_stack.Count - 1];
            if (_hovered < 0 || _hovered >= level.Entries.Count) return null;
            return level.Entries[_hovered];
        }

        public void Draw()
        {
            if (!IsOpen) return;
            var level = _stack[_stack.Count - 1];
            var dl = ImGui.GetForegroundDrawList();
            var center = DrawUtil.ScreenCenter;
            float outerR = UIAConfig.RadialOuterRadius.Value;
            float innerR = Mathf.Min(UIAConfig.RadialInnerRadius.Value, outerR - 30f);
            int count = level.Entries.Count;

            // Hover determination from mouse direction
            var mouse = DrawUtil.MousePos();
            var delta = mouse - center;
            float dist = delta.magnitude;
            _hoverDist = dist;
            _hovered = -1;
            if (count > 0 && dist >= innerR * 0.9f)
            {
                float angle = Mathf.Atan2(delta.y, delta.x); // ImGui Y-down; entries laid out same space
                float sector = Mathf.PI * 2f / count;
                // First sector centered at top (-PI/2)
                float rel = DrawUtil.NormalizeAngle(angle + Mathf.PI * 0.5f + sector * 0.5f);
                _hovered = Mathf.FloorToInt(rel / sector) % count;
            }

            // Background disc + dead zone
            dl.AddCircleFilled(center, outerR + 6f, Theme.C(0f, 0f, 0f, 0.25f), 64);
            dl.AddCircle(center, outerR + 6f, Theme.PanelBorder, 64, 1.5f);
            dl.AddCircle(center, innerR - 6f, Theme.PanelBorder, 48, 1.5f);

            if (count == 0)
            {
                DrawUtil.TextShadowCentered(dl, center, Theme.TextDim, "(empty)");
                DrawTitle(dl, center, innerR, level.Title, null);
                return;
            }

            float sectorSize = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                var entry = level.Entries[i];
                float a0 = -Mathf.PI * 0.5f - sectorSize * 0.5f + sectorSize * i;
                float a1 = a0 + sectorSize;
                const float gap = 0.012f;

                uint fill = !entry.Enabled ? Theme.RingDisabled
                          : i == _hovered ? Theme.RingHover
                          : Theme.RingBg;
                DrawUtil.RingSector(dl, center, innerR, outerR, a0 + gap, a1 - gap, fill);
                if (i == _hovered && entry.Enabled)
                    DrawUtil.ArcLine(dl, center, outerR - 2f, a0 + gap, a1 - gap,
                        entry.AccentOverride ?? Theme.RingHoverRim, 3f);

                // Sector content
                float aMid = (a0 + a1) * 0.5f;
                var dir = new Vector2(Mathf.Cos(aMid), Mathf.Sin(aMid));
                var slotCenter = center + dir * ((innerR + outerR) * 0.5f);

                float iconSize = Mathf.Clamp((outerR - innerR) * 0.52f, 24f, 64f);
                if (entry.Icon != null)
                {
                    DrawUtil.Icon(dl, entry.Icon, slotCenter - new Vector2(0f, 8f), iconSize, entry.Enabled ? 1f : 0.35f);
                    var lbl = Truncate(entry.Label, 18);
                    DrawUtil.TextShadowCentered(dl, slotCenter + new Vector2(0f, iconSize * 0.5f + 2f),
                        entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled, lbl);
                }
                else
                {
                    DrawUtil.TextShadowCentered(dl, slotCenter - new Vector2(0f, 8f),
                        entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled, Truncate(entry.Label, 18));
                }
                if (!string.IsNullOrEmpty(entry.Sublabel))
                    DrawUtil.TextShadowCentered(dl, slotCenter + new Vector2(0f, entry.Icon != null ? iconSize * 0.5f + 18f : 8f),
                        Theme.TextDim, Truncate(entry.Sublabel, 22));
                if (entry.IsBranch)
                    DrawUtil.TextShadowCentered(dl, center + dir * (outerR - 12f), Theme.Accent, ">");
            }

            DrawTitle(dl, center, innerR, level.Title, HoveredEntry());
        }

        private static void DrawTitle(ImDrawListPtr dl, Vector2 center, float innerR, string title, RadialEntry hovered)
        {
            // Center readout: level title + hovered entry details
            DrawUtil.TextShadowCentered(dl, center - new Vector2(0f, 26f), Theme.TextDim, title);
            if (hovered != null)
            {
                DrawUtil.TextShadowCentered(dl, center - new Vector2(0f, 6f),
                    hovered.Enabled ? Theme.TextPrimary : Theme.TextDisabled, hovered.Label);
                if (!string.IsNullOrEmpty(hovered.Sublabel))
                    DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 14f), Theme.TextDim, hovered.Sublabel);
                if (!string.IsNullOrEmpty(hovered.Warning))
                    DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 32f), Theme.Warn, hovered.Warning);
                if (!hovered.Enabled && !string.IsNullOrEmpty(hovered.DisabledReason))
                    DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 32f), Theme.Critical, hovered.DisabledReason);
            }
            else
            {
                DrawUtil.TextShadowCentered(dl, center + new Vector2(0f, 0f), Theme.TextDisabled, "-");
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }
    }
}
