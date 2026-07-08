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
    /// Stationeers UI Ascended (SSUI). Loads three ways with identical behavior:
    /// BepInEx chainloader (plugins folder), StationeersLaunchPad (folder mod — SLP
    /// AddComponents any BaseUnityPlugin it finds), and ScriptEngine hot reload (scripts
    /// folder — OnDestroy performs full cleanup so reloads never double-patch).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class UIAscendedPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.florpydorp.stationeers.uiascended";
        public const string PluginName = "Stationeers UI Ascended";
        public const string PluginVersion = "0.1.0";

        public static UIAscendedPlugin Instance { get; private set; }

        private Harmony _harmony;
        private RadialController _radials;
        private ToolRadialFeature _toolRadial;
        private BagRadialFeature _bagRadial;
        private HudOverlayFeature _hud;
        private SettingsWindow _settingsWindow;
        private ProfileEditorWindow _profileEditor;
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
                    UIALog.Info("Dedicated server detected — SSUI is client-side only; nothing will load.");
                    return;
                }

                BagProfileStore.LoadProfiles();

                _toolRadial = new ToolRadialFeature();
                _bagRadial = new BagRadialFeature();
                _radials = new RadialController();
                _radials.Register(new ToolbeltRadialFeature());
                _radials.Register(_toolRadial);
                _radials.Register(_bagRadial);
                _hud = new HudOverlayFeature();

                _harmony = new Harmony(PluginGuid);
                PatchHarness.TryPatchAll(_harmony,
                    typeof(Patch_ImGuiWindowManager_Draw),
                    typeof(Patch_InventoryManager_SmartStow),
                    typeof(Patch_InventoryManager_CheckDisplaySlot),
                    typeof(Patch_KeyManager_ToggleScoreboard));

                BoosterBridge.TryRegister("StationeersUIAscended", PluginVersion);

                UIALog.Info($"{PluginName} v{PluginVersion} initialized. " +
                            $"Hold {UIAConfig.ToolbeltRadialKey.Value} for the toolbelt radial, " +
                            $"{UIAConfig.SettingsWindowKey.Value} for settings.");
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

                // Settings window toggle (works in menus too, as long as a world is loaded)
                if (Input.GetKeyDown(UIAConfig.SettingsWindowKey.Value) && Guards.CanDraw())
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
            if (!Guards.CanDraw())
            {
                if (_radials.IsRadialOpen) _radials.CloseAll();
                return;
            }
            Localization.PushFont();
            try
            {
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

        public bool ToolRadialOwnsVanillaKey =>
            UIAConfig.MasterEnable.Value && _toolRadial != null && _toolRadial.OwnsVanillaKey;

        public bool BagRadialOwnsVanillaKey =>
            UIAConfig.MasterEnable.Value && _bagRadial != null && _bagRadial.OwnsVanillaKey;

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
                _radials?.CloseAll();
                if (_settingsWindow != null && _settingsWindow.IsShowing) ImGuiWindowManager.Close(_settingsWindow);
                if (_profileEditor != null && _profileEditor.IsShowing) ImGuiWindowManager.Close(_profileEditor);
                _hud?.RestoreVanillaIfNeeded();
                BagProfileStore.SaveAssignments();
                IconCache.Clear();
                _harmony?.UnpatchSelf();
                UIALog.Info("Cleaned up (hot reload safe).");
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
