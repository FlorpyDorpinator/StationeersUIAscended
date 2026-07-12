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

    /// <summary>
    /// Vanilla NRE guard: Human.SpawnDynamicThing (Human.cs:4430 in 27701) checks
    /// GameMode == Creative but NOT whether a spawnable is selected — with
    /// InventoryManager.SpawnPrefab null it dereferences spawnPrefab.SpawnId and throws
    /// on every F9 press. Vanilla would only ever crash in this state, so skipping is
    /// strictly safe, creative or not.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Scripts.Objects.Entities.Human), "SpawnDynamicThing")]
    internal static class Patch_Human_SpawnDynamicThing
    {
        private static bool Prefix()
        {
            return InventoryManager.SpawnPrefab != null;
        }
    }

    /// <summary>
    /// Suppresses the vanilla creative SpawnItem key while the F9 HUD editor owns the
    /// same key (KeyMap.SpawnItem is F9 by default, KeyManager.cs:427): one press must
    /// toggle the editor OR spawn an item, never both. Rebinding HudEditorKey away from
    /// the collision restores vanilla spawning untouched.
    /// </summary>
    [HarmonyPatch(typeof(KeyManager), "SpawnDynamicThing")]
    internal static class Patch_KeyManager_SpawnDynamicThing
    {
        private static bool Prefix()
        {
            try
            {
                if (StationeersUIMod.Instance == null) return true;
                if (UIAConfig.MasterEnable == null || !UIAConfig.MasterEnable.Value) return true;
                var editorKey = UI.Hud.HudConfig.HudEditorKey;
                if (editorKey == null || editorKey.Value != KeyMap.SpawnItem) return true;
                // Same context gate the editor toggle uses: if the press could reach our
                // editor, vanilla stays quiet (even when a sibling menu blocks the toggle —
                // better a dead key than a surprise spawn).
                return !Guards.CanToggleMenus();
            }
            catch
            {
                return true; // never let the guard itself take vanilla down
            }
        }
    }
}

