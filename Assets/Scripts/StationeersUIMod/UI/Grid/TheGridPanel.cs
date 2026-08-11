using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.UI;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The "Universal Inventory" window (code name: The Grid) — the top-level singleton overlay that
    /// shows EVERY inventory slot the local player has (worn equipment, both hands, and every container
    /// nested inside them, recursively). Its own screen-space canvas carries a glass background
    /// <see cref="PanelGraphic"/>, a title, an X close button, a draggable title bar, a bottom-right
    /// resize grip, and a scrollable stack rendering the whole tree through a single root
    /// <see cref="GridRegionView"/> (the flat, packed grid of bordered container regions).
    ///
    /// <para>The renderer is ALWAYS <see cref="GridRegionView"/>. The old nested-tree renderer
    /// (<see cref="BagGridView"/>) is retained but deprecated and unreachable: no UI builds,
    /// activates, or writes it any more. Its <c>UIAConfig.GridMode</c> config entry (zero readers)
    /// was itself removed in 0.9.2.5's config-migration cleanup.</para>
    ///
    /// <para>Geometry (position + size) is user-driven and PERSISTED: dragging the title bar moves the
    /// window, dragging the bottom-right grip (drawn as three short diagonal lines, never a glyph)
    /// resizes it, and both write <see cref="UIAConfig.GridWinX"/>/<c>Y</c>/<c>W</c>/<c>H</c> on
    /// drag-END (never per frame). The panel is anchored to the canvas TOP-LEFT and positioned by its
    /// centre (the centre-pivot rule every PanelGraphic rect follows). A resize drag calls
    /// <see cref="Relayout"/> every frame — a LIGHT pass (chrome + one region Layout into the new
    /// content width, which is what re-wraps the cell grid) with NO tree rebuild.</para>
    ///
    /// <para>The only game-state mutations reachable from this window are per-container sort, click-to-hand,
    /// and cell-to-cell drag-drop — all through <see cref="Core.ItemActions"/>, MP-safe funnels with the
    /// occupant re-verified at execute time. This panel itself reads only networked state (occupant
    /// identity + a structural signature) and hosts the drag ghost overlay for the cells.</para>
    ///
    /// <para>Change tracking is signature-diff, not event-subscription-per-slot: each <see cref="Tick"/>
    /// compares <see cref="GridModel.ComputeSignature"/> to the last build; a structural change (insert /
    /// remove / swap / collapse-toggle) rebuilds the tree, otherwise visible cells refresh their own
    /// icon/text dirty key. The one event we hook is <see cref="InventoryWindowManager.OnUIClose"/> so
    /// the vanilla close-all (backtick) closes the window in lockstep; it is unhooked in
    /// <see cref="Shutdown"/> along with canvas destruction and every static reset (double-F6 safe).</para>
    ///
    /// <para>PINNING. This panel OWNS the pinned-window collection. A container the player tore out
    /// (dragging its <see cref="GridTab"/> past the panel rect, or an entry point calling
    /// <see cref="PinContainer"/>) is recorded per-save in <see cref="GridPinStore"/> and is EXCLUDED from
    /// this window's tree: every structural <see cref="Rebuild"/> prunes pinned nodes out of the freshly
    /// built <see cref="ContainerNode"/> tree (recursively, so a pin nested inside a pin still gets its own
    /// window) and hands each pruned node to its <see cref="PinnedInventoryWindow"/>. Pin/unpin moves
    /// <see cref="GridPinStore.PinVersion"/>, which <see cref="Tick"/> diffs alongside the structural
    /// signature, so the tree rebuilds the moment the pin set changes. Pinning mutates NO game state.</para>
    ///
    /// <para>PIN LIFETIME. Pinned windows do NOT die with this one: <see cref="Hide"/> (the Grid key)
    /// leaves them visible, interactive and ticking — <see cref="Tick"/> pumps
    /// <see cref="PinnedInventoryWindow.TickAll"/> before its own open check. The vanilla close-all
    /// (backtick, <see cref="HandleUIClose"/>) is the single gesture that closes main + pins together,
    /// and even that only CLOSES: the pin records survive, so the next open restores the windows. A
    /// pin's own X / shrink button is what truly unpins. <see cref="Shutdown"/> destroys their shared
    /// canvas outright.</para>
    ///
    /// <para>CURSOR. This window registers NO modal and never frees the mouse: opening it leaves the
    /// cursor LOCKED and head-look working, so the player can open the inventory and just look at it
    /// while still playing. Clicking is enabled only while the player deliberately frees the mouse
    /// with the vanilla mouse-control key (<c>MouseModeController.AltKeyDown</c>) or the cursor is
    /// resolved free for some other vanilla reason — <see cref="ApplyInteractive"/> gates the
    /// <see cref="GraphicRaycaster"/> on exactly that, for pinned windows too. The only cursor state
    /// we touch is <c>BlockCursorRaycast</c>, re-asserted while the pointer is over the panel and
    /// released by every exit path, so nothing here can strand an unlocked cursor.</para>
    ///
    /// <para>STYLE + F9 EDITABILITY. Every colour and glass value the chrome paints comes from
    /// <see cref="GridTheme"/>'s resolved getters (follow-the-global-box-theme by default, per-value
    /// overrides otherwise), and <see cref="Tick"/> polls <see cref="GridTheme.StyleHash"/> so an edit
    /// re-skins the live window without a rebuild. The window is NOT a HUD document element — it is a
    /// modal, raycasting, un-warped, on-demand overlay on its own canvas — so it is edited through the
    /// <c>UiaControlCenter</c> ADAPTER pattern instead: <see cref="HitTestWindow"/> lets the F9 editor
    /// claim a click on the window as a selection, and while <c>HudEditorMode.Active</c> the window drops
    /// into EDIT PREVIEW (see <see cref="ApplyEditPreview"/>) — raycaster off — so it
    /// becomes a live-themed static surface the editor's own clicks pass straight through to.</para>
    /// </summary>
    public static class TheGridPanel
    {
        /// <summary>The window's user-facing name. Code identifiers stay "Grid" on purpose (config keys,
        /// class names) so renaming the UI orphans nothing.</summary>
        public const string WindowTitle = "Universal Inventory";

        // --- layout constants (px, screen-space overlay) ---
        private const float Pad = 12f;
        private const float TitleH = 34f;
        private const float TitleGap = 6f;
        private const float CloseSizeFallback = 20f;   // used only before UIAConfig binds (hot reload)
        private const float ProfBtnW = 26f;     // the profile-mode (luggage-tag) header button

        /// <summary>The F9-editable close/chrome BUTTON size in px (<see cref="UIAConfig.GridChromeButtonSize"/>,
        /// clamped 12..40, shipped default 20) — the header X here, and (same config) the pinned windows'
        /// X + shrink/restore, kept one uniform size so the windows read as one family. The profile-mode
        /// header button's HEIGHT follows this too (its width stays <see cref="ProfBtnW"/>). Read live so
        /// an F9 size edit re-places and re-sizes the chrome on the next relayout; falls back to 20 before
        /// the config binds.</summary>
        private static float CloseSize()
        {
            try { if (UIAConfig.GridChromeButtonSize != null) return Mathf.Clamp(UIAConfig.GridChromeButtonSize.Value, 12f, 40f); }
            catch { }
            return CloseSizeFallback;
        }
        private const float GripSize = 18f;     // hit box of the bottom-right resize grip
        private const float GripInset = 3f;     // gap from the panel's bottom-right corner

        // --- geometry clamps (mirror the UIAConfig AcceptableValueRanges) ---
        // MaxW/MaxH are deliberately larger than any real display: every clamp path below caps them
        // against Screen.width/height (LoadGeometry, ClampToScreen, WindowResize.OnDrag), so the
        // SCREEN is the real bound and the window can be dragged out to (near) full screen ("drag-huge").
        // The UIAConfig GridWinW/GridWinH AcceptableValueRanges match these so a stored huge size never
        // snaps back on load.
        private const float MinW = 360f, MaxW = 8000f;
        private const float MinH = 300f, MaxH = 8000f;

        // --- pinned-window spawn defaults (match PinnedInventoryWindow's own defaults) ---
        private const float PinW = 360f, PinH = 260f;
        private const float PinGrabX = 24f;   // where the cursor lands inside the spawned title bar
        private const float PinGrabY = 12f;
        private const float PinCascade = 26f; // offset per already-open pin so entry-point pins don't stack

        private static GameObject _root;
        private static Canvas _canvas;

        private static RectTransform _panel;
        private static PanelGraphic _panelBg;
        private static TextMeshProUGUI _title;

        private static PanelGraphic _closeBg;
        private static PanelButton _closeBtn;

        // Profile-mode header toggle (design O4a): a drawn luggage-tag glyph, latched-accent while
        // the mode is on. Its state lives in GridProfileMode; this is only the button.
        private static PanelGraphic _profBg;
        private static PanelButton _profBtn;
        private static PolygonPanelGraphic _profGlyph;
        private static readonly List<Vector2> _tagPts = new List<Vector2>(5);
        private static int _profileStamp = int.MinValue;   // GridProfileMode.ChromeStamp at last build

        private static RectTransform _titleBar;   // invisible drag strip over the title text
        private static WindowDrag _titleDrag;

        private static RectTransform _grip;       // bottom-right resize handle
        private static WindowResize _gripDrag;
        private static PolylineGraphic[] _gripLines;

        private static RectTransform _overlay;    // top-most, non-raycast host for cell drag ghosts

        private static RectTransform _viewport;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static GridScrollbar _scrollbar;   // thin rounded scroll indicator on the right edge
        private static GridRegionView _rootRegion;  // the ONE renderer (flat packed grid of regions)
        private static GraphicRaycaster _raycaster;

        // Live geometry in px: X/Y are the TOP-LEFT corner measured from the screen's top-left.
        private static float _winX, _winY, _winW = 560f, _winH = 760f;
        private static bool _geomLoaded;

        private static bool _open;
        private static bool _interactive;   // EFFECTIVE state: open AND the player has freed the mouse
        private static bool _editPreview;   // open behind the active F9 editor = the editor owns clicks
        private static bool _blockHeld;     // we are the one holding CursorManager.BlockCursorRaycast
        private static bool _hooked;
        private static bool _menuHidden;    // whole window (main + pins) hidden UNDER a vanilla blocking menu
        private static long _signature;
        private static bool _haveSignature;
        private static bool _haveContent;   // true once the region renderer holds a bound tree
        private static int _styleHash;      // GridTheme.StyleHash() at the last shape/glass pass
        private static bool _haveStyleHash;
        private static int _sizeHash;       // hash of the F9-editable SIZE configs at the last relayout
        private static bool _haveSizeHash;

        // Two-point scratch reused while building the three grip lines (SetPoints copies).
        private static readonly List<Vector2> _linePts = new List<Vector2>(2);

        // --- pinning ---
        // Nodes pruned OUT of the main tree by the last Rebuild, in pin order of discovery: one per
        // pinned container that is still reachable in the live inventory. Rebuilt structurally (never
        // per frame) and handed straight to the matching PinnedInventoryWindow.
        private static readonly List<ContainerNode> _pinNodes = new List<ContainerNode>(4);
        // Reused id scratch for reconciling live windows against the pin store (no per-Tick alloc).
        private static readonly List<long> _pinScratch = new List<long>(4);
        private static int _pinVersion;

        /// <summary>True while the window is visible. Opening NEVER frees the cursor.</summary>
        public static bool IsOpen { get { return _open; } }

        /// <summary>True only while the window is open AND the player has deliberately freed the
        /// mouse (the vanilla mouse-control key, or any other vanilla state that unlocked the
        /// cursor) AND the F9 editor is not up. This is the gate every click/drag surface in the
        /// Grid checks, and it drives the <see cref="GraphicRaycaster"/>.</summary>
        public static bool IsInteractive { get { return _open && _interactive; } }

        /// <summary>Top-most, non-raycast overlay layer inside this window's canvas — the host a cell's
        /// floating drag ghost can parent to so it draws above the panel and blocks no drop raycast.
        /// Null until the window has been built. (Cells that parent to the ROOT canvas transform and
        /// <c>SetAsLastSibling</c> land on the same canvas and behave identically.)</summary>
        public static RectTransform GhostHost { get { return _overlay; } }

        /// <summary>True while the window is showing as an F9 EDIT PREVIEW: visible and live-themed,
        /// but with its raycaster off, so the editor owns every click.</summary>
        public static bool IsEditPreview { get { return _open && _editPreview; } }

        /// <summary>Is this screen point over the window panel? The Grid's canvas is
        /// ScreenSpaceOverlay, so the camera argument is null (the <c>UiaControlCenter.HitTestWindow</c>
        /// contract). Fed the RAW mouse position by the F9 editor — this window is UN-WARPED, so it must
        /// NOT be hit-tested with the inverse-warped point the HUD elements use. False when closed or
        /// not yet built.</summary>
        public static bool HitTestWindow(Vector2 screenPoint)
        {
            if (!_open || _panel == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPoint, null);
        }

        // ---------- public API ----------

        /// <summary>Flip the overlay open/closed (the tap gesture's target).</summary>
        public static void Toggle()
        {
            if (_open) Hide();
            else Show(true);
        }

        /// <summary>Open the window. Retained under its old name for the callers in
        /// <c>StationeersUIMod</c>: there is no latched-vs-peek split any more — opening never
        /// frees the cursor, so every open is the same open.</summary>
        public static void ShowLatched() { Show(true); }

        /// <summary>Alias of <see cref="ShowLatched"/>. The old read-only "peek" is what EVERY open
        /// now is until the player frees the mouse, so the two gestures have converged.</summary>
        public static void ShowPeek() { Show(true); }

        /// <summary>Open the overlay and build its first frame from the live inventory. The cursor
        /// stays LOCKED and head-look keeps working: this window registers no modal of any kind
        /// (see <see cref="ApplyInteractive"/>). <paramref name="interactive"/> is ignored — kept
        /// only so the existing call sites compile unchanged.</summary>
        public static void Show(bool interactive)
        {
            EnsureBuilt();
            if (_canvas == null) return;
            _open = true;
            _menuHidden = false;   // a fresh open re-evaluates the menu gate on the next Tick
            // Opened while the F9 editor is up = edit preview from frame one, so the "open F9, then
            // press B" flow leaves every click to the editor. Set BEFORE ApplyInteractive.
            _editPreview = EditorActive();
            ApplyInteractive();
            if (!_root.activeSelf) _root.SetActive(true);
            // Set the wheel mode BEFORE the first frame so the ScrollRect's own free-pan and the
            // scroll-select cursor never both consume the wheel (see GridSelection.ApplyScrollMode).
            GridSelection.ApplyScrollMode(_scroll);
            LoadGeometry();           // re-clamp against the CURRENT resolution every open
            _haveSignature = false;   // force a structural rebuild on the next Tick
            Rebuild();
        }

        /// <summary>No-op kept for the hold-to-peek call site: there is nothing to promote now that
        /// opening never took the cursor. Interactivity is decided every Tick by whether the player
        /// has the mouse freed.</summary>
        public static void Promote()
        {
            if (!_open) return;
            ApplyInteractive();
        }

        /// <summary>Hide the MAIN window only (keeps the canvas + pooled views for the next open).
        /// Pinned windows are deliberately LEFT ALIVE and interactive — the B key closes this window,
        /// not the pins — so neither <see cref="_pinNodes"/> nor the pin windows are touched here.
        /// The one gesture that takes the pins down with it is the vanilla close-all
        /// (<see cref="HandleUIClose"/>, the backtick); each pin's own X / shrink button unpins it.
        /// Releases the cursor-raycast block so a hidden window can never leave vanilla world-picking
        /// blocked.</summary>
        public static void Hide()
        {
            _open = false;
            _menuHidden = false;   // a stand-down must never strand the menu-hidden flag
            // Drop the scroll-select cursor + its flat nav list: the next open starts fresh, and the
            // list must not hold refs to cells/tabs that a hide-then-reopen re-pools.
            GridSelection.Clear();
            // Unity never delivers OnEndDrag to an inactive component, so hiding mid-drag would strand
            // the ghost and leave the source cell dimmed forever. Cancel before the root goes inactive.
            BagGridCell.CancelActiveDrag();
            // Closing the window EXITS profile mode (design O4a) and takes its transient UI down.
            // The mode flip bumps GridProfileMode.Version, so still-visible pinned windows drop
            // their strips on their next TickAll.
            GridProfileMode.SetActive(false);
            GridProfilePopup.Close();
            GridCapturePanel.Close();
            _editPreview = false;   // the next open re-evaluates against the editor's state
            ApplyInteractive();     // main raycaster off; pins keep whatever the mouse state says
            ReleaseCursorBlock();
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        /// <summary>LIGHT re-layout with NO tree rebuild: re-place the chrome at the current window size
        /// and re-flow the bound region into the new content width (which is what re-wraps the cell grid
        /// and picks up a changed F10 cell size). Safe and cheap enough to call every frame of a resize
        /// drag, and the hook the F10 cell-size slider uses to update an open window live.</summary>
        public static void Relayout()
        {
            if (_canvas == null || _panel == null) return;
            LayoutChrome();
            if (!_haveContent || _rootRegion == null) return;
            float innerW = _viewport.sizeDelta.x;
            _rootRegion.Rect.anchoredPosition = Vector2.zero;
            float h = _rootRegion.Layout(innerW);
            _content.sizeDelta = new Vector2(innerW, h);
        }

        // ---------- interactivity (NO cursor modal — the window never frees the mouse) ----------

        /// <summary>Has the player got the mouse? True while the vanilla mouse-control key is held
        /// (<c>MouseModeController.AltKeyDown</c> = <c>KeyManager.GetButton(KeyMap.MouseControl)</c>,
        /// the branch of <c>MouseModeController.Check</c> that forces the free cursor) or while the
        /// cursor is RESOLVED free for any other vanilla reason. Read-only: we never ask for the
        /// cursor ourselves, so nothing here can strand it.</summary>
        private static bool MouseFreed()
        {
            try { if (MouseModeController.AltKeyDown) return true; }
            catch { }
            return Cursor.lockState != CursorLockMode.Locked && Cursor.visible;
        }

        /// <summary>Drive the raycaster and the pinned windows from the EFFECTIVE state. The window
        /// is CLICKABLE only while the player has deliberately freed the mouse and the F9 editor is
        /// not up; otherwise it is a look-at-only surface and gameplay input is untouched. Pinned
        /// windows follow the same rule INDEPENDENTLY of this window being open, so they stay live
        /// after B closes the main window. The single place any of these is written, so they can
        /// never disagree. Three bool writes — safe every frame.</summary>
        private static void ApplyInteractive()
        {
            bool free = MouseFreed() && !EditorActive();
            bool was = _interactive;
            _interactive = _open && free && !_editPreview;
            // Releasing the mouse-control key mid-drag kills the raycaster underneath the gesture, so
            // Unity would never deliver OnEndDrag — the ghost and the dimmed source cell would strand.
            if (was && !_interactive) BagGridCell.CancelActiveDrag();
            // Keep the raycaster ALIVE for the whole of any drag even when the mouse is not otherwise
            // freed, so EventSystem.RaycastAll can always land the drop on a cell: an outbound HUD
            // hand/1-6 drag (HudSlotDrag.IsDragging), a live vanilla world drag (the cursor stays
            // LOCKED while an item is carried off the ground), or a cell drag already in flight.
            bool dragInFlight = HudSlotDrag.IsDragging || Core.DropResolver.VanillaWorldDragLive() || BagGridCell.IsDragActive;
            if (_raycaster != null) _raycaster.enabled = _interactive || dragInFlight;
            // Pinned windows register NO modal of their own either.
            PinnedInventoryWindow.Interactive = free;
            if (!_interactive) ReleaseCursorBlock();
        }

        /// <summary>Force the main + pinned window raycasters ON for THIS frame when a drag is in flight,
        /// so a release raycast that runs EARLIER in the frame than <see cref="ApplyInteractive"/> still
        /// lands on a cell. The bug: <see cref="ApplyInteractive"/> (the only writer of the raycaster
        /// enable) runs from <c>TheGridPanel.Tick</c> — AFTER <c>HudSlotDrag.Release</c>, which resolves
        /// its drop via <c>EventSystem.RaycastAll</c> from <c>HudSystem.Update</c> earlier in the same
        /// frame. So a hand/1-6 flick-drop onto a pinned cell saw a one-frame-stale, still-DISABLED pin
        /// raycaster (the cursor was locked last frame → not interactive → raycaster off) and missed.
        /// Priming here — called by the drop resolver just before its raycast — closes that gap. Pure
        /// raycaster writes; none of <see cref="ApplyInteractive"/>'s cursor-block / cancel side effects,
        /// and a no-op unless a drag is actually live.</summary>
        public static void PrimeDragRaycasters()
        {
            bool dragInFlight = HudSlotDrag.IsDragging || Core.DropResolver.VanillaWorldDragLive() || BagGridCell.IsDragActive;
            if (!dragInFlight) return;
            if (_raycaster != null && _open) _raycaster.enabled = true;
            PinnedInventoryWindow.PrimeDragRaycasters();
        }

        /// <summary>Enter/leave EDIT PREVIEW — the <c>UiaControlCenter.ApplyEditPreview</c> pattern.
        /// In preview the window yields all input to the F9 editor (raycaster off) and is just a
        /// live-themed surface the editor can click to select and restyle.</summary>
        private static void ApplyEditPreview(bool on)
        {
            if (_editPreview == on) return;
            _editPreview = on;
            // A drag in flight when the editor opens would otherwise never see its OnEndDrag (the
            // raycaster dies underneath it), stranding the ghost and the dimmed source cell.
            if (on) BagGridCell.CancelActiveDrag();
            ApplyInteractive();
        }

        /// <summary>Is the F9 HUD editor up? Wrapped so this file has ONE reference to the editor and
        /// a missing/failed editor can never keep the window from opening.</summary>
        private static bool EditorActive()
        {
            try { return Windows.HudEditorMode.Active; }
            catch { return false; }
        }

        /// <summary>Block vanilla's world-pick raycast while the pointer is genuinely over this
        /// window and we are interactive, so a click meant for a cell can't also hit the world
        /// through the glass. Re-ASSERTED every frame rather than latched once: a right-click item
        /// radial's <c>ModalScope</c> close is DEFERRED and clears the flag on its way out, long
        /// after any one-shot set of ours. Idempotent plain bool store — costs nothing.</summary>
        private static void UpdateCursorBlock()
        {
            // Yield BlockCursorRaycast ownership to an open radial. While a radial is up its
            // ModalScope holds the block for the whole gesture (set once in Open, released on Close),
            // but MouseFreed() cannot tell the modal's cursor-unlock from the player freeing the
            // mouse, so _interactive stays true and this hover-driven updater would toggle the block
            // on/off as the cursor crosses the panel edge toward a wedge — flickering vanilla's
            // world-hover selection highlight (play-test 2026-07-22, "cursor flickers moving to a
            // wedge"). RETURN — do NOT ReleaseCursorBlock: releasing would hand the flag away from
            // ModalScope and re-open the world raycast. Normal hover behaviour resumes the frame the
            // radial closes (_blockHeld is idempotent). Cell/keyboard actions already ignore input
            // while a radial is open, so leaving the raycaster enabled is harmless.
            if (RadialController.AnyRadialOpen) return;
            if (!_interactive || _panel == null) { ReleaseCursorBlock(); return; }
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, null))
            {
                ReleaseCursorBlock();
                return;
            }
            // A live VANILLA world drag (an item picked up off the ground or pulled from a world
            // container) must be allowed to FINISH on a grid cell. Vanilla only dispatches
            // Drag()/DragSlot() — and thus the Core/WorldDrag prefix that routes the drop into the
            // ItemActions funnel — while InputMouse.Update runs, and that early-returns the instant
            // BlockCursorRaycast is set (InputMouse.cs:342). Holding the block over the panel therefore
            // FREEZES the drag and strands the item mid-air. So while a world drag is live, hand the
            // raycast back: the WorldDrag prefix is Priority.First and owns the release before vanilla
            // can resolve its through-the-panel physics WorldSlot, so the drop stays exactly one message.
            if (Core.DropResolver.VanillaWorldDragLive()) { ReleaseCursorBlock(); return; }
            _blockHeld = true;
            Core.CursorBlockArbiter.Hold(BlockId);
        }

        /// <summary>Drop the cursor-raycast block if WE are the ones holding it. Called from every
        /// path that can end the interactive state (hide, non-interactive frame, teardown) so the
        /// flag can never outlive the window.</summary>
        private static void ReleaseCursorBlock()
        {
            if (!_blockHeld) return;
            _blockHeld = false;
            Core.CursorBlockArbiter.Release(BlockId);
        }

        /// <summary>Stable arbiter hold id for the main Universal Inventory window.</summary>
        private const string BlockId = "grid";

        // ---- Esc consumption (the ModalScope deferred-release idiom, for one key only) ----
        // Vanilla binds Escape (KeyMap._Cancel) at key-UP to the pause menu, while our Esc chain
        // acts on key-DOWN. When a chain link consumes the press, we push a Typing input state
        // that starves the game's key-up binding, and hold it until Escape has been physically
        // up for two frames — releasing on the up frame itself could still hand that very
        // key-up to the game, depending on update order (the exact hazard Core/ModalScope
        // documents for the radials). Held for the duration of one key press only.

        private const string EscSwallowStateKey = "UIA_GridEsc";
        private static bool _escSwallowHeld;
        private static int _escSwallowClearFrames;

        /// <summary>The Esc chain consumed this press: starve vanilla's Escape key-up until the
        /// key is released. Idempotent.</summary>
        private static void BeginEscSwallow()
        {
            if (_escSwallowHeld) return;
            _escSwallowHeld = true;
            _escSwallowClearFrames = 0;
            try { KeyManager.SetInputState(EscSwallowStateKey, KeyInputState.Typing); } catch { }
        }

        /// <summary>Per-frame: complete a pending release once Escape has been up two frames.</summary>
        private static void PumpEscSwallow()
        {
            if (!_escSwallowHeld) return;
            if (Input.GetKey(KeyCode.Escape)) { _escSwallowClearFrames = 0; return; }
            if (++_escSwallowClearFrames < 2) return;
            ReleaseEscSwallow();
        }

        /// <summary>Unconditional release (also from Shutdown — a held input state must never
        /// survive a hot reload).</summary>
        private static void ReleaseEscSwallow()
        {
            _escSwallowClearFrames = 0;
            if (!_escSwallowHeld) return;
            _escSwallowHeld = false;
            try { KeyManager.RemoveInputState(EscSwallowStateKey); } catch { }
        }

        /// <summary>Pumped every frame from the mod's Update while the window is open. Rebuilds the tree
        /// only when the structural signature changed (insert/remove/swap/collapse) or the PIN SET moved
        /// (<see cref="GridPinStore.PinVersion"/> — a pin/unpin changes which containers this window may
        /// render); otherwise refreshes the visible cells' own dirty keys and every pinned window's.
        /// Steady-state allocation-free.</summary>
        public static void Tick()
        {
            // Finish any deferred Esc release FIRST — it must complete even if the window (or
            // profile mode) closed on the very press that armed it.
            PumpEscSwallow();

            // Hide the WHOLE inventory (main window + every pinned window) UNDER a vanilla blocking
            // menu — the ESC/pause menu (incl. on an MP CLIENT, where it does not pause the world, so
            // Guards now ORs InventoryManager.InGameMenuOpen), the console, the IC10 editor, naming
            // windows, Stationpedia and the creative spawn menu. Edge-triggered on _menuHidden:
            // SetActive(false) fully hides the render AND kills the raycaster, unlike a mere
            // sortingOrder drop (an active raycaster at a lower order would keep stealing clicks where
            // it does not overlap the menu). Hide != close — _open, every pin, each scroll position and
            // all geometry are left INTACT, so lifting the menu returns the window exactly as it was.
            // The F9 editor is EXCLUDED (mirrors HudSystem.cs:959) so the open-F9 -> B -> click-to-theme
            // flow stays visible.
            bool vanillaFront = !EditorActive() && Core.Guards.VanillaMenuWantsFront();
            if (vanillaFront != _menuHidden)
            {
                _menuHidden = vanillaFront;
                if (vanillaFront)
                {
                    BagGridCell.CancelActiveDrag();   // a drag mid-hide would never see its OnEndDrag
                    ReleaseCursorBlock();             // never leave vanilla world-picking blocked
                    if (_root != null) _root.SetActive(false);
                    PinnedInventoryWindow.SuppressAll(true);
                }
                else
                {
                    if (_root != null) _root.SetActive(_open);   // restore to exactly the prior state
                    PinnedInventoryWindow.SuppressAll(false);
                }
            }
            if (vanillaFront) return;   // skip the whole Tick body (pin pump + open check) while suppressed

            // Per-tier skin (0.9.2.5): publish the tier the HUD is currently showing so GridTheme's
            // bare overrides can resolve. Set BEFORE the style-hash polls below — the slot folds
            // into StyleHash, so a suit-power flip repaints an already-open window (and every
            // pinned one, which polls the same hash). Base whenever the HUD is unavailable, so a
            // main-menu / stand-down Grid never strands on the bare skin.
            var snap = HudSystem.LastSnapshot;
            GridTheme.Slot = (snap != null && snap.Valid && snap.Tier == HudTier.Bare)
                ? HudStyleSlot.Bare : HudStyleSlot.Base;

            // Pinned windows outlive the main window (B closes only this one), so their pump, the
            // interactivity gate AND the theme-hash poll run BEFORE the open check — a global
            // theme drag must repaint a pins-only screen too. All no-ops when nothing is live.
            if (!_open)
            {
                if (PinnedInventoryWindow.LiveCount > 0)
                {
                    ApplyInteractive();
                    PollStyleHash(mainOpen: false);
                    PollSizeHash(mainOpen: false);   // a live F9 size edit must reflow the pins too
                    GridGhostHint.Tick();   // a drag can live on a pinned window's cells too
                    PinnedInventoryWindow.TickAll();
                }
                return;
            }
            if (_canvas == null) return;

            // Follow the F9 editor in and out of edit preview while the window stays open (the user
            // pressed F9 with the Grid already up, or closed the editor with it still up).
            ApplyEditPreview(EditorActive());

            // Clicks land only while the player has deliberately freed the mouse; re-resolved every
            // frame because that is a held key, not a latched state.
            ApplyInteractive();

            // Esc chain for the profile-mode UI (design O4a: "Esc exits the mode"): the capture
            // confirm first, then the chip popup, then the mode itself. When a link CONSUMES the
            // press, the vanilla key-UP binding (KeyMap._Cancel -> pause menu) is starved via a
            // deferred-release Typing input state (see BeginEscSwallow) so one press can never
            // both close our UI and open the pause menu on top. With nothing to consume, vanilla
            // sees Esc completely untouched.
            //
            // Skipped entirely while the tutorial coach or the Handbook viewer is open: either one
            // sits ABOVE this panel and owns Escape itself while modal, so letting this chain also
            // react would let a single press act on both layers at once.
            if (Input.GetKeyDown(KeyCode.Escape)
                && !Menu.Tutorial.TutorialCoach.IsOpen && !Menu.HandbookViewer.IsOpen)
            {
                if (GridCapturePanel.IsNameInputFocused)
                {
                    // TMP's own built-in Esc handling deselects the field; swallow the key-up
                    // so the pause menu doesn't ALSO react, and leave the dialog itself alone.
                    BeginEscSwallow();
                }
                else if (GridCapturePanel.IsOpen) { GridCapturePanel.EscClose(); BeginEscSwallow(); }
                else if (GridProfilePopup.IsOpen) { GridProfilePopup.Close(); BeginEscSwallow(); }
                else if (GridProfileMode.Active) { GridProfileMode.SetActive(false); BeginEscSwallow(); }
            }

            PollStyleHash(mainOpen: true);
            PollSizeHash(mainOpen: true);   // live F9 "Sizes" edits relayout the open window (main + pins)

            UpdateCursorBlock();

            // The profile-mode transients (both are cheap no-ops while closed): auto-close rules,
            // live theme, their own world-pick holds.
            GridProfilePopup.Tick();
            GridCapturePanel.Tick();

            // Ghost routing hints (O4d): throttled router dry-run while an item drag is live in
            // profile mode. One bool read when idle.
            GridGhostHint.Tick();

            // Scroll-select + keyboard nav (#4): wheel moves the cursor, F acts on it. GridSelection
            // gates ITSELF by mouse regime — it drives the cursor only while the cursor is CAPTURED
            // (normal play, the vanilla-inventory-scroll regime, with vanilla's own nav suppressed via
            // Core.InventoryNavPatches) and free-pans the window once the mouse is freed — so it is
            // ticked EVERY open frame, not just when interactive. Runs BEFORE the signature check so an
            // F-driven collapse toggle rebuilds on this very frame.
            GridSelection.Tick(_scroll);

            // Drive the scroll indicator from the live scroll/content each frame (cheap; auto-hides).
            if (_scrollbar != null) _scrollbar.Tick();

            long sig = GridModel.ComputeSignature();
            int pinVer = GridPinStore.PinVersion;
            int profStamp = GridProfileMode.ChromeStamp();
            if (!_haveSignature || sig != _signature || pinVer != _pinVersion || profStamp != _profileStamp)
            {
                Rebuild();
                PinnedInventoryWindow.TickAll();   // a rebuild must not cost the pins a frame
                return;
            }
            StyleChrome();
            if (_rootRegion != null) _rootRegion.RefreshIfDirty();
            PinnedInventoryWindow.TickAll();
        }

        /// <summary>Restyle when the theme actually moved — a follow-mode palette drag and an
        /// override edit both change <see cref="GridTheme.StyleHash"/>. StyleChrome runs every
        /// frame anyway (hover states), so this gate exists to catch the SHAPE (a corner-radius
        /// change resizes no rect; LayoutChrome is the only other thing that re-issues SetShape)
        /// AND to force a mesh rebuild: globals like EdgeFeather are read INSIDE OnPopulateMesh
        /// via the -1 sentinel, so a global Theme-tab drag changes no per-graphic field and the
        /// dirty guards would otherwise leave every Grid mesh stale while the HUD boxes rebuild
        /// (HudSystem.LayoutHash folds EdgeFeather and dirties ITS canvas only — the Grid lives
        /// on its own; follow-mode tracking finding, 2026-07-20). Steady-state cost is the hash
        /// itself; the dirty walk runs only on a real theme edit.</summary>
        private static void PollStyleHash(bool mainOpen)
        {
            int sh = GridTheme.StyleHash();
            if (_haveStyleHash && sh == _styleHash) return;
            bool first = !_haveStyleHash;
            _styleHash = sh;
            _haveStyleHash = true;
            if (mainOpen) LayoutChrome();       // re-place + re-shape + StyleChrome
            if (!first) DirtyAllMeshes();       // a fresh build is already fully rebuilt
        }

        /// <summary>Relayout when a live F9 SIZE edit moved — cell size, item icon scale, tab height,
        /// tab text size, tab icon size, sort-button scale, chrome-button size, or the wrap-column counts
        /// (<see cref="UIAConfig.GridCellSize"/> / <c>GridIconScale</c> / <c>GridTabHeight</c> /
        /// <c>GridTabTextSize</c> / <c>GridTabIconSize</c> / <c>GridSortButtonScale</c> /
        /// <c>GridChromeButtonSize</c> / <c>GridPinTitleIconSize</c> / <c>GridPinTitleTextSize</c> /
        /// <c>GridCellCols</c> / <c>GridMaxBagCols</c>). The F9 "Sizes"
        /// popup writes those configs with NULL callbacks, so —
        /// exactly like the F10 cell-size slider — the open window must re-flow itself: these are LAYOUT
        /// values (cell/icon dimensions, tab band, wrap columns) that <see cref="PollStyleHash"/>
        /// (restyle only) does not pick up. A light <see cref="Relayout"/> is enough because Layout
        /// re-reads every size config and re-applies the cell/tab metrics; no tree rebuild is needed.
        /// Steady-state cost is the hash itself; a Relayout runs only on a real edit. Called from BOTH
        /// Tick branches (the pins outlive the main window), like the style-hash poll.</summary>
        private static void PollSizeHash(bool mainOpen)
        {
            int sh = SizeHash();
            if (_haveSizeHash && sh == _sizeHash) return;
            bool first = !_haveSizeHash;
            _sizeHash = sh;
            _haveSizeHash = true;
            if (first) return;   // baseline the first observation; a fresh open already laid out
            if (mainOpen) Relayout();
            PinnedInventoryWindow.RelayoutAll();
        }

        /// <summary>Fold the F9-editable size configs into one change signal (quantised so a slider's
        /// sub-pixel jitter still moves it). Cheap null-guarded reads; polled every Tick.</summary>
        private static int SizeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridCellSize, 46f) * 16f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridIconScale, 0.70f) * 100f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridTabHeight, 20f) * 16f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridTabTextSize, 11f) * 16f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridTabIconSize, 14f) * 16f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridSortButtonScale, 1f) * 100f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridChromeButtonSize, 20f) * 16f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridPinTitleIconSize, 16f) * 16f);
                h = h * 31 + Mathf.RoundToInt(CfgF(UIAConfig.GridPinTitleTextSize, 12f) * 16f);
                h = h * 31 + CfgI(UIAConfig.GridCellCols, 5);
                h = h * 31 + CfgI(UIAConfig.GridMaxBagCols, 2);
                return h;
            }
        }

        private static float CfgF(BepInEx.Configuration.ConfigEntry<float> e, float fallback)
        {
            try { return e != null ? e.Value : fallback; } catch { return fallback; }
        }

        private static int CfgI(BepInEx.Configuration.ConfigEntry<int> e, int fallback)
        {
            try { return e != null ? e.Value : fallback; } catch { return fallback; }
        }

        // Reused walk buffer for DirtyAllMeshes (no steady-state allocation; only ever filled on
        // a real theme-hash change). Cleared after every use and in Shutdown.
        private static readonly List<UnityEngine.UI.MaskableGraphic> _dirtyScratch =
            new List<UnityEngine.UI.MaskableGraphic>(128);

        /// <summary>Force every Grid glass mesh — the main window's subtree and every pinned
        /// window — to rebuild. See <see cref="PollStyleHash"/> for why the dirty guards cannot
        /// catch mesh-time global reads on their own.</summary>
        private static void DirtyAllMeshes()
        {
            if (_root != null)
            {
                _dirtyScratch.Clear();
                _root.GetComponentsInChildren(true, _dirtyScratch);
                for (int i = 0; i < _dirtyScratch.Count; i++)
                {
                    var g = _dirtyScratch[i];
                    if (g is IGlassSurface) g.SetVerticesDirty();
                }
                _dirtyScratch.Clear();
            }
            PinnedInventoryWindow.DirtyAllMeshes();
        }

        /// <summary>Full teardown for a clean ScriptEngine hot reload: unhook the vanilla close event,
        /// destroy the canvas (which takes every pooled view/cell, the drag ghosts, and the drag handler
        /// components with it), destroy the pinned windows' own canvas, drop the tab drag-out delegate
        /// (a stale one would call into a dead assembly after reload), and reset all statics so a
        /// double-F6 strands nothing. The pin RECORDS are untouched — they are per-save disk state.</summary>
        public static void Shutdown()
        {
            Unhook();
            GridSelection.Clear();      // drop the scroll-select cursor + its refs to pooled cells/tabs
            BagGridCell.CancelActiveDrag(); // drop any in-flight drag (ghost + dim + pinned source) first
            ReleaseCursorBlock();       // never leave vanilla world-picking blocked across F6
            ReleaseEscSwallow();        // a held Typing input state must not survive the reload
            GridTab.ResetStatics();     // drop the static drag-out delegate + bounds override
            GridProfilePopup.Shutdown();   // transient canvases + cursor-block holds
            GridCapturePanel.Shutdown();
            GridProfileMode.Reset();       // the mode + its version counter start clean
            GridGhostHint.Reset();         // drop the gesture item/slot refs + the hint id
            PinnedInventoryWindow.Shutdown();
            _pinNodes.Clear();
            _pinScratch.Clear();
            _pinVersion = 0;
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _raycaster = null;
            _panel = null;
            _panelBg = null;
            _title = null;
            _closeBg = null;
            _closeBtn = null;
            _profBg = null;
            _profBtn = null;
            _profGlyph = null;
            _profileStamp = int.MinValue;
            _tagPts.Clear();
            _titleBar = null;
            _titleDrag = null;
            _grip = null;
            _gripDrag = null;
            _gripLines = null;
            _overlay = null;
            _viewport = null;
            _content = null;
            _scroll = null;
            _scrollbar = null;   // its GameObject dies with the canvas below; just drop the ref
            _rootRegion = null;
            _linePts.Clear();
            _dirtyScratch.Clear();
            _open = false;
            _interactive = false;
            _editPreview = false;
            _blockHeld = false;
            _menuHidden = false;
            _signature = 0L;
            _haveSignature = false;
            _haveContent = false;
            _styleHash = 0;
            _haveStyleHash = false;
            GridTheme.Slot = HudStyleSlot.Base; // hot-reload rule: never resume on a stale tier skin
            _sizeHash = 0;
            _haveSizeHash = false;
            _geomLoaded = false;
            _winX = 0f;
            _winY = 0f;
            _winW = 560f;
            _winH = 760f;
        }

        // ---------- geometry (position + size memory) ----------

        /// <summary>Pull the remembered geometry out of config and clamp it to the CURRENT screen.
        /// A stored X below zero means "never placed" — centre the window and store that position, so
        /// the first open is centred and every later open lands exactly where the user left it. Also
        /// self-heals a window stranded off-screen by a resolution change.</summary>
        private static void LoadGeometry()
        {
            float w = 560f, h = 760f, x = -1f, y = -1f;
            try
            {
                w = UIAConfig.GridWinW.Value;
                h = UIAConfig.GridWinH.Value;
                x = UIAConfig.GridWinX.Value;
                y = UIAConfig.GridWinY.Value;
            }
            catch { }

            _winW = Mathf.Clamp(float.IsNaN(w) ? 560f : w, MinW, Mathf.Max(MinW, Mathf.Min(MaxW, Screen.width)));
            _winH = Mathf.Clamp(float.IsNaN(h) ? 760f : h, MinH, Mathf.Max(MinH, Mathf.Min(MaxH, Screen.height)));

            bool firstOpen = x < 0f || y < 0f || float.IsNaN(x) || float.IsNaN(y);
            if (firstOpen)
            {
                x = (Screen.width - _winW) * 0.5f;
                y = (Screen.height - _winH) * 0.5f;
            }
            _winX = x;
            _winY = y;
            ClampToScreen();
            _geomLoaded = true;

            if (firstOpen) SaveGeometry();   // remember the centred placement from now on
        }

        /// <summary>Keep the whole window inside the screen (its top-left never goes negative and its
        /// bottom-right never passes the screen edge). Oversized windows pin to the top-left.</summary>
        private static void ClampToScreen()
        {
            float maxX = Mathf.Max(0f, Screen.width - _winW);
            float maxY = Mathf.Max(0f, Screen.height - _winH);
            _winX = Mathf.Clamp(_winX, 0f, maxX);
            _winY = Mathf.Clamp(_winY, 0f, maxY);
        }

        /// <summary>Persist the live geometry. Called on drag-END only (a BepInEx config write per frame
        /// of a drag would hammer the file), and once when the first open centres the window.</summary>
        private static void SaveGeometry()
        {
            try
            {
                UIAConfig.GridWinX.Value = _winX;
                UIAConfig.GridWinY.Value = _winY;
                UIAConfig.GridWinW.Value = _winW;
                UIAConfig.GridWinH.Value = _winH;
            }
            catch { }
        }

        // ---------- build ----------

        private static void Rebuild()
        {
            if (_canvas == null) return;
            if (!_geomLoaded) LoadGeometry();

            LayoutChrome();

            ContainerNode root = GridModel.BuildRoot();
            _signature = root != null ? root.Signature : GridModel.ComputeSignature();
            _haveSignature = true;

            // Pinned containers do NOT live in this window: prune them out of the freshly built tree
            // (collecting each pruned node) and reconcile the pinned windows against the result. Done
            // BEFORE the region binds, so a pinned bag never renders in both places for a frame.
            GridPinStore.EnsureSaveLoaded();
            _pinVersion = GridPinStore.PinVersion;
            _profileStamp = GridProfileMode.ChromeStamp();   // this rebuild carries the new chrome
            _pinNodes.Clear();
            if (root != null && GridPinStore.Count > 0) PrunePinned(root);
            SyncPinnedWindows();

            float innerW = _viewport.sizeDelta.x;
            if (root == null)
            {
                _rootRegion.Recycle();
                _haveContent = false;
                _content.sizeDelta = new Vector2(innerW, 0f);
                GridSelection.Rebuild(_rootRegion);   // empties the nav list + clears the cursor
                return;
            }

            _rootRegion.Bind(root, 0);
            _rootRegion.Rect.anchoredPosition = Vector2.zero;
            _haveContent = true;
            float h = _rootRegion.Layout(innerW);
            _content.sizeDelta = new Vector2(innerW, h);

            // Rebuild the scroll-select nav list from the freshly bound + laid-out tree and reconcile
            // the cursor by Slot/RefId identity (kept AFTER Layout so the rects are placed for
            // ensure-visible and the cells/tabs have reset their own selection flags).
            GridSelection.Rebuild(_rootRegion);
        }

        // ---------- pinning (this panel owns the pinned-window collection) ----------

        /// <summary>Is this container currently torn out into its own pinned window?</summary>
        public static bool IsPinned(long containerRefId)
        {
            return containerRefId != 0L && GridPinStore.IsPinned(containerRefId);
        }

        /// <summary>Pin a container by its persistent <c>Thing.ReferenceId</c> — the entry point the
        /// keyboard/mouse shortcuts use. Already pinned: the existing window is raised to the front
        /// instead (a second press focuses rather than duplicating). A pin the store has never seen is
        /// placed near the screen centre, cascaded by the number of pins already open so several
        /// shortcut pins don't land exactly on top of each other. Mutates NO game state — it records a
        /// view preference and forces this window's tree to rebuild without the container.
        /// Returns true if the container is pinned when the call returns.</summary>
        public static bool PinContainer(long containerRefId)
        {
            if (containerRefId == 0L) return false;
            GridPinStore.EnsureSaveLoaded();

            if (GridPinStore.IsPinnedLoaded(containerRefId))
            {
                if (!_open) ShowLatched();
                Focus(containerRefId);
                return true;
            }

            float off = PinCascade * GridPinStore.Count;
            float x = (Screen.width - PinW) * 0.5f + off;
            float y = (Screen.height - PinH) * 0.5f + off;
            GridPinStore.Pin(containerRefId, new Rect(x, y, PinW, PinH));

            if (!_open) ShowLatched();      // Show() forces a rebuild, which spawns the window
            else ForceRebuild();
            Focus(containerRefId);
            return true;
        }

        /// <summary>Drop a container's pin: its window closes and the container reappears inside this
        /// window's tree on the rebuild this schedules. No-op if it was not pinned.</summary>
        public static void UnpinContainer(long containerRefId)
        {
            if (containerRefId == 0L || !GridPinStore.IsPinned(containerRefId)) return;
            var w = PinnedInventoryWindow.Find(containerRefId);
            if (w != null) w.Close();
            GridPinStore.Unpin(containerRefId);
            if (_open) ForceRebuild();
        }

        /// <summary>Open a container INSIDE the Universal Inventory — expand its region in the main list
        /// and show the window — rather than tearing it out into a pinned window. This is the DEFAULT for
        /// the 1-6 keys: a container the player has NOT manually pinned lives in the main list, so the key
        /// just reveals it there. <paramref name="toggle"/> (the keyboard half) flips an already-open,
        /// already-expanded region closed on a repeat press (open ↔ close); the mouse half only ever
        /// opens. VIEW-ONLY: writes a per-save expand flag and rebuilds — no game state changes.</summary>
        public static void OpenContainerInGrid(long containerRefId, bool toggle)
        {
            if (containerRefId == 0L) return;
            GridCollapseStore.EnsureSaveLoaded();
            bool wasCollapsed = GridCollapseStore.IsCollapsedLoaded(containerRefId);
            // Keyboard repeat on an already-shown, already-expanded region collapses it; otherwise expand.
            bool collapseNow = toggle && _open && !wasCollapsed;
            GridCollapseStore.SetCollapsed(containerRefId, collapseNow);
            if (!_open) { ShowLatched(); return; }   // Show forces the rebuild that renders it expanded
            // Grid already open: rebuild only when the expand/collapse actually flipped, so a repeat open
            // of an already-expanded region does not needlessly rebuild (which would reset the scroll).
            if (collapseNow != wasCollapsed) ForceRebuild();
        }

        /// <summary>Raise a pinned window above its siblings so a repeat shortcut press reads as
        /// "focus this one". Cheap sibling reorder; no-op when the window is not live.</summary>
        private static void Focus(long containerRefId)
        {
            var w = PinnedInventoryWindow.Find(containerRefId);
            if (w != null && w.transform != null) w.transform.SetAsLastSibling();
        }

        /// <summary>Invalidate the structural signature and rebuild NOW (rather than next Tick), so a
        /// pin/unpin is reflected on the same frame the user's gesture completed.</summary>
        private static void ForceRebuild()
        {
            _haveSignature = false;
            Rebuild();
        }

        /// <summary>A tab was released OUTSIDE this window (<see cref="GridTab.DragOutRequested"/>):
        /// pin that container with its window's top-left placed so the cursor sits inside the new title
        /// bar, clamped on-screen. VIEW-ONLY — the record goes to <see cref="GridPinStore"/> and the
        /// rebuild does the rest (prune from the tree, spawn the window at the stored geometry).</summary>
        private static void HandleTabDragOut(GridTab tab, Vector2 screenPoint)
        {
            if (tab == null) return;
            long id = tab.RefId;
            if (id == 0L) return;

            GridPinStore.EnsureSaveLoaded();
            if (GridPinStore.IsPinnedLoaded(id)) { Focus(id); return; }

            // Screen space is bottom-up; pinned-window geometry is top-left-down, like this panel's.
            float x = screenPoint.x - PinGrabX;
            float y = (Screen.height - screenPoint.y) - PinGrabY;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - PinW));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - PinH));
            GridPinStore.Pin(id, new Rect(x, y, PinW, PinH));

            ForceRebuild();
            Focus(id);
        }

        /// <summary>Walk the freshly built tree and REMOVE every pinned container from its parent's
        /// <see cref="ContainerNode.Children"/>, collecting the removed nodes into <see cref="_pinNodes"/>.
        /// Recurses into a pinned node too, so a pin nested inside a pin still gets its own window rather
        /// than riding along inside its parent's. Iterates backwards because it removes in place.
        /// Structural only (never per frame) and allocation-free.</summary>
        private static void PrunePinned(ContainerNode node)
        {
            var kids = node != null ? node.Children : null;
            if (kids == null) return;
            for (int i = kids.Count - 1; i >= 0; i--)
            {
                var child = kids[i];
                if (child == null) { kids.RemoveAt(i); continue; }
                if (GridPinStore.IsPinnedLoaded(child.RefId))
                {
                    kids.RemoveAt(i);
                    _pinNodes.Add(child);
                }
                PrunePinned(child);   // a pinned node's own pinned descendants leave it too
            }
        }

        /// <summary>Reconcile the live pinned windows against <see cref="_pinNodes"/>: (re)bind a window
        /// for every pinned container that is still reachable in the local inventory — creating it on
        /// first sight, at the geometry <see cref="GridPinStore"/> remembers — and CLOSE the window of any
        /// pinned container that has gone away (dropped, given to a teammate, world-stored). The pin
        /// RECORD survives that close, so the window returns if the container does. Runs on structural
        /// rebuilds only; the id scratch list is reused, so it allocates nothing steady-state.</summary>
        private static void SyncPinnedWindows()
        {
            for (int i = 0; i < _pinNodes.Count; i++)
            {
                var node = _pinNodes[i];
                if (node == null || node.RefId == 0L) continue;
                var w = PinnedInventoryWindow.Find(node.RefId);
                if (w == null) w = PinnedInventoryWindow.Create();
                if (w == null) continue;
                w.Bind(node);   // first bind restores the stored geometry
            }

            if (PinnedInventoryWindow.LiveCount == 0) return;
            GridPinStore.GetPinned(_pinScratch);
            for (int i = 0; i < _pinScratch.Count; i++)
            {
                long id = _pinScratch[i];
                if (WasCollected(id)) continue;
                var w = PinnedInventoryWindow.Find(id);
                if (w != null) w.Close();   // container unreachable this frame: close, keep the pin
            }
            _pinScratch.Clear();
        }

        /// <summary>Did this rebuild's prune find a live node for that pinned container? Linear over a
        /// handful of pins and allocation-free (no LINQ, no set).</summary>
        private static bool WasCollected(long refId)
        {
            for (int i = 0; i < _pinNodes.Count; i++)
            {
                var n = _pinNodes[i];
                if (n != null && n.RefId == refId) return true;
            }
            return false;
        }

        /// <summary>Size + place the panel, title, drag strip, close button, resize grip and scroll
        /// viewport from the remembered geometry. Cheap (pure RectTransform writes plus dirty-guarded
        /// shape sets); called on Show, on every structural rebuild, and every frame of a resize drag.</summary>
        private static void LayoutChrome()
        {
            float panelW = _winW;
            float panelH = _winH;

            // Anchored to the canvas TOP-LEFT, positioned by its CENTRE (the centre-pivot rule):
            // stored X/Y are the window's top-left corner in px from the screen's top-left.
            _panel.anchoredPosition = new Vector2(_winX + panelW * 0.5f, -(_winY + panelH * 0.5f));
            _panel.sizeDelta = new Vector2(panelW, panelH);
            // No SetShape here: the corner sweep is part of the inherited box theme, so the ONE
            // place it is issued is GridTheme.ApplyBox, from StyleChrome (called at the end of this
            // method and every Tick).

            // Title (top-left) and the chrome buttons (top-right: [tag][X]), anchored to the
            // panel's top-left. The title/move band stops short of BOTH buttons. Live F9 chrome
            // size, read once for the placement below (the profile tag follows its HEIGHT).
            float closeSz = CloseSize();
            float chromeW = closeSz + 6f + ProfBtnW;
            _title.rectTransform.anchoredPosition = new Vector2(Pad + 2f, -Pad);
            _title.rectTransform.sizeDelta = new Vector2(Mathf.Max(40f, panelW - Pad * 2f - chromeW - 6f), TitleH);
            HudText.Sync(_title);
            HudText.Set(_title, WindowTitle);

            // The move handle spans the title text's band, stopping short of the buttons so they
            // never start a drag (they are also LATER siblings, so they win the raycast regardless).
            float barW = Mathf.Max(40f, panelW - Pad * 2f - chromeW - 6f);
            _titleBar.anchoredPosition = new Vector2(Pad + barW * 0.5f, -(Pad + TitleH * 0.5f)); // centre pivot
            _titleBar.sizeDelta = new Vector2(barW, TitleH);

            var closeRt = (RectTransform)_closeBtn.transform;
            closeRt.anchoredPosition = new Vector2(panelW - Pad - closeSz * 0.5f, -Pad - TitleH * 0.5f); // centre pivot
            closeRt.sizeDelta = new Vector2(closeSz, closeSz);
            if (_closeBtn.Label != null)
                _closeBtn.Label.rectTransform.sizeDelta = new Vector2(closeSz, closeSz);

            var profRt = (RectTransform)_profBtn.transform;
            profRt.anchoredPosition = new Vector2(panelW - Pad - closeSz - 6f - ProfBtnW * 0.5f,
                                                  -Pad - TitleH * 0.5f); // centre pivot
            profRt.sizeDelta = new Vector2(ProfBtnW, closeSz);

            // Resize grip, tucked into the bottom-right corner (centre pivot).
            _grip.anchoredPosition = new Vector2(panelW - GripInset - GripSize * 0.5f,
                                                 -(panelH - GripInset - GripSize * 0.5f));
            _grip.sizeDelta = new Vector2(GripSize, GripSize);

            float vpX = Pad;
            float vpY = Pad + TitleH + TitleGap;
            float vpW = Mathf.Max(40f, panelW - Pad * 2f);
            float vpH = Mathf.Max(40f, panelH - vpY - Pad);
            _viewport.anchoredPosition = new Vector2(vpX, -vpY);
            _viewport.sizeDelta = new Vector2(vpW, vpH);

            StyleChrome();
        }

        /// <summary>Paint the chrome (bg, border, title, close button, grip) through the ONE shared
        /// styling path, <see cref="GridTheme.ApplyBox"/>: fill, border colour, border WIDTH, the
        /// corner sweep and the full glass/frost stack a HUD box gets, all inherited from the global
        /// box theme (or the Grid's overrides). NOTHING here hardcodes a width or a radius, and the
        /// hover states are the helper's own delta on the inherited line, so a thin global border
        /// stays thin. Runs every Tick so a live F9 edit and the hover states show without a rebuild;
        /// every setter downstream is dirty-guarded, so an unchanged frame costs no mesh rebuild and
        /// allocates nothing.</summary>
        private static void StyleChrome()
        {
            if (_panelBg == null) return;

            Color text = GridTheme.Text;

            GridTheme.ApplyBox(_panelBg, _winW, _winH, GridTheme.GridSurface.Window, false);

            if (_title != null) _title.color = text;

            // The close button keeps its hover ACCENT: an interaction affordance, not part of the box
            // theme. ApplyBox supplies it (and the widened line) as a delta on the inherited values.
            bool hover = _closeBtn != null && _closeBtn.Hover;
            float closeSz = CloseSize();
            GridTheme.ApplyBox(_closeBg, closeSz, closeSz, GridTheme.GridSurface.Button, hover);
            var lbl = _closeBtn != null ? _closeBtn.Label : null;
            if (lbl != null) lbl.color = hover ? HudPalette.LineAccent.Value : text;

            // The profile-mode button: LATCHED accent while the mode is on (the same "mode reveals
            // editing chrome" read F9 trains), hover accent otherwise. The glyph is drawn
            // iconography — its line inherits the theme border weight, never an absolute width.
            bool pActive = GridProfileMode.Active;
            bool pHover = _profBtn != null && _profBtn.Hover;
            if (_profBg != null)
                GridTheme.ApplyBox(_profBg, ProfBtnW, closeSz, GridTheme.GridSurface.Button, pHover || pActive);
            if (_profGlyph != null)
            {
                Color gcol = pHover || pActive
                    ? (HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : GridTheme.Border)
                    : GridTheme.Border;
                Color gfill = gcol;
                gfill.a *= pActive ? 0.30f : 0.10f;
                _profGlyph.color = gfill;
                _profGlyph.BorderColor = gcol;
                _profGlyph.BorderWidth = Mathf.Max(0.8f, GridTheme.BorderWidth * 0.8f);
            }

            if (_gripLines != null)
            {
                bool gHover = _gripDrag != null && (_gripDrag.Hover || _gripDrag.Dragging);
                Color gc = gHover ? HudPalette.LineAccent.Value : GridTheme.Border;
                // The grip strokes are drawn ICONOGRAPHY, not a box outline, but they still inherit
                // their weight from the global border width so a thin theme reads thin everywhere.
                float gw = Mathf.Max(0.8f, GridTheme.BorderWidth);
                for (int i = 0; i < _gripLines.Length; i++)
                {
                    var ln = _gripLines[i];
                    if (ln == null) continue;
                    ln.color = gc;
                    ln.Width = gw;
                }
            }
        }

        private static void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new GameObject("UIAscended_TheGrid");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the vanilla HUD and the radial ring; unlike the radial (hover-driven, raycasts off)
            // this is an interactive window, so its canvas keeps a GraphicRaycaster.
            _canvas.sortingOrder = 5020;
            // The analytic SDF panel renderer (the window shell rides it when the HUD boxes do —
            // GridTheme.ApplyCore) carries its whole per-panel contract in the extra vertex
            // streams, and UGUI silently DROPS those streams unless the owning Canvas opts in:
            // the shader would decode zeros (radius 0, border 0, garbage flags). The same opt-in
            // HudSystem makes for the HUD canvas. Normal/Tangent are parameter lanes here, not
            // lighting inputs; harmless for the mesh-path graphics (position/color/uv0 only),
            // which is the combination the HUD canvas has run since 0.9.0.
            _canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
            // Enabled ONLY while the player has the mouse freed (ApplyInteractive drives it), so an
            // open window is a look-at-only surface until the mouse-control key is held.
            _raycaster = _root.AddComponent<GraphicRaycaster>();

            // The window panel with the glass background (a raycast target so clicks on empty panel
            // area don't fall through to the world). Anchored TOP-LEFT, pivoted centre.
            var pGo = new GameObject("Panel", typeof(RectTransform));
            pGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)pGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panelBg = pGo.AddComponent<PanelGraphic>();
            _panelBg.raycastTarget = true;

            _title = HudText.Make(_panel, "Title", HudText.Size(16f),
                TextAlignmentOptions.Left, warp: false);
            AnchorTopLeft(_title.rectTransform);

            BuildTitleBar();
            BuildCloseButton();
            BuildProfileButton();
            BuildScroll();
            BuildGrip();
            BuildGhostOverlay();

            // The tab drag-out (pin) gesture: a method group, assigned once at build and cleared by
            // Shutdown, so a hot reload can never leave a delegate pointing into the dead assembly.
            // GridTab.PanelBounds is deliberately left NULL — each tab then derives the window rect it
            // lives in (its ScrollRect viewport's parent), which is right for BOTH this panel and a
            // pinned window, so a nested bag can be torn out of a pin too.
            GridTab.DragOutRequested = HandleTabDragOut;

            Hook();
        }

        /// <summary>The invisible move handle over the title band. A fully transparent
        /// <see cref="Image"/> (alpha 0 still raycasts — UGUI's hit test is the rect, not the pixel)
        /// carries the drag; it is built BEFORE the close button so the X, a later sibling, always
        /// wins the raycast where they would meet.</summary>
        private static void BuildTitleBar()
        {
            var tGo = new GameObject("TitleBar", typeof(RectTransform));
            tGo.transform.SetParent(_panel, false);
            _titleBar = (RectTransform)tGo.transform;
            AnchorTopLeft(_titleBar);
            _titleBar.pivot = new Vector2(0.5f, 0.5f);   // positioned by its centre, like every chrome rect

            var hit = tGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            _titleDrag = tGo.AddComponent<WindowDrag>();
        }

        private static void BuildCloseButton()
        {
            var cGo = new GameObject("Close", typeof(RectTransform));
            cGo.transform.SetParent(_panel, false);
            AnchorTopLeft((RectTransform)cGo.transform);
            ((RectTransform)cGo.transform).pivot = new Vector2(0.5f, 0.5f); // centre pivot: PanelGraphic draws centred on origin
            _closeBg = cGo.AddComponent<PanelGraphic>();
            _closeBg.raycastTarget = true;
            _closeBtn = cGo.AddComponent<PanelButton>();
            _closeBtn.Clicked = Hide;

            var lbl = HudText.Make((RectTransform)cGo.transform, "X", HudText.Size(15f),
                TextAlignmentOptions.Center, warp: false);
            lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            lbl.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            lbl.rectTransform.anchoredPosition = Vector2.zero;
            lbl.rectTransform.sizeDelta = new Vector2(CloseSize(), CloseSize());   // live F9 size; LayoutChrome re-fits
            HudText.Set(lbl, "X"); // ASCII only: the game TMP font tofus dingbats like the multiplication-X glyph
            _closeBtn.Label = lbl;
        }

        /// <summary>The PROFILE MODE toggle (design O4a): a chrome button left of the X carrying a
        /// DRAWN luggage-tag glyph — a 5-point <see cref="PolygonPanelGraphic"/> pentagon, never a
        /// TMP dingbat (tofu). Clicking flips <see cref="GridProfileMode"/>; the mode's chrome
        /// (chips, CAPTURE, badges swap) arrives via the stamp-diffed rebuild. Styled latched
        /// (accent) while the mode is on — see <see cref="StyleChrome"/>.</summary>
        private static void BuildProfileButton()
        {
            var go = new GameObject("ProfileMode", typeof(RectTransform));
            go.transform.SetParent(_panel, false);
            AnchorTopLeft((RectTransform)go.transform);
            ((RectTransform)go.transform).pivot = new Vector2(0.5f, 0.5f); // centre pivot: PanelGraphic draws centred
            _profBg = go.AddComponent<PanelGraphic>();
            _profBg.raycastTarget = true;
            _profBtn = go.AddComponent<PanelButton>();
            _profBtn.Clicked = ToggleProfileMode;

            var gGo = new GameObject("TagGlyph", typeof(RectTransform));
            gGo.transform.SetParent(go.transform, false);
            var grt = (RectTransform)gGo.transform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;
            grt.sizeDelta = new Vector2(18f, 12f);
            _profGlyph = gGo.AddComponent<PolygonPanelGraphic>();
            _profGlyph.raycastTarget = false;
            // CCW pentagon centred on the origin: a tag pointing left (the string end), body right.
            _tagPts.Clear();
            _tagPts.Add(new Vector2(-8f, 0f));
            _tagPts.Add(new Vector2(-3f, -5f));
            _tagPts.Add(new Vector2(8f, -5f));
            _tagPts.Add(new Vector2(8f, 5f));
            _tagPts.Add(new Vector2(-3f, 5f));
            _profGlyph.SetPoints(_tagPts, null, null, 0, 2);
            _tagPts.Clear();
        }

        /// <summary>Flip profile mode and rebuild NOW (same-frame feedback; the stamp diff in
        /// <see cref="Tick"/> would catch it next frame anyway). Raycaster gating means this can
        /// only fire from an interactive click.</summary>
        private static void ToggleProfileMode()
        {
            GridProfileMode.Toggle();
            if (_open) ForceRebuild();
        }

        /// <summary>The bottom-right resize grip: a transparent hit rect carrying three short DRAWN
        /// diagonal lines (the notepad-grip look). Drawn, never a glyph — the game's TMP font tofus
        /// box-drawing/dingbat characters. Lines run parallel to the corner's hypotenuse and grow
        /// toward it; they are not raycast targets, so only the hit rect starts a resize.</summary>
        private static void BuildGrip()
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

            // Three "/" strokes nested into the bottom-right corner, each longer than the last.
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
                // Weight comes from the inherited theme; StyleChrome sets it every pass.

                _linePts.Clear();
                _linePts.Add(new Vector2(h - len[i], -h));   // on the bottom edge
                _linePts.Add(new Vector2(h, -h + len[i]));   // on the right edge
                line.SetPoints(_linePts, false);

                _gripLines[i] = line;
            }
            _linePts.Clear();
        }

        /// <summary>A stretched, non-raycast layer kept as the LAST child of the canvas: the host for a
        /// cell's floating drag ghost, so the ghost draws above the window and occludes no drop target.</summary>
        private static void BuildGhostOverlay()
        {
            var oGo = new GameObject("DragOverlay", typeof(RectTransform));
            oGo.transform.SetParent(_root.transform, false);
            _overlay = (RectTransform)oGo.transform;
            _overlay.anchorMin = Vector2.zero;
            _overlay.anchorMax = Vector2.one;
            _overlay.pivot = new Vector2(0.5f, 0.5f);
            _overlay.offsetMin = Vector2.zero;
            _overlay.offsetMax = Vector2.zero;
            _overlay.SetAsLastSibling();
        }

        private static void BuildScroll()
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
            // whole list area a hit, so the wheel scrolls anywhere over the list and empty space can
            // start a drag-scroll. Built BEFORE the Content so it is the FIRST sibling: behind every
            // cell (cells still win their own clicks) and, living on the viewport rather than the
            // content, it never scrolls away. It also hover-GATES scrolling for free: no hit means no
            // wheel event, so the wheel is never stolen from gameplay.
            var hGo = new GameObject("ScrollHit", typeof(RectTransform));
            hGo.transform.SetParent(_viewport, false);
            var hrt = (RectTransform)hGo.transform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;
            var hit = hGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);   // alpha 0 still raycasts: UGUI hit-tests the rect
            hit.raycastTarget = true;

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

            // The ONE renderer. BagGridView (the old nested tree) is deprecated and never built.
            _rootRegion = GridRegionView.Create(_content);

            // Thin rounded scroll indicator on the right edge (auto-hides when the list fits).
            _scrollbar = GridScrollbar.Create(_scroll, _viewport, _content);
        }

        private static void AnchorTopLeft(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        // ---------- vanilla close-all bridge ----------

        private static void Hook()
        {
            if (_hooked) return;
            InventoryWindowManager.OnUIClose += HandleUIClose;
            _hooked = true;
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            InventoryWindowManager.OnUIClose -= HandleUIClose;
            _hooked = false;
        }

        /// <summary>Vanilla fired its close-all (backtick / KeyMap.HideAllWindows → HideAll()).
        /// This is the ONE gesture that takes the whole Grid down: the main window AND every pinned
        /// window. The pin RECORDS in <see cref="GridPinStore"/> survive — the windows come back on
        /// the next open — so backtick "closes", it does not unpin; a pin's own X / shrink button is
        /// the gesture that truly unpins. Runs even when the main window is already hidden, because
        /// pins now outlive it.</summary>
        private static void HandleUIClose()
        {
            if (_open) Hide();
            PinnedInventoryWindow.CloseAll();
            _pinNodes.Clear();
        }

        // ---------- window move / resize ----------

        /// <summary>Title-bar move handle. Records the pointer and the window's top-left at drag start
        /// and drives the position from the ABSOLUTE pointer delta (not per-frame deltas, which drift),
        /// clamped on-screen every frame. The window never resizes here, so no relayout is needed —
        /// only the panel's anchoredPosition moves. Geometry is persisted once, on drag end.</summary>
        private sealed class WindowDrag : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private Vector2 _grabPointer;
            private float _grabX, _grabY;
            private bool _active;

            public void OnBeginDrag(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (!IsInteractive) return;
                _active = true;
                _grabPointer = e.position;
                _grabX = _winX;
                _grabY = _winY;
            }

            public void OnDrag(PointerEventData e)
            {
                if (!_active || e == null) return;
                // Screen space: +x is right, +y is UP, while our stored Y grows DOWNWARD.
                _winX = _grabX + (e.position.x - _grabPointer.x);
                _winY = _grabY - (e.position.y - _grabPointer.y);
                ClampToScreen();
                LayoutChrome();
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (!_active) return;
                _active = false;
                SaveGeometry();
            }
        }

        /// <summary>Bottom-right resize grip. The window's top-left stays pinned, so the drag maps
        /// straight onto width/height (clamped to the config ranges and to the screen). Each frame runs
        /// the LIGHT <see cref="Relayout"/> — chrome plus one region Layout, which re-wraps the cell grid
        /// into the new width — never a tree rebuild. Geometry is persisted once, on drag end.</summary>
        private sealed class WindowResize : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler,
            IPointerEnterHandler, IPointerExitHandler
        {
            private Vector2 _grabPointer;
            private float _grabW, _grabH;

            public bool Hover;
            public bool Dragging;

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }

            public void OnBeginDrag(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (!IsInteractive) return;
                Dragging = true;
                _grabPointer = e.position;
                _grabW = _winW;
                _grabH = _winH;
            }

            public void OnDrag(PointerEventData e)
            {
                if (!Dragging || e == null) return;
                float maxW = Mathf.Max(MinW, Mathf.Min(MaxW, Screen.width - _winX));
                float maxH = Mathf.Max(MinH, Mathf.Min(MaxH, Screen.height - _winY));
                _winW = Mathf.Clamp(_grabW + (e.position.x - _grabPointer.x), MinW, maxW);
                _winH = Mathf.Clamp(_grabH - (e.position.y - _grabPointer.y), MinH, maxH);
                Relayout();
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (!Dragging) return;
                Dragging = false;
                SaveGeometry();
            }
        }

        // ---------- close button ----------

        /// <summary>Left-click surface for the X button. Records hover so <see cref="StyleChrome"/>
        /// can promote the accent on the next chrome pass; the click action is a method group set once
        /// at build (no per-frame delegate churn, nothing to unsubscribe).</summary>
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
