using System.Collections.Generic;
using Assets.Scripts;
using StationeersUIMod.UI.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.Windows
{
    /// <summary>
    /// The F9 HUD editor's screen state: the live HUD stays up over a dimmed backdrop,
    /// hovering any element highlights its colours in the F9 window, and CLICKING an
    /// element pops an edit panel right where you clicked. Hit rects come from the
    /// panels themselves; clicks are inverse-warped so the curved HUD hit-tests true.
    ///
    /// Like the radial editor: no modal of its own — the F9 window (a game ImGui window)
    /// owns cursor unlock and Escape-to-close; the mode dies with the window.
    /// </summary>
    public static class HudEditorMode
    {
        public static bool Active { get; private set; }

        public static readonly HashSet<string> HotPalette = new HashSet<string>();
        public static string HotSignature { get; private set; } = "";

        public static HudEditTarget Selected;
        /// <summary>Popup anchor in ImGui coords (y down).</summary>
        public static Vector2 PopupPos;
        /// <summary>Bumped on every new selection so the popup MOVES to the new click
        /// (ImGuiCond.Appearing alone pins it where it first opened).</summary>
        public static int SelectionStamp;

        private static Canvas _dimCanvas;
        private static Image _dim;
        private static bool _blockedCursor;
        private static readonly List<HudEditTarget> _targets = new List<HudEditTarget>();

        public static void Enter()
        {
            Active = true;
            Selected = null;
            // The world stays live behind the editor; without this, editor clicks also
            // ran vanilla's cursor machine (same latent hole ModalScope plugs for radials).
            try
            {
                if (CursorManager.Instance != null)
                {
                    CursorManager.Instance.BlockCursorRaycast = true;
                    _blockedCursor = true;
                }
            }
            catch { }
        }

        public static void Exit()
        {
            // Closing the designer is an interruption just like releasing the mouse: preserve
            // the visible partial drag as one undoable edit before tearing down its state.
            CommitActiveDrag();
            Active = false;
            Selected = null;
            SelectedElement = null;
            HoverElement = null;
            _selectedId = null;
            _pendingSelectId = null;
            _dragging = false;
            _preDrag = null;
            _multiIds.Clear();
            _marquee = false;
            _dragOrig.Clear();
            _origPts.Clear();
            _origHin.Clear();
            _origHout.Clear();
            _dragPointIndex = -1;
            EditingPoints = false;
            CancelDrawLine();
            CancelDrawShape();
            HudSystem.ForceTier = null;
            HotPalette.Clear();
            HotSignature = "";
            if (_dimCanvas != null) _dimCanvas.gameObject.SetActive(false);
            if (_blockedCursor)
            {
                _blockedCursor = false;
                try
                {
                    if (CursorManager.Instance != null)
                        CursorManager.Instance.BlockCursorRaycast = false;
                }
                catch { }
            }
        }

        public static void Shutdown()
        {
            Exit();
            UI.Hud.HudDocumentHistory.Clear();
            _views.Clear();
            if (_dimCanvas != null) Object.Destroy(_dimCanvas.gameObject);
            _dimCanvas = null;
            _dim = null;
        }

        /// <summary>Per-frame while active (plugin Update, after HudSystem.Update).</summary>
        public static void Update()
        {
            if (!Active) return;
            EnsureBackdrop();
            _dimCanvas.gameObject.SetActive(true);
            // Overlay canvases always draw over camera-rendered geometry, so in
            // CurvedWorldCanvas mode the dim would sit ON TOP of the world-space HUD —
            // there, the world itself is the backdrop.
            bool worldMode = HudConfig.Curvature != null
                && HudConfig.Curvature.Value == HudCurvature.CurvedWorldCanvas;
            _dim.color = new Color(0f, 0f, 0f, worldMode ? 0f : 0.55f);

            // Mouse -> canvas coords (centre origin, y up), inverse-warped.
            var m = (Vector2)Input.mousePosition;
            var p = new Vector2(m.x - Screen.width * 0.5f, m.y - Screen.height * 0.5f);
            p = Unwarp(p);

            bool imguiOwnsMouse = false;
            try { imguiOwnsMouse = ImGuiNET.ImGui.GetIO().WantCaptureMouse; } catch { }

            // Document mode gets the full designer; the legacy panel set keeps the
            // original click-to-config flow.
            if (HudSystem.DocumentMode)
            {
                UpdateDesigner(m, p, imguiOwnsMouse);
                return;
            }

            _targets.Clear();
            HudSystem.CollectEditTargets(_targets);

            // Hover: smallest containing rect wins (panels overlap the vignette strip).
            HudEditTarget hover = null;
            float bestArea = float.MaxValue;
            if (!imguiOwnsMouse)
            {
                foreach (var t in _targets)
                {
                    if (!t.CanvasRect.Contains(p)) continue;
                    float area = t.CanvasRect.width * t.CanvasRect.height;
                    if (area < bestArea) { bestArea = area; hover = t; }
                }
            }

            HotPalette.Clear();
            if (hover != null)
                foreach (var name in hover.Palette) HotPalette.Add(name);
            else if (Selected != null)
                foreach (var name in Selected.Palette) HotPalette.Add(name);
            HotSignature = HotPalette.Count == 0 ? "" : string.Join("|", HotPalette);

            if (!imguiOwnsMouse && Input.GetMouseButtonDown(0))
            {
                Selected = hover; // click empty space = close the popup
                if (hover != null)
                {
                    SelectionStamp++;
                    PopupPos = new Vector2(Mathf.Min(m.x + 18f, Screen.width - 340f),
                        Mathf.Clamp(Screen.height - m.y - 20f, 10f, Screen.height - 320f));
                }
            }
        }

        // ==================================================================== designer

        /// <summary>Line-drawing mode: clicks lay points, Enter/RMB commits a Polyline
        /// element, Escape cancels. Toggled from the F9 window.</summary>
        public static bool DrawingLine { get; private set; }

        /// <summary>Pen tool: like the line tool, but clicking the FIRST point (or Enter/RMB)
        /// CLOSES the path into a filled <see cref="UI.Hud.HudElementType.Shape"/> glass element.</summary>
        public static bool DrawingShape { get; private set; }

        /// <summary>Point-edit sub-mode: with a Shape/Polyline selected, DRAG its anchors, Alt+click an
        /// anchor to delete, click a segment to insert. Toggled from the F9 window; auto-exits when
        /// the selection changes to a non-point element.</summary>
        public static bool EditingPoints { get; private set; }
        internal static bool IsPointEditable(UI.Hud.HudElementDef d)
            => d != null && (d.Type == UI.Hud.HudElementType.Shape || d.Type == UI.Hud.HudElementType.Polyline);
        public static void BeginEditPoints() { EditingPoints = true; }
        public static void EndEditPoints() { EditingPoints = false; }

        /// <summary>The selected document element (by Id — survives view rebuilds).</summary>
        internal static UI.Hud.HudElementView SelectedElement { get; private set; }
        private static string _selectedId;
        private static string _pendingSelectId;

        internal static UI.Hud.HudElementView HoverElement { get; private set; }

        private static readonly List<UI.Hud.HudElementView> _views = new List<UI.Hud.HudElementView>();
        private static readonly List<Vector2> _drawPts = new List<Vector2>();

        // Drag state. Handle indices: 0..3 corners (BL,BR,TR,TL), 4..7 edges (B,R,T,L), -1 move.
        private static bool _dragging;
        private static int _dragHandle = -1;
        private static Vector2 _dragStart;         // canvas coords at mouse-down
        private static float _origX, _origY, _origW, _origH;
        private static bool _dragMoved;
        private static bool _dragBare;              // this gesture edits the bare override (preview=BARE)
        private static UI.Hud.HudCurvature _dragMode; // and this curvature mode's LIVE layout (captured at grab)
        private static UI.Hud.HudDocument _preDrag; // undo snapshot armed at mouse-down
        private static UI.Hud.HudDocument _dragDocument; // exact live document the snapshot belongs to
        private static string _dragProfileName;     // save target if a profile swap interrupts the drag

        // Point-edit sub-mode: dragging a Shape/Polyline ANCHOR (not the 8 rect handles).
        private const int PointHandle = -2;          // _dragHandle sentinel
        private static int _dragPointIndex = -1;
        private static int _pointDragKind;           // 0 = anchor, 1 = Bézier IN handle, 2 = OUT handle
        private static Vector2 _origPoint;           // grabbed anchor/handle start pos (element ref px)
        private static readonly List<Vector2> _origPts = new List<Vector2>();  // for resize-scales-points
        private static readonly List<Vector2> _origHin = new List<Vector2>();
        private static readonly List<Vector2> _origHout = new List<Vector2>();

        // Multi-selection (Ctrl+drag marquee). The PRIMARY selection (_selectedId, popup,
        // resize handles) stays single; the marquee set moves/deletes as a group.
        private static readonly HashSet<string> _multiIds = new HashSet<string>();
        private static bool _marquee;
        private static Vector2 _marqueeStart, _marqueeEnd;
        // Group-drag original positions by element Id, captured at mouse-down.
        private static readonly Dictionary<string, Vector2> _dragOrig = new Dictionary<string, Vector2>();

        internal static bool MarqueeActive => _marquee;
        internal static Rect MarqueeRect => Rect.MinMaxRect(
            Mathf.Min(_marqueeStart.x, _marqueeEnd.x), Mathf.Min(_marqueeStart.y, _marqueeEnd.y),
            Mathf.Max(_marqueeStart.x, _marqueeEnd.x), Mathf.Max(_marqueeStart.y, _marqueeEnd.y));
        internal static bool IsMultiSelected(string id) => id != null && _multiIds.Contains(id);
        internal static int MultiCount => _multiIds.Count;

        private const float HandleScreenR = 7f;    // hit radius around a handle, px

        private static void UpdateDesigner(Vector2 mouseScreen, Vector2 p, bool imguiOwnsMouse)
        {
            _views.Clear();
            HudSystem.CollectElementViews(_views);
            float scale = HudConfig.HudScale.Value;

            // Re-resolve the selection by Id — views are rebuilt on structural edits.
            if (_pendingSelectId != null) { _selectedId = _pendingSelectId; _pendingSelectId = null; }
            SelectedElement = null;
            if (_selectedId != null)
                foreach (var v in _views)
                    if (v.Def.Id == _selectedId) { SelectedElement = v; break; }

            // The point-edit sub-mode only applies while a Shape/Polyline is selected.
            if (EditingPoints && (SelectedElement == null || !IsPointEditable(SelectedElement.Def)))
                EditingPoints = false;

            // Keyboard: delete / duplicate / undo / redo — NEVER while an ImGui text
            // field owns the keyboard (Delete there erases a character, not an element,
            // and Ctrl+Z is a text-edit reflex; ImGui does not block Unity's Input).
            bool imguiOwnsKeys = false;
            try { imguiOwnsKeys = ImGuiNET.ImGui.GetIO().WantCaptureKeyboard; } catch { }
            if (!imguiOwnsKeys)
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl && Input.GetKeyDown(KeyCode.Z)) { DoUndo(); return; }
                if (ctrl && Input.GetKeyDown(KeyCode.Y)) { DoRedo(); return; }
                if (SelectedElement != null && Input.GetKeyDown(KeyCode.Delete)) { DeleteSelected(); return; }
                if (SelectedElement != null && ctrl && Input.GetKeyDown(KeyCode.D)) { DuplicateSelected(); return; }
                // Arrow keys nudge the selection (primary + marquee group): 1px per press,
                // Shift = one grid cell.
                float nx = (Input.GetKeyDown(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKeyDown(KeyCode.LeftArrow) ? 1f : 0f);
                float ny = (Input.GetKeyDown(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKeyDown(KeyCode.DownArrow) ? 1f : 0f);
                if ((nx != 0f || ny != 0f) && (SelectedElement != null || _multiIds.Count > 0))
                {
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    float step = shift
                        ? (HudConfig.GridSnapSize != null ? Mathf.Max(1f, HudConfig.GridSnapSize.Value) : 8f)
                        : 1f;
                    // A nudge while the mouse is still held ends the drag first. This yields two
                    // honest history steps (drag, then nudge) instead of losing the drag baseline.
                    CommitActiveDrag();
                    PushUndoNow();
                    foreach (var v in _views)
                    {
                        bool inSel = (SelectedElement != null && ReferenceEquals(v, SelectedElement))
                            || _multiIds.Contains(v.Def.Id);
                        if (!inSel) continue;
                        bool bare = UI.Hud.HudElementView.EditBare(v.Def);
                        var mode = UI.Hud.HudElementView.LayoutMode;
                        v.Def.SetXFor(bare, mode, v.Def.XFor(bare, mode) + nx * step);
                        v.Def.SetYFor(bare, mode, v.Def.YFor(bare, mode) + ny * step);
                        HudSystem.RelayoutElement(v);
                    }
                    Features.HudProfileStore.MarkChanged();
                    return;
                }
            }

            if (DrawingLine)
            {
                UpdateDrawLine(p, imguiOwnsMouse, scale);
                return;
            }
            if (DrawingShape)
            {
                UpdateDrawShape(p, imguiOwnsMouse, scale);
                return;
            }

            // Hover: smallest element rect containing the unwarped point.
            HoverElement = null;
            float bestArea = float.MaxValue;
            if (!imguiOwnsMouse && !_dragging)
            {
                foreach (var v in _views)
                {
                    // Single-click can grab ANY element, even one hidden in the current preview
                    // (its rect still exists) — otherwise you couldn't select a Live-mode element
                    // while previewing bare to change its mode ("locked in to whatever they were").
                    // The marquee (CommitMarquee) still filters by visibility so drag-groups stay
                    // tier-homogeneous.
                    var r = v.CanvasRect(scale);
                    if (!r.Contains(p)) continue;
                    float area = r.width * r.height;
                    if (area < bestArea) { bestArea = area; HoverElement = v; }
                }
            }

            // Marquee in progress: track until release, then select everything it touches.
            if (_marquee)
            {
                _marqueeEnd = p;
                if (Input.GetMouseButtonUp(0)) CommitMarquee(scale);
                return;
            }

            if (_dragging)
            {
                // A profile/document swap must not let old drag origins mutate the new document.
                // CommitActiveDrag saves the old document directly when it is no longer active.
                if (!ReferenceEquals(Features.HudProfileStore.Active, _dragDocument))
                {
                    CommitActiveDrag();
                    return;
                }
                UpdateDrag(p, scale);
                if (Input.GetMouseButtonUp(0))
                    CommitActiveDrag();
                return;
            }

            if (imguiOwnsMouse || !Input.GetMouseButtonDown(0)) return;

            // Point-edit sub-mode intercepts clicks entirely: drag an anchor, Alt+click deletes,
            // clicking a segment inserts. (Exit the sub-mode to move/resize the whole element.)
            if (EditingPoints && SelectedElement != null && IsPointEditable(SelectedElement.Def))
            {
                HandlePointEditClick(SelectedElement, mouseScreen, p, scale);
                return;
            }

            // Ctrl+drag = marquee multi-select (takes priority over grabbing anything).
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlHeld)
            {
                _marquee = true;
                _marqueeStart = _marqueeEnd = p;
                return;
            }

            // Handle grab beats element grab; both beat empty-space deselect.
            if (SelectedElement != null)
            {
                int h = HandleAt(SelectedElement, mouseScreen, scale);
                if (h >= 0)
                {
                    BeginDrag(SelectedElement, p, h);
                    return;
                }
            }
            if (HoverElement != null)
            {
                // Clicking OUTSIDE the marquee group collapses it to a single selection;
                // clicking a group member keeps the group (so it can be dragged together).
                if (!_multiIds.Contains(HoverElement.Def.Id)) _multiIds.Clear();
                if (!ReferenceEquals(HoverElement, SelectedElement))
                {
                    _selectedId = HoverElement.Def.Id;
                    SelectedElement = HoverElement;
                    ElementStamp++;
                    PopupPos = new Vector2(Mathf.Min(mouseScreen.x + 18f, Screen.width - 360f),
                        Mathf.Clamp(Screen.height - mouseScreen.y - 20f, 10f, Screen.height - 340f));
                }
                BeginDrag(HoverElement, p, -1);
            }
            else
            {
                _selectedId = null;
                SelectedElement = null;
                _multiIds.Clear();
            }
        }

        /// <summary>Finish the Ctrl+drag marquee: everything whose rect OVERLAPS the box
        /// joins the selection group. A tiny drag (a Ctrl+click) instead TOGGLES the
        /// element under the cursor in and out of the group.</summary>
        private static void CommitMarquee(float scale)
        {
            _marquee = false;
            var rect = MarqueeRect;
            bool tinyDrag = rect.width < 4f && rect.height < 4f;

            if (tinyDrag)
            {
                if (HoverElement != null)
                {
                    string id = HoverElement.Def.Id;
                    if (!_multiIds.Remove(id))
                    {
                        _multiIds.Add(id);
                        // Toggling in also seeds the primary if nothing is selected yet.
                        if (_selectedId == null) { _selectedId = id; ElementStamp++; }
                    }
                }
                return;
            }

            _multiIds.Clear();
            string first = null;
            foreach (var v in _views)
            {
                // Marquee only what's visible in this preview, so a group is always tier-homogeneous
                // (every member resolves EditBare the same way) and never grabs a hidden element.
                if (!v.VisibleAt(UI.Hud.HudElementView.LayoutTier)) continue;
                if (!v.CanvasRect(scale).Overlaps(rect)) continue;
                _multiIds.Add(v.Def.Id);
                if (first == null) first = v.Def.Id;
            }
            if (first != null && _selectedId == null)
            {
                _selectedId = first;
                ElementStamp++;
            }
        }

        /// <summary>Bumped whenever a NEW element is selected so the popup follows.</summary>
        public static int ElementStamp;

        private static void BeginDrag(UI.Hud.HudElementView v, Vector2 p, int handle)
        {
            _dragging = true;
            _dragHandle = handle;
            _dragStart = p;
            _dragMoved = false;
            // Whether this gesture writes the bare override or the base layout follows the
            // previewed tier (see HudElementView.EditBare); captured once so it stays consistent
            // across the drag even if a relayout re-reads the preview mid-gesture.
            _dragBare = UI.Hud.HudElementView.EditBare(v.Def);
            _dragMode = UI.Hud.HudElementView.LayoutMode;
            _origX = v.Def.XFor(_dragBare, _dragMode); _origY = v.Def.YFor(_dragBare, _dragMode);
            _origW = v.Def.WFor(_dragBare, _dragMode); _origH = v.Def.HFor(_dragBare, _dragMode);

            // Moving a marquee-group member moves the WHOLE group (resize stays single).
            _dragOrig.Clear();
            if (handle < 0 && _multiIds.Count > 1 && _multiIds.Contains(v.Def.Id))
                foreach (var view in _views)
                    if (_multiIds.Contains(view.Def.Id))
                    {
                        bool b = UI.Hud.HudElementView.EditBare(view.Def);
                        _dragOrig[view.Def.Id] = new Vector2(view.Def.XFor(b, _dragMode), view.Def.YFor(b, _dragMode));
                    }

            // A Shape's 8 resize handles SCALE its point set (else dragging a corner would grow the
            // hit-rect but leave the drawn shape unchanged); snapshot the points to scale from.
            _origPts.Clear(); _origHin.Clear(); _origHout.Clear();
            if (v.Def.Type == UI.Hud.HudElementType.Shape)
            {
                _origPts.AddRange(v.Def.GetPoints("pts"));
                _origHin.AddRange(v.Def.GetPoints("hin"));
                _origHout.AddRange(v.Def.GetPoints("hout"));
            }

            ArmDragSnapshot();
        }

        private static void UpdateDrag(Vector2 p, float scale)
        {
            var v = SelectedElement;
            // Keep the transaction armed if a view rebuild temporarily removes the selected
            // view. Its already-applied partial geometry still needs committing on interruption.
            if (v == null) return;
            var d = v.Def;
            bool bare = _dragBare; // write the bare override or the base layout (captured at grab)
            Vector2 delta = (p - _dragStart) / Mathf.Max(0.01f, scale);
            if (!_dragMoved && delta.sqrMagnitude < 4f) return; // 2px dead zone
            _dragMoved = true;

            bool snap = HudConfig.GridSnapEnabled != null && HudConfig.GridSnapEnabled.Value
                && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            float grid = HudConfig.GridSnapSize != null ? Mathf.Max(1f, HudConfig.GridSnapSize.Value) : 8f;

            // Point-edit sub-mode: move the grabbed anchor OR a Bézier handle. Re-base + refit
            // happen on release so the shape doesn't jump under the cursor mid-drag.
            if (_dragHandle == PointHandle)
            {
                if (_pointDragKind == 0)
                {
                    var pl = new List<Vector2>(d.GetPoints("pts"));
                    if (_dragPointIndex >= 0 && _dragPointIndex < pl.Count)
                    {
                        Vector2 np = _origPoint + delta;
                        if (snap) np = new Vector2(Snap(np.x, true, grid), Snap(np.y, true, grid));
                        pl[_dragPointIndex] = np;
                        d.SetPoints("pts", pl);
                        HudSystem.RelayoutElement(v);
                    }
                }
                else
                {
                    // Dragging a Bézier handle: mirror the opposite handle (smooth point) unless Alt
                    // is held (a cusp with independent tangents).
                    var hinL = EnsureHandleList(d, "hin");
                    var houtL = EnsureHandleList(d, "hout");
                    int i = _dragPointIndex;
                    if (i >= 0 && i < hinL.Count && i < houtL.Count)
                    {
                        Vector2 nh = _origPoint + delta;
                        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                        if (_pointDragKind == 1) { hinL[i] = nh; if (!alt) houtL[i] = -nh; }
                        else { houtL[i] = nh; if (!alt) hinL[i] = -nh; }
                        d.SetPoints("hin", hinL);
                        d.SetPoints("hout", houtL);
                        HudSystem.RelayoutElement(v);
                    }
                }
                return;
            }

            if (_dragHandle < 0)
            {
                // Group move: every marquee member follows the same delta, each into its own
                // previewed tier.
                if (_dragOrig.Count > 1)
                {
                    foreach (var view in _views)
                    {
                        Vector2 orig;
                        if (!_dragOrig.TryGetValue(view.Def.Id, out orig)) continue;
                        bool b = UI.Hud.HudElementView.EditBare(view.Def);
                        view.Def.SetXFor(b, _dragMode, Snap(orig.x + delta.x, snap, grid));
                        view.Def.SetYFor(b, _dragMode, Snap(orig.y + delta.y, snap, grid));
                        HudSystem.RelayoutElement(view);
                    }
                    return;
                }
                d.SetXFor(bare, _dragMode, Snap(_origX + delta.x, snap, grid));
                d.SetYFor(bare, _dragMode, Snap(_origY + delta.y, snap, grid));
            }
            else
            {
                var mode = _dragMode;
                // A resized percent-sized element becomes fixed-size: the user just chose
                // exact pixels, and mixing both would make the handles fight the screen.
                if (d.WPctFor(bare, mode) > 0f && ResizesX(_dragHandle)) { if (_origW <= 2f) _origW = v.CanvasRect(scale).width / scale; d.SetWFor(bare, mode, _origW); d.SetWPctFor(bare, mode, -1f); }
                if (d.HPctFor(bare, mode) > 0f && ResizesY(_dragHandle)) { if (_origH <= 2f) _origH = v.CanvasRect(scale).height / scale; d.SetHFor(bare, mode, _origH); d.SetHPctFor(bare, mode, -1f); }

                float dx = delta.x, dy = delta.y;
                // Corner/edge semantics: move the grabbed edge(s); the opposite edge pins.
                bool left = _dragHandle == 0 || _dragHandle == 3 || _dragHandle == 7;
                bool right = _dragHandle == 1 || _dragHandle == 2 || _dragHandle == 5;
                bool bottom = _dragHandle == 0 || _dragHandle == 1 || _dragHandle == 4;
                bool top = _dragHandle == 2 || _dragHandle == 3 || _dragHandle == 6;

                if (right) { float w = Snap(Mathf.Max(4f, _origW + dx), snap, grid); d.SetWFor(bare, mode, w); d.SetXFor(bare, mode, _origX + (w - _origW) * 0.5f); }
                if (left) { float w = Snap(Mathf.Max(4f, _origW - dx), snap, grid); d.SetWFor(bare, mode, w); d.SetXFor(bare, mode, _origX - (w - _origW) * 0.5f); }
                if (top) { float h = Snap(Mathf.Max(4f, _origH + dy), snap, grid); d.SetHFor(bare, mode, h); d.SetYFor(bare, mode, _origY + (h - _origH) * 0.5f); }
                if (bottom) { float h = Snap(Mathf.Max(4f, _origH - dy), snap, grid); d.SetHFor(bare, mode, h); d.SetYFor(bare, mode, _origY - (h - _origH) * 0.5f); }

                // A Shape SCALES its points with the box, so a corner drag grows the drawn contour
                // (its points are absolute, not proportional to W/H). Scales about the centre.
                if (d.Type == UI.Hud.HudElementType.Shape && _origPts.Count > 0 && _origW > 0.01f && _origH > 0.01f)
                {
                    float sx = d.WFor(bare, mode) / _origW;
                    float sy = d.HFor(bare, mode) / _origH;
                    var pl = new List<Vector2>(_origPts.Count);
                    for (int i = 0; i < _origPts.Count; i++)
                        pl.Add(new Vector2(_origPts[i].x * sx, _origPts[i].y * sy));
                    d.SetPoints("pts", pl);
                    // Bézier handles scale with the box too, so a curve keeps its shape on resize.
                    if (_origHin.Count == _origPts.Count && _origHout.Count == _origPts.Count)
                    {
                        var hi = new List<Vector2>(_origHin.Count);
                        var ho = new List<Vector2>(_origHout.Count);
                        for (int i = 0; i < _origHin.Count; i++)
                        {
                            hi.Add(new Vector2(_origHin[i].x * sx, _origHin[i].y * sy));
                            ho.Add(new Vector2(_origHout[i].x * sx, _origHout[i].y * sy));
                        }
                        d.SetPoints("hin", hi);
                        d.SetPoints("hout", ho);
                    }
                }
            }
            HudSystem.RelayoutElement(v);
        }

        private static bool ResizesX(int h) => h != 4 && h != 6;
        private static bool ResizesY(int h) => h != 5 && h != 7;

        private static float Snap(float v, bool on, float grid)
            => on ? Mathf.Round(v / grid) * grid : v;

        /// <summary>The 8 handle anchor points of a rect (canvas coords), order:
        /// corners BL,BR,TR,TL then edge mids B,R,T,L.</summary>
        internal static Vector2 HandlePoint(Rect r, int i)
        {
            switch (i)
            {
                case 0: return new Vector2(r.xMin, r.yMin);
                case 1: return new Vector2(r.xMax, r.yMin);
                case 2: return new Vector2(r.xMax, r.yMax);
                case 3: return new Vector2(r.xMin, r.yMax);
                case 4: return new Vector2(r.center.x, r.yMin);
                case 5: return new Vector2(r.xMax, r.center.y);
                case 6: return new Vector2(r.center.x, r.yMax);
                default: return new Vector2(r.xMin, r.center.y);
            }
        }

        /// <summary>Canvas point -> screen px (Input.mousePosition space), FORWARD-warped
        /// so gizmos hug curved elements.</summary>
        internal static Vector2 CanvasToScreen(Vector2 canvas)
        {
            var w = HudWarp.WarpPoint(canvas);
            return new Vector2(w.x + Screen.width * 0.5f, w.y + Screen.height * 0.5f);
        }

        private static int HandleAt(UI.Hud.HudElementView v, Vector2 mouseScreen, float scale)
        {
            var r = v.CanvasRect(scale);
            for (int i = 0; i < 8; i++)
            {
                var s = CanvasToScreen(HandlePoint(r, i));
                if ((s - mouseScreen).sqrMagnitude <= HandleScreenR * HandleScreenR * 4f)
                    return i;
            }
            return -1;
        }

        // ---------------- point-edit sub-mode (drag / add / delete anchors) ----------------

        /// <summary>Route a click while editing points: on an anchor → drag it (Alt = delete);
        /// otherwise, if it lands near a segment → insert an anchor there.</summary>
        private static void HandlePointEditClick(UI.Hud.HudElementView v, Vector2 mouseScreen, Vector2 p, float scale)
        {
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool bezier = v.Def.GetI("curveMode", v.Def.GetB("smooth", false) ? 1 : 0) == 2;

            // Bézier: a curve HANDLE (in/out) grabs before its anchor.
            if (bezier)
            {
                int kind; int hi = BezierHandleAt(v, mouseScreen, scale, out kind);
                if (hi >= 0) { BeginPointDrag(v, p, hi, kind); return; }
            }

            int pi = PointHandleAt(v, mouseScreen, scale);
            if (pi >= 0)
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (alt) DeleteShapePoint(v, pi);            // Alt+click an anchor deletes it
                else if (ctrl && bezier) ToggleCornerPoint(v, pi); // Ctrl+click = corner/smooth toggle
                else BeginPointDrag(v, p, pi, 0);
                return;
            }
            TryInsertShapePoint(v, p, scale);           // clicking a segment inserts an anchor
        }

        /// <summary>The Bézier handle (in/out) under the mouse, or -1. <paramref name="kind"/>: 1=in, 2=out.</summary>
        private static int BezierHandleAt(UI.Hud.HudElementView v, Vector2 mouseScreen, float scale, out int kind)
        {
            kind = 0;
            var pts = v.Def.GetPoints("pts");
            var hin = v.Def.GetPoints("hin");
            var hout = v.Def.GetPoints("hout");
            for (int i = 0; i < pts.Length; i++)
            {
                if (i < hout.Length)
                {
                    var s = CanvasToScreen(PointCanvas(v, pts[i] + hout[i], scale));
                    if ((s - mouseScreen).sqrMagnitude <= HandleScreenR * HandleScreenR * 4f) { kind = 2; return i; }
                }
                if (i < hin.Length)
                {
                    var s = CanvasToScreen(PointCanvas(v, pts[i] + hin[i], scale));
                    if ((s - mouseScreen).sqrMagnitude <= HandleScreenR * HandleScreenR * 4f) { kind = 1; return i; }
                }
            }
            return -1;
        }

        /// <summary>Anchor screen positions: element centre + point·scale, forward-warped so they
        /// hug the curved shape (matches the gizmo, which draws through the same transform).</summary>
        internal static Vector2 PointCanvas(UI.Hud.HudElementView v, Vector2 pt, float scale)
            => v.CanvasRect(scale).center + pt * scale;

        private static int PointHandleAt(UI.Hud.HudElementView v, Vector2 mouseScreen, float scale)
        {
            var pts = v.Def.GetPoints("pts");
            for (int i = 0; i < pts.Length; i++)
            {
                var s = CanvasToScreen(PointCanvas(v, pts[i], scale));
                if ((s - mouseScreen).sqrMagnitude <= HandleScreenR * HandleScreenR * 4f) return i;
            }
            return -1;
        }

        private static void BeginPointDrag(UI.Hud.HudElementView v, Vector2 p, int index, int kind)
        {
            var pts = v.Def.GetPoints("pts");
            if (index < 0 || index >= pts.Length) return;
            _dragging = true;
            _dragHandle = PointHandle;
            _dragPointIndex = index;
            _pointDragKind = kind;
            _dragStart = p;
            _dragMoved = false;
            if (kind == 1) { var h = v.Def.GetPoints("hin"); _origPoint = index < h.Length ? h[index] : Vector2.zero; }
            else if (kind == 2) { var h = v.Def.GetPoints("hout"); _origPoint = index < h.Length ? h[index] : Vector2.zero; }
            else _origPoint = pts[index];
            ArmDragSnapshot();
        }

        /// <summary>A handle array padded (with zero) to the current point count, so a partially
        /// initialised Bézier shape can still be dragged without index gaps.</summary>
        private static List<Vector2> EnsureHandleList(UI.Hud.HudElementDef d, string key)
        {
            int n = d.GetPoints("pts").Length;
            var src = d.GetPoints(key);
            var list = new List<Vector2>(n);
            for (int i = 0; i < n; i++) list.Add(i < src.Length ? src[i] : Vector2.zero);
            return list;
        }

        /// <summary>Ctrl+click on a Bézier anchor: toggle it between a CORNER point (both handles
        /// zeroed — its adjoining segments run STRAIGHT, so one shape can mix straight lines and
        /// curves) and a smooth point (handles re-seeded from the neighbour tangents). A zero-handle
        /// cubic degenerates to a line, so no renderer change is needed.</summary>
        private static void ToggleCornerPoint(UI.Hud.HudElementView v, int index)
        {
            var d = v.Def;
            var pts = d.GetPoints("pts");
            if (index < 0 || index >= pts.Length) return;
            PushUndoNow();
            var hin = EnsureHandleList(d, "hin");
            var hout = EnsureHandleList(d, "hout");
            bool isCorner = hin[index].sqrMagnitude < 0.01f && hout[index].sqrMagnitude < 0.01f;
            if (!isCorner)
            {
                hin[index] = Vector2.zero;   // -> corner: both adjoining segments straighten
                hout[index] = Vector2.zero;
            }
            else
            {
                int m = pts.Length;          // -> smooth: re-seed gentle tangents from neighbours
                Vector2 tan = (pts[(index + 1) % m] - pts[(index - 1 + m) % m]) * 0.16f;
                hout[index] = tan;
                hin[index] = -tan;
            }
            d.SetPoints("hin", hin);
            d.SetPoints("hout", hout);
            Features.HudProfileStore.MarkChanged();
            HudSystem.RelayoutElement(v);
        }

        private static void DeleteShapePoint(UI.Hud.HudElementView v, int index)
        {
            var d = v.Def;
            var pts = new List<Vector2>(d.GetPoints("pts"));
            int min = d.Type == UI.Hud.HudElementType.Shape ? 3 : 2; // a fill needs 3, a line 2
            if (index < 0 || index >= pts.Count || pts.Count <= min) return;
            PushUndoNow();
            pts.RemoveAt(index);
            d.SetPoints("pts", pts);
            RemoveHandleAt(d, "hin", index);   // keep Bézier handles index-aligned
            RemoveHandleAt(d, "hout", index);
            RebaseShape(d);
            Features.HudProfileStore.MarkChanged();
            HudSystem.RelayoutElement(v);
        }

        private static void RemoveHandleAt(UI.Hud.HudElementDef d, string key, int index)
        {
            var h = d.GetPoints(key);
            if (index < 0 || index >= h.Length) return;
            var list = new List<Vector2>(h);
            list.RemoveAt(index);
            d.SetPoints(key, list);
        }

        private static void InsertHandleAt(UI.Hud.HudElementDef d, string key, int index)
        {
            var h = d.GetPoints(key);
            if (h.Length == 0) return; // no handles set (straight/smooth) → nothing to keep aligned
            var list = new List<Vector2>(h);
            index = Mathf.Clamp(index, 0, list.Count);
            list.Insert(index, Vector2.zero);
            d.SetPoints(key, list);
        }

        private static bool TryInsertShapePoint(UI.Hud.HudElementView v, Vector2 p, float scale)
        {
            var d = v.Def;
            var pts = d.GetPoints("pts");
            if (pts.Length < 2) return false;
            // Work in element-relative reference px (the space `pts` live in).
            Vector2 pe = (p - v.CanvasRect(scale).center) / Mathf.Max(0.01f, scale);
            bool closed = d.Type == UI.Hud.HudElementType.Shape || d.GetB("closed", false);
            int segs = closed ? pts.Length : pts.Length - 1;
            float best = float.MaxValue; int bestSeg = -1; Vector2 bestProj = pe;
            for (int i = 0; i < segs; i++)
            {
                Vector2 proj = ClosestOnSegment(pe, pts[i], pts[(i + 1) % pts.Length]);
                float dd = (proj - pe).sqrMagnitude;
                if (dd < best) { best = dd; bestSeg = i; bestProj = proj; }
            }
            if (bestSeg < 0 || best > 14f * 14f) return false; // only when the click is on a segment
            PushUndoNow();
            var list = new List<Vector2>(pts);
            list.Insert(bestSeg + 1, bestProj);
            d.SetPoints("pts", list);
            InsertHandleAt(d, "hin", bestSeg + 1);   // keep Bézier handles index-aligned
            InsertHandleAt(d, "hout", bestSeg + 1);
            RebaseShape(d);
            Features.HudProfileStore.MarkChanged();
            HudSystem.RelayoutElement(v);
            return true;
        }

        private static Vector2 ClosestOnSegment(Vector2 pt, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return a;
            float t = Mathf.Clamp01(Vector2.Dot(pt - a, ab) / len2);
            return a + ab * t;
        }

        /// <summary>Re-centre a shape's points on their bbox centre (shifting X/Y so it stays put
        /// visually) and refit W/H to the extents, so the selection rect and resize handles keep
        /// hugging it after a point edit.</summary>
        private static void RebaseShape(UI.Hud.HudElementDef d)
        {
            var pts = d.GetPoints("pts");
            if (pts.Length == 0) return;
            Vector2 min = pts[0], max = pts[0];
            for (int i = 1; i < pts.Length; i++) { min = Vector2.Min(min, pts[i]); max = Vector2.Max(max, pts[i]); }
            Vector2 mid = (min + max) * 0.5f;
            if (mid.sqrMagnitude > 1e-4f)
            {
                var rel = new List<Vector2>(pts.Length);
                for (int i = 0; i < pts.Length; i++) rel.Add(pts[i] - mid);
                d.SetPoints("pts", rel);
                d.X += mid.x; d.Y += mid.y; // element X/Y and points share reference-px units
            }
            d.W = Mathf.Max(8f, max.x - min.x);
            d.H = Mathf.Max(8f, max.y - min.y);
        }

        // ---------------- structural edits (all one-undo-step, all rebuild views) ----

        private static void PushUndoNow()
        {
            var doc = Features.HudProfileStore.Active;
            if (doc != null) UI.Hud.HudDocumentHistory.Push(doc.Clone());
        }

        /// <summary>Complete a synchronous document mutation without manufacturing a dead undo
        /// step. The caller supplies the pre-edit clone, mutates the live document, then calls
        /// here; only a real serialized change clears redo and arms autosave.</summary>
        private static bool CommitDocumentMutation(UI.Hud.HudDocument before)
        {
            var current = Features.HudProfileStore.Active;
            if (before == null || current == null || DocumentsEqual(current, before)) return false;
            UI.Hud.HudDocumentHistory.Push(before);
            Features.HudProfileStore.MarkChanged();
            return true;
        }

        /// <summary>Bind the pre-edit snapshot to the exact document/profile being dragged so
        /// later interruptions cannot mix its history or persistence into a replacement.</summary>
        private static void ArmDragSnapshot()
        {
            _dragDocument = Features.HudProfileStore.Active;
            _preDrag = _dragDocument != null ? _dragDocument.Clone() : null;
            _dragProfileName = ActiveProfileName();
        }

        /// <summary>Finish an in-flight canvas gesture before another command changes editor
        /// state. A real change becomes exactly one undo step; returning to the precise pre-drag
        /// document creates no dead history entry. If a profile swap already happened, save the
        /// old document directly rather than contaminating the new profile's history.</summary>
        private static void CommitActiveDrag()
        {
            if (!_dragging && _preDrag == null) return;

            var dragged = _dragDocument;
            bool changed = dragged != null && _preDrag != null && !DocumentsEqual(dragged, _preDrag);

            // Point drags defer bbox re-centering until the gesture ends. Do it only after an
            // actual point change; a move-away-and-back must remain a true no-op.
            if (changed && _dragHandle == PointHandle)
            {
                var def = FindElement(dragged, _selectedId);
                if (def != null)
                {
                    RebaseShape(def);
                    if (ReferenceEquals(Features.HudProfileStore.Active, dragged))
                    {
                        if (SelectedElement != null && ReferenceEquals(SelectedElement.Def, def))
                            HudSystem.RelayoutElement(SelectedElement);
                        else
                            HudSystem.RequestViewRebuild();
                    }
                    changed = !DocumentsEqual(dragged, _preDrag);
                }
            }

            if (changed)
            {
                if (ReferenceEquals(Features.HudProfileStore.Active, dragged))
                {
                    UI.Hud.HudDocumentHistory.Push(_preDrag);
                    Features.HudProfileStore.MarkChanged();
                }
                else if (!string.IsNullOrEmpty(_dragProfileName))
                {
                    // The active history belongs to the replacement document. Persist the old
                    // partial drag without putting its snapshot on that unrelated undo stack.
                    Features.HudProfileStore.Save(dragged, _dragProfileName);
                }
            }

            ClearDragState();
        }

        private static void ClearDragState()
        {
            _dragging = false;
            _dragMoved = false;
            _preDrag = null;
            _dragDocument = null;
            _dragProfileName = null;
            _dragHandle = -1;
            _dragPointIndex = -1;
            _pointDragKind = 0;
            _dragOrig.Clear();
            _origPts.Clear();
            _origHin.Clear();
            _origHout.Clear();
        }

        private static UI.Hud.HudElementDef FindElement(UI.Hud.HudDocument doc, string id)
        {
            if (doc == null || doc.Elements == null || id == null) return null;
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var e = doc.Elements[i];
                if (e != null && e.Id == id) return e;
            }
            return null;
        }

        internal static bool DocumentsEqual(UI.Hud.HudDocument a, UI.Hud.HudDocument b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Schema != b.Schema || a.Name != b.Name
                || a.Font != b.Font || a.Description != b.Description || a.Author != b.Author)
                return false;
            if (a.Elements == null || b.Elements == null) return a.Elements == b.Elements;
            if (a.Elements.Count != b.Elements.Count) return false;
            for (int i = 0; i < a.Elements.Count; i++)
                if (!ElementsEqual(a.Elements[i], b.Elements[i])) return false;
            return true;
        }

        private static bool ElementsEqual(UI.Hud.HudElementDef a, UI.Hud.HudElementDef b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Id != b.Id || a.Type != b.Type || a.Anchor != b.Anchor
                || a.X != b.X || a.Y != b.Y || a.W != b.W || a.H != b.H
                || a.WPct != b.WPct || a.HPct != b.HPct || a.Z != b.Z || a.Tiers != b.Tiers
                || a.Fill != b.Fill || a.Border != b.Border || a.TextColor != b.TextColor
                || a.BorderWidth != b.BorderWidth || a.RTL != b.RTL || a.RTR != b.RTR
                || a.RBR != b.RBR || a.RBL != b.RBL || a.FontScale != b.FontScale
                || a.Text != b.Text || a.Align != b.Align || a.Icon != b.Icon)
                return false;
            if (a.Params == null || b.Params == null) return a.Params == b.Params;
            if (a.Params.Count != b.Params.Count) return false;
            for (int i = 0; i < a.Params.Count; i++)
            {
                var ap = a.Params[i];
                var bp = b.Params[i];
                if (ReferenceEquals(ap, bp)) continue;
                if (ap == null || bp == null || ap.K != bp.K || ap.V != bp.V) return false;
            }
            return true;
        }

        public static void ClearElementSelection()
        {
            _selectedId = null;
            SelectedElement = null;
        }

        public static void DeleteSelected()
        {
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            var v = SelectedElement;
            if (doc == null || v == null) return;
            PushUndoNow();
            // A marquee group deletes together (one undo step).
            if (_multiIds.Count > 1 && _multiIds.Contains(v.Def.Id))
                doc.Elements.RemoveAll(e => e != null && _multiIds.Contains(e.Id));
            else
                doc.Elements.Remove(v.Def);
            _multiIds.Clear();
            _selectedId = null;
            Features.HudProfileStore.MarkChanged();
            HudSystem.RequestViewRebuild();
        }

        /// <summary>Clear the selected element's per-curvature-mode LIVE placement override for the
        /// current mode, reverting it to the base (shared) layout. One undo step.</summary>
        public static void ResetModeLayout()
        {
            var view = SelectedElement;
            if (view == null || view.Def == null) return;
            PushUndoNow();
            view.Def.SetModeLayout(UI.Hud.HudElementView.LayoutMode, false);
            HudSystem.RelayoutElement(view);
            Features.HudProfileStore.MarkChanged();
        }

        public static void DuplicateSelected()
        {
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            var v = SelectedElement;
            if (doc == null || v == null) return;
            PushUndoNow();
            var copy = v.Def.Clone();
            copy.Id = System.Guid.NewGuid().ToString("N");
            copy.X += 14f;
            copy.Y -= 14f;
            doc.Elements.Add(copy);
            _pendingSelectId = copy.Id;
            Features.HudProfileStore.MarkChanged();
            HudSystem.RequestViewRebuild();
        }

        /// <summary>Move every element onto the coherent Global or Custom style contract. Moving
        /// to Custom snapshots each element's currently effective appearance/effects first, so
        /// the bulk action cannot reveal stale profile values or make the HUD jump.</summary>
        public static void SetAllFollowGlobal(bool follow)
        {
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            if (doc == null || doc.Elements == null) return;
            var before = doc.Clone();
            var views = new List<HudElementView>();
            HudSystem.CollectElementViews(views);
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var def = doc.Elements[i];
                if (def == null) continue;
                HudElementView live = null;
                for (int j = 0; j < views.Count; j++)
                {
                    if (views[j] != null && ReferenceEquals(views[j].Def, def))
                    {
                        live = views[j];
                        break;
                    }
                }
                if (live != null) live.SetUnifiedStyleSource(follow, !follow);
                else HudElementView.SetUnifiedStyleSourceWithoutView(def, follow, !follow);
            }
            CommitDocumentMutation(before);
        }

        /// <summary>Make every element's glass match the global sliders without changing its
        /// source contract: Custom receives explicit current values, Legacy restores -1 sentinels,
        /// and coherent Global already follows. A real change is one undo step; an idempotent click
        /// leaves undo/redo untouched.</summary>
        public static void ResetAllGlassToGlobal()
        {
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            if (doc == null || doc.Elements == null) return;
            var before = doc.Clone();
            foreach (var e in doc.Elements)
            {
                if (e == null) continue;
                if (HudElementView.IsCustomStyleDefinition(e))
                {
                    e.SetF("sheen", HudConfig.GlassSheen != null ? HudConfig.GlassSheen.Value : 0f);
                    e.SetF("spec", HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f);
                }
                else if (e.GetI("styleSource", 0) == 0)
                {
                    e.SetF("sheen", -1f);
                    e.SetF("spec", -1f);
                }
            }
            CommitDocumentMutation(before);
        }

        /// <summary>Reset every element's effect values to the current globals. Legacy/Global
        /// elements drop their old override keys; Custom receives explicit values so its complete
        /// local contract remains truthful. A real change is one undo step; an idempotent click
        /// leaves undo/redo untouched.</summary>
        public static void ResetAllElementEffects()
        {
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            if (doc == null || doc.Elements == null) return;
            var before = doc.Clone();
            string[] legacyOnlyKeys =
            {
                "fxShine", "fxShineAmt", "fxIrid", "fxIridAmt",
                "fxDissolve", "fxFrost", "fxFrostAmt", "fxChroma", "fxChromaAmt",
            };
            string[] motionKeys =
            {
                "fxCollapse", "fxCollapseAmt", "fxGlitch", "fxGlitchAmt", "fxWarp", "fxWarpAmt",
                "fxPulse", "fxPulseAmt",
            };
            string[] inheritedEffectKeys =
            {
                "bfade", "softEdge", "glow", "glowIn", "glowWidth", "glowDiffuse",
                "ripple", "rippleFreq", "rippleSmooth", "edgeFlow", "frostDepth",
            };
            foreach (var e in doc.Elements)
            {
                if (e == null) continue;
                for (int i = 0; i < legacyOnlyKeys.Length; i++) e.Set(legacyOnlyKeys[i], null);

                // Custom is a complete explicit snapshot, so never remove its keys and let the
                // runtime silently fall through to globals while the inspector shows defaults.
                if (!HudElementView.IsCustomStyleDefinition(e))
                {
                    for (int i = 0; i < inheritedEffectKeys.Length; i++)
                        e.Set(inheritedEffectKeys[i], null);
                    for (int i = 0; i < motionKeys.Length; i++) e.Set(motionKeys[i], null);
                    continue;
                }
                e.SetB("customBorderFadeOn", HudConfig.FxBorderFadeOn != null && HudConfig.FxBorderFadeOn.Value);
                e.SetB("customSoftEdgeOn", HudConfig.FxSoftEdgeOn != null && HudConfig.FxSoftEdgeOn.Value);
                e.SetB("customGlowOn", HudConfig.FxGlowOn != null && HudConfig.FxGlowOn.Value);
                e.SetB("customRippleOn", HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value);
                e.SetF("bfade", HudConfig.FxBorderFade != null ? HudConfig.FxBorderFade.Value : 0f);
                e.SetF("softEdge", HudConfig.FxSoftEdge != null ? HudConfig.FxSoftEdge.Value : 0f);
                e.SetF("glow", HudConfig.FxGlow != null ? HudConfig.FxGlow.Value : 0f);
                e.SetF("glowIn", HudConfig.FxGlowInner != null ? HudConfig.FxGlowInner.Value : 0f);
                e.SetF("glowWidth", HudConfig.FxGlowWidth != null ? HudConfig.FxGlowWidth.Value : 24f);
                e.SetF("glowDiffuse", HudConfig.FxGlowDiffuse != null ? HudConfig.FxGlowDiffuse.Value : 0f);
                e.SetF("ripple", HudConfig.FxEdgeRipple != null ? HudConfig.FxEdgeRipple.Value : 0f);
                e.SetF("rippleFreq", HudConfig.FxEdgeRippleFreq != null ? HudConfig.FxEdgeRippleFreq.Value : 2f);
                e.SetF("rippleSmooth", 0f);
                e.SetF("edgeFlow", HudConfig.FxEdgeFlowSpeed != null ? HudConfig.FxEdgeFlowSpeed.Value : 0.22f);
                e.SetF("frostDepth", HudConfig.FrostDepth != null ? HudConfig.FrostDepth.Value : 1f);
                e.SetB("customShineOn", HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value);
                e.SetF("customShine", HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f);
                e.SetB("customIridOn", HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value);
                e.SetF("customIrid", HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f);
                e.SetB("customChromaOn", HudConfig.FxChromaOn != null && HudConfig.FxChromaOn.Value);
                e.SetF("customChroma", HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f);
                e.SetB("customFrostOn", true);
                e.SetF("customFrost", HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f);
                e.SetB("customDissolve", HudConfig.FxDissolveBoot != null && HudConfig.FxDissolveBoot.Value);
                e.SetB("fxCollapse", true);
                e.SetF("fxCollapseAmt", 1f);
                e.SetB("fxGlitch", true);
                e.SetF("fxGlitchAmt", 1f);
                e.SetB("fxWarp", true);
                e.SetF("fxWarpAmt", 1f);
                e.SetB("fxPulse", false);
                e.SetF("fxPulseAmt", 1f);
                e.SetB("customStyleReady", true);
            }
            CommitDocumentMutation(before);
        }

        /// <summary>Strip the "glassy" look from one element: zero its sheen (milky fill), edge
        /// light (spec — the bright border whitening), and glow, so it renders as a plain,
        /// slightly-transparent bordered box. Fill / border / corners / feather are left alone.
        /// Written as an explicit 0 (NOT -1 = follow global), so it stays flat regardless of the
        /// global glass sliders.</summary>
        private static void StripGlass(UI.Hud.HudElementDef d)
        {
            if (d == null || !HudElementView.CanFlattenDefinition(d)) return;
            // Flat is itself a deliberate local design. Snapshot the currently visible theme
            // first so a Global element keeps its colours/geometry, then disable every optical
            // layer explicitly instead of writing sentinels that Global would ignore.
            HudElementView.SetUnifiedStyleSourceWithoutView(d, false, true);
            d.SetF("sheen", 0f);
            d.SetF("spec", 0f);       // 0 (not -1) = also opts out of the GLOBAL edge-light boost
            d.SetF("glow", 0f);
            d.SetF("glowIn", 0f);
            d.SetF("softEdge", 0f);
            d.SetF("bfade", 0f);      // solid border, no light-driven dissolve
            d.SetF("ripple", 0f);     // no edge shimmer
            d.SetB("customBorderFadeOn", false);
            d.SetB("customSoftEdgeOn", false);
            d.SetB("customGlowOn", false);
            d.SetB("customRippleOn", false);
            d.SetB("customShineOn", false);
            d.SetF("customShine", 0f);
            d.SetB("customIridOn", false);
            d.SetF("customIrid", 0f);
            d.SetB("customChromaOn", false);
            d.SetF("customChroma", 0f);
            d.SetB("customFrostOn", false);
            d.SetF("customFrost", 0f);
            d.SetB("customDissolve", false);
        }

        /// <summary>Flatten the SELECTED element (see <see cref="StripGlass"/>). One undo step.</summary>
        public static void MakeSelectedFlat()
        {
            CommitActiveDrag();
            var view = SelectedElement;
            if (view == null || view.Def == null) return;
            var doc = Features.HudProfileStore.Active;
            if (doc == null) return;
            var before = doc.Clone();
            StripGlass(view.Def);
            CommitDocumentMutation(before);
        }

        /// <summary>Flatten EVERY element in the active profile (the one-click "make the whole HUD
        /// plain bordered boxes"). One undo step.</summary>
        public static void MakeAllFlat()
        {
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            if (doc == null || doc.Elements == null) return;
            var before = doc.Clone();
            foreach (var e in doc.Elements)
                if (e != null) StripGlass(e);
            CommitDocumentMutation(before);
        }

        /// <summary>Add a fresh element of the given type at screen centre, selected.</summary>
        public static void AddElement(UI.Hud.HudElementType type)
        {
            var doc = Features.HudProfileStore.Active;
            if (doc == null) return;
            PushUndoNow();
            var e = new UI.Hud.HudElementDef
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Type = type,
                Anchor = UI.Hud.HudAnchor.Center,
                X = 0f, Y = 0f,
                W = DefaultW(type), H = DefaultH(type),
                Z = 50,
            };
            if (type == UI.Hud.HudElementType.Label) e.Text = "NEW LABEL";
            if (type == UI.Hud.HudElementType.Icon) e.Icon = "Gauge";
            if (type == UI.Hud.HudElementType.Readout)
            {
                // A friendly instrument out of the box: compact row, boxed, no bar.
                e.Set("src", "Speed");
                e.Set("label", "SPEED");
                e.Icon = "speed";
                e.SetB("box", true);
                e.SetB("bar", false);
                e.SetB("target", false);
                // Instruments are suit-tier: they must vanish in the power-off (bare) HUD
                // like the shipped speed box does (play-test: a re-created box kept showing
                // in bare because the default was All).
                e.Tiers = UI.Hud.HudTierMask.Suited | UI.Hud.HudTierMask.Robot;
            }

            // On a Glassy-family profile, a fresh element arrives in the profile's own
            // glass dress (literal colours + sheen/spec) instead of raw palette defaults,
            // so a re-created box matches its siblings without hand-restyling.
            string prof = Features.HudProfileStore.Active != null ? Features.HudProfileStore.Active.Name : null;
            bool glassy = prof != null && prof.IndexOf("Glassy", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (glassy)
            {
                switch (type)
                {
                    case UI.Hud.HudElementType.Box:
                    case UI.Hud.HudElementType.Readout:
                    case UI.Hud.HudElementType.Compass:
                    case UI.Hud.HudElementType.EquipmentColumn:
                    case UI.Hud.HudElementType.HandBoxes:
                    case UI.Hud.HudElementType.KeybindChips:
                    case UI.Hud.HudElementType.SuitChips:
                    case UI.Hud.HudElementType.Clock:
                    case UI.Hud.HudElementType.WorldName:
                    case UI.Hud.HudElementType.DayCounter:
                    case UI.Hud.HudElementType.ActiveHandBadge:
                    case UI.Hud.HudElementType.VitalsPanel:
                    case UI.Hud.HudElementType.DamageDoll:
                    case UI.Hud.HudElementType.JetpackBox:
                    case UI.Hud.HudElementType.StateChips:
                    case UI.Hud.HudElementType.PngDoll:
                        e.Fill = "#05080DA6";
                        e.Border = "#B9BEC259";
                        e.SetF("sheen", 0.5f);
                        e.SetF("spec", 0.8f);
                        break;
                }
            }
            doc.Elements.Add(e);
            _pendingSelectId = e.Id;
            Features.HudProfileStore.MarkChanged();
            HudSystem.RequestViewRebuild();
        }

        private static float DefaultW(UI.Hud.HudElementType t)
        {
            switch (t)
            {
                case UI.Hud.HudElementType.Icon: return 40f;
                case UI.Hud.HudElementType.Readout: return 120f;
                case UI.Hud.HudElementType.Compass: return 240f;
                case UI.Hud.HudElementType.EquipmentColumn: return 84f;
                case UI.Hud.HudElementType.Portrait: return 150f;
                case UI.Hud.HudElementType.VitalsPanel: return 168f;
                case UI.Hud.HudElementType.DamageDoll: return 120f;
                case UI.Hud.HudElementType.JetpackBox: return 168f;
                case UI.Hud.HudElementType.StateChips: return 168f;
                case UI.Hud.HudElementType.PngDoll: return 126f;
                default: return 180f;
            }
        }

        private static float DefaultH(UI.Hud.HudElementType t)
        {
            switch (t)
            {
                case UI.Hud.HudElementType.Icon: return 40f;
                case UI.Hud.HudElementType.Readout: return 120f;
                case UI.Hud.HudElementType.Compass: return 46f;
                case UI.Hud.HudElementType.EquipmentColumn: return 520f;
                case UI.Hud.HudElementType.Portrait: return 150f;
                case UI.Hud.HudElementType.VitalsPanel: return 140f;
                case UI.Hud.HudElementType.DamageDoll: return 150f;
                case UI.Hud.HudElementType.JetpackBox: return 128f;
                case UI.Hud.HudElementType.StateChips: return 40f;
                case UI.Hud.HudElementType.PngDoll: return 192f;
                default: return 60f;
            }
        }

        public static void DoUndo()
        {
            // Commit first so Ctrl+Z during a drag immediately restores its pre-drag geometry.
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            if (doc == null || !UI.Hud.HudDocumentHistory.CanUndo) return;
            var prev = UI.Hud.HudDocumentHistory.Undo(doc);
            if (prev == null) return;
            Features.HudProfileStore.SetActive(prev, ActiveProfileName());
            Features.HudProfileStore.MarkChanged();
            _selectedId = null;
        }

        public static void DoRedo()
        {
            // A changed drag is a new history branch and therefore correctly clears redo.
            CommitActiveDrag();
            var doc = Features.HudProfileStore.Active;
            if (doc == null || !UI.Hud.HudDocumentHistory.CanRedo) return;
            var next = UI.Hud.HudDocumentHistory.Redo(doc);
            if (next == null) return;
            Features.HudProfileStore.SetActive(next, ActiveProfileName());
            Features.HudProfileStore.MarkChanged();
            _selectedId = null;
        }

        internal static string ActiveProfileName()
            => HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : "Default";

        // ---------------- draw-a-line tool ----------------

        public static void BeginDrawLine()
        {
            DrawingLine = true;
            _drawPts.Clear();
        }

        public static void CancelDrawLine()
        {
            DrawingLine = false;
            _drawPts.Clear();
        }

        internal static IList<Vector2> DrawPoints => _drawPts;

        private static void UpdateDrawLine(Vector2 p, bool imguiOwnsMouse, float scale)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelDrawLine(); return; }
            bool commit = Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(1);
            if (!commit && !imguiOwnsMouse && Input.GetMouseButtonDown(0))
                _drawPts.Add(p);
            if (!commit) return;

            DrawingLine = false;
            if (_drawPts.Count < 2) { _drawPts.Clear(); return; }
            var doc = Features.HudProfileStore.Active;
            if (doc == null) { _drawPts.Clear(); return; }

            // Re-base the points around their centroid so the element's X/Y IS the line's
            // middle and the handles land somewhere sensible.
            Vector2 min = _drawPts[0], max = _drawPts[0];
            for (int i = 1; i < _drawPts.Count; i++)
            {
                min = Vector2.Min(min, _drawPts[i]);
                max = Vector2.Max(max, _drawPts[i]);
            }
            Vector2 mid = (min + max) * 0.5f;
            float inv = 1f / Mathf.Max(0.01f, scale);
            var rel = new List<Vector2>(_drawPts.Count);
            for (int i = 0; i < _drawPts.Count; i++) rel.Add((_drawPts[i] - mid) * inv);

            PushUndoNow();
            var e = new UI.Hud.HudElementDef
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Type = UI.Hud.HudElementType.Polyline,
                Anchor = UI.Hud.HudAnchor.Center,
                X = mid.x * inv, Y = mid.y * inv,
                W = Mathf.Max(8f, (max.x - min.x) * inv),
                H = Mathf.Max(8f, (max.y - min.y) * inv),
                Z = 60,
            };
            e.SetPoints("pts", rel);
            e.SetF("width", 2f);
            doc.Elements.Add(e);
            _drawPts.Clear();
            _pendingSelectId = e.Id;
            Features.HudProfileStore.MarkChanged();
            HudSystem.RequestViewRebuild();
        }

        // ---------------- pen tool (draw a filled Shape) ----------------

        public static void BeginDrawShape()
        {
            CancelDrawLine();
            DrawingShape = true;
            _drawPts.Clear();
        }

        public static void CancelDrawShape()
        {
            DrawingShape = false;
            _drawPts.Clear();
        }

        /// <summary>Pen placement: left-click lays a point; clicking the FIRST point (once there
        /// are ≥3) or Enter/RMB CLOSES the path into a filled <see cref="UI.Hud.HudElementType.Shape"/>;
        /// Escape cancels. Shares <see cref="_drawPts"/> and the centroid-rebase with the line tool.</summary>
        private static void UpdateDrawShape(Vector2 p, bool imguiOwnsMouse, float scale)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelDrawShape(); return; }

            // Clicking back on the first dot closes the loop (Illustrator's pen-close).
            bool nearFirst = _drawPts.Count >= 3 && (p - _drawPts[0]).sqrMagnitude < 12f * 12f;
            bool leftClick = !imguiOwnsMouse && Input.GetMouseButtonDown(0);
            bool commit = Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(1) || (nearFirst && leftClick);

            if (!commit && leftClick) _drawPts.Add(p);
            if (!commit) return;

            DrawingShape = false;
            if (_drawPts.Count < 3) { _drawPts.Clear(); return; } // a fill needs three points
            var doc = Features.HudProfileStore.Active;
            if (doc == null) { _drawPts.Clear(); return; }

            // Re-base points around their centroid so the element's X/Y IS the shape's middle
            // and the resize handles hug it (identical to the line tool).
            Vector2 min = _drawPts[0], max = _drawPts[0];
            for (int i = 1; i < _drawPts.Count; i++)
            {
                min = Vector2.Min(min, _drawPts[i]);
                max = Vector2.Max(max, _drawPts[i]);
            }
            Vector2 mid = (min + max) * 0.5f;
            float inv = 1f / Mathf.Max(0.01f, scale);
            var rel = new List<Vector2>(_drawPts.Count);
            for (int i = 0; i < _drawPts.Count; i++) rel.Add((_drawPts[i] - mid) * inv);

            PushUndoNow();
            var e = new UI.Hud.HudElementDef
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Type = UI.Hud.HudElementType.Shape,
                Anchor = UI.Hud.HudAnchor.Center,
                X = mid.x * inv, Y = mid.y * inv,
                W = Mathf.Max(8f, (max.x - min.x) * inv),
                H = Mathf.Max(8f, (max.y - min.y) * inv),
                Z = 55,
            };
            e.SetPoints("pts", rel);
            e.SetB("closed", true);
            ApplyGlassyDress(e);   // born in the profile's glass dress, like Add-element does
            doc.Elements.Add(e);
            _drawPts.Clear();
            _pendingSelectId = e.Id;
            Features.HudProfileStore.MarkChanged();
            HudSystem.RequestViewRebuild();
        }

        /// <summary>On a Glassy-family profile, dress a fresh element in the profile's own glass
        /// (literal fill/border + sheen/spec) so it matches its siblings without hand-restyling.
        /// Mirrors the switch in <see cref="AddElement"/>.</summary>
        private static void ApplyGlassyDress(UI.Hud.HudElementDef e)
        {
            string prof = Features.HudProfileStore.Active != null ? Features.HudProfileStore.Active.Name : null;
            bool glassy = prof != null && prof.IndexOf("Glassy", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!glassy) return;
            e.Fill = "#05080DA6";
            e.Border = "#B9BEC259";
            e.SetF("sheen", 0.5f);
            e.SetF("spec", 0.8f);
        }

        /// <summary>Inverse of the screen warp — shared with the radial drop zones.</summary>
        private static Vector2 Unwarp(Vector2 p) => HudWarp.Unwarp(p);

        private static void EnsureBackdrop()
        {
            if (_dimCanvas != null) return;
            var go = new GameObject("UIAscended_HudEditorDim");
            Object.DontDestroyOnLoad(go);
            _dimCanvas = go.AddComponent<Canvas>();
            _dimCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _dimCanvas.sortingOrder = 3750; // just under the HUD canvas (3800)

            var igo = new GameObject("Dim", typeof(RectTransform));
            igo.transform.SetParent(go.transform, false);
            _dim = igo.AddComponent<Image>();
            _dim.raycastTarget = false;
            var rt = _dim.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
