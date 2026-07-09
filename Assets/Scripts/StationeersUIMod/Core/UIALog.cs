using Assets.Scripts;
using BepInEx.Logging;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>Thin static wrapper so any class can log without holding the plugin instance.
    /// Works for both BepInEx plugin and SLP Unity-mod paths (falls back to Debug.Log + console print).</summary>
    public static class UIALog
    {
        private static ManualLogSource _log;

        public static void Init(ManualLogSource log) => _log = log;

        public static void Info(string msg)
        {
            _log?.LogInfo(msg);
            SafeConsole($"[StationeersUIMod] {msg}");
            UnityEngine.Debug.Log("[StationeersUIMod] " + msg);
        }

        public static void Warn(string msg)
        {
            _log?.LogWarning(msg);
            UnityEngine.Debug.LogWarning("[StationeersUIMod] " + msg);
        }

        public static void Error(string msg)
        {
            _log?.LogError(msg);
            UnityEngine.Debug.LogError("[StationeersUIMod] " + msg);
        }

        public static void Debug(string msg)
        {
            _log?.LogDebug(msg);
#if DEBUG || DEVELOPMENT_BUILD
            UnityEngine.Debug.Log("[StationeersUIMod DEBUG] " + msg);
#endif
        }

        private static void SafeConsole(string msg)
        {
            try { ConsoleWindow.Print(msg, System.ConsoleColor.Cyan); } catch { }
        }
    }
}

