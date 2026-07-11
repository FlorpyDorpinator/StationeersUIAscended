using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The visor-edge darkening: a vertex-gradient ring, transparent on an inner ellipse
    /// and solid at the screen border. No texture, no shader — the gradient is baked into
    /// the mesh like everything else in this stack.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VignetteGraphic : MaskableGraphic
    {
        private const int Segments = 64;

        /// <summary>0..1 — how far in from the corners the darkening starts.</summary>
        public float InnerExtent = 0.72f;

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (color.a <= 0.004f) return;
            var rect = GetPixelAdjustedRect();
            float hw = rect.width * 0.5f, hh = rect.height * 0.5f;
            if (hw < 1f || hh < 1f) return;

            Color inner = color; inner.a = 0f;
            float ext = Mathf.Clamp(InnerExtent, 0.2f, 0.98f);

            for (int i = 0; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                // Inner ring: ellipse. Outer ring: pushed to the rect border along the
                // same ray (the corners get the strongest shading, like a real vignette).
                vh.AddVert(new Vector3(ca * hw * ext, sa * hh * ext, 0f), inner, Vector2.zero);
                float scale = 1f / Mathf.Max(Mathf.Abs(ca), Mathf.Abs(sa));
                vh.AddVert(new Vector3(ca * hw * scale, sa * hh * scale, 0f), color, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                int b = i * 2;
                vh.AddTriangle(b, b + 2, b + 3);
                vh.AddTriangle(b, b + 3, b + 1);
            }
        }
    }
}
