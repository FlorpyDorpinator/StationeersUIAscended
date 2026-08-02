using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;   // InputMouse (the cursor-freed / mouse-control signal)
using StationeersUIMod.Features;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The Universal Inventory's SCROLL-SELECT + KEYBOARD NAVIGATION cursor (#4) — the mod-side
    /// mirror of vanilla's <c>InventoryWindowManager</c> highlighted-slot model (<c>_visibleSlots</c>
    /// ordered flat list + <c>CurrentSlotIndex</c> cursor + <c>CurrentScollButton</c> highlight), but
    /// over The Grid's region tree rather than vanilla's slot buttons.
    ///
    /// <para>WHAT IT DOES. Vanilla-style: while the main window is open and the cursor is CAPTURED
    /// (normal play — the mouse is controlling the camera, NOT freed), the mouse WHEEL moves a selection
    /// cursor through a flat, display-ordered list of every navigable target (each region's manila
    /// <see cref="GridTab"/>, then its cells, then its nested children — recursively — plus the contents
    /// of any PINNED windows), auto-scrolling whichever window the cursor lands in; <c>F</c>
    /// (<see cref="KeyMap.InventorySelect"/>) acts on the cursor (equip a cell's occupant to the active
    /// hand, or open/close a tab's region — the primary open gesture under the collapsed-by-default
    /// store). The MOMENT the mouse is FREED ("out and about" for click/drag) the feature stands down
    /// entirely and the wheel FREE-PANS the whole window instead (see <see cref="ApplyScrollMode"/>).
    /// <c>G</c> is left to SmartStow+ (the mod's own G), which is the vanilla-accurate "smart stow the
    /// held item" — handling it here too would double-fire.</para>
    ///
    /// <para>NO DOUBLE-FIRE. This runs in the CAPTURED regime, which is exactly where vanilla's own
    /// inventory-cursor nav (<c>NextButton</c>/<c>PreviousButton</c> — the wheel + NextItem/PreviousItem
    /// keys — and <c>InventorySelect</c>) also runs (they early-return only when
    /// <c>InputMouse.IsMouseControl</c>, i.e. when FREED). So the two are NOT mutually exclusive by the
    /// cursor state alone: the Harmony prefixes in <c>Core.InventoryNavPatches</c> actively stand vanilla
    /// down whenever <see cref="OwnsVanillaInventoryNav"/> — leaving the 1–6 equipment hotkeys untouched
    /// (those go through <c>CheckDisplaySlot→MoveEquipmentSlot</c>, never NextButton/PreviousButton).
    /// The F key is additionally gated on <c>KeyManager.InputState == Game</c> and on no radial open.</para>
    ///
    /// <para>MP-SAFETY. This is a VIEW/LAYOUT system. The only state mutations it can reach are F's
    /// equip and G's stow, and both route through the SAME gated funnel the click/drag paths already
    /// use — <see cref="BagGridCell.ActivateFromKeyboard"/>, which covers take-or-place, resolving
    /// to <c>ItemActions.EquipToActiveHand</c> or <c>ItemActions.StowActiveHandTo</c>, each
    /// re-verified at execute time — never a new mutation path. F on a tab is a per-save collapse-flag
    /// write only.</para>
    ///
    /// <para>OWNERSHIP. The MAIN window drives the cursor, but the flat list SPANS the main tree and
    /// every pinned window (still exactly ONE box highlighted at a time). The flat list is
    /// rebuilt on every structural rebuild and the cursor is reconciled by Slot/RefId identity, so a
    /// bag opening/closing or an item moving keeps the selection stable (like vanilla's
    /// <c>TryUpdateSelectedInventorySlot</c>); when the selected item is gone the cursor keeps its list
    /// POSITION instead of jumping to the top.</para>
    ///
    /// <para>HOT-RELOAD. All state is static and cleared by <see cref="Clear"/> from
    /// <c>TheGridPanel.Hide</c>/<c>Shutdown</c> — the flat list holds refs to pooled cells/tabs that
    /// die with the panel canvas, so it must not survive a reload.</para>
    /// </summary>
    public static class GridSelection
    {
        /// <summary>One navigable target in display order: EITHER a cell (with its
        /// <see cref="Slot"/> identity) or a tab (with its container <see cref="RefId"/> identity).
        /// <see cref="Rect"/> is the on-screen rect used for ensure-visible auto-scroll.</summary>
        public struct NavItem
        {
            public BagGridCell Cell;
            public GridTab Tab;
            public Slot Slot;
            public long RefId;
            public RectTransform Rect;

            public static NavItem ForCell(BagGridCell cell)
            {
                return new NavItem
                {
                    Cell = cell,
                    Tab = null,
                    Slot = cell != null ? cell.Slot : null,
                    RefId = 0L,
                    Rect = cell != null ? cell.Rect : null,
                };
            }

            public static NavItem ForTab(GridTab tab)
            {
                return new NavItem
                {
                    Cell = null,
                    Tab = tab,
                    Slot = null,
                    RefId = tab != null ? tab.RefId : 0L,
                    Rect = tab != null ? tab.Rect : null,
                };
            }
        }

        // The display-ordered flat nav list, rebuilt on every structural rebuild by walking the
        // region tree (GridRegionView.CollectNav). Steady-state it is untouched.
        private static readonly List<NavItem> _items = new List<NavItem>(64);

        private static int _cursor = -1;   // index into _items, or < 0 for "no selection yet"

        // Identity of the selected target, for reconciling the cursor across a structural rebuild
        // (the flat list is rebuilt, but the pooled cell/tab may land at a different index).
        private static bool _selIsTab;
        private static Slot _selSlot;
        private static long _selRefId;

        // The components currently wearing the selection visual, so a cursor MOVE (no rebuild) can
        // clear the old one. On a rebuild the cells/tabs reset their own flag (Bind/SetCore), so
        // these are dropped without touching a possibly-repurposed pooled component.
        private static BagGridCell _curCell;
        private static GridTab _curTab;

        // The ScrollRect's default wheel sensitivity (BuildScroll's value), restored when the
        // feature is off so plain wheel free-pan returns.
        private const float DefaultScrollSensitivity = 28f;

        /// <summary>Is the scroll-select / keyboard-nav feature on? Fail-open (default true) around an
        /// unbound config, matching the other Grid feature gates.</summary>
        public static bool Enabled
        {
            get
            {
                try { return UIAConfig.GridKeyboardNav == null || UIAConfig.GridKeyboardNav.Value; }
                catch { return true; }
            }
        }

        /// <summary>Apply the wheel mode to the list's <see cref="ScrollRect"/>. Scroll-select is a
        /// CAPTURED-regime feature (see <see cref="OwnsVanillaInventoryNav"/>): while the mouse controls
        /// the camera the wheel drives the CURSOR, so the ScrollRect's own wheel free-pan is neutralised
        /// (sensitivity 0). The MOMENT the mouse is freed ("out and about") the feature stands down and
        /// the wheel must FREE-PAN the whole window so the player can see every bag — so the default
        /// free-pan sensitivity is restored. Also restored when the feature is off. Set on Show and
        /// re-asserted every Tick so a mid-session config toggle or a mouse-mode flip can never leave
        /// the two wheel consumers fighting.</summary>
        public static void ApplyScrollMode(ScrollRect scroll)
        {
            if (scroll == null) return;
            bool ownWheel = Enabled && !MouseFreed();
            scroll.scrollSensitivity = ownWheel ? 0f : DefaultScrollSensitivity;
        }

        /// <summary>The cursor is "freed" — mouse-control / double-tap latch, vanilla
        /// <c>InputMouse.IsMouseControl</c>. In this regime the player is clicking/dragging with the
        /// pointer and the wheel FREE-PANS the list; scroll-select runs only when the cursor is CAPTURED
        /// (this returns false). Using the exact vanilla signal keeps the mod and vanilla mutually
        /// exclusive on the wheel: vanilla's own NextButton/PreviousButton also early-return when it
        /// is true (decompile InventoryWindowManager.cs:133/144).</summary>
        private static bool MouseFreed()
        {
            try { return InputMouse.IsMouseControl; } catch { return false; }
        }

        /// <summary>True while the Universal Inventory OWNS the mouse wheel + F/G: the main window is
        /// open, the feature is on, no radial is up, and the cursor is CAPTURED. The Harmony prefixes in
        /// <c>Core.InventoryNavPatches</c> read this to stand vanilla's inventory-cursor nav down in
        /// exactly this regime, so the wheel/F never drive two cursors or act twice. Pure function of
        /// live state (no cached flag), so it is correct regardless of Update order versus vanilla's
        /// ManagerUpdate.</summary>
        public static bool OwnsVanillaInventoryNav
        {
            get
            {
                try
                {
                    return Enabled
                        && TheGridPanel.IsOpen
                        && !MouseFreed()
                        && !RadialController.AnyRadialOpen;
                }
                catch { return false; }
            }
        }

        /// <summary>Structural (re)build: rebuild the flat nav list from the region tree and reconcile
        /// the cursor by Slot/RefId identity. Called at the END of <c>TheGridPanel.Rebuild</c> — after
        /// the root region has bound and laid out — so the rects the list references are placed and the
        /// cells/tabs are freshly bound (their own selection flags reset).</summary>
        public static void Rebuild(GridRegionView root)
        {
            // The cells/tabs were re-bound/re-set, which clears their own selection flag; drop the
            // stale component handles without touching a possibly-repurposed pooled component.
            _curCell = null;
            _curTab = null;

            _items.Clear();
            if (root != null) root.CollectNav(_items);
            // Pinned nested bags are torn out into their own floating windows; fold their contents into
            // the ONE nav cursor too (after the main tree, in display order per window). EnsureVisible
            // resolves each item's own ScrollRect, so landing on a pinned item scrolls THAT window.
            PinnedInventoryWindow.CollectNav(_items);

            int n = _items.Count;
            if (n == 0)
            {
                _cursor = -1;
                _selSlot = null;
                _selRefId = 0L;
                _selIsTab = false;
                return;
            }

            // Reconcile: find the previously-selected target by identity.
            int found = -1;
            if (_cursor >= 0 && (_selSlot != null || _selRefId != 0L))
            {
                for (int i = 0; i < n; i++)
                {
                    NavItem it = _items[i];
                    if (_selIsTab)
                    {
                        if (it.Tab != null && it.RefId != 0L && it.RefId == _selRefId) { found = i; break; }
                    }
                    else if (it.Cell != null && ReferenceEquals(it.Slot, _selSlot))
                    {
                        found = i;
                        break;
                    }
                }
            }

            if (found < 0)
            {
                // Never had a selection: stay unselected (the first wheel/key establishes one).
                if (_cursor < 0) return;
                // The selected target is gone (item taken / bag closed): keep the list POSITION
                // rather than snapping to the top, like vanilla clamps CurrentSlotIndex.
                found = Mathf.Clamp(_cursor, 0, n - 1);
            }

            // Re-assert the visual; no ensure-visible on a structural rebuild (the change may be
            // unrelated to the cursor — don't yank the view under the player).
            SetCursor(found, null, false);
        }

        /// <summary>Per-frame input, called from <c>TheGridPanel.Tick</c> EVERY frame the main window is
        /// open (both mouse regimes — it gates itself). Scroll-select drives the cursor + F only while
        /// the cursor is CAPTURED (normal play, the vanilla-inventory-scroll regime); while the mouse is
        /// FREED it stands down entirely and the wheel free-pans the window instead. Runs BEFORE the
        /// panel's structural signature check, so an F-driven collapse toggle rebuilds on the same
        /// frame.</summary>
        public static void Tick(ScrollRect scroll)
        {
            // Reflect the wheel mode every frame (BEFORE the gates) so flipping the config OR freeing
            // the mouse restores the window's free-pan the very same frame.
            ApplyScrollMode(scroll);
            if (!Enabled) return;
            // CAPTURED-regime feature: while the mouse is FREED ("out and about") the player clicks and
            // drags and the wheel free-pans the whole window (ApplyScrollMode) — we drive nothing here.
            if (MouseFreed()) return;
            // A radial owns the wheel AND the keyboard while it is up (its ModalScope even frees the
            // cursor); leave the cursor where it is and process nothing this frame.
            if (RadialController.AnyRadialOpen) return;
            HandleWheel(scroll);
            HandleKeys();
        }

        // ---------- wheel ----------

        private static void HandleWheel(ScrollRect scroll)
        {
            // No hover-gate: in the captured regime the cursor is locked to the centre for camera
            // control, so it is never "over" the window — vanilla's own inventory wheel likewise cycles
            // slots without hovering the panel. We already gate on the window being OPEN + captured.

            // Match vanilla EXACTLY (decompile InventoryManager.cs:1412): the inventory wheel nav is
            // skipped in Placement mode, where the same wheel cycles the ConstructionPanel's build
            // variant. We do not patch CheckDisplaySlotInput, so its ConstructionPanel scroll still runs;
            // standing our cursor down here keeps the wheel single-purpose while a multi-variant kit is out.
            try
            {
                if (Assets.Scripts.Inventory.InventoryManager.CurrentMode
                    == Assets.Scripts.Inventory.InventoryManager.Mode.Placement) return;
            }
            catch { }

            float dy = 0f;
            try { dy = Input.mouseScrollDelta.y; } catch { }
            if (Mathf.Abs(dy) < 0.01f) return;

            bool invert = false;
            try { invert = Assets.Scripts.Serialization.Settings.CurrentData.InvertMouseWheelInventory; }
            catch { }

            float v = dy * (invert ? -1f : 1f);
            // Match vanilla's mapping: wheel UP (v > 0) → NextButton → CurrentSlotIndex-- → toward the
            // TOP of the list; wheel DOWN → toward the bottom.
            int dir = v > 0f ? -1 : 1;
            MoveCursor(dir, scroll);
        }

        private static void MoveCursor(int dir, ScrollRect scroll)
        {
            int n = _items.Count;
            if (n == 0) return;

            int idx;
            if (_cursor < 0 || _cursor >= n)
            {
                // First nav establishes an endpoint (top for a downward move, bottom for upward).
                idx = dir > 0 ? 0 : n - 1;
            }
            else
            {
                idx = _cursor + dir;
                if (idx < 0) idx = n - 1;        // wrap
                else if (idx >= n) idx = 0;
            }
            SetCursor(idx, scroll, true);
        }

        // ---------- keys ----------

        private static void HandleKeys()
        {
            KeyInputState state;
            try { state = KeyManager.InputState; }
            catch { return; }
            if (state != KeyInputState.Game) return;      // typing / modal: keys belong elsewhere
            if (RadialController.AnyRadialOpen) return;    // a radial owns the keyboard this frame

            // F acts on the cursor (open/close a bag, or equip a cell's item to the active hand). G is
            // deliberately NOT handled here: it belongs to SmartStow+ (the mod's own G, patched onto
            // InventoryManager.SmartStow downstream of vanilla's InventoryWindowManager.SmartStow),
            // which routes the held item to the best bag — the vanilla-accurate G. Handling it here too
            // would double-fire (stow into the highlighted cell AND smart-route). See Core.InventoryNavPatches.
            bool f = false;
            try { f = KeyManager.GetButtonDown(KeyMap.InventorySelect); } catch { }
            KeyCode rk = DeviceWindowKey;
            bool r = rk != KeyCode.None && Input.GetKeyDown(rk);
            if (!f && !r) return;

            if (_cursor < 0 || _cursor >= _items.Count) return;
            NavItem it = _items[_cursor];

            // R opens the highlighted DEVICE's vanilla internals window (its own window, not in-grid) —
            // the scroll-nav twin of clicking a device. Only a CELL can be a device (a tab has no Slot);
            // RadialController yields R to us this frame via OwnsDeviceWindowKey so the Tool Radial (same
            // key) does not also open. Falls through to F when the highlight is not an openable device.
            if (r && it.Slot != null)
            {
                DynamicThing occ = null;
                try { occ = it.Slot.Get(); } catch { }
                if (occ != null && Core.DeviceWindow.CanOpen(occ) && Core.DeviceWindow.Open(occ)) return;
            }
            if (f) DoSelect(it);
        }

        /// <summary>The key that opens a highlighted device's internals window in scroll-nav — the Tool
        /// Radial key (R by default). Shared with <see cref="OwnsDeviceWindowKey"/> so RadialController
        /// stands its radial down on exactly the frame the grid consumes it.</summary>
        public static KeyCode DeviceWindowKey
        {
            get { try { return UIAConfig.ToolRadialKey != null ? UIAConfig.ToolRadialKey.Value : KeyCode.R; } catch { return KeyCode.R; } }
        }

        /// <summary>True while the scroll-select cursor sits on an openable DEVICE and the grid owns the
        /// device-window key: RadialController skips opening the Tool Radial (same key) this frame so R
        /// opens the device's internals window instead. Pure function of live state (no cached flag).</summary>
        public static bool OwnsDeviceWindowKey
        {
            get
            {
                try
                {
                    if (!Enabled || !TheGridPanel.IsOpen || MouseFreed() || RadialController.AnyRadialOpen) return false;
                    if (_cursor < 0 || _cursor >= _items.Count) return false;
                    NavItem it = _items[_cursor];
                    if (it.Slot == null) return false;                 // a tab, not a device cell
                    DynamicThing occ = it.Slot.Get();
                    return occ != null && Core.DeviceWindow.CanOpen(occ);
                }
                catch { return false; }
            }
        }

        /// <summary>F on the cursor: a CELL equips its occupant to the active hand (the same funnel
        /// call the left-click uses); a TAB toggles its region collapse (the primary open gesture under
        /// the collapsed-by-default store — mirrors vanilla F opening a container).</summary>
        private static void DoSelect(NavItem it)
        {
            if (it.Tab != null)
            {
                if (it.RefId != 0L) GridCollapseStore.ToggleCollapsed(it.RefId);
                return;
            }
            if (it.Cell != null) it.Cell.ActivateFromKeyboard();
        }

        // ---------- cursor + visuals ----------

        private static void SetCursor(int idx, ScrollRect scroll, bool ensure)
        {
            ClearVisual();
            _cursor = idx;
            if (idx < 0 || idx >= _items.Count)
            {
                _selSlot = null;
                _selRefId = 0L;
                _selIsTab = false;
                return;
            }

            NavItem it = _items[idx];
            _selIsTab = it.Tab != null;
            _selSlot = it.Slot;
            _selRefId = it.RefId;

            if (it.Cell != null) { _curCell = it.Cell; it.Cell.SetSelected(true); }
            if (it.Tab != null) { _curTab = it.Tab; it.Tab.SetSelected(true); }

            if (ensure && scroll != null) EnsureVisible(it, scroll);
        }

        private static void ClearVisual()
        {
            if (_curCell != null) { _curCell.SetSelected(false); _curCell = null; }
            if (_curTab != null) { _curTab.SetSelected(false); _curTab = null; }
        }

        /// <summary>Nudge the list's <see cref="ScrollRect.verticalNormalizedPosition"/> so the
        /// selected rect sits fully inside the viewport, scrolling the minimum needed (align the item's
        /// top to the viewport top when it is above the fold, its bottom to the viewport bottom when
        /// below). Content is top-left-pivoted, so its local Y runs 0 (top) → -height (bottom).</summary>
        private static void EnsureVisible(NavItem it, ScrollRect scroll)
        {
            RectTransform target = it.Rect;
            if (target == null) return;
            // Resolve the ScrollRect that actually contains this item: the main list for a main-window
            // cell/tab, or the pinned window's own list for a pinned item. A window with no scroll
            // container has nothing to auto-scroll (no-op) — the highlight still shows. `scroll` is
            // only a hint used when the ancestor walk finds nothing.
            ScrollRect owner = target.GetComponentInParent<ScrollRect>();
            if (owner == null) owner = scroll;
            if (owner == null) return;
            RectTransform content = owner.content;
            RectTransform viewport = owner.viewport;
            if (content == null || viewport == null) return;

            float contentH = content.rect.height;
            float viewH = viewport.rect.height;
            float range = contentH - viewH;
            if (range <= 1f) { owner.verticalNormalizedPosition = 1f; return; }

            Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(content, target);
            float centerY = (b.min.y + b.max.y) * 0.5f;
            float halfH = (b.max.y - b.min.y) * 0.5f;
            float depth = -centerY;                 // distance from content TOP, downward positive
            const float margin = 8f;

            // Current scroll offset (how far the content top sits above the viewport top).
            float s = (1f - owner.verticalNormalizedPosition) * range;
            float itemTop = depth - halfH - margin;
            float itemBot = depth + halfH + margin;
            if (itemTop < s) s = itemTop;
            else if (itemBot > s + viewH) s = itemBot - viewH;
            s = Mathf.Clamp(s, 0f, range);

            owner.verticalNormalizedPosition = 1f - s / range;
        }

        /// <summary>Drop all selection state and the visual on the current target. Called from
        /// <c>TheGridPanel.Hide</c> (the window closed — start the next open fresh) and
        /// <c>Shutdown</c> (hot reload: the list holds refs to pooled cells/tabs that die with the
        /// canvas). Idempotent.</summary>
        public static void Clear()
        {
            ClearVisual();
            _items.Clear();
            _cursor = -1;
            _selIsTab = false;
            _selSlot = null;
            _selRefId = 0L;
        }
    }
}
