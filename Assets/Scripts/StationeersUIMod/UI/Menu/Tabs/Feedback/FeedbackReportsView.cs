using System;
using System.Collections.Generic;
using System.Globalization;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Feedback
{
    /// <summary>
    /// The right column of the Suggestions/Bugs tab: "Your reports" (plan §4). One row per report
    /// this PC holds - everything still in the outbox ("Waiting to send"), then the newest sent
    /// receipts with their live status mapped from the relay's labels by FeedbackService
    /// (Received / Under review / Fixed in &lt;ver&gt; / Closed; "--" when unknown - never guessed).
    ///
    /// <para>No background polling: statuses are asked for when the tab opens (throttled) and on
    /// Refresh (<see cref="FeedbackTab.RefreshNow"/>). The rows repaint from local data only - the
    /// disk listing (head-reads, cheap) and FeedbackService's status cache - whenever
    /// <see cref="FeedbackService.ChangeStamp"/> moves, which is one int compare per frame. The
    /// list rebuilds in its own container, so an answer landing while the player types in the form
    /// never steals their focus.</para>
    /// </summary>
    internal sealed class FeedbackReportsView : MonoBehaviour
    {
        private FeedbackTabState _st;
        private RectTransform _list;
        private TextMeshProUGUI _info;
        private UiaControls.UiaButton _refresh;
        private GameObject _sendWaiting;
        private int _seenStamp = int.MinValue;
        private bool _seenRunning;

        internal static FeedbackReportsView Build(Transform column, FeedbackTabState st, string tabTitle)
        {
            var sec = UiaComposite.Section(column, "feedback.reports", "Your reports",
                "Every report sent from this PC, newest first, with where it is on the developers' tracker. " +
                "Received = it arrived. Under review = a fix plan has been written for it. Fixed in (a version) = " +
                "the fix ships in that version. Closed = done without a fix. Status is checked when you open " +
                "this tab or press Refresh - never in the background.");
            var view = sec.Root.gameObject.AddComponent<FeedbackReportsView>();
            view._st = st;

            // Toolbar: last-checked line + Refresh + (when anything waits) Send waiting now.
            var bar = UiaUi.Go("toolbar", sec.Body);
            UiaUi.Size(bar, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)bar.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft, false);
            view._info = UiaUi.Text(bar.transform, "", UiaTheme.SmallSize, UiaTheme.TextMute, TextAlignmentOptions.Left);
            UiaControls.FitText(view._info, 9f);
            var ile = view._info.gameObject.AddComponent<LayoutElement>();
            ile.flexibleWidth = 1f;
            ile.minWidth = 0f;
            ile.preferredWidth = 0f;
            ile.minHeight = UiaTheme.RowH;
            ile.preferredHeight = UiaTheme.RowH;
            view._sendWaiting = UiaControls.Button(bar.transform, "Send waiting now", view.OnSendWaiting,
                150f, 28f).gameObject;
            view._refresh = UiaControls.Button(bar.transform, "Refresh", view.OnRefresh, 96f, 28f);
            UiaSearch.RegisterRow(tabTitle, "feedback.reports", "Your reports (feedback status)",
                UiaSearch.MakeJump(sec.Root.gameObject));

            var listGo = UiaUi.Go("report-list", sec.Body);
            view._list = (RectTransform)listGo.transform;
            UiaUi.VLayout(view._list, 6f);

            view.Repaint(UiaControlCenter.IsRestyling);
            return view;
        }

        // ================================================================== gestures

        private void OnRefresh()
        {
            FeedbackTab.RefreshNow(_st);
            Repaint(false);
        }

        private void OnSendWaiting()
        {
            // `uiafeedback retry` semantics (every counter back to zero, oldest first, one at a
            // time) - minus the open draft's saved copy once its text was edited (its own Send
            // owns the new version; see FeedbackTabState.SweepExclusion).
            FeedbackService.RetryAllNow(_st != null ? _st.SweepExclusion() : null);
            Repaint(false);
        }

        // ================================================================== the per-frame poll

        private void Update()
        {
            if (_st == null) return;
            int stamp = FeedbackService.ChangeStamp;
            bool running = FeedbackService.StatusCheckRunning;
            if (stamp == _seenStamp && running == _seenRunning) return;
            Repaint(false);
        }

        // ================================================================== paint

        private void Repaint(bool useCache)
        {
            int stampNow = FeedbackService.ChangeStamp;
            _seenStamp = stampNow;
            bool running = FeedbackService.StatusCheckRunning;
            _seenRunning = running;
            if (_st == null || _list == null) return;

            // A check we asked for has finished (possibly while this page was closed): stamp it.
            if (_st.CheckRequested && !running)
            {
                _st.CheckRequested = false;
                _st.HasCheckedAt = true;
                _st.CheckedAtLocal = DateTime.Now;
            }

            // Restyle repaints reuse the last listing (see FeedbackTab.Build); every other repaint
            // re-reads the folders (a few head-reads - cheap).
            if (!useCache || _st.Reports == null)
            {
                int total;
                _st.Reports = FeedbackService.ListReports(FeedbackTab.MaxSentShown, out total);
                _st.TotalSent = total;
                _st.ReportsStamp = stampNow;
            }
            // Painting from the cache: remember the stamp the LISTING was read at, so a change that
            // landed between that read and this restyle still triggers a fresh read next frame.
            else _seenStamp = _st.ReportsStamp;
            var reports = _st.Reports;

            // ---- toolbar ----
            if (_info != null)
            {
                if (running) _info.text = "Checking with the developers' tracker...";
                else if (!FeedbackService.RelayUsable) _info.text = "Status unknown - the feedback server address is not valid.";
                else if (_st.HasCheckedAt)
                    _info.text = "Last checked at " + _st.CheckedAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture) + ".";
                else _info.text = "";
            }
            if (_refresh != null) _refresh.SetEnabled(!running && FeedbackService.RelayUsable);
            string excluded = _st.SweepExclusion();
            int sendable = 0;
            for (int i = 0; i < reports.Count; i++)
                if (reports[i].Waiting && !reports[i].Sending
                    && !string.Equals(reports[i].Path, excluded, StringComparison.OrdinalIgnoreCase)) sendable++;
            bool showSend = sendable > 0 && FeedbackService.RelayUsable;
            if (_sendWaiting != null && _sendWaiting.activeSelf != showSend) _sendWaiting.SetActive(showSend);

            // ---- rows ----
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                var c = _list.GetChild(i);
                c.gameObject.SetActive(false);
                Destroy(c.gameObject);
            }

            if (reports.Count == 0)
            {
                UiaControls.Note(_list,
                    "Nothing sent yet. Reports you send here - or with the uiafeedback console command - show up in this list.");
                return;
            }

            bool anyUnknown = false, anyPaused = false;
            int sentShown = 0;
            for (int i = 0; i < reports.Count; i++)
            {
                var r = reports[i];
                if (!r.Waiting) sentShown++;
                Color col;
                string status = StatusText(r, out col);
                if (status == "--") anyUnknown = true;
                if (r.Waiting && r.RetriesUsedUp && !r.Sending) anyPaused = true;
                Row(r, status, col);
            }

            if (_st.TotalSent > sentShown)
                UiaControls.Note(_list, "Showing your " + sentShown + " newest sent reports (of " + _st.TotalSent + ").");
            if (anyPaused)
                UiaControls.Note(_list, "Paused = its automatic retries are used up. Send waiting now tries again.");
            if (anyUnknown)
                UiaControls.Note(_list, "-- = unknown right now (the feedback server could not be reached, or it does " +
                    "not know that number). Nothing is guessed.");
        }

        private bool IsDraft(FeedbackService.ReportInfo r)
        {
            return _st != null && _st.PendingPath != null
                && string.Equals(r.Path, _st.PendingPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The player-facing status for one row, with its colour. Never guesses: a sent
        /// report that has not been asked about (or could not be) reads "--".</summary>
        private string StatusText(FeedbackService.ReportInfo r, out Color col)
        {
            if (r.Waiting)
            {
                if (r.Sending) { col = UiaTheme.TextDim; return "Sending..."; }
                col = UiaTheme.Warn;
                if (IsDraft(r)) return "Waiting to send (in the form)";
                return r.RetriesUsedUp ? "Waiting to send (paused)" : "Waiting to send";
            }
            if (r.Number <= 0) { col = UiaTheme.TextDim; return "Delivered"; }
            bool checking;
            string s = FeedbackService.CachedStatus(r.Number, out checking);
            if (s == null)
            {
                col = UiaTheme.TextMute;
                return checking ? "Checking..." : "--";
            }
            if (s.StartsWith("Fixed in", StringComparison.Ordinal)) col = UiaTheme.Good;
            else if (s == "Under review") col = UiaTheme.Accent;
            else if (s == "Received" || s == "Open") col = UiaTheme.TextDim;
            else col = UiaTheme.TextMute;   // Closed, --
            return FeedbackFormView.Printable(s);
        }

        private void Row(FeedbackService.ReportInfo r, string status, Color statusCol)
        {
            var row = UiaUi.Image(_list, UiaTheme.PanelRaised, "report");
            UiaUi.VLayout((RectTransform)row.transform, 2f, 10, 10, 6, 7);

            var top = UiaUi.Go("top", row.transform);
            UiaUi.Size(top, 20f);
            UiaUi.HLayout((RectTransform)top.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft, false);

            string id = r.Number > 0 ? "UIA-" + r.Number.ToString(CultureInfo.InvariantCulture) : "UIA-?";
            var idT = UiaUi.Text(top.transform, id, UiaTheme.SmallSize, r.Number > 0 ? UiaTheme.Accent : UiaTheme.TextMute,
                TextAlignmentOptions.Left);
            idT.fontStyle = FontStyles.Bold;
            UiaUi.Size(idT.gameObject, 20f, 78f, flexW: 0f);

            string kind = KindWord(r.Kind);
            if (r.HasWhen) kind += "  " + r.WhenUtc.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);
            var kindT = UiaUi.Text(top.transform, kind, UiaTheme.SmallSize - 1f, UiaTheme.TextMute, TextAlignmentOptions.Left);
            kindT.richText = false;
            UiaControls.FitText(kindT, 8.5f, false);
            var kle = UiaUi.Size(kindT.gameObject, 20f, flexW: 1f);
            kle.minWidth = 0f;
            kle.preferredWidth = 0f;

            var st = UiaUi.Text(top.transform, status, UiaTheme.SmallSize, statusCol, TextAlignmentOptions.Right);
            st.richText = false;   // "Fixed in <label text>" comes from a GitHub label
            UiaControls.FitText(st, 8.5f);
            UiaUi.Size(st.gameObject, 20f, 176f, flexW: 0f);

            string title = FeedbackFormView.Printable(r.Title);
            var titleT = UiaUi.Text(row.transform, title.Length > 0 ? title : "(no title)", UiaTheme.SmallSize,
                title.Length > 0 ? UiaTheme.Text : UiaTheme.TextMute, TextAlignmentOptions.TopLeft, true);
            titleT.richText = false;   // a player-typed title is data, never TMP markup
            titleT.margin = new Vector4(0f, 1f, 0f, 1f);
            titleT.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
        }

        private static string KindWord(string kind)
        {
            if (string.Equals(kind, "bug", StringComparison.OrdinalIgnoreCase)) return "Bug";
            if (string.Equals(kind, "suggestion", StringComparison.OrdinalIgnoreCase)) return "Suggestion";
            return string.IsNullOrEmpty(kind) ? "?" : FeedbackFormView.Printable(kind);
        }
    }
}
