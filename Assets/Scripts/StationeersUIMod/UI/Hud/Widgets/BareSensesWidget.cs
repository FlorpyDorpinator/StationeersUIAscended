using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The BARE tier's whole interface as a document element: felt-sense WORDS instead of
    /// numbers. A word only exists while its band is out of nominal (you notice sensations,
    /// you don't read a dashboard) — it flares bright when the band CHANGES, then settles
    /// dim. The only "clock" is a vague day-part word. The placed sense rows stack top-to-
    /// bottom inside the element rect with the day-part word beneath them.
    ///
    /// WHICH sense sits in WHICH row — and whether it appears at all — is author-controlled via
    /// the <c>order</c> param: a CSV of slot tokens (a sense key or <c>none</c>), one per row
    /// top-to-bottom. All EIGHT senses (temp/air/pressure/thirst/hunger/health/cognition/toilet)
    /// can be placed at once. The F9 editor exposes one combo per row (see
    /// <see cref="DescribeProps"/>); picking a sense already placed elsewhere SWAPS the two rows
    /// (lossless reorder), while <c>none</c> clears a row. The row HEIGHT scales to how many
    /// senses are placed (six placed reads identically to the old fixed layout; unassigned rows
    /// collapse rather than leaving a gap).
    /// </summary>
    internal sealed class BareSensesWidget : HudElementView
    {
        // Layout rows = the count of selectable senses, so every sense can be placed at once.
        private const int Rows = 8;

        // Sense identity. Index = the sense's slot in the snapshot word set (see WordForSense);
        // the token is what the `order` CSV stores. SenseNames/SlotOptions feed the editor combos
        // (option i+1 = sense i). Toilet is server-sim only and stays silent on a pure MP client.
        private static readonly string[] SenseKeys =
            { "temp", "air", "pressure", "thirst", "hunger", "health", "cognition", "toilet" };
        private static readonly string[] SenseNames =
            { "Temperature", "Air", "Pressure", "Thirst", "Hunger", "Health", "Consciousness", "Toilet" };
        private const string NoneToken = "none";
        private const string DefaultOrder = "temp,air,pressure,thirst,hunger,health";

        // Combo entries: option 0 = empty row, options 1..N = the senses in SenseNames order.
        private static readonly string[] SlotOptions =
            { "— none —", "Temperature", "Air", "Pressure", "Thirst", "Hunger", "Health", "Consciousness", "Toilet" };

        /// <summary>Bands whose word is an emergency: painted Critical and held bright even
        /// after the flare settles, so a life-threatening sensation never fades to a whisper.</summary>
        private static readonly HashSet<string> Urgent = new HashSet<string>
        {
            "FREEZING", "BURNING", "CHOKING", "VACUUM", "STARVING", "PARCHED", "DYING", "FAILING",
            "FADING", "DESPERATE",
        };

        private readonly TextMeshProUGUI[] _words = new TextMeshProUGUI[Rows];
        private readonly string[] _current = new string[Rows];
        private readonly float[] _flash = new float[Rows];   // seconds since band change
        private TextMeshProUGUI _dayPart;

        // Row→sense mapping, parsed from `order`. Cached against the raw CSV so the steady-state
        // path (unchanged string) never re-splits or allocates.
        private readonly int[] _slots = new int[Rows];
        private readonly int[] _slotsCache = new int[Rows];
        private string _orderCache;

        // The row rects are re-placed only when the assignment changes (row height scales to the
        // number of placed senses), so this caches the last-laid-out arrangement + scale.
        private float _lastScale = 1f;
        private int _layoutSig = int.MinValue;

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < Rows; i++)
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
            _lastScale = scale;
            _layoutSig = int.MinValue; // geometry moved — force a re-place on the next update
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            _lastScale = scale;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            ReadSlots(_slots);

            // Re-place the rows only when the assignment changes: the row height scales to the
            // number of PLACED senses, and unassigned rows collapse instead of leaving a gap.
            int sig = 0;
            for (int k = 0; k < Rows; k++) sig = sig * 9 + (_slots[k] + 1); // slot -1..7 → 0..8
            if (sig != _layoutSig) { _layoutSig = sig; Reflow(scale); }

            // Row k shows whatever sense the author assigned to it (or nothing when empty). A
            // sense only produces a word while it's out of nominal; nominal rows stay blank. The
            // "show everything" demo snapshot is the only thing that fills every row for preview.
            for (int k = 0; k < Rows; k++)
            {
                int sense = _slots[k];
                Apply(k, sense >= 0 ? WordForSense(s, sense) : "", dt, scale);
            }

            HudText.Sync(_dayPart);
            _dayPart.fontSize = HudText.Size(WordSize() * 0.6f * Def.FontScale) * scale;
            _dayPart.color = HudPalette.TextDim.Value;
            HudText.Set(_dayPart, s != null ? (s.DayPartWord ?? "") : "");
        }

        /// <summary>Place one row per PLACED sense, stacked from the top, with the day-part word
        /// last. Row height = rect / (placed + 1), so fewer senses = taller rows and all eight
        /// still fit. Unassigned rows are left unplaced (Apply also deactivates them).</summary>
        private void Reflow(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float top = c.y + s.y * 0.5f;

            int assigned = 0;
            for (int k = 0; k < Rows; k++) if (_slots[k] >= 0) assigned++;
            int rowsToShow = Mathf.Max(1, assigned);
            float rowH = s.y / (rowsToShow + 1);
            var size = new Vector2(s.x, rowH);

            int placed = 0;
            for (int k = 0; k < Rows; k++)
            {
                if (_slots[k] < 0) continue;            // unassigned: collapses (no reserved row)
                var rt = _words[k].rectTransform;
                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2(c.x, top - rowH * (placed + 0.5f));
                placed++;
            }
            _dayPart.rectTransform.sizeDelta = size;
            _dayPart.rectTransform.anchoredPosition = new Vector2(c.x, top - rowH * (rowsToShow + 0.5f));
        }

        private static string WordForSense(HudSnapshot s, int sense)
        {
            if (s == null) return "";
            switch (sense)
            {
                case 0: return s.WordTemp;
                case 1: return s.WordAir;
                case 2: return s.WordPressure;
                case 3: return s.WordThirst;
                case 4: return s.WordHunger;
                case 5: return s.WordHealth;
                case 6: return s.WordCognition;
                case 7: return s.WordToilet;
                default: return "";
            }
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

        // ---- slot mapping ----

        /// <summary>Copy the current row→sense mapping into <paramref name="slots"/>. Re-parses the
        /// CSV only when the raw string changes, so the per-frame path is a plain copy.</summary>
        private void ReadSlots(int[] slots)
        {
            string csv = Def.GetS("order", DefaultOrder);
            if (string.IsNullOrEmpty(csv)) csv = DefaultOrder;
            if (!string.Equals(csv, _orderCache, StringComparison.Ordinal))
            {
                _orderCache = csv;
                ParseOrder(csv, _slotsCache);
            }
            for (int k = 0; k < Rows; k++) slots[k] = _slotsCache[k];
        }

        /// <summary>CSV → row slots (sense index, or −1 for an empty row). Unknown, "none" and
        /// DUPLICATE tokens all become empty rows, so a hand-edited profile can't double-place a
        /// sense.</summary>
        private static void ParseOrder(string csv, int[] slots)
        {
            for (int k = 0; k < Rows; k++) slots[k] = -1;
            if (string.IsNullOrEmpty(csv)) return;
            var toks = csv.Split(',');
            int used = 0; // bitmask of senses already placed
            int slot = 0;
            for (int t = 0; t < toks.Length && slot < Rows; t++, slot++)
            {
                int si = SenseIndex(toks[t]);
                if (si >= 0 && (used & (1 << si)) == 0) { used |= 1 << si; slots[slot] = si; }
                // else: leave -1 (none / unknown / duplicate)
            }
        }

        private static int SenseIndex(string tok)
        {
            if (string.IsNullOrEmpty(tok)) return -1;
            tok = tok.Trim();
            for (int i = 0; i < SenseKeys.Length; i++)
                if (string.Equals(tok, SenseKeys[i], StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void WriteSlots(int[] slots)
        {
            var sb = new StringBuilder(64);
            for (int k = 0; k < Rows; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append(slots[k] >= 0 ? SenseKeys[slots[k]] : NoneToken);
            }
            Def.Set("order", sb.ToString());
            _orderCache = null; // force ReadSlots to re-parse next frame
        }

        // Editor combo backing: option 0 = none, 1..N = sense (SenseNames order).
        private int GetSlotOption(int row)
        {
            var slots = new int[Rows];
            string csv = Def.GetS("order", DefaultOrder);
            ParseOrder(string.IsNullOrEmpty(csv) ? DefaultOrder : csv, slots);
            return slots[row] >= 0 ? slots[row] + 1 : 0;
        }

        private void SetSlotOption(int row, int option)
        {
            var slots = new int[Rows];
            string csv = Def.GetS("order", DefaultOrder);
            ParseOrder(string.IsNullOrEmpty(csv) ? DefaultOrder : csv, slots);

            int sense = option - 1; // −1 = none
            if (sense < 0)
            {
                slots[row] = -1;               // clear this row
            }
            else
            {
                // If the sense already sits in another row, SWAP the two (lossless reorder);
                // otherwise drop it into this row, replacing whatever was there (add / move-in).
                int other = -1;
                for (int i = 0; i < Rows; i++) if (slots[i] == sense) { other = i; break; }
                if (other >= 0 && other != row) slots[other] = slots[row];
                slots[row] = sense;
            }
            WriteSlots(slots);
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            into.Add(HudProp.F("Felt-sense word size", () => Def.GetF("wordSize", 20f),
                v => Def.SetF("wordSize", v), 12f, 36f));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            into.Add(HudProp.Header("Senses (row = top → bottom)"));
            for (int i = 0; i < Rows; i++)
            {
                int row = i; // capture for the closures
                var pr = HudProp.Enum("Row " + (row + 1), () => GetSlotOption(row),
                    v => SetSlotOption(row, v), SlotOptions);
                if (row == 0)
                    pr.Help = "Which sense shows in this row. '— none —' removes it. " +
                              "Picking a sense already in another row swaps the two rows. " +
                              "All eight senses can be placed at once; the rows scale to fit.";
                into.Add(pr);
            }
        }
    }
}
