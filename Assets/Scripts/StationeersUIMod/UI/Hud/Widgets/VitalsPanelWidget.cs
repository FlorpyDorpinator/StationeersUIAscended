using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// A vanilla-faithful vitals list (RECON of PlayerStateWindow's needs card): one row per
    /// active need, each a GAME icon (via <see cref="Core.VanillaIcons"/>) plus a value. No
    /// bars — icons carry the meaning and the value is a bare percentage (or, in words mode,
    /// a coloured level word). Membership is dynamic per frame, mirroring vanilla:
    ///   HUNGER + WATER always; TOILET only when sanitation is valid and above 25%; HEALTH
    ///   only when damaged. The hunger row also shows 1..4 food-quality stars.
    ///
    /// The panel resizes to only the rows that are live: the optional background box takes the
    /// height of the visible rows, and rows flow top-to-bottom from the element rect's top
    /// edge. Because the visible SET only changes when gear/needs cross a boundary, geometry is
    /// re-flowed on a cheap signature change (like <see cref="SuitChipsWidget"/>) — steady state
    /// just pushes colours, icons (which late-resolve — the game singleton is world-only, so the
    /// icon channel retries every frame until the sprite lands) and the odd changed number.
    ///
    /// Words mode (bare tier / suit-off) trades each percentage for a level word, and can add
    /// two param-driven rows — pressure and temperature — so the bare panel can narrate the
    /// environment in plain language.
    /// </summary>
    internal sealed class VitalsPanelWidget : HudElementView
    {
        // Row identity. Order here is the top-to-bottom draw order.
        private const int Hunger = 0, Water = 1, Toilet = 2, Health = 3, Pressure = 4, Temp = 5;
        private const int RowCount = 6;

        // The game-icon key each row asks VanillaIcons for.
        private static readonly string[] IconKeys = { "Hunger", "Water", "Toilet", "Health", "Pressure", "Temp" };

        private sealed class Row
        {
            public Image Icon;
            public RectTransform IconRt;
            public TextMeshProUGUI Value;
            public RectTransform ValueRt;
            public bool IconIsOverride; // PNG override => tint accent; vanilla art keeps white
            // Percentage-string cache: rebuilt only when the shown integer changes.
            public int ShownInt;
            public string Text;
        }

        private readonly Row[] _rows = new Row[RowCount];

        // Up to four food-quality stars, drawn to the right of the hunger row.
        private const int StarCount = 4;
        private readonly Image[] _stars = new Image[StarCount];

        private PanelGraphic _box;
        // Thin grey separators drawn BETWEEN visible rows (RowCount-1 max).
        private readonly PanelGraphic[] _seps = new PanelGraphic[RowCount - 1];
        private int _sig = -1;

        protected override void BuildContent(RectTransform root)
        {
            _box = MakePanel(root, "Box");

            for (int i = 0; i < _seps.Length; i++)
            {
                _seps[i] = MakeBar(root, "Sep" + i); // borderless thin line
                _seps[i].enabled = false;
            }

            for (int i = 0; i < RowCount; i++)
            {
                var row = new Row { ShownInt = int.MinValue, Text = "--" };
                row.Icon = MakeIcon(root, "Icon" + i);
                row.Icon.enabled = false; // born hidden: a sprite-less Image draws a white box
                row.IconRt = row.Icon.rectTransform;
                row.Value = HudText.Make(root, "Value" + i, 15f, TextAlignmentOptions.MidlineRight);
                row.ValueRt = row.Value.rectTransform;
                _rows[i] = row;
            }

            for (int i = 0; i < StarCount; i++)
            {
                _stars[i] = MakeIcon(root, "Star" + i);
                _stars[i].enabled = false;
            }
        }

        public override void Layout(float scale)
        {
            // Rect / scale / a row-height knob may have moved; force a re-flow on the next
            // update, where the live visible set (and thus the box height) is known.
            Root.anchoredPosition = Vector2.zero;
            _sig = -1;
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            _lastScale = scale;
            bool words = Def.GetB("words", false);

            // --- membership (vanilla PlayerStateWindow logic) ---
            bool vHunger = true;
            bool vWater = true;
            bool vToilet = s != null && s.SanitationValid && s.Sanitation01 > 0.25f;
            // Vanilla shows HEALTH on IsDamaged() = whole-body OR any organ damage
            // (Entity.cs:1753). HealthRatio is body-only, so also check the organ regions.
            bool vHealth = s != null && (s.HealthRatio < 1.0f
                || s.DamageHead01 > 0.001f || s.DamageChest01 > 0.001f || s.DamageBody01 > 0.001f);
            bool vPressure = words && Def.GetB("rowPressure", false);
            bool vTemp = words && Def.GetB("rowTemp", false);

            bool showBox = Def.GetB("box", true);

            int sig = (vHunger ? 1 : 0) | (vWater ? 2 : 0) | (vToilet ? 4 : 0)
                | (vHealth ? 8 : 0) | (vPressure ? 16 : 0) | (vTemp ? 32 : 0)
                | (words ? 64 : 0) | (showBox ? 128 : 0);
            if (sig != _sig)
            {
                _sig = sig;
                Reflow(scale, vHunger, vWater, vToilet, vHealth, vPressure, vTemp, showBox);
            }

            // --- chrome ---
            _box.enabled = showBox;
            if (showBox)
            {
                _box.color = FillColor();
                _box.BorderColor = BorderColor();
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }

            Color accent = TextColor();
            Color dim = HudPalette.TextDim.Value;

            // --- rows ---
            PushNeedRow(Hunger, vHunger, s != null ? s.FoodRatio : 0f, words, accent, dim,
                "FED", "PECKISH", "STARVING");
            PushNeedRow(Water, vWater, s != null ? s.WaterRatio : 0f, words, accent, dim,
                "HYDRATED", "THIRSTY", "PARCHED");
            // Toilet has no vanilla level-word, so it stays numeric even in words mode.
            PushRowNumeric(Toilet, vToilet, s != null ? s.Sanitation01 : 0f, accent, dim);
            PushNeedRow(Health, vHealth, s != null ? s.HealthRatio : 0f, words, accent, dim,
                "OK", "HURT", "CRITICAL");

            PushPressureRow(vPressure, s, dim);
            PushTempRow(vTemp, s, dim);

            // The hunger row's ICON is the live food-quality badge (burger + stars in ONE
            // vanilla image, exactly what vanilla shows) — no separate badge slot, which
            // had put two burgers on the row (play-test).
            if (vHunger) PushFoodBadge(_rows[Hunger]);
            for (int i = 0; i < StarCount; i++) _stars[i].enabled = false;
        }

        /// <summary>Swap the hunger row's icon to vanilla's FoodQualityToggle badge for the
        /// current quality band. Re-checked each frame (band changes as food quality does);
        /// Image.sprite is dirty-guarded so a steady band costs nothing.</summary>
        private void PushFoodBadge(Row row)
        {
            try
            {
                float quality = 0.5f;
                var h = Core.Guards.LocalHuman;
                if (h != null) quality = h.FoodQuality;
                var badge = Core.VanillaIcons.FoodQualityStar(quality);
                if (badge != null)
                {
                    row.Icon.sprite = badge;
                    row.IconIsOverride = false;
                    row.Icon.color = Color.white;
                }
            }
            catch { }
        }

        // ---- row pushers ----

        /// <summary>A need row: icon + either a coloured level word (words mode) or a NN%
        /// value coloured by the same good/warn/crit band.</summary>
        private void PushNeedRow(int i, bool visible, float ratio01, bool words,
            Color accent, Color dim, string good, string warn, string crit)
        {
            var row = _rows[i];
            ResolveIcon(row, i, accent);
            row.Icon.enabled = visible && row.Icon.sprite != null;
            row.Value.enabled = visible;
            if (!visible) return;

            SyncValueFont(row, _lastScale);

            int band = ratio01 > 0.66f ? 0 : ratio01 > 0.33f ? 1 : 2;
            Color col = band == 0 ? HudPalette.Good.Value
                : band == 1 ? HudPalette.Warn.Value : HudPalette.Critical.Value;

            if (words)
            {
                row.Value.color = col;
                HudText.Set(row.Value, band == 0 ? good : band == 1 ? warn : crit);
            }
            else
            {
                row.Value.color = col;
                HudText.Set(row.Value, NumberText(row, ratio01));
            }
        }

        /// <summary>A numeric-only row (toilet): icon + NN%, neutral accent colour.</summary>
        private void PushRowNumeric(int i, bool visible, float ratio01, Color accent, Color dim)
        {
            var row = _rows[i];
            ResolveIcon(row, i, accent);
            row.Icon.enabled = visible && row.Icon.sprite != null;
            row.Value.enabled = visible;
            if (!visible) return;

            SyncValueFont(row, _lastScale);
            row.Value.color = accent;
            HudText.Set(row.Value, NumberText(row, ratio01));
        }

        /// <summary>Words-mode pressure narration (icon "Pressure"). Bands: &lt;20 kPa THIN AIR,
        /// &gt;260 kPa HIGH PRESSURE, otherwise PRESSURIZED. Uses the felt/ambient pressure.</summary>
        private void PushPressureRow(bool visible, HudSnapshot s, Color dim)
        {
            var row = _rows[Pressure];
            ResolveIcon(row, Pressure, TextColor());
            row.Icon.enabled = visible && row.Icon.sprite != null;
            row.Value.enabled = visible;
            if (!visible) return;

            SyncValueFont(row, _lastScale);

            bool valid = s != null && s.HasAtmosphere;
            float kPa = valid ? s.PressureKPa : 0f;
            string word;
            Color col;
            if (!valid || kPa < 20f) { word = "THIN AIR"; col = HudPalette.Critical.Value; }
            else if (kPa > 260f) { word = "HIGH PRESSURE"; col = HudPalette.Warn.Value; }
            else { word = "PRESSURIZED"; col = HudPalette.Good.Value; }
            row.Value.color = col;
            HudText.Set(row.Value, word);
        }

        /// <summary>Words-mode temperature narration. The icon prefers vanilla's conditional
        /// hot/cold sprite (TempStateIcon) and falls back to the neutral "Temp" toggle; the word
        /// is red when hot, blue when cold (per the brief's lerp), good when comfortable.</summary>
        private void PushTempRow(bool visible, HudSnapshot s, Color dim)
        {
            var row = _rows[Temp];
            // Temp icon is state-driven (hot/cold/neutral), so recompute rather than cache-null.
            if (visible)
            {
                Sprite sp = null;
                try
                {
                    var over = Core.HudIconStore.TryGet("Temp");
                    if (over != null) { sp = over; row.IconIsOverride = true; }
                    else
                    {
                        row.IconIsOverride = false;
                        if (s != null && s.FeltValid) sp = Core.VanillaIcons.TempStateIcon(s.FeltTempC);
                        if (sp == null) sp = Core.VanillaIcons.TryGet("Temp");
                    }
                }
                catch { }
                if (sp != null) row.Icon.sprite = sp;
                row.Icon.color = row.IconIsOverride ? TextColor() : Color.white;
            }
            row.Icon.enabled = visible && row.Icon.sprite != null;
            row.Value.enabled = visible;
            if (!visible) return;

            SyncValueFont(row, _lastScale);

            bool valid = s != null && s.FeltValid;
            if (!valid)
            {
                row.Value.color = HudPalette.TextDim.Value;
                HudText.Set(row.Value, "--");
                return;
            }

            float c = s.FeltTempC;
            Color blue = new Color(0.4f, 0.7f, 1f, 1f);
            string word;
            Color col;
            if (c > 40f) { word = "HOT"; col = HudPalette.Critical.Value; }
            else if (c > 25f) { word = "WARM"; col = HudPalette.Warn.Value; }
            else if (c < 0f) { word = "FREEZING"; col = blue; }
            else if (c < 10f) { word = "COLD"; col = blue; }
            else { word = "COMFORTABLE"; col = HudPalette.Good.Value; }
            row.Value.color = col;
            HudText.Set(row.Value, word);
        }

        /// <summary>The food-quality badge to the right of the hunger row. Vanilla's
        /// FoodQualityToggle.Sprites[0..3] are COMPOSITE badges (the whole 1★..4★ rating in a
        /// single image), not individual stars — vanilla shows exactly one via SetImage(band).
        /// So we draw ONE badge (slot 0) and keep the other slots dark rather than tiling the
        /// same multi-star art four times (review finding).</summary>
        private void PushStars(bool hungerVisible, Color accent, Color dim)
        {
            for (int i = 0; i < StarCount; i++) _stars[i].enabled = false;
            if (!hungerVisible) return;

            float quality = 0.5f;
            try
            {
                var h = Core.Guards.LocalHuman;
                if (h != null) quality = h.FoodQuality;
            }
            catch { }

            Sprite badge = Core.VanillaIcons.FoodQualityStar(quality);
            if (badge == null) return;
            _stars[0].sprite = badge;
            _stars[0].enabled = true;
            _stars[0].color = Color.white; // vanilla art keeps its native colour
        }

        // ---- helpers ----

        /// <summary>Late-resolving vanilla icon: retry until the world singleton hands over the
        /// sprite. A user PNG override (if present) wins and is tinted with the accent; vanilla
        /// art keeps its native white.</summary>
        private void ResolveIcon(Row row, int i, Color accent)
        {
            if (row.Icon.sprite == null)
            {
                string key = IconKeys[i];
                Sprite sp = null;
                try
                {
                    sp = Core.HudIconStore.TryGet(key);
                    if (sp != null) row.IconIsOverride = true;
                    else { row.IconIsOverride = false; sp = Core.VanillaIcons.TryGet(key); }
                }
                catch { }
                if (sp != null) row.Icon.sprite = sp;
            }
            row.Icon.color = row.IconIsOverride ? accent : Color.white;
        }

        /// <summary>NN% string, rebuilt only when the rounded integer changes — no per-frame
        /// allocation for a steady value.</summary>
        private static string NumberText(Row row, float ratio01)
        {
            int v = (int)(ratio01 * 100f);
            if (v != row.ShownInt)
            {
                row.ShownInt = v;
                row.Text = v.ToString(CultureInfo.InvariantCulture) + "%";
            }
            return row.Text;
        }

        private float _lastScale = 1f;

        private void SyncValueFont(Row row, float scale)
        {
            HudText.Sync(row.Value);
            float px = Def.GetF("rowHeight", 30f) * 0.5f;
            row.Value.fontSize = HudText.Size(px * Def.FontScale) * scale;
        }

        // ---- layout ----

        private void Reflow(float scale, bool vHunger, bool vWater, bool vToilet,
            bool vHealth, bool vPressure, bool vTemp, bool showBox)
        {
            _lastScale = scale;
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float rowH = Def.GetF("rowHeight", 42f) * scale;
            float pad = 5f * scale;
            float iconScale = Def.GetF("iconScale", 0.92f);

            // Build the visible-order list.
            var order = new List<int>(RowCount);
            if (vHunger) order.Add(Hunger);
            if (vWater) order.Add(Water);
            if (vToilet) order.Add(Toilet);
            if (vHealth) order.Add(Health);
            if (vPressure) order.Add(Pressure);
            if (vTemp) order.Add(Temp);
            int n = order.Count;

            float top = c.y + s.y * 0.5f;                 // stack grows down from the top edge
            float stackH = n * rowH;

            // Box takes the height of the visible rows, top-aligned in the element rect.
            if (showBox)
            {
                float boxCy = top - stackH * 0.5f;
                ((RectTransform)_box.transform).anchoredPosition = new Vector2(c.x, boxCy);
                _box.SetShape(s.x, Mathf.Max(2f, stackH),
                    Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL));
            }

            float left = c.x - s.x * 0.5f + pad;
            float right = c.x + s.x * 0.5f - pad;
            float iconSz = Mathf.Clamp(rowH * iconScale, 6f, rowH * 1.15f);

            // Thin grey separators between adjacent visible rows.
            var sepCol = HudPalette.TextDim.Value; sepCol.a *= 0.4f;
            float sepW = s.x - pad * 2f;
            for (int i = 0; i < _seps.Length; i++)
            {
                bool on = i < n - 1;
                _seps[i].enabled = on;
                if (!on) continue;
                float sy = top - rowH * (i + 1);
                ((RectTransform)_seps[i].transform).anchoredPosition = new Vector2(c.x, sy);
                _seps[i].SetShape(sepW, Mathf.Max(1f, 1.4f * scale), 0.5f);
                _seps[i].color = sepCol;
                _seps[i].BorderWidth = 0f;
            }

            // (No star strip: the hunger row's icon IS the food-quality badge.)
            for (int k = 0; k < n; k++)
            {
                int id = order[k];
                var row = _rows[id];
                float rowCy = top - rowH * (k + 0.5f);

                row.IconRt.anchoredPosition = new Vector2(left + iconSz * 0.5f, rowCy);
                row.IconRt.sizeDelta = new Vector2(iconSz, iconSz);

                float textLeft = left + iconSz + 6f * scale;
                float tw = Mathf.Max(12f, right - textLeft);
                row.ValueRt.anchoredPosition = new Vector2(textLeft + tw * 0.5f, rowCy);
                row.ValueRt.sizeDelta = new Vector2(tw, rowH);
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Background box", () => d.GetB("box", true), v => d.SetB("box", v)));
            into.Add(HudProp.Bool("Words mode (bare)", () => d.GetB("words", false), v => d.SetB("words", v)));
            into.Add(HudProp.Bool("Pressure row (words)", () => d.GetB("rowPressure", false), v => d.SetB("rowPressure", v)));
            into.Add(HudProp.Bool("Temp row (words)", () => d.GetB("rowTemp", false), v => d.SetB("rowTemp", v)));
            into.Add(HudProp.F("Row height", () => d.GetF("rowHeight", 42f),
                v => d.SetF("rowHeight", Mathf.Clamp(v, 16f, 96f)), 16f, 96f));
            into.Add(HudProp.F("Icon size (rel)", () => d.GetF("iconScale", 0.92f),
                v => d.SetF("iconScale", Mathf.Clamp(v, 0.2f, 1.15f)), 0.2f, 1.15f));
        }
    }
}
