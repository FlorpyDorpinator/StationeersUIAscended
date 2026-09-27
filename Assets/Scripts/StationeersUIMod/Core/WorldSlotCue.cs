using Assets.Scripts;          // CursorManager, CameraController
using Assets.Scripts.Objects;  // Slot, Interactable, DynamicThing, Thing
using Assets.Scripts.UI;       // InputMouse, DragResult
using StationeersUIMod.UI.Menu.Tutorial;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Drives vanilla's world-slot PLACEMENT HIGHLIGHT — the little green / yellow / blue / red box that
    /// appears inside a device or locker slot as you drag an item toward it — during OUR custom drags.
    ///
    /// <para>WHY. Vanilla draws that box in <c>InputMouse.HandleSlotDisplay</c> →
    /// <c>CursorManager.SetSelection</c>, but <c>HandleSlotDisplay</c> early-returns the instant
    /// <c>BlockCursorRaycast</c> is set (InputMouse.cs:343) — and every UIA drag holds that flag (the
    /// radial's <see cref="ModalScope"/>, a HUD hand-box drag). So when you dragged a radial chip over a
    /// charger or locker slot, the placement box never showed. <c>SetSelection</c> itself is NOT gated by
    /// the flag — only the world raycast that feeds vanilla's version is — so we reproduce vanilla's OWN
    /// math (its <c>GetHoverWorldSlot</c> raycast, <c>InputMouse.IsValid</c>, and the same
    /// result→colour mapping as <c>GetWorldResultColor</c>) and call <c>SetSelection</c> ourselves.</para>
    ///
    /// <para>READ-ONLY. This only draws a highlight (and plays vanilla's own selection cue via
    /// SetSelection's built-in sound-on-change); it never mutates game state. The real move still funnels
    /// through <see cref="ItemActions"/> on release, execute-gated. The raycast bounds the range exactly
    /// as vanilla's does (<c>CursorManager.MaxInteractDistance</c>), so no out-of-reach slot ever lights.</para>
    ///
    /// <para>Because vanilla's HandleSlotDisplay is frozen while we hold the block, nothing else ever
    /// hides the box — so we MUST hide it ourselves the moment the drag leaves a slot or ends
    /// (<see cref="Hide"/>). <see cref="Shutdown"/> resets the shown-state for hot-reload safety.</para>
    /// </summary>
    internal static class WorldSlotCue
    {
        private static bool _showing;

        /// <summary>Per-frame while a drag is live: show / refresh the placement box for the world slot
        /// under the cursor, or hide it when there is none. Pass the carried item; pass null (or call
        /// <see cref="Hide"/>) when no drag is in flight. Safe to call every frame — a no-op when idle.</summary>
        public static void Tick(DynamicThing carried)
        {
            if (carried == null) { Hide(); return; }

            Slot slot;
            Interactable interactable;
            if (!TryWorldSlot(out slot, out interactable) || interactable == null || slot == null)
            {
                Hide();
                return;
            }

            try
            {
                DragResult result = InputMouse.IsValid(carried, slot);
                CursorManager.SetSelection(interactable.GetSelection(), ResultColor(result));
                // Tutorial hook (Build Contract s4): fires on the "just appeared" edge only (the box
                // is ticked every frame while a drag lingers over the same slot).
                if (!_showing) TutorialSignals.Raise(TSignal.WorldSlotCueShown);
                _showing = true;
            }
            catch { Hide(); }
        }

        /// <summary>Take the placement box down. Idempotent.</summary>
        public static void Hide()
        {
            if (!_showing) return;
            _showing = false;
            try { CursorManager.SetSelectionVisibility(false); } catch { }
        }

        /// <summary>Vanilla <c>InputMouse.GetHoverWorldSlot</c>, but returning the interactable too — we
        /// need it for <c>GetSelection()</c> and vanilla's private version discards it.</summary>
        private static bool TryWorldSlot(out Slot slot, out Interactable interactable)
        {
            slot = null;
            interactable = null;
            try
            {
                var cam = CameraController.CurrentCamera;
                if (cam == null) return false;
                float maxDist = 3f;
                try { maxDist = CursorManager.MaxInteractDistance; } catch { }
                int mask = CursorManager.Instance != null ? (int)CursorManager.Instance.CursorHitMask : ~0;
                RaycastHit hit;
                if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out hit, maxDist, mask)) return false;
                var thing = hit.transform.GetComponentInParent<Thing>();
                if (thing == null) return false;
                interactable = thing.GetInteractable(hit.collider);
                if (interactable == null || interactable.Slot == null) { interactable = null; return false; }
                slot = interactable.Slot;
                return true;
            }
            catch { slot = null; interactable = null; return false; }
        }

        /// <summary>Vanilla <c>InputMouse.GetWorldResultColor</c>, kept in lockstep so our box reads the
        /// same as vanilla's: green = will place / swap, yellow = merge, blue = insert into a bag, red =
        /// refused.</summary>
        private static Color ResultColor(DragResult result)
        {
            switch (result)
            {
                case DragResult.Swap:
                case DragResult.Valid:
                    return Color.green;
                case DragResult.Merge:
                    return Color.yellow;
                case DragResult.Insert:
                    return Color.blue;
                default:
                    return Color.red;
            }
        }

        /// <summary>Hot-reload / teardown: forget the shown-state (the game singleton owns the actual
        /// highlighter object, so there is nothing to destroy — just drop our latch).</summary>
        public static void Shutdown()
        {
            _showing = false;
        }
    }
}
