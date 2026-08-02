using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>A thin, driven scroll-position indicator + drag handle for a <see cref="ScrollRect"/>,
    /// mirroring <see cref="UI.Grid.GridScrollbar"/>'s track/thumb pattern for the Kit's plain-Image
    /// style (no PanelGraphic/GridTheme dependency — those are Universal Inventory-only). Attached
    /// automatically by <see cref="UiaUi.ScrollView"/> to every scroll list in the Control Center, so
    /// the item picker's 100+ row list (previously "just wheel/drag", no position indicator at all)
    /// and every other kit list shows "there is more below" instead of silently truncating.
    ///
    /// <para>SELF-TICKING: the component lives on the VIEWPORT GameObject (which the owning tab/panel
    /// never deactivates on its own — tabs are rebuilt, not hidden), and only the separate bar-root
    /// child is toggled inactive when nothing overflows. That split matters: if the driver toggled
    /// its OWN GameObject off, its Update() would never run again to notice content growing back —
    /// this way the bar can hide and reappear across Rebuild()s with no caller-side wiring.</para>
    ///
    /// <para>Parented UNDER the viewport (inside its existing RectMask2D, so it clips to the list and
    /// never bleeds over the chrome) and kept as the last sibling so it draws above the rows. Not
    /// part of the scrolled content, so it stays put while the content moves. No static state, so
    /// hot-reload just destroys it with its GameObject like everything else in the kit.</para>
    ///
    /// <para>INPUT — and why this class handles NO pointer events itself. Unlike
    /// <see cref="UI.Grid.GridScrollbar"/> (which lives on its own GameObject), this driver shares the
    /// viewport GameObject with the ScrollRect, so any event interface implemented HERE would fire in
    /// addition to the ScrollRect's own and fight it: an <c>IScrollHandler</c> forwarding the wheel to
    /// <c>_scroll.gameObject</c> would be forwarding to ITSELF (unbounded recursion -> stack overflow),
    /// and an <c>IDragHandler</c> doing absolute-position jump math would beat the ScrollRect's
    /// relative drag on every list row. Both are therefore delegated correctly instead:
    /// <list type="bullet">
    /// <item>WHEEL: nothing to do. The ScrollRect is on this very GameObject, so a wheel anywhere over
    /// the list — including over the thumb, whose event bubbles up from the bar — reaches it natively.</item>
    /// <item>DRAG: handled by <see cref="UiaScrollbarDrag"/> on the BAR-ROOT child. Bubbling stops at
    /// the first ancestor with a handler, so a press on the thumb/bar is bar-only (jump math) and a
    /// press on list content never sees the bar at all (ScrollRect-only).</item>
    /// </list></para></summary>
    internal sealed class UiaScrollbar : MonoBehaviour
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;

        private RectTransform _self;
        private Image _track;
        private RectTransform _thumbRt;
        private Image _thumb;

        private const float Pad = 2f;       // inset from the viewport's top/right/bottom edge
        private const float Width = 6f;     // bar width, px
        private const float MinThumb = 18f; // shortest readable thumb, px

        /// <summary>Build the bar under <paramref name="viewport"/> and bind it to
        /// <paramref name="scroll"/>. No-op (returns null) if either is missing.</summary>
        public static UiaScrollbar Create(ScrollRect scroll, RectTransform viewport, RectTransform content)
        {
            if (scroll == null || viewport == null || content == null) return null;

            var bar = viewport.gameObject.AddComponent<UiaScrollbar>();
            bar._scroll = scroll;
            bar._viewport = viewport;
            bar._content = content;

            var selfGo = new GameObject("scrollbar", typeof(RectTransform));
            selfGo.transform.SetParent(viewport, false);
            bar._self = (RectTransform)selfGo.transform;
            bar._self.anchorMin = bar._self.anchorMax = new Vector2(1f, 1f);
            bar._self.pivot = new Vector2(1f, 1f);
            // Drag lives on the bar root, NOT on this driver (which shares the viewport GO with the
            // ScrollRect) — see the INPUT note on the class. Only presses that start on the bar/thumb
            // reach it; content drags bubble past it straight to the ScrollRect.
            selfGo.AddComponent<UiaScrollbarDrag>().Owner = bar;

            // Faint track (full bar height), non-raycast so the wheel over empty bar area still
            // reaches the list underneath.
            var tGo = new GameObject("track", typeof(RectTransform));
            tGo.transform.SetParent(bar._self, false);
            var trackRt = (RectTransform)tGo.transform;
            trackRt.anchorMin = Vector2.zero; trackRt.anchorMax = Vector2.one;
            trackRt.offsetMin = Vector2.zero; trackRt.offsetMax = Vector2.zero;
            bar._track = tGo.AddComponent<Image>();
            bar._track.raycastTarget = false;
            UiaImages.Round(bar._track);

            // Thumb — the bar's only raycast target, and thus its drag handle: the press bubbles up
            // one level to the bar root's UiaScrollbarDrag and stops there (never reaching the
            // ScrollRect). Anchored to the top of the bar; positioned/sized every Update.
            var hGo = new GameObject("thumb", typeof(RectTransform));
            hGo.transform.SetParent(bar._self, false);
            bar._thumbRt = (RectTransform)hGo.transform;
            bar._thumbRt.anchorMin = bar._thumbRt.anchorMax = new Vector2(0.5f, 1f);
            bar._thumbRt.pivot = new Vector2(0.5f, 1f);
            bar._thumb = hGo.AddComponent<Image>();
            bar._thumb.raycastTarget = true;
            UiaImages.Round(bar._thumb);

            bar._self.SetAsLastSibling();
            bar.Repaint();
            return bar;
        }

        private void Update() => Repaint();

        /// <summary>Recompute visibility, geometry and colour from the live scroll state. Cheap
        /// (a handful of floats + two RectTransform writes) and safe to call every frame.</summary>
        private void Repaint()
        {
            if (_scroll == null || _viewport == null || _content == null || _self == null) return;

            float viewportH = _viewport.rect.height;
            float contentH = _content.rect.height;
            float travel = contentH - viewportH;

            // Nothing overflows -> hide (no bar when the whole list fits).
            if (travel <= 1f || viewportH <= 4f)
            {
                if (_self.gameObject.activeSelf) _self.gameObject.SetActive(false);
                return;
            }
            if (!_self.gameObject.activeSelf) _self.gameObject.SetActive(true);

            float barH = Mathf.Max(0f, viewportH - Pad * 2f);
            _self.sizeDelta = new Vector2(Width, barH);
            _self.anchoredPosition = new Vector2(-Pad, -Pad);

            // Thumb length = viewport/content ratio; position tracks the scroll (norm 1 = top).
            float thumbH = Mathf.Clamp(barH * (viewportH / contentH), Mathf.Min(MinThumb, barH), barH);
            float vnorm = Mathf.Clamp01(_scroll.verticalNormalizedPosition);
            float topOffset = (1f - vnorm) * (barH - thumbH);
            _thumbRt.sizeDelta = new Vector2(Width, thumbH);
            _thumbRt.anchoredPosition = new Vector2(0f, -topOffset);

            _thumb.color = UiaTheme.AccentDim;
            var mute = UiaTheme.TextMute;
            _track.color = new Color(mute.r, mute.g, mute.b, 0.18f);
        }

        // ---- drag the thumb to scroll (driven by UiaScrollbarDrag on the bar root) ----

        /// <summary>Jump the scroll so the thumb centres on the pointer. Called ONLY for presses that
        /// began on the bar/thumb (the bar root owns the drag handlers).</summary>
        internal void DragTo(PointerEventData e)
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
    }

    /// <summary>The bar's drag handlers, deliberately on the BAR-ROOT child GameObject rather than on
    /// <see cref="UiaScrollbar"/> itself — the driver shares the viewport GameObject with the
    /// <see cref="ScrollRect"/>, and a drag handler there would run IN ADDITION to the ScrollRect's
    /// (and, being added last, win), turning every drag on a list row into a position jump.
    ///
    /// <para>Sitting one level down fixes that with the event system's own routing: bubbling stops at
    /// the first ancestor that handles the event, so a press on the thumb (the only raycast target in
    /// the bar) stops here, while a press on list content never passes through the bar and goes to the
    /// ScrollRect alone. The wheel is unaffected either way — no IScrollHandler anywhere in the bar, so
    /// it always bubbles to the viewport's ScrollRect.</para>
    ///
    /// <para>Kept separate from the driver so the driver can keep ticking on the (always-active)
    /// viewport while this GameObject — the bar root — is toggled off whenever nothing overflows.</para></summary>
    internal sealed class UiaScrollbarDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        internal UiaScrollbar Owner;

        public void OnBeginDrag(PointerEventData e) { if (Owner != null) Owner.DragTo(e); }
        public void OnDrag(PointerEventData e) { if (Owner != null) Owner.DragTo(e); }
    }
}
