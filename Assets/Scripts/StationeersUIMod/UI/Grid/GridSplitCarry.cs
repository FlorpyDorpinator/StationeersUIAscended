using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// Opt-in split-to-cursor for the UNIVERSAL INVENTORY (<see cref="UIAConfig.SplitToCursor"/>):
    /// after a split square (1 / 1/2 / Split N, <see cref="GridRegionView"/>) the split child — once
    /// <see cref="SplitToCursor"/> sees it land in a hand — rides the cursor as the grid's drag ghost
    /// in a CLICK-TO-PLACE mode. The next left click places it EXACTLY like a cell drag release
    /// (<see cref="BagGridCell.ResolveCarryClick"/> = the same ray-pick and the same
    /// <c>ResolveRelease</c> the drag uses: a cell / bag, a manila tab in profile mode, a HUD hand or
    /// equipment box, an open vanilla slot, a physical-world slot, or open space = drop at the feet).
    /// Right-click or Esc stops carrying: the child simply stays in the hand vanilla put it in. The
    /// window stays open throughout. Clicking the child's own hand cell (or its hand box) = keep it.
    ///
    /// <para><b>The click catcher.</b> While carrying, a full-screen, invisible, raycast-eating Image on
    /// its own canvas (<see cref="SortingOrder"/>, above the whole Grid family incl. the Control Center,
    /// below <see cref="DragGhostLayer"/>) OWNS the next press, so the click that places the child can
    /// never ALSO click a cell (take-to-hand / device window), a split square (a second split), a tab or
    /// window chrome underneath. On that press the catcher turns raycast-TRANSPARENT
    /// (<see cref="SplitCarryCatcher.PassThrough"/>, an <see cref="ICanvasRaycastFilter"/>): UGUI keeps
    /// routing the release to the catcher (it owns the press), so no click fires underneath, while the
    /// EventSystem's hover state (<c>IsPointerOverGameObject</c>, vanilla's <c>SlotDisplayButton.CurrentSlot</c>)
    /// refreshes against the REAL surfaces — which is what the shared <see cref="DropResolver"/> reads.
    /// The resolution therefore runs a frame or two after the release, once that state is fresh
    /// (see <see cref="OnCatcherUp"/>). The wheel is forwarded to whatever is under the catcher, so the
    /// window still scrolls to the bag you want.</para>
    ///
    /// <para><b>MP-safe.</b> No state is mutated here except through the placement's own
    /// <see cref="ItemActions"/> call (DragTo / DragAllOfTypeTo / DropToWorld — one message, the source
    /// pinned to the child and re-verified at execute time). Cancelling sends nothing.</para>
    ///
    /// <para><b>Lifetime.</b> Ticked from the plugin Update just before <see cref="TheGridPanel.Tick"/>
    /// (its own try/catch; the catcher also self-cancels if that Tick stops running);
    /// cancels itself whenever the Universal Inventory (or a pinned window) stops being interactive, a
    /// radial / the F9 editor / F10 / a vanilla menu / the tutorial coach takes over, or the child leaves
    /// its hand. <see cref="Shutdown"/> destroys the catcher canvas and the ghost, releases the
    /// world-pick hold and nulls every static (F6 hot reload).</para>
    /// </summary>
    internal static class GridSplitCarry
    {
        /// <summary>Above the Grid family (main 5020, pins 5030, tab drag-out 5100, profile popup 5150,
        /// Control Center 5200), below the drag-ghost layer (5250) so the ghost draws on top.</summary>
        private const int SortingOrder = 5245;
        private const string BlockId = "splitcarry";   // our named Core.CursorBlockArbiter hold

        private static GameObject _root;               // catcher canvas (lazily built, reused)
        private static SplitCarryCatcher _catcher;
        private static GameObject _ghostGo;            // on DragGhostLayer
        private static RectTransform _ghostRt;

        private static ScannedSlot _source;            // the hand slot holding the child, pinned
        private static bool _pressed;
        private static int _pressFrame = -999;
        private static int _resolveAt = -1;
        private static bool _blockHeld;
        private static BagGridCell _highlight;         // the cell lit as the would-be target

        private static PointerEventData _ped;          // reused per-frame pick (no per-frame alloc)
        private static EventSystem _pedEs;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>(16);

        private static bool _swallowUntilUp;           // a press that began during a carry: no grid click on its release
        private static int _lastTickFrame = -999;      // the catcher's self-hide watchdog reads this

        /// <summary>A split child is on the cursor right now.</summary>
        public static bool Active { get { return _source != null; } }

        /// <summary>The grid's own click / drag handlers (cells, region chrome, control squares, tabs)
        /// refuse while this is true: a carry is live, OR a press that began during the carry has not
        /// been released yet. Once the place-press is down the catcher is raycast-transparent (so hover
        /// refreshes for the resolution), which means a SECOND press before the resolve lands on the
        /// real surfaces underneath; this gate (like <c>HudSlotDrag.CanDrag</c>) keeps that press from
        /// also clicking a cell, a split square or a tab — even when its release arrives after the
        /// carry already ended.</summary>
        internal static bool BlocksGridClicks { get { return _source != null || _swallowUntilUp; } }

        /// <summary>Per frame, BEFORE (and independent of) <see cref="TheGridPanel.Tick"/>, in its own
        /// try/catch in the plugin Update.</summary>
        public static void Tick()
        {
            _lastTickFrame = Time.frameCount;
            if (_swallowUntilUp && !AnyMouseHeldOrReleasing()) _swallowUntilUp = false;
            // Idle (the steady state, and always with the opt-in off): one static compare.
            if (_source == null && !SplitToCursor.Waiting(SplitToCursor.Ctx.Grid)) return;
            bool gate = Gate();
            if (_source == null)
            {
                if (!gate) { SplitToCursor.Cancel(SplitToCursor.Ctx.Grid); return; }
                // Never start carrying in the middle of another pointer gesture (an MP child can land
                // while a button is held): the ready result waits for the pointer to be idle.
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1)
                    || BagGridCell.IsDragActive || HudSlotDrag.IsDragging) return;
                Slot slot;
                DynamicThing child;
                if (SplitToCursor.TryTake(SplitToCursor.Ctx.Grid, out slot, out child)) Begin(slot, child);
                return;
            }

            if (!gate) { Cancel(); return; }

            // A new press of any button while the place-press is already armed (the catcher is
            // transparent then): its click must never reach the grid underneath, even if it is
            // released after this carry ends (BlocksGridClicks).
            if (_pressed && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                && Time.frameCount > _pressFrame)
                _swallowUntilUp = true;

            // The child left its hand under us (teammate, despawn, our own placement round-tripped):
            // stop quietly — nothing to place any more.
            DynamicThing occ = null;
            try { occ = _source.Occupant; } catch { }
            if (occ == null || !ReferenceEquals(occ, _source.Expected)) { Cancel(); return; }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                TheGridPanel.BeginEscSwallow();   // one Esc = stop carrying, never ALSO the pause menu
                return;
            }

            // WATCHDOG: the place-press was seen but its release never reached the catcher (the app
            // lost focus, a menu stole the frame). A lost edge is an abort, never a placement. The
            // `!GetMouseButtonUp` term is load-bearing (the HudSlotDrag watchdog lesson): when our Tick
            // runs BEFORE the EventSystem on the release frame, the up has not reached the catcher
            // yet — that is a normal release, not a lost one; the next frame sees _resolveAt set.
            if (_pressed && _resolveAt < 0 && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)
                && Time.frameCount > _pressFrame + 2)
            {
                Cancel();
                return;
            }

            UpdateGhost();
            UpdateHighlight();

            if (_resolveAt >= 0 && Time.frameCount >= _resolveAt) Resolve();
        }

        /// <summary>Stop carrying WITHOUT sending anything: the child stays where it is. Idempotent;
        /// also drops this context's pending/ready child so it can never attach later.</summary>
        public static void Cancel()
        {
            try { SplitToCursor.Cancel(SplitToCursor.Ctx.Grid); } catch { }
            // Ending with a button still down (a second press mid-resolve, or a cancel during the
            // place-press): that press's release must not click the grid under it.
            if (_source != null)
            {
                try { if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) _swallowUntilUp = true; }
                catch { }
            }
            if (_source == null && _ghostGo == null && !_blockHeld
                && (_root == null || !_root.activeSelf)) return;
            bool wasCarrying = _source != null;
            _source = null;
            _pressed = false;
            _pressFrame = -999;
            _resolveAt = -1;
            SetHighlight(null);
            if (_ghostGo != null) Object.Destroy(_ghostGo);
            _ghostGo = null;
            _ghostRt = null;
            if (_catcher != null) _catcher.PassThrough = false;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            if (_blockHeld)
            {
                _blockHeld = false;
                try { CursorBlockArbiter.Release(BlockId); } catch { }
            }
            if (wasCarrying) { try { GridGhostHint.EndDrag(); } catch { } }
            _hits.Clear();
        }

        /// <summary>Hot-reload / teardown: cancel, destroy the catcher canvas and null every static.</summary>
        public static void Shutdown()
        {
            Cancel();
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _catcher = null;
            _swallowUntilUp = false;
            _lastTickFrame = -999;
            _ped = null;
            _pedEs = null;
            _hits.Clear();
        }

        // ------------------------------------------------------------------ gesture

        /// <summary>Every surface that could own the pointer instead of the Universal Inventory.</summary>
        private static bool Gate()
        {
            try
            {
                if (RadialController.AnyRadialOpen) return false;
                if (Windows.HudEditorMode.Active) return false;
                if (Guards.VanillaMenuWantsFront()) return false;
                if (Menu.UiaControlCenter.IsOpen) return false;
                if (Menu.Tutorial.TutorialCoach.IsOpen || Menu.HandbookViewer.IsOpen) return false;
                return TheGridPanel.IsInteractive
                    || (PinnedInventoryWindow.LiveCount > 0 && PinnedInventoryWindow.Interactive);
            }
            catch { return false; }
        }

        private static void Begin(Slot slot, DynamicThing child)
        {
            if (slot == null || child == null) return;
            var src = new ScannedSlot { Slot = slot, Holder = slot.Parent, Location = "" }.Pin();
            if (!ReferenceEquals(src.Expected, child)) return;   // moved between detection and now
            if (!EnsureCatcher()) return;                       // no catcher = no safe click: vanilla stands

            _source = src;
            _pressed = false;
            _pressFrame = -999;
            _resolveAt = -1;
            _catcher.PassThrough = false;
            _root.SetActive(true);
            if (!_blockHeld)
            {
                _blockHeld = true;   // vanilla's world hover stands down, like a cell drag (D-002)
                try { CursorBlockArbiter.Hold(BlockId); } catch { }
            }
            try
            {
                SpawnGhost(child);
                GridGhostHint.BeginDrag(child, slot);   // the would-receive bag glows
            }
            catch (System.Exception ex)
            {
                // A broken ghost / hint must never leave the catcher up with the world-pick hold taken.
                UIALog.Warn("Split-to-cursor: carry start failed (cancelled): " + ex.Message);
                Cancel();
                return;
            }
            try { UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash); } catch { }
        }

        /// <summary>The catcher's self-hide watchdog (<see cref="SplitCarryCatcher"/>): true when
        /// <see cref="Tick"/> has not run for a few frames (a broken plugin Update, a stand-down path
        /// that skipped the cancel). The catcher then cancels the carry rather than eat every click.</summary>
        internal static bool TickStale() { return Time.frameCount > _lastTickFrame + 5; }

        private static bool AnyMouseHeldOrReleasing()
        {
            for (int b = 0; b < 3; b++)
                if (Input.GetMouseButton(b) || Input.GetMouseButtonUp(b)) return true;
            return false;
        }

        /// <summary>The catcher got the place-press. LMB arms the placement (and turns the catcher
        /// transparent so hover state refreshes against the real surfaces); RMB cancels.</summary>
        internal static void OnCatcherDown(PointerEventData e)
        {
            if (_source == null || e == null) return;
            if (e.button == PointerEventData.InputButton.Right) { Cancel(); return; }
            if (e.button != PointerEventData.InputButton.Left) return;
            if (_pressed) return;
            _pressed = true;
            _pressFrame = Time.frameCount;
            if (_catcher != null) _catcher.PassThrough = true;
        }

        /// <summary>The place-press was released (UGUI delivers the up to the press owner — the
        /// catcher — wherever the pointer is, and fires NO click underneath). Schedule the resolution
        /// for when the EventSystem's hover state has been refreshed with the catcher transparent:
        /// its raycast runs at the START of its frame, so the frame after the press is the first one
        /// that sees through; +1 more covers either Update order between it and our Tick.</summary>
        internal static void OnCatcherUp(PointerEventData e)
        {
            if (_source == null || e == null || !_pressed) return;
            if (e.button != PointerEventData.InputButton.Left) return;
            if (_resolveAt >= 0) return;
            _resolveAt = Mathf.Max(Time.frameCount + 1, _pressFrame + 2);
        }

        /// <summary>The wheel lands on the catcher: hand it to whatever is under it (the window's
        /// ScrollRect), so carrying never stops you scrolling to the bag you want.</summary>
        internal static void OnCatcherScroll(PointerEventData e)
        {
            if (e == null || _catcher == null) return;
            GameObject under = null;
            bool was = _catcher.PassThrough;
            try
            {
                _catcher.PassThrough = true;
                var es = EventSystem.current;
                if (es != null)
                {
                    _hits.Clear();
                    es.RaycastAll(e, _hits);
                    for (int i = 0; i < _hits.Count && under == null; i++) under = _hits[i].gameObject;
                }
            }
            catch { under = null; }
            finally
            {
                _catcher.PassThrough = was;
                _hits.Clear();
            }
            if (under != null) ExecuteEvents.ExecuteHierarchy(under, e, ExecuteEvents.scrollHandler);
        }

        /// <summary>Place the child: a click on its own hand cell keeps it; anything else resolves
        /// through the grid's shared drag-release resolution. Always ends the carry.</summary>
        private static void Resolve()
        {
            var src = _source;
            try
            {
                bool allOfType = ItemActions.MoveAllOfTypeHeld();   // vanilla's "move all of this type" on the click
                Slot cellUnder = HudSlotDrag.GridCellSlotUnderCursor();
                if (src != null && !ReferenceEquals(cellUnder, src.Slot))
                    BagGridCell.ResolveCarryClick(src, allOfType);
            }
            catch (System.Exception ex)
            {
                UIALog.Warn("Split-to-cursor placement failed: " + ex.Message);
            }
            finally { Cancel(); }
        }

        // ------------------------------------------------------------------ visuals

        private static bool EnsureCatcher()
        {
            if (_root != null && _catcher != null) return true;   // Unity fake-null catches a destroyed root
            try
            {
                _root = new GameObject("UIAscended_SplitCarryCatcher");
                Object.DontDestroyOnLoad(_root);
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                _root.AddComponent<GraphicRaycaster>();

                var go = new GameObject("Catcher", typeof(RectTransform));
                go.transform.SetParent(_root.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);   // alpha 0 still raycasts (rect hit test)
                img.raycastTarget = true;
                _catcher = go.AddComponent<SplitCarryCatcher>();
                _root.SetActive(false);
                return true;
            }
            catch (System.Exception ex)
            {
                UIALog.Warn("Split-to-cursor: catcher build failed: " + ex.Message);
                if (_root != null) Object.Destroy(_root);
                _root = null;
                _catcher = null;
                return false;
            }
        }

        private static void SpawnGhost(DynamicThing child)
        {
            if (_ghostGo != null) Object.Destroy(_ghostGo);
            _ghostGo = null;
            _ghostRt = null;
            Transform host = DragGhostLayer.EnsureHost();
            if (host == null) return;                      // no ghost: the carry still works
            _ghostGo = new GameObject("SplitCarryGhost", typeof(RectTransform));
            _ghostGo.transform.SetParent(host, false);
            _ghostRt = (RectTransform)_ghostGo.transform;
            _ghostRt.anchorMin = _ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRt.pivot = new Vector2(0.5f, 0.5f);
            float size = 46f;
            try { size = UIAConfig.GridCellSize.Value; } catch { }
            size *= WheelGridScale.Geo;   // #12: match the resolution-scaled cell (BagGridCell.SpawnGhost)
            _ghostRt.sizeDelta = new Vector2(size * 0.9f, size * 0.9f);
            var img = _ghostGo.AddComponent<Image>();
            Sprite icon = null;
            try { icon = child.GetThumbnail(); } catch { }
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;                     // never occlude the pick under the cursor
            img.color = HudConfig.TintIcon(new Color(1f, 1f, 1f, icon != null ? 0.85f : 0f));
            _ghostRt.SetAsLastSibling();
            UpdateGhost();
        }

        private static void UpdateGhost()
        {
            // ScreenSpaceOverlay at scale factor 1: world position IS the screen pixel (the same math
            // the bag-grid cell ghost uses).
            if (_ghostRt != null)
                _ghostRt.position = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
        }

        /// <summary>Light the cell a click would place into (the drag's drop highlight). One UGUI pick
        /// per carry frame with the catcher seen through; never the child's own hand cell.</summary>
        private static void UpdateHighlight()
        {
            BagGridCell cell = null;
            if (_catcher != null && !_pressed)
            {
                bool was = _catcher.PassThrough;
                try
                {
                    var es = EventSystem.current;
                    if (es != null)
                    {
                        if (_ped == null || _pedEs != es) { _ped = new PointerEventData(es); _pedEs = es; }
                        _ped.position = Input.mousePosition;
                        _catcher.PassThrough = true;
                        _hits.Clear();
                        es.RaycastAll(_ped, _hits);
                        for (int i = 0; i < _hits.Count; i++)
                        {
                            var go = _hits[i].gameObject;
                            if (go == null) continue;
                            var c = go.GetComponentInParent<BagGridCell>();
                            if (c != null) { cell = c; break; }
                            break;   // top-most hit is not a cell: nothing to light
                        }
                    }
                }
                catch { cell = null; }
                finally
                {
                    _catcher.PassThrough = was;
                    _hits.Clear();
                }
            }
            if (cell != null && _source != null && ReferenceEquals(cell.Slot, _source.Slot)) cell = null;
            if (!_pressed) SetHighlight(cell);
        }

        private static void SetHighlight(BagGridCell cell)
        {
            if (ReferenceEquals(cell, _highlight)) return;
            if (_highlight != null) _highlight.SetDropHighlight(false);   // Unity fake-null: a pooled-away cell is skipped
            _highlight = cell;
            if (_highlight != null) _highlight.SetDropHighlight(true);
        }
    }

    /// <summary>The split-to-cursor click catcher (see <see cref="GridSplitCarry"/>): a full-screen
    /// invisible Image that owns the place-press, then lets raycasts through while that press resolves.
    /// Holds no state of its own beyond the pass-through bit; destroyed with its canvas on Shutdown.</summary>
    internal sealed class SplitCarryCatcher : MonoBehaviour,
        ICanvasRaycastFilter, IPointerDownHandler, IPointerUpHandler, IScrollHandler
    {
        /// <summary>True = raycasts pass through (the press is already owned).</summary>
        public bool PassThrough;

        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera) { return !PassThrough; }

        // Self-hide: a full-screen click eater must never outlive the Tick that drives it.
        private void Update()
        {
            if (GridSplitCarry.TickStale()) GridSplitCarry.Cancel();
        }

        public void OnPointerDown(PointerEventData e) { GridSplitCarry.OnCatcherDown(e); }

        public void OnPointerUp(PointerEventData e) { GridSplitCarry.OnCatcherUp(e); }

        public void OnScroll(PointerEventData e) { GridSplitCarry.OnCatcherScroll(e); }
    }
}
