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
        private bool? _slideOutHasContent;             // memoized emptiness of the slide-out
        public object Tag;

        // --- Option A additions ---
        public string StateText;            // live state under the icon: "87%", "5,301 kPa", "x25"
        public string BindingLabel;         // 1B.3: grey tool-type label for a (possibly empty) belt slot wedge
        public Sprite HoverIcon;            // swapped in while hovered (STOW wedges preview the held item)
        public bool StowStyle;              // schema A stow wedge: neutral fill, orange only on hover
        public bool GroupStyle;             // sorting-class GROUP wedge: distinct edge/fill palette
        public bool DeviceSlotStyle;        // item installed in a device's functional slot: distinct edge
        public DynamicThing BindableBag;    // storage container this wedge represents — Ctrl+1..0 bindable (#3)
        public Action<int> OnScroll;        // scroll-wheel value adjust (+1 / -1 per notch)
        public Func<string> ValueText;      // live value between the scroll triangles
        public Core.ScannedSlot DragSource; // parking: where this item physically lives
        public Slot DropSlot;               // parking: dropping a dragged item goes to this slot
        public Func<DynamicThing, Slot> DropResolver; // parking: pick a slot for the dragged item (bags)

        // --- hotkey binding (#4): a device SETTING wedge can be bound to a key by hovering it
        // and pressing a letter. Populated by DeviceControls; null on non-setting wedges. ---
        public DynamicThing HotkeyThing;
        public Interactable HotkeyInteractable;
        public bool CanHotkey => HotkeyInteractable != null && HotkeyThing != null;

        public bool IsBranch => ChildProvider != null && OnSelect == null;
        public bool HasSlideOut => SlideOutProvider != null;

        /// <summary>Would a swipe on this wedge actually open a satellite? A slide-out
        /// provider that yields NOTHING (a tool with no settings/slots — wire cutters, a
        /// wrench) makes <see cref="HasSlideOut"/> true but <see cref="OpenSatellite"/>
        /// no-ops, so the swipe chevron must be gated on real content, not just a non-null
        /// provider. Evaluated once and cached — entries are rebuilt on every Refresh, so the
        /// snapshot can't go stale within one entry's lifetime; providers run lazily and this
        /// only fires for wedges that carry a slide-out.</summary>
        public bool SlideOutHasContent()
        {
            if (SlideOutProvider == null) return false;
            if (_slideOutHasContent.HasValue) return _slideOutHasContent.Value;
            bool has;
            try { var e = SlideOutProvider(); has = e != null && e.Count > 0; }
            catch { has = false; } // a throwing provider is treated as empty (OpenSatellite nulls it)
            _slideOutHasContent = has;
            return has;
        }

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
        /// <summary>The configured ceiling for wedges-per-page (F10 "Max wedges" slider).</summary>
        private static int ConfiguredMaxWedges
            => UIAConfig.RadialMaxWedges != null ? UIAConfig.RadialMaxWedges.Value : 14;

        // Live, session-only wedges-per-page override, set by scrolling over empty space / a
        // non-value wedge while a radial is open (see AdjustVisibleWedges + UpdateScroll). -1 =
        // "follow the configured max". A transient "focus this wheel down" gesture, NOT a saved
        // setting: Close() resets it to -1, so every fresh radial starts at the configured max and
        // no stale value survives a hot-reload.
        private static int _liveWedges = -1;

        /// <summary>Wedges per page. Crowded rings page with the Q key instead of a MORE
        /// wedge — pages are windows over the LIVE entry list, so refreshes stay fresh. Floored at
        /// 2 (the scroll-to-shrink minimum) and clamped to the configured max above.</summary>
        private static int MaxPerPage
        {
            get
            {
                int max = Mathf.Max(2, ConfiguredMaxWedges);
                int live = _liveWedges < 0 ? max : _liveWedges;
                return Mathf.Clamp(live, 2, max);
            }
        }

        /// <summary>#scroll-wedge-count: nudge how many wedges show per page, in [2, configured max].
        /// Scroll over empty space / the hub / a wedge with no value modifier calls this; scroll over
        /// a value wedge still adjusts that value (UpdateScroll). Session-only — reset on Close().</summary>
        public void AdjustVisibleWedges(int dir)
        {
            int max = Mathf.Max(2, ConfiguredMaxWedges);
            int cur = _liveWedges < 0 ? max : Mathf.Clamp(_liveWedges, 2, max);
            int next = Mathf.Clamp(cur + (dir > 0 ? 1 : -1), 2, max);
            if (next == cur) return;
            _liveWedges = next;
            // Clamp the open level's page to the new page count so the visible window is valid the
            // very next Draw (PageOf also clamps, but this keeps _level.Page honest for Q paging).
            var lvl = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
            if (lvl != null) lvl.Page = Mathf.Clamp(lvl.Page, 0, lvl.PageCount - 1);
        }

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
        private int _lastHovered = -1;      // R8: last-ticked main-ring hover (hover-tick de-dupe)
        private int _satHovered = -1;       // index on the satellite ring
        // The source wedge whose satellite the user just RMB-closed. Its satellite sits PAST the
        // ring's outer edge, so without this the slide-out dwell would instantly re-open it (RMB
        // would appear to do nothing). Suppress re-open until the cursor returns inside the ring or
        // moves onto a different wedge.
        private int _slideOutSuppressIndex = -1;
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

        // --- R3 double-tap repeat: remember the last committed action, keyed by the active
        // feature/root, so tapping the feature key twice re-runs it WITHOUT reopening the ring.
        // Persists across Close (the commit that populates it also closes the radial, so the
        // repeat necessarily happens after a close) — cleared only on hot-reload via
        // ResetRepeatCache(). ---
        private static System.Action _lastCommit;
        private static string _lastCommitKey;

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

        /// <summary>The entry under the cursor (satellite ring first, then main), or null — used
        /// by the controller for number-key bag binding (#3).</summary>
        public RadialEntry HoveredEntry()
        {
            if (_satellite != null && _satHovered >= 0) return SatEntry(_satHovered);
            if (_hovered >= 0) return MainEntry(_hovered);
            return null;
        }
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
            _lastHovered = -1;
            _satHovered = -1;
            _slideOutSuppressIndex = -1;
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
            _lastHovered = -1;
            _satHovered = -1;
            _slideOutSuppressIndex = -1;
            _parking.Clear();
            _press = null;
            _searchOpen = false;
            _centerOffset = Vector2.zero;
            _hubDragging = false;
            _closeHovered = false;
            _branchEnteredAt = -999f;
            _dwellIndex = -1;
            _liveWedges = -1;   // the scroll-to-focus wedge count is per-open; drop it on close
            HudDropCue.Clear(); // #4: no drag in flight once the radial is gone
            Core.WorldSlotCue.Hide(); // take the world-slot placement box down with the radial
            UI.SearchPanelView.Hide();
        }

        /// <summary>Hold-mode release. Returns true if the menu should stay open (went sticky).</summary>
        public bool OnHoldReleased()
        {
            if (!IsOpen) return false;
            // World-grab in hold mode: if a chip is on the cursor when the key is let go, DROP
            // it where the cursor is (bag / HUD box / world / ground) instead of running
            // whatever wedge happens to sit behind it. Parked chips (dropped on open screen)
            // likewise cancel to a close, never a wedge select.
            if (_parking.Dragging != null)
            {
                ResolveDragRelease(DrawUtil.MousePos());
                Close();
                return false;
            }
            if (_parking.Active)
            {
                Close();
                return false;
            }
            // Reaching into the world (Alt held) at release means "I'm done with the world",
            // not "select this wedge" — just close.
            bool worldReach = false;
            try { worldReach = KeyManager.GetButton(KeyMap.MouseControl); } catch { }
            if (worldReach)
            {
                Close();
                return false;
            }
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
                // The child radial is transient — RMB ignores it and acts on the MAIN radial
                // (as if the child were gone), mirroring sticky's RMB. At the root RMB does
                // nothing here: releasing MMB is the exit, and parking never runs in hold mode.
                if (_satellite != null) _slideOutSuppressIndex = _satellite.SourceIndex;
                _satellite = null;
                _satHovered = -1;
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
                // A child radial (satellite) is TRANSIENT — RMB ignores it and acts on the MAIN
                // radial as if the child were gone: drop the child, then go back one main level,
                // or close everything when already at the first main level. Suppress an instant
                // slide-out re-open on the wedge the cursor is sitting past.
                if (_satellite != null) _slideOutSuppressIndex = _satellite.SourceIndex;
                _satellite = null;
                _satHovered = -1;
                if (_stack.Count > 1)
                {
                    _stack.RemoveAt(_stack.Count - 1);
                    Top().Refresh(); // the re-exposed level may be stale (items moved since)
                    _hovered = -1;
                    return;
                }
                DumpChipsToGround(); // main first radial → close everything
                Close();
                return;
            }

            // Hub interactions (move the radial, the always-works CLOSE band) — shared by both
            // schemas AND by hold mode, so holding the key can move the radial too.
            if (UpdateHubDrag()) return;

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
                    // MMB is CLOSE-ONLY in radials (FlorpyDorp 2026-07-24). It must NEVER pick the wedge
                    // under the cursor: that silently equipped whatever tool you moused over, and the
                    // resulting pick -> close -> reopen loop is EXACTLY what blinked the cursor (each close
                    // hides the pointer, each reopen shows it — the "flicker"). Selection is LMB. Over the
                    // CLOSE band MMB still dumps parked chips to the ground (the deliberate-dump contract);
                    // anywhere else it just dismisses, and parked chips stay in their slots.
                    if (_closeHovered) DumpChipsToGround();
                    Close();
                    return;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                try { if (ImGui.GetIO().WantCaptureMouse) return; } catch { }

                // Drag layer first: Alt-grab a world item/slot, pick a parked chip back up, or
                // tear an item out of a visor HUD box. Any of these consumes the press.
                if (TryBeginDrag(mouse)) return;

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
                // A greyed (disabled) entry can still be DRAGGED out — only its click/activate is
                // blocked. Both hands full greys the "take to hand" affordance, but the item icon must
                // still be tearable onto the world, a bag, a device slot, etc. (FlorpyDorp 2026-07-23).
                // Capture the press when the entry is enabled OR carries a drag source; the release path
                // (below) refuses Execute for a still-disabled entry so a pure CLICK never fires the
                // into-hand action the greying was meant to forbid.
                if (entry == null) return;
                if (!entry.Enabled && !entry.CanDrag) return;
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

                // A disabled entry was press-captured ONLY so it could be dragged out. If the press
                // resolved as a plain click (no drag ever started, so we are still here with _parking
                // idle), do NOT run its action — greyed means "won't go into your hands". Give the
                // fail cue so the refusal is felt, matching the dimmed affordance.
                if (!entry.Enabled)
                {
                    try { UIAudioManager.Play(UIAudioManager.ActionFailHash); } catch { }
                    return;
                }

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

        /// <summary>The drag-layer GRAB, shared by sticky click-handling and HOLD mode: Alt-grab
        /// a free item or a world-slot occupant out of the physical world, pick a parked chip
        /// back up, or tear an item out of a visor HUD hand/equipment box. Each mints
        /// <c>_parking.Dragging</c> (and closes any satellite — the hand is busy) and returns
        /// true; returns false when nothing under the cursor is grabbable, so the caller can do
        /// its own wedge handling. World grabs are range-gated by the raycast at grab and again
        /// at drop.</summary>
        private bool TryBeginDrag(Vector2 mouse)
        {
            // Z-grab (the vanilla MouseControl key, default Alt): a free-lying world item.
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
                    return true;
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
                    return true;
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
                return true;
            }

            // Bugs 2 & 5: the visor HUD hand boxes and the six equipment boxes are drag
            // SOURCES too, not just drop targets — press one that holds an item to tear it
            // into the drag layer. Boxes live outside the rings, so this can never shadow a
            // wedge press. The chip is Expected-pinned like any other.
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
                return true;
            }
            return false;
        }

        /// <summary>Hub drag: grab the centre circle (off the CLOSE band, no satellite open) and
        /// hold LMB to move the whole radial; clicking the CLOSE band closes. Returns true when it
        /// owns the mouse this frame (dragging, just started, or closed), so the caller stops.
        /// Shared by sticky mode and hold mode.</summary>
        private bool UpdateHubDrag()
        {
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
                return true; // dragging the radial around owns the mouse
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
                        return true;
                    }
                    // Grabbing the hub (inside the ring, off the CLOSE band) moves the radial.
                    float hubR = Mathf.Max(0f, _lastInnerR - 6f);
                    if (_satellite == null && _mainDist < hubR)
                    {
                        _hubDragging = true;
                        _lastDragMouse = hubMouse;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Option A HOLD mode (holding the radial key — e.g. hold R): give the transient
        /// radial the SAME mouse interaction as the tapped (sticky) radial — move the radial (hub
        /// drag), drag an item off a wedge, Alt-grab items out of the physical world, drop onto
        /// wedges / HUD boxes / the ground, and click-to-select. Wedge selection ALSO still happens
        /// on key-release (the flick-release gesture — see <see cref="OnHoldReleased"/>). While a
        /// chip is on the cursor or Alt is held, Draw() withholds child radials and the ring
        /// highlight so the radial stays out of the way. Call every frame from the controller's
        /// non-sticky branch; self-gates to Option A (B hold mode dives branches on LMB).</summary>
        public void UpdateHoldInteractiveA()
        {
            if (!IsOpen || _sticky || _searchOpen) return;
            if (!UIAConfig.IsA || UIAConfig.IsB) return; // Option A only
            _parking.Prune();

            // Starting a mouse interaction while holding the key LATCHES the radial open
            // (sticky), so hold-<key> gains the full tap-<key> feature set — drag items off
            // wedges, move the hub, world-grab, drop, click-select. The hold radial is otherwise
            // transient (built for flick-release), and flick-release still works: releasing the
            // key WITHOUT ever clicking never promotes, so the key-up still selects the hovered
            // wedge. We process this same frame's press below, then the controller's sticky
            // branch drives every following frame.
            if (Input.GetMouseButtonDown(0))
                _sticky = true;

            if (UpdateHubDrag()) return;
            UpdateStickyOptionA();
        }

        /// <summary>Stack-merge a dragged stackable onto a wedge already showing a COMPATIBLE
        /// stack (the vanilla "drop 20 sheets onto 10" behaviour), through Thing.Merge —
        /// server-authoritative and gated by Slot.CanMerge. Returns true when a merge ran.
        /// The wedge's slot comes from its Tag (device/tool slots) or its DragSource (bag item
        /// wedges); dropping a stack back onto itself is a no-op.</summary>
        private bool TryStackMerge(RadialEntry target, DynamicThing item)
        {
            if (target == null || item == null) return false;
            Slot slot = (target.Tag as Slot) ?? target.DragSource?.Slot;
            DynamicThing occ = slot?.Get();
            if (occ == null || ReferenceEquals(occ, item)) return false;
            if (!(occ is IMergeable dst) || !(item is IMergeable src)) return false;
            try { if (!Slot.CanMerge(item, slot)) return false; } catch { return false; }
            return ItemActions.MergeInto(dst, src);
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

            // Stacking: dropping a stack onto a wedge that already shows a COMPATIBLE stack
            // merges them (drag 20 sheets onto 10 → 30) via vanilla's own Thing.Merge funnel,
            // gated by Slot.CanMerge. Checked before the swap/insert path — an item wedge is
            // otherwise not a drop target, so nothing happened before.
            if (target != null && TryStackMerge(target, item))
            {
                _satellite = null;
                Top().Refresh();
                _hovered = -1;
                _pendingRefreshAt = Time.unscaledTime + 0.6f;
                return;
            }

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
            // Scroll over a VALUE wedge (suit pressure, thrust, ...) adjusts that value. Scroll over
            // anything else — empty space, the hub, or a plain wedge with no value modifier — changes
            // how many wedges the ring shows: down toward 2, up toward the configured max.
            if (entry?.OnScroll == null) { AdjustVisibleWedges(s > 0f ? 1 : -1); return; }
            try { entry.OnScroll(s > 0f ? 1 : -1); }
            catch (Exception e) { UIALog.Warn("Scroll adjust failed: " + e.Message); }
            if (_satellite != null) RefreshSatellite();
            _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: values re-sync after roundtrip
        }

        /// <summary>#4: hover a device-setting wedge and press an allowed letter to bind that
        /// key to the setting. Same on-the-rings gate as scroll so a stray keypress off the
        /// wheel can't bind. Called every frame the radial is open.</summary>
        public void UpdateHotkeyCapture()
        {
            if (!IsOpen || _searchOpen) return;
            RadialEntry entry = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                              : _hovered >= 0 && _mainDist <= _lastOuterR * 1.2f ? MainEntry(_hovered)
                              : null;
            if (entry == null) return;
            // 1B.4 anti-hotbar guard (belt-and-suspenders): a tool-equip wedge carries neither a
            // BindableBag nor CanHotkey. It already can't bind (the CanHotkey gate below rejects
            // it), but reject it explicitly first so no future change can key-bind a tool into
            // the hand and grow a ten-slot hotbar — the one thing this UI must never become.
            if (entry.BindableBag == null && !entry.CanHotkey) return;
            if (!entry.CanHotkey) return;
            var k = Core.WedgeHotkeys.PressedBindableLetter();
            if (k != KeyCode.None)
            {
                // Same letter over the wedge it's already bound to → toggle the binding OFF.
                if (Core.WedgeHotkeys.IsBoundTo(k, entry.HotkeyInteractable))
                {
                    Core.WedgeHotkeys.Unbind(k);
                    UIALog.Debug("Hotkey " + k + " unbound from " + entry.Label);
                }
                else
                {
                    Core.WedgeHotkeys.Bind(k, entry.HotkeyInteractable, entry.HotkeyThing, entry.Label);
                    UIALog.Debug("Hotkey " + k + " bound to " + entry.Label);
                }
            }
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

        /// <summary>Like <see cref="RefreshAll"/>, but for an inventory MUTATION triggered from outside
        /// the wheel's own click path while it stays open — the G-key Smart Stow pass-through
        /// (<c>RadialController.UpdatePassthroughKeys</c>). Rebuilds now (single-player applies the move
        /// synchronously, so the belt/hand wedges re-read immediately) AND schedules the same
        /// post-roundtrip re-read every action path uses, so on an MP client the wedges refill once the
        /// server has applied the stow instead of freezing until the wheel is reopened.</summary>
        public void RefreshAfterExternalMutation()
        {
            if (!IsOpen) return;
            RefreshAll();
            _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: re-read after the server applies it
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

            // R3: cache the committed action under the active feature (the ROOT level title,
            // stable across branch dives) so a double-tap of the feature key can repeat it.
            if (entry.OnSelect != null)
            {
                _lastCommit = entry.OnSelect;
                _lastCommitKey = _stack.Count > 0 ? _stack[0].Title : entry.Label;
            }

            // R8: audible confirm on a successful commit, gated by the same toggle as the
            // hover tick. UIAudioManager.Play is a cheap pooled-source call.
            if (UIAConfig.RadialWedgeSounds != null && UIAConfig.RadialWedgeSounds.Value)
                UIAudioManager.Play(UIAudioManager.ClickMediumHash);
        }

        /// <summary>R3 double-tap repeat: re-invoke the last committed action if it belongs to
        /// <paramref name="featureKey"/> (the feature's root title). Returns true when it fired.
        /// Static so the controller can call it while no ring is open. Fail-soft — a stale
        /// action (its target destroyed) is swallowed and reported false.</summary>
        public static bool RepeatLast(string featureKey)
        {
            if (_lastCommit == null || string.IsNullOrEmpty(featureKey)) return false;
            if (!string.Equals(featureKey, _lastCommitKey, StringComparison.Ordinal)) return false;
            try { _lastCommit(); }
            catch (Exception e) { UIALog.Warn("Radial repeat-last failed: " + e.Message); return false; }
            return true;
        }

        /// <summary>Hot-reload teardown: drop the cached repeat action so a reloaded assembly
        /// never invokes a delegate into the dead one. Call from the controller's
        /// ShutdownImmediate()/CloseAll().</summary>
        public static void ResetRepeatCache()
        {
            _lastCommit = null;
            _lastCommitKey = null;
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
            float satScale = UIAConfig.RadialSatelliteScale != null ? UIAConfig.RadialSatelliteScale.Value : 1f;
            float outer = Mathf.Clamp(mainOuterR * 0.55f + Mathf.Max(0, entries.Count - 4) * 8f, 130f, 220f) * satScale;
            // 2a: the child readout's font tracks its hub radius (innerR/110 in ReadoutView), so a
            // tiny hub made the "Stow X into Y" lines shrink to unreadable and overflow. Floor the
            // hub radius, then grow `outer` in step so the larger hub doesn't crush the wedge band.
            float hubRatio = UIAConfig.RadialSatelliteHubRatio != null ? UIAConfig.RadialSatelliteHubRatio.Value : 0.34f;
            float innerR = Mathf.Max(outer * hubRatio, 76f * satScale);
            outer = Mathf.Max(outer, innerR + 66f); // keep a usable wedge band around the bigger hub
            var dir = new Vector2(Mathf.Cos(aMid), Mathf.Sin(aMid));
            _satellite = new SatelliteRing
            {
                Title = string.IsNullOrEmpty(entry.SlideOutLabel) ? entry.Label : entry.SlideOutLabel + ": " + entry.Label,
                Provider = entry.SlideOutProvider,
                Entries = entries,
                Center = mainCenter + dir * (mainOuterR + outer * 0.75f + 16f),
                OuterR = outer,
                InnerR = innerR,
                SourceIndex = sourceIndex,
            };
            _satHovered = -1;
        }

        /// <summary>R8: play a soft tick when the main-ring hover lands on a new ENABLED wedge.
        /// De-duped against <c>_lastHovered</c> so a resting cursor is silent; gated by the
        /// RadialWedgeSounds toggle. Called once per Draw after hover resolution.</summary>
        private void HoverTick()
        {
            if (_hovered == _lastHovered) return;
            _lastHovered = _hovered;
            if (_hovered < 0) return;
            if (UIAConfig.RadialWedgeSounds == null || !UIAConfig.RadialWedgeSounds.Value) return;
            var e = MainEntry(_hovered);
            if (e != null && e.Enabled)
                UIAudioManager.Play(UIAudioManager.HoverLightHash);
        }

        /// <summary>Which wedge index a mouse delta from the ring centre points at (-1 if the
        /// ring is empty). Exposed to <c>RadialController</c> for flick-commit (R2), which resolves
        /// a sector without ever drawing the ring.</summary>
        internal static int SectorFromMouse(Vector2 delta, int count)
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

            // #4: publish the dragged item so the visor HUD can light up a hand / equipment box
            // the cursor is over (cleared in Close()).
            HudDropCue.Dragging = _parking.Dragging?.Item;

            // Drive vanilla's world-slot placement box (green/yellow/blue/red) for a chip dragged over a
            // charger / locker / device slot — vanilla's own version is frozen while we hold the cursor
            // block. A no-op when not dragging or not over a world slot; taken down again in Close().
            Core.WorldSlotCue.Tick(_parking.Dragging?.Item);

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

            // The world-reach modifier (vanilla MouseControl, default Alt): while it's held the
            // player is reaching into the physical world (Z-grab / world-slot grab), so the ring
            // goes passive — see the highlight/satellite suppression just below.
            bool altReach = false;
            try { altReach = KeyManager.GetButton(KeyMap.MouseControl); } catch { }

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

            // Reaching into the world (Alt held) makes the ring passive — no wedge highlight to
            // fight the world grab, and nothing to "switch to" while you aim at the floor. A drag
            // already in progress keeps its highlight: that IS the drop preview (a slot/box lights
            // up when it will take the carried item).
            if (altReach && _parking.Dragging == null) { _hovered = -1; _closeHovered = false; }

            // --- hover state: satellite ring ---
            _satHovered = -1;
            // Pressing the world-reach modifier dismisses any open child radial at once — the
            // hand is reaching into the world and a satellite would fight the grab (user request;
            // a grab in progress already nulled it when the chip was minted).
            if (_satellite != null && altReach) _satellite = null;
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

            // R8: hover tick. _hovered is now final for the frame; a fresh landing on an
            // ENABLED wedge ticks once. De-duped so a static hover never repeats the sound.
            HoverTick();

            // --- slide-out trigger (dwell-gated so fast flick-releases aren't hijacked) ---
            // Never while dragging a chip: the hand is busy — a child radial popping up
            // mid-drag both steals the drop target and reads as noise (0.6.2 request).
            // Never while the mouse modifier (Alt) is held either: that key means "I'm
            // reaching into the world" (Z-grab / world-slot grab), so a child radial dwelling
            // open would fight the world interaction (resetting the candidate stops it firing
            // the instant the key is released).
            // Drop the RMB-close suppression once the cursor comes back inside the ring or moves
            // onto a different wedge (so a later swipe on that same wedge can still open it).
            if (_mainDist <= outerR + 14f || (_hovered >= 0 && _hovered != _slideOutSuppressIndex))
                _slideOutSuppressIndex = -1;

            if (_satellite == null && _hovered >= 0 && _hovered != _slideOutSuppressIndex
                && _mainDist > outerR + 14f && _parking.Dragging == null && !altReach)
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
            else if (_satellite != null || _mainDist <= outerR + 14f || altReach)
            {
                _slideOutCandidate = -1;
            }

            // The interaction model (hover, satellites, levels, sticky/hold logic) lives in RadialMenu.
            // Unity UGUI (procedural) is the only renderer — the legacy ImGui draw-list painter
            // was removed in 0.9.2.5 (FlorpyDorp: "delete the renderer, keep the settings editor").
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
        }
    }
}

