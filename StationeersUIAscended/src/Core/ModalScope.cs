using Assets.Scripts;
using Assets.Scripts.UI;

namespace StationeersUIAscended.Core
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
            public bool UnlockCursor => true;
        }

        private readonly UiaModal _modal = new UiaModal();
        private readonly string _inputStateKey;
        private bool _releasePending;
        private int _clearFrames;
        public bool IsOpen { get; private set; }

        public ModalScope(string inputStateKey)
        {
            _inputStateKey = inputStateKey;
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
            ImGuiManager.SetBlockUguiClicks(true);
        }

        public void Close()
        {
            _releasePending = false;
            _clearFrames = 0;
            if (!IsOpen) return;
            IsOpen = false;
            ImGuiManager.SetBlockUguiClicks(false);
            KeyManager.RemoveInputState(_inputStateKey);
            MouseModeController.RemoveModal(_modal);
            CursorManager instance = CursorManager.Instance;
            if (instance != null)
                instance.OnApplicationFocus(true);
            if (PanelToolTip.Instance != null)
                PanelToolTip.Instance.ClearToolTip();
        }
    }
}
