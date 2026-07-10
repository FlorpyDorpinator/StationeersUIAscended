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

        private const float Feather = 1.25f;   // px alpha fade on the outermost edge

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_radius <= 0f) return;

            int segments = Mathf.Clamp(Mathf.CeilToInt(_radius * 1.2f), 48, 256);

            float bw = BorderWidth;
            bool hasBorder = bw > 0.1f && BorderColor.a > 0.01f;

            // Radial stops. The last one is a transparent fringe: overlay canvases get no MSAA,
            // so a hard mesh edge stair-steps. A 1px alpha ramp is free anti-aliasing.
            Color edge = hasBorder ? BorderColor : color;
            Color fade = edge; fade.a = 0f;

            // Fan for the fill (centre vertex 0), then concentric rings.
            vh.AddVert(Vector2.zero, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                vh.AddVert(dir * _radius, color, Vector2.one);                       // ring A: fill edge
                if (hasBorder)
                {
                    vh.AddVert(dir * _radius, BorderColor, Vector2.one);             // ring B: border inner
                    vh.AddVert(dir * (_radius + bw), BorderColor, Vector2.one);      // ring C: border outer
                }
                vh.AddVert(dir * (_radius + (hasBorder ? bw : 0f) + Feather), fade, Vector2.one); // fringe
            }

            int stride = hasBorder ? 4 : 2;
            for (int i = 0; i < segments; i++)
            {
                int a = 1 + i * stride;
                int b = 1 + (i + 1) * stride;
                vh.AddTriangle(0, a, b);                       // fill fan
                for (int s = 0; s < stride - 1; s++)           // bands out to the fringe
                {
                    vh.AddTriangle(a + s, a + s + 1, b + s + 1);
                    vh.AddTriangle(a + s, b + s + 1, b + s);
                }
            }
        }
    }
}
