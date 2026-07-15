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

        /// <summary>Per-corner radius with the −1 = "global CornerRadius" convention.</summary>
        protected static float Radius(float perCorner)
            => perCorner >= 0f ? perCorner
             : (HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f);

        protected float BorderWidthFor()
            => Def.BorderWidth >= 0f ? Def.BorderWidth
             : (HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f);

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

        /// <summary>When true the element ignores its OWN colour refs and draws the GLOBAL box
        /// colours (the shared HudPalette entries), so editing those in F9 re-tints every following
        /// element at once. Off = the element keeps its own Fill/Border/Text (and bar) colours.</summary>
        protected bool FollowGlobal => Def != null && Def.GetB("followGlobal", false);

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
            into.Add(HudProp.Bool("Drop: light whole box", () => d.GetB("dropWholeBox", false), v => d.SetB("dropWholeBox", v)));
            into.Add(HudProp.Color("Drop highlight colour", () => d.GetS("dropHiColor", ""), v => d.Set("dropHiColor", v)));
        }

        /// <summary>The element's effective glass sheen. An element set to "Follow global colours"
        /// also follows the GLOBAL glass — ignoring its own baked "sheen" — so the one global slider
        /// (and the "Make ALL boxes follow global" button) drives it just like the box colours do.
        /// Otherwise it uses its own value, with the "-1 = global" convention (matching corner radius
        /// and border width) as a per-param opt-in.</summary>
        protected float GlassSheenFor()
        {
            float global = HudConfig.GlassSheen != null ? HudConfig.GlassSheen.Value : 0f;
            if (FollowGlobal) return global;
            float v = Def.GetF("sheen", -1f);
            return v >= 0f ? v : global;
        }

        /// <summary>The element's effective glass edge light. Follows the global when this element
        /// follows global colours, else its own value with the "-1 = global" fallback. See
        /// <see cref="GlassSheenFor"/>.</summary>
        protected float GlassEdgeFor()
        {
            float global = HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f;
            float v = FollowGlobal ? global : Def.GetF("spec", -1f);
            float baseSpec = v >= 0f ? v : global;
            // Tier A's edge-light knob boosts the PANEL border light too (play-test: the
            // slider only drove drawn lines, which most HUDs barely use — the "brighter and
            // darker along the border" run lives in PanelGraphic.BorderAt and this is its
            // volume). Every widget flows through here (incl. hand/equipment boxes' direct
            // Spec sets), so one slider lights the whole HUD's edges.
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
        protected void ApplyGlass(PanelGraphic g)
        {
            if (g == null) return;
            g.Sheen = GlassSheenFor();
            g.Spec = GlassEdgeFor();
            ApplyMeshFx(g);
        }

        /// <summary>The Tier A mesh-effect push WITHOUT the sheen/spec pair: the concept-art
        /// trio (border fade / soft edge / glow in+out) + edge shimmer, per-element overrides
        /// with the "-1 = follow global" convention, everything dirty-guarded in the graphic.
        /// Split out of <see cref="ApplyGlass"/> because widgets that style sheen/spec by hand
        /// (hand boxes' active-accent logic, equipment column) MUST still receive the mesh
        /// effects — calling only ApplyFx left the hand/1-6 boxes inert for every 0.9.0 mesh
        /// feature (play-test round 11; same shape as the round-1 ApplyFx split). Tier A off
        /// = everything 0 = classic 0.8.0 output.</summary>
        protected void ApplyMeshFx(PanelGraphic g)
        {
            if (g == null) return;
            bool tierA = HudConfig.FxTierA != null && HudConfig.FxTierA.Value;
            bool bfOn = tierA && HudConfig.FxBorderFadeOn != null && HudConfig.FxBorderFadeOn.Value;
            bool seOn = tierA && HudConfig.FxSoftEdgeOn != null && HudConfig.FxSoftEdgeOn.Value;
            bool glOn = tierA && HudConfig.FxGlowOn != null && HudConfig.FxGlowOn.Value;
            g.BorderFade = bfOn ? OwnOrGlobal("bfade", HudConfig.FxBorderFade) : 0f;
            g.SoftEdge = seOn ? OwnOrGlobal("softEdge", HudConfig.FxSoftEdge) : 0f;
            g.Glow = glOn ? OwnOrGlobal("glow", HudConfig.FxGlow) : 0f;
            g.GlowInner = glOn ? OwnOrGlobal("glowIn", HudConfig.FxGlowInner) : 0f;
            g.GlowWidth = HudConfig.FxGlowWidth != null ? HudConfig.FxGlowWidth.Value : 24f;
            g.GlowDiffuse = HudConfig.FxGlowDiffuse != null ? HudConfig.FxGlowDiffuse.Value : 0f;
            bool rippleOn = tierA && HudConfig.FxEdgeLightOn != null && HudConfig.FxEdgeLightOn.Value;
            g.EdgeRipple = rippleOn && HudConfig.FxEdgeRipple != null ? HudConfig.FxEdgeRipple.Value : 0f;
            g.EdgeRippleFreq = HudConfig.FxEdgeRippleFreq != null ? HudConfig.FxEdgeRippleFreq.Value : 2f;

            ApplyFx(g);
        }

        /// <summary>Sheen-pattern resolve: the element's own param when >= 0, else the global.</summary>
        protected float OwnOrGlobal(string key, ConfigEntry<float> global)
        {
            float v = Def != null ? Def.GetF(key, -1f) : -1f;
            return v >= 0f ? v : (global != null ? global.Value : 0f);
        }

        /// <summary>The 0.9.0 half of panel styling: per-element strength (uv0.x) + shared
        /// effect-material selection. Split from <see cref="ApplyGlass"/> so widgets that set
        /// Sheen/Spec directly with state overrides (hand boxes, equipment column — play-test:
        /// "no effects on the hand/1-6 slots") can opt their boxes into effects without
        /// disturbing their custom glass logic.</summary>
        protected void ApplyFx(PanelGraphic g)
        {
            if (g == null) return;
            g.FxStrength = FxStrengthFor();

            // Material selection (one per graphic; frost wins over edge-fx): all shared
            // materials, so batching holds per family; Assign/Unassign are per-frame cheap
            // (dictionary lookups) and only churn on an actual toggle. Falls through to the
            // default UI material whenever the bundle/tier is off — the Tier A look.
            bool frost = HudBackdrop.Active
                && HudConfig.FxTierC != null && HudConfig.FxTierC.Value
                && Def != null && Def.GetB("fxFrost", true) && Def.GetF("fxFrostAmt", 1f) > 0.001f;
            bool edgeFx = !frost
                && HudConfig.FxTierB != null && HudConfig.FxTierB.Value
                && Core.HudShaderStore.TierBAvailable
                && Def != null
                && (Def.GetB("fxShine", true) || Def.GetB("fxIrid", true) || Def.GetB("fxDissolve", true));
            if (frost) { if (!HudFxMaterials.Assign(g, "glass")) HudFxMaterials.Unassign(g); }
            else if (edgeFx) { if (!HudFxMaterials.Assign(g, "edgefx")) HudFxMaterials.Unassign(g); }
            else HudFxMaterials.Unassign(g);
        }

        /// <summary>Per-element Tier B modulation (0..1) baked into uv0.x on rebuild: the max of
        /// the enabled per-element shader-effect strengths, 0 when Tier B is off/unavailable.
        /// One scalar by design — the SHARED material carries the per-effect globals; uv0.x is
        /// the element's volume knob (master-plan §12.2/§12.3).</summary>
        protected float FxStrengthFor()
        {
            if (Def == null) return 0f;
            // PER-TIER gating (play-test bug: frost is Tier C — gating everything on Tier B
            // made "C on, B off" bake uv0.x = 0, so the frost shader multiplied by zero and
            // Tier C alone did nothing).
            bool tierB = HudConfig.FxTierB != null && HudConfig.FxTierB.Value;
            bool shineOn = tierB && HudConfig.FxShineOn != null && HudConfig.FxShineOn.Value;
            bool iridOn = tierB && HudConfig.FxIridOn != null && HudConfig.FxIridOn.Value;
            float shine = shineOn && Def.GetB("fxShine", true) ? Mathf.Clamp01(Def.GetF("fxShineAmt", 1f)) : 0f;
            float irid = iridOn && Def.GetB("fxIrid", true) ? Mathf.Clamp01(Def.GetF("fxIridAmt", 1f)) : 0f;
            float frost = (HudConfig.FxTierC != null && HudConfig.FxTierC.Value
                && Def.GetB("fxFrost", true)) ? Mathf.Clamp01(Def.GetF("fxFrostAmt", 1f)) : 0f;
            return Mathf.Max(shine, Mathf.Max(irid, frost));
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
            into.Add(HudProp.Bool("Follow global colours", () => d.GetB("followGlobal", false), v => d.SetB("followGlobal", v)));
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
        }

        /// <summary>Effective per-element strength for an effect: 0 when its checkbox is off,
        /// else the slider value. Shared by the glitch, collapse and warp wiring.</summary>
        internal float EffectAmt(string key, string amtKey)
            => Def != null && Def.GetB(key, true) ? Def.GetF(amtKey, 1f) : 0f;

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
