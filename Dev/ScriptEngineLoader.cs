using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using LaunchPadBooster;
using UnityEngine;

namespace StationeersUIMod.Dev
{
    /// <summary>
    /// DEV-ONLY bridge that lets BepInEx ScriptEngine hot-reload this SLP mod.
    ///
    /// The shipped entry point is <see cref="StationeersUIMod"/> — a plain MonoBehaviour with
    /// <c>OnLoaded(List&lt;GameObject&gt;, ConfigFile)</c>, which is StationeersLaunchPad's
    /// "DefaultEntrypoint" shape. ScriptEngine knows nothing about SLP: it scans a reloaded
    /// assembly for <see cref="BaseUnityPlugin"/> subclasses and AddComponents those. A bare
    /// MonoBehaviour is never instantiated, so under ScriptEngine the mod would silently do
    /// nothing. This shim is the missing BaseUnityPlugin — it stands in for SLP.
    ///
    /// It attaches the real entry point to its OWN GameObject, so ScriptEngine's teardown
    /// destroys it too and StationeersUIMod.OnDestroy runs (Harmony un-patched, radials closed).
    ///
    /// This file lives OUTSIDE Assets/, so Unity — and therefore the shipped SLP mod — never
    /// contains it. Only Dev/StationeersUIMod.Dev.csproj compiles it.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ScriptEngineLoader : BaseUnityPlugin
    {
        public const string PluginGuid = "com.stationeersuimod.ui.scriptengine";
        public const string PluginName = "Stationeers UI Mod (ScriptEngine dev loader)";
        // const => compile-time inlined, so reading it does NOT trigger StationeersUIMod's
        // static initializer. That matters: PruneBoosterRegistry must run first (see below).
        public const string PluginVersion = StationeersUIMod.ModVersion;

        private const string ModName = "StationeersUIMod";

        private void Awake()
        {
            // The WHOLE body is guarded. On the very first ScriptEngine load (LoadOnStart=true) this
            // Awake can run on the boot frame, before the game scene / singletons exist — and if any
            // step throws there, an unguarded Awake would surface as ScriptEngine's opaque "Failed to
            // load plugin" NRE and could leave a half-added StationeersUIMod component receiving
            // Update / OnDestroy against half-initialized state. Fail soft instead: log, tidy up, stay
            // inert. (Root-cause fix: set LoadOnStart=false in the ScriptEngine cfg so the mod loads on
            // the first post-boot F6/watcher pass instead of the boot frame; this is belt-and-braces.)
            StationeersUIMod added = null;
            try
            {
                // MUST run before anything touches the StationeersUIMod type, because its static
                // `readonly Mod MOD = new Mod(...)` initializer calls LaunchPadBooster's Mod ctor,
                // which does ModsByHash.Add(hash, this) — a Dictionary.Add that THROWS on a
                // duplicate key. LaunchPadBooster.dll is never reloaded, so its registry survives
                // every hot reload while our assembly is recreated. Without this prune, the second
                // F6 would die in a TypeInitializationException.
                PruneBoosterRegistry();

                if (StationeersUIMod.Instance != null)
                {
                    Logger.LogWarning("StationeersUIMod already loaded (probably by StationeersLaunchPad). " +
                                      "Dev loader staying inert — remove the mod from the SLP mods folder to hot-reload it.");
                    return;
                }

                Core.UIALog.Init(Logger);

                added = gameObject.AddComponent<StationeersUIMod>();
                added.OnLoaded(new List<GameObject>(), Config);

                Logger.LogInfo($"ScriptEngine dev loader started StationeersUIMod v{StationeersUIMod.VersionDisplay}. Press F6 to hot-reload.");
            }
            catch (System.Exception e)
            {
                Logger.LogError("ScriptEngine dev loader Awake failed; staying inert. " + e);
                // Never leave a half-added component behind — it would tick Update / OnDestroy against
                // half-initialized state. StationeersUIMod.OnLoaded already self-guards its own body, so
                // reaching here means a Unity-level failure (e.g. AddComponent on the boot frame).
                try { if (added != null) Destroy(added); }
                catch { }
            }
        }

        /// <summary>
        /// Drop Mod registrations left behind by a previous hot reload. Booster keeps
        /// <c>Mod.AllMods</c> (public) and <c>Mod.ModsByHash</c> (internal) as statics inside
        /// LaunchPadBooster.dll, which outlives our reloaded assembly.
        /// </summary>
        private void PruneBoosterRegistry()
        {
            try
            {
                int removed = Mod.AllMods.RemoveAll(m => m.ID.Name == ModName);

                var byHash = typeof(Mod).GetField("ModsByHash", BindingFlags.NonPublic | BindingFlags.Static);
                if (byHash?.GetValue(null) is IDictionary<int, Mod> dict)
                    dict.Remove(Animator.StringToHash(ModName));

                if (removed > 0)
                    Logger.LogInfo($"Cleared {removed} stale LaunchPadBooster registration(s) from a previous hot reload.");
            }
            catch (System.Exception e)
            {
                // Booster internals moved: hot reload may now break on the 2nd F6.
                Logger.LogWarning("Could not prune LaunchPadBooster registry: " + e.Message);
            }
        }
    }
}
