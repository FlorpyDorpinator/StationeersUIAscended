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

        /// <summary>`uiaflash` — force the smart-stow "it went in here" flash onto every worn 1-6 box
        /// for one cycle, using the icon of whatever is in your hand (falling back to each box's own
        /// item). Pure diagnostic; mutates NO game state.
        ///
        /// It exists to BISECT this feature, which has now failed several times for reasons outside
        /// our code (a third-party mod owning smart-stow). If the boxes visibly swap icons, the
        /// renderer AND the SlotFlash registry are both proven good, so any real-stow failure is in
        /// the TRIGGER — i.e. who moved the item and whether we observed it. If nothing swaps, the
        /// fault is on the render side instead. One command, one answer.</summary>
        public static void FlashTest(string input)
        {
            try
            {
                var human = Guards.LocalHuman;
                if (human == null) { ConsoleWindow.Print("uiaflash: no local player in the world.", ConsoleColor.Yellow); return; }

                // Prefer the held item's icon, so the swap is unmistakable against the box's own art.
                Sprite icon = null;
                try
                {
                    var held = human.RightHandSlot != null ? human.RightHandSlot.Get() : null;
                    if (held == null && human.LeftHandSlot != null) held = human.LeftHandSlot.Get();
                    if (held != null) icon = held.GetThumbnail();
                }
                catch { }

                var slots = new List<Slot>();
                try
                {
                    slots.Add(human.HelmetSlot); slots.Add(human.GlassesSlot); slots.Add(human.SuitSlot);
                    slots.Add(human.BackpackSlot); slots.Add(human.UniformSlot); slots.Add(human.ToolbeltSlot);
                }
                catch { }

                int posted = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s == null) continue;
                    Sprite use = icon;
                    if (use == null) { try { var occ = s.Get(); if (occ != null) use = occ.GetThumbnail(); } catch { } }
                    if (use == null) continue;
                    SlotFlash.Post(s, use);
                    posted++;
                }

                ConsoleWindow.Print("uiaflash: posted " + posted + " box flash(es) - watch the 1-6 boxes now.", ConsoleColor.Cyan);
                if (posted == 0)
                    ConsoleWindow.Print("  (nothing to flash: hold an item, or wear some equipment)", ConsoleColor.Yellow);
                else
                    ConsoleWindow.Print("  swapped = renderer OK (any stow failure is in the trigger); no swap = renderer fault.", ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("uiaflash failed: " + e.Message, ConsoleColor.Red);
            }
        }

        /// <summary>`stowtrace` — dump the SmartStow+ ROUTER's full candidate table for the item
        /// currently in the ACTIVE hand: every enabled stage's best candidate in chain order, with
        /// stage tag, destination bag, depth, score and the human reason. Row one (marked `&gt;`)
        /// is exactly what a G press would execute. READ-ONLY by construction — it calls only
        /// <see cref="StowRouter.ResolveAll"/> (pure decision, pooled list read immediately, no
        /// mutation, no message); the MP-safe diagnostic for "why did my coal go in the food bag"
        /// bug reports. The caller-policy gates the router deliberately leaves to SmartStowPlus
        /// (master/enabled switches) are REPORTED rather than silently applied, so the trace never
        /// diverges from what G would do without saying so.</summary>
        public static void StowTrace(string input)
        {
            try
            {
                var human = Guards.LocalHuman;
                if (human == null)
                {
                    ConsoleWindow.Print("stowtrace: no local player in the world.", ConsoleColor.Yellow);
                    return;
                }

                Slot hand = null;
                try { hand = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot; } catch { }
                DynamicThing held = null;
                try { held = hand != null ? hand.Get() : null; } catch { }
                if (held == null)
                {
                    ConsoleWindow.Print("stowtrace: hold an item in the ACTIVE hand first.", ConsoleColor.Yellow);
                    return;
                }

                string itemName = StateText.Strip(SafeName(held));
                int depth = StowRouter.ConfiguredDepth();
                ConsoleWindow.Print(
                    string.Format("stowtrace: '{0}' (scan depth {1})", itemName, depth),
                    ConsoleColor.Cyan);

                bool gatedOff = false;
                try { gatedOff = !UIAConfig.MasterEnable.Value || !UIAConfig.SmartStowPlusEnabled.Value; }
                catch { }
                if (gatedOff)
                    ConsoleWindow.Print(
                        "  (SmartStow+ is currently DISABLED - G runs vanilla; rows show what the router WOULD pick)",
                        ConsoleColor.Yellow);

                // POOLED list: compose every output line before any further resolve happens.
                var results = StowRouter.ResolveAll(held, hand, depth);
                if (results.Count == 0)
                {
                    ConsoleWindow.Print("  no stage matched - vanilla Smart Stow handles this item.", ConsoleColor.White);
                    return;
                }
                for (int i = 0; i < results.Count; i++)
                {
                    var c = results[i];
                    string holder = c.Holder != null ? StateText.Strip(SafeName(c.Holder)) : "inventory";
                    ConsoleWindow.Print(
                        string.Format("  {0} {1,-8} -> {2}  (depth {3}, score {4}) - {5}",
                            i == 0 ? ">" : " ", StageTag(c.Stage), holder, c.Depth, c.Score, c.Reason),
                        i == 0 ? ConsoleColor.Green : ConsoleColor.White);
                }
                ConsoleWindow.Print("  > = what G would do; later rows are each stage's own best candidate.", ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("stowtrace failed: " + e.Message, ConsoleColor.Red);
            }
        }

        private static string SafeName(Thing t)
        {
            if (t == null) return "?";
            try
            {
                string n = t.DisplayName;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            try { return t.PrefabName ?? "?"; } catch { return "?"; }
        }

        private static string StageTag(StowStage s)
        {
            if (s == StowStage.BeltTool) return "BELT";
            if (s == StowStage.StackMerge) return "STACK";
            if (s == StowStage.FunctionalSocket) return "SOCKET";
            if (s == StowStage.Profile) return "PROFILE";
            if (s == StowStage.Affinity) return "AFFINITY";
            if (s == StowStage.BagDefault) return "DEFAULT";
            if (s == StowStage.Memory) return "MEMORY";
            if (s == StowStage.GenericFallback) return "GENERIC";
            if (s == StowStage.BeltFallback) return "BELT*";
            return "?";
        }

        // The large box shares the CardboardBox class with the small one; only the prefab
        // differs ("CardboardBoxLarge"), so filter on the prefab name.
        private static bool IsLarge(CardboardBox b)
        {
            var pn = b != null ? b.PrefabName : null;
            return !string.IsNullOrEmpty(pn)
                && pn.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>Intercepts our console commands before vanilla parsing (so no "unknown command");
    /// everything else passes straight through. This prefix is also WHY we never use the game's
    /// CommandLine.AddCommand: its static _commandsMap survives an F6 hot reload, rejects
    /// duplicate registration, and has no Remove — a stale entry would drive a dead assembly.</summary>
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
                if (Matches(cmd, "uiaprof")) { UiaProfCommand(cmd); return false; }
                if (Matches(cmd, "uiaflash")) { FinderCommands.FlashTest(cmd); return false; }
                if (Matches(cmd, "stowtrace")) { FinderCommands.StowTrace(cmd); return false; }
            }
            catch { }
            return true;
        }

        /// <summary>`uiaprof [on|off|clear|save|ab &lt;effect&gt;]` — the vendored profiler + the
        /// A/B cost driver. Read-only diagnostics; the ab subcommand toggles CONFIG values
        /// (client-side cosmetics only) and restores them when the run completes.</summary>
        private static void UiaProfCommand(string cmd)
        {
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "toggle";
            switch (sub)
            {
                case "on": ConsoleWindow.Print(Profiling.ProfilicusUniversalis.SetVisible(true), ConsoleColor.Cyan); break;
                case "off": ConsoleWindow.Print(Profiling.ProfilicusUniversalis.SetVisible(false), ConsoleColor.Cyan); break;
                case "toggle": ConsoleWindow.Print(Profiling.ProfilicusUniversalis.Toggle(), ConsoleColor.Cyan); break;
                case "clear": Profiling.ProfilicusUniversalis.Clear(); ConsoleWindow.Print("profiler window cleared.", ConsoleColor.Cyan); break;
                case "save": ConsoleWindow.Print(Profiling.ProfilicusUniversalis.SaveSnapshot(), ConsoleColor.Cyan); break;
                case "ab":
                    if (parts.Length >= 3) UiaAbDriver.Start(parts[2]);
                    else ConsoleWindow.Print("usage: uiaprof ab <effect>  —  effects: " + UiaAbDriver.KnownEffects, ConsoleColor.Yellow);
                    break;
                default:
                    ConsoleWindow.Print("usage: uiaprof [on|off|clear|save|ab <effect>]", ConsoleColor.Yellow);
                    break;
            }
        }

        private static bool Matches(string cmd, string name)
            => cmd.Equals(name, StringComparison.OrdinalIgnoreCase)
             || cmd.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase);
    }
}
