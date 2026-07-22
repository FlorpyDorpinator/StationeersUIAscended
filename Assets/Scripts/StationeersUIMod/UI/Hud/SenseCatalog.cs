using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>The numeric value behind a sense. Read from the MP-safe fields on
    /// <see cref="HudSnapshot"/> (never from the live Human) — see <see cref="SenseCatalog.Driver"/>.</summary>
    internal enum SenseDriver
    {
        FeltTempC, OxygenQuality, BreathKPa, Toxins, WaterRatio, FoodRatio, Damage, Sanitation01,
        // 0.9.1 additional moodlet drivers (all read from MP-safe snapshot fields).
        HygieneRaw, Mood01, Stun01, GForce, Soiled
    }

    /// <summary>Which way the driver crosses the threshold to trigger a band. High = a large value
    /// is bad (heat, toxins, damage, toilet); Low = a small value is bad (cold, oxygen, water,
    /// food, pressure).</summary>
    internal enum BandSide { High, Low }

    /// <summary>Default severity of a band — drives the DEFAULT colour and whether the word is held
    /// bright after its flare (Critical never fades to a whisper). The author can override the
    /// colour per band, so this is only the starting point.</summary>
    internal enum SenseSev { Notice, Warn, Critical }

    /// <summary>One threshold band of a sense: the condition (driver, side, threshold), the default
    /// WORD and default severity. Thresholds carry unit + slider metadata so the F9 editor presents a
    /// sensible control. Author overrides (word / colour / threshold) live in the element's Params
    /// bag keyed <c>w_ / c_ / t_ + sense key + "_" + band id</c>; this catalog holds defaults only.</summary>
    internal sealed class SenseBand
    {
        public readonly string Id;
        public readonly string Word;
        public readonly SenseDriver Driver;
        public readonly BandSide Side;
        public readonly float Threshold;
        public readonly SenseSev Sev;
        public readonly float ThMin, ThMax;
        public readonly string Unit;
        /// <summary>Word shown when the wearer is a ROBOT. Null = this band does not apply to a robot
        /// at all (it is skipped), which reproduces the old sampler's reduced robot ladder.</summary>
        public readonly string RobotWord;
        /// <summary>Caption for the F9 editor. Defaults to <see cref="Word"/>; set it when two bands
        /// legitimately SHOW the same word from different drivers (CHOKING from no pressure vs no
        /// oxygen) so the author can tell the two trigger rows apart.</summary>
        public readonly string EditorLabel;
        /// <summary>Optional DEFAULT colour for this band as a palette-name-or-<c>#RRGGBBAA</c> ref.
        /// Null = fall back to the severity default (Critical -> red, else the element's word colour).
        /// Lets a positive/neutral moodlet (FRESH, WEIGHTLESS) carry its own hue without the author
        /// setting it. The author's per-band <c>c_</c> override still wins over this.</summary>
        public readonly string ColorRef;

        public SenseBand(string id, string word, SenseDriver drv, BandSide side, float th,
            SenseSev sev, float thMin, float thMax, string unit, string robotWord = null,
            string editorLabel = null, string colorRef = null)
        {
            Id = id; Word = word; Driver = drv; Side = side; Threshold = th; Sev = sev;
            ThMin = thMin; ThMax = thMax; Unit = unit; RobotWord = robotWord;
            EditorLabel = editorLabel ?? word; ColorRef = colorRef;
        }
    }

    /// <summary>A felt sense and its bands. <see cref="Bands"/> are ordered MOST-SEVERE FIRST so a
    /// simple first-match picks the right word even for bidirectional senses (temperature, pressure)
    /// whose cold/hot arms share one driver — a scalar only ever satisfies one arm, and among the
    /// bands it satisfies the most severe (listed first) wins.</summary>
    internal sealed class SenseInfo
    {
        public readonly string Key, Name;
        /// <summary>Requires a valid breathing/world atmosphere this frame (FeltValid) — else silent.
        /// You can't feel the warmth, oxygen or pressure of nothing.</summary>
        public readonly bool NeedsAtmosphere;
        /// <summary>Server-simulated only (bowel). Silent unless SanitationValid, so a pure MP client
        /// never guesses a value the server never sent (CLAUDE.md MP rule).</summary>
        public readonly bool ServerOnly;
        /// <summary>Silent entirely for a ROBOT wearer. The old sampler blanked every felt sense for
        /// robots except health (which used its own FAILING / DAMAGED words), so all senses default
        /// to suppressed and only health opts out.</summary>
        public readonly bool RobotSuppressed;
        public readonly SenseBand[] Bands;

        public SenseInfo(string key, string name, bool needsAtmo, bool serverOnly, SenseBand[] bands,
            bool robotSuppressed = true)
        {
            Key = key; Name = name; NeedsAtmosphere = needsAtmo; ServerOnly = serverOnly;
            Bands = bands; RobotSuppressed = robotSuppressed;
        }
    }

    /// <summary>Default sense/band table for the bare-tier felt senses. Bands and thresholds mirror
    /// the ladders that used to live inline in <c>HudSampler.Sample</c> exactly, so a fresh element
    /// with no overrides reads identically to the old widget. The author edits copies of these
    /// (word/colour/threshold) per element; the sampler now only publishes the raw driver values.</summary>
    internal static class SenseCatalog
    {
        // INDEX-ALIGNED with BareSensesWidget.SenseKeys: temp, air, pressure, thirst, hunger,
        // health, cognition, toilet, mood, clean, soiled, stun, gforce. Keep the two in lockstep
        // (SenseKeys + SlotOptions must gain a matching entry at the SAME index for every sense here).
        public static readonly SenseInfo[] Senses =
        {
            new SenseInfo("temp", "Temperature", true, false, new[]
            {
                new SenseBand("burning",  "BURNING",  SenseDriver.FeltTempC, BandSide.High,  80f, SenseSev.Critical, -60f, 120f, "C"),
                new SenseBand("freezing", "FREEZING", SenseDriver.FeltTempC, BandSide.Low,  -10f, SenseSev.Critical, -60f, 120f, "C"),
                new SenseBand("hot",      "HOT",      SenseDriver.FeltTempC, BandSide.High,  50f, SenseSev.Warn,     -60f, 120f, "C"),
                new SenseBand("cold",     "COLD",     SenseDriver.FeltTempC, BandSide.Low,    0f, SenseSev.Warn,     -60f, 120f, "C"),
                new SenseBand("warm",     "WARM",     SenseDriver.FeltTempC, BandSide.High,  32f, SenseSev.Notice,   -60f, 120f, "C"),
                new SenseBand("chilly",   "CHILLY",   SenseDriver.FeltTempC, BandSide.Low,   10f, SenseSev.Notice,   -60f, 120f, "C"),
            }),
            // Air + pressure do NOT require a valid atmosphere: in a true void breathKPa is 0 and
            // OxygenQuality (blood O2) still falls, so VACUUM / CHOKING must fire (HudSampler's old
            // rule — "0 kPa, the VACUUM/CHOKING words fire in a true void"). Only temperature is
            // silent without a felt atmosphere.
            new SenseInfo("air", "Air", false, false, new[]
            {
                // CHOKING has TWO arms, exactly like the old `breathKPa < 6.3 || o2Quality < 0.5`
                // condition: nothing to breathe, OR nothing worth breathing. Modelling only the
                // oxygen arm delayed the word in a true void until blood O2 had decayed (2026-07-19
                // review). Both arms show the same WORD; only the editor captions differ.
                new SenseBand("chokingp", "CHOKING",   SenseDriver.BreathKPa,     BandSide.Low,  6.3f, SenseSev.Critical, 0f, 1200f, "kPa", null, "CHOKING (no pressure)"),
                new SenseBand("choking",  "CHOKING",   SenseDriver.OxygenQuality, BandSide.Low,  0.5f, SenseSev.Critical, 0f, 1f,    "O2",  null, "CHOKING (no oxygen)"),
                new SenseBand("toxic",    "TOXIC AIR", SenseDriver.Toxins,        BandSide.High, 0.5f, SenseSev.Warn,     0f, 5f,    "kPa"),
                new SenseBand("thin",     "THIN AIR",  SenseDriver.OxygenQuality, BandSide.Low,  0.9f, SenseSev.Notice,   0f, 1f,    "O2"),
            }),
            new SenseInfo("pressure", "Pressure", false, false, new[]
            {
                new SenseBand("vacuum",   "VACUUM",       SenseDriver.BreathKPa, BandSide.Low,    6.3f,   SenseSev.Critical, 0f, 1200f, "kPa"),
                new SenseBand("crushing", "CRUSHING",     SenseDriver.BreathKPa, BandSide.High, 607.95f,  SenseSev.Critical, 0f, 1200f, "kPa"),
                new SenseBand("lowp",     "LOW PRESSURE", SenseDriver.BreathKPa, BandSide.Low,   20f,     SenseSev.Warn,     0f, 1200f, "kPa"),
            }),
            new SenseInfo("thirst", "Thirst", false, false, new[]
            {
                new SenseBand("parched", "PARCHED", SenseDriver.WaterRatio, BandSide.Low, 0.33f, SenseSev.Critical, 0f, 1f, "frac"),
                new SenseBand("thirsty", "THIRSTY", SenseDriver.WaterRatio, BandSide.Low, 0.66f, SenseSev.Warn,     0f, 1f, "frac"),
            }),
            new SenseInfo("hunger", "Hunger", false, false, new[]
            {
                new SenseBand("starving", "STARVING", SenseDriver.FoodRatio, BandSide.Low, 0.33f, SenseSev.Critical, 0f, 1f, "frac"),
                new SenseBand("hungry",   "HUNGRY",   SenseDriver.FoodRatio, BandSide.Low, 0.66f, SenseSev.Warn,     0f, 1f, "frac"),
            }),
            // Health is the ONE sense a robot still feels, and it speaks machine: FAILING / DAMAGED
            // at the same two thresholds the old robot ladder used. UNWELL has no robot word, so it
            // is skipped for a robot (the old robot ladder had no third band).
            new SenseInfo("health", "Health", false, false, new[]
            {
                new SenseBand("dying",  "DYING",  SenseDriver.Damage, BandSide.High, 0.75f, SenseSev.Critical, 0f, 1f, "frac", "FAILING"),
                new SenseBand("ailing", "AILING", SenseDriver.Damage, BandSide.High, 0.25f, SenseSev.Warn,     0f, 1f, "frac", "DAMAGED"),
                new SenseBand("unwell", "UNWELL", SenseDriver.Damage, BandSide.High, 0.05f, SenseSev.Notice,   0f, 1f, "frac"),
            }, robotSuppressed: false),
            new SenseInfo("cognition", "Consciousness", false, false, new[]
            {
                new SenseBand("fading", "FADING", SenseDriver.OxygenQuality, BandSide.Low, 0.5f, SenseSev.Critical, 0f, 1f, "O2"),
                new SenseBand("dazed",  "DAZED",  SenseDriver.OxygenQuality, BandSide.Low, 0.9f, SenseSev.Warn,     0f, 1f, "O2"),
            }),
            new SenseInfo("toilet", "Toilet", false, true, new[]
            {
                new SenseBand("desperate", "DESPERATE",  SenseDriver.Sanitation01, BandSide.High, 0.75f, SenseSev.Critical, 0f, 1f, "frac"),
                new SenseBand("needtogo",  "NEED TO GO", SenseDriver.Sanitation01, BandSide.High, 0.45f, SenseSev.Warn,     0f, 1f, "frac"),
            }),

            // ---- 0.9.1 additional bare-relevant moodlets (auto-surface #8). Every driver is
            // client-safe: Mood + Hygiene are networked; Soiled + G-force are computed locally on
            // each client; Stun is best-effort off DamageState (like the health sense's Damage).
            // Suit-only moodlets (air tank, filter, coolant, waste, power, leak, internals, jetpack)
            // are deliberately NOT here — they can never fire on a bare (suitless) wearer. ----
            new SenseInfo("mood", "Mood", false, false, new[]
            {
                new SenseBand("miserable", "MISERABLE", SenseDriver.Mood01, BandSide.Low, 0.15f, SenseSev.Critical, 0f, 1f, "frac"),
                new SenseBand("uneasy",    "UNEASY",    SenseDriver.Mood01, BandSide.Low, 0.5f,  SenseSev.Warn,     0f, 1f, "frac", colorRef: "#C89050"),
            }),
            // "Clean" is a POSITIVE sense: a word appears when hygiene is HIGH (just showered), with a
            // dirty warning at the low end. Hygiene tops out at 1.5 (= 150%, the PRISTINE band), read
            // from the UNCLAMPED HygieneRaw driver so the 1.0..1.5 range is reachable. A sound fires on
            // the 150% edge (see BareSensesWidget.TickCleanSound). Client-safe (networked hygiene).
            new SenseInfo("clean", "Clean", false, false, new[]
            {
                new SenseBand("pristine", "PRISTINE", SenseDriver.HygieneRaw, BandSide.High, 1.5f,  SenseSev.Notice, 0f, 1.5f, "frac", colorRef: "#7CE0C0"),
                new SenseBand("fresh",    "FRESH",    SenseDriver.HygieneRaw, BandSide.High, 1.0f,  SenseSev.Notice, 0f, 1.5f, "frac", colorRef: "#7CE0C0"),
                new SenseBand("grimy",    "GRIMY",    SenseDriver.HygieneRaw, BandSide.Low,  0.25f, SenseSev.Warn,   0f, 1.5f, "frac", colorRef: "#A89050"),
            }),
            new SenseInfo("soiled", "Soiled", false, false, new[]
            {
                new SenseBand("soiled", "SOILED", SenseDriver.Soiled, BandSide.High, 0.5f, SenseSev.Warn, 0f, 1f, "on/off", colorRef: "#9C7A48"),
            }),
            new SenseInfo("stun", "Stun", false, false, new[]
            {
                new SenseBand("stunned", "STUNNED", SenseDriver.Stun01, BandSide.High, 0.75f, SenseSev.Critical, 0f, 1f, "frac"),
                new SenseBand("reeling", "REELING", SenseDriver.Stun01, BandSide.High, 0.05f, SenseSev.Warn,     0f, 1f, "frac", colorRef: "#B080E0"),
            }),
            // G-force is bidirectional: high = crushing acceleration, low = weightless. GForce ~1 at 1g.
            new SenseInfo("gforce", "G-Force", false, false, new[]
            {
                new SenseBand("crushing", "CRUSHING G", SenseDriver.GForce, BandSide.High, 4f,    SenseSev.Critical, 0f, 6f, "g"),
                new SenseBand("heavy",    "HEAVY G",    SenseDriver.GForce, BandSide.High, 1.5f,  SenseSev.Warn,     0f, 6f, "g", colorRef: "#E08850"),
                new SenseBand("zerog",    "WEIGHTLESS", SenseDriver.GForce, BandSide.Low,  0.01f, SenseSev.Notice,   0f, 6f, "g", colorRef: "#88C0FF"),
            }),
        };

        public static float Driver(HudSnapshot s, SenseDriver d)
        {
            if (s == null) return 0f;
            switch (d)
            {
                case SenseDriver.FeltTempC: return s.FeltTempC;
                case SenseDriver.OxygenQuality: return s.O2Quality;
                case SenseDriver.BreathKPa: return s.BreathKPa;
                case SenseDriver.Toxins: return s.Toxins;
                case SenseDriver.WaterRatio: return s.WaterRatio;
                case SenseDriver.FoodRatio: return s.FoodRatio;
                case SenseDriver.Damage: return s.Damage;
                case SenseDriver.Sanitation01: return s.Sanitation01;
                case SenseDriver.HygieneRaw: return s.HygieneRaw;
                case SenseDriver.Mood01: return s.Mood01;
                case SenseDriver.Stun01: return s.Stun01;
                case SenseDriver.GForce: return s.GForce;
                case SenseDriver.Soiled: return s.Soiled ? 1f : 0f;
                default: return 0f;
            }
        }

        /// <summary>Default colour for a severity. Critical → the critical palette (red); Warn and
        /// Notice → the pale bare-word tint, so a fresh element matches the old two-colour look until
        /// the author sets per-band colours. Both are palette entries, so they track the F9 palette.</summary>
        public static Color DefaultColor(SenseSev sev)
            => sev == SenseSev.Critical ? HudPalette.Critical.Value : HudPalette.BareWord.Value;
    }
}
