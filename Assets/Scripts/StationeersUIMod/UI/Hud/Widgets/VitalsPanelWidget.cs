using System.Collections.Generic;
using System.Globalization;
using Assets.Scripts;
using Assets.Scripts.Localization2;
using Assets.Scripts.UI;
using StationeersUIMod.UI.Menu.Tutorial;
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
    ///   only when damaged; COGNITION only when stunned (Stun > 0.5, showing the stun %). The
    ///   hunger row also shows 1..4 food-quality stars.
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
        private const int Hunger = 0, Water = 1, Toilet = 2, Health = 3, Cognition = 4, Pressure = 5, Temp = 6;
        private const int RowCount = 7;

        // The game-icon key each row asks VanillaIcons for.
        private static readonly string[] IconKeys = { "Hunger", "Water", "Toilet", "Health", "Cognition", "Pressure", "Temp" };

        private sealed class Row
        {
            public Image Icon;
            public RectTransform IconRt;
            // Some needs have no vanilla sprite (the toilet/bowel need) — those draw a
            // procedural line glyph instead. Exactly one channel is enabled per row.
            public HudIconGraphic Glyph;
            public RectTransform GlyphRt;
            public bool UsesGlyph;
            public TextMeshProUGUI Value;
            public RectTransform ValueRt;
            public bool IconIsOverride; // PNG override => tint accent; vanilla art keeps white
            // Percentage-string cache: rebuilt only when the shown integer changes.
            public int ShownInt;
            public string Text;
            // The left-column NAME label, shown ONLY in icons-as-words mode so the percentage can
            // right-align in its own column (a neat "Hunger 3/4 ....... 37%" table). Its string is
            // rebuilt only when the food-quality level changes (the name itself is constant).
            public TextMeshProUGUI Name;
            public RectTransform NameRt;
            public int NameLevel;
            public string NameStr;
        }

        private readonly Row[] _rows = new Row[RowCount];

        // Up to four food-quality stars, drawn to the right of the hunger row.
        private const int StarCount = 4;
        private readonly Image[] _stars = new Image[StarCount];

        private PanelGraphic _box;
        // Thin grey separators drawn BETWEEN visible rows (RowCount-1 max).
        private readonly PanelGraphic[] _seps = new PanelGraphic[RowCount - 1];
        private CanvasGroup _hudRootGroup;
        private VitalsTooltipReceiver _tooltipReceiver;
        private Rect _tooltipRect;
        private bool _tooltipRectValid;
        private int _sig = -1;

        // The vitals frame takes the trapezoid insets (base supplies the sliders); the thin
        // row separators stay rectangular.
        protected override bool SupportsTrapezoid => true;

        // The frame is a real PanelGraphic (fill/border/glass all apply).
        protected override bool SupportsPanelAppearance => true;

        // The frame is optional, "box" key, default ON.
        protected override bool OptionalPanelBackgroundIsOff => !Def.GetBFor(EditBare(Def), "box", true);

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
                var row = new Row { ShownInt = int.MinValue, Text = "--", NameLevel = int.MinValue };
                row.Icon = MakeIcon(root, "Icon" + i);
                row.Icon.enabled = false; // born hidden: a sprite-less Image draws a white box
                row.IconRt = row.Icon.rectTransform;

                var glyphGo = new GameObject("Glyph" + i, typeof(RectTransform));
                glyphGo.transform.SetParent(root, false);
                row.Glyph = glyphGo.AddComponent<HudIconGraphic>();
                row.Glyph.raycastTarget = false;
                row.Glyph.enabled = false;
                glyphGo.AddComponent<VisorWarp>();
                row.GlyphRt = (RectTransform)glyphGo.transform;
                row.GlyphRt.anchorMin = row.GlyphRt.anchorMax = new Vector2(0.5f, 0.5f);

                row.Value = HudText.Make(root, "Value" + i, 15f, TextAlignmentOptions.MidlineRight);
                row.ValueRt = row.Value.rectTransform;
                // Left-column name label (icons-as-words mode only), born hidden.
                row.Name = HudText.Make(root, "Name" + i, 15f, TextAlignmentOptions.MidlineLeft);
                row.Name.enabled = false;
                row.NameRt = row.Name.rectTransform;
                _rows[i] = row;
            }

            for (int i = 0; i < StarCount; i++)
            {
                _stars[i] = MakeIcon(root, "Star" + i);
                _stars[i].enabled = false;
            }

            // Keep the HUD's established input-transparent contract: this is a MANUAL logical-rect
            // hover, not a GraphicRaycaster target. UpdateHover inverse-warps the pointer through the
            // same path as F9 and the HUD slot boxes, so mesh curvature cannot detach the hit area.
            _tooltipReceiver = new VitalsTooltipReceiver(this);
            _hudRootGroup = root.parent != null ? root.parent.GetComponent<CanvasGroup>() : null;
        }

        public override void Layout(float scale)
        {
            // Rect / scale / a row-height knob may have moved; force a re-flow on the next
            // update, where the live visible set (and thus the box height) is known.
            Root.anchoredPosition = Vector2.zero;
            _sig = -1;
            _tooltipRectValid = false;
            if (_tooltipReceiver != null) _tooltipReceiver.UpdateHover(false, default(Rect));
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            _lastScale = scale;
            bool words = Def.GetBFor(LayoutBare, "words", false);

            // --- membership (vanilla PlayerStateWindow logic) ---
            bool vHunger = true;
            bool vWater = true;
            bool vToilet = s != null && s.SanitationValid && s.Sanitation01 > 0.25f;
            // Vanilla shows HEALTH on IsDamaged() = whole-body OR any organ damage
            // (Entity.cs:1753). HealthRatio is body-only, so also check the organ regions.
            bool vHealth = s != null && (s.HealthRatio < 1.0f
                || s.DamageHead01 > 0.001f || s.DamageChest01 > 0.001f || s.DamageBody01 > 0.001f);
            // Cognition (consciousness): vanilla shows this row ONLY when DamageState.Stun > 0.5,
            // displaying the stun value itself — NOT whenever oxygen dips (PlayerStateWindow.cs
            // :308-317). Stun01 = DamageState.Stun / 100, so Stun01*100 == the vanilla Stun scale.
            bool vCognition = s != null && s.Stun01 * 100f > 0.5f;
            bool vPressure = words && Def.GetBFor(LayoutBare, "rowPressure", false);
            bool vTemp = words && Def.GetBFor(LayoutBare, "rowTemp", false);

            bool showBox = Def.GetBFor(LayoutBare, "box", true);
            bool rowLines = Def.GetBFor(LayoutBare, "rowLines", true);
            bool showIcons = Def.GetBFor(LayoutBare, "icons", true);
            // "Icons as words": hides every row icon AND prefixes each value with the need's
            // NAME (e.g. "Thirst 37%", "Hunger 2/4 100%"). Distinct from "Words mode", which
            // shows a level WORD in place of the number and keeps the icon.
            bool iconWords = Def.GetBFor(LayoutBare, "iconWords", false);

            // rowLines/icons feed the signature so toggling either re-flows (separators live in
            // Reflow, and the text column shifts left when the icons are gone). iconWords does the
            // same (icons hidden + gutter reclaimed), so it feeds the signature too.
            int sig = (vHunger ? 1 : 0) | (vWater ? 2 : 0) | (vToilet ? 4 : 0)
                | (vHealth ? 8 : 0) | (vCognition ? 256 : 0) | (vPressure ? 16 : 0) | (vTemp ? 32 : 0)
                | (words ? 64 : 0) | (showBox ? 128 : 0) | (rowLines ? 512 : 0) | (showIcons ? 1024 : 0)
                | (iconWords ? 2048 : 0);
            if (sig != _sig)
            {
                _sig = sig;
                Reflow(scale, vHunger, vWater, vToilet, vHealth, vCognition, vPressure, vTemp, showBox);
            }
            if (_tooltipReceiver != null)
                _tooltipReceiver.UpdateHover(TooltipCanShow(), _tooltipRect);

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

            // The hunger row's "N/4" level in icons-as-words mode: vanilla's own food-quality
            // banding (<0.45/<0.7/<0.9 -> 1..4, PlayerStateWindow.GetFoodQualityIndex). FoodQuality
            // is networked (MP-safe). Only computed when iconWords is on.
            int foodLevel = 0;
            if (iconWords)
            {
                float q = 0.5f;
                try { var h = Core.Guards.LocalHuman; if (h != null) q = h.FoodQuality; } catch { }
                foodLevel = q < 0.45f ? 1 : q < 0.7f ? 2 : q < 0.9f ? 3 : 4;
            }

            // --- rows ---
            PushNeedRow(Hunger, vHunger, s != null ? s.FoodRatio : 0f, words, iconWords, accent, dim,
                "FED", "PECKISH", "STARVING", "Hunger", foodLevel);
            PushNeedRow(Water, vWater, s != null ? s.WaterRatio : 0f, words, iconWords, accent, dim,
                "HYDRATED", "THIRSTY", "PARCHED", "Thirst", 0);
            // Toilet: Sanitation01 is the WASTE ratio (high = need to go, decompile Human.cs
            // :2807 GetWasteRatio + IsSanitationCritical = ratio > threshold). Invert it to a
            // "holding capacity" reserve so it reads like the other needs (high % = fine,
            // green RELIEVED; low % = urgent, red DESPERATE) in both bare words and suited %.
            PushNeedRow(Toilet, vToilet, s != null ? (1f - s.Sanitation01) : 1f, words, iconWords, accent, dim,
                "RELIEVED", "UNEASY", "DESPERATE", "Toilet", 0);
            PushNeedRow(Health, vHealth, s != null ? s.HealthRatio : 0f, words, iconWords, accent, dim,
                "OK", "HURT", "CRITICAL", "Health", 0);
            // Cognition/consciousness: vanilla's readout is the STUN percentage (rising = blacking
            // out), shown only while stunned. Bands read on (1 - stun) so a low stun is ALERT/green
            // and a rising stun fades to DAZED/FADING, but the NUMBER shown is the stun % itself.
            PushNeedRow(Cognition, vCognition, s != null ? (1f - s.Stun01) : 1f, words, iconWords, accent, dim,
                "ALERT", "DAZED", "FADING", "Cognition", 0, displayRatio01: s != null ? s.Stun01 : 0f);

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
        private void PushNeedRow(int i, bool visible, float ratio01, bool words, bool iconWords,
            Color accent, Color dim, string good, string warn, string crit, string wordName, int level,
            float? displayRatio01 = null)
        {
            var row = _rows[i];
            ResolveIcon(row, i, accent);
            EnableRowIcon(row, visible);
            row.Value.enabled = visible;
            if (row.Name != null) row.Name.enabled = visible && iconWords;
            if (!visible) return;

            SyncValueFont(row, _lastScale);

            // Bands/words select on ratio01 (high = good). A row whose displayed NUMBER differs from
            // its banding value (Cognition bands on 1-stun but shows the stun %) passes displayRatio01.
            float shown = displayRatio01 ?? ratio01;
            int band = ratio01 > 0.66f ? 0 : ratio01 > 0.33f ? 1 : 2;
            Color col = band == 0 ? HudPalette.Good.Value
                : band == 1 ? HudPalette.Warn.Value : HudPalette.Critical.Value;
            row.Value.color = col;

            if (iconWords)
            {
                // Two columns: the NAME (+ the hunger N/4) hugs the LEFT, and the percentage stays
                // in row.Value RIGHT-aligned, so every "%" lines up in a neat vertical column.
                if (row.Name != null) { row.Name.color = col; HudText.Set(row.Name, ComposeName(row, wordName, level)); }
                HudText.Set(row.Value, NumberText(row, shown));
            }
            else if (words)
                HudText.Set(row.Value, band == 0 ? good : band == 1 ? warn : crit);
            else
                HudText.Set(row.Value, NumberText(row, shown));
        }

        /// <summary>A numeric-only row (toilet): icon + NN%, neutral accent colour.</summary>
        private void PushRowNumeric(int i, bool visible, float ratio01, Color accent, Color dim)
        {
            var row = _rows[i];
            ResolveIcon(row, i, accent);
            EnableRowIcon(row, visible);
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
            EnableRowIcon(row, visible);
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
            EnableRowIcon(row, visible);
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
            // The bowel/toilet need: use the GAME's own toilet icon (added in the Toilet
            // Update, grabbed at runtime off PlayerStateWindow.WastePercentageObject). A PNG
            // override wins; the procedural toilet glyph is only a last resort so the row is
            // never a white box before the world singleton exists.
            if (i == Toilet)
            {
                var over = Core.HudIconStore.TryGet(IconKeys[i]);
                Sprite sp = over != null ? over : Core.VanillaIcons.TryGet("toilet");
                if (sp != null)
                {
                    row.UsesGlyph = false;
                    row.IconIsOverride = over != null;
                    row.Icon.sprite = sp;
                    row.Icon.color = over != null ? accent : Color.white;
                    return;
                }
                row.UsesGlyph = true;         // fallback until the game icon resolves
                row.Glyph.Kind = HudIconKind.Toilet;
                row.Glyph.color = accent;
                return;
            }

            row.UsesGlyph = false;
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

        /// <summary>Enable exactly the icon channel the row uses (sprite OR glyph), or neither
        /// when hidden/unresolved. The "Row icons" checkbox suppresses BOTH channels so the panel
        /// can read as bare value-only rows.</summary>
        private void EnableRowIcon(Row row, bool visible)
        {
            bool showIcon = visible && Def.GetBFor(LayoutBare, "icons", true)
                && !Def.GetBFor(LayoutBare, "iconWords", false);
            row.Icon.enabled = showIcon && !row.UsesGlyph && row.Icon.sprite != null;
            if (row.Glyph != null) row.Glyph.enabled = showIcon && row.UsesGlyph;
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

        /// <summary>The left-column NAME string for icons-as-words mode: "Name" (or "Name N/4"
        /// when level &gt; 0, the hunger row). Rebuilt only when the level changes — the name is
        /// constant — so a steady value never allocates. All ASCII (letters/digits/'/'/space).</summary>
        private static string ComposeName(Row row, string name, int level)
        {
            if (level != row.NameLevel)
            {
                row.NameLevel = level;
                row.NameStr = level > 0 ? name + " " + level + "/4" : name;
            }
            return row.NameStr;
        }

        private float _lastScale = 1f;

        private void SyncValueFont(Row row, float scale)
        {
            HudText.Sync(row.Value);
            // Default MUST match Reflow's and the inspector's (42f). At 30f an element without the
            // key sized its fonts for a 30px row while the layout flowed 42px rows, and touching
            // the slider once "fixed" it permanently — reading as a font bug, not a default bug.
            float px = Def.GetFFor(LayoutBare, "rowHeight", 42f) * 0.5f;
            float size = HudText.Size(px * Def.FontScaleFor(LayoutBare)) * scale;
            row.Value.fontSize = size;
            if (row.Name != null) { HudText.Sync(row.Name); row.Name.fontSize = size; }
        }

        // ---- layout ----

        private void Reflow(float scale, bool vHunger, bool vWater, bool vToilet,
            bool vHealth, bool vCognition, bool vPressure, bool vTemp, bool showBox)
        {
            _lastScale = scale;
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float rowH = Def.GetFFor(LayoutBare, "rowHeight", 42f) * scale;
            float pad = 5f * scale;
            float iconScale = Def.GetFFor(LayoutBare, "iconScale", 0.92f);
            bool rowLines = Def.GetBFor(LayoutBare, "rowLines", true);
            bool icons = Def.GetBFor(LayoutBare, "icons", true)
                && !Def.GetBFor(LayoutBare, "iconWords", false);

            // Build the visible-order list.
            var order = new List<int>(RowCount);
            if (vHunger) order.Add(Hunger);
            if (vWater) order.Add(Water);
            if (vToilet) order.Add(Toilet);
            if (vHealth) order.Add(Health);
            if (vCognition) order.Add(Cognition);
            if (vPressure) order.Add(Pressure);
            if (vTemp) order.Add(Temp);
            int n = order.Count;

            float top = c.y + s.y * 0.5f;                 // stack grows down from the top edge
            float stackH = n * rowH;

            // Follow the LIVE row stack, not the document's maximum/editor rectangle. This stays in
            // logical canvas space; the receiver inverse-warps the screen pointer before testing it.
            _tooltipRect = Rect.MinMaxRect(c.x - s.x * 0.5f, top - stackH,
                c.x + s.x * 0.5f, top);
            _tooltipRectValid = stackH > 0f && s.x > 0f;

            // Box takes the height of the visible rows, top-aligned in the element rect.
            if (showBox)
            {
                float boxCy = top - stackH * 0.5f;
                ((RectTransform)_box.transform).anchoredPosition = new Vector2(c.x, boxCy);
                _box.SetShape(s.x, Mathf.Max(2f, stackH),
                    Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)), Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)),
                    InsetTop(scale), InsetBottom(scale));
            }

            float left = c.x - s.x * 0.5f + pad;
            float right = c.x + s.x * 0.5f - pad;
            float iconSz = Mathf.Clamp(rowH * iconScale, 6f, rowH * 1.15f);

            // Thin grey separators between adjacent visible rows.
            var sepCol = HudPalette.TextDim.Value; sepCol.a *= 0.4f;
            float sepW = s.x - pad * 2f;
            for (int i = 0; i < _seps.Length; i++)
            {
                bool on = rowLines && i < n - 1;
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

                var iconPos = new Vector2(left + iconSz * 0.5f, rowCy);
                var iconDim = new Vector2(iconSz, iconSz);
                row.IconRt.anchoredPosition = iconPos;
                row.IconRt.sizeDelta = iconDim;
                if (row.GlyphRt != null) { row.GlyphRt.anchoredPosition = iconPos; row.GlyphRt.sizeDelta = iconDim; }

                // With icons off, the value column reclaims the icon gutter (full-width rows).
                float textLeft = icons ? left + iconSz + 6f * scale : left;
                float tw = Mathf.Max(12f, right - textLeft);
                var valPos = new Vector2(textLeft + tw * 0.5f, rowCy);
                var valSize = new Vector2(tw, rowH);
                row.ValueRt.anchoredPosition = valPos;
                row.ValueRt.sizeDelta = valSize;
                // The name label shares the value's rect; opposite alignment (left vs right) puts
                // the name at the far left and the percentage at the far right, so the percentages
                // form a clean vertical column (icons-as-words mode only).
                if (row.NameRt != null) { row.NameRt.anchoredPosition = valPos; row.NameRt.sizeDelta = valSize; }
            }
        }

        private bool TooltipCanShow()
        {
            if (!Core.DetailedVitalsTooltip.IsEnabled || !_tooltipRectValid || !Cursor.visible) return false;
            // Modes C/D project a cylinder through a perspective camera; a flat logical rect is
            // not their screen-space inverse and can miss by 60-150 px. Stay fail-soft until that
            // projection has a proven inverse. Flat, per-element VertexWarp, and Dome are exact.
            if (HudWarp.Active == HudWarp.Kind.Cylinder) return false;
            if (global::StationeersUIMod.Features.RadialController.AnyRadialOpen) return false;
            if (global::StationeersUIMod.Windows.HudEditorMode.Active
                || global::StationeersUIMod.Windows.RadialEditorMode.Active) return false;
            if (global::StationeersUIMod.UI.Menu.UiaControlCenter.IsOpen
                || global::StationeersUIMod.UI.Grid.TheGridPanel.IsOpen) return false;
            if (HudSlotDrag.IsDragging) return false;
            if (Root == null || Group == null || !Root.gameObject.activeInHierarchy || Group.alpha < 0.5f)
                return false;
            if (_hudRootGroup != null && _hudRootGroup.alpha < 0.5f) return false;
            try
            {
                if (Core.Guards.VanillaMenuWantsFront()) return false;
                if (ImGuiNET.ImGui.GetIO().WantCaptureMouse) return false;
                var cursor = CursorManager.Instance;
                if (cursor != null && cursor.BlockCursorRaycast) return false;
                if (!MouseModeController.InGame || MouseModeController.InCharacterCustomisation) return false;
                if (HudSlotDrag.PointerOverOtherUiPublic()) return false;
                var human = Core.Guards.LocalHuman;
                return human != null && !human.IsArtificial;
            }
            catch { return false; }
        }

        protected override void OnBeforeDestroy()
        {
            if (_tooltipReceiver != null) _tooltipReceiver.Shutdown();
            _tooltipReceiver = null;
            _hudRootGroup = null;
            _tooltipRectValid = false;
        }

        /// <summary>Small, owned adapter to the game's live screen-space tooltip. It is deliberately
        /// NOT a MonoBehaviour/Graphic: manual inverse-warped polling preserves the HUD's completely
        /// raycast-transparent input contract and cannot swallow a click behind the vitals card.</summary>
        private sealed class VitalsTooltipReceiver : IScreenSpaceTooltip
        {
            private VitalsPanelWidget _owner;
            private bool _available;
            private bool _hovered;

            internal VitalsTooltipReceiver(VitalsPanelWidget owner)
            {
                _owner = owner;
            }

            internal void UpdateHover(bool available, Rect logicalRect)
            {
                _available = available;
                bool over = false;
                Vector2 canvasPoint = default(Vector2);
                if (available)
                {
                    var mouse = (Vector2)Input.mousePosition;
                    canvasPoint = new Vector2(mouse.x - Screen.width * 0.5f,
                        mouse.y - Screen.height * 0.5f);
                    // Use the SAME inverse the proven HUD hit-test uses (HudSystem.ZoneAt ->
                    // HudWarp.Unwarp(p)): it inverse-warps flat + VertexWarp + Dome exactly. The
                    // previous per-element fxWarp multiplier scaled the unwarp by a transition amount
                    // (often not 1), so the hover-rect test missed and the tooltip never fired.
                    canvasPoint = HudWarp.Unwarp(canvasPoint);
                    over = logicalRect.Contains(canvasPoint);
                }
                if (over == _hovered) return;
                _hovered = over;
                if (!over || _owner == null) return;
                try
                {
                    var human = Core.Guards.LocalHuman;
                    var panel = PanelToolTip.Instance;
                    if (human == null || human.IsArtificial || panel == null)
                    {
                        _hovered = false; // retry if initialization raced this frame
                        return;
                    }
                    Core.VanillaTooltip.Lift(panel); // keep the tooltip above the mod's UI canvases
                    panel.SetUpTooltip(GameStrings.PlayerStatsTooltipTitle.DisplayString,
                        human.GetStatsTooltip(), this);
                    // Tutorial hook (Build Contract s4): the tooltip just opened (over went false->true
                    // above), not a per-frame re-affirmation while it stays shown.
                    TutorialSignals.Raise(TSignal.VitalsTooltipShown);
                }
                catch { _hovered = false; }
            }

            public void DoUpdate()
            {
                if (!TooltipIsVisible) return;
                try
                {
                    var human = Core.Guards.LocalHuman;
                    var panel = PanelToolTip.Instance;
                    if (human != null && panel != null) panel.SetInfoText(human.GetStatsTooltip());
                }
                catch { }
            }

            public bool TooltipIsVisible
            {
                get { return _hovered && _available && _owner != null && _owner.TooltipCanShow(); }
            }

            internal void Shutdown()
            {
                _available = false;
                _hovered = false;
                _owner = null;
            }
        }

        private float TooltipWarpMultiplier()
        {
            return TransitionAmt("fxWarp");
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;

            int appearanceStart = into.Count;
            into.Add(HudProp.Bool("Background box", () => d.GetBFor(EditBare(d), "box", true), v => d.SetBFor(EditBare(d), "box", v)));
            into.Add(HudProp.Bool("Row lines", () => d.GetBFor(EditBare(d), "rowLines", true), v => d.SetBFor(EditBare(d), "rowLines", v)));
            for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;

            into.Add(HudProp.Bool("Row icons", () => d.GetBFor(EditBare(d), "icons", true), v => d.SetBFor(EditBare(d), "icons", v)));
            into.Add(HudProp.Bool("Words mode (bare)", () => d.GetBFor(EditBare(d), "words", false), v => d.SetBFor(EditBare(d), "words", v)));
            // #3: replace icons with the need NAME + value ("Thirst 37%", "Hunger 2/4 100%").
            into.Add(HudProp.Bool("Icons as words", () => d.GetBFor(EditBare(d), "iconWords", false), v => d.SetBFor(EditBare(d), "iconWords", v)));
            into.Add(HudProp.Bool("Pressure row (words)", () => d.GetBFor(EditBare(d), "rowPressure", false),
                v => d.SetBFor(EditBare(d), "rowPressure", v)));
            into.Add(HudProp.Bool("Temp row (words)", () => d.GetBFor(EditBare(d), "rowTemp", false),
                v => d.SetBFor(EditBare(d), "rowTemp", v)));

            int layoutStart = into.Count;
            into.Add(HudProp.F("Row height", () => d.GetFFor(EditBare(d), "rowHeight", 42f),
                v => d.SetFFor(EditBare(d), "rowHeight", Mathf.Clamp(v, 16f, 96f)), 16f, 96f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            appearanceStart = into.Count;
            into.Add(HudProp.F("Icon size (rel)", () => d.GetFFor(EditBare(d), "iconScale", 0.92f),
                v => d.SetFFor(EditBare(d), "iconScale", Mathf.Clamp(v, 0.2f, 1.15f)), 0.2f, 1.15f));
            for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
        }
    }
}
