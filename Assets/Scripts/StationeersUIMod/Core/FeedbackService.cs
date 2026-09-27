using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Assets.Scripts;                 // ConsoleWindow, GameManager
using Assets.Scripts.Networking;      // NetworkManager
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;         // UnityWebRequest (UnityEngine.UnityWebRequestModule)
// NetworkBase is in the GLOBAL namespace.

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The mod side of the in-game feedback pipeline (Documentation/0.9.8.0/Feedback-Pipeline-Plan.md
    /// §1.3-§1.6, §2.3, §5): build a bug report / suggestion, write it to a disk OUTBOX, then POST it
    /// to the token relay, which files the GitHub issue and answers <c>{number}</c> ("UIA-104").
    /// Two triggers: the <c>uiafeedback</c> console command (see <see cref="Command"/>) and the F10
    /// "Suggestions/Bugs" tab (UI/Menu/Tabs/FeedbackTab.cs), which goes through the same
    /// <see cref="SaveReport"/> + queue path via the "F10 tab API" section below.
    ///
    /// <para>INVARIANT 1 - FEEDBACK IS NEVER LOST. The report is written to
    /// <c>config/StationeersUIMod/Feedback/outbox/&lt;utc-timestamp&gt;.json</c> BEFORE the network is
    /// touched. Only a delivery - HTTP 201 that is not the relay's DRY_RUN answer, or its 429
    /// "identical report already received" - moves it to
    /// <c>Feedback/sent/&lt;timestamp&gt;-UIA-&lt;n&gt;.json</c> (the local receipt `uiafeedback status`
    /// reads). Any failure leaves it in the outbox and bumps an
    /// <c>attempts</c> counter inside it; the automatic sweep (a few seconds after mod init) only
    /// re-sends files with attempts &lt; 5, oldest first, ONE at a time - a dead relay is never
    /// hammered. `uiafeedback retry` zeroes the counters first ("retry means the broken thing is
    /// fixed", the sister app's semantic).</para>
    ///
    /// <para>MUTATES NO GAME STATE, by construction: network I/O plus the mod's own files under
    /// config/StationeersUIMod/Feedback. No OnServer, no ItemActions, no Slot/Thing, nothing the
    /// multiplayer funnel cares about - which is also why the console command is allowed to exist
    /// under the "read-only console diagnostics" rule.</para>
    ///
    /// <para>HOT-RELOAD SAFETY. Coroutines run on a hidden DontDestroyOnLoad host created lazily.
    /// <see cref="Shutdown"/> (wired into StationeersUIMod.OnDestroy's unskippable finally block)
    /// stops them, aborts any in-flight request, destroys the host and resets EVERY static below.
    /// An aborted request is lossless (its file is still in the outbox). It has to be unskippable:
    /// a surviving pump from the old assembly would race the new assembly's startup sweep and could
    /// send the same outbox file twice (= two GitHub issues).</para>
    ///
    /// <para>JSON: neither the Dev csproj nor the asmdef references a JSON library (the game ships
    /// Newtonsoft.Json, but the mod does not link it), so the payload is hand-built with
    /// <see cref="AppendJsonString"/> and the only values read back - the relay's <c>number</c>,
    /// <c>state</c>, <c>labels</c>, and our own <c>attempts</c>/<c>kind</c>/<c>title</c> - are
    /// hand-parsed.</para>
    /// </summary>
    public static class FeedbackService
    {
        // ------------------------------------------------------------------ config
        // Infrastructure, not look: bound here (not in HudConfig) so HudTheme - which reflects
        // HudConfig's fields + an explicit UIAConfig include-list - can never carry them with a
        // theme. New adds, no renames: no ConfigMigration step needed (directive #8 clause 3).
        private const string Section = "Feedback";

        /// <summary>Kill switch. Off = the command refuses to save, send, retry or query anything,
        /// and the startup sweep does not run.</summary>
        public static ConfigEntry<bool> Enabled;

        /// <summary>Override for the token relay's base URL. Empty = the built-in
        /// <see cref="DefaultRelayUrl"/>.</summary>
        public static ConfigEntry<string> RelayUrl;

        /// <summary>The production relay (the self-hosted relay behind a Cloudflare tunnel). The
        /// cfg default stays EMPTY and falls back to this, so a moved relay reaches every install
        /// through a mod update - a URL baked into the cfg would stick behind BepInEx's saved value.</summary>
        private const string DefaultRelayUrl = "https://uiascended.ssui.dev";

        /// <summary>File name of the .cfg we were bound from - it tells the SLP route
        /// (com.stationeersuimod.ui.cfg) from the dev-shim route (com.stationeersuimod.ui.scriptengine.cfg),
        /// the same distinction StationeersUIMod.OnLoaded's legacy-cfg migration keys on.</summary>
        private static string _cfgFileName;

        private const string ShimCfgName = "com.stationeersuimod.ui.scriptengine.cfg";

        /// <summary>Called from <see cref="UIAConfig.Bind"/>. Fail-soft: a problem here must never
        /// abort the rest of mod init (UIAConfig.Bind runs inside OnLoaded's one big try).</summary>
        public static void Bind(ConfigFile cfg)
        {
            try
            {
                Enabled = cfg.Bind(Section, "Enabled", true,
                    "In-game feedback (the `uiafeedback` console command). Off = nothing is saved, sent, " +
                    "retried or queried - a full kill switch for the feedback pipeline.");
                RelayUrl = cfg.Bind(Section, "RelayUrl", "",
                    "Leave EMPTY to use the built-in UI Ascended feedback relay (" + DefaultRelayUrl + "). " +
                    "Set a URL only to override it - e.g. a moved relay before a mod update ships, or a Quick " +
                    "Tunnel / localhost while testing.");
                _cfgFileName = Path.GetFileName(cfg.ConfigFilePath);
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: config bind failed (feedback unavailable): " + e.Message);
            }
        }

        // ------------------------------------------------------------------ constants
        private const int MaxAttempts = 5;
        private const int TimeoutSeconds = 8;            // a player is waiting (plan §1.5.4)
        private const float WatchdogGraceSeconds = 4f;   // our own backstop on top of the request timeout
        private const float StartupSweepDelaySeconds = 10f;
        private const int TitleCap = 120;
        private const int DescriptionCap = 4000;
        private const int ProfileCap = 100 * 1024;       // chars; the relay re-caps server-side
        private const int LogCap = 30 * 1024;            // chars
        private const int LogLineBudget = 100;
        private const long LogTailBytes = 4L * 1024 * 1024; // only the newest 4 MB of a log is scanned
        private const int LogMaxLineChars = 2000;
        private const int StatusMaxReceipts = 10;
        private const string ClientHeaderValue = "uia-mod"; // X-UIA-Client: obscurity, NOT security (plan §2.4)

        // ------------------------------------------------------------------ runtime state
        // EVERY one of these is reset in Shutdown (hot-reload rule).
        private static GameObject _hostGo;
        private static FeedbackHost _host;
        private static UnityWebRequest _inFlight;
        private static readonly List<Job> _queue = new List<Job>();
        private static readonly HashSet<string> _queuedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _pumping;
        private static bool _statusQueued;

        // F10 "Suggestions/Bugs" tab state (2026-09-26). The tab POLLS these (ChangeStamp + the
        // per-path results) instead of receiving callbacks, so a result lands correctly whoever
        // queued the send (the tab, the startup sweep, `uiafeedback retry`) and nothing here ever
        // calls into a page that may already be destroyed. All reset in Shutdown.
        private static readonly Dictionary<string, SendResult> _results =
            new Dictionary<string, SendResult>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _statusCache = new Dictionary<int, string>();
        private static readonly HashSet<int> _statusChecking = new HashSet<int>();
        private static bool _uiStatusQueued;
        private static int _changeStamp;

        // Immutable parse helpers (no state to reset).
        private static readonly Regex AttemptsPrefix =
            new Regex("^\\s*\\{\\s*\"attempts\"\\s*:\\s*(-?\\d+)\\s*,", RegexOptions.CultureInvariant);
        private static readonly Regex NumberField =
            new Regex("\"number\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex DryRunTrue =
            new Regex("\"dryRun\"\\s*:\\s*true", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex DuplicateTrue =
            new Regex("\"duplicate\"\\s*:\\s*true", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex StateField =
            new Regex("\"state\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex JsonStringLiteral =
            new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex JsonNameField =
            new Regex("\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex ReceiptName =
            new Regex("-UIA-(\\d+|unknown)\\.json$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        // A managed stack frame with no leading whitespace - Unity's "Namespace.Type:Method (args)"
        // and the BepInEx/Unity exception form "Namespace.Type.Method () (at <...>:0)". An exception
        // HEADER ("System.NullReferenceException: Object ...") does not match: no '(' right after
        // the member name.
        private static readonly Regex UnityFrame =
            new Regex("^[A-Za-z_][\\w.`+<>\\[\\],]*[.:][\\w`+<>|\\[\\],]+ ?\\(", RegexOptions.CultureInvariant);

        private sealed class Job
        {
            public bool IsStatus;
            public string Path;           // send: the outbox file
            public bool Announce;         // send: player-initiated -> print the per-report result lines
            public bool FromUi;           // send: queued by the F10 tab (only changes the log wording)
            public SendResult Result;     // send: how it ended (recorded into _results by JobDone)
            public bool Quiet;            // status: the F10 tab's check - fills the cache, prints nothing
            public List<Receipt> Receipts; // status: what to query, in print order
        }

        /// <summary>How one send attempt ended - what the F10 tab turns into player words.</summary>
        internal enum SendOutcome
        {
            /// <summary>HTTP 201 (not DRY_RUN). <see cref="SendResult.Number"/> is the issue number, 0 = none returned.</summary>
            Delivered,
            /// <summary>HTTP 429 {duplicate:true}: an identical report already landed this hour. Counted as delivered.</summary>
            Duplicate,
            /// <summary>HTTP 201 {dryRun:true}: the relay built the issue but filed nothing. Still in the outbox.</summary>
            DryRun,
            /// <summary>Anything else (no response, HTTP error, file problem). Still in the outbox (if it existed).</summary>
            Failed,
            /// <summary>Never attempted: feedback turned off, or the relay override is not a URL. Still in the outbox.</summary>
            NotSent,
        }

        /// <summary>The recorded end of one send job, keyed by its outbox path in <see cref="_results"/>.
        /// <see cref="Reason"/> is short diagnostic text (HTTP code / relay error / file problem) and may
        /// contain non-ASCII from the relay - the displaying side must filter it.</summary>
        internal sealed class SendResult
        {
            public SendOutcome Outcome;
            public int Number;
            public string Reason = "";
            /// <summary>Failed/DryRun: the attempts counter now in the file (-1 = unknown).</summary>
            public int Attempts = -1;
            /// <summary>Failed/DryRun: the automatic retries are used up (only an explicit retry sends it now).</summary>
            public bool RetriesUsedUp;
        }

        /// <summary>One row of the F10 tab's "Your reports" list: an outbox file (waiting) or a sent
        /// receipt. Kind/Title come from our own payload; Number is parsed from the receipt name.</summary>
        internal sealed class ReportInfo
        {
            public string Path;
            public bool Waiting;          // still in outbox/
            public bool Sending;          // waiting AND queued/in flight right now
            public int Attempts;
            public bool RetriesUsedUp;
            public int Number;            // sent: the issue number; 0 = the relay gave none
            public string Kind = "";      // "bug" / "suggestion" (raw)
            public string Title = "";
            public bool HasWhen;
            public DateTime WhenUtc;
        }

        private sealed class Receipt
        {
            public int Number;            // 0 = the relay gave us no number
            public string Kind = "";
            public string Title = "";
        }

        /// <summary>The hidden coroutine host. Pure carrier - no Update, no state.</summary>
        private sealed class FeedbackHost : MonoBehaviour { }

        // ------------------------------------------------------------------ folders
        private static string FeedbackDir
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "StationeersUIMod", "Feedback"); }
        }
        private static string OutboxDir { get { return Path.Combine(FeedbackDir, "outbox"); } }
        private static string SentDir { get { return Path.Combine(FeedbackDir, "sent"); } }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Mod init hook (StationeersUIMod.OnLoaded, after patching). If anything is waiting
        /// in the outbox and a relay is configured, schedules ONE background sweep a few seconds
        /// later. Fail-soft: never throws, never blocks init.</summary>
        public static void Init()
        {
            try
            {
                if (!IsEnabled() || RelayBase() == null) return;
                if (!OutboxHasSendable()) return;
                if (!EnsureHost()) return;
                _host.StartCoroutine(DelayedSweep());
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: startup retry sweep not scheduled: " + e.Message);
            }
        }

        /// <summary>Hot-reload / quit teardown. Stops every coroutine, aborts the in-flight request
        /// (lossless - its file is still in the outbox), destroys the host and resets every static.
        /// Idempotent; never throws.</summary>
        public static void Shutdown()
        {
            try { if (_host != null) _host.StopAllCoroutines(); }
            catch (Exception e) { UIALog.Warn("FeedbackService: stopping coroutines failed: " + e.Message); }
            try
            {
                if (_inFlight != null)
                {
                    _inFlight.Abort();
                    _inFlight.Dispose();
                }
            }
            catch (Exception e) { UIALog.Warn("FeedbackService: aborting the in-flight request failed: " + e.Message); }
            _inFlight = null;
            try { if (_hostGo != null) UnityEngine.Object.Destroy(_hostGo); }
            catch (Exception e) { UIALog.Warn("FeedbackService: destroying the host failed: " + e.Message); }
            _hostGo = null;
            _host = null;
            _queue.Clear();
            _queuedPaths.Clear();
            _pumping = false;
            _statusQueued = false;
            _results.Clear();
            _statusCache.Clear();
            _statusChecking.Clear();
            _uiStatusQueued = false;
            _changeStamp = 0;
            Enabled = null;
            RelayUrl = null;
            _cfgFileName = null;
        }

        private static bool EnsureHost()
        {
            if (_host != null) return true;   // Unity fake-null also catches a destroyed host
            if (_pumping) RecoverDeadPump();  // the pump died WITH its host - see RecoverDeadPump
            try
            {
                var go = new GameObject("UIAscended_FeedbackHost");
                go.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<FeedbackHost>();
                _hostGo = go;
                return _host != null;
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: could not create the coroutine host: " + e.Message);
                _host = null;
                return false;
            }
        }

        /// <summary>The F10 tab's stuck-send check: if the pump's host GameObject was destroyed by
        /// something other than <see cref="Shutdown"/>, recover now (see <see cref="RecoverDeadPump"/>).
        /// A no-op while the pump is healthy or idle.</summary>
        internal static void RecoverIfPumpDead()
        {
            try
            {
                if (!_pumping || _host != null) return;
                RecoverDeadPump();
                EnsurePump();   // anything still queued gets a fresh pump on a fresh host
            }
            catch (Exception e) { UIALog.Warn("FeedbackService: pump recovery failed: " + e.Message); }
        }

        /// <summary>A coroutine stopped by its host's destruction never runs its <c>finally</c>, so
        /// <c>_pumping</c> would stay true (every later EnsurePump a no-op) and the in-flight path
        /// would stay "queued" forever. Only Shutdown destroys the host on purpose (and resets all of
        /// this itself); this is the backstop for anything else. The job that was in flight is
        /// recorded as Failed (its file is still in the outbox - lossless), status flags whose job is
        /// gone are cleared, and the queued jobs are left for the fresh pump.</summary>
        private static void RecoverDeadPump()
        {
            _pumping = false;
            try
            {
                if (_inFlight != null) { _inFlight.Abort(); _inFlight.Dispose(); }
            }
            catch { }
            _inFlight = null;
            try { if (_hostGo != null) UnityEngine.Object.Destroy(_hostGo); } catch { }   // component gone, GO left?
            _hostGo = null;
            _host = null;

            bool quietStatusQueued = false, consoleStatusQueued = false;
            var stillQueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _queue.Count; i++)
            {
                var j = _queue[i];
                if (j == null) continue;
                if (j.IsStatus) { if (j.Quiet) quietStatusQueued = true; else consoleStatusQueued = true; }
                else if (j.Path != null) stillQueued.Add(j.Path);
            }
            var orphans = new List<string>();
            foreach (var p in _queuedPaths) if (!stillQueued.Contains(p)) orphans.Add(p);
            for (int i = 0; i < orphans.Count; i++)
            {
                _queuedPaths.Remove(orphans[i]);
                _results[orphans[i]] = new SendResult { Outcome = SendOutcome.Failed, Reason = "the send was interrupted" };
            }
            if (!quietStatusQueued) { _uiStatusQueued = false; _statusChecking.Clear(); }
            if (!consoleStatusQueued) _statusQueued = false;
            Bump();
            UIALog.Warn("FeedbackService: the send pump's host was destroyed mid-job - recovered ("
                + orphans.Count + " interrupted send(s) left in the outbox).");
        }

        // ------------------------------------------------------------------ settings helpers

        private static bool IsEnabled()
        {
            try { return Enabled != null && Enabled.Value; } catch { return false; }
        }

        /// <summary>The relay base URL (trimmed, no trailing slash): the override when one is set, else
        /// the built-in default. Null only when an override is set but is not an http(s) URL.</summary>
        private static string RelayBase()
        {
            string raw;
            try { raw = RelayUrl != null ? RelayUrl.Value : null; } catch { raw = null; }
            raw = (raw ?? "").Trim().TrimEnd('/');
            if (raw.Length == 0) raw = DefaultRelayUrl;
            if (!raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return null;
            return raw;
        }

        private static string RawRelaySetting()
        {
            try { return RelayUrl != null ? (RelayUrl.Value ?? "") : ""; } catch { return ""; }
        }

        // ------------------------------------------------------------------ public-ish API (F10 tab later)

        /// <summary>Build a report and write it to the outbox. Returns the outbox path, or null with
        /// <paramref name="error"/> set (then NOTHING was saved or sent). Every auto-context field is
        /// individually fail-soft. <paramref name="notes"/> receives player-facing ASCII notes
        /// (attachment degraded, etc.). Does not touch the network - pair with <see cref="QueueSend"/>.</summary>
        internal static string SaveReport(string kind, string title, string description, string contact,
            bool attachProfile, bool attachLog, List<string> notes, out string error)
        {
            error = null;
            try
            {
                var ctx = CaptureContext();

                string profileXml = null;
                if (attachProfile)
                {
                    string note;
                    profileXml = ReadProfileXml(ContextValue(ctx, "profileName"), out note);
                    if (note != null && notes != null) notes.Add(note);
                }
                string logExcerpt = null;
                if (attachLog)
                {
                    string note;
                    logExcerpt = ReadLogExcerpt(out note);
                    if (note != null && notes != null) notes.Add(note);
                }

                string payload = BuildPayload(kind, title, description, contact, ctx, profileXml, logExcerpt);
                string path = WriteNewOutboxFile(payload, out error);
                return path;
            }
            catch (Exception e)
            {
                error = e.Message;
                UIALog.Warn("FeedbackService: saving a report failed: " + e);
                return null;
            }
        }

        /// <summary>Queue one outbox file for sending. <paramref name="front"/> puts a player-initiated
        /// report ahead of any background sweep (it still never runs in PARALLEL with the request
        /// already in flight). Returns false if not queued (disabled / invalid relay override / already
        /// queued / no host).</summary>
        internal static bool QueueSend(string path, bool announce, bool front)
        {
            return QueueSend(path, announce, front, false);
        }

        private static bool QueueSend(string path, bool announce, bool front, bool fromUi)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!IsEnabled() || RelayBase() == null) return false;
            if (_queuedPaths.Contains(path)) return false;
            if (!EnsureHost()) return false;
            var job = new Job { IsStatus = false, Path = path, Announce = announce, FromUi = fromUi };
            if (front) _queue.Insert(0, job); else _queue.Add(job);
            _queuedPaths.Add(path);
            Bump();
            EnsurePump();
            return true;
        }

        /// <summary>Unsent reports currently in the outbox (any attempts count). Never throws.</summary>
        public static int OutboxCount()
        {
            try { return ListJson(OutboxDir).Count; } catch { return 0; }
        }

        // ------------------------------------------------------------------ F10 "Suggestions/Bugs" tab API
        // Everything UI/Menu/Tabs/FeedbackTab.cs needs, and nothing that blocks: saving is the same
        // small local file write the console command does, every network call rides the one-at-a-
        // time pump, and completion is POLLED (ChangeStamp + TryGetResult) rather than called back -
        // see the _results note. The console command's behaviour and output are unchanged.

        internal const int ContactCap = 60;

        /// <summary>Bumped whenever an outbox/sent file, a queued send, a recorded result or a cached
        /// status changes. The tab compares it once per frame and re-reads only when it moved.</summary>
        internal static int ChangeStamp { get { return _changeStamp; } }

        /// <summary>False until UIAConfig.Bind ran (the tab then says so instead of guessing).</summary>
        internal static bool SettingsLoaded { get { return Enabled != null; } }

        /// <summary>Feedback &gt; Enabled.</summary>
        internal static bool FeedbackEnabled { get { return IsEnabled(); } }

        /// <summary>A usable relay (the built-in one, or an override that is an http(s) URL).</summary>
        internal static bool RelayUsable { get { return RelayBase() != null; } }

        /// <summary>The relay's host name for the "what gets sent" disclosure ("" when unusable).</summary>
        internal static string RelayHostForDisplay()
        {
            string b = RelayBase();
            if (b == null) return "";
            try { return new Uri(b).Host; } catch { return b; }
        }

        /// <summary>The auto-context exactly as the next report would carry it (wire keys, same
        /// fail-soft capture). The tab renders it as its always-visible disclosure block.</summary>
        internal static List<KeyValuePair<string, string>> PreviewContext()
        {
            try { return CaptureContext(); }
            catch { return new List<KeyValuePair<string, string>>(); }
        }

        /// <summary>Save a report from the tab - the console's exact outbox write (invariant 1: on
        /// disk BEFORE any network) - then queue it at the FRONT of the pump. Returns the outbox path,
        /// or null with <paramref name="error"/> set when nothing new was saved. <paramref name="queued"/>
        /// false = saved but not queued (relay override invalid / no host): it waits in the outbox.
        /// Client-side caps mirror the relay's (title 120, description 4000, contact 60).
        ///
        /// <para><paramref name="replacesPath"/> = the draft's earlier, still-unsent copy that this
        /// EDITED version supersedes. Replace-or-nothing: the new file is written first, then the old
        /// one removed; if the old one cannot be removed (IO error, or it is being sent right now) the
        /// new file is withdrawn again, null is returned and <paramref name="replaceBlocked"/> is set -
        /// never two versions of one report in the outbox (= two issues).</para></summary>
        internal static string SubmitFromUi(string kind, string title, string description, string contact,
            bool attachProfile, bool attachLog, string replacesPath, List<string> notes,
            out bool queued, out bool replaceBlocked, out string error)
        {
            queued = false;
            replaceBlocked = false;
            error = null;
            try
            {
                if (!IsEnabled()) { error = "feedback is turned off (Feedback > Enabled)"; return null; }
                string c = Cap(contact, ContactCap);
                string path = SaveReport(kind, Cap(title, TitleCap), Cap(description, DescriptionCap),
                    c.Length > 0 ? c : null, attachProfile, attachLog, notes, out error);
                if (path == null) return null;
                if (!string.IsNullOrEmpty(replacesPath)
                    && !string.Equals(replacesPath, path, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(replacesPath)
                    && !DiscardUnsent(replacesPath) && File.Exists(replacesPath))
                {
                    replaceBlocked = true;
                    error = _queuedPaths.Contains(replacesPath)
                        ? "the earlier copy is being sent right now"
                        : "the earlier copy could not be removed";
                    try { File.Delete(path); }
                    catch (Exception e)
                    {
                        UIALog.Warn("FeedbackService: could not withdraw " + SafeFileName(path)
                            + " after its replace was blocked - both copies may be sent: " + e.Message);
                    }
                    Bump();
                    return null;
                }
                queued = QueueSend(path, false, true, true);
                return path;
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: tab submit failed: " + e);
                if (error == null) error = e.Message;
                return null;
            }
        }

        /// <summary>Is this outbox file queued or in flight right now?</summary>
        internal static bool IsQueued(string path)
        {
            return !string.IsNullOrEmpty(path) && _queuedPaths.Contains(path);
        }

        /// <summary>How the most recent send of this outbox file ended, if one finished this session
        /// (whoever queued it - the tab, a sweep, or `uiafeedback retry`).</summary>
        internal static bool TryGetResult(string path, out SendResult result)
        {
            result = null;
            return !string.IsNullOrEmpty(path) && _results.TryGetValue(path, out result) && result != null;
        }

        /// <summary>The tab's "Try again" for its own unsent draft: zero that one report's attempts
        /// counter ("retry means the broken thing is fixed") and queue it at the front. True = on its
        /// way (or already was).</summary>
        internal static bool ResendNow(string path)
        {
            try
            {
                if (!IsEnabled() || RelayBase() == null) return false;
                if (!IsOutboxFile(path)) return false;
                if (_queuedPaths.Contains(path)) return true;
                int attempts;
                string payload;
                if (!ReadOutbox(path, out attempts, out payload)) return false;
                if (attempts != 0 && !WriteOutboxFile(path, 0, payload)) return false;
                _results.Remove(path);   // the next recorded result answers THIS attempt
                return QueueSend(path, false, true, true);
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: resend failed to start: " + e.Message);
                return false;
            }
        }

        /// <summary>Delete ONE unsent report - only ever the tab's own earlier copy of a draft the
        /// player then edited and sent again. The tab writes the new version to the outbox FIRST, so
        /// nothing is lost (invariant 1). Refuses anything outside outbox/, and anything queued or in
        /// flight (it may be landing right now).</summary>
        internal static bool DiscardUnsent(string path)
        {
            try
            {
                if (!IsOutboxFile(path) || _queuedPaths.Contains(path)) return false;
                File.Delete(path);
                _results.Remove(path);
                Bump();
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: could not remove the superseded draft " + SafeFileName(path) + ": " + e.Message);
                return false;
            }
        }

        /// <summary>The tab's quiet outbox sweep (tab open / Refresh): every report with automatic
        /// tries left, oldest first, one at a time - the startup sweep's rules, no console output.
        /// Skips <paramref name="exceptPath"/> (the open draft). Returns how many were queued.</summary>
        internal static int SweepNow(string exceptPath)
        {
            try { return Sweep(false, exceptPath); }
            catch (Exception e) { UIALog.Warn("FeedbackService: tab sweep failed: " + e.Message); return 0; }
        }

        /// <summary>The tab's "Send waiting reports now": `uiafeedback retry` without the console
        /// output - every counter back to zero, then sweep. Skips the open draft. Returns how many
        /// were queued.</summary>
        internal static int RetryAllNow(string exceptPath)
        {
            try
            {
                if (!IsEnabled() || RelayBase() == null) return 0;
                // A file this session already saw DELIVERED but could not move to sent/ is parked at
                // the attempts cap by MarkSent; resetting it would file the report a second time.
                // (Only knowable within the session - `uiafeedback retry` has the same cross-session
                // corner, unchanged.)
                var files = ListJson(OutboxDir);
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    SendResult r;
                    if (_results.TryGetValue(files[i], out r) && r != null
                        && (r.Outcome == SendOutcome.Delivered || r.Outcome == SendOutcome.Duplicate))
                        files.RemoveAt(i);
                }
                ResetAttempts(files, exceptPath);
                int queued = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    if (exceptPath != null && string.Equals(files[i], exceptPath, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        int attempts;
                        string payload;
                        if (!ReadOutbox(files[i], out attempts, out payload) || attempts >= MaxAttempts) continue;
                        if (QueueSend(files[i], false, false)) queued++;
                    }
                    catch (Exception e) { UIALog.Warn("FeedbackService: tab retry skipped " + SafeFileName(files[i]) + ": " + e.Message); }
                }
                return queued;
            }
            catch (Exception e) { UIALog.Warn("FeedbackService: tab retry failed: " + e.Message); return 0; }
        }

        /// <summary>True when this outbox file has since been delivered (a sent/ receipt exists for
        /// its timestamp stem); <paramref name="number"/> = its issue number, 0 = none given.</summary>
        internal static bool FindReceipt(string outboxPath, out int number)
        {
            number = 0;
            try
            {
                if (string.IsNullOrEmpty(outboxPath) || !Directory.Exists(SentDir)) return false;
                string stem = Path.GetFileNameWithoutExtension(outboxPath);
                if (string.IsNullOrEmpty(stem)) return false;
                // MarkSent names: <stem>-UIA-<n|unknown>.json, or <stem>-<18-digit ticks>-UIA-... on a
                // name clash. The digit floor keeps stem "...Z" from matching stem "...Z-2"'s receipt.
                var rx = new Regex("^" + Regex.Escape(stem) + "(?:-\\d{12,})?-UIA-(\\d+|unknown)\\.json$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                foreach (var f in Directory.GetFiles(SentDir, stem + "-*.json"))
                {
                    var m = rx.Match(Path.GetFileName(f) ?? "");
                    if (!m.Success) continue;
                    int n;
                    if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) number = n;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>The "Your reports" list: every outbox file (waiting, newest first) then the newest
        /// <paramref name="maxSent"/> sent receipts (newest first). <paramref name="totalSent"/> = all
        /// receipts on disk. Local disk only - never touches the network. Never throws.</summary>
        internal static List<ReportInfo> ListReports(int maxSent, out int totalSent)
        {
            var list = new List<ReportInfo>();
            totalSent = 0;
            try
            {
                var outbox = ListJson(OutboxDir);
                for (int i = outbox.Count - 1; i >= 0; i--)
                {
                    var info = ReadReportInfo(outbox[i], true);
                    if (info != null) list.Add(info);
                }
                var sent = ListJson(SentDir);
                totalSent = sent.Count;
                int taken = 0;
                for (int i = sent.Count - 1; i >= 0 && taken < maxSent; i--)
                {
                    var info = ReadReportInfo(sent[i], false);
                    if (info == null) continue;
                    list.Add(info);
                    taken++;
                }
            }
            catch (Exception e) { UIALog.Warn("FeedbackService: listing reports failed: " + e.Message); }
            return list;
        }

        private static ReportInfo ReadReportInfo(string path, bool waiting)
        {
            try
            {
                var info = new ReportInfo();
                info.Path = path;
                info.Waiting = waiting;
                string name = Path.GetFileName(path) ?? "";
                if (!waiting)
                {
                    var m = ReceiptName.Match(name);
                    int n;
                    if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                        info.Number = n;
                }
                DateTime when;
                if (name.Length >= 19 && DateTime.TryParseExact(name.Substring(0, 19), "yyyyMMdd'T'HHmmssfff'Z'",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out when))
                {
                    info.HasWhen = true;
                    info.WhenUtc = when;
                }
                // HEAD ONLY: attempts, kind and title are the first members we write (title <= 120
                // chars), so 4 KB always holds them - the tab re-lists on every ChangeStamp, and a
                // report carrying a 100 KB profile must not be read whole for its title.
                string head = ReadHead(path, 4096);
                if (head.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    info.Kind = ReadJsonString(head, "kind");
                    info.Title = ReadJsonString(head, "title");
                    if (waiting)
                    {
                        int attempts = 0;
                        var m = AttemptsPrefix.Match(head);
                        if (m.Success) int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out attempts);
                        info.Attempts = attempts;
                        info.RetriesUsedUp = attempts >= MaxAttempts;
                    }
                }
                else if (waiting) return null;   // not a report (a stray hand-dropped file): not listed
                info.Sending = waiting && _queuedPaths.Contains(path);
                return info;
            }
            catch { return null; }
        }

        private static string ReadHead(string path, int maxChars)
        {
            var buf = new char[maxChars];
            int n;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                n = sr.ReadBlock(buf, 0, buf.Length);
            return new string(buf, 0, n);
        }

        /// <summary>Queue the tab's status check for these issue numbers (the newest 10 at most) -
        /// Quiet: nothing is printed, the answers land in the cache <see cref="CachedStatus"/> reads.
        /// False when one is already running, or feedback/relay is unusable.</summary>
        internal static bool RequestStatusQuiet(IList<int> numbers)
        {
            try
            {
                if (!IsEnabled() || RelayBase() == null || numbers == null) return false;
                if (_uiStatusQueued) return false;
                var receipts = new List<Receipt>();
                var seen = new HashSet<int>();
                for (int i = 0; i < numbers.Count && receipts.Count < StatusMaxReceipts; i++)
                {
                    int n = numbers[i];
                    if (n <= 0 || !seen.Add(n)) continue;
                    var r = new Receipt();
                    r.Number = n;
                    receipts.Add(r);
                }
                if (receipts.Count == 0) return false;
                if (!EnsureHost()) return false;
                for (int i = 0; i < receipts.Count; i++) _statusChecking.Add(receipts[i].Number);
                _uiStatusQueued = true;
                _queue.Add(new Job { IsStatus = true, Quiet = true, Receipts = receipts });
                Bump();
                EnsurePump();
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: tab status check failed to start: " + e.Message);
                return false;
            }
        }

        /// <summary>True while the tab's status check is queued or running.</summary>
        internal static bool StatusCheckRunning { get { return _uiStatusQueued; } }

        /// <summary>The last status answer for this issue number this session ("Received", "Under
        /// review", "Fixed in X", "Closed", "Open", or "--" = unknown), null = never asked.
        /// <paramref name="checking"/> = a check for it is queued or in flight.</summary>
        internal static string CachedStatus(int number, out bool checking)
        {
            checking = number > 0 && _statusChecking.Contains(number);
            string s;
            return number > 0 && _statusCache.TryGetValue(number, out s) ? s : null;
        }

        private static bool IsOutboxFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
                if (!File.Exists(path)) return false;
                string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
                string outbox = Path.GetFullPath(OutboxDir);
                return string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    outbox.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string Cap(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max).TrimEnd();
        }

        // ------------------------------------------------------------------ the pump (one request at a time)

        private static void EnsurePump()
        {
            if (_pumping || _queue.Count == 0) return;
            if (!EnsureHost()) return;
            _pumping = true;
            try { _host.StartCoroutine(Pump()); }
            catch (Exception e)
            {
                _pumping = false;
                UIALog.Warn("FeedbackService: could not start the send pump: " + e.Message);
            }
        }

        private static Job Dequeue()
        {
            if (_queue.Count == 0) return null;
            var j = _queue[0];
            _queue.RemoveAt(0);
            return j;
        }

        /// <summary>THE coroutine. Every step that can throw lives in a try/caught helper; the
        /// iterator itself only sequences and yields (a yield may not sit inside try/catch, and an
        /// exception escaping MoveNext would silently kill the pump). The watchdog loop replaces a
        /// bare <c>yield return op</c> so a request that somehow never completes cannot wedge the
        /// queue forever.</summary>
        private static IEnumerator Pump()
        {
            Job job = null;
            try
            {
                while (true)
                {
                    job = Dequeue();
                    if (job == null) yield break;

                    if (job.IsStatus)
                    {
                        var receipts = job.Receipts ?? new List<Receipt>();
                        bool anyDash = false;
                        // The F10 tab's (Quiet) check stops asking once the relay gave NO response
                        // at all: the rest would only time out one by one (up to ~12 s each) while a
                        // player's own Send waits behind them. They read "--" instead - unknown,
                        // never guessed. The console check keeps its one-request-per-receipt output.
                        bool unreachable = false;
                        for (int i = 0; i < receipts.Count; i++)
                        {
                            UnityWebRequest req = unreachable ? null : BeginStatus(receipts[i]);
                            if (req != null)
                            {
                                float deadline = Time.realtimeSinceStartup + TimeoutSeconds + WatchdogGraceSeconds;
                                while (!IsDoneSafe(req) && Time.realtimeSinceStartup < deadline) yield return null;
                            }
                            bool noResponse;
                            if (!FinishStatus(receipts[i], req, job.Quiet, out noResponse)) anyDash = true;
                            if (noResponse && job.Quiet) unreachable = true;
                        }
                        EndStatusJob(anyDash, job.Quiet);
                    }
                    else
                    {
                        UnityWebRequest req = BeginSend(job);
                        if (req != null)
                        {
                            float deadline = Time.realtimeSinceStartup + TimeoutSeconds + WatchdogGraceSeconds;
                            while (!IsDoneSafe(req) && Time.realtimeSinceStartup < deadline) yield return null;
                            FinishSend(job, req);
                        }
                    }
                    JobDone(job);
                    job = null;
                }
            }
            finally
            {
                PumpEnded(job);
            }
        }

        private static IEnumerator DelayedSweep()
        {
            yield return new WaitForSecondsRealtime(StartupSweepDelaySeconds);
            SweepSafe(false);
        }

        private static void SweepSafe(bool announce)
        {
            try { Sweep(announce); }
            catch (Exception e) { UIALog.Warn("FeedbackService: outbox sweep failed: " + e.Message); }
        }

        /// <summary>Queue every outbox file with attempts &lt; 5, OLDEST first (names are UTC
        /// timestamps, so ordinal order is chronological). Returns how many were queued.</summary>
        private static int Sweep(bool announce)
        {
            return Sweep(announce, null);
        }

        /// <summary>As <see cref="Sweep(bool)"/>, skipping <paramref name="exceptPath"/> - the F10
        /// tab's open draft, whose own Send / Try again is the one path allowed to send it (a sweep
        /// carrying the OLD text while the player edits it would file the wrong version).</summary>
        private static int Sweep(bool announce, string exceptPath)
        {
            if (!IsEnabled() || RelayBase() == null) return 0;
            int queued = 0;
            foreach (var path in ListJson(OutboxDir))
            {
                try
                {
                    if (exceptPath != null && string.Equals(path, exceptPath, StringComparison.OrdinalIgnoreCase)) continue;
                    int attempts;
                    string payload;
                    if (!ReadOutbox(path, out attempts, out payload)) continue;
                    if (attempts >= MaxAttempts) continue;
                    if (QueueSend(path, announce, false)) queued++;
                }
                catch (Exception e)
                {
                    UIALog.Warn("FeedbackService: skipped unreadable outbox file " + SafeFileName(path) + ": " + e.Message);
                }
            }
            if (queued > 0 && !announce)
                UIALog.Warn("FeedbackService: " + queued + " unsent feedback report(s) found in the outbox; retrying one at a time.");
            return queued;
        }

        private static bool IsDoneSafe(UnityWebRequest req)
        {
            try { return req == null || req.isDone; } catch { return true; }
        }

        private static void JobDone(Job job)
        {
            try
            {
                if (job == null) return;
                if (job.IsStatus)
                {
                    if (job.Quiet) _uiStatusQueued = false;
                    else _statusQueued = false;
                    // Anything this job never reached (abnormal end) stops reading "checking".
                    if (job.Receipts != null)
                        for (int i = 0; i < job.Receipts.Count; i++)
                            if (job.Receipts[i] != null) _statusChecking.Remove(job.Receipts[i].Number);
                }
                else if (job.Path != null)
                {
                    _queuedPaths.Remove(job.Path);
                    // Every send job records how it ended - whoever queued it - so the F10 tab
                    // can tell its own report's fate even when a sweep or `retry` carried it.
                    var r = job.Result;
                    if (r == null)
                    {
                        r = new SendResult();
                        r.Outcome = SendOutcome.Failed;
                        r.Reason = "the send ended without an answer";
                    }
                    if (_results.Count >= 64) _results.Clear();   // bounded; only the newest matter
                    _results[job.Path] = r;
                }
                Bump();
            }
            catch { }
        }

        private static void Bump()
        {
            unchecked { _changeStamp++; }
        }

        private static void PumpEnded(Job unfinished)
        {
            try
            {
                _pumping = false;
                if (unfinished != null) JobDone(unfinished);
                // Something got queued while we were winding down (or we ended abnormally with work
                // left): start a fresh pump rather than strand it.
                if (_queue.Count > 0 && _host != null) EnsurePump();
            }
            catch { }
        }

        // ------------------------------------------------------------------ send

        /// <summary>Read the outbox file and start the POST. Returns null when there is nothing to
        /// wait for (file gone, disabled, over the attempts cap, or the request could not even be
        /// created - that last case is recorded as a failed attempt).</summary>
        private static UnityWebRequest BeginSend(Job job)
        {
            try
            {
                int attempts;
                string payload;
                if (!File.Exists(job.Path))
                {
                    UIALog.Warn("FeedbackService: queued report vanished before sending: " + Path.GetFileName(job.Path));
                    job.Result = new SendResult { Outcome = SendOutcome.Failed, Reason = "the saved report file disappeared before sending" };
                    return null;
                }
                if (!ReadOutbox(job.Path, out attempts, out payload))
                {
                    UIALog.Warn("FeedbackService: outbox file is not a report (left untouched): " + Path.GetFileName(job.Path));
                    if (job.Announce) Say("uiafeedback: that outbox file could not be read - it was left on disk untouched.", ConsoleColor.Yellow);
                    job.Result = new SendResult { Outcome = SendOutcome.Failed, Reason = "the saved report file could not be read" };
                    return null;
                }
                if (attempts >= MaxAttempts)
                {
                    job.Result = new SendResult { Outcome = SendOutcome.Failed, Reason = "its automatic retries are used up",
                        Attempts = attempts, RetriesUsedUp = true };
                    return null;
                }

                string relay = RelayBase();
                if (!IsEnabled() || relay == null)
                {
                    if (job.Announce) Say("uiafeedback: feedback was disabled or the relay override became invalid before sending - the report stays in the outbox.", ConsoleColor.Yellow);
                    job.Result = new SendResult { Outcome = SendOutcome.NotSent,
                        Reason = !IsEnabled() ? "feedback is turned off" : "the Feedback > RelayUrl override is not a web address" };
                    return null;
                }

                UnityWebRequest req = null;
                try
                {
                    req = new UnityWebRequest(relay + "/v1/report", UnityWebRequest.kHttpVerbPOST);
                    var up = new UploadHandlerRaw(new UTF8Encoding(false).GetBytes(payload));
                    up.contentType = "application/json";
                    req.uploadHandler = up;
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.timeout = TimeoutSeconds;
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Accept", "application/json");
                    req.SetRequestHeader("X-UIA-Client", ClientHeaderValue);
                    req.SendWebRequest();
                    _inFlight = req;
                    return req;
                }
                catch (Exception e)
                {
                    try { if (req != null) req.Dispose(); } catch { }
                    RecordFailure(job, "could not start the request: " + e.Message);
                    return null;
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: send setup failed: " + e);
                return null;
            }
        }

        private static void FinishSend(Job job, UnityWebRequest req)
        {
            try
            {
                long code = 0;
                string body = null;
                string err = null;
                bool done = IsDoneSafe(req);
                if (!done)
                {
                    try { req.Abort(); } catch { }
                    err = "timed out";
                }
                else
                {
                    try { code = req.responseCode; } catch { }
                    try { if (req.downloadHandler != null) body = req.downloadHandler.text; } catch { }
                    try { if (req.result != UnityWebRequest.Result.Success) err = req.error; } catch { }
                }

                if (code == 201 && body != null && DryRunTrue.IsMatch(body))
                {
                    // The relay's DRY_RUN answers 201 {number:0, dryRun:true, issue} - it BUILT the
                    // issue but filed nothing, so this is not a delivery: keep it in the outbox.
                    RecordFailure(job, "the relay is in DRY_RUN mode - it built the issue but filed nothing", SendOutcome.DryRun);
                }
                else if (code == 201)
                {
                    int number = 0;
                    try
                    {
                        var m = body != null ? NumberField.Match(body) : null;
                        if (m != null && m.Success) int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                    }
                    catch { number = 0; }
                    MarkSent(job, number, false);
                }
                else if (code == 429 && body != null && DuplicateTrue.IsMatch(body))
                {
                    // 429 {duplicate:true} = an identical title+description LANDED within the last
                    // hour (the relay releases the dedupe key when filing fails). Most often our own
                    // earlier send whose 201 we never saw (timeout / reload). Counting it as a
                    // failure would re-send it after the window closes = a duplicate issue.
                    MarkSent(job, 0, true);
                }
                else
                {
                    string reason = code > 0 ? ("HTTP " + code.ToString(CultureInfo.InvariantCulture)) : (string.IsNullOrEmpty(err) ? "no response" : err);
                    string relayErr = body != null ? ReadJsonString(body, "error") : "";
                    if (relayErr.Length > 0) reason += ": " + Shorten(relayErr, 120);
                    RecordFailure(job, reason);
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: finishing a send failed: " + e);
            }
            finally
            {
                if (_inFlight == req) _inFlight = null;
                try { req.Dispose(); } catch { }
            }
        }

        /// <summary>Delivered (HTTP 201, or the relay's "identical report already received"): move the
        /// outbox file to sent/ as the local receipt. If the move is impossible, the file is parked at
        /// the attempts cap instead, so the automatic sweep can never re-send (= duplicate) a report
        /// the relay already filed.</summary>
        private static void MarkSent(Job job, int number, bool duplicate)
        {
            string label = number > 0 ? ("UIA-" + number.ToString(CultureInfo.InvariantCulture)) : null;
            job.Result = new SendResult { Outcome = duplicate ? SendOutcome.Duplicate : SendOutcome.Delivered, Number = number };
            bool moved = false;
            try
            {
                Directory.CreateDirectory(SentDir);
                string stem = Path.GetFileNameWithoutExtension(job.Path);
                string dst = Path.Combine(SentDir, stem + "-UIA-" + (number > 0 ? number.ToString(CultureInfo.InvariantCulture) : "unknown") + ".json");
                if (File.Exists(dst))
                    dst = Path.Combine(SentDir, stem + "-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)
                        + "-UIA-" + (number > 0 ? number.ToString(CultureInfo.InvariantCulture) : "unknown") + ".json");
                try { File.Move(job.Path, dst); moved = true; }
                catch
                {
                    File.Copy(job.Path, dst, true);
                    try { File.Delete(job.Path); moved = true; }
                    catch { moved = false; }
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: report delivered but the receipt could not be written: " + e.Message);
            }
            if (!moved)
            {
                // Still in outbox/ after a successful delivery: freeze it so only an explicit retry
                // (which the player would only run on purpose) could ever send it again.
                try
                {
                    int attempts;
                    string payload;
                    if (File.Exists(job.Path) && ReadOutbox(job.Path, out attempts, out payload))
                        WriteOutboxFile(job.Path, MaxAttempts, payload);
                }
                catch { }
            }

            if (job.Announce)
            {
                Say(label != null ? "Thanks - that's now " + label + "."
                    : duplicate ? "Thanks - an identical report already reached the developers this hour, so no new number was issued."
                    : "Thanks - the report was received (the relay sent no reference number).", ConsoleColor.Green);
                UIALog.Debug("Feedback: " + Path.GetFileName(job.Path) + " delivered as "
                    + (label ?? (duplicate ? "(duplicate of a recent report)" : "(no number)")) + ".");
            }
            else
            {
                UIALog.Info((job.FromUi ? "Feedback: a report from the F10 tab was delivered - "
                                        : "Feedback: an earlier report was delivered - ")
                    + (label != null ? "that's now " + label
                        : duplicate ? "an identical one had already arrived" : "filed (no number returned)") + ".");
            }
        }

        /// <summary>Any failure: the file stays in outbox/ and its attempts counter goes up by one.
        /// Also records the job's <see cref="SendResult"/> (<paramref name="outcome"/> Failed or
        /// DryRun) for the F10 tab.</summary>
        private static void RecordFailure(Job job, string reason, SendOutcome outcome = SendOutcome.Failed)
        {
            job.Result = new SendResult { Outcome = outcome, Reason = reason ?? "" };
            int attemptsNow = -1;
            try
            {
                int attempts;
                string payload;
                if (File.Exists(job.Path) && ReadOutbox(job.Path, out attempts, out payload))
                {
                    attemptsNow = Math.Max(0, attempts) + 1;
                    if (!WriteOutboxFile(job.Path, attemptsNow, payload)) attemptsNow = -1;
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: could not update the attempts counter: " + e.Message);
            }

            job.Result.Attempts = attemptsNow;
            job.Result.RetriesUsedUp = attemptsNow >= MaxAttempts;
            UIALog.Warn("FeedbackService: send failed for " + SafeFileName(job.Path) + " ("
                + (reason ?? "unknown") + ")" + (attemptsNow > 0 ? ", attempt " + attemptsNow + "/" + MaxAttempts : "") + ".");
            if (!job.Announce) return;
            Say("Saved locally - will retry.", ConsoleColor.Yellow);
            Say("  (" + (reason ?? "unknown error") + ")", ConsoleColor.White);
            if (attemptsNow >= MaxAttempts)
                Say("  " + MaxAttempts + " tries used up - automatic retries stop here; `uiafeedback retry` tries again.", ConsoleColor.White);
        }

        // ------------------------------------------------------------------ status (Phase 3 preview)

        private static UnityWebRequest BeginStatus(Receipt r)
        {
            try
            {
                if (r == null || r.Number <= 0) return null;
                string relay = RelayBase();
                if (!IsEnabled() || relay == null) return null;
                var req = UnityWebRequest.Get(relay + "/v1/status/" + r.Number.ToString(CultureInfo.InvariantCulture));
                req.timeout = TimeoutSeconds;
                req.SetRequestHeader("Accept", "application/json");
                req.SetRequestHeader("X-UIA-Client", ClientHeaderValue);
                req.SendWebRequest();
                _inFlight = req;
                return req;
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: status request failed to start: " + e.Message);
                return null;
            }
        }

        /// <summary>Print one status line (unless <paramref name="quiet"/> - the F10 tab's check) and
        /// cache it for the tab either way. Returns false when the status is "--" (unknown - never
        /// guessed: relay unreachable, non-200, or an unparseable answer). <paramref name="noResponse"/>
        /// = a request was made and NOTHING came back (timeout / transport failure, HTTP code 0).</summary>
        private static bool FinishStatus(Receipt r, UnityWebRequest req, bool quiet, out bool noResponse)
        {
            string status = "--";
            noResponse = false;
            try
            {
                if (req != null)
                {
                    if (!IsDoneSafe(req)) { noResponse = true; try { req.Abort(); } catch { } }
                    else
                    {
                        long code = 0;
                        string body = null;
                        try { code = req.responseCode; } catch { }
                        try { if (req.downloadHandler != null) body = req.downloadHandler.text; } catch { }
                        if (code == 200 && body != null) status = MapStatus(body);
                        if (code == 0) noResponse = true;
                    }
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: reading a status answer failed: " + e.Message);
                status = "--";
            }
            finally
            {
                if (req != null)
                {
                    if (_inFlight == req) _inFlight = null;
                    try { req.Dispose(); } catch { }
                }
            }

            try
            {
                if (r != null && r.Number > 0)
                {
                    _statusCache[r.Number] = status;
                    _statusChecking.Remove(r.Number);
                    Bump();
                }
            }
            catch { }

            if (quiet) return status != "--";
            try
            {
                string id = r != null && r.Number > 0 ? "UIA-" + r.Number.ToString(CultureInfo.InvariantCulture) : "UIA-?";
                string kind = r != null && r.Kind.Length > 0 ? r.Kind : "?";
                string title = r != null ? Shorten(r.Title, 48) : "";
                Say("  " + id + "  " + kind + "  \"" + title + "\"  -  " + status,
                    status == "--" ? ConsoleColor.Yellow : ConsoleColor.White);
            }
            catch { }
            return status != "--";
        }

        private static void EndStatusJob(bool anyDash, bool quiet)
        {
            if (anyDash && !quiet)
                Say("  -- = unknown right now (relay unreachable, or it does not know that number). Nothing is guessed.",
                    ConsoleColor.White);
        }

        /// <summary>Relay <c>{state, labels}</c> -> player words (plan §4). Labels may be plain
        /// strings or GitHub-style <c>{"name": ...}</c> objects; both are read.</summary>
        private static string MapStatus(string body)
        {
            var sm = StateField.Match(body);
            if (!sm.Success) return "--";
            string state = JsonUnescape(sm.Groups[1].Value);
            var labels = ReadLabels(body);

            if (state.Equals("closed", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    string l = labels[i];
                    if (l.StartsWith("fixed-in:", StringComparison.OrdinalIgnoreCase))
                    {
                        string ver = l.Substring("fixed-in:".Length).Trim();
                        if (ver.Length > 0) return "Fixed in " + ver;
                    }
                }
                return "Closed";
            }
            if (state.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                if (HasLabel(labels, "plan:ready")) return "Under review";
                if (HasLabel(labels, "triage:pending")) return "Received";
                return "Open";
            }
            return "--";
        }

        private static List<string> ReadLabels(string body)
        {
            var list = new List<string>();
            try
            {
                int key = body.IndexOf("\"labels\"", StringComparison.Ordinal);
                if (key < 0) return list;
                int open = body.IndexOf('[', key);
                if (open < 0) return list;
                // Find the matching ']' while skipping string contents.
                int depth = 0, close = -1;
                bool inStr = false;
                for (int i = open; i < body.Length; i++)
                {
                    char c = body[i];
                    if (inStr)
                    {
                        if (c == '\\') { i++; continue; }
                        if (c == '"') inStr = false;
                        continue;
                    }
                    if (c == '"') inStr = true;
                    else if (c == '[') depth++;
                    else if (c == ']') { depth--; if (depth == 0) { close = i; break; } }
                }
                if (close < 0) return list;
                string arr = body.Substring(open + 1, close - open - 1);
                var rx = arr.IndexOf('{') >= 0 ? JsonNameField : JsonStringLiteral;
                foreach (Match m in rx.Matches(arr)) list.Add(JsonUnescape(m.Groups[1].Value));
            }
            catch { }
            return list;
        }

        private static bool HasLabel(List<string> labels, string want)
        {
            for (int i = 0; i < labels.Count; i++)
                if (string.Equals(labels[i], want, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ outbox files

        /// <summary>Outbox format = the exact wire payload with an <c>attempts</c> member written
        /// FIRST: <c>{"attempts":N,"kind":...}</c>. Writing it first is what lets it be read and
        /// rewritten without a JSON parser, and lets the POST send the payload WITHOUT it. A
        /// hand-dropped file lacking the prefix is treated as attempts 0 and sent as-is.</summary>
        private static bool ReadOutbox(string path, out int attempts, out string payload)
        {
            attempts = 0;
            payload = null;
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                text = sr.ReadToEnd();
            if (string.IsNullOrEmpty(text)) return false;
            var m = AttemptsPrefix.Match(text);
            if (m.Success)
            {
                int a;
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out a)) attempts = a;
                payload = "{" + text.Substring(m.Length);
                return true;
            }
            string t = text.Trim();
            if (t.StartsWith("{", StringComparison.Ordinal) && t.EndsWith("}", StringComparison.Ordinal))
            {
                payload = t;
                return true;
            }
            return false;
        }

        private static string ComposeOutbox(int attempts, string payload)
        {
            string p = payload.Trim();
            string rest = p.Substring(1).TrimStart();   // after the opening '{'
            string head = "{\"attempts\":" + attempts.ToString(CultureInfo.InvariantCulture);
            return rest.StartsWith("}", StringComparison.Ordinal) ? head + rest : head + "," + rest;
        }

        /// <summary>Atomic-ish write: temp file first, then swap it in, so a crash mid-write never
        /// leaves a truncated report where the only copy used to be.</summary>
        private static bool WriteOutboxFile(string path, int attempts, string payload)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, ComposeOutbox(attempts, payload), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null); return true; }
                    catch { File.Delete(path); }
                }
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("FeedbackService: writing " + SafeFileName(path) + " failed: " + e.Message);
                return false;
            }
        }

        private static string WriteNewOutboxFile(string payload, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(OutboxDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                string path = Path.Combine(OutboxDir, stamp + ".json");
                for (int n = 2; File.Exists(path) && n < 100; n++)
                    path = Path.Combine(OutboxDir, stamp + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
                if (!WriteOutboxFile(path, 0, payload))
                {
                    error = "could not write " + Path.GetFileName(path);
                    return null;
                }
                Bump();
                return path;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>*.json files in a folder, ordinal-sorted (= chronological for our timestamp
        /// names). Missing folder = empty list.</summary>
        private static List<string> ListJson(string dir)
        {
            var list = new List<string>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) list.Add(f);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static bool OutboxHasSendable()
        {
            foreach (var path in ListJson(OutboxDir))
            {
                try
                {
                    int attempts;
                    string payload;
                    if (ReadOutbox(path, out attempts, out payload) && attempts < MaxAttempts) return true;
                }
                catch { }
            }
            return false;
        }

        // ------------------------------------------------------------------ auto-context (plan §1.3)

        /// <summary>Every field individually fail-soft: a value that cannot be read is "" and never
        /// blocks the report. Order = the order the relay contract lists them.</summary>
        private static List<KeyValuePair<string, string>> CaptureContext()
        {
            var ctx = new List<KeyValuePair<string, string>>(8);
            ctx.Add(new KeyValuePair<string, string>("modVersion", Safe(() => global::StationeersUIMod.StationeersUIMod.VersionDisplay)));
            ctx.Add(new KeyValuePair<string, string>("deployRoute", Safe(DeployRoute)));
            // GameManager.GetGameVersion(): the game's own version string - what the `version`
            // console command prints and what the MP handshake compares. Verified against
            // Reference/StationeersGameVersions/Stationeers 8-1-26 V27758 Orbital Update Beta/
            // Assembly-CSharp/Assets/Scripts/GameManager.cs:248 (public static string, cached
            // Assembly-CSharp AssemblyName.Version) and Util/Commands/VersionCommand.cs:42.
            ctx.Add(new KeyValuePair<string, string>("gameBuild", Safe(() => GameManager.GetGameVersion())));
            string profile = Safe(ActiveProfileName);
            ctx.Add(new KeyValuePair<string, string>("profileName", profile));
            ctx.Add(new KeyValuePair<string, string>("profileModified",
                Safe(() => profile.Length > 0 ? Features.HudProfileStore.ShippedEditState(profile) : "")));
            ctx.Add(new KeyValuePair<string, string>("playerContext", Safe(PlayerContext)));
            ctx.Add(new KeyValuePair<string, string>("display", Safe(() => Screen.width.ToString(CultureInfo.InvariantCulture)
                + "x" + Screen.height.ToString(CultureInfo.InvariantCulture))));
            ctx.Add(new KeyValuePair<string, string>("os", Safe(() => SystemInfo.operatingSystem)));
            return ctx;
        }

        private static string ContextValue(List<KeyValuePair<string, string>> ctx, string key)
        {
            for (int i = 0; i < ctx.Count; i++)
                if (ctx[i].Key == key) return ctx[i].Value;
            return "";
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        /// <summary>SLP package (local mod folder / Workshop) vs the dev shim, told apart by the cfg
        /// file name exactly as OnLoaded's legacy-cfg migration does; within the shim, ScriptEngine
        /// (assembly loaded from bytes or from its dump folder) vs a BepInEx/plugins copy.</summary>
        private static string DeployRoute()
        {
            string cfg = _cfgFileName ?? "";
            if (!cfg.Equals(ShimCfgName, StringComparison.OrdinalIgnoreCase))
            {
                string dir = global::StationeersUIMod.StationeersUIMod.ModDirectory;
                if (string.IsNullOrEmpty(dir)) return "SLP";
                return dir.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "SLP (Workshop)" : "SLP (local mod folder)";
            }
            string loc = "";
            try { loc = typeof(FeedbackService).Assembly.Location ?? ""; } catch { loc = ""; }
            if (loc.Length == 0
                || loc.IndexOf("ScriptEngine", StringComparison.OrdinalIgnoreCase) >= 0
                || loc.IndexOf(Path.DirectorySeparatorChar + "scripts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                return "ScriptEngine (F6 hot-reload)";
            if (loc.IndexOf(Path.DirectorySeparatorChar + "plugins" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                return "BepInEx plugins";
            return "dev loader";
        }

        private static string ActiveProfileName()
        {
            var doc = Features.HudProfileStore.Active;
            if (doc != null && !string.IsNullOrEmpty(doc.Name)) return doc.Name;
            var e = UI.Hud.HudConfig.HudActiveProfile;
            return e != null ? (e.Value ?? "") : "";
        }

        /// <summary>"SP" / "MP client" / "MP host (n connected)", plus the HUD tier. Network role
        /// verified against V27758: NetworkManager.IsClient/IsServer compare the public static
        /// NetworkManager.NetworkRole (Assets/Scripts/Networking/NetworkManager.cs:55,65,1825 -
        /// None when offline/SP, Server when hosting, Client when joined; assignments at :133/:682/
        /// :864/:929/:951), and NetworkBase.Clients is the host's public static client list
        /// (NetworkBase.cs:195) - the same pair GamePause.CanOwnPause already relies on. The tier is
        /// the one HudSystem sampled last frame (HudSampler: Robot / Suited = powered suit / Bare).</summary>
        private static string PlayerContext()
        {
            string role = "";
            try
            {
                if (NetworkManager.IsClient) role = "MP client";
                else if (NetworkManager.IsServer)
                {
                    int n = 0;
                    try { n = NetworkBase.Clients.Count; } catch { }
                    role = "MP host (" + n.ToString(CultureInfo.InvariantCulture) + " connected)";
                }
                else role = "SP";
            }
            catch { role = ""; }

            string tier = "";
            try
            {
                if (Guards.LocalHuman == null) tier = "not in a world";
                else
                {
                    var snap = UI.Hud.HudSystem.LastSnapshot;
                    if (snap != null && snap.Valid) tier = snap.Tier.ToString();
                }
            }
            catch { tier = ""; }

            if (role.Length == 0) return tier;
            return tier.Length == 0 ? role : role + " - " + tier;
        }

        // ------------------------------------------------------------------ opt-in attachments (plan §1.4)

        /// <summary>The ACTIVE profile's XML as it is ON DISK (unsaved in-memory edits are not in it -
        /// profileModified says when that is the case). Capped at 100 KB with a marker line.</summary>
        private static string ReadProfileXml(string profileName, out string note)
        {
            note = null;
            try
            {
                string path = string.IsNullOrEmpty(profileName) ? null : Features.HudProfileStore.ProfilePath(profileName);
                if (path == null || !File.Exists(path))
                {
                    note = "+profile: no profile file on disk for the active HUD profile - sending without it.";
                    return null;
                }
                var buf = new char[ProfileCap + 1];
                int n;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                    n = sr.ReadBlock(buf, 0, buf.Length);
                if (n <= ProfileCap) return new string(buf, 0, n);
                const string marker = "\n[... truncated by UI Ascended: profile exceeded 100 KB ...]";
                note = "+profile: your profile is over 100 KB - it was truncated.";
                return new string(buf, 0, ProfileCap - marker.Length) + marker;
            }
            catch (Exception e)
            {
                note = "+profile: could not read the profile (" + e.Message + ") - sending without it.";
                return null;
            }
        }

        /// <summary>Filtered log excerpt: ONLY the mod's own lines ("[StationeersUIMod" prefix) and
        /// exception blocks that mention a StationeersUIMod frame - never the whole log (it holds save
        /// names, mod lists and worse). Source: Unity's player log (Application.consoleLogPath) first,
        /// because under SLP the mod logs through Debug.Log and BepInEx's LogOutput.log omits Unity
        /// messages by default ([Logging.Disk] WriteUnityLog = false); LogOutput.log is the fallback
        /// (it carries the ScriptEngine shim's ManualLogSource lines). Newest ~100 lines, 30 KB cap,
        /// the user-profile folder redacted to %USERPROFILE%. Unreadable = omitted with a note.</summary>
        private static string ReadLogExcerpt(out string note)
        {
            note = null;
            var sources = new List<KeyValuePair<string, bool>>(2); // path, isBepInExLog
            try
            {
                string p = Application.consoleLogPath;
                if (!string.IsNullOrEmpty(p)) sources.Add(new KeyValuePair<string, bool>(p, false));
            }
            catch { }
            try { sources.Add(new KeyValuePair<string, bool>(Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log"), true)); }
            catch { }

            bool anyReadable = false;
            string lastError = null;
            for (int i = 0; i < sources.Count; i++)
            {
                try
                {
                    if (!File.Exists(sources[i].Key)) continue;
                    var lines = FilterLog(sources[i].Key, sources[i].Value);
                    anyReadable = true;
                    if (lines.Count == 0) continue;
                    return ComposeLog(lines);
                }
                catch (Exception e) { lastError = e.Message; }
            }
            note = anyReadable
                ? "+log: no UI Ascended lines found in the log - sending without it."
                : "+log: could not read the game log" + (lastError != null ? " (" + lastError + ")" : "") + " - sending without it.";
            return null;
        }

        private static List<string> FilterLog(string path, bool bepinexLog)
        {
            var blocks = new List<List<string>>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0L, fs.Length - LogTailBytes);
                if (start > 0) fs.Seek(start, SeekOrigin.Begin);
                using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                {
                    if (start > 0) sr.ReadLine();   // drop the partial first line
                    var block = new List<string>();
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length > LogMaxLineChars) line = line.Substring(0, LogMaxLineChars) + " [...]";
                        if (block.Count > 0 && IsContinuation(line))
                        {
                            if (block.Count < 80) block.Add(line);
                            continue;
                        }
                        KeepBlock(block, blocks, bepinexLog);
                        block = new List<string>();
                        block.Add(line);
                    }
                    KeepBlock(block, blocks, bepinexLog);
                }
            }

            // Newest blocks first until the line budget is spent, then restore chronological order.
            var picked = new List<List<string>>();
            int count = 0;
            for (int i = blocks.Count - 1; i >= 0 && count < LogLineBudget; i--)
            {
                picked.Add(blocks[i]);
                count += blocks[i].Count;
            }
            picked.Reverse();
            var result = new List<string>(count);
            for (int i = 0; i < picked.Count; i++) result.AddRange(picked[i]);
            return result;
        }

        private static bool IsContinuation(string line)
        {
            if (line.Length == 0) return false;
            char c = line[0];
            if (c == ' ' || c == '\t') return true;
            if (line.StartsWith("at ", StringComparison.Ordinal)
                || line.StartsWith("Stack trace:", StringComparison.Ordinal)
                || line.StartsWith("Rethrow as ", StringComparison.Ordinal)
                || line.StartsWith("(Filename:", StringComparison.Ordinal)
                || line.StartsWith("--- End of", StringComparison.Ordinal)
                || line.StartsWith("(wrapper ", StringComparison.Ordinal)) return true;
            return UnityFrame.IsMatch(line);
        }

        private static void KeepBlock(List<string> block, List<List<string>> into, bool bepinexLog)
        {
            if (block == null || block.Count == 0) return;
            string head = block[0];
            bool ours = head.IndexOf("[StationeersUIMod", StringComparison.Ordinal) >= 0
                || (bepinexLog && head.IndexOf(":Stationeers UI Mod (", StringComparison.Ordinal) >= 0);
            if (!ours)
            {
                bool exception = false, modFrame = false;
                for (int i = 0; i < block.Count; i++)
                {
                    string l = block[i];
                    if (!exception && l.IndexOf("Exception", StringComparison.Ordinal) >= 0) exception = true;
                    if (!modFrame && l.IndexOf("StationeersUIMod", StringComparison.Ordinal) >= 0) modFrame = true;
                }
                ours = exception && modFrame;
            }
            if (!ours) return;
            into.Add(block);
            if (into.Count > 4 * LogLineBudget) into.RemoveRange(0, into.Count - 2 * LogLineBudget);
        }

        private static string ComposeLog(List<string> lines)
        {
            string text = RedactUserProfile(string.Join("\n", lines.ToArray()));
            if (text.Length <= LogCap) return text;
            const string marker = "[... older lines truncated by UI Ascended to fit 30 KB ...]\n";
            string tail = text.Substring(text.Length - (LogCap - marker.Length));
            int nl = tail.IndexOf('\n');
            if (nl >= 0 && nl < tail.Length - 1) tail = tail.Substring(nl + 1);   // start on a whole line
            return marker + tail;
        }

        private static string RedactUserProfile(string text)
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home) || home.Length < 4) return text;
                text = ReplaceIgnoreCase(text, home, "%USERPROFILE%");
                text = ReplaceIgnoreCase(text, home.Replace('\\', '/'), "%USERPROFILE%");
            }
            catch { }
            return text;
        }

        private static string ReplaceIgnoreCase(string text, string find, string with)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(find)) return text;
            int i = text.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return text;
            var sb = new StringBuilder(text.Length);
            int from = 0;
            while (i >= 0)
            {
                sb.Append(text, from, i - from).Append(with);
                from = i + find.Length;
                i = text.IndexOf(find, from, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(text, from, text.Length - from);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ JSON (hand-built)

        /// <summary>The relay contract (plan §2.3 + FeedbackRelay/ReportModels.cs): <c>{kind, title,
        /// description, contact?, context{modVersion, deployRoute, gameBuild, profileName,
        /// profileModified, playerContext, display, os}, profileXml?, logExcerpt?}</c>. Context values
        /// are strings except profileModified, a JSON bool (omitted when unknown) - see
        /// <see cref="ProfileModifiedLiteral"/>.</summary>
        private static string BuildPayload(string kind, string title, string description, string contact,
            List<KeyValuePair<string, string>> ctx, string profileXml, string logExcerpt)
        {
            var sb = new StringBuilder(512 + (profileXml != null ? profileXml.Length : 0) + (logExcerpt != null ? logExcerpt.Length : 0));
            sb.Append('{');
            AppendMember(sb, "kind", kind, true);
            AppendMember(sb, "title", title, false);
            AppendMember(sb, "description", description, false);
            if (!string.IsNullOrEmpty(contact)) AppendMember(sb, "contact", contact, false);
            sb.Append(",\"context\":{");
            bool firstCtx = true;
            for (int i = 0; i < ctx.Count; i++)
            {
                string k = ctx[i].Key;
                if (k == "profileModified")
                {
                    // The relay binds this as bool? - a string here is a 400 (FeedbackRelay/
                    // ReportModels.cs). Unknown = omitted, which the relay reads as null.
                    string lit = ProfileModifiedLiteral(ctx[i].Value);
                    if (lit == null) continue;
                    if (!firstCtx) sb.Append(',');
                    AppendJsonString(sb, k);
                    sb.Append(':').Append(lit);
                }
                else AppendMember(sb, k, ctx[i].Value, firstCtx);
                firstCtx = false;
            }
            sb.Append('}');
            if (profileXml != null) AppendMember(sb, "profileXml", profileXml, false);
            if (logExcerpt != null) AppendMember(sb, "logExcerpt", logExcerpt, false);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>HudProfileStore.ShippedEditState -> the wire bool. "no" (a pristine shipped theme)
        /// = false. "yes", "custom" and any "+unsaved edits" = true: the question a triager asks is
        /// "does the shipped default reproduce this?", and for a player's own profile the answer is
        /// no. "" (could not tell) = null, i.e. the member is omitted - never a guess.</summary>
        private static string ProfileModifiedLiteral(string state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            return state == "no" ? "false" : "true";
        }

        private static void AppendMember(StringBuilder sb, string key, string value, bool first)
        {
            if (!first) sb.Append(',');
            AppendJsonString(sb, key);
            sb.Append(':');
            AppendJsonString(sb, value ?? "");
        }

        /// <summary>RFC 8259 string escaping: quote, backslash and every control character; U+2028/
        /// U+2029 too (harmless, and keeps the text safe for JS consumers). Other non-ASCII passes
        /// straight through (valid JSON; the body is sent as UTF-8).</summary>
        private static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ' || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>First <c>"key": "value"</c> string member in our own file. Safe for top-level
        /// keys we write before any nested object (kind, title): inside a JSON string every quote is
        /// escaped, so the unescaped sequence <c>"key":</c> cannot occur within a value.</summary>
        private static string ReadJsonString(string json, string key)
        {
            try
            {
                var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                    RegexOptions.CultureInvariant);
                return m.Success ? JsonUnescape(m.Groups[1].Value) : "";
            }
            catch { return ""; }
        }

        private static string JsonUnescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char e = s[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else sb.Append('u');
                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ console command

        /// <summary>
        /// <c>uiafeedback</c> - send a bug report or an idea to the developers from the game console.
        /// Registered through the shared <see cref="Patch_CommandLine_Process"/> prefix (FinderCommands.cs).
        /// MUTATES NO GAME STATE: network I/O and the mod's own Feedback/ folder only - consistent
        /// with the console rule (read-only diagnostics; no game-state mutation). All output ASCII.
        /// <list type="bullet">
        /// <item><c>uiafeedback</c> - usage, settings, outbox/sent counts, what gets sent.</item>
        /// <item><c>uiafeedback bug|idea &lt;title&gt; | &lt;description&gt; [+profile] [+log]</c></item>
        /// <item><c>uiafeedback retry</c> - zero every attempts counter, resend the outbox oldest-first.</item>
        /// <item><c>uiafeedback status</c> - relay status for the newest sent receipts ("--" if unknown).</item>
        /// </list>
        /// </summary>
        public static void Command(string input)
        {
            try
            {
                string text = (input ?? "").Trim();
                // Drop the command word itself.
                int sp = text.IndexOf(' ');
                string rest = sp >= 0 ? text.Substring(sp + 1).TrimStart() : "";
                int sp2 = rest.IndexOf(' ');
                string sub = (sp2 >= 0 ? rest.Substring(0, sp2) : rest).ToLowerInvariant();
                string args = sp2 >= 0 ? rest.Substring(sp2 + 1) : "";

                if (sub.Length == 0 || sub == "help" || sub == "?") { PrintUsage(); return; }
                if (sub == "bug") { SubmitFromConsole("bug", args); return; }
                if (sub == "idea" || sub == "suggestion" || sub == "suggest") { SubmitFromConsole("suggestion", args); return; }
                if (sub == "retry") { RetryFromConsole(); return; }
                if (sub == "status") { StatusFromConsole(); return; }

                Say("uiafeedback: unknown subcommand '" + Shorten(sub, 24) + "'.", ConsoleColor.Yellow);
                PrintUsage();
            }
            catch (Exception e)
            {
                Say("uiafeedback failed: " + e.Message, ConsoleColor.Red);
                UIALog.Warn("FeedbackService: command failed: " + e);
            }
        }

        private static void PrintUsage()
        {
            Say("uiafeedback: send a bug report or an idea straight to the UI Ascended developers.", ConsoleColor.Cyan);
            Say("  uiafeedback bug <title> | <what you did, what you expected, what happened> [+profile] [+log]", ConsoleColor.White);
            Say("  uiafeedback idea <title> | <your idea> [+profile] [+log]", ConsoleColor.White);
            Say("  uiafeedback retry    - resend everything still waiting in the outbox", ConsoleColor.White);
            Say("  uiafeedback status   - what happened to the reports you sent", ConsoleColor.White);
            Say("  +profile attaches your active HUD profile; +log attaches recent UI Ascended log lines only.", ConsoleColor.White);
            Say("  Always sent: mod version, install type, game build, HUD profile name (+ whether it was edited),",
                ConsoleColor.White);
            Say("  SP/MP role + HUD tier, screen size, OS. No Steam ID, no save name. Anonymous.", ConsoleColor.White);

            bool enabled = IsEnabled();
            string relay = RelayBase();
            string raw = RawRelaySetting().Trim();
            Say("  Enabled: " + (Enabled == null ? "unknown (settings not loaded)" : (enabled ? "yes" : "no (Feedback > Enabled)")),
                enabled ? ConsoleColor.White : ConsoleColor.Yellow);
            if (relay != null) Say("  Relay: " + relay + (raw.Length == 0 ? " (built-in)" : " (override from Feedback > RelayUrl)"),
                ConsoleColor.White);
            else Say("  Relay: the override '" + Shorten(raw, 60) + "' is not an http(s) URL - fix it, or clear "
                + "Feedback > RelayUrl to use the built-in relay.", ConsoleColor.Yellow);

            int waiting = 0, stuck = 0, sent = 0;
            try
            {
                foreach (var p in ListJson(OutboxDir))
                {
                    int a; string payload;
                    try { if (ReadOutbox(p, out a, out payload) && a >= MaxAttempts) stuck++; } catch { }
                    waiting++;
                }
                sent = ListJson(SentDir).Count;
            }
            catch { }
            Say("  Outbox: " + waiting + " waiting" + (stuck > 0 ? " (" + stuck + " out of automatic retries - use retry)" : "")
                + "   Sent: " + sent, ConsoleColor.White);
        }

        private static void SubmitFromConsole(string kind, string args)
        {
            if (Enabled == null) { Say("uiafeedback: feedback settings are not loaded - nothing was saved.", ConsoleColor.Yellow); return; }
            if (!IsEnabled())
            {
                Say("uiafeedback: feedback is turned off (Feedback > Enabled = false). Nothing was saved or sent.", ConsoleColor.Yellow);
                return;
            }

            string word = kind == "bug" ? "bug" : "idea";
            int bar = (args ?? "").IndexOf('|');
            if (bar < 0)
            {
                Say("uiafeedback: separate the title from the description with ' | ', e.g.", ConsoleColor.Yellow);
                Say("  uiafeedback " + word + " Radial closes early | It closes when I swap hands. +profile", ConsoleColor.White);
                return;
            }
            string title = args.Substring(0, bar).Trim();
            string desc = args.Substring(bar + 1);

            // +profile / +log are recognised anywhere after the description and stripped from it.
            bool wantProfile = false, wantLog = false;
            var kept = new StringBuilder(desc.Length);
            foreach (var tok in desc.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Equals("+profile", StringComparison.OrdinalIgnoreCase)) { wantProfile = true; continue; }
                if (tok.Equals("+log", StringComparison.OrdinalIgnoreCase)) { wantLog = true; continue; }
                if (kept.Length > 0) kept.Append(' ');
                kept.Append(tok);
            }
            desc = kept.ToString().Trim();

            if (title.Length == 0 || desc.Length == 0)
            {
                Say("uiafeedback: both a title and a description are needed (title | description).", ConsoleColor.Yellow);
                return;
            }
            if (title.Length > TitleCap)
            {
                title = title.Substring(0, TitleCap).TrimEnd();
                Say("  (title trimmed to " + TitleCap + " characters)", ConsoleColor.White);
            }
            if (desc.Length > DescriptionCap)
            {
                desc = desc.Substring(0, DescriptionCap).TrimEnd();
                Say("  (description trimmed to " + DescriptionCap + " characters)", ConsoleColor.White);
            }

            var notes = new List<string>();
            string error;
            string path = SaveReport(kind, title, desc, null, wantProfile, wantLog, notes, out error);
            for (int i = 0; i < notes.Count; i++) Say("  " + notes[i], ConsoleColor.Yellow);
            if (path == null)
            {
                Say("uiafeedback: could not save the report to disk (" + (error ?? "unknown error") + ") - nothing was sent.", ConsoleColor.Red);
                return;
            }

            Say("uiafeedback: " + word + " report saved (Feedback/outbox/" + Path.GetFileName(path) + ")"
                + (wantProfile || wantLog ? " with" + (wantProfile ? " +profile" : "") + (wantLog ? " +log" : "") : "") + ".",
                ConsoleColor.Cyan);
            if (RelayBase() == null)
            {
                Say("Saved locally - the Feedback > RelayUrl override is not a valid URL. Fix or clear it; the report "
                    + "is sent automatically once the relay is reachable.", ConsoleColor.Yellow);
                return;
            }
            // Printed BEFORE queueing: the pump runs synchronously up to its first yield, so a request
            // that cannot even be created reports its failure inside QueueSend.
            Say("  sending...", ConsoleColor.White);
            if (!QueueSend(path, true, true)) Say("Saved locally - will retry.", ConsoleColor.Yellow);
        }

        private static void RetryFromConsole()
        {
            if (!IsEnabled())
            {
                Say("uiafeedback: feedback is turned off (Feedback > Enabled = false) - nothing retried.", ConsoleColor.Yellow);
                return;
            }
            if (RelayBase() == null)
            {
                Say("uiafeedback: the Feedback > RelayUrl override is not a valid URL - fix or clear it. Nothing sent.",
                    ConsoleColor.Yellow);
                return;
            }
            var files = ListJson(OutboxDir);
            if (files.Count == 0)
            {
                Say("uiafeedback: the outbox is empty - nothing to retry.", ConsoleColor.Cyan);
                return;
            }
            // "Retry means the broken thing is fixed": every counter back to zero FIRST, so reports
            // that exhausted their automatic tries get a clean five again.
            int reset = ResetAttempts(files, null);
            int queued = Sweep(true);
            int already = reset - queued;
            Say("uiafeedback: retrying " + queued + " report(s), one at a time"
                + (already > 0 ? " (" + already + " already on the way)" : "") + "...", ConsoleColor.Cyan);
        }

        /// <summary>Zero the attempts counter of every readable outbox file in <paramref name="files"/>
        /// (except <paramref name="exceptPath"/>). Returns how many readable reports there were. Shared
        /// by `uiafeedback retry` and the F10 tab's "Send waiting reports now".</summary>
        private static int ResetAttempts(List<string> files, string exceptPath)
        {
            int reset = 0;
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    if (exceptPath != null && string.Equals(files[i], exceptPath, StringComparison.OrdinalIgnoreCase)) continue;
                    int attempts;
                    string payload;
                    if (ReadOutbox(files[i], out attempts, out payload))
                    {
                        if (attempts != 0) WriteOutboxFile(files[i], 0, payload);
                        reset++;
                    }
                }
                catch (Exception e) { UIALog.Warn("FeedbackService: retry could not reset " + SafeFileName(files[i]) + ": " + e.Message); }
            }
            if (reset > 0) Bump();
            return reset;
        }

        private static void StatusFromConsole()
        {
            if (!IsEnabled())
            {
                Say("uiafeedback: feedback is turned off (Feedback > Enabled = false).", ConsoleColor.Yellow);
                return;
            }

            // Local first (no network): anything still waiting.
            var outbox = ListJson(OutboxDir);
            if (outbox.Count > 0)
            {
                Say("uiafeedback: " + outbox.Count + " report(s) not sent yet:", ConsoleColor.Cyan);
                for (int i = 0; i < outbox.Count; i++)
                {
                    try
                    {
                        int attempts;
                        string payload;
                        if (!ReadOutbox(outbox[i], out attempts, out payload)) continue;
                        Say("  (waiting)  " + DisplayKind(ReadJsonString(payload, "kind")) + "  \""
                            + Shorten(ReadJsonString(payload, "title"), 48) + "\"  -  tried " + attempts + "/" + MaxAttempts,
                            ConsoleColor.White);
                    }
                    catch { }
                }
            }

            var sent = ListJson(SentDir);
            if (sent.Count == 0)
            {
                Say("uiafeedback: no sent reports yet.", ConsoleColor.Cyan);
                return;
            }
            if (RelayBase() == null)
            {
                Say("uiafeedback: the Feedback > RelayUrl override is not a valid URL - status unknown (--).", ConsoleColor.Yellow);
                return;
            }
            if (_statusQueued)
            {
                Say("uiafeedback: a status check is already running.", ConsoleColor.Cyan);
                return;
            }

            sent.Reverse();   // newest first
            var receipts = new List<Receipt>();
            for (int i = 0; i < sent.Count && receipts.Count < StatusMaxReceipts; i++)
            {
                var r = new Receipt();
                try
                {
                    var m = ReceiptName.Match(Path.GetFileName(sent[i]));
                    int n;
                    if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) r.Number = n;
                    int attempts;
                    string payload;
                    if (ReadOutbox(sent[i], out attempts, out payload))
                    {
                        r.Kind = DisplayKind(ReadJsonString(payload, "kind"));
                        r.Title = ReadJsonString(payload, "title");
                    }
                }
                catch { }
                receipts.Add(r);
            }
            if (!EnsureHost())
            {
                Say("uiafeedback: could not start the status check.", ConsoleColor.Red);
                return;
            }
            Say("uiafeedback: checking " + receipts.Count + " sent report(s)"
                + (sent.Count > receipts.Count ? " (the newest " + receipts.Count + " of " + sent.Count + ")" : "") + "...",
                ConsoleColor.Cyan);
            _statusQueued = true;
            _queue.Add(new Job { IsStatus = true, Receipts = receipts });
            EnsurePump();
        }

        // ------------------------------------------------------------------ small helpers

        private static string DisplayKind(string kind)
        {
            if (string.Equals(kind, "suggestion", StringComparison.OrdinalIgnoreCase)) return "idea";
            if (string.Equals(kind, "bug", StringComparison.OrdinalIgnoreCase)) return "bug";
            return string.IsNullOrEmpty(kind) ? "?" : kind;
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path) ?? "?"; } catch { return "?"; }
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            return s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        /// <summary>Console output, forced to printable ASCII (the display-string rule: anything a
        /// player typed, or a path with a non-ASCII user name, prints '?' instead of tofu).</summary>
        private static void Say(string msg, ConsoleColor color)
        {
            try { ConsoleWindow.Print(Ascii(msg), color); } catch { }
        }

        private static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c >= ' ' && c <= '~' ? c : '?');
            }
            return sb.ToString();
        }
    }
}
