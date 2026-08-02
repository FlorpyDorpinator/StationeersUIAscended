using Assets.Scripts.UI;   // InventoryWindowManager
using HarmonyLib;
using StationeersUIMod.UI.Grid;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Stand vanilla's inventory-cursor navigation DOWN while the Universal Inventory owns the wheel.
    ///
    /// <para>WHY. The Universal Inventory's scroll-select (<see cref="GridSelection"/>) now runs in the
    /// CAPTURED regime — the mouse wheel cycles a highlight through the grid during normal play, exactly
    /// like vanilla's own inventory scroll. But vanilla's own cursor nav runs in that SAME regime (it
    /// only stands down when <c>InputMouse.IsMouseControl</c>, i.e. when the mouse is FREED). So without
    /// this, one wheel notch would move BOTH cursors and one F would act twice. These prefixes suppress
    /// vanilla's nav in exactly the regime the grid owns it (<see cref="GridSelection.OwnsVanillaInventoryNav"/>).</para>
    ///
    /// <para>SEAM (verified against the 27701 decompile). <c>InventoryWindowManager.NextButton</c>
    /// (:131) / <c>PreviousButton</c> (:142) are the SINGLE choke point for all vanilla inventory-cursor
    /// movement — both the mouse wheel (<c>InventoryManager.CheckDisplaySlotInput</c> :1417/1421) and the
    /// NextItem/PreviousItem keys (:1406/1410) route through them — and they leave the 1–6 equipment
    /// hotkeys untouched (those go through <c>CheckDisplaySlot→MoveEquipmentSlot</c>, never these). We do
    /// NOT prefix <c>CheckDisplaySlotInput</c> itself, which would also kill the 1–6 keys and the
    /// ConstructionPanel scroll. <c>InventorySelect</c> (:455) is vanilla's F on the highlighted slot;
    /// with our own F acting on the grid cursor it must be suppressed too, or F double-acts.</para>
    ///
    /// <para>G is deliberately NOT patched. Vanilla's <c>InventoryWindowManager.SmartStow()</c> (:517) is
    /// the mod's own SmartStow+ path (its Harmony prefix on the separate <c>InventoryManager.SmartStow</c>
    /// runs downstream of it), so suppressing it would break G entirely. G therefore stays owned by
    /// SmartStow+, the vanilla-accurate "smart stow the held item" — and <see cref="GridSelection"/>
    /// pointedly does not handle G, so there is no double-fire.</para>
    ///
    /// <para>MP-SAFETY. Input-routing only: these prefixes suppress vanilla's own cursor move / select in
    /// a regime the mod replaces — they add NO mutation. The mod's F still funnels through
    /// <c>ItemActions</c>, re-verified at execute time. Fail-open on any exception (let vanilla run), so a
    /// game update that renames a target degrades to "vanilla nav also runs" rather than a dead wheel.</para>
    /// </summary>
    [HarmonyPatch(typeof(InventoryWindowManager), "NextButton")]
    internal static class Patch_IWM_NextButton
    {
        private static bool Prefix()
        {
            try { return !GridSelection.OwnsVanillaInventoryNav; } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(InventoryWindowManager), "PreviousButton")]
    internal static class Patch_IWM_PreviousButton
    {
        private static bool Prefix()
        {
            try { return !GridSelection.OwnsVanillaInventoryNav; } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(InventoryWindowManager), "InventorySelect")]
    internal static class Patch_IWM_InventorySelect
    {
        private static bool Prefix()
        {
            try { return !GridSelection.OwnsVanillaInventoryNav; } catch { return true; }
        }
    }
}
