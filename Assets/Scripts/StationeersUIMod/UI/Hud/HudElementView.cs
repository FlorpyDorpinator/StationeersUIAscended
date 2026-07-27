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
            // selected on a Both element). Bare inherits the base until a property is forked. These
            // shorthands keep the bag rows terse; first-class fields use their own ...For accessors.
            System.Func<string, float, float> gf = (k, dv) => d.GetFFor(EditBare(d), k, dv);
            System.Action<string, float> sf = (k, v) => d.SetFFor(EditBare(d), k, v);
            System.Func<string, bool, bool> gb = (k, dv) => d.GetBFor(EditBare(d), k, dv);
            System.Action<string, bool> sb = (k, v) => d.SetBFor(EditBare(d), k, v);
            // GLOBAL-BACKED display defaults (Phase 0c). A row whose key is absent must show what
            // the RESOLVER would use — the global — not a hand-picked literal, or the slider reads
            // a number the HUD is not rendering and the first drag writes that wrong number in.
            System.Func<string, ConfigEntry<float>, float> gfg = (k, g)
                => d.GetFFor(EditBare(d), k, g != null ? g.Value : 0f);
            System.Func<string, ConfigEntry<bool>, bool> gbg = (k, g)
                => d.GetBFor(EditBare(d), k, g != null && g.Value);

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
                // The COLOUR ref resolves in both states; the WIDTH routes through
                // BorderWidthFor, which returns the global while following (review 2026-07-17:
                // offering the slider in Global made it a dead control).
                into.Add(HudProp.Color("Ring / outline colour", () => d.BorderFor(EditBare(d)), v => d.SetBorderFor(EditBare(d), v)));
                if (!UsesGlobalStyle)
                {
                    into.Add(HudProp.F("Ring / outline width", () => d.BorderWidthFor(EditBare(d)),
                        v => d.SetBorderWidthFor(EditBare(d), Mathf.Clamp(v, 0f, 8f)), 0f, 8f));
                    // Edge glass on the ring, matching a panel: an edge-light strength (drawn where
                    // the rim faces the key light) and a border fade (the unlit arc dissolves).
                    // These read the same "spec"/"bfade"/"customBorderFadeOn" keys a custom panel
                    // uses, and ApplyBorderOnlyEdge now honours them (previously hard-zeroed).
                    if (HudConfig.FxTierA == null || !HudConfig.FxTierA.Value)
                        into.Add(HudProp.Header("  (Surface/edge master is OFF globally — edge light/fade are inactive)"));
                    into.Add(HudProp.F("Ring edge light", () => gfg("spec", HudConfig.GlassEdge),
                        v => sf("spec", Mathf.Clamp01(v)), 0f, 1f));
                    into.Add(HudProp.Bool("Ring edge fade", () => gbg("customBorderFadeOn", HudConfig.FxBorderFadeOn),
                        v => sb("customBorderFadeOn", v)));
                    if (gbg("customBorderFadeOn", HudConfig.FxBorderFadeOn))
                        into.Add(HudProp.F("  edge fade amount", () => gfg("bfade", HudConfig.FxBorderFade),
                            v => sf("bfade", Mathf.Clamp01(v)), 0f, 1f));
                    // The ring's own halo. Custom style is otherwise byte-for-byte what it always
                    // was, so these two default to nothing: strength 0 emits no glow band at all,
                    // and an empty colour ref derives the halo from the ring colour (a panel's
                    // halo is likewise its own border hue).
                    into.Add(HudProp.F("Ring glow", () => gf("ringGlow", 0f),
                        v => sf("ringGlow", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    into.Add(HudProp.Color("Ring glow colour", () => d.GetSFor(EditBare(d), "ringGlowColor", ""),
                        v => d.SetSFor(EditBare(d), "ringGlowColor", string.IsNullOrEmpty(v) ? null : v),
                        () => HudPalette.PanelBorder.Value));
                }
                else
                {
                    // The ring is not a panel, so F9's Effects tab has no per-element glass row
                    // for it. Say where its edge actually comes from instead of leaving the
                    // author to conclude the element simply has no edge treatment.
                    into.Add(HudProp.Header(
                        "Ring edge follows the theme: edge light, border fade and halo, as on panels"));
                }
            }
            if (UsesGlobalStyle)
            {
                into.Add(HudProp.Header("Sizing, glass and effects follow the F9 globals (Theme + Effects)"));
            }
            else if (SupportsPanelAppearance)
            {
                into.Add(HudProp.F("Border width", () => d.BorderWidthFor(EditBare(d)),
                    v => d.SetBorderWidthFor(EditBare(d), Mathf.Clamp(v, 0f, 8f)), 0f, 8f));
                into.Add(HudProp.F("Edge softness / AA", () => gfg("feather", HudConfig.EdgeFeather),
                    v => sf("feather", Mathf.Clamp(v, 0f, 4f)), 0f, 4f));
                if (SupportsAuthoredCorners)
                {
                    into.Add(HudProp.F("Corner TL", () => d.RTLFor(EditBare(d)), v => d.SetRTLFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f));
                    into.Add(HudProp.F("Corner TR", () => d.RTRFor(EditBare(d)), v => d.SetRTRFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f));
                    into.Add(HudProp.F("Corner BR", () => d.RBRFor(EditBare(d)), v => d.SetRBRFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f));
                    into.Add(HudProp.F("Corner BL", () => d.RBLFor(EditBare(d)), v => d.SetRBLFor(EditBare(d), Mathf.Max(0f, v)), 0f, 64f));
                    // The four sliders above set the SIZE of the corner; this sets its SHAPE.
                    // Slot-aware through the same EditBare(d) accessors as its neighbours, so a
                    // per-tier fork picks it up automatically (SeedSlotFromBase reads the prop
                    // list itself — no key manifest to maintain).
                    into.Add(HudProp.Enum("Corner style",
                        () => Mathf.Clamp(d.GetIFor(EditBare(d), "cornerStyle", CornerStyleFollow), 0, 2),
                        v => d.SetIFor(EditBare(d), "cornerStyle", Mathf.Clamp(v, 0, 2)),
                        CornerStyleNames));
                    // The chamfer is native to the sharp panel shader from ABI 3 on, so this hint
                    // only appears when the FALLBACK is actually active — i.e. an older effects
                    // bundle is resident. With a current bundle a cut box is as rich as a rounded
                    // one and there is nothing to warn about.
                    if (Mathf.Clamp(d.GetIFor(EditBare(d), "cornerStyle", CornerStyleFollow), 0, 2)
                            == CornerStyleCut
                        && HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                        && Core.HudShaderStore.SdfAvailable
                        && !Core.HudShaderStore.SdfCutAvailable)
                        into.Add(HudProp.Header(
                            "  This bundle is too old to cut corners on the sharp panel shader — "
                            + "the box keeps its shape on the classic renderer"));
                }
                else
                {
                    // The Theme tab has "Default corner rounding", so its absence here reads as
                    // a missing control unless we say why. These surfaces derive their radius
                    // from their own size (pills) or from a pen contour, and would ignore it.
                    into.Add(HudProp.Header(Def != null && Def.Type == HudElementType.Shape
                        ? "Corner rounding comes from the pen contour — edit the points instead"
                        : "Corner rounding is derived from this element's size (pill shape)"));
                }
                into.Add(HudProp.F("Glass sheen", () => gfg("sheen", HudConfig.GlassSheen),
                    v => sf("sheen", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.F("Glass edge light", () => gfg("spec", HudConfig.GlassEdge),
                    v => sf("spec", Mathf.Clamp01(v)), 0f, 1f));
                if (SupportsAnalyticPanel)
                {
                    into.Add(HudProp.F("Corner shape (2=round, 8=squircle)", () => gfg("squircle", HudConfig.SdfSquircle),
                        v => sf("squircle", Mathf.Clamp(v, 2f, 8f)), 2f, 8f));
                    // Cut and squircle are mutually exclusive corner GEOMETRIES (the shader packs
                    // exponent 1 for the cut), so say so rather than let the slider look broken.
                    // The authored value is kept and resumes the moment Cut is off.
                    if (CornerCutFor() == 1
                        || (CornerCutFor() < 0 && HudConfig.HudCornerStyle != null
                            && HudConfig.HudCornerStyle.Value == 1))
                        into.Add(HudProp.Header(
                            "  Cut corners replace the squircle shoulder — this returns when Cut is off"));
                    into.Add(HudProp.Bool("Gaussian-distance halo", () => gbg("gaussianHalo", HudConfig.SdfGaussianHalo),
                        v => sb("gaussianHalo", v)));
                }
            }
            if (SupportsTrapezoid)
            {
                into.Add(HudProp.F("Top inset (trapezoid)", () => gf("insetTop", 0f),
                    v => sf("insetTop", Mathf.Max(0f, v)), 0f, 400f));
                into.Add(HudProp.F("Bottom inset (trapezoid)", () => gf("insetBottom", 0f),
                    v => sf("insetBottom", Mathf.Max(0f, v)), 0f, 400f));
            }
            if (UsesFontScale)
                into.Add(HudProp.F("Font scale", () => d.FontScaleFor(EditBare(d)),
                    v => d.SetFontScaleFor(EditBare(d), Mathf.Clamp(v, 0.4f, 3f)), 0.4f, 3f));
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

        // The sub-tab ids registered by HudEditorWindow.DrawEffectsTab's BeginSubTab calls.
        private const string FxTabGlass = "##UIAFxGlass";
        private const string FxTabEdges = "##UIAFxEdges";
        private const string FxTabGlow = "##UIAFxGlow";
        private const string FxTabBloom = "##UIAFxBloom";
        private const string FxTabAlerts = "##UIAFxAlerts";
        private const string FxTabAdvanced = "##UIAFxAdvanced";

        private static string Num(ConfigEntry<float> e) => e != null ? e.Value.ToString("0.00") : "--";
        private static string Num(ConfigEntry<int> e) => e != null ? e.Value.ToString() : "--";
        private static string Whole(ConfigEntry<float> e) => e != null ? ((int)e.Value).ToString() : "--";
        private static string Hz(ConfigEntry<float> e) => e != null ? e.Value.ToString("0.00") + " Hz" : "--";
        private static string Sec(ConfigEntry<float> e) => e != null ? e.Value.ToString("0.0") + " s" : "--";
        private static string OnOff(ConfigEntry<bool> e) => e != null && e.Value ? "ON" : "OFF";
        private static string Str(ConfigEntry<string> e)
            => e != null && !string.IsNullOrEmpty(e.Value) ? e.Value : "--";

        /// <summary>The consistent shared-global row: what it is, the standing wording, the jump.
        /// Used everywhere the popup used to drop a bare "…is a shared global" sentence.</summary>
        private static void AddSharedGlobalRow(List<HudProp> into, string what,
            string subTabId, string subTabName, string id, bool machineLocal = false)
        {
            into.Add(HudProp.Note("  " + what));
            into.Add(HudProp.Note("    " + (machineLocal ? MachineLocalWording : SharedGlobalWording)));
            AddJump(into, subTabId, subTabName, id);
        }

        /// <summary>Same, plus the knob's LIVE value — so a shared knob is never a blank pointer.
        /// The popup rebuilds its prop list every frame (HudEditorWindow's _propScratch), so the
        /// text is current without any subscription or cache.</summary>
        private static void AddSharedValueRow(List<HudProp> into, string what, string value,
            string subTabId, string subTabName, string id, bool machineLocal = false)
        {
            into.Add(HudProp.Note("  " + what + ":  " + value));
            into.Add(HudProp.Note("    " + (machineLocal ? MachineLocalWording : SharedGlobalWording)));
            AddJump(into, subTabId, subTabName, id);
        }

        /// <summary>The jump button. Its CAPTION is the path, so it degrades to a readable
        /// signpost if the F9 window is not on screen to receive the request.</summary>
        private static void AddJump(List<HudProp> into, string subTabId, string subTabName, string id)
        {
            into.Add(HudProp.Button("Open F9 > Effects > " + subTabName, "jmp_" + id,
                () => Windows.HudEditorWindow.RequestEditorTab("Effects", subTabId),
                "Opens the F9 HUD editor on that sub-tab. Everything there is shared by every panel."));
        }

        /// <summary>The read-only block that replaces the old two-line frost prose. Every knob here
        /// is physically shared — one dual-Kawase pyramid, one set of material uniforms, one
        /// full-screen post pass, one alert clock — so it is shown with its live value rather than
        /// as an editable row that would lie about being per-element.</summary>
        private static void AddSharedGlobalsBlock(List<HudProp> into)
        {
            into.Add(HudProp.Header("SHARED GLOBALS - one capture, one clock, every panel"));
            into.Add(HudProp.Note("Not per-element and never can be. They KEEP applying to this"));
            into.Add(HudProp.Note("element while it is off-global - only its own frost, chroma,"));
            into.Add(HudProp.Note("edge light and halo stop tracking the F9 sliders."));

            // The backdrop LOOK: shared uniforms on the shared glass materials. Travels with a theme.
            into.Add(HudProp.Note("  Backdrop darkening:  " + Num(HudConfig.FrostDarken)));
            into.Add(HudProp.Note("  Frost tint:  " + Str(HudConfig.FrostTint)));
            into.Add(HudProp.Note("    " + SharedGlobalWording));
            AddJump(into, FxTabGlass, "Glass", "backdroplook");

            // The backdrop COST: sizes/throttles the one blur pyramid. HudTheme.Exclude keeps both
            // out of themes so an imported look cannot tank another player's frame rate.
            into.Add(HudProp.Note("  Frost downsample:  1 / " + Whole(HudConfig.FrostDownsample)));
            into.Add(HudProp.Note("  Re-blur every:  " + Num(HudConfig.FrostUpdateEveryN) + " frame(s)"));
            into.Add(HudProp.Note("    " + MachineLocalWording));
            AddJump(into, FxTabAdvanced, "Advanced", "backdropcost");

            // Bloom is a full-screen post pass (HudBloomFx) — nothing about it can be per-element.
            into.Add(HudProp.Note("  Bloom:  " + OnOff(HudConfig.FxBloomOn)
                + "   strength " + Num(HudConfig.FxBloomStrength)
                + "   threshold " + Num(HudConfig.FxBloomThreshold)));
            into.Add(HudProp.Note("  Bloom highlights (band 2):  " + OnOff(HudConfig.FxBloom2On)
                + "   strength " + Num(HudConfig.FxBloom2Strength)));
            into.Add(HudProp.Note("    " + SharedGlobalWording));
            into.Add(HudProp.Note("    resolution, blur steps and fine detail are"));
            into.Add(HudProp.Note("    " + MachineLocalWording));
            AddJump(into, FxTabBloom, "Bloom", "bloom");

            // Alerts: HudAlertPulse reads the globals; the element contributes only a stable seed.
            into.Add(HudProp.Note("  Warning pulse:  " + OnOff(HudConfig.FxAlertPulseOn)
                + "   breath " + Sec(HudConfig.FxAlertBreathSeconds)
                + " x " + Num(HudConfig.FxAlertCautionBreaths)));
            into.Add(HudProp.Note("  Strength " + Num(HudConfig.FxAlertPulseStrength)
                + "   caution x" + Num(HudConfig.FxAlertCautionBright)
                + "   critical x" + Num(HudConfig.FxAlertCriticalBright)));
            into.Add(HudProp.Note("    " + SharedGlobalWording));
            AddJump(into, FxTabAlerts, "Alerts", "alerts");
        }

        private void AddUnifiedEffectProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            // Per-mode edit shorthands (see AddUnifiedAppearanceProps): every effect knob forks
            // between the SUIT base and the BARE override on EditBare(d). The ONE exception is the
            // "Motion & power transitions" trio (collapse / glitch / warp) further down — those are
            // NOT threaded at render time (a death/power transition shouldn't fork per mode), so
            // they stay on the shared base via raw d.GetB/d.SetB here to keep editor and render honest.
            System.Func<string, float, float> gf = (k, dv) => d.GetFFor(EditBare(d), k, dv);
            System.Action<string, float> sf = (k, v) => d.SetFFor(EditBare(d), k, v);
            System.Func<string, bool, bool> gb = (k, dv) => d.GetBFor(EditBare(d), k, dv);
            System.Action<string, bool> sb = (k, v) => d.SetBFor(EditBare(d), k, v);
            // POPUP DEFAULTS = RENDERER DEFAULTS (Phase 0c, 2026-07-26). Every row whose key can be
            // absent reads its display default from the SAME global the resolver falls back to.
            // The literals these replaced disagreed with the renderer — customFrost showed 1.0
            // against a live FrostStrength of 0.87, customChroma 0.3 against 0.564, glowWidth 24
            // against FxGlowWidth — so the slider read a number the HUD was not rendering and the
            // first drag wrote that wrong number in. The three keys that genuinely have NO global
            // (rippleSmooth, edgeFadeX/Y, customFrostOn) keep their literal and say so in place.
            System.Func<string, ConfigEntry<float>, float> gfg = (k, g)
                => d.GetFFor(EditBare(d), k, g != null ? g.Value : 0f);
            System.Func<string, ConfigEntry<bool>, bool> gbg = (k, g)
                => d.GetBFor(EditBare(d), k, g != null && g.Value);
            into.Add(HudProp.Header("Effects"));
            if (SupportsPanelAppearance && OptionalPanelBackgroundIsOff)
                into.Add(HudProp.Header(OptionalPanelBackgroundHint));
            if (SupportsAnalyticPanel
                && (HudConfig.SdfPanels == null || !HudConfig.SdfPanels.Value
                    || !Core.HudShaderStore.SdfAvailable))
                into.Add(HudProp.Header(HudConfig.SdfPanels == null || !HudConfig.SdfPanels.Value
                    ? "Analytic SDF panels are OFF — advanced halo motion is inactive; fallback radius caps at 160 px"
                    : "ABI-2 SDF bundle is unavailable — advanced halo motion is inactive; fallback radius caps at 160 px"));
            if (SupportsPanelAppearance && UsesGlobalStyle)
                into.Add(HudProp.Header("Panel appearance follows the F9 Effects globals"));
            if (SupportsPanelAppearance && UsesCustomStyle)
            {
                // PARITY CONTRACT: this block mirrors the F9 Effects tab section-for-section,
                // label-for-label and range-for-range, so separating an element from the globals
                // never presents a smaller or differently-named set of knobs than the tab it just
                // stopped following. Values that physically CANNOT be per-element (shared material
                // uniforms — light direction/tint/rim/falloff, the animation clocks, and the one
                // backdrop capture) are named in place as shared rather than silently omitted:
                // an author who cannot find "light angle" here must be told where it went.

                // ---- mirrors global "Edges, glow & pulse" ----
                into.Add(HudProp.Header("Edges, glow & pulse"));
                if (HudConfig.FxTierA == null || !HudConfig.FxTierA.Value)
                    into.Add(HudProp.Header("Surface/edge master is OFF globally — Tier A values are inactive"));

                // The global tab splits edge light across Theme ("Default glass edge light")
                // and Effects ("Edge energy -> Strength"), which ADD. Custom collapses them
                // into the single final value the snapshot froze, so the same key is offered
                // in both places rather than inventing a second strength that would
                // double-count on the next snapshot. UNGATED: GlassEdgeFor renders `spec`
                // regardless of the Edge-energy checkbox, so hiding this row behind it left a
                // hidden-but-active value (review 2026-07-17).
                into.Add(HudProp.F("  strength (= Appearance -> Glass edge light)", () => gfg("spec", HudConfig.GlassEdge),
                    v => sf("spec", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Edge energy (borders + lines)", () => gbg("customRippleOn", HudConfig.FxEdgeLightOn),
                    v => sb("customRippleOn", v)));
                if (gbg("customRippleOn", HudConfig.FxEdgeLightOn))
                {
                    AddSharedGlobalRow(into,
                        "light angle, colour, opposing-rim catch and falloff",
                        FxTabEdges, "Edges", "edgelight");
                    into.Add(HudProp.F("  irregular energy", () => gfg("ripple", HudConfig.FxEdgeRipple),
                        v => sf("ripple", Mathf.Clamp(v, 0f, 2.5f)), 0f, 2.5f));
                    into.Add(HudProp.F("  energy frequency", () => gfg("rippleFreq", HudConfig.FxEdgeRippleFreq),
                        v => sf("rippleFreq", Mathf.Clamp(v, 0.05f, 8f)), 0.05f, 8f));
                    // The one control that genuinely has no global to fall back to.
                    into.Add(HudProp.F("  energy smoothness (per-element only)", () => gf("rippleSmooth", 0f),
                        v => sf("rippleSmooth", Mathf.Clamp01(v)), 0f, 1f));
                    if (SupportsAnalyticPanel || (Def != null && Def.Type == HudElementType.Shape))
                        into.Add(HudProp.F("  flow speed (0 = frozen)", () => gfg("edgeFlow", HudConfig.FxEdgeFlowSpeed),
                            v => sf("edgeFlow", Mathf.Clamp(v, 0f, 4f)), 0f, 4f));
                    AddSharedGlobalRow(into, "per-element ripple desync (on + amount)",
                        FxTabEdges, "Edges", "desync");
                    if (SupportsAnalyticPanel)
                    {
                        into.Add(HudProp.Bool("  Flowing edge aura (SDF)", () => gbg("customGlowFlowOn", HudConfig.FxGlowFlowAuraOn),
                            v => sb("customGlowFlowOn", v)));
                        if (gbg("customGlowFlowOn", HudConfig.FxGlowFlowAuraOn))
                            into.Add(HudProp.F("    aura strength", () => gfg("glowFlowAura", HudConfig.FxGlowFlowAura),
                                v => sf("glowFlowAura", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    }
                }

                into.Add(HudProp.Bool("Border fade (unlit sections dissolve)", () => gbg("customBorderFadeOn", HudConfig.FxBorderFadeOn),
                    v => sb("customBorderFadeOn", v)));
                if (gbg("customBorderFadeOn", HudConfig.FxBorderFadeOn))
                    into.Add(HudProp.F("  fade amount", () => gfg("bfade", HudConfig.FxBorderFade),
                        v => sf("bfade", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Soft edge (boxes melt together)", () => gbg("customSoftEdgeOn", HudConfig.FxSoftEdgeOn),
                    v => sb("customSoftEdgeOn", v)));
                if (gbg("customSoftEdgeOn", HudConfig.FxSoftEdgeOn))
                    into.Add(HudProp.F("  width (px)", () => gfg("softEdge", HudConfig.FxSoftEdge),
                        v => sf("softEdge", Mathf.Clamp(v, 0f, 48f)), 0f, 48f));

                // F9 places BOX END FADE inside "Edges, glow & pulse" (after soft edge); the
                // popup mirrors that order. The pair stays editable in Global mode too — the
                // stand-alone section below covers that path. Both are element GEOMETRY with no
                // global counterpart, so 0 is the honest display default.
                into.Add(HudProp.Header("Box end fade"));
                into.Add(HudProp.F("Fade box ends L/R", () => gf("edgeFadeX", 0f),
                    v => sf("edgeFadeX", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
                into.Add(HudProp.F("Fade box top/bottom", () => gf("edgeFadeY", 0f),
                    v => sf("edgeFadeY", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
                AddSharedGlobalRow(into, "fade curve and border influence (the ramp SHAPE)",
                    FxTabGlass, "Glass", "edgefadeshape");

                if (SupportsGlowHalo)
                {
                    into.Add(HudProp.Bool("Glow halo", () => gbg("customGlowOn", HudConfig.FxGlowOn),
                        v => sb("customGlowOn", v)));
                    if (gbg("customGlowOn", HudConfig.FxGlowOn))
                    {
                        into.Add(HudProp.F("  outward strength", () => gfg("glow", HudConfig.FxGlow),
                            v => sf("glow", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                        into.Add(HudProp.F("  inward strength", () => gfg("glowIn", HudConfig.FxGlowInner),
                            v => sf("glowIn", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                        if (SupportsAnalyticPanel)
                            into.Add(HudProp.F("  extended atmospheric haze (SDF)", () => gfg("glowHaze", HudConfig.FxGlowHaze),
                                v => sf("glowHaze", Mathf.Clamp01(v)), 0f, 1f));
                    }
                }

                // The envelope (radius/spread) drives the MESH halo too, so it shows for any
                // surface with glow on — Shapes included. The organic extras (uneven reach,
                // breathing) are analytic-shader terms and stay rectangle-only.
                bool customHaloEnvelopeOn = (SupportsGlowHalo && gbg("customGlowOn", HudConfig.FxGlowOn))
                    || (SupportsAnalyticPanel
                        && gbg("customRippleOn", HudConfig.FxEdgeLightOn)
                        && gbg("customGlowFlowOn", HudConfig.FxGlowFlowAuraOn));
                if (customHaloEnvelopeOn)
                {
                    into.Add(HudProp.Header("SHARED HALO / FLOWING-AURA ENVELOPE"));
                    // A Shape renders the MESH halo, whose GlowWidth clamps at 160 px — a
                    // 320 slider ceiling there means the top half silently does nothing
                    // (review 2026-07-17). Analytic panels carry the full 320.
                    float haloCap = SupportsAnalyticPanel ? 320f : 160f;
                    into.Add(HudProp.F("  Halo / aura radius (px)", () => gfg("glowWidth", HudConfig.FxGlowWidth),
                        v => sf("glowWidth", Mathf.Clamp(v, 6f, haloCap)), 6f, haloCap));
                    into.Add(HudProp.F("  spread (tight rim -> diffuse)", () => gfg("glowDiffuse", HudConfig.FxGlowDiffuse),
                        v => sf("glowDiffuse", Mathf.Clamp01(v)), 0f, 1f));
                    into.Add(HudProp.F("  extra diffuse (beyond max spread)", () => gfg("glowExtraDiffuse", HudConfig.FxGlowExtraDiffuse),
                        v => sf("glowExtraDiffuse", Mathf.Clamp01(v)), 0f, 1f));
                    if (SupportsAnalyticPanel)
                    {
                        into.Add(HudProp.Bool("  Uneven / organic reach (SDF)", () => gbg("customGlowUnevenOn", HudConfig.FxGlowUnevenOn),
                            v => sb("customGlowUnevenOn", v)));
                        if (gbg("customGlowUnevenOn", HudConfig.FxGlowUnevenOn))
                        {
                            into.Add(HudProp.F("    unevenness amount", () => gfg("glowUneven", HudConfig.FxGlowUneven),
                                v => sf("glowUneven", Mathf.Clamp01(v)), 0f, 1f));
                            into.Add(HudProp.F("    organic scale (1 = classic)", () => gfg("glowOrganicScale", HudConfig.FxGlowOrganicScale),
                                v => sf("glowOrganicScale", Mathf.Clamp(v, 0.25f, 4f)), 0.25f, 4f));
                        }
                        into.Add(HudProp.Bool("  Halo / aura breathing (SDF)", () => gbg("customGlowBreathOn", HudConfig.FxGlowBreathOn),
                            v => sb("customGlowBreathOn", v)));
                        if (gbg("customGlowBreathOn", HudConfig.FxGlowBreathOn))
                            into.Add(HudProp.F("    breath depth", () => gfg("glowBreath", HudConfig.FxGlowBreath),
                                v => sf("glowBreath", Mathf.Clamp01(v)), 0f, 1f));
                    }
                    into.Add(HudProp.Note("  Extreme radius increases transparent GPU overdraw."));
                }
                AddSharedValueRow(into, "halo breath speed",
                    Hz(HudConfig.FxGlowBreathSpeed), FxTabGlow, "Glow", "breathspeed");

                // Global groups the per-element pulse opt-in under "Edges, glow & pulse"; match it
                // here rather than stranding it below in Motion, where it reads as a different
                // feature from the one the tab presents.
                // The pulse CONTROL now lives in "Motion & power transitions" below, with the rest
                // of the tri-state effects — one row per effect, in one place, for every element
                // (Custom or not). Only the pointer stays here so the F9 grouping still reads true.
                into.Add(HudProp.Note("  Breathing pulse: see \"Motion & power transitions\" below."));
                if (HudConfig.FxPulseOn == null || !HudConfig.FxPulseOn.Value)
                    into.Add(HudProp.Note("  Per-element pulse is disabled globally."));
                AddSharedValueRow(into, "pulse speed / depth",
                    Hz(HudConfig.FxPulseSpeed) + ", " + Num(HudConfig.FxPulseDepth),
                    FxTabGlow, "Glow", "pulseshape");

                // ---- mirrors global "Glass animation" ----
                into.Add(HudProp.Header(SupportsAnalyticPanel
                    ? "Glass animation"
                    : "Glass animation (legacy surface: independent strengths are approximate)"));
                if (HudConfig.FxTierB == null || !HudConfig.FxTierB.Value)
                    into.Add(HudProp.Header("Glass animation master is OFF globally — Tier B values are inactive"));
                into.Add(HudProp.Bool("Shine sweep", () => gbg("customShineOn", HudConfig.FxShineOn),
                    v => sb("customShineOn", v)));
                if (gbg("customShineOn", HudConfig.FxShineOn))
                {
                    into.Add(WithId(HudProp.F("  strength", () => gfg("customShine", HudConfig.FxShine),
                        v => sf("customShine", Mathf.Clamp(v, 0f, 2f)), 0f, 2f), "customShine"));
                    AddSharedValueRow(into, "sweep period",
                        Sec(HudConfig.FxShinePeriod), FxTabGlass, "Glass", "shineperiod");
                }
                into.Add(HudProp.Bool("Iridescent rim", () => gbg("customIridOn", HudConfig.FxIridOn),
                    v => sb("customIridOn", v)));
                if (gbg("customIridOn", HudConfig.FxIridOn))
                    into.Add(WithId(HudProp.F("  strength", () => gfg("customIrid", HudConfig.FxIridescence),
                        v => sf("customIrid", Mathf.Clamp01(v)), 0f, 1f), "customIrid"));
                into.Add(HudProp.Bool("Chromatic fringe (uses frosted backdrop)", () => gbg("customChromaOn", HudConfig.FxChromaOn),
                    v => sb("customChromaOn", v)));
                if (gbg("customChromaOn", HudConfig.FxChromaOn))
                {
                    into.Add(WithId(HudProp.F("  strength", () => gfg("customChroma", HudConfig.FxChroma),
                        v => sf("customChroma", Mathf.Clamp01(v)), 0f, 1f), "customChroma"));
                    // The fringe is produced by OFFSETTING the frosted backdrop sample, so with no
                    // frost there is nothing to offset (HudPanelSdf.shader, the chroma tap). This
                    // gate is correct, but until now the popup only hinted at it with "(uses
                    // frosted backdrop)" — and it is the second-order term behind FlorpyDorp's
                    // "the chromatic aberration AND the frosted blur dissipate together".
                    into.Add(HudProp.Note(
                        "    Needs frost > 0 on THIS element - the fringe is the frosted"));
                    into.Add(HudProp.Note(
                        "    backdrop sampled at an offset, so no frost means no fringe."));
                    if (FrostAmountFor() <= 0.001f)
                        into.Add(HudProp.Note(
                            "    Inactive right now: this element's frost resolves to 0."));
                }
                // Dissolve is a TRANSITION, not a steady glass term: its tri-state row is in
                // "Motion & power transitions" below (writing the old `customDissolve` bool from
                // here as well would let two controls disagree with the resolver).
                into.Add(HudProp.Note("  Dissolve reveal: see \"Motion & power transitions\" below."));

                // ---- mirrors global "Frosted glass" ----
                into.Add(HudProp.Header("Frosted glass"));
                if (HudConfig.FxTierC == null || !HudConfig.FxTierC.Value)
                    into.Add(HudProp.Header("Frosted-glass master is OFF globally — Tier C values are inactive"));
                // No global counterpart: FrostAmountFor's own default for this key is a literal
                // true (the global gate is the STRENGTH slider, offered below).
                into.Add(HudProp.Bool("Frosted glass", () => gb("customFrostOn", true),
                    v => sb("customFrostOn", v)));
                if (gb("customFrostOn", true))
                {
                    into.Add(HudProp.F("  frost strength", () => gfg("customFrost", HudConfig.FrostStrength),
                        v => sf("customFrost", Mathf.Clamp01(v)), 0f, 1f));
                    if (SupportsAnalyticPanel)
                        into.Add(HudProp.F("  blur depth (shallow - deep)", () => gfg("frostDepth", HudConfig.FrostDepth),
                            v => sf("frostDepth", Mathf.Clamp01(v)), 0f, 1f));
                }
            }

            // ---- Polyline mirror of the F9 "Edges, glow & pulse" section ----
            // Lines render the edge-light/ripple family and the mesh glow halo (2026-07-16),
            // resolved through the SAME two-state contract as panels. The line's strength key
            // stays `edgeLight` (0..2, the raw F9 "Edge energy -> Strength" scale) — panels
            // collapse theirs into `spec`, which is a different 0..1 glass quantity.
            bool isLine = Def != null && Def.Type == HudElementType.Polyline;
            if (isLine && UsesGlobalStyle)
                into.Add(HudProp.Header("Line effects follow the F9 Effects globals"));
            if (isLine && UsesCustomStyle)
            {
                into.Add(HudProp.Header("Edges, glow & pulse"));
                if (HudConfig.FxTierA == null || !HudConfig.FxTierA.Value)
                    into.Add(HudProp.Header("Surface/edge master is OFF globally — Tier A values are inactive"));
                into.Add(HudProp.Bool("Edge energy (borders + lines)", () => gbg("customRippleOn", HudConfig.FxEdgeLightOn),
                    v => sb("customRippleOn", v)));
                if (gbg("customRippleOn", HudConfig.FxEdgeLightOn))
                {
                    into.Add(HudProp.F("  strength (line edge light)", () => gfg("edgeLight", HudConfig.FxEdgeLight),
                        v => sf("edgeLight", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    AddSharedGlobalRow(into,
                        "light angle, colour, opposing-rim catch and falloff",
                        FxTabEdges, "Edges", "lineedgelight");
                    into.Add(HudProp.F("  irregular energy", () => gfg("ripple", HudConfig.FxEdgeRipple),
                        v => sf("ripple", Mathf.Clamp(v, 0f, 2.5f)), 0f, 2.5f));
                    into.Add(HudProp.F("  energy frequency", () => gfg("rippleFreq", HudConfig.FxEdgeRippleFreq),
                        v => sf("rippleFreq", Mathf.Clamp(v, 0.05f, 8f)), 0.05f, 8f));
                    into.Add(HudProp.F("  energy smoothness (per-element only)", () => gf("rippleSmooth", 0f),
                        v => sf("rippleSmooth", Mathf.Clamp01(v)), 0f, 1f));
                    AddSharedGlobalRow(into, "per-element ripple desync (on + amount)",
                        FxTabEdges, "Edges", "linedesync");
                }
                into.Add(HudProp.Bool("Glow halo", () => gbg("customGlowOn", HudConfig.FxGlowOn),
                    v => sb("customGlowOn", v)));
                if (gbg("customGlowOn", HudConfig.FxGlowOn))
                {
                    into.Add(HudProp.F("  outward strength", () => gfg("glow", HudConfig.FxGlow),
                        v => sf("glow", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    into.Add(HudProp.Header("SHARED HALO / FLOWING-AURA ENVELOPE"));
                    // The line halo is the MESH recipe: its radius cap is 160 px, not the
                    // analytic panels' 320.
                    into.Add(HudProp.F("  Halo / aura radius (px)", () => gfg("glowWidth", HudConfig.FxGlowWidth),
                        v => sf("glowWidth", Mathf.Clamp(v, 6f, 160f)), 6f, 160f));
                    into.Add(HudProp.F("  spread (tight rim -> diffuse)", () => gfg("glowDiffuse", HudConfig.FxGlowDiffuse),
                        v => sf("glowDiffuse", Mathf.Clamp01(v)), 0f, 1f));
                    into.Add(HudProp.F("  extra diffuse (beyond max spread)", () => gfg("glowExtraDiffuse", HudConfig.FxGlowExtraDiffuse),
                        v => sf("glowExtraDiffuse", Mathf.Clamp01(v)), 0f, 1f));
                    AddSharedValueRow(into, "halo breath speed",
                        Hz(HudConfig.FxGlowBreathSpeed), FxTabGlow, "Glow", "linebreathspeed");
                }
            }

            // ONE shared-globals block per popup, whichever mirror ran above (a Polyline is not a
            // panel, so the two branches are mutually exclusive today — but a duplicated block
            // would collide on the jump buttons' StableIds, so this is stated rather than assumed).
            if (UsesCustomStyle && (SupportsPanelAppearance || isLine))
                AddSharedGlobalsBlock(into);

            // These are element participation/geometry controls rather than theme values, so
            // they remain editable in both coherent modes. (Custom shows the pair inline above,
            // in the F9 position — this stand-alone section covers the following state.)
            if (SupportsPanelAppearance && !UsesCustomStyle)
            {
                into.Add(HudProp.Header("Box end fade"));
                into.Add(HudProp.F("Fade box ends L/R", () => gf("edgeFadeX", 0f),
                    v => sf("edgeFadeX", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
                into.Add(HudProp.F("Fade box top/bottom", () => gf("edgeFadeY", 0f),
                    v => sf("edgeFadeY", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
                // The RAMP SHAPE (curve + how much the border joins in) is shared, like the edge
                // light's angle and falloff — say where it lives rather than leave a gap.
                AddSharedGlobalRow(into, "fade curve and border influence (the ramp SHAPE)",
                    FxTabGlass, "Glass", "edgefadeshapeglobal");
            }

            // ---- Motion & power transitions ------------------------------------------------
            // DELIBERATELY NOT gated on UsesCustomStyle (bug, 2026-07-19: the toggles were both
            // invisible AND ignored for a global-styled element). "Follow the global THEME" and
            // "follow the global MOTION" are different questions: an element must be able to keep
            // the shared look while sitting a transition out — or, from a Custom element, keep
            // inheriting the globals for motion. So this block is unconditional and every row is a
            // tri-state, which is the only encoding that can express Off at all.
            //
            // One row per registry effect, driven by HudTransitionFx.All, so this inspector and the
            // F9 global menu cannot present different effects or different labels.
            into.Add(HudProp.Header("Motion & power transitions"));
            into.Add(HudProp.Header("  Inherit = follow the F9 global (Effects -> Suit power)"));
            into.Add(HudProp.Header("  On = force it, at this element's own strength.  Off = never."));
            for (int i = 0; i < HudTransitionFx.All.Length; i++)
                AddTransitionRows(into, d, HudTransitionFx.All[i]);
            into.Add(HudProp.Header("  A global master that is OFF wins over every element."));
            into.Add(HudProp.Header("  Transitions are shared by the suit and bare layouts."));
            MarkProps(into, start, HudPropGroup.Effects);
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
