using System;
using System.Reflection;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// PROFILE-AWARE SORT (design O4e): when a bag with an assigned profile is sorted, its
    /// profile-matched items come FIRST — ordered by match tier (explicit item rule &gt; slot
    /// class &gt; category, exactly the <see cref="BagProfile.Match"/> ranking the router uses) —
    /// and the game's own comparator breaks every tie, so unmatched items keep their vanilla
    /// A-by-slot-class-then-name order after the profiled block.
    ///
    /// <para><b>Mechanism — ONLY the comparator changes.</b> Vanilla sorting runs entirely inside
    /// <c>OnServer.SortContents(Thing)</c> (decompile OnServer.cs:529): stack consolidation, then
    /// <c>list3.Sort(Item.SmartSortItems)</c> (OnServer.cs:596) and the move-out/move-back pass.
    /// <c>Item.SmartSortItems</c> is a <c>public static readonly Comparison&lt;Item&gt;</c> FIELD
    /// (decompile Item.cs:890 — slot-class name, then DisplayName, then quantity), loaded fresh
    /// from the field at every call site. This class swaps that field for a wrapper via
    /// reflection at plugin init and RESTORES it on shutdown. The execution, pacing and funnel
    /// mechanics of sort are untouched — same one <c>SortContentsMessage</c> from a client, same
    /// server-side move sequence, same gates; the server merely consults our comparison delegate
    /// while ordering. When the bag has no (resolvable) profile, or the feature is toggled off,
    /// the wrapper defers to the captured vanilla delegate PER COMPARE, so behaviour is
    /// bit-identical to stock.</para>
    ///
    /// <para><b>Where it applies.</b> <c>OnServer.SortContents</c> runs where the simulation runs:
    /// single-player and the hosting player. On a multiplayer CLIENT the sort executes on the
    /// server, so an unmodded server keeps vanilla order — the feature degrades to stock, never
    /// desyncs (the server's result is authoritative and replicated either way). On our own
    /// hosted server, a bag with no assignment in OUR store (e.g. a guest's bag) compares pure
    /// vanilla.</para>
    ///
    /// <para><b>Safety.</b> The comparator is a pure function over networked reads (ParentSlot,
    /// PrefabName, SlotType, SortingClass); it mutates nothing. Comparison-contract note: within
    /// one sort every item shares the same parent container (the list is built from that thing's
    /// slots, and items are only moved out AFTER the Sort call — OnServer.cs:596-604), so one
    /// profile consistently ranks the whole list and transitivity holds. Reflection-writing an
    /// initonly static is supported on Unity's Mono; if the runtime refuses, Install fail-softs
    /// and sort stays vanilla. Hot-reload: <see cref="Reset"/> restores the original delegate
    /// (only when the field still holds OUR wrapper, so another mod's swap is never clobbered)
    /// and drops the statics — keeping only the captured vanilla delegate, which points at game
    /// code and lets a wrapper stranded inside another mod's chain still defer to vanilla.</para>
    /// </summary>
    public static class ProfileSort
    {
        private static FieldInfo _field;
        private static Comparison<Item> _original;
        private static Comparison<Item> _wrapper;
        private static bool _installed;

        /// <summary>Swap <c>Item.SmartSortItems</c> for the profile-aware wrapper. Idempotent;
        /// fail-soft (a refused reflection write just leaves sort vanilla, logged once).</summary>
        public static void Install()
        {
            if (_installed) return;
            try
            {
                _field = typeof(Item).GetField("SmartSortItems", BindingFlags.Public | BindingFlags.Static);
                var current = _field != null ? _field.GetValue(null) as Comparison<Item> : null;
                if (current == null)
                {
                    UIALog.Warn("ProfileSort: Item.SmartSortItems not found - profile-aware sort disabled (vanilla sort unaffected).");
                    _field = null;
                    return;
                }
                _original = current;
                _wrapper = CompareProfileAware;
                _field.SetValue(null, _wrapper);
                _installed = true;
                UIALog.Info("ProfileSort: profile-aware sort comparator installed.");
            }
            catch (Exception e)
            {
                UIALog.Warn("ProfileSort unavailable (vanilla sort unaffected): " + e.Message);
                _field = null;
                _original = null;
                _wrapper = null;
            }
        }

        /// <summary>Hot-reload/shutdown teardown: put the vanilla delegate back — but only if the
        /// field still holds OUR wrapper (another mod may have wrapped on top of us; clobbering it
        /// would drop their behaviour). Clears the statics — EXCEPT <see cref="_original"/>, which
        /// deliberately survives: in the declined (foreign-wrapper) branch the other mod captured
        /// our wrapper as ITS inner comparator and keeps calling it forever (assemblies never
        /// unload), and the stranded wrapper's per-compare fallback reads <c>_original</c> — nulling
        /// it would collapse every tie to 0 (arbitrary order) instead of deferring to vanilla.
        /// Keeping it costs nothing: it is a delegate over GAME code (Assembly-CSharp), so it pins
        /// nothing from this assembly across the reload.</summary>
        public static void Reset()
        {
            try
            {
                if (_installed && _field != null)
                {
                    var current = _field.GetValue(null) as Comparison<Item>;
                    if (ReferenceEquals(current, _wrapper))
                        _field.SetValue(null, _original);
                }
            }
            catch { }
            _installed = false;
            _field = null;
            _wrapper = null;
        }

        /// <summary>The wrapped comparison. Vanilla-first design: any doubt (feature off, no bag,
        /// no profile, an exception) defers to the captured vanilla delegate, so the ONLY case
        /// that differs from stock is "both items sit in a bag whose assigned profile matched at
        /// least one of them differently". Sort is a click-frequency action, so the per-compare
        /// profile match (a few rule scans) is comfortably cheap. Deliberately NO cached
        /// bag/profile statics — a cache would strand a Thing reference from a dead world across
        /// saves/reloads for a micro-optimisation sort does not need.
        ///
        /// <para><b>Simple mode (0.9.8.0 plan §9.2/§10.2):</b> this defers to vanilla on EVERY
        /// compare, same as the existing "no resolvable profile" deference — the wrapper stays
        /// installed either way (no per-flip install/uninstall churn), it just never finds a
        /// profile to prefer. <see cref="SimpleModeActive"/> is fail-soft to "not Simple" so a
        /// config hiccup can never silently switch a Complex player's bags to vanilla order.</para></summary>
        private static int CompareProfileAware(Item a, Item b)
        {
            var vanilla = _original;
            try
            {
                if (a != null && b != null && ConfigOn() && !SimpleModeActive())
                {
                    Thing bag = OwnerOf(a);
                    if (bag == null) bag = OwnerOf(b);
                    if (bag != null)
                    {
                        BagProfile profile = BagProfileStore.GetAssignedProfile(bag);
                        if (profile != null)
                        {
                            int ma = MatchValue(profile, a);
                            int mb = MatchValue(profile, b);
                            if (ma != mb) return mb.CompareTo(ma);   // higher match tier first
                        }
                    }
                }
            }
            catch { }
            return vanilla != null ? vanilla(a, b) : 0;
        }

        /// <summary>The container being sorted: the item's parent slot's owning Thing. During
        /// <c>OnServer.SortContents</c> the Sort runs BEFORE items are moved out, so ParentSlot
        /// is live for every item in the list (decompile OnServer.cs:588-604).</summary>
        private static Thing OwnerOf(Item item)
        {
            var slot = item.ParentSlot;
            return slot != null ? slot.Parent : null;
        }

        /// <summary>Profile match value for ordering; unmatched sorts below every match (-1 &lt;
        /// the smallest real Match result, which is the category rules' plain priority).</summary>
        private static int MatchValue(BagProfile profile, Item item)
        {
            int? m = profile.Match(item);
            return m.HasValue ? m.Value : -1;
        }

        private static bool ConfigOn()
        {
            try
            {
                return UIAConfig.MasterEnable != null && UIAConfig.MasterEnable.Value
                    && UIAConfig.StowProfileSort != null && UIAConfig.StowProfileSort.Value;
            }
            catch { return false; }
        }

        /// <summary>Is Smart Stow in SIMPLE mode right now (0.9.8.0 plan §9.2)? Fail-soft to
        /// "not Simple" — <see cref="StowModeConfig.Available"/> false, or any hiccup reading it,
        /// keeps today's Complex profile-aware compare running unchanged (rule 5 of this change:
        /// a config hiccup must never flip Complex behaviour).</summary>
        private static bool SimpleModeActive()
        {
            try { return StowModeConfig.Available && StowModeConfig.Mode == StowMode.Simple; }
            catch { return false; }
        }
    }
}
