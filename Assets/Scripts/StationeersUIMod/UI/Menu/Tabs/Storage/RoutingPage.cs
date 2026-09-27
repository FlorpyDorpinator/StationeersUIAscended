using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// The ROUTING sub-tab (Complex only): the expert tuning of the Smart Stow chain — the old
    /// Settings page minus what moved out (the master on/off switch lives in the mode bar, the
    /// test box in the Organizer's Bags column, the Universal Inventory knobs on their own
    /// page). The four advanced toggles sit inside a "More options" disclosure instead of the
    /// retired global Simple/Advanced density flag (ANSWERS #15's direction, the HudTab pilot).
    /// Config-only writes; nothing here touches game state.
    /// </summary>
    internal static class RoutingPage
    {
        private const string TabTitle = "SmartStow";

        public static void Build(Transform col)
        {
            // Borderless-page contract (manila seam): no page-level panel/border here — the
            // FolderTabs Card is the one bordered frame; this page is Notes + kit Sections.
            UiaControls.Note(col, "These switches tune the Complex chain. Simple mode uses its own fixed order.");

            var sec = UiaComposite.Section(col, "smartstow.routing", "Smart Stow chain",
                "The numbered steps run in this order on every G press. Switching one off skips "
                + "that step for every bag; the Organizer decides WHERE things go, these decide HOW "
                + "the router looks.", true);

            var t1 = UiaControls.ToggleRow(sec.Body, "1 - Prefer topping up matching stacks",
                UIAConfig.StowPreferStacks.Value, v => UIAConfig.StowPreferStacks.Value = v);
            var t2 = UiaControls.ToggleRow(sec.Body, "2 - Route components to their sockets",
                UIAConfig.StowSocketPriority.Value, v => UIAConfig.StowSocketPriority.Value = v);
            var t3 = UiaControls.ToggleRow(sec.Body, "3 - Use bag profiles",
                UIAConfig.StowUseProfiles.Value, v => UIAConfig.StowUseProfiles.Value = v);
            var t4 = UiaControls.ToggleRow(sec.Body, "4 - Route to bags with similar contents",
                UIAConfig.StowUseAffinity.Value, v => UIAConfig.StowUseAffinity.Value = v);
            var t5 = UiaControls.ToggleRow(sec.Body, "5 - Known bag types get a default",
                UIAConfig.StowUseBagTypeDefaults.Value, v => UIAConfig.StowUseBagTypeDefaults.Value = v);
            var t6 = UiaControls.ToggleRow(sec.Body, "6 - Remember where each type went",
                UIAConfig.StowUseTypeMemory.Value, v => UIAConfig.StowUseTypeMemory.Value = v);
            var t7 = UiaControls.ToggleRow(sec.Body, "7 - Loose build items go to one bag",
                UIAConfig.StowGenericFallback.Value, v => UIAConfig.StowGenericFallback.Value = v);
            var depth = UiaControls.SliderRow(sec.Body, "How deep to search (levels)", 1f, 5f,
                UIAConfig.ScanDepth.Value, v => UIAConfig.ScanDepth.Value = Mathf.RoundToInt(v), "0", 1f);

            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Prefer topping up matching stacks",
                UiaSearch.MakeJump(t1.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Route components to their sockets",
                UiaSearch.MakeJump(t2.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Use bag profiles",
                UiaSearch.MakeJump(t3.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Route to bags with similar contents",
                UiaSearch.MakeJump(t4.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Known bag types get a default",
                UiaSearch.MakeJump(t5.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Remember where each type went",
                UiaSearch.MakeJump(t6.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Loose build items go to one bag",
                UiaSearch.MakeJump(t7.transform.parent.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "How deep to search",
                UiaSearch.MakeJump(depth.transform.parent.gameObject));

            if (sec.Extras != null)
            {
                var secRef = sec;
                System.Action reveal = () => secRef.SetExpanded(true);
                var a1 = UiaControls.ToggleRow(sec.Extras, "Stow tools onto the toolbelt first",
                    UIAConfig.StowToolsToToolbeltFirst.Value, v => UIAConfig.StowToolsToToolbeltFirst.Value = v);
                var a2 = UiaControls.ToggleRow(sec.Extras, "Allow nested-bag stow",
                    UIAConfig.StowIntoNestedBags.Value, v => UIAConfig.StowIntoNestedBags.Value = v);
                var a3 = UiaControls.ToggleRow(sec.Extras, "Sort lists profile items first",
                    UIAConfig.StowProfileSort.Value, v => UIAConfig.StowProfileSort.Value = v);
                var a4 = UiaControls.ToggleRow(sec.Extras, "Show where items were stowed (and why)",
                    UIAConfig.StowToastEnabled.Value, v => UIAConfig.StowToastEnabled.Value = v);
                UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Stow tools onto the toolbelt first",
                    UiaSearch.MakeJump(a1.transform.parent.gameObject, reveal));
                UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Allow nested-bag stow",
                    UiaSearch.MakeJump(a2.transform.parent.gameObject, reveal));
                UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Sort lists profile items first",
                    UiaSearch.MakeJump(a3.transform.parent.gameObject, reveal));
                UiaSearch.RegisterRow(TabTitle, "smartstow.routing", "Show where items were stowed",
                    UiaSearch.MakeJump(a4.transform.parent.gameObject, reveal));
            }

            int excluded = 0;
            try { excluded = BagProfileStore.ExcludedCount; } catch { }
            if (excluded > 0)
                StowShared.SubNote(col, excluded
                    + " container(s) in this save are set to \"never stow into this\" (the Bags column on the Organizer).");
        }
    }
}
