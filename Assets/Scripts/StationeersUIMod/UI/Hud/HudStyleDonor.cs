using System;
using System.Collections.Generic;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>Why a category in state <see cref="HudFxSource.Donor"/> resolves the way it does.
    /// Every value except <see cref="Ok"/> means "this category falls back to the GLOBAL" — never
    /// to a neutral, never to a blank element (plan §5.2).</summary>
    internal enum HudDonorState
    {
        /// <summary>The category is not in Donor state at all.</summary>
        NotDonor,
        /// <summary>A legal donor that OWNS this category: its stored values are what we read.</summary>
        Ok,
        /// <summary>No <c>styleDonor</c> stored for this slot.</summary>
        None,
        /// <summary>The stored Id names no element in the document (deleted, or a bad hand edit).</summary>
        Missing,
        /// <summary>The element points at itself.</summary>
        Self,
        /// <summary>DEPTH-1 VIOLATION: the donor is itself inheriting this category from someone
        /// else (plan §5.4, FlorpyDorp's decision 1).</summary>
        Depth,
        /// <summary>Legal donor, but it FOLLOWS the globals for this category — so following it
        /// and following the global are the same answer, and we take the global.</summary>
        FollowsGlobal,
    }

    /// <summary>
    /// INHERIT-FROM-ANOTHER-ELEMENT (style-parity Phase 4, plan §5): the donor reference, the
    /// Id -> def index the per-frame resolvers use, the depth-1 rule, and the <c>Sanitize</c>
    /// sweep that clears a dangling reference.
    ///
    /// THE MODEL. A follower stores its donor's <c>Id</c> (a GUID, guaranteed unique and never
    /// renamed by <c>HudDocument.Sanitize</c>) in the slot-aware, PER-CATEGORY param
    /// <see cref="DonorParamKeyFor"/>,
    /// and puts the categories that should follow it into <see cref="HudFxSource.Donor"/>. It is a
    /// REFERENCE, not a copy: the donor's values are read every frame through the same resolvers,
    /// so editing the donor ripples to every follower immediately. (The one-shot alternative —
    /// "copy this category in and then diverge" — ships too, as the popup's "Copy ... from" button,
    /// and it stores no reference at all.)
    ///
    /// PRECEDENCE, per category: <c>Own &gt; Donor &gt; Global</c>. A Donor category resolves
    /// through the donor's OWN values when the donor owns that category; in every other case —
    /// donor missing, donor is itself a follower (depth), donor points at us, or the donor simply
    /// follows the globals — it degrades to the GLOBAL. That is the fail-soft landing zone the
    /// plan insists on: never a neutral, never a blank element.
    ///
    /// ALLOCATION-FREE ON THE DRAW PATH. Resolution is one string read out of the param bag plus
    /// one dictionary hit; the Id -> def map is rebuilt only when the document identity changes
    /// (a profile switch, an undo/redo — both replace <c>HudProfileStore.Active</c>) or its element
    /// COUNT changes (an add, a delete, a duplicate), plus an explicit <see cref="Invalidate"/> the
    /// editor calls after a structural edit. Nothing scans the element list per frame.
    ///
    /// HOT-RELOAD. The only state here is that cache; <see cref="Shutdown"/> drops it, and nothing
    /// registers a callback or holds a Unity object, so the type goes with the assembly on F6.
    /// </summary>
    internal static class HudStyleDonor
    {
        /// <summary>Param key holding the donor element's Id, PER CATEGORY.
        ///
        /// One key per family, not one per element: the four donor-capable families are chosen
        /// INDEPENDENTLY, so a single shared key made "inherit Glass from A" and then "inherit Glow
        /// from C" silently re-point Glass at C — a donor the Glass pick never validated. The
        /// category number is part of the key instead, which makes that state unrepresentable.
        ///
        /// SLOT-AWARE (written with <c>SetSFor</c>), so the base value is "the same donor for every
        /// tier" for free and a per-tier fork MAY store its own — exactly like every other style
        /// key. No migration: this key has never shipped in a public build or a saved profile.</summary>
        internal static string DonorParamKeyFor(HudFxCategory cat) => DonorKeyPrefix + (int)cat;

        /// <summary>The shared stem of every per-category donor key. Only <c>Sweep</c> and the
        /// diagnostics have any business with the stem itself.</summary>
        internal const string DonorKeyPrefix = "styleDonor";

        // ---- the Id -> def index ------------------------------------------------------------

        private static HudDocument _doc;
        private static int _count = -1;
        private static int _stampSeen = -1;
        private static int _stamp;
        private static readonly Dictionary<string, HudElementDef> _byId =
            new Dictionary<string, HudElementDef>(StringComparer.Ordinal);

        /// <summary>Degradations already reported, keyed on FOLLOWER Id + category — an element
        /// whose Glass AND Glow are both broken has two things wrong with it and deserves two
        /// lines. Cleared with the index, i.e. once per document load / structural mutation, which
        /// is what turns "log the degradation" into one line instead of one line per frame.
        ///
        /// The key is built ONLY on the path that is about to log (see <see cref="WarnOnce"/>'s
        /// early-outs), so the steady per-frame degraded path allocates nothing.</summary>
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Followers with at least one already-reported degradation. Checked FIRST, on the
        /// def's own Id string, so the common repeat case never builds a key.</summary>
        private static readonly HashSet<string> _warnedAny = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Force the next lookup to rebuild. Called after a structural edit that does not
        /// change the element COUNT or the document identity (a re-pointed donor, a pasted Id).</summary>
        internal static void Invalidate() { _stamp++; }

        /// <summary>The current invalidation stamp. Anything else that caches a DERIVED view of the
        /// document (the popup's donor dropdown) keys on this so there is one invalidation signal,
        /// not two that can disagree.</summary>
        internal static int Stamp { get { return _stamp; } }

        /// <summary>Hot-reload/teardown reset (CLAUDE.md: every new static resets in the relevant
        /// Shutdown). Drops the document reference, the index and the warned set.</summary>
        internal static void Shutdown()
        {
            _doc = null;
            _count = -1;
            _stampSeen = -1;
            _stamp = 0;
            _byId.Clear();
            _warned.Clear();
            _warnedAny.Clear();
        }

        /// <summary>Can this category be inherited from another element at all?
        ///
        /// TRANSITIONS CANNOT. The seven power transitions resolve on
        /// <see cref="HudStyleSlot.Base"/> for every tier (<c>HudStyleFx.SlotFor</c>) and their
        /// storage is <c>HudTransitionFx</c>'s own tri-state rather than a registry row, so a
        /// donor category there would have to mirror a second model with different slot rules for
        /// no gain the "Copy from" button does not already give. The popup omits the option on the
        /// Transitions page and says so; this predicate is the single place that decides.</summary>
        internal static bool IsDonorCapable(HudFxCategory cat)
            => HudStyleFx.IsFollowable(cat) && cat != HudFxCategory.Transitions;

        private static void EnsureIndex()
        {
            HudDocument doc = null;
            try { doc = Features.HudProfileStore.Active; } catch { }
            int n = doc != null && doc.Elements != null ? doc.Elements.Count : 0;
            if (ReferenceEquals(doc, _doc) && n == _count && _stamp == _stampSeen) return;

            _doc = doc;
            _count = n;
            _stampSeen = _stamp;
            _byId.Clear();
            _warned.Clear();
            _warnedAny.Clear();
            if (doc == null || doc.Elements == null) return;
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var e = doc.Elements[i];
                if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                _byId[e.Id] = e;                 // last one wins; Sanitize guarantees uniqueness
            }
        }

        /// <summary>The live element with this Id, or null. Cached — see the type comment.</summary>
        internal static HudElementDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureIndex();
            HudElementDef d;
            return _byId.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>The donor Id stored for one category/slot (with the base fallback every other
        /// slot-aware read has). Null when this category inherits from nobody.</summary>
        internal static string DonorIdOf(HudElementDef follower, HudFxCategory cat, HudStyleSlot slot)
        {
            if (follower == null || !IsDonorCapable(cat)) return null;
            return follower.GetSFor(HudStyleFx.SlotFor(cat, slot), DonorParamKeyFor(cat), null);
        }

        /// <summary>THE donor resolution, at the DEF level (plan §5.1/§5.2/§5.6).
        ///
        /// Deliberately def-only: the donor may have NO live view at all (it is hidden in the
        /// current tier, or its widget failed to build), and a follower must still inherit from it.
        /// So this reads the donor's stored params + its own <c>styleSrc</c> word and nothing else —
        /// the same def-only convention <c>SnapshotDefRow</c> established in Phase 3.
        ///
        /// PER-TIER (§5.6): the caller passes the slot IT is resolving, and every read on the donor
        /// goes through the donor's own <c>ResolveSlot</c> (that is what <c>GetXFor(slot, …)</c>
        /// does), so "bare follows bare" with no extra storage.
        ///
        /// DEPTH 1 (§5.4, decision 1): a donor that is ITSELF in Donor state for this category is
        /// not a legal donor. There is no graph to walk and therefore no cycle to detect — one hop,
        /// one dictionary lookup, no allocation, no visited set on a per-frame path.</summary>
        internal static HudDonorState Evaluate(HudElementDef follower, HudFxCategory cat,
            HudStyleSlot slot, out HudElementDef donor)
        {
            donor = null;
            if (follower == null || !IsDonorCapable(cat)) return HudDonorState.NotDonor;
            var slotFor = HudStyleFx.SlotFor(cat, slot);
            if (HudStyleFx.SourceOf(follower, cat, slot) != HudFxSource.Donor)
                return HudDonorState.NotDonor;

            string id = follower.GetSFor(slotFor, DonorParamKeyFor(cat), null);
            if (string.IsNullOrEmpty(id)) return HudDonorState.None;
            if (string.Equals(id, follower.Id, StringComparison.Ordinal)) return HudDonorState.Self;

            var d = Find(id);
            if (d == null) return HudDonorState.Missing;

            var src = HudStyleFx.SourceOf(d, cat, slot);
            if (src == HudFxSource.Donor) return HudDonorState.Depth;
            if (src != HudFxSource.Own) return HudDonorState.FollowsGlobal;
            donor = d;
            return HudDonorState.Ok;
        }

        /// <summary>The def a Donor category reads its stored values from, or NULL when it degrades
        /// to the globals. The degrade paths log ONE line per follower per document version (see
        /// <see cref="_warned"/>) — a per-frame resolver must never spam the log.</summary>
        internal static HudElementDef Resolve(HudElementDef follower, HudFxCategory cat,
            HudStyleSlot slot)
        {
            HudElementDef donor;
            var state = Evaluate(follower, cat, slot, out donor);
            if (state == HudDonorState.Ok) return donor;
            if (state == HudDonorState.NotDonor || state == HudDonorState.FollowsGlobal) return null;
            WarnOnce(follower, cat, state);
            return null;
        }

        private static void WarnOnce(HudElementDef follower, HudFxCategory cat, HudDonorState state)
        {
            if (follower == null || string.IsNullOrEmpty(follower.Id)) return;
            EnsureIndex();
            // A view built against the PREVIOUS document (the frame or two between a profile switch
            // and the view rebuild) would report every donor as missing. That is not a broken
            // profile, so it is not a warning: only elements of the LIVE document can be degraded.
            if (!_byId.ContainsKey(follower.Id)) return;
            // Cheap gate first, on the def's OWN string: once this element has reported anything,
            // the steady per-frame path costs one hash lookup and allocates nothing. Only the rare
            // "this element has a SECOND broken family" case builds the composite key.
            if (_warnedAny.Contains(follower.Id))
            {
                if (!_warned.Add(follower.Id + "|" + (int)cat)) return;
            }
            else
            {
                _warnedAny.Add(follower.Id);
                _warned.Add(follower.Id + "|" + (int)cat);
            }
            string why;
            switch (state)
            {
                case HudDonorState.Missing:
                    why = "its donor element no longer exists"; break;
                case HudDonorState.Self:
                    why = "it points at itself"; break;
                case HudDonorState.Depth:
                    why = "its donor is itself inheriting that family (only one hop is allowed)"; break;
                default:
                    why = "it stores no donor"; break;
            }
            UIALog.Warn("HudStyleDonor: element '" + follower.Id + "' cannot inherit "
                + HudStyleFx.CategoryName(cat) + " - " + why
                + ". That family falls back to the F9 globals.");
        }

        /// <summary>What the popup and <c>hudfx</c> print for a donor: the element TYPE plus a short
        /// Id. Element Ids are GUIDs (or the shipped profiles' hand-written slugs) and there is no
        /// user-visible NAME field on <see cref="HudElementDef"/>, so this is the whole identity a
        /// player can be shown — the same shape the F9 element list uses.</summary>
        internal static string LabelOf(HudElementDef d)
        {
            if (d == null) return "(missing element)";
            return d.Type + " " + ShortId(d.Id);
        }

        internal static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(no id)";
            return id.Length <= 10 ? id : id.Substring(0, 8);
        }

        /// <summary>One line describing a category's donor state, for the popup header and the
        /// `hudfx` dump. Never throws.</summary>
        internal static string DescribeState(HudDonorState state, HudElementDef donor, string id)
        {
            switch (state)
            {
                case HudDonorState.Ok: return "inheriting from " + LabelOf(donor);
                case HudDonorState.FollowsGlobal:
                    return "donor " + LabelOf(donor) + " follows the globals - so does this";
                case HudDonorState.Missing:
                    return "donor " + ShortId(id) + " no longer exists - using the globals";
                case HudDonorState.Self: return "points at itself - using the globals";
                case HudDonorState.Depth:
                    return "donor " + ShortId(id) + " is itself inheriting - using the globals";
                case HudDonorState.None: return "no donor stored - using the globals";
                default: return "not inheriting";
            }
        }

        // ---- Sanitize sweep (plan §4.2 / §5.5) -----------------------------------------------

        /// <summary>Clear DANGLING and SELF donor references, and drop the categories that pointed
        /// at them back to Global. Returns the number of repairs so <c>Sanitize</c> can log one
        /// aggregated line and mark the profile for write-back.
        ///
        /// A DEPTH violation is deliberately NOT swept: it is not a broken reference but a state
        /// that appears and disappears as the DONOR's own source changes, so freezing the
        /// follower's category to Global on load would silently destroy a design that becomes legal
        /// again the moment the donor is detached. It degrades at RESOLVE time instead (plan §5.4).
        ///
        /// SLOT ORDER IS BASE FIRST, then the forks — the opposite of the Phase 3 migration's, and
        /// on purpose. Repairing the base runs <c>ProtectForks</c>, which hands every fork that has
        /// no word of its own the base's OLD (donor-bearing) word; the fork pass immediately after
        /// repairs exactly those, so the sweep settles in ONE load instead of two.</summary>
        internal static int Sweep(HudDocument doc)
        {
            if (doc == null || doc.Elements == null) return 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var e = doc.Elements[i];
                if (e != null && !string.IsNullOrEmpty(e.Id)) ids.Add(e.Id);
            }

            int repaired = 0;
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var el = doc.Elements[i];
                if (el == null) continue;
                repaired += SweepSlot(el, HudStyleSlot.Base, ids);
                if (el.ForksSlot(HudStyleSlot.Bare)) repaired += SweepSlot(el, HudStyleSlot.Bare, ids);
                if (el.ForksSlot(HudStyleSlot.Robot)) repaired += SweepSlot(el, HudStyleSlot.Robot, ids);
            }
            return repaired;
        }

        /// <summary>Sweep ONE slot, one CATEGORY AT A TIME. Per category because the donor
        /// reference is per category: a dangling Glass donor must drop Glass to Global and leave a
        /// perfectly good Glow inheritance alone.</summary>
        private static int SweepSlot(HudElementDef el, HudStyleSlot slot, HashSet<string> ids)
        {
            int packed = HudStyleFx.PackedOf(el, slot);
            int fixedPacked = packed;
            int repaired = 0;

            for (int i = 0; i < HudStyleFx.Followable.Length; i++)
            {
                var cat = HudStyleFx.Followable[i];
                if (!IsDonorCapable(cat)) continue;
                string key = DonorParamKeyFor(cat);
                bool isDonor = HudStyleFx.SourceIn(packed, cat) == HudFxSource.Donor;
                // PRESENCE, per slot. HasSlotOverride is a RAW prefixed-key test and answers false
                // for Base by construction (FindSlotOverride bails on SlotPrefixChar '\0'), so
                // asking it about the base slot would report every dangling base key as absent —
                // the sweep would then count a repair it never performed.
                bool storesKey = slot == HudStyleSlot.Base
                    ? el.GetS(key, null) != null
                    : el.HasSlotOverride(slot, key);
                if (!isDonor && !storesKey) continue;

                string id = el.GetSFor(slot, key, null);
                bool bad = string.IsNullOrEmpty(id)
                    || string.Equals(id, el.Id, StringComparison.Ordinal)
                    || !ids.Contains(id);
                if (!bad) continue;

                if (storesKey && !string.IsNullOrEmpty(id))
                {
                    // Written RAW rather than through SetSFor: the value is dangling for every slot
                    // that can see it, so the copy-on-write would only stamp the same dead Id into
                    // the forks (as an empty string, which reads as dangling anyway).
                    el.Set(HudElementDef.SlotPrefix(slot) + key, null);
                    repaired++;
                }
                if (isDonor)
                {
                    fixedPacked = HudStyleFx.WithSource(fixedPacked, cat, HudFxSource.Global);
                    repaired++;
                }
            }

            if (fixedPacked != packed)
            {
                el.SetIFor(slot, HudStyleFx.SourceParamKey, fixedPacked);
                // TODO(one release after 0.9.2.5): stop writing styleSource — see
                // HudElementView.WriteSourceBits, which this pair mirrors. Kept for ONE release
                // so a downgrade to 0.9.2.x still reads a swept element correctly (plan §4.2).
                el.SetIFor(slot, HudStyleFx.LegacySourceParamKey,
                    HudStyleFx.AllFollow(fixedPacked)
                        ? HudElementView.StyleGlobal : HudElementView.StyleCustom);
            }
            return repaired;
        }
    }
}
