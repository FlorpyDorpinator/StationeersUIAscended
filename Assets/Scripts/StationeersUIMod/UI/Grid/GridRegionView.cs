using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// Renders ONE <see cref="ContainerNode"/> in The Grid's <see cref="GridDisplayMode.Grid"/> mode:
    /// a thin-border <see cref="PanelGraphic"/> region rectangle enclosing (a) a wrapped, tightly
    /// packed grid of <see cref="BagGridCell"/> for the node's own slots (REUSED verbatim — same
    /// cell, click-to-hand, state text, active-hand accent) and (b) inset nested
    /// <see cref="GridRegionView"/>s for the node's nested STORAGE containers, with a
    /// <see cref="GridTab"/> (the trapezoidal manila label) sitting above/on the region's top border,
    /// plus — for containers that pass vanilla's sortable gate — a small ASCII "SORT" button in the tab
    /// band just to the RIGHT of the tab. The cell grid wraps to a configured column count
    /// (<see cref="UIAConfig.GridCellCols"/>, default 5, so a 15-slot bag reads as 3 rows of 5) capped
    /// by how many cells the live width holds, and nested bag regions pack into up to
    /// <see cref="UIAConfig.GridMaxBagCols"/> masonry columns (default 2; more appear as the window is
    /// dragged wider, one when it is narrow). Cell size is the live F10
    /// <see cref="UIAConfig.GridCellSize"/>, so the grid re-wraps responsively on a resize/slider change.
    /// The whole-inventory ROOT node renders NO own cells (no loose hand/helmet "top line") — only its
    /// child container regions. A container with vanilla interactions (on/off, valve, lock, Unpack,
    /// a stack's Split One / Split Half, plus the "split N" square on host/SP) gets them as compact
    /// cell-sized SQUARE buttons wrapped under its cells (D-005), never full-width bars.
    /// Tapping the tab toggles collapse via <see cref="GridCollapseStore"/>; a collapsed region shows
    /// just its tab. The tree of region views mirrors the tree of <see cref="ContainerNode"/>s — but
    /// in Grid mode a node's <see cref="ContainerNode.Children"/> are ONLY its nested storage
    /// containers (tools stay leaf cells in <see cref="ContainerNode.Slots"/>), so this view never
    /// expands a tool into a sub-region.
    ///
    /// <para>Pooling is INSTANCE-LOCAL: each view keeps its cells and child region-views as its own
    /// children, growing to a high-water mark and deactivating the surplus — there is NO static pool,
    /// so nothing is stranded across an F6 reload (the whole tree lives under <c>TheGridPanel</c>'s
    /// canvas, which its <c>Shutdown</c> destroys). This view holds no static state and subscribes to
    /// no events; the panel drives structure (<see cref="Bind"/>) and geometry (<see cref="Layout"/>)
    /// on structural change and content (<see cref="RefreshIfDirty"/>) each Tick.</para>
    ///
    /// <para>MP-safety: the mutations reachable from here are (a) the REUSED children's click-to-hand /
    /// drag-drop in <see cref="BagGridCell"/> and (b) Sort, which goes through
    /// <see cref="ItemActions.SortContainer"/> — one server-authoritative message with the occupant
    /// re-verified at execute time. Everything else this view does is a networked-only read plus the
    /// local, per-save collapse flag.</para>
    ///
    /// <para>Center-pivot rule: every rect that carries a <see cref="PanelGraphic"/> /
    /// <see cref="PolygonPanelGraphic"/> (the region box, each cell, the tab) keeps a CENTRE pivot
    /// (0.5,0.5) because those graphics draw their mesh centred on the rect origin; <see cref="Layout"/>
    /// positions each by its CENTRE (a top-left pivot would offset the box half its size).</para>
    /// </summary>
    public sealed class GridRegionView : MonoBehaviour
    {
        // Layout constants (px, at the canvas' reference resolution).
        private const float CellSizeFallback = 46f; // used only if the config entry is not bound yet
        private const float CellGap = 3f;        // tighter than Nested mode — the packed "one grid" read
        private const float Border = 2f;         // region edge -> content inset (border line + breathing room)
        private const float InnerPad = 5f;       // extra inner padding inside the region border
        private const float TabLeft = 6f;        // tab's left inset from the region's left corner
        private const float NestedIndent = 10f;  // nested storage regions inset a touch from the parent grid
        private const float SectionGap = 6f;     // gap between the cell grid and nested regions (and between regions)
        private const float MinRegionW = 60f;
        private const float SortW = 38f;         // per-region Sort control (in the tab band, right of the tab)
        private const float SortH = 16f;
        private const float SortTabGap = 6f;     // gap between the manila tab and the Sort control in the tab band

        // Profile-mode strip (design O4a): the chip + CAPTURE band seated INSIDE the box's top
        // edge (the same band the Sort control lives in). Inside the box — deliberately NOT on
        // the manila tab band — so it renders identically inside a PinnedInventoryWindow, whose
        // suppressed tab band is shifted out of view.
        private const float StripH = 16f;        // chip/CAPTURE control height (matches SortH)
        private const float StripGap = 4f;       // gap between the strip band and the first cell row
        private const float ChipMinW = 44f;
        private const float ChipMaxW = 200f;
        private const float ChipPad = 8f;        // horizontal text padding inside the chip
        private const float CapBtnW = 56f;       // the CAPTURE button

        // Device control buttons (on/off, lock/arm, valve, mode, split) — vanilla's InventoryWindow
        // interaction row, in our theme. D-005 (FlorpyDorp: "The buttons to control stuff should be
        // smaller squares just like the inventory icons"): each control is a compact SQUARE the size of
        // an inventory cell, wrapped into rows exactly like the cells (same column rule), under the cells
        // and nested regions — never a full-width bar. A button-only device (no cells) shows ONLY these.
        // Labels poll at CtrlPollInterval so "On" -> "Off" follows the device.
        private const float CtrlPollInterval = 0.25f;

        // D-005: the third split button's hover-tooltip body — the radial's "Split count" gesture
        // (scroll to choose, click to split), in words. ASCII only.
        private const string SplitCountTip =
            "Scroll over this button to choose how many to split off, then click to split them off.";

        /// <summary>The region box's fill is a faint TINT of the inherited panel fill so stacked
        /// regions group their cells without over-darkening. A relative scale on the theme's alpha —
        /// not a hardcoded colour: a translucent global fill stays translucent, an opaque one still
        /// reads as a lighter slab than the window shell behind it.</summary>
        private const float RegionFillAlpha = 0.4f;

        /// <summary>The live cell edge length — the F10 <see cref="UIAConfig.GridCellSize"/> slider
        /// (28..80), falling back to the shipped 46 px if the config is not bound yet (hot reload before
        /// <c>UIAConfig.Init</c>). Read once per Bind/Layout into a local; never per cell.</summary>
        private static float CellPx()
        {
            try { if (UIAConfig.GridCellSize != null) return UIAConfig.GridCellSize.Value; }
            catch { }
            return CellSizeFallback;
        }

        /// <summary>The configured cells-per-row cap (<see cref="UIAConfig.GridCellCols"/>, 1..10,
        /// default 5), falling back to 5 before the config binds. The LIVE column count is this capped
        /// by how many cells actually fit the content width (see <see cref="Layout"/>), so it never
        /// exceeds the space. Read once per Layout into a local; never per cell.</summary>
        private static int CellCols()
        {
            try { if (UIAConfig.GridCellCols != null) return Mathf.Clamp(UIAConfig.GridCellCols.Value, 1, 10); }
            catch { }
            return 5;
        }

        /// <summary>The configured maximum side-by-side bag columns (<see cref="UIAConfig.GridMaxBagCols"/>,
        /// 1..5, default 2), falling back to 2 before the config binds. Fewer columns are used when the
        /// window is too narrow to hold them (the masonry pack in <see cref="Layout"/>).</summary>
        private static int MaxBagCols()
        {
            try { if (UIAConfig.GridMaxBagCols != null) return Mathf.Clamp(UIAConfig.GridMaxBagCols.Value, 1, 5); }
            catch { }
            return 2;
        }

        /// <summary>The F9-editable SORT-button scale (<see cref="UIAConfig.GridSortButtonScale"/>,
        /// clamped 0.5..2.0, default 1.0), falling back to 1.0 before the config binds. The button's
        /// footprint is <c>SortW/SortH x</c> this, keeping aspect. Read once per Layout/StyleRegion into a
        /// local; the same scale governs the button on a pinned window (its region is a GridRegionView too).</summary>
        private static float SortScale()
        {
            try { if (UIAConfig.GridSortButtonScale != null) return Mathf.Clamp(UIAConfig.GridSortButtonScale.Value, 0.5f, 2f); }
            catch { }
            return 1f;
        }

        /// <summary>The live SORT-button width in px (<see cref="SortW"/> scaled by <see cref="SortScale"/>).</summary>
        private static float SortWidth() { return SortW * SortScale(); }

        /// <summary>The live SORT-button height in px (<see cref="SortH"/> scaled by <see cref="SortScale"/>).</summary>
        private static float SortHeight() { return SortH * SortScale(); }

        private RectTransform _rect;

        // Region chrome (built once in BuildBody, restyled every RefreshIfDirty).
        private GameObject _regionGo;
        private RectTransform _regionRt;
        private PanelGraphic _regionBg;
        private GridTab _tab;

        // Per-region Sort control (top-right, in the tab band opposite the tab). Shown only when the
        // container passes vanilla's sortable gate; hidden otherwise.
        private GameObject _sortGo;
        private RectTransform _sortRt;
        private PanelGraphic _sortBg;
        private RegionClickable _sortClick;
        private TextMeshProUGUI _sortLabel;
        private bool _sortable;

        // Profile-mode strip controls (built once, shown only while GridProfileMode.Active): the
        // CHIP (assigned profile name / "no profile" — click opens the assign popup) and the
        // CAPTURE button (click opens the capture confirm panel). Config/profile state only.
        private GameObject _chipGo;
        private RectTransform _chipRt;
        private PanelGraphic _chipBg;
        private RegionClickable _chipClick;
        private TextMeshProUGUI _chipLabel;
        private GameObject _capGo;
        private RectTransform _capRt;
        private PanelGraphic _capBg;
        private RegionClickable _capClick;
        private TextMeshProUGUI _capLabel;

        // Device control-button pool (grown on demand, idled when unused). One themed square per device
        // interaction, wired to ItemActions.PressInteractable (the MP-safe funnel); a stack additionally
        // gets the "split N" square wired to ItemActions.SplitStackCount (the radial's own path).
        private readonly List<CtrlButton> _ctrls = new List<CtrlButton>(3);
        private readonly List<DeviceControls.FlatControl> _ctrlControls = new List<DeviceControls.FlatControl>(2);
        private int _activeCtrls;
        private float _nextCtrlPoll;

        // D-005: the "choose number" split count for the bound stack — the radial's scroll-chosen
        // count (ItemMenuBuilder "Split count"), kept per region and per stack (reset when the region
        // binds a different container), clamped to [1, Quantity-1] exactly like the radial.
        private int _splitCount = 1;
        private long _splitCountFor;

        private bool _stripVisible;
        private bool _chipAssigned;
        private float _chipPrefW;     // label-fitting chip width, measured at Bind
        private float _chipDrawW;     // laid-out chip width (Layout clamps to the region)
        private int _stripStyleHash;
        private bool _stripStyledChipHover;
        private bool _stripStyledCapHover;
        private float _stripStyledChipW = -1f;
        private bool _stripStyled;

        // Instance-local pools (children of this view; surplus is deactivated, never destroyed here).
        private readonly List<BagGridCell> _cells = new List<BagGridCell>(8);
        private readonly List<GridRegionView> _childViews = new List<GridRegionView>(2);

        // Masonry scratch: per-column running bottom Y while packing nested bag regions into columns.
        // INSTANCE-local (never static): a child region's own Layout runs INSIDE this region's pack
        // loop, so a shared buffer would clobber mid-pack. Fully overwritten each Layout — it holds no
        // state across a rebuild or a hot reload (the whole tree is destroyed with the panel canvas).
        // Sized past the max bag-column count (1..5).
        private readonly float[] _colBottoms = new float[8];

        private ContainerNode _node;
        private int _depth;
        private bool _collapsed;
        private bool _isRoot;      // the whole-inventory root node (no Container): no tab, no border wrapper
        private int _activeCells;
        private int _activeChildren;
        private float _cellPx = -1f;   // last applied cell edge (so Layout re-sizes only when it moved)
        private float _iconScale = -1f; // last applied icon scale (a live F9 icon-scale edit re-sizes cells even when the edge did not move)
        // Last laid-out region box size. StyleRegion issues the corner sweep through
        // GridTheme.ApplyBox (the ONE place a radius may be decided), which needs the box's w/h —
        // Layout records them here rather than calling SetShape itself.
        private float _boxW;
        private float _boxH;
        // Re-paint guard. The region's fill is the theme fill TINTED (see RegionFillAlpha), so the
        // two writes disagree by construction: calling ApplyBox every frame would set the untinted
        // fill and the tint would set it back, dirtying the mesh forever. Restyle only when something
        // that feeds it actually moved — the resolved theme (GridTheme.StyleHash, the same signal
        // TheGridPanel polls), the box size, or the depth/hover state.
        private int _styleHash;
        private float _styledW = -1f;
        private float _styledH = -1f;
        private int _styledDepth = -1;
        private bool _styledHint;      // ghost routing hint (O4d) state at the last repaint
        private int _sortStyleHash;
        private bool _sortStyledHover;
        private bool _sortStyled;

        /// <summary>The region's RectTransform — the parent view/panel positions this through it.</summary>
        public RectTransform Rect { get { return _rect; } }

        /// <summary>The height (px) of the manila tab band this region reserves at its top, live from
        /// the F9-editable <see cref="UIAConfig.GridTabHeight"/> (0 for the root, which has no tab). A
        /// <see cref="PinnedInventoryWindow"/> SUPPRESSES the tab but the region still reserves this
        /// band, so the window reads it every Layout to keep its body offset in sync when the tab
        /// height changes live.</summary>
        public float TabBandHeight { get { return (_tab != null && !_isRoot) ? _tab.Height : 0f; } }

        /// <summary>Build an idle region view under <paramref name="parent"/>. The region box + tab are
        /// wired once here (the tab's collapse action is a method group, so a later <see cref="Bind"/>
        /// allocates no delegates); content and geometry come from Bind/Layout.</summary>
        public static GridRegionView Create(Transform parent)
        {
            var go = new GameObject("GridRegionView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            var view = go.AddComponent<GridRegionView>();
            view._rect = rect;
            view.BuildBody();
            go.SetActive(false);
            return view;
        }

        private void BuildBody()
        {
            // Region box — created FIRST so it draws BEHIND the cells + nested regions. Not a raycast
            // target: empty region area is inert (the tab owns the only click surface), and clicks on
            // a cell land on the cell. Centre pivot: PanelGraphic draws its box centred on the rect
            // origin, so a top-left pivot would offset the glass half its size (the bug fixed in
            // Nested mode). Layout places the CENTRE; a top-left anchor keeps the flow math simple.
            _regionGo = new GameObject("Region", typeof(RectTransform));
            _regionGo.transform.SetParent(_rect, false);
            _regionRt = (RectTransform)_regionGo.transform;
            _regionRt.anchorMin = _regionRt.anchorMax = new Vector2(0f, 1f);
            _regionRt.pivot = new Vector2(0.5f, 0.5f);
            _regionBg = _regionGo.AddComponent<PanelGraphic>();
            _regionBg.raycastTarget = false;

            // The manila tab (its own PolygonPanelGraphic raycast target + collapse click). Created
            // after the region box; Bind re-parents it to LAST sibling so pool-grown cells + nested
            // regions still draw behind it. Collapse is a method group set once — no per-rebuild churn.
            _tab = GridTab.Create(_rect);
            _tab.Clicked = ToggleCollapse;

            // Sort control — built last so it draws (and raycasts) above the region box; Bind re-parents
            // the tab to LAST sibling afterwards, and Layout keeps the two on opposite ends of the tab
            // band, so they never contend for the same pixels. Centre pivot: PanelGraphic draws its box
            // centred on the rect origin (a top-left pivot would offset it half its size).
            _sortGo = new GameObject("Sort", typeof(RectTransform));
            _sortGo.transform.SetParent(_rect, false);
            _sortRt = (RectTransform)_sortGo.transform;
            _sortRt.anchorMin = _sortRt.anchorMax = new Vector2(0f, 1f);
            _sortRt.pivot = new Vector2(0.5f, 0.5f);
            _sortBg = _sortGo.AddComponent<PanelGraphic>();
            _sortBg.raycastTarget = true;
            _sortClick = _sortGo.AddComponent<RegionClickable>();
            _sortClick.Clicked = DoSort;
            _sortLabel = HudText.Make(_sortRt, "SortLabel", HudText.Size(9f),
                TextAlignmentOptions.Center, warp: false);
            var slr = _sortLabel.rectTransform;
            slr.anchorMin = slr.anchorMax = new Vector2(0.5f, 0.5f);
            slr.pivot = new Vector2(0.5f, 0.5f);
            slr.anchoredPosition = Vector2.zero;
            slr.sizeDelta = new Vector2(SortWidth(), SortHeight());   // live F9 scale; Layout re-fits it
            HudText.Set(_sortLabel, "SORT");   // ASCII only: the game TMP font tofus non-Latin glyphs
            _sortGo.SetActive(false);

            // Profile-mode strip controls (chip + CAPTURE), the Sort trio pattern cloned. Built
            // once, inactive until a profile-mode Bind shows them; click actions are method
            // groups wired here so a re-Bind allocates no delegates. All mutation behind these
            // clicks is CONFIG/PROFILE state (BagProfileStore / ProfileCapture) — never game state.
            _chipGo = new GameObject("ProfileChip", typeof(RectTransform));
            _chipGo.transform.SetParent(_rect, false);
            _chipRt = (RectTransform)_chipGo.transform;
            _chipRt.anchorMin = _chipRt.anchorMax = new Vector2(0f, 1f);
            _chipRt.pivot = new Vector2(0.5f, 0.5f);
            _chipBg = _chipGo.AddComponent<PanelGraphic>();
            _chipBg.raycastTarget = true;
            _chipClick = _chipGo.AddComponent<RegionClickable>();
            _chipClick.Clicked = OpenProfilePopup;
            _chipLabel = HudText.Make(_chipRt, "ChipLabel", HudText.Size(9f),
                TextAlignmentOptions.Center, warp: false);
            _chipLabel.overflowMode = TextOverflowModes.Truncate;   // never "..." — the ellipsis glyph tofus
            var clr = _chipLabel.rectTransform;
            clr.anchorMin = Vector2.zero;
            clr.anchorMax = Vector2.one;
            clr.pivot = new Vector2(0.5f, 0.5f);
            clr.offsetMin = new Vector2(3f, 0f);
            clr.offsetMax = new Vector2(-3f, 0f);
            _chipGo.SetActive(false);

            _capGo = new GameObject("ProfileCapture", typeof(RectTransform));
            _capGo.transform.SetParent(_rect, false);
            _capRt = (RectTransform)_capGo.transform;
            _capRt.anchorMin = _capRt.anchorMax = new Vector2(0f, 1f);
            _capRt.pivot = new Vector2(0.5f, 0.5f);
            _capBg = _capGo.AddComponent<PanelGraphic>();
            _capBg.raycastTarget = true;
            _capClick = _capGo.AddComponent<RegionClickable>();
            _capClick.Clicked = OpenCapturePanel;
            _capLabel = HudText.Make(_capRt, "CaptureLabel", HudText.Size(9f),
                TextAlignmentOptions.Center, warp: false);
            var cpl = _capLabel.rectTransform;
            cpl.anchorMin = Vector2.zero;
            cpl.anchorMax = Vector2.one;
            cpl.pivot = new Vector2(0.5f, 0.5f);
            cpl.offsetMin = Vector2.zero;
            cpl.offsetMax = Vector2.zero;
            HudText.Set(_capLabel, "CAPTURE");   // ASCII only
            _capGo.SetActive(false);
        }

        /// <summary>Structural (re)build: bind this view to <paramref name="node"/> at tree
        /// <paramref name="depth"/>, setting the tab content and renting cells for the node's own slots
        /// and child region-views for its nested storage containers (a collapsed region renders
        /// neither). Called by the panel only when the structural signature changed, so per-rebuild
        /// allocation is acceptable; steady state does not run this. After Bind, call
        /// <see cref="Layout"/> to place everything.
        ///
        /// <paramref name="forceExpanded"/> pins this region open regardless of the collapse store —
        /// used by a pinned window, whose whole purpose is to show ONE bag's contents and which
        /// suppresses the region's own collapse tab. Without it, the collapsed-by-default store would
        /// render every pinned window empty. Nested child regions inherit the store default (false).</summary>
        public void Bind(ContainerNode node, int depth, bool forceExpanded = false)
        {
            _node = node;
            _depth = depth;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            _isRoot = node != null && node.Container == null;
            _collapsed = !_isRoot && !forceExpanded && node != null && GridCollapseStore.IsCollapsed(node.RefId);

            // Tab content: container name (ASCII already) + its thumbnail (client-safe read). The
            // whole-inventory ROOT gets NO tab/border wrapper — it is just the flat, full-width stack
            // of its top-level container regions (the panel's own "The Grid" title already frames it).
            if (_isRoot)
            {
                if (_tab.Rect.gameObject.activeSelf) _tab.Rect.gameObject.SetActive(false);
            }
            else
            {
                if (!_tab.Rect.gameObject.activeSelf) _tab.Rect.gameObject.SetActive(true);
                Sprite icon = null;
                if (node.Container != null)
                {
                    try { icon = node.Container.GetThumbnail(); } catch { }
                }
                // Passive profile badge (design O4b): the assigned profile's short ASCII tag, shown
                // only OUTSIDE profile mode (the mode's chip strip supersedes it) and behind its
                // config off-switch. Store reads happen here — a structural rebuild — never per
                // frame; ProfileTag is cached per loaded profile name.
                string badge = null;
                if (node.Container != null && !GridProfileMode.Active && ProfileBadgesOn())
                {
                    try
                    {
                        string pn = BagProfileStore.GetAssignedProfileName(node.Container);
                        if (!string.IsNullOrEmpty(pn)) badge = BagProfileStore.ProfileTag(pn);
                    }
                    catch { }
                }
                // 5-arg overload: hand the tab the container's persistent ReferenceId (the 3-arg one
                // leaves GridTab.RefId at 0, and GridTab.BeginDragOut early-returns on RefId == 0 —
                // which made the whole drag-out-to-pin gesture unreachable dead code) + the badge.
                _tab.Set(node.Title, icon, _collapsed, node.RefId, badge);
            }

            // Sort control: only for a real container that passes vanilla's sortable gate.
            HudText.Sync(_sortLabel);
            _sortable = !_collapsed && !_isRoot && ContainerSortable(node);
            if (_sortGo.activeSelf != _sortable) _sortGo.SetActive(_sortable);

            // Profile-mode strip (design O4a): chip + CAPTURE, per real bag region, only while the
            // mode is on. Mode flips force a rebuild (TheGridPanel diffs GridProfileMode's stamp),
            // so evaluating here — structurally — is enough; nothing profile-ish runs per frame.
            // Gated on the ONE assignability predicate (redesign plan Q5 / FlorpyDorp's "only
            // backpacks and boxes are assignable"): a region for a suit, a tool with slots or a
            // packaging box renders normally but offers no chip and no CAPTURE, because neither
            // gesture has anywhere legitimate to write. The passive BADGE above is deliberately
            // NOT gated — an assignment made before this rule existed stays visible (hide, never
            // destroy) even though the router no longer honours it.
            _stripVisible = GridProfileMode.Active && !_collapsed && !_isRoot
                && node != null && BagProfileGate.IsAssignableContainer(node.Container);
            if (_chipGo.activeSelf != _stripVisible) _chipGo.SetActive(_stripVisible);
            if (_capGo.activeSelf != _stripVisible) _capGo.SetActive(_stripVisible);
            if (_stripVisible)
            {
                string pn = null;
                try { pn = BagProfileStore.GetAssignedProfileName(node.Container); } catch { }
                _chipAssigned = !string.IsNullOrEmpty(pn);
                string chipText = _chipAssigned ? pn : "no profile";
                HudText.Sync(_chipLabel);
                HudText.Set(_chipLabel, chipText);
                float m = 40f;
                float mm = _chipLabel.GetPreferredValues(chipText).x;
                if (!float.IsNaN(mm) && !float.IsInfinity(mm)) m = mm;
                _chipPrefW = Mathf.Clamp(m + ChipPad * 2f, ChipMinW, ChipMaxW);
                HudText.Sync(_capLabel);
                _stripStyled = false;   // repaint the strip against the fresh assignment state
            }

            // Cells for this node's own slots (none while collapsed). The whole-inventory ROOT renders
            // NO own cells at all: its hands / helmet / glasses loose slots are dropped, so the window
            // is exactly the stack of container regions (the "top line" the spec removed).
            float cell = CellPx();
            int slotCount = (!_collapsed && !_isRoot && node != null && node.Slots != null) ? node.Slots.Count : 0;
            EnsureCells(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                var c = _cells[i];
                PrepCell(c, cell);
                c.Bind(node.Slots[i]);
                c.SetActiveHand(GridModel.IsActiveHand(node.Slots[i]));
            }
            for (int i = slotCount; i < _cells.Count; i++) _cells[i].Idle();
            _activeCells = slotCount;

            // Nested STORAGE sub-regions (none while collapsed). In Grid mode node.Children are only
            // the nested containers — tools already stayed leaf cells above.
            int childCount = (!_collapsed && node != null && node.Children != null) ? node.Children.Count : 0;
            EnsureChildViews(childCount);
            for (int i = 0; i < childCount; i++) _childViews[i].Bind(node.Children[i], depth + 1);
            for (int i = childCount; i < _childViews.Count; i++) _childViews[i].Recycle();
            _activeChildren = childCount;

            // Device control buttons (on/off, lock/arm, valve…) for this node's container, if it is an
            // interactive device. Empty for a plain bag — the enumeration returns nothing, so no strip.
            BuildControls();

            // Hide the region box entirely when collapsed (just the tab shows), and always for the root.
            _regionGo.SetActive(!_collapsed && node != null && !_isRoot);

            // Keep the tab + Sort control + profile strip drawing (and raycasting) ABOVE the cells
            // + nested regions the pool grew as later siblings.
            if (_sortable) _sortRt.SetAsLastSibling();
            if (_stripVisible)
            {
                _chipRt.SetAsLastSibling();
                _capRt.SetAsLastSibling();
            }
            if (!_isRoot) _tab.Rect.SetAsLastSibling();

            StyleRegion();
        }

        private void PrepCell(BagGridCell c, float cell)
        {
            // Top-left ANCHOR for the section's downward/rightward flow, CENTRE pivot for the glass box
            // (PanelGraphic draws centred on the rect origin). Anchor + pivot are independent — Layout
            // places the CENTRE. Same idiom BagGridView uses.
            var r = c.Rect;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            c.SetSize(cell);
        }

        private void EnsureCells(int n)
        {
            while (_cells.Count < n) _cells.Add(BagGridCell.Create(_rect));
        }

        private void EnsureChildViews(int n)
        {
            while (_childViews.Count < n) _childViews.Add(Create(_rect));
        }

        private void EnsureCtrlPool(int n)
        {
            while (_ctrls.Count < n) _ctrls.Add(CtrlButton.Create(_rect));
        }

        /// <summary>Build the device-control buttons for this region's container (structural rebuild only).
        /// Enumerates the thing's real key interactions (<see cref="DeviceControls.BuildFlat"/>) and wires
        /// one themed SQUARE per one to <see cref="ItemActions.PressInteractable"/> — the SAME gated funnel
        /// the radial uses (server-authoritative, occupant/interactable re-verified at execute time). An
        /// empty list (a plain bag, or a device with nothing to change) leaves no buttons.
        ///
        /// <para>D-005, a STACK: its vanilla Button1/Button2 ARE Split One / Split Half (Stackable.InteractWith,
        /// live 27798 Stackable.cs) and get the "1" / "1/2" glyphs; a third square — the radial's
        /// "Split count" (ItemMenuBuilder.BuildSplitLevel) — is added on the SAME gate the radial uses,
        /// <see cref="ItemActions.CanSplitCount"/> (host / single-player with 2+ in the stack; arbitrary
        /// count has no networked vanilla path, so an MP client never sees it). Scroll over it to choose
        /// N, click to split N off through <see cref="ItemActions.SplitStackCount"/>.</para></summary>
        private void BuildControls()
        {
            _ctrlControls.Clear();
            var thing = (!_isRoot && !_collapsed && _node != null) ? _node.Container : null;
            if (thing != null) DeviceControls.BuildFlat(thing, _ctrlControls);

            bool stack = thing is Assets.Scripts.Objects.Items.Stackable;
            bool addCount = stack && ItemActions.CanSplitCount(thing);
            long thingId = 0L;
            try { thingId = thing != null ? thing.ReferenceId : 0L; } catch { }
            if (thingId != _splitCountFor) { _splitCountFor = thingId; _splitCount = 1; }

            int n = _ctrlControls.Count + (addCount ? 1 : 0);
            EnsureCtrlPool(n);
            for (int i = 0; i < _ctrlControls.Count; i++)
            {
                var b = _ctrls[i];
                var t = _ctrlControls[i].Thing;
                var ia = _ctrlControls[i].Interactable;
                CtrlKind kind = CtrlKind.Generic;
                if (stack && ia != null)
                {
                    if (ia.Action == InteractableType.Button1) kind = CtrlKind.SplitOne;
                    else if (ia.Action == InteractableType.Button2) kind = CtrlKind.SplitHalf;
                }
                b.Bind(kind, t, ia, _ctrlControls[i].Label, _ctrlControls[i].Enabled);
                b.Input.Clicked = () => ItemActions.PressInteractable(t, ia);
                b.Input.Scrolled = null;   // a plain control hands the wheel on to the list
            }
            if (addCount)
            {
                var b = _ctrls[_ctrlControls.Count];
                var t = thing;
                b.Bind(CtrlKind.SplitCount, t, null, "Split " + _splitCount, true);
                b.Input.Clicked = DoSplitCount;
                b.Input.Scrolled = AdjustSplitCount;
            }
            for (int i = n; i < _ctrls.Count; i++) _ctrls[i].Idle();
            _activeCtrls = n;
            _nextCtrlPoll = 0f;   // force a fresh label/enabled read on the next refresh
        }

        /// <summary>The bound stack's current split ceiling (Quantity - 1, at least 1) — the radial's
        /// clamp (ItemMenuBuilder.BuildSplitLevel). Client-safe: Quantity is networked state.</summary>
        private int SplitCountMax()
        {
            var s = _node != null ? _node.Container as Assets.Scripts.Objects.Items.Stackable : null;
            int q = 0;
            try { q = s != null ? s.Quantity : 0; } catch { }
            return Mathf.Max(1, q - 1);
        }

        /// <summary>Wheel over the "split N" square: step the count, exactly like the radial's scroll
        /// (+1 per notch up, -1 down, clamped to [1, Quantity-1]). Local UI state only.</summary>
        private void AdjustSplitCount(int delta)
        {
            _splitCount = Mathf.Clamp(_splitCount + delta, 1, SplitCountMax());
            _nextCtrlPoll = 0f;   // show the new number on this very refresh
        }

        /// <summary>Click on the "split N" square: split the chosen count off through the radial's own
        /// action, <see cref="ItemActions.SplitStackCount"/> (host/SP only, carried-by-local-player gated,
        /// clamped again at execute time; never a client-side Quantity write).</summary>
        private void DoSplitCount()
        {
            var thing = _node != null ? _node.Container : null;
            if (thing == null) return;
            _splitCount = Mathf.Clamp(_splitCount, 1, SplitCountMax());
            ItemActions.SplitStackCount(thing, _splitCount);
            _nextCtrlPoll = 0f;
        }

        /// <summary>Place the tab, the region box, the wrapped cell grid, and the inset nested regions
        /// within <paramref name="width"/> px; returns the total height used and sizes this view's rect
        /// to it. A collapsed (or null) region is just the tab row. Recurses into active child regions
        /// with an indented, narrower width. Cheap — pure RectTransform writes, no allocation.</summary>
        public float Layout(float width)
        {
            width = Mathf.Max(MinRegionW, width);
            bool root = _isRoot;
            float cell = CellPx();

            float boxTop;
            if (root)
            {
                boxTop = 0f;   // the whole-inventory root has no tab and no border wrapper
            }
            else
            {
                // A live F9 tab-text-size edit re-applies the pooled tab's font and re-fits its
                // PreferredWidth here (a Relayout, not a rebind, drives this — so it must run before the
                // width read below). A no-op float compare when the size did not change.
                _tab.SyncTextSize();

                // Tab sits above the region's top border, inset from the left corner like a folder tab.
                // Its natural (label-fitting) width, clamped so it never exceeds the region — and, when
                // this region is sortable, reserving the Sort control's slot in the tab band to its
                // right, so even a very long name can never crowd the button out of the band.
                // Live F9 Sort-button footprint (scaled, aspect-kept), read once for the reservation
                // and the placement below.
                float sortW = SortWidth();
                float sortH = SortHeight();
                float tabRoom = width - TabLeft * 2f;
                if (_sortable) tabRoom -= SortTabGap + sortW;
                float tabW = Mathf.Min(_tab.PreferredWidth, Mathf.Max(1f, tabRoom));
                _tab.SetWidth(tabW);
                _tab.Rect.anchoredPosition = new Vector2(TabLeft + tabW * 0.5f, -_tab.Height * 0.5f); // centre pivot
                boxTop = _tab.Height;

                if (_sortable)
                {
                    // In the TAB BAND, just to the RIGHT of the manila tab, vertically centred on it
                    // (sortH < the tab height). Seating it ABOVE the box — rather than inside its first
                    // cell row — means the cell grid always uses the full width with no reserved slot,
                    // and the reservation above guarantees it stays inside the region's right edge.
                    float sx = TabLeft + tabW + SortTabGap + sortW * 0.5f;
                    float sxMax = width - 2f - sortW * 0.5f;
                    if (sx > sxMax) sx = sxMax;
                    _sortRt.anchoredPosition = new Vector2(sx, -_tab.Height * 0.5f); // centre pivot
                    _sortRt.sizeDelta = new Vector2(sortW, sortH);
                    if (_sortLabel != null) _sortLabel.rectTransform.sizeDelta = new Vector2(sortW, sortH);
                    // A live scale edit changes only Layout metrics, not GridTheme.StyleHash, so the
                    // hash-gated StyleRegion below would keep the stale corner sweep — force one restyle.
                    _sortStyled = false;
                    // No SetShape here: the corner sweep is part of the inherited box theme, so the
                    // ONE place it is issued is GridTheme.ApplyBox (from StyleRegion).
                }

                if (_stripVisible)
                {
                    // The chip + CAPTURE own the box's top edge: chip at the left, CAPTURE right after
                    // it. The Sort control now lives in the TAB BAND above the box (not inside its
                    // top-right corner), so the strip has the full box width and reserves nothing for
                    // it. The cells start BELOW this band (see the y advance), so nothing here covers a cell.
                    float bandY = boxTop + Border + 3f;
                    float left = Border + InnerPad;
                    float rightLimit = width - Border - 3f;
                    float avail = Mathf.Max(24f, rightLimit - left - CapBtnW - 4f);
                    float chipW = Mathf.Min(_chipPrefW, avail);
                    _chipRt.anchoredPosition = new Vector2(left + chipW * 0.5f, -(bandY + StripH * 0.5f)); // centre pivot
                    _chipRt.sizeDelta = new Vector2(chipW, StripH);
                    _capRt.anchoredPosition = new Vector2(left + chipW + 4f + CapBtnW * 0.5f,
                                                          -(bandY + StripH * 0.5f)); // centre pivot
                    _capRt.sizeDelta = new Vector2(CapBtnW, StripH);
                    _chipDrawW = chipW;
                }
            }

            if (_collapsed || _node == null)
            {
                _rect.sizeDelta = new Vector2(width, boxTop);
                return boxTop;
            }

            // Non-root regions inset their content by the border + pad; the root fills full width so its
            // container regions read as one flat grid. Nested storage regions inset a touch; root ones don't.
            float inset = root ? 0f : (Border + InnerPad);
            float childIndent = root ? 0f : NestedIndent;
            float innerLeft = inset;
            float contentW = Mathf.Max(cell, width - 2f * inset);

            float y = boxTop + inset;
            // The profile strip band occupies the box's top edge: everything (cells AND nested
            // regions) starts below it while the mode is up.
            if (_stripVisible) y += StripH + StripGap;
            bool hadContent = false;

            if (_activeCells > 0)
            {
                // Column count: the RESPONSIVE fit (how many cells the live content width holds) capped
                // by the configured column count (default 5 — a 15-slot bag reads as 3 rows of 5). A
                // narrower window or bag column wraps tighter, but the grid never exceeds the chosen
                // count. Both terms are >= 1, so cols >= 1 (no divide-by-zero below). The Sort control
                // now lives in the tab band, so there is no first-row reservation — every row is full.
                int responsiveCols = Mathf.Max(1, Mathf.FloorToInt((contentW + CellGap) / (cell + CellGap)));
                int cols = Mathf.Min(responsiveCols, CellCols());
                // Re-size the cells only when a size that feeds the cell actually moved since the last
                // pass — the F10/F9 cell EDGE or the F9 ICON SCALE (which SetSize applies but does not
                // change the edge, so an edge-only guard would miss it) — so a window resize-drag
                // Relayout stays pure RectTransform writes (no mesh rebuilds).
                float iconScale = BagGridCell.IconScale();
                bool resize = cell != _cellPx || iconScale != _iconScale;
                _cellPx = cell;
                _iconScale = iconScale;
                for (int i = 0; i < _activeCells; i++)
                {
                    if (resize) _cells[i].SetSize(cell);
                    int row = i / cols;
                    int col = i % cols;
                    // Centre-pivot cell: place its CENTRE (content-left corner + half a cell).
                    _cells[i].Rect.anchoredPosition = new Vector2(
                        innerLeft + col * (cell + CellGap) + cell * 0.5f,
                        -(y + row * (cell + CellGap)) - cell * 0.5f);
                }
                int rows = (_activeCells + cols - 1) / cols;
                y += rows * (cell + CellGap) - CellGap;
                hadContent = true;
            }

            if (_activeChildren > 0)
            {
                if (hadContent) y += SectionGap;   // one gap between the cell grid and the bag columns

                // Nested bags pack into N side-by-side columns (MASONRY): the column count falls out of
                // the band width — more columns as the window is dragged wider — capped by the
                // configured maximum (default 2). Each column is kept wide enough to hold a full cell
                // row (CellCols cells) inside a child region's own inset, so #7b (cell columns) and #7c
                // (bag columns) never fight: a bag column is never so narrow it forces its cell grid
                // below the chosen count.
                float childBandLeft = innerLeft + childIndent;
                float childBandW = Mathf.Max(cell, contentW - childIndent);
                float colGap = SectionGap;
                float regionInset = 2f * (Border + InnerPad);   // a child region's own left+right inset
                float minBagColW = CellCols() * cell + (CellCols() - 1) * CellGap + regionInset;
                int bagCols = Mathf.Clamp(
                    Mathf.FloorToInt((childBandW + colGap) / (minBagColW + colGap)), 1,
                    Mathf.Min(MaxBagCols(), _colBottoms.Length));
                if (bagCols > _activeChildren) bagCols = Mathf.Max(1, _activeChildren);
                float colW = (childBandW - (bagCols - 1) * colGap) / bagCols;

                // Seed every column's running bottom to the band top, then drop each bag into the
                // currently-SHORTEST column (ties → lowest index, so at bagCols == 1 this is the old
                // in-order vertical stack). Each bag is laid out at its column width, so its own cell
                // grid re-wraps to that width.
                for (int c = 0; c < bagCols; c++) _colBottoms[c] = y;
                for (int i = 0; i < _activeChildren; i++)
                {
                    int best = 0;
                    for (int c = 1; c < bagCols; c++)
                        if (_colBottoms[c] < _colBottoms[best]) best = c;
                    var cv = _childViews[i];
                    float cx = childBandLeft + best * (colW + colGap);
                    float cy = _colBottoms[best];
                    float ch = cv.Layout(colW);
                    cv.Rect.anchoredPosition = new Vector2(cx, -cy);   // child region: top-left pivot
                    _colBottoms[best] = cy + ch + colGap;   // trailing gap, for the next bag in this column
                }

                // Region height = the TALLEST column (each column's trailing gap stripped). Every
                // column holds >= 1 bag because bagCols <= _activeChildren, so this is well-defined.
                float maxBottom = y;
                for (int c = 0; c < bagCols; c++)
                {
                    float b = _colBottoms[c] - colGap;
                    if (b > maxBottom) maxBottom = b;
                }
                y = maxBottom;
                hadContent = true;
            }

            // Device controls (D-005): compact cell-sized SQUARES wrapped into rows with the cells' own
            // column rule (the responsive fit capped by the configured column count), under the
            // cells / nested regions — never a full-width bar that stretches across a wide window.
            if (_activeCtrls > 0)
            {
                if (hadContent) y += SectionGap;
                float cs = cell;   // the live F10/F9 cell edge: a control is exactly one inventory cell
                int responsiveCtrlCols = Mathf.Max(1, Mathf.FloorToInt((contentW + CellGap) / (cs + CellGap)));
                int ctrlCols = Mathf.Min(responsiveCtrlCols, CellCols());
                for (int i = 0; i < _activeCtrls; i++)
                {
                    var b = _ctrls[i];
                    b.SetSize(cs);
                    int row = i / ctrlCols;
                    int col = i % ctrlCols;
                    // Centre-pivot square: place its CENTRE (content-left corner + half a cell).
                    b.Rt.anchoredPosition = new Vector2(
                        innerLeft + col * (cs + CellGap) + cs * 0.5f,
                        -(y + row * (cs + CellGap)) - cs * 0.5f);
                }
                int ctrlRows = (_activeCtrls + ctrlCols - 1) / ctrlCols;
                y += ctrlRows * (cs + CellGap) - CellGap;
                hadContent = true;
            }

            y += inset;

            if (!root)
            {
                // Region box: centre-pivot rect spanning the tab's bottom (boxTop) down to y.
                float boxH = Mathf.Max(2f * Border, y - boxTop);
                _regionRt.anchoredPosition = new Vector2(width * 0.5f, -(boxTop + boxH * 0.5f)); // centre pivot
                _regionRt.sizeDelta = new Vector2(width, boxH);
                // The corner sweep rides the inherited theme, so it is issued only by
                // GridTheme.ApplyBox — record the size and repaint.
                _boxW = width;
                _boxH = boxH;
                StyleRegion();
            }

            _rect.sizeDelta = new Vector2(width, y);
            return y;
        }

        /// <summary>Per-frame content refresh: restyle the tab + region border to the live palette, then
        /// refresh visible cells (icon/text dirty key + active-hand) and recurse into visible nested
        /// regions. A collapsed region stops at the tab. Steady-state allocation-free.</summary>
        public void RefreshIfDirty()
        {
            if (_node == null) return;
            StyleRegion();
            if (_collapsed) return;
            for (int i = 0; i < _activeCells; i++)
            {
                var c = _cells[i];
                c.SetActiveHand(GridModel.IsActiveHand(c.Slot));
                c.RefreshIfDirty();
            }
            for (int i = 0; i < _activeChildren; i++) _childViews[i].RefreshIfDirty();
            RefreshControls();
        }

        /// <summary>Per-frame device-control upkeep: poll each button's live state-baked label + enabled
        /// (on→off, armed→disarmed) at a light cadence — the interactable set is fixed by the last Bind, so
        /// the count never changes here — and paint each button to the live theme + hover. ApplyBox is
        /// dirty-guarded downstream, so an unchanged button costs only float compares. No-op with no
        /// controls (the common case: every plain cell/bag region).</summary>
        private void RefreshControls()
        {
            if (_activeCtrls == 0) return;

            if (Time.unscaledTime >= _nextCtrlPoll)
            {
                _nextCtrlPoll = Time.unscaledTime + CtrlPollInterval;
                for (int i = 0; i < _activeCtrls && i < _ctrls.Count; i++)
                {
                    var b = _ctrls[i];
                    if (b.Kind == CtrlKind.SplitCount)
                    {
                        // The radial's count wedge: clamp to the live stack, re-read the same gate.
                        _splitCount = Mathf.Clamp(_splitCount, 1, SplitCountMax());
                        b.SetState("Split " + _splitCount, ItemActions.CanSplitCount(b.Thing), _splitCount.ToString());
                        continue;
                    }
                    if (i >= _ctrlControls.Count) continue;
                    var ctrl = _ctrlControls[i];
                    string label; bool enabled;
                    DeviceControls.ReadControl(ctrl.Thing, ctrl.Interactable, out label, out enabled);
                    ctrl.Enabled = enabled;
                    _ctrlControls[i] = ctrl;   // struct write-back (kept for any other reader)
                    b.SetState(string.IsNullOrEmpty(label) ? b.Label : label, enabled, null);
                }
            }

            Color accent = HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : GridTheme.Border;
            Color txt = HudPalette.TextLabel != null ? HudPalette.TextLabel.Value : GridTheme.Text;
            Color dim = txt; dim.a *= 0.45f;
            for (int i = 0; i < _activeCtrls && i < _ctrls.Count; i++)
                _ctrls[i].Style(accent, txt, dim);
        }

        /// <summary>Append this region's navigable targets to <paramref name="list"/> in DISPLAY order
        /// for the scroll-select cursor (#4): its manila <see cref="GridTab"/> first (the root has
        /// none), then — unless collapsed — its own cells, then recurse into each nested child region
        /// (which emits its own tab, cells and children). Mirrors exactly what the eye reads top-to-
        /// bottom, so wheeling the cursor tracks the layout. Called only on a structural rebuild (from
        /// <see cref="GridSelection.Rebuild"/>), never per frame; allocation-free beyond the list adds.</summary>
        public void CollectNav(List<GridSelection.NavItem> list)
        {
            if (list == null || _node == null || !gameObject.activeSelf) return;

            if (!_isRoot && _tab != null && _tab.Rect != null && _tab.Rect.gameObject.activeSelf)
                list.Add(GridSelection.NavItem.ForTab(_tab));

            if (_collapsed) return;   // a collapsed region shows only its tab

            for (int i = 0; i < _activeCells; i++)
            {
                var c = _cells[i];
                if (c != null && c.Slot != null) list.Add(GridSelection.NavItem.ForCell(c));
            }

            for (int i = 0; i < _activeChildren; i++)
            {
                var cv = _childViews[i];
                if (cv != null) cv.CollectNav(list);
            }
        }

        /// <summary>Paint the tab (its own <see cref="GridTab.RefreshStyle"/>), the Sort button and the
        /// region box through the ONE shared styling path, <see cref="GridTheme.ApplyBox"/>: fill,
        /// border colour, border WIDTH, the corner sweep and the same glass stack a HUD box gets, all
        /// inherited from the global box theme (or the Grid's overrides). NOTHING here hardcodes a
        /// width or a radius — depth is expressed only as the helper's own relative scale
        /// (<c>Region</c> vs <c>RegionNested</c>), so a thin global border stays thin at every depth
        /// and hovered. Every setter downstream is dirty-guarded, so an unchanged frame costs no mesh
        /// rebuild and allocates nothing.</summary>
        private void StyleRegion()
        {
            if (_isRoot) return;   // root shows no tab or border wrapper — nothing to style
            _tab.RefreshStyle();

            int hash = GridTheme.StyleHash();

            if (_sortable && _sortBg != null)
            {
                // The Sort button keeps its hover ACCENT: an interaction affordance, not part of the
                // box theme. ApplyBox supplies it (and the widened line) as a delta on the inherited
                // values. Its footprint is the live F9 scale; Layout resets _sortStyled when the scale
                // moves so the corner sweep re-issues here with the new w/h.
                bool sHover = _sortClick != null && _sortClick.Hover;
                if (!_sortStyled || _sortStyleHash != hash || _sortStyledHover != sHover)
                {
                    _sortStyled = true;
                    _sortStyleHash = hash;
                    _sortStyledHover = sHover;
                    GridTheme.ApplyBox(_sortBg, SortWidth(), SortHeight(), GridTheme.GridSurface.Button, sHover);
                    if (_sortLabel != null)
                        _sortLabel.color = sHover ? HudPalette.LineAccent.Value : HudPalette.TextLabel.Value;
                }
            }

            if (_stripVisible && _chipBg != null && _chipDrawW > 0f)
            {
                // The chip + CAPTURE: hash/hover-gated exactly like the Sort button above.
                bool chHover = _chipClick != null && _chipClick.Hover;
                bool caHover = _capClick != null && _capClick.Hover;
                if (!_stripStyled || _stripStyleHash != hash || _stripStyledChipHover != chHover
                    || _stripStyledCapHover != caHover || _stripStyledChipW != _chipDrawW)
                {
                    _stripStyled = true;
                    _stripStyleHash = hash;
                    _stripStyledChipHover = chHover;
                    _stripStyledCapHover = caHover;
                    _stripStyledChipW = _chipDrawW;
                    GridTheme.ApplyBox(_chipBg, _chipDrawW, StripH, GridTheme.GridSurface.Button, chHover);
                    GridTheme.ApplyBox(_capBg, CapBtnW, StripH, GridTheme.GridSurface.Button, caHover);
                    Color accent = HudPalette.LineAccent != null
                        ? HudPalette.LineAccent.Value : GridTheme.Border;
                    Color muted = HudPalette.TextLabel != null
                        ? HudPalette.TextLabel.Value : GridTheme.Text;
                    if (_chipLabel != null)
                        _chipLabel.color = chHover ? accent : (_chipAssigned ? GridTheme.Text : muted);
                    if (_capLabel != null)
                        _capLabel.color = caHover ? accent : muted;
                }
            }

            if (_regionBg == null || _collapsed || _node == null) return;
            if (_boxW <= 0f || _boxH <= 0f) return;   // pre-Layout (Bind) — the first Layout repaints
            // Ghost routing hint (O4d): while a cell drag is live in profile mode, the region
            // whose container the router would pick wears the theme's own HOVER accent — the
            // existing mechanic, on a different surface than the tab's drop cue (tab accent =
            // "release to PIN", region accent = "G would stow HERE"). One static long compare
            // per region per frame; zero when no drag is live.
            bool hinted = GridGhostHint.IsHinted(_node.RefId);
            if (_styleHash == hash && _styledW == _boxW && _styledH == _boxH && _styledDepth == _depth
                && _styledHint == hinted)
                return;
            _styleHash = hash;
            _styledW = _boxW;
            _styledH = _boxH;
            _styledDepth = _depth;
            _styledHint = hinted;

            // Depth is expressed ONLY as the helper's relative scales (a nested region's line is
            // 0.75x a top-level one's), never as an absolute width or a second colour. The ghost
            // hint rides the helper's hover delta (accent border + scaled line), same as every
            // other interactive surface.
            GridTheme.ApplyBox(_regionBg, _boxW, _boxH,
                _depth == 0 ? GridTheme.GridSurface.Region : GridTheme.GridSurface.RegionNested,
                hinted);

            // Faint fill so the region groups its cells without over-darkening (nested regions stack).
            // A scale on the INHERITED alpha, applied after ApplyBox resolved the fill.
            Color fill = _regionBg.color;
            fill.a *= RegionFillAlpha;
            _regionBg.color = fill;
        }

        // ---- tab action (wired once in BuildBody) ----

        /// <summary>Toggle this container's collapse state. It is folded into
        /// <see cref="GridModel.ComputeSignature"/>, so the panel's next Tick sees the signature change
        /// and rebuilds the tree — no direct relayout call needed from here.</summary>
        private void ToggleCollapse()
        {
            if (_node == null) return;
            GridCollapseStore.ToggleCollapsed(_node.RefId);
        }

        /// <summary>The bag this region currently renders (null for the root / a recycled view).
        /// Read by the drag-to-pin drop (a tab hit resolves its owning region, then this).</summary>
        public DynamicThing BoundContainer
        {
            get { return _node != null ? _node.Container : null; }
        }

        /// <summary>The chip: open the themed profile-assign popup anchored under it. Config-state
        /// only — selecting a row writes a <c>BagProfileStore</c> assignment; nothing moves.</summary>
        private void OpenProfilePopup()
        {
            if (!GridProfileMode.Active || _node == null || _node.Container == null) return;
            Vector3 p = _chipRt != null ? _chipRt.position : transform.position;   // overlay canvas: screen px
            GridProfilePopup.Open(_node.Container, new Vector2(p.x, p.y));
        }

        /// <summary>The CAPTURE button: open the generalizing-capture confirm panel for this bag
        /// (design O3b). Config-state only.</summary>
        private void OpenCapturePanel()
        {
            if (!GridProfileMode.Active || _node == null || _node.Container == null) return;
            GridCapturePanel.Open(_node.Container);
        }

        /// <summary>Is the O4b badge feature on? Fail-open (default true) around an unbound config.</summary>
        private static bool ProfileBadgesOn()
        {
            try { return UIAConfig.GridProfileBadges == null || UIAConfig.GridProfileBadges.Value; }
            catch { return true; }
        }

        /// <summary>Sort this region's container through the mutation funnel —
        /// <see cref="ItemActions.SortContainer"/> (host: <c>OnServer.SortContents</c>; client: one
        /// SortContentsMessage — Slot.cs:828-845). Re-verify at EXECUTE time that the SAME container
        /// still occupies the parent slot (MP: a teammate may have taken the bag between build and
        /// click) before handing off; ItemActions re-checks carry/ParentSlot itself. No client-side
        /// reordering, no bulk loop — exactly one authoritative message.</summary>
        private void DoSort()
        {
            if (_node == null || _node.ParentSlot == null) return;
            DynamicThing occ = null;
            try { occ = _node.ParentSlot.Get(); } catch { }   // Get(), never the [Obsolete] Occupant
            if (occ == null || occ != _node.Container) return; // occupant changed → no-op; the rebuild refreshes
            ItemActions.SortContainer(_node.Container);
        }

        /// <summary>True when the node's container has ≥2 sortable inner slots — the exact gate vanilla's
        /// <c>InventoryWindow.IsSortable(parentSlot)</c> uses (<see cref="Slot.IsSortable"/> = a
        /// None/Ore class slot). The whole-inventory ROOT (no ParentSlot) is never sortable.</summary>
        private static bool ContainerSortable(ContainerNode node)
        {
            if (node == null || node.ParentSlot == null) return false;
            DynamicThing c = node.Container;
            if (c == null || !c.HasAnySlots) return false;
            var slots = c.Slots;
            if (slots == null) return false;
            int n = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].IsSortable)
                {
                    n++;
                    if (n > 1) return true;
                }
            }
            return false;
        }

        /// <summary>Return this region (and its whole subtree of cells and child regions) to the idle
        /// pool: deactivate everything, drop any shared glass material (the tab + region box), drop the
        /// node reference. Objects stay parented under this view (destroyed with the canvas on Shutdown),
        /// so nothing is stranded across a hot reload. No statics, no event subscriptions to unhook.</summary>
        public void Recycle()
        {
            _node = null;
            _collapsed = false;
            for (int i = 0; i < _cells.Count; i++) _cells[i].Idle();
            _activeCells = 0;
            for (int i = 0; i < _ctrls.Count; i++) _ctrls[i].Idle();
            _activeCtrls = 0;
            _ctrlControls.Clear();
            for (int i = 0; i < _childViews.Count; i++) _childViews[i].Recycle();
            _activeChildren = 0;
            if (_tab != null) _tab.Idle();
            if (_regionBg != null) HudFxMaterials.Unassign(_regionBg);
            if (_sortBg != null) HudFxMaterials.Unassign(_sortBg);
            if (_regionGo != null && _regionGo.activeSelf) _regionGo.SetActive(false);
            // Drop the re-paint guards so a re-Bind repaints from scratch (a hot reload or a theme
            // change while this view sat idle must not be missed).
            _boxW = 0f;
            _boxH = 0f;
            _styleHash = 0;
            _styledW = -1f;
            _styledH = -1f;
            _styledDepth = -1;
            _styledHint = false;
            _sortStyled = false;
            _sortable = false;
            if (_sortClick != null) _sortClick.Hover = false;
            if (_sortGo != null && _sortGo.activeSelf) _sortGo.SetActive(false);
            // Profile-mode strip: hide + drop its style guards and any shared glass material.
            _stripVisible = false;
            _chipAssigned = false;
            _chipPrefW = 0f;
            _chipDrawW = 0f;
            _stripStyled = false;
            _stripStyledChipW = -1f;
            if (_chipClick != null) _chipClick.Hover = false;
            if (_capClick != null) _capClick.Hover = false;
            if (_chipBg != null) HudFxMaterials.Unassign(_chipBg);
            if (_capBg != null) HudFxMaterials.Unassign(_capBg);
            if (_chipGo != null && _chipGo.activeSelf) _chipGo.SetActive(false);
            if (_capGo != null && _capGo.activeSelf) _capGo.SetActive(false);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Left-click surface for the region's small chrome controls (Sort, the profile
        /// chip, CAPTURE). Records hover so the next <see cref="StyleRegion"/> promotes the accent,
        /// and forwards a LEFT click to <see cref="Clicked"/> — a method group wired once at build
        /// (no per-rebuild delegate churn). Instance-scoped — no statics, nothing to unhook on
        /// teardown; the component dies with the panel's canvas on Shutdown.</summary>
        private sealed class RegionClickable : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public System.Action Clicked;
            public bool Hover;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (Clicked != null) Clicked();
            }

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }
        }

        // ---------------------------------------------------------------- square controls (D-005) --

        /// <summary>What a control square shows. The split trio gets glyphs ("1", "1/2", the chosen
        /// count) over a small "SPLIT" caption; every other control shows its own vanilla label,
        /// auto-sized to fit the square.</summary>
        private enum CtrlKind
        {
            Generic,
            SplitOne,
            SplitHalf,
            SplitCount
        }

        /// <summary>Show the vanilla tooltip with a control's FULL label (the square only has room for
        /// a glyph / a squeezed label). Same singleton + lift the grid cells use
        /// (<see cref="Core.VanillaTooltip"/>), through the 3-argument <c>SetUpTooltip</c> with a NULL
        /// receiver so a stale screen-space receiver cannot clear it on the next LateUpdate (live 27798
        /// PanelToolTip.SetUpTooltip(string,string,IScreenSpaceTooltip) sets <c>_tooltipToUpdate</c>).
        /// Honors the game's own ShowSlotToolTips, like the cells. Fail-soft.</summary>
        private static void ShowControlTip(string title, string body)
        {
            try
            {
                if (string.IsNullOrEmpty(title)) return;
                if (!Assets.Scripts.Serialization.Settings.CurrentData.ShowSlotToolTips) return;
                var tip = Assets.Scripts.UI.PanelToolTip.Instance;
                if (tip == null) return;
                Core.VanillaTooltip.Lift(tip);
                tip.SetUpTooltip(title, body ?? "", null);
            }
            catch { }
        }

        /// <summary>Printable Basic Latin only (the game's TMP font tofus anything else) — for the label
        /// drawn INSIDE a square. Allocation-free for the common all-ASCII label.</summary>
        private static string AsciiLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < ' ' || s[i] > '~') { clean = false; break; }
            }
            if (clean) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c >= ' ' && c <= '~' ? c : ' ');
            }
            return sb.ToString();
        }

        /// <summary>One pooled device-control SQUARE (D-005): a cell-sized glass button painted by the
        /// shared theme path (<see cref="GridTheme.ApplyBox"/>, Button surface — the inherited line,
        /// corner and glass, a hover delta, no literal widths/radii), a centre glyph/label, a small
        /// caption for the split trio, and two DRAWN triangles (never a glyph — TMP tofus arrows) that
        /// mark the "split N" square as scrollable. Owned by one region; pooled (idled, never destroyed
        /// here); dies with the panel canvas on Shutdown, holding no statics.</summary>
        private sealed class CtrlButton
        {
            public GameObject Go;
            public RectTransform Rt;
            public PanelGraphic Bg;
            public CtrlInput Input;
            public TextMeshProUGUI Glyph;
            public TextMeshProUGUI Caption;
            public TriangleGraphic Up;
            public TriangleGraphic Down;
            public CtrlKind Kind;
            public DynamicThing Thing;
            public Interactable Interactable;
            public string Label;          // the FULL vanilla label: the hover tooltip's title
            public bool Enabled = true;
            public float Size = -1f;      // last laid-out edge; -1 forces the next SetSize to re-lay

            public static CtrlButton Create(RectTransform parent)
            {
                var b = new CtrlButton();
                b.Go = new GameObject("Ctrl", typeof(RectTransform));
                b.Go.transform.SetParent(parent, false);
                b.Rt = (RectTransform)b.Go.transform;
                b.Rt.anchorMin = b.Rt.anchorMax = new Vector2(0f, 1f);   // top-left anchor, centre pivot
                b.Rt.pivot = new Vector2(0.5f, 0.5f);                    // (PanelGraphic draws centred)

                b.Bg = b.Go.AddComponent<PanelGraphic>();
                b.Bg.raycastTarget = true;
                b.Input = b.Go.AddComponent<CtrlInput>();
                b.Input.Owner = b;

                b.Glyph = HudText.Make(b.Rt, "Glyph", HudText.Size(12f), TextAlignmentOptions.Center, warp: false);
                b.Glyph.overflowMode = TextOverflowModes.Truncate;   // never "..." — the ellipsis glyph tofus
                b.Glyph.enableAutoSizing = true;
                CentreRect(b.Glyph.rectTransform);

                b.Caption = HudText.Make(b.Rt, "Caption", HudText.Size(8f), TextAlignmentOptions.Center, warp: false);
                b.Caption.overflowMode = TextOverflowModes.Truncate;
                CentreRect(b.Caption.rectTransform);
                b.Caption.gameObject.SetActive(false);

                b.Up = MakeTriangle(b.Rt, "Up", true);
                b.Down = MakeTriangle(b.Rt, "Down", false);

                b.Go.SetActive(false);
                return b;
            }

            private static void CentreRect(RectTransform rt)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }

            private static TriangleGraphic MakeTriangle(RectTransform parent, string name, bool up)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                CentreRect((RectTransform)go.transform);
                ((RectTransform)go.transform).sizeDelta = new Vector2(8f, 8f);
                var t = go.AddComponent<TriangleGraphic>();
                t.raycastTarget = false;
                t.Configure(up, 6f);
                go.SetActive(false);
                return t;
            }

            /// <summary>Structural (re)bind: kind, target and label. Content is re-laid by the next
            /// <see cref="SetSize"/> when the kind changed (split glyph vs full label use different
            /// layouts).</summary>
            public void Bind(CtrlKind kind, DynamicThing thing, Interactable interactable, string label, bool enabled)
            {
                if (kind != Kind) Size = -1f;
                Kind = kind;
                Thing = thing;
                Interactable = interactable;
                Label = label;
                Enabled = enabled;
                if (!Go.activeSelf) Go.SetActive(true);
                HudText.Sync(Glyph);
                HudText.Sync(Caption);

                bool split = kind != CtrlKind.Generic;
                bool count = kind == CtrlKind.SplitCount;
                if (Caption.gameObject.activeSelf != split) Caption.gameObject.SetActive(split);
                if (Up.gameObject.activeSelf != count) Up.gameObject.SetActive(count);
                if (Down.gameObject.activeSelf != count) Down.gameObject.SetActive(count);
                HudText.Set(Caption, split ? "SPLIT" : "");   // ASCII only
                Glyph.enableWordWrapping = !split;            // a label may take two lines; a glyph never
                HudText.Set(Glyph, GlyphFor(null));
            }

            /// <summary>Poll result: the live label (tooltip), enabled state, and — for the "split N"
            /// square — its current number. Re-raises the tooltip when hovered and the text moved.</summary>
            public void SetState(string label, bool enabled, string countGlyph)
            {
                bool labelChanged = !string.Equals(label, Label, System.StringComparison.Ordinal);
                Label = label;
                Enabled = enabled;
                HudText.Set(Glyph, GlyphFor(countGlyph));
                if (labelChanged && Input != null && Input.Hover) ShowTip();
            }

            private string GlyphFor(string countGlyph)
            {
                switch (Kind)
                {
                    case CtrlKind.SplitOne: return "1";
                    case CtrlKind.SplitHalf: return "1/2";
                    case CtrlKind.SplitCount: return string.IsNullOrEmpty(countGlyph) ? GlyphOfCount() : countGlyph;
                    default: return AsciiLabel(Label);
                }
            }

            /// <summary>The count digits out of "Split N" (the bind-time label), without a parse.</summary>
            private string GlyphOfCount()
            {
                string l = Label ?? "";
                int sp = l.LastIndexOf(' ');
                return sp >= 0 && sp + 1 < l.Length ? l.Substring(sp + 1) : "1";
            }

            /// <summary>Size the square to one inventory cell and lay its content out for its kind.
            /// Only does work when the edge (or the kind) changed, so a resize-drag relayout stays
            /// pure RectTransform writes.</summary>
            public void SetSize(float s)
            {
                s = Mathf.Max(16f, s);
                if (Size > 0f && Mathf.Approximately(s, Size)) return;
                Size = s;
                Rt.sizeDelta = new Vector2(s, s);

                bool split = Kind != CtrlKind.Generic;
                bool count = Kind == CtrlKind.SplitCount;
                var g = Glyph.rectTransform;
                if (split)
                {
                    // Glyph in the upper ~60%, caption in the bottom band; the count square leaves
                    // room on the right for its two scroll triangles.
                    float gw = count ? s * 0.62f : s - 6f;
                    g.anchoredPosition = new Vector2(count ? -s * 0.12f : 0f, s * 0.10f);
                    g.sizeDelta = new Vector2(gw, s * 0.56f);
                    Glyph.fontSizeMin = HudText.Size(8f);
                    Glyph.fontSizeMax = HudText.Size(Mathf.Clamp(s * 0.36f, 10f, 24f));

                    var c = Caption.rectTransform;
                    c.anchoredPosition = new Vector2(0f, -s * 0.5f + s * 0.16f);
                    c.sizeDelta = new Vector2(s - 4f, s * 0.26f);
                    Caption.fontSize = HudText.Size(Mathf.Clamp(s * 0.17f, 7f, 10f));
                }
                else
                {
                    g.anchoredPosition = Vector2.zero;
                    g.sizeDelta = new Vector2(s - 6f, s - 6f);
                    Glyph.fontSizeMin = HudText.Size(6f);
                    Glyph.fontSizeMax = HudText.Size(Mathf.Clamp(s * 0.24f, 8f, 13f));
                }

                if (count)
                {
                    float tri = Mathf.Clamp(s * 0.16f, 5f, 12f);
                    float tx = s * 0.5f - tri * 0.5f - 4f;
                    var ur = (RectTransform)Up.transform;
                    var dr = (RectTransform)Down.transform;
                    ur.sizeDelta = dr.sizeDelta = new Vector2(tri + 2f, tri + 2f);
                    ur.anchoredPosition = new Vector2(tx, s * 0.10f + tri * 0.75f);
                    dr.anchoredPosition = new Vector2(tx, s * 0.10f - tri * 0.75f);
                    Up.Configure(true, tri);
                    Down.Configure(false, tri);
                }
            }

            /// <summary>Paint the square through the shared theme path (hover = the theme's own delta)
            /// and colour its glyph/caption/triangles: accent while hovered, the label colour when
            /// enabled, dimmed when vanilla's dry-run says the control is unavailable.</summary>
            public void Style(Color accent, Color txt, Color dim)
            {
                if (Go == null || !Go.activeSelf) return;
                bool hov = Input != null && Input.Hover;
                float s = Size > 0f ? Size : 46f;
                GridTheme.ApplyBox(Bg, s, s, GridTheme.GridSurface.Button, hov);
                Color c = hov ? accent : (Enabled ? txt : dim);
                Glyph.color = c;
                if (Caption.gameObject.activeSelf)
                {
                    Color cc = c;
                    cc.a *= 0.8f;
                    Caption.color = cc;
                }
                if (Up.gameObject.activeSelf) Up.color = c;
                if (Down.gameObject.activeSelf) Down.color = c;
            }

            public void ShowTip()
            {
                ShowControlTip(Label, Kind == CtrlKind.SplitCount ? SplitCountTip : null);
            }

            public void HideTip()
            {
                Core.VanillaTooltip.Clear();
            }

            /// <summary>Back to the pool: drop the target and the click/scroll wiring, take a hovered
            /// tooltip down, release any shared glass material, deactivate.</summary>
            public void Idle()
            {
                if (Input != null)
                {
                    if (Input.Hover) HideTip();
                    Input.Hover = false;
                    Input.Clicked = null;
                    Input.Scrolled = null;
                }
                Thing = null;
                Interactable = null;
                Label = null;
                Enabled = true;
                if (Bg != null) HudFxMaterials.Unassign(Bg);
                if (Go != null && Go.activeSelf) Go.SetActive(false);
            }
        }

        /// <summary>Pointer surface for a control square: LEFT click fires <see cref="Clicked"/> (never
        /// while a radial owns the mouse), hover drives the accent + the full-label tooltip, and the
        /// wheel steps <see cref="Scrolled"/> — the "split N" square's count — or, on every other square,
        /// is handed on up the hierarchy so the list's ScrollRect still scrolls under the pointer.
        /// Instance-scoped; dies with the panel canvas.</summary>
        private sealed class CtrlInput : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
        {
            public System.Action Clicked;
            public System.Action<int> Scrolled;
            public CtrlButton Owner;
            public bool Hover;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (RadialController.AnyRadialOpen) return;   // a wedge click must never also press a control
                if (Clicked != null) Clicked();
            }

            public void OnPointerEnter(PointerEventData e)
            {
                Hover = true;
                if (Owner != null) Owner.ShowTip();
            }

            public void OnPointerExit(PointerEventData e)
            {
                Hover = false;
                if (Owner != null) Owner.HideTip();
            }

            public void OnScroll(PointerEventData e)
            {
                if (e == null) return;
                if (Scrolled != null)
                {
                    float dy = e.scrollDelta.y;
                    if (Mathf.Abs(dy) >= 0.01f) Scrolled(dy > 0f ? 1 : -1);
                    return;
                }
                // Not a value square: pass the wheel on so the window still scrolls under the pointer.
                Transform p = transform.parent;
                if (p != null) ExecuteEvents.ExecuteHierarchy(p.gameObject, e, ExecuteEvents.scrollHandler);
            }

            private void OnDisable()
            {
                if (!Hover) return;   // deactivation bypasses OnPointerExit: never strand the tooltip
                Hover = false;
                if (Owner != null) Owner.HideTip();
            }
        }
    }
}
