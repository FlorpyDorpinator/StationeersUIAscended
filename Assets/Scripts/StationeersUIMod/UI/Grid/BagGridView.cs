using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// Renders ONE <see cref="ContainerNode"/> as a collapsible section of The Grid: a header row
    /// (collapse chevron + container icon + title + used/total count + a Sort button shown only when
    /// the container is sortable) and, when expanded, a wrapped grid of <see cref="BagGridCell"/> for
    /// the node's own slots followed by indented child <see cref="BagGridView"/>s for its nested
    /// containers. The tree of views mirrors the tree of <see cref="ContainerNode"/>s.
    ///
    /// <para>Pooling is INSTANCE-LOCAL: each view keeps its cells and child-views as its own children,
    /// growing to a high-water mark and deactivating the surplus — there is NO static pool, so nothing
    /// is stranded across an F6 reload (the whole tree lives under <c>TheGridPanel</c>'s canvas, which
    /// its <c>Shutdown</c> destroys). This view holds no static state and subscribes to no events; the
    /// panel drives structure (<see cref="Bind"/>) and geometry (<see cref="Layout"/>) on structural
    /// change and content (<see cref="RefreshIfDirty"/>) each Tick.</para>
    ///
    /// <para>MP-safety: the only mutations reachable from here are (a) sort via
    /// <see cref="Slot.SortContents"/> (one server-authoritative message, occupant re-verified at
    /// execute time) and (b) click-to-hand, which lives entirely in <see cref="BagGridCell"/>. Reads
    /// are networked-only. Collapse state is local, per-save, via <see cref="GridCollapseStore"/>.</para>
    ///
    /// Stage-2 seams: the header has room for a pin button + per-window ✕ (drag a header out to a
    /// standalone pinned window); cells carry the drag/right-click seams. Neither is wired here.
    /// </summary>
    public sealed class BagGridView : MonoBehaviour
    {
        // Layout constants (px, at the canvas' reference resolution).
        private const float HeaderH = 24f;
        private const float HeaderRadius = 5f;
        private const float CellSize = 46f;
        private const float CellGap = 4f;
        private const float Indent = 14f;      // per-depth left inset for nested sections
        private const float Pad = 6f;          // inner top/bottom padding of the expanded body
        private const float SectionGap = 6f;   // gap between stacked child sections
        private const float SortW = 44f;
        private const float SortH = 18f;
        private const float CountW = 54f;
        private const float IconSize = 18f;

        private RectTransform _rect;

        // Header chrome (built once in Create, restyled every RefreshIfDirty).
        private RectTransform _headerRt;
        private PanelGraphic _headerBg;
        private GridClickable _headerClick;
        private TextMeshProUGUI _chevron;
        private Image _icon;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _count;
        private GameObject _sortGo;
        private RectTransform _sortRt;
        private PanelGraphic _sortBg;
        private GridClickable _sortClick;
        private TextMeshProUGUI _sortLabel;

        // Instance-local pools (children of this view; surplus is deactivated, never destroyed here).
        private readonly List<BagGridCell> _cells = new List<BagGridCell>(8);
        private readonly List<BagGridView> _childViews = new List<BagGridView>(2);

        private ContainerNode _node;
        private int _depth;
        private bool _collapsed;
        private bool _sortable;
        private int _activeCells;
        private int _activeChildren;

        /// <summary>The section's RectTransform — the parent view/panel positions this through it.</summary>
        public RectTransform Rect { get { return _rect; } }

        /// <summary>Build an idle section view under <paramref name="parent"/>. The header chrome is
        /// wired once here (collapse + sort actions are method groups, so a later <see cref="Bind"/>
        /// allocates no delegates); content and geometry come from Bind/Layout.</summary>
        public static BagGridView Create(Transform parent)
        {
            var go = new GameObject("BagGridView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            var view = go.AddComponent<BagGridView>();
            view._rect = rect;
            view.BuildHeader();
            go.SetActive(false);
            return view;
        }

        private void BuildHeader()
        {
            // Header GO owns the glass bg (raycast target) + the collapse click handler.
            var hGo = new GameObject("Header", typeof(RectTransform));
            hGo.transform.SetParent(_rect, false);
            _headerRt = (RectTransform)hGo.transform;
            _headerRt.anchorMin = _headerRt.anchorMax = new Vector2(0f, 1f);
            _headerRt.pivot = new Vector2(0.5f, 0.5f); // centre pivot: PanelGraphic draws centred on the rect origin
            _headerBg = hGo.AddComponent<PanelGraphic>();
            _headerBg.raycastTarget = true;
            _headerClick = hGo.AddComponent<GridClickable>();
            _headerClick.Clicked = ToggleCollapse;

            _chevron = HudText.Make(_headerRt, "Chevron", HudText.Size(13f),
                TextAlignmentOptions.Center, warp: false);
            LayoutChild(_chevron.rectTransform, new Vector2(2f, 0f), new Vector2(18f, HeaderH));

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(_headerRt, false);
            _icon = iconGo.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            LayoutChild(_icon.rectTransform, new Vector2(22f, -(HeaderH - IconSize) * 0.5f),
                new Vector2(IconSize, IconSize));

            _title = HudText.Make(_headerRt, "Title", HudText.Size(13f),
                TextAlignmentOptions.Left, warp: false);
            LayoutChild(_title.rectTransform, new Vector2(24f, 0f), new Vector2(120f, HeaderH));

            _count = HudText.Make(_headerRt, "Count", HudText.Size(11f),
                TextAlignmentOptions.Right, warp: false);
            LayoutChild(_count.rectTransform, new Vector2(0f, 0f), new Vector2(CountW, HeaderH));

            // Sort button — added last so it draws (and raycasts) above the header bg.
            _sortGo = new GameObject("Sort", typeof(RectTransform));
            _sortGo.transform.SetParent(_headerRt, false);
            _sortRt = (RectTransform)_sortGo.transform;
            _sortRt.anchorMin = _sortRt.anchorMax = new Vector2(0f, 1f);
            _sortRt.pivot = new Vector2(0.5f, 0.5f); // centre pivot (see header)
            _sortBg = _sortGo.AddComponent<PanelGraphic>();
            _sortBg.raycastTarget = true;
            _sortClick = _sortGo.AddComponent<GridClickable>();
            _sortClick.Clicked = DoSort;
            _sortLabel = HudText.Make(_sortRt, "SortLabel", HudText.Size(10f),
                TextAlignmentOptions.Center, warp: false);
            LayoutChild(_sortLabel.rectTransform, Vector2.zero, new Vector2(SortW, SortH));
            HudText.Set(_sortLabel, "SORT");
            _sortGo.SetActive(false);
        }

        private static void LayoutChild(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Structural (re)build: bind this view to <paramref name="node"/> at tree
        /// <paramref name="depth"/>, renting cells for its slots and child views for its containers
        /// (a collapsed section renders neither). Called by the panel only when the structural
        /// signature changed, so per-rebuild allocation is acceptable; steady state does not run this.
        /// After Bind, call <see cref="Layout"/> to place everything.</summary>
        public void Bind(ContainerNode node, int depth)
        {
            _node = node;
            _depth = depth;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            _collapsed = node != null && GridCollapseStore.IsCollapsed(node.RefId);

            HudText.Sync(_chevron); HudText.Sync(_title);
            HudText.Sync(_count); HudText.Sync(_sortLabel);

            HudText.Set(_title, node != null ? node.Title : "");
            HudText.Set(_count, node != null ? (node.UsedCount + "/" + node.TotalCount) : "");
            HudText.Set(_chevron, _collapsed ? "+" : "-"); // ASCII only: the game TMP font tofus arrows/dingbats

            Sprite icon = null;
            if (node != null && node.Container != null)
            {
                try { icon = node.Container.GetThumbnail(); } catch { }
            }
            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _icon.color = Color.white;

            _sortable = ContainerSortable(node);
            _sortGo.SetActive(_sortable);

            // Cells for this node's own slots (none while collapsed).
            int slotCount = (!_collapsed && node != null && node.Slots != null) ? node.Slots.Count : 0;
            EnsureCells(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                var c = _cells[i];
                PrepCell(c);
                c.Bind(node.Slots[i]);
                c.SetActiveHand(GridModel.IsActiveHand(node.Slots[i]));
            }
            for (int i = slotCount; i < _cells.Count; i++) _cells[i].Idle();
            _activeCells = slotCount;

            // Nested container sub-sections (none while collapsed).
            int childCount = (!_collapsed && node != null && node.Children != null) ? node.Children.Count : 0;
            EnsureChildViews(childCount);
            for (int i = 0; i < childCount; i++) _childViews[i].Bind(node.Children[i], depth + 1);
            for (int i = childCount; i < _childViews.Count; i++) _childViews[i].Recycle();
            _activeChildren = childCount;

            StyleHeader();
        }

        private void PrepCell(BagGridCell c)
        {
            // Anchor to the section's top-left for downward flow, but keep the CENTRE pivot:
            // PanelGraphic draws its box centred on the rect origin, so a top-left pivot offsets the
            // glass box half a cell from the (centre-anchored) icon. Anchor + pivot are independent —
            // top-left anchor for the flow math, centre pivot for the box; Layout places the centre.
            var r = c.Rect;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            c.SetSize(CellSize);
        }

        private void EnsureCells(int n)
        {
            while (_cells.Count < n) _cells.Add(BagGridCell.Create(_rect));
        }

        private void EnsureChildViews(int n)
        {
            while (_childViews.Count < n) _childViews.Add(Create(_rect));
        }

        /// <summary>Place the header, the wrapped cell grid, and the indented child sections within
        /// <paramref name="width"/> px; returns the total height used and sizes this view's rect to it.
        /// A collapsed (or null) section is just the header row. Recurses into active child views with
        /// an indented, narrower width. Cheap — pure RectTransform writes, no allocation.</summary>
        public float Layout(float width)
        {
            width = Mathf.Max(40f, width);

            _headerRt.anchoredPosition = new Vector2(width * 0.5f, -HeaderH * 0.5f); // centre of the top-left header rect
            _headerRt.sizeDelta = new Vector2(width, HeaderH);
            _headerBg.SetShape(width, HeaderH, HeaderRadius);

            // Sort button pins to the right edge; the count sits just left of it (or the edge).
            float countRight;
            if (_sortGo.activeSelf)
            {
                _sortRt.anchoredPosition = new Vector2(width - 4f - SortW * 0.5f, -HeaderH * 0.5f); // centre pivot
                _sortRt.sizeDelta = new Vector2(SortW, SortH);
                _sortBg.SetShape(SortW, SortH, 4f);
                countRight = width - SortW - 8f;
            }
            else
            {
                countRight = width - 6f;
            }
            _count.rectTransform.anchoredPosition = new Vector2(countRight - CountW, 0f);
            _count.rectTransform.sizeDelta = new Vector2(CountW, HeaderH);

            // Title takes the space between the icon/left edge and the count.
            float titleX = _icon.enabled ? 44f : 24f;
            float titleW = Mathf.Max(20f, (countRight - CountW) - titleX - 4f);
            _title.rectTransform.anchoredPosition = new Vector2(titleX, 0f);
            _title.rectTransform.sizeDelta = new Vector2(titleW, HeaderH);

            float y = HeaderH;
            if (_collapsed || _node == null)
            {
                _rect.sizeDelta = new Vector2(width, y);
                return y;
            }

            y += Pad;

            if (_activeCells > 0)
            {
                int cols = Mathf.Max(1, Mathf.FloorToInt((width + CellGap) / (CellSize + CellGap)));
                for (int i = 0; i < _activeCells; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    // Centre-pivot cell: place its CENTRE (top-left corner + half a cell).
                    _cells[i].Rect.anchoredPosition = new Vector2(
                        col * (CellSize + CellGap) + CellSize * 0.5f,
                        -(y + row * (CellSize + CellGap)) - CellSize * 0.5f);
                }
                int rows = (_activeCells + cols - 1) / cols;
                y += rows * (CellSize + CellGap) - CellGap;
            }

            for (int i = 0; i < _activeChildren; i++)
            {
                y += SectionGap;
                var cv = _childViews[i];
                float ch = cv.Layout(width - Indent);
                cv.Rect.anchoredPosition = new Vector2(Indent, -y);
                y += ch;
            }

            y += Pad;
            _rect.sizeDelta = new Vector2(width, y);
            return y;
        }

        /// <summary>Per-frame content refresh: restyle the header to the live palette, then refresh
        /// visible cells (icon/text dirty key + active-hand) and recurse into visible child sections.
        /// Collapsed sections stop at the header. Steady-state allocation-free.</summary>
        public void RefreshIfDirty()
        {
            if (_node == null) return;
            StyleHeader();
            if (_collapsed) return;
            for (int i = 0; i < _activeCells; i++)
            {
                var c = _cells[i];
                c.SetActiveHand(GridModel.IsActiveHand(c.Slot));
                c.RefreshIfDirty();
            }
            for (int i = 0; i < _activeChildren; i++) _childViews[i].RefreshIfDirty();
        }

        /// <summary>Apply HUD palette colours to the header chrome (fill, border, text, sort button).
        /// Depth-graded: the root section carries the bright line accent, nested sections a thinner
        /// steel border. Hover promotes the accent. Every setter here is dirty-guarded downstream.</summary>
        private void StyleHeader()
        {
            if (_headerBg == null) return;

            _headerBg.color = HudPalette.PanelFill.Value;
            bool hHover = _headerClick != null && _headerClick.Hover;
            _headerBg.BorderColor = hHover
                ? HudPalette.LineAccent.Value
                : (_depth == 0 ? HudPalette.LineAccent.Value : HudPalette.PanelBorder.Value);
            _headerBg.BorderWidth = _depth == 0 ? 1.4f : 1.0f;

            // Same glass family the HUD panels use: when the effect bundle loaded, carry the
            // shared "glass" material on the header fill (per-element strength rides uv0.x).
            // Assign is idempotent + per-frame safe (dictionary lookup when already assigned) and
            // self-enforces the IHudFxGraphic/Mask scoping contract; material lifetime + hot-reload
            // teardown are owned entirely by HudFxMaterials.Shutdown, so no static state lands here.
            if (HudFxMaterials.Available)
            {
                _headerBg.FxStrength = 1f;
                if (!HudFxMaterials.Assign(_headerBg, "glass")) HudFxMaterials.Unassign(_headerBg);
            }
            else HudFxMaterials.Unassign(_headerBg);

            if (_chevron != null) _chevron.color = HudPalette.TextLabel.Value;
            if (_title != null) _title.color = HudPalette.TextValue.Value;
            if (_count != null) _count.color = HudPalette.TextDim.Value;

            if (_sortGo.activeSelf)
            {
                bool sHover = _sortClick != null && _sortClick.Hover;
                _sortBg.color = HudPalette.PanelFill.Value;
                _sortBg.BorderColor = sHover ? HudPalette.LineAccent.Value : HudPalette.PanelBorder.Value;
                _sortBg.BorderWidth = 1.0f;
                if (_sortLabel != null)
                    _sortLabel.color = sHover ? HudPalette.LineAccent.Value : HudPalette.TextLabel.Value;
            }
        }

        // ---- header actions (wired once in Create) ----

        /// <summary>Toggle this container's collapse state. It is folded into
        /// <see cref="GridModel.ComputeSignature"/>, so the panel's next Tick sees the signature
        /// change and rebuilds the tree — no direct relayout call needed from here.</summary>
        private void ToggleCollapse()
        {
            if (_node == null) return;
            GridCollapseStore.ToggleCollapsed(_node.RefId);
        }

        /// <summary>Sort the container's contents through the mutation funnel —
        /// <see cref="ItemActions.SortContainer"/> (one server-authoritative message via
        /// <c>Slot.SortContents</c>). Re-verify the SAME container still occupies the slot at
        /// execute time (MP: a teammate may have moved it between build and click) before handing
        /// off; ItemActions re-checks carry/ParentSlot itself. No client-side reordering.</summary>
        private void DoSort()
        {
            if (_node == null || _node.ParentSlot == null) return;
            DynamicThing occ = null;
            try { occ = _node.ParentSlot.Get(); } catch { }
            if (occ == null || occ != _node.Container) return; // occupant changed → no-op, rebuild refreshes
            ItemActions.SortContainer(_node.Container);
        }

        /// <summary>True when the container in this node has ≥2 sortable inner slots — the exact gate
        /// vanilla's <c>InventoryWindow.IsSortable(parentSlot)</c> uses. The worn-slot ROOT (no
        /// ParentSlot) is never a sortable container.</summary>
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

        /// <summary>Return this section (and its whole subtree of cells and child views) to the idle
        /// pool: deactivate everything, drop the node reference. Objects stay parented under this view
        /// (destroyed with the canvas on Shutdown), so nothing is stranded across a hot reload. No
        /// statics, no event subscriptions to unhook.</summary>
        public void Recycle()
        {
            _node = null;
            _collapsed = false;
            _sortable = false;
            for (int i = 0; i < _cells.Count; i++) _cells[i].Idle();
            _activeCells = 0;
            for (int i = 0; i < _childViews.Count; i++) _childViews[i].Recycle();
            _activeChildren = 0;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>A left-click surface for the header and the Sort button. Records hover so the view
        /// can promote the accent on the next restyle (the panel restyles every Tick, so feedback lands
        /// within a frame). The click action is a method group set once at Create — no per-rebuild
        /// delegate churn, nothing to unsubscribe.
        ///
        /// Stage-2 seam: a pin button / per-window ✕ would be additional GridClickables in the header.</summary>
        private sealed class GridClickable : MonoBehaviour,
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
    }
}
