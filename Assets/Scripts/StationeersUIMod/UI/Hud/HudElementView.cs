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

        /// <summary>True while the HUD is drawn with the BARE (power-off) geometry. Set by
        /// HudSystem each frame. At runtime it follows the live tier; while the F9 editor is open
        /// it follows the EXPLICIT preview instead, so what you see always matches what you edit
        /// (a drag can't render one tier while writing another). Elements with a bare override
        /// resolve their geometry against it.</summary>
        internal static bool LayoutBare;

        /// <summary>The effective tier the HUD is drawn for (VISIBILITY — which elements show).
        /// The F9 editor filters selection by this so you can't grab an element that isn't on
        /// screen in the current preview.</summary>
        internal static HudTier LayoutTier;

        /// <summary>True only when the F9 editor is open AND the preview is EXPLICITLY bare — the
        /// tier the editor writes to. Deliberately separate from <see cref="LayoutBare"/> so a
        /// 'Live'/'Suited' preview always edits the base layout and never silently forks a bare
        /// override just because the player happens to be bare (adversarial review 2026-07-12).</summary>
        internal static bool EditBareTier;

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

        // Explicit style-source mode added by the reorganised F9 inspector. Zero is deliberately
        // "legacy mixed": profiles authored before this mode existed keep their exact collection
        // of followGlobal flags and -1 sentinels until the author chooses a coherent mode.
        // 1 = every meaningful panel appearance/effect follows the globals; 2 = a complete local
        // snapshot. Switching Global -> Custom seeds that snapshot first, so there is no visual jump.
        private const int StyleLegacy = 0;
        private const int StyleGlobal = 1;
        private const int StyleCustom = 2;
        private static readonly string[] StyleSourceNames =
            { "Legacy mixed (preserve profile)", "Follow global theme + effects", "Custom (restore saved values)" };

        private static int StyleSourceOf(HudElementDef d)
        {
            int v = d != null ? d.GetI("styleSource", StyleLegacy) : StyleLegacy;
            return Mathf.Clamp(v, StyleLegacy, StyleCustom);
        }

        private int StyleSource
        {
            get
            {
                return StyleSourceOf(Def);
            }
        }

        private bool UsesGlobalStyle => StyleSource == StyleGlobal;
        private bool UsesCustomStyle => StyleSource == StyleCustom;

        /// <summary>Per-corner radius resolved through the explicit style source, with the old
        /// -1 sentinel retained only for legacy profiles.</summary>
        protected float Radius(float perCorner)
        {
            float global = HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f;
            if (UsesGlobalStyle) return global;
            if (UsesCustomStyle) return perCorner >= 0f ? perCorner : global;
            return perCorner >= 0f ? perCorner : global;
        }

        protected float BorderWidthFor()
        {
            float global = HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f;
            if (UsesGlobalStyle) return global;
            return Def != null && Def.BorderWidth >= 0f ? Def.BorderWidth : global;
        }

        /// <summary>Trapezoid insets (reference px × scale) pull the top/bottom corners of the
        /// element's background panel inward — the same shaping the Box primitive and the hand
        /// tray use to angle their side edges. Views that expose a single framed panel forward
        /// these into their <see cref="PanelGraphic.SetShape"/> call.</summary>
        protected float InsetTop(float scale) => Def.GetF("insetTop", 0f) * scale;
        protected float InsetBottom(float scale) => Def.GetF("insetBottom", 0f) * scale;

        /// <summary>Whether this element draws a single framed background panel that a trapezoid
        /// inset can reshape. Box-bearing views override this to true so the two inset sliders
        /// appear in their prop list; multi-panel views (chips, equipment grid, body doll) leave
        /// it false so no dead slider is offered.</summary>
        protected virtual bool SupportsTrapezoid => false;

        /// <summary>Whether this element owns at least one UIA PanelGraphic/PolygonPanelGraphic
        /// whose surface can actually consume panel styling. Keeps the inspector from offering
        /// frost, corners, and glow on text-only or borrowed-vanilla widgets where those controls
        /// could never affect a pixel.</summary>
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
                case HudElementType.MoodletDashboard:
                case HudElementType.VitalsPanel:
                case HudElementType.DamageDoll:
                case HudElementType.JetpackBox:
                case HudElementType.StateChips:
                case HudElementType.SuitChips:
                case HudElementType.PngDoll:
                    return true;
                default:
                    return false;
            }
        }

        private bool SupportsPanelAppearance => SupportsPanelAppearanceFor(Def);

        /// <summary>Used by F9 to suppress panel-only actions on text, borrowed vanilla UI and
        /// other elements that cannot render any UIA surface.</summary>
        internal bool CanFlatten => SupportsPanelAppearance;

        internal static bool CanFlattenDefinition(HudElementDef d)
            => SupportsPanelAppearanceFor(d);

        internal static bool IsCustomStyleDefinition(HudElementDef d)
            => StyleSourceOf(d) == StyleCustom;

        // Freeform pen Shapes use PolygonPanelGraphic and deliberately retain the proven mesh
        // renderer: an arbitrary concave contour is not representable by the rounded-box SDF.
        private bool SupportsAnalyticPanel => SupportsPanelAppearance
            && Def != null && Def.Type != HudElementType.Shape;

        // Suit-chip and moodlet-pill corner radii are deliberately derived from their size, and
        // arbitrary pen Shapes use PolygonPanelGraphic. None consumes the four authored radii.
        private bool SupportsAuthoredCorners => SupportsAnalyticPanel
            && Def != null
            && Def.Type != HudElementType.SuitChips
            && Def.Type != HudElementType.MoodletDashboard;

        // ActiveHandBadge intentionally derives its contour from ActiveHandAccent at runtime.
        private bool SupportsCustomBorderColor => Def != null
            && Def.Type != HudElementType.ActiveHandBadge;

        /// <summary>When true the element ignores its OWN colour refs and draws the GLOBAL box
        /// colours (the shared HudPalette entries), so editing those in F9 re-tints every following
        /// element at once. Off = the element keeps its own Fill/Border/Text (and bar) colours.</summary>
        protected bool FollowGlobal => Def != null
            && (UsesGlobalStyle || (!UsesCustomStyle && Def.GetB("followGlobal", false)));

        /// <summary>The global palette value when this element follows global colours; otherwise the
        /// element's own colour ref resolved against that same value as the fallback.</summary>
        protected Color GlobalOr(string colorRef, Color global)
            => FollowGlobal ? global : HudPalette.Resolve(colorRef, global);

        protected Color FillColor() => GlobalOr(Def.Fill, HudPalette.PanelFill.Value);
        protected Color BorderColor() => GlobalOr(Def.Border, HudPalette.PanelBorder.Value);
        protected Color TextColor() => GlobalOr(Def.TextColor, HudPalette.TextValue.Value);

        // #4: drag-over drop highlight — the hand / 1-6 equipment boxes light up while a dragged
        // item hovers a valid target. Per-element mode (border vs whole box) + colour (a ColorRef;
        // empty = the global HudDropHighlight palette).
        protected bool DropWholeBox() => Def != null && Def.GetB("dropWholeBox", false);
        protected Color DropHighlightColor()
        {
            string cref = Def != null ? Def.GetS("dropHiColor", "") : "";
            return string.IsNullOrEmpty(cref)
                ? HudPalette.DropHighlight.Value
                : HudPalette.Resolve(cref, HudPalette.DropHighlight.Value);
        }
        /// <summary>F9 props for the drop highlight — added by widgets that are drop targets.</summary>
        protected void AddDropHighlightProps(System.Collections.Generic.List<HudProp> into)
        {
            var d = Def;
            int start = into.Count;
            into.Add(HudProp.Bool("Drop: light whole box", () => d.GetB("dropWholeBox", false), v => d.SetB("dropWholeBox", v)));
            into.Add(HudProp.Color("Drop highlight colour", () => d.GetS("dropHiColor", ""),
                v => d.Set("dropHiColor", v), () => HudPalette.DropHighlight.Value));
            MarkProps(into, start, HudPropGroup.Interaction);
        }

        /// <summary>The element's effective glass sheen. An element set to "Follow global colours"
        /// also follows the GLOBAL glass — ignoring its own baked "sheen" — so the one global slider
        /// (and the "Make ALL boxes follow global" button) drives it just like the box colours do.
        /// Otherwise it uses its own value, with the "-1 = global" convention (matching corner radius
        /// and border width) as a per-param opt-in.</summary>
        protected float GlassSheenFor()
        {
            float global = HudConfig.GlassSheen != null ? HudConfig.GlassSheen.Value : 0f;
            if (UsesGlobalStyle || FollowGlobal) return global;
            float v = Def.GetF("sheen", -1f);
            return v >= 0f ? v : global;
        }

        /// <summary>The element's effective glass edge light. Follows the global when this element
        /// follows global colours, else its own value with the "-1 = global" fallback. See
        /// <see cref="GlassSheenFor"/>.</summary>
        protected float GlassEdgeFor()
        {
            float global = HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f;
            float own = Def.GetF("spec", -1f);
            float v = (UsesGlobalStyle || FollowGlobal) ? global : own;
            float baseSpec = v >= 0f ? v : global;
            // In coherent Custom mode `spec` is the FINAL per-element edge-light strength.
            // The transition snapshot captures any current global boost, so applying it again
            // would make Custom neither independent nor visually continuous.
            if (UsesCustomStyle) return Mathf.Clamp01(baseSpec);
            // Tier A's edge-light knob boosts the PANEL border light too (play-test: the
            // slider only drove drawn lines, which most HUDs barely use — the "brighter and
            // darker along the border" run lives in PanelGraphic.BorderAt and this is its
            // volume). Every widget flows through here (incl. hand/equipment boxes' direct
            // Spec sets), so one slider lights the whole HUD's edges.
            // BUT an element that has EXPLICITLY zeroed its own edge light (spec == 0 while not
            // following global colours) opts OUT of that global boost — otherwise "Make flat" and
            // dragging Glass edge light to 0 did nothing, because the global lit the border back up.
            bool optedOut = !FollowGlobal && own == 0f;
            if (!optedOut
                && HudConfig.FxTierA != null && HudConfig.FxTierA.Value
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
            float fx = Def != null ? Def.GetF("edgeFadeX", 0f) : 0f;
            float fy = Def != null ? Def.GetF("edgeFadeY", 0f) : 0f;
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
            return Def != null ? Def.GetF("feather", -1f) : -1f;
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
            g.Glow = glOn ? OwnOrGlobal("glow", HudConfig.FxGlow) : 0f;
            g.GlowInner = glOn ? OwnOrGlobal("glowIn", HudConfig.FxGlowInner) : 0f;
            g.GlowWidth = OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth);
            g.GlowDiffuse = OwnOrGlobal("glowDiffuse", HudConfig.FxGlowDiffuse);
            bool rippleOn = tierA && StyleFeatureOn("customRippleOn", HudConfig.FxEdgeLightOn);
            g.EdgeRipple = rippleOn ? OwnOrGlobal("ripple", HudConfig.FxEdgeRipple) : 0f;
            g.EdgeRippleFreq = OwnOrGlobal("rippleFreq", HudConfig.FxEdgeRippleFreq);
            g.RippleSmooth = UsesGlobalStyle ? 0f : (Def != null ? Def.GetF("rippleSmooth", 0f) : 0f);

            ApplyFx(g);
        }

        /// <summary>Sheen-pattern resolve: the element's own param when >= 0, else the global.</summary>
        protected float OwnOrGlobal(string key, ConfigEntry<float> global)
        {
            if (UsesGlobalStyle) return global != null ? global.Value : 0f;
            float v = Def != null ? Def.GetF(key, -1f) : -1f;
            return v >= 0f ? v : (global != null ? global.Value : 0f);
        }

        private bool StyleFeatureOn(string customKey, ConfigEntry<bool> global)
        {
            bool gv = global != null && global.Value;
            return UsesCustomStyle && Def != null ? Def.GetB(customKey, gv) : gv;
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
            bool sdfWanted = panel != null
                && HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                && Core.HudShaderStore.SdfAvailable;
            bool sdfAssigned = sdfWanted && HudFxMaterials.Assign(g.AsGraphic, "sdfglass");
            if (panel != null)
            {
                float edgeFadeX = Def != null ? Def.GetF("edgeFadeX", 0f) : 0f;
                float edgeFadeY = Def != null ? Def.GetF("edgeFadeY", 0f) : 0f;
                if (sdfAssigned)
                {
                    panel.SetSdfStyle(true,
                        SdfSquircleFor(), SdfGaussianFor(), EdgeFlowFor(),
                        FrostAmountFor(), FrostDepthFor(), ChromaAmountFor(),
                        ShineAmountFor(), IridAmountFor(), DissolveFor(),
                        edgeFadeX, edgeFadeY);
                    // The SDF ABI carries independent final strengths; uv0.x's old combined
                    // volume is not used and must not force needless legacy mesh semantics.
                    g.FxStrength = 0f;
                    return;
                }

                panel.SetSdfStyle(false, 2f, false, 0f, 0f, 1f, 0f, 0f, 0f,
                    false, 0f, 0f);
            }

            g.FxStrength = FxStrengthFor();

            // Material selection (one per graphic; frost wins over edge-fx): all shared
            // materials, so batching holds per family; Assign/Unassign are per-frame cheap
            // (dictionary lookups) and only churn on an actual toggle. Falls through to the
            // default UI material whenever the bundle/tier is off — the Tier A look.
            bool frost = FrostAmountFor() > 0.001f;
            bool edgeFx = !frost
                && Core.HudShaderStore.TierBAvailable
                && (ShineAmountFor() > 0.001f || IridAmountFor() > 0.001f || DissolveFor());
            if (frost) { if (!HudFxMaterials.Assign(g.AsGraphic, "glass")) HudFxMaterials.Unassign(g.AsGraphic); }
            else if (edgeFx) { if (!HudFxMaterials.Assign(g.AsGraphic, "edgefx")) HudFxMaterials.Unassign(g.AsGraphic); }
            else HudFxMaterials.Unassign(g.AsGraphic);
        }

        private float SdfSquircleFor()
        {
            float global = HudConfig.SdfSquircle != null ? HudConfig.SdfSquircle.Value : 2f;
            if (UsesGlobalStyle) return global;
            float own = Def != null ? Def.GetF("squircle", -1f) : -1f;
            return own >= 2f ? Mathf.Clamp(own, 2f, 8f) : global;
        }

        private bool SdfGaussianFor()
        {
            bool global = HudConfig.SdfGaussianHalo != null && HudConfig.SdfGaussianHalo.Value;
            return UsesCustomStyle && Def != null ? Def.GetB("gaussianHalo", global) : global;
        }

        private float EdgeFlowFor()
            => OwnOrGlobal("edgeFlow", HudConfig.FxEdgeFlowSpeed);

        private float FrostDepthFor()
            => Mathf.Clamp01(OwnOrGlobal("frostDepth", HudConfig.FrostDepth));

        private float ShineAmountFor()
        {
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            if (!tier || Def == null) return 0f;
            bool globalOn = HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            float global = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            if (UsesGlobalStyle) return globalOn ? Mathf.Clamp(global, 0f, 2f) : 0f;
            if (UsesCustomStyle)
                return Def.GetB("customShineOn", globalOn)
                    ? Mathf.Clamp(Def.GetF("customShine", global), 0f, 2f) : 0f;
            return globalOn && Def.GetB("fxShine", true)
                ? Mathf.Clamp(global * Mathf.Clamp01(Def.GetF("fxShineAmt", 1f)), 0f, 2f) : 0f;
        }

        private float IridAmountFor()
        {
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            if (!tier || Def == null) return 0f;
            bool globalOn = HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            float global = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            if (UsesGlobalStyle) return globalOn ? Mathf.Clamp01(global) : 0f;
            if (UsesCustomStyle)
                return Def.GetB("customIridOn", globalOn)
                    ? Mathf.Clamp01(Def.GetF("customIrid", global)) : 0f;
            return globalOn && Def.GetB("fxIrid", true)
                ? Mathf.Clamp01(global * Mathf.Clamp01(Def.GetF("fxIridAmt", 1f))) : 0f;
        }

        private float FrostAmountFor()
        {
            bool tier = HudBackdrop.Active && HudConfig.FxTierC != null && HudConfig.FxTierC.Value;
            if (!tier || Def == null) return 0f;
            float global = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f;
            if (UsesGlobalStyle) return Mathf.Clamp01(global);
            if (UsesCustomStyle)
                return Def.GetB("customFrostOn", true)
                    ? Mathf.Clamp01(Def.GetF("customFrost", global)) : 0f;
            return Def.GetB("fxFrost", true)
                ? Mathf.Clamp01(global * Mathf.Clamp01(Def.GetF("fxFrostAmt", 1f))) : 0f;
        }

        private float ChromaAmountFor()
        {
            if (FrostAmountFor() <= 0.001f || Def == null) return 0f;
            bool tier = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            bool globalOn = HudConfig.FxChromaOn != null && HudConfig.FxChromaOn.Value;
            float global = HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f;
            if (!tier) return 0f;
            if (UsesGlobalStyle) return globalOn ? Mathf.Clamp01(global) : 0f;
            if (UsesCustomStyle)
                return Def.GetB("customChromaOn", globalOn)
                    ? Mathf.Clamp01(Def.GetF("customChroma", global)) : 0f;
            return globalOn && Def.GetB("fxChroma", true)
                ? Mathf.Clamp01(global * Mathf.Clamp01(Def.GetF("fxChromaAmt", 1f))) : 0f;
        }

        private bool DissolveFor()
        {
            if (Def == null || HudConfig.FxTierB == null || !HudConfig.FxTierB.Value) return false;
            bool global = HudConfig.FxDissolveBoot != null && HudConfig.FxDissolveBoot.Value;
            if (UsesGlobalStyle) return global;
            if (UsesCustomStyle) return Def.GetB("customDissolve", global);
            return global && Def.GetB("fxDissolve", true);
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
            bool active = Def != null
                && HudConfig.FxTierA != null && HudConfig.FxTierA.Value
                && HudConfig.FxPulseOn != null && HudConfig.FxPulseOn.Value
                && !UsesGlobalStyle
                && Def.GetB("fxPulse", false);
            float amt = active ? Mathf.Clamp(Def.GetF("fxPulseAmt", 1f), 0f, 2f) : 0f;

            if (amt <= 0f)
            {
                if (_pulseTinted) TintFxGraphics(Color.white); // one-shot reset on opt-out
                _pulseTinted = false;
                return;
            }

            float speed = HudConfig.FxPulseSpeed != null ? HudConfig.FxPulseSpeed.Value : 0.5f;
            float depth = (HudConfig.FxPulseDepth != null ? HudConfig.FxPulseDepth.Value : 0.25f) * amt;
            // Stable per-element phase offset (seeded by the Id hash) so a wall of pulsing
            // elements breathes organically instead of in lockstep.
            float phase = Def.Id != null ? (Def.Id.GetHashCode() & 0xFF) / 255f : 0f;
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

        /// <summary>Whether an editor field writes the BARE override rather than the base (live)
        /// layout: only when the preview is EXPLICITLY bare AND the element is a "Both" element.
        /// A single-mode element has one layout, so it always edits base; and keying off the
        /// explicit preview (not the render tier) means editing while the player is genuinely bare
        /// in a 'Live' preview never silently forks a bare override.</summary>
        internal static bool EditBare(HudElementDef d)
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
            into.Add(HudProp.Header("Style source"));
            into.Add(HudProp.Enum("Appearance & effects",
                () => StyleSource,
                v =>
                {
                    int next = Mathf.Clamp(v, StyleLegacy, StyleCustom);
                    // The first transition snapshots the current look. Later Global <-> Custom
                    // comparisons restore the dormant custom design instead of overwriting it.
                    if (next == StyleCustom && StyleSource != StyleCustom
                        && !d.GetB("customStyleReady", false))
                        SeedCustomStyleFromEffective(d);
                    d.SetI("styleSource", next);
                },
                StyleSourceNames));
            MarkProps(into, styleStart, HudPropGroup.Appearance);

            // Existing profiles deliberately stay on their exact, mixed collection of flags and
            // -1 sentinels until the author opts into the new contract. Their familiar controls
            // remain available under this explicit compatibility mode.
            if (StyleSource == StyleLegacy)
            {
                AddUnifiedLayoutProps(into, d);
                AddLegacyAppearanceProps(into, d);
                AddLegacyEffectProps(into, d);
                return;
            }

            AddUnifiedLayoutProps(into, d);
            AddUnifiedAppearanceProps(into, d);
            AddUnifiedEffectProps(into, d);
        }

        private void AddLegacyAppearanceProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            into.Add(HudProp.Header("Appearance (legacy mixed)"));
            into.Add(HudProp.Bool("Follow global colours + glass", () => d.GetB("followGlobal", false), v =>
            {
                bool was = d.GetB("followGlobal", false);
                d.SetB("followGlobal", v);
                if (was && !v) SeedColoursFromGlobal(d);
            }));
            if (!d.GetB("followGlobal", false))
            {
                into.Add(HudProp.Color("Text / accent", () => d.TextColor, v => d.TextColor = v));
                if (SupportsPanelAppearance)
                {
                    into.Add(HudProp.Color("Fill", () => d.Fill, v => d.Fill = v));
                    if (SupportsCustomBorderColor)
                        into.Add(HudProp.Color("Border", () => d.Border, v => d.Border = v));
                    into.Add(HudProp.F("Glass sheen (-1 = global)", () => d.GetF("sheen", -1f),
                        v => d.SetF("sheen", v < 0f ? -1f : Mathf.Clamp01(v)), -1f, 1f));
                    into.Add(HudProp.F("Glass edge light (-1 = global)", () => d.GetF("spec", -1f),
                        v => d.SetF("spec", v < 0f ? -1f : Mathf.Clamp01(v)), -1f, 1f));
                }
            }
            if (SupportsPanelAppearance)
            {
                into.Add(HudProp.F("Border width (-1 = global)", () => d.BorderWidth,
                    v => d.BorderWidth = v, -1f, 8f));
                into.Add(HudProp.F("Edge softness / AA (-1 = global)", () => d.GetF("feather", -1f),
                    v => d.SetF("feather", v < 0f ? -1f : Mathf.Clamp(v, 0f, 4f)), -1f, 4f));
                if (SupportsAuthoredCorners)
                {
                    into.Add(HudProp.F("Corner TL (-1 = global)", () => d.RTL, v => d.RTL = v, -1f, 64f));
                    into.Add(HudProp.F("Corner TR (-1 = global)", () => d.RTR, v => d.RTR = v, -1f, 64f));
                    into.Add(HudProp.F("Corner BR (-1 = global)", () => d.RBR, v => d.RBR = v, -1f, 64f));
                    into.Add(HudProp.F("Corner BL (-1 = global)", () => d.RBL, v => d.RBL = v, -1f, 64f));
                }
            }
            if (SupportsTrapezoid)
            {
                into.Add(HudProp.F("Top inset (trapezoid)", () => d.GetF("insetTop", 0f),
                    v => d.SetF("insetTop", Mathf.Max(0f, v)), 0f, 400f));
                into.Add(HudProp.F("Bottom inset (trapezoid)", () => d.GetF("insetBottom", 0f),
                    v => d.SetF("insetBottom", Mathf.Max(0f, v)), 0f, 400f));
            }
            into.Add(HudProp.F("Font scale", () => d.FontScale,
                v => d.FontScale = Mathf.Clamp(v, 0.4f, 3f), 0.4f, 3f));
            MarkProps(into, start, HudPropGroup.Appearance);
        }

        private void AddLegacyEffectProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            if (SupportsPanelAppearance)
            {
                into.Add(HudProp.Header("Panel effects (legacy mixed)"));
                into.Add(HudProp.Bool("Follow global edge + glow values", () => !EffectsAreSeparated(d), v =>
                {
                    if (v) ResetEffectsToGlobal(d); else SeedEffectsFromGlobal(d);
                }));
                into.Add(HudProp.Bool("Shine sweep", () => d.GetB("fxShine", true), v => d.SetB("fxShine", v)));
                into.Add(HudProp.F("  shine multiplier", () => d.GetF("fxShineAmt", 1f),
                    v => d.SetF("fxShineAmt", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Iridescent edge", () => d.GetB("fxIrid", true), v => d.SetB("fxIrid", v)));
                into.Add(HudProp.F("  iridescence multiplier", () => d.GetF("fxIridAmt", 1f),
                    v => d.SetF("fxIridAmt", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Chromatic fringe", () => d.GetB("fxChroma", true), v => d.SetB("fxChroma", v)));
                into.Add(HudProp.F("  chromatic multiplier", () => d.GetF("fxChromaAmt", 1f),
                    v => d.SetF("fxChromaAmt", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Frosted glass", () => d.GetB("fxFrost", true), v => d.SetB("fxFrost", v)));
                into.Add(HudProp.F("  frost multiplier", () => d.GetF("fxFrostAmt", 1f),
                    v => d.SetF("fxFrostAmt", Mathf.Clamp01(v)), 0f, 1f));
                if (SupportsAnalyticPanel)
                    into.Add(HudProp.F("  frost depth (-1 = global)", () => d.GetF("frostDepth", -1f),
                        v => d.SetF("frostDepth", v < 0f ? -1f : Mathf.Clamp01(v)), -1f, 1f));
                into.Add(HudProp.Bool("Dissolve during boot", () => d.GetB("fxDissolve", true), v => d.SetB("fxDissolve", v)));

                if (EffectsAreSeparated(d))
                {
                    into.Add(HudProp.F("Border fade", () => d.GetF("bfade", 0f),
                        v => d.SetF("bfade", Mathf.Clamp01(v)), 0f, 1f));
                    into.Add(HudProp.F("Soft edge px", () => d.GetF("softEdge", 0f),
                        v => d.SetF("softEdge", Mathf.Clamp(v, 0f, 48f)), 0f, 48f));
                    into.Add(HudProp.F("Glow outside", () => d.GetF("glow", 0f),
                        v => d.SetF("glow", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    into.Add(HudProp.F("Glow inside", () => d.GetF("glowIn", 0f),
                        v => d.SetF("glowIn", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    into.Add(HudProp.F("Glow width px", () => d.GetF("glowWidth", 24f),
                        v => d.SetF("glowWidth", Mathf.Clamp(v, 6f, 160f)), 6f, 160f));
                    into.Add(HudProp.F("Glow diffuseness", () => d.GetF("glowDiffuse", 0f),
                        v => d.SetF("glowDiffuse", Mathf.Clamp01(v)), 0f, 1f));
                    into.Add(HudProp.F("Edge ripple", () => d.GetF("ripple", 0f),
                        v => d.SetF("ripple", Mathf.Clamp(v, 0f, 2.5f)), 0f, 2.5f));
                    into.Add(HudProp.F("Ripple frequency", () => d.GetF("rippleFreq", 2f),
                        v => d.SetF("rippleFreq", Mathf.Clamp(v, 0.05f, 8f)), 0.05f, 8f));
                }
                into.Add(HudProp.F("Ripple smoothness", () => d.GetF("rippleSmooth", 0f),
                    v => d.SetF("rippleSmooth", Mathf.Clamp01(v)), 0f, 1f));
                if (SupportsAnalyticPanel)
                    into.Add(HudProp.F("Edge flow speed (-1 = global)", () => d.GetF("edgeFlow", -1f),
                        v => d.SetF("edgeFlow", v < 0f ? -1f : Mathf.Clamp(v, 0f, 4f)), -1f, 4f));
                into.Add(HudProp.F("Fade box ends L/R", () => d.GetF("edgeFadeX", 0f),
                    v => d.SetF("edgeFadeX", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
                into.Add(HudProp.F("Fade box top/bottom", () => d.GetF("edgeFadeY", 0f),
                    v => d.SetF("edgeFadeY", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
            }

            into.Add(HudProp.Header("Motion & power transitions"));
            into.Add(HudProp.Bool("Death collapse", () => d.GetB("fxCollapse", true), v => d.SetB("fxCollapse", v)));
            into.Add(HudProp.F("  collapse strength", () => d.GetF("fxCollapseAmt", 1f),
                v => d.SetF("fxCollapseAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            into.Add(HudProp.Bool("Glitch tear", () => d.GetB("fxGlitch", true), v => d.SetB("fxGlitch", v)));
            into.Add(HudProp.F("  glitch strength", () => d.GetF("fxGlitchAmt", 1f),
                v => d.SetF("fxGlitchAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            into.Add(HudProp.Bool("Warp / curve", () => d.GetB("fxWarp", true), v => d.SetB("fxWarp", v)));
            into.Add(HudProp.F("  warp strength", () => d.GetF("fxWarpAmt", 1f),
                v => d.SetF("fxWarpAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            if (SupportsPanelAppearance)
            {
                into.Add(HudProp.Bool("Breathing pulse", () => d.GetB("fxPulse", false), v => d.SetB("fxPulse", v)));
                into.Add(HudProp.F("  pulse strength", () => d.GetF("fxPulseAmt", 1f),
                    v => d.SetF("fxPulseAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            }
            MarkProps(into, start, HudPropGroup.Effects);
        }

        private void AddUnifiedLayoutProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            into.Add(HudProp.Header("Layout"));
            into.Add(HudProp.Anchor("Anchor", () => (int)d.AnchorFor(EditBare(d), LayoutMode),
                v => d.SetAnchorFor(EditBare(d), LayoutMode, (HudAnchor)v)));
            into.Add(HudProp.F("X", () => d.XFor(EditBare(d), LayoutMode),
                v => d.SetXFor(EditBare(d), LayoutMode, v), -2000f, 2000f));
            into.Add(HudProp.F("Y", () => d.YFor(EditBare(d), LayoutMode),
                v => d.SetYFor(EditBare(d), LayoutMode, v), -2000f, 2000f));
            into.Add(HudProp.F("Width", () => d.WFor(EditBare(d), LayoutMode),
                v => d.SetWFor(EditBare(d), LayoutMode, Mathf.Max(2f, v)), 2f, 2200f));
            into.Add(HudProp.F("Height", () => d.HFor(EditBare(d), LayoutMode),
                v => d.SetHFor(EditBare(d), LayoutMode, Mathf.Max(2f, v)), 2f, 1300f));
            into.Add(HudProp.F("Width % (-1 = fixed)", () => d.WPctFor(EditBare(d), LayoutMode),
                v => d.SetWPctFor(EditBare(d), LayoutMode, v), -1f, 1f));
            into.Add(HudProp.F("Height % (-1 = fixed)", () => d.HPctFor(EditBare(d), LayoutMode),
                v => d.SetHPctFor(EditBare(d), LayoutMode, v), -1f, 1f));
            into.Add(HudProp.I("Z order", () => d.Z,
                v => { d.Z = v; HudSystem.RequestZResort(); }, -100, 100));
            into.Add(HudProp.Tier("Visible tiers (B / S / R)", () => (int)d.Tiers,
                v => { d.Tiers = (HudTierMask)v; if (!IsBoth(d.Tiers)) d.SetBareLayout(false); }));
            MarkProps(into, start, HudPropGroup.Layout);
        }

        private void AddUnifiedAppearanceProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            into.Add(HudProp.Header("Appearance"));
            if (UsesGlobalStyle)
            {
                into.Add(HudProp.Header("Following F9 Theme and Effects globals"));
            }
            else
            {
                into.Add(HudProp.Color("Text / accent", () => d.TextColor, v => d.TextColor = v));
                if (SupportsPanelAppearance)
                {
                    into.Add(HudProp.Color("Fill", () => d.Fill, v => d.Fill = v));
                    if (SupportsCustomBorderColor)
                        into.Add(HudProp.Color("Border", () => d.Border, v => d.Border = v));
                    into.Add(HudProp.F("Border width", () => d.BorderWidth,
                        v => d.BorderWidth = Mathf.Clamp(v, 0f, 8f), 0f, 8f));
                    into.Add(HudProp.F("Edge softness / AA", () => d.GetF("feather", 1.25f),
                        v => d.SetF("feather", Mathf.Clamp(v, 0f, 4f)), 0f, 4f));
                    if (SupportsAuthoredCorners)
                    {
                        into.Add(HudProp.F("Corner TL", () => d.RTL, v => d.RTL = Mathf.Max(0f, v), 0f, 64f));
                        into.Add(HudProp.F("Corner TR", () => d.RTR, v => d.RTR = Mathf.Max(0f, v), 0f, 64f));
                        into.Add(HudProp.F("Corner BR", () => d.RBR, v => d.RBR = Mathf.Max(0f, v), 0f, 64f));
                        into.Add(HudProp.F("Corner BL", () => d.RBL, v => d.RBL = Mathf.Max(0f, v), 0f, 64f));
                    }
                    into.Add(HudProp.F("Glass sheen", () => d.GetF("sheen", 0f),
                        v => d.SetF("sheen", Mathf.Clamp01(v)), 0f, 1f));
                    into.Add(HudProp.F("Glass edge light", () => d.GetF("spec", 0f),
                        v => d.SetF("spec", Mathf.Clamp01(v)), 0f, 1f));
                    if (SupportsAnalyticPanel)
                    {
                        into.Add(HudProp.F("Corner shape (2=round, 8=squircle)", () => d.GetF("squircle", 2f),
                            v => d.SetF("squircle", Mathf.Clamp(v, 2f, 8f)), 2f, 8f));
                        into.Add(HudProp.Bool("Gaussian-distance halo", () => d.GetB("gaussianHalo", false),
                            v => d.SetB("gaussianHalo", v)));
                    }
                }
            }
            if (SupportsTrapezoid)
            {
                into.Add(HudProp.F("Top inset (trapezoid)", () => d.GetF("insetTop", 0f),
                    v => d.SetF("insetTop", Mathf.Max(0f, v)), 0f, 400f));
                into.Add(HudProp.F("Bottom inset (trapezoid)", () => d.GetF("insetBottom", 0f),
                    v => d.SetF("insetBottom", Mathf.Max(0f, v)), 0f, 400f));
            }
            into.Add(HudProp.F("Font scale", () => d.FontScale,
                v => d.FontScale = Mathf.Clamp(v, 0.4f, 3f), 0.4f, 3f));
            MarkProps(into, start, HudPropGroup.Appearance);
        }

        private void AddUnifiedEffectProps(List<HudProp> into, HudElementDef d)
        {
            int start = into.Count;
            into.Add(HudProp.Header("Effects"));
            if (SupportsPanelAppearance && UsesGlobalStyle)
                into.Add(HudProp.Header("Panel appearance follows the F9 Effects globals"));
            if (SupportsPanelAppearance && UsesCustomStyle)
            {
                into.Add(HudProp.Header("Per-element strengths (timing, direction, tint and capture remain global)"));
                into.Add(HudProp.Bool("Border fade", () => d.GetB("customBorderFadeOn", true),
                    v => d.SetB("customBorderFadeOn", v)));
                if (d.GetB("customBorderFadeOn", true))
                    into.Add(HudProp.F("  border fade amount", () => d.GetF("bfade", 0f),
                        v => d.SetF("bfade", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Soft edge", () => d.GetB("customSoftEdgeOn", true),
                    v => d.SetB("customSoftEdgeOn", v)));
                if (d.GetB("customSoftEdgeOn", true))
                    into.Add(HudProp.F("  soft edge px", () => d.GetF("softEdge", 0f),
                        v => d.SetF("softEdge", Mathf.Clamp(v, 0f, 48f)), 0f, 48f));
                into.Add(HudProp.Bool("Glow", () => d.GetB("customGlowOn", false),
                    v => d.SetB("customGlowOn", v)));
                if (d.GetB("customGlowOn", false))
                {
                    into.Add(HudProp.F("  glow outside", () => d.GetF("glow", 0f),
                        v => d.SetF("glow", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    into.Add(HudProp.F("  glow inside", () => d.GetF("glowIn", 0f),
                        v => d.SetF("glowIn", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                    into.Add(HudProp.F("  glow width px", () => d.GetF("glowWidth", 24f),
                        v => d.SetF("glowWidth", Mathf.Clamp(v, 6f, 160f)), 6f, 160f));
                    into.Add(HudProp.F("  glow diffuseness", () => d.GetF("glowDiffuse", 0f),
                        v => d.SetF("glowDiffuse", Mathf.Clamp01(v)), 0f, 1f));
                }
                into.Add(HudProp.Bool("Animated edge energy", () => d.GetB("customRippleOn", true),
                    v => d.SetB("customRippleOn", v)));
                if (d.GetB("customRippleOn", true))
                {
                    into.Add(HudProp.F("  ripple amount", () => d.GetF("ripple", 0f),
                        v => d.SetF("ripple", Mathf.Clamp(v, 0f, 2.5f)), 0f, 2.5f));
                    into.Add(HudProp.F("  ripple frequency", () => d.GetF("rippleFreq", 2f),
                        v => d.SetF("rippleFreq", Mathf.Clamp(v, 0.05f, 8f)), 0.05f, 8f));
                    into.Add(HudProp.F("  ripple smoothness", () => d.GetF("rippleSmooth", 0f),
                        v => d.SetF("rippleSmooth", Mathf.Clamp01(v)), 0f, 1f));
                    if (SupportsAnalyticPanel)
                        into.Add(HudProp.F("  flow speed", () => d.GetF("edgeFlow", 0.22f),
                            v => d.SetF("edgeFlow", Mathf.Clamp(v, 0f, 4f)), 0f, 4f));
                }

                into.Add(HudProp.Header(SupportsAnalyticPanel
                    ? "Optical layers"
                    : "Optical layers (legacy surface: independent strengths are approximate)"));
                into.Add(HudProp.Bool("Shine sweep", () => d.GetB("customShineOn", true),
                    v => d.SetB("customShineOn", v)));
                if (d.GetB("customShineOn", true))
                    into.Add(HudProp.F("  shine strength", () => d.GetF("customShine", 0.6f),
                        v => d.SetF("customShine", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                into.Add(HudProp.Bool("Iridescent edge", () => d.GetB("customIridOn", true),
                    v => d.SetB("customIridOn", v)));
                if (d.GetB("customIridOn", true))
                    into.Add(HudProp.F("  iridescence strength", () => d.GetF("customIrid", 0.25f),
                        v => d.SetF("customIrid", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Chromatic fringe", () => d.GetB("customChromaOn", true),
                    v => d.SetB("customChromaOn", v)));
                if (d.GetB("customChromaOn", true))
                    into.Add(HudProp.F("  chromatic strength", () => d.GetF("customChroma", 0.3f),
                        v => d.SetF("customChroma", Mathf.Clamp01(v)), 0f, 1f));
                into.Add(HudProp.Bool("Frosted glass", () => d.GetB("customFrostOn", true),
                    v => d.SetB("customFrostOn", v)));
                if (d.GetB("customFrostOn", true))
                {
                    into.Add(HudProp.F("  frost strength", () => d.GetF("customFrost", 1f),
                        v => d.SetF("customFrost", Mathf.Clamp01(v)), 0f, 1f));
                    if (SupportsAnalyticPanel)
                        into.Add(HudProp.F("  frost depth", () => d.GetF("frostDepth", 1f),
                            v => d.SetF("frostDepth", Mathf.Clamp01(v)), 0f, 1f));
                }
                into.Add(HudProp.Bool("Dissolve during boot", () => d.GetB("customDissolve", true),
                    v => d.SetB("customDissolve", v)));
            }

            // These are element participation/geometry controls rather than theme values, so
            // they remain editable in both coherent modes.
            if (SupportsPanelAppearance)
            {
                into.Add(HudProp.F("Fade box ends L/R", () => d.GetF("edgeFadeX", 0f),
                    v => d.SetF("edgeFadeX", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
                into.Add(HudProp.F("Fade box top/bottom", () => d.GetF("edgeFadeY", 0f),
                    v => d.SetF("edgeFadeY", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
            }

            if (UsesCustomStyle)
            {
                into.Add(HudProp.Header("Motion & power transitions"));
                into.Add(HudProp.Bool("Death collapse", () => d.GetB("fxCollapse", true), v => d.SetB("fxCollapse", v)));
                if (d.GetB("fxCollapse", true))
                    into.Add(HudProp.F("  collapse strength", () => d.GetF("fxCollapseAmt", 1f),
                        v => d.SetF("fxCollapseAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                into.Add(HudProp.Bool("Glitch tear", () => d.GetB("fxGlitch", true), v => d.SetB("fxGlitch", v)));
                if (d.GetB("fxGlitch", true))
                    into.Add(HudProp.F("  glitch strength", () => d.GetF("fxGlitchAmt", 1f),
                        v => d.SetF("fxGlitchAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                into.Add(HudProp.Bool("Warp / curve", () => d.GetB("fxWarp", true), v => d.SetB("fxWarp", v)));
                if (d.GetB("fxWarp", true))
                    into.Add(HudProp.F("  warp strength", () => d.GetF("fxWarpAmt", 1f),
                        v => d.SetF("fxWarpAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                if (SupportsPanelAppearance)
                {
                    into.Add(HudProp.Bool("Breathing pulse", () => d.GetB("fxPulse", false), v => d.SetB("fxPulse", v)));
                    if (d.GetB("fxPulse", false))
                        into.Add(HudProp.F("  pulse strength", () => d.GetF("fxPulseAmt", 1f),
                            v => d.SetF("fxPulseAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
                }
            }
            else
                into.Add(HudProp.Header("Motion participation follows global defaults"));
            MarkProps(into, start, HudPropGroup.Effects);
        }

        private static void MarkProps(List<HudProp> props, int start, HudPropGroup group)
        {
            for (int i = Mathf.Max(0, start); i < props.Count; i++)
                if (props[i] != null) props[i].Group = group;
        }

        private void DescribeLegacyProps(List<HudProp> into)
        {
            var d = Def;
            // Geometry edits the previewed mode's layout, but only a "Both" element keeps two:
            // with the preview set to BARE these move a Both element's bare-mode position; in any
            // other preview (or for a single-mode element) they move its one live layout. EditBare
            // is called inside each closure so it tracks the live preview between frames.
            into.Add(HudProp.Anchor("Anchor", () => (int)d.AnchorFor(EditBare(d), LayoutMode), v => d.SetAnchorFor(EditBare(d), LayoutMode, (HudAnchor)v)));
            into.Add(HudProp.F("X", () => d.XFor(EditBare(d), LayoutMode), v => d.SetXFor(EditBare(d), LayoutMode, v), -2000f, 2000f));
            into.Add(HudProp.F("Y", () => d.YFor(EditBare(d), LayoutMode), v => d.SetYFor(EditBare(d), LayoutMode, v), -2000f, 2000f));
            into.Add(HudProp.F("Width", () => d.WFor(EditBare(d), LayoutMode), v => d.SetWFor(EditBare(d), LayoutMode, Mathf.Max(2f, v)), 2f, 2200f));
            into.Add(HudProp.F("Height", () => d.HFor(EditBare(d), LayoutMode), v => d.SetHFor(EditBare(d), LayoutMode, Mathf.Max(2f, v)), 2f, 1300f));
            into.Add(HudProp.F("Width % of screen (-1 = fixed)", () => d.WPctFor(EditBare(d), LayoutMode), v => d.SetWPctFor(EditBare(d), LayoutMode, v), -1f, 1f));
            into.Add(HudProp.F("Height % of screen (-1 = fixed)", () => d.HPctFor(EditBare(d), LayoutMode), v => d.SetHPctFor(EditBare(d), LayoutMode, v), -1f, 1f));
            // Z is baked into sibling order at build; re-apply it to the live panels on change,
            // or the slider silently updates the number without re-layering anything.
            into.Add(HudProp.I("Z order", () => d.Z, v => { d.Z = v; HudSystem.RequestZResort(); }, -100, 100));
            // Which mode(s) this element appears in. "Both" additionally keeps a SEPARATE location
            // per mode: with the F9 preview set to BARE, dragging a "Both" element saves its
            // bare-mode spot; in any other preview it saves its live-mode spot. Switching away from
            // "Both" drops the now-meaningless bare override.
            into.Add(HudProp.Enum("Show in mode",
                () => TiersToMode(d.Tiers),
                v => { d.Tiers = ModeToTiers(v); if (!IsBoth(d.Tiers)) d.SetBareLayout(false); },
                ShowModeNames));
            // Follow the GLOBAL box colours (edit them in F9 → Colours → "Global box colours") or
            // give this element its own. When following, the per-element colour pickers are hidden
            // because they'd have no effect.
            into.Add(HudProp.Bool("Follow global colours", () => d.GetB("followGlobal", false), v =>
            {
                bool was = d.GetB("followGlobal", false);
                d.SetB("followGlobal", v);
                // Separating (was following → now own): freeze the colours it was SHOWING (the current
                // global palette) into this element, so it stays exactly that colour until you change
                // it — no jump back to whatever the profile happened to store.
                if (was && !v) SeedColoursFromGlobal(d);
            }));
            if (!d.GetB("followGlobal", false))
            {
                into.Add(HudProp.Color("Fill", () => d.Fill, v => d.Fill = v));
                into.Add(HudProp.Color("Border", () => d.Border, v => d.Border = v));
                into.Add(HudProp.Color("Text / accent", () => d.TextColor, v => d.TextColor = v));
                // Glass follows the same flag as the colours: when this element follows global, its
                // glass follows HudConfig.GlassSheen/GlassEdge, so these per-element sliders would be
                // dead — hide them alongside the colour pickers. (-1 on a slider = follow global.)
                into.Add(HudProp.F("Glass sheen (-1 = global)", () => d.GetF("sheen", -1f), v => d.SetF("sheen", v < 0f ? -1f : Mathf.Clamp01(v)), -1f, 1f));
                into.Add(HudProp.F("Glass edge light (-1 = global)", () => d.GetF("spec", -1f), v => d.SetF("spec", v < 0f ? -1f : Mathf.Clamp01(v)), -1f, 1f));
            }
            into.Add(HudProp.F("Border width (-1 = global)", () => d.BorderWidth, v => d.BorderWidth = v, -1f, 8f));
            into.Add(HudProp.F("Edge softness / AA (-1 = global)", () => d.GetF("feather", -1f), v => d.SetF("feather", v < 0f ? -1f : Mathf.Clamp(v, 0f, 4f)), -1f, 4f));
            into.Add(HudProp.F("Corner TL (-1 = global)", () => d.RTL, v => d.RTL = v, -1f, 64f));
            into.Add(HudProp.F("Corner TR (-1 = global)", () => d.RTR, v => d.RTR = v, -1f, 64f));
            into.Add(HudProp.F("Corner BR (-1 = global)", () => d.RBR, v => d.RBR = v, -1f, 64f));
            into.Add(HudProp.F("Corner BL (-1 = global)", () => d.RBL, v => d.RBL = v, -1f, 64f));
            if (SupportsTrapezoid)
            {
                // Trapezoid shaping: a positive inset angles that edge inward (the visor-bar
                // shoulders). Same params the Box primitive uses; each supporting view forwards
                // them into its panel's SetShape, so this is where readouts/compass/etc. get taper.
                into.Add(HudProp.F("Top inset (trapezoid)", () => d.GetF("insetTop", 0f), v => d.SetF("insetTop", Mathf.Max(0f, v)), 0f, 400f));
                into.Add(HudProp.F("Bottom inset (trapezoid)", () => d.GetF("insetBottom", 0f), v => d.SetF("insetBottom", Mathf.Max(0f, v)), 0f, 400f));
            }
            into.Add(HudProp.F("Font scale", () => d.FontScale, v => d.FontScale = Mathf.Clamp(v, 0.4f, 3f), 0.4f, 3f));

            // Per-element effect opt-in + strength (the flicker family stays global in the F9
            // window). Each is a checkbox + a strength slider; default on at 1× so nothing changes
            // until you tune it. "Death collapse" is the CRT squash; turn it off for e.g. the top bar.
            into.Add(HudProp.Header("Effects (this element)"));
            // Follow the GLOBAL mesh-effect sliders (F9 → Effects), or separate this element so it
            // keeps its own. INDEPENDENT of the colour checkbox. Separating FREEZES the current
            // global values into the element (so the look doesn't jump — it stays put until you move
            // a slider); re-following resets those sliders back to "-1 = global". Derived from the
            // params, so it needs no extra stored flag. Covers: border fade / soft edge / glow (out,
            // in, width, diffuse) / edge ripple / ripple freq.
            into.Add(HudProp.Bool("Follow global effects", () => !EffectsAreSeparated(d), v =>
            {
                if (v) ResetEffectsToGlobal(d);   // follow: drop back to -1 (use the global sliders)
                else SeedEffectsFromGlobal(d);    // separate: freeze current global, then tweak below
            }));
            into.Add(HudProp.Bool("Death collapse", () => d.GetB("fxCollapse", true), v => d.SetB("fxCollapse", v)));
            into.Add(HudProp.F("  collapse strength", () => d.GetF("fxCollapseAmt", 1f),
                v => d.SetF("fxCollapseAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            into.Add(HudProp.Bool("Glitch tear", () => d.GetB("fxGlitch", true), v => d.SetB("fxGlitch", v)));
            into.Add(HudProp.F("  glitch strength", () => d.GetF("fxGlitchAmt", 1f),
                v => d.SetF("fxGlitchAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            into.Add(HudProp.Bool("Warp / curve", () => d.GetB("fxWarp", true), v => d.SetB("fxWarp", v)));
            into.Add(HudProp.F("  warp strength", () => d.GetF("fxWarpAmt", 1f),
                v => d.SetF("fxWarpAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));

            // 0.9.0 effect rows. Pulse is Tier A and opt-IN (default off — breathing is an
            // accent, not a default). Shine/iridescence/dissolve are Tier B (need the shader
            // bundle; the global master + missing-bundle fallback gate them), frost is Tier C.
            // All stored as Params-bag keys — zero enum/attribute changes (plan §7.2).
            into.Add(HudProp.Bool("Pulse (breathing)", () => d.GetB("fxPulse", false), v => d.SetB("fxPulse", v)));
            into.Add(HudProp.F("  pulse strength", () => d.GetF("fxPulseAmt", 1f),
                v => d.SetF("fxPulseAmt", Mathf.Clamp(v, 0f, 2f)), 0f, 2f));
            into.Add(HudProp.Bool("Shine sweep (Tier B)", () => d.GetB("fxShine", true), v => d.SetB("fxShine", v)));
            into.Add(HudProp.F("  shine strength", () => d.GetF("fxShineAmt", 1f),
                v => d.SetF("fxShineAmt", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.Bool("Iridescent edge (Tier B)", () => d.GetB("fxIrid", true), v => d.SetB("fxIrid", v)));
            into.Add(HudProp.F("  iridescence strength", () => d.GetF("fxIridAmt", 1f),
                v => d.SetF("fxIridAmt", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.Bool("Dissolve reveal (Tier B)", () => d.GetB("fxDissolve", true), v => d.SetB("fxDissolve", v)));
            into.Add(HudProp.Bool("Frosted glass (Tier C)", () => d.GetB("fxFrost", true), v => d.SetB("fxFrost", v)));
            into.Add(HudProp.F("  frost strength", () => d.GetF("fxFrostAmt", 1f),
                v => d.SetF("fxFrostAmt", Mathf.Clamp01(v)), 0f, 1f));
            // Concept-art trio, per-element (-1 = follow the global sliders in F9 > Effects).
            into.Add(HudProp.F("Border fade (-1=global)", () => d.GetF("bfade", -1f),
                v => d.SetF("bfade", Mathf.Clamp(v, -1f, 1f)), -1f, 1f));
            into.Add(HudProp.F("Soft edge px (-1=global)", () => d.GetF("softEdge", -1f),
                v => d.SetF("softEdge", Mathf.Clamp(v, -1f, 48f)), -1f, 48f));
            into.Add(HudProp.F("Glow out (-1=global)", () => d.GetF("glow", -1f),
                v => d.SetF("glow", Mathf.Clamp(v, -1f, 2f)), -1f, 2f));
            into.Add(HudProp.F("Glow in (-1=global)", () => d.GetF("glowIn", -1f),
                v => d.SetF("glowIn", Mathf.Clamp(v, -1f, 2f)), -1f, 2f));
            into.Add(HudProp.F("Glow width px (-1=global)", () => d.GetF("glowWidth", -1f),
                v => d.SetF("glowWidth", v < 0f ? -1f : Mathf.Clamp(v, 6f, 160f)), -1f, 160f));
            into.Add(HudProp.F("Glow diffuse (-1=global)", () => d.GetF("glowDiffuse", -1f),
                v => d.SetF("glowDiffuse", v < 0f ? -1f : Mathf.Clamp01(v)), -1f, 1f));
            into.Add(HudProp.F("Edge ripple (-1=global)", () => d.GetF("ripple", -1f),
                v => d.SetF("ripple", v < 0f ? -1f : Mathf.Clamp(v, 0f, 2.5f)), -1f, 2.5f));
            into.Add(HudProp.F("Ripple freq — low = wider (-1=global)", () => d.GetF("rippleFreq", -1f),
                v => d.SetF("rippleFreq", v < 0f ? -1f : Mathf.Clamp(v, 0.05f, 8f)), -1f, 8f));
            into.Add(HudProp.F("Ripple gradient (0=noisy, 1=smooth)", () => d.GetF("rippleSmooth", 0f),
                v => d.SetF("rippleSmooth", Mathf.Clamp01(v)), 0f, 1f));
            // Whole-box edge fade: the far ends of THIS box dissolve to transparent so a wide
            // bar melts into the visor (concept art). Fades the box/border/glow, not the text.
            into.Add(HudProp.F("Fade box ends L/R (0=off)", () => d.GetF("edgeFadeX", 0f),
                v => d.SetF("edgeFadeX", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
            into.Add(HudProp.F("Fade box top/bottom (0=off)", () => d.GetF("edgeFadeY", 0f),
                v => d.SetF("edgeFadeY", Mathf.Clamp(v, 0f, 0.5f)), 0f, 0.5f));
        }

        // ---- Follow-global helpers (the two per-element checkboxes) ----------------------------

        /// <summary>The per-element "-1 = follow global" mesh-effect params, each paired with the
        /// global slider it defers to. This is the set the "Follow global effects" checkbox governs
        /// (glass/feather/corners are handled by their own controls). Colours are separate.</summary>
        private static readonly string[] _fxKeys =
            { "bfade", "softEdge", "glow", "glowIn", "glowWidth", "glowDiffuse", "ripple", "rippleFreq" };

        private static float FxGlobal(string key)
        {
            switch (key)
            {
                case "bfade": return HudConfig.FxBorderFade != null ? HudConfig.FxBorderFade.Value : 0f;
                case "softEdge": return HudConfig.FxSoftEdge != null ? HudConfig.FxSoftEdge.Value : 0f;
                case "glow": return HudConfig.FxGlow != null ? HudConfig.FxGlow.Value : 0f;
                case "glowIn": return HudConfig.FxGlowInner != null ? HudConfig.FxGlowInner.Value : 0f;
                case "glowWidth": return HudConfig.FxGlowWidth != null ? HudConfig.FxGlowWidth.Value : 24f;
                case "glowDiffuse": return HudConfig.FxGlowDiffuse != null ? HudConfig.FxGlowDiffuse.Value : 0f;
                case "ripple": return HudConfig.FxEdgeRipple != null ? HudConfig.FxEdgeRipple.Value : 0f;
                case "rippleFreq": return HudConfig.FxEdgeRippleFreq != null ? HudConfig.FxEdgeRippleFreq.Value : 2f;
                default: return -1f;
            }
        }

        /// <summary>True when ANY of the mesh-effect params carries its own value (>= 0) rather than
        /// the "-1 = follow global" sentinel — i.e. the element has been separated from the globals.
        /// The "Follow global effects" checkbox shows the inverse of this.</summary>
        private static bool EffectsAreSeparated(HudElementDef d)
        {
            if (d == null) return false;
            for (int i = 0; i < _fxKeys.Length; i++)
                if (d.GetF(_fxKeys[i], -1f) >= 0f) return true;
            return false;
        }

        /// <summary>Separate: freeze each still-following (-1) mesh-effect param at the current global
        /// value, so the element looks identical but is now independently editable.</summary>
        private static void SeedEffectsFromGlobal(HudElementDef d)
        {
            if (d == null) return;
            for (int i = 0; i < _fxKeys.Length; i++)
                if (d.GetF(_fxKeys[i], -1f) < 0f) d.SetF(_fxKeys[i], FxGlobal(_fxKeys[i]));
        }

        /// <summary>Re-follow: drop every mesh-effect param back to -1 so it tracks the global sliders.</summary>
        private static void ResetEffectsToGlobal(HudElementDef d)
        {
            if (d == null) return;
            for (int i = 0; i < _fxKeys.Length; i++) d.SetF(_fxKeys[i], -1f);
        }

        /// <summary>Separate the colours: freeze the CURRENT global palette (what a following element
        /// shows) into this element's own refs as hex literals, so it stays that colour independently.</summary>
        private static void SeedColoursFromGlobal(HudElementDef d)
        {
            if (d == null) return;
            d.Fill = HudPalette.ToHexRef(HudPalette.PanelFill.Value);
            d.Border = HudPalette.ToHexRef(HudPalette.PanelBorder.Value);
            d.TextColor = HudPalette.ToHexRef(HudPalette.TextValue.Value);
        }

        /// <summary>Freeze the element's currently rendered theme/effect values into a complete
        /// custom snapshot before changing its source mode. The reads happen while the OLD mode
        /// is still active, so Global -> Custom and Legacy -> Custom are visually continuous.
        /// Dormant values are retained after switching back to Global, making comparison reversible.</summary>
        private void SeedCustomStyleFromEffective(HudElementDef d)
        {
            if (d == null) return;

            d.Fill = HudPalette.ToHexRef(FillColor());
            d.Border = HudPalette.ToHexRef(BorderColor());
            d.TextColor = HudPalette.ToHexRef(TextColor());
            d.BorderWidth = BorderWidthFor();
            d.RTL = Radius(d.RTL);
            d.RTR = Radius(d.RTR);
            d.RBR = Radius(d.RBR);
            d.RBL = Radius(d.RBL);
            d.SetF("feather", EffectiveFeatherFor());
            d.SetF("sheen", GlassSheenFor());

            // Custom stores the final visible edge-light strength. This includes the current
            // global Tier-A boost when Global/Legacy was the source, then stops tracking it.
            d.SetF("spec", GlassEdgeFor());

            d.SetF("squircle", SdfSquircleFor());
            d.SetB("gaussianHalo", SdfGaussianFor());
            d.SetB("customBorderFadeOn", StyleFeatureOn("customBorderFadeOn", HudConfig.FxBorderFadeOn));
            d.SetB("customSoftEdgeOn", StyleFeatureOn("customSoftEdgeOn", HudConfig.FxSoftEdgeOn));
            d.SetB("customGlowOn", StyleFeatureOn("customGlowOn", HudConfig.FxGlowOn));
            d.SetB("customRippleOn", StyleFeatureOn("customRippleOn", HudConfig.FxEdgeLightOn));
            d.SetF("bfade", OwnOrGlobal("bfade", HudConfig.FxBorderFade));
            d.SetF("softEdge", OwnOrGlobal("softEdge", HudConfig.FxSoftEdge));
            d.SetF("glow", OwnOrGlobal("glow", HudConfig.FxGlow));
            d.SetF("glowIn", OwnOrGlobal("glowIn", HudConfig.FxGlowInner));
            d.SetF("glowWidth", OwnOrGlobal("glowWidth", HudConfig.FxGlowWidth));
            d.SetF("glowDiffuse", OwnOrGlobal("glowDiffuse", HudConfig.FxGlowDiffuse));
            d.SetF("ripple", OwnOrGlobal("ripple", HudConfig.FxEdgeRipple));
            d.SetF("rippleFreq", OwnOrGlobal("rippleFreq", HudConfig.FxEdgeRippleFreq));
            d.SetF("rippleSmooth", UsesGlobalStyle ? 0f : d.GetF("rippleSmooth", 0f));
            d.SetF("edgeFlow", EdgeFlowFor());
            d.SetF("frostDepth", FrostDepthFor());

            bool globalShineOn = HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            float globalShine = HudConfig.FxShine != null ? HudConfig.FxShine.Value : 0f;
            bool globalIridOn = HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            float globalIrid = HudConfig.FxIridescence != null ? HudConfig.FxIridescence.Value : 0f;
            bool globalChromaOn = HudConfig.FxChromaOn != null && HudConfig.FxChromaOn.Value;
            float globalChroma = HudConfig.FxChroma != null ? HudConfig.FxChroma.Value : 0f;
            float globalFrost = HudConfig.FrostStrength != null ? HudConfig.FrostStrength.Value : 1f;
            bool fromGlobal = UsesGlobalStyle;
            bool fromCustom = UsesCustomStyle;

            d.SetB("customShineOn", fromCustom ? d.GetB("customShineOn", globalShineOn)
                : globalShineOn && (fromGlobal || d.GetB("fxShine", true)));
            d.SetF("customShine", fromCustom
                ? Mathf.Clamp(d.GetF("customShine", globalShine), 0f, 2f)
                : Mathf.Clamp(globalShine * (fromGlobal ? 1f
                    : Mathf.Clamp01(d.GetF("fxShineAmt", 1f))), 0f, 2f));
            d.SetB("customIridOn", fromCustom ? d.GetB("customIridOn", globalIridOn)
                : globalIridOn && (fromGlobal || d.GetB("fxIrid", true)));
            d.SetF("customIrid", fromCustom ? Mathf.Clamp01(d.GetF("customIrid", globalIrid))
                : Mathf.Clamp01(globalIrid * (fromGlobal ? 1f
                    : Mathf.Clamp01(d.GetF("fxIridAmt", 1f)))));
            d.SetB("customChromaOn", fromCustom ? d.GetB("customChromaOn", globalChromaOn)
                : globalChromaOn && (fromGlobal || d.GetB("fxChroma", true)));
            d.SetF("customChroma", fromCustom ? Mathf.Clamp01(d.GetF("customChroma", globalChroma))
                : Mathf.Clamp01(globalChroma * (fromGlobal ? 1f
                    : Mathf.Clamp01(d.GetF("fxChromaAmt", 1f)))));
            d.SetB("customFrostOn", fromCustom ? d.GetB("customFrostOn", true)
                : fromGlobal || d.GetB("fxFrost", true));
            d.SetF("customFrost", fromCustom ? Mathf.Clamp01(d.GetF("customFrost", globalFrost))
                : Mathf.Clamp01(globalFrost * (fromGlobal ? 1f
                    : Mathf.Clamp01(d.GetF("fxFrostAmt", 1f)))));
            bool dissolve = HudConfig.FxDissolveBoot != null && HudConfig.FxDissolveBoot.Value;
            d.SetB("customDissolve", fromCustom ? d.GetB("customDissolve", dissolve)
                : dissolve && (fromGlobal || d.GetB("fxDissolve", true)));
            if (fromGlobal)
            {
                d.SetB("fxCollapse", true);
                d.SetF("fxCollapseAmt", 1f);
                d.SetB("fxGlitch", true);
                d.SetF("fxGlitchAmt", 1f);
                d.SetB("fxWarp", true);
                d.SetF("fxWarpAmt", 1f);
                d.SetB("fxPulse", false);
                d.SetF("fxPulseAmt", 1f);
            }

            // Readout bar refs have palette-dependent empty defaults. Freeze their currently
            // rendered colours as literals so Custom truly stops tracking the global palette.
            if (d.Type == HudElementType.Readout)
            {
                d.Set("barFill", HudPalette.ToHexRef(GlobalOr(d.GetS("barFill", ""), HudPalette.Good.Value)));
                d.Set("barWarn", HudPalette.ToHexRef(GlobalOr(d.GetS("barWarn", ""), HudPalette.Warn.Value)));
                d.Set("barCrit", HudPalette.ToHexRef(GlobalOr(d.GetS("barCrit", ""), HudPalette.Critical.Value)));
                d.Set("barTrack", HudPalette.ToHexRef(GlobalOr(d.GetS("barTrack", ""), HudPalette.PanelBorder.Value)));
                d.Set("barTarget", HudPalette.ToHexRef(GlobalOr(d.GetS("barTarget", ""), HudPalette.TextValue.Value)));
            }
            d.SetB("followGlobal", false);
            d.SetB("customStyleReady", true);
        }

        /// <summary>One-click bulk style-source operation used by F9. Customisation freezes each
        /// element's own currently effective look through the same path as the inspector, rather
        /// than merely flipping a flag and revealing stale profile values.</summary>
        internal void SetUnifiedStyleSource(bool followGlobal, bool forceCustomSnapshot = false)
        {
            if (Def == null) return;
            if (!followGlobal && (forceCustomSnapshot
                || (StyleSource != StyleCustom && !Def.GetB("customStyleReady", false))))
                SeedCustomStyleFromEffective(Def);
            Def.SetI("styleSource", followGlobal ? StyleGlobal : StyleCustom);
        }

        /// <summary>Document-level counterpart for elements whose view is unavailable (different
        /// tier, failed build, or HUD canvas not present). It resolves the same stored/global
        /// contract without touching Unity objects, so bulk F9 actions never skip definitions.</summary>
        internal static void SetUnifiedStyleSourceWithoutView(HudElementDef d, bool followGlobal,
            bool forceCustomSnapshot = false)
        {
            if (d == null) return;
            if (followGlobal)
            {
                d.SetI("styleSource", StyleGlobal);
                return;
            }

            int source = StyleSourceOf(d);
            if (!forceCustomSnapshot && source != StyleCustom && d.GetB("customStyleReady", false))
            {
                d.SetI("styleSource", StyleCustom);
                return;
            }
            if (!forceCustomSnapshot && source == StyleCustom) return;

            bool sourceGlobal = source == StyleGlobal;
            bool sourceCustom = source == StyleCustom;
            bool followsColours = sourceGlobal || (!sourceCustom && d.GetB("followGlobal", false));
            Color fill = followsColours ? HudPalette.PanelFill.Value
                : HudPalette.Resolve(d.Fill, HudPalette.PanelFill.Value);
            Color border = followsColours ? HudPalette.PanelBorder.Value
                : HudPalette.Resolve(d.Border, HudPalette.PanelBorder.Value);
            Color text = followsColours ? HudPalette.TextValue.Value
                : HudPalette.Resolve(d.TextColor, HudPalette.TextValue.Value);
            d.Fill = HudPalette.ToHexRef(fill);
            d.Border = HudPalette.ToHexRef(border);
            d.TextColor = HudPalette.ToHexRef(text);

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
            d.SetB("customBorderFadeOn", sourceCustom
                ? d.GetB("customBorderFadeOn", borderFadeGlobal) : borderFadeGlobal);
            d.SetB("customSoftEdgeOn", sourceCustom
                ? d.GetB("customSoftEdgeOn", softEdgeGlobal) : softEdgeGlobal);
            d.SetB("customGlowOn", sourceCustom ? d.GetB("customGlowOn", glowGlobal) : glowGlobal);
            d.SetB("customRippleOn", sourceCustom ? d.GetB("customRippleOn", rippleGlobal) : rippleGlobal);
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
            d.SetB("customDissolve", sourceCustom ? d.GetB("customDissolve", dissolve)
                : dissolve && (sourceGlobal || d.GetB("fxDissolve", true)));
            if (sourceGlobal)
            {
                d.SetB("fxCollapse", true);
                d.SetF("fxCollapseAmt", 1f);
                d.SetB("fxGlitch", true);
                d.SetF("fxGlitchAmt", 1f);
                d.SetB("fxWarp", true);
                d.SetF("fxWarpAmt", 1f);
                d.SetB("fxPulse", false);
                d.SetF("fxPulseAmt", 1f);
            }

            if (d.Type == HudElementType.Readout)
            {
                d.Set("barFill", HudPalette.ToHexRef(SnapshotColor(d, source, "barFill", HudPalette.Good.Value)));
                d.Set("barWarn", HudPalette.ToHexRef(SnapshotColor(d, source, "barWarn", HudPalette.Warn.Value)));
                d.Set("barCrit", HudPalette.ToHexRef(SnapshotColor(d, source, "barCrit", HudPalette.Critical.Value)));
                d.Set("barTrack", HudPalette.ToHexRef(SnapshotColor(d, source, "barTrack", HudPalette.PanelBorder.Value)));
                d.Set("barTarget", HudPalette.ToHexRef(SnapshotColor(d, source, "barTarget", HudPalette.TextValue.Value)));
            }

            d.SetB("followGlobal", false);
            d.SetB("customStyleReady", true);
            d.SetI("styleSource", StyleCustom);
        }

        private static float SnapshotFloat(HudElementDef d, int source, string key, float global)
        {
            if (source == StyleGlobal) return global;
            float own = d.GetF(key, -1f);
            return own >= 0f ? own : global;
        }

        private static Color SnapshotColor(HudElementDef d, int source, string key, Color global)
        {
            bool follows = source == StyleGlobal
                || (source == StyleLegacy && d.GetB("followGlobal", false));
            return follows ? global : HudPalette.Resolve(d.GetS(key, ""), global);
        }

        /// <summary>Effective per-element strength for an effect: 0 when its checkbox is off,
        /// else the slider value. Shared by the glitch, collapse and warp wiring.</summary>
        internal float EffectAmt(string key, string amtKey)
            => Def != null && (UsesGlobalStyle || Def.GetB(key, true))
                ? (UsesGlobalStyle ? 1f : Def.GetF(amtKey, 1f)) : 0f;

        /// <summary>Push this element's fxWarp strength onto its child warp components. Called on
        /// (re)layout — the surrounding DirtyAllMeshes re-meshes so the new bend takes effect.</summary>
        internal void ApplyWarpMult()
        {
            if (Root == null) return;
            float mult = EffectAmt("fxWarp", "fxWarpAmt");
            var vw = Root.GetComponentsInChildren<VisorWarp>(true);
            for (int i = 0; i < vw.Length; i++) vw[i].StrengthMult = mult;
            var tw = Root.GetComponentsInChildren<TmpWarp>(true);
            for (int i = 0; i < tw.Length; i++) tw[i].StrengthMult = mult;
        }

        /// <summary>Default editor target: the element's own rect. Views with richer
        /// internals may add more.</summary>
        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            var c = CenterForLogical(scale);
            var s = SizeFor(scale);
            into.Add(new HudEditTarget
            {
                Title = Def.Type.ToString(),
                Palette = System.Array.Empty<string>(),
                Values = System.Array.Empty<ConfigEntryBase>(),
                CanvasRect = new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y),
            });
        }
    }
}
