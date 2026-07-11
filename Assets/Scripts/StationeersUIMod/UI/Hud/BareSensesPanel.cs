using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The BARE tier's whole interface: felt-sense WORDS instead of numbers. A word only
    /// exists while its band is out of nominal (you notice sensations, you don't read a
    /// dashboard) — it flares bright when the band CHANGES, then settles dim. The only
    /// "clock" is a vague day-part word. Sits where the vitals card lives at higher tiers.
    /// </summary>
    internal sealed class BareSensesPanel : HudPanel
    {
        public override string Id => "BareSenses";
        public override bool SuitTier => false;
        public override ConfigEntry<bool> Toggle => HudConfig.ShowVitals;
        public override bool VisibleAt(HudTier tier) => tier == HudTier.Bare;

        private const int Senses = 6;
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
            float x = Screen.width * 0.5f - 30f;
            float baseY = -Screen.height * 0.5f + 60f;
            float step = HudText.Size(HudConfig.BareWordFontSize.Value) * scale * 1.45f;
            for (int i = 0; i < Senses; i++)
            {
                _words[i].rectTransform.sizeDelta = new Vector2(360f, step);
                _words[i].rectTransform.anchoredPosition =
                    new Vector2(x - 180f, baseY + 26f + (Senses - 1 - i) * step);
            }
            _dayPart.rectTransform.sizeDelta = new Vector2(360f, 18f);
            _dayPart.rectTransform.anchoredPosition = new Vector2(x - 180f, baseY);
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
            _dayPart.fontSize = HudText.Size(HudConfig.LabelFontSize.Value * 1.1f) * scale;
            var dim = HudPalette.TextDim.Value;
            _dayPart.color = dim;
            HudText.Set(_dayPart, s.DayPartWord);
        }

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
            t.fontSize = HudText.Size(HudConfig.BareWordFontSize.Value) * scale;
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

        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            float x = Screen.width * 0.5f - 30f;
            float baseY = -Screen.height * 0.5f + 60f;
            float step = HudText.Size(HudConfig.BareWordFontSize.Value) * scale * 1.45f;
            into.Add(new HudEditTarget
            {
                Title = "Felt senses (no suit)",
                Palette = new[] { "HudBareWord", "HudCritical", "HudTextDim" },
                Values = new ConfigEntryBase[] { HudConfig.BareWordFontSize, HudConfig.DiegeticTiers },
                CanvasRect = new Rect(x - 360f, baseY - 12f, 360f, step * Senses + 44f),
            });
        }
    }
}
