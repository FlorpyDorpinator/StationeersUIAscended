using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// A single instrument readout laid out inside its element rect: an optional dark box,
    /// a small icon, a small-caps LABEL over a big VALUE with a subscript unit, an optional
    /// TARGET line, and an optional <see cref="ThresholdBarGraphic"/> gauge. Which live
    /// number it shows is chosen by the "src" param (<see cref="HudReadoutSource"/>); every
    /// value, its safe/warn/crit bands and its setpoint come from the frame snapshot so the
    /// cell agrees with the vanilla panels it echoes (top status bar, vitals card).
    ///
    /// Geometry is relative to the rect — the bar reserves a bottom (or right, when vertical)
    /// strip and the text stack fills what's left — so resizing in the editor scales the whole
    /// cell sensibly rather than clipping.
    /// </summary>
    internal sealed class ReadoutWidget : HudElementView
    {
        private PanelGraphic _box;
        private HudIconGraphic _glyph;
        private Image _iconSprite;
        private RectTransform _iconRt;
        private TextMeshProUGUI _label, _value, _target;
        private RectTransform _labelRt, _valueRt, _targetRt;
        private ThresholdBarGraphic _bar;
        private RectTransform _barRt;

        // Display-string cache: rebuilt only when the rounded value / validity / unit
        // changes (see UpdatePanel) — never a per-frame allocation.
        private bool _shownValid;
        private float _shownValue;
        private string _shownUnit;
        private string _valueText = "--";
        private bool _shownTgtOn;
        private float _shownTgt;
        private string _tgtText = "";
        private bool _iconIsVanilla; // game art keeps its own colours + late-resolves

        // Parsed-source cache: Enum.TryParse only re-runs when the editor changes the raw
        // "src" string. The param bag hands back the stored reference, so a ReferenceEquals
        // guard self-heals on any edit without a per-frame parse.
        private string _srcKey;
        private HudReadoutSource _srcCache = HudReadoutSource.ExternalPressure;

        // "Game art" bar style: vanilla's own pressure-ramp sprites instead of the
        // procedural threshold bar. Built lazily; late-resolves like the icons.
        private Image _rampBack, _rampFront;
        private RectTransform _rampBackRt, _rampFrontRt;
        private Vector2 _barCenter, _barSize;   // the bar strip Layout() reserved
        private bool _barIsVertical;
        private float _lastFill = -1f;
        // Auto value-font size in REFERENCE px, derived from the box each Layout so text
        // shrinks/grows with the box (FlorpyDorp: "text should update when I resize").
        private float _autoValueRef = 17f;

        private static readonly string[] SourceNames = System.Enum.GetNames(typeof(HudReadoutSource));
        private static readonly string[] BarStyleNames = { "Procedural", "Game art" };

        // The cell's background box takes the trapezoid insets (base supplies the sliders).
        protected override bool SupportsTrapezoid => true;

        protected override void BuildContent(RectTransform root)
        {
            _box = MakePanel(root, "Box");

            // Optional icon, decided from the element's Icon field. Resolution order:
            // user PNG override -> the GAME'S own icon (keys like "Hunger"/"Temp"/
            // "Pressure" — the art players already know) -> built-in glyph. Vanilla
            // sprites can resolve late (their singletons exist only in-world), so that
            // channel keeps retrying in UpdatePanel until the sprite lands.
            // A conditional temp icon needs the sprite slot even if no Icon key was set —
            // otherwise the F9 "Conditional temp icon" toggle would silently do nothing.
            // BUILD time has no tier context, and the sprite slot must exist for EVERY tier that
            // could ask for it — so this asks whether ANY slot wants the conditional icon.
            bool tempIconWanted = Def.GetB("tempIcon", false)
                || Def.GetBFor(HudStyleSlot.Bare, "tempIcon", false)
                || Def.GetBFor(HudStyleSlot.Robot, "tempIcon", false);
            if (!string.IsNullOrEmpty(Def.Icon) || tempIconWanted)
            {
                var sprite = Core.HudIconStore.TryGet(Def.Icon);
                _iconIsVanilla = sprite == null && (tempIconWanted || Core.VanillaIcons.IsKey(Def.Icon));
                if (_iconIsVanilla && !tempIconWanted) sprite = Core.VanillaIcons.TryGet(Def.Icon);

                if (sprite != null || _iconIsVanilla)
                {
                    _iconSprite = MakeIcon(root, "IconSprite");
                    _iconSprite.sprite = sprite;
                    _iconSprite.enabled = sprite != null;
                    _iconRt = _iconSprite.rectTransform;
                }
                else
                {
                    var go = new GameObject("IconGlyph", typeof(RectTransform));
                    go.transform.SetParent(root, false);
                    _glyph = go.AddComponent<HudIconGraphic>();
                    _glyph.raycastTarget = false;
                    go.AddComponent<VisorWarp>();
                    _iconRt = (RectTransform)go.transform;
                    _iconRt.anchorMin = _iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                    HudIconKind kind;
                    if (System.Enum.TryParse(Def.Icon, true, out kind)) _glyph.Kind = kind;
                }
            }

            // The multi-word LABEL and TARGET wrap to the box width instead of spilling past its
            // edges (their rects are sized to the content width in Layout); the VALUE is a single
            // number+unit and stays on one line.
            _label = HudText.Make(root, "Label", 11f, TextAlignmentOptions.Center, wrap: true);
            _value = HudText.Make(root, "Value", 17f, TextAlignmentOptions.Center);
            _target = HudText.Make(root, "Target", 10f, TextAlignmentOptions.Center, wrap: true);
            _labelRt = _label.rectTransform;
            _valueRt = _value.rectTransform;
            _targetRt = _target.rectTransform;

            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(root, false);
            _bar = barGo.AddComponent<ThresholdBarGraphic>();
            _bar.raycastTarget = false;
            barGo.AddComponent<VisorWarp>();
            _barRt = (RectTransform)barGo.transform;
            _barRt.anchorMin = _barRt.anchorMax = new Vector2(0.5f, 0.5f);
        }

        /// <summary>A stacked-layout row height as a fraction of the content region. Clamped rather
        /// than validated: a hand-edited profile holding 5 or -2 pins the row to an edge instead of
        /// flinging the text outside the box.</summary>
        private float StackRow(string key, float def)
        {
            return Mathf.Clamp01(Def.GetFFor(LayoutBare, key, def));
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            bool showBar = Def.GetBFor(LayoutBare, "bar", true);
            bool vertical = Def.GetBFor(LayoutBare, "barVertical", false);
            float iconScale = Def.GetFFor(LayoutBare, "iconScale", 1f); // F9 "Icon scale" multiplier
            // Per-element "Show icon" (default on). Off collapses the icon gutter so the label/
            // value reclaim it — this is how the SPEED box drops its velocity glyph.
            bool showIcon = _iconRt != null && Def.GetBFor(LayoutBare, "icon", true);

            // Per-element "Wrap text": the label/target wrap to the box width or spill on one line.
            bool wrapText = Def.GetBFor(LayoutBare, "wrap", true);
            if (_label.enableWordWrapping != wrapText) _label.enableWordWrapping = wrapText;
            if (_target.enableWordWrapping != wrapText) _target.enableWordWrapping = wrapText;

            ((RectTransform)_box.transform).anchoredPosition = c;
            _box.SetShape(s.x, s.y,
                Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)), Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)),
                InsetTop(scale), InsetBottom(scale));

            float pad = 6f * scale;
            float left = c.x - s.x * 0.5f + pad;
            float right = c.x + s.x * 0.5f - pad;
            float topEdge = c.y + s.y * 0.5f - pad;
            float botEdge = c.y - s.y * 0.5f + pad;
            float gap = 4f * scale;

            // Content region shrinks by the strip the bar reserves. The strip geometry is
            // remembered so the game-art ramp (when chosen) can occupy the same pixels.
            float cLeft = left, cRight = right, cTop = topEdge, cBot = botEdge;
            _barIsVertical = vertical;
            if (showBar)
            {
                if (vertical)
                {
                    float thick = Mathf.Clamp(s.x * 0.12f, 4f * scale, 14f * scale);
                    _barCenter = new Vector2(right - thick * 0.5f, c.y);
                    _barSize = new Vector2(thick, cTop - cBot);
                    cRight = right - thick - gap;
                }
                else
                {
                    float thick = Mathf.Clamp(s.y * 0.14f, 4f * scale, 14f * scale);
                    _barCenter = new Vector2(c.x, botEdge + thick * 0.5f);
                    _barSize = new Vector2(cRight - cLeft, thick);
                    cBot = botEdge + thick + gap;
                }
                _barRt.anchoredPosition = _barCenter;
                _barRt.sizeDelta = _barSize;
                LayoutRamp();
                _lastFill = -1f; // geometry moved: re-place the fill next update
            }

            float cx = (cLeft + cRight) * 0.5f;
            float cw = cRight - cLeft;

            // Stacked box (the internal-pressure/temp instruments): title on top, a TARGET
            // line, then the big value with its conditional icon to the left, horizontal
            // bar already reserved along the bottom.
            if (Def.GetBFor(LayoutBare, "stack", false))
            {
                // Row heights are fractions of the content region (0 = bottom edge, 1 = top), so
                // they hold their proportions at any box size or HUD scale. Editable in F9 —
                // the shipped 0.84/0.54/0.26 leave the value crowding the TARGET line on a short
                // box, and there was no way to open that gap up.
                float lblY = Mathf.Lerp(cBot, cTop, StackRow("rowTitleY", 0.84f));
                float tgtY = Mathf.Lerp(cBot, cTop, StackRow("rowTargetY", 0.54f));
                float valY = Mathf.Lerp(cBot, cTop, StackRow("rowValueY", 0.26f));
                _label.alignment = TMPro.TextAlignmentOptions.Center;
                _value.alignment = TMPro.TextAlignmentOptions.Center;
                _labelRt.anchoredPosition = new Vector2(cx, lblY);
                _labelRt.sizeDelta = new Vector2(cw, 16f * scale);
                _targetRt.anchoredPosition = new Vector2(cx, tgtY);
                _targetRt.sizeDelta = new Vector2(cw, 12f * scale);
                _valueRt.anchoredPosition = new Vector2(cx, valY);
                _valueRt.sizeDelta = new Vector2(cw, 22f * scale);
                _autoValueRef = (cTop - cBot) / Mathf.Max(0.01f, scale) * 0.19f;
                if (showIcon)
                {
                    // Conditional hot/cold glyph sits just left of the value row (item 7:
                    // vanilla's hot/cold icon reads ~1.5× the old size).
                    float isz = Mathf.Clamp((cTop - cBot) * 0.34f, 14f * scale, 40f * scale) * iconScale;
                    _iconRt.anchoredPosition = new Vector2(cLeft + isz * 0.5f, valY);
                    _iconRt.sizeDelta = new Vector2(isz, isz);
                }
            }
            else if (vertical)
            {
                // Tall card: multi-line label up top, value mid, target line, icon at the
                // very bottom (the concept's gauge column). The label box gets TWO lines
                // of room — "INTERNAL PRESSURE" style names wrap on a 92px card.
                float lblY = Mathf.Lerp(cBot, cTop, 0.86f);
                float valY = Mathf.Lerp(cBot, cTop, 0.56f);
                float tgtY = Mathf.Lerp(cBot, cTop, 0.34f);
                _labelRt.anchoredPosition = new Vector2(cx, lblY);
                _labelRt.sizeDelta = new Vector2(cw, 28f * scale);
                _valueRt.anchoredPosition = new Vector2(cx, valY);
                _valueRt.sizeDelta = new Vector2(cw, 24f * scale);
                _targetRt.anchoredPosition = new Vector2(cx, tgtY);
                _targetRt.sizeDelta = new Vector2(cw, 12f * scale);
                _autoValueRef = cw / Mathf.Max(0.01f, scale) * 0.22f;
                if (showIcon)
                {
                    float isz = Mathf.Clamp(cw * 0.5f, 14f * scale, 44f * scale) * iconScale;
                    _iconRt.anchoredPosition = new Vector2(cx, cBot + isz * 0.5f + 2f * scale);
                    _iconRt.sizeDelta = new Vector2(isz, isz);
                }
            }
            else
            {
                // Compact row (the mockup's cluster rows): icon left, label top beside it,
                // value right-of-label, thin bar already reserved along the bottom.
                _autoValueRef = (cTop - cBot) / Mathf.Max(0.01f, scale) * 0.42f;
                float isz = showIcon
                    ? Mathf.Clamp((cTop - cBot) * 0.82f, 14f * scale, 44f * scale) * iconScale : 0f;
                float textLeft = cLeft + (isz > 0f ? isz + 6f * scale : 0f);
                float tw = Mathf.Max(20f, cRight - textLeft);
                float midY = (cTop + cBot) * 0.5f;
                if (showIcon)
                {
                    _iconRt.anchoredPosition = new Vector2(cLeft + isz * 0.5f, midY);
                    _iconRt.sizeDelta = new Vector2(isz, isz);
                }
                _label.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                _value.alignment = TMPro.TextAlignmentOptions.MidlineRight;
                _labelRt.anchoredPosition = new Vector2(textLeft + tw * 0.5f, midY);
                _labelRt.sizeDelta = new Vector2(tw, 16f * scale);
                _valueRt.anchoredPosition = new Vector2(textLeft + tw * 0.5f, midY);
                _valueRt.sizeDelta = new Vector2(tw, 22f * scale);
                _targetRt.anchoredPosition = new Vector2(textLeft + tw * 0.5f, cBot + 6f * scale);
                _targetRt.sizeDelta = new Vector2(tw, 12f * scale);
            }
        }

        public override void UpdatePanel(HudSnapshot snap, float scale)
        {
            var src = ParseSource(Def);
            Reading r = Read(snap, src);

            // --- chrome (per-element ColorRef overrides via the base helpers) ---
            bool showBox = Def.GetBFor(LayoutBare, "box", true);
            _box.enabled = showBox;
            if (showBox)
            {
                _box.color = FillColor();
                _box.BorderColor = BorderColor();
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }

            var accent = TextColor();
            // Per-element "Show icon" (default on): when off, hide both icon channels entirely —
            // Layout already reclaims the gutter — and skip the resolve. This is the SPEED box's
            // "remove the icon" toggle.
            bool showIcon = Def.GetBFor(LayoutBare, "icon", true);
            if (_glyph != null) { _glyph.enabled = showIcon; if (showIcon) _glyph.color = accent; }
            if (!showIcon)
            {
                if (_iconSprite != null) _iconSprite.enabled = false;
            }
            else
            {
                // Conditional temperature icon: vanilla shows the hot sprite above 50°C, the
                // cold sprite below 0°C, nothing between (PlayerStateWindow thresholds). Driven
                // from the live reading, overriding the static Def.Icon sprite each frame.
                bool tempIcon = Def.GetBFor(LayoutBare, "tempIcon", false);
                if (_iconSprite != null && tempIcon)
                {
                    var ts = r.Valid ? Core.VanillaIcons.TempStateIcon(r.Raw) : null;
                    _iconSprite.sprite = ts;
                    _iconSprite.enabled = ts != null;
                    _iconSprite.color = Color.white;
                }
                else if (_iconSprite != null)
                {
                    if (_iconIsVanilla)
                    {
                        // Game art keeps its native colours; retry until the singleton exists.
                        if (_iconSprite.sprite == null)
                        {
                            var late = Core.VanillaIcons.TryGet(Def.Icon);
                            _iconSprite.sprite = late;
                            _iconSprite.enabled = late != null;
                        }
                        _iconSprite.color = Color.white;
                    }
                    else
                    {
                        _iconSprite.color = accent; // PNG overrides are white-on-transparent
                    }
                }
            }

            // --- text ---
            string lbl = Def.GetS("label", "");
            if (string.IsNullOrEmpty(lbl)) lbl = r.Label;

            // Value/target strings rebuild only when the ROUNDED display value changes —
            // an always-on readout must not allocate a string per frame. Rounded (never
            // truncated) to the source's decimals: the legacy cells showed "22.9" and
            // "-5.9", and (int) would have read a degree warm on the negative side.
            float shown = r.Valid ? (float)System.Math.Round(r.Raw, r.Decimals) : 0f;
            if (r.Valid != _shownValid || (r.Valid && shown != _shownValue)
                || !ReferenceEquals(r.Unit, _shownUnit))
            {
                _shownValid = r.Valid; _shownValue = shown; _shownUnit = r.Unit;
                _valueText = r.Valid
                    ? shown.ToString(r.Decimals == 1 ? "0.0" : "0", CultureInfo.InvariantCulture) + Small(r.Unit)
                    : "--";
            }
            string valueText = _valueText;

            Color vcol = r.Level >= 2 ? HudPalette.Critical.Value
                : r.Level == 1 ? HudPalette.Warn.Value
                : accent;

            bool showTarget = Def.GetBFor(LayoutBare, "target", SourceHasTarget(src));
            bool tgtOn = showTarget && r.HasTarget && !float.IsNaN(r.Target);
            float tgtShown = tgtOn ? (float)System.Math.Round(r.Target) : 0f;
            if (tgtOn != _shownTgtOn || (tgtOn && tgtShown != _shownTgt))
            {
                _shownTgtOn = tgtOn; _shownTgt = tgtShown;
                _tgtText = tgtOn
                    ? "TARGET " + tgtShown.ToString("0", CultureInfo.InvariantCulture)
                    : "";
            }
            string tgtText = _tgtText;

            // Font tracks the box size (from Layout's _autoValueRef) times an optional F9
            // multiplier, so shrinking the box shrinks the text. "textScale" default 1.
            float vs = _autoValueRef * Def.GetFFor(LayoutBare, "textScale", 1f) * Def.FontScaleFor(LayoutBare);
            HudText.Sync(_label); HudText.Sync(_value); HudText.Sync(_target);
            _value.fontSize = HudText.Size(vs) * scale;
            _label.fontSize = HudText.Size(vs * 0.62f) * scale;
            _target.fontSize = HudText.Size(vs * 0.55f) * scale;
            // Label + TARGET line were hardcoded to the (blue-grey) TextLabel/TextDim palette
            // slots with no per-element control — the only editable text colour was the VALUE
            // (via the base "Text / accent"). Route both through their own ColorRefs so a themed
            // HUD can recolour the "INTERNAL PRESSURE" label and the "TARGET ##" line (empty ref
            // = today's palette default).
            _label.color = GlobalOr(Def.GetSFor(LayoutBare, "labelColor", ""), HudPalette.TextLabel.Value);
            _value.color = vcol;
            _target.color = GlobalOr(Def.GetSFor(LayoutBare, "targetColor", ""), HudPalette.TextDim.Value);
            HudText.Set(_label, lbl);
            HudText.Set(_value, valueText);
            HudText.Set(_target, tgtText);

            // --- gauge ---
            bool showBar = Def.GetBFor(LayoutBare, "bar", true);
            bool gameBar = showBar && UseGameBar();
            if (gameBar)
            {
                EnsureRamp();
                gameBar = UpdateRamp(r); // false until vanilla's sprites exist
            }
            else if (_rampBack != null)
            {
                _rampBack.enabled = false;
                _rampFront.enabled = false;
            }
            _bar.enabled = showBar && !gameBar;
            if (showBar && !gameBar)
            {
                _bar.Vertical = Def.GetBFor(LayoutBare, "barVertical", false);
                _bar.SetRange(r.Min, r.Max);
                _bar.SetZones(r.WarnLow, r.CritLow, r.WarnHigh, r.CritHigh);
                _bar.Value = r.Valid ? r.Raw : float.NaN;
                _bar.Target = (showTarget && r.HasTarget) ? r.Target : float.NaN;
                _bar.FillColor = GlobalOr(Def.GetSFor(LayoutBare, "barFill", ""), HudPalette.Good.Value);
                _bar.WarnColor = GlobalOr(Def.GetSFor(LayoutBare, "barWarn", ""), HudPalette.Warn.Value);
                _bar.CritColor = GlobalOr(Def.GetSFor(LayoutBare, "barCrit", ""), HudPalette.Critical.Value);
                _bar.TrackColor = GlobalOr(Def.GetSFor(LayoutBare, "barTrack", ""), HudPalette.PanelBorder.Value);
                _bar.TargetColor = GlobalOr(Def.GetSFor(LayoutBare, "barTarget", ""), HudPalette.TextValue.Value);
                _bar.CornerRadius = 3f * scale;
                _bar.TargetWidth = 2f * scale;
            }
        }

        // ---- the game-art ramp bar ----

        /// <summary>"Game" bar style: vanilla's own pressure-ramp sprites in the strip the
        /// procedural bar would occupy.</summary>
        private bool UseGameBar()
            => string.Equals(Def.GetSFor(LayoutBare, "barStyle", ""), "game", System.StringComparison.OrdinalIgnoreCase);

        private void EnsureRamp()
        {
            if (_rampBack != null) return;
            _rampBack = MakeRampImage("RampBack");
            _rampFront = MakeRampImage("RampFront");
            _rampBackRt = _rampBack.rectTransform;
            _rampFrontRt = _rampFront.rectTransform;
            LayoutRamp();
        }

        private Image MakeRampImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = false;
            // BORN DISABLED: a sprite-less Image renders as a solid white rect, and the
            // vanilla sprites resolve late (world-only) — the 2026-07-12 play-test's
            // "white square at screen center" was exactly this object, enabled at its
            // default 100x100 before the first successful UpdateRamp.
            img.enabled = false;
            go.AddComponent<VisorWarp>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            return img;
        }

        /// <summary>Static ramp geometry: BOTH images fill the reserved bar strip; a
        /// vertical gauge rotates the horizontal art 90° (a left-origin horizontal
        /// fill then grows from the bottom). The fill level is fillAmount cropping —
        /// the same reveal vanilla gets from its stretch-mask.</summary>
        private void LayoutRamp()
        {
            if (_rampBackRt == null) return;
            float len = _barIsVertical ? _barSize.y : _barSize.x;
            float thick = _barIsVertical ? _barSize.x : _barSize.y;
            var rot = _barIsVertical ? new Vector3(0f, 0f, 90f) : Vector3.zero;
            _rampBackRt.localEulerAngles = rot;
            _rampFrontRt.localEulerAngles = rot;
            _rampBackRt.anchoredPosition = _barCenter;
            _rampBackRt.sizeDelta = new Vector2(len, thick);
            _rampFrontRt.anchoredPosition = _barCenter;
            _rampFrontRt.sizeDelta = new Vector2(len, thick);
        }

        /// <summary>Per-frame fill. Pressure sources use vanilla's EXACT curve; everything
        /// else maps linearly over the gauge span. Vanilla scene truth: the track draws at
        /// vanilla's own tint (white ~25%), the lit art white, and the fill is a REVEAL of
        /// full-length art — Image.fillAmount reproduces it without vanilla's mask.</summary>
        private bool UpdateRamp(Reading r)
        {
            var back = Core.VanillaIcons.PressureRampBack();
            var front = Core.VanillaIcons.PressureRampFront();
            if (back == null || front == null)
            {
                // World not up yet — hide BOTH images (never let a sprite-less Image
                // draw its white placeholder) and let the procedural bar stand in.
                _rampBack.enabled = false;
                _rampFront.enabled = false;
                return false;
            }

            if (_rampBack.sprite == null) _rampBack.sprite = back;
            if (_rampFront.sprite == null)
            {
                _rampFront.sprite = front;
                _rampFront.type = Image.Type.Filled;
                _rampFront.fillMethod = Image.FillMethod.Horizontal;
                _rampFront.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
            _rampBack.enabled = true;
            _rampBack.color = Core.VanillaIcons.PressureRampBackColor();
            _rampFront.color = Core.VanillaIcons.PressureRampFrontColor();

            float fill;
            var src = ParseSource(Def);
            if (src == HudReadoutSource.InternalPressure || src == HudReadoutSource.ExternalPressure
                || src == HudReadoutSource.SuitTargetPressure || src == HudReadoutSource.JetpackPropellant)
            {
                fill = Core.VanillaIcons.PressureFill(r.Valid ? r.Raw : 0f);
                if (float.IsNaN(fill)) fill = 0f;
            }
            else
            {
                fill = r.Max > r.Min
                    ? Mathf.Clamp01(((r.Valid ? r.Raw : r.Min) - r.Min) / (r.Max - r.Min))
                    : 0f;
            }
            fill = Mathf.Round(fill * 256f) / 256f; // quantize: no per-frame mesh churn
            _lastFill = fill;
            _rampFront.enabled = fill > 0f;
            _rampFront.fillAmount = fill; // Image dirty-guards internally
            return true;
        }

        // ---- source resolution ----

        private HudReadoutSource ParseSource(HudElementDef d)
        {
            string raw = d.GetS("src", "");
            if (ReferenceEquals(raw, _srcKey)) return _srcCache;
            _srcKey = raw;
            HudReadoutSource src;
            _srcCache = System.Enum.TryParse(raw, true, out src) ? src : HudReadoutSource.ExternalPressure;
            return _srcCache;
        }

        /// <summary>Sources whose value carries a natural setpoint — the TARGET line and bar
        /// caret default on for these and stay off for everything else.</summary>
        private static bool SourceHasTarget(HudReadoutSource src)
            => src == HudReadoutSource.InternalPressure || src == HudReadoutSource.InternalTemp;

        /// <summary>One frame's worth of a readout: the number, its unit, whether it can be
        /// trusted, its optional setpoint, the gauge span and the warn/crit boundaries. Zone
        /// boundaries default to NaN (band absent); threshold coloring reads them back out.</summary>
        private struct Reading
        {
            public string Label, Unit;
            public float Raw;
            public bool Valid;
            public bool HasTarget;
            public float Target;
            public int Level;
            public float Min, Max;
            public float WarnLow, CritLow, WarnHigh, CritHigh;
            /// <summary>Displayed decimal places (temps and O2 carry one, like the legacy
            /// cells; everything else is whole numbers). Rounded, never truncated.</summary>
            public int Decimals;
            /// <summary>Band to color "--" with when the reading is invalid (the legacy top
            /// bar painted a missing battery red, not neutral).</summary>
            public int InvalidLevel;
        }

        // Every value below comes straight from the snapshot; the warn/crit numbers replicate
        // the ones the top status bar and vitals card already use so the cell reads identically.
        private static Reading Read(HudSnapshot s, HudReadoutSource src)
        {
            var r = new Reading
            {
                Unit = "",
                Min = 0f,
                Max = 100f,
                Target = float.NaN,
                WarnLow = float.NaN,
                CritLow = float.NaN,
                WarnHigh = float.NaN,
                CritHigh = float.NaN,
            };

            switch (src)
            {
                case HudReadoutSource.ExternalPressure:
                    r.Label = "EXTERNAL PRESSURE"; r.Unit = " kPa";
                    r.Valid = s.HasAtmosphere; r.Raw = s.PressureKPa;
                    r.Max = 700f;
                    r.WarnLow = 20f; r.CritLow = 6.3f; r.WarnHigh = 303.97f; r.CritHigh = 607.95f;
                    break;
                case HudReadoutSource.ExternalTemp:
                    r.Label = "EXTERNAL TEMP"; r.Unit = " °C"; r.Decimals = 1;
                    r.Valid = s.HasAtmosphere; r.Raw = s.TempC;
                    r.Min = -30f; r.Max = 90f;
                    r.WarnLow = 0f; r.CritLow = -10f; r.WarnHigh = 50f; r.CritHigh = 80f;
                    break;
                case HudReadoutSource.FeltTemp:
                    // The vitals-card TEMP semantics: what your skin reads — breathing
                    // atmosphere when sealed, ambient otherwise. Valid with ANY atmosphere
                    // (InternalTemp instead requires internals running).
                    r.Label = "TEMP"; r.Unit = " °C"; r.Decimals = 1;
                    r.Valid = s.FeltValid; r.Raw = s.FeltTempC;
                    r.Min = -30f; r.Max = 90f;
                    r.WarnLow = 0f; r.CritLow = -10f; r.WarnHigh = 50f; r.CritHigh = 80f;
                    break;
                case HudReadoutSource.ExternalO2:
                    r.Label = "EXTERNAL O2"; r.Unit = " %"; r.Decimals = 1;
                    r.Valid = s.HasAtmosphere; r.Raw = s.O2Fraction * 100f;
                    r.WarnLow = 18f; r.CritLow = 10f;
                    break;
                case HudReadoutSource.InternalPressure:
                    r.Label = "INTERNAL PRESSURE"; r.Unit = " kPa";
                    r.Valid = s.InternalValid; r.Raw = s.InternalPressureKPa;
                    r.Max = 300f;
                    r.WarnLow = 20f; r.CritLow = 6.3f; r.WarnHigh = 303.97f; r.CritHigh = 607.95f;
                    if (s.SuitTargetPressureKPa >= 0f) { r.HasTarget = true; r.Target = s.SuitTargetPressureKPa; }
                    break;
                case HudReadoutSource.InternalTemp:
                    r.Label = "INTERNAL TEMP"; r.Unit = " °C"; r.Decimals = 1;
                    r.Valid = s.InternalValid; r.Raw = s.InternalTempC;
                    r.Min = -30f; r.Max = 90f;
                    r.WarnLow = 0f; r.CritLow = -10f; r.WarnHigh = 50f; r.CritHigh = 80f;
                    if (!float.IsNaN(s.SuitTargetTempC)) { r.HasTarget = true; r.Target = s.SuitTargetTempC; }
                    break;
                case HudReadoutSource.SuitTargetPressure:
                    r.Label = "TARGET PRESSURE"; r.Unit = " kPa";
                    r.Valid = s.SuitTargetPressureKPa >= 0f; r.Raw = s.SuitTargetPressureKPa;
                    r.Max = 300f;
                    break;
                case HudReadoutSource.SuitTargetTemp:
                    r.Label = "TARGET TEMP"; r.Unit = " °C"; r.Decimals = 1;
                    r.Valid = !float.IsNaN(s.SuitTargetTempC); r.Raw = s.SuitTargetTempC;
                    r.Min = -10f; r.Max = 50f;
                    break;
                case HudReadoutSource.Nutrition:
                    r.Label = "NUTRITION"; r.Unit = " %";
                    r.Valid = true; r.Raw = s.FoodRatio * 100f;
                    r.WarnLow = 40f; r.CritLow = 20f;
                    break;
                case HudReadoutSource.Hydration:
                    r.Label = "HYDRATION"; r.Unit = " %";
                    r.Valid = true; r.Raw = s.Hydration01 * 100f;
                    r.WarnLow = 40f; r.CritLow = 20f;
                    break;
                case HudReadoutSource.Sanitation:
                    r.Label = "SANITATION"; r.Unit = " %";
                    r.Valid = s.SanitationValid; r.Raw = s.Sanitation01 * 100f;
                    r.WarnLow = 40f; r.CritLow = 20f;
                    break;
                case HudReadoutSource.Hygiene:
                    r.Label = "HYGIENE"; r.Unit = " %";
                    r.Valid = true; r.Raw = s.Hygiene01 * 100f;
                    r.WarnLow = 40f; r.CritLow = 20f;
                    break;
                case HudReadoutSource.JetpackThrust:
                    r.Label = "JETPACK THRUST"; r.Unit = " %";
                    r.Valid = s.JetpackPresent; r.Raw = s.JetpackThrustPct;
                    r.Max = 200f;
                    break;
                case HudReadoutSource.JetpackPropellant:
                    r.Label = "PROPELLANT"; r.Unit = " kPa";
                    r.Valid = s.JetpackPresent && s.JetpackIsGas && s.JetpackPropellantDeltaKPa >= 0f;
                    r.Raw = s.JetpackPropellantDeltaKPa;
                    r.Max = 2000f; r.WarnLow = 500f; r.CritLow = 100f;
                    break;
                case HudReadoutSource.SuitPower:
                    r.Label = "SUIT POWER"; r.Unit = " %";
                    r.Valid = s.SuitBatteryPct >= 0; r.Raw = s.SuitBatteryPct;
                    // Battery % is an integer; the legacy cells alarmed INCLUSIVELY
                    // (amber AT 25, red AT 10) — the half-step encodes that exactly.
                    r.WarnLow = 25.5f; r.CritLow = 10.5f;
                    r.InvalidLevel = 2; // no battery reads as an alarm, not a shrug
                    break;
                case HudReadoutSource.Health:
                    r.Label = "HEALTH"; r.Unit = " %";
                    r.Valid = true; r.Raw = s.HealthRatio * 100f;
                    r.WarnLow = 75f; r.CritLow = 25f;
                    break;
                case HudReadoutSource.O2Quality:
                    r.Label = "O2 QUALITY"; r.Unit = " %";
                    r.Valid = true; r.Raw = s.O2Quality * 100f;
                    r.WarnLow = 100f; r.CritLow = 75f;
                    break;
                case HudReadoutSource.Heading:
                    r.Label = "HEADING"; r.Unit = "°";
                    r.Valid = true; r.Raw = s.HeadingDeg;
                    r.Max = 360f;
                    break;
                case HudReadoutSource.Speed:
                    r.Label = "SPEED"; r.Unit = " m/s"; r.Decimals = 1;
                    r.Valid = true; r.Raw = s.SpeedMs;
                    r.Max = 20f;
                    break;
            }

            r.Level = r.Valid ? LevelFor(r) : r.InvalidLevel;
            return r;
        }

        /// <summary>Whole-value threshold band, mirroring the bar's own crit-trumps-warn rule
        /// so the number and the gauge always agree. Bounds are strict; integer-quantized
        /// sources that legacy alarmed INCLUSIVELY (battery: red AT 10%) encode that by
        /// placing the boundary at n + 0.5 instead.</summary>
        private static int LevelFor(Reading r)
        {
            float v = r.Raw;
            if ((!float.IsNaN(r.CritLow) && v < r.CritLow) || (!float.IsNaN(r.CritHigh) && v > r.CritHigh)) return 2;
            if ((!float.IsNaN(r.WarnLow) && v < r.WarnLow) || (!float.IsNaN(r.WarnHigh) && v > r.WarnHigh)) return 1;
            return 0;
        }

        private static string Small(string unit) => "<size=62%>" + unit + "</size>";

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;

            // Content: what this readout represents and which optional data it exposes.
            into.Add(HudProp.Enum("Source", () => (int)ParseSource(d),
                v => d.Set("src", SourceNames[Mathf.Clamp(v, 0, SourceNames.Length - 1)]), SourceNames));
            // Tier-aware (Wave C): which optional PARTS a readout draws is presentation, so it
            // forks; the SOURCE and the label override above are identity and stay shared.
            // The REBUILD is guarded on a real change (BuildContent decides whether the
            // conditional-icon slot exists, and an unguarded request would also fire on the no-op
            // round trip SeedSlotFromBase performs when a per-tier fork is opened) — but the WRITE
            // is not. Skipping the write on an unchanged value would leave a freshly seeded fork
            // with no stored copy of this key, and because an absent BASE key gives
            // HudElementDef's copy-on-write nothing to hand the fork for a BOOL, a later SUITED
            // edit would then leak straight back into BARE (adversarial review, 2026-07-25).
            into.Add(HudProp.Bool("Conditional temp icon", () => d.GetBFor(EditBare(d), "tempIcon", false),
                v =>
                {
                    bool changed = v != d.GetBFor(EditBare(d), "tempIcon", false);
                    d.SetBFor(EditBare(d), "tempIcon", v);
                    if (changed) HudSystem.RequestViewRebuild();
                }));
            into.Add(HudProp.Bool("Target line", () => d.GetBFor(EditBare(d), "target", SourceHasTarget(ParseSource(d))),
                v => d.SetBFor(EditBare(d), "target", v)));
            into.Add(HudProp.Bool("Threshold bar", () => d.GetBFor(EditBare(d), "bar", true),
                v => d.SetBFor(EditBare(d), "bar", v)));
            into.Add(HudProp.Text("Label override", () => d.GetS("label", ""), v => d.Set("label", Empty(v))));
            // The icon slot is created at build time, so an icon change rebuilds the view.
            into.Add(HudProp.Text("Icon (game key/glyph/PNG)", () => d.Icon ?? "",
                v =>
                {
                    string next = Empty(v);
                    if (string.Equals(next, d.Icon, System.StringComparison.Ordinal)) return;
                    d.Icon = next;
                    HudSystem.RequestViewRebuild();
                }));

            int layoutStart = into.Count;
            into.Add(HudProp.Bool("Wrap text (label/target)", () => d.GetBFor(EditBare(d), "wrap", true),
                v => d.SetBFor(EditBare(d), "wrap", v)));
            into.Add(HudProp.Bool("Stacked layout", () => d.GetBFor(EditBare(d), "stack", false),
                v => d.SetBFor(EditBare(d), "stack", v)));
            into.Add(HudProp.Bool("Vertical bar", () => d.GetBFor(EditBare(d), "barVertical", false),
                v => d.SetBFor(EditBare(d), "barVertical", v)));
            // Stacked-only row heights (0 = bottom of the content region, 1 = top). Ignored by the
            // tall-card and compact-row layouts, which stack their rows in a different order.
            into.Add(HudProp.F("Stacked: title row Y", () => d.GetFFor(EditBare(d), "rowTitleY", 0.84f),
                v => d.SetFFor(EditBare(d), "rowTitleY", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.F("Stacked: target row Y", () => d.GetFFor(EditBare(d), "rowTargetY", 0.54f),
                v => d.SetFFor(EditBare(d), "rowTargetY", Mathf.Clamp01(v)), 0f, 1f));
            into.Add(HudProp.F("Stacked: value row Y", () => d.GetFFor(EditBare(d), "rowValueY", 0.26f),
                v => d.SetFFor(EditBare(d), "rowValueY", Mathf.Clamp01(v)), 0f, 1f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            int appearanceStart = into.Count;
            into.Add(HudProp.Bool("Background box", () => d.GetBFor(EditBare(d), "box", true), v => d.SetBFor(EditBare(d), "box", v)));
            into.Add(HudProp.Bool("Show icon", () => d.GetBFor(EditBare(d), "icon", true), v => d.SetBFor(EditBare(d), "icon", v)));
            // Per-element text colours: the LABEL ("INTERNAL PRESSURE") and the TARGET line were
            // previously locked to the palette; expose both (empty = the palette default).
            into.Add(HudProp.Color("Label colour", () => d.GetSFor(EditBare(d), "labelColor", ""),
                v => d.SetSFor(EditBare(d), "labelColor", Empty(v)), () => HudPalette.TextLabel.Value));
            into.Add(HudProp.Color("Target colour", () => d.GetSFor(EditBare(d), "targetColor", ""),
                v => d.SetSFor(EditBare(d), "targetColor", Empty(v)), () => HudPalette.TextDim.Value));
            into.Add(HudProp.Enum("Bar style", () => UseGameBar() ? 1 : 0,
                v => d.SetSFor(EditBare(d), "barStyle", v == 1 ? "game" : null), BarStyleNames));
            // Bar refs are colours, and colours are per-element in BOTH style states (the
            // 2026-07-16 standardisation): empty tracks the palette slot, hex stands alone.
            into.Add(HudProp.Color("Bar fill", () => d.GetSFor(EditBare(d), "barFill", ""),
                v => d.SetSFor(EditBare(d), "barFill", Empty(v)), () => HudPalette.Good.Value));
            into.Add(HudProp.Color("Bar warn", () => d.GetSFor(EditBare(d), "barWarn", ""),
                v => d.SetSFor(EditBare(d), "barWarn", Empty(v)), () => HudPalette.Warn.Value));
            into.Add(HudProp.Color("Bar crit", () => d.GetSFor(EditBare(d), "barCrit", ""),
                v => d.SetSFor(EditBare(d), "barCrit", Empty(v)), () => HudPalette.Critical.Value));
            into.Add(HudProp.Color("Bar track", () => d.GetSFor(EditBare(d), "barTrack", ""),
                v => d.SetSFor(EditBare(d), "barTrack", Empty(v)), () => HudPalette.PanelBorder.Value));
            into.Add(HudProp.Color("Bar target", () => d.GetSFor(EditBare(d), "barTarget", ""),
                v => d.SetSFor(EditBare(d), "barTarget", Empty(v)), () => HudPalette.TextValue.Value));
            into.Add(HudProp.F("Icon scale", () => d.GetFFor(EditBare(d), "iconScale", 1f), v => d.SetFFor(EditBare(d), "iconScale", Mathf.Clamp(v, 0.2f, 4f)), 0.2f, 4f));
            into.Add(HudProp.F("Text scale (× box-auto)", () => d.GetFFor(EditBare(d), "textScale", 1f), v => d.SetFFor(EditBare(d), "textScale", Mathf.Clamp(v, 0.3f, 3f)), 0.3f, 3f));
            for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
        }

        /// <summary>Blank in the editor means "revert to the palette/auto default": stored as a
        /// removed key rather than an empty string, so the profile never persists a dead param.</summary>
        private static string Empty(string v) => string.IsNullOrEmpty(v) ? null : v;
    }
}
