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

        /// <summary>`uiatutorial [edit|reset]` — the first-run tutorial coach, on demand.
        /// Bare: replay the tutorial (manual replays never auto-pause; the F10 header pause
        /// button is the affordance for stillness). `edit`: open it in the DEV TEXT EDITOR —
        /// headings/bodies become editable fields and Save writes per-step overrides to
        /// config/StationeersUIMod/Tutorial/TutorialText.xml, which win over the built-in copy.
        /// `reset`: delete every saved text override. Opens UI only — mutates no game state.
        /// Note: the coach opens under the console; close the console to use it.</summary>
        public static void UiaTutorial(string input)
        {
            try
            {
                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string sub = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "";
                if (sub == "reset")
                {
                    UI.Menu.Tutorial.TutorialTextStore.ResetAll();
                    ConsoleWindow.Print("uiatutorial: all step-text overrides deleted (built-in copy restored).", ConsoleColor.Green);
                    return;
                }
                if (!Guards.CanDraw())
                {
                    ConsoleWindow.Print("uiatutorial: not in a world (or the mod is disabled) - nothing to show.", ConsoleColor.Yellow);
                    return;
                }
                bool edit = sub == "edit";
                UI.Menu.Tutorial.TutorialCoach.Open(edit);
                ConsoleWindow.Print(edit
                    ? "uiatutorial: DEV EDIT mode open (close the console to use it). Save writes to config/StationeersUIMod/Tutorial."
                    : "uiatutorial: tutorial opened (close the console to use it). Subcommands: edit, reset.",
                    ConsoleColor.Cyan);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("uiatutorial failed: " + e.Message, ConsoleColor.Red);
            }
        }

        /// <summary>`uiadev [on|off]` — AUTHOR MODE (FlorpyDorp). Bare toggles; `on`/`off` are
        /// explicit. While it is on, the read-only gate that protects the HUD themes WE ship stands
        /// down, so they can be edited in place instead of only through a duplicate.
        ///
        /// Session-only by construction — see <see cref="UiaDevMode"/> for why that is the whole
        /// point rather than a missing feature: nothing persists it, so a player who finds this
        /// command cannot permanently put themselves in a state where their edits to a shipped theme
        /// fight the next update. Mutates no game state (a client-side authoring latch only).</summary>
        public static void UiaDev(string input)
        {
            try
            {
                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string sub = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "";
                bool on;
                if (sub.Length == 0) on = !UiaDevMode.Active;
                else if (sub == "on") on = true;
                else if (sub == "off") on = false;
                else
                {
                    ConsoleWindow.Print("usage: uiadev [on|off]   (no argument toggles)", ConsoleColor.Yellow);
                    return;
                }

                UiaDevMode.Active = on;
                // Turning it back OFF re-arms the one-per-theme "read-only" notice, so the very next
                // refused edit explains itself again instead of failing silently because this
                // session already showed that toast once (see HudProfileStore's latch).
                if (!on) Features.HudProfileStore.ClearReadOnlyNotices();

                ConsoleWindow.Print("uiadev: author mode " + (on ? "ON" : "OFF") + ".",
                    on ? ConsoleColor.Green : ConsoleColor.Cyan);
                ConsoleWindow.Print(on
                    ? "  shipped HUD themes are editable this session (F9 edits to them save again)."
                    : "  shipped HUD themes are read-only again - duplicate one to make it yours.",
                    ConsoleColor.White);
                if (on)
                    ConsoleWindow.Print("  this session only: it resets on restart and on every F6 reload.",
                        ConsoleColor.White);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("uiadev failed: " + e.Message, ConsoleColor.Red);
            }
        }

        // "uiareset" — see Patch_CommandLine_Process. A full nuke of every UIA on-disk file: the
        // whole config/StationeersUIMod tree (every HUD profile, bag profile, HUD icon, profiler
        // snapshot — everything under it) plus BOTH possible .cfg names (the live SLP one and the
        // legacy dev-shim one — see StationeersUIMod.OnLoaded's freshInstall detection for why two
        // names exist). This is the playtester-incident escape hatch: "my HUD/config is broken
        // beyond what any in-game reset button fixes, give me a clean slate". Read-only diagnostics
        // everywhere else in this file — this is the one deliberate exception, gated by a two-step
        // confirm: bare `uiareset` only PRINTS what it would delete; `uiareset confirm` actually
        // deletes it, and only within the SAME session that saw the bare command first (armed by
        // _resetArmed, cleared by ResetSessionState on world/hot-reload teardown — see HudSystem.
        // Shutdown — so a confirm primed in one dev session can never fire in the next).
        private static bool _resetArmed;

        /// <summary>Hot-reload / world-teardown reset for the two-step confirm arm above. Not
        /// destructive to reset (worst case: a primed confirm silently un-arms and the player has to
        /// type the bare command again) — every new static still gets a reset path, per project
        /// convention.</summary>
        internal static void ResetSessionState() { _resetArmed = false; }

        public static void UiaReset(string input)
        {
            try
            {
                bool confirm = input != null && input.IndexOf("confirm", StringComparison.OrdinalIgnoreCase) >= 0;
                string configTree = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "StationeersUIMod");
                string cfgMain = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.stationeersuimod.ui.cfg");
                string cfgLegacy = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "com.stationeersuimod.ui.scriptengine.cfg");

                if (!confirm)
                {
                    _resetArmed = true;
                    ConsoleWindow.Print("uiareset: THIS WILL DELETE:", ConsoleColor.Yellow);
                    ConsoleWindow.Print("  " + configTree + "  (every HUD/bag profile, icon, snapshot - everything under it)", ConsoleColor.White);
                    ConsoleWindow.Print("  " + cfgMain, ConsoleColor.White);
                    ConsoleWindow.Print("  " + cfgLegacy + "  (if present)", ConsoleColor.White);
                    ConsoleWindow.Print("Nothing has been touched yet. Run `uiareset confirm` (this session) to actually do it.", ConsoleColor.Yellow);
                    return;
                }

                if (!_resetArmed)
                {
                    ConsoleWindow.Print("uiareset: run `uiareset` (no argument) first to see what will be deleted.", ConsoleColor.Yellow);
                    return;
                }
                _resetArmed = false;

                int removed = 0;
                try
                {
                    if (System.IO.Directory.Exists(configTree)) { System.IO.Directory.Delete(configTree, true); removed++; }
                }
                catch (Exception e) { ConsoleWindow.Print("  config tree delete failed: " + e.Message, ConsoleColor.Red); }
                try
                {
                    if (System.IO.File.Exists(cfgMain)) { System.IO.File.Delete(cfgMain); removed++; }
                }
                catch (Exception e) { ConsoleWindow.Print("  main cfg delete failed: " + e.Message, ConsoleColor.Red); }
                try
                {
                    if (System.IO.File.Exists(cfgLegacy)) { System.IO.File.Delete(cfgLegacy); removed++; }
                }
                catch (Exception e) { ConsoleWindow.Print("  legacy cfg delete failed: " + e.Message, ConsoleColor.Red); }

                ConsoleWindow.Print("uiareset: done (" + removed + " target(s) removed). RESTART THE GAME for a clean slate.", ConsoleColor.Green);
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("uiareset failed: " + e.Message, ConsoleColor.Red);
            }
        }

        /// <summary>`hudfx [elementId]` — READ-ONLY dump of the steady-state style registry
        /// (<see cref="UI.Hud.HudStyleFx"/>) for ONE element, resolved at the style slot the HUD is
        /// currently rendering.
        ///
        /// With no argument it lists every element in the active HUD document (Id + type + style
        /// source), so you can copy an Id. The Id argument matches on a unique PREFIX — the
        /// generated ones are GUIDs and nobody is typing those in full.
        ///
        /// Per row it prints: the category, the canonical key, WHERE the value came from
        /// (global / own / shared) and what it resolves to. This is the diagnostic plan §6's risk
        /// table asks for: every hand-list deleted in Phases 2-5 is paired with a before/after dump
        /// diff per shipped profile, not just a screenshot. It mutates nothing — every read goes
        /// through the same accessors the renderer uses.</summary>
        public static void HudFx(string input)
        {
            try
            {
                var doc = Features.HudProfileStore.Active;
                if (doc == null || doc.Elements == null)
                {
                    ConsoleWindow.Print("hudfx: no active HUD profile.", ConsoleColor.Yellow);
                    return;
                }

                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string want = parts.Length >= 2 ? parts[1] : null;

                if (string.IsNullOrEmpty(want))
                {
                    ConsoleWindow.Print(
                        "hudfx: " + doc.Elements.Count + " element(s) in profile '" + (doc.Name ?? "?")
                        + "'. Pass an Id prefix to dump one.", ConsoleColor.Cyan);
                    foreach (var e in doc.Elements)
                    {
                        if (e == null) continue;
                        var slot = e.ResolveSlot(UI.Hud.HudElementView.LayoutSlot);
                        ConsoleWindow.Print(
                            string.Format("  {0,-38} {1,-18} {2}", e.Id ?? "(no id)", e.Type,
                                SourceLine(e, slot)),
                            ConsoleColor.White);
                    }
                    ConsoleWindow.Print(
                        "  registry: " + UI.Hud.HudStyleFx.All.Length + " style rows + "
                        + UI.Hud.HudStyleFx.Transitions.Length + " transition effects.",
                        ConsoleColor.White);
                    return;
                }

                UI.Hud.HudElementDef target = null;
                int matches = 0;
                foreach (var e in doc.Elements)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    if (!e.Id.StartsWith(want, StringComparison.OrdinalIgnoreCase)) continue;
                    matches++;
                    if (target == null) target = e;
                }
                if (matches == 0)
                {
                    ConsoleWindow.Print("hudfx: no element Id starts with '" + want + "'. Run `hudfx` for the list.",
                        ConsoleColor.Yellow);
                    return;
                }
                if (matches > 1)
                {
                    ConsoleWindow.Print("hudfx: '" + want + "' matches " + matches + " elements - be more specific.",
                        ConsoleColor.Yellow);
                    return;
                }

                var s = target.ResolveSlot(UI.Hud.HudElementView.LayoutSlot);
                bool custom = IsCustom(target, s);
                ConsoleWindow.Print(
                    string.Format("hudfx: {0} ({1})  slot {2}{3}  style {4}",
                        target.Id, target.Type, s,
                        target.ForksSlot(s) ? " [forked]" : " [shared base]",
                        custom ? "MIXED/OWN" : "ALL-GLOBAL"),
                    ConsoleColor.Cyan);
                // Phase 3: the per-category follow word, for the rendering slot AND for every slot
                // this element forks — a Bare fork can follow a family the Suited base owns.
                ConsoleWindow.Print("  styleSrc [" + s + "]  " + SourceLine(target, s),
                    ConsoleColor.Cyan);
                if (target.ForksSlot(UI.Hud.HudStyleSlot.Bare) && s != UI.Hud.HudStyleSlot.Bare)
                    ConsoleWindow.Print("  styleSrc [Bare]  "
                        + SourceLine(target, UI.Hud.HudStyleSlot.Bare), ConsoleColor.White);
                if (target.ForksSlot(UI.Hud.HudStyleSlot.Robot) && s != UI.Hud.HudStyleSlot.Robot)
                    ConsoleWindow.Print("  styleSrc [Robot]  "
                        + SourceLine(target, UI.Hud.HudStyleSlot.Robot), ConsoleColor.White);
                if (s != UI.Hud.HudStyleSlot.Base)
                    ConsoleWindow.Print("  styleSrc [Base]  "
                        + SourceLine(target, UI.Hud.HudStyleSlot.Base), ConsoleColor.White);
                ConsoleWindow.Print(
                    "  source: global = follows the F9 slider, own = this element stores it, "
                    + "donor = it reads ANOTHER element's, shared = one value for every panel.",
                    ConsoleColor.White);
                // Phase 4: every category in state D gets a line naming its donor and saying
                // whether the link actually resolves — dangling and depth-illegal donors print
                // WHY they degraded rather than silently reading as "global" three lines down.
                var followable = UI.Hud.HudStyleFx.Followable;
                for (int i = 0; i < followable.Length; i++)
                {
                    var fcat = followable[i];
                    if (UI.Hud.HudStyleFx.SourceOf(target, fcat, s) != UI.Hud.HudFxSource.Donor)
                        continue;
                    UI.Hud.HudElementDef donorDef;
                    var dstate = UI.Hud.HudStyleDonor.Evaluate(target, fcat, s, out donorDef);
                    string did = UI.Hud.HudStyleDonor.DonorIdOf(target, fcat, s);
                    ConsoleWindow.Print("  donor [" + fcat + "]  D -> "
                        + UI.Hud.HudStyleDonor.ShortId(did) + "   "
                        + UI.Hud.HudStyleDonor.DescribeState(dstate, donorDef, did),
                        dstate == UI.Hud.HudDonorState.Ok ? ConsoleColor.Green : ConsoleColor.Yellow);
                }

                var all = UI.Hud.HudStyleFx.All;
                UI.Hud.HudFxCategory? lastCat = null;
                for (int i = 0; i < all.Length; i++)
                {
                    var def = all[i];
                    if (def == null) continue;
                    if (!lastCat.HasValue || lastCat.Value != def.Category)
                    {
                        lastCat = def.Category;
                        ConsoleWindow.Print("  -- " + def.Category, ConsoleColor.Cyan);
                    }

                    // PER-ROW EFFECTIVE SOURCE, from this row's own CATEGORY rather than from one
                    // element-wide flag — the point of Phase 3, and what makes a mixed element
                    // legible in the dump. Phase 4: a row can now resolve THROUGH A DONOR, and the
                    // value column follows it there, so what is printed is what renders.
                    var rowSrc = UI.Hud.HudStyleFx.SourceOf(target, def.Category, s);
                    UI.Hud.HudStyleSlot rowSlot;
                    var rowDef = UI.Hud.HudElementView.StyleSrcDefFor(target, def.Category, s, out rowSlot);
                    bool rowOwn = rowDef != null;
                    string source;
                    if (def.SharedOnly) source = "shared";
                    else if (rowSrc == UI.Hud.HudFxSource.Donor)
                        source = rowDef != null ? "donor" : "donor(=global)";
                    else if (!def.HasGlobal) source = rowOwn ? "own-only" : "own-only(off)";
                    else if (rowOwn && def.HasOwnValue(target, s)) source = "own";
                    else if (rowOwn) source = "own(=global)";
                    else source = "global";

                    string flags = "";
                    if (def.SdfOnly) flags += " sdf";
                    if (def.LegacyApprox) flags += " approx";
                    if (def.DoesNotTravel) flags += " machine-local";
                    if (!def.TierOn) flags += " TIER-OFF";
                    else if (!def.MasterOn) flags += " off";

                    ConsoleWindow.Print(
                        string.Format("     {0,-20} {1,-14} {2,-10} (global {3}){4}",
                            def.Key, source,
                            rowDef != null ? def.ResolveText(rowDef, rowSlot, true) : def.GlobalText,
                            def.GlobalText, flags),
                        ConsoleColor.White);
                }

                // The seven power transitions are NOT in the style table (HudTransitionFx stays
                // their sole authority - see HudStyleFx's type comment), so they are dumped from
                // their own registry rather than duplicated into this one.
                // THE WHOLE TRANSITIONS BLOCK IS BASE-SLOT, header/mode/raw alike. The seven power
                // transitions are deliberately not per-tier: SetMode/SetAmount write raw base keys,
                // so their follow bit lives on Base too (HudStyleFx.SlotFor). Reading the header
                // from one slot and the modes from another is exactly the desync this dump exists
                // to catch, so it must not commit it itself.
                bool trOwn = UI.Hud.HudStyleFx.SourceOf(target, UI.Hud.HudFxCategory.Transitions,
                    UI.Hud.HudStyleSlot.Base) == UI.Hud.HudFxSource.Own;
                ConsoleWindow.Print("  -- Transitions (HudTransitionFx, slot Base - shared by every tier)  "
                    + (trOwn ? "own" : "global - stored modes are DORMANT"), ConsoleColor.Cyan);
                var fxs = UI.Hud.HudStyleFx.Transitions;
                for (int i = 0; i < fxs.Length; i++)
                {
                    var fx = fxs[i];
                    if (fx == null) continue;
                    // Both the EFFECTIVE mode (gated by the Transitions follow state) and the RAW
                    // stored one, so a dormant On/Off left behind by decision 5 is visible rather
                    // than silently absent.
                    var mode = UI.Hud.HudTransitionFx.ModeOf(target, fx, false);
                    var raw = UI.Hud.HudTransitionFx.RawModeOf(target, fx, UI.Hud.HudStyleSlot.Base);
                    string src = mode.ToString().ToLowerInvariant()
                        + (raw != mode ? " (stored " + raw.ToString().ToLowerInvariant() + ")" : "");
                    ConsoleWindow.Print(
                        string.Format("     {0,-20} {1,-14} {2,-10} (global {3})",
                            fx.Key, src,
                            UI.Hud.HudTransitionFx.Resolve(target, fx, false).ToString("0.###"),
                            fx.GlobalOn ? fx.GlobalAmt.ToString("0.###") : "OFF"),
                        ConsoleColor.White);
                }
            }
            catch (Exception e)
            {
                ConsoleWindow.Print("hudfx failed: " + e.Message, ConsoleColor.Red);
            }
        }

        /// <summary>Does this slot own ANY style family of its own? Phase 3's successor to the
        /// two-state test — it goes through HudStyleFx's packed word, so it can never drift from
        /// what the resolvers do.</summary>
        private static bool IsCustom(UI.Hud.HudElementDef d, UI.Hud.HudStyleSlot slot)
            => d != null && !UI.Hud.HudStyleFx.AllFollow(d, slot);

        /// <summary>"Surface=O Glass=G Edges=G Glow=O Transitions=G" — the per-category source
        /// letters for one slot (G global / D donor / O own).
        ///
        /// Goes through <c>HudStyleFx.SourceOf</c>, NOT through a raw unpack of this slot's word, so
        /// Transitions reports the Base bit the resolvers actually read. A fork's word can carry
        /// stale Transitions bits that nothing reads; printing them would make the dump disagree
        /// with the popup and with the renderer.</summary>
        private static string SourceLine(UI.Hud.HudElementDef d, UI.Hud.HudStyleSlot slot)
        {
            var sb = new System.Text.StringBuilder();
            var cats = UI.Hud.HudStyleFx.Followable;
            for (int i = 0; i < cats.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(cats[i]).Append('=')
                  .Append(UI.Hud.HudStyleFx.SourceLetter(UI.Hud.HudStyleFx.SourceOf(d, cats[i], slot)));
            }
            return sb.ToString();
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
                if (Matches(cmd, "uiareset")) { FinderCommands.UiaReset(cmd); return false; }
                if (Matches(cmd, "uiatutorial")) { FinderCommands.UiaTutorial(cmd); return false; }
                if (Matches(cmd, "uiadev")) { FinderCommands.UiaDev(cmd); return false; }
                if (Matches(cmd, "hudfx")) { FinderCommands.HudFx(cmd); return false; }
                if (Matches(cmd, "uiadiag"))
                {
                    // `uiadiag cursor` = focused pointer-flicker trace (cursor lock/visibility + every
                    // SetCursor call + CURSORFLIP corrections only, no world-highlight noise).
                    bool cursorOnly = cmd.IndexOf("cursor", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool on = CursorDiag.Toggle(cursorOnly);
                    ConsoleWindow.Print(
                        on ? ("uiadiag: " + (cursorOnly ? "CURSOR-ONLY " : "") + "trace ON - reproduce the flicker, then run `uiadiag` again to stop. "
                              + "Watch the BepInEx log (LogOutput.log) for SetCursor(...) and CURSORFLIP lines.")
                           : "uiadiag: trace OFF.",
                        on ? ConsoleColor.Green : ConsoleColor.Cyan);
                    return false;
                }
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
