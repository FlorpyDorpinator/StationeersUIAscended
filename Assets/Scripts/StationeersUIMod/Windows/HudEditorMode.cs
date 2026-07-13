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
            CancelDrawLine();
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
        private static UI.Hud.HudDocument _preDrag; // undo snapshot armed at mouse-down

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
                    PushUndoNow();
                    foreach (var v in _views)
                    {
                        bool inSel = (SelectedElement != null && ReferenceEquals(v, SelectedElement))
                            || _multiIds.Contains(v.Def.Id);
                        if (!inSel) continue;
                        v.Def.X += nx * step;
                        v.Def.Y += ny * step;
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

            // Hover: smallest element rect containing the unwarped point.
            HoverElement = null;
            float bestArea = float.MaxValue;
            if (!imguiOwnsMouse && !_dragging)
            {
                foreach (var v in _views)
                {
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
                UpdateDrag(p, scale);
                if (Input.GetMouseButtonUp(0))
                {
                    _dragging = false;
                    _dragOrig.Clear();
                    if (_dragMoved)
                    {
                        // One gesture = one undo step + one full relayout via the store.
                        UI.Hud.HudDocumentHistory.Push(_preDrag);
                        Features.HudProfileStore.MarkChanged();
                    }
                    _preDrag = null;
                }
                return;
            }

            if (imguiOwnsMouse || !Input.GetMouseButtonDown(0)) return;

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
            _origX = v.Def.X; _origY = v.Def.Y; _origW = v.Def.W; _origH = v.Def.H;

            // Moving a marquee-group member moves the WHOLE group (resize stays single).
            _dragOrig.Clear();
            if (handle < 0 && _multiIds.Count > 1 && _multiIds.Contains(v.Def.Id))
                foreach (var view in _views)
                    if (_multiIds.Contains(view.Def.Id))
                        _dragOrig[view.Def.Id] = new Vector2(view.Def.X, view.Def.Y);

            var doc = Features.HudProfileStore.Active;
            _preDrag = doc != null ? doc.Clone() : null;
        }

        private static void UpdateDrag(Vector2 p, float scale)
        {
            var v = SelectedElement;
            if (v == null) { _dragging = false; return; }
            var d = v.Def;
            Vector2 delta = (p - _dragStart) / Mathf.Max(0.01f, scale);
            if (!_dragMoved && delta.sqrMagnitude < 4f) return; // 2px dead zone
            _dragMoved = true;

            bool snap = HudConfig.GridSnapEnabled != null && HudConfig.GridSnapEnabled.Value
                && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            float grid = HudConfig.GridSnapSize != null ? Mathf.Max(1f, HudConfig.GridSnapSize.Value) : 8f;

            if (_dragHandle < 0)
            {
                // Group move: every marquee member follows the same delta.
                if (_dragOrig.Count > 1)
                {
                    foreach (var view in _views)
                    {
                        Vector2 orig;
                        if (!_dragOrig.TryGetValue(view.Def.Id, out orig)) continue;
                        view.Def.X = Snap(orig.x + delta.x, snap, grid);
                        view.Def.Y = Snap(orig.y + delta.y, snap, grid);
                        HudSystem.RelayoutElement(view);
                    }
                    return;
                }
                d.X = Snap(_origX + delta.x, snap, grid);
                d.Y = Snap(_origY + delta.y, snap, grid);
            }
            else
            {
                // A resized percent-sized element becomes fixed-size: the user just chose
                // exact pixels, and mixing both would make the handles fight the screen.
                if (d.WPct > 0f && ResizesX(_dragHandle)) { d.W = _origW = _origW <= 2f ? v.CanvasRect(scale).width / scale : _origW; d.WPct = -1f; }
                if (d.HPct > 0f && ResizesY(_dragHandle)) { d.H = _origH = _origH <= 2f ? v.CanvasRect(scale).height / scale : _origH; d.HPct = -1f; }

                float dx = delta.x, dy = delta.y;
                // Corner/edge semantics: move the grabbed edge(s); the opposite edge pins.
                bool left = _dragHandle == 0 || _dragHandle == 3 || _dragHandle == 7;
                bool right = _dragHandle == 1 || _dragHandle == 2 || _dragHandle == 5;
                bool bottom = _dragHandle == 0 || _dragHandle == 1 || _dragHandle == 4;
                bool top = _dragHandle == 2 || _dragHandle == 3 || _dragHandle == 6;

                if (right) { d.W = Snap(Mathf.Max(4f, _origW + dx), snap, grid); d.X = _origX + (d.W - _origW) * 0.5f; }
                if (left) { d.W = Snap(Mathf.Max(4f, _origW - dx), snap, grid); d.X = _origX - (d.W - _origW) * 0.5f; }
                if (top) { d.H = Snap(Mathf.Max(4f, _origH + dy), snap, grid); d.Y = _origY + (d.H - _origH) * 0.5f; }
                if (bottom) { d.H = Snap(Mathf.Max(4f, _origH - dy), snap, grid); d.Y = _origY - (d.H - _origH) * 0.5f; }
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

        // ---------------- structural edits (all one-undo-step, all rebuild views) ----

        private static void PushUndoNow()
        {
            var doc = Features.HudProfileStore.Active;
            if (doc != null) UI.Hud.HudDocumentHistory.Push(doc.Clone());
        }

        /// <summary>Any document swap invalidates an in-flight drag — without this, the
        /// release after a mid-drag Ctrl+Z pushes the STALE pre-drag snapshot onto the
        /// undo stack (a future state masquerading as the past) and autosaves the wrong
        /// baseline.</summary>
        private static void CancelDrag()
        {
            _dragging = false;
            _dragMoved = false;
            _preDrag = null;
        }

        public static void ClearElementSelection()
        {
            _selectedId = null;
            SelectedElement = null;
        }

        public static void DeleteSelected()
        {
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

        public static void DuplicateSelected()
        {
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
                default: return 60f;
            }
        }

        public static void DoUndo()
        {
            CancelDrag();
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
            CancelDrag();
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
