using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Grid; // GridModel.IsStorageContainer — the ONE shared "is real storage" gate

namespace StationeersUIMod.Core
{
    /// <summary>Which rung of the SmartStow+ chain produced a candidate. Order here IS the
    /// resolution order (spec: Documentation/SmartStow-Rework-Design-Options.md, O1/O2;
    /// Home: Documentation/0.9.8.0/SmartStow-Simple-Refactor-Plan.md par.6). Nothing persists
    /// these ordinals - renumbering is safe.</summary>
    public enum StowStage
    {
        None = 0,
        Home,            // 0: the item's own remembered home (Simple mode 0.9.8.0; opt-in first in Complex)
        BeltTool,        // 1: GENUINE tool -> directly-worn belt (home-slot aware) -> worn back container
        StackMerge,      // 2: top up an existing partial stack anywhere accessible (+ #13 continuation)
        FunctionalSocket,// 3: component (canister/battery/filter/...) -> a matching EMPTY socket (#11)
        Profile,         // 4: bag whose explicitly ASSIGNED profile matches the item
        Affinity,        // 5: content affinity: bag already holding similar items (no profile needed)
        BagDefault,      // 6: bag-type default: mining belt -> "Ores", tool belt -> "Tools", ...
        Memory,          // 7: per-save item-type memory (where this type last went)
        GenericFallback, // 8: deterministic generic-bag home for None-typed items (#12)
        BeltFallback,    // 9: FALSE tool (cable coil) -> a bare toolbelt slot, LAST resort (#14)
    }

    /// <summary>One possible stow destination, as decided (never executed) by the router.</summary>
    public struct StowCandidate
    {
        public Slot Slot;       // destination slot (for StackMerge: the slot holding the target stack)
        public Thing Holder;    // the bag/container that owns the slot
        public StowStage Stage;
        public string Reason;   // short human string (ASCII only; may reach TMP one day)
        public int Score;       // stage-specific: profile match priority / affinity score; else 0
        public int Depth;       // container nesting depth (0 = worn directly on the human)
    }

    /// <summary>
    /// The SmartStow+ DECISION engine (design doc O1): a staged resolver that turns "the held
    /// item + the reachable inventory" into an ordered list of stow candidates. PURE DECISION —
    /// the router never mutates game state; execution (one OnServer.MoveToSlot / Thing.Merge,
    /// re-gated at execute time) stays in <see cref="Features.SmartStowPlus"/>. Because every
    /// consumer (the G key, the stowtrace diagnostic, future ghost hints) resolves through this
    /// one class, behaviour can never drift between surfaces.
    ///
    /// Two chains since 0.9.8.0 (StowModeConfig.Mode, SmartStow-Simple-Refactor-Plan par.6):
    /// SIMPLE = home -> belt tools -> stack merge -> socket -> affinity -> generic fallback ->
    /// belt last-resort (fixed, per-stage toggles ignored, every bag unprofiled); COMPLEX =
    /// today's chain byte-identical - belt tools -> stack merge -> socket -> profile -> affinity
    /// -> bag default -> type memory -> generic -> belt last-resort - with an opt-in home-first
    /// stage (P5a). In Complex, stages gate themselves on their UIAConfig toggles so a dry run
    /// always mirrors what execution would do.
    ///
    /// One InventoryScanner.Scan per resolve (lazy — the belt stage needs none), shared across
    /// stages. Pooled static lists, cleared per use; <see cref="Reset"/> runs on mod shutdown so
    /// a hot reload never holds Slot/Thing references into a dead world.
    /// </summary>
    public static class StowRouter
    {
        // ---- affinity scoring (spec O2a): per candidate bag with a free compatible slot ----
        private const int AffinityPrefabPoints = 9;   // same prefab already inside
        private const int AffinityPrefabCap = 45;     // ...at most 5 counted
        private const int AffinityClassPoints = 3;    // same Slot.Class (skip None)
        private const int AffinityClassCap = 15;
        private const int AffinityCategoryPoints = 1; // same SortingClass
        private const int AffinityCategoryCap = 5;
        private const int AffinityThreshold = 3;      // fire only when total >= 3

        private const string ReasonHomeSlot = "back to its home slot";
        private const string ReasonHomeBag = "back to its home bag";
        private const string ReasonBeltHome = "tool to worn toolbelt";
        private const string ReasonBackContainer = "tool to worn back container";
        private const string ReasonStack = "tops up a matching stack";
        private const string ReasonSocket = "into a matching socket";
        private const string ReasonAffinity = "already holds similar items";
        private const string ReasonMemory = "this type went here last";
        private const string ReasonGeneric = "into general storage";
        private const string ReasonBeltLast = "toolbelt (last resort)";

        /// <summary>Bag-type defaults (spec O2b): bag PrefabName -> the recommended/starter
        /// profile that bag should behave like when it has NO explicit assignment. The stage
        /// only fires when that profile actually exists (Quick setup / starter file) AND its
        /// rules match the held item, so unknown prefabs and missing profiles cost nothing.
        /// Prefab roster verified against the 27701 decompile + asset rip (recon R3);
        /// ItemMiningBeltAdvanced / ItemHorticultureBelt exist only in the live english.xml —
        /// harmless here if absent from a build.</summary>
        private static readonly Dictionary<string, string> BagTypeDefaults = new Dictionary<string, string>
        {
            { "ItemToolBelt", "Tools" },
            { "ItemMkIIToolbelt", "Tools" },
            { "ItemEmergencyToolBelt", "Tools" },
            { "ItemSurvivalBelt", "Tools" },
            { "ItemMiningBelt", "Ores" },
            { "ItemMiningBeltMKII", "Ores" },
            { "ItemMiningBeltAdvanced", "Ores" },
            { "ItemMiningBackPack", "Ores" },
            { "ItemHardMiningBackPack", "Ores" },
            { "ItemHorticultureBelt", "Farming" },
        };

        /// <summary>Per-bag affinity bookkeeping, gathered in one pass over the scan.</summary>
        private struct AffBag
        {
            public Thing Bag;
            public int Depth;        // holder depth (all direct slots of one holder share it)
            public int PrefabPts;    // uncapped tallies; capped when totalled
            public int ClassPts;
            public int CatPts;
            public Slot TypedSlot;   // first free slot whose Type == held.SlotType (BestDirectSlot mirror)
            public Slot GenericSlot; // first free compatible slot of any other type
            public bool Skip;        // bag has an explicit (resolvable) profile -> never an affinity target
        }

        /// <summary>Per-generic-bag bookkeeping for the deterministic fallback stage (#12),
        /// gathered in one pass over the scan. A "generic bag" is any non-profiled container
        /// exposing a free slot that accepts a None-typed item (which is necessarily a None
        /// slot). Ranked most-of-category, then emptiest, then shallowest.</summary>
        private struct GenBag
        {
            public Thing Bag;
            public int Depth;
            public int CatCount;    // occupants sharing the held item's SortingClass
            public int FreeSlots;   // free slots that accept the item (all None-typed here)
            public Slot Dest;       // first such free slot (the placement target)
            public bool Skip;       // profile-owned -> the player made it law, never a dumping ground
        }

        // Pooled — cleared per use, dropped on Reset(). The results list is returned to
        // ResolveAll callers: valid only until the next resolve, never cache it.
        private static readonly List<StowCandidate> _results = new List<StowCandidate>(8);
        private static readonly List<AffBag> _affBags = new List<AffBag>(16);
        private static readonly List<GenBag> _genBags = new List<GenBag>(8);

        /// <summary>Per-bag memo for the profile/bag-default stages: the assigned-name lookup,
        /// FindProfile walk and Match(held) result depend only on (bag, held), yet the scan
        /// emits one entry PER SLOT — without this an empty 16-slot belt recomputed all three
        /// 16x per resolve (and the ghost hint resolves at ~4 Hz during a drag). Same memo
        /// pattern the affinity stage already uses (_affBags). Cleared per stage + on Reset().</summary>
        private struct BagMemo
        {
            public Thing Bag;
            public bool HasMatch;     // false = this bag can never take the item at this stage
            public int Priority;      // valid only when HasMatch
            public string ProfileName; // profile (or bag-default profile) name, for the Reason
        }
        private static readonly List<BagMemo> _bagMemos = new List<BagMemo>(8);

        /// <summary>ReferenceIds we have already reported as "assigned a profile, but not a
        /// container profiles may be assigned to". The router declines them EVERY resolve; the
        /// log line must fire once per bag, not four times a second. Cleared by <see cref="Reset"/>
        /// (hot-reload rule) — the set holds longs only, never Thing references.</summary>
        private static readonly HashSet<long> _ineligibleLogged = new HashSet<long>();

        /// <summary>Containers whose stored assignment we have already RESOLVED against the active
        /// Stow Profile once this session (SmartStow B2). With one active Stow Profile, an
        /// assignment can legitimately name a Bag Profile that lives only in an INACTIVE one; that
        /// reads as "unassigned" everywhere (same fail-soft as an ineligible container) and must say
        /// so exactly once, not four times a second. Membership means "checked", so the extra
        /// FindProfile walk happens once per container rather than per stage per resolve. Cleared by
        /// <see cref="Reset"/> and by <see cref="ForgetAssignmentWarnings"/> (a Stow Profile switch
        /// changes the answer). Holds longs only, never Thing references.</summary>
        private static readonly HashSet<long> _assignmentChecked = new HashSet<long>();

        // One-entry caches for the winner Reason strings, so a repeated winning resolve during
        // a drag ("profile: Ores" 4x/sec) composes the concat once, not per resolve.
        private static string _profileReasonName, _profileReason;
        private static string _defaultReasonName, _defaultReason;

        /// <summary>True ONLY while a SIMPLE-mode resolve runs its fixed fallback chain
        /// (SmartStow-Simple-Refactor-Plan S-5/F3): the per-stage UIAConfig toggles are bypassed
        /// (Simple is not configurable) and the "skip profile-owned bags" rule inside the affinity
        /// and generic-fallback stages is suspended (Simple treats EVERY bag as unprofiled).
        /// Never true during a Complex resolve, so Complex routing stays byte-identical to
        /// 0.9.7.4. Set/cleared in a try/finally inside <see cref="Resolve"/> (a thrown stage can
        /// never leak it into the next resolve) and belt-and-braces cleared by <see cref="Reset"/>.</summary>
        private static bool _simpleChain;

        /// <summary>The scan depth execution uses (nested-bag toggle collapses it to 1).
        /// Exposed so diagnostics resolve with exactly the depth the G key would.</summary>
        public static int ConfiguredDepth()
        {
            return UIAConfig.StowIntoNestedBags.Value ? UIAConfig.ScanDepth.Value : 1;
        }

        /// <summary>Resolve the single best candidate — the first stage (in chain order) that
        /// yields one. Returns false when every stage declines (= vanilla SmartStow takes over).
        /// No mutation; the caller executes through the MP funnel. This overload is the
        /// EXECUTION surface (the G key): it may seed the belt's home-slot table on first
        /// sight, exactly when a real stow is about to use it.</summary>
        public static bool ResolveBest(DynamicThing held, Slot selectedSlot, int depth, out StowCandidate winner)
        {
            return ResolveBest(held, selectedSlot, depth, false, out winner);
        }

        /// <summary>As above with an explicit <paramref name="dryRun"/> flag. Dry callers (the
        /// ghost hint) must pass true: a dry resolve performs NO side effect of any kind — in
        /// particular it never seeds the belt-binding table (a mod-config disk write), so a
        /// diagnostic can never bake home-slot bindings a later real G press then honours.</summary>
        public static bool ResolveBest(DynamicThing held, Slot selectedSlot, int depth, bool dryRun, out StowCandidate winner)
        {
            _results.Clear();
            Resolve(_results, held, selectedSlot, depth, true, dryRun);
            if (_results.Count > 0)
            {
                winner = _results[0];
                return true;
            }
            winner = default(StowCandidate);
            return false;
        }

        /// <summary>Dry-run diagnostics (stowtrace, the F10 test box): every enabled stage's
        /// best candidate in chain order — element 0 is what <see cref="ResolveBest"/> would
        /// execute. ALWAYS side-effect-free (see the dry-run overload above). Returns the
        /// POOLED list: read it immediately, never hold or cache it.</summary>
        public static List<StowCandidate> ResolveAll(DynamicThing held, Slot selectedSlot, int depth)
        {
            _results.Clear();
            Resolve(_results, held, selectedSlot, depth, false, true);
            return _results;
        }

        /// <summary>Hot-reload teardown: drop pooled Slot/Thing references (mod Shutdown path).</summary>
        public static void Reset()
        {
            _results.Clear();
            _affBags.Clear();
            _genBags.Clear();
            _bagMemos.Clear();
            _ineligibleLogged.Clear();
            _assignmentChecked.Clear();
            _profileReasonName = null;
            _profileReason = null;
            _defaultReasonName = null;
            _defaultReason = null;
            _simpleChain = false;
            InventoryScanner.ResetPool();
        }

        /// <summary>Forget the once-per-container assignment diagnostics (called by
        /// <c>BagProfileStore.LoadProfiles</c>). Switching Stow Profiles changes which names
        /// resolve, so both "not assignable" and "not in this Stow Profile" must be able to speak
        /// again — and a switch that FIXES a dangling name must not leave the old warning as the
        /// last word in the log.</summary>
        public static void ForgetAssignmentWarnings()
        {
            _ineligibleLogged.Clear();
            _assignmentChecked.Clear();
        }

        // ------------------------------------------------------------------ the chain ----

        private static void Resolve(List<StowCandidate> results, DynamicThing held, Slot selectedSlot, int depth, bool firstOnly, bool dryRun)
        {
            Human human = InventoryManager.ParentHuman;
            if (human == null || held == null) return;

            List<ScannedSlot> slots = null; // one scan per resolve, shared by every stage below
            StowCandidate c;

            // Mode fork (0.9.8.0, SmartStow-Simple-Refactor-Plan par.6 + Build-Decisions ANSWERS):
            // SIMPLE runs the HOME stage and then the FIXED no-profile fallback chain (S-5/F3);
            // COMPLEX runs today's 0.9.7.4 chain byte-identically, with the opt-in home-first
            // stage (P5a, default off). Fail-soft: when the mode keys could not bind yet
            // (StowModeConfig.Available false), behave exactly like 0.9.7.4 (Complex).
            StowMode mode = StowMode.Complex;
            try { if (StowModeConfig.Available) mode = StowModeConfig.Mode; } catch { }

            if (mode == StowMode.Simple)
            {
                // Stage 0: the item's own remembered home. Found by ReferenceId, never by scan,
                // so nesting depth cannot hide it (Ningy's mining belt inside the backpack).
                if (TryHome(human, held, selectedSlot, out c)) { results.Add(c); if (firstOnly) return; }

                // No live home / home bag full -> the FIXED fallback chain: belt tool -> stack
                // merge -> socket -> affinity -> generic fallback -> belt last-resort. Simple is
                // NOT configurable: the per-stage toggles stay Complex settings and are ignored
                // here, and every bag counts as unprofiled (Profile/BagDefault/Memory never run;
                // _simpleChain suspends the profile-skip inside affinity + generic fallback).
                // Wherever the item lands only SEEDS its home, so the ambiguity lasts one press.
                _simpleChain = true;
                try
                {
                    if (TryBeltTool(human, held, dryRun, out c)) { results.Add(c); if (firstOnly) return; }
                    if (TryStackMerge(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
                    if (TryFunctionalSocket(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
                    if (TryAffinity(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
                    if (TryGenericFallback(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
                    if (TryBeltFallback(human, held, out c)) { results.Add(c); if (firstOnly) return; }
                }
                finally { _simpleChain = false; }
                return;
            }

            // COMPLEX: today's chain, unchanged. P5a (HomeFirstInComplex, default OFF): the home
            // stage runs first for players who want "back where it was" ahead of their profiles.
            if (StowModeConfig.HomeFirstInComplex
                && TryHome(human, held, selectedSlot, out c)) { results.Add(c); if (firstOnly) return; }

            if (TryBeltTool(human, held, dryRun, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryStackMerge(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryFunctionalSocket(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryProfile(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryAffinity(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryBagDefault(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryMemory(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryGenericFallback(human, held, selectedSlot, ref slots, depth, out c)) { results.Add(c); if (firstOnly) return; }
            if (TryBeltFallback(human, held, out c)) { results.Add(c); if (firstOnly) return; }
        }

        private static List<ScannedSlot> EnsureScan(ref List<ScannedSlot> slots, int depth)
        {
            // Pooled scan: the list never escapes a resolve (stages copy Slot/Thing refs into
            // StowCandidate, never ScannedSlot instances), so reusing the scanner's buffers is
            // safe and makes the recurring ghost-hint dry-run near-zero-alloc. Location strings
            // are skipped — no router stage reads them.
            if (slots == null) slots = InventoryScanner.ScanPooled(depth, false);
            return slots;
        }

        /// <summary>Stage 0 - HOME (0.9.8.0, SmartStow-Simple-Refactor-Plan par.6.1): the held
        /// item's own remembered home from <see cref="StowHomeStore"/>. Runs first in Simple mode
        /// and (opt-in, P5a) first in Complex. The home is found by ReferenceId - never by scan -
        /// so nesting depth cannot hide it. <c>TryGetLiveHome</c> already validated the container
        /// (resolves + PrefabHash match, on the local player, real storage, not stow-excluded, no
        /// body-bag ancestor); occupancy policy lives HERE:
        ///   a. the exact home slot, empty and movable -> move ("that spot");
        ///   b. the exact home slot holding a matching partial stack -> merge (the host-only #13
        ///      continuation then stays inside this same bag);
        ///   c. any other matching partial stack in the home bag -> merge;
        ///   d. the first free accepting slot in the home bag, a type-matched slot beating a
        ///      generic one (mirroring <see cref="BestDirectSlot"/>).
        /// A decline (home bag full, every gate refused) falls to the fallback chain, whose
        /// landing only SEEDS a home - a full home bag is "visiting", never a new home. Pure
        /// decision: no seeding, no intents; the store's single documented side effect is
        /// refreshing the entry's `seen` GC stamp. The executor re-gates AllowMove/CanMerge on
        /// the same synchronous call stack.</summary>
        private static bool TryHome(Human human, DynamicThing held, Slot selectedSlot, out StowCandidate c)
        {
            c = default(StowCandidate);
            Thing homeBag;
            Slot exact;
            bool live = false;
            try { live = StowHomeStore.TryGetLiveHome(held, out homeBag, out exact); }
            catch { homeBag = null; exact = null; }
            if (!live || homeBag == null || ReferenceEquals(homeBag, held)) return false;

            IMergeable heldMergeable = held as IMergeable;
            bool bagIsHuman = ReferenceEquals(homeBag, human);

            // a/b: the exact remembered slot. TryGetLiveHome may return it OCCUPIED on purpose -
            // a matching partial stack there is the merge-back-into-home case (plan par.6.1 b).
            if (exact != null && exact != selectedSlot && IsDestSlot(exact))
            {
                DynamicThing occ = exact.Get();
                if (occ == null)
                {
                    if (Slot.AllowMove(held, exact))
                        return HomeCandidate(out c, exact, homeBag, ReasonHomeSlot, bagIsHuman);
                }
                else if (heldMergeable != null)
                {
                    IMergeable target = occ as IMergeable;
                    if (target != null && target.CanStack(heldMergeable) && !target.IsStackFull
                        && Slot.CanMerge(held, exact))
                        return HomeCandidate(out c, exact, homeBag, ReasonHomeSlot, bagIsHuman);
                }
            }

            // c/d: same-bag fallback over the home bag's own DIRECT slots (S-1). A matching
            // partial stack anywhere in the bag beats a free slot; a type-matched free slot beats
            // a generic one. Hands are never stow destinations; the exact slot was already judged.
            List<Slot> bagSlots = homeBag.Slots;
            if (bagSlots == null) return false;
            Slot typedFree = null;
            Slot genericFree = null;
            for (int i = 0; i < bagSlots.Count; i++)
            {
                Slot s = bagSlots[i];
                if (s == null || s == exact || s == selectedSlot) continue;
                if (bagIsHuman && s.IsHandSlot) continue;
                if (s.IsLocked || !IsDestSlot(s)) continue;
                // A jetpack's propellant/battery or a survival belt's battery/chip socket is never
                // a same-bag FALLBACK (2026-09-26): a canister homed in a jetpack's storage slot
                // would otherwise prefer the typed propellant slot here and be burned as fuel. The
                // EXACT remembered slot (a/b above) is still honoured - that item came from there.
                if (BagProfileGate.IsDeviceSlot(homeBag, s)) continue;
                DynamicThing occ = s.Get();
                if (occ != null)
                {
                    if (heldMergeable == null) continue;
                    IMergeable target = occ as IMergeable;
                    if (target == null || !target.CanStack(heldMergeable) || target.IsStackFull) continue;
                    if (!Slot.CanMerge(held, s)) continue;
                    return HomeCandidate(out c, s, homeBag, ReasonHomeBag, bagIsHuman);   // (c) beats any free slot
                }
                if (!Slot.AllowMove(held, s)) continue;
                if (s.Type == held.SlotType) { if (typedFree == null) typedFree = s; }
                else if (genericFree == null) genericFree = s;
            }
            Slot free = typedFree != null ? typedFree : genericFree;
            if (free == null) return false;
            return HomeCandidate(out c, free, homeBag, ReasonHomeBag, bagIsHuman);
        }

        /// <summary>Fill a HOME-stage candidate. Depth is display-only here (homes are id-found,
        /// not scan-found): 0 on the human's own worn slots, 1 for a carried container.</summary>
        private static bool HomeCandidate(out StowCandidate c, Slot slot, Thing holder, string reason, bool bagIsHuman)
        {
            c = default(StowCandidate);
            c.Slot = slot;
            c.Holder = holder;
            c.Stage = StowStage.Home;
            c.Reason = reason;
            c.Depth = bagIsHuman ? 0 : 1;
            return true;
        }

        /// <summary>Stage 1 (ported from SmartStowPlus): a GENUINE tool belongs on a
        /// DIRECTLY-WORN belt/container before it ever nests into a belt tucked inside another
        /// bag. Priority: a) the equipped waist tool belt (home-slot aware via the Belt Wheel's
        /// bindings), b) the worn Back container itself. Both type-gated by Slot.AllowMove.
        ///
        /// #14: only a REAL <see cref="Tool"/> (game type) flies here at high priority. A FALSE
        /// tool — cable coil, SlotType==Tool but not a Tool subclass — is deliberately NOT caught
        /// here; it routes like any other item and only reaches a bare toolbelt slot as a last
        /// resort (<see cref="TryBeltFallback"/>), so the belt stops greedily swallowing coils.</summary>
        private static bool TryBeltTool(Human human, DynamicThing held, bool dryRun, out StowCandidate c)
        {
            c = default(StowCandidate);
            // Simple chain (S-5/F3): the stage toggle is a Complex setting, ignored in Simple.
            if ((!_simpleChain && !UIAConfig.StowToolsToToolbeltFirst.Value) || !(held is Tool)) return false;

            DynamicThing belt = human.ToolbeltSlot != null ? human.ToolbeltSlot.Get() : null;
            if (IsExcluded(belt)) belt = null;   // Q5 exclusion applies to the belt stage too
            Slot dest = HomeOrBestDirectSlot(belt, held, dryRun);
            string reason = ReasonBeltHome;
            Thing holder = belt;
            if (dest == null)
            {
                DynamicThing back = human.BackpackSlot != null ? human.BackpackSlot.Get() : null;
                if (IsExcluded(back)) back = null;
                dest = BestDirectSlot(back, held);
                reason = ReasonBackContainer;
                holder = back;
            }
            if (dest == null) return false;

            c.Slot = dest;
            c.Holder = holder;
            c.Stage = StowStage.BeltTool;
            c.Reason = reason;
            c.Depth = 1; // worn on the body, its slots sit at depth 1
            return true;
        }

        /// <summary>Stage 1 (ported verbatim): the FIRST matching partial stack in scan order.
        /// Decide-time gates mirror the old code exactly (CanStack, not full, Slot.CanMerge);
        /// the executor re-derives the target and performs the one Thing.Merge.</summary>
        private static bool TryStackMerge(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!_simpleChain && !UIAConfig.StowPreferStacks.Value) return false;   // toggle = Complex-only
            IMergeable heldMergeable = held as IMergeable;
            if (heldMergeable == null) return false;

            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Slot == selectedSlot) continue;
                // Hands are never stow destinations ("stowing" into your other hand).
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                // D-005: never feed a stack sitting in a SEALED (hidden / stack) slot — see IsDestSlot.
                if (!IsDestSlot(scanned)) continue;
                // Never top up a stack living inside a consumable/dispenser/starter box.
                if (!HolderIsRealStorage(scanned.Holder)) continue;
                if (IsExcluded(scanned.Holder)) continue;
                DynamicThing occ = scanned.Occupant;
                if (occ == null) continue;
                IMergeable target = occ as IMergeable;
                if (target == null) continue;
                if (!target.CanStack(heldMergeable) || target.IsStackFull) continue;
                if (!Slot.CanMerge(held, scanned.Slot)) continue;

                c.Slot = scanned.Slot;
                c.Holder = scanned.Holder;
                c.Stage = StowStage.StackMerge;
                c.Reason = ReasonStack;
                c.Depth = scanned.Depth;
                return true;
            }
            return false;
        }

        /// <summary>Stage 3 — NEW (#11), functional-socket priority: a component item (a
        /// canister, battery, filter, cartridge, board, ...) prefers an EMPTY slot whose class is
        /// exactly its <see cref="DynamicThing.SlotType"/> — a real socket (the suit air/waste
        /// tank, the jetpack propellant, the suit battery/filter, a tool's battery slot) — over
        /// any generic None bag slot, anywhere reachable. This is what stops a canister dropping
        /// into a backpack while the jetpack canister slot sits open. NO gas-type inspection:
        /// like vanilla, the first empty socket of the right class wins (user decision #11).
        /// Runs BEFORE the bag stages so the socket beats a bag that would merely accept it.</summary>
        private static bool TryFunctionalSocket(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!_simpleChain && !UIAConfig.StowSocketPriority.Value) return false;   // toggle = Complex-only
            Slot.Class st = held.SlotType;
            if (!IsFunctionalSocketClass(st)) return false;

            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Slot == selectedSlot || scanned.Occupant != null) continue;
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                if (!IsDestSlot(scanned)) continue;   // D-005: a hidden socket is not a socket
                // A TRUE socket only: the slot's own class equals the item's type. Excludes None
                // (generic bag) slots by construction, which is the whole point of the stage.
                if (scanned.Slot.Type != st) continue;
                if (IsExcluded(scanned.Holder)) continue;   // Q5: even a real socket is off-limits
                if (!Slot.AllowMove(held, scanned.Slot)) continue;

                c.Slot = scanned.Slot;
                c.Holder = scanned.Holder;
                c.Stage = StowStage.FunctionalSocket;
                c.Reason = ReasonSocket;
                c.Depth = scanned.Depth;
                return true; // first empty socket reached in scan order (body-slot order), like vanilla
            }
            return false;
        }

        /// <summary>The component-socket classes (#11): item types that live in a dedicated
        /// physical socket rather than a generic bag. Mirrors the mod's existing
        /// component-socket allow-list (ItemMenuBuilder.IsComponentSocket), all verified exact
        /// <see cref="Slot.Class"/> members in the 27701 decompile. Deliberately EXCLUDES Tool,
        /// Belt, Ore, Ingot, Uniform, Helmet, Suit, None (bag / worn-garment / bulk classes).</summary>
        private static bool IsFunctionalSocketClass(Slot.Class st)
        {
            switch (st)
            {
                case Slot.Class.GasCanister:
                case Slot.Class.LiquidCanister:
                case Slot.Class.DirtCanister:
                case Slot.Class.GasFilter:
                case Slot.Class.Battery:
                case Slot.Class.Cartridge:
                case Slot.Class.Motherboard:
                case Slot.Class.Circuitboard:
                case Slot.Class.DataDisk:
                case Slot.Class.SensorProcessingUnit:
                case Slot.Class.Organ:
                case Slot.Class.ProgrammableChip:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Stage 4 (ported verbatim): the bag whose explicitly ASSIGNED profile best
        /// matches the item. Higher match priority wins; on a tie the shallower bag beats one
        /// nested deeper. AllowMove checked per candidate, exactly like the old loop.</summary>
        private static bool TryProfile(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!UIAConfig.StowUseProfiles.Value || BagProfileStore.Profiles.Count == 0) return false;

            Slot best = null;
            Thing bestBag = null;
            string bestName = null;
            int bestPriority = int.MinValue;
            int bestDepth = int.MaxValue;
            _bagMemos.Clear();
            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Slot == selectedSlot || scanned.Occupant != null) continue;
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                Thing bag = scanned.Holder;
                if (bag == null || bag == human) continue;
                if (!HolderIsRealStorage(bag)) continue; // not a consumable/dispenser/starter box
                if (IsExcluded(bag)) continue;          // Q5: "never smart-stow into this"
                // Per-bag memo: assigned name + FindProfile + Match are (bag, held)-constant,
                // so compute them once per bag, not once per empty slot of that bag.
                int mi = IndexOfBagMemo(bag);
                if (mi < 0)
                {
                    BagMemo m = new BagMemo();
                    m.Bag = bag;
                    // Same lookup GetAssignedProfile does, without its per-call LINQ allocation —
                    // through the eligibility gate, so an assignment on a non-assignable container
                    // reads as "unassigned" here and at every later stage (EffectiveAssignedName).
                    string name = EffectiveAssignedName(bag);
                    BagProfile profile = FindProfile(name);
                    int? priority = profile != null ? profile.Match(held) : null;
                    m.HasMatch = priority.HasValue;
                    m.Priority = priority.HasValue ? priority.Value : 0;
                    m.ProfileName = name;
                    _bagMemos.Add(m);
                    mi = _bagMemos.Count - 1;
                }
                BagMemo memo = _bagMemos[mi];
                if (!memo.HasMatch) continue;
                if (memo.Priority < bestPriority) continue;
                if (memo.Priority == bestPriority && scanned.Depth >= bestDepth) continue;
                if (!IsDestSlot(scanned)) continue;   // D-005: never a sealed slot
                // 2026-09-26: jetpacks/tool belts are assignable now; a profile routes into their
                // STORAGE only - never the propellant/battery/chip socket (see IsDeviceSlot).
                if (BagProfileGate.IsDeviceSlot(bag, scanned.Slot)) continue;
                if (!Slot.AllowMove(held, scanned.Slot)) continue;
                best = scanned.Slot;
                bestBag = bag;
                bestName = memo.ProfileName;
                bestPriority = memo.Priority;
                bestDepth = scanned.Depth;
            }
            _bagMemos.Clear();
            if (best == null) return false;

            c.Slot = best;
            c.Holder = bestBag;
            c.Stage = StowStage.Profile;
            c.Reason = ProfileReason(bestName);
            c.Score = bestPriority;
            c.Depth = bestDepth;
            return true;
        }

        /// <summary>Stage 3 — NEW (spec O2a), content affinity: a bag with NO explicit profile
        /// gets an implicit one derived live from its DIRECT contents. Score per candidate bag:
        /// same prefab +9 each (cap 45), same Slot.Class +3 each (cap 15, None never counts),
        /// same SortingClass +1 each (cap 5); each occupant counts once at its best tier. Fires
        /// only at score >= 3; higher score wins, shallow beats nested on ties. Slot pick inside
        /// the winning bag mirrors BestDirectSlot: type-match first, then generic. Runs AFTER
        /// the profile stage, so an explicit profile match always wins; bags carrying an
        /// explicit profile are excluded entirely (the player made them law — their leftover
        /// contents must not attract off-profile items).</summary>
        private static bool TryAffinity(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!_simpleChain && !UIAConfig.StowUseAffinity.Value) return false;   // toggle = Complex-only

            _affBags.Clear();
            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Slot == selectedSlot) continue;
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                Thing bag = scanned.Holder;
                // Never hands, never the human itself, never the held thing's own slots.
                if (bag == null || bag == human || ReferenceEquals(bag, held)) continue;
                if (!HolderIsRealStorage(bag)) continue; // not a consumable/dispenser/starter box

                int i = IndexOfAffBag(bag);
                if (i < 0)
                {
                    AffBag fresh = new AffBag();
                    fresh.Bag = bag;
                    fresh.Depth = scanned.Depth;
                    // Skip when the bag is profile-owned (the player made it law) OR excluded (Q5).
                    // Simple chain: every bag is unprofiled - the profile skip is suspended, and
                    // the once-per-container assignment diagnostics (inside EffectiveAssignedName)
                    // never run for a mode that ignores profiles.
                    string assigned = _simpleChain ? null : EffectiveAssignedName(bag);
                    fresh.Skip = FindProfile(assigned) != null || IsExcluded(bag);
                    _affBags.Add(fresh);
                    i = _affBags.Count - 1;
                }
                AffBag entry = _affBags[i];
                if (entry.Skip) continue;
                // A wearable's device socket is neither content nor destination (2026-09-26): the
                // jetpack's propellant canister must not make it "already hold canisters", and a
                // canister must never be affinity-routed into that socket (the functional-socket
                // stage alone fills sockets, under its own toggle).
                if (BagProfileGate.IsDeviceSlot(bag, scanned.Slot)) continue;

                DynamicThing occ = scanned.Occupant;
                if (occ != null)
                {
                    if (occ.PrefabHash == held.PrefabHash)
                        entry.PrefabPts += AffinityPrefabPoints;
                    else if (held.SlotType != Slot.Class.None && occ.SlotType == held.SlotType)
                        entry.ClassPts += AffinityClassPoints;
                    else if (occ.SortingClass == held.SortingClass)
                        entry.CatPts += AffinityCategoryPoints;
                }
                else if (IsDestSlot(scanned) && Slot.AllowMove(held, scanned.Slot))   // D-005: never a sealed slot
                {
                    if (scanned.Slot.Type == held.SlotType)
                    {
                        if (entry.TypedSlot == null) entry.TypedSlot = scanned.Slot;
                    }
                    else if (entry.GenericSlot == null)
                    {
                        entry.GenericSlot = scanned.Slot;
                    }
                }
                _affBags[i] = entry;
            }

            Thing winBag = null;
            Slot winSlot = null;
            int winScore = 0;
            int winDepth = int.MaxValue;
            for (int i = 0; i < _affBags.Count; i++)
            {
                AffBag e = _affBags[i];
                if (e.Skip) continue;
                Slot dest = e.TypedSlot != null ? e.TypedSlot : e.GenericSlot;
                if (dest == null) continue; // no free compatible slot -> not a candidate
                int score = Cap(e.PrefabPts, AffinityPrefabCap)
                          + Cap(e.ClassPts, AffinityClassCap)
                          + Cap(e.CatPts, AffinityCategoryCap);
                if (score < AffinityThreshold) continue;
                if (score < winScore) continue;
                if (score == winScore && e.Depth >= winDepth) continue;
                winBag = e.Bag;
                winSlot = dest;
                winScore = score;
                winDepth = e.Depth;
            }
            _affBags.Clear();
            if (winSlot == null) return false;

            c.Slot = winSlot;
            c.Holder = winBag;
            c.Stage = StowStage.Affinity;
            c.Reason = ReasonAffinity;
            c.Score = winScore;
            c.Depth = winDepth;
            return true;
        }

        /// <summary>Stage 4 — NEW (spec O2b), bag-type defaults: a profile-LESS bag whose prefab
        /// appears in <see cref="BagTypeDefaults"/> behaves as if the named recommended profile
        /// were assigned — but only when that profile actually exists and matches the item.
        /// Post-B2 the SHIPPED table is the only source here; the user prefab-default file is
        /// retired (FlorpyDorp Q4 — see the note inside).
        /// Matters mostly for empty bags fresh from the printer (affinity needs contents).
        /// Winner selection mirrors the profile stage (priority, then shallow beats nested).</summary>
        private static bool TryBagDefault(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!UIAConfig.StowUseBagTypeDefaults.Value || BagProfileStore.Profiles.Count == 0) return false;

            Slot best = null;
            Thing bestBag = null;
            string bestProfile = null;
            int bestPriority = int.MinValue;
            int bestDepth = int.MaxValue;
            _bagMemos.Clear();
            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Slot == selectedSlot || scanned.Occupant != null) continue;
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                Thing bag = scanned.Holder;
                if (bag == null || bag == human || ReferenceEquals(bag, held)) continue;
                if (!HolderIsRealStorage(bag)) continue; // not a consumable/dispenser/starter box
                if (IsExcluded(bag)) continue;          // Q5: "never smart-stow into this"
                // Per-bag memo: default-name lookup, both FindProfile walks and Match(held) are
                // (bag, held)-constant — compute once per bag, not once per empty slot.
                int mi = IndexOfBagMemo(bag);
                if (mi < 0)
                {
                    BagMemo m = new BagMemo();
                    m.Bag = bag;
                    // SmartStow B2 (FlorpyDorp Q4): the USER prefab-default table
                    // (prefab-defaults.xml, "All Mining Belts") is retired — Bag Profiles are
                    // mapped to a container by hand, one container at a time, and nothing derives a
                    // mapping for a whole prefab any more. The file and its accessors survive
                    // (hide, never destroy: BagProfileStore.GetPrefabDefaultProfileName still reads
                    // it, the rename/delete cascade still maintains it), it is simply no longer
                    // CONSULTED here. What remains is the SHIPPED bag-type table below — a routing
                    // heuristic in the same family as content affinity, gated by the player's own
                    // "5 - Known bag types get a default" toggle, which never writes an assignment
                    // and never claims a container the player mapped by hand.
                    string profName;
                    if (bag.PrefabName != null && BagTypeDefaults.TryGetValue(bag.PrefabName, out profName))
                    {
                        // An explicit (resolvable, ELIGIBLE) assignment supersedes the built-in
                        // default; an assignment on a non-assignable container does not. Since
                        // 2026-09-26 tool belts ARE assignable, so a hand-assigned tool belt now
                        // follows the player's own profile instead of the shipped "Tools" default
                        // (exactly like a hand-assigned mining belt always did with "Ores").
                        if (FindProfile(EffectiveAssignedName(bag)) == null)
                        {
                            BagProfile prof = FindProfile(profName);
                            if (prof != null) // fires only when the recommended profile exists
                            {
                                int? priority = prof.Match(held);
                                m.HasMatch = priority.HasValue;
                                m.Priority = priority.HasValue ? priority.Value : 0;
                                m.ProfileName = profName;
                            }
                        }
                    }
                    _bagMemos.Add(m);
                    mi = _bagMemos.Count - 1;
                }
                BagMemo memo = _bagMemos[mi];
                if (!memo.HasMatch) continue;
                if (memo.Priority < bestPriority) continue;
                if (memo.Priority == bestPriority && scanned.Depth >= bestDepth) continue;
                if (!IsDestSlot(scanned)) continue;   // D-005: never a sealed slot
                if (BagProfileGate.IsDeviceSlot(bag, scanned.Slot)) continue;   // never a battery/chip socket (survival belt)
                if (!Slot.AllowMove(held, scanned.Slot)) continue;
                best = scanned.Slot;
                bestBag = bag;
                bestProfile = memo.ProfileName;
                bestPriority = memo.Priority;
                bestDepth = scanned.Depth;
            }
            _bagMemos.Clear();
            if (best == null) return false;

            c.Slot = best;
            c.Holder = bestBag;
            c.Stage = StowStage.BagDefault;
            c.Reason = DefaultReason(bestProfile);
            c.Score = bestPriority;
            c.Depth = bestDepth;
            return true;
        }

        /// <summary>Stage 5 (ported verbatim, demoted below affinity per spec O2c): the bag
        /// where this item TYPE last went this save. First free, movable slot on that bag in
        /// scan order — identical to the old FirstOrDefault, without the closure.</summary>
        private static bool TryMemory(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!UIAConfig.StowUseTypeMemory.Value) return false;
            long? bagRef = BagProfileStore.RecallStow(held);
            if (!bagRef.HasValue) return false;

            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Occupant != null) continue;
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                if (scanned.Holder == null || scanned.Holder.ReferenceId != bagRef.Value) continue;
                if (!HolderIsRealStorage(scanned.Holder)) continue; // not a consumable/dispenser/starter box
                if (IsExcluded(scanned.Holder)) continue;          // Q5: "never smart-stow into this"
                if (!IsDestSlot(scanned)) continue;           // D-005: never a sealed slot
                // The remembered bag may be a jetpack now that profile stows can land in one
                // (2026-09-26): "first free slot" must mean its storage, never slot 0's propellant.
                if (BagProfileGate.IsDeviceSlot(scanned.Holder, scanned.Slot)) continue;
                if (!Slot.AllowMove(held, scanned.Slot)) continue;

                c.Slot = scanned.Slot;
                c.Holder = scanned.Holder;
                c.Stage = StowStage.Memory;
                c.Reason = ReasonMemory;
                c.Depth = scanned.Depth;
                return true;
            }
            return false;
        }

        /// <summary>Stage 8 — NEW (#12), deterministic generic fallback: a None-typed build item
        /// (walls, frames, kits — SlotType None) fits ANY generic None slot, so vanilla drops it
        /// into whichever generic bag comes first in body-slot order (the "walls went to the
        /// wrong backpack" bug). Instead, pick a stable target: among non-profiled bags with a
        /// free slot that accepts it, the one already holding the MOST of the item's SortingClass,
        /// then the EMPTIEST, then the SHALLOWEST. Only None-typed items reach here — a typed item
        /// with no socket/profile home is left to vanilla, whose typed placement is unambiguous.
        /// Profile-owned bags are excluded (the player made them law), exactly like affinity.</summary>
        private static bool TryGenericFallback(Human human, DynamicThing held, Slot selectedSlot, ref List<ScannedSlot> slots, int depth, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!_simpleChain && !UIAConfig.StowGenericFallback.Value) return false;   // toggle = Complex-only
            if (held.SlotType != Slot.Class.None) return false;

            _genBags.Clear();
            foreach (ScannedSlot scanned in EnsureScan(ref slots, depth))
            {
                if (scanned.Slot == selectedSlot) continue;
                if (scanned.Slot == human.LeftHandSlot || scanned.Slot == human.RightHandSlot) continue;
                Thing bag = scanned.Holder;
                // Never the human's own worn slots (bag == human), never the held thing's slots.
                if (bag == null || bag == human || ReferenceEquals(bag, held)) continue;
                if (!HolderIsRealStorage(bag)) continue; // not a consumable/dispenser/starter box

                int i = IndexOfGenBag(bag);
                if (i < 0)
                {
                    GenBag fresh = new GenBag();
                    fresh.Bag = bag;
                    fresh.Depth = scanned.Depth;
                    // Simple chain: profile-skip suspended (every bag is unprofiled); Q5 exclusion
                    // always holds. Short-circuit keeps EffectiveAssignedName Complex-only.
                    fresh.Skip = (!_simpleChain && FindProfile(EffectiveAssignedName(bag)) != null)
                        || IsExcluded(bag);   // Q5: "never smart-stow into this"
                    _genBags.Add(fresh);
                    i = _genBags.Count - 1;
                }
                GenBag e = _genBags[i];
                if (e.Skip) continue;
                // Device sockets are not contents (the propellant canister must not count toward
                // the category tally) and never a destination (2026-09-26, see IsDeviceSlot).
                if (BagProfileGate.IsDeviceSlot(bag, scanned.Slot)) continue;

                DynamicThing occ = scanned.Occupant;
                if (occ != null)
                {
                    if (occ.SortingClass == held.SortingClass) e.CatCount++;
                }
                else if (IsDestSlot(scanned) && Slot.AllowMove(held, scanned.Slot))   // D-005: never a sealed slot
                {
                    // held is None-typed, so any accepting free slot is itself a None slot ->
                    // this bag is a genuine generic container.
                    e.FreeSlots++;
                    if (e.Dest == null) e.Dest = scanned.Slot;
                }
                _genBags[i] = e;
            }

            Thing winBag = null;
            Slot winSlot = null;
            int winCat = -1;
            int winFree = -1;
            int winDepth = int.MaxValue;
            for (int i = 0; i < _genBags.Count; i++)
            {
                GenBag e = _genBags[i];
                if (e.Skip || e.Dest == null) continue; // no free generic slot -> not a candidate
                // most-of-category, then emptiest, then shallowest.
                if (e.CatCount < winCat) continue;
                if (e.CatCount == winCat)
                {
                    if (e.FreeSlots < winFree) continue;
                    if (e.FreeSlots == winFree && e.Depth >= winDepth) continue;
                }
                winBag = e.Bag;
                winSlot = e.Dest;
                winCat = e.CatCount;
                winFree = e.FreeSlots;
                winDepth = e.Depth;
            }
            _genBags.Clear();
            if (winSlot == null) return false;

            c.Slot = winSlot;
            c.Holder = winBag;
            c.Stage = StowStage.GenericFallback;
            c.Reason = ReasonGeneric;
            c.Score = winCat;
            c.Depth = winDepth;
            return true;
        }

        /// <summary>Stage 9 — NEW (#14), toolbelt LAST resort: only a FALSE tool reaches the belt
        /// here — SlotType==Tool but NOT a <see cref="Tool"/> subclass (the cable coil). A genuine
        /// tool already had first crack in stage 1; a non-tool cannot enter a Tool-typed slot at
        /// all. So this fires solely for coils, and only when every content-aware stage above
        /// declined — the belt is now the coil's lowest-priority destination, not its greedy
        /// first one. No scan needed: the target is a bare slot on the directly-worn belt. Gated
        /// on the same toggle as stage 1 (turning tool->belt off disables both belt behaviours).</summary>
        private static bool TryBeltFallback(Human human, DynamicThing held, out StowCandidate c)
        {
            c = default(StowCandidate);
            if (!_simpleChain && !UIAConfig.StowToolsToToolbeltFirst.Value) return false;   // toggle = Complex-only
            if (held.SlotType != Slot.Class.Tool || held is Tool) return false;

            DynamicThing belt = human.ToolbeltSlot != null ? human.ToolbeltSlot.Get() : null;
            if (IsExcluded(belt)) return false;   // Q5: "never smart-stow into this"
            Slot dest = BestDirectSlot(belt, held);
            if (dest == null) return false;

            c.Slot = dest;
            c.Holder = belt;
            c.Stage = StowStage.BeltFallback;
            c.Reason = ReasonBeltLast;
            c.Depth = 1;
            return true;
        }

        // ------------------------------------------------------------------- helpers ----

        /// <summary>Shared gate: is <paramref name="holder"/> a real, freely-stowable storage
        /// container (backpack, belt, suit/uniform, reusable crate, mod container) rather than a
        /// single-purpose consumable/dispenser/starter box — a water-bottle bag, a cereal box, a
        /// burger box, the starter supplies box — that merely exposes a game-legal free slot?
        /// Delegates to the ONE predicate (<see cref="GridModel.IsStorageContainer"/>) so the router
        /// and the Universal Inventory grid can never disagree on what counts as storage. Fail-closed:
        /// a null or non-<see cref="DynamicThing"/> holder is treated as NOT storage.</summary>
        private static bool HolderIsRealStorage(Thing holder)
        {
            return GridModel.IsStorageContainer(holder as DynamicThing);
        }

        /// <summary>D-005: may a stow LAND in this slot? Never a SEALED slot
        /// (<see cref="ItemActions.IsSealedSlot"/> — a non-interactable slot vanilla never draws, or any
        /// slot of a stack): the scan walks every slot, and a hidden slot inside an otherwise real
        /// container would otherwise be a perfectly "free, AllowMove-able" destination the player can
        /// neither see nor reach. (The consumable-stack case — the cable coil — is already excluded by
        /// <see cref="HolderIsRealStorage"/>; this is the belt-and-braces half for hidden slots.)</summary>
        private static bool IsDestSlot(Slot slot)
        {
            return slot != null && !ItemActions.IsSealedSlot(slot);
        }

        /// <summary>The scan-stage form of <see cref="IsDestSlot(Slot)"/>: reads the flag the scanner
        /// computed ONCE per slot with the same predicate (<see cref="ScannedSlot.Sealed"/>), so the
        /// router and every other scan consumer share one answer. The flag is also INHERITED by
        /// everything beneath a sealed slot, so a bag the old phantom grid tucked into a cable coil is
        /// no stow destination either — its own slots are ordinary, but a consumed coil destroys it
        /// with everything inside.</summary>
        private static bool IsDestSlot(ScannedSlot scanned)
        {
            return scanned != null && scanned.Slot != null && !scanned.Sealed;
        }

        /// <summary>Design Q5: the player marked this container "never smart-stow into this".
        /// Checked by EVERY stage of the chain (belt, stack, socket, profile, affinity, bag
        /// default, memory, generic fallback, belt fallback) — an exclusion the belt stage ignored
        /// would be an exclusion the player cannot trust. One save-key check plus, only when the
        /// save actually has exclusions, one hash lookup; the empty-set fast path costs nothing
        /// for the overwhelming majority of players.</summary>
        private static bool IsExcluded(Thing holder)
        {
            return holder != null && BagProfileStore.IsStowExcluded(holder);
        }

        /// <summary>The bag's assigned profile name AS THE ROUTER SHOULD SEE IT: the saved name
        /// when the container is one profiles may be assigned to, otherwise null.
        ///
        /// <para>A container that carries a saved assignment but is NOT assignable (a Terrain
        /// Manipulator that slipped through the old F10 bag list, a suit, a cereal box) keeps its
        /// data — hide, never destroy — and is simply treated as UNASSIGNED by every stage. That
        /// uniformity matters: if only the profile stage ignored it, the bag-type-default stage
        /// would still see "this bag has an explicit assignment" and decline too, so a
        /// non-assignable container with a known bag-type default would lose that default as well
        /// and end up routing worse than a fresh one. Reported ONCE per container (per session) so
        /// the player can find and clear it; nothing on disk is touched. (Tool belts and jetpacks
        /// became assignable on 2026-09-26: an assignment saved on one before B1 is honoured again
        /// from that date, on its storage slots only - see BagProfileGate.IsDeviceSlot.)</para>
        ///
        /// <para>An ASSIGNABLE container whose assignment names a profile the active Stow Profile
        /// does not have is returned unchanged — the stages resolve it to null on their own and
        /// decline, exactly as they always did for a dangling name. The only thing added is one
        /// explanatory log line per container (SmartStow B2).</para></summary>
        private static string EffectiveAssignedName(Thing bag)
        {
            string name = BagProfileStore.GetAssignedProfileName(bag);
            if (string.IsNullOrEmpty(name)) return null;
            if (!BagProfileGate.IsAssignableContainer(bag))
            {
                if (bag != null && _ineligibleLogged.Add(bag.ReferenceId))
                    UIALog.Info("Bag profile '" + name + "' is assigned to '" + DescribeBag(bag)
                        + "', which is not a container profiles can be assigned to - Smart Stow now treats it as unassigned. "
                        + "The assignment is kept on disk; clear it in F10 > Storage if you no longer want it.");
                return null;
            }
            // SmartStow B2: with ONE active Stow Profile, a perfectly valid assignment can name a
            // Bag Profile that lives only in another Stow Profile. The stages below already decline
            // on a name FindProfile cannot resolve, so behaviour is unchanged (fail-soft, data kept)
            // — this only makes the reason visible once, so "my bag stopped using its profile" has
            // an answer in the log. The membership test keeps it to one lookup per container.
            if (bag != null && !_assignmentChecked.Contains(bag.ReferenceId))
            {
                _assignmentChecked.Add(bag.ReferenceId);
                if (FindProfile(name) == null)
                    UIALog.Info("Bag profile '" + name + "' is assigned to '" + DescribeBag(bag)
                        + "', but the active Stow Profile '" + (Features.StowProfileStore.ActiveName ?? "?")
                        + "' has no profile with that name - Smart Stow treats that container as unassigned for now. "
                        + "Nothing was deleted; switching back to the Stow Profile that has it restores the mapping.");
            }
            return name;
        }

        private static string DescribeBag(Thing bag)
        {
            string what = null;
            try { what = bag.DisplayName; } catch { }
            if (string.IsNullOrEmpty(what)) { try { what = bag.PrefabName; } catch { } }
            return what ?? "a container";
        }

        private static int Cap(int value, int cap)
        {
            return value > cap ? cap : value;
        }

        private static int IndexOfAffBag(Thing bag)
        {
            for (int i = 0; i < _affBags.Count; i++)
                if (ReferenceEquals(_affBags[i].Bag, bag)) return i;
            return -1;
        }

        private static int IndexOfGenBag(Thing bag)
        {
            for (int i = 0; i < _genBags.Count; i++)
                if (ReferenceEquals(_genBags[i].Bag, bag)) return i;
            return -1;
        }

        private static int IndexOfBagMemo(Thing bag)
        {
            for (int i = 0; i < _bagMemos.Count; i++)
                if (ReferenceEquals(_bagMemos[i].Bag, bag)) return i;
            return -1;
        }

        /// <summary>"profile: X" with a one-entry cache (see the field comment).</summary>
        private static string ProfileReason(string name)
        {
            if (!ReferenceEquals(name, _profileReasonName) || _profileReason == null)
            {
                _profileReasonName = name;
                _profileReason = "profile: " + name;
            }
            return _profileReason;
        }

        /// <summary>"bag default: X" with a one-entry cache.</summary>
        private static string DefaultReason(string name)
        {
            if (!ReferenceEquals(name, _defaultReasonName) || _defaultReason == null)
            {
                _defaultReasonName = name;
                _defaultReason = "bag default: " + name;
            }
            return _defaultReason;
        }

        /// <summary>Profile lookup by name without BagProfileStore's LINQ FirstOrDefault
        /// allocation (this runs several times per resolve). Same first-match semantics.</summary>
        private static BagProfile FindProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            List<BagProfile> list = BagProfileStore.Profiles;
            for (int i = 0; i < list.Count; i++)
            {
                BagProfile p = list[i];
                if (p != null && p.Name == name) return p;
            }
            return null;
        }

        /// <summary>Home-slot-aware pick for the worn tool belt (feature 1B.2, ported verbatim).
        /// When ToolbeltHomeSlots is on, seed the belt's binding table on first sight (mod-local,
        /// idempotent — NOT game state) and prefer this tool type's remembered home slot when it
        /// is free and accepts the tool; otherwise the best free direct slot. Placement is
        /// auto-recorded by BeltBindingStore's Slot.Take postfix after the move settles.
        /// A DRY resolve never seeds: seeding writes the bindings file, and seeding at
        /// diagnostic time could bake a mid-reorganization belt arrangement a later REAL
        /// G press then honours. Dry runs still READ any existing table (HomeSlotFor), so
        /// the predicted slot matches what execution would pick.</summary>
        private static Slot HomeOrBestDirectSlot(DynamicThing belt, DynamicThing held, bool dryRun)
        {
            if (belt == null || belt.Slots == null || held == null) return null;
            if (UIAConfig.ToolbeltHomeSlots.Value)
            {
                if (!dryRun) BeltBindingStore.SeedIfNew(belt);
                int home = BeltBindingStore.HomeSlotFor(belt, held);
                if (home >= 0)
                {
                    foreach (Slot s in belt.Slots)
                    {
                        if (s == null || s.SlotIndex != home) continue;
                        // Home slot found: use it only if genuinely free and type-gated open; else
                        // fall through to BestDirectSlot (the postfix rebinds home on landing).
                        if (!s.IsLocked && s.Get() == null && IsDestSlot(s)
                            && !BagProfileGate.IsDeviceSlot(belt, s) && Slot.AllowMove(held, s)) return s;
                        break;
                    }
                }
            }
            return BestDirectSlot(belt, held);
        }

        /// <summary>The best empty, movable direct slot of a worn container (ported verbatim):
        /// a slot whose Class matches the item's SlotType wins over a generic slot, so a tool
        /// lands in a real tool slot when one is free but still accepts an open normal slot
        /// otherwise. Only this container's OWN slots — never nested.</summary>
        private static Slot BestDirectSlot(DynamicThing container, DynamicThing held)
        {
            if (container == null || container.Slots == null || held == null) return null;
            Slot generic = null;
            foreach (Slot s in container.Slots)
            {
                if (s == null || s.IsLocked || s.Get() != null) continue;
                if (!IsDestSlot(s)) continue;   // D-005: never a sealed slot
                // Belt-and-braces (2026-09-26): a tool/coil can't type-pass a propellant/battery/chip
                // socket anyway, but a worn jetpack or survival belt socket is never a "direct slot".
                if (BagProfileGate.IsDeviceSlot(container, s)) continue;
                if (!Slot.AllowMove(held, s)) continue;
                if (s.Type == held.SlotType) return s;   // exact type match — the tool's real home
                if (generic == null) generic = s;        // kept as fallback
            }
            return generic;
        }
    }
}
