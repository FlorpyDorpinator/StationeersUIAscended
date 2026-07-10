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
    /// Option A "search all bags": the radial gives way to a Unity panel — search box on
    /// top, live-filtered results below. Clicking a result takes the item to a free hand,
    /// or drops it at your feet when both hands are full.
    ///
    /// Typing is captured straight from Input.inputString rather than a TMP_InputField:
    /// our modal already holds the game's keys in the Typing state, and raw capture cannot
    /// lose focus to the EventSystem while UGUI clicks are blocked.
    /// </summary>
    public static class SearchPanelView
    {
        public enum Result { None, Exit, Took }

        private const int MaxRows = 9;
        private const float PanelW = 560f;
        private const float PanelH = 620f;
        private const float RowH = 46f;

        private sealed class RowData
        {
            public ScannedSlot Source;   // pinned
            public string Name;
            public string Location;
            public int Count;
            public Sprite Icon;
        }

        private static string _query = "";
        private static string _builtFor;
        private static readonly List<RowData> _rows = new List<RowData>();
        private static float _openedAt;

        // visuals
        private static Canvas _canvas;
        private static Image _panel;
        private static TextMeshProUGUI _title;
        private static Image _inputBg;
        private static TextMeshProUGUI _queryText;
        private static TextMeshProUGUI _hint;
        private static readonly List<RowVisual> _rowPool = new List<RowVisual>();

        private sealed class RowVisual
        {
            public RectTransform Root;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Sub;
        }

        public static void Begin()
        {
            _query = "";
            _builtFor = null;
            _openedAt = Time.unscaledTime;
            RebuildRows();
        }

        /// <summary>Per-frame input while the panel owns the screen. Runs OUTSIDE the ImGui
        /// frame (called from RadialMenu.UpdateSticky).</summary>
        public static Result UpdateInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                return Result.Exit;

            // Raw typed characters. Ignore the very first frame so the click/keypress that
            // opened the panel can't leak a character in. Enter is handled AFTER the whole
            // batch is applied and the rows rebuilt — characters and '\r' can arrive in the
            // same Input.inputString, and Enter must act on the query the user just typed.
            bool enter = false;
            if (Time.unscaledTime - _openedAt > 0.05f)
            {
                string typed = Input.inputString;
                for (int i = 0; i < typed.Length; i++)
                {
                    char c = typed[i];
                    if (c == '\b')
                    {
                        if (_query.Length > 0) _query = _query.Substring(0, _query.Length - 1);
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        enter = true;
                    }
                    else if (!char.IsControl(c) && _query.Length < 40)
                    {
                        _query += c;
                    }
                }
            }
            RebuildRows();
            if (enter && _rows.Count > 0 && ItemActions.TakeOrDrop(_rows[0].Source))
                return Result.Took;

            if (Input.GetMouseButtonDown(0))
            {
                int hit = RowAt(DrawUtil.MousePos());
                if (hit >= 0 && hit < _rows.Count)
                {
                    if (ItemActions.TakeOrDrop(_rows[hit].Source))
                        return Result.Took;
                }
            }
            return Result.None;
        }

        public static void Render()
        {
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);

            var mouse = DrawUtil.MousePos();
            int hovered = RowAt(mouse);

            UnityRadialView.SyncFont(_title);
            UnityRadialView.SyncFont(_queryText);
            UnityRadialView.SyncFont(_hint);

            var navy = RadialPalette.WedgeBg.Value;
            navy.a = Mathf.Max(navy.a, 0.86f); // results need a solid card to read against
            _panel.color = navy;
            _panel.rectTransform.sizeDelta = new Vector2(PanelW, PanelH);
            _panel.rectTransform.anchoredPosition = Vector2.zero;

            _title.text = UnityRadialView.WedgeText("Search all bags");
            _title.color = RadialPalette.TextPrimary.Value;
            _title.fontStyle = UnityRadialView.WedgeFontStyle();

            var inputCol = RadialPalette.HubFill.Value;
            inputCol.a = 0.9f;
            inputCol = Color.Lerp(Color.black, inputCol, 0.4f);
            _inputBg.color = inputCol;

            bool caretOn = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
            _queryText.text = _query.Length == 0 && !caretOn ? "" : _query + (caretOn ? "_" : "");
            _queryText.color = RadialPalette.TextPrimary.Value;
            _queryText.fontStyle = UnityRadialView.WedgeFontStyle();

            _hint.text = _rows.Count == 0
                ? (_query.Length == 0 ? "type to filter your bags" : "nothing matches")
                : "click an item: free hand if you have one, otherwise it drops at your feet";
            _hint.color = RadialPalette.TextDim.Value;

            while (_rowPool.Count < MaxRows) _rowPool.Add(MakeRow("Row" + _rowPool.Count));
            for (int i = 0; i < _rowPool.Count; i++)
            {
                bool used = i < _rows.Count;
                _rowPool[i].Root.gameObject.SetActive(used);
                if (!used) continue;
                var row = _rows[i];
                var v = _rowPool[i];
                v.Root.anchoredPosition = new Vector2(0f, RowLocalY(i));
                v.Root.sizeDelta = new Vector2(PanelW - 28f, RowH - 4f);

                Color bg = i == hovered ? RadialPalette.WedgeHover.Value : RadialPalette.WedgeBg.Value;
                bg.a = i == hovered ? 0.85f : 0.35f;
                v.Bg.color = bg;

                v.Icon.sprite = row.Icon;
                v.Icon.enabled = row.Icon != null;

                UnityRadialView.SyncFont(v.Name);
                UnityRadialView.SyncFont(v.Sub);
                v.Name.text = UnityRadialView.WedgeText(row.Name) + (row.Count > 1 ? "  x" + row.Count : "");
                v.Name.color = RadialPalette.TextPrimary.Value;
                v.Name.fontStyle = UnityRadialView.WedgeFontStyle();
                v.Sub.text = row.Location ?? "";
                v.Sub.color = RadialPalette.TextDim.Value;
            }
        }

        public static void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            // The rows pin Slot/DynamicThing graphs (via pinned ScannedSlots) — never hold
            // them past the panel's life, across world unloads, or across hot reloads.
            _rows.Clear();
            _builtFor = null;
        }

        public static void Shutdown()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _title = null;
            _inputBg = null;
            _queryText = null;
            _hint = null;
            _rowPool.Clear();
            _rows.Clear();
            _builtFor = null;
        }

        // ---------- data ----------

        private static void RebuildRows()
        {
            if (_builtFor == _query) return;
            _builtFor = _query;
            _rows.Clear();

            var groups = InventoryScanner
                .Scan(UIAConfig.ScanDepth.Value, UIAConfig.AllowToolSlotSources.Value)
                .Where(s => s.Occupant != null && s.Depth > 0)
                .Where(s => _query.Length == 0
                    || s.Occupant.DisplayName.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(s => s.Occupant.PrefabHash)
                .OrderBy(g => g.First().Occupant.DisplayName)
                .Take(MaxRows);

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
                });
            }
        }

        // ---------- layout (shared by hit-test and render) ----------

        /// <summary>Row center Y in canvas-local coords (y up, panel centered on screen).</summary>
        private static float RowLocalY(int i)
            => PanelH * 0.5f - 218f - RowH * 0.5f - i * RowH;

        private static int RowAt(Vector2 mouseImGui)
        {
            // ImGui screen coords -> canvas-local around screen center.
            var local = new Vector2(mouseImGui.x - Screen.width * 0.5f,
                                    Screen.height * 0.5f - mouseImGui.y);
            if (Mathf.Abs(local.x) > (PanelW - 28f) * 0.5f) return -1;
            for (int i = 0; i < _rows.Count; i++)
                if (Mathf.Abs(local.y - RowLocalY(i)) <= RowH * 0.5f)
                    return i;
            return -1;
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

            var pgo = new GameObject("Panel", typeof(RectTransform));
            pgo.transform.SetParent(go.transform, false);
            _panel = pgo.AddComponent<Image>();
            _panel.raycastTarget = false;
            Center(_panel.rectTransform);

            _title = MakeText(pgo.transform, "Title", 22f);
            _title.rectTransform.sizeDelta = new Vector2(PanelW - 40f, 34f);
            _title.rectTransform.anchoredPosition = new Vector2(0f, PanelH * 0.5f - 44f);

            var igo = new GameObject("InputBg", typeof(RectTransform));
            igo.transform.SetParent(pgo.transform, false);
            _inputBg = igo.AddComponent<Image>();
            _inputBg.raycastTarget = false;
            Center(_inputBg.rectTransform);
            _inputBg.rectTransform.sizeDelta = new Vector2(PanelW - 48f, 88f);
            _inputBg.rectTransform.anchoredPosition = new Vector2(0f, PanelH * 0.5f - 118f);

            _queryText = MakeText(igo.transform, "Query", 26f);
            _queryText.rectTransform.sizeDelta = new Vector2(PanelW - 80f, 60f);
            _queryText.rectTransform.anchoredPosition = Vector2.zero;
            _queryText.alignment = TextAlignmentOptions.Center;

            _hint = MakeText(pgo.transform, "Hint", 12f);
            _hint.rectTransform.sizeDelta = new Vector2(PanelW - 48f, 20f);
            _hint.rectTransform.anchoredPosition = new Vector2(0f, PanelH * 0.5f - 184f);
        }

        private static void Center(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = UnityRadialView.Font();
            tmp.fontSize = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 9f;
            tmp.fontSizeMax = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            Center(tmp.rectTransform);
            return tmp;
        }

        private static RowVisual MakeRow(string name)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(_panel.transform, false);
            var rt = (RectTransform)root.transform;
            Center(rt);

            var bg = root.AddComponent<Image>();
            bg.raycastTarget = false;

            var igo = new GameObject("Icon", typeof(RectTransform));
            igo.transform.SetParent(rt, false);
            var icon = igo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            Center(icon.rectTransform);
            icon.rectTransform.sizeDelta = new Vector2(34f, 34f);
            icon.rectTransform.anchoredPosition = new Vector2(-(PanelW - 28f) * 0.5f + 30f, 0f);

            var nameTmp = MakeText(rt, "Name", 15f);
            nameTmp.alignment = TextAlignmentOptions.Left;
            nameTmp.rectTransform.sizeDelta = new Vector2(PanelW - 140f, 20f);
            nameTmp.rectTransform.anchoredPosition = new Vector2(24f, 8f);

            var sub = MakeText(rt, "Sub", 11f);
            sub.alignment = TextAlignmentOptions.Left;
            sub.rectTransform.sizeDelta = new Vector2(PanelW - 140f, 16f);
            sub.rectTransform.anchoredPosition = new Vector2(24f, -11f);

            return new RowVisual { Root = rt, Bg = bg, Icon = icon, Name = nameTmp, Sub = sub };
        }
    }
}
