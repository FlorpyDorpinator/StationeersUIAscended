using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The state-chip row: three small rounded boxes carrying the VANILLA helmet / helmet-lamp
    /// / jetpack on-off PNGs — the very sprites the game swaps when you press J / L / I. Each
    /// chip is a glass panel with a native-colour Image; the sprite is pulled live from the
    /// player-state toggles (Core.VanillaIcons.SuitStateIcon), so it always matches the vanilla
    /// HUD. Meant to sit as a tight strip above the portrait, boxes right up against each other.
    ///
    /// A chip is present only while its equipment is (no helmet => the helmet toggle has no
    /// sprite => that chip hides), and a chip can be switched off entirely from the F9 designer.
    /// The visible set changes as gear is donned or the designer toggles a chip, so the strip is
    /// re-flowed only when that set changes (a cheap 3-bit signature) — steady state pushes the
    /// current sprite and colours and nothing else. Vanilla sprites resolve late (their singleton
    /// only exists in-world), so the sprite channel keeps retrying every frame until it lands.
    /// </summary>
    internal sealed class StateChipsWidget : HudElementView
    {
        private const int Helmet = 0, Light = 1, Jetpack = 2, N = 3;

        private readonly PanelGraphic[] _panel = new PanelGraphic[N];
        private readonly Image[] _icon = new Image[N];
        private readonly Sprite[] _sprite = new Sprite[N];
        private readonly int[] _order = new int[N];
        private int _sig = -1;

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < N; i++)
            {
                _panel[i] = MakePanel(root, "Chip" + i);
                _icon[i] = MakeIcon(root, "Icon" + i);
            }
        }

        public override void Layout(float scale)
        {
            // Rect/scale (or a designer knob) may have changed; force a re-flow on the next
            // update, where the live visible set is known.
            Root.anchoredPosition = Vector2.zero;
            _sig = -1;
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            // Vanilla's ImageToggle sprites are ALWAYS populated (both on/off frames live in
            // the prefab; vanilla hides an icon via alpha, not a null sprite). So sprite-null
            // can't gate presence — gate on the actual worn GEAR: a chip shows while the
            // equipment is worn, and its SPRITE flips with the on/off state (play-test: the
            // light-off icon must show when the lamp is off, not vanish).
            bool wantHelmet = Def.GetB("helmet", true) && s != null && s.HelmetPresent;
            bool wantLight = Def.GetB("light", true) && s != null && s.HelmetPresent;
            bool wantJetpack = Def.GetB("jetpack", true) && s != null && s.JetpackPresent;

            // On-state per chip. Helmet: index1 (the "on" sprite) is the OPEN visor, so pass
            // on = !HelmetClosed. Lamp: clean bool → lighton/lightoff sprites. Jetpack:
            // actually FLYING (vanilla IsJetpackOn — ControlMode Jetpack/JetpackGravity),
            // so it reads jetpackoff while walking even with the pack on your back.
            bool helmetOpen = s != null && !s.HelmetClosed;
            bool lightOn = s != null && s.HelmetLightOn;
            bool jetOn = s != null && s.JetpackOn;

            _sprite[Helmet] = wantHelmet ? SafeIcon("helmet", helmetOpen) : null;
            _sprite[Light] = wantLight ? SafeIcon("light", lightOn) : null;
            _sprite[Jetpack] = wantJetpack ? SafeIcon("jetpack", jetOn) : null;

            int sig = 0;
            for (int i = 0; i < N; i++) if (_sprite[i] != null) sig |= (1 << i);
            if (sig != _sig)
            {
                _sig = sig;
                Reflow(scale);
            }

            Color fill = FillColor();
            Color border = BorderColor();
            float bw = BorderWidthFor();

            for (int i = 0; i < N; i++)
            {
                bool vis = _sprite[i] != null;
                _panel[i].enabled = vis;
                _icon[i].enabled = vis;
                if (!vis) continue;

                _panel[i].color = fill;
                _panel[i].BorderColor = border;
                _panel[i].BorderWidth = bw;
                ApplyGlass(_panel[i]);

                _icon[i].sprite = _sprite[i];
                _icon[i].color = Color.white;   // vanilla art shows at native colour
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Gap (px)", () => d.GetF("gap", 2f),
                v => d.SetF("gap", Mathf.Clamp(v, 0f, 12f)), 0f, 12f));
            into.Add(HudProp.F("Icon inset (0..0.4)", () => d.GetF("inset", 0.14f),
                v => d.SetF("inset", Mathf.Clamp(v, 0f, 0.4f)), 0f, 0.4f));
            into.Add(HudProp.Bool("Helmet chip", () => d.GetB("helmet", true), v => d.SetB("helmet", v)));
            into.Add(HudProp.Bool("Light chip", () => d.GetB("light", true), v => d.SetB("light", v)));
            into.Add(HudProp.Bool("Jetpack chip", () => d.GetB("jetpack", true), v => d.SetB("jetpack", v)));
        }

        // ---- layout ----

        /// <summary>Pack only the chips that currently have a sprite into a tight, centred row
        /// of equal-width boxes. Hidden chips keep their last position but are disabled, so they
        /// contribute nothing until the set changes and we re-flow again.</summary>
        private void Reflow(float scale)
        {
            var c = CenterFor(scale);
            var sz = SizeFor(scale);
            float gap = Def.GetF("gap", 2f) * scale;
            float inset = Mathf.Clamp(Def.GetF("inset", 0.14f), 0f, 0.4f);

            int n = 0;
            for (int i = 0; i < N; i++) if (_sprite[i] != null) _order[n++] = i;
            if (n == 0) return;

            float chipW = Mathf.Max(2f, (sz.x - gap * (n - 1)) / n);
            float chipH = sz.y;
            float total = chipW * n + gap * (n - 1);
            float start = total * 0.5f - chipW * 0.5f;

            float side = Mathf.Min(chipW, chipH);
            float iconSz = Mathf.Max(2f, side * (1f - inset * 2f));

            for (int k = 0; k < n; k++)
            {
                int idx = _order[k];
                var pos = new Vector2(c.x - start + k * (chipW + gap), c.y);

                ((RectTransform)_panel[idx].transform).anchoredPosition = pos;
                _panel[idx].SetShape(chipW, chipH,
                    Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL));

                var irt = _icon[idx].rectTransform;
                irt.anchoredPosition = pos;
                irt.sizeDelta = new Vector2(iconSz, iconSz);
            }
        }

        private static Sprite SafeIcon(string key, bool on)
        {
            try { return Core.VanillaIcons.SuitStateIcon(key, on); }
            catch { return null; }
        }
    }
}
