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
        public bool IsOpen { get; private set; }

        public ModalScope(string inputStateKey)
        {
            _inputStateKey = inputStateKey;
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
