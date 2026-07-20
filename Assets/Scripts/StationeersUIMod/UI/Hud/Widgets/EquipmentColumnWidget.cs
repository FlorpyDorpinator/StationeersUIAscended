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
        private static readonly string[] Labels = { "HELMET", "GLASSES", "SUIT", "BACK", "UNIFORM", "BELT" };
        // The 1-6 slot digits are constant; hold them so the per-frame text set never allocates.
        private static readonly string[] SlotNums = { "1", "2", "3", "4", "5", "6" };

        private sealed class Box
        {
            public PanelGraphic Panel;
            public TextMeshProUGUI Number, Label;
            public Image Icon;
            public Image Warn; // damage/leak/broken alert overlay (vanilla parity)
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
                b.Label = HudText.Make(root, "Label" + i, 10f, TextAlignmentOptions.Center);
                // Created LAST so it draws on top of icon/label: the damage/leak alert overlay.
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
            bool horizontal = Def.GetB("horizontal", false);
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
                if (!inSlice) { b.Warn.gameObject.SetActive(false); continue; }
                var center = BoxCenter(i - first, c, box, gap, horizontal, count);

                var prt = (RectTransform)b.Panel.transform;
                prt.anchoredPosition = center;
                float rr = box * 0.25f;
                b.Panel.SetShape(box, box,
                    Mathf.Min(Radius(Def.RTLFor(LayoutBare)), rr), Mathf.Min(Radius(Def.RTRFor(LayoutBare)), rr),
                    Mathf.Min(Radius(Def.RBRFor(LayoutBare)), rr), Mathf.Min(Radius(Def.RBLFor(LayoutBare)), rr));

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

                // Damage alert overlay — centred over the item like vanilla's DamageImage X, sized
                // to cover the thumbnail (UpdatePanel toggles it and drives the red<->white pulse).
                float warnSize = box * Mathf.Clamp(Def.GetFFor(LayoutBare, "warnScale", 0.72f), 0.2f, 1.2f);
                b.Warn.rectTransform.sizeDelta = new Vector2(warnSize, warnSize);
                b.Warn.rectTransform.anchoredPosition = center;
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var human = s.Human;
            int first, count;
            Slice(out first, out count);
            var box = BoxSize(SizeFor(scale), Def.GetFFor(LayoutBare, "gap", 8f) * scale,
                Def.GetB("horizontal", false), count);
            bool labels = Def.GetBFor(LayoutBare, "labels", true);

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
                // "make the numbers more grey so it isn't so bright").
                b.Number.color = HudPalette.TextDim.Value;
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
                b.Icon.color = new Color(1f, 1f, 1f, (filled || flash != null) ? 0.95f : 0f);
                b.Icon.rectTransform.localScale = Vector3.one * (flash != null ? 1f + 0.22f * flashP : 1f);

                HudText.Sync(b.Label);
                b.Label.enabled = labels;
                b.Label.fontSize = HudText.Size(9.9f * Def.FontScaleFor(LayoutBare)) * scale;
                var lc = filled ? HudPalette.TextLabel.Value : HudPalette.TextDim.Value;
                if (!filled) lc.a *= 0.7f;
                b.Label.color = lc;
                HudText.Set(b.Label, i == 4 && s.IsRobot ? "BATTERY" : Labels[i]);

                // Damage / leak / broken alert. Vanilla lights an animated warning on a damaged
                // suit or helmet inventory slot; we mirror it on our boxes. IsLeaking (a leaking
                // suit) and IsBurning are NETWORKED (MP-safe on a client); IsBroken/DamageState
                // are best-effort. The pulse supplies the "glow" without borrowing vanilla's
                // prefab Animator. Leak sprite is vanilla's own (VanillaIcons -> StatusUpdates).
                bool warn = false, wFire = false, wLeak = false;
                if (filled && Def.GetBFor(LayoutBare, "damageWarn", true))
                {
                    try
                    {
                        bool broken = occ.IsBroken;
                        bool leaking = occ.IsLeaking;
                        bool burning = occ.IsBurning;
                        float dmg = 0f;
                        try { if (occ.DamageState != null) dmg = occ.DamageState.TotalRatioClamped; } catch { }
                        // Vanilla shows the broken-X only for a FULL break; the mod also raises it
                        // for a leak / fire / heavy damage, the "your gear is failing" states the
                        // alert is for. Light scuffs (dmg < ~0.2) stay quiet.
                        warn = broken || leaking || burning || dmg > 0.2f;
                        // CAUSE precedence copied from vanilla SlotDisplay.RefreshState (27701):
                        //   StatusFire  <- IsBurning
                        //   StatusLeak  <- !IsBurning && IsLeaking
                        // and the broken-X (StateImage) is its own break-only state. A leaking suit
                        // must therefore draw the LEAK glyph, never the X (play-test: "it's still an X").
                        wFire = burning;
                        wLeak = leaking && !burning;
                    }
                    catch { warn = false; }
                }

                if (warn)
                {
                    // The glyph vanilla itself would draw for THIS cause, harvested live off a
                    // SlotDisplayButton so we always match the current build's art:
                    //   burning -> StatusFire, leaking -> StatusLeak, broken/heavy damage -> StateImage (X).
                    // Fail-soft chain: the slot UI may not exist yet, so fall back to the X and then
                    // the legacy named lookup rather than drawing nothing.
                    Sprite ws = wFire ? Core.VanillaIcons.FireIcon()
                              : wLeak ? Core.VanillaIcons.LeakIcon()
                              : Core.VanillaIcons.BrokenCross();
                    if (ws == null) ws = Core.VanillaIcons.BrokenCross() ?? Core.VanillaIcons.TryGet("leak");
                    b.Warn.sprite = ws;
                    b.Warn.gameObject.SetActive(ws != null);
                    if (ws != null)
                    {
                        // ONE pulse for every alert glyph (leak / fire / broken): white -> red ->
                        // white, endlessly. FlorpyDorp: an alpha-only pulse on the leak icon read as
                        // "just white" — the red is the alarm, so every state cycles through it.
                        // #ED1C24 is vanilla's own DamageImage tint; ~0.66 s period ("rapid").
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
            into.Add(HudProp.Bool("Horizontal row", () => d.GetB("horizontal", false), v => d.SetB("horizontal", v)));
            // The slot thumbnail was a hard-coded 58% of the box with no knob. Mirrors the hand
            // boxes' icon controls so the two item-carrying widgets tune the same way.
            into.Add(HudProp.F("Icon scale (× box size)", () => d.GetFFor(EditBare(d), "iconScale", 0.58f),
                v => d.SetFFor(EditBare(d), "iconScale", Mathf.Clamp(v, 0.1f, 1f)), 0.1f, 1f));
            into.Add(HudProp.F("Icon X", () => d.GetFFor(EditBare(d), "iconDX", 0f), v => d.SetFFor(EditBare(d), "iconDX", v), -200f, 200f));
            into.Add(HudProp.F("Icon Y", () => d.GetFFor(EditBare(d), "iconDY", 0f), v => d.SetFFor(EditBare(d), "iconDY", v), -200f, 200f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            into.Add(HudProp.Bool("Show labels", () => d.GetBFor(EditBare(d), "labels", true), v => d.SetBFor(EditBare(d), "labels", v)));
            into.Add(HudProp.Bool("Damage alert (leak / fire / broken)", () => d.GetBFor(EditBare(d), "damageWarn", true), v => d.SetBFor(EditBare(d), "damageWarn", v)));
            into.Add(HudProp.F("Alert overlay scale (× box)", () => d.GetFFor(EditBare(d), "warnScale", 0.72f), v => d.SetFFor(EditBare(d), "warnScale", Mathf.Clamp(v, 0.2f, 1.2f)), 0.2f, 1.2f));
            into.Add(HudProp.I("First slot (0=helmet)", () => d.GetI("first", 0), v => d.SetI("first", Mathf.Clamp(v, 0, 5)), 0, 5));
            into.Add(HudProp.I("Slot count", () => d.GetI("count", 6), v => d.SetI("count", Mathf.Clamp(v, 1, 6)), 1, 6));

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
            bool horizontal = Def.GetB("horizontal", false);
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
