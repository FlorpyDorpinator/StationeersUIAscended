using System.Collections.Generic;
using StationeersUIMod.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// An arbitrary stroked polyline — the editor's freehand HUD-line tool and the connector
    /// accents. Each segment is an offset quad (±normal · Width/2); interior corners are BEVEL
    /// joins (one outer triangle, no miter math, no rounding) and open ends get flat caps.
    ///
    /// Like the other HUD graphics this draws on an overlay canvas with no MSAA, so the AA is
    /// baked: every outer edge — both long sides, the bevel wedges, and the cap ends — carries a
    /// Width/2 + feather skirt of alpha-0 vertices, and the colour ramp across it IS the fringe.
    ///
    /// The stroke colour is the inherited <see cref="Graphic.color"/> (Graphic already dirties
    /// the mesh on colour change) so the editor's generic colour control drives it directly.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PolylineGraphic : MaskableGraphic, IHudFxGraphic
    {
        // Per-instance point list, reused across rebuilds (SetPoints only copies on a real diff).
        private readonly List<Vector2> _points = new List<Vector2>();
        private bool _closed;
        private float _width = 2f;

        /// <summary>Stroke width in px. Floored at 0.05 (a tiny non-zero guard against
        /// degenerate normal/join math) rather than the old 0.5 CLAMP: a sub-pixel width is
        /// honoured by the phone-wire coverage-fade in <see cref="OnPopulateMesh"/>, which
        /// draws a 1px hairline at reduced alpha so a thin line FADES OUT smoothly toward
        /// zero instead of snapping to 0.5px and flickering/vanishing.</summary>
        public float Width
        {
            get => _width;
            set
            {
                float v = float.IsNaN(value) ? 0.05f : Mathf.Max(0.05f, value);
                if (!Mathf.Approximately(_width, v)) { _width = v; SetVerticesDirty(); }
            }
        }

        private float _fadeEnds;

        /// <summary>Fraction of the line's arc length (0..0.49) over which each OPEN end
        /// fades from nothing to full alpha — the visor-rim "line that dissolves at its
        /// ends". 0 (default) keeps the classic hard caps; closed loops ignore this.</summary>
        public float FadeEnds
        {
            get => _fadeEnds;
            set
            {
                float v = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 0.49f);
                if (!Mathf.Approximately(_fadeEnds, v)) { _fadeEnds = v; SetVerticesDirty(); }
            }
        }

        private float _fxStrength;

        /// <summary>0..1 — per-element effect strength baked into uv0.x on every vertex (see
        /// <see cref="IHudFxGraphic"/>). Dirties the mesh so the value is baked on the next
        /// rebuild; 0 (default) leaves uv0.x at 0, which the stock UI material never samples,
        /// so rendered pixels are unchanged until an FX material reads the channel.</summary>
        public float FxStrength
        {
            get => _fxStrength;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_fxStrength, value)) { _fxStrength = value; SetVerticesDirty(); }
            }
        }

        private float _edgeLight;

        /// <summary>0..2 — directional edge-light gain. Each segment's solid colour is
        /// brightened by (1 + EdgeLight · saturate(dot(segmentNormal, <see cref="EdgeLightDir"/>)))
        /// and clamped to 1 per channel (colours are LDR), so strokes whose left-normal faces
        /// the key light glint while others stay flat. 0 (default) = off — flat colour, and
        /// the mesh is byte-identical to the pre-effect output. Composes with FadeEnds.</summary>
        public float EdgeLight
        {
            get => _edgeLight;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 2f);
                if (!Mathf.Approximately(_edgeLight, value)) { _edgeLight = value; SetVerticesDirty(); }
            }
        }

        // Default = PanelGraphic's key light (upper-left, unit(-1,2)), reused verbatim so a
        // lit line and a lit panel next to it catch the same light. Already unit length.
        private Vector2 _edgeLightDir = new Vector2(PanelGraphic.LightX, PanelGraphic.LightY);

        /// <summary>Unit direction the edge-light comes FROM (dotted against each segment's
        /// left normal). Defaults to PanelGraphic's key light so panels and lines agree.
        /// Normalized on set; a NaN or zero vector falls back to the panel key so the effect
        /// can never divide by zero or bake a garbage direction from a bad profile.</summary>
        public Vector2 EdgeLightDir
        {
            get => _edgeLightDir;
            set
            {
                Vector2 v = value;
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || v.sqrMagnitude < 1e-6f)
                    v = new Vector2(PanelGraphic.LightX, PanelGraphic.LightY);
                else
                    v = v.normalized;
                if (!Mathf.Approximately(_edgeLightDir.x, v.x) || !Mathf.Approximately(_edgeLightDir.y, v.y))
                { _edgeLightDir = v; SetVerticesDirty(); }
            }
        }

        private float _edgeRipple;
        private float _edgeRippleFreq = 2f;

        /// <summary>0..2.5 — an irregular light/dark shimmer ALONG the stroke, layered on top of
        /// <see cref="EdgeLight"/> (it modulates the edge-light brightness, so it only bites when
        /// EdgeLight &gt; 0). Above ~1.4 the modulation overdrives: troughs clip fully dark,
        /// crests overshoot (extra brightening at the catches) — matches PanelGraphic's
        /// EdgeRipple extremes. Position-based pseudo-noise — NO time animation, so the mesh
        /// never rebuilds per frame. 0 (default) = off.</summary>
        public float EdgeRipple
        {
            get => _edgeRipple;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 2.5f);
                if (!Mathf.Approximately(_edgeRipple, value)) { _edgeRipple = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0.05..8 — <see cref="EdgeRipple"/> base frequency in cycles per ~100px.
        /// Default 2. Low = a WIDE, slow light→dark sweep along the stroke (floor dropped from
        /// 0.5). Inert while EdgeRipple is 0, so the default is byte-identity-safe.</summary>
        public float EdgeRippleFreq
        {
            get => _edgeRippleFreq;
            set
            {
                value = float.IsNaN(value) ? 2f : Mathf.Clamp(value, 0.05f, 8f);
                if (!Mathf.Approximately(_edgeRippleFreq, value)) { _edgeRippleFreq = value; SetVerticesDirty(); }
            }
        }

        private float _rippleSmooth;

        /// <summary>0..1 — fades the ripple's higher harmonics toward a single clean sine (1 = a
        /// smooth light→dark gradient, 0 = the classic layered noise). Pair with a low
        /// <see cref="EdgeRippleFreq"/> for one broad soft sweep.</summary>
        public float RippleSmooth
        {
            get => _rippleSmooth;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_rippleSmooth, value)) { _rippleSmooth = value; SetVerticesDirty(); }
            }
        }

        private float _glow;
        private float _glowWidth = 24f;
        private float _glowDiffuse;
        private float _glowExtraDiffuse;
        private float _flowSpeed;

        /// <summary>0..2 — luminous halo skirt OUTSIDE both sides of the stroke (and around the
        /// caps): the panel/shape halo recipe with the line's single stroke colour. Light-shaped
        /// by the SOFT cosine lobe only (<see cref="PanelGraphic.KeyLightWeightSoft"/> — the
        /// sharp specular exponent painted radial rays at bends, 2026-07-16). 0 (default) = off,
        /// mesh byte-identical to the pre-halo output.</summary>
        public float Glow
        {
            get => _glow;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 2f);
                if (!Mathf.Approximately(_glow, value)) { _glow = value; SetVerticesDirty(); }
            }
        }

        /// <summary>Halo reach in px (6..160 — the mesh cap shared with the shape halo).</summary>
        public float GlowWidth
        {
            get => _glowWidth;
            set
            {
                value = float.IsNaN(value) ? 24f : Mathf.Clamp(value, 6f, 160f);
                if (!Mathf.Approximately(_glowWidth, value)) { _glowWidth = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..1 — tight rim glow (0) toward wide soft haze (1); the shared recipe.</summary>
        public float GlowDiffuse
        {
            get => _glowDiffuse;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_glowDiffuse, value)) { _glowDiffuse = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..1 — softness beyond GlowDiffuse's ceiling (see PanelGraphic).</summary>
        public float GlowExtraDiffuse
        {
            get => _glowExtraDiffuse;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (!Mathf.Approximately(_glowExtraDiffuse, value)) { _glowExtraDiffuse = value; SetVerticesDirty(); }
            }
        }

        /// <summary>0..4 — the MOVING edge ripple (0 = static). When flowing, the mesh stops
        /// baking the static position-keyed shimmer into <see cref="EdgeLight"/> and instead
        /// writes (arc-length, weight·EdgeRipple, freq, speed) into uv1; HudEdgeFX animates a
        /// travelling 3-harmonic wave off _Time — the same contract PolygonPanelGraphic uses.
        /// Inert unless the caller also binds a bundle material (HudEdgeFX/HudGlass) to the
        /// graphic; with the stock UI material or an old bundle the uv1 payload is ignored, so
        /// this fails soft to the static look. Requires <see cref="EdgeRipple"/> &gt; 0 (it is
        /// the wave's amplitude weight).</summary>
        public float FlowSpeed
        {
            get => _flowSpeed;
            set
            {
                value = float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 4f);
                if (!Mathf.Approximately(_flowSpeed, value)) { _flowSpeed = value; SetVerticesDirty(); }
            }
        }

        /// <summary>Replace the polyline. Copies <paramref name="pts"/> into the internal list and
        /// only dirties the mesh when the point count, any point, or the closed flag actually
        /// changed — the editor may push this every frame while dragging.</summary>
        public void SetPoints(IList<Vector2> pts, bool closed = false)
        {
            int n = pts != null ? pts.Count : 0;
            bool changed = closed != _closed || n != _points.Count;
            if (!changed)
                for (int i = 0; i < n; i++)
                    if (_points[i] != pts[i]) { changed = true; break; }
            if (!changed) return;

            _closed = closed;
            _points.Clear();
            for (int i = 0; i < n; i++) _points.Add(pts[i]);
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        private static float Feather => HudConfig.EdgeFeather != null
            ? HudConfig.EdgeFeather.Value : 1.25f;

        // NaN is spammed by a bad editor drag; one warning is enough to flag it in the log.
        private static bool _warnedNaN;

        // Scratch (single-threaded UGUI rebuild, shared like PanelGraphic._centers): the cleaned
        // point set plus per-segment direction/normal, so a rebuild allocates nothing.
        private static readonly List<Vector2> _clean = new List<Vector2>(64);
        private static readonly List<Vector2> _dir = new List<Vector2>(64);
        private static readonly List<Vector2> _nrm = new List<Vector2>(64);
        private static readonly List<float> _arc = new List<float>(64); // cumulative arc length
        private static readonly List<Vector2> _fine = new List<Vector2>(128); // subdivided points
        private static readonly List<float> _arcPx = new List<float>(128); // cumulative arc px (flow phase)
        private static readonly float[] _haloW = new float[8]; // halo stop decay weights

        // Per-rebuild halo shaping (set by PrepareHaloShaping when glow is on) — shared by the
        // fringe continuity colours in PopulateMeshCore AND the columns in EmitHalo, so the
        // fringe lands on EXACTLY the value the adjoining halo column base carries.
        private static float _hGw, _hShapeFloor, _hRippleShare, _hPeakDim, _hBaseA;
        private static float _hGwInterior; // closed loops: interior-side depth clamp
        private static int _hInteriorSgn;  // +1/-1 = which side faces the loop interior; 0 = open
        private static bool _warnedHaloVerts;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // Same tripwire as PanelGraphic: rebuild storms show as Hud.Mesh.* Calls/s.
            using (Profiling.ProfilicusUniversalis.Time("Hud.Mesh.Polyline"))
                PopulateMeshCore(vh);
        }

        private void PopulateMeshCore(VertexHelper vh)
        {
            vh.Clear();
            if (_points.Count < 2) return;

            // Drop NaN points and collapse zero-length segments up front, so the normal and join
            // math below never divides a zero-length direction.
            var pts = _clean;
            pts.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                Vector2 p = _points[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y))
                {
                    if (!_warnedNaN) { _warnedNaN = true; UIALog.Warn("PolylineGraphic: NaN point skipped."); }
                    continue;
                }
                if (pts.Count > 0 && NearlySame(pts[pts.Count - 1], p)) continue;
                pts.Add(p);
            }

            bool closed = _closed;
            // A closed loop whose last point restates the first would make a zero-length seam.
            if (closed && pts.Count >= 2 && NearlySame(pts[0], pts[pts.Count - 1]))
                pts.RemoveAt(pts.Count - 1);
            if (closed && pts.Count < 3) closed = false; // need three distinct points to loop
            int n = pts.Count;
            if (n < 2) return;

            bool hasGlow = _glow > 0.004f && _glowWidth > 0.05f;
            bool flowing = _flowSpeed > 0.004f && _edgeRipple > 0.004f;
            // Position-keyed effects (ripple shimmer, the halo's rippled light, the moving
            // flow wave) interpolate LINEARLY between mesh points — a 2-point line would show
            // one long gradient, not shimmer. Subdivide long spans (~36px, budgeted) only when
            // such an effect is on, so plain lines stay byte-identical. Inserted points are
            // collinear: Bevel() skips them (cross ~ 0) and per-segment normals are unchanged.
            if (_edgeRipple > 0.004f || hasGlow || flowing)
            {
                _fine.Clear();
                int subSegs = closed ? n : n - 1;
                int budget = 240 - n; // hard cap on inserted points (mesh size guard)
                for (int s = 0; s < subSegs; s++)
                {
                    Vector2 a = pts[s], b = pts[(s + 1) % n];
                    _fine.Add(a);
                    int cuts = Mathf.Min(Mathf.FloorToInt((b - a).magnitude / 36f), 16);
                    cuts = Mathf.Min(cuts, Mathf.Max(0, budget));
                    budget -= cuts;
                    for (int k = 1; k <= cuts; k++)
                        _fine.Add(Vector2.Lerp(a, b, k / (float)(cuts + 1)));
                }
                if (!closed) _fine.Add(pts[n - 1]);
                pts.Clear();
                for (int i = 0; i < _fine.Count; i++) pts.Add(_fine[i]);
                n = pts.Count;
            }

            float hw = _width * 0.5f;
            // Phone-wire AA (Persson/"Humus" coverage trick): a rasteriser can't draw a
            // stroke thinner than ~a pixel without it shimmering/dropping out, so we render at
            // a floor half-width and instead scale the SOLID alpha by the coverage the true
            // width WOULD have had. A 0.3px line => drawn at the floor @ ~proportional alpha,
            // fading smoothly to nothing as Width -> 0 rather than vanishing. Floor dropped from
            // 0.5 to 0.28 half-width (~0.56px) so genuinely hairline strokes can render finer
            // before the coverage takes over — a touch more shimmer risk at the extreme, worth
            // it for the thin-line ask. The fringe/fade colours derive from `solid` (alpha-0
            // copies), so they follow.
            float drawHw = Mathf.Max(hw, 0.28f);
            float cov = drawHw > 0f ? hw / drawHw : 1f; // <= 1
            float f = Feather;
            Color solid = color;
            solid.a *= cov;
            Color fade = solid; fade.a = 0f;
            int segCount = closed ? n : n - 1;

            // Per-segment unit direction and left normal (left of travel: n = (-dy, dx)).
            var dir = _dir; var nrm = _nrm;
            dir.Clear(); nrm.Clear();
            for (int s = 0; s < segCount; s++)
            {
                Vector2 d = (pts[(s + 1) % n] - pts[s]).normalized; // never zero: dups collapsed
                dir.Add(d);
                nrm.Add(new Vector2(-d.y, d.x));
            }

            // End-fade: per-point alpha weight from the arc-length fraction. Open lines
            // only — a loop has no ends. Weight 1 everywhere keeps the classic output.
            bool fading = !closed && _fadeEnds > 0.001f;
            var arc = _arc;
            arc.Clear();
            if (fading)
            {
                float total = 0f;
                arc.Add(0f);
                for (int i = 1; i < n; i++)
                {
                    total += (pts[i] - pts[i - 1]).magnitude;
                    arc.Add(total);
                }
                if (total < 1e-3f) fading = false;
                else for (int i = 0; i < n; i++) arc[i] = arc[i] / total;
            }

            // Absolute cumulative arc length (px) per point — the moving flow's travel phase
            // (uv1.x). Distinct from `arc` above, which is a normalized fraction for end-fade.
            // A closed loop wraps, so the last segment's length is included to seed the seam.
            if (flowing)
            {
                _arcPx.Clear();
                float run = 0f;
                _arcPx.Add(0f);
                for (int i = 1; i < n; i++)
                {
                    run += (pts[i] - pts[i - 1]).magnitude;
                    _arcPx.Add(run);
                }
            }
            // Per-point solid colour = base (already coverage-scaled) with the arc-length
            // end-fade folded into alpha, then the directional edge-light folded into rgb.
            // `segNormal` is the left normal of the segment/corner this vertex belongs to.
            Color SolidAt(int i, Vector2 segNormal)
            {
                Color c = solid;
                if (fading)
                {
                    float t = arc[i];
                    float w = Mathf.Clamp01(Mathf.Min(t, 1f - t) / _fadeEnds);
                    c.a *= w;
                }
                if (_edgeLight > 0f)
                {
                    // Brighten segments that face the key light; clamp per channel (LDR).
                    float d = Mathf.Clamp01(segNormal.x * _edgeLightDir.x + segNormal.y * _edgeLightDir.y);
                    // EdgeRipple: irregular shimmer along the stroke via layered incommensurate
                    // sines keyed on the point position (matches PanelGraphic.BorderAt). No time
                    // term — deterministic, no per-frame rebuild. When FLOWING, the static bake
                    // is suppressed (the HudEdgeFX wave off uv1 is the only ripple, or they'd
                    // stack a frozen copy under the moving one — the shape renderer's rule).
                    if (_edgeRipple > 0.004f && _flowSpeed <= 0.004f)
                    {
                        Vector2 p = pts[i];
                        float t = (p.x + p.y * 0.7f) * (_edgeRippleFreq * 0.0628f);
                        float harm = 1f - _rippleSmooth;
                        float ripple = 1f + _edgeRipple * (0.32f * Mathf.Sin(t)
                            + harm * (0.24f * Mathf.Sin(t * 2.417f + 1.7f)
                            + 0.14f * Mathf.Sin(t * 5.089f + 4.2f)));
                        // Ceiling 2: overdriven ripple (slider > 1) lets crests overshoot the
                        // nominal light dot (channels still clamp per-pixel below) while
                        // troughs clip fully dark — matches PanelGraphic.BorderLightW.
                        d = Mathf.Clamp(d * ripple, 0f, 2f);
                    }
                    // Pull the stroke TOWARD the configurable edge-light colour where it faces the
                    // key light (matches PanelGraphic.BorderAt — lines and borders tint alike). White
                    // reproduces the classic bright specular highlight.
                    float ws = Mathf.Clamp01(_edgeLight * d);
                    Color tint = PanelGraphic.LightTint();
                    c.r += (tint.r - c.r) * ws;
                    c.g += (tint.g - c.g) * ws;
                    c.b += (tint.b - c.b) * ws;
                }
                return c;
            }

            // Halo shaping is prepared BEFORE the ribbons: with glow on, the stroke's outer
            // fringe must land ON the halo's inner value instead of fading to alpha 0 — the
            // same continuity rule as PanelGraphic/PolygonPanelGraphic (an alpha-0 fringe
            // against an a0 halo base reads as a dark ~1px valley around the whole stroke;
            // review 2026-07-16). FringeAt returns that per-point, per-side landing colour.
            float rimBase = drawHw + f;
            if (hasGlow) PrepareHaloShaping(pts, closed, cov, rimBase);
            float EndW(int i)
            {
                if (!fading) return 1f;
                float t = arc[i];
                return Mathf.Clamp01(Mathf.Min(t, 1f - t) / _fadeEnds);
            }
            // The moving-flow travel phase (uv1.x) at point i; 0 when not flowing (AddVertFx
            // then emits an all-zero uv1 the shader treats as static).
            float ArcAt(int i) => flowing ? _arcPx[i] : 0f;
            Color FringeAt(int i, Vector2 sideNormal, float sgn, Color solidC)
            {
                if (!hasGlow || (_hInteriorSgn != 0 && (int)sgn == _hInteriorSgn
                        && _hGwInterior < 0.5f))
                {
                    Color c0 = solidC; c0.a = 0f; return c0;
                }
                Color c = color;
                c.a = HaloA0(pts[i], sideNormal, EndW(i));
                return c;
            }

            // Segment ribbons: fringe | core | fringe strips across the width.
            for (int s = 0; s < segCount; s++)
            {
                int ia = s, ib = (s + 1) % n;
                Vector2 a = pts[ia];
                Vector2 b = pts[ib];
                Vector2 nn = nrm[s];
                Color sa = SolidAt(ia, nn), sb = SolidAt(ib, nn);
                int bi = vh.currentVertCount;
                AddCross(vh, a, nn, drawHw, f, sa,
                    FringeAt(ia, nn, 1f, sa), FringeAt(ia, -nn, -1f, sa), ArcAt(ia)); // bi+0..3
                AddCross(vh, b, nn, drawHw, f, sb,
                    FringeAt(ib, nn, 1f, sb), FringeAt(ib, -nn, -1f, sb), ArcAt(ib)); // bi+4..7
                Strip(vh, bi + 0, bi + 1, bi + 4, bi + 5); // left fringe
                Strip(vh, bi + 1, bi + 2, bi + 5, bi + 6); // core
                Strip(vh, bi + 2, bi + 3, bi + 6, bi + 7); // right fringe
            }

            // Bevel joins: fill the outer wedge left open where two segments turn.
            int firstJoint = closed ? 0 : 1;
            int lastJoint = closed ? n : n - 1; // exclusive
            for (int j = firstJoint; j < lastJoint; j++)
            {
                int s0 = (j - 1 + segCount) % segCount; // incoming segment
                int s1 = j % segCount;                  // outgoing segment
                // Averaged corner normal for the edge-light (struct math, no allocation).
                Vector2 jn = (nrm[s0] + nrm[s1]).normalized;
                Color sj = SolidAt(j % n, jn);
                float cross = dir[s0].x * dir[s1].y - dir[s0].y * dir[s1].x;
                float bevelSgn = cross > 0f ? -1f : 1f; // the outer side of the turn
                Bevel(vh, pts[j], dir[s0], dir[s1], nrm[s0], nrm[s1], drawHw, f, sj,
                    FringeAt(j % n, nrm[s0] * bevelSgn, bevelSgn, sj),
                    FringeAt(j % n, nrm[s1] * bevelSgn, bevelSgn, sj), ArcAt(j % n));
            }

            // Flat caps on the open ends, pushed out by the feather so the line fades to
            // nothing. With end-fade active the end alpha is already 0 — skip the caps.
            if (!closed && !fading)
            {
                // Use the edge-lit end colour so the cap matches the ribbon's end cross
                // exactly (with EdgeLight 0 this is `solid`/`fade` — byte-identical).
                Color s0c = SolidAt(0, nrm[0]);
                Color s1c = SolidAt(n - 1, nrm[segCount - 1]);
                Color cap0 = color, cap1 = color;
                cap0.a = hasGlow ? HaloA0(pts[0], -dir[0], 1f) : 0f;
                cap1.a = hasGlow ? HaloA0(pts[n - 1], dir[segCount - 1], 1f) : 0f;
                Cap(vh, pts[0], -dir[0], nrm[0], drawHw, f, s0c,
                    FringeAt(0, nrm[0], 1f, s0c), FringeAt(0, -nrm[0], -1f, s0c),
                    hasGlow, cap0, ArcAt(0));
                Cap(vh, pts[n - 1], dir[segCount - 1], nrm[segCount - 1], drawHw, f, s1c,
                    FringeAt(n - 1, nrm[segCount - 1], 1f, s1c),
                    FringeAt(n - 1, -nrm[segCount - 1], -1f, s1c),
                    hasGlow, cap1, ArcAt(n - 1));
            }

            // Halo skirt: 8-stop luminous decay ribbons outside BOTH sides of the stroke,
            // wrapped around bends and caps — the panel/shape halo recipe with the stroke's
            // own colour as the halo hue.
            if (hasGlow)
                EmitHalo(vh, pts, dir, nrm, closed, segCount, drawHw, f, fading);
        }

        /// <summary>Fill the per-rebuild halo shaping fields + stop weights. The shared
        /// falloff family: smoothstep-complement power curve, exponent extended past the 0.65
        /// diffuse ceiling to the C1 bound 0.5 by ExtraDiffuse.</summary>
        private void PrepareHaloShaping(List<Vector2> pts, bool closed, float cov, float rimBase)
        {
            _hGw = Mathf.Min(_glowWidth, 160f);
            float pFall = Mathf.Lerp(Mathf.Lerp(2f, 0.65f, _glowDiffuse), 0.5f,
                _glowExtraDiffuse);
            for (int gs = 0; gs < 8; gs++)
            {
                float t = (gs + 1) / 8f;
                float s = t * t * (3f - 2f * t);
                _haloW[gs] = Mathf.Pow(1f - s, pFall);
            }
            _hShapeFloor = Mathf.Lerp(0.08f + 0.27f * _glowDiffuse, 0.62f, _glowExtraDiffuse);
            _hRippleShare = 0.35f * (1f - _glowDiffuse);
            _hPeakDim = 1f - 0.35f * _glowDiffuse;
            _hBaseA = Mathf.Max(0.5f, color.a * cov);

            // Closed loop: the interior side's skirt must not cross the medial axis, or the
            // opposite sides' inner skirts stack alpha inside small loops. Winding picks the
            // interior side (CCW = interior on the left/+n); depth clamps to the centroid
            // clearance. Open lines and large loops are untouched.
            _hInteriorSgn = 0;
            _hGwInterior = _hGw;
            if (closed)
            {
                int n = pts.Count;
                float area2 = 0f;
                Vector2 centroid = Vector2.zero;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = pts[i], b = pts[(i + 1) % n];
                    area2 += a.x * b.y - b.x * a.y;
                    centroid += a;
                }
                centroid /= n;
                _hInteriorSgn = area2 > 0f ? 1 : -1;
                float rMin = float.MaxValue;
                for (int i = 0; i < n; i++)
                    rMin = Mathf.Min(rMin, (pts[i] - centroid).magnitude);
                _hGwInterior = Mathf.Min(_hGw, Mathf.Max(0f, rMin - rimBase));
            }
        }

        /// <summary>Per-point halo alpha at full strength: the shared light-shaped recipe.
        /// The directional weight is the SOFT cosine lobe only — the sharp specular exponent
        /// extruded across a skirt anchors thin radial rays at bends (2026-07-16 rule). The
        /// ripple share rides the same soft base so a crest can't re-sharpen the lobe.</summary>
        private float HaloA0(Vector2 p, Vector2 outDir, float endW)
        {
            float lwS = PanelGraphic.KeyLightWeightSoft(outDir, 0.5f);
            float lw = lwS;
            if (_edgeRipple > 0.004f && _hRippleShare > 0.001f)
            {
                float t = (p.x + p.y * 0.7f) * (_edgeRippleFreq * 0.0628f);
                float harm = 1f - _rippleSmooth;
                float ripple = 1f + _edgeRipple * (0.32f * Mathf.Sin(t)
                    + harm * (0.24f * Mathf.Sin(t * 2.417f + 1.7f)
                    + 0.14f * Mathf.Sin(t * 5.089f + 4.2f)));
                lw = Mathf.Lerp(lwS, Mathf.Clamp(lwS * ripple, 0f, 2f), _hRippleShare);
            }
            lw = Mathf.Min(1.2f, lw);
            float shaped = 0.55f * _hBaseA * (_hShapeFloor + (1f - _hShapeFloor) * lw)
                * _hPeakDim;
            return Mathf.Min(0.9f, _glow * shaped) * endW;
        }

        /// <summary>One halo column: a base vert at the fringe's outer edge carrying the halo's
        /// full value (continuity — the skirt starts where the AA fringe ends) plus eight decay
        /// stops along <paramref name="outDir"/>. Returns the base vertex index.</summary>
        private int HaloColumn(VertexHelper vh, Vector2 basePt, Vector2 outDir, float gw, float a0)
        {
            int bi = vh.currentVertCount;
            Color h = color; h.a = a0;
            AddVertFx(vh, basePt, h, 0f);
            for (int gs = 0; gs < 8; gs++)
            {
                Color g = color; g.a = a0 * _haloW[gs];
                AddVertFx(vh, basePt + outDir * (gw * ((gs + 1) / 8f)), g, 0f);
            }
            return bi;
        }

        // Stitch the 8 bands between two consecutive halo columns.
        private static void HaloStitch(VertexHelper vh, int c0, int c1)
        {
            for (int gs = 0; gs < 8; gs++)
                Strip(vh, c0 + gs, c0 + gs + 1, c1 + gs, c1 + gs + 1);
        }

        private static Vector2 Rot(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void EmitHalo(VertexHelper vh, List<Vector2> pts, List<Vector2> dir,
            List<Vector2> nrm, bool closed, int segCount, float hw, float f, bool fading)
        {
            int n = pts.Count;
            float rimBase = hw + f; // columns start where the AA fringe ends

            // Mesh-size guard: skip the halo (the stroke itself stays renderable) rather than
            // tripping UGUI's ~65k vertex ceiling on pathological point counts.
            if (vh.currentVertCount + n * 2 * 9 + 4096 > 60000)
            {
                if (!_warnedHaloVerts)
                {
                    _warnedHaloVerts = true;
                    UIALog.Warn("PolylineGraphic: halo skipped (vertex budget).");
                }
                return;
            }

            float EndW(int i)
            {
                if (!fading) return 1f;
                float t = _arc[i];
                return Mathf.Clamp01(Mathf.Min(t, 1f - t) / _fadeEnds);
            }

            // firstCol/lastCol per side so the cap fans can bridge the two ribbons.
            int firstL = -1, lastL = -1, firstR = -1, lastR = -1;
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? 1f : -1f;
                // Interior side of a closed loop: depth clamped by PrepareHaloShaping so
                // opposite skirts can't cross the medial axis and stack; a fully-starved
                // side is skipped (its fringe already fell back to alpha 0).
                float gw = _hInteriorSgn != 0 && (int)sgn == _hInteriorSgn
                    ? _hGwInterior : _hGw;
                if (gw < 0.5f) continue;
                int prev = -1, first = -1;
                for (int i = 0; i < n; i++)
                {
                    bool hasIn = closed || i > 0;
                    bool hasOut = closed || i < n - 1;
                    int sIn = (i - 1 + segCount) % segCount;
                    int sOut = i % segCount;
                    Vector2 p = pts[i];
                    float endW = EndW(i);

                    if (!hasIn || !hasOut)
                    {
                        // Open end: the end segment's own normal.
                        Vector2 od = nrm[hasIn ? sIn : sOut] * sgn;
                        int c = HaloColumn(vh, p + od * rimBase, od, gw, HaloA0(p, od, endW));
                        if (prev >= 0) HaloStitch(vh, prev, c);
                        if (first < 0) first = c;
                        prev = c;
                        continue;
                    }

                    Vector2 na = nrm[sIn] * sgn, nb = nrm[sOut] * sgn;
                    float cross = dir[sIn].x * dir[sOut].y - dir[sIn].y * dir[sOut].x;
                    float turn = Vector2.SignedAngle(na, nb);
                    // Left turn bends away from the -n side: that side is the OUTER wedge.
                    bool outerSide = (cross > 0f) == (sgn < 0f);
                    if (Mathf.Abs(cross) < 1e-4f || Mathf.Abs(turn) < 1f)
                    {
                        // Straight (incl. subdivision points): one column, averaged normal.
                        // The sum guard keeps a 180° hairpin from collapsing the column onto
                        // the centreline (zero outward direction smears halo across the line).
                        Vector2 sum = na + nb;
                        Vector2 od = sum.sqrMagnitude > 1e-6f ? sum.normalized : na;
                        int c = HaloColumn(vh, p + od * rimBase, od, gw, HaloA0(p, od, endW));
                        if (prev >= 0) HaloStitch(vh, prev, c);
                        if (first < 0) first = c;
                        prev = c;
                    }
                    else if (outerSide && Mathf.Abs(turn) > 24f)
                    {
                        // Outer wedge of a real bend: a FAN of columns rotating from the
                        // incoming to the outgoing normal (~18°/column) — the shape renderer's
                        // corner-fan rule; a single mitered column would stretch the decay
                        // into a bright diamond ray. Column BASES are pulled radially onto the
                        // bevel's straight fringe chord (r = R·cos(turn/2)/cos(θ)) so the fan
                        // sits ON the stroke boundary instead of leaving a lens-shaped gap
                        // over the bevel (review 2026-07-16).
                        int cols = Mathf.Clamp(2 + Mathf.CeilToInt(Mathf.Abs(turn) / 18f), 3, 12);
                        float halfRad = Mathf.Abs(turn) * Mathf.Deg2Rad * 0.5f;
                        float cosHalf = Mathf.Cos(halfRad);
                        for (int k = 0; k < cols; k++)
                        {
                            float frac = k / (float)(cols - 1);
                            Vector2 od = Rot(na, turn * Mathf.Deg2Rad * frac);
                            float theta = Mathf.Abs(turn) * Mathf.Deg2Rad * frac - halfRad;
                            float rK = rimBase * cosHalf / Mathf.Max(Mathf.Cos(theta), 0.2f);
                            int c = HaloColumn(vh, p + od * rK, od, gw, HaloA0(p, od, endW));
                            if (prev >= 0) HaloStitch(vh, prev, c);
                            if (first < 0) first = c;
                            prev = c;
                        }
                    }
                    else
                    {
                        // Inner side of a bend (or a gentle outer turn): one averaged column;
                        // the alpha fades with the miter length so the two sides' overlapping
                        // skirts can't double-brighten the crease (the shape seam-kill rule).
                        Vector2 avg = na + nb;
                        Vector2 od = avg.sqrMagnitude > 1e-6f ? avg.normalized : na;
                        float cosHalf = Vector2.Dot(od, na);
                        float mm = cosHalf > 0.05f ? 1f / cosHalf : 3f;
                        float fadeK = outerSide ? 1f : Mathf.Clamp01(2f - mm);
                        int c = HaloColumn(vh, p + od * rimBase, od, gw,
                            HaloA0(p, od, endW) * fadeK);
                        if (prev >= 0) HaloStitch(vh, prev, c);
                        if (first < 0) first = c;
                        prev = c;
                    }
                }
                if (closed && prev >= 0 && first >= 0) HaloStitch(vh, prev, first);
                if (side == 0) { firstL = first; lastL = prev; }
                else { firstR = first; lastR = prev; }
            }

            // Cap fans: wrap the halo around each open end, bridging the left ribbon's end
            // column to the right's through the outward direction (180° at ~22.5°/step).
            // With end-fade active the end alpha is already 0 — nothing to wrap.
            if (!closed && !fading && firstL >= 0 && firstR >= 0)
            {
                // End point: rotate from +n through +dir to -n (turn = -180° from the left
                // normal). Start point: rotate from +n through -dir to -n (+180°).
                CapFan(vh, pts[n - 1], nrm[segCount - 1], -180f, lastL, lastR, _hGw,
                    HaloA0(pts[n - 1], dir[segCount - 1], 1f), rimBase);
                CapFan(vh, pts[0], nrm[0], 180f, firstL, firstR, _hGw,
                    HaloA0(pts[0], -dir[0], 1f), rimBase);
            }
        }

        /// <summary>Semicircular halo fan around an open end: intermediate columns between the
        /// two side ribbons' end columns, rotating <paramref name="sweepDeg"/> from the left
        /// normal. One alpha for the whole arc (the lobe barely varies over a cap's footprint;
        /// per-column light would re-anchor a ray at the tip).</summary>
        private void CapFan(VertexHelper vh, Vector2 p, Vector2 leftNormal, float sweepDeg,
            int leftCol, int rightCol, float gw, float a0, float rimBase)
        {
            int steps = 8; // 22.5° per column across the 180° arc
            int prev = leftCol;
            for (int k = 1; k < steps; k++)
            {
                Vector2 od = Rot(leftNormal, sweepDeg * Mathf.Deg2Rad * (k / (float)steps));
                int c = HaloColumn(vh, p + od * rimBase, od, gw, a0);
                HaloStitch(vh, prev, c);
                prev = c;
            }
            HaloStitch(vh, prev, rightCol);
        }

        private static Color Fade(Color c) { c.a = 0f; return c; }

        /// <summary>Emit one vertex carrying the per-element <see cref="FxStrength"/> in uv0.x
        /// (uv0.y = legacy marker, always 0 for the polyline), plus the moving-flow payload in
        /// uv1 = (arc-length px, flowWeight·EdgeRipple, freq, speed) — mirroring
        /// PolygonPanelGraphic. uv1 is all-zero unless flowing (and always zero on a stock-UI
        /// stroke), which every shader treats as "static", so a non-flowing line is unchanged.</summary>
        private void AddVertFx(VertexHelper vh, Vector3 pos, Color32 c, float legacyMarker,
            float arcLen = 0f, float flowW = 0f)
            => vh.AddVert(pos, c,
                // uv0.z carries the ripple's harmonic weight (1 - RippleSmooth) so the shader's
                // travelling wave honours RippleSmooth exactly like the static bake — otherwise
                // it hardcodes 0.6 and the two diverge for any RippleSmooth != 0.4.
                new Vector4(_fxStrength, legacyMarker, 1f - _rippleSmooth, 0f),
                _flowSpeed > 0.004f
                    ? new Vector4(arcLen, flowW * _edgeRipple, _edgeRippleFreq, _flowSpeed)
                    : Vector4.zero,
                new Vector3(0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f));

        /// <summary>Four verts across the stroke at <paramref name="p"/>: outer fringe,
        /// inner edge (solid), inner edge (solid), outer fringe — the +n side first. The
        /// fringe colours are per side: alpha 0 classically, or the halo's landing value
        /// when glow is on (continuity — see FringeAt). All four share the point's arc phase;
        /// only the two solid verts carry flow weight (the fringe is alpha-0/halo, not the
        /// travelling stroke).</summary>
        private void AddCross(VertexHelper vh, Vector2 p, Vector2 nn,
            float hw, float f, Color solid, Color fadeL, Color fadeR, float arcLen = 0f)
        {
            AddVertFx(vh, p + nn * (hw + f), fadeL, 0f, arcLen, 0f);
            AddVertFx(vh, p + nn * hw, solid, 0f, arcLen, 1f);
            AddVertFx(vh, p - nn * hw, solid, 0f, arcLen, 1f);
            AddVertFx(vh, p - nn * (hw + f), fadeR, 0f, arcLen, 0f);
        }

        // Quad l0-l1-r1-r0 (l* on the start cross, r* on the end cross).
        private static void Strip(VertexHelper vh, int l0, int l1, int r0, int r1)
        {
            vh.AddTriangle(l0, l1, r1);
            vh.AddTriangle(l0, r1, r0);
        }

        /// <summary>Outer bevel wedge at a corner: a solid triangle from the joint centre to the
        /// two segments' outer edge points, plus a fringe skirt across that outer face. The outer
        /// side is the one the turn bends away from (chosen by the cross product's sign).</summary>
        private void Bevel(VertexHelper vh, Vector2 p, Vector2 d0, Vector2 d1,
            Vector2 n0, Vector2 n1, float hw, float f, Color solid, Color fadeA, Color fadeB,
            float arcLen = 0f)
        {
            float cross = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(cross) < 1e-4f) return; // collinear: no outer gap to fill
            float sign = cross > 0f ? -1f : 1f;   // left turn -> outer is the -n side
            Vector2 na = n0 * sign, nb = n1 * sign;

            int bi = vh.currentVertCount;
            AddVertFx(vh, p, solid, 0f, arcLen, 1f);              // bi+0 apex (joint centre)
            AddVertFx(vh, p + na * hw, solid, 0f, arcLen, 1f);    // bi+1 incoming outer edge
            AddVertFx(vh, p + na * (hw + f), fadeA, 0f, arcLen, 0f); // bi+2 incoming outer fringe
            AddVertFx(vh, p + nb * hw, solid, 0f, arcLen, 1f);    // bi+3 outgoing outer edge
            AddVertFx(vh, p + nb * (hw + f), fadeB, 0f, arcLen, 0f); // bi+4 outgoing outer fringe
            vh.AddTriangle(bi, bi + 1, bi + 3);              // solid wedge
            vh.AddTriangle(bi + 1, bi + 2, bi + 4);          // fringe
            vh.AddTriangle(bi + 1, bi + 4, bi + 3);
        }

        /// <summary>Flat end cap: the solid end cross faded out along <paramref name="outward"/>
        /// (−dir at the start, +dir at the end). Classically the fade runs over the feather
        /// distance to alpha 0. With glow on it extends the full hw+f so its boundary MEETS the
        /// halo cap fan's base ring (the fan otherwise floats hw px past the tip — a transparent
        /// crescent moat at wide strokes; review 2026-07-16), and its mid verts land ON the halo
        /// value while the corners stay 0 (fading laterally where the fan overlaps).</summary>
        private void Cap(VertexHelper vh, Vector2 p, Vector2 outward, Vector2 nn,
            float hw, float f, Color solid, Color fadeL, Color fadeR, bool glow, Color capHalo,
            float arcLen = 0f)
        {
            Vector2 e = p + outward * (glow ? hw + f : f);
            Color farCorner = solid; farCorner.a = 0f;
            Color farMid = glow ? capHalo : farCorner;
            int bi = vh.currentVertCount;
            AddCross(vh, p, nn, hw, f, solid, fadeL, fadeR, arcLen); // bi+0..3 (matches ribbon end)
            // The far cross is the cap fade-out (alpha 0) or the static halo — no flow weight.
            AddVertFx(vh, e + nn * (hw + f), farCorner, 0f); // bi+4
            AddVertFx(vh, e + nn * hw, farMid, 0f);          // bi+5
            AddVertFx(vh, e - nn * hw, farMid, 0f);          // bi+6
            AddVertFx(vh, e - nn * (hw + f), farCorner, 0f); // bi+7
            Strip(vh, bi + 0, bi + 1, bi + 4, bi + 5);
            Strip(vh, bi + 1, bi + 2, bi + 5, bi + 6);
            Strip(vh, bi + 2, bi + 3, bi + 6, bi + 7);
        }

        private static bool NearlySame(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-6f;
    }
}
