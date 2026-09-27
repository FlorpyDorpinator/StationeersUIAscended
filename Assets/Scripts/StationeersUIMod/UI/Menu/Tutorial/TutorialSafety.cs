using System;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// "Is the player safe enough to be taught something right now?" (plan A.5 / D.2 row 5). The
    /// director asks before a lesson starts and while one runs: not calm on Start -> the "Hold on"
    /// card; not calm for 2 s mid-lesson -> "Lesson on hold" (with the reason - <see cref="HoldReason"/>).
    ///
    /// <para><b>Calm</b> = a valid HUD snapshot, no ACUTE <see cref="SenseSev.Critical"/> felt-sense band
    /// tripped - evaluated exactly like <c>BareSensesWidget.EvalSense</c> (catalog thresholds, the
    /// FeltValid / SanitationValid MP guards, the robot suppression), never a client-side guess - and
    /// no FRESH chronic suit problem (point 3).</para>
    ///
    /// <para>Three deliberate narrowings of the plan's wording (reported in the wave's Changes Reports):
    /// (1) ACUTE senses only - temperature, air, pressure, health, consciousness, stun and g-force.
    /// The chronic Critical bands (PARCHED, STARVING, DESPERATE, MISERABLE) are slow burns; a player
    /// at 30% food would otherwise see "Lesson on hold - deal with the emergency first" with no
    /// emergency in sight, and a real decline still trips DYING. (2) In the BARE tier the suit status
    /// arm is ignored: a Bare suit is unpowered by definition, so its "battery critical" arm is always
    /// tripped - lesson 15 (no suit power) could never fire. The body's own senses are the truth there.
    /// (3) The suit's worst-of status CRITICAL (<c>HudSnapshot.SuitStatusLevel &gt;= 3</c>, HudSampler
    /// "suit status word": no air tank, a depleted filter still fitted, a battery critical-not-empty -
    /// the robot's own battery) is CHRONIC, not an emergency: many players walk around like that for
    /// hours, and the real danger it leads to (choking, vacuum, freezing) trips the acute senses
    /// anyway. It holds lessons only while it is NEW - <see cref="ChronicGraceSeconds"/> from the moment
    /// that problem appeared, so the player notices it and learns why - and then counts as the player's
    /// settled state. The Hold-on card's "Start anyway" (<see cref="AcceptChronic"/>) switches it off
    /// for the rest of the game session. The acute gate is never relaxed.</para>
    ///
    /// <para>The snapshot is <see cref="HudSystem.LastSnapshot"/>; with the Visor HUD half switched
    /// off (no snapshot is sampled) we sample one ourselves through <see cref="HudSampler.Sample"/> -
    /// the same client-safe reads, into the sampler's own reused object, at most once per frame. The
    /// chronic reason re-reads the same suit fields the sampler's status word does (read-only:
    /// <c>Human.Suit</c> Human.cs:728, <c>ISuit.AsThing / Battery / AirTank / HasFilters / EmptyFilter</c>
    /// ISuit.cs:32-54, <c>BatteryCell.IsCritical</c> BatteryCell.cs:110, <c>Human.RobotBattery</c>
    /// Human.cs:738 - decompile V27798), only while that status reads CRITICAL. Read-only; the result is
    /// cached per frame. Nothing here holds a Unity object beyond the frame's snapshot.</para>
    /// </summary>
    internal static class TutorialSafety
    {
        /// <summary>A new chronic suit problem holds lessons this long, then it is the player's settled
        /// state (the hold line resumes 5 s after it - TutorialDirector's ResumeAfterSeconds).</summary>
        internal const float ChronicGraceSeconds = 15f;
        // Not evaluated for this long (outside a world): a chronic problem found afterwards is new again.
        private const float ChronicGapSeconds = 5f;

        // Chronic arms (bit per suit problem) - a NEW bit restarts the grace clock.
        private const int ChronicBattery = 1, ChronicNoBattery = 2, ChronicTank = 4, ChronicFilter = 8, ChronicOther = 16;

        // The reasons (short ASCII; the hold line reads "Lesson on hold - suit: no air tank.").
        private const string WhyTank = "suit: no air tank";
        private const string WhyFilter = "suit: filter empty";
        private const string WhySuitNoBattery = "suit: no battery";
        private const string WhySuitBattery = "suit: battery almost flat";
        private const string WhyRobotNoBattery = "no battery";
        private const string WhyRobotBattery = "battery almost flat";
        private const string WhySuitCritical = "suit CRITICAL";
        private const string WhyEmergency = "EMERGENCY";   // an acute band with no word of its own

        private static int _calmFrame = -1;
        private static bool _calm;
        private static int _snapFrame = -1;
        private static HudSnapshot _snap;
        // The last evaluation: why it was not calm (a constant above or a catalog word - never built per
        // frame; null = calm, or no snapshot) and whether that is ACUTE (an emergency) or chronic.
        private static string _why;
        private static bool _whyAcute;
        // Chronic tracking: the CRITICAL arms seen since this spell began, when the newest one appeared,
        // when we last evaluated, and the player's "Start anyway" (this game session - the director
        // carries it across an F6 reload).
        private static int _chronicBits;
        private static float _chronicSince = -1f;
        private static float _lastEvalAt = -999f;
        private static bool _chronicAccepted;

        // Index-aligned with SenseCatalog.Senses (temp, air, pressure, thirst, hunger, health,
        // cognition, toilet, mood, clean, soiled, stun, gforce) - see the class remarks, point (1).
        private static bool IsAcute(string senseKey)
        {
            switch (senseKey)
            {
                case "temp":
                case "air":
                case "pressure":
                case "health":
                case "cognition":
                case "stun":
                case "gforce":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The snapshot the tutorial reads this frame: the HUD's own when it sampled one,
        /// else one we sample (HUD half off). Null outside a world. Cached per frame.</summary>
        internal static HudSnapshot Snapshot()
        {
            int f = Time.frameCount;
            if (_snapFrame == f) return _snap;
            _snapFrame = f;
            _snap = null;
            try
            {
                if (!Guards.CanDraw()) return null;
                var s = HudSystem.LastSnapshot;
                if (s == null) s = HudSampler.Sample();   // Visor HUD off: nobody sampled this frame
                _snap = s != null && s.Valid ? s : null;
            }
            catch { _snap = null; }
            return _snap;
        }

        /// <summary>True when a lesson may start or keep running (see the class remarks). Fail-safe:
        /// no snapshot (or any throw) reads as NOT calm.</summary>
        internal static bool IsCalm()
        {
            int f = Time.frameCount;
            if (_calmFrame == f) return _calm;
            _calmFrame = f;
            _calm = Evaluate();
            return _calm;
        }

        /// <summary>Why a lesson is held right now: null when calm (or when no snapshot can say - the
        /// caller shows its generic line), else a short ASCII reason - a felt-sense word for an
        /// emergency ("FREEZING", <paramref name="acute"/> true) or the suit's own problem ("suit: no
        /// air tank", false). A constant or a catalog string: no allocation.</summary>
        internal static string HoldReason(out bool acute)
        {
            bool calm = IsCalm();
            acute = !calm && _whyAcute;
            return calm ? null : _why;
        }

        /// <summary>The Hold-on card's "Start anyway": a chronic suit problem no longer holds any lesson
        /// for the rest of the game session (an emergency still does).</summary>
        internal static void AcceptChronic()
        {
            _chronicAccepted = true;
            _calmFrame = -1;   // re-evaluate this very frame
        }

        /// <summary>"Start anyway" was pressed this session (the director's F6 carry).</summary>
        internal static bool ChronicAccepted { get { return _chronicAccepted; } }

        private static bool Evaluate()
        {
            _why = null;
            _whyAcute = false;
            float now = Time.unscaledTime;
            try
            {
                var s = Snapshot();
                if (s == null) { _whyAcute = true; return false; }   // fail-safe: not calm, nothing to name

                // Chronic: the suit's own CRITICAL arms, tracked whatever else is going on (so its clock
                // is right when an emergency ends). Ignored in Bare (point 2).
                int bits = s.Tier != HudTier.Bare && s.SuitStatusLevel >= 3 ? ChronicBits(s) : 0;
                if (bits == 0 || now - _lastEvalAt > ChronicGapSeconds) { _chronicBits = 0; _chronicSince = -1f; }
                if (bits != 0)
                {
                    // A NEW arm (one not seen since this spell began) restarts the grace; an arm that
                    // flickers off and on again (a battery hovering at its band edge) does not.
                    if (_chronicSince < 0f || (bits & ~_chronicBits) != 0) _chronicSince = now;
                    _chronicBits |= bits;
                }

                string acute = AcuteWord(s);
                if (acute != null) { _why = acute; _whyAcute = true; return false; }
                if (bits != 0 && !_chronicAccepted && now - _chronicSince < ChronicGraceSeconds)
                {
                    _why = ChronicWhy(bits, s.IsRobot);
                    return false;
                }
                return true;
            }
            catch { _whyAcute = true; return false; }
            finally { _lastEvalAt = now; }
        }

        /// <summary>The first ACUTE Critical band tripped (its word - the robot's where it has one), or
        /// null. No allocation.</summary>
        private static string AcuteWord(HudSnapshot s)
        {
            var senses = SenseCatalog.Senses;
            bool robot = s.IsRobot;
            for (int i = 0; senses != null && i < senses.Length; i++)
            {
                var info = senses[i];
                if (info == null || !IsAcute(info.Key)) continue;
                if (info.NeedsAtmosphere && !s.FeltValid) continue;   // can't feel the warmth of nothing
                if (info.ServerOnly && !s.SanitationValid) continue;  // server-only sim (MP rule)
                if (robot && info.RobotSuppressed) continue;
                var bands = info.Bands;
                for (int b = 0; bands != null && b < bands.Length; b++)
                {
                    var band = bands[b];
                    if (band == null) continue;
                    if (robot && band.RobotWord == null) continue;
                    float val = SenseCatalog.Driver(s, band.Driver);
                    bool hit = band.Side == BandSide.High ? val >= band.Threshold : val <= band.Threshold;
                    if (!hit) continue;
                    // First match wins (bands are most-severe first): a Critical hit is a
                    // trouble sign, a milder match means this sense is not critical.
                    if (band.Sev == SenseSev.Critical)
                    {
                        string w = robot && band.RobotWord != null ? band.RobotWord : band.Word;
                        return string.IsNullOrEmpty(w) ? WhyEmergency : w;
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>Which of the sampler's CRITICAL arms hold (HudSampler "suit status word"): the same
        /// reads, only while that status reads CRITICAL. A status the arms here do not explain (a future
        /// sampler arm) still counts, as <see cref="ChronicOther"/>.</summary>
        private static int ChronicBits(HudSnapshot s)
        {
            int bits = 0;
            try
            {
                var h = s.Human;
                if (h != null)
                {
                    if (s.IsRobot)
                    {
                        var b = h.RobotBattery;
                        if (b == null) bits |= ChronicNoBattery;
                        else if (b.IsCritical) bits |= ChronicBattery;
                    }
                    else
                    {
                        var suit = h.Suit;
                        if (suit != null && suit.AsThing != null)
                        {
                            var b = suit.Battery;
                            if (b == null) bits |= ChronicNoBattery;
                            else if (b.IsCritical) bits |= ChronicBattery;
                            if (suit.AirTank == null) bits |= ChronicTank;
                            if (suit.HasFilters && suit.EmptyFilter) bits |= ChronicFilter;
                        }
                    }
                }
            }
            catch { bits = 0; }
            return bits != 0 ? bits : ChronicOther;
        }

        private static string ChronicWhy(int bits, bool robot)
        {
            if ((bits & ChronicTank) != 0) return WhyTank;
            if ((bits & ChronicFilter) != 0) return WhyFilter;
            if ((bits & ChronicNoBattery) != 0) return robot ? WhyRobotNoBattery : WhySuitNoBattery;
            if ((bits & ChronicBattery) != 0) return robot ? WhyRobotBattery : WhySuitBattery;
            return WhySuitCritical;
        }

        /// <summary>A short ASCII reason for logs / the editor ("suit: no air tank", "CHOKING"), or null
        /// when calm. The same verdict as <see cref="IsCalm"/> this frame.</summary>
        internal static string WhyNotCalm()
        {
            try
            {
                bool acute;
                string why = HoldReason(out acute);
                if (why != null) return why;
                return IsCalm() ? null : "no HUD snapshot (not in a world?)";
            }
            catch (Exception e) { return "check failed: " + e.Message; }
        }

        /// <summary>Hot-reload rule: drop the cached snapshot reference, the frame stamps, the chronic
        /// clock and the session's "Start anyway" (the director carries that one across an F6).</summary>
        internal static void Shutdown()
        {
            _calmFrame = -1;
            _calm = false;
            _snapFrame = -1;
            _snap = null;
            _why = null;
            _whyAcute = false;
            _chronicBits = 0;
            _chronicSince = -1f;
            _lastEvalAt = -999f;
            _chronicAccepted = false;
        }
    }
}
