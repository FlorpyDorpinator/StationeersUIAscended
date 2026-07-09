using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIAscended
{
    /// <summary>
    /// All user-facing configuration. Bound once at plugin init; StationeersLaunchPad
    /// auto-renders the resulting .cfg in its in-game mod config panel.
    /// </summary>
    public static class UIAConfig
    {
        // --- General ---
        public static ConfigEntry<bool> MasterEnable;
        public static ConfigEntry<KeyCode> SettingsWindowKey;
        public static ConfigEntry<int> HoldThresholdMs;

        // --- Toolbelt radial ---
        public static ConfigEntry<bool> ToolbeltRadialEnabled;
        public static ConfigEntry<KeyCode> ToolbeltRadialKey;
        public static ConfigEntry<bool> ToolbeltShowStowEntries;

        // --- Tool radial ---
        public static ConfigEntry<bool> ToolRadialEnabled;
        public static ConfigEntry<KeyCode> ToolRadialKey;
        public static ConfigEntry<bool> ToolRadialTakeOverVanillaKey;

        // --- Bag radial ---
        public static ConfigEntry<bool> BagRadialEnabled;
        public static ConfigEntry<KeyCode> BagRadialKey;
        public static ConfigEntry<bool> BagRadialTapOpens;
        public static ConfigEntry<int> BagRadialGroupThreshold;

        // --- Equipment key radials (1-6) ---
        public static ConfigEntry<bool> EquipmentKeyRadialsEnabled;

        // --- Emergency inject (testing aid) ---
        public static ConfigEntry<bool> EmergencyInjectEnabled;
        public static ConfigEntry<KeyCode> EmergencyHealKey;
        public static ConfigEntry<KeyCode> EmergencyStimKey;

        // --- Slot finder ---
        public static ConfigEntry<int> ScanDepth;
        public static ConfigEntry<bool> AllowToolSlotSources;

        // --- SmartStow+ ---
        public static ConfigEntry<bool> SmartStowPlusEnabled;
        public static ConfigEntry<bool> StowPreferStacks;
        public static ConfigEntry<bool> StowUseProfiles;
        public static ConfigEntry<bool> StowUseTypeMemory;
        public static ConfigEntry<bool> StowIntoNestedBags;

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
        public static ConfigEntry<bool> HardcoreGating;

        // --- Radial visuals ---
        public static ConfigEntry<float> RadialOuterRadius;
        public static ConfigEntry<float> RadialInnerRadius;
        public static ConfigEntry<bool> IconFlipV;

        public static void Bind(ConfigFile cfg)
        {
            MasterEnable = cfg.Bind("1. General", "MasterEnable", true,
                "Master switch. When off, the mod draws and patches nothing.");
            SettingsWindowKey = cfg.Bind("1. General", "SettingsWindowKey", KeyCode.F10,
                "Key that toggles the in-game UI Ascended settings window.");
            HoldThresholdMs = cfg.Bind("1. General", "HoldThresholdMs", 180,
                new ConfigDescription("How long a radial key must be held before the radial opens (ms). Shorter taps fall through to the vanilla action.",
                    new AcceptableValueRange<int>(60, 600)));

            ToolbeltRadialEnabled = cfg.Bind("2. Toolbelt Radial", "Enabled", true,
                "Hold a key to open a radial of everything on your toolbelt; release over a tool to equip it into the active hand.");
            ToolbeltRadialKey = cfg.Bind("2. Toolbelt Radial", "Key", KeyCode.Mouse2,
                "Radial key (default: middle mouse; the vanilla PingHighlight binding on Mouse2 is currently unused by the game).");
            ToolbeltShowStowEntries = cfg.Bind("2. Toolbelt Radial", "ShowStowEntries", true,
                "Always show empty belt slots in the radial, so you can put the held tool back (disabled entries when nothing fits).");

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

            EmergencyInjectEnabled = cfg.Bind("4c. Emergency Inject", "Enabled", true,
                "Testing aid: a modifier+key that finds a health/stim auto-injector you OWN and uses it on " +
                "yourself, even while incapacitated. On a multiplayer client the mod cannot heal you directly " +
                "(health is server-authoritative) - it brings the injector to hand for a one-click vanilla use.");
            EmergencyHealKey = cfg.Bind("4c. Emergency Inject", "HealKey", KeyCode.Alpha9,
                "Hold Shift + this key = use a health auto-injector on yourself.");
            EmergencyStimKey = cfg.Bind("4c. Emergency Inject", "StimKey", KeyCode.Alpha8,
                "Hold Shift + this key = use a stim (epinephrine) auto-injector on yourself.");

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
            HideVanillaHands = cfg.Bind("7. HUD", "HideVanillaHands", false,
                "Hide the vanilla hand slots panel while the UI Ascended hand boxes are shown (objects stay alive; restored on toggle/exit).");
            HideVanillaClothing = cfg.Bind("7. HUD", "HideVanillaClothing", false,
                "Hide the vanilla clothing/equipment panel.");
            HideVanillaStatus = cfg.Bind("7. HUD", "HideVanillaStatus", false,
                "Hide the vanilla status panel (right-side player state window).");
            HardcoreGating = cfg.Bind("7. HUD", "HardcoreGating", false,
                "Diegetic mode: status strip & vitals need a worn helmet; the context panel needs powered sensor lenses.");

            RadialOuterRadius = cfg.Bind("8. Radial Visuals", "OuterRadius", 240f,
                new ConfigDescription("Outer radius of radial menus in pixels.", new AcceptableValueRange<float>(120f, 480f)));
            RadialInnerRadius = cfg.Bind("8. Radial Visuals", "InnerRadius", 90f,
                new ConfigDescription("Inner dead-zone radius in pixels (hovering here selects nothing / backs out).", new AcceptableValueRange<float>(40f, 240f)));
            IconFlipV = cfg.Bind("8. Radial Visuals", "IconFlipV", false,
                "Flip item icons vertically (toggle if atlas-packed icons render upside down).");
        }
    }
}
