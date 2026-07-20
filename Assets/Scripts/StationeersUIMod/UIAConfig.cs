using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod
{
    /// <summary>
    /// Which interaction model the radials use. Visuals are separate config entries
    /// (tuned in the radial editor); the schema switches BEHAVIOR:
    /// OptionA — the 2026-07 overhaul: STOW wedges, device-control satellites with
    ///   scroll-adjustable values, take/open satellites on nested bags (no giant swap
    ///   lists), auto-close after actions, search-all-bags panel, drag-out parking.
    /// OptionB — everything OptionA does, plus "The Hub": the toolbelt radial grows a
    ///   top-center wedge into the Tab inventory root. TAP middle mouse = sticky toolbelt
    ///   (tap MMB again on a wedge to select), HOLD = transient (LMB dives into branches,
    ///   release runs the hovered action and closes).
    /// OptionD — the classic pre-overhaul behavior, kept for A/B testing.
    /// </summary>
    public enum ControlSchema
    {
        OptionA,
        OptionB,
        OptionD,
    }

    /// <summary>How a bag radial presents its free space (Option A playtest options).</summary>
    public enum EmptySlotMode
    {
        /// <summary>Every empty slot is its own STOW wedge; no aggregate wedge.</summary>
        EmptySlots,
        /// <summary>One aggregate STOW wedge plus every empty slot.</summary>
        StowAndEmptySlots,
        /// <summary>Just the aggregate STOW wedge and the items.</summary>
        StowOnly,
    }

    /// <summary>
    /// All user-facing configuration. Bound once at plugin init; StationeersLaunchPad
    /// auto-renders the resulting .cfg in its in-game mod config panel.
    /// </summary>
    public static class UIAConfig
    {
        // --- General ---
        public static ConfigEntry<bool> MasterEnable;
        /// <summary>Master switch for the RADIAL half (see <see cref="Bind"/>). Paired with the
        /// HUD half's master, <see cref="UI.Hud.HudConfig.VisorHudEnabled"/> — the two halves are
        /// independent so a player can run either alone.</summary>
        public static ConfigEntry<bool> RadialEnabled;
        public static ConfigEntry<KeyCode> SettingsWindowKey;
        public static ConfigEntry<int> HoldThresholdMs;
        public static ConfigEntry<ControlSchema> Schema;
        public static ConfigEntry<KeyCode> RadialHandSwapKey;
        public static ConfigEntry<KeyCode> RadialPageKey;
        public static ConfigEntry<KeyCode> RadialFineAdjustKey;
        public static ConfigEntry<bool> RadialMovementEnabled;
        public static ConfigEntry<bool> RadialHintBar;
        /// <summary>Set true once the first-run guide has been shown (so it only auto-opens once).</summary>
        public static ConfigEntry<bool> GuideShown;
        public static ConfigEntry<bool> CursorLatchEnabled;
        public static ConfigEntry<int> CursorLatchMs;

        // --- Radial feel (interaction deltas: flick, double-tap, sounds, hints, head-look) ---
        /// <summary>R2: enable flick-commit — a fast directional flick past the selection radius
        /// commits the sector under the cursor without ever drawing the ring.</summary>
        public static ConfigEntry<bool> RadialFlickCommit;
        /// <summary>R2: the flick window in ms — a release faster than this (with enough travel)
        /// counts as a flick.</summary>
        public static ConfigEntry<int> RadialFlickMs;
        /// <summary>R3: enable double-tap-repeat — a second tap of the same radial key within the
        /// window re-runs the last committed action for that feature.</summary>
        public static ConfigEntry<bool> RadialDoubleTapRepeat;
        /// <summary>R3: the double-tap window in ms.</summary>
        public static ConfigEntry<int> RadialDoubleTapMs;
        /// <summary>R8: play a soft hover tick as the highlighted wedge changes and a click on
        /// commit.</summary>
        public static ConfigEntry<bool> RadialWedgeSounds;
        /// <summary>R10: fade a contextual hint out once it has been used enough times.</summary>
        public static ConfigEntry<bool> RadialHintFade;
        /// <summary>Head-look: while a radial opened by ANOTHER key is up, holding the head-look
        /// key lets the camera look around (the radial modal stops unlocking the cursor so the
        /// game re-locks it and head-look resumes).</summary>
        public static ConfigEntry<bool> RadialHeadLookHold;
        /// <summary>Head-look key (default MMB) — hold while a radial is open to peek around.</summary>
        public static ConfigEntry<KeyCode> RadialHeadLookKey;

        /// <summary>Shorthand for "the Option A behavior SET is active" — B is A plus the
        /// Hub gestures, so every A-gated behavior (STOW wedges, satellites, search,
        /// parking, scroll values, auto-close) applies to both. Only interaction deltas
        /// check IsB on top.</summary>
        public static bool IsA => Schema == null || Schema.Value != ControlSchema.OptionD;

        /// <summary>Option B's interaction deltas: The Hub wedge, MMB tap-opens-sticky,
        /// MMB-tap-selects in sticky radials, LMB branch-diving in hold mode.</summary>
        public static bool IsB => Schema != null && Schema.Value == ControlSchema.OptionB;

        // --- Toolbelt radial ---
        public static ConfigEntry<bool> ToolbeltRadialEnabled;
        public static ConfigEntry<KeyCode> ToolbeltRadialKey;
        public static ConfigEntry<bool> ToolbeltShowStowEntries;
        /// <summary>1B.2: remember which slot each tool type lives in on a belt (home slots) so a
        /// tool always returns to the same wedge position.</summary>
        public static ConfigEntry<bool> ToolbeltHomeSlots;
        /// <summary>1B.3: reserve a wedge for every belt slot (occupied or empty) so the ring
        /// geometry is stable, and show a grey binding label on empty-but-bound slots.</summary>
        public static ConfigEntry<bool> ToolbeltStableGeometry;

        // --- Tool radial ---
        public static ConfigEntry<bool> ToolRadialEnabled;
        public static ConfigEntry<KeyCode> ToolRadialKey;
        public static ConfigEntry<bool> ToolRadialTakeOverVanillaKey;

        // --- Bag radial ---
        public static ConfigEntry<bool> BagRadialEnabled;
        public static ConfigEntry<KeyCode> BagRadialKey;
        public static ConfigEntry<bool> BagRadialTapOpens;
        public static ConfigEntry<int> BagRadialGroupThreshold;
        public static ConfigEntry<bool> BagGrouping;
        public static ConfigEntry<EmptySlotMode> BagEmptySlots;
        public static ConfigEntry<int> RadialMaxWedges;

        // --- Equipment key radials (1-6) ---
        public static ConfigEntry<bool> EquipmentKeyRadialsEnabled;

        // --- Slot finder ---
        public static ConfigEntry<int> ScanDepth;
        public static ConfigEntry<bool> AllowToolSlotSources;

        // --- SmartStow+ ---
        public static ConfigEntry<bool> SmartStowPlusEnabled;
        public static ConfigEntry<bool> StowPreferStacks;
        public static ConfigEntry<bool> StowUseProfiles;
        public static ConfigEntry<bool> StowUseTypeMemory;
        public static ConfigEntry<bool> StowIntoNestedBags;
        public static ConfigEntry<bool> StowToolsToToolbeltFirst;

        // --- The Grid ---
        /// <summary>Master switch for The Grid — the full-inventory glass overlay (every worn slot
        /// + every nested container). Off = the key does nothing and the panel never opens.</summary>
        public static ConfigEntry<bool> GridEnabled;
        /// <summary>Key that toggles / peeks The Grid. Default B. NOTE: KeyCode.B is vanilla's
        /// KeyMap.InstantStop (rover/vehicle instant-stop) default — a soft collision; the mod's
        /// hold/tap handler only fires while a human player is the active pilot, and B is
        /// rebindable from the game's Controls screen (UIA group).</summary>
        public static ConfigEntry<KeyCode> GridKey;
        /// <summary>Hold-to-peek: holding the key shows The Grid only while held (momentary peek),
        /// a quick tap toggles it open/closed. Off = tap-only toggle.</summary>
        public static ConfigEntry<bool> GridHoldPeek;
        /// <summary>How The Grid lays out the inventory. Grid = the flat, Diablo-style pack (each
        /// storage container is a bordered region of cells, tools stay single cells); Nested = the
        /// classic indented tree (tools expand into sub-sections). Flippable from the panel's title
        /// bar toggle and the F10 menu; persists once chosen.</summary>
        public static ConfigEntry<UI.Grid.GridDisplayMode> GridMode;
        /// <summary>Remembered window geometry (top-left px from the screen's top-left) + size.
        /// X/Y default to -1 = "centre on first open, then store". W/H are clamped on apply so
        /// the window always fits on-screen. Persisted by <see cref="UI.Grid.TheGridPanel"/> on
        /// the end of a move / resize drag (not per-frame).</summary>
        public static ConfigEntry<float> GridWinX;
        public static ConfigEntry<float> GridWinY;
        public static ConfigEntry<float> GridWinW;
        public static ConfigEntry<float> GridWinH;
        /// <summary>Edge length in px of one inventory cell (icon scales with it). Live from the
        /// F10 menu; a change relayouts the open window.</summary>
        public static ConfigEntry<float> GridCellSize;

        // --- HUD ---
        public static ConfigEntry<bool> HudEnabled;
        public static ConfigEntry<bool> HudHandBoxes;
        public static ConfigEntry<bool> HudStatusStrip;
        public static ConfigEntry<bool> HudVitals;
        public static ConfigEntry<bool> HudClock;
        public static ConfigEntry<bool> HudContextPanel;
        public static ConfigEntry<bool> HudVisorArcs;
        public static ConfigEntry<float> HudScale;
        public static ConfigEntry<bool> HideVanillaHands;
        public static ConfigEntry<bool> HideVanillaClothing;
        public static ConfigEntry<bool> HideVanillaStatus;
        public static ConfigEntry<bool> HideVanillaPlayerState;
        public static ConfigEntry<bool> HardcoreGating;

        // --- Radial visuals ---
        public static ConfigEntry<float> RadialOuterRadius;
        public static ConfigEntry<float> RadialInnerRadius;
        public static ConfigEntry<bool> IconFlipV;
        public static ConfigEntry<bool> UseUnityRadial;
        public static ConfigEntry<float> RadialIconScale;
        public static ConfigEntry<float> RadialIconRatio;
        public static ConfigEntry<bool> RadialShowWedgeLabels;
        public static ConfigEntry<float> RadialEdgeFeather;
        public static ConfigEntry<float> RadialBorderWidth;
        public static ConfigEntry<bool> RadialSideBorders;
        public static ConfigEntry<float> RadialSideWidthInner;
        public static ConfigEntry<float> RadialSideWidthOuter;
        public static ConfigEntry<float> RadialWedgeGapDeg;
        public static ConfigEntry<bool> RadialDimShading;
        public static ConfigEntry<float> RadialDimStrength;
        public static ConfigEntry<string> RadialFontName;
        public static ConfigEntry<bool> RadialUppercaseLabels;
        public static ConfigEntry<bool> RadialShowStateText;
        public static ConfigEntry<bool> RadialBindingCurved;

        // --- Hint bar (the key-hint strip under an open wheel) ---
        // Everything the strip's PanelGraphic can actually consume, so its look is authored in the
        // F10 radial editor instead of being hand-tuned in code. Colours live in RadialPalette.
        public static ConfigEntry<float> HintBarCorner;
        public static ConfigEntry<float> HintBarBorderWidth;
        public static ConfigEntry<float> HintBarFeather;
        public static ConfigEntry<float> HintBarSheen;
        public static ConfigEntry<float> HintBarSpec;
        public static ConfigEntry<float> HintBarGlow;
        public static ConfigEntry<float> HintBarGlowWidth;
        public static ConfigEntry<float> HintBarFontSize;
        public static ConfigEntry<bool> HintBarBold;
        public static ConfigEntry<float> HintBarHeight;
        public static ConfigEntry<float> HintBarPadding;
        public static ConfigEntry<float> HintBarDrop;
        public static ConfigEntry<bool> HintBarFrost;
        public static ConfigEntry<float> HintBarFrostStrength;
        /// <summary>Editor-only: pin the strip on screen over a white swatch so its transparency and
        /// colours can be judged without opening a wheel. Never persisted as "on" by intent — the
        /// F10 menu clears it on close.</summary>
        public static ConfigEntry<bool> HintBarPreview;
        public static ConfigEntry<float> RadialSatelliteScale;
        public static ConfigEntry<float> RadialShineIntensity;
        public static ConfigEntry<float> ParkedChipRadius;
        public static ConfigEntry<float> RadialHubTitleSize;   // line 1: item/menu title (bold)
        public static ConfigEntry<float> RadialTextVerb;       // line 2: the action verb
        public static ConfigEntry<float> RadialTextLabel;      // line 3: the item name
        public static ConfigEntry<float> RadialTextSub;        // line 4: the detail / location
        public static ConfigEntry<float> RadialTextWarn;       // line 5: the stat / warning
        public static ConfigEntry<bool> RadialRotateLongLabels;
        public static ConfigEntry<float> RadialSatelliteHubRatio;
        public static ConfigEntry<bool> RadialDynamicReadoutText;

        // --- Radial effects (0.9.0): frosted-glass backdrop + glass mesh look on wedges ---
        public static ConfigEntry<bool> RadialFrost;
        public static ConfigEntry<float> RadialFrostStrength;
        public static ConfigEntry<float> RadialSheen;
        public static ConfigEntry<float> RadialEdgeLight;

        public static void Bind(ConfigFile cfg)
        {
            MasterEnable = cfg.Bind("1. General", "MasterEnable", true,
                "Master switch. When off, the mod draws and patches nothing.");
            RadialEnabled = cfg.Bind("1. General", "RadialEnabled", true,
                "Master switch for the RADIAL half of the mod (toolbelt / tool / bag / equipment " +
                "radials, the SmartStow+ wheel and drag-out parking). Off = the radial menus never " +
                "open and their keys fall back to the vanilla actions; the HUD half is unaffected. " +
                "Pair with the Visor HUD master ('10. Visor HUD > VisorHudEnabled') to run either " +
                "half of the mod on its own.");
            SettingsWindowKey = cfg.Bind("1. General", "SettingsWindowKey", KeyCode.F10,
                "Key that toggles the in-game UI Ascended settings window.");
            HoldThresholdMs = cfg.Bind("1. General", "HoldThresholdMs", 180,
                new ConfigDescription("How long a radial key must be held before the radial opens (ms). Shorter taps fall through to the vanilla action.",
                    new AcceptableValueRange<int>(60, 600)));
            Schema = cfg.Bind("1. General", "ControlSchema", ControlSchema.OptionA,
                "Radial interaction model. OptionA: STOW wedges, device-control satellites with scroll " +
                "values, take/open on nested bags, auto-close after actions, search panel, drag-out " +
                "parking. OptionB: OptionA plus The Hub - a top-center wedge on the toolbelt radial " +
                "into the Tab inventory root; TAP middle mouse opens the toolbelt sticky (tap MMB " +
                "again to select), HOLD stays transient (LMB enters branches, release runs the " +
                "hovered action). OptionD: the classic behavior, kept for A/B comparison.");
            RadialHandSwapKey = cfg.Bind("1. General", "RadialHandSwapKey", KeyCode.E,
                "While a radial is open: swap the active hand (radials aim stows and equips at the " +
                "active hand). Matches vanilla E-to-swap; drives the same vanilla swap underneath.");
            RadialPageKey = cfg.Bind("1. General", "RadialPageKey", KeyCode.Q,
                "While a radial is open: flip to the next page of a crowded ring (a 1/2 counter " +
                "shows above the ring). Vanilla Q-throw never fires while a radial is open.");
            RadialFineAdjustKey = cfg.Bind("1. General", "RadialFineAdjustKey", KeyCode.C,
                "Hold while scrolling a value wedge (suit pressure/temperature) for fine ±1 steps " +
                "instead of the coarse ±10.");
            RadialMovementEnabled = cfg.Bind("1. General", "MoveWhileRadialOpen", true,
                "Keep walking with WASD (and jumping with Space) while a radial menu is open. " +
                "Camera look stays on the cursor; typing in the search panel never moves you; " +
                "disabled while seated (vehicle controls share the same gate). " +
                "Off = the classic stop-and-pick behavior.");
            RadialHintBar = cfg.Bind("1. General", "RadialHintBar", true,
                "Show a slim, contextual key-hint bar just below an open radial (LMB select, RMB " +
                "back, Alt reach, swap hand, page…). Follows your rebinds. Turn off for a cleaner wheel.");
            GuideShown = cfg.Bind("1. General", "GuideShown", false,
                "Internal: set once the first-run how-to guide has been shown. Reset to false to see it again.");
            CursorLatchEnabled = cfg.Bind("1. General", "CursorLatchOnDoubleTap", true,
                "Double-tap the mouse-modifier key (the one you normally HOLD to free the cursor) to " +
                "LATCH the cursor up, so you can click around hands-free. Press the same key once more " +
                "to drop back to normal look/aim. Off = vanilla hold-only behaviour.");
            CursorLatchMs = cfg.Bind("1. General", "CursorLatchWindowMs", 250,
                new ConfigDescription("How close together the two taps must be to latch the cursor (ms).",
                    new AcceptableValueRange<int>(120, 500)));

            RadialFlickCommit = cfg.Bind("1b. Radial Feel", "FlickCommit", false,
                "Flick-commit: a fast directional flick of the radial key (released before the ring " +
                "would even draw, but past the selection radius) commits the wedge under the cursor " +
                "straight away. Off = always open the ring first.");
            RadialFlickMs = cfg.Bind("1b. Radial Feel", "FlickWindowMs", 180,
                new ConfigDescription("How quick a release counts as a flick (ms). A release slower than " +
                    "this opens the ring normally.",
                    new AcceptableValueRange<int>(80, 400)));
            RadialDoubleTapRepeat = cfg.Bind("1b. Radial Feel", "DoubleTapRepeat", false,
                "Double-tap a radial key to repeat its last committed action without reopening the ring " +
                "(e.g. re-equip the last tool). A single tap keeps today's open/close behaviour.");
            RadialDoubleTapMs = cfg.Bind("1b. Radial Feel", "DoubleTapWindowMs", 250,
                new ConfigDescription("How close together the two taps must be to count as a double-tap (ms).",
                    new AcceptableValueRange<int>(120, 500)));
            RadialWedgeSounds = cfg.Bind("1b. Radial Feel", "WedgeSounds", true,
                "Play a soft tick as the highlighted wedge changes and a click on commit " +
                "(uses the game's own UI sounds).");
            RadialHintFade = cfg.Bind("1b. Radial Feel", "HintFade", true,
                "Fade a contextual key-hint out once you've used that action enough times, so the hints " +
                "teach then get out of the way. Reset the counters from the F10 menu.");
            RadialHeadLookHold = cfg.Bind("1b. Radial Feel", "HeadLookHold", true,
                "While a radial opened by ANOTHER key is up, hold the head-look key to look around " +
                "(camera re-locks to the mouse for as long as it's held). A quick tap still closes " +
                "the radial. When no radial is open the key does its normal job.");
            RadialHeadLookKey = cfg.Bind("1b. Radial Feel", "HeadLookKey", KeyCode.Mouse2,
                "Key to hold for head-look while a radial is open (default: middle mouse). Rebindable.");

            ToolbeltRadialEnabled = cfg.Bind("2. Toolbelt Radial", "Enabled", true,
                "Hold a key to open a radial of everything on your toolbelt; release over a tool to equip it into the active hand.");
            ToolbeltRadialKey = cfg.Bind("2. Toolbelt Radial", "Key", KeyCode.Mouse2,
                "Radial key (default: middle mouse; the vanilla PingHighlight binding on Mouse2 is currently unused by the game).");
            ToolbeltShowStowEntries = cfg.Bind("2. Toolbelt Radial", "ShowStowEntries", true,
                "Always show empty belt slots in the radial, so you can put the held tool back (disabled entries when nothing fits).");
            ToolbeltHomeSlots = cfg.Bind("2. Toolbelt Radial", "HomeSlots", true,
                "Remember which slot each tool type lives in on a belt, so a tool always returns to the " +
                "same wedge position (home slots). Bindings are per-save.");
            ToolbeltStableGeometry = cfg.Bind("2. Toolbelt Radial", "StableGeometry", true,
                "Reserve a wedge for every belt slot (occupied or empty) so the ring layout never " +
                "shifts, and show a dim grey label on an empty slot that has a tool type bound to it.");

            ToolRadialEnabled = cfg.Bind("3. Tool Radial", "Enabled", true,
                "Hold a key while holding a tool to open its Controls/Slots radial.");
            ToolRadialKey = cfg.Bind("3. Tool Radial", "Key", KeyCode.R,
                "Radial key. Default R: tap keeps the vanilla open-hand-slot-window action, hold opens the radial.");
            ToolRadialTakeOverVanillaKey = cfg.Bind("3. Tool Radial", "TakeOverVanillaKey", true,
                "When the radial key is R, suppress the vanilla handler and re-dispatch taps ourselves so hold can open the radial. Disable if you rebound the radial to a free key.");

            BagRadialEnabled = cfg.Bind("4. Bag Radial", "Enabled", true,
                "Hold a key to navigate backpack/bags/items as nested radials.");
            BagRadialKey = cfg.Bind("4. Bag Radial", "Key", KeyCode.Tab,
                "Radial key (default Tab).");
            BagRadialTapOpens = cfg.Bind("4. Bag Radial", "TapOpensRadial", true,
                "On: TAP opens the bag radial (sticky - click to navigate) and HOLD shows the vanilla scoreboard. " +
                "Off: hold opens the radial and tap shows the scoreboard.");
            BagRadialGroupThreshold = cfg.Bind("4. Bag Radial", "GroupThreshold", 10,
                new ConfigDescription("When a bag holds more than this many items, group them by sorting category first.",
                    new AcceptableValueRange<int>(4, 24)));
            BagGrouping = cfg.Bind("4. Bag Radial", "GroupBySortingClass", true,
                "Option A: group crowded bags into UIA sorting-class wedges (Storage, Power Cells, ...). " +
                "Off = always show the raw items and slots, never category wedges.");
            BagEmptySlots = cfg.Bind("4. Bag Radial", "EmptySlotDisplay", EmptySlotMode.StowAndEmptySlots,
                "How bag radials present free space. EmptySlots: every empty slot is its own STOW wedge. " +
                "StowAndEmptySlots: one aggregate STOW wedge plus the empty slots. StowOnly: just the " +
                "aggregate STOW wedge and the items.");
            RadialMaxWedges = cfg.Bind("4. Bag Radial", "MaxWedges", 14,
                new ConfigDescription("Maximum wedges a radial shows at once; overflow goes into a MORE " +
                    "wedge that opens the rest. (Biggest vanilla bag is 28 slots.)",
                    new AcceptableValueRange<int>(6, 32)));

            EquipmentKeyRadialsEnabled = cfg.Bind("4b. Equipment Keys", "Enabled", true,
                "Tap 1-6 to open a management radial for that equipment piece (on/off, slots, swaps); " +
                "hold 1-6 to equip/unequip it to the active hand. Replaces the vanilla slot-window toggle on those keys.");

            ScanDepth = cfg.Bind("5. Slot Finder", "ScanDepth", 3,
                new ConfigDescription("How many container levels deep to search for compatible items (1 = only worn slots).",
                    new AcceptableValueRange<int>(1, 5)));
            AllowToolSlotSources = cfg.Bind("5. Slot Finder", "AllowToolSlotSources", true,
                "Offer batteries/cartridges that are currently inside other tools (with a consequence warning).");

            SmartStowPlusEnabled = cfg.Bind("6. SmartStow+", "Enabled", true,
                "Extend vanilla Smart Stow (G) with stack-merge priority, bag profiles and item-type memory. Falls through to vanilla when no rule matches.");
            StowPreferStacks = cfg.Bind("6. SmartStow+", "PreferExistingStacks", true,
                "First choice: merge the held stackable into a matching partial stack anywhere accessible.");
            StowUseProfiles = cfg.Bind("6. SmartStow+", "UseBagProfiles", true,
                "Second choice: stow into the bag whose assigned profile matches the item.");
            StowUseTypeMemory = cfg.Bind("6. SmartStow+", "UseTypeMemory", true,
                "Third choice: stow where the same item type was last stowed this save.");
            StowIntoNestedBags = cfg.Bind("6. SmartStow+", "StowIntoNestedBags", true,
                "Allow SmartStow+ to target bags nested inside other bags.");
            StowToolsToToolbeltFirst = cfg.Bind("6. SmartStow+", "ToolsToToolbeltFirst", true,
                "Highest priority for tools: fill an empty slot on your directly-worn tool belt (equip slot 6) first, "
                + "then the worn jetpack/backpack itself, BEFORE nesting into a belt tucked inside another bag.");

            GridEnabled = cfg.Bind("9. The Grid", "Enabled", true,
                "The Grid: a themed glass overlay showing EVERY inventory slot you carry — helmet, " +
                "glasses, suit, back/backpack, uniform, toolbelt, both hands, and every nested " +
                "container inside them. Left-click a cell to take/equip it to the active hand; sort " +
                "per container. Off = the key does nothing and the panel never opens.");
            GridKey = cfg.Bind("9. The Grid", "Key", KeyCode.B,
                "Key that opens The Grid. Default B. Note: B is vanilla's rover instant-stop " +
                "binding — rebind from the game's Controls screen (UI Ascended group) if that " +
                "clashes with how you drive.");
            GridHoldPeek = cfg.Bind("9. The Grid", "HoldToPeek", true,
                "Hold the key to peek The Grid only while held (it hides on release); a quick tap " +
                "toggles it open so it stays. Off = tap-only toggle, no momentary peek.");
            GridMode = cfg.Bind("9. The Grid", "DisplayMode", UI.Grid.GridDisplayMode.Grid,
                "DEPRECATED - no longer used. The Grid always renders the flat, packed grid " +
                "layout: each storage container (bags, suit, backpack) is a bordered region of " +
                "one-box cells, and tools stay a single cell. The old Nested tree mode and its " +
                "toggles were removed; this value is ignored.");
            GridWinX = cfg.Bind("9. The Grid", "WindowX", -1f,
                "Internal: remembered window left edge in pixels from the screen's top-left. " +
                "-1 = centre the window the first time it opens, then store the position here. " +
                "Set by dragging the title bar.");
            GridWinY = cfg.Bind("9. The Grid", "WindowY", -1f,
                "Internal: remembered window top edge in pixels from the screen's top-left. " +
                "-1 = centre on first open, then store. Set by dragging the title bar.");
            GridWinW = cfg.Bind("9. The Grid", "WindowW", 560f,
                new ConfigDescription("Remembered window width in pixels. Set by dragging the " +
                    "bottom-right resize grip.",
                    new AcceptableValueRange<float>(360f, 1100f)));
            GridWinH = cfg.Bind("9. The Grid", "WindowH", 760f,
                new ConfigDescription("Remembered window height in pixels. Set by dragging the " +
                    "bottom-right resize grip.",
                    new AcceptableValueRange<float>(300f, 1300f)));
            GridCellSize = cfg.Bind("9. The Grid", "CellSize", 46f,
                new ConfigDescription("Edge length in pixels of one inventory cell. Item icons " +
                    "scale with it (icon = size x 0.7). Adjust from the F10 menu; the open " +
                    "window relayouts live.",
                    new AcceptableValueRange<float>(28f, 80f)));

            HudEnabled = cfg.Bind("7. HUD", "Enabled", true,
                "Draw the UI Ascended visor HUD overlay.");
            HudHandBoxes = cfg.Bind("7. HUD", "HandBoxes", true,
                "Bottom-center two-hand boxes (icon, name, charge, state). Never a ten-slot hotbar.");
            HudStatusStrip = cfg.Bind("7. HUD", "StatusStrip", true,
                "Slim top status strip: pressure, temperature, suit battery, air, waste, filters.");
            HudVitals = cfg.Bind("7. HUD", "Vitals", true,
                "Compact corner vitals: health, O2, hydration, nutrition.");
            HudClock = cfg.Bind("7. HUD", "Clock", true,
                "Day counter + time-of-day readout.");
            HudContextPanel = cfg.Bind("7. HUD", "ContextPanel", true,
                "Contextual panel under the reticle showing the looked-at device's name and state.");
            HudVisorArcs = cfg.Bind("7. HUD", "VisorArcs", false,
                "Decorative curved visor edge lines (flat approximation of the curved-visor concept).");
            HudScale = cfg.Bind("7. HUD", "Scale", 1.0f,
                new ConfigDescription("Overall HUD scale.", new AcceptableValueRange<float>(0.6f, 1.6f)));
            HideVanillaHands = cfg.Bind("7. HUD", "HideVanillaHands", true,
                "Hide the vanilla hand slots panel while the UI Ascended hand boxes are shown " +
                "(objects stay alive; restored on toggle/exit). Default ON since the HUD " +
                "Designer replaced the whole vanilla HUD surface.");
            HideVanillaClothing = cfg.Bind("7. HUD", "HideVanillaClothing", true,
                "Hide the vanilla clothing/equipment panel (ours replaces it).");
            HideVanillaStatus = cfg.Bind("7. HUD", "HideVanillaStatus", true,
                "Hide the vanilla status panel (right-side player state window incl. the " +
                "moodlet strip — the HUD's own dashboard replaces it).");
            HideVanillaPlayerState = cfg.Bind("7. HUD", "HideVanillaPlayerState", true,
                "Hide vanilla's bottom-right instrument cluster (internal/external/jetpack/" +
                "health boxes) through the game's own visibility path — the HUD's readout " +
                "cards replace them. Restored the moment this is turned off.");
            HardcoreGating = cfg.Bind("7. HUD", "HardcoreGating", false,
                "Diegetic mode: status strip & vitals need a worn helmet; the context panel needs powered sensor lenses.");

            RadialOuterRadius = cfg.Bind("8. Radial Visuals", "OuterRadius", 240f,
                new ConfigDescription("Outer radius of radial menus in pixels.", new AcceptableValueRange<float>(120f, 480f)));
            RadialInnerRadius = cfg.Bind("8. Radial Visuals", "InnerRadius", 120f,
                new ConfigDescription("Inner hub radius in pixels (the center readout circle; hovering here selects nothing).", new AcceptableValueRange<float>(60f, 260f)));
            IconFlipV = cfg.Bind("8. Radial Visuals", "IconFlipV", false,
                "Flip item icons vertically (toggle if atlas-packed icons render upside down).");
            UseUnityRadial = cfg.Bind("8. Radial Visuals", "UseUnityRadial", true,
                "Use the procedural Unity UGUI radial renderer (runtime-generated wedges via MaskableGraphic, " +
                "TMP auto-sized labels, preserveAspect icons, hover pop animations). This is the primary renderer. " +
                "Turn off to fall back to the legacy ImGui draw-list painter for comparison.");
            RadialIconScale = cfg.Bind("8. Radial Visuals", "IconScale", 1.30f,
                new ConfigDescription("LEGACY (unused since 0.3.1 — see IconRatio).",
                    new AcceptableValueRange<float>(0.5f, 2.5f)));
            RadialIconRatio = cfg.Bind("8. Radial Visuals", "IconRatio", 0.62f,
                new ConfigDescription("Icon size as a fraction of the wedge's available space (the " +
                    "smaller of the ring band's thickness and the wedge's width at mid radius) — icons " +
                    "grow and shrink with the wedges.",
                    new AcceptableValueRange<float>(0.15f, 1.1f)));
            RadialShineIntensity = cfg.Bind("8. Radial Visuals", "ShineIntensity", 1.0f,
                new ConfigDescription("Strength of the gloss/shine blended into wedges (0 = flat/no " +
                    "shader look; colour comes from the RimShine palette entry).",
                    new AcceptableValueRange<float>(0f, 2f)));
            ParkedChipRadius = cfg.Bind("8. Radial Visuals", "ParkedChipRadius", 39f,
                new ConfigDescription("Radius in px of the circles items sit in when dragged out of a " +
                    "radial onto the screen.",
                    new AcceptableValueRange<float>(16f, 80f)));
            RadialShowWedgeLabels = cfg.Bind("8. Radial Visuals", "ShowWedgeLabels", false,
                "Draw the item name under each wedge icon. Off by default: the hub already names " +
                "whatever is selected, and per-wedge names collide on a crowded toolbelt.");
            RadialEdgeFeather = cfg.Bind("8. Radial Visuals", "EdgeFeather", 1.25f,
                new ConfigDescription("Anti-aliasing: width in pixels of the colour ramp on every radial edge. " +
                    "Overlay canvases get no MSAA, so this fringe IS the anti-aliasing. 0 = hard, jagged edges; " +
                    "~1-2 looks right; higher goes soft/glowy.",
                    new AcceptableValueRange<float>(0f, 4f)));
            RadialBorderWidth = cfg.Bind("8. Radial Visuals", "BorderWidth", 3.2f,
                new ConfigDescription("Thickness in pixels of the border around wedges (arcs and, when " +
                    "SideBorders is on, the straight side edges too).",
                    new AcceptableValueRange<float>(0f, 10f)));
            RadialSideBorders = cfg.Bind("8. Radial Visuals", "SideBorders", true,
                "Draw the border along the straight SIDE edges of each wedge too, so every wedge " +
                "wears a complete outline (the always-on orange lines).");
            RadialSideWidthInner = cfg.Bind("8. Radial Visuals", "SideWidthInner", 3.2f,
                new ConfigDescription("Side line width in px where the wedge meets the HUB. Equal " +
                    "inner/outer = straight parallel lines; different values taper the lines.",
                    new AcceptableValueRange<float>(0.5f, 12f)));
            RadialSideWidthOuter = cfg.Bind("8. Radial Visuals", "SideWidthOuter", 3.2f,
                new ConfigDescription("Side line width in px at the OUTER rim.",
                    new AcceptableValueRange<float>(0.5f, 12f)));
            RadialWedgeGapDeg = cfg.Bind("8. Radial Visuals", "WedgeGapDegrees", 1.2f,
                new ConfigDescription("Angular gap between neighbouring wedges, in degrees. The gap is " +
                    "what lets each wedge's outline read as its own line; side edges are anti-aliased " +
                    "whenever a gap exists.",
                    new AcceptableValueRange<float>(0f, 6f)));
            RadialDimShading = cfg.Bind("8. Radial Visuals", "DimShading", true,
                "While one wedge is highlighted, shade the other wedges darker so the selection pops.");
            RadialDimStrength = cfg.Bind("8. Radial Visuals", "DimStrength", 1.0f,
                new ConfigDescription("How hard the non-highlighted wedges dim (0 = no dimming, 1 = full).",
                    new AcceptableValueRange<float>(0f, 1f)));
            RadialFontName = cfg.Bind("8. Radial Visuals", "FontName", "RBNoBold",
                "TMP font asset name (substring match) used by all radial text. Default RBNoBold is the " +
                "game's true bold face (RBNo3.1 — the scoreboard/leaderboard title font). Empty = auto: " +
                "prefer any Bold game font, else the first font the game loaded. The radial editor " +
                "lists every font the game has.");
            RadialUppercaseLabels = cfg.Bind("8. Radial Visuals", "UppercaseLabels", true,
                "Render wedge labels in ALL CAPS (bold-caps look; the hub readout keeps normal case).");
            RadialShowStateText = cfg.Bind("8. Radial Visuals", "ShowStateText", true,
                "Show live state under wedge icons: battery %, canister kPa, stack counts, filter wear. " +
                "Independent of the item-name labels.");
            RadialBindingCurved = cfg.Bind("8. Radial Visuals", "BindingLabelCurved", true,
                "Bend the bound-tool label around the hub GLYPH BY GLYPH so it truly follows the arc. " +
                "Off = the label still sits on the arc, but is drawn as one straight tangential line.");

            const string HB = "10. Hint Bar";
            HintBarCorner = cfg.Bind(HB, "CornerRadius", 12f,
                new ConfigDescription("Corner rounding of the strip. Half the height = a full pill.",
                    new AcceptableValueRange<float>(0f, 24f)));
            HintBarBorderWidth = cfg.Bind(HB, "BorderWidth", 0f,
                new ConfigDescription("Rim thickness. 0 = no border (the rim colour's alpha also has to be > 0).",
                    new AcceptableValueRange<float>(0f, 6f)));
            HintBarFeather = cfg.Bind(HB, "EdgeSoftness", 1.25f,
                new ConfigDescription("Edge anti-aliasing / softness in px.",
                    new AcceptableValueRange<float>(0f, 4f)));
            HintBarSheen = cfg.Bind(HB, "GlassSheen", 0.35f,
                new ConfigDescription("Glass surface top-light. 0 = flat paint.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HintBarSpec = cfg.Bind(HB, "GlassEdgeLight", 0f,
                new ConfigDescription("Glass EDGE light. Note this lights the border run, so it can read " +
                    "as a faint rim even with BorderWidth 0.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HintBarGlow = cfg.Bind(HB, "Glow", 0f,
                new ConfigDescription("Outward glow halo strength. 0 = off.",
                    new AcceptableValueRange<float>(0f, 2f)));
            HintBarGlowWidth = cfg.Bind(HB, "GlowRadius", 24f,
                new ConfigDescription("Glow halo radius in px (only matters when Glow > 0).",
                    new AcceptableValueRange<float>(6f, 160f)));
            HintBarFontSize = cfg.Bind(HB, "FontSize", 11.2f,
                new ConfigDescription("Hint text size. The strip auto-sizes to its text, so this is also " +
                    "what makes the bar longer or shorter.",
                    new AcceptableValueRange<float>(7f, 24f)));
            HintBarBold = cfg.Bind(HB, "Bold", true, "Draw the hint text bold.");
            HintBarHeight = cfg.Bind(HB, "Height", 30f,
                new ConfigDescription("Strip height in px.", new AcceptableValueRange<float>(16f, 56f)));
            HintBarPadding = cfg.Bind(HB, "SidePadding", 24f,
                new ConfigDescription("Total left+right padding around the text.",
                    new AcceptableValueRange<float>(0f, 80f)));
            HintBarDrop = cfg.Bind(HB, "DropBelowWheel", 34f,
                new ConfigDescription("Gap between the wheel's outer edge and the strip.",
                    new AcceptableValueRange<float>(0f, 200f)));
            HintBarFrost = cfg.Bind(HB, "FrostedGlass", false,
                "Blur the screen behind the strip (frosted glass). Same requirements as the radial's " +
                "own frost: HUD Tier C on, the shader bundle loaded, and the visor backdrop running.");
            HintBarFrostStrength = cfg.Bind(HB, "FrostStrength", 0.85f,
                new ConfigDescription("How strong the blur behind the strip is.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HintBarPreview = cfg.Bind(HB, "PreviewOverWhite", false,
                "Editor aid: pin the strip on screen over a white swatch so you can judge its colours " +
                "and transparency without opening a wheel. Cleared when the F10 menu closes.");
            RadialSatelliteScale = cfg.Bind("8. Radial Visuals", "SatelliteScale", 1.0f,
                new ConfigDescription("Size multiplier for child (satellite) radials.",
                    new AcceptableValueRange<float>(0.5f, 1.6f)));
            // The five hub/child readout lines, each sized separately (line 1 = bold title).
            RadialHubTitleSize = cfg.Bind("8. Radial Visuals", "HubTitleSize", 18f,
                new ConfigDescription("Readout line 1 — the BOLD title (open item/menu name).",
                    new AcceptableValueRange<float>(9f, 32f)));
            RadialTextVerb = cfg.Bind("8. Radial Visuals", "ReadoutVerbSize", 15f,
                new ConfigDescription("Readout line 2 — the action verb (Take, Open, Turn Off…).",
                    new AcceptableValueRange<float>(8f, 26f)));
            RadialTextLabel = cfg.Bind("8. Radial Visuals", "ReadoutNameSize", 15f,
                new ConfigDescription("Readout line 3 — the hovered item's name.",
                    new AcceptableValueRange<float>(8f, 26f)));
            RadialTextSub = cfg.Bind("8. Radial Visuals", "ReadoutDetailSize", 12f,
                new ConfigDescription("Readout line 4 — the detail / location sub-line.",
                    new AcceptableValueRange<float>(8f, 24f)));
            RadialTextWarn = cfg.Bind("8. Radial Visuals", "ReadoutStatSize", 12f,
                new ConfigDescription("Readout line 5 — the live stat / warning line.",
                    new AcceptableValueRange<float>(8f, 24f)));
            RadialRotateLongLabels = cfg.Bind("8. Radial Visuals", "RotateLongLabels", true,
                "When a wedge's label is too long to fit horizontally (e.g. 'STABILIZER OFF'), angle it " +
                "along the wedge so it reads without clipping out the sides instead of shrinking to nothing.");
            RadialSatelliteHubRatio = cfg.Bind("8. Radial Visuals", "ChildHubRatio", 0.34f,
                new ConfigDescription("Child (satellite) radial's HUB size as a fraction of its ring — " +
                    "bigger = a roomier centre readout on child radials.", new AcceptableValueRange<float>(0.2f, 0.6f)));
            RadialDynamicReadoutText = cfg.Bind("8. Radial Visuals", "DynamicReadoutText", true,
                "Scale the hub readout text down on small (child) hubs so the lines never overlap, " +
                "while keeping it full-size on the big main hub. Off = fixed sizes everywhere.");

            // Radial effects (0.9.0): the same glass look the HUD panels wear, opted into per radial.
            RadialFrost = cfg.Bind("8. Radial Visuals", "FrostedGlass", false,
                "Frosted-glass blur behind radial wedges: wedge fills sample the same blurred-screen " +
                "backdrop the HUD panels use in Tier C. Requires the HUD's Tier C frosted-glass master " +
                "(F9 > Effects / Visor HUD editor) ON, the shader bundle present, and Flat/VertexWarp " +
                "HUD curvature; silently falls back to the flat wedge look otherwise.");
            RadialFrostStrength = cfg.Bind("8. Radial Visuals", "FrostStrength", 0.85f,
                new ConfigDescription("How strongly the frosted backdrop shows through the wedge fill " +
                    "(0 = none, 1 = full). Only matters while Frosted glass is on AND active.",
                    new AcceptableValueRange<float>(0f, 1f)));
            RadialSheen = cfg.Bind("8. Radial Visuals", "GlassSheen", 0f,
                new ConfigDescription("Milky whitening baked into each wedge toward its OUTER rim " +
                    "(smoked glass catching light). Pure vertex colour — works with or without frost. 0 = off.",
                    new AcceptableValueRange<float>(0f, 1f)));
            RadialEdgeLight = cfg.Bind("8. Radial Visuals", "GlassEdgeLight", 0f,
                new ConfigDescription("Directional rim light on the wedge borders where they face the key " +
                    "light (matching the HUD panels' edge light). Pure vertex colour. 0 = off.",
                    new AcceptableValueRange<float>(0f, 1f)));

            // Every radial colour, live-editable from the F10 colour wheels.
            Overlay.RadialPalette.Bind(cfg);

            // The visor HUD: sizes, curvature, tiers, fonts + its own palette (F9 editor).
            UI.Hud.HudConfig.Bind(cfg);

            // The F10 Control Center theme (follows the HUD palette by default). Bound AFTER
            // HudConfig so its follow-mode derivation reads a live HudPalette.
            UI.Menu.Kit.UiaMenuTheme.Bind(cfg);

            // The Universal Inventory window's theme (follows the global box theme by default).
            // Same ordering reason as above: it derives from a live HudPalette/HudConfig.
            UI.Grid.GridTheme.Bind(cfg);
        }
    }
}

