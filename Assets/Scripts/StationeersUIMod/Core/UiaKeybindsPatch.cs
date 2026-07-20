using System;
using HarmonyLib;

namespace StationeersUIMod.Core
{
    /// <summary>Registers UIA_Menu in the game's native Controls screen. Postfix on
    /// KeyManager.SetupKeyBindings — the one place vanilla builds its key registry, and the point
    /// that runs before the Controls panel is built and before settings reconciliation, so a modded
    /// KeyItem both shows up as a row and round-trips through the settings XML by name.
    ///
    /// The try/catch is NOT redundant with RegisterWithGame's own. This postfix sits on the BOOT
    /// path (WorldManager.ManagerAwake -> Settings.LoadSettings -> SetupKeyBindings), and
    /// RegisterWithGame holds direct references to game statics (KeyManager.GetControlsGroup,
    /// new ControlsGroup, ControlsAssignment.ControlsItems). If a future game update renames or
    /// reshapes one of those, the JIT raises MissingFieldException/TypeLoadException on METHOD
    /// ENTRY — before that method's own try block is live — and it would escape into boot. Catching
    /// at the call site keeps a stale mod to one dead feature instead of an unbootable game
    /// (philosophy #5).</summary>
    [HarmonyPatch(typeof(KeyManager), "SetupKeyBindings")]
    internal static class Patch_KeyManager_SetupKeyBindings
    {
        private static void Postfix()
        {
            try { UiaKeybinds.RegisterWithGame(); }
            catch (Exception e) { UIALog.Warn("UiaKeybinds registration skipped: " + e.Message); }
        }
    }
}
