using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// The bottom hand tray as a document element: a trapezoid shelf carrying exactly TWO
    /// boxes — LEFT HAND and RIGHT HAND, live thumbnails, a state line, and the active-hand
    /// accent (border + side bar). Never a hotbar. The element rect IS the tray; the two
    /// boxes are laid out inside it so a designer can move and resize the whole thing while
    /// the sub-parts keep their proportions. Both boxes remain chip drop targets.
    /// </summary>
    internal sealed class HandBoxesWidget : HudElementView
    {
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

        /// <summary>Box geometry derived from the element rect: the pair (plus the gap between
        /// them) fills ~72% of the rect width, leaving the trapezoid shoulders; each box is
        /// ~78% of the rect height. Shared by Layout and the drop-zone map so they never drift.</summary>
        private void BoxMetrics(float scale, out float boxW, out float boxH, out float boxX)
        {
            var s = SizeFor(scale);
            float gap = Def.GetF("gap", 22f) * scale;
            float areaW = s.x * 0.72f;
            boxW = Mathf.Max(8f, (areaW - gap) * 0.5f);
            boxH = Mathf.Max(8f, s.y * 0.78f);
            boxX = gap * 0.5f + boxW * 0.5f;
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            ((RectTransform)_tray.transform).anchoredPosition = c;
            _tray.SetShape(s.x, s.y,
                Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL),
                topInset: s.x * 0.116f);

            float boxW, boxH, boxX;
            BoxMetrics(scale, out boxW, out boxH, out boxX);
            float boxRadius = Mathf.Min(Radius(Def.RTL), 12f);

            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? -1f : 1f;
                float x = c.x + sign * boxX;
                float y = c.y;

                ((RectTransform)_box[i].transform).anchoredPosition = new Vector2(x, y);
                _box[i].SetShape(boxW, boxH, boxRadius);

                ((RectTransform)_accent[i].transform).anchoredPosition =
                    new Vector2(x + sign * (boxW * 0.5f - 2.4f * scale), y);
                _accent[i].SetShape(3.2f * scale, boxH * 0.72f, 1.4f);

                _title[i].rectTransform.sizeDelta = new Vector2(boxW, 14f * scale);
                _title[i].rectTransform.anchoredPosition = new Vector2(x, y + boxH * 0.5f - 11f * scale);

                _icon[i].rectTransform.sizeDelta = new Vector2(boxH * 0.52f, boxH * 0.52f);
                _icon[i].rectTransform.anchoredPosition = new Vector2(x, y - boxH * 0.02f);

                _state[i].rectTransform.sizeDelta = new Vector2(boxW, 12f * scale);
                _state[i].rectTransform.anchoredPosition = new Vector2(x, y - boxH * 0.5f + 10f * scale);
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            bool trayOn = Def.GetB("tray", true);
            _tray.enabled = trayOn;
            if (trayOn)
            {
                var trayFill = FillColor(); trayFill.a *= 0.75f;
                _tray.color = trayFill;
                var trayBorder = BorderColor(); trayBorder.a *= 0.55f;
                _tray.BorderColor = trayBorder;
                _tray.BorderWidth = BorderWidthFor();
                ApplyGlass(_tray);
            }

            float labelSize = HudConfig.LabelFontSize != null ? HudConfig.LabelFontSize.Value : 11f;

            for (int i = 0; i < 2; i++)
            {
                bool isRight = i == 1;
                bool active = isRight == s.RightHandActive;

                Slot slot = null;
                try { slot = isRight ? s.Human.RightHandSlot : s.Human.LeftHandSlot; } catch { }
                DynamicThing occ = null;
                try { occ = slot?.Get(); } catch { }

                _box[i].color = FillColor();
                _box[i].BorderColor = active ? HudPalette.ActiveHandAccent.Value : BorderColor();
                _box[i].BorderWidth = BorderWidthFor() * (active ? 1.4f : 1f);
                // The active border IS the which-hand cue — specular whitening at a lit
                // corner would wash the accent out, so the active box never catches
                // light. Set once (not ApplyGlass-then-override: a per-frame value
                // flip-flop would defeat the dirty-guard and rebuild the mesh).
                _box[i].Sheen = Def.GetF("sheen", 0f);
                _box[i].Spec = active ? 0f : Def.GetF("spec", 0f);

                var ac = HudPalette.ActiveHandAccent.Value;
                if (!active) ac.a = 0f;
                _accent[i].color = ac;
                _accent[i].BorderColor = Color.clear;

                HudText.Sync(_title[i]);
                _title[i].fontSize = HudText.Size(labelSize * Def.FontScale) * scale;
                _title[i].color = active ? TextColor() : HudPalette.TextLabel.Value;
                HudText.Set(_title[i], isRight ? "RIGHT HAND" : "LEFT HAND");

                Sprite icon = null;
                if (occ != null) { try { icon = occ.GetThumbnail(); } catch { } }
                _icon[i].sprite = icon;
                _icon[i].enabled = icon != null;
                _icon[i].color = Color.white;

                HudText.Sync(_state[i]);
                _state[i].fontSize = HudText.Size(labelSize * 0.9f * Def.FontScale) * scale;
                string state = "";
                if (occ == null) state = "empty";
                else { try { state = StateText.For(occ) ?? ""; } catch { } }
                _state[i].color = occ == null ? HudPalette.TextDim.Value : TextColor();
                HudText.Set(_state[i], state);
            }
        }

        /// <summary>Dragged radial chips can be dropped straight onto either hand box
        /// (0.6.2). Same box geometry as Layout().</summary>
        public override void CollectDropZones(List<HudDropZone> into, HudSnapshot s, float scale)
        {
            var human = s?.Human;
            if (human == null) return;
            // Logical (unwarped) centre: drop zones are hit-tested with the inverse-warped
            // mouse, so they must live where the box logically is, not where it draws.
            var c = CenterForLogical(scale);
            float boxW, boxH, boxX;
            BoxMetrics(scale, out boxW, out boxH, out boxX);
            for (int i = 0; i < 2; i++)
            {
                Slot slot = null;
                try { slot = i == 0 ? human.LeftHandSlot : human.RightHandSlot; } catch { }
                if (slot == null) continue;
                float x = c.x + (i == 0 ? -1f : 1f) * boxX;
                into.Add(new HudDropZone
                {
                    CanvasRect = new Rect(x - boxW * 0.5f, c.y - boxH * 0.5f, boxW, boxH),
                    Slot = slot,
                    Label = i == 0 ? "Left hand" : "Right hand",
                });
            }
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.F("Box gap", () => d.GetF("gap", 22f),
                v => d.SetF("gap", Mathf.Max(0f, v)), 0f, 160f));
            into.Add(HudProp.Bool("Show tray shelf", () => d.GetB("tray", true),
                v => d.SetB("tray", v)));
        }
    }
}
