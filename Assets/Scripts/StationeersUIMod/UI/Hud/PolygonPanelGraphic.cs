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
    /// The outer/inner GLOW halos render here too (same 8-stop decay + light-shaped intensity
    /// recipe as PanelGraphic, emitted along the contour's own normal columns), with freeform
    /// guards: the inner band's depth cap is tighter than the rectangle's (inward offsets can
    /// cross at narrow necks — folds degrade fail-soft through EarClipFill's fan), the miter is
    /// capped for glow stops so a sharp corner can't fan a 160px halo into a spike, and the
    /// inner glow's alpha dissolves toward corners (the same seam-kill the panel uses).
    /// Everything else (fill, border, feather, soft edge, sheen, spec, border-fade, per-side
    /// border, ripple, FX uv0) renders like a panel. The ripple stays the STATIC baked shimmer —
    /// the moving/flowing ripple is the analytic SDF path, which remains rectangle-only.
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
        private float _glow, _glowInner, _glowWidth = 24f, _glowDiffuse, _glowExtraDiffuse;
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
        public float GlowExtraDiffuse { get => _glowExtraDiffuse; set { Set01(ref _glowExtraDiffuse, value); } }
        public float EdgeRipple { get => _edgeRipple; set { SetClamp(ref _edgeRipple, value, 0f, 2.5f); } }
        public float EdgeRippleFreq { get => _edgeRippleFreq; set { SetClamp(ref _edgeRippleFreq, value, 0.05f, 8f, 2f); } }
        public float RippleSmooth { get => _rippleSmooth; set { Set01(ref _rippleSmooth, value); } }

        // The MOVING ripple (0 = static). When flowing, the mesh stops baking the static
        // position-keyed shimmer and instead writes (arc-length, weight·amount, freq, speed)
        // into uv1; HudEdgeFX/HudGlass animate a travelling 3-harmonic wave off _Time. An old
        // resident bundle simply ignores uv1 → the static look (fail-soft both directions).
        private float _flowSpeed;
        public float FlowSpeed { get => _flowSpeed; set { SetClamp(ref _flowSpeed, value, 0f, 4f); } }

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
        private static readonly List<int> _colOf = new List<int>(256);        // contour vertex -> first column index
        private static readonly List<float> _arcLen = new List<float>(256);   // cumulative contour length (flow phase)
        // Max stops per column: 4 inner-glow + 4 base + 8 outer-halo (same counts as PanelGraphic).
        private static readonly float[] _stopD = new float[16];
        private static readonly Color[] _stopC = new Color[16];
        private static readonly float[] _stopM = new float[16];
        private static readonly float[] _inW = new float[4];

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

            // Cumulative arc length per contour vertex (px) — the moving ripple's phase axis
            // (uv1.x). One seam where the loop closes; the static ripple has the same trait.
            _arcLen.Clear();
            float arcAcc = 0f;
            for (int i = 0; i < n; i++)
            {
                _arcLen.Add(arcAcc);
                arcAcc += (_contour[(i + 1) % n] - _contour[i]).magnitude;
            }

            // 5. Colour-stop distances along each outward normal — the same three-band ramp
            //    PanelGraphic runs: [inner-glow band] [fill/border base stops] [outer halo].
            float f = Feather;
            float bw = Mathf.Max(0f, _borderWidth);
            bool hasBorder = bw > 0.05f && _borderColor.a > 0.004f;
            float rampD = f;

            // Inner glow: the depth cap is TIGHTER than PanelGraphic's rectangular 0.55 — a
            // freeform contour's inward offsets can cross at narrow necks (the polygon's
            // medial axis), and the cap keeps folds invisible before they become possible.
            // A pathological neck degrades fail-soft: EarClipFill fans + warns once.
            bool hasGlowIn = _glowInner > 0.004f;
            float glowInD = 0f;
            int inStops = 0;
            if (hasGlowIn)
            {
                glowInD = Mathf.Min(Mathf.Min(_glowWidth, 160f), Mathf.Min(hw, hh) * 0.3f);
                if (glowInD < rampD + 1.5f) hasGlowIn = false; else inStops = 4;
            }
            if (inStops > 0)
            {
                // Same smoothstep-complement power falloff as PanelGraphic (shared exponent).
                float pIn = Mathf.Lerp(Mathf.Lerp(2f, 0.65f, _glowDiffuse), 0.5f,
                    _glowExtraDiffuse);
                for (int gs = 0; gs < inStops; gs++)
                {
                    float u = 1f - gs / (float)inStops; // 1 deepest .. toward the frame
                    float sm = u * u * (3f - 2f * u);
                    _inW[gs] = Mathf.Pow(1f - sm, pIn);
                    _stopD[gs] = Mathf.Lerp(-glowInD, -rampD, gs / (float)inStops);
                    _stopM[gs] = 1f - u;
                }
            }

            int baseStops;
            if (hasBorder)
            {
                _stopD[inStops + 0] = -rampD;   // fill, ramping to the border
                _stopD[inStops + 1] = 0f;       // border inner
                _stopD[inStops + 2] = bw;       // border solid
                _stopD[inStops + 3] = bw + f;   // fade out
                baseStops = 4;
            }
            else
            {
                _stopD[inStops + 0] = 0f;
                _stopD[inStops + 1] = f;
                baseStops = 2;
            }
            if (_softEdge > 0.01f) _stopD[inStops + baseStops - 1] += _softEdge;   // wide melt-out skirt
            for (int mi = 0; mi < baseStops; mi++) _stopM[inStops + mi] = 1f;

            // Outer halo: eight decay stops in the same column system (PanelGraphic's
            // Mach-band-killing count); uv0.y markers fade 1 -> 0 across the band.
            bool hasGlow = _glow > 0.004f;
            int stops = inStops + baseStops;
            float outerD = _stopD[inStops + baseStops - 1];
            if (hasGlow)
            {
                for (int gs = 0; gs < 8; gs++)
                {
                    float t = (gs + 1) / 8f;
                    _stopD[inStops + baseStops + gs] = outerD + Mathf.Min(_glowWidth, 160f) * t;
                    _stopM[inStops + baseStops + gs] = 1f - t;
                }
                stops += 8;
            }
            float pFall = Mathf.Lerp(Mathf.Lerp(2f, 0.65f, _glowDiffuse), 0.5f,
                _glowExtraDiffuse);
            float shapeFloor = Mathf.Lerp(0.08f + 0.27f * _glowDiffuse, 0.62f,
                _glowExtraDiffuse);
            float rippleShare = 0.35f * (1f - _glowDiffuse);

            // 6. Emit columns of `stops` verts along the contour, coloured per the glass
            //    shading. A sharp CONVEX corner emits a halo FAN — several columns rotating
            //    from the incoming to the outgoing edge normal — instead of one mitered
            //    column: a single 45°-mitered column stretched a square corner's halo into a
            //    bright diamond ray with hard bevels between sparse columns (play-test
            //    2026-07-16, "tighter squares"). Base/inner stops stay mitered and COINCIDENT
            //    across the fan with identical colours (the coincident-verts lesson), so the
            //    crisp border corner is untouched; only the halo wraps at constant width.
            _colOf.Clear();
            int totalCols = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = _contour[i];
                Vector2 pc = p - center;
                Vector2 dir = _nrm[i];
                float mm = _miter[i];
                _colOf.Add(totalCols);
                // Inward glow stops keep a nearly-flat miter (deep inward spikes cross the
                // medial axis); halo stops fan on a capped miter so a sharp corner can't
                // shoot a 160px-wide spike. Base stops keep the exact original behaviour.
                float mmIn = Mathf.Min(mm, 1.25f);
                float mmHalo = Mathf.Min(mm, 1.5f);

                Color fillC = FillAt(pc.y, hh);
                if (hasBorder)
                {
                    Color bc = BorderAt(dir, pc, hw);
                    _stopC[inStops + 0] = fillC; _stopC[inStops + 1] = bc;
                    _stopC[inStops + 2] = bc; _stopC[inStops + 3] = Fade(bc);
                }
                else
                {
                    _stopC[inStops + 0] = fillC; _stopC[inStops + 1] = Fade(fillC);
                }

                if (hasGlow || hasGlowIn)
                {
                    // PanelGraphic.ColumnColors' halo recipe, ported verbatim: accent-hued
                    // (raw border colour, never the spec-whitened bc — whitened halos read as
                    // grey fog), light-shaped so unlit stretches emit almost nothing (which
                    // also tames overlap stacking at freeform corners), keeping only a
                    // fraction of the border's ripple shimmer.
                    Color halo = hasBorder ? Color.Lerp(color, _borderColor, 0.75f) : color;
                    // Round 3 (2026-07-16): the halo shapes by the SOFT cosine lobe, never the
                    // border's sharp specular exponent (thin radial rays out of square-shape
                    // corners at high sharpness) — see PanelGraphic.KeyLightWeightSoft. The
                    // ripple share rides the same soft base, keeping the flow-bake gate.
                    float txH = Mathf.Clamp01((pc.x + hw) / (2f * hw));
                    float lwS = PanelGraphic.KeyLightWeightSoft(dir, txH);
                    float lwR = (_edgeRipple > 0.004f && _flowSpeed <= 0.004f)
                        ? Mathf.Clamp(lwS * RippleGain(pc), 0f, 2f) : lwS;
                    float lw = Mathf.Min(1.2f, Mathf.Lerp(lwS, lwR, rippleShare));
                    float baseA = Mathf.Max(0.5f, halo.a);
                    float shaped = 0.55f * baseA * (shapeFloor + (1f - shapeFloor) * lw)
                        * (1f - 0.35f * _glowDiffuse);
                    if (hasGlow)
                    {
                        float a0 = Mathf.Min(0.9f, _glow * shaped);
                        for (int gs = 0; gs < 8; gs++)
                        {
                            float t = (gs + 1) / 8f;
                            float s = t * t * (3f - 2f * t);
                            Color g = halo; g.a = a0 * Mathf.Pow(1f - s, pFall);
                            _stopC[inStops + baseStops + gs] = g;
                        }
                        // Continuity: the base fade lands ON the halo's inner value, so the
                        // profile runs border -> glow -> nothing without a hard ring.
                        Color h0 = halo; h0.a = a0;
                        _stopC[inStops + baseStops - 1] = h0;
                    }
                    if (hasGlowIn)
                    {
                        // Corner dissolve (the panel's seam-kill): adjacent edges' inner bands
                        // overlap at bends, so the inner glow's ALPHA fades out where the
                        // contour turns. The shape's own corner signal is its miter length —
                        // 1 on straight runs, >1 at bends. Colour only; no geometry change.
                        float glowFade = Mathf.Clamp01(2f - mm);
                        float aGi = Mathf.Min(0.9f, _glowInner * shaped) * glowFade;
                        for (int gs = 0; gs < inStops; gs++)
                        {
                            // Position-true fill under the band stop (the panel's bowtie-X
                            // lesson: sample the fill at the stop vertex's own y).
                            float bd = _stopD[gs] * mmIn;
                            Color fillHere = FillAt(pc.y + dir.y * bd, hh);
                            _stopC[gs] = PanelGraphic.GlowOver(halo, aGi * _inW[gs], fillHere);
                        }
                        Color fillRamp = FillAt(pc.y - dir.y * rampD * mm, hh);
                        _stopC[inStops] = PanelGraphic.GlowOver(halo, aGi, hasBorder ? fillRamp : fillC);
                    }
                }

                // Fan test: raw edge normals (not the averaged _nrm) around this vertex.
                Vector2 pPrev = _contour[(i - 1 + n) % n];
                Vector2 pNext = _contour[(i + 1) % n];
                Vector2 eIn = (p - pPrev).normalized;
                Vector2 eOut = (pNext - p).normalized;
                float turnDeg = Vector2.SignedAngle(eIn, eOut); // + = left turn = convex (CCW)
                bool fanCorner = hasGlow && turnDeg > 24f;

                float arc = _arcLen[i];
                if (!fanCorner)
                {
                    for (int s = 0; s < stops; s++)
                    {
                        float d;
                        if (s < inStops) d = _stopD[s] * mmIn;
                        else if (s < inStops + baseStops) d = _stopD[s] * mm;
                        else d = outerD * mm + (_stopD[s] - outerD) * mmHalo;
                        AddVertFx(vh, p + dir * d, _stopC[s], _stopM[s], arc, FlowWeight(s, inStops));
                    }
                    totalCols++;
                }
                else
                {
                    // Halo fan: base/inner verts repeat the mitered positions (coincident,
                    // same colours — degenerate strips), halo verts rotate at constant width
                    // from the bevel radius, one column every ~18° of turn.
                    Vector2 nIn = new Vector2(eIn.y, -eIn.x);
                    float turnRad = turnDeg * Mathf.Deg2Rad;
                    int fanCols = Mathf.Clamp(2 + Mathf.CeilToInt(turnDeg / 18f), 3, 9);
                    for (int k = 0; k < fanCols; k++)
                    {
                        Vector2 dirK = Rot(nIn, turnRad * k / (fanCols - 1));
                        for (int s = 0; s < stops; s++)
                        {
                            float d; Vector2 dd;
                            if (s < inStops) { d = _stopD[s] * mmIn; dd = dir; }
                            else if (s < inStops + baseStops) { d = _stopD[s] * mm; dd = dir; }
                            else { d = outerD * mm + (_stopD[s] - outerD); dd = dirK; }
                            AddVertFx(vh, p + dd * d, _stopC[s], _stopM[s], arc, FlowWeight(s, inStops));
                        }
                        totalCols++;
                    }
                }
            }

            // 7. Interior fill: ear-clip the contour, emit triangles over each vertex's stop-0 vert
            //    (the deepest inner-glow vert when that band exists, else the inner-ramp fill vert).
            //    Convex or concave both handled; fill and border share the same outer contour, so
            //    there is no internal seam ("fused"). _colOf maps contour vertex -> first column
            //    (corner fans add columns; their stop-0 verts are coincident, any one works).
            EarClipFill(vh, _contour, stops, _colOf);

            // 8. Ring strips between consecutive columns (inner glow + border band + fade + halo),
            //    like PanelGraphic. Fan columns ride the same loop: their base bands are degenerate
            //    (coincident verts) and their halo bands form the corner wedge.
            for (int c = 0; c < totalCols; c++)
            {
                int ai = c * stops;
                int aj = ((c + 1) % totalCols) * stops;
                for (int s = 0; s < stops - 1; s++)
                {
                    vh.AddTriangle(ai + s, ai + s + 1, aj + s + 1);
                    vh.AddTriangle(ai + s, aj + s + 1, aj + s);
                }
            }
        }

        /// <summary>Rotate a 2D vector by <paramref name="rad"/> radians (CCW positive).</summary>
        private static Vector2 Rot(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>How strongly the moving ripple modulates each stop: nothing on the fill
        /// stop (the interior must never pulse), the rim marker's own ramp everywhere else
        /// (full on the border band, decaying across the halo, ramping in across the inner glow).</summary>
        private float FlowWeight(int s, int inStops)
            => s == inStops ? 0f : _stopM[s];

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
        /// over each vertex's stop-0 vertex (index colOf[i]*stops — corner fans make columns
        /// per-vertex variable). O(n²), no allocation beyond the index ring. Fail-soft: if it
        /// stalls on self-intersecting input, fan the remainder + warn once.</summary>
        private static void EarClipFill(VertexHelper vh, List<Vector2> poly, int stops, List<int> colOf)
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
                    vh.AddTriangle(colOf[i0] * stops, colOf[i1] * stops, colOf[i2] * stops);
                    _ring.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate / self-intersecting: fall through to the fan
            }

            if (_ring.Count == 3)
            {
                vh.AddTriangle(colOf[_ring[0]] * stops, colOf[_ring[1]] * stops, colOf[_ring[2]] * stops);
            }
            else if (_ring.Count > 3)
            {
                // Fallback: a naive fan (may look wrong on a concave remainder, but never crashes).
                if (!_warnedEarClip) { _warnedEarClip = true; UIALog.Warn("PolygonPanelGraphic: ear-clip fell back to fan (self-intersecting contour?)."); }
                for (int k = 1; k < _ring.Count - 1; k++)
                    vh.AddTriangle(colOf[_ring[0]] * stops, colOf[_ring[k]] * stops, colOf[_ring[k + 1]] * stops);
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
            // Flowing ripple replaces the static bake entirely (the shader animates the same
            // wave off uv1) — baking both would stack a frozen copy under the moving one.
            if (_edgeRipple > 0.004f && _flowSpeed <= 0.004f)
                w = Mathf.Clamp(w * RippleGain(p), 0f, 2f);
            return w;
        }

        /// <summary>EdgeRipple's multiplicative gain at a contour point — PanelGraphic's
        /// recipe. Split out so the halo can ripple its SOFT light weight.</summary>
        private float RippleGain(Vector2 p)
        {
            float t = (p.x + p.y * 0.7f) * (_edgeRippleFreq * 0.0628f);
            float harm = 1f - _rippleSmooth;
            return 1f + _edgeRipple * (0.32f * Mathf.Sin(t)
                + harm * (0.24f * Mathf.Sin(t * 2.417f + 1.7f)
                + 0.14f * Mathf.Sin(t * 5.089f + 4.2f)));
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

        // uv0 = (per-element FX strength, centre/edge marker) — the established contract.
        // uv1 = (contour arc-length px, flowWeight·rippleAmount, rippleFreq, flowSpeed) — the
        // moving-ripple payload; all-zero when not flowing, which every shader treats as off.
        private void AddVertFx(VertexHelper vh, Vector3 pos, Color32 c, float marker,
            float arcLen = 0f, float flowW = 0f)
            // uv0.z = the ripple's harmonic weight (1 - RippleSmooth), so the shader's moving
            // wave honours RippleSmooth exactly like the static bake (was hardcoded 0.6).
            => vh.AddVert(pos, c, new Vector4(_fxStrength, marker, 1f - _rippleSmooth, 0f),
                _flowSpeed > 0.004f
                    ? new Vector4(arcLen, flowW * _edgeRipple, _edgeRippleFreq, _flowSpeed)
                    : Vector4.zero,
                new Vector3(0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f));

        private static bool NearlySame(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-6f;
    }
}
