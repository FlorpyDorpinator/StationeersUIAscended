using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>Key bindings + the in-world hint bar toggle. Rebinding of the single-key actions
    /// is done here (and mirrors into the game's own Controls screen — Phase 2). Chorded actions
    /// (Ctrl+digit, Ctrl-tap) are listed for reference.</summary>
    public sealed class ControlsTab : IUiaTab
    {
        public string Title => "Controls";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            UiaControls.Header(col, "On-screen hints");
            UiaControls.ToggleRow(col, "Show the key-hint bar under an open wheel",
                UIAConfig.RadialHintBar.Value, v => UIAConfig.RadialHintBar.Value = v);

            UiaControls.Header(col, "Mouse cursor");
            UiaControls.ToggleRow(col, "Double-tap the mouse modifier to latch the cursor up",
                UIAConfig.CursorLatchEnabled.Value, v =>
                {
                    UIAConfig.CursorLatchEnabled.Value = v;
                    if (!v) CursorLatch.Reset(); // never leave a latched cursor behind when switched off
                });
            UiaControls.Note(col,
                "Normally you HOLD the mouse-modifier key to free the cursor. With this on, tapping it " +
                "twice quickly keeps the cursor up hands-free — press the key once more to go back to " +
                "normal look/aim.");

            UiaControls.Header(col, "Key bindings");
            UiaControls.Note(col,
                "Click a key to rebind it. This is the home for UI Ascended's keys — they are not " +
                "listed in the game's own Controls screen, because most of them only do anything " +
                "while a wheel or overlay is open and the game would report them as conflicts.");
            foreach (var b in UiaKeybinds.All)
                BindRow(col, b);

            UiaControls.Header(col, "Chorded actions (fixed)");
            InfoRow(col, "Swap toolbelt / backpack (wheel open)", "Ctrl tap");
            InfoRow(col, "Open a bound bag", "Ctrl + 1 - 0");
            InfoRow(col, "Bind hovered bag to a number", "1 - 0");
            InfoRow(col, "Fine value adjust while scrolling", UiaKeybinds.Glyph("UIA_FineAdjust"));
        }

        private static void BindRow(Transform parent, UiaBind bind)
        {
            RectTransform right;
            var row = UiaUi.Go("bindrow", parent);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.LabelSize; lbl.color = UiaTheme.Text;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = bind.Label;
            lbl.overflowMode = TextOverflowModes.Ellipsis; lbl.enableWordWrapping = false;
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            right = (RectTransform)row.transform;
            string glyph = UiaKeybinds.Glyph(bind.Get());
            var btn = UiaControls.Button(right, glyph, null, 110f, UiaTheme.RowH);
            btn.OnClick = () => UiaRebindCapture.Begin(bind, () => UiaControlCenter.Refresh());
        }

        private static void InfoRow(Transform parent, string label, string key)
        {
            var row = UiaUi.Go("inforow", parent);
            UiaUi.Size(row, 26f);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);
            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.SmallSize; lbl.color = UiaTheme.TextDim;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = label;
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var chipGo = UiaUi.Go("chip", row.transform);
            UiaUi.Size(chipGo, 22f, 96f);
            var chip = chipGo.AddComponent<Image>(); chip.color = UiaTheme.Panel;
            UiaUi.OutlineOf(chip, UiaTheme.Divider, 1f);
            var kt = UiaUi.Text(chipGo.transform, key, UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)kt.transform);
        }
    }
}
