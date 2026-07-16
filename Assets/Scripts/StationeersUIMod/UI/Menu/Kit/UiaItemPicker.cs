using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>A searchable item picker modelled on the game's own creative-spawn menu: a search
    /// box over a thumbnail grid of every carriable prefab (<c>DynamicThing.DynamicThingPrefabs</c>),
    /// icons via <c>prefab.GetThumbnail()</c>. Clicking a tile calls back with the item's PrefabName
    /// (what bag-profile item rules match on). Multi-add: stays open until Done. Mounts in the
    /// Control Center's popup layer so it floats over the window.</summary>
    public static class UiaItemPicker
    {
        private const int MaxResults = 60;

        private static GameObject _overlay;
        private static Action<string> _onPick;
        private static RectTransform _grid;
        private static TextMeshProUGUI _status;
        private static string _query = "";

        public static bool IsOpen => _overlay != null;

        public static void Open(Action<string> onPick, Action onClosed)
        {
            Close();
            _onPick = onPick;
            _closed = onClosed;
            var layer = UiaControls.PopupLayer;
            if (layer == null) return;

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
            panel.sizeDelta = new Vector2(760f, 560f);
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
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.font = UiaTheme.Font(); title.fontSize = UiaTheme.TitleSize; title.color = UiaTheme.Text;
            title.text = "Add an item"; title.raycastTarget = false; title.alignment = TextAlignmentOptions.Left;
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UiaControls.Button(header.transform, "Done", Close, 90f, 26f, UiaControls.ButtonStyle.Primary);

            var searchGo = UiaUi.Go("search", panel);
            UiaUi.Size(searchGo, 30f);
            UiaUi.InputField(searchGo.transform, "Search items by name...", q => { _query = q; Rebuild(); });

            var statusGo = UiaUi.Go("status", panel);
            UiaUi.Size(statusGo, 18f);
            _status = statusGo.AddComponent<TextMeshProUGUI>();
            _status.font = UiaTheme.Font(); _status.fontSize = UiaTheme.SmallSize; _status.color = UiaTheme.TextMute;
            _status.raycastTarget = false; _status.text = "Type to search.";

            var scrollHost = UiaUi.Go("scrollhost", panel);
            UiaUi.Size(scrollHost, flexH: 1f);
            ScrollRect scroll;
            var content = UiaUi.ScrollView(scrollHost.transform, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);
            _grid = content;
            var g = content.gameObject.GetComponent<VerticalLayoutGroup>();
            if (g != null) UnityEngine.Object.DestroyImmediate(g); // swap the VLayout for a grid (immediate: same-frame add below)
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(112f, 112f);
            grid.spacing = new Vector2(UiaTheme.Gap, UiaTheme.Gap);

            _query = "";
            Rebuild();
        }

        private static Action _closed;

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

            string q = _query != null ? _query.Trim().ToLowerInvariant() : "";
            int shown = 0, matched = 0;
            for (int i = 0; i < list.Count && shown < MaxResults; i++)
            {
                var it = list[i];
                if (it == null) continue;
                string prefab = it.PrefabName;
                if (string.IsNullOrEmpty(prefab)) continue;
                string disp = SafeDisplayName(it);
                if (q.Length > 0 &&
                    (prefab == null || prefab.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) &&
                    (disp == null || disp.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0))
                    continue;
                matched++;
                if (q.Length == 0 && shown >= MaxResults) continue;
                Cell(it, prefab, disp);
                shown++;
            }
            if (_status != null)
                _status.text = q.Length == 0
                    ? ("Showing " + shown + " of " + list.Count + " items - type to narrow.")
                    : (matched + " match" + (matched == 1 ? "" : "es") + (matched > shown ? " (showing " + shown + ")" : ""));
        }

        private static string SafeDisplayName(DynamicThing it)
        {
            try { return it.DisplayName; } catch { return it.PrefabName; }
        }

        private static void Cell(DynamicThing it, string prefab, string disp)
        {
            var cell = UiaUi.Go("cell", _grid);
            var bg = cell.AddComponent<Image>(); bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.VLayout((RectTransform)cell.transform, 2f, 4, 4, 4, 4, TextAnchor.UpperCenter);

            var iconGo = UiaUi.Go("icon", cell.transform);
            UiaUi.Size(iconGo, 64f);
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            Sprite sp = null;
            try { sp = it.GetThumbnail(); } catch { }
            if (sp != null) { icon.sprite = sp; icon.color = Color.white; }
            else icon.color = new Color(0.12f, 0.16f, 0.20f, 1f);

            var nameGo = UiaUi.Go("n", cell.transform);
            UiaUi.Size(nameGo, 30f);
            var name = nameGo.AddComponent<TextMeshProUGUI>();
            name.font = UiaTheme.Font(); name.fontSize = 11f; name.color = UiaTheme.TextDim;
            name.alignment = TextAlignmentOptions.Top; name.raycastTarget = false;
            name.enableWordWrapping = true; name.overflowMode = TextOverflowModes.Ellipsis;
            name.text = disp ?? prefab;

            cell.AddComponent<UiaControls.UiaButton>().Init(bg, bg.color, UiaTheme.PanelHover, UiaTheme.SelectedDim)
                .OnClick = () =>
                {
                    if (_onPick != null) _onPick(prefab);
                    if (_status != null) _status.text = "Added: " + (disp ?? prefab);
                    bg.color = UiaTheme.SelectedDim;
                };
        }

        public static void Close()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            _grid = null;
            _status = null;
            _onPick = null;
            var c = _closed; _closed = null;
            if (c != null) c();
        }

        public static void Reset()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null; _grid = null; _status = null; _onPick = null; _closed = null; _query = "";
        }
    }
}
