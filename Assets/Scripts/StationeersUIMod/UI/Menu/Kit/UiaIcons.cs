using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>The kit's drawn icon set (F10 plan B7 + mockup 3's visual language). The game's
    /// TMP font renders ASCII only, so every icon here is GEOMETRY — line strokes, dots and rings
    /// emitted by <see cref="UiaIconGraphic"/> — never a glyph. Each stroke carries a baked alpha
    /// fringe (the TriangleGraphic idiom: overlay canvases get no MSAA, so the ~1px ramp IS the
    /// anti-aliasing). Colour comes from the caller, who reads it off <see cref="UiaTheme"/>.</summary>
    public enum UiaIcon
    {
        Document,       // sheet with a folded corner + text lines (a Storage Layout)
        Folder,
        Briefcase,
        Rocket,
        Box,            // shipping cube
        Kebab,          // three vertical dots (the card menu button)
        Pencil,
        Trash,
        Share,          // 3 nodes + 2 connecting lines
        Plus,
        ChevronUp,
        ChevronDown,
        ChevronRight,
        X,
        Capture,        // four viewfinder corner brackets
        Search,         // magnifier
        Info,           // the (i) help affordance: a serif "i" (dot + tall stem + flag + foot)
        CaretDown,      // solid down triangle: the dropdown affordance
    }

    /// <summary>One drawn line icon. Renders in a square centred on its RectTransform, scaled to
    /// the SMALLER rect dimension, so the same icon works at any size the layout hands it. All
    /// geometry is unit-space [-1,1] scaled at emit time; colour is the plain Graphic colour, so
    /// re-tinting is free and theme restyles just rebuild the widget like everything in the kit.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiaIconGraphic : MaskableGraphic
    {
        private UiaIcon _kind = UiaIcon.Plus;
        private float _strokeScale = 1f;

        // Per-emit scratch, set once at the top of OnPopulateMesh.
        private float _half;      // icon half-extent in px
        private float _stroke;    // stroke width in px

        public void Configure(UiaIcon kind, float strokeScale = 1f)
        {
            if (_kind == kind && Mathf.Approximately(_strokeScale, strokeScale)) return;
            _kind = kind;
            _strokeScale = strokeScale;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float s = Mathf.Min(r.width, r.height);
            if (s < 4f) return;
            _half = s * 0.42f;                                   // ~8% margin inside the rect
            _stroke = Mathf.Max(1.2f, s * 0.085f) * _strokeScale;
            Vector2 c = r.center;

            switch (_kind)
            {
                case UiaIcon.Document: Document(vh, c); break;
                case UiaIcon.Folder: Folder(vh, c); break;
                case UiaIcon.Briefcase: Briefcase(vh, c); break;
                case UiaIcon.Rocket: Rocket(vh, c); break;
                case UiaIcon.Box: Box(vh, c); break;
                case UiaIcon.Kebab: Kebab(vh, c); break;
                case UiaIcon.Pencil: Pencil(vh, c); break;
                case UiaIcon.Trash: Trash(vh, c); break;
                case UiaIcon.Share: Share(vh, c); break;
                case UiaIcon.Plus: Plus(vh, c); break;
                case UiaIcon.ChevronUp: Chevron(vh, c, 0f, 1f); break;
                case UiaIcon.ChevronDown: Chevron(vh, c, 0f, -1f); break;
                case UiaIcon.ChevronRight: Chevron(vh, c, 1f, 0f); break;
                case UiaIcon.X: Cross(vh, c); break;
                case UiaIcon.Capture: Capture(vh, c); break;
                case UiaIcon.Search: Search(vh, c); break;
                case UiaIcon.Info: Info(vh, c); break;
                case UiaIcon.CaretDown: CaretDown(vh, c); break;
            }
        }

        // ---- geometry helpers (all unit-space points, scaled by _half around centre c) ----

        private Vector2 P(Vector2 c, float x, float y) => new Vector2(c.x + x * _half, c.y + y * _half);

        /// <summary>A stroked segment: a solid core quad plus an alpha-0 fringe strip on each
        /// side (and a tiny lengthwise extension so butt ends don't shear to a hard pixel).</summary>
        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float w = -1f)
        {
            if (w <= 0f) w = _stroke;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            d /= len;
            Vector2 n = new Vector2(-d.y, d.x);
            float hw = w * 0.5f;
            const float f = 0.9f;   // AA fringe px

            Color solid = color;
            Color faded = color; faded.a = 0f;
            Vector2 ea = a - d * f * 0.5f;   // fringe rows stretch slightly past the ends
            Vector2 eb = b + d * f * 0.5f;

            int i0 = vh.currentVertCount;
            // Column at A: fringe+, core+, core-, fringe-  (then the same at B).
            vh.AddVert(ea + n * (hw + f), faded, Vector2.zero);
            vh.AddVert(a + n * hw, solid, Vector2.zero);
            vh.AddVert(a - n * hw, solid, Vector2.zero);
            vh.AddVert(ea - n * (hw + f), faded, Vector2.zero);
            vh.AddVert(eb + n * (hw + f), faded, Vector2.zero);
            vh.AddVert(b + n * hw, solid, Vector2.zero);
            vh.AddVert(b - n * hw, solid, Vector2.zero);
            vh.AddVert(eb - n * (hw + f), faded, Vector2.zero);

            for (int s = 0; s < 3; s++)
            {
                vh.AddTriangle(i0 + s, i0 + 4 + s, i0 + 5 + s);
                vh.AddTriangle(i0 + s, i0 + 5 + s, i0 + 1 + s);
            }
        }

        /// <summary>An open polyline through unit-space points.</summary>
        private void Poly(VertexHelper vh, Vector2 c, float[] xs, float[] ys, bool closed, float w = -1f)
        {
            int n = Mathf.Min(xs.Length, ys.Length);
            for (int i = 0; i < n - 1; i++)
                Line(vh, P(c, xs[i], ys[i]), P(c, xs[i + 1], ys[i + 1]), w);
            if (closed && n > 2)
                Line(vh, P(c, xs[n - 1], ys[n - 1]), P(c, xs[0], ys[0]), w);
        }

        /// <summary>A filled dot: an 8-segment fan with an alpha-0 outer ring for AA.</summary>
        private void Dot(VertexHelper vh, Vector2 at, float r)
        {
            const int Segs = 8;
            const float f = 0.9f;
            Color solid = color;
            Color faded = color; faded.a = 0f;
            int i0 = vh.currentVertCount;
            vh.AddVert(at, solid, Vector2.zero);
            for (int i = 0; i < Segs; i++)
            {
                float ang = i * (Mathf.PI * 2f / Segs);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                vh.AddVert(at + dir * r, solid, Vector2.zero);
                vh.AddVert(at + dir * (r + f), faded, Vector2.zero);
            }
            for (int i = 0; i < Segs; i++)
            {
                int a = i0 + 1 + i * 2;
                int b = i0 + 1 + ((i + 1) % Segs) * 2;
                vh.AddTriangle(i0, a, b);              // core wedge
                vh.AddTriangle(a, a + 1, b + 1);       // fringe
                vh.AddTriangle(a, b + 1, b);
            }
        }

        /// <summary>A solid anti-aliased triangle. The fringe is the triangle scaled about its
        /// INCENTRE, which moves all three edges outward by the same distance.</summary>
        private void FilledTri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
        {
            const float f = 0.9f;
            float la = (b - c).magnitude, lb = (c - a).magnitude, lc = (a - b).magnitude;
            float per = la + lb + lc;
            float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
            if (per < 0.01f || area < 0.01f) return;
            Vector2 inc = (a * la + b * lb + c * lc) / per;
            float k = (2f * area / per + f) / (2f * area / per);
            Color solid = color;
            Color faded = color; faded.a = 0f;
            int i0 = vh.currentVertCount;
            vh.AddVert(a, solid, Vector2.zero);
            vh.AddVert(b, solid, Vector2.zero);
            vh.AddVert(c, solid, Vector2.zero);
            vh.AddVert(inc + (a - inc) * k, faded, Vector2.zero);
            vh.AddVert(inc + (b - inc) * k, faded, Vector2.zero);
            vh.AddVert(inc + (c - inc) * k, faded, Vector2.zero);
            vh.AddTriangle(i0, i0 + 1, i0 + 2);
            for (int e = 0; e < 3; e++)
            {
                int p = i0 + e, q = i0 + (e + 1) % 3;
                vh.AddTriangle(p, q, q + 3);
                vh.AddTriangle(p, q + 3, p + 3);
            }
        }

        /// <summary>A stroked circle: a solid ring band with alpha-0 fringes inside and out.</summary>
        private void Ring(VertexHelper vh, Vector2 at, float r, float w = -1f, int segs = 16)
        {
            if (w <= 0f) w = _stroke;
            float hw = w * 0.5f;
            const float f = 0.9f;
            Color solid = color;
            Color faded = color; faded.a = 0f;
            int i0 = vh.currentVertCount;
            for (int i = 0; i < segs; i++)
            {
                float ang = i * (Mathf.PI * 2f / segs);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                vh.AddVert(at + dir * (r - hw - f), faded, Vector2.zero);
                vh.AddVert(at + dir * (r - hw), solid, Vector2.zero);
                vh.AddVert(at + dir * (r + hw), solid, Vector2.zero);
                vh.AddVert(at + dir * (r + hw + f), faded, Vector2.zero);
            }
            for (int i = 0; i < segs; i++)
            {
                int a = i0 + i * 4;
                int b = i0 + ((i + 1) % segs) * 4;
                for (int s = 0; s < 3; s++)
                {
                    vh.AddTriangle(a + s, b + s, b + s + 1);
                    vh.AddTriangle(a + s, b + s + 1, a + s + 1);
                }
            }
        }

        // ---- the icons ----

        private void Document(VertexHelper vh, Vector2 c)
        {
            // Sheet outline with a folded top-right corner, then the fold crease + 2 text lines.
            Poly(vh, c,
                new[] { -0.6f, -0.6f, 0.25f, 0.6f, 0.6f },
                new[] { -0.8f, 0.8f, 0.8f, 0.45f, -0.8f }, true);
            Poly(vh, c, new[] { 0.25f, 0.25f, 0.6f }, new[] { 0.8f, 0.45f, 0.45f }, false);
            Line(vh, P(c, -0.35f, 0.05f), P(c, 0.35f, 0.05f));
            Line(vh, P(c, -0.35f, -0.35f), P(c, 0.35f, -0.35f));
        }

        private void Folder(VertexHelper vh, Vector2 c)
        {
            // Body with the classic top-left tab, one closed silhouette.
            Poly(vh, c,
                new[] { -0.85f, -0.85f, -0.30f, -0.12f, 0.85f, 0.85f },
                new[] { -0.55f, 0.50f, 0.50f, 0.28f, 0.28f, -0.55f }, true);
        }

        private void Briefcase(VertexHelper vh, Vector2 c)
        {
            Poly(vh, c,
                new[] { -0.8f, -0.8f, 0.8f, 0.8f },
                new[] { -0.55f, 0.35f, 0.35f, -0.55f }, true);
            // Handle above the lid.
            Poly(vh, c,
                new[] { -0.25f, -0.25f, 0.25f, 0.25f },
                new[] { 0.35f, 0.62f, 0.62f, 0.35f }, false);
            // Clasp: a short centre tick on the lid line.
            Line(vh, P(c, 0f, 0.35f), P(c, 0f, 0.12f));
        }

        private void Rocket(VertexHelper vh, Vector2 c)
        {
            // Hull: nose to skirt, mirrored.
            Poly(vh, c,
                new[] { 0f, 0.30f, 0.30f, -0.30f, -0.30f },
                new[] { 0.85f, 0.15f, -0.50f, -0.50f, 0.15f }, true);
            // Fins.
            Poly(vh, c, new[] { 0.30f, 0.62f, 0.30f }, new[] { -0.12f, -0.62f, -0.50f }, false);
            Poly(vh, c, new[] { -0.30f, -0.62f, -0.30f }, new[] { -0.12f, -0.62f, -0.50f }, false);
            // Porthole.
            Ring(vh, P(c, 0f, 0.22f), _half * 0.16f, _stroke * 0.85f, 12);
        }

        private void Box(VertexHelper vh, Vector2 c)
        {
            // Isometric-ish cube wireframe: front face + top + right depth edges.
            Poly(vh, c,
                new[] { -0.75f, -0.75f, 0.35f, 0.35f },
                new[] { -0.75f, 0.35f, 0.35f, -0.75f }, true);
            Line(vh, P(c, -0.75f, 0.35f), P(c, -0.40f, 0.72f));
            Line(vh, P(c, 0.35f, 0.35f), P(c, 0.72f, 0.72f));
            Line(vh, P(c, -0.40f, 0.72f), P(c, 0.72f, 0.72f));
            Line(vh, P(c, 0.35f, -0.75f), P(c, 0.72f, -0.38f));
            Line(vh, P(c, 0.72f, 0.72f), P(c, 0.72f, -0.38f));
        }

        private void Kebab(VertexHelper vh, Vector2 c)
        {
            // Three clearly SEPARATE dots. The old recipe (r >= 1.4px, pitch 0.55*half) left only
            // ~0.7px of solid gap at 15-16px, which the 0.9px AA fringes filled in — the menu
            // button blurred into "a dot over a bar" and read like the (i) glyph. Smaller dots on
            // a wider pitch keep a ~2.3px solid gap at 15px (still inside the icon's height).
            float r = Mathf.Max(1.15f, _half * 0.15f);
            const float pitch = 0.74f;
            Dot(vh, P(c, 0f, pitch), r);
            Dot(vh, P(c, 0f, 0f), r);
            Dot(vh, P(c, 0f, -pitch), r);
        }

        private void Pencil(VertexHelper vh, Vector2 c)
        {
            // Body: two parallel strokes down the diagonal; tip converges; eraser cap closes it.
            Vector2 d = new Vector2(1f, 1f).normalized;          // body direction (toward cap)
            Vector2 n = new Vector2(-d.y, d.x);
            Vector2 tip = P(c, -0.78f, -0.78f);
            Vector2 heel = new Vector2(c.x - 0.45f * _half, c.y - 0.45f * _half);   // where the cone meets the body
            Vector2 cap = new Vector2(c.x + 0.62f * _half, c.y + 0.62f * _half);
            float hw = _half * 0.20f;
            Line(vh, heel + n * hw, cap + n * hw);
            Line(vh, heel - n * hw, cap - n * hw);
            Line(vh, tip, heel + n * hw);
            Line(vh, tip, heel - n * hw);
            Line(vh, cap + n * hw, cap - n * hw);
        }

        private void Trash(VertexHelper vh, Vector2 c)
        {
            Line(vh, P(c, -0.65f, 0.45f), P(c, 0.65f, 0.45f));                      // lid
            Poly(vh, c,                                                              // handle
                new[] { -0.18f, -0.18f, 0.18f, 0.18f },
                new[] { 0.45f, 0.68f, 0.68f, 0.45f }, false);
            Poly(vh, c,                                                              // can
                new[] { -0.50f, -0.40f, 0.40f, 0.50f },
                new[] { 0.45f, -0.72f, -0.72f, 0.45f }, false);
            Line(vh, P(c, -0.14f, 0.22f), P(c, -0.12f, -0.50f), _stroke * 0.8f);     // ribs
            Line(vh, P(c, 0.14f, 0.22f), P(c, 0.12f, -0.50f), _stroke * 0.8f);
        }

        private void Share(VertexHelper vh, Vector2 c)
        {
            Line(vh, P(c, -0.42f, 0f), P(c, 0.42f, 0.55f));
            Line(vh, P(c, -0.42f, 0f), P(c, 0.42f, -0.55f));
            float r = Mathf.Max(1.8f, _half * 0.24f);
            Dot(vh, P(c, -0.42f, 0f), r);
            Dot(vh, P(c, 0.42f, 0.55f), r);
            Dot(vh, P(c, 0.42f, -0.55f), r);
        }

        private void Plus(VertexHelper vh, Vector2 c)
        {
            Line(vh, P(c, -0.65f, 0f), P(c, 0.65f, 0f));
            Line(vh, P(c, 0f, -0.65f), P(c, 0f, 0.65f));
        }

        /// <summary>Chevron pointing along (dx,dy) — one of the two axes.</summary>
        private void CaretDown(VertexHelper vh, Vector2 c)
        {
            // A stroked chevron at dropdown size (9-12px) smudged into a blob; a solid wedge
            // reads as "this opens a list" at any size.
            FilledTri(vh, P(c, -0.62f, 0.32f), P(c, 0.62f, 0.32f), P(c, 0f, -0.40f));
        }

        private void Chevron(VertexHelper vh, Vector2 c, float dx, float dy)
        {
            if (dx > 0.5f)
            {
                Line(vh, P(c, -0.25f, 0.5f), P(c, 0.28f, 0f));
                Line(vh, P(c, 0.28f, 0f), P(c, -0.25f, -0.5f));
            }
            else if (dy > 0.5f)
            {
                Line(vh, P(c, -0.5f, -0.25f), P(c, 0f, 0.28f));
                Line(vh, P(c, 0f, 0.28f), P(c, 0.5f, -0.25f));
            }
            else
            {
                Line(vh, P(c, -0.5f, 0.25f), P(c, 0f, -0.28f));
                Line(vh, P(c, 0f, -0.28f), P(c, 0.5f, 0.25f));
            }
        }

        private void Cross(VertexHelper vh, Vector2 c)
        {
            Line(vh, P(c, -0.55f, -0.55f), P(c, 0.55f, 0.55f));
            Line(vh, P(c, -0.55f, 0.55f), P(c, 0.55f, -0.55f));
        }

        private void Capture(VertexHelper vh, Vector2 c)
        {
            const float o = 0.75f;   // outer corner
            const float l = 0.32f;   // bracket arm length
            // TL
            Line(vh, P(c, -o, o - l), P(c, -o, o));
            Line(vh, P(c, -o, o), P(c, -o + l, o));
            // TR
            Line(vh, P(c, o - l, o), P(c, o, o));
            Line(vh, P(c, o, o), P(c, o, o - l));
            // BL
            Line(vh, P(c, -o, -o + l), P(c, -o, -o));
            Line(vh, P(c, -o, -o), P(c, -o + l, -o));
            // BR
            Line(vh, P(c, o - l, -o), P(c, o, -o));
            Line(vh, P(c, o, -o), P(c, o, -o + l));
        }

        private void Search(VertexHelper vh, Vector2 c)
        {
            Ring(vh, P(c, -0.15f, 0.15f), _half * 0.48f);
            Line(vh, P(c, 0.22f, -0.22f), P(c, 0.68f, -0.68f), _stroke * 1.25f);
        }

        private void Info(VertexHelper vh, Vector2 c)
        {
            // A heavy lowercase "i": a clearly DETACHED dot over a thick lower-half stem, no
            // enclosing ring. The first draft ringed it, and at 14-16px the dot fused with the
            // circle's top arc — reading as a POWER symbol (play-test round 2). The (i) button
            // already draws its own rounded outline, so the ring was redundant enclosure; the
            // bare dot+stem stays unmistakable at this size.
            // Round 3 (FlorpyDorp: "more like an I and less stubby"): the heavy dot over a short
            // half-height stem read stubby — and, next to the kebab, like the same glyph. Now the
            // classic information serif "i": a small detached dot, a TALL slimmer stem filling
            // most of the icon height, a short top-left flag serif and a bottom foot serif.
            // At 15px (_half ~6.3px) the solid gap between the dot and the flag is ~1.5px, which
            // survives the 0.9px AA fringes.
            float stem = _stroke * 1.05f;
            float serif = _stroke * 0.9f;
            Dot(vh, P(c, 0.02f, 0.74f), Mathf.Max(1.1f, _half * 0.17f));
            Line(vh, P(c, 0f, 0.24f), P(c, 0f, -0.82f), stem);                  // stem
            Line(vh, P(c, -0.32f, 0.15f), P(c, 0.02f, 0.24f), serif);           // top-left flag
            Line(vh, P(c, -0.34f, -0.82f), P(c, 0.34f, -0.82f), serif);         // foot
        }
    }

    /// <summary>Factory + button helpers for the drawn icons.</summary>
    public static class UiaIcons
    {
        /// <summary>Attach a drawn icon as a centred child of <paramref name="parent"/> at
        /// <paramref name="size"/> px square. Non-raycast (it decorates; the host handles clicks).
        /// Colour should come from <see cref="UiaTheme"/> at the call site so a theme restyle
        /// (which rebuilds the menu) re-tints it like every other widget.</summary>
        public static UiaIconGraphic Attach(Transform parent, UiaIcon kind, float size, Color color)
        {
            var go = UiaUi.Go("icon", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
            var g = go.AddComponent<UiaIconGraphic>();
            g.raycastTarget = false;
            g.color = color;
            g.Configure(kind);
            return g;
        }

        /// <summary>As <see cref="Attach(Transform,UiaIcon,float,Color)"/> but anchored inside the
        /// host rect at a fractional position (0..1 in x/y) instead of the centre — for an icon
        /// riding one end of a wider control (a search box's magnifier, a disclosure chevron).</summary>
        public static UiaIconGraphic AttachAt(Transform parent, UiaIcon kind, float size, Color color,
            float anchorX, float anchorY, float offsetX = 0f, float offsetY = 0f)
        {
            var g = Attach(parent, kind, size, color);
            var rt = (RectTransform)g.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, anchorY);
            rt.pivot = new Vector2(anchorX, anchorY);
            rt.anchoredPosition = new Vector2(offsetX, offsetY);
            return g;
        }

        /// <summary>The drawn-icon sibling of <see cref="UiaControls.SetButtonIcon"/>: swap a
        /// button's text label for a drawn icon. The label is emptied, not removed, so the
        /// button's repaint path stays untouched. Default tint is the kit text colour.</summary>
        public static UiaIconGraphic SetButtonIcon(UiaControls.UiaButton btn, UiaIcon kind, float size)
            => SetButtonIcon(btn, kind, size, UiaTheme.Text);

        public static UiaIconGraphic SetButtonIcon(UiaControls.UiaButton btn, UiaIcon kind, float size, Color color)
        {
            if (btn == null) return null;
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "";
            return Attach(btn.transform, kind, size, color);
        }
    }
}
