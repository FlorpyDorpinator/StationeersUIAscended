using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// A single annular-sector wedge, generated procedurally at runtime via OnPopulateMesh.
    ///
    /// This is the key component Florpy introduced to make a reliable Unity radial possible
    /// without the problems of the earlier prefab implementation (on main).
    ///
    /// Because the mesh is emitted from (a0, a1, innerR, outerR) on every change, a single
    /// component type handles any number of entries (N=1..14+). Pre-authored prefabs baked
    /// a fixed capacity or per-N layout into assets, causing capacity limits, dead zones,
    /// and maintenance pain.
    ///
    /// Angles come in ImGui convention (y-down, clockwise from +X). The sine negation for
    /// UGUI's y-up space is applied once when emitting vertices.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RadialWedgeGraphic : MaskableGraphic, ICanvasRaycastFilter
    {
        private float _innerR = 90f;
        private float _outerR = 200f;
        private float _a0;
        private float _a1 = Mathf.PI * 0.5f;
        private bool _fullRing;

        /// <summary>Extra radial thickness added on the outer edge (hover pop-out).</summary>
        public float OuterBulge { get; set; }

        public void SetGeometry(float innerR, float outerR, float a0, float a1, bool fullRing)
        {
            if (Mathf.Approximately(_innerR, innerR) && Mathf.Approximately(_outerR, outerR)
                && Mathf.Approximately(_a0, a0) && Mathf.Approximately(_a1, a1) && _fullRing == fullRing)
                return;
            _innerR = innerR;
            _outerR = outerR;
            _a0 = a0;
            _a1 = a1;
            _fullRing = fullRing;
            SetVerticesDirty();
        }

        /// <summary>Call after changing OuterBulge (cheap: geometry only, no layout).</summary>
        public void RefreshGeometry() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float inner = _innerR;
            float outer = _outerR + OuterBulge;
            if (outer <= inner) return;

            float a0 = _a0;
            float a1 = _fullRing ? _a0 + Mathf.PI * 2f : _a1;
            float sweep = Mathf.Abs(a1 - a0);
            // ~4px of arc per segment keeps big rings smooth without exploding vertex count.
            int segments = Mathf.Clamp(Mathf.CeilToInt(sweep * outer / 4f), 3, 192);

            var c = color;
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(a0, a1, i / (float)segments);
                // ImGui angles are y-down; UGUI local space is y-up. Negate the sine.
                var dir = new Vector2(Mathf.Cos(t), -Mathf.Sin(t));
                vh.AddVert(dir * inner, c, Vector2.zero);
                vh.AddVert(dir * outer, c, Vector2.one);
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                vh.AddTriangle(b, b + 1, b + 3);
                vh.AddTriangle(b, b + 3, b + 2);
            }
        }

        /// <summary>Polar hit test, so Unity's EventSystem would route pointer events correctly.
        /// (The radial currently drives hover itself, but this keeps the graphic honest and
        /// stops it from swallowing clicks meant for the world.)</summary>
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!raycastTarget) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, screenPoint, eventCamera, out Vector2 local);

            float dist = local.magnitude;
            if (dist < _innerR || dist > _outerR + OuterBulge) return false;
            if (_fullRing) return true;

            // Back to ImGui convention (y-down, clockwise) before comparing with a0/a1.
            float ang = Mathf.Atan2(-local.y, local.x);
            float rel = Mathf.Repeat(ang - _a0, Mathf.PI * 2f);
            return rel <= Mathf.Abs(_a1 - _a0);
        }
    }
}
