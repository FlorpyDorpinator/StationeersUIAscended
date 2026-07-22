using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.UI;
using HarmonyLib;
using UI.ImGuiUi.ImGuiWindows;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Fail-soft patch harness (SprayColor pattern): each patch class applies independently,
    /// so a game update that breaks one target degrades that feature instead of the mod.
    /// </summary>
    public static class PatchHarness
    {
        public static int Applied { get; private set; }
        public static int Failed { get; private set; }

        public static void TryPatchAll(Harmony harmony, params Type[] patchClasses)
        {
            foreach (var type in patchClasses)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    Applied++;
                    UIALog.Debug("Patched: " + type.Name);
                }
                catch (Exception e)
                {
                    Failed++;
                    UIALog.Error($"Patch {type.Name} FAILED (feature degraded): {e.Message}");
                }
            }
            UIALog.Info($"Harmony patches applied: {Applied}, failed: {Failed}.");
        }
    }

    /// <summary>
    /// Single per-frame ImGui hook: runs inside the gameplay branch of the game's own
    /// ImGui frame (after ImGuiWindowManager windows, before the frame is rendered), so we
    /// never touch frame begin/end ourselves and we never draw during splash/loading.
    /// </summary>
    [HarmonyPatch(typeof(ImGuiWindowManager), nameof(ImGuiWindowManager.Draw))]
    internal static class Patch_ImGuiWindowManager_Draw
    {
        private static void Postfix()
        {
            try
            {
                StationeersUIMod.Instance?.DrawOverlay();
            }
            catch (Exception e)
            {
                StationeersUIMod.Instance?.ReportDrawException(e);
            }
        }
    }

    /// <summary>
    /// Suppresses the vanilla polled handling of keys we own: the active-hand key (R) when
    /// the tool radial has it, and the 1-6 equipment keys when equipment-key radials are on.
    /// Taps/holds are re-dispatched by the owning feature so nothing is lost.
    /// </summary>
    [HarmonyPatch(typeof(InventoryManager), "CheckDisplaySlot", typeof(SlotDisplay), typeof(string))]
    internal static class Patch_InventoryManager_CheckDisplaySlot
    {
        private static bool Prefix(SlotDisplay displaySlot, string buttonName, ref bool __result)
        {
            var plugin = StationeersUIMod.Instance;
            if (plugin == null) return true;
            if (buttonName == "ActiveHandSlot" && plugin.ToolRadialOwnsVanillaKey)
            {
                __result = false;
                return false;
            }
            if (plugin.EquipmentKeysOwnButton(buttonName))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Suppresses the vanilla Tab scoreboard toggle while the bag radial owns Tab; taps are
    /// re-dispatched by BagRadialFeature.OnTap.
    /// </summary>
    [HarmonyPatch(typeof(KeyManager), "ToggleScoreboard")]
    internal static class Patch_KeyManager_ToggleScoreboard
    {
        private static bool Prefix()
        {
            var plugin = StationeersUIMod.Instance;
            if (plugin != null && plugin.BagRadialOwnsVanillaKey)
                return false;
            return true;
        }
    }

    /// <summary>
    /// 0.6.2: optional WASD/Space while a radial is open. Set per-frame by
    /// RadialController (radial open + config on + not typing in search + not seated).
    ///
    /// Decompile facts (27701): walking is NOT gated by KeyManager's input state at all —
    /// it dies because the unlocked cursor flips InventoryManager.AllowMouseControl
    /// (InventoryManager.cs:130, "!Cursor.visible &amp;&amp; ..."), read by
    /// MovementController.GetDesiredGroundVelocity (:625) / SetMovementMode (:188) /
    /// step-up (:812). Jump alone has two extra gates inside HandleJump (:590
    /// InputState != Game, :594 Cursor.visible). Forcing AllowMouseControl true merely
    /// recreates the normal hidden-cursor gameplay value, so anything that behaves in
    /// normal play behaves identically here; the ONLY other callers are Shuttle (:457)
    /// and Rover (:361) pilot input, which is why Active requires not-seated.
    /// We never flip KeyManager's input state map — that would re-arm every Game-bound
    /// KeyWrap binding (drop, swap hands, spawn item...). Jump instead swaps the two
    /// gate INPUTS only for the synchronous duration of HandleJump.
    /// </summary>
    public static class RadialMovement
    {
        public static bool Active;

        private static System.Reflection.MethodInfo _setInputState;
        private static bool _searched;
        private static readonly object[] _arg = new object[1];

        /// <summary>KeyManager.InputState has a private setter; swap it via reflection.
        /// Single-threaded and only ever held across HandleJump's body.</summary>
        internal static bool TrySetInputState(KeyInputState value)
        {
            try
            {
                if (!_searched)
                {
                    _searched = true;
                    var prop = typeof(KeyManager).GetProperty("InputState",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    _setInputState = prop != null ? prop.GetSetMethod(true) : null;
                    if (_setInputState == null)
                        UIALog.Warn("KeyManager.InputState setter not found - Space-jump in radials degraded (WASD unaffected).");
                }
                if (_setInputState == null) return false;
                _arg[0] = value;
                _setInputState.Invoke(null, _arg);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>WASD half of the radial movement pass-through: while Active, the getter
    /// reports the normal hidden-cursor gameplay value. (Body is 4 static reads — well
    /// past Mono's inline budget, so the detour reliably applies.)</summary>
    [HarmonyPatch(typeof(InventoryManager), "AllowMouseControl", MethodType.Getter)]
    internal static class Patch_InventoryManager_AllowMouseControl
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && RadialMovement.Active) __result = true;
        }
    }

    /// <summary>Cursor latch (see <see cref="CursorLatch"/>): while the latch is on, report the
    /// mouse modifier as HELD. AltKeyDown's only consumer is MouseModeController.Check
    /// (MouseModeController.cs:57), which unlocks the cursor whenever it reads true — so this one
    /// postfix latches the cursor while still inheriting every other rule vanilla applies (open
    /// modals, character customisation, not-in-game). We never touch Cursor/InputMouse ourselves.</summary>
    [HarmonyPatch(typeof(Assets.Scripts.MouseModeController),
        nameof(Assets.Scripts.MouseModeController.AltKeyDown), MethodType.Getter)]
    internal static class Patch_MouseModeController_AltKeyDown
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && CursorLatch.Active) __result = true;
        }
    }

    /// <summary>Jump half: HandleJump early-returns on InputState != Game (:590) and
    /// Cursor.visible (:594). For exactly the synchronous duration of the original body
    /// we swap both inputs to their hidden-cursor values and restore in a Finalizer
    /// (exception-safe). Nothing else reads either value mid-call — vanilla's KeyWrap
    /// polling happens in KeyManager.ManagerUpdate, not inside physics.</summary>
    [HarmonyPatch(typeof(Assets.Scripts.MovementController), "HandleJump")]
    internal static class Patch_MovementController_HandleJump
    {
        internal struct Scope
        {
            public bool RestoreCursor;
            public bool RestoreInput;
            public KeyInputState Previous;
        }

        private static void Prefix(out Scope __state)
        {
            __state = default(Scope);
            if (!RadialMovement.Active) return;
            if (UnityEngine.Cursor.visible)
            {
                UnityEngine.Cursor.visible = false;
                __state.RestoreCursor = true;
            }
            if (KeyManager.InputState != KeyInputState.Game)
            {
                __state.Previous = KeyManager.InputState;
                __state.RestoreInput = RadialMovement.TrySetInputState(KeyInputState.Game);
            }
        }

        private static void Finalizer(Scope __state)
        {
            if (__state.RestoreInput) RadialMovement.TrySetInputState(__state.Previous);
            if (__state.RestoreCursor) UnityEngine.Cursor.visible = true;
        }
    }

    /// <summary>Jetpack half of the radial pass-through (#9). The jetpack toggle lives in
    /// MovementController.MovementHandler: the ENABLE arm (Animation -> Jetpack, in SetMovementMode)
    /// only wants InventoryManager.AllowMouseControl — already forced by <see cref="RadialMovement"/> —
    /// but the DISABLE arm (the switch's Jetpack / JetpackGravity cases, decompile :458 / :502) is
    /// gated on <c>!Cursor.visible</c>. With a radial open the cursor is freed, so you could start the
    /// jetpack but never stop it. Mirror the HandleJump fix: for exactly the synchronous duration of
    /// MovementHandler, while the pass-through is Active, report the cursor as hidden. The only
    /// Cursor.visible reads inside are jetpack-related (the toggle + HandleJetpack physics), which we
    /// WANT to behave as normal hidden-cursor flight; vanilla's key polling runs in
    /// KeyManager.ManagerUpdate, not inside physics, so nothing else observes the swap. Restored in a
    /// Finalizer so an exception mid-body can't strand the cursor hidden.</summary>
    [HarmonyPatch(typeof(Assets.Scripts.MovementController), "MovementHandler")]
    internal static class Patch_MovementController_MovementHandler
    {
        private static void Prefix(out bool __state)
        {
            __state = false;
            if (!RadialMovement.Active) return;
            if (UnityEngine.Cursor.visible) { UnityEngine.Cursor.visible = false; __state = true; }
        }

        private static void Finalizer(bool __state)
        {
            if (__state) UnityEngine.Cursor.visible = true;
        }
    }

    /// <summary>
    /// While the HUD hides the vanilla instrument cluster, vanilla's own per-frame
    /// re-show of the jetpack box (PlayerStateWindow.UpdateJetpackPanels, 27701
    /// PlayerStateWindow.cs:322 — `if (!InfoJetpack.IsVisible) SetVisible(true)`) would
    /// fight our per-frame re-hide: both sides rewriting every Image/TMP alpha in the
    /// panel each frame. Skipping the method is display-only (it only shows/updates the
    /// jetpack readout) and stops the instant the hide toggle clears.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Scripts.UI.PlayerStateWindow), "UpdateJetpackPanels")]
    internal static class Patch_PlayerStateWindow_UpdateJetpackPanels
    {
        private static bool Prefix()
        {
            return !UI.Hud.HudSystem.PlayerStateClusterHidden;
        }
    }

    /// <summary>
    /// Vanilla NRE guard: Human.SpawnDynamicThing (Human.cs:4430 in 27701) checks
    /// GameMode == Creative but NOT whether a spawnable is selected — with
    /// InventoryManager.SpawnPrefab null it dereferences spawnPrefab.SpawnId and throws
    /// on every F9 press. Vanilla would only ever crash in this state, so skipping is
    /// strictly safe, creative or not.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Scripts.Objects.Entities.Human), "SpawnDynamicThing")]
    internal static class Patch_Human_SpawnDynamicThing
    {
        private static bool Prefix()
        {
            return InventoryManager.SpawnPrefab != null;
        }
    }

    /// <summary>
    /// Suppresses the vanilla creative SpawnItem key while the F9 HUD editor owns the
    /// same key (KeyMap.SpawnItem is F9 by default, KeyManager.cs:427): one press must
    /// toggle the editor OR spawn an item, never both. Rebinding HudEditorKey away from
    /// the collision restores vanilla spawning untouched.
    /// </summary>
    [HarmonyPatch(typeof(KeyManager), "SpawnDynamicThing")]
    internal static class Patch_KeyManager_SpawnDynamicThing
    {
        private static bool Prefix()
        {
            try
            {
                if (StationeersUIMod.Instance == null) return true;
                if (UIAConfig.MasterEnable == null || !UIAConfig.MasterEnable.Value) return true;
                var editorKey = UI.Hud.HudConfig.HudEditorKey;
                if (editorKey == null || editorKey.Value != KeyMap.SpawnItem) return true;
                // Same context gate the editor toggle uses: if the press could reach our
                // editor, vanilla stays quiet (even when a sibling menu blocks the toggle —
                // better a dead key than a surprise spawn).
                return !Guards.CanToggleMenus();
            }
            catch
            {
                return true; // never let the guard itself take vanilla down
            }
        }
    }

    /// <summary>
    /// While the F9 HUD editor is open, swallow ALL vanilla equipment-slot input
    /// (InventoryManager.CheckDisplaySlotInput drives the 1-6 slot keys that pop the
    /// helmet/back/belt bag windows, plus the inventory scroll/next/prev). Otherwise a
    /// 1-6 press mid-edit throws a vanilla window over the designer (play-test). Purely a
    /// suppression — the instant the editor closes, vanilla input resumes untouched.
    /// </summary>
    [HarmonyPatch(typeof(InventoryManager), "CheckDisplaySlotInput")]
    internal static class Patch_InventoryManager_CheckDisplaySlotInput
    {
        private static bool Prefix()
        {
            try { return !Windows.HudEditorMode.Active; }
            catch { return true; }
        }
    }

    /// <summary>
    /// Diagnostic + fail-soft guard for a VANILLA null-ref. ThingRenderer.OverrideShadowMode
    /// (ThingRenderer.cs:405) dereferences its private UnityRenderer without a null check, so a
    /// batched/instanced Thing (no individual renderer — HasRenderer() is still true via its
    /// RocketRenderer/DrawData) throws an unobserved-task NRE every time it crosses the shadow-LOD
    /// distance as the player walks past it. We read that field; when it's null we log the
    /// offending Thing ONCE — name + ref id + world position, so it can be walked to and deleted —
    /// and skip the body so the log spam stops. Purely defensive: a renderer-less ThingRenderer has
    /// no Unity shadow mode to set. Not the mod's bug; this just papers over it and names the culprit.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Scripts.Objects.ThingRenderer), "OverrideShadowMode")]
    internal static class Patch_ThingRenderer_OverrideShadowMode
    {
        private static System.Reflection.FieldInfo _urField;
        private static readonly System.Collections.Generic.HashSet<long> _logged =
            new System.Collections.Generic.HashSet<long>();

        private static bool Prefix(Assets.Scripts.Objects.ThingRenderer __instance)
        {
            try
            {
                if (__instance == null || UIAConfig.MasterEnable == null || !UIAConfig.MasterEnable.Value)
                    return true;
                if (_urField == null)
                    _urField = AccessTools.Field(typeof(Assets.Scripts.Objects.ThingRenderer), "UnityRenderer");
                var r = _urField != null ? _urField.GetValue(__instance) as UnityEngine.Renderer : null;
                if (r != null) return true; // has a real renderer -> let vanilla run

                var t = __instance.Parent;
                if (t != null && _logged.Add(t.ReferenceId))
                {
                    UnityEngine.Vector3 pos = UnityEngine.Vector3.zero;
                    try { pos = t.transform.position; } catch { }
                    UIALog.Info("[UIA shadow-guard] renderer-less Thing '" + t.DisplayName + "' (RefId "
                        + t.ReferenceId + ") at " + pos.ToString("F1") + " - the source of the vanilla "
                        + "shadow-LOD NRE spam. Walk there and delete it; the crash is now suppressed.");
                }
                return false; // skip the body -> no NRE
            }
            catch { return true; } // never let the guard itself take vanilla down
        }
    }
}

