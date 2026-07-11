using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// A single annular-sector wedge, generated procedurally at runtime via OnPopulateMesh.
    ///
    /// Geometry is a grid: angular COLUMNS (left to right across the sweep) times radial
    /// STOPS (inner to outer). Colors live on vertices, so borders and anti-aliasing are
    /// baked into the mesh — overlay canvases never get MSAA, so a ~1px color/alpha ramp
    /// on every free edge IS the anti-aliasing.
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

        /// <summary>Draw the border along the straight side edges too (full wedge outline).</summary>
        public bool SideBorders { get; set; }

        /// <summary>Anti-alias the side edges. Only valid when wedges do not abut (gap > 0),
        /// otherwise the fade on both sides of a shared edge paints a visible seam.</summary>
        public bool FeatherSides { get; set; }

        /// <summary>Cap in px for the OUTWARD side fringe (set to half the wedge gap by the
        /// view). Without it a feather wider than the gap bleeds into the neighbour wedge.</summary>
        public float SideFeatherLimit { get; set; } = float.MaxValue;

        /// <summary>Side line width in px where the wedge meets the hub / the outer rim.
        /// The old constant-ANGLE lines were physically wider at the rim than the hub;
        /// these are true pixel widths at each end (equal = straight parallel lines).</summary>
        public float SideWidthInner { get; set; } = 3.2f;
        public float SideWidthOuter { get; set; } = 3.2f;

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
            if (sweep < 0.001f) return;

            Color fill = color;
            Color border = BorderColor;
            float bWidth = Mathf.Max(0f, BorderWidth);
            bool hasBorder = bWidth > 0.1f && border.a > 0.01f;

            // Outer edge gloss — the "shader" look. Colour comes from the RimShine palette
            // entry, amount from RimHighlight (driven by ShineIntensity in the view). The
            // 0.9 factor keeps full intensity visibly tinted instead of barely-there.
            Color outerFill = fill;
            if (RimHighlight > 0.001f)
            {
                Color shine = Overlay.RadialPalette.RimShine != null
                    ? Overlay.RadialPalette.RimShine.Value
                    : new Color(1f, 1f, 1f, 0.10f);
                outerFill = Color.Lerp(fill, shine, Mathf.Clamp01(RimHighlight * 0.9f));
                outerFill.a = fill.a;
            }

            // ---- radial stops, inner -> outer, each with its colour ----
            // Both sides of the border band ramp: an alpha-0 fringe outside, and a colour
            // ramp from the fill on the inside (a zero-width jump from translucent navy to
            // opaque orange stair-steps just as badly as an alpha edge).
            float f = Feather;
            _stopR[0] = inner - f; _stopC[0] = Fade(fill);
            _stopR[1] = inner;     _stopC[1] = fill;
            int stops = 2;
            if (hasBorder)
            {
                float rampStart = Mathf.Max(inner + 0.01f, outer - f);
                _stopR[stops] = rampStart;             _stopC[stops++] = outerFill;   // fill up to here
                _stopR[stops] = outer;                 _stopC[stops++] = border;      // ramp -> border
                _stopR[stops] = outer + bWidth;        _stopC[stops++] = border;      // solid band
                _stopR[stops] = outer + bWidth + f;    _stopC[stops++] = Fade(border);// fringe out
            }
            else
            {
                _stopR[stops] = outer;      _stopC[stops++] = outerFill;
                _stopR[stops] = outer + f;  _stopC[stops++] = Fade(outerFill);
            }

            // ---- angular columns, a0 -> a1 ----
            // Side lines are TRUE PIXEL widths: the border/fill boundary sits at angular
            // offset w(r)/r per radial stop, where w(r) lerps SideWidthInner->SideWidthOuter
            // across the band. Equal widths = straight parallel lines; the old constant-angle
            // boundary made lines physically fat at the rim and skinny at the hub.
            int colCount = 0;
            bool sideBorder = !_fullRing && SideBorders && hasBorder;
            bool sideFeather = !_fullRing && FeatherSides;
            float rMin = Mathf.Max(1f, inner - f);
            float wI = Mathf.Max(0f, SideWidthInner);
            float wO = Mathf.Max(0f, SideWidthOuter);
            float fOut = Mathf.Min(f, Mathf.Max(0f, SideFeatherLimit));

            // Narrow wedges: shrink the side treatment so it never eats the whole sector.
            float worstAng = sideBorder ? (Mathf.Max(wI, wO) + f) / rMin
                           : sideFeather ? fOut / rMin : 0f;
            float maxSide = sweep * 0.30f;
            if (worstAng > maxSide && worstAng > 0f)
            {
                float k = maxSide / worstAng;
                wI *= k;
                wO *= k;
                f *= k;
                fOut *= k;
            }

            float sideAng = sideBorder ? (Mathf.Max(wI, wO) + f) / rMin : 0f;
            float aStart = a0 + sideAng;
            float aEnd = a1 - sideAng;
            float interiorSweep = Mathf.Max(0.001f, aEnd - aStart);
            // ~2.5px of arc per segment: curved edges read as curves, not polygons.
            int segments = Mathf.Clamp(Mathf.CeilToInt(interiorSweep * outer / 2.5f), 2, 256);

            void EmitFixed(float angle, float blend, float alphaMul)
            {
                EmitColumn(vh, angle, blend, alphaMul, stops, border);
                colCount++;
            }
            // A column whose angle varies per stop: edge + sign * (w(r) + extraPx) / r.
            void EmitEdge(float edge, float sign, float widthI, float widthO, float extraPx,
                float blend, float alphaMul)
            {
                for (int s = 0; s < stops; s++)
                {
                    float r = Mathf.Max(1f, _stopR[s]);
                    float w = Mathf.Lerp(widthI, widthO, Mathf.InverseLerp(inner, outer, r));
                    float ang = edge + sign * ((w + extraPx) / r);
                    var dir = new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang));
                    Color c = _stopC[s];
                    if (blend < 0.999f)
                    {
                        Color bc = border;
                        bc.a = c.a <= 0.001f ? 0f : border.a;
                        c = Color.Lerp(bc, c, blend);
                    }
                    c.a *= alphaMul;
                    vh.AddVert(dir * _stopR[s], c, new Vector2(0f, s));
                }
                colCount++;
            }

            if (sideBorder)
            {
                EmitEdge(a0, -1f, fOut, fOut, 0f, 0f, 0f);   // outward fringe (into the gap)
                EmitFixed(a0, 0f, 1f);                        // the wedge edge itself
                EmitEdge(a0, 1f, wI, wO, 0f, 0f, 1f);         // border -> fill boundary
                EmitEdge(a0, 1f, wI, wO, f, 1f, 1f);          // AA ramp into the fill
                for (int i = 0; i <= segments; i++)
                    EmitFixed(Mathf.Lerp(aStart, aEnd, i / (float)segments), 1f, 1f);
                EmitEdge(a1, -1f, wI, wO, f, 1f, 1f);
                EmitEdge(a1, -1f, wI, wO, 0f, 0f, 1f);
                EmitFixed(a1, 0f, 1f);
                EmitEdge(a1, 1f, fOut, fOut, 0f, 0f, 0f);
            }
            else if (sideFeather)
            {
                EmitEdge(a0, -1f, fOut, fOut, 0f, 1f, 0f);
                for (int i = 0; i <= segments; i++)
                    EmitFixed(Mathf.Lerp(a0, a1, i / (float)segments), 1f, 1f);
                EmitEdge(a1, 1f, fOut, fOut, 0f, 1f, 0f);
            }
            else
            {
                // Abutting wedges (or a full ring): hard side edges, current classic look.
                for (int i = 0; i <= segments; i++)
                    EmitFixed(Mathf.Lerp(a0, a1, i / (float)segments), 1f, 1f);
            }

            for (int c = 0; c < colCount - 1; c++)
                Quads(vh, c, c + 1, stops);
        }

        /// <summary>Width of the anti-aliasing ramp, in pixels. Live-tunable from F10.</summary>
        private static float Feather => UIAConfig.RadialEdgeFeather != null
            ? UIAConfig.RadialEdgeFeather.Value : 1.25f;

        private static readonly float[] _stopR = new float[6];
        private static readonly Color[] _stopC = new Color[6];

        private static Color Fade(Color c) { c.a = 0f; return c; }

        private static void EmitColumn(VertexHelper vh, float angle, float blend, float alphaMul,
            int stops, Color border)
        {
            var dir = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
            for (int s = 0; s < stops; s++)
            {
                Color c = _stopC[s];
                if (blend < 0.999f)
                {
                    // Border-coloured column: keep the stop's alpha SHAPE (0 at the radial
                    // fringes) but pull the colour toward the border.
                    Color bc = border;
                    bc.a = c.a <= 0.001f ? 0f : border.a;
                    c = Color.Lerp(bc, c, blend);
                }
                c.a *= alphaMul;
                vh.AddVert(dir * _stopR[s], c, new Vector2(0f, s));
            }
        }

        private static void Quads(VertexHelper vh, int colA, int colB, int stops)
        {
            int baseA = colA * stops;
            int baseB = colB * stops;
            for (int s = 0; s < stops - 1; s++)
            {
                vh.AddTriangle(baseA + s, baseA + s + 1, baseB + s + 1);
                vh.AddTriangle(baseA + s, baseB + s + 1, baseB + s);
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
