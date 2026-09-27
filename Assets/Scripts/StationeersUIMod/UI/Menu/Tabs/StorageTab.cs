using System;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using StationeersUIMod.UI.Menu.Tabs.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>
    /// The SMARTSTOW tab — as of 0.9.8.0 this class is a THIN HOST (mockup 3,
    /// `Documentation/0.9.8.0/concepts/smartstow-organizer-3.png`): the SMART STOW MODE bar
    /// (Simple / Complex + the master switch), a manila-folder sub-tab strip, and one page per
    /// sub-tab, each in its own file under <c>Tabs/Storage/</c>:
    ///
    /// <list type="bullet">
    /// <item><b>Simple</b> mode: Return Home (<see cref="SimpleStowPage"/>) and Universal
    /// Inventory (<see cref="UniversalInventoryPage"/>).</item>
    /// <item><b>Complex</b> mode: Organizer (<see cref="OrganizerPage"/> — Storage Layouts |
    /// Bags | Bag Profiles + the rule-editing band + the share bar), Routing
    /// (<see cref="RoutingPage"/>), and Universal Inventory.</item>
    /// </list>
    ///
    /// <para>The class KEEPS its name and <see cref="IUiaTab"/> shape so UiaControlCenter's tab
    /// list keeps working, and <see cref="ResetCaches"/> stays the one teardown contract
    /// (UiaControlCenter.Shutdown calls it). Player-facing terminology on every page: "Storage
    /// Layout" for a Stow Profile, "Universal Inventory" — never "the Grid".</para>
    ///
    /// <para>The ONLY game-state mutation reachable anywhere under this tab is the
    /// labeller-funnel rename (<c>ItemActions.RenameThing</c>/<c>LabelWith</c>) — everything
    /// else writes config XML.</para>
    /// </summary>
    public sealed class StorageTab : IUiaTab
    {
        public string Title => "SmartStow";

        private static readonly string[] SimpleTabs = { "Return Home", "Universal Inventory" };
        private static readonly string[] ComplexTabs = { "Organizer", "Routing", "Universal Inventory" };

        // The shell's content area, kept so a SUB-TAB/nav switch can reset the SAME ScrollRect
        // UiaControlCenter.Refresh captures (its capture/restore takes the FIRST one under the
        // content area — an Organizer COLUMN's, when the Organizer is up). Refresh preserving
        // the fraction is right for an in-page gesture and wrong across a page change, so the
        // leaving gesture scrolls that first view back to the top (fix wave finding 5). STATIC
        // because the nav jumps are static; it points at a shell-owned rect and is dropped in
        // ResetCaches (the same teardown that runs on window destroy).
        private static RectTransform _content;

        /// <summary>Hot-reload/menu teardown: drop every restyle-scoped cache (they hold
        /// DynamicThing references and Sprites into the live world), the page state, and the
        /// drag-drop statics. StowRenameConfig's lazily-bound entry is dropped here too — this
        /// is the only mod-teardown path that reaches it.</summary>
        public static void ResetCaches()
        {
            StowShared.ResetCaches();
            StowRenameConfig.Reset();
            _content = null;
        }

        public void Build(RectTransform content, bool advanced)
        {
            _content = content;
            // One wrapper child carries the layout. `content` is the Control Center's REUSED
            // content area - only its CHILDREN are destroyed between builds - so a layout group
            // must never be added to it directly.
            var wrapGo = UiaUi.Go("smartstow", content);
            var wrap = (RectTransform)wrapGo.transform;
            UiaUi.Fill(wrap);
            UiaUi.VLayout(wrap, 6f);

            BuildModeBar(wrap);

            bool simple = StowModeConfig.Mode == StowMode.Simple;
            string[] titles = simple ? SimpleTabs : ComplexTabs;
            var st = StowShared.State;
            int selected = simple ? st.SubTabSimple : st.SubTabComplex;
            selected = Mathf.Clamp(selected, 0, titles.Length - 1);
            if (simple) st.SubTabSimple = selected; else st.SubTabComplex = selected;

            var tabs = UiaComposite.FolderTabs(wrap, titles, selected,
                i => SelectSubTab(simple, i), 176f, 36f);
            // The strip + card zone flexes to fill everything under the mode bar.
            UiaUi.Size(tabs.Zone.gameObject, flexH: 1f);

            // STATIC navigation entries for ALL FOUR sub-pages, whatever mode is up (fix wave
            // finding 7): only the active sub-page's own rows survive a rebuild, so a search
            // hit for another page lands on one of these instead — the jump switches the mode
            // when it must, then the sub-tab, then rebuilds.
            UiaSearch.RegisterRow(Title, "smartstow.nav", "Organizer page",
                () => NavTo(StowMode.Complex, 0));
            UiaSearch.RegisterRow(Title, "smartstow.nav", "Routing page",
                () => NavTo(StowMode.Complex, 1));
            UiaSearch.RegisterRow(Title, "smartstow.nav", "Universal Inventory page", NavToUniversal);
            UiaSearch.RegisterRow(Title, "smartstow.nav", "Return Home page",
                () => NavTo(StowMode.Simple, 0));

            if (simple)
            {
                if (selected == 0) BuildScrolled(tabs.Content, SimpleStowPage.Build);
                else BuildScrolled(tabs.Content, UniversalInventoryPage.Build);
            }
            else
            {
                if (selected == 0) OrganizerPage.Build(tabs.Content);   // manages its own scrolling
                else if (selected == 1) BuildScrolled(tabs.Content, RoutingPage.Build);
                else BuildScrolled(tabs.Content, UniversalInventoryPage.Build);
            }
        }

        // ---------------------------------------------------------------- mode bar

        /// <summary>The SMART STOW MODE bar: label, the Simple/Complex segmented switch, the
        /// two one-line explanations, and the master on/off switch (Part D #1 — you feel that
        /// switch in play, so it is not buried in Routing).</summary>
        private void BuildModeBar(Transform parent)
        {
            // 1:1 round (panels report): the mode row has NO panel of its own — the concept
            // shows the label and chips sitting directly on the frame interior fill. This also
            // keeps the polish-round seam contract (no chrome layer between the main FolderTabs
            // card and the sub-strip). The chips' own visuals are the kit SegmentedControl (K).
            var bar = UiaUi.Go("modebar", parent);
            UiaUi.VLayout((RectTransform)bar.transform, 2f, 10, 10, 6, 6);

            var row = UiaUi.Go("row", bar.transform);
            UiaUi.Size(row, 40f).minHeight = 40f;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            var label = UiaUi.Text(row.transform, "SMART STOW MODE", UiaTheme.SmallSize,
                StowSkin.HeaderText, TextAlignmentOptions.Left);
            label.characterSpacing = 5f;
            UiaUi.Size(label.gameObject, 40f, 158f, flexW: 0f);

            bool available = StowModeConfig.Available;
            bool simple = StowModeConfig.Mode == StowMode.Simple;
            UiaComposite.SegmentedControl(row.transform,
                new string[] { "Simple", "Complex" }, simple ? 0 : 1,
                i =>
                {
                    // Live flip, no restart: the resolver reads Mode per G press, and the tab
                    // simply rebuilds onto the other page set. Pinned inventory windows diff
                    // GridProfileMode.ChromeStamp directly and a bare mode flip would not move
                    // it, so bump the chrome version too (orchestrator integration note 3).
                    StowModeConfig.Mode = i == 0 ? StowMode.Simple : StowMode.Complex;
                    StowShared.BumpGridChrome();
                    UiaControlCenter.Refresh();
                }, 108f, 34f);
            UiaSearch.RegisterRow(Title, "smartstow.mode", "Smart Stow mode (Simple / Complex)",
                UiaSearch.MakeJump(row));
            // Comparison round: the two mode-description captions leave the flow — the (i)
            // beside the chips carries them (hide, never destroy) and the band closes up so
            // the sub-tabs tuck directly under the mode row.
            StowSkin.BareInfo(row.transform,
                "Simple: items return to the bag and slot you last placed them in. "
                + "Complex: you decide which bag holds what, with bag profiles and rules. "
                + "Switching modes loses nothing - both keep their data on disk.");

            var spacer = UiaUi.Go("spacer", row.transform);
            UiaUi.Size(spacer, UiaTheme.RowH, flexW: 1f);

            // The comparison round's section-label token: cyan caps, not neutral grey.
            var masterLabel = UiaUi.Text(row.transform, "Smart Stow (G)", UiaTheme.LabelSize,
                StowSkin.HeaderText, TextAlignmentOptions.Right);
            UiaUi.Size(masterLabel.gameObject, 40f, 120f, flexW: 0f);
            var master = UiaControls.Switch(row.transform, UIAConfig.SmartStowPlusEnabled.Value,
                v => UIAConfig.SmartStowPlusEnabled.Value = v);
            UiaSearch.RegisterRow(Title, "smartstow.mode", "Smart Stow on/off (master switch)",
                UiaSearch.MakeJump(master.gameObject));

            if (!available)
            {
                var warn = UiaControls.Note(bar.transform,
                    "The mode switch is unavailable right now (settings are not loaded yet) - Simple is assumed.");
                warn.color = UiaTheme.Warn;
            }
        }

        // ---------------------------------------------------------------- plumbing

        private static void SelectSubTab(bool simple, int idx)
        {
            var st = StowShared.State;
            int current = simple ? st.SubTabSimple : st.SubTabComplex;
            if (current == idx) return;
            if (simple) st.SubTabSimple = idx; else st.SubTabComplex = idx;
            LeavePage(st);
            UiaControlCenter.Refresh();
        }

        /// <summary>The search nav jump: switch the MODE when the target page needs the other
        /// one (sanctioned by finding 7), then the sub-tab, then rebuild. A mode change bumps
        /// the grid chrome exactly like the segmented switch does.</summary>
        private static void NavTo(StowMode mode, int idx)
        {
            var st = StowShared.State;
            if (StowModeConfig.Mode != mode)
            {
                StowModeConfig.Mode = mode;
                StowShared.BumpGridChrome();
            }
            if (mode == StowMode.Simple) st.SubTabSimple = idx; else st.SubTabComplex = idx;
            LeavePage(st);
            UiaControlCenter.Refresh();
        }

        /// <summary>Universal Inventory exists in BOTH modes — keep whichever is current and
        /// pick its index, never flip the mode just to show shared knobs.</summary>
        private static void NavToUniversal()
        {
            NavTo(StowModeConfig.Mode, StowModeConfig.Mode == StowMode.Simple ? 1 : 2);
        }

        /// <summary>Everything leaving a sub-page must do: close the inline rename rows AND
        /// their drafts (finding 6), drop the rule filter, and send the shell-captured scroll
        /// back to the top. Refresh preserves the FIRST ScrollRect's fraction under the content
        /// area — right for an in-page gesture, wrong across a page change, and when the
        /// Organizer is up that first view is one of its COLUMNS (finding 5) — so reset that
        /// same one here before rebuilding.</summary>
        private static void LeavePage(SmartStowState st)
        {
            StowShared.CloseRenameRows(st);
            st.RuleSearch = null;
            if (_content != null)
            {
                var sr = _content.GetComponentInChildren<ScrollRect>();
                if (sr != null) sr.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>Wrap a simple page in the standard kit scroll view. BORDERLESS-PAGE
        /// CONTRACT (manila seam): the scroll view is chromeless (a near-invisible mask
        /// target only) and the pages inside build kit Sections — no page-level border or
        /// glass skin may ever sit here, because the FolderTabs Card is the ONE visible
        /// bordered panel the selected tab merges into.</summary>
        private void BuildScrolled(Transform host, Action<Transform> page)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(host, out scroll, UiaTheme.Gap);
            page(col);
        }
    }
}
