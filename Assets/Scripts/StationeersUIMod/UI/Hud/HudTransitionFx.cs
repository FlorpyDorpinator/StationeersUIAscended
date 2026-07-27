using System;
using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>How ONE power-transition effect resolves for ONE element.
    ///
    /// The root defect this replaces: each per-element flag was a plain bool defaulting TRUE, which
    /// conflated "inherit the global" with "explicitly on" — so "off" could never be expressed and
    /// the resolver ignored the stored value entirely for global-styled elements (FlorpyDorp,
    /// 2026-07-19: "I turned off death collapse and it still does it").</summary>
    internal enum HudFxMode
    {
        /// <summary>Use the global master + the global strength. The default for every element.</summary>
        Inherit = 0,
        /// <summary>Force ON, using the element's OWN strength (still gated by the global master).</summary>
        On = 1,
        /// <summary>Force OFF regardless of the global.</summary>
        Off = 2,
    }

    /// <summary>One row of the transition-effect registry: the param keys, the display label and
    /// lazy accessors for the GLOBAL master + global strength ConfigEntries.
    ///
    /// The ConfigEntry accessors are <see cref="Func{T}"/>s, not cached references, because
    /// HudConfig's entries are (re)bound at plugin start and after every F6 hot-reload — a cached
    /// ConfigEntry would point at the previous assembly's binding.</summary>
    internal sealed class HudTransitionFxDef
    {
        /// <summary>The effect's canonical key, e.g. "fxCollapse". Never itself stored.</summary>
        public readonly string Key;
        /// <summary>Where the element's tri-state lives in the param bag: "&lt;Key&gt;Mode" (int).</summary>
        public readonly string ModeKey;
        /// <summary>Where the element's own strength lives: "&lt;Key&gt;Amt" (float, 0..2).</summary>
        public readonly string AmtKey;
        /// <summary>The pre-tri-state bool this effect used to be stored as, read as a fallback
        /// (and mirrored on write) until every consumer has moved to the resolver. Equal to
        /// <see cref="Key"/> for effects that never had one.</summary>
        public readonly string LegacyKey;
        /// <summary>What an ABSENT legacy bool meant. True for the participation-style effects
        /// (collapse/glitch/warp/dissolve: on unless opted out), false for the opt-in accents
        /// (pulse). Migration and the legacy fallback both need it to avoid inventing motion.</summary>
        public readonly bool LegacyDefault;
        /// <summary>Label for the F9 rows — one string, so the per-element and global menus
        /// cannot drift apart.</summary>
        public readonly string Label;
        /// <summary>One-line explanation for the global menu.</summary>
        public readonly string Tip;

        private readonly Func<ConfigEntry<bool>> _master;
        private readonly Func<ConfigEntry<float>> _amount;
        private readonly bool _masterFallback;
        private readonly float _amountFallback;

        internal HudTransitionFxDef(string key, string legacyKey, bool legacyDefault,
            string label, string tip,
            Func<ConfigEntry<bool>> master, bool masterFallback,
            Func<ConfigEntry<float>> amount, float amountFallback)
        {
            Key = key;
            ModeKey = key + "Mode";
            AmtKey = key + "Amt";
            LegacyKey = string.IsNullOrEmpty(legacyKey) ? key : legacyKey;
            LegacyDefault = legacyDefault;
            Label = label;
            Tip = tip;
            _master = master;
            _masterFallback = masterFallback;
            _amount = amount;
            _amountFallback = amountFallback;
        }

        /// <summary>The bound global master switch, or null before Bind / after a failed bind.</summary>
        public ConfigEntry<bool> MasterEntry
        {
            get { try { return _master != null ? _master() : null; } catch { return null; } }
        }

        /// <summary>The bound global strength, or null before Bind / after a failed bind.</summary>
        public ConfigEntry<float> AmountEntry
        {
            get { try { return _amount != null ? _amount() : null; } catch { return null; } }
        }

        public bool GlobalOn
        {
            get { var e = MasterEntry; return e != null ? e.Value : _masterFallback; }
        }

        public float GlobalAmt
        {
            get { var e = AmountEntry; return Mathf.Clamp(e != null ? e.Value : _amountFallback, 0f, 2f); }
        }
    }

    /// <summary>
    /// THE one registry of HUD power-transition effects, so nothing can drift: the param keys, the
    /// labels, the global masters and the single shared resolver all live here. Per-element state is
    /// a TRI-STATE int in the element's param bag under "&lt;key&gt;Mode"
    /// (0 Inherit / 1 On / 2 Off), with the element's own strength at "&lt;key&gt;Amt".
    ///
    /// Resolution order (one rule, everywhere):
    ///   global master OFF  -> 0
    ///   element Off        -> 0
    ///   element On         -> the element's own amount (falling back to the global)
    ///   element Inherit    -> the global amount
    ///
    /// The table is immutable and holds no scene/game state, so it needs no hot-reload teardown:
    /// its lambdas read HudConfig's static fields at CALL time (never capture a ConfigEntry), and
    /// the whole type is replaced with the assembly on F6.
    /// </summary>
    internal static class HudTransitionFx
    {
        // NOTE on the dissolve key: "fxDissolve" was ALSO the name of an extinct legacy multiplier
        // that HudStyleMigration.LegacyFxKeys strips on every load. We never write the bare key —
        // only "fxDissolveMode"/"fxDissolveAmt" — so the strip cannot touch our state. The element's
        // legacy bool for dissolve is "customDissolve" (still read by HudElementView.DissolveFor).
        internal static readonly HudTransitionFxDef[] All =
        {
            new HudTransitionFxDef("fxCollapse", "fxCollapse", true,
                "Death collapse (CRT vertical squash)",
                "The old CRT death: the element widens slightly and squashes to a line as it dies.",
                () => HudConfig.FxCollapseOn, false, () => HudConfig.FxCollapseAmt, 1f),

            new HudTransitionFxDef("fxTvOff", "fxTvOff", true,
                "TV off (horizontal collapse to a dot)",
                "The classic CRT power-off: the picture squashes to a horizontal line, then pinches "
                + "to a dot and blinks out.",
                () => HudConfig.FxTvOffOn, false, () => HudConfig.FxTvOffAmt, 1f),

            new HudTransitionFxDef("fxDissolve", "customDissolve", true,
                "Dissolve frontier",
                "The travelling dissolve edge that reveals the element on boot (and eats it again on "
                + "power-down). Needs the Tier B shader bundle.",
                () => HudConfig.FxDissolveBoot, true, () => HudConfig.FxDissolveAmt, 1f),

            new HudTransitionFxDef("fxFlicker", "fxFlicker", true,
                "Flicker",
                "The staggered per-element flicker as the suit HUD strikes on / gutters out.",
                () => HudConfig.FlickerAnimations, true, () => HudConfig.FxFlickerAmt, 1f),

            new HudTransitionFxDef("fxGlitch", "fxGlitch", true,
                "Glitch tear",
                "The transform-jitter tear that runs across the HUD during a power transition.",
                () => HudConfig.FxGlitchOn, true, () => HudConfig.FxGlitchAmt, 1f),

            new HudTransitionFxDef("fxWarp", "fxWarp", true,
                "Warp / visor curve",
                "How much of the global visor curvature this element takes.",
                () => HudConfig.FxWarpOn, true, () => HudConfig.FxWarpAmt, 1f),

            // Pulse is the odd one out: it was always an OPT-IN accent (legacy default false), so its
            // global strength defaults to 0 — an inheriting element therefore breathes only once the
            // player raises the global slider, and nothing starts breathing on upgrade.
            new HudTransitionFxDef("fxPulse", "fxPulse", false,
                "Breathing pulse",
                "A slow brightness breath on the element (speed and depth are shared globals).",
                () => HudConfig.FxPulseOn, true, () => HudConfig.FxPulseAmt, 0f),
        };

        /// <summary>Registry lookup by canonical key. Returns null for an unknown key — callers
        /// fail soft rather than inventing an effect.</summary>
        internal static HudTransitionFxDef Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i] != null && string.Equals(All[i].Key, key, StringComparison.Ordinal)) return All[i];
            return null;
        }

        /// <summary>This element's effective tri-state for one effect.
        ///
        /// PHASE 3 GATE (per-category follow): when the element's <b>Transitions</b> category
        /// FOLLOWS the globals, every effect reads as <see cref="HudFxMode.Inherit"/> regardless of
        /// what is stored. That is what "follow" has to mean — and it is what makes FlorpyDorp's
        /// decision 5 work here: re-ticking the Transitions follow box leaves the stored On/Off
        /// keys dormant on disk instead of deleting them, and this gate is the thing that stops
        /// them being read. Unticking it again re-reveals exactly the same rows.
        ///
        /// Nothing was migrated to make that true: the Sanitize mapping gives any element with a
        /// stored On/Off <c>Transitions = Own</c>, so an existing profile resolves identically.
        /// Use <see cref="RawModeOf"/> when you need the STORED state (the migration does).</summary>
        internal static HudFxMode ModeOf(HudElementDef d, HudTransitionFxDef fx, bool bare)
        {
            if (d == null || fx == null) return HudFxMode.Inherit;
            var slot = bare ? HudStyleSlot.Bare : HudStyleSlot.Base;
            if (HudStyleFx.SourceOf(d, HudFxCategory.Transitions, slot) != HudFxSource.Own)
                return HudFxMode.Inherit;
            return RawModeOf(d, fx, slot);
        }

        /// <summary>The STORED tri-state, with the legacy-bool fallback for profiles that predate
        /// the tri-state migration (and for elements built in memory this session). Ignores the
        /// per-category follow gate on purpose — the Sanitize mapping reads this to decide whether
        /// the element's Transitions category should be Own in the first place.</summary>
        internal static HudFxMode RawModeOf(HudElementDef d, HudTransitionFxDef fx, HudStyleSlot slot)
        {
            if (d == null || fx == null) return HudFxMode.Inherit;
            int m = d.GetIFor(slot, fx.ModeKey, -1);
            if (m == (int)HudFxMode.Inherit) return HudFxMode.Inherit;
            if (m == (int)HudFxMode.On) return HudFxMode.On;
            if (m == (int)HudFxMode.Off) return HudFxMode.Off;

            // Not stored (or garbage): read the legacy bool exactly the way its own default meant it.
            bool legacy = d.GetBFor(slot, fx.LegacyKey, fx.LegacyDefault);
            if (fx.LegacyDefault) return legacy ? HudFxMode.Inherit : HudFxMode.Off;
            return legacy ? HudFxMode.On : HudFxMode.Inherit;
        }

        internal static HudFxMode ModeOf(HudElementDef d, string key, bool bare)
        {
            return ModeOf(d, Find(key), bare);
        }

        /// <summary>Write the element's tri-state. The legacy bool is MIRRORED (On -> true,
        /// Off -> false, Inherit -> removed) so the consumers that still read it directly
        /// (HudElementView.DissolveFor / ApplyPulse, HudStyleMigration's "is this element
        /// effectively global" test) stay in agreement until they are moved to the resolver.</summary>
        internal static void SetMode(HudElementDef d, HudTransitionFxDef fx, bool bare, HudFxMode mode)
        {
            if (d == null || fx == null) return;
            // RAW base writes on purpose (0.9.2.5): power transitions are deliberately SHARED by
            // every tier, so they must not trip HudElementDef's per-tier copy-on-write and freeze a
            // fork's copy of a motion setting. The bare path exists only for the legacy overrides
            // the pre-tri-state migration may still be folding.
            if (bare)
            {
                d.SetIFor(HudStyleSlot.Bare, fx.ModeKey, (int)mode);
                if (mode == HudFxMode.Inherit) d.SetSFor(HudStyleSlot.Bare, fx.LegacyKey, null);
                else d.SetBFor(HudStyleSlot.Bare, fx.LegacyKey, mode == HudFxMode.On);
                return;
            }
            d.SetI(fx.ModeKey, (int)mode);
            if (mode == HudFxMode.Inherit) d.Set(fx.LegacyKey, null);
            else d.SetB(fx.LegacyKey, mode == HudFxMode.On);
        }

        internal static void SetMode(HudElementDef d, string key, bool bare, HudFxMode mode)
        {
            SetMode(d, Find(key), bare, mode);
        }

        /// <summary>The element's OWN strength (only meaningful in <see cref="HudFxMode.On"/>);
        /// falls back to the global so a freshly-forced element does not jump.</summary>
        internal static float AmountOf(HudElementDef d, HudTransitionFxDef fx, bool bare)
        {
            if (fx == null) return 0f;
            if (d == null) return fx.GlobalAmt;
            return Mathf.Clamp(d.GetFFor(bare, fx.AmtKey, fx.GlobalAmt), 0f, 2f);
        }

        /// <summary>Write the element's own strength. Setting a strength IMPLIES the element wants
        /// its own value, so an inheriting element is promoted to On — otherwise the slider would
        /// visibly do nothing.</summary>
        internal static void SetAmount(HudElementDef d, HudTransitionFxDef fx, bool bare, float v)
        {
            if (d == null || fx == null) return;
            // Raw on the base path — see SetMode: transitions stay shared across tiers.
            if (bare) d.SetFFor(HudStyleSlot.Bare, fx.AmtKey, Mathf.Clamp(v, 0f, 2f));
            else d.SetF(fx.AmtKey, Mathf.Clamp(v, 0f, 2f));
            if (ModeOf(d, fx, bare) == HudFxMode.Inherit) SetMode(d, fx, bare, HudFxMode.On);
        }

        /// <summary>THE resolver. Every consumer (animator, glitch, warp, dissolve, the menus)
        /// goes through this one rule.</summary>
        internal static float Resolve(HudElementDef d, HudTransitionFxDef fx, bool bare)
        {
            if (fx == null || !fx.GlobalOn) return 0f;      // master off = off everywhere
            if (d == null) return 0f;                       // no element = nothing to animate
            switch (ModeOf(d, fx, bare))
            {
                case HudFxMode.Off: return 0f;
                case HudFxMode.On: return AmountOf(d, fx, bare);
                default: return fx.GlobalAmt;
            }
        }

        internal static float Resolve(HudElementDef d, string key, bool bare)
        {
            return Resolve(d, Find(key), bare);
        }

        /// <summary>The global strength an inheriting element would get (0 when the master is off).
        /// Lets the global menu preview the effective value without an element.</summary>
        internal static float GlobalAmtFor(string key)
        {
            var fx = Find(key);
            if (fx == null || !fx.GlobalOn) return 0f;
            return fx.GlobalAmt;
        }

        // ---- one-time, idempotent per-element migration (called from HudDocument.Sanitize) ----

        /// <summary>Fold an element's pre-tri-state bools into "&lt;key&gt;Mode". Returns the number
        /// of modes written. Idempotent: a stored mode is never rewritten, and an element with no
        /// legacy bool is left at Inherit (nothing written, no profile bloat).
        ///
        /// Mapping, per effect, for a legacy bool that is actually PRESENT:
        ///   legacy default TRUE  (collapse/glitch/warp/dissolve):
        ///       false                       -> Off   (never lose a user's explicit "off")
        ///       true + a tuned strength     -> On    (keeps the strength they dialled in)
        ///       true + the stock strength   -> Inherit (so the new globals actually drive it)
        ///   legacy default FALSE (pulse):
        ///       true                        -> On    (it was an explicit opt-in)
        ///       false                       -> Inherit (which resolves to 0 by global default)
        /// Both the base value and any "b_" BARE override are migrated.</summary>
        internal static int MigrateElement(HudElementDef d)
        {
            if (d == null) return 0;
            int written = 0;
            for (int i = 0; i < All.Length; i++)
            {
                var fx = All[i];
                if (fx == null) continue;
                if (MigrateOne(d, fx, false)) written++;
                if (MigrateOne(d, fx, true)) written++;
            }
            return written;
        }

        private static bool MigrateOne(HudElementDef d, HudTransitionFxDef fx, bool bare)
        {
            // Already migrated (or explicitly authored) — never touch it again.
            if (bare ? d.HasBareOverride(fx.ModeKey) : d.GetS(fx.ModeKey, null) != null) return false;
            // No stored legacy bool: Inherit is the default, so write nothing.
            bool hasLegacy = bare ? d.HasBareOverride(fx.LegacyKey) : d.GetS(fx.LegacyKey, null) != null;
            if (!hasLegacy) return false;

            bool legacy = d.GetBFor(bare, fx.LegacyKey, fx.LegacyDefault);
            HudFxMode mode;
            if (fx.LegacyDefault)
            {
                if (!legacy) mode = HudFxMode.Off;
                else if (!NearOne(d.GetFFor(bare, fx.AmtKey, 1f))) mode = HudFxMode.On;
                else return false;   // stock "on" — leave it Inherit so the globals drive it
            }
            else
            {
                if (!legacy) return false;
                mode = HudFxMode.On;
            }

            // Write the mode WITHOUT SetMode's legacy mirroring: the stored bool is already what
            // we just derived the mode from, and re-writing it would churn the profile on load.
            if (bare) d.SetIFor(HudStyleSlot.Bare, fx.ModeKey, (int)mode);
            else d.SetI(fx.ModeKey, (int)mode);
            return true;
        }

        private static bool NearOne(float v) { return Mathf.Abs(v - 1f) < 0.0001f; }
    }
}
