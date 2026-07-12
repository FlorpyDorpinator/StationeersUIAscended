using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>Which built-in vector glyph a <see cref="HudIconGraphic"/> draws.
    /// Order is load-bearing: <see cref="HudGlyphs"/> indexes its table by <c>(int)</c>
    /// of this enum, so new kinds must be appended, never inserted.</summary>
    public enum HudIconKind
    {
        None, Flame, Droplet, Toilet, Bolt, Burger, Chair, Helmet, Jetpack, Sun,
        Thermometer, Lungs, Heart, Gauge, HandLeft, HandRight, Drop, Throw, Swap,
        Backpack, Glasses, Belt, Suit, Uniform, Skull, Compass
    }

    /// <summary>
    /// Hand-authored, resolution-independent line icons for the HUD. Each glyph is one or
    /// more open polylines (a path whose first and last point coincide reads as closed) plus
    /// optional filled circles, all authored in a normalized [-1,+1] square with y pointing
    /// up. <see cref="Emit"/> transforms that into pixel-space STROKED quads with mitred
    /// (bevel-capped) joins and an alpha-0 fringe — the same baked-AA trick PanelGraphic uses,
    /// because the overlay canvas has no MSAA and the fringe IS the anti-aliasing.
    ///
    /// The emitter is deliberately self-contained (its own stroker, no dependency on the
    /// other HUD graphics) so a single icon can be baked anywhere a VertexHelper exists.
    /// </summary>
    public static class HudGlyphs
    {
        // Corners at which a mitre is clamped into a bevel — beyond this the spike would
        // shoot off a thin icon and read as a defect. 4x half-stroke is generous enough to
        // keep right-angle corners sharp while taming near-reversals.
        private const float MiterLimit = 4f;
        private const int DiscSegments = 20;

        private struct Glyph
        {
            public float[][] Paths;   // each inner array: x0,y0,x1,y1,... (open; closed if ends meet)
            public float[] Circles;   // filled discs as cx,cy,r triplets
        }

        // ---- Public API -------------------------------------------------------------

        /// <summary>Bake <paramref name="kind"/> into <paramref name="vh"/>, centred on
        /// <paramref name="center"/> and filling a <paramref name="size"/>-px square. The
        /// normalized [-1,+1] authoring square maps to that box; <paramref name="stroke"/> is
        /// the line width in px and <paramref name="feather"/> the alpha-fringe width. No
        /// allocations: all scratch is static (UGUI mesh rebuilds are single-threaded).</summary>
        public static void Emit(VertexHelper vh, HudIconKind kind, Vector2 center,
            float size, float stroke, Color color, float feather)
        {
            int idx = (int)kind;
            if (vh == null || idx < 0 || idx >= Table.Length) return;
            Glyph g = Table[idx];
            if (g.Paths == null && g.Circles == null) return;

            float half = size * 0.5f;
            float hw = Mathf.Max(0.1f, stroke * 0.5f);
            float f = Mathf.Max(0f, feather);

            if (g.Paths != null)
                for (int p = 0; p < g.Paths.Length; p++)
                    StrokePath(vh, g.Paths[p], center, half, hw, f, color);

            if (g.Circles != null)
                for (int c = 0; c + 3 <= g.Circles.Length; c += 3)
                {
                    var cc = new Vector2(center.x + g.Circles[c] * half,
                                         center.y + g.Circles[c + 1] * half);
                    FillDisc(vh, cc, g.Circles[c + 2] * half, f, color);
                }
        }

        // ---- Stroker ----------------------------------------------------------------

        // Scratch reused across every path of every glyph. Sized well past the ~13-point
        // ceiling of the authored glyphs so no path can overrun them.
        private static readonly List<Vector2> _pts = new List<Vector2>(32);
        private static readonly Vector2[] _jn = new Vector2[64]; // per-vertex join normal
        private static readonly float[] _js = new float[64];      // per-vertex mitre scale

        private static void StrokePath(VertexHelper vh, float[] path, Vector2 center,
            float half, float hw, float f, Color color)
        {
            if (path == null || path.Length < 4) return; // need at least two points

            _pts.Clear();
            for (int i = 0; i + 1 < path.Length; i += 2)
            {
                var pt = new Vector2(center.x + path[i] * half, center.y + path[i + 1] * half);
                if (_pts.Count > 0 && (pt - _pts[_pts.Count - 1]).sqrMagnitude < 1e-4f)
                    continue; // drop coincident consecutive points
                _pts.Add(pt);
            }
            int n = _pts.Count;
            if (n < 2) return;

            // A repeated first==last point marks a closed contour; fold it away and wrap joins.
            bool closed = false;
            if (n >= 3 && (_pts[0] - _pts[n - 1]).sqrMagnitude < 1e-3f)
            {
                _pts.RemoveAt(n - 1);
                n--;
                closed = true;
            }
            if (n > _jn.Length) return; // guard the scratch (authored glyphs never hit this)

            // Join normal + mitre scale at each vertex.
            for (int i = 0; i < n; i++)
            {
                bool hasIn = closed || i > 0;
                bool hasOut = closed || i < n - 1;
                Vector2 nIn = Vector2.zero, nOut = Vector2.zero;
                if (hasIn)
                {
                    Vector2 d = (_pts[i] - _pts[(i - 1 + n) % n]).normalized;
                    nIn = new Vector2(-d.y, d.x);
                }
                if (hasOut)
                {
                    Vector2 d = (_pts[(i + 1) % n] - _pts[i]).normalized;
                    nOut = new Vector2(-d.y, d.x);
                }

                Vector2 mid;
                float scale;
                if (hasIn && hasOut)
                {
                    mid = nIn + nOut;
                    if (mid.sqrMagnitude < 1e-6f)
                    {
                        mid = nOut; // 180° reversal — fall back to a flat cap
                        scale = 1f;
                    }
                    else
                    {
                        mid = mid.normalized;
                        float dot = Mathf.Max(Vector2.Dot(mid, nOut), 1f / MiterLimit);
                        scale = 1f / dot;
                    }
                }
                else
                {
                    mid = hasOut ? nOut : nIn;
                    scale = 1f;
                }
                _jn[i] = mid;
                _js[i] = scale;
            }

            Color fade = color; fade.a = 0f;
            int start = vh.currentVertCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 nrm = _jn[i];
                float sc = _js[i];
                Vector2 core = nrm * (hw * sc);
                Vector2 fr = nrm * ((hw + f) * sc);
                Vector2 pt = _pts[i];
                vh.AddVert(pt + fr, fade, Vector2.zero);    // 0 fringe (left)
                vh.AddVert(pt + core, color, Vector2.zero); // 1 core   (left)
                vh.AddVert(pt - core, color, Vector2.zero); // 2 core   (right)
                vh.AddVert(pt - fr, fade, Vector2.zero);    // 3 fringe (right)
            }

            int segs = closed ? n : n - 1;
            for (int s = 0; s < segs; s++)
            {
                int a = start + s * 4;
                int b = start + ((s + 1) % n) * 4;
                for (int k = 0; k < 3; k++)
                {
                    vh.AddTriangle(a + k, a + k + 1, b + k + 1);
                    vh.AddTriangle(a + k, b + k + 1, b + k);
                }
            }
        }

        private static void FillDisc(VertexHelper vh, Vector2 center, float r, float f, Color color)
        {
            if (r <= 0.01f) return;
            Color fade = color; fade.a = 0f;
            int start = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero); // fan hub
            for (int i = 0; i <= DiscSegments; i++)
            {
                float a = i / (float)DiscSegments * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(center + d * r, color, Vector2.zero);       // core ring
                vh.AddVert(center + d * (r + f), fade, Vector2.zero);  // fringe ring
            }
            for (int i = 0; i < DiscSegments; i++)
            {
                int c0 = start + 1 + i * 2, c1 = start + 1 + (i + 1) * 2;
                int f0 = start + 2 + i * 2, f1 = start + 2 + (i + 1) * 2;
                vh.AddTriangle(start, c0, c1); // solid fan
                vh.AddTriangle(c0, f0, f1);    // fringe band
                vh.AddTriangle(c0, f1, c1);
            }
        }

        // ---- Glyph table ------------------------------------------------------------
        // Authored thin-line minimalist, y up, in [-1,+1]. Kept small on purpose: these must
        // stay legible at 16px, so each glyph is a few strokes, not a faithful drawing.

        private static readonly Glyph[] Table = BuildTable();

        private static Glyph G(float[][] paths, float[] circles)
        {
            Glyph g; g.Paths = paths; g.Circles = circles; return g;
        }

        private static Glyph[] BuildTable()
        {
            var t = new Glyph[26];

            t[(int)HudIconKind.None] = G(null, null);

            // Flame: teardrop outline + inner tongue.
            t[(int)HudIconKind.Flame] = G(new[]
            {
                new[] { 0f,-0.85f, -0.45f,-0.55f, -0.55f,-0.05f, -0.30f,0.35f, -0.05f,0.55f,
                        0f,0.9f, 0.25f,0.4f, 0.5f,0f, 0.45f,-0.5f, 0f,-0.85f },
                new[] { 0f,-0.55f, -0.20f,-0.15f, 0f,0.25f, 0.20f,-0.15f, 0f,-0.55f },
            }, null);

            // Droplet: single teardrop.
            t[(int)HudIconKind.Droplet] = G(new[]
            {
                new[] { 0f,0.9f, 0.35f,0.15f, 0.5f,-0.35f, 0.25f,-0.75f,
                        -0.25f,-0.75f, -0.5f,-0.35f, -0.35f,0.15f, 0f,0.9f },
            }, null);

            // Toilet: side profile — tank, bowl/seat, pedestal.
            t[(int)HudIconKind.Toilet] = G(new[]
            {
                new[] { -0.55f,0.9f, -0.2f,0.9f, -0.2f,0.35f, -0.55f,0.35f, -0.55f,0.9f },
                new[] { -0.2f,0.55f, 0.6f,0.5f, 0.45f,0.05f, 0.05f,-0.15f, -0.15f,-0.05f, -0.2f,0.35f },
                new[] { 0.05f,-0.15f, 0f,-0.8f, -0.3f,-0.85f, 0.35f,-0.85f },
            }, null);

            // Bolt: closed zigzag lightning.
            t[(int)HudIconKind.Bolt] = G(new[]
            {
                new[] { 0.15f,0.9f, -0.4f,0.15f, -0.05f,0.15f, -0.15f,-0.9f,
                        0.4f,-0.05f, 0.05f,-0.05f, 0.15f,0.9f },
            }, null);

            // Burger: three horizontal bars.
            t[(int)HudIconKind.Burger] = G(new[]
            {
                new[] { -0.6f,0.45f, 0.6f,0.45f },
                new[] { -0.6f,0f, 0.6f,0f },
                new[] { -0.6f,-0.45f, 0.6f,-0.45f },
            }, null);

            // Chair: side profile — back+seat, two legs.
            t[(int)HudIconKind.Chair] = G(new[]
            {
                new[] { -0.4f,0.85f, -0.4f,-0.05f, 0.5f,-0.05f },
                new[] { 0.4f,-0.05f, 0.4f,-0.85f },
                new[] { -0.35f,-0.05f, -0.35f,-0.85f },
            }, null);

            // Helmet: dome outline + visor window.
            t[(int)HudIconKind.Helmet] = G(new[]
            {
                new[] { -0.6f,-0.1f, -0.6f,0.3f, -0.4f,0.65f, 0f,0.8f, 0.4f,0.65f, 0.6f,0.3f,
                        0.6f,-0.1f, 0.6f,-0.5f, -0.6f,-0.5f, -0.6f,-0.1f },
                new[] { -0.45f,0.2f, 0.45f,0.2f, 0.45f,-0.1f, -0.45f,-0.1f, -0.45f,0.2f },
            }, null);

            // Jetpack: rounded pack, centre seam, two nozzle flames.
            t[(int)HudIconKind.Jetpack] = G(new[]
            {
                new[] { -0.5f,0.7f, 0.5f,0.7f, 0.55f,-0.4f, -0.55f,-0.4f, -0.5f,0.7f },
                new[] { 0f,0.7f, 0f,-0.4f },
                new[] { -0.35f,-0.4f, -0.3f,-0.85f, -0.25f,-0.4f },
                new[] { 0.35f,-0.4f, 0.3f,-0.85f, 0.25f,-0.4f },
            }, null);

            // Sun: filled core + eight rays. (Rays are inherently disjoint, so this glyph
            // knowingly runs past the ~5-path guideline — it is the one honest exception.)
            t[(int)HudIconKind.Sun] = G(new[]
            {
                new[] { 0.55f,0f, 0.9f,0f },
                new[] { 0.39f,0.39f, 0.64f,0.64f },
                new[] { 0f,0.55f, 0f,0.9f },
                new[] { -0.39f,0.39f, -0.64f,0.64f },
                new[] { -0.55f,0f, -0.9f,0f },
                new[] { -0.39f,-0.39f, -0.64f,-0.64f },
                new[] { 0f,-0.55f, 0f,-0.9f },
                new[] { 0.39f,-0.39f, 0.64f,-0.64f },
            }, new[] { 0f,0f,0.38f });

            // Thermometer: bulb + rounded-top tube + side ticks.
            t[(int)HudIconKind.Thermometer] = G(new[]
            {
                new[] { -0.13f,-0.4f, -0.13f,0.7f, 0f,0.85f, 0.13f,0.7f, 0.13f,-0.4f },
                new[] { 0.13f,0.45f, 0.35f,0.45f },
                new[] { 0.13f,0.15f, 0.35f,0.15f },
                new[] { 0.13f,-0.15f, 0.35f,-0.15f },
            }, new[] { 0f,-0.55f,0.3f });

            // Lungs: trachea + two lobes.
            t[(int)HudIconKind.Lungs] = G(new[]
            {
                new[] { 0f,0.9f, 0f,0.1f },
                new[] { -0.08f,0.15f, -0.4f,0.05f, -0.55f,-0.4f, -0.4f,-0.8f,
                        -0.2f,-0.7f, -0.12f,-0.2f, -0.08f,0.15f },
                new[] { 0.08f,0.15f, 0.4f,0.05f, 0.55f,-0.4f, 0.4f,-0.8f,
                        0.2f,-0.7f, 0.12f,-0.2f, 0.08f,0.15f },
            }, null);

            // Heart.
            t[(int)HudIconKind.Heart] = G(new[]
            {
                new[] { 0f,-0.85f, -0.5f,-0.2f, -0.75f,0.25f, -0.55f,0.7f, -0.2f,0.7f, 0f,0.4f,
                        0.2f,0.7f, 0.55f,0.7f, 0.75f,0.25f, 0.5f,-0.2f, 0f,-0.85f },
            }, null);

            // Gauge: dial arc + needle + hub.
            t[(int)HudIconKind.Gauge] = G(new[]
            {
                new[] { -0.8f,0f, -0.69f,0.4f, -0.4f,0.69f, 0f,0.8f, 0.4f,0.69f, 0.69f,0.4f, 0.8f,0f },
                new[] { 0f,-0.1f, -0.3f,0.5f },
            }, new[] { 0f,-0.1f,0.1f });

            // Hands: palm + four finger strokes + a thumb on the outer side. Fingers are
            // separate strokes, so hands sit at six paths — the readable minimum for a hand.
            t[(int)HudIconKind.HandLeft] = G(new[]
            {
                new[] { -0.45f,-0.7f, 0.45f,-0.7f, 0.45f,0.05f, -0.45f,0.05f, -0.45f,-0.7f },
                new[] { -0.32f,0.05f, -0.32f,0.45f },
                new[] { -0.11f,0.05f, -0.11f,0.6f },
                new[] { 0.11f,0.05f, 0.11f,0.6f },
                new[] { 0.32f,0.05f, 0.32f,0.4f },
                new[] { 0.45f,-0.15f, 0.8f,0.05f },
            }, null);

            t[(int)HudIconKind.HandRight] = G(new[]
            {
                new[] { -0.45f,-0.7f, 0.45f,-0.7f, 0.45f,0.05f, -0.45f,0.05f, -0.45f,-0.7f },
                new[] { -0.32f,0.05f, -0.32f,0.4f },
                new[] { -0.11f,0.05f, -0.11f,0.6f },
                new[] { 0.11f,0.05f, 0.11f,0.6f },
                new[] { 0.32f,0.05f, 0.32f,0.45f },
                new[] { -0.45f,-0.15f, -0.8f,0.05f },
            }, null);

            // Drop: down arrow onto a surface line.
            t[(int)HudIconKind.Drop] = G(new[]
            {
                new[] { 0f,0.85f, 0f,-0.4f },
                new[] { -0.35f,-0.1f, 0f,-0.5f, 0.35f,-0.1f },
                new[] { -0.5f,-0.7f, 0.5f,-0.7f },
            }, null);

            // Throw: lobbed trajectory + arrowhead.
            t[(int)HudIconKind.Throw] = G(new[]
            {
                new[] { -0.8f,-0.5f, -0.5f,0.2f, 0f,0.5f, 0.5f,0.3f, 0.8f,-0.3f },
                new[] { 0.9f,0f, 0.8f,-0.3f, 0.5f,-0.25f },
            }, null);

            // Swap: two opposed arrows.
            t[(int)HudIconKind.Swap] = G(new[]
            {
                new[] { -0.6f,0.3f, 0.6f,0.3f },
                new[] { 0.35f,0.5f, 0.6f,0.3f, 0.35f,0.1f },
                new[] { 0.6f,-0.3f, -0.6f,-0.3f },
                new[] { -0.35f,-0.1f, -0.6f,-0.3f, -0.35f,-0.5f },
            }, null);

            // Backpack: body, top handle, front pocket.
            t[(int)HudIconKind.Backpack] = G(new[]
            {
                new[] { -0.5f,0.6f, 0.5f,0.6f, 0.55f,-0.7f, -0.55f,-0.7f, -0.5f,0.6f },
                new[] { -0.15f,0.6f, -0.1f,0.85f, 0.1f,0.85f, 0.15f,0.6f },
                new[] { -0.35f,-0.55f, -0.35f,-0.15f, 0.35f,-0.15f, 0.35f,-0.55f },
            }, null);

            // Glasses: two lenses, bridge, temples.
            t[(int)HudIconKind.Glasses] = G(new[]
            {
                new[] { -0.7f,0.25f, -0.1f,0.25f, -0.1f,-0.25f, -0.7f,-0.25f, -0.7f,0.25f },
                new[] { 0.1f,0.25f, 0.7f,0.25f, 0.7f,-0.25f, 0.1f,-0.25f, 0.1f,0.25f },
                new[] { -0.1f,0.1f, 0.1f,0.1f },
                new[] { -0.7f,0.15f, -0.9f,0.25f },
                new[] { 0.7f,0.15f, 0.9f,0.25f },
            }, null);

            // Belt: strap edges, buckle, prong.
            t[(int)HudIconKind.Belt] = G(new[]
            {
                new[] { -0.85f,0.2f, 0.85f,0.2f },
                new[] { -0.85f,-0.2f, 0.85f,-0.2f },
                new[] { -0.25f,0.3f, 0.25f,0.3f, 0.25f,-0.3f, -0.25f,-0.3f, -0.25f,0.3f },
                new[] { 0f,0f, 0.25f,0f },
            }, null);

            // Suit: torso, helmet dome, chest box, two arms.
            t[(int)HudIconKind.Suit] = G(new[]
            {
                new[] { -0.45f,0.5f, 0.45f,0.5f, 0.5f,-0.8f, -0.5f,-0.8f, -0.45f,0.5f },
                new[] { -0.3f,0.5f, -0.3f,0.7f, 0f,0.85f, 0.3f,0.7f, 0.3f,0.5f },
                new[] { -0.2f,0.1f, 0.2f,0.1f, 0.2f,-0.2f, -0.2f,-0.2f, -0.2f,0.1f },
                new[] { -0.45f,0.35f, -0.7f,-0.1f },
                new[] { 0.45f,0.35f, 0.7f,-0.1f },
            }, null);

            // Uniform: T-shirt outline.
            t[(int)HudIconKind.Uniform] = G(new[]
            {
                new[] { -0.3f,0.6f, -0.55f,0.6f, -0.8f,0.3f, -0.5f,0f, -0.5f,-0.8f,
                        0.5f,-0.8f, 0.5f,0f, 0.8f,0.3f, 0.55f,0.6f, 0.3f,0.6f, 0f,0.35f, -0.3f,0.6f },
            }, null);

            // Skull: cranium+jaw, nose, two teeth; eyes as filled discs.
            t[(int)HudIconKind.Skull] = G(new[]
            {
                new[] { -0.5f,0.1f, -0.55f,0.5f, -0.2f,0.8f, 0.2f,0.8f, 0.55f,0.5f, 0.5f,0.1f,
                        0.35f,-0.15f, 0.35f,-0.4f, -0.35f,-0.4f, -0.35f,-0.15f, -0.5f,0.1f },
                new[] { 0f,0f, -0.08f,-0.2f, 0.08f,-0.2f, 0f,0f },
                new[] { -0.12f,-0.4f, -0.12f,-0.15f },
                new[] { 0.12f,-0.4f, 0.12f,-0.15f },
            }, new[] { -0.22f,0.15f,0.15f, 0.22f,0.15f,0.15f });

            // Compass: bezel ring, needle diamond, centre pin.
            t[(int)HudIconKind.Compass] = G(new[]
            {
                new[] { 0.85f,0f, 0.6f,0.6f, 0f,0.85f, -0.6f,0.6f, -0.85f,0f,
                        -0.6f,-0.6f, 0f,-0.85f, 0.6f,-0.6f, 0.85f,0f },
                new[] { 0f,0.5f, 0.18f,0f, 0f,-0.5f, -0.18f,0f, 0f,0.5f },
            }, new[] { 0f,0f,0.07f });

            return t;
        }
    }
}
