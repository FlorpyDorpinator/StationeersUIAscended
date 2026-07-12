using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The gauge bar from the concept art: a rounded-end track carrying a value fill,
    /// faint threshold zones, and a target caret. Reads raw units (kPa, °C, %) directly —
    /// the owning widget hands over real numbers and the boundaries, this class only paints.
    ///
    /// Two orientations share one code path: a "main" axis (the direction the fill grows)
    /// and a "cross" axis (the bar's thickness). Vertical gauges fill bottom→top for the
    /// column layout; horizontal fills left→right for compact rows.
    ///
    /// Overlay canvases get no MSAA, so every filled region is skirted with an alpha ramp
    /// of width <see cref="Feather"/> — that fringe IS the anti-aliasing. Inherited
    /// <see cref="Graphic.color"/> is deliberately pinned to white and never read; the
    /// explicit colour properties drive every pixel so a widget can tint each element.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ThresholdBarGraphic : MaskableGraphic
    {
        // Track edge → fill inset. Leaves a hairline of track colour framing the fill so it
        // reads as sitting INSIDE the track rather than replacing it.
        private const float FillInset = 1.5f;
        // Faint tint applied to zone segments on the track behind the fill: enough to show
        // where the bands sit at a safe value, weak enough not to fight the live fill.
        private const float ZoneTint = 0.28f;

        private bool _vertical;
        private float _min, _max = 1f;
        private float _value;
        private float _target = float.NaN;
        private float _warnLow = float.NaN, _critLow = float.NaN;
        private float _warnHigh = float.NaN, _critHigh = float.NaN;
        private float _cornerRadius = 3f;
        private float _targetWidth = 2f;

        private Color _trackColor = Color.clear;
        private Color _fillColor = Color.white;
        private Color _warnColor = Color.clear;
        private Color _critColor = Color.clear;
        private Color _targetColor = Color.clear;

        // Dirty-on-change everywhere: the HUD pushes these each frame and only a real change
        // may cost a mesh rebuild. This bar is always on screen, so a needless rebuild hurts.
        public bool Vertical
        {
            get => _vertical;
            set { if (_vertical != value) { _vertical = value; SetVerticesDirty(); } }
        }

        public float Value
        {
            get => _value;
            // NaN-aware like Target: a widget with no reading pushes NaN every frame,
            // and NaN != NaN would re-dirty the mesh forever.
            set { if (!Same(_value, value)) { _value = value; SetVerticesDirty(); } }
        }

        /// <summary>Raw target position; <see cref="float.NaN"/> hides the caret.</summary>
        public float Target
        {
            get => _target;
            set { if (!Same(_target, value)) { _target = value; SetVerticesDirty(); } }
        }

        public float CornerRadius
        {
            get => _cornerRadius;
            set { if (!Mathf.Approximately(_cornerRadius, value)) { _cornerRadius = value; SetVerticesDirty(); } }
        }

        /// <summary>Caret thickness in px, measured along the main axis.</summary>
        public float TargetWidth
        {
            get => _targetWidth;
            set { if (!Mathf.Approximately(_targetWidth, value)) { _targetWidth = value; SetVerticesDirty(); } }
        }

        public Color TrackColor { get => _trackColor; set => SetC(ref _trackColor, value); }
        public Color FillColor { get => _fillColor; set => SetC(ref _fillColor, value); }
        public Color WarnColor { get => _warnColor; set => SetC(ref _warnColor, value); }
        public Color CritColor { get => _critColor; set => SetC(ref _critColor, value); }
        public Color TargetColor { get => _targetColor; set => SetC(ref _targetColor, value); }

        /// <summary>Raw min/max the bar spans. Fill and zones map through this range.</summary>
        public void SetRange(float min, float max)
        {
            if (Same(_min, min) && Same(_max, max)) return;
            _min = min; _max = max;
            SetVerticesDirty();
        }

        /// <summary>
        /// The four zone boundaries in RAW units. Low side: values under <paramref name="warnLow"/>
        /// read warn, under <paramref name="critLow"/> read crit; high side mirrors this. Any
        /// boundary passed as <see cref="float.NaN"/> is unused (that band simply does not exist).
        /// </summary>
        public void SetZones(float warnLow, float critLow, float warnHigh, float critHigh)
        {
            if (Same(_warnLow, warnLow) && Same(_critLow, critLow)
                && Same(_warnHigh, warnHigh) && Same(_critHigh, critHigh))
                return;
            _warnLow = warnLow; _critLow = critLow;
            _warnHigh = warnHigh; _critHigh = critHigh;
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        protected override void OnEnable()
        {
            base.OnEnable();
            // The colour properties own every pixel; the inherited tint must never dim them.
            color = Color.white;
        }

        private void SetC(ref Color field, Color value)
        {
            if (field == value) return;
            field = value;
            SetVerticesDirty();
        }

        private static float Feather => HudConfig.EdgeFeather != null
            ? HudConfig.EdgeFeather.Value : 1.25f;

        // NaN-aware equality so a NaN boundary doesn't dirty the mesh every frame (NaN != NaN).
        private static bool Same(float a, float b)
            => (float.IsNaN(a) && float.IsNaN(b)) || a == b;

        private static readonly Vector2[] _corners = new Vector2[4];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 1f || rect.height < 1f) return;

            float f = Feather;

            // 1) Rounded-end track. Single radius, clamped to half the short side (a full
            //    pill on the narrow axis of a thin bar).
            float shortSide = Mathf.Min(rect.width, rect.height);
            float radius = Mathf.Clamp(_cornerRadius, 0.5f, shortSide * 0.5f);
            AddRoundedTrack(vh, rect, radius, _trackColor, f);

            // Inner area shared by zones, fill and caret: the track inset so the fill reads
            // as framed and never pokes out of the rounded ends.
            float ix0 = rect.xMin + FillInset, iy0 = rect.yMin + FillInset;
            float ix1 = rect.xMax - FillInset, iy1 = rect.yMax - FillInset;
            if (ix1 <= ix0 || iy1 <= iy0) return;

            float mainLo = _vertical ? iy0 : ix0;
            float mainHi = _vertical ? iy1 : ix1;
            float crossLo = _vertical ? ix0 : iy0;
            float crossHi = _vertical ? ix1 : iy1;

            float span = _max - _min;
            if (span <= 0f) return; // degenerate range: track only.

            // 2) Zone segments, faint, behind the fill. Drawn low→high so the player always
            //    sees where the bands sit even while the value is safe.
            AddZone(vh, _min, _critLow, _critColor, mainLo, mainHi, crossLo, crossHi, span, f);
            AddZone(vh, float.IsNaN(_critLow) ? _min : _critLow, _warnLow,
                _warnColor, mainLo, mainHi, crossLo, crossHi, span, f);
            AddZone(vh, _warnHigh, float.IsNaN(_critHigh) ? _max : _critHigh,
                _warnColor, mainLo, mainHi, crossLo, crossHi, span, f);
            AddZone(vh, _critHigh, _max, _critColor, mainLo, mainHi, crossLo, crossHi, span, f);

            // 3) Value fill from the min end to the value, tinted as a whole by the band the
            //    value sits in — the vanilla-style "the whole bar goes red" glance read.
            float tVal = Mathf.Clamp01((_value - _min) / span);
            if (tVal > 0f)
            {
                Color fill = LevelColor(_value);
                float valPos = Mathf.Lerp(mainLo, mainHi, tVal);
                AddBand(vh, mainLo, valPos, crossLo, crossHi, fill, f);
            }

            // 4) Target caret: a thin quad across the bar at the target position, drawn last.
            if (!float.IsNaN(_target) && _targetColor.a > 0.004f && _targetWidth > 0.05f)
            {
                float tTgt = Mathf.Clamp01((_target - _min) / span);
                float pos = Mathf.Lerp(mainLo, mainHi, tTgt);
                float hw = _targetWidth * 0.5f;
                AddBand(vh, pos - hw, pos + hw, crossLo, crossHi, _targetColor, f);
            }
        }

        /// <summary>Whole-fill colour for a raw value: crit trumps warn trumps safe.</summary>
        private Color LevelColor(float v)
        {
            if ((!float.IsNaN(_critLow) && v < _critLow) || (!float.IsNaN(_critHigh) && v > _critHigh))
                return _critColor;
            if ((!float.IsNaN(_warnLow) && v < _warnLow) || (!float.IsNaN(_warnHigh) && v > _warnHigh))
                return _warnColor;
            return _fillColor;
        }

        // Map a raw [lo,hi] slice to the main axis and paint it faintly. Skips unused
        // (NaN) or inverted slices.
        private void AddZone(VertexHelper vh, float rawLo, float rawHi, Color baseCol,
            float mainLo, float mainHi, float crossLo, float crossHi, float span, float f)
        {
            if (float.IsNaN(rawLo) || float.IsNaN(rawHi)) return;
            Color c = baseCol;
            c.a *= ZoneTint;
            if (c.a <= 0.004f) return;
            float t0 = Mathf.Clamp01((rawLo - _min) / span);
            float t1 = Mathf.Clamp01((rawHi - _min) / span);
            if (t1 - t0 <= 0.0001f) return;
            AddBand(vh, Mathf.Lerp(mainLo, mainHi, t0), Mathf.Lerp(mainLo, mainHi, t1),
                crossLo, crossHi, c, f);
        }

        // Axis-agnostic filled rectangle: main axis is the fill direction, cross the thickness.
        private void AddBand(VertexHelper vh, float mainA, float mainB,
            float crossLo, float crossHi, Color c, float f)
        {
            if (_vertical) AddFringedRect(vh, crossLo, mainA, crossHi, mainB, c, f);
            else AddFringedRect(vh, mainA, crossLo, mainB, crossHi, c, f);
        }

        // A solid rectangle with an outward alpha-ramp skirt on all four sides: inner quad
        // (2 tris) plus a ring of 8 tris fading to zero. Corners ramp diagonally, which reads
        // as anti-aliasing at these sizes. Zero heap allocation.
        private static void AddFringedRect(VertexHelper vh, float x0, float y0, float x1, float y1,
            Color c, float f)
        {
            if (c.a <= 0.004f) return;
            if (x1 - x0 <= 0.05f || y1 - y0 <= 0.05f) return;
            Color fade = c; fade.a = 0f;
            int b = vh.currentVertCount;

            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero); // 0 inner BL
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero); // 1 inner BR
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero); // 2 inner TR
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero); // 3 inner TL
            vh.AddVert(new Vector3(x0 - f, y0 - f), fade, Vector2.zero); // 4 outer BL
            vh.AddVert(new Vector3(x1 + f, y0 - f), fade, Vector2.zero); // 5 outer BR
            vh.AddVert(new Vector3(x1 + f, y1 + f), fade, Vector2.zero); // 6 outer TR
            vh.AddVert(new Vector3(x0 - f, y1 + f), fade, Vector2.zero); // 7 outer TL

            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
            for (int i = 0; i < 4; i++)
            {
                int in0 = b + i, in1 = b + (i + 1) % 4;
                int out0 = b + 4 + i, out1 = b + 4 + (i + 1) % 4;
                vh.AddTriangle(in0, out0, out1);
                vh.AddTriangle(in0, out1, in1);
            }
        }

        // Rounded-rect track by the corner-disc sweep (PanelGraphic's idiom, single radius,
        // no border): a fan from the rect centre to a ring of corner-arc points, plus a
        // fringe skirt fading to zero. Winding is irrelevant — the UI shader is Cull Off.
        private void AddRoundedTrack(VertexHelper vh, Rect rect, float radius, Color c, float f)
        {
            if (c.a <= 0.004f) return;
            Color fade = c; fade.a = 0f;

            _corners[0] = new Vector2(rect.xMin + radius, rect.yMin + radius); // BL, sweep π..3π/2
            _corners[1] = new Vector2(rect.xMax - radius, rect.yMin + radius); // BR, 3π/2..2π
            _corners[2] = new Vector2(rect.xMax - radius, rect.yMax - radius); // TR, 2π..5π/2
            _corners[3] = new Vector2(rect.xMin + radius, rect.yMax - radius); // TL, 5π/2..3π

            int segs = Mathf.Clamp(Mathf.CeilToInt(radius * 0.6f), 3, 14);
            int centerIdx = vh.currentVertCount;
            vh.AddVert(rect.center, c, Vector2.zero);
            int ringStart = vh.currentVertCount;

            int columns = 0;
            for (int cn = 0; cn < 4; cn++)
            {
                float a0 = Mathf.PI * (1f + cn * 0.5f);
                Vector2 ctr = _corners[cn];
                for (int i = 0; i <= segs; i++)
                {
                    float a = a0 + Mathf.PI * 0.5f * (i / (float)segs);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vh.AddVert(ctr + dir * radius, c, Vector2.zero);
                    vh.AddVert(ctr + dir * (radius + f), fade, Vector2.zero);
                    columns++;
                }
            }

            for (int j = 0; j < columns; j++)
            {
                int cur = ringStart + j * 2;
                int nxt = ringStart + ((j + 1) % columns) * 2;
                vh.AddTriangle(centerIdx, cur, nxt);      // solid interior fan
                vh.AddTriangle(cur, nxt, nxt + 1);        // fringe skirt
                vh.AddTriangle(cur, nxt + 1, cur + 1);
            }
        }
    }
}
