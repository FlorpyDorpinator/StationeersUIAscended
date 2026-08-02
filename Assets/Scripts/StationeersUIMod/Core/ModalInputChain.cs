using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>Two services shared by the stacked UGUI modals (Control Center 5200, tutorial
    /// coach 5600, Handbook viewer 5700).
    ///
    /// 1. REASSERT. <c>KeyManager.RemoveInputState(key)</c> only re-applies the map's LAST entry
    /// when the removed key was current - and <c>WorldManager.SetGamePause(true)</c> inserts
    /// "WorldManager" -&gt; Paused into that same map. A modal closing above a still-open modal
    /// while a mod pause is held would therefore drop the layer underneath into Paused,
    /// re-arming vanilla's Game|Paused Escape binding over our own window. <see cref="ReassertTop"/>
    /// hands Typing back to the topmost still-open layer after any RemoveInputState.
    ///
    /// 2. ESC SWALLOW. Vanilla binds Escape (KeyMap._Cancel) at key-UP; our windows close on
    /// key-DOWN. Releasing the Typing state on the down frame hands that very key-up to the
    /// game - the pause menu would open (and re-pause) over the world we just revealed. The
    /// swallow holds a one-shot Typing state until Escape has been physically up for two
    /// frames - the exact idiom TheGridPanel ships for its own Esc chain.</summary>
    internal static class ModalInputChain
    {
        private const string EscStateKey = "UIA_ModalEsc";
        private static bool _escHeld;
        private static int _escUpFrames;

        /// <summary>Hand the Typing input state back to the topmost still-open UIA modal (if
        /// any). Call after every RemoveInputState a modal performs, and after the Esc swallow
        /// clears. Safe no-op when nothing is open.</summary>
        internal static void ReassertTop()
        {
            try
            {
                if (UI.Menu.HandbookViewer.IsOpen)
                { KeyManager.SetInputState(UI.Menu.HandbookViewer.InputStateKey, KeyInputState.Typing); return; }
                if (UI.Menu.Tutorial.TutorialCoach.IsOpen)
                { KeyManager.SetInputState(UI.Menu.Tutorial.TutorialCoach.InputStateKey, KeyInputState.Typing); return; }
                if (UI.Menu.UiaControlCenter.IsOpen && UI.Menu.UiaControlCenter.ModalHeld)
                { KeyManager.SetInputState(UI.Menu.UiaControlCenter.InputStateKey, KeyInputState.Typing); return; }
            }
            catch { }
        }

        /// <summary>A UIA modal consumed this Escape press on its DOWN frame: starve vanilla's
        /// key-up binding until the key has been physically up for two frames. Idempotent.</summary>
        internal static void BeginEscSwallow()
        {
            if (_escHeld) return;
            _escHeld = true;
            _escUpFrames = 0;
            try { KeyManager.SetInputState(EscStateKey, KeyInputState.Typing); } catch { }
        }

        /// <summary>Per-frame pump (from StationeersUIMod.Update, outside the main try so a
        /// breaker frame cannot strand the swallow).</summary>
        internal static void Pump()
        {
            if (!_escHeld) return;
            if (Input.GetKey(KeyCode.Escape)) { _escUpFrames = 0; return; }
            if (++_escUpFrames < 2) return;
            Release();
        }

        /// <summary>Unconditional release (also from Shutdown - a held input state must never
        /// survive a hot reload). The swallow key is current while held, so removing it pops
        /// KeyManager to the map's last entry - which is "WorldManager"/Paused whenever a mod
        /// pause is still latched; hand the state straight back to any open UIA modal.</summary>
        internal static void Release()
        {
            _escUpFrames = 0;
            if (!_escHeld) return;
            _escHeld = false;
            try { KeyManager.RemoveInputState(EscStateKey); } catch { }
            ReassertTop();
        }
    }
}
