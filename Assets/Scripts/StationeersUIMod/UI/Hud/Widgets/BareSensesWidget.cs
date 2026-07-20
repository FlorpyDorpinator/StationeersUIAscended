using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The BARE tier's whole interface as a document element: felt-sense WORDS instead of numbers.
    /// A word only exists while its band is out of nominal — it flares bright when the band CHANGES,
    /// then settles dim (Critical bands stay bright). The only "clock" is a vague day-part word.
    ///
    /// The words are laid out ENTIRELY inside the element rect (never screen-anchored). The author
    /// arranges them with a layout MODE (vertical / horizontal / grid), a 9-way ANCHOR that clusters
    /// the block within the element, plus spacing / grid columns / padding — so moving or resizing
    /// the element carries the words with it. Colour, opacity, an optional whole-element background
    /// (everything a Box has, default OFF) and optional per-sense boxes are all author-controlled.
    ///
    /// The WORDS, their COLOURS and the LEVELS at which they trigger are the author-editable catalog
    /// in <see cref="SenseCatalog"/>. The widget evaluates each sense's active band from the raw,
    /// MP-safe driver values on the snapshot (temperature, oxygen, pressure, water, food, damage,
    /// sanitation) against per-element threshold overrides — the sampler no longer bakes the word.
    /// The multiplayer guards are honoured here too: temp/air/pressure need a valid atmosphere
    /// (FeltValid); the toilet sense stays silent unless the server sim is trusted (SanitationValid).
    ///
    /// WHICH sense sits in WHICH row is the <c>order</c> CSV (one token per row); picking a sense
    /// already placed elsewhere SWAPS the two rows. Nominal senses collapse (leave no gap).
    /// </summary>
    internal sealed class BareSensesWidget : HudElementView
    {
        private const int Rows = 8;
        private const int DayItem = Rows;  // the day-part word is item index 8

        // Sense identity. Index = the sense's slot in SenseCatalog.Senses AND the `order` token set.
        private static readonly string[] SenseKeys =
            { "temp", "air", "pressure", "thirst", "hunger", "health", "cognition", "toilet" };
        private const string NoneToken = "none";
        private const string DefaultOrder = "temp,air,pressure,thirst,hunger,health";

        // Combo entries: option 0 = empty row, options 1..N = the senses in catalog order.
        private static readonly string[] SlotOptions =
            { "— none —", "Temperature", "Air", "Pressure", "Thirst", "Hunger", "Health", "Consciousness", "Toilet" };
        private static readonly string[] LayoutModeNames = { "Vertical", "Horizontal", "Grid" };

        // Per-(sense,band) Params-bag keys, precomputed once so the per-frame read path never
        // concatenates a string. t_ = threshold, w_ = word, c_ = colour ref.
        private static readonly string[][] ThKeys;
        private static readonly string[][] WKeys;
        private static readonly string[][] CKeys;

        static BareSensesWidget()
        {
            var senses = SenseCatalog.Senses;
            ThKeys = new string[senses.Length][];
            WKeys = new string[senses.Length][];
            CKeys = new string[senses.Length][];
            for (int i = 0; i < senses.Length; i++)
            {
                var bands = senses[i].Bands;
                ThKeys[i] = new string[bands.Length];
                WKeys[i] = new string[bands.Length];
                CKeys[i] = new string[bands.Length];
                for (int b = 0; b < bands.Length; b++)
                {
                    ThKeys[i][b] = "t_" + senses[i].Key + "_" + bands[b].Id;
                    WKeys[i][b] = "w_" + senses[i].Key + "_" + bands[b].Id;
                    CKeys[i][b] = "c_" + senses[i].Key + "_" + bands[b].Id;
                }
            }
        }

        private PanelGraphic _bg;                                   // whole-element background (default off)
        private readonly PanelGraphic[] _itemBg = new PanelGraphic[Rows + 1]; // per-sense boxes (+ day)
        private readonly TextMeshProUGUI[] _words = new TextMeshProUGUI[Rows];
        private readonly string[] _current = new string[Rows];
        private readonly float[] _flash = new float[Rows];         // seconds since band change
        private TextMeshProUGUI _dayPart;

        // Row→sense mapping, parsed from `order` and cached against the raw CSV.
        private readonly int[] _slots = new int[Rows];
        private readonly int[] _slotsCache = new int[Rows];
        private string _orderCache;

        // Per-frame layout scratch (active items in draw order + their measured line heights/widths).
        private readonly int[] _active = new int[Rows + 1];
        private readonly float[] _lineH = new float[Rows + 1];
        private readonly float[] _cellW = new float[Rows + 1];
        private bool _itemBoxOn;

        // Resolved-colour memo per (sense, band), plus one slot each for the word/day-part refs.
        // HudPalette.Resolve parses a "#RRGGBBAA" literal by allocating several short-lived strings —
        // and a hex literal is exactly what the F9 colour picker writes — so resolving every active
        // band every frame churned garbage. A hex ref is constant for a given string, so it memos
        // safely; a PALETTE-NAME ref must never be memoed (it tracks the live palette by contract).
        private readonly string[][] _crefMemo = NewJagged<string>();
        private readonly Color[][] _colMemo = NewJagged<Color>();
        private string _wordCref; private Color _wordCol;
        private string _dayCref; private Color _dayCol;

        private static T[][] NewJagged<T>()
        {
            var s = SenseCatalog.Senses;
            var a = new T[s.Length][];
            for (int i = 0; i < s.Length; i++) a[i] = new T[s[i].Bands.Length];
            return a;
        }

        protected override bool SupportsTrapezoid => true; // the whole-element background can be a trapezoid

        protected override void BuildContent(RectTransform root)
        {
            // Draw order = child order: background first (behind), then per-item boxes, then words.
            _bg = MakePanel(root, "SensesBg");
            _bg.gameObject.SetActive(false);
            for (int i = 0; i < _itemBg.Length; i++)
            {
                _itemBg[i] = MakePanel(root, "ItemBg" + i);
                _itemBg[i].gameObject.SetActive(false);
            }
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
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            ReadSlots(_slots);

            float opacity = TextOpacity();
            _itemBoxOn = Def.GetBFor(LayoutBare, "itemBox", false);
            Color wordBase = ResolveRef(Def.GetSFor(LayoutBare, "wordColor", ""),
                HudPalette.BareWord.Value, ref _wordCref, ref _wordCol);

            // Every per-item box starts hidden; the arrange pass re-activates the ones in use.
            for (int i = 0; i < _itemBg.Length; i++) _itemBg[i].gameObject.SetActive(false);

            // Pass 1: evaluate + animate each row, set its word/colour/font, and collect the ACTIVE
            // items (a placed sense whose band produced a word) in row order, day-part last.
            int count = 0;
            for (int row = 0; row < Rows; row++)
            {
                int sense = _slots[row];
                string word; Color col; bool crit;
                EvalSense(s, sense, wordBase, out word, out col, out crit);
                ApplySenseWord(row, word, col, crit, dt, scale, opacity);
                if (word.Length > 0)
                {
                    _active[count] = row;
                    _lineH[count] = HudText.Size(WordSize() * Def.FontScaleFor(LayoutBare)) * scale * 1.3f;
                    count++;
                }
            }

            string day = (s != null && Def.GetBFor(LayoutBare, "showDay", true)) ? (s.DayPartWord ?? "") : "";
            ApplyDayWord(day, scale, opacity);
            if (day.Length > 0)
            {
                _active[count] = DayItem;
                _lineH[count] = HudText.Size(WordSize() * 0.6f * Def.FontScaleFor(LayoutBare)) * scale * 1.3f;
                count++;
            }

            // Pass 2: arrange the active items inside the element rect, then the whole-element bg.
            if (count > 0) PlaceItems(count, scale);
            ApplyElementBg(scale);
        }

        // ---- band evaluation ----

        /// <summary>Resolve a sense to its active band's word + colour from the MP-safe drivers on the
        /// snapshot, honouring per-element threshold/word/colour overrides and the atmosphere /
        /// sanitation validity guards. Empty word = nominal or untrusted (hidden). First matching band
        /// in the catalog's severity-descending order wins, so bidirectional senses resolve correctly.</summary>
        private void EvalSense(HudSnapshot s, int sense, Color wordBase, out string word, out Color color, out bool critical)
        {
            word = ""; color = wordBase; critical = false;
            if (s == null || sense < 0) return;
            var info = SenseCatalog.Senses[sense];
            if (info.NeedsAtmosphere && !s.FeltValid) return;       // can't feel the warmth of nothing
            if (info.ServerOnly && !s.SanitationValid) return;      // server-only bowel sim (MP rule)
            // A robot feels almost nothing: the old sampler blanked every felt sense but health for
            // robots and spoke FAILING / DAMAGED instead. That suppression used to arrive baked into
            // the word; now that we evaluate drivers ourselves it has to be reasserted here, or a
            // robot reads human words (this element defaults to Tiers=All when hand-added in F9).
            bool robot = s.IsRobot;
            if (robot && info.RobotSuppressed) return;

            var bands = info.Bands;
            for (int b = 0; b < bands.Length; b++)
            {
                var band = bands[b];
                if (robot && band.RobotWord == null) continue;      // band has no robot meaning
                float th = Def.GetFFor(LayoutBare, ThKeys[sense][b], band.Threshold);
                float val = SenseCatalog.Driver(s, band.Driver);
                bool hit = band.Side == BandSide.High ? val >= th : val <= th;
                if (!hit) continue;
                word = Def.GetSFor(LayoutBare, WKeys[sense][b], robot ? band.RobotWord : band.Word);
                // Critical keeps its own palette default; every other band falls back to the
                // element's authored word colour so one picker re-tints the whole readout.
                Color fallback = band.Sev == SenseSev.Critical
                    ? SenseCatalog.DefaultColor(band.Sev) : wordBase;
                color = ResolveRef(Def.GetSFor(LayoutBare, CKeys[sense][b], ""), fallback,
                    ref _crefMemo[sense][b], ref _colMemo[sense][b]);
                critical = band.Sev == SenseSev.Critical;
                return;
            }
        }

        /// <summary>Resolve a colour ref, memoising only what is safe to memo. An EMPTY ref is just the
        /// fallback (which varies with the authored word colour), and a PALETTE-NAME ref must re-resolve
        /// every frame so it tracks live F9 palette edits — by contract. Only a "#RRGGBBAA" literal is
        /// constant for a given string, and only its parse allocates, so that is the one we cache.</summary>
        private Color ResolveRef(string cref, Color fallback, ref string memoRef, ref Color memoCol)
        {
            if (string.IsNullOrEmpty(cref)) return fallback;
            if (HudPalette.IsPaletteName(cref)) return HudPalette.Resolve(cref, fallback);
            if (!string.Equals(memoRef, cref, StringComparison.Ordinal))
            {
                memoRef = cref;
                memoCol = HudPalette.Resolve(cref, fallback);
            }
            return memoCol;
        }

        private void ApplySenseWord(int row, string word, Color baseCol, bool crit, float dt, float scale, float opacity)
        {
            var t = _words[row];
            word = word ?? "";
            if (word != _current[row]) { _current[row] = word; _flash[row] = 0f; } // band changed — flare
            else _flash[row] += dt;

            bool show = word.Length > 0;
            t.gameObject.SetActive(show);
            if (!show) return;

            HudText.Sync(t);
            t.fontSize = HudText.Size(WordSize() * Def.FontScaleFor(LayoutBare)) * scale;
            // Flare for ~0.4 s, linger bright ~3 s, then settle (Critical bands hold full brightness).
            float f = _flash[row];
            float brightness = f < 0.4f ? Mathf.Lerp(0.4f, 1.35f, f / 0.4f)
                : f < 3.5f ? Mathf.Lerp(1.35f, 1f, (f - 0.4f) / 3.1f)
                : crit ? 1f : 0.62f;
            Color c = baseCol;
            c.a *= Mathf.Clamp01(brightness) * opacity;
            t.color = c;
            HudText.Set(t, word);
        }

        private void ApplyDayWord(string day, float scale, float opacity)
        {
            bool show = day.Length > 0;
            _dayPart.gameObject.SetActive(show);
            if (!show) return;
            HudText.Sync(_dayPart);
            _dayPart.fontSize = HudText.Size(WordSize() * 0.6f * Def.FontScaleFor(LayoutBare)) * scale;
            Color c = ResolveRef(Def.GetSFor(LayoutBare, "dayColor", ""),
                HudPalette.TextDim.Value, ref _dayCref, ref _dayCol);
            c.a *= opacity;
            _dayPart.color = c;
            HudText.Set(_dayPart, day);
        }

        // ---- layout (all element-relative) ----

        private TextMeshProUGUI ItemText(int itemIdx) => itemIdx == DayItem ? _dayPart : _words[itemIdx];

        private void PlaceItems(int count, float scale)
        {
            int mode = Def.GetIFor(LayoutBare, "layoutMode", 0);
            var anchor = (HudAnchor)Def.GetIFor(LayoutBare, "align", (int)HudAnchor.TopRight);
            float gap = Def.GetFFor(LayoutBare, "gap", 4f) * scale;
            float pad = Def.GetFFor(LayoutBare, "pad", 6f) * scale;
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float availW = Mathf.Max(4f, s.x - 2f * pad);

            if (mode == 1) PlaceHorizontal(count, c, s, gap, pad, anchor);
            else if (mode == 2) PlaceGrid(count, c, s, availW, gap, pad, anchor);
            else PlaceVertical(count, c, s, availW, gap, pad, anchor);
        }

        private void PlaceVertical(int count, Vector2 c, Vector2 s, float availW, float gap, float pad, HudAnchor anchor)
        {
            float blockH = gap * Mathf.Max(0, count - 1);
            for (int i = 0; i < count; i++) blockH += _lineH[i];
            float top = BlockTop(VComp(anchor), c, s, pad, blockH);
            var tmp = TmpAlign(anchor);
            float y = top;
            for (int i = 0; i < count; i++)
            {
                float lh = _lineH[i];
                PlaceOne(i, new Vector2(c.x, y - lh * 0.5f), new Vector2(availW, lh), tmp);
                y -= lh + gap;
            }
        }

        private void PlaceHorizontal(int count, Vector2 c, Vector2 s, float gap, float pad, HudAnchor anchor)
        {
            float availW = Mathf.Max(4f, s.x - 2f * pad);
            float total = gap * Mathf.Max(0, count - 1);
            float maxLh = 0f;
            for (int i = 0; i < count; i++)
            {
                // pad is the block's INSET from the element edges; adding it per cell too made the
                // padding slider inflate every word cell instead of just insetting the run.
                float pw = Mathf.Clamp(ItemText(_active[i]).preferredWidth, 8f, availW);
                _cellW[i] = pw;
                total += pw;
                if (_lineH[i] > maxLh) maxLh = _lineH[i];
            }
            int h = HComp(anchor), v = VComp(anchor);
            float leftMost = c.x - s.x * 0.5f + pad;
            float left = h == 0 ? leftMost
                : h == 2 ? c.x + s.x * 0.5f - pad - total
                : c.x - total * 0.5f;
            left = Mathf.Max(left, leftMost);   // overflow runs RIGHT, never off the left edge
            float yc = v == 0 ? c.y + s.y * 0.5f - pad - maxLh * 0.5f
                : v == 2 ? c.y - s.y * 0.5f + pad + maxLh * 0.5f
                : c.y;
            var tmp = TmpAlign(anchor);
            float x = left;
            for (int i = 0; i < count; i++)
            {
                float cw = _cellW[i];
                PlaceOne(i, new Vector2(x + cw * 0.5f, yc), new Vector2(cw, _lineH[i]), tmp);
                x += cw + gap;
            }
        }

        private void PlaceGrid(int count, Vector2 c, Vector2 s, float availW, float gap, float pad, HudAnchor anchor)
        {
            int cols = Mathf.Clamp(Def.GetIFor(LayoutBare, "gridCols", 2), 1, count);
            int rows = Mathf.CeilToInt(count / (float)cols);
            float cellW = Mathf.Max(8f, (availW - gap * (cols - 1)) / cols);
            float cellH = 0f;
            for (int i = 0; i < count; i++) if (_lineH[i] > cellH) cellH = _lineH[i];
            float gridH = rows * cellH + gap * Mathf.Max(0, rows - 1);
            float top = BlockTop(VComp(anchor), c, s, pad, gridH);
            float gridLeft = c.x - availW * 0.5f;   // grid fills the width; the anchor's H sets text alignment
            var tmp = TmpAlign(anchor);
            for (int i = 0; i < count; i++)
            {
                int r = i / cols, col = i % cols;
                float cx = gridLeft + col * (cellW + gap) + cellW * 0.5f;
                float cy = top - r * (cellH + gap) - cellH * 0.5f;
                PlaceOne(i, new Vector2(cx, cy), new Vector2(cellW, cellH), tmp);
            }
        }

        /// <summary>Top edge of the block for a vertical anchor component (0 top / 1 middle / 2 bottom),
        /// inset by <paramref name="pad"/> from the element rect.</summary>
        private static float BlockTop(int vComp, Vector2 c, Vector2 s, float pad, float blockH)
        {
            float topMost = c.y + s.y * 0.5f - pad;
            float top = vComp == 0 ? topMost                                  // top
                : vComp == 2 ? c.y - s.y * 0.5f + pad + blockH                // bottom
                : c.y + blockH * 0.5f;                                        // middle
            // Never START above the element's top inset. With more words firing than the box can
            // hold, middle/bottom anchoring would push the run up out of the rect (and out of the
            // F9 hit-rect/handles). Clamping makes overflow run DOWNWARD instead, which keeps the
            // first words where the author put them and the element still selectable.
            return Mathf.Min(top, topMost);
        }

        private void PlaceOne(int activePos, Vector2 center, Vector2 size, TextAlignmentOptions tmp)
        {
            int idx = _active[activePos];
            var t = ItemText(idx);
            t.alignment = tmp;
            t.rectTransform.sizeDelta = size;
            t.rectTransform.anchoredPosition = center;

            var bg = _itemBg[idx];
            if (_itemBoxOn)
            {
                bg.gameObject.SetActive(true);
                ((RectTransform)bg.transform).anchoredPosition = center;
                bg.SetShape(size.x, size.y,
                    Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)),
                    Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)));
                bg.color = FillColor();
                bg.BorderColor = BorderColor();
                bg.BorderWidth = BorderWidthFor();
                ApplyGlass(bg);
            }
            // (left inactive from the top-of-frame reset when the box mode is off)
        }

        private void ApplyElementBg(float scale)
        {
            if (!Def.GetBFor(LayoutBare, "box", false)) { _bg.gameObject.SetActive(false); return; }
            _bg.gameObject.SetActive(true);
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            ((RectTransform)_bg.transform).anchoredPosition = c;
            _bg.SetShape(s.x, s.y,
                Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)),
                Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)),
                InsetTop(scale), InsetBottom(scale));
            _bg.color = FillColor();
            _bg.BorderColor = BorderColor();
            _bg.BorderWidth = BorderWidthFor();
            ApplyGlass(_bg);
        }

        private float WordSize() => Def.GetFFor(LayoutBare, "wordSize", 20f);
        private float TextOpacity() => Mathf.Clamp01(Def.GetFFor(LayoutBare, "textOpacity", 1f));

        // 9-way anchor → TMP alignment (all nine anchors are real TMP values).
        private static TextAlignmentOptions TmpAlign(HudAnchor a)
        {
            switch (a)
            {
                case HudAnchor.TopLeft: return TextAlignmentOptions.TopLeft;
                case HudAnchor.TopCenter: return TextAlignmentOptions.Top;
                case HudAnchor.TopRight: return TextAlignmentOptions.TopRight;
                case HudAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case HudAnchor.Center: return TextAlignmentOptions.Center;
                case HudAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case HudAnchor.BottomLeft: return TextAlignmentOptions.BottomLeft;
                case HudAnchor.BottomCenter: return TextAlignmentOptions.Bottom;
                case HudAnchor.BottomRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        private static int HComp(HudAnchor a) => (int)a % 3;  // 0 left, 1 centre, 2 right
        private static int VComp(HudAnchor a) => (int)a / 3;  // 0 top, 1 middle, 2 bottom

        // ---- slot mapping (row → sense) ----

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

        private static void ParseOrder(string csv, int[] slots)
        {
            for (int k = 0; k < Rows; k++) slots[k] = -1;
            if (string.IsNullOrEmpty(csv)) return;
            var toks = csv.Split(',');
            int used = 0;
            int slot = 0;
            for (int t = 0; t < toks.Length && slot < Rows; t++, slot++)
            {
                int si = SenseIndex(toks[t]);
                if (si >= 0 && (used & (1 << si)) == 0) { used |= 1 << si; slots[slot] = si; }
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
            _orderCache = null;
        }

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

            int sense = option - 1;
            if (sense < 0)
            {
                slots[row] = -1;
            }
            else
            {
                int other = -1;
                for (int i = 0; i < Rows; i++) if (slots[i] == sense) { other = i; break; }
                if (other >= 0 && other != row) slots[other] = slots[row];
                slots[row] = sense;
            }
            WriteSlots(slots);
        }

        // ---- per-band threshold read/write with same-arm overlap validation ----

        private float GetBandThreshold(int sense, int band, bool bare)
        {
            var bd = SenseCatalog.Senses[sense].Bands[band];
            return Def.GetFFor(bare, ThKeys[sense][band], bd.Threshold);
        }

        /// <summary>Set a band's threshold, clamped to its range AND kept monotonic within its arm
        /// (bands sharing a driver + side): a more-severe band must trigger further out than a
        /// less-severe one, so the author can't invert two levels of the same arm.</summary>
        private void SetBandThreshold(int sense, int band, float v, bool bare)
        {
            var bands = SenseCatalog.Senses[sense].Bands;
            var self = bands[band];
            v = Mathf.Clamp(v, self.ThMin, self.ThMax);
            for (int i = 0; i < bands.Length; i++)
            {
                if (i == band) continue;
                var o = bands[i];
                if (o.Driver != self.Driver || o.Side != self.Side) continue;
                float ot = GetBandThreshold(sense, i, bare);
                bool selfMoreSevere = (int)self.Sev > (int)o.Sev;
                if (self.Side == BandSide.Low)
                    v = selfMoreSevere ? Mathf.Min(v, ot) : Mathf.Max(v, ot);
                else
                    v = selfMoreSevere ? Mathf.Max(v, ot) : Mathf.Min(v, ot);
            }
            Def.SetFFor(bare, ThKeys[sense][band], v);
        }

        // ---- F9 inspector ----

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            bool bare = EditBare(d);

            int lstart = into.Count;
            into.Add(HudProp.Enum("Layout", () => d.GetIFor(bare, "layoutMode", 0),
                v => d.SetIFor(bare, "layoutMode", Mathf.Clamp(v, 0, 2)), LayoutModeNames));
            var an = HudProp.Anchor("Arrange (within element)",
                () => d.GetIFor(bare, "align", (int)HudAnchor.TopRight),
                v => d.SetIFor(bare, "align", v));
            an.Help = "Where the words cluster inside the element box — up/down/left/right/centre. " +
                      "Also sets the text's horizontal alignment.";
            into.Add(an);
            into.Add(HudProp.F("Spacing", () => d.GetFFor(bare, "gap", 4f),
                v => d.SetFFor(bare, "gap", Mathf.Clamp(v, 0f, 60f)), 0f, 60f));
            into.Add(HudProp.I("Grid columns", () => d.GetIFor(bare, "gridCols", 2),
                v => d.SetIFor(bare, "gridCols", Mathf.Clamp(v, 1, 6)), 1, 6));
            into.Add(HudProp.F("Inner padding", () => d.GetFFor(bare, "pad", 6f),
                v => d.SetFFor(bare, "pad", Mathf.Clamp(v, 0f, 60f)), 0f, 60f));
            into.Add(HudProp.Bool("Show day-part word", () => d.GetBFor(bare, "showDay", true),
                v => d.SetBFor(bare, "showDay", v)));
            for (int i = lstart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            int astart = into.Count;
            into.Add(HudProp.Color("Word colour", () => d.GetSFor(bare, "wordColor", ""),
                v => d.SetSFor(bare, "wordColor", Empty(v)), () => HudPalette.BareWord.Value));
            into.Add(HudProp.F("Text opacity", () => d.GetFFor(bare, "textOpacity", 1f),
                v => d.SetFFor(bare, "textOpacity", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.Color("Day-part colour", () => d.GetSFor(bare, "dayColor", ""),
                v => d.SetSFor(bare, "dayColor", Empty(v)), () => HudPalette.TextDim.Value));
            into.Add(HudProp.F("Felt-sense word size", () => d.GetFFor(bare, "wordSize", 20f),
                v => d.SetFFor(bare, "wordSize", v), 12f, 36f));
            into.Add(HudProp.Bool("Background box (whole element)", () => d.GetBFor(bare, "box", false),
                v => d.SetBFor(bare, "box", v)));
            into.Add(HudProp.Bool("Box each sense", () => d.GetBFor(bare, "itemBox", false),
                v => d.SetBFor(bare, "itemBox", v)));
            for (int i = astart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;

            into.Add(HudProp.Header("Senses (row = top → bottom)"));
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                var pr = HudProp.Enum("Row " + (row + 1), () => GetSlotOption(row),
                    v => SetSlotOption(row, v), SlotOptions);
                if (row == 0)
                    pr.Help = "Which sense shows in this row. '— none —' removes it. Picking a sense " +
                              "already in another row swaps the two. All eight can be placed at once.";
                into.Add(pr);
            }

            // Each sense gets its OWN nested tab, so the ~60-row block reads as a compact browser
            // instead of a scrolling wall. Only the selected sense's bands draw; ImGui remembers
            // which tab is open, so nothing is stored on the element.
            into.Add(HudProp.Header("Sense words, colours & levels"));
            var senses = SenseCatalog.Senses;
            var pages = new List<HudProp>(senses.Length);
            for (int si = 0; si < senses.Length; si++)
            {
                int sense = si;
                var info = senses[si];
                var bands = info.Bands;
                var page = new List<HudProp>(bands.Length * 3 + 1);
                if (info.ServerOnly)
                    page.Add(HudProp.Header("Server-simulated — stays silent on a multiplayer client"));
                for (int bi = 0; bi < bands.Length; bi++)
                {
                    int band = bi;
                    var bd = bands[bi];
                    string sideLabel = (bd.Side == BandSide.High ? ">= " : "<= ");
                    // StableId pins each row's ImGui identity to its unique PARAM KEY, not its label:
                    // two bands may legitimately share a word (CHOKING from no pressure vs no oxygen),
                    // and label-derived ids would then collide and cross-wire the two rows.
                    var wp = HudProp.Text(bd.EditorLabel + " — text",
                        () => d.GetSFor(bare, WKeys[sense][band], bd.Word),
                        v => d.SetSFor(bare, WKeys[sense][band], string.IsNullOrEmpty(v) ? null : v));
                    wp.StableId = WKeys[sense][band];
                    page.Add(wp);

                    var cp = HudProp.Color(bd.EditorLabel + " — colour",
                        () => d.GetSFor(bare, CKeys[sense][band], ""),
                        v => d.SetSFor(bare, CKeys[sense][band], Empty(v)), ColorFallbackFor(bd.Sev));
                    cp.StableId = CKeys[sense][band];
                    page.Add(cp);

                    var tp = HudProp.F(bd.EditorLabel + " — level (" + sideLabel + bd.Unit + ")",
                        () => GetBandThreshold(sense, band, bare),
                        v => SetBandThreshold(sense, band, v, bare), bd.ThMin, bd.ThMax);
                    tp.StableId = ThKeys[sense][band];
                    page.Add(tp);
                }
                pages.Add(HudProp.TabPage(info.Name, page));
            }
            into.Add(HudProp.TabGroup("senses", pages));
        }

        private static string Empty(string v) => string.IsNullOrEmpty(v) ? null : v;

        // Single source for "severity -> default colour" (SenseCatalog.DefaultColor); EvalSense uses
        // the same call, so the rule lives in one place instead of three.
        private static Func<Color> ColorFallbackFor(SenseSev sev)
            => () => SenseCatalog.DefaultColor(sev);
    }
}
