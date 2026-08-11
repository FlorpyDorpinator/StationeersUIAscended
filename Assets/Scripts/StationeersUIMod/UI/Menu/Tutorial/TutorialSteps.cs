using System.Collections.Generic;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>One page of the first-run coach: a stable id, the shipped heading/body copy, and
    /// the id of the <see cref="TutorialDemoStage"/> scene that illustrates it. Pure data — no
    /// Unity, no config, nothing to reset on hot-reload.</summary>
    internal sealed class TutorialStep
    {
        internal string Id;
        internal string DefaultHeading;
        internal string DefaultBody;
        internal string DemoId;
    }

    /// <summary>
    /// The 15-step curriculum, transcribed from
    /// <c>Documentation/tutorial Plans/First-Run-Tutorial-Plan.md</c> section 6 (facts locked to
    /// <c>Documentation/Interaction-Model-Reference.md</c>). This file is the ONE place the shipped
    /// copy lives; a player/dev override layers on top via <see cref="TutorialTextStore"/>.
    ///
    /// <para>ASCII ONLY. Everything here reaches TMP, whose font renders only Basic Latin — an
    /// arrow, an em-dash or a curly quote comes out as tofu. Write "-&gt;", "1 - 6", "+-", straight
    /// quotes, plain hyphens. (CLAUDE.md, "TMP glyphs = ASCII only in DISPLAYED strings".)</para>
    ///
    /// <para><b>GLYPH TOKENS.</b> Never hard-code a key name in copy — a rebind would make the
    /// tutorial lie. Body/heading text carries tokens that <see cref="TutorialCoach"/> resolves at
    /// DISPLAY time (so a rebind mid-tutorial is picked up on the next step render):</para>
    /// <list type="bullet">
    ///   <item><c>{UIA_Grid}</c> — one of the mod's own binds: <c>UiaKeybinds.Glyph("UIA_Grid")</c>.
    ///     Valid ids are exactly the registry's (<c>Core/UiaKeybinds.cs</c>): UIA_ToolRadial,
    ///     UIA_ToolbeltRadial, UIA_BagRadial, UIA_Grid, UIA_Menu, UIA_HudDesigner, UIA_HandSwap,
    ///     UIA_Page, UIA_FineAdjust.</item>
    ///   <item><c>{V:SmartStow}</c> — a VANILLA button, resolved live through
    ///     <c>KeyManager.GetKey("SmartStow")</c> (KeyManager is GLOBAL namespace). Used names:
    ///     SmartStow, MouseControl, InventorySelect, SwapHands, HelmetSlot..ToolBeltSlot.</item>
    /// </list>
    /// <para>Rendering wraps the resolved glyph in square brackets, e.g. <c>{V:MouseControl}</c>
    /// -&gt; <c>[Alt]</c>, matching the window mock in plan section 4. An unknown token degrades to
    /// its own inner name in brackets rather than throwing.</para>
    ///
    /// <para>Keys that are NOT rebindable are written literally, never as tokens: Shift, Ctrl, Esc,
    /// Enter, and the fixed chords "1 - 6" / "Shift + 1 - 6" / "Ctrl + 1 - 0" (reference section 19,
    /// "Fixed chords/gestures (not rebindable)").</para>
    /// </summary>
    internal static class TutorialSteps
    {
        private static readonly List<TutorialStep> _all = Build();

        internal static IReadOnlyList<TutorialStep> All { get { return _all; } }

        /// <summary>Index of the step with this id, or -1. Used by the replay/deep-link paths.</summary>
        internal static int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == id) return i;
            return -1;
        }

        private static TutorialStep S(string id, string heading, string body, string demo)
        {
            return new TutorialStep { Id = id, DefaultHeading = heading, DefaultBody = body, DemoId = demo };
        }

        private static List<TutorialStep> Build()
        {
            var list = new List<TutorialStep>(15);

            // ---------------- Act I - the basics ----------------

            // 1. The title card. The third sentence has an MP variant (plan section 6, step 1):
            // TutorialCoach swaps it when we are NOT holding the pause, because there is no
            // multiplayer pause (reference section 20) and the player should know to skip.
            list.Add(S("welcome", "Welcome",
                "Welcome to UI Ascended. Your bottom bar is two HANDS - there is no hotbar. " +
                "Wheels and one universal window replace menu-diving. " +
                "The game is paused while you read. " +
                "Click Next - this takes about three minutes.",
                "static.welcome"));

            // 2. The gate skill: everything clickable depends on freeing the cursor.
            list.Add(S("freemouse", "Free your mouse",
                "Your mouse normally aims. Hold {V:MouseControl} to free the cursor - or DOUBLE-TAP " +
                "{V:MouseControl} to KEEP it free, and tap once more to go back to aiming. " +
                "Windows can only be clicked while the cursor is free.",
                "cursorlatch"));

            // 3. The grammar (reference section 1): 180 ms tap/hold split, sticky vs transient.
            list.Add(S("taphold", "Wheels: tap or hold",
                "Every wheel key works two ways. TAP: the wheel opens and stays - point and " +
                "LEFT-CLICK to act, RIGHT-CLICK to go back, Esc to close. " +
                "HOLD the same key: a quick throwaway wheel - sweep to what you want and RELEASE to act.",
                "tapvshold"));

            // ---------------- Act II - the wheels ----------------

            // 4. Belt wheel + The Hub. Final sentence is the plan's trap footnote (reference section 9).
            list.Add(S("beltwheel", "The belt wheel and The Hub",
                "TAP {UIA_ToolbeltRadial}: your toolbelt as a wheel. Click a tool to take it in hand - " +
                "its slot remembers it. The top wedge, THE HUB, leads to everything you carry. " +
                "The wheel's center shows details; its bottom band is Close. " +
                "{UIA_Page} swaps which belt you wear, {UIA_BagRadial} flips to your backpack. " +
                "Note: {UIA_ToolbeltRadial} pressed inside an open wheel just closes it.",
                "beltwheel"));

            // 5. Bag wheel + search. Shares the belt-wheel demo (plan section 7: demo 3 covers 4 and 5).
            list.Add(S("bagwheel", "The bag wheel and search",
                "TAP {UIA_BagRadial}: your bags as a wheel (HOLD {UIA_BagRadial} still shows the " +
                "scoreboard). Open a bag wedge to dive inside. Big bags group by type. " +
                "Pick SEARCH and just type to find anything you carry.",
                "beltwheel"));

            // 6. Equipment digits. Static art (plan section 7: steps 1, 6, 15 are static).
            list.Add(S("equipkeys", "Equipment keys 1 - 6",
                "TAP a number: that worn item's wheel - its settings, its slots, its swaps. " +
                "HOLD the number: take it off into your hand (or put on what you are holding - " +
                "tapping an EMPTY slot does that too). " +
                "Inside any wheel, the numbers jump between wheels.",
                "static.equipkeys"));

            // 7. THE core gesture (reference section 6): outerR+14 px, 0.18 s dwell, both modes.
            list.Add(S("swipe", "Child wheels: the swipe",
                "A wedge with a little chevron holds more. Push THROUGH it, past the wheel's edge, " +
                "and hold still a moment - a child wheel pops out beside it. " +
                "Pull back to dismiss it, RIGHT-CLICK to go back.",
                "swipechild"));

            // 8. The worked example - the mod's signature move. Copy never says "refill": no such verb.
            list.Add(S("batteryswap", "Swap a battery",
                "Tool in hand? TAP {UIA_ToolRadial}. Its battery sits on the wheel with its charge. " +
                "Swipe out through it: TAKE or REPLACE. Click REPLACE - every battery you carry " +
                "appears, charge and warnings shown. Click one. Swapped, one step. " +
                "Canisters and cartridges work exactly the same. Hold Shift to keep the wheel open.",
                "batteryswap"));

            // 9. Values, drag, park. Final sentence is the plan's trap footnote.
            list.Add(S("scrollpark", "Scroll wedges, drag, and parking",
                "Wedges with a value (suit pressure, thrust) adjust with the SCROLL WHEEL - hold " +
                "{UIA_FineAdjust} for fine steps. DRAG any item off its wedge: drop it on another " +
                "wedge, a hand box, or into a machine slot in the world - the game's colored box " +
                "shows what will happen. Drop it on open screen to PARK it while you sort; the " +
                "Close band drops parked items on the ground, Esc just cancels. " +
                "Note: parked items clear harmlessly if you jump to another wheel.",
                "valueandpark"));

            // ---------------- Act III - one window for everything ----------------

            // 10. Opening the Universal Inventory (reference section 11), incl. the look-only trap.
            list.Add(S("grid", "The Universal Inventory",
                "HOLD {UIA_Grid}: a quick peek at everything you carry. TAP {UIA_Grid}: it stays open. " +
                "Every container you wear is a folder - click its tab to open it. " +
                "Remember step 2: free your mouse to click inside.",
                "gridpeeklatch"));

            // 11. The two inverted regimes (reference section 12).
            list.Add(S("gridwork", "Working the window",
                "Mouse captured: the SCROLL WHEEL moves a highlight through every slot - press " +
                "{V:InventorySelect} to take the item, or to place what you are holding into an empty " +
                "slot. Mouse freed: scroll pans, LEFT-CLICK takes to your hand, RIGHT-CLICK opens the " +
                "item's wheel, and you can DRAG anything anywhere - other slots, your hands, the " +
                "numbers, the ground.",
                "gridregimes"));

            // 12. All three pin entry points (reference section 13). Shift + 1 - 6 is a fixed chord.
            list.Add(S("pin", "Pin a bag",
                "Any bag can live on screen. Drag its folder tab OUT of the window - now it is its own " +
                "little window. Or press Shift + 1 - 6 for a worn container - or, with the mouse free, " +
                "just CLICK its box on your HUD. " +
                "Closing the big window leaves pins up; a pin's X tucks it back in.",
                "pinflow"));

            // 13. Smart-Stow (reference section 17). Ctrl + 1 - 0 is a fixed chord.
            list.Add(S("stow", "Smart-Stow",
                "Hands full? Press {V:SmartStow}. The item routes itself - tools to their belt slot, " +
                "stacks top up, batteries to empty sockets, then your bag rules - and the receiving box " +
                "flashes. It works with a wheel open, and it learns: a tool goes back to the slot you " +
                "gave it. Bind a bag to Ctrl + 1 - 0 by hovering its wedge in a wheel and pressing the " +
                "number.",
                "stowflash"));

            // ---------------- Act IV - make it yours ----------------

            // 14. Themes (reference section 21): click-to-apply cards, four shipped themes.
            list.Add(S("profiles", "Pick your look",
                "Open {UIA_Menu} -> PROFILES. Click a card and the whole mod re-skins - HUD, wheels, " +
                "windows, this menu. Four looks ship: Stationeers Blue, Stationeers Blue Minimalist, " +
                "Zirillian Red and Pure HUD. Everything about them is editable later.",
                "themeswitch"));

            // 15. Where to go next. The window adds the Handbook + Replay buttons on this step.
            list.Add(S("finish", "Where to go next",
                "The GUIDE tab is your reference card - replay this tutorial there any time. " +
                "Rebind keys on the CONTROLS tab. Want to redesign the HUD itself? Press " +
                "{UIA_HudDesigner} - and read the Designer Handbook.",
                "static.finish"));

            return list;
        }
    }
}
