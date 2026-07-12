using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The BARE tier's whole interface as a document element: felt-sense WORDS instead of
    /// numbers. A word only exists while its band is out of nominal (you notice sensations,
    /// you don't read a dashboard) — it flares bright when the band CHANGES, then settles
    /// dim. The only "clock" is a vague day-part word. The six senses stack top-to-bottom
    /// inside the element rect with the day-part word beneath them, so resizing the element
    /// in the designer scales the whole readout rather than any one screen anchor.
    /// </summary>
    internal sealed class BareSensesWidget : HudElementView
    {
        private const int Senses = 6;

        /// <summary>Bands whose word is an emergency: painted Critical and held bright even
        /// after the flare settles, so a life-threatening sensation never fades to a whisper.</summary>
        private static readonly HashSet<string> Urgent = new HashSet<string>
        {
            "FREEZING", "BURNING", "CHOKING", "VACUUM", "STARVING", "PARCHED", "DYING", "FAILING",
        };

        private readonly TextMeshProUGUI[] _words = new TextMeshProUGUI[Senses];
        private readonly string[] _current = new string[Senses];
        private readonly float[] _flash = new float[Senses];   // seconds since band change
        private TextMeshProUGUI _dayPart;

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < Senses; i++)
            {
                _words[i] = HudText.Make(root, "Word" + i, 20f, TextAlignmentOptions.Right);
                _current[i] = "";
                _flash[i] = 99f;
            }
            _dayPart = HudText.Make(root, "DayPart", 13f, TextAlignmentOptions.Right);
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;

            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float top = c.y + s.y * 0.5f;
            // Seven equal rows: six senses stacked from the top, the day-part word last.
            float rowH = s.y / (Senses + 1);

            for (int i = 0; i < Senses; i++)
            {
                _words[i].rectTransform.sizeDelta = new Vector2(s.x, rowH);
                _words[i].rectTransform.anchoredPosition =
                    new Vector2(c.x, top - rowH * (i + 0.5f));
            }
            _dayPart.rectTransform.sizeDelta = new Vector2(s.x, rowH);
            _dayPart.rectTransform.anchoredPosition =
                new Vector2(c.x, top - rowH * (Senses + 0.5f));
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Apply(0, s.WordTemp, dt, scale);
            Apply(1, s.WordAir, dt, scale);
            Apply(2, s.WordPressure, dt, scale);
            Apply(3, s.WordThirst, dt, scale);
            Apply(4, s.WordHunger, dt, scale);
            Apply(5, s.WordHealth, dt, scale);

            HudText.Sync(_dayPart);
            _dayPart.fontSize = HudText.Size(WordSize() * 0.6f * Def.FontScale) * scale;
            _dayPart.color = HudPalette.TextDim.Value;
            HudText.Set(_dayPart, s.DayPartWord ?? "");
        }

        private float WordSize() => Def.GetF("wordSize", 20f);

        private void Apply(int i, string word, float dt, float scale)
        {
            word = word ?? "";
            if (word != _current[i])
            {
                _current[i] = word;
                _flash[i] = 0f;         // band changed — the sensation flares
            }
            else
            {
                _flash[i] += dt;
            }

            var t = _words[i];
            bool show = word.Length > 0;
            t.gameObject.SetActive(show);
            if (!show) return;

            HudText.Sync(t);
            t.fontSize = HudText.Size(WordSize() * Def.FontScale) * scale;
            Color c = Urgent.Contains(word) ? HudPalette.Critical.Value : HudPalette.BareWord.Value;
            // Flare for ~0.4s, linger bright ~3s, then settle to a quiet presence.
            float f = _flash[i];
            float brightness = f < 0.4f ? Mathf.Lerp(0.4f, 1.35f, f / 0.4f)
                : f < 3.5f ? Mathf.Lerp(1.35f, 1f, (f - 0.4f) / 3.1f)
                : Urgent.Contains(word) ? 1f : 0.62f;
            c.a *= Mathf.Clamp01(brightness);
            t.color = c;
            HudText.Set(t, word);
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            into.Add(HudProp.F("Felt-sense word size", () => Def.GetF("wordSize", 20f),
                v => Def.SetF("wordSize", v), 12f, 36f));
        }
    }
}
