using System.Collections.Generic;
using System.Linq;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// Option A "search all bags", radial form: the TOP HALF is one fixed wedge — the word
    /// SEARCH with a text box under it — and the BOTTOM HALF fills with result wedges as
    /// you type. Results behave like any other item wedge: click takes (free hand, else
    /// drops at your feet), press-drag tears the item out into a parking chip, Shift keeps
    /// the search open after a take, and the wheel pages when there are more matches than
    /// wedges.
    ///
    /// Typing is captured raw from Input.inputString (never a focusable field: the modal
    /// already owns the keys, and raw capture cannot lose focus while UGUI clicks are
    /// blocked). Escape / right-click returns to the radial.
    /// </summary>
    public static class SearchPanelView
    {
        public enum Result { None, Exit, Took }

        private const int MaxResultWedges = 9;

        private sealed class RowData
        {
            public ScannedSlot Source;   // pinned
            public string Name;
            public string Location;
            public int Count;
            public Sprite Icon;
            public string State;
        }

        // ---------- state ----------
        private static string _query = "";
        private static string _builtFor;
        private static readonly List<RowData> _rows = new List<RowData>();
        private static int _scroll;              // paging offset into _rows
        private static float _openedAt;
        private static int _hovered = -1;        // index into the VISIBLE window
        private static RowData _press;
        private static float _pressAt;
        private static Vector2 _pressPos;
        private const float DragHoldSec = 0.25f;
        private const float DragMovePx = 14f;

        // ---------- visuals ----------
        private static Canvas _canvas;
        private static RectTransform _root;
        private static RadialWedgeGraphic _topWedge;
        private static TextMeshProUGUI _title;
        private static Image _inputBg;
        private static TextMeshProUGUI _queryText;
        private static CircleGraphic _hub;
        private static TextMeshProUGUI _hubLine1, _hubLine2;
        private static readonly List<WedgeVisual> _pool = new List<WedgeVisual>();

        private sealed class WedgeVisual
        {
            public RadialWedgeGraphic Wedge;
            public Image Icon;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI State;
        }

        public static void Begin()
        {
            _query = "";
            _builtFor = null;
            _scroll = 0;
            _openedAt = Time.unscaledTime;
            _press = null;
            RebuildRows();
        }

        // ---------- input (runs OUTSIDE the ImGui frame, from RadialMenu.UpdateSticky) ----------

        public static Result UpdateInput(ParkingState parking, Vector2 center, float innerR, float outerR)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Exiting may not orphan an in-flight drag or press: the release would land
                // in the restored radial and fire a wedge that was never pressed there.
                if (parking != null) parking.Dragging = null;
                _press = null;
                return Result.Exit;
            }
            if (Input.GetMouseButtonDown(1))
            {
                if (parking != null && parking.Dragging != null) { parking.Dragging = null; return Result.None; }
                _press = null;
                return Result.Exit;
            }

            // Typing. Enter takes the top visible result — processed AFTER the batch so it
            // acts on the query the user just finished typing.
            bool enter = false;
            if (Time.unscaledTime - _openedAt > 0.05f)
            {
                string typed = Input.inputString;
                for (int i = 0; i < typed.Length; i++)
                {
                    char c = typed[i];
                    if (c == '\b') { if (_query.Length > 0) _query = _query.Substring(0, _query.Length - 1); }
                    else if (c == '\r' || c == '\n') enter = true;
                    else if (!char.IsControl(c) && _query.Length < 40) _query += c;
                }
            }
            RebuildRows();

            var mouse = DrawUtil.MousePos();
            var delta = mouse - center;
            float dist = delta.magnitude;
            var visible = VisibleRows();
            _hovered = HoverIndex(delta, dist, innerR, outerR, visible.Count);

            // Wheel pages the results when there are more than fit.
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && _rows.Count > MaxResultWedges)
            {
                _scroll = Mathf.Clamp(_scroll + (wheel > 0 ? -1 : 1), 0, _rows.Count - MaxResultWedges);
                visible = VisibleRows();
                _hovered = HoverIndex(delta, dist, innerR, outerR, visible.Count);
            }

            if (enter)
            {
                var top = visible.Count > 0 ? visible[0] : null;
                if (top != null && ItemActions.TakeOrDrop(top.Source))
                {
                    _press = null; // never leave a press behind an exit
                    if (RadialMenu.KeepOpenAfterAction) { _builtFor = null; RebuildRows(); return Result.None; } // same inverted-Shift semantics as the wheel
                    return Result.Took;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (_hovered >= 0 && _hovered < visible.Count)
                {
                    _press = visible[_hovered];
                    _pressAt = Time.unscaledTime;
                    _pressPos = mouse;
                }
                return Result.None;
            }

            if (_press != null && Input.GetMouseButton(0))
            {
                bool heldLong = Time.unscaledTime - _pressAt >= DragHoldSec;
                bool movedFar = (mouse - _pressPos).magnitude >= DragMovePx;
                if ((heldLong || movedFar) && parking != null)
                {
                    parking.RemoveBySlot(_press.Source?.Slot);
                    parking.Dragging = new ParkingState.Chip
                    {
                        Source = _press.Source,
                        Icon = _press.Icon,
                        Name = _press.Name,
                    };
                    _press = null;
                    UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                }
                return Result.None;
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (parking != null && parking.Dragging != null)
                {
                    ResolveDragRelease(parking, mouse, dist, outerR);
                    return Result.None;
                }
                var pressed = _press;
                _press = null;
                if (pressed == null) return Result.None;
                var current = _hovered >= 0 && _hovered < visible.Count ? visible[_hovered] : null;
                if (!ReferenceEquals(pressed, current)) return Result.None;
                if (ItemActions.TakeOrDrop(pressed.Source))
                {
                    if (RadialMenu.KeepOpenAfterAction) { _builtFor = null; RebuildRows(); return Result.None; } // same inverted-Shift semantics as the wheel
                    return Result.Took;
                }
            }
            return Result.None;
        }

        private static void ResolveDragRelease(ParkingState parking, Vector2 mouse, float dist, float outerR)
        {
            var chip = parking.Dragging;
            parking.Dragging = null;
            if (chip?.Source?.Occupant == null) return;
            if (dist > outerR + 30f && parking.Chips.Count < ParkingState.MaxChips)
            {
                parking.RemoveBySlot(chip.Source.Slot);
                chip.Pos = mouse;
                parking.Chips.Add(chip);
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            }
            // released inside the rings: cancel — the item never moved.
        }

        private static int HoverIndex(Vector2 delta, float dist, float innerR, float outerR, int count)
        {
            if (count <= 0) return -1;
            if (dist < innerR * 0.9f || dist > outerR * 1.2f) return -1;
            float ang = Mathf.Atan2(delta.y, delta.x);   // y-down: bottom half = (0, PI)
            if (ang <= 0f || ang >= Mathf.PI) return -1;
            int idx = Mathf.FloorToInt(ang / (Mathf.PI / count));
            return Mathf.Clamp(idx, 0, count - 1);
        }

        private static List<RowData> VisibleRows()
        {
            if (_rows.Count <= MaxResultWedges) return _rows;
            int start = Mathf.Clamp(_scroll, 0, _rows.Count - MaxResultWedges);
            return _rows.GetRange(start, MaxResultWedges);
        }

        // ---------- data ----------

        private static void RebuildRows()
        {
            if (_builtFor == _query) return;
            _builtFor = _query;
            _rows.Clear();
            _scroll = 0;

            // D-005 (A3): never list an item in (or beneath) a SEALED slot (ScannedSlot.Sealed) — a
            // search "take" would rip a built-in part out of an emergency suit/tool for good.
            var groups = InventoryScanner
                .Scan(UIAConfig.ScanDepth.Value, UIAConfig.AllowToolSlotSources.Value)
                .Where(s => s.Occupant != null && s.Depth > 0 && !s.Sealed)
                .Where(s => _query.Length == 0
                    || s.Occupant.DisplayName.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(s => s.Occupant.PrefabHash)
                .OrderBy(g => g.First().Occupant.DisplayName);

            foreach (var g in groups)
            {
                var first = g.OrderBy(s => s.Depth).First().Pin();
                var thing = first.Occupant;
                Sprite icon = null;
                try { icon = thing.GetThumbnail(); } catch { }
                _rows.Add(new RowData
                {
                    Source = first,
                    Name = thing.DisplayName,
                    Location = first.Location,
                    Count = g.Count(),
                    Icon = icon,
                    State = StateText.For(thing),
                });
            }
        }

        // ---------- rendering ----------

        public static void Render(Vector2 center, float innerR, float outerR)
        {
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _root.anchoredPosition = UnityRadialView.CanvasAnchoredPos(center);

            float gapRad = UIAConfig.RadialWedgeGapDeg != null
                ? UIAConfig.RadialWedgeGapDeg.Value * Mathf.Deg2Rad : 0f;
            bool sideBorders = UIAConfig.RadialSideBorders == null || UIAConfig.RadialSideBorders.Value;
            float bw = UIAConfig.RadialBorderWidth.Value;
            float midR = (innerR + outerR) * 0.5f;
            float ringWidth = outerR - innerR;

            // --- the fixed top wedge: SEARCH + the box ---
            float swI = UIAConfig.RadialSideWidthInner != null ? UIAConfig.RadialSideWidthInner.Value : 3.2f;
            float swO = UIAConfig.RadialSideWidthOuter != null ? UIAConfig.RadialSideWidthOuter.Value : 3.2f;
            _topWedge.SetGeometry(innerR, outerR, Mathf.PI + gapRad * 0.5f, Mathf.PI * 2f - gapRad * 0.5f, false);
            _topWedge.SideBorders = sideBorders;
            _topWedge.FeatherSides = gapRad > 0.0005f;
            _topWedge.SideFeatherLimit = gapRad * 0.5f * midR;
            _topWedge.SideWidthInner = swI;
            _topWedge.SideWidthOuter = swO;
            _topWedge.BorderWidth = bw;
            _topWedge.BorderColor = RadialPalette.WedgeBorder.Value;
            _topWedge.color = RadialPalette.WedgeBg.Value;
            _topWedge.RefreshGeometry();

            UnityRadialView.SyncFont(_title);
            _title.fontStyle = UnityRadialView.WedgeFontStyle();
            _title.text = UnityRadialView.WedgeText("Search");
            _title.color = RadialPalette.TextPrimary.Value;
            _title.rectTransform.sizeDelta = new Vector2(ringWidth * 1.6f, 22f);
            _title.rectTransform.anchoredPosition = new Vector2(0f, midR + ringWidth * 0.14f);

            var boxCol = Color.Lerp(Color.black, RadialPalette.WedgeBg.Value, 0.35f);
            boxCol.a = 0.92f;
            _inputBg.color = boxCol;
            _inputBg.rectTransform.sizeDelta = new Vector2(Mathf.Max(150f, ringWidth * 1.5f), 30f);
            _inputBg.rectTransform.anchoredPosition = new Vector2(0f, midR - ringWidth * 0.16f);

            bool caretOn = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
            UnityRadialView.SyncFont(_queryText);
            _queryText.text = _query + (caretOn ? "_" : " ");
            _queryText.color = RadialPalette.TextPrimary.Value;

            // --- hub readout ---
            _hub.SetRadius(innerR - 6f);
            _hub.color = RadialPalette.HubFill.Value;
            _hub.BorderColor = RadialPalette.HubBorder.Value;
            _hub.BorderWidth = UIAConfig.RadialBorderWidth.Value * 0.8f;
            _hub.Refresh();

            var visible = VisibleRows();
            UnityRadialView.SyncFont(_hubLine1);
            UnityRadialView.SyncFont(_hubLine2);
            var hoveredRow = _hovered >= 0 && _hovered < visible.Count ? visible[_hovered] : null;
            _hubLine1.text = hoveredRow != null ? hoveredRow.Name
                : _rows.Count == 0 ? (_query.Length == 0 ? "type to search" : "no matches")
                : _rows.Count + " match(es)";
            _hubLine1.color = RadialPalette.TextPrimary.Value;
            _hubLine2.text = hoveredRow != null
                ? hoveredRow.Location + (hoveredRow.Count > 1 ? "  x" + hoveredRow.Count : "")
                : _rows.Count > MaxResultWedges ? "wheel = more results" : "";
            _hubLine2.color = RadialPalette.TextDim.Value;
            _hubLine1.rectTransform.sizeDelta = new Vector2((innerR - 8f) * 1.7f, 20f);
            _hubLine2.rectTransform.sizeDelta = new Vector2((innerR - 8f) * 1.7f, 18f);
            _hubLine1.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            _hubLine2.rectTransform.anchoredPosition = new Vector2(0f, -14f);

            // --- result wedges across the bottom half ---
            int count = visible.Count;
            while (_pool.Count < MaxResultWedges) _pool.Add(MakeWedge("Result" + _pool.Count));
            float sector = count > 0 ? Mathf.PI / count : 0f;
            float iconRatio = UIAConfig.RadialIconRatio != null ? UIAConfig.RadialIconRatio.Value : 0.62f;

            for (int i = 0; i < _pool.Count; i++)
            {
                bool used = i < count;
                var v = _pool[i];
                v.Wedge.gameObject.SetActive(used);
                v.Icon.gameObject.SetActive(used);
                v.Name.gameObject.SetActive(used);
                v.State.gameObject.SetActive(used);
                if (!used) continue;

                var row = visible[i];
                bool isHovered = i == _hovered;
                float a0 = sector * i + (i == 0 ? gapRad * 0.5f : gapRad * 0.5f);
                float a1 = sector * (i + 1) - gapRad * 0.5f;
                v.Wedge.SetGeometry(innerR, outerR, a0, a1, false);
                v.Wedge.SideBorders = sideBorders;
                v.Wedge.FeatherSides = gapRad > 0.0005f;
                v.Wedge.SideFeatherLimit = gapRad * 0.5f * midR;
                v.Wedge.SideWidthInner = swI;
                v.Wedge.SideWidthOuter = swO;
                v.Wedge.BorderWidth = isHovered ? bw * 1.2f : bw;
                v.Wedge.BorderColor = isHovered
                    ? RadialPalette.WedgeBorderHover.Value : RadialPalette.WedgeBorder.Value;
                v.Wedge.color = isHovered ? RadialPalette.WedgeHover.Value : RadialPalette.WedgeBg.Value;
                v.Wedge.RefreshGeometry();

                float aMid = (a0 + a1) * 0.5f;
                var dir = new Vector2(Mathf.Cos(aMid), -Mathf.Sin(aMid));
                var slot = dir * midR;
                float halfAngle = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                float chord = 2f * midR * Mathf.Sin(halfAngle);
                float iconSize = Mathf.Min(ringWidth, chord) * iconRatio;

                v.Icon.sprite = row.Icon;
                v.Icon.enabled = row.Icon != null;
                v.Icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                v.Icon.rectTransform.anchoredPosition = slot + new Vector2(0f, ringWidth * 0.08f);

                UnityRadialView.SyncFont(v.Name);
                v.Name.fontStyle = UnityRadialView.WedgeFontStyle();
                v.Name.text = UnityRadialView.WedgeText(row.Name) + (row.Count > 1 ? " x" + row.Count : "");
                v.Name.color = RadialPalette.TextPrimary.Value;
                v.Name.rectTransform.sizeDelta = new Vector2(Mathf.Max(60f, chord - 8f), 30f);
                v.Name.rectTransform.anchoredPosition = slot - new Vector2(0f, iconSize * 0.5f + 10f);

                UnityRadialView.SyncFont(v.State);
                v.State.text = row.State ?? "";
                v.State.color = RadialPalette.TextDim.Value;
                v.State.rectTransform.sizeDelta = new Vector2(Mathf.Max(60f, iconSize * 1.6f), 15f);
                v.State.rectTransform.anchoredPosition = slot + new Vector2(0f, iconSize * 0.62f + 6f);
            }
        }

        public static void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            // The rows pin Slot/DynamicThing graphs (via pinned ScannedSlots) — never hold
            // them past the panel's life, across world unloads, or across hot reloads.
            _rows.Clear();
            _builtFor = null;
            _press = null;
        }

        public static void Shutdown()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _root = null;
            _topWedge = null;
            _title = null;
            _inputBg = null;
            _queryText = null;
            _hub = null;
            _hubLine1 = null;
            _hubLine2 = null;
            _pool.Clear();
            _rows.Clear();
            _builtFor = null;
            _press = null;
        }

        // ---------- construction ----------

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("UIAscended_SearchCanvas");
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5005;
            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var rootGo = new GameObject("SearchRadial", typeof(RectTransform));
            rootGo.transform.SetParent(go.transform, false);
            _root = (RectTransform)rootGo.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = Vector2.zero;

            var twGo = new GameObject("TopWedge", typeof(RectTransform));
            twGo.transform.SetParent(_root, false);
            _topWedge = twGo.AddComponent<RadialWedgeGraphic>();
            _topWedge.raycastTarget = false;

            _title = MakeText(_root, "Title", 20f);
            var ibGo = new GameObject("InputBg", typeof(RectTransform));
            ibGo.transform.SetParent(_root, false);
            _inputBg = ibGo.AddComponent<Image>();
            _inputBg.raycastTarget = false;
            var ibRt = _inputBg.rectTransform;
            ibRt.anchorMin = ibRt.anchorMax = new Vector2(0.5f, 0.5f);

            _queryText = MakeText(ibRt, "Query", 16f);
            _queryText.rectTransform.sizeDelta = new Vector2(240f, 26f);

            var hubGo = new GameObject("Hub", typeof(RectTransform));
            hubGo.transform.SetParent(_root, false);
            _hub = hubGo.AddComponent<CircleGraphic>();
            _hub.raycastTarget = false;
            _hubLine1 = MakeText(_root, "HubLine1", 15f);
            _hubLine2 = MakeText(_root, "HubLine2", 12f);
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = UnityRadialView.Font();
            tmp.fontSize = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8f;
            tmp.fontSizeMax = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            return tmp;
        }

        private static WedgeVisual MakeWedge(string name)
        {
            var wGo = new GameObject(name, typeof(RectTransform));
            wGo.transform.SetParent(_root, false);
            var wedge = wGo.AddComponent<RadialWedgeGraphic>();
            wedge.raycastTarget = false;

            var iGo = new GameObject("Icon", typeof(RectTransform));
            iGo.transform.SetParent(_root, false);
            var icon = iGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var iRt = icon.rectTransform;
            iRt.anchorMin = iRt.anchorMax = new Vector2(0.5f, 0.5f);

            var nameTmp = MakeText(_root, name + "Name", 13f);
            var stateTmp = MakeText(_root, name + "State", 11f);
            stateTmp.enableWordWrapping = false;
            stateTmp.overflowMode = TextOverflowModes.Overflow;

            return new WedgeVisual { Wedge = wedge, Icon = icon, Name = nameTmp, State = stateTmp };
        }
    }
}
