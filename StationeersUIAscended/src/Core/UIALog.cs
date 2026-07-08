using BepInEx.Logging;

namespace StationeersUIAscended.Core
{
    /// <summary>Thin static wrapper so any class can log without holding the plugin instance.</summary>
    public static class UIALog
    {
        private static ManualLogSource _log;

        public static void Init(ManualLogSource log) => _log = log;

        public static void Info(string msg) => _log?.LogInfo(msg);
        public static void Warn(string msg) => _log?.LogWarning(msg);
        public static void Error(string msg) => _log?.LogError(msg);
        public static void Debug(string msg) => _log?.LogDebug(msg);
    }
}
