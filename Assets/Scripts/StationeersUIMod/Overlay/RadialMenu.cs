using System;
using System.Collections.Generic;
using Assets.Scripts;
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
        /// <summary>Wedges per page. Crowded rings page with the Q key instead of a MORE
        /// wedge — pages are windows over the LIVE entry list, so refreshes stay fresh.</summary>
        private static int MaxPerPage
            => Mathf.Max(4, UIAConfig.RadialMaxWedges != null ? UIAConfig.RadialMaxWedges.Value : 14);

        private static int PageCountOf(List<RadialEntry> entries)
            => entries == null || entries.Count == 0 ? 1
             : Mathf.CeilToInt(entries.Count / (float)MaxPerPage);

        private static List<RadialEntry> PageOf(List<RadialEntry> entries, int page)
        {
            if (entries == null) return new List<RadialEntry>();
            int max = MaxPerPage;
            if (entries.Count <= max) return entries;
            int p = Mathf.Clamp(page, 0, PageCountOf(entries) - 1);
            int start = p * max;
            return entries.GetRange(start, Mathf.Min(max, entries.Count - start));
        }

        private sealed class Level
        {
            public string Title;
            public Func<List<RadialEntry>> Provider;
            public List<RadialEntry> Entries;   // the FULL list; rendering pages over it
            public int Page;

            public int PageCount => PageCountOf(Entries);
            public List<RadialEntry> Visible => PageOf(Entries, Page);

            public void Refresh()
            {
                try { Entries = Provider?.Invoke() ?? Entries ?? new List<RadialEntry>(); }
                catch (Exception e)
                {
                    UIALog.Warn("Radial level refresh failed: " + e.Message);
                    Entries = Entries ?? new List<RadialEntry>();
                }
                Page = Mathf.Clamp(Page, 0, PageCount - 1);
            }
        }

        private sealed class SatelliteRing
        {
            public string Title;
            public Func<List<RadialEntry>> Provider;
            public List<RadialEntry> Entries;   // FULL list, paged like levels
            public int Page;
            public Vector2 Center;
            public float OuterR;
            public float InnerR;
            public int SourceIndex;

            public int PageCount => PageCountOf(Entries);
            public List<RadialEntry> Visible => PageOf(Entries, Page);
        }

        private readonly List<Level> _stack = new List<Level>();
        private SatelliteRing _satellite;
        private bool _sticky;
        private int _hovered = -1;          // index on the main ring
        private int _satHovered = -1;       // index on the satellite ring
        private float _mainDist;
        private float _satDist;
        private float _lastOuterR;          // main ring outer radius as of the last Draw
        private float _lastInnerR;          // main ring inner (hub) radius as of the last Draw
        private bool _closeHovered;         // cursor on the hub CLOSE button
        private Vector2 _centerOffset;      // hub drag: radial moved away from screen center
        private bool _hubDragging;
        private Vector2 _lastDragMouse;
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
        private float _pendingRefreshAt;   // MP: re-read labels after the server applied an action
        private static bool _searchRequested;
        private const float DragHoldSec = 0.25f;  // hold this long on an item wedge to start a drag
        private const float DragMovePx = 14f;     // ...or move this far while pressed

        // --- Option B: The Hub + hold-mode navigation ---
        /// <summary>Tag marking the toolbelt's Hub wedge (identification/styling; since
        /// 0.6.1 hold mode dwell-enters ANY branch wedge, not just the Hub).</summary>
        public static readonly object HubTag = new object();
        private float _branchEnteredAt = -999f;   // hold mode: guards release right after entering
        private int _dwellIndex = -1;
        private float _dwellSince;
        private const float BranchDwellSec = 0.25f;    // resting on a branch this long enters it
        private const float BranchGraceSec = 0.30f;    // release inside this window = cancel, not run

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

        /// <summary>Held Shift means "keep the radial open after this action" (Option A).</summary>
        internal static bool ShiftHeld
            => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /// <summary>Q flips to the next page of a crowded ring (satellite first when open).
        /// Vanilla Q-throw is Game-state-bound, so the radial modal already suppresses it.</summary>
        public void NextPage()
        {
            if (!IsOpen || _searchOpen) return;
            if (_satellite != null && _satellite.PageCount > 1)
            {
                _satellite.Page = (_satellite.Page + 1) % _satellite.PageCount;
                _satHovered = -1;
                UIAudioManager.Play(UIAudioManager.ClickLightHash);
                return;
            }
            var lvl = Top();
            if (lvl.PageCount > 1)
            {
                lvl.Page = (lvl.Page + 1) % lvl.PageCount;
                _hovered = -1;
                _press = null; // the wedge under the cursor just changed identity
                UIAudioManager.Play(UIAudioManager.ClickLightHash);
            }
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
            _centerOffset = Vector2.zero;   // a moved hub snaps back to center on reopen
            _hubDragging = false;
            _closeHovered = false;
            _branchEnteredAt = -999f;
            _dwellIndex = -1;
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
            _centerOffset = Vector2.zero;
            _hubDragging = false;
            _closeHovered = false;
            _branchEnteredAt = -999f;
            _dwellIndex = -1;
            UI.SearchPanelView.Hide();
        }

        /// <summary>Hold-mode release. Returns true if the menu should stay open (went sticky).</summary>
        public bool OnHoldReleased()
        {
            if (!IsOpen) return false;
            // Releasing over the hub CLOSE button is a cancel, never a select.
            if (_closeHovered)
            {
                Close();
                return false;
            }
            // Option B: releasing right after diving into a branch (Hub dwell / LMB) is
            // gesture momentum — the cursor is parked over whatever wedge happens to sit
            // where the branch wedge was, and that must never run as a selection.
            if (UIAConfig.IsB && Time.unscaledTime - _branchEnteredAt < BranchGraceSec)
            {
                Close();
                return false;
            }
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
                // Option B: hold mode is strictly transient — "closes when you let go".
                // Branch diving is LMB / Hub dwell WHILE held; tap MMB for the sticky mode.
                if (UIAConfig.IsB)
                {
                    Close();
                    return false;
                }
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
            if (ShiftHeld)
            {
                // Shift = "keep it open, I'm not done" — the radial goes sticky.
                _sticky = true;
                _satellite = null;
                Top().Refresh();
                _hovered = -1;
                _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: refresh again post-roundtrip
                return true;
            }
            Close();
            return false;
        }

        /// <summary>Option B, per-frame while open in HOLD mode (MMB still down): resting on
        /// any branch wedge (The Hub, a bag, a category) for a beat dwell-enters it, LMB
        /// enters immediately or runs an action and closes, RMB backs out one level.
        /// Everything stays transient — the release itself is handled by OnHoldReleased.
        /// Call from Update (outside the ImGui frame).</summary>
        public void UpdateHoldB()
        {
            if (!IsOpen || _sticky || _searchOpen) return;

            // Dwell-entry: resting on ANY branch wedge for a beat enters it, so a held-MMB
            // journey (Hub -> Backpack -> category -> item) needs no clicks at all. Sweeps
            // across the ring stay under the dwell; entering a level resets the timer
            // (PushBranch clears _hovered), and the release grace covers quick lets-go.
            if (_satellite == null && _hovered >= 0)
            {
                var h = MainEntry(_hovered);
                if (h != null && h.Enabled && h.IsBranch)
                {
                    if (_dwellIndex != _hovered)
                    {
                        _dwellIndex = _hovered;
                        _dwellSince = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - _dwellSince >= BranchDwellSec)
                    {
                        PushBranch(h);
                        _branchEnteredAt = Time.unscaledTime;
                        _dwellIndex = -1;
                        UIAudioManager.Play(UIAudioManager.ClickLightHash);
                        return;
                    }
                }
                else _dwellIndex = -1;
            }
            else _dwellIndex = -1;

            if (Input.GetMouseButtonDown(1))
            {
                // Back out one step, mirroring sticky's RMB. At the root RMB does nothing:
                // releasing MMB is the exit, and parking never runs in hold mode.
                if (_satellite != null) { _satellite = null; return; }
                if (_stack.Count > 1)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                    Top().Refresh(); // the re-exposed level may be stale (items moved since)
                    _hovered = -1;
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;
            try { if (ImGui.GetIO().WantCaptureMouse) return; } catch { }

            if (_closeHovered)
            {
                Close();
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

            if (entry.IsBranch)
            {
                if (fromSat) PromoteSatellite();
                PushBranch(entry);
                _branchEnteredAt = Time.unscaledTime;
                UIAudioManager.Play(UIAudioManager.ClickLightHash);
                return;
            }
            Execute(entry);
            if (ConsumeSearchRequest())
            {
                _sticky = true;         // the search panel needs the menu latched open
                _searchOpen = true;
                UI.SearchPanelView.Begin();
                return;
            }
            if (ShiftHeld)
            {
                // Shift = "keep it open, I'm not done" — stay transient, just refreshed.
                _satellite = null;
                Top().Refresh();
                _hovered = -1;
                _pendingRefreshAt = Time.unscaledTime + 0.6f;
                return;
            }
            Close(); // one action per open, same contract as A's click model
        }

        /// <summary>Per-frame input while sticky. Call from Update (outside the ImGui frame).</summary>
        public void UpdateSticky()
        {
            if (!IsOpen || !_sticky) return;

            // Search mode owns all input until it exits (Escape/RMB) or takes an item.
            if (_searchOpen)
            {
                var result = UI.SearchPanelView.UpdateInput(
                    _parking, DrawUtil.ScreenCenter + _centerOffset, _lastInnerR, _lastOuterR);
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

            // On a multiplayer client, a keep-open action refreshes entries BEFORE the
            // server has applied it — labels/values would stay one state behind. A second
            // refresh after the round-trip window fixes that.
            if (_pendingRefreshAt > 0f && Time.unscaledTime >= _pendingRefreshAt)
            {
                _pendingRefreshAt = 0f;
                Top().Refresh();
                RefreshSatellite();
            }

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

            // --- hub interactions, shared by both schemas ---
            var hubMouse = DrawUtil.MousePos();
            if (_hubDragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    _hubDragging = false;
                }
                else
                {
                    _centerOffset += hubMouse - _lastDragMouse;
                    _lastDragMouse = hubMouse;
                }
                return; // dragging the radial around owns the mouse
            }
            if (Input.GetMouseButtonDown(0) && _parking.Dragging == null)
            {
                bool imguiOwns = false;
                try { imguiOwns = ImGui.GetIO().WantCaptureMouse; } catch { }
                if (!imguiOwns)
                {
                    if (_closeHovered)
                    {
                        // The always-works exit. A deliberate close, so parked items drop
                        // (same contract as RMB-out).
                        DumpChipsToGround();
                        Close();
                        return;
                    }
                    // Grabbing the hub (inside the ring, off the CLOSE band) moves the radial.
                    float hubR = Mathf.Max(0f, _lastInnerR - 6f);
                    if (_satellite == null && _mainDist < hubR)
                    {
                        _hubDragging = true;
                        _lastDragMouse = hubMouse;
                        return;
                    }
                }
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

            // Option B: MMB is the SELECT button in sticky radials — the whole gesture is
            // "tap to open, flick, tap to pick". Wedge = select (branches navigate, actions
            // run + close), CLOSE band = deliberate close, empty space = dismiss (parked
            // items stay in their slots). LMB keeps working exactly as in A.
            if (UIAConfig.IsB && Input.GetMouseButtonDown(2)
                && _parking.Dragging == null && _press == null)
            {
                bool imguiOwnsM = false;
                try { imguiOwnsM = ImGui.GetIO().WantCaptureMouse; } catch { }
                if (!imguiOwnsM)
                {
                    if (_closeHovered)
                    {
                        DumpChipsToGround(); // same contract as clicking CLOSE
                        Close();
                        return;
                    }
                    RadialEntry mmb = null;
                    bool mmbFromSat = false;
                    if (_satellite != null && _satHovered >= 0)
                    {
                        mmb = SatEntry(_satHovered);
                        mmbFromSat = true;
                    }
                    else if (_hovered >= 0 && _mainDist <= _lastOuterR * 1.2f)
                    {
                        mmb = MainEntry(_hovered);
                    }
                    if (mmb == null)
                    {
                        Close(); // tap on nothing = dismiss
                        return;
                    }
                    if (!mmb.Enabled)
                    {
                        UIAudioManager.Play(UIAudioManager.ActionFailHash);
                        return;
                    }
                    SelectSticky(mmb, mmbFromSat);
                    return;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                try { if (ImGui.GetIO().WantCaptureMouse) return; } catch { }

                // Z-grab (the vanilla MouseControl key, default Alt): clicking a world item
                // under the cursor tears it into the drag layer as if dragged off a wedge —
                // move the radial aside (hub drag), grab things off the floor, drop them
                // into bags. Range-gated at grab AND at drop (the server doesn't check).
                bool mouseMod = false;
                try { mouseMod = KeyManager.GetButton(KeyMap.MouseControl); } catch { }
                if (mouseMod)
                {
                    var worldThing = WorldItemUnderCursor();
                    if (worldThing != null)
                    {
                        Sprite icon = null;
                        try { icon = worldThing.GetThumbnail(); } catch { }
                        _parking.RemoveByWorldThing(worldThing); // one item = one chip
                        _parking.Dragging = new ParkingState.Chip
                        {
                            WorldSource = worldThing,
                            Icon = icon,
                            Name = worldThing.DisplayName,
                        };
                        _satellite = null; // hands are busy: no child radials while dragging
                        UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                        return;
                    }
                    // Bug 8: grab an item OUT of a physical-world object's slot (a locker, a
                    // charger, a crate) into the drag layer — the vanilla drag-from-world-slot,
                    // range-bounded by the interaction raycast. It becomes a normal slot-sourced
                    // chip (Expected-pinned), so dropping it anywhere routes through the funnel.
                    var grabSlot = WorldSlotUnderCursor();
                    DynamicThing grabOcc = grabSlot?.Get();
                    if (grabSlot != null && grabOcc is Item)
                    {
                        Sprite icon = null;
                        try { icon = grabOcc.GetThumbnail(); } catch { }
                        _parking.RemoveBySlot(grabSlot); // one slot = one chip
                        _parking.Dragging = new ParkingState.Chip
                        {
                            Source = new ScannedSlot { Slot = grabSlot, Holder = grabSlot.Parent, Location = "" }.Pin(),
                            Icon = icon,
                            Name = grabOcc.DisplayName,
                        };
                        _satellite = null; // hands are busy: no child radials while dragging
                        UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                        return;
                    }
                }

                // Parked chips sit outside the rings; picking one up beats wedge presses.
                var chip = _parking.ChipAt(mouse);
                if (chip != null)
                {
                    _parking.Chips.Remove(chip);
                    _parking.Dragging = chip;
                    _satellite = null; // hands are busy: no child radials while dragging
                    UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                    return;
                }

                // Bugs 2 & 5: the visor HUD hand boxes and the six equipment boxes are drag
                // SOURCES too, not just drop targets — press one that holds an item to tear it
                // into the drag layer (drag your held item into a STOW wedge, swap two boxes,
                // pull a worn piece onto the belt). Boxes live outside the rings, so this can
                // never shadow a wedge press. The chip is Expected-pinned like any other.
                var hudZone = UI.Hud.HudSystem.ZoneAt();
                DynamicThing hudOcc = hudZone?.Slot?.Get();
                if (hudOcc != null)
                {
                    Sprite icon = null;
                    try { icon = hudOcc.GetThumbnail(); } catch { }
                    _parking.RemoveBySlot(hudZone.Slot); // one slot = one chip
                    _parking.Dragging = new ParkingState.Chip
                    {
                        Source = new ScannedSlot { Slot = hudZone.Slot, Holder = hudZone.Slot.Parent, Location = hudZone.Label ?? "" }.Pin(),
                        Icon = icon,
                        Name = hudOcc.DisplayName,
                    };
                    _satellite = null; // hands are busy: no child radials while dragging
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
                    _satellite = null; // hands are busy: no child radials while dragging
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

                SelectSticky(entry, fromSat);
            }
        }

        /// <summary>Sticky-mode selection of a resolved, enabled entry — shared by the LMB
        /// click model and Option B's MMB tap-select. Branches navigate; actions run and
        /// close unless search/parking/Shift keeps the radial open.</summary>
        private void SelectSticky(RadialEntry entry, bool fromSat)
        {
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
            if (!_parking.Active && !ShiftHeld)
            {
                Close(); // one action, radial goes away
                return;
            }
            // Parking locks the radial open; Shift means "I'm not done yet".
            _satellite = null;
            Top().Refresh();
            _hovered = -1;
            _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: refresh again post-roundtrip
        }

        /// <summary>Mouse released while dragging an item: drop it into the wedge under the
        /// cursor when that wedge can take it, park it when released on open screen, and
        /// silently cancel otherwise (the item never left its slot / the floor).</summary>
        private void ResolveDragRelease(Vector2 mouse)
        {
            var chip = _parking.Dragging;
            _parking.Dragging = null;
            var item = chip?.Item;
            bool stale = item == null
                || (chip.IsWorld
                    ? chip.WorldSource.ParentSlot != null // someone picked it up meanwhile
                    : chip.Source.Expected != null && item != chip.Source.Expected);
            if (stale)
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
                bool moved = dest != null && (chip.IsWorld
                    ? ItemActions.MoveWorldItemToSlot(chip.WorldSource, dest)
                    : ItemActions.SwapIntoSlot(chip.Source, dest));
                if (moved)
                {
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    // Bug 1: on an MP client the slot is still empty this frame (the move is a
                    // server round-trip); a second refresh once it lands makes the item appear
                    // in the wedge without reopening the radial. Matches the scroll/select paths.
                    _pendingRefreshAt = Time.unscaledTime + 0.6f;
                    return;
                }
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }

            // 0.6.2: releasing over a visor HUD hand box or equipment box drops the chip
            // INTO that slot (swap when occupied — the ItemActions funnel gates it all).
            // A release on a box is always consumed: it must never fall through to parking.
            if (TryDropOnHudZone(chip, item))
                return;

            // Bug 7: releasing over a slot on a PHYSICAL-WORLD object (a battery charger, a
            // locker) drops the chip into that slot — swap when occupied, so the game sees a
            // radial battery being installed into / swapped with the charger's battery. The
            // release raycast bounds it to the vanilla interaction range.
            if (TryDropOnWorldSlot(chip, item))
                return;

            bool outsideRings = _mainDist > _lastOuterR + 30f
                && (_satellite == null || _satDist > _satellite.OuterR + 30f);
            if (outsideRings && _parking.Chips.Count < ParkingState.MaxChips)
            {
                if (chip.IsWorld) _parking.RemoveByWorldThing(chip.WorldSource);
                else _parking.RemoveBySlot(chip.Source.Slot); // one source = one chip, always
                chip.Pos = mouse;
                _parking.Chips.Add(chip);
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            }
            // else: released over dead space inside the rings, or the screen is full — cancel.
        }

        /// <summary>0.6.2: drop a dragged chip onto a visor HUD hand/equipment box. Returns
        /// true when the release landed ON a box (whether or not the move succeeded — a
        /// failed move plays the fail sound and eats the release; it never parks). Uses the
        /// same hit-test as the box drag-SOURCE grab, so both agree exactly.</summary>
        private bool TryDropOnHudZone(ParkingState.Chip chip, DynamicThing item)
        {
            try
            {
                var zone = UI.Hud.HudSystem.ZoneAt();
                if (zone?.Slot == null) return false;
                bool moved = chip.IsWorld
                    ? ItemActions.MoveWorldItemToSlot(chip.WorldSource, zone.Slot)
                    : ItemActions.SwapIntoSlot(chip.Source, zone.Slot);
                if (moved)
                {
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: box refills post-roundtrip
                }
                // failure already played the fail sound inside ItemActions.Fail()
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("HUD drop zone failed: " + e.Message);
            }
            return false;
        }

        /// <summary>Bug 7: drop a dragged chip into a slot on a physical-world object (charger,
        /// locker, crate). Returns true when the release landed ON a world slot (whether or not
        /// the move succeeded), so it consumes the release instead of parking. The mutation goes
        /// through the same funnel as any slot move/swap; range is bounded by the raycast.</summary>
        private bool TryDropOnWorldSlot(ParkingState.Chip chip, DynamicThing item)
        {
            try
            {
                var worldSlot = WorldSlotUnderCursor();
                if (worldSlot == null) return false;
                bool moved = chip.IsWorld
                    ? ItemActions.MoveWorldItemToSlot(chip.WorldSource, worldSlot)
                    : ItemActions.SwapIntoSlot(chip.Source, worldSlot);
                if (moved)
                {
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    _pendingRefreshAt = Time.unscaledTime + 0.6f;
                }
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("World slot drop failed: " + e.Message);
            }
            return false;
        }

        /// <summary>The deliberate exit-drop: one drop message per parked chip, each verified
        /// against its pinned occupant. This is the single sanctioned multi-message action in
        /// the mod (explicit design decision — see the Option A schema doc).</summary>
        private void DumpChipsToGround()
        {
            if (_parking.Chips.Count == 0) return;
            int dropped = 0;
            foreach (var chip in _parking.Chips)
            {
                if (chip.IsWorld) continue; // world-grabbed chips never left the world
                if (ItemActions.DropToWorld(chip.Source)) dropped++;
            }
            _parking.Chips.Clear();
            if (dropped > 0) UIAudioManager.Play(UIAudioManager.ObjectPutHash);
        }

        /// <summary>Own per-frame raycast (mirrors InputMouse.Idle's, InputMouse.cs:381-384) —
        /// InputMouse.CursorThing goes stale on miss and its updater has side effects, and
        /// BlockCursorRaycast (set while our modal is open) gates the vanilla one anyway.
        /// Only free-lying DynamicThings count; range = the vanilla 3 m cursor cap.</summary>
        private static DynamicThing WorldItemUnderCursor()
        {
            try
            {
                var cam = CameraController.CurrentCamera;
                if (cam == null) return null;
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                float maxDist = 3f;
                try { maxDist = CursorManager.MaxInteractDistance; } catch { }
                int mask = CursorManager.Instance != null ? (int)CursorManager.Instance.CursorHitMask : ~0;
                RaycastHit hit;
                if (!Physics.Raycast(ray, out hit, maxDist, mask)) return null;
                // `as Item`, matching vanilla's own pickup filter (InputMouse.cs:384) —
                // NOT DynamicThing: entities (chickens, players) and non-item dynamics
                // (lander capsule!) must never mint grab chips.
                var thing = Thing.Find(hit.collider) as Item;
                if (thing == null || thing.ParentSlot != null) return null; // free-lying only
                return thing;
            }
            catch { return null; }
        }

        /// <summary>The slot on a physical-world object under the cursor (a locker/charger/crate
        /// slot), mirroring vanilla's InputMouse.GetHoverWorldSlot (InputMouse.cs:288-304):
        /// raycast → the collider's Thing → its Interactable → the Interactable's Slot. Bounded
        /// by the vanilla interaction range, so both the grab (bug 8) and the drop (bug 7) can
        /// never out-reach the vanilla cursor. Independent of BlockCursorRaycast, like the item
        /// raycast above.</summary>
        private static Slot WorldSlotUnderCursor()
        {
            try
            {
                var cam = CameraController.CurrentCamera;
                if (cam == null) return null;
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                float maxDist = 3f;
                try { maxDist = CursorManager.MaxInteractDistance; } catch { }
                int mask = CursorManager.Instance != null ? (int)CursorManager.Instance.CursorHitMask : ~0;
                RaycastHit hit;
                if (!Physics.Raycast(ray, out hit, maxDist, mask)) return null;
                var thing = hit.transform.GetComponentInParent<Thing>();
                if (thing == null) return null;
                var interactable = thing.GetInteractable(hit.collider);
                return interactable != null ? interactable.Slot : null;
            }
            catch { return null; }
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
            _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: values re-sync after roundtrip
        }

        private void RefreshSatellite()
        {
            if (_satellite == null || _satellite.Provider == null) return;
            try { _satellite.Entries = _satellite.Provider() ?? _satellite.Entries; }
            catch { }
            _satellite.Page = Mathf.Clamp(_satellite.Page, 0, _satellite.PageCount - 1);
        }

        /// <summary>Rebuild the current level and satellite — for when OUTSIDE state the
        /// entries depend on changes while the radial is open (e.g. the Q/E hand switch:
        /// STOW previews, "Equip/Swap" verbs and insert targets all follow the active hand).</summary>
        public void RefreshAll()
        {
            if (!IsOpen) return;
            _press = null; // the rebuilt entries are new objects; a held press means nothing now
            Top().Refresh();
            RefreshSatellite();
        }

        // ---------- internals ----------

        private Level Top() => _stack[_stack.Count - 1];

        private RadialEntry MainEntry(int i)
        {
            var entries = Top().Visible;   // hover indices are page-relative
            return i >= 0 && i < entries.Count ? entries[i] : null;
        }

        private RadialEntry SatEntry(int i)
        {
            if (_satellite?.Entries == null) return null;
            var entries = _satellite.Visible;
            return i >= 0 && i < entries.Count ? entries[i] : null;
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
            // Nothing to show (a tool with no settings/options) — don't form an empty child
            // radial; the wedge just isn't swipeable (play-test: wrench/cutters).
            if (entries.Count == 0) return;
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

        /// <summary>The CLOSE band: the bottom 60° of the hub between half and full hub radius
        /// (ImGui screen coords are y-down, so "bottom" is angles around +90°).</summary>
        internal static bool InCloseButton(Vector2 deltaFromCenter, float innerR)
        {
            float hubR = innerR - 6f;
            float dist = deltaFromCenter.magnitude;
            // Outer bound matches the DRAWN band (0.94 * hubR) — never larger than the
            // visual, or clicks close from pixels that look like the wedge ring.
            if (dist < hubR * 0.52f || dist > hubR * 0.94f) return false;
            float ang = Mathf.Atan2(deltaFromCenter.y, deltaFromCenter.x);
            return ang > Mathf.PI / 3f && ang < Mathf.PI * 2f / 3f;
        }

        // ---------- drawing ----------

        public void Draw()
        {
            if (!IsOpen) return;

            var center = DrawUtil.ScreenCenter + _centerOffset;
            float outerR = UIAConfig.RadialOuterRadius.Value;
            _lastOuterR = outerR;
            // Hub floor of 104px: the six-line center readout needs that much vertical room.
            float innerR = Mathf.Clamp(UIAConfig.RadialInnerRadius.Value, 104f, Mathf.Max(104f, outerR - 30f));
            _lastInnerR = innerR;
            var mouse = DrawUtil.MousePos();

            // Search mode replaces the rings entirely (parked chips stay visible).
            if (_searchOpen)
            {
                _closeHovered = false;
                UI.UnityRadialView.Hide();
                UI.SearchPanelView.Render(center, innerR, outerR);
                UI.ParkedItemsView.Render(_parking, mouse);
                return;
            }
            UI.SearchPanelView.Hide();

            var dl = ImGui.GetForegroundDrawList();
            var level = Top();
            var visible = level.Visible;
            int count = visible.Count;

            // Paging indicator above whichever ring Q currently flips.
            string pageKeyName = UIAConfig.RadialPageKey != null
                ? UIAConfig.RadialPageKey.Value.ToString() : "Q";
            bool satPages = _satellite != null && _satellite.PageCount > 1;
            string pageText = satPages
                ? null // Q targets the satellite; the main ring shows no counter
                : level.PageCount > 1
                    ? (level.Page + 1) + "/" + level.PageCount + "  -  " + pageKeyName + ": next page"
                    : null;
            string satPageText = satPages
                ? (_satellite.Page + 1) + "/" + _satellite.PageCount + "  -  " + pageKeyName + ": next page"
                : null;

            // --- hover state: main ring ---
            // The wedge hover boundary and the hub's claim (drag zone + CLOSE band) must be
            // the SAME line (hubR), or there is an annulus where a wedge highlights while
            // the click routes to the hub — chip dumps from a click aimed at a wedge.
            var delta = mouse - center;
            _mainDist = delta.magnitude;
            float hubClaim = innerR - 6f;
            _hovered = _mainDist >= hubClaim ? SectorFromMouse(delta, count) : -1;
            _closeHovered = InCloseButton(delta, innerR);
            if (_closeHovered) _hovered = -1; // highlight and input may never disagree

            // --- hover state: satellite ring ---
            _satHovered = -1;
            if (_satellite != null)
            {
                var satDelta = mouse - _satellite.Center;
                _satDist = satDelta.magnitude;
                if (_satDist <= _satellite.OuterR + 24f)
                {
                    if (_satDist >= _satellite.InnerR * 0.85f)
                        _satHovered = SectorFromMouse(satDelta, _satellite.Visible.Count);
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
            // Never while dragging a chip: the hand is busy — a child radial popping up
            // mid-drag both steals the drop target and reads as noise (0.6.2 request).
            // Never while the mouse modifier (Alt) is held either: that key means "I'm
            // reaching into the world" (Z-grab / world-slot grab), so a child radial dwelling
            // open would fight the world interaction (resetting the candidate stops it firing
            // the instant the key is released).
            bool slideMouseMod = false;
            try { slideMouseMod = KeyManager.GetButton(KeyMap.MouseControl); } catch { }
            if (_satellite == null && _hovered >= 0 && _mainDist > outerR + 14f
                && _parking.Dragging == null && !slideMouseMod)
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
            else if (_satellite != null || _mainDist <= outerR + 14f || slideMouseMod)
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
                    center, innerR, outerR, visible,
                    _satellite == null ? _hovered : -1, level.Title,
                    _satellite?.Center, _satellite?.InnerR ?? 0f, _satellite?.OuterR ?? 0f,
                    _satellite?.Visible, _satHovered, _satellite?.Title,
                    readout, null, _sticky,
                    _parking.Dragging?.Item, _closeHovered,
                    pageText, satPageText);
                UI.ParkedItemsView.Render(_parking, mouse);
                return;
            }
            UI.UnityRadialView.Hide();
            UI.ParkedItemsView.Render(_parking, mouse);

            if (pageText != null)
                DrawUtil.TextShadowCentered(dl, center - new Vector2(0f, outerR + 22f), Theme.TextDim, pageText);
            if (satPageText != null && _satellite != null)
                DrawUtil.TextShadowCentered(dl, _satellite.Center - new Vector2(0f, _satellite.OuterR + 18f), Theme.TextDim, satPageText);

            // Legacy ImGui draw-list path (kept for A/B and as reference)
            DrawRing(dl, center, innerR, outerR, visible,
                _satellite == null ? _hovered : (_satellite != null ? _satellite.SourceIndex : -1),
                _satellite != null, solidHub: true);
            if (_satellite != null)
                DrawRing(dl, _satellite.Center, _satellite.InnerR, _satellite.OuterR, _satellite.Visible, _satHovered, false, solidHub: false);

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

            // ImGui fallback's stand-in for the hub CLOSE button (input works either way).
            Line(62f, _closeHovered ? Theme.Accent : Theme.TextDisabled, "- CLOSE -");

            RadialEntry hovered = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                                : _hovered >= 0 ? MainEntry(_hovered)
                                : null;
            if (hovered == null)
            {
                string hint = _sticky
                    ? (UIAConfig.IsB ? "MMB/LMB select | RMB back" : "LMB select | RMB back")
                    : (UIAConfig.IsB ? "hover to dive | release to cancel" : "release to cancel");
                Line(-12f, Theme.TextDisabled, hint);
                return;
            }

            string verb = hovered.ActionText ?? (hovered.IsBranch ? "Open" : "Select");
            Line(-14f, hovered.Enabled ? Theme.Accent : Theme.TextDisabled, verb);
            // Don't print the same word twice when the wedge IS its verb ("Replace").
            if (!string.Equals(hovered.Label, verb, StringComparison.OrdinalIgnoreCase))
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

