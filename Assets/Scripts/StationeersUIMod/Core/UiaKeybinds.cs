using System;
using System.Collections.Generic;
using Assets.Scripts.UI;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>One rebindable mod action. <see cref="Get"/>/<see cref="Set"/> read and write the
    /// backing BepInEx config entry, so every existing consumer (which reads the config value
    /// directly) keeps working unchanged. <see cref="NativeRebindable"/> marks the actions that get
    /// a row in the game's OWN Controls screen — deliberately only UIA_Menu; see the class remarks
    /// on <see cref="UiaKeybinds"/> for why the rest are Control-Center-only.</summary>
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
    ///   2. Registering UIA_Menu in the game's native Controls screen via a Harmony postfix on
    ///      KeyManager.SetupKeyBindings; a native rebind is mirrored back into the config entry
    ///      so nothing else has to change.
    /// Reads route through <see cref="Key"/>, which prefers a live vanilla rebind when the action
    /// has been registered with KeyManager and otherwise falls back to the config value.
    ///
    /// WHY ONLY UIA_Menu IS A NATIVE ROW (do not "fix" this by registering the rest):
    /// vanilla flags a conflict whenever two KeyItems share a KeyCode
    /// (Assets/Scripts/UI/KeyLookup.cs:38-56 -> ControlsAssignment.Conflicts -> the CONTROLS
    /// CONFLICT banner). Our binds are context-dependent — they only live while a radial/overlay
    /// is open, and they coexist with vanilla by hold-vs-tap arbitration — but vanilla's flat key
    /// list has no way to express that, so registering all ten MANUFACTURED a permanent banner
    /// against ActiveHandSlot(R), SwapHands(E), Drop(Q), QuantityModifier(C), InstantStop(B),
    /// ShowScoreBoard(Tab), SpawnItem(F9) and PingHighlight(MMB).
    ///
    /// The two suppression hooks vanilla offers were both rejected:
    ///   * KeyManager.NeverConflict — its second loop (KeyManager.cs:649) matches on KEYCODE, not
    ///     identity, so whitelisting our names would blanket-exempt every keyitem on those eight
    ///     keycodes. A user's own later vanilla-vs-vanilla clash on R would go white and
    ///     unreported, and the dead zone follows our binds (GetKey resolves live). Degrading
    ///     vanilla's only conflict diagnostic to hide noise we created is the wrong trade.
    ///   * KeyManager.IgnoreConflictKeyMaps — correct instrument (name-pair, not keycode), but it
    ///     needs symmetric closure plus active Conflicts pruning, because KeyLookup.Refresh only
    ///     REMOVES from Conflicts when a bucket drops to <=1 (KeyLookup.cs:49-53). Installing it
    ///     after the rows exist (i.e. every F6) yields banner-up-with-all-rows-white, unclearable
    ///     without a restart. And it would hide overlaps that are real, not phantom.
    /// So we simply stop creating the conflict: UIA_Menu (F10) has no vanilla occupant and can't
    /// clash, and it acts as the discoverability anchor pointing at the Control Center, where the
    /// other nine are rebound. Zero mutation of vanilla statics, vanilla's diagnostic untouched.
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
            Add("UIA_Grid", "Open Universal Inventory", KeyCode.B,
                () => UIAConfig.GridKey.Value, k => UIAConfig.GridKey.Value = k);
            // The ONE native row (see the class remarks): F10 has no vanilla occupant, so it can
            // never raise the CONTROLS CONFLICT banner, and it points players at the Control
            // Center where every other UIA bind lives.
            Add("UIA_Menu", "Open the UI Ascended menu", KeyCode.F10,
                () => UIAConfig.SettingsWindowKey.Value, k => UIAConfig.SettingsWindowKey.Value = k,
                nativeRebindable: true);
            Add("UIA_HudDesigner", "Open the HUD designer", KeyCode.F9,
                () => UI.Hud.HudConfig.HudEditorKey.Value, k => UI.Hud.HudConfig.HudEditorKey.Value = k);
            Add("UIA_HandSwap", "Swap active hand (radial open)", KeyCode.E,
                () => UIAConfig.RadialHandSwapKey.Value, k => UIAConfig.RadialHandSwapKey.Value = k);
            Add("UIA_Page", "Page a crowded radial", KeyCode.Q,
                () => UIAConfig.RadialPageKey.Value, k => UIAConfig.RadialPageKey.Value = k);
            Add("UIA_FineAdjust", "Fine value adjust (radial)", KeyCode.C,
                () => UIAConfig.RadialFineAdjustKey.Value, k => UIAConfig.RadialFineAdjustKey.Value = k);
            Add("UIA_HeadLook", "Head-look (hold, radial open)", KeyCode.Mouse2,
                () => UIAConfig.RadialHeadLookKey.Value, k => UIAConfig.RadialHeadLookKey.Value = k);
        }

        private static void Add(string id, string label, KeyCode def, Func<KeyCode> get, Action<KeyCode> set,
                                bool nativeRebindable = false)
        {
            _all.Add(new UiaBind
            {
                Id = id,
                Label = label,
                Group = GroupName,
                NativeRebindable = nativeRebindable,
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
        /// the game's Controls screen shows it and it persists in the settings XML. Fail-soft.
        /// Routes through ControlsAssignment.Deregister + Display.Assign rather than writing
        /// KeyItem.Key directly: vanilla indexes its conflict buckets by KeyCode
        /// (ControlsAssignment.KeyHashLookup), so a raw Key write leaves the item filed under its
        /// OLD key — which desyncs conflict reporting for every other item in both buckets.</summary>
        public static void PushToGame(UiaBind b, KeyCode k)
        {
            if (b == null) return;
            try
            {
                KeyItem item = KeyManager.GetKeyitem(b.Id);
                if (item == null) return;
                if (item.Display != null)
                {
                    // Deregister FIRST — it looks the bucket up by the current (old) Key.
                    ControlsAssignment.Deregister(item, false);
                    item.Display.Assign(k, true);   // sets Key, re-registers in the new bucket, refreshes the row
                }
                else
                {
                    item.Key = k;                   // no row built yet: no bucket to keep coherent
                }
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
                // Explicit argument types (CLAUDE.md's CheckDisplaySlot rule): a future overload
                // yields null here rather than an AmbiguousMatchException on the boot path.
                var addKey = typeof(KeyManager).GetMethod("AddKey",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(KeyCode), typeof(ControlsGroup), typeof(bool) },
                    null);
                if (addKey == null) return;
                // Too late to register cleanly: vanilla's ControlsAssignment rows are built once,
                // in Settings.SetupValues, and KeyManager.AssignDefaultKeys ("Reset to Defaults")
                // dereferences keyItem.Display over ALL of AllKeys. A KeyItem added after the rows
                // exist has Display == null and would NRE vanilla's reset mid-loop. This only
                // happens when the mod first loads mid-session via F6/ScriptEngine; skipping costs
                // one native row on a dev path and keeps vanilla intact.
                bool rowsAlreadyBuilt = false;
                try { rowsAlreadyBuilt = ControlsAssignment.ControlsItems.Count > 0; } catch { }
                for (int i = 0; i < _all.Count; i++)
                {
                    var b = _all[i];
                    if (!b.NativeRebindable) continue;
                    if (KeyManager.GetKeyitem(b.Id) != null) continue;
                    if (rowsAlreadyBuilt) continue;
                    // KeyItem's ctor takes DefaultKey from the key it is constructed with, so
                    // register at the FACTORY default (or vanilla's "Reset to Defaults" would
                    // restore whatever the key happened to be at load), then apply the live value.
                    addKey.Invoke(null, new object[] { b.Id, b.Default, group, false });
                    // The postfix can run before OnLoaded binds the config, so a Get() may NRE —
                    // leave the default in place; the settings-XML load reconciles by name.
                    try
                    {
                        KeyItem added = KeyManager.GetKeyitem(b.Id);
                        if (added != null && b.Get != null) added.Key = b.Get();
                    }
                    catch { }
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
