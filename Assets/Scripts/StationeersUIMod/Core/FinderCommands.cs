using System;
using System.Collections.Generic;
using Assets.Scripts;                 // DynamicBodyBag
using Assets.Scripts.Objects;         // Thing
using Assets.Scripts.Objects.Items;   // CardboardBox
using HarmonyLib;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Read-only console finders that list nearby world Things with their coordinates:
    ///   • <c>finddead</c>       — dead-player body bags (DynamicBodyBag)
    ///   • <c>findlargebox</c>   — large cardboard boxes (CardboardBox prefab'd *Large*), + name
    /// Both take an optional range in metres (default 1000): <c>findlargebox 2500</c>. Nothing
    /// is mutated — FindObjectsOfType filtered by distance from the local player, printed to the
    /// ConsoleWindow. Registered by intercepting CommandLine.Process (below).
    /// </summary>
    public static class FinderCommands
    {
        private const float DefaultRange = 1000f;

        /// <summary>Generic finder: every loaded T within range, nearest first, with coords.</summary>
        public static void Run<T>(string input, string noun, Func<T, bool> filter, Func<T, string> nameOf)
            where T : Thing
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
                    ConsoleWindow.Print("no local player in the world.", ConsoleColor.Yellow);
                    return;
                }
                Vector3 me = human.Position;

                var all = UnityEngine.Object.FindObjectsOfType<T>();
                var hits = new List<T>();
                foreach (var t in all)
                {
                    if (t == null) continue;
                    if (filter != null && !filter(t)) continue;
                    if (Vector3.Distance(me, t.Position) <= range) hits.Add(t);
                }
                hits.Sort((a, b) => Vector3.Distance(me, a.Position).CompareTo(Vector3.Distance(me, b.Position)));

                ConsoleWindow.Print(
                    string.Format("{0}: {1} found within {2:0}m of you.", noun, hits.Count, range),
                    ConsoleColor.Cyan);

                foreach (var t in hits)
                {
                    Vector3 p = t.Position;
                    string name = null;
                    try { name = nameOf != null ? nameOf(t) : null; } catch { }
                    if (string.IsNullOrEmpty(name)) name = "Unnamed";
                    name = StateText.Strip(name);
                    float d = Vector3.Distance(me, p);
                    ConsoleWindow.Print(
                        string.Format("  {0}  @ ({1:0}, {2:0}, {3:0})  -  {4:0}m", name, p.x, p.y, p.z, d),
                        ConsoleColor.White);
                }
                if (hits.Count == 0)
                    ConsoleWindow.Print(string.Format("  (none within {0:0}m)", range), ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print(noun + " failed: " + e.Message, ConsoleColor.Red);
            }
        }

        public static void FindDead(string input)
            => Run<DynamicBodyBag>(input, "finddead", null, b => b.PlayersDisplayName);

        public static void FindLargeBox(string input)
            => Run<CardboardBox>(input, "findlargebox", IsLarge, b => b.DisplayName);

        // The large box shares the CardboardBox class with the small one; only the prefab
        // differs ("CardboardBoxLarge"), so filter on the prefab name.
        private static bool IsLarge(CardboardBox b)
        {
            var pn = b != null ? b.PrefabName : null;
            return !string.IsNullOrEmpty(pn)
                && pn.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>Intercepts our finder commands before vanilla parsing (so no "unknown command");
    /// everything else passes straight through.</summary>
    [HarmonyPatch(typeof(Util.Commands.CommandLine), "Process", new[] { typeof(string) })]
    internal static class Patch_CommandLine_Process
    {
        private static bool Prefix(string input)
        {
            try
            {
                if (string.IsNullOrEmpty(input)) return true;
                string cmd = input.TrimStart();
                if (Matches(cmd, "finddead")) { FinderCommands.FindDead(cmd); return false; }
                if (Matches(cmd, "findlargebox")) { FinderCommands.FindLargeBox(cmd); return false; }
            }
            catch { }
            return true;
        }

        private static bool Matches(string cmd, string name)
            => cmd.Equals(name, StringComparison.OrdinalIgnoreCase)
             || cmd.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase);
    }
}
