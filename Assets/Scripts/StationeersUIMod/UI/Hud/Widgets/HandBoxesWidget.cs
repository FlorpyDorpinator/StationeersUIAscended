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
            float gap = Def.GetFFor(LayoutBare, "gap", 22f) * scale;
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
                Radius(Def.RTLFor(LayoutBare)), Radius(Def.RTRFor(LayoutBare)), Radius(Def.RBRFor(LayoutBare)), Radius(Def.RBLFor(LayoutBare)),
                topInset: s.x * 0.116f);

            float boxW, boxH, boxX;
            BoxMetrics(scale, out boxW, out boxH, out boxX);
            float boxRadius = Mathf.Min(Radius(Def.RTLFor(LayoutBare)), 12f);

            // F9 nudges for the two text rows (reference px × scale). Applied identically to both
            // hands so the pair stays symmetric — "+X" shifts both labels the same screen direction.
            float titleDX = Def.GetFFor(LayoutBare, "titleDX", 0f) * scale;
            float titleDY = Def.GetFFor(LayoutBare, "titleDY", 0f) * scale;
            float stateDX = Def.GetFFor(LayoutBare, "stateDX", 0f) * scale;
            float stateDY = Def.GetFFor(LayoutBare, "stateDY", 0f) * scale;

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
                _title[i].rectTransform.anchoredPosition =
                    new Vector2(x + titleDX, y + boxH * 0.5f - 11f * scale + titleDY);

                // Thumbnail size as a fraction of box height (0.52 = the long-standing hard-coded
                // look, so an un-keyed profile is pixel-identical). Nudges match the text rows'
                // reference-px × scale convention and are mirrored across both hands.
                float iconSize = boxH * Mathf.Clamp(Def.GetFFor(LayoutBare, "iconScale", 0.52f), 0.1f, 1f);
                _icon[i].rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                _icon[i].rectTransform.anchoredPosition = new Vector2(
                    x + Def.GetFFor(LayoutBare, "iconDX", 0f) * scale,
                    y - boxH * 0.02f + Def.GetFFor(LayoutBare, "iconDY", 0f) * scale);

                _state[i].rectTransform.sizeDelta = new Vector2(boxW, 12f * scale);
                _state[i].rectTransform.anchoredPosition =
                    new Vector2(x + stateDX, y - boxH * 0.5f + 10f * scale + stateDY);
            }
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            bool trayOn = Def.GetBFor(LayoutBare, "tray", true);
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

            // Per-element colours (empty ref = today's palette default; alloc-free on the
            // palette-name path so resolving once here is cheap). The active-hand accent drives
            // BOTH the selected box border and its side bar; the two "dim" text states (the
            // inactive hand name + the empty state line) were previously hardcoded to the palette.
            Color activeAccent = GlobalOr(Def.GetSFor(LayoutBare, "activeBorderColor", ""), HudPalette.ActiveHandAccent.Value);
            Color titleInactive = GlobalOr(Def.GetSFor(LayoutBare, "titleInactiveColor", ""), HudPalette.TextLabel.Value);
            Color stateEmpty = GlobalOr(Def.GetSFor(LayoutBare, "stateEmptyColor", ""), HudPalette.TextDim.Value);

            for (int i = 0; i < 2; i++)
            {
                bool isRight = i == 1;
                bool active = isRight == s.RightHandActive;

                Slot slot = null;
                try { slot = isRight ? s.Human.RightHandSlot : s.Human.LeftHandSlot; } catch { }
                DynamicThing occ = null;
                try { occ = slot?.Get(); } catch { }

                _box[i].color = FillColor();
                _box[i].BorderColor = active ? activeAccent : BorderColor();
                _box[i].BorderWidth = BorderWidthFor() * (active ? 1.4f : 1f);
                // The active border IS the which-hand cue — specular whitening at a lit
                // corner would wash the accent out, so the active box never catches
                // light. Set once (not ApplyGlass-then-override: a per-frame value
                // flip-flop would defeat the dirty-guard and rebuild the mesh).
                _box[i].Sheen = GlassSheenFor();
                _box[i].Spec = active ? 0f : GlassEdgeFor();
                _box[i].FeatherOverride = FeatherFor();
                ApplyMeshFx(_box[i]); // full 0.9.0 push (trio + ripple + uv0 + material) — sheen/spec above stay custom

                // #4: drop cue — light this box (border, or whole box per F9) while a dragged
                // item is over it and would be accepted. Overrides the active-hand look on purpose.
                if (HudDropCue.Active && ReferenceEquals(HudDropCue.HoveredSlot, slot) && HudDropCue.Accepts(slot))
                {
                    var hi = DropHighlightColor();
                    if (DropWholeBox()) _box[i].color = hi;
                    else { _box[i].BorderColor = hi; _box[i].BorderWidth = BorderWidthFor() * 2f; }
                }

                var ac = activeAccent;
                if (!active) ac.a = 0f;
                _accent[i].color = ac;
                _accent[i].BorderColor = Color.clear;

                HudText.Sync(_title[i]);
                _title[i].fontSize = HudText.Size(labelSize * Def.FontScaleFor(LayoutBare)) * scale;
                _title[i].color = active ? TextColor() : titleInactive;
                HudText.Set(_title[i], isRight ? "RIGHT HAND" : "LEFT HAND");

                Sprite icon = null;
                if (occ != null) { try { icon = occ.GetThumbnail(); } catch { } }
                // Stow flash (vanilla parity) — fires here only when something is stowed into a
                // container held in this hand; the usual worn-container flash lands on the
                // equipment column. Cheap to check either way.
                float flashP;
                Sprite flash = Core.SlotFlash.Get(slot, out flashP);
                if (flash != null) icon = flash;
                _icon[i].sprite = icon;
                _icon[i].enabled = icon != null;
                // #9 item-icon tint: per-element and per-tier since 0.9.2.5 (mode 0 inherits the
                // global "Tint item icons" checkbox, so an untouched profile is unchanged).
                _icon[i].color = TintItemIcon(Color.white);
                _icon[i].rectTransform.localScale = Vector3.one * (flash != null ? 1f + 0.22f * flashP : 1f);

                HudText.Sync(_state[i]);
                _state[i].fontSize = HudText.Size(labelSize * 0.9f * Def.FontScaleFor(LayoutBare)) * scale;
                string state = "";
                if (occ == null) state = "empty";
                else { try { state = StateText.For(occ) ?? ""; } catch { } }
                _state[i].color = occ == null ? stateEmpty : TextColor();
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

            int layoutStart = into.Count;
            into.Add(HudProp.F("Box gap", () => d.GetFFor(EditBare(d), "gap", 22f),
                v => d.SetFFor(EditBare(d), "gap", Mathf.Max(0f, v)), 0f, 160f));

            // Nudge the two text rows within each hand box (both hands move together, stays
            // symmetric). "Hand name" is the LEFT/RIGHT HAND label; "Item state" is the status
            // line under the thumbnail. Offsets are in reference px, so they scale with the HUD.
            into.Add(HudProp.F("Hand-name X", () => d.GetFFor(EditBare(d), "titleDX", 0f), v => d.SetFFor(EditBare(d), "titleDX", v), -200f, 200f));
            into.Add(HudProp.F("Hand-name Y", () => d.GetFFor(EditBare(d), "titleDY", 0f), v => d.SetFFor(EditBare(d), "titleDY", v), -200f, 200f));
            into.Add(HudProp.F("Item-state X", () => d.GetFFor(EditBare(d), "stateDX", 0f), v => d.SetFFor(EditBare(d), "stateDX", v), -200f, 200f));
            into.Add(HudProp.F("Item-state Y", () => d.GetFFor(EditBare(d), "stateDY", 0f), v => d.SetFFor(EditBare(d), "stateDY", v), -200f, 200f));

            // The item thumbnail sat at a hard-coded 52% of box height with no knob at all
            // (play-test ask). Same fraction-of-height + reference-px-nudge shape as ReadoutWidget's
            // icon controls, so the two inspectors read alike.
            into.Add(HudProp.F("Icon scale (× box height)", () => d.GetFFor(EditBare(d), "iconScale", 0.52f),
                v => d.SetFFor(EditBare(d), "iconScale", Mathf.Clamp(v, 0.1f, 1f)), 0.1f, 1f));
            into.Add(HudProp.F("Icon X", () => d.GetFFor(EditBare(d), "iconDX", 0f), v => d.SetFFor(EditBare(d), "iconDX", v), -200f, 200f));
            into.Add(HudProp.F("Icon Y", () => d.GetFFor(EditBare(d), "iconDY", 0f), v => d.SetFFor(EditBare(d), "iconDY", v), -200f, 200f));
            for (int i = layoutStart; i < into.Count; i++) into[i].Group = HudPropGroup.Layout;

            into.Add(HudProp.Bool("Show tray shelf", () => d.GetBFor(EditBare(d), "tray", true),
                v => d.SetBFor(EditBare(d), "tray", v)));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            // Per-element colours. The ACTIVE hand box border + side bar (was the fixed orange
            // HudActiveHand accent); the INACTIVE hand name and the EMPTY state line (were the
            // fixed blue-grey TextLabel/TextDim). The active hand's own name/state follow the base
            // "Text / accent" ref. Empty = the palette default.
            into.Add(HudProp.Color("Active hand border colour", () => d.GetSFor(EditBare(d), "activeBorderColor", ""),
                v => d.SetSFor(EditBare(d), "activeBorderColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.ActiveHandAccent.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Inactive hand-name colour", () => d.GetSFor(EditBare(d), "titleInactiveColor", ""),
                v => d.SetSFor(EditBare(d), "titleInactiveColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.TextLabel.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;
            into.Add(HudProp.Color("Empty state-line colour", () => d.GetSFor(EditBare(d), "stateEmptyColor", ""),
                v => d.SetSFor(EditBare(d), "stateEmptyColor", string.IsNullOrEmpty(v) ? null : v),
                () => HudPalette.TextDim.Value));
            into[into.Count - 1].Group = HudPropGroup.Appearance;

            AddIconTintProps(into);      // item thumbnail wash (inherit / own colour / off)
            AddDropHighlightProps(into); // #4: drag-over drop cue mode + colour
        }
    }
}
