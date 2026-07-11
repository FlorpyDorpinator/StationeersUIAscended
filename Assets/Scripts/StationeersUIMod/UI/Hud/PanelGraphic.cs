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
        private float _w = 100f, _h = 40f, _radius = 8f, _topInset, _bottomInset;
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
        {
            if (Mathf.Approximately(_w, width) && Mathf.Approximately(_h, height)
                && Mathf.Approximately(_radius, cornerRadius)
                && Mathf.Approximately(_topInset, topInset)
                && Mathf.Approximately(_bottomInset, bottomInset))
                return;
            _w = width; _h = height; _radius = cornerRadius;
            _topInset = topInset; _bottomInset = bottomInset;
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        private static float Feather => HudConfig.EdgeFeather != null
            ? HudConfig.EdgeFeather.Value : 1.25f;

        // Scratch (single-threaded UI rebuild, same pattern as CircleGraphic).
        private static readonly List<Vector2> _centers = new List<Vector2>(4);
        private static readonly float[] _stopD = new float[4];
        private static readonly Color[] _stopC = new Color[4];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float hw = _w * 0.5f, hh = _h * 0.5f;
            // The threshold must admit hairlines: the vitals bars are ~1.4px tall panels
            // (hh 0.7) — a 1px guard would silently cull every one of them.
            if (hw < 0.3f || hh < 0.3f) return;

            float r = Mathf.Clamp(_radius, 0.5f, Mathf.Min(hw, hh) - 0.5f);

            // Corner-centre polygon, counter-clockwise from bottom-left.
            _centers.Clear();
            _centers.Add(new Vector2(-hw + r + Mathf.Max(0f, _bottomInset), -hh + r));
            _centers.Add(new Vector2(hw - r - Mathf.Max(0f, _bottomInset), -hh + r));
            _centers.Add(new Vector2(hw - r - Mathf.Max(0f, _topInset), hh - r));
            _centers.Add(new Vector2(-hw + r + Mathf.Max(0f, _topInset), hh - r));

            float f = Feather;
            float bw = Mathf.Max(0f, BorderWidth);
            bool hasBorder = bw > 0.05f && BorderColor.a > 0.004f;

            int stops;
            if (hasBorder)
            {
                _stopD[0] = -Mathf.Min(f, r); _stopC[0] = color;        // fill up to here
                _stopD[1] = 0f;               _stopC[1] = BorderColor;  // ramp -> border
                _stopD[2] = bw;               _stopC[2] = BorderColor;  // solid line
                _stopD[3] = bw + f;           _stopC[3] = Fade(BorderColor);
                stops = 4;
            }
            else
            {
                _stopD[0] = 0f; _stopC[0] = color;
                _stopD[1] = f;  _stopC[1] = Fade(color);
                stops = 2;
            }

            // Contour directions: for each corner, an arc from the incoming edge's outward
            // normal to the outgoing edge's, so slanted (trapezoid) sides offset correctly.
            int cornerSegs = Mathf.Clamp(Mathf.CeilToInt(r * 0.5f), 3, 12);

            vh.AddVert(Vector3.zero, color, Vector2.zero); // fill fan centre

            int columns = 0;
            for (int c = 0; c < 4; c++)
            {
                Vector2 prev = _centers[(c + 3) % 4];
                Vector2 cur = _centers[c];
                Vector2 next = _centers[(c + 1) % 4];
                float aIn = NormalAngle(prev, cur);
                float aOut = NormalAngle(cur, next);
                while (aOut < aIn) aOut += Mathf.PI * 2f; // CCW sweep

                for (int i = 0; i <= cornerSegs; i++)
                {
                    float a = Mathf.Lerp(aIn, aOut, i / (float)cornerSegs);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    for (int s = 0; s < stops; s++)
                        vh.AddVert(cur + dir * (r + _stopD[s]), _stopC[s], Vector2.one);
                    columns++;
                }
            }

            // Close the loop by re-emitting the first column.
            {
                Vector2 prev = _centers[3];
                Vector2 cur = _centers[0];
                Vector2 next = _centers[1];
                float aIn = NormalAngle(prev, cur);
                var dir = new Vector2(Mathf.Cos(aIn), Mathf.Sin(aIn));
                for (int s = 0; s < stops; s++)
                    vh.AddVert(cur + dir * (r + _stopD[s]), _stopC[s], Vector2.one);
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
