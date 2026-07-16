using System;
using System.Collections.Generic;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>One rebindable mod action. <see cref="Get"/>/<see cref="Set"/> read and write the
    /// backing BepInEx config entry, so every existing consumer (which reads the config value
    /// directly) keeps working unchanged. <see cref="NativeRebindable"/> marks the single-key
    /// actions that can appear as a row in the game's own Controls screen (the vanilla capture
    /// UI records exactly one KeyCode, so chorded actions like Ctrl+digit can't be native rows).</summary>
    public sealed class UiaBind
    {
        public string Id;              // stable name, e.g. "UIA_ToolRadial" (also the vanilla KeyItem name)
        public string Label;           // human label for the menu / guide
        public string Group;           // controls-group label
        public bool NativeRebindable;  // can be a row in the vanilla Controls screen
        public KeyCode Default;        // factory default (for the native registration + "reset")
        public Func<KeyCode> Get;      // read the live key from the config entry
        public Action<KeyCode> Set;    // write the config entry
    }

    /// <summary>
    /// The single registry of the mod's key bindings. Two jobs:
    ///   1. A stable list every surface reads — the Control Center's Controls tab, the radial
    ///      hint bar, and the guide — so a rebind shows everywhere at once.
    ///   2. (Phase 2) the source for registering our single-key actions in the game's native
    ///      Controls screen via a Harmony postfix on KeyManager.SetupKeyBindings; a native rebind
    ///      is mirrored back into the config entry so nothing else has to change.
    /// Reads route through <see cref="Key"/>, which prefers a live vanilla rebind when the action
    /// has been registered with KeyManager and otherwise falls back to the config value.
    /// </summary>
    public static class UiaKeybinds
    {
        public const string GroupName = "UI Ascended";

        private static readonly List<UiaBind> _all = new List<UiaBind>();
        public static IReadOnlyList<UiaBind> All => _all;

        private static bool _built;

        /// <summary>Build the registry. Safe to call repeatedly; must run AFTER UIAConfig.Bind so
        /// the backing entries exist when a Get/Set lambda is invoked.</summary>
        public static void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _all.Clear();

            Add("UIA_ToolRadial", "Open tool / device radial", KeyCode.R,
                () => UIAConfig.ToolRadialKey.Value, k => UIAConfig.ToolRadialKey.Value = k);
            Add("UIA_ToolbeltRadial", "Open toolbelt radial", KeyCode.Mouse2,
                () => UIAConfig.ToolbeltRadialKey.Value, k => UIAConfig.ToolbeltRadialKey.Value = k);
            Add("UIA_BagRadial", "Open bag / backpack radial", KeyCode.Tab,
                () => UIAConfig.BagRadialKey.Value, k => UIAConfig.BagRadialKey.Value = k);
            Add("UIA_Menu", "Open the UI Ascended menu", KeyCode.F10,
                () => UIAConfig.SettingsWindowKey.Value, k => UIAConfig.SettingsWindowKey.Value = k);
            Add("UIA_HudDesigner", "Open the HUD designer", KeyCode.F9,
                () => UI.Hud.HudConfig.HudEditorKey.Value, k => UI.Hud.HudConfig.HudEditorKey.Value = k);
            Add("UIA_HandSwap", "Swap active hand (radial open)", KeyCode.E,
                () => UIAConfig.RadialHandSwapKey.Value, k => UIAConfig.RadialHandSwapKey.Value = k);
            Add("UIA_Page", "Page a crowded radial", KeyCode.Q,
                () => UIAConfig.RadialPageKey.Value, k => UIAConfig.RadialPageKey.Value = k);
            Add("UIA_FineAdjust", "Fine value adjust (radial)", KeyCode.C,
                () => UIAConfig.RadialFineAdjustKey.Value, k => UIAConfig.RadialFineAdjustKey.Value = k);
        }

        private static void Add(string id, string label, KeyCode def, Func<KeyCode> get, Action<KeyCode> set)
        {
            _all.Add(new UiaBind
            {
                Id = id,
                Label = label,
                Group = GroupName,
                NativeRebindable = true,
                Default = def,
                Get = get,
                Set = set,
            });
        }

        public static UiaBind Find(string id)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == id) return _all[i];
            return null;
        }

        // ---- native game Controls integration (see UiaKeybindsPatch) ----

        private static bool _registered;
        private static bool _hooked;

        /// <summary>Push a just-rebound key into the game's native KeyItem too (if registered), so
        /// the game's Controls screen shows it and it persists in the settings XML. Fail-soft.</summary>
        public static void PushToGame(UiaBind b, KeyCode k)
        {
            if (b == null) return;
            try
            {
                KeyItem item = KeyManager.GetKeyitem(b.Id);
                if (item != null) item.Key = k;
            }
            catch { }
        }

        /// <summary>Register our single-key actions as rows in the game's Controls screen. Called
        /// from a Harmony postfix on KeyManager.SetupKeyBindings (so it runs before the panel builds
        /// and before settings reconciliation). Fail-soft: if anything is missing, mod-panel
        /// rebinding still works and this simply does nothing.</summary>
        public static void RegisterWithGame()
        {
            EnsureBuilt();
            try
            {
                ControlsGroup group = null;
                try { group = KeyManager.GetControlsGroup(GroupName); } catch { group = null; }
                if (group == null) group = new ControlsGroup(GroupName);
                var addKey = typeof(KeyManager).GetMethod("AddKey",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (addKey == null) return;
                for (int i = 0; i < _all.Count; i++)
                {
                    var b = _all[i];
                    if (!b.NativeRebindable) continue;
                    if (KeyManager.GetKeyitem(b.Id) != null) continue;
                    // The postfix can run before OnLoaded binds the config, so a Get() may NRE —
                    // fall back to the hardcoded default; the settings-XML load reconciles by name.
                    KeyCode def;
                    try { def = b.Get != null ? b.Get() : b.Default; } catch { def = b.Default; }
                    addKey.Invoke(null, new object[] { b.Id, def, group, false });
                }
                _registered = true;
                if (!_hooked) { KeyManager.OnControlsChanged += SyncFromGame; _hooked = true; }
                SyncFromGame();
            }
            catch (Exception e) { UIALog.Warn("UiaKeybinds.RegisterWithGame: " + e.Message); }
        }

        /// <summary>Mirror the game's (possibly rebound) key back into our config entries, so every
        /// consumer that reads the config value picks up a native Controls-screen rebind.</summary>
        public static void SyncFromGame()
        {
            if (!_registered) return;
            for (int i = 0; i < _all.Count; i++)
            {
                var b = _all[i];
                if (!b.NativeRebindable || b.Get == null || b.Set == null) continue;
                try
                {
                    KeyItem item = KeyManager.GetKeyitem(b.Id);
                    if (item == null) continue;
                    if (b.Get() != item.Key) b.Set(item.Key);
                }
                catch { }
            }
        }

        public static void Unhook()
        {
            try { if (_hooked) KeyManager.OnControlsChanged -= SyncFromGame; } catch { }
            _hooked = false;
            _registered = false;
        }

        /// <summary>The live key for an action: the config value (kept in sync with any native
        /// rebind — Phase 2). KeyCode.None if the id is unknown.</summary>
        public static KeyCode Key(string id)
        {
            var b = Find(id);
            return b != null && b.Get != null ? b.Get() : KeyCode.None;
        }

        /// <summary>Short on-screen glyph for an action's current key (for the hint bar / chips).</summary>
        public static string Glyph(string id) => Glyph(Key(id));

        /// <summary>A compact, readable label for a KeyCode ("MMB", "Alt", "1", "F10"…).</summary>
        public static string Glyph(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "MMB";
                case KeyCode.Mouse3: return "M4";
                case KeyCode.Mouse4: return "M5";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.None: return "-";
            }
            string s = k.ToString();
            if (s.StartsWith("Alpha") && s.Length == 6) return s.Substring(5);   // Alpha1 -> 1
            if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
            return s;
        }
    }
}
