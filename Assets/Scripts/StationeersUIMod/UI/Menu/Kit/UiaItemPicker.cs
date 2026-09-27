using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>A searchable item picker modelled on the game's own creative-spawn menu: a search
    /// box over every carriable prefab (<c>DynamicThing.DynamicThingPrefabs</c>), icons via
    /// <c>prefab.GetThumbnail()</c>. Clicking an entry calls back with the item's PrefabName
    /// (what bag-profile item rules match on). Multi-add: stays open until Done. Mounts in the
    /// Control Center's popup layer so it floats over the window.
    ///
    /// Design O6 upgrades: SortingClass filter CHIPS + a Slot.Class dropdown filter (both compose
    /// with the search text — the exact fields BagProfile.Match reads), and a List/Icons view
    /// toggle. LIST view (compact rows) is the default; the icon grid is one click away. Thumbnails
    /// come from a per-session cache (prefabHash -&gt; Sprite) so keystroke rebuilds never re-render
    /// them; nothing fetches per frame, and the cache clears on Close/Reset so no sprite outlives
    /// a world.</summary>
    public static class UiaItemPicker
    {
        // Result caps. Rebuild() runs per keystroke / filter click (never per frame); list rows
        // are cheaper than grid cells, and an active filter earns a deeper "browse" cap because
        // it is how "add all the things I mean" gets scanned.
        private const int GridMax = 60;
        private const int ListMax = 100;
        private const int FilteredMax = 150;

        private static GameObject _overlay;
        private static Action<string> _onPick;
        private static Action _closed;
        private static RectTransform _grid;
        private static TextMeshProUGUI _status;
        private static string _query = "";
        private static string _pickVerb = "Added";

        // ---- filters + view state (statics: reset in Reset(), filters re-cleared per Open) ----
        private static int _catSel;                  // 0 = all, else 1 + index into _catValues
        private static SortingClass[] _catValues;
        private static readonly List<UiaControls.UiaButton> _catChips = new List<UiaControls.UiaButton>();
        private static int _slotSel;                 // 0 = any, else 1 + index into _slotValues
        private static Slot.Class[] _slotValues;
        private static bool _gridView;               // false = LIST view, the default
        private static UiaControls.UiaButton _listBtn, _gridBtn;

        // Thumbnail cache: prefabHash -> sprite, for one picker session. Only NON-NULL results
        // are cached — a thumbnail the game has not generated yet may exist on a later rebuild.
        private static readonly Dictionary<int, Sprite> _thumbs = new Dictionary<int, Sprite>();

        public static bool IsOpen => _overlay != null;

        /// <summary>Open the picker. <paramref name="title"/> / <paramref name="pickVerb"/> let a
        /// caller re-skin the surface ("Test an item" / "Testing") without a second code path;
        /// defaults keep the classic "Add an item" / "Added" behaviour for existing callers.</summary>
        public static void Open(Action<string> onPick, Action onClosed, string title = null, string pickVerb = null)
        {
            Close();
            _onPick = onPick;
            _closed = onClosed;
            _pickVerb = string.IsNullOrEmpty(pickVerb) ? "Added" : pickVerb;
            var layer = UiaControls.PopupLayer;
            if (layer == null) return;

            // Filters reset per open (a stale category filter looks like missing items); the
            // list/grid view choice persists for the session — it is a preference, not a filter.
            _query = "";
            _catSel = 0;
            _slotSel = 0;

            _overlay = UiaUi.Go("item-picker", layer);
            var dim = _overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            UiaUi.Fill((RectTransform)_overlay.transform);
            _overlay.transform.SetAsLastSibling();
            _overlay.AddComponent<UiaControls.UiaButton>().Init(dim, dim.color, dim.color, dim.color).OnClick = Close;

            var panelGo = UiaUi.Go("panel", _overlay.transform);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 620f);
            var pimg = panelGo.AddComponent<Image>(); pimg.color = UiaTheme.Window;
            UiaImages.Round(pimg);
            UiaUi.OutlineOf(pimg, UiaTheme.Accent, 1.5f);
            UiaUi.VLayout(panel, UiaTheme.Gap, (int)UiaTheme.Pad, (int)UiaTheme.Pad, (int)UiaTheme.Pad, (int)UiaTheme.Pad);
            // Eat clicks so the dim-backdrop close doesn't fire when clicking inside the panel.
            pimg.raycastTarget = true;

            var header = UiaUi.Go("h", panel);
            UiaUi.Size(header, 26f);
            UiaUi.HLayout((RectTransform)header.transform, UiaTheme.Gap);
            var titleGo = UiaUi.Go("t", header.transform);
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.font = UiaTheme.Font(); titleText.fontSize = UiaTheme.TitleSize; titleText.color = UiaTheme.Text;
            titleText.text = string.IsNullOrEmpty(title) ? "Add an item" : title;
            titleText.raycastTarget = false; titleText.alignment = TextAlignmentOptions.Left;
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // View toggle: two segmented buttons (the tab-bar pattern), List selected by default.
            _listBtn = UiaControls.Button(header.transform, "List", () => SetView(false), 64f, 26f);
            _gridBtn = UiaControls.Button(header.transform, "Icons", () => SetView(true), 64f, 26f);
            UpdateViewButtons();
            UiaControls.Button(header.transform, "Done", Close, 90f, 26f, UiaControls.ButtonStyle.Primary);

            var searchGo = UiaUi.Go("search", panel);
            UiaUi.Size(searchGo, 30f);
            UiaUi.InputField(searchGo.transform, "Search items by name...", q => { _query = q; Rebuild(); });

            // Category chips: "All" + every SortingClass, wrapping across as many rows as it takes.
            // Height and row count are DERIVED from the live enum (today: 12 chips = 2 rows = 44px,
            // same as the old hardcoded constant) so a future SortingClass addition grows the strip
            // instead of silently overflowing into the "Slot class" dropdown below it; the mask is
            // a defensive backstop for the same reason (GridLayoutGroup itself never clips).
            var chipsGo = UiaUi.Go("cats", panel);
            chipsGo.AddComponent<RectMask2D>();
            _catValues = (SortingClass[])Enum.GetValues(typeof(SortingClass));
            var catNames = Enum.GetNames(typeof(SortingClass));
            const int ChipCols = 8;
            const float ChipCellH = 20f, ChipSpacing = 4f;
            int chipCount = catNames.Length + 1; // + "All"
            int chipRows = (chipCount + ChipCols - 1) / ChipCols;
            float chipsH = chipRows * (ChipCellH + ChipSpacing) - ChipSpacing;
            UiaUi.Size(chipsGo, chipsH);
            var cgrid = chipsGo.AddComponent<GridLayoutGroup>();
            cgrid.cellSize = new Vector2(87f, ChipCellH);
            cgrid.spacing = new Vector2(ChipSpacing, ChipSpacing);
            cgrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cgrid.constraintCount = ChipCols;
            _catChips.Clear();
            CatChip(chipsGo.transform, "All", 0);
            for (int i = 0; i < catNames.Length; i++)
                CatChip(chipsGo.transform, catNames[i], i + 1);

            // Slot-class filter: too many values for chips — a dropdown row matches the kit.
            _slotValues = (Slot.Class[])Enum.GetValues(typeof(Slot.Class));
            var slotOptions = new List<string> { "Any slot class" };
            slotOptions.AddRange(Enum.GetNames(typeof(Slot.Class)));
            UiaControls.DropdownRow(panel, "Slot class", slotOptions, 0, i => { _slotSel = i; Rebuild(); });

            var statusGo = UiaUi.Go("status", panel);
            UiaUi.Size(statusGo, 18f);
            _status = statusGo.AddComponent<TextMeshProUGUI>();
            _status.font = UiaTheme.Font(); _status.fontSize = UiaTheme.SmallSize; _status.color = UiaTheme.TextMute;
            _status.raycastTarget = false; _status.text = "Type to search, or pick a filter.";

            var scrollHost = UiaUi.Go("scrollhost", panel);
            UiaUi.Size(scrollHost, flexH: 1f);
            ScrollRect scroll;
            var content = UiaUi.ScrollView(scrollHost.transform, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);
            _grid = content;
            ApplyViewLayout();

            Rebuild();
        }

        // ------------------------------------------------------------------ view + filters ----

        private static void SetView(bool grid)
        {
            if (_gridView == grid) return;
            _gridView = grid;
            UpdateViewButtons();
            ApplyViewLayout();
            Rebuild();
        }

        private static void UpdateViewButtons()
        {
            if (_listBtn != null) _listBtn.SetSelected(!_gridView);
            if (_gridBtn != null) _gridBtn.SetSelected(_gridView);
        }

        /// <summary>Swap the content's layout group for the active view (immediate destroy: the
        /// replacement is added in the same frame). ScrollView ships a VLayout by default.</summary>
        private static void ApplyViewLayout()
        {
            if (_grid == null) return;
            var v = _grid.GetComponent<VerticalLayoutGroup>();
            if (v != null) UnityEngine.Object.DestroyImmediate(v);
            var g = _grid.GetComponent<GridLayoutGroup>();
            if (g != null) UnityEngine.Object.DestroyImmediate(g);
            if (_gridView)
            {
                var grid = _grid.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(112f, 112f);
                grid.spacing = new Vector2(UiaTheme.Gap, UiaTheme.Gap);
            }
            else
            {
                UiaUi.VLayout(_grid, 2f);
            }
        }

        private static void CatChip(Transform parent, string label, int chipIndex)
        {
            var go = UiaUi.Go("chip", parent);
            var bg = go.AddComponent<Image>(); bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            var t = UiaUi.Text(go.transform, label, 10f, UiaTheme.Text, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)t.transform);
            // No ellipsis: an 87x20 chip holds a long SortingClass name by shrinking toward 8pt
            // (and, at that size, two lines fit the 20px cell).
            UiaControls.FitText(t, 8f);
            var btn = go.AddComponent<UiaControls.UiaButton>()
                .Init(bg, UiaTheme.PanelRaised, UiaTheme.PanelHover, UiaTheme.SelectedDim);
            btn.OnClick = () => SelectCat(chipIndex);
            btn.SetSelected(_catSel == chipIndex);
            _catChips.Add(btn);
        }

        private static void SelectCat(int chipIndex)
        {
            _catSel = chipIndex;
            for (int i = 0; i < _catChips.Count; i++)
                if (_catChips[i] != null) _catChips[i].SetSelected(i == chipIndex);
            Rebuild();
        }

        // -------------------------------------------------------------------------- results ----

        private static void Rebuild()
        {
            if (_grid == null) return;
            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var c = _grid.GetChild(i);
                c.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(c.gameObject);
            }

            var list = DynamicThing.DynamicThingPrefabs;
            if (list == null) { if (_status != null) _status.text = "No item list available (load a world first)."; return; }

            // OrdinalIgnoreCase IndexOf is allocation-free; the old per-candidate
            // ToLowerInvariant pair allocated two strings per item per keystroke over the
            // whole prefab list (the honest-match-counter walk never early-exits).
            string q = _query != null ? _query.Trim() : "";
            bool filtered = _catSel > 0 || _slotSel > 0;
            int cap = filtered ? FilteredMax : (_gridView ? GridMax : ListMax);
            int shown = 0, matched = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) continue;
                string prefab = it.PrefabName;
                if (string.IsNullOrEmpty(prefab)) continue;
                if (_catSel > 0 && it.SortingClass != _catValues[_catSel - 1]) continue;
                if (_slotSel > 0 && it.SlotType != _slotValues[_slotSel - 1]) continue;
                string disp = SafeDisplayName(it);
                if (q.Length > 0 &&
                    (prefab == null || prefab.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) &&
                    (disp == null || disp.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                matched++;
                if (shown >= cap) continue; // keep counting for an honest status line
                if (_gridView) GridCell(it, prefab, disp);
                else ListRow(it, prefab, disp);
                shown++;
            }
            if (_status != null)
                _status.text = (q.Length == 0 && !filtered)
                    ? ("Showing " + shown + " of " + list.Count + " items - type to narrow, or pick a filter.")
                    : (matched + " match" + (matched == 1 ? "" : "es") + (matched > shown ? " (showing " + shown + ")" : ""));
        }

        private static string SafeDisplayName(DynamicThing it)
        {
            try { return it.DisplayName; } catch { return it.PrefabName; }
        }

        /// <summary>Session thumbnail cache; only successful fetches are stored, so a not-yet-
        /// generated thumbnail retries on the next rebuild instead of caching as forever-blank.</summary>
        private static Sprite Thumb(DynamicThing it)
        {
            int hash = 0;
            try { hash = it.PrefabHash; } catch { }
            Sprite sp;
            if (hash != 0 && _thumbs.TryGetValue(hash, out sp)) return sp;
            sp = null;
            try { sp = it.GetThumbnail(); } catch { }
            if (sp != null && hash != 0) _thumbs[hash] = sp;
            return sp;
        }

        private static void GridCell(DynamicThing it, string prefab, string disp)
        {
            var cell = UiaUi.Go("cell", _grid);
            var bg = cell.AddComponent<Image>(); bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.VLayout((RectTransform)cell.transform, 2f, 4, 4, 4, 4, TextAnchor.UpperCenter);

            var iconGo = UiaUi.Go("icon", cell.transform);
            UiaUi.Size(iconGo, 64f);
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            var sp = Thumb(it);
            if (sp != null) { icon.sprite = sp; icon.color = Color.white; }
            else icon.color = new Color(0.12f, 0.16f, 0.20f, 1f);

            var nameGo = UiaUi.Go("n", cell.transform);
            UiaUi.Size(nameGo, 30f);
            var name = nameGo.AddComponent<TextMeshProUGUI>();
            name.font = UiaTheme.Font(); name.fontSize = 11f; name.color = UiaTheme.TextDim;
            name.alignment = TextAlignmentOptions.Top; name.raycastTarget = false;
            name.text = disp ?? prefab;
            // No ellipsis: two lines at 11pt, shrinking toward 8pt (three lines) for the
            // longest item names; past that it spills below the 30px slot, never hides.
            UiaControls.FitText(name, 8f);

            HookPick(cell, bg, prefab, disp);
        }

        private static void ListRow(DynamicThing it, string prefab, string disp)
        {
            var row = UiaUi.Go("row", _grid);
            UiaUi.Size(row, 30f);
            var bg = row.AddComponent<Image>(); bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.HLayout((RectTransform)row.transform, 8f, 6, 6, 0, 0, TextAnchor.MiddleLeft);

            var iconGo = UiaUi.Go("icon", row.transform);
            UiaUi.Size(iconGo, 24f, 24f, flexW: 0f);
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            var sp = Thumb(it);
            if (sp != null) { icon.sprite = sp; icon.color = Color.white; }
            else icon.color = new Color(0.12f, 0.16f, 0.20f, 1f);

            var name = UiaUi.Text(row.transform, disp ?? prefab, UiaTheme.SmallSize, UiaTheme.Text, TextAlignmentOptions.Left);
            // No ellipsis in the fixed 30px list row: shrink toward 9pt, wrapping to a second
            // line inside the row when a name is still too wide.
            UiaControls.FitText(name, 9f);
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // The item's category on the right: cheap orientation while filtering/browsing.
            string cat = "";
            try { cat = it.SortingClass.ToString(); } catch { }
            var catText = UiaUi.Text(row.transform, cat, 11f, UiaTheme.TextMute, TextAlignmentOptions.Right);
            UiaUi.Size(catText.gameObject, 24f, 90f, flexW: 0f);
            UiaControls.FitText(catText, 8f);   // no ellipsis: shrink/wrap in its 90x24 slot

            HookPick(row, bg, prefab, disp);
        }

        private static void HookPick(GameObject go, Image bg, string prefab, string disp)
        {
            // The pick confirmation must survive the pointer leaving the row on the very next
            // frame (the core workflow is "click several items in a row"). Writing bg.color
            // directly gets clobbered by UiaButton.OnPointerExit's Repaint(false), which recomputes
            // from _selectedState - so route the highlight through SetSelected instead, which is
            // what Repaint actually reads.
            var btn = go.AddComponent<UiaControls.UiaButton>().Init(bg, bg.color, UiaTheme.PanelHover, UiaTheme.SelectedDim);
            btn.OnClick = () =>
                {
                    if (_onPick != null) _onPick(prefab);
                    if (_status != null) _status.text = _pickVerb + ": " + (disp ?? prefab);
                    btn.SetSelected(true);
                };
        }

        // ------------------------------------------------------------------------- teardown ----

        public static void Close()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            _grid = null;
            _status = null;
            _onPick = null;
            _catChips.Clear();
            _listBtn = null;
            _gridBtn = null;
            _thumbs.Clear(); // sprites never outlive a picker session (nor a world)
            var c = _closed; _closed = null;
            if (c != null) c();
        }

        public static void Reset()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null; _grid = null; _status = null; _onPick = null; _closed = null; _query = "";
            _catSel = 0; _slotSel = 0; _catValues = null; _slotValues = null;
            _catChips.Clear(); _listBtn = null; _gridBtn = null;
            _gridView = false; _pickVerb = "Added";
            _thumbs.Clear();
        }
    }
}
