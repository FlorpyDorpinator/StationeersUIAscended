using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>The in-menu how-to: what the two halves are, how the radial gestures work, how
    /// smart storage works, and a live key reference (reads current binds from UiaKeybinds so it
    /// always matches the player's rebinds). Also shown once on first run.</summary>
    public sealed class GuideTab : IUiaTab
    {
        public string Title => "Guide";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            UiaControls.Header(col, "Two halves - use either, or both");
            UiaControls.Note(col,
                "UI Ascended is two independent parts. The RADIAL half replaces menu-diving with " +
                "hold-a-key wheels for your tools, belt, bags and equipment. The VISOR HUD half is a " +
                "clean, curved heads-up display you can restyle or design yourself. Turn either on or " +
                "off with the two switches at the top of this window - they never depend on each other.");

            UiaControls.Header(col, "The radial menus");
            UiaControls.Note(col,
                "Tap a key to open a wheel; use your cursor from there. Move over a wedge and " +
                "click act. Holding the same key instead does the quick vanilla action, like putting the item in your hand.");
            KeyRow(col, "Open the tool / device wheel (hold)", UiaKeybinds.Glyph("UIA_ToolRadial"));
            KeyRow(col, "Open the toolbelt wheel (hold)", UiaKeybinds.Glyph("UIA_ToolbeltRadial"));
            KeyRow(col, "Open the bag / backpack wheel", UiaKeybinds.Glyph("UIA_BagRadial"));
            KeyRow(col, "Equipment wheels (tap opens, hold equips)", "1 - 6");
            KeyRow(col, "Select / drag a wedge", "LMB");
            KeyRow(col, "Back / close", "RMB");
            KeyRow(col, "Reach into the world (grab items)", "Alt");
            KeyRow(col, "Swap active hand", UiaKeybinds.Glyph("UIA_HandSwap"));
            KeyRow(col, "Page a crowded wheel", UiaKeybinds.Glyph("UIA_Page"));
            KeyRow(col, "Swap toolbelt / backpack (wheel open)", "Tab");
            KeyRow(col, "Open a bound bag", "Ctrl + 1 - 0");
            UiaControls.Note(col,
                "Bind a bag: with a wheel open, hover a bag and press a number key. It's remembered " +
                "per save. Drag an item off a wheel onto the screen to park it, then drop it anywhere.");

            UiaControls.Header(col, "Smart storage");
            KeyRow(col, "Smart Stow the held item", "G");
            UiaControls.Note(col,
                "SmartStow+ extends vanilla stow (G): it tops up a matching stack (and keeps going "
                + "until the hand is empty), sends components to their sockets (canisters to tanks, "
                + "batteries to battery slots), routes to the bag whose profile matches, then remembers "
                + "where that type went. Loose walls and kits get one steady general-storage bag. "
                + "Set up bag profiles under the Storage tab.");

            UiaControls.Header(col, "The visor HUD");
            UiaControls.Note(col,
                "Pick a ready-made look on the Profiles tab, or open the HUD Designer (F9) to build " +
                "your own - every element can be moved, resized, recoloured and restyled. Profiles are " +
                "plain files you can share with a friend.");
            KeyRow(col, "Open the HUD Designer", UiaKeybinds.Glyph("UIA_HudDesigner"));
            KeyRow(col, "Open this menu", UiaKeybinds.Glyph("UIA_Menu"));

            UiaControls.Note(col,
                "Rebind any of these under the Controls tab, or in the game's own Controls screen " +
                "(single-key actions appear there under \"UI Ascended\").");
        }

        private static void KeyRow(Transform parent, string label, string key)
        {
            var row = UiaUi.Go("keyrow", parent);
            UiaUi.Size(row, 26f);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);

            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.SmallSize; lbl.color = UiaTheme.TextDim;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = label;
            lbl.overflowMode = TextOverflowModes.Ellipsis; lbl.enableWordWrapping = false;
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var chipGo = UiaUi.Go("chip", row.transform);
            UiaUi.Size(chipGo, 22f, 96f);
            var chip = chipGo.AddComponent<Image>(); chip.color = UiaTheme.PanelRaised;
            UiaUi.OutlineOf(chip, UiaTheme.AccentDim, 1f);
            var kt = UiaUi.Text(chipGo.transform, key, UiaTheme.SmallSize, UiaTheme.Accent, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)kt.transform);
        }
    }
}
