using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// The UNIVERSAL INVENTORY sub-tab (both modes): the old Settings page's Universal
    /// Inventory block, verbatim — cell size, scroll-select, profile tags, drag glow. Nothing
    /// here is ever called "the Grid" (FlorpyDorp's standing directive); the CODE keeps its
    /// Grid names. Config-only writes.
    /// </summary>
    internal static class UniversalInventoryPage
    {
        private const string TabTitle = "SmartStow";

        public static void Build(Transform col)
        {
            // Borderless-page contract (manila seam): no page-level panel/border here — the
            // FolderTabs Card is the one bordered frame; this page is a single kit Section.
            var sec = UiaComposite.Section(col, "smartstow.ui", "Universal Inventory",
                "The one window that shows your whole inventory. These knobs are per-machine "
                + "comfort settings - they do not travel with a UI theme.");

            var cell = UiaControls.SliderRow(sec.Body, "Cell size", 28f, 80f, UIAConfig.GridCellSize.Value,
                // global:: because the plugin CLASS shares the root namespace's name.
                v =>
                {
                    UIAConfig.GridCellSize.Value = v;
                    global::StationeersUIMod.UI.Grid.TheGridPanel.Relayout();
                }, "0", 1f);
            UiaSearch.RegisterRow(TabTitle, "smartstow.ui", "Universal Inventory cell size",
                UiaSearch.MakeJump(cell.transform.parent.gameObject));

            // GridSelection reads UIAConfig.GridKeyboardNav live every interactive frame, so the
            // flip takes effect without a rebuild.
            var nav = UiaControls.ToggleRow(sec.Body, "Scroll to select (mouse wheel through your inventory)",
                UIAConfig.GridKeyboardNav.Value, v => UIAConfig.GridKeyboardNav.Value = v);
            UiaControls.Note(sec.Body, "While the inventory is open, the mouse wheel moves a highlight through your "
                + "bags and slots - F takes the highlighted item or places a held item into an empty cell. "
                + "Free the mouse and the wheel pans the window instead.");
            UiaSearch.RegisterRow(TabTitle, "smartstow.ui", "Scroll to select",
                UiaSearch.MakeJump(nav.transform.parent.gameObject));

            // Live toggles: badges refresh via GridProfileMode.ChromeStamp (folds the config
            // bit), hints are re-read by GridGhostHint.Tick - no extra plumbing needed.
            var badges = UiaControls.ToggleRow(sec.Body, "Show profile tags on bag tabs",
                UIAConfig.GridProfileBadges.Value, v => UIAConfig.GridProfileBadges.Value = v);
            UiaSearch.RegisterRow(TabTitle, "smartstow.ui", "Show profile tags on bag tabs",
                UiaSearch.MakeJump(badges.transform.parent.gameObject));

            var glow = UiaControls.ToggleRow(sec.Body, "Glow the bag Smart Stow would pick while dragging",
                UIAConfig.GridGhostHints.Value, v => UIAConfig.GridGhostHints.Value = v);
            UiaSearch.RegisterRow(TabTitle, "smartstow.ui", "Glow the bag Smart Stow would pick",
                UiaSearch.MakeJump(glow.transform.parent.gameObject));
        }
    }
}
