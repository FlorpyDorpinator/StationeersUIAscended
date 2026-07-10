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
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CircleGraphic : MaskableGraphic
    {
        private float _radius = 100f;

        public float BorderWidth { get; set; }
        public Color BorderColor { get; set; } = Color.clear;

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

        private static readonly float[] _stopR = new float[4];
        private static readonly Color[] _stopC = new Color[4];

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_radius <= 0f) return;

            int segments = Mathf.Clamp(Mathf.CeilToInt(_radius * 1.2f), 48, 256);
            float bw = BorderWidth;
            bool hasBorder = bw > 0.1f && BorderColor.a > 0.01f;
            float f = Feather;

            // Rings from the fan edge outwards. The rim needs a ramp on BOTH sides: an alpha-0
            // fringe outside, and a colour ramp from the fill on the inside — otherwise the
            // inner boundary of the orange rim is a hard step and stair-steps just as badly.
            int stops;
            if (hasBorder)
            {
                float rampStart = Mathf.Max(1f, _radius - f);
                _stopR[0] = rampStart;        _stopC[0] = color;                 // fill up to here
                _stopR[1] = _radius;          _stopC[1] = BorderColor;           // ramp -> rim
                _stopR[2] = _radius + bw;     _stopC[2] = BorderColor;           // solid rim
                _stopR[3] = _radius + bw + f; _stopC[3] = Fade(BorderColor);     // fringe out
                stops = 4;
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

        private static Color Fade(Color c) { c.a = 0f; return c; }
    }
}
