using System.Collections.Generic;
using StationeersUIMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameStatus = Assets.Scripts.UI.StatusUpdates;
using GameStatusItem = Assets.Scripts.UI.StatusUpdate;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The car-dashboard moodlet strip: a horizontal run of small icon+label pills that
    /// mirrors the state icons the vanilla game is currently showing. The strip is centred
    /// in the element rect — one active moodlet sits dead centre, two split around it, and
    /// N spread evenly — so it reads like an instrument cluster rather than a fixed grid.
    ///
    /// The mirror is read-only and fail-soft: every touch of the game's icon list is wrapped
    /// so a game update that breaks the mirror degrades to an empty strip instead of throwing
    /// once a frame. Pills are pooled and only re-flowed when the visible SET changes (a cheap
    /// integer signature guards the rebuild), so an always-on-screen strip never allocates or
    /// re-lays-out in steady state.
    /// </summary>
    internal sealed class MoodletDashboardWidget : HudElementView
    {
        private const int Pool = 10;

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
            public string Label;       // UPPERCASE, cached until the set signature changes
            public int Level;          // 0 = normal, 1 = caution, 2 = critical
            public float Width;        // laid-out pill width in scaled px
        }

        private readonly Chip[] _chips = new Chip[Pool];
        private readonly Item[] _items = new Item[Pool];
        private int _active;

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

                ch.Label = HudText.Make(root, "Label" + i, 11f, TextAlignmentOptions.MidlineLeft);
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

            // The pill box is OPT-IN (default: bare icons over the world, per play-test).
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

                Color tint = _items[i].Level >= 2 ? crit : _items[i].Level == 1 ? warn : normalIcon;
                if (ch.Glyph.enabled) ch.Glyph.color = tint;
                // The GAME's moodlet art keeps its own colours — the state colour lives
                // in the label; only a critical flash tints the art itself.
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

        /// <summary>Cheap, allocation-free fingerprint of the visible set — count folds in
        /// naturally, per-entry identity via the instance hash, and the caution/critical level
        /// so a moodlet crossing into the red re-flows its tint. No name lookups here.</summary>
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
                    unchecked { sig = sig * 31 + h * 3 + level; }
                }
            }
            catch { return 0; }
            return sig;
        }

        /// <summary>Second pass (set-change only): resolve each visible moodlet into a cached
        /// item — glyph or sprite, UPPERCASE label, level — then flow the pills centred.</summary>
        private void Rebuild(float scale)
        {
            _active = 0;
            try
            {
                var all = GameStatus.AllStatusUpdates;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var su = all[i];
                        int level;
                        if (!Showing(su, out level)) continue;

                        if (_active >= Pool)
                        {
                            if (!_warnedOverflow)
                            {
                                _warnedOverflow = true;
                                UIALog.Warn("MoodletDashboard: more than " + Pool +
                                    " active moodlets; extras are dropped this session.");
                            }
                            break;
                        }

                        Sprite sprite = null;
                        try { sprite = su.Icon; } catch { }
                        // The GAME'S own moodlet art is the default (these icons are what
                        // players already know); the thin-line glyph set is the opt-in.
                        HudIconKind kind = Def.GetB("glyphs", false)
                            ? GlyphFor(su, sprite)
                            : (sprite != null ? HudIconKind.None : GlyphFor(su, null));

                        string label = "";
                        try { label = su.GetDisplayName(); } catch { }
                        label = string.IsNullOrEmpty(label) ? "" : label.ToUpperInvariant();

                        _items[_active] = new Item
                        {
                            Kind = kind,
                            Sprite = sprite,
                            Label = label,
                            Level = level,
                        };
                        _active++;
                    }
                }
            }
            catch { _active = 0; }

            Reflow(scale);
        }

        /// <summary>Position and size the active pills as one centred row; hide the rest of the
        /// pool. Pill width adapts to label length via a character-count estimate (no per-frame
        /// TMP preferred-width query).</summary>
        private void Reflow(float scale)
        {
            var c = CenterFor(scale);
            float chipHref = Def.GetF("chipH", 30f);
            float chipH = Mathf.Max(8f, chipHref * scale);
            float gap = Mathf.Max(0f, Def.GetF("gap", 10f)) * scale;
            bool labels = Def.GetB("labels", true);

            // Dashboard-light proportions (play-test): the ICON is the signal — nearly
            // the whole chip height — and the word is a small caption beside it.
            float pad = chipH * 0.14f;
            float iconSz = chipH * 0.92f;
            float iconGap = chipH * 0.16f;
            float charW = chipH * 0.17f;   // rough label em; slight over-estimate avoids clipping
            float radius = Mathf.Min(chipH * 0.5f,
                Mathf.Min(Mathf.Min(Radius(Def.RTL), Radius(Def.RTR)),
                          Mathf.Min(Radius(Def.RBR), Radius(Def.RBL))));

            float total = 0f;
            for (int i = 0; i < _active; i++)
            {
                float labelW = labels && _items[i].Label.Length > 0
                    ? iconGap + _items[i].Label.Length * charW
                    : 0f;
                float w = pad * 2f + iconSz + labelW;
                _items[i].Width = w;
                total += w;
                if (i > 0) total += gap;
            }

            float cursor = c.x - total * 0.5f;
            var lblFont = HudText.Size(chipHref * 0.27f * Def.FontScale) * scale;

            for (int i = 0; i < _active; i++)
            {
                var ch = _chips[i];
                float w = _items[i].Width;
                float cx = cursor + w * 0.5f;
                cursor += w + gap;

                ch.Pill.enabled = true;
                ch.PillRt.anchoredPosition = new Vector2(cx, c.y);
                ch.Pill.SetShape(w, chipH, radius, radius, radius, radius);

                bool showLabel = labels && _items[i].Label.Length > 0;
                float innerLeft = cx - w * 0.5f + pad;
                float iconCx = showLabel ? innerLeft + iconSz * 0.5f : cx;

                // Exactly one icon channel draws; the other is parked so a pooled pill never
                // double-draws a glyph over a sprite.
                bool useGlyph = _items[i].Kind != HudIconKind.None;
                ch.Glyph.enabled = useGlyph;
                ch.Sprite.enabled = !useGlyph && _items[i].Sprite != null;

                var iconRt = useGlyph ? ch.GlyphRt : ch.SpriteRt;
                iconRt.anchoredPosition = new Vector2(iconCx, c.y);
                iconRt.sizeDelta = new Vector2(iconSz, iconSz);
                if (useGlyph) ch.Glyph.Kind = _items[i].Kind;
                else ch.Sprite.sprite = _items[i].Sprite;

                ch.Label.enabled = showLabel;
                if (showLabel)
                {
                    float labelStart = innerLeft + iconSz + iconGap;
                    float labelW = cx + w * 0.5f - pad - labelStart;
                    ch.LabelRt.sizeDelta = new Vector2(Mathf.Max(2f, labelW), chipH);
                    ch.LabelRt.anchoredPosition = new Vector2(labelStart + labelW * 0.5f, c.y);
                    ch.Label.fontSize = lblFont;
                    HudText.Set(ch.Label, _items[i].Label);
                }
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

        private static bool Has(string s, string sub) => s.IndexOf(sub, System.StringComparison.Ordinal) >= 0;

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Chip gap", () => d.GetF("gap", 10f), v => d.SetF("gap", Mathf.Clamp(v, 0f, 60f)), 0f, 60f));
            into.Add(HudProp.F("Chip height", () => d.GetF("chipH", 30f), v => d.SetF("chipH", Mathf.Clamp(v, 10f, 80f)), 10f, 80f));
            into.Add(HudProp.Bool("Show labels", () => d.GetB("labels", true), v => d.SetB("labels", v)));
            into.Add(HudProp.Bool("Thin-line glyphs (off = game icons)", () => d.GetB("glyphs", false), v => d.SetB("glyphs", v)));
        }
    }
}
