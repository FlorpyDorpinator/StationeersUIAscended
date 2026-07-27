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

        // The per-slot StyleSourceOf helpers that used to sit here are gone (Phase 3). Nothing reads
        // the two-state field at runtime any more — HudStyleFx.SourceOf answers per CATEGORY, and
        // "styleSource" survives only as a write for downgrade compat (see WriteSourceBits) and as
        // the legacy input the def-only snapshot and HudStyleMigration still interpret.

        // ================= PER-CATEGORY FOLLOW (Phase 3, 2026-07-27) =========================
        //
        // The single two-state switch above is no longer what the resolvers ask. Each of the five
        // followable families — Surface / Glass / Edges / Glow / Transitions, i.e. F9's own
        // sub-tabs — carries its own source, packed two bits per category into the element's
        // "styleSrc" param (HudStyleFx.SourceParamKey). "styleSource" is still WRITTEN in lock-step
        // (1 when every category follows, 2 otherwise) so a downgrade to 0.9.2.x still renders
        // correctly for one release; nothing reads it any more except the legacy def-only snapshot
        // and HudStyleMigration.
        //
        // THE ONE RULE, everywhere (plan §3.2):
        //     Global  ->  the global value
        //     Own     ->  the element's stored param, WITH THE GLOBAL AS ITS DEFAULT
        //     Donor   ->  Global for now (Phase 4)
        // The "default IS the global" half is Phase 0b's one missing-key convention, and it is what
        // makes the Phase 3 migration a pure re-encoding: an incomplete old Custom snapshot picks
        // the globals up for the keys it never stored, so nothing needs seeding at migration time.

        /// <summary>Where ONE category of this element's style comes from, for the slot the HUD is
        /// drawing (StyleSlot, so an element that has not forked reads the base). One param-bag
        /// read plus two bit ops — the same cost the single styleSource read used to be.</summary>
        protected HudFxSource SourceFor(HudFxCategory cat)
            => HudStyleFx.SourceOf(Def, cat, StyleSlot);

        /// <summary>True when this category carries the element's OWN values.</summary>
        protected bool Owns(HudFxCategory cat) => SourceFor(cat) == HudFxSource.Own;

        /// <summary>True when this category follows the F9 globals (or a donor, which resolves to
        /// the globals until Phase 4).</summary>
        protected bool Follows(HudFxCategory cat) => SourceFor(cat) != HudFxSource.Own;

        /// <summary>True while this element's SURFACE family (sizing, corners, glass sheen and
        /// edge light — F9's Theme tab) follows the globals. Widget props that would merely mirror
        /// a global sizing value hide behind this; it is the successor to the old whole-element
        /// FollowGlobal, narrowed to the category those props actually belong to.</summary>
        protected bool FollowGlobal => Follows(HudFxCategory.Surface);

        /// <summary>Per-corner radius. SURFACE category; the -1 sentinel is retained for legacy
        /// profiles and still means "the global".</summary>
        protected float Radius(float perCorner)
        {
            float global = HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f;
            if (Follows(HudFxCategory.Surface)) return global;
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
            if (Follows(HudFxCategory.Surface)) return -1;
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
            if (Follows(HudFxCategory.Surface)) return global;
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

            // PHASE 3: the two branches this used to have (Custom vs follow-global) collapsed into
            // one, because every value in them was already resolved by a per-family helper and only
            // TWO things actually differ — and both belong to the SURFACE family:
            //   • whether the halo comes from the element's own "ringGlow" or is derived from the
            //     panel recipe (RingGlowFor, which now asks the same question);
            //   • whether the halo takes the authored "ringGlowColor" ref or the rim's own hue.
            // Edge light (spec, Surface), border fade (Glass) and halo radius (Glow) were computed
            // IDENTICALLY in both branches, so they simply resolve through their own categories now
            // and a ring can, say, own its width while still following the global border fade.
            //
            // THE HAIRLINE FLOOR IS NOW UNCONDITIONAL, and that is a FIX, not a simplification.
            // The old Custom branch kept the classic 0 floor, so unfollowing Theme on a portrait
            // under a theme whose global PanelBorderWidth is sub-pixel (0.076 on Stationeers Blue
            // and Zirillian Red) dropped the rim BELOW PanelGraphic's own `bw > 0.05f` admit gate
            // and the ring vanished — the exact defect ApplyBorderOnlyEdge was written to fix,
            // re-entering through the follow checkbox. Panel parity in BOTH states: a ring's
            // sub-pixel line behaves like a panel's sub-pixel line, always.
            bool ownSurface = Owns(HudFxCategory.Surface);
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            ring.HairlineFloor = 0.05f;
            ring.EdgeSpec = GlassEdgeFor();
            ring.EdgeTint = PanelGraphic.LightTint();
            ring.EdgeFade = tierA
                && StyleFeatureOn(HudFxCategory.Glass, "customBorderFadeOn", HudConfig.FxBorderFadeOn)
                ? OwnOrGlobal(HudFxCategory.Glass, "bfade", HudConfig.FxBorderFade) : 0f;
            ring.GlowStrength = RingGlowFor();
            ring.GlowWidth = OwnOrGlobal(HudFxCategory.Glow, "glowWidth", HudConfig.FxGlowWidth);
            if (!ownSurface)
            {
                ring.GlowColor = Color.clear;                        // the rim's own hue, as panels do
                return;
            }
            string cref = Def != null ? Def.GetSFor(LayoutBare, "ringGlowColor", "") : "";
            ring.GlowColor = string.IsNullOrEmpty(cref)
                ? Color.clear                                        // clear = derive from the rim
                : HudPalette.Resolve(cref, HudPalette.PanelBorder.Value);
        }

        /// <summary>The halo strength a BORDER-ONLY ring is currently showing, resolved the same
        /// way in both style states so the separation snapshot can freeze it (Phase 0a).
        /// Custom stores its own "ringGlow" (0 = no halo band at all); Global derives the ring's
        /// halo from the panel recipe, so separating an element with the glow master on used to
        /// drop the halo entirely — this is the value the seed now writes down.</summary>
        private float RingGlowFor()
        {
            if (Def == null) return 0f;
            // "ringGlow" is a SURFACE row (the registry gives it no F9 home and files it with the
            // ring's other chrome), so the Surface source decides whether the ring carries its own
            // halo band or derives one from the Glow family's panel recipe.
            if (Owns(HudFxCategory.Surface))
                return Mathf.Max(0f, Def.GetFFor(LayoutBare, "ringGlow", 0f));
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = tierA
                && StyleFeatureOn(HudFxCategory.Glow, "customGlowOn", HudConfig.FxGlowOn);
            return glowOn
                ? Mathf.Max(0f, OwnOrGlobal(HudFxCategory.Glow, "glow", HudConfig.FxGlow)) : 0f;
        }

        /// <summary>Used by F9 to suppress panel-only actions on text, borrowed vanilla UI and
        /// other elements that cannot render any UIA surface.</summary>
        internal bool CanFlatten => SupportsPanelAppearance;

        internal static bool CanFlattenDefinition(HudElementDef d)
            => SupportsPanelAppearanceFor(d);

        /// <summary>Does this element own ANY style family of its own (base slot)? The Phase 3
        /// successor to "styleSource == 2": a MIXED element answers true, because it is no longer
        /// a pure follower. Callers that need a finer answer ask
        /// <see cref="HudStyleFx.SourceOf"/> per category.</summary>
        internal static bool IsCustomStyleDefinition(HudElementDef d)
            => !HudStyleFx.AllFollow(d, HudStyleSlot.Base);

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
            if (Follows(HudFxCategory.Surface)) return global;
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
            if (Owns(HudFxCategory.Surface))
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
            if (Follows(HudFxCategory.Surface)) return -1f;
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
            // CATEGORY MAP for this block, taken from the registry (HudStyleFx.All), which is the
            // single truth: border fade and soft edge are GLASS rows (F9 draws them under Effects >
            // Glass, CORE EFFECTS — decision 4 says the categories ARE the F9 sub-tabs); the halo
            // family is GLOW; the ripple/flow family is EDGES.
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool bfOn = tierA
                && StyleFeatureOn(HudFxCategory.Glass, "customBorderFadeOn", HudConfig.FxBorderFadeOn);
            bool seOn = tierA
                && StyleFeatureOn(HudFxCategory.Glass, "customSoftEdgeOn", HudConfig.FxSoftEdgeOn);
            bool glOn = tierA
                && StyleFeatureOn(HudFxCategory.Glow, "customGlowOn", HudConfig.FxGlowOn);
            g.BorderFade = bfOn ? OwnOrGlobal(HudFxCategory.Glass, "bfade", HudConfig.FxBorderFade) : 0f;
            g.SoftEdge = seOn ? OwnOrGlobal(HudFxCategory.Glass, "softEdge", HudConfig.FxSoftEdge) : 0f;
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
            float glowBase = glOn ? OwnOrGlobal(HudFxCategory.Glow, "glow", HudConfig.FxGlow) : 0f;
            g.Glow = tierA ? (BorderWidthFor() > 0.05f ? HudAlertPulse.Glow(glowBase, AlertSeed) : glowBase) : 0f;
            g.GlowInner = glOn ? OwnOrGlobal(HudFxCategory.Glow, "glowIn", HudConfig.FxGlowInner) : 0f; // never floored: sits under the text
            g.GlowWidth = OwnOrGlobal(HudFxCategory.Glow, "glowWidth", HudConfig.FxGlowWidth);
            g.GlowDiffuse = OwnOrGlobal(HudFxCategory.Glow, "glowDiffuse", HudConfig.FxGlowDiffuse);
            // An SDF-family key (2026-07-16). Since Phase 0b a missing per-element value resolves
            // to the GLOBAL, like every other knob here — see NewSdfOwnOrGlobal.
            g.GlowExtraDiffuse = Mathf.Clamp01(
                NewSdfOwnOrGlobal(HudFxCategory.Glow, "glowExtraDiffuse", HudConfig.FxGlowExtraDiffuse));
            bool rippleOn = tierA
                && StyleFeatureOn(HudFxCategory.Edges, "customRippleOn", HudConfig.FxEdgeLightOn);
            g.EdgeRipple = rippleOn ? OwnOrGlobal(HudFxCategory.Edges, "ripple", HudConfig.FxEdgeRipple) : 0f;
            g.EdgeRippleFreq = RippleFreqFor(
                OwnOrGlobal(HudFxCategory.Edges, "rippleFreq", HudConfig.FxEdgeRippleFreq));
            // rippleSmooth is the one knob with NO global at all, so "following" can only mean 0.
            g.RippleSmooth = Owns(HudFxCategory.Edges) && Def != null
                ? Def.GetFFor(LayoutBare, "rippleSmooth", 0f) : 0f;

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

        /// <summary>THE float resolver, per category (plan §3.2). <paramref name="cat"/> names the
        /// follow-able family the key belongs to — always the one the REGISTRY assigns it
        /// (<see cref="HudStyleFx.All"/>), never a guess at the call site: border fade and soft edge
        /// are Glass rows even though they read as "edges", because that is where F9 draws them.
        ///
        /// Following ⇒ the global. Own ⇒ the element's own param, with the global as the default
        /// (the -1 sentinel is retained for the legacy profiles that wrote it).</summary>
        protected float OwnOrGlobal(HudFxCategory cat, string key, ConfigEntry<float> global)
        {
            if (Follows(cat)) return global != null ? global.Value : 0f;
            float v = Def != null ? Def.GetFFor(LayoutBare, key, -1f) : -1f;
            return v >= 0f ? v : (global != null ? global.Value : 0f);
        }

        /// <summary>THE companion-bool resolver, per category. Same rule: following ⇒ the global
        /// master; Own ⇒ the element's own checkbox, defaulting to the global master.</summary>
        protected bool StyleFeatureOn(HudFxCategory cat, string customKey, ConfigEntry<bool> global)
        {
            bool gv = global != null && global.Value;
            return Owns(cat) && Def != null ? Def.GetBFor(LayoutBare, customKey, gv) : gv;
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
        private bool NewSdfFeatureOn(HudFxCategory cat, string customKey, ConfigEntry<bool> global)
        {
            bool gv = global != null && global.Value;
            return Owns(cat) && Def != null ? Def.GetBFor(LayoutBare, customKey, gv) : gv;
        }

        protected float NewSdfOwnOrGlobal(HudFxCategory cat, string key, ConfigEntry<float> global)
        {
            float gv = global != null ? global.Value : 0f;
            if (Follows(cat)) return gv;
            // Own: a missing key resolves to the GLOBAL, the same rule OwnOrGlobal uses.
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
            if (Follows(HudFxCategory.Surface)) return global;
            float own = Def != null ? Def.GetFFor(LayoutBare, "squircle", -1f) : -1f;
            return own >= 2f ? Mathf.Clamp(own, 2f, 8f) : global;
        }

        private bool SdfGaussianFor()
        {
            bool global = HudConfig.SdfGaussianHalo != null && HudConfig.SdfGaussianHalo.Value;
            return Owns(HudFxCategory.Surface) && Def != null
                ? Def.GetBFor(LayoutBare, "gaussianHalo", global) : global;
        }

        private float EdgeFlowFor()
            => OwnOrGlobal(HudFxCategory.Edges, "edgeFlow", HudConfig.FxEdgeFlowSpeed);

        private float HaloHazeFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = StyleFeatureOn(HudFxCategory.Glow, "customGlowOn", HudConfig.FxGlowOn);
            return tierA && glowOn
                ? Mathf.Clamp01(NewSdfOwnOrGlobal(HudFxCategory.Glow, "glowHaze", HudConfig.FxGlowHaze)) : 0f;
        }

        private float HaloBreathFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = StyleFeatureOn(HudFxCategory.Glow, "customGlowOn", HudConfig.FxGlowOn);
            bool auraOn = HaloFlowAuraFor() > 0.001f;
            bool breathOn = NewSdfFeatureOn(HudFxCategory.Glow, "customGlowBreathOn", HudConfig.FxGlowBreathOn);
            return tierA && (glowOn || auraOn) && breathOn
                ? Mathf.Clamp01(NewSdfOwnOrGlobal(HudFxCategory.Glow, "glowBreath", HudConfig.FxGlowBreath)) : 0f;
        }

        private float HaloUnevenFor()
        {
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool glowOn = StyleFeatureOn(HudFxCategory.Glow, "customGlowOn", HudConfig.FxGlowOn);
            bool auraOn = HaloFlowAuraFor() > 0.001f;
            bool unevenOn = NewSdfFeatureOn(HudFxCategory.Glow, "customGlowUnevenOn", HudConfig.FxGlowUnevenOn);
            return tierA && (glowOn || auraOn) && unevenOn
                ? Mathf.Clamp01(NewSdfOwnOrGlobal(HudFxCategory.Glow, "glowUneven", HudConfig.FxGlowUneven)) : 0f;
        }

        /// <summary>Footprint multiplier of the unevenness noise (0.25x..4x, 1 = classic).
        /// A modifier of the uneven feature — neutral whenever unevenness resolves off.</summary>
        private float HaloOrganicScaleFor()
        {
            if (HaloUnevenFor() <= 0.001f) return 1f;
            float v = NewSdfOwnOrGlobal(HudFxCategory.Glow, "glowOrganicScale", HudConfig.FxGlowOrganicScale);
            // A -1 sentinel can reach the Custom branch raw (ResetEffectsToGlobal writes it
            // into every _fxKeys slot) — anything below the slider floor means "neutral".
            return v < 0.2f ? 1f : Mathf.Clamp(v, 0.25f, 4f);
        }

        private float HaloFlowAuraFor()
        {
            // The flowing aura is an EDGES row in the registry (F9 draws it on Effects > Edges,
            // under the edge-energy master) even though it emits through the Glow envelope.
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool edgeOn = StyleFeatureOn(HudFxCategory.Edges, "customRippleOn", HudConfig.FxEdgeLightOn);
            bool auraOn = NewSdfFeatureOn(HudFxCategory.Edges, "customGlowFlowOn", HudConfig.FxGlowFlowAuraOn);
            return tierA && edgeOn && auraOn
                ? Mathf.Clamp(NewSdfOwnOrGlobal(HudFxCategory.Edges, "glowFlowAura", HudConfig.FxGlowFlowAura), 0f, 2f) : 0f;
        }

        private float FrostDepthFor()
            => Mathf.Clamp01(OwnOrGlobal(HudFxCategory.Glass, "frostDepth", HudConfig.FrostDepth));

        /// <summary>The per-element frost opt-out, resolved. It is the one companion bool with NO
        /// global gate (the global IS the strength slider), so an absent key means ON.</summary>
        private bool FrostFeatureOn()
            => Owns(HudFxCategory.Glass) && Def != null
                ? Def.GetBFor(LayoutBare, "customFrostOn", true) : true;

        private float ShineAmountFor()
        {
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            if (!tier || Def == null) return 0f;
            bool globalOn = HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            float global = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            if (Follows(HudFxCategory.Glass)) return globalOn ? Mathf.Clamp(global, 0f, 2f) : 0f;
            return Def.GetBFor(LayoutBare, "customShineOn", globalOn)
                ? Mathf.Clamp(Def.GetFFor(LayoutBare, "customShine", global), 0f, 2f) : 0f;
        }

        private float IridAmountFor()
        {
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            if (!tier || Def == null) return 0f;
            bool globalOn = HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            float global = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            if (Follows(HudFxCategory.Glass)) return globalOn ? Mathf.Clamp01(global) : 0f;
            return Def.GetBFor(LayoutBare, "customIridOn", globalOn)
                ? Mathf.Clamp01(Def.GetFFor(LayoutBare, "customIrid", global)) : 0f;
        }

        private float FrostAmountFor()
        {
            bool tier = HudBackdrop.Active && HudConfig.FxTierC != null && HudConfig.FxTierC.Value;
            if (!tier || Def == null) return 0f;
            float global = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f;
            if (Follows(HudFxCategory.Glass)) return Mathf.Clamp01(global);
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
            if (Follows(HudFxCategory.Glass)) return globalOn ? Mathf.Clamp01(global) : 0f;
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
            // The single whole-element "Follow F9 global style" switch that used to live here is
            // gone (Phase 3). It became a MASTER ROW above the style tab bar, plus one follow
            // checkbox per category page — see AddUnifiedEffectProps / CategoryFollowRow.
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
            into.Add(HudProp.Note(
                "Sizing, glass and effects: Effects tab (Theme / Glass / Edges / Glow pages)"));
            into.Add(HudProp.Note(
                "Each page has its own \"Follow global\" box - untick only what you want to own."));
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

        /// <summary>The source of ONE category in the slot the POPUP is editing (EditSlot), which
        /// is what every row-visibility test must ask — the render slot and the edit slot agree
        /// while F9 is open, but the editor is the authority on what it is about to write.
        /// <see cref="HudStyleFx.SourceOf"/> redirects Transitions to Base for us.</summary>
        private static HudFxSource EditSourceFor(HudElementDef d, HudFxCategory cat)
            => HudStyleFx.SourceOf(d, cat, EditSlot(d));

        /// <summary>True when the popup's target slot carries its OWN values for this category.
        /// A category with no follow state at all (Bloom / Alerts) answers true, because its rows
        /// are physically shared and are always rendered.</summary>
        private static bool EditOwns(HudElementDef d, HudFxCategory cat)
            => !HudStyleFx.IsFollowable(cat) || EditSourceFor(d, cat) == HudFxSource.Own;

        /// <summary>Is the companion feature that gates this row ON for this element? Following ⇒
        /// the global, Own ⇒ the element's own key with the global as its default — the same one
        /// missing-key rule Phase 0b gave every resolver. The companion's OWN category decides,
        /// which matters for the cross-category gates (the flowing aura is an Edges checkbox that
        /// opens a Glow envelope).</summary>
        private bool FxFeatureOn(HudElementDef d, string onParamKey)
        {
            if (string.IsNullOrEmpty(onParamKey)) return true;
            bool gv = FxCompanionDefault(onParamKey);
            if (d == null) return gv;
            var comp = HudStyleFx.FindByParam(onParamKey);
            var cat = comp != null ? comp.Category : HudFxCategory.Glass;
            if (!EditOwns(d, cat)) return gv;
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

        /// <summary>Whether a row is drawn at all on this element's page, before its section gate.
        ///
        /// PHASE 3: a FOLLOWING category hides its per-element CONTROLS (requirement 3: "following a
        /// category ⇒ its controls are hidden") and shows its follow checkbox plus a summary line
        /// instead. Two classes of row are NOT controls and stay put in both states:
        ///
        ///   • SHARED-ONLY rows. Plan §3.6 is explicit — "always rendered, greyed, showing the live
        ///     global … never silently absent". They are read-only values, not controls, and they
        ///     are the ONLY place several of them appear: backdrop darkening, frost tint, the blur
        ///     resolution and the re-blur rate are GLASS rows, and 16 of Stationeers Blue's 24
        ///     elements follow Glass — hiding them there put FlorpyDorp's original complaint
        ///     ("no knob to adjust them") straight back where it started.
        ///   • StateIndependent null-global rows (the box-end fades, the trapezoid insets, the
        ///     element font scale) sit OUTSIDE the follow system — the renderer reads them in both
        ///     states, so hiding them would be a regression, not a simplification.</summary>
        private bool FxRowVisible(HudElementDef d, HudStyleFxDef def)
        {
            if (def == null || !FxApplies(def)) return false;
            if (def.SharedOnly) return def.MasterOn;       // exactly F9's own per-row master gate
            if (!def.HasGlobal && def.StateIndependent) return true;
            if (!EditOwns(d, def.Category)) return false;
            if (!def.HasGlobal) return true;
            return FxFeatureOn(d, def.OnParamKey);
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

        /// <summary>Does this element own ANY row of a category — i.e. does the category earn a
        /// page at all? Asked before the follow state, because a FOLLOWING page still has to exist
        /// to carry its checkbox and its summary (a drawn line, by contrast, has no Theme page in
        /// either state because no Surface row applies to it).</summary>
        private bool FxCategoryApplies(HudFxCategory cat)
        {
            var all = HudStyleFx.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Category == cat && FxApplies(all[i])) return true;
            return false;
        }

        /// <summary>Does this element have at least one row of a category that the follow state
        /// actually GOVERNS? A page whose only applicable rows are SHARED read-only values or
        /// StateIndependent geometry (a text-only element's Theme page, which is just the font
        /// scale and the shared HUD font scale) gets no follow checkbox — the box would be a
        /// control that provably cannot change anything.</summary>
        private bool FxCategoryHasFollowableRows(HudFxCategory cat)
        {
            var all = HudStyleFx.All;
            for (int i = 0; i < all.Length; i++)
            {
                var def = all[i];
                if (def == null || def.Category != cat || !FxApplies(def)) continue;
                if (def.SharedOnly) continue;
                if (!def.HasGlobal && def.StateIndependent) continue;
                return true;
            }
            return false;
        }

        /// <summary>The F9 sub-tab a category's jump button points at: the first row in the
        /// category that actually has a home there. Derived, never hand-typed.</summary>
        private static void AddCategoryJump(List<HudProp> into, HudFxCategory cat)
        {
            var all = HudStyleFx.All;
            for (int i = 0; i < all.Length; i++)
            {
                string tab, subTabId, subTabName;
                if (all[i] == null || all[i].Category != cat) continue;
                if (!HudStyleFx.F9Home(all[i], out tab, out subTabId, out subTabName)) continue;
                AddJump(into, tab, subTabId, subTabName, "cat" + (int)cat);
                return;
            }
        }

        /// <summary>The per-category FOLLOW checkbox that heads every followable page — plan §3.3
        /// requirement 3, and the row that replaced the single whole-element switch.
        ///
        /// Unticking SEEDS the category from the values the element is currently resolving BEFORE
        /// the source flips, so not a single pixel changes (the Phase 0a value-preserving contract,
        /// now per category). Re-ticking only flips the bits: the seeded keys stay on disk, dormant
        /// and unread, which is FlorpyDorp's decision 5 — nothing is lost, and unticking again
        /// re-seeds from whatever is on screen at that moment.</summary>
        private HudProp CategoryFollowRow(HudElementDef d, HudFxCategory cat)
        {
            string name = HudStyleFx.CategoryName(cat);
            return WithId(WithHelp(HudProp.Bool("Follow global " + name,
                () => !EditOwns(d, cat),
                v => SetCategoryFollow(cat, v)),
                "Ticked: this element takes the F9 " + name + " globals. Unticked: it gets its own "
                + "copy of every " + name + " value, seeded from exactly what it shows now."),
                "styleSrc" + (int)cat);
        }

        /// <summary>One page of the style tab bar: the category's follow checkbox, then either its
        /// summary line (following) or every registry row of that category in table order (own).
        /// Appends nothing when the element has no surface the category can touch.</summary>
        private void AddStylePage(List<HudProp> pages, HudElementDef d, string caption,
            HudFxCategory cat)
        {
            if (!FxCategoryApplies(cat)) return;

            var page = new List<HudProp>();
            bool followable = HudStyleFx.IsFollowable(cat) && FxCategoryHasFollowableRows(cat);
            bool own = EditOwns(d, cat);
            if (followable)
            {
                page.Add(CategoryFollowRow(d, cat));
                if (!own)
                {
                    page.Add(HudProp.Note("    This element's " + HudStyleFx.CategoryName(cat)
                        + " follows the F9 globals."));
                    page.Add(HudProp.Note(
                        "    Untick to edit it here - the values open at exactly what you see now."));
                    AddCategoryJump(page, cat);
                }
            }

            var all = HudStyleFx.All;
            string lastSection = null;
            HudStyleFxDef runDef = null;     // the open run of shared rows, if any
            bool runLocal = false;           // ... and whether it held a machine-local one

            for (int i = 0; i < all.Length; i++)
            {
                HudStyleFxDef def = all[i];
                if (def == null || def.Category != cat) continue;
                if (!FxApplies(def)) continue;
                if (!FxSectionOpen(d, cat, def.Section)) continue;
                if (!FxRowVisible(d, def)) continue;
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
            if (page.Count == 0) return;
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
            // A row with NO global is per-element or nothing — and the two classes of those behave
            // DIFFERENTLY under the follow system, so the caption has to say which. StateIndependent
            // rows (the box-end fades, the trapezoid insets, the element font scale) are element
            // geometry and stay editable in both states; the rest (energy smoothness, the portrait
            // ring pair, the frost opt-out) are neutral while the category follows and therefore
            // only appear once it does not.
            if (!def.HasGlobal)
                label = label + (def.StateIndependent
                    ? "  (per-element only)"
                    : "  (per-element only; shown when not following)");

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

            // THE MASTER ROW (Phase 3). It is a convenience over the five per-page checkboxes, not
            // a separate piece of state: ticked when ALL five follow, unticked when none do, and
            // it says "(mixed)" in between rather than lying in either direction. Clicking it drives
            // every category through the SAME seed-on-unfollow / dormant-on-refollow semantics as
            // the page checkboxes, so the two can never disagree.
            //
            // It is deliberately EXCLUDED from the per-tier seed walk (SeedSkip): its getter
            // collapses a mixed element to `false`, and replaying that into a fresh Bare fork would
            // unfollow all five categories on an element that only owned one. The five page
            // checkboxes carry the real per-category state across the fork.
            bool allFollow = HudStyleFx.AllFollow(d, EditSlot(d));
            bool noneFollow = HudStyleFx.NoneFollow(d, EditSlot(d));
            into.Add(WithId(WithHelp(HudProp.Bool("Follow F9 global style (theme + effects)",
                () => HudStyleFx.AllFollow(d, EditSlot(d)),
                v => { for (int i = 0; i < HudStyleFx.Followable.Length; i++)
                           SetCategoryFollow(HudStyleFx.Followable[i], v); }),
                "Every style family at once. Each page below can be followed or owned on its own."),
                "styleSourceAll"));
            if (!allFollow && !noneFollow)
                into.Add(HudProp.Note("    (mixed) - some families follow the globals, some do not."));

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

        /// <summary>The Transitions page (Phase 3, FlorpyDorp's decision 3: the Inherit/On/Off
        /// dropdowns are RETIRED, with no "Advanced" escape hatch).
        ///
        /// Following ⇒ the follow checkbox, a summary line and a jump. Nothing else — a following
        /// element's motion is entirely the F9 globals, so there is nothing here to set.
        ///
        /// Own ⇒ seven rows that look exactly like F9's: a plain checkbox and a strength slider
        /// each. No third state is offered, because once the category is unfollowed each transition
        /// is simply on or off FOR THIS ELEMENT.
        ///
        /// WHY THIS DOES NOT LOSE THE GRANULARITY THE PLAN PREDICTED (§3.7): storage is still
        /// HudTransitionFx's own per-effect keys, and an effect the author has not touched stores
        /// NOTHING — its mode stays absent, i.e. Inherit, so it keeps tracking the global master and
        /// the global strength for ever. The checkbox and the slider simply DISPLAY those live
        /// globals until the row is touched. So the plan's worry — "an element that was Inherit on 6
        /// and Off on 1 ends up with 6 effects frozen at today's globals" — does not happen: exactly
        /// the one row that was ever touched is frozen, and it was already frozen before.</summary>
        private List<HudProp> BuildTransitionsPage(HudElementDef d)
        {
            var page = new List<HudProp>();
            page.Add(CategoryFollowRow(d, HudFxCategory.Transitions));
            if (!EditOwns(d, HudFxCategory.Transitions))
            {
                page.Add(HudProp.Note(
                    "    This element's motion follows the F9 globals (Effects > Transitions)."));
                page.Add(HudProp.Note(
                    "    Untick to switch individual transitions on or off for this element."));
                AddJump(page, HudStyleFx.TabEffects, HudStyleFx.SubTabTransitions, "Transitions",
                    "transitions");
                return page;
            }

            page.Add(HudProp.Header("Motion & power transitions"));
            // One row per registry effect, driven by HudTransitionFx.All, so this inspector and the
            // F9 global menu cannot present different effects or different labels.
            for (int i = 0; i < HudTransitionFx.All.Length; i++)
                AddTransitionRows(page, d, HudTransitionFx.All[i]);
            page.Add(HudProp.Note("  A global master that is OFF wins over every element."));
            page.Add(HudProp.Note("  Transitions are shared by the suit and bare layouts."));
            page.Add(HudProp.Note("  A row you never touch keeps tracking the global, live."));
            AddJump(page, HudStyleFx.TabEffects, HudStyleFx.SubTabTransitions, "Transitions",
                "transitions");
            return page;
        }

        /// <summary>What the checkbox for one effect SHOWS. On/Off are the element's own stored
        /// decision; an untouched (Inherit) row displays the live global master, so the page reads
        /// as a faithful copy of F9 before you touch anything — and stays a live one afterwards for
        /// every row you leave alone. This is a DISPLAY-time read, never a stored write.</summary>
        private static bool TransitionRowOn(HudElementDef d, HudTransitionFxDef fx)
        {
            var m = HudTransitionFx.ModeOf(d, fx, false);
            if (m == HudFxMode.On) return true;
            if (m == HudFxMode.Off) return false;
            return fx.GlobalOn;
        }

        /// <summary>Per-element rows for ONE registry transition effect: a checkbox and a strength,
        /// styled exactly like F9's pair. Ticking writes <see cref="HudFxMode.On"/>, unticking
        /// writes <see cref="HudFxMode.Off"/>; dragging the strength promotes an untouched row to On
        /// (HudTransitionFx.SetAmount's own rule — otherwise the slider would visibly do nothing).
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
            into.Add(WithId(WithHelp(HudProp.Bool(fx.Label,
                () => TransitionRowOn(d, fx),
                v => HudTransitionFx.SetMode(d, fx, false, v ? HudFxMode.On : HudFxMode.Off)),
                fx.Tip), fx.ModeKey));
            into.Add(WithId(HudProp.F("  strength", () => HudTransitionFx.AmountOf(d, fx, false),
                v => HudTransitionFx.SetAmount(d, fx, false, v), 0f, 2f), fx.AmtKey));

            HudFxMode mode = HudTransitionFx.ModeOf(d, fx, false);
            if (mode == HudFxMode.Inherit)
                into.Add(HudProp.Note(fx.GlobalOn
                    ? "    still tracking the global (" + fx.GlobalAmt.ToString("0.00")
                      + ") - touch a row to freeze it here"
                    : "    still tracking the global (master OFF) - touch a row to freeze it here"));
            if (!fx.GlobalOn && mode == HudFxMode.On)
                into.Add(HudProp.Note("    Global master is OFF - ticking this changes nothing."));
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

        /// <summary>Attach a tooltip to a prop whose factory takes none (Bool / Float).</summary>
        private static HudProp WithHelp(HudProp p, string help)
        {
            if (p != null) p.Help = help;
            return p;
        }

        // ---- per-tier fork seeding ----------------------------------------------------------

        /// <summary>Rows <see cref="SeedSlotFromBase"/> must NOT round-trip: setters with side
        /// effects or identity semantics. The geometry rows fork the LAYOUT (a separate feature
        /// with its own toggle); "tiers" would call ClearVisualBareOverrides and wipe the very fork
        /// being seeded; "zOrder" is document order, not look; the transition rows are deliberately
        /// shared by every tier (see AddTransitionRows) and re-writing them would churn the base.
        /// Everything else is safe: a row that is not actually tier-aware simply writes the same
        /// value back to the same base storage — a no-op round trip.
        ///
        /// "styleSourceAll" is the Phase 3 MASTER follow row and must be skipped for a different
        /// reason: it is a derived convenience, not state. Its getter reports `false` for a MIXED
        /// element, and replaying that into a fresh fork would unfollow all five categories on an
        /// element that only owned one. The five per-category checkboxes ("styleSrc0".."styleSrc6")
        /// are NOT skipped — they carry the real per-slot follow state across the fork, which is
        /// what makes a Bare fork able to follow a family the Suited base owns.</summary>
        private static readonly HashSet<string> SeedSkip = BuildSeedSkip();

        private static HashSet<string> BuildSeedSkip()
        {
            var s = new HashSet<string>(System.StringComparer.Ordinal)
            {
                "elAnchor", "elX", "elY", "elW", "elH", "elWPct", "elHPct", "zOrder", "tiers",
                "styleSourceAll",
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

        // ================= PER-CATEGORY SOURCE WRITES + SEEDING (Phase 3) ====================

        /// <summary>Write one slot's packed source word, keeping the pre-Phase-3 field in step.
        ///
        /// The legacy "styleSource" write is DOWNGRADE COMPAT and nothing else (plan §4.2): a
        /// 0.9.2.x build reads only that field, so a profile saved here must still describe itself
        /// in its vocabulary — 1 when every category follows, 2 otherwise, which is the closest
        /// honest two-state summary of a five-state word. Phase 5 drops the write.</summary>
        private static void WriteSourceBits(HudElementDef d, HudStyleSlot slot, int packed)
        {
            if (d == null) return;
            d.SetIFor(slot, HudStyleFx.SourceParamKey, packed);
            d.SetIFor(slot, HudStyleFx.LegacySourceParamKey,
                HudStyleFx.AllFollow(packed) ? StyleGlobal : StyleCustom);
        }

        /// <summary>Tick or untick ONE category's follow box — the whole of requirement 3 and
        /// requirement 4, in five lines.
        ///
        /// UNTICKING SEEDS FIRST, while the old source is still live, so every row on the page
        /// opens at exactly the value the element is already rendering and not a single pixel moves
        /// (Phase 0a's value-preserving contract, now per category).
        ///
        /// TICKING only flips the bits. The seeded keys stay on disk, dormant and unread — that is
        /// FlorpyDorp's decision 5: nothing is deleted, and unticking again re-seeds from whatever
        /// is on screen at that moment rather than resurrecting a stale snapshot.
        ///
        /// Per SLOT: separating while the BARE tab is up writes the bare fork, not the shared base
        /// every tier inherits (the silent leak the Wave C audit found) — EXCEPT Transitions, whose
        /// bits live on Base whatever tab is up, because the rows it governs do
        /// (<see cref="HudStyleFx.SlotFor"/>). Writing it per-slot desynced the checkbox from its
        /// own rows on a bare-forked element: untick a transition and the box sprang back.</summary>
        internal void SetCategoryFollow(HudFxCategory cat, bool follow)
        {
            var d = Def;
            if (d == null || !HudStyleFx.IsFollowable(cat)) return;
            var slot = HudStyleFx.SlotFor(cat, EditSlot(d));
            // Seed only on a REAL Global -> Own transition. For the four steady families re-seeding
            // an already-Own category is idempotent (every write is that category's own value read
            // back), so the gate changes nothing there — but SeedTransitions is NOT idempotent: it
            // writes Inherit over stored On/Off by design, and the per-tier fork seed replays every
            // checkbox at its current value, so an unguarded call would wipe an already-separated
            // element's motion design just because the author ticked "Separate BARE style".
            if (!follow && HudStyleFx.SourceOf(d, cat, slot) != HudFxSource.Own)
                SeedCategory(d, slot, cat);
            int packed = HudStyleFx.WithSource(HudStyleFx.PackedOf(d, slot), cat,
                follow ? HudFxSource.Global : HudFxSource.Own);
            WriteSourceBits(d, slot, packed);
        }

        /// <summary>Freeze ONE category's currently resolved values into the element, by walking
        /// <see cref="HudStyleFx.All"/> — the registry loop that replaced ~40 hand-written writes.
        /// A knob added to the table is seeded for free; there is no second list to forget.
        ///
        /// VALUE-PRESERVING BY CONSTRUCTION and it must stay that way: every write in
        /// <see cref="SeedFxRow"/> is a live resolver read taken while the OLD source is still
        /// active, so seeding a category that is already showing these values writes them down
        /// again. A literal default sneaked in there becomes a visible jump on every unfollow.
        ///
        /// TRANSITIONS seed one thing and one thing only — see <see cref="SeedTransitions"/>. They
        /// deliberately do NOT write a mode or a strength for an untouched effect: the key stays
        /// ABSENT so the row goes on tracking the global master and strength live, which is how
        /// this phase avoids the granularity loss plan §3.7 predicted.</summary>
        private void SeedCategory(HudElementDef d, HudStyleSlot slot, HudFxCategory cat)
        {
            if (d == null) return;
            if (cat == HudFxCategory.Transitions) { SeedTransitions(d); return; }
            // The snapshot defines the TARGET SLOT's style, so force the slot-aware resolvers onto
            // that slot. Restored in finally so a mid-frame flip can't strand the render slot.
            var prevLayoutSlot = LayoutSlot;
            LayoutSlot = slot;
            try
            {
                var all = HudStyleFx.All;
                for (int i = 0; i < all.Length; i++)
                {
                    var def = all[i];
                    if (def == null || def.Category != cat) continue;
                    // The capability predicate is the gate that used to be hand-written ("only a
                    // Polyline gets edgeLight", "only border-only chrome gets ringGlow"): an
                    // element never gains a key for a surface it does not have.
                    if (!FxApplies(def)) continue;
                    try { SeedFxRow(d, slot, def); }
                    catch (System.Exception e)
                    {
                        Core.UIALog.Warn("HudElementView: seeding '" + (def.Key ?? "?") + "' on '"
                            + (d.Id ?? "?") + "' failed (" + e.Message + ") — it keeps following.");
                    }
                }
                d.Set("followGlobal", null);            // extinct legacy flag — never re-written
                d.SetBFor(slot, "customStyleReady", true);
            }
            finally { LayoutSlot = prevLayoutSlot; }
        }

        /// <summary>The Transitions half of the unfollow seed, and it is the SAME contract as every
        /// other category: freeze what the element is CURRENTLY RESOLVING.
        ///
        /// While Transitions follows, <c>HudTransitionFx.ModeOf</c> returns Inherit for all seven
        /// effects whatever is stored — so Inherit IS the value being rendered, and Inherit is what
        /// the seed must write down. Without this, unfollowing REVEALED a dormant stored On/Off and
        /// changed a pixel, which fails the gating test. (13 elements in the shipped Stationeers
        /// Blue carry <c>fxCollapseMode=2</c>, so this was not hypothetical.)
        ///
        /// Yes, this overwrites a dormant motion design — exactly as re-seeding overwrites the
        /// dormant floats in every other category. That is decision 5's approved unfollow rule, not
        /// an exception to it.
        ///
        /// Writing Inherit REMOVES the mode key rather than storing a 0, so the row goes straight
        /// back to tracking the global live, and the profile does not grow. Legacy "b_" overrides
        /// are cleared too, but only where one is actually stored: the renderer still honours a bare
        /// override (<c>Resolve(def, key, LayoutBare)</c>), so leaving one behind would be the same
        /// reveal one tier over.</summary>
        private static void SeedTransitions(HudElementDef d)
        {
            if (d == null) return;
            var all = HudTransitionFx.All;
            for (int i = 0; i < all.Length; i++)
            {
                var fx = all[i];
                if (fx == null) continue;
                if (HudTransitionFx.RawModeOf(d, fx, HudStyleSlot.Base) != HudFxMode.Inherit)
                    HudTransitionFx.SetMode(d, fx, false, HudFxMode.Inherit);
                if (d.HasSlotOverride(HudStyleSlot.Bare, fx.ModeKey)
                    || d.HasSlotOverride(HudStyleSlot.Bare, fx.LegacyKey))
                    HudTransitionFx.SetMode(d, fx, true, HudFxMode.Inherit);
            }
        }

        /// <summary>Freeze ONE registry row's currently resolved value. The special cases below are
        /// exactly the rows whose storage is not "a param holding the resolved number": the three
        /// first-class def FIELDS, the two keys whose stored form is a live-tracking SENTINEL
        /// (cornerStyle 0, an empty colour ref), and the four null-global keys. Everything else
        /// falls through to the generic branch, which IS plan §3.2's one rule — so a row added to
        /// the table is seeded correctly without touching this function.</summary>
        private void SeedFxRow(HudElementDef d, HudStyleSlot slot, HudStyleFxDef def)
        {
            if (d == null || def == null || def.SharedOnly) return;
            switch (def.Key)
            {
                // ---- first-class HudElementDef fields ----
                case "cornerRadius":
                    d.SetRTLFor(slot, Radius(d.RTLFor(slot)));
                    d.SetRTRFor(slot, Radius(d.RTRFor(slot)));
                    d.SetRBRFor(slot, Radius(d.RBRFor(slot)));
                    d.SetRBLFor(slot, Radius(d.RBLFor(slot)));
                    return;
                case "borderWidth": d.SetBorderWidthFor(slot, BorderWidthFor()); return;
                // The element font scale MULTIPLIES the shared one and is read in both states, so
                // it never follows; writing it back merely completes the slot's copy.
                case "elFontScale": d.SetFontScaleFor(slot, d.FontScaleFor(slot)); return;

                // ---- stored form is a live-tracking sentinel, so copy it VERBATIM ----
                // 0 = "Follow global" is cornerStyle's own analogue of a palette-name ColorRef, and
                // CornerCutFor maps it to -1 in both states. Freezing it to the global's concrete
                // value would silently stop a separated element tracking the F9 combo.
                case "cornerStyle":
                    d.SetIFor(slot, "cornerStyle",
                        Mathf.Clamp(d.GetIFor(slot, "cornerStyle", CornerStyleFollow), 0, 2));
                    return;
                // Empty = "derive from the rim"; a palette name must keep live-tracking F9. Same
                // deliberate non-snapshot rule the Fill/Border/Text refs follow.
                case "ringGlowColor":
                {
                    string ringRef = d.GetSFor(slot, "ringGlowColor", null);
                    d.SetSFor(slot, "ringGlowColor", string.IsNullOrEmpty(ringRef) ? null : ringRef);
                    return;
                }

                // ---- resolvers that are not a plain own-or-global read ----
                case "feather": d.SetFFor(slot, "feather", EffectiveFeatherFor()); return;
                case "sheen": d.SetFFor(slot, "sheen", GlassSheenFor()); return;
                // Own stores the FINAL visible edge-light strength, global Tier-A boost included,
                // and then stops tracking it (see GlassEdgeFor).
                case "spec": d.SetFFor(slot, "spec", GlassEdgeFor()); return;
                case "squircle": d.SetFFor(slot, "squircle", SdfSquircleFor()); return;
                case "gaussianHalo": d.SetBFor(slot, "gaussianHalo", SdfGaussianFor()); return;

                // ---- the four null-global keys ----
                // A following ring derives its halo from the panel recipe while an owning one
                // stores 0 by default, so separation used to DELETE the ring's halo. Freeze what it
                // is actually showing.
                case "ringGlow": d.SetFFor(slot, "ringGlow", Mathf.Clamp(RingGlowFor(), 0f, 2f)); return;
                case "customFrostOn": d.SetBFor(slot, "customFrostOn", FrostFeatureOn()); return;
                case "rippleSmooth":
                    d.SetFFor(slot, "rippleSmooth",
                        Owns(HudFxCategory.Edges) ? d.GetFFor(slot, "rippleSmooth", 0f) : 0f);
                    return;
            }

            string key = def.ParamKey;
            if (string.IsNullOrEmpty(key)) return;

            // Element GEOMETRY (box-end fades, trapezoid insets): un-gated by the style source, so
            // writing them back is a value-preserving no-op — but it COMPLETES the slot's copy,
            // which is what stops a later base edit leaking into a per-tier fork (HudElementDef's
            // copy-on-write only protects keys that already exist).
            if (def.StateIndependent)
            {
                d.SetFFor(slot, key, d.GetFFor(slot, key, 0f));
                return;
            }

            // THE GENERIC BRANCH = plan §3.2's one rule. Following ⇒ the global; Own ⇒ the stored
            // value with the global as its default. Colour refs are deliberately never snapshotted.
            switch (def.Kind)
            {
                case HudFxKind.Bool:
                    d.SetBFor(slot, key, Owns(def.Category)
                        ? d.GetBFor(slot, key, def.GlobalBool) : def.GlobalBool);
                    break;
                case HudFxKind.Color:
                    break;
                case HudFxKind.Int:
                case HudFxKind.Combo:
                    d.SetIFor(slot, key, Owns(def.Category)
                        ? d.GetIFor(slot, key, def.GlobalInt) : def.GlobalInt);
                    break;
                default:
                    d.SetFFor(slot, key, Owns(def.Category)
                        ? d.GetFFor(slot, key, def.GlobalFloat) : def.GlobalFloat);
                    break;
            }
        }

        /// <summary>Freeze every STEADY-STATE category — the snapshot the F9 BULK buttons take.
        ///
        /// MOTION IS DELIBERATELY EXCLUDED, and this is the 2026-07-19 bulk-ops/motion independence
        /// rule restated: a bulk style operation must never rewrite an element's transitions. It
        /// used to destroy an authored per-effect strength and pin an element's dissolve to Off;
        /// under Phase 3 the equivalent mistake would be seeding Inherit over an explicit Off that
        /// the author set on purpose (13 elements in the shipped Stationeers Blue carry one).
        ///
        /// The per-element MASTER checkbox is the opposite case and does NOT come through here: it
        /// loops <see cref="SetCategoryFollow"/> over all five categories, Transitions included,
        /// because that is an explicit local action on one element with an immediately visible
        /// result.
        ///
        /// Re-following does NOT delete the stored keys (decision 5): they stay dormant and are
        /// overwritten by the next seed.</summary>
        private void SeedCustomStyleFromEffective(HudElementDef d, HudStyleSlot slot)
        {
            if (d == null) return;
            for (int i = 0; i < HudStyleFx.Followable.Length; i++)
            {
                var cat = HudStyleFx.Followable[i];
                if (cat == HudFxCategory.Transitions) continue;
                SeedCategory(d, slot, cat);
            }
        }

        /// <summary>The packed word a BULK style operation should write: all four steady-state
        /// families moved to <paramref name="followGlobal"/>, and the element's existing Transitions
        /// bit PRESERVED (see <see cref="SeedCustomStyleFromEffective"/> for why).</summary>
        private static int BulkPackedFor(HudElementDef d, bool followGlobal)
            => HudStyleFx.WithSource(
                followGlobal ? HudStyleFx.AllGlobalPacked : HudStyleFx.AllOwnPacked,
                HudFxCategory.Transitions, HudStyleMigration.TransitionSourceForBase(d));

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
            // Phase 3: the bulk buttons move the four STEADY-STATE families through the same
            // seed-first / dormant-on-refollow semantics as the per-page checkboxes, and leave
            // motion exactly where the author put it — BulkPackedFor preserves the Transitions bit.
            WriteSourceBits(Def, slot, BulkPackedFor(Def, followGlobal));
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
                WriteSourceBits(d, HudStyleSlot.Base, BulkPackedFor(d, true));
                return;
            }

            int legacySource = Mathf.Clamp(d.GetI(HudStyleFx.LegacySourceParamKey, StyleLegacy),
                StyleLegacy, StyleCustom);
            if (!forceCustomSnapshot && legacySource != StyleCustom && d.GetB("customStyleReady", false))
            {
                WriteSourceBits(d, HudStyleSlot.Base, BulkPackedFor(d, false));
                return;
            }
            // Already Own everywhere it matters — leave the packed word alone rather than
            // flattening a MIXED element that only wanted one family separated. (Unreachable
            // today: every caller passes forceCustomSnapshot when followGlobal is false.)
            if (!forceCustomSnapshot && legacySource == StyleCustom) return;

            // PHASE 3: the ~40 hand-written writes that used to sit here are a REGISTRY LOOP. The
            // per-key rules are unchanged (SnapshotDefRow still honours a stored 0's full legacy
            // semantics, which HudStyleMigration depends on) — what changed is that the table drives
            // them, so a knob added to HudStyleFx can no longer be silently missed by this path
            // while the live seed picks it up. That divergence was real: Phase 1's audit found this
            // function missing cornerStyle, edgeFadeX/Y and the portrait ring pair, so "Make flat"
            // and a legacy-profile migration could still delete a ring's halo after Phase 0a had
            // fixed the interactive path. All five are covered now, by construction.
            var all = HudStyleFx.All;
            for (int i = 0; i < all.Length; i++)
            {
                var def = all[i];
                if (def == null || def.SharedOnly) continue;
                if (!HudStyleFx.IsFollowable(def.Category)) continue;   // Bloom / Alerts own nothing
                if (def.Category == HudFxCategory.Transitions) continue;
                try { SnapshotDefRow(d, def, legacySource); }
                catch (System.Exception e)
                {
                    Core.UIALog.Warn("HudElementView: def-only snapshot of '" + (def.Key ?? "?")
                        + "' on '" + (d.Id ?? "?") + "' failed (" + e.Message + ").");
                }
            }
            // Motion deliberately NOT snapshotted — same reasoning as SeedCustomStyleFromEffective:
            // a bulk style flip must not rewrite an element's transitions, and BulkPackedFor keeps
            // its follow bit exactly where the author left it.

            d.Set("followGlobal", null); // extinct legacy flag — never re-written
            d.SetB("customStyleReady", true);
            WriteSourceBits(d, HudStyleSlot.Base, BulkPackedFor(d, false));
        }

        /// <summary>Border-only chrome, answered from a raw def (no live view) — the same static-map
        /// precedent <see cref="SupportsPanelAppearanceFor"/> sets for the def-only call sites. The
        /// LIVE path must still go through the virtual <see cref="SupportsBorderOnlyChrome"/>.</summary>
        internal static bool SupportsBorderOnlyChromeFor(HudElementDef d)
            => d != null && (d.Type == HudElementType.Portrait || d.Type == HudElementType.BodyDoll);

        /// <summary>Snapshot ONE registry row into a raw def. Each case is the rule that key
        /// already had — including the legacy (stored 0) semantics: the followGlobal colour/glass
        /// gate, the -1 sentinels and the extinct fx* multipliers. The DEFAULT branch is the
        /// conservative one (SnapshotFloat's -1 sentinel / the plain global for a bool), so a row
        /// added to the table later behaves sanely here without a code change.
        ///
        /// THE SOURCE IS RESOLVED PER ROW, from the row's own CATEGORY. Deriving one element-wide
        /// Global/Custom from the legacy field was a real bug on a MIXED element: an element that
        /// had unfollowed only Glow read as "Custom" for every family, so the Surface rows took the
        /// Custom branch even though Surface was FOLLOWING — which wrote <c>ringGlow</c> from the
        /// stored 0 (deleting a portrait's halo, the exact Phase-0a defect) and froze <c>spec</c>
        /// without the Tier-A edge-light boost it was actually rendering. The legacy state (a stored
        /// 0) is the one case that stays element-wide, because it IS an element-wide condition:
        /// there are no per-category bits on a profile that predates them.</summary>
        private static void SnapshotDefRow(HudElementDef d, HudStyleFxDef def, int legacySource)
        {
            int source = legacySource == StyleLegacy
                ? StyleLegacy
                : (HudStyleFx.SourceOf(d, def.Category, HudStyleSlot.Base) == HudFxSource.Own
                    ? StyleCustom : StyleGlobal);
            bool sourceGlobal = source == StyleGlobal;
            bool sourceCustom = source == StyleCustom;
            // Colour refs are deliberately left untouched: they resolve identically in both
            // coherent states (palette names live-track, hex stands alone). Legacy's forced
            // palette is handled by HudStyleMigration, which rewrites the refs to palette NAMES
            // so the element keeps tracking the F9 palette it was showing.
            bool followsColours = sourceGlobal || (!sourceCustom && d.GetB("followGlobal", false));

            switch (def.Key)
            {
                // ---- first-class fields ----
                case "borderWidth":
                {
                    float g = HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f;
                    d.BorderWidth = sourceGlobal || d.BorderWidth < 0f ? g : d.BorderWidth;
                    return;
                }
                case "cornerRadius":
                {
                    float g = HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f;
                    d.RTL = sourceGlobal || d.RTL < 0f ? g : d.RTL;
                    d.RTR = sourceGlobal || d.RTR < 0f ? g : d.RTR;
                    d.RBR = sourceGlobal || d.RBR < 0f ? g : d.RBR;
                    d.RBL = sourceGlobal || d.RBL < 0f ? g : d.RBL;
                    return;
                }
                case "elFontScale": return;   // the element's own multiplier: never follows

                // ---- ADDED IN PHASE 3 (Phase 1 audit, discrepancy 1: this path had none of them
                //      even after Phase 0a added them to the interactive seed) ----
                case "cornerStyle":
                    d.SetI("cornerStyle",
                        Mathf.Clamp(d.GetI("cornerStyle", CornerStyleFollow), 0, 2));
                    return;
                case "ringGlow":
                {
                    if (!SupportsBorderOnlyChromeFor(d)) return;
                    if (sourceCustom) { d.SetF("ringGlow", Mathf.Max(0f, d.GetF("ringGlow", 0f))); return; }
                    // Following: the ring derives its halo from the panel recipe (see RingGlowFor),
                    // so freeze THAT — separating a portrait used to delete its halo entirely.
                    bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
                    bool glowOn = tierA && (HudConfig.FxGlowOn != null && HudConfig.FxGlowOn.Value);
                    float glowNow = SnapshotFloat(d, source, "glow",
                        HudConfig.FxGlow != null ? HudConfig.FxGlow.Value : 0f);
                    d.SetF("ringGlow", glowOn ? Mathf.Clamp(Mathf.Max(0f, glowNow), 0f, 2f) : 0f);
                    return;
                }
                case "ringGlowColor":
                {
                    if (!SupportsBorderOnlyChromeFor(d)) return;
                    string r = d.GetS("ringGlowColor", null);
                    d.Set("ringGlowColor", string.IsNullOrEmpty(r) ? null : r);
                    return;
                }

                // ---- the glass/edge pair with the legacy followGlobal gate ----
                case "sheen":
                {
                    float g = HudConfig.GlassSheen != null ? HudConfig.GlassSheen.Value : 0f;
                    float own = d.GetF("sheen", -1f);
                    d.SetF("sheen", sourceGlobal || followsColours || own < 0f ? g : own);
                    return;
                }
                case "spec":
                {
                    float g = HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f;
                    float own = d.GetF("spec", -1f);
                    float resolved = sourceGlobal || followsColours || own < 0f ? g : own;
                    bool optedOut = !followsColours && own == 0f;
                    if (!sourceCustom && !optedOut
                        && HudConfig.FxTierA != null && HudConfig.FxTierA.Value
                        && HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value
                        && HudConfig.FxEdgeLight != null && HudConfig.FxEdgeLight.Value > 0f)
                        resolved = Mathf.Clamp01(resolved + HudConfig.FxEdgeLight.Value * 0.45f);
                    d.SetF("spec", Mathf.Clamp01(resolved));
                    return;
                }
                case "squircle":
                {
                    float g = HudConfig.SdfSquircle != null ? HudConfig.SdfSquircle.Value : 2f;
                    float own = d.GetF("squircle", -1f);   // this sentinel is "< 2", not "< 0"
                    d.SetF("squircle", sourceGlobal || own < 2f ? g : Mathf.Clamp(own, 2f, 8f));
                    return;
                }

                // ---- the SDF family: a stored value wins even when negative (no -1 sentinel) ----
                case "glowExtraDiffuse":
                case "glowHaze":
                case "glowBreath":
                case "glowUneven":
                case "glowOrganicScale":
                case "glowFlowAura":
                    d.SetF(def.ParamKey,
                        SnapshotNewFloat(d, source, def.ParamKey, def.GlobalFloat));
                    return;

                // ---- the four animated-glass pairs, with their extinct fx* multipliers ----
                case "shine":
                    d.SetF("customShine", sourceCustom
                        ? Mathf.Clamp(d.GetF("customShine", def.GlobalFloat), 0f, 2f)
                        : Mathf.Clamp(def.GlobalFloat * (sourceGlobal ? 1f
                            : Mathf.Clamp01(d.GetF("fxShineAmt", 1f))), 0f, 2f));
                    return;
                case "irid":
                    d.SetF("customIrid", Mathf.Clamp01(sourceCustom
                        ? d.GetF("customIrid", def.GlobalFloat)
                        : def.GlobalFloat * (sourceGlobal ? 1f
                            : Mathf.Clamp01(d.GetF("fxIridAmt", 1f)))));
                    return;
                case "chroma":
                    d.SetF("customChroma", Mathf.Clamp01(sourceCustom
                        ? d.GetF("customChroma", def.GlobalFloat)
                        : def.GlobalFloat * (sourceGlobal ? 1f
                            : Mathf.Clamp01(d.GetF("fxChromaAmt", 1f)))));
                    return;
                case "frost":
                    d.SetF("customFrost", Mathf.Clamp01(sourceCustom
                        ? d.GetF("customFrost", def.GlobalFloat)
                        : def.GlobalFloat * (sourceGlobal ? 1f
                            : Mathf.Clamp01(d.GetF("fxFrostAmt", 1f)))));
                    return;
                case "shineOn":
                    d.SetB("customShineOn", sourceCustom ? d.GetB("customShineOn", def.GlobalBool)
                        : def.GlobalBool && (sourceGlobal || d.GetB("fxShine", true)));
                    return;
                case "iridOn":
                    d.SetB("customIridOn", sourceCustom ? d.GetB("customIridOn", def.GlobalBool)
                        : def.GlobalBool && (sourceGlobal || d.GetB("fxIrid", true)));
                    return;
                case "chromaOn":
                    d.SetB("customChromaOn", sourceCustom ? d.GetB("customChromaOn", def.GlobalBool)
                        : def.GlobalBool && (sourceGlobal || d.GetB("fxChroma", true)));
                    return;
                case "customFrostOn":
                    d.SetB("customFrostOn", sourceCustom ? d.GetB("customFrostOn", true)
                        : sourceGlobal || d.GetB("fxFrost", true));
                    return;

                // ---- null-global odds and ends ----
                case "rippleSmooth":
                    d.SetF("rippleSmooth", sourceGlobal ? 0f : d.GetF("rippleSmooth", 0f));
                    return;
                case "edgeLight":
                    // The line's own edge-light key; panels collapse theirs into `spec`.
                    if (d.Type != HudElementType.Polyline) return;
                    break;
            }

            string key = def.ParamKey;
            if (string.IsNullOrEmpty(key)) return;

            // Element GEOMETRY (box-end fades, trapezoid insets) — write the stored value back so
            // the snapshot is COMPLETE, which is what stops a later base edit leaking into a fork.
            if (def.StateIndependent) { d.SetF(key, d.GetF(key, 0f)); return; }

            switch (def.Kind)
            {
                case HudFxKind.Bool:
                    d.SetB(key, sourceCustom ? d.GetB(key, def.GlobalBool) : def.GlobalBool);
                    break;
                case HudFxKind.Color:
                    break;                                   // refs are never snapshotted
                case HudFxKind.Int:
                case HudFxKind.Combo:
                    d.SetI(key, sourceCustom ? d.GetI(key, def.GlobalInt) : def.GlobalInt);
                    break;
                default:
                    d.SetF(key, SnapshotFloat(d, source, key, def.GlobalFloat));
                    break;
            }
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
