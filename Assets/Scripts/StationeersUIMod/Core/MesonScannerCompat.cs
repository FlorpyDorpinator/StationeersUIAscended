using System;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using HarmonyLib;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Soft-dependency bridge to Elmotrix's "Meson Scanner" mod (Steam Workshop 3163662089,
    /// assembly <c>MesonScannerMod</c>). READ-ONLY: no game-state mutation, no compile-time
    /// reference, nothing at all happens unless the local player is actually wearing vanilla
    /// Sensor Lenses with the vanilla meson SPU in them, switched on.
    ///
    /// <para><b>What that mod does</b> (from its v0.24 decompile; verified against the game
    /// decompile V27798): a Harmony prefix on <c>SPUMesonScanner.Render</c> replaces the vanilla
    /// meson render with a filtered one whose state is two STATIC enums on
    /// <c>MesonScannerMod.Utils</c> - <c>CurrentMode</c> (Pipes/Cables/Chutes/Other) and
    /// <c>CurrentDisplayMode</c> (Mode1..3) - plus a private <c>LockIn</c> bool. Its only
    /// feedback is a per-frame <c>WindowTitleBar.SetTitle(text)</c> on the vanilla GLASSES
    /// inventory window (when that window is visible), where <c>text</c> is a LOCAL built inside
    /// the render prefix. UIA replaces those slot windows, so the player never saw it.</para>
    ///
    /// <para><b>How the mode changes</b>: a prefix on <c>InventoryManager.NormalMode</c> polls raw
    /// keys while the lenses are on - LeftShift = next display mode, Mouse2 (MMB) = lock-in
    /// toggle, and with an EMPTY active hand: RMB = next mode, LMB = lock onto the network under
    /// the cursor, wheel = step through locked networks. No interactable is involved, so there is
    /// nothing for <see cref="ItemActions"/> / the radial device controls to offer; UIA leaves
    /// that input alone (vanilla only calls NormalMode while the cursor is hidden, so clicks
    /// inside UIA's radials/windows never reach it).</para>
    ///
    /// <para><b>Multiplayer</b>: the mode is a per-peer rendering preference held in the mod's
    /// own statics - it is never networked, and each peer's scanner draws from its own copy. So
    /// what we show on an MP client is that client's actual truth, not a guess. The gates we add
    /// (lenses worn, <c>Sensor</c>, <c>OnOff</c>, and vanilla's render gate <c>IsOperable</c> +
    /// <c>InteractOnOff.State</c>) are the same Thing state vanilla's own per-peer render reads.</para>
    ///
    /// <para>The label text MIRRORS the mod's own strings (v0.24) since its text is never stored.
    /// Lookup is by enum NAME, so a reordered enum still labels correctly; an unknown name
    /// degrades to a generic "MODE n" label rather than a wrong one. Any binding failure =
    /// one warning in the log and the feature latches off until restart / F6.</para>
    /// </summary>
    internal static class MesonScannerCompat
    {
        private const string AsmName = "MesonScannerMod";
        private const string UtilsTypeName = "MesonScannerMod.Utils";

        private enum BindState { Unresolved, Ready, Off }

        private static BindState _state;
        private static Type _modeEnum;
        private static Type _displayEnum;
        private static Func<int> _getMode;
        private static Func<int> _getDisplay;
        private static Func<bool> _getLock;   // optional: missing field = no lock suffix
        // Optional: the mod's private Utils.lockCables - the cable networks its LAST render drew
        // (rebuilt every Cables-mode render; a lock-in returns a non-empty subset). Its emptiness
        // decides the Cables + Ctrl title. Missing = assume networks exist (the usual case).
        private static Func<object> _getCableNets;

        // Last label built + the state it was built from, so the steady state allocates nothing.
        private static int _lastKey = int.MinValue;
        private static string _lastLabel = "";

        /// <summary>Hot-reload rule: every static resets (HudSystem.Shutdown).</summary>
        internal static void Shutdown()
        {
            _state = BindState.Unresolved;
            _modeEnum = null;
            _displayEnum = null;
            _getMode = null;
            _getDisplay = null;
            _getLock = null;
            _getCableNets = null;
            _lastKey = int.MinValue;
            _lastLabel = "";
        }

        /// <summary>Whether the Meson Scanner mod is loaded and bound (resolves on first call).
        /// Only the F9 show-all preview asks this without a scanner on.</summary>
        internal static bool Installed
        {
            get
            {
                Resolve();
                return _state == BindState.Ready;
            }
        }

        /// <summary>The scanner mode label for the local player, or false when there is nothing
        /// to show (mod absent, lenses not worn / not the meson SPU / switched off).
        /// <paramref name="powered"/> mirrors vanilla's render gate EXACTLY - <c>Sensor != null &amp;&amp;
        /// IsOperable &amp;&amp; InteractOnOff.State == 1</c> (live V27798 SensorLenses.UpdateEachFrame), the
        /// only path that calls <c>Sensor.Render</c> and therefore the mod's render prefix. A scanner
        /// that fails it (flat / missing battery) keeps its mode (the mod still takes input) but draws
        /// nothing, so the HUD dims the tag. (<c>Powered</c> is NOT that gate: it is set by the
        /// server's CheckPower and lags the battery, so it could dim a scanner that is drawing.)</summary>
        internal static bool TryGetMode(Human human, out string label, out bool powered)
        {
            label = null;
            powered = false;
            if (human == null || _state == BindState.Off) return false;

            // Cheap vanilla gate FIRST - exactly the mod's own input gate (KeyToggles prefix):
            // SensorLenses in the GLASSES slot, Sensor is SPUMesonScanner, OnOff. Only past this
            // do we ever look for the mod, so a player without it pays a slot read per frame.
            SensorLenses lenses;
            try
            {
                var slot = human.GlassesSlot;
                lenses = slot != null ? slot.Get() as SensorLenses : null;
                if (lenses == null || !(lenses.Sensor is SPUMesonScanner) || !lenses.OnOff) return false;
                var onOff = lenses.InteractOnOff;
                powered = lenses.IsOperable && onOff != null && onOff.State == 1;
            }
            catch { return false; }

            Resolve();
            if (_state != BindState.Ready) return false;

            int mode, display;
            bool locked, ctrl, noCableNets = false, noPlants = false;
            try
            {
                mode = _getMode();
                display = _getDisplay();
                locked = _getLock != null && _getLock();
                // The mod reads the raw LeftControl key for its "+ Connected Devices" variant
                // (KeyManager.GetButton(KeyCode) = Input.GetKey unless the console is open).
                ctrl = KeyManager.GetButton(KeyCode.LeftControl);
                string modeName = NameOf(_modeEnum, mode);
                string displayName = NameOf(_displayEnum, display);
                // Cables + Ctrl: the mod switches to "Connected Devices - Operational State" only
                // INSIDE its per-network loop, so with no cable network drawn its title stays
                // "<cable mode> + Connected Devices".
                if (ctrl && modeName == "Cables" && _getCableNets != null)
                {
                    var nets = _getCableNets() as System.Collections.ICollection;
                    noCableNets = nets != null && nets.Count == 0;
                }
                // Other / Mode1 (plants): the mod sets its title only inside the per-plant loop, so
                // with no plant in the world its title is EMPTY. Plant.AllPlants is vanilla's own
                // list (live V27798 Plant.cs:29), maintained on every peer.
                if (modeName == "Other" && displayName == "Mode1")
                    noPlants = Plant.AllPlants.Count == 0;
            }
            catch (Exception e)
            {
                LatchOff("reading its mode failed (" + e.GetType().Name + ")");
                return false;
            }

            int key = (mode & 0xFF) | ((display & 0xFF) << 8) | (ctrl ? 1 << 16 : 0) | (locked ? 1 << 17 : 0)
                | (noCableNets ? 1 << 18 : 0) | (noPlants ? 1 << 19 : 0);
            if (key != _lastKey)
            {
                _lastKey = key;
                _lastLabel = BuildLabel(mode, display, ctrl, locked, noCableNets, noPlants);
            }
            label = _lastLabel;
            return true;
        }

        // ------------------------------------------------------------------ binding

        private static void Resolve()
        {
            if (_state != BindState.Unresolved) return;
            _state = BindState.Off; // pessimistic: any early return below leaves it off

            try
            {
                Type utils = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name;
                    try { name = asm.GetName().Name; }
                    catch { continue; }
                    if (name != AsmName) continue;
                    Type t = null;
                    try { t = asm.GetType(UtilsTypeName, false); }
                    catch { }
                    if (t != null) utils = t; // last wins (a script-engine reload of that mod)
                }
                if (utils == null)
                {
                    // Not installed - the normal case. Info, not a warning: nothing is wrong.
                    UIALog.Info("Meson Scanner mod not loaded - the HUD scanner-mode tag stays off.");
                    return;
                }

                const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var fMode = utils.GetField("CurrentMode", Any);
                var fDisplay = utils.GetField("CurrentDisplayMode", Any);
                if (!IsIntEnum(fMode) || !IsIntEnum(fDisplay))
                {
                    LatchOff("its Utils.CurrentMode / CurrentDisplayMode fields changed");
                    return;
                }

                // Its Render prefix must really be live - if a game update broke that patch the
                // vanilla scanner is drawing (no modes), and a mode label would be a lie.
                if (!RenderPrefixFrom(utils.Assembly))
                {
                    LatchOff("its SPUMesonScanner.Render patch is not installed");
                    return;
                }

                _modeEnum = fMode.FieldType;
                _displayEnum = fDisplay.FieldType;
                _getMode = StaticGetter<Func<int>>(fMode, typeof(int));
                _getDisplay = StaticGetter<Func<int>>(fDisplay, typeof(int));

                var fLock = utils.GetField("LockIn", Any);
                if (fLock != null && fLock.FieldType == typeof(bool))
                {
                    try { _getLock = StaticGetter<Func<bool>>(fLock, typeof(bool)); }
                    catch { _getLock = null; } // optional - the label just loses its lock suffix
                }

                var fCables = utils.GetField("lockCables", Any);
                if (fCables != null && !fCables.FieldType.IsValueType
                    && typeof(System.Collections.ICollection).IsAssignableFrom(fCables.FieldType))
                {
                    try { _getCableNets = StaticGetter<Func<object>>(fCables, typeof(object)); }
                    catch { _getCableNets = null; } // optional - Cables + Ctrl assumes networks exist
                }

                _state = BindState.Ready;
                UIALog.Info("Elmotrix's Meson Scanner detected - its scanner mode shows on the HUD glasses box.");
            }
            catch (Exception e)
            {
                LatchOff("binding failed (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private static void LatchOff(string why)
        {
            _state = BindState.Off;
            _getMode = _getDisplay = null;
            _getLock = null;
            _getCableNets = null;
            UIALog.Warn("Meson Scanner compatibility disabled: " + why + ". UI Ascended works normally; only the HUD scanner-mode tag is off.");
        }

        private static bool IsIntEnum(FieldInfo f)
        {
            return f != null && f.IsStatic && f.FieldType.IsEnum
                && Enum.GetUnderlyingType(f.FieldType) == typeof(int);
        }

        private static bool RenderPrefixFrom(Assembly modAsm)
        {
            var render = AccessTools.Method(typeof(SPUMesonScanner), "Render");
            if (render == null) return false;
            var info = Harmony.GetPatchInfo(render);
            if (info == null || info.Prefixes == null) return false;
            foreach (var p in info.Prefixes)
            {
                var m = p != null ? p.PatchMethod : null;
                if (m != null && m.DeclaringType != null && m.DeclaringType.Assembly == modAsm) return true;
            }
            return false;
        }

        /// <summary>An allocation-free getter for a static field (a reflected FieldInfo.GetValue
        /// would box the enum every frame). An int-backed enum is an int on the IL stack.</summary>
        private static T StaticGetter<T>(FieldInfo f, Type ret) where T : class
        {
            var dm = new DynamicMethod("UIA_MesonGet_" + f.Name, ret, Type.EmptyTypes, f.DeclaringType, true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, f);
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate(typeof(T)) as T;
        }

        // ------------------------------------------------------------------ label

        /// <summary>Runs only when the mode/keys change. Mirrors the title text the mod's render
        /// prefix builds (v0.24), upper-cased to match the HUD's slot labels. ASCII only (TMP).</summary>
        private static string BuildLabel(int mode, int display, bool ctrl, bool locked, bool noCableNets, bool noPlants)
        {
            string modeName = NameOf(_modeEnum, mode);
            string displayName = NameOf(_displayEnum, display);
            string body = Describe(modeName, displayName, ctrl, noCableNets, noPlants);
            if (body == null)
                body = (modeName ?? ("MODE " + mode)) + " " + (display + 1);
            // The mod's own title is EMPTY here (plants mode, no plants): no mode text, just the tag.
            string s = body.Length == 0 ? "MESON" : "MESON: " + body.ToUpperInvariant();
            if (locked) s += " [LOCKED]";
            return s;
        }

        private static string NameOf(Type enumType, int value)
        {
            try { return enumType != null ? Enum.GetName(enumType, value) : null; }
            catch { return null; }
        }

        private static string Describe(string mode, string display, bool ctrl, bool noCableNets, bool noPlants)
        {
            const string Devices = " + Connected Devices";
            switch (mode)
            {
                case "Pipes":
                    switch (display)
                    {
                        case "Mode1": return "Pipes - Painted Color" + (ctrl ? Devices : "");
                        case "Mode2": return "Pipes - Stress" + (ctrl ? Devices : "");
                        case "Mode3": return "Pipes - Temperature" + (ctrl ? Devices : "");
                    }
                    break;
                case "Cables":
                    // The mod overwrites its cable title with this whenever Ctrl is held AND at least
                    // one cable network is drawn (the overwrite sits inside its per-network loop).
                    if (ctrl && !noCableNets) return "Connected Devices - Operational State";
                    switch (display)
                    {
                        case "Mode1": return "Cables + Pipes - Painted Color" + (ctrl ? Devices : "");
                        case "Mode2": return "Cables - Painted Color" + (ctrl ? Devices : "");
                        case "Mode3": return "Cables - Load" + (ctrl ? Devices : "");
                    }
                    break;
                case "Chutes":
                    switch (display)
                    {
                        case "Mode1": return "Chutes - Painted Color" + (ctrl ? Devices : "");
                        case "Mode2": return "Chutes - Contents" + (ctrl ? Devices : "");
                        case "Mode3": return "Loose Items";
                    }
                    break;
                case "Other":
                    switch (display)
                    {
                        case "Mode1":
                            if (noPlants) return "";   // the mod's title stays empty with no plants
                            return ctrl ? "Plants - Growth Efficiency" : "Plants - Growth Stage";
                        case "Mode2": return "Structures - Convection";
                        case "Mode3": return "Structures - Damage";
                    }
                    break;
            }
            return null;
        }
    }
}
