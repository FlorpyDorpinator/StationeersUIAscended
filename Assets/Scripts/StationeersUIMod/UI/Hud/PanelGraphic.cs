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

        // Glass treatment (0 = off = the exact pre-glass flat rendering). Both are pure
        // vertex-colour effects — no shaders, no textures, so they warp, fade and batch
        // exactly like every other panel.
        private float _sheen;
        private float _spec;

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
                _stopD[0] = -Mathf.Min(f, rMin); // fill up to here, ramp -> border
                _stopD[1] = 0f;
                _stopD[2] = bw;                  // solid line
                _stopD[3] = bw + f;              // fade out
                stops = 4;
            }
            else
            {
                _stopD[0] = 0f;
                _stopD[1] = f;
                stops = 2;
            }

            // Stop colours are computed PER COLUMN: with glass off they are constants
            // (bitwise the old output); with glass on, fill follows the sheen gradient
            // and the border follows the specular run. Vertex colours interpolate
            // linearly across a quad, so linear-in-position light reads exactly.
            void ColumnColors(Vector2 dir, Vector2 onShape)
            {
                Color fillC = FillAt(onShape.y, hh);
                if (hasBorder)
                {
                    Color bc = BorderAt(dir, onShape, hw);
                    _stopC[0] = fillC; _stopC[1] = bc; _stopC[2] = bc; _stopC[3] = Fade(bc);
                }
                else
                {
                    _stopC[0] = fillC; _stopC[1] = Fade(fillC);
                }
            }

            vh.AddVert(Vector3.zero, FillAt(0f, hh), Vector2.zero); // fill fan centre

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
                    ColumnColors(dir, cur + dir * rc);
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
                ColumnColors(dir, cur + dir * _radii[0]);
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
        private const float LightX = -0.4472f, LightY = 0.8944f;

        /// <summary>Border colour for an edge whose outward normal is <paramref name="dir"/>
        /// at contour point <paramref name="p"/>: cubic falloff on the light dot products
        /// picks WHICH edges catch light, and a linear cross-panel term makes each catch
        /// run brighter→dimmer along the edge. Spec 0 returns the flat border colour.</summary>
        private Color BorderAt(Vector2 dir, Vector2 p, float hw)
        {
            if (_spec <= 0.004f) return BorderColor;
            float k1 = Mathf.Max(0f, dir.x * LightX + dir.y * LightY);
            float k2 = Mathf.Max(0f, -(dir.x * LightX + dir.y * LightY));
            float tx = Mathf.Clamp01((p.x + hw) / (2f * hw)); // 0 left .. 1 right
            float w = k1 * k1 * k1 * (1f - 0.55f * tx)
                    + 0.5f * k2 * k2 * k2 * (0.25f + 0.75f * tx);
            w = Mathf.Clamp01(w * _spec * 1.6f);
            Color c = BorderColor;
            c.r += (1f - c.r) * w;
            c.g += (1f - c.g) * w;
            c.b += (1f - c.b) * w;
            c.a += (1f - c.a) * w * 0.85f;
            return c;
        }
    }
}
