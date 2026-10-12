using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>
    /// GitHub #12: the ONE "Wheel &amp; inventory text size" row — plus, since 2026-10-11, the opt-in
    /// "Scale wheels & inventory with resolution" toggle right above it — built identically on both
    /// F10 doors (Radial &gt; Size &amp; readout and Storage &gt; Universal Inventory) so the two can
    /// never drift. Writes <see cref="UIAConfig.WheelInvTextScale"/> and
    /// <see cref="UIAConfig.WheelInvScaleWithResolution"/> only (personal preferences: deliberately
    /// NO <c>HudProfileStore.MarkThemeChanged</c>, they never travel with a theme). Live without any
    /// extra plumbing: the radial family reads both every frame, and the Universal Inventory / pinned
    /// windows relayout from <c>TheGridPanel</c>'s size-hash poll (which folds
    /// <see cref="WheelGridScale.Hash"/>). Both rows are registered with <see cref="UiaSearch"/>.
    /// </summary>
    internal static class WheelInvTextRow
    {
        public const string Label = "Wheel & inventory text size";
        public const string ResLabel = "Scale wheels & inventory with resolution";

        public static void Build(Transform parent, string tabTitle, string sectionId)
        {
            if (UIAConfig.WheelInvScaleWithResolution != null)
            {
                var res = UiaControls.ToggleRow(parent, ResLabel, UIAConfig.WheelInvScaleWithResolution.Value,
                    v => UIAConfig.WheelInvScaleWithResolution.Value = v);
                UiaControls.Note(parent, "On by default. The wheels and inventory windows are designed at "
                    + "2560x1440; on any other screen this keeps them the same size relative to your screen, "
                    + "growing on bigger screens (x1.5 at 3840x2160) and shrinking on smaller ones (x0.75 at "
                    + "1920x1080). Nothing changes at 2560x1440. Off: they keep the same pixel size on every "
                    + "screen - use Wheel size / Cell size to resize them. A personal setting: it stays the "
                    + "same whichever UI theme you use.");
                var resJump = UiaSearch.MakeJump(res.transform.parent.gameObject);
                UiaSearch.RegisterRow(tabTitle, sectionId, ResLabel, resJump);
                UiaSearch.RegisterRow(tabTitle, sectionId, "Wheel and inventory size for 4K / big screens", resJump);
            }

            if (UIAConfig.WheelInvTextScale == null) return;   // config not bound yet (hot reload)
            var row = UiaControls.SliderRow(parent, Label, WheelGridScale.TextMin, WheelGridScale.TextMax,
                UIAConfig.WheelInvTextScale.Value,
                v => UIAConfig.WheelInvTextScale.Value = v, "0.00", 0.05f);

            UiaControls.Note(parent, "Text in the radial wheels and in the Universal Inventory and "
                + "pinned storage windows. Text that has to fit a wedge, the hub or a cell shrinks to fit "
                + "- make the wheel, hub or cells bigger for more room. A personal setting: it stays the "
                + "same whichever UI theme you use. The same settings are on the Radial and Storage tabs.");

            var jump = UiaSearch.MakeJump(row.transform.parent.gameObject);
            UiaSearch.RegisterRow(tabTitle, sectionId, Label, jump);
            // An alias for the words a player with the GitHub #12 problem types ("font", "4K").
            UiaSearch.RegisterRow(tabTitle, sectionId, "Font size for 4K / big screens", jump);
        }
    }
}
