using System;
using System.Collections.Generic;
using Assets.Scripts;            // DynamicBodyBag
using HarmonyLib;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Console command <c>finddead</c> — lists dead-player body bags (DynamicBodyBag) near you
    /// with their world coordinates and distance, nearest first. Optional range argument
    /// (metres): <c>finddead 2000</c>; defaults to 1000 m. Read-only; touches no game state.
    /// Registered by intercepting <see cref="Util.Commands.CommandLine.Process(string)"/> so it
    /// needs no reflection into the command map.
    /// </summary>
    public static class FindDeadCommand
    {
        private const float DefaultRange = 1000f;

        public static void Run(string input)
        {
            try
            {
                float range = DefaultRange;
                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                float parsed;
                if (parts.Length >= 2 && float.TryParse(parts[1], out parsed) && parsed > 0f) range = parsed;

                var human = Guards.LocalHuman;
                if (human == null)
                {
                    ConsoleWindow.Print("finddead: no local player in the world.", ConsoleColor.Yellow);
                    return;
                }
                Vector3 me = human.Position;

                // Every loaded body bag in the scene (the world streams, so distant ones simply
                // aren't loaded — within a few hundred metres they are).
                var bags = UnityEngine.Object.FindObjectsOfType<DynamicBodyBag>();
                var hits = new List<DynamicBodyBag>();
                foreach (var bag in bags)
                {
                    if (bag == null) continue;
                    if (Vector3.Distance(me, bag.Position) <= range) hits.Add(bag);
                }
                hits.Sort((a, b) =>
                    Vector3.Distance(me, a.Position).CompareTo(Vector3.Distance(me, b.Position)));

                ConsoleWindow.Print(
                    string.Format("finddead: {0} body bag(s) within {1:0}m of you.", hits.Count, range),
                    ConsoleColor.Cyan);

                foreach (var bag in hits)
                {
                    Vector3 p = bag.Position;
                    string name = null;
                    try { name = bag.PlayersDisplayName; } catch { }
                    if (string.IsNullOrEmpty(name)) name = "Unidentified";
                    float d = Vector3.Distance(me, p);
                    ConsoleWindow.Print(
                        string.Format("  {0}  @ ({1:0}, {2:0}, {3:0})  -  {4:0}m",
                            name, p.x, p.y, p.z, d),
                        ConsoleColor.White);
                }
                if (hits.Count == 0)
                    ConsoleWindow.Print(string.Format("  (none within {0:0}m)", range), ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("finddead failed: " + e.Message, ConsoleColor.Red);
            }
        }
    }

    /// <summary>Intercepts the console's <c>finddead</c> before vanilla parsing (so it doesn't
    /// print "unknown command"). Any other input passes straight through.</summary>
    [HarmonyPatch(typeof(Util.Commands.CommandLine), "Process", new[] { typeof(string) })]
    internal static class Patch_CommandLine_Process
    {
        private static bool Prefix(string input)
        {
            try
            {
                if (string.IsNullOrEmpty(input)) return true;
                string cmd = input.TrimStart();
                if (cmd.Equals("finddead", StringComparison.OrdinalIgnoreCase)
                    || cmd.StartsWith("finddead ", StringComparison.OrdinalIgnoreCase))
                {
                    FindDeadCommand.Run(cmd);
                    return false; // handled — skip the vanilla command dispatch
                }
            }
            catch { }
            return true;
        }
    }
}
