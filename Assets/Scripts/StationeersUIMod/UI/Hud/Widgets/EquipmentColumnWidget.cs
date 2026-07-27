using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using StationeersUIMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The left equipment column from the concept art, re-homed onto a document element:
    /// six rounded boxes — key number, live thumbnail, slot name — matching the 1-6
    /// equipment keys exactly (helmet, glasses, suit, back, uniform, belt). The robot's
    /// uniform slot is its battery. Shown at every tier: knowing what you wear is not a
    /// suit sensor.
    ///
    /// Geometry is driven by the element rect — the six boxes stack (or, with the
    /// horizontal flag, row) INSIDE it, each box sized to fit six-across with the gap
    /// tunable, so dragging the element bigger or docking it along a different edge scales
    /// the whole column sensibly.
    /// </summary>
    internal sealed class EquipmentColumnWidget : HudElementView
    {
        // Each of the six equipment boxes is a real PanelGraphic (fill/border/glass all apply).
        protected override bool SupportsPanelAppearance => true;

        // The universal accent never reaches a pixel here — slot numbers and labels resolve
        // through their own dedicated refs ("numColor"/"labelColor"/"labelEmptyColor").
        protected override bool UsesAccentColor => false;

        private static readonly string[] Labels = { "HELMET", "GLASSES", "SUIT", "BACK", "UNIFORM", "BELT" };
        // The 1-6 slot digits are constant; hold them so the per-frame text set never allocates.
        private static readonly string[] SlotNums = { "1", "2", "3", "4", "5", "6" };

        private sealed class Box
        {
            public PanelGraphic Panel;
            public TextMeshProUGUI Number, Label;
            public Image Icon;
            public ThresholdBarGraphic Bar; // vanilla-style damage/health bar (green->yellow->red)
            public Image Warn; // leak / fire glyph overlay (vanilla parity) - NO broken-X
        }

        private readonly List<Box> _boxes = new List<Box>();

        protected override void BuildContent(RectTransform root)
        {
            for (int i = 0; i < 6; i++)
            {
                var b = new Box();
                b.Panel = MakePanel(root, "Box" + i);
                b.Number = HudText.Make(root, "Num" + i, 12f, TextAlignmentOptions.TopLeft);
                b.Icon = MakeIcon(root, "Icon" + i);
                // Damage bar draws over the thumbnail's lower edge (created after the icon, before
                // the label/warn so it sits under them in z). VisorWarp keeps it on the curve.
                var barGo = new GameObject("Bar" + i, typeof(RectTransform));
                barGo.transform.SetParent(root, false);
                b.Bar = barGo.AddComponent<ThresholdBarGraphic>();
                b.Bar.raycastTarget = false;
                barGo.AddComponent<VisorWarp>();
                var brt0 = (RectTransform)barGo.transform;
                brt0.anchorMin = brt0.anchorMax = new Vector2(0.5f, 0.5f);
                b.Bar.SetRange(0f, 1f);
                b.Bar.gameObject.SetActive(false);
                b.Label = HudText.Make(root, "Label" + i, 10f, TextAlignmentOptions.Center);
                // Created LAST so it draws on top of icon/bar/label: the leak / fire glyph overlay.
                b.Warn = MakeIcon(root, "Warn" + i);
                b.Warn.gameObject.SetActive(false);
                _boxes.Add(b);
            }
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;

            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float gap = Def.GetFFor(LayoutBare, "gap", 8f) * scale;
            bool horizontal = Def.GetBFor(LayoutBare, "horizontal", false);
            bool labels = Def.GetBFor(LayoutBare, "labels", true);
            int first, count;
            Slice(out first, out count);
            float box = BoxSize(s, gap, horizontal, count);

            for (int i = 0; i < 6; i++)
            {
                var b = _boxes[i];
                bool inSlice = i >= first && i < first + count;
                b.Panel.gameObject.SetActive(inSlice);
                b.Number.gameObject.SetActive(inSlice);
                b.Icon.gameObject.SetActive(inSlice);
                b.Label.gameObject.SetActive(inSlice && labels);
                if (!inSlice) { b.Warn.gameObject.SetActive(false); b.Bar.gameObject.SetActive(false); continue; }
                var center = BoxCenter(i - first, c, box, gap, horizontal, count);

                var prt = (RectTransform)b.Panel.transform;
                prt.anchoredPosition = center;
                float rr = box * 0.25f;
                b.Panel.SetShape(box, box,
                    Mathf.Min(RadiusTL(), rr), Mathf.Min(RadiusTR(), rr),
                    Mathf.Min(RadiusBR(), rr), Mathf.Min(RadiusBL(), rr));

                // Nudged down+right off the rounded corner (FlorpyDorp: the digit was
                // clipping the box edge).
                b.Number.rectTransform.sizeDelta = new Vector2(box - box * 0.16f, box * 0.18f);
                b.Number.rectTransform.anchoredPosition = new Vector2(center.x + box * 0.07f, center.y + box * 0.5f - box * 0.21f);

                // Thumbnail size as a fraction of the box (0.58 = the previous hard-coded look, so
                // an un-keyed profile is pixel-identical). Nudges are reference px × scale, matching
                // the hand boxes' icon controls.
                float iconSize = box * Mathf.Clamp(Def.GetFFor(LayoutBare, "iconScale", 0.58f), 0.1f, 1f);
                b.Icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                b.Icon.rectTransform.anchoredPosition = new Vector2(
                    center.x + Def.GetFFor(LayoutBare, "iconDX", 0f) * scale,
                    center.y + box * 0.04f + Def.GetFFor(LayoutBare, "iconDY", 0f) * scale);

                b.Label.rectTransform.sizeDelta = new Vector2(box + box * 0.18f, box * 0.16f);
                b.Label.rectTransform.anchoredPosition = new Vector2(center.x, center.y - box * 0.5f + box * 0.13f);
                b.Label.enabled = labels;

                // Leak / fire glyph — centred over the item (UpdatePanel toggles it and drives
                // the red<->white pulse). Sized as a fraction of the box.
                float warnSize = box * Mathf.Clamp(Def.GetFFor(LayoutBare, "warnScale", 0.72f), 0.2f, 1.2f);
                b.Warn.rectTransform.sizeDelta = new Vector2(warnSize, warnSize);
                b.Warn.rectTransform.anchoredPosition = center;

                // Vanilla-style damage bar: a thin pill-ended track pinned to the bottom edge of
                // the thumbnail, width tracking the icon. Shown / hidden / coloured in UpdatePanel.
                float barThick = Mathf.Max(2f, box * Mathf.Clamp(Def.GetFFor(LayoutBare, "damageBarThick", 0.085f), 0.03f, 0.25f));
                float barW = iconSize * 0.98f;
                float iconCY = center.y + box * 0.04f + Def.GetFFor(LayoutBare, "iconDY", 0f) * scale;
                var brt = b.Bar.rectTransform;
                brt.sizeDelta = new Vector2(barW, barThick);
                brt.anchoredPosition = new Vector2(
                    center.x + Def.GetFFor(LayoutBare, "iconDX", 0f) * scale,
                    iconCY - iconSize * 0.5f + barThick * 0.5f);
                b.Bar.CornerRadius = barThick * 0.5f;
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var human = s.Human;
            int first, count;
            Slice(out first, out count);
            var box = BoxSize(SizeFor(scale), Def.GetFFor(LayoutBare, "gap", 8f) * scale,
                Def.GetBFor(LayoutBare, "horizontal", false), count);
            bool labels = Def.GetBFor(LayoutBare, "labels", true);

            // Per-element text colours (the slot number + the slot label filled/empty were locked
            // to the blue-grey TextDim/TextLabel palette slots with no per-element control). Empty
            // ref = today's palette default; alloc-free on the palette-name path.
            Color numColor = GlobalOr(Def.GetSFor(LayoutBare, "numColor", ""), HudPalette.TextDim.Value);
            Color labelColor = GlobalOr(Def.GetSFor(LayoutBare, "labelColor", ""), HudPalette.TextLabel.Value);
            Color labelEmptyColor = GlobalOr(Def.GetSFor(LayoutBare, "labelEmptyColor", ""), HudPalette.TextDim.Value);

            for (int i = first; i < first + count; i++)
            {
                var b = _boxes[i];
                Slot slot = SlotFor(human, i);

                DynamicThing occ = null;
                try { occ = slot?.Get(); } catch { }
                bool filled = occ != null;

                // Chrome goes through the base helpers so a per-element ColorRef override
                // paints the boxes; an empty slot only dims what the base resolved.
                var fill = FillColor();
                var border = BorderColor();
                if (!filled) { fill.a *= 0.55f; border.a *= 0.45f; }
                b.Panel.color = fill;
                b.Panel.BorderColor = border;
                b.Panel.BorderWidth = BorderWidthFor();
                // Empty slots read as dim — spec's alpha lift would erase that cue, so
                // only occupied boxes catch the edge light. Set once (not ApplyGlass-
                // then-override: a per-frame flip-flop would defeat the dirty-guard).
                b.Panel.Sheen = GlassSheenFor();
                b.Panel.Spec = filled ? GlassEdgeFor() : 0f;
                b.Panel.FeatherOverride = FeatherFor();
                ApplyMeshFx(b.Panel); // full 0.9.0 push (trio + ripple + uv0 + material) — sheen/spec above stay custom

                // #4: drop cue — light this equipment box while a dragged item is over it and fits.
                if (HudDropCue.Active && ReferenceEquals(HudDropCue.HoveredSlot, slot) && HudDropCue.Accepts(slot))
                {
                    var hi = DropHighlightColor();
                    if (DropWholeBox()) b.Panel.color = hi;
                    else { b.Panel.BorderColor = hi; b.Panel.BorderWidth = BorderWidthFor() * 2f; }
                }

                HudText.Sync(b.Number);
                b.Number.fontSize = HudText.Size(11f * Def.FontScaleFor(LayoutBare)) * scale;
                // The dim label grey, not the bright slot-number white (FlorpyDorp:
                // "make the numbers more grey so it isn't so bright") — now an editable ref.
                b.Number.color = numColor;
                HudText.Set(b.Number, i >= 0 && i < SlotNums.Length ? SlotNums[i] : (i + 1).ToString());

                Sprite icon = null;
                if (filled) { try { icon = occ.GetThumbnail(); } catch { } }
                // Smart-stow "it went in here" flash: after an item is stowed into this worn
                // container, show the stowed item's icon instead of the container's (vanilla
                // parity), with a brief scale pop. Reverts on its own when the flash expires.
                float flashP;
                Sprite flash = Core.SlotFlash.Get(slot, out flashP);
                if (flash != null) icon = flash;
                b.Icon.sprite = icon;
                b.Icon.enabled = icon != null;
                // #9 item-icon tint: per-element and per-tier since 0.9.2.5 (mode 0 inherits the
                // global checkbox). Keeps the filled/empty alpha cue.
                b.Icon.color = TintItemIcon(new Color(1f, 1f, 1f, (filled || flash != null) ? 0.95f : 0f));
                b.Icon.rectTransform.localScale = Vector3.one * (flash != null ? 1f + 0.22f * flashP : 1f);

                HudText.Sync(b.Label);
                b.Label.enabled = labels;
                b.Label.fontSize = HudText.Size(9.9f * Def.FontScaleFor(LayoutBare)) * scale;
                var lc = filled ? labelColor : labelEmptyColor;
                if (!filled) lc.a *= 0.7f;
                b.Label.color = lc;
                HudText.Set(b.Label, i == 4 && s.IsRobot ? "BATTERY" : Labels[i]);

                // Damage / leak / fire. Vanilla shows a health BAR on a damaged worn item
                // (green->yellow->red, hidden when pristine) plus a small animated leak / fire
                // glyph while the suit is venting or burning. We mirror both exactly — and, unlike
                // before, draw NO broken-X (vanilla never shows one for a damaged item; a full
                // break just reads as an empty red bar). IsLeaking / IsBurning are NETWORKED
                // (MP-safe on a client); DamageState is best-effort. Leak / fire sprites are
                // vanilla's own (VanillaIcons -> SlotDisplayButton / StatusUpdates).
                bool showBar = false, wFire = false, wLeak = false;
                float health = 1f;
                if (filled && Def.GetBFor(LayoutBare, "damageWarn", true))
                {
                    try
                    {
                        bool leaking = occ.IsLeaking;
                        bool burning = occ.IsBurning;
                        float dmg = 0f;
                        try { if (occ.DamageState != null) dmg = Mathf.Clamp01(occ.DamageState.TotalRatioClamped); } catch { }
                        // Bar shows for any real damage (a <2% scuff stays quiet). Health is the
                        // exact slider value vanilla uses: 1 - TotalRatioClamped (MedicalAnalyser
                        // parity, SlotDisplay.RefreshDamage). A full break -> health 0 -> the fill
                        // is empty (bare track), same as vanilla's slider at value 0; a leaking or
                        // burning broken suit still raises its glyph on top.
                        showBar = dmg > 0.02f;
                        health = 1f - dmg;
                        // CAUSE precedence copied from vanilla SlotDisplay.RefreshState (27701):
                        //   StatusFire <- IsBurning ; StatusLeak <- !IsBurning && IsLeaking.
                        wFire = burning;
                        wLeak = leaking && !burning;
                    }
                    catch { showBar = false; wFire = false; wLeak = false; }
                }

                if (showBar)
                {
                    b.Bar.gameObject.SetActive(true);
                    b.Bar.Value = health;                    // full bar = healthy
                    // Vanilla's EXACT gradient colour for this damage ratio (0=green..1=red),
                    // pulled live off StatusUpdates.DamageGradient; our own ramp is only the
                    // pre-manager fallback (main menu / early load).
                    b.Bar.FillColor = Core.VanillaIcons.DamageColor(1f - health, DamageBarColor(health));
                    b.Bar.TrackColor = DamageBarTrack;
                }
                else b.Bar.gameObject.SetActive(false);

                if (wFire || wLeak)
                {
                    // The glyph vanilla itself draws for THIS cause, harvested live off a
                    // SlotDisplayButton so we always match the current build's art:
                    //   burning -> StatusFire, leaking -> StatusLeak. Fail-soft to the named
                    // leak lookup rather than drawing nothing if the slot UI isn't up yet.
                    Sprite ws = wFire ? Core.VanillaIcons.FireIcon() : Core.VanillaIcons.LeakIcon();
                    if (ws == null) ws = Core.VanillaIcons.LeakIcon() ?? Core.VanillaIcons.TryGet("leak");
                    b.Warn.sprite = ws;
                    b.Warn.gameObject.SetActive(ws != null);
                    if (ws != null)
                    {
                        // White -> red -> white pulse (vanilla's own DamageImage tint #ED1C24),
                        // ~0.66 s period ("rapid"). The red is the alarm; alpha-only read as "just
                        // white" in play-test, so the whole glyph cycles through red.
                        float t = Mathf.PingPong(Time.unscaledTime * 3f, 1f);
                        b.Warn.color = Color.Lerp(new Color(0.929f, 0.110f, 0.141f, 1f), Color.white, t);
                    }
                }
                else b.Warn.gameObject.SetActive(false);
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;

            int layoutStart = into.Count;
            into.Add(HudProp.F("Box gap", () => d.GetFFor(EditBare(d), "gap", 8f), v => d.SetFFor(EditBare(d), "gap", Mathf.Clamp(v, 0f, 40f)), 0f, 40f));
            // Tier-aware: "the column stacks when suited, rows along the bottom when bare" is a
            // look, not an identity, so it forks with the rest of the style (Wave C).
            into.Add(HudProp.Bool("Horizontal row", () => d.GetBFor(EditBare(d), "horizontal", false),
                v => d.SetBFor(EditBare(d), "horizontal", v)));
            // The slot thumbnail was a hard-coded 58% of the box with no knob. Mirrors the hand
            // boxes' icon controls so the two item-carrying widgets tune the same way.
            into.Add(HudProp.F("Icon scale (× box size)", () => d.GetFFor(EditBare(d), "iconScale", 0.58f),
                v => d.SetFFor(EditBare(d), "iconScale", Mathf.Clamp(v, 0.1f, 1f)), 0.1f, 1f));
            into.Add(HudProp.F("Icon X", () => d.GetFFor(EditBare(d), "iconDX", 0f), v => d.SetFFor(EditBare(d), "iconDX", v), -200f, 200f));
            into.Add(HudProp.F("Icon Y", () => d.GetFFor(EditBare(d), "iconDY", 0f), v => d.SetFFor(EditBare(d), "iconDY", v), -200f, 200f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            into.Add(HudProp.Bool("Show labels", () => d.GetBFor(EditBare(d), "labels", true), v => d.SetBFor(EditBare(d), "labels", v)));
            into.Add(HudProp.Bool("Damage bar + leak / fire alert", () => d.GetBFor(EditBare(d), "damageWarn", true), v => d.SetBFor(EditBare(d), "damageWarn", v)));
            into.Add(HudProp.F("Damage bar thickness (× box)", () => d.GetFFor(EditBare(d), "damageBarThick", 0.085f), v => d.SetFFor(EditBare(d), "damageBarThick", Mathf.Clamp(v, 0.03f, 0.25f)), 0.03f, 0.25f));
            into.Add(HudProp.F("Leak / fire glyph scale (× box)", () => d.GetFFor(EditBare(d), "warnScale", 0.72f), v => d.SetFFor(EditBare(d), "warnScale", Mathf.Clamp(v, 0.2f, 1.2f)), 0.2f, 1.2f));
            into.Add(HudProp.I("First slot (0=helmet)", () => d.GetI("first", 0), v => d.SetI("first", Mathf.Clamp(v, 0, 5)), 0, 5));
            into.Add(HudProp.I("Slot count", () => d.GetI("count", 6), v => d.SetI("count", Mathf.Clamp(v, 1, 6)), 1, 6));

            // Per-element text colours: the 1-6 slot number and the slot label (filled/empty) were
            // locked to the palette. Expose all three (empty ref = the palette default).
            into.Add(HudProp.Color("Slot number colour", () => d.GetSFor(EditBare(d), "numColor", ""),
                v => d.SetSFor(EditBare(d), "numColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.TextDim.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Slot label colour", () => d.GetSFor(EditBare(d), "labelColor", ""),
                v => d.SetSFor(EditBare(d), "labelColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.TextLabel.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Empty slot label colour", () => d.GetSFor(EditBare(d), "labelEmptyColor", ""),
                v => d.SetSFor(EditBare(d), "labelEmptyColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.TextDim.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            AddIconTintProps(into);      // item thumbnail wash (inherit / own colour / off)
            AddDropHighlightProps(into); // #4: drag-over drop cue mode + colour
        }

        /// <summary>Each equipment box takes chip drops for ITS worn slot (0.6.2): drop a
        /// helmet on box 1 to don it, a backpack on box 4, etc. Same geometry as Layout().</summary>
        public override void CollectDropZones(List<HudDropZone> into, HudSnapshot s, float scale)
        {
            var human = s?.Human;
            if (human == null) return;

            // Logical (unwarped) centre — drop zones meet the inverse-warped mouse.
            var c = CenterForLogical(scale);
            var size = SizeFor(scale);
            float gap = Def.GetFFor(LayoutBare, "gap", 8f) * scale;
            bool horizontal = Def.GetBFor(LayoutBare, "horizontal", false);
            int first, count;
            Slice(out first, out count);
            float box = BoxSize(size, gap, horizontal, count);

            for (int i = first; i < first + count; i++)
            {
                Slot slot = SlotFor(human, i);
                if (slot == null) continue;
                var center = BoxCenter(i - first, c, box, gap, horizontal, count);
                into.Add(new HudDropZone
                {
                    CanvasRect = new Rect(center.x - box * 0.5f, center.y - box * 0.5f, box, box),
                    Slot = slot,
                    Label = Labels[i],
                });
            }
        }

        // --- shared geometry ---
        // The stacking axis carries the six boxes; the cross axis bounds their size. Both
        // Layout and the drop zones read these so the hit-rects match the pixels exactly.

        /// <summary>Which contiguous run of the six slots this element renders — two
        /// sliced elements make the "1 2 3 [hands] 4 5 6" bottom row possible.</summary>
        private void Slice(out int first, out int count)
        {
            first = Mathf.Clamp(Def.GetI("first", 0), 0, 5);
            count = Mathf.Clamp(Def.GetI("count", 6), 1, 6 - first);
        }

        private static float BoxSize(Vector2 rect, float gap, bool horizontal, int count)
        {
            float along = horizontal ? rect.x : rect.y;
            float across = horizontal ? rect.y : rect.x;
            return Mathf.Max(2f, Mathf.Min(across, (along - (count - 1) * gap) / count));
        }

        private static Vector2 BoxCenter(int orderIdx, Vector2 center, float box, float gap,
            bool horizontal, int count)
        {
            float total = box * count + gap * (count - 1);
            float start = total * 0.5f - box * 0.5f;
            // The first sliced box leads: leftmost when horizontal, topmost when vertical.
            return horizontal
                ? new Vector2(center.x - start + orderIdx * (box + gap), center.y)
                : new Vector2(center.x, center.y + start - orderIdx * (box + gap));
        }

        // --- damage bar colouring ---
        // Live fill colour is vanilla's own StatusUpdates.DamageGradient (see UpdatePanel /
        // VanillaIcons.DamageColor). DamageBarColor below is only the FALLBACK ramp used before
        // that manager exists — a two-stop green->yellow->red lerp through yellow at the midpoint.
        private static readonly Color DamageBarTrack = new Color(0f, 0f, 0f, 0.55f);

        private static Color DamageBarColor(float health)
        {
            health = Mathf.Clamp01(health);
            Color green = new Color(0.30f, 0.85f, 0.35f, 1f);
            Color yellow = new Color(0.95f, 0.80f, 0.15f, 1f);
            Color red = new Color(0.90f, 0.20f, 0.18f, 1f);
            return health > 0.5f
                ? Color.Lerp(yellow, green, (health - 0.5f) * 2f)
                : Color.Lerp(red, yellow, health * 2f);
        }

        private static Slot SlotFor(Human human, int i)
        {
            if (human == null) return null;
            try
            {
                switch (i)
                {
                    case 0: return human.HelmetSlot;
                    case 1: return human.GlassesSlot;
                    case 2: return human.SuitSlot;
                    case 3: return human.BackpackSlot;
                    case 4: return human.UniformSlot;
                    case 5: return human.ToolbeltSlot;
                }
            }
            catch { }
            return null;
        }
    }
}
