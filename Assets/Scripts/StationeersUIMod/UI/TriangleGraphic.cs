using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// A tiny solid triangle for the scroll-value wedges (up/down arrows around the value).
    /// Drawn as a mesh instead of a glyph: the game's fonts have no arrow characters, and a
    /// mesh gets the same baked-fringe anti-aliasing treatment as the wedges.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TriangleGraphic : MaskableGraphic
    {
        private bool _pointsUp = true;
        private float _size = 10f;

        public void Configure(bool pointsUp, float size)
        {
            if (_pointsUp == pointsUp && Mathf.Approximately(_size, size)) return;
            _pointsUp = pointsUp;
            _size = size;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float w = _size;
            float h = _size * 0.72f;
            float dir = _pointsUp ? 1f : -1f;

            // Solid core + an alpha-0 skirt around it (overlay canvases get no MSAA).
            var tip = new Vector2(0f, dir * h * 0.5f);
            var left = new Vector2(-w * 0.5f, -dir * h * 0.5f);
            var right = new Vector2(w * 0.5f, -dir * h * 0.5f);
            float f = 1.1f;

            Color solid = color;
            Color faded = color; faded.a = 0f;

            vh.AddVert(tip, solid, Vector2.zero);
            vh.AddVert(left, solid, Vector2.zero);
            vh.AddVert(right, solid, Vector2.zero);
            vh.AddVert(tip + new Vector2(0f, dir * f * 1.4f), faded, Vector2.zero);
            vh.AddVert(left + new Vector2(-f * 1.2f, -dir * f), faded, Vector2.zero);
            vh.AddVert(right + new Vector2(f * 1.2f, -dir * f), faded, Vector2.zero);

            vh.AddTriangle(0, 1, 2);      // core
            vh.AddTriangle(3, 4, 0);      // fringe: tip-left
            vh.AddTriangle(4, 1, 0);
            vh.AddTriangle(0, 2, 5);      // fringe: tip-right
            vh.AddTriangle(0, 5, 3);
            vh.AddTriangle(1, 4, 5);      // fringe: bottom
            vh.AddTriangle(1, 5, 2);
        }
    }
}
