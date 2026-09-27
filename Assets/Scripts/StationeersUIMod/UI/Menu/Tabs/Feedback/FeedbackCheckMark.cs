using System.Collections.Generic;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Feedback
{
    /// <summary>
    /// The success panel's DRAWN check mark (the TMP font has no check glyph - it would render as
    /// tofu): a soft "good"-tinted disc (<see cref="PanelGraphic"/> with a half-size corner radius,
    /// round corners forced so a global CUT corner style cannot turn it into an octagon) under a
    /// tick drawn as ONE concave six-point outline filled by <see cref="PolygonPanelGraphic"/> - the
    /// same two renderers the HUD and the Universal Inventory's tag glyph already use, so no new
    /// rendering technique and the same baked AA feather. Colours read <see cref="UiaTheme.Good"/>;
    /// a theme restyle rebuilds the page and re-tints it. No statics.
    /// </summary>
    internal static class FeedbackCheckMark
    {
        internal static void Build(Transform parent, float size)
        {
            var row = UiaUi.Go("check-row", parent);
            UiaUi.Size(row, size);
            UiaUi.HLayout((RectTransform)row.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleCenter, false);
            var host = UiaUi.Go("check", row.transform);
            UiaUi.Size(host, size, size, flexW: 0f);

            Color good = UiaTheme.Good;

            var discGo = UiaUi.Go("disc", host.transform);
            Centre((RectTransform)discGo.transform, size);
            var disc = discGo.AddComponent<PanelGraphic>();
            disc.raycastTarget = false;
            disc.CornerCut = 0;
            disc.color = new Color(good.r, good.g, good.b, 0.16f);
            disc.BorderColor = new Color(good.r, good.g, good.b, 0.85f);
            disc.BorderWidth = 1.6f;
            disc.SetShape(size, size, size * 0.5f);

            var tickGo = UiaUi.Go("tick", host.transform);
            Centre((RectTransform)tickGo.transform, size);
            var tick = tickGo.AddComponent<PolygonPanelGraphic>();
            tick.raycastTarget = false;
            tick.color = good;
            tick.BorderColor = new Color(0f, 0f, 0f, 0f);
            tick.BorderWidth = 0f;
            tick.SetPoints(TickOutline(size * 0.5f * 0.62f), null, null, 0, 2);
        }

        private static void Centre(RectTransform rt, float size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>A stroked tick as a closed CCW outline centred on the origin (PolygonPanelGraphic's
        /// space): the polyline L-B-R offset by half the stroke on each side, mitred at the bottom
        /// vertex. <paramref name="s"/> = the tick's half-extent in px.</summary>
        private static List<Vector2> TickOutline(float s)
        {
            Vector2 l = new Vector2(-0.95f, 0.05f) * s;
            Vector2 b = new Vector2(-0.30f, -0.62f) * s;
            Vector2 r = new Vector2(1.00f, 0.70f) * s;
            float h = 0.17f * s;   // half the stroke width

            Vector2 d1 = (b - l).normalized;
            Vector2 d2 = (r - b).normalized;
            Vector2 n1 = new Vector2(-d1.y, d1.x);   // the V's inner side of each arm
            Vector2 n2 = new Vector2(-d2.y, d2.x);
            Vector2 m = (n1 + n2).normalized;         // mitre direction at the bottom vertex
            float cos = Mathf.Max(0.3f, Vector2.Dot(n1, m));
            float miter = h / cos;

            var pts = new List<Vector2>(6);
            pts.Add(l - n1 * h);
            pts.Add(b - m * miter);
            pts.Add(r - n2 * h);
            pts.Add(r + n2 * h);
            pts.Add(b + m * miter);
            pts.Add(l + n1 * h);
            return pts;
        }
    }
}
