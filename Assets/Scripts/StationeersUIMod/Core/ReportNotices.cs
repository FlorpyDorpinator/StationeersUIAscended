using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using StationeersUIMod.UI.Menu;
using StationeersUIMod.UI.Menu.Tutorial;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// "Your report was fixed / answered" popups (2026-10-02, GitHub #9-#11). A player who sent a
    /// report from F10 > Suggestions/Bugs keeps a local receipt per report
    /// (<c>Feedback/sent/...-UIA-n.json</c>, see <see cref="FeedbackService"/>). Once per session, a
    /// little after they are in the world with nothing else on screen, this shows one card per news
    /// item, chained (OK on one card swaps in the next):
    /// <list type="number">
    ///   <item>A hand-written REPLY compiled into this build (<see cref="Replies"/>), keyed by issue
    ///     number: a fix that ships in this build, or an answer that needs no code. Local only - no
    ///     network, and only the player holding that number's receipt ever sees it.</item>
    ///   <item>Otherwise a generic "fixed in X" card, when the relay's status for one of their
    ///     reports reads closed + <c>fixed-in:X</c> (the label set at release, mapped by
    ///     <c>FeedbackService.MapStatus</c>) and this build is X or newer. It rides the F10 tab's own
    ///     quiet status check, so it needs no relay change and inherits Feedback > Enabled (the kill
    ///     switch) and the one-request-at-a-time pump.</item>
    /// </list>
    ///
    /// <para><b>No text comes from GitHub.</b> The repo is public and anyone can comment, so a
    /// reply's words live here, in reviewed code. The only outside text on a card is the player's
    /// OWN report title, read from their own receipt, made printable ASCII with TMP rich-text
    /// (<c>&lt; &gt;</c>) and tutorial-token (<c>{ }</c>) characters neutralised.</para>
    ///
    /// <para><b>Once.</b> A card counts as seen when the player closes it (OK, X or Esc); its keys go
    /// to <c>Feedback/notices-seen.txt</c> ("note:&lt;id&gt;", "fixed:&lt;n&gt;"). A card that
    /// vanished without a callback (another card replaced it) shows again later; a crash before the
    /// close shows it again next session.</para>
    ///
    /// <para><b>The card</b> is the tutorial coach's (<see cref="TutorialCoach.OpenCard"/>, sort 5600,
    /// re-skins with the UI theme, holds the single-player pause like a lesson card). The Director
    /// already treats any open card as "suspend" (<c>TutorialDirector.Suspended</c>), and this waits
    /// until no lesson, tip or card is up, so the two never fight. A card the coach cannot build
    /// backs off (30 s) and gives up after three tries. Mutates no game state. Hot reload:
    /// <see cref="Shutdown"/> resets every static.</para>
    /// </summary>
    internal static class ReportNotices
    {
        private sealed class Reply
        {
            public int Number;
            public string Id;        // seen key "note:<Id>"
            public bool Fixed;       // also stands in for the generic "fixed:<Number>" card
            public string Heading;
            public string Body;      // %TITLE% is filled in when the card is built
        }

        /// <summary>The hand-written replies. Keep every string ASCII, with no '{' (the coach's token
        /// pass) and no '&lt;' (TMP tags; the coach's body label has rich text off, but its heading
        /// may not). No version numbers: a reply first appears in the build that carries the fix,
        /// and a player who skipped versions still reads it correctly ("this update"). Add an entry
        /// when a fix ships for a GitHub report, or to answer one; a reply is shown only to the
        /// player holding that issue number's receipt.</summary>
        private static readonly Reply[] Replies =
        {
            new Reply
            {
                Number = 9, Id = "9-rocket-screen", Fixed = true,
                Heading = "Your bug report is fixed",
                Body = "Thanks for reporting UIA-9, \"%TITLE%\". This update fixes it: the HUD now steps "
                     + "behind the rocket UI while it is open - and behind the game's other windows too, "
                     + "like the trader, the comms terminal and plant genetics - so it no longer covers them.",
            },
            new Reply
            {
                Number = 10, Id = "10-helmet-warning", Fixed = true,
                Heading = "Your suggestion is in",
                Body = "Thanks for suggesting UIA-10, \"%TITLE%\". New in this update: the helmet chip "
                     + "can turn red or amber while your visor is open. It starts off - turn it on in "
                     + "F10 > HUD > Show > Helmet-open warning.",
            },
            new Reply
            {
                Number = 11, Id = "11-stow-rebind", Fixed = false,
                Heading = "A reply to your suggestion",
                Body = "Thanks for suggesting UIA-11, \"%TITLE%\". Good news: Smart Stow can already be "
                     + "rebound, mouse buttons included. Open the game's Settings > Controls, find Smart "
                     + "Stow under Inventory, click it and press the button you want. And since this "
                     + "update, the new key also works with a wheel open.",
            },
        };

        private sealed class Notice
        {
            public string Heading;
            public string Body;
            public string[] SeenKeys;
        }

        private enum Phase : byte { WaitWorld, WaitStatus, Show, Done }

        private const float FirstDelaySeconds = 15f;   // after world entry: the Director's Welcome / What's new go first
        private const float SettleSeconds = 4f;        // the "nothing else is up" gate must hold this long
        private const float RefusedBackoffSeconds = 30f;
        private const int MaxRefusals = 3;             // the coach could not build its card: stand down
        private const float StatusTimeoutSeconds = 45f;
        private const int MaxReceipts = 10;            // FeedbackService.StatusMaxReceipts
        private const string SeenFileName = "notices-seen.txt";

        // ---- runtime state: EVERY one is reset in Shutdown ----
        private static Phase _phase;
        private static float _worldSince = -1f;
        private static float _readySince = -1f;
        private static float _statusSince = -1f;
        private static readonly List<Notice> _queue = new List<Notice>();
        private static readonly List<FeedbackService.ReportInfo> _pending = new List<FeedbackService.ReportInfo>();
        private static HashSet<string> _seen;
        private static Notice _showing;
        private static int _failures;
        private static int _refusals;
        private static float _retryAt = -1f;

        private static string SeenPath
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "StationeersUIMod", "Feedback", SeenFileName); }
        }

        /// <summary>Per-frame pump (StationeersUIMod.Update, after the tutorial ticks). Cheap when idle:
        /// a few bool reads. Fail-soft: three throws and it stands down for the session.</summary>
        internal static void Tick(bool radialOpen)
        {
            if (_phase == Phase.Done) return;
            try
            {
                TickInner(radialOpen);
                _failures = 0;
            }
            catch (Exception e)
            {
                if (++_failures >= 3)
                {
                    _phase = Phase.Done;
                    UIALog.Warn("ReportNotices: standing down for this session after repeated errors: " + e.Message);
                }
            }
        }

        private static void TickInner(bool radialOpen)
        {
            float now = Time.unscaledTime;
            if (!Guards.CanDraw())
            {
                _worldSince = -1f;
                _readySince = -1f;
                return;
            }
            if (_worldSince < 0f) _worldSince = now;

            if (_phase == Phase.WaitWorld)
            {
                if (now - _worldSince < 2f) return;   // let the world finish settling before any disk read
                Collect();
                if (_phase == Phase.Done) return;
            }

            if (_phase == Phase.WaitStatus)
            {
                if (!StatusesArrived() && now - _statusSince < StatusTimeoutSeconds) return;
                AddFixedNotices();
                _pending.Clear();
                _phase = _queue.Count > 0 ? Phase.Show : Phase.Done;
                if (_phase == Phase.Done) return;
            }

            // Phase.Show
            if (_showing != null)
            {
                // Our card closed without a callback (CloseCard, or another card replaced it and has
                // since closed): it was not acknowledged, so it stays queued and shows again.
                if (!TutorialCoach.IsOpen) { _showing = null; _readySince = -1f; }
                return;
            }
            if (_queue.Count == 0) { _phase = Phase.Done; return; }

            if (now - _worldSince < FirstDelaySeconds || now < _retryAt || !QuietMoment(radialOpen))
            {
                _readySince = -1f;
                return;
            }
            if (_readySince < 0f) _readySince = now;
            if (now - _readySince < SettleSeconds) return;
            ShowHead();
        }

        /// <summary>True when the player is just playing: in the world, nothing of ours or vanilla's
        /// on screen, no lesson / tip / card, no radial, no editor.</summary>
        private static bool QuietMoment(bool radialOpen)
        {
            if (radialOpen) return false;
            if (!Guards.CanAcceptGameplayInput()) return false;
            if (Guards.VanillaMenuWantsFront()) return false;
            if (UiaControlCenter.IsOpen || HandbookViewer.IsOpen || TutorialCoach.IsOpen) return false;
            if (UI.Grid.TheGridPanel.IsOpen) return false;   // open with a locked cursor passes the input gate
            if (Windows.HudEditorMode.Active || Windows.RadialEditorMode.Active) return false;
            if (TutorialDirector.ActiveLessonId != null || TutorialDirector.ActiveStepId != null) return false;
            return true;
        }

        // ------------------------------------------------------------------ collect

        /// <summary>Read the player's receipts once: queue every unseen compiled reply, and ask the
        /// relay about the rest (quietly) for the generic fixed-in card.</summary>
        private static void Collect()
        {
            _seen = LoadSeen();
            int total;
            var reports = FeedbackService.ListReports(MaxReceipts, out total);
            var numbers = new List<int>();
            for (int i = 0; i < reports.Count; i++)
            {
                var r = reports[i];
                if (r == null || r.Waiting || r.Number <= 0) continue;
                var reply = FindReply(r.Number);
                if (reply != null)
                {
                    if (!_seen.Contains("note:" + reply.Id)) _queue.Add(FromReply(reply, r));
                    if (reply.Fixed) continue;   // its card covers "fixed"; no status needed
                }
                if (_seen.Contains("fixed:" + r.Number.ToString(CultureInfo.InvariantCulture))) continue;
                _pending.Add(r);
                numbers.Add(r.Number);
            }

            if (numbers.Count > 0 && FeedbackService.FeedbackEnabled && FeedbackService.RelayUsable)
            {
                // False = the F10 tab's own check is already running: its answers land in the same
                // cache, so just wait for them.
                FeedbackService.RequestStatusQuiet(numbers);
                _statusSince = Time.unscaledTime;
                _phase = Phase.WaitStatus;
                return;
            }
            _pending.Clear();
            _phase = _queue.Count > 0 ? Phase.Show : Phase.Done;
        }

        private static bool StatusesArrived()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                bool checking;
                string s = FeedbackService.CachedStatus(_pending[i].Number, out checking);
                if (s == null || checking) return false;
            }
            return true;
        }

        private static void AddFixedNotices()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var r = _pending[i];
                bool checking;
                string s = FeedbackService.CachedStatus(r.Number, out checking);
                const string Prefix = "Fixed in ";
                if (s == null || !s.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                string ver = s.Substring(Prefix.Length).Trim();
                if (!ShippedIn(ver)) continue;   // labelled ahead of the release this player runs

                string n = r.Number.ToString(CultureInfo.InvariantCulture);
                bool suggestion = string.Equals(r.Kind, "suggestion", StringComparison.OrdinalIgnoreCase);
                _queue.Add(new Notice
                {
                    Heading = suggestion ? "Your suggestion is in" : "Your bug report is fixed",
                    Body = (suggestion ? "Thanks for suggesting UIA-" : "Thanks for reporting UIA-") + n
                         + ", \"" + SafeTitle(r.Title) + "\". "
                         + (suggestion ? "It was added in " : "It was fixed in ") + Clean(ver)
                         + ". See the change notes on the Workshop page for the details.",
                    SeenKeys = new[] { "fixed:" + n },
                });
            }
        }

        /// <summary>Is <paramref name="ver"/> this build or older? Unparseable = assume shipped.</summary>
        private static bool ShippedIn(string ver)
        {
            try
            {
                var running = new Version(global::StationeersUIMod.StationeersUIMod.ModVersion);
                Version fixedIn;
                if (!TryVersion(ver, out fixedIn)) return true;
                return running >= fixedIn;
            }
            catch { return true; }
        }

        private static bool TryVersion(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrEmpty(s)) return false;
            int end = 0;   // "1.0.2-beta" -> "1.0.2"
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
            try { v = new Version(s.Substring(0, end)); return true; }
            catch { return false; }
        }

        private static Reply FindReply(int number)
        {
            for (int i = 0; i < Replies.Length; i++)
                if (Replies[i].Number == number) return Replies[i];
            return null;
        }

        private static Notice FromReply(Reply reply, FeedbackService.ReportInfo r)
        {
            string body = reply.Body.Replace("%TITLE%", SafeTitle(r.Title));
            var keys = reply.Fixed
                ? new[] { "note:" + reply.Id, "fixed:" + reply.Number.ToString(CultureInfo.InvariantCulture) }
                : new[] { "note:" + reply.Id };
            return new Notice { Heading = reply.Heading, Body = body, SeenKeys = keys };
        }

        // ------------------------------------------------------------------ show

        private static void ShowHead()
        {
            if (_queue.Count == 0) return;
            var n = _queue[0];
            var spec = new TCardSpec
            {
                StepId = null,          // not a lesson: no dev edit field, no lesson-editor jump
                HeadingKey = "",
                BodyKey = "",
                Heading = n.Heading,
                Body = n.Body,
                Buttons = new[] { "OK" },
                PrimaryIndex = 0,
                // The card takes the input either way; in single-player let the world wait too (the
                // coach holds the shared pause only in SP, and only while TutorialAutoPause is on).
                Pause = true,
                OnButton = i => Acknowledge(n, true),
                OnLater = () => Acknowledge(n, false),
            };
            _showing = n;
            TutorialCoach.OpenCard(spec);
            if (TutorialCoach.IsOpen) { _refusals = 0; return; }

            // The coach refused (its build failed - it has already logged why and torn down). Back
            // off instead of retrying every frame, and stand down after a few refusals.
            _showing = null;
            _readySince = -1f;
            _retryAt = Time.unscaledTime + RefusedBackoffSeconds;
            if (++_refusals >= MaxRefusals)
            {
                _phase = Phase.Done;
                UIALog.Warn("ReportNotices: the card could not be shown; giving up for this session.");
            }
        }

        /// <summary>The player closed the card: record it, then swap in the next one (OK - same card,
        /// in place) or wait for the next quiet moment (X / Esc - the card is already gone). A close
        /// the GAME caused (the coach's system close when the world stops drawing: leaving the world,
        /// a disconnect, a respawn) is not the player reading it - the card stays queued.</summary>
        private static void Acknowledge(Notice n, bool viaButton)
        {
            try
            {
                if (_showing != n) return;
                _showing = null;
                if (!viaButton && !Guards.CanDraw()) { _readySince = -1f; return; }
                _queue.Remove(n);
                MarkSeen(n.SeenKeys);
                if (_queue.Count == 0) { _phase = Phase.Done; return; }
                if (viaButton) ShowHead();
                else _readySince = -1f;
            }
            catch (Exception e) { UIALog.Warn("ReportNotices: closing a card failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ seen file

        private static HashSet<string> LoadSeen()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string p = SeenPath;
                if (!File.Exists(p)) return set;
                foreach (var line in File.ReadAllLines(p, new UTF8Encoding(false)))
                {
                    string t = (line ?? "").Trim();
                    if (t.Length > 0) set.Add(t);
                }
            }
            catch (Exception e) { UIALog.Warn("ReportNotices: could not read " + SeenFileName + ": " + e.Message); }
            return set;
        }

        private static void MarkSeen(string[] keys)
        {
            if (keys == null || keys.Length == 0) return;
            if (_seen == null) _seen = LoadSeen();
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Length; i++)
                if (_seen.Add(keys[i])) sb.Append(keys[i]).Append('\n');
            if (sb.Length == 0) return;
            try
            {
                string p = SeenPath;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.AppendAllText(p, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { UIALog.Warn("ReportNotices: could not write " + SeenFileName + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ text

        /// <summary>The player's own report title, safe for the card: printable ASCII, no TMP tags,
        /// no tutorial tokens, at most 60 characters.</summary>
        private static string SafeTitle(string s)
        {
            s = Clean(s);
            if (s.Length > 60) s = s.Substring(0, 57).TrimEnd() + "...";
            return s.Length > 0 ? s : "your report";
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<' || c == '{') sb.Append('(');
                else if (c == '>' || c == '}') sb.Append(')');
                else if (c == '\r' || c == '\n' || c == '\t') sb.Append(' ');
                else sb.Append(c >= ' ' && c <= '~' ? c : '?');
            }
            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Hot reload / quit: reset every static. Does not close a card (the coach's own
        /// Shutdown tears its canvas down).</summary>
        internal static void Shutdown()
        {
            _phase = Phase.WaitWorld;
            _worldSince = -1f;
            _readySince = -1f;
            _statusSince = -1f;
            _queue.Clear();
            _pending.Clear();
            _seen = null;
            _showing = null;
            _failures = 0;
            _refusals = 0;
            _retryAt = -1f;
        }
    }
}
