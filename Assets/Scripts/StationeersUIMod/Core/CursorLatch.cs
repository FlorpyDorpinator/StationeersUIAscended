using Assets.Scripts;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Cursor latch (the CursorToggle behaviour): DOUBLE-TAP the mouse-modifier key — the one you
    /// normally hold down to free the cursor — and the cursor stays up hands-free until you press
    /// that same key once more.
    ///
    /// HOW IT HOOKS (and why it is only a few lines): vanilla funnels the entire cursor decision
    /// through ONE property, <c>MouseModeController.AltKeyDown</c> (decompile 27701,
    /// MouseModeController.cs:13), and that property's ONLY consumer is
    /// <c>MouseModeController.Check()</c> (:57) — when it reads true the cursor is unlocked
    /// (SetState(false)); otherwise it is locked. So "latched" is just "report the modifier as held".
    /// A single postfix on that getter therefore inherits every OTHER rule vanilla already applies —
    /// open modals, character customisation, not-in-game — which is why this class owns no cursor,
    /// lock-state or visibility code of its own and can never fight vanilla for control.
    ///
    /// Edge detection deliberately reads the RAW key through <c>KeyManager.GetButtonDown</c> rather
    /// than the patched property, so the latch can always see the press that is meant to cancel it.
    ///
    /// Safety: the latch is dropped whenever we are not in game (or the feature/mod is switched off),
    /// so leaving a world can never strand a free cursor, and <see cref="Reset"/> clears it on
    /// teardown for hot-reload.
    /// </summary>
    public static class CursorLatch
    {
        /// <summary>True while the cursor is being held up by the latch. Read by the AltKeyDown
        /// postfix in <c>Patches.cs</c>.</summary>
        public static bool Active { get; private set; }

        // unscaledTime of the last modifier tap that did NOT latch, or a long-ago sentinel.
        private static float _lastTap = -10f;

        /// <summary>Once per frame from the plugin's Update. <paramref name="radialOpen"/> suppresses
        /// tap detection: the radial binds this SAME key for world-reach / Z-grab
        /// (RadialMenu.cs:337, :783-785 — "the vanilla MouseControl key, default Alt"), so reaching
        /// twice while a wheel is open must not silently latch the cursor. Passed in rather than read
        /// from a static, so there is no extra hot-reload state to reset.</summary>
        public static void Tick(bool radialOpen)
        {
            bool on = UIAConfig.MasterEnable != null && UIAConfig.MasterEnable.Value
                   && UIAConfig.CursorLatchEnabled != null && UIAConfig.CursorLatchEnabled.Value;

            bool inGame = false;
            try { inGame = MouseModeController.InGame && !MouseModeController.InCharacterCustomisation; }
            catch { }

            // Not in game, or switched off mid-latch: release, so we can never strand a free cursor.
            if (!on || !inGame)
            {
                if (Active || _lastTap > -1f) Reset();
                return;
            }

            // Radial open: ignore the key entirely (it means "reach" there). Also forget any pending
            // half of a double-tap, so a tap before opening can't pair with one after closing.
            if (radialOpen) { _lastTap = -10f; return; }

            bool down = false;
            try { down = KeyManager.GetButtonDown(KeyMap.MouseControl); } catch { }
            if (!down) return;

            if (Active)
            {
                // "...until you hit the mouse mod button again."
                Reset();
                return;
            }

            float now = Time.unscaledTime;
            int ms = UIAConfig.CursorLatchMs != null ? UIAConfig.CursorLatchMs.Value : 250;
            float window = Mathf.Clamp(ms, 120, 500) / 1000f;

            if (now - _lastTap <= window)
            {
                Active = true;
                // Consume the pair, so a THIRD quick tap cancels the latch (handled above) instead
                // of counting as the first half of a new double-tap.
                _lastTap = -10f;
            }
            else
            {
                _lastTap = now;
            }
        }

        public static void Reset()
        {
            Active = false;
            _lastTap = -10f;
        }
    }
}
