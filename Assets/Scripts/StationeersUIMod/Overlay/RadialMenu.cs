using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using ImGuiNET;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.Overlay
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

        // --- Option A additions ---
        public string StateText;            // live state under the icon: "87%", "5,301 kPa", "x25"
        public Sprite HoverIcon;            // swapped in while hovered (STOW wedges preview the held item)
        public bool StowStyle;              // schema A stow wedge: neutral fill, orange only on hover
        public Action<int> OnScroll;        // scroll-wheel value adjust (+1 / -1 per notch)
        public Func<string> ValueText;      // live value between the scroll triangles
        public Core.ScannedSlot DragSource; // parking: where this item physically lives
        public Slot DropSlot;               // parking: dropping a dragged item goes to this slot
        public Func<DynamicThing, Slot> DropResolver; // parking: pick a slot for the dragged item (bags)

        public bool IsBranch => ChildProvider != null && OnSelect == null;
        public bool HasSlideOut => SlideOutProvider != null;
        public bool IsScrollAdjust => OnScroll != null;
        public bool CanDrag => DragSource != null;
        public bool AcceptsDrop => DropSlot != null || DropResolver != null;

        /// <summary>Resolve where a dragged thing would land on this wedge (null = doesn't fit).</summary>
        public Slot ResolveDrop(DynamicThing dragged)
        {
            if (dragged == null) return null;
            try
            {
                if (DropSlot != null)
                    return DropSlot.Get() == null && Slot.AllowMove(dragged, DropSlot) ? DropSlot : null;
                return DropResolver?.Invoke(dragged);
            }
            catch { return null; }
        }
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
        private float _lastOuterR;          // main ring outer radius as of the last Draw
        private int _slideOutCandidate = -1;      // wedge the cursor is dwelling past the rim on
        private float _slideOutCandidateSince;
        private float _satOpenedAt;
        private const float SlideOutDwellSec = 0.18f;   // dwell before a satellite opens
        private const float SatGraceSec = 0.25f;        // fresh satellites don't steal hold-releases

        // --- Option A: click-on-release, drag-out parking, search mode ---
        private readonly ParkingState _parking = new ParkingState();
        private RadialEntry _press;         // wedge pressed but not yet released
        private bool _pressFromSat;
        private float _pressAt;
        private Vector2 _pressPos;
        private bool _searchOpen;
        private static bool _searchRequested;
        private const float DragHoldSec = 0.25f;  // hold this long on an item wedge to start a drag
        private const float DragMovePx = 14f;     // ...or move this far while pressed

        public bool IsOpen => _stack.Count > 0;
        public bool IsSticky => _sticky;
        public bool IsParking => _parking.Active;
        /// <summary>The search panel owns ALL input while open — the controller must not act
        /// on raw key presses (radial-key re-press, MMB dismiss) that are really typing.</summary>
        public bool IsSearchOpen => _searchOpen;

        /// <summary>Entries call this from OnSelect to flip the radial into search mode.</summary>
        public static void RequestSearch() => _searchRequested = true;
        private static bool ConsumeSearchRequest()
        {
            bool r = _searchRequested;
            _searchRequested = false;
            return r;
        }

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
            _parking.Clear();
            _press = null;
            _searchOpen = false;
            _searchRequested = false;
        }

        /// <summary>Close WITHOUT dropping parked items — Escape, guards and re-taps cancel
        /// parking silently (the items never left their slots). Only the deliberate
        /// RMB-out-of-everything path dumps chips to the ground first.</summary>
        public void Close()
        {
            _stack.Clear();
            _satellite = null;
            _sticky = false;
            _hovered = -1;
            _satHovered = -1;
            _parking.Clear();
            _press = null;
            _searchOpen = false;
            UI.SearchPanelView.Hide();
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
            if (ConsumeSearchRequest())
            {
                _sticky = true;
                _searchOpen = true;
                UI.SearchPanelView.Begin();
                return true;
            }
            Close();
            return false;
        }

        /// <summary>Per-frame input while sticky. Call from Update (outside the ImGui frame).</summary>
        public void UpdateSticky()
        {
            if (!IsOpen || !_sticky) return;

            // Search mode owns all input until it exits (Escape/RMB) or takes an item.
            if (_searchOpen)
            {
                var result = UI.SearchPanelView.UpdateInput();
                if (result == UI.SearchPanelView.Result.Exit)
                {
                    _searchOpen = false;
                    UI.SearchPanelView.Hide();
                    Top().Refresh();
                }
                else if (result == UI.SearchPanelView.Result.Took)
                {
                    _searchOpen = false;
                    UI.SearchPanelView.Hide();
                    // Parked chips must never be silently discarded by an auto-close:
                    // with chips on screen the radial stays locked open, like any action.
                    if (_parking.Active) Top().Refresh();
                    else Close();
                }
                return;
            }

            _parking.Prune();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close(); // cancel path: parked items stay in their slots, nothing drops
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                _press = null; // whatever was pressed no longer means what it meant
                if (_parking.Dragging != null) { _parking.Dragging = null; return; } // cancel the drag
                if (_satellite != null) { _satellite = null; return; }
                if (_stack.Count > 1)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                    Top().Refresh(); // the re-exposed level may be stale (items moved since)
                    _hovered = -1;
                    return;
                }
                DumpChipsToGround(); // the deliberate RMB-out-of-everything: parked items drop
                Close();
                return;
            }

            if (UIAConfig.IsA)
            {
                UpdateStickyOptionA();
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

        /// <summary>
        /// Option A click model: actions run on mouse-UP so that press-and-hold (or
        /// press-and-move) on an item wedge can become a DRAG instead. A successful
        /// action closes the radial — unless parking is in progress, which locks it open.
        /// </summary>
        private void UpdateStickyOptionA()
        {
            var mouse = DrawUtil.MousePos();

            if (Input.GetMouseButtonDown(0))
            {
                try { if (ImGui.GetIO().WantCaptureMouse) return; } catch { }

                // Parked chips sit outside the rings; picking one up beats wedge presses.
                var chip = _parking.ChipAt(mouse);
                if (chip != null)
                {
                    _parking.Chips.Remove(chip);
                    _parking.Dragging = chip;
                    UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                    return;
                }

                RadialEntry entry = null;
                bool fromSat = false;
                if (_satellite != null && _satHovered >= 0)
                {
                    entry = SatEntry(_satHovered);
                    fromSat = true;
                }
                else if (_hovered >= 0 && _mainDist <= _lastOuterR * 1.2f)
                {
                    entry = MainEntry(_hovered);
                }
                if (entry == null || !entry.Enabled) return;
                _press = entry;
                _pressFromSat = fromSat;
                _pressAt = Time.unscaledTime;
                _pressPos = mouse;
                return;
            }

            if (_press != null && Input.GetMouseButton(0))
            {
                bool heldLong = Time.unscaledTime - _pressAt >= DragHoldSec;
                bool movedFar = (mouse - _pressPos).magnitude >= DragMovePx;
                if (_press.CanDrag && (heldLong || movedFar))
                {
                    // One slot = one chip: re-dragging an already-parked item's wedge
                    // replaces its chip instead of minting a duplicate mutation source.
                    _parking.RemoveBySlot(_press.DragSource?.Slot);
                    _parking.Dragging = new ParkingState.Chip
                    {
                        Source = _press.DragSource,
                        Icon = _press.Icon,
                        Name = _press.Label,
                    };
                    _press = null;
                    UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                }
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (_parking.Dragging != null)
                {
                    ResolveDragRelease(mouse);
                    return;
                }
                var entry = _press;
                bool fromSat = _pressFromSat;
                _press = null;
                if (entry == null) return;

                // The press is only valid if the SAME entry object is still under the
                // cursor: RMB-back, satellite auto-close and level refreshes all replace
                // the entry lists, and a cancel gesture must never fire the old action.
                RadialEntry underCursor = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                                        : _hovered >= 0 && _mainDist <= _lastOuterR * 1.2f ? MainEntry(_hovered)
                                        : null;
                if (!ReferenceEquals(entry, underCursor)) return;

                if (entry.IsBranch)
                {
                    if (fromSat) PromoteSatellite();
                    PushBranch(entry);
                    return;
                }
                Execute(entry);
                if (ConsumeSearchRequest())
                {
                    _searchOpen = true;
                    UI.SearchPanelView.Begin();
                    return;
                }
                if (!_parking.Active)
                {
                    Close(); // Option A: one action, radial goes away
                    return;
                }
                // Parking locks the radial open: keep shopping.
                _satellite = null;
                Top().Refresh();
                _hovered = -1;
            }
        }

        /// <summary>Mouse released while dragging an item: drop it into the wedge under the
        /// cursor when that wedge can take it, park it when released on open screen, and
        /// silently cancel otherwise (the item never left its slot).</summary>
        private void ResolveDragRelease(Vector2 mouse)
        {
            var chip = _parking.Dragging;
            _parking.Dragging = null;
            var item = chip?.Source?.Occupant;
            if (item == null || (chip.Source.Expected != null && item != chip.Source.Expected))
            {
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }

            // A wedge only counts as the drop target while the cursor is actually ON the
            // rings: _hovered is directional and stays set arbitrarily far out, and a
            // release at the screen edge must PARK, not insert into whatever wedge the
            // cursor happens to point at.
            RadialEntry target = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                               : _hovered >= 0 && _mainDist <= _lastOuterR * 1.2f ? MainEntry(_hovered)
                               : null;
            if (target != null && target.AcceptsDrop)
            {
                Slot dest = target.ResolveDrop(item);
                if (dest != null && ItemActions.SwapIntoSlot(chip.Source, dest))
                {
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    return;
                }
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }

            bool outsideRings = _mainDist > _lastOuterR + 30f
                && (_satellite == null || _satDist > _satellite.OuterR + 30f);
            if (outsideRings && _parking.Chips.Count < ParkingState.MaxChips)
            {
                _parking.RemoveBySlot(chip.Source.Slot); // one slot = one chip, always
                chip.Pos = mouse;
                _parking.Chips.Add(chip);
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            }
            // else: released over dead space inside the rings, or the screen is full — cancel.
        }

        /// <summary>The deliberate exit-drop: one drop message per parked chip, each verified
        /// against its pinned occupant. This is the single sanctioned multi-message action in
        /// the mod (explicit design decision — see the Option A schema doc).</summary>
        private void DumpChipsToGround()
        {
            if (_parking.Chips.Count == 0) return;
            int dropped = 0;
            foreach (var chip in _parking.Chips)
                if (ItemActions.DropToWorld(chip.Source)) dropped++;
            _parking.Chips.Clear();
            if (dropped > 0) UIAudioManager.Play(UIAudioManager.ObjectPutHash);
        }

        /// <summary>Scroll-wheel value adjust on the hovered wedge (Option A device controls).
        /// Called every frame the radial is open, in both hold and sticky modes.</summary>
        public void UpdateScroll()
        {
            if (!IsOpen || _searchOpen) return;
            float s = Input.mouseScrollDelta.y;
            if (Mathf.Abs(s) < 0.01f) return;
            // Same on-the-rings bound as clicks/drops: _hovered alone is directional and
            // would let a scroll from anywhere on screen adjust a device.
            RadialEntry entry = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                              : _hovered >= 0 && _mainDist <= _lastOuterR * 1.2f ? MainEntry(_hovered)
                              : null;
            if (entry?.OnScroll == null) return;
            try { entry.OnScroll(s > 0f ? 1 : -1); }
            catch (Exception e) { UIALog.Warn("Scroll adjust failed: " + e.Message); }
            if (_satellite != null) RefreshSatellite();
        }

        private void RefreshSatellite()
        {
            if (_satellite?.Provider == null) return;
            try { _satellite.Entries = _satellite.Provider() ?? _satellite.Entries; }
            catch { }
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
            // Roomier satellites: scale up with entry count so wedge labels stay readable.
            float outer = Mathf.Clamp(mainOuterR * 0.55f + Mathf.Max(0, entries.Count - 4) * 8f, 130f, 220f)
                * (UIAConfig.RadialSatelliteScale != null ? UIAConfig.RadialSatelliteScale.Value : 1f);
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

            // Search mode replaces the rings entirely (parked chips stay visible).
            if (_searchOpen)
            {
                UI.UnityRadialView.Hide();
                UI.SearchPanelView.Render();
                UI.ParkedItemsView.Render(_parking, DrawUtil.MousePos());
                return;
            }
            UI.SearchPanelView.Hide();

            var dl = ImGui.GetForegroundDrawList();
            var center = DrawUtil.ScreenCenter;
            float outerR = UIAConfig.RadialOuterRadius.Value;
            _lastOuterR = outerR;
            // Hub floor of 104px: the six-line center readout needs that much vertical room.
            float innerR = Mathf.Clamp(UIAConfig.RadialInnerRadius.Value, 104f, Mathf.Max(104f, outerR - 30f));
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

            // The interaction model (hover, satellites, levels, sticky/hold logic) lives in RadialMenu.
            // Only the paint differs. Unity UGUI (procedural) is the primary renderer.
            if (UIAConfig.UseUnityRadial.Value)
            {
                RadialEntry readout = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                                    : _hovered >= 0 ? MainEntry(_hovered)
                                    : null;
                UI.UnityRadialView.Render(
                    center, innerR, outerR, level.Entries,
                    _satellite == null ? _hovered : -1, level.Title,
                    _satellite?.Center, _satellite?.InnerR ?? 0f, _satellite?.OuterR ?? 0f,
                    _satellite?.Entries, _satHovered, _satellite?.Title,
                    readout, null, _sticky,
                    _parking.Dragging?.Source?.Occupant);
                UI.ParkedItemsView.Render(_parking, mouse);
                return;
            }
            UI.UnityRadialView.Hide();
            UI.ParkedItemsView.Render(_parking, mouse);

            // Legacy ImGui draw-list path (kept for A/B and as reference)
            DrawRing(dl, center, innerR, outerR, level.Entries,
                _satellite == null ? _hovered : (_satellite != null ? _satellite.SourceIndex : -1),
                _satellite != null, solidHub: true);
            if (_satellite != null)
                DrawRing(dl, _satellite.Center, _satellite.InnerR, _satellite.OuterR, _satellite.Entries, _satHovered, false, solidHub: false);

            DrawCenterReadout(dl, center, innerR, level);
        }

        private static void DrawRing(ImDrawListPtr dl, Vector2 center, float innerR, float outerR,
            List<RadialEntry> entries, int hovered, bool dimmed, bool solidHub)
        {
            int count = entries.Count;
            float bgAlpha = dimmed ? 0.14f : 0.25f;
            dl.AddCircleFilled(center, outerR + 6f, Theme.C(0f, 0f, 0f, bgAlpha), 64);
            dl.AddCircle(center, outerR + 6f, Theme.PanelBorder, 64, 1.5f);
            if (solidHub)
                dl.AddCircleFilled(center, innerR - 6f, Theme.HubBg, 48); // readable center readout
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

                uint fill = !entry.Enabled ? Theme.RingDisabled
                          : entry.FillOverride.HasValue ? (i == hovered ? Theme.RingStowHover : entry.FillOverride.Value)
                          : i == hovered ? Theme.RingHover
                          : Theme.RingBg;

                // Wedges touch (no angular gap); a thin radial separator divides them instead.
                // A lone entry is drawn as a complete annulus — stroking a 2*PI arc leaves a
                // notch where the path's ends meet (the "circle doesn't close" bug).
                if (count == 1)
                    DrawUtil.RingFull(dl, center, innerR, outerR, fill);
                else
                    DrawUtil.RingSector(dl, center, innerR, outerR, a0, a1, fill);

                if (i == hovered && entry.Enabled && !dimmed)
                {
                    uint rim = entry.AccentOverride ?? Theme.RingHoverRim;
                    if (count == 1) DrawUtil.CircleOutline(dl, center, outerR - 2f, rim, 3f);
                    else DrawUtil.ArcLine(dl, center, outerR - 2f, a0, a1, rim, 3f);
                }

                // Content: icon (aspect preserved) with one centered, width-fitted label under it.
                float aMid = (a0 + a1) * 0.5f;
                var dir = new Vector2(Mathf.Cos(aMid), Mathf.Sin(aMid));
                float midRadius = (innerR + outerR) * 0.5f;
                var slotCenter = center + dir * midRadius;

                float ringWidth = outerR - innerR;
                float iconSize = Mathf.Clamp(ringWidth * 0.48f, 22f, 56f);

                // Usable label width. The tangential chord at midRadius bounds wedges at the top
                // and bottom; the ring's radial thickness bounds those at the left and right.
                // Blend by direction. Clamping the half-angle at PI/2 matters: with one entry the
                // sector spans 2*PI and sin(PI) == 0, which used to collapse the budget to 42px
                // (the "B.." bug).
                float halfAngle = Mathf.Min(sectorSize * 0.5f, Mathf.PI * 0.5f);
                float chord = 2f * midRadius * Mathf.Sin(halfAngle);
                float availW = Mathf.Abs(dir.x) * ringWidth + Mathf.Abs(dir.y) * chord;
                availW = Mathf.Clamp(availW - 10f, 44f, ringWidth * 2.4f);

                float iconAlpha = (entry.Enabled ? 1f : 0.35f) * contentAlpha;
                uint labelColor = entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled;

                if (entry.Icon != null)
                {
                    DrawUtil.Icon(dl, entry.Icon, slotCenter - new Vector2(0f, ringWidth * 0.16f), iconSize, iconAlpha);
                    float textTop = slotCenter.y - ringWidth * 0.16f + iconSize * 0.5f + 2f;
                    float textH = Mathf.Max(16f, ringWidth * 0.42f);
                    var textCenter = new Vector2(slotCenter.x, textTop + textH * 0.5f);
                    DrawUtil.TextFittedCentered(dl, textCenter, availW, textH, labelColor, entry.Label);
                }
                else
                {
                    DrawUtil.TextFittedCentered(dl, slotCenter, availW, ringWidth * 0.72f, labelColor, entry.Label, 3);
                }

                // State/value line (the Unity renderer's under-icon text). Without it the
                // ImGui fallback is blind while scroll-adjusting a device value.
                string state = null;
                try { state = entry.ValueText != null ? entry.ValueText() : entry.StateText; }
                catch { }
                if (!string.IsNullOrEmpty(state))
                {
                    state = Core.StateText.Strip(state);
                    if (entry.IsScrollAdjust) state = "^ " + state + " v"; // ASCII-only font atlas
                    DrawUtil.TextShadowCentered(dl,
                        slotCenter + new Vector2(0f, ringWidth * 0.30f),
                        entry.IsScrollAdjust ? Theme.Accent : Theme.TextDim, state);
                }

                // ASCII only: the game's ImGui font atlas has no glyphs for fancy arrows.
                if (entry.HasSlideOut)
                    DrawUtil.TextShadowCentered(dl, center + dir * (outerR - 10f), Theme.Accent, ">");
                else if (entry.IsBranch)
                    DrawUtil.TextShadowCentered(dl, center + dir * (outerR - 10f), Theme.TextDim, "+");
            }

            // Wedges now touch, so draw the dividers on top of them (skipped for a lone entry,
            // which is a continuous annulus with no boundaries).
            if (count > 1)
            {
                for (int i = 0; i < count; i++)
                {
                    float boundary = -Mathf.PI * 0.5f - sectorSize * 0.5f + sectorSize * i;
                    DrawUtil.RingSeparator(dl, center, innerR, outerR, boundary, Theme.RingSep, 1.5f);
                }
            }
        }

        /// <summary>The center always says what the hovered entry will do. Every line is
        /// fitted to the hub circle's CHORD at that line's height, so text can never cross
        /// the circle no matter how long the strings or how small the hub.</summary>
        private void DrawCenterReadout(ImDrawListPtr dl, Vector2 center, float innerR, Level level)
        {
            float hubR = innerR - 6f;

            // Usable width inside the circle at vertical offset y (text is ~16px tall).
            float ChordW(float y)
            {
                float edge = Mathf.Abs(y) + 9f;
                if (edge >= hubR) return 0f;
                return 2f * Mathf.Sqrt(hubR * hubR - edge * edge) - 8f;
            }

            void Line(float y, uint color, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                float w = ChordW(y);
                if (w < 24f) return; // no room at this height — drop the line entirely
                // Shrink to fit rather than truncate; single line, so a long name stays whole.
                DrawUtil.TextFittedCentered(dl, center + new Vector2(0f, y), w, 18f, color, text, maxLines: 1);
            }

            // Breadcrumb: which ring the pointer is acting in ("Toolbelt" / "Open: Spray Gun").
            string title = _satellite != null ? _satellite.Title : level.Title;
            if (_satellite != null)
                Line(-58f, Theme.TextDisabled, level.Title);
            Line(-40f, Theme.TextDim, title);

            RadialEntry hovered = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                                : _hovered >= 0 ? MainEntry(_hovered)
                                : null;
            if (hovered == null)
            {
                Line(-12f, Theme.TextDisabled, _sticky ? "LMB select | RMB back" : "release to cancel");
                return;
            }

            string verb = hovered.ActionText ?? (hovered.IsBranch ? "Open" : "Select");
            Line(-14f, hovered.Enabled ? Theme.Accent : Theme.TextDisabled, verb);
            Line(6f, hovered.Enabled ? Theme.TextPrimary : Theme.TextDisabled, hovered.Label);
            Line(26f, Theme.TextDim, hovered.Sublabel);
            if (!hovered.Enabled && !string.IsNullOrEmpty(hovered.DisabledReason))
                Line(46f, Theme.Critical, hovered.DisabledReason);
            else if (!string.IsNullOrEmpty(hovered.Warning))
                Line(46f, Theme.Warn, hovered.Warning);
            else if (hovered.HasSlideOut && _satellite == null)
                Line(46f, Theme.TextDim, "slide out > " + (hovered.SlideOutLabel ?? "more"));
        }
    }
}

