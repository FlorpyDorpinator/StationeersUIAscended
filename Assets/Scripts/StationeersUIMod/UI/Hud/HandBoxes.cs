using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using BepInEx.Configuration;
using StationeersUIMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The bottom-centre hand tray: a trapezoid shelf carrying exactly TWO boxes — LEFT
    /// HAND and RIGHT HAND, live thumbnails, state text, and the active-hand accent.
    /// Never a hotbar. Present at every tier (your hands are not suit sensors).
    /// </summary>
    internal sealed class HandBoxes : HudPanel
    {
        public override string Id => "Hands";
        public override bool SuitTier => false;
        public override ConfigEntry<bool> Toggle => HudConfig.ShowHands;
        public override bool VisibleAt(HudTier tier) => true;

        private PanelGraphic _tray;
        private readonly PanelGraphic[] _box = new PanelGraphic[2];
        private readonly PanelGraphic[] _accent = new PanelGraphic[2];
        private readonly TextMeshProUGUI[] _title = new TextMeshProUGUI[2];
        private readonly TextMeshProUGUI[] _state = new TextMeshProUGUI[2];
        private readonly Image[] _icon = new Image[2];

        protected override void BuildContent(RectTransform root)
        {
            _tray = MakePanel(root, "Tray");
            for (int i = 0; i < 2; i++)
            {
                _box[i] = MakePanel(root, "Box" + i);
                _accent[i] = MakeBar(root, "Accent" + i);
                _title[i] = HudText.Make(root, "Title" + i, 11f, TextAlignmentOptions.Center);
                _icon[i] = MakeIcon(root, "Icon" + i);
                _state[i] = HudText.Make(root, "State" + i, 10f, TextAlignmentOptions.Center);
            }
        }

        public override void Layout(float scale)
        {
            float w = HudConfig.HandBoxWidth.Value * scale;
            float h = HudConfig.HandBoxHeight.Value * scale;
            float gap = 22f * scale;
            float baseY = -Screen.height * 0.5f + h * 0.5f + 16f;
            Root.anchoredPosition = Vector2.zero;

            var trt = (RectTransform)_tray.transform;
            trt.anchoredPosition = new Vector2(0f, baseY - 4f);
            _tray.SetShape(w * 2f + gap + 110f, h + 26f, 12f, topInset: 46f);

            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1f : 1f) * (gap * 0.5f + w * 0.5f);
                ((RectTransform)_box[i].transform).anchoredPosition = new Vector2(x, baseY);
                _box[i].SetShape(w, h, Mathf.Min(HudConfig.CornerRadius.Value, 12f));
                ((RectTransform)_accent[i].transform).anchoredPosition =
                    new Vector2(x + (i == 0 ? -1f : 1f) * (w * 0.5f - 2.4f), baseY);
                _accent[i].SetShape(3.2f, h * 0.72f, 1.4f);
                _title[i].rectTransform.sizeDelta = new Vector2(w, 14f);
                _title[i].rectTransform.anchoredPosition = new Vector2(x, baseY + h * 0.5f - 11f);
                _icon[i].rectTransform.sizeDelta = new Vector2(h * 0.52f, h * 0.52f);
                _icon[i].rectTransform.anchoredPosition = new Vector2(x, baseY - h * 0.02f);
                _state[i].rectTransform.sizeDelta = new Vector2(w, 12f);
                _state[i].rectTransform.anchoredPosition = new Vector2(x, baseY - h * 0.5f + 10f);
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            var trayFill = HudPalette.PanelFill.Value;
            trayFill.a *= 0.75f;
            _tray.color = trayFill;
            var trayBorder = HudPalette.PanelBorder.Value;
            trayBorder.a *= 0.55f;
            _tray.BorderColor = trayBorder;
            _tray.BorderWidth = HudConfig.BorderWidth.Value;

            for (int i = 0; i < 2; i++)
            {
                bool isRight = i == 1;
                bool active = isRight == s.RightHandActive;

                Slot slot = null;
                try { slot = isRight ? s.Human.RightHandSlot : s.Human.LeftHandSlot; } catch { }
                DynamicThing occ = null;
                try { occ = slot?.Get(); } catch { }

                _box[i].color = HudPalette.PanelFill.Value;
                _box[i].BorderColor = active ? HudPalette.ActiveHandAccent.Value : HudPalette.PanelBorder.Value;
                _box[i].BorderWidth = HudConfig.BorderWidth.Value * (active ? 1.4f : 1f);

                var ac = HudPalette.ActiveHandAccent.Value;
                if (!active) ac.a = 0f;
                _accent[i].color = ac;
                _accent[i].BorderColor = Color.clear;

                HudText.Sync(_title[i]);
                _title[i].fontSize = HudText.Size(HudConfig.LabelFontSize.Value) * scale;
                _title[i].color = active ? HudPalette.TextValue.Value : HudPalette.TextLabel.Value;
                HudText.Set(_title[i], isRight ? "RIGHT HAND" : "LEFT HAND");

                Sprite icon = null;
                if (occ != null) { try { icon = occ.GetThumbnail(); } catch { } }
                _icon[i].sprite = icon;
                _icon[i].enabled = icon != null;
                _icon[i].color = Color.white;

                HudText.Sync(_state[i]);
                _state[i].fontSize = HudText.Size(HudConfig.LabelFontSize.Value * 0.9f) * scale;
                string state = "";
                if (occ == null) state = "empty";
                else { try { state = StateText.For(occ) ?? ""; } catch { } }
                _state[i].color = occ == null ? HudPalette.TextDim.Value : HudPalette.TextValue.Value;
                HudText.Set(_state[i], state);
            }
        }

        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            float w = HudConfig.HandBoxWidth.Value * scale;
            float h = HudConfig.HandBoxHeight.Value * scale;
            float baseY = -Screen.height * 0.5f + h * 0.5f + 16f;
            float trayW = w * 2f + 22f * scale + 110f;
            into.Add(new HudEditTarget
            {
                Title = "Hand boxes",
                Palette = new[] { "HudPanelFill", "HudPanelBorder", "HudActiveHand",
                    "HudTextLabel", "HudTextValue", "HudTextDim" },
                Values = new ConfigEntryBase[] { HudConfig.ShowHands, HudConfig.HandBoxWidth,
                    HudConfig.HandBoxHeight, HudConfig.CornerRadius, HudConfig.LabelFontSize },
                CanvasRect = new Rect(-trayW * 0.5f, baseY - h * 0.5f - 17f, trayW, h + 30f),
            });
        }

        /// <summary>Dragged radial chips can be dropped straight onto either hand box
        /// (0.6.2). Same box geometry as Layout().</summary>
        public override void CollectDropZones(List<HudDropZone> into, HudSnapshot s, float scale)
        {
            var human = s?.Human;
            if (human == null) return;
            float w = HudConfig.HandBoxWidth.Value * scale;
            float h = HudConfig.HandBoxHeight.Value * scale;
            float gap = 22f * scale;
            float baseY = -Screen.height * 0.5f + h * 0.5f + 16f;
            for (int i = 0; i < 2; i++)
            {
                Slot slot = null;
                try { slot = i == 0 ? human.LeftHandSlot : human.RightHandSlot; } catch { }
                if (slot == null) continue;
                float x = (i == 0 ? -1f : 1f) * (gap * 0.5f + w * 0.5f);
                into.Add(new HudDropZone
                {
                    CanvasRect = new Rect(x - w * 0.5f, baseY - h * 0.5f, w, h),
                    Slot = slot,
                    Label = i == 0 ? "Left hand" : "Right hand",
                });
            }
        }
    }
}
