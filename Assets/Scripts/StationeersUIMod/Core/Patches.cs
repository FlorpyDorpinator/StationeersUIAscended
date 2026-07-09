using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.UI;
using HarmonyLib;
using UI.ImGuiUi.ImGuiWindows;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Fail-soft patch harness (SprayColor pattern): each patch class applies independently,
    /// so a game update that breaks one target degrades that feature instead of the mod.
    /// </summary>
    public static class PatchHarness
    {
        public static int Applied { get; private set; }
        public static int Failed { get; private set; }

        public static void TryPatchAll(Harmony harmony, params Type[] patchClasses)
        {
            foreach (var type in patchClasses)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    Applied++;
                    UIALog.Debug("Patched: " + type.Name);
                }
                catch (Exception e)
                {
                    Failed++;
                    UIALog.Error($"Patch {type.Name} FAILED (feature degraded): {e.Message}");
                }
            }
            UIALog.Info($"Harmony patches applied: {Applied}, failed: {Failed}.");
        }
    }

    /// <summary>
    /// Single per-frame ImGui hook: runs inside the gameplay branch of the game's own
    /// ImGui frame (after ImGuiWindowManager windows, before the frame is rendered), so we
    /// never touch frame begin/end ourselves and we never draw during splash/loading.
    /// </summary>
    [HarmonyPatch(typeof(ImGuiWindowManager), nameof(ImGuiWindowManager.Draw))]
    internal static class Patch_ImGuiWindowManager_Draw
    {
        private static void Postfix()
        {
            try
            {
                StationeersUIMod.Instance?.DrawOverlay();
            }
            catch (Exception e)
            {
                StationeersUIMod.Instance?.ReportDrawException(e);
            }
        }
    }

    /// <summary>
    /// Suppresses the vanilla polled handling of keys we own: the active-hand key (R) when
    /// the tool radial has it, and the 1-6 equipment keys when equipment-key radials are on.
    /// Taps/holds are re-dispatched by the owning feature so nothing is lost.
    /// </summary>
    [HarmonyPatch(typeof(InventoryManager), "CheckDisplaySlot", typeof(SlotDisplay), typeof(string))]
    internal static class Patch_InventoryManager_CheckDisplaySlot
    {
        private static bool Prefix(SlotDisplay displaySlot, string buttonName, ref bool __result)
        {
            var plugin = StationeersUIMod.Instance;
            if (plugin == null) return true;
            if (buttonName == "ActiveHandSlot" && plugin.ToolRadialOwnsVanillaKey)
            {
                __result = false;
                return false;
            }
            if (plugin.EquipmentKeysOwnButton(buttonName))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Suppresses the vanilla Tab scoreboard toggle while the bag radial owns Tab; taps are
    /// re-dispatched by BagRadialFeature.OnTap.
    /// </summary>
    [HarmonyPatch(typeof(KeyManager), "ToggleScoreboard")]
    internal static class Patch_KeyManager_ToggleScoreboard
    {
        private static bool Prefix()
        {
            var plugin = StationeersUIMod.Instance;
            if (plugin != null && plugin.BagRadialOwnsVanillaKey)
                return false;
            return true;
        }
    }
}

