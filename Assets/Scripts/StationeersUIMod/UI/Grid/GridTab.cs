using System.Collections.Generic;
using BepInEx.Configuration;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The trapezoidal manila "file-folder" tab that labels one storage region in The Grid's Grid
    /// mode: a glass <see cref="PolygonPanelGraphic"/> whose contour is a 4-point trapezoid (wide
    /// bottom edge sitting along the region's top border, narrower top edge via a slant inset),
    /// carrying a small container <see cref="Image"/> icon, an auto-sized ASCII name, and a drawn
    /// collapse chevron (<see cref="TriangleGraphic"/> — never a TMP dingbat). Tapping anywhere on
    /// the tab is the region's collapse/expand toggle.
    ///
    /// <para>One tab is owned by one <c>GridRegionView</c> (no static pool). The region sets content
    /// via <see cref="Set"/>, reads <see cref="PreferredWidth"/> / <see cref="Height"/> to lay the
    /// region out, then locks the final width with <see cref="SetWidth"/> (which rebuilds the
    /// trapezoid contour and repositions icon/name/chevron). <see cref="RefreshStyle"/> re-applies
    /// the live palette + hover accent each Tick.</para>
    ///
    /// <para>The tab is also the PIN gesture's handle: dragging it lifts a small ghost of the tab under
    /// the cursor, and RELEASING OUTSIDE the Universal Inventory panel rect tears that container out into
    /// its own <see cref="PinnedInventoryWindow"/> (the pin itself is raised through
    /// <see cref="DragOutRequested"/>, which <c>TheGridPanel</c> owns). Releasing INSIDE the panel is
    /// treated as a normal click — the collapse toggle — re-issued by <see cref="EndDragOut"/>, since a
    /// release even slightly off the tab rect produces no OnPointerClick at all. The click handler
    /// therefore ignores any pointer with <c>dragging</c> set, or the two paths would toggle collapse
    /// twice and cancel out. Pinning mutates NO game state; this file raises an intent and nothing else.</para>
    ///
    /// <para>Center-pivot rule: the polygon graphic draws its mesh CENTRED on the rect origin, so this
    /// rect keeps a centre pivot (0.5,0.5) and the contour points are authored symmetric about (0,0);
    /// the owning region positions the tab by its CENTRE. The two statics (<see cref="PanelBounds"/>,
    /// <see cref="DragOutRequested"/>) are the ONLY ones and are cleared by <see cref="ResetStatics"/>
    /// from The Grid's teardown; everything else is destroyed with The Grid's canvas on hot reload,
    /// stranding nothing. The click action is a public field the region wires once (no per-rebuild
    /// delegate churn).</para>
    /// </summary>
    public sealed class GridTab : MonoBehaviour
    {
        // Layout constants (px, at the canvas' reference resolution).
        private const float TabH = 20f;        // tab height
        private const float Slant = 9f;        // horizontal inset of the top edge (per side) → the manila slant
        private const float PadX = 7f;         // inner horizontal padding inside the top (narrow) span
        private const float IconSize = 14f;
        private const float Gap = 4f;          // gap between icon / name / chevron
        private const float ChevW = 11f;       // chevron cell width
        private const float ChevSize = 9f;     // triangle size
        private const float NameMaxW = 220f;   // clamp so a very long name can't blow the tab out
        private const float MinW = 46f;

        private RectTransform _rect;
        private PolygonPanelGraphic _bg;
        private GridClickable _click;
        private Image _icon;
        private TextMeshProUGUI _name;
        private RectTransform _chevRt;
        private TriangleGraphic _chev;

        // Trapezoid contour scratch (instance-local; rebuilt only on SetWidth, not per frame).
        private readonly List<Vector2> _pts = new List<Vector2>(4);

        private float _preferredWidth = MinW;
        private bool _collapsed;

        // ---- drag-out (pin) gesture state; all instance-local, torn down in Idle/EndDrag ----
        private bool _dragging;
        private bool _outside;            // pointer is currently past the Universal Inventory panel rect
        private RectTransform _bounds;    // panel rect resolved ONCE per gesture (never per drag frame)
        private GameObject _ghostGo;      // the little tab ghost following the cursor
        private RectTransform _ghostRt;
        /// <summary>Sorting order for the drag ghost's own nested canvas. Must beat every window it can
        /// fly over: TheGridPanel is 5020 and <see cref="PinnedInventoryWindow"/> is 5030, and the ghost
        /// is exactly the affordance for creating another 5030 window, so it sits above both.</summary>
        private const int GhostSortingOrder = 5100;
        private PanelGraphic _ghostBg;
        private Image _ghostIcon;
        private TextMeshProUGUI _ghostName;

        /// <summary>Left-click action — the owning region sets this to its collapse toggle. A method
        /// group, so wiring it once at bind time allocates no delegate.</summary>
        public System.Action Clicked;

        /// <summary>The container this tab labels (<c>ContainerNode.RefId</c> — the persistent
        /// <c>Thing.ReferenceId</c>). Set by the owning region through the 4-argument
        /// <see cref="Set(string,Sprite,bool,long)"/>; 0 means "identity unknown", which disables the
        /// drag-out gesture entirely (a drag then simply cancels — it never collapses by surprise).</summary>
        public long RefId;

        /// <summary>The region view that owns this tab, resolved once at build. Handed to
        /// <see cref="DragOutRequested"/> so the handler can reach the live view without a lookup.</summary>
        public GridRegionView OwnerRegion { get { return _ownerRegion; } }
        private GridRegionView _ownerRegion;

        /// <summary>OPTIONAL override for the rect a release is tested against — the Universal Inventory
        /// panel. When null the tab derives it from its own hierarchy (the ScrollRect viewport's parent,
        /// which is the window panel in both <c>TheGridPanel</c> and <see cref="PinnedInventoryWindow"/>),
        /// so the gesture works without any wiring. Cleared by <see cref="ResetStatics"/>.</summary>
        public static RectTransform PanelBounds;

        /// <summary>Raised when a tab is released OUTSIDE the Universal Inventory panel: "pin this
        /// container, at this SCREEN point". <c>TheGridPanel</c> assigns it once and is responsible for
        /// <see cref="GridPinStore"/> + spawning the <see cref="PinnedInventoryWindow"/>; this file
        /// mutates nothing. Null = drag-out disabled (the gesture cancels). Cleared by
        /// <see cref="ResetStatics"/>.</summary>
        public static System.Action<GridTab, Vector2> DragOutRequested;

        /// <summary>Hot-reload teardown: drop both statics so a reloaded assembly can never be called
        /// through a stale delegate or hold a destroyed rect. Call from <c>TheGridPanel.Shutdown</c>.</summary>
        public static void ResetStatics()
        {
            PanelBounds = null;
            DragOutRequested = null;
        }

        /// <summary>The tab's RectTransform (the region positions/sizes the tab through this).</summary>
        public RectTransform Rect { get { return _rect; } }

        /// <summary>The tab's fixed height (px) — the region reserves this above the region border.</summary>
        public float Height { get { return TabH; } }

        /// <summary>The natural width (px) that fits the icon, name and chevron — computed in
        /// <see cref="Set"/>. The region uses it to size the region no narrower than its label.</summary>
        public float PreferredWidth { get { return _preferredWidth; } }

        /// <summary>Build an idle tab under <paramref name="parent"/>. The polygon is the raycast
        /// target (icon/name/chevron are inert), so pointer events land on the click handler.</summary>
        public static GridTab Create(Transform parent)
        {
            var go = new GameObject("GridTab", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            // Top-left anchor for the region's downward/leftward flow math; CENTRE pivot because the
            // polygon draws centred on the rect origin (top-left pivot would offset it half its size).
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var tab = go.AddComponent<GridTab>();
            tab._rect = rect;
            // The region component already exists when it builds its tab, so this resolves now and
            // never has to run again (no per-frame GetComponentInParent).
            tab._ownerRegion = go.GetComponentInParent<GridRegionView>();

            tab._bg = go.AddComponent<PolygonPanelGraphic>();
            tab._bg.raycastTarget = true;
            // Manila folder look: border on top + the two slanted sides, open at the bottom where the
            // tab meets the region's own top border (1=T,2=R,4=B,8=L → omit B). Approximate on a
            // freeform contour (resolved per-vertex normal quadrant), which is exactly what we want.
            tab._bg.BorderSides = 1 | 2 | 8;

            tab._click = go.AddComponent<GridClickable>();
            tab._click.Owner = tab;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(rect, false);
            tab._icon = iconGo.AddComponent<Image>();
            tab._icon.raycastTarget = false;
            tab._icon.preserveAspect = true;
            CentreChild(tab._icon.rectTransform);

            tab._name = HudText.Make(rect, "Name", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            CentreChild(tab._name.rectTransform);

            var chevGo = new GameObject("Chevron", typeof(RectTransform));
            chevGo.transform.SetParent(rect, false);
            tab._chevRt = (RectTransform)chevGo.transform;
            CentreChild(tab._chevRt);
            tab._chev = chevGo.AddComponent<TriangleGraphic>();
            tab._chev.raycastTarget = false;

            go.SetActive(false);
            return tab;
        }

        private static void CentreChild(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>Bind the tab's content: container <paramref name="name"/> (ASCII already), its
        /// <paramref name="icon"/> thumbnail (or none), and whether the region is currently
        /// <paramref name="collapsed"/> (drives the chevron direction). Measures the name to compute
        /// <see cref="PreferredWidth"/> and lays the tab out at that width; the region may override
        /// via <see cref="SetWidth"/>. Structural-only (called on rebuild), so measuring here is fine.</summary>
        public void Set(string name, Sprite icon, bool collapsed)
        {
            SetCore(name, icon, collapsed);
        }

        /// <summary>As <see cref="Set(string,Sprite,bool)"/>, plus the container's <c>ContainerNode.RefId</c>
        /// — the identity the drag-out (pin) gesture raises. Prefer this overload: a tab bound through the
        /// 3-argument one keeps <see cref="RefId"/> at 0 and can therefore only collapse, never pin.</summary>
        public void Set(string name, Sprite icon, bool collapsed, long refId)
        {
            RefId = refId;
            SetCore(name, icon, collapsed);
        }

        private void SetCore(string name, Sprite icon, bool collapsed)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            _collapsed = collapsed;

            HudText.Sync(_name);
            HudText.Set(_name, name ?? "");

            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _icon.color = Color.white;

            // Collapsed → chevron points RIGHT (region hidden), expanded → points DOWN (region open).
            // TriangleGraphic only draws up/down, so rotate its rect for the right-pointing state.
            _chev.Configure(false, ChevSize);
            _chevRt.localRotation = collapsed ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;

            float nameW = 0f;
            if (!string.IsNullOrEmpty(name))
            {
                float m = _name.GetPreferredValues(name).x;
                if (!float.IsNaN(m) && !float.IsInfinity(m)) nameW = Mathf.Min(m, NameMaxW);
            }

            // 2 slants + 2 pads bound the usable (narrow) top span; inside it: icon, gap, name, gap, chevron.
            _preferredWidth = Mathf.Max(MinW,
                2f * Slant + 2f * PadX + IconSize + Gap + nameW + Gap + ChevW);

            SetWidth(_preferredWidth);
            RefreshStyle();
        }

        /// <summary>Set the tab's width, rebuild the trapezoid contour to fit, and reposition the icon,
        /// name and chevron within the narrow top span. Centre-pivot: the contour is symmetric about
        /// (0,0) and children are placed by their centre offsets from it. Cheap — 4 contour points plus
        /// pure RectTransform writes; only re-meshes the polygon when the width actually changed.</summary>
        public void SetWidth(float width)
        {
            width = Mathf.Max(MinW, width);
            float half = width * 0.5f;
            float hy = TabH * 0.5f;

            _rect.sizeDelta = new Vector2(width, TabH);

            // CCW trapezoid, centred on the rect origin: wide bottom edge, narrower top edge (slant in).
            _pts.Clear();
            _pts.Add(new Vector2(-half, -hy));           // bottom-left
            _pts.Add(new Vector2(half, -hy));            // bottom-right
            _pts.Add(new Vector2(half - Slant, hy));     // top-right
            _pts.Add(new Vector2(-half + Slant, hy));    // top-left
            _bg.SetPoints(_pts, null, null, 0, 2);

            // Content lives inside the narrow top span: [pad][icon][gap][name..][gap][chevron][pad].
            float leftInner = -half + Slant + PadX;
            float rightInner = half - Slant - PadX;

            float iconX = leftInner + IconSize * 0.5f;
            _icon.rectTransform.anchoredPosition = new Vector2(iconX, 0f);
            _icon.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);

            float chevX = rightInner - ChevW * 0.5f;
            _chevRt.anchoredPosition = new Vector2(chevX, 0f);
            _chevRt.sizeDelta = new Vector2(ChevW, TabH);

            float nameLeft = iconX + IconSize * 0.5f + Gap;
            float nameRight = chevX - ChevW * 0.5f - Gap;
            float nameW = Mathf.Max(4f, nameRight - nameLeft);
            _name.rectTransform.anchoredPosition = new Vector2((nameLeft + nameRight) * 0.5f, 0f);
            _name.rectTransform.sizeDelta = new Vector2(nameW, TabH);
        }

        /// <summary>Re-apply the live theme to the tab chrome (glass fill, border, name, chevron) and
        /// promote the accent on hover. EVERYTHING style-ish comes from <see cref="GridTheme"/>, which
        /// Follows the F9 palette + glass globals unless overridden — no absolute width or radius is
        /// authored here, so a thin global border stays thin on a tab, hovered or not. Runs every Tick
        /// from the region; every setter is dirty-guarded downstream, so an unchanged tab costs no mesh
        /// rebuild.</summary>
        public void RefreshStyle()
        {
            if (_bg == null) return;

            bool hover = (_click != null && _click.Hover) || _dragging;

            ApplyThemeBox(_bg, hover);

            // While the tab is being dragged out, fade the seated tab so the ghost under the cursor
            // reads as "this label is in flight" (the same dim idiom BagGridCell uses for its source).
            // A DELTA on the inherited fill, applied after the theme wrote it.
            if (_dragging)
            {
                Color fill = _bg.color;
                fill.a *= DragFadeAlpha;
                _bg.color = fill;
            }

            if (_name != null) _name.color = hover ? GridTheme.Text : HudPalette.TextLabel.Value;
            if (_chev != null) _chev.color = hover ? HudPalette.LineAccent.Value : HudPalette.TextLabel.Value;
        }

        // ---- theme plumbing -------------------------------------------------------------------
        //
        // GridTheme.ApplyBox is the ONE styling path for every Grid box, but its parameter is the
        // concrete PanelGraphic (it calls SetShape, which only a rect has). The tab's chrome is a
        // PolygonPanelGraphic — a sibling implementer of IGlassSurface, not a subclass — so it cannot
        // go through that overload. ApplyThemeBox below is its trapezoid twin: it resolves the SAME
        // values from the SAME theme getters (Fill / Border / BorderWidth / Sheen / Spec) and applies
        // the same INTERIOR glass stack HudGlobalGlass.Apply(includeGlow:false, wantFrost:false,
        // wantTierB:true) gives an inner HUD box — the shape step is simply absent, because a freeform
        // contour carries no corner radius (the trapezoid's silhouette IS its shape). If GridTheme ever
        // grows an IGlassSurface overload, delete this and call it.

        /// <summary>Border-width scale for a tab, mirroring <c>GridTheme.WidthScale(GridSurface.Tab)</c>.
        /// Relative to the inherited global width — never an absolute line thickness.</summary>
        private const float TabWidthScale = 0.8f;

        /// <summary>Hover/drag line delta, mirroring <c>GridTheme.HoverWidthScale</c>.</summary>
        private const float HoverWidthScale = 1.25f;

        /// <summary>How far the seated tab's inherited fill alpha drops while its ghost is in flight.</summary>
        private const float DragFadeAlpha = 0.35f;

        private static float Cfg(ConfigEntry<float> e, float d) { return e != null ? e.Value : d; }
        private static bool On(ConfigEntry<bool> e) { return e != null && e.Value; }

        /// <summary>Paint a tab's trapezoid from <see cref="GridTheme"/>: inherited fill, border colour
        /// (accent when <paramref name="hover"/>), inherited border WIDTH scaled per surface and by the
        /// hover delta, plus the interior glass stack. Per-frame safe and allocation-free — every setter
        /// is dirty-guarded and <c>HudFxMaterials.Assign</c> is idempotent.</summary>
        private static void ApplyThemeBox(PolygonPanelGraphic bg, bool hover)
        {
            if (bg == null) return;

            float width = Mathf.Max(0f, GridTheme.BorderWidth) * TabWidthScale;
            if (hover) width *= HoverWidthScale;

            bg.color = GridTheme.Fill;
            bg.BorderColor = hover
                ? (HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : GridTheme.Border)
                : GridTheme.Border;
            bg.BorderWidth = width;

            ApplyInteriorGlass(bg);
        }

        /// <summary>The interior-surface half of the HUD's global glass stack, for an IGlassSurface the
        /// PanelGraphic-typed <c>HudGlobalGlass.Apply</c> cannot take. Sheen/edge light come from the
        /// theme (so a Grid override still wins) and keep Tier A's edge-light boost; the glow halo is
        /// reserved for the window shell and frost is a shell-only opt-in, exactly as
        /// <c>GridTheme.ApplyBox</c> resolves them for an inner box.</summary>
        private static void ApplyInteriorGlass(PolygonPanelGraphic g)
        {
            if (g == null) return;
            bool tierA = On(HudConfig.FxTierA);
            bool edge = tierA && On(HudConfig.FxEdgeLightOn);

            float spec = GridTheme.Spec;
            if (edge && Cfg(HudConfig.FxEdgeLight, 0f) > 0f)
                spec = Mathf.Clamp01(spec + HudConfig.FxEdgeLight.Value * 0.45f);

            g.Sheen = GridTheme.Sheen;
            g.Spec = spec;
            g.FeatherOverride = -1f;   // -1 = the global EdgeFeather

            g.BorderFade = tierA && On(HudConfig.FxBorderFadeOn) ? Cfg(HudConfig.FxBorderFade, 0f) : 0f;
            g.SoftEdge = tierA && On(HudConfig.FxSoftEdgeOn) ? Cfg(HudConfig.FxSoftEdge, 0f) : 0f;

            g.Glow = 0f;               // interior surface: no halo
            g.GlowInner = 0f;

            g.EdgeRipple = edge ? Cfg(HudConfig.FxEdgeRipple, 0f) : 0f;
            g.EdgeRippleFreq = Cfg(HudConfig.FxEdgeRippleFreq, 2f);
            g.RippleSmooth = 0f;       // global forces the un-smoothed ripple

            bool tierB = On(HudConfig.FxTierB) && Core.HudShaderStore.TierBAvailable;
            bool shineOn = tierB && On(HudConfig.FxShineOn) && Cfg(HudConfig.FxShine, 0f) > 0.001f;
            bool iridOn = tierB && On(HudConfig.FxIridOn) && Cfg(HudConfig.FxIridescence, 0f) > 0.001f;

            if (HudFxMaterials.Available && (shineOn || iridOn))
            {
                g.FxStrength = 1f;
                if (!HudFxMaterials.Assign(g, "edgefx")) HudFxMaterials.Unassign(g);
            }
            else
            {
                g.FxStrength = 0f;
                HudFxMaterials.Unassign(g);
            }
        }

        // ---- drag-out (pin) gesture ----

        /// <summary>Begin a tab drag: only with the LEFT button and only while the window is latched
        /// (interactive), and only when this tab knows which container it labels (<see cref="RefId"/>)
        /// and someone is listening for the pin — otherwise the gesture never engages and UGUI's normal
        /// click path is left untouched. Spawns the ghost and resolves the panel rect ONCE.</summary>
        private void BeginDragOut(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            if (_dragging) return;
            if (RefId == 0L || DragOutRequested == null) return;
            if (!TheGridPanel.IsInteractive) return;

            _dragging = true;
            _bounds = ResolveBounds();
            _outside = false;
            SpawnGhost(e);
            RefreshStyle();
        }

        /// <summary>Follow the cursor with the ghost and re-test inside/outside the Universal Inventory
        /// panel, tinting the ghost accent once the release would PIN. Pure visuals — no allocation, no
        /// mutation, and no raycast (a rect containment test, unlike the cell drag's target pick).</summary>
        private void DragOut(PointerEventData e)
        {
            if (!_dragging || e == null) return;
            if (_ghostRt != null) _ghostRt.position = new Vector3(e.position.x, e.position.y, 0f);

            bool outside = IsOutsidePanel(e);
            if (outside != _outside)
            {
                _outside = outside;
                StyleGhost();
            }
        }

        /// <summary>Resolve the gesture. Released OUTSIDE the Universal Inventory panel rect → raise
        /// <see cref="DragOutRequested"/> (pin this container at the cursor); released INSIDE → treat it
        /// as the normal click, i.e. the collapse toggle, because a release even slightly off the tab rect
        /// sends no OnPointerClick and the toggle would be silently lost. (When a click IS also delivered
        /// it is suppressed there by the <c>e.dragging</c> guard, so the toggle fires exactly once.)
        /// A drag that cannot pin
        /// (identity unknown / no listener) never engaged, so it lands nowhere near here. The ghost is
        /// always torn down, including on an aborted gesture.</summary>
        private void EndDragOut(PointerEventData e)
        {
            if (!_dragging) { DestroyGhost(); return; }
            _dragging = false;

            bool outside = e != null && IsOutsidePanel(e);
            Vector2 at = e != null ? e.position : Vector2.zero;
            _bounds = null;
            _outside = false;
            DestroyGhost();
            RefreshStyle();

            if (!outside)
            {
                if (Clicked != null) Clicked();     // released inside: this was a click after all
                return;
            }
            var handler = DragOutRequested;         // copy: the handler may rebuild/recycle this tab
            if (handler != null && RefId != 0L) handler(this, at);
        }

        /// <summary>True when the pointer sits outside the window panel this tab lives in. With no rect
        /// to test against we answer FALSE (= "inside"), so an unresolvable gesture degrades to the
        /// harmless collapse toggle rather than pinning at a guessed position. The canvas is a
        /// screen-space OVERLAY, so <see cref="PointerEventData.pressEventCamera"/> is null, which is
        /// exactly what <see cref="RectTransformUtility.RectangleContainsScreenPoint"/> wants.</summary>
        private bool IsOutsidePanel(PointerEventData e)
        {
            RectTransform r = _bounds != null ? _bounds : ResolveBounds();
            if (r == null) return false;
            return !RectTransformUtility.RectangleContainsScreenPoint(r, e.position, e.pressEventCamera);
        }

        /// <summary>The rect a release is tested against: the explicit <see cref="PanelBounds"/> override
        /// if set, else this tab's own window panel — the parent of the ScrollRect viewport, which is how
        /// both <c>TheGridPanel</c> and <see cref="PinnedInventoryWindow"/> build their body. Resolved
        /// once per gesture, never per drag frame.</summary>
        private RectTransform ResolveBounds()
        {
            if (PanelBounds != null) return PanelBounds;
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.viewport != null)
            {
                var panel = scroll.viewport.parent as RectTransform;
                if (panel != null) return panel;
            }
            return null;
        }

        /// <summary>Spawn the drag affordance: a small rounded glass copy of this tab (its icon + ASCII
        /// name) parented to the ROOT canvas transform — not to this tab — so the RectMask2D of the
        /// scroll viewport cannot clip it as the cursor leaves the window. It carries its OWN nested
        /// Canvas with <c>overrideSorting</c> at <see cref="GhostSortingOrder"/>: sibling order alone
        /// only sorts it within its host canvas, so it would otherwise fly UNDERNEATH already-pinned
        /// windows (a higher-sortingOrder canvas) exactly when the user is dragging out another one.
        /// No GraphicRaycaster is added — the ghost is non-raycast, so it never occludes anything under
        /// the pointer. One allocation per user gesture, never per frame.</summary>
        private void SpawnGhost(PointerEventData e)
        {
            DestroyGhost();
            Canvas canvas = _bg != null ? _bg.canvas : null;
            Transform host = canvas != null ? canvas.rootCanvas.transform : null;
            if (host == null) return;

            float w = Mathf.Max(MinW, _rect != null ? _rect.sizeDelta.x : _preferredWidth);

            _ghostGo = new GameObject("GridTabDragGhost", typeof(RectTransform));
            _ghostGo.transform.SetParent(host, false);
            _ghostRt = (RectTransform)_ghostGo.transform;
            _ghostRt.anchorMin = _ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRt.pivot = new Vector2(0.5f, 0.5f);   // centre pivot: PanelGraphic draws about the origin
            _ghostRt.sizeDelta = new Vector2(w, TabH);

            // Own nested canvas so the ghost outranks pinned windows (5030), not just its siblings.
            var gc = _ghostGo.AddComponent<Canvas>();
            gc.overrideSorting = true;
            gc.sortingOrder = GhostSortingOrder;

            // Shape + style are the theme's job (StyleGhost -> GridTheme.ApplyBox, which calls
            // SetShape with the inherited corner radius); nothing is authored here.
            _ghostBg = _ghostGo.AddComponent<PanelGraphic>();
            _ghostBg.raycastTarget = false;

            float leftInner = -w * 0.5f + Slant + PadX;
            float rightInner = w * 0.5f - Slant - PadX;

            var giGo = new GameObject("Icon", typeof(RectTransform));
            giGo.transform.SetParent(_ghostRt, false);
            _ghostIcon = giGo.AddComponent<Image>();
            _ghostIcon.raycastTarget = false;
            _ghostIcon.preserveAspect = true;
            _ghostIcon.sprite = _icon != null ? _icon.sprite : null;
            _ghostIcon.enabled = _ghostIcon.sprite != null;
            CentreChild(_ghostIcon.rectTransform);
            float iconX = leftInner + IconSize * 0.5f;
            _ghostIcon.rectTransform.anchoredPosition = new Vector2(iconX, 0f);
            _ghostIcon.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);

            // ASCII already (the region hands the tab an ASCII container name); copied verbatim from
            // the live label so the ghost can never introduce a glyph the game's TMP font tofus.
            _ghostName = HudText.Make(_ghostRt, "Name", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            CentreChild(_ghostName.rectTransform);
            float nameLeft = iconX + IconSize * 0.5f + Gap;
            _ghostName.rectTransform.anchoredPosition =
                new Vector2((nameLeft + rightInner) * 0.5f, 0f);
            _ghostName.rectTransform.sizeDelta =
                new Vector2(Mathf.Max(4f, rightInner - nameLeft), TabH);
            HudText.Sync(_ghostName);
            HudText.Set(_ghostName, _name != null ? _name.text : "");

            _ghostRt.SetAsLastSibling();   // last within the host canvas; the nested canvas does the rest
            _ghostRt.position = new Vector3(e.position.x, e.position.y, 0f);
            StyleGhost();
        }

        /// <summary>Style the ghost through <see cref="GridTheme"/> — it is a plain rect, so it takes the
        /// shared <c>ApplyBox</c> path verbatim (inherited fill, border colour, border WIDTH, corner
        /// radius and the interior glass stack, all following the F9 globals unless overridden). The
        /// PIN-imminent state (pointer outside the panel) is expressed as the theme's own hover delta —
        /// the accent border and the scaled line — never an absolute width. The one authored value is
        /// the fill's opacity LIFT, a delta on the inherited alpha, so the ghost reads as detached from
        /// the surface it is flying over.</summary>
        private void StyleGhost()
        {
            if (_ghostBg == null) return;
            float w = _ghostRt != null ? _ghostRt.sizeDelta.x : _preferredWidth;
            float h = _ghostRt != null ? _ghostRt.sizeDelta.y : TabH;
            GridTheme.ApplyBox(_ghostBg, w, h, GridTheme.GridSurface.TabGhost, _outside);

            Color fill = _ghostBg.color;
            fill.a = Mathf.Min(1f, fill.a * GhostAlphaScale + GhostAlphaLift);
            _ghostBg.color = fill;

            if (_ghostIcon != null)
                _ghostIcon.color = new Color(1f, 1f, 1f, _outside ? 1f : 0.75f);
            if (_ghostName != null)
                _ghostName.color = _outside ? GridTheme.Text : HudPalette.TextLabel.Value;
        }

        /// <summary>The ghost's opacity delta on the inherited fill: it flies OVER the window it came
        /// from, so it is lifted toward opaque rather than given a fill colour of its own.</summary>
        private const float GhostAlphaScale = 1.25f;
        private const float GhostAlphaLift = 0.15f;

        /// <summary>Destroy the drag ghost if present (idempotent; safe from Idle and from an aborted
        /// gesture). The ghost now goes through <c>GridTheme.ApplyBox</c>, so it CAN be holding a shared
        /// effect material: unassign first, or HudFxMaterials keeps a dictionary entry keyed by a
        /// destroyed Graphic (one leaked entry per pin gesture). Unassign is a cheap no-op otherwise.</summary>
        private void DestroyGhost()
        {
            if (_ghostBg != null) HudFxMaterials.Unassign(_ghostBg);
            if (_ghostGo != null) Object.Destroy(_ghostGo);
            _ghostGo = null;
            _ghostRt = null;
            _ghostBg = null;
            _ghostIcon = null;
            _ghostName = null;
        }

        /// <summary>Return the tab to the idle pool: hide it and drop any shared glass material so a
        /// pooled tab doesn't linger in HudFxMaterials' registry. A tab recycled MID-drag (a structural
        /// rebuild landing during the gesture) drops its drag state and tears the ghost down, so nothing
        /// is stranded. No events to unhook; the two statics are cleared by <see cref="ResetStatics"/>
        /// and its canvas is destroyed by TheGridPanel.Shutdown on hot reload.</summary>
        public void Idle()
        {
            _dragging = false;
            _outside = false;
            _bounds = null;
            DestroyGhost();
            if (_icon != null) { _icon.sprite = null; _icon.enabled = false; }
            if (_bg != null) HudFxMaterials.Unassign(_bg);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>The ghost lives on the ROOT canvas, not under this tab, so it does NOT die with us.
        /// Unity also never delivers OnEndDrag to a deactivated component, so a window hidden (or a tab
        /// pooled/destroyed) mid-gesture would strand the ghost on screen forever. Both exit paths clear
        /// the drag state and tear it down; DestroyGhost is idempotent.</summary>
        private void OnDisable()
        {
            if (!_dragging && _ghostGo == null) return;
            _dragging = false;
            _outside = false;
            _bounds = null;
            DestroyGhost();
        }

        private void OnDestroy()
        {
            DestroyGhost();
        }

        /// <summary>Pointer surface for the whole tab: click, hover and the drag-out (pin) gesture. All
        /// three drag events land here (UGUI routes a whole gesture to the object where it began), so the
        /// tab's drag state stays instance-local — no static drag state to strand across a hot reload.
        /// A plain click forwards to the region's collapse toggle (wired once via <see cref="Clicked"/>);
        /// a DRAGGED pointer's click is ignored here and the gesture's outcome (collapse vs pin) is
        /// decided solely by <see cref="EndDragOut"/>.</summary>
        private sealed class GridClickable : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
            IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public GridTab Owner;
            public bool Hover;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                // UGUI DOES still deliver a click after a drag when the release object is the press
                // object (eligibleForClick survives the drag) and it arrives BEFORE OnEndDrag — so
                // without this guard a tab released inside the panel would toggle collapse here AND
                // again in EndDragOut's inside-release branch, netting a no-op. The drag path owns
                // the outcome; a plain (undragged) click still falls through to Clicked().
                if (e.dragging) return;
                if (Owner != null && Owner.Clicked != null) Owner.Clicked();
            }

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }

            public void OnBeginDrag(PointerEventData e) { if (Owner != null) Owner.BeginDragOut(e); }
            public void OnDrag(PointerEventData e) { if (Owner != null) Owner.DragOut(e); }
            public void OnEndDrag(PointerEventData e) { if (Owner != null) Owner.EndDragOut(e); }
        }
    }
}
