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
        /// windows, the console, the creative spawn menu). The mod HUD stays VISIBLE but drops its
        /// overlay sort order far below the menu while one is front (#7). Deliberate MOD menus
        /// (F9/F10 editors) are not included.</summary>
        public static bool VanillaMenuWantsFront()
        {
            // Escape / start / options menu (single-player: pauses). EXCEPT when the pause is ours —
            // see SelfPauseHeld: a mod pause has no vanilla menu behind it to give the front to.
            if (WorldManager.IsGamePaused && !SelfPauseHeld) return true;
            if (ConsoleWindow.IsOpen) return true;
            if (InputWindowBase.IsInputWindow) return true;   // IC/logic editor, naming windows, dialogs
            if (Stationpedia.IsOpenAndLocked) return true;
            if (ImguiCreativeSpawnMenu.Show) return true;
            // The ESC/pause menu on a MULTIPLAYER CLIENT does NOT pause the world (IsGamePaused stays
            // false), so the checks above miss it there. Detect the menu panel itself:
            // InventoryManager.InGameMenuOpen == GameMenuPanel.activeInHierarchy (verified
            // InventoryManager.cs:923/927, 27701). GameMenuPanel is a serialized prefab field that is
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

