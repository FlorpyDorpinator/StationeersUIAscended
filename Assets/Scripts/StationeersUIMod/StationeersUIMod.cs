using System;
using System.Collections.Generic;
using Assets.Scripts;
using BepInEx.Configuration;
using HarmonyLib;
using LaunchPadBooster;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.Windows;
using UI.ImGuiUi.ImGuiWindows;
using UnityEngine;

namespace StationeersUIMod
{
    /// <summary>
    /// Stationeers UI Mod. Proper StationeersLaunchPad / Unity mod.
    /// Entry via OnLoaded (LaunchPadBooster). ImGui hooks via Harmony patch on ImGuiWindowManager.Draw.
    /// </summary>
    public sealed class StationeersUIMod : MonoBehaviour
    {
        public const string ModVersion = "0.2.0";
        public const string VersionDisplay = "0.2.0 Alpha";
        public const string ModGuid = "com.stationeersuimod.ui";

        public static StationeersUIMod Instance { get; private set; }

        public static readonly Mod MOD = new Mod("StationeersUIMod", ModVersion);

        private static bool _loaded;
        private Harmony _harmony;
        private RadialController _radials;
        private ToolRadialFeature _toolRadial;
        private BagRadialFeature _bagRadial;
        private HudOverlayFeature _hud;
        private EmergencyInjectFeature _emergencyInject;
        private SettingsWindow _settingsWindow;
        private ProfileEditorWindow _profileEditor;
        private readonly List<EquipmentKeyRadialFeature> _equipFeatures
            = new List<EquipmentKeyRadialFeature>();
        private int _drawExceptions;

        /// <summary>
        /// SLP / LaunchPadBooster entry point. prefabs may be null or empty for pure UI mods.
        /// config is the BepInEx-backed config that SLP renders in its mod panel.
        /// </summary>
        public void OnLoaded(List<GameObject> prefabs, ConfigFile config)
        {
            if (_loaded)
            {
                Debug.LogWarning("[StationeersUIMod] OnLoaded called twice; ignoring.");
                return;
            }
            _loaded = true;

            Instance = this;

#if DEVELOPMENT_BUILD
            Debug.Log("[StationeersUIMod] DEVELOPMENT BUILD");
#endif

            try
            {
                // Initialize logging (BepInEx logger optional)
                // If we were loaded as BepInEx plugin too, the old Awake may have run; guard below.
                UIALog.Info("OnLoaded entry for StationeersUIMod.");

                UIAConfig.Bind(config);

                if (GameManager.IsBatchMode)
                {
                    UIALog.Info("Dedicated server detected — Stationeers UI Mod is client-side only; nothing will load.");
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

                _harmony = new Harmony(ModGuid);
                PatchHarness.TryPatchAll(_harmony,
                    typeof(Patch_ImGuiWindowManager_Draw),
                    typeof(Patch_InventoryManager_SmartStow),
                    typeof(Patch_InventoryManager_CheckDisplaySlot),
                    typeof(Patch_KeyManager_ToggleScoreboard));

                // The static `new Mod(...)` above registers us with LaunchPadBooster for the optional client-side mod list.
                // (Proper direct reference - no reflection hack.)

                UIALog.Info($"{VersionDisplay} initialized. " +
                            $"Hold {UIAConfig.ToolbeltRadialKey.Value} for the toolbelt radial, " +
                            $"{UIAConfig.SettingsWindowKey.Value} for settings.");

                try
                {
                    ConsoleWindow.Print($"[StationeersUIMod] v{VersionDisplay} loaded ({DateTime.Now:HH:mm:ss}).", System.ConsoleColor.Cyan);
                }
                catch { }
            }
            catch (Exception e)
            {
                UIALog.Error("Initialization failed: " + e);
            }
        }

        private void Awake()
        {
            // SLP loads the component and calls OnLoaded. This is here for safety / hot reload scenarios.
            if (Instance != null && Instance != this) return;
        }

        private void Update()
        {
            if (Instance != this || _radials == null) return;
            if (GameManager.IsBatchMode) return;

            try
            {
                _radials.Update();
                _emergencyInject?.Update();

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
                    return false;
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
                    ConsoleWindow.Print($"[StationeersUIMod] v{VersionDisplay} unloading ({DateTime.Now:HH:mm:ss})", System.ConsoleColor.Yellow);
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
                _loaded = false;
            }
        }
    }
}
