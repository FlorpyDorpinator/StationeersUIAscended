using System.Collections.Generic;
using TMPro;
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
    /// An optional WORDS mode (F9 checkbox) trades the icons for the state as text —
    /// "HELMET OPEN/CLOSED", "LIGHT ON/OFF", "JETPACK ON/OFF" — with its own size + colour knobs.
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
        // Each chip is a real PanelGraphic (fill/border/glass all apply).
        protected override bool SupportsPanelAppearance => true;

        // Words-mode text resolves its own "wordColor" ref (HudPalette.TextValue fallback via
        // GlobalOr) — the universal accent row can never touch a pixel here.
        protected override bool UsesAccentColor => false;

        private const int Helmet = 0, Light = 1, Jetpack = 2, N = 3;

        private readonly PanelGraphic[] _panel = new PanelGraphic[N];
        private readonly Image[] _icon = new Image[N];
        private readonly TextMeshProUGUI[] _word = new TextMeshProUGUI[N]; // optional words-mode labels
        private readonly Sprite[] _sprite = new Sprite[N];
        private readonly string[] _wordStr = new string[N];               // words-mode text (null = chip hidden)
        private readonly int[] _order = new int[N];
        private int _sig = -1;

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < N; i++)
            {
                _panel[i] = MakePanel(root, "Chip" + i);
                _icon[i] = MakeIcon(root, "Icon" + i);
                _word[i] = HudText.Make(root, "Word" + i, 11f, TextAlignmentOptions.Center, wrap: true);
                _word[i].enabled = false;
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
            // Optional WORDS mode: each chip reads its state as text ("HELMET OPEN", "LIGHT ON",
            // "JETPACK OFF") instead of the vanilla on/off icon.
            bool words = Def.GetBFor(LayoutBare, "words", false);

            // Vanilla's ImageToggle sprites are ALWAYS populated (both on/off frames live in
            // the prefab; vanilla hides an icon via alpha, not a null sprite). So presence gates
            // on the actual worn GEAR: a chip shows while the equipment is worn, and its icon/word
            // flips with the on/off state (play-test: the light-off icon must show when the lamp is
            // off, not vanish).
            bool wantHelmet = Def.GetB("helmet", true) && s != null && s.HelmetPresent;
            bool wantLight = Def.GetB("light", true) && s != null && s.HelmetPresent;
            bool wantJetpack = Def.GetB("jetpack", true) && s != null && s.JetpackPresent;

            // On-state per chip. Helmet: index1 (the "on" sprite) is the OPEN visor, so on =
            // !HelmetClosed. Lamp: clean bool. Jetpack: actually FLYING (vanilla IsJetpackOn —
            // ControlMode Jetpack/JetpackGravity), so it reads OFF while walking even with the
            // pack on your back.
            bool helmetOpen = s != null && !s.HelmetClosed;
            bool lightOn = s != null && s.HelmetLightOn;
            bool jetOn = s != null && s.JetpackOn;

            if (words)
            {
                _wordStr[Helmet] = wantHelmet ? (helmetOpen ? "HELMET OPEN" : "HELMET CLOSED") : null;
                _wordStr[Light] = wantLight ? (lightOn ? "LIGHT ON" : "LIGHT OFF") : null;
                _wordStr[Jetpack] = wantJetpack ? (jetOn ? "JETPACK ON" : "JETPACK OFF") : null;
            }
            else
            {
                _sprite[Helmet] = wantHelmet ? SafeIcon("helmet", helmetOpen) : null;
                _sprite[Light] = wantLight ? SafeIcon("light", lightOn) : null;
                _sprite[Jetpack] = wantJetpack ? SafeIcon("jetpack", jetOn) : null;
            }

            // Re-flow only when the visible SET (or the mode) changes; steady state just pushes
            // colours + the current sprite/word. A mode bit (8) forces a re-flow on the words toggle.
            int sig = words ? 8 : 0;
            for (int i = 0; i < N; i++)
                if (words ? (_wordStr[i] != null) : (_sprite[i] != null)) sig |= (1 << i);
            if (sig != _sig)
            {
                _sig = sig;
                Reflow(scale);
            }

            Color fill = FillColor();
            Color border = BorderColor();
            float bw = BorderWidthFor();

            // #7: optional single tint over ALL three chip icons. Vanilla art is native-colour
            // (white multiply) by default; a green ref here recolours the whole state-chip strip
            // at once. Empty ref resolves to white = no-op.
            Color iconTint = Def.GetBFor(LayoutBare, "iconTintOn", false)
                ? HudPalette.Resolve(Def.GetSFor(LayoutBare, "iconTint", ""), Color.white)
                : Color.white;
            Color wordColor = GlobalOr(Def.GetSFor(LayoutBare, "wordColor", ""), HudPalette.TextValue.Value);
            float wordSize = HudText.Size(Def.GetFFor(LayoutBare, "wordSize", 11f) * Def.FontScaleFor(LayoutBare)) * scale;

            for (int i = 0; i < N; i++)
            {
                bool vis = words ? (_wordStr[i] != null) : (_sprite[i] != null);
                _panel[i].enabled = vis;
                _icon[i].enabled = vis && !words;
                _word[i].enabled = vis && words;
                if (!vis) continue;

                _panel[i].color = fill;
                _panel[i].BorderColor = border;
                _panel[i].BorderWidth = bw;
                ApplyGlass(_panel[i]);

                if (words)
                {
                    HudText.Sync(_word[i]);
                    _word[i].color = wordColor;
                    _word[i].fontSize = wordSize;
                    HudText.Set(_word[i], _wordStr[i]);
                }
                else
                {
                    _icon[i].sprite = _sprite[i];
                    _icon[i].color = iconTint;   // native colour by default; F9 tint recolours all chips
                }
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;

            into.Add(HudProp.Bool("Helmet chip", () => d.GetB("helmet", true), v => d.SetB("helmet", v)));
            into.Add(HudProp.Bool("Light chip", () => d.GetB("light", true), v => d.SetB("light", v)));
            into.Add(HudProp.Bool("Jetpack chip", () => d.GetB("jetpack", true), v => d.SetB("jetpack", v)));
            // Words instead of icons: each chip reads "HELMET OPEN/CLOSED", "LIGHT ON/OFF",
            // "JETPACK ON/OFF" as text.
            into.Add(HudProp.Bool("Words instead of icons", () => d.GetBFor(EditBare(d), "words", false),
                v => d.SetBFor(EditBare(d), "words", v)));

            int layoutStart = into.Count;
            into.Add(HudProp.F("Gap (px)", () => d.GetFFor(EditBare(d), "gap", 2f),
                v => d.SetFFor(EditBare(d), "gap", Mathf.Clamp(v, 0f, 12f)), 0f, 12f));
            into.Add(HudProp.F("Word text size", () => d.GetFFor(EditBare(d), "wordSize", 11f),
                v => d.SetFFor(EditBare(d), "wordSize", Mathf.Clamp(v, 6f, 32f)), 6f, 32f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            int appearanceStart = into.Count;
            into.Add(HudProp.F("Icon inset (0..0.4)", () => d.GetFFor(EditBare(d), "inset", 0.14f),
                v => d.SetFFor(EditBare(d), "inset", Mathf.Clamp(v, 0f, 0.4f)), 0f, 0.4f));
            // #7: tint every chip icon at once (off = native vanilla colours).
            into.Add(HudProp.Bool("Tint chip icons", () => d.GetBFor(EditBare(d), "iconTintOn", false),
                v => d.SetBFor(EditBare(d), "iconTintOn", v)));
            into.Add(HudProp.Color("Chip icon tint", () => d.GetSFor(EditBare(d), "iconTint", ""),
                v => d.SetSFor(EditBare(d), "iconTint", string.IsNullOrEmpty(v) ? null : v), () => Color.white));
            into.Add(HudProp.Color("Word colour", () => d.GetSFor(EditBare(d), "wordColor", ""),
                v => d.SetSFor(EditBare(d), "wordColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.TextValue.Value));
            for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
        }

        // ---- layout ----

        /// <summary>Pack only the chips that currently have a sprite into a tight, centred row
        /// of equal-width boxes. Hidden chips keep their last position but are disabled, so they
        /// contribute nothing until the set changes and we re-flow again.</summary>
        private void Reflow(float scale)
        {
            bool words = Def.GetBFor(LayoutBare, "words", false);
            var c = CenterFor(scale);
            var sz = SizeFor(scale);
            float gap = Def.GetFFor(LayoutBare, "gap", 2f) * scale;
            float inset = Mathf.Clamp(Def.GetFFor(LayoutBare, "inset", 0.14f), 0f, 0.4f);

            int n = 0;
            for (int i = 0; i < N; i++)
                if (words ? (_wordStr[i] != null) : (_sprite[i] != null)) _order[n++] = i;
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
                    RadiusTL(), RadiusTR(), RadiusBR(), RadiusBL());

                if (words)
                {
                    // Word fills the chip (minus the same inset padding the icon uses); wraps to
                    // two lines in a narrow chip rather than overflowing.
                    var wrt = _word[idx].rectTransform;
                    wrt.anchoredPosition = pos;
                    wrt.sizeDelta = new Vector2(Mathf.Max(2f, chipW * (1f - inset)), chipH);
                }
                else
                {
                    var irt = _icon[idx].rectTransform;
                    irt.anchoredPosition = pos;
                    irt.sizeDelta = new Vector2(iconSz, iconSz);
                }
            }
        }

        private static Sprite SafeIcon(string key, bool on)
        {
            try { return Core.VanillaIcons.SuitStateIcon(key, on); }
            catch { return null; }
        }
    }
}
