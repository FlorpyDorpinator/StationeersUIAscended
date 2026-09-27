using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Grid;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.Windows;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>Per-lesson progress (Progress.xml). New = never shown; Offered = shown but not
    /// finished (S4 / ignored) - may come back; Active = running or interrupted (First Steps resumes;
    /// so does the lesson a paused first-run tour stands at); Done; Skipped (the player said so, or
    /// the first-run tour set it aside - replayable from GUIDE; after the tour it comes back once);
    /// Learned (skill evidence - never shown); Later (the Welcome card's "ask me next time").</summary>
    internal enum TLessonState : byte { New, Offered, Active, Done, Skipped, Learned, Later }

    /// <summary>
    /// The tutorial's scheduler - the BRAIN (Documentation/0.9.8.0/Tutorial-Plan-and-Script.md
    /// A.5-A.7, A.10, A.12, D.1-D.3; interfaces pinned in Tutorial-Build-Contract.md s6).
    ///
    /// <para><b>What it owns.</b> The first-run gate (moved here from StationeersUIMod.Update,
    /// unchanged: in a world, menus toggleable, nothing else up, input state Game, not paused, no
    /// vanilla menu, a responsive body, held 1 s), the Welcome / Welcome-back / Hold-on / What's-new
    /// cards and their choices, the multiplayer invitation strip, First Steps (lesson 1) as live
    /// strips, the 14 just-in-time lessons (triggers from 4 Hz polls + <see cref="TutorialSignals"/>,
    /// a queue of 3, 60 s between lessons, 20 s after a world load, skill evidence, S1/S2/S4 timers,
    /// the safety gate and "Lesson on hold"), the seven "as I go" tips, Watch mode (a lesson replayed
    /// as cards), the F10 tour (lesson 16, callouts), the F9 first-press card (lesson 17) and the
    /// editor hooks (<see cref="PreviewStep"/> / <see cref="RefreshActiveText"/>).</para>
    ///
    /// <para><b>Tour mode</b> (FlorpyDorp, 2026-09-26: "show all the lessons in order one after another
    /// when a player first opens the mod"). Welcome &gt; Start the tour plays the overview cards, then
    /// <see cref="TutorialChapters.Tour"/> (lessons 1-17) back to back - no queue, no eligibility, no
    /// 60 s gap, no skill-evidence suppression - then the entry.tourend card. Each lesson opens with its
    /// SETUP step (<see cref="TStep.IsSetup"/>): passed over silently when its situation already exists,
    /// and when it self-skips the lesson is set aside (Skipped) and the tour moves on. The strip header
    /// is chrome|tourheader. Quit mid-tour and the Welcome-back card continues it at the same lesson and
    /// step (<see cref="TutorialProgressStore.TourState"/> / <see cref="TutorialProgressStore.TourLessonId"/>).
    /// After the tour, a lesson it did not finish comes back just-in-time ONCE; a finished one never
    /// does. "Teach me as I go" keeps the full just-in-time behaviour. Updaters (GuideShown, no
    /// Progress.xml) get the same Welcome - What's new is not shown when the tour runs.</para>
    ///
    /// <para><b>What it never does.</b> Mutate game state or synthesise input: it only READS (hands,
    /// held item, tier, open windows), listens to signals raised by the real UI after the real action
    /// returned, and talks through <see cref="TutorialStrip"/>, <see cref="TutorialCoach"/> and
    /// <see cref="TutorialSpotlight"/>. The only game call on its behalf is the coach's own
    /// <c>GamePause</c> (single-player cards).</para>
    ///
    /// <para><b>Callbacks are deferred.</b> Card buttons and the strip's confirm callback only RECORD
    /// what happened; <see cref="Tick"/> acts on it next frame - so nothing re-enters the coach or the
    /// strip from inside their own event handlers, and a callback fired by our own Close/Hide calls is
    /// dropped (<c>_suppress</c>).</para>
    ///
    /// <para><b>Per frame</b>: an int-compare pass over the signal counters, a few float timers and
    /// bit tests - zero allocation. Text is resolved only when it changes (step change, variant, oops,
    /// chrome line). <b>Hot reload</b>: every static resets in <see cref="Shutdown"/>; the director
    /// holds no event subscription (signals are polled; the card/strip delegates are static readonly
    /// method groups that die with the assembly).</para>
    /// </summary>
    internal static class TutorialDirector
    {
        // ================================================================ tuning (plan A.5-A.10)

        private const float SettleSeconds = 1.0f;          // first-run gate settle (unchanged)
        private const float PollInterval = 0.25f;          // 4 Hz polls
        private const float LessonGapSeconds = 60f;        // between lessons
        private const float WorldSettleSeconds = 20f;      // after a world load
        private const int QueueMax = 3;
        private const float QueueExpirySeconds = 120f;     // a stale "moment of need" is no longer one
        private const float S1WarnSeconds = 45f;           // "No rush - this step skips itself in 15 s."
        private const float S1SkipSeconds = 60f;
        private const float PulseAfterSeconds = 20f;       // waiting: [Key] pulses once every 4 s
        private const float PulseEverySeconds = 4f;
        private const float HoldAfterSeconds = 2f;         // not calm for 2 s -> "Lesson on hold"
        private const float ResumeAfterSeconds = 5f;       // resumes 5 s after calm returns
        private const float OopsSeconds = 5f;
        private const float WaitingChipSeconds = 90f;      // no progress -> "Lesson waiting"
        private const float ContextLostSeconds = 3f;       // S4
        private const double ReofferMinutes = 10.0;        // ignored offers come back 10+ min apart
        private const int MaxOffers = 3;
        private const float RemindCalmSeconds = 300f;      // "Remind me in 5 minutes" (of calm play)
        private const float InviteSeconds = 12f;           // 0.2 / MP what's-new strip
        private const float NoticeSeconds = 6f;            // "Lessons are off..."
        private const float ConfirmWatchdogExtra = 3f;     // a lost confirm callback never strands a step
        private const float TipGapSeconds = 20f;
        private const float TipMomentSeconds = 10f;        // a tip's moment of need expires
        private const float WindowOverrunSeconds = 30f;    // 1.8 -> 1.9 waits for the window to close
        private const float BranchNoArrowSeconds = 20f;    // 1.7 branch
        private const int LessonWheelsAfterCore = 8;       // lesson 2
        private const int LessonBeltTaps = 20;             // lesson 11
        private const int LessonStowCount = 5;             // lesson 9
        private const int LessonSettingClicks = 5;         // lesson 10
        private const int LessonBagWheels = 3;             // lesson 13
        private const float LessonVisorAfterCore = 180f;   // lesson 14: 3+ min after First Steps
        private const float LessonVisorTier = 10f;
        private const float LessonSensesTier = 5f;
        private const int ResumeLaterMax = 2;              // Welcome back: Later re-offers it twice, the third converts
        private const int TickBreakAfter = 30;             // failing frames IN A ROW before a tutorial tick is switched off

        // ================================================================ state

        private enum Flow : byte { Idle, Lesson, Tip, Invite, Notice, Watch, Tour }
        // (Naming: "Tour" / Flow.Tour / CardCtx.Tour are lesson 16's F10 callout tour, as before; the
        // first-run tour of tour mode is "the first-run tour" - _tourLive, TourStartCurrent, TourEnd...)
        private enum CardCtx : byte { None, Welcome, Resume, Hold, LessonCard, Designer, WhatsNew, WhatsNewPage, Watch, TourOpener, Tour, Preview, TourEnd }
        private enum Act : byte { B0, B1, B2, Next, Back, SkipLesson, SkipTour, TryNow, Done, TourTake, TourNo, TourNext, TourStop }
        private enum Chrome : byte { None, SelfSkip, Reopen, OnHold, Waiting, Overrun }
        private enum Entry : byte { None, Welcome, Invite, Resume, WhatsNew, WhatsNewInvite }

        // Step evidence bits (since the current step began; see Ev()).
        private const ulong EvBeltSticky = 1UL << 0, EvBeltHold = 1UL << 1, EvToolWheel = 1UL << 2,
            EvTakeCommit = 1UL << 3, EvSwapCommit = 1UL << 4, EvOpenCommit = 1UL << 5, EvStowCommit = 1UL << 6,
            EvSettingCommit = 1UL << 7, EvChildWheel = 1UL << 8, EvBackedOut = 1UL << 9, EvWheelClosed = 1UL << 10,
            EvChipParked = 1UL << 11, EvChipDropped = 1UL << 12, EvWorldGrab = 1UL << 13, EvWorldCue = 1UL << 14,
            EvHubMoved = 1UL << 15, EvKeepOpen = 1UL << 16, EvValueScrolled = 1UL << 17, EvGreyHovered = 1UL << 18,
            EvHotkeyBound = 1UL << 19, EvBagBound = 1UL << 20, EvDigitJump = 1UL << 21, EvGearHeld = 1UL << 22,
            EvBeltPicker = 1UL << 23, EvBeltSwapped = 1UL << 24, EvBeltBack = 1UL << 25, EvSearchTook = 1UL << 26,
            EvStowOk = 1UL << 27, EvGridTab = 1UL << 28, EvGridTake = 1UL << 29, EvGridItemWheel = 1UL << 30,
            EvGridDrag = 1UL << 31, EvGridShiftDrag = 1UL << 32, EvGridSplit = 1UL << 33, EvDeviceWindow = 1UL << 34,
            EvPin = 1UL << 35, EvInGrid = 1UL << 36, EvGridKeyboard = 1UL << 37, EvVitals = 1UL << 38,
            EvHoldCommit = 1UL << 39, EvHandSwapped = 1UL << 40, EvAnyCommit = 1UL << 41,
            // ChildWheelOpened carries the classified wedge: Open / Hub = a BRANCH DIVE (THE HUB, a
            // bag level, the REPLACE list); anything else = a slide-out SATELLITE (EvChildWheel).
            EvBranchDive = 1UL << 42, EvHubDive = 1UL << 43,
            // Tour mode: the HUD Designer key was pressed during lesson 17's setup (the F9 intercept),
            // or that press is what started the lesson (seeded into the first step's window).
            EvDesignerKey = 1UL << 44;

        private static bool _init;
        private static float _lastTick;
        private static float _nextPoll, _lastPoll, _nextSchedule;

        // world
        private static bool _inWorld;
        private static int _entrySerial;
        private static float _worldEnterTime = -999f;
        private static bool _resumeOfferedThisEntry, _whatsNewHandledThisEntry, _welcomeHoldForNextEntry;
        private static int _laterAtEntry = -1;
        private static int _welcomeShownAtEntry = -1;
        private static float _gateSince = -1f;
        private static bool _inviteShown;          // 0.2: once per session
        private static bool _remindPending;
        private static float _remindCalm;
        // EntryNeeded() (native GamePause.CanOwnPause + progress lookups) and the first-run gate are
        // sampled at the 4 Hz poll rate, never per idle frame; 0 = evaluate on the next idle frame.
        private static float _nextEntryEval;
        private static bool _tipsWasOn = true;     // TutorialTips as last seen (its ON->OFF edge ends automatic lessons)
        private static bool _stoodDown;            // uiareset: the progress store went read-only - the director stands still

        // signals
        private static readonly int[] _seen = new int[(int)TSignal.Count];
        private static TWheelKind _wheelKind;
        private static bool _wheelSticky, _childSinceOpen;
        private static float _lastHoverAt = -1f;
        private static int _hoverDwells;
        private static bool _arrowSeenThisStep;
        // A ChildWheelOpened whose kind cannot tell a dive from a slide-out (see OnChildWheel): the
        // frame it was raised on, settled from the ring's own published geometry a frame later.
        private static int _childPendingFrame = -1;
        // Lesson 4's radial evidence: the open satellite came off a stack wedge / this wheel is the held
        // stack's own (both ARE the split level). Set on those open edges, read at a commit.
        private static bool _satFromStack, _heldStackWheel;
        // Lesson 3's gear-wheel evidence: a slide-out opened off a component-socket wedge (battery,
        // canister, filter, cartridge - RadialEntry.DeviceSlotStyle) in this wheel. Survives the REPLACE
        // dive that follows it; the next WheelOpened resets it.
        private static bool _compChild;
        // Tour mode (setup states). How deep inside THE HUB the open wheel is: 1 = THE HUB itself, more =
        // a bag level inside it, 0 = not in it (lesson 8's BagWheelOpen). Kept from the dive / back-out
        // signals; a new wheel or a close resets it. And a count of wheel opens, so lesson 7's second-belt
        // scan runs once per open wheel (BeltWheelWithSpare), never per frame.
        private static int _hubDepth;
        private static int _wheelSerial;

        // polled
        private static bool _calmNow;
        private static Slot _lastActiveHand;
        private static float _playSeconds;
        private static float _tabletFor, _lowFor, _toolFor, _stackHoverFor, _arrowHoverFor;
        private static float _suitedFor, _bareFor, _gridFreeFor, _gridLockedFor, _gridInteractiveFor;
        private static bool _gridWasOpen, _gridFreedSinceOpen;
        private static float _northFor;
        private static int _otherBeltFrame = -1;
        private static bool _escOopsArmed, _vanillaMenuWasUp;
        // Tour mode: the current setup step's STATE, read at 4 Hz (PollSetup) - IsDone answers from it;
        // lesson 4's setup dwell on a stack wedge; lesson 7's cached "another belt in reach" (scanned once
        // per open belt wheel: _beltSpareSerial = the _wheelSerial it was scanned for).
        private static bool _setupOk;
        private static float _setupStackFor;
        private static bool _beltSpare;
        private static int _beltSpareSerial = -1;

        // flow
        private static Flow _flow;
        private static TLesson _active;
        private static int _track;
        private static int _stepIdx;
        private static float _stepActive, _stepWait, _pulseNext;
        // S1's clock: in-control time on this step, wheel open or not (plan S1 is unconditional -
        // _stepActive, the read / timeout clock, only runs while the step's wheel is up).
        private static float _stepLive;
        private static bool _activeAuto;          // the running lesson was started by the scheduler (a just-in-time offer)
        private static bool _tourAuto;            // the F10 tour opened itself (first F10 open), not from GUIDE
        // Tour mode: the running lesson (or lesson 16's callouts) is being played BY the first-run tour -
        // its end moves the tour on.
        private static bool _activeTour;
        // Tour mode: the running lesson's tracks SET ASIDE (bit 1 << track) - a setup self-skipped, its
        // reason went away (S4), or (lesson 3) every move of that track was only shown. It then ends set
        // aside (Skipped) - lesson 3 still plays its other track first - and after the tour only a track
        // set aside comes back (TrackEligible). Lesson 3's bits are PERSISTED ("aside:tools@<track>",
        // AsideKey) as they happen, so a quit or an F6 between its two tracks keeps them: StartLesson
        // reads them back for the tour.
        private static int _asideTracks;
        // Lesson 3 in the tour: _doSteps / _doDone when the current track began (per-track "only shown").
        private static int _doStepsAtTrack, _doDoneAtTrack;
        // The evidence window of the NEXT step opens the moment the current one is DONE (its Then line
        // included): the hand it measures from and what was held are taken there.
        private static bool _windowOpen;
        private static long _windowHand;
        private static DynamicThing _windowHeld;
        private static float _stepRead = 8f;      // S2 reading time, computed on step entry (never per frame)
        private static ulong _ev, _evPending;
        private static bool _flagA, _flagB;
        private static string _sayField, _branchField, _oopsField, _header;
        private static float _oopsUntil;
        private static bool _confirming, _pendingConfirmDone;
        private static float _confirmStart, _confirmMin;
        private static bool _onHold;
        // Why the safety gate holds (TutorialSafety.HoldReason, read at 4 Hz while not calm): constant
        // strings, so the on-hold line is rebuilt only when the reason changes (_holdWhyShown = what it was
        // built with). Kept while a hold waits out its 5 s of calm; cleared when the hold ends.
        private static string _holdWhy, _holdWhyShown;
        private static bool _holdAcute;
        private static float _unsafeFor, _calmFor, _contextLostFor;
        private static int _doSteps, _doDone;
        private static long _handStartId;
        private static DynamicThing _stowItem;
        private static bool _skipStow;            // 1.4 branch: 1.5 is skipped
        private static bool _windowWaitClose;     // 1.8 done, waiting for the window to close before 1.9
        private static float _windowWaitFor;
        private static float _lastLessonEnd = -999f, _lastTipEnd = -999f;
        private static float _coreEndedAt = -999f;

        // strip / chrome
        private static bool _stripShowing;
        private static Chrome _chromeShown;
        private static float _drainShown = -2f;
        private static float _timedLeft, _timedTotal;
        private static TStep _timedStep;          // tip / invite step on the strip
        private static string _timedText, _timedHeader, _timedDemo, _timedId;
        // Spots of the step on the strip: which of step.Spots were NOT on screen when asked (bit i),
        // retried at the 4 Hz poll; and whether the hand-box fallback ring stands in for all of them.
        private static TStep _spotStep;
        private static int _spotMissing;
        private static bool _spotFallback;

        // cards
        private static CardCtx _card;
        private static Act[] _acts = new Act[0];
        private static TStep _cardStep;
        private static int _cardOpenFrame = -1;
        private static float _cardRetryAt;
        private static bool _pendingLater;
        private static int _suppress;
        private static int _pageIdx;              // What's-new pages / Watch / Tour index
        private static TLesson _watchLesson;
        private static int _watchTrack;
        private static bool _pendingOpenDesigner;
        private static int _f10OpenedByUsFrame = -100;
        // The frame the director itself closed F10 (CloseMenu): the plugin's F10-key toggle, which runs
        // after this tick in the same Update, must not open it again (MenuClosedThisFrame).
        private static int _f10ClosedByUsFrame = -100;
        private static bool _f10WasOpen;
        // Lesson 16's callouts: the furthest callout page shown this run (-1 = none yet). F10 closed (its
        // key, the X) on the last callout or the one before it = the tour was seen: Done, not Skipped.
        private static int _tourMaxPage = -1;

        // queue
        private struct Queued { internal TLesson L; internal float At; internal ulong Seed; internal int Track; }
        private static readonly Queued[] _queue = new Queued[QueueMax];
        private static int _queueCount;

        // tips
        private static readonly float[] _tipMoment = new float[8];   // 1..7; <0 = none

        // play requests (deferred to Tick)
        private static TLesson _pendingPlay;
        private static bool _pendingPlayWatch;

        // the first-run tour (tour mode). Its state and position are PERSISTED (TutorialProgressStore
        // TourState / TourLessonId / TourStep / TourTrack); these are this session's view of it.
        // _tourLive: the tour runs in THIS world entry - its lesson is up, or (flow idle: an explicit
        // Watch / Try it ran in between, F10 was open) TickTour starts it again. False = paused: quit
        // mid-tour, Welcome back "Later", Hold on "Remind me" - it waits for the Welcome-back card
        // (or GUIDE's Continue). _tourEndDue: the last lesson ended; entry.tourend waits for a clear
        // screen. _holdTour: the Hold-on card up now is the tour's. _inPress: inside a card button press
        // (a card that follows swaps in place, and our own pressed card is the only thing up).
        private static bool _tourLive;
        private static bool _tourEndDue;
        private static float _tourRetryAt;
        private static bool _holdTour;
        private static bool _inPress;
        // "jit:<lesson id>" per lesson (index = TutorialChapters.Lessons), built once on first use so the
        // 4 Hz eligibility checks never concatenate: the flag that a post-tour lesson came back once.
        private static string[] _jitKeys;

        // editor preview
        private static TStep _preview;
        private static CardCtx _cardBeforePreview;
        private static TStep _cardStepBeforePreview;
        private static string _cardBodyBeforePreview;

        // error reporting (log once per source per session)
        private static readonly List<string> _reported = new List<string>(4);

        // Per-frame tick circuit breaker (director / strip / spotlight): consecutive failing frames,
        // and the ticks switched off for the session. Index = TickSub().
        private const int SubDirector = 0, SubStrip = 1, SubSpotlight = 2;
        private static readonly string[] TickSubs = { "director", "strip", "spotlight" };
        private static readonly int[] _tickFails = new int[3];
        private static readonly bool[] _tickOff = new bool[3];

        // callbacks - static readonly method groups: allocated once, never re-subscribed
        private static readonly Action<int> _onButton = OnCardButton;
        private static readonly Action _onLater = OnCardLater;
        private static readonly Action _onConfirmDone = OnStripConfirmed;

        // ================================================================ public API (contract s6)

        internal static TLesson[] Lessons { get { return TutorialChapters.Lessons; } }

        internal static string ActiveLessonId
        {
            get
            {
                if (_flow == Flow.Watch && _watchLesson != null) return _watchLesson.Id;
                if (_flow == Flow.Tour) return "menu";
                return _active != null ? _active.Id : null;
            }
        }

        internal static string ActiveStepId
        {
            get
            {
                if (_preview != null) return _preview.Id;
                if (_card != CardCtx.None && _cardStep != null) return _cardStep.Id;
                if ((_flow == Flow.Tip || _flow == Flow.Invite) && _timedStep != null) return _timedStep.Id;
                var s = CurrentStep();
                return s != null ? s.Id : null;
            }
        }

        internal static TLessonState StateOf(string lessonId)
        {
            if (string.IsNullOrEmpty(lessonId)) return TLessonState.New;
            try { return TutorialProgressStore.GetState(lessonId); }
            catch { return TLessonState.New; }
        }

        /// <summary>The GUIDE chip text for a state (chrome keys state.*): NEW / IN PROGRESS / DONE /
        /// SKIPPED. Offered and Later read NEW; Learned reads DONE.</summary>
        internal static string StateLabel(TLessonState s)
        {
            string key;
            switch (s)
            {
                case TLessonState.Active: key = "chrome|state.progress"; break;
                case TLessonState.Done:
                case TLessonState.Learned: key = "chrome|state.done"; break;
                case TLessonState.Skipped: key = "chrome|state.skipped"; break;
                default: key = "chrome|state.new"; break;
            }
            return TutorialTokens.Resolve(TutorialTextStore.Get(key));
        }

        /// <summary>Can this lesson be tried live right now (its reason exists: a tool in hand for
        /// lesson 3, suit power for 14, none for 15...)? GUIDE's "Try it" and Watch's "Try it now".</summary>
        internal static bool ContextExists(string lessonId)
        {
            var l = TutorialChapters.FindLesson(lessonId);
            return l != null && ContextExists(l);
        }

        /// <summary>Play a lesson: <paramref name="watch"/> = as cards (paused single-player), else
        /// live. Returns false for an unknown lesson, outside a world, or live play of a lesson whose
        /// context is missing (the caller may fall back to watch). The switch happens next frame.
        /// Tour mode: live play of the lesson the first-run tour stands at (GUIDE's Continue) continues
        /// the TOUR there - its setup step gets the player into the situation, so no context check.</summary>
        internal static bool PlayLesson(string lessonId, bool watch)
        {
            try
            {
                var l = TutorialChapters.FindLesson(lessonId);
                if (l == null) return false;
                if (!Guards.CanDraw()) return false;
                if (StoodDown()) return false;   // uiareset: nothing runs until the restart
                if (!watch && !IsTourCurrent(l) && !ContextExists(l)) return false;
                _pendingPlay = l;
                _pendingPlayWatch = watch;
                return true;
            }
            catch (Exception e) { ReportTickError("play", e); return false; }
        }

        /// <summary>GUIDE "Skip this lesson": the running lesson (or tour / watch) ends as Skipped. With
        /// nothing running it skips the lesson GUIDE's Continue / Skip buttons name - the first one
        /// STORED Active (an interrupted First Steps nobody resumed: it would otherwise hold every other
        /// lesson back until its Welcome-back card, which multiplayer never shows). Tour mode: a lesson
        /// of the first-run tour ends Skipped and the tour CONTINUES with the next one (EndLesson /
        /// EndTour -> TourAfterLesson); skipping the lesson a paused tour stands at moves it on too.</summary>
        internal static void SkipCurrentLesson()
        {
            try
            {
                if (_flow == Flow.Tour) { EndTour(TLessonState.Skipped); return; }
                if (_flow == Flow.Watch) { EndWatch(); return; }
                if (_flow == Flow.Lesson && _active != null)
                {
                    // Lesson 3 in the tour: the track skipped (and the one it never reached) is unfinished -
                    // after the tour it may come back, a track already finished may not (TrackEligible).
                    if (_activeTour) AsideFrom(_active, _track);
                    EndLesson(TLessonState.Skipped);
                    return;
                }
                if (_flow == Flow.Tip || _flow == Flow.Invite || _flow == Flow.Notice) EndTimed();
                SkipStoredActive(true);
            }
            catch (Exception e) { ReportTickError("skip", e); }
        }

        /// <summary>GUIDE "Stop the tour": the first-run tour ENDS - running or paused - WITHOUT turning
        /// lessons off (contrast <see cref="StopAllLessons"/>). The lesson it stands at is set aside
        /// (Skipped; a running one stops now - lesson 16's callouts too, F10 stays open; First Steps gets
        /// its "as I go" twins), so it - like every tour lesson not reached or set aside - comes back
        /// just-in-time ONCE (<see cref="PostTourOfferable"/>); a finished one never does. A closing card
        /// that was due is dropped. What runs outside the tour (a GUIDE Try it / Watch, a tip) goes on.
        /// Safe no-op when there is no tour.</summary>
        internal static void StopTour()
        {
            try
            {
                if (StoodDown()) return;
                bool running = TutorialProgressStore.TourState == TTourState.Running;
                if (!running && !_tourEndDue && !_tourLive) return;
                float now = Time.unscaledTime;
                _tourEndDue = false;
                _remindPending = false;   // no Welcome back is owed for a tour that is over
                if (!running) { _tourLive = false; return; }
                // The tour's Hold-on card (modal - normally not reachable from GUIDE) goes with it.
                if (_card == CardCtx.Hold && _holdTour) { _holdTour = false; CloseCardCtx(); }
                bool lessonRuns = _flow == Flow.Lesson && _active != null && _activeTour;
                bool calloutsRun = _flow == Flow.Tour && _activeTour;
                // Ends the tour FIRST, so the lesson ending below does not move it on (TourAfterLesson
                // finds nothing current); settles a paused lesson (Skipped), marks lesson 3's unfinished
                // tracks (running or paused).
                TourEnd(true, now);
                if (lessonRuns) EndLesson(TLessonState.Skipped);
                else if (calloutsRun)
                {
                    // Not as the tour's lesson: EndTour would close F10 and move a tour on - the player is
                    // in F10 (the Guide), so it stays open.
                    _activeTour = false;
                    EndTour(TLessonState.Skipped);
                }
                _lastLessonEnd = now;   // a breather before anything pops up by itself
                TutorialProgressStore.Flush();
            }
            catch (Exception e) { ReportTickError("stoptour", e); }
        }

        /// <summary>Did the director close F10 itself this frame? The plugin's F10-key toggle (it runs
        /// after <see cref="Tick"/> in the same Update) must not re-open it: F10 pressed on a lesson-16
        /// callout of the first-run tour ends the callouts, and the tour closes F10 for lesson 17's strip
        /// in that very frame. Allocation-free.</summary>
        internal static bool MenuClosedThisFrame
        {
            get { return _f10ClosedByUsFrame == Time.frameCount; }
        }

        /// <summary>GUIDE "Stop all lessons": lessons off (<c>TutorialTips</c>) and whatever runs ends.
        /// The RUNNING lesson ends resumable (First Steps stays Active; a just-in-time lesson comes back
        /// as Offered). A lesson only STORED Active - an interrupted First Steps that is not running - ends
        /// as Skipped (its "as I go" twins may still fire once lessons are back on). Tour mode: the
        /// first-run tour ENDS here (first, so the lines below settle its lesson like any other).</summary>
        internal static void StopAllLessons()
        {
            try
            {
                // (A paused tour's lesson - stored Active, not running - ends Skipped, exactly as
                // SkipStoredActive below treats any stored-Active lesson; a running one is left to it.
                // TourEnd also marks lesson 3's unfinished tracks, running or paused.)
                TourEnd(true, Time.unscaledTime);
                SkipStoredActive(false);   // first: it tells the running lesson apart before StopEverything clears it
                SetTips(false);
                StopEverything(true);
            }
            catch (Exception e) { ReportTickError("stop", e); }
        }

        /// <summary><c>uiatutorial restart</c> / GUIDE "Restart from the beginning": progress cleared
        /// (text overrides kept), GuideShown/TutorialCompleted reset, and the Welcome card shows on the
        /// NEXT world entry (C.19's console line promises exactly that).</summary>
        internal static void Restart()
        {
            try
            {
                StopEverything(false);
                try { if (UIAConfig.GuideShown != null) UIAConfig.GuideShown.Value = false; } catch { }
                try { if (UIAConfig.TutorialCompleted != null) UIAConfig.TutorialCompleted.Value = false; } catch { }
                TutorialProgressStore.Clear();   // the first-run tour too: None - the Welcome offers it again
                _tourLive = _tourEndDue = _holdTour = false;
                _queueCount = 0;
                _inviteShown = false;
                _remindPending = false;
                _laterAtEntry = -1;
                _welcomeHoldForNextEntry = true;
                _coreEndedAt = -999f;
                ClearTipMoments();
            }
            catch (Exception e) { ReportTickError("restart", e); }
        }

        /// <summary>Turn just-in-time lessons on or off (<c>UIAConfig.TutorialTips</c>). Off also ends
        /// whatever started by itself (see <see cref="EndAutomatic"/>); the GUIDE toggle writes the
        /// config directly, so the tick catches that same ON->OFF edge too.</summary>
        internal static void SetTips(bool on)
        {
            try { if (UIAConfig.TutorialTips != null) UIAConfig.TutorialTips.Value = on; } catch { }
            if (!on)
            {
                try { EndAutomatic(); } catch (Exception e) { ReportTickError("tips", e); }
            }
            _tipsWasOn = on;
        }

        /// <summary>Editor: show this step's presentation now (strip, card or callout), with no
        /// progress writes and no done-wait. Pauses the running lesson until <see cref="StopPreview"/>.</summary>
        internal static bool PreviewStep(string stepId)
        {
            try
            {
                var step = TutorialChapters.FindStep(stepId);
                if (step == null || !Guards.CanDraw()) return false;
                if (StoodDown()) return false;
                if (_preview != null && _card == CardCtx.Preview && !IsStripKind(step))
                {
                    // One card preview to the next: swap it in place (the coach replaces an open card's
                    // content); what the first preview covered is still what StopPreview puts back.
                    _preview = step;
                    ShowPreview();
                    return true;
                }
                StopPreview();
                // Remember the card a card-preview is about to replace, so StopPreview can put it back.
                _cardBeforePreview = _card;
                _cardStepBeforePreview = _cardStep;
                _cardBodyBeforePreview = _cardBodyField;
                _preview = step;
                if (IsStripKind(step) && _stripShowing && !_confirming && !StripShows(step.Id))
                {
                    // A strip preview over another strip cross-fades in place: Show with a different
                    // step id drops that strip's chrome line, drain and any pending Then by itself, so
                    // only our mirrors and the old spots need clearing (no hide + fade-in flash).
                    _chromeShown = Chrome.None;
                    _drainShown = -2f;
                    SpotClear();
                }
                else HideStrip();   // same step on screen or a Then in flight: Hide drops its confirm callback
                ShowPreview();
                return true;
            }
            catch (Exception e) { ReportTickError("preview", e); return false; }
        }

        /// <summary>End the editor preview and put back what it covered. Idempotent and safe from any
        /// caller - the editor's close button, its self-heal when it finds itself closed, the coach's
        /// X on a preview card: a second call (or one after the world already ended the preview) is a
        /// no-op. No flash: a card the preview replaced is swapped back IN PLACE (the coach keeps the
        /// card and its pause), and the strip of a running lesson / tip cross-fades back to its own
        /// words instead of hiding and fading in again.</summary>
        internal static void StopPreview()
        {
            if (_preview == null) return;
            _preview = null;
            var prev = _cardBeforePreview;
            var prevStep = _cardStepBeforePreview;
            var prevBody = _cardBodyBeforePreview;
            _cardBeforePreview = CardCtx.None;
            _cardStepBeforePreview = null;
            _cardBodyBeforePreview = null;
            try
            {
                bool restoreCard = prev != CardCtx.None && prev != CardCtx.Preview;
                if (_card == CardCtx.Preview || (_card == CardCtx.None && restoreCard))
                {
                    if (restoreCard)
                    {
                        // Swap the covered card (Watch / tour / Welcome / lesson card...) back over the
                        // preview card: never close-then-reopen, which flashes the scrim and drops the
                        // single-player pause for a frame.
                        _card = prev;
                        _cardStep = prevStep;
                        _cardBodyField = prevBody;
                        ReopenCard();
                    }
                    else
                    {
                        _card = CardCtx.None;
                        _cardStep = null;
                        CoachClose();
                    }
                }
                RestoreStripAfterPreview();
            }
            catch (Exception e) { ReportTickError("preview", e); }
        }

        /// <summary>After a preview: the strip goes back to what runs underneath - in place - or hides.
        /// A lesson in its Then line (the preview cancelled that confirm) and anything suspended hide;
        /// the tick re-presents them when due.</summary>
        private static void RestoreStripAfterPreview()
        {
            if (!_stripShowing) return;   // a card preview: the strip was already down; the tick brings it back
            if (_card != CardCtx.None || Suspended()) { HideStrip(); return; }
            if (_flow == Flow.Lesson && !_confirming)
            {
                var step = CurrentStep();
                if (step != null && step.Kind != TPresentation.Card) { PresentStrip(step); return; }
            }
            else if (_flow == Flow.Tip || _flow == Flow.Invite || _flow == Flow.Notice)
            {
                SpotClear();   // the preview's spots; a timed strip has none
                _chromeShown = Chrome.None;
                _drainShown = -2f;
                ShowStripRaw(_timedId, _timedHeader, _timedText, _timedDemo);
                return;
            }
            HideStrip();
        }

        /// <summary>Editor: re-resolve the showing strip / card from the text store right now. A strip
        /// swaps its words IN PLACE (<c>TutorialStrip.UpdateText</c>: no cross-fade per keystroke, the
        /// demo, chrome line and spots stay as they are); a card is rebuilt (the coach swaps it in place).</summary>
        internal static void RefreshActiveText()
        {
            try
            {
                // In place only when the strip really shows that step: UpdateText with another step
                // on screen would Show with THAT step's demo - the full present path handles it instead.
                if (_preview != null)
                {
                    if (IsStripKind(_preview) && StripShows(_preview.Id))
                    {
                        string header;
                        string body = PreviewStripText(_preview, out header);
                        UpdateStripRaw(_preview.Id, header, body);
                    }
                    else ShowPreview();
                    return;
                }
                if (_card != CardCtx.None) { ReopenCard(); return; }
                if (_flow == Flow.Lesson && _stripShowing && !_confirming)
                {
                    var step = CurrentStep();
                    if (step != null)
                    {
                        _header = LessonHeader();
                        _stepRead = ReadSecondsFor(step);   // an edited S2 line reads for its new length
                        if (!StripShows(step.Id)) { PresentStrip(step); return; }
                        UpdateStripRaw(step.Id, _header, StripText(step));
                        // A chrome line on screen follows an edit of its own text too.
                        if (_chromeShown != Chrome.None)
                        {
                            var c = _chromeShown;
                            _chromeShown = Chrome.None;
                            SetChromeLine(c, step);
                        }
                    }
                    return;
                }
                if ((_flow == Flow.Tip || _flow == Flow.Invite || _flow == Flow.Notice) && _stripShowing)
                {
                    RebuildTimedText();
                    if (StripShows(_timedId)) UpdateStripRaw(_timedId, _timedHeader, _timedText);
                    else ShowStripRaw(_timedId, _timedHeader, _timedText, _timedDemo);
                }
            }
            catch (Exception e) { ReportTickError("refresh", e); }
        }

        /// <summary>The ToggleHudEditor gate (lesson 17): the first F9 open while lessons are on shows
        /// the Designer card (designer.card - not Steps[0], which is now the tour's setup strip)
        /// INSTEAD; its first button opens F9 for real. True = consumed.
        /// <para>Tour mode: during lesson 17's setup step THIS press is the step (TDone.DesignerKey) -
        /// consumed, because the card that follows offers "Open the Designer". With the first-run tour
        /// standing at lesson 17 between lessons, the press starts it (seeded: its setup is done). While
        /// the tour holds the lessons back, any other F9 opens the Designer as usual - the tour covers it
        /// in its turn. After the tour the card comes back once if the tour did not finish lesson 17.</para></summary>
        internal static bool InterceptDesignerOpen()
        {
            try
            {
                if (!_inWorld || !Guards.CanDraw()) return false;
                if (StoodDown() || _preview != null) return false;
                // Lesson 17's setup asks for this very key.
                var cur = _flow == Flow.Lesson ? CurrentStep() : null;
                if (cur != null && cur.Done == TDone.DesignerKey)
                {
                    if (_card != CardCtx.None || TutorialCoach.IsOpen) return false;
                    Ev(EvDesignerKey);
                    return true;
                }
                var l = TutorialChapters.FindLesson("designer");
                if (l == null) return false;
                // The tour's own turn for lesson 17, and nothing running: the press starts it.
                if (_tourLive && _flow == Flow.Idle && _card == CardCtx.None && _pendingPlay == null
                    && IsTourCurrent(l) && !TutorialCoach.IsOpen)
                {
                    TourStartCurrent(Time.unscaledTime, false, EvDesignerKey);
                    return _flow != Flow.Idle || _card != CardCtx.None;
                }
                if (!TipsOn()) return false;
                if (!AutoOfferable(l)) return false;
                if (_card != CardCtx.None) return false;
                if (TutorialCoach.IsOpen || HandbookViewer.IsOpen) return false;
                var card = FindStepIn(l, "designer.card");
                if (card == null) return false;
                OpenStepCard(CardCtx.Designer, card, null);
                if (_card != CardCtx.Designer) return false;
                NoteJitIfAfterTour(l);
                return true;
            }
            catch (Exception e) { ReportTickError("designer", e); return false; }
        }

        /// <summary>Log a tutorial failure once per source per session (the caller keeps going). The
        /// per-frame ticks report through <see cref="TickFailed"/>, which also counts them.</summary>
        internal static void ReportTickError(string source, Exception e)
        {
            try
            {
                if (_reported.Contains(source)) return;
                _reported.Add(source);
                UIALog.Error("Tutorial " + source + " failed (reported once per session): " + e);
            }
            catch { }
        }

        // ---- per-frame tick circuit breaker

        private static int TickSub(string sub)
        {
            if (sub == null) return -1;
            for (int i = 0; i < TickSubs.Length; i++)
                if (string.Equals(sub, TickSubs[i], StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Has the circuit breaker switched <paramref name="sub"/>'s per-frame tick off for this
        /// session? <c>"director"</c>, <c>"strip"</c> or <c>"spotlight"</c> - StationeersUIMod gates its
        /// three tutorial tick calls on it. Allocation-free (an F6 reload re-arms it).</summary>
        internal static bool TickDisabled(string sub)
        {
            int i = TickSub(sub);
            return i >= 0 && _tickOff[i];
        }

        /// <summary>A pumped tick returned normally: its run of consecutive failures starts over.</summary>
        internal static void TickSucceeded(string sub)
        {
            int i = TickSub(sub);
            if (i >= 0) _tickFails[i] = 0;
        }

        /// <summary>A pumped tick threw. The first failure is logged (once per source); after
        /// <see cref="TickBreakAfter"/> failing frames IN A ROW that tick is switched off for the rest of
        /// the session - one loud log line - and whatever it had on screen is taken down, so a tutorial
        /// broken by a game update costs nothing per frame and never spams the log. A strip that is off
        /// holds every lesson (<see cref="Suspended"/>): nothing is marked shown that could not be seen.</summary>
        internal static void TickFailed(string sub, Exception e)
        {
            ReportTickError(sub, e);
            int i = TickSub(sub);
            if (i < 0 || _tickOff[i]) return;
            if (++_tickFails[i] < TickBreakAfter) return;
            _tickOff[i] = true;
            try
            {
                UIALog.Error("Tutorial " + sub + " failed " + TickBreakAfter + " frames in a row - its per-frame tick is now OFF "
                    + "until the game restarts (or an F6 reload); the rest of UI Ascended is unaffected. Last error: " + e);
            }
            catch { }
            try
            {
                if (i == SubDirector) StopEverything(true);   // nothing half-shown; progress stays resumable
                else if (i == SubStrip) { _stripShowing = false; _chromeShown = Chrome.None; TutorialStrip.Hide(false); }
                else if (i == SubSpotlight) { _spotStep = null; _spotMissing = 0; _spotFallback = false; TutorialSpotlight.Clear(); }
            }
            catch { }
        }

        // ================================================================ tick

        /// <summary>Pumped every frame from StationeersUIMod.Update (where the first-run block used to
        /// be). Never throws.</summary>
        internal static void Tick()
        {
            if (_tickOff[SubDirector]) return;
            try { TickCore(); _tickFails[SubDirector] = 0; }
            catch (Exception e) { TickFailed("director", e); }
        }

        /// <summary>uiareset ran (<see cref="TutorialProgressStore.SuppressWritesUntilRestart"/>): the
        /// store answers from an empty state and never writes again, so nothing may start - acting on
        /// that empty state would pop the What's-new card, the Designer card, re-run the Welcome...</summary>
        private static bool StoodDown()
        {
            try { return TutorialProgressStore.WritesSuppressed; }
            catch { return false; }
        }

        private static void TickCore()
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - _lastTick, 0f, 0.25f);
            _lastTick = now;
            if (StoodDown())
            {
                // Take everything down once and stand still until the restart the reset asked for.
                if (!_stoodDown)
                {
                    _stoodDown = true;
                    StopEverything(false);
                    _queueCount = 0;
                }
                return;
            }
            if (!_init) Init(now);

            TrackWorld(now);
            if (!_inWorld) return;

            IngestSignals(now);
            ResolvePendingChild(now);
            ProcessCallbacks(now);
            DetectCardClosedElsewhere();

            if (now >= _nextPoll)
            {
                float pdt = Mathf.Clamp(now - _lastPoll, 0f, 1f);
                _lastPoll = now;
                _nextPoll = now + PollInterval;
                Poll(now, pdt);
            }

            if (_pendingOpenDesigner) { _pendingOpenDesigner = false; OpenDesignerNow(); }
            // (A pending play request - GUIDE's Try it / Watch, uiatutorial play - is taken in
            // ProcessCallbacks, before a card's deferred Later: see there.)
            CheckDropAttempt();

            if (_preview == null)
            {
                TickEntry(now);
                TickMenuEdge(now);
                TickTour(now);
                TickFlow(now, dt);
                if (now >= _nextSchedule)
                {
                    _nextSchedule = now + PollInterval;
                    CheckTipsSwitch();
                    Schedule(now);
                }
            }
            TutorialProgressStore.Tick();
        }

        private static void Init(float now)
        {
            _init = true;
            for (int i = 1; i < (int)TSignal.Count; i++) _seen[i] = TutorialSignals.Count((TSignal)i);
            for (int i = 0; i < _tipMoment.Length; i++) _tipMoment[i] = -1f;
            _lastPoll = now;
            TutorialProgressStore.Load();
            RepairStaleActive();
            _tipsWasOn = TipsOn();
            ReadCarry();
        }

        /// <summary>A lesson is Active only while it runs - except First Steps, which resumes. One left
        /// Active in Progress.xml (the game crashed or was killed mid-lesson, so no teardown made it
        /// Offered) would never be offered again and read IN PROGRESS forever: it gets what a clean
        /// interruption leaves - Offered (the F10 tour: New). Tour mode: the lesson a paused first-run
        /// tour stands at resumes too (Welcome back / GUIDE's Continue) - it stays Active.</summary>
        private static void RepairStaleActive()
        {
            var ls = TutorialChapters.Lessons;
            for (int i = 0; ls != null && i < ls.Length; i++)
            {
                var l = ls[i];
                if (l == null || string.IsNullOrEmpty(l.Id) || l.Id == "core") continue;
                if (IsTourCurrent(l)) continue;
                if (StateOf(l.Id) != TLessonState.Active) continue;
                TutorialProgressStore.SetState(l.Id, l.Id == "menu" ? TLessonState.New : TLessonState.Offered);
            }
        }

        /// <summary>Lessons were switched off (4 Hz): the GUIDE toggle writes <c>UIAConfig.TutorialTips</c>
        /// directly, so its ON->OFF edge is caught here as well as in <see cref="SetTips"/>.</summary>
        private static void CheckTipsSwitch()
        {
            bool on = TipsOn();
            if (_tipsWasOn && !on) EndAutomatic();
            _tipsWasOn = on;
        }

        /// <summary>Lessons off ("Off = no lessons pop up by themselves"): what started BY ITSELF ends
        /// now - a just-in-time lesson (shelved like any interruption: Offered, so it may come back once
        /// lessons are on again), a tip, the invitation strip, the F10 tour that opened itself. What the
        /// player started keeps going: First Steps from the Welcome, GUIDE's Try it / Watch, and the
        /// "Lessons are off" notice that confirms this very switch.</summary>
        private static void EndAutomatic()
        {
            _queueCount = 0;
            switch (_flow)
            {
                case Flow.Lesson:
                    if (_active != null && _activeAuto) ShelveLesson();
                    break;
                case Flow.Tip:
                case Flow.Invite:
                    EndTimed();
                    break;
                case Flow.Tour:
                    if (_tourAuto) EndTour(TLessonState.New);
                    break;
            }
        }

        // ---- hot reload: an F6 in the middle of a world session is not a new world entry

        private const string CarryKey = "StationeersUIMod.Tutorial.DirectorCarry";

        private static string CarryFloat(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        /// <summary>Shutdown (an F6 teardown, or quitting): park the per-entry and pacing state on the
        /// AppDomain - the one place that outlives the reloaded assembly - so the next assembly's first
        /// tick can tell "hot reload mid-world" from "a new world entry". Without it every F6 re-offered
        /// the Welcome-back card, re-asked a Later'd Welcome and restarted the lesson pacing. A plain
        /// string: a type from the old assembly would be foreign to the new one. Read (and cleared) once,
        /// at <see cref="Init"/>; a game restart starts a new AppDomain.</summary>
        private static void WriteCarry()
        {
            try
            {
                var ci = CultureInfo.InvariantCulture;
                int bits = (_inWorld ? 1 : 0) | (_resumeOfferedThisEntry ? 2 : 0) | (_whatsNewHandledThisEntry ? 4 : 0)
                    | (_welcomeHoldForNextEntry ? 8 : 0) | (_inviteShown ? 16 : 0) | (_remindPending ? 32 : 0)
                    | (_tourLive ? 64 : 0) | (_tourEndDue ? 128 : 0)
                    | (TutorialSafety.ChronicAccepted ? 256 : 0);
                string s = "1;" + bits.ToString(ci) + ";" + _entrySerial.ToString(ci) + ";" + _laterAtEntry.ToString(ci)
                    + ";" + _welcomeShownAtEntry.ToString(ci) + ";" + CarryFloat(_worldEnterTime) + ";" + CarryFloat(_remindCalm)
                    + ";" + CarryFloat(_coreEndedAt) + ";" + CarryFloat(_lastLessonEnd) + ";" + CarryFloat(_lastTipEnd)
                    + ";" + CarryFloat(_playSeconds);
                AppDomain.CurrentDomain.SetData(CarryKey, s);
            }
            catch { }
        }

        /// <summary>Init: take the carry the previous assembly parked (<see cref="WriteCarry"/>). The MP
        /// invitation stays once per game session either way; the rest is restored only when that
        /// assembly was IN a world and the world is still running now - the same world entry goes on:
        /// no OnWorldEnter, the Welcome back already offered this entry stays offered.</summary>
        private static void ReadCarry()
        {
            string s = null;
            try
            {
                s = AppDomain.CurrentDomain.GetData(CarryKey) as string;
                if (s != null) AppDomain.CurrentDomain.SetData(CarryKey, null);
            }
            catch { s = null; }
            if (string.IsNullOrEmpty(s)) return;
            try
            {
                var p = s.Split(';');
                if (p.Length != 11 || p[0] != "1") return;
                var ci = CultureInfo.InvariantCulture;
                int bits, serial, laterAt, shownAt;
                float enter, remind, coreEnd, lessonEnd, tipEnd, play;
                if (!int.TryParse(p[1], NumberStyles.Integer, ci, out bits)
                    || !int.TryParse(p[2], NumberStyles.Integer, ci, out serial)
                    || !int.TryParse(p[3], NumberStyles.Integer, ci, out laterAt)
                    || !int.TryParse(p[4], NumberStyles.Integer, ci, out shownAt)
                    || !float.TryParse(p[5], NumberStyles.Float, ci, out enter)
                    || !float.TryParse(p[6], NumberStyles.Float, ci, out remind)
                    || !float.TryParse(p[7], NumberStyles.Float, ci, out coreEnd)
                    || !float.TryParse(p[8], NumberStyles.Float, ci, out lessonEnd)
                    || !float.TryParse(p[9], NumberStyles.Float, ci, out tipEnd)
                    || !float.TryParse(p[10], NumberStyles.Float, ci, out play))
                    return;
                _inviteShown = (bits & 16) != 0;
                // The Hold-on card's "Start anyway" lasts the game session, an F6 included (like the invite).
                if ((bits & 256) != 0) TutorialSafety.AcceptChronic();
                if ((bits & 1) == 0 || !WorldRunning()) return;
                _inWorld = true;   // TrackWorld: no OnWorldEnter - this is the same world entry
                _entrySerial = serial;
                _laterAtEntry = laterAt;
                _welcomeShownAtEntry = shownAt;
                _resumeOfferedThisEntry = (bits & 2) != 0;
                _whatsNewHandledThisEntry = (bits & 4) != 0;
                _welcomeHoldForNextEntry = (bits & 8) != 0;
                _remindPending = (bits & 32) != 0;
                // Tour mode: a first-run tour running at the F6 goes on with the lesson it was in (its
                // step is in Progress.xml); a closing card that was waiting still shows.
                _tourLive = (bits & 64) != 0;
                _tourEndDue = (bits & 128) != 0;
                _worldEnterTime = enter;
                _remindCalm = remind;
                _coreEndedAt = coreEnd;
                _lastLessonEnd = lessonEnd;
                _lastTipEnd = tipEnd;
                _playSeconds = play;
                // Edge trackers start from what is on screen now, so an F10 / window already open is
                // not mistaken for the player opening it this frame.
                try { _gridWasOpen = TheGridPanel.IsOpen; } catch { }
                try { _f10WasOpen = UiaControlCenter.IsOpen; } catch { }
            }
            catch { }
        }

        // ================================================================ world

        private static bool WorldRunning()
        {
            try
            {
                if (GameManager.IsBatchMode) return false;
                if (GameManager.GameState != GameState.Running) return false;
                if (ImGuiLoadingScreen.IsShowing) return false;
                return true;
            }
            catch { return false; }
        }

        private static void TrackWorld(float now)
        {
            bool running = WorldRunning();
            if (running && !_inWorld) OnWorldEnter(now);
            else if (!running && _inWorld) OnWorldExit();
            _inWorld = running;
        }

        private static void OnWorldEnter(float now)
        {
            _entrySerial++;
            _worldEnterTime = now;
            _resumeOfferedThisEntry = false;
            _whatsNewHandledThisEntry = false;
            _welcomeHoldForNextEntry = false;
            _gateSince = -1f;
            _lastLessonEnd = -999f;
            _lastTipEnd = -999f;
            _gridWasOpen = false;
            _f10WasOpen = false;
            // A first-run tour left Running is PAUSED at a new entry: the Welcome-back card offers it.
            _tourLive = false;
            _tourEndDue = false;
            _hubDepth = 0;
        }

        /// <summary>The world went away (menu, load, disconnect): leave nothing on screen and nothing
        /// half-owned; an interrupted First Steps stays Active (the Welcome-back card resumes it), a
        /// just-in-time lesson becomes Offered (it comes back later). Tour mode: the first-run tour
        /// pauses at its lesson and step (still Active - the Welcome back continues it there).</summary>
        private static void OnWorldExit()
        {
            StopEverything(true);
            _tourLive = false;
            _tourEndDue = false;   // the closing card is a summary: not worth a card on the next entry
            _holdTour = false;
            _hubDepth = 0;
            _queueCount = 0;
            ClearTipMoments();
            _lastActiveHand = null;
            _stowItem = null;
            _gateSince = -1f;
            _childPendingFrame = -1;
            _satFromStack = _heldStackWheel = false;
            try { TutorialProgressStore.Flush(); } catch { }
        }

        /// <summary>End whatever runs. <paramref name="keepResumable"/>: the core stays Active and a
        /// just-in-time lesson becomes Offered; otherwise the lesson state is left as it was. Tour mode:
        /// the lesson the first-run tour stands at stays Active either way - it resumes there (the
        /// session's _tourLive decides whether that is now - after a Watch / Try it - or at the Welcome back).</summary>
        private static void StopEverything(bool keepResumable)
        {
            if (_flow == Flow.Lesson && _active != null)
            {
                if (keepResumable && _active.Id != "core" && !IsTourCurrent(_active))
                {
                    var st = StateOf(_active.Id);
                    if (st == TLessonState.Active) TutorialProgressStore.SetState(_active.Id, TLessonState.Offered);
                }
            }
            if (_flow == Flow.Tour && StateOf("menu") == TLessonState.Active
                && !IsTourCurrent(TutorialChapters.FindLesson("menu")))
                TutorialProgressStore.SetState("menu", TLessonState.New);
            ClearFlow();
            HideStrip();
            SpotClear();
            if (_card != CardCtx.None) { _card = CardCtx.None; CoachClose(); }
            _cardStep = null;
            _holdTour = false;   // a Hold-on card of the tour went with it
            _pendingLater = false;
            _pendingConfirmDone = false;
            _preview = null;
            _cardBeforePreview = CardCtx.None;   // nothing left for a later StopPreview to put back
            _cardStepBeforePreview = null;
            _cardBodyBeforePreview = null;
        }

        private static void ClearFlow()
        {
            _flow = Flow.Idle;
            _active = null;
            _activeAuto = false;
            _tourAuto = false;
            _activeTour = false;
            _asideTracks = 0;
            _doStepsAtTrack = _doDoneAtTrack = 0;
            _tourMaxPage = -1;
            _setupOk = false;
            _setupStackFor = 0f;
            _track = 0;
            _stepIdx = 0;
            _confirming = false;
            _onHold = false;
            _holdWhy = null;
            _windowWaitClose = false;
            _windowOpen = false;
            _windowHeld = null;
            _stowItem = null;
            _timedStep = null;
            _watchLesson = null;
            _branchField = null;
            _oopsField = null;
        }

        // ================================================================ signals

        private static void IngestSignals(float now)
        {
            for (int i = 1; i < (int)TSignal.Count; i++)
            {
                var s = (TSignal)i;
                int c = TutorialSignals.Count(s);
                if (c == _seen[i]) continue;
                int n = c - _seen[i];
                if (n <= 0) n = 1;   // the bus was reset under us - count it once
                _seen[i] = c;
                OnSignal(s, TutorialSignals.Arg(s), n, now);
            }
        }

        private static void Ev(ulong bit)
        {
            if (_confirming) _evPending |= bit;
            else _ev |= bit;
        }

        private static bool Has(ulong bits) { return (_ev & bits) != 0; }

        private static void OnSignal(TSignal s, int arg, int n, float now)
        {
            int kind = arg & 0xFF;
            int flags = arg >> 8;
            var step = _flow == Flow.Lesson ? CurrentStep() : null;
            switch (s)
            {
                case TSignal.WheelOpened:
                {
                    _wheelKind = (TWheelKind)kind;
                    _wheelSticky = (flags & 1) != 0;
                    _childSinceOpen = false;
                    _satFromStack = false;
                    _heldStackWheel = false;
                    _compChild = false;
                    _hubDepth = 0;
                    _wheelSerial++;
                    if (_wheelKind == TWheelKind.HeldTool)
                    {
                        var hs = Held() as Stackable;
                        try { _heldStackWheel = hs != null && hs.Quantity > 1; } catch { _heldStackWheel = false; }
                    }
                    TutorialProgressStore.Bump("wheels");
                    if (CoreSatisfied()) TutorialProgressStore.Bump("wheelsAfterCore");
                    TipMoment(2, now);
                    if (_wheelKind == TWheelKind.Belt)
                    {
                        Ev(_wheelSticky ? EvBeltSticky : EvBeltHold);
                        if (_wheelSticky) TutorialProgressStore.Bump("beltTaps");
                        if (!_wheelSticky && step != null && step.Id == "core.beltopen") ShowOops("oops@held", now);
                    }
                    if (_wheelKind == TWheelKind.HeldTool) Ev(EvToolWheel);
                    if (_wheelKind == TWheelKind.Gear)
                    {
                        if (_wheelSticky) SetEvidence("ev:geartap");
                        TriggerIf("gear", 0, 0);
                        if (IsSuitWheelOpen()) TriggerIf("settings", 0, 0);
                    }
                    // (A WheelOpened never carries TWheelKind.Hub - RadialController.ActiveWheelKind has no
                    // Hub; a dive into THE HUB arrives as ChildWheelOpened(TWedgeKind.Hub), see OnDive.)
                    if (_wheelKind == TWheelKind.Bag || _wheelKind == TWheelKind.BoundBag)
                    {
                        if (_wheelKind != TWheelKind.BoundBag) TriggerIf("bags", 0, 0);
                        CountBagWheel();
                    }
                    if ((_wheelKind == TWheelKind.Belt || _wheelKind == TWheelKind.Gear) && IsToolbeltRingOpen()
                        && WantsTrigger("belts") && CarriesAnotherBelt())
                        TriggerIf("belts", 0, 0);
                    if (TutorialProgressStore.Counter("wheelsAfterCore") >= LessonWheelsAfterCore) TriggerIf("readwheel", 0, 0);
                    if (TutorialProgressStore.Counter("beltTaps") >= LessonBeltTaps) TriggerIf("speed", 0, 0);
                    break;
                }
                case TSignal.WheelClosed:
                {
                    Ev(EvWheelClosed);
                    _hubDepth = 0;
                    // (_satFromStack / _heldStackWheel are NOT cleared here: signals are read in enum
                    // order, so a close-after-action is seen before the same frame's WedgeCommitted.
                    // The next WheelOpened resets them.)
                    // Only Guard / OpenerKey / Esc / Switch are told apart (RMB, MMB, the close band and
                    // close-after-action all arrive as Other). A Guard close (pause, menu, stand-down) or
                    // a Switch to another wheel is not the player stepping back out of THE HUB.
                    var route = (TCloseRoute)kind;
                    if (step != null && step.Done == TDone.HubBack && _flagA
                        && route != TCloseRoute.Guard && route != TCloseRoute.Switch)
                        _flagB = true;
                    break;
                }
                case TSignal.WedgeHovered:
                    if (_lastHoverAt >= 0f && now - _lastHoverAt >= 0.4f) _hoverDwells++;
                    _lastHoverAt = now;
                    if ((flags & 2) != 0) Ev(EvGreyHovered);
                    if ((flags & 1) != 0) _arrowSeenThisStep = true;   // 1.7's branch: an arrowed wedge was met
                    break;
                case TSignal.WedgeCommitted:
                {
                    var wk = (TWedgeKind)kind;
                    bool open = wk == TWedgeKind.Open || wk == TWedgeKind.Hub;
                    if (!open && wk != TWedgeKind.Close) Ev(EvAnyCommit);
                    if (wk == TWedgeKind.Take) Ev(EvTakeCommit);
                    if (wk == TWedgeKind.Swap) Ev(EvSwapCommit);
                    if (wk == TWedgeKind.Stow) Ev(EvStowCommit);
                    if (open) Ev(EvOpenCommit);
                    if (wk == TWedgeKind.Setting)
                    {
                        Ev(EvSettingCommit);
                        if (TutorialProgressStore.Bump("settingClicks") >= LessonSettingClicks) TriggerIf("settings", 0, 0);
                    }
                    if (!_wheelSticky && wk != TWedgeKind.Close)
                    {
                        Ev(EvHoldCommit);
                        if (TutorialProgressStore.Bump("holdCommits") >= 3) Learn("speed");
                    }
                    // Lesson 3's evidence: a REPLACE (Swap) or an INSTALL - "Install"/"Insert" classify as
                    // Stow - committed in a child ring of a tool wheel. The kind is read BEFORE the action
                    // runs (RadialMenu.Execute), so a plain take reads Take - but a take with a BUSY hand
                    // honestly reads Swap too (the held item swaps into the source slot). In the HELD
                    // tool's wheel that cannot happen (its own parts only ever take), so any child ring
                    // will do there. A GEAR wheel's child ring can be a bag level, where that busy-hand
                    // take is an everyday Swap: there it only counts after a slide-out off a component
                    // socket (the suit's battery / tank -> REPLACE -> pick). Stow only counts in the held
                    // tool's wheel: a gear wheel's child ring can be a bag level full of stow wedges.
                    if ((wk == TWedgeKind.Swap && _wheelKind == TWheelKind.HeldTool && _childSinceOpen)
                        || (wk == TWedgeKind.Swap && _wheelKind == TWheelKind.Gear && _compChild)
                        || (wk == TWedgeKind.Stow && _wheelKind == TWheelKind.HeldTool && _childSinceOpen))
                        SetEvidence("ev:replace");
                    // Lesson 4's evidence from the wheel ("any split commit"): a commit on a stack's split
                    // choices - Split one / half read Take, Split count (a scroll wedge) reads Value -
                    // inside the satellite that slid out of a stack wedge (still on screen at the click:
                    // the ring's last published geometry), or at the root of the held stack's own wheel.
                    if (wk == TWedgeKind.Take || wk == TWedgeKind.Value)
                    {
                        bool inStackSat = false;
                        if (_satFromStack)
                        {
                            try { inStackSat = Overlay.RadialHintContext.SatelliteShown && Overlay.RadialHintContext.GeometryFresh; }
                            catch { inStackSat = false; }
                        }
                        if (inStackSat || (_heldStackWheel && !_childSinceOpen)) SetEvidence("ev:split");
                    }
                    if (wk == TWedgeKind.Take || wk == TWedgeKind.Swap) TipMoment(3, now);
                    if (step != null && step.Id == "tools.careful" && wk == TWedgeKind.Take) ShowOops("oops@took", now);
                    break;
                }
                case TSignal.ChildWheelOpened:
                    OnChildWheel((TWedgeKind)kind, flags, now);
                    break;
                case TSignal.BackedOut:
                    Ev(EvBackedOut);
                    if (_hubDepth > 0) _hubDepth -= Mathf.Min(n, _hubDepth);   // one level up per back-out
                    if (step != null && step.Done == TDone.HubBack && _flagA) _flagB = true;
                    break;
                case TSignal.ChipParked:
                    Ev(EvChipParked);
                    TriggerIf("dragdrop", EvChipParked, 0);
                    break;
                case TSignal.ChipDroppedOnTarget:
                case TSignal.ChipsDroppedOnClose:
                    Ev(EvChipDropped);
                    SetEvidence("ev:drop");
                    break;
                case TSignal.WorldReachGrab: Ev(EvWorldGrab); break;
                case TSignal.WorldSlotCueShown: Ev(EvWorldCue); break;
                case TSignal.HubMoved: Ev(EvHubMoved); break;
                case TSignal.KeepOpenUsed: Ev(EvKeepOpen); break;
                case TSignal.ValueScrolled:
                    Ev(EvValueScrolled);
                    TriggerIf("settings", EvValueScrolled, 0);
                    break;
                case TSignal.GreyClicked: TriggerIf("readwheel", 0, 0); break;
                case TSignal.HotkeyBound: Ev(EvHotkeyBound); SetEvidence("ev:hotkey"); break;
                case TSignal.BagBound: Ev(EvBagBound); SetEvidence("ev:bagbound"); break;
                case TSignal.InWheelDigitJump: Ev(EvDigitJump); break;
                case TSignal.GearHeld: Ev(EvGearHeld); SetEvidence("ev:gearhold"); break;
                case TSignal.BeltPickerOpened: Ev(EvBeltPicker); break;
                case TSignal.BeltSwapped: Ev(EvBeltSwapped); SetEvidence("ev:beltswap"); break;
                case TSignal.BeltPickerBack: Ev(EvBeltBack); break;
                case TSignal.SearchTook: Ev(EvSearchTook); SetEvidence("ev:searchtook"); break;
                case TSignal.SmartStowed:
                    if (arg == 1)
                    {
                        Ev(EvStowOk);
                        if (TutorialProgressStore.Bump("stowOk") >= LessonStowCount) TriggerIf("stow", 0, 0);
                    }
                    break;
                case TSignal.GridTabToggled: Ev(EvGridTab); break;
                case TSignal.GridCellTaken: Ev(EvGridTake); break;
                case TSignal.GridItemWheelOpened: Ev(EvGridItemWheel); break;
                case TSignal.GridDragMoved: Ev(EvGridDrag); SetEvidence("ev:griddrag"); break;
                case TSignal.GridShiftDragMoved: Ev(EvGridShiftDrag); SetEvidence("ev:griddrag"); break;
                case TSignal.GridStackPopupOpened: TriggerIf("split", 0, 0); break;
                case TSignal.GridSplitDone: Ev(EvGridSplit); SetEvidence("ev:split"); break;
                case TSignal.DeviceWindowOpened: Ev(EvDeviceWindow); break;
                case TSignal.PinCreated: Ev(EvPin); SetEvidence("ev:pin"); break;
                case TSignal.InGridOpened: Ev(EvInGrid); break;
                case TSignal.ScrollHighlightMoved:
                case TSignal.KeyboardTake: Ev(EvGridKeyboard); break;
                case TSignal.VitalsTooltipShown: Ev(EvVitals); break;
                case TSignal.HandSwapped: Ev(EvHandSwapped); break;
            }
        }

        // ---- child rings: a branch DIVE vs a slide-out SATELLITE

        /// <summary>RadialMenu raises ChildWheelOpened for a branch DIVE (PushBranch: THE HUB, a bag
        /// level, the REPLACE list) and for a slide-out SATELLITE (OpenSatellite), each classified by
        /// the wedge's own click. A dive always reads Open (every branch's ClickVerb is "Open") or Hub;
        /// a satellite reads its source wedge's click - Take / Swap / ... - EXCEPT a bag wedge's "More"
        /// slide-out, whose click is Open too (BagRadialFeature / ItemMenuBuilder nested-bag entries).
        /// So Open alone is ambiguous: it is settled a frame later from the ring's own published
        /// geometry (<c>RadialHintContext.SatelliteShown</c>, written by UnityRadialView.Render in the
        /// same Draw that opens a satellite). Explicit flag bits, if a raise site adds them, win:
        /// bit 0 = satellite, bit 1 = dive.</summary>
        private static void OnChildWheel(TWedgeKind ck, int flags, float now)
        {
            _childSinceOpen = true;
            if ((flags & 1) != 0) { OnSatellite(); return; }
            if ((flags & 2) != 0 || ck == TWedgeKind.Hub) { OnDive(ck, now); return; }
            if (ck == TWedgeKind.Open)
            {
                if (_childPendingFrame >= 0) ResolvePendingChild(now, true);   // two in a row: settle the first now
                _childPendingFrame = TutorialSignals.Frame(TSignal.ChildWheelOpened);
                if (_childPendingFrame < 0) _childPendingFrame = Time.frameCount;
                return;
            }
            OnSatellite();
        }

        /// <summary>Per frame, allocation-free: settle an ambiguous (Open) child ring once the ring's
        /// Draw of the raise frame has published. A wheel that closed meanwhile publishes nothing -
        /// after a few frames it counts as a dive (the pre-0.9.8.0 reading of Open).</summary>
        private static void ResolvePendingChild(float now, bool force = false)
        {
            int raised = _childPendingFrame;
            if (raised < 0) return;
            bool fresh = false, sat = false;
            try
            {
                fresh = Overlay.RadialHintContext.GeometryFrame >= raised;
                sat = Overlay.RadialHintContext.SatelliteShown;
            }
            catch { fresh = false; }
            int f = Time.frameCount;
            if (!force && (f <= raised || !fresh) && f - raised < 4) return;
            _childPendingFrame = -1;
            if (fresh && sat && f > raised) OnSatellite();
            else OnDive(TWedgeKind.Open, now);
        }

        private static void OnDive(TWedgeKind ck, float now)
        {
            var step = _flow == Flow.Lesson ? CurrentStep() : null;
            _satFromStack = false;   // a new level replaced the ring (and any satellite)
            Ev(EvBranchDive);
            TipMoment(4, now);
            // Lesson 8's setup (BagWheelOpen): THE HUB is depth 1; a bag level inside it one deeper.
            if (ck == TWedgeKind.Hub) _hubDepth = 1;
            else if (_hubDepth > 0) _hubDepth++;
            if (ck == TWedgeKind.Hub)
            {
                Ev(EvHubDive);
                // THE HUB is the bag root ([Tab] opens the same place, 8.1): lesson 8's "first dive into
                // THE HUB after the core" and lesson 13's bag-wheel count both take it.
                if (CoreSatisfied()) TriggerIf("bags", 0, 0);
                CountBagWheel();
            }
            if (step != null)
            {
                if (step.Id == "core.take" && ck == TWedgeKind.Hub && _branchField == null) ShowOops("oops@hub", now);
                if (step.Done == TDone.HubBack) _flagA = true;
            }
        }

        private static void OnSatellite()
        {
            Ev(EvChildWheel);
            // A stack wedge's slide-out IS its split level (ItemMenuBuilder.BuildComponentSatellite /
            // BuildManageEntries -> BuildSplitLevel). The pointer still rests on the source wedge: it
            // stopped past that wedge's edge to open the satellite, and the satellite's own hover is
            // not resolved until the ring's next Draw. On the open edge only.
            var src = HoveredWedge();
            _satFromStack = IsStackWedge(src);
            // A component socket's slide-out (Take / REPLACE ...): lesson 3's gear-wheel evidence.
            try { if (src != null && src.DeviceSlotStyle) _compChild = true; } catch { }
        }

        /// <summary>A bag wheel opened (the [Tab] wheel, a bound bag, or a dive into THE HUB): lesson 13
        /// counts them and, from the third on, looks for two carried bags sharing a name (an inventory
        /// walk - on this open edge only, never per frame).</summary>
        private static void CountBagWheel()
        {
            int bags = TutorialProgressStore.Bump("bagWheels");
            if (bags >= LessonBagWheels && WantsTrigger("names") && DuplicateBagNames()) TriggerIf("names", 0, 0);
        }

        // ================================================================ skill evidence (plan A.7)

        private static void SetEvidence(string flag)
        {
            if (!TutorialProgressStore.Flag(flag)) TutorialProgressStore.SetFlag(flag, true);
            CheckSkills();
        }

        private static void CheckSkills()
        {
            if (TutorialProgressStore.Flag("ev:replace")) Learn("tools");
            if (TutorialProgressStore.Flag("ev:split")) Learn("split");
            if (TutorialProgressStore.Flag("ev:drop")) Learn("dragdrop");
            if (TutorialProgressStore.Flag("ev:geartap") && TutorialProgressStore.Flag("ev:gearhold")) Learn("gear");
            if (TutorialProgressStore.Flag("ev:beltswap")) Learn("belts");
            if (TutorialProgressStore.Flag("ev:searchtook") && TutorialProgressStore.Flag("ev:bagbound")) Learn("bags");
            if (TutorialProgressStore.Flag("ev:hotkey")) Learn("settings");
            if (TutorialProgressStore.Flag("ev:pin") && TutorialProgressStore.Flag("ev:griddrag")) Learn("window");
        }

        /// <summary>Mark a lesson Learned - only from New/Offered, and never the one running right now
        /// (evidence during a lesson is the lesson working).</summary>
        private static void Learn(string lessonId)
        {
            if (_active != null && _active.Id == lessonId) return;
            var st = StateOf(lessonId);
            if (st != TLessonState.New && st != TLessonState.Offered) return;
            TutorialProgressStore.SetState(lessonId, TLessonState.Learned);
            DropQueued(lessonId);
        }

        // ================================================================ polls (4 Hz)

        private static void Poll(float now, float pdt)
        {
            _calmNow = TutorialSafety.IsCalm();
            if (!_calmNow)
            {
                // The on-hold line names the reason (a constant / catalog word: no allocation here).
                bool acute;
                string why = TutorialSafety.HoldReason(out acute);
                if (why != null) { _holdWhy = why; _holdAcute = acute; }
            }

            Slot active = null;
            try { active = InventoryManager.ActiveHandSlot; } catch { }
            if (active != null && _lastActiveHand != null && !ReferenceEquals(active, _lastActiveHand)) Ev(EvHandSwapped);
            _lastActiveHand = active;
            DynamicThing held = null;
            try { held = active != null ? active.Get() : null; } catch { }

            bool playing = false;
            try { playing = Guards.CanDraw() && !WorldManager.IsGamePaused; } catch { }
            if (playing)
            {
                _playSeconds += pdt;
                if (_remindPending && _calmNow) _remindCalm += pdt;
            }

            PollGrid(pdt);
            PollVanillaMenu(now);
            RetrySpots();
            PollSetup(pdt, held);   // a running lesson's setup state: whether or not lessons pop up by themselves

            if (!TipsOn()) return;
            PollTools(held, pdt);
            PollTier(now, pdt);
            PollHover(now, pdt);
        }

        private static void PollGrid(float pdt)
        {
            bool open = false, inter = false;
            try { open = TheGridPanel.IsOpen; inter = TheGridPanel.IsInteractive; } catch { }
            if (open && !_gridWasOpen)
            {
                _gridFreedSinceOpen = false;
                TutorialProgressStore.Bump("gridOpens");
            }
            _gridWasOpen = open;
            if (inter) _gridFreedSinceOpen = true;
            _gridInteractiveFor = inter ? _gridInteractiveFor + pdt : 0f;
            _gridLockedFor = open && !_gridFreedSinceOpen ? _gridLockedFor + pdt : 0f;
            if (_gridLockedFor >= 8f) TipMoment(7, Time.unscaledTime);
            if (_gridInteractiveFor >= 5f && TipsOn()) TriggerIf("window", 0, 0);
        }

        /// <summary>1.8's oops: Esc pressed while the window is open opens the game menu - once that
        /// menu closes again, the strip says which key closes the window.</summary>
        private static void PollVanillaMenu(float now)
        {
            bool up = false;
            try { up = Guards.VanillaMenuWantsFront(); } catch { }
            var step = _flow == Flow.Lesson ? CurrentStep() : null;
            if (up && !_vanillaMenuWasUp && step != null && step.Id == "core.window" && _gridWasOpen) _escOopsArmed = true;
            if (!up && _vanillaMenuWasUp && _escOopsArmed)
            {
                _escOopsArmed = false;
                if (step != null && step.Id == "core.window") ShowOops("oops@esc", now);
            }
            _vanillaMenuWasUp = up;
        }

        private static void PollTools(DynamicThing held, float pdt)
        {
            if (!WantsTrigger("tools")) { _tabletFor = _lowFor = _toolFor = 0f; return; }
            if (held is Tablet)
            {
                _tabletFor += pdt;
                if (_tabletFor >= 3f) TriggerIf("tools", 0, 1);
            }
            else _tabletFor = 0f;

            bool low = false;
            if (!(held is Tablet))
            {
                var pt = held as PowerTool;
                if (pt != null)
                {
                    try { var bat = pt.Battery; low = bat != null && bat.CurrentPowerPercentage <= 20; } catch { low = false; }
                }
            }
            _lowFor = low ? _lowFor + pdt : 0f;
            if (_lowFor >= 1f) TriggerIf("tools", 0, 0);

            bool tool = IsSocketTool(held) && !(held is Tablet);
            _toolFor = tool ? _toolFor + pdt : 0f;
            if (_toolFor >= 3f) TriggerIf("tools", 0, 0);
        }

        /// <summary>Is the Visor HUD half on? Lessons 14 / 15 talk about the visor - never without it.</summary>
        private static bool VisorOn()
        {
            try { return HudConfig.VisorHudEnabled == null || HudConfig.VisorHudEnabled.Value; }
            catch { return true; }
        }

        private static void PollTier(float now, float pdt)
        {
            var snap = VisorOn() ? TutorialSafety.Snapshot() : null;
            if (snap == null) { _suitedFor = _bareFor = 0f; return; }
            bool suited = snap.Tier == HudTier.Suited || snap.Tier == HudTier.Robot;
            bool bare = snap.Tier == HudTier.Bare && !snap.IsRobot;
            _suitedFor = suited ? _suitedFor + pdt : 0f;
            _bareFor = bare ? _bareFor + pdt : 0f;
            if (_suitedFor >= LessonVisorTier && CoreEndedLongAgo(now)) TriggerIf("visor", 0, 0);
            if (_bareFor >= LessonSensesTier) TriggerIf("senses", 0, 0);
        }

        /// <summary>What the pointer RESTS on in an open wheel (the hover signal only reports changes):
        /// lesson 4 fires after 0.5 s on a stack, tip 5 after 0.8 s on a wedge with an arrow.</summary>
        private static void PollHover(float now, float pdt)
        {
            var e = HoveredWedge();
            bool arrow = false;
            try { arrow = e != null && e.HasSlideOut; } catch { arrow = false; }
            bool stack = IsStackWedge(e);
            _stackHoverFor = stack ? _stackHoverFor + pdt : 0f;
            _arrowHoverFor = arrow ? _arrowHoverFor + pdt : 0f;
            if (_stackHoverFor >= 0.5f && WantsTrigger("split")) TriggerIf("split", 0, 0);
            if (_arrowHoverFor >= 0.8f) TipMoment(5, now);
        }

        /// <summary>The wedge under the pointer in the open wheel (satellite first, then the main
        /// ring), or null. Read-only; called at 4 Hz and on a satellite's open edge, never per frame.</summary>
        private static Overlay.RadialEntry HoveredWedge()
        {
            if (!RadialController.AnyRadialOpen) return null;
            try
            {
                var c = RadialController.Active;
                return c != null ? c._menu.HoveredEntry() : null;
            }
            catch { return null; }
        }

        /// <summary>Does this wedge hold a stack of 2+ (something that splits)?</summary>
        private static bool IsStackWedge(Overlay.RadialEntry e)
        {
            try
            {
                var st = e != null && e.DragSource != null ? e.DragSource.Occupant as Stackable : null;
                return st != null && st.Quantity > 1;
            }
            catch { return false; }
        }

        /// <summary>Lesson 5's second trigger (Dipole, 08-08): the vanilla Drop key pressed with a wheel
        /// open where it has nothing to page and no belt picker to open - the player was probably trying
        /// to drop something. Reads the key only (never consumes it); per frame, allocation-free.</summary>
        private static void CheckDropAttempt()
        {
            if (!RadialController.AnyRadialOpen) return;
            KeyCode drop;
            try { drop = KeyManager.GetKey("Drop"); } catch { return; }
            if (drop == KeyCode.None || !Input.GetKeyDown(drop)) return;
            if (!WantsTrigger("dragdrop")) return;
            try
            {
                if (!Overlay.RadialHintContext.InteractionFresh || Overlay.RadialHintContext.Pageable
                    || Overlay.RadialHintContext.Search) return;
            }
            catch { return; }
            if (TutorialSignals.Frame(TSignal.BeltPickerOpened) >= Time.frameCount - 1) return;
            TriggerIf("dragdrop", 0, 0);
        }

        // ================================================================ setup states (tour mode)

        /// <summary>Is <paramref name="d"/> one of the setup STATES (TDone's last block, bar the
        /// DesignerKey event)? Those are read at 4 Hz by <see cref="PollSetup"/>, never per frame.</summary>
        private static bool IsSetupState(TDone d)
        {
            switch (d)
            {
                case TDone.WheelOpen:
                case TDone.HoldingPartsTool:
                case TDone.HoldingTablet:
                case TDone.StackHovered:
                case TDone.BeltWheelWithSpare:
                case TDone.BagWheelOpen:
                case TDone.SettingsWheelOpen:
                case TDone.SuitPowered:
                case TDone.NoSuitPower:
                case TDone.MenuOpen:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>4 Hz (Poll), allocation-free: the current step's setup state into _setupOk, which
        /// IsDone answers from - read-only game state (the hand, the open wheel, the tier, F10).
        /// Lesson 4's "rests 0.5 s on a stack wedge" keeps its own dwell here: the just-in-time hover
        /// poll only runs while lessons pop up by themselves.</summary>
        private static void PollSetup(float pdt, DynamicThing held)
        {
            var step = _flow == Flow.Lesson ? CurrentStep() : null;
            if (step == null || _confirming || !IsSetupState(step.Done))
            {
                _setupOk = false;
                _setupStackFor = 0f;
                return;
            }
            if (step.Done == TDone.StackHovered)
                _setupStackFor = IsStackWedge(HoveredWedge()) ? _setupStackFor + pdt : 0f;
            _setupOk = SetupHolds(step, held);
        }

        /// <summary>The setup state right now (see TDone's setup block). Cheap reads only, except
        /// lesson 7's belt scan - once per open belt wheel (<see cref="BeltWheelWithSpare"/>) - and
        /// lesson 10's wedge walk - only while its setup is the step.</summary>
        private static bool SetupHolds(TStep step, DynamicThing held)
        {
            switch (step.Done)
            {
                case TDone.WheelOpen: return RadialController.AnyRadialOpen;
                // Lesson 3's context PER TRACK (ToolsHandFor): the tool track wants a held TOOL with a
                // part socket that is not the tablet (its strips talk batteries and canisters - the tablet
                // has its own track, which wants the tablet).
                case TDone.HoldingPartsTool: return ToolsHandFor(held, 0);
                case TDone.HoldingTablet: return ToolsHandFor(held, 1);
                case TDone.StackHovered: return _setupStackFor >= 0.5f;
                case TDone.BeltWheelWithSpare: return BeltWheelWithSpare();
                case TDone.BagWheelOpen: return BagWheelOpen();
                case TDone.SettingsWheelOpen: return SettingsWheelShown();
                case TDone.SuitPowered: return SuitPoweredNow();
                case TDone.NoSuitPower: return NoSuitPowerNow();
                case TDone.MenuOpen:
                    try { return UiaControlCenter.IsOpen; } catch { return false; }
                case TDone.DesignerKey: return ((_ev | _evPending) & EvDesignerKey) != 0;
                default: return false;
            }
        }

        /// <summary>Step ENTRY: does the setup's situation already exist? Then the step is passed over
        /// silently. Where the 4 Hz reading needs time (a dwell, a free mouse for 1 s) the instant
        /// equivalent is asked; lesson 17's key counts only when that press started the lesson (the
        /// seed in the evidence window this step is about to take).</summary>
        private static bool SetupHoldsOnEntry(TStep step)
        {
            switch (step.Done)
            {
                case TDone.StackHovered: return IsStackWedge(HoveredWedge());
                case TDone.GridFreed:
                    try { return TheGridPanel.IsOpen && TheGridPanel.IsInteractive; } catch { return false; }
                case TDone.DesignerKey: return (_evPending & EvDesignerKey) != 0;
                default: return IsSetupState(step.Done) && SetupHolds(step, Held());
            }
        }

        /// <summary>Lesson 7's setup: the belt ring is open (the belt picker it swaps to counts) AND
        /// another belt is in reach. The inventory scan (<see cref="CarriesAnotherBelt"/>) runs once per
        /// wheel open - _wheelSerial counts WheelOpened - and is cached for as long as that wheel stays.</summary>
        private static bool BeltWheelWithSpare()
        {
            if (!RadialController.AnyRadialOpen) return false;
            var k = RadialController.ActiveWheelKind;
            if (k != TWheelKind.Belt && k != TWheelKind.BeltPicker) return false;
            if (_beltSpareSerial != _wheelSerial)
            {
                _beltSpareSerial = _wheelSerial;
                _beltSpare = CarriesAnotherBelt();
            }
            return _beltSpare;
        }

        /// <summary>Lesson 8's setup: the bag wheel ({UIA_BagRadial}; a bound bag too) is open, or THE HUB
        /// inside the belt wheel - a bag level or SEARCH inside either counts. ActiveWheelKind never reads
        /// Hub (a dive into THE HUB keeps the belt's kind), so the dive depth (_hubDepth) tells it.</summary>
        private static bool BagWheelOpen()
        {
            if (!RadialController.AnyRadialOpen) return false;
            switch (RadialController.ActiveWheelKind)
            {
                case TWheelKind.Bag:
                case TWheelKind.BoundBag:
                    return true;
                case TWheelKind.Search:
                    return _hubDepth > 0 || _wheelKind == TWheelKind.Bag || _wheelKind == TWheelKind.BoundBag;
                case TWheelKind.Belt:
                    return _hubDepth > 0;
                default:
                    return false;
            }
        }

        /// <summary>Lesson 10's setup: the open wheel shows a Setting (a device control) or a Value (a
        /// scroll wedge) wedge, or the SETTINGS branch 3+ controls collapse into (ItemMenuBuilder
        /// .BuildSettingsWedge: a branch labelled "Settings"). The ring publishes "some visible wedge
        /// scrolls" every frame (RadialHintContext.LevelScroll - the satellite included); otherwise the
        /// visible main ring is walked. Only while that setup is the step, at 4 Hz. NOT the suit's own
        /// wheel ({V:SuitSlot}): its scroll wedges are the suit's pressure / temperature setpoints - the
        /// lesson's "scroll a value" must never land on those (the setup asks for the jetpack wheel).</summary>
        private static bool SettingsWheelShown()
        {
            if (IsSuitWheelOpen()) return false;
            if (!RadialController.AnyRadialOpen) return false;
            try
            {
                if (Overlay.RadialHintContext.InteractionFresh && !Overlay.RadialHintContext.Search
                    && Overlay.RadialHintContext.LevelScroll) return true;
                var c = RadialController.Active;
                var menu = c != null ? c._menu : null;
                if (menu == null || !menu.IsOpen || menu.IsSearchOpen) return false;
                for (int i = 0; i < 32; i++)
                {
                    var e = menu.MainEntry(i);
                    if (e == null) break;
                    if (e.CanHotkey || e.IsScrollAdjust) return true;
                    if (e.IsBranch && string.Equals(e.Label, "Settings", StringComparison.Ordinal)) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Lesson 14's context: the visor half is on and the tier is Suited or Robot.</summary>
        private static bool SuitPoweredNow()
        {
            var s = VisorOn() ? TutorialSafety.Snapshot() : null;
            return s != null && (s.Tier == HudTier.Suited || s.Tier == HudTier.Robot);
        }

        /// <summary>Lesson 15's context: the visor half is on, the tier is Bare, and not a robot.</summary>
        private static bool NoSuitPowerNow()
        {
            var s = VisorOn() ? TutorialSafety.Snapshot() : null;
            return s != null && s.Tier == HudTier.Bare && !s.IsRobot;
        }

        // ================================================================ context helpers (read-only)

        private static bool TipsOn()
        {
            try { return UIAConfig.TutorialTips == null || UIAConfig.TutorialTips.Value; }
            catch { return true; }
        }

        private static bool PauseCards()
        {
            try { return UIAConfig.TutorialAutoPause == null || UIAConfig.TutorialAutoPause.Value; }
            catch { return true; }
        }

        /// <summary>"The core is done, skipped or in 'as I go' mode" (plan A.7). In multiplayer - where
        /// the Welcome never opens (A.6: an invitation instead) and neither does the Welcome back - a
        /// First Steps that was never started, or one interrupted and not running, counts too, so
        /// just-in-time strips "work normally" there.</summary>
        private static bool CoreSatisfied()
        {
            var st = StateOf("core");
            return st == TLessonState.Done || st == TLessonState.Skipped || st == TLessonState.Learned
                || TutorialProgressStore.Flag("asigo")
                || ((st == TLessonState.New || st == TLessonState.Active) && IsMultiplayer());
        }

        /// <summary>First Steps holds everything else back (plan: "priority over everything") while it is
        /// stored Active and can come back by itself - in single-player, where the Welcome-back card
        /// offers it. Multiplayer never shows that card (A.6), so there an interrupted First Steps must
        /// not block the just-in-time lessons and tips forever. (A RUNNING core is a busy flow anyway.)</summary>
        private static bool CorePending()
        {
            return StateOf("core") == TLessonState.Active && !IsMultiplayer();
        }

        /// <summary>A multiplayer client, or a host with anyone connected (vanilla's own no-pause
        /// condition, GamePause.CanOwnPause minus its menu check). Read-only.</summary>
        private static bool IsMultiplayer()
        {
            try { return Assets.Scripts.Networking.NetworkManager.IsClient || NetworkBase.Clients.Count != 0; }
            catch { return false; }
        }

        private static bool CoreEndedLongAgo(float now)
        {
            if (!CoreSatisfied()) return false;
            return _coreEndedAt < 0f || now - _coreEndedAt >= LessonVisorAfterCore;
        }

        private static bool IsSimpleStow()
        {
            try { return !StowModeConfig.Available || StowModeConfig.Mode == StowMode.Simple; }
            catch { return true; }
        }

        private static bool IsMpClient()
        {
            try { return !GameManager.RunSimulation; }
            catch { return false; }
        }

        private static bool IsRobot()
        {
            var s = TutorialSafety.Snapshot();
            return s != null && s.IsRobot;
        }

        private static long HandId()
        {
            try
            {
                var slot = InventoryManager.ActiveHandSlot;
                var t = slot != null ? slot.Get() : null;
                return t != null ? t.ReferenceId : 0L;
            }
            catch { return 0L; }
        }

        private static bool ActiveHandEmpty()
        {
            try { var slot = InventoryManager.ActiveHandSlot; return slot != null && slot.Get() == null; }
            catch { return false; }
        }

        private static bool BothHandsFull()
        {
            try
            {
                var h = InventoryManager.ParentHuman;
                if (h == null) return false;
                return h.LeftHandSlot != null && h.LeftHandSlot.Get() != null
                    && h.RightHandSlot != null && h.RightHandSlot.Get() != null;
            }
            catch { return false; }
        }

        private static bool HasSlotOfClass(DynamicThing t, Slot.Class c)
        {
            try
            {
                var slots = t != null ? t.Slots : null;
                if (slots == null) return false;
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i] != null && slots[i].Type == c) return true;
            }
            catch { }
            return false;
        }

        /// <summary>The component-socket classes ItemMenuBuilder.IsComponentSocket treats as parts
        /// (the plan's list: battery, canister, filter, cartridge).</summary>
        private static bool HasComponentSocket(DynamicThing t)
        {
            return HasSlotOfClass(t, Slot.Class.Battery) || HasSlotOfClass(t, Slot.Class.GasCanister)
                || HasSlotOfClass(t, Slot.Class.GasFilter) || HasSlotOfClass(t, Slot.Class.Cartridge)
                || HasSlotOfClass(t, Slot.Class.LiquidCanister);
        }

        /// <summary>Lesson 3's "holding a tool?": a held TOOL (the game's own <c>Tool</c> base - power
        /// tools, the tablet, the welding torch...) with a part socket. A carried suit or jetpack has
        /// battery / canister slots too, but it is not a tool and has no [R] tool wheel lesson.</summary>
        private static bool IsSocketTool(DynamicThing t)
        {
            return t is Tool && HasComponentSocket(t);
        }

        /// <summary>Lesson 3's hand PER TRACK: track 1 (tablets and cartridges) = the tablet; track 0
        /// (tools and batteries) = a socket tool that is NOT the tablet (a tablet is a socket tool too, but
        /// it has its own track - the same split the just-in-time triggers and TrackForContext make).</summary>
        private static bool ToolsHandFor(DynamicThing h, int track)
        {
            if (h == null) return false;
            return track == 1 ? h is Tablet : !(h is Tablet) && IsSocketTool(h);
        }

        private static DynamicThing Held()
        {
            try { var slot = InventoryManager.ActiveHandSlot; return slot != null ? slot.Get() : null; }
            catch { return null; }
        }

        private static bool HeldIsCanisterTool()
        {
            var h = Held();
            return h != null && !HasSlotOfClass(h, Slot.Class.Battery)
                && (HasSlotOfClass(h, Slot.Class.GasCanister) || HasSlotOfClass(h, Slot.Class.LiquidCanister));
        }

        private static bool HeldIsLowPowerTool()
        {
            var pt = Held() as PowerTool;
            if (pt == null || pt is Tablet) return false;
            try { var b = pt.Battery; return b != null && b.CurrentPowerPercentage <= 20; }
            catch { return false; }
        }

        private static bool TabletHasCartridge()
        {
            var t = Held() as Tablet;
            if (t == null) return false;
            try
            {
                var slots = t.Slots;
                for (int i = 0; slots != null && i < slots.Count; i++)
                    if (slots[i] != null && slots[i].Get() is Cartridge) return true;
            }
            catch { }
            return false;
        }

        private static bool BeltHasTools()
        {
            try
            {
                var h = InventoryManager.ParentHuman;
                var belt = h != null && h.ToolbeltSlot != null ? h.ToolbeltSlot.Get() : null;
                if (belt == null || belt.Slots == null) return false;
                for (int i = 0; i < belt.Slots.Count; i++)
                    if (belt.Slots[i] != null && belt.Slots[i].Get() != null) return true;
            }
            catch { }
            return false;
        }

        private static bool IsToolbeltRingOpen()
        {
            try { var c = RadialController.Active; return c != null && RadialController.IsToolbeltRing(c.ActiveFeature); }
            catch { return false; }
        }

        private static bool IsSuitWheelOpen()
        {
            try
            {
                var c = RadialController.Active;
                var eq = c != null ? c.ActiveFeature as EquipmentKeyRadialFeature : null;
                return eq != null && eq.ButtonName == "SuitSlot";
            }
            catch { return false; }
        }

        /// <summary>Lesson 7's context: a second belt in reach. Computed at most once per frame and only
        /// on a belt-wheel open edge (it scans the inventory - never per frame).</summary>
        private static bool CarriesAnotherBelt()
        {
            if (_otherBeltFrame == Time.frameCount) return true;
            try
            {
                var list = ToolbeltRadialFeature.BuildBeltPicker();
                if (list != null && list.Count >= 2) { _otherBeltFrame = Time.frameCount; return true; }
            }
            catch { }
            return false;
        }

        // Scratch for the same-name scan (lesson 13) - cleared before and after use.
        private static readonly List<string> _names = new List<string>(16);

        /// <summary>Lesson 13's condition: two carried containers share a display name. Walks the
        /// worn slots and hands and one level of nested bags. Only on a bag-wheel open edge.</summary>
        private static bool DuplicateBagNames()
        {
            _names.Clear();
            try
            {
                var h = InventoryManager.ParentHuman;
                if (h == null) return false;
                Slot[] roots = { h.HelmetSlot, h.GlassesSlot, h.SuitSlot, h.BackpackSlot, h.UniformSlot, h.ToolbeltSlot, h.LeftHandSlot, h.RightHandSlot };
                for (int i = 0; i < roots.Length; i++)
                {
                    var t = roots[i] != null ? roots[i].Get() : null;
                    if (t == null || !GridModel.IsStorageContainer(t)) continue;
                    if (AddName(t)) return true;
                    var slots = t.Slots;
                    for (int j = 0; slots != null && j < slots.Count; j++)
                    {
                        var inner = slots[j] != null ? slots[j].Get() : null;
                        if (inner != null && GridModel.IsStorageContainer(inner) && AddName(inner)) return true;
                    }
                }
                return false;
            }
            catch { return false; }
            finally { _names.Clear(); }
        }

        private static bool AddName(DynamicThing t)
        {
            string n = null;
            try { n = t.DisplayName; } catch { }
            if (string.IsNullOrEmpty(n)) return false;
            for (int i = 0; i < _names.Count; i++)
                if (string.Equals(_names[i], n, StringComparison.OrdinalIgnoreCase)) return true;
            _names.Add(n);
            return false;
        }

        private static bool ContextExists(TLesson l)
        {
            if (l == null) return false;
            switch (l.Trigger)
            {
                case TTrigger.Tools:
                {
                    var h = Held();
                    return h != null && (h is Tablet || IsSocketTool(h));
                }
                case TTrigger.Visor: return SuitPoweredNow();
                case TTrigger.Senses: return NoSuitPowerNow();
                default:
                    return true;
            }
        }

        /// <summary>The context of ONE track (a running lesson's S4, the tour's resume point): lesson 3's
        /// tool track wants a socket tool that is not the tablet, its tablet track the tablet - so a swap
        /// mid-track is noticed (S4) and a resume with the other kind of tool in hand rewinds to the
        /// track's setup. Every other lesson has one track: <see cref="ContextExists(TLesson)"/>.</summary>
        private static bool ContextExists(TLesson l, int track)
        {
            if (l != null && l.Trigger == TTrigger.Tools) return ToolsHandFor(Held(), track);
            return ContextExists(l);
        }

        // ================================================================ triggers + queue

        /// <summary>Could this lesson be triggered now (lessons on, its state allows an offer)? Cheap -
        /// the pollers call it before any costly check.</summary>
        private static bool WantsTrigger(string lessonId)
        {
            if (!TipsOn()) return false;
            var l = TutorialChapters.FindLesson(lessonId);
            return l != null && Eligible(l);
        }

        private static bool Eligible(TLesson l)
        {
            if (l == null || l.Priority <= 0) return false;       // entry/core/menu/designer/whatsnew are not queued
            if (_active == l) return false;
            if (TourHoldsLessons()) return false;                 // the first-run tour plays them itself, in order
            if (l.NeedsCore && !CoreSatisfied()) return false;
            var st = StateOf(l.Id);
            if (AfterTour(l)) return PostTourOfferable(l, st);
            if (st == TLessonState.New) return true;
            if (st == TLessonState.Offered)
                return TutorialProgressStore.GetOffers(l.Id) < MaxOffers
                    && TutorialProgressStore.MinutesSinceOffered(l.Id) >= ReofferMinutes;
            return false;
        }

        // ---- tour mode: what the first-run tour does to lessons popping up by themselves

        /// <summary>While the first-run tour runs - or waits, paused, for its Welcome-back card (single-
        /// player; multiplayer never shows that card, so there a paused tour holds nothing back, like
        /// CorePending) - or its closing card is due, nothing pops up by itself: no just-in-time lesson,
        /// no tip, no F10 opener, no F9 card. The tour shows them all, in order.</summary>
        private static bool TourHoldsLessons()
        {
            if (_tourEndDue) return true;
            if (TutorialProgressStore.TourState != TTourState.Running) return false;
            return _tourLive || !IsMultiplayer();
        }

        /// <summary>A tour lesson after the first-run tour ended (played through, or stopped).</summary>
        private static bool AfterTour(TLesson l)
        {
            return l != null && l.InTour && TutorialProgressStore.TourState == TTourState.Ended;
        }

        /// <summary>After the tour: a lesson it did not finish (set aside = Skipped, never reached = New,
        /// an ignored offer = Offered) comes back just-in-time ONCE - the moment its trigger fires; a
        /// finished (Done) or Learned one never does. "Teach me as I go" players never took the tour
        /// (TourState None) and keep the full just-in-time rules.</summary>
        private static bool PostTourOfferable(TLesson l, TLessonState st)
        {
            if (st != TLessonState.New && st != TLessonState.Offered && st != TLessonState.Skipped) return false;
            return !TutorialProgressStore.Flag(JitKey(l));
        }

        /// <summary>The lessons that open themselves on their key rather than through the queue - the F10
        /// tour (first F10 open) and the Designer card (first F9 open): New, or after the tour once.</summary>
        private static bool AutoOfferable(TLesson l)
        {
            if (l == null || TourHoldsLessons()) return false;
            var st = StateOf(l.Id);
            if (AfterTour(l)) return PostTourOfferable(l, st);
            return st == TLessonState.New;
        }

        /// <summary>An automatic offer is starting: after the tour it was this lesson's one comeback.</summary>
        private static void NoteJitIfAfterTour(TLesson l)
        {
            if (AfterTour(l)) TutorialProgressStore.SetFlag(JitKey(l), true);
        }

        /// <summary>"jit:&lt;lesson id&gt;" - cached per lesson, so the 4 Hz checks never concatenate.</summary>
        private static string JitKey(TLesson l)
        {
            var ls = TutorialChapters.Lessons;
            if (_jitKeys == null || _jitKeys.Length != ls.Length)
            {
                var keys = new string[ls.Length];
                for (int i = 0; i < ls.Length; i++) keys[i] = ls[i] != null ? "jit:" + ls[i].Id : null;
                _jitKeys = keys;
            }
            for (int i = 0; i < ls.Length; i++)
                if (ReferenceEquals(ls[i], l)) return _jitKeys[i];
            return "jit:?";
        }

        // ---- tour mode: lesson 3's two tracks (which one may come back after the tour)

        // Persisted per track (Progress.xml flags): the first-run tour set that track of lesson 3 aside.
        private const string AsideToolTrack = "aside:tools@0", AsideTabletTrack = "aside:tools@1";

        private static string AsideKey(int track) { return track == 1 ? AsideTabletTrack : AsideToolTrack; }

        /// <summary>Lesson 3 (tools / tablet) is the one lesson with two tracks.</summary>
        private static bool HasTracks(TLesson l) { return l != null && l.Id == "tools"; }

        /// <summary>The running lesson's CURRENT track is set aside (its setup self-skipped, its reason
        /// went away, every move of it was only shown). In the tour lesson 3's bit is persisted at once, so
        /// a quit / F6 before the lesson ends keeps it (StoredAside).</summary>
        private static void SetTrackAside()
        {
            int t = Mathf.Clamp(_track, 0, 1);
            _asideTracks |= 1 << t;
            if (_activeTour && HasTracks(_active))
            {
                TutorialProgressStore.SetFlag(AsideKey(t), true);
                TutorialProgressStore.Flush();   // rare (a set-aside): on disk before the next track starts
            }
        }

        /// <summary>Lesson 3 of the tour ended early on <paramref name="track"/> (skipped, or the tour
        /// stopped under it): that track and the one after it never finished - both are set aside.</summary>
        private static void AsideFrom(TLesson l, int track)
        {
            if (!HasTracks(l)) return;
            for (int t = Mathf.Clamp(track, 0, 1); t <= 1; t++)
            {
                TutorialProgressStore.SetFlag(AsideKey(t), true);
                if (ReferenceEquals(_active, l)) _asideTracks |= 1 << t;
            }
        }

        private static void ClearAside()
        {
            TutorialProgressStore.SetFlag(AsideToolTrack, false);
            TutorialProgressStore.SetFlag(AsideTabletTrack, false);
        }

        /// <summary>The persisted aside bits of a lesson (bit 1 &lt;&lt; track); 0 for a one-track lesson.</summary>
        private static int StoredAside(TLesson l)
        {
            if (!HasTracks(l)) return 0;
            int bits = 0;
            if (TutorialProgressStore.Flag(AsideToolTrack)) bits |= 1;
            if (TutorialProgressStore.Flag(AsideTabletTrack)) bits |= 2;
            return bits;
        }

        /// <summary>After the tour, lesson 3's one comeback is for a track the tour did NOT finish: with
        /// tracks recorded as set aside, only those - a Drill in hand never replays a tool track the player
        /// already did (and never burns jit:tools on it); with none recorded (never reached, skipped whole,
        /// ignored) either one. Every other lesson, and lesson 3 outside that rule: true. Constant keys -
        /// the 4 Hz tool poll may ask.</summary>
        private static bool TrackEligible(TLesson l, int track)
        {
            if (!HasTracks(l) || !AfterTour(l)) return true;
            int bits = StoredAside(l);
            return bits == 0 || (bits & (1 << Mathf.Clamp(track, 0, 1))) != 0;
        }

        /// <summary>Every move of the current track that asked for something was only shown (each
        /// self-skipped) - that track was ignored (per track: counted from when it began).</summary>
        private static bool TrackIgnored()
        {
            return _doSteps - _doStepsAtTrack > 0 && _doDone - _doDoneAtTrack == 0;
        }

        private static void TriggerIf(string lessonId, ulong seed, int track)
        {
            if (!TipsOn()) return;
            var l = TutorialChapters.FindLesson(lessonId);
            if (l == null || !Eligible(l)) return;
            if (!TrackEligible(l, track)) return;   // after the tour: that track of lesson 3 was finished
            float now = Time.unscaledTime;
            for (int i = 0; i < _queueCount; i++)
            {
                if (_queue[i].L != l) continue;
                _queue[i].At = now;
                _queue[i].Seed |= seed;
                _queue[i].Track = track;
                return;
            }
            if (_queueCount < QueueMax)
            {
                _queue[_queueCount++] = new Queued { L = l, At = now, Seed = seed, Track = track };
                return;
            }
            // Full: replace the least urgent (highest priority number, then oldest) if the newcomer beats it.
            int worst = 0;
            for (int i = 1; i < _queueCount; i++)
                if (_queue[i].L.Priority > _queue[worst].L.Priority
                    || (_queue[i].L.Priority == _queue[worst].L.Priority && _queue[i].At < _queue[worst].At))
                    worst = i;
            if (l.Priority <= _queue[worst].L.Priority)
                _queue[worst] = new Queued { L = l, At = now, Seed = seed, Track = track };
        }

        private static void DropQueued(string lessonId)
        {
            for (int i = 0; i < _queueCount; i++)
            {
                if (_queue[i].L == null || _queue[i].L.Id != lessonId) continue;
                RemoveQueued(i);
                return;
            }
        }

        private static void RemoveQueued(int i)
        {
            for (int j = i; j < _queueCount - 1; j++) _queue[j] = _queue[j + 1];
            _queueCount--;
            _queue[_queueCount] = default(Queued);
        }

        /// <summary>Scheduler (4 Hz): start the best queued lesson when the rules allow, else a tip.</summary>
        private static void Schedule(float now)
        {
            if (_flow != Flow.Idle || _card != CardCtx.None || _preview != null) return;
            if (!TipsOn()) { _queueCount = 0; return; }
            // Stale or context-lost entries leave the queue.
            for (int i = _queueCount - 1; i >= 0; i--)
                if (now - _queue[i].At > QueueExpirySeconds || !Eligible(_queue[i].L) || !ContextExists(_queue[i].L))
                    RemoveQueued(i);
            if (Suspended() || UiaControlCenter.IsOpen || RadialEditorMode.Active) return;
            if (!_calmNow) return;
            if (now - _worldEnterTime < WorldSettleSeconds) return;
            if (CorePending()) return;   // First Steps has priority over everything (single-player; see CorePending)
            if (TourHoldsLessons()) return;   // so does the first-run tour (it plays every lesson itself)

            if (_queueCount > 0 && now - _lastLessonEnd >= LessonGapSeconds)
            {
                int best = 0;
                for (int i = 1; i < _queueCount; i++)
                    if (_queue[i].L.Priority < _queue[best].L.Priority
                        || (_queue[i].L.Priority == _queue[best].L.Priority && _queue[i].At < _queue[best].At))
                        best = i;
                var q = _queue[best];
                RemoveQueued(best);
                // Lesson 3's track follows the hand NOW (a tablet vs a tool may have changed while the
                // lesson waited in the queue); every other lesson has one track. After the first-run tour
                // only a track it did not finish may come back: the hand changed to a finished one -
                // dropped, the one comeback kept for its own moment.
                int track = q.L.Id == "tools" ? TrackForContext(q.L) : q.Track;
                if (!TrackEligible(q.L, track)) return;
                NoteJitIfAfterTour(q.L);   // after the first-run tour: its one comeback
                StartLesson(q.L, track, 0, q.Seed, true, now);
                return;
            }
            if (now - _lastTipEnd >= TipGapSeconds && now - _lastLessonEnd >= TipGapSeconds) TryStartTip(now);
        }

        // ================================================================ tips ("Teach me as I go")

        private static void TipMoment(int n, float now)
        {
            if (n > 0 && n < _tipMoment.Length) _tipMoment[n] = now;
        }

        private static void ClearTipMoments()
        {
            for (int i = 0; i < _tipMoment.Length; i++) _tipMoment[i] = -1f;
        }

        // Precomputed keys (index = tip number) so the 4 Hz tip check never concatenates.
        private static readonly string[] TipIds = { null, "tip.1", "tip.2", "tip.3", "tip.4", "tip.5", "tip.6", "tip.7" };
        /// <summary>The core step each tip stands in for: a tip fires in "as I go" mode, or when its
        /// core step was only SHOWN (self-skipped) - its just-in-time twin (plan A.7). Flags are
        /// "s1:" + the core step id (set by <see cref="SelfSkip"/>).</summary>
        private static readonly string[] TipTwinFlags =
        {
            null, "s1:core.beltopen", "s1:core.read", "s1:core.stow", "s1:core.back", "s1:core.pushout",
            "s1:core.window", "s1:core.window",
        };

        private static bool TipWanted(int n, float now)
        {
            if (TutorialProgressStore.Flag(TipIds[n])) return false;
            if (!TutorialProgressStore.Flag("asigo") && !TutorialProgressStore.Flag(TipTwinFlags[n])) return false;
            switch (n)
            {
                case 1: return _playSeconds >= 120f && TutorialProgressStore.Counter("wheels") == 0;
                case 6: return _playSeconds >= 300f && TutorialProgressStore.Counter("gridOpens") == 0;
                default: return _tipMoment[n] >= 0f && now - _tipMoment[n] <= TipMomentSeconds;
            }
        }

        private static void TryStartTip(float now)
        {
            // Only while First Steps is not pending (as-I-go replaces it; S1 twins come after) - nor the
            // first-run tour (it teaches all of it).
            if (CorePending() || TourHoldsLessons()) return;
            for (int n = 1; n <= 7; n++)
            {
                if (!TipWanted(n, now)) continue;
                var step = TutorialChapters.FindStep(TipIds[n]);
                if (step == null) continue;
                TutorialProgressStore.SetFlag(TipIds[n], true);
                _tipMoment[n] = -1f;
                StartTimed(Flow.Tip, step, now);
                return;
            }
        }

        // ================================================================ timed strips (tip / invite / notice)

        private static void StartTimed(Flow flow, TStep step, float now)
        {
            _flow = flow;
            _timedStep = step;
            _timedId = step != null ? step.Id : "chrome|lessonsoff";
            _timedDemo = step != null ? step.DemoId : null;
            RebuildTimedText();
            _timedTotal = flow == Flow.Invite ? InviteSeconds
                : flow == Flow.Notice ? NoticeSeconds
                : ReadSecondsFor(_timedText);
            _timedLeft = _timedTotal;
            _drainShown = -2f;
            _stripShowing = false;
        }

        private static void RebuildTimedText()
        {
            if (_flow == Flow.Notice)
            {
                _timedHeader = "";
                _timedText = ChromeText("lessonsoff");
                return;
            }
            var step = _timedStep;
            if (step == null) { _timedText = ""; _timedHeader = ""; return; }
            if (step.Kind == TPresentation.Tip) { _timedHeader = ""; _timedText = Text(step, "says"); return; }
            // Invitation strips: the entry invite, or What's new's multiplayer line.
            _timedHeader = LessonTitle(step.Lesson);
            _timedText = Text(step, step.Has("says") ? "says" : "says@mp");
        }

        private static void TickTimed(float now, float dt)
        {
            if (Suspended()) { HideStrip(); return; }
            if (!_stripShowing)
            {
                ShowStripRaw(_timedId, _timedHeader, _timedText, _timedDemo);
                _drainShown = -2f;
            }
            if (_flow == Flow.Invite && UiaControlCenter.IsOpen) { EndTimed(); return; }
            // The lesson editor holds a tip / invite / notice on screen too (see TickLesson).
            if (!UiaControlCenter.IsOpen && !EditorFreeze()) _timedLeft -= dt;
            SetDrain(_timedTotal > 0f ? Mathf.Clamp01(_timedLeft / _timedTotal) : -1f);
            if (_timedLeft <= 0f) EndTimed();
        }

        private static void EndTimed()
        {
            var flow = _flow;
            HideStrip();
            _flow = Flow.Idle;
            _timedStep = null;
            if (flow == Flow.Tip) _lastTipEnd = Time.unscaledTime;
        }

        // ================================================================ entry (first-run gate)

        /// <summary>The first-run gate, moved from StationeersUIMod.Update UNCHANGED in logic: safely in
        /// control of a character (menus toggleable, nothing else up, input state Game, not paused, no
        /// vanilla menu, a responsive body).</summary>
        private static bool FirstRunGate()
        {
            try
            {
                return Guards.CanToggleMenus()
                    && !UiaControlCenter.IsOpen && !RadialController.AnyRadialOpen
                    && !HudEditorMode.Active && !RadialEditorMode.Active
                    && !TheGridPanel.IsOpen
                    && KeyManager.InputState == KeyInputState.Game
                    && !WorldManager.IsGamePaused && !Guards.VanillaMenuWantsFront()
                    && InventoryManager.ParentHuman != null
                    && !InventoryManager.ParentHuman.IsUnresponsive;
            }
            catch { return false; }
        }

        private static Entry EntryNeeded()
        {
            bool sp = GamePause.CanOwnPause();
            bool tips = TipsOn();
            bool guideShown = true;
            try { guideShown = UIAConfig.GuideShown == null || UIAConfig.GuideShown.Value; } catch { }

            var entryState = StateOf("entry");
            // The FIRST Welcome (a fresh install, or "Restart from the beginning") always comes: it is
            // where "No lessons" lives, and Restart promises it. Everything after that is a lesson
            // popping up by itself - "Off = no lessons pop up by themselves" (UIAConfig.TutorialTips) -
            // so the re-asks, the invitation strip, the Welcome back and What's new need lessons on.
            // Tour mode: an UPDATER (GuideShown from 0.9.7.x, no Progress.xml - TutorialProgressStore
            // seeds nothing Done for them) has never answered this Welcome either: entry is still New,
            // so they get the same first Welcome - the tour - instead of What's new.
            bool welcomeDue = (!guideShown || entryState == TLessonState.New) && !_welcomeHoldForNextEntry;
            if (!welcomeDue && tips)
            {
                // Later: ask again at the next world entry (twice at most, see WelcomeChoice).
                if (entryState == TLessonState.Later && _entrySerial > _laterAtEntry
                    && TutorialProgressStore.Counter("welcomeLater") <= 2)
                    welcomeDue = true;
                // Shown but never answered (the world went away under it): ask again next entry.
                else if (entryState == TLessonState.Offered && _entrySerial > _welcomeShownAtEntry)
                    welcomeDue = true;
            }
            if (welcomeDue)
            {
                if (sp) return Entry.Welcome;
                return _inviteShown || !tips ? Entry.None : Entry.Invite;
            }
            if (!tips) return Entry.None;

            // Welcome back: an interrupted First Steps - or a paused first-run tour (tour mode: it
            // continues at the same lesson and step) - single-player, once per world entry (an F6
            // reload is not a new entry, see ReadCarry); its Later re-offers it twice at most, then
            // converts (ResumeChoice). "Remind me in 5 minutes" brings it back after calm play.
            if (sp && (CorePending() || TourPending()))
            {
                if (_remindPending)
                {
                    if (_remindCalm >= RemindCalmSeconds) return Entry.Resume;
                }
                else if (!_resumeOfferedThisEntry)
                    return Entry.Resume;
            }

            // What's new is not shown while the first-run tour runs: the tour covers all of it (it stays
            // in GUIDE). (Starting the tour marks it Done; this guards a tour resumed from an older file.)
            if (StateOf("whatsnew") == TLessonState.New && !_whatsNewHandledThisEntry && guideShown
                && TutorialProgressStore.TourState != TTourState.Running)
                return sp ? Entry.WhatsNew : Entry.WhatsNewInvite;
            return Entry.None;
        }

        /// <summary>A first-run tour that is Running but not running THIS entry - paused (quit mid-tour,
        /// Welcome back "Later", Hold on "Remind me"): the Welcome-back card continues it.</summary>
        private static bool TourPending()
        {
            return !_tourLive && !_tourEndDue && TutorialProgressStore.TourState == TTourState.Running;
        }

        private static void TickEntry(float now)
        {
            if (_flow != Flow.Idle || _card != CardCtx.None) { _gateSince = -1f; _nextEntryEval = 0f; return; }
            // Sampled at the 4 Hz poll rate, never per idle frame: EntryNeeded asks the game
            // (GamePause.CanOwnPause is a native call) and the progress store. The gate is the same
            // one, held for the same 1 s settle, now checked four times a second.
            if (now < _nextEntryEval) return;
            _nextEntryEval = now + PollInterval;
            var need = EntryNeeded();
            if (need == Entry.None || !FirstRunGate()) { _gateSince = -1f; return; }
            if (_gateSince < 0f) { _gateSince = now; return; }
            if (now - _gateSince < SettleSeconds) return;
            _gateSince = -1f;
            _nextEntryEval = 0f;

            var entry = TutorialChapters.FindLesson("entry");
            switch (need)
            {
                case Entry.Welcome:
                    OpenWelcome();
                    // Consume the one-shot only when the card genuinely opened - a transient build failure
                    // must not burn the Welcome forever (same rule as the old first-run block).
                    if (_card == CardCtx.Welcome)
                    {
                        _welcomeShownAtEntry = _entrySerial;
                        try { if (UIAConfig.GuideShown != null) UIAConfig.GuideShown.Value = true; } catch { }
                        TutorialProgressStore.SetState("entry", TLessonState.Offered);
                        TutorialProgressStore.Flush();   // a crash now must not look like an updater next launch
                    }
                    break;
                case Entry.Invite:
                    _inviteShown = true;
                    if (entry != null) StartTimed(Flow.Invite, FindStepIn(entry, "entry.invite"), now);
                    break;
                case Entry.Resume:
                    _resumeOfferedThisEntry = true;
                    _remindPending = false;
                    _remindCalm = 0f;
                    if (entry != null) OpenStepCard(CardCtx.Resume, FindStepIn(entry, "entry.resume"), null);
                    break;
                case Entry.WhatsNew:
                    _whatsNewHandledThisEntry = true;
                    OpenWhatsNew();
                    break;
                case Entry.WhatsNewInvite:
                {
                    _whatsNewHandledThisEntry = true;
                    var wn = TutorialChapters.FindLesson("whatsnew");
                    if (wn != null && wn.Steps.Length > 0)
                    {
                        StartTimed(Flow.Invite, wn.Steps[0], now);
                        TutorialProgressStore.SetState("whatsnew", TLessonState.Done);   // offered once (it's in GUIDE)
                    }
                    break;
                }
            }
        }

        private static TStep FindStepIn(TLesson l, string id)
        {
            for (int i = 0; l != null && i < l.Steps.Length; i++) if (l.Steps[i].Id == id) return l.Steps[i];
            return null;
        }

        private static void OpenWelcome()
        {
            var entry = TutorialChapters.FindLesson("entry");
            var step = FindStepIn(entry, "entry.welcome");
            if (step == null) return;
            string bodyField = !GamePause.CanOwnPause() ? "body@mp" : (!PauseCards() ? "body@running" : "body");
            OpenStepCard(CardCtx.Welcome, step, bodyField);
        }

        private static void OpenWhatsNew()
        {
            var wn = TutorialChapters.FindLesson("whatsnew");
            if (wn == null || wn.Steps.Length == 0) return;
            _pageIdx = 0;
            OpenStepCard(CardCtx.WhatsNew, wn.Steps[0], null);
        }

        // ================================================================ F10 tour (lesson 16)

        private static void TickMenuEdge(float now)
        {
            bool open = false;
            try { open = UiaControlCenter.IsOpen; } catch { }
            if (open && !_f10WasOpen)
            {
                bool ours = Time.frameCount - _f10OpenedByUsFrame <= 3;
                if (!ours) OnMenuOpenedByPlayer();
            }
            // F10 closed under the tour: the coach closes the callout itself and reports it as Later
            // (HandleLater decides New / Skipped / Done). This is only the backstop if that report is lost.
            if (!open && _f10WasOpen && _flow == Flow.Tour && !_pendingLater)
            {
                if (_card == CardCtx.TourOpener || _card == CardCtx.Tour) HandleLater(Time.unscaledTime);
                else EndTour(TLessonState.Skipped);
            }
            _f10WasOpen = open;
        }

        private static void OnMenuOpenedByPlayer()
        {
            if (!TipsOn()) return;
            var l = TutorialChapters.FindLesson("menu");
            if (!AutoOfferable(l)) return;   // New (or once after the first-run tour); never during it
            if (_flow != Flow.Idle || _card != CardCtx.None || _preview != null) return;
            if (HudEditorMode.Active || TutorialCoach.IsOpen) return;
            NoteJitIfAfterTour(l);
            StartTour(true);
        }

        private static void StartTour(bool withOpener)
        {
            var l = TutorialChapters.FindLesson("menu");
            if (l == null) return;
            _flow = Flow.Tour;
            _tourAuto = withOpener;   // the opener = it opened itself on the first F10 open (lessons off ends it)
            _pageIdx = withOpener ? -1 : FirstPage(l);
            _tourMaxPage = -1;
            TutorialProgressStore.SetState("menu", TLessonState.Active);
            ShowTourCard();
        }

        /// <summary>Lesson 16's callouts count as SEEN: the furthest one shown this run is the last, or
        /// the one before it (whose Guide callout the tour's closing card repeats anyway). Closing F10
        /// there - its key, the X, Esc - is the natural "done with this menu": the lesson ends Done, not set
        /// aside (so it does not come back just-in-time after the tour).</summary>
        private static bool CalloutsSeen()
        {
            var l = TutorialChapters.FindLesson("menu");
            if (l == null || _tourMaxPage < 0) return false;
            int last = l.Steps.Length - 1;
            return _tourMaxPage >= Mathf.Max(FirstPage(l), last - 1);
        }

        /// <summary>The F10 key went down this frame: a callout / the opener closed by it is the player
        /// closing the menu (the plugin's toggle shuts F10 after this tick). Read-only key poll.</summary>
        private static bool MenuKeyDownNow()
        {
            try { return UIAConfig.SettingsWindowKey != null && Input.GetKeyDown(UIAConfig.SettingsWindowKey.Value); }
            catch { return false; }
        }

        /// <summary>Close F10 ourselves - and remember the frame, so the plugin's F10-key toggle (it runs
        /// after this tick in the same Update) does not open it again: <see cref="MenuClosedThisFrame"/>.</summary>
        private static void CloseMenu(string source)
        {
            bool open;
            try { open = UiaControlCenter.IsOpen; } catch { open = false; }
            if (!open) return;
            _f10ClosedByUsFrame = Time.frameCount;
            try { UiaControlCenter.Close(); } catch (Exception e) { ReportTickError(source, e); }
        }

        /// <summary>The first page of a lesson's cards / callouts: its first step that is not a setup
        /// (tour mode: lesson 16 opens with menu.setup, a strip - the callouts are Steps[1..]).</summary>
        private static int FirstPage(TLesson l)
        {
            if (l == null) return 0;
            for (int i = 0; i < l.Steps.Length; i++)
                if (!l.Steps[i].IsSetup) return i;
            return 0;
        }

        /// <summary>Tour mode: lesson 16 played by the first-run tour reached its first callout (its
        /// setup is done - F10 is open): the callouts run in the F10-tour flow from here, and its end
        /// (<see cref="EndTour"/>) moves the tour on.</summary>
        private static void EnterCallouts()
        {
            bool tour = _activeTour;
            int page = _stepIdx;
            HideStrip();
            SpotClear();
            ClearFlow();
            _activeTour = tour;
            _flow = Flow.Tour;
            _tourAuto = false;
            var l = TutorialChapters.FindLesson("menu");
            _pageIdx = Mathf.Max(page, FirstPage(l));
            _tourMaxPage = -1;
            TutorialProgressStore.SetState("menu", TLessonState.Active);
            ShowTourCard();
        }

        private static void ShowTourCard()
        {
            var l = TutorialChapters.FindLesson("menu");
            if (l == null) { EndTour(TLessonState.Skipped); return; }
            TCardSpec spec;
            string anchor;
            if (_pageIdx < 0)
            {
                anchor = "tab:UI Themes";
                spec = NewSpec(null, LessonTitle(l), ChromeText("touropener"), null);
                SetButtons(spec, new[] { Act.TourNo, Act.TourTake }, 1);
                _cardStep = null;
                _card = CardCtx.TourOpener;
            }
            else
            {
                int first = FirstPage(l);
                _pageIdx = Mathf.Clamp(_pageIdx, first, l.Steps.Length - 1);   // never the setup strip
                if (_pageIdx > _tourMaxPage) _tourMaxPage = _pageIdx;
                var step = l.Steps[_pageIdx];
                anchor = step.Anchor;
                if (!string.IsNullOrEmpty(anchor) && anchor.StartsWith("tab:", StringComparison.Ordinal))
                {
                    try { UiaControlCenter.SelectTab(anchor.Substring(4)); } catch { }
                }
                spec = NewSpec(step.Id, Text(step, "title"), Text(step, "callout"), step.DemoId);
                SetEditKeys(spec, step, "title", "callout");
                bool last = _pageIdx >= l.Steps.Length - 1;
                // In the first-run tour this is one lesson of many: "Skip lesson", not "Skip tour".
                Act skip = _activeTour ? Act.SkipLesson : Act.SkipTour;
                if (_pageIdx <= first) SetButtons(spec, new[] { skip, last ? Act.Done : Act.Next }, 1);
                else SetButtons(spec, new[] { skip, Act.Back, last ? Act.Done : Act.Next }, 2);
                _cardStep = step;
                _card = CardCtx.Tour;
            }
            Rect r = default(Rect);
            bool got = false;
            try { got = UiaControlCenter.TryGetAnchorRect(anchor, out r); }
            catch { got = false; }
            if (!got) r = default(Rect);   // the coach centres a zero-rect callout, with no ring
            _suppress++;
            try { TutorialCoach.OpenCallout(spec, r); }
            catch (Exception e) { ReportTickError("callout", e); }
            finally { _suppress--; }
            _cardOpenFrame = Time.frameCount;
            if (!CoachCardOpen()) EndTour(TLessonState.New);
        }

        private static void EndTour(TLessonState result)
        {
            if (_flow != Flow.Tour) return;
            bool firstRun = _activeTour;
            _flow = Flow.Idle;
            _tourAuto = false;
            _activeTour = false;
            if (_card == CardCtx.Tour || _card == CardCtx.TourOpener) { _card = CardCtx.None; CoachClose(); }
            _cardStep = null;
            // Played by the first-run tour: anything but Done sets it aside (it comes back once after).
            if (firstRun && result != TLessonState.Done) result = TLessonState.Skipped;
            TutorialProgressStore.SetState("menu", result);
            TutorialProgressStore.Flush();
            float now = Time.unscaledTime;
            _lastLessonEnd = now;
            if (!firstRun) return;
            // Close F10 so lesson 17's strip can show (the menu covers the strip), then move on. When the
            // F10 key itself ended the callouts (TutorialCoach: under a callout it closes the tour AND the
            // menu), the plugin's toggle runs after this tick in the same frame: CloseMenu marks the frame
            // so that toggle does not open F10 again over lesson 17's "Press F9" strip.
            CloseMenu("tour");
            TourAfterLesson(TutorialChapters.FindLesson("menu"), now);
        }

        // ================================================================ watch mode

        private static void StartWatch(TLesson l)
        {
            StopEverything(true);
            _flow = Flow.Watch;
            _watchLesson = l;
            _watchTrack = TrackForContext(l);
            _pageIdx = NextIndex(l, 0, _watchTrack, true);
            if (_pageIdx < 0) { EndWatch(); return; }
            ShowWatchCard();
        }

        private static int TrackForContext(TLesson l)
        {
            if (l == null || l.Id != "tools") return 0;
            return Held() is Tablet ? 1 : 0;
        }

        private static void ShowWatchCard()
        {
            var l = _watchLesson;
            if (l == null || _pageIdx < 0 || _pageIdx >= l.Steps.Length) { EndWatch(); return; }
            var step = l.Steps[_pageIdx];
            string heading = Text(step, "title");
            string body;
            string bodyField;          // the one key the body was read from; "" = composed (Says + Then)
            string demo = step.DemoId;
            if (step.Kind == TPresentation.Card) { bodyField = "body"; body = Text(step, bodyField); }
            else if (step.Kind == TPresentation.Callout) { bodyField = "callout"; body = Text(step, bodyField); }
            else
            {
                string say = ChooseSayField(step);
                bodyField = step.Has(say) ? say : (step.Has("says") ? "says" : "says@mp");
                body = Text(step, bodyField);
                string then = ChooseThenField(step);
                if (then != null) { body = body + "\n\n" + Text(step, then); bodyField = ""; }
                demo = DemoFor(step, say);
            }
            var spec = NewSpec(step.Id, heading, body, demo);
            SetEditKeys(spec, step, "title", bodyField);
            bool first = PrevIndex(l, _pageIdx - 1, _watchTrack, true) < 0;
            bool last = NextIndex(l, _pageIdx + 1, _watchTrack, true) < 0;
            if (last)
            {
                bool tryNow = l.Id != "entry" && ContextExists(l);
                if (tryNow && !first) SetButtons(spec, new[] { Act.TryNow, Act.Back, Act.Done }, 2);
                else if (tryNow) SetButtons(spec, new[] { Act.TryNow, Act.Done }, 1);
                else if (!first) SetButtons(spec, new[] { Act.Back, Act.Done }, 1);
                else SetButtons(spec, new[] { Act.Done }, 0);
            }
            else if (first) SetButtons(spec, new[] { Act.SkipLesson, Act.Next }, 1);
            else SetButtons(spec, new[] { Act.SkipLesson, Act.Back, Act.Next }, 2);
            _cardStep = step;
            OpenCard(CardCtx.Watch, spec);
            if (_card != CardCtx.Watch) EndWatch();
        }

        private static void EndWatch()
        {
            if (_flow == Flow.Watch) _flow = Flow.Idle;
            _watchLesson = null;
            if (_card == CardCtx.Watch) { _card = CardCtx.None; CoachClose(); }
            _cardStep = null;
        }

        // ================================================================ play requests

        private static void StartPlay(TLesson l, bool watch, float now)
        {
            if (watch) { StartWatch(l); return; }
            // Tour mode: the lesson the first-run tour stands at (GUIDE's Continue - the paused tour's
            // lesson reads IN PROGRESS - or Try it on it) continues the TOUR there, lesson and step. Any
            // other lesson plays outside the tour; once it is over, a tour still live this entry picks
            // up where it stood (TickTour).
            if (IsTourCurrent(l))
            {
                StopEverything(true);
                if (l.Id != "menu") CloseMenu("play");
                _remindPending = false;
                _tourLive = true;
                TourStartCurrent(now, false, 0UL);
                return;
            }
            switch (l.Id)
            {
                case "entry":
                    StopEverything(true);
                    OpenWelcome();
                    return;
                case "menu":
                    StopEverything(true);
                    if (!UiaControlCenter.IsOpen)
                    {
                        _f10OpenedByUsFrame = Time.frameCount;
                        try { UiaControlCenter.OpenOnTab("UI Themes"); } catch (Exception e) { ReportTickError("tour", e); }
                    }
                    if (UiaControlCenter.IsOpen) StartTour(false);
                    return;
                case "designer":
                {
                    // designer.card - Steps[0] is the tour's setup strip ("Press F9..."), which only
                    // makes sense inside the first-run tour.
                    StopEverything(true);
                    var card = FindStepIn(l, "designer.card");
                    if (card != null) OpenStepCard(CardCtx.Designer, card, null);
                    return;
                }
                case "whatsnew":
                    StopEverything(true);
                    OpenWhatsNew();
                    return;
            }
            StopEverything(true);
            // Live strips: F10 would cover them - close it (the player asked to try it now).
            CloseMenu("play");
            int track = TrackForContext(l);
            if (l.Id == "core")
            {
                // An interrupted First Steps continues where it stopped; a finished one replays.
                bool resume = StateOf("core") == TLessonState.Active;
                if (!resume) TutorialProgressStore.SetStep("core", 0);
                TutorialProgressStore.SetState("core", TLessonState.Active);
                if (!TutorialSafety.IsCalm()) { OpenHold(); return; }
                StartLesson(l, 0, resume ? TutorialProgressStore.GetStep("core") : 0, 0UL, false, now);
                return;
            }
            StartLesson(l, track, 0, 0UL, false, now);
        }

        /// <summary>The Hold-on card (0.4). <paramref name="tour"/>: it holds the first-run tour's next
        /// lesson back (HoldChoice then starts / pauses the tour instead of First Steps). The reason the
        /// safety gate gives (TutorialSafety.HoldReason - "suit: no air tank", "FREEZING") goes under the
        /// body, so the player learns WHY; "Start anyway" then really starts for a suit problem
        /// (TutorialSafety.AcceptChronic) - an emergency still holds the strip, saying so.</summary>
        private static void OpenHold(bool tour = false)
        {
            var entry = TutorialChapters.FindLesson("entry");
            var step = FindStepIn(entry, "entry.hold");
            if (step != null)
            {
                bool acute;
                string why = TutorialSafety.HoldReason(out acute);
                OpenStepCard(CardCtx.Hold, step, null, why != null ? "(" + TutorialTextStore.Sanitize(why) + ")" : null);
            }
            _holdTour = tour && _card == CardCtx.Hold;
        }

        // ================================================================ the first-run tour (tour mode)
        //
        // FlorpyDorp, 2026-09-26: "show all the lessons in order one after another when a player first
        // opens the mod". Welcome > Start the tour -> the overview cards -> Tour[0..16] back to back ->
        // entry.tourend. Its state and position are persisted (TutorialProgressStore TourState /
        // TourLessonId / TourStep / TourTrack); _tourLive is whether it runs in THIS world entry.

        /// <summary>Is <paramref name="l"/> the lesson the first-run tour stands at (running or paused)?</summary>
        private static bool IsTourCurrent(TLesson l)
        {
            if (l == null || TutorialProgressStore.TourState != TTourState.Running) return false;
            return string.Equals(TutorialProgressStore.TourLessonId, l.Id, StringComparison.Ordinal);
        }

        /// <summary>The lesson the running / paused tour stands at, or null.</summary>
        private static TLesson TourCurrentLesson()
        {
            if (TutorialProgressStore.TourState != TTourState.Running) return null;
            return TutorialChapters.FindLesson(TutorialProgressStore.TourLessonId);
        }

        /// <summary>Tour position of a lesson id: -1 = the overview (and anything unknown - a renamed
        /// lesson restarts the tour from its beginning), 0..Tour.Length-1 = its index in the tour.</summary>
        private static int TourIndexOf(string lessonId)
        {
            var tour = TutorialChapters.Tour;
            if (!string.IsNullOrEmpty(lessonId))
                for (int i = 0; i < tour.Length; i++)
                    if (tour[i] != null && string.Equals(tour[i].Id, lessonId, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static TLesson TourLessonAt(int idx)
        {
            var tour = TutorialChapters.Tour;
            if (idx < 0) return TutorialChapters.Overview;
            return idx < tour.Length ? tour[idx] : null;
        }

        private static bool TiersOn()
        {
            try { return HudConfig.DiegeticTiers == null || HudConfig.DiegeticTiers.Value; }
            catch { return true; }
        }

        private static bool RadialOn()
        {
            try { return UIAConfig.RadialEnabled == null || UIAConfig.RadialEnabled.Value; }
            catch { return true; }
        }

        private static bool GridOn()
        {
            try { return UIAConfig.GridEnabled == null || UIAConfig.GridEnabled.Value; }
            catch { return true; }
        }

        /// <summary>Lessons the tour passes over because their situation cannot exist for this player:
        /// lesson 15 (no suit power) for a robot; lessons 14 and 15 with the Visor HUD half off; lesson 15
        /// with the diegetic tiers off (the HUD never goes Bare then). Lesson 14 still plays with the tiers
        /// off: the sampler reads the tier as Suited (Robot) whatever the suit's power (HudSampler, "!UsesTiers"),
        /// so its setup (SuitPowered) holds and every visor element it points at is on screen. Also - as
        /// First Steps already skips its wheel / window moves when that half is off (HalfAvailableFor) -
        /// the wheel lessons with the RADIAL half off and the big-window lesson with the Universal
        /// Inventory off: their setups could never be met, and each would wait out a minute for nothing.</summary>
        private static bool TourSkips(TLesson l)
        {
            if (l == null) return true;
            switch (l.Trigger)
            {
                case TTrigger.Visor:
                    return !VisorOn();
                case TTrigger.Senses:
                    return !VisorOn() || !TiersOn() || IsRobot();
                case TTrigger.ReadWheel:
                case TTrigger.Tools:
                case TTrigger.Split:
                case TTrigger.DragDrop:
                case TTrigger.Gear:
                case TTrigger.Belts:
                case TTrigger.Bags:
                case TTrigger.Settings:
                case TTrigger.Speed:
                    return !RadialOn();
                case TTrigger.Window:
                    return !GridOn();
                default:
                    return false;
            }
        }

        /// <summary>Does the lesson, entered at <paramref name="step"/>, begin with a live strip (not a
        /// card)? Only then can "not calm" hold it back with the Hold-on card.</summary>
        private static bool StartsLive(TLesson l, int step, int track)
        {
            int i = NextIndex(l, step, track);
            return i >= 0 && l.Steps[i].Kind != TPresentation.Card;
        }

        /// <summary>Welcome &gt; Start the tour: the tour starts over from its overview - every lesson, in
        /// order, whatever state it is in (a replayed Welcome replays them all). What's new is covered by
        /// the tour (it stays in GUIDE). The overview's first card replaces the Welcome in place.</summary>
        private static void TourBegin(float now)
        {
            // A tour paused elsewhere (the Welcome replayed over it) starts over: the lesson it stood at
            // is no longer "in progress" (First Steps keeps its own resume point).
            var old = TourCurrentLesson();
            if (old != null && old.Id != "core" && StateOf(old.Id) == TLessonState.Active
                && !(_flow == Flow.Lesson && ReferenceEquals(_active, old)))
                TutorialProgressStore.SetState(old.Id, TLessonState.Offered);
            if (StateOf("whatsnew") == TLessonState.New) TutorialProgressStore.SetState("whatsnew", TLessonState.Done);
            int later = TutorialProgressStore.Counter("resumeLater");
            if (later > 0) TutorialProgressStore.Bump("resumeLater", -later);   // this tour's Welcome back gets its own two Laters
            ClearAside();   // a new tour: no track of lesson 3 is set aside yet
            // ...and every lesson it does not finish gets its one comeback afresh - an earlier tour's
            // spent comebacks ("jit:<id>") must not silence this one's (rare path: allocation is fine).
            var all = TutorialChapters.Lessons;
            for (int i = 0; all != null && i < all.Length; i++)
                if (all[i] != null) TutorialProgressStore.SetFlag(JitKey(all[i]), false);
            TutorialProgressStore.SetTourState(TTourState.Running);
            var start = TourLessonAt(-1) ?? TourLessonAt(0);
            TutorialProgressStore.SetTourAt(start != null ? start.Id : null, 0, 0);
            _remindPending = false;
            _tourEndDue = false;
            TourStartCurrent(now, true, 0UL);
        }

        /// <summary>"Teach me as I go" / "No lessons" on a (replayed) Welcome: no tour - the full
        /// just-in-time rules, not the after-the-tour once-rule. The lesson a running or paused tour
        /// stood at (stored Active - IN PROGRESS - and resumable only BY the tour, which is gone now) is
        /// settled like any interruption under those rules, so it is never stuck: Offered - it comes back
        /// just-in-time when its moment comes (under these rules a Skipped lesson never would); the F10
        /// tour New (it opens itself on the next F10 open); First Steps Skipped with its "as I go" twins
        /// (SkipStoredCore - "as I go" replaces it, and a stored-Active core would hold every lesson back).</summary>
        private static void TourForget(float now)
        {
            _tourLive = false;
            _tourEndDue = false;
            _holdTour = false;
            if (TutorialProgressStore.TourState == TTourState.None) return;
            var cur = TutorialProgressStore.TourState == TTourState.Running ? TourCurrentLesson() : null;
            TutorialProgressStore.SetTourState(TTourState.None);
            TutorialProgressStore.SetTourAt(null, 0, 0);
            ClearAside();   // the once-rule is gone with the tour
            _remindPending = false;
            bool running = cur != null
                && ((_flow == Flow.Lesson && ReferenceEquals(_active, cur)) || (_flow == Flow.Tour && cur.Id == "menu"));
            if (cur != null && !running && StateOf(cur.Id) == TLessonState.Active)
            {
                if (cur.Id == "core") SkipStoredCore(now);
                else TutorialProgressStore.SetState(cur.Id, cur.Id == "menu" ? TLessonState.New : TLessonState.Offered);
            }
        }

        /// <summary>Start (or continue) the tour at its saved position. Lessons it passes over
        /// (<see cref="TourSkips"/>) are set aside on the way; past the last one the tour completes.
        /// Resume points: lesson 16 always from its setup (F10 has to be open for its callouts); a
        /// context-bound lesson whose situation is gone from its track's setup. <paramref name="fromCard"/>:
        /// called from a card the player just answered (Welcome, Welcome back, the overview's last card) -
        /// not calm there, the Hold-on card takes that card's place (pause kept). <paramref name="seed"/>:
        /// evidence the lesson starts with (lesson 17 started by the F9 press itself).</summary>
        private static void TourStartCurrent(float now, bool fromCard, ulong seed)
        {
            var tour = TutorialChapters.Tour;
            int idx = TourIndexOf(TutorialProgressStore.TourLessonId);
            int step = TutorialProgressStore.TourStep, track = TutorialProgressStore.TourTrack;
            TLesson l = null;
            for (int guard = 0; guard <= tour.Length + 1; guard++)
            {
                if (idx >= tour.Length) break;
                l = TourLessonAt(idx);
                if (l != null && !TourSkips(l)) break;
                if (l != null)
                {
                    var st = StateOf(l.Id);
                    if (st != TLessonState.Done && st != TLessonState.Learned)
                        TutorialProgressStore.SetState(l.Id, TLessonState.Skipped);   // set aside: back once, if it ever applies
                }
                l = null;
                idx++;
                step = 0;
                track = 0;
            }
            if (l == null) { ReleaseCardCtx(); TourComplete(now); return; }
            if (l.Id == "menu") { step = 0; track = 0; }
            else if (step > 0 && l.ContextBound && !ContextExists(l, track))
            {
                // The track's setup: get back into the situation first (lesson 3 per TRACK - resuming its
                // tablet track with a Drill in hand rewinds to "hold the tablet").
                int first = NextIndex(l, 0, track);
                step = first >= 0 ? first : 0;
            }
            TutorialProgressStore.SetTourAt(l.Id, step, track);
            _tourLive = true;
            _tourRetryAt = now + 1f;
            if (fromCard && StartsLive(l, step, track) && !TutorialSafety.IsCalm())
            {
                if (StateOf(l.Id) != TLessonState.Active) TutorialProgressStore.SetState(l.Id, TLessonState.Active);
                OpenHold(true);
                return;
            }
            // Let the answered card go without closing it: the lesson's own first card swaps in place;
            // a strip lets the coach close the card when the press returns.
            ReleaseCardCtx();
            StartLesson(l, track, step, seed, false, now, true);
        }

        /// <summary>A lesson the tour played ended (Done / Skipped): straight on to the next one - no
        /// gap, no queue. Only the overview's last card may still meet "not calm" (the Hold-on card, as
        /// the Welcome's Start did before First Steps); later lessons have their own "Lesson on hold".</summary>
        private static void TourAfterLesson(TLesson l, float now)
        {
            if (l == null || !IsTourCurrent(l)) return;   // the tour ended meanwhile (Stop the tour / Stop all)
            if (!TourMoveNext(l, now)) return;            // that was the last one: the tour completed
            if (!_tourLive) { TourPark(); return; }
            // The overview's cards may have been read over F10 (a Welcome started from GUIDE): the live
            // lessons begin now, and F10 would cover their strips.
            if (l.IsOverview) CloseMenu("tour");
            TourStartCurrent(now, _inPress && l.IsOverview, 0UL);
        }

        /// <summary>GUIDE skipped the lesson a paused tour stands at: the tour moves on to the next one
        /// (it starts right away when the tour is live this entry - TickTour - else it waits there).</summary>
        private static void TourMoveOn(TLesson l, float now)
        {
            if (l == null || !IsTourCurrent(l)) return;
            if (TourMoveNext(l, now) && !_tourLive) TourPark();
        }

        /// <summary>Save the tour's position as the lesson after <paramref name="l"/> (the overview ->
        /// First Steps), from its first step. False when <paramref name="l"/> was the last: the tour
        /// completed instead.</summary>
        private static bool TourMoveNext(TLesson l, float now)
        {
            var tour = TutorialChapters.Tour;
            int idx = l.IsOverview ? 0 : TourIndexOf(l.Id) + 1;
            if (!l.IsOverview && idx <= 0) return false;   // not a tour lesson (cannot be current - defensive)
            if (idx >= tour.Length || tour[idx] == null) { TourComplete(now); return false; }
            TutorialProgressStore.SetTourAt(tour[idx].Id, 0, 0);
            return true;
        }

        /// <summary>Pause the tour where it stands: the lesson there reads IN PROGRESS (Active - GUIDE's
        /// Continue names it, RepairStaleActive keeps it) and the Welcome-back card offers it.</summary>
        private static void TourPark()
        {
            _tourLive = false;
            var cur = TourCurrentLesson();
            if (cur != null && StateOf(cur.Id) != TLessonState.Active) TutorialProgressStore.SetState(cur.Id, TLessonState.Active);
        }

        /// <summary>The tour ENDS (Stop the tour, Stop all lessons, Welcome back's Stop lessons or its
        /// third Later): from now on a lesson it did not finish comes back just-in-time, once
        /// (<see cref="PostTourOfferable"/>). A lesson it stood at that is not running is settled:
        /// <paramref name="setAside"/> = Skipped (First Steps: its "as I go" twins, SkipStoredCore); else
        /// like any interruption - Offered (First Steps stays Active: its Welcome back offers it). Lesson 3
        /// standing there (running or paused): its current track and the one after it never finished -
        /// set aside, so after the tour those may come back and a finished one may not (TrackEligible).</summary>
        private static void TourEnd(bool setAside, float now)
        {
            _tourLive = false;
            if (TutorialProgressStore.TourState != TTourState.Running) return;
            var cur = TourCurrentLesson();
            bool running = cur != null
                && ((_flow == Flow.Lesson && ReferenceEquals(_active, cur)) || (_flow == Flow.Tour && cur.Id == "menu"));
            if (HasTracks(cur)) AsideFrom(cur, running ? _track : TutorialProgressStore.TourTrack);   // (before SetTourAt clears it)
            TutorialProgressStore.SetTourState(TTourState.Ended);
            TutorialProgressStore.SetTourAt(null, 0, 0);
            if (cur != null && !running && StateOf(cur.Id) == TLessonState.Active)
            {
                if (cur.Id == "core") { if (setAside) SkipStoredCore(now); }
                else TutorialProgressStore.SetState(cur.Id, setAside ? TLessonState.Skipped : TLessonState.Offered);
            }
            TutorialProgressStore.Flush();
        }

        /// <summary>Past the last lesson: the tour is over (Ended) and its closing card is due.</summary>
        private static void TourComplete(float now)
        {
            TutorialProgressStore.SetTourState(TTourState.Ended);
            TutorialProgressStore.SetTourAt(null, 0, 0);
            TutorialProgressStore.Flush();
            _tourLive = false;
            _tourEndDue = true;
            TryOpenTourEnd();
        }

        /// <summary>entry.tourend, once the screen is clear: no lesson, no card, no F10, no open wheel, no
        /// Universal Inventory, nothing that suspends lessons - the HUD Designer or Handbook lesson 17's
        /// card just opened included. Inside a card press our own pressed card is the only thing up: the
        /// closing card takes its place. Retried every frame while due (TickTour; bool reads only).</summary>
        private static void TryOpenTourEnd()
        {
            if (!_tourEndDue) return;
            if (_flow != Flow.Idle || _card != CardCtx.None || _preview != null || _pendingOpenDesigner) return;
            bool blocked;
            try
            {
                blocked = UiaControlCenter.IsOpen || RadialController.AnyRadialOpen || TheGridPanel.IsOpen
                    || (_inPress ? SuspendedExceptCoach() : Suspended());
            }
            catch { blocked = true; }
            if (blocked) return;
            _tourEndDue = false;
            var step = FindStepIn(TutorialChapters.FindLesson("entry"), "entry.tourend");
            if (step != null) OpenStepCard(CardCtx.TourEnd, step, null);
        }

        /// <summary>entry.tourend: Start playing / See all lessons (F10 &gt; Guide).</summary>
        private static void TourEndChoice(int b)
        {
            CloseCardCtx();
            if (b == 1) OpenGuideTab();
        }

        /// <summary>Per frame, allocation-free: the closing card when due; and a tour live this entry
        /// whose lesson is not running (an explicit Watch / Try it / uiatutorial play ran in between, or
        /// a skip from GUIDE) picks up where it stands - once F10 is closed (except for lesson 16, whose
        /// setup IS "open F10") and nothing else is up. Retried at most once a second.</summary>
        private static void TickTour(float now)
        {
            if (_tourEndDue) { TryOpenTourEnd(); return; }
            if (!_tourLive) return;
            if (_flow != Flow.Idle || _card != CardCtx.None || _pendingPlay != null) return;
            if (TutorialProgressStore.TourState != TTourState.Running) { _tourLive = false; return; }
            if (now < _tourRetryAt) return;
            if (Suspended()) return;
            if (UiaControlCenter.IsOpen)
            {
                var cur = TourCurrentLesson();
                if (cur == null || cur.Id != "menu") return;
            }
            _tourRetryAt = now + 1f;
            TourStartCurrent(now, false, 0UL);
        }

        // ================================================================ lessons

        private static TStep CurrentStep()
        {
            if (_active == null || _stepIdx < 0 || _stepIdx >= _active.Steps.Length) return null;
            return _active.Steps[_stepIdx];
        }

        /// <summary>The next step of <paramref name="track"/> at or after <paramref name="from"/>, or -1.
        /// <paramref name="skipSetup"/>: pass over setup steps (tour mode) - Watch mode replays what a
        /// lesson TEACHES, never "get into the situation first".</summary>
        private static int NextIndex(TLesson l, int from, int track, bool skipSetup = false)
        {
            if (l == null) return -1;
            for (int i = Mathf.Max(0, from); i < l.Steps.Length; i++)
                if (l.Steps[i].Track == track && !(skipSetup && l.Steps[i].IsSetup)) return i;
            return -1;
        }

        private static int PrevIndex(TLesson l, int from, int track, bool skipSetup = false)
        {
            if (l == null) return -1;
            for (int i = Mathf.Min(from, l.Steps.Length - 1); i >= 0; i--)
                if (l.Steps[i].Track == track && !(skipSetup && l.Steps[i].IsSetup)) return i;
            return -1;
        }

        /// <summary>Run a lesson live from <paramref name="stepIdx"/>. <paramref name="tour"/> (tour mode):
        /// the first-run tour plays it - its position is saved on every step, and its end moves the tour
        /// on (<see cref="TourAfterLesson"/>).</summary>
        private static void StartLesson(TLesson l, int track, int stepIdx, ulong seed, bool offered, float now, bool tour = false)
        {
            if (l == null) return;
            HideStrip();
            SpotClear();
            _flow = Flow.Lesson;
            _active = l;
            _activeAuto = offered;   // the scheduler's offer; First Steps / GUIDE / console starts are the player's own
            _activeTour = tour;
            // Tour mode: lesson 3's tracks this tour already set aside - persisted as they happened, so a
            // tour resumed on the tablet track (quit / F6 after the tool track was set aside) still ends
            // the lesson set aside. (A new tour clears them - TourBegin.) Outside the tour: none.
            _asideTracks = tour ? StoredAside(l) : 0;
            _setupOk = false;
            _setupStackFor = 0f;
            _track = l.Id == "tools" ? Mathf.Clamp(track, 0, 1) : 0;
            _stepIdx = NextIndex(l, stepIdx, _track);
            _doSteps = 0;
            _doDone = 0;
            _doStepsAtTrack = _doDoneAtTrack = 0;
            _skipStow = false;
            _windowWaitClose = false;
            _windowOpen = false;
            _windowHeld = null;
            _confirming = false;
            _evPending = seed;
            if (offered) TutorialProgressStore.NoteOffered(l.Id);
            TutorialProgressStore.SetState(l.Id, TLessonState.Active);
            TutorialProgressStore.SetTrack(l.Id, _track);
            if (l.Id == "core")
            {
                _remindPending = false;   // it runs now: no Welcome back is owed
                if (StateOf("entry") != TLessonState.Done) TutorialProgressStore.SetState("entry", TLessonState.Done);
            }
            if (_stepIdx < 0) { FinishLesson(now); return; }
            EnterStep(now);
        }

        private static void EnterStep(float now)
        {
            var step = CurrentStep();
            if (step == null) { FinishLesson(now); return; }

            // Moves that cannot be done now are passed over FIRST - before this step takes the evidence
            // window (_evPending, the baseline hand, the hover dwells), so whatever the player did during
            // the last Then line carries on to the move actually entered.
            _branchField = null;
            _oopsField = null;
            // Tour mode: where the first-run tour stands (resume point), saved on every step it enters.
            if (_activeTour && _active != null) TutorialProgressStore.SetTourAt(_active.Id, _stepIdx, _track);
            // Tour mode: a setup step whose situation already exists is passed over silently - no strip,
            // no flash (the wheel is open, the tool is in hand, F10 is up...).
            if (step.IsSetup && SetupHoldsOnEntry(step))
            {
                AdvanceStep(now);
                return;
            }
            // Lesson 16's callouts run in the F10-tour flow (its setup got F10 open).
            if (step.Kind == TPresentation.Callout)
            {
                EnterCallouts();
                return;
            }
            // A half the player switched off (F10's master switches) cannot be practised: its First
            // Steps moves are skipped (shown, not learned) instead of waiting out a minute each.
            if (_active != null && _active.Id == "core" && !HalfAvailableFor(step))
            {
                if (step.Id == "core.take") _skipStow = true;   // no tool was taken, so 1.5 has nothing to put back
                AdvanceStep(now);
                return;
            }
            // The key this move tells the player to press is UNBOUND: it cannot be done as written. S1
            // at once - shown, not learned: counted as a do-step nobody did (a just-in-time lesson comes
            // back as Offered), and a First Steps move's "as I go" twin may fire once the key is bound.
            if (!OpenerBound(step))
            {
                if (step.IsDoStep && !step.IsSetup) _doSteps++;
                NoteShownNotLearned(step);
                AdvanceStep(now);
                return;
            }
            if (step.Id == "tools.tablet.install" && TabletHasCartridge())
            {
                AdvanceStep(now);   // the slot is already full: straight to 3.3T
                return;
            }
            // 1.5 needs something in the active hand (plan: "1.4 done and the active hand holds
            // something"); 1.4's no-tools branch skips it outright - unless [G] already put it away
            // during 1.4's Then line: that IS 1.5, done (credited below).
            bool stowCredited = step.Id == "core.stow" && (_evPending & EvStowOk) != 0;
            if (step.Id == "core.stow" && !stowCredited && (_skipStow || Held() == null))
            {
                // Not taught now - its "as I go" twin (tip 3: G puts it back) may fire later.
                _skipStow = false;
                _stowItem = null;
                TutorialProgressStore.SetFlag("s1:core.stow", true);
                AdvanceStep(now);
                return;
            }

            // Entered. The evidence window opened when the previous move was DONE (Complete): the
            // actions raised during its Then line count here, and 1.4's "a different tool in hand" is
            // measured from the hand at that moment. A lesson's first move opens its window now. Hover
            // dwells are NOT carried: 1.3 is "read before you click" - pointing at wedges while 1.2's
            // Then line showed is incidental, not reading a text that was not on screen yet.
            _stepActive = 0f;
            _stepLive = 0f;
            _stepWait = 0f;
            _pulseNext = PulseAfterSeconds;
            _ev = _evPending;
            _evPending = 0UL;
            _hoverDwells = 0;
            _lastHoverAt = -1f;
            _handStartId = _windowOpen ? _windowHand : HandId();
            var heldAtWindow = _windowOpen ? _windowHeld : null;
            _windowOpen = false;
            _windowHeld = null;
            _arrowSeenThisStep = false;
            _flagA = _flagB = false;
            _oopsUntil = 0f;
            _onHold = false;
            _holdWhy = null;   // (the 4 Hz poll names it again while not calm)
            _unsafeFor = _calmFor = _contextLostFor = 0f;
            _northFor = 0f;
            _gridFreeFor = 0f;
            _drainShown = -2f;
            _setupOk = false;        // a setup's state is read afresh (PollSetup, 4 Hz)
            _setupStackFor = 0f;

            // Step-specific setup.
            // 1.4's no-tools branch - unless the take already happened in 1.3's Then line (which may
            // have emptied the belt of its last tool).
            if (step.Id == "core.take" && !BeltHasTools() && !TookTool()) _branchField = "branch@nobelt";
            if (step.Id == "core.stow")
            {
                // What 1.5 puts away: the item in hand now - or, credited, the one held when 1.4 was done.
                _stowItem = stowCredited ? (heldAtWindow ?? Held()) : Held();
                _skipStow = false;
            }
            // A setup is never one of the lesson's moves: not counted as a do-step (so a lesson whose
            // only "done" was getting into the situation still reads as ignored).
            if (step.IsDoStep && !step.IsSetup) _doSteps++;

            _sayField = ChooseSayField(step);
            _stepRead = ReadSecondsFor(step);
            _header = LessonHeader();
            if (_active != null && _active.Id == "core") TutorialProgressStore.SetStep("core", _stepIdx);

            if (step.Kind == TPresentation.Card)
            {
                HideStrip();
                SpotClear();
                OpenLessonCard(step);
                return;
            }
            if (!Suspended()) PresentStrip(step);
        }

        /// <summary>A lesson's CARD step. Tour mode: First Steps' last card (core.finish) in the
        /// first-run tour is the hand-over to lesson 2 - body@tour, and chrome|tournext ("Next lesson")
        /// / chrome|tourstop ("Stop the tour") instead of its own button@0 / @1.</summary>
        private static void OpenLessonCard(TStep step)
        {
            if (step == null) return;
            if (!_activeTour || step.Id != "core.finish")
            {
                OpenStepCard(CardCtx.LessonCard, step, null);
                return;
            }
            _cardStep = step;
            string field = step.Has("body@tour") ? "body@tour" : "body";
            var spec = NewSpec(step.Id, Text(step, "heading"), Text(step, field), step.DemoId);
            SetEditKeys(spec, step, "heading", field);
            SetButtons(spec, new[] { Act.TourNext, Act.TourStop }, 0);
            _cardBodyField = field;
            OpenCard(CardCtx.LessonCard, spec);
        }

        private static void AdvanceStep(float now)
        {
            var step = CurrentStep();
            if (step != null && step.Id == "core.take" && _branchField != null) _skipStow = true;
            SpotClear();
            _stepIdx = NextIndex(_active, _stepIdx + 1, _track);
            if (_stepIdx < 0) { FinishLesson(now); return; }
            EnterStep(now);
        }

        /// <summary>All steps shown: Done - unless every do-step only self-skipped (the player ignored
        /// the lesson), which makes it Offered so it may come back (max 3 offers, 10+ min apart).
        /// Tour mode: lesson 3 in the first-run tour plays its tablet track after the tool track; a
        /// lesson with a track set aside (a setup self-skipped, S4, or - lesson 3 - a track only shown)
        /// ends set aside (Skipped), and in the tour an ignored lesson is set aside too (EndLesson) - it
        /// comes back once, just-in-time, after the tour (lesson 3: only a track set aside, if one was
        /// recorded - TrackEligible).</summary>
        private static void FinishLesson(float now)
        {
            if (_active == null) { ClearFlow(); return; }
            // Tour mode, lesson 3: a track whose moves were all only shown (each self-skipped) was ignored
            // - set aside like one whose setup never came about, so after the tour THAT track comes back
            // (the lesson-wide rule below would call the lesson Done once the other track was done).
            if (_activeTour && HasTracks(_active) && TrackIgnored()) SetTrackAside();
            if (_activeTour && TourNextTrack(now)) return;
            TLessonState result = TLessonState.Done;
            if (_asideTracks != 0) result = TLessonState.Skipped;
            else if (_active.Id != "core" && _doSteps > 0 && _doDone == 0
                && (_activeTour || TutorialProgressStore.GetOffers(_active.Id) < MaxOffers))
                result = TLessonState.Offered;
            EndLesson(result);
        }

        /// <summary>Tour mode: lesson 3 (two tracks) in the first-run tour - the tool track is over (done,
        /// or set aside): the tablet track follows, opening with its own setup. False when there is no
        /// further track (the lesson ends).</summary>
        private static bool TourNextTrack(float now)
        {
            var l = _active;
            if (l == null || !_activeTour || _track != 0) return false;
            int first = NextIndex(l, 0, 1);
            if (first < 0) return false;
            HideStrip();
            SpotClear();
            _confirming = false;
            _windowWaitClose = false;
            _track = 1;
            _doStepsAtTrack = _doSteps;   // the tablet track's own "only shown" count starts here
            _doDoneAtTrack = _doDone;
            TutorialProgressStore.SetTrack(l.Id, 1);
            _stepIdx = first;
            EnterStep(now);
            return true;
        }

        private static void EndLesson(TLessonState result)
        {
            var l = _active;
            bool tour = _activeTour;
            float now = Time.unscaledTime;
            HideStrip();
            SpotClear();
            if (_card == CardCtx.LessonCard) { _card = CardCtx.None; CoachClose(); }
            // The first-run tour sets a lesson it could not finish ASIDE - Skipped, not Offered: after
            // the tour it comes back once, just-in-time (the tour's closing card promises that).
            if (tour && result == TLessonState.Offered) result = TLessonState.Skipped;
            if (l != null)
            {
                TutorialProgressStore.SetState(l.Id, result);
                // Lesson 3 finished: no track of it is waiting for a comeback any more.
                if (result == TLessonState.Done && HasTracks(l)) ClearAside();
                if (l.Id == "core")
                {
                    _coreEndedAt = now;
                    _remindPending = false;
                    // Skipped: the moves not done yet were never learned - their "as I go" twins may
                    // still fire (plan A.7). A move whose Then line is showing WAS done.
                    if (result == TLessonState.Skipped) FlagCoreTwinsFrom(_confirming ? _stepIdx + 1 : _stepIdx);
                    // Finished or skipped, First Steps replays from its first move (only an
                    // interrupted - still Active - one resumes where it stopped).
                    TutorialProgressStore.SetStep("core", 0);
                    if (result == TLessonState.Done)
                        try { if (UIAConfig.TutorialCompleted != null) UIAConfig.TutorialCompleted.Value = true; } catch { }
                }
                TutorialProgressStore.Flush();
            }
            ClearFlow();
            _lastLessonEnd = now;
            // The first-run tour moves on - straight into the next lesson (no gap, no queue).
            if (tour && l != null) TourAfterLesson(l, now);
        }

        /// <summary>First Steps skipped from move <paramref name="from"/> on: flag the "as I go" twin of
        /// each move not done (the tip that stands in for it, see TipTwinFlags) - shown at most, never
        /// learned. Rare (a skip), so the key concatenation is fine.</summary>
        private static void FlagCoreTwinsFrom(int from)
        {
            var core = TutorialChapters.FindLesson("core");
            if (core == null || core.Steps == null) return;
            for (int i = Mathf.Max(0, from); i < core.Steps.Length; i++)
            {
                var s = core.Steps[i];
                if (s == null) continue;
                string flag = "s1:" + s.Id;
                for (int t = 1; t < TipTwinFlags.Length; t++)
                    if (string.Equals(TipTwinFlags[t], flag, StringComparison.Ordinal))
                    {
                        TutorialProgressStore.SetFlag(flag, true);
                        break;
                    }
            }
        }

        /// <summary>First Steps is stored Active but not running (quit mid-lesson, its Welcome back
        /// answered Later, multiplayer): end it as Skipped, like the GUIDE skip of a running one - the
        /// moves from the one it stopped at get their twins, and it replays from the start.</summary>
        private static void SkipStoredCore(float now)
        {
            FlagCoreTwinsFrom(TutorialProgressStore.GetStep("core"));
            TutorialProgressStore.SetState("core", TLessonState.Skipped);
            TutorialProgressStore.SetStep("core", 0);
            _remindPending = false;
            _coreEndedAt = now;
        }

        /// <summary>GUIDE Skip / Stop all with a lesson STORED Active that is not running (the running one
        /// - and the running tour - are left to their own path): it ends as Skipped. <paramref name="firstOnly"/>:
        /// just the one GUIDE's Continue / Skip buttons name, the first Active in list order.</summary>
        private static void SkipStoredActive(bool firstOnly)
        {
            var ls = TutorialChapters.Lessons;
            bool any = false;
            for (int i = 0; ls != null && i < ls.Length; i++)
            {
                var l = ls[i];
                if (l == null || string.IsNullOrEmpty(l.Id)) continue;
                if (_flow == Flow.Lesson && ReferenceEquals(_active, l)) continue;
                if (_flow == Flow.Tour && l.Id == "menu") continue;
                if (StateOf(l.Id) != TLessonState.Active) continue;
                bool tourAt = IsTourCurrent(l);
                // Lesson 3 paused in the tour: the track it stood at (and the one after) never finished.
                if (tourAt) AsideFrom(l, TutorialProgressStore.TourTrack);
                if (l.Id == "core") SkipStoredCore(Time.unscaledTime);
                else TutorialProgressStore.SetState(l.Id, TLessonState.Skipped);
                DropQueued(l.Id);
                // Tour mode: that was the lesson a paused first-run tour stood at - the tour moves on.
                if (tourAt) TourMoveOn(l, Time.unscaledTime);
                any = true;
                if (firstOnly) break;
            }
            if (any) TutorialProgressStore.Flush();
        }

        /// <summary>S4: the reason for the lesson went away - shelve it (Offered; it comes back later).
        /// Tour mode: in the first-run tour it is set aside (Skipped - lesson 3 first gets its other
        /// track) and the tour moves on.</summary>
        private static void ShelveLesson()
        {
            if (_active == null) return;
            if (_activeTour)
            {
                SetTrackAside();   // (lesson 3: this track - persisted, see AsideKey)
                if (TourNextTrack(Time.unscaledTime)) return;
                EndLesson(TLessonState.Skipped);
                return;
            }
            EndLesson(TutorialProgressStore.GetOffers(_active.Id) < MaxOffers ? TLessonState.Offered : TLessonState.Done);
        }

        private static string LessonTitle(TLesson l)
        {
            if (l == null) return "";
            return TutorialTokens.Resolve(TutorialTextStore.Get(l.TitleKey));
        }

        /// <summary>"FIRST STEPS - 3/8": strip steps only (cards and setup steps are not counted), per
        /// track; a setup step shows the title alone (it is not one of the lesson's moves). Tour mode:
        /// in the first-run tour the header is chrome|tourheader ("LESSON 3/17 - TOOLS AND BATTERIES").
        /// Built on step entry only (allocates).</summary>
        private static string LessonHeader()
        {
            var l = _active;
            if (l == null) return "";
            string titleKey = (l.TrackTitleKey != null && _track == 1) ? l.TrackTitleKey : l.TitleKey;
            if (_activeTour && l.TourOrder > 0) return TourHeader(l, titleKey);
            string title = TutorialTokens.Resolve(TutorialTextStore.Get(titleKey));
            var cur = CurrentStep();
            if (cur != null && cur.IsSetup) return title;
            int n = 0, total = 0;
            for (int i = 0; i < l.Steps.Length; i++)
            {
                var s = l.Steps[i];
                if (s.Track != _track || s.Kind == TPresentation.Card || s.IsSetup) continue;
                total++;
                if (i <= _stepIdx) n = total;
            }
            return total > 0 ? title + " - " + n + "/" + total : title;
        }

        /// <summary>chrome|tourheader with its placeholders swapped in BEFORE the token resolve (like
        /// chrome|reopen's {OPENER}): {N} = the lesson's TourOrder, {TOTAL} = the tour's length, {TITLE}
        /// = its title (lesson 3's tablet track: the track title). {TITLE} goes last, so a title can
        /// never feed the other two.</summary>
        private static string TourHeader(TLesson l, string titleKey)
        {
            var ci = CultureInfo.InvariantCulture;
            string raw = TutorialTextStore.Get("chrome|tourheader")
                .Replace("{N}", l.TourOrder.ToString(ci))
                .Replace("{TOTAL}", TutorialChapters.Tour.Length.ToString(ci))
                .Replace("{TITLE}", TutorialTextStore.Get(titleKey));
            return TutorialTokens.Resolve(raw);
        }

        private static string ChooseSayField(TStep step)
        {
            if (step == null) return "says";
            switch (step.Id)
            {
                case "core.hands":
                    if (ActiveHandEmpty()) return "says@empty";
                    if (BothHandsFull()) return "says@bothfull";
                    return "says";
                case "core.stow":
                    return IsSimpleStow() ? "says" : "says@complex";
                case "tools.open":
                    return HeldIsLowPowerTool() ? "says@low" : "says";
                case "tools.careful":
                case "tools.replace":
                    return HeldIsCanisterTool() ? "says@canister" : "says";
                case "split.open":
                case "split.count":
                case "split.window":
                    return IsMpClient() ? "says@client" : "says";
                case "stow.order":
                case "stow.profiles":
                case "names.how":
                    return IsSimpleStow() ? "says" : "says@complex";
                case "settings.keepopen":
                    return ShiftInverted() ? "says@inverted" : "says";
                case "visor.compass":
                case "visor.lowpower":
                    return IsRobot() ? "says@robot" : "says";
            }
            return "says";
        }

        private const string DemoToolReplace = "toolreplace";
        private const string DemoToolReplaceCanister = "toolreplace.canister";

        /// <summary>The demo for the variant on screen - a constant id, never built (zero-alloc): the
        /// canister wording of 3.2 / 3.3 (<c>says@canister</c>, a held canister tool) shows the
        /// canister swap instead of the battery one. The tablet track already names
        /// <c>toolreplace.cartridge</c> in its data. Anything else shows the step's own demo.</summary>
        private static string DemoFor(TStep step, string sayField)
        {
            if (step == null) return null;
            if (sayField == "says@canister" && step.DemoId == DemoToolReplace && step.Has(sayField))
                return DemoToolReplaceCanister;
            return step.DemoId;
        }

        private static string ChooseThenField(TStep step)
        {
            if (step == null) return null;
            switch (step.Id)
            {
                case "core.stow":
                    return StowWentHome() ? "then" : "then@elsewhere";
                case "bags.root":
                    return BagKeyIsScoreboard() ? "then" : "then@rebound";
                case "stow.where":
                    return IsSimpleStow() ? "then" : "then@complex";
                case "settings.keepopen":
                    return ShiftInverted() ? "then@inverted" : "then";
            }
            return step.Has("then") ? "then" : null;
        }

        private static bool ShiftInverted()
        {
            try { return UIAConfig.RadialShiftKeepsOpen != null && !UIAConfig.RadialShiftKeepsOpen.Value; }
            catch { return false; }
        }

        /// <summary>8.1's Then variant: holding the bag key still shows the scoreboard only while the
        /// bag key IS vanilla's scoreboard key (BagRadialFeature.OwnsVanillaKey) and a tap opens the wheel.</summary>
        private static bool BagKeyIsScoreboard()
        {
            try
            {
                return UIAConfig.BagRadialKey != null && UIAConfig.BagRadialKey.Value == KeyMap.ShowScoreBoard
                    && (UIAConfig.BagRadialTapOpens == null || UIAConfig.BagRadialTapOpens.Value);
            }
            catch { return true; }
        }

        /// <summary>1.5's Then: did the stowed tool land back on the worn toolbelt? When that cannot be
        /// read yet, the plain line wins ("then" - it makes no claim about where the tool went): on a
        /// multiplayer client SmartStowed is optimistic (the server's move has not come back when the
        /// step completes), and an item still in a hand has not landed anywhere.</summary>
        private static bool StowWentHome()
        {
            try
            {
                if (IsMpClient()) return true;
                var item = _stowItem;
                var h = InventoryManager.ParentHuman;
                if (item == null || h == null || h.ToolbeltSlot == null) return true;
                var slot = item.ParentSlot;
                if (slot == null || ReferenceEquals(slot, h.LeftHandSlot) || ReferenceEquals(slot, h.RightHandSlot)) return true;
                var belt = h.ToolbeltSlot.Get();
                return belt != null && ReferenceEquals(slot.Parent, belt);
            }
            catch { return true; }
        }

        // ================================================================ lesson tick

        private static void TickFlow(float now, float dt)
        {
            switch (_flow)
            {
                case Flow.Lesson: TickLesson(now, dt); break;
                case Flow.Tip:
                case Flow.Invite:
                case Flow.Notice: TickTimed(now, dt); break;
            }
        }

        private static void TickLesson(float now, float dt)
        {
            var step = CurrentStep();
            if (step == null) { FinishLesson(now); return; }
            if (_card != CardCtx.None)
            {
                // The lesson's card (or another card) drives now; a confirm in flight waits for it.
                if (_confirming) _confirmStart += dt;
                return;
            }
            if (step.Kind == TPresentation.Card)
            {
                // A card step whose card went away (an editor preview replaced it, or it failed to
                // open): bring it back as soon as nothing else is on screen (retry at most 1/s) - F10
                // included: nothing opens on its own over the menu.
                if (!Suspended() && !UiaControlCenter.IsOpen && now >= _cardRetryAt)
                {
                    _cardRetryAt = now + 1f;
                    OpenLessonCard(step);
                }
                return;
            }
            bool suspended = Suspended();
            // The lesson editor (uiadev) open over this LIVE step: its clocks stand still - S1
            // self-skip, the S2 read and its drain line, "or N s" timeouts, S4 context-lost, the oops
            // and waiting timers - so the words on screen can be edited without the step moving on
            // (typing in the editor also closes the wheel a step may need). They resume where they
            // stopped when the editor closes. Done-conditions still count.
            bool frozen = EditorFreeze();
            if (_confirming)
            {
                // Never hide mid-confirm: the strip freezes its own confirm clock while it is stood down
                // (vanilla menu, F9, F10, a card), and a Hide would drop the callback. The watchdog only
                // runs while nothing covers it - F10 included, exactly the strip's own stand-down set -
                // so a long menu visit can neither skip the player's "Then" line nor advance the lesson
                // (and open 1.9's card) underneath the menu.
                if (suspended || frozen || UiaControlCenter.IsOpen) { _confirmStart += dt; return; }
                if (now - _confirmStart > _confirmMin + ConfirmWatchdogExtra) { _confirming = false; AfterConfirm(now); }
                return;
            }
            if (suspended) { HideStrip(); return; }
            if (_windowWaitClose) { TickWindowWait(now, dt, step); return; }

            if (!_stripShowing) PresentStrip(step);

            // Safety gate (plan A.5): not calm for 2 s -> on hold; calm again for 5 s -> resume. The hold
            // line names the reason (_holdWhy). A chronic suit problem stops counting as "not calm" after
            // its grace (TutorialSafety), so it can never hold the lesson - or the tour - indefinitely;
            // an emergency holds for as long as it lasts.
            if (!_calmNow) { _unsafeFor += dt; _calmFor = 0f; }
            else { _calmFor += dt; _unsafeFor = 0f; }
            if (!_onHold && _unsafeFor >= HoldAfterSeconds) _onHold = true;
            else if (_onHold && _calmFor >= ResumeAfterSeconds) { _onHold = false; _holdWhy = null; _stepWait = 0f; }

            // S4: the reason for a context-bound lesson went away (tool put down, suit power changed;
            // lesson 3 per TRACK - a Drill swapped in on the tablet track, or the reverse).
            if (_active.ContextBound && StepChecksContext(step) && !ContextExists(_active, _track))
            {
                if (!frozen) _contextLostFor += dt;
                if (_contextLostFor >= ContextLostSeconds) { ShelveLesson(); return; }
            }
            else _contextLostFor = 0f;

            // An oops line shows for a few seconds, then the step's own words come back.
            if (_oopsField != null)
            {
                if (frozen) _oopsUntil += dt;
                else if (now >= _oopsUntil) { _oopsField = null; PresentStrip(step); }
            }

            // Per-frame measures a done-condition needs.
            if (step.Done == TDone.GridFreed)
            {
                bool inter = false;
                try { inter = TheGridPanel.IsInteractive; } catch { }
                // Held while the editor is open: its own cursor unlock reads as "mouse freed" here.
                _gridFreeFor = !inter ? 0f : (frozen ? _gridFreeFor : _gridFreeFor + dt);
            }
            if (step.Done == TDone.FacingNorth)
            {
                var s = TutorialSafety.Snapshot();
                bool north = s != null && Mathf.Abs(Mathf.DeltaAngle(s.HeadingDeg, 0f)) <= 7.5f;
                _northFor = north ? _northFor + dt : 0f;
            }

            bool covered = UiaControlCenter.IsOpen;
            bool wheelOk = WheelNeedMet(step);

            // Tour mode: a setup completes on its STATE even while F10 covers the strip - menu.setup's
            // MenuOpen IS that state, so it is checked before the "covered" gate (see CompleteSetup).
            if (step.IsSetup)
            {
                if (!_onHold && IsDone(step, now)) { CompleteSetup(step, now, covered); return; }
            }
            else if (!_onHold && !covered && IsDone(step, now)) { Complete(step, now, false); return; }

            // Two clocks. _stepActive - the S2 read and "or N s" timeouts - runs only while the step's
            // wheel is up (you read what is on it). _stepLive is S1's, which the plan makes
            // unconditional: it runs whenever the player is in control, wheel open or not, so a wheel
            // step the player walked away from still skips itself. A wheel-bound step stops at S1's
            // limit whatever its own pattern: a closed wheel never holds a lesson forever.
            bool live = !_onHold && !covered && !frozen;
            bool running = live && wheelOk;
            if (running) { _stepActive += dt; _stepWait = 0f; }
            else if (!covered && !frozen) _stepWait += dt;
            if (live) _stepLive += dt;

            // 1.7's branch: 20 s without meeting an arrowed wedge -> point at a wheel that has one.
            if (step.Id == "core.pushout" && _branchField == null && !_arrowSeenThisStep && _stepActive >= BranchNoArrowSeconds)
            {
                _branchField = "branch@noarrow";
                PresentStrip(step);
            }

            TSkip skip = EffectiveSkip(step);
            bool selfSkips = skip == TSkip.S1 || step.NeedsWheel != TWheelKind.Other;
            if (running)
            {
                if (step.Timeout > 0f && _stepActive >= step.Timeout) { Complete(step, now, true); return; }
                if (skip == TSkip.S2 && _stepActive >= _stepRead) { Complete(step, now, true); return; }
            }
            if (live)
            {
                if (selfSkips && _stepLive >= S1SkipSeconds) { SelfSkip(step, now); return; }
                if (skip == TSkip.S1 && _stepLive >= _pulseNext)
                {
                    _pulseNext = _stepLive + PulseEverySeconds;
                    try { TutorialStrip.PulseKeys(); } catch (Exception e) { ReportTickError("strip", e); }
                }
            }

            // Chrome line (on-hold > self-skip warning > reopen > waiting chip). While the editor holds
            // the clocks the step shows its own words: "skips itself in 15 s" / "Lesson waiting" would
            // be untrue, and the wheel it may need was closed by the editor taking the keyboard.
            Chrome want = Chrome.None;
            if (_onHold) want = Chrome.OnHold;
            else if (frozen) want = Chrome.None;
            else if (selfSkips && _stepLive >= S1WarnSeconds) want = Chrome.SelfSkip;
            else if (!wheelOk) want = _stepWait >= WaitingChipSeconds ? Chrome.Waiting : Chrome.Reopen;
            else if (_stepWait >= WaitingChipSeconds) want = Chrome.Waiting;
            SetChromeLine(want, step);

            if (skip == TSkip.S2 && !covered) SetDrain(Mathf.Clamp01(1f - _stepActive / Mathf.Max(0.1f, _stepRead)));
            else SetDrain(-1f);
        }

        /// <summary>1.8 is done; before the Finish card the window must be closed and the aim back
        /// (max 30 s, then the overrun line; the card opens anyway after another 30 s).</summary>
        private static void TickWindowWait(float now, float dt, TStep step)
        {
            bool open = false, freeMouse = false, covered = false;
            try { open = TheGridPanel.IsOpen; } catch { }
            try { freeMouse = Cursor.visible; } catch { }
            try { covered = UiaControlCenter.IsOpen; } catch { }
            // F10 frees the cursor and covers the strip: the wait (and its 60 s give-up) holds until it
            // closes, so the Finish card never opens on its own over the menu. The editor holds it too.
            if (covered) return;
            if (!EditorFreeze()) _windowWaitFor += dt;
            if ((!open && !freeMouse) || _windowWaitFor >= WindowOverrunSeconds * 2f)
            {
                _windowWaitClose = false;
                SetChromeLine(Chrome.None, step);
                AdvanceStep(now);
                return;
            }
            if (!_stripShowing) PresentStrip(step);
            SetChromeLine(_windowWaitFor >= WindowOverrunSeconds ? Chrome.Overrun : Chrome.None, step);
        }

        /// <summary>Is the half this First Steps move needs switched on? Wheel moves need the RADIAL half,
        /// the big-window move the Universal Inventory; hands and G work with either.</summary>
        private static bool HalfAvailableFor(TStep step)
        {
            try
            {
                switch (step.Id)
                {
                    case "core.beltopen":
                    case "core.read":
                    case "core.take":
                    case "core.back":
                    case "core.pushout":
                        return UIAConfig.RadialEnabled == null || UIAConfig.RadialEnabled.Value;
                    case "core.window":
                        return UIAConfig.GridEnabled == null || UIAConfig.GridEnabled.Value;
                    default:
                        return true;
                }
            }
            catch { return true; }
        }

        private static bool StepChecksContext(TStep step)
        {
            // Tour mode: a SETUP step is how the player gets INTO the context - it cannot lose it (else
            // lessons 3 / 14 / 15 would shelve 3 s into their own setup).
            if (step.IsSetup) return false;
            // Lesson 3 asks the player to put the tablet in a bag at 3.4T - only its DO-steps need the
            // tool in hand. Tier lessons (14, 15) need their tier on every step.
            if (step.Lesson != null && step.Lesson.Trigger == TTrigger.Tools) return step.IsDoStep;
            return true;
        }

        private static TSkip EffectiveSkip(TStep step)
        {
            // 1.1's "active hand already empty" variant is a timed read; the other two are do-steps.
            if (step.Id == "core.hands") return _sayField == "says@empty" ? TSkip.S2 : TSkip.S1;
            return step.Skip;
        }

        private static bool WheelNeedMet(TStep step)
        {
            if (step.NeedsWheel == TWheelKind.Other) return true;
            if (!RadialController.AnyRadialOpen) return false;
            // The belt steps need the belt ring itself (MMB, or 6 - both classify as Belt); THE HUB
            // dive and back still sits inside it (ActiveWheelKind never reads Hub), and so does SEARCH
            // opened from THE HUB (it reads Search while the panel owns the ring - the wheel has not
            // closed, so the "wheel closed" line would be wrong), and the Q belt chooser (it replaces
            // the belt ring's root in the same wheel; RMB goes back to the belt). Tool steps: any open
            // wheel (the child wheels and the REPLACE list live inside the tool wheel).
            if (step.NeedsWheel == TWheelKind.Belt)
            {
                var k = RadialController.ActiveWheelKind;
                return k == TWheelKind.Belt || k == TWheelKind.Search || k == TWheelKind.BeltPicker;
            }
            return true;
        }

        /// <summary>The key a move tells the player to press to open what it needs: the step's own
        /// <c>Opener</c> (its wheel), else - for the First Steps / lesson 3 moves that say "Tap [MMB]",
        /// "tap [B]", "Tap [R]" without naming an opener in the data - that key. Null = none.</summary>
        private static string OpenerFor(TStep step)
        {
            if (step == null) return null;
            if (!string.IsNullOrEmpty(step.Opener)) return step.Opener;
            switch (step.Id)
            {
                case "core.beltopen":
                case "core.back":
                case "core.pushout":
                    return "{UIA_ToolbeltRadial}";
                case "core.window":
                    return "{UIA_Grid}";
                case "tools.open":
                case "tools.tablet.open":
                    return "{UIA_ToolRadial}";
            }
            return null;
        }

        /// <summary>Is that key bound? Resolved exactly like the strip's own [Key] glyph
        /// (<see cref="TutorialTokens.Glyph"/>): a V: token from the game's registry, a UIA_ one from
        /// <see cref="UiaKeybinds"/>. An unknown token or a failed lookup counts as bound - never skip
        /// a move over a lookup problem. Called on step entry only.</summary>
        private static bool OpenerBound(TStep step)
        {
            string token = OpenerFor(step);
            if (string.IsNullOrEmpty(token)) return true;
            try
            {
                if (token.Length >= 2 && token[0] == '{' && token[token.Length - 1] == '}')
                    token = token.Substring(1, token.Length - 2);
                if (token.StartsWith("V:", StringComparison.Ordinal))
                    return KeyManager.GetKey(token.Substring(2)) != KeyCode.None;
                UiaKeybinds.EnsureBuilt();
                if (UiaKeybinds.Find(token) == null) return true;
                return UiaKeybinds.Key(token) != KeyCode.None;
            }
            catch { return true; }
        }

        private static bool IsDone(TStep step, float now)
        {
            switch (step.Done)
            {
                case TDone.Hands:
                    if (_sayField == "says@empty") return false;          // timed read
                    if (_sayField == "says@bothfull") return Has(EvHandSwapped);
                    return ActiveHandEmpty();
                case TDone.BeltTap: return Has(EvBeltSticky);
                case TDone.WedgesRead:
                {
                    int d = _hoverDwells;
                    if (_lastHoverAt >= 0f && now - _lastHoverAt >= 0.4f && RadialController.AnyRadialOpen) d++;
                    return d >= 2;
                }
                case TDone.TakeTool:
                    if (_branchField != null) return Has(EvHubDive | EvBranchDive);
                    return TookTool();
                case TDone.Stowed: return Has(EvStowOk);
                // Opened THE HUB then backed out or closed - or simply backed out (there is no backing
                // out without having gone in, whatever the dive itself raised).
                case TDone.HubBack: return _flagB || Has(EvBackedOut);
                case TDone.ChildWheel: return Has(EvChildWheel);
                case TDone.GridFreed: return _gridFreeFor >= 1f;
                case TDone.GreyHovered: return Has(EvGreyHovered);
                case TDone.StowCommit: return Has(EvStowCommit);
                case TDone.ToolWheel: return Has(EvToolWheel);
                case TDone.ReplaceOpened: return Has(EvBranchDive | EvOpenCommit | EvSwapCommit | EvStowCommit);
                case TDone.SwapCommit: return Has(EvSwapCommit);
                case TDone.InstallCommit: return Has(EvStowCommit | EvSwapCommit);
                // "Take 1" / "Take half" classify as Take, "Split off this many" (a scroll wedge) as
                // Value: any commit counts.
                case TDone.SplitCommit: return Has(EvAnyCommit | EvGridSplit);
                case TDone.GridSplit: return Has(EvGridSplit);
                case TDone.ChipParked: return Has(EvChipParked);
                case TDone.ChipDropped: return Has(EvChipDropped);
                case TDone.WorldCue: return Has(EvWorldCue);
                case TDone.WorldGrab: return Has(EvWorldGrab);
                case TDone.GearHeld: return Has(EvGearHeld);
                case TDone.DigitJump: return Has(EvDigitJump);
                case TDone.InGrid: return Has(EvInGrid);
                case TDone.BeltPicker: return Has(EvBeltPicker);
                case TDone.BeltSwapOrBack: return Has(EvBeltSwapped | EvBeltBack);
                case TDone.BagLevel: return Has(EvBranchDive | EvOpenCommit);
                case TDone.SearchTook: return Has(EvSearchTook);
                case TDone.BagBound: return Has(EvBagBound);
                case TDone.ValueScrolled: return Has(EvValueScrolled);
                case TDone.HotkeyBound: return Has(EvHotkeyBound);
                case TDone.KeepOpen: return Has(EvKeepOpen);
                case TDone.HoldCommit: return Has(EvHoldCommit);
                case TDone.HubMoved: return Has(EvHubMoved);
                case TDone.GridTab: return Has(EvGridTab);
                case TDone.GridMove: return Has(EvGridDrag | EvGridItemWheel | EvGridTake);
                case TDone.DeviceWindow: return Has(EvDeviceWindow);
                case TDone.GridShiftDrag: return Has(EvGridShiftDrag);
                case TDone.PinCreated: return Has(EvPin);
                case TDone.GridKeyboard: return Has(EvGridKeyboard);
                case TDone.FacingNorth: return _northFor >= 0.4f;
                case TDone.VitalsTooltip: return Has(EvVitals);
                // Tour mode: setup STATES, read at 4 Hz by PollSetup (never per frame) - true while the
                // lesson's situation exists; lesson 17's key is the one event.
                case TDone.WheelOpen:
                case TDone.HoldingPartsTool:
                case TDone.HoldingTablet:
                case TDone.StackHovered:
                case TDone.BeltWheelWithSpare:
                case TDone.BagWheelOpen:
                case TDone.SettingsWheelOpen:
                case TDone.SuitPowered:
                case TDone.NoSuitPower:
                case TDone.MenuOpen:
                    return _setupOk;
                case TDone.DesignerKey: return Has(EvDesignerKey);
                default: return false;   // Read / Button / Next / Invite: timers and buttons decide
            }
        }

        /// <summary>1.4's measure: a commit put a different item in the active hand than the one it held
        /// when this move's evidence window opened (the moment 1.3 was DONE - see EnterStep), so a tool
        /// taken during 1.3's Then line counts.</summary>
        private static bool TookTool()
        {
            long id = HandId();
            return id != 0L && id != _handStartId && Has(EvAnyCommit);
        }

        /// <summary>This move is over: the NEXT move's evidence window opens now. Signals raised from here
        /// on (the Then line included) go to _evPending and count for it; its baseline hand and what was
        /// held are taken at this moment, not when it is entered.</summary>
        private static void OpenNextWindow()
        {
            _windowOpen = true;
            _windowHand = HandId();
            _windowHeld = Held();
        }

        /// <summary>The step was DONE (<paramref name="byTimer"/> false: flash, Then, advance) or its
        /// read / timeout ran out (true: Then if it has one, else just move on - no success tick for
        /// something the player did not do).</summary>
        private static void Complete(TStep step, float now, bool byTimer)
        {
            if (!byTimer && step.IsDoStep && !step.IsSetup) _doDone++;
            OpenNextWindow();
            SetChromeLine(Chrome.None, step);
            SetDrain(-1f);
            string thenField = ChooseThenField(step);
            string thenText = thenField != null ? Text(step, thenField) : null;
            if (thenText == null && byTimer)
            {
                AfterConfirmFor(step, now);
                return;
            }
            _confirming = true;
            _confirmStart = now;
            _confirmMin = thenText != null ? Mathf.Clamp(1.0f + 0.05f * thenText.Length, 1.4f, 6f) : 0.6f;
            _suppress++;
            try { TutorialStrip.Confirm(thenText, _confirmMin, _onConfirmDone); }
            catch (Exception e) { ReportTickError("strip", e); _pendingConfirmDone = true; }
            finally { _suppress--; }
        }

        private static void AfterConfirm(float now)
        {
            var step = CurrentStep();
            AfterConfirmFor(step, now);
        }

        private static void AfterConfirmFor(TStep step, float now)
        {
            // 1.8 -> 1.9 waits for the window to close first.
            if (step != null && step.Id == "core.window")
            {
                bool open = false;
                try { open = TheGridPanel.IsOpen; } catch { }
                if (open) { _windowWaitClose = true; _windowWaitFor = 0f; return; }
            }
            AdvanceStep(now);
        }

        /// <summary>Tour mode: a setup step's state now holds (the player got into the situation). The
        /// usual success beat (and senses.setup's Then line) - except while F10 covers the strip
        /// (menu.setup: F10 open IS the state), where no confirm could play: it moves on at once.</summary>
        private static void CompleteSetup(TStep step, float now, bool covered)
        {
            if (!covered) { Complete(step, now, false); return; }
            OpenNextWindow();
            SetChromeLine(Chrome.None, step);
            SetDrain(-1f);
            AfterConfirmFor(step, now);
        }

        /// <summary>S1: shown, not learned. The core's twin tip may fire later. Tour mode: a SETUP that
        /// self-skips sets the lesson aside instead (see <see cref="SetupSkipped"/>).</summary>
        private static void SelfSkip(TStep step, float now)
        {
            if (step != null && step.IsSetup) { SetupSkipped(step, now); return; }
            NoteShownNotLearned(step);
            SetChromeLine(Chrome.None, step);
            AfterConfirmFor(step, now);
        }

        /// <summary>Tour mode: the lesson's situation never came about (its setup self-skipped): the
        /// lesson is set aside - Skipped, so after the first-run tour it comes back once, just-in-time,
        /// the first time the player needs it (entry.tourend promises that). In the tour, lesson 3 first
        /// plays its other track, then the tour moves on. Outside the tour: an automatic offer is shelved
        /// like any ignored one (Offered); one the player started ends Skipped.</summary>
        private static void SetupSkipped(TStep step, float now)
        {
            SetChromeLine(Chrome.None, step);
            SetTrackAside();   // (in the tour, lesson 3's track is persisted: see AsideKey)
            if (_activeTour)
            {
                if (TourNextTrack(now)) return;
                EndLesson(TLessonState.Skipped);
                return;
            }
            if (_activeAuto) ShelveLesson();
            else EndLesson(TLessonState.Skipped);
        }

        /// <summary>S1's bookkeeping: a First Steps move flags its "as I go" twin; 1.5 follows "1.4 done
        /// and the active hand holds something" - no tool taken, no 1.5.</summary>
        private static void NoteShownNotLearned(TStep step)
        {
            if (step == null) return;
            if (_active != null && _active.Id == "core") TutorialProgressStore.SetFlag("s1:" + step.Id, true);
            if (step.Id == "core.take") _skipStow = true;
        }

        private static float ReadSecondsFor(TStep step)
        {
            if (step.ReadSeconds > 0f) return step.ReadSeconds;
            return ReadSecondsFor(Text(step, _branchField ?? _sayField ?? "says"));
        }

        /// <summary>Plan S2: 2.5 s + 0.06 s per character, 5-12 s.</summary>
        private static float ReadSecondsFor(string text)
        {
            int n = text != null ? text.Length : 0;
            return Mathf.Clamp(2.5f + 0.06f * n, 5f, 12f);
        }

        private static void ShowOops(string field, float now)
        {
            var step = CurrentStep();
            if (step == null || !step.Has(field) || _confirming) return;
            _oopsField = field;
            _oopsUntil = now + OopsSeconds;
            if (_stripShowing) PresentStrip(step);
        }

        // ================================================================ strip helpers

        /// <summary>Anything that must hide the strip and freeze a lesson: no world UI, the console or a
        /// vanilla input window / menu, the F9 editor, the Handbook, or a card (ours or not).</summary>
        private static bool Suspended()
        {
            if (SuspendedExceptCoach()) return true;
            try { return TutorialCoach.IsOpen; }
            catch { return true; }
        }

        /// <summary><see cref="Suspended"/> minus "a card is up" - for a card that is to take the place
        /// of the one being pressed (tour mode's closing card after lesson 17's "Not now").</summary>
        private static bool SuspendedExceptCoach()
        {
            // The strip's tick is off (circuit breaker): nothing can be shown, so nothing may advance
            // or be marked shown - every lesson holds until a restart.
            if (_tickOff[SubStrip]) return true;
            try
            {
                if (!Guards.CanDraw()) return true;
                if (!Guards.CanToggleMenus()) return true;
                if (Guards.VanillaMenuWantsFront()) return true;
                if (HudEditorMode.Active || RadialEditorMode.Active) return true;
                if (HandbookViewer.IsOpen) return true;
                return false;
            }
            catch { return true; }
        }

        /// <summary>The lesson editor (uiadev, <c>uiatutorial edit</c> / F8) is open over a LIVE lesson,
        /// tip or invite - not a preview, which stops the flow by itself. While it is, the step's
        /// timers stand still (TickLesson / TickTimed / TickWindowWait) and resume on close. The strip
        /// stays up: the editor is a dev ImGui window and never suspends the lesson.</summary>
        private static bool EditorFreeze()
        {
            if (_preview != null) return false;
            try { return TutorialEditorWindow.IsOpen; }
            catch { return false; }
        }

        /// <summary>Show the current lesson step on the strip (text resolved now) with its spots.</summary>
        private static void PresentStrip(TStep step)
        {
            if (step == null) return;
            // The demo follows the SAY variant (an oops / branch line keeps the variant's demo).
            ShowStripRaw(step.Id, _header ?? "", StripText(step), DemoFor(step, _sayField));
            // Start from a clean chrome line: the tick re-applies whatever the step needs.
            _chromeShown = Chrome.None;
            try { TutorialStrip.SetChrome(null); } catch (Exception e) { ReportTickError("strip", e); }
            _drainShown = -2f;
            ApplySpots(step);
        }

        /// <summary>The words a lesson step shows right now: an oops line, else a branch line, else its
        /// Says variant.</summary>
        private static string StripText(TStep step)
        {
            string field = _oopsField ?? _branchField ?? _sayField ?? "says";
            if (!step.Has(field)) field = "says";
            return Text(step, field);
        }

        /// <summary>A strip preview's words (and header): Says, else the multiplayer Says.</summary>
        private static string PreviewStripText(TStep step, out string header)
        {
            string field = step.Has("says") ? "says" : (step.Has("says@mp") ? "says@mp" : null);
            header = step.Kind == TPresentation.Tip ? "" : LessonTitle(step.Lesson);
            return field != null ? Text(step, field) : "";
        }

        private static bool IsStripKind(TStep step)
        {
            return step != null && step.Kind != TPresentation.Card && step.Kind != TPresentation.Callout;
        }

        private static string StripShowingId()
        {
            try { return TutorialStrip.CurrentStepId; }
            catch { return null; }
        }

        private static bool StripShows(string stepId)
        {
            return stepId != null && string.Equals(StripShowingId(), stepId, StringComparison.Ordinal);
        }

        private static void ShowStripRaw(string stepId, string header, string body, string demo)
        {
            _suppress++;
            try { TutorialStrip.Show(stepId, header ?? "", body ?? "", demo); _stripShowing = true; }
            catch (Exception e) { ReportTickError("strip", e); }
            finally { _suppress--; }
        }

        /// <summary>Editor refresh: new words for the step on the strip, swapped in place - no
        /// cross-fade, no demo restart (if another step is showing it behaves like Show).</summary>
        private static void UpdateStripRaw(string stepId, string header, string body)
        {
            _suppress++;
            try { TutorialStrip.UpdateText(stepId, header ?? "", body ?? ""); _stripShowing = true; }
            catch (Exception e) { ReportTickError("strip", e); }
            finally { _suppress--; }
        }

        private static void HideStrip()
        {
            if (!_stripShowing) return;
            _stripShowing = false;
            _chromeShown = Chrome.None;
            _drainShown = -2f;
            _suppress++;
            try { TutorialStrip.Hide(true); }
            catch (Exception e) { ReportTickError("strip", e); }
            finally { _suppress--; }
            SpotClear();
        }

        private static void SetChromeLine(Chrome want, TStep step)
        {
            if (!_stripShowing) return;
            // (The on-hold line is rebuilt when its reason changes too - a reference compare per frame.)
            if (want == _chromeShown && (want != Chrome.OnHold || ReferenceEquals(_holdWhyShown, _holdWhy))) return;
            _chromeShown = want;
            string line = null;
            switch (want)
            {
                case Chrome.SelfSkip: line = ChromeText("selfskip"); break;
                case Chrome.OnHold: line = HoldLine(); _holdWhyShown = _holdWhy; break;
                case Chrome.Waiting: line = ChromeText("waiting"); break;
                case Chrome.Overrun: line = ChromeText("overrun"); break;
                case Chrome.Reopen:
                {
                    string raw = TutorialTextStore.Get("chrome|reopen");
                    string opener = step != null && !string.IsNullOrEmpty(step.Opener) ? step.Opener : "{UIA_ToolbeltRadial}";
                    line = TutorialTokens.Resolve(raw.Replace("{OPENER}", opener));
                    break;
                }
            }
            // C.19's "Lesson waiting" chip is the strip's compact form (header + demo hidden, slim at the
            // top edge); every other chrome line keeps the full strip.
            try { TutorialStrip.SetChrome(line, compact: want == Chrome.Waiting); }
            catch (Exception e) { ReportTickError("strip", e); }
        }

        /// <summary>The on-hold line with its reason (the player learns WHY the lesson waits). An
        /// emergency keeps chrome|onhold and names it: "Lesson on hold - deal with the emergency first
        /// (FREEZING)." The suit's own chronic status is no emergency - the part of chrome|onhold before
        /// " - " plus the reason: "Lesson on hold - suit: no air tank." (an edited line keeps its own
        /// opening). No reason known: chrome|onhold as it stands. Built when the reason changes only.</summary>
        private static string HoldLine()
        {
            string line = ChromeText("onhold");
            if (string.IsNullOrEmpty(_holdWhy)) return line;
            string why = TutorialTextStore.Sanitize(_holdWhy);   // a catalog word may be author-edited: ASCII only
            string head = (line ?? "").TrimEnd();
            if (head.EndsWith(".", StringComparison.Ordinal)) head = head.Substring(0, head.Length - 1);
            if (_holdAcute) return head + " (" + why + ").";
            int dash = head.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) head = head.Substring(0, dash);
            return head + " - " + why + ".";
        }

        private static void SetDrain(float f)
        {
            if (f < 0f)
            {
                if (_drainShown == -1f) return;
                _drainShown = -1f;
            }
            else
            {
                if (_drainShown >= 0f && Mathf.Abs(_drainShown - f) < 0.004f) return;
                _drainShown = f;
            }
            try { TutorialStrip.SetDrain(f < 0f ? -1f : f); }
            catch (Exception e) { ReportTickError("strip", e); }
        }

        private static void ApplySpots(TStep step)
        {
            SpotClear();
            if (step == null || step.Spots == null || step.Spots.Length == 0) return;
            if (_tickOff[SubSpotlight]) return;   // circuit breaker: the spotlight is off for the session
            _spotStep = step;
            bool any = false;
            try
            {
                int n = Mathf.Min(step.Spots.Length, 31);
                for (int i = 0; i < n; i++)
                {
                    if (TutorialSpotlight.Show(step.Spots[i].Type, step.Spots[i].Src)) any = true;
                    else _spotMissing |= 1 << i;
                }
                if (!any && step.SpotFallbackHands) _spotFallback = TutorialSpotlight.Show(HudElementType.HandBoxes, null);
            }
            catch (Exception e) { ReportTickError("spotlight", e); }
        }

        /// <summary>4 Hz (Poll), allocation-free: a spot target that was not on screen when its step was
        /// presented (the HUD faded, the element hidden at this tier, not built yet) is asked for again
        /// while that step is up. When a real target appears while the hand-box fallback stands in for
        /// all of them, the spots are re-applied so the stand-in ring goes; while none is on screen the
        /// fallback itself is retried.</summary>
        private static void RetrySpots()
        {
            var step = _spotStep;
            if (step == null || _spotMissing == 0 || step.Spots == null) return;
            if (_tickOff[SubSpotlight]) return;
            try
            {
                int n = Mathf.Min(step.Spots.Length, 31);
                int all = n >= 31 ? int.MaxValue : (1 << n) - 1;
                bool gained = false;
                for (int i = 0; i < n; i++)
                {
                    int bit = 1 << i;
                    if ((_spotMissing & bit) == 0) continue;
                    if (TutorialSpotlight.Show(step.Spots[i].Type, step.Spots[i].Src)) { _spotMissing &= ~bit; gained = true; }
                }
                if (gained && _spotFallback) ApplySpots(step);   // Clear + re-show: the stand-in ring goes
                else if (!gained && !_spotFallback && (_spotMissing & all) == all && step.SpotFallbackHands)
                    _spotFallback = TutorialSpotlight.Show(HudElementType.HandBoxes, null);
            }
            catch (Exception e) { ReportTickError("spotlight", e); }
        }

        private static void SpotClear()
        {
            _spotStep = null;
            _spotMissing = 0;
            _spotFallback = false;
            if (_tickOff[SubSpotlight]) return;   // switched off (and cleared) by the circuit breaker
            try { TutorialSpotlight.Clear(); }
            catch (Exception e) { ReportTickError("spotlight", e); }
        }

        private static string Text(TStep step, string field)
        {
            if (step == null) return "";
            return TutorialTokens.Resolve(TutorialTextStore.Get(step.Id + "|" + field));
        }

        private static string ChromeText(string name)
        {
            return TutorialTokens.Resolve(TutorialTextStore.Get("chrome|" + name));
        }

        // ================================================================ cards

        private static TCardSpec NewSpec(string stepId, string heading, string body, string demo)
        {
            return new TCardSpec
            {
                StepId = stepId,
                Heading = heading ?? "",
                Body = body ?? "",
                DemoId = demo,
                Buttons = new string[0],
                PrimaryIndex = 0,
                Pause = PauseCards(),
                HasHole = false,          // spots ride the strip, never a card (no scrim hole needed)
                RunningLine = null,
                OnButton = _onButton,
                OnLater = _onLater,
            };
        }

        /// <summary>Name the text keys this card was READ from, so the coach's in-place editor (uiadev)
        /// edits the words actually on screen - the Welcome's body@mp / body@running, a Watch card's
        /// Says variant, a callout's title / callout. A field that is the coach's default ("heading" /
        /// "body") stays null; "" marks a composed line (Says + Then) the editor must leave read-only.
        /// Card builds only (allocation is fine).</summary>
        private static void SetEditKeys(TCardSpec spec, TStep step, string headingField, string bodyField)
        {
            if (spec == null || step == null) return;
            spec.HeadingKey = headingField == null || headingField == "heading" ? null
                : headingField.Length == 0 ? "" : step.Id + "|" + headingField;
            spec.BodyKey = bodyField == null || bodyField == "body" ? null
                : bodyField.Length == 0 ? "" : step.Id + "|" + bodyField;
        }

        private static void SetButtons(TCardSpec spec, Act[] acts, int primary)
        {
            _acts = acts;
            var labels = new string[acts.Length];
            for (int i = 0; i < acts.Length; i++) labels[i] = ActLabel(acts[i]);
            spec.Buttons = labels;
            spec.PrimaryIndex = Mathf.Clamp(primary, 0, Mathf.Max(0, acts.Length - 1));
        }

        private static string ActLabel(Act a)
        {
            var step = _cardStep;
            switch (a)
            {
                case Act.B0: return step != null ? Text(step, "button@0") : "OK";
                case Act.B1: return step != null ? Text(step, "button@1") : "";
                case Act.B2: return step != null ? Text(step, "button@2") : "";
                case Act.Next: return ChromeText("next");
                case Act.Back: return ChromeText("back");
                case Act.Done: return ChromeText("done");
                case Act.SkipLesson: return ChromeText("skiplesson");
                case Act.SkipTour: return ChromeText("skiptour");
                case Act.TryNow: return ChromeText("trynow");
                case Act.TourTake: return ChromeText("tourtake");
                case Act.TourNo: return ChromeText("tourno");
                case Act.TourNext: return ChromeText("tournext");
                case Act.TourStop: return ChromeText("tourstop");
                default: return "OK";
            }
        }

        /// <summary>Open a CARD step with its own buttons (button@0..2). <paramref name="bodyField"/>
        /// overrides the body variant (the Welcome's multiplayer / no-pause bodies). <paramref name="note"/>:
        /// a live line under the body (the Hold-on card's reason) - the body is then composed, read-only in
        /// the coach's in-place editor (the lesson editor still edits the step's own body).</summary>
        private static void OpenStepCard(CardCtx ctx, TStep step, string bodyField, string note = null)
        {
            if (step == null) return;
            _cardStep = step;
            string field = bodyField != null && step.Has(bodyField) ? bodyField : "body";
            string body = Text(step, field);
            if (!string.IsNullOrEmpty(note)) body = body + "\n\n" + note;
            var spec = NewSpec(step.Id, Text(step, "heading"), body, step.DemoId);
            SetEditKeys(spec, step, "heading", string.IsNullOrEmpty(note) ? field : "");
            int n = step.Has("button@2") ? 3 : step.Has("button@1") ? 2 : 1;
            var acts = n == 3 ? new[] { Act.B0, Act.B1, Act.B2 } : n == 2 ? new[] { Act.B0, Act.B1 } : new[] { Act.B0 };
            SetButtons(spec, acts, 0);
            _cardBodyField = field;
            OpenCard(ctx, spec);
        }

        private static string _cardBodyField;

        private static void OpenCard(CardCtx ctx, TCardSpec spec)
        {
            HideStrip();
            _suppress++;
            try { TutorialCoach.OpenCard(spec); }
            catch (Exception e) { ReportTickError("card", e); }
            finally { _suppress--; }
            _cardOpenFrame = Time.frameCount;
            _card = CoachCardOpen() ? ctx : CardCtx.None;
        }

        private static bool CoachCardOpen()
        {
            try { return TutorialCoach.CardOpen; }
            catch { return false; }
        }

        private static void CoachClose()
        {
            _suppress++;
            try { TutorialCoach.CloseCard(); }
            catch (Exception e) { ReportTickError("card", e); }
            finally { _suppress--; }
        }

        /// <summary>Re-render the open card from the text store (the editor typed into it).</summary>
        private static void ReopenCard()
        {
            switch (_card)
            {
                case CardCtx.Watch: ShowWatchCard(); return;
                case CardCtx.Tour:
                case CardCtx.TourOpener: ShowTourCard(); return;
                case CardCtx.Preview: ShowPreview(); return;
                case CardCtx.WhatsNewPage: ShowWhatsNewPage(); return;
                case CardCtx.LessonCard: if (_cardStep != null) OpenLessonCard(_cardStep); return;   // keeps the tour's buttons
                case CardCtx.Hold: OpenHold(_holdTour); return;   // keeps its reason line (and whose card it is)
            }
            if (_cardStep == null) return;
            var ctx = _card;
            OpenStepCard(ctx, _cardStep, _cardBodyField);
        }

        /// <summary>A card button - handled SYNCHRONOUSLY, inside the coach's press: the coach keeps
        /// the pause and the modal when we answer with OpenCard (the next card swaps in place) and
        /// auto-closes only if we answer with neither (TutorialSpecs.TCardSpec remarks). Deferring to
        /// the next tick would drop the pause for a frame between two cards.</summary>
        private static void OnCardButton(int i)
        {
            if (_suppress > 0) return;
            _inPress = true;
            try
            {
                if (i >= 0 && i < _acts.Length) HandleAct(_acts[i], Time.unscaledTime);
            }
            catch (Exception e) { ReportTickError("card", e); }
            finally { _inPress = false; }
        }

        /// <summary>Let go of our card WITHOUT closing it (tour mode's card-to-card steps): inside a
        /// button press, a card that opens next swaps in place - the pause is never dropped - and if
        /// nothing opens, the coach closes the pressed card itself once the press returns. (From a
        /// "Later" the card is already closed.)</summary>
        private static void ReleaseCardCtx()
        {
            _card = CardCtx.None;
            _cardStep = null;
        }

        private static void OnCardLater()
        {
            if (_suppress > 0) return;
            _pendingLater = true;
        }

        /// <summary>The strip finished its success beat. Recorded even when it fires synchronously from
        /// inside our own Confirm/Hide call - the tick only acts on it while a confirm is in flight.</summary>
        private static void OnStripConfirmed()
        {
            _pendingConfirmDone = true;
        }

        /// <summary>Deferred callbacks: the strip's confirm beat, a pending play request and the coach's
        /// "Later" (fired by the coach AFTER it closed the card - X, Esc, the world going away, F10 closing
        /// under a callout).</summary>
        private static void ProcessCallbacks(float now)
        {
            // A confirm that finished just as a preview started waits for it to end: advancing now
            // would present the next step over the preview.
            if (_pendingConfirmDone && _preview == null)
            {
                _pendingConfirmDone = false;
                if (_confirming && _flow == Flow.Lesson) { _confirming = false; AfterConfirm(now); }
            }
            // An explicit play (GUIDE's Try it / Watch, uiatutorial play) BEFORE a card's Later: GUIDE
            // asks, then closes F10 - and the lesson-16 callout that goes with F10 reports Later. Taken
            // first, the play's StopEverything drops that Later: the first-run tour's lesson 16 stays
            // where it stands (Active) and resumes after the played lesson (TickTour), instead of being
            // set aside by EndTour.
            if (_pendingPlay != null)
            {
                var l = _pendingPlay;
                _pendingPlay = null;
                StartPlay(l, _pendingPlayWatch, now);
            }
            if (_pendingLater)
            {
                _pendingLater = false;
                HandleLater(now);
            }
        }

        /// <summary>Our card vanished without telling us (closed by a path that skipped OnLater) - treat
        /// it as Later for that card, so the Director never waits on a card that is gone.</summary>
        private static void DetectCardClosedElsewhere()
        {
            if (_card == CardCtx.None || Time.frameCount == _cardOpenFrame) return;
            if (_pendingLater) return;
            if (CoachCardOpen()) return;
            HandleLater(Time.unscaledTime);
            if (_card != CardCtx.None && !CoachCardOpen()) _card = CardCtx.None;
        }

        private static void HandleLater(float now)
        {
            switch (_card)
            {
                case CardCtx.Welcome: WelcomeChoice(-1, now); break;
                case CardCtx.Resume: ResumeChoice(1, now); break;
                case CardCtx.Hold: HoldChoice(0, now); break;
                // (designer.card as lesson 17 of the first-run tour: Later = "Not now", as on the F9 card)
                case CardCtx.LessonCard:
                    LessonCardChoice(_cardStep != null && _cardStep.Id == "designer.card" ? 2 : 0, now);
                    break;
                case CardCtx.Designer: DesignerChoice(2, now); break;
                case CardCtx.WhatsNew:
                case CardCtx.WhatsNewPage: CloseCardCtx(); TutorialProgressStore.SetState("whatsnew", TLessonState.Done); TutorialProgressStore.Flush(); break;
                case CardCtx.Watch: EndWatch(); break;
                case CardCtx.TourOpener:
                    // Esc on the opener (F10 still up) = "No thanks". F10 closed under it = "not now":
                    // asked again on a later open, three times in all (no nagging). The F10 KEY is closing
                    // too: the coach reports it before the plugin's toggle shuts the menu this frame.
                    EndTour((!UiaControlCenter.IsOpen || MenuKeyDownNow()) && TutorialProgressStore.Bump("tourOpenerClosed") < 3
                        ? TLessonState.New : TLessonState.Skipped);
                    break;
                // A callout closed (its X, Esc, F10 closed - its key, a tab that closes it): the callouts
                // were SEEN when it stood at the last one or the one before it - Done; else set aside.
                // (The "Skip lesson" / "Skip tour" button stays an explicit Skipped - TourAct.)
                case CardCtx.Tour: EndTour(CalloutsSeen() ? TLessonState.Done : TLessonState.Skipped); break;
                case CardCtx.Preview: StopPreview(); break;
                case CardCtx.TourEnd: TourEndChoice(0); break;   // X / Esc = "Start playing"
                default: CloseCardCtx(); break;
            }
        }

        private static void CloseCardCtx()
        {
            if (_card == CardCtx.None) return;
            _card = CardCtx.None;
            _cardStep = null;
            CoachClose();
        }

        private static void HandleAct(Act a, float now)
        {
            switch (_card)
            {
                case CardCtx.Welcome: WelcomeChoice(ButtonIndex(a), now); return;
                case CardCtx.Resume: ResumeChoice(ButtonIndex(a), now); return;
                case CardCtx.Hold: HoldChoice(ButtonIndex(a), now); return;
                case CardCtx.LessonCard:
                    // The tour's hand-over card (core.finish in the first-run tour).
                    if (a == Act.TourNext || a == Act.TourStop) { TourFinishChoice(a == Act.TourStop, now); return; }
                    LessonCardChoice(ButtonIndex(a), now);
                    return;
                case CardCtx.TourEnd: TourEndChoice(ButtonIndex(a)); return;
                case CardCtx.Designer: DesignerChoice(ButtonIndex(a), now); return;
                case CardCtx.WhatsNew: WhatsNewChoice(ButtonIndex(a), now); return;
                case CardCtx.WhatsNewPage: WhatsNewPageAct(a); return;
                case CardCtx.Watch: WatchAct(a, now); return;
                case CardCtx.TourOpener:
                case CardCtx.Tour: TourAct(a); return;
                case CardCtx.Preview: StopPreview(); return;
            }
        }

        private static int ButtonIndex(Act a)
        {
            switch (a)
            {
                case Act.B0: return 0;
                case Act.B1: return 1;
                case Act.B2: return 2;
                default: return -1;
            }
        }

        // ---- Welcome (0.1): Start / Teach me as I go / No lessons / (Esc) Later

        private static void WelcomeChoice(int b, float now)
        {
            // Whatever the answer, this entry's card was the Welcome: an updater's What's new (still New
            // after "Teach me as I go") waits for a later entry instead of following it straight away.
            _whatsNewHandledThisEntry = true;
            if (b == 0)
            {
                // Start the tour (tour mode): the overview cards replace this one in place - the pause is
                // kept - then every lesson in order (TourBegin). Choosing lessons turns them on - the
                // Welcome also shows after "uiatutorial restart" with lessons switched off.
                TutorialProgressStore.SetState("entry", TLessonState.Done);
                SetTips(true);
                TourBegin(now);
                TutorialProgressStore.Flush();
                return;
            }
            CloseCardCtx();
            switch (b)
            {
                case 1:
                    TutorialProgressStore.SetState("entry", TLessonState.Done);
                    TutorialProgressStore.SetFlag("asigo", true);
                    TourForget(now);   // "as I go" keeps the full just-in-time behaviour: no tour, no once-rule
                    SetTips(true);     // "as I go" IS lessons popping up by themselves
                    break;
                case 2:
                    TutorialProgressStore.SetState("entry", TLessonState.Done);
                    TourForget(now);
                    SetTips(false);
                    StartTimed(Flow.Notice, null, now);
                    break;
                default:
                {
                    // Later: ask again at the next world entry, twice at most - then "as I go".
                    int later = TutorialProgressStore.Bump("welcomeLater");
                    _laterAtEntry = _entrySerial;
                    if (later > 2)
                    {
                        TutorialProgressStore.SetState("entry", TLessonState.Done);
                        TutorialProgressStore.SetFlag("asigo", true);
                    }
                    else TutorialProgressStore.SetState("entry", TLessonState.Later);
                    break;
                }
            }
            TutorialProgressStore.Flush();
        }

        /// <summary>Start / Continue First Steps: calm -> close the card and run the strips; not calm ->
        /// the Hold-on card REPLACES the current card (one card to the next, the pause never drops).</summary>
        private static void StartCoreOrHold(float now)
        {
            var core = TutorialChapters.FindLesson("core");
            if (core == null) { CloseCardCtx(); return; }
            // Only an interrupted First Steps (stored Active) continues where it stopped; anything else
            // - the Welcome replayed from GUIDE after it was finished or skipped - starts at move 1.
            // (Hold's "Start anyway" reads the same stored step.)
            if (StateOf("core") != TLessonState.Active) TutorialProgressStore.SetStep("core", 0);
            TutorialProgressStore.SetState("core", TLessonState.Active);
            if (!TutorialSafety.IsCalm()) { OpenHold(); return; }
            CloseCardCtx();
            StartLesson(core, 0, TutorialProgressStore.GetStep("core"), 0UL, false, now);
        }

        // ---- Welcome back (0.3): Continue / Later / Stop lessons

        private static void ResumeChoice(int b, float now)
        {
            // Tour mode: a paused first-run tour is what the card offers (the core may be its lesson).
            bool tour = TutorialProgressStore.TourState == TTourState.Running;
            if (b == 0)
            {
                if (tour)
                {
                    // Continue: the tour's lesson (and step) replaces this card in place - or, not calm,
                    // the Hold-on card does.
                    _remindPending = false;
                    TourStartCurrent(now, true, 0UL);
                    TutorialProgressStore.Flush();
                    return;
                }
                StartCoreOrHold(now);
                return;
            }
            CloseCardCtx();
            switch (b)
            {
                case 2:
                    if (tour) TourEnd(false, now);   // Stop lessons ends the tour (like GUIDE's Stop all)
                    SetTips(false);
                    StartTimed(Flow.Notice, null, now);
                    break;
                default:
                {
                    // Later (X / Esc too): offered again at the next world entry - twice at most, a
                    // count kept in Progress.xml (like the Welcome's Later). The third Later turns the
                    // rest of First Steps into "as I go": Skipped, the moves not done get their tips -
                    // and a paused tour ends there, its lesson set aside (the rest come just-in-time).
                    if (TutorialProgressStore.Bump("resumeLater") > ResumeLaterMax)
                    {
                        if (tour) TourEnd(true, now);
                        else SkipStoredCore(now);
                    }
                    break;
                }
            }
            TutorialProgressStore.Flush();
        }

        // ---- Hold on (0.4): Remind me in 5 minutes / Start anyway

        private static void HoldChoice(int b, float now)
        {
            // "Start anyway" is a real override: a chronic suit problem (no air tank, a used-up filter, a
            // battery almost flat) no longer holds any lesson this session - before, the lesson started
            // only to sit "on hold" under it. An emergency still holds its strip, and says which.
            if (b == 1)
            {
                TutorialSafety.AcceptChronic();
                _calmNow = TutorialSafety.IsCalm();
            }
            if (_holdTour)
            {
                // Tour mode: the card held the first-run tour's next lesson back.
                _holdTour = false;
                if (b == 1)
                {
                    // Start anyway: the lesson replaces the card (a strip - the coach closes the card).
                    ReleaseCardCtx();
                    TourStartCurrent(now, false, 0UL);
                    return;
                }
                // Remind: the tour pauses; the Welcome-back card offers it after 5 minutes of calm play.
                CloseCardCtx();
                TourPark();
                _remindPending = true;
                _remindCalm = 0f;
                TutorialProgressStore.Flush();
                return;
            }
            CloseCardCtx();
            if (b == 1)
            {
                var core = TutorialChapters.FindLesson("core");
                if (core != null) StartLesson(core, 0, TutorialProgressStore.GetStep("core"), 0UL, false, now);
                return;
            }
            // Remind: re-offer the Welcome-back card after 5 minutes of calm play.
            TutorialProgressStore.SetState("core", TLessonState.Active);
            _remindPending = true;
            _remindCalm = 0f;
        }

        // ---- That's the core (1.9): Start playing / See all lessons

        private static void LessonCardChoice(int b, float now)
        {
            var step = _cardStep;
            if (_flow != Flow.Lesson || _active == null) { CloseCardCtx(); return; }
            if (step != null && step.Id == "core.finish")
            {
                // In the first-run tour its buttons are TourNext / TourStop (HandleAct); this is its
                // Later (X / Esc) - read as the primary, "Next lesson" (outside the tour: "Start playing").
                if (_activeTour) { TourFinishChoice(false, now); return; }
                CloseCardCtx();
                EndLesson(TLessonState.Done);
                if (b == 1) OpenGuideTab();
                return;
            }
            // Release, don't close: the overview's next card (or the Hold-on card, or the tour's closing
            // card) then swaps in place and the pause is kept between cards; a strip that follows lets
            // the coach close this one itself.
            ReleaseCardCtx();
            if (step != null && step.Id == "designer.card") { DesignerLessonChoice(b, now); return; }
            AdvanceStep(now);
        }

        /// <summary>Tour mode: core.finish in the first-run tour - "Next lesson" moves on to lesson 2;
        /// "Stop the tour" ends the tour here (First Steps is done either way): the lessons not played
        /// yet come just-in-time, the first time the player needs each (once).</summary>
        private static void TourFinishChoice(bool stop, float now)
        {
            ReleaseCardCtx();
            if (_flow != Flow.Lesson || _active == null) return;
            if (stop) TourEnd(false, now);
            EndLesson(TLessonState.Done);   // the tour, still Running, moves on (TourAfterLesson)
        }

        /// <summary>Tour mode: designer.card played as lesson 17 of the first-run tour: the lesson is
        /// Done whatever the answer (like the F9 card's DesignerChoice); Open the Designer / Read the
        /// Handbook act as there. The tour's closing card follows - in place for "Not now", else once
        /// the Designer / Handbook is closed again.</summary>
        private static void DesignerLessonChoice(int b, float now)
        {
            if (b == 0) _pendingOpenDesigner = true;
            else if (b == 1)
            {
                try { HandbookViewer.Open(); } catch (Exception e) { ReportTickError("handbook", e); }
            }
            EndLesson(TLessonState.Done);
        }

        private static void OpenGuideTab()
        {
            _f10OpenedByUsFrame = Time.frameCount;
            try { UiaControlCenter.OpenOnTab(TutorialAnchors.GuideTab); }
            catch (Exception e) { ReportTickError("guide", e); }
        }

        // ---- The HUD Designer (17.1): Open the Designer / Read the Handbook / Not now

        private static void DesignerChoice(int b, float now)
        {
            CloseCardCtx();
            TutorialProgressStore.SetState("designer", TLessonState.Done);
            TutorialProgressStore.Flush();
            if (b == 0) _pendingOpenDesigner = true;
            else if (b == 1)
            {
                try { HandbookViewer.Open(); } catch (Exception e) { ReportTickError("handbook", e); }
            }
        }

        private static void OpenDesignerNow()
        {
            try
            {
                if (HudEditorMode.Active) return;
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) inst.ToggleHudEditor();
            }
            catch (Exception e) { ReportTickError("designer", e); }
        }

        // ---- What's new (18.1): Show me / Got it; pages 18.2-18.5

        private static void WhatsNewChoice(int b, float now)
        {
            if (b == 0)
            {
                var wn = TutorialChapters.FindLesson("whatsnew");
                if (wn != null && wn.Steps.Length > 1)
                {
                    _pageIdx = 1;
                    ShowWhatsNewPage();
                    return;
                }
            }
            CloseCardCtx();
            TutorialProgressStore.SetState("whatsnew", TLessonState.Done);
            TutorialProgressStore.Flush();
        }

        private static void ShowWhatsNewPage()
        {
            var wn = TutorialChapters.FindLesson("whatsnew");
            if (wn == null || _pageIdx < 1 || _pageIdx >= wn.Steps.Length) { WhatsNewChoice(1, 0f); return; }
            var step = wn.Steps[_pageIdx];
            _cardStep = step;
            var spec = NewSpec(step.Id, Text(step, "heading"), Text(step, "body"), step.DemoId);
            bool last = _pageIdx >= wn.Steps.Length - 1;
            if (_pageIdx == 1) SetButtons(spec, new[] { last ? Act.Done : Act.Next }, 0);
            else SetButtons(spec, new[] { Act.Back, last ? Act.Done : Act.Next }, 1);
            OpenCard(CardCtx.WhatsNewPage, spec);
        }

        private static void WhatsNewPageAct(Act a)
        {
            if (a == Act.Back) { _pageIdx = Mathf.Max(1, _pageIdx - 1); ShowWhatsNewPage(); return; }
            if (a == Act.Next) { _pageIdx++; ShowWhatsNewPage(); return; }
            CloseCardCtx();
            TutorialProgressStore.SetState("whatsnew", TLessonState.Done);
            TutorialProgressStore.Flush();
        }

        // ---- Watch cards

        private static void WatchAct(Act a, float now)
        {
            var l = _watchLesson;
            switch (a)
            {
                case Act.Next:
                    _pageIdx = NextIndex(l, _pageIdx + 1, _watchTrack, true);
                    if (_pageIdx < 0) EndWatch(); else ShowWatchCard();
                    return;
                case Act.Back:
                {
                    int p = PrevIndex(l, _pageIdx - 1, _watchTrack, true);
                    if (p >= 0) _pageIdx = p;
                    ShowWatchCard();
                    return;
                }
                case Act.TryNow:
                    EndWatch();
                    if (l != null) { _pendingPlay = l; _pendingPlayWatch = false; }
                    return;
                default:
                    EndWatch();
                    return;
            }
        }

        // ---- Tour callouts

        private static void TourAct(Act a)
        {
            var l = TutorialChapters.FindLesson("menu");
            int count = l != null ? l.Steps.Length : 0;
            int first = FirstPage(l);   // the callouts page Steps[first..] - menu.setup is a strip
            switch (a)
            {
                case Act.TourTake: _pageIdx = first; ShowTourCard(); return;
                case Act.Next: _pageIdx = Mathf.Min(_pageIdx + 1, count - 1); ShowTourCard(); return;
                case Act.Back: _pageIdx = Mathf.Max(first, _pageIdx - 1); ShowTourCard(); return;
                case Act.Done: EndTour(TLessonState.Done); return;
                default: EndTour(TLessonState.Skipped); return;   // Skip tour / Skip lesson / No thanks
            }
        }

        // ================================================================ editor preview

        private static void ShowPreview()
        {
            var step = _preview;
            if (step == null) return;
            switch (step.Kind)
            {
                case TPresentation.Card:
                {
                    _cardStep = step;
                    string bodyField = step.Has("body") ? "body" : "says@mp";
                    var spec = NewSpec(step.Id, Text(step, "heading"), Text(step, bodyField), step.DemoId);
                    SetEditKeys(spec, step, "heading", bodyField);
                    var acts = new List<Act>(3);
                    if (step.Has("button@0")) acts.Add(Act.B0);
                    if (step.Has("button@1")) acts.Add(Act.B1);
                    if (step.Has("button@2")) acts.Add(Act.B2);
                    if (acts.Count == 0) acts.Add(Act.Done);
                    SetButtons(spec, acts.ToArray(), 0);
                    OpenCard(CardCtx.Preview, spec);
                    return;
                }
                case TPresentation.Callout:
                {
                    _cardStep = step;
                    var spec = NewSpec(step.Id, Text(step, "title"), Text(step, "callout"), step.DemoId);
                    SetEditKeys(spec, step, "title", "callout");
                    SetButtons(spec, new[] { Act.Done }, 0);
                    Rect r = default(Rect);
                    bool got = false;
                    try { got = UiaControlCenter.IsOpen && UiaControlCenter.TryGetAnchorRect(step.Anchor, out r); }
                    catch { got = false; }
                    if (got)
                    {
                        _suppress++;
                        try { TutorialCoach.OpenCallout(spec, r); }
                        catch (Exception e) { ReportTickError("callout", e); }
                        finally { _suppress--; }
                        _cardOpenFrame = Time.frameCount;
                        _card = CoachCardOpen() ? CardCtx.Preview : CardCtx.None;
                    }
                    else OpenCard(CardCtx.Preview, spec);
                    return;
                }
                default:
                {
                    string header;
                    string body = PreviewStripText(step, out header);
                    ShowStripRaw(step.Id, header, body, step.DemoId);
                    ApplySpots(step);
                    return;
                }
            }
        }

        // ================================================================ teardown

        /// <summary>Hot-reload / plugin teardown: every static back to its load-time value. Persisted
        /// state is left consistent (an interrupted just-in-time lesson becomes Offered); the caller
        /// then runs TutorialProgressStore.Shutdown, which flushes.</summary>
        internal static void Shutdown()
        {
            try
            {
                // (Tour mode: the lesson the first-run tour stands at stays Active - it resumes there.)
                if (_flow == Flow.Lesson && _active != null && _active.Id != "core"
                    && StateOf(_active.Id) == TLessonState.Active && !IsTourCurrent(_active))
                    TutorialProgressStore.SetState(_active.Id, TLessonState.Offered);
                if (_flow == Flow.Tour && StateOf("menu") == TLessonState.Active
                    && !IsTourCurrent(TutorialChapters.FindLesson("menu")))
                    TutorialProgressStore.SetState("menu", TLessonState.New);
            }
            catch { }
            WriteCarry();   // before the reset: an F6 mid-world is not a new world entry (see ReadCarry)

            _init = false;
            _lastTick = 0f;
            _nextPoll = _lastPoll = _nextSchedule = 0f;
            _inWorld = false;
            _entrySerial = 0;
            _worldEnterTime = -999f;
            _resumeOfferedThisEntry = _whatsNewHandledThisEntry = _welcomeHoldForNextEntry = false;
            _laterAtEntry = -1;
            _welcomeShownAtEntry = -1;
            _gateSince = -1f;
            _inviteShown = false;
            _remindPending = false;
            _remindCalm = 0f;
            _nextEntryEval = 0f;
            _tipsWasOn = true;
            _stoodDown = false;
            for (int i = 0; i < _seen.Length; i++) _seen[i] = 0;
            _wheelKind = TWheelKind.Other;
            _wheelSticky = _childSinceOpen = false;
            _lastHoverAt = -1f;
            _hoverDwells = 0;
            _arrowSeenThisStep = false;
            _childPendingFrame = -1;
            _satFromStack = _heldStackWheel = false;
            _compChild = false;
            _hubDepth = 0;
            _wheelSerial = 0;
            _setupOk = false;
            _setupStackFor = 0f;
            _beltSpare = false;
            _beltSpareSerial = -1;
            _tourLive = _tourEndDue = _holdTour = _inPress = false;
            _tourRetryAt = 0f;
            _jitKeys = null;
            _calmNow = false;
            _lastActiveHand = null;
            _playSeconds = 0f;
            _tabletFor = _lowFor = _toolFor = _stackHoverFor = _arrowHoverFor = 0f;
            _suitedFor = _bareFor = _gridFreeFor = _gridLockedFor = _gridInteractiveFor = 0f;
            _gridWasOpen = _gridFreedSinceOpen = false;
            _northFor = 0f;
            _otherBeltFrame = -1;
            _escOopsArmed = _vanillaMenuWasUp = false;
            ClearFlow();   // also _activeAuto, _tourAuto, _activeTour, _asideTracks, _doStepsAtTrack/_doDoneAtTrack,
                           // _tourMaxPage, _holdWhy, _windowOpen, _windowHeld
            _holdWhyShown = null;
            _holdAcute = false;
            _stepActive = _stepWait = _pulseNext = _stepLive = 0f;
            _windowHand = 0L;
            _stepRead = 8f;
            _ev = _evPending = 0UL;
            _flagA = _flagB = false;
            _sayField = _branchField = _oopsField = _header = null;
            _oopsUntil = 0f;
            _confirming = _pendingConfirmDone = false;
            _confirmStart = _confirmMin = 0f;
            _onHold = false;
            _unsafeFor = _calmFor = _contextLostFor = 0f;
            _doSteps = _doDone = 0;
            _handStartId = 0L;
            _stowItem = null;
            _skipStow = false;
            _windowWaitClose = false;
            _windowWaitFor = 0f;
            _lastLessonEnd = _lastTipEnd = -999f;
            _coreEndedAt = -999f;
            _stripShowing = false;
            _chromeShown = Chrome.None;
            _drainShown = -2f;
            _timedLeft = _timedTotal = 0f;
            _timedStep = null;
            _timedText = _timedHeader = _timedDemo = _timedId = null;
            _spotStep = null;
            _spotMissing = 0;
            _spotFallback = false;
            _card = CardCtx.None;
            _acts = new Act[0];
            _cardStep = null;
            _cardBodyField = null;
            _cardOpenFrame = -1;
            _cardRetryAt = 0f;
            _pendingLater = false;
            _suppress = 0;
            _pageIdx = 0;
            _watchLesson = null;
            _watchTrack = 0;
            _pendingOpenDesigner = false;
            _f10OpenedByUsFrame = -100;
            _f10ClosedByUsFrame = -100;
            _f10WasOpen = false;
            for (int i = 0; i < _queue.Length; i++) _queue[i] = default(Queued);
            _queueCount = 0;
            ClearTipMoments();
            _pendingPlay = null;
            _pendingPlayWatch = false;
            _preview = null;
            _cardBeforePreview = CardCtx.None;
            _cardStepBeforePreview = null;
            _cardBodyBeforePreview = null;
            _names.Clear();
            _reported.Clear();
            for (int i = 0; i < _tickFails.Length; i++) { _tickFails[i] = 0; _tickOff[i] = false; }
            TutorialSafety.Shutdown();
            TutorialLint.Shutdown();
        }
    }
}
