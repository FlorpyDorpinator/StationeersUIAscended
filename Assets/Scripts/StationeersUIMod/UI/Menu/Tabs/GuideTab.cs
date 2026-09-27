using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>The in-menu how-to: the LESSONS list (0.9.8.0 tutorial - every lesson with its
    /// state, Watch / Try it, skip / stop / restart / stop the tour, and the just-in-time switch),
    /// then what the two halves are, how the radial gestures work, how smart storage works, and a
    /// live key reference (reads current binds from UiaKeybinds so it always matches the player's
    /// rebinds). Kit v2 conversion: each group is a Section; every row registers with the settings
    /// search.</summary>
    public sealed class GuideTab : IUiaTab
    {
        public string Title => "Guide";

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            BuildLessons(col);
            BuildHalves(col);
            BuildWheels(col);
            BuildInventory(col);
            BuildStorage(col);
            BuildVisorHud(col);
        }

        // ---------- Two halves ----------

        private void BuildHalves(Transform col)
        {
            var sec = UiaComposite.Section(col, "guide.halves", "Two halves - use either, or both");
            UiaControls.Note(sec.Body,
                "UI Ascended is two independent parts. The RADIAL half replaces menu-diving with " +
                "wheels for your tools, belt, bags and equipment. The VISOR HUD half is a clean, " +
                "curved heads-up display you can restyle or design yourself. Turn either on or " +
                "off with the two switches at the top of this window - they never depend on each other.");

            // The deep-dive reference. (The old "Replay the tutorial" button lived here; the Lessons
            // section above replaces it - its search label now jumps there.)
            var btnRow = UiaUi.Go("guidebtns", sec.Body);
            UiaUi.Size(btnRow, 32f).minHeight = 32f;
            UiaUi.HLayout((RectTransform)btnRow.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);
            var handbookBtn = UiaControls.Button(btnRow.transform, "Designer Handbook",
                () => HandbookViewer.Open(), 190f, 28f, UiaControls.ButtonStyle.Panel);

            UiaSearch.RegisterRow(Title, "guide.halves", "Designer Handbook", UiaSearch.MakeJump(handbookBtn.gameObject));
        }

        // ---------- Lessons (0.9.8.0 tutorial; copy from plan C.19) ----------

        /// <summary>What the Lessons section remembers across Refresh AND Restyle (UiaPageState;
        /// dropped by the shell's one UiaPageState.Reset on teardown): the inline status line left
        /// by the last Watch / Try it / Skip / Stop / Restart. Plain data only.</summary>
        private sealed class LessonsState
        {
            internal const string Key = "guide.lessons";
            public string Msg;
            public UiaComposite.StatusKind Kind;
        }

        /// <summary>Every lesson with its state, Watch (cards, paused in single-player) and Try it
        /// (live practice - F10 closes once the lesson has started), plus Continue / Skip this lesson
        /// / Stop all lessons / Restart, Stop the tour (only while the first-run tour is under way)
        /// and the just-in-time switch (UIAConfig.TutorialTips). Everything goes through
        /// TutorialDirector (the tour's state is read from its progress store); nothing here touches
        /// game state.</summary>
        private void BuildLessons(Transform col)
        {
            var st = UiaPageState.Get<LessonsState>(LessonsState.Key);
            var sec = UiaComposite.Section(col, "guide.lessons", "Lessons");
            UiaControls.Note(sec.Body,
                "Short hands-on lessons. Each one also shows up by itself the first time you need it.");

            GameObject tipsRow = null;
            if (UIAConfig.TutorialTips != null)
            {
                var tips = UiaControls.ToggleRow(sec.Body, "Show lessons when something new comes up",
                    UIAConfig.TutorialTips.Value, v => { if (UIAConfig.TutorialTips != null) UIAConfig.TutorialTips.Value = v; });
                tipsRow = tips != null && tips.transform.parent != null ? tips.transform.parent.gameObject : null;
            }

            Tutorial.TLesson[] lessons = null;
            string activeId = null;
            try
            {
                lessons = Tutorial.TutorialDirector.Lessons;
                activeId = Tutorial.TutorialDirector.ActiveLessonId;
            }
            catch (System.Exception e) { UIALog.Warn("Guide tab: lesson list unavailable: " + e.Message); }

            var listGo = UiaUi.Go("lessons", sec.Body);
            UiaUi.VLayout((RectTransform)listGo.transform, 4f);
            string continueId = activeId;
            int shown = 0;
            if (lessons != null)
            {
                for (int i = 0; i < lessons.Length; i++)
                {
                    var lesson = lessons[i];
                    if (lesson == null || string.IsNullOrEmpty(lesson.Id)) continue;
                    string title = LessonTitle(lesson);
                    if (string.IsNullOrEmpty(title)) continue;   // entry cards etc. - nothing to replay by name
                    var state = StateOf(lesson.Id);
                    if (continueId == null && state == Tutorial.TLessonState.Active) continueId = lesson.Id;
                    bool running = string.Equals(lesson.Id, activeId, System.StringComparison.Ordinal);
                    var row = LessonRow(listGo.transform, lesson, title, state, running);
                    UiaSearch.RegisterRow(Title, "guide.lessons", "Lesson: " + title, UiaSearch.MakeJump(row));
                    shown++;
                }
            }
            if (shown == 0)
                UiaControls.Note(listGo.transform, "No lessons to show right now - they load with your world.");

            var acts = UiaUi.Go("lesson-actions", sec.Body);
            UiaUi.Size(acts, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)acts.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            string cid = continueId;
            var cont = UiaControls.Button(acts.transform, "Continue lesson", () => ContinueLesson(cid),
                170f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
            if (cont != null) cont.SetEnabled(cid != null);
            var skip = UiaControls.Button(acts.transform, "Skip this lesson", SkipLesson, 170f, UiaTheme.RowH);
            if (skip != null) skip.SetEnabled(cid != null);
            UiaControls.Button(acts.transform, "Stop all lessons", StopLessons, 170f, UiaTheme.RowH);
            UiaComposite.ConfirmButton(acts.transform, "Restart from the beginning", RestartLessons, 260f, UiaTheme.RowH);

            // Tour mode (review fix 2026-09-26): while the first-run tour is under way - running, or
            // paused - stop JUST the tour; "Stop all lessons" also turns lessons off. Its own row, not
            // a fifth button above: those widths are minimums (UiaUi.Size), and five would crowd the
            // narrowest (1000-unit) window.
            GameObject tourRow = null;
            if (TourUnderWay())
            {
                tourRow = UiaUi.Go("tour-actions", sec.Body);
                UiaUi.Size(tourRow, UiaTheme.RowH).minHeight = UiaTheme.RowH;
                UiaUi.HLayout((RectTransform)tourRow.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);
                UiaControls.Button(tourRow.transform, "Stop the tour", StopTour, 170f, UiaTheme.RowH);
                UiaControls.Note(sec.Body,
                    "Stop the tour ends just the tour; Stop all lessons also turns lessons off. " +
                    "Lessons the tour didn't finish show up once, when you need them.");
            }

            UiaComposite.InlineStatus(sec.Body, st.Msg, st.Kind);

            UiaSearch.RegisterRow(Title, "guide.lessons", "Lessons", UiaSearch.MakeJump(sec.Root.gameObject));
            UiaSearch.RegisterRow(Title, "guide.lessons", "Replay the tutorial", UiaSearch.MakeJump(sec.Root.gameObject));
            if (tipsRow != null)
                UiaSearch.RegisterRow(Title, "guide.lessons", "Show lessons when something new comes up", UiaSearch.MakeJump(tipsRow));
            UiaSearch.RegisterRow(Title, "guide.lessons", "Stop all lessons", UiaSearch.MakeJump(acts));
            UiaSearch.RegisterRow(Title, "guide.lessons", "Restart the lessons", UiaSearch.MakeJump(acts));
            if (tourRow != null)
                UiaSearch.RegisterRow(Title, "guide.lessons", "Stop the tour", UiaSearch.MakeJump(tourRow));
        }

        /// <summary>One lesson: title, state chip (chrome|state.* copy - NEW / IN PROGRESS / DONE /
        /// SKIPPED as shipped), Watch, Try it.</summary>
        private static GameObject LessonRow(Transform parent, Tutorial.TLesson lesson, string title,
            Tutorial.TLessonState state, bool running)
        {
            var row = UiaUi.Image(parent, UiaTheme.PanelRaised, "lesson");
            row.raycastTarget = false;
            UiaUi.Size(row.gameObject, 34f).minHeight = 34f;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 10, 8, 3, 3, TextAnchor.MiddleLeft);

            var t = UiaUi.Text(row.transform, title, UiaTheme.LabelSize, UiaTheme.Text, TextAlignmentOptions.Left);
            t.richText = false;
            t.characterSpacing = 2f;
            UiaControls.FitText(t, 9f, false);   // no ellipsis: shrink inside the row
            var tle = UiaUi.Size(t.gameObject, 28f, flexW: 1f);
            tle.minWidth = 0f;
            tle.preferredWidth = 0f;

            StateChip(row.transform, state, running);

            string id = lesson.Id;
            bool live = IsLiveLesson(lesson);
            UiaControls.Button(row.transform, "Watch", () => WatchLesson(id), 84f, 26f);
            UiaControls.Button(row.transform, "Try it", () => TryLesson(id, live), 84f, 26f);
            return row.gameObject;
        }

        private static void StateChip(Transform parent, Tutorial.TLessonState state, bool running)
        {
            string fallback;
            Color col;
            if (running) state = Tutorial.TLessonState.Active;
            if (state == Tutorial.TLessonState.Active) { fallback = "IN PROGRESS"; col = UiaTheme.Selected; }
            else if (state == Tutorial.TLessonState.Done || state == Tutorial.TLessonState.Learned) { fallback = "DONE"; col = UiaTheme.Good; }
            else if (state == Tutorial.TLessonState.Skipped) { fallback = "SKIPPED"; col = UiaTheme.TextMute; }
            else { fallback = "NEW"; col = UiaTheme.Accent; }   // New / Offered / Later
            // The words are lesson COPY (chrome|state.new / .progress / .done / .skipped, plan C.19), so a
            // Lesson Editor edit reaches the Guide too; TutorialDirector.StateLabel owns the same state ->
            // key mapping the console's `uiatutorial list` prints. The literal is only the fail-soft.
            string text = null;
            try { text = Tutorial.TutorialDirector.StateLabel(state); }
            catch { text = null; }
            if (string.IsNullOrEmpty(text)) text = fallback;

            var chipGo = UiaUi.Go("state", parent);
            UiaUi.Size(chipGo, 22f, 110f, flexW: 0f).minHeight = 22f;
            var chip = chipGo.AddComponent<Image>();
            chip.color = UiaTheme.Panel;
            chip.raycastTarget = false;
            UiaImages.Round(chip);
            UiaUi.OutlineOf(chip, new Color(col.r, col.g, col.b, 0.55f), 1f);
            var ct = UiaUi.Text(chipGo.transform, text, UiaTheme.SmallSize - 1f, col, TextAlignmentOptions.Center);
            ct.richText = false;
            ct.fontStyle = FontStyles.Bold;
            ct.characterSpacing = 2f;
            UiaUi.Fill((RectTransform)ct.transform);
            UiaControls.FitText(ct, 8f, false);
        }

        private static string LessonTitle(Tutorial.TLesson lesson)
        {
            string t = null;
            try
            {
                if (!string.IsNullOrEmpty(lesson.TitleKey)) t = Tutorial.TutorialTextStore.Get(lesson.TitleKey);
                if (!string.IsNullOrEmpty(t)) t = Tutorial.TutorialTokens.Resolve(t);
            }
            catch { t = null; }
            return t;
        }

        private static Tutorial.TLessonState StateOf(string id)
        {
            try { return Tutorial.TutorialDirector.StateOf(id); }
            catch { return Tutorial.TLessonState.New; }
        }

        /// <summary>Does this lesson practise on the live world (strips / tips)? Then F10 must get
        /// out of the way once it starts. Cards and F10-tour callouts show over the menu. Judged by
        /// the first step that TEACHES - not a tour-mode setup strip ("Press F10...", "Press F9..."),
        /// which is passed over when its situation already exists: with F10 open, lesson 16's callouts
        /// and lesson 17's card must keep the menu (closing it would end the callouts at once).</summary>
        private static bool IsLiveLesson(Tutorial.TLesson lesson)
        {
            try
            {
                if (lesson == null || lesson.Steps == null) return false;
                for (int i = 0; i < lesson.Steps.Length; i++)
                {
                    var s = lesson.Steps[i];
                    if (s == null || s.IsSetup) continue;
                    return s.Kind == Tutorial.TPresentation.Strip || s.Kind == Tutorial.TPresentation.Tip;
                }
            }
            catch { }
            return false;
        }

        private static bool IsLiveLessonId(string id)
        {
            try
            {
                var all = Tutorial.TutorialDirector.Lessons;
                if (all == null) return false;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && string.Equals(all[i].Id, id, System.StringComparison.Ordinal))
                        return IsLiveLesson(all[i]);
            }
            catch { }
            return false;
        }

        private static bool Play(string id, bool watch)
        {
            try { return Tutorial.TutorialDirector.PlayLesson(id, watch); }
            catch (System.Exception e)
            {
                UIALog.Warn("Guide tab: PlayLesson(" + id + ") failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Watch: the lesson replays as cards over this menu (paused in single-player).</summary>
        private static void WatchLesson(string id)
        {
            if (Play(id, true)) { SetStatus(null, UiaComposite.StatusKind.Info, false); return; }
            SetStatus("That lesson can't be shown right now.", UiaComposite.StatusKind.Warn, true);
        }

        /// <summary>Try it: the live version. Started FIRST (the menu stays if it can't), then F10
        /// closes for a strip lesson - practice happens on the world, not behind the menu.</summary>
        private static void TryLesson(string id, bool live)
        {
            if (!Play(id, false))
            {
                SetStatus("Nothing to practise that on right now - it starts by itself when it comes up.",
                    UiaComposite.StatusKind.Warn, true);
                return;
            }
            SetStatus(null, UiaComposite.StatusKind.Info, false);
            if (live) UiaControlCenter.Close();
        }

        private static void ContinueLesson(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            string active = null;
            try { active = Tutorial.TutorialDirector.ActiveLessonId; } catch { }
            bool live = IsLiveLessonId(id);
            if (string.Equals(id, active, System.StringComparison.Ordinal))
            {
                // Already running behind the menu: just get out of its way.
                SetStatus(null, UiaComposite.StatusKind.Info, false);
                if (live) UiaControlCenter.Close();
                return;
            }
            if (!Play(id, false))
            {
                SetStatus("That lesson can't continue right now.", UiaComposite.StatusKind.Warn, true);
                return;
            }
            SetStatus(null, UiaComposite.StatusKind.Info, false);
            if (live) UiaControlCenter.Close();
            else UiaControlCenter.Refresh();
        }

        private static void SkipLesson()
        {
            try { Tutorial.TutorialDirector.SkipCurrentLesson(); }
            catch (System.Exception e) { UIALog.Warn("Guide tab: SkipCurrentLesson failed: " + e.Message); }
            SetStatus("Lesson skipped - replay it any time from this list.", UiaComposite.StatusKind.Info, true);
        }

        private static void StopLessons()
        {
            try { Tutorial.TutorialDirector.StopAllLessons(); }
            catch (System.Exception e) { UIALog.Warn("Guide tab: StopAllLessons failed: " + e.Message); }
            SetStatus("Lessons stopped. Replay any of them from this list.", UiaComposite.StatusKind.Info, true);
        }

        /// <summary>Is the first-run tour under way - playing now, or paused (quit mid-tour, Welcome
        /// back "Later")? Read from the director's own persisted record of it: TTourState.Running is
        /// exactly "started and not finished" (TutorialProgressStore.TourState - internal, a field
        /// read after the first load). Build-time only, never per frame.</summary>
        private static bool TourUnderWay()
        {
            try { return Tutorial.TutorialProgressStore.TourState == Tutorial.TTourState.Running; }
            catch { return false; }
        }

        /// <summary>GUIDE "Stop the tour": the first-run tour ends - running or paused - WITHOUT turning
        /// lessons off (unlike Stop all lessons). Lessons it did not finish keep their one just-in-time
        /// comeback. The refresh drops this row once the director has ended the tour.</summary>
        private static void StopTour()
        {
            try { Tutorial.TutorialDirector.StopTour(); }
            catch (System.Exception e) { UIALog.Warn("Guide tab: StopTour failed: " + e.Message); }
            SetStatus("Tour stopped. Lessons it didn't finish show up once, when you need them - or replay any from this list.",
                UiaComposite.StatusKind.Info, true);
        }

        private static void RestartLessons()
        {
            try { Tutorial.TutorialDirector.Restart(); }
            catch (System.Exception e) { UIALog.Warn("Guide tab: Restart failed: " + e.Message); }
            SetStatus("Lesson progress cleared. The welcome card shows on your next world entry.",
                UiaComposite.StatusKind.Good, true);
        }

        /// <summary>Remember the status line for the (re)built page; optionally rebuild now so the
        /// chips and buttons reflect the Director's new state.</summary>
        private static void SetStatus(string msg, UiaComposite.StatusKind kind, bool refresh)
        {
            var st = UiaPageState.Get<LessonsState>(LessonsState.Key);
            st.Msg = msg;
            st.Kind = kind;
            if (refresh) UiaControlCenter.Refresh();
        }

        // ---------- The wheels ----------

        private void BuildWheels(Transform col)
        {
            var sec = UiaComposite.Section(col, "guide.wheels", "The wheels");
            UiaControls.Note(sec.Body,
                "TAP a wheel key: the wheel opens and STAYS - point and LEFT-CLICK to act, " +
                "RIGHT-CLICK to back out, Esc (or the key again) closes. HOLD the key instead for " +
                "the quick version: the belt and tool wheels become sweep-and-release, the number " +
                "keys equip, Tab shows the scoreboard. A wedge with a chevron holds more: push OUT " +
                "through it past the rim and hold still a moment - a child wheel opens (Take / " +
                "Replace / settings). Replace is how you swap a battery or canister in one step.");
            KeyRow(sec.Body, "Tool / device wheel (item in hand)", UiaKeybinds.Glyph("UIA_ToolRadial"));
            KeyRow(sec.Body, "Toolbelt wheel (top wedge = The Hub)", UiaKeybinds.Glyph("UIA_ToolbeltRadial"));
            KeyRow(sec.Body, "Bag / backpack wheel", UiaKeybinds.Glyph("UIA_BagRadial"));
            KeyRow(sec.Body, "Equipment wheels (tap = wheel, hold = equip)", "1 - 6");
            KeyRow(sec.Body, "Select / drag a wedge", "LMB");
            KeyRow(sec.Body, "Back / close", "RMB");
            KeyRow(sec.Body, "Keep the wheel open after an action", "Shift");
            KeyRow(sec.Body, "Reach into the world (grab items)", "Alt");
            KeyRow(sec.Body, "Swap active hand", UiaKeybinds.Glyph("UIA_HandSwap"));
            KeyRow(sec.Body, "Page a crowded wheel / swap worn belt", UiaKeybinds.Glyph("UIA_Page"));
            KeyRow(sec.Body, "Swap toolbelt / backpack (wheel open)", "Tab");
            KeyRow(sec.Body, "Open a bound bag", "Ctrl + 1 - 0");
            UiaControls.Note(sec.Body,
                "Bind a bag: with a wheel open, hover the bag and press a number key - remembered " +
                "per save. Drag an item off a wheel onto open screen to PARK it while you sort. " +
                "Close the wheel any way you like - Esc, its key, the Close band - and anything " +
                "you dragged out drops at your feet.");
        }

        // ---------- The Universal Inventory ----------

        private void BuildInventory(Transform col)
        {
            var sec = UiaComposite.Section(col, "guide.inventory", "The Universal Inventory");
            KeyRow(sec.Body, "Open it (tap = stays, hold = peek)", UiaKeybinds.Glyph("UIA_Grid"));
            UiaControls.Note(sec.Body,
                "One window for every container you wear or hold. Free the mouse to click inside: " +
                "hold Alt, or DOUBLE-TAP Alt to keep it free. Bags are folder tabs - click one to " +
                "open it, DRAG the tab out to pin the bag as its own window. Shift + 1 - 6 (or a " +
                "click on a 1 - 6 HUD box with the mouse free) opens that worn container right " +
                "here - or brings up its own window if you pinned it. While " +
                "the mouse is captured the SCROLL WHEEL moves a highlight and F takes - or places - " +
                "the item; with the mouse free, scroll pans, LEFT-CLICK takes to your hand, " +
                "RIGHT-CLICK opens the item's wheel, and you can drag anything anywhere.");
        }

        // ---------- Smart storage ----------

        private void BuildStorage(Transform col)
        {
            var sec = UiaComposite.Section(col, "guide.storage", "Smart storage");
            KeyRow(sec.Body, "Smart Stow the held item", "G");
            UiaControls.Note(sec.Body,
                "SmartStow+ extends vanilla stow (G): it tops up a matching stack (and keeps going " +
                "until the hand is empty), sends components to their sockets (canisters to tanks, " +
                "batteries to battery slots). It works with a wheel open, too. Two modes, set on " +
                "the SmartStow tab:");
            UiaControls.Note(sec.Body,
                "Simple: Smart Stow returns each item to where you last placed it - move an item " +
                "by hand and its home moves.");
            UiaControls.Note(sec.Body,
                "Complex: Storage Layouts of Bag Profiles - set them up in the SmartStow tab.");
        }

        // ---------- The visor HUD ----------

        private void BuildVisorHud(Transform col)
        {
            var sec = UiaComposite.Section(col, "guide.hud", "The visor HUD");
            UiaControls.Note(sec.Body,
                "Pick a ready-made look on the UI Themes tab, or open the HUD Designer (F9) to " +
                "build your own - every element can be moved, resized, recoloured and restyled. UI " +
                "Themes are plain files you can share with a friend. The Designer Handbook (button " +
                "above) is the full deep-dive.");
            KeyRow(sec.Body, "Open the HUD Designer", UiaKeybinds.Glyph("UIA_HudDesigner"));
            KeyRow(sec.Body, "Open this menu", UiaKeybinds.Glyph("UIA_Menu"));

            UiaControls.Note(sec.Body,
                "Rebind any of these under the Controls tab. (Only the menu key also appears in the " +
                "game's own Controls screen, under \"UI Ascended\" - the rest live here to avoid " +
                "false conflict warnings.)");
        }

        private static GameObject KeyRow(Transform parent, string label, string key)
        {
            var row = UiaUi.Go("keyrow", parent);
            // minHeight = preferredHeight: a hand-built row bypasses whatever floor Kit v2 gives
            // UiaUi.Size/Button/Note centrally, so it needs its own explicit floor or a squeeze
            // can crush it below readable height (pixel-verified regression, F10 visual fix wave).
            UiaUi.Size(row, 26f).minHeight = 26f;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);

            var lblGo = UiaUi.Go("l", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.SmallSize; lbl.color = UiaTheme.TextDim;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = label;
            UiaControls.FitText(lbl, 9f);   // no ellipsis: shrink, then 2 lines in the 26px row
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var chipGo = UiaUi.Go("chip", row.transform);
            UiaUi.Size(chipGo, 22f, 96f).minHeight = 22f;
            var chip = chipGo.AddComponent<Image>(); chip.color = UiaTheme.PanelRaised;
            UiaUi.OutlineOf(chip, UiaTheme.AccentDim, 1f);
            var kt = UiaUi.Text(chipGo.transform, key, UiaTheme.SmallSize, UiaTheme.Accent, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)kt.transform);
            return row;
        }
    }
}
