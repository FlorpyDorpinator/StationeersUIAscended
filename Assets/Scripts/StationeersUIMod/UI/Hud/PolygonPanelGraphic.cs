using System.Collections.Generic;
using StationeersUIMod.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A FILLED glass shape whose silhouette is an arbitrary CLOSED (and possibly CONCAVE)
    /// contour — the renderer behind the F9 "pen tool". Where <see cref="PanelGraphic"/> is a
    /// single convex rounded rect, this fills any polygon the user draws (fused trapezoids,
    /// blobs, notched bars) as ONE seamless glass panel: one outer border, one AA feather, no
    /// internal seams.
    ///
    /// It mirrors PanelGraphic's mesh idiom — a ring of outward-normal "columns" of colour
    /// stops (fill → border-in → border-solid → fade), so the border band + baked AA are
    /// identical — but the INTERIOR is ear-clipped (a fan only works for convex shapes) and the
    /// contour comes from a point list instead of four corner arcs. The glass shading (Sheen
    /// gradient, Spec/BorderFade/BorderSides/EdgeRipple light) is the SAME math PanelGraphic
    /// uses, so a drawn Shape catches the same key light as the boxes beside it. Curves are
    /// baked into the contour (Catmull-Rom / — later — Bézier), so the fill never needs to know
    /// about them; edges are densely subdivided so the visor <see cref="VisorWarp"/> can bow them.
    ///
    /// NOT yet honoured (deferred; the props exist for <see cref="IGlassSurface"/> parity but the
    /// mesh ignores them): the outer/inner GLOW halos, whose PanelGraphic implementation is bound
    /// to the four-corner-arc miter machinery. Everything else (fill, border, feather, soft edge,
    /// sheen, spec, border-fade, per-side border, ripple, FX uv0) renders like a panel.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PolygonPanelGraphic : MaskableGraphic, IHudFxGraphic, IGlassSurface
    {
        // Author-space anchor points (element-relative, already scaled by the view). The dense
        // render contour is derived from these each rebuild per the curve mode.
        private readonly List<Vector2> _anchors = new List<Vector2>();
        private readonly List<Vector2> _hin = new List<Vector2>();   // per-anchor Bézier IN handle (offset from anchor)
        private readonly List<Vector2> _hout = new List<Vector2>();  // per-anchor Bézier OUT handle (offset from anchor)
        private int _curveMode;   // 0 = straight, 1 = smooth (Catmull-Rom), 2 = Bézier
        private int _curveSteps = 12;

        private float _borderWidth = 1.4f;
        private Color _borderColor = Color.clear;

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

        // ── glass / FX knobs (IGlassSurface). Defaults match "off" so an unstyled shape is a
        // flat translucent fill + thin border, exactly like an unstyled panel.
        private float _sheen, _spec, _borderFade, _softEdge, _fxStrength;
        private float _glow, _glowInner, _glowWidth = 24f, _glowDiffuse;   // stored; mesh ignores (see class note)
        private float _edgeRipple, _edgeRippleFreq = 2f, _rippleSmooth;
        private float _featherOverride = -1f;
        private int _borderSides = 15;

        public float Sheen { get => _sheen; set { Set01(ref _sheen, value); } }
        public float Spec { get => _spec; set { Set01(ref _spec, value); } }
        public float BorderFade { get => _borderFade; set { Set01(ref _borderFade, value); } }
        public float SoftEdge { get => _softEdge; set { SetClamp(ref _softEdge, value, 0f, 48f); } }
        public float FxStrength { get => _fxStrength; set { Set01(ref _fxStrength, value); } }
        public float Glow { get => _glow; set { SetClamp(ref _glow, value, 0f, 2f); } }
        public float GlowInner { get => _glowInner; set { SetClamp(ref _glowInner, value, 0f, 2f); } }
        public float GlowWidth { get => _glowWidth; set { SetClamp(ref _glowWidth, value, 6f, 160f, 24f); } }
        public float GlowDiffuse { get => _glowDiffuse; set { Set01(ref _glowDiffuse, value); } }
        public float EdgeRipple { get => _edgeRipple; set { SetClamp(ref _edgeRipple, value, 0f, 2.5f); } }
        public float EdgeRippleFreq { get => _edgeRippleFreq; set { SetClamp(ref _edgeRippleFreq, value, 0.05f, 8f, 2f); } }
        public float RippleSmooth { get => _rippleSmooth; set { Set01(ref _rippleSmooth, value); } }

        /// <summary>Per-element AA-ramp width (px); -1 (default) = the global HudConfig.EdgeFeather.</summary>
        public float FeatherOverride
        {
            get => _featherOverride;
            set
            {
                value = float.IsNaN(value) ? -1f : value;
                if (!Mathf.Approximately(_featherOverride, value)) { _featherOverride = value; SetVerticesDirty(); }
            }
        }

        /// <summary>Per-side border bitmask (1=T,2=R,4=B,8=L); on a freeform contour "sides" are
        /// resolved by each vertex's outward normal, so this melts roughly the intended quadrant.</summary>
        public int BorderSides
        {
            get => _borderSides;
            set { int v = Mathf.Clamp(value, 0, 15); if (_borderSides != v) { _borderSides = v; SetVerticesDirty(); } }
        }

        public MaskableGraphic AsGraphic => this;

        private void Set01(ref float field, float value)
        {
            value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            if (!Mathf.Approximately(field, value)) { field = value; SetVerticesDirty(); }
        }

        private void SetClamp(ref float field, float value, float lo, float hi, float nanTo = 0f)
        {
            value = float.IsNaN(value) ? nanTo : Mathf.Clamp(value, lo, hi);
            if (!Mathf.Approximately(field, value)) { field = value; SetVerticesDirty(); }
        }

        /// <summary>Replace the shape's anchor points + curve mode. Only dirties the mesh on a
        /// real change (the editor pushes this every frame while dragging).</summary>
        public void SetPoints(IList<Vector2> pts, IList<Vector2> hin, IList<Vector2> hout, int curveMode, int curveSteps)
        {
            int n = pts != null ? pts.Count : 0;
            int steps = Mathf.Clamp(curveSteps, 2, 32);
            bool changed = curveMode != _curveMode || steps != _curveSteps || n != _anchors.Count
                || !SameList(_hin, hin) || !SameList(_hout, hout);
            if (!changed)
                for (int i = 0; i < n; i++)
                    if (_anchors[i] != pts[i]) { changed = true; break; }
            if (!changed) return;

            _curveMode = curveMode; _curveSteps = steps;
            CopyInto(_anchors, pts);
            CopyInto(_hin, hin);
            CopyInto(_hout, hout);
            SetVerticesDirty();
        }

        private static void CopyInto(List<Vector2> dst, IList<Vector2> src)
        {
            dst.Clear();
            if (src != null) for (int i = 0; i < src.Count; i++) dst.Add(src[i]);
        }

        private static bool SameList(List<Vector2> a, IList<Vector2> b)
        {
            int nb = b != null ? b.Count : 0;
            if (a.Count != nb) return false;
            for (int i = 0; i < nb; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static Vector2 HAt(List<Vector2> list, int i)
            => (list != null && i >= 0 && i < list.Count) ? list[i] : Vector2.zero;

        public void Refresh() => SetVerticesDirty();

        private float Feather => _featherOverride >= 0f ? _featherOverride
            : (HudConfig.EdgeFeather != null ? HudConfig.EdgeFeather.Value : 1.25f);

        private static bool _warnedNaN;
        private static bool _warnedEarClip;

        // Scratch, reused across rebuilds (single-threaded UGUI rebuild — same pattern as
        // PanelGraphic._centers / PolylineGraphic._clean, so a rebuild allocates nothing).
        private static readonly List<Vector2> _pin = new List<Vector2>(64);   // cleaned anchors
        private static readonly List<Vector2> _pinHin = new List<Vector2>(64);  // hin/hout kept in lockstep with _pin
        private static readonly List<Vector2> _pinHout = new List<Vector2>(64);
        private static readonly List<Vector2> _contour = new List<Vector2>(256);
        private static readonly List<Vector2> _nrm = new List<Vector2>(256);  // per-vertex outward normal
        private static readonly List<float> _miter = new List<float>(256);
        private static readonly List<int> _ring = new List<int>(256);         // ear-clip index ring
        private static readonly float[] _stopD = new float[6];
        private static readonly Color[] _stopC = new Color[6];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            using (Profiling.ProfilicusUniversalis.Time("Hud.Mesh.Shape"))
                PopulateMeshCore(vh);
        }

        private void PopulateMeshCore(VertexHelper vh)
        {
            vh.Clear();

            // 1. Clean anchors (drop NaN + coincident) then build the dense render contour.
            _pin.Clear(); _pinHin.Clear(); _pinHout.Clear();
            for (int i = 0; i < _anchors.Count; i++)
            {
                Vector2 p = _anchors[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y))
                {
                    if (!_warnedNaN) { _warnedNaN = true; UIALog.Warn("PolygonPanelGraphic: NaN point skipped."); }
                    continue;
                }
                if (_pin.Count > 0 && NearlySame(_pin[_pin.Count - 1], p)) continue;
                _pin.Add(p);
                _pinHin.Add(HAt(_hin, i));    // Bézier handles kept aligned to the cleaned anchors
                _pinHout.Add(HAt(_hout, i));
            }
            if (_pin.Count >= 2 && NearlySame(_pin[0], _pin[_pin.Count - 1]))
            {
                int last = _pin.Count - 1;
                _pin.RemoveAt(last); _pinHin.RemoveAt(last); _pinHout.RemoveAt(last);
            }
            if (_pin.Count < 3) return;

            BuildContour(_pin, _pinHin, _pinHout, _curveMode, _curveSteps, _contour);
            if (_contour.Count < 3) return;

            // 2. Force CCW winding (the outward-normal math assumes it).
            if (SignedArea(_contour) < 0f) _contour.Reverse();
            int n = _contour.Count;

            // 3. Bounding box → glass gradient reference (Sheen keys off centred y, Spec off x/normal).
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = _contour[i];
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            float hw = (maxX - minX) * 0.5f, hh = (maxY - minY) * 0.5f;
            if (hw < 0.3f || hh < 0.3f) return;
            Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            // 4. Per-vertex outward normal (CCW: outward = edge dir rotated -90° → (dy,-dx)) and a
            //    miter length so the border keeps its width around corners, capped so a sharp
            //    reflex/acute vertex can't shoot a spike.
            _nrm.Clear(); _miter.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 pPrev = _contour[(i - 1 + n) % n];
                Vector2 p = _contour[i];
                Vector2 pNext = _contour[(i + 1) % n];
                Vector2 eIn = (p - pPrev).normalized;
                Vector2 eOut = (pNext - p).normalized;
                Vector2 nIn = new Vector2(eIn.y, -eIn.x);
                Vector2 nOut = new Vector2(eOut.y, -eOut.x);
                Vector2 nn = nIn + nOut;
                nn = nn.sqrMagnitude < 1e-6f ? nOut : nn.normalized;
                float d = Vector2.Dot(nn, nOut);
                float m = Mathf.Clamp(1f / Mathf.Max(0.35f, d), 1f, 4f);
                _nrm.Add(nn); _miter.Add(m);
            }

            // 5. Colour-stop distances along each outward normal (mirrors PanelGraphic's base ramp).
            float f = Feather;
            float bw = Mathf.Max(0f, _borderWidth);
            bool hasBorder = bw > 0.05f && _borderColor.a > 0.004f;
            float rampD = f;
            int stops;
            if (hasBorder)
            {
                _stopD[0] = -rampD;   // fill, ramping to the border
                _stopD[1] = 0f;       // border inner
                _stopD[2] = bw;       // border solid
                _stopD[3] = bw + f;   // fade out
                stops = 4;
            }
            else
            {
                _stopD[0] = 0f;
                _stopD[1] = f;
                stops = 2;
            }
            if (_softEdge > 0.01f) _stopD[stops - 1] += _softEdge;   // wide melt-out skirt

            // 6. Emit one column of `stops` verts per contour vertex, coloured per the glass shading.
            for (int i = 0; i < n; i++)
            {
                Vector2 p = _contour[i];
                Vector2 pc = p - center;
                Vector2 dir = _nrm[i];
                Color fillC = FillAt(pc.y, hh);
                if (hasBorder)
                {
                    Color bc = BorderAt(dir, pc, hw);
                    _stopC[0] = fillC; _stopC[1] = bc; _stopC[2] = bc; _stopC[3] = Fade(bc);
                }
                else
                {
                    _stopC[0] = fillC; _stopC[1] = Fade(fillC);
                }
                float mm = _miter[i];
                for (int s = 0; s < stops; s++)
                    AddVertFx(vh, p + dir * (_stopD[s] * mm), _stopC[s], 1f);
            }

            // 7. Interior fill: ear-clip the contour, emit triangles over each vertex's stop-0 vert
            //    (the on-contour / inner-ramp fill vertex). Convex or concave both handled; fill and
            //    border share the same outer contour, so there is no internal seam ("fused").
            EarClipFill(vh, _contour, stops);

            // 8. Ring strips between consecutive columns (border band + fade), like PanelGraphic.
            for (int i = 0; i < n; i++)
            {
                int ai = i * stops;
                int aj = ((i + 1) % n) * stops;
                for (int s = 0; s < stops - 1; s++)
                {
                    vh.AddTriangle(ai + s, ai + s + 1, aj + s + 1);
                    vh.AddTriangle(ai + s, aj + s + 1, aj + s);
                }
            }
        }

        // ---- contour construction ----

        /// <summary>Anchors → dense render contour. Straight = anchors; Smooth = a CLOSED
        /// Catmull-Rom through them (curve passes through each anchor). Then every straight run
        /// &gt; ~48px is subdivided so the visor warp (which only moves existing verts) can bow it.
        /// Bézier (curveMode 2) is added later; until then it falls back to straight.</summary>
        private static void BuildContour(List<Vector2> anchors, List<Vector2> hin, List<Vector2> hout,
            int curveMode, int steps, List<Vector2> outC)
        {
            outC.Clear();
            int m = anchors.Count;
            if (curveMode == 2 && m >= 2)
            {
                // Cubic Bézier per closed segment: P0 = anchor, P1 = anchor + OUT handle,
                // P2 = next + next's IN handle, P3 = next. Zero handles ⇒ straight line.
                for (int i = 0; i < m; i++)
                {
                    Vector2 p0 = anchors[i];
                    Vector2 p3 = anchors[(i + 1) % m];
                    Vector2 p1 = p0 + HAt(hout, i);
                    Vector2 p2 = p3 + HAt(hin, (i + 1) % m);
                    for (int s = 0; s < steps; s++)
                        outC.Add(Cubic(p0, p1, p2, p3, s / (float)steps));
                }
            }
            else if (curveMode == 1 && m >= 3)
            {
                for (int i = 0; i < m; i++)
                {
                    Vector2 p0 = anchors[(i - 1 + m) % m];
                    Vector2 p1 = anchors[i];
                    Vector2 p2 = anchors[(i + 1) % m];
                    Vector2 p3 = anchors[(i + 2) % m];
                    for (int s = 0; s < steps; s++)
                        outC.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
                }
            }
            else
            {
                for (int i = 0; i < m; i++) outC.Add(anchors[i]);
            }

            // Subdivide long straight runs (in place, walking a snapshot count so we don't rescan
            // inserted points). Kept simple: rebuild into the same list via a scratch swap.
            SubdivideLong(outC, 48f);

            // Drop any coincident neighbours the sampling produced.
            for (int i = outC.Count - 1; i > 0; i--)
                if (NearlySame(outC[i], outC[i - 1])) outC.RemoveAt(i);
            if (outC.Count >= 2 && NearlySame(outC[0], outC[outC.Count - 1])) outC.RemoveAt(outC.Count - 1);
        }

        private static readonly List<Vector2> _subScratch = new List<Vector2>(256);

        private static void SubdivideLong(List<Vector2> c, float maxLen)
        {
            int n = c.Count;
            if (n < 2) return;
            _subScratch.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = c[i];
                Vector2 b = c[(i + 1) % n];
                _subScratch.Add(a);
                float len = (b - a).magnitude;
                if (len > maxLen)
                {
                    int segs = Mathf.Min(240, Mathf.FloorToInt(len / maxLen));
                    for (int k = 1; k <= segs; k++)
                        _subScratch.Add(Vector2.Lerp(a, b, k / (float)(segs + 1)));
                }
            }
            c.Clear();
            c.AddRange(_subScratch);
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Vector2 Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        // ---- interior triangulation (ear clipping) ----

        /// <summary>Ear-clip <paramref name="poly"/> (assumed CCW, simple) and emit fill triangles
        /// over each vertex's stop-0 vertex (index i*stops). O(n²), no allocation beyond the index
        /// ring. Fail-soft: if it stalls on self-intersecting input, fan the remainder + warn once.</summary>
        private static void EarClipFill(VertexHelper vh, List<Vector2> poly, int stops)
        {
            int n = poly.Count;
            _ring.Clear();
            for (int i = 0; i < n; i++) _ring.Add(i);

            int guard = 0, guardMax = n * n + 4;
            while (_ring.Count > 3 && guard++ < guardMax)
            {
                bool clipped = false;
                int cnt = _ring.Count;
                for (int k = 0; k < cnt; k++)
                {
                    int i0 = _ring[(k - 1 + cnt) % cnt];
                    int i1 = _ring[k];
                    int i2 = _ring[(k + 1) % cnt];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    // Convex (left turn) for a CCW polygon.
                    if (Cross(b - a, c - b) <= 0f) continue;
                    // No other ring vertex inside this ear.
                    bool empty = true;
                    for (int j = 0; j < cnt; j++)
                    {
                        int idx = _ring[j];
                        if (idx == i0 || idx == i1 || idx == i2) continue;
                        if (PointInTri(poly[idx], a, b, c)) { empty = false; break; }
                    }
                    if (!empty) continue;
                    vh.AddTriangle(i0 * stops, i1 * stops, i2 * stops);
                    _ring.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate / self-intersecting: fall through to the fan
            }

            if (_ring.Count == 3)
            {
                vh.AddTriangle(_ring[0] * stops, _ring[1] * stops, _ring[2] * stops);
            }
            else if (_ring.Count > 3)
            {
                // Fallback: a naive fan (may look wrong on a concave remainder, but never crashes).
                if (!_warnedEarClip) { _warnedEarClip = true; UIALog.Warn("PolygonPanelGraphic: ear-clip fell back to fan (self-intersecting contour?)."); }
                for (int k = 1; k < _ring.Count - 1; k++)
                    vh.AddTriangle(_ring[0] * stops, _ring[k] * stops, _ring[k + 1] * stops);
            }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos); // all same sign (incl. on-edge) → inside
        }

        private static float SignedArea(List<Vector2> c)
        {
            float a = 0f;
            int n = c.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = c[i], q = c[(i + 1) % n];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        // ---- glass shading (ported from PanelGraphic so shapes and panels catch the same light) ----

        private Color FillAt(float y, float hh)
        {
            if (_sheen <= 0.004f) return color;
            float t = Mathf.Clamp01((y + hh) / (2f * hh));
            float w = _sheen * 0.30f * t * t;
            Color c = color;
            c.r += (1f - c.r) * w;
            c.g += (1f - c.g) * w;
            c.b += (1f - c.b) * w;
            c.a = Mathf.Min(1f, c.a * (1f + 0.30f * _sheen * t * t));
            return c;
        }

        private float BorderLightSmooth(Vector2 dir, Vector2 p, float hw)
        {
            float tx = Mathf.Clamp01((p.x + hw) / (2f * hw));
            return PanelGraphic.KeyLightWeight(dir, tx);
        }

        private float BorderLightW(Vector2 dir, Vector2 p, float hw)
        {
            float w = BorderLightSmooth(dir, p, hw);
            if (_edgeRipple > 0.004f)
            {
                float t = (p.x + p.y * 0.7f) * (_edgeRippleFreq * 0.0628f);
                float harm = 1f - _rippleSmooth;
                float ripple = 1f + _edgeRipple * (0.32f * Mathf.Sin(t)
                    + harm * (0.24f * Mathf.Sin(t * 2.417f + 1.7f)
                    + 0.14f * Mathf.Sin(t * 5.089f + 4.2f)));
                w = Mathf.Clamp(w * ripple, 0f, 2f);
            }
            return w;
        }

        private Color BorderAt(Vector2 dir, Vector2 p, float hw)
        {
            float w = BorderLightW(dir, p, hw);
            Color c = _borderColor;
            if (_spec > 0.004f)
            {
                float ws = Mathf.Clamp01(w * _spec * 1.6f);
                Color tint = PanelGraphic.LightTint();
                c.r += (tint.r - c.r) * ws;
                c.g += (tint.g - c.g) * ws;
                c.b += (tint.b - c.b) * ws;
                c.a += (1f - c.a) * ws * 0.85f;
            }
            if (_borderFade > 0.004f)
                c.a *= Mathf.Lerp(1f, Mathf.Clamp01(w * 2.2f), _borderFade);
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

        private static Color Fade(Color c) { c.a = 0f; return c; }

        private void AddVertFx(VertexHelper vh, Vector3 pos, Color32 c, float marker)
            => vh.AddVert(pos, c, new Vector2(_fxStrength, marker));

        private static bool NearlySame(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-6f;
    }
}
