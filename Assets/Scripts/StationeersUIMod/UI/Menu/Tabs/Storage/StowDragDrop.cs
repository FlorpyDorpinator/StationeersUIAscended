using System;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// Drag-and-drop of a Bag Profile row onto a Storage Layout card = COPY it to that layout
    /// (Part 3 promotion, 2026-09-25). The kebab menu's "Copy to ..." rows remain the accessible
    /// twin — drag is an accelerator, never the only path.
    ///
    /// <para>Pattern: the local IBeginDrag/IDrag/IEndDrag + IDropHandler pair (the BagGridCell
    /// idiom, miniaturized). All three drag events land on the SOURCE row (UGUI routes the whole
    /// gesture to the object it began on); OnDrop fires on the layout card under the cursor
    /// BEFORE OnEndDrag, so the source is still alive to read. A drag that starts predominantly
    /// VERTICAL is not a profile drag at all — it is the user scrolling the profile list, so the
    /// gesture is forwarded to the parent ScrollRect and this row acts purely as a relay (the
    /// BagGridCell empty-cell relay, verbatim).</para>
    ///
    /// <para>Statics (the active drag + the drop callback) are cleared by <see cref="Reset"/>,
    /// which <see cref="StowShared.ResetCaches"/> calls from the StorageTab teardown contract —
    /// nothing here can strand across a hot reload. Everything a drop does is a config write
    /// through <c>StowProfileStore.TransferProfile</c>; no game state is reachable.</para>
    /// </summary>
    internal sealed class ProfileDragSource : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string SetName;       // the layout this row's profile lives in
        public string ProfileName;

        /// <summary>The row currently being dragged, or null. Read by drop targets.</summary>
        public static ProfileDragSource Active { get; private set; }

        private static GameObject _ghost;
        private static RectTransform _ghostRt;

        private bool _forwarding;    // vertical start: this gesture belongs to the ScrollRect

        public void OnBeginDrag(PointerEventData e)
        {
            if (e == null) return;
            // A mostly-vertical tear is a scroll, not a profile drag (the list scrolls).
            if (Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x))
            {
                _forwarding = true;
                Forward(e, ExecuteEvents.beginDragHandler);
                return;
            }
            _forwarding = false;
            Active = this;
            BuildGhost(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_forwarding) { Forward(e, ExecuteEvents.dragHandler); return; }
            if (_ghostRt != null && e != null) _ghostRt.position = e.position;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_forwarding) { Forward(e, ExecuteEvents.endDragHandler); _forwarding = false; return; }
            // OnDrop (on the card) has already fired by now if the release was over a target.
            DestroyGhost();
            if (Active == this) Active = null;
        }

        private void Forward<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn)
            where T : IEventSystemHandler
        {
            var p = transform.parent;
            if (p != null) ExecuteEvents.ExecuteHierarchy(p.gameObject, e, fn);
        }

        private void BuildGhost(PointerEventData e)
        {
            DestroyGhost();
            RectTransform layer = UiaControls.PopupLayer;
            if (layer == null) layer = transform.root as RectTransform;
            if (layer == null) return;
            _ghost = UiaUi.Go("profile-drag-ghost", layer);
            _ghostRt = (RectTransform)_ghost.transform;
            _ghostRt.sizeDelta = new Vector2(170f, 26f);   // widened below to fit the name
            _ghostRt.SetAsLastSibling();
            var bg = _ghost.AddComponent<Image>();
            Color c = StowBadge.ColorFor(ProfileName);
            bg.color = new Color(c.r, c.g, c.b, 0.85f);
            bg.raycastTarget = false;   // the ghost must never block the drop raycast
            UiaImages.Round(bg);
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            // The FULL name (no-ellipsis round): the ghost is not in any layout, so instead of
            // head+tail-shortening the name the pill widens to fit it (names are capped at 40
            // characters by the rename field, so this stays a pill, never a banner).
            var t = UiaUi.Text(_ghost.transform, ProfileName ?? "", UiaTheme.SmallSize,
                lum > 0.55f ? new Color(0.05f, 0.07f, 0.10f, 1f) : new Color(0.95f, 0.97f, 1f, 1f),
                TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)t.transform);
            float need = 0f;
            try { need = t.GetPreferredValues(t.text, 32767f, 32767f).x; } catch { }
            if (need + 20f > 170f) _ghostRt.sizeDelta = new Vector2(need + 20f, 26f);
            if (e != null) _ghostRt.position = e.position;
        }

        private static void DestroyGhost()
        {
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost);
            _ghost = null;
            _ghostRt = null;
        }

        private void OnDisable()
        {
            // A Refresh mid-drag (should not happen — gestures rebuild, drags do not — but a
            // theme restyle CAN land) must not strand a ghost or a stale Active reference.
            if (Active == this) Active = null;
            if (_forwarding) _forwarding = false;
            DestroyGhost();
        }

        /// <summary>Hot-reload / teardown: no stale ghost canvas, no dead delegate.</summary>
        public static void ResetStatics()
        {
            Active = null;
            DestroyGhost();
        }
    }

    /// <summary>A Storage Layout card as a drop target. Copy semantics only (a drop never
    /// MOVES — the kebab keeps Move); the actual transfer runs through the one callback the
    /// Organizer wires per build.</summary>
    internal sealed class LayoutDropTarget : MonoBehaviour,
        IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public string SetName;
        /// <summary>Outline flashed while a compatible drag hovers the card (assigned at build).</summary>
        public Outline HoverOutline;

        public void OnDrop(PointerEventData e)
        {
            var src = ProfileDragSource.Active;
            SetHover(false);
            if (src == null || string.IsNullOrEmpty(src.ProfileName)) return;
            if (string.Equals(src.SetName, SetName, StringComparison.OrdinalIgnoreCase)) return;
            var handler = StowDragDrop.OnProfileDropped;
            if (handler != null) handler(src.SetName, src.ProfileName, SetName);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            var src = ProfileDragSource.Active;
            if (src == null) return;
            SetHover(!string.Equals(src.SetName, SetName, StringComparison.OrdinalIgnoreCase));
        }

        public void OnPointerExit(PointerEventData e) { SetHover(false); }

        private void SetHover(bool on)
        {
            if (HoverOutline != null && HoverOutline.enabled != on) HoverOutline.enabled = on;
        }
    }

    /// <summary>The drag-drop wiring point: one static callback (sourceSet, profileName,
    /// targetSet), assigned by the Organizer on every build and cleared on teardown.</summary>
    internal static class StowDragDrop
    {
        public static Action<string, string, string> OnProfileDropped;

        public static void Reset()
        {
            OnProfileDropped = null;
            ProfileDragSource.ResetStatics();
        }
    }
}
