using System.Collections.Generic;
using Assets.Scripts.Objects;
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
    /// <para>WHAT IT DOES. While the main window is open AND interactive (the player has freed the
    /// mouse — the same gate every click surface checks), the mouse WHEEL moves a selection cursor
    /// through a flat, display-ordered list of every navigable target (each region's manila
    /// <see cref="GridTab"/>, then its cells, then its nested children — recursively), auto-scrolling
    /// the list so the cursor stays in view; <c>F</c> (<see cref="KeyMap.InventorySelect"/>) acts on
    /// the cursor (equip a cell's occupant to the active hand, or open/close a tab's region — the
    /// primary open gesture under the collapsed-by-default store); <c>G</c>
    /// (<see cref="KeyMap.SmartStow"/>) stows the active-hand item into the cursor's empty cell.</para>
    ///
    /// <para>NO DOUBLE-FIRE. Every vanilla inventory-nav path (<c>NextButton</c>/<c>PreviousButton</c>
    /// via <c>CheckDisplaySlotInput</c>, <c>InventorySelect</c>, <c>SmartStow</c>) early-returns while
    /// <c>InputMouse.IsMouseControl</c> is true — and that is TRUE exactly when the cursor is freed,
    /// which is exactly when this window is interactive (<c>MouseModeController.Check</c> →
    /// <c>SetMouseControl(true)</c>). So the two are mutually exclusive by construction: the grid owns
    /// the wheel/F/G frames precisely in the regime where vanilla has stood its own nav down. The keys
    /// are additionally gated on <c>KeyManager.InputState == Game</c> (respects typing / modal states,
    /// like <c>Core.Guards</c>) and on no radial being open.</para>
    ///
    /// <para>MP-SAFETY. This is a VIEW/LAYOUT system. The only state mutations it can reach are F's
    /// equip and G's stow, and both route through the SAME gated funnel the click/drag paths already
    /// use — <see cref="BagGridCell.ActivateFromKeyboard"/> → <c>ItemActions.EquipToActiveHand</c> and
    /// <see cref="BagGridCell.StowHereFromKeyboard"/> → <c>ItemActions.StowActiveHandTo</c>, each
    /// re-verified at execute time — never a new mutation path. F on a tab is a per-save collapse-flag
    /// write only.</para>
    ///
    /// <para>OWNERSHIP. The MAIN window owns the cursor (pinned windows do not participate — a
    /// deliberate simplicity choice: exactly ONE box is highlighted at a time). The flat list is
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

        /// <summary>Apply the wheel mode to the list's <see cref="ScrollRect"/>: with the feature on,
        /// the wheel drives the CURSOR (and ensure-visible drives the pan), so the ScrollRect's own
        /// wheel free-pan is neutralised (sensitivity 0 — drag-to-scroll on empty space is unaffected,
        /// it rides OnDrag not the wheel). With the feature off, the default free-pan sensitivity is
        /// restored. Set on Show and re-asserted each interactive Tick so a mid-session config toggle
        /// (or a race with the EventSystem's own scroll dispatch on the enabling frame) can never
        /// leave the two wheel consumers fighting.</summary>
        public static void ApplyScrollMode(ScrollRect scroll)
        {
            if (scroll == null) return;
            scroll.scrollSensitivity = Enabled ? 0f : DefaultScrollSensitivity;
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

        /// <summary>Per-frame input, called from <c>TheGridPanel.Tick</c> ONLY while the window is open
        /// AND interactive (the freed-mouse regime where vanilla's own nav is stood down). Reads the
        /// wheel (cursor move + auto-scroll) and the F/G keys (act on the cursor). Runs BEFORE the
        /// panel's structural signature check, so an F-driven collapse toggle rebuilds on the same
        /// frame.</summary>
        public static void Tick(ScrollRect scroll)
        {
            // Reflect the current config every interactive frame (BEFORE the Enabled gate) so a live
            // toggle restores the ScrollRect's own free-pan the moment the feature is switched off.
            ApplyScrollMode(scroll);
            if (!Enabled) return;
            // A radial owns the wheel AND the keyboard while it is up (its ModalScope even frees the
            // cursor); leave the cursor where it is and process nothing this frame.
            if (RadialController.AnyRadialOpen) return;
            HandleWheel(scroll);
            HandleKeys();
        }

        // ---------- wheel ----------

        private static void HandleWheel(ScrollRect scroll)
        {
            // Only while the pointer is over the window (the same hover-gate the ScrollHit gives the
            // old free-pan): a freed cursor parked over the world leaves the wheel to the game.
            if (!TheGridPanel.HitTestWindow((Vector2)Input.mousePosition)) return;

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

            bool f = false, g = false;
            try { f = KeyManager.GetButtonDown(KeyMap.InventorySelect); } catch { }
            try { g = KeyManager.GetButtonDown(KeyMap.SmartStow); } catch { }
            if (!f && !g) return;

            if (_cursor < 0 || _cursor >= _items.Count) return;
            NavItem it = _items[_cursor];

            if (f) DoSelect(it);
            else if (g) DoStow(it);
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

        /// <summary>G on the cursor: a CELL stows the active-hand item into it (the gated
        /// <c>StowActiveHandTo</c> funnel — fails softly with the vanilla sound on an occupied cell or
        /// empty hand). G on a tab is a no-op (routing an item INTO a bag is the drag / SmartStow+
        /// path, not this cursor).</summary>
        private static void DoStow(NavItem it)
        {
            if (it.Cell != null) it.Cell.StowHereFromKeyboard();
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
            if (target == null || scroll == null) return;
            RectTransform content = scroll.content;
            RectTransform viewport = scroll.viewport;
            if (content == null || viewport == null) return;

            float contentH = content.rect.height;
            float viewH = viewport.rect.height;
            float range = contentH - viewH;
            if (range <= 1f) { scroll.verticalNormalizedPosition = 1f; return; }

            Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(content, target);
            float centerY = (b.min.y + b.max.y) * 0.5f;
            float halfH = (b.max.y - b.min.y) * 0.5f;
            float depth = -centerY;                 // distance from content TOP, downward positive
            const float margin = 8f;

            // Current scroll offset (how far the content top sits above the viewport top).
            float s = (1f - scroll.verticalNormalizedPosition) * range;
            float itemTop = depth - halfH - margin;
            float itemBot = depth + halfH + margin;
            if (itemTop < s) s = itemTop;
            else if (itemBot > s + viewH) s = itemBot - viewH;
            s = Mathf.Clamp(s, 0f, range);

            scroll.verticalNormalizedPosition = 1f - s / range;
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
