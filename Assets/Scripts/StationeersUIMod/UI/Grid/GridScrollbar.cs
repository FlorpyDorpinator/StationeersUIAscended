using StationeersUIMod.UI.Hud;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// A thin, rounded ("ovular") scroll bar pinned to the right inside a window's scroll viewport —
    /// shared by the main Universal Inventory window and every pinned window. It is PURELY a driven
    /// indicator + a drag handle over the existing <see cref="ScrollRect"/>: it never owns the scroll
    /// state, it reads <c>verticalNormalizedPosition</c> and the content/viewport heights each
    /// <see cref="Tick"/> and paints the thumb to match.
    ///
    /// <para>DYNAMIC. Auto-hides when the content fits (nothing to scroll); the thumb length is the
    /// viewport/content ratio (clamped to a readable minimum) and its position tracks the scroll, so
    /// it grows and shrinks as bags are pinned/emptied and as the window is resized.</para>
    ///
    /// <para>STYLE comes live from <see cref="GridTheme"/> (F9 → Edit: Universal Inventory): width,
    /// colour (a ColorRef, empty = the window border colour) and opacity. Read every Tick — the
    /// <see cref="PanelGraphic"/> setters are dirty-guarded, so an unchanged bar costs no mesh
    /// rebuild — so it needs no StyleHash plumbing and picks up a tier flip automatically (the border
    /// colour it inherits is already tier-resolved).</para>
    ///
    /// <para>INPUT. Only the THUMB is a raycast target: dragging it scrolls (the drag handler lives on
    /// this root and receives the bubbled event); the wheel is forwarded to the ScrollRect so it keeps
    /// working while the pointer is over the thumb; the faint track is non-raycast so the wheel over
    /// empty bar area still falls through to the list.</para>
    ///
    /// Parented UNDER the viewport (inside its RectMask2D, so it clips to the list area and never
    /// bleeds over the chrome) and kept as the last sibling so it draws above the cells. Not part of
    /// the scrolled content, so it stays put while the content moves.
    /// </summary>
    internal sealed class GridScrollbar : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IScrollHandler
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;

        private RectTransform _self;
        private RectTransform _trackRt;
        private PanelGraphic _track;
        private RectTransform _thumbRt;
        private PanelGraphic _thumb;

        private const float Pad = 2f;          // inset from the viewport top/right/bottom edge
        private const float MinThumb = 18f;    // shortest readable thumb, px

        /// <summary>Build the bar under <paramref name="viewport"/> and bind it to
        /// <paramref name="scroll"/>. Returns null only if the viewport is missing.</summary>
        public static GridScrollbar Create(ScrollRect scroll, RectTransform viewport, RectTransform content)
        {
            if (scroll == null || viewport == null) return null;

            var go = new GameObject("GridScrollbar", typeof(RectTransform));
            go.transform.SetParent(viewport, false);
            var bar = go.AddComponent<GridScrollbar>();
            bar._scroll = scroll;
            bar._viewport = viewport;
            bar._content = content;
            bar._self = (RectTransform)go.transform;
            // Pin to the viewport's top-right corner; height is set to the viewport each Tick.
            bar._self.anchorMin = bar._self.anchorMax = new Vector2(1f, 1f);
            bar._self.pivot = new Vector2(1f, 1f);

            // Faint track (full height), non-raycast so the wheel over it still reaches the list.
            var tGo = new GameObject("Track", typeof(RectTransform));
            tGo.transform.SetParent(bar._self, false);
            bar._trackRt = (RectTransform)tGo.transform;
            bar._trackRt.anchorMin = Vector2.zero;
            bar._trackRt.anchorMax = Vector2.one;
            bar._trackRt.offsetMin = Vector2.zero;
            bar._trackRt.offsetMax = Vector2.zero;
            bar._track = tGo.AddComponent<PanelGraphic>();
            bar._track.raycastTarget = false;

            // Thumb — the raycast target + drag handle (the drag handler on this root gets the
            // bubbled event). Anchored to the top of the bar; positioned/sized each Tick.
            var hGo = new GameObject("Thumb", typeof(RectTransform));
            hGo.transform.SetParent(bar._self, false);
            bar._thumbRt = (RectTransform)hGo.transform;
            bar._thumbRt.anchorMin = bar._thumbRt.anchorMax = new Vector2(0.5f, 1f);
            bar._thumbRt.pivot = new Vector2(0.5f, 1f);
            bar._thumb = hGo.AddComponent<PanelGraphic>();
            bar._thumb.raycastTarget = true;

            bar._self.SetAsLastSibling();
            return bar;
        }

        /// <summary>Recompute visibility, geometry and style from the live scroll + theme. Call once
        /// per frame while the owning window is open. Cheap and dirty-guarded.</summary>
        public void Tick()
        {
            if (_scroll == null || _viewport == null || _content == null || _self == null) return;

            float viewportH = _viewport.rect.height;
            float contentH = _content.rect.height;
            float travel = contentH - viewportH;

            // Nothing overflows → hide (no bar when the whole list fits).
            if (travel <= 1f || viewportH <= 4f)
            {
                if (_self.gameObject.activeSelf) _self.gameObject.SetActive(false);
                return;
            }
            if (!_self.gameObject.activeSelf) _self.gameObject.SetActive(true);

            float width = GridTheme.ScrollBarPx;
            float barH = Mathf.Max(0f, viewportH - Pad * 2f);

            // Right edge of the viewport, inset by Pad on all three sides.
            _self.sizeDelta = new Vector2(width, barH);
            _self.anchoredPosition = new Vector2(-Pad, -Pad);

            // Thumb length = viewport/content ratio; position tracks the scroll (norm 1 = top).
            float thumbH = Mathf.Clamp(barH * (viewportH / contentH), Mathf.Min(MinThumb, barH), barH);
            float vnorm = Mathf.Clamp01(_scroll.verticalNormalizedPosition);
            float topOffset = (1f - vnorm) * (barH - thumbH);
            _thumbRt.sizeDelta = new Vector2(width, thumbH);
            _thumbRt.anchoredPosition = new Vector2(0f, -topOffset);

            // Rounded "pill": radius = half the short side (PanelGraphic clamps to the short side).
            float r = width * 0.5f;
            _thumb.SetShape(width, thumbH, r);
            _track.SetShape(width, barH, r);

            // Live style from the Grid theme. Track is a faint fraction of the thumb colour.
            Color c = GridTheme.ScrollBarColor;
            _thumb.color = c;
            _track.color = new Color(c.r, c.g, c.b, c.a * 0.22f);
        }

        // ---- drag the thumb to scroll (event bubbles up from the thumb, the only raycast target) ----

        public void OnBeginDrag(PointerEventData e) => DragTo(e);
        public void OnDrag(PointerEventData e) => DragTo(e);

        private void DragTo(PointerEventData e)
        {
            if (_scroll == null || _self == null || e == null) return;
            float barH = _self.rect.height;
            float thumbH = _thumbRt != null ? _thumbRt.rect.height : 0f;
            float span = barH - thumbH;
            if (span <= 0.01f) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _self, e.position, e.pressEventCamera, out local))
                return;

            // Self pivot is top-right (1,1); local.y runs 0 at the top to -barH at the bottom.
            float fromTop = Mathf.Clamp(-local.y - thumbH * 0.5f, 0f, span);
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - fromTop / span);
        }

        /// <summary>Keep the wheel working while the pointer is over the thumb by forwarding it to the
        /// ScrollRect (which is not under the cursor then, so it would otherwise get no wheel).</summary>
        public void OnScroll(PointerEventData e)
        {
            if (_scroll != null && e != null)
                ExecuteEvents.Execute(_scroll.gameObject, e, ExecuteEvents.scrollHandler);
        }
    }
}
