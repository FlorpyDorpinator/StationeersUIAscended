using System.Collections.Generic;
using Assets.Scripts;              // CursorManager (the world-pick block we hold while hovered)
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// One container torn OUT of the Universal Inventory (<see cref="TheGridPanel"/>) into its own
    /// small window: glass <see cref="PanelGraphic"/> chrome themed exactly like the main panel
    /// (<see cref="HudPalette"/> + <see cref="HudFxMaterials"/>), a draggable title bar carrying the
    /// container's ASCII name and thumbnail, a bottom-right three-line resize grip, a restore button
    /// (vanilla's own shrink glyph via <see cref="VanillaIcons.ShrinkIcon"/>) and a plain ASCII X —
    /// both of which UNPIN the container so it returns to the main window's tree. The body is ONE
    /// <see cref="GridRegionView"/> bound to the pinned <see cref="ContainerNode"/>, so cells behave
    /// exactly as they do in the main window (click-to-hand, drag-drop, Sort, state text).
    ///
    /// <para>Pinning is VIEW-ONLY: this window mutates NO game state. Its only reachable mutations are
    /// the reused cell/region ones, which all funnel through <see cref="ItemActions"/> with the
    /// occupant re-verified at execute time. Geometry (top-left + size, px from the screen's top-left)
    /// is persisted per-save to <see cref="GridPinStore"/> on drag-END only — never per frame.</para>
    ///
    /// <para>Lifetime: every pinned window is a child of ONE shared, lazily built screen-space canvas
    /// owned by this class. <see cref="Shutdown"/> destroys that canvas (taking every window, pooled
    /// region, cell and drag handler with it) and resets every static, so a double-F6 strands nothing.
    /// A pinned window OUTLIVES the main window: B hides only <see cref="TheGridPanel"/>, and the
    /// pins keep rendering, refreshing, dragging and resizing until the vanilla close-all (backtick)
    /// or their own X / shrink button takes them down.</para>
    ///
    /// <para>Cursor: this class registers NO modal, and neither does the main window — NOTHING here
    /// ever frees the cursor, so nothing here can strand one. <see cref="Interactive"/> is driven
    /// purely by whether the PLAYER has freed the mouse (the vanilla mouse-control key), resolved by
    /// <c>TheGridPanel.ApplyInteractive</c> and pushed here every frame, independently of the main
    /// window being open. While a pinned window is interactive AND hovered we hold vanilla's
    /// <c>CursorManager.BlockCursorRaycast</c> so a click on a cell cannot also pick the world
    /// through the glass; that flag is released the moment the pointer leaves, the pins go inert, or
    /// the last window dies (<see cref="ReleaseCursorBlock"/>), and never clobbers the main window's
    /// own hold.</para>
    ///
    /// <para>Scrolling is HOVER-GATED: the viewport carries a fully transparent raycast-target Image,
    /// so UGUI delivers the wheel to this window only when the pointer is actually over its list —
    /// and, with the raycaster off while the cursor is locked, never during normal play.</para>
    ///
    /// <para>Style: every box here (panel shell, both chrome buttons) is painted by the ONE shared
    /// path <see cref="GridTheme.ApplyBox"/>, which inherits fill, border colour, border WIDTH, the
    /// corner sweep and the full glass stack from the global HUD box theme. Nothing in this file
    /// hardcodes a border width or a radius.</para>
    ///
    /// <para>Centre-pivot rule: every rect carrying a <see cref="PanelGraphic"/> (the window panel, the
    /// two buttons, the grip) keeps pivot (0.5,0.5) and is positioned by its CENTRE; a top-left ANCHOR
    /// is kept for the flow math. ASCII only in displayed strings (the X label, the shrink fallback) —
    /// the game's TMP font tofus dingbats, so the grip is DRAWN with <see cref="PolylineGraphic"/>.</para>
    /// </summary>
    public sealed class PinnedInventoryWindow : MonoBehaviour
    {
        // --- layout constants (px, screen-space overlay); deliberately the main panel's, one size down ---
        private const float Pad = 8f;
        private const float TitleH = 24f;
        private const float TitleGap = 4f;
        private const float BtnSizeFallback = 20f;   // used only before UIAConfig binds (hot reload)
        private const float BtnGap = 4f;
        private const float IconSizeFallback = 16f;   // title-icon size before UIAConfig binds (hot reload)
        private const float TitleTextFallback = 12f;  // title font size before UIAConfig binds (hot reload)
        private const float GripSize = 16f;
        private const float GripInset = 3f;
        // No radius or border-width constants: both are inherited from the global box theme through
        // GridTheme.ApplyBox (the grip's drawn strokes take GridTheme.BorderWidth, floored so a
        // near-zero global line does not make the grip invisible).
        private const float GripLineMinW = 0.8f;

        // Geometry clamps (a pinned bag window starts smaller than the main one, but resizes just as
        // large). MaxW/MaxH are deliberately larger than any real display: LoadGeometry and
        // WindowResize.OnDrag cap them against Screen.width/height, so the SCREEN is the real bound and
        // a pinned window can be dragged out to (near) full screen ("drag-huge"). Pinned geometry lives
        // in GridPinStore (a Rect, no BepInEx range gate), so nothing here snaps a stored huge size back.
        private const float MinW = 180f, MaxW = 8000f;
        private const float MinH = 120f, MaxH = 8000f;

        private const float DefaultW = 360f, DefaultH = 260f;

        /// <summary>The F9-editable chrome BUTTON size in px (<see cref="UIAConfig.GridChromeButtonSize"/>,
        /// clamped 12..40, shipped default 20) — the close X and the shrink/restore button, kept one
        /// uniform size with the main window's X (same config). Read live everywhere the old
        /// <c>BtnSize</c> const was used, so an F9 size edit re-places and re-sizes the chrome on the next
        /// <see cref="Layout"/>; falls back to 20 before the config binds (hot reload before
        /// <c>UIAConfig.Init</c>).</summary>
        private static float BtnSize()
        {
            try { if (UIAConfig.GridChromeButtonSize != null) return Mathf.Clamp(UIAConfig.GridChromeButtonSize.Value, 12f, 40f); }
            catch { }
            return BtnSizeFallback;
        }

        /// <summary>The F9-editable title ICON size in px (<see cref="UIAConfig.GridPinTitleIconSize"/>,
        /// clamped 8..32, shipped default 16) — the thumbnail on a pinned window's title bar. Read live
        /// everywhere the old <c>IconSize</c> const was used, so a size edit re-places and re-sizes the
        /// title icon on the next <see cref="LayoutChrome"/>; falls back to 16 before the config binds
        /// (hot reload before <c>UIAConfig.Init</c>).</summary>
        private static float PinTitleIconSize()
        {
            try { if (UIAConfig.GridPinTitleIconSize != null) return Mathf.Clamp(UIAConfig.GridPinTitleIconSize.Value, 8f, 32f); }
            catch { }
            return IconSizeFallback;
        }

        /// <summary>The F9-editable title TEXT size (<see cref="UIAConfig.GridPinTitleTextSize"/>, clamped
        /// 8..24, shipped default 12) — the BASE size before <see cref="HudText.Size"/> applies the global
        /// font scale. Applied at Create and re-applied (dirty-guarded) by <see cref="LayoutChrome"/> on a
        /// live edit; falls back to 12 before the config binds (hot reload before <c>UIAConfig.Init</c>).</summary>
        private static float PinTitleTextSize()
        {
            try { if (UIAConfig.GridPinTitleTextSize != null) return Mathf.Clamp(UIAConfig.GridPinTitleTextSize.Value, 8f, 24f); }
            catch { }
            return TitleTextFallback;
        }

        // ---------- shared canvas + live registry (the only statics; all reset in Shutdown) ----------

        private static GameObject _canvasRoot;
        private static Canvas _canvas;
        private static readonly List<PinnedInventoryWindow> _live = new List<PinnedInventoryWindow>(4);
        private static bool _interactive = true;

        // ---------- window focus / z-order (D-006) ----------
        // Every window's raycaster lives on its own NESTED canvas with no raycaster above it, so to
        // the EventSystem each window is its own root: RaycastComparer only compares graphic depth
        // WITHIN one rootRaycaster, and with equal sort priorities it falls through to the raycasters'
        // REGISTRATION order (RaycastResult.index) — not the sibling order that decides what DRAWS
        // on top (live UnityEngine.UI EventSystem.RaycastComparer / BaseRaycaster.rootRaycaster).
        // So where two pins overlapped, the one you could see was not necessarily the one that got
        // the click, and a pin's controls could be dead until it was dragged clear ("the bottom one
        // can't be clicked or dragged until the top one is moved away"). Fix: every window's canvas
        // overrides sorting with an explicit order in [PinSortBase, PinSortBase + PinSortSpan]
        // ranked by focus, so DRAW order and RAYCAST priority (GraphicRaycaster.sortOrderPriority =
        // canvas.sortingOrder for a screen-space-overlay canvas) are one and the same; a
        // pointer-down on a window (TickAll) raises it, like any desktop. The range sits above the
        // main window (5020) and below the tab drag ghost (5100) and every popup.
        private const int PinSortBase = 5030;
        private const int PinSortSpan = 60;
        private static int _focusStamp;   // monotonically increasing focus clock; reset in Shutdown

        // GridProfileMode.ChromeStamp() at the last TickAll. Profile-mode chrome (the chip +
        // CAPTURE strip, tab badges) is created by a structural region re-Bind, and pins outlive
        // the main window's rebuild loop — so TickAll diffs the stamp itself and re-binds each
        // live window when it moves (mode flip, assignment change, capture applied). Reset in
        // Shutdown; int.MinValue forces one harmless rebind on the first tick after a reload.
        private static int _profileStamp = int.MinValue;

        // Two-point scratch reused while building the three grip lines (SetPoints copies its input).
        private static readonly List<Vector2> _linePts = new List<Vector2>(2);

        // Vanilla's shrink glyph (VanillaIcons.ShrinkIcon) falls back to Resources.FindObjectsOfTypeAll,
        // which walks EVERY loaded object — and VanillaIcons caches only a HIT, so a miss re-walks on
        // every call. StyleChrome runs per frame off the panel's Tick, so probing there directly cost a
        // full object walk per frame per window until the Stationpedia awoke. Resolve it ONCE, shared by
        // every pinned window, and keep the retry OFF the per-frame path: a coarse unscaled-time throttle
        // with a hard probe budget, so the Tick path costs a float compare once resolved (or exhausted).
        private const float ShrinkProbeInterval = 3f;
        private const int ShrinkProbeBudget = 30;
        private static Sprite _shrinkSprite;
        private static float _nextShrinkProbe;
        private static int _shrinkProbesLeft = ShrinkProbeBudget;

        /// <summary>The shared, late-resolving vanilla shrink glyph, or null while it does not exist yet.
        /// Allocation-free and cheap on every call but the (throttled, budgeted) probe itself.</summary>
        private static Sprite ResolveShrinkSprite()
        {
            if (_shrinkSprite != null) return _shrinkSprite;
            if (_shrinkProbesLeft <= 0) return null;
            float now = Time.unscaledTime;
            if (now < _nextShrinkProbe) return null;
            _nextShrinkProbe = now + ShrinkProbeInterval;
            _shrinkProbesLeft--;
            try { _shrinkSprite = VanillaIcons.ShrinkIcon; } catch { }
            return _shrinkSprite;
        }

        /// <summary>How many pinned windows are alive right now.</summary>
        public static int LiveCount { get { return _live.Count; } }

        /// <summary>Is this screen point over ANY live pinned window's panel? Same contract as
        /// <c>TheGridPanel.HitTestWindow</c>: the pins share the Grid's un-warped
        /// ScreenSpaceOverlay canvas family, so the camera argument is null and the RAW mouse
        /// position is what the F9 editor must pass. Lets a click on a pinned window open the
        /// Grid style popup even when the main window is closed — pins outlive it by design
        /// (editor-access finding, 2026-07-20).</summary>
        public static bool HitTestAny(Vector2 screenPoint)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w == null || w._closed || w._panel == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(w._panel, screenPoint, null))
                    return true;
            }
            return false;
        }

        // Reused walk buffer for DirtyAllMeshes (allocation-free steady state; only ever filled
        // on a real theme-hash change). Cleared after every use and in Shutdown.
        private static readonly List<MaskableGraphic> _dirtyScratch = new List<MaskableGraphic>(64);

        /// <summary>Force every pinned window's glass mesh to rebuild (SetVerticesDirty). Called
        /// by <c>TheGridPanel</c> when <see cref="GridTheme.StyleHash"/> moves: globals like
        /// EdgeFeather are read INSIDE OnPopulateMesh via the -1 sentinel, so a global drag
        /// changes no per-graphic field and the dirty guards would otherwise skip the rebuild
        /// (follow-mode tracking finding, 2026-07-20). Runs only on a real theme edit.</summary>
        public static void DirtyAllMeshes()
        {
            if (_canvasRoot == null) return;
            _dirtyScratch.Clear();
            _canvasRoot.GetComponentsInChildren(true, _dirtyScratch);
            for (int i = 0; i < _dirtyScratch.Count; i++)
            {
                var g = _dirtyScratch[i];
                if (g is IGlassSurface) g.SetVerticesDirty();
            }
            _dirtyScratch.Clear();
        }

        /// <summary>Are the pinned windows CLICKABLE? Written every frame by
        /// <c>TheGridPanel.ApplyInteractive</c> from one fact only: has the player deliberately freed
        /// the mouse (the vanilla mouse-control key, or a cursor resolved free for any other vanilla
        /// reason). Deliberately NOT tied to the main window being open — a pin stays live after B
        /// hides the Universal Inventory. Neither this class nor the main panel registers a cursor
        /// modal, so nothing here can ever free (or strand) the cursor; we only READ the state.
        ///
        /// <para>Falling to false kills the raycasters underneath any gesture in flight, and Unity
        /// never delivers OnEndDrag to a component that stopped receiving events — so an in-flight
        /// item drag, window move and window resize are all cancelled on the edge.</para></summary>
        public static bool Interactive
        {
            get { return _interactive; }
            set
            {
                bool was = _interactive;
                _interactive = value;
                // A drag in flight keeps every pin's raycaster ON regardless of mouse-freed state, so
                // EventSystem.RaycastAll can always resolve the drop onto a pinned cell: an outbound
                // HUD hand/1-6 drag, a live vanilla world drag (the cursor stays LOCKED while carrying
                // a ground item), or a cell drag already in flight. Mirrors TheGridPanel.ApplyInteractive.
                bool dragInFlight = HudSlotDrag.IsDragging || DropResolver.VanillaWorldDragLive() || BagGridCell.IsDragActive;
                for (int i = 0; i < _live.Count; i++)
                {
                    var w = _live[i];
                    if (w == null) continue;
                    if (w._raycaster != null) w._raycaster.enabled = value || dragInFlight;
                    if (was && !value) w.CancelGestures();
                }
                if (was && !value)
                {
                    if (_live.Count > 0) BagGridCell.CancelActiveDrag();
                    ReleaseCursorBlock();
                }
            }
        }

        /// <summary>Force every live pin's raycaster ON for THIS frame when a drag is in flight, so a
        /// release raycast running earlier in the frame than the <see cref="Interactive"/> setter still
        /// resolves onto a pinned cell. See <c>TheGridPanel.PrimeDragRaycasters</c> for the full frame-
        /// ordering rationale. Pure raycaster writes; a no-op unless a drag is live.</summary>
        public static void PrimeDragRaycasters()
        {
            bool dragInFlight = HudSlotDrag.IsDragging || DropResolver.VanillaWorldDragLive() || BagGridCell.IsDragActive;
            if (!dragInFlight) return;
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w != null && w._raycaster != null) w._raycaster.enabled = true;
            }
        }

        // We are the ones holding vanilla's CursorManager.BlockCursorRaycast on behalf of the PINS.
        // Tracked so the flag is only ever cleared by its owner (the main window tracks its own hold
        // separately) and so no path can leave vanilla world-picking blocked.
        private static bool _blockHeld;

        /// <summary>Hold vanilla's world-pick block while the pointer is genuinely over a live,
        /// interactive pinned window, so a click meant for a cell cannot also pick the world through
        /// the glass. Re-ASSERTED every frame rather than latched once (a right-click item radial's
        /// <c>ModalScope</c> close is deferred and clears the flag on its way out). Released as soon
        /// as the pointer leaves — unless the MAIN window is itself hovered and interactive, in which
        /// case its own hold is left alone rather than clobbered. Allocation-free.</summary>
        private static void UpdateCursorBlock()
        {
            // Yield BlockCursorRaycast to an open radial (its ModalScope owns it for the whole
            // gesture) — otherwise this hover-driven updater toggles the block as the cursor crosses
            // a pinned window's edge toward a wedge, flickering vanilla's world-hover highlight. See
            // TheGridPanel.UpdateCursorBlock for the full rationale (return, do NOT release).
            if (RadialController.AnyRadialOpen) return;
            bool over = false;
            if (_interactive)
            {
                Vector2 mouse = Input.mousePosition;
                for (int i = 0; i < _live.Count; i++)
                {
                    var w = _live[i];
                    if (w == null || w._closed || w._panel == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(w._panel, mouse, null))
                    {
                        over = true;
                        break;
                    }
                }
            }

            if (!over) { ReleaseCursorBlock(); return; }
            // A live VANILLA world drag (an item picked off the ground or pulled from a world
            // container) must be free to FINISH on a pinned cell. Vanilla only dispatches
            // Drag()/DragSlot() — and thus the Core/WorldDrag prefix that funnels the drop through the
            // ItemActions/OnServer path — while InputMouse.Update runs, and that early-returns the
            // instant BlockCursorRaycast is set (InputMouse.cs:342). Holding the block over the pin
            // therefore FREEZES the drag at the window edge so the drop prefix never fires on release.
            // Hand the raycast back while a world drag is live — mirrors TheGridPanel.UpdateCursorBlock.
            if (Core.DropResolver.VanillaWorldDragLive()) { ReleaseCursorBlock(); return; }
            _blockHeld = true;
            Core.CursorBlockArbiter.Hold(BlockId);
        }

        /// <summary>Drop the world-pick block if WE are the ones holding it, leaving the main window's
        /// own hold untouched (it re-asserts its own every frame it is hovered). Called from every path
        /// that can end the pins' interactive state — inert frame, close-all, teardown — so the flag can
        /// never outlive the windows.</summary>
        private static void ReleaseCursorBlock()
        {
            if (!_blockHeld) return;
            _blockHeld = false;
            // Releases only OUR named hold — the arbiter keeps the flag up for the main window / a
            // radial if either still holds, so this can no longer strand another owner (the reason the
            // old code re-asserted every frame and still left one-frame gaps).
            Core.CursorBlockArbiter.Release(BlockId);
        }

        /// <summary>Stable arbiter hold id shared by the whole pinned-window family (the block state is
        /// static: "the cursor is over ANY live, interactive pinned window").</summary>
        private const string BlockId = "pinned";

        /// <summary>Abort this window's own move/resize gestures. Called when the raycasters go dark
        /// underneath them (the player let go of the mouse-control key mid-drag), where Unity would
        /// never deliver the matching OnEndDrag. Persists the geometry the drag had reached, so a
        /// cancelled move is not silently forgotten.</summary>
        private void CancelGestures()
        {
            bool moved = false;
            if (_titleDrag != null) moved |= _titleDrag.Cancel();
            if (_gripDrag != null) moved |= _gripDrag.Cancel();
            // With the raycaster off there is no OnPointerExit either, so a button hovered at the
            // moment the mouse was re-locked would stay accented forever.
            if (_closeBtn != null) _closeBtn.Hover = false;
            if (_shrinkBtn != null) _shrinkBtn.Hover = false;
            if (moved && !_closed) SaveGeometry();
        }

        // ---------- instance state ----------

        private RectTransform _panel;
        private PanelGraphic _panelBg;
        private TextMeshProUGUI _title;
        private Image _titleIcon;

        private RectTransform _titleBar;
        private WindowDrag _titleDrag;

        private PanelGraphic _shrinkBg;
        private PanelButton _shrinkBtn;
        private Image _shrinkIcon;

        private PanelGraphic _closeBg;
        private PanelButton _closeBtn;

        private RectTransform _grip;
        private WindowResize _gripDrag;
        private PolylineGraphic[] _gripLines;

        private RectTransform _viewport;
        private RectTransform _content;
        private ScrollRect _scroll;
        private GridScrollbar _scrollbar;   // thin rounded scroll indicator on the right edge
        private GridRegionView _region;
        private GraphicRaycaster _raycaster;

        private ContainerNode _node;
        private long _refId;
        private bool _haveContent;
        private bool _closed;

        // D-006: this window's own (nested) canvas — the one its raycaster and every graphic sit
        // under — and its focus stamp (higher = more recently focused = drawn AND hit on top).
        private Canvas _winCanvas;
        private int _focus;

        // Height of the region's own manila tab, which we SUPPRESS inside a pinned window (see
        // SuppressRegionTab). The region still lays itself out with a tab band reserved at the top, so
        // the body is shifted up by exactly this much to close the gap the hidden tab leaves.
        private float _tabInset;

        // Live geometry in px: X/Y are the window's TOP-LEFT corner from the screen's top-left.
        private float _winX, _winY, _winW = DefaultW, _winH = DefaultH;

        /// <summary>The pinned container's persistent <c>Thing.ReferenceId</c> — the key this window's
        /// pin + geometry live under in <see cref="GridPinStore"/>. 0 until bound.</summary>
        public long RefId { get { return _refId; } }

        /// <summary>The node this window currently renders (re-bound by the panel on every structural
        /// rebuild, so it never holds a stale tree). Null until bound.</summary>
        public ContainerNode Node { get { return _node; } }

        // ---------- public API ----------

        /// <summary>Build an empty pinned window (its chrome only) under the shared pin canvas, which is
        /// created on first use. Call <see cref="Bind"/> to give it a container. Returns null if the
        /// canvas could not be built.</summary>
        public static PinnedInventoryWindow Create()
        {
            EnsureCanvas();
            if (_canvasRoot == null) return null;

            var go = new GameObject("UIAscended_PinnedWindow", typeof(RectTransform));
            go.transform.SetParent(_canvasRoot.transform, false);

            var win = go.AddComponent<PinnedInventoryWindow>();
            win.Build(go);
            _live.Add(win);
            win.BringToFront();   // D-006: a freshly opened window lands on top (draw AND raycast)
            return win;
        }

        /// <summary>D-006: raise this window above every other pinned window — both what DRAWS on top
        /// and what the EventSystem hits first, which are now the same thing (see the z-order note on
        /// <see cref="PinSortBase"/>). Called for a new window, on a pointer-down anywhere on a window
        /// (<see cref="TickAll"/>), and by <c>TheGridPanel</c>'s focus-on-repeat-shortcut. View-only.</summary>
        public void BringToFront()
        {
            if (_closed) return;
            _focus = ++_focusStamp;
            ApplySortOrders();
        }

        /// <summary>Rank every live window by focus stamp and give its nested canvas an explicit,
        /// overriding sort order (lowest focus = <see cref="PinSortBase"/>). Rank, not raw stamp, so
        /// the orders stay packed inside the band however long a session runs. A handful of windows:
        /// the O(n^2) rank count is allocation-free and runs only on a focus change / open / close.</summary>
        private static void ApplySortOrders()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w == null || w._closed || w._winCanvas == null) continue;
                int rank = 0;
                for (int j = 0; j < _live.Count; j++)
                {
                    var o = _live[j];
                    if (o == null || o == w || o._closed) continue;
                    if (o._focus < w._focus || (o._focus == w._focus && j < i)) rank++;
                }
                int order = PinSortBase + Mathf.Min(rank, PinSortSpan);
                if (!w._winCanvas.overrideSorting) w._winCanvas.overrideSorting = true;
                if (w._winCanvas.sortingOrder != order) w._winCanvas.sortingOrder = order;
            }
        }

        /// <summary>The live window the player would HIT at this screen point — the highest-focus one
        /// whose panel contains it (drawn on top there, so it is also first in the raycast order).</summary>
        private static PinnedInventoryWindow TopWindowAt(Vector2 screenPoint)
        {
            PinnedInventoryWindow best = null;
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w == null || w._closed || w._panel == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(w._panel, screenPoint, null)) continue;
                if (best == null || w._focus > best._focus) best = w;
            }
            return best;
        }

        /// <summary>D-006 window focus: on the frame a mouse button goes DOWN over a pinned window,
        /// raise the window under the cursor (the one that is drawn — and therefore hit — on top there)
        /// above the others, so the one you click or start dragging comes forward and any visible part
        /// of a lower window stays clickable. Polled rather than an IPointerDownHandler on the window
        /// root: a press handler on an ancestor would make that ancestor the press target and change
        /// who receives clicks. Only while the pins are interactive and no radial owns the mouse.</summary>
        private static void RaiseWindowUnderPointerDown()
        {
            if (!_interactive || _live.Count < 2) return;
            if (!(Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))) return;
            if (RadialController.AnyRadialOpen) return;
            var w = TopWindowAt(Input.mousePosition);
            if (w == null) return;
            // Already on top? Nothing to do (keeps the stamp from climbing on every click).
            for (int i = 0; i < _live.Count; i++)
            {
                var o = _live[i];
                if (o != null && o != w && !o._closed && o._focus > w._focus) { w.BringToFront(); return; }
            }
        }

        /// <summary>Find the live pinned window for a container, or null. Linear over a handful of
        /// windows and allocation-free (no LINQ), so it is safe on the panel's per-Tick path.</summary>
        public static PinnedInventoryWindow Find(long containerRefId)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w != null && !w._closed && w._refId == containerRefId) return w;
            }
            return null;
        }

        /// <summary>Refresh every live pinned window. Pumped once per frame by <c>TheGridPanel.Tick</c>
        /// — from BEFORE its own open check, so the pins keep refreshing, dragging and resizing after B
        /// has hidden the main window. Also re-asserts the world-pick block for whichever window the
        /// pointer is over. Steady-state allocation-free; costs nothing when nothing is live.</summary>
        public static void TickAll()
        {
            if (_live.Count == 0)
            {
                ReleaseCursorBlock();   // the last window may have died mid-hover
                return;
            }
            // Profile-mode chrome follows GridProfileMode via a stamp diff (see _profileStamp):
            // a moved stamp re-binds each window's bound node, which is what creates/removes the
            // chip + CAPTURE strip inside a pin. Steady state this is one int compare.
            int stamp = GridProfileMode.ChromeStamp();
            bool rebind = stamp != _profileStamp;
            _profileStamp = stamp;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var w = _live[i];
                if (w == null || w._closed) { _live.RemoveAt(i); continue; }
                if (rebind && w._node != null) w.Bind(w._node);
                w.RefreshIfDirty();
                if (w._scrollbar != null) w._scrollbar.Tick();   // drive the scroll indicator
            }
            RaiseWindowUnderPointerDown();   // D-006: click / drag a window = bring it forward
            UpdateCursorBlock();
        }

        /// <summary>Append every live pinned window's navigable targets to the scroll-select flat list
        /// (<see cref="GridSelection.Rebuild"/>), so a nested bag torn out into its own window is still
        /// reachable by the one wheel cursor. Each window's body is a <see cref="GridRegionView"/> bound
        /// forceExpanded with its own tab suppressed, so <c>CollectNav</c> emits its cells and any nested
        /// child regions (each with its own tab) — in window order, after the main tree.</summary>
        public static void CollectNav(List<GridSelection.NavItem> list)
        {
            if (list == null) return;
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w != null && !w._closed && w._region != null && w.isActiveAndEnabled)
                    w._region.CollectNav(list);
            }
        }

        /// <summary>Re-flow every live pinned window into its current width — the pin-side of a live F9
        /// SIZE edit (cell size, icon scale, tab height, tab text, or the wrap-column counts). Unlike
        /// <see cref="TickAll"/> (restyle + dirty refresh only), this re-runs each window's LIGHT
        /// <see cref="Layout"/>, which re-wraps the cell grid and re-applies the new cell/tab metrics.
        /// Driven by <c>TheGridPanel</c>'s size-hash poll, which fires only on a real edit — never per
        /// frame — so the tree rebuild cost is not paid here.</summary>
        public static void RelayoutAll()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w == null || w._closed) continue;
                w.Layout();
            }
        }

        /// <summary>Close every live pinned window WITHOUT unpinning (the vanilla close-all bridge and
        /// the mod's own hide path): the pins survive in <see cref="GridPinStore"/> and the windows come
        /// back on the next open.</summary>
        public static void CloseAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var w = _live[i];
                if (w != null) w.Close();
            }
            _live.Clear();
            ReleaseCursorBlock();   // closed mid-hover must not leave world-picking blocked
        }

        /// <summary>Hide (or restore) EVERY pinned window at once by toggling the shared canvas root,
        /// while a vanilla blocking menu (ESC/pause, console, IC10 editor, naming window, Stationpedia,
        /// creative spawn) owns the screen. SetActive(false) fully hides the render AND kills every
        /// window's raycaster in one flip; <see cref="_live"/> and each window's geometry are LEFT
        /// UNTOUCHED, so lifting the menu restores every pin exactly as it was — hide != close.
        /// Also hands vanilla's world-pick block back so a menu can never leave world-picking blocked.
        /// Driven edge-triggered by <c>TheGridPanel.Tick</c>; a no-op before the canvas is built.</summary>
        public static void SuppressAll(bool suppress)
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(!suppress);
            ReleaseCursorBlock();   // never leave the world-pick block held behind a menu
        }

        /// <summary>Full teardown for a clean ScriptEngine hot reload: destroy the shared canvas (which
        /// takes every window, pooled region view, cell and drag handler with it) and reset every static
        /// so a double-F6 leaves nothing behind.</summary>
        public static void Shutdown()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var w = _live[i];
                if (w != null) w._closed = true;
            }
            _live.Clear();
            ReleaseCursorBlock();          // never leave vanilla world-picking blocked across F6
            if (_canvasRoot != null) Object.Destroy(_canvasRoot);
            _canvasRoot = null;
            _canvas = null;
            _interactive = true;
            _blockHeld = false;
            _focusStamp = 0;               // D-006 focus clock starts fresh after a reload
            _profileStamp = int.MinValue;
            _linePts.Clear();
            _dirtyScratch.Clear();
            _shrinkSprite = null;          // a sprite from the pre-reload scene must never survive
            _nextShrinkProbe = 0f;
            _shrinkProbesLeft = ShrinkProbeBudget;
        }

        /// <summary>Bind (or re-bind) this window to a pinned container's node: title, thumbnail, and the
        /// one region view. The FIRST bind also restores the window's remembered geometry from
        /// <see cref="GridPinStore"/> (falling back to a centred default). Structural only — the panel
        /// calls this when its signature changed, never per frame.</summary>
        public void Bind(ContainerNode node)
        {
            if (_closed || _panel == null) return;

            bool first = _node == null;
            _node = node;
            _refId = node != null ? node.RefId : 0L;

            if (first) LoadGeometry();

            HudText.Sync(_title);
            HudText.Set(_title, node != null ? Ascii(node.Title) : "");

            Sprite thumb = null;
            if (node != null && node.Container != null)
            {
                try { thumb = node.Container.GetThumbnail(); } catch { }
            }
            _titleIcon.sprite = thumb;
            _titleIcon.enabled = thumb != null;
            _titleIcon.color = Color.white;

            if (node == null)
            {
                _region.Recycle();
                _tabInset = 0f;
                _haveContent = false;
            }
            else
            {
                _region.Bind(node, 0, forceExpanded: true);   // a pinned window always shows its bag open (its tab is suppressed)
                SuppressRegionTab();
                _region.Rect.anchoredPosition = new Vector2(0f, _tabInset);
                _haveContent = true;
            }

            Layout();
        }

        /// <summary>LIGHT re-layout with NO tree rebuild: re-place the chrome at the current window size
        /// and re-flow the bound region into the new content width (which re-wraps its cell grid and
        /// picks up a changed F10 cell size). Cheap enough to run every frame of a resize drag.</summary>
        public void Layout()
        {
            if (_closed || _panel == null) return;
            LayoutChrome();
            if (!_haveContent || _region == null) return;
            // Keep the suppressed-tab compensation in sync with a live F9 tab-HEIGHT edit: the region
            // still reserves the (hidden) tab band at its top, so the body must be shifted up by the
            // CURRENT band height, not the one captured at the last bind (SuppressRegionTab).
            _tabInset = _region.TabBandHeight;
            float innerW = _viewport.sizeDelta.x;
            _region.Rect.anchoredPosition = new Vector2(0f, _tabInset);
            float h = _region.Layout(innerW);
            _content.sizeDelta = new Vector2(innerW, Mathf.Max(0f, h - _tabInset));
        }

        /// <summary>Hide the region's OWN manila tab while it is hosted inside a pinned window. The
        /// window's title bar already names (and moves) the container, so the region tab is redundant —
        /// and, being a live drag-out handle, it invites a re-pin of an already-pinned bag. Deactivating
        /// the tab GameObject removes its raycast surface outright; the region still reserves the tab
        /// band in its layout, so <see cref="_tabInset"/> records the height and Layout shifts the body
        /// up by it. Only <c>GridRegionView.Bind</c> re-activates the tab, so this runs right after each
        /// bind — never per frame. The tab is a DIRECT child of the region rect, so a non-recursive
        /// Find can never reach a nested child region's tab.</summary>
        private void SuppressRegionTab()
        {
            _tabInset = 0f;
            if (_region == null || _region.Rect == null) return;
            var t = _region.Rect.Find("GridTab");
            if (t == null) return;
            var tab = t.GetComponent<GridTab>();
            if (tab != null) _tabInset = tab.Height;
            if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
        }

        /// <summary>Per-frame content refresh: restyle the chrome to the live palette (so an F9 palette
        /// edit and hover states show without a rebuild) and let the region refresh its own visible
        /// cells' dirty keys. Steady-state allocation-free.</summary>
        public void RefreshIfDirty()
        {
            if (_closed || _panel == null) return;
            StyleChrome();
            if (_region != null) _region.RefreshIfDirty();
        }

        /// <summary>Destroy this window. Does NOT touch the pin record — un-pinning is the shrink/X
        /// button's job (<see cref="Unpin"/>), so a close-all can never silently forget a pin.
        /// Idempotent.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _live.Remove(this);
            if (_live.Count == 0) ReleaseCursorBlock();   // last window down, possibly mid-hover
            else ApplySortOrders();                       // D-006: repack the remaining z-order band
            if (_region != null) _region.Recycle();
            // Every chrome graphic that can carry a shared effect material must be handed back
            // before the destroy, or its dead key sits in HudFxMaterials' assignment table until
            // the next full Shutdown — and pins close/reopen all session long.
            if (_panelBg != null) HudFxMaterials.Unassign(_panelBg);
            if (_closeBg != null) HudFxMaterials.Unassign(_closeBg);
            if (_shrinkBg != null) HudFxMaterials.Unassign(_shrinkBg);
            _scrollbar = null;   // plain PanelGraphics (no shared FX material); dies with the canvas
            if (gameObject != null) Object.Destroy(gameObject);
        }

        /// <summary>The restore (shrink) and X buttons: drop the pin — the container leaves this window
        /// and reappears inside the Universal Inventory on the panel's next signature-driven rebuild
        /// (<see cref="GridPinStore.PinVersion"/> moves, which the panel folds into its signature) —
        /// then destroy the window. Mutates NO game state.</summary>
        private void Unpin()
        {
            long id = _refId;
            Close();
            if (id != 0L) GridPinStore.Unpin(id);
        }

        // ---------- geometry ----------

        /// <summary>Pull this pin's remembered geometry out of <see cref="GridPinStore"/> and clamp it to
        /// the CURRENT screen (self-healing a window stranded off-screen by a resolution change). An
        /// unknown pin gets a centred default.</summary>
        private void LoadGeometry()
        {
            Rect g = new Rect(-1f, -1f, DefaultW, DefaultH);   // "never placed" sentinel: centre it
            if (_refId != 0L)
            {
                Rect stored;
                if (GridPinStore.TryGetGeom(_refId, out stored)) g = stored;
            }

            float w = g.width, h = g.height, x = g.x, y = g.y;
            _winW = Mathf.Clamp(float.IsNaN(w) ? DefaultW : w, MinW, Mathf.Max(MinW, Mathf.Min(MaxW, Screen.width)));
            _winH = Mathf.Clamp(float.IsNaN(h) ? DefaultH : h, MinH, Mathf.Max(MinH, Mathf.Min(MaxH, Screen.height)));

            if (x < 0f || y < 0f || float.IsNaN(x) || float.IsNaN(y))
            {
                x = (Screen.width - _winW) * 0.5f;
                y = (Screen.height - _winH) * 0.5f;
            }
            _winX = x;
            _winY = y;
            ClampToScreen();
        }

        /// <summary>Place this window's top-left corner (px from the screen's top-left), clamped
        /// on-screen, and re-lay the chrome. The spawn path (a tab dragged out of the main window) uses
        /// this to drop the window at the cursor; it does NOT persist — the caller pins the geometry.</summary>
        public void SetPosition(float x, float y)
        {
            _winX = x;
            _winY = y;
            ClampToScreen();
            Layout();
        }

        /// <summary>This window's live geometry (top-left + size, px from the screen's top-left) — what
        /// the caller stores in <see cref="GridPinStore"/> when it pins the container.</summary>
        public Rect Geometry { get { return new Rect(_winX, _winY, _winW, _winH); } }

        private void ClampToScreen()
        {
            float maxX = Mathf.Max(0f, Screen.width - _winW);
            float maxY = Mathf.Max(0f, Screen.height - _winH);
            _winX = Mathf.Clamp(_winX, 0f, maxX);
            _winY = Mathf.Clamp(_winY, 0f, maxY);
        }

        /// <summary>Persist the live geometry into the pin record. Called on drag-END only (a per-frame
        /// XML write would hammer the disk); <see cref="GridPinStore.SetGeom"/> itself no-ops when the
        /// geometry did not actually move.</summary>
        private void SaveGeometry()
        {
            if (_refId == 0L) return;
            GridPinStore.SetGeom(_refId, new Rect(_winX, _winY, _winW, _winH));
        }

        // ---------- build ----------

        /// <summary>The shared screen-space canvas every pinned window parents to. Sorted just ABOVE the
        /// main Universal Inventory window so a pinned bag is never buried under it; it carries no
        /// raycaster of its own (each window owns one, so peek/latched can be flipped per window).
        /// Since D-006 each window's own nested canvas OVERRIDES this order with its focus rank
        /// (<see cref="ApplySortOrders"/>) — the root order is only the band's floor.</summary>
        private static void EnsureCanvas()
        {
            if (_canvasRoot != null) return;
            _canvasRoot = new GameObject("UIAscended_GridPins");
            Object.DontDestroyOnLoad(_canvasRoot);
            _canvas = _canvasRoot.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5030;   // TheGridPanel is 5020
            // The pin shells ride the analytic SDF renderer when the HUD boxes do (GridTheme.
            // ApplyCore); its per-panel contract lives in the extra vertex streams, which UGUI
            // silently drops unless the Canvas opts in — see TheGridPanel.EnsureBuilt for the
            // full rationale. Harmless for the mesh-path graphics.
            _canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
        }

        private void Build(GameObject host)
        {
            var hostRt = (RectTransform)host.transform;
            hostRt.anchorMin = Vector2.zero;
            hostRt.anchorMax = Vector2.one;
            hostRt.offsetMin = Vector2.zero;
            hostRt.offsetMax = Vector2.zero;

            _raycaster = host.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = _interactive;

            // GraphicRaycaster's [RequireComponent(typeof(Canvas))] auto-adds a NESTED Canvas on this
            // child `host`. A nested canvas is its own batch root and honours its OWN
            // additionalShaderChannels — so it SILENTLY DROPS the SDF panel's uv1/uv2/uv3/normal/tangent
            // streams (the analytic radius/border/frost/superellipse param block PanelGraphic packs
            // there), and the `sdfglass` shader then reads an undefined param block and paints the whole
            // shell a solid garbage colour (play-test 2026-07-21: every pinned window rendered bright
            // yellow, main window fine). EnsureCanvas set the channels on _canvasRoot, but _panelBg lives
            // under this nested canvas — so re-assert the same five channels HERE. The main window and
            // the capture/profile popups avoid this by putting Canvas+raycaster on ONE GameObject.
            var winCanvas = host.GetComponent<Canvas>();
            if (winCanvas != null)
                winCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                    | AdditionalCanvasShaderChannels.TexCoord2
                    | AdditionalCanvasShaderChannels.TexCoord3
                    | AdditionalCanvasShaderChannels.Normal
                    | AdditionalCanvasShaderChannels.Tangent;
            // D-006: this nested canvas gets an explicit, overriding sort order per focus rank
            // (ApplySortOrders) — seeded at the band base so it never flashes under the main window
            // before Create's BringToFront runs.
            _winCanvas = winCanvas;
            if (_winCanvas != null)
            {
                _winCanvas.overrideSorting = true;
                _winCanvas.sortingOrder = PinSortBase;
            }

            // The window panel: glass background, a raycast target so clicks on empty panel area do not
            // fall through to the world. Anchored TOP-LEFT, pivoted CENTRE (the centre-pivot rule).
            var pGo = new GameObject("Panel", typeof(RectTransform));
            pGo.transform.SetParent(hostRt, false);
            _panel = (RectTransform)pGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panelBg = pGo.AddComponent<PanelGraphic>();
            _panelBg.raycastTarget = true;

            var iGo = new GameObject("TitleIcon", typeof(RectTransform));
            iGo.transform.SetParent(_panel, false);
            _titleIcon = iGo.AddComponent<Image>();
            _titleIcon.raycastTarget = false;
            _titleIcon.preserveAspect = true;
            _titleIcon.enabled = false;
            var irt = _titleIcon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot = new Vector2(0.5f, 0.5f);

            _title = HudText.Make(_panel, "Title", HudText.Size(PinTitleTextSize()),
                TextAlignmentOptions.Left, warp: false);
            AnchorTopLeft(_title.rectTransform);

            BuildTitleBar();
            BuildButtons();
            BuildScroll();
            BuildGrip();
        }

        /// <summary>The invisible move handle over the title band. A fully transparent
        /// <see cref="Image"/> (alpha 0 still raycasts — UGUI hit-tests the rect, not the pixel) carries
        /// the drag; it is built BEFORE the two buttons so they, later siblings, win the raycast where
        /// they would meet.</summary>
        private void BuildTitleBar()
        {
            var tGo = new GameObject("TitleBar", typeof(RectTransform));
            tGo.transform.SetParent(_panel, false);
            _titleBar = (RectTransform)tGo.transform;
            AnchorTopLeft(_titleBar);
            _titleBar.pivot = new Vector2(0.5f, 0.5f);

            var hit = tGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            _titleDrag = tGo.AddComponent<WindowDrag>();
            _titleDrag.Owner = this;
        }

        /// <summary>The restore (shrink) button and the close X, both at the panel's top-right. The
        /// restore button wears VANILLA's own shrink sprite (grabbed at runtime, never bundled); until
        /// the Stationpedia exists that sprite is null, so the button falls back to an ASCII "-" label
        /// and <see cref="ResolveShrinkSprite"/> retries on its coarse throttle (never per frame — the
        /// underlying lookup walks every loaded object).</summary>
        private void BuildButtons()
        {
            _shrinkBtn = MakeButton("Restore", "-", out _shrinkBg);
            _shrinkBtn.Clicked = Unpin;

            // The sprite sits ON TOP of the ASCII fallback label; whichever is available is enabled.
            var sGo = new GameObject("ShrinkIcon", typeof(RectTransform));
            sGo.transform.SetParent(_shrinkBtn.transform, false);
            _shrinkIcon = sGo.AddComponent<Image>();
            _shrinkIcon.raycastTarget = false;
            _shrinkIcon.preserveAspect = true;
            _shrinkIcon.enabled = false;
            var srt = _shrinkIcon.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(BtnSize() - 6f, BtnSize() - 6f);   // live F9 size; LayoutChrome re-fits

            _closeBtn = MakeButton("Close", "X", out _closeBg);
            _closeBtn.Clicked = Unpin;
        }

        /// <summary>One small glass chrome button: a centre-pivot <see cref="PanelGraphic"/> box, a
        /// centred ASCII label (never a dingbat — the game's TMP font tofus them) and the click
        /// surface.</summary>
        private PanelButton MakeButton(string name, string asciiLabel, out PanelGraphic bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_panel, false);
            var rt = (RectTransform)go.transform;
            AnchorTopLeft(rt);
            rt.pivot = new Vector2(0.5f, 0.5f);   // centre pivot: PanelGraphic draws centred on the origin

            bg = go.AddComponent<PanelGraphic>();
            bg.raycastTarget = true;

            var btn = go.AddComponent<PanelButton>();

            var lbl = HudText.Make(rt, "Label", HudText.Size(12f),
                TextAlignmentOptions.Center, warp: false);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(BtnSize(), BtnSize());
            HudText.Set(lbl, asciiLabel);
            btn.Label = lbl;
            return btn;
        }

        /// <summary>The bottom-right resize grip: a transparent hit rect carrying three short DRAWN
        /// diagonal strokes (the notepad-grip look the main panel uses). Drawn, never a glyph — the
        /// game's TMP font tofus box-drawing characters. The lines are not raycast targets, so only the
        /// hit rect starts a resize.</summary>
        private void BuildGrip()
        {
            var gGo = new GameObject("ResizeGrip", typeof(RectTransform));
            gGo.transform.SetParent(_panel, false);
            _grip = (RectTransform)gGo.transform;
            AnchorTopLeft(_grip);
            _grip.pivot = new Vector2(0.5f, 0.5f);   // centre pivot: the lines are drawn about the origin

            var hit = gGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            _gripDrag = gGo.AddComponent<WindowResize>();
            _gripDrag.Owner = this;

            float h = GripSize * 0.5f;
            float[] len = { GripSize * 0.30f, GripSize * 0.56f, GripSize * 0.82f };
            _gripLines = new PolylineGraphic[len.Length];
            for (int i = 0; i < len.Length; i++)
            {
                var lGo = new GameObject("GripLine" + i.ToString(), typeof(RectTransform));
                lGo.transform.SetParent(_grip, false);
                var lrt = (RectTransform)lGo.transform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = Vector2.zero;
                lrt.sizeDelta = new Vector2(GripSize, GripSize);

                var line = lGo.AddComponent<PolylineGraphic>();
                line.raycastTarget = false;
                line.Width = Mathf.Max(GripLineMinW, GridTheme.BorderWidth);   // re-asserted every StyleChrome

                _linePts.Clear();
                _linePts.Add(new Vector2(h - len[i], -h));   // on the bottom edge
                _linePts.Add(new Vector2(h, -h + len[i]));   // on the right edge
                line.SetPoints(_linePts, false);

                _gripLines[i] = line;
            }
            _linePts.Clear();
        }

        private void BuildScroll()
        {
            var vGo = new GameObject("Viewport", typeof(RectTransform));
            vGo.transform.SetParent(_panel, false);
            _viewport = (RectTransform)vGo.transform;
            AnchorTopLeft(_viewport);
            vGo.AddComponent<RectMask2D>();

            // THE SCROLL HIT TARGET. UGUI dispatches the wheel (IScrollHandler) and a drag by walking
            // UP from whatever the raycast HIT, so a viewport carrying only a RectMask2D receives
            // nothing over empty list area — the wheel then only worked when the pointer happened to
            // sit on a cell. A fully transparent raycast-target Image filling the viewport gives the
            // whole list a hit, so the wheel scrolls anywhere over it and empty space can start a
            // drag-scroll. Built BEFORE the Content so it is the FIRST sibling: behind every cell
            // (cells still win their own clicks) and, living on the viewport rather than the content,
            // it never scrolls away. It also HOVER-GATES the wheel for free — no hit, no event — so
            // each pinned window scrolls only itself and only while the pointer is over it, and (with
            // the raycaster off unless the mouse is freed) never steals the wheel from gameplay.
            var hGo = new GameObject("ScrollHit", typeof(RectTransform));
            hGo.transform.SetParent(_viewport, false);
            var hrt = (RectTransform)hGo.transform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;
            var scrollHit = hGo.AddComponent<Image>();
            scrollHit.color = new Color(0f, 0f, 0f, 0f);   // alpha 0 still raycasts: UGUI hit-tests the rect
            scrollHit.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(_viewport, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _scroll = vGo.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 28f;
            _scroll.inertia = false;

            _region = GridRegionView.Create(_content);

            // Thin rounded scroll indicator on the right edge (auto-hides when the list fits).
            _scrollbar = GridScrollbar.Create(_scroll, _viewport, _content);
        }

        private static void AnchorTopLeft(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        /// <summary>Size + place the panel, title icon/text, drag strip, the two buttons, the grip and
        /// the scroll viewport from the live geometry. Cheap (pure RectTransform writes plus
        /// dirty-guarded shape sets); runs on bind and every frame of a resize drag.</summary>
        private void LayoutChrome()
        {
            float panelW = _winW;
            float panelH = _winH;

            // Anchored to the canvas TOP-LEFT, positioned by its CENTRE (the centre-pivot rule):
            // the stored X/Y is the window's top-left corner in px from the screen's top-left.
            _panel.anchoredPosition = new Vector2(_winX + panelW * 0.5f, -(_winY + panelH * 0.5f));
            _panel.sizeDelta = new Vector2(panelW, panelH);
            // No SetShape here: the corner sweep is part of the inherited box theme, so the ONE place
            // it is issued is GridTheme.ApplyBox, from StyleChrome (called at the end of this method
            // and every Tick) — exactly as the main window does it.

            // Two buttons at the top-right: [restore][X], right-aligned (centre pivot). Live F9 size,
            // read once and applied to both buttons (and the shrink glyph, which fills the button).
            float btn = BtnSize();
            float btnRight = panelW - Pad;
            var closeRt = (RectTransform)_closeBtn.transform;
            closeRt.anchoredPosition = new Vector2(btnRight - btn * 0.5f, -Pad - TitleH * 0.5f);
            closeRt.sizeDelta = new Vector2(btn, btn);

            var shrinkRt = (RectTransform)_shrinkBtn.transform;
            shrinkRt.anchoredPosition = new Vector2(btnRight - btn - BtnGap - btn * 0.5f,
                                                    -Pad - TitleH * 0.5f);
            shrinkRt.sizeDelta = new Vector2(btn, btn);
            if (_shrinkIcon != null)
                _shrinkIcon.rectTransform.sizeDelta = new Vector2(btn - 6f, btn - 6f);
            if (_closeBtn != null && _closeBtn.Label != null)
                _closeBtn.Label.rectTransform.sizeDelta = new Vector2(btn, btn);
            if (_shrinkBtn != null && _shrinkBtn.Label != null)
                _shrinkBtn.Label.rectTransform.sizeDelta = new Vector2(btn, btn);

            // Title icon (centre pivot) then the name, both inside the title band. Live F9 icon size,
            // read once and applied to the icon rect; the title font is re-fit below.
            float titleIcon = PinTitleIconSize();
            float iconX = Pad + titleIcon * 0.5f;
            _titleIcon.rectTransform.anchoredPosition = new Vector2(iconX, -Pad - TitleH * 0.5f);
            _titleIcon.rectTransform.sizeDelta = new Vector2(titleIcon, titleIcon);

            // Re-apply the live title FONT size (dirty-guarded): a live F9 edit changes no structural
            // signature, so Bind never re-runs — LayoutChrome (reached via RelayoutAll) carries it, the
            // same way the tab name is re-fit through GridTab.SyncTextSize. Costs one float compare when
            // unchanged; only writes (regenerating the TMP mesh) on a real edit.
            if (_title != null)
            {
                float fs = HudText.Size(PinTitleTextSize());
                if (!Mathf.Approximately(_title.fontSize, fs)) _title.fontSize = fs;
            }

            float titleLeft = iconX + titleIcon * 0.5f + 4f;
            float titleRight = btnRight - 2f * btn - BtnGap - 4f;
            float titleW = Mathf.Max(20f, titleRight - titleLeft);
            _title.rectTransform.anchoredPosition = new Vector2(titleLeft, -Pad);
            _title.rectTransform.sizeDelta = new Vector2(titleW, TitleH);

            // The move handle spans the title band, stopping short of the buttons (which are also later
            // siblings, so they win the raycast regardless).
            float barW = Mathf.Max(20f, titleRight - Pad);
            _titleBar.anchoredPosition = new Vector2(Pad + barW * 0.5f, -(Pad + TitleH * 0.5f));
            _titleBar.sizeDelta = new Vector2(barW, TitleH);

            // Resize grip, tucked into the bottom-right corner (centre pivot).
            _grip.anchoredPosition = new Vector2(panelW - GripInset - GripSize * 0.5f,
                                                 -(panelH - GripInset - GripSize * 0.5f));
            _grip.sizeDelta = new Vector2(GripSize, GripSize);

            float vpX = Pad;
            float vpY = Pad + TitleH + TitleGap;
            float vpW = Mathf.Max(30f, panelW - Pad * 2f);
            float vpH = Mathf.Max(30f, panelH - vpY - Pad);
            _viewport.anchoredPosition = new Vector2(vpX, -vpY);
            _viewport.sizeDelta = new Vector2(vpW, vpH);

            StyleChrome();
        }

        /// <summary>Paint this window's chrome (panel shell, title, both buttons, grip) through the ONE
        /// shared styling path, <see cref="GridTheme.ApplyBox"/>: fill, border colour, border WIDTH, the
        /// corner sweep and the same glass/frost stack a HUD box gets, all inherited from the global box
        /// theme (or the Grid's overrides) — so a pinned window is skinned identically to the main one
        /// and to a HUD box. NOTHING here hardcodes a width or a radius; the hover states are the
        /// helper's own delta on the inherited line, so a thin global border stays thin. Runs every
        /// frame so a live F9 edit and the hover states show without a rebuild; every setter downstream
        /// is dirty-guarded, so an unchanged frame costs no mesh rebuild and allocates nothing.</summary>
        private void StyleChrome()
        {
            if (_panelBg == null) return;

            Color text = GridTheme.Text;

            // The shell: the same surface variant the main window uses, so both take the glow halo and
            // the frosted backdrop and read as one family.
            GridTheme.ApplyBox(_panelBg, _winW, _winH, GridTheme.GridSurface.Window, false);

            if (_title != null) _title.color = text;

            StyleButton(_closeBtn, _closeBg, text);
            StyleButton(_shrinkBtn, _shrinkBg, text);

            // Late-resolve vanilla's shrink glyph: the Stationpedia singleton only exists once its UI
            // awakes in-world, so a miss is retried (VanillaIcons caches only a HIT). Until then the
            // ASCII "-" fallback label carries the button.
            if (_shrinkIcon != null && _shrinkIcon.sprite == null)
            {
                Sprite s = ResolveShrinkSprite();
                if (s != null)
                {
                    _shrinkIcon.sprite = s;
                    _shrinkIcon.enabled = true;
                    if (_shrinkBtn != null && _shrinkBtn.Label != null)
                        _shrinkBtn.Label.enabled = false;   // the sprite replaces the fallback label
                }
            }
            if (_shrinkIcon != null && _shrinkIcon.enabled && _shrinkBtn != null)
                _shrinkIcon.color = _shrinkBtn.Hover ? HudPalette.LineAccent.Value : text;

            if (_gripLines != null)
            {
                bool gHover = _gripDrag != null && (_gripDrag.Hover || _gripDrag.Dragging);
                Color gc = gHover ? HudPalette.LineAccent.Value : GridTheme.Border;
                // The grip strokes are drawn ICONOGRAPHY, not a box outline (there is no PanelGraphic
                // to hand ApplyBox), but they still inherit their weight and colour from the same
                // theme, so a thin global line reads thin here too. Floored so a near-zero global
                // border cannot make the grip vanish.
                float gw = Mathf.Max(GripLineMinW, GridTheme.BorderWidth);
                for (int i = 0; i < _gripLines.Length; i++)
                {
                    var ln = _gripLines[i];
                    if (ln == null) continue;
                    ln.color = gc;
                    ln.Width = gw;
                }
            }
        }

        /// <summary>One chrome button, painted by the shared theme path. The hover ACCENT is an
        /// interaction affordance rather than part of the box theme, so it is passed to
        /// <see cref="GridTheme.ApplyBox"/> as its hover DELTA (accent colour + a scaled line) on the
        /// inherited values — never an absolute width.</summary>
        private static void StyleButton(PanelButton btn, PanelGraphic bg, Color text)
        {
            if (bg == null) return;
            bool hover = btn != null && btn.Hover;
            float sz = BtnSize();
            GridTheme.ApplyBox(bg, sz, sz, GridTheme.GridSurface.Button, hover);
            var lbl = btn != null ? btn.Label : null;
            if (lbl != null) lbl.color = hover ? HudPalette.LineAccent.Value : text;
        }

        /// <summary>Strip anything outside Basic Latin from a displayed name: the game's TMP font renders
        /// ASCII reliably and tofus the rest, and a container's display name is data we do not control.</summary>
        private static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < ' ' || s[i] > '~') { clean = false; break; }
            }
            if (clean) return s;                       // the common case allocates nothing
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c >= ' ' && c <= '~' ? c : ' ');
            }
            return sb.ToString();
        }

        // ---------- window move / resize ----------

        /// <summary>Title-bar move handle. Records the pointer and the window's top-left at drag start
        /// and drives the position from the ABSOLUTE pointer delta (per-frame deltas drift), clamped
        /// on-screen every frame. Nothing resizes, so only the panel's anchoredPosition moves; the
        /// geometry is persisted once, on drag END.</summary>
        private sealed class WindowDrag : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public PinnedInventoryWindow Owner;

            private Vector2 _grabPointer;
            private float _grabX, _grabY;
            private bool _active;

            public void OnBeginDrag(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (Owner == null || Owner._closed || !_interactive) return;
                _active = true;
                _grabPointer = e.position;
                _grabX = Owner._winX;
                _grabY = Owner._winY;
            }

            public void OnDrag(PointerEventData e)
            {
                if (!_active || e == null || Owner == null || Owner._closed) return;
                // Screen space: +x is right and +y is UP, while our stored Y grows DOWNWARD.
                Owner._winX = _grabX + (e.position.x - _grabPointer.x);
                Owner._winY = _grabY - (e.position.y - _grabPointer.y);
                Owner.ClampToScreen();
                Owner.LayoutChrome();
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (!_active) return;
                _active = false;
                if (Owner != null && !Owner._closed) Owner.SaveGeometry();
            }

            /// <summary>Abandon a move in flight (the raycaster went dark underneath it, so Unity will
            /// never deliver OnEndDrag). The window keeps wherever the drag had reached; true asks the
            /// owner to persist it, since the normal save point is gone.</summary>
            public bool Cancel()
            {
                if (!_active) return false;
                _active = false;
                return true;
            }
        }

        /// <summary>Bottom-right resize grip. The window's top-left stays pinned, so the drag maps
        /// straight onto width/height (clamped to this window's ranges and to the screen). Each frame
        /// runs the LIGHT <see cref="Layout"/> — chrome plus one region layout, which re-wraps the cell
        /// grid into the new width — never a tree rebuild. Geometry is persisted once, on drag END.</summary>
        private sealed class WindowResize : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler,
            IPointerEnterHandler, IPointerExitHandler
        {
            public PinnedInventoryWindow Owner;

            private Vector2 _grabPointer;
            private float _grabW, _grabH;

            public bool Hover;
            public bool Dragging;

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }

            public void OnBeginDrag(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (Owner == null || Owner._closed || !_interactive) return;
                Dragging = true;
                _grabPointer = e.position;
                _grabW = Owner._winW;
                _grabH = Owner._winH;
            }

            public void OnDrag(PointerEventData e)
            {
                if (!Dragging || e == null || Owner == null || Owner._closed) return;
                float maxW = Mathf.Max(MinW, Mathf.Min(MaxW, Screen.width - Owner._winX));
                float maxH = Mathf.Max(MinH, Mathf.Min(MaxH, Screen.height - Owner._winY));
                Owner._winW = Mathf.Clamp(_grabW + (e.position.x - _grabPointer.x), MinW, maxW);
                Owner._winH = Mathf.Clamp(_grabH - (e.position.y - _grabPointer.y), MinH, maxH);
                Owner.Layout();
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (!Dragging) return;
                Dragging = false;
                if (Owner != null && !Owner._closed) Owner.SaveGeometry();
            }

            /// <summary>Abandon a resize in flight (see <see cref="WindowDrag.Cancel"/>). Also drops the
            /// hover flag: with the raycaster off there is no OnPointerExit either, so the grip would
            /// otherwise stay accented.</summary>
            public bool Cancel()
            {
                Hover = false;
                if (!Dragging) return false;
                Dragging = false;
                return true;
            }
        }

        // ---------- buttons ----------

        /// <summary>Left-click surface for a chrome button. Records hover so <see cref="StyleChrome"/>
        /// promotes the accent on the next pass; the action is a method group set once at build (no
        /// per-frame delegate churn, nothing to unsubscribe).</summary>
        private sealed class PanelButton : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public System.Action Clicked;
            public bool Hover;
            public TextMeshProUGUI Label;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (Clicked != null) Clicked();
            }

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }
        }
    }
}
