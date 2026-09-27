using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// The folder-tab art system for the 1:1 concept match (2026-09-26 geometry round): the
    /// colour recipes, the custom tab contour mesh and the collar band mesh that
    /// <see cref="UiaComposite.FolderTabs"/> assembles. Everything here exists because the
    /// measured concept outgrew PanelGraphic trapezoids: the selected tab is ONE filled path
    /// from its raised top down THROUGH the collar band (no bottom edge, its gradient ending
    /// on the band's own body colour so tab and band read as one surface), unselected tabs
    /// tuck UNDER the band, and the band's bright top line breaks at the selected tab's
    /// fillets.
    /// </summary>
    internal static class UiaFolderPalette
    {
        // ---- derivation helpers ----------------------------------------------------------
        // Every colour is an (hue-shift, saturation-scale, value-scale) recipe against a LIVE
        // theme anchor — warm tones against the accent (UiaTheme.Selected), cool tones
        // against the cyan accent (UiaTheme.Accent). The parameters were FITTED to the
        // concept's pixel-sampled RGBs so the shipped themes reproduce each sample within
        // ~5%, while any other theme re-tints the whole folder system coherently in its own
        // hue family. The sampled target is quoted beside each recipe.

        private static Color FromWarm(float hueShiftDeg, float sMul, float vMul)
        {
            float h, s, v;
            Color.RGBToHSV(UiaTheme.Selected, out h, out s, out v);
            Color c = Color.HSVToRGB(Mathf.Repeat(h + hueShiftDeg / 360f, 1f),
                Mathf.Clamp01(s * sMul), Mathf.Clamp01(v * vMul));
            c.a = 1f;
            return c;
        }

        private static Color FromCool(float hueShiftDeg, float sMul, float vMul)
        {
            float h, s, v;
            Color.RGBToHSV(UiaTheme.Accent, out h, out s, out v);
            Color c = Color.HSVToRGB(Mathf.Repeat(h + hueShiftDeg / 360f, 1f),
                Mathf.Clamp01(s * sMul), Mathf.Clamp01(v * vMul));
            c.a = 1f;
            return c;
        }

        // ---- selected tab (both strips) --------------------------------------------------
        internal static Color TabFillTop => FromWarm(2f, 1.05f, 0.77f);     // (197,109,23)
        internal static Color TabFillMid => FromWarm(1f, 0.95f, 0.59f);     // (150,88,30)
        internal static Color TabFillBot => FromWarm(9f, 0.68f, 0.39f);     // (100,78,43) == band body
        internal static Color TabRimTop => FromWarm(10f, 0.92f, 0.90f);     // (230,166,53) golden
        internal static Color TabRimSide => FromWarm(2f, 1.06f, 0.82f);     // (209,115,23)

        // ---- collar band family, REBASED onto the tab's own ramp -------------------------
        // FlorpyDorp: the frame must read as the SAME MATERIAL as the tab ("I prefer the
        // color of the tab over the border color") — every band/frame tone is now a cut of
        // the tab gradient (saturated warm amber), not the old desaturated olive. O's
        // lattice seams read these same names, so they follow automatically.
        internal static Color BandLine => Color.Lerp(TabFillTop, TabRimTop, 0.45f);     // bright tab-amber line
        internal static Color BandBodyTop => Color.Lerp(TabFillMid, TabFillBot, 0.5f);  // the tab's lower-mid amber
        internal static Color BandBodyBot => TabFillBot;                                // == the tab gradient's end
        internal static Color BandLip => Color.Lerp(TabFillBot, TabStrokeTop, 0.25f);   // slightly lifted amber

        // ---- main strip deltas (dimmer body, brighter line — same tab family) ------------
        internal static Color MainBandLine => Color.Lerp(TabFillTop, TabRimTop, 0.6f);
        internal static Color MainBandBodyTop => Scale(BandBodyTop, 0.78f);
        internal static Color MainBandBodyBot => Scale(BandBodyBot, 0.78f);
        internal static Color MainBandLip => Scale(BandLip, 0.85f);

        private static Color Scale(Color c, float k)
        {
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        // ---- unselected tabs -------------------------------------------------------------
        internal static Color SubTabFillTop => FromCool(-3f, 1.18f, 0.30f);     // (19,59,68)
        internal static Color SubTabFillBot => FromCool(7f, 1.17f, 0.20f);      // (13,35,46)
        internal static Color SubTabRim => FromCool(-3f, 0.98f, 0.57f);         // (52,116,130)
        internal static Color MainTabFillTop => FromCool(2f, 1.11f, 0.23f);     // (17,44,53)
        internal static Color MainTabFillBot => FromCool(-3f, 1.39f, 0.12f);    // (4,23,27)
        internal static Color MainTabRim => FromCool(-6f, 0.88f, 0.765f);       // (81,164,176)

        // ---- collar side/bottom rails (the vertical gold gradient discipline) -----------
        // Kept for O's lattice seams, which colour-match against these names.
        internal static Color RailLineTop => FromWarm(10f, 0.58f, 0.77f);       // (196,161,100)
        internal static Color RailLineBot => FromWarm(9f, 0.51f, 0.42f);        // (107,89,61)
        internal static Color RailBodyTop => FromWarm(11f, 0.66f, 0.39f);       // (100,80,45)
        internal static Color RailBodyBot => FromWarm(40f, 0.31f, 0.22f);       // (55,57,42) sage
        internal static Color RailLip => FromWarm(18f, 0.42f, 0.385f);          // (98,90,63)
        internal static Color RailDarkRim => FromWarm(12f, 0.51f, 0.137f);      // (35,30,20)

        // ---- the ONE-PIECE frame ring, REBASED onto the tab ramp -------------------------
        // FrameTop is tuned so the band's TOP row equals the tab fill AT band-top level
        // (t ~= 0.62 between the tab's mid and bottom stops) — the tab/band boundary is
        // seam-free BY CONSTRUCTION: identical colour on both sides of the flare. FrameBot
        // is the tab's end stop darkened ~20% with only MILD desaturation, so the whole
        // ring keeps the tab's warmth instead of the old border olive.
        internal static Color FrameTop => Color.Lerp(TabFillMid, TabFillBot, 0.62f);
        internal static Color FrameBot
        {
            get
            {
                Color c = TabFillBot;
                float g = (c.r + c.g + c.b) / 3f;
                c = Color.Lerp(c, new Color(g, g, g, 1f), 0.15f);   // mild desat only
                return new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 1f);
            }
        }
        // The selected tab's side strokes: lighter/warmer at the tab top, ending on the
        // band line's own tone (identical colour where the stroke hands off to the line).
        internal static Color TabStrokeTop => FromWarm(10f, 0.62f, 0.86f);      // ~(219,177,105)
        internal static Color TabStrokeBot => Color.Lerp(TabFillTop, TabRimTop, 0.45f);   // == BandLine

        // ---- labels (neutral tokens, per the type spec) ----------------------------------
        internal static Color SelectedLabel => new Color(0.988f, 0.973f, 0.910f); // warm white (252,248,232)
        internal static Color RestingLabel => new Color(0.922f, 0.933f, 0.933f, 0.9f); // (235,238,238)@90%

        // ---- mode chips (SMART STOW MODE Simple/Complex, via SegChip) --------------------
        internal static Color ChipActiveBody => FromWarm(-2f, 1.05f, 0.58f);    // (147,74,17)
        internal static Color ChipActiveRim => FromWarm(0f, 1.10f, 0.77f);      // (196,98,14)
        internal static Color ChipRestFill => FromCool(0f, 0.91f, 0.11f);       // (11,22,25)
        internal static Color ChipRestBorder => FromCool(-1f, 0.83f, 0.59f);    // ~(74,135,150) top value
        // Concept round (2026-09-26): the selected chip's HAIRLINE rim is a lighter orange than
        // its old 1.5px rim — the tab's golden rim pulled 40% into the chip rim, ~(210,125,30)
        // on the shipped accent — so a ~1px line still reads as a lit edge on the orange body.
        internal static Color ChipActiveRimLight => Color.Lerp(ChipActiveRim, TabRimTop, 0.4f);
        // The faint hairline joining neighbouring chips across their gap (the concept's
        // "connecting line"): the resting rim at ~35% alpha.
        internal static Color ChipBridge
        {
            get { Color c = ChipRestBorder; c.a = 0.35f; return c; }
        }
        // Chip labels, both states: neutral white (238,241,243) — NOT the warm SelectedLabel
        // (FlorpyDorp: text "should just be white", never a yellow cast).
        internal static Color ChipLabel => new Color(0.933f, 0.945f, 0.953f, 1f);
    }

    /// <summary>
    /// The ONE-PIECE frame ring (collar round 2): band + side rails + bottom rail as a
    /// SINGLE closed mesh, so there are no joints, no mitres and no square strip ends —
    /// the frame "curves around the menus" by construction. The outer silhouette has
    /// rounded corners (r~10); the inner cutout has LARGER rounded corners (r~14, the
    /// concept's content-panel curve); the top member is the collar band (its thickness),
    /// the sides and bottom their own thicknesses.
    ///
    /// <para>Shading is the concept's SOFT gold surface, not line-art: a vertical ramp
    /// (FrameTop family at the top to FrameBot at the bottom) modulated ACROSS the ring's
    /// width — slightly darker outer rim, lifted crown, then a soft AO fade on the inner
    /// edge where the gold meets the dark panel (the recessed look). Both silhouettes carry
    /// baked alpha fringes. NO bright line anywhere (FlorpyDorp's closer: "get rid of the
    /// bold orange line") — the tab/band junction is carried entirely by the colour-matched
    /// gradient stop and the fillet feet.</para>
    ///
    /// <para>Built as concentric sampled contours (identical point structure per contour,
    /// quad strips between them); static scratch lists, so a rebuild allocates nothing.
    /// Z: drawn OVER the unselected tabs' tucked bottoms, UNDER the selected tab.</para>
    ///
    /// <para>MOVING EDGE LIGHT (2026-09-26, "make it do it on all the lines"): this is a
    /// custom mesh, so it cannot ride the analytic sdfglass sweep the tabs use (that shader
    /// reads a packed parameter mesh). It takes the MESH-PATH twin instead — the shared
    /// <c>edgefx</c> material (HudEdgeFX.shader §e), fed a per-vertex uv1 payload of
    /// (contour arc length, weight x amplitude, ripple freq, flow speed) exactly like
    /// PolygonPanelGraphic's flowing Shapes. The wave lives on the band's CROWN rows only
    /// (a soft light travelling clockwise ALONG the gold, never the whole band strobing):
    /// the outer/top-most row, the AO row and both fringes carry weight 0, and on the TOP
    /// member the upper crown row is zeroed too, so the rows the selected tab's fill and its
    /// <see cref="TabFeetGraphic"/> feet abut never pulse (the seam-free junction holds and
    /// the feet stay static on the default material). One arc seam is unavoidable: it sits at
    /// the BOTTOM-CENTRE of the ring, where the weight tapers to 0 over ~40px so the phase
    /// wrap is invisible. The crossbar's arc is borrowed from the OUTER ring at its two tees
    /// (see <see cref="ArcAt"/>) so the phase is continuous where it melts into the rails.
    /// Gated off (no bundle / pre-flow bundle / HUD FX clock down / Tier A or edge light off
    /// / amplitude or speed 0 / canvas without the uv1 channel) = default material + all-zero
    /// uv1: exactly the static look. uv0.x (FxStrength) stays 0 so the family's shine and
    /// iridescence never touch the gold.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class FolderFrameGraphic : MaskableGraphic, UI.Hud.IHudFxGraphic
    {
        private Color _rampTop, _rampBot;
        private float _topTh = 10f, _sideTh = 12f, _botTh = 10f;
        // Ring-topology fix: a SUB strip draws NO ring of its own (nesting two rings
        // doubled every rail into a grooved pair) — only its band, as a horizontal
        // CROSSBAR whose ends overhang the rect to melt into the OUTER ring's side rails.
        private bool _crossbar;
        private float _overhang;
        private const float ROut = 10f;            // outer silhouette corner radius
        private const float RIn = 14f;             // inner cutout corner radius
        private const float Fringe = 1f;

        // The cross-ring shading stops: fraction of the member thickness, colour multiplier,
        // alpha. Outer rim gently dark (seam round: 0.85 -> 0.95 — the band's top row abuts
        // the tab fill, and the harder rim made a luminance ledge there), lifted crown,
        // then the inner AO into the panel.
        private static readonly float[] StopF = { 0f, 0.3f, 0.7f, 1f };
        private static readonly float[] StopM = { 0.95f, 1.05f, 0.95f, 0.52f };

        // ---- moving edge light (mesh path) ---------------------------------------------
        // Per-stop wave weight on the RAILS: the crown rows (0.3 / 0.7) carry the light, the
        // outer row and the inner AO row carry none (fringes are always 0), so the light is
        // a soft ~5-8px band that fades to nothing toward both silhouettes. On the TOP member
        // the 0.3 row is additionally faded to 0 (see TopnessAt) — the band's upper rows under
        // the selected tab and its feet stay perfectly still, so the tab/band colour match
        // can never flicker into a seam.
        private static readonly float[] StopW = { 0f, 1f, 1f, 0f };
        // The crossbar's rows == the ring's top-member profile (0 / 0 / 1 / 0), so a
        // sub-strip band reads as the same light as the main band it echoes.
        private static readonly float[] CrossW = { 0f, 0f, 1f, 0f };
        // Theme amount -> effective shader amplitude. The shipped theme's EdgeRipple is
        // 2.27, tuned for a hairline; on a 10-12px gold surface that would blow the band out
        // to white / crush it to black. Peak |wave| is ~0.70, so FlowMaxAmp 0.4 caps the
        // brightness swing at ~+-28% (2.27 -> 0.34 -> ~+-24%) and the alpha dip at ~14% —
        // it stays gold (the wave is multiplicative, hue-preserving).
        private const float FlowGain = 0.15f;
        private const float FlowMaxAmp = 0.4f;
        // Arc distance from the bottom-centre seam over which the weight ramps 0 -> 1.
        private const float SeamTaper = 40f;

        private float _fxStrength;           // uv0.x — never set by the kit; 0 = no shine/irid
        private bool _flowOn;                // payload baked + edgefx assigned

        /// <summary>Opt-in moving light along the gold band. Off by default: FlorpyDorp
        /// (2026-09-26) keeps the animation to the manila tabs, the window exterior and the
        /// title-bar row; the frame stays the static soft-shaded gold.</summary>
        internal bool Flow { get; set; }
        private float _flowAmp, _flowFreq = 2f, _flowSpeed;

        // The crown-centre reference contour + its cumulative arc (px), rebuilt with the
        // mesh. Per instance (not static) because a sub strip's crossbar reads the OUTER
        // ring's copy between rebuilds. The arc is shared by every contour's vertex i, so
        // wave fronts run straight across the band instead of shearing at the corners.
        private readonly System.Collections.Generic.List<Vector2> _refPts =
            new System.Collections.Generic.List<Vector2>(40);
        private readonly System.Collections.Generic.List<float> _refArc =
            new System.Collections.Generic.List<float>(40);
        private bool _refValid;

        // Crossbar only: the outer ring this band tees into, and the ring's arc at the two
        // tees (continuous phase at the junction). Found once; re-searched at most every 2s.
        private FolderFrameGraphic _ring;
        private float _nextRingSearch;
        private bool _teeValid;
        private float _teeL, _teeR;

        /// <summary>IHudFxGraphic: baked into uv0.x. The kit never sets it — 0 keeps the
        /// shared edgefx material's shine/iridescence off the gold; only the uv1 flow runs.</summary>
        public float FxStrength
        {
            get { return _fxStrength; }
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_fxStrength, value)) { _fxStrength = value; SetVerticesDirty(); }
            }
        }

        public void SetColors(Color rampTop, Color rampBot)
        {
            if (_rampTop == rampTop && _rampBot == rampBot) return;
            _rampTop = rampTop;
            _rampBot = rampBot;
            SetVerticesDirty();
        }

        public void SetThickness(float topTh, float sideTh, float botTh)
        {
            if (Mathf.Approximately(_topTh, topTh) && Mathf.Approximately(_sideTh, sideTh)
                && Mathf.Approximately(_botTh, botTh)) return;
            _topTh = topTh;
            _sideTh = sideTh;
            _botTh = botTh;
            SetVerticesDirty();
        }

        /// <summary>Crossbar mode (the SUB strip): draw ONLY the band — no side rails, no
        /// bottom — with the ends extended <paramref name="endOverhang"/> px beyond the rect
        /// so they overlap into the outer ring's side rails and the junction reads as one
        /// surface (same shading family, no endcap).</summary>
        public void SetCrossbar(bool on, float endOverhang)
        {
            if (_crossbar == on && Mathf.Approximately(_overhang, endOverhang)) return;
            _crossbar = on;
            _overhang = endOverhang;
            SetVerticesDirty();
        }

        // Contour scratch (single-threaded UGUI rebuild — the PolygonPanelGraphic idiom).
        private static readonly System.Collections.Generic.List<Vector2> _pts =
            new System.Collections.Generic.List<Vector2>(48);

        // ---- flow gate + material hygiene ------------------------------------------------

        /// <summary>Poll the globals the panel sweep follows and rebuild ONLY when one
        /// changes — the motion itself is shader-side (_Time), so a steady state costs a few
        /// float compares and one idempotent Assign lookup per frame, no mesh work.</summary>
        private void LateUpdate()
        {
            float amp, freq, speed;
            bool want = ResolveFlow(out amp, out freq, out speed);
            // Assign is idempotent (a dictionary lookup) and fails soft: out of scope (e.g.
            // under a stencil Mask) or no material => stay on the static default path.
            if (want && !UI.Hud.HudFxMaterials.Assign(this, "edgefx")) want = false;
            if (!want)
            {
                UI.Hud.HudFxMaterials.Unassign(this);
                amp = 0f;
                speed = 0f;
            }
            if (want != _flowOn
                || (want && (Mathf.Abs(amp - _flowAmp) > 0.001f
                    || Mathf.Abs(freq - _flowFreq) > 0.001f
                    || Mathf.Abs(speed - _flowSpeed) > 0.001f)))
            {
                _flowOn = want;
                _flowAmp = amp;
                _flowFreq = freq;
                _flowSpeed = speed;
                SetVerticesDirty();
            }
            if (_flowOn && _crossbar) UpdateTees();
        }

        /// <summary>The panel-sweep parameters, mirroring HudGlobalGlass.Apply: the ripple
        /// amount only while Tier A and the global edge light are on; its frequency; the
        /// shader flow speed. False whenever the mesh-path flow cannot run.</summary>
        private bool ResolveFlow(out float amp, out float freq, out float speed)
        {
            amp = 0f;
            freq = UI.Hud.HudConfig.FxEdgeRippleFreq != null ? UI.Hud.HudConfig.FxEdgeRippleFreq.Value : 2f;
            speed = 0f;
            if (!Flow) return false;
            if (!Core.HudShaderStore.TierBAvailable || !Core.HudShaderStore.FlowAbiAvailable) return false;
            // The shared family's clocks (dissolve envelope etc.) only tick while the HUD FX
            // clock runs — the same gate UiaGlassSkin.ApplySdfFlow puts on the tabs' sweep.
            if (!UI.Hud.HudSystem.FxClockLive) return false;
            bool tierA = UI.Hud.HudConfig.FxTierA != null && UI.Hud.HudConfig.FxTierA.Value;
            bool edge = UI.Hud.HudConfig.FxEdgeLightOn != null && UI.Hud.HudConfig.FxEdgeLightOn.Value;
            if (!tierA || !edge) return false;
            float amount = UI.Hud.HudConfig.FxEdgeRipple != null ? UI.Hud.HudConfig.FxEdgeRipple.Value : 0f;
            amp = Mathf.Min(FlowMaxAmp, Mathf.Max(0f, amount) * FlowGain);
            speed = Mathf.Clamp(UI.Hud.HudConfig.FxEdgeFlowSpeed != null
                ? UI.Hud.HudConfig.FxEdgeFlowSpeed.Value : 0.22f, 0f, 4f);
            if (amp < 0.004f || speed < 0.004f || float.IsNaN(freq)) return false;
            // UGUI strips uv1 unless the (nearest) canvas opts into the channel; the F10
            // canvas does, but a stray host without it must stay static, not garbage.
            var cv = canvas;
            if (cv == null || (cv.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) == 0)
                return false;
            return true;
        }

        protected override void OnDisable()
        {
            UI.Hud.HudFxMaterials.Unassign(this);
            _flowOn = false;   // re-enable re-resolves, re-assigns and rebuilds
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            // Forget the shared-material registration before the graphic dies (hot-reload /
            // page-rebuild hygiene: the registry is keyed by Graphic).
            UI.Hud.HudFxMaterials.Unassign(this);
            base.OnDestroy();
        }

        /// <summary>The ring's arc length (px, from the bottom-centre seam, clockwise) at the
        /// reference crown contour point nearest <paramref name="local"/> (this graphic's local
        /// space). False before the first ring build or on a crossbar.</summary>
        private bool ArcAt(Vector2 local, out float arc)
        {
            arc = 0f;
            if (_crossbar || !_refValid || _refPts.Count < 2 || _refArc.Count != _refPts.Count) return false;
            float best = float.MaxValue;
            for (int i = 0; i < _refPts.Count - 1; i++)
            {
                Vector2 a = _refPts[i], b = _refPts[i + 1];
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(local - a, ab) / len2) : 0f;
                float d = (a + ab * t - local).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    arc = Mathf.Lerp(_refArc[i], _refArc[i + 1], t);
                }
            }
            return true;
        }

        /// <summary>Crossbar: find the outer ring this band tees into — the nearest ancestor
        /// subtree holding a non-crossbar FolderFrameGraphic whose rect contains our centre.
        /// Runs once (then cached), or at most every 2s while none exists.</summary>
        private FolderFrameGraphic FindRing()
        {
            Vector3 probe = rectTransform.TransformPoint(rectTransform.rect.center);
            Transform t = transform.parent;
            for (int depth = 0; t != null && depth < 12; depth++, t = t.parent)
            {
                var rings = t.GetComponentsInChildren<FolderFrameGraphic>(false);
                for (int i = 0; i < rings.Length; i++)
                {
                    var g = rings[i];
                    if (g == null || g == this || g._crossbar) continue;
                    Vector2 lp = g.rectTransform.InverseTransformPoint(probe);
                    if (g.rectTransform.rect.Contains(lp)) return g;
                }
            }
            return null;
        }

        /// <summary>Crossbar: read the outer ring's arc at our two tees (left/right rect edge
        /// at band mid-height, projected onto the ring's crown line); rebuild only when a tee
        /// moves by more than half a px. Invalid => plain x arc (the wave then meets the rail
        /// with a phase step, but only beside the rail's weight-0 AO row).</summary>
        private void UpdateTees()
        {
            if (_ring == null && Time.unscaledTime >= _nextRingSearch)
            {
                _nextRingSearch = Time.unscaledTime + 2f;
                _ring = FindRing();
            }
            bool ok = false;
            float tl = 0f, tr = 0f;
            if (_ring != null)
            {
                Rect r = rectTransform.rect;
                var rrt = _ring.rectTransform;
                Vector2 pl = rrt.InverseTransformPoint(rectTransform.TransformPoint(new Vector2(r.xMin, r.center.y)));
                Vector2 pr = rrt.InverseTransformPoint(rectTransform.TransformPoint(new Vector2(r.xMax, r.center.y)));
                ok = _ring.ArcAt(pl, out tl) && _ring.ArcAt(pr, out tr) && r.width > 1f;
                // Sanity: clockwise the right tee is further along, and the band's phase
                // stretch stays modest (a tee resolved onto the wrong member fails here).
                if (ok)
                {
                    float k = (tr - tl) / r.width;
                    ok = k > 0.5f && k < 3f;
                }
            }
            if (ok != _teeValid
                || (ok && (Mathf.Abs(tl - _teeL) > 0.5f || Mathf.Abs(tr - _teeR) > 0.5f)))
            {
                _teeValid = ok;
                _teeL = tl;
                _teeR = tr;
                SetVerticesDirty();
            }
        }

        // uv0 = (FxStrength, marker 0, harmonic weight 1 == the global's RippleSmooth 0, 0).
        // uv1 = (arc px, weight*amp, freq, speed) while flowing, else all-zero (inert).
        // Weight-0 vertices STILL carry arc/freq/speed: uv1 interpolates across each
        // triangle, and a zeroed neighbour would ramp the arc from 0 to its real value over
        // a few px — squeezing hundreds of px of phase into a strobing stripe at every
        // weight edge. Only the weight (uv1.y) may fall to 0.
        private void AddVertFx(VertexHelper vh, Vector3 pos, Color c, float arc, float weight)
        {
            Vector4 uv1 = _flowOn
                ? new Vector4(arc, Mathf.Max(0f, weight) * _flowAmp, _flowFreq, _flowSpeed)
                : Vector4.zero;
            vh.AddVert(pos, c, new Vector4(_fxStrength, 0f, 1f, 0f), uv1,
                new Vector3(0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f));
        }

        /// <summary>0 on the rails, 1 inside the TOP member's height, easing over one inner
        /// radius through the upper corners — keyed on the vertex's own height so the first
        /// tab (which sits over the top-left corner's arc) still gets a still junction.</summary>
        private float TopnessAt(Rect r, float y)
            => Mathf.Clamp01(1f - ((r.yMax - _topTh) - y) / RIn);

        private void BuildRefContour(Rect r)
        {
            _refValid = false;
            _refArc.Clear();
            const float f = 0.5f;   // the crown centre
            float fL = f * _sideTh, fT = f * _topTh, fB = f * _botTh;
            var cr = new Rect(r.xMin + fL, r.yMin + fB, r.width - 2f * fL, r.height - fT - fB);
            if (cr.width < 4f || cr.height < 4f) { _refPts.Clear(); return; }
            float rad = Mathf.Lerp(ROut, RIn, f);
            rad = Mathf.Min(rad, Mathf.Min(cr.width, cr.height) * 0.45f);
            BuildRoundedPath(cr, rad, _refPts);
            float acc = 0f;
            for (int i = 0; i < _refPts.Count; i++)
            {
                if (i > 0) acc += (_refPts[i] - _refPts[i - 1]).magnitude;
                _refArc.Add(acc);
            }
            _refValid = _refPts.Count >= 2;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            _refValid = false;
            if (_crossbar)
            {
                PopulateCrossbar(vh, r);
                return;
            }
            if (r.width < 40f || r.height < 30f) return;

            // The phase axis first: the crown-centre contour's arc, shared by vertex index.
            BuildRefContour(r);
            if (!_refValid) return;
            float total = _refArc[_refArc.Count - 1];

            // Contours, outermost first: an alpha-0 fringe just outside the silhouette,
            // the shaded stops across the ring, then an alpha-0 fringe just inside the
            // cutout (blending the AO softly onto the panel).
            int stops = StopF.Length;
            int contours = stops + 2;
            int ptsPerContour = 0;

            for (int k = 0; k < contours; k++)
            {
                float f;
                float mul;
                float alpha = 1f;
                bool outerFringe = k == 0;
                bool innerFringe = k == contours - 1;
                if (outerFringe) { f = 0f; mul = StopM[0]; alpha = 0f; }
                else if (innerFringe) { f = 1f; mul = StopM[stops - 1]; alpha = 0f; }
                else { f = StopF[k - 1]; mul = StopM[k - 1]; }

                // The contour rect: per-side insets scale with the member thicknesses; the
                // fringes push 1px beyond their silhouettes.
                float fL = f * _sideTh, fR = f * _sideTh, fT = f * _topTh, fB = f * _botTh;
                if (outerFringe) { fL = -Fringe; fR = -Fringe; fT = -Fringe; fB = -Fringe; }
                if (innerFringe) { fL += Fringe; fR += Fringe; fT += Fringe; fB += Fringe; }
                var cr = new Rect(r.xMin + fL, r.yMin + fB,
                    r.width - fL - fR, r.height - fT - fB);
                if (cr.width < 4f || cr.height < 4f) return;
                float rad = Mathf.Lerp(ROut, RIn, Mathf.Clamp01(f));
                rad = Mathf.Min(rad, Mathf.Min(cr.width, cr.height) * 0.45f);

                BuildRoundedPath(cr, rad, _pts);
                if (ptsPerContour == 0) ptsPerContour = _pts.Count;
                if (_pts.Count != ptsPerContour) return;   // defensive: structure must match
                if (_pts.Count != _refArc.Count) return;    // the phase axis pairs by index

                // This contour's wave weight: the rail profile, fringes always still.
                float stopW = (outerFringe || innerFringe) ? 0f : StopW[k - 1];
                bool fadeOnTop = k == 2;   // the upper crown row goes still on the top member

                for (int i = 0; i < _pts.Count; i++)
                {
                    Vector2 p = _pts[i];
                    float yT = Mathf.Clamp01((r.yMax - p.y) / Mathf.Max(1f, r.height));
                    Color ramp = Color.Lerp(_rampTop, _rampBot, Mathf.Pow(yT, 1.15f));
                    // Seam round: along the TOP RUN the outermost stop blends to full tone —
                    // its row abuts the selected tab's fill at band-top, and any rim
                    // darkening there made a luminance ledge. The sides/bottom keep the
                    // gentle outer rim; the blend is positional, so no seam appears where
                    // the top run turns the corner.
                    float mulHere = mul;
                    if (k == 1)   // the outermost SOLID stop (k0 is the alpha-0 fringe)
                        mulHere = Mathf.Lerp(mul, 1f,
                            Mathf.Clamp01((p.y - (r.yMax - _topTh)) / Mathf.Max(1f, _topTh)));
                    Color c = new Color(ramp.r * mulHere, ramp.g * mulHere, ramp.b * mulHere, alpha);

                    // Flow weight: stop profile x top-member stillness x the seam taper
                    // (0 at the duplicated bottom-centre vertices, so the arc wrap from
                    // `total` back to 0 has zero amplitude and cannot be seen).
                    float w = stopW;
                    if (w > 0f && fadeOnTop) w *= 1f - TopnessAt(r, p.y);
                    float arc = _refArc[i];
                    if (w > 0f) w *= Mathf.Clamp01(Mathf.Min(arc, total - arc) / SeamTaper);
                    AddVertFx(vh, new Vector3(p.x, p.y), c, arc, w);
                }
            }

            // Quad strips between consecutive contours. The path is OPEN with its first and
            // last points coincident (the bottom-centre seam), so no strip wraps: a wrapping
            // quad would interpolate the arc from `total` to 0 across one segment and squeeze
            // the whole wave into a strobing stripe.
            for (int k = 0; k < contours - 1; k++)
            {
                int a = k * ptsPerContour;
                int b = (k + 1) * ptsPerContour;
                for (int i = 0; i < ptsPerContour - 1; i++)
                {
                    int j = i + 1;
                    vh.AddTriangle(a + i, b + i, b + j);
                    vh.AddTriangle(a + i, b + j, a + j);
                }
            }

            // No bright top line any more (FlorpyDorp's closer) — the soft crown IS the top.
        }

        /// <summary>The SUB strip's band-only crossbar: the same soft cross-section as the
        /// ring's members (dark-ish edge, lifted crown, inner AO, baked fringes) as plain
        /// horizontal rows, ends extended past the rect so they melt into the outer ring's
        /// side rails. The band sits high in the OUTER frame, so its local ramp stays in
        /// the upper gold family — near-identical tones at the tee junction.</summary>
        private void PopulateCrossbar(VertexHelper vh, Rect r)
        {
            float w = r.width, h = r.height;   // h = the band thickness
            if (w < 20f || h < 4f) return;
            float x0 = r.xMin - _overhang;
            float x1 = r.xMax + _overhang;
            // The melt zone at each end: the overhang plus a couple px inside the rect. In
            // it the band's BOTTOM darkening is suppressed (final collar micros: the AO
            // fringe was drawing over the rail as a small dark tick at the tee) — the end
            // columns hold the body tone, so the junction is clean gold-on-gold.
            float zL = r.xMin + 2f;
            float zR = r.xMax - 2f;
            Color localBot = Color.Lerp(_rampTop, _rampBot, 0.3f);

            // The phase axis: borrowed from the OUTER ring at the two tees (left edge = the
            // ring's arc up the left rail, right edge = its arc down the right rail), linear
            // between — so the wave arrives at each junction in the rail's own phase. The
            // stretch (tee-to-tee ring distance / bar width, ~1.1-1.2) only slows the bar's
            // wave slightly. No ring found yet: plain x arc (see UpdateTees).
            float arcScale = _teeValid ? (_teeR - _teeL) / Mathf.Max(1f, w) : 1f;
            float arcBase = _teeValid ? _teeL : 0f;
            float aX0 = arcBase + (x0 - r.xMin) * arcScale;
            float aZL = arcBase + (zL - r.xMin) * arcScale;
            float aZR = arcBase + (zR - r.xMin) * arcScale;
            float aX1 = arcBase + (x1 - r.xMin) * arcScale;
            if (!_teeValid)
            {
                // Keep the fallback arc non-negative (x0 lies left of r.xMin).
                float shift = -aX0;
                aX0 += shift; aZL += shift; aZR += shift; aX1 += shift;
            }

            int stops = StopF.Length;
            BarRow4(vh, x0, zL, zR, x1, r.yMax + Fringe,
                CrossColor(0f, localBot, 1f, 0f), CrossColor(0f, localBot, 1f, 0f),
                aX0, aZL, aZR, aX1, 0f);
            for (int k = 0; k < stops; k++)
            {
                float f = StopF[k];
                // Top row at FULL tone (seam round): the band's first row abuts the tab
                // fill at band-top with the SAME colour, so no luminance ledge exists
                // across the boundary — the crown lift only begins below it.
                float mul = k == 0 ? 1f : StopM[k];
                float endMul = k == stops - 1 ? 0.95f : mul;   // no AO in the melt zones
                // Flow: the top two rows (under the selected tab + its feet) stay still;
                // the light rides the lower crown row, as on the ring's top member.
                BarRow4(vh, x0, zL, zR, x1, r.yMax - f * h,
                    CrossColor(f, localBot, mul, 1f), CrossColor(f, localBot, endMul, 1f),
                    aX0, aZL, aZR, aX1, CrossW[k]);
            }
            BarRow4(vh, x0, zL, zR, x1, r.yMin - Fringe,
                CrossColor(1f, localBot, StopM[stops - 1], 0f), CrossColor(1f, localBot, 0.95f, 0f),
                aX0, aZL, aZR, aX1, 0f);
            int total = stops + 2;
            for (int k = 0; k < total - 1; k++)
            {
                int a = k * 4;
                for (int s = 0; s < 3; s++)
                {
                    vh.AddTriangle(a + s, a + 4 + s, a + 5 + s);
                    vh.AddTriangle(a + s, a + 5 + s, a + 1 + s);
                }
            }

            // No bright top line any more (FlorpyDorp's closer) — the soft crown IS the top.
        }

        private Color CrossColor(float f, Color localBot, float mul, float alpha)
        {
            Color ramp = Color.Lerp(_rampTop, localBot, f);
            return new Color(ramp.r * mul, ramp.g * mul, ramp.b * mul, alpha);
        }

        /// <summary>One crossbar row as four columns: [end zone | mid span | end zone], the
        /// end columns carrying the melt colour, the mid columns the normal band colour. The
        /// outermost columns (hidden under the ring's rails) carry no flow weight; the arc
        /// is linear in x, so interpolation along the long mid span is exact.</summary>
        private void BarRow4(VertexHelper vh, float x0, float zL, float zR, float x1,
            float y, Color mid, Color end, float aX0, float aZL, float aZR, float aX1, float w)
        {
            AddVertFx(vh, new Vector3(x0, y), end, aX0, 0f);
            AddVertFx(vh, new Vector3(zL, y), mid, aZL, w);
            AddVertFx(vh, new Vector3(zR, y), mid, aZR, w);
            AddVertFx(vh, new Vector3(x1, y), end, aX1, 0f);
        }

        /// <summary>Sample a rounded rect as a fixed-structure path: per corner N arc steps,
        /// per vertical side 2 midpoints (so the vertical ramp stays smooth down the rails).
        /// The structure (point count and ordering) is identical for every contour, which is
        /// what lets the concentric strips pair up vertex-for-vertex.
        ///
        /// <para>The path runs CLOCKWISE from the BOTTOM-CENTRE and returns to it (first and
        /// last points coincide — an open list, closed geometrically): that duplicated point
        /// is the moving light's one arc seam, placed where it is least visible (under the
        /// menu content, far from the tabs) and flanked by SeamTaper points where the flow
        /// weight has reached 1.</para></summary>
        private static void BuildRoundedPath(Rect r, float rad, System.Collections.Generic.List<Vector2> into)
        {
            into.Clear();
            const int N = 5;      // arc steps per corner
            const int Mid = 2;    // midpoints per side
            float x0 = r.xMin, x1 = r.xMax, y0 = r.yMin, y1 = r.yMax;
            float cx = r.center.x;
            // The seam-taper stations, clamped inside each half of the bottom run (the
            // clamp only moves points, never adds/removes them, so structure stays fixed).
            float t = Mathf.Min(SeamTaper, Mathf.Max(0f, (cx - (x0 + rad)) * 0.5f));

            // Bottom-centre seam, leftwards to the bottom-left corner.
            into.Add(new Vector2(cx, y0));
            into.Add(new Vector2(cx - t, y0));
            AddSide(into, new Vector2(cx - t, y0), new Vector2(x0 + rad, y0), 1);
            AddArc(into, new Vector2(x0 + rad, y0 + rad), rad, -90f, -180f, N);
            AddSide(into, new Vector2(x0, y0 + rad), new Vector2(x0, y1 - rad), Mid);
            // Top-left corner: from the left-edge tangent up over to the top-edge tangent.
            AddArc(into, new Vector2(x0 + rad, y1 - rad), rad, 180f, 90f, N);
            AddSide(into, new Vector2(x0 + rad, y1), new Vector2(x1 - rad, y1), Mid);
            AddArc(into, new Vector2(x1 - rad, y1 - rad), rad, 90f, 0f, N);
            AddSide(into, new Vector2(x1, y1 - rad), new Vector2(x1, y0 + rad), Mid);
            AddArc(into, new Vector2(x1 - rad, y0 + rad), rad, 0f, -90f, N);
            // Bottom-right corner back along the bottom run to the seam.
            AddSide(into, new Vector2(x1 - rad, y0), new Vector2(cx + t, y0), 1);
            into.Add(new Vector2(cx + t, y0));
            into.Add(new Vector2(cx, y0));
        }

        private static void AddArc(System.Collections.Generic.List<Vector2> into,
            Vector2 centre, float rad, float fromDeg, float toDeg, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                into.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
        }

        private static void AddSide(System.Collections.Generic.List<Vector2> into,
            Vector2 from, Vector2 to, int midPoints)
        {
            for (int i = 1; i <= midPoints; i++)
                into.Add(Vector2.Lerp(from, to, i / (float)(midPoints + 1)));
        }
    }

    /// <summary>
    /// The selected tab's fillet FEET — the only custom mesh left in the tab (structural
    /// rethink 2026-09-26: the tab BODY is a stock PanelGraphic with the mod's built-in
    /// edge lighting, its fill ramp a ChipGradient overlay, and the band renders
    /// continuously beneath everything; these feet are the few px that overlap the band's
    /// top edge to sell the flow). Per side: the concave outward flare over the last ~6px
    /// above band-top, then a short taper 3px below it, all in the junction tone (== the
    /// tab ramp's bottom == the band's crown), with baked AA fringes outward.
    ///
    /// <para>Deliberately STATIC on the default UI material (moving-edge-light round,
    /// 2026-09-26): the feet only overlap the band's top ~4px, and
    /// <see cref="FolderFrameGraphic"/> keeps those rows at flow weight 0 (outer row always,
    /// upper crown row on the top member / crossbar), so the band beneath them never pulses
    /// and a still foot always matches it. Should the frame's top-member weights change,
    /// this graphic must move onto the same payload.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class TabFeetGraphic : MaskableGraphic
    {
        private float _bodyH = 43f;     // face height (raise + visible) above band-top
        private float _bandTh = 10f;
        private float _topInset = 10f;  // the body trapezoid's per-side top narrowing
        private Color _tone;

        private const float Fringe = 0.9f;
        private const float FlareX = 4f;   // outward flare at band-top
        private const float FlareH = 6f;   // flare vertical span above band-top
        private const float FootH = 3f;    // taper below band-top

        public void Configure(float bodyH, float bandTh, float topInset, Color tone)
        {
            if (Mathf.Approximately(_bodyH, bodyH) && Mathf.Approximately(_bandTh, bandTh)
                && Mathf.Approximately(_topInset, topInset) && _tone == tone) return;
            _bodyH = bodyH;
            _bandTh = bandTh;
            _topInset = topInset;
            _tone = tone;
            SetVerticesDirty();
        }

        // Row depths below the FACE TOP, spanning the flare + the sub-band taper.
        private static readonly float[] RowD = { -6f, -3f, 0f, 1.6f, 3f };   // relative to band-top

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float w = r.width;
            if (w < 20f || r.height < _bandTh + 8f) return;
            float bandTopY = r.yMin + _bandTh;
            float cx = r.center.x;

            for (int side = -1; side <= 1; side += 2)
            {
                int i0 = vh.currentVertCount;
                int rows = 0;
                for (int i = 0; i < RowD.Length; i++)
                {
                    float rel = RowD[i];
                    float y = bandTopY - rel;               // rel > 0 = below band-top
                    float d = _bodyH + rel;                 // depth below the face top
                    // The BODY's slant edge at this depth (bottom = full width), plus the
                    // concave ease-in flare above band-top, minus the taper below it.
                    float slantHalf = w * 0.5f - _topInset * Mathf.Clamp01(1f - d / _bodyH);
                    float outer;
                    if (rel <= 0f)
                    {
                        float t = Mathf.Clamp01((rel + FlareH) / FlareH);
                        outer = slantHalf + FlareX * t * t;
                    }
                    else
                    {
                        outer = w * 0.5f + FlareX - rel * 1.3f;
                    }
                    // The foot's inner edge tucks a couple px under the body so no seam can
                    // open between them; below band-top it narrows toward the toe.
                    float inner = rel <= 0f ? slantHalf - 2f
                        : Mathf.Max(outer - (6f - rel * 1.2f), outer - 6f);
                    Color solid = _tone;
                    Color faded = _tone; faded.a = 0f;
                    vh.AddVert(new Vector3(cx + side * inner, y), solid, Vector2.zero);
                    vh.AddVert(new Vector3(cx + side * outer, y), solid, Vector2.zero);
                    vh.AddVert(new Vector3(cx + side * (outer + Fringe), y), faded, Vector2.zero);
                    rows++;
                }
                // Toe AA: one extra alpha-0 row just below the last.
                {
                    float y = bandTopY - (RowD[RowD.Length - 1] + Fringe);
                    float outer = w * 0.5f + FlareX - (RowD[RowD.Length - 1] + Fringe) * 1.3f;
                    Color faded = _tone; faded.a = 0f;
                    vh.AddVert(new Vector3(cx + side * (outer - 4f), y), faded, Vector2.zero);
                    vh.AddVert(new Vector3(cx + side * outer, y), faded, Vector2.zero);
                    vh.AddVert(new Vector3(cx + side * (outer + Fringe), y), faded, Vector2.zero);
                    rows++;
                }
                for (int k = 0; k < rows - 1; k++)
                {
                    int a = i0 + k * 3;
                    for (int s = 0; s < 2; s++)
                    {
                        // Mirrored sides flip the winding; UGUI has no backface culling.
                        vh.AddTriangle(a + s, a + 3 + s, a + 4 + s);
                        vh.AddTriangle(a + s, a + 4 + s, a + 1 + s);
                    }
                }
            }
        }
    }
}
