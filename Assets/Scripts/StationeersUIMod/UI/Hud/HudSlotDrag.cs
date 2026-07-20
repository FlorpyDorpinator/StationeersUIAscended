using System.Collections.Generic;
using Assets.Scripts;              // MouseModeController
using Assets.Scripts.Objects;
using Assets.Scripts.UI;           // SlotDisplayButton
using StationeersUIMod.Core;
using StationeersUIMod.Features;   // RadialController
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Mouse drag-and-drop out of the visor HUD's hand boxes and worn-equipment column.
    ///
    /// WHY THIS EXISTS. Vanilla's ONLY slot-drag implementation lives on <c>SlotDisplayButton</c>,
    /// whose GameObjects sit inside <c>InventoryManager.PanelHandsGameObject</c> and
    /// <c>ClothingPanel</c>. <c>InventoryManager.SetUIPanelVisibility</c> is not an alpha fade — it
    /// calls <c>SetActive(false)</c> — and the mod drives both panels false every frame
    /// (HideVanillaHands / HideVanillaClothing). A deactivated hierarchy receives no pointer events,
    /// so <c>OnBeginDrag</c> never fires and vanilla's whole <c>OnEndDrag</c> resolver — where
    /// drop-at-feet, slot→slot AND insert-into-an-open-bag all live — is unreachable. The mod's own
    /// hand/equipment boxes are pure renderers (raycastTarget = false under a blocksRaycasts = false
    /// CanvasGroup), so nothing was left on screen to grab. (That is true of the BOXES specifically —
    /// <see cref="Widgets.MoodletBorrowWidget"/> deliberately opts its borrowed vanilla subtree back
    /// INTO raycasts to keep vanilla's moodlet tooltip, so a grab under the moodlet strip is refused
    /// by the occlusion gate below. Position the strip clear of the boxes in F9.)
    ///
    /// The GESTURE is not new: <see cref="Overlay.RadialMenu"/> already tears an item out of these
    /// same boxes into its parking layer, but only while a radial is open. This driver runs the same
    /// grab whenever the cursor is genuinely free, reusing <see cref="HudSystem.ZoneAt"/> so a curved
    /// box is grabbed where it DRAWS (the hit-test inverse-warps the cursor). That also means no
    /// EventSystem hit layer is needed — raycastable graphics at the HUD canvas's sorting order
    /// would have swallowed clicks across those whole screen regions.
    ///
    /// INPUT SAFETY. This polls raw <c>Input</c>, so it must refuse the click whenever anyone else
    /// could own it. <see cref="CanDrag"/> is the single gate: no radial, no F9 designer, no vanilla
    /// full-attention menu, nothing captured by ImGui, and a genuinely free cursor; the grab
    /// additionally refuses when the pointer is over any real UGUI element. <see cref="Tick"/> also
    /// SELF-HEALS — if the HUD stops ticking us mid-drag we never see the mouse-up, so a frame gap
    /// always cancels rather than letting a long-finished gesture fire a real move later.
    ///
    /// MULTIPLAYER. Every mutation goes through <see cref="ItemActions"/> (<c>OnServer.MoveToSlot /
    /// SwapSlots / MoveToSlotOrWorld</c>, re-gated at execute time) and the source is
    /// <c>Expected</c>-pinned, so a grab that went stale can never move somebody else's item.
    /// One release = one message.
    /// </summary>
    internal static class HudSlotDrag
    {
        private static ScannedSlot _source;
        private static bool _cuePublished;      // did WE set HudDropCue (vs the radial)?
        private static int _lastTickFrame = -999;
        private static RectTransform _ghostRt;
        private static Image _ghost;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>(8);
        private static int _lastDragEndFrame = -999;

        /// <summary>TRUE from the frame a grab is armed until its release is FULLY resolved.
        /// A pure read of <c>_source</c> — the grab is pinned in <see cref="TryBegin"/> and only
        /// cleared by <see cref="Cancel"/>, which <see cref="Release"/> calls in its finally AFTER
        /// the move/drop has executed, so this stays true for the whole gesture including the
        /// resolution itself. Exists so other raw-Input consumers over the same boxes
        /// (StationeersUIMod's mod-modifier + click pin handler) can refuse a drag.</summary>
        public static bool IsDragging { get { return _source != null; } }

        /// <summary>The frame on which the most recent ACTIVE drag finished (released or cancelled);
        /// -999 when none has. <see cref="IsDragging"/> is already false by the time a same-frame
        /// consumer polls if it ticks after us, so a consumer that must reject the release frame
        /// too should also test <c>Time.frameCount == LastDragEndFrame</c>.</summary>
        public static int LastDragEndFrame { get { return _lastDragEndFrame; } }

        /// <summary>Driven from <see cref="HudSystem"/>'s per-frame tick, BEFORE it resolves
        /// <c>HudDropCue.HoveredSlot</c> — publishing the carried item here means the box widgets
        /// light up valid targets on this same frame, exactly as they do for a radial chip.</summary>
        public static void Tick()
        {
            // SELF-HEAL. HudSystem.Update has early-return paths (HUD stood down, tier switch,
            // not built yet) that skip this tick entirely. A drag begun before such a gap would
            // never observe its mouse-up, and resuming later to see an UNRELATED release would
            // execute a real inventory move — or drop the item on the floor. A gap always cancels.
            int frame = Time.frameCount;
            bool gap = frame - _lastTickFrame > 1;
            _lastTickFrame = frame;
            if (gap && _source != null) { Cancel(); return; }

            if (!CanDrag()) { Cancel(); return; }

            if (_source == null)
            {
                // INBOUND feedback: while VANILLA carries a world item and we do not, publish it so
                // the boxes light their accepted targets exactly as they do for our own grab.
                // Nothing else publishes during a vanilla drag, so without this the whole inbound
                // direction is invisible until the release lands (2026-07-20 review).
                PublishInboundCue();

                // Refuse the grab when a real UGUI element is under the pointer: our own boxes are
                // raycastTarget = false, so this is false over them and true over a bag grid cell,
                // the Control Center, or any vanilla window drawn above us — whoever owns the
                // pixel owns the click, and only one drag may start from one press.
                if (Input.GetMouseButtonDown(0) && !PointerOverOtherUi()) TryBegin();
                return;
            }

            // The pinned item left the slot under us (teammate took it, despawn, our own move
            // round-tripped): abandon quietly rather than acting on whatever replaced it.
            if (_source.Occupant == null) { Cancel(); return; }

            UpdateGhost();
            if (Input.GetMouseButtonUp(0)) Release();
        }

        /// <summary>The INBOUND (vanilla world drag → HUD box) patch reuses this exact gate, so there
        /// is one definition of "the HUD may take this click". Also requires that WE are not already
        /// dragging, so the two directions can never both act on one press.</summary>
        internal static bool GateOpenForWorldDrop() => _source == null && CanDrag();

        /// <summary>Mirror a live VANILLA world drag into <see cref="HudDropCue"/> so the boxes give
        /// the same hover feedback they give our own grab, and take it down again when that drag
        /// ends. Uses the shared <c>_cuePublished</c> ownership flag, so <see cref="Cancel"/> and
        /// <see cref="Shutdown"/> already clean it up and the radial's own cue is never touched.
        /// <c>WorldDrag</c> covers both inbound sources: the only rung that differs between
        /// WorldDragTo and DragTo already branches on the item's ParentSlot at evaluation time.</summary>
        private static void PublishInboundCue()
        {
            DynamicThing carried = null;
            try
            {
                var m = InputMouse.Instance;
                if (m != null && (m.WorldMode == WorldMouseMode.Drag || m.WorldMode == WorldMouseMode.DragSlot))
                    carried = m.CursorItem;
            }
            catch { }

            if (carried != null)
            {
                HudDropCue.Dragging = carried;
                HudDropCue.Kind = HudDropCue.DropCueKind.WorldDrag;
                _cuePublished = true;
            }
            else if (_cuePublished)
            {
                // The vanilla drag ended (resolved elsewhere, or was cancelled) — take our cue down.
                HudDropCue.Dragging = null;
                HudDropCue.HoveredSlot = null;
                HudDropCue.Kind = HudDropCue.DropCueKind.RadialChip;
                _cuePublished = false;
            }
        }

        /// <summary>The one gate. Every clause is a surface that could legitimately own this click.</summary>
        private static bool CanDrag()
        {
            // The radial grabs from these very boxes into its own parking layer.
            if (RadialController.AnyRadialOpen) return false;

            // The F9 HUD designer drags the hand-box ELEMENT with its own raw mouse polling, in the
            // same frame. Without this, repositioning a hand box in the editor would ALSO tear the
            // real item out of the hand and drop it on the floor.
            if (Windows.HudEditorMode.Active) return false;

            // A vanilla full-attention menu (pause/start, Stationpedia, IC editor, console, creative)
            // unlocks the cursor while MouseModeController.InGame stays TRUE — so Cursor.visible on
            // its own is NOT "free for us". This is the same predicate the HUD uses to demote itself.
            if (Guards.VanillaMenuWantsFront()) return false;

            // Anything ImGui is capturing (our F9/F10 windows, vanilla ImGui menus) owns the mouse.
            if (ImGuiWantsMouse()) return false;

            return CursorFree();
        }

        private static bool CursorFree()
        {
            try
            {
                if (!Cursor.visible) return false;
                return MouseModeController.InGame && !MouseModeController.InCharacterCustomisation;
            }
            catch { return Cursor.visible; }
        }

        private static bool ImGuiWantsMouse()
        {
            try { return ImGuiNET.ImGui.GetIO().WantCaptureMouse; }
            catch { return false; }
        }

        private static bool PointerOverOtherUi()
        {
            try
            {
                var es = EventSystem.current;
                return es != null && es.IsPointerOverGameObject();
            }
            catch { return false; }
        }

        private static void TryBegin()
        {
            var zone = HudSystem.ZoneAt();
            Slot slot = zone != null ? zone.Slot : null;
            if (slot == null) return;
            DynamicThing occ = null;
            try { occ = slot.Get(); } catch { }
            if (occ == null) return;                       // empty box: nothing to tear out

            _source = new ScannedSlot
            {
                Slot = slot,
                Holder = slot.Parent,
                Location = zone.Label ?? "",
            }.Pin();

            HudDropCue.Dragging = occ;                     // boxes light up accepted targets
            HudDropCue.Kind = HudDropCue.DropCueKind.HudSlotDrag;   // we resolve through DragTo
            _cuePublished = true;
            ShowGhost(occ);
            try { UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash); } catch { }
        }

        /// <summary>Resolve where the release landed. Order matters: a REAL UI element under the
        /// cursor wins over our own boxes (a bag window drawn above the HUD should take the drop),
        /// and landing on nothing is vanilla's "drop it at my feet".</summary>
        private static void Release()
        {
            var src = _source;
            try
            {
                Slot dest = VanillaSlotUnderCursor() ?? GridCellSlotUnderCursor();
                if (dest == null)
                {
                    var zone = HudSystem.ZoneAt();
                    dest = zone != null ? zone.Slot : null;
                }

                // DragTo treats dest == source as a clean no-op ("changed my mind"); it also picks
                // move / swap / merge and re-gates every one at execute time.
                if (dest != null) ItemActions.DragTo(src, dest);
                // Released on some OTHER mod/vanilla UI that owns the pixel but resolved no slot —
                // the Control Center's chrome, a bag window's title bar or padding. That is an
                // abort, not "throw it on the floor": the grab gate refuses those pixels, so the
                // release must refuse them symmetrically.
                else if (PointerOverOtherUi()) { }
                else ItemActions.DropToWorld(src);
            }
            catch (System.Exception e)
            {
                UIALog.Warn("HUD slot drag failed: " + e.Message);
            }
            finally { Cancel(); }
        }

        /// <summary>A vanilla slot button under the cursor — an OPEN bag / inventory window, which
        /// the mod never hides. Vanilla maintains this static from its own OnPointerEnter.
        ///
        /// The isActiveAndEnabled guard is load-bearing: OnPointerExit is the ONLY thing that clears
        /// <c>CurrentSlot</c>, and <c>SetActive(false)</c> does NOT raise it. A button hidden while
        /// hovered (exactly what this mod does to the hand/clothing panels every frame) would
        /// otherwise leave a live, WRONG destination pinned — and it takes top priority here, so a
        /// later release would teleport the item into a slot the cursor was nowhere near.</summary>
        internal static Slot VanillaSlotUnderCursor()
        {
            try
            {
                var btn = SlotDisplayButton.CurrentSlot;
                if (btn == null || !btn.isActiveAndEnabled) return null;

                // isActiveAndEnabled ALONE is not enough. Nothing else ever clears CurrentSlot
                // (vanilla's ResetMouseOverUI has zero callers), so a hide -> show round trip of the
                // vanilla panels — which is exactly what toggling "Hide vanilla hands" does — leaves
                // a stale button that is active again, and it takes TOP priority here. Cross-check
                // that the cursor is genuinely inside the button before trusting it, or a release
                // over empty space teleports the item into a slot the cursor never touched.
                var rt = btn.transform as RectTransform;
                if (rt == null) return null;
                Camera cam = null;
                var canvas = btn.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = canvas.worldCamera;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam))
                    return null;

                return btn.Slot;
            }
            catch { return null; }
        }

        /// <summary>A cell of the mod's own bag grid under the cursor. Read-only use of
        /// <c>BagGridCell.Slot</c> — this deliberately does not reach into the grid's drag code.
        /// Allocates one PointerEventData, but only on RELEASE, never per frame.</summary>
        private static Slot GridCellSlotUnderCursor()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null) return null;
                var ped = new PointerEventData(es) { position = Input.mousePosition };
                _hits.Clear();
                es.RaycastAll(ped, _hits);
                for (int i = 0; i < _hits.Count; i++)
                {
                    var go = _hits[i].gameObject;
                    if (go == null) continue;
                    var cell = go.GetComponentInParent<Grid.BagGridCell>();
                    if (cell != null && cell.Slot != null) return cell.Slot;
                }
            }
            catch { }
            return null;
        }

        // ---- drag ghost (what you are carrying, under the cursor) ----

        private static void ShowGhost(DynamicThing item)
        {
            var root = HudSystem.OverlayRoot;
            if (root == null) return;                      // HUD not built: drag still works, no ghost
            if (_ghost == null)                            // Unity fake-null also catches a destroyed ghost
            {
                var go = new GameObject("HudDragGhost", typeof(RectTransform));
                go.transform.SetParent(root, false);
                _ghostRt = (RectTransform)go.transform;
                _ghostRt.anchorMin = _ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
                _ghostRt.sizeDelta = new Vector2(48f, 48f);
                _ghost = go.AddComponent<Image>();
                _ghost.raycastTarget = false;              // never let the ghost eat its own drop
                _ghost.preserveAspect = true;
                // Deliberately NO VisorWarp: the ghost tracks the raw cursor and must not be bent
                // by the visor curve the way the boxes underneath it are.
            }
            Sprite icon = null;
            try { icon = item.GetThumbnail(); } catch { }
            _ghost.sprite = icon;
            _ghost.color = icon != null ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0f);
            _ghost.transform.SetAsLastSibling();
            _ghost.gameObject.SetActive(true);
            UpdateGhost();
        }

        private static void UpdateGhost()
        {
            if (_ghostRt == null) return;
            var m = (Vector2)Input.mousePosition;
            _ghostRt.anchoredPosition =
                new Vector2(m.x - Screen.width * 0.5f, m.y - Screen.height * 0.5f);
        }

        /// <summary>Idempotent: safe to call every frame. Clears the drop cue ONLY if we were the
        /// ones who published it — the radial drag layer owns HudDropCue too, and blanking it
        /// unconditionally killed the radial's own drop highlight for the rest of the session.</summary>
        internal static void Cancel()
        {
            // Ticked every frame the gate is closed (radial open, F9 up, menu front), so make the
            // idle case free rather than re-poking Unity's fake-null operator and SetActive.
            if (_source == null && !_cuePublished && _ghost == null) return;
            // Stamp ONLY when a real grab was live: Cancel also runs for a ghost/cue-only tidy-up,
            // and those must not make a consumer reject an innocent click frame.
            if (_source != null) _lastDragEndFrame = Time.frameCount;
            _source = null;
            if (_cuePublished)
            {
                HudDropCue.Dragging = null;
                HudDropCue.HoveredSlot = null;
                _cuePublished = false;
            }
            if (_ghost != null && _ghost.gameObject != null) _ghost.gameObject.SetActive(false);
        }

        /// <summary>Hot-reload / HUD teardown: drop every reference so a stale ghost or pinned slot
        /// can never survive into a reloaded assembly (CLAUDE.md static-reset rule).</summary>
        public static void Shutdown()
        {
            _source = null;
            if (_cuePublished) { HudDropCue.Clear(); _cuePublished = false; }
            if (_ghost != null && _ghost.gameObject != null)
                Object.Destroy(_ghost.gameObject);
            _ghost = null;
            _ghostRt = null;
            _lastTickFrame = -999;
            _lastDragEndFrame = -999;
            _hits.Clear();
        }
    }
}
