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

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_radius <= 0f) return;

            int segments = Mathf.Clamp(Mathf.CeilToInt(_radius * 0.6f), 24, 128);

            // Fan: centre vertex + rim ring.
            vh.AddVert(Vector2.zero, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                vh.AddVert(new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * _radius, color, Vector2.one);
            }
            for (int i = 1; i <= segments; i++)
                vh.AddTriangle(0, i, i + 1);

            // Optional rim band drawn just outside the fill.
            float bw = BorderWidth;
            if (bw <= 0.1f || BorderColor.a <= 0.01f) return;

            int ringStart = vh.currentVertCount;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                vh.AddVert(dir * _radius, BorderColor, Vector2.zero);
                vh.AddVert(dir * (_radius + bw), BorderColor, Vector2.one);
            }
            for (int i = 0; i < segments; i++)
            {
                int b = ringStart + i * 2;
                vh.AddTriangle(b, b + 1, b + 3);
                vh.AddTriangle(b, b + 3, b + 2);
            }
        }
    }
}
