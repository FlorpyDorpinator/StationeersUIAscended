using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// A single annular-sector wedge, generated procedurally at runtime via OnPopulateMesh.
    ///
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


        public float BorderWidth { get; set; } = 0f;


        public Color BorderColor { get; set; } = Color.clear;


        public float RimHighlight { get; set; } = 0f;

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

            Color fill = color;
            Color border = BorderColor;
            float bWidth = Mathf.Max(0f, BorderWidth);
            bool hasBorder = bWidth > 0.1f && border.a > 0.01f;

            float borderOuter = outer + bWidth;

            // Build main fill + optional outer shine rim + border band
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(a0, a1, i / (float)segments);
                var dir = new Vector2(Mathf.Cos(t), -Mathf.Sin(t));

                // Inner vert (pure fill)
                vh.AddVert(dir * inner, fill, Vector2.zero);

                // Outer vert for fill (with optional rim shine/highlight)
                Color outerFill = fill;
                if (RimHighlight > 0.001f)
                {
                    // Orange-tinted highlight/rim for "orange highlight" as requested.
                    // Pulls the outer edge toward warm orange for pop (especially nice against blue selected fill).
                    Color orangeHighlight = new Color(1.0f, 0.55f, 0.15f, 0.35f);
                    outerFill = Color.Lerp(fill, orangeHighlight, RimHighlight * 0.55f);
                    outerFill.a = fill.a;
                }
                vh.AddVert(dir * outer, outerFill, Vector2.one);

                // Optional border band outer verts
                if (hasBorder)
                {
                    vh.AddVert(dir * borderOuter, border, new Vector2(0, 2));
                }
            }

            // Fill triangles (inner to main outer ring). 
            // When hasBorder the vertex stride is 3 per radial line: [inner, fillOuter, borderOuter]
            int stride = hasBorder ? 3 : 2;
            for (int i = 0; i < segments; i++)
            {
                int b0 = i * stride;           // inner i
                int b1 = b0 + 1;               // fill outer i
                int n0 = (i + 1) * stride;     // inner i+1
                int n1 = n0 + 1;               // fill outer i+1

                // Two triangles for the annular sector fill
                vh.AddTriangle(b0, b1, n1);
                vh.AddTriangle(b0, n1, n0);
            }

            // Border band triangles (slim outer stroke, only the thin outer band)
            if (hasBorder)
            {
                for (int i = 0; i < segments; i++)
                {
                    int b1 = i * 3 + 1;   // fill outer i
                    int b2 = i * 3 + 2;   // border outer i
                    int n1 = (i + 1) * 3 + 1;
                    int n2 = (i + 1) * 3 + 2;

                    vh.AddTriangle(b1, b2, n2);
                    vh.AddTriangle(b1, n2, n1);
                }
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
            if (dist < _innerR || dist > _outerR + OuterBulge + BorderWidth + 2f) return false;
            if (_fullRing) return true;

            // Back to ImGui convention (y-down, clockwise) before comparing with a0/a1.
            float ang = Mathf.Atan2(-local.y, local.x);
            float rel = Mathf.Repeat(ang - _a0, Mathf.PI * 2f);
            return rel <= Mathf.Abs(_a1 - _a0);
        }
    }
}
