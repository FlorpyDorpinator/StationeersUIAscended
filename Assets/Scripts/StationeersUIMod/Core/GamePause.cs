using System;
using System.Collections.Generic;
using Assets.Scripts;              // GameManager
using Assets.Scripts.GridSystem;   // GameState (the enum lives here, not with GameManager)
using Assets.Scripts.Inventory;    // InventoryManager
using Assets.Scripts.Networking;   // NetworkManager
using Assets.Scripts.UI;           // ImGuiLoadingScreen
// WorldManager, KeyManager, KeyInputState and NetworkBase are in the GLOBAL namespace.

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The mod's single owner of <c>WorldManager.SetGamePause</c> — a named-reason latch shared by
    /// everything that wants the world to hold still (the first-run tutorial, the F10 pause button).
    ///
    /// <para>WHY A LATCH. Two surfaces can want stillness at once (the tutorial auto-pauses, the
    /// player then clicks the F10 pause button). With each writing <c>SetGamePause</c> off its own
    /// private bool, whichever closed first would resume the world under the other. Reasons are
    /// idempotent strings; the world freezes on the empty -> non-empty edge and thaws only when the
    /// LAST reason leaves — the <see cref="CursorBlockArbiter"/> pattern applied to time.</para>
    ///
    /// <para>SINGLE-PLAYER ONLY, by construction. Vanilla itself only pauses when
    /// <c>!NetworkManager.IsClient &amp;&amp; NetworkBase.Clients.Count == 0</c>
    /// (InventoryManager.cs:184-194, V27758); the only network pause path
    /// (<c>NetworkBase.PauseEvent</c>) is <c>protected</c>. On a client — or a host with anyone
    /// connected — <see cref="CanOwnPause"/> is false and a hold records its reason without freezing
    /// anything. Callers must render that state honestly (a disabled button), never as a silent
    /// no-op that pretends to have worked.</para>
    ///
    /// <para>WE NEVER CLAIM A PAUSE WE DID NOT TAKE. If the world is already paused when a reason
    /// arrives (vanilla's Esc menu, the pause prompt), the reason is recorded but
    /// <see cref="Held"/> stays false and we will not resume the world on release — vanilla's pause
    /// is vanilla's to end. Symmetrically, if anything else resumes the world while we hold it
    /// (an Esc round-trip), <c>WorldManager.OnPaused(false)</c> drops our whole latch silently.
    /// We never fight vanilla for the state.</para>
    ///
    /// <para>UNWIND ORDER (callers, read this). Release the pause BEFORE releasing your modal input
    /// state. <c>SetGamePause(false)</c> calls <c>KeyManager.RemoveInputState("WorldManager")</c>,
    /// and <c>RemoveInputState</c> re-applies whatever sits LAST in a dictionary — do it after your
    /// own <c>RemoveInputState</c> and the state you pop back to is arbitrary.</para>
    ///
    /// <para>All game calls are try/caught and warn at most once: a pause is a comfort, never a
    /// reason to take a feature (or the frame) down with it.</para>
    /// </summary>
    internal static class GamePause
    {
        private static readonly HashSet<string> _reasons = new HashSet<string>();
        private static readonly Action<bool> _onWorldPaused = OnWorldPaused;  // stored: must be removable

        private static bool _wePaused;    // WE called SetGamePause(true) and nobody has taken it back
        private static bool _applying;    // re-entrancy guard: true while OUR SetGamePause call runs
        private static bool _hooked;      // WorldManager.OnPaused subscription live
        private static bool _warned;      // log-once, so a broken game API can never spam the frame
        private static Action<bool> _pausedChanged;

        /// <summary>True while we hold at least one reason AND the world is actually frozen by US.
        /// False when a hold is recorded but the pause belongs to vanilla or multiplayer forbids it.</summary>
        internal static bool Held
        {
            get { return _wePaused && _reasons.Count > 0; }
        }

        /// <summary>Relay of the vanilla <c>WorldManager.OnPaused</c> event (true = paused), fired for
        /// ANY pause change — ours or vanilla's — so a button can repaint from one place. Subscribing
        /// hooks the vanilla event lazily; <see cref="Unhook"/> (or <see cref="ReleaseAll"/>) drops it.
        /// Use a stored delegate, never a lambda you cannot unsubscribe: a static vanilla event holding
        /// a handler from a hot-reloaded assembly calls into dead code forever.</summary>
        internal static event Action<bool> PausedChanged
        {
            add { EnsureHooked(); _pausedChanged += value; }
            remove { _pausedChanged -= value; }
        }

        /// <summary>True when a mod pause would be legitimate: single-player, nobody connected, and no
        /// vanilla in-game menu already owning the pause. Mirrors vanilla's own gate
        /// (InventoryManager.cs:184-194 + Stationpedia.cs:93-99, V27758). False on ANY throw — the
        /// <c>InGameMenuOpen</c> getter dereferences a serialized prefab field unguarded, so it NREs
        /// before the singleton is wired up (same trap as <see cref="Guards.VanillaMenuWantsFront"/>).</summary>
        internal static bool CanOwnPause()
        {
            try
            {
                if (NetworkManager.IsClient) return false;
                if (NetworkBase.Clients.Count != 0) return false;
                var im = InventoryManager.Instance;
                if (im == null) return false;
                if (im.InGameMenuOpen) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Register (or re-affirm) a named reason to hold the world still. Idempotent per
        /// reason. The first reason freezes the world when <see cref="CanOwnPause"/> allows it and
        /// nothing else already has it paused.
        ///
        /// <para><paramref name="reassertTypingKey"/> is the caller's <c>KeyManager</c> input-state key
        /// and fixes the headline trap: <c>SetGamePause(true)</c> pushes
        /// <c>KeyInputState.Paused</c> (WorldManager.cs:1431), stomping the <c>Typing</c> state a modal
        /// just took — vanilla Esc would then open the in-game menu straight over our window. Pass the
        /// key and it is re-asserted the same frame; pass null if you own no input state.</para></summary>
        internal static void Hold(string reason, string reassertTypingKey = null)
        {
            if (string.IsNullOrEmpty(reason)) return;
            EnsureHooked();
            if (_wePaused) { _reasons.Add(reason); return; }   // already frozen by us: join in
            // Record the reason ONLY when the freeze was actually taken. A declined pause
            // (multiplayer, vanilla already owns it) must leave no reason behind: the stranded
            // entry would make every later Release refuse to resume while pausing nothing.
            if (!TryPause(reassertTypingKey)) return;
            _reasons.Add(reason);
        }

        /// <summary>Drop a named reason. The world resumes only when the LAST reason leaves and only if
        /// the pause is OURS — so one surface closing can never resume the world under another.
        ///
        /// <para>CALLERS: call this BEFORE releasing your modal input state (see the type remarks) —
        /// vanilla's un-pause pops the input-state dictionary and the order decides what you land in.</para></summary>
        internal static void Release(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;
            if (!_reasons.Remove(reason)) return;   // was not holding — no state change
            if (_reasons.Count > 0) return;         // someone else still wants stillness
            ResumeWorld();
        }

        /// <summary>Teardown / hot-reload: drop every reason, resume the world if the pause is ours, and
        /// unhook the vanilla event. A double-F6 while paused must never leave the game frozen with the
        /// owning assembly gone.</summary>
        internal static void ReleaseAll()
        {
            _reasons.Clear();
            ResumeWorld();           // also clears Guards.SelfPauseHeld, whichever branch it takes
            Unhook();
            _pausedChanged = null;   // relay subscribers die with the old assembly
            _applying = false;
            _warned = false;
        }

        /// <summary>Pumped every frame while in-game. Its only job is the world-transition guard:
        /// <c>Time.timeScale</c> is force-reset by a load/unload WITHOUT clearing
        /// <c>IsGamePaused</c>, so a latch must never survive one.
        ///
        /// <para>Two exits, deliberately different. World GONE (unloading, loading screen, batch mode):
        /// drop the latch silently and never call <c>SetGamePause</c> — the transition owns time now and
        /// touching a dying world is how you eat an NRE inside a manager teardown. World ALIVE but the
        /// mod went quiet (master switch off, vanilla UI hidden — both make
        /// <see cref="Guards.CanDraw"/> false): resume properly, or flipping the master switch while
        /// paused would strand the player in a frozen world.</para></summary>
        internal static void Tick()
        {
            if (_reasons.Count == 0 && !_wePaused) return;
            if (Guards.CanDraw()) return;
            if (WorldAlive())
            {
                _reasons.Clear();
                ResumeWorld();
                return;
            }
            _reasons.Clear();
            _wePaused = false;
            Guards.SelfPauseHeld = false;
        }

        /// <summary>Drop the vanilla <c>OnPaused</c> subscription. Idempotent; called by
        /// <see cref="ReleaseAll"/> and safe to call again from a plugin Shutdown.</summary>
        internal static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            try { WorldManager.OnPaused -= _onWorldPaused; }
            catch { }
        }

        // ---------- internals ----------

        private static void EnsureHooked()
        {
            if (_hooked) return;
            try
            {
                WorldManager.OnPaused += _onWorldPaused;
                _hooked = true;
            }
            catch (Exception e) { WarnOnce("could not hook WorldManager.OnPaused: " + e.Message); }
        }

        /// <summary>Runs INSIDE vanilla's event dispatch (WorldManager.cs:1448) — it must not throw, or
        /// it takes the rest of vanilla's pause handling with it.</summary>
        private static void OnWorldPaused(bool paused)
        {
            try
            {
                // Someone ELSE resumed the world while we held it (the classic case: the player opens
                // and closes the Esc menu over our window). Never fight vanilla for the state — drop
                // the whole latch silently and let the owning surfaces repaint from the relay below.
                if (!paused && !_applying && (_wePaused || _reasons.Count > 0))
                {
                    _reasons.Clear();
                    _wePaused = false;
                    Guards.SelfPauseHeld = false;
                }
                var h = _pausedChanged;
                if (h != null) h(paused);
            }
            catch (Exception e) { WarnOnce("OnPaused relay failed: " + e.Message); }
        }

        /// <summary>True only when WE actually took the freeze this call. Declines (multiplayer,
        /// vanilla already paused, API throw) return false so Hold records no reason for a pause
        /// that never happened - a reason with no freeze would jam every later Release.</summary>
        private static bool TryPause(string reassertTypingKey)
        {
            if (!CanOwnPause()) return false;                 // multiplayer / vanilla menu
            bool alreadyPaused = false;
            try { alreadyPaused = WorldManager.IsGamePaused; }
            catch (Exception e) { WarnOnce("IsGamePaused read failed: " + e.Message); return false; }
            if (alreadyPaused) return false;                  // vanilla's pause, vanilla's to end

            // Set the flags BEFORE the call: SetGamePause fires OnPaused synchronously and our relay
            // must hand subscribers the FINAL state, not the one they are about to leave.
            _wePaused = true;
            Guards.SelfPauseHeld = true;
            _applying = true;
            try
            {
                WorldManager.SetGamePause(true);
            }
            catch (Exception e)
            {
                _wePaused = false;
                Guards.SelfPauseHeld = false;
                WarnOnce("SetGamePause(true) failed: " + e.Message);
                return false;
            }
            finally { _applying = false; }

            // THE trap: the call above pushed KeyInputState.Paused over the caller's Typing state, so
            // vanilla Esc would open the in-game menu on top of our modal. Put ours back on top.
            if (!string.IsNullOrEmpty(reassertTypingKey))
            {
                try { KeyManager.SetInputState(reassertTypingKey, KeyInputState.Typing); }
                catch (Exception e) { WarnOnce("re-assert input state failed: " + e.Message); }
            }
            return true;
        }

        private static void ResumeWorld()
        {
            if (!_wePaused)
            {
                Guards.SelfPauseHeld = false;   // defensive: the flag is only ever ours to clear
                return;
            }
            _wePaused = false;
            Guards.SelfPauseHeld = false;
            _applying = true;
            try
            {
                // Never touch a dying world, whoever asked. A transition resets Time.timeScale itself
                // and SetGamePause dereferences five managers that may already be gone — and any
                // caller can land here mid-unload (UiaControlCenter.Close does exactly that when
                // Guards.CanDraw drops), so the guard belongs HERE, not only in Tick.
                if (!WorldAlive()) return;
                // A NETWORK-held pause (a joining client) outranks ours — vanilla gates its own
                // un-pause the same way (InventoryManager.cs:184-194, V27758).
                if (!NetworkBase.IsPaused && WorldManager.IsGamePaused)
                    WorldManager.SetGamePause(false);
            }
            catch (Exception e) { WarnOnce("SetGamePause(false) failed: " + e.Message); }
            finally { _applying = false; }
        }

        /// <summary>Is there still a world to un-pause? Fail-CLOSED (false = treat as dying), because
        /// the silent-drop path is the one that cannot hurt a world in transition.</summary>
        private static bool WorldAlive()
        {
            try
            {
                if (GameManager.IsBatchMode) return false;
                if (GameManager.GameState != GameState.Running) return false;
                if (ImGuiLoadingScreen.IsShowing) return false;
                return true;
            }
            catch { return false; }
        }

        private static void WarnOnce(string msg)
        {
            if (_warned) return;
            _warned = true;
            UIALog.Warn("GamePause: " + msg);
        }
    }
}
