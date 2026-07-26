using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// A compact row (or column) of key-hint chips — each a rounded box carrying a bold key
    /// glyph and a dim action label, e.g. "Q DROP", "Q THROW", "E SWITCH". Keys are read live
    /// from the bindings that actually drive them: the vanilla drop key for drop/throw (throw
    /// is the held drop, so it shares the glyph) and the mod's own hand-swap key for switch.
    /// Any chip's whole text can be pinned with an override param when a binding isn't clean.
    /// Chips flow inside the element rect.
    /// </summary>
    internal sealed class KeybindChipsWidget : HudElementView
    {
        private const int N = 3;
        private readonly PanelGraphic[] _chip = new PanelGraphic[N];
        private readonly TextMeshProUGUI[] _key = new TextMeshProUGUI[N];
        private readonly TextMeshProUGUI[] _label = new TextMeshProUGUI[N];

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < N; i++)
            {
                _chip[i] = MakePanel(root, "Chip" + i);
                _key[i] = HudText.Make(root, "Key" + i, 12f, TextAlignmentOptions.Center);
                _key[i].fontStyle = FontStyles.Bold;
                _label[i] = HudText.Make(root, "Label" + i, 10f, TextAlignmentOptions.MidlineLeft);
            }
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            bool vertical = Def.GetBFor(LayoutBare, "vertical", false);
            float gap = 6f * scale;
            float pad = 6f * scale;

            for (int i = 0; i < N; i++)
            {
                float cw, ch, cx, cy;
                if (vertical)
                {
                    ch = Mathf.Max(6f, (s.y - gap * (N - 1)) / N);
                    cw = s.x;
                    cx = c.x;
                    cy = c.y + s.y * 0.5f - ch * 0.5f - i * (ch + gap);
                }
                else
                {
                    cw = Mathf.Max(6f, (s.x - gap * (N - 1)) / N);
                    ch = s.y;
                    cx = c.x - s.x * 0.5f + cw * 0.5f + i * (cw + gap);
                    cy = c.y;
                }

                ((RectTransform)_chip[i].transform).anchoredPosition = new Vector2(cx, cy);
                _chip[i].SetShape(cw, ch,
                    Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)), Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)));

                float keyW = Mathf.Max(2f, cw * 0.34f);
                _key[i].rectTransform.sizeDelta = new Vector2(keyW, ch);
                _key[i].rectTransform.anchoredPosition =
                    new Vector2(cx - cw * 0.5f + pad + keyW * 0.5f, cy);

                float labelW = Mathf.Max(2f, cw - keyW - pad * 2f);
                _label[i].rectTransform.sizeDelta = new Vector2(labelW, ch);
                _label[i].rectTransform.anchoredPosition =
                    new Vector2(cx - cw * 0.5f + pad + keyW + labelW * 0.5f, cy);
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var keyCol = TextColor();
            var labelCol = HudPalette.TextDim.Value;

            for (int i = 0; i < N; i++)
            {
                _chip[i].color = FillColor();
                _chip[i].BorderColor = BorderColor();
                _chip[i].BorderWidth = BorderWidthFor();
                ApplyGlass(_chip[i]);

                string keyStr, labelStr;
                ResolveChip(i, out keyStr, out labelStr);

                HudText.Sync(_key[i]);
                _key[i].fontSize = HudText.Size(12f * Def.FontScaleFor(LayoutBare)) * scale;
                _key[i].color = keyCol;
                HudText.Set(_key[i], keyStr);

                HudText.Sync(_label[i]);
                _label[i].fontSize = HudText.Size(10f * Def.FontScaleFor(LayoutBare)) * scale;
                _label[i].color = labelCol;
                HudText.Set(_label[i], labelStr);
            }
        }

        /// <summary>A non-empty "chipN" override wins and is split into glyph + label at its
        /// first space; otherwise the chip is built from the live binding for its action.</summary>
        private void ResolveChip(int i, out string key, out string label)
        {
            string ov = Def.GetS("chip" + (i + 1), "");
            if (!string.IsNullOrEmpty(ov)) { SplitChip(ov, out key, out label); return; }

            switch (i)
            {
                case 0: key = Glyph(DropKey()); label = "DROP"; break;
                case 1: key = Glyph(DropKey()); label = "THROW"; break;
                default: key = Glyph(SwapKey()); label = "SWITCH"; break;
            }
        }

        private static KeyCode DropKey()
        {
            // KeyManager.GetKey is the authoritative lookup (KeyMap.Drop is vanilla-
            // deprecated even though the game still assigns it, KeyManager.cs:702).
            try { return KeyManager.GetKey("Drop"); } catch { return KeyCode.Q; }
        }

        private static KeyCode SwapKey()
        {
            try
            {
                return UIAConfig.RadialHandSwapKey != null ? UIAConfig.RadialHandSwapKey.Value : KeyCode.E;
            }
            catch { return KeyCode.E; }
        }

        private static void SplitChip(string text, out string key, out string label)
        {
            int sp = text.IndexOf(' ');
            if (sp < 0) { key = ""; label = text; return; }
            key = text.Substring(0, sp);
            label = text.Substring(sp + 1).TrimStart();
        }

        /// <summary>Short, readable glyph for a KeyCode: strip the Alpha/Keypad prefixes, map
        /// the mouse buttons, and pass everything else through as-is.</summary>
        private static string Glyph(KeyCode k)
        {
            string n = k.ToString();
            if (n.StartsWith("Alpha")) return n.Substring(5);
            if (n.StartsWith("Keypad")) return n.Substring(6);
            if (n == "Mouse0") return "LMB";
            if (n == "Mouse1") return "RMB";
            if (n == "Mouse2") return "MMB";
            if (n.StartsWith("Mouse")) return "M" + n.Substring(5);
            return n;
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Vertical layout", () => d.GetBFor(EditBare(d), "vertical", false),
                v => d.SetBFor(EditBare(d), "vertical", v)));
            into[into.Count - 1].Group = HudPropGroup.Layout;

            for (int i = 1; i <= N; i++)
            {
                string key = "chip" + i;
                into.Add(HudProp.Text("Chip " + i + " (key label, empty = auto)",
                    () => d.GetS(key, ""),
                    v => d.Set(key, string.IsNullOrEmpty(v) ? null : v)));
            }
        }
    }
}
