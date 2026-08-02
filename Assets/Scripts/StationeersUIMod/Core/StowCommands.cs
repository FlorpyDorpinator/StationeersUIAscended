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
    /// <para>Sub-commands (B4): <c>rules</c> also lists every rule; <c>export &lt;set&gt;</c> prints
    /// that Stow Profile's UIAP1 share code (and drops a copy in <c>StowProfiles/Export/</c>);
    /// <c>roundtrip</c> runs the codec self-test over the shipped sets, your active set and a
    /// synthetic torture case.</para>
    ///
    /// <para>Mutates NO GAME STATE: it reads the same accessors the router reads
    /// (<see cref="BagProfileStore"/>, <see cref="BagProfileGate"/>) and performs one read-only
    /// <see cref="InventoryScanner"/> pass. The single write anywhere in here is the explicit
    /// <c>export</c> text file, which is the point of that sub-command.</para>
    /// </summary>
    public static class StowCommands
    {
        public static void StowProfiles(string input)
        {
            try
            {
                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string sub = parts.Length >= 2 ? parts[1] : string.Empty;
                if (sub.Equals("export", StringComparison.OrdinalIgnoreCase))
                {
                    // Stow Profile names contain spaces, so everything after the sub-command is one name.
                    Export(parts.Length >= 3 ? string.Join(" ", parts, 2, parts.Length - 2) : null);
                    return;
                }
                if (sub.Equals("roundtrip", StringComparison.OrdinalIgnoreCase))
                {
                    RoundTrip();
                    return;
                }
                bool verbose = sub.IndexOf("rules", StringComparison.OrdinalIgnoreCase) >= 0;

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
                ConsoleWindow.Print("  (`stowprofiles export <name>` prints a share code; `stowprofiles roundtrip` self-tests the codec)",
                    ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("stowprofiles failed: " + e.Message, ConsoleColor.Red);
            }
        }

        // ---------- B4: share-code export ----------

        /// <summary>`stowprofiles export &lt;name&gt;` — the codec's diagnostic face. Prints the
        /// fingerprint, the length and the code itself (wrapped, because a console line is not 5000
        /// characters wide), and writes the same thing to <c>StowProfiles/Export/&lt;name&gt;.txt</c>
        /// so it can be shared without fighting the clipboard.</summary>
        private static void Export(string setName)
        {
            if (string.IsNullOrEmpty(setName)) setName = StowProfileStore.ActiveName;
            if (string.IsNullOrEmpty(setName))
            {
                ConsoleWindow.Print("stowprofiles export: name a Stow Profile (see the list above).", ConsoleColor.Yellow);
                return;
            }

            StowProfileDoc doc = DocFor(setName);
            if (doc == null)
            {
                ConsoleWindow.Print("stowprofiles export: no Stow Profile called '" + setName + "'.", ConsoleColor.Yellow);
                return;
            }

            string fingerprint;
            string code = StowShareCodec.Encode(doc, out fingerprint);
            if (code == null)
            {
                ConsoleWindow.Print("stowprofiles export: could not encode '" + setName + "' (see the log).", ConsoleColor.Red);
                return;
            }

            int rules = 0;
            for (int i = 0; i < doc.Profiles.Count; i++)
                if (doc.Profiles[i] != null) rules += doc.Profiles[i].RuleCount;

            ConsoleWindow.Print("stowprofiles export '" + doc.Name + "': " + doc.Profiles.Count
                + " bag profile(s), " + rules + " rule(s)", ConsoleColor.Cyan);
            ConsoleWindow.Print("  fingerprint " + fingerprint + "   code length " + code.Length + " chars",
                ConsoleColor.White);
            string file = StowShareCodec.WriteExportFile(doc.Name, code, fingerprint);
            ConsoleWindow.Print(file != null ? "  written to " + file : "  (could not write the .txt copy - see the log)",
                file != null ? ConsoleColor.Green : ConsoleColor.Yellow);
            for (int i = 0; i < code.Length; i += 100)
                ConsoleWindow.Print("  " + code.Substring(i, Math.Min(100, code.Length - i)), ConsoleColor.DarkGray);
        }

        /// <summary>The ACTIVE set is served from the live in-memory profile list (so an unsaved edit
        /// is in the code), anything else from its file.</summary>
        private static StowProfileDoc DocFor(string setName)
        {
            if (string.Equals(setName, StowProfileStore.ActiveName, StringComparison.OrdinalIgnoreCase)
                && StowProfileStore.Available)
            {
                return new StowProfileDoc
                {
                    Name = StowProfileStore.ActiveName,
                    Description = StowProfileStore.Active != null ? StowProfileStore.Active.Description : null,
                    Profiles = new List<BagProfile>(BagProfileStore.Profiles),
                };
            }
            List<string> names = StowProfileStore.ListNames();
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], setName, StringComparison.OrdinalIgnoreCase))
                    return new StowProfileDoc
                    {
                        Name = names[i],
                        Description = null,
                        Profiles = StowProfileStore.ProfilesOf(names[i]),
                    };
            return null;
        }

        // ---------- B4: codec round-trip self-test ----------

        /// <summary>`stowprofiles roundtrip` — encode, decode, compare STRUCTURALLY, for every shipped
        /// preset, the player's active set, and a synthetic torture document that exercises the parts
        /// real content does not: an empty set, a profile with no rules at all, a missing badge, and
        /// negative / zero / huge priorities (which is what the zigzag varint exists for).
        ///
        /// <para>This is the tester's tool as much as the developer's: "does sharing work on YOUR
        /// install" is one command, and a FAIL line names the exact field that differs.</para>
        /// Read-only — nothing is written and no game state is touched.</summary>
        private static void RoundTrip()
        {
            ConsoleWindow.Print("stowprofiles roundtrip: UIAP1 codec self-test", ConsoleColor.Cyan);
            int pass = 0, fail = 0;

            for (int i = 0; i < ShippedStowProfiles.Names.Length; i++)
            {
                StowProfileDoc doc = ShippedStowProfiles.Build(ShippedStowProfiles.Names[i]);
                if (One("shipped: " + ShippedStowProfiles.Names[i], doc)) pass++; else fail++;
            }

            if (StowProfileStore.Available)
            {
                StowProfileDoc active = DocFor(StowProfileStore.ActiveName);
                if (active != null) { if (One("active: " + active.Name, active)) pass++; else fail++; }
            }

            if (One("synthetic: edge cases", Torture())) pass++; else fail++;
            if (One("synthetic: empty set", new StowProfileDoc { Name = "Empty", Description = null })) pass++; else fail++;

            // Damaged-input cases: these must FAIL to decode, with a message, and never throw.
            int rejectFails = 0;
            rejectFails += Reject("not a code at all", "hello world");
            rejectFails += Reject("truncated body", TruncatedCode());
            rejectFails += Reject("empty", "");
            pass += 3 - rejectFails;
            fail += rejectFails;

            ConsoleWindow.Print("  " + pass + " passed, " + fail + " failed.",
                fail == 0 ? ConsoleColor.Green : ConsoleColor.Red);
        }

        private static bool One(string label, StowProfileDoc doc)
        {
            if (doc == null)
            {
                ConsoleWindow.Print("  FAIL " + label + " - could not build the document", ConsoleColor.Red);
                return false;
            }
            string fpOut;
            string code = StowShareCodec.Encode(doc, out fpOut);
            if (code == null)
            {
                ConsoleWindow.Print("  FAIL " + label + " - encode returned nothing", ConsoleColor.Red);
                return false;
            }
            string fpIn, error;
            int unknown;
            StowProfileDoc back = StowShareCodec.Decode(code, out fpIn, out error, out unknown);
            if (back == null)
            {
                ConsoleWindow.Print("  FAIL " + label + " - decode: " + error, ConsoleColor.Red);
                return false;
            }
            string difference;
            if (!StowShareCodec.Equivalent(doc, back, out difference))
            {
                ConsoleWindow.Print("  FAIL " + label + " - " + difference, ConsoleColor.Red);
                return false;
            }
            if (!string.Equals(fpOut, fpIn, StringComparison.Ordinal))
            {
                ConsoleWindow.Print("  FAIL " + label + " - fingerprint " + fpOut + " != " + fpIn, ConsoleColor.Red);
                return false;
            }
            int rules = 0;
            for (int i = 0; i < doc.Profiles.Count; i++)
                if (doc.Profiles[i] != null) rules += doc.Profiles[i].RuleCount;
            ConsoleWindow.Print(string.Format("  PASS {0,-30} {1,2} profile(s) {2,4} rule(s)  {3,6} chars  {4}{5}",
                label, doc.Profiles.Count, rules, code.Length, fpOut,
                unknown > 0 ? "  (" + unknown + " rule(s) this build does not recognise)" : ""),
                ConsoleColor.Green);
            return true;
        }

        /// <summary>A code that MUST be rejected. Returns 1 when it was wrongly accepted (i.e. a
        /// failure), 0 when it was correctly refused.</summary>
        private static int Reject(string label, string code)
        {
            string fp, error;
            int unknown;
            StowProfileDoc doc = StowShareCodec.Decode(code, out fp, out error, out unknown);
            if (doc != null)
            {
                ConsoleWindow.Print("  FAIL reject " + label + " - it was accepted", ConsoleColor.Red);
                return 1;
            }
            ConsoleWindow.Print("  PASS reject " + label + " - \"" + error + "\"", ConsoleColor.Green);
            return 0;
        }

        private static string TruncatedCode()
        {
            string fp;
            string code = StowShareCodec.Encode(ShippedStowProfiles.Build(ShippedStowProfiles.Starter), out fp);
            if (string.IsNullOrEmpty(code)) return "UIAP1-F-";
            return code.Substring(0, code.Length - 8);
        }

        /// <summary>The synthetic torture document: everything real content does not exercise.</summary>
        private static StowProfileDoc Torture()
        {
            var doc = new StowProfileDoc
            {
                Name = "Torture (2)",
                Description = "Edge cases: no rules, no badge, negative/zero/huge priorities, punctuation.",
                Profiles = new List<BagProfile>(),
            };
            doc.Profiles.Add(new BagProfile { Name = "No rules at all", Badge = null });
            var p = new BagProfile { Name = "Every kind", Badge = "EDGE" };
            p.Items.Add(new ItemRule { Prefab = "ItemWrench", Priority = 0 });
            p.Items.Add(new ItemRule { Prefab = "ItemNotARealPrefabName", Priority = -25 });
            p.Items.Add(new ItemRule { Prefab = "ItemKitDynamicMKIILiquidCanister", Priority = 999999 });
            p.UIAClasses.Add(new UIAClassRule { Name = "Electronics", Priority = 55 });
            p.UIAClasses.Add(new UIAClassRule { Name = "NotAUIAClass", Priority = 55 });
            p.SlotClasses.Add(new SlotClassRule { Name = "Battery", Priority = 60 });
            p.Categories.Add(new CategoryRule { Name = "Ices", Priority = 10 });
            doc.Profiles.Add(p);
            doc.Profiles.Add(new BagProfile { Name = "Name with (parens) and  spaces", Badge = "P&Q" });
            return doc;
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
