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
                    // term — deterministic, no per-frame rebuild.
                    if (_edgeRipple > 0.004f)
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

            // Segment ribbons: fringe | core | fringe strips across the width.
            for (int s = 0; s < segCount; s++)
            {
                int ia = s, ib = (s + 1) % n;
                Vector2 a = pts[ia];
                Vector2 b = pts[ib];
                Vector2 nn = nrm[s];
                Color sa = SolidAt(ia, nn), sb = SolidAt(ib, nn);
                int bi = vh.currentVertCount;
                AddCross(vh, a, nn, drawHw, f, sa, Fade(sa)); // bi+0..3
                AddCross(vh, b, nn, drawHw, f, sb, Fade(sb)); // bi+4..7
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
                Bevel(vh, pts[j], dir[s0], dir[s1], nrm[s0], nrm[s1], drawHw, f, sj, Fade(sj));
            }

            // Flat caps on the open ends, pushed out by the feather so the line fades to
            // nothing. With end-fade active the end alpha is already 0 — skip the caps.
            if (!closed && !fading)
            {
                // Use the edge-lit end colour so the cap matches the ribbon's end cross
                // exactly (with EdgeLight 0 this is `solid`/`fade` — byte-identical).
                Color s0c = SolidAt(0, nrm[0]);
                Color s1c = SolidAt(n - 1, nrm[segCount - 1]);
                Cap(vh, pts[0], -dir[0], nrm[0], drawHw, f, s0c, Fade(s0c));
                Cap(vh, pts[n - 1], dir[segCount - 1], nrm[segCount - 1], drawHw, f, s1c, Fade(s1c));
            }
        }

        private static Color Fade(Color c) { c.a = 0f; return c; }

        /// <summary>Emit one vertex carrying the per-element <see cref="FxStrength"/> in uv0.x
        /// and the legacy marker (always 0 for the polyline) in uv0.y. All mesh emission routes
        /// through here so the FX channel is never forgotten; with FxStrength 0 and the stock UI
        /// material (uv0 unread) this is pixel-identical to the pre-FX mesh. See master plan §12.2.</summary>
        private void AddVertFx(VertexHelper vh, Vector3 pos, Color32 c, float legacyMarker)
            => vh.AddVert(pos, c, new Vector2(_fxStrength, legacyMarker));

        /// <summary>Four verts across the stroke at <paramref name="p"/>: outer fringe (fade),
        /// inner edge (solid), inner edge (solid), outer fringe (fade) — the +n side first.</summary>
        private void AddCross(VertexHelper vh, Vector2 p, Vector2 nn,
            float hw, float f, Color solid, Color fade)
        {
            AddVertFx(vh, p + nn * (hw + f), fade, 0f);
            AddVertFx(vh, p + nn * hw, solid, 0f);
            AddVertFx(vh, p - nn * hw, solid, 0f);
            AddVertFx(vh, p - nn * (hw + f), fade, 0f);
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
            Vector2 n0, Vector2 n1, float hw, float f, Color solid, Color fade)
        {
            float cross = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(cross) < 1e-4f) return; // collinear: no outer gap to fill
            float sign = cross > 0f ? -1f : 1f;   // left turn -> outer is the -n side
            Vector2 na = n0 * sign, nb = n1 * sign;

            int bi = vh.currentVertCount;
            AddVertFx(vh, p, solid, 0f);              // bi+0 apex (joint centre)
            AddVertFx(vh, p + na * hw, solid, 0f);    // bi+1 incoming outer edge
            AddVertFx(vh, p + na * (hw + f), fade, 0f); // bi+2 incoming outer fringe
            AddVertFx(vh, p + nb * hw, solid, 0f);    // bi+3 outgoing outer edge
            AddVertFx(vh, p + nb * (hw + f), fade, 0f); // bi+4 outgoing outer fringe
            vh.AddTriangle(bi, bi + 1, bi + 3);              // solid wedge
            vh.AddTriangle(bi + 1, bi + 2, bi + 4);          // fringe
            vh.AddTriangle(bi + 1, bi + 4, bi + 3);
        }

        /// <summary>Flat end cap: the solid end cross faded out over the feather distance along
        /// <paramref name="outward"/> (−dir at the start, +dir at the end).</summary>
        private void Cap(VertexHelper vh, Vector2 p, Vector2 outward, Vector2 nn,
            float hw, float f, Color solid, Color fade)
        {
            Vector2 e = p + outward * f;
            int bi = vh.currentVertCount;
            AddCross(vh, p, nn, hw, f, solid, fade); // bi+0..3 (matches the ribbon end exactly)
            AddVertFx(vh, e + nn * (hw + f), fade, 0f); // bi+4
            AddVertFx(vh, e + nn * hw, fade, 0f);       // bi+5
            AddVertFx(vh, e - nn * hw, fade, 0f);       // bi+6
            AddVertFx(vh, e - nn * (hw + f), fade, 0f); // bi+7
            Strip(vh, bi + 0, bi + 1, bi + 4, bi + 5);
            Strip(vh, bi + 1, bi + 2, bi + 5, bi + 6);
            Strip(vh, bi + 2, bi + 3, bi + 6, bi + 7);
        }

        private static bool NearlySame(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-6f;
    }
}
