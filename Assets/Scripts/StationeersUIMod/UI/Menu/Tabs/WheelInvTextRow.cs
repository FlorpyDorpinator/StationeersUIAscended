using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>
    /// GitHub #12: the ONE "Wheel &amp; inventory text size" row, built identically on both of its F10
    /// doors — Radial &gt; Size &amp; readout and Storage &gt; Universal Inventory — so the two can never
    /// drift. Writes <see cref="UIAConfig.WheelInvTextScale"/> only (a personal readability
    /// preference: deliberately NO <c>HudProfileStore.MarkThemeChanged</c>, it never travels with a
    /// theme). Live without any extra plumbing: the radial family reads it every frame, and the
    /// Universal Inventory / pinned windows relayout from <c>TheGridPanel</c>'s size-hash poll
    /// (which folds <see cref="WheelGridScale.Hash"/>). Registered with <see cref="UiaSearch"/>, so
    /// searching "text" (or "size", "wheel", "inventory") finds it on either tab.
    /// </summary>
    internal static class WheelInvTextRow
    {
        public const string Label = "Wheel & inventory text size";

        public static void Build(Transform parent, string tabTitle, string sectionId)
        {
            if (UIAConfig.WheelInvTextScale == null) return;   // config not bound yet (hot reload)
            var row = UiaControls.SliderRow(parent, Label, WheelGridScale.TextMin, WheelGridScale.TextMax,
                UIAConfig.WheelInvTextScale.Value,
                v => UIAConfig.WheelInvTextScale.Value = v, "0.00", 0.05f);

            float k = WheelGridScale.Resolution();
            string res = k > 1.001f
                ? "Your " + Screen.width + "x" + Screen.height + " screen already scales the wheels and "
                    + "inventory windows x" + k.ToString("0.00") + " (Scale with resolution, on by default); "
                    + "this multiplies on top. "
                : "";
            UiaControls.Note(parent, res + "Text in the radial wheels and in the Universal Inventory and "
                + "pinned storage windows. Text that has to fit a wedge, the hub or a cell shrinks to fit "
                + "- make the wheel, hub or cells bigger for more room. A personal setting: it stays the "
                + "same whichever UI theme you use. The same setting is on the Radial and Storage tabs.");

            var jump = UiaSearch.MakeJump(row.transform.parent.gameObject);
            UiaSearch.RegisterRow(tabTitle, sectionId, Label, jump);
            // An alias for the words a player with the GitHub #12 problem types ("font", "4K").
            UiaSearch.RegisterRow(tabTitle, sectionId, "Font size for 4K / big screens", jump);
        }
    }
}
