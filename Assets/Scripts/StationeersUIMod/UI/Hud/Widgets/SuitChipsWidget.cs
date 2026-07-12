using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The suit status-chip row: one small rounded chip per piece of life-support gear —
    /// helmet (visor), suit conditioner, helmet lamp, and optionally internals — each carrying
    /// its glyph and a state dot. The dot reads green when the gear is on/sealed, dim when it's
    /// simply off, and red for the one genuinely dangerous case: an OPEN visor while a suit is
    /// worn. A chip is present only while its gear is (no helmet, no helmet chips).
    ///
    /// The visible set changes as gear is donned or removed, so the chips are re-flowed only
    /// when that set changes (a cheap 4-bit signature) — steady state pushes colours and
    /// nothing else. Chips flow horizontally (or vertically) and stay centred in the rect.
    /// </summary>
    internal sealed class SuitChipsWidget : HudElementView
    {
        private const int Helmet = 0, SuitAc = 1, Light = 2, Internals = 3;

        private static readonly HudIconKind[] Kinds =
            { HudIconKind.Helmet, HudIconKind.Suit, HudIconKind.Sun, HudIconKind.Lungs };

        private sealed class Chip
        {
            public PanelGraphic Panel;
            public HudIconGraphic Glyph;
            public CircleGraphic Dot;
        }

        private readonly Chip[] _chips = new Chip[4];
        private readonly int[] _order = new int[4];
        private int _sig = -1;

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < _chips.Length; i++)
            {
                var chip = new Chip { Panel = MakePanel(root, "Chip" + i) };
                chip.Glyph = MakeGlyph(root, "Glyph" + i, Kinds[i]);
                chip.Dot = MakeDot(root, "Dot" + i);
                _chips[i] = chip;
            }
        }

        public override void Layout(float scale)
        {
            // Rect/scale (or an editor knob) may have changed; force a re-flow on the next
            // update, where the live visible set is known.
            Root.anchoredPosition = Vector2.zero;
            _sig = -1;
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            bool wantInternals = Def.GetB("internals", false);

            // Suit presence isn't a snapshot field; read it guarded, exactly the property the
            // sampler uses (Human.Suit). A missing suit hides the conditioner chip and takes
            // the danger out of an open visor.
            bool suitPresent = false;
            try { suitPresent = s != null && s.Human != null && s.Human.Suit != null; } catch { }

            bool vHelmet = s != null && s.HelmetPresent;
            bool vSuit = suitPresent;
            bool vLight = s != null && s.HelmetPresent;
            bool vInternals = wantInternals && s != null && s.HasInternals;

            int sig = (vHelmet ? 1 : 0) | (vSuit ? 2 : 0) | (vLight ? 4 : 0) | (vInternals ? 8 : 0);
            if (sig != _sig)
            {
                _sig = sig;
                Reflow(scale, vHelmet, vSuit, vLight, vInternals);
            }

            Color accent = TextColor();
            Color fill = FillColor();
            Color border = BorderColor();
            float bw = BorderWidthFor();
            Color good = HudPalette.Good.Value;
            Color dim = HudPalette.TextDim.Value;
            Color crit = HudPalette.Critical.Value;

            // Visor down = sealed (green); an open visor is the dangerous one only while suited
            // (red), otherwise a neutral off. Lamp / conditioner off are plain neutral.
            PushChip(Helmet, vHelmet, accent, fill, border, bw,
                s != null && s.HelmetClosed ? good : (suitPresent ? crit : dim));
            PushChip(SuitAc, vSuit, accent, fill, border, bw,
                s != null && s.SuitAcOn ? good : dim);
            PushChip(Light, vLight, accent, fill, border, bw,
                s != null && s.HelmetLightOn ? good : dim);
            PushChip(Internals, vInternals, accent, fill, border, bw,
                s != null && s.InternalsOn ? good : dim);
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Internals chip", () => d.GetB("internals", false), v => d.SetB("internals", v)));
            into.Add(HudProp.Bool("Vertical", () => d.GetB("vertical", false), v => d.SetB("vertical", v)));
            into.Add(HudProp.F("Gap", () => d.GetF("gap", 8f), v => d.SetF("gap", Mathf.Clamp(v, 0f, 40f)), 0f, 40f));
        }

        // ---- layout ----

        private void Reflow(float scale, bool vHelmet, bool vSuit, bool vLight, bool vInternals)
        {
            var c = CenterFor(scale);
            var sz = SizeFor(scale);
            float gap = Def.GetF("gap", 8f) * scale;
            bool vertical = Def.GetB("vertical", false);

            int n = 0;
            if (vHelmet) _order[n++] = Helmet;
            if (vSuit) _order[n++] = SuitAc;
            if (vLight) _order[n++] = Light;
            if (vInternals) _order[n++] = Internals;
            if (n == 0) return;

            float along = vertical ? sz.y : sz.x;
            float across = vertical ? sz.x : sz.y;
            float chip = Mathf.Max(2f, Mathf.Min(across, (along - gap * (n - 1)) / n));

            float total = chip * n + gap * (n - 1);
            float start = total * 0.5f - chip * 0.5f;

            float r = chip * 0.28f;
            float glyphSz = chip * 0.56f;
            float dotR = 3f * scale;                        // 6px state dot
            float inset = chip * 0.5f - dotR - 2f * scale;

            for (int k = 0; k < n; k++)
            {
                var chipObj = _chips[_order[k]];
                Vector2 pos = vertical
                    ? new Vector2(c.x, c.y + start - k * (chip + gap))
                    : new Vector2(c.x - start + k * (chip + gap), c.y);

                ((RectTransform)chipObj.Panel.transform).anchoredPosition = pos;
                chipObj.Panel.SetShape(chip, chip, r, r, r, r);

                var grt = (RectTransform)chipObj.Glyph.transform;
                grt.anchoredPosition = new Vector2(pos.x, pos.y - chip * 0.04f);
                grt.sizeDelta = new Vector2(glyphSz, glyphSz);

                var drt = (RectTransform)chipObj.Dot.transform;
                drt.anchoredPosition = new Vector2(pos.x + inset, pos.y + inset);
                drt.sizeDelta = new Vector2(dotR * 2f, dotR * 2f);
                chipObj.Dot.SetRadius(dotR);
            }
        }

        private void PushChip(int i, bool visible, Color accent, Color fill, Color border,
            float bw, Color dot)
        {
            var chip = _chips[i];
            if (chip == null) return;
            chip.Panel.enabled = visible;
            chip.Glyph.enabled = visible;
            chip.Dot.enabled = visible;
            if (!visible) return;

            chip.Panel.color = fill;
            chip.Panel.BorderColor = border;
            chip.Panel.BorderWidth = bw;
            chip.Glyph.color = accent;
            chip.Dot.color = dot;
        }

        // ---- factories ----

        private static HudIconGraphic MakeGlyph(Transform parent, string name, HudIconKind kind)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<HudIconGraphic>();
            g.raycastTarget = false;
            go.AddComponent<VisorWarp>();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            g.Kind = kind;
            return g;
        }

        private static CircleGraphic MakeDot(Transform parent, string name)
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
    }
}
