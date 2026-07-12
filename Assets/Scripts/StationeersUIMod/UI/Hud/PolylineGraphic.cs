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
    public sealed class PolylineGraphic : MaskableGraphic
    {
        // Per-instance point list, reused across rebuilds (SetPoints only copies on a real diff).
        private readonly List<Vector2> _points = new List<Vector2>();
        private bool _closed;
        private float _width = 2f;

        /// <summary>Stroke width in px (min 0.5 — thinner reads as a pure fringe hairline).</summary>
        public float Width
        {
            get => _width;
            set
            {
                float v = Mathf.Max(0.5f, value);
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
            float f = Feather;
            Color solid = color;
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
            Color SolidAt(int i)
            {
                if (!fading) return solid;
                float t = arc[i];
                float w = Mathf.Clamp01(Mathf.Min(t, 1f - t) / _fadeEnds);
                Color c = solid; c.a *= w;
                return c;
            }

            // Segment ribbons: fringe | core | fringe strips across the width.
            for (int s = 0; s < segCount; s++)
            {
                int ia = s, ib = (s + 1) % n;
                Vector2 a = pts[ia];
                Vector2 b = pts[ib];
                Vector2 nn = nrm[s];
                Color sa = SolidAt(ia), sb = SolidAt(ib);
                int bi = vh.currentVertCount;
                AddCross(vh, a, nn, hw, f, sa, Fade(sa)); // bi+0..3
                AddCross(vh, b, nn, hw, f, sb, Fade(sb)); // bi+4..7
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
                Color sj = SolidAt(j % n);
                Bevel(vh, pts[j], dir[s0], dir[s1], nrm[s0], nrm[s1], hw, f, sj, Fade(sj));
            }

            // Flat caps on the open ends, pushed out by the feather so the line fades to
            // nothing. With end-fade active the end alpha is already 0 — skip the caps.
            if (!closed && !fading)
            {
                Cap(vh, pts[0], -dir[0], nrm[0], hw, f, solid, fade);
                Cap(vh, pts[n - 1], dir[segCount - 1], nrm[segCount - 1], hw, f, solid, fade);
            }
        }

        private static Color Fade(Color c) { c.a = 0f; return c; }

        /// <summary>Four verts across the stroke at <paramref name="p"/>: outer fringe (fade),
        /// inner edge (solid), inner edge (solid), outer fringe (fade) — the +n side first.</summary>
        private static void AddCross(VertexHelper vh, Vector2 p, Vector2 nn,
            float hw, float f, Color solid, Color fade)
        {
            vh.AddVert(p + nn * (hw + f), fade, Vector2.zero);
            vh.AddVert(p + nn * hw, solid, Vector2.zero);
            vh.AddVert(p - nn * hw, solid, Vector2.zero);
            vh.AddVert(p - nn * (hw + f), fade, Vector2.zero);
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
        private static void Bevel(VertexHelper vh, Vector2 p, Vector2 d0, Vector2 d1,
            Vector2 n0, Vector2 n1, float hw, float f, Color solid, Color fade)
        {
            float cross = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(cross) < 1e-4f) return; // collinear: no outer gap to fill
            float sign = cross > 0f ? -1f : 1f;   // left turn -> outer is the -n side
            Vector2 na = n0 * sign, nb = n1 * sign;

            int bi = vh.currentVertCount;
            vh.AddVert(p, solid, Vector2.zero);              // bi+0 apex (joint centre)
            vh.AddVert(p + na * hw, solid, Vector2.zero);    // bi+1 incoming outer edge
            vh.AddVert(p + na * (hw + f), fade, Vector2.zero); // bi+2 incoming outer fringe
            vh.AddVert(p + nb * hw, solid, Vector2.zero);    // bi+3 outgoing outer edge
            vh.AddVert(p + nb * (hw + f), fade, Vector2.zero); // bi+4 outgoing outer fringe
            vh.AddTriangle(bi, bi + 1, bi + 3);              // solid wedge
            vh.AddTriangle(bi + 1, bi + 2, bi + 4);          // fringe
            vh.AddTriangle(bi + 1, bi + 4, bi + 3);
        }

        /// <summary>Flat end cap: the solid end cross faded out over the feather distance along
        /// <paramref name="outward"/> (−dir at the start, +dir at the end).</summary>
        private static void Cap(VertexHelper vh, Vector2 p, Vector2 outward, Vector2 nn,
            float hw, float f, Color solid, Color fade)
        {
            Vector2 e = p + outward * f;
            int bi = vh.currentVertCount;
            AddCross(vh, p, nn, hw, f, solid, fade); // bi+0..3 (matches the ribbon end exactly)
            vh.AddVert(e + nn * (hw + f), fade, Vector2.zero); // bi+4
            vh.AddVert(e + nn * hw, fade, Vector2.zero);       // bi+5
            vh.AddVert(e - nn * hw, fade, Vector2.zero);       // bi+6
            vh.AddVert(e - nn * (hw + f), fade, Vector2.zero); // bi+7
            Strip(vh, bi + 0, bi + 1, bi + 4, bi + 5);
            Strip(vh, bi + 1, bi + 2, bi + 5, bi + 6);
            Strip(vh, bi + 2, bi + 3, bi + 6, bi + 7);
        }

        private static bool NearlySame(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-6f;
    }
}
