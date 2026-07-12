using System;
using System.Collections.Generic;
using StationeersUIMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameStatus = Assets.Scripts.UI.StatusUpdates;
using GameStatusItem = Assets.Scripts.UI.StatusUpdate;
using GameStatusType = Assets.Scripts.UI.StatusUpdateType;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The car-dashboard moodlet strip: a run of icon-forward chips that mirrors the state
    /// icons the vanilla game is currently showing. The ICON is the signal (nearly the whole
    /// chip), the word a small caption below it — two-word names ("POWER CRITICAL") stack on
    /// two lines to stay narrow. Chips flow center-out in rows and WRAP downward into new rows
    /// when a burst would overflow the element width, so a wide alert cluster forms a tidy grid
    /// instead of running off-screen.
    ///
    /// DEDUPE: vanilla stores several logical StatusUpdates that share ONE physical Image
    /// GameObject (the PowerLow/PowerCritical pair, the Waste pair, the 4-way Pressure group).
    /// When that shared image is active EVERY member reports "showing", which naively yields a
    /// duplicate chip (e.g. POWER LOW next to POWER CRITICAL). We group the visible updates by
    /// their shared Image.gameObject identity and emit ONE chip per group, choosing the
    /// highest-priority member (Critical &gt; Warning &gt; Notice, tie-broken by the critical
    /// flash) — reproducing vanilla's single flashing chip.
    ///
    /// The mirror is read-only and fail-soft: every touch of the game's icon list is wrapped so
    /// a game update that breaks the mirror degrades to an empty strip instead of throwing once
    /// a frame. Chips are pooled and only re-flowed when the visible SET changes (a cheap integer
    /// signature guards the rebuild), so an always-on-screen strip never allocates in steady state.
    /// </summary>
    internal sealed class MoodletDashboardWidget : HudElementView
    {
        private const int Pool = 10;
        private const int GroupCap = 24;   // dedupe scratch depth (visible updates before grouping)

        private sealed class Chip
        {
            public PanelGraphic Pill;
            public HudIconGraphic Glyph;
            public Image Sprite;
            public TextMeshProUGUI Label;
            public RectTransform PillRt, GlyphRt, SpriteRt, LabelRt;
        }

        /// <summary>Resolved, cached state for one visible moodlet — everything the draw path
        /// needs so a steady frame never re-localises a name or re-classifies an icon.</summary>
        private struct Item
        {
            public HudIconKind Kind;   // None => draw the vanilla sprite instead
            public Sprite Sprite;
            public string Label;       // UPPERCASE, may carry a '\n' when the name is stacked
            public int Lines;          // 1 or 2 caption lines (drives uniform row height)
            public int Level;          // 0 = normal, 1 = caution, 2 = critical
        }

        private readonly Chip[] _chips = new Chip[Pool];
        private readonly Item[] _items = new Item[Pool];
        private int _active;

        // Dedupe scratch (rebuild-only; grouped by shared Image.gameObject instance id).
        private readonly int[] _grpKey = new int[GroupCap];
        private readonly GameStatusItem[] _grpWin = new GameStatusItem[GroupCap];
        private readonly int[] _grpPri = new int[GroupCap];
        private readonly int[] _grpLvl = new int[GroupCap];
        private int _grpCount;

        // Rebuild guards: a re-flow happens only when the visible set (or the geometry it is
        // laid out against) actually changes.
        private int _sig = int.MinValue;
        private float _scale = -1f;
        private bool _layoutDirty = true;
        private bool _warnedOverflow;

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < Pool; i++)
            {
                var ch = new Chip();
                ch.Pill = MakePanel(root, "Pill" + i);
                ch.Sprite = MakeIcon(root, "Sprite" + i);

                var go = new GameObject("Glyph" + i, typeof(RectTransform));
                go.transform.SetParent(root, false);
                ch.Glyph = go.AddComponent<HudIconGraphic>();
                ch.Glyph.raycastTarget = false;
                go.AddComponent<VisorWarp>();
                ch.GlyphRt = (RectTransform)go.transform;
                ch.GlyphRt.anchorMin = ch.GlyphRt.anchorMax = new Vector2(0.5f, 0.5f);

                ch.Label = HudText.Make(root, "Label" + i, 11f, TextAlignmentOptions.Top);
                ch.PillRt = (RectTransform)ch.Pill.transform;
                ch.SpriteRt = ch.Sprite.rectTransform;
                ch.LabelRt = ch.Label.rectTransform;

                Hide(ch);
                _chips[i] = ch;
            }
        }

        /// <summary>Geometry the strip lays itself out against changes on resolution/config —
        /// the flow is content-driven (it depends on the live moodlet count), so it can't be
        /// baked here; instead we mark it dirty and re-flow on the next snapshot.</summary>
        public override void Layout(float scale) => _layoutDirty = true;

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            int sig = Signature();
            if (sig != _sig || scale != _scale || _layoutDirty)
            {
                Rebuild(scale);
                _sig = sig;
                _scale = scale;
                _layoutDirty = false;
            }

            // Chrome is re-applied every frame (cheap, dirty-guarded in the graphics) so a
            // palette edit in the F9 designer takes effect without a set change.
            var fill = FillColor();
            var border = BorderColor();
            float bw = BorderWidthFor();
            var normalLabel = HudPalette.TextLabel.Value;
            var normalIcon = TextColor();
            var warn = HudPalette.Warn.Value;
            var crit = HudPalette.Critical.Value;

            // The chip backdrop is OPT-IN (default: bare icons over the world, per play-test).
            bool boxed = Def.GetB("box", false);

            for (int i = 0; i < _active; i++)
            {
                var ch = _chips[i];
                var pillFill = fill;
                var pillBorder = border;
                if (!boxed) { pillFill.a = 0f; pillBorder.a = 0f; }
                ch.Pill.color = pillFill;
                ch.Pill.BorderColor = pillBorder;
                ch.Pill.BorderWidth = bw;
                ApplyGlass(ch.Pill);

                Color tint = _items[i].Level >= 2 ? crit : _items[i].Level == 1 ? warn : normalIcon;
                if (ch.Glyph.enabled) ch.Glyph.color = tint;
                // The GAME's moodlet art keeps its own colours — the state colour lives in the
                // caption; only a critical flash tints the art itself.
                if (ch.Sprite.enabled) ch.Sprite.color = _items[i].Level >= 2 ? crit : Color.white;
                if (ch.Label.enabled)
                {
                    HudText.Sync(ch.Label);
                    ch.Label.color = _items[i].Level >= 1 ? tint : normalLabel;
                }
            }
        }

        // ---- mirror ----

        /// <summary>A moodlet is on the strip iff its image object is live and it isn't one of
        /// the dedicated-display internals (jetpack/light/etc.). Level: critical flash beats a
        /// static caution. All game reads are guarded so a broken mirror yields "not showing".</summary>
        private static bool Showing(GameStatusItem su, out int level)
        {
            level = 0;
            try
            {
                if (su == null || su.UsesDedicatedDisplay) return false;
                var img = su.Image;
                if (img == null || !img.gameObject.activeSelf) return false;
                if (su._lastFlashState) level = 2;
                else if (su._lastStaticState) level = 1;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Priority for winner-of-a-shared-image selection: Critical(2) &gt; Warning(1)
        /// &gt; Notice(0), tie-broken by the critical flash so a flashing member wins its peers.</summary>
        private static int Priority(GameStatusItem su)
        {
            int type = 0;
            bool flash = false;
            try { type = (int)su.Type; } catch { }
            try { flash = su._lastFlashState; } catch { }
            return type * 2 + (flash ? 1 : 0);
        }

        /// <summary>Cheap, allocation-free fingerprint of the visible set — count folds in
        /// naturally, per-entry identity via the instance hash, plus type/level so a moodlet
        /// crossing into the red (or a shared image's winner changing) re-flows. No name
        /// lookups here.</summary>
        private int Signature()
        {
            int sig = 17;
            try
            {
                var all = GameStatus.AllStatusUpdates;
                if (all == null) return 0;
                for (int i = 0; i < all.Count; i++)
                {
                    int level;
                    if (!Showing(all[i], out level)) continue;
                    int h;
                    try { h = all[i].GetHashCode(); } catch { h = 0; }
                    int type = 0;
                    try { type = (int)all[i].Type; } catch { }
                    unchecked { sig = sig * 31 + h * 3 + level * 7 + type; }
                }
            }
            catch { return 0; }
            return sig;
        }

        /// <summary>Second pass (set-change only): resolve the visible moodlets into cached items.
        /// First DEDUPE — group by shared Image.gameObject identity and keep the highest-priority
        /// member per group — then resolve each winner into a glyph/sprite + stacked UPPERCASE
        /// caption, and flow the chips into centred, wrapping rows.</summary>
        private void Rebuild(float scale)
        {
            _active = 0;
            _grpCount = 0;
            try
            {
                var all = GameStatus.AllStatusUpdates;
                if (all != null)
                {
                    // --- pass A: group visible updates by their shared Image.gameObject ---
                    for (int i = 0; i < all.Count; i++)
                    {
                        var su = all[i];
                        int level;
                        if (!Showing(su, out level)) continue;

                        int key;
                        try { key = su.Image.gameObject.GetInstanceID(); }
                        catch { continue; }
                        int pri = Priority(su);

                        int g = -1;
                        for (int k = 0; k < _grpCount; k++)
                            if (_grpKey[k] == key) { g = k; break; }

                        if (g < 0)
                        {
                            if (_grpCount >= GroupCap) continue; // pathological; drop extras
                            g = _grpCount++;
                            _grpKey[g] = key;
                            _grpWin[g] = su;
                            _grpPri[g] = pri;
                            _grpLvl[g] = level;
                        }
                        // Winner by ACTUAL DISPLAYED LEVEL first, then static priority.
                        // A shared-image pair reports BOTH members active, but only one
                        // carries the live level: during caution-only the Warning peer is
                        // level 1 ("POWER LOW") while the Critical peer is level 0 — ranking
                        // by Type alone would wrongly show "POWER CRITICAL" in normal colour.
                        else if (level > _grpLvl[g] || (level == _grpLvl[g] && pri > _grpPri[g]))
                        {
                            _grpWin[g] = su;
                            _grpPri[g] = pri;
                            _grpLvl[g] = level;
                        }
                    }

                    // --- pass B: resolve one chip per group ---
                    bool stack = Def.GetB("stackWords", true);
                    for (int g = 0; g < _grpCount; g++)
                    {
                        if (_active >= Pool)
                        {
                            if (!_warnedOverflow)
                            {
                                _warnedOverflow = true;
                                UIALog.Warn("MoodletDashboard: more than " + Pool +
                                    " distinct moodlets; extras are dropped this session.");
                            }
                            break;
                        }

                        var su = _grpWin[g];
                        Sprite sprite = null;
                        try { sprite = su.Icon; } catch { }
                        // The GAME'S own moodlet art is the default (players already know these
                        // icons); the thin-line glyph set is the opt-in.
                        HudIconKind kind = Def.GetB("glyphs", false)
                            ? GlyphFor(su, sprite)
                            : (sprite != null ? HudIconKind.None : GlyphFor(su, null));

                        string raw = "";
                        try { raw = su.GetDisplayName(); } catch { }
                        raw = string.IsNullOrEmpty(raw) ? "" : raw.ToUpperInvariant();

                        string label = raw;
                        int lines = raw.Length > 0 ? 1 : 0;
                        if (stack && raw.Length > 0)
                        {
                            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2)
                            {
                                label = parts[0] + "\n" + parts[1];
                                lines = 2;
                            }
                        }

                        _items[_active] = new Item
                        {
                            Kind = kind,
                            Sprite = sprite,
                            Label = label,
                            Lines = lines,
                            Level = _grpLvl[g],
                        };
                        _active++;
                    }
                }
            }
            catch { _active = 0; }

            // Release the winner references we no longer need (grouping is done).
            for (int g = 0; g < _grpCount; g++) _grpWin[g] = null;

            Reflow(scale);
        }

        /// <summary>Position and size the active chips as centred rows that wrap downward from
        /// the top of the element rect when a row would overflow the element width. Chips are a
        /// fixed width; the icon dominates the chip and the caption sits below it.</summary>
        private void Reflow(float scale)
        {
            var center = CenterFor(scale);
            var size = SizeFor(scale);
            float elemW = Mathf.Max(8f, size.x);
            float elemTop = center.y + size.y * 0.5f;

            float chipHref = Def.GetF("chipH", 60f);
            float chipH = Mathf.Max(8f, chipHref * scale);
            float chipW = Mathf.Max(8f, Def.GetF("chipWidth", 120f) * scale);
            float iconScale = Mathf.Clamp(Def.GetF("iconScale", 0.95f), 0.3f, 1.6f);
            float textScale = Mathf.Clamp(Def.GetF("textScale", 0.24f), 0.08f, 0.6f);
            float colGap = Mathf.Max(0f, Def.GetF("colGap", 8f)) * scale;
            float rowGap = Mathf.Max(0f, Def.GetF("rowGap", 6f)) * scale;
            bool labels = Def.GetB("labels", true);

            float iconSz = chipH * iconScale;
            float iconGap = chipH * 0.06f;
            float captionFont = HudText.Size(chipHref * textScale * Def.FontScale) * scale;
            float lineH = captionFont * 1.12f;
            float radius = Mathf.Min(chipH * 0.5f,
                Mathf.Min(Mathf.Min(Radius(Def.RTL), Radius(Def.RTR)),
                          Mathf.Min(Radius(Def.RBR), Radius(Def.RBL))));

            // Uniform row height keeps chips aligned even when only some names stack to two
            // lines: the tallest caption in the set sets the caption band for every chip.
            int maxLines = 0;
            if (labels)
                for (int i = 0; i < _active; i++)
                    if (_items[i].Label.Length > 0 && _items[i].Lines > maxLines)
                        maxLines = _items[i].Lines;
            float captionH = maxLines * lineH;
            float rowH = iconSz + (captionH > 0f ? iconGap + captionH : 0f);

            // How many fixed-width chips fit across the element width (at least one).
            int perRow = Mathf.Max(1, Mathf.FloorToInt((elemW + colGap) / (chipW + colGap)));

            int idx = 0;
            int row = 0;
            while (idx < _active)
            {
                int count = Mathf.Min(perRow, _active - idx);
                float rowW = count * chipW + (count - 1) * colGap;
                float startX = center.x - rowW * 0.5f;
                float rowCenterY = elemTop - rowH * 0.5f - row * (rowH + rowGap);
                float rowTop = rowCenterY + rowH * 0.5f;

                for (int k = 0; k < count; k++, idx++)
                {
                    var ch = _chips[idx];
                    float cx = startX + k * (chipW + colGap) + chipW * 0.5f;

                    ch.Pill.enabled = true;
                    ch.PillRt.anchoredPosition = new Vector2(cx, rowCenterY);
                    ch.Pill.SetShape(chipW, rowH, radius, radius, radius, radius);

                    // Exactly one icon channel draws; the other is parked so a pooled chip never
                    // double-draws a glyph over a sprite.
                    bool useGlyph = _items[idx].Kind != HudIconKind.None;
                    ch.Glyph.enabled = useGlyph;
                    ch.Sprite.enabled = !useGlyph && _items[idx].Sprite != null;

                    float iconCy = rowTop - iconSz * 0.5f;   // icon hugs the top of the chip
                    var iconRt = useGlyph ? ch.GlyphRt : ch.SpriteRt;
                    iconRt.anchoredPosition = new Vector2(cx, iconCy);
                    iconRt.sizeDelta = new Vector2(iconSz, iconSz);
                    if (useGlyph) ch.Glyph.Kind = _items[idx].Kind;
                    else ch.Sprite.sprite = _items[idx].Sprite;

                    bool showLabel = labels && _items[idx].Label.Length > 0 && captionH > 0f;
                    ch.Label.enabled = showLabel;
                    if (showLabel)
                    {
                        float capTop = rowTop - iconSz - iconGap;
                        ch.LabelRt.sizeDelta = new Vector2(chipW, captionH);
                        ch.LabelRt.anchoredPosition = new Vector2(cx, capTop - captionH * 0.5f);
                        ch.Label.alignment = TextAlignmentOptions.Top;
                        ch.Label.enableWordWrapping = false;
                        ch.Label.fontSize = captionFont;
                        HudText.Set(ch.Label, _items[idx].Label);
                    }
                }
                row++;
            }

            for (int i = _active; i < Pool; i++) Hide(_chips[i]);
        }

        private static void Hide(Chip ch)
        {
            ch.Pill.enabled = false;
            ch.Glyph.enabled = false;
            ch.Sprite.enabled = false;
            ch.Label.enabled = false;
        }

        /// <summary>Map a well-known moodlet to one of our line glyphs by its stable raw name
        /// (falling back to the vanilla sprite's object name); anything unmapped returns None so
        /// the caller draws the vanilla sprite instead. Substring match keeps it robust to the
        /// game's exact wording.</summary>
        private static HudIconKind GlyphFor(GameStatusItem su, Sprite sprite)
        {
            string id = null;
            try { id = su.DisplayName; } catch { }
            if (string.IsNullOrEmpty(id) && sprite != null) id = sprite.name;
            if (string.IsNullOrEmpty(id)) return HudIconKind.None;
            id = id.ToLowerInvariant();

            if (Has(id, "hung") || Has(id, "starv") || Has(id, "nutri")) return HudIconKind.Burger;
            if (Has(id, "thirst") || Has(id, "dehyd") || Has(id, "hydra")) return HudIconKind.Droplet;
            if (Has(id, "toilet") || Has(id, "sanit") || Has(id, "soil")) return HudIconKind.Toilet;
            if (Has(id, "power") || Has(id, "batter")) return HudIconKind.Bolt;
            if (Has(id, "suffoc") || Has(id, "oxygen") || Has(id, "breath")) return HudIconKind.Lungs;
            if (Has(id, "cold") || Has(id, "freez") || Has(id, "templow") || Has(id, "temperaturelow")) return HudIconKind.Thermometer;
            if (Has(id, "hot") || Has(id, "overheat") || Has(id, "temphigh") || Has(id, "temperaturehigh")) return HudIconKind.Flame;
            if (Has(id, "pressure")) return HudIconKind.Gauge;
            if (Has(id, "hurt") || Has(id, "injur") || Has(id, "health") || Has(id, "damage")) return HudIconKind.Heart;
            return HudIconKind.None;
        }

        private static bool Has(string s, string sub) => s.IndexOf(sub, StringComparison.Ordinal) >= 0;

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Chip width", () => d.GetF("chipWidth", 120f), v => d.SetF("chipWidth", Mathf.Clamp(v, 24f, 480f)), 24f, 480f));
            into.Add(HudProp.F("Chip height", () => d.GetF("chipH", 60f), v => d.SetF("chipH", Mathf.Clamp(v, 10f, 240f)), 10f, 240f));
            into.Add(HudProp.F("Icon scale (× chip height)", () => d.GetF("iconScale", 0.95f), v => d.SetF("iconScale", Mathf.Clamp(v, 0.3f, 1.6f)), 0.3f, 1.6f));
            into.Add(HudProp.F("Text scale (× chip height)", () => d.GetF("textScale", 0.24f), v => d.SetF("textScale", Mathf.Clamp(v, 0.08f, 0.6f)), 0.08f, 0.6f));
            into.Add(HudProp.F("Column gap", () => d.GetF("colGap", 8f), v => d.SetF("colGap", Mathf.Clamp(v, 0f, 40f)), 0f, 40f));
            into.Add(HudProp.F("Row gap", () => d.GetF("rowGap", 6f), v => d.SetF("rowGap", Mathf.Clamp(v, 0f, 40f)), 0f, 40f));
            into.Add(HudProp.Bool("Stack two-word names", () => d.GetB("stackWords", true), v => d.SetB("stackWords", v)));
            into.Add(HudProp.Bool("Show labels", () => d.GetB("labels", true), v => d.SetB("labels", v)));
            into.Add(HudProp.Bool("Chip backdrop box", () => d.GetB("box", false), v => d.SetB("box", v)));
            into.Add(HudProp.Bool("Thin-line glyphs (off = game icons)", () => d.GetB("glyphs", false), v => d.SetB("glyphs", v)));
        }
    }
}
