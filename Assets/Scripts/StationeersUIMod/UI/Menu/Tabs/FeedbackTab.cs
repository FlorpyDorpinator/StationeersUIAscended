using System;
using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using StationeersUIMod.UI.Menu.Tabs.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>
    /// The "Suggestions/Bugs" tab (Documentation/0.9.8.0/Feedback-Pipeline-Plan.md §1.2-§1.4, §4):
    /// the in-game front end of the live feedback pipeline. A player picks Bug or Suggestion,
    /// types a title + description (+ an optional contact), optionally attaches their HUD profile
    /// and/or the mod's own recent log lines, and presses Send - the report goes to the relay,
    /// becomes a GitHub issue ("UIA-104"), and a Claude bot plans it. Beside the form, "Your
    /// reports" lists what this PC has sent (and anything still waiting) with its live status.
    /// Modeled on the sister app's FeedbackForm (kind tiles, the always-visible "what gets sent" line,
    /// Send disabled until filled, typed text kept on failure, a success panel that REPLACES the
    /// form with a Done button).
    ///
    /// <para>Everything behind it is <see cref="FeedbackService"/> - the same outbox + one-at-a-
    /// time pump the `uiafeedback` console command uses. MUTATES NO GAME STATE: network I/O plus
    /// the mod's own config/StationeersUIMod/Feedback folder only.</para>
    ///
    /// <para>HOT-RELOAD SAFETY: this tab owns NO statics. The draft + send state live in a
    /// <see cref="UiaPageState"/> object (survives Refresh AND Restyle, dropped by the shell's one
    /// UiaPageState.Reset in UiaControlCenter.Shutdown); every live widget reference lives on the
    /// two page MonoBehaviours (<see cref="FeedbackFormView"/>, <see cref="FeedbackReportsView"/>),
    /// which die with the page. Completion is POLLED from FeedbackService (ChangeStamp + per-path
    /// results), so the service never calls into a page that may already be destroyed.</para>
    ///
    /// <para>Standing directive #8: a Control Center surface, not a HUD element - nothing per-tier,
    /// nothing that travels with a theme; the two config keys it reads (Feedback.Enabled /
    /// Feedback.RelayUrl) already exist and are infrastructure (plan §1.1, §1.6).</para>
    /// </summary>
    public sealed class FeedbackTab : IUiaTab
    {
        public string Title => "Suggestions/Bugs";

        /// <summary>At most this many SENT receipts are listed / status-checked (newest first) -
        /// the console `uiafeedback status` cap.</summary>
        internal const int MaxSentShown = 10;

        /// <summary>An automatic (tab-open) refresh is skipped when the last one ran less than this
        /// long ago - flipping between tabs must not re-query the relay every time. The Refresh
        /// button ignores the throttle.</summary>
        private const float AutoRefreshSeconds = 60f;

        public void Build(RectTransform content, bool advanced)
        {
            var st = UiaPageState.Get<FeedbackTabState>(FeedbackTabState.Key);

            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            // Kill switch (Feedback > Enabled = false): a plain notice and nothing else.
            if (!FeedbackService.SettingsLoaded || !FeedbackService.FeedbackEnabled)
            {
                BuildOffNotice(col);
                return;
            }

            // A theme Restyle rebuilds this page up to ~7x/s during an F9 colour drag; none of the
            // disk/hash work below changes because a colour moved (the ProfilesTab/StowShared
            // IsRestyling idiom). Real builds (tab open, Refresh, Done) always read fresh.
            bool restyle = UiaControlCenter.IsRestyling;
            if (!restyle || st.Context == null) st.Context = FeedbackService.PreviewContext();
            if (!restyle && Time.unscaledTime - st.LastAutoRefresh >= AutoRefreshSeconds)
                RefreshNow(st);

            // Two columns: the form (wider) and Your reports. Each column's PREFERRED width is
            // pinned to 0 so the flexible weights alone split the row - a wrapped TMP note would
            // otherwise report its whole unwrapped line as preferred width and skew the split.
            var rowGo = UiaUi.Go("feedback-columns", col);
            UiaUi.HLayout((RectTransform)rowGo.transform, 12f, 0, 0, 0, 0, TextAnchor.UpperLeft, false);
            var left = Column(rowGo.transform, "form-col", 3f);
            var right = Column(rowGo.transform, "reports-col", 2f);

            FeedbackFormView.Build(left, st, Title);
            FeedbackReportsView.Build(right, st, Title);
        }

        /// <summary>The whole "Refresh" gesture (tab open + the Refresh button): send what is waiting
        /// (quietly - the startup sweep's rules; the open draft only while its text is unchanged, see
        /// <see cref="FeedbackTabState.SweepExclusion"/>), then ask the relay for the newest receipts'
        /// status. Never blocks: both only QUEUE work on FeedbackService's one-at-a-time pump.</summary>
        internal static void RefreshNow(FeedbackTabState st)
        {
            if (st == null) return;
            st.LastAutoRefresh = Time.unscaledTime;
            if (!FeedbackService.FeedbackEnabled) return;
            FeedbackService.SweepNow(st.SweepExclusion());
            int total;
            var reports = FeedbackService.ListReports(MaxSentShown, out total);
            var numbers = new List<int>();
            for (int i = 0; i < reports.Count; i++)
                if (!reports[i].Waiting && reports[i].Number > 0) numbers.Add(reports[i].Number);
            if (numbers.Count > 0 && FeedbackService.RequestStatusQuiet(numbers))
                st.CheckRequested = true;
        }

        private static RectTransform Column(Transform parent, string name, float weight)
        {
            var go = UiaUi.Go(name, parent);
            var rt = (RectTransform)go.transform;
            UiaUi.VLayout(rt, UiaTheme.Gap);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = weight;
            return rt;
        }

        private void BuildOffNotice(Transform col)
        {
            var sec = UiaComposite.Section(col, "feedback.off", Title);
            if (!FeedbackService.SettingsLoaded)
            {
                UiaControls.Note(sec.Body,
                    "Feedback settings are not loaded yet - this tab works once UI Ascended has finished starting up.");
                return;
            }
            UiaControls.Note(sec.Body,
                "Feedback is turned off in the config (section [Feedback], Enabled = false, in UI Ascended's " +
                ".cfg file under BepInEx/config). Set it back to true to send bug reports and suggestions from here.");
        }
    }
}

namespace StationeersUIMod.UI.Menu.Tabs.Feedback
{
    /// <summary>Everything the Suggestions/Bugs tab remembers, held in <see cref="UiaPageState"/>
    /// under <see cref="Key"/> so it survives the shell's Refresh AND Restyle rebuilds (a half-typed
    /// report must never vanish because a theme colour moved or the player looked at another tab),
    /// and is dropped by the shell's single UiaPageState.Reset on teardown. Plain data only - no
    /// Unity objects.</summary>
    internal sealed class FeedbackTabState
    {
        internal const string Key = "feedback.tab";

        // ---- the draft ----
        /// <summary>null = not picked yet (Send stays disabled), "bug" or "suggestion" (wire values).</summary>
        public string Kind;
        public string Title = "";
        public string Description = "";
        public string Contact = "";
        public bool IncludeProfile;
        public bool IncludeLog;
        /// <summary>The player flipped "Include my HUD profile" by hand: picking a kind stops
        /// re-defaulting it (Bug = on, Suggestion = off).</summary>
        public bool ProfileTouched;

        // ---- the send ----
        /// <summary>This draft's saved outbox file - in flight, or failed and waiting. Null = the
        /// draft has not been saved yet.</summary>
        public string PendingPath;
        /// <summary>The draft as it was when <see cref="PendingPath"/> was saved: unchanged = Send
        /// re-sends that SAME file ("Try again"), changed = the new version replaces it.</summary>
        public string PendingSig;
        /// <summary>A send of PendingPath was seen queued/in flight; resolve it once it leaves the queue.</summary>
        public bool AwaitingResult;
        /// <summary>Unscaled time the current send started (-1 = none): drives the stuck-send notice.</summary>
        public float BusySince = -1f;
        public string StatusMsg;
        public UiaComposite.StatusKind StatusKind;
        /// <summary>Player-facing notes from the last save (an attachment that could not be read, ...).</summary>
        public List<string> Notes = new List<string>();

        // ---- the success panel ----
        public bool Sent;
        public int SentNumber;
        public bool SentDuplicate;

        // ---- Your reports ----
        public float LastAutoRefresh = -1000f;
        /// <summary>A status check was requested and has not been seen finishing yet.</summary>
        public bool CheckRequested;
        public bool HasCheckedAt;
        public DateTime CheckedAtLocal;

        // ---- restyle caches ----
        public List<KeyValuePair<string, string>> Context;
        public List<FeedbackService.ReportInfo> Reports;
        public int TotalSent;
        /// <summary>FeedbackService.ChangeStamp when <see cref="Reports"/> was read.</summary>
        public int ReportsStamp = int.MinValue;

        /// <summary>The draft as it would be sent right now (trimmed), for "is this still the text
        /// that was saved to PendingPath?".</summary>
        public string Signature()
        {
            var sb = new System.Text.StringBuilder(64);
            sb.Append(Kind ?? "").Append('\u0001')
              .Append((Title ?? "").Trim()).Append('\u0001')
              .Append((Description ?? "").Trim()).Append('\u0001')
              .Append((Contact ?? "").Trim()).Append('\u0001')
              .Append(IncludeProfile ? '1' : '0').Append(IncludeLog ? '1' : '0');
            return sb.ToString();
        }

        /// <summary>The outbox file a sweep must leave alone: the draft's saved copy once the player
        /// has EDITED the form since saving it (a sweep would file the old text while the new text
        /// waits for its own Send). Unchanged, it is just another waiting report - sweeps may carry
        /// it, which is what makes "sent automatically later" true within the same session.</summary>
        public string SweepExclusion()
        {
            if (PendingPath == null) return null;
            return string.Equals(Signature(), PendingSig, StringComparison.Ordinal) ? null : PendingPath;
        }
    }
}
