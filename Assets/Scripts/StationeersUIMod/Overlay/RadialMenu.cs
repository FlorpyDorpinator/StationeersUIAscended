using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects;
using ImGuiNET;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Tutorial;
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

        // --- D-022 / D-023 presentation hints (read-only; set by builders, drawn by UnityRadialView) ---
        /// <summary>D-022: explicit one-word action for the curved word over the ring's top ("Take",
        /// "Open"). Null = derived by <see cref="ClickVerb"/> from <see cref="ActionText"/> and
        /// branch-ness. Set it when ActionText is a DESCRIPTION rather than a verb — the recent-item
        /// wedge reads "Recent item" in the hub, but its click takes the item.</summary>
        public string Verb;
        /// <summary>D-023: a small PERSISTENT tag drawn inside the wedge along its outer rim
        /// ("Recent item") so a wedge whose role isn't obvious from its icon says what it is.
        /// Null = none. Display text: runs through the radial's ALL-CAPS option, ASCII only.</summary>
        public string CornerTag;

        /// <summary>
        /// D-022 — the short verb for what a CLICK (or a hold-release) on this wedge will actually
        /// do, for the curved action word over the ring. Derived from the same fields the click path
        /// reads (<c>RadialMenu.SelectSticky</c> / <c>OnHoldReleased</c>): a disabled wedge does
        /// nothing, so it says nothing (null beats a false promise); a pure branch OPENS its level;
        /// anything else is the builder's ActionText shortened to its verb. Take-family actions run
        /// <c>ItemActions.EquipToActiveHand</c>, which SWAPS when the active hand is busy, so the
        /// word follows the hand — D-021's complaint was exactly a "select" that actually swapped.
        /// <para>Read-only: it reads networked slot state and the static <c>Slot.Allow*</c> gates,
        /// never mutates. Returns string literals only (no per-frame allocation). Null when the
        /// action has no honest one-word name.</para>
        /// </summary>
        public string ClickVerb()
        {
            if (!Enabled) return null;
            if (!string.IsNullOrEmpty(Verb))
                return IsWord(Verb, "take") ? TakeOrSwap("Take")
                     : IsWord(Verb, "equip") ? TakeOrSwap("Equip")
                     : Verb;
            if (IsBranch) return "Open";
            string a = ActionText;
            if (string.IsNullOrEmpty(a)) return null;

            if (IsWord(a, "take"))
            {
                if (a.StartsWith("Take 1", StringComparison.OrdinalIgnoreCase)) return "Take 1";       // split one
                if (a.StartsWith("Take half", StringComparison.OrdinalIgnoreCase)) return "Take half"; // split half
                return TakeOrSwap("Take");
            }
            if (IsWord(a, "grab")) return TakeOrSwap("Take");     // the recent-item wedge ("Grab another")
            // Toolbelt tool wedges ("Equip" / "Swap into hand" — the builder's hand state at BUILD
            // time) also run EquipToActiveHand, so follow the hand LIVE rather than the snapshot.
            if (IsWord(a, "equip") || string.Equals(a, "Swap into hand", StringComparison.OrdinalIgnoreCase))
                return TakeOrSwap("Equip");
            if (IsWord(a, "swap")) return "Swap";                 // "Swap in" (a candidate) / "Swap belt"
            if (IsWord(a, "turn"))
                return a.EndsWith("off", StringComparison.OrdinalIgnoreCase) ? "Turn off" : "Turn on";
            if (IsWord(a, "scroll")) return "Adjust";             // a value wedge: a bare click nudges it up
            if (IsWord(a, "enter")) return "Open";
            if (IsWord(a, "next")) return "Next mode";
            for (int i = 0; i < SimpleVerbs.Length; i++)
                if (IsWord(a, SimpleVerbs[i])) return SimpleVerbWords[i];

            // A device control whose ActionText is vanilla's state-baked label ("Stabilizer On",
            // "A/C Off"): the click presses that button, i.e. flips the named state.
            if (CanHotkey)
                return a.EndsWith(" On", StringComparison.OrdinalIgnoreCase)
                    || a.EndsWith(" Off", StringComparison.OrdinalIgnoreCase) ? "Toggle" : "Use";
            return null;
        }

        // ActionText first words that already ARE the verb (lower-case match -> display word).
        private static readonly string[] SimpleVerbs =
        {
            "stow", "insert", "install", "eject", "open", "close", "search", "sort", "split",
            "unpack", "replace", "wear", "activate", "deactivate", "lock", "unlock", "use", "drop",
        };
        private static readonly string[] SimpleVerbWords =
        {
            "Stow", "Insert", "Install", "Eject", "Open", "Close", "Search", "Sort", "Split",
            "Unpack", "Replace", "Wear", "Activate", "Deactivate", "Lock", "Unlock", "Use", "Drop",
        };

        /// <summary>True when <paramref name="text"/> begins with the whole word
        /// <paramref name="word"/> (case-insensitive). No allocation.</summary>
        private static bool IsWord(string text, string word)
        {
            if (text == null || text.Length < word.Length) return false;
            if (string.Compare(text, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            return text.Length == word.Length || text[word.Length] == ' ';
        }

        /// <summary>Is this a take-to-hand phrase ("Take to hand", "Grab another", "Equip") that
        /// <see cref="ClickVerb"/> may re-word to a swap? Used by the hub readout so its verb line
        /// agrees with the curved action word. (The split "Take 1 / Take half" phrases run SplitStack,
        /// not the take ladder, so they never swap.)</summary>
        public bool IsTakePhrase
        {
            get
            {
                string a = ActionText;
                if (a == null) return false;
                if (IsWord(a, "grab") || IsWord(a, "equip")) return true;
                return IsWord(a, "take")
                    && !a.StartsWith("Take 1", StringComparison.OrdinalIgnoreCase)
                    && !a.StartsWith("Take half", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>The verb a take-family click earns RIGHT NOW. A read-only mirror of
        /// <c>ItemActions.EquipToActiveHand</c>'s ladder (keep the two in step): empty active hand =
        /// a plain take; a part of the HELD tool lands in the OTHER hand (a take) when that hand is
        /// free and accepts it; a SEALED source (<c>ItemActions.IsSealedSlot</c>) may only be
        /// emptied, never refilled, so the click runs <c>TakeToFreeHand</c> — the other hand when it
        /// is free and accepts the item, else a fail, NEVER a swap (A12a); otherwise the held item
        /// swaps into the source slot when <c>Slot.AllowSwap</c> says so; anything else fails at
        /// click time, so no word.</summary>
        private string TakeOrSwap(string takeWord)
        {
            try
            {
                Slot hand = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot;
                DynamicThing handOcc = hand?.Get();
                if (handOcc == null) return takeWord;
                Slot src = DragSource?.Slot;
                // No exposed source (the recent-item wedge keeps its pinned source inside its click
                // closure): assume the usual busy-hand outcome — the one case this mirror cannot
                // follow down the part-of-the-held-tool / sealed rungs.
                if (src == null) return "Swap";
                if (IsInsideThing(src, handOcc))
                    return OtherHandTakes(hand, src) ? takeWord : null;
                if (ItemActions.IsSealedSlot(src))
                    return !src.IsLocked && OtherHandTakes(hand, src) ? takeWord : null;
                return Slot.AllowSwap(src, hand) ? "Swap" : null;
            }
            catch { return takeWord; }
        }

        /// <summary>Would <paramref name="src"/>'s occupant land in the hand that is NOT
        /// <paramref name="activeHand"/> — free, not the source itself, and accepting it? The shared
        /// rung of EquipToActiveHand's part-of-the-held-tool branch and TakeToFreeHand (whose active-
        /// hand rung can't apply here: the active hand is busy whenever this is asked).</summary>
        private static bool OtherHandTakes(Slot activeHand, Slot src)
        {
            var human = Assets.Scripts.Inventory.InventoryManager.ParentHuman;
            Slot other = human == null ? null
                : activeHand == human.LeftHandSlot ? human.RightHandSlot : human.LeftHandSlot;
            DynamicThing item = src.Get();
            return other != null && other != src && other.Get() == null && item != null
                && Slot.AllowMove(item, other);
        }

        /// <summary>Mirror of <c>ItemActions.IsInsideThing</c> (private there): is the slot somewhere
        /// inside <paramref name="root"/>'s contents tree?</summary>
        private static bool IsInsideThing(Slot slot, Thing root)
        {
            Thing parent = slot?.Parent;
            int depth = 0;
            while (parent != null && depth++ < 8)
            {
                if (parent == root) return true;
                parent = (parent as DynamicThing)?.ParentSlot?.Parent;
            }
            return false;
        }

        /// <summary>A6: can a click on this wedge move the ACTIVE HAND's item? Every take-family
        /// action can (a busy hand SWAPS its item into the source slot) and every stow-from-hand
        /// wedge does. Read by <c>RadialMenu</c>'s execute-time un-park; deliberately generous — an
        /// over-match only means a parked held item is not dropped when the wheel closes.</summary>
        internal bool MayMoveHeldItem
            => DragSource != null || Tag is Slot || StowStyle || FillOverride.HasValue || IsTakePhrase
               || (Verb != null && (IsWord(Verb, "take") || IsWord(Verb, "equip")));

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

        /// <summary>Resolve where a dragged thing would land on this wedge (null = doesn't fit).
        /// An OCCUPIED DropSlot is a valid target when vanilla's own drop ladder says so —
        /// swap/merge/insert, exactly what dropping the item on the physical slot would do
        /// (the play-test repro: a ground canister dragged onto the suit's canister wedge must
        /// SWAP, not dead-end). The executor re-gates everything at execute time
        /// (ItemActions.WorldDragTo / SwapIntoSlot), so this only answers "could it land".</summary>
        public Slot ResolveDrop(DynamicThing dragged)
        {
            if (dragged == null) return null;
            try
            {
                if (DropSlot != null)
                {
                    var occ = DropSlot.Get();
                    if (occ == null)
                        return Slot.AllowMove(dragged, DropSlot) ? DropSlot : null;
                    if (ReferenceEquals(occ, dragged))
                        return DropSlot; // "changed my mind" — the executors no-op this cleanly
                    if (dragged.ParentSlot == null)
                    {
                        // World-sourced: ask vanilla's ladder directly (same call the world-slot
                        // placement cue uses, so the highlight and the executor always agree).
                        switch (Assets.Scripts.UI.InputMouse.IsValid(dragged, DropSlot))
                        {
                            case Assets.Scripts.UI.DragResult.Swap:
                            case Assets.Scripts.UI.DragResult.Valid:
                            case Assets.Scripts.UI.DragResult.Merge:
                            case Assets.Scripts.UI.DragResult.Insert:
                                return DropSlot;
                            default:
                                return null;
                        }
                    }
                    // Slot-sourced: SwapIntoSlot's occupied rung is AllowSwap; merge is handled
                    // upstream by TryStackMerge, but CanMerge here keeps the highlight honest.
                    return Slot.AllowSwap(dragged.ParentSlot, DropSlot)
                        || Slot.CanMerge(dragged, DropSlot) ? DropSlot : null;
                }
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
        // Tutorial hook (Build Contract s3/s4): the last entry WedgeHovered fired for, across EITHER
        // ring, so switching rings (main <-> satellite) or hovering a different entry both count as a
        // change, but resting on the same one never re-fires. Kept separate from _lastHovered (which
        // only tracks the main ring's index, for the hover-tick sound).
        private RadialEntry _lastHoverSignalEntry;
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
        /// <summary>The drag-out parking state. <c>internal</c> (not private) ONLY so the uiatest
        /// harness (Testing/UiaTestHarness.cs) can stage the exact chip states the drag layer mints
        /// without synthesizing mouse input — nothing else touches it from outside this class.</summary>
        internal readonly ParkingState _parking = new ParkingState();
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

        /// <summary>How far past the main ring's outer radius the pointer still counts as ON the
        /// ring (a wedge is the click / drop / scroll target). The radial's own hit-test — every
        /// "is the pointer on the rings" check uses it, and so does the D-008 world-slot mask, so
        /// the mask can never disagree with where a release actually resolves.</summary>
        private const float RingReach = 1.2f;
        /// <summary>The satellite's hover reach past its outer radius (see the Draw hover pass).</summary>
        private const float SatReachPx = 24f;
        /// <summary>The PARKING line: a drag released more than this many px outside every ring
        /// parks as a chip on open screen (SearchPanelView uses the same 30 px).</summary>
        private const float ParkMarginPx = 30f;

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
        /// <summary>A5: an item is on the cursor right now (the drag layer's in-flight chip). RMB
        /// then cancels just that drag, so an owner-side RMB gesture must yield to it.</summary>
        public bool IsDragging => _parking.Dragging != null;

        /// <summary>B15b / D-064: set by the owner (RadialController) while RMB at this menu's ROOT
        /// does not close but RETURNS to the ring the menu replaced — the Q belt-picker going back to
        /// its belt ring. Presentation only: it makes the published <see cref="RadialHintContext.CanGoBack"/>
        /// honest (the strip reads "RMB back"); the return itself is the owner's own RMB handling.
        /// That return REPLACES the menu, so parked items drop on it like any close (D-004) — also
        /// published. Reset by every <see cref="Open"/> / <see cref="Close"/>, so it can never outlive
        /// the menu it was set for; the owner re-asserts it each frame. Instance state: nothing static
        /// survives a hot reload.</summary>
        public bool RmbReturnsFromRoot { get; set; }

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

        /// <summary>Should the radial stay open after the action just picked? The ONE place the
        /// Shift modifier's meaning is decided (all three action-commit sites consult this).
        /// Default (<see cref="UIAConfig.RadialShiftKeepsOpen"/> = true): closes after one action,
        /// held Shift keeps it open. Inverted (config false, a play-tester request 2026-08-06):
        /// stays open after actions, held Shift closes it after this one. The identity
        /// <c>ShiftHeld == config</c> encodes both directions in one expression. Fail-soft to the
        /// default semantics before the config binds (hot reload).</summary>
        internal static bool KeepOpenAfterAction
        {
            get
            {
                bool shiftMeansKeepOpen = true;
                try { if (UIAConfig.RadialShiftKeepsOpen != null) shiftMeansKeepOpen = UIAConfig.RadialShiftKeepsOpen.Value; }
                catch { }
                return ShiftHeld == shiftMeansKeepOpen;
            }
        }

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
            // D-004: opening a radial while one is already up (Tab toolbelt<->backpack swap, 1-6
            // equipment jump, Ctrl+number bag, Q belt-picker) REPLACES the old menu — that is a close
            // route like any other, so the old menu's dragged-out items drop first. A fresh open has
            // nothing held, so this is a no-op there.
            ReleaseHeldItems();
            HudDropCue.Clear();          // no drag survives into the new menu (D-008: no stuck cue)
            Core.WorldSlotCue.Hide();
            RadialHintContext.ResetInteraction();
            RmbReturnsFromRoot = false;  // B15b: the owner re-asserts it for the menu it just opened

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

        /// <summary>
        /// Close the radial. D-004 (FlorpyDorp 2026-09-25, "I don't like the spaghetti bugs"): EVERY
        /// close route funnels through here and therefore through <see cref="ReleaseHeldItems"/> —
        /// items dragged out of the radial onto the screen DROP to the ground no matter how the menu
        /// goes away: RMB out of the root, the key that opened it, Tab / 1-6 / Ctrl+number / Q
        /// (via <see cref="Open"/>), Escape, MMB, the hub CLOSE band, hold-release, and every
        /// programmatic <c>RadialController.CloseAll</c> (guards, stand-downs, the radial editor,
        /// hot reload). Before this, only RMB-at-the-root dumped and every other route silently
        /// cancelled — the D-004 report (a worn helmet dragged out, then the opener key: nothing).
        /// What drops is only what the local player CARRIES (A1): a chip grabbed out of a world
        /// container cancels on every route.
        /// </summary>
        public void Close()
        {
            ReleaseHeldItems();          // A2: never throws, and always leaves the parking state clean
            RadialHintContext.ResetInteraction();
            RmbReturnsFromRoot = false;  // B15b: never survives a close
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
            // likewise end in a close, never a wedge select — and Close() drops them (D-004).
            if (_parking.Dragging != null)
            {
                ResolveDragRelease(DrawUtil.MousePos());
                Close();
                return false;
            }
            // B4: THE release resolution — shared with the D-022 word / D-021 strip preview in Draw,
            // so what the hint promises and what letting go does can never disagree.
            RadialEntry entry = HoldReleaseEntry();
            if (entry == null)
            {
                Close();
                return false;
            }
            Execute(entry);
            if (ConsumeSearchRequest())
            {
                _sticky = true;
                _searchOpen = true;
                UI.SearchPanelView.Begin();
                return true;
            }
            if (KeepOpenAfterAction)
            {
                // "Keep it open, I'm not done" — the radial goes sticky. (Shift held by default;
                // the inverted option makes staying open the default and Shift the closer.)
                TutorialSignals.Raise(TSignal.KeepOpenUsed);
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

        /// <summary>
        /// B4 — the entry a HOLD-mode key release would EXECUTE right now, or null when letting go
        /// just closes. The one resolution <see cref="OnHoldReleased"/> runs (after its drag-release
        /// case) and the one <see cref="Draw"/> previews, so the curved action word and the "Release
        /// confirm / close" hint always name what the release actually does. Read-only.
        /// <list type="bullet">
        /// <item>Parked chips, the world-reach modifier (Alt: "I'm done with the world"), the hub
        /// CLOSE button, and the grace window right after diving into a branch (gesture momentum —
        /// the cursor sits over whatever wedge happens to be where the branch was) all close.</item>
        /// <item>A child ring's hovered wedge is the target once the ring has been open long enough to
        /// be deliberate; before that — or with nothing hovered on it — the release means the child
        /// ring's SOURCE wedge (a fresh satellite never steals a fast flick-release).</item>
        /// <item>A disabled wedge and a pure branch close: hold mode is strictly transient ("closes
        /// when you let go"); diving is LMB / dwell WHILE held, tap the key for sticky mode.</item>
        /// </list>
        /// </summary>
        private RadialEntry HoldReleaseEntry()
        {
            if (_parking.Active) return null;
            bool worldReach = false;
            try { worldReach = KeyManager.GetButton(KeyMap.MouseControl); } catch { }
            if (worldReach || _closeHovered) return null;
            if (Time.unscaledTime - _branchEnteredAt < BranchGraceSec) return null;
            RadialEntry entry;
            if (_satellite != null && _satHovered >= 0 && Time.unscaledTime - _satOpenedAt >= SatGraceSec)
                entry = SatEntry(_satHovered);
            else if (_satellite != null)
                entry = MainEntry(_satellite.SourceIndex);
            else
                entry = MainEntry(_hovered);
            if (entry == null || !entry.Enabled || entry.IsBranch) return null;
            return entry;
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
                    TutorialSignals.Raise(TSignal.BackedOut);
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
            else if (_hovered >= 0 && _mainDist <= _lastOuterR * RingReach)
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
            if (KeepOpenAfterAction)
            {
                // "Keep it open, I'm not done" — stay transient, just refreshed.
                TutorialSignals.Raise(TSignal.KeepOpenUsed);
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
                Close(); // D-004: a close like any other — dragged-out items drop (Close funnels it)
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                _press = null; // whatever was pressed no longer means what it meant
                // RMB mid-drag is NOT a close: it cancels just this drag (the item goes back where
                // it was — it never left its slot), and the radial stays open.
                if (_parking.Dragging != null)
                {
                    _parking.Dragging = null;
                    TutorialSignals.Raise(TSignal.ChipsCancelled);
                    return;
                }
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
                    TutorialSignals.Raise(TSignal.BackedOut);
                    return;
                }
                Close(); // main first radial → close everything (Close drops parked items, D-004)
                return;
            }

            // Hub interactions (move the radial, the always-works CLOSE band) — shared with
            // hold mode too, so holding the key can move the radial.
            if (UpdateHubDrag()) return;

            // (The classic schema's mouse-DOWN click model — click executes immediately, no
            // press-drag — went with the schema chooser in the post-0.9.2.5 play-test round. UpdateStickyOptionA is the
            // one click model: actions run on mouse-UP so a press can become a drag instead.)
            UpdateStickyOptionA();
        }

        /// <summary>
        /// The click model: actions run on mouse-UP so that press-and-hold (or press-and-move)
        /// on an item wedge can become a DRAG instead. A successful action closes the radial —
        /// unless parking is in progress, which locks it open.
        /// </summary>
        private void UpdateStickyOptionA()
        {
            var mouse = DrawUtil.MousePos();

            // MMB in a sticky radial: the whole gesture is "tap to open, flick, tap to pick",
            // and MMB is the dismiss half of it. Selection is LMB.
            if (Input.GetMouseButtonDown(2)
                && _parking.Dragging == null && _press == null)
            {
                bool imguiOwnsM = false;
                try { imguiOwnsM = ImGui.GetIO().WantCaptureMouse; } catch { }
                if (!imguiOwnsM)
                {
                    // MMB is CLOSE-ONLY in radials (FlorpyDorp 2026-07-24). It must NEVER pick the wedge
                    // under the cursor: that silently equipped whatever tool you moused over, and the
                    // resulting pick -> close -> reopen loop is EXACTLY what blinked the cursor (each close
                    // hides the pointer, each reopen shows it — the "flicker"). Selection is LMB. Wherever
                    // it is pressed it is a close, so parked chips drop (D-004 — Close funnels it; the old
                    // "only over the CLOSE band" split is gone).
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
                else if (_hovered >= 0 && _mainDist <= _lastOuterR * RingReach)
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
                                        : _hovered >= 0 && _mainDist <= _lastOuterR * RingReach ? MainEntry(_hovered)
                                        : null;
                if (!ReferenceEquals(entry, underCursor)) return;

                // A disabled entry was press-captured ONLY so it could be dragged out. If the press
                // resolved as a plain click (no drag ever started, so we are still here with _parking
                // idle), do NOT run its action — greyed means "won't go into your hands". Give the
                // fail cue so the refusal is felt, matching the dimmed affordance.
                if (!entry.Enabled)
                {
                    try { UIAudioManager.Play(UIAudioManager.ActionFailHash); } catch { }
                    TutorialSignals.Raise(TSignal.GreyClicked);
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
            if (!_parking.Active && !KeepOpenAfterAction)
            {
                Close(); // one action, radial goes away
                return;
            }
            // Tutorial hook: only when the Shift/keep-open MECHANIC is why it stayed open — parked
            // chips alone (KeepOpenAfterAction false) are a different reason and must not count.
            if (KeepOpenAfterAction) TutorialSignals.Raise(TSignal.KeepOpenUsed);
            // Parking locks the radial open; KeepOpenAfterAction means "I'm not done yet".
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
                    TutorialSignals.Raise(TSignal.WorldReachGrab);
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
                    TutorialSignals.Raise(TSignal.WorldReachGrab);
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
        /// Reachable in STICKY mode only (UpdateHoldB never calls it).</summary>
        private bool UpdateHubDrag()
        {
            var hubMouse = DrawUtil.MousePos();
            if (_hubDragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    _hubDragging = false;
                    // Tutorial hook: fires on release, not gated on distance moved — a plain click on
                    // the hub (no real move) also counts, which is a harmless over-match.
                    TutorialSignals.Raise(TSignal.HubMoved);
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
                        // The always-works exit. Parked items drop — every close does (D-004).
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

        /// <summary>HOLD mode with the pre-Hub (classic Option A) mouse interaction: give the
        /// transient radial the SAME mouse interaction as the tapped (sticky) radial — move the
        /// radial (hub drag), drag an item off a wedge, Alt-grab items out of the physical world,
        /// drop onto wedges / HUD boxes / the ground, and click-to-select. Wedge selection ALSO
        /// still happens on key-release (see <see cref="OnHoldReleased"/>).
        /// <para>DORMANT since the post-0.9.2.5 play-test round: The Hub is the only schema now, and its hold mode is
        /// strictly transient — LMB DIVES into branches (<see cref="UpdateHoldB"/>) rather than
        /// latching the ring sticky, so this whole body is unreachable. Kept intact (rather than
        /// deleted) because it is a complete, working interaction surface we may want back as an
        /// opt-in "drag while holding" mode; flip the guard below to revive it.</para></summary>
        public void UpdateHoldInteractiveA()
        {
            if (!IsOpen || _sticky || _searchOpen) return;
            if (UIAConfig.IsB) return; // always true since the post-0.9.2.5 play-test round — see the note above
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
                               : _hovered >= 0 && _mainDist <= _lastOuterR * RingReach ? MainEntry(_hovered)
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
                TutorialSignals.Raise(TSignal.ChipDroppedOnTarget);
                return;
            }

            if (target != null && target.AcceptsDrop)
            {
                Slot dest = target.ResolveDrop(item);
                // World chips run vanilla's FULL drop ladder (insert -> merge -> swap-the-
                // occupant-out -> move) via WorldDragTo — MoveWorldItemToSlot is empty-slot-only
                // by contract and dead-ended the "swap a ground canister into the suit" drop.
                bool moved = dest != null && (chip.IsWorld
                    ? ItemActions.WorldDragTo(chip.WorldSource, dest)
                    : ItemActions.SwapIntoSlot(chip.Source, dest));
                if (moved)
                {
                    _parking.RemoveBySlot(dest); // A6: a parked occupant just got swapped out of dest
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    // Bug 1: on an MP client the slot is still empty this frame (the move is a
                    // server round-trip); a second refresh once it lands makes the item appear
                    // in the wedge without reopening the radial. Matches the scroll/select paths.
                    _pendingRefreshAt = Time.unscaledTime + 0.6f;
                    TutorialSignals.Raise(TSignal.ChipDroppedOnTarget);
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
            // D-008: only once the pointer is OUT of the radial. A world slot that merely sits
            // BEHIND the radial (seen through the hub or a translucent wedge) is masked — the
            // same mask that keeps its green placement box from lighting up (Draw), so the
            // highlight and this executor can never disagree.
            if (!PointerInRadialMask() && TryDropOnWorldSlot(chip, item))
                return;

            bool outsideRings = BeyondParkingLine(_mainDist, _satDist);
            if (outsideRings && _parking.Chips.Count < ParkingState.MaxChips)
            {
                if (chip.IsWorld) _parking.RemoveByWorldThing(chip.WorldSource);
                else _parking.RemoveBySlot(chip.Source.Slot); // one source = one chip, always
                chip.Pos = mouse;
                _parking.Chips.Add(chip);
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
                TutorialSignals.Raise(TSignal.ChipParked);
            }
            else
            {
                // released over dead space inside the rings, or the screen is full — cancel.
                TutorialSignals.Raise(TSignal.ChipsCancelled);
            }
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
                // WorldDragTo, not MoveWorldItemToSlot: an occupied HUD hand/equipment box must
                // swap/merge exactly like vanilla, matching the wedge drop path.
                bool moved = chip.IsWorld
                    ? ItemActions.WorldDragTo(chip.WorldSource, zone.Slot)
                    : ItemActions.SwapIntoSlot(chip.Source, zone.Slot);
                if (moved)
                {
                    _parking.RemoveBySlot(zone.Slot); // A6: a parked occupant just got swapped out
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: box refills post-roundtrip
                    TutorialSignals.Raise(TSignal.ChipDroppedOnTarget);
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
                // WorldDragTo, not MoveWorldItemToSlot: WorldSlotCue already shows vanilla's
                // GREEN swap cue over an occupied world slot — the executor must deliver it.
                bool moved = chip.IsWorld
                    ? ItemActions.WorldDragTo(chip.WorldSource, worldSlot)
                    : ItemActions.SwapIntoSlot(chip.Source, worldSlot);
                if (moved)
                {
                    _parking.RemoveBySlot(worldSlot); // A6: a parked occupant just got swapped out
                    _satellite = null;
                    Top().Refresh();
                    _hovered = -1;
                    _pendingRefreshAt = Time.unscaledTime + 0.6f;
                    TutorialSignals.Raise(TSignal.ChipDroppedOnTarget);
                }
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("World slot drop failed: " + e.Message);
            }
            return false;
        }

        /// <summary>
        /// D-004 — THE close-with-held-item handler: the one place an item held by this radial's
        /// drag layer is resolved when the menu goes away. Called ONLY from <see cref="Close"/> and
        /// <see cref="Open"/> (a menu replaced by another), so every close route shares it.
        /// <list type="number">
        /// <item>A drag still IN FLIGHT counts as "dragged out onto the screen" only where a release
        /// could only have PARKED it (A9, <see cref="InFlightJoinsDrop"/>): past the parking line, off
        /// the radial's own footprint, NOT over a visor HUD box or a physical-world slot (a release
        /// there PLACES the item into that slot), and only while the screen has room for one more chip
        /// (<see cref="ParkingState.MaxChips"/>, the release's own cap). Then it joins the parked
        /// chips; anywhere else it cancels — the item never left its slot. World-grabbed chips never
        /// left the world and are skipped.</item>
        /// <item>Parked chips drop to the ground through <see cref="DumpChipsToGround"/> — the SAME
        /// gated per-chip <c>ItemActions.DropToWorld</c> the RMB exit has always used (each chip
        /// re-verified against its pinned Expected occupant at execute time; no new mutation
        /// path). Only what the local player CARRIES is dropped (A1): a chip grabbed out of a
        /// world container (a charger, a locker) cancels, whatever the range.</item>
        /// </list>
        /// The drop runs only while the local player could have performed the RMB drop at all
        /// (<see cref="PlayerCanDropNow"/>): never into a world that is loading/unloading or while
        /// the app quits, and never on behalf of an unconscious/dead body. In those cases the chips
        /// simply cancel — nothing is lost, parking is visual-only and the items never moved.
        /// <para>A2: this runs at the head of EVERY close and every replacing open (and of the F6
        /// teardown), so it never throws: each chip's drop is isolated, any other failure is logged,
        /// and the parking state is cleared in a <c>finally</c> — a bad chip can never leave a stuck
        /// radial, a dirty parking list, or a teardown aborted half-way.</para>
        /// </summary>
        private void ReleaseHeldItems()
        {
            var inFlight = _parking.Dragging;
            _parking.Dragging = null;
            if (inFlight == null && _parking.Chips.Count == 0) return; // nothing held: every plain open/close

            try
            {
                // Gate FIRST: at app quit this returns false before anything touches ImGui (the pointer
                // reads below go through ImGui.GetIO, which must not run against a torn-down context).
                if (PlayerCanDropNow())
                {
                    if (inFlight != null && InFlightJoinsDrop(inFlight))
                    {
                        _parking.RemoveBySlot(inFlight.Source.Slot); // one slot = one chip, always
                        if (_parking.Chips.Count < ParkingState.MaxChips) _parking.Chips.Add(inFlight);
                    }
                    DumpChipsToGround();
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Radial close: releasing the held items failed: " + e.Message);
            }
            finally
            {
                _parking.Clear();
            }
        }

        /// <summary>A9: would RELEASING the in-flight chip right now have parked it — the only outcome
        /// that counts as "dragged out onto the screen"? Read against the LIVE pointer (the close path
        /// can run before this frame's Draw, or in search mode where Draw skips the hover pass).
        /// <list type="bullet">
        /// <item>Not a world grab, and carried by the local player (A1) — anything else cancels.</item>
        /// <item>Past the parking line (<see cref="PointerBeyondParkingLineNow"/>). Search mode stops
        /// there: its release (SearchPanelView) only ever parks or cancels.</item>
        /// <item>Off the radial's footprint (<see cref="PointerInRadialMaskNow"/>): on the rings a
        /// release resolves against a wedge first (place, fail or park), so the close cancels there —
        /// the safe side of the narrow band where the rings' reach overlaps the parking line.</item>
        /// <item>Not over a visor HUD hand/equipment box nor a physical-world slot: a release there
        /// PLACES the item into that slot (TryDropOnHudZone / TryDropOnWorldSlot), never parks it.</item>
        /// </list></summary>
        private bool InFlightJoinsDrop(ParkingState.Chip chip)
        {
            if (chip == null || chip.IsWorld || chip.Source == null) return false;
            var item = chip.Item;
            if (item == null || !ItemActions.IsCarriedByLocalPlayer(item)) return false;
            if (!PointerBeyondParkingLineNow()) return false;
            if (_searchOpen) return true;
            if (PointerInRadialMaskNow()) return false;
            if (UI.Hud.HudSystem.ZoneAt()?.Slot != null) return false;   // ZoneAt is fail-soft (null)
            if (WorldSlotUnderCursor() != null) return false;            // fail-soft raycast (null)
            return true;
        }

        /// <summary>Could the local player perform the RMB exit-drop right now? Mirrors the gates an
        /// open radial already lives under (<c>Guards.CanKeepRadialOpenWhy</c>): a live world
        /// (GameState Running or Paused — GameManager.cs:78, GridSystem/GameState.cs), not quitting
        /// (<c>Singleton&lt;GameManager&gt;.IsQuitting</c>, the flag vanilla's own OnDestroy paths
        /// check — Util/Singleton.cs:11, DynamicThing.cs:2640), and a responsive local entity
        /// (<c>InventoryManager.Parent</c> + <c>Entity.IsUnresponsive</c>, InventoryManager.cs:49,
        /// Entity.cs:1551). Fail-soft: any exception = don't drop.</summary>
        private static bool PlayerCanDropNow()
        {
            try
            {
                if (Assets.Scripts.Util.Singleton<GameManager>.IsQuitting) return false;
                var gs = GameManager.GameState;
                if (gs != Assets.Scripts.GridSystem.GameState.Running
                    && gs != Assets.Scripts.GridSystem.GameState.Paused) return false;
                var parent = Assets.Scripts.Inventory.InventoryManager.Parent;
                return parent != null && !parent.IsUnresponsive
                    && Assets.Scripts.Inventory.InventoryManager.ParentHuman != null;
            }
            catch { return false; }
        }

        /// <summary>The parking line (see <see cref="ParkMarginPx"/>): past it on every ring, a
        /// released drag parks on open screen. One predicate for the release path and the close
        /// path, so "would have parked" and "counts as on the screen" are the same test.</summary>
        private bool BeyondParkingLine(float mainDist, float satDist)
            => mainDist > _lastOuterR + ParkMarginPx
               && (_satellite == null || satDist > _satellite.OuterR + ParkMarginPx);

        /// <summary><see cref="BeyondParkingLine"/> measured from the LIVE pointer — the close path
        /// can run from Update before this frame's Draw, or in search mode where Draw skips the
        /// hover pass, so it cannot trust the last Draw's cached distances.</summary>
        private bool PointerBeyondParkingLineNow()
        {
            try
            {
                var mouse = DrawUtil.MousePos();
                float mainDist = (mouse - (DrawUtil.ScreenCenter + _centerOffset)).magnitude;
                float satDist = _satellite != null ? (mouse - _satellite.Center).magnitude : float.MaxValue;
                return BeyondParkingLine(mainDist, satDist);
            }
            catch { return false; }
        }

        /// <summary><see cref="PointerInRadialMask"/> measured from the LIVE pointer, for the close
        /// path (same reasons as <see cref="PointerBeyondParkingLineNow"/>). Fail-safe: an unreadable
        /// pointer counts as ON the radial, where the in-flight chip cancels.</summary>
        private bool PointerInRadialMaskNow()
        {
            try
            {
                var mouse = DrawUtil.MousePos();
                if ((mouse - (DrawUtil.ScreenCenter + _centerOffset)).magnitude <= _lastOuterR * RingReach) return true;
                return _satellite != null && (mouse - _satellite.Center).magnitude <= _satellite.OuterR + SatReachPx;
            }
            catch { return true; }
        }

        /// <summary>
        /// D-008 — the radial's footprint as a MASK over the physical world. While a drag's pointer
        /// is inside it, a world slot BEHIND the radial neither lights up (vanilla's placement box,
        /// driven by <c>WorldSlotCue</c> in Draw) nor takes the drop (<see cref="ResolveDragRelease"/>);
        /// both engage only once the pointer leaves the radial. The footprint is the radial's own
        /// hit geometry, not a crude rect: the main ring's on-the-rings reach
        /// (<see cref="RingReach"/> — the hub included, it is part of the radial) plus the
        /// satellite's hover disc. Uses the last Draw's distances, like every other hover test.
        /// </summary>
        private bool PointerInRadialMask()
        {
            if (_mainDist <= _lastOuterR * RingReach) return true;
            return _satellite != null && _satDist <= _satellite.OuterR + SatReachPx;
        }

        /// <summary>The deliberate exit-drop: one drop message per parked chip, each verified
        /// against its pinned occupant. This is the single sanctioned multi-message action in
        /// the mod (explicit design decision — see the Option A schema doc). Reached ONLY through
        /// <see cref="ReleaseHeldItems"/> (D-004) — every close route drops the same way.
        /// <para>A1: a chip whose item the local player does NOT carry — an Alt-grab out of a
        /// charger/locker slot (<c>TryBeginDrag</c> bug 8) — is simply cancelled: parking is visual
        /// only, the item never moved, so no message and no fail sound, at any range. World-grabbed
        /// chips (free-lying items) never left the world either and are skipped the same way.</para>
        /// <para>A2: the chip list is detached BEFORE the first message goes out (a drop that re-enters
        /// a close path can never replay a chip) and each chip's drop is isolated — one bad chip is
        /// logged and skipped, the rest still drop.</para></summary>
        private void DumpChipsToGround()
        {
            if (_parking.Chips.Count == 0) return;
            var chips = _parking.Chips.ToArray();
            _parking.Chips.Clear();
            int dropped = 0;
            for (int i = 0; i < chips.Length; i++)
            {
                var chip = chips[i];
                try
                {
                    if (chip == null || chip.IsWorld || chip.Source == null) continue;
                    var item = chip.Item;
                    if (item == null || !ItemActions.IsCarriedByLocalPlayer(item)) continue; // A1: cancel
                    if (ItemActions.DropToWorld(chip.Source)) dropped++;
                }
                catch (Exception e)
                {
                    UIALog.Warn("Radial close: dropping a parked item failed: " + e.Message);
                }
            }
            if (dropped > 0)
            {
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
                // Tutorial hook (Build Contract s4): only when at least one chip actually dropped —
                // the D-004 shared close handler, so every close route (RMB, the opener key, MMB, the
                // close band, Esc...) that drops something raises this the same way.
                TutorialSignals.Raise(TSignal.ChipsDroppedOnClose);
            }
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
                              : _hovered >= 0 && _mainDist <= _lastOuterR * RingReach ? MainEntry(_hovered)
                              : null;
            // Scroll over a VALUE wedge (suit pressure, thrust, ...) adjusts that value. Scroll over
            // anything else — empty space, the hub, or a plain wedge with no value modifier — changes
            // how many wedges the ring shows: down toward 2, up toward the configured max.
            if (entry?.OnScroll == null) { AdjustVisibleWedges(s > 0f ? 1 : -1); return; }
            bool scrolledOk = true;
            try { entry.OnScroll(s > 0f ? 1 : -1); }
            catch (Exception e) { UIALog.Warn("Scroll adjust failed: " + e.Message); scrolledOk = false; }
            if (_satellite != null) RefreshSatellite();
            _pendingRefreshAt = Time.unscaledTime + 0.6f; // MP: values re-sync after roundtrip
            if (scrolledOk) TutorialSignals.Raise(TSignal.ValueScrolled);
        }

        /// <summary>#4: hover a device-setting wedge and press an allowed letter to bind that
        /// key to the setting. Same on-the-rings gate as scroll so a stray keypress off the
        /// wheel can't bind. Called every frame the radial is open.</summary>
        public void UpdateHotkeyCapture()
        {
            if (!IsOpen || _searchOpen) return;
            RadialEntry entry = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                              : _hovered >= 0 && _mainDist <= _lastOuterR * RingReach ? MainEntry(_hovered)
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

        /// <summary>The visible main-ring entry at a page-relative index, or null. <c>internal</c> (not
        /// private) only so the uiatest harness (Testing/UiaTestHarness.cs) can read the live ring.</summary>
        internal RadialEntry MainEntry(int i)
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

        /// <summary>Enter a branch wedge's level (what a click on it runs, via SelectSticky).
        /// <c>internal</c> (not private) only so the uiatest harness (Testing/UiaTestShots.cs) can open
        /// The Hub for its screenshots without a synthesized click.</summary>
        internal void PushBranch(RadialEntry branch)
        {
            var level = new Level { Title = branch.Label, Provider = branch.ChildProvider };
            level.Refresh();
            _stack.Add(level);
            _satellite = null;
            _hovered = -1;
            // Tutorial hook: a branch dive (The Hub, a category, a nested bag...) opened a child ring.
            // flag 2 = a branch DIVE (the director tells dives from slide-outs by these bits)
            TutorialSignals.Raise(TSignal.ChildWheelOpened, TutorialSignals.Pack((int)ClassifyWedge(branch), 2));
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
            UnparkActedOn(entry); // A6: before the action runs, while the held item is still in hand
            // Tutorial: classify BEFORE the action runs. On single-player/host the move lands
            // immediately, so ClickVerb read afterwards sees the item already in hand and reports a
            // TAKE as a SWAP (review finding: that silently marked lesson 3 learned).
            TWedgeKind committedKind = entry.OnSelect != null ? ClassifyWedge(entry) : TWedgeKind.Other;
            bool committedOk = true;
            try { entry.OnSelect?.Invoke(); }
            catch (Exception e) { UIALog.Error($"Radial action '{entry.Label}' failed: {e}"); committedOk = false; }

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

            // Tutorial hook (Build Contract s4): a wedge action committed successfully, classified
            // the same way the curved D-022 action word is, so a lesson can never disagree with it.
            if (committedOk && entry.OnSelect != null)
                TutorialSignals.Raise(TSignal.WedgeCommitted, (int)committedKind);
        }

        /// <summary>
        /// A6 — acting on a parked item un-parks it. A parked item is still in its slot (parking is
        /// visual only), so its wedges stay live; if one of them MOVES it, the chip must go at once
        /// or the close-drop sends a second message for the same item: on an MP client the local
        /// slot still shows it until the server's reply lands, so the chip's pinned check passes and
        /// "park X, click X's Take wedge, close within a round trip" would also drop X. Un-parking
        /// never drops anything — the chip simply goes away (the item stays wherever the action puts
        /// it). Runs BEFORE the action (single-player applies the move synchronously). Matched by
        /// what the wedge exposes: its <see cref="RadialEntry.DragSource"/> (slot or pinned item), a
        /// slot <see cref="RadialEntry.Tag"/>, and the ACTIVE HAND's slot whenever the click can move
        /// the held item (<see cref="RadialEntry.MayMoveHeldItem"/>: a take into a busy hand swaps it
        /// out; a stow wedge stows it).
        /// </summary>
        private void UnparkActedOn(RadialEntry entry)
        {
            if (entry == null || _parking.Chips.Count == 0) return;
            try
            {
                var src = entry.DragSource;
                if (src != null)
                {
                    _parking.RemoveBySlot(src.Slot);
                    _parking.RemoveByItem(src.Expected ?? src.Occupant);
                }
                var tagSlot = entry.Tag as Slot;
                if (tagSlot != null) _parking.RemoveBySlot(tagSlot);
                if (entry.MayMoveHeldItem)
                    _parking.RemoveBySlot(Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot);
            }
            catch (Exception e)
            {
                UIALog.Warn("Radial un-park failed: " + e.Message);
            }
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
            RadialHintContext.Reset(); // D-021 statics: nothing stale survives a double-F6
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
            // Tutorial hook: the slide-out gesture opened a satellite ring (core.pushout,
            // tools.careful, split.open all key off this).
            // flag 1 = a SLIDE-OUT satellite (not a dive) - see the ChildWheelOpened arg bits
            TutorialSignals.Raise(TSignal.ChildWheelOpened, TutorialSignals.Pack((int)ClassifyWedge(entry), 1));
        }

        /// <summary>D-021: fold one ring's visible wedges into the hint strip's level-wide flags —
        /// can something here be press-dragged out, does a wedge take the mouse wheel, does a click
        /// RUN something (so the Shift keep-open modifier means anything).</summary>
        private static void ScanLevel(List<RadialEntry> entries, ref bool drag, ref bool scroll, ref bool action)
        {
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                if (e.CanDrag) drag = true;
                if (e.IsScrollAdjust) scroll = true;
                if (e.Enabled && e.OnSelect != null && !e.IsBranch) action = true;
            }
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

        /// <summary>Tutorial hook (Build Contract s3/s4): the wedge's TWedgeKind for WedgeHovered /
        /// WedgeCommitted, read from the SAME fields and the SAME <see cref="RadialEntry.ClickVerb"/>
        /// the curved D-022 action word uses, so a lesson can never name a different action than the
        /// wheel just showed. "Equip" folds into Take (both land the item in a hand); the branch verb
        /// "Open" is IsBranch's own word. Verbs with no clean bucket (Search, Sort, Split, Wear,
        /// Activate/Deactivate, Lock/Unlock, a generic Use/Drop/Eject/Insert/Install/Unpack/Replace-as-
        /// a-branch-label) fall to Other by design — the enum has 9 buckets, not one per verb, and
        /// Other is a safe default, never a wrong specific one.</summary>
        private static TWedgeKind ClassifyWedge(RadialEntry e)
        {
            if (e == null) return TWedgeKind.Other;
            if (ReferenceEquals(e.Tag, HubTag)) return TWedgeKind.Hub;
            if (e.IsScrollAdjust) return TWedgeKind.Value;
            if (e.CanHotkey) return TWedgeKind.Setting;
            if (e.StowStyle) return TWedgeKind.Stow;
            string verb = e.ClickVerb();
            if (verb == null) return TWedgeKind.Other;
            if (verb == "Take" || verb == "Take 1" || verb == "Take half" || verb == "Equip") return TWedgeKind.Take;
            if (verb == "Open") return TWedgeKind.Open;
            if (verb == "Stow" || verb == "Install" || verb == "Insert") return TWedgeKind.Stow;
            if (verb == "Swap") return TWedgeKind.Swap;
            return TWedgeKind.Other;
        }

        /// <summary>Tutorial hook: WedgeHovered fires on a hover CHANGE only (either ring, or a switch
        /// between them) — never a resting hover, and never every frame. Called once per Draw, right
        /// after <see cref="HoverTick"/> so both ring hovers are already resolved for the frame.</summary>
        private void RaiseHoverSignal()
        {
            RadialEntry cur = _satellite != null && _satHovered >= 0 ? SatEntry(_satHovered)
                            : _hovered >= 0 ? MainEntry(_hovered) : null;
            if (ReferenceEquals(cur, _lastHoverSignalEntry)) return;
            _lastHoverSignalEntry = cur;
            if (cur == null) return;
            int flags = (cur.HasSlideOut ? 1 : 0) | (!cur.Enabled ? 2 : 0);
            TutorialSignals.Raise(TSignal.WedgeHovered, TutorialSignals.Pack((int)ClassifyWedge(cur), flags));
        }

        /// <summary>Which wedge index a mouse delta from the ring centre points at (-1 if the
        /// ring is empty). Used by the hover resolvers for the main ring and the satellite.
        /// (It used to be <c>internal</c> for RadialController's flick-commit too; that feature
        /// was deleted in the post-0.9.2.5 play-test round and this is now purely an in-class helper.)</summary>
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
                // D-008: a search-result drag can only PARK or cancel (SearchPanelView's release never
                // targets a HUD box or a world slot), so neither drop cue may light up for it — they
                // would promise a landing the release never delivers.
                HudDropCue.Dragging = null;
                Core.WorldSlotCue.Hide();
                UI.UnityRadialView.Hide();
                UI.SearchPanelView.Render(center, innerR, outerR);
                UI.ParkedItemsView.Render(_parking, mouse);
                // D-021: the hint strip still hugs the (search) ring and speaks search-mode keys.
                RadialHintContext.PublishGeometry(center, outerR, false, Vector2.zero, 0f);
                RadialHintContext.PublishSearch(_parking.Dragging != null, _parking.Chips.Count);
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
                if (_satDist <= _satellite.OuterR + SatReachPx)
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
            RaiseHoverSignal();

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

            // #4 / D-008: publish the dragged item for the drop cues. The visor HUD lights a hand /
            // equipment box under the cursor (HudDropCue); vanilla's world-slot placement box
            // (green/yellow/blue/red) is driven by WorldSlotCue, because vanilla's own is frozen while
            // we hold the cursor block. The WORLD cue is masked by the radial: while the pointer is on
            // the radial a world slot behind it never lights — a release there goes to a wedge or
            // cancels, never into the world (ResolveDragRelease applies the same mask). Both cues are
            // taken down again in Close()/Open(), so a close mid-drag can't strand a green box.
            var dragItem = _parking.Dragging?.Item;
            HudDropCue.Dragging = dragItem;
            Core.WorldSlotCue.Tick(dragItem != null && !PointerInRadialMask() ? dragItem : null);

            // D-022: the curved action word over the ring names what a click (sticky) or a release
            // (hold) will do. Sticky: the hovered MAIN wedge, and only while the pointer is ON the
            // rings (the reach the LMB path uses). Hold (B4): exactly the wedge the key release would
            // execute — the same HoldReleaseEntry OnHoldReleased runs, so a child ring's hovered wedge
            // (or its source wedge) names its verb, and a branch wedge (whose release CLOSES) or any
            // other release that just closes shows no word at all: null beats a false promise.
            RadialEntry verbEntry;
            if (_sticky)
                verbEntry = _satellite == null && _parking.Dragging == null && _hovered >= 0
                    && _mainDist <= _lastOuterR * RingReach ? MainEntry(_hovered) : null;
            else
                verbEntry = _parking.Dragging == null ? HoldReleaseEntry() : null;
            string actionVerb = verbEntry != null && (_sticky || verbEntry.OnSelect != null)
                ? verbEntry.ClickVerb() : null;
            // "Release confirm" (hold): the release runs an action. Sticky mode never reads it (B8).
            bool hoverAction = verbEntry != null && verbEntry.Enabled && !verbEntry.IsBranch
                && verbEntry.OnSelect != null;

            // RMB, as the hint strip must name it. B1: in STICKY mode a child ring at the root is no
            // "back" — RMB there drops the child AND closes everything (UpdateSticky), parked items
            // included; only hold mode's RMB just folds the child ring away (UpdateHoldB). B15b: a
            // root the owner returns from on RMB (the D-064 belt picker) IS a back — one that replaces
            // the menu, so parked items drop on it too (D-004).
            bool rootReturns = _stack.Count == 1 && RmbReturnsFromRoot;
            bool canGoBack = _stack.Count > 1 || (!_sticky && _satellite != null) || rootReturns;

            // D-021: tell the key-hint strip what the inputs do right now. The "what can I do on this
            // ring" flags are LEVEL-wide (any visible wedge, child ring included), not per-hover, so the
            // strip stays still while the pointer sweeps around the ring instead of re-flowing on
            // every wedge it crosses.
            bool lvlDrag = false, lvlScroll = false, lvlAction = false;
            ScanLevel(visible, ref lvlDrag, ref lvlScroll, ref lvlAction);
            if (_satellite != null) ScanLevel(_satellite.Visible, ref lvlDrag, ref lvlScroll, ref lvlAction);
            RadialHintContext.PublishInteraction(
                _sticky, _parking.Dragging != null, canGoBack,
                satPages || level.PageCount > 1, altReach, _parking.Chips.Count, actionVerb,
                hoverAction, lvlDrag, lvlScroll, lvlAction,
                rootReturns && _parking.Chips.Count > 0);

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
                pageText, satPageText,
                actionVerb, verbFromCaller: true);
            UI.ParkedItemsView.Render(_parking, mouse);
        }
    }

    /// <summary>
    /// D-021: what the open radial is doing THIS frame, published for the key-hint strip
    /// (<c>UI.RadialHintBar</c>) so its words name what each input actually does in context — and
    /// so it can yield its click hint to the D-022 curved action word. Presentation-only: value
    /// types and string literals, never a game-object reference.
    /// <para>GEOMETRY is published by whoever renders a ring — <c>UnityRadialView.Render</c> (the
    /// live radial AND the F10 radial-editor preview) and <see cref="RadialMenu.Draw"/> in search
    /// mode; INTERACTION state by <see cref="RadialMenu.Draw"/>. Each half carries the frame it was
    /// published on, so a closed radial's snapshot simply goes stale. Hot-reload: every field is
    /// reset by <see cref="Reset"/> (called from <c>RadialMenu.ResetRepeatCache</c> in the
    /// controller's ShutdownImmediate, and from <c>RadialHintBar.Shutdown</c>).</para>
    /// </summary>
    public static class RadialHintContext
    {
        // ---- geometry (ImGui screen coords: y-down) ----
        public static int GeometryFrame = -100;
        public static Vector2 Center;
        public static float OuterR;
        public static bool SatelliteShown;
        public static Vector2 SatCenter;
        public static float SatOuterR;

        // ---- interaction ----
        public static int InteractionFrame = -100;
        public static bool Search;         // the search panel owns the ring
        public static bool Sticky;         // tap-opened (click to act) vs hold-opened (release to act)
        public static bool Dragging;       // a chip is on the cursor
        public static bool CanGoBack;      // RMB backs out (nested level, hold-mode child ring, belt-picker return) instead of closing
        public static bool BackDropsParked; // that RMB "back" replaces the menu (belt-picker return): parked items drop (D-004)
        public static bool Pageable;       // the page key flips something
        public static bool AltReach;       // the world-reach modifier is held
        public static int Parked;          // chips parked on screen (they drop when the wheel closes)
        public static string Verb;         // the D-022 action word on show (null = none)
        public static bool HoverAction;    // the hovered wedge RUNS something (a hold-release confirms it)
        public static bool LevelDraggable; // some visible wedge can be press-dragged out
        public static bool LevelScroll;    // some visible wedge takes the mouse wheel
        public static bool LevelAction;    // some visible wedge runs an action (Shift keep-open applies)

        /// <summary>Published within the last couple of frames (the strip reads it from Update, the
        /// radial writes it from the ImGui draw hook one step behind).</summary>
        public static bool GeometryFresh => Time.frameCount - GeometryFrame <= 2;
        public static bool InteractionFresh => Time.frameCount - InteractionFrame <= 2;

        public static void PublishGeometry(Vector2 center, float outerR, bool satShown, Vector2 satCenter, float satOuterR)
        {
            GeometryFrame = Time.frameCount;
            Center = center;
            OuterR = outerR;
            SatelliteShown = satShown;
            SatCenter = satCenter;
            SatOuterR = satOuterR;
        }

        public static void PublishInteraction(bool sticky, bool dragging, bool canGoBack, bool pageable,
            bool altReach, int parked, string verb, bool hoverAction,
            bool levelDraggable, bool levelScroll, bool levelAction, bool backDropsParked)
        {
            InteractionFrame = Time.frameCount;
            Search = false;
            Sticky = sticky;
            Dragging = dragging;
            CanGoBack = canGoBack;
            BackDropsParked = backDropsParked;
            Pageable = pageable;
            AltReach = altReach;
            Parked = parked;
            Verb = verb;
            HoverAction = hoverAction;
            LevelDraggable = levelDraggable;
            LevelScroll = levelScroll;
            LevelAction = levelAction;
        }

        public static void PublishSearch(bool dragging, int parked)
        {
            InteractionFrame = Time.frameCount;
            Search = true;
            Sticky = true;
            Dragging = dragging;
            CanGoBack = true;          // Escape / RMB return to the wheel
            BackDropsParked = false;   // ...without closing it: parked chips stay parked
            Pageable = false;
            AltReach = false;
            Parked = parked;
            Verb = null;
            HoverAction = false;
            LevelDraggable = true;     // result wedges press-drag out like any item wedge
            LevelScroll = LevelAction = false;
        }

        /// <summary>A menu closed or was replaced: nothing it published describes the screen any more.</summary>
        public static void ResetInteraction()
        {
            InteractionFrame = -100;
            Verb = null;
            Dragging = HoverAction = Search = BackDropsParked = false;
            LevelDraggable = LevelScroll = LevelAction = false;
            Parked = 0;
        }

        /// <summary>Hot-reload / teardown: back to the pristine state.</summary>
        public static void Reset()
        {
            ResetInteraction();
            GeometryFrame = -100;
            Center = Vector2.zero;
            OuterR = 0f;
            SatelliteShown = false;
            SatCenter = Vector2.zero;
            SatOuterR = 0f;
            Sticky = CanGoBack = Pageable = AltReach = false;
        }
    }
}

