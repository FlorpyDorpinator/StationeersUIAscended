using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// A filled disc with an optional rim, generated procedurally.
    ///
    /// The hub used to be a plain <see cref="Image"/> with no sprite assigned — Unity renders
    /// that as a white QUAD, which is why the centre of the radial was a square. A sprite would
    /// work, but it would need bundling; a generated mesh needs nothing.
    ///
    /// EDGE TREATMENT (2026-07-25). A ring is the only UIA surface that is NOT a
    /// <see cref="Hud.PanelGraphic"/>, so under a theme whose visible panel edge comes from the
    /// edge-LIGHT family rather than from raw border width it used to read as a different
    /// material entirely (FlorpyDorp: "there is no edge glass kind of effect ... on this
    /// element"). The properties below let a caller push that same family in: the sub-pixel
    /// policy, the directional edge light, the border fade and a cheap outer halo. EVERY one of
    /// them is inert at its default, and the whole per-column colour path is skipped unless one
    /// is set, so the radial hub / search hub / parked chips (no Def, widths > 1px) emit exactly
    /// the mesh they always did.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CircleGraphic : MaskableGraphic
    {
        private float _radius = 100f;
        private float _borderWidth;
        private Color _borderColor = Color.clear;
        private float _hairlineFloor;
        private float _edgeSpec;
        private Color _edgeTint = Color.white;
        private float _edgeFade;
        private float _glowStrength;
        private float _glowWidth = 14f;
        private Color _glowColor = Color.clear;

        // The mesh is a function of all of these, so each setter dirties on CHANGE. (Callers that
        // already call Refresh() by hand — the radial hubs, the parked chips — are unaffected:
        // a dirty flag set twice in one frame still rebuilds once. The portrait ring never called
        // Refresh at all, so before this its alarm-pulse recolour only reached the screen when
        // something else happened to dirty the mesh.)
        public float BorderWidth
        {
            get { return _borderWidth; }
            set { if (!Mathf.Approximately(_borderWidth, value)) { _borderWidth = value; SetVerticesDirty(); } }
        }

        public Color BorderColor
        {
            get { return _borderColor; }
            set { if (_borderColor != value) { _borderColor = value; SetVerticesDirty(); } }
        }

        /// <summary>Sub-pixel border policy.
        /// 0 (default) = the classic COVERAGE fade: a sub-1px rim draws 1px wide with its alpha
        /// scaled by the missing coverage (phone-wire AA, right for a standalone stroke).
        /// &gt; 0 = PANEL PARITY: keep the authored width (floored at this value) and the authored
        /// alpha, and let the feather ramps either side carry it — which is literally what
        /// <see cref="Hud.PanelGraphic"/> does (its stop ladder runs fill → border → fade with a
        /// full feather on both sides and NO coverage term, PanelGraphic.cs "hasBorder" gate
        /// `bw > 0.05f`). The coverage form is why the first fix for the vanishing portrait ring
        /// did not land: at the shipped themes' global width of 0.076px it multiplied the rim
        /// alpha by 0.076 (~invisible), while every panel beside it kept the full border colour
        /// at the peak of a ~2px ramp.</summary>
        public float HairlineFloor
        {
            get { return _hairlineFloor; }
            set { if (!Mathf.Approximately(_hairlineFloor, value)) { _hairlineFloor = value; SetVerticesDirty(); } }
        }

        /// <summary>Directional edge light on the rim, same meaning and same maths as
        /// PanelGraphic's Spec: where the rim faces the key light it is pulled toward
        /// <see cref="EdgeTint"/> and its alpha is lifted toward opaque. This — not border width
        /// — is what actually makes a panel edge visible under the shipped themes (they run
        /// EdgeLightStrength 1.5+ against a 0.076px border). 0 = off.</summary>
        public float EdgeSpec
        {
            get { return _edgeSpec; }
            set { if (!Mathf.Approximately(_edgeSpec, value)) { _edgeSpec = value; SetVerticesDirty(); } }
        }

        /// <summary>The colour the edge light whitens TOWARD (PanelGraphic.LightTint()).</summary>
        public Color EdgeTint
        {
            get { return _edgeTint; }
            set { if (_edgeTint != value) { _edgeTint = value; SetVerticesDirty(); } }
        }

        /// <summary>Border fade: the UNLIT arc of the rim dissolves toward transparent, so the
        /// ring reads as a lit frame rather than a uniform outline. 0 = solid all the way round.</summary>
        public float EdgeFade
        {
            get { return _edgeFade; }
            set { if (!Mathf.Approximately(_edgeFade, value)) { _edgeFade = value; SetVerticesDirty(); } }
        }

        /// <summary>Outer halo strength — a cheap 4-stop glow band outside the rim, light-shaped
        /// by the same soft cosine lobe the panel halo uses. 0 = no halo (no extra vertices).</summary>
        public float GlowStrength
        {
            get { return _glowStrength; }
            set { if (!Mathf.Approximately(_glowStrength, value)) { _glowStrength = value; SetVerticesDirty(); } }
        }

        /// <summary>Halo reach in px. Only read while <see cref="GlowStrength"/> is non-zero.</summary>
        public float GlowWidth
        {
            get { return _glowWidth; }
            set { if (!Mathf.Approximately(_glowWidth, value)) { _glowWidth = value; SetVerticesDirty(); } }
        }

        /// <summary>Halo hue. Alpha 0 (default) = derive it from <see cref="BorderColor"/>, which
        /// is what a panel does (its halo is the fill lerped 75% toward the border colour).</summary>
        public Color GlowColor
        {
            get { return _glowColor; }
            set { if (_glowColor != value) { _glowColor = value; SetVerticesDirty(); } }
        }

        public void SetRadius(float radius)
        {
            if (Mathf.Approximately(_radius, radius)) return;
            _radius = radius;
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        /// <summary>Width of the anti-aliasing ramp, in pixels. Live-tunable from F10.</summary>
        private static float Feather => UIAConfig.RadialEdgeFeather != null
            ? UIAConfig.RadialEdgeFeather.Value : 1.25f;

        private const int GlowStops = 4;
        private static readonly float[] _stopR = new float[4 + GlowStops];
        private static readonly Color[] _stopC = new Color[4 + GlowStops];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_radius <= 0f) return;

            int segments = Mathf.Clamp(Mathf.CeilToInt(_radius * 1.2f), 48, 256);
            float bw = _borderWidth;
            Color rim = _borderColor;
            if (_hairlineFloor > 0f)
            {
                // Panel parity — see HairlineFloor. No coverage term: the ramps carry the line.
                bw = Mathf.Max(bw, _hairlineFloor);
            }
            else if (bw > 0.05f && bw < 1f)
            {
                // Classic coverage hairline (the standalone-stroke look).
                rim.a *= bw;
                bw = 1f;
            }
            // The AUTHORED width still decides whether a rim exists at all, so width 0 draws none.
            bool hasBorder = bw > 0.05f && rim.a > 0.004f && _borderWidth > 0.05f;
            float f = Feather;
            bool glow = hasBorder && _glowStrength > 0.004f && _glowWidth > 0.5f;
            // Everything below the gate is the classic constant-colour mesh, byte for byte.
            bool lit = hasBorder && (_edgeSpec > 0.004f || _edgeFade > 0.004f || glow);

            // Rings from the fan edge outwards. The rim needs a ramp on BOTH sides: an alpha-0
            // fringe outside, and a colour ramp from the fill on the inside — otherwise the
            // inner boundary of the orange rim is a hard step and stair-steps just as badly.
            int stops;
            if (hasBorder)
            {
                float rampStart = Mathf.Max(1f, _radius - f);
                _stopR[0] = rampStart;        _stopC[0] = color;                 // fill up to here
                _stopR[1] = _radius;          _stopC[1] = rim;                   // ramp -> rim
                _stopR[2] = _radius + bw;     _stopC[2] = rim;                   // solid rim
                _stopR[3] = _radius + bw + f; _stopC[3] = Fade(rim);             // fringe out
                stops = 4;
                if (glow)
                {
                    for (int gs = 0; gs < GlowStops; gs++)
                        _stopR[4 + gs] = _stopR[3] + _glowWidth * ((gs + 1) / (float)GlowStops);
                    stops += GlowStops;
                }
            }
            else
            {
                _stopR[0] = _radius;     _stopC[0] = color;
                _stopR[1] = _radius + f; _stopC[1] = Fade(color);
                stops = 2;
            }

            // Fan centre, then one vertex per stop per column.
            vh.AddVert(Vector2.zero, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                if (lit) ColumnColors(dir, rim, glow);
                for (int s = 0; s < stops; s++)
                    vh.AddVert(dir * _stopR[s], _stopC[s], Vector2.one);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = 1 + i * stops;
                int b = 1 + (i + 1) * stops;
                vh.AddTriangle(0, a, b);                 // fill fan
                for (int s = 0; s < stops - 1; s++)      // bands out to the fringe
                {
                    vh.AddTriangle(a + s, a + s + 1, b + s + 1);
                    vh.AddTriangle(a + s, b + s + 1, b + s);
                }
            }
        }

        /// <summary>Per-column rim (and halo) colour under the edge treatment — the ring's share
        /// of PanelGraphic's BorderAt/halo maths, evaluated on the circle's outward normal.
        /// Deliberately an APPROXIMATION of the panel path, not a port of it: the key direction,
        /// rim, sharpness and tint come from the SAME statics every panel uses, so the ring lights
        /// on the same side of the HUD, but the edge RIPPLE, the inner glow band and the SDF
        /// fragment path are left out — they are per-element state a ring has no author-facing
        /// controls for, and the goal is "belongs to the same family", not shader parity.</summary>
        private void ColumnColors(Vector2 dir, Color rim, bool glow)
        {
            // 0 (left) .. 1 (right) across the shape, matching PanelGraphic's cross-panel run.
            float tx = Mathf.Clamp01(dir.x * 0.5f + 0.5f);
            Color c = rim;
            if (_edgeSpec > 0.004f || _edgeFade > 0.004f)
            {
                float w = Hud.PanelGraphic.KeyLightWeight(dir, tx);
                if (_edgeSpec > 0.004f)
                {
                    // Same constants as PanelGraphic.BorderAt: the lit arc is pulled toward the
                    // tint and, crucially, its ALPHA is lifted 85% of the way to opaque. That
                    // alpha lift is what a 0.076px themed border actually shows on screen.
                    float ws = Mathf.Clamp01(w * _edgeSpec * 1.6f);
                    Color tint = _edgeTint;
                    c.r += (tint.r - c.r) * ws;
                    c.g += (tint.g - c.g) * ws;
                    c.b += (tint.b - c.b) * ws;
                    c.a += (1f - c.a) * ws * 0.85f;
                }
                if (_edgeFade > 0.004f)
                    c.a *= Mathf.Lerp(1f, Mathf.Clamp01(w * 2.2f), _edgeFade);
            }
            _stopC[0] = color;
            _stopC[1] = c;
            _stopC[2] = c;
            _stopC[3] = Fade(c);
            if (!glow) return;

            Color halo = _glowColor.a > 0.004f ? _glowColor : rim;
            // The halo rides the SOFT cosine lobe, never the sharp specular exponent (the panel
            // learned this the hard way: an extruded sharp lobe grows rays out of corners).
            float lw = Mathf.Min(1.2f, Hud.PanelGraphic.KeyLightWeightSoft(dir, tx));
            float a0 = Mathf.Min(0.9f, _glowStrength * 0.55f * Mathf.Max(0.5f, halo.a)
                * (0.35f + 0.65f * lw));
            for (int gs = 0; gs < GlowStops; gs++)
            {
                float t = (gs + 1) / (float)GlowStops;
                float s = t * t * (3f - 2f * t);           // smoothstep-complement falloff:
                Color g = halo;                            // C1 at both ends, so no Mach band
                g.a = a0 * Mathf.Pow(1f - s, 1.2f);
                _stopC[4 + gs] = g;
            }
            // Continuity: land the rim's outward fade ON the halo's inner value instead of 0,
            // or the halo restarts visible at the fringe and draws a hard ring around itself.
            Color h0 = halo;
            h0.a = a0;
            _stopC[3] = h0;
        }

        private static Color Fade(Color c) { c.a = 0f; return c; }
    }
}
