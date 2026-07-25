using System.Collections.Generic;
using StationeersUIMod.Core; // VanillaIcons.DamageColor (StatusUpdates.DamageGradient)
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The injured-body silhouette: a seven-part humanoid figure — head disc, chest and
    /// lower-torso boxes, two arm capsules, two leg capsules — synthesised entirely from the
    /// snapshot's per-region damage ratios. Each part is tinted by VANILLA's own damage-doll
    /// gradient (StatusUpdates.DamageGradient, pulled live) for its region's damage ratio —
    /// green when healthy through to red when hurt, matching the vanilla body indicator — while
    /// a dim ramp stands in before that manager exists (head reads the head ratio, chest the
    /// chest ratio, everything else the whole-body ratio), so a glance tells you where you're hurt.
    ///
    /// The figure is proportional to the element rect (a fixed ~1:2.4 humanoid aspect, roughly
    /// 40% of the rect wide) and re-centres in it, so resizing in the editor scales the whole
    /// silhouette rather than clipping. There are always exactly seven parts, so the layout is
    /// static — only the per-part colours change, pushed dirty-on-change.
    /// </summary>
    internal sealed class BodyDollWidget : HudElementView
    {
        // Region -> which snapshot ratio drives a part's colour.
        private const int RegionHead = 0, RegionChest = 1, RegionBody = 2;

        private CircleGraphic _head;
        private PanelGraphic _chest, _torso, _armL, _armR, _legL, _legR;

        private struct Part { public MaskableGraphic G; public int Region; }
        private readonly Part[] _parts = new Part[7];

        protected override void BuildContent(RectTransform root)
        {
            _head = MakeDisc(root, "Head");
            _chest = MakePanel(root, "Chest");
            _torso = MakePanel(root, "Torso");
            _armL = MakePanel(root, "ArmL");
            _armR = MakePanel(root, "ArmR");
            _legL = MakePanel(root, "LegL");
            _legR = MakePanel(root, "LegR");

            _parts[0] = new Part { G = _head, Region = RegionHead };
            _parts[1] = new Part { G = _chest, Region = RegionChest };
            _parts[2] = new Part { G = _torso, Region = RegionBody };
            _parts[3] = new Part { G = _armL, Region = RegionBody };
            _parts[4] = new Part { G = _armR, Region = RegionBody };
            _parts[5] = new Part { G = _legL, Region = RegionBody };
            _parts[6] = new Part { G = _legR, Region = RegionBody };
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            // Keep a person-shaped figure on any rect: derive the height from the ~40%-width
            // target at a 1:2.4 aspect, then clamp so it always fits the rect's height.
            float fh = Mathf.Min(s.x * 0.40f * 2.4f, s.y * 0.96f);
            float fw = fh / 2.4f;
            float top = c.y + fh * 0.5f;
            float Y(float frac) => top - frac * fh;

            ((RectTransform)_head.transform).anchoredPosition = new Vector2(c.x, Y(0.10f));
            _head.SetRadius(fw * 0.28f);

            SetBox(_chest, new Vector2(c.x, Y(0.32f)), fw * 0.54f, fh * 0.24f);
            SetBox(_torso, new Vector2(c.x, Y(0.53f)), fw * 0.46f, fh * 0.16f);

            float armThick = fw * 0.17f, armH = fh * 0.40f, armX = fw * 0.42f;
            SetCapsule(_armL, new Vector2(c.x - armX, Y(0.40f)), armThick, armH);
            SetCapsule(_armR, new Vector2(c.x + armX, Y(0.40f)), armThick, armH);

            float legThick = fw * 0.21f, legH = fh * 0.38f, legX = fw * 0.14f;
            SetCapsule(_legL, new Vector2(c.x - legX, Y(0.81f)), legThick, legH);
            SetCapsule(_legR, new Vector2(c.x + legX, Y(0.81f)), legThick, legH);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            bool auto = Def.GetB("auto", true);
            float dHead = Mathf.Clamp01(s.DamageHead01);
            float dChest = Mathf.Clamp01(s.DamageChest01);
            float dBody = Mathf.Clamp01(s.DamageBody01);
            float worst = Mathf.Max(dHead, Mathf.Max(dChest, dBody));

            // "Only when damaged": when unhurt, drop the fill to nothing and leave only a
            // faint ghost outline. The outline-vs-fill switch always coincides with a fill
            // alpha change, so the mesh rebuild that picks up the fill also picks up the
            // border — no flicker, no per-frame rebuild while the state holds.
            bool ghost = auto && worst <= 0.02f;

            var dim = HudPalette.TextDim.Value;
            var warn = HudPalette.Warn.Value;
            var crit = HudPalette.Critical.Value;
            float bw = BorderWidthFor();

            for (int i = 0; i < _parts.Length; i++)
            {
                var g = _parts[i].G;
                if (g == null) continue;
                float r = _parts[i].Region == RegionHead ? dHead
                        : _parts[i].Region == RegionChest ? dChest
                        : dBody;

                Color fill, border;
                float borderW;
                if (ghost)
                {
                    fill = dim; fill.a = 0f;
                    border = dim; border.a *= 0.30f;
                    borderW = Mathf.Max(1f, bw * 0.8f);
                }
                else
                {
                    // Vanilla's OWN damage-doll colour for this region's ratio (green healthy ->
                    // yellow -> red hurt), pulled live off StatusUpdates.DamageGradient so the doll
                    // matches the vanilla body indicator exactly. Vanilla's DamageIndicatorAlpha is
                    // 1 (opaque). Our dim->warn->crit ramp (ColorFor) is only the pre-manager
                    // fallback (main menu / early load).
                    fill = VanillaIcons.DamageColor(r, ColorFor(r, dim, warn, crit));
                    border = dim; border.a = 0f;
                    borderW = 0f;
                }

                g.color = fill;                 // Graphic.color guards: rebuild only on change
                SetBorder(g, border, borderW);
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Only when damaged", () => d.GetB("auto", true), v => d.SetB("auto", v)));
        }

        // ---- helpers ----

        private static CircleGraphic MakeDisc(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<CircleGraphic>();
            g.raycastTarget = false;
            go.AddComponent<VisorWarp>();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            return g;
        }

        private static void SetBox(PanelGraphic p, Vector2 pos, float w, float h)
        {
            ((RectTransform)p.transform).anchoredPosition = pos;
            float r = Mathf.Min(w, h) * 0.22f;
            p.SetShape(w, h, r, r, r, r);
        }

        private static void SetCapsule(PanelGraphic p, Vector2 pos, float w, float h)
        {
            ((RectTransform)p.transform).anchoredPosition = pos;
            float r = Mathf.Min(w, h) * 0.5f;   // stadium: fully rounded on the short axis
            p.SetShape(w, h, r, r, r, r);
        }

        /// <summary>FALLBACK ramp only (used before StatusUpdates exists — see UpdatePanel, which
        /// prefers vanilla's DamageGradient). Dim neutral silhouette at rest, brightening and
        /// reddening through the warn band and on to critical as a region's damage ratio climbs.</summary>
        private static Color ColorFor(float r, Color dim, Color warn, Color crit)
        {
            Color baseDim = dim; baseDim.a *= 0.35f;
            if (r <= 0.02f) return baseDim;
            if (r <= 0.5f) return Color.Lerp(baseDim, warn, r / 0.5f);
            return Color.Lerp(warn, crit, (r - 0.5f) / 0.5f);
        }

        private static void SetBorder(MaskableGraphic g, Color c, float w)
        {
            var p = g as PanelGraphic;
            if (p != null) { p.BorderColor = c; p.BorderWidth = w; return; }
            var cg = g as CircleGraphic;
            if (cg != null) { cg.BorderColor = c; cg.BorderWidth = w; }
        }
    }
}
