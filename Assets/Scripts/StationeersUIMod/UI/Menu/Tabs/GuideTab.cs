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
                "wheels for your tools, belt, bags and equipment. The VISOR HUD half is a clean, " +
                "curved heads-up display you can restyle or design yourself. Turn either on or " +
                "off with the two switches at the top of this window - they never depend on each other.");

            // Guided pass vs reference card: the coach walks it, this tab looks it up.
            var btnRow = UiaUi.Go("guidebtns", col);
            UiaUi.Size(btnRow, 32f);
            UiaUi.HLayout((RectTransform)btnRow.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);
            UiaControls.Button(btnRow.transform, "Replay the tutorial",
                () => Tutorial.TutorialCoach.Open(false), 190f, 28f, UiaControls.ButtonStyle.Panel);
            UiaControls.Button(btnRow.transform, "Designer Handbook",
                () => HandbookViewer.Open(), 190f, 28f, UiaControls.ButtonStyle.Panel);

            UiaControls.Header(col, "The wheels");
            UiaControls.Note(col,
                "TAP a wheel key: the wheel opens and STAYS - point and LEFT-CLICK to act, " +
                "RIGHT-CLICK to back out, Esc (or the key again) closes. HOLD the key instead for " +
                "the quick version: the belt and tool wheels become sweep-and-release, the number " +
                "keys equip, Tab shows the scoreboard. A wedge with a chevron holds more: push OUT " +
                "through it past the rim and hold still a moment - a child wheel opens (Take / " +
                "Replace / settings). Replace is how you swap a battery or canister in one step.");
            KeyRow(col, "Tool / device wheel (item in hand)", UiaKeybinds.Glyph("UIA_ToolRadial"));
            KeyRow(col, "Toolbelt wheel (top wedge = The Hub)", UiaKeybinds.Glyph("UIA_ToolbeltRadial"));
            KeyRow(col, "Bag / backpack wheel", UiaKeybinds.Glyph("UIA_BagRadial"));
            KeyRow(col, "Equipment wheels (tap = wheel, hold = equip)", "1 - 6");
            KeyRow(col, "Select / drag a wedge", "LMB");
            KeyRow(col, "Back / close", "RMB");
            KeyRow(col, "Keep the wheel open after an action", "Shift");
            KeyRow(col, "Reach into the world (grab items)", "Alt");
            KeyRow(col, "Swap active hand", UiaKeybinds.Glyph("UIA_HandSwap"));
            KeyRow(col, "Page a crowded wheel / swap worn belt", UiaKeybinds.Glyph("UIA_Page"));
            KeyRow(col, "Swap toolbelt / backpack (wheel open)", "Tab");
            KeyRow(col, "Open a bound bag", "Ctrl + 1 - 0");
            UiaControls.Note(col,
                "Bind a bag: with a wheel open, hover the bag and press a number key - remembered " +
                "per save. Drag an item off a wheel onto open screen to PARK it while you sort; the " +
                "wheel's bottom Close band drops parked items on the ground, Esc just cancels.");

            UiaControls.Header(col, "The Universal Inventory");
            KeyRow(col, "Open it (tap = stays, hold = peek)", UiaKeybinds.Glyph("UIA_Grid"));
            UiaControls.Note(col,
                "One window for every container you wear or hold. Free the mouse to click inside: " +
                "hold Alt, or DOUBLE-TAP Alt to keep it free. Bags are folder tabs - click one to " +
                "open it, DRAG the tab out to pin the bag as its own window (Shift + 1 - 6, or a " +
                "plain click on a 1 - 6 HUD box in mouse mode, pins a worn container too). While " +
                "the mouse is captured the SCROLL WHEEL moves a highlight and F takes - or places - " +
                "the item; with the mouse free, scroll pans, LEFT-CLICK takes to your hand, " +
                "RIGHT-CLICK opens the item's wheel, and you can drag anything anywhere.");

            UiaControls.Header(col, "Smart storage");
            KeyRow(col, "Smart Stow the held item", "G");
            UiaControls.Note(col,
                "SmartStow+ extends vanilla stow (G): it tops up a matching stack (and keeps going "
                + "until the hand is empty), sends components to their sockets (canisters to tanks, "
                + "batteries to battery slots), routes to the bag whose profile matches, then remembers "
                + "where that type went. Loose walls and kits get one steady general-storage bag. "
                + "It works with a wheel open, too. Set up bag profiles under the Storage tab.");

            UiaControls.Header(col, "The visor HUD");
            UiaControls.Note(col,
                "Pick a ready-made look on the Profiles tab, or open the HUD Designer (F9) to build " +
                "your own - every element can be moved, resized, recoloured and restyled. Profiles are " +
                "plain files you can share with a friend. The Designer Handbook (button above) is the " +
                "full deep-dive.");
            KeyRow(col, "Open the HUD Designer", UiaKeybinds.Glyph("UIA_HudDesigner"));
            KeyRow(col, "Open this menu", UiaKeybinds.Glyph("UIA_Menu"));

            UiaControls.Note(col,
                "Rebind any of these under the Controls tab. (Only the menu key also appears in the " +
                "game's own Controls screen, under \"UI Ascended\" - the rest live here to avoid " +
                "false conflict warnings.)");
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
