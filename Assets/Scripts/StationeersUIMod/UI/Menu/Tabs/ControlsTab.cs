using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>Key bindings + the in-world hint bar toggle. Rebinding of the single-key actions
    /// is done here (and mirrors into the game's own Controls screen — Phase 2). Chorded actions
    /// (Ctrl+digit, Ctrl-tap) are listed for reference. Kit v2 conversion: each group is a
    /// Section; every control row registers with the settings search.</summary>
    public sealed class ControlsTab : IUiaTab
    {
        public string Title => "Controls";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            BuildHints(col);
            BuildCursor(col);
            BuildKeyBindings(col);
            BuildChorded(col);
        }

        // ---------- On-screen hints ----------

        private void BuildHints(Transform col)
        {
            var sec = UiaComposite.Section(col, "controls.hints", "On-screen hints");
            var hintBar = UiaControls.ToggleRow(sec.Body, "Show the key-hint bar under an open wheel",
                UIAConfig.RadialHintBar.Value, v => UIAConfig.RadialHintBar.Value = v);
            UiaSearch.RegisterRow(Title, "controls.hints", "Show the key-hint bar",
                UiaSearch.MakeJump(hintBar.transform.parent.gameObject));
        }

        // ---------- Mouse cursor ----------

        private void BuildCursor(Transform col)
        {
            var sec = UiaComposite.Section(col, "controls.cursor", "Mouse cursor");
            var latch = UiaControls.ToggleRow(sec.Body, "Double-tap the mouse modifier to latch the cursor up",
                UIAConfig.CursorLatchEnabled.Value, v =>
                {
                    UIAConfig.CursorLatchEnabled.Value = v;
                    if (!v) CursorLatch.Reset(); // never leave a latched cursor behind when switched off
                });
            UiaControls.Note(sec.Body,
                "Normally you HOLD the mouse-modifier key to free the cursor. With this on, tapping it " +
                "twice quickly keeps the cursor up hands-free - press the key once more to go back to " +
                "normal look/aim.");
            UiaSearch.RegisterRow(Title, "controls.cursor", "Double-tap to latch the cursor",
                UiaSearch.MakeJump(latch.transform.parent.gameObject));
        }

        // ---------- Key bindings ----------

        private void BuildKeyBindings(Transform col)
        {
            var sec = UiaComposite.Section(col, "controls.binds", "Key bindings");
            UiaControls.Note(sec.Body,
                "Click a key to rebind it. This is the home for UI Ascended's keys - they are not " +
                "listed in the game's own Controls screen, because most of them only do anything " +
                "while a wheel or overlay is open and the game would report them as conflicts.");
            foreach (var b in UiaKeybinds.All)
                BindRow(sec.Body, b);
        }

        // ---------- Chorded actions (fixed) ----------

        private void BuildChorded(Transform col)
        {
            var sec = UiaComposite.Section(col, "controls.chorded", "Chorded actions (fixed)");
            InfoRow(sec.Body, "Swap toolbelt / backpack (wheel open)", "Tab");
            InfoRow(sec.Body, "Open a bound bag", "Ctrl + 1 - 0");
            InfoRow(sec.Body, "Bind hovered bag to a number", "1 - 0");
            InfoRow(sec.Body, "Fine value adjust while scrolling", UiaKeybinds.Glyph("UIA_FineAdjust"));
        }

        private void BindRow(Transform parent, UiaBind bind)
        {
            var row = UiaUi.Go("bindrow", parent);
            // Hand-built row: give it its own floor (see GuideTab.KeyRow's comment) rather than
            // rely solely on whatever minimum Kit v2 gives UiaUi.Size centrally.
            UiaUi.Size(row, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.LabelSize; lbl.color = UiaTheme.Text;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = bind.Label;
            UiaControls.FitText(lbl, 9f);   // no ellipsis: shrink, then 2 lines in the RowH row
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            string glyph = UiaKeybinds.Glyph(bind.Get());
            var btn = UiaControls.Button((RectTransform)row.transform, glyph, null, 110f, UiaTheme.RowH);
            btn.OnClick = () => UiaRebindCapture.Begin(bind, () => UiaControlCenter.Refresh());

            UiaSearch.RegisterRow(Title, "controls.binds", bind.Label, UiaSearch.MakeJump(row));
        }

        private static GameObject InfoRow(Transform parent, string label, string key)
        {
            var row = UiaUi.Go("inforow", parent);
            UiaUi.Size(row, 26f).minHeight = 26f;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);
            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.SmallSize; lbl.color = UiaTheme.TextDim;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = label;
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var chipGo = UiaUi.Go("chip", row.transform);
            UiaUi.Size(chipGo, 22f, 96f).minHeight = 22f;
            var chip = chipGo.AddComponent<Image>(); chip.color = UiaTheme.Panel;
            UiaUi.OutlineOf(chip, UiaTheme.Divider, 1f);
            var kt = UiaUi.Text(chipGo.transform, key, UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)kt.transform);
            return row;
        }
    }
}
