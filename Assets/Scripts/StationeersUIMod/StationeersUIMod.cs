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
        public const string ModVersion = "0.9.7.3";
        public const string VersionDisplay = "0.9.7.3 Experimental";
        public const string ModGuid = "com.stationeersuimod.ui";

        public static StationeersUIMod Instance { get; private set; }

        public static readonly Mod MOD = new Mod("StationeersUIMod", ModVersion);

        private static bool _loaded;
        private Harmony _harmony;
        private RadialController _radials;
        private ToolRadialFeature _toolRadial;
        private BagRadialFeature _bagRadial;
        private SettingsWindow _settingsWindow;
        private ProfileEditorWindow _profileEditor;
        private HudEditorWindow _hudEditorWindow;
        private readonly List<EquipmentKeyRadialFeature> _equipFeatures
            = new List<EquipmentKeyRadialFeature>();
        private int _drawExceptions;
        private float _drawExcLogAt = -999f;

        // ---- Per-frame exception circuit breaker for Update() ----------------------------------
        // A throwing Update() used to format + WRITE a full stack trace every single frame — a
        // per-frame disk write that is itself an FPS-collapse mechanism (the reported "cursor froze,
        // even other apps lagged"). These rate-limit the log (first few in full, then once / 5s) and
        // trip a breaker after a RUN of consecutive failures, so a permanently-throwing frame stops
        // burning CPU/disk and instead retries after a short cooldown. All per-instance — they die
        // with the instance, so a hot reload starts fresh with no teardown reset needed.
        private int _updateExc;             // total Update() throws since load
        private int _updateExcStreak;       // consecutive throwing frames (0 after any clean frame)
        private float _updateExcLogAt = -999f;
        private float _updateBreakerUntil;  // unscaledTime until which the Update body is skipped
        private bool _updateBreakerTripped; // announced-once latch for a broken stretch; cleared only by a clean frame (NOT by the per-retry cooldown re-arm), so the trip banner logs exactly once per stretch

        // Per-instance teardown latch: a second OnDestroy on THIS instance is a clean no-op. This is
        // NOT the old cross-instance "Instance != this" guard (that used to skip teardown of the very
        // object being destroyed and leak its Harmony patches + canvases on every reload).
        private bool _torndown;

        // The Grid (full-inventory glass overlay) hold-to-peek / tap-to-toggle bookkeeping.
        private const float GridTapSeconds = 0.25f;
        private float _gridKeyDownTime;
        private bool _gridPeeking;
        // Set when THIS press opened the Grid as an F9 edit preview, so the matching key-UP
        // (which in hold-to-peek mode would read as a tap-to-close) is swallowed instead of
        // shutting the window the user just opened to style. Reset in the cleanup path.
        private bool _gridEditPreviewOpened;
        // Same hazard, second instance: in hold-to-peek mode with the cursor ALREADY FREED (mouse
        // mod on), a Grid-key press opens the window LATCHED rather than as a peek, so _gridPeeking
        // is false — and the matching key-UP would then fall into the "already open + tap = close"
        // branch and shut the window on the very tap that opened it. Set here so that key-UP knows
        // this press was the opener and leaves the window latched. Reset each press + in cleanup.
        private bool _gridLatchedOpenedThisPress;

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

                // Fresh-install detection for ConfigMigration — must be read BEFORE the legacy copy
                // below and before UIAConfig.Bind, either of which creates the .cfg. Fresh = neither
                // the SLP cfg nor the legacy dev-shim cfg exists yet; such installs already carry the
                // current defaults, so migrations are skipped for them. SLP pre-creates the new cfg
                // (saveOnInit:true) BEFORE OnLoaded runs, so it always exists by the time we get here
                // — an existing-but-0-byte file is SLP's untouched stub, not a real prior install, so
                // it counts as absent (mirrors the zero-length check the legacy-copy block below uses).
                bool freshInstall;
                try
                {
                    string cfgPath = config.ConfigFilePath;
                    string legacyPath = System.IO.Path.Combine(
                        BepInEx.Paths.ConfigPath, "com.stationeersuimod.ui.scriptengine.cfg");
                    bool cfgExists = System.IO.File.Exists(cfgPath) && new System.IO.FileInfo(cfgPath).Length > 0;
                    freshInstall = !cfgExists && !System.IO.File.Exists(legacyPath);
                }
                catch { freshInstall = false; }

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
                // Config-schema migration: force corrected defaults / renamed keys onto EXISTING
                // players' .cfg where BepInEx would otherwise keep their stale stored value. Runs
                // after every setting is bound; no-op on a fresh install. See ConfigMigration.
                Core.ConfigMigration.Run(config, freshInstall);
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
                // O4e profile-aware sort: swap the game's SmartSortItems comparator for the
                // profile-aware wrapper (defers to vanilla per-compare when a bag has no profile;
                // restored in OnDestroy). Reflection field swap, fail-soft — see ProfileSort.
                Features.ProfileSort.Install();
                // Shipped HUD themes (zip: StationeersUIMod/HudProfiles/) are synced into config
                // each launch: seed if absent, refresh an untouched shipped theme we've updated, and
                // prune a retired shipped theme the player never edited — never touching the player's
                // own profiles. Fail-soft; inert under F6 (ModDirectory null). See SyncShipped.
                Features.HudProfileStore.SyncShipped(ModDirectory);

                // Register UIA_Menu in the game's native Controls screen (the only bind that gets a
                // vanilla row — see UiaKeybinds' class remarks). The SetupKeyBindings postfix also
                // does this at game startup; doing it here as well re-hooks the OnControlsChanged
                // sync after an F6 hot-reload (idempotent — it skips keys already registered and
                // only re-subscribes if not already hooked).
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

                _harmony = new Harmony(ModGuid);
                PatchHarness.TryPatchAll(_harmony,
                    typeof(Patch_ImGuiWindowManager_Draw),
                    typeof(Patch_InventoryManager_SmartStow),
                    typeof(Patch_InventoryManager_HiddenSlotMoveAnim),
                    typeof(Patch_InventoryManager_CheckDisplaySlot),
                    typeof(Patch_KeyManager_ToggleScoreboard),
                    typeof(Patch_Human_SpawnDynamicThing),
                    typeof(Patch_KeyManager_SpawnDynamicThing),
                    typeof(Patch_InventoryManager_CheckDisplaySlotInput),
                    // Suppress vanilla inventory-cursor nav (wheel + Next/Prev + select) while the
                    // Universal Inventory owns the wheel in the captured regime — no double-cursor.
                    typeof(Core.Patch_IWM_NextButton),
                    typeof(Core.Patch_IWM_PreviousButton),
                    typeof(Core.Patch_IWM_InventorySelect),
                    typeof(Patch_InventoryManager_AllowMouseControl),
                    typeof(Patch_MouseModeController_AltKeyDown), // double-tap cursor latch
                    typeof(Patch_MovementController_HandleJump),
                    typeof(Patch_MovementController_MovementHandler), // #9 jetpack toggle while a wheel is open
                    typeof(Patch_PlayerStateWindow_UpdateJetpackPanels),
                    typeof(Core.Patch_Human_GetStatsTooltip_Details),
                    typeof(Patch_ThingRenderer_OverrideShadowMode), // names + silences the vanilla shadow-LOD NRE
                    typeof(Core.Patch_CommandLine_Process), // `finddead` console command
                    typeof(Core.Patch_KeyManager_SetupKeyBindings), // native Controls-screen rows for our keys
                    // #9: auto-record a tool landing in the worn tool-belt so the grey ghost label
                    // and G-stow fly-home track manual swaps/drags/grid moves. Slot.Take is the
                    // single settle point; without this the binding table freezes at first seed.
                    typeof(BeltBindingStore.SlotTakePatch),
                    // Inbound world->visor-box drag. PRIVATE vanilla targets, so a rename in a game
                    // update degrades only this feature (that is what the harness is for).
                    typeof(Core.Patch_InputMouse_Drag),
                    typeof(Core.Patch_InputMouse_DragSlot),
                    // Cursor/drag diagnostics (`uiadiag`): the SetCursor funnel trace + modal-identity
                    // logging. Read-only; the trace itself is off unless the console command turns it on.
                    typeof(Core.Patch_CursorManager_SetCursor),
                    typeof(Core.Patch_MouseModeController_AddModal),
                    typeof(Core.Patch_MouseModeController_RemoveModal));

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

        // First-run tutorial gate: -1 = gate not currently satisfied; else the unscaled time the
        // gate FIRST passed. The coach only opens after the gate has held for the settle window.
        private float _firstRunGateSince = -1f;
        private const float FirstRunSettleSec = 1.0f;

        private void Update()
        {
            if (Instance != this || _radials == null) return;
            if (GameManager.IsBatchMode) return;

            // Circuit breaker: after a run of consecutive throwing frames, stand the body down for a
            // short cooldown rather than throw (and disk-write) every frame. Self-healing — once the
            // cooldown elapses we try again, and a single clean frame clears the streak below.
            if (_updateBreakerUntil > 0f)
            {
                if (Time.unscaledTime < _updateBreakerUntil) return;
                _updateBreakerUntil = 0f; // cooldown elapsed — attempt recovery this frame
            }

            // OUTSIDE the main try, deliberately: a throw earlier in the body must never starve
            // these two — the pause latch (a frozen world with nobody servicing the latch is
            // unrecoverable without a restart) and the Esc swallow (a stuck Typing state starves
            // every vanilla key). Both are internally fail-soft, so they cannot re-trip the breaker.
            Core.GamePause.Tick();
            Core.ModalInputChain.Pump();

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
                    if (_radials.IsRadialOpen) _radials.CloseAll("standdown-nocandraw-early");
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
                // Grid PIN shortcuts are read BEFORE the radial half on purpose. Shift+1..6 shares
                // its key-down frame with the 1..6 equipment radial keys, and RadialController arms
                // its pending press on GetKeyDown — so a claimed pin gesture must consume that one
                // frame, or releasing the key would ALSO open the equipment radial. Claiming skips
                // exactly one _radials.Update() (and the wedge hotkeys, which could otherwise fire a
                // bound number in the same frame); the key is held from here on, so no later frame
                // sees a GetKeyDown and nothing is stranded. The MOUSE half never claims — a click
                // is no radial key — so it cannot stall the controller.
                // Never while the tutorial coach or the Handbook viewer is modal: both leave the
                // cursor free, and the click-to-pin half hit-tests HUD zones directly (HudSystem.
                // ZoneAt), which the modal scrim cannot block — a click on the card could pin a
                // window underneath it.
                bool pinClaimed = UIAConfig.GridEnabled.Value
                    && !UI.Menu.Tutorial.TutorialCoach.IsOpen && !UI.Menu.HandbookViewer.IsOpen
                    && HandleGridPinShortcuts();

                if (UIAConfig.RadialEnabled.Value)
                {
                    if (!pinClaimed)
                    {
                        _radials.Update();

                        // #4: fire wedge-bound hotkeys only during real gameplay — no radial open
                        // (that's binding/using-the-wheel time) and gameplay input accepted (so a
                        // bound letter can't fire into a text field / open menu).
                        if (!_radials.IsRadialOpen && Guards.CanAcceptGameplayInput())
                            Core.WedgeHotkeys.TickExecute();
                    }

                    UI.RadialHintBar.Tick(_radials.IsRadialOpen);
                }
                else
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll("radial-half-disabled");
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

                // Double-tap the mouse modifier to latch the cursor up. Suppressed while a radial is
                // open: the wheel binds the SAME key for world-reach/Z-grab, so reaching twice there
                // must not silently latch the cursor.
                Core.CursorLatch.Tick(_radials != null && _radials.IsRadialOpen);
                Core.UiaAbDriver.Tick(); // A/B capture state machine (inert unless a run is active)

                // The UGUI Control Center (F10) is the player-facing front door. Pump it every
                // frame (it owns its own Escape/close and rebind capture) and toggle on the key.
                UI.Menu.UiaControlCenter.Update();
                UI.Menu.Tutorial.TutorialCoach.Update();
                UI.Menu.HandbookViewer.Update();

                // First run: open the tutorial coach once, the moment the player is safely in-game
                // WITH control. Stricter than CanToggleMenus alone: never over a pause or a vanilla
                // menu (an MP client's Esc menu doesn't pause, so CanToggleMenus can't see it), never
                // for an unresponsive body, and only after the gate has held for a short settle so it
                // can't pop on the world's fade-in frame.
                if (!UIAConfig.GuideShown.Value)
                {
                    bool firstRunGate = Guards.CanToggleMenus()
                        && !UI.Menu.UiaControlCenter.IsOpen && !_radials.IsRadialOpen
                        && !Windows.HudEditorMode.Active && !Windows.RadialEditorMode.Active
                        && !UI.Grid.TheGridPanel.IsOpen
                        && KeyManager.InputState == KeyInputState.Game
                        && !WorldManager.IsGamePaused && !Guards.VanillaMenuWantsFront()
                        && Assets.Scripts.Inventory.InventoryManager.ParentHuman != null
                        && !Assets.Scripts.Inventory.InventoryManager.ParentHuman.IsUnresponsive;
                    if (!firstRunGate) _firstRunGateSince = -1f;
                    else if (_firstRunGateSince < 0f) _firstRunGateSince = Time.unscaledTime;
                    else if (Time.unscaledTime - _firstRunGateSince >= FirstRunSettleSec)
                    {
                        UI.Menu.Tutorial.TutorialCoach.OpenFirstRun();
                        // Consume the one-shot only when the coach genuinely opened — a transient
                        // build failure must not burn the auto-tutorial forever.
                        if (UI.Menu.Tutorial.TutorialCoach.IsOpen) UIAConfig.GuideShown.Value = true;
                        else _firstRunGateSince = -1f;   // re-settle before the retry
                    }
                }

                // F10 opens even while the F9 HUD editor is active: the menu then becomes a
                // live-themed EDITABLE surface (click it in the editor to theme it). It refuses
                // only during a radial.
                if (Input.GetKeyDown(UIAConfig.SettingsWindowKey.Value) && Guards.CanToggleMenus()
                    && !_radials.IsRadialOpen
                    && !UI.Menu.Tutorial.TutorialCoach.IsOpen && !UI.Menu.HandbookViewer.IsOpen)
                    UI.Menu.UiaControlCenter.Toggle();

                if (Input.GetKeyDown(UI.Hud.HudConfig.HudEditorKey.Value) && Guards.CanToggleMenus()
                    && !_radials.IsRadialOpen && !Windows.RadialEditorMode.Active
                    && !UI.Menu.UiaControlCenter.IsOpen
                    && !UI.Menu.Tutorial.TutorialCoach.IsOpen && !UI.Menu.HandbookViewer.IsOpen)
                    ToggleHudEditor();

                // The Grid — its own master switch (independent of the radial/HUD halves).
                // Tap the key to toggle, hold it to peek (hide on release) when HoldToPeek is on.
                //
                // Tick() is pumped whether or not the main window is OPEN, because pinned windows
                // outlive it: the Grid key hides only the Universal Inventory, and the pins must keep
                // refreshing, dragging, resizing and moving after it is gone. Tick() runs the pin pump
                // (and the mouse-freed interactivity gate) BEFORE its own open check and both are
                // no-ops while nothing is pinned, so the closed-window cost is one bool test plus an
                // empty-list check. Standing down (world can't draw, or the master switch went off)
                // takes the pin WINDOWS with it — CloseAll keeps the pin RECORDS, so they come back on
                // the next open; only a pin's own X / shrink button truly unpins.
                if (UIAConfig.GridEnabled.Value && Guards.CanDraw())
                {
                    // The Grid key stays quiet while the tutorial coach / Handbook viewer is modal
                    // (raw Input reads would otherwise still fire; Tick keeps running so pinned
                    // windows stay alive behind the scrim).
                    if (!UI.Menu.Tutorial.TutorialCoach.IsOpen && !UI.Menu.HandbookViewer.IsOpen)
                        HandleGridInput();
                    UI.Grid.TheGridPanel.Tick();
                }
                else
                {
                    if (UI.Grid.TheGridPanel.IsOpen) UI.Grid.TheGridPanel.Hide();
                    if (UI.Grid.PinnedInventoryWindow.LiveCount > 0)
                        UI.Grid.PinnedInventoryWindow.CloseAll();
                    // A stand-down eats the key-UP of a hold-to-peek press; drop the latch so the next
                    // press is read fresh rather than as "peek already in progress".
                    _gridPeeking = false;
                    _gridEditPreviewOpened = false;
                    _gridLatchedOpenedThisPress = false;
                }

                // World-drag mirror: while VANILLA carries a world item OVER the Grid / a pinned window,
                // echo its thumbnail on the shared top layer (5250) so it draws above them instead of
                // vanishing behind. Ticked unconditionally so it always self-hides when the drag ends or
                // leaves those bounds; a no-op (one InputMouse read) when no world drag is live.
                Core.DragGhostLayer.TickWorldMirror();

                // Vanilla F2 helper-hints panel: keep it above every MOD canvas while playing, and
                // hand it back to vanilla's natural layering (under the Esc menu & co.) the moment a
                // vanilla full-attention menu is front. Cheap, dirty-guarded, fail-soft.
                Core.HelperHintsLift.Tick();

                // Cursor/drag trace (OFF unless `uiadiag` in the console): sampled LAST so it sees the
                // fully-resolved frame state, and writes a log line only when something changed.
                Core.CursorDiag.Sample();

                // A clean frame clears the failure streak, so the circuit breaker only ever trips on a
                // genuine RUN of consecutive throws, never on an isolated hiccup. It also re-arms the
                // trip announcement (see NoteUpdateException) so the NEXT broken stretch is reported once.
                _updateExcStreak = 0;
                _updateBreakerTripped = false;
            }
            catch (Exception e)
            {
                NoteUpdateException(e);
            }
        }

        /// <summary>Circuit-breaker sink for a throwing <see cref="Update"/> frame. Rate-limits the log
        /// (first 3 in full, then once / 5s) so a per-frame throw can never become a per-frame disk
        /// write, and trips a short stand-down after a RUN of consecutive failures so a permanently
        /// broken frame stops burning CPU. Self-heals: the breaker retries after its cooldown and one
        /// clean frame clears the streak.</summary>
        private void NoteUpdateException(Exception e)
        {
            _updateExc++;
            _updateExcStreak++;
            float now = Time.unscaledTime;

            if (_updateExc <= 3)
            {
                UIALog.Error("Update failed: " + e);
                _updateExcLogAt = now;
                if (_updateExc == 3)
                    UIALog.Error("Further Update errors will be rate-limited (once / 5s).");
            }
            else if (now - _updateExcLogAt >= 5f)
            {
                UIALog.Error($"Update still failing ({_updateExc} total): " + e.Message);
                _updateExcLogAt = now;
            }

            // A sustained run of throwing frames: stand the Update body down for a short cooldown
            // instead of hammering it (and the disk) every frame. The banner fires exactly ONCE per
            // broken stretch: _updateBreakerUntil is zeroed by the recovery gate every ~5s, so keying
            // firstTrip off it would re-log each retry — the _updateBreakerTripped latch (cleared only
            // by a clean frame) stays set through every cooldown re-arm and keeps the re-arm silent.
            if (_updateExcStreak >= 30)
            {
                bool firstTrip = !_updateBreakerTripped;
                _updateBreakerTripped = true;
                _updateBreakerUntil = now + 5f;
                if (firstTrip)
                    UIALog.Error("Update circuit breaker tripped (30 consecutive failures) — "
                        + "standing the per-frame body down for 5s, then retrying.");
            }
        }

        /// <summary>Read the Grid key: tap = toggle, hold = momentary peek (when HoldToPeek is on).
        /// Opening is gated by <see cref="Guards.CanAcceptGameplayInput"/> (a radial/menu that already
        /// freed the cursor refuses); closing/peek-release is always honoured so the panel can never
        /// strand. All state is reset in teardown via <see cref="UI.Grid.TheGridPanel.Shutdown"/>.
        ///
        /// <para>The Grid key does NOT free the mouse. The window opens with the cursor still locked
        /// and head-look still live, so you can glance at your inventory while playing; you click and
        /// drag in it only while holding the vanilla mouse-control key (<c>KeyMap.MouseControl</c>,
        /// default Alt), which is the only thing that unlocks the cursor. Nothing here registers a
        /// cursor modal, so no combination of open/closed windows can strand an unlocked cursor.</para>
        ///
        /// <para>ONE exception to the gameplay gate: while the F9 HUD editor is up the cursor is
        /// already free, so <c>CanAcceptGameplayInput</c> refuses and the key would do nothing
        /// silently — yet "open F9, press the Grid key, click the window to style it" is exactly the
        /// authoring flow (the F10 menu is reachable the same way). So an editor-active press opens
        /// the window as an EDIT PREVIEW instead: <see cref="UI.Grid.TheGridPanel.Show"/> sees the
        /// active editor and comes up non-interactive (raycaster off, no cursor modal), a live-themed
        /// surface the editor owns every click on. Gameplay behaviour is untouched.</para></summary>
        private void HandleGridInput()
        {
            var key = UIAConfig.GridKey.Value;
            if (key == KeyCode.None) return;

            bool holdPeek = UIAConfig.GridHoldPeek.Value;

            // The Grid key is a LETTER by default, and the F9 editor is full of ImGui text fields
            // (element name, colour hex, profile name). While one owns the keyboard the press is a
            // character, not a command: neither open nor close on it. (Same reasoning as the
            // editor's own delete/undo hotkeys, which check WantCaptureKeyboard.)
            if (Windows.HudEditorMode.Active && ImGuiOwnsKeyboard()) return;

            if (Input.GetKeyDown(key))
            {
                _gridKeyDownTime = Time.unscaledTime;
                _gridEditPreviewOpened = false;   // each press decides for itself
                _gridLatchedOpenedThisPress = false;
                if (!UI.Grid.TheGridPanel.IsOpen)
                {
                    if (GridEditPreviewAllowed())
                    {
                        // Editor preview: always LATCHED, never a peek. Peeking is a momentary
                        // gameplay glance whose release would hide the window mid-edit, and the
                        // latch is what the window falls back into once the editor closes.
                        _gridPeeking = false;
                        _gridEditPreviewOpened = true;
                        UI.Grid.TheGridPanel.ShowLatched();
                        return;
                    }
                    // Fresh open. Unlike the vanilla slot-hotkeys this may open with the mouse FREED
                    // (mouse mod active): CanOpenUniversalInventory drops ONLY the freed-cursor block,
                    // keeping every typing/paused/menu block, so B opens the grid whether the mouse is
                    // captured or out and about.
                    if (!Guards.CanOpenUniversalInventory()) return;
                    // A read-only PEEK is a captured-cursor gameplay glance; if the mouse is already
                    // freed the player wants a real interactive window, so latch instead of peeking.
                    if (holdPeek && !UnityEngine.Cursor.visible) { _gridPeeking = true; UI.Grid.TheGridPanel.ShowPeek(); }
                    else
                    {
                        // Latched open (either not in hold-peek mode, or the cursor was already
                        // freed). In hold-peek mode this press's key-UP must NOT be read as a
                        // tap-to-close, or B-with-mouse-mod-on opens then instantly shuts the window.
                        _gridLatchedOpenedThisPress = holdPeek;
                        UI.Grid.TheGridPanel.ShowLatched();
                    }
                }
                else
                {
                    // Already latched open. This press starts a possible tap-to-close.
                    _gridPeeking = false;
                    if (!holdPeek) UI.Grid.TheGridPanel.Toggle();
                }
            }

            if (holdPeek && Input.GetKeyUp(key))
            {
                // This release belongs to the press that just opened the edit preview. In hold-to-peek
                // mode it would otherwise read as "panel was already open + tap = close" and shut the
                // window on the same keystroke that opened it.
                if (_gridEditPreviewOpened) { _gridEditPreviewOpened = false; return; }

                bool tap = (Time.unscaledTime - _gridKeyDownTime) <= GridTapSeconds;
                if (_gridPeeking)
                {
                    // Opened by this press: a tap promotes the peek to the LATCHED state (it stays up
                    // on release); a hold was just a glance → hide on release. Neither state frees the
                    // cursor — the window is clickable only while the player holds the vanilla
                    // mouse-control key, so the Grid key never takes head-look away.
                    if (tap) { UI.Grid.TheGridPanel.Promote(); _gridPeeking = false; }
                    else { UI.Grid.TheGridPanel.Hide(); _gridPeeking = false; }
                }
                else if (tap)
                {
                    // This press OPENED the window latched (cursor already freed / not peek mode):
                    // its own release must leave the window up. Only a tap on an ALREADY-open window
                    // closes it — so swallow this one release and let the NEXT tap toggle it shut.
                    if (_gridLatchedOpenedThisPress) { _gridLatchedOpenedThisPress = false; return; }
                    // Panel was already open before this press; a tap closes it.
                    UI.Grid.TheGridPanel.Hide();
                }
            }
        }

        /// <summary>May this press open the Universal Inventory as an F9 EDIT PREVIEW? Only while the
        /// HUD editor is genuinely up and menus may be toggled at all (<see cref="Guards.CanToggleMenus"/>
        /// — never over the console, a vanilla input window or the creative spawn menu), and never
        /// during a radial, which owns the cursor. Deliberately does NOT consult
        /// <c>CanAcceptGameplayInput</c>: the editor freeing the cursor is exactly why that gate says no.
        /// Wrapped in a try so a missing/failed editor can never block the normal gameplay open.</summary>
        private bool GridEditPreviewAllowed()
        {
            try
            {
                if (!Windows.HudEditorMode.Active) return false;
                if (_radials != null && _radials.IsRadialOpen) return false;
                return Guards.CanToggleMenus();
            }
            catch { return false; }
        }

        /// <summary>Does an ImGui text field currently own the keyboard? ImGui does not block Unity's
        /// <c>Input</c>, so every letter hotkey must ask. Fails safe to "no" if ImGui is unavailable.</summary>
        private static bool ImGuiOwnsKeyboard()
        {
            try { return ImGuiNET.ImGui.GetIO().WantCaptureKeyboard; }
            catch { return false; }
        }

        // ---------- Grid PIN entry points (Shift+1..6, mouse-modifier + click a HUD equipment box) ----------

        /// <summary>How far (screen px) the pointer may travel between press and release and still
        /// count as a CLICK rather than a drag. Deliberately tiny — a real tear-out leaves the box
        /// almost immediately, while a hand-held click wobbles a pixel or two.</summary>
        private const float PinClickSlopPx = 5f;

        // The armed mouse-modifier press (see TryPinFromHudEquipmentClick). All three reset together
        // through ClearPinPress(), including from OnDestroy.
        private static Assets.Scripts.Objects.Slot _pinPressSlot;
        private static Vector2 _pinPressPos;
        private static bool _pinPressSawDrag;

        /// <summary>The two shortcuts that tear an equipment container out of the Universal Inventory
        /// into its own pinned window. Both are pure VIEW state — they mutate no game state at all;
        /// the whole gesture is a record in <see cref="Features.GridPinStore"/> plus a window.
        /// Returns TRUE only when the KEYBOARD half claimed this frame's key-down, which the caller
        /// uses to keep the same press from also arming an equipment radial. The mouse half never
        /// claims (a click is no radial key). Called only while <c>UIAConfig.GridEnabled</c>.</summary>
        private bool HandleGridPinShortcuts()
        {
            bool claimed = TryPinFromEquipmentKey();
            TryPinFromHudEquipmentClick();
            return claimed;
        }

        /// <summary>Shift + the vanilla 1..6 equipment button (read live through
        /// <c>KeyManager.GetKey</c>, so a rebind in the game's own Controls screen is respected):
        /// TOGGLE exactly the one container worn in that slot — never its nested bags, never anything
        /// else. Already pinned, the second press UNPINS it and closes its window (identical to that
        /// window's own X / shrink button), so the same key that tore the bag out puts it back.
        /// The gesture is claimed on ANY of the six keys, even when the slot is empty or holds
        /// something that is not a container — otherwise releasing the key would fall through into the
        /// equipment radial, which is not what Shift asked for.
        ///
        /// <para>Allowed from clean gameplay, and additionally whenever one of our own windows is up
        /// (the Universal Inventory, or a pinned window that outlived it): holding the mouse-control
        /// key to use those windows frees the cursor, which <c>CanAcceptGameplayInput</c> deliberately
        /// refuses, and Shift+# must still work with a bag window on screen.</para></summary>
        private bool TryPinFromEquipmentKey()
        {
            if (_radials != null && _radials.IsRadialOpen) return false;
            if (!Guards.CanAcceptGameplayInput()
                && !UI.Grid.TheGridPanel.IsOpen
                && UI.Grid.PinnedInventoryWindow.LiveCount == 0) return false;
            if (!Guards.CanToggleMenus()) return false;   // never over the console / a text field
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return false;

            var human = Guards.LocalHuman;
            if (human == null) return false;

            var names = EquipmentKeyRadialFeature.ButtonNames;
            for (int i = 0; i < names.Length; i++)
            {
                KeyCode key;
                try { key = KeyManager.GetKey(names[i]); }
                catch { continue; }
                if (key == KeyCode.None || !Input.GetKeyDown(key)) continue;
                PinSlotContainer(EquipSlotAt(human, i), true);   // keyboard half TOGGLES
                return true;   // one gesture per frame; the press belongs to the pin either way
            }
            return false;
        }

        /// <summary>The world-reach mouse modifier (vanilla <c>KeyMap.MouseControl</c>, default Alt —
        /// the same one the radial uses for reach/Z-grab) plus a left click on one of the six HUD
        /// equipment boxes pins that container. Hit-testing goes through
        /// <see cref="UI.Hud.HudSystem.ZoneAt"/>, the existing HUD box hit test the drag layer already
        /// uses (it inverse-warps the cursor, so a curved HUD is grabbed where it DRAWS); the hit is
        /// then matched against the human's six equipment slots so a HAND box — which reports a zone
        /// too — is ignored. Gated on <see cref="Guards.CanToggleMenus"/>, NOT
        /// <c>CanAcceptGameplayInput</c>: holding the modifier frees the cursor, which that gate
        /// deliberately refuses. ZoneAt() runs only on a press/release frame, never per frame.
        ///
        /// <para>CLICK, NOT DRAG. <see cref="UI.Hud.HudSlotDrag"/> polls raw <c>Input</c> over these
        /// very boxes to tear an item OUT of a slot, and neither system sees an EventSystem
        /// <c>eventData.dragging</c> to test (the boxes are pure renderers). So one press used to feed
        /// both, and dragging the backpack onto the ground ALSO pinned its inventory. The pin now
        /// resolves on mouse-UP and only for a genuine click: the press records the slot and the
        /// pointer, and the release must land on the SAME slot, within <see cref="PinClickSlopPx"/> of
        /// where it started, with no drag live (<c>HudSlotDrag.IsDragging</c>) and none finished during
        /// this gesture (<c>LastDragEndFrame</c>, which is already cleared by the time we tick after
        /// the drag layer). Anything else silently drops the press.</para></summary>
        private void TryPinFromHudEquipmentClick()
        {
            if (_radials != null && _radials.IsRadialOpen) { ClearPinPress(); return; }

            // A drag observed at ANY point in this gesture disqualifies it, permanently — the release
            // frame itself is caught by LastDragEndFrame, because HudSlotDrag clears _source in its
            // own tick before ours.
            if (_pinPressSlot != null
                && (UI.Hud.HudSlotDrag.IsDragging
                    || UI.Hud.HudSlotDrag.LastDragEndFrame == Time.frameCount))
                _pinPressSawDrag = true;

            if (Input.GetMouseButtonDown(0))
            {
                ClearPinPress();

                // Open the pinned window on a PLAIN click whenever the cursor is free (the mod's
                // mouse mode) — the same condition the drag layer uses to let you tear an item OUT of
                // these boxes. The old code required the vanilla MouseControl (Alt) modifier be held,
                // which made a plain click in mouse mode a no-op (FlorpyDorp: "click any of the 1-6
                // slots, they don't open"). When the cursor is LOCKED to the FPS crosshair
                // IsCursorFree is false, so a normal attack/use click is never hijacked.
                if (!UI.Hud.HudSlotDrag.IsCursorFree) return;
                if (!Guards.CanToggleMenus()) return;
                if (UI.Hud.HudSlotDrag.IsDragging) return;   // the drag layer already claimed it

                var down = EquipSlotUnderPointer();
                if (down == null) return;
                _pinPressSlot = down;
                _pinPressPos = (Vector2)Input.mousePosition;
                return;
            }

            if (!Input.GetMouseButtonUp(0))
            {
                // Self-heal: the button went up on a frame we did not run (a stand-down gap, a menu
                // stealing the frame), so the press can never resolve — forget it.
                if (_pinPressSlot != null && !Input.GetMouseButton(0)) ClearPinPress();
                return;
            }

            var pressed = _pinPressSlot;
            bool sawDrag = _pinPressSawDrag;
            Vector2 pressAt = _pinPressPos;
            ClearPinPress();

            if (pressed == null || sawDrag) return;
            if (UI.Hud.HudSlotDrag.IsDragging) return;
            if (((Vector2)Input.mousePosition - pressAt).sqrMagnitude > PinClickSlopPx * PinClickSlopPx) return;
            if (!Guards.CanToggleMenus()) return;
            if (!ReferenceEquals(EquipSlotUnderPointer(), pressed)) return;   // released over another box

            PinSlotContainer(pressed, false);
        }

        /// <summary>The equipment slot whose HUD box is under the cursor right now, or null. The hit
        /// test is <see cref="UI.Hud.HudSystem.ZoneAt"/> (it inverse-warps the cursor, so a curved HUD
        /// is grabbed where it DRAWS); the zone's slot is then matched against the human's six worn
        /// slots, so a HAND box — which reports a zone too — comes back null.</summary>
        private static Assets.Scripts.Objects.Slot EquipSlotUnderPointer()
        {
            var zone = UI.Hud.HudSystem.ZoneAt();
            var slot = zone != null ? zone.Slot : null;
            if (slot == null) return null;

            var human = Guards.LocalHuman;
            if (human == null) return null;
            for (int i = 0; i < EquipmentKeyRadialFeature.ButtonNames.Length; i++)
                if (ReferenceEquals(EquipSlotAt(human, i), slot)) return slot;
            return null;
        }

        /// <summary>Forget the armed press. Called on every mouse-down, on every resolved/abandoned
        /// mouse-up, whenever the gesture is cancelled (a radial opened over it, the button was
        /// released while we were not ticking) and from teardown — no static may survive an F6.</summary>
        private static void ClearPinPress()
        {
            _pinPressSlot = null;
            _pinPressSawDrag = false;
            _pinPressPos = Vector2.zero;
        }

        /// <summary>The human's worn slot for equipment index 0..5, in the vanilla 1..6 button order
        /// (Helmet, Glasses, Suit, Back, Uniform, Toolbelt) — the same mapping
        /// <see cref="EquipmentKeyRadialFeature.CreateAll"/> and the HUD equipment column use.</summary>
        private static Assets.Scripts.Objects.Slot EquipSlotAt(
            Assets.Scripts.Objects.Entities.Human human, int index)
        {
            if (human == null) return null;
            try
            {
                switch (index)
                {
                    case 0: return human.HelmetSlot;
                    case 1: return human.GlassesSlot;
                    case 2: return human.SuitSlot;
                    case 3: return human.BackpackSlot;
                    case 4: return human.UniformSlot;
                    case 5: return human.ToolbeltSlot;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Pin the ONE container occupying <paramref name="slot"/> — that container only, not
        /// the bags nested inside it. Read-only on the game side: <c>slot.Get()</c> for identity (never
        /// the obsolete Occupant), then that single persistent ReferenceId into
        /// <see cref="UI.Grid.TheGridPanel.PinContainer"/>, which records the one pin and FOCUSES an
        /// existing window rather than duplicating it, so a repeat Shift+# raises the bag you already
        /// tore out. The window then outlives the main one (only the vanilla close-all takes pins down;
        /// a pin's own X / shrink button unpins it). An empty slot, or one holding something The Grid
        /// would not render as its own region, is a NO-OP with the vanilla action-fail sound — a pinned
        /// window for a leaf item would come up empty.
        ///
        /// <para><paramref name="toggle"/> (the keyboard half): if this container is ALREADY pinned the
        /// call UNPINS it instead — <c>TheGridPanel.UnpinContainer</c> closes the window and folds the
        /// container back into the main tree, exactly what that window's X / shrink button does. The
        /// mouse half passes false and keeps the pin-or-focus behaviour.</para></summary>
        private static void PinSlotContainer(Assets.Scripts.Objects.Slot slot, bool toggle)
        {
            if (slot == null) return;

            Assets.Scripts.Objects.DynamicThing occupant = null;
            try { occupant = slot.Get(); }
            catch { occupant = null; }

            if (occupant == null || !IsPinnableContainer(occupant))
            {
                try { UIAudioManager.Play(UIAudioManager.ActionFailHash); }
                catch { }
                return;
            }

            long refId = occupant.ReferenceId;
            if (toggle && UI.Grid.TheGridPanel.IsPinned(refId))
            {
                UI.Grid.TheGridPanel.UnpinContainer(refId);
                return;
            }

            UI.Grid.TheGridPanel.PinContainer(refId);
        }

        /// <summary>Would The Grid build a container REGION for this thing? Mirrors
        /// <c>GridModel.ShouldRecurse</c> exactly so a shortcut can never pin something the tree walk
        /// then refuses to produce a node for — which would leave a pin record with no window forever.
        /// The renderer is unconditionally <see cref="UI.Grid.GridModel.ActiveMode"/> (Grid; Nested is
        /// deprecated and unreachable) — there is no config branch here (the dead
        /// <c>UIAConfig.GridMode</c> entry was removed in 0.9.2.5's config-migration cleanup).</summary>
        private static bool IsPinnableContainer(Assets.Scripts.Objects.DynamicThing thing)
        {
            if (thing == null || thing.Slots == null || thing.Slots.Count == 0) return false;
            return UI.Grid.GridModel.IsStorageContainer(thing);
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
                    if (_radials.IsRadialOpen) _radials.CloseAll("standdown-nocandraw-draw");
                    if (Windows.RadialEditorMode.Active) Windows.RadialEditorMode.Exit();
                    return;
                }

                // Editor mode owns the screen: black backdrop + example radials.
                if (Windows.RadialEditorMode.Active)
                {
                    if (_radials.IsRadialOpen) _radials.CloseAll("radial-editor-active");
                    Windows.RadialEditorMode.Draw();
                    return;
                }

                // The click-to-edit popup rides the game's ImGui frame.
                Windows.HudEditorWindow.DrawPopupOverlay();

                // The visor HUD is pure UGUI and draws from Update (HudSystem stands down, and
                // restores vanilla's panels, when VisorHudEnabled is off). The 0.1.0 ImGui HUD
                // overlay was retired in 0.9.2.5 — document mode IS the HUD now.

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
            _drawExceptions++;
            float now = Time.unscaledTime;
            // Rate-limited AND self-healing: first 3 in full, then at most once / 5s. The ImGui
            // postfix calls this every frame DrawOverlay throws, so a lifetime "suppress after 3"
            // would either spam the disk or go permanently silent — this does neither.
            if (_drawExceptions <= 3)
            {
                UIALog.Error("Overlay draw failed: " + e);
                _drawExcLogAt = now;
                if (_drawExceptions == 3)
                    UIALog.Error("Further overlay draw errors will be rate-limited (once / 5s).");
            }
            else if (now - _drawExcLogAt >= 5f)
            {
                UIALog.Error($"Overlay draw still failing ({_drawExceptions} total): " + e.Message);
                _drawExcLogAt = now;
            }
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
            // the post-0.9.2.5 play-test round: the per-wheel EquipmentKeyRadialsEnabled toggle is gone — with the radial
            // half on, the 1-6 wheels are core functionality, so RadialEnabled is the only gate.
            if (!UIAConfig.MasterEnable.Value || !UIAConfig.RadialEnabled.Value) return false;
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
            // IDEMPOTENT, cross-instance-safe teardown. The old `if (Instance != this) return;` guard
            // sat BEFORE the try/finally, so IF it ever tripped it would skip teardown of the very
            // object being destroyed — leaking that object's Harmony patches (N x the per-frame ImGui
            // postfix and every polled prefix -> progressive FPS collapse), its DontDestroyOnLoad
            // canvases, and its game-static delegate subscriptions. In the CURRENT ScriptEngine reload
            // path that guard did NOT actually trip: each F6 loads a renamed assembly, so Instance is a
            // per-assembly static the new load can't overwrite, and the loader destroys the old instance
            // (end-of-frame OnDestroy) before AddComponent-ing the new one (deferred a frame). So the
            // real leak this changeset fixes was the per-frame stack-trace disk write, not a per-reload
            // teardown skip. We nonetheless no longer DEPEND on that ordering: teardown ALWAYS releases
            // what this instance owns (only the global pointer writes at the very end are gated on
            // ownership), which also lets the genuinely-needed game-static restores below that the old
            // path lacked (ProfileSort/StowRouter/etc.) run unconditionally. Everything here is
            // null-guarded and idempotent, so a stale/uninitialized instance running it is a safe no-op
            // and a double invocation cannot double-free.
            if (_torndown) return;   // per-instance latch: a second OnDestroy on THIS object no-ops
            _torndown = true;
            bool isCurrent = (Instance == this);
            try
            {
                _radials?.ShutdownImmediate();
                Core.WedgeHotkeys.Clear();
                Core.SlotFlash.Reset(); // drop any live stow-flash so a reload never reads a stale sprite
                Core.StowRouter.Reset(); // drop pooled stow candidates (they hold Slot/Thing refs)
                Features.BagProfileStore.ResetRuntimeCaches(); // badge-tag + prefab-default caches (strings only, but keep double-F6 clean)
                // NOTE: ProfileSort.Reset() lives in the FINALLY below — Item.SmartSortItems is a
                // GAME static holding our delegate, so its restore must not be skippable.
                // Release the cursor latch BEFORE unpatching: the AltKeyDown postfix is about to go
                // away, so a latch left on would strand a freed cursor with nothing holding it.
                Core.CursorLatch.Reset();
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
                UI.Grid.TheGridPanel.Shutdown();   // destroys its canvas + unhooks OnUIClose
                Features.GridCollapseStore.Reset(); // drop the per-save collapse cache
                Features.GridPinStore.Reset();      // drop the per-save pin cache (disk file survives)
                UI.Grid.GridModel.Reset();          // drop the cached model/signature state
                _gridPeeking = false;
                _gridEditPreviewOpened = false;
                _gridLatchedOpenedThisPress = false;
                ClearPinPress();                    // never carry an armed press across a reload
                UI.Hud.HudSystem.Shutdown();
                Core.DragGhostLayer.Shutdown();    // destroy the shared top-most ghost canvas + world mirror
                Core.CursorDiag.Shutdown();        // silence the cursor/drag trace
                Core.CursorBlockArbiter.Shutdown(); // force-clear the world-pick block (no stale hold post-reload)
                Core.WorldSlotCue.Hide();          // drop any live world-slot placement highlight
                Core.WorldSlotCue.Shutdown();
                BagProfileStore.SaveAssignments();
                IconCache.Clear();
                Core.VanillaIcons.Clear();          // drop borrowed vanilla sprites (dead refs after a reload)
                HudIconStore.Shutdown();
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
                // These MUST NOT be skippable. The block above is one long try covering ~20
                // teardown calls; anything that throws part-way used to skip whatever followed.
                // All of these leave live references from GAME statics into an assembly that F6
                // is about to replace: KeyManager.OnControlsChanged would keep a delegate into
                // dead code (and starve the later vanilla subscribers InventoryManager
                // .RefreshDisplaySlotBindings and HotkeyDisplay.Refresh), Item.SmartSortItems
                // would keep our dead sort wrapper installed (and the NEXT Install() would then
                // capture that dead wrapper as its "original" — unrecoverable without a game
                // restart), and un-unpatched Harmony patches keep running dead detours. Each is
                // independently guarded so one failing cannot skip the others.
                // The tutorial/handbook/pause trio hold GAME statics too: a latched pause is
                // Time.timeScale = 0 with nobody left to clear it, GamePause subscribes
                // WorldManager.OnPaused (a plain static event the game never clears — the
                // reloaded assembly's Unhook removes only its OWN delegate, so a skip here is
                // unrecoverable without a game restart), and the modals hold KeyManager input
                // states + MouseModeController modals + the Esc-swallow Typing state.
                try { UI.Menu.Tutorial.TutorialCoach.Shutdown(); }
                catch (Exception e) { UIALog.Error("TutorialCoach.Shutdown failed: " + e); }
                try { UI.Menu.Tutorial.TutorialTextStore.Shutdown(); }
                catch (Exception e) { UIALog.Error("TutorialTextStore.Shutdown failed: " + e); }
                try { UI.Menu.HandbookViewer.Shutdown(); }
                catch (Exception e) { UIALog.Error("HandbookViewer.Shutdown failed: " + e); }
                try { Core.ModalInputChain.Release(); }
                catch (Exception e) { UIALog.Error("ModalInputChain.Release failed: " + e); }
                try { Core.GamePause.ReleaseAll(); }
                catch (Exception e) { UIALog.Error("GamePause.ReleaseAll failed: " + e); }
                try { Core.GamePause.Unhook(); }
                catch (Exception e) { UIALog.Error("GamePause.Unhook failed: " + e); }
                try { Features.ProfileSort.Reset(); }
                catch (Exception e) { UIALog.Error("ProfileSort.Reset failed: " + e); }
                try { Core.UiaKeybinds.Unhook(); }
                catch (Exception e) { UIALog.Error("UiaKeybinds.Unhook failed: " + e); }
                // MUTATES A VANILLA OBJECT: the F2 hints panel's lifted canvas must be restored
                // here or the tips would cover the Esc menu forever after the mod unloads.
                try { Core.HelperHintsLift.Shutdown(); }
                catch (Exception e) { UIALog.Error("HelperHintsLift.Shutdown failed: " + e); }
                try { Core.Patch_Human_GetStatsTooltip_Details.ResetRuntimeState(); }
                catch (Exception e) { UIALog.Error("DetailedVitalsTooltip reset failed: " + e); }
                try { _harmony?.UnpatchSelf(); }
                catch (Exception e) { UIALog.Error("UnpatchSelf failed: " + e); }
                _harmony = null;   // this instance's patches are gone; never let a re-entry re-unpatch
                // Relinquish the global pointer ONLY if we still own it. A stale instance being
                // destroyed must never null out a live newer Instance / re-enable init under it — but
                // it has still discharged every one of its OWN liabilities above.
                if (isCurrent)
                {
                    Instance = null;
                    _loaded = false;
                }
            }
        }
    }
}
