using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
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

        private sealed class Box
        {
            public PanelGraphic Panel;
            public TextMeshProUGUI Number, Label;
            public Image Icon;
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
                _boxes.Add(b);
            }
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;

            var c = CenterFor(scale);
            var s = SizeFor(scale);
            float gap = Def.GetF("gap", 8f) * scale;
            bool horizontal = Def.GetB("horizontal", false);
            bool labels = Def.GetB("labels", true);
            float box = BoxSize(s, gap, horizontal);

            for (int i = 0; i < 6; i++)
            {
                var b = _boxes[i];
                var center = BoxCenter(i, c, box, gap, horizontal);

                var prt = (RectTransform)b.Panel.transform;
                prt.anchoredPosition = center;
                float rr = box * 0.25f;
                b.Panel.SetShape(box, box,
                    Mathf.Min(Radius(Def.RTL), rr), Mathf.Min(Radius(Def.RTR), rr),
                    Mathf.Min(Radius(Def.RBR), rr), Mathf.Min(Radius(Def.RBL), rr));

                b.Number.rectTransform.sizeDelta = new Vector2(box - box * 0.16f, box * 0.18f);
                b.Number.rectTransform.anchoredPosition = new Vector2(center.x, center.y + box * 0.5f - box * 0.145f);

                b.Icon.rectTransform.sizeDelta = new Vector2(box * 0.58f, box * 0.58f);
                b.Icon.rectTransform.anchoredPosition = new Vector2(center.x, center.y + box * 0.04f);

                b.Label.rectTransform.sizeDelta = new Vector2(box + box * 0.18f, box * 0.16f);
                b.Label.rectTransform.anchoredPosition = new Vector2(center.x, center.y - box * 0.5f + box * 0.13f);
                b.Label.enabled = labels;
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var human = s.Human;
            var box = BoxSize(SizeFor(scale), Def.GetF("gap", 8f) * scale, Def.GetB("horizontal", false));
            bool labels = Def.GetB("labels", true);

            for (int i = 0; i < 6; i++)
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

                HudText.Sync(b.Number);
                b.Number.fontSize = HudText.Size(11f * Def.FontScale) * scale;
                b.Number.color = HudPalette.SlotNumber.Value;
                HudText.Set(b.Number, (i + 1).ToString());

                Sprite icon = null;
                if (filled) { try { icon = occ.GetThumbnail(); } catch { } }
                b.Icon.sprite = icon;
                b.Icon.enabled = icon != null;
                b.Icon.color = new Color(1f, 1f, 1f, filled ? 0.95f : 0f);

                HudText.Sync(b.Label);
                b.Label.enabled = labels;
                b.Label.fontSize = HudText.Size(9.9f * Def.FontScale) * scale;
                var lc = filled ? HudPalette.TextLabel.Value : HudPalette.TextDim.Value;
                if (!filled) lc.a *= 0.7f;
                b.Label.color = lc;
                HudText.Set(b.Label, i == 4 && s.IsRobot ? "BATTERY" : Labels[i]);
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Box gap", () => d.GetF("gap", 8f), v => d.SetF("gap", Mathf.Clamp(v, 0f, 40f)), 0f, 40f));
            into.Add(HudProp.Bool("Horizontal row", () => d.GetB("horizontal", false), v => d.SetB("horizontal", v)));
            into.Add(HudProp.Bool("Show labels", () => d.GetB("labels", true), v => d.SetB("labels", v)));
        }

        /// <summary>Each equipment box takes chip drops for ITS worn slot (0.6.2): drop a
        /// helmet on box 1 to don it, a backpack on box 4, etc. Same geometry as Layout().</summary>
        public override void CollectDropZones(List<HudDropZone> into, HudSnapshot s, float scale)
        {
            var human = s?.Human;
            if (human == null) return;

            var c = CenterFor(scale);
            var size = SizeFor(scale);
            float gap = Def.GetF("gap", 8f) * scale;
            bool horizontal = Def.GetB("horizontal", false);
            float box = BoxSize(size, gap, horizontal);

            for (int i = 0; i < 6; i++)
            {
                Slot slot = SlotFor(human, i);
                if (slot == null) continue;
                var center = BoxCenter(i, c, box, gap, horizontal);
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

        private static float BoxSize(Vector2 rect, float gap, bool horizontal)
        {
            float along = horizontal ? rect.x : rect.y;
            float across = horizontal ? rect.y : rect.x;
            return Mathf.Max(2f, Mathf.Min(across, (along - 5f * gap) / 6f));
        }

        private static Vector2 BoxCenter(int i, Vector2 center, float box, float gap, bool horizontal)
        {
            float total = box * 6f + gap * 5f;
            float start = total * 0.5f - box * 0.5f;
            // Box 0 (helmet) leads: leftmost when horizontal, topmost when vertical.
            return horizontal
                ? new Vector2(center.x - start + i * (box + gap), center.y)
                : new Vector2(center.x, center.y + start - i * (box + gap));
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
