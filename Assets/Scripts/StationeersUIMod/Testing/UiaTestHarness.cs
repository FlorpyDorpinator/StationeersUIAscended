using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Assets.Scripts;                  // ConsoleWindow, GameManager, CameraController
using Assets.Scripts.GridSystem;       // GameState
using Assets.Scripts.Inventory;        // InventoryManager
using Assets.Scripts.Networking;       // NetworkManager
using Assets.Scripts.Objects;          // Thing, DynamicThing, Slot, Prefab, Layers, Interactable
using Assets.Scripts.Objects.Entities; // Human
using Assets.Scripts.Objects.Items;    // Stackable
using HarmonyLib;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Grid;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
// OnServer, NetworkBase and KeyManager live in the GLOBAL namespace.

namespace StationeersUIMod.Testing
{
    /// <summary>
    /// <c>uiatest</c> — the in-game automated test harness (FlorpyDorp approved, 2026-09-25). It runs
    /// the play-test checklist of the 2026-09-25 fix wave ("Triage fix wave 1") as deterministic
    /// assertions inside a LIVE world, plus posed screenshots (see UiaTestShots.cs) that are judged
    /// by eye afterwards — reported <c>CAPTURED</c>, never PASS (a written PNG proves nothing about
    /// what it shows).
    ///
    /// <para><b>A DEVELOPER TOOL, GATED TWICE.</b> It refuses to start unless the session's
    /// <c>uiadev</c> author unlock is on (<see cref="Core.UiaDevMode.Active"/> — read here, never
    /// written) AND the world is single-player (below). The refusal tells a player what it would do to
    /// their save.</para>
    ///
    /// <para><b>THE ONE CONSOLE COMMAND THAT MUTATES GAME STATE — and therefore SINGLE-PLAYER ONLY.</b>
    /// Every other command in <c>Core/FinderCommands.cs</c> is a read-only diagnostic. This one spawns
    /// test items, moves them through the mod's own funnels and despawns them again, so it refuses to
    /// start — and aborts mid-run — unless vanilla's own "nobody else is in this world" test holds:
    /// <c>!NetworkManager.IsClient &amp;&amp; NetworkBase.Clients.Count == 0</c> (the exact gate vanilla
    /// uses before it will pause the world, 27758 InventoryManager.cs:187; the codebase's own copy is
    /// <c>Core/GamePause.CanOwnPause</c>). A client can join a HOSTED world mid-run, so the gate is
    /// re-read on every frame of the run (<see cref="AbortReason"/>) and the run aborts, restores and
    /// despawns the moment it fails. Before anything spawns, the area is checked for vanilla machine
    /// intakes that would swallow a loose test item (<see cref="IntakeNearby"/>).</para>
    ///
    /// <para><b>What a run may leave behind — and what it never does.</b> The HUD-profile POINTER (the
    /// config key the next launch loads) is never moved by a run: only the LIVE document is swapped
    /// (<c>HudProfileStore.SetActive</c>) to throwaway copies, and afterwards the player's profile is
    /// put back BY NAME through the store's own load — nothing is ever written under the player's
    /// profile name (<see cref="RestoreProfile"/>). A crash-recovery marker
    /// (<c>TestResults/.uiatest-active</c>) records the starting state before anything is switched and
    /// is consumed by the next run / <c>uiatest recover</c> (<see cref="RecoverFromMarker"/>). The
    /// per-world stores the copy save shares with the real one (same world key): HintUsage counters
    /// are snapshotted and rolled back through <c>HintUsageStore</c>'s own API; BeltBindings and
    /// StowHomes expose no removal API, so their residue is NAMED in the log instead
    /// (<see cref="RestorePerWorldStores"/>).</para>
    ///
    /// <para><b>Mutations go through the real funnels.</b> Test ACTIONS call the same public
    /// <c>ItemActions</c> paths players reach. Test FIXTURES (spawning, despawning) use vanilla's own
    /// server-side creative-spawn path — <c>OnServer.SpawnDynamicThingMaxStack</c>'s body (27758
    /// OnServer.cs:751-821: <c>Prefab.Find</c>, the DLC / NotSpawnable gates,
    /// <c>OnServer.Create&lt;T&gt;(prefab, pos, rot)</c> OnServer.cs:424, the max-stack fill) and its
    /// create-into-slot sibling <c>OnServer.Create&lt;T&gt;(prefab, slot)</c> (OnServer.cs:379, which
    /// lands through <c>OnServer.MoveToSlotOrWorld</c>); despawn is <c>OnServer.Destroy</c>
    /// (OnServer.cs:533 — the server-side destroy vanilla's own out-of-bounds cleanup and
    /// <c>DynamicThing.Recycle</c> use, GameManager.cs:1554 / DynamicThing.cs:1003). Exactly ONE place
    /// bypasses a gate on purpose: <see cref="LegacyTrap_DirectPlace_TestOnly"/>; and exactly one
    /// fixture writes a game field directly, <see cref="LockSlot_TestOnly"/> (it makes a TEST item's
    /// slot more restrictive, never less). Both refuse anything that is not test-spawned.</para>
    ///
    /// <para><b>Fail-soft, self-contained, hot-reload clean.</b> Each test runs in its own exception
    /// fence (<see cref="Test"/>); a throwing test is recorded as FAIL and the suite moves on. The
    /// coroutine host is a component on the plugin's own GameObject, so an F6 hot reload destroys it
    /// together with the plugin and <see cref="OnRunnerDestroyed"/> restores the HUD profile/theme,
    /// despawns every test item, unpatches the harness's own Harmony instance and resets every static.
    /// Nothing here subscribes to a game static event.</para>
    ///
    /// <para>Test-only access into the mod: a handful of <c>private</c> members were widened to
    /// <c>internal</c> (each one's doc comment names this harness) so the harness drives the real
    /// entry points without reflection. The exceptions are documented where they live: members of
    /// files another session is rewriting right now are read BY REFLECTION instead of widened (the F10
    /// Control Center internals, <c>ItemActions.SweepMayTouch</c>, <c>RadialHintBar.AllKinds</c>) so
    /// a rename degrades a test to SKIP rather than colliding with that work; and two Harmony hooks on
    /// the harness's own id (a drop-message counter on <c>ItemActions.DropToWorld</c> and the hover
    /// driver for the screenshots) are removed at the end of every run.</para>
    /// </summary>
    internal static partial class UiaTestHarness
    {
        internal static readonly string[] AllSuites =
        {
            "drop", "sealed", "sweep", "popup", "theme", "f10", "hints", "belt", "rangefinder", "shots",
        };

        /// <summary>Suites that create test items (in a slot or in the world). Any of them queued means
        /// the machine-intake check runs before the first spawn (the spawn helpers re-check lazily, so a
        /// suite missing from this list is still covered).</summary>
        private static readonly string[] SpawningSuites = { "drop", "sealed", "sweep", "hints", "belt", "shots" };

        private const string HarmonyId = "com.stationeersuimod.ui.uiatest";
        /// <summary>A throwaway HUD profile the theme / rangefinder / shots suites work in, so the
        /// player's own profiles are never edited. Deleted after every suite that makes it.</summary>
        private const string ScratchProfile = "uiatest-scratch";
        /// <summary>The live-document name a SHIPPED theme is shown under while the theme / shots suites
        /// cycle through them: an in-memory CLONE, so an autosave during the cycle can only ever land in
        /// this throwaway name — never in a shipped theme's file (the <c>uiadev</c> unlock this harness
        /// requires also lifts the shipped read-only gate) and never under the player's profile.</summary>
        private const string ThemeScratch = "uiatest-theme";
        /// <summary>The crash-recovery marker, in <see cref="ResultsRoot"/>.</summary>
        private const string MarkerFileName = ".uiatest-active";
        /// <summary>HintUsageStore's own folder name (Features/HintUsageStore.cs:45, private there) — used
        /// READ-ONLY here, to learn which counter kinds this world already stores.</summary>
        private const string HintUsageFolder = "HintUsage";
        /// <summary>No vanilla machine intake may sit within this many metres of the player or of the
        /// world-spawn point (safety review, spawn clearance; see <see cref="IntakeNearby"/>).</summary>
        private const float IntakeClearance = 3f;
        /// <summary>How far ahead the drop suite's floor spawn lands (vanilla's creative spawn distance).</summary>
        private const float WorldSpawnMeters = 1.2f;
        private const float ConsoleWaitSeconds = 180f;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The run-start banner: printed to the console when a run is queued and written at the
        /// top of the log. ASCII only (it reaches the console).</summary>
        private static readonly string[] Conditions =
        {
            "Run this on a COPY of your save: it spawns, moves and despawns TEST items in this world.",
            "Turn the game's autosave OFF for the run (an autosave mid-run would write the test items into the save).",
            "Stand on open floor, at least 3 m from any machine intake (chute inlet, recycler, furnace, vending machine...), facing open space - the run refuses otherwise.",
            "Do NOT build or copy a new mod DLL during the run: ScriptEngine hot-reloads on the copy and cuts the run off mid-step.",
            "Hands off mouse and keyboard until the 'uiatest done' notice.",
            "To abort safely at any time: `uiatest stop`, Esc (pause) or reopening the console - everything is restored and despawned.",
        };

        // ------------------------------------------------------------------ run state
        // Every static below is reset by ResetStatics (end of run AND runner teardown), so a double-F6
        // leaves nothing behind. The Unity objects they point at are destroyed by RestoreAll/DespawnAll.

        private static UiaTestRunner _runner;
        private static bool _running;
        /// <summary>Preflight has run: from here on the run may have touched state, so it ends through
        /// the full restore (<see cref="FinishRun"/>) rather than the quiet exit.</summary>
        private static bool _started;
        private static bool _stopRequested;
        private static string _abort;
        private static string _stamp;
        private static string _logPath;
        private static string _shotDir;
        private static float _runStartedAt;
        private static readonly StringBuilder _log = new StringBuilder(16384);
        private static readonly List<string> _results = new List<string>();
        /// <summary>Verdict counters. <c>_shotCount</c> = screenshots CAPTURED (never a pass);
        /// <c>_capturedOnly</c> = tests whose only outcome was screenshots; <c>_partial</c> = PASS/FAIL
        /// tests with NOT-proven or skipped parts.</summary>
        private static int _pass, _fail, _skip, _shotCount, _capturedOnly, _partial;

        private static readonly List<Thing> _spawned = new List<Thing>();
        private static readonly List<GameObject> _temp = new List<GameObject>();
        private static readonly List<PinnedInventoryWindow> _pins = new List<PinnedInventoryWindow>();

        private static Harmony _harmony;
        private static bool _dropHooked, _hoverHooked;
        private static int _dropCalls, _dropOk;
        private const int HoverOff = int.MinValue;
        private static int _hoverIndex = HoverOff;

        // restore registry (RestoreAll undoes exactly what was touched, in a safe order)
        private static bool _profileTouched, _scratchActive;
        private static string _origProfile;
        /// <summary>The player's own live document when the profile guard began — the no-disk fallback of
        /// <see cref="RestoreProfile"/> (reinstalled, never written). Null when the live document was not
        /// theirs.</summary>
        private static HudDocument _origDoc;
        /// <summary>Slots <see cref="LockSlot_TestOnly"/> locked; RestoreAll unlocks any still locked.</summary>
        private static readonly List<Slot> _lockedSlots = new List<Slot>();
        /// <summary>The machine-intake check passed for this run (<see cref="EnsureSpawnArea"/>).</summary>
        private static bool _spawnAreaChecked;
        private static List<HudDocument.ThemeEntry> _savedGlobals;
        private static bool _forceTierTouched;
        private static HudTier? _savedForceTier;
        private static bool _recentTouched;
        private static int _recentHash;
        private static string _recentName;
        private static Sprite _recentIcon;
        private static bool _hintsTouched;
        private static string _rfElementId;

        // per-world stores + the crash-recovery marker (Preflight records, FinishRun / teardown restore)
        /// <summary>SnapshotStores ran for this run (an intake abort in Preflight stops before it).</summary>
        private static bool _storesSnapshotted;
        private static bool _markerWritten;
        private static string _markerProfile;
        /// <summary>The per-world file key (Core.WorldKey) at run start; null = memory-only world.</summary>
        private static string _worldKeyAtStart;
        /// <summary>Hint-usage counters at run start (kind -> count); null = no snapshot.</summary>
        private static Dictionary<string, int> _hintSnap;
        /// <summary>The world key <see cref="_hintSnap"/> belongs to ("" = memory-only).</summary>
        private static string _hintSnapKey;
        /// <summary>The roll-back could not happen this run (the world key was gone) — the marker stays.</summary>
        private static bool _hintRestorePending;
        private static int _stowHomesAtStart = -1;
        /// <summary>ReferenceIds of TEST belts this run wore (the BeltBindings residue it may leave).</summary>
        private static readonly List<long> _testBeltRefs = new List<long>();

        /// <summary>The coroutine host: a bare component on the PLUGIN's GameObject. It carries no
        /// state; its only job besides hosting the run is <see cref="OnDestroy"/>, which is how the
        /// harness learns about an F6 hot reload / quit without the plugin having to call it.</summary>
        private sealed class UiaTestRunner : MonoBehaviour
        {
            private void OnDestroy() { OnRunnerDestroyed(this); }
        }

        // ================================================================== command surface

        /// <summary>Entry point from <c>Patch_CommandLine_Process</c> (Core/FinderCommands.cs).
        /// <c>uiatest</c> = status + usage; <c>uiatest all</c>; <c>uiatest &lt;suite&gt; [...]</c>;
        /// <c>uiatest stop</c>; <c>uiatest recover</c>. Never throws into the console.
        /// <para>Only a RUN is gated (uiadev + single-player): <c>stop</c> must always work, and
        /// <c>recover</c> only puts the player's own local state back (the HUD-profile pointer, the
        /// harness's scratch files, this world's client-side hint counters) — after a crash the
        /// session-only <c>uiadev</c> unlock is off again, and recovery must not wait for it.</para></summary>
        internal static void Command(string input)
        {
            try
            {
                var parts = (input ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) { PrintStatus(); return; }
                string first = parts[1].ToLowerInvariant();

                if (first == "stop")
                {
                    if (!_running) { Say("uiatest: nothing is running.", ConsoleColor.Yellow); return; }
                    _stopRequested = true;
                    Say(_started
                        ? "uiatest: stop requested - the current step is abandoned, then everything is restored and despawned."
                        : "uiatest: stop requested - the run has not started yet, so it ends without touching anything.",
                        ConsoleColor.Yellow);
                    return;
                }
                if (first == "recover")
                {
                    if (_running)
                    {
                        Say("uiatest: a run is in progress - its own cleanup restores everything (`uiatest stop` ends it).", ConsoleColor.Yellow);
                        return;
                    }
                    var report = new List<string>();
                    bool any = RecoverAll(report);
                    if (!any) { Say("uiatest recover: nothing to recover (no crash-recovery marker, HUD profile not on a test copy).", ConsoleColor.Green); return; }
                    for (int i = 0; i < report.Count; i++) Say("uiatest recover: " + report[i], ConsoleColor.Yellow);
                    return;
                }
                if (_running)
                {
                    Say("uiatest: a run is already in progress (`uiatest stop` aborts it).", ConsoleColor.Yellow);
                    return;
                }

                var suites = new List<string>();
                if (first == "all") suites.AddRange(AllSuites);
                else
                {
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string s = parts[i].ToLowerInvariant();
                        if (Array.IndexOf(AllSuites, s) < 0)
                        {
                            Say("uiatest: unknown suite '" + Ascii(parts[i]) + "'. Suites: " + string.Join(" ", AllSuites), ConsoleColor.Yellow);
                            return;
                        }
                        if (!suites.Contains(s)) suites.Add(s);
                    }
                    suites.Sort((a, b) => Array.IndexOf(AllSuites, a).CompareTo(Array.IndexOf(AllSuites, b)));
                }

                string why;
                if (!DevGate(out why))
                {
                    Say("uiatest: REFUSED - " + why + ".", ConsoleColor.Red);
                    Say("  uiatest is a DEVELOPER test tool, not a player feature. While it runs it spawns, moves and", ConsoleColor.Red);
                    Say("  despawns TEST items in THIS world (an autosave mid-run would write them into your save),", ConsoleColor.Red);
                    Say("  swaps your HUD to test copies of profiles/themes, and can leave small entries for its test", ConsoleColor.Red);
                    Say("  items in this world's UIA data (belt bindings, stow homes - named in its log).", ConsoleColor.Red);
                    Say("  Only ever run it on a COPY of a save. To proceed: `uiadev` (this session only), then `uiatest " + string.Join(" ", suites.ToArray()) + "`.", ConsoleColor.Red);
                    return;
                }
                if (!SinglePlayerGate(out why))
                {
                    Say("uiatest: REFUSED - " + why + ".", ConsoleColor.Red);
                    Say("  uiatest spawns and moves test items, so it only ever runs in a SINGLE-PLAYER world (nobody else connected).", ConsoleColor.Red);
                    return;
                }
                if (!WorldReady(out why)) { Say("uiatest: REFUSED - " + why + ".", ConsoleColor.Red); return; }
                var runner = EnsureRunner();
                if (runner == null) { Say("uiatest: REFUSED - the mod instance is not loaded.", ConsoleColor.Red); return; }

                _running = true;
                _started = false;
                _stopRequested = false;
                _abort = null;
                _stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", Inv);
                string root = ResultsRoot();
                _logPath = Path.Combine(root, _stamp + ".log");
                _shotDir = Path.Combine(Path.Combine(root, "shots"), _stamp);
                runner.StartCoroutine(RunMain(suites));

                Say("uiatest: queued [" + string.Join(" ", suites.ToArray()) + "] - gate: " + why + ".", ConsoleColor.Cyan);
                Say("  BEFORE YOU CLOSE THE CONSOLE:", ConsoleColor.Yellow);
                for (int i = 0; i < Conditions.Length; i++) Say("   * " + Conditions[i], ConsoleColor.Yellow);
                Say("  CLOSE THE CONSOLE to start. Then do not touch mouse or keyboard until the 'uiatest done' notice.", ConsoleColor.Cyan);
                Say("  Results print here and to " + _logPath, ConsoleColor.White);
            }
            catch (Exception e)
            {
                _running = false;
                _started = false;
                Say("uiatest failed to start: " + e.Message, ConsoleColor.Red);
            }
        }

        /// <summary>Safety review: the harness ships in the DLL, so a run needs the session's
        /// <c>uiadev</c> author unlock (<see cref="Core.UiaDevMode.Active"/>, Core/UiaDevMode.cs:28 —
        /// session-only by design: it dies with every restart and F6). Read only; never set here.</summary>
        private static bool DevGate(out string why)
        {
            bool on = false;
            try { on = Core.UiaDevMode.Active; } catch { on = false; }
            why = on ? "uiadev author mode is on" : "the `uiadev` author unlock is off for this session";
            return on;
        }

        private static void PrintStatus()
        {
            Say("uiatest - in-game DEVELOPER test harness for Stationeers UI Ascended (SINGLE-PLAYER ONLY: it spawns test items).", ConsoleColor.Cyan);
            string why;
            bool dev = DevGate(out why);
            Say("  dev gate: " + (dev ? "OK - " : "LOCKED - ") + why + (dev ? "" : " (type `uiadev` first)"), dev ? ConsoleColor.Green : ConsoleColor.Red);
            bool sp = SinglePlayerGate(out why);
            Say("  gate:     " + (sp ? "OK - " : "REFUSED - ") + why, sp ? ConsoleColor.Green : ConsoleColor.Red);
            try
            {
                string stamp = MarkerStamp();
                if (stamp != null && !_running)
                    Say("  recovery: a run (" + stamp + ") ended without its cleanup - `uiatest recover` puts back what it recorded (the next run does it first anyway).", ConsoleColor.Yellow);
            }
            catch { }
            bool world = WorldReady(out why);
            Say("  world:    " + (world ? "ready" : "NOT READY - " + why), world ? ConsoleColor.Green : ConsoleColor.Yellow);
            var h = Guards.LocalHuman;
            if (h != null)
            {
                bool hands = h.LeftHandSlot != null && h.RightHandSlot != null
                    && h.LeftHandSlot.Get() == null && h.RightHandSlot.Get() == null;
                Say("  hands:    " + (hands ? "both empty (item suites can spawn into them)"
                    : "NOT empty - drop/sealed/sweep/belt tests SKIP until both hands are empty"),
                    hands ? ConsoleColor.Green : ConsoleColor.Yellow);
                DynamicThing belt = h.ToolbeltSlot != null ? h.ToolbeltSlot.Get() : null;
                Say("  toolbelt: " + (belt != null ? "wearing '" + Ascii(belt.DisplayName) + "'"
                    : "none worn (the belt/hints suites wear a TEST belt, removed afterwards)"), ConsoleColor.White);
            }
            Say("  suites:   " + string.Join(" ", AllSuites), ConsoleColor.White);
            Say("  usage:    uiatest all | uiatest <suite> [suite ...] | uiatest stop | uiatest recover", ConsoleColor.White);
            Say("  results:  " + ResultsRoot() + "  (screenshots under shots/<timestamp>/)", ConsoleColor.White);
            if (_running) Say("  a run is IN PROGRESS.", ConsoleColor.Yellow);
        }

        /// <summary>Vanilla's own "alone in this world" test (27758 InventoryManager.cs:187, the gate
        /// before <c>WorldManager.SetGamePause(true)</c>; mirrored by Core/GamePause.CanOwnPause):
        /// not a client (<c>NetworkManager.IsClient</c>, NetworkManager.cs:55 — NetworkRole Client) and
        /// no connected clients (<c>NetworkBase.Clients</c>, NetworkBase.cs:195). A HOSTED world with
        /// nobody connected passes but is reported as such — the per-frame re-check aborts the run the
        /// moment anyone joins (a Steam P2P request can flip a plain single-player world to
        /// NetworkRole.Server mid-session, NetworkManager.cs:670-688). Fail-closed.</summary>
        internal static bool SinglePlayerGate(out string why)
        {
            try
            {
                if (NetworkManager.IsClient) { why = "you are a multiplayer CLIENT"; return false; }
                int clients = NetworkBase.Clients.Count;
                if (clients > 0) { why = clients + " client(s) connected to this world"; return false; }
                if (!GameManager.RunSimulation) { why = "this game is not the simulation authority"; return false; }
                why = NetworkManager.IsServer
                    ? "hosted world with nobody connected (the run aborts if anyone joins)"
                    : "single-player (no network session)";
                return true;
            }
            catch (Exception e)
            {
                why = "network state unreadable (" + e.GetType().Name + ")";
                return false;
            }
        }

        private static bool WorldReady(out string why)
        {
            try
            {
                if (!Guards.CanDraw())
                {
                    why = "not in a running world with the UI showing (load a world; the mod must be enabled; F1 must not hide the UI)";
                    return false;
                }
                if (Windows.HudEditorMode.Active) { why = "the F9 HUD editor is open - close it first"; return false; }
                if (Windows.RadialEditorMode.Active) { why = "the radial editor is open - close F10 first"; return false; }
                if (UI.Menu.Tutorial.TutorialCoach.IsOpen || UI.Menu.HandbookViewer.IsOpen)
                {
                    why = "the tutorial / handbook is open - close it first";
                    return false;
                }
                why = null;
                return true;
            }
            catch (Exception e)
            {
                why = "world state unreadable (" + e.GetType().Name + ")";
                return false;
            }
        }

        /// <summary>Null = keep going. Read every frame of the run by <see cref="Drive"/>.</summary>
        private static string AbortReason()
        {
            if (_stopRequested) return "stopped by `uiatest stop`";
            string why;
            if (!SinglePlayerGate(out why)) return "single-player gate: " + why;
            try
            {
                if (Assets.Scripts.Util.Singleton<GameManager>.IsQuitting) return "the game is quitting";
                if (GameManager.GameState != GameState.Running) return "the world stopped running (loading / menu)";
                if (InventoryManager.ParentHuman == null) return "no local player any more";
                // A pause keeps GameState Running (WorldManager.SetGamePause only flips IsGamePaused and
                // Time.timeScale, 27758 WorldManager.cs:1425-1438) but closes every radial.
                if (WorldManager.IsGamePaused) return "the game was paused (Esc menu?) mid-run";
                if (!Guards.CanDraw()) return "the UI stood down mid-run (hidden, or the mod was disabled)";
                if (ConsoleWindow.IsOpen) return "the console was reopened mid-run (it takes input and closes radials)";
                // The F9 editor targets the config's profile NAME (HudEditorMode.ActiveProfileName) while a
                // run shows a test copy as the live document - an edit made now could land the copy under
                // the player's name. The run never opens either editor, so this is always a human.
                if (Windows.HudEditorMode.Active) return "the F9 HUD editor was opened mid-run";
                if (Windows.RadialEditorMode.Active) return "the radial editor was opened mid-run";
            }
            catch (Exception e) { return "world state unreadable (" + e.GetType().Name + ")"; }
            return null;
        }

        private static UiaTestRunner EnsureRunner()
        {
            if (_runner != null) return _runner;
            var plugin = global::StationeersUIMod.StationeersUIMod.Instance;
            if (plugin == null) return null;
            _runner = plugin.gameObject.GetComponent<UiaTestRunner>();
            if (_runner == null) _runner = plugin.gameObject.AddComponent<UiaTestRunner>();
            return _runner;
        }

        private static string ResultsRoot()
            => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "StationeersUIMod"), "TestResults");

        // ================================================================== the run

        private static IEnumerator RunMain(List<string> suites)
        {
            _runStartedAt = Time.unscaledTime;
            LogLine("uiatest run " + _stamp + "  -  Stationeers UI Ascended " + global::StationeersUIMod.StationeersUIMod.VersionDisplay);
            LogLine("suites: " + string.Join(" ", suites.ToArray()));
            LogLine("conditions (printed to the console when the run was queued):");
            for (int i = 0; i < Conditions.Length; i++) LogLine("  * " + Conditions[i]);

            // The command is typed into the console, and the console must be CLOSED for the radial
            // (Guards.CanKeepRadialOpen) and for clean screenshots — so the run waits for it.
            float until = Time.unscaledTime + ConsoleWaitSeconds;
            while (_abort == null && !_stopRequested && ConsoleWindow.IsOpen)
            {
                if (Time.unscaledTime > until) { _abort = "the console was never closed (waited " + ConsoleWaitSeconds.ToString("0", Inv) + " s)"; break; }
                yield return null;
            }
            // Let the closing key's UP and the cursor re-lock settle before anything opens.
            float settle = Time.unscaledTime + 0.8f;
            while (_abort == null && !_stopRequested && Time.unscaledTime < settle) yield return null;

            // Safety review: NOTHING has been touched yet. A `uiatest stop` typed during the console wait
            // (or a console that was never closed) ends the run HERE - no Preflight (which recovers an old
            // marker, closes F10 and deletes stale scratch profiles), no restore pass, no log file.
            if (_stopRequested || _abort != null)
            {
                FinishBeforeStart(_stopRequested ? "stopped before it started (`uiatest stop`)" : _abort);
                yield break;
            }

            _started = true;
            try
            {
                Preflight(suites);
                for (int i = 0; i < suites.Count; i++)
                {
                    string s = suites[i];
                    if (_abort == null) { string w = AbortReason(); if (w != null) _abort = w; }
                    if (_abort != null) { LogLine(""); LogLine("== suite " + s + ": NOT RUN (" + _abort + ")"); continue; }

                    LogLine("");
                    LogLine("== suite " + s);
                    var res = new DriveResult();
                    var d = Drive(SuiteBody(s), res);
                    while (d.MoveNext()) yield return d.Current;
                    if (res.Error != null)
                    {
                        LogLine("   suite '" + s + "' setup threw: " + Describe(res.Error));
                        _fail++;
                        _results.Add("[FAIL] " + s + " (suite setup threw: " + res.Error.GetType().Name + ")");
                    }
                    SuiteCleanup();
                    // Deferred destroys (OnServer.Destroy -> Unity Destroy) land at end of frame: give them
                    // time so the next suite reads empty hands again.
                    for (int f = 0; f < 5; f++) yield return null;
                }
            }
            finally
            {
                FinishRun();
            }
        }

        private static IEnumerator SuiteBody(string suite)
        {
            switch (suite)
            {
                case "drop": return SuiteDrop();
                case "sealed": return SuiteSealed();
                case "sweep": return SuiteSweep();
                case "popup": return SuitePopup();
                case "theme": return SuiteTheme();
                case "f10": return SuiteF10();
                case "hints": return SuiteHints();
                case "belt": return SuiteBelt();
                case "rangefinder": return SuiteRangefinder();
                case "shots": return SuiteShots();
            }
            return null;
        }

        private static void Preflight(List<string> suites)
        {
            string why;
            SinglePlayerGate(out why);
            string dev;
            DevGate(out dev);
            LogLine("gate: " + why + "; dev gate: " + dev);
            CloseRadialsQuietly();

            // 1) A run that never reached its own cleanup (crash / quit / power loss) left its marker: put
            //    back what it recorded BEFORE this run records its own starting state or switches anything.
            var rec = new List<string>();
            try { RecoverAll(rec); }
            catch (Exception e) { rec.Add("crash recovery failed: " + e.Message); }
            for (int i = 0; i < rec.Count; i++)
            {
                LogLine("preflight: " + rec[i]);
                Say("  uiatest preflight: " + rec[i], ConsoleColor.Yellow);
            }

            // 2) Nothing this run puts in the world may land near a machine intake (checked before the
            //    first spawn; the spawn helpers re-check lazily for any suite missing from the list).
            bool spawns = false;
            for (int i = 0; i < suites.Count; i++)
                if (Array.IndexOf(SpawningSuites, suites[i]) >= 0) spawns = true;
            if (spawns && !EnsureSpawnArea(null))
            {
                LogLine("preflight: " + _abort);
                return;
            }

            // 3) This run's starting state + the crash-recovery marker - BEFORE any profile switch.
            SnapshotStores();
            WriteMarker();

            try
            {
                if (UI.Menu.UiaControlCenter.IsOpen)
                {
                    UI.Menu.UiaControlCenter.Close();
                    LogLine("preflight: closed the F10 Control Center");
                }
            }
            catch (Exception e) { LogLine("preflight: F10 close failed: " + e.Message); }

            try
            {
                DeleteHarnessProfiles(null);   // stale copies from an interrupted run (no-op if absent)
                if (HudProfileStore.HasPendingSave)
                    LogLine("preflight: note - the active HUD profile has unsaved edits; the first test switch flushes them to it as usual");
            }
            catch (Exception e) { LogLine("preflight: scratch cleanup failed: " + e.Message); }

            var h = Guards.LocalHuman;
            if (h != null)
            {
                LogLine("player: tier " + (HudSystem.LastSnapshot != null ? HudSystem.LastSnapshot.Tier.ToString() : "?")
                    + ", hands " + (HandsAreEmpty() ? "empty" : "NOT empty")
                    + ", toolbelt " + (h.ToolbeltSlot != null && h.ToolbeltSlot.Get() != null ? "worn" : "none")
                    + ", active HUD profile '" + Ascii(HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : "?") + "'");
            }
        }

        private static void SuiteCleanup()
        {
            RestoreAll();
            int n = DespawnAll();
            if (n > 0) LogLine("   (despawned " + n + " test item(s))");
        }

        /// <summary>The end of a run that STARTED (Preflight ran). Order matters: restore + despawn first,
        /// then the per-world stores — by then every radial has been closed for at least the five
        /// settle frames each suite ends with, so the key-hint strip has already booked its last session
        /// (RadialHintBar.UpdateSession adds exposures on the frame AFTER a ring closes) and the roll-back
        /// cannot be overtaken by it.</summary>
        private static void FinishRun()
        {
            try { RestoreAll(); }
            catch (Exception e) { LogLine("restore failed: " + e.Message); }
            int n = 0;
            try { n = DespawnAll(); }
            catch (Exception e) { LogLine("despawn failed: " + e.Message); }
            try { Unhook(); }
            catch (Exception e) { LogLine("unhook failed: " + e.Message); }
            var notes = new List<string>();
            try { RestorePerWorldStores(notes); }
            catch (Exception e)
            {
                notes.Add("per-world restore failed: " + e.Message);
                _hintRestorePending = _hintSnap != null && !string.IsNullOrEmpty(_hintSnapKey);
            }
            FinishMarker(notes);

            float secs = Time.unscaledTime - _runStartedAt;
            LogLine("");
            if (notes.Count > 0)
            {
                LogLine("after the run:");
                for (int i = 0; i < notes.Count; i++) LogLine("  " + notes[i]);
            }
            if (_abort != null) LogLine("RUN ABORTED: " + _abort);
            LogLine("totals: " + _pass + " pass, " + _fail + " fail, " + _skip + " skip"
                + (_capturedOnly > 0 ? ", " + _capturedOnly + " captured-only" : "")
                + (_partial > 0 ? "  (" + _partial + " of the pass/fail tests only PARTLY proven - see their NOT PROVEN / skip lines)" : "")
                + "  -  " + _shotCount + " screenshot(s) CAPTURED (judge by eye - never counted as a pass)"
                + "  -  " + n + " test item(s) despawned  -  " + secs.ToString("0.0", Inv) + " s");
            WriteLog();

            Say("", ConsoleColor.White);
            Say("uiatest done: " + _pass + " PASS, " + _fail + " FAIL, " + _skip + " SKIP, " + _shotCount + " CAPTURED"
                + (_partial > 0 ? "  (" + _partial + " partly proven)" : "")
                + (_abort != null ? "  (ABORTED: " + _abort + ")" : ""),
                _fail > 0 || _abort != null ? ConsoleColor.Red : ConsoleColor.Green);
            for (int i = 0; i < _results.Count; i++)
            {
                string r = _results[i];
                ConsoleColor c = r.StartsWith("[PASS]", StringComparison.Ordinal) ? ConsoleColor.Green
                    : r.StartsWith("[FAIL]", StringComparison.Ordinal) ? ConsoleColor.Red
                    : r.StartsWith("[CAPTURED]", StringComparison.Ordinal) ? ConsoleColor.Cyan : ConsoleColor.Yellow;
                Say("  " + r, c);
            }
            for (int i = 0; i < notes.Count; i++) Say("  after the run: " + notes[i], ConsoleColor.Yellow);
            Say("  full log: " + _logPath, ConsoleColor.White);
            if (_shotCount > 0) Say("  screenshots: " + _shotDir + "  (" + _shotCount + " CAPTURED - judge them by eye)", ConsoleColor.White);
            try
            {
                global::StationeersUIMod.Overlay.Toast.Show(
                    "uiatest done: " + _pass + " pass / " + _fail + " fail / " + _skip + " skip / " + _shotCount + " captured - open the console",
                    _fail > 0 || _abort != null
                        ? global::StationeersUIMod.Overlay.Theme.Warn
                        : global::StationeersUIMod.Overlay.Theme.Good, 6f);
            }
            catch { }
            ResetStatics(keepRunner: true);
        }

        /// <summary>The end of a run that never STARTED (stopped, or the console was never closed, during
        /// the console wait): nothing was touched, so there is nothing to restore - one line, no log.</summary>
        private static void FinishBeforeStart(string reason)
        {
            Say("uiatest: " + (reason ?? "ended") + " - nothing was touched.", ConsoleColor.Yellow);
            ResetStatics(keepRunner: true);
        }

        /// <summary>F6 hot reload / quit: the plugin's GameObject (and this host with it) is being
        /// destroyed. Restores and despawns what a live run touched — the profile restore is careful
        /// not to rebuild HUD objects in a store that already shut down (see RestoreProfile) — rolls the
        /// per-world stores back (or leaves the marker for the next run), then unpatches and resets every
        /// static.</summary>
        private static void OnRunnerDestroyed(UiaTestRunner r)
        {
            if (!ReferenceEquals(r, _runner)) return;
            try
            {
                if (_running && !_started)
                {
                    // Still in the console wait: nothing was touched, so there is nothing to restore.
                }
                else if (_running)
                {
                    LogLine("");
                    LogLine("RUN INTERRUPTED (the mod was unloaded / hot-reloaded mid-run) - emergency restore:");
                    RestoreAll();
                    int n = DespawnAll();
                    LogLine("  despawned " + n + " test item(s)");
                    // The per-world roll-back runs through the stores' own API even mid-teardown (fenced); if
                    // it cannot, the marker stays and the next run in this world finishes it.
                    var notes = new List<string>();
                    try { RestorePerWorldStores(notes); }
                    catch (Exception e)
                    {
                        notes.Add("per-world restore failed: " + e.Message);
                        _hintRestorePending = _hintSnap != null && !string.IsNullOrEmpty(_hintSnapKey);
                    }
                    FinishMarker(notes);
                    for (int i = 0; i < notes.Count; i++) LogLine("  " + notes[i]);
                    WriteLog();
                }
                else if (_spawned.Count > 0) DespawnAll();
            }
            catch (Exception e)
            {
                try { UIALog.Warn("uiatest teardown: " + e.Message); } catch { }
            }
            finally
            {
                try { Unhook(); } catch { }
                ResetStatics(keepRunner: false);
            }
        }

        private static void ResetStatics(bool keepRunner)
        {
            if (!keepRunner) _runner = null;
            _running = false;
            _started = false;
            _stopRequested = false;
            _abort = null;
            _stamp = null;
            _logPath = null;
            _shotDir = null;
            _runStartedAt = 0f;
            _log.Length = 0;
            _results.Clear();
            _pass = _fail = _skip = _shotCount = _capturedOnly = _partial = 0;
            _spawned.Clear();
            _temp.Clear();
            _pins.Clear();
            _harmony = null;
            _dropHooked = _hoverHooked = false;
            _dropCalls = _dropOk = 0;
            _hoverIndex = HoverOff;
            _profileTouched = _scratchActive = false;
            _origProfile = null;
            _origDoc = null;
            _lockedSlots.Clear();
            _spawnAreaChecked = false;
            _savedGlobals = null;
            _forceTierTouched = false;
            _savedForceTier = null;
            _recentTouched = false;
            _recentHash = 0;
            _recentName = null;
            _recentIcon = null;
            _hintsTouched = false;
            _rfElementId = null;
            _storesSnapshotted = false;
            _markerWritten = false;
            _markerProfile = null;
            _worldKeyAtStart = null;
            _hintSnap = null;
            _hintSnapKey = null;
            _hintRestorePending = false;
            _stowHomesAtStart = -1;
            _testBeltRefs.Clear();
            ResetShotStatics();
        }

        // ================================================================== crash recovery + per-world stores

        private static string MarkerPath() => Path.Combine(ResultsRoot(), MarkerFileName);

        /// <summary>The stamp of a marker left by an earlier run, or null (status line).</summary>
        private static string MarkerStamp()
        {
            string path = MarkerPath();
            if (!File.Exists(path)) return null;
            Dictionary<string, string> kv;
            Dictionary<string, int> hints;
            if (!ReadMarker(path, out kv, out hints)) return "unreadable marker";
            string s;
            return kv.TryGetValue("stamp", out s) && !string.IsNullOrEmpty(s) ? s : "?";
        }

        /// <summary>Safety review (crash recovery): written in Preflight BEFORE the first profile switch,
        /// deleted by the run's own cleanup. If the game dies mid-run the file survives, and the next
        /// run's Preflight (or <c>uiatest recover</c>) puts back what it records: the player's profile
        /// NAME (only if the pointer was left on a harness copy - a pre-marker build could leave it
        /// there; this build never moves it), and the world's hint-usage counters as they were. The
        /// snapshot lives IN the marker (key=value text, UTF-8) - there are no separate backup files.
        /// A failed write is logged and the run continues: the pointer is never moved, so the marker only
        /// matters for the counters.</summary>
        private static void WriteMarker()
        {
            try
            {
                var sb = new StringBuilder(512);
                sb.Append("# uiatest crash-recovery marker (Stationeers UI Ascended). Written when a run starts, deleted when it ends.\r\n");
                sb.Append("# If this file exists, a run ended without its cleanup: the next `uiatest` run (or `uiatest recover`)\r\n");
                sb.Append("# puts back what it records. Safe to delete by hand once you have checked your HUD profile.\r\n");
                sb.Append("version=1\r\n");
                sb.Append("stamp=").Append(_stamp ?? "").Append("\r\n");
                sb.Append("profile=").Append(_markerProfile ?? "").Append("\r\n");
                sb.Append("world=").Append(_hintSnapKey ?? "").Append("\r\n");
                if (_hintSnap != null)
                    foreach (var kv in _hintSnap)
                        sb.Append("hint:").Append(kv.Key).Append('=').Append(kv.Value.ToString(Inv)).Append("\r\n");
                Directory.CreateDirectory(ResultsRoot());
                File.WriteAllText(MarkerPath(), sb.ToString(), new UTF8Encoding(false));
                _markerWritten = true;
                LogLine("preflight: crash-recovery marker written (" + MarkerPath() + ")");
            }
            catch (Exception e)
            {
                LogLine("preflight: could not write the crash-recovery marker (" + e.Message + ") - a crash mid-run could not roll the hint counters back");
            }
        }

        private static bool ReadMarker(string path, out Dictionary<string, string> kv, out Dictionary<string, int> hints)
        {
            kv = new Dictionary<string, string>(StringComparer.Ordinal);
            hints = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i];
                    if (string.IsNullOrEmpty(l) || l[0] == '#') continue;
                    int eq = l.IndexOf('=');   // the FIRST '=': keys never hold one, values (names) may
                    if (eq <= 0) continue;
                    string k = l.Substring(0, eq).Trim();
                    string v = l.Substring(eq + 1).Trim();
                    if (k.StartsWith("hint:", StringComparison.Ordinal))
                    {
                        int n;
                        if (int.TryParse(v, NumberStyles.Integer, Inv, out n) && n >= 0 && k.Length > 5) hints[k.Substring(5)] = n;
                    }
                    else kv[k] = v;
                }
                return kv.ContainsKey("version");
            }
            catch { return false; }
        }

        /// <summary>Delete the marker at the end of a run - unless the hint roll-back could not happen
        /// (the world key was gone, e.g. the player left the world mid-run): then it stays, holding the
        /// snapshot for the next run in that world.</summary>
        private static void FinishMarker(List<string> notes)
        {
            if (!_markerWritten) return;
            if (_hintRestorePending)
            {
                notes.Add("the crash-recovery marker stays (" + MarkerPath() + ") until that roll-back happens");
                return;
            }
            try { if (File.Exists(MarkerPath())) File.Delete(MarkerPath()); }
            catch (Exception e) { notes.Add("could not delete the crash-recovery marker: " + e.Message); }
            _markerWritten = false;
        }

        /// <summary>Everything a crashed run can have left: a marker (see <see cref="RecoverFromMarker"/>),
        /// or - from a build before the marker existed - a HUD-profile pointer left on a harness copy.
        /// True when anything was found (the report says what was done).</summary>
        private static bool RecoverAll(List<string> report)
        {
            bool marker = RecoverFromMarker(report);
            if (!marker) marker = RecoverProfilePointer(null, report);
            return marker;
        }

        /// <summary>Put back what a marker recorded: (1) the profile pointer, only when it still points at a
        /// harness copy (a profile the player picked since is never overridden) - and the live document with
        /// it, by NAME; (2) the harness's scratch profiles; (3) the world's hint-usage counters, only in the
        /// SAME world and only if nothing went DOWN since (a deliberate reset is never undone). Then the
        /// marker goes - except when no world is loaded yet (the counters wait for it). Never throws.</summary>
        private static bool RecoverFromMarker(List<string> report)
        {
            string path = MarkerPath();
            if (!File.Exists(path)) return false;
            Dictionary<string, string> kv;
            Dictionary<string, int> hints;
            if (!ReadMarker(path, out kv, out hints))
            {
                report.Add("the crash-recovery marker is unreadable - removed (" + path + ")");
                try { File.Delete(path); } catch { }
                return true;
            }
            string stamp, profile, world;
            kv.TryGetValue("stamp", out stamp);
            kv.TryGetValue("profile", out profile);
            kv.TryGetValue("world", out world);
            report.Add("found the crash-recovery marker of run " + (string.IsNullOrEmpty(stamp) ? "?" : stamp) + " - putting back what it recorded:");

            RecoverProfilePointer(profile, report);
            DeleteHarnessProfiles(report);

            bool keep = false;
            if (hints.Count > 0 && !string.IsNullOrEmpty(world))
            {
                bool inWorld = false;
                try { inWorld = GameManager.GameState == GameState.Running; } catch { inWorld = false; }
                string key = null;
                bool haveKey = false;
                try { haveKey = WorldKey.TryGet(out key); } catch { haveKey = false; }
                if (!inWorld)
                {
                    keep = true;
                    report.Add("hint usage: no world is loaded - load world '" + world + "' and run `uiatest recover` there (the marker stays until then)");
                }
                else if (haveKey && string.Equals(key, world, StringComparison.Ordinal))
                    report.Add("hint usage (HintUsage/" + world + ".xml): " + RestoreHintCounts(hints));
                else
                    report.Add("hint usage: NOT rolled back - the interrupted run was in world '" + world + "', this is "
                        + (haveKey ? "'" + key + "'" : "an unsaved world with no key yet") + ". HintUsage/" + world
                        + ".xml may keep exposure counts from that run (the key-hint strip fades a little sooner there; F10 > Radial > Reset hint counters in that world clears them)");
            }
            if (!keep)
            {
                try { File.Delete(path); report.Add("crash-recovery marker removed"); }
                catch (Exception e) { report.Add("could not remove the crash-recovery marker: " + e.Message); }
            }
            return true;
        }

        private static bool IsHarnessProfileName(string name)
        {
            return string.Equals(name, ScratchProfile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ThemeScratch, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A HUD-profile pointer left on a harness copy goes back to <paramref name="recorded"/>
        /// (the marker's profile) or, without one, the shipped default; a live document that is a harness
        /// copy is replaced by the pointer's profile BY NAME (<see cref="ActivateByName"/>, which writes
        /// nothing). Anything else is left alone. True when it changed something.</summary>
        private static bool RecoverProfilePointer(string recorded, List<string> report)
        {
            bool did = false;
            var cfg = HudConfig.HudActiveProfile;
            if (cfg == null) return false;
            if (IsHarnessProfileName(cfg.Value))
            {
                string target = !string.IsNullOrEmpty(recorded) && !IsHarnessProfileName(recorded)
                    ? recorded : ShippedProfiles.StationeersBlueName;
                report.Add("HUD profile setting: '" + cfg.Value + "' (a test copy) -> '" + target + "'");
                cfg.Value = target;
                did = true;
            }
            var live = HudProfileStore.Active;
            if (live != null && IsHarnessProfileName(live.Name))
            {
                string why;
                if (ActivateByName(cfg.Value, true, out why))
                    report.Add("live HUD: the test copy '" + live.Name + "' replaced by your profile '" + cfg.Value + "'");
                else
                    report.Add("live HUD: could NOT load your profile '" + cfg.Value + "' (" + why + ") - nothing was written to it; restart the game to load it");
                did = true;
            }
            return did;
        }

        /// <summary>Delete the harness's throwaway profiles (the store refuses the one that is still the
        /// live document or the configured profile - never a player's).</summary>
        private static void DeleteHarnessProfiles(List<string> report)
        {
            string[] names = { ScratchProfile, ThemeScratch };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (HudProfileStore.Delete(names[i]) && report != null)
                        report.Add("removed the leftover test profile '" + names[i] + "'");
                }
                catch (Exception e) { if (report != null) report.Add("could not remove '" + names[i] + "': " + e.Message); }
            }
        }

        /// <summary>The run-start state of the per-world stores the copy save shares with the real one
        /// (same world key - Core/WorldKey.cs:77). HintUsage: every counter via the store's own
        /// <c>HintUsageStore.Get</c> (Features/HintUsageStore.cs:128), for every kind the key-hint strip
        /// records (<see cref="HintKinds"/>). StowHomes: <c>StowHomeStore.HomesCount</c>
        /// (Features/StowHomeStore.cs:162), for the after-run notice.</summary>
        private static void SnapshotStores()
        {
            _storesSnapshotted = true;
            _markerProfile = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
            string key = null;
            try { if (!WorldKey.TryGet(out key)) key = null; } catch { key = null; }
            _worldKeyAtStart = key;
            try
            {
                var kinds = HintKinds(key);
                var snap = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < kinds.Count; i++) snap[kinds[i]] = HintUsageStore.Get(kinds[i]);
                _hintSnap = snap;
                _hintSnapKey = key ?? "";
                LogLine("preflight: hint-usage counters snapshotted (" + snap.Count + " kinds, world "
                    + (key != null ? "'" + key + "'" : "memory-only") + ")");
            }
            catch (Exception e)
            {
                _hintSnap = null;
                _hintSnapKey = null;
                LogLine("preflight: could not snapshot the hint-usage counters (" + e.Message + ") - exposure counts this run adds would STAY");
            }
            try { _stowHomesAtStart = StowHomeStore.HomesCount; }
            catch { _stowHomesAtStart = -1; }
        }

        /// <summary>Every hint kind whose counter a run can move or that this world already stores: the
        /// strip's own list (<c>RadialHintBar.AllKinds</c>, private - REFLECTION, like the other privates
        /// here) plus every kind in this world's file (retired kinds keep their counters there - read
        /// READ-ONLY with <c>SaveScopedXmlStore.TryLoad</c>, which never writes). The union matters: the
        /// roll-back goes through <c>HintUsageStore.ResetCounters</c>, which zeroes EVERY kind.</summary>
        private static List<string> HintKinds(string worldKey)
        {
            var kinds = new List<string>();
            try
            {
                var f = typeof(UI.RadialHintBar).GetField("AllKinds", AnyStatic);
                var arr = f != null ? f.GetValue(null) as string[] : null;
                if (arr != null)
                    for (int i = 0; i < arr.Length; i++)
                        if (!string.IsNullOrEmpty(arr[i]) && !kinds.Contains(arr[i])) kinds.Add(arr[i]);
            }
            catch { }
            if (!string.IsNullOrEmpty(worldKey))
            {
                try
                {
                    HintUsageFile file;
                    string err;
                    if (SaveScopedXmlStore.TryLoad<HintUsageFile>(SaveScopedXmlStore.FileFor(HintUsageFolder, worldKey), out file, out err)
                        && file != null && file.Hints != null)
                    {
                        for (int i = 0; i < file.Hints.Count; i++)
                        {
                            var h = file.Hints[i];
                            if (h != null && !string.IsNullOrEmpty(h.Kind) && !kinds.Contains(h.Kind)) kinds.Add(h.Kind);
                        }
                    }
                }
                catch { }
            }
            return kinds;
        }

        /// <summary>Roll the hint-usage counters back to <paramref name="snap"/> through the store's OWN
        /// API - never by writing its file under it: <c>ResetCounters</c> (HintUsageStore.cs:153-158:
        /// zeroes this world's counters and saves), then <c>Add</c> n times per kind (:137-143, memory
        /// only), then one <c>Flush</c> (:146-149). Only when every counter is at or above its snapshot
        /// (only increments happened); a counter that went DOWN means somebody reset them on purpose, and
        /// that is never undone. Returns a one-line report.</summary>
        private static string RestoreHintCounts(Dictionary<string, int> snap)
        {
            int moved = 0, lower = 0;
            var diff = new StringBuilder();
            foreach (var kv in snap)
            {
                int now = HintUsageStore.Get(kv.Key);
                if (now == kv.Value) continue;
                moved++;
                if (now < kv.Value) lower++;
                if (diff.Length < 240)
                    diff.Append(diff.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(now.ToString(Inv)).Append("->").Append(kv.Value.ToString(Inv));
            }
            if (moved == 0) return "unchanged (" + snap.Count + " kinds checked)";
            if (lower > 0)
                return "NOT rolled back - " + lower + " counter(s) went DOWN since the snapshot (a deliberate reset?), and that is never undone [" + diff + "]";
            HintUsageStore.ResetCounters();
            foreach (var kv in snap)
                for (int i = 0; i < kv.Value; i++) HintUsageStore.Add(kv.Key);
            HintUsageStore.Flush();
            int bad = 0;
            foreach (var kv in snap)
                if (HintUsageStore.Get(kv.Key) != kv.Value) bad++;
            return bad == 0
                ? "rolled back to the run-start snapshot [" + diff + "]"
                : "roll-back INCOMPLETE - " + bad + " counter(s) still differ [" + diff + "]";
        }

        /// <summary>The per-world stores after a run (finding: the copy save shares the real save's world
        /// key, so these files ARE the real save's). HintUsage is rolled back (<see cref="RestoreHintCounts"/>)
        /// when this is still the snapshot's world, else left for the marker. BeltBindings and StowHomes
        /// expose no API that removes an entry (BeltBindingStore: SeedIfNew :153 / RecordPlacement :216 only
        /// add, Reset :346 only forgets memory; StowHomeStore: ForgetThisWorld :599 wipes EVERY home), so
        /// their residue is named here instead of silently left.</summary>
        private static void RestorePerWorldStores(List<string> notes)
        {
            string key = null;
            bool haveKey = false;
            try { haveKey = WorldKey.TryGet(out key); } catch { haveKey = false; }
            string keyNow = haveKey ? key : "";
            string file = !string.IsNullOrEmpty(_worldKeyAtStart) ? _worldKeyAtStart + ".xml" : "(memory only - no world key, nothing on disk)";

            if (_hintSnap != null)
            {
                if (!string.Equals(keyNow, _hintSnapKey ?? "", StringComparison.Ordinal))
                {
                    _hintRestorePending = !string.IsNullOrEmpty(_hintSnapKey);
                    notes.Add("hint usage: NOT rolled back yet - the world key is " + (haveKey ? "'" + keyNow + "'" : "unreadable (world left?)")
                        + " instead of '" + _hintSnapKey + "'; the next `uiatest` run (or `uiatest recover`) in that world rolls it back from the marker");
                }
                else notes.Add("hint usage (HintUsage/" + file + "): " + RestoreHintCounts(_hintSnap));
            }
            else if (_storesSnapshotted)   // SnapshotStores ran, but the hint snapshot itself failed
                notes.Add("hint usage: there was no run-start snapshot - exposure counts this run added STAY in HintUsage/" + file);

            if (_testBeltRefs.Count > 0)
            {
                var ids = new StringBuilder();
                for (int i = 0; i < _testBeltRefs.Count; i++) ids.Append(i > 0 ? ", " : "").Append(_testBeltRefs[i].ToString(Inv));
                notes.Add("belt bindings: BeltBindings/" + file + " may keep a seeded (empty) entry for the TEST belt(s) this run wore (ref "
                    + ids + ") - BeltBindingStore has no removal API. Inert unless this world later issues that ReferenceId to a belt you wear.");
            }

            try
            {
                int after = StowHomeStore.HomesCount;
                if (_stowHomesAtStart >= 0 && after != _stowHomesAtStart)
                    notes.Add("stow homes: StowHomes/" + file + " went from " + _stowHomesAtStart + " to " + after
                        + " entries during the run (homes recorded for TEST items; no per-entry removal API) - they self-prune the next time this world loads (StowHomeStore.RunGc drops every home whose item no longer exists)");
            }
            catch { }
        }

        // ================================================================== spawn clearance (machine intakes)

        /// <summary>
        /// Safety review: a vanilla machine intake swallows a LOOSE item. The trigger lives on a
        /// <c>MachineInputTrigger</c> component (27758 Assets/Scripts/Events/MachineInputTrigger.cs:9-37 -
        /// its <c>OnTriggerEnter</c> takes any non-entity, non-child DynamicThing and calls
        /// <c>ParentMachine.OnInputTriggerEnter</c>, :12-21; <c>ParentMachine</c> :36), and the import devices queue whatever Item
        /// arrives: <c>DeviceImport.OnInputTriggerEnter</c> (Assets/Scripts/Objects/Pipes/DeviceImport.cs:390-406;
        /// its trigger field <c>ImportTrigger</c>, :590), <c>DeviceInputOutputImport</c> (:369, :552) and
        /// <c>ImportExport</c> (Objects/Components/ImportExport.cs:344; per-import <c>ImportInfo.Trigger</c>,
        /// ImportInfo.cs:18). DeviceImport alone covers chute inlets, vending machines and generator slots
        /// (ChuteInlet / VendingMachine via DeviceImportExport / PowerGeneratorSlot all derive from it).
        /// Items in a SLOT are ignored (IsChild), so what matters is everything a run
        /// puts in the WORLD: the drop suite's floor spawn 1.2 m ahead and every drop at your feet. Any
        /// MachineInputTrigger - or the body of any of those three device types - within
        /// <see cref="IntakeClearance"/> of you or of the spawn point therefore aborts the run. Fail-closed:
        /// a scan that cannot run counts as "intake nearby".
        /// </summary>
        private static bool IntakeNearby(out string why)
        {
            why = null;
            var h = Guards.LocalHuman;
            if (h == null) { why = "no local player to check around"; return true; }
            Vector3 center = PlayerCenter(h);
            Vector3 spawn = center;
            try { spawn = center + h.EntityForward * WorldSpawnMeters; } catch { }
            why = ScanForIntake(center, "you") ?? ScanForIntake(spawn, "the spawn point ahead");
            return why != null;
        }

        private static string ScanForIntake(Vector3 at, string fromWhat)
        {
            Collider[] cols;
            try { cols = Physics.OverlapSphere(at, IntakeClearance, ~0, QueryTriggerInteraction.Collide); }
            catch (Exception e) { return "the intake scan could not run (" + e.GetType().Name + ")"; }
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null) continue;
                Thing machine = null;
                bool isTrigger = false;
                try
                {
                    var trig = c.GetComponentInParent<global::Assets.Scripts.Events.MachineInputTrigger>();
                    if (trig == null && c.attachedRigidbody != null)
                        trig = c.attachedRigidbody.GetComponent<global::Assets.Scripts.Events.MachineInputTrigger>();
                    if (trig != null) { isTrigger = true; machine = trig.ParentMachine; }
                    if (machine == null)
                    {
                        var th = c.GetComponentInParent<Thing>();
                        if (th is global::Assets.Scripts.Objects.Pipes.DeviceImport
                            || th is global::Assets.Scripts.Objects.Pipes.DeviceInputOutputImport
                            || th is global::Objects.Components.ImportExport)
                            machine = th;
                    }
                }
                catch { }
                if (machine == null && !isTrigger) continue;
                string name = "?";
                try { name = Ascii(machine != null ? machine.DisplayName : c.name); } catch { }
                float d = 0f;
                try { d = Vector3.Distance(at, c.bounds.ClosestPoint(at)); } catch { }
                return "'" + name + "' has a machine intake " + (isTrigger ? "trigger " : "") + F(d) + " m from " + fromWhat;
            }
            return null;
        }

        private static string IntakeAbort(string why)
        {
            return "machine intake nearby - " + why + ". A test item dropped or spawned here could be pulled into it: stand at least "
                + IntakeClearance.ToString("0", Inv) + " m from every chute inlet, recycler, furnace, vending machine or other machine intake, face open floor, and re-run";
        }

        /// <summary>The intake check, once per run, before the first spawn. Sets the run's abort (and fails
        /// <paramref name="t"/>'s setup) when an intake is near.</summary>
        private static bool EnsureSpawnArea(TestCase t)
        {
            if (_spawnAreaChecked) return true;
            string why;
            if (IntakeNearby(out why))
            {
                _abort = IntakeAbort(why);
                if (t != null) t.Fail("setup refused: " + _abort);
                return false;
            }
            _spawnAreaChecked = true;
            LogLine("   spawn area: no machine intake within " + IntakeClearance.ToString("0", Inv) + " m of you or the spawn point");
            return true;
        }

        private static Vector3 PlayerCenter(Human h)
        {
            try { return h.RigidBody != null ? h.RigidBody.worldCenterOfMass : h.Position; }
            catch { return h.Position; }
        }

        /// <summary>Is the forward spawn point inside or behind solid geometry? A ray from your centre to
        /// just past the point, plus a small overlap at the point - solid, non-trigger colliders only, on
        /// every layer except Ignore Raycast / Player / PlayerImmune (RangefinderWidget.TryMeasure's
        /// mask), ignoring anything you are or carry.</summary>
        private static bool SpawnPointBlocked(Vector3 from, Vector3 to, out string why)
        {
            why = null;
            int mask = ~0;
            int[] skip = { (int)Layers.IgnoreRaycast, (int)Layers.Player, (int)Layers.PlayerImmune };
            for (int i = 0; i < skip.Length; i++) if (skip[i] >= 0 && skip[i] < 32) mask &= ~(1 << skip[i]);
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return false;
            try
            {
                var hits = Physics.RaycastAll(from, dir / dist, dist + 0.35f, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    if (IsSelfOrCarried(hits[i].collider)) continue;
                    why = "'" + Ascii(hits[i].collider != null ? hits[i].collider.name : "?") + "' " + F(hits[i].distance) + " m ahead";
                    return true;
                }
                var inside = Physics.OverlapSphere(to, 0.3f, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < inside.Length; i++)
                {
                    if (IsSelfOrCarried(inside[i])) continue;
                    why = "'" + Ascii(inside[i] != null ? inside[i].name : "?") + "' at the spawn point";
                    return true;
                }
            }
            catch (Exception e) { why = "the clearance check could not run (" + e.GetType().Name + ")"; return true; }
            return false;
        }

        private static bool IsSelfOrCarried(Collider c)
        {
            if (c == null) return true;
            try
            {
                var th = c.GetComponentInParent<Thing>();
                if (th == null) return false;
                if (ReferenceEquals(th, Guards.LocalHuman)) return true;
                return ItemActions.IsCarriedByLocalPlayer(th);
            }
            catch { return false; }
        }

        // ================================================================== test driver

        private sealed class DriveResult
        {
            public Exception Error;
            public bool Aborted;
        }

        /// <summary>The exception fence + flattener every step runs through. Nested IEnumerators are
        /// driven here (never handed to Unity), so a throw anywhere below is CAUGHT and reported instead
        /// of killing the coroutine; everything else (null, WaitForEndOfFrame) goes to Unity. Each step
        /// re-reads <see cref="AbortReason"/> — the single-player gate included — and an abort disposes
        /// the abandoned enumerators, which runs their <c>finally</c> blocks. Never throws.</summary>
        private static IEnumerator Drive(IEnumerator root, DriveResult res)
        {
            var stack = new List<IEnumerator>(4);
            if (root != null) stack.Add(root);
            try
            {
                while (stack.Count > 0)
                {
                    if (_abort == null) { string w = AbortReason(); if (w != null) _abort = w; }
                    if (_abort != null)
                    {
                        res.Aborted = true;
                        yield break;   // the finally below disposes the abandoned steps
                    }
                    IEnumerator top = stack[stack.Count - 1];
                    bool more = false;
                    object cur = null;
                    Exception err = null;
                    try
                    {
                        more = top.MoveNext();
                        if (more) cur = top.Current;
                    }
                    catch (Exception e) { err = e; }
                    if (err != null)
                    {
                        res.Error = err;
                        stack.RemoveAt(stack.Count - 1);   // the thrower already unwound its own finally blocks
                        yield break;
                    }
                    if (!more) { stack.RemoveAt(stack.Count - 1); continue; }
                    var nested = cur as IEnumerator;
                    if (nested != null && !(cur is CustomYieldInstruction)) { stack.Add(nested); continue; }
                    yield return cur;
                }
            }
            finally
            {
                // Runs on completion, on abort/error above, AND when an outer driver disposes this one
                // (an abort noticed a level up): every suspended step's finally blocks still run.
                DisposeStack(stack);
            }
        }

        private static void DisposeStack(List<IEnumerator> stack)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                try
                {
                    var d = stack[i] as IDisposable;
                    if (d != null) d.Dispose();
                }
                catch (Exception e) { LogLine("    (cleanup of an abandoned step threw: " + e.Message + ")"); }
            }
            stack.Clear();
        }

        private sealed class TestCase
        {
            public readonly string Id;
            public readonly string Title;
            public int Checks;
            public int Fails;
            public string SkipReason;
            public readonly List<string> Lines = new List<string>();

            public TestCase(string id, string title) { Id = id; Title = title; }

            public bool Check(bool ok, string what)
            {
                Checks++;
                if (!ok) Fails++;
                Lines.Add((ok ? "    ok   " : "    FAIL ") + what);
                return ok;
            }

            public void Fail(string what)
            {
                Fails++;
                Lines.Add("    FAIL " + what);
            }

            public void Info(string what) { Lines.Add("    info " + what); }

            /// <summary>Records why part (or all) of the test could not run. The test is reported SKIP
            /// only if no assertion ran at all; otherwise PASS/FAIL with a "partial" note.</summary>
            public void Skip(string why)
            {
                if (SkipReason == null) SkipReason = why;
                Lines.Add("    skip " + why);
            }

            /// <summary>Verdict honesty: a claim of the test's title that this run did NOT prove (a
            /// pointer-driven path, a constant standing in for its enforcement...). It never turns a PASS
            /// into a FAIL, but the verdict line says "NOT proven: n" and the line is printed with it.</summary>
            public readonly List<string> Unproven = new List<string>();
            public void NotProven(string what)
            {
                Unproven.Add(what);
                Lines.Add("    NOT PROVEN " + what);
            }

            /// <summary>A screenshot written for a human to judge: its own state, never a check (a PNG on
            /// disk proves nothing about what it shows).</summary>
            public int Captures;
            public readonly List<string> CapturedPaths = new List<string>();
            public void Captured(string path, string detail)
            {
                Captures++;
                CapturedPaths.Add(path);
                Lines.Add("    CAPTURED " + path + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
            }
        }

        /// <summary>Run one test body in its own exception fence (a throw = FAIL, the suite continues)
        /// and record the verdict.</summary>
        private static IEnumerator Test(string id, string title, Func<TestCase, IEnumerator> body)
        {
            var t = new TestCase(id, title);
            IEnumerator it = null;
            try { it = body(t); }
            catch (Exception e) { t.Fail("threw while starting: " + Describe(e)); }
            if (it == null) { Record(t); yield break; }
            var res = new DriveResult();
            var d = Drive(it, res);
            bool recorded = false;
            try
            {
                while (d.MoveNext()) yield return d.Current;
                if (res.Error != null) t.Fail("threw: " + Describe(res.Error));
                if (res.Aborted) t.Fail("aborted: " + _abort);
                Record(t);
                recorded = true;
            }
            finally
            {
                // Forward a dispose from an outer driver (an abort noticed a level up) so the test's own
                // finally blocks run, and still record the verdict; a no-op after normal completion.
                var disp = d as IDisposable;
                if (disp != null) disp.Dispose();
                if (!recorded)
                {
                    t.Fail("aborted: " + (_abort ?? "abandoned"));
                    Record(t);
                }
            }
        }

        /// <summary>The verdict: FAIL if any check failed; PASS if checks ran; CAPTURED if the only outcome
        /// was screenshots (a third state - never a pass); SKIP if nothing ran. A PASS/FAIL with skipped
        /// or NOT-proven parts says so on its verdict line and counts as "partly proven".</summary>
        private static void Record(TestCase t)
        {
            string status = t.Fails > 0 ? "FAIL" : t.Checks > 0 ? "PASS" : t.Captures > 0 ? "CAPTURED" : "SKIP";
            if (status == "PASS") _pass++;
            else if (status == "FAIL") _fail++;
            else if (status == "SKIP") _skip++;
            else _capturedOnly++;
            bool partial = (status == "PASS" || status == "FAIL") && (t.SkipReason != null || t.Unproven.Count > 0);
            if (partial) _partial++;
            string tail;
            if (status == "SKIP") tail = " - " + (t.SkipReason ?? "no assertion ran");
            else
            {
                var sb = new StringBuilder(" (");
                if (t.Checks > 0) sb.Append(t.Checks).Append(t.Checks == 1 ? " check" : " checks");
                if (t.Captures > 0) sb.Append(t.Checks > 0 ? "; " : "").Append(t.Captures).Append(" screenshot(s) CAPTURED - judge by eye");
                if (t.SkipReason != null) sb.Append("; partial - ").Append(t.SkipReason);
                if (t.Unproven.Count > 0) sb.Append("; NOT proven: ").Append(t.Unproven.Count).Append(t.Unproven.Count == 1 ? " item" : " items");
                sb.Append(')');
                tail = sb.ToString();
            }
            string line = "[" + status + "] " + t.Id + "  " + t.Title + tail;
            LogLine(line);
            for (int i = 0; i < t.Lines.Count; i++) LogLine(t.Lines[i]);
            _results.Add(line);
            for (int i = 0; i < t.CapturedPaths.Count; i++) _results.Add("[CAPTURED] " + t.CapturedPaths[i]);
            // The console is closed during the run; these accumulate so reopening it shows progress.
            Say(line, status == "PASS" ? ConsoleColor.Green : status == "FAIL" ? ConsoleColor.Red
                : status == "CAPTURED" ? ConsoleColor.Cyan : ConsoleColor.Yellow);
            int shownFail = 0, shownUnproven = 0;
            for (int i = 0; i < t.Lines.Count; i++)
            {
                string l = t.Lines[i];
                if (l.StartsWith("    FAIL", StringComparison.Ordinal)) { if (shownFail++ < 6) Say(l, ConsoleColor.Red); }
                else if (l.StartsWith("    NOT PROVEN", StringComparison.Ordinal)) { if (shownUnproven++ < 4) Say(l, ConsoleColor.Yellow); }
                else if (l.StartsWith("    CAPTURED", StringComparison.Ordinal)) Say(l, ConsoleColor.Cyan);
            }
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Realtime wait (unaffected by timeScale), as a flattenable enumerator.</summary>
        private static IEnumerator Seconds(float s)
        {
            float until = Time.unscaledTime + s;
            while (Time.unscaledTime < until) yield return null;
        }

        /// <summary>Despawn everything this run spawned so far and let the destroys land. Tests that
        /// need empty hands start with this.</summary>
        private static IEnumerator ClearTestItems()
        {
            if (DespawnAll() > 0)
                for (int i = 0; i < 4; i++) yield return null;
        }

        // ================================================================== fixtures: spawning

        /// <summary>Resolve a prefab exactly the way vanilla's creative spawn does before it creates
        /// anything (27758 OnServer.SpawnDynamicThingMaxStack, OnServer.cs:763-777): the prefab must
        /// exist, the account must own its DLC (<c>SharedDLCManager.CheckSharedAccess</c>, OnServer.cs:769,
        /// SharedDLCManager.cs:65) and it must not be tagged NotSpawnable (OnServer.cs:774).</summary>
        private static DynamicThing ResolvePrefab(string prefabName, TestCase t)
        {
            DynamicThing prefab = null;
            try { prefab = Prefab.Find<DynamicThing>(prefabName); }
            catch (Exception e) { if (t != null) t.Info("prefab lookup '" + prefabName + "' threw: " + e.Message); }
            if (prefab == null)
            {
                if (t != null) t.Skip("prefab '" + prefabName + "' does not exist on this game build");
                return null;
            }
            try
            {
                if (!global::DLC.SharedDLCManager.CheckSharedAccess(prefab.DLCType))
                {
                    if (t != null) t.Skip("prefab '" + prefabName + "' needs a DLC this account does not own");
                    return null;
                }
            }
            catch { }
            try
            {
                if (prefab.tag == "NotSpawnable")
                {
                    if (t != null) t.Skip("prefab '" + prefabName + "' is tagged NotSpawnable");
                    return null;
                }
            }
            catch { }
            return prefab;
        }

        private static bool PrefabExists(string prefabName)
        {
            try { return Prefab.Find<DynamicThing>(prefabName) != null; }
            catch { return false; }
        }

        private static string FirstExistingPrefab(TestCase t, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                if (PrefabExists(names[i])) return names[i];
            if (t != null) t.Skip("none of these prefabs exist on this build: " + string.Join(", ", names));
            return null;
        }

        /// <summary>Fixture spawns re-check the single-player gate in the SAME frame as the create.</summary>
        private static bool SetupAuthority(TestCase t)
        {
            string why;
            if (SinglePlayerGate(out why)) return true;
            _abort = "single-player gate: " + why;
            if (t != null) t.Fail("setup refused: " + why);
            return false;
        }

        /// <summary>Server-side world spawn, placed like vanilla's creative spawn (OnServer.cs:778-780:
        /// the player's rigidbody centre of mass + EntityForward * distance, rotated 180 degrees about
        /// the player's up) through <c>OnServer.Create&lt;T&gt;(prefab, pos, rot)</c> (OnServer.cs:424).
        /// Refused (the test SKIPs) when the point is inside or behind geometry, and never before the
        /// run's machine-intake check passed. Tracked for despawn.</summary>
        private static DynamicThing SpawnInWorld(string prefabName, float forwardMeters, TestCase t)
        {
            if (!SetupAuthority(t)) return null;
            if (!EnsureSpawnArea(t)) return null;
            var human = Guards.LocalHuman;
            if (human == null) { if (t != null) t.Fail("setup: no local player"); return null; }
            var prefab = ResolvePrefab(prefabName, t);
            if (prefab == null) return null;
            Vector3 center = PlayerCenter(human);
            Vector3 pos = center + human.EntityForward * forwardMeters;
            string blocked;
            if (SpawnPointBlocked(center, pos, out blocked))
            {
                if (t != null) t.Skip("the world-spawn point " + F(forwardMeters) + " m ahead is blocked (" + blocked + ") - face OPEN floor and re-run");
                return null;
            }
            DynamicThing thing = null;
            try
            {
                Quaternion rot = Quaternion.AngleAxis(180f, human.ThingTransform.up);
                thing = OnServer.Create<DynamicThing>(prefab, pos, rot);
            }
            catch (Exception e) { if (t != null) t.Fail("setup: OnServer.Create('" + prefabName + "') threw: " + e.Message); }
            if (thing == null) return null;
            _spawned.Add(thing);
            FillLikeCreative(thing);
            return thing;
        }

        /// <summary>Server-side spawn straight INTO an empty slot: <c>OnServer.Create&lt;T&gt;(prefab,
        /// slot)</c> (27758 OnServer.cs:379-397) creates the thing and lands it with
        /// <c>OnServer.MoveToSlotOrWorld</c> — vanilla's own create-into-slot path. Setup only: the
        /// slot must be empty (an occupied slot would drop the new thing at the world origin), and the
        /// landing is verified: a thing that did not land is despawned at once; one that did is tracked
        /// for the suite's despawn.</summary>
        private static DynamicThing SpawnIntoSlot(string prefabName, Slot slot, TestCase t)
        {
            if (slot == null) { if (t != null) t.Fail("setup: no slot to spawn '" + prefabName + "' into"); return null; }
            if (slot.Get() != null) { if (t != null) t.Fail("setup: the slot for '" + prefabName + "' is occupied"); return null; }
            if (!SetupAuthority(t)) return null;
            if (!EnsureSpawnArea(t)) return null;
            var prefab = ResolvePrefab(prefabName, t);
            if (prefab == null) return null;
            DynamicThing thing = null;
            try { thing = OnServer.Create<DynamicThing>(prefab, slot); }
            catch (Exception e) { if (t != null) t.Fail("setup: OnServer.Create('" + prefabName + "') into a slot threw: " + e.Message); }
            if (thing == null) return null;
            _spawned.Add(thing);
            FillLikeCreative(thing);
            if (thing.ParentSlot != slot)
            {
                // A refused landing leaves the new thing LOOSE in the world (Create<T>(prefab, slot) makes it
                // at the origin, then MoveToSlotOrWorld - 27758 OnServer.cs:379-397): despawn it NOW, not
                // at the suite's end, so nothing (an intake, a fall) can take it first.
                try { if (!thing.IsBeingDestroyed) OnServer.Destroy(thing); } catch { }
                _spawned.Remove(thing);
                if (t != null) t.Fail("setup: '" + prefabName + "' did not land in the requested slot (despawned at once)");
                return null;
            }
            return thing;
        }

        /// <summary>The creative spawn's max-stack fill (OnServer.cs:781-785). Only stacks matter here:
        /// a cable coil must be splittable (Quantity &gt; 1) to be a "plain stack".</summary>
        private static void FillLikeCreative(DynamicThing thing)
        {
            try
            {
                var st = thing as Stackable;
                if (st != null) st.SetQuantity(st.MaxQuantity);
            }
            catch { }
        }

        /// <summary>Despawn every tracked test item through the server-side destroy
        /// (<c>OnServer.Destroy</c>, 27758 OnServer.cs:533-545), newest first. The destroy is deferred
        /// to end of frame by Unity; DynamicThing.OnDestroy empties the parent slot
        /// (DynamicThing.cs:2638-2670). Skipped when the world is already gone (nothing to clean) or
        /// the app is quitting.</summary>
        private static int DespawnAll()
        {
            int n = 0;
            bool canDestroy = false;
            try
            {
                canDestroy = GameManager.RunSimulation
                    && !Assets.Scripts.Util.Singleton<GameManager>.IsQuitting
                    && GameManager.GameState != GameState.None;
            }
            catch { canDestroy = false; }
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var th = _spawned[i];
                if (!canDestroy || th == null) continue;   // Unity-null: already gone
                try
                {
                    if (!th.IsBeingDestroyed) { OnServer.Destroy(th); n++; }
                }
                catch (Exception e) { LogLine("   despawn of a test item failed: " + e.Message); }
            }
            _spawned.Clear();
            return n;
        }

        // ================================================================== fixtures: the ONE gate bypass

        /// <summary>
        /// ########################################################################################
        /// ##  TEST-ONLY GATE BYPASS - THE ONLY ONE IN THE MOD. NEVER CALL THIS FROM ANYWHERE ELSE. ##
        /// ########################################################################################
        /// Recreates a LEGACY SAVE STATE: an item the pre-D-005 phantom grid tucked into a cable
        /// coil's hidden slot. The D-005 gates (ItemActions.IsSealedSlot at every placement funnel)
        /// make that state unreachable on purpose, so the rescue path can only be tested by building
        /// the state directly — with the exact call the old grid made: <c>OnServer.MoveToSlot</c>
        /// straight at the slot, WITHOUT the IsSealedSlot / Slot.AllowMove pre-checks. On the
        /// host/single-player that call is <c>childThing.MoveToSlot(slot, slot.Parent, false)</c>
        /// (27758 OnServer.cs:60-67), i.e. a DynamicThing.MoveToSlot-level placement whose only own
        /// checks are CanEnter + an empty slot (DynamicThing.cs:2781-2793 — no interactability check,
        /// which is exactly how items got trapped). Single-player only, re-checked in this very frame;
        /// refuses anything that is not a test-spawned item going into a SEALED slot of a test-spawned
        /// OWNER (the slot's parent thing) — so it can never reach into anything of the player's.
        /// </summary>
        private static bool LegacyTrap_DirectPlace_TestOnly(DynamicThing item, Slot sealedSlot, TestCase t)
        {
            if (item == null || sealedSlot == null) { t.Fail("legacy trap: nothing to place"); return false; }
            if (!_spawned.Contains(item)) { t.Fail("legacy trap: refused - the item is not a test-spawned item"); return false; }
            var owner = sealedSlot.Parent as DynamicThing;
            if (owner == null || !_spawned.Contains(owner))
            {
                t.Fail("legacy trap: refused - the sealed slot's owner is not a test-spawned item");
                return false;
            }
            if (!ItemActions.IsSealedSlot(sealedSlot)) { t.Fail("legacy trap: refused - the target slot is not sealed"); return false; }
            if (sealedSlot.Get() != null) { t.Fail("legacy trap: the sealed slot is already occupied"); return false; }
            if (!SetupAuthority(t)) return false;
            try { OnServer.MoveToSlot(item, sealedSlot); }
            catch (Exception e) { t.Fail("legacy trap: the direct placement threw: " + e.Message); return false; }
            t.Info("LEGACY STATE BUILT with the one test-only gate bypass (OnServer.MoveToSlot straight into the sealed slot)");
            return true;
        }

        /// <summary>
        /// TEST-ONLY FIXTURE (not a gate bypass - it makes a slot MORE restrictive, never less): set or
        /// clear <c>Slot.IsLocked</c> (a plain public field, 27758 Slot.cs:967) on a TEST-SPAWNED item's
        /// slot - the very field vanilla's only runtime writer sets, <c>JetpackElectric.OnInteractableUpdated</c>
        /// (JetpackElectric.cs:165-178, the write at :174). Needed because the one JetpackElectric
        /// prefab, ItemJetpackTurbine, is tagged NotSpawnable (drop.d). Locking refuses a slot whose owner
        /// is not a test-spawned item or that is already locked, and re-checks the single-player gate in
        /// this frame; unlocking only ever undoes a lock THIS fixture set. Every lock is remembered and
        /// cleared again by RestoreAll even when the test aborts.
        /// </summary>
        private static bool LockSlot_TestOnly(Slot slot, bool locked, TestCase t)
        {
            if (slot == null) { if (t != null) t.Fail("lock fixture: no slot"); return false; }
            if (!locked)
            {
                if (_lockedSlots.Remove(slot)) slot.IsLocked = false;
                return true;
            }
            var owner = slot.Parent as DynamicThing;
            if (owner == null || !_spawned.Contains(owner))
            {
                if (t != null) t.Fail("lock fixture: refused - the slot's owner is not a test-spawned item");
                return false;
            }
            if (slot.IsLocked)
            {
                if (t != null) t.Fail("lock fixture: refused - the slot is already locked (not by this fixture)");
                return false;
            }
            if (!SetupAuthority(t)) return false;
            _lockedSlots.Add(slot);
            slot.IsLocked = true;
            return true;
        }

        // ================================================================== fixtures: HUD profile / theme / tier
        //
        // Safety review (profile restore): a run NEVER moves the config's profile pointer
        // (HudConfig.HudActiveProfile - what the next launch loads) and never goes through
        // HudProfileStore.LoadActive, whose self-heal WRITES its starter under the requested name when a
        // load fails (HudProfileStore.cs:1199-1233 - with the F10/F9 switchers' `keep.Clone()` starter
        // that is a copy of whatever was live, i.e. a shipped theme's clone landing under the player's
        // profile). Instead the LIVE document is swapped with the store's own HudProfileStore.SetActive
        // (:1126 - flushes the outgoing document under ITS name, applies the incoming theme), and every
        // document comes from HudProfileStore.Load (:380) or is an in-memory clone. A crash mid-run
        // therefore leaves the player's profile setting untouched by construction.

        /// <summary>Make <paramref name="name"/> the LIVE document by name: the store's own
        /// <c>HudProfileStore.Load</c> then <c>SetActive</c> - LoadActive without its self-heal write.
        /// Nothing is written under <paramref name="name"/>. With <paramref name="embedFallback"/> a
        /// shipped theme missing from disk comes up from its embed IN MEMORY (the factory LoadActive would
        /// self-heal from - not written). False, with a reason, when the file cannot be read.</summary>
        private static bool ActivateByName(string name, bool embedFallback, out string why)
        {
            why = null;
            HudDocument doc = null;
            try { doc = HudProfileStore.Load(name); }
            catch (Exception e) { why = e.GetType().Name + ": " + e.Message; }
            if (doc == null && embedFallback)
            {
                doc = BuildEmbedded(name);
                if (doc != null) { doc.Sanitize(); doc.Name = name; }
            }
            if (doc == null)
            {
                if (why == null) why = "the file is missing, locked (antivirus / cloud sync?) or unreadable";
                return false;
            }
            Windows.HudEditorWindow.FlushPendingElementEdit();
            HudProfileStore.SetActive(doc, name);
            HudDocumentHistory.Clear();
            Windows.HudEditorMode.ClearElementSelection();
            if (!ReferenceEquals(HudProfileStore.Active, doc)) { why = "the store did not take the document"; return false; }
            return true;
        }

        /// <summary>Show a SHIPPED theme for a test: its on-disk copy (what the F10 switcher would load),
        /// else its embed (what LoadActive would self-heal from), as an in-memory CLONE under the
        /// throwaway <see cref="ThemeScratch"/> name via <c>SetActive</c>. No file is written and no name
        /// but the throwaway one can receive an autosave. Restored by name when the suite ends.</summary>
        private static bool ApplyShippedTheme(string name, TestCase t)
        {
            try
            {
                BeginProfileGuard();
                HudDocument src = HudProfileStore.Load(name);
                if (src == null)
                {
                    src = BuildEmbedded(name);
                    if (src != null) src.Sanitize();
                }
                if (src == null) { t.Fail("the shipped theme '" + name + "' is neither on disk nor embedded"); return false; }
                var doc = src.Clone();
                doc.Name = ThemeScratch;
                Windows.HudEditorWindow.FlushPendingElementEdit();
                HudProfileStore.SetActive(doc, ThemeScratch);
                HudDocumentHistory.Clear();
                Windows.HudEditorMode.ClearElementSelection();
                _scratchActive = false;   // the live document is no longer the scratch profile
                return ReferenceEquals(HudProfileStore.Active, doc);
            }
            catch (Exception e)
            {
                t.Fail("showing the shipped theme '" + name + "' threw: " + e.Message);
                return false;
            }
        }

        /// <summary>Remember the player's profile NAME (the pointer), their own live document (the
        /// no-disk fallback of the restore) and the full global look ONCE per suite, so
        /// <see cref="RestoreAll"/> can put all of it back exactly.</summary>
        private static void BeginProfileGuard()
        {
            if (!_profileTouched && HudConfig.HudActiveProfile != null)
            {
                _origProfile = HudConfig.HudActiveProfile.Value;
                var live = HudProfileStore.Active;
                _origDoc = live != null && !IsHarnessProfileName(live.Name)
                    && string.Equals(live.Name, _origProfile, StringComparison.OrdinalIgnoreCase) ? live : null;
                _profileTouched = true;
            }
            CaptureGlobals();
        }

        private static void CaptureGlobals()
        {
            if (_savedGlobals != null) return;
            try { _savedGlobals = HudTheme.Snapshot(); }
            catch (Exception e) { LogLine("   could not snapshot the global look: " + e.Message); }
        }

        /// <summary>Activate a throwaway copy of the player's active profile. The player's own profiles
        /// are never edited; the copy is deleted when the suite ends.</summary>
        private static bool BeginScratchProfile(TestCase t)
        {
            if (_scratchActive) return true;
            var active = HudProfileStore.Active;
            if (active == null || HudConfig.HudActiveProfile == null) { t.Skip("no active HUD profile"); return false; }
            BeginProfileGuard();
            try { HudProfileStore.Delete(ScratchProfile); } catch { }
            var clone = active.Clone();
            clone.Name = ScratchProfile;
            if (!HudProfileStore.Save(clone, ScratchProfile)) { t.Fail("could not write the scratch profile"); return false; }
            string why;
            if (!ActivateByName(ScratchProfile, false, out why)) { t.Fail("could not activate the scratch profile (" + why + ")"); return false; }
            _scratchActive = true;
            t.Info("working in a scratch HUD profile '" + ScratchProfile + "' (a copy of '" + Ascii(active.Name)
                + "'); your profile '" + Ascii(_origProfile) + "' comes back after the suite");
            return true;
        }

        /// <summary>Put the player's profile and global look back NOW (mid-suite), e.g. after the
        /// theme shots so a later scratch copy starts from the player's own profile.</summary>
        private static void RestoreProfileNow()
        {
            RestoreStep("profile", RestoreProfile);
            RestoreStep("globals", () =>
            {
                var g = _savedGlobals;
                _savedGlobals = null;
                if (g != null) HudTheme.Apply(g);
            });
        }

        private static void ForceTier(HudTier? tier)
        {
            if (!_forceTierTouched)
            {
                _savedForceTier = HudSystem.ForceTier;
                _forceTierTouched = true;
            }
            HudSystem.ForceTier = tier;
        }

        private static void SaveRecent()
        {
            if (_recentTouched) return;
            _recentTouched = true;
            _recentHash = RetrievalMemory.LastPrefabHash;
            _recentName = RetrievalMemory.LastName;
            _recentIcon = RetrievalMemory.LastIcon;
        }

        // ================================================================== restore

        /// <summary>Undo everything a suite touched, in a safe order. Idempotent; every step is fenced.
        /// Called after every suite, at the end of the run, and from the F6 teardown.</summary>
        private static void RestoreAll()
        {
            RestoreStep("hover", () => { _hoverIndex = HoverOff; });
            RestoreStep("locks", () =>
            {
                // Test-only slot locks (LockSlot_TestOnly) never outlive their test, even on an abort.
                for (int i = _lockedSlots.Count - 1; i >= 0; i--)
                    if (_lockedSlots[i] != null) _lockedSlots[i].IsLocked = false;
                _lockedSlots.Clear();
            });
            RestoreStep("radials", CloseRadialsQuietly);
            RestoreStep("f10", RestoreF10);
            RestoreStep("tier", () =>
            {
                if (!_forceTierTouched) return;
                _forceTierTouched = false;
                HudSystem.ForceTier = _savedForceTier;
                _savedForceTier = null;
            });
            RestoreStep("profile", RestoreProfile);
            RestoreStep("globals", () =>
            {
                var g = _savedGlobals;
                _savedGlobals = null;
                if (g != null) HudTheme.Apply(g);
            });
            RestoreStep("recent", () =>
            {
                if (!_recentTouched) return;
                _recentTouched = false;
                RetrievalMemory.LastPrefabHash = _recentHash;
                RetrievalMemory.LastName = _recentName;
                RetrievalMemory.LastIcon = _recentIcon;
                _recentName = null;
                _recentIcon = null;
            });
            RestoreStep("pins", () =>
            {
                for (int i = _pins.Count - 1; i >= 0; i--)
                {
                    var w = _pins[i];
                    if (w != null) w.Close();
                }
                _pins.Clear();
            });
            RestoreStep("temp", () =>
            {
                for (int i = _temp.Count - 1; i >= 0; i--)
                    if (_temp[i] != null) UnityEngine.Object.Destroy(_temp[i]);
                _temp.Clear();
            });
            RestoreStep("hints", () =>
            {
                if (!_hintsTouched) return;
                _hintsTouched = false;
                RadialHintContext.Reset();
            });
            _rfElementId = null;
        }

        private static void RestoreStep(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { LogLine("   restore step '" + what + "' failed: " + e.Message); }
        }

        /// <summary>Close any open radial WITHOUT letting a test chip drop: the live menu's parking is
        /// cleared first (parking is visual-only, so clearing it never moves an item).</summary>
        private static void CloseRadialsQuietly()
        {
            var ctl = RadialController.Active;
            if (ctl == null) return;
            try { ctl._menu._parking.Clear(); } catch { }
            if (ctl.IsRadialOpen) ctl.CloseAll("uiatest");
        }

        /// <summary>Put the player's profile back BY NAME - never by writing a document under it
        /// (safety review: the old fallback saved <c>keep.Clone()</c>, i.e. whatever test copy was live,
        /// under the player's name whenever <c>Load</c> failed - an antivirus or OneDrive lock, ~12
        /// switches per run). (1) The pointer: a run never moves it, but it is set back defensively (a
        /// name, not a document). (2) The live document, if the store is still up: the store's own
        /// <c>Load</c> + <c>SetActive</c> (<see cref="ActivateByName"/>). If that load fails, NOTHING is
        /// written: the player's own in-memory document from before the run is reinstalled (when there is
        /// one), a loud warning says so, and the store's own self-heal/quarantine handles the file at the
        /// next launch (the pointer still names it). In the F6 teardown the store may already be shut
        /// down (Active null): then only the pointer is checked and no HUD object is rebuilt. (3) The
        /// harness's scratch profiles are deleted (the store refuses the one still live).</summary>
        private static void RestoreProfile()
        {
            if (!_profileTouched) return;
            string orig = _origProfile;
            HudDocument origDoc = _origDoc;
            _profileTouched = false;
            _scratchActive = false;
            _origProfile = null;
            _origDoc = null;
            try
            {
                var cfg = HudConfig.HudActiveProfile;
                if (orig != null && cfg != null && !string.Equals(cfg.Value, orig, StringComparison.Ordinal))
                {
                    LogLine("   HUD profile setting was '" + Ascii(cfg.Value) + "' - set back to '" + Ascii(orig) + "' (by name)");
                    cfg.Value = orig;
                }
                var cur = HudProfileStore.Active;
                bool needsSwitch = orig != null && cur != null && !ReferenceEquals(cur, origDoc)
                    && (IsHarnessProfileName(cur.Name) || !string.Equals(cur.Name, orig, StringComparison.OrdinalIgnoreCase));
                if (needsSwitch)
                {
                    string why;
                    if (!ActivateByName(orig, false, out why))
                    {
                        bool reinstalled = false;
                        if (origDoc != null)
                        {
                            // The player's OWN document object from before the run, under its own name:
                            // no disk access, nothing written (SetActive only flushes the OUTGOING test copy
                            // under the test copy's name).
                            Windows.HudEditorWindow.FlushPendingElementEdit();
                            HudProfileStore.SetActive(origDoc, orig);
                            reinstalled = ReferenceEquals(HudProfileStore.Active, origDoc);
                        }
                        WarnLoud("your HUD profile '" + orig + "' could not be re-read after the test run (" + why + "). NOTHING was written to it"
                            + (reinstalled ? "; your own layout is back on screen from memory" : "; a test copy is still on screen")
                            + ". Restart the game to load it from disk again (the profile setting still points at it)"
                            + (reinstalled ? "." : ", and do not edit the HUD (F9) before you do."));
                    }
                }
                if (cur != null)
                {
                    HudDocumentHistory.Clear();   // no undo step may carry a test document back
                    Windows.HudEditorMode.ClearElementSelection();
                }
            }
            catch (Exception e) { LogLine("   could not re-activate the original HUD profile: " + e.Message); }
            DeleteHarnessProfiles(null);
        }

        /// <summary>A warning that must not be missed: console (red), BepInEx log, the run log, a toast.</summary>
        private static void WarnLoud(string msg)
        {
            LogLine("   WARNING: " + msg);
            Say("uiatest WARNING: " + msg, ConsoleColor.Red);
            try { UIALog.Warn("uiatest: " + msg); } catch { }
            try
            {
                global::StationeersUIMod.Overlay.Toast.Show("uiatest: your HUD profile could not be re-read - open the console",
                    global::StationeersUIMod.Overlay.Theme.Warn, 8f);
            }
            catch { }
        }

        // ================================================================== Harmony (own id, removed at the end)

        private static bool EnsureHarmony()
        {
            if (_harmony == null) _harmony = new Harmony(HarmonyId);
            return _harmony != null;
        }

        /// <summary>Counts <c>ItemActions.DropToWorld</c> calls — the only way to PROVE "exactly one drop
        /// message" in single-player, where a second drop of an already-dropped item is a silent no-op.
        /// Postfix on our own method (not a vanilla one); removed by <see cref="Unhook"/>.</summary>
        private static bool HookDropCounter(TestCase t)
        {
            if (_dropHooked) return true;
            try
            {
                EnsureHarmony();
                var target = AccessTools.Method(typeof(ItemActions), "DropToWorld", new[] { typeof(ScannedSlot) });
                var post = AccessTools.Method(typeof(UiaTestHarness), "DropToWorldPostfix");
                if (target == null || post == null)
                {
                    t.Info("drop counter unavailable (method not found) - the exactly-once checks become info");
                    return false;
                }
                _harmony.Patch(target, postfix: new HarmonyMethod(post));
                _dropHooked = true;
                return true;
            }
            catch (Exception e)
            {
                t.Info("drop counter unavailable: " + e.Message);
                return false;
            }
        }

        private static void DropToWorldPostfix(bool __result)
        {
            _dropCalls++;
            if (__result) _dropOk++;
        }

        private static void ResetDropCounter() { _dropCalls = 0; _dropOk = 0; }

        private static void CheckDrops(TestCase t, int calls, int ok, string what)
        {
            if (_dropHooked)
                t.Check(_dropCalls == calls && _dropOk == ok,
                    what + " (DropToWorld calls " + _dropCalls + ", accepted " + _dropOk + "; expected " + calls + "/" + ok + ")");
            else t.NotProven(what + " - the DropToWorld call counter could not be hooked, so the message count is not asserted");
        }

        private static void Unhook()
        {
            _hoverIndex = HoverOff;
            if (_harmony != null)
            {
                try { _harmony.UnpatchSelf(); }
                catch (Exception e) { LogLine("unpatch failed: " + e.Message); }
            }
            _harmony = null;
            _dropHooked = false;
            _hoverHooked = false;
        }

        // ================================================================== small helpers

        private static int ScanDepth()
        {
            try { return UIAConfig.ScanDepth != null ? UIAConfig.ScanDepth.Value : 3; }
            catch { return 3; }
        }

        private static bool HandsAreEmpty()
        {
            var h = Guards.LocalHuman;
            return h != null && h.LeftHandSlot != null && h.RightHandSlot != null
                && h.LeftHandSlot.Get() == null && h.RightHandSlot.Get() == null;
        }

        private static bool RequireEmptyHands(TestCase t)
        {
            if (HandsAreEmpty()) return true;
            t.Skip("both hands must be EMPTY (the test spawns its items into them) - put what you hold away and re-run");
            return false;
        }

        private static Slot ActiveHand()
        {
            Slot a = null;
            try { a = InventoryManager.ActiveHandSlot; } catch { }
            if (a != null) return a;
            var h = Guards.LocalHuman;
            return h != null ? h.LeftHandSlot : null;
        }

        private static Slot OtherHand(Slot hand)
        {
            var h = Guards.LocalHuman;
            if (h == null) return null;
            return hand == h.LeftHandSlot ? h.RightHandSlot : h.LeftHandSlot;
        }

        private static Slot FirstSlot(Thing thing, Slot.Class type, bool visibleOnly)
        {
            if (thing == null || thing.Slots == null) return null;
            for (int i = 0; i < thing.Slots.Count; i++)
            {
                var s = thing.Slots[i];
                if (s == null || s.Type != type) continue;
                if (visibleOnly && !ItemActions.IsVanillaVisibleSlot(s)) continue;
                return s;
            }
            return null;
        }

        /// <summary>Empty, unlocked, generic (None) slots vanilla would draw and nothing seals.</summary>
        private static List<Slot> FreeStorageSlots(Thing bag)
        {
            var list = new List<Slot>();
            if (bag == null || bag.Slots == null) return list;
            for (int i = 0; i < bag.Slots.Count; i++)
            {
                var s = bag.Slots[i];
                if (s == null || s.IsLocked || s.Type != Slot.Class.None || s.Get() != null) continue;
                if (!ItemActions.IsVanillaVisibleSlot(s) || ItemActions.IsSealedSlot(s)) continue;
                list.Add(s);
            }
            return list;
        }

        private static ScannedSlot Pinned(Slot slot)
        {
            return new ScannedSlot { Slot = slot, Holder = slot != null ? slot.Parent : null, Location = "" }.Pin();
        }

        /// <summary>A chip in exactly the shape the radial's own drag layer mints (RadialMenu.TryBeginDrag:
        /// a slot-sourced chip with an Expected-pinned ScannedSlot), parked at <paramref name="pos"/>.</summary>
        private static ParkingState.Chip ChipFor(Slot slot, Vector2 pos)
        {
            DynamicThing occ = slot != null ? slot.Get() : null;
            Sprite icon = null;
            try { if (occ != null) icon = occ.GetThumbnail(); } catch { }
            return new ParkingState.Chip
            {
                Source = Pinned(slot),
                Icon = icon,
                Name = occ != null ? occ.DisplayName : "?",
                Pos = pos,
            };
        }

        /// <summary>A screen point past the parking line (RadialMenu.BeyondParkingLine: outer radius +
        /// 30 px) — where a release PARKS a chip.</summary>
        private static Vector2 BeyondParkingLine()
        {
            float r = 240f;
            try { if (UIAConfig.RadialOuterRadius != null) r = UIAConfig.RadialOuterRadius.Value; } catch { }
            return DrawUtil.ScreenCenter + new Vector2(r + 140f, 0f);
        }

        private static GameObject NewFixtureCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = -32000;
            var g = go.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            g.interactable = false;
            g.blocksRaycasts = false;
            _temp.Add(go);
            return go;
        }

        private static void DestroyFixture(GameObject go)
        {
            if (go == null) return;
            _temp.Remove(go);
            UnityEngine.Object.Destroy(go);
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>' && inTag) { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsAscii(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < ' ' || s[i] > '~') return false;
            return true;
        }

        private static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c >= ' ' && c <= '~' ? c : '?');
            }
            return sb.ToString();
        }

        private static string F(float v) { return v.ToString("0.00", Inv); }

        private static string Describe(Exception e)
        {
            if (e == null) return "?";
            if (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            string where = "";
            try
            {
                string st = e.StackTrace;
                if (!string.IsNullOrEmpty(st))
                {
                    int nl = st.IndexOf('\n');
                    where = " @ " + (nl > 0 ? st.Substring(0, nl) : st).Trim();
                }
            }
            catch { }
            return e.GetType().Name + ": " + e.Message + where;
        }

        private static void LogLine(string s)
        {
            _log.Append(Ascii(s)).Append("\r\n");
        }

        private static void WriteLog()
        {
            if (_logPath == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                File.WriteAllText(_logPath, _log.ToString(), Encoding.ASCII);
            }
            catch (Exception e)
            {
                try { UIALog.Warn("uiatest: could not write the log: " + e.Message); } catch { }
            }
        }

        private static void Say(string msg, ConsoleColor color)
        {
            try { ConsoleWindow.Print(Ascii(msg), color); } catch { }
        }

        // ================================================================== suite: drop (D-004 / A1)

        private static IEnumerator SuiteDrop()
        {
            yield return Test("drop.a", "Alt-grabbed battery out of a WORLD device: every close route cancels it (A1)", DropWorldChipCancels);
            yield return Test("drop.b", "Carried item parked past the line: Close() and CloseAll drop it, exactly once (D-004)", DropCarriedChip);
            yield return Test("drop.c", "Parking chip cap - the CONSTANT only (its enforcement is pointer-driven, NOT exercised)", DropChipCap);
            yield return Test("drop.d", "The electric jetpack's own Lock locks its battery slot, and DropToWorld then refuses it", DropLockedSlot);
            yield return Test("drop.e", "A LOCKED slot (lock set directly on a TEST flashlight's battery slot): DropToWorld refuses; unlocked, the same call drops", DropLockedDirect);
        }

        private static IEnumerator DropWorldChipCancels(TestCase t)
        {
            var ctl = RadialController.Active;
            if (ctl == null) { t.Skip("the radial controller is not running (radial half disabled?)"); yield break; }
            HookDropCounter(t);

            var fl = SpawnInWorld("ItemFlashlight", WorldSpawnMeters, t);
            if (fl == null) yield break;
            yield return Frames(3);
            Slot bslot = FirstSlot(fl, Slot.Class.Battery, true);
            if (bslot == null) { t.Skip("ItemFlashlight exposes no vanilla-visible battery slot on this build"); yield break; }
            DynamicThing bat = bslot.Get();
            if (bat == null)
            {
                bat = SpawnIntoSlot("ItemBatteryCell", bslot, t);
                yield return Frames(2);
            }
            if (bat == null || bslot.Get() != bat) { t.Fail("setup: could not seat a battery in the flashlight"); yield break; }
            t.Check(!ItemActions.IsCarriedByLocalPlayer(bat), "setup: the battery sits in a WORLD device (a flashlight on the floor), not carried by you");

            // a1 - the state RadialMenu.TryBeginDrag's world-slot grab + a release past the parking line leave
            var m1 = new RadialMenu();
            m1._parking.Chips.Add(ChipFor(bslot, BeyondParkingLine()));
            ResetDropCounter();
            m1.Close();
            yield return Frames(1);
            t.Check(bslot.Get() == bat && bat.ParentSlot == bslot, "a1 parked chip + RadialMenu.Close(): the battery is still in the device");
            t.Check(m1._parking.Chips.Count == 0 && m1._parking.Dragging == null, "a1 the close cleared the parking state");
            CheckDrops(t, 0, 0, "a1 the chip-level A1 gate cancelled it before any drop message");

            // a2 - still ON the cursor (in flight) when the menu closes
            var m2 = new RadialMenu();
            m2._parking.Dragging = ChipFor(bslot, BeyondParkingLine());
            ResetDropCounter();
            m2.Close();
            yield return Frames(1);
            t.Check(bslot.Get() == bat && bat.ParentSlot == bslot, "a2 in-flight drag + Close(): the battery is still in the device");
            CheckDrops(t, 0, 0, "a2 no drop message");

            // a3 - the live controller's programmatic close (guards, stand-downs, F6 all route here)
            ctl._menu._parking.Chips.Add(ChipFor(bslot, BeyondParkingLine()));
            ResetDropCounter();
            ctl.CloseAll("uiatest-drop-a");
            yield return Frames(1);
            t.Check(bslot.Get() == bat && bat.ParentSlot == bslot, "a3 parked chip + RadialController.CloseAll: the battery is still in the device");
            CheckDrops(t, 0, 0, "a3 no drop message");

            // a4 - the funnel's own backstop, whatever a caller checked
            ResetDropCounter();
            bool dropped = ItemActions.DropToWorld(Pinned(bslot));
            yield return Frames(1);
            t.Check(!dropped && bslot.Get() == bat, "a4 ItemActions.DropToWorld itself refuses an item you do not carry");
        }

        private static IEnumerator DropCarriedChip(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            var ctl = RadialController.Active;
            if (ctl == null) { t.Skip("the radial controller is not running (radial half disabled?)"); yield break; }
            HookDropCounter(t);

            Slot hand = ActiveHand();
            var item = SpawnIntoSlot("ItemBatteryCell", hand, t);
            if (item == null) yield break;
            yield return Frames(2);
            t.Check(ItemActions.IsCarriedByLocalPlayer(item), "setup: the item is carried (in your hand)");

            var m = new RadialMenu();
            m._parking.Chips.Add(ChipFor(hand, BeyondParkingLine()));
            ResetDropCounter();
            m.Close();
            yield return Frames(2);
            CheckOnGround(t, item, hand, "b1 RadialMenu.Close()");
            CheckDrops(t, 1, 1, "b1 exactly one drop message");
            m.Close();   // a replayed close must find nothing left to drop
            yield return Frames(1);
            CheckDrops(t, 1, 1, "b1 a second Close() sends nothing more");

            var item2 = SpawnIntoSlot("ItemBatteryCell", hand, t);
            if (item2 == null) yield break;
            yield return Frames(2);
            ctl._menu._parking.Chips.Add(ChipFor(hand, BeyondParkingLine()));
            ResetDropCounter();
            ctl.CloseAll("uiatest-drop-b");
            yield return Frames(2);
            CheckOnGround(t, item2, hand, "b2 RadialController.CloseAll");
            CheckDrops(t, 1, 1, "b2 exactly one drop message");
            t.NotProven("an IN-FLIGHT carried drag joining the drop: it joins only while the LIVE pointer is past the parking line (RadialMenu.InFlightJoinsDrop) - pointer-driven, needs OS input; only PARKED chips are driven here");
        }

        private static void CheckOnGround(TestCase t, DynamicThing item, Slot fromHand, string what)
        {
            bool inWorld = item != null && item.ParentSlot == null && !item.IsBeingDestroyed;
            t.Check(inWorld, what + ": the item is on the ground");
            t.Check(fromHand != null && fromHand.Get() == null, what + ": the hand is empty");
            var h = Guards.LocalHuman;
            if (inWorld && h != null)
            {
                float d = Vector3.Distance(h.Position, item.Position);
                t.Check(d <= 4f, what + ": it landed at your feet (" + F(d) + " m)");
            }
        }

        private static IEnumerator DropChipCap(TestCase t)
        {
            t.Check(ParkingState.MaxChips == 12, "PROVEN: the parking cap CONSTANT is 12 chips (ParkingState.MaxChips = " + ParkingState.MaxChips + ")");
            t.NotProven("that anything HONOURS the cap: it is enforced where a release parks a chip (RadialMenu.ResolveDragRelease) and where a close folds the in-flight chip in (ReleaseHeldItems) - both pointer-driven; parking a 13th chip honestly needs OS input, so a 13th chip being refused is NOT tested");
            yield break;
        }

        private const string JetpackPrefab = "ItemJetpackTurbine";

        private static IEnumerator DropLockedSlot(TestCase t)
        {
            // Explicit, BEFORE any spawn: the only JetpackElectric prefab is tagged NotSpawnable in the game
            // data (asset rip: GameObject/ItemJetpackTurbine.prefab:21 "m_TagString: NotSpawnable"), so
            // vanilla's creative spawn refuses it (27758 OnServer.cs:774) and so does this harness. Should a
            // future build make it spawnable, the runtime-lock path below runs as written.
            DynamicThing jp = null;
            try { jp = Prefab.Find<DynamicThing>(JetpackPrefab); } catch { }
            bool notSpawnable = false;
            try { notSpawnable = jp != null && jp.tag == "NotSpawnable"; } catch { }
            if (jp == null || notSpawnable)
            {
                t.Skip(JetpackPrefab + " (the only JetpackElectric - the one item whose slot vanilla locks at runtime, JetpackElectric.cs:165-178) "
                    + (jp == null ? "does not exist on this build" : "is tagged NotSpawnable, so vanilla's creative spawn refuses it (OnServer.cs:774) and so does this harness")
                    + " - the jetpack's own Lock -> BatterySlot.IsLocked path is NOT proven; drop.e proves the DropToWorld refusal on a directly locked test slot");
                yield break;
            }
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            HookDropCounter(t);

            var jet = SpawnIntoSlot(JetpackPrefab, ActiveHand(), t);
            if (jet == null) yield break;
            yield return Frames(3);
            Slot bs = FirstSlot(jet, Slot.Class.Battery, false);
            if (bs == null) { t.Skip(JetpackPrefab + " has no battery slot on this build"); yield break; }
            if (bs.Get() == null)
            {
                SpawnIntoSlot("ItemBatteryCell", bs, t);
                yield return Frames(2);
            }
            DynamicThing bat = bs.Get();
            if (bat == null) { t.Fail("setup: could not seat a battery in the jetpack"); yield break; }
            Interactable lk = null;
            try { lk = jet.InteractLock; } catch { }
            if (lk == null) { t.Skip("the jetpack has no Lock interaction on this build"); yield break; }

            // Lock it through the real funnel a radial "Lock" wedge uses (ItemActions.ToggleInteractable ->
            // Thing.Interact). JetpackElectric.OnInteractableUpdated then sets BatterySlot.IsLocked
            // (27758 JetpackElectric.cs:165-178).
            if (!bs.IsLocked) ItemActions.ToggleInteractable(lk);
            for (int i = 0; i < 30 && !bs.IsLocked; i++) yield return null;
            if (!bs.IsLocked)
            {
                t.Skip("the jetpack's Lock interaction did not lock its battery slot here - the locked-slot refusal (Slot.IsLocked in DropToWorld) stays unexercised");
                yield break;
            }
            t.Check(ItemActions.IsCarriedByLocalPlayer(bat), "setup: the battery is carried (jetpack in your hand) and its slot is LOCKED");

            ResetDropCounter();
            bool ok = ItemActions.DropToWorld(Pinned(bs));
            yield return Frames(1);
            t.Check(!ok && bs.Get() == bat, "DropToWorld refuses a LOCKED slot (vanilla SlotDisplayButton.PlayerMoveToWorld parity)");

            var m = new RadialMenu();
            m._parking.Chips.Add(ChipFor(bs, BeyondParkingLine()));
            ResetDropCounter();
            m.Close();
            yield return Frames(1);
            t.Check(bs.Get() == bat, "a parked chip of the locked battery stays put when the wheel closes");
            CheckDrops(t, 1, 0, "the close asked the funnel once and the funnel refused");

            if (bs.IsLocked) ItemActions.ToggleInteractable(lk);   // tidy: unlock before the despawn
        }

        /// <summary>drop.e: the DropToWorld lock refusal as a unit check that needs no jetpack. The lock is
        /// set directly on a TEST flashlight's battery slot (<see cref="LockSlot_TestOnly"/> - the field
        /// JetpackElectric itself writes), the refusal is asserted, then the SAME call on the SAME slot,
        /// unlocked, must drop it - so the refusal is proven to be the lock and nothing else
        /// (ItemActions.DropToWorld: <c>source.Slot.IsLocked</c> after the possession check).</summary>
        private static IEnumerator DropLockedDirect(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            HookDropCounter(t);

            var fl = SpawnIntoSlot("ItemFlashlight", ActiveHand(), t);
            if (fl == null) yield break;
            yield return Frames(3);
            Slot bs = FirstSlot(fl, Slot.Class.Battery, true);
            if (bs == null) { t.Skip("ItemFlashlight exposes no vanilla-visible battery slot on this build"); yield break; }
            DynamicThing bat = bs.Get();
            if (bat == null)
            {
                bat = SpawnIntoSlot("ItemBatteryCell", bs, t);
                yield return Frames(2);
            }
            if (bat == null || bs.Get() != bat) { t.Fail("setup: could not seat a battery in the test flashlight"); yield break; }
            // A battery the flashlight prefab brought along is part of this fixture: track it, because the
            // control step drops it OUT of the flashlight, and a loose battery is not despawned with it.
            if (!_spawned.Contains(bat)) _spawned.Add(bat);
            t.Check(ItemActions.IsCarriedByLocalPlayer(bat) && !bs.IsLocked,
                "setup: the battery is carried (test flashlight in your hand) and its slot starts UNLOCKED");

            if (!LockSlot_TestOnly(bs, true, t)) yield break;
            try
            {
                ResetDropCounter();
                bool refused = !ItemActions.DropToWorld(Pinned(bs));
                yield return Frames(1);
                t.Check(refused && bs.Get() == bat, "LOCKED: DropToWorld refuses it and the battery stays (vanilla SlotDisplayButton.PlayerMoveToWorld parity)");

                var m = new RadialMenu();
                m._parking.Chips.Add(ChipFor(bs, BeyondParkingLine()));
                ResetDropCounter();
                m.Close();
                yield return Frames(1);
                t.Check(bs.Get() == bat, "LOCKED: a parked chip of the battery stays put when the wheel closes");
                CheckDrops(t, 1, 0, "LOCKED: the close asked the funnel once and the funnel refused");
            }
            finally { LockSlot_TestOnly(bs, false, null); }

            // Control: the SAME slot, unlocked, through the SAME call.
            ResetDropCounter();
            bool dropped = ItemActions.DropToWorld(Pinned(bs));
            yield return Frames(2);
            t.Check(dropped && bat.ParentSlot == null && bs.Get() == null,
                "control, UNLOCKED: the same DropToWorld call drops it - so the refusal above was the lock, nothing else");
            CheckDrops(t, 1, 1, "control: exactly one accepted drop message");
            t.Info("scope: the lock was set by the harness, not by a vanilla item - a vanilla item locking its own slot at runtime is drop.d's claim");
        }

        // ================================================================== suite: sealed (D-005 / A3 / A4)

        private static IEnumerator SuiteSealed()
        {
            yield return Test("sealed.suit", "Emergency EVA suit: hidden slots are sealed - no wedges, flagged by the scanner, never offered", SealedSuit);
            yield return Test("sealed.coil", "Cable coil: a plain stack, never storage, never a bindable bag", SealedCoil);
            yield return Test("sealed.trap", "Legacy item trapped in a coil: rescue cell, take-out to a free hand, no way back in", SealedTrap);
        }

        private static IEnumerator SealedSuit(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            Slot hand = ActiveHand();
            var suit = SpawnIntoSlot("ItemEmergencyEvaSuit", hand, t);
            if (suit == null) yield break;
            yield return Frames(3);

            // Verdict honesty (safety review): the old per-slot check "sealed == !IsInteractable" restated
            // ItemActions.IsSealedSlot's own definition for a non-stack item (it returns exactly
            // !IsVanillaVisibleSlot there) - it could never fail, so it is gone. What is asserted instead is
            // what the seal DOES (below) plus a negative control that CAN fail: the seal must not over-reach
            // onto slots vanilla draws.
            var sealedSlots = new HashSet<Slot>();
            int total = 0, visible = 0, occupied = 0;
            for (int i = 0; suit.Slots != null && i < suit.Slots.Count; i++)
            {
                var s = suit.Slots[i];
                if (s == null) continue;
                total++;
                if (ItemActions.IsVanillaVisibleSlot(s)) visible++;
                if (ItemActions.IsSealedSlot(s)) { sealedSlots.Add(s); if (s.Get() != null) occupied++; }
            }
            t.Info(total + " slot(s): " + sealedSlots.Count + " sealed, " + visible + " vanilla-visible, " + occupied + " sealed slot(s) occupied");
            if (sealedSlots.Count == 0) { t.Skip("this build's Emergency EVA suit exposes every slot - nothing is sealed"); yield break; }

            int walked = 0;
            var entries = ItemMenuBuilder.BuildManageEntries(suit, hand, false);
            int touching = CountEntriesTouching(entries, sealedSlots, 2, ref walked);
            t.Check(touching == 0, "ItemMenuBuilder offers no wedge for a sealed slot (" + walked + " wedges walked, 2 levels deep)");

            var scan = InventoryScanner.Scan(ScanDepth(), true);
            int seen = 0, flagged = 0;
            for (int i = 0; i < scan.Count; i++)
            {
                if (scan[i].Slot == null || !sealedSlots.Contains(scan[i].Slot)) continue;
                seen++;
                if (scan[i].Sealed) flagged++;
            }
            t.Check(seen > 0 && flagged == seen, "InventoryScanner flags the suit's hidden slots Sealed (" + flagged + "/" + seen + ") - the flag search, the recent-item wedge and SmartStow skip");

            if (occupied > 0)
            {
                var found = InventoryScanner.FindCompatible(OtherHand(hand), ScanDepth(), true);
                bool offered = false;
                for (int i = 0; i < found.Count; i++) if (found[i].Slot != null && sealedSlots.Contains(found[i].Slot)) offered = true;
                t.Check(!offered, "FindCompatible never offers an item from a sealed slot (" + occupied + " occupied)");
            }
            else t.NotProven("FindCompatible skipping the CONTENTS of a sealed slot: the suit's sealed slots spawned EMPTY, so there was nothing to offer (a check here could not fail) - sealed.trap proves it with a real trapped item");

            if (visible == 0) t.Check(!GridModel.IsStorageContainer(suit), "the Universal Inventory does not treat the suit as storage");
            else t.Info("the suit has " + visible + " vanilla-visible slot(s) on this build - its storage status is theirs");

            // The negative control: vanilla-visible slots of a NON-stack item are never sealed - on the suit
            // itself when it has any, else on a carried bag in the other hand.
            if (visible > 0) CheckVisibleNotSealed(t, suit, "the suit");
            else
            {
                string bagName = FirstExistingPrefab(null, BagPrefabs);
                var bag = bagName != null ? SpawnIntoSlot(bagName, OtherHand(hand), t) : null;
                if (bag == null) { t.NotProven("the negative control (a non-stack container's visible slots are not sealed): no bag could be spawned"); yield break; }
                yield return Frames(2);
                CheckVisibleNotSealed(t, bag, "a carried '" + bagName + "' (a non-stack container)");
            }
        }

        /// <summary>A negative control that CAN fail: none of <paramref name="owner"/>'s vanilla-visible
        /// slots may be sealed (D-005 seals only hidden slots and stack slots). Catches an over-reaching
        /// seal, which would make ordinary storage unusable.</summary>
        private static void CheckVisibleNotSealed(TestCase t, Thing owner, string what)
        {
            int visible = 0, wronglySealed = 0;
            for (int i = 0; owner != null && owner.Slots != null && i < owner.Slots.Count; i++)
            {
                var s = owner.Slots[i];
                if (s == null || !ItemActions.IsVanillaVisibleSlot(s)) continue;
                visible++;
                if (ItemActions.IsSealedSlot(s)) wronglySealed++;
            }
            if (visible == 0) { t.NotProven("the negative control: " + what + " has no vanilla-visible slot on this build"); return; }
            t.Check(wronglySealed == 0, "negative control: none of " + what + "'s " + visible + " vanilla-visible slot(s) is sealed ("
                + wronglySealed + " sealed) - the seal does not over-reach onto slots vanilla draws");
        }

        /// <summary>Walk a built radial level (and its branches / swipe-outs, <paramref name="depth"/>
        /// levels down) and count wedges that point at a forbidden slot — as a wedge's own slot
        /// (Tag), a drag source or a drop target. Providers are read-only builders; they are invoked
        /// fenced.</summary>
        private static int CountEntriesTouching(List<RadialEntry> entries, HashSet<Slot> bad, int depth, ref int walked)
        {
            if (entries == null) return 0;
            int hits = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                walked++;
                var ts = e.Tag as Slot;
                if (ts != null && bad.Contains(ts)) hits++;
                if (e.DragSource != null && e.DragSource.Slot != null && bad.Contains(e.DragSource.Slot)) hits++;
                if (e.DropSlot != null && bad.Contains(e.DropSlot)) hits++;
                if (depth <= 0 || walked > 400) continue;
                if (e.ChildProvider != null)
                {
                    List<RadialEntry> kids = null;
                    try { kids = e.ChildProvider(); } catch { }
                    hits += CountEntriesTouching(kids, bad, depth - 1, ref walked);
                }
                if (e.SlideOutProvider != null)
                {
                    List<RadialEntry> kids = null;
                    try { kids = e.SlideOutProvider(); } catch { }
                    hits += CountEntriesTouching(kids, bad, depth - 1, ref walked);
                }
            }
            return hits;
        }

        private static IEnumerator SealedCoil(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            Slot coilHand = ActiveHand();
            var coil = SpawnIntoSlot("ItemCableCoil", coilHand, t);
            if (coil == null) yield break;
            yield return Frames(3);

            var st = coil as Stackable;
            t.Check(st != null, "the cable coil is a Stackable (MultiMergeConstructor : MultiConstructor : Stackable)");
            if (st != null) t.Info("quantity " + st.Quantity + " / " + st.MaxQuantity);
            t.Check(!GridModel.IsStorageContainer(coil), "GridModel.IsStorageContainer(coil) is false - no phantom grid");
            t.Check(!ItemMenuBuilder.IsBindableBag(coil), "ItemMenuBuilder.IsBindableBag(coil) is false - never Ctrl+number storage");
            if (st != null && st.Quantity > 1)
                t.Check(ItemMenuBuilder.IsPlainStack(coil), "ItemMenuBuilder.IsPlainStack(coil) is true - swiping it lands on the split choices");
            else t.NotProven("IsPlainStack(coil): the spawned coil holds fewer than 2 (IsPlainStack needs a splittable stack)");

            // Verdict honesty (safety review): "every coil slot is sealed" is IsSealedSlot's own Stackable
            // rule restated (any slot of a Stackable owner is sealed by definition, and the coil being a
            // Stackable is already asserted above) - it could never fail, so it is reported, not asserted.
            int slots = 0, sealedCount = 0;
            for (int i = 0; coil.Slots != null && i < coil.Slots.Count; i++)
            {
                var s = coil.Slots[i];
                if (s == null) continue;
                slots++;
                if (ItemActions.IsSealedSlot(s)) sealedCount++;
            }
            t.Info(slots + " slot(s) on this build's coil, " + sealedCount + " sealed (by the Stackable rule itself - not re-asserted)");

            var entries = ItemMenuBuilder.BuildManageEntries(coil, coil.ParentSlot, false);
            bool slotWedge = false;
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && entries[i].Tag is Slot) slotWedge = true;
            t.Check(!slotWedge, "the coil's manage ring shows no slot / STOW wedge (" + entries.Count + " wedges)");

            // The negative control that CAN fail: the stack rule seals the COIL's slots, not every carried
            // container's - a NON-stack bag in the other hand keeps every vanilla-visible slot unsealed.
            string bagName = FirstExistingPrefab(null, BagPrefabs);
            var bag = bagName != null ? SpawnIntoSlot(bagName, OtherHand(coilHand), t) : null;
            if (bag == null) { t.NotProven("the negative control (a non-stack container's visible slots are not sealed): no bag could be spawned"); yield break; }
            yield return Frames(2);
            CheckVisibleNotSealed(t, bag, "a carried '" + bagName + "' (a NON-stack container)");
        }

        private static IEnumerator SealedTrap(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            var h = Guards.LocalHuman;
            Slot coilHand = h.LeftHandSlot, freeHand = h.RightHandSlot;

            var coil = SpawnIntoSlot("ItemCableCoil", coilHand, t);
            if (coil == null) yield break;
            yield return Frames(3);
            Slot cslot = null;
            for (int i = 0; coil.Slots != null && i < coil.Slots.Count; i++)
                if (coil.Slots[i] != null) { cslot = coil.Slots[i]; break; }
            if (cslot == null) { t.Skip("this build's cable coil carries no slot - the legacy trap cannot exist here"); yield break; }
            t.Check(ItemActions.IsSealedSlot(cslot), "the coil's slot is sealed");

            string trapName = PickTrapItem(cslot);
            if (trapName == null) { t.Skip("no candidate item fits the coil slot (type " + cslot.Type + ")"); yield break; }
            var item = SpawnIntoSlot(trapName, freeHand, t);
            if (item == null) yield break;
            yield return Frames(2);
            bool isBelt = trapName == "ItemToolBelt";
            int pickerBefore = isBelt ? PickerCandidates() : -1;

            if (!LegacyTrap_DirectPlace_TestOnly(item, cslot, t)) yield break;
            yield return Frames(2);
            if (item.ParentSlot != cslot) { t.Skip("the coil slot refused the item even ungated (DynamicThing.CanEnter) - no legacy state to rescue"); yield break; }
            t.Check(freeHand.Get() == null, "setup: '" + Ascii(item.DisplayName) + "' is now trapped in the coil (legacy state reproduced)");

            // --- the grid keeps it visible, take-out-only ---
            t.Check(GridModel.IsCellSlot(cslot, item), "grid: the trapped slot renders as a cell");
            t.Check(GridModel.IsTakeOnlySlot(cslot), "grid: that cell is TAKE-OUT-ONLY");
            var root = GridModel.BuildRoot();
            var node = FindNode(root, coil, 0);
            t.Check(node != null, "grid: the coil keeps a rescue region while something is trapped in it");
            if (node != null)
                t.Check(node.Slots.Contains(cslot), "grid: the rescue region holds the trapped slot (" + node.Slots.Count + " cell(s))");
            t.Check(!GridModel.IsStorageContainer(coil), "grid: the coil is still not storage");

            // --- listings never offer it ---
            var scan = InventoryScanner.Scan(ScanDepth(), true);
            ScannedSlot entry = null;
            for (int i = 0; i < scan.Count; i++) if (scan[i].Slot == cslot) { entry = scan[i]; break; }
            t.Check(entry != null && entry.Sealed, "scanner: the trapped slot is flagged Sealed (search / recent item / SmartStow skip it)");
            var cands = InventoryScanner.FindCompatible(freeHand, ScanDepth(), true);
            bool offered = false;
            for (int i = 0; i < cands.Count; i++) if (cands[i].Slot == cslot) offered = true;
            t.Check(!offered, "FindCompatible never offers the trapped item (search/swap candidates)");
            if (isBelt)
            {
                int pickerAfter = PickerCandidates();
                t.Check(pickerBefore >= 1 && pickerAfter == pickerBefore - 1,
                    "the Q belt picker listed the spare belt in your hand (" + pickerBefore + " candidate(s)) and no longer lists it once trapped (" + pickerAfter + ")");
            }

            // --- the rescue: one gated move into a FREE hand, never a swap ---
            bool took = ItemActions.TakeToFreeHand(new ScannedSlot { Slot = cslot, Holder = coil, Location = "" }.Pin());
            yield return Frames(2);
            t.Check(took, "ItemActions.TakeToFreeHand accepted the rescue");
            t.Check(item.ParentSlot == freeHand && cslot.Get() == null, "the item came out into your FREE hand; the coil slot is empty");
            t.Check(coil.ParentSlot == coilHand, "the coil stayed in the other hand (no swap)");

            // --- and it can never go back in ---
            var back = Pinned(freeHand);
            t.Check(!ItemActions.DragTo(back, cslot), "DragTo into the sealed coil slot is refused");
            t.Check(!ItemActions.SwapIntoSlot(Pinned(freeHand), cslot), "SwapIntoSlot into it is refused");
            t.Check(!ItemActions.DragAllOfTypeTo(Pinned(freeHand), cslot), "a Shift-drag into it is refused");
            yield return Frames(1);
            t.Check(item.ParentSlot == freeHand && cslot.Get() == null, "nothing moved: the item is still in your hand, the coil slot still empty");
        }

        /// <summary>A trap item that fits the coil slot's type: a tool belt when the slot takes one (so
        /// the belt picker's A4 exclusion is exercised too), else a battery cell or an ingot.</summary>
        private static string PickTrapItem(Slot slot)
        {
            string[] candidates = { "ItemToolBelt", "ItemBatteryCell", "ItemIronIngot" };
            for (int i = 0; i < candidates.Length; i++)
            {
                DynamicThing p = null;
                try { p = Prefab.Find<DynamicThing>(candidates[i]); } catch { }
                if (p != null && InventoryScanner.IsTypeCompatible(slot, p)) return candidates[i];
            }
            return null;
        }

        /// <summary>Selectable belt-picker candidates (everything but the "Worn" wedge).</summary>
        private static int PickerCandidates()
        {
            int n = 0;
            var picker = ToolbeltRadialFeature.BuildBeltPicker();
            for (int i = 0; i < picker.Count; i++)
                if (picker[i] != null && picker[i].ActionText != "Worn") n++;
            return n;
        }

        private static ContainerNode FindNode(ContainerNode n, DynamicThing container, int depth)
        {
            if (n == null || depth > 24) return null;
            if (n.Container == container) return n;
            if (n.Children == null) return null;
            for (int i = 0; i < n.Children.Count; i++)
            {
                var hit = FindNode(n.Children[i], container, depth + 1);
                if (hit != null) return hit;
            }
            return null;
        }

        // ================================================================== suite: sweep (D-018)

        private static IEnumerator SuiteSweep()
        {
            yield return Test("sweep.all", "Shift-drag moves every same-type stack into the other bag; the other type stays (D-018)", SweepAll);
            yield return Test("sweep.occupied", "Shift-drag onto an OCCUPIED slot is one swap - no sweep", SweepOccupied);
            yield return Test("sweep.maytouch", "SweepMayTouch: never a sealed slot, never a hidden structure slot", SweepMayTouchTest);
        }

        private static readonly string[] BagPrefabs = { "ItemHardBackpack", "DynamicCrate" };

        private static IEnumerator SweepAll(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            var h = Guards.LocalHuman;
            string bagName = FirstExistingPrefab(t, BagPrefabs);
            if (bagName == null) yield break;
            var a = SpawnIntoSlot(bagName, h.LeftHandSlot, t);
            var b = SpawnIntoSlot(bagName, h.RightHandSlot, t);
            if (a == null || b == null) yield break;
            yield return Frames(3);
            var fa = FreeStorageSlots(a);
            var fb = FreeStorageSlots(b);
            if (fa.Count < 4 || fb.Count < 4) { t.Skip("'" + bagName + "' has too few free generic slots (" + fa.Count + "/" + fb.Count + ")"); yield break; }

            var fe = new List<DynamicThing>();
            for (int i = 0; i < 3; i++)
            {
                var x = SpawnIntoSlot("ItemIronIngot", fa[i], t);
                if (x == null) yield break;
                fe.Add(x);
            }
            var cu = SpawnIntoSlot("ItemCopperIngot", fa[3], t);
            if (cu == null) yield break;
            yield return Frames(2);

            Slot dest = fb[0];
            bool ok = ItemActions.DragAllOfTypeTo(new ScannedSlot { Slot = fa[0], Holder = a, Location = "" }.Pin(), dest);
            yield return Frames(2);
            t.Check(ok, "ItemActions.DragAllOfTypeTo accepted the Shift-drag");
            t.Check(fe[0].ParentSlot == dest, "the dragged stack landed exactly where it was dropped");
            int inB = 0;
            for (int i = 0; i < fe.Count; i++) if (fe[i].ParentSlot != null && fe[i].ParentSlot.Parent == b) inB++;
            t.Check(inB == 3, "all 3 iron stacks followed into the other bag (" + inB + "/3)");
            t.Check(cu.ParentSlot == fa[3], "the copper stack (a different type) stayed where it was");
        }

        private static IEnumerator SweepOccupied(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            var h = Guards.LocalHuman;
            string bagName = FirstExistingPrefab(t, BagPrefabs);
            if (bagName == null) yield break;
            var a = SpawnIntoSlot(bagName, h.LeftHandSlot, t);
            var b = SpawnIntoSlot(bagName, h.RightHandSlot, t);
            if (a == null || b == null) yield break;
            yield return Frames(3);
            var fa = FreeStorageSlots(a);
            var fb = FreeStorageSlots(b);
            if (fa.Count < 2 || fb.Count < 1) { t.Skip("'" + bagName + "' has too few free generic slots"); yield break; }

            var fe0 = SpawnIntoSlot("ItemIronIngot", fa[0], t);
            var fe1 = SpawnIntoSlot("ItemIronIngot", fa[1], t);
            var cu = SpawnIntoSlot("ItemCopperIngot", fb[0], t);
            if (fe0 == null || fe1 == null || cu == null) yield break;
            yield return Frames(2);

            bool ok = ItemActions.DragAllOfTypeTo(new ScannedSlot { Slot = fa[0], Holder = a, Location = "" }.Pin(), fb[0]);
            yield return Frames(2);
            t.Check(ok, "the drag onto the occupied slot went through");
            t.Check(fe0.ParentSlot == fb[0] && cu.ParentSlot == fa[0], "it was ONE swap: iron in the target slot, copper back in the source slot");
            t.Check(fe1.ParentSlot == fa[1], "the second iron stack did NOT follow (no sweep on a swap - vanilla parity)");
        }

        private static IEnumerator SweepMayTouchTest(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            string bagName = FirstExistingPrefab(t, BagPrefabs);
            if (bagName == null) yield break;
            var bag = SpawnIntoSlot(bagName, ActiveHand(), t);
            if (bag == null) yield break;
            yield return Frames(3);
            var free = FreeStorageSlots(bag);
            if (free.Count < 3) { t.Skip("'" + bagName + "' has too few free slots"); yield break; }
            bool may;
            if (!SweepMayTouch(free[0], out may))
            {
                t.Skip("ItemActions.SweepMayTouch(Slot) not found by reflection (renamed?) - update the harness");
                yield break;
            }
            t.Check(may, "an ordinary vanilla-visible bag slot may be swept");

            // A sealed slot: the coil's (a stack's slots are always sealed), else an emergency suit's hidden one.
            Slot sealedSlot = null;
            var coil = SpawnIntoSlot("ItemCableCoil", free[1], t);
            yield return Frames(2);
            if (coil != null && coil.Slots != null)
                for (int i = 0; i < coil.Slots.Count && sealedSlot == null; i++)
                    if (coil.Slots[i] != null) sealedSlot = coil.Slots[i];
            if (sealedSlot == null)
            {
                var suit = SpawnIntoSlot("ItemEmergencyEvaSuit", free[2], t);
                yield return Frames(2);
                if (suit != null && suit.Slots != null)
                    for (int i = 0; i < suit.Slots.Count && sealedSlot == null; i++)
                        if (suit.Slots[i] != null && ItemActions.IsSealedSlot(suit.Slots[i])) sealedSlot = suit.Slots[i];
            }
            if (sealedSlot != null)
                t.Check(SweepMayTouch(sealedSlot, out may) && !may, "a SEALED slot is never swept (" + Ascii(sealedSlot.Parent.DisplayName) + ")");
            else t.NotProven("the SEALED-slot half: no sealed slot available on this build (the coil has no slot, the suit exposes all)");

            // A world structure's hidden slot (a vending machine's stock) - only if one is near.
            Slot hidden = FindNearbyHiddenStructureSlot(12f);
            if (hidden != null)
            {
                t.Check(!ItemActions.IsSealedSlot(hidden), "a structure slot is never 'sealed' by the D-005 rule (owner is not a DynamicThing)");
                t.Check(SweepMayTouch(hidden, out may) && !may, "...yet a HIDDEN structure slot is never swept (the visibility half, A8) - " + Ascii(hidden.Parent.DisplayName));
            }
            else t.NotProven("the HIDDEN-structure-slot half: no structure with a non-interactable slot within 12 m (e.g. a vending machine 4-12 m away exercises it - not within 3 m, where the machine-intake check refuses the run)");
        }

        /// <summary>REFLECTION, deliberately: <c>ItemActions.SweepMayTouch</c> is private and
        /// Core/ItemActions.cs is being rewritten by another session (the 0.9.8.0 SmartStow capture), so
        /// it is read by name instead of widened there. A rename degrades the test to SKIP.</summary>
        private static bool SweepMayTouch(Slot slot, out bool result)
        {
            result = false;
            try
            {
                var m = typeof(ItemActions).GetMethod("SweepMayTouch", AnyStatic, null, new[] { typeof(Slot) }, null);
                if (m == null || m.ReturnType != typeof(bool)) return false;
                result = (bool)m.Invoke(null, new object[] { slot });
                return true;
            }
            catch { return false; }
        }

        private static Slot FindNearbyHiddenStructureSlot(float radius)
        {
            var h = Guards.LocalHuman;
            if (h == null) return null;
            Collider[] cols;
            try { cols = Physics.OverlapSphere(h.Position, radius); }
            catch { return null; }
            var seen = new HashSet<Thing>();
            for (int i = 0; i < cols.Length; i++)
            {
                Thing th = null;
                try { th = cols[i] != null ? cols[i].GetComponentInParent<Thing>() : null; } catch { }
                if (th == null || th is DynamicThing || !seen.Add(th) || th.Slots == null) continue;
                for (int s = 0; s < th.Slots.Count; s++)
                {
                    var sl = th.Slots[s];
                    if (sl != null && !ItemActions.IsVanillaVisibleSlot(sl)) return sl;
                }
            }
            return null;
        }

        // ================================================================== suite: popup (D-006)

        private static IEnumerator SuitePopup()
        {
            yield return Test("popup.focus", "Two overlapped pinned windows: focus-ranked sort orders inside 5030..5090; a pointer-down raises", PopupFocus);
        }

        private static bool InBand(Canvas c) { return c != null && c.sortingOrder >= 5030 && c.sortingOrder <= 5090; }

        private static IEnumerator PopupFocus(TestCase t)
        {
            int before = PinnedInventoryWindow.LiveCount;
            var w1 = PinnedInventoryWindow.Create();
            if (w1 == null) { t.Fail("PinnedInventoryWindow.Create returned null"); yield break; }
            _pins.Add(w1);
            w1.Bind(null);   // chrome only, default centred geometry - no container, no pin record
            var w2 = PinnedInventoryWindow.Create();
            if (w2 == null) { t.Fail("the second PinnedInventoryWindow.Create returned null"); yield break; }
            _pins.Add(w2);
            w2.Bind(null);
            var g1 = w1.Geometry;
            w2.SetPosition(g1.x + g1.width * 0.45f, g1.y + g1.height * 0.35f);   // overlap w1's lower-right
            yield return Frames(2);

            var c1 = w1.GetComponent<Canvas>();
            var c2 = w2.GetComponent<Canvas>();
            if (c1 == null || c2 == null) { t.Fail("a pinned window has no nested canvas"); yield break; }
            t.Check(c1.overrideSorting && c2.overrideSorting, "both nested canvases override sorting (draw order == raycast priority)");
            t.Check(InBand(c1) && InBand(c2), "both sort inside 5030..5090 (" + c1.sortingOrder + ", " + c2.sortingOrder + ")");
            t.Check(c2.sortingOrder > c1.sortingOrder, "the newest window opens on top");
            w1.BringToFront();
            t.Check(c1.sortingOrder > c2.sortingOrder && InBand(c1) && InBand(c2), "focus the first: it ranks above (" + c1.sortingOrder + " > " + c2.sortingOrder + ")");
            w2.BringToFront();
            t.Check(c2.sortingOrder > c1.sortingOrder && InBand(c1) && InBand(c2), "focus the second: it ranks above again");

            Vector2 overlap, onlyW1;
            if (OverlapPoints(w1, w2, out overlap, out onlyW1))
            {
                t.Check(PinnedInventoryWindow.TopWindowAt(overlap) == w2, "the pointer-down hit test picks the TOP window in the overlap");
                var hit = PinnedInventoryWindow.TopWindowAt(onlyW1);
                t.Check(hit == w1, "on the exposed part of the LOWER window it picks that window");
                if (hit != null) hit.BringToFront();   // what RaiseWindowUnderPointerDown does after its button read
                t.Check(c1.sortingOrder > c2.sortingOrder, "the pointer-down raise brings the lower window forward");
            }
            else t.Skip("could not derive screen points from the two window rects");
            t.NotProven("the mouse-button READ that triggers the raise (Input.GetMouseButtonDown in PinnedInventoryWindow.RaiseWindowUnderPointerDown) - OS input; the handler's hit test + raise are driven directly");

            w1.Close();
            w2.Close();
            _pins.Remove(w1);
            _pins.Remove(w2);
            yield return Frames(1);
            t.Check(PinnedInventoryWindow.LiveCount == before, "closing both restores the live pinned-window count (" + before + ")");
        }

        /// <summary>Screen points (overlay canvas: world == screen px) inside BOTH panels and inside the
        /// first panel only, from each window's real "Panel" rect.</summary>
        private static bool OverlapPoints(PinnedInventoryWindow w1, PinnedInventoryWindow w2, out Vector2 both, out Vector2 onlyFirst)
        {
            both = onlyFirst = Vector2.zero;
            var p1 = w1 != null ? w1.transform.Find("Panel") as RectTransform : null;
            var p2 = w2 != null ? w2.transform.Find("Panel") as RectTransform : null;
            if (p1 == null || p2 == null) return false;
            Rect r1 = ScreenRect(p1), r2 = ScreenRect(p2);
            float xMin = Mathf.Max(r1.xMin, r2.xMin), xMax = Mathf.Min(r1.xMax, r2.xMax);
            float yMin = Mathf.Max(r1.yMin, r2.yMin), yMax = Mathf.Min(r1.yMax, r2.yMax);
            if (xMax - xMin < 8f || yMax - yMin < 8f) return false;
            both = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            for (int yi = 1; yi < 8; yi++)
                for (int xi = 1; xi < 8; xi++)
                {
                    var p = new Vector2(Mathf.Lerp(r1.xMin, r1.xMax, xi / 8f), Mathf.Lerp(r1.yMin, r1.yMax, yi / 8f));
                    if (r1.Contains(p) && !r2.Contains(p)) { onlyFirst = p; return true; }
                }
            return false;
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        // ================================================================== suite: theme (B2)

        private static IEnumerator SuiteTheme()
        {
            yield return Test("theme.arc-pin", "A pinned ArcAccent travels with its profile: AUTO under a theme without the key, restored on return (B2)", ThemeArcPin);
            yield return Test("theme.missing-key", "HudTheme.Apply: a rad: family with the key missing -> default (AUTO); no rad: family -> untouched", ThemeMissingKey);
        }

        private static string ThemeValue(List<HudDocument.ThemeEntry> theme, string key)
        {
            if (theme == null) return null;
            for (int i = 0; i < theme.Count; i++)
                if (theme[i] != null && theme[i].K == key) return theme[i].V;
            return null;
        }

        private static bool HasFamily(List<HudDocument.ThemeEntry> theme, string prefix)
        {
            if (theme == null) return false;
            for (int i = 0; i < theme.Count; i++)
                if (theme[i] != null && theme[i].K != null && theme[i].K.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static IEnumerator ThemeArcPin(TestCase t)
        {
            if (!BeginScratchProfile(t)) yield break;
            yield return Frames(3);
            var e = RadialPalette.ArcAccent;
            if (e == null || e.Config == null) { t.Fail("RadialPalette.ArcAccent is not bound"); yield break; }
            t.Check(e.FollowsTheme && string.Equals(e.DefaultValue, RadialPalette.Auto, StringComparison.OrdinalIgnoreCase),
                "ArcAccent is a theme-following entry whose default is AUTO");

            string pinHex = RadialPalette.ToHex(new Color32(0x2B, 0xD9, 0x6A, 0xFF));
            e.Value = RadialPalette.FromHex(pinHex);          // the palette path the F10 colour wheel writes
            HudProfileStore.MarkThemeChanged();               // the F10/F9 global-edit contract
            t.Check(!e.IsAuto && string.Equals(e.Config.Value, pinHex, StringComparison.OrdinalIgnoreCase),
                "pinned: ArcAccent resolves to " + pinHex + ", not AUTO");
            HudProfileStore.FlushNow();
            var mine = HudProfileStore.Active != null ? HudProfileStore.Active.Theme : null;
            t.Check(string.Equals(ThemeValue(mine, "rad:ArcAccent"), pinHex, StringComparison.OrdinalIgnoreCase),
                "the scratch profile's theme captured rad:ArcAccent=" + pinHex);

            string shipped = ShippedProfiles.ZirillianRedName;
            // What the switch will show: the on-disk copy, or (absent) its embed - ApplyShippedTheme's own
            // resolution (an in-memory clone; nothing is written under the shipped name).
            var shippedDoc = HudProfileStore.Load(shipped) ?? BuildEmbedded(shipped);
            string shippedVal = ThemeValue(shippedDoc != null ? shippedDoc.Theme : null, "rad:ArcAccent");
            bool shippedHasRad = HasFamily(shippedDoc != null ? shippedDoc.Theme : null, "rad:");
            if (!ApplyShippedTheme(shipped, t)) { t.Fail("could not show '" + shipped + "'"); yield break; }
            yield return Frames(3);
            if (shippedVal != null)
                t.Check(string.Equals(e.Config.Value, shippedVal, StringComparison.OrdinalIgnoreCase), "'" + shipped + "' carries its own ArcAccent and applies it");
            else if (shippedHasRad)
                t.Check(e.IsAuto, "under '" + shipped + "' (rad: keys, no rad:ArcAccent) the arc is back to AUTO - no bleed");
            else t.NotProven("the AUTO-under-another-theme half: '" + shipped + "' carries no rad: keys at all (themeless), so the pin legitimately stays there");

            string back;
            if (!ActivateByName(ScratchProfile, false, out back)) { t.Fail("could not switch back to the scratch profile (" + back + ")"); yield break; }
            _scratchActive = true;
            yield return Frames(3);
            t.Check(!e.IsAuto && string.Equals(e.Config.Value, pinHex, StringComparison.OrdinalIgnoreCase),
                "back on the pinned profile: its ArcAccent " + pinHex + " is restored");
        }

        private static IEnumerator ThemeMissingKey(TestCase t)
        {
            var e = RadialPalette.ArcAccent;
            var other = RadialPalette.WedgeBg;
            if (e == null || other == null) { t.Fail("radial palette not bound"); yield break; }
            string pinHex = RadialPalette.ToHex(new Color32(0xE0, 0x40, 0xC0, 0xFF));
            // This test rewrites globals: snapshot and put them back within the same frame (no yield in
            // between, so the HUD never renders the synthetic state).
            var snap = HudTheme.Snapshot();
            try
            {
                e.Value = RadialPalette.FromHex(pinHex);
                HudTheme.Apply(new List<HudDocument.ThemeEntry>
                {
                    new HudDocument.ThemeEntry { K = "rad:" + other.Name, V = other.Config.Value },
                });
                t.Check(e.IsAuto, "a theme that carries the rad: family but not rad:ArcAccent resets it to its default (AUTO)");

                e.Value = RadialPalette.FromHex(pinHex);
                HudTheme.Apply(new List<HudDocument.ThemeEntry>
                {
                    new HudDocument.ThemeEntry { K = "uiatest:none", V = "1" },
                });
                t.Check(!e.IsAuto && string.Equals(e.Config.Value, pinHex, StringComparison.OrdinalIgnoreCase),
                    "a theme with NO rad: family leaves the pin untouched (the documented absent-family contract)");
            }
            finally { HudTheme.Apply(snap); }
            yield break;
        }

        // ================================================================== suite: f10 (D-020 / D-009)

        private static IEnumerator SuiteF10()
        {
            yield return Test("f10.lowpower", "The low-power threshold reads 10 under every shipped theme (D-020)", F10LowPower);
            yield return Test("f10.scroll", "The scroll capture reads TOP when the content fits (Simple<->Advanced jump, D-020)", F10ScrollCapture);
            yield return Test("f10.input", "UiaUi.InputField carries an intrinsic LayoutElement - never a hairline (D-009)", F10InputField);
        }

        private static string[] ShippedNames()
        {
            return new[]
            {
                ShippedProfiles.StationeersBlueName,
                ShippedProfiles.StationeersBlueMinimalistName,
                ShippedProfiles.ZirillianRedName,
                ShippedProfiles.PureHudName,
            };
        }

        private static HudDocument BuildEmbedded(string name)
        {
            if (name == ShippedProfiles.StationeersBlueName) return ShippedProfiles.BuildStationeersBlue();
            if (name == ShippedProfiles.StationeersBlueMinimalistName) return ShippedProfiles.BuildStationeersBlueMinimalist();
            if (name == ShippedProfiles.ZirillianRedName) return ShippedProfiles.BuildZirillianRed();
            if (name == ShippedProfiles.PureHudName) return ShippedProfiles.BuildPureHud();
            return null;
        }

        private static IEnumerator F10LowPower(TestCase t)
        {
            var cfg = HudConfig.LowPowerThreshold;
            if (cfg == null) { t.Fail("HudConfig.LowPowerThreshold is not bound"); yield break; }
            float def = 0f;
            try { def = Convert.ToSingle(cfg.DefaultValue, Inv); } catch { }
            t.Check(Mathf.Approximately(def, 10f), "the config default is 10 (" + def.ToString("0.###", Inv) + ")");
            // Each apply below rewrites the globals: snapshot and restore within the same frame (no
            // yield in between, so the HUD never renders a borrowed theme).
            var snap = HudTheme.Snapshot();
            try
            {
                var names = ShippedNames();
                for (int i = 0; i < names.Length; i++)
                {
                    string n = names[i];
                    var emb = BuildEmbedded(n);
                    if (emb == null || emb.Theme == null) { t.Fail("the embedded '" + n + "' has no theme"); continue; }
                    HudTheme.Apply(emb.Theme);
                    t.Check(Mathf.Approximately(cfg.Value, 10f), "'" + n + "' as shipped applies LowPowerThreshold " + cfg.Value.ToString("0.###", Inv));

                    var disk = HudProfileStore.Load(n);
                    if (disk == null || disk.Theme == null) { t.NotProven("'" + n + "' ON DISK: it is not on disk here (it self-heals from the embed on first use) - only its embed was checked"); continue; }
                    HudTheme.Apply(disk.Theme);
                    float v = cfg.Value;
                    string state = HudProfileStore.ShippedEditState(n) ?? "";
                    if (Mathf.Approximately(v, 10f)) t.Check(true, "'" + n + "' on disk applies 10");
                    else if (state.StartsWith("no", StringComparison.Ordinal))
                        t.Check(false, "'" + n + "' on disk is PRISTINE yet applies " + v.ToString("0.###", Inv) + " - SyncShipped did not refresh it");
                    else t.Info("'" + n + "' on disk applies " + v.ToString("0.###", Inv) + " - edit state '" + Ascii(state) + "': a hand-tuned value survives by design");
                }
            }
            finally { HudTheme.Apply(snap); }
            yield break;
        }

        /// <summary>REFLECTION, deliberately: UiaControlCenter.cs is under an in-flight refactor by another
        /// session (Kit v2), so its privates are read by name here instead of being widened in a file
        /// someone else is rewriting. A rename degrades this test to SKIP, never a build break. The
        /// helper under test reads the private static <c>_contentArea</c>; it is pointed at a fixture for
        /// ONE synchronous call and restored in a finally (the window is closed during the run).</summary>
        private static IEnumerator F10ScrollCapture(TestCase t)
        {
            var cc = typeof(UI.Menu.UiaControlCenter);
            FieldInfo fContent = null;
            MethodInfo mCapture = null;
            try
            {
                fContent = cc.GetField("_contentArea", AnyStatic);
                mCapture = cc.GetMethod("TryCaptureScroll", AnyStatic);   // throws if it ever gets overloaded
            }
            catch { fContent = null; mCapture = null; }
            if (fContent == null || mCapture == null || fContent.FieldType != typeof(RectTransform))
            {
                t.Skip("UiaControlCenter._contentArea / TryCaptureScroll not found (the F10 refactor renamed them) - update the harness");
                yield break;
            }
            var canvasGo = NewFixtureCanvas("uiatest-f10-scroll");
            var host = (RectTransform)UI.Menu.Kit.UiaUi.Go("host", canvasGo.transform).transform;
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
            host.sizeDelta = new Vector2(320f, 200f);
            ScrollRect sr;
            var content = UI.Menu.Kit.UiaUi.ScrollView(host, out sr);
            UI.Menu.Kit.UiaUi.Size(UI.Menu.Kit.UiaUi.Go("row", content), 20f);
            yield return Frames(1);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float raw = sr.verticalNormalizedPosition;
            t.Info("fixture: " + F(content.rect.height) + " px of content in a " + F(sr.viewport.rect.height) + " px viewport; Unity's raw verticalNormalizedPosition reads " + raw.ToString("0.###", Inv));

            float y;
            bool ok = InvokeCapture(fContent, mCapture, host, out y);
            t.Check(ok && Mathf.Abs(y - 1f) < 0.001f, "content fits: the helper reports TOP (1), not Unity's raw bottom - got " + (ok ? y.ToString("0.###", Inv) : "no scroll view"));

            for (int i = 0; i < 40; i++) UI.Menu.Kit.UiaUi.Size(UI.Menu.Kit.UiaUi.Go("row" + i, content), 30f);
            yield return Frames(1);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            sr.verticalNormalizedPosition = 0.5f;
            float mid = sr.verticalNormalizedPosition;
            ok = InvokeCapture(fContent, mCapture, host, out y);
            t.Check(ok && Mathf.Abs(y - mid) < 0.01f, "content scrolls: the helper passes the real position through (" + mid.ToString("0.###", Inv) + " -> " + (ok ? y.ToString("0.###", Inv) : "-") + ")");
            DestroyFixture(canvasGo);
            t.NotProven("the LIVE F10 window's jump itself - the capture helper is driven on a fixture scroll view (the window stays closed during the run)");
        }

        private static bool InvokeCapture(FieldInfo fContent, MethodInfo m, RectTransform fixture, out float y)
        {
            y = -1f;
            object saved = fContent.GetValue(null);
            try
            {
                fContent.SetValue(null, fixture);
                var args = new object[] { 0f };
                bool r = (bool)m.Invoke(null, args);
                y = (float)args[0];
                return r;
            }
            catch (Exception e)
            {
                LogLine("   TryCaptureScroll invoke failed: " + Describe(e));
                return false;
            }
            finally { fContent.SetValue(null, saved); }
        }

        private static IEnumerator F10InputField(TestCase t)
        {
            var canvasGo = NewFixtureCanvas("uiatest-f10-input");
            var host = (RectTransform)UI.Menu.Kit.UiaUi.Go("d009-host", canvasGo.transform).transform;
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
            host.sizeDelta = new Vector2(320f, 200f);
            // THE D-009 CONDITION: a VerticalLayoutGroup that controls child height without force-expanding it.
            UI.Menu.Kit.UiaUi.VLayout(host, 4f);
            var field = UI.Menu.Kit.UiaUi.InputField(host, "uiatest", null);
            if (field == null) { t.Fail("UiaUi.InputField returned null"); DestroyFixture(canvasGo); yield break; }
            yield return Frames(1);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(host);

            float rowH = UI.Menu.Kit.UiaTheme.RowH;
            var rt = (RectTransform)field.transform;
            var le = field.GetComponent<LayoutElement>();
            float pref = LayoutUtility.GetPreferredHeight(rt);
            float min = LayoutUtility.GetMinHeight(rt);
            float flexW = LayoutUtility.GetFlexibleWidth(rt);
            t.Check(le != null, "the field carries a LayoutElement");
            t.Check(pref >= rowH - 0.5f, "preferred height " + F(pref) + " >= the kit row height " + F(rowH));
            t.Check(min >= rowH - 0.5f, "min height " + F(min) + " >= the row height - no host can squeeze it to a hairline");
            t.Check(flexW > 0f, "flexible width " + F(flexW) + " > 0 - it fills its row");
            t.Check(rt.rect.height >= rowH - 0.5f, "inside a height-controlling VerticalLayoutGroup it lays out " + F(rt.rect.height) + " px tall");
            t.Check(field.customCaretColor, "the caret colour is themed (customCaretColor)");
            DestroyFixture(canvasGo);
        }

        // ================================================================== suite: hints (B1 / B3 / B4 / B15b)

        private static IEnumerator SuiteHints()
        {
            yield return Test("hints.parked", "Sticky root with parked chips: RMB warns it drops them (D-004 / B1)", HintsParked);
            yield return Test("hints.dragging", "A drag in flight: release places or parks, RMB cancels the drag", HintsDragging);
            yield return Test("hints.picker", "Belt-picker return reads 'back' - and 'back, drop parked' with chips (B15b)", HintsPicker);
            yield return Test("hints.hold", "Hold mode: release confirms or closes; RMB backs out of a child ring", HintsHold);
            yield return Test("hints.qbelt", "The 6-key toolbelt ring shows the Q change-belt hint (IsToolbeltRing, B3)", HintsQBelt);
        }

        private static void SetHintContext(bool sticky, bool dragging, bool canGoBack, bool backDropsParked,
            int parked, bool pageable, bool hoverAction)
        {
            _hintsTouched = true;
            RadialHintContext.InteractionFrame = Time.frameCount;
            RadialHintContext.Search = false;
            RadialHintContext.Sticky = sticky;
            RadialHintContext.Dragging = dragging;
            RadialHintContext.CanGoBack = canGoBack;
            RadialHintContext.BackDropsParked = backDropsParked;
            RadialHintContext.Pageable = pageable;
            RadialHintContext.AltReach = false;
            RadialHintContext.Parked = parked;
            RadialHintContext.Verb = null;
            RadialHintContext.HoverAction = hoverAction;
            RadialHintContext.LevelDraggable = true;
            RadialHintContext.LevelScroll = false;
            RadialHintContext.LevelAction = true;
        }

        /// <summary>The strip's own composition for the CURRENT RadialHintContext, fade ignored, colour
        /// tags stripped. The preview rebuild first defeats the list cache, whose key does not include
        /// the fade flag (a veteran's faded list must not answer for fadeOn=false).</summary>
        private static string ComposeHints()
        {
            UI.RadialHintBar.RefreshHints(false, false);
            UI.RadialHintBar.RefreshHints(true, false);
            return StripTags(UI.RadialHintBar.Compose(64, new Color32(255, 255, 255, 255)));
        }

        private static void CheckHint(TestCase t, string composed, string expect, bool present, string what)
        {
            bool has = composed != null && composed.IndexOf(expect, StringComparison.Ordinal) >= 0;
            t.Check(has == present, what + (present ? " contains '" : " does NOT contain '") + expect + "'   [" + composed + "]");
        }

        private static IEnumerator HintsParked(TestCase t)
        {
            CloseRadialsQuietly();
            yield return Frames(1);
            SetHintContext(true, false, false, false, 2, false, false);
            string s = ComposeHints();
            CheckHint(t, s, "RMB  close, drop parked", true, "root + 2 parked");
            t.Check(IsAscii(s), "the strip text is plain ASCII");
            SetHintContext(true, false, false, false, 0, false, false);
            s = ComposeHints();
            CheckHint(t, s, "RMB  close", true, "root, nothing parked");
            CheckHint(t, s, "drop parked", false, "root, nothing parked");
            SyntheticHintScope(t);
        }

        /// <summary>What the four synthetic hint tests do NOT prove: the context is written by hand
        /// (<see cref="SetHintContext"/>), so the strip's COMPOSITION is proven, not that a live ring
        /// publishes that context (hints.qbelt reads a live ring's own).</summary>
        private static void SyntheticHintScope(TestCase t)
        {
            t.NotProven("that a LIVE ring publishes this RadialHintContext - it is set by hand here, so only the strip's composition for it is proven (hints.qbelt reads a live ring's own context)");
        }

        private static IEnumerator HintsDragging(TestCase t)
        {
            CloseRadialsQuietly();
            yield return Frames(1);
            SetHintContext(true, true, false, false, 0, false, false);
            string s = ComposeHints();
            CheckHint(t, s, "Release  place or park", true, "dragging");
            CheckHint(t, s, "RMB  cancel drag", true, "dragging");
            t.Check(IsAscii(s), "the strip text is plain ASCII");
            SyntheticHintScope(t);
        }

        private static IEnumerator HintsPicker(TestCase t)
        {
            CloseRadialsQuietly();
            yield return Frames(1);
            SetHintContext(true, false, true, false, 0, false, false);
            string s = ComposeHints();
            CheckHint(t, s, "RMB  back", true, "picker root (RMB returns)");
            CheckHint(t, s, "drop parked", false, "picker root, nothing parked");
            SetHintContext(true, false, true, true, 1, false, false);
            s = ComposeHints();
            CheckHint(t, s, "RMB  back, drop parked", true, "picker root + 1 parked");
            SyntheticHintScope(t);
        }

        private static IEnumerator HintsHold(TestCase t)
        {
            CloseRadialsQuietly();
            yield return Frames(1);
            SetHintContext(false, false, false, false, 0, false, true);
            string s = ComposeHints();
            CheckHint(t, s, "Release  confirm", true, "hold, hovering an action");
            SetHintContext(false, false, true, false, 0, false, false);
            s = ComposeHints();
            CheckHint(t, s, "Release  close", true, "hold, nothing actionable");
            CheckHint(t, s, "RMB  back", true, "hold with a child ring");
            SyntheticHintScope(t);
        }

        private static IEnumerator HintsQBelt(TestCase t)
        {
            var ctl = RadialController.Active;
            if (ctl == null) { t.Skip("the radial controller is not running"); yield break; }
            KeyCode pageK = KeyCode.None;
            try { pageK = UiaKeybinds.Key("UIA_Page"); } catch { }
            if (pageK == KeyCode.None) { t.Skip("the page/belt key (UIA_Page) is unbound - no Q hint exists"); yield break; }
            string q = UiaKeybinds.Glyph(pageK);
            var sixKey = new EquipmentKeyRadialFeature("ToolBeltSlot", hm => hm.ToolbeltSlot);
            t.Check(RadialController.IsToolbeltRing(sixKey) && RadialController.IsToolbeltRing(new ToolbeltRadialFeature()),
                "IsToolbeltRing holds for the 6-key AND the MMB ring");

            yield return ClearTestItems();
            if (!EnsureWornBelt(t)) yield break;
            yield return Frames(2);
            CloseRadialsQuietly();
            yield return Frames(1);
            ctl.SwitchToFeature(sixKey);
            yield return Frames(3);   // the ring publishes its real RadialHintContext from Draw
            if (!ctl._menu.IsOpen) { t.Fail("the 6-key ring did not open"); yield break; }
            _hintsTouched = true;
            string s = ComposeHints();   // the REAL published context, fade ignored
            CheckHint(t, s, q + "  change belt", true, "the live 6-key ring");
            CloseRadialsQuietly();
            yield return Frames(2);

            SetHintContext(true, false, false, false, 0, true, false);   // no toolbelt ring active any more
            s = ComposeHints();
            CheckHint(t, s, q + "  next page", true, "a pageable non-belt ring");
            CheckHint(t, s, "change belt", false, "a non-belt ring");
        }

        /// <summary>The 6-key ring needs a worn belt. Wear a TEST belt when the slot is empty (never
        /// displaces the player's own); it is despawned with every other test item.</summary>
        private static bool EnsureWornBelt(TestCase t)
        {
            var h = Guards.LocalHuman;
            if (h == null || h.ToolbeltSlot == null) { t.Skip("no toolbelt slot"); return false; }
            var worn = h.ToolbeltSlot.Get();
            if (worn != null)
            {
                t.Info("using your worn belt '" + Ascii(worn.DisplayName) + "' (never moved)");
                return true;
            }
            var belt = SpawnIntoSlot("ItemToolBelt", h.ToolbeltSlot, t);
            if (belt == null) return false;
            // A WORN belt is seeded into BeltBindings by the toolbelt ring (ToolbeltRadialFeature ->
            // BeltBindingStore.SeedIfNew), and that store has no removal API: remember its id so the
            // after-run notice can name the residue.
            try { if (!_testBeltRefs.Contains(belt.ReferenceId)) _testBeltRefs.Add(belt.ReferenceId); } catch { }
            t.Info("no belt worn - wearing a TEST tool belt for this test (despawned afterwards)");
            return true;
        }

        // ================================================================== suite: belt (D-007 / D-064)

        private static IEnumerator SuiteBelt()
        {
            yield return Test("belt.parity", "The 6-key ring builds exactly the MMB ring: The Hub first, same wedges (D-007)", BeltParity);
            yield return Test("belt.q-rmb", "Q opens the belt picker from BOTH rings; RMB returns to the SAME ring (D-064)", BeltQPickerRmb);
        }

        private static IEnumerator BeltParity(TestCase t)
        {
            yield return ClearTestItems();
            if (!EnsureWornBelt(t)) yield break;
            yield return Frames(2);
            var a = ToolbeltRadialFeature.BuildRootEntries();
            var b = new EquipmentKeyRadialFeature("ToolBeltSlot", hm => hm.ToolbeltSlot).BuildRoot();
            t.Check(a.Count == b.Count, "same wedge count (" + a.Count + " vs " + b.Count + ")");
            int n = Mathf.Min(a.Count, b.Count);
            bool same = true;
            for (int i = 0; i < n; i++)
            {
                var x = a[i];
                var y = b[i];
                if (x == null || y == null) { same = x == y && same; continue; }
                if (x.Label != y.Label || x.ActionText != y.ActionText || !ReferenceEquals(x.Tag, y.Tag)
                    || x.StowStyle != y.StowStyle || x.Enabled != y.Enabled)
                {
                    same = false;
                    t.Info("wedge " + i + " differs: '" + Ascii(x.Label) + "' vs '" + Ascii(y.Label) + "'");
                }
            }
            t.Check(same, "every wedge matches (label, action, tag, stow style, enabled)");
            t.Check(a.Count > 0 && ReferenceEquals(a[0].Tag, RadialMenu.HubTag) && a[0].Label == "The Hub", "wedge 0 is The Hub");
            t.Check(!RadialController.IsToolbeltRing(new EquipmentKeyRadialFeature("BackSlot", hm => hm.BackpackSlot)),
                "IsToolbeltRing is false for the 4-key (back) ring");
        }

        private static IEnumerator BeltQPickerRmb(TestCase t)
        {
            yield return ClearTestItems();
            if (!RequireEmptyHands(t)) yield break;
            var ctl = RadialController.Active;
            if (ctl == null) { t.Skip("the radial controller is not running"); yield break; }
            if (!EnsureWornBelt(t)) yield break;
            yield return Frames(2);
            var spare = SpawnIntoSlot("ItemToolBelt", ActiveHand(), t);
            if (spare == null) yield break;
            yield return Frames(2);

            yield return QPickerRoundTrip(t, ctl, new EquipmentKeyRadialFeature("ToolBeltSlot", hm => hm.ToolbeltSlot), "6 key", spare);
            yield return QPickerRoundTrip(t, ctl, new ToolbeltRadialFeature(), "MMB", spare);
            t.NotProven("the Q key and RMB button READS themselves (the Input calls in RadialController.UpdateOpen) - OS input; driven directly: the Q branch's gate (IsToolbeltRing) + action (OpenBeltPicker) and the RMB branch's gates (RmbReturnsFromRoot, !IsDragging) + action (SwitchToFeature(_active))");
        }

        /// <summary>The belt a belt-picker wedge would equip. <c>ToolbeltRadialFeature.BuildBeltPicker</c>
        /// puts the candidate's identity nowhere on the entry but its own action -
        /// <c>OnSelect = () =&gt; ItemActions.SwapWornToolbelt(chosen)</c> - so it is read by REFLECTION off
        /// that closure (its DynamicThing-typed captured field). Null when the entry's shape changed; the
        /// caller then reports "not proven" rather than guessing by display name (two belts share one).</summary>
        private static DynamicThing PickerEntryBelt(RadialEntry e)
        {
            try
            {
                object target = e != null && e.OnSelect != null ? e.OnSelect.Target : null;
                if (target == null) return null;
                var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!typeof(DynamicThing).IsAssignableFrom(fields[i].FieldType)) continue;
                    var v = fields[i].GetValue(target) as DynamicThing;
                    if (v != null) return v;
                }
            }
            catch { }
            return null;
        }

        private static IEnumerator QPickerRoundTrip(TestCase t, RadialController ctl, IRadialFeature origin, string label, DynamicThing spare)
        {
            CloseRadialsQuietly();
            yield return Frames(2);
            ctl.SwitchToFeature(origin);
            yield return Frames(2);
            var menu = ctl._menu;
            if (!t.Check(menu.IsOpen && menu.IsSticky, label + ": the ring opened sticky")) yield break;
            t.Check(ReferenceEquals(ctl.ActiveFeature, origin), label + ": it is the active feature");
            var hub = menu.MainEntry(0);
            t.Check(hub != null && ReferenceEquals(hub.Tag, RadialMenu.HubTag), label + ": wedge 0 is The Hub");

            // Q (RadialController.UpdateOpen): IsToolbeltRing(_active) ? OpenBeltPicker() : NextPage()
            t.Check(RadialController.IsToolbeltRing(ctl.ActiveFeature), label + ": the Q branch takes the belt-picker path (IsToolbeltRing)");
            ctl.OpenBeltPicker();
            yield return Frames(2);   // UpdateOpen re-asserts RmbReturnsFromRoot = _inBeltPicker && CanReturnTo(_active)
            t.Check(menu.IsOpen, label + ": the belt picker is up");
            t.Check(ReferenceEquals(ctl.ActiveFeature, origin), label + ": the picker keeps the origin feature");
            // Matched by ReferenceId, never by display name: a belt of your own elsewhere in your inventory
            // shares the TEST belt's name and would pass a name match on its own.
            bool listsSpare = false;
            int candidates = 0, identified = 0;
            long spareId = spare.ReferenceId;
            for (int i = 0; i < 64; i++)
            {
                var e = menu.MainEntry(i);
                if (e == null) break;
                if (e.ActionText == "Worn") continue;
                candidates++;
                var b = PickerEntryBelt(e);
                if (b == null) continue;
                identified++;
                if (b.ReferenceId == spareId) listsSpare = true;
            }
            if (identified > 0)
                t.Check(listsSpare, label + ": the picker lists the spare TEST belt in your hand (matched by ReferenceId " + spareId.ToString(Inv)
                    + " among " + candidates + " candidate wedge(s))");
            else t.NotProven(label + ": the picker listing the spare TEST belt by ReferenceId - none of its " + candidates
                + " candidate wedge(s) exposes which belt it would equip (BuildBeltPicker's OnSelect closure changed shape) - update PickerEntryBelt");
            t.Check(menu.RmbReturnsFromRoot, label + ": RMB at the picker root is a RETURN (the controller's pickerReturns)");
            t.Check(!menu.IsDragging, label + ": no drag in flight (the RMB branch's other gate)");

            // RMB (UpdateOpen): pickerReturns && !IsDragging && RMB -> SwitchToFeature(_active)
            ctl.SwitchToFeature(ctl.ActiveFeature);
            yield return Frames(2);
            t.Check(menu.IsOpen && menu.IsSticky, label + ": RMB returned to a sticky ring (not a close)");
            t.Check(ReferenceEquals(ctl.ActiveFeature, origin), label + ": feature identity preserved (the same instance)");
            var rebuilt = ToolbeltRadialFeature.BuildRootEntries();
            bool same = true;
            int n = 0;
            for (int i = 0; i < rebuilt.Count && i < 64; i++)
            {
                var e = menu.MainEntry(i);
                if (e == null) break;
                n++;
                if (rebuilt[i] == null || e.Label != rebuilt[i].Label) same = false;
            }
            var first = menu.MainEntry(0);
            t.Check(n > 0 && same && first != null && ReferenceEquals(first.Tag, RadialMenu.HubTag),
                label + ": the origin ring is rebuilt (" + n + " visible wedges match BuildRootEntries, The Hub first)");
            t.Check(!menu.RmbReturnsFromRoot, label + ": back on the ring, RMB closes again (the picker flag is cleared)");
            CloseRadialsQuietly();
            yield return Frames(2);
        }

        // ================================================================== suite: rangefinder (D-019 / B6 / B7)

        private static IEnumerator SuiteRangefinder()
        {
            yield return Test("rangefinder.defaults", "A fresh Rangefinder defaults to the Suited|Robot tiers (B6)", RfDefaults);
            yield return Test("rangefinder.units", "Unit math: 12.4 m reads 40.7 ft", RfUnits);
            yield return Test("rangefinder.mask", "The ray skips Player / PlayerImmune / Ignore Raycast and triggers; nothing inside max range reads '--' (B7)", RfMask);
            yield return Test("rangefinder.live", "The live widget shows the measured distance in m, then in ft", RfLive);
        }

        /// <summary>Adds a Rangefinder through the F9 editor's own "add element" (HudEditorMode.AddElement)
        /// to the scratch profile, and remembers its id for the live test.</summary>
        private static bool AddRangefinder(TestCase t)
        {
            if (!BeginScratchProfile(t)) return false;
            var doc = HudProfileStore.Active;
            int before = doc != null && doc.Elements != null ? doc.Elements.Count : 0;
            Windows.HudEditorMode.AddElement(HudElementType.Rangefinder);
            doc = HudProfileStore.Active;
            if (doc == null || doc.Elements == null || doc.Elements.Count != before + 1)
            {
                t.Fail("HudEditorMode.AddElement(Rangefinder) did not add an element");
                return false;
            }
            var el = doc.Elements[doc.Elements.Count - 1];
            if (el == null || el.Type != HudElementType.Rangefinder) { t.Fail("the added element is not a Rangefinder"); return false; }
            _rfElementId = el.Id;
            return true;
        }

        private static IEnumerator RfDefaults(TestCase t)
        {
            if (!AddRangefinder(t)) yield break;
            HudElementDef el = null;
            var doc = HudProfileStore.Active;
            for (int i = 0; doc != null && i < doc.Elements.Count; i++)
                if (doc.Elements[i] != null && doc.Elements[i].Id == _rfElementId) el = doc.Elements[i];
            t.Check(el != null && el.Tiers == (HudTierMask.Suited | HudTierMask.Robot),
                "the new Rangefinder's tiers are Suited|Robot (" + (el != null ? el.Tiers.ToString() : "?") + ") - never shown in the bare tier by default");
            t.Info("a bare HudElementDef defaults to " + new HudElementDef().Tiers + "; the Suited|Robot default comes from the F9 add path (HudEditorMode.AddElement)");
            yield return Frames(2);
        }

        /// <summary>rangefinder.units. Verdict honesty (safety review): the old version re-implemented the
        /// widget's quantize + format (<c>Mathf.Round(v * 10) * 0.1</c>, "0.0") and asserted its own copy,
        /// which can only ever agree with itself. The widget's own conversion constant
        /// (<c>RangefinderWidget.MetersToFeet</c>, internal) is asserted directly; its quantize + format
        /// live inline in <c>RangefinderWidget.UpdatePanel</c> with no callable helper, so they are proven on
        /// the LIVE widget: a probe at a chosen distance, the widget's own text compared with HARD-CODED
        /// expectations (computed by hand, not by the harness).</summary>
        private static IEnumerator RfUnits(TestCase t)
        {
            float k = UI.Hud.Widgets.RangefinderWidget.MetersToFeet;
            t.Check(Mathf.Abs(k - 3.28084f) < 0.00001f,
                "the widget's own constant RangefinderWidget.MetersToFeet is the international foot (" + k.ToString("0.#####", Inv) + " ft per m)");

            var live = new LiveRf();
            yield return LiveRangefinder(t, live);
            if (live.Value == null) { t.NotProven("the widget RENDERING 12.4 m as '12.4 m' / '40.7 ft' (" + (t.SkipReason ?? "no live view") + ")"); yield break; }
            float probe, baseline;
            if (!ProbeGeometry(t, out probe, out baseline)) { t.NotProven("the widget RENDERING 12.4 m as '12.4 m' / '40.7 ft' - no open space to place the probe"); yield break; }

            // 12.4 m (the spec's own example) needs 12.6 m of open space; else the same path at 1.24 m.
            // Both fixtures sit well clear of a rounding edge: 124.0 / 406.8 and 12.4 / 40.68 tenths.
            float face;
            string expM, expFt;
            if (baseline >= 12.6f) { face = 12.4f; expM = "12.4 m"; expFt = "40.7 ft"; }
            else
            {
                face = 1.24f; expM = "1.2 m"; expFt = "4.1 ft";
                t.NotProven("the spec's own example (12.4 m reads '40.7 ft'): only " + F(baseline) + " m of open space ahead, 12.6 m needed - the same widget path is asserted at 1.24 m ('1.2 m' / '4.1 ft') instead");
            }
            GameObject go = null;
            try
            {
                go = PlaceProbe(face);
                live.View.Def.Set("unit", null);
                yield return Seconds(0.35f);
                string txt = live.Value.text;
                t.Check(txt == expM, "a surface " + F(face) + " m ahead: the LIVE widget reads '" + Ascii(txt) + "' (expected '" + expM + "')");
                live.View.Def.Set("unit", "ft");
                yield return Seconds(0.35f);
                txt = live.Value.text;
                t.Check(txt == expFt, "switched to feet: the LIVE widget reads '" + Ascii(txt) + "' (expected '" + expFt + "')");
            }
            finally
            {
                try { if (live.View != null && live.View.Def != null) live.View.Def.Set("unit", null); } catch { }
                if (go != null) { _temp.Remove(go); UnityEngine.Object.Destroy(go); }
            }
        }

        /// <summary>The live Rangefinder view and its own 'Value' text (see <see cref="LiveRangefinder"/>).</summary>
        private sealed class LiveRf
        {
            public HudElementView View;
            public TextMeshProUGUI Value;
        }

        /// <summary>The LIVE Rangefinder element - added to the scratch profile through the F9 add path if
        /// this suite has not yet - with the Suited tier forced when you are bare (restored after). Fills
        /// <paramref name="into"/>, or leaves it empty with a skip recorded.</summary>
        private static IEnumerator LiveRangefinder(TestCase t, LiveRf into)
        {
            if (_rfElementId == null && !AddRangefinder(t)) yield break;
            var tier = HudSystem.LastSnapshot != null ? HudSystem.LastSnapshot.Tier : HudTier.Bare;
            if (tier == HudTier.Bare)
            {
                ForceTier(HudTier.Suited);
                t.Info("you are in the BARE tier - forcing the Suited tier for this check (restored after)");
            }
            yield return Seconds(0.4f);   // the HUD rebuilds its views for the new element
            var view = FindView(_rfElementId);
            if (view == null || view.Root == null) { t.Skip("the Rangefinder view is not built (visor HUD off?)"); yield break; }
            var vt = view.Root.Find("Value");
            var tmp = vt != null ? vt.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null) { t.Skip("the widget's 'Value' text was not found (the widget changed?)"); yield break; }
            into.View = view;
            into.Value = tmp;
        }

        /// <summary>A probe that STAYS for the widget's own 10 Hz samples: a thin BoxCollider (not a Thing,
        /// not saved, not networked) square to the crosshair ray with its NEAR face at
        /// <paramref name="nearFace"/> metres. Tracked in the temp list (RestoreAll destroys it).</summary>
        private static GameObject PlaceProbe(float nearFace)
        {
            var cam = CameraController.CurrentCamera;
            Vector3 f = cam.transform.forward;
            var go = new GameObject("uiatest-rangefinder-probe");
            _temp.Add(go);
            go.transform.SetPositionAndRotation(CameraController.CameraOrigin + f * (nearFace + 0.01f), Quaternion.LookRotation(f));
            go.layer = (int)Layers.Default;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.6f, 0.6f, 0.02f);
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>Common camera geometry precondition of the live probes: the player looks roughly
        /// level at open space (the probe must sit in front of everything, and never inside the body).</summary>
        private static bool ProbeGeometry(TestCase t, out float probeDist)
        {
            float baseline;
            return ProbeGeometry(t, out probeDist, out baseline);
        }

        /// <summary>As above, also handing back the <paramref name="baseline"/> distance to the first
        /// surface ahead (+infinity when nothing is within 200 m).</summary>
        private static bool ProbeGeometry(TestCase t, out float probeDist, out float baseline)
        {
            probeDist = 1.5f;
            baseline = float.PositiveInfinity;
            var cam = CameraController.CurrentCamera;
            if (cam == null) { t.Skip("no camera"); return false; }
            Vector3 f = cam.transform.forward;
            if (Mathf.Abs(f.y) > 0.6f) { t.Skip("look roughly LEVEL (not at the floor or the sky) and re-run the live probe checks"); return false; }
            float d0;
            bool b0 = UI.Hud.Widgets.RangefinderWidget.TryMeasure(200f, out d0);
            t.Info("baseline: " + (b0 ? F(d0) + " m to the first surface" : "no surface within 200 m"));
            if (b0) baseline = d0;
            if (b0 && d0 < 1.8f) { t.Skip("face OPEN space - a surface is " + F(d0) + " m ahead (need at least 1.8 m) - and re-run the live probe checks"); return false; }
            probeDist = b0 ? Mathf.Clamp(d0 * 0.5f, 0.8f, 1.5f) : 1.5f;
            return true;
        }

        /// <summary>A thin physics probe (plain GameObject + BoxCollider — not a Thing, not saved, not
        /// networked) square to the crosshair ray at <paramref name="dist"/>, measured through the
        /// widget's own <c>RangefinderWidget.TryMeasure</c> in the SAME frame, then destroyed
        /// immediately so the next probe never sees it.</summary>
        private static bool MeasureWithProbe(float dist, int layer, bool trigger, out float meters)
        {
            meters = 0f;
            GameObject go = null;
            try
            {
                var cam = CameraController.CurrentCamera;
                if (cam == null) return false;
                Vector3 f = cam.transform.forward;
                go = new GameObject("uiatest-rangefinder-probe");
                go.transform.SetPositionAndRotation(CameraController.CameraOrigin + f * dist, Quaternion.LookRotation(f));
                if (layer >= 0 && layer < 32) go.layer = layer;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.6f, 0.6f, 0.02f);
                bc.isTrigger = trigger;
                Physics.SyncTransforms();
                return UI.Hud.Widgets.RangefinderWidget.TryMeasure(200f, out meters);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static IEnumerator RfMask(TestCase t)
        {
            float D;
            if (!ProbeGeometry(t, out D)) yield break;
            float nearFace = D - 0.01f;
            float m;
            bool hit = MeasureWithProbe(D, (int)Layers.Default, false, out m);
            t.Check(hit && Mathf.Abs(m - nearFace) < 0.06f, "a solid probe on 'Default' at " + F(D) + " m is measured (" + (hit ? F(m) + " m" : "no hit") + ")");
            CheckExcluded(t, D, "Player", (int)Layers.Player);
            CheckExcluded(t, D, "PlayerImmune", (int)Layers.PlayerImmune);
            CheckExcluded(t, D, "Ignore Raycast", (int)Layers.IgnoreRaycast);
            hit = MeasureWithProbe(D, (int)Layers.Default, true, out m);
            t.Check(!hit || m > D + 0.1f, "a TRIGGER probe on 'Default' is ignored (QueryTriggerInteraction.Ignore) - " + (hit ? F(m) + " m" : "no hit"));

            // Max range below the nearest surface: TryMeasure reports no reading, which the widget renders "--".
            GameObject go = null;
            bool within = true;
            try
            {
                var cam = CameraController.CurrentCamera;
                Vector3 f = cam.transform.forward;
                go = new GameObject("uiatest-rangefinder-probe");
                go.transform.SetPositionAndRotation(CameraController.CameraOrigin + f * D, Quaternion.LookRotation(f));
                go.layer = (int)Layers.Default;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.6f, 0.6f, 0.02f);
                Physics.SyncTransforms();
                float dummy;
                within = UI.Hud.Widgets.RangefinderWidget.TryMeasure(D * 0.5f, out dummy);
            }
            finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
            t.Check(!within, "with max range (" + F(D * 0.5f) + " m) short of the nearest surface TryMeasure reports no reading (the widget's '--' input)");
            t.NotProven("the LIVE element actually drawing '--': it clamps an authored max range to 10..500 m, so it can only show '--' with nothing within 10 m - only TryMeasure's no-reading result is asserted");
            yield break;
        }

        private static void CheckExcluded(TestCase t, float D, string name, int layer)
        {
            if (layer < 0 || layer > 31) { t.Info("layer '" + name + "' does not exist on this build"); return; }
            float m;
            bool hit = MeasureWithProbe(D, layer, false, out m);
            t.Check(!hit || m > D + 0.1f, "a solid probe on '" + name + "' (layer " + layer + ") is NOT measured - " + (hit ? F(m) + " m (the surface beyond it)" : "no hit"));
        }

        private static IEnumerator RfLive(TestCase t)
        {
            var live = new LiveRf();
            yield return LiveRangefinder(t, live);
            if (live.Value == null) yield break;
            var view = live.View;
            var tmp = live.Value;

            float D;
            if (!ProbeGeometry(t, out D)) yield break;
            // A probe that STAYS for the widget's own 10 Hz samples (destroyed in the finally below / RestoreAll).
            GameObject go = null;
            try
            {
                float expectM = D - 0.01f;
                go = PlaceProbe(expectM);

                view.Def.Set("unit", null);
                yield return Seconds(0.35f);
                float shown;
                string txt = tmp.text;
                bool parsed = ParseReading(txt, " m", out shown);
                t.Check(parsed && Mathf.Abs(shown - expectM) <= 0.15f, "metres: the widget reads '" + Ascii(txt) + "' for a surface " + F(expectM) + " m ahead");

                view.Def.Set("unit", "ft");
                yield return Seconds(0.35f);
                txt = tmp.text;
                parsed = ParseReading(txt, " ft", out shown);
                float expectFt = expectM * 3.28084f;   // an independent expectation (tolerance check), not the widget's code path
                t.Check(parsed && Mathf.Abs(shown - expectFt) <= 0.5f, "feet: the widget reads '" + Ascii(txt) + "' (expected ~" + expectFt.ToString("0.0", Inv) + " ft)");
                view.Def.Set("unit", null);
            }
            finally
            {
                if (go != null) { _temp.Remove(go); UnityEngine.Object.Destroy(go); }
            }
        }

        private static bool ParseReading(string text, string unit, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text) || !text.EndsWith(unit, StringComparison.Ordinal)) return false;
            return float.TryParse(text.Substring(0, text.Length - unit.Length).Trim(), NumberStyles.Float, Inv, out value);
        }

        private static HudElementView FindView(string elementId)
        {
            if (elementId == null) return null;
            var views = new List<HudElementView>();
            try { HudSystem.CollectElementViews(views); } catch { return null; }
            for (int i = 0; i < views.Count; i++)
                if (views[i] != null && views[i].Def != null && views[i].Def.Id == elementId) return views[i];
            return null;
        }
    }
}
