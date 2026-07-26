using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>The silhouette the portrait hologram is clipped to. Round is the shipped default;
    /// the four angular shapes were added 2026-07-24 so the visor portrait can be framed as a
    /// triangle / trapezoid / square / rectangle instead of only a disc.</summary>
    internal enum PortraitShape { Round = 0, Triangle = 1, Trapezoid = 2, Square = 3, Rectangle = 4 }

    /// <summary>Geometry for the angular portrait shapes: CCW corner points, centred on the
    /// element origin (both the mask stencil and the ring are centre-anchored, so a shared
    /// origin-centred contour drives both). Round is handled separately (a fan, not a contour),
    /// so <see cref="BuildPolygon"/> returns false for it.</summary>
    internal static class PortraitShapes
    {
        /// <param name="w">Element width (already scaled).</param>
        /// <param name="h">Element height (already scaled).</param>
        /// <param name="top">Trapezoid TOP edge width as a fraction of <paramref name="w"/> (0..1).</param>
        /// <param name="bot">Trapezoid BOTTOM edge width as a fraction of <paramref name="w"/> (0..1).</param>
        /// <returns>false for <see cref="PortraitShape.Round"/> (no polygon); true otherwise, with
        /// the corner points written to <paramref name="pts"/>.</returns>
        public static bool BuildPolygon(PortraitShape shape, float w, float h, float top, float bot, List<Vector2> pts)
        {
            pts.Clear();
            float hw = w * 0.5f, hh = h * 0.5f;
            switch (shape)
            {
                case PortraitShape.Rectangle:
                    Quad(pts, hw, hh);
                    return true;
                case PortraitShape.Square:
                {
                    float s = Mathf.Min(w, h) * 0.5f;
                    Quad(pts, s, s);
                    return true;
                }
                case PortraitShape.Triangle:
                    // Isosceles: apex at top-centre, base along the bottom edge.
                    pts.Add(new Vector2(-hw, -hh));   // bottom-left
                    pts.Add(new Vector2(hw, -hh));    // bottom-right
                    pts.Add(new Vector2(0f, hh));     // top apex
                    return true;
                case PortraitShape.Trapezoid:
                {
                    float tw = Mathf.Clamp01(top) * hw;   // half TOP width
                    float bw = Mathf.Clamp01(bot) * hw;   // half BOTTOM width
                    pts.Add(new Vector2(-bw, -hh));   // bottom-left
                    pts.Add(new Vector2(bw, -hh));    // bottom-right
                    pts.Add(new Vector2(tw, hh));     // top-right
                    pts.Add(new Vector2(-tw, hh));    // top-left
                    return true;
                }
                default:
                    return false; // Round — a fan, not a contour
            }
        }

        private static void Quad(List<Vector2> pts, float hw, float hh)
        {
            pts.Add(new Vector2(-hw, -hh));   // bottom-left  (CCW)
            pts.Add(new Vector2(hw, -hh));    // bottom-right
            pts.Add(new Vector2(hw, hh));     // top-right
            pts.Add(new Vector2(-hw, hh));    // top-left
        }
    }

    /// <summary>
    /// The portrait's mask STENCIL — an opaque filled silhouette written into the stencil buffer
    /// by the sibling <see cref="UnityEngine.UI.Mask"/> (showMaskGraphic = false, so this graphic
    /// is never itself visible; only its shape clips the hologram + scanlines parented under it).
    ///
    /// It replaces the plain <see cref="CircleGraphic"/> that used to be the mask: because the
    /// stencil is invisible and the visible RING is drawn on top of the clipped edge, a hard-edged
    /// fan/polygon here is indistinguishable from the old soft disc — but this one can also emit
    /// the four angular silhouettes (<see cref="PortraitShape"/>). The visible round ring stays a
    /// real <see cref="CircleGraphic"/>, so the default round portrait is byte-for-byte unchanged.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class PortraitMaskGraphic : MaskableGraphic
    {
        private PortraitShape _shape = PortraitShape.Round;
        private float _radius = 50f;                 // Round only
        private float _w, _h, _top = 0.6f, _bot = 1f; // angular only

        // Single-threaded UGUI rebuild — a static scratch list allocates nothing per rebuild.
        private static readonly List<Vector2> _pts = new List<Vector2>(8);

        /// <summary>Drive the stencil as a circle of the given radius.</summary>
        public void SetRound(float radius)
        {
            if (_shape == PortraitShape.Round && Mathf.Approximately(_radius, radius)) return;
            _shape = PortraitShape.Round;
            _radius = radius;
            SetVerticesDirty();
        }

        /// <summary>Drive the stencil as one of the angular silhouettes.</summary>
        public void SetPolygon(PortraitShape shape, float w, float h, float top, float bot)
        {
            if (_shape == shape && Mathf.Approximately(_w, w) && Mathf.Approximately(_h, h)
                && Mathf.Approximately(_top, top) && Mathf.Approximately(_bot, bot)) return;
            _shape = shape; _w = w; _h = h; _top = top; _bot = bot;
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_shape == PortraitShape.Round)
            {
                if (_radius <= 0f) return;
                int seg = Mathf.Clamp(Mathf.CeilToInt(_radius * 1.2f), 48, 256);
                vh.AddVert(Vector2.zero, color, Vector2.zero);
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg * Mathf.PI * 2f;
                    vh.AddVert(new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * _radius, color, Vector2.zero);
                }
                for (int i = 0; i < seg; i++)
                    vh.AddTriangle(0, 1 + i, 2 + i);
                return;
            }

            if (!PortraitShapes.BuildPolygon(_shape, _w, _h, _top, _bot, _pts) || _pts.Count < 3) return;
            // Fan from the origin: every angular shape here is convex and contains its centre, so a
            // centroid fan triangulates it without ear-clipping.
            vh.AddVert(Vector2.zero, color, Vector2.zero);
            for (int i = 0; i < _pts.Count; i++)
                vh.AddVert(_pts[i], color, Vector2.zero);
            for (int i = 0; i < _pts.Count; i++)
                vh.AddTriangle(0, 1 + i, 1 + (i + 1) % _pts.Count);
        }
    }
}
