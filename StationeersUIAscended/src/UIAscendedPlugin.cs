using System;
using Assets.Scripts;
using BepInEx;
using HarmonyLib;
using StationeersUIAscended.Core;
using StationeersUIAscended.Features;
using StationeersUIAscended.Windows;
using UI.ImGuiUi.ImGuiWindows;
using UnityEngine;

namespace StationeersUIAscended
{
    /// <summary>
    /// Stationeers UI Ascended. Loads three ways with identical behavior:
    /// BepInEx chainloader (plugins folder), StationeersLaunchPad (folder mod — SLP
    /// AddComponents any BaseUnityPlugin it finds), and ScriptEngine hot reload (scripts
    /// folder — OnDestroy performs full cleanup so reloads never double-patch).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class UIAscendedPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.florpydorp.stationeers.uiascended";
        public const string PluginName = "Stationeers UI Ascended";
        public const string PluginVersion = "0.2.0"; // BepInEx needs System.Version format
        public const string VersionDisplay = "0.2.0 Alpha";

        public static UIAscendedPlugin Instance { get; private set; }

        private Harmony _harmony;
        private RadialController _radials;
        private ToolRadialFeature _toolRadial;
        private BagRadialFeature _bagRadial;
        private HudOverlayFeature _hud;
        private EmergencyInjectFeature _emergencyInject;
        private SettingsWindow _settingsWindow;
        private ProfileEditorWindow _profileEditor;
        private readonly System.Collections.Generic.List<EquipmentKeyRadialFeature> _equipFeatures
            = new System.Collections.Generic.List<EquipmentKeyRadialFeature>();
        private int _drawExceptions;

        private void Awake()
        {
            if (Instance != null)
            {
                // Loaded twice (e.g. plugins folder AND SLP mod folder) — stay inert.
                Logger.LogWarning("Second UIAscendedPlugin instance detected; this one stays inactive. " +
                                  "Install the mod either as a BepInEx plugin OR an SLP folder mod, not both.");
                return;
            }
            Instance = this;
            UIALog.Init(Logger);

            try
            {
                UIAConfig.Bind(Config);

                if (GameManager.IsBatchMode)
                {
                    UIALog.Info("Dedicated server detected — Stationeers UI Ascended is client-side only; nothing will load.");
                    return;
                }

                BagProfileStore.LoadProfiles();

                _toolRadial = new ToolRadialFeature();
                _bagRadial = new BagRadialFeature();
                _radials = new RadialController();
                _radials.Register(new ToolbeltRadialFeature());
                _radials.Register(_toolRadial);
                _radials.Register(_bagRadial);
                _equipFeatures.Clear();
                foreach (var equipFeature in EquipmentKeyRadialFeature.CreateAll())
                {
                    _equipFeatures.Add(equipFeature);
                    _radials.Register(equipFeature);
                }
                _hud = new HudOverlayFeature();
                _emergencyInject = new EmergencyInjectFeature();

                _harmony = new Harmony(PluginGuid);
                PatchHarness.TryPatchAll(_harmony,
                    typeof(Patch_ImGuiWindowManager_Draw),
                    typeof(Patch_InventoryManager_SmartStow),
                    typeof(Patch_InventoryManager_CheckDisplaySlot),
                    typeof(Patch_KeyManager_ToggleScoreboard));

                BoosterBridge.TryRegister("StationeersUIAscended", PluginVersion);

                UIALog.Info($"{PluginName} v{VersionDisplay} initialized. " +
                            $"Hold {UIAConfig.ToolbeltRadialKey.Value} for the toolbelt radial, " +
                            $"{UIAConfig.SettingsWindowKey.Value} for settings.");
                // Visible in the in-game F3 console, so ScriptEngine hot reloads are obvious.
                try
                {
                    ConsoleWindow.Print($"[UI Ascended] v{VersionDisplay} loaded ({System.DateTime.Now:HH:mm:ss}). " +
                                        "F6 = hot reload (ScriptEngine).", System.ConsoleColor.Cyan);
                }
                catch { }
            }
            catch (Exception e)
            {
                UIALog.Error("Initialization failed: " + e);
            }
        }

        private void Update()
        {
            if (Instance != this || _radials == null) return;
            if (GameManager.IsBatchMode) return;

            try
            {
                _radials.Update();
                _emergencyInject?.Update();

                // Settings window toggle (not while a radial owns the screen)
                if (Input.GetKeyDown(UIAConfig.SettingsWindowKey.Value) && Guards.CanDraw()
                    && !_radials.IsRadialOpen)
                    ToggleSettingsWindow();
            }
            catch (Exception e)
            {
                UIALog.Error("Update failed: " + e);
            }
        }

        /// <summary>Called from the ImGuiWindowManager.Draw postfix — inside the game's ImGui frame.</summary>
        public void DrawOverlay()
        {
            if (Instance != this || _radials == null) return;
            if (GameManager.IsBatchMode || Assets.Scripts.UI.ImGuiLoadingScreen.IsShowing) return;

            Localization.PushFont();
            try
            {
                // Toast draws even without a living player (death/incapacitated feedback).
                Overlay.Toast.Draw();

                if (!Guards.CanDraw())
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll();
                    return;
                }
                _hud.Draw();
                _radials.Draw();
            }
            finally
            {
                Localization.PopFont();
            }
        }

        public void ReportDrawException(Exception e)
        {
            if (++_drawExceptions <= 3)
                UIALog.Error("Overlay draw failed: " + e);
            if (_drawExceptions == 3)
                UIALog.Error("Further overlay draw errors suppressed.");
        }

        /// <summary>
        /// Vanilla-key suppression must only happen while WE can actually handle the key —
        /// i.e. a radial is open, or gameplay input is ours to take. Otherwise the vanilla
        /// handler must stay alive: e.g. the scoreboard is a cursor-unlocking modal, and once
        /// it is open our controller (correctly) ignores Tab — blocking vanilla too would
        /// leave the scoreboard stuck open with all input dead.
        /// </summary>
        private bool CanHandleSuppressedKeys =>
            _radials != null && (_radials.IsRadialOpen || Guards.CanAcceptGameplayInput());

        public bool ToolRadialOwnsVanillaKey =>
            UIAConfig.MasterEnable.Value && Assets.Scripts.Inventory.InventoryManager.ShowUi
            && _toolRadial != null && _toolRadial.OwnsVanillaKey
            && CanHandleSuppressedKeys;

        public bool BagRadialOwnsVanillaKey =>
            UIAConfig.MasterEnable.Value && Assets.Scripts.Inventory.InventoryManager.ShowUi
            && _bagRadial != null && _bagRadial.OwnsVanillaKey
            && CanHandleSuppressedKeys;

        /// <summary>
        /// True when the equipment-key radials own the given vanilla slot button RIGHT NOW.
        /// Falls back to vanilla when: the mod/HUD is disabled, the key collides with another
        /// radial key (which would shadow us), or we have no possible action for the current
        /// state (empty slot + nothing fitting in hand) — so vanilla behavior is never simply
        /// eaten without a replacement.
        /// </summary>
        public bool EquipmentKeysOwnButton(string buttonName)
        {
            if (!UIAConfig.MasterEnable.Value || !UIAConfig.EquipmentKeyRadialsEnabled.Value) return false;
            if (!Assets.Scripts.Inventory.InventoryManager.ShowUi) return false;
            if (!CanHandleSuppressedKeys) return false;
            foreach (var feature in _equipFeatures)
            {
                if (feature.ButtonName != buttonName) continue;
                var key = feature.Key;
                if (key == KeyCode.None) return false;
                if (key == UIAConfig.ToolbeltRadialKey.Value || key == UIAConfig.ToolRadialKey.Value
                    || key == UIAConfig.BagRadialKey.Value || key == UIAConfig.SettingsWindowKey.Value)
                    return false; // collision: the other feature wins the key, leave vanilla alive
                return feature.CanAct();
            }
            return false;
        }

        public void ToggleSettingsWindow()
        {
            if (_settingsWindow == null) _settingsWindow = new SettingsWindow();
            if (_settingsWindow.IsShowing) ImGuiWindowManager.Close(_settingsWindow);
            else ImGuiWindowManager.Open(_settingsWindow);
        }

        public void ToggleProfileEditor()
        {
            if (_profileEditor == null) _profileEditor = new ProfileEditorWindow();
            if (_profileEditor.IsShowing) ImGuiWindowManager.Close(_profileEditor);
            else ImGuiWindowManager.Open(_profileEditor);
        }

        /// <summary>Full cleanup: required for ScriptEngine hot reload (and polite everywhere else).</summary>
        private void OnDestroy()
        {
            if (Instance != this) return;
            try
            {
                _radials?.ShutdownImmediate();
                if (_settingsWindow != null && _settingsWindow.IsShowing) ImGuiWindowManager.Close(_settingsWindow);
                if (_profileEditor != null && _profileEditor.IsShowing) ImGuiWindowManager.Close(_profileEditor);
                _hud?.RestoreVanillaIfNeeded();
                BagProfileStore.SaveAssignments();
                IconCache.Clear();
                _harmony?.UnpatchSelf();
                UIALog.Info("Cleaned up (hot reload safe).");
                try
                {
                    ConsoleWindow.Print($"[UI Ascended] v{VersionDisplay} unloading ({System.DateTime.Now:HH:mm:ss}) - hot reload in progress...",
                        System.ConsoleColor.Yellow);
                }
                catch { }
            }
            catch (Exception e)
            {
                UIALog.Error("Cleanup failed: " + e);
            }
            finally
            {
                Instance = null;
            }
        }
    }
}
