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
    public sealed class PanelGraphic : MaskableGraphic
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

        // Scratch (single-threaded UI rebuild, same pattern as CircleGraphic).
        private static readonly List<Vector2> _centers = new List<Vector2>(4);
        private static readonly float[] _radii = new float[4];
        private static readonly float[] _stopD = new float[4];
        private static readonly Color[] _stopC = new Color[4];

        protected override void OnPopulateMesh(VertexHelper vh)
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

            int stops;
            if (hasBorder)
            {
                _stopD[0] = -Mathf.Min(f, rMin); _stopC[0] = color;        // fill up to here
                _stopD[1] = 0f;                  _stopC[1] = BorderColor;  // ramp -> border
                _stopD[2] = bw;                  _stopC[2] = BorderColor;  // solid line
                _stopD[3] = bw + f;              _stopC[3] = Fade(BorderColor);
                stops = 4;
            }
            else
            {
                _stopD[0] = 0f; _stopC[0] = color;
                _stopD[1] = f;  _stopC[1] = Fade(color);
                stops = 2;
            }

            vh.AddVert(Vector3.zero, color, Vector2.zero); // fill fan centre

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
                int cornerSegs = Mathf.Clamp(Mathf.CeilToInt(rc * 0.5f), 3, 12);
                for (int i = 0; i <= cornerSegs; i++)
                {
                    float a = Mathf.Lerp(aIn, aOut, i / (float)cornerSegs);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    for (int s = 0; s < stops; s++)
                        vh.AddVert(cur + dir * (rc + _stopD[s]), _stopC[s], Vector2.one);
                    columns++;
                }
            }

            // Close the loop by re-emitting the first column.
            {
                Vector2 prev = _centers[3];
                Vector2 cur = _centers[0];
                float aIn = NormalAngle(prev, cur);
                var dir = new Vector2(Mathf.Cos(aIn), Mathf.Sin(aIn));
                for (int s = 0; s < stops; s++)
                    vh.AddVert(cur + dir * (_radii[0] + _stopD[s]), _stopC[s], Vector2.one);
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
    }
}
