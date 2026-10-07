using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.UI;
using Assets.Scripts.UI.ImGuiUi;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Centralized gate checks. Every draw and every input path must pass through these so
    /// the mod is inert on dedicated servers, in menus, during loading, and while typing.
    /// </summary>
    public static class Guards
    {
        public static Human LocalHuman => InventoryManager.ParentHuman;

        /// <summary>True while the world is paused by US (<see cref="GamePause"/> is the ONLY writer).
        /// A pause we hold must not read as "a vanilla menu wants the front": that check demotes the
        /// HUD's sort order, hides the Universal Inventory and closes open radials — i.e. our own
        /// pause would tear down the very UI it was taken to hold still.</summary>
        internal static bool SelfPauseHeld;

        /// <summary>True when it is safe and sensible to draw gameplay UI this frame.</summary>
        public static bool CanDraw()
        {
            if (!UIAConfig.MasterEnable.Value) return false;
            if (GameManager.IsBatchMode) return false;
            if (GameManager.GameState != GameState.Running) return false;
            if (ImGuiLoadingScreen.IsShowing) return false;
            if (InventoryManager.ParentHuman == null) return false;
            if (!InventoryManager.ShowUi) return false;
            return true;
        }

        /// <summary>True when gameplay hotkeys may act (nothing else owns input).</summary>
        public static bool CanAcceptGameplayInput()
        {
            if (!CanDraw()) return false;
            if (WorldManager.IsGamePaused) return false;
            if (ConsoleWindow.IsOpen) return false;
            if (InputWindowBase.IsInputWindow) return false;
            if (Stationpedia.IsOpenAndLocked) return false;
            if (ImguiCreativeSpawnMenu.Show) return false;
            if (KeyManager.InputState != KeyInputState.Game) return false;
            // Vanilla parity (InventoryManager.ManagerUpdate): no slot hotkeys while the
            // cursor is free (escape menu / Alt mouse-control) or the player is out cold.
            if (UnityEngine.Cursor.visible) return false;
            var parent = InventoryManager.Parent;
            if (parent == null || parent.IsUnresponsive) return false;
            return true;
        }

        /// <summary>Gate for OPENING the Universal Inventory (the Grid key, B). Same as
        /// <see cref="CanAcceptGameplayInput"/> EXCEPT it does NOT block on a freed cursor: pressing B
        /// must open the grid even while the mouse mod is active (FlorpyDorp) — the player may free the
        /// mouse first and then open the inventory to click through it. The genuine "keys belong
        /// elsewhere" states still block (paused, console, a text input, Stationpedia, the creative
        /// menu, a non-Game input state), and — because dropping the <c>Cursor.visible</c> check would
        /// otherwise let it through — an MP-client pause menu via <see cref="VanillaMenuWantsFront"/>.
        /// No character still blocks.</summary>
        public static bool CanOpenUniversalInventory()
        {
            if (!CanDraw()) return false;
            if (WorldManager.IsGamePaused) return false;
            if (ConsoleWindow.IsOpen) return false;
            if (InputWindowBase.IsInputWindow) return false;
            if (Stationpedia.IsOpenAndLocked) return false;
            if (ImguiCreativeSpawnMenu.Show) return false;
            if (KeyManager.InputState != KeyInputState.Game) return false;
            if (VanillaMenuWantsFront()) return false;   // the MP-client pause menu the dropped Cursor.visible check would miss
            var parent = InventoryManager.Parent;
            if (parent == null || parent.IsUnresponsive) return false;
            // Deliberately NOT gating on Cursor.visible — B opens the grid with the mouse freed too.
            return true;
        }

        /// <summary>Gate for the F9/F10 editor toggles: never over the console, a text
        /// input, or the creative spawn menu (vanilla also binds F9 to SpawnItem there).</summary>
        public static bool CanToggleMenus()
        {
            if (!CanDraw()) return false;
            if (ConsoleWindow.IsOpen) return false;
            if (InputWindowBase.IsInputWindow) return false;
            if (ImguiCreativeSpawnMenu.Show) return false;
            return true;
        }

        /// <summary>True when a VANILLA full-attention menu is up that must render ABOVE the mod's
        /// overlay (start/escape menu, Stationpedia, the IC/logic script editor & other input
        /// windows, the console, the creative spawn menu, and - GitHub #9 - every other screen-space
        /// window the game opens: the rocket UI, the trader, the comms terminal, plant genetics, the
        /// scoreboard, confirmation prompts...). The mod HUD stays VISIBLE but drops its overlay sort
        /// order far below the menu while one is front (#7). Deliberate MOD menus (F9/F10 editors)
        /// are not included. World-space screens (a computer's own display, tablets, the fabricator
        /// screen) are part of the 3D scene, which an overlay always draws over, so they are not -
        /// and cannot be - covered here.</summary>
        public static bool VanillaMenuWantsFront()
        {
            if (GameMenuOrInputFront()) return true;
            // #9: any other vanilla window that owns the pointer (the generic check), then the four
            // the player can open from the world by name, in case the generic one ever stops resolving.
            if (AnyVanillaWindowModal()) return true;
            return KnownVanillaWindowOpenSafe();
        }

        /// <summary>The pre-#9 set only: the pause / game menu, the console, a vanilla text-input
        /// window (IC10 editor, printer recipe picker, naming), locked Stationpedia, the creative
        /// menu. For callers that mean "the player is in a MENU" rather than "a window covers the
        /// screen": HudAlertPulse's freeze (the rocket UI / trader / comms windows do not pause the
        /// world, so the alarm must keep polling under them) and the tutorial's Esc-oops edge (which
        /// must not fire for a prompt or the scoreboard).</summary>
        public static bool GameMenuOrInputFront()
        {
            // Escape / start / options menu (single-player: pauses). EXCEPT when the pause is ours —
            // see SelfPauseHeld: a mod pause has no vanilla menu behind it to give the front to.
            if (WorldManager.IsGamePaused && !SelfPauseHeld) return true;
            if (ConsoleWindow.IsOpen) return true;
            if (InputWindowBase.IsInputWindow) return true;   // IC10 editor, printer recipe picker, text entry
            if (Stationpedia.IsOpenAndLocked) return true;
            if (ImguiCreativeSpawnMenu.Show) return true;
            // The ESC/pause menu on a MULTIPLAYER CLIENT does NOT pause the world (IsGamePaused stays
            // false), so the checks above miss it there. Detect the menu panel itself:
            // InventoryManager.InGameMenuOpen == GameMenuPanel.activeInHierarchy (verified
            // InventoryManager.cs:430, 27798). GameMenuPanel is a serialized prefab field that is
            // dereferenced unguarded inside the getter, so null-guard the singleton (not yet
            // instantiated during early load) and swallow a bare-field NRE.
            try
            {
                var im = InventoryManager.Instance;
                if (im != null && im.InGameMenuOpen) return true;
            }
            catch { }
            return false;
        }

        // ---- #9: vanilla's own "a window owns the pointer" list ----
        // MouseModeController._openModals (private static List<IModal>, V27798 MouseModeController.cs:13,
        // the same in 24790/27701/27758): every vanilla screen-space window adds itself on show and
        // removes itself on hide (AddModal/RemoveModal) so the game knows to unlock the cursor -
        // RocketCanvas, TraderCanvas, CommsTerminal, PanelPlantGenetics, InputSourceCode, InputPrefabs,
        // InputWindow, InGameMenu, ConfirmationPanel, ScoreBoard, PortalPanel, the console, Stationpedia.
        // The mod's own modals (radials, F10, Handbook, the tutorial card) also register there, so
        // only entries whose TYPE lives in the game assembly count - that also drops other mods' and
        // a hot-reloaded assembly's stale ones. CursorDiag.OpenModalCount reads the same field.
        // The cached list reference is safe across F6 (the game assembly never reloads); the resolve
        // flag is still reset in ResetForReload (static-reset rule).
        private static List<IModal> _openModals;
        private static bool _openModalsResolved;

        private static bool AnyVanillaWindowModal()
        {
            if (!_openModalsResolved)
            {
                _openModalsResolved = true;
                try
                {
                    var f = typeof(MouseModeController).GetField("_openModals", BindingFlags.NonPublic | BindingFlags.Static);
                    _openModals = f != null ? f.GetValue(null) as List<IModal> : null;
                }
                catch { _openModals = null; }
                if (_openModals == null)
                    UIALog.Warn("Guards: MouseModeController._openModals did not resolve; only the named vanilla windows will push the HUD back.");
            }
            var list = _openModals;
            if (list == null) return false;
            try
            {
                var gameAsm = typeof(MouseModeController).Assembly;
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m == null || m.GetType().Assembly != gameAsm) continue;
                    // A PINNED (unlocked) Stationpedia stays in the list while the player walks around
                    // with it open; it keeps its own IsOpenAndLocked rule above.
                    if (m is Stationpedia) continue;
                    var uo = m as UnityEngine.Object;
                    if (!ReferenceEquals(uo, null) && uo == null) continue;   // destroyed, never removed
                    if (m.UnlockCursor) return true;
                }
            }
            catch { }
            return false;
        }

        // Latched off after the first throw (a named type missing on another game branch): the guard
        // runs ~10 times a frame, so a broken lookup must not throw-and-catch every call.
        private static bool _namedWindowsBroken;

        private static bool KnownVanillaWindowOpenSafe()
        {
            if (_namedWindowsBroken) return false;
            try { return KnownVanillaWindowOpen(); }
            catch (System.Exception e) { NamedWindowsBroke(e); return false; }
        }

        private static void NamedWindowsBroke(System.Exception e)
        {
            _namedWindowsBroken = true;
            UIALog.Warn("Guards: the named vanilla window checks failed (" + e.GetType().Name + ": " + e.Message
                + "); relying on the generic modal check from now on.");
        }

        /// <summary>The vanilla windows a player opens from the world (#9), named explicitly. Its own
        /// method, never inlined, so a type missing on another game branch throws at the CALL - inside
        /// the caller's try - instead of breaking the whole guard.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool KnownVanillaWindowOpen()
        {
            var rocket = global::Objects.Rockets.UI.RocketCanvas.Instance;      // V27798 RocketCanvas.cs:12/14
            if (rocket != null && rocket.IsVisible) return true;
            var trader = global::TraderUI.TraderCanvas.Instance;                // TraderCanvas.cs:19/21
            if (trader != null && trader.IsVisible) return true;
            var comms = CommsTerminal.Instance;                                 // CommsTerminal.cs:15/19
            if (comms != null && comms.IsVisible) return true;
            var genetics = global::Assets.Scripts.UI.Genetics.PanelPlantGenetics.Instance;  // PanelPlantGenetics.cs:11/13
            if (genetics != null && genetics.IsVisible) return true;
            return false;
        }

        /// <summary>True while a vanilla window that parks the world raycast ITSELF is open: the rocket
        /// UI and plant genetics write CursorManager.BlockCursorRaycast on show and clear it on hide
        /// (V27798 RocketCanvas.cs:83/149, PanelPlantGenetics.cs:61/132). CursorBlockArbiter must not
        /// clear the flag under them when the mod's last hold is released.</summary>
        internal static bool VanillaRaycastWindowOpen()
        {
            if (_namedWindowsBroken) return false;
            try { return RaycastWindowOpen(); }
            catch (System.Exception e) { NamedWindowsBroke(e); return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool RaycastWindowOpen()
        {
            var rocket = global::Objects.Rockets.UI.RocketCanvas.Instance;
            if (rocket != null && rocket.IsVisible) return true;
            var genetics = global::Assets.Scripts.UI.Genetics.PanelPlantGenetics.Instance;
            return genetics != null && genetics.IsVisible;
        }

        /// <summary>Hot-reload teardown: forget the resolved modal list and the broken latch
        /// (static-reset rule).</summary>
        internal static void ResetForReload()
        {
            _openModals = null;
            _openModalsResolved = false;
            _namedWindowsBroken = false;
        }

        /// <summary>True while an OPEN radial may stay open (cursor is intentionally free).</summary>
        public static bool CanKeepRadialOpen()
        {
            return CanKeepRadialOpenWhy() == null;
        }

        /// <summary>The NAME of the first check that would close an open radial, or null if it may stay
        /// open. Exists so <c>RadialController</c> can log exactly WHICH gate tripped when a wheel closes
        /// with reason <c>cankeep-*</c> — the difference between "a vanilla input window opened",
        /// "the game paused" and "the player went unresponsive" is the whole fix.</summary>
        public static string CanKeepRadialOpenWhy()
        {
            if (!CanDraw()) return "!CanDraw";
            if (WorldManager.IsGamePaused) return "paused";
            if (ConsoleWindow.IsOpen) return "console";
            if (InputWindowBase.IsInputWindow) return "inputWindow";
            if (Stationpedia.IsOpenAndLocked) return "stationpedia";
            if (ImguiCreativeSpawnMenu.Show) return "creative";
            var parent = InventoryManager.Parent;
            if (parent == null) return "noParent";
            if (parent.IsUnresponsive) return "unresponsive";
            return null;
        }
    }
}

