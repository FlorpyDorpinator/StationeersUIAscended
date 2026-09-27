using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// A recycled-row vertical list for the Control Center (F10 plan pain 14): fixed row height,
    /// a bind callback, and only ~(viewport/rowH)+2 live rows however long the data is — the
    /// shipped "By Printer" layout alone carries 559 item rules, and one-GameObject-per-rule
    /// rebuilt on every gesture is exactly the cost this removes.
    ///
    /// <para>Owns its own ScrollRect (virtualisation needs manual row placement, so it cannot sit
    /// INSIDE a layout-grouped <see cref="UiaUi.ScrollView"/> content the normal way — give it a
    /// height instead: it is a normal layout child that flexes to fill by default, or takes a
    /// fixed height via <paramref name="viewHeight"/> when its host scrolls separately). The kit
    /// scroll indicator (<see cref="UiaScrollbar"/>) attaches like every other list, and content
    /// reserves the same right gutter.</para>
    ///
    /// <para>No per-frame allocations: rows are pooled and re-bound only when their data index
    /// changes; the visibility scan is a handful of float ops per frame. No statics — the whole
    /// list dies with its page, hot-reload clean by construction.</para>
    /// </summary>
    public sealed class UiaVirtualList : MonoBehaviour
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private float _rowH = 30f;
        private int _count;
        private Func<RectTransform, RectTransform> _makeRow;
        private Action<int, RectTransform> _bindRow;

        private readonly List<RectTransform> _pool = new List<RectTransform>(16);
        private readonly List<int> _boundIndex = new List<int>(16);   // -1 = unbound / hidden

        /// <summary>The underlying ScrollRect (for a caller that wants to save/restore position).</summary>
        public ScrollRect Scroll => _scroll;

        /// <summary>Build a virtual list under <paramref name="parent"/>.
        /// <paramref name="makeRow"/> constructs ONE reusable row under the given content parent
        /// (widgets included) and returns its RectTransform — called only while the pool grows.
        /// <paramref name="bindRow"/> paints a row for a data index — called on scroll/refresh.
        /// <paramref name="viewHeight"/> &gt; 0 pins the visible height; otherwise the list
        /// flexes to fill its host (the usual page arrangement).</summary>
        public static UiaVirtualList Create(Transform parent, float rowHeight,
            Func<RectTransform, RectTransform> makeRow, Action<int, RectTransform> bindRow,
            float viewHeight = -1f)
        {
            var hostGo = UiaUi.Go("vlist", parent);
            if (viewHeight > 0f) UiaUi.Size(hostGo, viewHeight);
            else UiaUi.Size(hostGo, flexH: 1f);

            var viewportImg = hostGo.AddComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.001f);   // wheel-hit + mask target
            hostGo.AddComponent<RectMask2D>();

            var scroll = hostGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.viewport = (RectTransform)hostGo.transform;

            var contentGo = UiaUi.Go("content", hostGo.transform);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            // The kit-wide right gutter: the scroll bar's overlay lane (see UiaUi.ScrollView).
            content.offsetMax = new Vector2(-14f, 0f);
            scroll.content = content;

            UiaScrollbar.Create(scroll, (RectTransform)hostGo.transform, content);

            var list = hostGo.AddComponent<UiaVirtualList>();
            list._scroll = scroll;
            list._viewport = (RectTransform)hostGo.transform;
            list._content = content;
            list._rowH = Mathf.Max(8f, rowHeight);
            list._makeRow = makeRow;
            list._bindRow = bindRow;
            return list;
        }

        /// <summary>Set (or change) the data length. Content height follows; rows re-bind on the
        /// next frame's visibility pass. Scroll position is clamped, not reset.</summary>
        public void SetCount(int count)
        {
            _count = Mathf.Max(0, count);
            if (_content != null)
                _content.sizeDelta = new Vector2(_content.sizeDelta.x, _count * _rowH);
            // Force every pooled row to re-bind (indices may now show different data).
            for (int i = 0; i < _boundIndex.Count; i++) _boundIndex[i] = -1;
        }

        /// <summary>Re-bind every visible row in place (the data changed but not its length).</summary>
        public void RefreshVisible()
        {
            for (int i = 0; i < _boundIndex.Count; i++) _boundIndex[i] = -1;
        }

        /// <summary>Scroll so the given data index sits at the top of the viewport.</summary>
        public void ScrollTo(int index)
        {
            if (_content == null || _viewport == null) return;
            float contentH = _count * _rowH;
            float viewH = _viewport.rect.height;
            float travel = contentH - viewH;
            if (travel <= 0f || _scroll == null) return;
            float top = Mathf.Clamp(index, 0, _count - 1) * _rowH;
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - top / travel);
        }

        private void Update()
        {
            if (_content == null || _viewport == null || _makeRow == null) return;

            float viewH = _viewport.rect.height;
            if (viewH <= 2f) return;
            // Content pivot is top: anchoredPosition.y grows positive as the list scrolls down.
            float scrolled = Mathf.Max(0f, _content.anchoredPosition.y);
            int first = Mathf.Max(0, (int)(scrolled / _rowH));
            int visible = Mathf.Min(_count - first, (int)(viewH / _rowH) + 2);
            if (visible < 0) visible = 0;

            // Grow the pool to the demand (only ever grows; a shrunken viewport just leaves
            // spare rows inactive).
            while (_pool.Count < visible)
            {
                RectTransform row = null;
                try { row = _makeRow(_content); } catch { }
                if (row == null) return;   // fail-soft: a broken factory shows an empty list
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(0f, _rowH);
                _pool.Add(row);
                _boundIndex.Add(-1);
            }

            for (int k = 0; k < _pool.Count; k++)
            {
                var row = _pool[k];
                if (row == null) continue;
                if (k >= visible)
                {
                    if (row.gameObject.activeSelf) row.gameObject.SetActive(false);
                    if (_boundIndex[k] != -1) _boundIndex[k] = -1;
                    continue;
                }
                int dataIndex = first + k;
                if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
                row.anchoredPosition = new Vector2(0f, -dataIndex * _rowH);
                if (_boundIndex[k] != dataIndex)
                {
                    _boundIndex[k] = dataIndex;
                    if (_bindRow != null)
                    {
                        try { _bindRow(dataIndex, row); }
                        catch (Exception e) { Core.UIALog.Warn("VirtualList bind failed: " + e.Message); }
                    }
                }
            }
        }
    }
}
