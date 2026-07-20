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
    /// plus — for containers that pass vanilla's sortable gate — a small ASCII "SORT" button at the
    /// region's TOP-RIGHT, opposite the tab. Cell size is the live F10
    /// <see cref="UIAConfig.GridCellSize"/>, so the grid re-wraps responsively on a resize/slider change.
    /// The whole-inventory ROOT node renders NO own cells (no loose hand/helmet "top line") — only its
    /// child container regions.
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
        private const float SortW = 38f;         // per-region Sort control (top-right, opposite the tab)
        private const float SortH = 16f;

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

        // Instance-local pools (children of this view; surplus is deactivated, never destroyed here).
        private readonly List<BagGridCell> _cells = new List<BagGridCell>(8);
        private readonly List<GridRegionView> _childViews = new List<GridRegionView>(2);

        private ContainerNode _node;
        private int _depth;
        private bool _collapsed;
        private bool _isRoot;      // the whole-inventory root node (no Container): no tab, no border wrapper
        private int _activeCells;
        private int _activeChildren;
        private float _cellPx = -1f;   // last applied cell edge (so Layout re-sizes only when it moved)
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
        private int _sortStyleHash;
        private bool _sortStyledHover;
        private bool _sortStyled;

        /// <summary>The region's RectTransform — the parent view/panel positions this through it.</summary>
        public RectTransform Rect { get { return _rect; } }

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
            _sortClick.Owner = this;
            _sortLabel = HudText.Make(_sortRt, "SortLabel", HudText.Size(9f),
                TextAlignmentOptions.Center, warp: false);
            var slr = _sortLabel.rectTransform;
            slr.anchorMin = slr.anchorMax = new Vector2(0.5f, 0.5f);
            slr.pivot = new Vector2(0.5f, 0.5f);
            slr.anchoredPosition = Vector2.zero;
            slr.sizeDelta = new Vector2(SortW, SortH);
            HudText.Set(_sortLabel, "SORT");   // ASCII only: the game TMP font tofus non-Latin glyphs
            _sortGo.SetActive(false);
        }

        /// <summary>Structural (re)build: bind this view to <paramref name="node"/> at tree
        /// <paramref name="depth"/>, setting the tab content and renting cells for the node's own slots
        /// and child region-views for its nested storage containers (a collapsed region renders
        /// neither). Called by the panel only when the structural signature changed, so per-rebuild
        /// allocation is acceptable; steady state does not run this. After Bind, call
        /// <see cref="Layout"/> to place everything.</summary>
        public void Bind(ContainerNode node, int depth)
        {
            _node = node;
            _depth = depth;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            _isRoot = node != null && node.Container == null;
            _collapsed = !_isRoot && node != null && GridCollapseStore.IsCollapsed(node.RefId);

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
                // 4-arg overload: hand the tab the container's persistent ReferenceId. The 3-arg one
                // leaves GridTab.RefId at 0, and GridTab.BeginDragOut early-returns on RefId == 0 —
                // which made the whole drag-out-to-pin gesture unreachable dead code.
                _tab.Set(node.Title, icon, _collapsed, node.RefId);
            }

            // Sort control: only for a real container that passes vanilla's sortable gate.
            HudText.Sync(_sortLabel);
            _sortable = !_collapsed && !_isRoot && ContainerSortable(node);
            if (_sortGo.activeSelf != _sortable) _sortGo.SetActive(_sortable);

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

            // Hide the region box entirely when collapsed (just the tab shows), and always for the root.
            _regionGo.SetActive(!_collapsed && node != null && !_isRoot);

            // Keep the tab + Sort control drawing (and raycasting) ABOVE the cells + nested regions the
            // pool grew as later siblings.
            if (_sortable) _sortRt.SetAsLastSibling();
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
                // Tab sits above the region's top border, inset from the left corner like a folder tab.
                // Its natural (label-fitting) width, clamped so it never exceeds the region. (The Sort
                // control now lives INSIDE the box, so the tab band is the tab's alone.)
                float tabRoom = width - TabLeft * 2f;
                float tabW = Mathf.Min(_tab.PreferredWidth, Mathf.Max(1f, tabRoom));
                _tab.SetWidth(tabW);
                _tab.Rect.anchoredPosition = new Vector2(TabLeft + tabW * 0.5f, -_tab.Height * 0.5f); // centre pivot
                boxTop = _tab.Height;

                if (_sortable)
                {
                    // INSIDE the box's own top-right corner, just in from the border. The FIRST cell
                    // row reserves this control's footprint (see the cell loop), so seating it inside
                    // the box can never cover a cell even when that row runs the full width.
                    _sortRt.anchoredPosition = new Vector2(width - Border - 3f - SortW * 0.5f,
                                                           -(boxTop + Border + 3f + SortH * 0.5f)); // centre pivot
                    _sortRt.sizeDelta = new Vector2(SortW, SortH);
                    // No SetShape here: the corner sweep is part of the inherited box theme, so the
                    // ONE place it is issued is GridTheme.ApplyBox (from StyleRegion).
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
            bool hadContent = false;

            if (_activeCells > 0)
            {
                // Responsive: cols falls out of the LIVE content width, so a narrower window (or a
                // bigger F10 cell size) re-wraps the same slots without a tree rebuild.
                int cols = Mathf.Max(1, Mathf.FloorToInt((contentW + CellGap) / (cell + CellGap)));
                // The Sort control is seated INSIDE the box's top-right corner, so the FIRST row
                // reserves its footprint and wraps early; every later row uses the full width. That
                // keeps Sort in the corner of its own box without it ever covering a cell.
                int colsFirst = cols;
                if (_sortable)
                {
                    float firstRowW = contentW - (SortW + CellGap + 4f);
                    colsFirst = Mathf.Clamp(
                        Mathf.FloorToInt((firstRowW + CellGap) / (cell + CellGap)), 1, cols);
                }
                // Re-size the cells only when the F10 slider actually moved since the last pass, so a
                // resize-drag Relayout stays pure RectTransform writes (no mesh rebuilds).
                bool resize = cell != _cellPx;
                _cellPx = cell;
                for (int i = 0; i < _activeCells; i++)
                {
                    if (resize) _cells[i].SetSize(cell);
                    int row, col;
                    if (i < colsFirst) { row = 0; col = i; }
                    else { int j = i - colsFirst; row = 1 + j / cols; col = j % cols; }
                    // Centre-pivot cell: place its CENTRE (content-left corner + half a cell).
                    _cells[i].Rect.anchoredPosition = new Vector2(
                        innerLeft + col * (cell + CellGap) + cell * 0.5f,
                        -(y + row * (cell + CellGap)) - cell * 0.5f);
                }
                int rows = _activeCells <= colsFirst
                    ? 1
                    : 1 + (_activeCells - colsFirst + cols - 1) / cols;
                y += rows * (cell + CellGap) - CellGap;
                hadContent = true;
            }

            for (int i = 0; i < _activeChildren; i++)
            {
                if (hadContent) y += SectionGap;
                var cv = _childViews[i];
                float ch = cv.Layout(contentW - childIndent);
                cv.Rect.anchoredPosition = new Vector2(innerLeft + childIndent, -y);
                y += ch;
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
                // values. Its footprint is fixed by Layout, so w/h are the constants.
                bool sHover = _sortClick != null && _sortClick.Hover;
                if (!_sortStyled || _sortStyleHash != hash || _sortStyledHover != sHover)
                {
                    _sortStyled = true;
                    _sortStyleHash = hash;
                    _sortStyledHover = sHover;
                    GridTheme.ApplyBox(_sortBg, SortW, SortH, GridTheme.GridSurface.Button, sHover);
                    if (_sortLabel != null)
                        _sortLabel.color = sHover ? HudPalette.LineAccent.Value : HudPalette.TextLabel.Value;
                }
            }

            if (_regionBg == null || _collapsed || _node == null) return;
            if (_boxW <= 0f || _boxH <= 0f) return;   // pre-Layout (Bind) — the first Layout repaints
            if (_styleHash == hash && _styledW == _boxW && _styledH == _boxH && _styledDepth == _depth)
                return;
            _styleHash = hash;
            _styledW = _boxW;
            _styledH = _boxH;
            _styledDepth = _depth;

            // Depth is expressed ONLY as the helper's relative scales (a nested region's line is
            // 0.75x a top-level one's), never as an absolute width or a second colour.
            GridTheme.ApplyBox(_regionBg, _boxW, _boxH,
                _depth == 0 ? GridTheme.GridSurface.Region : GridTheme.GridSurface.RegionNested,
                false);

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
            _sortStyled = false;
            _sortable = false;
            if (_sortClick != null) _sortClick.Hover = false;
            if (_sortGo != null && _sortGo.activeSelf) _sortGo.SetActive(false);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Left-click surface for the region's Sort control. Records hover so the next
        /// <see cref="StyleRegion"/> promotes the accent, and forwards a LEFT click to
        /// <see cref="DoSort"/>. Instance-scoped (Owner back-reference) — no statics, nothing to unhook
        /// on teardown; the component dies with the panel's canvas on Shutdown.</summary>
        private sealed class RegionClickable : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public GridRegionView Owner;
            public bool Hover;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (Owner != null) Owner.DoSort();
            }

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }
        }
    }
}
