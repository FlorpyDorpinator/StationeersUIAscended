using System;
using System.Collections.Generic;
using Assets.Scripts;                 // ConsoleWindow
using Assets.Scripts.Objects;
using HarmonyLib;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Grid;   // GridModel.IsStorageContainer — the shared "is real storage" gate

namespace StationeersUIMod.Core
{
    /// <summary>
    /// `stowprofiles` — READ-ONLY dump of the SmartStow Stow Profile model, in the shape of the
    /// existing `hudfx` diagnostic: what is active, what is inside it, and how every container you
    /// can currently reach resolves. It exists so "my bag stopped using its profile" can be answered
    /// from a log paste instead of a screen-share.
    ///
    /// <para>Sections: the ACTIVE Stow Profile and the others on disk; its Bag Profiles with a
    /// per-kind rule breakdown; every reachable container with its assignment and the VERDICT the
    /// router will reach for it (assigned / not assignable / not in this Stow Profile / excluded);
    /// and a tail count of stored assignments that name nothing in this Stow Profile, including ones
    /// on containers that are nowhere near you.</para>
    ///
    /// <para>Mutates nothing: it reads the same accessors the router reads
    /// (<see cref="BagProfileStore"/>, <see cref="BagProfileGate"/>) and performs one read-only
    /// <see cref="InventoryScanner"/> pass. No game state, no config write, no message.</para>
    /// </summary>
    public static class StowCommands
    {
        public static void StowProfiles(string input)
        {
            try
            {
                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool verbose = parts.Length >= 2
                    && parts[1].IndexOf("rules", StringComparison.OrdinalIgnoreCase) >= 0;

                // ---- header: the active Stow Profile + the rest on disk ----
                string active = StowProfileStore.ActiveName;
                if (!StowProfileStore.Available)
                {
                    ConsoleWindow.Print(
                        "stowprofiles: the Stow Profile model is NOT active - bag profiles came from the legacy "
                        + "Profiles folder. See the BepInEx log for why.", ConsoleColor.Yellow);
                }
                ConsoleWindow.Print(
                    "stowprofiles: active '" + (active ?? "(none)") + "'  -  "
                    + BagProfileStore.Profiles.Count + " bag profile(s)", ConsoleColor.Cyan);
                ConsoleWindow.Print("  folder: " + StowProfileStore.Dir, ConsoleColor.White);

                List<string> all = StowProfileStore.ListNames();
                if (all.Count == 0)
                {
                    ConsoleWindow.Print("  (no Stow Profile files on disk)", ConsoleColor.Yellow);
                }
                else
                {
                    var sb = new System.Text.StringBuilder("  on disk: ");
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(all[i]);
                        if (string.Equals(all[i], active, StringComparison.Ordinal)) sb.Append(" *");
                    }
                    ConsoleWindow.Print(sb.ToString(), ConsoleColor.White);
                }

                // ---- the active set's bag profiles ----
                ConsoleWindow.Print("  -- bag profiles in '" + (active ?? "?") + "'", ConsoleColor.Cyan);
                var profiles = BagProfileStore.Profiles;
                if (profiles.Count == 0)
                {
                    ConsoleWindow.Print("     (none - this Stow Profile is empty)", ConsoleColor.Yellow);
                }
                for (int i = 0; i < profiles.Count; i++)
                {
                    BagProfile p = profiles[i];
                    if (p == null) continue;
                    ConsoleWindow.Print(
                        string.Format("     {0,-24} {1,3} rules  (item {2}, uia {3}, slot {4}, cat {5})  tag {6}",
                            p.Name, p.RuleCount, p.Items.Count, p.UIAClasses.Count, p.SlotClasses.Count,
                            p.Categories.Count, BagProfileStore.ProfileTag(p.Name)),
                        ConsoleColor.White);
                    if (!verbose) continue;
                    for (int r = 0; r < p.Items.Count; r++)
                        ConsoleWindow.Print("        item  " + p.Items[r].Prefab + "  p" + p.Items[r].Priority, ConsoleColor.DarkGray);
                    for (int r = 0; r < p.UIAClasses.Count; r++)
                        ConsoleWindow.Print("        uia   " + p.UIAClasses[r].Name + "  p" + p.UIAClasses[r].Priority, ConsoleColor.DarkGray);
                    for (int r = 0; r < p.SlotClasses.Count; r++)
                        ConsoleWindow.Print("        slot  " + p.SlotClasses[r].Name + "  p" + p.SlotClasses[r].Priority, ConsoleColor.DarkGray);
                    for (int r = 0; r < p.Categories.Count; r++)
                        ConsoleWindow.Print("        cat   " + p.Categories[r].Name + "  p" + p.Categories[r].Priority, ConsoleColor.DarkGray);
                }

                // ---- how every reachable container resolves ----
                DumpContainers();

                // ---- stored assignments this Stow Profile cannot satisfy ----
                var stored = new Dictionary<long, string>();
                BagProfileStore.GetAssignments(stored);
                int dangling = 0;
                foreach (var kv in stored)
                    if (BagProfileStore.FindProfile(kv.Value) == null) dangling++;
                ConsoleWindow.Print(
                    "  " + stored.Count + " assignment(s) stored for this save; " + dangling
                    + " name a profile this Stow Profile does not have (kept, treated as unassigned).",
                    dangling > 0 ? ConsoleColor.Yellow : ConsoleColor.White);
                ConsoleWindow.Print("  excluded containers this save: " + BagProfileStore.ExcludedCount, ConsoleColor.White);
                if (!verbose)
                    ConsoleWindow.Print("  (`stowprofiles rules` also lists every rule)", ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("stowprofiles failed: " + e.Message, ConsoleColor.Red);
            }
        }

        /// <summary>Every storage container within the configured scan depth, with the verdict the
        /// router will reach for it. Deliberately broader than F10's bag list: an ineligible or
        /// excluded container is exactly what a player needs to SEE here, so the gate is reported
        /// rather than used as a filter.</summary>
        private static void DumpContainers()
        {
            ConsoleWindow.Print("  -- containers you can reach", ConsoleColor.Cyan);
            if (Guards.LocalHuman == null)
            {
                ConsoleWindow.Print("     (no local player in the world)", ConsoleColor.Yellow);
                return;
            }

            var seen = new List<DynamicThing>();
            try
            {
                foreach (var scanned in InventoryScanner.Scan(StowRouter.ConfiguredDepth(), false))
                {
                    DynamicThing occ = scanned.Occupant;
                    if (occ == null || !GridModel.IsStorageContainer(occ)) continue;
                    if (!seen.Contains(occ)) seen.Add(occ);
                }
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("     (scan failed: " + e.Message + ")", ConsoleColor.Red);
                return;
            }
            if (seen.Count == 0)
            {
                ConsoleWindow.Print("     (none)", ConsoleColor.White);
                return;
            }

            for (int i = 0; i < seen.Count; i++)
            {
                DynamicThing bag = seen[i];
                string name = SafeName(bag);
                string assigned = BagProfileStore.GetAssignedProfileName(bag);
                bool assignable = BagProfileGate.IsAssignableContainer(bag);
                bool excluded = BagProfileStore.IsStowExcluded(bag);

                string verdict;
                ConsoleColor colour = ConsoleColor.White;
                if (string.IsNullOrEmpty(assigned))
                {
                    verdict = assignable ? "(no profile - routes by contents/defaults)" : "(no profile; not assignable)";
                }
                else if (!assignable)
                {
                    verdict = "'" + assigned + "' NOT ASSIGNABLE - reads as unassigned";
                    colour = ConsoleColor.Yellow;
                }
                else if (BagProfileStore.FindProfile(assigned) == null)
                {
                    verdict = "'" + assigned + "' NOT IN THIS STOW PROFILE - reads as unassigned";
                    colour = ConsoleColor.Yellow;
                }
                else
                {
                    verdict = "'" + assigned + "' -> resolves";
                    colour = ConsoleColor.Green;
                }
                if (excluded)
                {
                    verdict += "   [EXCLUDED - never smart-stow here]";
                    colour = ConsoleColor.Yellow;
                }

                ConsoleWindow.Print(
                    string.Format("     {0,-26} ref {1,-8} {2}", StateText.Strip(name), SafeRefId(bag), verdict),
                    colour);
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

        private static string SafeRefId(Thing t)
        {
            try { return t != null ? t.ReferenceId.ToString() : "?"; } catch { return "?"; }
        }

        // ---------- self-installed console hook ----------

        // WIRING NOTE. Every other Harmony patch class in this mod is registered by name in
        // StationeersUIMod.OnLoaded's PatchHarness.TryPatchAll(...) list. That file was owned by a
        // concurrent work stream when this landed, so this one patch installs itself on its own
        // Harmony id from BagProfileStore.LoadProfiles (the init entry point this subsystem owns)
        // and unpatches in BagProfileStore.ResetRuntimeCaches, which the mod's Shutdown path already
        // calls — so a double-F6 leaves nothing behind, exactly like the shared harness would.
        // When StationeersUIMod.cs is free again, add typeof(Core.Patch_CommandLine_StowProfiles)
        // to that list and delete Install/Uninstall plus their two call sites.
        private static Harmony _harmony;

        internal static void Install()
        {
            if (_harmony != null) return;
            try
            {
                var h = new Harmony("com.stationeersuimod.stowcommands");
                h.CreateClassProcessor(typeof(Patch_CommandLine_StowProfiles)).Patch();
                _harmony = h;
            }
            catch (Exception e)
            {
                UIALog.Warn("stowprofiles console command unavailable: " + e.Message);
            }
        }

        internal static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception e) { UIALog.Warn("stowprofiles unpatch failed: " + e.Message); }
            _harmony = null;
        }
    }

    /// <summary>
    /// A SECOND prefix on <c>CommandLine.Process(string)</c>, alongside the one in FinderCommands.
    ///
    /// <para>They compose. Harmony runs prefixes in priority order and the ORIGINAL is skipped if
    /// any of them returns false; this one is pinned to <see cref="Priority.First"/> so it is
    /// evaluated before the other, and it returns true for everything that is not ours — so the
    /// other prefix still sees `finddead`, `hudfx`, `uiareset` and the rest exactly as before, and
    /// unrecognised input still reaches vanilla's parser. (Under the stricter classic-Harmony rule,
    /// where a false return also skips the remaining prefixes, the outcome is identical for the same
    /// reason: we only return false for our own command.)</para>
    /// </summary>
    [HarmonyPatch(typeof(Util.Commands.CommandLine), "Process", new[] { typeof(string) })]
    internal static class Patch_CommandLine_StowProfiles
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(string input)
        {
            try
            {
                if (string.IsNullOrEmpty(input)) return true;
                string cmd = input.TrimStart();
                if (cmd.Equals("stowprofiles", StringComparison.OrdinalIgnoreCase)
                    || cmd.StartsWith("stowprofiles ", StringComparison.OrdinalIgnoreCase))
                {
                    StowCommands.StowProfiles(cmd);
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
