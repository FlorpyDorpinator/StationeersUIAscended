using System;
using BepInEx.Configuration;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>What KIND of widget one registry row is. <see cref="HudFxKind.Int"/> is not in the
    /// plan's sketch but is unavoidable: three shipped rows (bloom blur steps x2, caution flash
    /// count) are integer sliders and would otherwise have to lie about their type.</summary>
    internal enum HudFxKind
    {
        Bool,
        Float,
        Int,
        /// <summary>An #RRGGBB string edited through a hue wheel. Drawn by its own bespoke helper
        /// (the wheel needs a parse cache), so the generic row renderer skips it.</summary>
        Color,
        /// <summary>A named-option combo (corner style, backdrop blur resolution, bloom glow
        /// resolution). Same as <see cref="Color"/>: bespoke drawer, skipped by the generic row.</summary>
        Combo,
    }

    /// <summary>The follow-able family a row belongs to. These map ONE-FOR-ONE onto the F9 sub-tabs
    /// (FlorpyDorp's decision 4, plan §3.4), because requirement 2 is literally "the element popup
    /// must look 1:1 like the global menu".
    ///
    /// A row's category is where the ELEMENT POPUP will page it in Phase 2; F9 may draw the same row
    /// on a different sub-tab when it is an "advanced" knob (the uneven/organic halo trio is
    /// Category = Glow but is drawn on Effects &gt; Advanced, exactly as plan §3.4 says: "Effects &gt;
    /// Glow (+ the Advanced halo rows)"). <see cref="HudStyleFxDef.Section"/> is what decides WHERE
    /// in F9 a row is drawn; Category decides which follow checkbox will own it.</summary>
    internal enum HudFxCategory { Surface, Glass, Edges, Glow, Bloom, Alerts, Transitions }

    /// <summary>Where ONE category of an element's style comes from (plan §3.2/§3.5). Exactly one
    /// of the three per category, which is why it is a packed radio rather than three flag sets —
    /// the illegal states are unrepresentable, the same reasoning that produced
    /// <see cref="HudFxMode"/> for the transitions family.
    ///
    /// <see cref="Donor"/> is Phase 4 (inherit-from-another-element). It is already legal in the
    /// packing so a Phase 4 profile round-trips through Phase 3 code, but nothing offers it in the
    /// UI yet and <see cref="HudStyleFx.SourceOf"/> resolves it as <see cref="Global"/> — fail-soft,
    /// never as a blank element.</summary>
    internal enum HudFxSource { Global = 0, Donor = 1, Own = 2 }

    /// <summary>ONE steady-state style knob: its per-element param key, its display label, its
    /// range, lazy accessors for the global <see cref="ConfigEntry"/> that backs it, and the honest
    /// capability flags (shared-only / SDF-only / legacy-approximate / does-not-travel).
    ///
    /// HOT-RELOAD DISCIPLINE (the reason every accessor is a <see cref="Func{T}"/> and never a
    /// cached entry): HudConfig re-binds every ConfigEntry at plugin start AND after every F6
    /// hot-reload, so a cached reference would point at the previous assembly's binding. Same rule,
    /// same reason, as <see cref="HudTransitionFxDef"/>. The table holds no scene or document state,
    /// so it needs no teardown of its own — it goes with the assembly.</summary>
    internal sealed class HudStyleFxDef
    {
        /// <summary>Canonical id for this row. Stable, never stored in a profile — it exists so a
        /// consumer (an F9 sub-tab, the `hudfx` dump, Phase 2's popup) can name one row.</summary>
        public readonly string Key;

        /// <summary>Where the element's own value lives in the param bag ("glow", "customFrost").
        /// Null = this knob has NO per-element storage: either it is physically shared
        /// (<see cref="SharedOnly"/>) or it is a first-class def FIELD (<see cref="FirstClassField"/>).</summary>
        public readonly string ParamKey;

        /// <summary>The companion per-element bool that gates this value ("customGlowOn" for
        /// "glow"), so seeding/reset/copy can move the pair together. Null when there is none.
        /// A checkbox row stores itself in <see cref="ParamKey"/> and leaves this null.</summary>
        public readonly string OnParamKey;

        /// <summary>The per-element storage when it is NOT a param-bag key but a first-class
        /// <see cref="HudElementDef"/> field (border width, the four corner radii, font scale).
        /// Informational; <see cref="OwnText"/> is what actually reads it.</summary>
        public readonly string FirstClassField;

        /// <summary>The ONE display string, verbatim — leading spaces and the "##id" ImGui
        /// disambiguator included, because both menus must render byte-identical captions and
        /// ImGui keys its widgets by label. Never paraphrase it at a call site.</summary>
        public readonly string Label;

        /// <summary>One-line explanation. NOT rendered by F9 today (the steady-state rows have no
        /// tip lines and adding them would be a visual change); it is here for the Phase 2 popup
        /// and for the `hudfx` dump.</summary>
        public readonly string Tip;

        public readonly HudFxCategory Category;

        /// <summary>Ordering/grouping tag WITHIN the category — it is what an F9 sub-tab loops on,
        /// so a contiguous run of rows renders in table order with no hand-written list. See the
        /// Sec* constants on <see cref="HudStyleFx"/>.</summary>
        public readonly string Section;

        public readonly HudFxKind Kind;
        public readonly float Min;
        public readonly float Max;

        /// <summary>Dimmed lines F9 prints DIRECTLY under this row, verbatim. They render only when
        /// the row itself renders, which matches every current call site.</summary>
        public readonly string[] Notes;

        /// <summary>Physically global: one render texture, one shared material uniform, one clock,
        /// or one full-screen post pass. There is nothing to make per-element and never can be
        /// (plan §1.6). The popup renders these as read-only rows showing the live global, which is
        /// why a shared row still carries its value accessor.</summary>
        public readonly bool SharedOnly;

        /// <summary>Needs the analytic panel shader. Inert on the legacy mesh path AND on a
        /// Cut-corner panel under an ABI-2 bundle (to-do item 10), where the panel drops off
        /// `sdfglass` entirely and takes frost depth, chroma, edge flow and the halo family with it.</summary>
        public readonly bool SdfOnly;

        /// <summary>The legacy mesh path has exactly ONE per-element scalar (uv0.x) shared by
        /// shine / iridescence / frost / chroma, so different Custom strengths there are an
        /// unavoidable approximation. A per-ROW flag, not one section header.</summary>
        public readonly bool LegacyApprox;

        /// <summary>Can be forked per <see cref="HudStyleSlot"/>. False for shared-only rows (there
        /// is nothing to fork) and for the transitions family, which is deliberately shared by every
        /// tier — see <see cref="HudTransitionFx.SetMode"/>'s raw base writes.</summary>
        public readonly bool PerTierCapable;

        /// <summary>This global is in <see cref="HudTheme"/>'s Exclude list: it trades frame time on
        /// the player's OWN machine, so an imported theme must never move it. The element popup's
        /// shared-row label says "machine-local, does not travel with the theme" for these.</summary>
        public readonly bool DoesNotTravel;

        /// <summary>A NULL-GLOBAL row whose renderer reads the per-element key in BOTH style states,
        /// so the element popup keeps it editable even while the element follows the globals.
        ///
        /// Only meaningful when <see cref="HasGlobal"/> is false. The distinction is real and was
        /// already deliberate before the registry existed: box-end fade and the trapezoid insets are
        /// element GEOMETRY (<c>ApplyEdgeFade</c> / <c>InsetTop</c> read them unconditionally) and
        /// the element font scale multiplies the shared one (<c>FontScaleFor</c>, likewise
        /// unconditional), whereas <c>rippleSmooth</c> is hard-zeroed while following and the
        /// portrait ring pair is only read on the Custom branch of <c>ApplyBorderOnlyEdge</c> — so
        /// offering those three while following would be a dead control.</summary>
        public readonly bool StateIndependent;

        /// <summary>Capability predicate — "does this element even have this surface?". Wired in
        /// Phase 2 against <see cref="HudElementView"/>'s <c>Fx*</c> capability accessors (the
        /// internal facades over its protected/private Supports* virtuals). Null means "applies
        /// wherever the surface exists", which is what every consumer assumes.
        ///
        /// Applies is about the element TYPE, never about runtime state: a knob that this surface
        /// COULD use but that is currently inert (the analytic shader is off, the bundle is too old,
        /// the panel's corners are Cut on an ABI-2 bundle) must still render, with a reason —
        /// that is what <see cref="SdfOnly"/> and <see cref="LegacyApprox"/> are for.</summary>
        public readonly Func<HudElementView, bool> Applies;

        private readonly Func<ConfigEntry<float>> _gf;
        private readonly Func<ConfigEntry<bool>> _gb;
        private readonly Func<ConfigEntry<int>> _gi;
        private readonly Func<ConfigEntry<string>> _gs;
        private readonly Func<ConfigEntry<bool>> _master;
        private readonly Func<ConfigEntry<bool>> _tier;
        private readonly Func<HudElementDef, HudStyleSlot, string> _ownText;

        internal HudStyleFxDef(string key, HudFxCategory category, string section, HudFxKind kind,
            string label, string tip, string paramKey, string onParamKey, string firstClassField,
            float min, float max, string[] notes,
            Func<ConfigEntry<float>> gf, Func<ConfigEntry<bool>> gb,
            Func<ConfigEntry<int>> gi, Func<ConfigEntry<string>> gs,
            Func<ConfigEntry<bool>> master, Func<ConfigEntry<bool>> tier,
            Func<HudElementDef, HudStyleSlot, string> ownText,
            bool sharedOnly, bool sdfOnly, bool legacyApprox, bool perTierCapable,
            bool doesNotTravel, bool stateIndependent, Func<HudElementView, bool> applies)
        {
            Key = key;
            Category = category;
            Section = section;
            Kind = kind;
            Label = label;
            Tip = tip;
            ParamKey = paramKey;
            OnParamKey = onParamKey;
            FirstClassField = firstClassField;
            Min = min;
            Max = max;
            Notes = notes;
            _gf = gf; _gb = gb; _gi = gi; _gs = gs;
            _master = master; _tier = tier; _ownText = ownText;
            SharedOnly = sharedOnly;
            SdfOnly = sdfOnly;
            LegacyApprox = legacyApprox;
            PerTierCapable = perTierCapable;
            DoesNotTravel = doesNotTravel;
            StateIndependent = stateIndependent;
            Applies = applies;
        }

        // ---- lazy global accessors (never cache the entry: HudConfig rebinds on F6) ----------

        public ConfigEntry<float> FloatEntry
        { get { try { return _gf != null ? _gf() : null; } catch { return null; } } }

        public ConfigEntry<bool> BoolEntry
        { get { try { return _gb != null ? _gb() : null; } catch { return null; } } }

        public ConfigEntry<int> IntEntry
        { get { try { return _gi != null ? _gi() : null; } catch { return null; } } }

        public ConfigEntry<string> StringEntry
        { get { try { return _gs != null ? _gs() : null; } catch { return null; } } }

        /// <summary>The row's global entry whatever its type — the plan's single
        /// <c>Func&lt;ConfigEntryBase&gt;</c> accessor, resolved through the typed ones.</summary>
        public ConfigEntryBase GlobalEntry
        {
            get
            {
                if (_gf != null) return FloatEntry;
                if (_gb != null) return BoolEntry;
                if (_gi != null) return IntEntry;
                if (_gs != null) return StringEntry;
                return null;
            }
        }

        /// <summary>False for the handful of knobs that genuinely have NO global — `rippleSmooth`
        /// ("energy smoothness, per-element only"), the box-end-fade amounts, the trapezoid insets,
        /// the portrait ring pair, `customFrostOn`, and the per-element font-scale multiplier.
        /// A row with no global is NEVER drawn in F9 and is neutral when the element is following.</summary>
        public bool HasGlobal => _gf != null || _gb != null || _gi != null || _gs != null;

        /// <summary>The feature checkbox directly above this row in F9 (FxGlowOn for "outward
        /// strength"), or null when the row is ungated. Distinct from <see cref="TierEntry"/>.</summary>
        public ConfigEntry<bool> MasterEntry
        { get { try { return _master != null ? _master() : null; } catch { return null; } } }

        /// <summary>The CAPABILITY gate that owns this row's whole family (FxTierA/B/C, SdfPanels).
        /// F9 draws these as one `if` around a section rather than per row; the popup uses it for
        /// the "Tier A values are inactive" notice.</summary>
        public ConfigEntry<bool> TierEntry
        { get { try { return _tier != null ? _tier() : null; } catch { return null; } } }

        public bool MasterOn { get { var e = MasterEntry; return e == null || e.Value; } }
        public bool TierOn { get { var e = TierEntry; return e == null || e.Value; } }

        // ---- live global values, fail-soft ---------------------------------------------------

        public float GlobalFloat { get { var e = FloatEntry; return e != null ? e.Value : 0f; } }
        public bool GlobalBool { get { var e = BoolEntry; return e != null && e.Value; } }
        public int GlobalInt { get { var e = IntEntry; return e != null ? e.Value : 0; } }
        public string GlobalString { get { var e = StringEntry; return e != null ? e.Value : null; } }

        /// <summary>The global's live value as display text, or "--" when this row has no global.</summary>
        public string GlobalText
        {
            get
            {
                if (_gf != null) { var e = FloatEntry; return e != null ? e.Value.ToString("0.###") : "--"; }
                if (_gb != null) { var e = BoolEntry; return e != null ? (e.Value ? "ON" : "OFF") : "--"; }
                if (_gi != null) { var e = IntEntry; return e != null ? e.Value.ToString() : "--"; }
                if (_gs != null) { var e = StringEntry; return e != null && !string.IsNullOrEmpty(e.Value) ? e.Value : "--"; }
                return "--";
            }
        }

        /// <summary>True when this element/slot actually STORES its own value for this row (as
        /// opposed to resolving the global through an absent key). Read-only presence test.</summary>
        public bool HasOwnValue(HudElementDef d, HudStyleSlot slot)
        {
            if (d == null || string.IsNullOrEmpty(ParamKey)) return false;
            try { return d.GetSFor(slot, ParamKey, null) != null; } catch { return false; }
        }

        /// <summary>What this element/slot resolves this row to, as display text.
        ///
        /// It implements plan §3.2's ONE rule and nothing else: following ⇒ the global; own ⇒ the
        /// stored value with THE GLOBAL as the default for an absent key. Phase 0b already made
        /// every live resolver agree with that, so this dump is a faithful mirror rather than a
        /// second opinion. First-class fields (border width, corner radii, font scale) route
        /// through their own accessor.</summary>
        public string ResolveText(HudElementDef d, HudStyleSlot slot, bool custom)
        {
            try
            {
                if (d == null) return GlobalText;
                if (_ownText != null) return _ownText(d, slot);
                if (SharedOnly || string.IsNullOrEmpty(ParamKey)) return GlobalText;
                if (!custom) return GlobalText;
                switch (Kind)
                {
                    case HudFxKind.Bool: return d.GetBFor(slot, ParamKey, GlobalBool) ? "ON" : "OFF";
                    case HudFxKind.Int: return d.GetIFor(slot, ParamKey, GlobalInt).ToString();
                    case HudFxKind.Combo: return d.GetIFor(slot, ParamKey, GlobalInt).ToString();
                    case HudFxKind.Color:
                        {
                            string s = d.GetSFor(slot, ParamKey, null);
                            return string.IsNullOrEmpty(s) ? "(derived)" : s;
                        }
                    default: return d.GetFFor(slot, ParamKey, GlobalFloat).ToString("0.###");
                }
            }
            catch { return "?"; }
        }
    }

    /// <summary>
    /// THE one registry of STEADY-STATE HUD style knobs — the surface/glass/edge/glow/bloom/alert
    /// families — so the five hand-written lists that currently describe them cannot drift:
    /// the F9 global rows, the element popup's mirror, the seed-on-separation writes, the def-only
    /// snapshot, and the "reset to globals" loop (plan §1.7).
    ///
    /// PHASE 1 SCOPE: this table exists and F9's global rows are rendered FROM it. Nothing else
    /// consumes it yet, and nothing behaves differently — the labels, ranges, order, gating and
    /// sub-tabs are byte-identical to the hand-written rows they replaced. Phases 2–5 fold the
    /// remaining four lists onto it.
    ///
    /// TRANSITIONS — the bridging decision (deliberate, recorded here rather than in a comment
    /// somewhere else): the seven power-transition effects are NOT duplicated into this table.
    /// <see cref="HudTransitionFx"/> remains their sole authority. It already owns a richer model
    /// than a steady-state row can express — a per-element TRI-STATE (Inherit/On/Off), a legacy-bool
    /// mirror, its own idempotent migration and its own resolver — and it already drives BOTH menus
    /// from one place, which is the very property this table is being built to gain. Re-describing
    /// those seven effects here would create exactly the second list the plan exists to delete.
    /// <see cref="HudFxCategory.Transitions"/> therefore exists in the enum (Phase 3's follow
    /// checkbox needs the category to be nameable) but <see cref="All"/> contains ZERO rows for it;
    /// consumers reach them through <see cref="Transitions"/>, the one-line bridge below.
    ///
    /// The table holds no scene, document or game state and caches no ConfigEntry, so it needs no
    /// hot-reload teardown: its lambdas read HudConfig's statics at CALL time and the whole type is
    /// replaced with the assembly on F6.
    /// </summary>
    internal static class HudStyleFx
    {
        // ---- section tags -------------------------------------------------------------------
        // Consts, not literals: a typo in a section string silently drops a row from F9, which is
        // the exact failure mode (a knob quietly disappearing from a hand-maintained list) this
        // registry exists to make impossible.

        internal const string SecText = "text";                 // Theme > Typography & boxes: TEXT
        internal const string SecBox = "box";                   //   ... BOX SHAPE
        internal const string SecSdfMaster = "sdfmaster";       //   ... SHARP GLASS PANELS (the master)
        internal const string SecSdf = "sdf";                   //   ... its two children
        // Per-element knobs with no F9 home at all. A per-element knob that DOES belong to an
        // existing F9 group carries that group's section instead (the trapezoid insets sit in BOX
        // SHAPE, the box-end-fade amounts in BOX END FADE, the frost opt-out in FROSTED BACKDROP),
        // so the element popup renders it in the right place and in the right ORDER. That is safe
        // for F9: its FxRow bails on a row whose global entry is null, which is every such row.
        internal const string SecElementOnly = "elementonly";

        internal const string SecTierA = "tierA";               // Effects > Glass: CORE EFFECTS master
        internal const string SecCore = "core";                 //   ... its children
        internal const string SecBoxEndFade = "boxendfade";     //   ... BOX END FADE (shape)
        internal const string SecTierB = "tierB";               //   ... ANIMATED GLASS master
        internal const string SecAnimGlass = "animglass";       //   ... its children
        internal const string SecTierC = "tierC";               //   ... FROSTED BACKDROP master
        internal const string SecFrost = "frost";               //   ... its children
        internal const string SecFrostPerf = "frostperf";       // Effects > Advanced: PERFORMANCE (frost)

        internal const string SecEdgeMaster = "edgemaster";     // Effects > Edges: the master toggle
        internal const string SecEdge = "edge";                 //   ... everything under it

        internal const string SecHalo = "halo";                 // Effects > Glow: Glow halo + strengths
        internal const string SecEnvelope = "envelope";         //   ... SHARED HALO / FLOWING-AURA ENVELOPE
        internal const string SecBreathSpeed = "breathspeed";   //   ... the shared breath clock
        internal const string SecPulseShape = "pulseshape";     //   ... BREATHING PULSE (shape)
        internal const string SecAdvancedHalo = "advancedhalo"; // Effects > Advanced: HALO FINE DETAIL

        internal const string SecBloomMaster = "bloommaster";   // Effects > Bloom: the master toggle
        internal const string SecBloomBase = "bloombase";       //   ... Base glow
        internal const string SecBloomPulse = "bloompulse";     //   ... Breathing bloom
        internal const string SecBloomReact = "bloomreact";     //   ... State-reactive bloom
        internal const string SecBloomBand2 = "bloomband2";     //   ... Border / highlight bloom
        internal const string SecBloomRes = "bloomres";         // Effects > Advanced: PERFORMANCE
        internal const string SecBloomBias = "bloombias";       // Effects > Advanced: BLOOM COLOUR BIAS

        internal const string SecAlertMaster = "alertmaster";   // Effects > Alerts: the master toggle
        internal const string SecAlertShape = "alertshape";     //   ... breath length / count / strength
        internal const string SecAlertBright = "alertbright";   //   ... the two brightness gains

        // ---- row factories ------------------------------------------------------------------
        // Named optional arguments keep the table below readable; every row states only what makes
        // it different from a plain per-element float.

        private static HudStyleFxDef Flt(string key, HudFxCategory cat, string section, string label,
            Func<ConfigEntry<float>> global, float min, float max, string paramKey = null,
            string tip = null, Func<ConfigEntry<bool>> master = null, Func<ConfigEntry<bool>> tier = null,
            string onParamKey = null, string firstClassField = null,
            Func<HudElementDef, HudStyleSlot, string> ownText = null,
            bool sharedOnly = false, bool sdfOnly = false, bool legacyApprox = false,
            bool doesNotTravel = false, string[] notes = null,
            Func<HudElementView, bool> applies = null)
            => new HudStyleFxDef(key, cat, section, HudFxKind.Float, label, tip, paramKey, onParamKey,
                firstClassField, min, max, notes, global, null, null, null, master, tier, ownText,
                sharedOnly, sdfOnly, legacyApprox, !sharedOnly, doesNotTravel, false, applies);

        private static HudStyleFxDef Bln(string key, HudFxCategory cat, string section, string label,
            Func<ConfigEntry<bool>> global, string paramKey = null, string tip = null,
            Func<ConfigEntry<bool>> master = null, Func<ConfigEntry<bool>> tier = null,
            bool sharedOnly = false, bool sdfOnly = false, bool legacyApprox = false,
            bool doesNotTravel = false, string[] notes = null,
            Func<HudElementView, bool> applies = null)
            => new HudStyleFxDef(key, cat, section, HudFxKind.Bool, label, tip, paramKey, null,
                null, 0f, 1f, notes, null, global, null, null, master, tier, null,
                sharedOnly, sdfOnly, legacyApprox, !sharedOnly, doesNotTravel, false, applies);

        private static HudStyleFxDef Whole(string key, HudFxCategory cat, string section, string label,
            Func<ConfigEntry<int>> global, int min, int max, string paramKey = null,
            string tip = null, Func<ConfigEntry<bool>> master = null, Func<ConfigEntry<bool>> tier = null,
            bool sharedOnly = false, bool doesNotTravel = false, string[] notes = null)
            => new HudStyleFxDef(key, cat, section, HudFxKind.Int, label, tip, paramKey, null,
                null, min, max, notes, null, null, global, null, master, tier, null,
                sharedOnly, false, false, !sharedOnly, doesNotTravel, false, null);

        private static HudStyleFxDef Col(string key, HudFxCategory cat, string section, string label,
            Func<ConfigEntry<string>> global, string paramKey = null, string tip = null,
            Func<ConfigEntry<bool>> master = null, Func<ConfigEntry<bool>> tier = null,
            bool sharedOnly = false, bool doesNotTravel = false, string[] notes = null,
            Func<HudElementView, bool> applies = null)
            => new HudStyleFxDef(key, cat, section, HudFxKind.Color, label, tip, paramKey, null,
                null, 0f, 0f, notes, null, null, null, global, master, tier, null,
                sharedOnly, false, false, !sharedOnly, doesNotTravel, false, applies);

        /// <param name="global">Int-backed combo (corner style, bloom glow resolution).</param>
        /// <param name="globalFloat">Float-backed combo — the backdrop blur divisor is stored as a
        /// float (2/4/8) even though it presents as three options.</param>
        private static HudStyleFxDef Cmb(string key, HudFxCategory cat, string section, string label,
            Func<ConfigEntry<int>> global, string paramKey = null, string tip = null,
            Func<ConfigEntry<bool>> master = null, Func<ConfigEntry<bool>> tier = null,
            bool sharedOnly = false, bool doesNotTravel = false,
            Func<ConfigEntry<float>> globalFloat = null,
            Func<HudElementView, bool> applies = null)
            => new HudStyleFxDef(key, cat, section, HudFxKind.Combo, label, tip, paramKey, null,
                null, 0f, 0f, null, globalFloat, null, global, null, master, tier, null,
                sharedOnly, false, false, !sharedOnly, doesNotTravel, false, applies);

        /// <summary>A knob with NO global at all (plan §1.3's honest caveat, generalised): the value
        /// is per-element or nothing. F9 never draws these — there is no global row to draw.
        ///
        /// <paramref name="stateIndependent"/> says the RENDERER reads the key in both style states,
        /// so the element popup keeps the row editable while the element follows the globals; the
        /// default (false) means the value is neutral while following and the popup hides it behind
        /// the "follows global" summary rather than offering a dead control.</summary>
        private static HudStyleFxDef Own(string key, HudFxCategory cat, string label, float min,
            float max, string paramKey, string tip, HudFxKind kind = HudFxKind.Float,
            string firstClassField = null, Func<HudElementDef, HudStyleSlot, string> ownText = null,
            bool sdfOnly = false, bool stateIndependent = false,
            Func<HudElementView, bool> applies = null, string section = SecElementOnly)
            => new HudStyleFxDef(key, cat, section, kind, label, tip, paramKey, null,
                firstClassField, min, max, null, null, null, null, null, null, null, ownText,
                false, sdfOnly, false, true, false, stateIndependent, applies);

        // -------------------------------------------------------------------------------------
        // THE TABLE. Row order inside a (Category, Section) pair IS the F9 draw order — the sub-tab
        // loops over it — so re-ordering rows here moves controls in the menu.
        // -------------------------------------------------------------------------------------
        internal static readonly HudStyleFxDef[] All =
        {
            // ================= SURFACE (Theme > Typography & boxes / element Appearance) ========

            Flt("hudFontScale", HudFxCategory.Surface, SecText, "Font scale (all HUD text)",
                () => HudConfig.FontScale, 0.6f, 1.8f, sharedOnly: true,
                tip: "Multiplies EVERY HUD label. An element's own font scale multiplies on top.",
                applies: v => v.FxUsesFontScale),
            // Element-only, but placed HERE rather than at the end of the category so the popup's
            // Theme page reads top-to-bottom the way F9's does. F9 never loops SecElementOnly, so
            // moving one of these rows can never move a control in the global menu.
            Own("elFontScale", HudFxCategory.Surface, "Font scale", 0.4f, 3f, null,
                "This element's own text multiplier, ON TOP of the shared HUD font scale.",
                firstClassField: "FontScale", ownText: (d, s) => d.FontScaleFor(s).ToString("0.###"),
                stateIndependent: true, applies: v => v.FxUsesFontScale, section: SecText),

            // BOX SHAPE. The four corner radii are ONE global row here and four per-element rows in
            // the popup — the global is the default all four fall back to through the -1 sentinel.
            Flt("cornerRadius", HudFxCategory.Surface, SecBox, "Default corner rounding (px)",
                () => HudConfig.CornerRadius, 0f, 28f, firstClassField: "RTL/RTR/RBR/RBL",
                tip: "Corner size. Each element can set its four corners individually.",
                ownText: (d, s) => d.RTLFor(s).ToString("0.##") + "/" + d.RTRFor(s).ToString("0.##")
                    + "/" + d.RBRFor(s).ToString("0.##") + "/" + d.RBLFor(s).ToString("0.##"),
                applies: v => v.FxAuthoredCorners),
            Cmb("cornerStyle", HudFxCategory.Surface, SecBox, "Corner style",
                () => HudConfig.HudCornerStyle, paramKey: "cornerStyle",
                tip: "Rounded arcs or a flat 45-degree chamfer. Per element, 0 = follow this global.",
                applies: v => v.FxAuthoredCorners),
            Flt("borderWidth", HudFxCategory.Surface, SecBox, "Default line thickness (px)",
                () => HudConfig.BorderWidth, 0f, 6f, firstClassField: "BorderWidth",
                tip: "Outline thickness.",
                ownText: (d, s) => d.BorderWidthFor(s).ToString("0.##"),
                applies: v => v.FxHasChrome),
            Flt("feather", HudFxCategory.Surface, SecBox, "Edge softness / AA (px)",
                () => HudConfig.EdgeFeather, 0f, 4f, paramKey: "feather",
                tip: "Width of the anti-aliasing ramp on every edge.",
                applies: v => v.FxHasPanel),
            Flt("sheen", HudFxCategory.Surface, SecBox, "Default glass sheen",
                () => HudConfig.GlassSheen, 0f, 1f, paramKey: "sheen",
                tip: "The milky vertical gradient across the plate.",
                applies: v => v.FxHasPanel),
            Flt("spec", HudFxCategory.Surface, SecBox, "Default glass edge light",
                () => HudConfig.GlassEdge, 0f, 1f, paramKey: "spec",
                tip: "How much the rim whitens where it faces the key light. A Custom element "
                   + "stores the FINAL value, so this ONE key is the element's edge-light strength "
                   + "rather than a second strength that would double-count on the next snapshot.",
                applies: v => v.FxHasChrome),
            Own("insetTop", HudFxCategory.Surface, "Top inset (trapezoid)", 0f, 400f, "insetTop",
                "Trapezoid geometry: how far the top edge is drawn in. No global counterpart.",
                stateIndependent: true, applies: v => v.FxHasTrapezoid, section: SecBox),
            Own("insetBottom", HudFxCategory.Surface, "Bottom inset (trapezoid)", 0f, 400f, "insetBottom",
                "Trapezoid geometry: how far the bottom edge is drawn in. No global counterpart.",
                stateIndependent: true, applies: v => v.FxHasTrapezoid, section: SecBox),

            // SHARP GLASS PANELS.
            Bln("sdfPanels", HudFxCategory.Surface, SecSdfMaster, "Draw boxes with the sharp panel shader",
                () => HudConfig.SdfPanels, sharedOnly: true,
                tip: "The analytic SDF panel renderer. A capability gate, like the tier masters.",
                notes: new[]
                {
                    "  Crisper corners, rounder halos, and the advanced glow motion.",
                    "  (the analytic SDF panel renderer)",
                },
                applies: v => v.FxHasPanel),
            Flt("squircle", HudFxCategory.Surface, SecSdf, "  corner shape (2 round - 8 squircle)",
                () => HudConfig.SdfSquircle, 2f, 8f, paramKey: "squircle", tier: () => HudConfig.SdfPanels,
                sdfOnly: true,
                tip: "Superellipse exponent for the corner shoulder. Mutually exclusive with Cut corners.",
                applies: v => v.FxHasPanel),
            Bln("gaussianHalo", HudFxCategory.Surface, SecSdf, "  smooth distance falloff on halos",
                () => HudConfig.SdfGaussianHalo, paramKey: "gaussianHalo", tier: () => HudConfig.SdfPanels,
                sdfOnly: true, tip: "Gaussian distance falloff on the halo, not a blur convolution.",
                notes: new[] { "    (Gaussian falloff, not a blur convolution)" },
                applies: v => v.FxHasPanel),

            // The portrait ring's own halo: no F9 group owns it, so it keeps SecElementOnly and the
            // popup gives it its own heading.
            Own("ringGlow", HudFxCategory.Surface, "Ring glow", 0f, 2f, "ringGlow",
                "Halo band on a border-only ring (portrait). 0 = none. No global counterpart.",
                applies: v => v.FxHasRing),
            Own("ringGlowColor", HudFxCategory.Surface, "Ring glow colour", 0f, 0f, "ringGlowColor",
                "Palette ref or hex for the ring halo. Empty = derive from the rim.",
                kind: HudFxKind.Color, applies: v => v.FxHasRing),

            // ================= GLASS (Effects > Glass) =========================================

            Bln("tierA", HudFxCategory.Glass, SecTierA, "Core effects - surfaces, edges and glow",
                () => HudConfig.FxTierA, sharedOnly: true,
                tip: "Capability master for every mesh effect. Off = flat plates.",
                notes: new[]
                {
                    "  Always available (needs no shader bundle). Off = flat plates.",
                    "  Also gates the Edges and Glow sub-tabs.",
                }),

            Bln("hairlinesOn", HudFxCategory.Glass, SecCore, "Hairlines (sub-1px lines fade, not vanish)",
                () => HudConfig.FxHairlinesOn, tier: () => HudConfig.FxTierA, sharedOnly: true,
                tip: "Sub-pixel lines fade instead of dropping out. Consumed inside PanelGraphic."),
            Flt("hairlineMin", HudFxCategory.Glass, SecCore, "  thinnest line (px)",
                () => HudConfig.FxHairlineMin, 0.05f, 1f, master: () => HudConfig.FxHairlinesOn,
                tier: () => HudConfig.FxTierA, sharedOnly: true,
                tip: "Floor width a hairline is allowed to shrink to before it starts fading."),
            Bln("borderFadeOn", HudFxCategory.Glass, SecCore, "Border fade (unlit sections dissolve)",
                () => HudConfig.FxBorderFadeOn, paramKey: "customBorderFadeOn",
                tier: () => HudConfig.FxTierA,
                tip: "The outline dissolves where the key light does not reach it.",
                applies: v => v.FxHasChrome),
            Flt("bfade", HudFxCategory.Glass, SecCore, "  fade amount",
                () => HudConfig.FxBorderFade, 0f, 1f, paramKey: "bfade",
                onParamKey: "customBorderFadeOn", master: () => HudConfig.FxBorderFadeOn,
                tier: () => HudConfig.FxTierA,
                tip: "0 = solid outline, 1 = unlit border sections dissolve completely.",
                applies: v => v.FxHasChrome),
            Bln("softEdgeOn", HudFxCategory.Glass, SecCore, "Soft edge (boxes melt together)",
                () => HudConfig.FxSoftEdgeOn, paramKey: "customSoftEdgeOn",
                tier: () => HudConfig.FxTierA,
                tip: "The fill fades outward past the frame so neighbouring boxes blend.",
                applies: v => v.FxHasPanel),
            Flt("softEdge", HudFxCategory.Glass, SecCore, "  Width (px)##softEdgeWidth",
                () => HudConfig.FxSoftEdge, 0f, 48f, paramKey: "softEdge",
                onParamKey: "customSoftEdgeOn", master: () => HudConfig.FxSoftEdgeOn,
                tier: () => HudConfig.FxTierA, tip: "How far outside the frame the fill fade reaches.",
                applies: v => v.FxHasPanel),

            // BOX END FADE: the AMOUNTS are per-element geometry, the SHAPE is a shared uniform
            // pair. The amounts sit FIRST because that is the order the element popup needs (set
            // WHERE it fades, then read HOW) — and, being SecElementOnly, they are invisible to F9.
            Own("edgeFadeX", HudFxCategory.Glass, "Fade box ends L/R", 0f, 0.5f, "edgeFadeX",
                "Element GEOMETRY, editable in both style states. No global counterpart.",
                stateIndependent: true, applies: v => v.FxHasPanel, section: SecBoxEndFade),
            Own("edgeFadeY", HudFxCategory.Glass, "Fade box top/bottom", 0f, 0.5f, "edgeFadeY",
                "Element GEOMETRY, editable in both style states. No global counterpart.",
                stateIndependent: true, applies: v => v.FxHasPanel, section: SecBoxEndFade),
            Flt("edgeFadeCurve", HudFxCategory.Glass, SecBoxEndFade,
                "  Fade curve (low = hard edge, high = long tail)##edgeFadeCurve",
                () => HudConfig.FxEdgeFadeCurve, 0.25f, 4f, tier: () => HudConfig.FxTierA,
                sharedOnly: true, tip: "Ramp shape for every element's box-end fade.",
                applies: v => v.FxHasPanel),
            Flt("edgeFadeBorder", HudFxCategory.Glass, SecBoxEndFade,
                "  Border joins the fade (1 = with the box)##edgeFadeBorder",
                () => HudConfig.FxEdgeFadeBorder, 0f, 2f, tier: () => HudConfig.FxTierA,
                sharedOnly: true, tip: "How much the outline joins the end fade. Needs the sharp panel shader.",
                applies: v => v.FxHasPanel),

            Bln("tierB", HudFxCategory.Glass, SecTierB, "Animated glass - shine, iridescence, colour fringe",
                () => HudConfig.FxTierB, sharedOnly: true,
                tip: "Capability master for the bundle-shader glass animation."),

            Bln("shineOn", HudFxCategory.Glass, SecAnimGlass, "Shine sweep",
                () => HudConfig.FxShineOn, paramKey: "customShineOn", tier: () => HudConfig.FxTierB,
                tip: "A bright band sweeping across the plate.", applies: v => v.FxHasPanel),
            Flt("shine", HudFxCategory.Glass, SecAnimGlass, "  Strength##shineStrength",
                () => HudConfig.FxShine, 0f, 2f, paramKey: "customShine", onParamKey: "customShineOn",
                master: () => HudConfig.FxShineOn, tier: () => HudConfig.FxTierB, legacyApprox: true,
                tip: "How bright the sweep is.", applies: v => v.FxHasPanel),
            Flt("shinePeriod", HudFxCategory.Glass, SecAnimGlass, "  period (seconds)",
                () => HudConfig.FxShinePeriod, 2f, 60f, master: () => HudConfig.FxShineOn,
                tier: () => HudConfig.FxTierB, sharedOnly: true,
                tip: "Seconds between sweeps. ONE clock for the whole HUD (_ShinePos).",
                applies: v => v.FxHasPanel),
            Bln("iridOn", HudFxCategory.Glass, SecAnimGlass, "Iridescent rim",
                () => HudConfig.FxIridOn, paramKey: "customIridOn", tier: () => HudConfig.FxTierB,
                tip: "Angle-dependent colour shift along the rim.", applies: v => v.FxHasPanel),
            Flt("irid", HudFxCategory.Glass, SecAnimGlass, "  Strength##iridescenceStrength",
                () => HudConfig.FxIridescence, 0f, 1f, paramKey: "customIrid", onParamKey: "customIridOn",
                master: () => HudConfig.FxIridOn, tier: () => HudConfig.FxTierB, legacyApprox: true,
                tip: "How strong the rim's colour shift is.", applies: v => v.FxHasPanel),
            Bln("chromaOn", HudFxCategory.Glass, SecAnimGlass, "Chromatic fringe (uses frosted backdrop)",
                () => HudConfig.FxChromaOn, paramKey: "customChromaOn", tier: () => HudConfig.FxTierB,
                tip: "Colour fringing at the plate edge.", applies: v => v.FxHasPanel),
            Flt("chroma", HudFxCategory.Glass, SecAnimGlass, "  Strength##chromaStrength",
                () => HudConfig.FxChroma, 0f, 1f, paramKey: "customChroma", onParamKey: "customChromaOn",
                master: () => HudConfig.FxChromaOn, tier: () => HudConfig.FxTierB,
                sdfOnly: true, legacyApprox: true,
                tip: "GATED ON FROST: the fringe is the frosted backdrop sampled at an offset, so an "
                   + "element with no frost has nothing to offset and shows none.",
                applies: v => v.FxHasPanel),

            Bln("tierC", HudFxCategory.Glass, SecTierC, "Frosted backdrop - blur the world behind the HUD",
                () => HudConfig.FxTierC, sharedOnly: true,
                tip: "Capability master for the backdrop blur capture.",
                notes: new[] { "  Flat and Vertex-warp curvature only; the priciest effect here." }),

            // The per-element opt-out sits FIRST (it gates the two rows below it in the popup);
            // SecElementOnly keeps it invisible to F9, which has no such row.
            Own("customFrostOn", HudFxCategory.Glass, "Frosted glass", 0f, 1f, "customFrostOn",
                "Per-element frost opt-out. It has NO global gate - the global is the STRENGTH "
                + "slider - so an absent key means ON, and that literal is the honest default.",
                kind: HudFxKind.Bool, applies: v => v.FxHasPanel, section: SecFrost),
            Flt("frost", HudFxCategory.Glass, SecFrost, "Frost strength (all elements)",
                () => HudConfig.FrostStrength, 0f, 1f, paramKey: "customFrost",
                onParamKey: "customFrostOn", tier: () => HudConfig.FxTierC, legacyApprox: true,
                tip: "How much of the blurred backdrop shows through this plate.",
                applies: v => v.FxHasPanel),
            Flt("frostDepth", HudFxCategory.Glass, SecFrost, "Blur depth (shallow - deep)",
                () => HudConfig.FrostDepth, 0f, 1f, paramKey: "frostDepth",
                onParamKey: "customFrostOn",
                tier: () => HudConfig.FxTierC, sdfOnly: true,
                tip: "Which level of the shared blur pyramid this plate samples.",
                applies: v => v.FxHasPanel),
            Flt("frostDarken", HudFxCategory.Glass, SecFrost, "Backdrop darkening",
                () => HudConfig.FrostDarken, 0f, 1f, tier: () => HudConfig.FxTierC, sharedOnly: true,
                tip: "A shared material uniform (_FrostDarken) - one value for every panel."),
            Col("frostTint", HudFxCategory.Glass, SecFrost, "Frost tint",
                () => HudConfig.FrostTint, tier: () => HudConfig.FxTierC, sharedOnly: true,
                tip: "A shared material uniform (_FrostTint) - one value for every panel."),

            // Effects > Advanced: PERFORMANCE. Both size/throttle the ONE dual-Kawase pyramid.
            Cmb("frostDownsample", HudFxCategory.Glass, SecFrostPerf, "Backdrop blur resolution",
                null, globalFloat: () => HudConfig.FrostDownsample, tier: () => HudConfig.FxTierC,
                sharedOnly: true, doesNotTravel: true,
                tip: "Divisor for the shared blur render target. Machine-local: never travels with a theme."),
            Whole("frostUpdateEveryN", HudFxCategory.Glass, SecFrostPerf, "Re-blur every N frames",
                () => HudConfig.FrostUpdateEveryN, 1, 8, tier: () => HudConfig.FxTierC,
                sharedOnly: true, doesNotTravel: true,
                tip: "Throttle for the shared blur pass. Machine-local: never travels with a theme."),

            // ================= EDGES (Effects > Edges) ========================================

            Bln("edgeLightOn", HudFxCategory.Edges, SecEdgeMaster, "Edge energy (borders + lines)",
                () => HudConfig.FxEdgeLightOn, paramKey: "customRippleOn", tier: () => HudConfig.FxTierA,
                tip: "The directional lit border, its shimmer and its flow.",
                applies: v => v.FxHasEdgeEnergy),

            Flt("edgeLight", HudFxCategory.Edges, SecEdge, "  Strength##edgeEnergyStrength",
                () => HudConfig.FxEdgeLight, 0f, 2f, paramKey: "edgeLight",
                onParamKey: "customRippleOn", tier: () => HudConfig.FxTierA,
                tip: "Edge-light strength. LINES store it here; panels collapse theirs into the "
                   + "0..1 glass quantity `spec` on the Theme page instead.",
                applies: v => v.FxIsLine),
            Col("edgeLightColour", HudFxCategory.Edges, SecEdge, "  Edge-light colour##edgeLightColour",
                () => HudConfig.FxEdgeLightColor, tier: () => HudConfig.FxTierA, sharedOnly: true,
                tip: "Shared _EdgeLightColor uniform - one key light for the whole HUD."),
            Flt("edgeLightAngle", HudFxCategory.Edges, SecEdge, "  light angle (0=R,90=top,180=L)",
                () => HudConfig.FxEdgeLightAngle, 0f, 360f, tier: () => HudConfig.FxTierA,
                sharedOnly: true, tip: "Shared _EdgeLightDir uniform - one key light for the whole HUD."),
            Flt("edgeLightRim", HudFxCategory.Edges, SecEdge, "  opposing-rim catch",
                () => HudConfig.FxEdgeLightRim, 0f, 2f, tier: () => HudConfig.FxTierA,
                sharedOnly: true, tip: "Shared _EdgeLightRim uniform: light caught on the far rim."),
            Flt("edgeLightSharp", HudFxCategory.Edges, SecEdge, "  falloff (high=tight, low=broad)",
                () => HudConfig.FxEdgeLightSharp, 1f, 8f, tier: () => HudConfig.FxTierA,
                sharedOnly: true, tip: "Shared _EdgeLightSharp uniform: how tight the lit arc is."),
            Flt("ripple", HudFxCategory.Edges, SecEdge, "  irregular energy",
                () => HudConfig.FxEdgeRipple, 0f, 2.5f, paramKey: "ripple", tier: () => HudConfig.FxTierA,
                tip: "Light/dark shimmer running along the lit edge. 0 = smooth.",
                applies: v => v.FxHasEdgeEnergy),
            Flt("rippleFreq", HudFxCategory.Edges, SecEdge, "  energy frequency",
                () => HudConfig.FxEdgeRippleFreq, 0.05f, 8f, paramKey: "rippleFreq",
                tier: () => HudConfig.FxTierA, tip: "Shimmer cycles per ~100 px of edge.",
                applies: v => v.FxHasEdgeEnergy),
            // The "(per-element only…)" suffix is appended by the popup from HasGlobal +
            // StateIndependent, so it is NOT baked into the caption here — one rule, one place.
            // F9 never draws this row (no global to draw), so the caption change is invisible there.
            Own("rippleSmooth", HudFxCategory.Edges, "  energy smoothness", 0f, 1f,
                "rippleSmooth",
                "THE one control that genuinely has no global (plan §1.3). Hard-zeroed while the "
                + "element follows, so it is neutral there rather than silently inherited.",
                applies: v => v.FxHasEdgeEnergy, section: SecEdge),
            Flt("edgeFlow", HudFxCategory.Edges, SecEdge, "  flow speed (0 = frozen)",
                () => HudConfig.FxEdgeFlowSpeed, 0f, 4f, paramKey: "edgeFlow",
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "How fast the shimmer crests travel along the edge. 0 = a frozen pattern.",
                applies: v => v.FxHasEdgeEnergy),
            Bln("rippleDesync", HudFxCategory.Edges, SecEdge, "  Desync per element (break lockstep)",
                () => HudConfig.FxRippleDesync, tier: () => HudConfig.FxTierA, sharedOnly: true,
                tip: "A shared RULE, not a shared value: it derives a stable per-element jitter "
                   + "from the element's Id hash so identical panels stop shimmering in unison."),
            Flt("rippleDesyncAmount", HudFxCategory.Edges, SecEdge, "    desync amount",
                () => HudConfig.FxRippleDesyncAmount, 0f, 1f, master: () => HudConfig.FxRippleDesync,
                tier: () => HudConfig.FxTierA, sharedOnly: true,
                tip: "How far each element's frequency and tempo may wander from the global."),
            Bln("glowFlowAuraOn", HudFxCategory.Edges, SecEdge, "  Flowing edge aura",
                () => HudConfig.FxGlowFlowAuraOn, paramKey: "customGlowFlowOn",
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "Moving edge crests emit their own aura through the shared halo envelope.",
                applies: v => v.FxHasPanel),
            Flt("glowFlowAura", HudFxCategory.Edges, SecEdge, "    aura strength",
                () => HudConfig.FxGlowFlowAura, 0f, 2f, paramKey: "glowFlowAura",
                onParamKey: "customGlowFlowOn", master: () => HudConfig.FxGlowFlowAuraOn,
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "How brightly the travelling crests emit.",
                applies: v => v.FxHasPanel,
                notes: new[]
                {
                    "    Moving edge crests emit through the shared halo radius/spread",
                    "    on the Glow sub-tab. Needs the sharp panel shader.",
                }),

            // ================= GLOW (Effects > Glow, + the Advanced halo rows) ================

            Bln("glowOn", HudFxCategory.Glow, SecHalo, "Glow halo",
                () => HudConfig.FxGlowOn, paramKey: "customGlowOn", tier: () => HudConfig.FxTierA,
                tip: "The halo band around (and into) the plate.", applies: v => v.FxHasHalo),
            Flt("glow", HudFxCategory.Glow, SecHalo, "  outward strength",
                () => HudConfig.FxGlow, 0f, 2f, paramKey: "glow", onParamKey: "customGlowOn",
                master: () => HudConfig.FxGlowOn, tier: () => HudConfig.FxTierA,
                tip: "Halo intensity OUTSIDE the frame.", applies: v => v.FxHasHalo),
            Flt("glowIn", HudFxCategory.Glow, SecHalo, "  inward strength",
                () => HudConfig.FxGlowInner, 0f, 2f, paramKey: "glowIn", onParamKey: "customGlowOn",
                master: () => HudConfig.FxGlowOn, tier: () => HudConfig.FxTierA,
                tip: "Halo intensity INTO the glass, under the text.", applies: v => v.FxHasPanel),
            Flt("glowHaze", HudFxCategory.Glow, SecHalo, "  extended atmospheric haze",
                () => HudConfig.FxGlowHaze, 0f, 1f, paramKey: "glowHaze", onParamKey: "customGlowOn",
                master: () => HudConfig.FxGlowOn, tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "A faint long tail beyond the halo's core falloff.", applies: v => v.FxHasPanel),

            Flt("glowWidth", HudFxCategory.Glow, SecEnvelope, "  Halo / aura radius (px)##glowWidth",
                () => HudConfig.FxGlowWidth, 6f, 320f, paramKey: "glowWidth",
                tier: () => HudConfig.FxTierA,
                tip: "Radius of the envelope the halo AND the flowing aura share. The mesh "
                   + "fallback caps the visible radius at 160 px.",
                applies: v => v.FxHasHalo),
            Flt("glowDiffuse", HudFxCategory.Glow, SecEnvelope, "  spread (tight rim -> diffuse)",
                () => HudConfig.FxGlowDiffuse, 0f, 1f, paramKey: "glowDiffuse",
                tier: () => HudConfig.FxTierA, tip: "0 = a tight rim, 1 = a wide soft haze.",
                applies: v => v.FxHasHalo),
            Flt("glowExtraDiffuse", HudFxCategory.Glow, SecEnvelope, "  extra diffuse (beyond max spread)",
                () => HudConfig.FxGlowExtraDiffuse, 0f, 1f, paramKey: "glowExtraDiffuse",
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "Softness past the spread slider's ceiling, on both glow bands.",
                applies: v => v.FxHasHalo),
            Bln("glowBreathOn", HudFxCategory.Glow, SecEnvelope, "  Halo / aura breathing",
                () => HudConfig.FxGlowBreathOn, paramKey: "customGlowBreathOn",
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "The halo breathes without tinting the element itself.",
                applies: v => v.FxHasPanel),
            Flt("glowBreath", HudFxCategory.Glow, SecEnvelope, "    breath depth",
                () => HudConfig.FxGlowBreath, 0f, 1f, paramKey: "glowBreath",
                onParamKey: "customGlowBreathOn", master: () => HudConfig.FxGlowBreathOn,
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "How far the halo swells and shrinks.", applies: v => v.FxHasPanel),

            Flt("glowBreathSpeed", HudFxCategory.Glow, SecBreathSpeed,
                "Shared Global + Custom breath speed (Hz)",
                () => HudConfig.FxGlowBreathSpeed, 0.03f, 2f, tier: () => HudConfig.FxTierA,
                sharedOnly: true,
                tip: "ONE clock (_UiaHaloBreathWave) for every breathing halo on the HUD.",
                notes: new[] { "  Shared timing stays editable even when only Custom elements breathe." }),

            Flt("pulseSpeed", HudFxCategory.Glow, SecPulseShape, "  pulse speed (Hz)",
                () => HudConfig.FxPulseSpeed, 0.05f, 3f, master: () => HudConfig.FxPulseOn,
                sharedOnly: true,
                tip: "Shape of the breathing PULSE transition. Read straight from the global in "
                   + "ApplyPulse, so it is shared by every element that pulses."),
            Flt("pulseDepth", HudFxCategory.Glow, SecPulseShape, "  pulse depth",
                () => HudConfig.FxPulseDepth, 0f, 1f, master: () => HudConfig.FxPulseOn,
                sharedOnly: true,
                tip: "How deep the dim half of the pulse goes. Shared, like the speed."),

            Bln("glowUnevenOn", HudFxCategory.Glow, SecAdvancedHalo, "Uneven / organic reach",
                () => HudConfig.FxGlowUnevenOn, paramKey: "customGlowUnevenOn",
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "Stable local-space breakup of the halo's reach and energy.",
                applies: v => v.FxHasPanel),
            Flt("glowUneven", HudFxCategory.Glow, SecAdvancedHalo, "  unevenness amount",
                () => HudConfig.FxGlowUneven, 0f, 1f, paramKey: "glowUneven",
                onParamKey: "customGlowUnevenOn", master: () => HudConfig.FxGlowUnevenOn,
                tier: () => HudConfig.FxTierA, sdfOnly: true, tip: "How irregular the reach becomes.",
                applies: v => v.FxHasPanel),
            Flt("glowOrganicScale", HudFxCategory.Glow, SecAdvancedHalo, "  organic scale (1 = classic)",
                () => HudConfig.FxGlowOrganicScale, 0.25f, 4f, paramKey: "glowOrganicScale",
                onParamKey: "customGlowUnevenOn", master: () => HudConfig.FxGlowUnevenOn,
                tier: () => HudConfig.FxTierA, sdfOnly: true,
                tip: "Footprint of the unevenness noise.", applies: v => v.FxHasPanel),

            // ================= BLOOM (Effects > Bloom) — a full-screen post pass ==============
            // Nothing here can ever be per-element: HudBloomFx composites the whole HUD at once.

            Bln("bloomOn", HudFxCategory.Bloom, SecBloomMaster,
                "Enable HUD bloom (HUD elements light each other)",
                () => HudConfig.FxBloomOn, sharedOnly: true, tip: "The full-screen HUD bloom pass."),

            Flt("bloomStrength", HudFxCategory.Bloom, SecBloomBase, "  Strength##bloomBaseStrength",
                () => HudConfig.FxBloomStrength, 0f, 3f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "Additive composite strength of the base glow."),
            Flt("bloomThreshold", HudFxCategory.Bloom, SecBloomBase, "  Bright threshold##bloomBaseThreshold",
                () => HudConfig.FxBloomThreshold, 0f, 1.5f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "Brightness a pixel must reach to bloom at all."),
            Flt("bloomKnee", HudFxCategory.Bloom, SecBloomBase, "  Soft knee##bloomBaseKnee",
                () => HudConfig.FxBloomKnee, 0f, 1f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "Softness of the threshold cutoff."),
            Whole("bloomBlurSteps", HudFxCategory.Bloom, SecBloomBase, "  Reach / blur steps##bloomBaseSteps",
                () => HudConfig.FxBloomBlurSteps, 1, 5, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, doesNotTravel: true,
                tip: "Dual-Kawase pyramid depth. Machine-local: never travels with a theme."),
            Flt("bloomSpread", HudFxCategory.Bloom, SecBloomBase, "  Width fine-adjust##bloomBaseSpread",
                () => HudConfig.FxBloomSpread, 0.5f, 3f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "Continuous width between pyramid steps."),
            Flt("bloomAnamorph", HudFxCategory.Bloom, SecBloomBase,
                "  Streak shape (-1 vertical, +1 horizontal)##bloomBaseAnamorph",
                () => HudConfig.FxBloomAnamorph, -1f, 1f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "Stretches the glow into a vertical or horizontal streak."),
            Flt("bloomSaturation", HudFxCategory.Bloom, SecBloomBase,
                "  Saturation (0 white-hot, 1 source hues)##bloomBaseSaturation",
                () => HudConfig.FxBloomSaturation, 0f, 2f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "0 = white-hot, 1 = the source hues, 2 = oversaturated."),
            Col("bloomTint", HudFxCategory.Bloom, SecBloomBase, "  Glow tint##bloomBaseTint",
                () => HudConfig.FxBloomTint, master: () => HudConfig.FxBloomOn, sharedOnly: true,
                tip: "Multiplied over the base glow."),

            Bln("bloomPulseOn", HudFxCategory.Bloom, SecBloomPulse, "Breathing bloom",
                () => HudConfig.FxBloomPulseOn, master: () => HudConfig.FxBloomOn, sharedOnly: true,
                tip: "A slow breath on the glow strength."),
            Flt("bloomPulseSpeed", HudFxCategory.Bloom, SecBloomPulse, "  Breaths per second##bloomPulseSpeed",
                () => HudConfig.FxBloomPulseSpeed, 0.05f, 2f, master: () => HudConfig.FxBloomPulseOn,
                sharedOnly: true, tip: "Breath rate, Hz."),
            Flt("bloomPulseDepth", HudFxCategory.Bloom, SecBloomPulse, "  Breath depth##bloomPulseDepth",
                () => HudConfig.FxBloomPulseDepth, 0f, 1f, master: () => HudConfig.FxBloomPulseOn,
                sharedOnly: true, tip: "Fraction of the strength breathed away."),

            Bln("bloomReactOn", HudFxCategory.Bloom, SecBloomReact, "State-reactive bloom",
                () => HudConfig.FxBloomReactOn, master: () => HudConfig.FxBloomOn, sharedOnly: true,
                tip: "Let suit power, alarms and the boot sequence drive the glow."),
            Flt("bloomReactPower", HudFxCategory.Bloom, SecBloomReact, "  Low suit power dimming##bloomReactPower",
                () => HudConfig.FxBloomReactPower, 0f, 1f, master: () => HudConfig.FxBloomReactOn,
                sharedOnly: true, tip: "How much low suit power dims the glow."),
            Flt("bloomReactAlarm", HudFxCategory.Bloom, SecBloomReact, "  Critical alarm response##bloomReactAlarm",
                () => HudConfig.FxBloomReactAlarm, 0f, 1f, master: () => HudConfig.FxBloomReactOn,
                sharedOnly: true, tip: "Red alarm pulse on critical states."),
            Flt("bloomReactBoot", HudFxCategory.Bloom, SecBloomReact, "  Boot flare##bloomReactBoot",
                () => HudConfig.FxBloomReactBoot, 0f, 1f, master: () => HudConfig.FxBloomReactOn,
                sharedOnly: true, tip: "Extra flare during the boot sequence."),

            Bln("bloom2On", HudFxCategory.Bloom, SecBloomBand2,
                "Border / highlight bloom (second bright band)",
                () => HudConfig.FxBloom2On, master: () => HudConfig.FxBloomOn, sharedOnly: true,
                tip: "A second, higher-threshold band that catches borders and highlights."),
            Flt("bloom2Threshold", HudFxCategory.Bloom, SecBloomBand2,
                "  Highlight threshold##bloomHighlightThreshold",
                () => HudConfig.FxBloom2Threshold, 0f, 1.5f, master: () => HudConfig.FxBloom2On,
                sharedOnly: true, tip: "Set this between your border brightness and your text."),
            Flt("bloom2Strength", HudFxCategory.Bloom, SecBloomBand2, "  Strength##bloomHighlightStrength",
                () => HudConfig.FxBloom2Strength, 0f, 3f, master: () => HudConfig.FxBloom2On,
                sharedOnly: true, tip: "Composite strength of the highlight band."),
            Whole("bloom2Steps", HudFxCategory.Bloom, SecBloomBand2, "  Reach / blur steps##bloomHighlightSteps",
                () => HudConfig.FxBloom2Steps, 1, 5, master: () => HudConfig.FxBloom2On,
                sharedOnly: true, doesNotTravel: true,
                tip: "The highlight band's own pyramid depth. Machine-local."),
            Flt("bloom2Spread", HudFxCategory.Bloom, SecBloomBand2, "  Width fine-adjust##bloomHighlightSpread",
                () => HudConfig.FxBloom2Spread, 0.5f, 3f, master: () => HudConfig.FxBloom2On,
                sharedOnly: true, tip: "The highlight band's own continuous width."),
            Col("bloom2Tint", HudFxCategory.Bloom, SecBloomBand2, "  Highlight tint##bloomHighlightTint",
                () => HudConfig.FxBloom2Tint, master: () => HudConfig.FxBloom2On, sharedOnly: true,
                tip: "Multiplied over the highlight glow only.",
                notes: new[] { "Raise panel edge light, then set this threshold between borders and text." }),

            Cmb("bloomRes", HudFxCategory.Bloom, SecBloomRes, "  glow resolution",
                () => HudConfig.FxBloomRes, master: () => HudConfig.FxBloomOn, sharedOnly: true,
                doesNotTravel: true,
                tip: "Bright-pass base resolution. Machine-local: never travels with a theme. "
                   + "-1 is the legacy alias that resolves through FxBloomFineDetail."),
            Flt("bloomSatBias", HudFxCategory.Bloom, SecBloomBias,
                "  Colour bias (+ favours coloured pixels)##bloomBaseBias",
                () => HudConfig.FxBloomSatBias, -1f, 1f, master: () => HudConfig.FxBloomOn,
                sharedOnly: true, tip: "Positive favours coloured pixels over neutral ones."),
            Flt("bloom2SatBias", HudFxCategory.Bloom, SecBloomBias,
                "  Colour bias (+ favours borders)##bloomHighlightBias",
                () => HudConfig.FxBloom2SatBias, -1f, 1f, master: () => HudConfig.FxBloom2On,
                sharedOnly: true, tip: "The borders-not-text knob for the highlight band."),

            // ================= ALERTS (Effects > Alerts) ======================================
            // HudAlertPulse reads these globals directly; an element contributes only a stable seed.

            Bln("alertPulseOn", HudFxCategory.Alerts, SecAlertMaster, "Warnings tint and breathe the HUD",
                () => HudConfig.FxAlertPulseOn, sharedOnly: true,
                tip: "The suit-warning pulse. Suited / robot only.",
                notes: new[] { "  Suited / robot only - never in bare mode." }),
            Flt("alertBreathSeconds", HudFxCategory.Alerts, SecAlertShape, "  breath length (sec)##alertBreathSecs",
                () => HudConfig.FxAlertBreathSeconds, 0.35f, 5f, master: () => HudConfig.FxAlertPulseOn,
                sharedOnly: true, tip: "Duration of ONE breath."),
            Whole("alertCautionBreaths", HudFxCategory.Alerts, SecAlertShape, "  caution flashes##alertBreathCount",
                () => HudConfig.FxAlertCautionBreaths, 1, 10, master: () => HudConfig.FxAlertPulseOn,
                sharedOnly: true, tip: "How many flashes a caution runs before it clears."),
            Flt("alertPulseStrength", HudFxCategory.Alerts, SecAlertShape, "  breath strength##alertPulseStrength",
                () => HudConfig.FxAlertPulseStrength, 0f, 1f, master: () => HudConfig.FxAlertPulseOn,
                sharedOnly: true, tip: "Peak of the caution flash and swing of the critical breath."),
            Flt("alertCautionBright", HudFxCategory.Alerts, SecAlertBright, "  caution brightness##alertCautionBright",
                () => HudConfig.FxAlertCautionBright, 0.25f, 3f, master: () => HudConfig.FxAlertPulseOn,
                sharedOnly: true, tip: "Gain on the picked caution colour."),
            Flt("alertCriticalBright", HudFxCategory.Alerts, SecAlertBright, "  critical brightness##alertCriticalBright",
                () => HudConfig.FxAlertCriticalBright, 0.25f, 3f, master: () => HudConfig.FxAlertPulseOn,
                sharedOnly: true, tip: "Gain on the picked critical colour."),
        };

        /// <summary>THE bridge to the power-transition registry (see the type comment). Consumers
        /// asking "what is in the Transitions category" get ONE answer, and it is not a copy.</summary>
        internal static HudTransitionFxDef[] Transitions => HudTransitionFx.All;

        /// <summary>Registry lookup by canonical key. Returns null for an unknown key — callers
        /// fail soft rather than inventing a knob.</summary>
        internal static HudStyleFxDef Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i] != null && string.Equals(All[i].Key, key, StringComparison.Ordinal))
                    return All[i];
            return null;
        }

        /// <summary>Lookup by the PER-ELEMENT param key ("glow", "customFrost"). Used by the
        /// diagnostics and, from Phase 3 on, by the seed/reset loops.</summary>
        internal static HudStyleFxDef FindByParam(string paramKey)
        {
            if (string.IsNullOrEmpty(paramKey)) return null;
            for (int i = 0; i < All.Length; i++)
                if (All[i] != null && string.Equals(All[i].ParamKey, paramKey, StringComparison.Ordinal))
                    return All[i];
            return null;
        }

        /// <summary>True when this row belongs to the given (category, section) pair — the one test
        /// every F9 sub-tab loop runs. Zero-alloc; F9 draws at most a few dozen rows a frame.</summary>
        internal static bool InSection(HudStyleFxDef d, HudFxCategory cat, string section)
            => d != null && d.Category == cat
               && string.Equals(d.Section, section, StringComparison.Ordinal);

        // ---- where F9 draws a section ---------------------------------------------------------
        // The ids are the ones HudEditorWindow's BeginSubTab calls register, so a "jump to the
        // global control" button in the element popup lands on the tab that actually owns the row.
        // They live HERE, with the sections, because the section IS the thing that decides the
        // answer — a section moved in F9 changes exactly one line, right next to its Sec* const.

        internal const string TabTheme = "Theme";
        internal const string TabEffects = "Effects";
        internal const string SubTabThemeBoxes = "##UIAThemeBoxes";
        internal const string SubTabGlass = "##UIAFxGlass";
        internal const string SubTabEdges = "##UIAFxEdges";
        internal const string SubTabGlow = "##UIAFxGlow";
        internal const string SubTabBloom = "##UIAFxBloom";
        internal const string SubTabAlerts = "##UIAFxAlerts";
        internal const string SubTabTransitions = "##UIAFxTransitions";
        internal const string SubTabAdvanced = "##UIAFxAdvanced";

        /// <summary>The F9 tab / sub-tab that owns a row, so the element popup can offer a jump to
        /// the shared control it is naming. Returns false for <see cref="SecElementOnly"/> — those
        /// rows have no global and therefore no home to jump to.</summary>
        internal static bool F9Home(HudStyleFxDef def, out string tab, out string subTabId,
            out string caption)
        {
            tab = TabEffects; subTabId = SubTabGlass; caption = "Glass";
            if (def == null || string.Equals(def.Section, SecElementOnly, StringComparison.Ordinal))
                return false;
            switch (def.Section)
            {
                case SecText:
                case SecBox:
                case SecSdfMaster:
                case SecSdf:
                    tab = TabTheme; subTabId = SubTabThemeBoxes; caption = "Typography & boxes";
                    return true;
                case SecTierA:
                case SecCore:
                case SecBoxEndFade:
                case SecTierB:
                case SecAnimGlass:
                case SecTierC:
                case SecFrost:
                    caption = "Glass"; subTabId = SubTabGlass; return true;
                case SecEdgeMaster:
                case SecEdge:
                    caption = "Edges"; subTabId = SubTabEdges; return true;
                case SecHalo:
                case SecEnvelope:
                case SecBreathSpeed:
                case SecPulseShape:
                    caption = "Glow"; subTabId = SubTabGlow; return true;
                case SecBloomMaster:
                case SecBloomBase:
                case SecBloomPulse:
                case SecBloomReact:
                case SecBloomBand2:
                    caption = "Bloom"; subTabId = SubTabBloom; return true;
                case SecAlertMaster:
                case SecAlertShape:
                case SecAlertBright:
                    caption = "Alerts"; subTabId = SubTabAlerts; return true;
                case SecFrostPerf:
                case SecBloomRes:
                case SecBloomBias:
                case SecAdvancedHalo:
                    caption = "Advanced"; subTabId = SubTabAdvanced; return true;
                default:
                    return true;
            }
        }

        // ---- PER-CATEGORY FOLLOW: storage + resolution (Phase 3, plan §3.2 / §3.5) -----------
        //
        // ONE packed int per element PER STYLE SLOT, two bits per followable category, in the
        // fixed order Surface | Glass | Edges | Glow | Transitions. Default 0 = follow everything =
        // exactly today's Global element, so an absent key costs nothing and means nothing new.
        //
        // Written through HudElementDef.SetIFor so it rides the existing copy-on-write fork
        // protection: a Bare fork can follow a category the Suited base owns, and vice versa.
        //
        // ALLOCATION-FREE BY CONSTRUCTION. Resolution is one param-bag read (the same linear scan
        // + int.Parse the old single "styleSource" read already cost on this path) plus a shift and
        // a mask. No dictionary, no string built, no boxing — these run per element per frame
        // inside ApplyGlass / ApplyMeshFx / ApplyFx. Deliberately NOT cached on the view: the F9
        // popup writes and reads the same value within one frame, and a frame-keyed cache would
        // make a freshly-ticked checkbox disagree with the rows underneath it for a frame.

        /// <summary>Param key holding the packed per-category source. Slot-aware
        /// (<c>SetIFor</c>), so "b_styleSrc" is the bare fork's own copy.</summary>
        internal const string SourceParamKey = "styleSrc";

        /// <summary>The pre-Phase-3 two-state field. Still WRITTEN alongside
        /// <see cref="SourceParamKey"/> for one release so a downgrade to 0.9.2.x still renders
        /// correctly (plan §4.2, "hide never destroy" applied to a schema field); the write is
        /// dropped in Phase 5. Also the fail-soft fallback for a def that <c>Sanitize</c> has not
        /// mapped yet (an element built in memory this session).</summary>
        internal const string LegacySourceParamKey = "styleSource";

        /// <summary>Every category that owns a follow checkbox, in PACKING ORDER. Bloom and Alerts
        /// are absent on purpose: they are physically one full-screen pass and one set of shared
        /// uniforms, so there is nothing per-element to follow (plan §1.6, §3.4).</summary>
        internal static readonly HudFxCategory[] Followable =
        {
            HudFxCategory.Surface, HudFxCategory.Glass, HudFxCategory.Edges,
            HudFxCategory.Glow, HudFxCategory.Transitions,
        };

        /// <summary>Follow everything — the default for every element that stores nothing.</summary>
        internal const int AllGlobalPacked = 0;

        /// <summary>Own everything: <see cref="HudFxSource.Own"/> (2) in all five two-bit fields,
        /// i.e. 0b10_10_10_10_10. This is what a pre-Phase-3 <c>styleSource == 2</c> maps to.</summary>
        internal const int AllOwnPacked = 0x2AA;

        /// <summary>Bit offset of a category's two-bit field, or -1 when the category has no
        /// follow state at all (Bloom / Alerts).</summary>
        internal static int ShiftFor(HudFxCategory cat)
        {
            switch (cat)
            {
                case HudFxCategory.Surface: return 0;
                case HudFxCategory.Glass: return 2;
                case HudFxCategory.Edges: return 4;
                case HudFxCategory.Glow: return 6;
                case HudFxCategory.Transitions: return 8;
                default: return -1;
            }
        }

        internal static bool IsFollowable(HudFxCategory cat) => ShiftFor(cat) >= 0;

        /// <summary>The page/checkbox caption for a category. Surface is called "Theme" everywhere
        /// the player sees it, because that is the F9 tab it mirrors.</summary>
        internal static string CategoryName(HudFxCategory cat)
        {
            switch (cat)
            {
                case HudFxCategory.Surface: return "Theme";
                case HudFxCategory.Glass: return "Glass";
                case HudFxCategory.Edges: return "Edges";
                case HudFxCategory.Glow: return "Glow";
                case HudFxCategory.Bloom: return "Bloom";
                case HudFxCategory.Alerts: return "Alerts";
                default: return "Transitions";
            }
        }

        /// <summary>This slot's packed source word, with the pre-Phase-3 fallback.
        /// Fail-soft: a def with neither key reads as "follow everything".</summary>
        internal static int PackedOf(HudElementDef d, HudStyleSlot slot)
        {
            if (d == null) return AllGlobalPacked;
            int v = d.GetIFor(slot, SourceParamKey, -1);
            if (v >= 0) return v;
            // Not mapped yet (a brand-new in-memory element, or a document that somehow skipped
            // Sanitize): read the legacy two-state field so behaviour is unchanged until the
            // migration runs. Custom => own everything, anything else => follow everything.
            return d.GetIFor(slot, LegacySourceParamKey, HudElementView.StyleGlobal)
                   == HudElementView.StyleCustom ? AllOwnPacked : AllGlobalPacked;
        }

        /// <summary>Unpack ONE category out of an already-read word. Two bit ops, no allocation.</summary>
        internal static HudFxSource SourceIn(int packed, HudFxCategory cat)
        {
            int shift = ShiftFor(cat);
            if (shift < 0) return HudFxSource.Global;
            int v = (packed >> shift) & 3;
            // Donor (1) is Phase 4 and 3 is illegal: both degrade to Global, never to a neutral.
            return v == (int)HudFxSource.Own ? HudFxSource.Own : HudFxSource.Global;
        }

        /// <summary>THE resolution rule (plan §3.2), asked once per resolver: Global => the global
        /// value; Own => the element's stored param with THE GLOBAL as its default (the one
        /// missing-key convention Phase 0b established); Donor => Global until Phase 4.
        ///
        /// TRANSITIONS ALWAYS RESOLVE AGAINST <see cref="HudStyleSlot.Base"/>, whatever slot the
        /// caller asks for. The seven power transitions are deliberately NOT per-tier
        /// (<c>PerTierCapable = false</c>): forking them would let the suit and bare layouts
        /// disagree about whether an element dies at all, so <c>HudTransitionFx.SetMode/SetAmount</c>
        /// write raw base keys. Their FOLLOW state has to live in the same place or the popup's
        /// checkbox and the rows underneath it desync on a bare-forked element — the checkbox would
        /// write "b_styleSrc" while every row it governs reads the base.</summary>
        internal static HudFxSource SourceOf(HudElementDef d, HudFxCategory cat, HudStyleSlot slot)
        {
            if (ShiftFor(cat) < 0) return HudFxSource.Global;
            return SourceIn(PackedOf(d, SlotFor(cat, slot)), cat);
        }

        /// <summary>The slot a category's follow state actually lives in: Base for Transitions
        /// (see <see cref="SourceOf"/>), the caller's slot for everything else. Every reader AND
        /// every writer must go through this, or they will disagree.</summary>
        internal static HudStyleSlot SlotFor(HudFxCategory cat, HudStyleSlot slot)
            => cat == HudFxCategory.Transitions ? HudStyleSlot.Base : slot;

        /// <summary>Replace one category's two bits. Returns the word unchanged for a category
        /// with no follow state.</summary>
        internal static int WithSource(int packed, HudFxCategory cat, HudFxSource src)
        {
            int shift = ShiftFor(cat);
            if (shift < 0) return packed;
            return (packed & ~(3 << shift)) | (((int)src & 3) << shift);
        }

        /// <summary>True when EVERY followable category follows the globals — the state the legacy
        /// <see cref="LegacySourceParamKey"/> records as 1, and what the master checkbox shows
        /// ticked. Word-only overload: callers that have an ELEMENT must use the overload below,
        /// which routes Transitions through its own slot (see <see cref="SlotFor"/>).</summary>
        internal static bool AllFollow(int packed)
        {
            for (int i = 0; i < Followable.Length; i++)
                if (SourceIn(packed, Followable[i]) == HudFxSource.Own) return false;
            return true;
        }

        /// <summary>True when NO followable category follows — the master checkbox shows unticked
        /// (anything between the two is "(mixed)").</summary>
        internal static bool NoneFollow(int packed)
        {
            for (int i = 0; i < Followable.Length; i++)
                if (SourceIn(packed, Followable[i]) != HudFxSource.Own) return false;
            return true;
        }

        /// <summary>Element-aware "does every family follow?" — asks <see cref="SourceOf"/> per
        /// category so Transitions is read from Base even when <paramref name="slot"/> is a fork.
        /// The word-only overloads would report a fork's stale Transitions bits, which nothing
        /// reads.</summary>
        internal static bool AllFollow(HudElementDef d, HudStyleSlot slot)
        {
            for (int i = 0; i < Followable.Length; i++)
                if (SourceOf(d, Followable[i], slot) == HudFxSource.Own) return false;
            return true;
        }

        internal static bool NoneFollow(HudElementDef d, HudStyleSlot slot)
        {
            for (int i = 0; i < Followable.Length; i++)
                if (SourceOf(d, Followable[i], slot) != HudFxSource.Own) return false;
            return true;
        }

        /// <summary>One letter per category for the `hudfx` dump: G(lobal) / D(onor) / O(wn).</summary>
        internal static char SourceLetter(HudFxSource s)
            => s == HudFxSource.Own ? 'O' : s == HudFxSource.Donor ? 'D' : 'G';

        /// <summary>How many rows a category owns. Diagnostics only.</summary>
        internal static int CountIn(HudFxCategory cat)
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++) if (All[i] != null && All[i].Category == cat) n++;
            return n;
        }
    }
}
