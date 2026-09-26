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
        private static int _armFrame = -999;        // the frame the grab was PROMOTED to a live drag
        private static bool _blockedRaycast;        // did WE assert the cursor-raycast block (via the arbiter)?
        private const string BlockId = "hudslotdrag";   // our named hold in Core.CursorBlockArbiter

        // ---- ARMED-BUT-NOT-YET-DRAGGING press (the click/drag split) ----
        // A mouse-down on an occupied box ARMS here without setting _source: IsDragging stays FALSE so
        // a pure click can still resolve as a mouse-mod PIN (StationeersUIMod.TryPinFromHudEquipmentClick,
        // which permanently disqualifies itself the instant a real drag begins). The press only PROMOTES
        // to an actual tear-out once the pointer leaves the click slop — mirroring vanilla's
        // Idle -> Click -> Drag machine, whose Click only promotes after the cursor moves off the press.
        private static bool _pressPending;
        private static Slot _pressSlot;
        private static string _pressLabel;
        private static Vector2 _pressPos;

        /// <summary>How far (screen px) the pointer may travel between press and release before the
        /// press PROMOTES from a pin-eligible click into a real drag. Kept EQUAL to the pin handler's
        /// <c>StationeersUIMod.PinClickSlopPx</c> and measured from the same down-frame cursor point, so
        /// the two decisions can never disagree: any movement that starts a drag also puts the release
        /// outside the pin's click slop (and IsDragging disqualifies the pin regardless).</summary>
        private const float PromoteSlopPx = 5f;

        /// <summary>TRUE from the frame a grab is PROMOTED to a real drag until its release is FULLY
        /// resolved. A pure read of <c>_source</c> — pinned in <see cref="PromoteToDrag"/> (NOT at the
        /// press: an armed-but-un-promoted press leaves this FALSE precisely so a pure click can resolve
        /// as a pin) and only cleared by <see cref="Cancel"/>, which <see cref="Release"/> calls in its
        /// finally AFTER the move/drop has executed, so this stays true for the whole gesture including
        /// the resolution itself. Exists so other raw-Input consumers over the same boxes
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

                if (_pressPending)
                {
                    // A press is armed but has not become a drag yet. Promote it once the pointer
                    // leaves the click slop; a release below the slop is a pure CLICK, left for the
                    // mouse-mod pin. TryPromotePendingPress returns true ONLY on the frame it just
                    // promoted — we then fall through into the live-drag handler below so that a fast
                    // flick (movement AND the mouse-up landing on one frame) resolves as a DROP this
                    // same frame instead of losing its release edge to the next tick.
                    if (!TryPromotePendingPress()) return;
                }
                else
                {
                    // Refuse the grab when a real UGUI element is under the pointer: our own boxes are
                    // raycastTarget = false, so this is false over them and true over a bag grid cell,
                    // the Control Center, or any vanilla window drawn above us — whoever owns the
                    // pixel owns the click, and only one drag may start from one press.
                    if (Input.GetMouseButtonDown(0) && !PointerOverOtherUi()) ArmPress();
                    return;
                }
            }

            // WATCHDOG. The gesture can only END on an edge, and edges get lost: Unity reports
            // GetMouseButtonDown and GetMouseButtonUp in the SAME frame when the OS delivers
            // press+release between two polls (a fast click at low frame rates), and a release
            // delivered while the app is unfocused is never seen at all — the frame-gap self-heal
            // does not help there because frameCount does not advance while unfocused. Without this
            // the drag stays armed forever: a ghost trailing a button nobody holds, and the NEXT
            // unrelated click's release executing a real move. Held-state is the ground truth.
            // The `!GetMouseButtonUp(0)` term is load-bearing: on a NORMAL release frame Unity reports
            // GetMouseButton(0)==false AND GetMouseButtonUp(0)==true, so without it this watchdog would
            // Cancel() the gesture BEFORE the release handler at the bottom of Tick ever runs — every
            // hand/equipment drop would silently abort (confirmed: the drop never reached Release). The
            // watchdog must fire only for a GENUINELY lost edge — button not held AND no up-edge seen
            // this frame (app unfocused on release, or a frame the OS coalesced). This mirrors the
            // pending-press watchdog in TryPromotePendingPress, which already checks both edges.
            if (Time.frameCount != _armFrame && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) { Cancel(); return; }

            // The pinned item left the slot under us (teammate took it, despawn, our own move
            // round-tripped): abandon quietly rather than acting on whatever replaced it.
            if (_source.Occupant == null) { Cancel(); return; }

            UpdateGhost();
            // The Grid + pinned raycasters are refreshed LATER this frame (TheGridPanel.Tick), but our
            // drop resolves NOW — HudSystem.Update (which pumps us) runs before that Tick. Force them on
            // for the release pick so a flick-drop onto a pinned cell, whose raycaster was disabled last
            // frame while the cursor was still locked, lands instead of falling through to drop-at-feet.
            // No-op unless a drag is live; pure raycaster writes.
            Grid.TheGridPanel.PrimeDragRaycasters();
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

            // The mod's own UGUI surfaces (The Grid, the pinned inventory window, the Control
            // Center) assert CursorManager.BlockCursorRaycast, which makes vanilla's InputMouse.Update
            // early-return — so WorldMode stays pinned at Drag with CursorItem still set and NOBODY
            // observes the mouse-up. Without this clause the inbound mirror republishes that FROZEN
            // drag every frame (boxes stuck lit, a dead Thing held in a static) and a later unrelated
            // click could still route it into a slot. These are UGUI, so ImGuiWantsMouse misses them.
            // ...but not when WE are the one asserting it for our own live gesture.
            if (!_blockedRaycast && CursorRaycastBlocked()) return false;

            return CursorFree();
        }

        /// <summary>True when the cursor is FREE (visible + in the mod's mouse mode, not character
        /// customisation) — the canonical "the player can interact with the HUD/UGUI right now"
        /// signal. Exposed so the equipment-slot click-to-pin path (StationeersUIMod
        /// .TryPinFromHudEquipmentClick) gates on the EXACT same condition the drag layer uses: if
        /// you can tear an item OUT of a 1-6 box, a plain click pins it — no Alt modifier needed.
        /// When the cursor is LOCKED to the FPS crosshair this is false, so a normal attack/use
        /// click is never hijacked.</summary>
        public static bool IsCursorFree => CursorFree();

        private static bool CursorFree()
        {
            try
            {
                if (!Cursor.visible) return false;
                return MouseModeController.InGame && !MouseModeController.InCharacterCustomisation;
            }
            catch { return Cursor.visible; }
        }

        /// <summary>
        /// Assert vanilla's cursor-raycast block for the duration of OUR gesture — the same protection
        /// <c>ModalScope</c> gives the radial, which runs this IDENTICAL grab out of these identical
        /// boxes and is safe only because of it.
        ///
        /// Patching <c>Drag</c>/<c>DragSlot</c> alone is not enough: vanilla's machine is
        /// <c>Idle → Click → Drag</c>, and <c>Click</c> only promotes after the cursor moves 0.1 px.
        /// So underneath a HUD-box grab, <c>Idle()</c> still runs — the PRESS itself can fire a world
        /// interactable behind the box (a door cycles), and a release that never moved far enough
        /// runs <c>Click()</c> → <c>MoveCurrentItemToHand</c>. Two mutations from one press, through a
        /// path the prefixes never see. Blocking the raycast shuts the whole machine off at the source.
        ///
        /// Routed through <see cref="CursorBlockArbiter"/> as a NAMED hold, so overlapping owners (a
        /// radial, The Grid, a pinned window) keep the flag up until the LAST releases — our release can
        /// never stomp theirs, and theirs can never stomp ours. The arbiter is edge-driven exactly like
        /// the old save/restore, so it still never fights a non-UIA writer.
        /// </summary>
        private static void BlockVanillaCursor(bool on)
        {
            if (on)
            {
                if (_blockedRaycast) return;
                _blockedRaycast = true;
                CursorBlockArbiter.Hold(BlockId);
            }
            else if (_blockedRaycast)
            {
                _blockedRaycast = false;
                CursorBlockArbiter.Release(BlockId);
            }
        }

        /// <summary>True while some surface has parked vanilla's cursor raycast — which also freezes
        /// InputMouse.Update, so any vanilla drag state we can see is stale, not live.</summary>
        private static bool CursorRaycastBlocked()
        {
            try
            {
                var cm = CursorManager.Instance;
                return cm != null && cm.BlockCursorRaycast;
            }
            catch { return false; }
        }

        private static bool ImGuiWantsMouse()
        {
            try { return ImGuiNET.ImGui.GetIO().WantCaptureMouse; }
            catch { return false; }
        }

        /// <summary>Exposed for the INBOUND patch: it must apply the SAME occlusion rule the outbound
        /// grab does, or a release over other UI falls through to vanilla's through-the-box resolve.</summary>
        internal static bool PointerOverOtherUiPublic() => PointerOverOtherUi();

        private static bool PointerOverOtherUi()
        {
            try
            {
                var es = EventSystem.current;
                return es != null && es.IsPointerOverGameObject();
            }
            catch { return false; }
        }

        /// <summary>ARM a press on an occupied box without committing to a drag. Records the slot and
        /// the down-frame cursor point, and blocks vanilla's cursor from the FIRST frame — the press
        /// itself must never fire a world interactable behind the box, nor a release-without-move run
        /// vanilla's Click() -> MoveCurrentItemToHand (see <see cref="BlockVanillaCursor"/>). Crucially
        /// it does NOT set <c>_source</c>, so <see cref="IsDragging"/> stays FALSE: a pure click can
        /// then be claimed by the mouse-mod PIN. Promotion to a real tear-out happens later, once the
        /// pointer moves, in <see cref="TryPromotePendingPress"/>.</summary>
        private static void ArmPress()
        {
            var zone = HudSystem.ZoneAt();
            Slot slot = zone != null ? zone.Slot : null;
            if (slot == null) return;
            DynamicThing occ = null;
            try { occ = slot.Get(); } catch { }
            if (occ == null) return;                       // empty box: nothing to tear out, nothing to pin

            _pressPending = true;
            _pressSlot = slot;
            _pressLabel = zone.Label ?? "";
            _pressPos = (Vector2)Input.mousePosition;
            BlockVanillaCursor(true);                      // stop vanilla's Idle()/Click() from the first frame
        }

        /// <summary>Resolve an armed press. Returns TRUE only on the frame it PROMOTES to a live drag
        /// (caller falls through to the drag handler); FALSE while still a click, when the press was
        /// released as a pure click, or when it was abandoned. Robust under either mouse-button edge
        /// convention: the release edge (<c>GetMouseButtonUp</c>) always wins over "not held", so a
        /// normal release is never mistaken for a lost one.</summary>
        private static bool TryPromotePendingPress()
        {
            // WATCHDOG: neither held NOR an up-edge this frame -> the release was delivered on a frame
            // we did not run (app unfocused, a menu stole the frame). Held-state is the ground truth;
            // abandon a press nobody holds rather than letting a later unrelated click promote it.
            if (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) { ClearPress(); return false; }

            // The pressed slot emptied before we acted (teammate took it, our own move round-tripped,
            // despawn): nothing to tear out and nothing to pin.
            DynamicThing occ = null;
            try { occ = _pressSlot != null ? _pressSlot.Get() : null; } catch { }
            if (occ == null) { ClearPress(); return false; }

            // Left the click slop -> a real tear-out, even if the button is ALSO coming up this frame
            // (a fast flick) so the drop resolves rather than reading as a click.
            Vector2 now = (Vector2)Input.mousePosition;
            if ((now - _pressPos).sqrMagnitude > PromoteSlopPx * PromoteSlopPx)
            {
                PromoteToDrag(occ);
                return true;
            }

            // Still within the slop. A release here is a pure CLICK: we never set _source, IsDragging
            // stayed false the whole gesture, so the pin handler resolves. Forget the press (which also
            // hands vanilla's cursor back). Otherwise keep waiting for movement or release.
            if (Input.GetMouseButtonUp(0)) ClearPress();
            return false;
        }

        /// <summary>Commit an armed press to a live drag: pin the source, publish the drop cue, spawn
        /// the ghost and play the pickup cue — everything <c>TryBegin</c> used to do on the down frame,
        /// now deferred until the gesture is unambiguously a drag. Vanilla's cursor is already blocked
        /// from <see cref="ArmPress"/>; <see cref="ClearPress"/> here leaves that block in place because
        /// <c>_source</c> is now set (the drag keeps it until <see cref="Cancel"/>).</summary>
        private static void PromoteToDrag(DynamicThing occ)
        {
            _source = new ScannedSlot
            {
                Slot = _pressSlot,
                Holder = _pressSlot != null ? _pressSlot.Parent : null,
                Location = _pressLabel ?? "",
            }.Pin();

            _armFrame = Time.frameCount;
            HudDropCue.Dragging = occ;                     // boxes light up accepted targets
            HudDropCue.Kind = HudDropCue.DropCueKind.HudSlotDrag;   // we resolve through DragTo
            _cuePublished = true;
            ShowGhost(occ);
            try { UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash); } catch { }

            ClearPress();   // _source is set, so this clears the pending fields WITHOUT releasing the block
        }

        /// <summary>Forget an armed press. Idempotent. Hands vanilla's cursor raycast back ONLY when no
        /// drag is live — <see cref="PromoteToDrag"/> calls this after setting <c>_source</c>, so the
        /// live drag keeps the block it inherited from <see cref="ArmPress"/>.</summary>
        private static void ClearPress()
        {
            bool wasPending = _pressPending;
            _pressPending = false;
            _pressSlot = null;
            _pressLabel = null;
            _pressPos = Vector2.zero;
            if (wasPending && _source == null) BlockVanillaCursor(false);
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
                // Off every box AND off all other UI: first give a PHYSICAL-WORLD slot under the
                // cursor its chance (D-002's sibling: a battery dragged from a HUD hand box onto a
                // charger must land IN the charger, not on the floor). Same picker + DragTo ladder
                // as the grid release; a world slot that refuses the item ABORTS the drop rather
                // than falling through to the ground.
                else
                {
                    Slot world = Grid.BagGridCell.WorldSlotUnderCursor();
                    if (world != null) ItemActions.DragTo(src, world);
                    // Truly open world: the player deliberately aimed at nothing, so drop at their
                    // feet. This no longer consults ZonesAvailable — ZoneAt (box geometry) is
                    // decoupled from the alpha/dropout availability gate, so a transient HUD flicker
                    // on the release frame can no longer swallow a genuine world-drop. The move still
                    // funnels through ItemActions.DropToWorld -> OnServer.MoveToSlotOrWorld (execute-gated).
                    else ItemActions.DropToWorld(src);
                }
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
        internal static Slot GridCellSlotUnderCursor()
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
            // Host the ghost on the shared TOP-MOST drag layer (5250), not the HUD canvas (3800):
            // parented under the HUD it flew BEHIND the Grid / pinned windows you were dropping into.
            var root = Core.DragGhostLayer.EnsureHost();
            if (root == null) return;                      // layer unavailable: drag still works, no ghost
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
            if (_source == null && !_cuePublished && _ghost == null && !_blockedRaycast && !_pressPending) return;
            BlockVanillaCursor(false);   // hand vanilla's cursor raycast back exactly as we found it
            // Stamp ONLY when a real grab was live: Cancel also runs for a ghost/cue-only tidy-up AND
            // for an armed-but-never-promoted press (a click abandoned by the gate closing), and those
            // must not make a consumer reject an innocent click frame — an un-promoted press never
            // became a drag, so LastDragEndFrame must NOT disqualify the pin it might still resolve as.
            if (_source != null) _lastDragEndFrame = Time.frameCount;
            _source = null;
            // Drop any armed-but-un-promoted press. The block it asserted was already handed back above.
            _pressPending = false;
            _pressSlot = null;
            _pressLabel = null;
            _pressPos = Vector2.zero;
            if (_cuePublished)
            {
                HudDropCue.Dragging = null;
                HudDropCue.HoveredSlot = null;
                // Kind MUST go back to the default too. Cancel() is the normal terminator of every
                // outbound gesture, and the radial never writes Kind — so leaving it at HudSlotDrag
                // meant the next radial session inherited the wrong Accepts ladder and lit boxes
                // green for drops MoveWorldItemToSlot/SwapIntoSlot always refuse (2026-07-20 review).
                HudDropCue.Kind = HudDropCue.DropCueKind.RadialChip;
                _cuePublished = false;
            }
            if (_ghost != null && _ghost.gameObject != null) _ghost.gameObject.SetActive(false);
        }

        /// <summary>Hot-reload / HUD teardown: drop every reference so a stale ghost or pinned slot
        /// can never survive into a reloaded assembly (CLAUDE.md static-reset rule).</summary>
        public static void Shutdown()
        {
            bool wasDragging = _source != null;
            BlockVanillaCursor(false);
            _source = null;
            _armFrame = -999;
            _pressPending = false;
            _pressSlot = null;
            _pressLabel = null;
            _pressPos = Vector2.zero;
            if (_cuePublished) { HudDropCue.Clear(); _cuePublished = false; }
            if (_ghost != null && _ghost.gameObject != null)
                Object.Destroy(_ghost.gameObject);
            _ghost = null;
            _ghostRt = null;
            _lastTickFrame = -999;
            // Keep the stamp when we tore down a LIVE gesture so the inbound patch's Rule 1 still owns
            // this frame's release — Shutdown can land mid-drag (Document-mode flip, hot reload), and
            // clearing it would hand the still-held press back to vanilla's Idle()/Click() machine.
            // Otherwise clear it like the rest of the state.
            _lastDragEndFrame = wasDragging ? Time.frameCount : -999;
            _hits.Clear();
        }
    }
}
