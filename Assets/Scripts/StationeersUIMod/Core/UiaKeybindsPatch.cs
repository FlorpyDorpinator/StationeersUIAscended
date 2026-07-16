using HarmonyLib;

namespace StationeersUIMod.Core
{
    /// <summary>Registers the mod's single-key actions in the game's native Controls screen. Postfix
    /// on KeyManager.SetupKeyBindings — the one place vanilla builds its key registry, and the point
    /// that runs before the Controls panel is built and before settings reconciliation, so a modded
    /// KeyItem both shows up as a row and round-trips through the settings XML by name. Fail-soft:
    /// RegisterWithGame swallows any trouble and the mod's own rebinder keeps working regardless.</summary>
    [HarmonyPatch(typeof(KeyManager), "SetupKeyBindings")]
    internal static class Patch_KeyManager_SetupKeyBindings
    {
        private static void Postfix()
        {
            UiaKeybinds.RegisterWithGame();
        }
    }
}
