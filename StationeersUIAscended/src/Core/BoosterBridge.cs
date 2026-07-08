using System;

namespace StationeersUIAscended.Core
{
    /// <summary>
    /// Optional, reflection-only LaunchPadBooster integration. When SLP (and thus Booster)
    /// is installed, registering a Mod adds us to SLP's multiplayer mod-list exchange as an
    /// OPTIONAL client-side mod. No compile-time reference: linking Booster would break
    /// plain-BepInEx installs without SLP, and shipping a copy of the DLL is forbidden
    /// (SLP loads every DLL in a mod folder).
    /// </summary>
    public static class BoosterBridge
    {
        public static bool Registered { get; private set; }

        public static void TryRegister(string name, string version)
        {
            try
            {
                var modType = Type.GetType("LaunchPadBooster.Mod, LaunchPadBooster", false);
                if (modType == null)
                {
                    UIALog.Debug("LaunchPadBooster not present; skipping soft registration.");
                    return;
                }
                Activator.CreateInstance(modType, name, version);
                Registered = true;
                UIALog.Info("Registered with LaunchPadBooster as optional client-side mod.");
            }
            catch (Exception e)
            {
                UIALog.Debug("Booster soft-registration skipped: " + e.Message);
            }
        }
    }
}
