using System;
using System.Collections.Generic;
using StationeersUIMod.UI.Hud;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>How a step is shown. Card = the coach's modal window (pauses single-player);
    /// Strip = the non-modal fade-in line (game runs); Callout = a small card pinned beside part of
    /// F10 (the menu tour); Tip = a one-line "as I go" strip; Chrome = supporting UI strings.</summary>
    internal enum TPresentation : byte { Card, Strip, Callout, Tip, Chrome }

    /// <summary>One editable string of a step: its copy key (contract s6 grammar), a human label for
    /// the editor, and the character budget the lint and the editor measure the RESOLVED text against.</summary>
    internal struct TField { internal string Key; internal string Label; internal int Budget; }

    /// <summary>What finishes a step (the director evaluates these; see TutorialDirector.IsDone).
    /// Card = a button; Next = a callout's Next; Read = its timed read (S2) only; the rest name the
    /// real action or signal that proves the player did it.
    ///
    /// <para>The last block (tour mode, 2026-09-26) is for SETUP steps (<see cref="TStep.IsSetup"/>):
    /// each is a STATE - true for as long as the lesson's situation exists, not an event that has to
    /// happen during the step - so a setup whose situation is already there is done the moment it
    /// starts. <see cref="GridFreed"/> (window open, mouse free for 1 s) is reused by lesson 12's
    /// setup; <see cref="DesignerKey"/> is the one event, and the director seeds it when the F9 press
    /// itself started the lesson.</para></summary>
    internal enum TDone : byte
    {
        Button = 0, Next, Read, Invite,
        Hands, BeltTap, WedgesRead, TakeTool, Stowed, HubBack, ChildWheel, GridFreed,
        GreyHovered, StowCommit, ToolWheel, ReplaceOpened, SwapCommit, InstallCommit,
        SplitCommit, GridSplit, ChipParked, ChipDropped, WorldCue, WorldGrab,
        GearHeld, DigitJump, InGrid, BeltPicker, BeltSwapOrBack, BagLevel, SearchTook, BagBound,
        ValueScrolled, HotkeyBound, KeepOpen, HoldCommit, HubMoved,
        GridTab, GridMove, DeviceWindow, GridShiftDrag, PinCreated, GridKeyboard,
        FacingNorth, VitalsTooltip,

        // ---- setup-step states (tour mode) ----
        /// <summary>Any UIA wheel is open (RadialController.AnyRadialOpen). Lessons 2 and 5.</summary>
        WheelOpen,
        /// <summary>The ACTIVE hand holds an item with a component socket - battery, gas or liquid
        /// canister, filter or cartridge slot; a Tablet counts. Lesson 3's own context (tool track).</summary>
        HoldingPartsTool,
        /// <summary>The ACTIVE hand holds a Tablet. Lesson 3's tablet track.</summary>
        HoldingTablet,
        /// <summary>The pointer rests 0.5 s or more on a stack wedge in an open wheel - lesson 4's own
        /// trigger test (the director's IsStackWedge).</summary>
        StackHovered,
        /// <summary>The belt wheel is open AND another belt is in reach (ToolbeltRadialFeature
        /// .BuildBeltPicker() gives 2+ entries) - lesson 7's own trigger. Scan on the belt-wheel open
        /// edge and cache it, as CarriesAnotherBelt does; never scan per frame.</summary>
        BeltWheelWithSpare,
        /// <summary>The bag wheel ({UIA_BagRadial}, TWheelKind.Bag) is open, or THE HUB is open inside
        /// the belt wheel (a bag level or SEARCH inside either counts). Lesson 8.</summary>
        BagWheelOpen,
        /// <summary>The open wheel shows at least one Setting or Value wedge (TWedgeKind.Setting /
        /// Value), or the SETTINGS branch that 3+ controls collapse into (ItemMenuBuilder
        /// .UseSettingsWedge) - in practice a gear wheel (1 - 6) or a held tool's wheel. The copy
        /// points at the jetpack ([4]): Stabilizer + Thrust, two controls, so both sit on its root
        /// wheel (Jetpack.cs V27798 :683-735). Lesson 10.</summary>
        SettingsWheelOpen,
        /// <summary>The visor half is on and the HUD tier is Suited or Robot - lesson 14's own context
        /// (ContextExists, TTrigger.Visor).</summary>
        SuitPowered,
        /// <summary>The visor half is on, the tier is Bare and the player is not a robot - lesson 15's
        /// own context (ContextExists, TTrigger.Senses).</summary>
        NoSuitPower,
        /// <summary>The UI Ascended menu is open (UiaControlCenter.IsOpen). F10 covers the strip, so
        /// this one has to be checked before the "covered" gate. Lesson 16.</summary>
        MenuOpen,
        /// <summary>The HUD Designer key was pressed during the step (the director's F9 intercept), or
        /// that press is what started the lesson. Lesson 17.</summary>
        DesignerKey,
    }

    /// <summary>Skip pattern (plan "Skip patterns"): S1 = a do-step that skips itself after 45 + 15 s
    /// and counts as SHOWN, not learned; S2 = a timed read (2.5 s + 0.06 s/char, 5-12 s) that a
    /// matching signal can finish early; S3 = a card (Next / Back / Skip; Esc = Later).</summary>
    internal enum TSkip : byte { None, S1, S2, S3 }

    /// <summary>Which trigger family fires a lesson (the director owns the evaluation).</summary>
    internal enum TTrigger : byte
    {
        None, Entry, Core, ReadWheel, Tools, Split, DragDrop, Gear, Belts, Bags, Stow, Settings,
        Speed, Window, Names, Visor, Senses, Menu, Designer, WhatsNew,
    }

    /// <summary>Skill evidence that marks a lesson LEARNED before it ever fires (plan A.7).</summary>
    internal enum TSkill : byte { None, Replace, Split, Drop, GearTapHold, BeltSwap, SearchAndBind, Hotkey, HoldCommits, PinAndDrag }

    /// <summary>A real HUD element a step outlines (SPOT). <see cref="Src"/> is the Readout source
    /// param ("ExternalPressure") or null.</summary>
    internal struct TSpot { internal HudElementType Type; internal string Src; }

    /// <summary>One step of a lesson. The first four fields are the contract's (s6); the rest are the
    /// director's own data. Pure data - nothing here is mutated after the static build.</summary>
    internal sealed class TStep
    {
        internal string Id;
        internal TPresentation Kind;
        internal string DemoId;           // demo-stage scene id (A.9 table) or "keys:<pattern>:<token>"; null = none
        internal TField[] Fields;         // every editable string, in export order

        internal TDone Done;
        internal TSkip Skip = TSkip.S2;
        internal float Timeout;           // "or N s": finishes as done after N s of active time (0 = none)
        internal float ReadSeconds;       // S2 override (0 = computed from the text length)
        internal byte Track;              // lesson 3: 0 = tools, 1 = tablet
        internal TWheelKind NeedsWheel;   // Other = none; else the strip shows the "reopen" chrome while it is shut
        internal string Opener;           // the token that reopens that wheel ("{UIA_ToolbeltRadial}")
        internal TSpot[] Spots;           // real HUD elements to outline (null = none)
        internal bool SpotFallbackHands;  // no spot on screen -> outline the hand boxes instead
        internal string Anchor;           // callouts: "tab:<title>", "search", "pause", "close" (+ Presentation's "master")

        /// <summary>Tour mode (2026-09-26): a "get into the situation" step. Always the FIRST step of
        /// its lesson (lesson 3: of each track); Kind Strip, Skip S1, and a Done that is a STATE (the
        /// setup block of <see cref="TDone"/>). The director advances silently when that state already
        /// holds on entry; it is never counted as a do-step, never checked for S4 context loss,
        /// never shown in Watch mode or counted in a lesson's "n/total"; and when it self-skips,
        /// the lesson is set aside for just-in-time (the tour moves on).</summary>
        internal bool IsSetup;

        internal TLesson Lesson;          // back-reference (null for tips)
        internal int Index;               // position in Lesson.Steps

        /// <summary>Does this step declare the field (e.g. "says@empty")?</summary>
        internal bool Has(string field)
        {
            if (Fields == null || string.IsNullOrEmpty(field)) return false;
            int n = Id.Length;
            for (int i = 0; i < Fields.Length; i++)
            {
                string k = Fields[i].Key;
                if (k != null && k.Length == n + 1 + field.Length && k[n] == '|'
                    && string.CompareOrdinal(k, 0, Id, 0, n) == 0
                    && string.CompareOrdinal(k, n + 1, field, 0, field.Length) == 0)
                    return true;
            }
            return false;
        }

        /// <summary>"&lt;id&gt;|&lt;field&gt;" (allocates - call on change only).</summary>
        internal string Key(string field) { return Id + "|" + field; }

        internal bool IsDoStep { get { return Skip == TSkip.S1; } }
    }

    /// <summary>One lesson. Id / Priority / Steps / TitleKey are the contract's (s6); the rest drive
    /// the director's triggers.</summary>
    internal sealed class TLesson
    {
        internal string Id;
        internal int Priority;            // queue tie-break, 1 = first (0 = not queued: entry/core/menu/designer/whatsnew)
        internal TStep[] Steps;
        internal string TitleKey;         // "lesson:<id>|title"

        internal int Number;              // the plan's lesson number (0-18; the overview is 0, beside the entry cards)
        internal TTrigger Trigger;
        internal bool NeedsCore;          // fires only once First Steps is done / skipped / "as I go"
        internal TSkill Skill;
        internal bool ContextBound;       // S4: the lesson pauses when its reason goes away
        internal string TrackTitleKey;    // lesson 3's tablet-track title key, else null

        /// <summary>Tour mode (2026-09-26): this lesson's place in the first-run tour - the {N} of
        /// chrome|tourheader and the order it plays in (1 = First Steps). 0 = not in the tour (the entry
        /// cards, the overview, What's new). <see cref="TutorialChapters.Tour"/> lists them in order.</summary>
        internal int TourOrder;

        /// <summary>Tour mode: the overview cards the tour opens with - right after Welcome &gt; Start,
        /// before TourOrder 1. Only lesson "overview".</summary>
        internal bool IsOverview;

        internal bool InTour { get { return TourOrder > 0; } }
    }

    /// <summary>
    /// The whole lesson script as DATA (Documentation/0.9.8.0/Tutorial-Plan-and-Script.md Part C,
    /// resolved against the code on 2026-09-26 - Documentation/0.9.8.0/Tutorial-Copy-Changes.md lists
    /// every line that moved, and why). Twenty lessons (0-18 plus the tour's overview), 101 steps
    /// (lesson 3's tablet track counted separately; 13 of them setup steps, 5 overview cards, and the
    /// tour's closing card), plus the seven "as I go" tips and the C.19 chrome keys. The words themselves live in <see cref="TutorialCopy"/>
    /// (shipped defaults) and <see cref="TutorialTextStore"/> (overrides); this file only names the
    /// keys, in export order.
    ///
    /// <para><b>Tour mode</b> (FlorpyDorp, 2026-09-26: "show all the lessons in order one after
    /// another when a player first opens the mod"): Welcome &gt; Start plays the
    /// <see cref="Overview"/> cards, then every lesson in <see cref="Tour"/> (TourOrder 1-17) back to
    /// back, then the entry.tourend card. A lesson whose situation is missing opens with a SETUP step
    /// (<see cref="TStep.IsSetup"/>) that tells the player how to get into it.</para>
    ///
    /// <para><b>Step ids are load-bearing</b>: text overrides and Progress.xml key on them. Change
    /// words freely (in game or in TutorialCopy.g.cs), never ids. A new lesson bumps
    /// <see cref="ScriptVersion"/> once the script has shipped (the tour and its overview are part
    /// of the first shipped script, 0.9.8.0 - still version 1).</para>
    ///
    /// <para>Pure data; built once by the static initializer; nothing to reset on hot reload.</para>
    /// </summary>
    internal static class TutorialChapters
    {
        internal const int ScriptVersion = 1;

        internal static readonly TLesson[] Lessons = Link(BuildLessons());

        /// <summary>Tour mode: the lessons of the first-run tour in play order (TourOrder 1..N);
        /// <c>Tour.Length</c> is the {TOTAL} of chrome|tourheader. Declared after
        /// <see cref="Lessons"/> - static initializers run in text order.</summary>
        internal static readonly TLesson[] Tour = BuildTour(Lessons);

        /// <summary>Tour mode: the overview lesson (<see cref="TLesson.IsOverview"/>), or null.</summary>
        internal static readonly TLesson Overview = FindOverview(Lessons);

        internal static readonly TStep[] Tips = BuildTips();
        internal static readonly string[] TipKeys = KeysOf(Tips);
        internal static readonly TField[] ChromeFields = BuildChrome();
        internal static readonly string[] ChromeKeys = KeysOf(ChromeFields);

        private static Dictionary<string, TStep> _byId;
        private static Dictionary<string, TLesson> _lessonById;

        /// <summary>The step with this id (lesson steps and tips), or null.</summary>
        internal static TStep FindStep(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return null;
            EnsureIndex();
            TStep s;
            return _byId.TryGetValue(stepId, out s) ? s : null;
        }

        /// <summary>The lesson with this id (case-insensitive), or null.</summary>
        internal static TLesson FindLesson(string lessonId)
        {
            if (string.IsNullOrEmpty(lessonId)) return null;
            EnsureIndex();
            TLesson l;
            return _lessonById.TryGetValue(lessonId, out l) ? l : null;
        }

        /// <summary>Every copy key in EXPORT order (contract s7): per lesson its title key, then its
        /// steps' fields in array order; then the tips; then the chrome. Allocates - lint / editor /
        /// exporter use only.</summary>
        internal static List<string> AllKeys()
        {
            var keys = new List<string>(420);
            for (int i = 0; i < Lessons.Length; i++)
            {
                var l = Lessons[i];
                keys.Add(l.TitleKey);
                for (int s = 0; s < l.Steps.Length; s++)
                {
                    var f = l.Steps[s].Fields;
                    for (int k = 0; f != null && k < f.Length; k++) keys.Add(f[k].Key);
                }
            }
            keys.AddRange(TipKeys);
            keys.AddRange(ChromeKeys);
            return keys;
        }

        /// <summary>The budget for any copy key (lesson titles 28; tips 125; chrome and step fields
        /// from their declarations), or 0 when the key is not part of the script.</summary>
        internal static int BudgetOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            if (key.StartsWith("lesson:", StringComparison.Ordinal)) return 28;
            TField f;
            return TryField(key, out f) ? f.Budget : 0;
        }

        /// <summary>The declared field for a copy key (steps, tips, chrome, lesson-track titles).</summary>
        internal static bool TryField(string key, out TField field)
        {
            field = default(TField);
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < ChromeFields.Length; i++)
                if (string.Equals(ChromeFields[i].Key, key, StringComparison.Ordinal)) { field = ChromeFields[i]; return true; }
            int bar = key.IndexOf('|');
            TStep s = bar > 0 ? FindStep(key.Substring(0, bar)) : null;
            if (s == null)
            {
                // A field declared on a step whose key does not start with the step id (lesson 3's
                // tablet-track title lives on tools.tablet.setup, the track's first step) - scan the lessons.
                for (int i = 0; i < Lessons.Length; i++)
                    for (int j = 0; j < Lessons[i].Steps.Length; j++)
                        if (Scan(Lessons[i].Steps[j].Fields, key, out field)) return true;
                return false;
            }
            return Scan(s.Fields, key, out field);
        }

        private static bool Scan(TField[] fields, string key, out TField field)
        {
            field = default(TField);
            for (int i = 0; fields != null && i < fields.Length; i++)
                if (string.Equals(fields[i].Key, key, StringComparison.Ordinal)) { field = fields[i]; return true; }
            return false;
        }

        private static void EnsureIndex()
        {
            if (_byId != null) return;
            var byId = new Dictionary<string, TStep>(StringComparer.Ordinal);
            var byLesson = new Dictionary<string, TLesson>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Lessons.Length; i++)
            {
                byLesson[Lessons[i].Id] = Lessons[i];
                for (int s = 0; s < Lessons[i].Steps.Length; s++) byId[Lessons[i].Steps[s].Id] = Lessons[i].Steps[s];
            }
            for (int i = 0; i < Tips.Length; i++) byId[Tips[i].Id] = Tips[i];
            _lessonById = byLesson;
            _byId = byId;
        }

        private static TLesson[] Link(TLesson[] lessons)
        {
            for (int i = 0; i < lessons.Length; i++)
            {
                var l = lessons[i];
                for (int s = 0; s < l.Steps.Length; s++)
                {
                    l.Steps[s].Lesson = l;
                    l.Steps[s].Index = s;
                    var f = l.Steps[s].Fields;
                    for (int k = 0; f != null && k < f.Length; k++)
                        if (f[k].Key.StartsWith("lesson:" + l.Id + "|title@", StringComparison.Ordinal))
                            l.TrackTitleKey = f[k].Key;
                }
            }
            return lessons;
        }

        /// <summary>The TourOrder &gt; 0 lessons sorted by TourOrder (stable; array order breaks ties).</summary>
        private static TLesson[] BuildTour(TLesson[] lessons)
        {
            var list = new List<TLesson>(lessons.Length);
            for (int i = 0; i < lessons.Length; i++)
            {
                var l = lessons[i];
                if (l == null || l.TourOrder <= 0) continue;
                int at = list.Count;
                while (at > 0 && list[at - 1].TourOrder > l.TourOrder) at--;
                list.Insert(at, l);
            }
            return list.ToArray();
        }

        private static TLesson FindOverview(TLesson[] lessons)
        {
            for (int i = 0; i < lessons.Length; i++)
                if (lessons[i] != null && lessons[i].IsOverview) return lessons[i];
            return null;
        }

        private static string[] KeysOf(TStep[] steps)
        {
            var list = new List<string>();
            for (int i = 0; i < steps.Length; i++)
                for (int k = 0; steps[i].Fields != null && k < steps[i].Fields.Length; k++) list.Add(steps[i].Fields[k].Key);
            return list.ToArray();
        }

        private static string[] KeysOf(TField[] fields)
        {
            var a = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) a[i] = fields[i].Key;
            return a;
        }

        private static TField F(string key, string label, int budget)
        {
            return new TField { Key = key, Label = label, Budget = budget };
        }

        private static TSpot Spot(HudElementType type, string src = null)
        {
            return new TSpot { Type = type, Src = src };
        }

        // ================================================================ the script
        // Part C, step by step: ids, presentation, demo, done-condition, skip pattern and the copy
        // keys (in export order). The words are in TutorialCopy.g.cs - keep the two in step; `uiatutorial
        // lint` reports any key without shipped copy and any shipped copy without a key.
        private static TLesson[] BuildLessons()
        {
            return new[]
            {
                // ---- 0 entry - WELCOME
                new TLesson
                {
                    Id = "entry", Number = 0, Priority = 0, TitleKey = "lesson:entry|title",
                    Trigger = TTrigger.Entry, NeedsCore = false, Skill = TSkill.None, ContextBound = false,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "entry.welcome", Kind = TPresentation.Card, DemoId = "welcome", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("entry.welcome|title", "Title (list row / replay heading)", 28),
                                F("entry.welcome|heading", "Heading", 28),
                                F("entry.welcome|body", "Body (single-player, paused)", 280),
                                F("entry.welcome|body@mp", "Body (multiplayer - no pause)", 280),
                                F("entry.welcome|body@running", "Body (single-player, card pause turned off)", 280),
                                F("entry.welcome|button@0", "Button 1 (primary)", 24),
                                F("entry.welcome|button@1", "Button 2", 24),
                                F("entry.welcome|button@2", "Button 3", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "entry.invite", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_Menu}", Done = TDone.Invite, Skip = TSkip.S2, ReadSeconds = 12f,
                            Fields = new[]
                            {
                                F("entry.invite|title", "Title (list row / replay heading)", 28),
                                F("entry.invite|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "entry.resume", Kind = TPresentation.Card, DemoId = "welcome", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("entry.resume|title", "Title (list row / replay heading)", 28),
                                F("entry.resume|heading", "Heading", 28),
                                F("entry.resume|body", "Body", 280),
                                F("entry.resume|button@0", "Button 1 (primary)", 24),
                                F("entry.resume|button@1", "Button 2", 24),
                                F("entry.resume|button@2", "Button 3", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "entry.hold", Kind = TPresentation.Card, DemoId = null, Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("entry.hold|title", "Title (list row / replay heading)", 28),
                                F("entry.hold|heading", "Heading", 28),
                                F("entry.hold|body", "Body", 280),
                                F("entry.hold|button@0", "Button 1 (primary)", 24),
                                F("entry.hold|button@1", "Button 2", 24),
                            },
                        },
                        // Tour mode: the card after the tour's last lesson (opened by id, like the
                        // cards above). Start playing / See all lessons (F10 > Guide).
                        new TStep
                        {
                            Id = "entry.tourend", Kind = TPresentation.Card, DemoId = "finish", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("entry.tourend|title", "Title (list row / replay heading)", 28),
                                F("entry.tourend|heading", "Heading", 28),
                                F("entry.tourend|body", "Body", 280),
                                F("entry.tourend|button@0", "Button 1 (primary)", 24),
                                F("entry.tourend|button@1", "Button 2", 24),
                            },
                        },
                    },
                },
                // ---- overview - THE BIG PICTURE (tour mode: five paused cards right after Welcome >
                // Start, before First Steps). One idea per card; each card's button@0 moves on, so
                // the plain lesson path (card -> button -> next step) plays them as they are.
                new TLesson
                {
                    Id = "overview", Number = 0, Priority = 0, TitleKey = "lesson:overview|title",
                    Trigger = TTrigger.None, NeedsCore = false, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 0, IsOverview = true,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "overview.hands", Kind = TPresentation.Card, DemoId = "handswap", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("overview.hands|title", "Title (list row / replay heading)", 28),
                                F("overview.hands|heading", "Heading", 28),
                                F("overview.hands|body", "Body", 280),
                                F("overview.hands|button@0", "Button (next card)", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "overview.wheels", Kind = TPresentation.Card, DemoId = "actionword", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("overview.wheels|title", "Title (list row / replay heading)", 28),
                                F("overview.wheels|heading", "Heading", 28),
                                F("overview.wheels|body", "Body", 280),
                                F("overview.wheels|button@0", "Button (next card)", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "overview.window", Kind = TPresentation.Card, DemoId = "gridintro", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("overview.window|title", "Title (list row / replay heading)", 28),
                                F("overview.window|heading", "Heading", 28),
                                F("overview.window|body", "Body", 280),
                                F("overview.window|button@0", "Button (next card)", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "overview.stow", Kind = TPresentation.Card, DemoId = "takeandstow", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("overview.stow|title", "Title (list row / replay heading)", 28),
                                F("overview.stow|heading", "Heading", 28),
                                F("overview.stow|body", "Body", 280),
                                F("overview.stow|button@0", "Button (next card)", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "overview.menu", Kind = TPresentation.Card, DemoId = "keys:tap:{UIA_Menu}", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("overview.menu|title", "Title (list row / replay heading)", 28),
                                F("overview.menu|heading", "Heading", 28),
                                F("overview.menu|body", "Body", 280),
                                F("overview.menu|button@0", "Button (last card - on to First Steps)", 24),
                            },
                        },
                    },
                },
                // ---- 1 core - FIRST STEPS
                new TLesson
                {
                    Id = "core", Number = 1, Priority = 0, TitleKey = "lesson:core|title",
                    Trigger = TTrigger.Core, NeedsCore = false, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 1,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "core.hands", Kind = TPresentation.Strip, DemoId = "handswap", Done = TDone.Hands, Skip = TSkip.S1, Spots = new[] { Spot(HudElementType.HandBoxes) },
                            Fields = new[]
                            {
                                F("core.hands|title", "Title (list row / replay heading)", 28),
                                F("core.hands|says", "Says (active hand full, other empty)", 125),
                                F("core.hands|says@empty", "Says (active hand already empty)", 125),
                                F("core.hands|says@bothfull", "Says (both hands full)", 125),
                                F("core.hands|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "core.beltopen", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.BeltTap, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("core.beltopen|title", "Title (list row / replay heading)", 28),
                                F("core.beltopen|says", "Says", 125),
                                F("core.beltopen|then", "Then (confirmation)", 100),
                                F("core.beltopen|oops@held", "Oops (held too long - the wheel closed on release)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "core.read", Kind = TPresentation.Strip, DemoId = "actionword", Done = TDone.WedgesRead, Skip = TSkip.S2, ReadSeconds = 10f, NeedsWheel = TWheelKind.Belt, Opener = "{UIA_ToolbeltRadial}",
                            Fields = new[]
                            {
                                F("core.read|title", "Title (list row / replay heading)", 28),
                                F("core.read|says", "Says", 125),
                                F("core.read|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "core.take", Kind = TPresentation.Strip, DemoId = "takeandstow", Done = TDone.TakeTool, Skip = TSkip.S1, NeedsWheel = TWheelKind.Belt, Opener = "{UIA_ToolbeltRadial}",
                            Fields = new[]
                            {
                                F("core.take|title", "Title (list row / replay heading)", 28),
                                F("core.take|says", "Says", 125),
                                F("core.take|then", "Then (confirmation)", 100),
                                F("core.take|branch@nobelt", "Branch (no belt, or no tools on it)", 125),
                                F("core.take|oops@hub", "Oops (opened The Hub instead)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "core.stow", Kind = TPresentation.Strip, DemoId = "takeandstow", Done = TDone.Stowed, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("core.stow|title", "Title (list row / replay heading)", 28),
                                F("core.stow|says", "Says (Smart Stow: Simple mode)", 125),
                                F("core.stow|says@complex", "Says (Smart Stow: Complex mode)", 125),
                                F("core.stow|then", "Then (confirmation)", 100),
                                F("core.stow|then@elsewhere", "Then (it went somewhere else)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "core.back", Kind = TPresentation.Strip, DemoId = "hubback", Done = TDone.HubBack, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("core.back|title", "Title (list row / replay heading)", 28),
                                F("core.back|says", "Says", 125),
                                F("core.back|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "core.pushout", Kind = TPresentation.Strip, DemoId = "pushout", Done = TDone.ChildWheel, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("core.pushout|title", "Title (list row / replay heading)", 28),
                                F("core.pushout|says", "Says", 125),
                                F("core.pushout|then", "Then (confirmation)", 100),
                                F("core.pushout|branch@noarrow", "Branch (20 s without an arrowed wedge)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "core.window", Kind = TPresentation.Strip, DemoId = "gridintro", Done = TDone.GridFreed, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("core.window|title", "Title (list row / replay heading)", 28),
                                F("core.window|says", "Says", 125),
                                F("core.window|then", "Then (confirmation)", 100),
                                F("core.window|oops@esc", "Oops (Esc opened the game menu)", 125),
                            },
                        },
                        // In the tour this card is the hand-over to lesson 2: body@tour, and the
                        // director's chrome|tournext / chrome|tourstop buttons instead of button@0/@1.
                        new TStep
                        {
                            Id = "core.finish", Kind = TPresentation.Card, DemoId = "finish", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("core.finish|title", "Title (list row / replay heading)", 28),
                                F("core.finish|heading", "Heading", 28),
                                F("core.finish|body", "Body", 280),
                                F("core.finish|body@tour", "Body (first-run tour - more lessons follow)", 280),
                                F("core.finish|button@0", "Button 1 (primary)", 24),
                                F("core.finish|button@1", "Button 2", 24),
                            },
                        },
                    },
                },
                // ---- 2 readwheel - READING A WHEEL
                new TLesson
                {
                    Id = "readwheel", Number = 2, Priority = 4, TitleKey = "lesson:readwheel|title",
                    Trigger = TTrigger.ReadWheel, NeedsCore = true, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 2,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "readwheel.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.WheelOpen, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("readwheel.setup|title", "Title (list row / replay heading)", 28),
                                F("readwheel.setup|says", "Says (setup: no wheel open yet)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "readwheel.middle", Kind = TPresentation.Strip, DemoId = "readout", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("readwheel.middle|title", "Title (list row / replay heading)", 28),
                                F("readwheel.middle|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "readwheel.grey", Kind = TPresentation.Strip, DemoId = "readout", Done = TDone.GreyHovered, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("readwheel.grey|title", "Title (list row / replay heading)", 28),
                                F("readwheel.grey|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "readwheel.stow", Kind = TPresentation.Strip, DemoId = "stowwedge", Done = TDone.StowCommit, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("readwheel.stow|title", "Title (list row / replay heading)", 28),
                                F("readwheel.stow|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "readwheel.hints", Kind = TPresentation.Strip, DemoId = "hintring", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("readwheel.hints|title", "Title (list row / replay heading)", 28),
                                F("readwheel.hints|says", "Says", 125),
                                F("readwheel.hints|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 3 tools - TOOLS AND BATTERIES
                new TLesson
                {
                    Id = "tools", Number = 3, Priority = 1, TitleKey = "lesson:tools|title",
                    Trigger = TTrigger.Tools, NeedsCore = true, Skill = TSkill.Replace, ContextBound = true,
                    TourOrder = 3,   // the tour plays track 0 (tools), then track 1 (tablet) - each opens with its own setup
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "tools.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.HoldingPartsTool, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("tools.setup|title", "Title (list row / replay heading)", 28),
                                F("tools.setup|says", "Says (setup: no tool with parts in hand)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.open", Kind = TPresentation.Strip, DemoId = "toolreplace", Done = TDone.ToolWheel, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("tools.open|title", "Title (list row / replay heading)", 28),
                                F("tools.open|says", "Says (a tool with parts in hand)", 125),
                                F("tools.open|says@low", "Says (held tool at 20% battery or less)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.careful", Kind = TPresentation.Strip, DemoId = "toolreplace", Done = TDone.ChildWheel, Skip = TSkip.S1, NeedsWheel = TWheelKind.HeldTool, Opener = "{UIA_ToolRadial}",
                            Fields = new[]
                            {
                                F("tools.careful|title", "Title (list row / replay heading)", 28),
                                F("tools.careful|says", "Says (battery)", 125),
                                F("tools.careful|says@canister", "Says (gas canister - welder)", 125),
                                F("tools.careful|oops@took", "Oops (the part was taken out)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.replace", Kind = TPresentation.Strip, DemoId = "toolreplace", Done = TDone.ReplaceOpened, Skip = TSkip.S1, NeedsWheel = TWheelKind.HeldTool, Opener = "{UIA_ToolRadial}",
                            Fields = new[]
                            {
                                F("tools.replace|title", "Title (list row / replay heading)", 28),
                                F("tools.replace|says", "Says (battery)", 125),
                                F("tools.replace|says@canister", "Says (gas canister)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.pick", Kind = TPresentation.Strip, DemoId = "toolreplace", Done = TDone.SwapCommit, Skip = TSkip.S1, NeedsWheel = TWheelKind.HeldTool, Opener = "{UIA_ToolRadial}",
                            Fields = new[]
                            {
                                F("tools.pick|title", "Title (list row / replay heading)", 28),
                                F("tools.pick|says", "Says", 125),
                                F("tools.pick|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.tablet.setup", Kind = TPresentation.Strip, DemoId = "hubroot", Done = TDone.HoldingTablet, Skip = TSkip.S1, Track = 1, IsSetup = true,
                            Fields = new[]
                            {
                                F("lesson:tools|title@tablet", "Lesson title (tablet track)", 28),
                                F("tools.tablet.setup|title", "Title (list row / replay heading)", 28),
                                F("tools.tablet.setup|says", "Says (setup: no tablet in hand)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.tablet.open", Kind = TPresentation.Strip, DemoId = "tabletinstall", Done = TDone.ToolWheel, Skip = TSkip.S1, Track = 1,
                            Fields = new[]
                            {
                                F("tools.tablet.open|title", "Title (list row / replay heading)", 28),
                                F("tools.tablet.open|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.tablet.install", Kind = TPresentation.Strip, DemoId = "tabletinstall", Done = TDone.InstallCommit, Skip = TSkip.S1, Track = 1, NeedsWheel = TWheelKind.HeldTool, Opener = "{UIA_ToolRadial}",
                            Fields = new[]
                            {
                                F("tools.tablet.install|title", "Title (list row / replay heading)", 28),
                                F("tools.tablet.install|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.tablet.replace", Kind = TPresentation.Strip, DemoId = "toolreplace.cartridge", Done = TDone.SwapCommit, Skip = TSkip.S2, Track = 1,
                            Fields = new[]
                            {
                                F("tools.tablet.replace|title", "Title (list row / replay heading)", 28),
                                F("tools.tablet.replace|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "tools.tablet.window", Kind = TPresentation.Strip, DemoId = "devicewindow", Done = TDone.DeviceWindow, Skip = TSkip.S2, Track = 1,
                            Fields = new[]
                            {
                                F("tools.tablet.window|title", "Title (list row / replay heading)", 28),
                                F("tools.tablet.window|says", "Says", 125),
                            },
                        },
                    },
                },
                // ---- 4 split - SPLITTING STACKS
                new TLesson
                {
                    Id = "split", Number = 4, Priority = 2, TitleKey = "lesson:split|title",
                    Trigger = TTrigger.Split, NeedsCore = true, Skill = TSkill.Split, ContextBound = false,
                    TourOrder = 4,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "split.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.StackHovered, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("split.setup|title", "Title (list row / replay heading)", 28),
                                F("split.setup|says", "Says (setup: no stack pointed at)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "split.open", Kind = TPresentation.Strip, DemoId = "split", Done = TDone.ChildWheel, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("split.open|title", "Title (list row / replay heading)", 28),
                                F("split.open|says", "Says (host / single-player)", 125),
                                F("split.open|says@client", "Says (multiplayer client - no SPLIT COUNT)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "split.count", Kind = TPresentation.Strip, DemoId = "split", Done = TDone.SplitCommit, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("split.count|title", "Title (list row / replay heading)", 28),
                                F("split.count|says", "Says (host / single-player)", 125),
                                F("split.count|says@client", "Says (multiplayer client - no SPLIT COUNT)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "split.window", Kind = TPresentation.Strip, DemoId = "splitpopup", Done = TDone.GridSplit, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("split.window|title", "Title (list row / replay heading)", 28),
                                F("split.window|says", "Says (host / single-player)", 125),
                                F("split.window|says@client", "Says (multiplayer client - no number button)", 125),
                                F("split.window|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 5 dragdrop - DRAG AND DROP
                new TLesson
                {
                    Id = "dragdrop", Number = 5, Priority = 1, TitleKey = "lesson:dragdrop|title",
                    Trigger = TTrigger.DragDrop, NeedsCore = true, Skill = TSkill.Drop, ContextBound = false,
                    TourOrder = 5,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "dragdrop.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.WheelOpen, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("dragdrop.setup|title", "Title (list row / replay heading)", 28),
                                F("dragdrop.setup|says", "Says (setup: no wheel open yet)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "dragdrop.park", Kind = TPresentation.Strip, DemoId = "dragpark", Done = TDone.ChipParked, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("dragdrop.park|title", "Title (list row / replay heading)", 28),
                                F("dragdrop.park|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "dragdrop.drop", Kind = TPresentation.Strip, DemoId = "dragpark", Done = TDone.ChipDropped, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("dragdrop.drop|title", "Title (list row / replay heading)", 28),
                                F("dragdrop.drop|says", "Says", 125),
                                F("dragdrop.drop|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "dragdrop.worldslot", Kind = TPresentation.Strip, DemoId = "worldslot", Done = TDone.WorldCue, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("dragdrop.worldslot|title", "Title (list row / replay heading)", 28),
                                F("dragdrop.worldslot|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "dragdrop.reach", Kind = TPresentation.Strip, DemoId = "altreach", Done = TDone.WorldGrab, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("dragdrop.reach|title", "Title (list row / replay heading)", 28),
                                F("dragdrop.reach|says", "Says", 125),
                                F("dragdrop.reach|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 6 gear - GEAR KEYS 1 - 6
                new TLesson
                {
                    Id = "gear", Number = 6, Priority = 2, TitleKey = "lesson:gear|title",
                    Trigger = TTrigger.Gear, NeedsCore = true, Skill = TSkill.GearTapHold, ContextBound = false,
                    TourOrder = 6,   // no setup: 6.1 itself says "TAP one for its wheel"
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "gear.tap", Kind = TPresentation.Strip, DemoId = "equiptaphold", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("gear.tap|title", "Title (list row / replay heading)", 28),
                                F("gear.tap|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "gear.hold", Kind = TPresentation.Strip, DemoId = "equiptaphold", Done = TDone.GearHeld, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("gear.hold|title", "Title (list row / replay heading)", 28),
                                F("gear.hold|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "gear.jump", Kind = TPresentation.Strip, DemoId = "keys:tap:{V:SuitSlot}", Done = TDone.DigitJump, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("gear.jump|title", "Title (list row / replay heading)", 28),
                                F("gear.jump|says", "Says", 125),
                                F("gear.jump|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "gear.ingrid", Kind = TPresentation.Strip, DemoId = "ingrid", Done = TDone.InGrid, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("gear.ingrid|title", "Title (list row / replay heading)", 28),
                                F("gear.ingrid|says", "Says", 125),
                                F("gear.ingrid|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 7 belts - BELTS
                new TLesson
                {
                    Id = "belts", Number = 7, Priority = 2, TitleKey = "lesson:belts|title",
                    Trigger = TTrigger.Belts, NeedsCore = true, Skill = TSkill.BeltSwap, ContextBound = false,
                    TourOrder = 7,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "belts.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.BeltWheelWithSpare, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("belts.setup|title", "Title (list row / replay heading)", 28),
                                F("belts.setup|says", "Says (setup: belt wheel shut, or no second belt)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "belts.q", Kind = TPresentation.Strip, DemoId = "beltswap", Done = TDone.BeltPicker, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("belts.q|title", "Title (list row / replay heading)", 28),
                                F("belts.q|says", "Says", 125),
                                F("belts.q|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "belts.swap", Kind = TPresentation.Strip, DemoId = "beltswap", Done = TDone.BeltSwapOrBack, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("belts.swap|title", "Title (list row / replay heading)", 28),
                                F("belts.swap|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "belts.home", Kind = TPresentation.Strip, DemoId = "ghostlabel", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("belts.home|title", "Title (list row / replay heading)", 28),
                                F("belts.home|says", "Says", 125),
                            },
                        },
                    },
                },
                // ---- 8 bags - BAGS AND SEARCH
                new TLesson
                {
                    Id = "bags", Number = 8, Priority = 2, TitleKey = "lesson:bags|title",
                    Trigger = TTrigger.Bags, NeedsCore = true, Skill = TSkill.SearchAndBind, ContextBound = false,
                    TourOrder = 8,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "bags.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_BagRadial}", Done = TDone.BagWheelOpen, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("bags.setup|title", "Title (list row / replay heading)", 28),
                                F("bags.setup|says", "Says (setup: bag wheel not open)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "bags.root", Kind = TPresentation.Strip, DemoId = "hubroot", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("bags.root|title", "Title (list row / replay heading)", 28),
                                F("bags.root|says", "Says", 125),
                                F("bags.root|then", "Then (bag key is the game's scoreboard key)", 100),
                                F("bags.root|then@rebound", "Then (bag key rebound)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "bags.inside", Kind = TPresentation.Strip, DemoId = "hubroot", Done = TDone.BagLevel, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("bags.inside|title", "Title (list row / replay heading)", 28),
                                F("bags.inside|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "bags.search", Kind = TPresentation.Strip, DemoId = "search", Done = TDone.SearchTook, Skip = TSkip.S2, ReadSeconds = 40f,
                            Fields = new[]
                            {
                                F("bags.search|title", "Title (list row / replay heading)", 28),
                                F("bags.search|says", "Says", 125),
                                F("bags.search|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "bags.recent", Kind = TPresentation.Strip, DemoId = "hubroot", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("bags.recent|title", "Title (list row / replay heading)", 28),
                                F("bags.recent|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "bags.bind", Kind = TPresentation.Strip, DemoId = "bindbag", Done = TDone.BagBound, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("bags.bind|title", "Title (list row / replay heading)", 28),
                                F("bags.bind|says", "Says", 125),
                                F("bags.bind|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 9 stow - SMART STOW
                new TLesson
                {
                    Id = "stow", Number = 9, Priority = 3, TitleKey = "lesson:stow|title",
                    Trigger = TTrigger.Stow, NeedsCore = true, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 9,   // no setup: three reads, nothing to get into
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "stow.order", Kind = TPresentation.Strip, DemoId = "stowroute", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("stow.order|title", "Title (list row / replay heading)", 28),
                                F("stow.order|says", "Says (Simple mode)", 125),
                                F("stow.order|says@complex", "Says (Complex mode)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "stow.where", Kind = TPresentation.Strip, DemoId = "keys:tap:{V:SmartStow}", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("stow.where|title", "Title (list row / replay heading)", 28),
                                F("stow.where|says", "Says", 125),
                                F("stow.where|then", "Then (Simple mode)", 100),
                                F("stow.where|then@complex", "Then (Complex mode)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "stow.profiles", Kind = TPresentation.Strip, DemoId = "stowroute", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("stow.profiles|title", "Title (list row / replay heading)", 28),
                                F("stow.profiles|says", "Says (Simple mode)", 125),
                                F("stow.profiles|says@complex", "Says (Complex mode)", 125),
                            },
                        },
                    },
                },
                // ---- 10 settings - SETTINGS AND HOTKEYS
                new TLesson
                {
                    Id = "settings", Number = 10, Priority = 3, TitleKey = "lesson:settings|title",
                    Trigger = TTrigger.Settings, NeedsCore = true, Skill = TSkill.Hotkey, ContextBound = false,
                    TourOrder = 10,
                    Steps = new[]
                    {
                        // Points at the jetpack's Thrust, not the suit: scrolling a suit's pressure or
                        // temperature by accident is the one value wedge that can hurt.
                        new TStep
                        {
                            Id = "settings.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{V:BackSlot}", Done = TDone.SettingsWheelOpen, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("settings.setup|title", "Title (list row / replay heading)", 28),
                                F("settings.setup|says", "Says (setup: no wheel with settings open)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "settings.value", Kind = TPresentation.Strip, DemoId = "valuescroll", Done = TDone.ValueScrolled, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("settings.value|title", "Title (list row / replay heading)", 28),
                                F("settings.value|says", "Says", 125),
                                F("settings.value|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "settings.hotkey", Kind = TPresentation.Strip, DemoId = "hotkey", Done = TDone.HotkeyBound, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("settings.hotkey|title", "Title (list row / replay heading)", 28),
                                F("settings.hotkey|says", "Says", 125),
                                F("settings.hotkey|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "settings.keepopen", Kind = TPresentation.Strip, DemoId = "shiftkeep", Done = TDone.KeepOpen, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("settings.keepopen|title", "Title (list row / replay heading)", 28),
                                F("settings.keepopen|says", "Says", 125),
                                F("settings.keepopen|says@inverted", "Says (wheels already set to stay open)", 125),
                                F("settings.keepopen|then", "Then (confirmation)", 100),
                                F("settings.keepopen|then@inverted", "Then (wheels already set to stay open)", 100),
                            },
                        },
                    },
                },
                // ---- 11 speed - FASTER WHEELS
                new TLesson
                {
                    Id = "speed", Number = 11, Priority = 5, TitleKey = "lesson:speed|title",
                    Trigger = TTrigger.Speed, NeedsCore = true, Skill = TSkill.HoldCommits, ContextBound = false,
                    TourOrder = 11,   // no setup: 11.1 itself says "HOLD [MMB]"
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "speed.hold", Kind = TPresentation.Strip, DemoId = "holdflick", Done = TDone.HoldCommit, Skip = TSkip.S1,
                            Fields = new[]
                            {
                                F("speed.hold|title", "Title (list row / replay heading)", 28),
                                F("speed.hold|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "speed.dive", Kind = TPresentation.Strip, DemoId = "holdflick", Done = TDone.HoldCommit, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("speed.dive|title", "Title (list row / replay heading)", 28),
                                F("speed.dive|says", "Says", 125),
                                F("speed.dive|then", "Then (confirmation)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "speed.move", Kind = TPresentation.Strip, DemoId = "hubdrag", Done = TDone.HubMoved, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("speed.move|title", "Title (list row / replay heading)", 28),
                                F("speed.move|says", "Says", 125),
                                F("speed.move|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 12 window - THE BIG WINDOW
                new TLesson
                {
                    Id = "window", Number = 12, Priority = 2, TitleKey = "lesson:window|title",
                    Trigger = TTrigger.Window, NeedsCore = true, Skill = TSkill.PinAndDrag, ContextBound = true,
                    TourOrder = 12,
                    Steps = new[]
                    {
                        // Reuses 1.8's GridFreed (window open, not a peek, mouse free for 1 s).
                        new TStep
                        {
                            Id = "window.setup", Kind = TPresentation.Strip, DemoId = "gridintro", Done = TDone.GridFreed, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("window.setup|title", "Title (list row / replay heading)", 28),
                                F("window.setup|says", "Says (setup: window shut or mouse not free)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "window.tabs", Kind = TPresentation.Strip, DemoId = "gridintro", Done = TDone.GridTab, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("window.tabs|title", "Title (list row / replay heading)", 28),
                                F("window.tabs|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "window.moves", Kind = TPresentation.Strip, DemoId = "gridmoves", Done = TDone.GridMove, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("window.moves|title", "Title (list row / replay heading)", 28),
                                F("window.moves|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "window.devices", Kind = TPresentation.Strip, DemoId = "devicewindow", Done = TDone.DeviceWindow, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("window.devices|title", "Title (list row / replay heading)", 28),
                                F("window.devices|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "window.shiftdrag", Kind = TPresentation.Strip, DemoId = "shiftdrag", Done = TDone.GridShiftDrag, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("window.shiftdrag|title", "Title (list row / replay heading)", 28),
                                F("window.shiftdrag|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "window.pin", Kind = TPresentation.Strip, DemoId = "pintear", Done = TDone.PinCreated, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("window.pin|title", "Title (list row / replay heading)", 28),
                                F("window.pin|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "window.keyboard", Kind = TPresentation.Strip, DemoId = "scrollselect", Done = TDone.GridKeyboard, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("window.keyboard|title", "Title (list row / replay heading)", 28),
                                F("window.keyboard|says", "Says", 125),
                            },
                        },
                    },
                },
                // ---- 13 names - NAME YOUR BAGS
                new TLesson
                {
                    Id = "names", Number = 13, Priority = 5, TitleKey = "lesson:names|title",
                    Trigger = TTrigger.Names, NeedsCore = true, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 13,   // no setup: two reads; duplicate names were only the reason to fire
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "names.why", Kind = TPresentation.Strip, DemoId = "bagnames", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("names.why|title", "Title (list row / replay heading)", 28),
                                F("names.why|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "names.how", Kind = TPresentation.Strip, DemoId = "bagnames", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("names.how|title", "Title (list row / replay heading)", 28),
                                F("names.how|says", "Says (Simple mode)", 125),
                                F("names.how|says@complex", "Says (Complex mode)", 125),
                                F("names.how|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 14 visor - YOUR VISOR
                new TLesson
                {
                    Id = "visor", Number = 14, Priority = 3, TitleKey = "lesson:visor|title",
                    Trigger = TTrigger.Visor, NeedsCore = true, Skill = TSkill.None, ContextBound = true,
                    TourOrder = 14,
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "visor.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{V:SuitSlot}", Done = TDone.SuitPowered, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("visor.setup|title", "Title (list row / replay heading)", 28),
                                F("visor.setup|says", "Says (setup: no suit power)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "visor.compass", Kind = TPresentation.Strip, DemoId = null, Done = TDone.FacingNorth, Skip = TSkip.S1, Timeout = 30f, Spots = new[] { Spot(HudElementType.Compass) },
                            Fields = new[]
                            {
                                F("visor.compass|title", "Title (list row / replay heading)", 28),
                                F("visor.compass|says", "Says", 125),
                                F("visor.compass|says@robot", "Says (robot)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "visor.outside", Kind = TPresentation.Strip, DemoId = null, Done = TDone.Read, Skip = TSkip.S2, Spots = new[] { Spot(HudElementType.Readout, "ExternalPressure"), Spot(HudElementType.Readout, "ExternalTemp") },
                            Fields = new[]
                            {
                                F("visor.outside|title", "Title (list row / replay heading)", 28),
                                F("visor.outside|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "visor.inside", Kind = TPresentation.Strip, DemoId = null, Done = TDone.Read, Skip = TSkip.S2, Spots = new[] { Spot(HudElementType.Readout, "InternalPressure"), Spot(HudElementType.Readout, "InternalTemp"), Spot(HudElementType.VitalsPanel) },
                            Fields = new[]
                            {
                                F("visor.inside|title", "Title (list row / replay heading)", 28),
                                F("visor.inside|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "visor.hover", Kind = TPresentation.Strip, DemoId = "keys:double:{V:MouseControl}", Done = TDone.VitalsTooltip, Skip = TSkip.S1, Timeout = 25f, Spots = new[] { Spot(HudElementType.VitalsPanel) },
                            Fields = new[]
                            {
                                F("visor.hover|title", "Title (list row / replay heading)", 28),
                                F("visor.hover|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "visor.moodlets", Kind = TPresentation.Strip, DemoId = null, Done = TDone.Read, Skip = TSkip.S2, Spots = new[] { Spot(HudElementType.MoodletDashboard) },
                            Fields = new[]
                            {
                                F("visor.moodlets|title", "Title (list row / replay heading)", 28),
                                F("visor.moodlets|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "visor.lowpower", Kind = TPresentation.Strip, DemoId = "lowpower", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("visor.lowpower|title", "Title (list row / replay heading)", 28),
                                F("visor.lowpower|says", "Says", 125),
                                F("visor.lowpower|says@robot", "Says (robot)", 125),
                                F("visor.lowpower|then", "Then (confirmation)", 100),
                            },
                        },
                    },
                },
                // ---- 15 senses - YOUR SENSES
                new TLesson
                {
                    Id = "senses", Number = 15, Priority = 1, TitleKey = "lesson:senses|title",
                    Trigger = TTrigger.Senses, NeedsCore = true, Skill = TSkill.None, ContextBound = true,
                    TourOrder = 15,
                    Steps = new[]
                    {
                        // SAFETY: the only setup that takes something away. Optional, only where the
                        // air is breathable, and "let this skip" is the default answer. Never shown to a
                        // robot (its situation can't exist - the director skips the lesson instead).
                        new TStep
                        {
                            Id = "senses.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{V:SuitSlot}", Done = TDone.NoSuitPower, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("senses.setup|title", "Title (list row / replay heading)", 28),
                                F("senses.setup|says", "Says (setup: suit still powered - optional, safe air only)", 125),
                                F("senses.setup|then", "Then (they took the power off - how to put it back)", 100),
                            },
                        },
                        new TStep
                        {
                            Id = "senses.why", Kind = TPresentation.Strip, DemoId = null, Done = TDone.Read, Skip = TSkip.S2, Spots = new[] { Spot(HudElementType.BareSenses) }, SpotFallbackHands = true,
                            Fields = new[]
                            {
                                F("senses.why|title", "Title (list row / replay heading)", 28),
                                F("senses.why|says", "Says", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "senses.words", Kind = TPresentation.Strip, DemoId = "baresenses", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("senses.words|title", "Title (list row / replay heading)", 28),
                                F("senses.words|says", "Says", 125),
                            },
                        },
                        // The lesson's last word is the power reminder (tour review, 2026-09-26): the setup's
                        // Then said "when this lesson ends, put it back" - this is that moment. The Then
                        // line plays after the read, and a Then in flight is never cut by S4, so putting
                        // the battery back while it shows still finishes the lesson Done.
                        new TStep
                        {
                            Id = "senses.numbers", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_Menu}", Done = TDone.Read, Skip = TSkip.S2,
                            Fields = new[]
                            {
                                F("senses.numbers|title", "Title (list row / replay heading)", 28),
                                F("senses.numbers|says", "Says", 125),
                                F("senses.numbers|then", "Then (reminder: put the suit battery back)", 100),
                            },
                        },
                    },
                },
                // ---- 16 menu - THE MENU
                new TLesson
                {
                    Id = "menu", Number = 16, Priority = 0, TitleKey = "lesson:menu|title",
                    Trigger = TTrigger.Menu, NeedsCore = false, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 16,
                    Steps = new[]
                    {
                        // A strip before the callouts: the F10 tour pages Steps[1..] (skip IsSetup).
                        new TStep
                        {
                            Id = "menu.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_Menu}", Done = TDone.MenuOpen, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("menu.setup|title", "Title (list row / replay heading)", 28),
                                F("menu.setup|says", "Says (setup: the menu is shut)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "menu.density", Kind = TPresentation.Callout, DemoId = null, Done = TDone.Next, Skip = TSkip.None, Anchor = "search",
                            Fields = new[]
                            {
                                F("menu.density|title", "Title (list row / replay heading)", 28),
                                F("menu.density|callout", "Callout text", 160),
                            },
                        },
                        new TStep
                        {
                            Id = "menu.halves", Kind = TPresentation.Callout, DemoId = null, Done = TDone.Next, Skip = TSkip.None, Anchor = "master",
                            Fields = new[]
                            {
                                F("menu.halves|title", "Title (list row / replay heading)", 28),
                                F("menu.halves|callout", "Callout text", 160),
                            },
                        },
                        new TStep
                        {
                            Id = "menu.themes", Kind = TPresentation.Callout, DemoId = "themeswitch", Done = TDone.Next, Skip = TSkip.None, Anchor = "tab:UI Themes",
                            Fields = new[]
                            {
                                F("menu.themes|title", "Title (list row / replay heading)", 28),
                                F("menu.themes|callout", "Callout text", 160),
                            },
                        },
                        new TStep
                        {
                            Id = "menu.settings", Kind = TPresentation.Callout, DemoId = null, Done = TDone.Next, Skip = TSkip.None, Anchor = "tab:SmartStow",
                            Fields = new[]
                            {
                                F("menu.settings|title", "Title (list row / replay heading)", 28),
                                F("menu.settings|callout", "Callout text", 160),
                            },
                        },
                        new TStep
                        {
                            Id = "menu.controls", Kind = TPresentation.Callout, DemoId = null, Done = TDone.Next, Skip = TSkip.None, Anchor = "tab:Controls",
                            Fields = new[]
                            {
                                F("menu.controls|title", "Title (list row / replay heading)", 28),
                                F("menu.controls|callout", "Callout text", 160),
                            },
                        },
                        new TStep
                        {
                            Id = "menu.guide", Kind = TPresentation.Callout, DemoId = null, Done = TDone.Next, Skip = TSkip.None, Anchor = "tab:Guide",
                            Fields = new[]
                            {
                                F("menu.guide|title", "Title (list row / replay heading)", 28),
                                F("menu.guide|callout", "Callout text", 160),
                            },
                        },
                    },
                },
                // ---- 17 designer - THE HUD DESIGNER
                new TLesson
                {
                    Id = "designer", Number = 17, Priority = 0, TitleKey = "lesson:designer|title",
                    Trigger = TTrigger.Designer, NeedsCore = false, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 17,
                    Steps = new[]
                    {
                        // The F9 intercept and PlayLesson open designer.card, not Steps[0].
                        new TStep
                        {
                            Id = "designer.setup", Kind = TPresentation.Strip, DemoId = "keys:tap:{UIA_HudDesigner}", Done = TDone.DesignerKey, Skip = TSkip.S1, IsSetup = true,
                            Fields = new[]
                            {
                                F("designer.setup|title", "Title (list row / replay heading)", 28),
                                F("designer.setup|says", "Says (setup: before the F9 press)", 125),
                            },
                        },
                        new TStep
                        {
                            Id = "designer.card", Kind = TPresentation.Card, DemoId = "designer", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("designer.card|title", "Title (list row / replay heading)", 28),
                                F("designer.card|heading", "Heading", 28),
                                F("designer.card|body", "Body", 280),
                                F("designer.card|button@0", "Button 1 (primary)", 24),
                                F("designer.card|button@1", "Button 2", 24),
                                F("designer.card|button@2", "Button 3", 24),
                            },
                        },
                    },
                },
                // ---- 18 whatsnew - WHAT'S NEW
                new TLesson
                {
                    Id = "whatsnew", Number = 18, Priority = 0, TitleKey = "lesson:whatsnew|title",
                    Trigger = TTrigger.WhatsNew, NeedsCore = false, Skill = TSkill.None, ContextBound = false,
                    TourOrder = 0,   // not in the tour (the tour covers all of it); updaters only, replayable
                    Steps = new[]
                    {
                        new TStep
                        {
                            Id = "whatsnew.card", Kind = TPresentation.Card, DemoId = "whatsnew", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("whatsnew.card|title", "Title (list row / replay heading)", 28),
                                F("whatsnew.card|heading", "Heading", 28),
                                F("whatsnew.card|body", "Body", 280),
                                F("whatsnew.card|says@mp", "Invitation strip (multiplayer)", 125),
                                F("whatsnew.card|button@0", "Button 1 (primary)", 24),
                                F("whatsnew.card|button@1", "Button 2", 24),
                            },
                        },
                        new TStep
                        {
                            Id = "whatsnew.word", Kind = TPresentation.Card, DemoId = "actionword", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("whatsnew.word|title", "Title (list row / replay heading)", 28),
                                F("whatsnew.word|heading", "Heading", 28),
                                F("whatsnew.word|body", "Body", 280),
                            },
                        },
                        new TStep
                        {
                            Id = "whatsnew.drop", Kind = TPresentation.Card, DemoId = "dragpark", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("whatsnew.drop|title", "Title (list row / replay heading)", 28),
                                F("whatsnew.drop|heading", "Heading", 28),
                                F("whatsnew.drop|body", "Body", 280),
                            },
                        },
                        new TStep
                        {
                            Id = "whatsnew.belts", Kind = TPresentation.Card, DemoId = "beltswap", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("whatsnew.belts|title", "Title (list row / replay heading)", 28),
                                F("whatsnew.belts|heading", "Heading", 28),
                                F("whatsnew.belts|body", "Body", 280),
                            },
                        },
                        new TStep
                        {
                            Id = "whatsnew.window", Kind = TPresentation.Card, DemoId = "shiftdrag", Done = TDone.Button, Skip = TSkip.S3,
                            Fields = new[]
                            {
                                F("whatsnew.window|title", "Title (list row / replay heading)", 28),
                                F("whatsnew.window|heading", "Heading", 28),
                                F("whatsnew.window|body", "Body", 280),
                            },
                        },
                    },
                },
            };
        }

        /// <summary>The seven "Teach me as I go" tips (Part C, lesson 1 table), one strip each.</summary>
        private static TStep[] BuildTips()
        {
            return new[]
            {
                new TStep { Id = "tip.1", Kind = TPresentation.Tip, DemoId = "keys:tap:{UIA_ToolbeltRadial}", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.1|says", "Tip strip", 125) } },
                new TStep { Id = "tip.2", Kind = TPresentation.Tip, DemoId = "actionword", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.2|says", "Tip strip", 125) } },
                new TStep { Id = "tip.3", Kind = TPresentation.Tip, DemoId = "keys:tap:{V:SmartStow}", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.3|says", "Tip strip", 125) } },
                new TStep { Id = "tip.4", Kind = TPresentation.Tip, DemoId = "hubback", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.4|says", "Tip strip", 125) } },
                new TStep { Id = "tip.5", Kind = TPresentation.Tip, DemoId = "pushout", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.5|says", "Tip strip", 125) } },
                new TStep { Id = "tip.6", Kind = TPresentation.Tip, DemoId = "keys:tap:{UIA_Grid}", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.6|says", "Tip strip", 125) } },
                new TStep { Id = "tip.7", Kind = TPresentation.Tip, DemoId = "keys:double:{V:MouseControl}", Done = TDone.Read, Skip = TSkip.S2,
                    Fields = new[] { F("tip.7|says", "Tip strip", 125) } },
            };
        }

        /// <summary>C.19 supporting strings (strip chrome, tour and watch buttons, the GUIDE Lessons
        /// section and its state chips), in export order. Budgets are the director's own choice.</summary>
        private static TField[] BuildChrome()
        {
            return new[]
            {
                F("chrome|selfskip", "Strip: self-skip line (S1)", 125),
                F("chrome|reopen", "Strip: the wheel closed ({OPENER} = the lesson's own opener key)", 125),
                F("chrome|onhold", "Strip: lesson on hold (emergency)", 125),
                F("chrome|waiting", "Strip: waiting chip", 125),
                F("chrome|lessonsoff", "Strip: lessons turned off", 125),
                F("chrome|overrun", "Strip: First Steps window left open", 125),
                F("chrome|touropener", "F10 tour opener callout", 160),
                F("chrome|tourtake", "F10 tour opener: take button", 24),
                F("chrome|tourno", "F10 tour opener: decline button", 24),
                F("chrome|back", "Button: back", 24),
                F("chrome|next", "Button: next", 24),
                F("chrome|done", "Button: done (last card / callout)", 24),
                F("chrome|skiptour", "Button: skip the F10 tour", 24),
                F("chrome|skiplesson", "Button: skip lesson (watch cards)", 24),
                F("chrome|trynow", "Button: try it now (last watch card)", 24),
                F("chrome|state.new", "Lesson state chip: new", 24),
                F("chrome|state.progress", "Lesson state chip: in progress", 24),
                F("chrome|state.done", "Lesson state chip: done", 24),
                F("chrome|state.skipped", "Lesson state chip: skipped", 24),
                // Tour mode (2026-09-26). The header's placeholders work like chrome|reopen's {OPENER}:
                // the director swaps them BEFORE TutorialTokens.Resolve. Budget = the header as shown,
                // measured with {N} = {TOTAL} = "17" and a 28-character {TITLE}.
                F("chrome|tourheader", "First-run tour: strip header ({N} = lesson number, {TOTAL} = lessons in the tour, {TITLE} = lesson title)", 48),
                F("chrome|tournext", "First-run tour: button - on to the next lesson (First Steps' last card)", 24),
                F("chrome|tourstop", "First-run tour: button - stop the tour, the rest come as you play", 24),
            };
        }
    }
}
