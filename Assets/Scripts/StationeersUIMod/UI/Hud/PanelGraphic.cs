using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A rounded, optionally trapezoid panel with a thin border and baked anti-aliasing —
    /// the RadialWedgeGraphic idiom applied to rectangles. The shape is a convex polygon of
    /// corner CENTRES swept by a disc (Minkowski sum), so outward offsets (border band,
    /// alpha fringe) are exact: the same polygon with a bigger disc.
    ///
    /// Overlay canvases get no MSAA; the ~1px colour/alpha ramp on every edge IS the AA.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PanelGraphic : MaskableGraphic, IHudFxGraphic
    {
        private float _w = 100f, _h = 40f, _topInset, _bottomInset;
        // Per-corner radii in CCW center order: BL, BR, TR, TL. The single-radius
        // SetShape sets all four; the designer's boxes set each independently.
        private float _rBL = 8f, _rBR = 8f, _rTR = 8f, _rTL = 8f;
        private float _borderWidth = 1.4f;
        private Color _borderColor = Color.clear;

        // Dirty-on-change (the HUD sets these every frame; only real changes may cost a
        // mesh rebuild — this graphic is ALWAYS on screen, unlike the radials).
        public float BorderWidth
        {
            get => _borderWidth;
            set { if (!Mathf.Approximately(_borderWidth, value)) { _borderWidth = value; SetVerticesDirty(); } }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { if (_borderColor != value) { _borderColor = value; SetVerticesDirty(); } }
        }

        // Glass treatment (0 = off = the exact pre-glass flat rendering). Both are pure
        // vertex-colour effects — no shaders, no textures, so they warp, fade and batch
        // exactly like every other panel.
        private float _sheen;
        private float _spec;
        private float _fxStrength;

        /// <summary>0..1 — a milky top-lit gradient baked into the fill: the panel reads
        /// as smoked glass catching light from above instead of a flat tint.</summary>
        public float Sheen
        {
            get => _sheen;
            set
            {
                // NaN-guard: float.TryParse accepts "NaN" from a hand-edited profile and
                // Clamp01 passes NaN through — which would poison every vertex colour.
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_sheen, value)) { _sheen = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..1 — specular "light catch" on the border: the line brightens to
        /// near-white where it faces the key light (upper-left) with a faint opposing rim
        /// (lower-right), and runs brighter→dimmer along each edge.</summary>
        public float Spec
        {
            get => _spec;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_spec, value)) { _spec = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..1 — per-element effect strength baked into uv0.x on every vertex (see
        /// <see cref="IHudFxGraphic"/>). Dirties the mesh so the new value is baked in on the
        /// next rebuild; 0 (default) leaves uv0.x at 0, which the stock UI material never
        /// samples, so rendered pixels are unchanged until an FX material reads the channel.</summary>
        public float FxStrength
        {
            get => _fxStrength;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_fxStrength, value)) { _fxStrength = value; SetVerticesDirty(); }
            }
        }

        // ── 0.9.0 vertex-colour features. All default to today's exact output, so a rebuild
        // with every new property at its default is byte-identical to the pre-0.9.0 mesh.
        private float _borderFade;
        private float _edgeRipple;
        private float _edgeRippleFreq = 2f;
        private float _softEdge;
        private float _glow;
        private float _glowInner;
        private float _glowWidth = 24f;
        private float _glowDiffuse;
        private int _borderSides = 15;

        /// <summary>0..1 — at 1 the border's ALPHA follows the directional light term: lit
        /// sections stay fully visible while unlit sections dissolve to ZERO (the concept-art
        /// "lines fade away"). 0 (default) keeps the constant-alpha border. Works even with
        /// Spec off — <see cref="BorderAt"/> computes the light weight independently.</summary>
        public float BorderFade
        {
            get => _borderFade;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_borderFade, value)) { _borderFade = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..2.5 — a higher-frequency light/dark shimmer ALONG the border, on top of
        /// the smooth directional light. Above ~1.4 the modulation overdrives: troughs clip to
        /// FULLY dark (the line visibly breaks up) and crests overshoot past the nominal light
        /// weight (extra whitening/glow at the catches) — the play-test ask for "more extremes".
        /// Position-based (deterministic per contour point); NO time animation — the mesh must
        /// not rebuild per frame. 0 (default) = off.</summary>
        public float EdgeRipple
        {
            get => _edgeRipple;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 2.5f);
                if (!Mathf.Approximately(_edgeRipple, value)) { _edgeRipple = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0.5..8 — <see cref="EdgeRipple"/> frequency in cycles per ~100px. Default 2.
        /// Only matters when EdgeRipple &gt; 0, so the default is inert for byte-identity.</summary>
        public float EdgeRippleFreq
        {
            get => _edgeRippleFreq;
            set
            {
                value = float.IsNaN(value) ? 2f : Mathf.Clamp(value, 0.5f, 8f);
                if (!Mathf.Approximately(_edgeRippleFreq, value)) { _edgeRippleFreq = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..48 extra px added to the OUTER fill fringe so the panel's fill fades out
        /// over a wide soft band instead of the crisp ~1.25px AA (the "boxes blur into each
        /// other" look). Only applies when the border reads as invisible — a solid frame line
        /// keeps its sharp outer edge. 0 (default) = the crisp classic fringe.</summary>
        public float SoftEdge
        {
            get => _softEdge;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 48f);
                if (!Mathf.Approximately(_softEdge, value)) { _softEdge = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..2 — a soft luminous HALO ring OUTSIDE the panel (8 extra stops per column,
        /// so it warps/batches with the rest of the mesh). Colour reads as the frame glowing;
        /// alpha follows the border's effective alpha. 0 (default) emits no halo.</summary>
        public float Glow
        {
            get => _glow;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 2f);
                if (!Mathf.Approximately(_glow, value)) { _glow = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..2 — the same luminous glow mirrored INWARD: the frame's light bleeds
        /// INTO the glass (concept art: "the lines glow into and out of the box"). Emitted as
        /// 4 extra stops between the fill fan and the border ramp, composited source-over onto
        /// the fill so it reads as light ON the glass. Shares GlowWidth (depth-capped in
        /// shallow panels) and GlowDiffuse. Independent of <see cref="Glow"/> — either side
        /// can run alone. 0 (default) emits nothing.</summary>
        public float GlowInner
        {
            get => _glowInner;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 2f);
                if (!Mathf.Approximately(_glowInner, value)) { _glowInner = value; SetVerticesDirty(); }
            }
        }

        /// <summary>6..160 — <see cref="Glow"/> halo radius in px. Default 24. Inert while Glow is
        /// 0, so the default never perturbs the byte-identical baseline.</summary>
        public float GlowWidth
        {
            get => _glowWidth;
            set
            {
                value = float.IsNaN(value) ? 24f : Mathf.Clamp(value, 6f, 160f);
                if (!Mathf.Approximately(_glowWidth, value)) { _glowWidth = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..1 — reshapes the halo from a tight rim-hugging glow (0, the classic
        /// falloff) toward a wide DIFFUSE haze (1): the decay flattens so the energy reaches
        /// further out, the peak at the frame dims to match, the directional shaping relaxes
        /// so the haze wraps more of the perimeter, and the ripple shimmer is blended out of
        /// the halo (its per-column contrast fans into radial spokes at halo distances).
        /// Inert while <see cref="Glow"/> is 0. Pair high values with a larger GlowWidth.</summary>
        public float GlowDiffuse
        {
            get => _glowDiffuse;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_glowDiffuse, value)) { _glowDiffuse = value; SetVerticesDirty(); }
            }
        }

        /// <summary>Per-side border visibility bitmask: 1=Top, 2=Right, 4=Bottom, 8=Left.
        /// Default 15 (all sides). Disabled sides melt away smoothly around the corner arcs
        /// (weighted by the outward normal) rather than hard-cutting. See <see cref="BorderAt"/>.</summary>
        public int BorderSides
        {
            get => _borderSides;
            set
            {
                int v = Mathf.Clamp(value, 0, 15);
                if (_borderSides != v) { _borderSides = v; SetVerticesDirty(); }
            }
        }

        /// <summary>Set the panel shape. Insets pull the top/bottom corners inward,
        /// turning the rect into a trapezoid (the hand-tray shoulders).</summary>
        public void SetShape(float width, float height, float cornerRadius,
            float topInset = 0f, float bottomInset = 0f)
            => SetShape(width, height, cornerRadius, cornerRadius, cornerRadius, cornerRadius,
                topInset, bottomInset);

        /// <summary>Per-corner radii (the designer's custom boxes). The Minkowski sweep
        /// stays exact per corner: each arc endpoint lands ON the rect side, so straight
        /// edges between unequal corners remain true side lines, not slants.</summary>
        public void SetShape(float width, float height,
            float radiusTL, float radiusTR, float radiusBR, float radiusBL,
            float topInset = 0f, float bottomInset = 0f)
        {
            if (Mathf.Approximately(_w, width) && Mathf.Approximately(_h, height)
                && Mathf.Approximately(_rTL, radiusTL) && Mathf.Approximately(_rTR, radiusTR)
                && Mathf.Approximately(_rBR, radiusBR) && Mathf.Approximately(_rBL, radiusBL)
                && Mathf.Approximately(_topInset, topInset)
                && Mathf.Approximately(_bottomInset, bottomInset))
                return;
            _w = width; _h = height;
            _rTL = radiusTL; _rTR = radiusTR; _rBR = radiusBR; _rBL = radiusBL;
            _topInset = topInset; _bottomInset = bottomInset;
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        private static float Feather => HudConfig.EdgeFeather != null
            ? HudConfig.EdgeFeather.Value : 1.25f;

        // Scratch (single-threaded UI rebuild, same pattern as CircleGraphic). The stop arrays
        // hold up to 4 inner-glow stops (GlowInner) + 4 base stops (fill-ramp, border-in,
        // border-solid, fade) + 8 halo stops (Glow) — see PopulateMeshCore. _stopM carries each
        // stop's uv0.y marker so shader rim effects fade smoothly ACROSS the glow bands instead
        // of striping them; _inW holds the inner band's column-independent falloff weights.
        private static readonly List<Vector2> _centers = new List<Vector2>(4);
        private static readonly float[] _radii = new float[4];
        private static readonly float[] _stopD = new float[17];
        private static readonly Color[] _stopC = new Color[17];
        private static readonly float[] _stopM = new float[17];
        private static readonly float[] _inW = new float[4];
        private static readonly float[] _miter = new float[4];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // Profiler tripwire: rebuild storms (Calls/s, Max/frame) expose any effect that
            // carelessly re-dirties meshes per frame. Runs in Canvas.willRenderCanvases, so
            // samples attribute to the NEXT frame's flush — irrelevant for 10s averages.
            using (Profiling.ProfilicusUniversalis.Time("Hud.Mesh.Panel"))
                PopulateMeshCore(vh);
        }

        private void PopulateMeshCore(VertexHelper vh)
        {
            vh.Clear();
            float hw = _w * 0.5f, hh = _h * 0.5f;
            // The threshold must admit hairlines: the vitals bars are ~1.4px tall panels
            // (hh 0.7) — a 1px guard would silently cull every one of them.
            if (hw < 0.3f || hh < 0.3f) return;

            // Per-corner radii, CSS-style normalized: when two adjacent radii would
            // overlap along a side, scale ALL of them down together (keeps proportions,
            // keeps the center polygon convex).
            float cap = Mathf.Min(hw, hh) - 0.5f;
            _radii[0] = Mathf.Clamp(_rBL, 0.5f, cap);
            _radii[1] = Mathf.Clamp(_rBR, 0.5f, cap);
            _radii[2] = Mathf.Clamp(_rTR, 0.5f, cap);
            _radii[3] = Mathf.Clamp(_rTL, 0.5f, cap);
            float k = 1f;
            k = Mathf.Min(k, _w / Mathf.Max(1f, _radii[0] + _radii[1]));  // bottom
            k = Mathf.Min(k, _w / Mathf.Max(1f, _radii[3] + _radii[2]));  // top
            k = Mathf.Min(k, _h / Mathf.Max(1f, _radii[0] + _radii[3]));  // left
            k = Mathf.Min(k, _h / Mathf.Max(1f, _radii[1] + _radii[2]));  // right
            if (k < 1f)
                for (int i = 0; i < 4; i++) _radii[i] = Mathf.Max(0.5f, _radii[i] * k);

            // Corner-centre polygon, counter-clockwise from bottom-left.
            _centers.Clear();
            _centers.Add(new Vector2(-hw + _radii[0] + Mathf.Max(0f, _bottomInset), -hh + _radii[0]));
            _centers.Add(new Vector2(hw - _radii[1] - Mathf.Max(0f, _bottomInset), -hh + _radii[1]));
            _centers.Add(new Vector2(hw - _radii[2] - Mathf.Max(0f, _topInset), hh - _radii[2]));
            _centers.Add(new Vector2(-hw + _radii[3] + Mathf.Max(0f, _topInset), hh - _radii[3]));

            float f = Feather;
            float bw = Mathf.Max(0f, BorderWidth);
            bool hasBorder = bw > 0.05f && BorderColor.a > 0.004f;

            // Inner ramp must never cross a corner's center: bound it by the SMALLEST radius.
            float rMin = Mathf.Min(Mathf.Min(_radii[0], _radii[1]), Mathf.Min(_radii[2], _radii[3]));

            // GlowInner: a luminous band INSIDE the contour ("the lines glow into the box"),
            // emitted as extra stops BEFORE the fill ramp in the same column system. Its depth
            // shares the GlowWidth slider but is capped so opposite edges can't reach each
            // other in shallow panels, and it silently stands down when the panel is too small
            // to carry a meaningful band.
            float rampD = Mathf.Min(f, rMin);
            bool hasGlowIn = _glowInner > 0.004f;
            int inStops = 0;
            float glowInD = 0f;
            if (hasGlowIn)
            {
                glowInD = Mathf.Min(_glowWidth, Mathf.Min(hw, hh) * 0.55f);
                if (glowInD < rampD + 1.5f) hasGlowIn = false; else inStops = 4;
            }

            int baseStops;
            if (hasBorder)
            {
                _stopD[inStops + 0] = -rampD;        // fill up to here, ramp -> border
                _stopD[inStops + 1] = 0f;
                _stopD[inStops + 2] = bw;            // solid line
                _stopD[inStops + 3] = bw + f;        // fade out
                baseStops = 4;
            }
            else
            {
                _stopD[inStops + 0] = 0f;
                _stopD[inStops + 1] = f;
                baseStops = 2;
            }

            // SoftEdge: widen the OUTERMOST (alpha-0) fade band so the panel melts out over a
            // broad soft skirt instead of the crisp ~1.25px AA ("boxes blur into each other").
            // Applies with OR without a visible border (play-test round 3: the border-only
            // gate made the slider dead on every framed panel — with a border, the frame line
            // itself gains the soft outward tail, which is exactly the concept-art look).
            // Every stop rides the same contour normal, so widening the last one is
            // geometrically safe (corner radii / insets stay consistent).
            if (_softEdge > 0.01f)
                _stopD[inStops + baseStops - 1] += _softEdge;

            // Base stops carry the full edge marker (uv0.y = 1): shader rim effects apply at
            // full strength up to the panel's contour.
            for (int mi = 0; mi < baseStops; mi++) _stopM[inStops + mi] = 1f;

            // Inner-glow stops: distances walk from the depth bound up to the fill ramp. The
            // per-stop falloff (the same smoothstep-complement power curve as the outer halo,
            // sharing the GlowDiffuse exponent) is column-independent, so it is precomputed
            // once into _inW. Markers fade 0 -> 1 toward the contour so shader rim effects
            // ramp IN across the band exactly as they ramp OUT across the halo.
            if (inStops > 0)
            {
                float pIn = Mathf.Lerp(2f, 0.65f, _glowDiffuse);
                for (int gs = 0; gs < inStops; gs++)
                {
                    float u = 1f - gs / (float)inStops; // 1 deepest .. toward the frame
                    float sm = u * u * (3f - 2f * u);
                    _inW[gs] = Mathf.Pow(1f - sm, pIn);
                    _stopD[gs] = Mathf.Lerp(-glowInD, -rampD, gs / (float)inStops);
                    _stopM[gs] = 1f - u;
                }
            }

            // Glow: a luminous halo OUTSIDE the panel, emitted in the SAME column system (so it
            // warps/batches identically). The base fade stop IS the halo's start (ColumnColors
            // recolors it), then FIVE decay stops sample a (1-t)^2.2 falloff — enough steps that
            // a wide halo reads as smooth light falloff, not banded stripes (play-test round 3:
            // 3 stops banded badly at large GlowWidth). Their uv0.y marker fades 1 -> 0 so
            // shader rim effects (iridescence) decay across the halo instead of striping it.
            bool hasGlow = _glow > 0.004f;
            int stops = inStops + baseStops;
            if (hasGlow)
            {
                // EIGHT decay stops (play-test round 6: five still Mach-banded — the eye
                // amplifies each piecewise-linear slope change into a phantom line on dark
                // backgrounds; more, finer segments push the bands below perception).
                float outer = _stopD[inStops + baseStops - 1];
                for (int gs = 0; gs < 8; gs++)
                {
                    float t = (gs + 1) / 8f;
                    _stopD[inStops + baseStops + gs] = outer + _glowWidth * t;
                    _stopM[inStops + baseStops + gs] = 1f - t;
                }
                stops += 8;
            }

            // Stop colours are computed PER COLUMN: with glass off they are constants
            // (bitwise the old output); with glass on, fill follows the sheen gradient
            // and the border follows the specular run. Vertex colours interpolate
            // linearly across a quad, so linear-in-position light reads exactly.
            void ColumnColors(Vector2 dir, Vector2 onShape)
            {
                Color fillC = FillAt(onShape.y, hh);
                Color bc = hasBorder ? BorderAt(dir, onShape, hw) : fillC;
                if (hasBorder)
                {
                    _stopC[inStops + 0] = fillC; _stopC[inStops + 1] = bc;
                    _stopC[inStops + 2] = bc; _stopC[inStops + 3] = Fade(bc);
                }
                else
                {
                    _stopC[inStops + 0] = fillC; _stopC[inStops + 1] = Fade(fillC);
                }
                if (hasGlow || hasGlowIn)
                {
                    // Halo tinted toward the frame so it reads as the border glowing. Its
                    // brightness is driven by the GLOW slider, floored against the source alpha
                    // (play-test round 3: riding the raw border alpha crushed the halo to
                    // invisibility on faint-border profiles — Glassy 4.0's top bar border is
                    // 2.7% alpha). Directional/side fades still shape it via the bc HUE mix.
                    // Hue: the RAW border colour (the element's accent), NOT the spec-whitened
                    // per-column bc — whitened halos read as grey fog, accent-hued halos read
                    // as the frame glowing (play-test round 4: "white clouds").
                    Color halo = hasBorder ? Color.Lerp(color, BorderColor, 0.75f) : color;
                    // Intensity: LIGHT-SHAPED (play-test round 5: a uniform 360° skirt reads
                    // as a hard-edged slab no matter how faint — the concept art glows only
                    // where the frame is LIT). The same directional weight the border uses
                    // shapes the glow on BOTH sides of the frame: bright at the light-catch,
                    // near-nothing on unlit sides, shimmering with the ripple. Overlap-stacking
                    // stays tame because most of each perimeter emits almost no glow.
                    // The GLOW keeps only a FRACTION of the border's shimmer (play-test round
                    // 10: "edge light + ripples + halo" — at halo widths each column's ripple
                    // value fans into a radial ray, and an overdriven crest bursts at corners).
                    // The border itself keeps the full ripple; the glow rides mostly the
                    // smooth directional light, diffuseness removes the remainder entirely,
                    // and the cap stops overdrive (lw up to 2) from spiking the skirt.
                    float lwS = BorderLightSmooth(dir, onShape, hw);
                    float rippleShare = 0.35f * (1f - _glowDiffuse);
                    float lw = Mathf.Min(1.2f,
                        Mathf.Lerp(lwS, BorderLightW(dir, onShape, hw), rippleShare));
                    float baseA = Mathf.Max(0.5f, halo.a);
                    // Play-test round 7: the 0.3 "whisper" multiplier was over-corrected —
                    // maxed out the halo was "pretty much barely visible". Now that the glow
                    // is light-shaped (no uniform-skirt slab risk) it can afford real
                    // presence: ~3x brighter at the lit catches, still near-nothing on unlit
                    // sides, capped so overdriven ripple crests (lw up to 2) can't go solid.
                    // GlowDiffuse relaxes the directional shaping (the haze wraps more of the
                    // perimeter) and dims the peak — the flatter falloff pushes the energy
                    // AWAY from the frame instead of brightening the rim. `shaped` is the
                    // shared per-column light term; inner and outer scale it independently.
                    float shapeFloor = 0.08f + 0.27f * _glowDiffuse;
                    float shaped = 0.55f * baseA * (shapeFloor + (1f - shapeFloor) * lw)
                        * (1f - 0.35f * _glowDiffuse);
                    if (hasGlow)
                    {
                        float a0 = Mathf.Min(0.9f, _glow * shaped);
                        // Eight stops on a smoothstep-complement power curve: C1-smooth at BOTH
                        // ends (no perceptible start or stop slope) and gamma-friendly — the
                        // Mach-band killer (play-test round 6). The exponent flattens with
                        // GlowDiffuse (2 = tight rim, 0.65 = wide haze; near the outer end
                        // (1-s)^p ~ (1-t)^(2p), so any p > 0.5 keeps the landing slope zero).
                        float pFall = Mathf.Lerp(2f, 0.65f, _glowDiffuse);
                        for (int gs = 0; gs < 8; gs++)
                        {
                            float t = (gs + 1) / 8f;
                            float s = t * t * (3f - 2f * t);
                            float wgt = Mathf.Pow(1f - s, pFall);
                            Color g = halo; g.a = a0 * wgt;
                            _stopC[inStops + baseStops + gs] = g;
                        }
                        // CONTINUITY (play-test: "hard edge around the halo"): the base ramp
                        // used to fade to alpha-0 at the panel edge while the halo restarted
                        // there visible — a hard step. Land the base fade ON the halo's inner
                        // value instead: border -> glow -> nothing in one unbroken ramp.
                        Color h0 = halo; h0.a = a0;
                        _stopC[inStops + baseStops - 1] = h0;
                    }
                    if (hasGlowIn)
                    {
                        // The same light mirrored INWARD: the frame's glow composites source-
                        // over onto the glass fill (rgb pulls toward the accent hue, alpha
                        // rises) and decays with the precomputed _inW falloff. The fill-ramp
                        // stop carries the full-strength value so the profile runs continuous
                        // through the border: glass -> inner glow -> frame -> halo.
                        float aGi = Mathf.Min(0.9f, _glowInner * shaped);
                        for (int gs = 0; gs < inStops; gs++)
                            _stopC[gs] = GlowOver(halo, aGi * _inW[gs], fillC);
                        _stopC[inStops] = GlowOver(halo, aGi, _stopC[inStops]);
                    }
                }
            }

            AddVertFx(vh, Vector3.zero, FillAt(0f, hh), 0f); // fill fan centre (marker 0)

            // Corner fan density must match the OUTERMOST ring, not just the corner radius:
            // a sharp corner (rc ~0.5) gets the minimum 3 wedges, which was invisible when
            // the mesh ended at the border — but with a wide soft-edge/glow skirt those 3
            // wedges span 80+px, and the directional light weight differs at each wedge
            // boundary, which reads as hard dark/bright RAYS at corners (play-test round 9).
            // skirtExtra is zero at defaults, so baseline meshes keep the classic fan.
            float skirtExtra = Mathf.Max(0f, _stopD[stops - 1] - (hasBorder ? bw + f : f));

            // Straight-edge column spacing: the classic ~48px is fine for warp fidelity, but
            // the ripple repeats every ~100/freq px — a skirt (glow/soft edge) turns every
            // column into a visible ray, so columns must sample the ripple ≥ ~4x per cycle
            // or it ALIASES into spokes (play-test round 10). Densify only when both a
            // ripple and a skirt exist, so default meshes stay byte-identical.
            float edgeStep = 48f;
            if (_edgeRipple > 0.004f && skirtExtra > 0.5f)
                edgeStep = Mathf.Clamp(25f / Mathf.Max(0.5f, _edgeRippleFreq), 10f, 48f);

            // Interior-angle-aware inner-glow miter: the plain distance taper (cap = distance
            // to the corner) is only right for RIGHT-ANGLE corners. At an ACUTE trapezoid
            // corner the adjacent edge slants over the band sooner, and the distance taper
            // pushed inner-glow verts straight THROUGH the slant — a lit flap escaped the
            // panel (play-test round 15 screenshot, top bar). Safe depth at distance a from
            // the corner is a·tan(θ/2) with θ the interior angle; θ = π − sweep, so the
            // factor is 1/tan(sweep/2). Obtuse corners get a deeper (>1) allowance, capped.
            if (inStops > 0)
            {
                for (int c = 0; c < 4; c++)
                {
                    float aInC = NormalAngle(_centers[(c + 3) % 4], _centers[c]);
                    float aOutC = NormalAngle(_centers[c], _centers[(c + 1) % 4]);
                    while (aOutC < aInC) aOutC += Mathf.PI * 2f;
                    float half = Mathf.Max(0.12f, (aOutC - aInC) * 0.5f);
                    _miter[c] = Mathf.Clamp(1f / Mathf.Tan(half), 0.1f, 3f);
                }
            }

            // One column = `stops` verts along the outward normal at a contour point. Each stop
            // carries its own uv0.y marker (_stopM) so shader rim effects fade across the halo.
            // innerCap = this column's safe INWARD depth (corner-arc columns: the corner radius;
            // edge columns: the 45° miter distance to the sharp corner). The inner band is
            // SCALED into that depth — not clamped to it: clamping truncated the falloff
            // mid-gradient, which drew bright hard-ended wedges into every corner (play-test
            // round 14). Compressing instead means every column still fades fully to zero, and
            // adjacent edges' bands both reach exactly zero ON the corner bisector, so the
            // miter seam carries no discontinuity at all.
            void EmitColumn(Vector2 dir, Vector2 onShape, float innerCap)
            {
                ColumnColors(dir, onShape);
                float bandD = inStops > 0 ? Mathf.Max(rampD, Mathf.Min(glowInD, innerCap)) : 0f;
                for (int s = 0; s < stops; s++)
                {
                    float d = s < inStops
                        ? Mathf.Lerp(-bandD, -rampD, s / (float)inStops)
                        : _stopD[s];
                    AddVertFx(vh, onShape + dir * d, _stopC[s], _stopM[s]);
                }
            }

            int columns = 0;
            for (int c = 0; c < 4; c++)
            {
                Vector2 prev = _centers[(c + 3) % 4];
                Vector2 cur = _centers[c];
                Vector2 next = _centers[(c + 1) % 4];
                float rc = _radii[c];
                float aIn = NormalAngle(prev, cur);
                float aOut = NormalAngle(cur, next);
                while (aOut < aIn) aOut += Mathf.PI * 2f; // CCW sweep

                // Contour: each corner's arc runs from the incoming edge's outward normal
                // to the outgoing edge's, at THAT corner's radius — arc endpoints land on
                // the rect sides, so unequal neighbours still connect with true side lines.
                // The skirt term densifies the fan in proportion to how far the glow/soft
                // edge reaches AND how wide this corner's sweep is (trapezoid slant corners
                // sweep well past 90°); zero-skirt panels keep the classic Ceil(rc/2) fan.
                int segCap = skirtExtra > 0.5f ? 32 : 12;
                int cornerSegs = Mathf.Clamp(Mathf.CeilToInt(rc * 0.5f
                    + skirtExtra * 0.35f * ((aOut - aIn) / (Mathf.PI * 0.5f))), 3, segCap);
                for (int i = 0; i <= cornerSegs; i++)
                {
                    float a = Mathf.Lerp(aIn, aOut, i / (float)cornerSegs);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    EmitColumn(dir, cur + dir * rc, Mathf.Max(rc, rampD));
                    columns++;
                }

                // EDGE SUBDIVISION: the straight run to the NEXT corner's arc start gets
                // intermediate columns every ~48px. Without them a 2500px top bar is one
                // quad — the visor warp can only move VERTICES, so the bar (and everything
                // aligned to it) stayed a dead-straight line while small elements curved
                // around it (play-test: "the interior of the top bar simply doesn't curve").
                {
                    float rNext = _radii[(c + 1) % 4];
                    var edgeDir = new Vector2(Mathf.Cos(aOut), Mathf.Sin(aOut));
                    Vector2 from = cur + edgeDir * rc;
                    Vector2 to = next + edgeDir * rNext;
                    float len = (to - from).magnitude;
                    // Cap 48 at the classic spacing (byte-identity for giant panels at
                    // defaults); the densified path may need ~240 on a full-width top bar.
                    int edgeSegCap = edgeStep < 47.5f ? 240 : 48;
                    int segs = Mathf.Min(edgeSegCap, Mathf.FloorToInt(len / edgeStep));
                    // The inner-glow MITER needs columns to carve its 45° taper even on short
                    // edges — a 90px box otherwise interpolates the whole edge between two
                    // corner-clamped columns and the glow never reaches its full depth.
                    if (inStops > 0)
                        segs = Mathf.Max(segs, Mathf.Min(8, Mathf.FloorToInt(len / 14f)));
                    for (int i = 1; i <= segs; i++)
                    {
                        float et = i / (float)(segs + 1);
                        Vector2 p = Vector2.Lerp(from, to, et);
                        // Inner-glow depth TAPERS toward each corner (45° miter): without it,
                        // adjacent edges' full-depth bands OVERLAP in the corner square and
                        // the doubled alpha draws bright DIAGONAL rays across small boxes
                        // (play-test round 13, the belt/hand-box X pattern). Capping each
                        // column at its distance to the sharp corner makes the two bands meet
                        // exactly on the corner bisector with no double-draw.
                        float innerCap = glowInD;
                        if (inStops > 0)
                            innerCap = Mathf.Min(glowInD, Mathf.Min(
                                len * et * _miter[c] + rc,
                                len * (1f - et) * _miter[(c + 1) % 4] + rNext));
                        EmitColumn(edgeDir, p, innerCap);
                        columns++;
                    }
                }
            }

            // Close the loop by re-emitting the first column.
            {
                Vector2 prev = _centers[3];
                Vector2 cur = _centers[0];
                float aIn = NormalAngle(prev, cur);
                var dir = new Vector2(Mathf.Cos(aIn), Mathf.Sin(aIn));
                EmitColumn(dir, cur + dir * _radii[0], Mathf.Max(_radii[0], rampD));
                columns++;
            }

            for (int i = 0; i < columns - 1; i++)
            {
                int a = 1 + i * stops;
                int b = 1 + (i + 1) * stops;
                vh.AddTriangle(0, a, b); // fill fan to the innermost ring
                for (int s = 0; s < stops - 1; s++)
                {
                    vh.AddTriangle(a + s, a + s + 1, b + s + 1);
                    vh.AddTriangle(a + s, b + s + 1, b + s);
                }
            }
        }

        /// <summary>Outward normal angle of the edge a->b for a CCW polygon: the edge
        /// direction rotated -90° (interior lies on the left of a CCW edge).</summary>
        private static float NormalAngle(Vector2 a, Vector2 b)
        {
            var d = (b - a).normalized;
            return Mathf.Atan2(-d.x, d.y);
        }

        private static Color Fade(Color c) { c.a = 0f; return c; }

        /// <summary>Source-over composite of a glow layer (colour <paramref name="g"/> at
        /// alpha <paramref name="ga"/>) onto an existing vertex colour — the inner glow is
        /// LIGHT ON the glass, not a replacement fill, so hue pull and alpha rise follow
        /// the standard over operator instead of an ad-hoc lerp.</summary>
        private static Color GlowOver(Color g, float ga, Color under)
        {
            float ua = under.a * (1f - ga);
            float oa = ga + ua;
            if (oa < 1e-4f) { under.a = 0f; return under; }
            return new Color(
                (g.r * ga + under.r * ua) / oa,
                (g.g * ga + under.g * ua) / oa,
                (g.b * ga + under.b * ua) / oa,
                oa);
        }

        /// <summary>Emit one vertex carrying the per-element <see cref="FxStrength"/> in uv0.x
        /// and the legacy center/edge marker (0 = fill centre, 1 = contour) in uv0.y. All mesh
        /// emission routes through here so the FX channel is never forgotten on a vertex; with
        /// FxStrength 0 and the stock UI material (uv0 unread) this is pixel-identical to the
        /// pre-FX mesh. See master plan §12.2.</summary>
        private void AddVertFx(VertexHelper vh, Vector3 pos, Color32 c, float legacyMarker)
            => vh.AddVert(pos, c, new Vector2(_fxStrength, legacyMarker));

        /// <summary>Fill colour at height y: the sheen whitens (and slightly solidifies)
        /// the glass toward its top edge, quadratically, so the lower body stays dark.
        /// Sheen 0 returns <see cref="Graphic.color"/> untouched.</summary>
        private Color FillAt(float y, float hh)
        {
            if (_sheen <= 0.004f) return color;
            float t = Mathf.Clamp01((y + hh) / (2f * hh)); // 0 bottom .. 1 top
            float w = _sheen * 0.30f * t * t;
            Color c = color;
            c.r += (1f - c.r) * w;
            c.g += (1f - c.g) * w;
            c.b += (1f - c.b) * w;
            c.a = Mathf.Min(1f, c.a * (1f + 0.30f * _sheen * t * t));
            return c;
        }

        // The two "lights" the border catches: a key from the upper-left and a fainter
        // rim from the lower-right (unit vectors of (-1,2) and (1,-2)).
        // internal so PolylineGraphic's EdgeLightDir defaults to the SAME key direction —
        // panels and lines must agree on where the light comes from.
        internal const float LightX = -0.4472f, LightY = 0.8944f;

        /// <summary>Border colour for an edge whose outward normal is <paramref name="dir"/>
        /// at contour point <paramref name="p"/>. The directional light weight <c>w</c> is
        /// computed ONCE, independent of Spec, so BorderFade / EdgeRipple / BorderSides all work
        /// even with whitening off: cubic falloff on the light dot picks WHICH edges catch light,
        /// a linear cross-panel term runs each catch brighter→dimmer, EdgeRipple adds an
        /// irregular shimmer along the contour, Spec whitens the lit line, BorderFade dissolves
        /// the unlit line toward zero, and BorderSides melts disabled sides away around the arcs.
        /// With every 0.9.0 property at its default this returns exactly the pre-0.9.0 colour.</summary>
        /// <summary>The directional light weight at a contour point (pre-whitening, ripple
        /// included) — shared by the border styling AND the glow halo, so the halo hugs the
        /// same lit sections the border brightens (concept art: glow follows the light, it
        /// is not a uniform skirt around the whole perimeter).</summary>
        private float BorderLightW(Vector2 dir, Vector2 p, float hw)
        {
            float w = BorderLightSmooth(dir, p, hw);

            // EdgeRipple: irregular light/dark shimmer along the contour. Layered incommensurate
            // sines (irrational frequency ratios) read as organic pseudo-noise, not a periodic
            // wave. Position-based only — NO time term, so the mesh never rebuilds per frame.
            if (_edgeRipple > 0.004f)
            {
                float t = (p.x + p.y * 0.7f) * (_edgeRippleFreq * 0.0628f);
                float ripple = 1f + _edgeRipple * (0.32f * Mathf.Sin(t)
                    + 0.24f * Mathf.Sin(t * 2.417f + 1.7f)
                    + 0.14f * Mathf.Sin(t * 5.089f + 4.2f));
                // Ceiling 2 (not 1): overdriven ripple (slider > 1) pushes crests PAST the
                // nominal light weight so spec whitening saturates and the halo brightens at
                // the catches, while troughs clip to fully dark — the "more extremes" ask.
                // Every consumer is safe with w in [0,2]: Spec and BorderFade re-clamp
                // internally, and the halo term caps a0 itself.
                w = Mathf.Clamp(w * ripple, 0f, 2f);
            }
            return w;
        }

        /// <summary>The smooth (ripple-free) directional light weight: cubic key-light catch
        /// plus the fainter opposing rim, running brighter→dimmer along each edge. Split out
        /// of <see cref="BorderLightW"/> so the halo can blend toward it as GlowDiffuse rises.</summary>
        private float BorderLightSmooth(Vector2 dir, Vector2 p, float hw)
        {
            float dot = dir.x * LightX + dir.y * LightY;
            float k1 = Mathf.Max(0f, dot);
            float k2 = Mathf.Max(0f, -dot);
            float tx = Mathf.Clamp01((p.x + hw) / (2f * hw)); // 0 left .. 1 right
            return k1 * k1 * k1 * (1f - 0.55f * tx)
                 + 0.5f * k2 * k2 * k2 * (0.25f + 0.75f * tx);
        }

        private Color BorderAt(Vector2 dir, Vector2 p, float hw)
        {
            float w = BorderLightW(dir, p, hw);

            Color c = BorderColor;

            // Spec: whiten the line where it faces the key light (formula/output unchanged).
            if (_spec > 0.004f)
            {
                float ws = Mathf.Clamp01(w * _spec * 1.6f);
                c.r += (1f - c.r) * ws;
                c.g += (1f - c.g) * ws;
                c.b += (1f - c.b) * ws;
                c.a += (1f - c.a) * ws * 0.85f;
            }

            // BorderFade: the border's ALPHA follows the light — lit stays, unlit dissolves to 0.
            if (_borderFade > 0.004f)
                c.a *= Mathf.Lerp(1f, Mathf.Clamp01(w * 2.2f), _borderFade);

            // BorderSides: per-side visibility, smoothly blended by the outward-normal weight so a
            // disabled side melts away around the corner arc instead of hard-cutting.
            if (_borderSides != 15)
            {
                float wT = Mathf.Clamp01(dir.y), wB = Mathf.Clamp01(-dir.y);
                float wR = Mathf.Clamp01(dir.x), wL = Mathf.Clamp01(-dir.x);
                float sum = wT + wB + wR + wL;
                float en = 0f;
                if ((_borderSides & 1) != 0) en += wT;
                if ((_borderSides & 2) != 0) en += wR;
                if ((_borderSides & 4) != 0) en += wB;
                if ((_borderSides & 8) != 0) en += wL;
                c.a *= en / Mathf.Max(1e-4f, sum);
            }

            return c;
        }
    }
}
