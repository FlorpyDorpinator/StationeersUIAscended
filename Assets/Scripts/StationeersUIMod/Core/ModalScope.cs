using Assets.Scripts;
using Assets.Scripts.UI;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Cursor-unlock + key-capture scope for radial menus, copied from the game's own
    /// ImguiCreativeSpawnMenu pattern: IModal via MouseModeController for the cursor,
    /// KeyManager input state for key suppression, and the OnApplicationFocus(true)
    /// reset on close so MouseModeController re-locks the cursor next frame.
    /// </summary>
    public sealed class ModalScope
    {
        private sealed class UiaModal : IModal
        {
            // The cursor is unconditionally unlocked for the whole life of any radial: nothing
            // ever asks the game to re-lock it while a radial is open.
            public bool UnlockCursor => true;
        }

        private readonly UiaModal _modal = new UiaModal();
        private readonly string _inputStateKey;
        private readonly string _blockId;
        private bool _releasePending;
        private int _clearFrames;
        public bool IsOpen { get; private set; }

        public ModalScope(string inputStateKey)
        {
            _inputStateKey = inputStateKey;
            _blockId = "radial:" + inputStateKey;
        }

        /// <summary>
        /// Close, but keep the key-capture (Typing) state until the closing keys are
        /// physically released. The vanilla bindings for RMB (ToggleActiveHandTool) and
        /// Escape (menu/cancel) fire on key-UP — releasing the input state on the DOWN
        /// frame would hand that very key-up to the game (torch toggles, pause menu).
        /// Call Pump() every frame to finish the release.
        /// </summary>
        public void RequestDeferredClose()
        {
            if (!IsOpen || _releasePending) return; // idempotent: re-requesting must NOT reset the countdown
            _releasePending = true;
            _clearFrames = 0;
        }

        /// <summary>Per-frame: completes a deferred close once Escape/mouse/holdKey are all up.</summary>
        public void Pump(UnityEngine.KeyCode holdKey)
        {
            if (!_releasePending || !IsOpen) return;
            bool anyDown = UnityEngine.Input.GetKey(UnityEngine.KeyCode.Escape)
                || UnityEngine.Input.GetMouseButton(0)
                || UnityEngine.Input.GetMouseButton(1)
                || (holdKey != UnityEngine.KeyCode.None && UnityEngine.Input.GetKey(holdKey));
            if (anyDown)
            {
                _clearFrames = 0;
                return;
            }
            if (++_clearFrames >= 2)
                Close();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            KeyManager.SetInputState(_inputStateKey, KeyInputState.Typing);
            MouseModeController.AddModal(_modal);
            TrySetBlockUguiClicks(true);
            // ImGui is invisible to Unity's EventSystem, so with the cursor unlocked
            // vanilla's InputMouse click/drag machine would run UNDER our radial (world
            // pickups through the wedges). BlockCursorRaycast gates both InputMouse.Update
            // and CursorManager.SetCursorTarget; the mod's own world-grab raycast is
            // independent of it. Routed through the arbiter (a NAMED hold) so a later
            // deferred Close cannot stomp The Grid / a pinned window that also holds it —
            // the whole reason those surfaces used to re-assert the flag every frame.
            CursorBlockArbiter.Hold(_blockId);
            // Assert the unlock THIS frame. The game runs MouseModeController.Check() once per frame
            // from CursorManager.ManagerUpdate; if that ran BEFORE this Open() on the open frame, the
            // cursor stays locked for one frame while the radial is already drawn, so vanilla's
            // LateUpdate SetMouseLook() burns one extra frame of camera rotation on the mouse motion
            // made while opening — a one-frame camera hitch, visible only when the mouse was moving
            // (hence intermittent; play-test 2026-07-21). The modal is already registered above, so
            // re-running Check() now drives SetState(false) this frame and no frame order can leave the
            // gap. The game's own Check() later this/next frame just re-confirms (no-op).
            try { MouseModeController.Check(); }
            catch { }
        }

        public void Close()
        {
            _releasePending = false;
            _clearFrames = 0;
            if (!IsOpen) return;
            IsOpen = false;
            TrySetBlockUguiClicks(false);
            KeyManager.RemoveInputState(_inputStateKey);
            MouseModeController.RemoveModal(_modal);
            CursorManager instance = CursorManager.Instance;
            CursorBlockArbiter.Release(_blockId);
            if (instance != null)
                instance.OnApplicationFocus(true);
            if (PanelToolTip.Instance != null)
                PanelToolTip.Instance.ClearToolTip();
        }

        /// <summary>
        /// Safe wrapper: the exact ImGuiManager.SetBlockUguiClicks may vary by game build.
        /// In current assemblies it may not be public or may be replaced by modal + focus rules.
        /// Fail-soft: if the method is missing the radials still work via MouseModeController + Key state.
        /// </summary>
        private static void TrySetBlockUguiClicks(bool block)
        {
            try
            {
                var t = typeof(Assets.Scripts.UI.ImGuiManager);
                var m = t.GetMethod("SetBlockUguiClicks", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (m != null)
                {
                    m.Invoke(null, new object[] { block });
                }
                // else: silently skip; modal already handles input isolation for most cases
            }
            catch { /* fail soft */ }
        }
    }
}

