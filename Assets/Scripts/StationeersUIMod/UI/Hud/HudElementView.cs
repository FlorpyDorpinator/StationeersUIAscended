using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A HudPanel whose identity, geometry, style and tier visibility come from ONE
    /// document element instead of hand-coded config. The document is the source of
    /// truth; views are renderers — HudSystem rebuilds them when the active document
    /// is replaced and relayouts them when it changes (HudProfileStore.Version feeds
    /// the layout hash, so the existing relayout machinery carries the designer).
    /// </summary>
    internal abstract class HudElementView : HudPanel
    {
        public HudElementDef Def;

        /// <summary>The STYLE SLOT the HUD is being drawn for this frame. Set by HudSystem: at
        /// runtime it follows the live tier (Bare / Robot / Base), and while the F9 editor is open
        /// it follows the EXPLICIT preview instead, so what you see always matches what you edit
        /// (a drag can't render one tier while writing another).
        ///
        /// This is the WANTED slot, not the resolved one — read <see cref="Slot"/> from a widget,
        /// which additionally asks the element whether it actually forks this slot.</summary>
        internal static HudStyleSlot LayoutSlot;

        /// <summary>True while the HUD is drawn with the BARE (power-off) geometry — the LAYOUT
        /// fork's discriminator, and the bool call shape every widget's style read still uses (it
        /// maps onto <see cref="HudStyleSlot.Bare"/>, which then resolves through the element's
        /// opt-in). Kept as a computed property so HudSystem/HudWarp/the editor read one truth.</summary>
        internal static bool LayoutBare => LayoutSlot == HudStyleSlot.Bare;

        /// <summary>The effective tier the HUD is drawn for (VISIBILITY — which elements show).
        /// The F9 editor filters selection by this so you can't grab an element that isn't on
        /// screen in the current preview.</summary>
        internal static HudTier LayoutTier;

        /// <summary>The slot an F9 edit TARGETS: the explicitly previewed tier while the editor is
        /// open, Base otherwise. Deliberately separate from <see cref="LayoutSlot"/> so a
        /// 'Live' preview always edits the base and never silently forks just because the player
        /// happens to be bare (adversarial review 2026-07-12).</summary>
        internal static HudStyleSlot EditTargetSlot;

        /// <summary>True only when the F9 editor is previewing BARE explicitly.</summary>
        internal static bool EditBareTier => EditTargetSlot == HudStyleSlot.Bare;

        /// <summary>THIS element's resolved style slot for the CURRENT frame: the render slot when
        /// the element forks it, else Base. Widgets read this (or the equivalent bool
        /// <see cref="LayoutBare"/>), never <see cref="LayoutSlot"/> directly.
        ///
        /// Named StyleSlot, not Slot: half the widgets declare locals of the GAME's
        /// <c>Assets.Scripts.Objects.Slot</c> type, and a base member sharing that name is a
        /// resolution hazard nobody should have to think about at a call site.</summary>
        protected HudStyleSlot StyleSlot => Def != null ? Def.ResolveSlot(LayoutSlot) : HudStyleSlot.Base;

        /// <summary>The curvature mode the HUD is currently laid out for (set by HudSystem from
        /// HudConfig.Curvature). Each mode (A/B/C/D) remembers its own LIVE placement per profile;
        /// Flat and any un-customised mode fall back to the base layout. The editor writes drags to
        /// THIS mode's slot, so switching curvature recalls that mode's arrangement.</summary>
        internal static HudCurvature LayoutMode;

        public override string Id => Def != null ? Def.Type + "_" + Def.Id : "element";

        /// <summary>Power-death participation keeps the 0.5.0 semantics: an element that
        /// exists ONLY while suited dies in the CRT collapse; anything the bare body also
        /// shows (hands, equipment) survives it.</summary>
        public override bool SuitTier => Def != null
            && (Def.Tiers & HudTierMask.Suited) != 0
            && (Def.Tiers & HudTierMask.Bare) == 0;

        /// <summary>Document elements have no per-element ConfigEntry — presence in the
        /// document IS the toggle (the visibility loop treats null as "always on").</summary>
        public override ConfigEntry<bool> Toggle => null;

        public override bool VisibleAt(HudTier tier)
        {
            if (Def == null) return false;
            switch (tier)
            {
                case HudTier.Bare: return (Def.Tiers & HudTierMask.Bare) != 0;
                case HudTier.Suited: return (Def.Tiers & HudTierMask.Suited) != 0;
                case HudTier.Robot: return (Def.Tiers & HudTierMask.Robot) != 0;
                default: return false;
            }
        }

        /// <summary>Element centre in UNWARPED canvas coords: anchor point plus the stored
        /// offset, scaled. This is the element's logical position — the editor hit-rect and
        /// handle math live here (the mouse is inverse-warped to meet it).</summary>
        protected Vector2 CenterForLogical(float scale)
        {
            bool bare = LayoutBare;
            return HudElementDef.AnchorPoint(Def.AnchorFor(bare, LayoutMode), Screen.width * 0.5f, Screen.height * 0.5f)
                + new Vector2(Def.XFor(bare, LayoutMode), Def.YFor(bare, LayoutMode)) * scale;
        }

        /// <summary>Element centre for placement — the LOGICAL centre. Curvature is applied
        /// ONCE, by each child graphic's own VisorWarp/TmpWarp mesh modifier (which bends
        /// vertices by their absolute canvas position). Pre-warping the placement here as
        /// well would double-warp — Barrel(Barrel(centre)) — and desync the F9 editor, whose
        /// handles forward-warp the LOGICAL corners a single time (adversarial review,
        /// 2026-07-12). Small centred elements therefore only micro-bend under the mesh warp;
        /// the top bar's compass sidesteps that by going boxless.</summary>
        protected Vector2 CenterFor(float scale) => CenterForLogical(scale);

        /// <summary>Fixed reference px × scale, unless the element opted into screen-
        /// relative sizing (WPct/HPct > 0) — full-width bars survive any resolution.</summary>
        protected Vector2 SizeFor(float scale)
        {
            bool bare = LayoutBare;
            float wp = Def.WPctFor(bare, LayoutMode), hp = Def.HPctFor(bare, LayoutMode);
            float w = wp > 0f ? Screen.width * wp : Def.WFor(bare, LayoutMode) * scale;
            float h = hp > 0f ? Screen.height * hp : Def.HFor(bare, LayoutMode) * scale;
            return new Vector2(w, h);
        }

        /// <summary>The element's rect in canvas coords — the designer's hit/handle box.
        /// LOGICAL (unwarped) space: the editor inverse-warps the mouse to meet it and
        /// forward-warps the drawn handles, so both agree with the visually-warped element.</summary>
        internal Rect CanvasRect(float scale)
        {
            var c = CenterForLogical(scale);
            var s = SizeFor(scale);
            return new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y);
        }

        /// <summary>Stranded-element self-heal: if this element is TOTALLY off screen (a
        /// resolution/aspect hop — e.g. streaming the game to another device — can re-anchor a
        /// layout outside the screen it comes home to), pull it back so a usable sliver is
        /// visible and it can be grabbed again. Partially off-screen is deliberately allowed —
        /// only a fully invisible element is rescued. Writes the same layout slot the rect was
        /// measured from (current tier + curvature mode). Returns true if it moved.</summary>
        internal bool EnsureOnScreen(float scale)
        {
            if (Def == null) return false;
            var r = CanvasRect(scale);
            float halfW = Screen.width * 0.5f, halfH = Screen.height * 0.5f;
            // "Totally off" = no meaningful screen overlap on SOME axis (an 8px sliver counts
            // as gone — it is unreachable in practice). Rects overlap only when BOTH axes do.
            const float Sliver = 8f;   // below this much overlap the element counts as lost
            const float MinVis = 32f;  // how much of it the rescue pulls back into view
            bool offX = r.xMax < -halfW + Sliver || r.xMin > halfW - Sliver;
            bool offY = r.yMax < -halfH + Sliver || r.yMin > halfH - Sliver;
            if (!offX && !offY) return false;

            float dx = 0f, dy = 0f;
            if (r.xMax < -halfW + MinVis) dx = (-halfW + MinVis) - r.xMax;
            else if (r.xMin > halfW - MinVis) dx = (halfW - MinVis) - r.xMin;
            if (r.yMax < -halfH + MinVis) dy = (-halfH + MinVis) - r.yMax;
            else if (r.yMin > halfH - MinVis) dy = (halfH - MinVis) - r.yMin;
            if (dx == 0f && dy == 0f) return false;

            bool bare = LayoutBare;
            float s = Mathf.Max(0.01f, scale);
            Def.SetXFor(bare, LayoutMode, Def.XFor(bare, LayoutMode) + dx / s);
            Def.SetYFor(bare, LayoutMode, Def.YFor(bare, LayoutMode) + dy / s);
            return true;
        }

        // The coherent TWO-STATE style contract (the 2026-07-16 standardisation): an element
        // either FOLLOWS the F9 globals (theme + effects) or carries a complete CUSTOM snapshot
        // seeded from those globals at the moment of separation. The old third state — "legacy
        // mixed" (0/absent: the followGlobal flag + -1 sentinels + fx* global-multipliers) — is
        // REGRESSED at profile load by HudStyleMigration; no resolver reads it any more.
        // StyleLegacy survives only as the raw stored value the migration recognises.
        internal const int StyleLegacy = 0;
        internal const int StyleGlobal = 1;
        internal const int StyleCustom = 2;

        /// <summary>The style source of ONE slot. Per-slot since 0.9.2.5, so an element can be flat
        /// in bare and glassy in suit; a slot that is not forked resolves the base value, so this is
        /// backward-identical for every existing profile.</summary>
        private static int StyleSourceOf(HudElementDef d, HudStyleSlot slot)
        {
            // Missing/0 reads as Global: HudStyleMigration rewrites every stored legacy element
            // during Sanitize, so this default only decides brand-new in-memory elements.
            int v = d != null ? d.GetIFor(slot, "styleSource", StyleGlobal) : StyleGlobal;
            return v == StyleCustom ? StyleCustom : StyleGlobal;
        }

        private static int StyleSourceOf(HudElementDef d) => StyleSourceOf(d, HudStyleSlot.Base);

        private int StyleSource
        {
            get
            {
                // StyleSlot, not LayoutSlot: an element that has not opted in reads the base. While
                // the F9 editor is open HudSystem drives LayoutSlot and EditTargetSlot from the
                // SAME preview, so this is also the slot DescribeProps is editing.
                return StyleSourceOf(Def, StyleSlot);
            }
        }

        // Protected: PrimitiveView's polyline styling resolves through the same two-state
        // contract (the line was the last surface on raw -1 sentinels, styleSource-blind).
        protected bool UsesGlobalStyle => StyleSource == StyleGlobal;
        protected bool UsesCustomStyle => StyleSource == StyleCustom;

        /// <summary>True while this element's styling follows the globals (the two-state
        /// contract: legacy reads as Global after migration). Widget props that would merely
        /// mirror global-palette values hide behind this.</summary>
        protected bool FollowGlobal => UsesGlobalStyle;

        /// <summary>Per-corner radius resolved through the explicit style source, with the old
        /// -1 sentinel retained only for legacy profiles.</summary>
        protected float Radius(float perCorner)
        {
            float global = HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f;
            if (UsesGlobalStyle) return global;
            if (UsesCustomStyle) return perCorner >= 0f ? perCorner : global;
            return perCorner >= 0f ? perCorner : global;
        }

        /// <summary>The element's corner STYLE as <see cref="PanelGraphic.CornerCut"/> expects it:
        /// -1 = follow the global <see cref="HudConfig.HudCornerStyle"/>, 0 = rounded, 1 = cut.
        ///
        /// Precedence mirrors <see cref="Radius"/> exactly — an element that FOLLOWS the globals
        /// hands the panel the -1 sentinel (so it tracks the F9 knob live, the same way the radius
        /// does), and a Custom element reads its own slot-aware "cornerStyle" param, whose 0 /
        /// absent value means "still follow the global". Every existing profile therefore resolves
        /// to -1 and renders exactly as before.</summary>
        protected int CornerCutFor()
        {
            if (UsesGlobalStyle) return -1;
            int own = Def != null ? Def.GetIFor(LayoutBare, "cornerStyle", 0) : 0;
            return own == CornerStyleRounded ? 0 : own == CornerStyleCut ? 1 : -1;
        }

        // The per-element param's own encoding (0 = follow the global, so an absent key is inert).
        internal const int CornerStyleFollow = 0;
        internal const int CornerStyleRounded = 1;
        internal const int CornerStyleCut = 2;
        private static readonly string[] CornerStyleNames =
            { "Follow global", "Rounded", "Cut (flat 45 degree)" };

        protected float BorderWidthFor()
        {
            float global = HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f;
            if (UsesGlobalStyle) return global;
            float bw = Def != null ? Def.BorderWidthFor(LayoutBare) : -1f;
            return bw >= 0f ? bw : global;
        }

        /// <summary>Trapezoid insets (reference px × scale) pull the top/bottom corners of the
        /// element's background panel inward — the same shaping the Box primitive and the hand
        /// tray use to angle their side edges. Views that expose a single framed panel forward
        /// these into their <see cref="PanelGraphic.SetShape"/> call.</summary>
        protected float InsetTop(float scale) => Def.GetFFor(LayoutBare, "insetTop", 0f) * scale;
        protected float InsetBottom(float scale) => Def.GetFFor(LayoutBare, "insetBottom", 0f) * scale;

        /// <summary>Whether this element draws a single framed background panel that a trapezoid
        /// inset can reshape. Box-bearing views override this to true so the two inset sliders
        /// appear in their prop list; multi-panel views (chips, equipment grid, body doll) leave
        /// it false so no dead slider is offered.</summary>
        protected virtual bool SupportsTrapezoid => false;

        /// <summary>Whether this element's "Text / accent" colour row does anything. Default true
        /// (the common case: most widgets tint at least one text/icon/line from it). Widgets that
        /// resolve their own dedicated colour ref instead (BareSenses/StateChips/MoodletBorrow's
        /// "wordColor", EquipmentColumn's numColor/labelColor) or draw no accent-tinted surface at
        /// all (Compass, Portrait, BodyDoll, DamageDoll) override to false so F9 stops offering a
        /// row that can never change a pixel.</summary>
        protected virtual bool UsesAccentColor => true;

        /// <summary>Whether this element's "Font scale" row does anything. Default true; widgets
        /// that draw no scalable text at all (Portrait, BodyDoll, SuitChips, DamageDoll, PngDoll,
        /// and PrimitiveView's Box/Polyline/Icon/Shape primitives) override to false.</summary>
        protected virtual bool UsesFontScale => true;

        /// <summary>Whether this element owns at least one UIA PanelGraphic/PolygonPanelGraphic
        /// whose surface can actually consume panel styling. Keeps the inspector from offering
        /// frost, corners, and glow on text-only or borrowed-vanilla widgets where those controls
        /// could never affect a pixel.
        ///
        /// DEF-ONLY (no live view) callers — bulk ops / flatten checks over a raw
        /// <see cref="HudElementDef"/> — cannot ask a live view's virtual, so this switch is kept
        /// as a static map for THOSE call sites (see <see cref="CanFlattenDefinition"/>). Live
        /// rendering/inspector code must go through the virtual <see cref="SupportsPanelAppearance"/>
        /// below instead, which each widget declares on itself.</summary>
        private static bool SupportsPanelAppearanceFor(HudElementDef d)
        {
            if (d == null) return false;
            switch (d.Type)
            {
                case HudElementType.Box:
                case HudElementType.Shape:
                case HudElementType.Readout:
                case HudElementType.ActiveHandBadge:
                case HudElementType.Compass:
                case HudElementType.EquipmentColumn:
                case HudElementType.HandBoxes:
                case HudElementType.KeybindChips:
                case HudElementType.VitalsPanel:
                case HudElementType.DamageDoll:
                case HudElementType.JetpackBox:
                case HudElementType.StateChips:
                case HudElementType.SuitChips:
                case HudElementType.PngDoll:
                // BareSenses owns an optional whole-element background panel (default OFF) plus
                // optional per-sense boxes, so it earns the full glass/corner/effect prop set.
                case HudElementType.BareSenses:
                    return true;
                // MoodletDashboard is deliberately NOT here. Its live view is MoodletBorrowWidget,
                // which reparents vanilla's real moodlet strip and builds no UIA surface of its own
                // (BuildContent makes a holder + CanvasGroup, nothing else). Claiming a panel here
                // filled its inspector with fill/border/glow/frost controls that cannot touch a
                // pixel — the retired MoodletDashboardWidget was the thing that had a backdrop.
                default:
                    return false;
            }
        }

        /// <summary>Per-widget capability declaration, same truth table as
        /// <see cref="SupportsPanelAppearanceFor"/> above but as a virtual so each widget states it
        /// on itself. Base default is TRUE (the most common case among the switch's entries);
        /// widgets whose Def.Type resolved false there override to false (or, for PrimitiveView/
        /// DynamicTextWidget which each cover several Def.Types, switch on Def.Type themselves).</summary>
        protected virtual bool SupportsPanelAppearance => true;

        /// <summary>Elements that own no full panel surface but DO draw a UIA border/ring from the
        /// element's own Border ref + BorderWidth. They are off the panel allow-list (correctly —
        /// they have no fill, glow or frost), which left those two authored values with no control
        /// at all: the shipped Glassy 4.0 profile sets a portrait ring colour and width that F9
        /// could not show or edit. Offer exactly the two knobs the widget actually reads.
        /// Base default false; only Portrait/BodyDoll override true. The call site (else-if after
        /// checking SupportsPanelAppearance) already keeps the two mutually exclusive, so this no
        /// longer needs its own "!SupportsPanelAppearance" guard.</summary>
        protected virtual bool SupportsBorderOnlyChrome => false;

        /// <summary>Push this element's EFFECTIVE EDGE onto a border-only ring (the portrait).
        ///
        /// WHY THIS EXISTS. A ring is a <see cref="CircleGraphic"/>, the one UIA surface that is
        /// not a <see cref="PanelGraphic"/>, so it never took part in the machinery that actually
        /// produces a visible panel edge under the shipped themes. Those themes set the global
        /// PanelBorderWidth to a hairline (0.076 px) and get their rims from the EDGE-LIGHT
        /// family instead: PanelGraphic.BorderAt pulls the border toward
        /// <see cref="PanelGraphic.LightTint"/> where it faces the key light and lifts its alpha
        /// 85% of the way to opaque (Spec), then BorderFade dissolves the unlit run, and a faint
        /// halo trails outside. Meanwhile the ring drew a raw 0.076 px line — which the first fix
        /// then rendered as a 1 px hairline at 7.6% alpha, i.e. still nothing. Hence FlorpyDorp's
        /// report that the ring vanishes under follow-global and that "there is no edge glass
        /// kind of effect and I can't even set that in the effects tab for this element".
        ///
        /// FOLLOW-GLOBAL resolves from exactly the knobs a panel resolves from:
        ///   - width: the global PanelBorderWidth, rendered the PANEL way (HairlineFloor, i.e.
        ///     authored width + full alpha carried by the feather ramps, PanelGraphic's own
        ///     `bw > 0.05f` admit gate) instead of coverage-faded;
        ///   - edge light: <see cref="GlassEdgeFor"/> — the same value <see cref="ApplyGlass"/>
        ///     pushes into every panel's Spec, Tier-A edge-light boost included — tinted by the
        ///     shared FxEdgeLightColor via PanelGraphic.LightTint() (which returns white by
        ///     itself when the global edge light is unchecked, so unchecking reverts the ring
        ///     exactly as it reverts a panel);
        ///   - border fade / halo: the same Tier-A gated FxBorderFade / FxGlow / FxGlowWidth
        ///     resolution <see cref="ApplyMeshFx"/> performs.
        /// With the whole edge family off, every value lands inert and the ring falls back to the
        /// classic global border — just drawn with the panel's geometry rather than a coverage fade.
        ///
        /// CUSTOM style is deliberately untouched (authored ring colour + width, classic
        /// sub-pixel behaviour). The only additions there are the two opt-in halo knobs below,
        /// both neutral until an author sets them.</summary>
        protected void ApplyBorderOnlyEdge(CircleGraphic ring)
        {
            if (ring == null) return;
            ring.BorderColor = BorderColor();
            ring.BorderWidth = BorderWidthFor();

            if (UsesCustomStyle)
            {
                // Edge light + border fade were HARD-ZEROED here, so a custom-styled ring (the
                // portrait under a per-suit theme, styleSource=Custom) could never show the edge
                // glass a panel shows, and F9 offered no knob for it — FlorpyDorp: "I still don't
                // get the edge lighting effects on the portrait border" with bfade already set.
                // Resolve them the SAME way a custom PANEL does: GlassEdgeFor reads the element's
                // own "spec", and border fade rides its own "customBorderFadeOn"/"bfade" under the
                // Tier-A master (identical gating to ApplyMeshFx). The portrait-specific halo knobs
                // (ringGlow/ringGlowColor) are kept verbatim, so no profile key is renamed/removed
                // (no ConfigMigration needed). Sub-pixel stroke behaviour stays classic (0 floor).
                bool edgeTierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
                ring.HairlineFloor = 0f;
                ring.EdgeSpec = GlassEdgeFor();
                ring.EdgeTint = PanelGraphic.LightTint();
                ring.EdgeFade = edgeTierA && StyleFeatureOn("customBorderFadeOn", HudConfig.FxBorderFadeOn)
                    ? OwnOrGlobal("bfade", HudConfig.FxBorderFade) : 0f;
                ring.GlowStrength = RingGlowFor();
                ring.GlowWidth = OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth);
                string cref = Def != null ? Def.GetSFor(LayoutBare, "ringGlowColor", "") : "";
                ring.GlowColor = string.IsNullOrEmpty(cref)
                    ? Color.clear                                   // clear = derive from the rim
                    : HudPalette.Resolve(cref, HudPalette.PanelBorder.Value);
                return;
            }

            // Follow-global. PanelGraphic's own admit threshold, so the ring's sub-pixel line is
            // the panel's sub-pixel line.
            ring.HairlineFloor = 0.05f;
            ring.EdgeSpec = GlassEdgeFor();
            ring.EdgeTint = PanelGraphic.LightTint();
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            ring.EdgeFade = tierA && StyleFeatureOn("customBorderFadeOn", HudConfig.FxBorderFadeOn)
                ? OwnOrGlobal("bfade", HudConfig.FxBorderFade) : 0f;
            ring.GlowStrength = RingGlowFor();
            ring.GlowWidth = OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth);
            ring.GlowColor = Color.clear;                            // the rim's own hue, as panels do
        }

        /// <summary>The halo strength a BORDER-ONLY ring is currently showing, resolved the same
        /// way in both style states so the separation snapshot can freeze it (Phase 0a).
        /// Custom stores its own "ringGlow" (0 = no halo band at all); Global derives the ring's
        /// halo from the panel recipe, so separating an element with the glow master on used to
        /// drop the halo entirely — this is the value the seed now writes down.</summary>
        private float RingGlowFor()
        {
            if (Def == null) return 0f;
            if (UsesCustomStyle) return Mathf.Max(0f, Def.GetFFor(LayoutBare, "ringGlow", 0f));
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = tierA && StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn);
            return glowOn ? Mathf.Max(0f, OwnOrGlobal("glow", HudConfig.FxGlow)) : 0f;
        }

        /// <summary>Used by F9 to suppress panel-only actions on text, borrowed vanilla UI and
        /// other elements that cannot render any UIA surface.</summary>
        internal bool CanFlatten => SupportsPanelAppearance;

        internal static bool CanFlattenDefinition(HudElementDef d)
            => SupportsPanelAppearanceFor(d);

        internal static bool IsCustomStyleDefinition(HudElementDef d)
            => StyleSourceOf(d) == StyleCustom;

        // Freeform pen Shapes use PolygonPanelGraphic (an arbitrary concave contour is not
        // representable by the rounded-box SDF). Its MESH renders both halo bands now
        // (2026-07-16), so the halo knobs apply to Shapes too. The ANALYTIC-only extras
        // (gaussian falloff, haze/breath/uneven, flowing aura) stay rectangle-only — they
        // live in the SDF fragment shader, which a Shape never takes.
        private bool SupportsGlowHalo => SupportsPanelAppearance;
        private bool SupportsAnalyticPanel => SupportsGlowHalo
            && Def != null && Def.Type != HudElementType.Shape;

        // ---- capability facades for HudStyleFx's `Applies` predicates (Phase 2) --------------
        //
        // The Supports*/Uses* members above are protected or private (each widget declares its own),
        // so a lambda living in HudStyleFx cannot see them. These internal one-liners are the whole
        // bridge: the registry stays the single description of every knob, including WHICH SURFACES
        // each knob exists on, and no second capability list is maintained anywhere.
        //
        // They answer "does this element TYPE have this surface at all", never "is it working right
        // now" — a knob that is merely inert (analytic shader off, bundle too old, Cut corners on an
        // ABI-2 bundle) still renders, with a reason. See SdfInertReason below.

        /// <summary>Owns a UIA PanelGraphic/PolygonPanelGraphic surface (fill + border + glass).</summary>
        internal bool FxHasPanel => SupportsPanelAppearance;

        /// <summary>Draws a border-only ring from Border + BorderWidth (portrait, body doll).</summary>
        internal bool FxHasRing => SupportsBorderOnlyChrome;

        /// <summary>A drawn line (PolylineGraphic): edge light, ripple and the mesh halo, no fill.</summary>
        internal bool FxIsLine => Def != null && Def.Type == HudElementType.Polyline;

        /// <summary>Anything with an outline the edge/border family can light: panel or ring.</summary>
        internal bool FxHasChrome => SupportsPanelAppearance || SupportsBorderOnlyChrome;

        /// <summary>Renders the edge-energy family (lit border, shimmer, flow): panels and lines.</summary>
        internal bool FxHasEdgeEnergy => SupportsPanelAppearance || FxIsLine;

        /// <summary>Renders a glow halo band: panels, freeform shapes and lines.</summary>
        internal bool FxHasHalo => SupportsGlowHalo || FxIsLine;

        /// <summary>Consumes the four AUTHORED corner radii (not a derived pill radius, not a pen
        /// contour), so the per-corner sliders and the corner-style combo mean something.</summary>
        internal bool FxAuthoredCorners => SupportsAuthoredCorners;

        /// <summary>Draws one framed background panel a trapezoid inset can reshape.</summary>
        internal bool FxHasTrapezoid => SupportsTrapezoid;

        /// <summary>Multiplies its text size by the element's own font scale.</summary>
        internal bool FxUsesFontScale => UsesFontScale;

        /// <summary>Several widgets retain their panel styling while their optional background
        /// is hidden. Keep those values editable, but tell the author why they currently cannot
        /// see a response instead of presenting apparently broken controls.
        /// Base default false (no optional-background knob); the widgets that own one — Readout,
        /// Compass, VitalsPanel, DamageDoll, JetpackBox, PngDoll (their "box" key defaults true) and
        /// BareSenses (whole-element background, "box" key defaults OFF since it's usually just
        /// words) — override this to read their own key.</summary>
        protected virtual bool OptionalPanelBackgroundIsOff => false;

        /// <summary>The toggle NAME named by <see cref="OptionalPanelBackgroundHint"/> — each widget
        /// labels its own differently, and sending someone to hunt for a "Background box" that its
        /// inspector calls "Box frame" is worse than silence. Default matches the common label;
        /// Compass and DamageDoll override it to their own inspector row's name.</summary>
        protected virtual string OptionalPanelBackgroundToggleName => "Background box";

        /// <summary>The hint shown while <see cref="OptionalPanelBackgroundIsOff"/>.</summary>
        private string OptionalPanelBackgroundHint
            => OptionalPanelBackgroundToggleName + " is OFF (Appearance) — panel effects are saved but currently invisible";

        // Suit-chip radii are deliberately derived from their size, and arbitrary pen Shapes use
        // PolygonPanelGraphic. Neither consumes the four authored radii.
        private bool SupportsAuthoredCorners => SupportsAnalyticPanel
            && Def != null
            && Def.Type != HudElementType.SuitChips;

        // ActiveHandBadge intentionally derives its contour from ActiveHandAccent at runtime.
        private bool SupportsCustomBorderColor => Def != null
            && Def.Type != HudElementType.ActiveHandBadge;

        /// <summary>Colours are per-element in BOTH style states (the 2026-07-16 standardisation):
        /// the ref renders exactly as written — a palette NAME (e.g. "HudPanelFill") tracks the F9
        /// palette live, a hex literal stands alone, and empty falls back to the slot's palette
        /// default. The follow-global toggle governs effects/glass/sizing only; it never force-feeds
        /// the palette over an element's own refs (that behaviour is what silently discarded the
        /// user's per-element colour work whenever an element followed the globals).</summary>
        protected Color GlobalOr(string colorRef, Color global)
            => HudPalette.Resolve(colorRef, global);

        protected Color FillColor() => GlobalOr(Def.FillFor(LayoutBare), HudPalette.PanelFill.Value);
        private int _alertSeed = -1;   // cached HudDocument.StableSeed(Def.Id); -1 = not yet resolved

        /// <summary>This element's stable alert-quantisation seed. Subclasses that tint a graphic the
        /// base <see cref="BorderColor"/> hook does not reach (PrimitiveView's line stroke) must pass
        /// THIS, not the seedless overload, or every such graphic shares one quantisation grid and
        /// they all re-mesh on the same frame.</summary>
        protected int AlertSeed
        {
            get
            {
                if (_alertSeed < 0)
                    _alertSeed = (Def != null && Def.Id != null) ? HudDocument.StableSeed(Def.Id) : 0;
                return _alertSeed;
            }
        }

        /// <summary>Resolved border colour, filtered through the status alert pulse. Both the halo hue
        /// and the edge-ripple contour derive from BorderColor inside the graphic (PanelGraphic.cs:755
        /// and BorderAt :1284), so this ONE hook recolours both, for every widget that resolves its
        /// border here. Identity when no alarm stands. The seed offsets only the QUANTISATION grid,
        /// never the wave phase — the alarm stays in lockstep across the HUD while the mesh rebuilds
        /// spread across frames.</summary>
        protected Color BorderColor()
        {
            return HudAlertPulse.Tint(
                GlobalOr(Def.BorderFor(LayoutBare), HudPalette.PanelBorder.Value), AlertSeed);
        }
        protected Color TextColor() => GlobalOr(Def.TextColorFor(LayoutBare), HudPalette.TextValue.Value);

        // #4: drag-over drop highlight — the hand / 1-6 equipment boxes light up while a dragged
        // item hovers a valid target. Per-element mode (border vs whole box) + colour (a ColorRef;
        // empty = the global HudDropHighlight palette).
        protected bool DropWholeBox() => Def != null && Def.GetBFor(LayoutBare, "dropWholeBox", false);
        protected Color DropHighlightColor()
        {
            string cref = Def != null ? Def.GetSFor(LayoutBare, "dropHiColor", "") : "";
            return string.IsNullOrEmpty(cref)
                ? HudPalette.DropHighlight.Value
                : HudPalette.Resolve(cref, HudPalette.DropHighlight.Value);
        }
        /// <summary>F9 props for the drop highlight — added by widgets that are drop targets.
        /// Tier-aware since 0.9.2.5: the cue is a LOOK, so it forks with the rest of the style.</summary>
        protected void AddDropHighlightProps(System.Collections.Generic.List<HudProp> into)
        {
            var d = Def;
            int start = into.Count;
            into.Add(HudProp.Bool("Drop: light whole box", () => d.GetBFor(EditBare(d), "dropWholeBox", false),
                v => d.SetBFor(EditBare(d), "dropWholeBox", v)));
            into.Add(HudProp.Color("Drop highlight colour", () => d.GetSFor(EditBare(d), "dropHiColor", ""),
                v => d.SetSFor(EditBare(d), "dropHiColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.DropHighlight.Value));
            MarkProps(into, start, HudPropGroup.Interaction);
        }

        // ---- per-element ITEM-ICON TINT (0.9.2.5) -------------------------------------------
        // The global "Tint item icons" checkbox (F9 -> View & Behavior) + the HudItemIconTint
        // palette entry stay, and remain the INHERITED default. This adds a per-element, per-tier
        // override in the same tri-state vocabulary the transition effects use, so a bare HUD can
        // wash its thumbnails differently from (or not at all like) the powered one.

        /// <summary>Combo captions for the icon-tint tri-state. Index order matches the stored int.</summary>
        private static readonly string[] IconTintModeNames = { "Inherit global", "On (own colour)", "Off" };

        /// <summary>Item-thumbnail tint for THIS element in the current slot. Mode 0 = inherit the
        /// global "Tint item icons" checkbox (+ HudItemIconTint), 1 = force on with this element's
        /// own colour ref (empty = the palette entry), 2 = force off. Preserves the icon's own
        /// alpha — the same contract as <see cref="HudConfig.TintIcon"/>, so an empty-slot fade
        /// stays invisible.</summary>
        protected Color TintItemIcon(Color c)
        {
            int mode = Def != null ? Def.GetIFor(LayoutBare, "iconTintMode", 0) : 0;
            if (mode == 2) return c;                    // explicit off
            if (mode != 1) return HudConfig.TintIcon(c); // inherit the global checkbox
            string cref = Def != null ? Def.GetSFor(LayoutBare, "iconTintColor", "") : "";
            Color fallback = HudPalette.ItemIconTint != null ? HudPalette.ItemIconTint.Value : Color.white;
            Color t = string.IsNullOrEmpty(cref) ? fallback : HudPalette.Resolve(cref, fallback);
            return new Color(c.r * t.r, c.g * t.g, c.b * t.b, c.a);
        }

        /// <summary>The two F9 rows, appended by any widget that draws item thumbnails.</summary>
        protected void AddIconTintProps(System.Collections.Generic.List<HudProp> into)
        {
            var d = Def;
            int start = into.Count;
            into.Add(WithId(HudProp.Enum("Item icon tint",
                () => Mathf.Clamp(d.GetIFor(EditBare(d), "iconTintMode", 0), 0, 2),
                v => d.SetIFor(EditBare(d), "iconTintMode", Mathf.Clamp(v, 0, 2)),
                IconTintModeNames), "iconTintMode"));
            if (Mathf.Clamp(d.GetIFor(EditBare(d), "iconTintMode", 0), 0, 2) == 1)
                into.Add(WithId(HudProp.Color("  icon tint colour", () => d.GetSFor(EditBare(d), "iconTintColor", ""),
                    v => d.SetSFor(EditBare(d), "iconTintColor", string.IsNullOrEmpty(v) ? null : v),
                    () => HudPalette.ItemIconTint != null ? HudPalette.ItemIconTint.Value : Color.white),
                    "iconTintColor"));
            else
                into.Add(HudProp.Header("  Inherit follows F9 -> View & Behavior -> Tint item icons."));
            MarkProps(into, start, HudPropGroup.Appearance);
        }

        /// <summary>The element's effective glass sheen: the global while following, the seeded
        /// per-element value in Custom (with -1/missing deferring to the global fail-soft).</summary>
        protected float GlassSheenFor()
        {
            float global = HudConfig.GlassSheen != null ? HudConfig.GlassSheen.Value : 0f;
            if (UsesGlobalStyle) return global;
            float v = Def != null ? Def.GetFFor(LayoutBare, "sheen", -1f) : -1f;
            return v >= 0f ? v : global;
        }

        /// <summary>The element's effective glass edge light.
        /// Custom: `spec` is the FINAL per-element strength — the separation snapshot froze any
        /// global Tier-A boost into it, so applying the boost again would make Custom neither
        /// independent nor visually continuous, and spec 0 simply means no edge light.
        /// Global: the GlassEdge base plus the Tier-A edge-light boost. That knob lights the
        /// PANEL border run too (play-test: the slider only drove drawn lines otherwise) — every
        /// widget flows through here, so one slider lights the whole following HUD's edges.</summary>
        protected float GlassEdgeFor()
        {
            float global = HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f;
            if (UsesCustomStyle)
            {
                float own = Def != null ? Def.GetFFor(LayoutBare, "spec", -1f) : -1f;
                return Mathf.Clamp01(own >= 0f ? own : global);
            }
            float baseSpec = global;
            if (HudConfig.FxTierA != null && HudConfig.FxTierA.Value
                && HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value
                && HudConfig.FxEdgeLight != null && HudConfig.FxEdgeLight.Value > 0f)
                baseSpec = Mathf.Clamp01(baseSpec + HudConfig.FxEdgeLight.Value * 0.45f);
            return baseSpec;
        }

        /// <summary>Push the element's glass params onto a panel: its own "sheen"/"spec" when set,
        /// else the global defaults (both default 0 = the flat pre-glass look). Views call this
        /// wherever they style a box — the setters are dirty-guarded, so the per-frame cost is two
        /// param reads. Also pushes the 0.9.0 per-element Tier B strength (uv0.x) so the shared
        /// effect shaders can modulate per element without breaking batching.</summary>
        protected void ApplyGlass(IGlassSurface g)
        {
            if (g == null) return;
            g.Sheen = GlassSheenFor();
            g.Spec = GlassEdgeFor();
            g.FeatherOverride = FeatherFor();
            ApplyMeshFx(g);
            ApplyEdgeFade(g.AsGraphic);
        }

        /// <summary>Attach/update the per-element EDGE FADE on a panel: the far left/right
        /// ("edgeFadeX") and top/bottom ("edgeFadeY") of the box dissolve to transparent, so a
        /// wide bar melts into the visor at its ends (the concept-art look). Fades the box / border
        /// / glow it runs on, NOT the sibling text/icons — the readouts stay crisp. The modifier is
        /// added lazily and left inert (fade 0) when unused, so untouched elements are unchanged.</summary>
        protected void ApplyEdgeFade(Graphic g)
        {
            if (g == null) return;
            float fx = Def != null ? Def.GetFFor(LayoutBare, "edgeFadeX", 0f) : 0f;
            float fy = Def != null ? Def.GetFFor(LayoutBare, "edgeFadeY", 0f) : 0f;
            bool on = fx > 0.001f || fy > 0.001f;
            // The fade is a per-vertex alpha ramp: across a single-fan interior it interpolates
            // RADIALLY (corner→centre bowtie X). Opt the panel into the dense interior so the
            // ramp is sampled every ~48px — barycentric interp then reproduces it exactly.
            var pg = g as PanelGraphic;
            // Analytic panels evaluate the fade from local coordinates in the fragment shader.
            // Leaving HudEdgeFade active would apply it a second time and reintroduce the legacy
            // vertex-interpolation bowtie this path exists to remove.
            if (pg != null && pg.SdfMode)
            {
                pg.DenseFill = false;
                var old = g.GetComponent<HudEdgeFade>();
                if (old != null) old.SetFade(0f, 0f);
                return;
            }
            if (pg != null) pg.DenseFill = on;
            var ef = g.GetComponent<HudEdgeFade>();
            if (!on)
            {
                if (ef != null) ef.SetFade(0f, 0f);
                return;
            }
            if (ef == null) ef = g.gameObject.AddComponent<HudEdgeFade>();
            ef.SetFade(fx, fy);
        }

        /// <summary>The element's effective edge softness (AA ramp width, px): its own "feather"
        /// when set (>= 0), else -1 = defer to the global HudConfig.EdgeFeather (PanelGraphic does
        /// the fallback). Same "-1 = global" convention as corner radius and border width.</summary>
        protected float FeatherFor()
        {
            if (UsesGlobalStyle) return -1f;
            return Def != null ? Def.GetFFor(LayoutBare, "feather", -1f) : -1f;
        }

        private float EffectiveFeatherFor()
        {
            float v = FeatherFor();
            return v >= 0f ? v : (HudConfig.EdgeFeather != null ? HudConfig.EdgeFeather.Value : 1.25f);
        }

        /// <summary>The Tier A mesh-effect push WITHOUT the sheen/spec pair: the concept-art
        /// trio (border fade / soft edge / glow in+out) + edge shimmer, per-element overrides
        /// with the "-1 = follow global" convention, everything dirty-guarded in the graphic.
        /// Split out of <see cref="ApplyGlass"/> because widgets that style sheen/spec by hand
        /// (hand boxes' active-accent logic, equipment column) MUST still receive the mesh
        /// effects — calling only ApplyFx left the hand/1-6 boxes inert for every 0.9.0 mesh
        /// feature (play-test round 11; same shape as the round-1 ApplyFx split). Tier A off
        /// = everything 0 = classic 0.8.0 output.</summary>
        protected void ApplyMeshFx(IGlassSurface g)
        {
            if (g == null) return;
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool bfOn = tierA && StyleFeatureOn("customBorderFadeOn", HudConfig.FxBorderFadeOn);
            bool seOn = tierA && StyleFeatureOn("customSoftEdgeOn", HudConfig.FxSoftEdgeOn);
            bool glOn = tierA && StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn);
            g.BorderFade = bfOn ? OwnOrGlobal("bfade", HudConfig.FxBorderFade) : 0f;
            g.SoftEdge = seOn ? OwnOrGlobal("softEdge", HudConfig.FxSoftEdge) : 0f;
            // Outer glow only, and only inside an enabled Tier A: an alarm forces a halo to exist so
            // there is something to tint when the user has the glow checkbox off (FxGlowOn defaults
            // false), but Tier A off must stay "everything 0 = classic 0.8.0 output". The floor is a
            // CONSTANT, so the skirt / ramp-stop geometry (PanelGraphic.cs:460, :711) changes only at
            // alarm onset and clear, never on a quantisation step.
            // The floor is withheld from a BORDERLESS element: with bw <= 0.05f PanelGraphic's
            // hasBorder gate (:635) makes the halo derive from the FILL colour instead of the tinted
            // BorderColor, so flooring the glow there would light it in its NORMAL hue — an alarm
            // that brightens without changing colour is an anti-signal. Such an element simply sits
            // the alert out.
            float glowBase = glOn ? OwnOrGlobal("glow", HudConfig.FxGlow) : 0f;
            g.Glow = tierA ? (BorderWidthFor() > 0.05f ? HudAlertPulse.Glow(glowBase, AlertSeed) : glowBase) : 0f;
            g.GlowInner = glOn ? OwnOrGlobal("glowIn", HudConfig.FxGlowInner) : 0f; // never floored: sits under the text
            g.GlowWidth = OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth);
            g.GlowDiffuse = OwnOrGlobal("glowDiffuse", HudConfig.FxGlowDiffuse);
            // An SDF-family key (2026-07-16). Since Phase 0b a missing per-element value resolves
            // to the GLOBAL, like every other knob here — see NewSdfOwnOrGlobal.
            g.GlowExtraDiffuse = Mathf.Clamp01(
                NewSdfOwnOrGlobal("glowExtraDiffuse", HudConfig.FxGlowExtraDiffuse));
            bool rippleOn = tierA && StyleFeatureOn("customRippleOn", HudConfig.FxEdgeLightOn);
            g.EdgeRipple = rippleOn ? OwnOrGlobal("ripple", HudConfig.FxEdgeRipple) : 0f;
            g.EdgeRippleFreq = RippleFreqFor(OwnOrGlobal("rippleFreq", HudConfig.FxEdgeRippleFreq));
            g.RippleSmooth = UsesGlobalStyle ? 0f : (Def != null ? Def.GetFFor(LayoutBare, "rippleSmooth", 0f) : 0f);

            // The MOVING ripple on freeform Shapes: same edgeFlow speed the SDF panels use,
            // animated by HudEdgeFX/HudGlass off the uv1 payload the shape bakes. Gated on
            // the PROVEN flow ABI, not just Tier B: a pre-flow resident bundle after F6 still
            // registers the families but ignores uv1 — a nonzero FlowSpeed would then suppress
            // the static bake with nothing animating it (shimmer lost entirely).
            var poly = g as PolygonPanelGraphic;
            if (poly != null)
                poly.FlowSpeed = rippleOn && Core.HudShaderStore.FlowAbiAvailable
                    ? Mathf.Max(0f, RippleFlowSpeedFor(EdgeFlowFor())) : 0f;

            ApplyFx(g);
        }

        /// <summary>Sheen-pattern resolve: the element's own param when >= 0, else the global.</summary>
        protected float OwnOrGlobal(string key, ConfigEntry<float> global)
        {
            if (UsesGlobalStyle) return global != null ? global.Value : 0f;
            float v = Def != null ? Def.GetFFor(LayoutBare, key, -1f) : -1f;
            return v >= 0f ? v : (global != null ? global.Value : 0f);
        }

        protected bool StyleFeatureOn(string customKey, ConfigEntry<bool> global)
        {
            bool gv = global != null && global.Value;
            return UsesCustomStyle && Def != null ? Def.GetBFor(LayoutBare, customKey, gv) : gv;
        }

        /// <summary>Resolver for the SDF halo family (the keys introduced 2026-07-16).
        ///
        /// ONE MISSING-KEY CONVENTION (Phase 0b, 2026-07-26): an absent per-element key means
        /// "THE GLOBAL", never "off" — exactly like <see cref="StyleFeatureOn"/>. The original
        /// pair fell back to false/a fixed neutral so a global added AFTER a snapshot could not
        /// silently reactivate inside it; the cost was that "follows global" meant one thing for
        /// `glow` and the opposite thing for `glowHaze`, invisibly, and every future global knob
        /// inherited the neutral rule and therefore never reached an existing Custom element.
        /// The two mitigations that make the global fallback safe are (1) separation now ALWAYS
        /// re-seeds a complete snapshot (see <see cref="SeedCustomStyleFromEffective"/>), and
        /// (2) <c>HudDocument.Sanitize</c> back-fills the six SDF keys + three booleans into
        /// already-stored Custom elements once, from the current globals.</summary>
        private bool NewSdfFeatureOn(string customKey, ConfigEntry<bool> global)
        {
            bool gv = global != null && global.Value;
            return UsesCustomStyle && Def != null ? Def.GetBFor(LayoutBare, customKey, gv) : gv;
        }

        protected float NewSdfOwnOrGlobal(string key, ConfigEntry<float> global)
        {
            float gv = global != null ? global.Value : 0f;
            if (UsesGlobalStyle) return gv;
            // Custom: a missing key resolves to the GLOBAL, the same rule OwnOrGlobal uses.
            return Def != null ? Def.GetFFor(LayoutBare, key, gv) : gv;
        }

        /// <summary>Per-element edge-ripple frequency. When the global "Desync ripple" toggle is on,
        /// each element's ripple frequency is nudged by a small, STABLE per-element amount (seeded off
        /// the same FNV-1a Id hash the animator uses) so identical elements drift to slightly different
        /// frequencies instead of shimmering/flowing in lockstep — the variation the pattern needs to
        /// read as organic. Returns <paramref name="baseFreq"/> unchanged when the toggle is off, so a
        /// non-desynced HUD stays byte-identical. Deterministic across mesh rebuilds, F6 and F9 edits;
        /// zero-alloc (safe on the per-frame style path); clamped to the packable 0.05..8 range so the
        /// SDF vertex pack never overflows. Since it only varies an already-transmitted value, every
        /// renderer (SDF panels, pen shapes, lines) desyncs with no shader/ABI change.</summary>
        protected float RippleFreqFor(float baseFreq)
        {
            if (Def == null || HudConfig.FxRippleDesync == null || !HudConfig.FxRippleDesync.Value)
                return baseFreq;
            float amt = HudConfig.FxRippleDesyncAmount != null ? HudConfig.FxRippleDesyncAmount.Value : 0f;
            if (amt <= 0.0001f) return baseFreq;
            // Re-mix the animator's 0..9999 seed (Knuth multiplicative) so the frequency spread does
            // not lattice with the flicker/boot-stagger that reads the same Id seed. No string alloc.
            uint h = (uint)HudDocument.StableSeed(Def.Id) * 2654435761u;
            float u = (h & 0xFFFFu) / 65535f;                       // stable 0..1
            float mul = 1f + (u * 2f - 1f) * amt * 0.35f;           // up to +/-35% at amount 1
            return Mathf.Clamp(baseFreq * mul, 0.05f, 8f);
        }

        /// <summary>Per-element edge-FLOW speed. Companion to <see cref="RippleFreqFor"/>: frequency
        /// jitter alone desyncs the spatial wavelength but NOT the animation TEMPO (the shader time
        /// term keys off flow speed, not frequency), so panels narrower than ~180px still flowed in
        /// near-unison (2026-07-19 review). Jittering flow speed per element (independent seed) drifts
        /// the temporal phase apart at EVERY panel size. Multiplicative, so a frozen element (speed ~0)
        /// stays frozen; skipped entirely near the mesh flow/static threshold so a barely-flowing
        /// element can't be jittered across it (a visible pop). Unchanged when the toggle is off;
        /// clamped to the packable 0..4 range so the SDF flow-speed pack never overflows. Only varies
        /// an already-transmitted value, so every renderer desyncs with no shader/ABI change. This is
        /// a RENDER-time nudge only — the snapshot path (EdgeFlowFor into a Def) must stay un-jittered
        /// so saved profiles keep the authored speed.</summary>
        protected float RippleFlowSpeedFor(float baseSpeed)
        {
            if (Def == null || HudConfig.FxRippleDesync == null || !HudConfig.FxRippleDesync.Value)
                return baseSpeed;
            // Leave near-frozen speeds alone: multiplicative jitter of a sub-threshold speed could
            // flip a mesh element across the 0.004 flow/static-bake boundary.
            if (baseSpeed <= 0.01f) return baseSpeed;
            float amt = HudConfig.FxRippleDesyncAmount != null ? HudConfig.FxRippleDesyncAmount.Value : 0f;
            if (amt <= 0.0001f) return baseSpeed;
            // XOR-salt the seed so speed and frequency offsets are INDEPENDENT (an element is not
            // always both faster and higher-frequency). No string alloc.
            uint h = ((uint)HudDocument.StableSeed(Def.Id) ^ 0x9E3779B9u) * 2654435761u;
            float u = (h & 0xFFFFu) / 65535f;                       // stable 0..1
            float mul = 1f + (u * 2f - 1f) * amt * 0.30f;           // up to +/-30% at amount 1
            return Mathf.Clamp(baseSpeed * mul, 0f, 4f);
        }

        /// <summary>The 0.9.0 half of panel styling: per-element strength (uv0.x) + shared
        /// effect-material selection. Split from <see cref="ApplyGlass"/> so widgets that set
        /// Sheen/Spec directly with state overrides (hand boxes, equipment column — play-test:
        /// "no effects on the hand/1-6 slots") can opt their boxes into effects without
        /// disturbing their custom glass logic.</summary>
        protected void ApplyFx(IGlassSurface g)
        {
            if (g == null) return;

            // Rectangular PanelGraphic surfaces get the complete analytic renderer when (and
            // only when) its shared material can actually be assigned. This ordering is the
            // fail-soft invariant: an unavailable bundle or a Mask restriction must never leave
            // an SDF parameter grid drawing as a stock rectangular UI mesh.
            var panel = g.AsGraphic as PanelGraphic;
            // Corner style is pushed FIRST, because with an OLD bundle it still decides which
            // renderer this panel may take. Since 0.9.2.6 the analytic shader draws the chamfer
            // natively (superellipse exponent 1 = the L1 norm, whose zero contour IS the cut), so
            // a cut panel keeps the full analytic feature set — frost, chroma, halo v2, edge flow,
            // dissolve, shine, iridescence — differing from a rounded one only in corner shape.
            // An ABI-2 bundle cannot decode that exponent, so there the pre-existing fallback
            // stands: the panel keeps its SHAPE on the mesh renderer and loses only the extras.
            if (panel != null) panel.CornerCut = CornerCutFor();
            bool sdfWanted = panel != null
                && (!panel.CornersAreCut || Core.HudShaderStore.SdfCutAvailable)
                && HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                && Core.HudShaderStore.SdfAvailable;
            bool sdfAssigned = sdfWanted && HudFxMaterials.Assign(g.AsGraphic, "sdfglass");
            if (panel != null)
            {
                float edgeFadeX = Def != null ? Def.GetFFor(LayoutBare, "edgeFadeX", 0f) : 0f;
                float edgeFadeY = Def != null ? Def.GetFFor(LayoutBare, "edgeFadeY", 0f) : 0f;
                if (sdfAssigned)
                {
                    panel.SetSdfStyle(true,
                        SdfSquircleFor(), SdfGaussianFor(), RippleFlowSpeedFor(EdgeFlowFor()),
                        FrostAmountFor(), FrostDepthFor(), ChromaAmountFor(),
                        ShineAmountFor(), IridAmountFor(), DissolveFor(),
                        edgeFadeX, edgeFadeY, HaloHazeFor(), HaloBreathFor(),
                        HaloUnevenFor(), HaloFlowAuraFor(), HaloOrganicScaleFor());
                    // The SDF ABI carries independent final strengths; uv0.x's old combined
                    // volume is not used and must not force needless legacy mesh semantics.
                    g.FxStrength = 0f;
                    return;
                }

                panel.SetSdfStyle(false, 2f, false, 0f, 0f, 1f, 0f, 0f, 0f,
                    false, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
            }

            g.FxStrength = FxStrengthFor();

            // Material selection (one per graphic; frost wins over edge-fx): all shared
            // materials, so batching holds per family; Assign/Unassign are per-frame cheap
            // (dictionary lookups) and only churn on an actual toggle. Falls through to the
            // default UI material whenever the bundle/tier is off — the Tier A look.
            bool frost = FrostAmountFor() > 0.001f;
            // A flowing Shape needs SOME bundle material bound or its uv1 payload is inert —
            // the edgefx family is the natural host when frost isn't claiming the graphic.
            var flowPoly = g.AsGraphic as PolygonPanelGraphic;
            bool meshFlow = flowPoly != null
                && flowPoly.FlowSpeed > 0.004f && flowPoly.EdgeRipple > 0.004f;
            bool edgeFx = !frost
                && Core.HudShaderStore.TierBAvailable
                && (ShineAmountFor() > 0.001f || IridAmountFor() > 0.001f || DissolveFor()
                    || meshFlow);
            if (frost) { if (!HudFxMaterials.Assign(g.AsGraphic, "glass")) HudFxMaterials.Unassign(g.AsGraphic); }
            else if (edgeFx) { if (!HudFxMaterials.Assign(g.AsGraphic, "edgefx")) HudFxMaterials.Unassign(g.AsGraphic); }
            else HudFxMaterials.Unassign(g.AsGraphic);
        }

        private float SdfSquircleFor()
        {
            float global = HudConfig.SdfSquircle != null ? HudConfig.SdfSquircle.Value : 2f;
            if (UsesGlobalStyle) return global;
            float own = Def != null ? Def.GetFFor(LayoutBare, "squircle", -1f) : -1f;
            return own >= 2f ? Mathf.Clamp(own, 2f, 8f) : global;
        }

        private bool SdfGaussianFor()
        {
            bool global = HudConfig.SdfGaussianHalo != null && HudConfig.SdfGaussianHalo.Value;
            return UsesCustomStyle && Def != null ? Def.GetBFor(LayoutBare, "gaussianHalo", global) : global;
        }

        private float EdgeFlowFor()
            => OwnOrGlobal("edgeFlow", HudConfig.FxEdgeFlowSpeed);

        private float HaloHazeFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn);
            return tierA && glowOn
                ? Mathf.Clamp01(NewSdfOwnOrGlobal("glowHaze", HudConfig.FxGlowHaze)) : 0f;
        }

        private float HaloBreathFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn);
            bool auraOn = HaloFlowAuraFor() > 0.001f;
            bool breathOn = NewSdfFeatureOn("customGlowBreathOn", HudConfig.FxGlowBreathOn);
            return tierA && (glowOn || auraOn) && breathOn
                ? Mathf.Clamp01(NewSdfOwnOrGlobal("glowBreath", HudConfig.FxGlowBreath)) : 0f;
        }

        private float HaloUnevenFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn);
            bool auraOn = HaloFlowAuraFor() > 0.001f;
            bool unevenOn = NewSdfFeatureOn("customGlowUnevenOn", HudConfig.FxGlowUnevenOn);
            return tierA && (glowOn || auraOn) && unevenOn
                ? Mathf.Clamp01(NewSdfOwnOrGlobal("glowUneven", HudConfig.FxGlowUneven)) : 0f;
        }

        /// <summary>Footprint multiplier of the unevenness noise (0.25x..4x, 1 = classic).
        /// A modifier of the uneven feature — neutral whenever unevenness resolves off.</summary>
        private float HaloOrganicScaleFor()
        {
            if (HaloUnevenFor() <= 0.001f) return 1f;
            float v = NewSdfOwnOrGlobal("glowOrganicScale", HudConfig.FxGlowOrganicScale);
            // A -1 sentinel can reach the Custom branch raw (ResetEffectsToGlobal writes it
            // into every _fxKeys slot) — anything below the slider floor means "neutral".
            return v < 0.2f ? 1f : Mathf.Clamp(v, 0.25f, 4f);
        }

        private float HaloFlowAuraFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool edgeOn = StyleFeatureOn("customRippleOn", HudConfig.FxEdgeLightOn);
            bool auraOn = NewSdfFeatureOn("customGlowFlowOn", HudConfig.FxGlowFlowAuraOn);
            return tierA && edgeOn && auraOn
                ? Mathf.Clamp(NewSdfOwnOrGlobal("glowFlowAura", HudConfig.FxGlowFlowAura), 0f, 2f) : 0f;
        }

        private float FrostDepthFor()
            => Mathf.Clamp01(OwnOrGlobal("frostDepth", HudConfig.FrostDepth));

        private float ShineAmountFor()
        {
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            if (!tier || Def == null) return 0f;
            bool globalOn = HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            float global = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            if (UsesGlobalStyle) return globalOn ? Mathf.Clamp(global, 0f, 2f) : 0f;
            return Def.GetBFor(LayoutBare, "customShineOn", globalOn)
                ? Mathf.Clamp(Def.GetFFor(LayoutBare, "customShine", global), 0f, 2f) : 0f;
        }

        private float IridAmountFor()
        {
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            if (!tier || Def == null) return 0f;
            bool globalOn = HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            float global = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            if (UsesGlobalStyle) return globalOn ? Mathf.Clamp01(global) : 0f;
            return Def.GetBFor(LayoutBare, "customIridOn", globalOn)
                ? Mathf.Clamp01(Def.GetFFor(LayoutBare, "customIrid", global)) : 0f;
        }

        private float FrostAmountFor()
        {
            bool tier = HudBackdrop.Active && HudConfig.FxTierC != null && HudConfig.FxTierC.Value;
            if (!tier || Def == null) return 0f;
            float global = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f;
            if (UsesGlobalStyle) return Mathf.Clamp01(global);
            return Def.GetBFor(LayoutBare, "customFrostOn", true)
                ? Mathf.Clamp01(Def.GetFFor(LayoutBare, "customFrost", global)) : 0f;
        }

        private float ChromaAmountFor()
        {
            if (FrostAmountFor() <= 0.001f || Def == null) return 0f;
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            bool globalOn = HudConfig.FxChromaOn != null && HudConfig.FxChromaOn.Value;
            float global = HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f;
            if (!tier) return 0f;
            if (UsesGlobalStyle) return globalOn ? Mathf.Clamp01(global) : 0f;
            return Def.GetBFor(LayoutBare, "customChromaOn", globalOn)
                ? Mathf.Clamp01(Def.GetFFor(LayoutBare, "customChroma", global)) : 0f;
        }

        private bool DissolveFor()
        {
            if (Def == null || HudConfig.FxTierB == null || !HudConfig.FxTierB.Value) return false;
            // Through the SHARED resolver, not the raw legacy bool. The old body short-circuited on
            // UsesGlobalStyle, so on the shipped (all-Global) profile a per-element "Dissolve = Off"
            // never reached the shader — and worse, the ANIMATOR did honour it, so the panel guttered
            // out in 0.4s while its shader kept sweeping the 1.2s frontier. The registry already
            // folds in the FxDissolveBoot master.
            return HudTransitionFx.Resolve(Def, "fxDissolve", LayoutBare) > 0.001f;
        }

        /// <summary>Legacy-shader modulation (0..1) baked into uv0.x after resolving the
        /// element's coherent style source. See <see cref="LegacyMultiplier"/>.</summary>
        protected float FxStrengthFor()
        {
            if (Def == null) return 0f;
            float shineGlobal = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            float iridGlobal = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            float frostGlobal = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 0f;
            float chromaGlobal = HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f;
            float shine = LegacyMultiplier(ShineAmountFor(), shineGlobal);
            float irid = LegacyMultiplier(IridAmountFor(), iridGlobal);
            float frost = LegacyMultiplier(FrostAmountFor(), frostGlobal);
            float chroma = LegacyMultiplier(ChromaAmountFor(), chromaGlobal);
            return Mathf.Max(Mathf.Max(shine, irid), Mathf.Max(frost, chroma));
        }

        // HudEdgeFX/HudGlass have one historical per-element scalar. Converting each resolved
        // final amount back to a shared-global multiplier makes Global exact and makes Custom
        // on/off choices work on the fail-soft/Polygon path. Different Custom strengths remain
        // an unavoidable approximation on that old ABI; the analytic path carries them all.
        private static float LegacyMultiplier(float effective, float global)
        {
            if (effective <= 0.001f || global <= 0.001f) return 0f;
            return Mathf.Clamp01(effective / global);
        }

        // ---- pulse (Tier A): a uniform CanvasRenderer tint on the element's OWN marked
        // graphics. Deliberately NOT CanvasGroup.alpha (HudAnimator owns it), NOT localScale
        // (CRT collapse owns it), NOT a re-mesh (adversarial review 2026-07-13). ----

        private List<Graphic> _fxGraphics;     // lazily cached IHudFxGraphic-marked children
        private bool _pulseTinted;             // true while a non-white tint is applied

        /// <summary>Apply this frame's breathing pulse, if the element opted in (fxPulse,
        /// default OFF — breathing is a per-element accent, not a default). Called by
        /// HudSystem's content loop after UpdatePanel. Safe on borrow widgets: only graphics
        /// implementing <see cref="IHudFxGraphic"/> are tinted, and borrowed vanilla graphics
        /// never carry the marker.</summary>
        internal void ApplyPulse()
        {
            // Through the SHARED resolver. The old body ANDed in !UsesGlobalStyle, which made the
            // new "Breathing pulse" tri-state row completely inert on every Global-styled element —
            // i.e. the whole shipped profile: a visible, settable control that did nothing. The
            // registry folds in the FxPulseOn master and the tri-state, so only Tier A stays here.
            bool tierA = Def != null && HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            float amt = tierA ? TransitionAmt("fxPulse") : 0f;

            if (amt <= 0f)
            {
                if (_pulseTinted) TintFxGraphics(Color.white); // one-shot reset on opt-out
                _pulseTinted = false;
                return;
            }

            float speed = HudConfig.FxPulseSpeed != null ? HudConfig.FxPulseSpeed.Value : 0.5f;
            float depth = (HudConfig.FxPulseDepth != null ? HudConfig.FxPulseDepth.Value : 0.25f) * amt;
            // Stable per-element phase offset (seeded by the Id hash) so a wall of pulsing
            // elements breathes organically instead of in lockstep. StableSeed, NOT GetHashCode:
            // string.GetHashCode is randomised per process on some runtimes, so the pulse phases
            // reshuffled on every launch / F6 reload (the very jitter StableSeed exists to avoid).
            float phase = Def.Id != null ? HudDocument.StableSeed(Def.Id) / 10000f : 0f;
            float s = 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime * speed + phase) * 2f * Mathf.PI);
            float f = Mathf.Clamp01(1f - depth * s);
            TintFxGraphics(new Color(f, f, f, 1f));
            _pulseTinted = true;
        }

        private void TintFxGraphics(Color c)
        {
            if (_fxGraphics == null)
            {
                _fxGraphics = new List<Graphic>();
                if (Root != null)
                    foreach (var g in Root.GetComponentsInChildren<Graphic>(true))
                        if (g is IHudFxGraphic) _fxGraphics.Add(g); // marker filter = borrow-safe
            }
            for (int i = 0; i < _fxGraphics.Count; i++)
            {
                var g = _fxGraphics[i];
                if (g != null && g.canvasRenderer != null) g.canvasRenderer.SetColor(c);
            }
        }

        /// <summary>The universal editable surface; widget views append their own props.
        /// Rendered generically by the F9 designer (Phase 4) — same contract as the
        /// config popup's DrawConfigWidget, but for document fields.</summary>
        /// <summary>Whether this element shows in BOTH bare and a live (suit/robot) tier — the only
        /// case where a separate per-mode layout is meaningful.</summary>
        internal static bool IsBoth(HudTierMask t)
            => (t & HudTierMask.Bare) != 0 && (t & (HudTierMask.Suited | HudTierMask.Robot)) != 0;

        /// <summary>The slot an F9 STYLE edit writes to: the explicitly previewed tier when this
        /// element FORKS it, else Base. Keying off the explicit preview (not the render tier) is
        /// unchanged from the old EditBare contract — a 'Live' preview never silently forks — and
        /// the fork gate is what stops an edit made with per-tier OFF from writing a "b_" key no
        /// read would ever see.</summary>
        internal static HudStyleSlot EditSlot(HudElementDef d)
            => d == null ? HudStyleSlot.Base : d.ResolveSlot(EditTargetSlot);

        /// <summary>Whether an editor STYLE field writes the bare override rather than the shared
        /// base. Bool shorthand for <see cref="EditSlot"/> == Bare, which is what every widget's
        /// <c>DescribeProps</c> passes to the tier-aware accessors.</summary>
        internal static bool EditBare(HudElementDef d)
            => EditSlot(d) == HudStyleSlot.Bare;

        /// <summary>Whether an editor LAYOUT field writes the BARE layout override rather than the
        /// base (live) layout: only when the preview is EXPLICITLY bare AND the element is a "Both"
        /// element. The layout fork ("bLayout"/"bX"…) is INDEPENDENT of the per-tier STYLE opt-in —
        /// it has its own explicit seeded toggle and predates Wave C — so it must NOT be gated on
        /// <c>ForksSlot</c>. A single-mode element has one layout, so it always edits base.</summary>
        internal static bool EditBareLayout(HudElementDef d)
            => EditBareTier && d != null && IsBoth(d.Tiers);

        // The per-element visibility dropdown. "Live" = the powered suit/robot HUD; "Both"
        // additionally remembers a separate location per mode.
        internal static readonly string[] ShowModeNames = { "Bare mode", "Live mode", "Both" };

        private static int TiersToMode(HudTierMask t)
        {
            bool bare = (t & HudTierMask.Bare) != 0;
            bool live = (t & (HudTierMask.Suited | HudTierMask.Robot)) != 0;
            if (bare && !live) return 0; // Bare mode
            if (live && !bare) return 1; // Live mode
            return 2;                    // Both (or None, which Sanitize repairs to All anyway)
        }

        private static HudTierMask ModeToTiers(int mode)
        {
            switch (mode)
            {
                case 0: return HudTierMask.Bare;
                case 1: return HudTierMask.Suited | HudTierMask.Robot;
                default: return HudTierMask.Bare | HudTierMask.Suited | HudTierMask.Robot;
            }
        }

        public virtual void DescribeProps(List<HudProp> into)
        {
            var d = Def;
            int styleStart = into.Count;
            into.Add(HudProp.Header("Style"));
            // ONE switch, two states. Unchecking ALWAYS seeds a complete snapshot from the
            // CURRENTLY EFFECTIVE values first (every read runs under the OLD mode), so every
            // slider below pops open at exactly the value the element is already showing — no
            // jump, no stale profile values.
            //
            // THE GUARD THAT USED TO SIT HERE WAS THE BUG (Phase 0a, 2026-07-26). It skipped the
            // seed whenever "customStyleReady" was already true — and that flag is STICKY: any
            // previous separation, "Snapshot ALL as Custom", or the legacy fold left it set even
            // after the element went back to Global. Unchecking then revealed a snapshot frozen at
            // some unrelated past moment (FlorpyDorp's shipped profiles carry customFrost 0.125
            // against a live global of 0.87, and spec/sheen/glow all 0), which is exactly the
            // "the frost and the chromatic aberration dissipate" report.
            //
            // The cost, stated plainly: a DORMANT Custom design no longer survives a Global
            // round-trip. "Uncheck -> recheck -> uncheck" now restores what is on screen, not the
            // values you had before. Per FlorpyDorp's decision 5 the dormant keys are still KEPT
            // (nothing is deleted on re-follow) — they are simply overwritten by the fresh seed.
            into.Add(WithId(HudProp.Bool("Follow F9 global style (theme + effects)",
                () => UsesGlobalStyle,
                v =>
                {
                    // Per-SLOT since 0.9.2.5: separating while the BARE tab is up must snapshot
                    // into the bare slot, not into the shared base every tier inherits (that was
                    // the second, silent leak the Wave C audit found).
                    var slot = EditSlot(d);
                    if (v)
                    {
                        d.SetIFor(slot, "styleSource", StyleGlobal);
                        return;
                    }
                    SeedCustomStyleFromEffective(d, slot);
                    d.SetIFor(slot, "styleSource", StyleCustom);
                }), "styleSource"));
            MarkProps(into, styleStart, HudPropGroup.Appearance);

            AddUnifiedLayoutProps(into, d);
            AddUnifiedAppearanceProps(into, d);
            AddUnifiedEffectProps(into, d);
        }

        private void AddUnifiedLayoutProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            into.Add(HudProp.Header("Layout"));
            // GEOMETRY forks on the LAYOUT toggle (EditBareLayout), never on the per-tier STYLE
            // opt-in — two independent features. The StableIds also let SeedSlotFromBase skip
            // these rows: seeding a style slot must never fork the layout as a side effect.
            into.Add(WithId(HudProp.Anchor("Anchor", () => (int)d.AnchorFor(EditBareLayout(d), LayoutMode),
                v => d.SetAnchorFor(EditBareLayout(d), LayoutMode, (HudAnchor)v)), "elAnchor"));
            into.Add(WithId(HudProp.F("X", () => d.XFor(EditBareLayout(d), LayoutMode),
                v => d.SetXFor(EditBareLayout(d), LayoutMode, v), -2000f, 2000f), "elX"));
            into.Add(WithId(HudProp.F("Y", () => d.YFor(EditBareLayout(d), LayoutMode),
                v => d.SetYFor(EditBareLayout(d), LayoutMode, v), -2000f, 2000f), "elY"));
            into.Add(WithId(HudProp.F("Width", () => d.WFor(EditBareLayout(d), LayoutMode),
                v => d.SetWFor(EditBareLayout(d), LayoutMode, Mathf.Max(2f, v)), 2f, 2200f), "elW"));
            into.Add(WithId(HudProp.F("Height", () => d.HFor(EditBareLayout(d), LayoutMode),
                v => d.SetHFor(EditBareLayout(d), LayoutMode, Mathf.Max(2f, v)), 2f, 1300f), "elH"));
            into.Add(WithId(HudProp.F("Width % (-1 = fixed)", () => d.WPctFor(EditBareLayout(d), LayoutMode),
                v => d.SetWPctFor(EditBareLayout(d), LayoutMode, v), -1f, 1f), "elWPct"));
            into.Add(WithId(HudProp.F("Height % (-1 = fixed)", () => d.HPctFor(EditBareLayout(d), LayoutMode),
                v => d.SetHPctFor(EditBareLayout(d), LayoutMode, v), -1f, 1f), "elHPct"));
            into.Add(WithId(HudProp.I("Z order", () => d.Z,
                v => { d.Z = v; HudSystem.RequestZResort(); }, -100, 100), "zOrder"));
            into.Add(WithId(HudProp.Tier("Show in (Bare / Suited)", () => (int)d.Tiers,
                v => { d.Tiers = (HudTierMask)v; if (!IsBoth(d.Tiers)) { d.SetBareLayout(false); d.ClearVisualBareOverrides(); } }),
                "tiers"));
            MarkProps(into, start, HudPropGroup.Layout);
        }

        private void AddUnifiedAppearanceProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            // Per-mode edit: every Appearance value reads/writes the SUIT base OR the BARE override
            // depending on the active mode tab (EditBare(d) is true only while the Bare tab is
            // selected on a Both element). Bare inherits the base until a property is forked.
            into.Add(HudProp.Header("Appearance"));
            // Colours are per-element in BOTH style states (see GlobalOr) and per-MODE too: a
            // palette-name ref tracks the F9 palette live, a hex literal stands alone.
            if (UsesAccentColor)
                into.Add(HudProp.Color("Text / accent", () => d.TextColorFor(EditBare(d)), v => d.SetTextColorFor(EditBare(d), v)));
            if (SupportsPanelAppearance)
            {
                into.Add(HudProp.Color("Fill", () => d.FillFor(EditBare(d)), v => d.SetFillFor(EditBare(d), v)));
                if (SupportsCustomBorderColor)
                    into.Add(HudProp.Color("Border", () => d.BorderFor(EditBare(d)), v => d.SetBorderFor(EditBare(d), v)));
            }
            else if (SupportsBorderOnlyChrome)
            {
                // No panel, but the widget draws a ring from these two — see SupportsBorderOnlyChrome.
                // Only the COLOUR ref lives here: it resolves in both states and is deliberately not
                // follow-gated (plan §3.4). The ring's WIDTH, edge light, border fade and halo are
                // registry rows and are drawn by the style pages on the Effects tab, under the same
                // captions F9 uses for them — see AddUnifiedEffectProps.
                into.Add(HudProp.Color("Ring / outline colour", () => d.BorderFor(EditBare(d)), v => d.SetBorderFor(EditBare(d), v)));
            }
            // SIZING / GLASS / EFFECTS are no longer mirrored here by hand. Every one of those rows
            // (border width, the four radii, corner style, edge softness, sheen, edge light, corner
            // shape, gaussian halo, the trapezoid insets, the element font scale) is a HudStyleFx
            // row, and the Effects tab's Theme page renders it from the registry with F9's own
            // caption and range. Keeping a second copy here is precisely the five-place duplication
            // the registry exists to delete (plan §1.7).
            into.Add(HudProp.Note(UsesGlobalStyle
                ? "Sizing, glass and effects: Effects tab (this element follows the F9 globals)"
                : "Sizing, glass and effects: Effects tab (Theme / Glass / Edges / Glow pages)"));
            MarkProps(into, start, HudPropGroup.Appearance);
        }

        // ---- SHARED-GLOBAL HONESTY (Phase 0d, 2026-07-26) -----------------------------------
        //
        // FlorpyDorp: "backdrop darkening, tint, downsample and re-blur SAY they're shared globals,
        // but off-global they stop working and there's no knob to adjust them."
        //
        // The renderer half of that is a misreading and needed no change: there is no styleSource
        // reference anywhere in HudSystem / HudBackdrop / HudFxMaterials, so a Custom panel samples
        // the same _UiaBlurTex pyramid and receives the same _FrostTint/_FrostDarken uniforms as a
        // following one. What actually broke was the CONTRACT: these knobs were named in a bare
        // sentence, with no value, no way to reach them, and no statement that editing one changes
        // every panel on screen. Everything below is that contract, and nothing else.
        //
        // Three wordings, used consistently and nowhere paraphrased:
        //   - a shared knob that TRAVELS with the profile theme;
        //   - a shared knob that is MACHINE-LOCAL (HudTheme.Exclude: it trades frame time on the
        //     player's own machine, so an imported theme must never move it — invisible to authors
        //     until now);
        //   - a jump/path row, which doubles as the button caption so it still reads as a path
        //     when the F9 window happens to be closed.

        private const string SharedGlobalWording = "shared global - edits affect EVERY panel";
        private const string MachineLocalWording =
            "shared global - machine-local, does not travel with the theme";

        /// <summary>The jump button. Its CAPTION is the path, so it degrades to a readable
        /// signpost if the F9 window is not on screen to receive the request. Both the tab and the
        /// sub-tab id come from <see cref="HudStyleFx.F9Home"/>, so a section that moves in F9 moves
        /// this button with it.</summary>
        private static void AddJump(List<HudProp> into, string tab, string subTabId,
            string subTabName, string id)
        {
            into.Add(HudProp.Button("Open F9 > " + tab + " > " + subTabName, "jmp_" + id,
                () => Windows.HudEditorWindow.RequestEditorTab(tab, subTabId),
                "Opens the F9 HUD editor on that sub-tab. Everything there is shared by every panel."));
        }

        // ================= THE STYLE PAGES (Phase 2, 2026-07-27) =============================
        //
        // The element popup's style area is a NESTED TAB BAR whose pages are F9's own sub-tabs —
        // Theme, Glass, Edges, Glow, Bloom, Alerts, Transitions (FlorpyDorp's decision 4). Every
        // row on every page comes from ONE `foreach` over HudStyleFx.All filtered by category, in
        // table order, with the table's label, range, section grouping and note lines. The ~230-line
        // hand-written mirror this replaces (plus the separate Polyline mirror and the appearance
        // duplication) was the thing that could drift from the global menu; there is nothing left to
        // drift. Parity is now structural, exactly as it already was for the transitions family.
        //
        // What a row looks like, in the four cases the registry distinguishes:
        //
        //   SHARED-ONLY  physically one value for the whole HUD (one blur pyramid, one material
        //                uniform, one clock, one post pass). ALWAYS rendered, in BOTH style states,
        //                read-only, showing the live global — never silently absent, because an
        //                author who cannot find "light angle" here must be told where it went. The
        //                standing wording and a jump to the owning F9 sub-tab close each RUN of
        //                shared rows rather than repeating under all 25 of them on the Bloom page.
        //
        //   PER-ELEMENT  editable while the element is Custom. While it FOLLOWS the globals the page
        //                shows a one-line summary instead of the rows — the convention this popup
        //                already used ("Panel appearance follows the F9 Effects globals"), kept so
        //                Phase 2 changes no behaviour it does not have to.
        //
        //   NULL-GLOBAL  no global exists (the box-end fades, the trapezoid insets, the element font
        //                scale, energy smoothness, the portrait ring, the frost opt-out). Labelled
        //                "(per-element only)". The ones the RENDERER reads in both states
        //                (StateIndependent) stay editable while following — that is deliberate,
        //                pre-existing behaviour for the fades and the insets, and must not regress.
        //
        //   INERT        the surface exists but the effect cannot run right now (no analytic shader,
        //                old bundle, Cut corners on an ABI-2 bundle, legacy single-scalar mesh).
        //                The control still renders — its value is stored and returns the moment the
        //                capability does — with a per-ROW reason line instead of the old blanket
        //                section header.

        /// <summary>The visible caption of a registry row: everything before ImGui's "##" id
        /// separator. F9 shows exactly this, so the two menus read identically; the popup gets its
        /// widget identity from <see cref="HudProp.StableId"/> instead.</summary>
        private static string FxLabel(HudStyleFxDef def)
        {
            string s = def != null ? (def.Label ?? "") : "";
            int i = s.IndexOf("##", System.StringComparison.Ordinal);
            return i >= 0 ? s.Substring(0, i) : s;
        }

        /// <summary>Does this element's TYPE have the surface this row belongs to?</summary>
        private bool FxApplies(HudStyleFxDef def)
            => def != null && (def.Applies == null || def.Applies(this));

        /// <summary>True when this element is actually being drawn by the analytic panel shader —
        /// the condition every <c>SdfOnly</c> row and every <c>LegacyApprox</c> caveat turns on.
        /// Mirrors <see cref="ApplyFx"/>'s own `sdfWanted` test, including the ABI-2 Cut fallback.</summary>
        private bool FxOnAnalyticPath
        {
            get
            {
                if (!SupportsAnalyticPanel) return false;
                if (HudConfig.SdfPanels == null || !HudConfig.SdfPanels.Value) return false;
                if (!Core.HudShaderStore.SdfAvailable) return false;
                bool cut = CornerCutFor() == 1
                    || (CornerCutFor() < 0 && HudConfig.HudCornerStyle != null
                        && HudConfig.HudCornerStyle.Value == 1);
                return !cut || Core.HudShaderStore.SdfCutAvailable;
            }
        }

        /// <summary>Why an <c>SdfOnly</c> row cannot do anything on THIS element right now, or null
        /// when it can. Generalises the two blanket headers the popup used to print into a per-row
        /// answer, which is what the plan's "capability-inert rows render with a reason" asks for.</summary>
        private string FxInertReason(HudStyleFxDef def)
        {
            if (def == null || !def.SdfOnly || FxOnAnalyticPath) return null;
            // edgeFlow is the ONE SdfOnly row with a real mesh path: a freeform shape and a drawn
            // line animate their baked uv1 payload through the edgefx family, so there it is inert
            // only when the resident bundle predates that ABI.
            if (string.Equals(def.Key, "edgeFlow", System.StringComparison.Ordinal)
                && !SupportsAnalyticPanel)
                return Core.HudShaderStore.FlowAbiAvailable ? null
                    : "the resident effects bundle has no flow ABI - the shimmer stays frozen";
            if (!SupportsAnalyticPanel)
            {
                if (Def != null && Def.Type == HudElementType.Shape)
                    return "a freeform pen shape renders the mesh halo, not the analytic panel";
                if (Def != null && Def.Type == HudElementType.Polyline)
                    return "a drawn line renders the mesh halo, not the analytic panel";
                return "this element owns no analytic panel surface";
            }
            if (HudConfig.SdfPanels == null || !HudConfig.SdfPanels.Value)
                return "sharp glass panels are OFF (F9 > Theme > Typography & boxes)";
            if (!Core.HudShaderStore.SdfAvailable)
                return "the resident effects bundle has no analytic panel shader";
            return "this panel's corners are Cut and this bundle cannot cut on the analytic shader";
        }

        /// <summary>Per-capability slider ceiling. Only the halo radius has one: the MESH halo
        /// clamps at 160 px, so a 320 ceiling on a shape or a line would leave the top half of the
        /// slider doing nothing (Phase 1 report, discrepancy 6).</summary>
        private float FxMaxFor(HudStyleFxDef def)
        {
            if (def == null) return 1f;
            if (string.Equals(def.Key, "glowWidth", System.StringComparison.Ordinal)
                && !SupportsAnalyticPanel) return 160f;
            return def.Max;
        }

        /// <summary>Is the companion feature that gates this row ON for this element? Following ⇒
        /// the global, Custom ⇒ the element's own key with the global as its default — the same one
        /// missing-key rule Phase 0b gave every resolver.</summary>
        private bool FxFeatureOn(HudElementDef d, string onParamKey)
        {
            if (string.IsNullOrEmpty(onParamKey)) return true;
            bool gv = FxCompanionDefault(onParamKey);
            if (UsesGlobalStyle || d == null) return gv;
            return d.GetBFor(EditBare(d), onParamKey, gv);
        }

        /// <summary>What an ABSENT companion bool means. Every one of them is some registry row's
        /// own ParamKey, so the answer is that row's global — except "customFrostOn", which has no
        /// global (the global IS the strength slider) and whose honest literal default is ON, the
        /// same one <see cref="FrostAmountFor"/> uses.</summary>
        private static bool FxCompanionDefault(string onParamKey)
        {
            if (string.Equals(onParamKey, "customFrostOn", System.StringComparison.Ordinal))
                return true;
            var comp = HudStyleFx.FindByParam(onParamKey);
            return comp != null && comp.HasGlobal && comp.GlobalBool;
        }

        /// <summary>Whether a row is drawn at all on this element's page, before its section gate.</summary>
        private bool FxRowVisible(HudElementDef d, HudStyleFxDef def)
        {
            if (def == null || !FxApplies(def)) return false;
            if (def.SharedOnly) return def.MasterOn;       // exactly F9's own per-row master gate
            if (!FxFeatureOn(d, def.OnParamKey)) return false;
            if (!def.HasGlobal) return def.StateIndependent || UsesCustomStyle;
            return UsesCustomStyle;                        // else: the page's follow summary covers it
        }

        /// <summary>The section gates a page inherits from its F9 sub-tab, resolved PER ELEMENT
        /// (F9 asks the global, the popup asks this element's own feature state).</summary>
        private bool FxSectionOpen(HudElementDef d, HudFxCategory cat, string section)
        {
            if (cat == HudFxCategory.Surface)
                return !string.Equals(section, HudStyleFx.SecSdf, System.StringComparison.Ordinal)
                    || (HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value);
            if (cat == HudFxCategory.Edges)
                return !string.Equals(section, HudStyleFx.SecEdge, System.StringComparison.Ordinal)
                    || FxFeatureOn(d, "customRippleOn");
            if (cat == HudFxCategory.Glow)
            {
                bool envelope = string.Equals(section, HudStyleFx.SecEnvelope, System.StringComparison.Ordinal)
                    || string.Equals(section, HudStyleFx.SecAdvancedHalo, System.StringComparison.Ordinal);
                if (!envelope) return true;
                // The shared halo envelope is live when a halo OR the flowing aura is on — the same
                // test F9's Glow sub-tab makes against the globals.
                return FxFeatureOn(d, "customGlowOn")
                    || (FxFeatureOn(d, "customRippleOn") && FxFeatureOn(d, "customGlowFlowOn"));
            }
            return true;
        }

        /// <summary>The accent heading F9 prints above a section, or null where it prints none.</summary>
        private static string FxSectionHeader(HudFxCategory cat, string section)
        {
            switch (section)
            {
                case HudStyleFx.SecText: return "TEXT";
                case HudStyleFx.SecBox: return "BOX SHAPE";
                case HudStyleFx.SecSdfMaster: return "SHARP GLASS PANELS";
                case HudStyleFx.SecTierA: return "CORE EFFECTS";
                case HudStyleFx.SecBoxEndFade: return "BOX END FADE";
                case HudStyleFx.SecTierB: return "ANIMATED GLASS";
                case HudStyleFx.SecTierC: return "FROSTED BACKDROP";
                case HudStyleFx.SecFrostPerf: return "PERFORMANCE";
                case HudStyleFx.SecEdgeMaster: return "EDGE ENERGY";
                case HudStyleFx.SecHalo: return "GLOW HALO";
                case HudStyleFx.SecEnvelope: return "SHARED HALO / FLOWING-AURA ENVELOPE";
                case HudStyleFx.SecPulseShape: return "BREATHING PULSE (shape)";
                case HudStyleFx.SecAdvancedHalo: return "HALO FINE DETAIL";
                case HudStyleFx.SecBloomMaster: return "HUD BLOOM";
                case HudStyleFx.SecBloomBase: return "BASE GLOW";
                case HudStyleFx.SecBloomPulse: return "BREATHING BLOOM";
                case HudStyleFx.SecBloomReact: return "STATE-REACTIVE BLOOM";
                case HudStyleFx.SecBloomBand2: return "BORDER / HIGHLIGHT BLOOM";
                case HudStyleFx.SecBloomRes: return "PERFORMANCE";
                case HudStyleFx.SecBloomBias: return "BLOOM COLOUR BIAS";
                case HudStyleFx.SecAlertMaster: return "WARNING PULSE";
                case HudStyleFx.SecElementOnly:
                    return cat == HudFxCategory.Surface ? "RING HALO" : null;
                default: return null;
            }
        }

        /// <summary>The tier/master warning F9 states once per family, kept because the popup shows
        /// the rows anyway (their values are stored and return when the master does) — so it must
        /// say why nothing is happening.</summary>
        private static string FxSectionCaveat(HudFxCategory cat, string section)
        {
            if (cat == HudFxCategory.Glass)
            {
                if (string.Equals(section, HudStyleFx.SecCore, System.StringComparison.Ordinal)
                    || string.Equals(section, HudStyleFx.SecBoxEndFade, System.StringComparison.Ordinal))
                    return HudConfig.FxTierA == null || !HudConfig.FxTierA.Value
                        ? "  Core effects are OFF globally - these values are inactive." : null;
                if (string.Equals(section, HudStyleFx.SecAnimGlass, System.StringComparison.Ordinal))
                    return HudConfig.FxTierB == null || !HudConfig.FxTierB.Value
                        ? "  Animated glass is OFF globally - these values are inactive." : null;
                if (string.Equals(section, HudStyleFx.SecFrost, System.StringComparison.Ordinal))
                    return HudConfig.FxTierC == null || !HudConfig.FxTierC.Value
                        ? "  Frosted backdrop is OFF globally - these values are inactive." : null;
            }
            if ((cat == HudFxCategory.Edges || cat == HudFxCategory.Glow)
                && (HudConfig.FxTierA == null || !HudConfig.FxTierA.Value)
                && (string.Equals(section, HudStyleFx.SecEdgeMaster, System.StringComparison.Ordinal)
                    || string.Equals(section, HudStyleFx.SecHalo, System.StringComparison.Ordinal)))
                return "  Core effects are OFF globally - these values are inactive.";
            return null;
        }

        /// <summary>The trailing line F9 prints under a section — the pointers to controls that
        /// live one tab over, and the one overdraw warning. Kept so a page is not merely the rows.</summary>
        private static string FxSectionFooter(HudFxCategory cat, string section)
        {
            if (cat == HudFxCategory.Glass
                && string.Equals(section, HudStyleFx.SecAnimGlass, System.StringComparison.Ordinal))
                return "  Dissolve reveal: see the Transitions page.";
            if (cat == HudFxCategory.Glow)
            {
                if (string.Equals(section, HudStyleFx.SecEnvelope, System.StringComparison.Ordinal))
                    return "  Extreme radius increases transparent GPU overdraw.";
                if (string.Equals(section, HudStyleFx.SecPulseShape, System.StringComparison.Ordinal))
                    return "  Breathing pulse master: see the Transitions page.";
            }
            return null;
        }

        /// <summary>One page of the style tab bar: every registry row of one category, in table
        /// order. Appends nothing when the page would be empty (a drawn line has no Theme page).</summary>
        private void AddStylePage(List<HudProp> pages, HudElementDef d, string caption,
            HudFxCategory cat)
        {
            var page = new List<HudProp>();
            var all = HudStyleFx.All;
            string lastSection = null;
            HudStyleFxDef runDef = null;     // the open run of shared rows, if any
            bool runLocal = false;           // ... and whether it held a machine-local one
            bool anyFollowed = false;        // a per-element row was suppressed by the follow state

            for (int i = 0; i < all.Length; i++)
            {
                HudStyleFxDef def = all[i];
                if (def == null || def.Category != cat) continue;
                if (!FxApplies(def)) continue;
                if (!FxSectionOpen(d, cat, def.Section)) continue;
                if (!FxRowVisible(d, def))
                {
                    if (!def.SharedOnly && UsesGlobalStyle && def.HasGlobal) anyFollowed = true;
                    continue;
                }
                if (!string.Equals(def.Section, lastSection, System.StringComparison.Ordinal))
                {
                    FxCloseSharedRun(page, ref runDef, ref runLocal);
                    string foot = FxSectionFooter(cat, lastSection);
                    if (foot != null) page.Add(HudProp.Note(foot));
                    string head = FxSectionHeader(cat, def.Section);
                    if (head != null) page.Add(HudProp.Header(head));
                    string caveat = FxSectionCaveat(cat, def.Section);
                    if (caveat != null) page.Add(HudProp.Note(caveat));
                    lastSection = def.Section;
                }
                if (def.SharedOnly)
                {
                    page.Add(HudProp.Note("  " + FxLabel(def).Trim() + ":  " + def.GlobalText
                        + (def.DoesNotTravel ? "   [machine-local]" : ""), def.Tip));
                    runDef = def;
                    if (def.DoesNotTravel) runLocal = true;
                    continue;
                }
                FxCloseSharedRun(page, ref runDef, ref runLocal);
                AddFxEditableRow(page, d, def);
            }
            FxCloseSharedRun(page, ref runDef, ref runLocal);
            string tailFoot = FxSectionFooter(cat, lastSection);
            if (tailFoot != null) page.Add(HudProp.Note(tailFoot));
            // Nothing to say at all — this element has no surface this family touches. A page whose
            // rows are merely SUPPRESSED by the follow state is still shown: it must carry the
            // summary that explains where its controls went.
            if (page.Count == 0 && !anyFollowed) return;
            if (anyFollowed)
            {
                page.Insert(0, HudProp.Note(
                    "This element FOLLOWS the F9 globals - untick \"Follow F9 global style\""));
                page.Insert(1, HudProp.Note("on the Appearance tab to give it its own values."));
            }
            pages.Add(HudProp.TabPage(caption, page));
        }

        /// <summary>Close a run of read-only shared rows with the standing wording and ONE jump to
        /// the F9 sub-tab that owns them. Per RUN rather than per ROW on purpose: the wording is
        /// identical for every row in a section, and 25 copies of it plus 25 buttons is what the
        /// Bloom page would otherwise be.</summary>
        private static void FxCloseSharedRun(List<HudProp> page, ref HudStyleFxDef runDef,
            ref bool runLocal)
        {
            if (runDef == null) { runLocal = false; return; }
            page.Add(HudProp.Note("    " + SharedGlobalWording));
            if (runLocal) page.Add(HudProp.Note("    [machine-local]: " + MachineLocalWording));
            string tab, subTabId, subTabName;
            if (HudStyleFx.F9Home(runDef, out tab, out subTabId, out subTabName))
                AddJump(page, tab, subTabId, subTabName, runDef.Key);
            runDef = null;
            runLocal = false;
        }

        /// <summary>ONE editable per-element row, built from its registry entry. The three
        /// FIRST-CLASS fields (the four authored radii behind one global, border width, the element
        /// font scale) live on <see cref="HudElementDef"/> rather than in the param bag and route
        /// through their own accessors; everything else is a slot-aware param read/write.</summary>
        private void AddFxEditableRow(List<HudProp> into, HudElementDef d, HudStyleFxDef def)
        {
            if (into == null || d == null || def == null) return;
            string label = FxLabel(def);
            if (!def.HasGlobal
                && label.IndexOf("per-element only", System.StringComparison.Ordinal) < 0)
                label = label + "  (per-element only)";

            switch (def.Key)
            {
                // ONE global row, FOUR authored corners. The global is what all four fall back to
                // through the -1 sentinel, so the popup necessarily shows more rows than F9 here.
                case "cornerRadius":
                    into.Add(WithId(HudProp.F("  Corner TL", () => d.RTLFor(EditBare(d)),
                        v => d.SetRTLFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f), "elRTL"));
                    into.Add(WithId(HudProp.F("  Corner TR", () => d.RTRFor(EditBare(d)),
                        v => d.SetRTRFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f), "elRTR"));
                    into.Add(WithId(HudProp.F("  Corner BR", () => d.RBRFor(EditBare(d)),
                        v => d.SetRBRFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f), "elRBR"));
                    into.Add(WithId(HudProp.F("  Corner BL", () => d.RBLFor(EditBare(d)),
                        v => d.SetRBLFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f), "elRBL"));
                    AddFxRowNotes(into, def);
                    return;
                case "borderWidth":
                    into.Add(WithId(HudProp.F(label, () => d.BorderWidthFor(EditBare(d)),
                        v => d.SetBorderWidthFor(EditBare(d), Mathf.Clamp(v, def.Min, def.Max)),
                        def.Min, def.Max), "elBorderWidth"));
                    AddFxRowNotes(into, def);
                    return;
                case "elFontScale":
                    into.Add(WithId(HudProp.F(label, () => d.FontScaleFor(EditBare(d)),
                        v => d.SetFontScaleFor(EditBare(d), Mathf.Clamp(v, def.Min, def.Max)),
                        def.Min, def.Max), "elFontScale"));
                    AddFxRowNotes(into, def);
                    return;
                case "cornerStyle":
                    into.Add(WithId(HudProp.Enum(label,
                        () => Mathf.Clamp(d.GetIFor(EditBare(d), "cornerStyle", CornerStyleFollow), 0, 2),
                        v => d.SetIFor(EditBare(d), "cornerStyle", Mathf.Clamp(v, 0, 2)),
                        CornerStyleNames), "cornerStyle"));
                    AddFxRowNotes(into, def);
                    return;
            }

            string key = def.ParamKey;
            if (string.IsNullOrEmpty(key)) return;
            switch (def.Kind)
            {
                case HudFxKind.Bool:
                {
                    bool gv = def.HasGlobal ? def.GlobalBool : FxCompanionDefault(key);
                    into.Add(WithId(HudProp.Bool(label, () => d.GetBFor(EditBare(d), key, gv),
                        v => d.SetBFor(EditBare(d), key, v)), def.Key));
                    break;
                }
                case HudFxKind.Color:
                    into.Add(WithId(HudProp.Color(label, () => d.GetSFor(EditBare(d), key, ""),
                        v => d.SetSFor(EditBare(d), key, string.IsNullOrEmpty(v) ? null : v),
                        () => HudPalette.PanelBorder.Value), def.Key));
                    break;
                case HudFxKind.Int:
                    into.Add(WithId(HudProp.I(label, () => d.GetIFor(EditBare(d), key, def.GlobalInt),
                        v => d.SetIFor(EditBare(d), key, Mathf.Clamp(v, (int)def.Min, (int)def.Max)),
                        (int)def.Min, (int)def.Max), def.Key));
                    break;
                default:
                {
                    float min = def.Min, max = FxMaxFor(def);
                    float gv = def.HasGlobal ? def.GlobalFloat : 0f;
                    into.Add(WithId(HudProp.F(label, () => d.GetFFor(EditBare(d), key, gv),
                        v => d.SetFFor(EditBare(d), key, Mathf.Clamp(v, min, max)), min, max), def.Key));
                    break;
                }
            }
            AddFxRowNotes(into, def);
        }

        /// <summary>The dimmed lines under one row: why it is inert (if it is), the legacy
        /// single-scalar caveat (if it applies), the table's own note lines, and the two answers
        /// the chroma row has always owed its author.</summary>
        private void AddFxRowNotes(List<HudProp> into, HudStyleFxDef def)
        {
            string inert = FxInertReason(def);
            if (inert != null) into.Add(HudProp.Note("    inactive: " + inert));
            if (def.LegacyApprox && !FxOnAnalyticPath)
                into.Add(HudProp.Note(
                    "    legacy surface: shine / iridescence / frost / chroma share ONE strength here,"));
            if (def.LegacyApprox && !FxOnAnalyticPath)
                into.Add(HudProp.Note("    so independent per-element values are approximate."));
            // `spec` used to carry THREE captions for one key (Phase 1 report, discrepancy 5) and one
            // of them sat right here, at the top of the edge family. The registry gives it a single
            // caption on the Theme page, so this is the pointer that replaces the duplicate row.
            if (string.Equals(def.Key, "edgeLightOn", System.StringComparison.Ordinal)
                && !FxIsLine && FxHasChrome)
                into.Add(HudProp.Note(
                    "    This element's edge-light STRENGTH is \"Default glass edge light\" (Theme page)."));
            if (string.Equals(def.Key, "chroma", System.StringComparison.Ordinal))
            {
                // The fringe IS the frosted backdrop sampled at an offset, so with no frost there is
                // nothing to offset. This is the second-order term behind FlorpyDorp's "the
                // chromatic aberration AND the frosted blur dissipate together".
                into.Add(HudProp.Note("    Needs frost > 0 on THIS element."));
                if (FrostAmountFor() <= 0.001f)
                    into.Add(HudProp.Note("    Inactive right now: this element's frost resolves to 0."));
            }
            if (def.Notes == null) return;
            for (int i = 0; i < def.Notes.Length; i++) into.Add(HudProp.Note(def.Notes[i]));
        }

        /// <summary>The element's STYLE AREA: a nested tab bar whose pages are F9's own sub-tabs,
        /// each rendered from <see cref="HudStyleFx"/> (see the block comment above AddStylePage).
        ///
        /// Everything that used to live here — the ~200-line Custom mirror of F9's Effects tab, the
        /// separate Polyline mirror, and the hand-written shared-globals block — is gone: those rows
        /// ARE the registry now, and a knob added to the table appears on both menus at once.
        ///
        /// PER-FRAME COST, stated rather than optimised away: the popup rebuilds its prop list every
        /// frame and this builds EVERY page, not just the visible one. That is deliberate, not an
        /// oversight — <see cref="SeedSlotFromBase"/> walks this same list to fork a per-tier style,
        /// and it must see every page or a Bare fork would only copy whichever tab happened to be
        /// open. A Custom Box builds roughly 190 props/frame against the ~150 the old mirror built,
        /// only while the popup is open, with no allocation beyond the props themselves. Measure
        /// before caching (plan §6's risk table says the same).</summary>
        private void AddUnifiedEffectProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            into.Add(HudProp.Header("Effects"));
            if (SupportsPanelAppearance && OptionalPanelBackgroundIsOff)
                into.Add(HudProp.Note(OptionalPanelBackgroundHint));

            var pages = new List<HudProp>();
            AddStylePage(pages, d, "Theme", HudFxCategory.Surface);
            // The effect families only earn a page on an element that renders something they can
            // touch. A borrowed vanilla widget or a bare label would otherwise get four pages of
            // shared globals it has no surface for.
            if (FxHasChrome || FxIsLine)
            {
                AddStylePage(pages, d, "Glass", HudFxCategory.Glass);
                AddStylePage(pages, d, "Edges", HudFxCategory.Edges);
                AddStylePage(pages, d, "Glow", HudFxCategory.Glow);
                AddStylePage(pages, d, "Bloom", HudFxCategory.Bloom);
                AddStylePage(pages, d, "Alerts", HudFxCategory.Alerts);
            }
            pages.Add(HudProp.TabPage("Transitions", BuildTransitionsPage(d)));
            into.Add(WithId(HudProp.TabGroup("elStyle", pages), "elStyle"));
            MarkProps(into, start, HudPropGroup.Effects);
        }

        /// <summary>The Transitions page — the existing HudTransitionFx-driven rows, moved verbatim
        /// off the bottom of the old Effects list and onto their own page.
        ///
        /// DELIBERATELY NOT gated on UsesCustomStyle (bug, 2026-07-19: the toggles were both
        /// invisible AND ignored for a global-styled element). "Follow the global THEME" and "follow
        /// the global MOTION" are different questions: an element must be able to keep the shared
        /// look while sitting a transition out. Retiring the tri-state in favour of a per-category
        /// follow checkbox is Phase 3, not this phase.</summary>
        private List<HudProp> BuildTransitionsPage(HudElementDef d)
        {
            var page = new List<HudProp>();
            page.Add(HudProp.Header("Motion & power transitions"));
            page.Add(HudProp.Header("  Inherit = follow the F9 global (Effects -> Suit power)"));
            page.Add(HudProp.Header("  On = force it, at this element's own strength.  Off = never."));
            // One row per registry effect, driven by HudTransitionFx.All, so this inspector and the
            // F9 global menu cannot present different effects or different labels.
            for (int i = 0; i < HudTransitionFx.All.Length; i++)
                AddTransitionRows(page, d, HudTransitionFx.All[i]);
            page.Add(HudProp.Header("  A global master that is OFF wins over every element."));
            page.Add(HudProp.Header("  Transitions are shared by the suit and bare layouts."));
            AddJump(page, HudStyleFx.TabEffects, HudStyleFx.SubTabTransitions, "Transitions",
                "transitions");
            return page;
        }

        /// <summary>Combo captions for the per-effect tri-state. A readonly array of literals: no
        /// scene, document or ConfigEntry reference, so it needs no hot-reload teardown (the whole
        /// type goes with the assembly on F6). Index order MUST match <see cref="HudFxMode"/>.</summary>
        private static readonly string[] TransitionModeNames = { "Inherit", "On", "Off" };

        /// <summary>Per-element rows for ONE registry transition effect, bound to the TRI-STATE so
        /// the inspector can never disagree with the resolver: an explicit Inherit / On / Off
        /// picker, plus the element's OWN strength while it is forced On (an inheriting element is
        /// told what the global would give it instead, so the row is never a silent blank).
        ///
        /// EVERY accessor is called with bare:false — i.e. the RAW base value, never the "b_" bare
        /// override. Power transitions are one animation for the whole element and are deliberately
        /// NOT forked per tier: forking here would let the suit and bare layouts disagree about
        /// whether an element dies at all, and the renderer's bare read falls back to this same base
        /// whenever no override exists. (The migration may still have written a legacy bare
        /// override; the resolver honours it, we simply never author a new one.)</summary>
        private void AddTransitionRows(List<HudProp> into, HudElementDef d, HudTransitionFxDef fx)
        {
            if (into == null || d == null || fx == null) return;
            // StableId, not the label, keys the ImGui control: seven rows share the "  strength"
            // caption and would otherwise collapse into one (HudPropDrawer derives the id from
            // Group + Label).
            into.Add(WithId(HudProp.Enum(fx.Label,
                () => (int)HudTransitionFx.ModeOf(d, fx, false),
                v => HudTransitionFx.SetMode(d, fx, false, ToMode(v)),
                TransitionModeNames), fx.ModeKey));

            HudFxMode mode = HudTransitionFx.ModeOf(d, fx, false);
            if (mode == HudFxMode.On)
                into.Add(WithId(HudProp.F("  strength", () => HudTransitionFx.AmountOf(d, fx, false),
                    v => HudTransitionFx.SetAmount(d, fx, false, v), 0f, 2f), fx.AmtKey));
            else if (mode == HudFxMode.Inherit)
                into.Add(HudProp.Header(fx.GlobalOn
                    ? "  inheriting strength " + fx.GlobalAmt.ToString("0.00")
                    : "  inheriting OFF (this effect's global master is off)"));
            if (mode == HudFxMode.On && !fx.GlobalOn)
                into.Add(HudProp.Header("  Global master is OFF - forcing On here changes nothing"));
        }

        /// <summary>Combo index -> tri-state, clamped so a stray index can never author garbage.</summary>
        private static HudFxMode ToMode(int v)
        {
            if (v == (int)HudFxMode.On) return HudFxMode.On;
            if (v == (int)HudFxMode.Off) return HudFxMode.Off;
            return HudFxMode.Inherit;
        }

        private static void MarkProps(List<HudProp> props, int start, HudPropGroup group)
        {
            for (int i = Mathf.Max(0, start); i < props.Count; i++)
                if (props[i] != null) props[i].Group = group;
        }

        /// <summary>Give a prop an explicit ImGui identity. REQUIRED whenever two props in the same
        /// group share a label: HudPropDrawer derives the id from Group + Label, so duplicates
        /// become one control and edits land on the wrong row. The global tab has the same problem
        /// and solves it with "##" label suffixes; props carry a StableId instead so the visible
        /// label stays exactly what the global tab shows.</summary>
        private static HudProp WithId(HudProp p, string stableId)
        {
            if (p != null) p.StableId = stableId;
            return p;
        }

        // ---- per-tier fork seeding ----------------------------------------------------------

        /// <summary>Rows <see cref="SeedSlotFromBase"/> must NOT round-trip: setters with side
        /// effects or identity semantics. The geometry rows fork the LAYOUT (a separate feature
        /// with its own toggle); "tiers" would call ClearVisualBareOverrides and wipe the very fork
        /// being seeded; "zOrder" is document order, not look; the transition rows are deliberately
        /// shared by every tier (see AddTransitionRows) and re-writing them would churn the base.
        /// Everything else is safe: a row that is not actually tier-aware simply writes the same
        /// value back to the same base storage — a no-op round trip.</summary>
        private static readonly HashSet<string> SeedSkip = BuildSeedSkip();

        private static HashSet<string> BuildSeedSkip()
        {
            var s = new HashSet<string>(System.StringComparer.Ordinal)
            {
                "elAnchor", "elX", "elY", "elW", "elH", "elWPct", "elHPct", "zOrder", "tiers",
            };
            for (int i = 0; i < HudTransitionFx.All.Length; i++)
            {
                var fx = HudTransitionFx.All[i];
                if (fx == null) continue;
                s.Add(fx.ModeKey);
                s.Add(fx.AmtKey);
            }
            return s;
        }

        /// <summary>The identity a seeded value is matched on across the two prop-list builds.</summary>
        private static string SeedKeyOf(HudProp p)
            => !string.IsNullOrEmpty(p.StableId) ? p.StableId : ((int)p.Group) + "|" + p.Label;

        private static void CollectSeedValues(List<HudProp> props, Dictionary<string, object> into)
        {
            if (props == null) return;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (p.Kind == HudPropKind.TabGroup || p.Kind == HudPropKind.TabPage)
                {
                    CollectSeedValues(p.Children, into);
                    continue;
                }
                if (p.Kind == HudPropKind.Header || p.Kind == HudPropKind.Points
                    || p.Kind == HudPropKind.TierMask || p.Kind == HudPropKind.Note
                    || p.Kind == HudPropKind.Button) continue;
                if (p.Get == null || p.Set == null) continue;
                string key = SeedKeyOf(p);
                if (SeedSkip.Contains(key) || into.ContainsKey(key)) continue;
                try { into[key] = p.Get(); } catch { }
            }
        }

        private static void ApplySeedValues(List<HudProp> props, Dictionary<string, object> from)
        {
            if (props == null) return;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (p.Kind == HudPropKind.TabGroup || p.Kind == HudPropKind.TabPage)
                {
                    ApplySeedValues(p.Children, from);
                    continue;
                }
                if (p.Kind == HudPropKind.Header || p.Kind == HudPropKind.Points
                    || p.Kind == HudPropKind.TierMask || p.Kind == HudPropKind.Note
                    || p.Kind == HudPropKind.Button) continue;
                if (p.Set == null) continue;
                string key = SeedKeyOf(p);
                object v;
                if (!from.TryGetValue(key, out v)) continue;
                try { p.Set(v); } catch { }
            }
        }

        /// <summary>Complete a slot fork: give <paramref name="slot"/> its own stored copy of every
        /// forkable value it is CURRENTLY resolving, so later base edits can never leak into it.
        ///
        /// VALUE-PRESERVING BY CONSTRUCTION, and that is why both reads and writes run with the
        /// target slot selected rather than reading "the base": an element that is not forked yet
        /// resolves the base anyway (<c>ResolveSlot</c>), while an element whose legacy fork
        /// Sanitize just ADOPTED resolves its own overrides where it has them and the base
        /// everywhere else. Reading the base outright would have overwritten exactly the handful of
        /// colours a pre-0.9.2.5 author had successfully forked.
        ///
        /// Values are copied as STORED (palette-name refs, -1 "follow the global" sentinels) —
        /// never as resolved colours or globals — because the prop getters ARE the slot accessors.
        /// That is what keeps the fork tracking the palette/theme exactly as the base does, instead
        /// of freezing it (the 2026-07-17 "Custom snapshots drift" defect).
        ///
        /// No hand-maintained key manifest: the widget's own <see cref="DescribeProps"/> IS the
        /// manifest, so a widget that gains a knob gains a forkable knob for free.</summary>
        internal static void SeedSlotFromBase(HudElementView view, HudStyleSlot slot)
        {
            if (view == null || view.Def == null || slot == HudStyleSlot.Base) return;
            var d = view.Def;
            var prevEdit = EditTargetSlot;
            var prevLayout = LayoutSlot;
            var scratch = new List<HudProp>();
            var values = new Dictionary<string, object>(System.StringComparer.Ordinal);
            try
            {
                // 1. Read what the slot resolves TODAY (base for a fresh fork, own-or-base for an
                //    adopted legacy one).
                EditTargetSlot = slot;
                LayoutSlot = slot;
                view.DescribeProps(scratch);
                CollectSeedValues(scratch, values);

                // 2. Open the fork, then write every recorded value into it. The list must be
                //    REBUILT: the setters resolve EditSlot at call time, and which rows are even
                //    visible depends on the slot's own styleSource, which this step establishes.
                d.SetForkSlot(slot, true);
                scratch.Clear();
                view.DescribeProps(scratch);
                ApplySeedValues(scratch, values);
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("HudElementView: seeding the per-tier style of '"
                    + (d.Id ?? "?") + "' failed (" + e.Message + ") — the fork inherits the base.");
            }
            finally
            {
                EditTargetSlot = prevEdit;
                LayoutSlot = prevLayout;
                d.Set(HudElementDef.TierStyleSeedKey, null);
            }
        }

        /// <summary>Freeze the element's currently rendered theme/effect values into a complete
        /// custom snapshot before changing its source mode. The reads happen while the OLD mode
        /// is still active, so Global -> Custom, Custom -> Custom and Legacy -> Custom are all
        /// visually continuous.
        ///
        /// Since Phase 0a this runs on EVERY Global -> Custom flip, not just the first one, so it
        /// must stay VALUE-PRESERVING BY CONSTRUCTION: every write below is a `...For()` resolver
        /// read, which means re-seeding an element that is already showing these values simply
        /// writes them down again. Keep it that way — a literal default sneaked in here becomes a
        /// visible jump on every separation.
        ///
        /// Re-following (Custom -> Global) does NOT delete the stored keys (FlorpyDorp's decision
        /// 5): they stay dormant and are overwritten by the next seed.</summary>
        private void SeedCustomStyleFromEffective(HudElementDef d, HudStyleSlot slot)
        {
            if (d == null) return;

            // The snapshot defines the TARGET SLOT's style, so force the slot-aware resolvers below
            // onto that slot — a snapshot taken while previewing BARE must land in the bare slot,
            // never in the shared base every tier inherits. Restored in finally so a mid-frame flip
            // can't strand the render slot.
            var _prevLayoutSlot = LayoutSlot;
            LayoutSlot = slot;
            try
            {

            // Colours are deliberately NOT snapshotted: refs resolve identically in both states
            // (see GlobalOr), so separation must not hex-freeze a palette-name ref that the
            // author wants tracking the F9 palette.
            d.SetBorderWidthFor(slot, BorderWidthFor());
            d.SetRTLFor(slot, Radius(d.RTLFor(slot)));
            d.SetRTRFor(slot, Radius(d.RTRFor(slot)));
            d.SetRBRFor(slot, Radius(d.RBRFor(slot)));
            d.SetRBLFor(slot, Radius(d.RBLFor(slot)));
            d.SetFFor(slot, "feather", EffectiveFeatherFor());
            d.SetFFor(slot, "sheen", GlassSheenFor());

            // Custom stores the final visible edge-light strength. This includes the current
            // global Tier-A boost when Global/Legacy was the source, then stops tracking it.
            d.SetFFor(slot, "spec", GlassEdgeFor());

            d.SetFFor(slot, "squircle", SdfSquircleFor());
            d.SetBFor(slot, "gaussianHalo", SdfGaussianFor());
            // CORNER STYLE (added to the seed 2026-07-26). Deliberately copied in its STORED
            // encoding, not resolved: 0 = "Follow global" is this key's own live-tracking sentinel,
            // the exact analogue of a palette-name ColorRef, and CornerCutFor already maps it to
            // the -1 the panel wants in BOTH states. Freezing it to the global's concrete value
            // here would silently stop a separated element tracking the F9 corner-style combo.
            d.SetIFor(slot, "cornerStyle",
                Mathf.Clamp(d.GetIFor(slot, "cornerStyle", CornerStyleFollow), 0, 2));
            d.SetBFor(slot, "customBorderFadeOn", StyleFeatureOn("customBorderFadeOn", HudConfig.FxBorderFadeOn));
            d.SetBFor(slot, "customSoftEdgeOn", StyleFeatureOn("customSoftEdgeOn", HudConfig.FxSoftEdgeOn));
            d.SetBFor(slot, "customGlowOn", StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn));
            d.SetBFor(slot, "customRippleOn", StyleFeatureOn("customRippleOn", HudConfig.FxEdgeLightOn));
            d.SetBFor(slot, "customGlowBreathOn", NewSdfFeatureOn("customGlowBreathOn", HudConfig.FxGlowBreathOn));
            d.SetBFor(slot, "customGlowUnevenOn", NewSdfFeatureOn("customGlowUnevenOn", HudConfig.FxGlowUnevenOn));
            d.SetBFor(slot, "customGlowFlowOn", NewSdfFeatureOn("customGlowFlowOn", HudConfig.FxGlowFlowAuraOn));
            d.SetFFor(slot, "bfade", OwnOrGlobal("bfade", HudConfig.FxBorderFade));
            d.SetFFor(slot, "softEdge", OwnOrGlobal("softEdge", HudConfig.FxSoftEdge));
            d.SetFFor(slot, "glow", OwnOrGlobal("glow", HudConfig.FxGlow));
            d.SetFFor(slot, "glowIn", OwnOrGlobal("glowIn", HudConfig.FxGlowInner));
            d.SetFFor(slot, "glowWidth", OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth));
            d.SetFFor(slot, "glowDiffuse", OwnOrGlobal("glowDiffuse", HudConfig.FxGlowDiffuse));
            d.SetFFor(slot, "glowExtraDiffuse", NewSdfOwnOrGlobal("glowExtraDiffuse", HudConfig.FxGlowExtraDiffuse));
            d.SetFFor(slot, "glowHaze", NewSdfOwnOrGlobal("glowHaze", HudConfig.FxGlowHaze));
            d.SetFFor(slot, "glowBreath", NewSdfOwnOrGlobal("glowBreath", HudConfig.FxGlowBreath));
            d.SetFFor(slot, "glowUneven", NewSdfOwnOrGlobal("glowUneven", HudConfig.FxGlowUneven));
            d.SetFFor(slot, "glowOrganicScale", NewSdfOwnOrGlobal("glowOrganicScale", HudConfig.FxGlowOrganicScale));
            d.SetFFor(slot, "glowFlowAura", NewSdfOwnOrGlobal("glowFlowAura", HudConfig.FxGlowFlowAura));
            d.SetFFor(slot, "ripple", OwnOrGlobal("ripple", HudConfig.FxEdgeRipple));
            d.SetFFor(slot, "rippleFreq", OwnOrGlobal("rippleFreq", HudConfig.FxEdgeRippleFreq));
            d.SetFFor(slot, "rippleSmooth", UsesGlobalStyle ? 0f : d.GetFFor(slot, "rippleSmooth", 0f));
            d.SetFFor(slot, "edgeFlow", EdgeFlowFor());
            d.SetFFor(slot, "frostDepth", FrostDepthFor());

            // BOX END FADE (added to the seed 2026-07-26). These two are element GEOMETRY, editable
            // in both states and un-gated by the style source, so writing them back is a value-
            // preserving no-op — but it COMPLETES the slot's copy, which is what stops a later base
            // edit leaking into a per-tier fork (HudElementDef's copy-on-write only protects keys
            // that exist).
            d.SetFFor(slot, "edgeFadeX", d.GetFFor(slot, "edgeFadeX", 0f));
            d.SetFFor(slot, "edgeFadeY", d.GetFFor(slot, "edgeFadeY", 0f));

            // THE PORTRAIT-STYLE RING (added to the seed 2026-07-26). Only border-only chrome reads
            // these, so a panel element is left alone rather than gaining two dead keys.
            if (SupportsBorderOnlyChrome)
            {
                // Custom's ringGlow defaults to 0 = no halo band, but a FOLLOWING ring derives its
                // halo from the panel recipe — so separation used to delete the ring's halo. Freeze
                // what it is actually showing.
                d.SetFFor(slot, "ringGlow", Mathf.Clamp(RingGlowFor(), 0f, 2f));
                // The COLOUR is copied as a REF, never as a resolved hex: an empty ref means "derive
                // from the rim" and a palette name must keep live-tracking the F9 palette — the same
                // deliberate non-snapshot rule the Fill/Border/Text refs follow above.
                string ringRef = d.GetSFor(slot, "ringGlowColor", null);
                d.SetSFor(slot, "ringGlowColor", string.IsNullOrEmpty(ringRef) ? null : ringRef);
            }

            bool globalShineOn = HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            float globalShine = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            bool globalIridOn = HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            float globalIrid = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            bool globalChromaOn = HudConfig.FxChromaOn != null && HudConfig.FxChromaOn.Value;
            float globalChroma = HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f;
            float globalFrost = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f;
            bool fromCustom = UsesCustomStyle;

            d.SetBFor(slot, "customShineOn", fromCustom ? d.GetBFor(slot, "customShineOn", globalShineOn) : globalShineOn);
            d.SetFFor(slot, "customShine", Mathf.Clamp(fromCustom
                ? d.GetFFor(slot, "customShine", globalShine) : globalShine, 0f, 2f));
            d.SetBFor(slot, "customIridOn", fromCustom ? d.GetBFor(slot, "customIridOn", globalIridOn) : globalIridOn);
            d.SetFFor(slot, "customIrid", Mathf.Clamp01(fromCustom
                ? d.GetFFor(slot, "customIrid", globalIrid) : globalIrid));
            d.SetBFor(slot, "customChromaOn", fromCustom ? d.GetBFor(slot, "customChromaOn", globalChromaOn) : globalChromaOn);
            d.SetFFor(slot, "customChroma", Mathf.Clamp01(fromCustom
                ? d.GetFFor(slot, "customChroma", globalChroma) : globalChroma));
            d.SetBFor(slot, "customFrostOn", fromCustom ? d.GetBFor(slot, "customFrostOn", true) : true);
            d.SetFFor(slot, "customFrost", Mathf.Clamp01(fromCustom
                ? d.GetFFor(slot, "customFrost", globalFrost) : globalFrost));
            // MOTION IS NOT SNAPSHOTTED. It used to be: this block wrote customDissolve plus a
            // hard 1f into every fx*Amt whenever an element was seeded into Custom. Two bugs came
            // out of that — (a) it destroyed an authored per-effect strength ("TV off = 1.8" became
            // 1.0) purely because the user flipped an UNRELATED appearance setting, and (b) writing
            // customDissolve=false when the global master happened to be off was later read back by
            // the tri-state's legacy fallback as an explicit "Off", permanently pinning the element
            // and re-creating the inherit-vs-explicit conflation this refactor exists to remove.
            // Transitions are deliberately independent of the style source now (see
            // AddUnifiedEffectProps) and default to Inherit, so seeding must leave them alone.

            // The line's edge-light strength key (panels collapse theirs into `spec`).
            if (d.Type == HudElementType.Polyline)
                d.SetFFor(slot, "edgeLight", OwnOrGlobal("edgeLight", HudConfig.FxEdgeLight));

            d.Set("followGlobal", null); // extinct legacy flag — never re-written
            d.SetBFor(slot, "customStyleReady", true);
            }
            finally { LayoutSlot = _prevLayoutSlot; }
        }

        /// <summary>One-click bulk style-source operation used by F9. Customisation freezes each
        /// element's own currently effective look through the same path as the inspector, rather
        /// than merely flipping a flag and revealing stale profile values.
        ///
        /// Phase 0a: the "customStyleReady" short-circuit is GONE here too, so the bulk button and
        /// the per-element checkbox cannot disagree — Global -> Custom always re-seeds. Seeding is
        /// value-preserving by construction (every write is a resolver read taken while the OLD
        /// mode is still active), so re-seeding an element that is already Custom simply writes
        /// its own values back.
        ///
        /// <paramref name="forceCustomSnapshot"/> is now redundant for this direction and is kept
        /// only so the call shape stays stable for
        /// <see cref="SetUnifiedStyleSourceWithoutView"/>'s LEGACY path, which HudStyleMigration
        /// depends on.</summary>
        internal void SetUnifiedStyleSource(bool followGlobal, bool forceCustomSnapshot = false)
        {
            if (Def == null) return;
            // Targets the slot the editor is previewing, so "Make flat" on the BARE tab flattens
            // bare and leaves the suited design alone (and, with per-tier off, still writes base).
            var slot = EditSlot(Def);
            if (!followGlobal) SeedCustomStyleFromEffective(Def, slot);
            Def.SetIFor(slot, "styleSource", followGlobal ? StyleGlobal : StyleCustom);
        }

        /// <summary>Document-level counterpart for elements whose view is unavailable (different
        /// tier, failed build, or HUD canvas not present). It resolves the same stored/global
        /// contract without touching Unity objects, so bulk F9 actions never skip definitions.
        ///
        /// MIGRATION CONTRACT: this reads the RAW stored styleSource — a stored 0 keeps its full
        /// legacy semantics here (followGlobal colour/glass gate, -1 sentinels, fx* multipliers
        /// folded into the custom snapshot) — because HudStyleMigration relies on this function
        /// to faithfully freeze a legacy element's CURRENT render. Everywhere else legacy is
        /// extinct: StyleSourceOf maps 0 to Global for the live resolvers.</summary>
        internal static void SetUnifiedStyleSourceWithoutView(HudElementDef d, bool followGlobal,
            bool forceCustomSnapshot = false)
        {
            if (d == null) return;
            if (followGlobal)
            {
                d.SetI("styleSource", StyleGlobal);
                return;
            }

            int source = Mathf.Clamp(d.GetI("styleSource", StyleLegacy), StyleLegacy, StyleCustom);
            if (!forceCustomSnapshot && source != StyleCustom && d.GetB("customStyleReady", false))
            {
                d.SetI("styleSource", StyleCustom);
                return;
            }
            if (!forceCustomSnapshot && source == StyleCustom) return;

            bool sourceGlobal = source == StyleGlobal;
            bool sourceCustom = source == StyleCustom;
            // Colour refs are deliberately left untouched: they resolve identically in both
            // coherent states (palette names live-track, hex stands alone). Legacy's forced
            // palette is handled by HudStyleMigration, which rewrites the refs to palette NAMES
            // so the element keeps tracking the F9 palette it was showing.
            bool followsColours = sourceGlobal || (!sourceCustom && d.GetB("followGlobal", false));

            float borderGlobal = HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f;
            d.BorderWidth = sourceGlobal || d.BorderWidth < 0f ? borderGlobal : d.BorderWidth;
            float radiusGlobal = HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f;
            d.RTL = sourceGlobal || d.RTL < 0f ? radiusGlobal : d.RTL;
            d.RTR = sourceGlobal || d.RTR < 0f ? radiusGlobal : d.RTR;
            d.RBR = sourceGlobal || d.RBR < 0f ? radiusGlobal : d.RBR;
            d.RBL = sourceGlobal || d.RBL < 0f ? radiusGlobal : d.RBL;
            d.SetF("feather", SnapshotFloat(d, source, "feather",
                HudConfig.EdgeFeather != null ? HudConfig.EdgeFeather.Value : 1.25f));

            float sheenGlobal = HudConfig.GlassSheen != null ? HudConfig.GlassSheen.Value : 0f;
            float edgeGlobal = HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f;
            float ownSheen = d.GetF("sheen", -1f);
            float ownEdge = d.GetF("spec", -1f);
            d.SetF("sheen", sourceGlobal || followsColours || ownSheen < 0f ? sheenGlobal : ownSheen);
            float resolvedEdge = sourceGlobal || followsColours || ownEdge < 0f
                ? edgeGlobal : ownEdge;
            bool optedOut = !followsColours && ownEdge == 0f;
            if (!sourceCustom && !optedOut
                && HudConfig.FxTierA != null && HudConfig.FxTierA.Value
                && HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value
                && HudConfig.FxEdgeLight != null && HudConfig.FxEdgeLight.Value > 0f)
                resolvedEdge = Mathf.Clamp01(resolvedEdge + HudConfig.FxEdgeLight.Value * 0.45f);
            d.SetF("spec", Mathf.Clamp01(resolvedEdge));

            float squircleGlobal = HudConfig.SdfSquircle != null ? HudConfig.SdfSquircle.Value : 2f;
            float ownSquircle = d.GetF("squircle", -1f);
            d.SetF("squircle", sourceGlobal || ownSquircle < 2f ? squircleGlobal
                : Mathf.Clamp(ownSquircle, 2f, 8f));
            bool gaussianGlobal = HudConfig.SdfGaussianHalo != null && HudConfig.SdfGaussianHalo.Value;
            d.SetB("gaussianHalo", sourceCustom ? d.GetB("gaussianHalo", gaussianGlobal) : gaussianGlobal);

            bool borderFadeGlobal = HudConfig.FxBorderFadeOn != null && HudConfig.FxBorderFadeOn.Value;
            bool softEdgeGlobal = HudConfig.FxSoftEdgeOn != null && HudConfig.FxSoftEdgeOn.Value;
            bool glowGlobal = HudConfig.FxGlowOn != null && HudConfig.FxGlowOn.Value;
            bool rippleGlobal = HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value;
            bool glowBreathGlobal = HudConfig.FxGlowBreathOn != null && HudConfig.FxGlowBreathOn.Value;
            bool glowUnevenGlobal = HudConfig.FxGlowUnevenOn != null && HudConfig.FxGlowUnevenOn.Value;
            bool glowFlowGlobal = HudConfig.FxGlowFlowAuraOn != null && HudConfig.FxGlowFlowAuraOn.Value;
            d.SetB("customBorderFadeOn", sourceCustom
                ? d.GetB("customBorderFadeOn", borderFadeGlobal) : borderFadeGlobal);
            d.SetB("customSoftEdgeOn", sourceCustom
                ? d.GetB("customSoftEdgeOn", softEdgeGlobal) : softEdgeGlobal);
            d.SetB("customGlowOn", sourceCustom ? d.GetB("customGlowOn", glowGlobal) : glowGlobal);
            d.SetB("customRippleOn", sourceCustom ? d.GetB("customRippleOn", rippleGlobal) : rippleGlobal);
            // ONE MISSING-KEY CONVENTION (Phase 0b): these three used to snapshot a literal `false`
            // for an already-Custom element, so the def-only path silently switched the SDF halo
            // family OFF for every element whose stored snapshot predated the keys. The stored
            // value still wins when present — only the ABSENT case changed, and it now reads the
            // current global, matching NewSdfFeatureOn and the three lines above.
            d.SetB("customGlowBreathOn", sourceCustom
                ? d.GetB("customGlowBreathOn", glowBreathGlobal) : glowBreathGlobal);
            d.SetB("customGlowUnevenOn", sourceCustom
                ? d.GetB("customGlowUnevenOn", glowUnevenGlobal) : glowUnevenGlobal);
            d.SetB("customGlowFlowOn", sourceCustom
                ? d.GetB("customGlowFlowOn", glowFlowGlobal) : glowFlowGlobal);
            d.SetF("bfade", SnapshotFloat(d, source, "bfade",
                HudConfig.FxBorderFade != null ? HudConfig.FxBorderFade.Value : 0f));
            d.SetF("softEdge", SnapshotFloat(d, source, "softEdge",
                HudConfig.FxSoftEdge != null ? HudConfig.FxSoftEdge.Value : 0f));
            d.SetF("glow", SnapshotFloat(d, source, "glow",
                HudConfig.FxGlow != null ? HudConfig.FxGlow.Value : 0f));
            d.SetF("glowIn", SnapshotFloat(d, source, "glowIn",
                HudConfig.FxGlowInner != null ? HudConfig.FxGlowInner.Value : 0f));
            d.SetF("glowWidth", SnapshotFloat(d, source, "glowWidth",
                HudConfig.FxGlowWidth != null ? HudConfig.FxGlowWidth.Value : 24f));
            d.SetF("glowDiffuse", SnapshotFloat(d, source, "glowDiffuse",
                HudConfig.FxGlowDiffuse != null ? HudConfig.FxGlowDiffuse.Value : 0f));
            d.SetF("glowExtraDiffuse", SnapshotNewFloat(d, source, "glowExtraDiffuse",
                HudConfig.FxGlowExtraDiffuse != null ? HudConfig.FxGlowExtraDiffuse.Value : 0f));
            d.SetF("glowHaze", SnapshotNewFloat(d, source, "glowHaze",
                HudConfig.FxGlowHaze != null ? HudConfig.FxGlowHaze.Value : 0f));
            d.SetF("glowBreath", SnapshotNewFloat(d, source, "glowBreath",
                HudConfig.FxGlowBreath != null ? HudConfig.FxGlowBreath.Value : 0f));
            d.SetF("glowUneven", SnapshotNewFloat(d, source, "glowUneven",
                HudConfig.FxGlowUneven != null ? HudConfig.FxGlowUneven.Value : 0f));
            d.SetF("glowOrganicScale", SnapshotNewFloat(d, source, "glowOrganicScale",
                HudConfig.FxGlowOrganicScale != null ? HudConfig.FxGlowOrganicScale.Value : 1f));
            d.SetF("glowFlowAura", SnapshotNewFloat(d, source, "glowFlowAura",
                HudConfig.FxGlowFlowAura != null ? HudConfig.FxGlowFlowAura.Value : 0f));
            d.SetF("ripple", SnapshotFloat(d, source, "ripple",
                HudConfig.FxEdgeRipple != null ? HudConfig.FxEdgeRipple.Value : 0f));
            d.SetF("rippleFreq", SnapshotFloat(d, source, "rippleFreq",
                HudConfig.FxEdgeRippleFreq != null ? HudConfig.FxEdgeRippleFreq.Value : 2f));
            d.SetF("rippleSmooth", sourceGlobal ? 0f : d.GetF("rippleSmooth", 0f));
            d.SetF("edgeFlow", SnapshotFloat(d, source, "edgeFlow",
                HudConfig.FxEdgeFlowSpeed != null ? HudConfig.FxEdgeFlowSpeed.Value : 0.22f));
            d.SetF("frostDepth", SnapshotFloat(d, source, "frostDepth",
                HudConfig.FrostDepth != null ? HudConfig.FrostDepth.Value : 1f));

            bool shineOn = HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            bool iridOn = HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            bool chromaOn = HudConfig.FxChromaOn != null && HudConfig.FxChromaOn.Value;
            float shine = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            float irid = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            float chroma = HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f;
            float frost = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f;
            bool dissolve = HudConfig.FxDissolveBoot != null && HudConfig.FxDissolveBoot.Value;
            d.SetB("customShineOn", sourceCustom ? d.GetB("customShineOn", shineOn)
                : shineOn && (sourceGlobal || d.GetB("fxShine", true)));
            d.SetF("customShine", sourceCustom ? Mathf.Clamp(d.GetF("customShine", shine), 0f, 2f)
                : Mathf.Clamp(shine * (sourceGlobal ? 1f : Mathf.Clamp01(d.GetF("fxShineAmt", 1f))), 0f, 2f));
            d.SetB("customIridOn", sourceCustom ? d.GetB("customIridOn", iridOn)
                : iridOn && (sourceGlobal || d.GetB("fxIrid", true)));
            d.SetF("customIrid", sourceCustom ? Mathf.Clamp01(d.GetF("customIrid", irid))
                : Mathf.Clamp01(irid * (sourceGlobal ? 1f : Mathf.Clamp01(d.GetF("fxIridAmt", 1f)))));
            d.SetB("customChromaOn", sourceCustom ? d.GetB("customChromaOn", chromaOn)
                : chromaOn && (sourceGlobal || d.GetB("fxChroma", true)));
            d.SetF("customChroma", sourceCustom ? Mathf.Clamp01(d.GetF("customChroma", chroma))
                : Mathf.Clamp01(chroma * (sourceGlobal ? 1f : Mathf.Clamp01(d.GetF("fxChromaAmt", 1f)))));
            d.SetB("customFrostOn", sourceCustom ? d.GetB("customFrostOn", true)
                : sourceGlobal || d.GetB("fxFrost", true));
            d.SetF("customFrost", sourceCustom ? Mathf.Clamp01(d.GetF("customFrost", frost))
                : Mathf.Clamp01(frost * (sourceGlobal ? 1f : Mathf.Clamp01(d.GetF("fxFrostAmt", 1f)))));
            // Motion deliberately NOT snapshotted here either — same reasoning as
            // SeedCustomStyleFromEffective: a style-source flip must not rewrite an element's
            // transitions or flatten an authored per-effect strength back to 1.

            // The line's edge-light strength key (panels collapse theirs into `spec`).
            if (d.Type == HudElementType.Polyline)
                d.SetF("edgeLight", SnapshotFloat(d, source, "edgeLight",
                    HudConfig.FxEdgeLight != null ? HudConfig.FxEdgeLight.Value : 0f));

            d.Set("followGlobal", null); // extinct legacy flag — never re-written
            d.SetB("customStyleReady", true);
            d.SetI("styleSource", StyleCustom);
        }

        private static float SnapshotFloat(HudElementDef d, int source, string key, float global)
        {
            if (source == StyleGlobal) return global;
            float own = d.GetF(key, -1f);
            return own >= 0f ? own : global;
        }

        /// <summary>The SDF-family float snapshot. Phase 0b folded its old "neutral" default into
        /// the GLOBAL, so an absent key means the same thing here as it does in the resolvers and
        /// in <see cref="SnapshotFloat"/>. It still differs from SnapshotFloat in one way, and
        /// deliberately: a Custom element's STORED value wins even when it is negative, because
        /// these keys never used -1 as a "follow" sentinel.</summary>
        private static float SnapshotNewFloat(HudElementDef d, int source, string key, float global)
        {
            if (source == StyleGlobal) return global;
            if (source == StyleCustom) return d.GetF(key, global);
            float own = d.GetF(key, -1f);
            return own >= 0f ? own : global;
        }

        /// <summary>Resolve a power-transition effect for this element through the shared
        /// <see cref="HudTransitionFx"/> registry: global master OFF -> 0, element Off -> 0,
        /// element On -> the element's own strength, element Inherit -> the global strength.
        ///
        /// THE BUG THIS FIXES (FlorpyDorp, 2026-07-19: "I turned off death collapse and it still
        /// does it"): the per-element flag was a plain bool defaulting TRUE, which conflated
        /// "inherit the global" with "explicitly on" — "off" could not be expressed, and for a
        /// global-styled element the stored value was never read at all. The state is now an
        /// explicit tri-state int at "&lt;key&gt;Mode", honoured in BOTH style states.</summary>
        internal float TransitionAmt(string key)
        {
            return HudTransitionFx.Resolve(Def, key, LayoutBare);
        }

        /// <summary>This element's tri-state for one effect (registry key).</summary>
        internal HudFxMode TransitionMode(string key)
        {
            return HudTransitionFx.ModeOf(Def, key, LayoutBare);
        }

        /// <summary>Legacy call shape kept for HudSystem / HudGlitch, which pass both keys.
        /// Registry keys resolve through the tri-state; an unknown key falls back to the old
        /// "on unless the element opted out" contract so nothing silently stops animating.</summary>
        internal float EffectAmt(string key, string amtKey)
        {
            var fx = HudTransitionFx.Find(key);
            if (fx != null) return HudTransitionFx.Resolve(Def, fx, LayoutBare);
            if (Def == null) return 0f;
            if (!Def.GetBFor(LayoutBare, key, true)) return 0f;
            return Mathf.Clamp(Def.GetFFor(LayoutBare, amtKey, 1f), 0f, 2f);
        }

        /// <summary>Push this element's fxWarp strength onto its child warp components. Called on
        /// (re)layout — the surrounding DirtyAllMeshes re-meshes so the new bend takes effect.</summary>
        internal void ApplyWarpMult()
        {
            if (Root == null) return;
            float mult = TransitionAmt("fxWarp");
            var vw = Root.GetComponentsInChildren<VisorWarp>(true);
            for (int i = 0; i < vw.Length; i++) vw[i].StrengthMult = mult;
            var tw = Root.GetComponentsInChildren<TmpWarp>(true);
            for (int i = 0; i < tw.Length; i++) tw[i].StrengthMult = mult;
        }

    }
}
