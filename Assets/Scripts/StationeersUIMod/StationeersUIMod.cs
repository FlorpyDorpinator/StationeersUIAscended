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
        public const string ModVersion = "0.9.0.1";
        public const string VersionDisplay = "0.9.0.1 Experimental";
        public const string ModGuid = "com.stationeersuimod.ui";

        public static StationeersUIMod Instance { get; private set; }

        public static readonly Mod MOD = new Mod("StationeersUIMod", ModVersion);

        private static bool _loaded;
        private Harmony _harmony;
        private RadialController _radials;
        private ToolRadialFeature _toolRadial;
        private BagRadialFeature _bagRadial;
        private HudOverlayFeature _hud;
        private SettingsWindow _settingsWindow;
        private ProfileEditorWindow _profileEditor;
        private HudEditorWindow _hudEditorWindow;
        private readonly List<EquipmentKeyRadialFeature> _equipFeatures
            = new List<EquipmentKeyRadialFeature>();
        private int _drawExceptions;

        /// <summary>The mod's install folder under SLP (local or Workshop), from the injected
        /// ModData. NULL under the F6 ScriptEngine dev flow (no mod folder — the dev shim passes
        /// no ModData); consumers (HudShaderStore) must fall back to a config-supplied path.</summary>
        public static string ModDirectory { get; private set; }

        /// <summary>
        /// SLP / LaunchPadBooster entry point. prefabs may be null or empty for pure UI mods.
        /// config is the BepInEx-backed config that SLP renders in its mod panel. modData is
        /// SLP's DefaultEntrypoint injection (parameter-type matched; optional so the dev shim's
        /// two-arg call still compiles) — it carries DirectoryPath, our bundle root.
        /// </summary>
        public void OnLoaded(List<GameObject> prefabs, ConfigFile config, ModData modData = null)
        {
            if (_loaded)
            {
                Debug.LogWarning("[StationeersUIMod] OnLoaded called twice; ignoring.");
                return;
            }
            _loaded = true;

            Instance = this;
            try { ModDirectory = modData != null ? modData.DirectoryPath : null; } catch { ModDirectory = null; }

#if DEVELOPMENT_BUILD
            Debug.Log("[StationeersUIMod] DEVELOPMENT BUILD");
#endif

            try
            {
                // Initialize logging (BepInEx logger optional)
                // If we were loaded as BepInEx plugin too, the old Awake may have run; guard below.
                UIALog.Info("OnLoaded entry for StationeersUIMod.");

                // One-time config migration: pre-0.9.0 packages shipped the DEV shim, so all user
                // settings live in its cfg. Now that SLP's DefaultEntrypoint owns init, the cfg
                // name changed — seed the new file from the legacy one so nobody loses settings.
                // (Under the dev shim both paths are the same file and this no-ops.)
                try
                {
                    string newCfg = config.ConfigFilePath;
                    string legacyCfg = System.IO.Path.Combine(
                        BepInEx.Paths.ConfigPath, "com.stationeersuimod.ui.scriptengine.cfg");
                    if (!string.Equals(newCfg, legacyCfg, StringComparison.OrdinalIgnoreCase)
                        && System.IO.File.Exists(legacyCfg)
                        && (!System.IO.File.Exists(newCfg) || new System.IO.FileInfo(newCfg).Length == 0))
                    {
                        System.IO.File.Copy(legacyCfg, newCfg, true);
                        config.Reload();
                        UIALog.Info("Migrated settings from the legacy dev-shim cfg to " + System.IO.Path.GetFileName(newCfg) + ".");
                    }
                }
                catch (Exception mig) { UIALog.Warn("Config migration skipped: " + mig.Message); }

                UIAConfig.Bind(config);
                Core.UiaKeybinds.EnsureBuilt();

                if (GameManager.IsBatchMode)
                {
                    UIALog.Info("Dedicated server detected — Stationeers UI Mod is client-side only; nothing will load.");
                    return;
                }

                // Profiler (vendored Profilicus, Jackson's — permission on record 2026-07-13).
                // Runtime-off by default: hidden = one static bool per probe, zero alloc.
                // (folder arg = the LEAF under BepInEx/config/StationeersUIMod/ — passing the mod
                // name here doubled the path to .../StationeersUIMod/StationeersUIMod; play-test)
                Profiling.ProfilicusUniversalis.Configure("UIA Profiler", "ProfilerSnapshots");

                BagProfileStore.LoadProfiles();
                // Shipped HUD profiles (zip: StationeersUIMod/HudProfiles/) land in config on
                // first run — required for the shipped default ("Smaller Test") to exist on a
                // fresh install. No-overwrite, fail-soft; inert under F6 (ModDirectory null).
                Features.HudProfileStore.ImportShipped(ModDirectory);

                // Register our single-key actions in the game's native Controls screen. The
                // SetupKeyBindings postfix also does this at game startup; doing it here as well
                // re-hooks the OnControlsChanged sync after an F6 hot-reload (idempotent — it skips
                // keys already registered and only re-subscribes if not already hooked).
                Core.UiaKeybinds.RegisterWithGame();

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

                _harmony = new Harmony(ModGuid);
                PatchHarness.TryPatchAll(_harmony,
                    typeof(Patch_ImGuiWindowManager_Draw),
                    typeof(Patch_InventoryManager_SmartStow),
                    typeof(Patch_InventoryManager_CheckDisplaySlot),
                    typeof(Patch_KeyManager_ToggleScoreboard),
                    typeof(Patch_Human_SpawnDynamicThing),
                    typeof(Patch_KeyManager_SpawnDynamicThing),
                    typeof(Patch_InventoryManager_CheckDisplaySlotInput),
                    typeof(Patch_InventoryManager_AllowMouseControl),
                    typeof(Patch_MovementController_HandleJump),
                    typeof(Patch_PlayerStateWindow_UpdateJetpackPanels),
                    typeof(Patch_ThingRenderer_OverrideShadowMode), // names + silences the vanilla shadow-LOD NRE
                    typeof(Core.Patch_CommandLine_Process), // `finddead` console command
                    typeof(Core.Patch_KeyManager_SetupKeyBindings)); // native Controls-screen rows for our keys

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
                // The radial editor lives and dies with the F10 window (Escape closes the
                // window through the game's own manager; we follow).
                if (Windows.RadialEditorMode.Active
                    && (_settingsWindow == null || !_settingsWindow.IsShowing))
                    Windows.RadialEditorMode.Exit();

                // Same contract for the F9 HUD editor; the two editors never share the
                // screen (the radial editor's black backdrop would hide the HUD anyway).
                if (Windows.HudEditorMode.Active
                    && (_hudEditorWindow == null || !_hudEditorWindow.IsShowing))
                    Windows.HudEditorMode.Exit();
                if (Windows.RadialEditorMode.Active && Windows.HudEditorMode.Active)
                    ToggleHudEditor();

                // The ImGui draw hook stops firing over the loading screen / main menu, so
                // anything the hook would hide must be hidden HERE — Update keeps running.
                // Without this, DontDestroyOnLoad radial/parking canvases freeze on screen
                // when the world ends while a radial is open. _radials.Update() still runs
                // below: it pumps the deferred modal release and is guard-aware itself.
                if (!Guards.CanDraw())
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll();
                    if (Windows.RadialEditorMode.Active) Windows.RadialEditorMode.Exit();
                    // The F9 editor's dim backdrop must never survive onto the loading
                    // screen / main menu; close its window through the game's manager.
                    if (Windows.HudEditorMode.Active)
                    {
                        if (_hudEditorWindow != null && _hudEditorWindow.IsShowing)
                            ImGuiWindowManager.Close(_hudEditorWindow);
                        Windows.HudEditorMode.Exit();
                    }
                    UI.UnityRadialView.Hide();
                    UI.ParkedItemsView.Hide();
                    UI.SearchPanelView.Hide();
                }

                // The RADIAL half has its own master switch (independent of the HUD half). When
                // it is off we behave exactly like the loading-screen stand-down: close any open
                // radial, hide the DontDestroyOnLoad canvases, and skip the controller so its keys
                // fall back to vanilla (the ownership props below already yield when off).
                if (UIAConfig.RadialEnabled.Value)
                {
                    _radials.Update();

                    // #4: fire wedge-bound hotkeys only during real gameplay — no radial open
                    // (that's binding/using-the-wheel time) and gameplay input accepted (so a
                    // bound letter can't fire into a text field / open menu).
                    if (!_radials.IsRadialOpen && Guards.CanAcceptGameplayInput())
                        Core.WedgeHotkeys.TickExecute();

                    UI.RadialHintBar.Tick(_radials.IsRadialOpen);
                }
                else
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll();
                    if (Windows.RadialEditorMode.Active) Windows.RadialEditorMode.Exit();
                    UI.UnityRadialView.Hide();
                    UI.ParkedItemsView.Hide();
                    UI.SearchPanelView.Hide();
                    UI.RadialHintBar.Tick(false);
                }

                // Frame.Total baseline: recorded from Update (NOT the ImGui hook — F1-hiding
                // ImGui must not stop A/B captures; adversarial review 2026-07-13). Every
                // effect's cost is judged as a delta against this row.
                if (Profiling.ProfilicusUniversalis.Enabled)
                    Profiling.ProfilicusUniversalis.Record("Frame.Total", Time.unscaledDeltaTime * 1000.0);
                // GC telemetry (Dean Hall's critique of timing-only profilers: on old mono,
                // allocations ARE the performance story): alloc KB/frame + gen0 collections/s.
                Profiling.UiaGcMonitor.Tick();

                // The visor HUD is pure UGUI — it runs off Update, not the ImGui hook
                // (which stops over loading screens; Update keeps running and hides it).
                using (Profiling.ProfilicusUniversalis.Time("Hud.Update.Total"))
                    UI.Hud.HudSystem.Update(Windows.HudEditorMode.Active);
                if (Windows.HudEditorMode.Active) Windows.HudEditorMode.Update();

                Core.UiaAbDriver.Tick(); // A/B capture state machine (inert unless a run is active)

                // The UGUI Control Center (F10) is the player-facing front door. Pump it every
                // frame (it owns its own Escape/close and rebind capture) and toggle on the key.
                UI.Menu.UiaControlCenter.Update();

                // First run: show the how-to guide once, the moment we are safely in-game.
                if (!UIAConfig.GuideShown.Value && Guards.CanToggleMenus()
                    && !UI.Menu.UiaControlCenter.IsOpen && !_radials.IsRadialOpen)
                {
                    UIAConfig.GuideShown.Value = true;
                    UI.Menu.UiaControlCenter.OpenGuide();
                }

                if (Input.GetKeyDown(UIAConfig.SettingsWindowKey.Value) && Guards.CanToggleMenus()
                    && !_radials.IsRadialOpen && !Windows.HudEditorMode.Active)
                    UI.Menu.UiaControlCenter.Toggle();

                if (Input.GetKeyDown(UI.Hud.HudConfig.HudEditorKey.Value) && Guards.CanToggleMenus()
                    && !_radials.IsRadialOpen && !Windows.RadialEditorMode.Active
                    && !UI.Menu.UiaControlCenter.IsOpen)
                    ToggleHudEditor();
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

                // Profiler window rides the game's ImGui frame (visible = user toggled it on
                // via F9's Profiler section or `uiaprof on`). Draw() is a no-op when hidden.
                Profiling.ProfilicusUniversalis.Draw();

                if (!Guards.CanDraw())
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll();
                    if (Windows.RadialEditorMode.Active) Windows.RadialEditorMode.Exit();
                    return;
                }

                // Editor mode owns the screen: black backdrop + example radials.
                if (Windows.RadialEditorMode.Active)
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll();
                    Windows.RadialEditorMode.Draw();
                    return;
                }

                // The click-to-edit popup rides the game's ImGui frame.
                Windows.HudEditorWindow.DrawPopupOverlay();

                // VisorHudEnabled is the HUD half's master switch: OFF now means NO HUD at all
                // (vanilla restored), not "fall back to the legacy ImGui HUD". The legacy ImGui
                // overlay is a diagnostics-only escape hatch that draws solely when the HUD half
                // is ON and the player explicitly opted into the legacy renderer. The UGUI visor
                // HUD itself draws from Update (HudSystem stands down when the master is off).
                if (UI.Hud.HudConfig.VisorHudEnabled.Value && UI.Hud.HudConfig.LegacyImGuiHud.Value)
                    _hud.Draw();
                else
                    _hud.RestoreVanillaIfNeeded();

                if (UIAConfig.RadialEnabled.Value)
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
            UIAConfig.MasterEnable.Value && UIAConfig.RadialEnabled.Value
            && Assets.Scripts.Inventory.InventoryManager.ShowUi
            && _toolRadial != null && _toolRadial.OwnsVanillaKey
            && CanHandleSuppressedKeys;

        public bool BagRadialOwnsVanillaKey =>
            UIAConfig.MasterEnable.Value && UIAConfig.RadialEnabled.Value
            && Assets.Scripts.Inventory.InventoryManager.ShowUi
            && _bagRadial != null && _bagRadial.OwnsVanillaKey
            && CanHandleSuppressedKeys;

        public bool EquipmentKeysOwnButton(string buttonName)
        {
            if (!UIAConfig.MasterEnable.Value || !UIAConfig.RadialEnabled.Value
                || !UIAConfig.EquipmentKeyRadialsEnabled.Value) return false;
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

        public void ToggleHudEditor()
        {
            if (_hudEditorWindow == null) _hudEditorWindow = new HudEditorWindow();
            if (_hudEditorWindow.IsShowing)
            {
                ImGuiWindowManager.Close(_hudEditorWindow);
                Windows.HudEditorMode.Exit();
            }
            else
            {
                if (Windows.RadialEditorMode.Active) Windows.RadialEditorMode.Exit();
                ImGuiWindowManager.Open(_hudEditorWindow);
                Windows.HudEditorMode.Enter();
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            try
            {
                _radials?.ShutdownImmediate();
                Core.WedgeHotkeys.Clear();
                Core.UiaAbDriver.Reset();
                // Profiler: hide + drop the sample window before unpatching (its Draw rides our
                // ImGui postfix, which UnpatchSelf removes). Statics hold no Unity objects.
                Profiling.ProfilicusUniversalis.SetVisible(false);
                Profiling.ProfilicusUniversalis.Clear();
                Profiling.UiaGcMonitor.Reset();
                if (_settingsWindow != null && _settingsWindow.IsShowing) ImGuiWindowManager.Close(_settingsWindow);
                if (_profileEditor != null && _profileEditor.IsShowing) ImGuiWindowManager.Close(_profileEditor);
                if (_hudEditorWindow != null && _hudEditorWindow.IsShowing) ImGuiWindowManager.Close(_hudEditorWindow);
                Windows.HudEditorMode.Shutdown();
                UI.Menu.UiaControlCenter.Shutdown();
                UI.Menu.Kit.UiaRebindCapture.Reset();
                UI.RadialHintBar.Shutdown();
                Core.UiaKeybinds.Unhook();
                UI.Hud.HudSystem.Shutdown();
                _hud?.RestoreVanillaIfNeeded();
                BagProfileStore.SaveAssignments();
                IconCache.Clear();
                HudIconStore.Shutdown();
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
