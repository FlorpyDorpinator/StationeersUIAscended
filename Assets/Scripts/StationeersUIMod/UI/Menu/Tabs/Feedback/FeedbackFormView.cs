using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Feedback
{
    /// <summary>
    /// The left column of the Suggestions/Bugs tab: the report form (kind tiles, title,
    /// description, contact, the two opt-ins, the always-visible "what gets sent" block, Send) or -
    /// once a report landed - the success panel that REPLACES it (drawn check mark, "Thanks - that's
    /// now UIA-104.", Done). Lives on the page, so it dies with it; every value it shows is read
    /// back from the <see cref="FeedbackTabState"/> page state.
    ///
    /// <para>SEND FLOW (never lost, never doubled): Send saves the report to the outbox FIRST
    /// (FeedbackService.SubmitFromUi - the console command's exact write) and queues it at the
    /// front of the pump; the fields lock while it is in flight. On failure the typed text stays and
    /// the saved file is remembered as the draft's <c>PendingPath</c>: pressing Send again with the
    /// text unchanged re-sends THAT file ("Try again"), and an edited draft is saved as a new file
    /// that then replaces the unsent copy - so a retry can never file a second issue.</para>
    ///
    /// <para>Completion is POLLED in <see cref="Update"/>: the draft's file leaving the queue plus
    /// its recorded <see cref="FeedbackService.SendResult"/> - which equally resolves a send that
    /// something else carried (the tab-open sweep or Send waiting now, which may take the draft's
    /// saved copy while its text is UNCHANGED; `uiafeedback retry`).</para>
    ///
    /// <para>ESC KEEPS THE DRAFT (a deliberate, local deviation from the kit input's "Esc = forget
    /// what I typed"): the sister app's rule - "Escape or backdrop: the draft survives, so nobody loses a
    /// paragraph they just typed" - fits a 4000-character report far better than a settings name.
    /// The kit field reverts silently on Esc; Update writes the draft back while unfocused.</para>
    /// </summary>
    internal sealed class FeedbackFormView : MonoBehaviour
    {
        private const string KindBug = "bug";
        private const string KindSuggestion = "suggestion";
        private const int TitleMax = 120;
        private const int DescriptionMax = 4000;
        private const float StuckSeconds = 180f;

        private FeedbackTabState _st;

        // The form's live widgets (null while the success panel is showing).
        private KindTile _bugTile, _ideaTile;
        private UiaTextInput _title, _desc, _contact;
        private TextMeshProUGUI _descLabel, _counter, _sendLabel, _sendHint;
        private UiaControls.UiaButton _send;
        private UiaControls.UiaToggle _profileToggle, _logToggle;
        private UiaComposite.InlineStatusHandle _status;
        private GameObject _stuckRow;

        private bool _paintedBusy;
        private bool _paintedStuck;
        private int _seenStamp = int.MinValue;

        private sealed class KindTile
        {
            public UiaControls.UiaButton Button;
            public TextMeshProUGUI Name, Hint;
            public UiaGlassSkin Skin;
        }

        // ================================================================== build

        internal static FeedbackFormView Build(Transform column, FeedbackTabState st, string tabTitle)
        {
            var sec = UiaComposite.Section(column, "feedback.form",
                st.Sent ? "Sent" : "Send a bug report or suggestion",
                "Your report goes to the UI Ascended feedback server (" + SafeHost() + ") and becomes a " +
                "tracked issue the developers read and plan against. Nothing leaves your PC until you " +
                "press Send. A report that cannot be sent right away is kept on your PC and sent " +
                "automatically later - it is never lost.");
            var view = sec.Root.gameObject.AddComponent<FeedbackFormView>();
            view._st = st;
            if (st.Sent) view.BuildSuccess(sec.Body);
            else view.BuildForm(sec.Body, tabTitle);
            return view;
        }

        private void BuildForm(RectTransform body, string tabTitle)
        {
            UiaControls.Note(body,
                "Found a bug, or have an idea? Send it straight to the UI Ascended developers - every report " +
                "becomes a tracked issue they read. You stay anonymous unless you add a contact.");
            if (!FeedbackService.RelayUsable)
            {
                var warn = UiaControls.Note(body,
                    "The Feedback > RelayUrl override in the config is not a web address, so reports are saved " +
                    "on your PC but cannot be sent until it is fixed or cleared.");
                warn.color = UiaTheme.Warn;
            }

            // ---- kind tiles ----
            var tilesGo = UiaUi.Go("kind-tiles", body);
            UiaUi.Size(tilesGo, 64f);
            UiaUi.HLayout((RectTransform)tilesGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft, false);
            _bugTile = BuildTile(tilesGo.transform, "Bug", "Something is not working", KindBug);
            _ideaTile = BuildTile(tilesGo.transform, "Suggestion", "An idea to make this better", KindSuggestion);
            UiaSearch.RegisterRow(tabTitle, "feedback.form", "Send a bug report or suggestion",
                UiaSearch.MakeJump(tilesGo));

            // ---- title ----
            FieldLabel(body, "Title");
            var topt = new UiaInputs.TextInputOptions();
            topt.Placeholder = TitlePlaceholder(_st.Kind);
            topt.InitialText = _st.Title;
            topt.CharacterLimit = TitleMax;
            topt.OnChanged = v => { if (!Typing(_title)) return; _st.Title = UiaInputs.AsciiFilter(v, false); PaintFooter(); };
            _title = UiaInputs.TextInput(body, topt);
            KeepTextOnEscape(_title);

            // ---- description (multi-line) ----
            _descLabel = FieldLabel(body, DescriptionLabel(_st.Kind));
            var dopt = new UiaInputs.TextInputOptions();
            dopt.Placeholder = DescriptionPlaceholder(_st.Kind);
            dopt.InitialText = _st.Description;
            dopt.CharacterLimit = DescriptionMax;
            dopt.MultiLine = true;
            dopt.Height = 150f;
            dopt.OnChanged = v => { if (!Typing(_desc)) return; _st.Description = UiaInputs.AsciiFilter(v, true); PaintCounter(); PaintFooter(); };
            _desc = UiaInputs.TextInput(body, dopt);
            KeepTextOnEscape(_desc);
            _counter = UiaUi.Text(body, "", UiaTheme.SmallSize - 1f, UiaTheme.TextMute, TextAlignmentOptions.Right);
            UiaUi.Size(_counter.gameObject, 16f);

            // ---- contact ----
            FieldLabel(body, "Contact (optional)");
            var copt = new UiaInputs.TextInputOptions();
            copt.Placeholder = "Discord name, if you want a reply";
            copt.InitialText = _st.Contact;
            copt.CharacterLimit = FeedbackService.ContactCap;
            copt.OnChanged = v => { if (!Typing(_contact)) return; _st.Contact = UiaInputs.AsciiFilter(v, false); PaintFooter(); };
            _contact = UiaInputs.TextInput(body, copt);
            KeepTextOnEscape(_contact);

            // ---- opt-ins (plan §1.4) ----
            _profileToggle = UiaControls.ToggleRow(body, "Include my HUD profile", _st.IncludeProfile, v =>
            {
                if (IsBusy()) { _profileToggle.Set(_st.IncludeProfile, false); return; }
                _st.IncludeProfile = v;
                _st.ProfileTouched = true;
                PaintFooter();
            });
            _logToggle = UiaControls.ToggleRow(body, "Include recent UI Ascended log lines", _st.IncludeLog, v =>
            {
                if (IsBusy()) { _logToggle.Set(_st.IncludeLog, false); return; }
                _st.IncludeLog = v;
                PaintFooter();
            });
            UiaControls.Note(body,
                "Your HUD profile lets the developers reproduce a HUD problem exactly. Log lines are only " +
                "UI Ascended's own lines and errors - never your whole game log.");

            // ---- what gets sent (the privacy disclosure, always visible) ----
            BuildDisclosure(body);

            // ---- footer ----
            var footGo = UiaUi.Go("footer", body);
            UiaUi.Size(footGo, 36f);
            UiaUi.HLayout((RectTransform)footGo.transform, UiaTheme.Gap, 0, 0, 2, 2, TextAnchor.MiddleLeft, false);
            _sendHint = UiaUi.Text(footGo.transform, "", UiaTheme.SmallSize, UiaTheme.TextMute, TextAlignmentOptions.Left);
            UiaControls.FitText(_sendHint, 9f);
            var hle = _sendHint.gameObject.AddComponent<LayoutElement>();
            hle.flexibleWidth = 1f;
            hle.minWidth = 0f;
            hle.preferredWidth = 0f;
            hle.minHeight = 32f;
            hle.preferredHeight = 32f;
            _send = UiaControls.Button(footGo.transform, "Send", OnSend, 150f, 32f, UiaControls.ButtonStyle.Primary);
            _sendLabel = _send.GetComponentInChildren<TextMeshProUGUI>();

            _status = UiaComposite.InlineStatus(body, _st.StatusMsg, _st.StatusKind);
            // Relay error text / exception messages reach this line: never parse them as TMP tags.
            if (_status.Text != null) _status.Text.richText = false;
            if (_st.Notes != null)
                for (int i = 0; i < _st.Notes.Count; i++)
                    UiaControls.Note(body, _st.Notes[i]).richText = false;

            // The should-never-happen escape hatch: a send that never comes back (the pump has a
            // watchdog, so only a destroyed host could do this) offers a clean slate - the saved
            // report stays in the outbox either way.
            _stuckRow = UiaUi.Go("stuck", body);
            UiaUi.VLayout((RectTransform)_stuckRow.transform, 4f);
            var sn = UiaControls.Note(_stuckRow.transform,
                "Still waiting on the feedback server. Your report is saved on your PC either way and will be " +
                "sent automatically later.");
            sn.color = UiaTheme.Warn;
            var srow = UiaUi.Go("stuck-row", _stuckRow.transform);
            UiaUi.Size(srow, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)srow.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft, false);
            UiaControls.Button(srow.transform, "Start a new report", ResetDraftAndRebuild, 190f, UiaTheme.RowH);
            _stuckRow.SetActive(false);

            PaintKind();
            PaintCounter();
            _paintedBusy = IsBusy();
            PaintBusy();
        }

        private KindTile BuildTile(Transform parent, string name, string hint, string kind)
        {
            var go = UiaUi.Go("kind-" + kind, parent);
            UiaUi.Size(go, 64f, flexW: 1f);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            var bg = go.AddComponent<Image>();
            UiaImages.Round(bg);
            bg.color = UiaTheme.PanelRaised;
            var t = new KindTile();
            t.Skin = UiaGlassSkin.Add(go);
            UiaUi.VLayout((RectTransform)go.transform, 2f, 14, 14, 10, 10, TextAnchor.MiddleLeft);
            t.Name = UiaUi.Text(go.transform, name, UiaTheme.LabelSize + 2f, UiaTheme.Text, TextAlignmentOptions.Left);
            t.Name.fontStyle = FontStyles.Bold;
            UiaUi.Size(t.Name.gameObject, 24f);
            t.Hint = UiaUi.Text(go.transform, hint, UiaTheme.SmallSize, UiaTheme.TextMute, TextAlignmentOptions.Left);
            UiaControls.FitText(t.Hint, 9f);
            UiaUi.Size(t.Hint.gameObject, 18f);
            t.Button = go.AddComponent<UiaControls.UiaButton>().Init(bg, UiaTheme.PanelRaised, UiaTheme.PanelHover, UiaTheme.AccentDim);
            t.Button.OnClick = () => PickKind(kind);
            return t;
        }

        private static TextMeshProUGUI FieldLabel(Transform parent, string text)
        {
            var t = UiaUi.Text(parent, text, UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.BottomLeft);
            t.margin = new Vector4(2f, 0f, 2f, 0f);
            UiaUi.Size(t.gameObject, 20f);
            return t;
        }

        /// <summary>The always-visible privacy disclosure (plan §1.2.7, §1.3): the auto-context with the
        /// values it would carry right now, what is opt-in, and what is never sent. One compact
        /// paragraph rather than a row per field, so the whole form (Send included) fits the
        /// reference window without scrolling.</summary>
        private void BuildDisclosure(Transform body)
        {
            var card = UiaUi.Image(body, UiaTheme.PanelRaised, "what-gets-sent");
            UiaUi.VLayout((RectTransform)card.transform, 3f, 10, 10, 7, 8);
            var head = UiaUi.Text(card.transform, "WHAT GETS SENT", UiaTheme.SmallSize - 1f, UiaTheme.Accent,
                TextAlignmentOptions.Left);
            head.characterSpacing = 4f;
            UiaUi.Size(head.gameObject, 18f);

            var ctx = _st.Context ?? new List<KeyValuePair<string, string>>();
            string modified = Value(ctx, "profileModified");
            var sb = new StringBuilder(256);
            for (int i = 0; i < ctx.Count; i++)
            {
                string key = ctx[i].Key;
                string label;
                string value = ctx[i].Value;
                if (key == "modVersion") label = "Mod version";
                else if (key == "deployRoute") label = "install";
                else if (key == "gameBuild") label = "game build";
                else if (key == "profileName")
                {
                    label = "HUD profile";
                    string words = EditedWords(modified);
                    value = string.IsNullOrEmpty(value) ? "" : "'" + value + "'" + (words.Length > 0 ? " (" + words + ")" : "");
                }
                else if (key == "playerContext") label = "session";
                else if (key == "display") label = "screen";
                else if (key == "os") label = "OS";
                else continue;   // profileModified is folded into the HUD profile entry
                if (sb.Length > 0) sb.Append(";  ");
                sb.Append(label).Append(": ").Append(string.IsNullOrEmpty(value) ? "unknown" : value);
            }
            sb.Append('.');
            var values = UiaControls.Note(card.transform, Printable(sb.ToString()));
            values.color = UiaTheme.TextDim;
            values.richText = false;
            var note = UiaControls.Note(card.transform,
                "Only if you switch them on above: your HUD profile file, recent UI Ascended log lines. " +
                "Never sent: your Steam ID or your save name. Reports go to " + SafeHost() + ".");
            note.color = UiaTheme.TextMute;
        }

        // ---- the success panel (replaces the form body) ----

        private void BuildSuccess(RectTransform body)
        {
            var wrap = UiaUi.Go("sent", body);
            UiaUi.VLayout((RectTransform)wrap.transform, 8f, 0, 0, 14, 10, TextAnchor.UpperCenter);

            FeedbackCheckMark.Build(wrap.transform, 64f);

            string head;
            string sub;
            if (_st.SentDuplicate)
            {
                head = "Thanks - the developers already have this one.";
                sub = "An identical report reached them within the last hour, so no new number was issued.";
            }
            else if (_st.SentNumber > 0)
            {
                head = "Thanks - that's now UIA-" + _st.SentNumber + ".";
                sub = "The developers have it.";
            }
            else
            {
                head = "Thanks - the developers have it.";
                sub = "The server did not send back a reference number this time.";
            }
            var h = UiaUi.Text(wrap.transform, head, UiaTheme.LabelSize + 4f, UiaTheme.Text, TextAlignmentOptions.Center, true);
            h.gameObject.AddComponent<LayoutElement>().minHeight = 26f;
            var s = UiaUi.Text(wrap.transform, sub, UiaTheme.SmallSize + 1f, UiaTheme.TextDim, TextAlignmentOptions.Center, true);
            s.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
            if (_st.Notes != null)
                for (int i = 0; i < _st.Notes.Count; i++)
                {
                    var n = UiaUi.Text(wrap.transform, _st.Notes[i], UiaTheme.SmallSize, UiaTheme.TextMute, TextAlignmentOptions.Center, true);
                    n.richText = false;
                    n.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
                }
            var f = UiaUi.Text(wrap.transform, "You can follow it under Your reports - its status is checked each time you open this tab.",
                UiaTheme.SmallSize, UiaTheme.TextMute, TextAlignmentOptions.Center, true);
            f.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            var btnRow = UiaUi.Go("done-row", wrap.transform);
            UiaUi.Size(btnRow, 40f);
            UiaUi.HLayout((RectTransform)btnRow.transform, 0f, 0, 0, 4, 4, TextAnchor.MiddleCenter, false);
            UiaControls.Button(btnRow.transform, "Done", ResetDraftAndRebuild, 150f, 32f, UiaControls.ButtonStyle.Primary);
        }

        // ================================================================== gestures

        private void PickKind(string kind)
        {
            if (_st == null || IsBusy()) return;
            _st.Kind = kind;
            // Plan §1.4: the profile opt-in defaults ON for a bug, OFF for a suggestion - until the
            // player sets it by hand.
            if (!_st.ProfileTouched)
            {
                _st.IncludeProfile = kind == KindBug;
                if (_profileToggle != null) _profileToggle.Set(_st.IncludeProfile, false);
            }
            PaintKind();
            PaintFooter();
        }

        private void OnSend()
        {
            if (_st == null || IsBusy() || !CanSend()) return;
            string sig = Signature();

            // 1) The unchanged draft of an earlier, still-unsent save: send THAT file again - a new
            //    save would put two copies in the outbox and, later, two issues on the tracker.
            if (_st.PendingPath != null && File.Exists(_st.PendingPath)
                && string.Equals(sig, _st.PendingSig, StringComparison.Ordinal))
            {
                if (FeedbackService.ResendNow(_st.PendingPath)) BeginAwait();
                else SetStatus(SavedButNotSentText(), UiaComposite.StatusKind.Warn);
                PaintBusy();
                return;
            }

            // 2) A new (or edited) report: save to the outbox FIRST, then queue. An edited draft
            //    REPLACES its earlier unsent copy - replace-or-nothing, inside the service.
            var notes = new List<string>();
            bool queued, replaceBlocked;
            string error;
            bool hadNotes = _st.Notes != null && _st.Notes.Count > 0;
            string path = FeedbackService.SubmitFromUi(_st.Kind, _st.Title, _st.Description, _st.Contact,
                _st.IncludeProfile, _st.IncludeLog, _st.PendingPath, notes, out queued, out replaceBlocked, out error);
            _st.Notes = CleanNotes(notes);
            if (path == null)
            {
                if (replaceBlocked)
                    SetStatus("Your earlier unsent copy of this report could not be replaced (" + Printable(error ?? "")
                        + "), so your edits were not saved yet - they are still here. Press Send again in a moment.",
                        UiaComposite.StatusKind.Warn);
                else
                    SetStatus("Could not save your report on this PC (" + Printable(error ?? "unknown error")
                        + ") - nothing was sent. Your text is still here.", UiaComposite.StatusKind.Error);
                RebuildNotes(hadNotes);
                return;
            }
            _st.PendingPath = path;
            _st.PendingSig = sig;
            if (queued) BeginAwait();
            else SetStatus(SavedButNotSentText(), UiaComposite.StatusKind.Warn);
            PaintBusy();
            RebuildNotes(hadNotes);
        }

        private void BeginAwait()
        {
            _st.AwaitingResult = true;
            _st.BusySince = Time.unscaledTime;
            SetStatus(null, UiaComposite.StatusKind.Info);
        }

        private void ResetDraftAndRebuild()
        {
            if (_st == null) return;
            _st.Kind = null;
            _st.Title = "";
            _st.Description = "";
            _st.Contact = "";
            _st.IncludeProfile = false;
            _st.IncludeLog = false;
            _st.ProfileTouched = false;
            _st.PendingPath = null;
            _st.PendingSig = null;
            _st.AwaitingResult = false;
            _st.BusySince = -1f;
            _st.StatusMsg = null;
            _st.StatusKind = UiaComposite.StatusKind.Info;
            _st.Notes = new List<string>();
            _st.Sent = false;
            _st.SentNumber = 0;
            _st.SentDuplicate = false;
            UiaControlCenter.Refresh();
        }

        // ================================================================== the per-frame poll

        private void Update()
        {
            if (_st == null || _st.Sent) return;

            // Esc keeps the draft (see the class note): an unfocused field that no longer shows the
            // draft was reverted by the kit's cancel - put the draft back.
            KeepDraft(_title, _st.Title);
            KeepDraft(_desc, _st.Description);
            KeepDraft(_contact, _st.Contact);

            bool busy = IsBusy();
            if (busy && !_st.AwaitingResult)
            {
                // Something else picked up the draft's (unchanged) saved copy - the tab-open sweep,
                // Send waiting now, `uiafeedback retry`: lock the form and wait for it too.
                _st.AwaitingResult = true;
                if (_st.BusySince < 0f) _st.BusySince = Time.unscaledTime;
            }
            if (!busy && _st.AwaitingResult)
            {
                _st.AwaitingResult = false;
                _st.BusySince = -1f;
                if (Resolve()) return;   // switched to the success panel (page rebuilt)
            }
            else
            {
                int stamp = FeedbackService.ChangeStamp;
                if (stamp != _seenStamp)
                {
                    _seenStamp = stamp;
                    // Delivered by other means while idle (a manual console retry, say)?
                    if (!busy && _st.PendingPath != null && !File.Exists(_st.PendingPath) && Resolve()) return;
                }
            }

            if (busy != _paintedBusy) { _paintedBusy = busy; PaintBusy(); }
            bool stuck = busy && _st.BusySince >= 0f && Time.unscaledTime - _st.BusySince > StuckSeconds;
            // A send this late can only mean the pump died with its host (it has a watchdog):
            // let the service recover, which records the send as interrupted and frees the form.
            if (stuck) FeedbackService.RecoverIfPumpDead();
            if (stuck != _paintedStuck)
            {
                _paintedStuck = stuck;
                if (_stuckRow != null) _stuckRow.SetActive(stuck);
            }
        }

        /// <summary>The draft's send has left the queue: turn its recorded result into the next
        /// state. Returns true when the page was rebuilt (success panel) - the caller must stop.</summary>
        private bool Resolve()
        {
            string path = _st.PendingPath;
            if (path == null) return false;

            FeedbackService.SendResult r;
            bool have = FeedbackService.TryGetResult(path, out r);
            int number;
            if (have && (r.Outcome == FeedbackService.SendOutcome.Delivered || r.Outcome == FeedbackService.SendOutcome.Duplicate))
                return ShowSent(r.Number, r.Outcome == FeedbackService.SendOutcome.Duplicate);
            if (!File.Exists(path))
            {
                if (FeedbackService.FindReceipt(path, out number)) return ShowSent(number, false);
                // Gone from the outbox without a receipt (removed by hand / uiareset): nothing to
                // retry any more - a fresh Send saves it again from the text still in the form.
                _st.PendingPath = null;
                _st.PendingSig = null;
                SetStatus("Your saved copy of this report is no longer on your PC. Press Send to save and send it again.",
                    UiaComposite.StatusKind.Warn);
                PaintBusy();
                return false;
            }

            if (have && r.Outcome == FeedbackService.SendOutcome.DryRun)
                SetStatus("The feedback server is in test mode right now, so nothing was filed yet. Your report is " +
                    "saved on your PC and will be sent automatically once the server is live.",
                    UiaComposite.StatusKind.Warn);
            else if (have && r.Outcome == FeedbackService.SendOutcome.Failed)
            {
                string reason = r.Reason ?? "";
                string lead = reason.StartsWith("HTTP 429", StringComparison.Ordinal)
                    ? "The feedback server limits how many reports one PC can send per hour, so this one was not delivered yet."
                    : "Not delivered yet" + (reason.Length == 0 ? "" : " (" + Printable(reason) + ")") + ".";
                SetStatus(lead + " Saved on your PC - it will be sent automatically later." + (r.RetriesUsedUp
                        ? " Its automatic retries are used up, so press Try again when you are back online."
                        : " Or press Try again to send it now."),
                    UiaComposite.StatusKind.Warn);
            }
            else
                SetStatus(SavedButNotSentText(), UiaComposite.StatusKind.Warn);
            PaintBusy();
            return false;
        }

        private bool ShowSent(int number, bool duplicate)
        {
            _st.Sent = true;
            _st.SentNumber = number;
            _st.SentDuplicate = duplicate;
            _st.PendingPath = null;
            _st.PendingSig = null;
            _st.AwaitingResult = false;
            _st.BusySince = -1f;
            _st.StatusMsg = null;
            UiaControlCenter.Refresh();   // the success panel REPLACES the form body
            return true;
        }

        // ================================================================== paint

        private bool IsBusy()
        {
            return _st != null && _st.PendingPath != null && FeedbackService.IsQueued(_st.PendingPath);
        }

        private bool CanSend()
        {
            return _st != null && _st.Kind != null
                && (_st.Title ?? "").Trim().Length > 0
                && (_st.Description ?? "").Trim().Length > 0;
        }

        private void PaintKind()
        {
            PaintTile(_bugTile, _st.Kind == KindBug);
            PaintTile(_ideaTile, _st.Kind == KindSuggestion);
            SetPlaceholder(_title, TitlePlaceholder(_st.Kind));
            SetPlaceholder(_desc, DescriptionPlaceholder(_st.Kind));
            if (_descLabel != null) _descLabel.text = DescriptionLabel(_st.Kind);
        }

        private static void PaintTile(KindTile t, bool selected)
        {
            if (t == null) return;
            if (t.Button != null) t.Button.SetSelected(selected);
            if (t.Skin != null) t.Skin.BorderColorOverride = selected ? (Color?)UiaTheme.Accent : null;
            if (t.Name != null) t.Name.color = selected ? UiaTheme.Text : UiaTheme.TextDim;
            if (t.Hint != null) t.Hint.color = selected ? UiaTheme.Text : UiaTheme.TextMute;
        }

        private void PaintCounter()
        {
            if (_counter == null) return;
            int n = (_st.Description ?? "").Length;
            _counter.text = n + " / " + DescriptionMax;
            _counter.color = n >= DescriptionMax - 200 ? UiaTheme.Warn : UiaTheme.TextMute;
        }

        private void PaintBusy()
        {
            bool busy = IsBusy();
            SetInteractable(_title, !busy);
            SetInteractable(_desc, !busy);
            SetInteractable(_contact, !busy);
            PaintFooter();
        }

        private void PaintFooter()
        {
            if (_send == null || _st == null) return;
            bool busy = IsBusy();
            bool can = CanSend();
            bool retry = !busy && _st.PendingPath != null && File.Exists(_st.PendingPath)
                && string.Equals(Signature(), _st.PendingSig, StringComparison.Ordinal);
            if (_sendLabel != null) _sendLabel.text = busy ? "Sending..." : (retry ? "Try again" : "Send");
            _send.SetEnabled(!busy && can);
            if (_sendHint != null) _sendHint.text = busy ? "" : MissingHint();
        }

        private string MissingHint()
        {
            var missing = new List<string>(3);
            if (_st.Kind == null) missing.Add("pick Bug or Suggestion");
            if ((_st.Title ?? "").Trim().Length == 0) missing.Add("add a title");
            if ((_st.Description ?? "").Trim().Length == 0) missing.Add("add a description");
            if (missing.Count == 0) return "";
            string s = missing[0];
            for (int i = 1; i < missing.Count; i++)
                s += (i == missing.Count - 1 ? " and " : ", ") + missing[i];
            return "To send: " + s + ".";
        }

        private void SetStatus(string msg, UiaComposite.StatusKind kind)
        {
            _st.StatusMsg = msg;
            _st.StatusKind = kind;
            if (_status != null)
            {
                if (string.IsNullOrEmpty(msg)) _status.Clear();
                else _status.Show(msg, kind);
            }
        }

        /// <summary>Attachment notes appear under the status line; they only change on a save (the
        /// player just pressed Send - no edit in progress, the fields are locked while it flies), so
        /// a page rebuild is the simplest honest repaint. Skipped when there were and are none.</summary>
        private void RebuildNotes(bool hadNotes)
        {
            if (hadNotes || (_st.Notes != null && _st.Notes.Count > 0)) UiaControlCenter.Refresh();
        }

        // ================================================================== helpers

        private string Signature()
        {
            return _st.Signature();
        }

        private static string SavedButNotSentText()
        {
            if (!FeedbackService.RelayUsable)
                return "Saved on your PC. It cannot be sent yet: the Feedback > RelayUrl setting is not a web address - " +
                    "fix or clear it and the report goes out automatically.";
            return "Saved on your PC - it will be sent automatically later.";
        }

        /// <summary>Esc must not wipe the draft. TMP's own cancel path (decompiled game
        /// Unity.TextMeshPro, TMP_InputField.DeactivateInputField: <c>m_AllowInput = false</c>, then
        /// <c>if (m_WasCanceled &amp;&amp; m_RestoreOriginalTextOnEscape) text = m_OriginalText;</c>) goes
        /// through the <c>text</c> SETTER, which fires onValueChanged - the kit forwards that to
        /// OnChanged BEFORE its own silent revert, so the reverted text would overwrite the draft.
        /// Two guards: TMP's restore is switched off for these three fields, and OnChanged only
        /// accepts values while the field actually owns the keyboard (<see cref="Typing"/>). The
        /// kit's silent revert still runs; <see cref="Update"/> writes the draft back.</summary>
        private static void KeepTextOnEscape(UiaTextInput f)
        {
            if (f != null && f.Field != null) f.Field.restoreOriginalTextOnEscape = false;
        }

        /// <summary>True while the field owns the keyboard (TMP isFocused = m_AllowInput, already
        /// false when a cancel restores text). Every genuine edit happens while focused; the seed and
        /// our own restores are silent (SetTextWithoutNotify), so nothing legitimate is dropped.</summary>
        private static bool Typing(UiaTextInput f)
        {
            return f == null || f.Field == null || f.Field.isFocused;
        }

        private static void KeepDraft(UiaTextInput f, string want)
        {
            if (f == null || f.Field == null || f.Field.isFocused) return;
            if (!string.Equals(f.Text, want ?? "", StringComparison.Ordinal)) f.SetTextSilent(want ?? "");
        }

        private static void SetInteractable(UiaTextInput f, bool on)
        {
            if (f != null && f.Field != null && f.Field.interactable != on) f.Field.interactable = on;
        }

        private static void SetPlaceholder(UiaTextInput f, string text)
        {
            if (f == null || f.Field == null) return;
            var ph = f.Field.placeholder as TMP_Text;
            if (ph != null) ph.text = text ?? "";
        }

        private static string TitlePlaceholder(string kind)
        {
            if (kind == KindBug) return "e.g. The radial closes when I swap hands";
            if (kind == KindSuggestion) return "e.g. Let me pin a bag window to the HUD";
            return "A short summary";
        }

        private static string DescriptionLabel(string kind)
        {
            if (kind == KindBug) return "What happened?";
            if (kind == KindSuggestion) return "What would you like?";
            return "Description";
        }

        private static string DescriptionPlaceholder(string kind)
        {
            if (kind == KindBug) return "What you did, what you expected, what happened instead.";
            if (kind == KindSuggestion) return "What it would let you do - and why it would help.";
            return "Pick Bug or Suggestion above, then tell us about it.";
        }

        private static string Value(List<KeyValuePair<string, string>> ctx, string key)
        {
            for (int i = 0; i < ctx.Count; i++)
                if (ctx[i].Key == key) return ctx[i].Value ?? "";
            return "";
        }

        /// <summary>HudProfileStore.ShippedEditState ("no" / "yes" / "custom", optionally with
        /// "(+unsaved edits)") in player words. "" = unknown - said as nothing, never guessed.</summary>
        private static string EditedWords(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool unsaved = s.IndexOf("unsaved", StringComparison.OrdinalIgnoreCase) >= 0;
            string b = s.StartsWith("no", StringComparison.OrdinalIgnoreCase) ? "unedited"
                : s.StartsWith("yes", StringComparison.OrdinalIgnoreCase) ? "edited"
                : s.StartsWith("custom", StringComparison.OrdinalIgnoreCase) ? "your own" : "";
            if (unsaved) b = b.Length > 0 ? b + ", with unsaved changes" : "unsaved changes";
            return b;
        }

        private static List<string> CleanNotes(List<string> notes)
        {
            var list = new List<string>();
            if (notes == null) return list;
            for (int i = 0; i < notes.Count; i++)
            {
                string s = notes[i];
                if (string.IsNullOrEmpty(s)) continue;
                if (s.StartsWith("+profile: ", StringComparison.Ordinal)) s = "HUD profile: " + s.Substring(10);
                else if (s.StartsWith("+log: ", StringComparison.Ordinal)) s = "Log lines: " + s.Substring(6);
                list.Add(Printable(s));
            }
            return list;
        }

        private static string SafeHost()
        {
            string h = FeedbackService.RelayHostForDisplay();
            return Printable(string.IsNullOrEmpty(h) ? "the UI Ascended feedback server" : h);
        }

        /// <summary>The TMP tofu rule for anything that did not come from our own literals (relay
        /// error text, exception messages, an OS string, a profile name): printable ASCII only,
        /// line breaks become spaces.</summary>
        internal static string Printable(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r' || c == '\n' || c == '\t') sb.Append(' ');
                else sb.Append(c >= ' ' && c <= '~' ? c : '?');
            }
            return sb.ToString();
        }
    }
}
