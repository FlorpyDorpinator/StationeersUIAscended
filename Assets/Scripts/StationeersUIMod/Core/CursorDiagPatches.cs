using Assets.Scripts;   // CursorManager, MouseModeController
using HarmonyLib;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Postfix on the single cursor funnel <c>CursorManager.SetCursor(bool)</c> (decompile 27701,
    /// CursorManager.cs:506 — the ONLY place <c>Cursor.visible</c> + <c>lockState</c> are written
    /// together). When the <c>uiadiag</c> trace is on, logs every call with the resulting state and a
    /// compact caller chain, so a cursor lock/unlock is always attributable. Pure diagnostic.
    /// </summary>
    [HarmonyPatch(typeof(CursorManager), "SetCursor")]
    internal static class Patch_CursorManager_SetCursor
    {
        private static void Postfix(bool isLocked) => CursorDiag.NoteSetCursor(isLocked);
    }

    /// <summary>Diagnostic only: NAME every cursor modal as it registers/unregisters, so a
    /// <c>uiadiag</c> trace shows exactly which modal drives a cursor lock/unlock. Param typed as
    /// <c>object</c> so we never depend on the game's IModal namespace.</summary>
    [HarmonyPatch(typeof(MouseModeController), "AddModal")]
    internal static class Patch_MouseModeController_AddModal
    {
        private static void Postfix(object modal)
            => CursorDiag.NoteModal("ADD", modal != null ? modal.GetType().Name : "null");
    }

    [HarmonyPatch(typeof(MouseModeController), "RemoveModal")]
    internal static class Patch_MouseModeController_RemoveModal
    {
        private static void Postfix(object modal)
            => CursorDiag.NoteModal("REMOVE", modal != null ? modal.GetType().Name : "null");
    }
}
