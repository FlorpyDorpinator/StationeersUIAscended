using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// GROUP SCALING for the HUD-size slider (GitHub issue #4; FlorpyDorp 2026-09-27: "is it
    /// possible when this gets adjusted they group and scale together? And they will adjust their
    /// position to not bleed off the screen? We need a cohesive fix").
    ///
    /// <para>Every element is placed as anchor + offset x scale, so elements that share an anchor
    /// already scale as one rigid block about it. What drifted was everything else: the top bar's
    /// pieces are pinned left / centre / right and slid into each other, and centre-anchored
    /// decorations and readouts drifted away from the corner stacks they sit beside. This pass
    /// runs once per relayout (never per frame):</para>
    /// <list type="number">
    /// <item><b>Measure at the DESIGN size</b>: the slider value the layout was authored at
    /// (<see cref="HudDocument.DesignScale"/>; the shipped themes are tuned at about 1.21). At that
    /// size the layout is exactly as authored; grouping only acts when the slider moves away from
    /// it, by the ratio slider / design size.</item>
    /// <item><b>Find groups</b>: elements whose rects touch or overlap join one group. A BACKDROP
    /// (wider or taller than half the screen, like the top bar) takes in only elements that sit
    /// mostly inside it, and only if it is a surface: a stroke (the bottom arc) takes in nothing,
    /// so a long decoration never glues two unrelated corners together.</item>
    /// <item><b>Pick a pivot</b> per group: the screen corner, edge midpoint or centre nearest the
    /// group (a group centred in an outer third of the screen snaps to that edge). Every group on
    /// the same pivot then merges into one piece with one size, so things near each other keep
    /// their spacing exactly (a bar floating under the top HUD stays under it).</item>
    /// <item><b>Scale</b> each piece about its pivot, then <b>cap</b> it: a piece stops growing where
    /// any member would cross the screen edge (minus a small margin; a decorative stroke may run
    /// past it), and two pieces stop where their content would first overlap (backdrops don't count
    /// there, only content). Shrinking is never capped.</item>
    /// </list>
    ///
    /// <para>While the F9 editor is open the group ASSIGNMENT is frozen (sizes still recompute, so
    /// the F9 size slider previews live): an edit can't make an element hop groups and jump on
    /// release. Elements not drawn in the current tier keep their last transform, so a power
    /// transition animates them out where they were. The result is written to each view as
    /// <see cref="HudElementView.GroupRatio"/> / <see cref="HudElementView.GroupOffset"/>; the
    /// document is never changed except for the one-time <see cref="HudDocument.DesignScale"/>
    /// stamp.</para>
    /// </summary>
    internal static class HudGroupScale
    {
        /// <summary>Reference px: rects at most this far apart count as touching.</summary>
        private const float TouchGapRef = 12f;
        /// <summary>Wider or taller than this share of the screen makes an element a backdrop.</summary>
        private const float BackdropFrac = 0.5f;
        /// <summary>A backdrop takes in only elements at least this much inside it.</summary>
        private const float InsideFrac = 0.7f;
        /// <summary>Reference px kept clear of the screen edge by the fit cap.</summary>
        private const float EdgeMarginRef = 4f;
        /// <summary>A group whose centre is within this share of the screen from the middle pivots on
        /// the middle line; beyond it, on that edge (so the screen splits into thirds).</summary>
        private const float MiddleBand = 1f / 6f;

        private sealed class Item
        {
            public HudElementView View;
            public Vector2 Anchor;
            public Rect R;          // designed rect (at the design size), canvas px
            public bool PctW, PctH; // screen-percent axes keep their size and only move
            public bool Backdrop;
            // A stroke (Polyline): its rect is mostly empty space, so it never takes elements in,
            // and as a thin decoration it may run past the screen edge rather than cap its group.
            public bool Line;
        }

        private sealed class Group
        {
            public readonly List<Item> Items = new List<Item>();
            public Rect Box;
            public Vector2 Pivot;
            public float U;         // this piece's scale relative to the design size (capped)
        }

        private static readonly List<Item> _items = new List<Item>();
        private static readonly List<Group> _groups = new List<Group>();
        private static readonly Dictionary<int, Group> _byRoot = new Dictionary<int, Group>();
        private static int[] _parent = new int[0];

        /// <summary>Hot-reload teardown: drop the scratch that could hold view references.</summary>
        internal static void Reset()
        {
            ClearScratch();
            _parent = new int[0];
        }

        private static void ClearScratch()
        {
            _items.Clear();
            _groups.Clear();
            _byRoot.Clear();
        }

        /// <summary>The slider value <paramref name="doc"/> was designed at. When unset, it's stamped
        /// (in memory; saved with the profile's next save) from the theme's own cfg:HudScale, else the
        /// live slider — so the size a layout loads at is, by definition, its design size.</summary>
        internal static float DesignScaleOf(HudDocument doc)
        {
            float live = HudConfig.HudScale != null ? HudConfig.HudScale.Value : 1f;
            if (doc == null) return live;
            if (Valid(doc.DesignScale)) return doc.DesignScale;
            float d = live;
            if (doc.Theme != null)
                for (int i = 0; i < doc.Theme.Count; i++)
                {
                    var e = doc.Theme[i];
                    float v;
                    if (e != null && e.K == "cfg:HudScale"
                        && float.TryParse(e.V, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && Valid(v))
                    { d = v; break; }
                }
            if (!Valid(d)) d = 1f;
            doc.DesignScale = d;
            return d;
        }

        private static bool Valid(float s) => s >= 0.3f && s <= 3f; // also false for NaN

        /// <summary>Recompute every element's group ratio and offset for <paramref name="globalScale"/>
        /// (HudConfig.EffectiveHudScale). Called by HudSystem.RelayoutAll before any panel lays out.
        /// <paramref name="freeze"/> (F9 open) re-uses each element's last group assignment.</summary>
        internal static void Apply(List<HudPanel> panels, float globalScale, HudDocument doc, bool freeze)
        {
            ClearScratch();
            float res = HudConfig.EffectiveResolutionScale();
            float design = DesignScaleOf(doc);
            float designRes = res * design;
            float sw = Screen.width, sh = Screen.height;
            float u = designRes > 0.0001f ? globalScale / designRes : 1f; // slider / design size
            if (float.IsNaN(u) || float.IsInfinity(u) || sw <= 0f || sh <= 0f || designRes <= 0.0001f)
            {
                SetAllIdentity(panels);
                return;
            }

            // 1. The elements drawn in the current tier, at their designed geometry. Views NOT drawn
            //    in this tier keep their last transform (a power transition animates them out in
            //    place instead of snapping them to ungrouped positions mid-collapse).
            for (int i = 0; i < panels.Count; i++)
            {
                var v = panels[i] as HudElementView;
                if (v == null || v.Def == null || !v.VisibleAt(HudElementView.LayoutTier)) continue;
                v.GroupRatio = 1f;
                v.GroupOffset = Vector2.zero;
                var it = new Item { View = v };
                v.DesignGeometry(designRes, out it.Anchor, out it.R, out it.PctW, out it.PctH);
                if (!Finite(it.R) || it.R.width <= 0f || it.R.height <= 0f)
                {
                    if (!freeze) v.GroupAssigned = false; // a NaN rect would otherwise 'touch' everything
                    continue;
                }
                it.Backdrop = it.R.width > sw * BackdropFrac || it.R.height > sh * BackdropFrac;
                it.Line = v.Def.Type == HudElementType.Polyline;
                _items.Add(it);
            }
            int n = _items.Count;
            if (n == 0) return;

            // 2. Groups: found fresh, or (F9 open) re-used from each view's frozen assignment.
            if (freeze) GroupsFromAssignments();
            else FindGroups(designRes, sw, sh);

            // At the design size every layout is exactly as authored.
            if (Mathf.Abs(u - 1f) < 0.0005f) { ClearScratch(); return; }

            // 3. Fit: a growing piece stops where a member would cross the screen edge.
            float hw = sw * 0.5f, hh = sh * 0.5f, margin = EdgeMarginRef * designRes;
            for (int i = 0; i < _groups.Count; i++)
            {
                var g = _groups[i];
                if (u <= 1f) { g.U = u; continue; }
                float cap = u;
                for (int k = 0; k < g.Items.Count; k++)
                {
                    var it = g.Items[k];
                    if (it.Line) continue; // a decorative stroke may run past the edge
                    CapAxis(it.R.xMin, it.R.xMax, it.PctW, g.Pivot.x, -hw + margin, hw - margin, ref cap);
                    CapAxis(it.R.yMin, it.R.yMax, it.PctH, g.Pivot.y, -hh + margin, hh - margin, ref cap);
                }
                g.U = Mathf.Clamp(cap, 1f, u);
            }

            // 4. Pieces apart as designed stay apart: a colliding pair is lowered together to where
            //    it clears (a few passes, since lowering one pair can't create a new collision).
            if (u > 1f)
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    bool changed = false;
                    for (int a = 0; a < _groups.Count; a++)
                        for (int b = a + 1; b < _groups.Count; b++)
                        {
                            Group ga = _groups[a], gb = _groups[b];
                            if (Clear(ga, ga.U, gb, gb.U)) continue;
                            float lo = 1f, hi = Mathf.Max(ga.U, gb.U);
                            for (int step = 0; step < 16; step++)
                            {
                                float mid = (lo + hi) * 0.5f;
                                if (Clear(ga, Mathf.Min(ga.U, mid), gb, Mathf.Min(gb.U, mid))) lo = mid;
                                else hi = mid;
                            }
                            ga.U = Mathf.Min(ga.U, lo);
                            gb.U = Mathf.Min(gb.U, lo);
                            changed = true;
                        }
                    if (!changed) break;
                }
            }

            // 5. Each view's share: its scale relative to the slider, and the shift from scaling
            //    about its own anchor to scaling about its piece's pivot
            //    (centre = pivot + (designed centre - pivot) x piece scale).
            for (int i = 0; i < _groups.Count; i++)
            {
                var g = _groups[i];
                for (int k = 0; k < g.Items.Count; k++)
                {
                    var it = g.Items[k];
                    it.View.GroupRatio = g.U / u;
                    it.View.GroupOffset = (g.Pivot - it.Anchor) * (1f - g.U);
                }
            }
            ClearScratch();
        }

        private static void SetAllIdentity(List<HudPanel> panels)
        {
            for (int i = 0; i < panels.Count; i++)
            {
                var v = panels[i] as HudElementView;
                if (v == null) continue;
                v.GroupRatio = 1f;
                v.GroupOffset = Vector2.zero;
            }
            ClearScratch();
        }

        /// <summary>Union-find over touching pairs, one pivot per group, then every group on the same
        /// pivot merged into one piece. Stores each view's pivot as its (freezable) assignment.</summary>
        private static void FindGroups(float designRes, float sw, float sh)
        {
            int n = _items.Count;
            if (_parent.Length < n) _parent = new int[n];
            for (int i = 0; i < n; i++) _parent[i] = i;
            // A backdrop only takes in what sits inside it, and only a SURFACE (a box or shape) does:
            // a stroke's rect is mostly empty, and the bottom arc's rect happens to enclose the
            // jetpack box (play-test 2026-09-27).
            float gap = TouchGapRef * designRes;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    Item a = _items[i], b = _items[j];
                    bool join;
                    if (!a.Backdrop && !b.Backdrop) join = Near(a.R, b.R, gap);
                    else if (a.Backdrop && b.Backdrop)
                    {
                        Item small = Area(a.R) <= Area(b.R) ? a : b, big = small == a ? b : a;
                        join = !big.Line && FracInside(small.R, big.R) >= InsideFrac;
                    }
                    else
                    {
                        Item back = a.Backdrop ? a : b, inner = a.Backdrop ? b : a;
                        join = !back.Line && FracInside(inner.R, back.R) >= InsideFrac;
                    }
                    if (join) Union(i, j);
                }

            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                Group g;
                if (!_byRoot.TryGetValue(root, out g)) { g = new Group(); _byRoot[root] = g; _groups.Add(g); }
                g.Items.Add(_items[i]);
            }
            for (int i = 0; i < _groups.Count; i++)
            {
                _groups[i].Box = BoxOf(_groups[i]);
                _groups[i].Pivot = PivotFor(_groups[i].Box, sw, sh);
            }
            // Every group on the same pivot scales as ONE piece with one size, so things near each
            // other keep their spacing exactly even when they don't touch (a bar floating under the
            // top HUD used to drift away from it).
            MergeByPivot();
            for (int i = 0; i < _groups.Count; i++)
                for (int k = 0; k < _groups[i].Items.Count; k++)
                {
                    var v = _groups[i].Items[k].View;
                    v.GroupPivot = _groups[i].Pivot;
                    v.GroupAssigned = true;
                }
        }

        /// <summary>F9 open: one piece per frozen pivot. Unassigned views (created in the editor)
        /// stay at identity until the editor closes and a full pass groups them.</summary>
        private static void GroupsFromAssignments()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (!it.View.GroupAssigned) continue;
                Group g = null;
                for (int k = 0; k < _groups.Count; k++)
                    if (_groups[k].Pivot == it.View.GroupPivot) { g = _groups[k]; break; }
                if (g == null) { g = new Group { Pivot = it.View.GroupPivot }; _groups.Add(g); }
                g.Items.Add(it);
            }
            for (int i = 0; i < _groups.Count; i++) _groups[i].Box = BoxOf(_groups[i]);
        }

        private static void MergeByPivot()
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int a = 0; a < _groups.Count && !merged; a++)
                    for (int b = a + 1; b < _groups.Count && !merged; b++)
                    {
                        Group ga = _groups[a], gb = _groups[b];
                        if (ga.Pivot != gb.Pivot) continue;
                        ga.Items.AddRange(gb.Items);
                        ga.Box = BoxOf(ga);
                        _groups.RemoveAt(b);
                        merged = true;
                    }
            }
        }

        /// <summary>The largest piece scale (≤ <paramref name="cap"/>) at which this member's edges on
        /// one axis stay inside [lo, hi]. Only edges already inside at the design size are held (a
        /// member the designer placed partly off screen keeps that). Fixed axes scale about the
        /// pivot; screen-percent axes keep their size and only their centre moves.</summary>
        private static void CapAxis(float min0, float max0, bool pct, float pivot, float lo, float hi, ref float cap)
        {
            if (pct)
            {
                float half = (max0 - min0) * 0.5f, dc = (min0 + max0) * 0.5f - pivot;
                if (dc < 0f && min0 >= lo) cap = Mathf.Min(cap, (pivot - lo - half) / -dc);
                if (dc > 0f && max0 <= hi) cap = Mathf.Min(cap, (hi - half - pivot) / dc);
                return;
            }
            float dMin = min0 - pivot, dMax = max0 - pivot;
            if (dMin < 0f && min0 >= lo) cap = Mathf.Min(cap, (pivot - lo) / -dMin);
            if (dMax > 0f && max0 <= hi) cap = Mathf.Min(cap, (hi - pivot) / dMax);
        }

        /// <summary>True when no content of one piece overlaps content of the other at these scales.
        /// Backdrops are skipped, as are pairs that already overlap as designed.</summary>
        private static bool Clear(Group ga, float ua, Group gb, float ub)
        {
            for (int i = 0; i < ga.Items.Count; i++)
            {
                var a = ga.Items[i];
                if (a.Backdrop) continue;
                for (int j = 0; j < gb.Items.Count; j++)
                {
                    var b = gb.Items[j];
                    if (b.Backdrop || a.R.Overlaps(b.R)) continue;
                    if (Scaled(a, ga.Pivot, ua).Overlaps(Scaled(b, gb.Pivot, ub))) return false;
                }
            }
            return true;
        }

        /// <summary>A member's rect at piece scale <paramref name="t"/> — the same maths the renderer
        /// applies through GroupRatio/GroupOffset.</summary>
        private static Rect Scaled(Item it, Vector2 pivot, float t)
        {
            float cx = pivot.x + (it.R.center.x - pivot.x) * t;
            float cy = pivot.y + (it.R.center.y - pivot.y) * t;
            float w = it.PctW ? it.R.width : it.R.width * t;
            float h = it.PctH ? it.R.height : it.R.height * t;
            return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        }

        private static Vector2 PivotFor(Rect box, float sw, float sh)
        {
            Vector2 c = box.center;
            float px = Mathf.Abs(c.x) <= sw * MiddleBand ? 0f : Mathf.Sign(c.x) * sw * 0.5f;
            float py = Mathf.Abs(c.y) <= sh * MiddleBand ? 0f : Mathf.Sign(c.y) * sh * 0.5f;
            return new Vector2(px, py);
        }

        private static Rect BoxOf(Group g)
        {
            Rect r = g.Items[0].R;
            float xMin = r.xMin, yMin = r.yMin, xMax = r.xMax, yMax = r.yMax;
            for (int i = 1; i < g.Items.Count; i++)
            {
                r = g.Items[i].R;
                xMin = Mathf.Min(xMin, r.xMin); yMin = Mathf.Min(yMin, r.yMin);
                xMax = Mathf.Max(xMax, r.xMax); yMax = Mathf.Max(yMax, r.yMax);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool Finite(Rect r)
            => !(float.IsNaN(r.x) || float.IsNaN(r.y) || float.IsNaN(r.width) || float.IsNaN(r.height)
                 || float.IsInfinity(r.x) || float.IsInfinity(r.y) || float.IsInfinity(r.width) || float.IsInfinity(r.height));

        /// <summary>Rects at most <paramref name="gap"/> apart on both axes (overlap counts).</summary>
        private static bool Near(Rect a, Rect b, float gap)
        {
            float xGap = Mathf.Max(a.xMin, b.xMin) - Mathf.Min(a.xMax, b.xMax);
            float yGap = Mathf.Max(a.yMin, b.yMin) - Mathf.Min(a.yMax, b.yMax);
            return xGap <= gap && yGap <= gap;
        }

        /// <summary>The share of <paramref name="inner"/>'s area that lies inside <paramref name="outer"/>.</summary>
        private static float FracInside(Rect inner, Rect outer)
        {
            float a = Area(inner);
            if (a <= 0f) return 0f;
            float w = Mathf.Min(inner.xMax, outer.xMax) - Mathf.Max(inner.xMin, outer.xMin);
            float h = Mathf.Min(inner.yMax, outer.yMax) - Mathf.Max(inner.yMin, outer.yMin);
            return w <= 0f || h <= 0f ? 0f : (w * h) / a;
        }

        private static float Area(Rect r) => r.width * r.height;

        private static int Find(int i)
        {
            while (_parent[i] != i) { _parent[i] = _parent[_parent[i]]; i = _parent[i]; }
            return i;
        }

        private static void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[rb] = ra;
        }
    }
}
