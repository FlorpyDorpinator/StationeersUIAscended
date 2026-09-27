using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    // ---------- XML models ----------
    // Public by necessity (XmlSerializer must see the types). Every value is an attribute and every
    // enum travels as its NAME string, so an unknown or hand-mangled value degrades to a default
    // instead of throwing the whole file away.

    [XmlRoot("TutorialProgress")]
    public class TutorialProgressFile
    {
        [XmlAttribute("version")] public int Version = 1;
        [XmlAttribute("script")] public int ScriptVersion;
        // Tour mode (2026-09-26): the first-run tour's state and where it stands, so a player who quits
        // mid-tour continues at the same lesson and step. Missing in older files: every one reads its
        // default (no tour, nothing to resume) - see TutorialProgressStore.Index.
        [XmlAttribute("tour")] public string Tour;              // TTourState name; null / unknown = None
        [XmlAttribute("tourLesson")] public string TourLesson;  // the lesson id the tour stands at, null = none
        [XmlAttribute("tourStep")] public int TourStep;         // the step index within it
        [XmlAttribute("tourTrack")] public int TourTrack;       // lesson 3's track (0 tools, 1 tablet)
        [XmlElement("Lesson")] public List<TutorialProgressLesson> Lessons = new List<TutorialProgressLesson>();
        [XmlElement("Counter")] public List<TutorialProgressCounter> Counters = new List<TutorialProgressCounter>();
        [XmlElement("Flag")] public List<TutorialProgressFlag> Flags = new List<TutorialProgressFlag>();
    }

    public class TutorialProgressLesson
    {
        [XmlAttribute("id")] public string Id;
        [XmlAttribute("state")] public string State;
        [XmlAttribute("step")] public int Step;
        [XmlAttribute("track")] public int Track;
        [XmlAttribute("offers")] public int Offers;
        [XmlAttribute("lastOfferedUtc")] public long LastOfferedTicks;
    }

    public class TutorialProgressCounter
    {
        [XmlAttribute("id")] public string Id;
        [XmlAttribute("value")] public int Value;
    }

    public class TutorialProgressFlag
    {
        [XmlAttribute("id")] public string Id;
    }

    /// <summary>The first-run tour (tour mode, 2026-09-26): None = never taken (a new player before the
    /// Welcome, "Teach me as I go", "No lessons", an existing file from before the tour); Running =
    /// started and not finished - playing now, or paused (quit mid-tour, Welcome back "Later"): it
    /// continues at <see cref="TutorialProgressStore.TourLessonId"/>; Ended = played through, or
    /// stopped ("Stop the tour" / "Stop all lessons") - from then on only lessons it did not finish
    /// come back just-in-time, once each.</summary>
    internal enum TTourState : byte { None, Running, Ended }

    /// <summary>
    /// Lesson progress, PER INSTALL (plan A.12): <c>BepInEx/config/StationeersUIMod/Tutorial/Progress.xml</c>.
    /// You learn the mod once, so this deliberately is not per save (contrast HintUsageStore).
    ///
    /// <para>Holds, per lesson: state (<see cref="TLessonState"/>), the step a lesson was on, lesson 3's
    /// track, offer count and last offer time (UTC); counters (belt-wheel taps, G presses, setting
    /// clicks...); and flags ("asigo", fired tips, S1-skipped core steps, the Welcome's Later count).</para>
    ///
    /// <para><b>Seeding.</b> When the file does not exist: <c>GuideShown == true</c> means an UPDATER
    /// from 0.9.7.x. Tour mode (FlorpyDorp, 2026-09-26: "updaters get the full tour too"): nothing is
    /// seeded Done any more - the Welcome stays New, so the director shows the updater the SAME Welcome
    /// card a new player gets (Start the tour / Teach me as I go / No lessons) instead of What's new;
    /// What's new stays New (the tour covers it and marks it Done; an "as I go" updater still gets it
    /// once, on a later world entry). Otherwise a fresh install: What's new is marked Learned.
    /// <see cref="Existed"/> / <see cref="SeededUpdater"/> tell the director which.</para>
    ///
    /// <para><b>The first-run tour</b> (<see cref="TourState"/>, <see cref="TourLessonId"/>,
    /// <see cref="TourStep"/>, <see cref="TourTrack"/>) lives in four root attributes. An older file
    /// has none of them and reads "no tour" - nothing to resume, just-in-time lessons as before.</para>
    ///
    /// <para><b>Writes are batched</b>: <see cref="MarkDirty"/> then <see cref="Flush"/> on lesson end
    /// and world unload (the director calls it), plus a 2 min debounce in <see cref="Tick"/> as a
    /// safety net for the counters. Load is tolerant: a missing file seeds, a corrupt one is logged once and replaced by
    /// a seeded default (and kept aside as Progress.xml.bad). Hot reload: <see cref="Shutdown"/>
    /// flushes, then clears every static.</para>
    ///
    /// <para><b>uiareset</b>: <see cref="SuppressWritesUntilRestart"/> drops the in-memory state and
    /// makes the store read-only (empty answers, no disk IO) for the rest of the game session - an F6
    /// reload included - so no later flush can re-create the file the reset just deleted.</para>
    /// </summary>
    internal static class TutorialProgressStore
    {
        private const float DebounceSeconds = 120f;   // counters tick on every wheel open: write rarely
        // Survives an F6 reload (the AppDomain outlives the reloaded assembly); a game restart clears it.
        private const string NoWritesKey = "StationeersUIMod.Tutorial.ProgressNoWrites";

        private static TutorialProgressFile _file;
        private static readonly Dictionary<string, TutorialProgressLesson> _lessons =
            new Dictionary<string, TutorialProgressLesson>(StringComparer.Ordinal);
        // Parsed states: the director asks several times a second, so the enum is parsed once (at
        // load / on write), never per query - no per-poll allocation.
        private static readonly Dictionary<string, TLessonState> _states =
            new Dictionary<string, TLessonState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TutorialProgressCounter> _counters =
            new Dictionary<string, TutorialProgressCounter>(StringComparer.Ordinal);
        private static readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);
        // The tour's state, parsed once (at load / on write) like the lesson states - never per query.
        private static TTourState _tour;
        private static bool _loaded;
        private static bool _dirty;
        private static float _dirtySince = -1f;
        private static bool _suppressed;

        /// <summary>True after <see cref="SuppressWritesUntilRestart"/> (this session, or before an F6
        /// reload in it): the store answers from an empty state and never touches the disk. The director
        /// stands down on it. Cheap (Load is a bool check once it ran).</summary>
        internal static bool WritesSuppressed
        {
            get { Load(); return _suppressed; }
        }

        /// <summary><c>uiareset confirm</c>, called just BEFORE the config tree is deleted (a restart
        /// follows): drop every in-memory state and make every later Load / Flush / Tick / Clear a no-op
        /// for the rest of the game session. Otherwise the next flush - world exit, the 2 min debounce,
        /// quitting, an F6 teardown - would re-create Progress.xml and silently undo the reset. The mark
        /// is also parked on the AppDomain, so an F6 reload in the same session stays read-only.</summary>
        internal static void SuppressWritesUntilRestart()
        {
            _suppressed = true;
            try { AppDomain.CurrentDomain.SetData(NoWritesKey, "1"); } catch { }
            DropState();
            _loaded = true;   // reads answer New / 0 / false; nothing re-reads or re-seeds the deleted file
        }

        private static void DropState()
        {
            _file = null;
            _lessons.Clear();
            _states.Clear();
            _counters.Clear();
            _flags.Clear();
            _tour = TTourState.None;
            _dirty = false;
            _dirtySince = -1f;
            Existed = false;
            SeededUpdater = false;
            StoredScriptVersion = 0;
        }

        /// <summary>True when Progress.xml existed (and parsed) at load.</summary>
        internal static bool Existed { get; private set; }
        /// <summary>True when this load seeded an updater (GuideShown set, no progress file).</summary>
        internal static bool SeededUpdater { get; private set; }
        /// <summary>The ScriptVersion stored in the file (0 = none / fresh).</summary>
        internal static int StoredScriptVersion { get; private set; }

        internal static string FilePath { get { return Path.Combine(TutorialTextStore.TutorialDir, "Progress.xml"); } }

        // ================================================================ load / save

        /// <summary>Lazy, idempotent, tolerant. Call before any read; every accessor calls it.</summary>
        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            DropState();
            // uiareset earlier this session (before an F6 reload): stay read-only and empty.
            if (!_suppressed)
            {
                try { _suppressed = AppDomain.CurrentDomain.GetData(NoWritesKey) != null; }
                catch { _suppressed = false; }
            }
            if (_suppressed) return;

            string path = FilePath, tmp = path + ".tmp";
            // Flush writes Progress.xml.tmp in full, THEN deletes Progress.xml and moves the temp in. A
            // crash between those two steps leaves only the (complete) temp copy: read it rather than
            // re-seed - with GuideShown set, a re-seed would take an existing player for an updater
            // and forget every lesson.
            string src = null;
            try { src = File.Exists(path) ? path : (File.Exists(tmp) ? tmp : null); }
            catch { src = null; }
            try
            {
                if (src != null)
                {
                    var serializer = new XmlSerializer(typeof(TutorialProgressFile));
                    TutorialProgressFile file;
                    using (var stream = File.OpenRead(src))
                        file = serializer.Deserialize(stream) as TutorialProgressFile;
                    if (file != null)
                    {
                        _file = file;
                        Existed = true;
                        StoredScriptVersion = file.ScriptVersion;
                        Index(file);
                        if (!ReferenceEquals(src, path)) MarkDirty();   // recovered: the next flush restores Progress.xml
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Tutorial progress unreadable (" + e.Message + ") - starting over; the old file is kept as Progress.xml.bad.");
                try { if (src != null) File.Copy(src, path + ".bad", true); } catch { }
            }
            Seed();
        }

        private static void Index(TutorialProgressFile file)
        {
            if (file.Lessons != null)
                for (int i = 0; i < file.Lessons.Count; i++)
                {
                    var l = file.Lessons[i];
                    if (l == null || string.IsNullOrEmpty(l.Id)) continue;
                    _lessons[l.Id] = l;
                    _states[l.Id] = Parse(l.State);
                }
            if (file.Counters != null)
                for (int i = 0; i < file.Counters.Count; i++)
                {
                    var c = file.Counters[i];
                    if (c != null && !string.IsNullOrEmpty(c.Id)) _counters[c.Id] = c;
                }
            if (file.Flags != null)
                for (int i = 0; i < file.Flags.Count; i++)
                {
                    var f = file.Flags[i];
                    if (f != null && !string.IsNullOrEmpty(f.Id)) _flags.Add(f.Id);
                }
            // Tour mode: an older file has no tour attributes - no tour, nothing to resume. A mangled
            // value degrades the same way instead of throwing the file away.
            _tour = ParseTour(file.Tour);
        }

        /// <summary>Tolerant parse: an unknown or hand-mangled value reads None.</summary>
        private static TTourState ParseTour(string s)
        {
            if (string.IsNullOrEmpty(s)) return TTourState.None;
            TTourState v;
            return Enum.TryParse(s, true, out v) && Enum.IsDefined(typeof(TTourState), v) ? v : TTourState.None;
        }

        /// <summary>First load on this install (or after a restart/corruption): updater or fresh.</summary>
        private static void Seed()
        {
            _file = new TutorialProgressFile();
            bool guideShown = false;
            try { guideShown = UIAConfig.GuideShown != null && UIAConfig.GuideShown.Value; } catch { }
            if (guideShown)
            {
                // An updater from 0.9.7.x (they saw the old 15-card coach). Tour mode (FlorpyDorp,
                // 2026-09-26): they get the full tour too - the SAME Welcome card as a new player, not
                // the What's-new card. So nothing is seeded Done: the Welcome stays New (the director's
                // EntryNeeded reads "GuideShown, but the Welcome never answered" as due) and First Steps
                // is the tour's first lesson. What's new stays New: the tour covers it (starting the tour
                // marks it Done); "Teach me as I go" still gets it once, on a later world entry.
                SeededUpdater = true;
                SetStateRaw("whatsnew", TLessonState.New);
            }
            else
            {
                SetStateRaw("whatsnew", TLessonState.Learned);   // nothing is "new" to a new player
            }
            MarkDirty();
        }

        /// <summary>Write now if anything changed. Returns false on an IO failure (logged).</summary>
        internal static bool Flush()
        {
            if (_suppressed) { _dirty = false; _dirtySince = -1f; return true; }   // uiareset: never re-create the file
            if (!_loaded || !_dirty) return true;
            try
            {
                var file = _file ?? new TutorialProgressFile();
                file.Version = 1;
                file.ScriptVersion = TutorialChapters.ScriptVersion;
                file.Lessons = new List<TutorialProgressLesson>(_lessons.Values);
                file.Lessons.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                file.Counters = new List<TutorialProgressCounter>(_counters.Values);
                file.Counters.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                file.Flags = new List<TutorialProgressFlag>(_flags.Count);
                foreach (var f in _flags) file.Flags.Add(new TutorialProgressFlag { Id = f });
                file.Flags.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                Directory.CreateDirectory(TutorialTextStore.TutorialDir);
                var serializer = new XmlSerializer(typeof(TutorialProgressFile));
                // Write to a temp file then swap, so a crash mid-write can never leave half a file.
                string path = FilePath, tmp = path + ".tmp";
                using (var stream = File.Create(tmp))
                    serializer.Serialize(stream, file);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _file = file;
                _dirty = false;
                _dirtySince = -1f;
                Existed = true;
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("Tutorial progress could not be saved: " + e.Message);
                _dirtySince = Time.unscaledTime;   // retry after the next debounce window, not every frame
                return false;
            }
        }

        /// <summary>The debounce safety net (pumped by the director): a change older than 2 min is
        /// written even if no lesson ended.</summary>
        internal static void Tick()
        {
            if (_suppressed || !_dirty || _dirtySince < 0f) return;
            if (Time.unscaledTime - _dirtySince >= DebounceSeconds) Flush();
        }

        internal static void MarkDirty()
        {
            if (!_dirty) _dirtySince = Time.unscaledTime;
            _dirty = true;
        }

        /// <summary><c>uiatutorial restart</c>: forget everything (the file too) and re-seed as a FRESH
        /// install - the caller has already reset GuideShown/TutorialCompleted.</summary>
        internal static void Clear()
        {
            if (_suppressed)
            {
                // After uiareset the file is gone and must stay gone: just start over in memory.
                DropState();
                _loaded = true;
                return;
            }
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                // A leftover temp copy would otherwise be "recovered" by the Load below (see Load).
                if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp");
            }
            catch (Exception e) { UIALog.Warn("Tutorial progress could not be deleted: " + e.Message); }
            _loaded = false;
            _dirty = false;
            _dirtySince = -1f;
            Load();
        }

        /// <summary>Hot reload / teardown: write what changed (never after uiareset), then drop every
        /// static. The uiareset mark itself lives on in the AppDomain until the game restarts.</summary>
        internal static void Shutdown()
        {
            try { Flush(); } catch { }
            DropState();
            _loaded = false;
            _suppressed = false;
        }

        // ================================================================ lessons

        private static TutorialProgressLesson Lesson(string id, bool create)
        {
            Load();
            TutorialProgressLesson l;
            if (_lessons.TryGetValue(id, out l)) return l;
            if (!create) return null;
            l = new TutorialProgressLesson { Id = id, State = TLessonState.New.ToString() };
            _lessons[id] = l;
            return l;
        }

        /// <summary>Tolerant enum parse: an unknown or hand-mangled value reads New.</summary>
        private static TLessonState Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return TLessonState.New;
            TLessonState v;
            return Enum.TryParse(s, true, out v) && Enum.IsDefined(typeof(TLessonState), v) ? v : TLessonState.New;
        }

        /// <summary>A lesson's state (New when never recorded). Dictionary lookup, no allocation.</summary>
        internal static TLessonState GetState(string lessonId)
        {
            if (string.IsNullOrEmpty(lessonId)) return TLessonState.New;
            Load();
            TLessonState s;
            return _states.TryGetValue(lessonId, out s) ? s : TLessonState.New;
        }

        internal static void SetState(string lessonId, TLessonState state)
        {
            if (string.IsNullOrEmpty(lessonId)) return;
            Load();
            TLessonState cur;
            if (_states.TryGetValue(lessonId, out cur) && cur == state) return;
            SetStateRaw(lessonId, state);
            MarkDirty();
        }

        private static void SetStateRaw(string lessonId, TLessonState state)
        {
            var l = Lesson(lessonId, true);
            l.State = state.ToString();
            _states[lessonId] = state;
        }

        internal static int GetStep(string lessonId)
        {
            var l = string.IsNullOrEmpty(lessonId) ? null : Lesson(lessonId, false);
            return l != null ? Mathf.Max(0, l.Step) : 0;
        }

        internal static void SetStep(string lessonId, int step)
        {
            if (string.IsNullOrEmpty(lessonId)) return;
            var l = Lesson(lessonId, true);
            if (l.Step == step) return;
            l.Step = step;
            MarkDirty();
        }

        internal static int GetTrack(string lessonId)
        {
            var l = string.IsNullOrEmpty(lessonId) ? null : Lesson(lessonId, false);
            return l != null ? Mathf.Clamp(l.Track, 0, 1) : 0;
        }

        internal static void SetTrack(string lessonId, int track)
        {
            if (string.IsNullOrEmpty(lessonId)) return;
            var l = Lesson(lessonId, true);
            if (l.Track == track) return;
            l.Track = track;
            MarkDirty();
        }

        internal static int GetOffers(string lessonId)
        {
            var l = string.IsNullOrEmpty(lessonId) ? null : Lesson(lessonId, false);
            return l != null ? Mathf.Max(0, l.Offers) : 0;
        }

        /// <summary>A lesson started: one more offer, stamped now (UTC, so spacing survives restarts).</summary>
        internal static void NoteOffered(string lessonId)
        {
            if (string.IsNullOrEmpty(lessonId)) return;
            var l = Lesson(lessonId, true);
            l.Offers = Mathf.Max(0, l.Offers) + 1;
            l.LastOfferedTicks = DateTime.UtcNow.Ticks;
            MarkDirty();
        }

        /// <summary>Minutes since the lesson was last offered (a large number when never).</summary>
        internal static double MinutesSinceOffered(string lessonId)
        {
            var l = string.IsNullOrEmpty(lessonId) ? null : Lesson(lessonId, false);
            if (l == null || l.LastOfferedTicks <= 0) return 1e6;
            try { return (DateTime.UtcNow - new DateTime(l.LastOfferedTicks, DateTimeKind.Utc)).TotalMinutes; }
            catch { return 1e6; }
        }

        // ================================================================ the first-run tour (tour mode)

        /// <summary>The tour's state (None when never recorded - every file from before the tour).
        /// A field read, no allocation.</summary>
        internal static TTourState TourState
        {
            get { Load(); return _tour; }
        }

        /// <summary>The lesson the tour stands at (resume point), or null.</summary>
        internal static string TourLessonId
        {
            get { Load(); return _file != null && !string.IsNullOrEmpty(_file.TourLesson) ? _file.TourLesson : null; }
        }

        /// <summary>The step index within <see cref="TourLessonId"/> (0 = its first).</summary>
        internal static int TourStep
        {
            get { Load(); return _file != null ? Mathf.Max(0, _file.TourStep) : 0; }
        }

        /// <summary>Lesson 3's track at the resume point (0 tools, 1 tablet).</summary>
        internal static int TourTrack
        {
            get { Load(); return _file != null ? Mathf.Clamp(_file.TourTrack, 0, 1) : 0; }
        }

        internal static void SetTourState(TTourState state)
        {
            Load();
            if (_tour == state && _file != null) return;
            if (_file == null) _file = new TutorialProgressFile();
            _tour = state;
            _file.Tour = state == TTourState.None ? null : state.ToString();
            MarkDirty();
        }

        /// <summary>Where the tour stands: a lesson id (null = nowhere - the tour ended), its step, and
        /// lesson 3's track. Called on every step the tour enters; the write is batched (MarkDirty).</summary>
        internal static void SetTourAt(string lessonId, int step, int track)
        {
            Load();
            if (_file == null) _file = new TutorialProgressFile();
            if (string.IsNullOrEmpty(lessonId)) { lessonId = null; step = 0; track = 0; }
            step = Mathf.Max(0, step);
            track = Mathf.Clamp(track, 0, 1);
            if (string.Equals(_file.TourLesson, lessonId, StringComparison.Ordinal)
                && _file.TourStep == step && _file.TourTrack == track) return;
            _file.TourLesson = lessonId;
            _file.TourStep = step;
            _file.TourTrack = track;
            MarkDirty();
        }

        // ================================================================ counters / flags

        internal static int Counter(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            Load();
            TutorialProgressCounter c;
            return _counters.TryGetValue(id, out c) && c != null ? c.Value : 0;
        }

        /// <summary>Add to a counter (saturating) and return the new value.</summary>
        internal static int Bump(string id, int by = 1)
        {
            if (string.IsNullOrEmpty(id) || by == 0) return Counter(id);
            Load();
            TutorialProgressCounter c;
            if (!_counters.TryGetValue(id, out c) || c == null)
            {
                c = new TutorialProgressCounter { Id = id };
                _counters[id] = c;
            }
            long v = (long)c.Value + by;
            c.Value = v > int.MaxValue ? int.MaxValue : (v < 0 ? 0 : (int)v);
            MarkDirty();
            return c.Value;
        }

        internal static bool Flag(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            Load();
            return _flags.Contains(id);
        }

        internal static void SetFlag(string id, bool on)
        {
            if (string.IsNullOrEmpty(id)) return;
            Load();
            bool changed = on ? _flags.Add(id) : _flags.Remove(id);
            if (changed) MarkDirty();
        }
    }
}
