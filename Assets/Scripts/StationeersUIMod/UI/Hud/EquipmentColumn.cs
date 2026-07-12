using System.Collections.Generic;
using Assets.Scripts.Objects;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The left equipment column from the concept art: six rounded boxes — key number,
    /// live thumbnail, slot name — matching the 1-6 equipment keys exactly (helmet,
    /// glasses, suit, back, uniform, belt). The robot's uniform slot is its battery.
    /// Shown at every tier: knowing what you wear is not a suit sensor.
    /// </summary>
    internal sealed class EquipmentColumn : HudPanel
    {
        public override string Id => "Equipment";
        public override bool SuitTier => false;
        public override ConfigEntry<bool> Toggle => HudConfig.ShowEquipment;
        public override bool VisibleAt(HudTier tier) => true;

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
            float size = HudConfig.EquipBoxSize.Value * scale;
            float gap = HudConfig.EquipSpacing.Value * scale;
            float x = -Screen.width * 0.5f + 24f + size * 0.5f;
            float total = size * 6f + gap * 5f;
            float top = total * 0.5f - size * 0.5f;
            Root.anchoredPosition = new Vector2(0f, 0f);

            for (int i = 0; i < 6; i++)
            {
                float y = top - i * (size + gap);
                var b = _boxes[i];
                var prt = (RectTransform)b.Panel.transform;
                prt.anchoredPosition = new Vector2(x, y);
                b.Panel.SetShape(size, size, Mathf.Min(HudConfig.CornerRadius.Value, size * 0.25f));
                b.Number.rectTransform.sizeDelta = new Vector2(size - 12f, 14f);
                b.Number.rectTransform.anchoredPosition = new Vector2(x, y + size * 0.5f - 11f);
                b.Icon.rectTransform.sizeDelta = new Vector2(size * 0.58f, size * 0.58f);
                b.Icon.rectTransform.anchoredPosition = new Vector2(x, y + size * 0.04f);
                b.Label.rectTransform.sizeDelta = new Vector2(size + 14f, 12f);
                b.Label.rectTransform.anchoredPosition = new Vector2(x, y - size * 0.5f + 10f);
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var human = s.Human;
            for (int i = 0; i < 6; i++)
            {
                var b = _boxes[i];
                Slot slot = null;
                try
                {
                    switch (i)
                    {
                        case 0: slot = human.HelmetSlot; break;
                        case 1: slot = human.GlassesSlot; break;
                        case 2: slot = human.SuitSlot; break;
                        case 3: slot = human.BackpackSlot; break;
                        case 4: slot = human.UniformSlot; break;
                        case 5: slot = human.ToolbeltSlot; break;
                    }
                }
                catch { }

                DynamicThing occ = null;
                try { occ = slot?.Get(); } catch { }
                bool filled = occ != null;

                var fill = HudPalette.PanelFill.Value;
                var border = HudPalette.PanelBorder.Value;
                if (!filled) { fill.a *= 0.55f; border.a *= 0.45f; }
                b.Panel.color = fill;
                b.Panel.BorderColor = border;
                b.Panel.BorderWidth = HudConfig.BorderWidth.Value;

                HudText.Sync(b.Number);
                b.Number.fontSize = HudText.Size(11f) * scale;
                b.Number.color = HudPalette.SlotNumber.Value;
                HudText.Set(b.Number, (i + 1).ToString());

                Sprite icon = null;
                if (filled) { try { icon = occ.GetThumbnail(); } catch { } }
                b.Icon.sprite = icon;
                b.Icon.enabled = icon != null;
                b.Icon.color = new Color(1f, 1f, 1f, filled ? 0.95f : 0f);

                HudText.Sync(b.Label);
                b.Label.fontSize = HudText.Size(HudConfig.LabelFontSize.Value * 0.9f) * scale;
                var lc = filled ? HudPalette.TextLabel.Value : HudPalette.TextDim.Value;
                if (!filled) lc.a *= 0.7f;
                b.Label.color = lc;
                HudText.Set(b.Label, i == 4 && s.IsRobot ? "BATTERY" : Labels[i]);
            }
        }

        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            float size = HudConfig.EquipBoxSize.Value * scale;
            float gap = HudConfig.EquipSpacing.Value * scale;
            float x = -Screen.width * 0.5f + 24f + size * 0.5f;
            float total = size * 6f + gap * 5f;
            into.Add(new HudEditTarget
            {
                Title = "Equipment column",
                Palette = new[] { "HudPanelFill", "HudPanelBorder", "HudSlotNumber", "HudTextLabel", "HudTextDim" },
                Values = new ConfigEntryBase[] { HudConfig.ShowEquipment, HudConfig.EquipBoxSize,
                    HudConfig.EquipSpacing, HudConfig.CornerRadius, HudConfig.LabelFontSize },
                CanvasRect = new Rect(x - size * 0.5f, -total * 0.5f, size, total),
            });
        }

        /// <summary>Each equipment box takes chip drops for ITS worn slot (0.6.2): drop a
        /// helmet on box 1 to don it, a backpack on box 4, etc. Same geometry as Layout().</summary>
        public override void CollectDropZones(List<HudDropZone> into, HudSnapshot s, float scale)
        {
            var human = s?.Human;
            if (human == null) return;
            float size = HudConfig.EquipBoxSize.Value * scale;
            float gap = HudConfig.EquipSpacing.Value * scale;
            float x = -Screen.width * 0.5f + 24f + size * 0.5f;
            float total = size * 6f + gap * 5f;
            float top = total * 0.5f - size * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                Slot slot = null;
                try
                {
                    switch (i)
                    {
                        case 0: slot = human.HelmetSlot; break;
                        case 1: slot = human.GlassesSlot; break;
                        case 2: slot = human.SuitSlot; break;
                        case 3: slot = human.BackpackSlot; break;
                        case 4: slot = human.UniformSlot; break;
                        case 5: slot = human.ToolbeltSlot; break;
                    }
                }
                catch { }
                if (slot == null) continue;
                float y = top - i * (size + gap);
                into.Add(new HudDropZone
                {
                    CanvasRect = new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                    Slot = slot,
                    Label = Labels[i],
                });
            }
        }
    }
}
