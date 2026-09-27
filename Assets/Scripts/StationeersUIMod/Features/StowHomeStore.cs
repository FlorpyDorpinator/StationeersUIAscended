using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Serialization;
using Assets.Scripts;                     // GameManager (world-exit detection only)
using Assets.Scripts.GridSystem;          // GameState (same)
using Assets.Scripts.Networking;          // NetworkManager (client/host branch for GC only)
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;      // Stackable (split-inherit quantity guard)
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    // ---------- XML model (SmartStow-Simple-Refactor-Plan §3.2) ----------

    [XmlRoot("StowHomes")]
    public class StowHomeFile
    {
        [XmlAttribute("schema")] public int Schema = 1;
        [XmlAttribute("world")] public string World;      // the raw World.CurrentId (or fallback key)
        [XmlAttribute("station")] public string Station;  // for humans reading the folder; never a key
        [XmlAttribute("saved")] public string Saved;      // ISO timestamp of the last flush
        [XmlElement("H")] public List<StowHomeRecord> Homes = new List<StowHomeRecord>();
    }

    public class StowHomeRecord
    {
        [XmlAttribute("i")] public long ItemId;           // item Thing.ReferenceId
        [XmlAttribute("ip")] public int ItemPrefab;       // item PrefabHash (guards re-issued ids)
        [XmlAttribute("c")] public long ContainerId;      // container ReferenceId; 0 = the local human's worn slots
        [XmlAttribute("cp")] public int ContainerPrefab;  // container PrefabHash (same guard); 0 with c=0
        [XmlAttribute("s")] public int SlotIndex;         // Slot.SlotIndex ("that spot")
        [XmlAttribute("src")] public string Source;       // snap | seed | drag | split | merge (diagnostics only)
        [XmlAttribute("t")] public string HomedAt;        // yyyy-MM-dd of the last (re)home
        [XmlAttribute("seen")] public string SeenAt;      // yyyy-MM-dd the item was last found (GC)
    }

    /// <summary>
    /// Simple SmartStow's per-ITEM-INSTANCE "home" memory (0.9.8.0 P0/P1, OBSERVE-ONLY phase).
    ///
    /// <para>An item's home is the exact slot the player last deliberately put it in:
    /// <c>(container ReferenceId, SlotIndex)</c>, with the sentinel container id 0 meaning "the local
    /// human's own worn slots". Entries carry both PrefabHashes so a ReferenceId re-issued after a
    /// reload-without-save can never point a home at a different thing (plan §3.4). The capture rule
    /// in one line (S-3, option C): <b>explicit placements rehome; mechanical landings only seed a
    /// first home, or refresh the slot index inside the same bag; taking an item out never changes
    /// its home.</b></para>
    ///
    /// <para><b>Who calls what.</b> The landing observer is a Harmony postfix on
    /// <c>Slot.Take(DynamicThing)</c> ([27798] Assets.Scripts.Objects/Slot.cs:365) in
    /// <c>Core/VanillaPlacementPatches.cs</c> — a NEW postfix beside (never replacing)
    /// <see cref="BeltBindingStore.SlotTakePatch"/>; Harmony runs both. Explicit intents come from
    /// prefixes on the five vanilla <c>Slot.Player*</c> gestures (same file) and — in the LATER
    /// resolver/executor phase — from <c>Core/ItemActions.cs</c> dispatch sites calling
    /// <see cref="NoteItemIntent"/> / <see cref="NoteContainerIntent"/> /
    /// <see cref="NoteSplitIntent"/> just before their authoritative send (see the doc comment on
    /// each Note* method for the exact planned call sites). Stack lineage: a postfix on
    /// <c>Stackable.OnSplitStack</c> ([27798] Assets.Scripts.Objects.Items/Stackable.cs:379, host
    /// only) and a prefix on the static funnel <c>Thing.Merge(IMergeable, IMergeable)</c>
    /// ([27798] Assets.Scripts.Objects/Thing.cs:3594).</para>
    ///
    /// <para><b>Multiplayer safety (CLAUDE.md rule 3).</b> This class NEVER mutates game state — it
    /// only observes the networked occupant-set funnel and reads networked identity (ReferenceId,
    /// SlotIndex, PrefabHash, <c>RootParentHuman</c>, <c>World.CurrentId</c>). The file is
    /// client-local config XML.</para>
    ///
    /// <para><b>Persistence.</b> One file per world at
    /// <c>BepInEx/config/StationeersUIMod/StowHomes/&lt;worldKey&gt;.xml</c> via
    /// <see cref="SaveScopedXmlStore"/>, keyed by <see cref="WorldKey"/> (never the station name; a
    /// null key is NEVER passed down — no key means memory-only). Writes are debounced ~2 s after
    /// the last change and forced on world stand-down and <see cref="Reset"/>.</para>
    ///
    /// <para><b>Hot reload.</b> Every static here is dropped by <see cref="Reset"/>, which the mod's
    /// Shutdown path calls (after flushing).</para>
    /// </summary>
    public static class StowHomeStore
    {
        // ---------- in-memory model ----------

        private sealed class HomeEntry
        {
            public int ItemPrefab;
            public long ContainerId;      // 0 = local human's worn slots
            public int ContainerPrefab;   // 0 with ContainerId 0
            public int SlotIndex;
            public string Source;
            public string HomedAt;        // yyyy-MM-dd
            public string SeenAt;         // yyyy-MM-dd
        }

        private static readonly Dictionary<long, HomeEntry> _homes = new Dictionary<long, HomeEntry>();

        private const string StoreFolder = "StowHomes";   // ConfigTreeRepair.Folders() lists this literal
        private const string StoreLabel = "Stow homes";
        private const int MaxEntries = 4096;              // cap (plan §7.5); oldest `seen` evicted first
        private const int ClientUnseenPruneDays = 30;     // client-side GC window (tunable)
        private const float FlushDelaySeconds = 2f;       // debounce (plan §7.3)
        private const float SettleSeconds = 3f;           // world-ready settle (plan §4.1)
        private const float IntentSeconds = 3f;           // intent expiry (0.9.7.3 precedent, §4.3)
        private const int MaxParentHops = 8;              // observer's bounded parent walk (§4.3)

        // ---------- persistence state ----------

        private static string _loadedKey;                 // world key of the loaded file; null = nothing/memory-only
        private static bool _hadFileOnLoad;               // file existed when this key loaded (snap vs seed)
        private static bool _dirty;
        private static float _dirtyAt;                    // unscaled time of the FIRST unflushed change

        // ---------- world-ready pump state ----------

        private static float _readySince = -1f;           // first frame CanDraw held; -1 = not holding
        private static bool _worldReady;                  // the observer's one-read master gate
        private static string _snappedKey;                // (key, human) of the last snapshot run
        private static long _snappedHumanId;
        private static bool _snappedThisWorld;            // false until the first snapshot after ready

        // ---------- mechanical scope ----------

        private static int _mechanicalDepth;

        // ---------- one-time mode notice (0.9.8.0, plan §9.3) ----------

        /// <summary>Session latch for <see cref="MaybeShowModeNotice"/> — true once the persisted
        /// flag says shown (or was just set), so the per-frame check costs one bool read.</summary>
        private static bool _modeNoticeDone;

        // ---------- intent tables (pooled structs, no alloc — plan §4.3) ----------

        private struct ItemIntent { public long ItemId; public long ContainerId; public int SlotIndex; public float Until; }
        private struct ContainerIntent { public long ContainerId; public float Until; }
        private struct SplitIntent { public long SourceId; public int PrefabHash; public int MaxQuantity; public float Until; }
        private struct RoutedNote { public long ItemId; public float Until; }

        // 16 item intents (not 8): a laggy MP client can hold that many drags in flight before the
        // first server echo lands, and an overflowed intent silently degrades to mechanical.
        private static readonly ItemIntent[] _itemIntents = new ItemIntent[16];
        private static readonly ContainerIntent[] _containerIntents = new ContainerIntent[4];
        private static readonly SplitIntent[] _splitIntents = new SplitIntent[4];
        private static readonly RoutedNote[] _routedNotes = new RoutedNote[8];
        private static int _itemIntentNext, _containerIntentNext, _splitIntentNext, _routedNoteNext;

        // ---------- stats (read by `stowhomes`; reset with everything else) ----------

        public static int StatLandingsSeen;      // landings that reached the local player's inventory
        public static int StatSeeds;             // first homes / re-seeds of dead homes
        public static int StatSlotRefreshes;     // same-bag slot index refreshes
        public static int StatRehomes;           // explicit rehomes (intent-matched or API)
        public static int StatSplitInherits;     // split children that inherited a home
        public static int StatMergeInherits;     // merge survivors that inherited a home
        public static int StatIntentsNoted;
        public static int StatIntentsMatched;
        public static int StatRoutedSuppressed;  // routed landings kept mechanical (never intent-rehomed)
        public static int StatGcPruned;
        public static int StatSnapshots;

        // =====================================================================================
        // Public surface — the P2/P3 resolver/executor phase consumes these names VERBATIM.
        // =====================================================================================

        /// <summary>How many home entries the current world's table holds.</summary>
        public static int HomesCount
        {
            get { EnsureLoaded(); return _homes.Count; }
        }

        /// <summary>True while the observer records landings (world ready + settled).</summary>
        public static bool WorldReady { get { return _worldReady; } }

        /// <summary>
        /// Full plan-§3.4 validation of <paramref name="held"/>'s home for THIS press.
        /// True when a home entry exists, its item PrefabHash matches, and its container resolves
        /// (<c>Referencable.Find&lt;Thing&gt;</c> + container PrefabHash match, or container id 0 →
        /// the local human), is on the local player (<c>RootParentHuman</c>), is not stow-excluded
        /// (S-10), is real storage, and has no body-bag ancestor.
        ///
        /// <para><paramref name="exactSlot"/> is the remembered slot when its index still resolves
        /// on the container and the slot is not locked (and, on the human, not a hand). It may be
        /// OCCUPIED — occupancy is resolver policy (§6.1 b/c: a matching partial stack there means
        /// merge), and every mutation re-gates through <c>Slot.AllowMove</c>/<c>Slot.CanMerge</c> at
        /// execute time. <paramref name="exactSlot"/> is null when only the bag survives (the slot
        /// index no longer exists, or is locked/blocked) — the home CONTAINER is still returned so
        /// the resolver can do the same-bag fallback (S-1).</para>
        ///
        /// <para>Read-only apart from refreshing the entry's <c>seen</c> stamp. Never mutates game
        /// state.</para>
        /// </summary>
        public static bool TryGetLiveHome(DynamicThing held, out Thing container, out Slot exactSlot)
        {
            container = null;
            exactSlot = null;
            if (held == null) return false;
            EnsureLoaded();

            HomeEntry e;
            if (!_homes.TryGetValue(SafeRefId(held), out e)) return false;
            if (e == null || e.ItemPrefab != SafePrefabHash(held)) return false;   // re-issued id (§3.4.1)

            Human human = Guards.LocalHuman;
            if (human == null) return false;

            Thing home;
            if (e.ContainerId == 0)
            {
                home = human;                                                       // "my worn slots"
            }
            else
            {
                // [27798] Referencable.cs:145 — Find<T> by ReferenceId (global-namespace class).
                DynamicThing dt = Referencable.Find<Thing>(e.ContainerId) as DynamicThing;
                if (dt == null || SafePrefabHash(dt) != e.ContainerPrefab) return false;  // destroyed / re-issued (§3.4.2)
                if (!ReferenceEquals(dt.RootParentHuman, human)) return false;      // on the local player (§3.4.3)
                if (HasOffLimitsAncestorOrSelf(dt)) return false;                   // body bag (§3.4.4)
                if (!UI.Grid.GridModel.IsStorageContainer(dt)) return false;        // still real storage (S-9)
                if (BagProfileStore.IsStowExcluded(dt)) return false;               // S-10
                home = dt;
            }

            container = home;
            TouchSeen(e);

            List<Slot> slots = home.Slots;
            if (slots != null && e.SlotIndex >= 0 && e.SlotIndex < slots.Count)     // §3.4.5
            {
                Slot s = slots[e.SlotIndex];
                if (s != null && !s.IsLocked && !(e.ContainerId == 0 && s.IsHandSlot))
                    exactSlot = s;
            }
            return true;
        }

        /// <summary>
        /// MECHANICAL landing (S-3): give <paramref name="item"/> its FIRST home in
        /// <paramref name="landedSlot"/>, or refresh its slot index when the landing is inside its
        /// existing home bag. NEVER overwrites a live home in another bag ("visiting"). A home whose
        /// container no longer resolves (or whose PrefabHash mismatches — a re-issued id) counts as
        /// dead and is re-seeded. No-op for an ineligible slot (hands, tools, packaging, body bags,
        /// excluded containers, anything not on the local player — §3.3).
        /// </summary>
        public static void SeedHome(DynamicThing item, Slot landedSlot, string src)
        {
            if (item == null || landedSlot == null) return;
            Human human = Guards.LocalHuman;
            if (human == null) return;
            Thing parent = landedSlot.Parent;
            if (parent == null || !IsEligibleHomeSlot(landedSlot, parent, human)) return;
            EnsureLoaded();

            long cid; int cp;
            if (!ContainerIdentity(parent, human, out cid, out cp)) return;

            long itemId = SafeRefId(item);
            int itemPrefab = SafePrefabHash(item);
            HomeEntry e;
            if (_homes.TryGetValue(itemId, out e) && e != null && e.ItemPrefab == itemPrefab)
            {
                if (e.ContainerId == cid)
                {
                    int idx = landedSlot.SlotIndex;
                    if (e.SlotIndex != idx)
                    {
                        e.SlotIndex = idx;                    // same-bag slot refresh (sorts, reshuffles)
                        e.HomedAt = Today();
                        StatSlotRefreshes++;
                        MarkDirty();
                    }
                    TouchSeen(e);
                    return;
                }
                if (IsHomeContainerAlive(e)) { TouchSeen(e); return; }   // visiting: a live home is never stolen
            }
            // First home, dead home, or a re-issued item id: (re)seed.
            WriteEntry(itemId, itemPrefab, cid, cp, landedSlot.SlotIndex, src);
            StatSeeds++;
        }

        /// <summary>
        /// EXPLICIT placement (S-3): the player chose this slot, so it becomes the item's home,
        /// overwriting any previous one. Still bounded by eligibility (§3.3) — an explicit drag
        /// into a tool, packaging or a body bag sets nothing (and clears nothing: the old home
        /// simply goes dormant).
        /// </summary>
        public static void Rehome(DynamicThing item, Slot slot, string src)
        {
            if (item == null || slot == null) return;
            Human human = Guards.LocalHuman;
            if (human == null) return;
            Thing parent = slot.Parent;
            if (parent == null || !IsEligibleHomeSlot(slot, parent, human)) return;
            EnsureLoaded();

            long cid; int cp;
            if (!ContainerIdentity(parent, human, out cid, out cp)) return;
            WriteEntry(SafeRefId(item), SafePrefabHash(item), cid, cp, slot.SlotIndex, src);
            StatRehomes++;
        }

        // ---------- intents (plan §4.3) ----------

        /// <summary>
        /// Register "the player is deliberately sending item X into (container, slot)". Called just
        /// BEFORE the authoritative send; the matching <c>Slot.Take</c> landing (the server echo on
        /// an MP client) then rehomes. One-shot; expires after 3 s unscaled.
        /// <para>Callers today: the <c>Slot.Player*</c> prefixes in
        /// <c>Core/VanillaPlacementPatches.cs</c>. Callers the P2/P3 phase ADDS (plan §4.2), each
        /// immediately before its authoritative send in <c>Core/ItemActions.cs</c>:
        /// <c>DragTo</c> / <c>SwapIntoSlot</c> / <c>WorldDragTo</c> (drags, both sides of a
        /// bag-to-bag drag-swap), <c>StowActiveHandTo</c> and <c>MoveWorldItemToSlot</c> (chosen
        /// destinations), and the radial/equipment-key stow sites that funnel through them.</para>
        /// </summary>
        public static void NoteItemIntent(long itemRefId, long destContainerRefId, int slotIndex)
        {
            if (itemRefId == 0 || slotIndex < 0) return;
            ItemIntent e;
            e.ItemId = itemRefId;
            e.ContainerId = destContainerRefId;
            e.SlotIndex = slotIndex;
            e.Until = UnityEngine.Time.unscaledTime + IntentSeconds;
            _itemIntents[_itemIntentNext] = e;
            _itemIntentNext = (_itemIntentNext + 1) % _itemIntents.Length;
            StatIntentsNoted++;
        }

        /// <summary>
        /// Register "the player chose CONTAINER as a destination for a bulk move" (MoveAll /
        /// MoveAllOfType tails, insert-into-container, and — in P2/P3 — the D-018 bulk action in
        /// <c>ItemActions</c>). Every landing in that container inside the window rehomes; NOT
        /// consumed per landing, expires after 3 s unscaled.
        /// </summary>
        public static void NoteContainerIntent(long containerRefId)
        {
            if (containerRefId < 0) return;   // 0 (the local human) is a valid destination
            ContainerIntent e;
            e.ContainerId = containerRefId;
            e.Until = UnityEngine.Time.unscaledTime + IntentSeconds;
            _containerIntents[_containerIntentNext] = e;
            _containerIntentNext = (_containerIntentNext + 1) % _containerIntents.Length;
            StatIntentsNoted++;
        }

        /// <summary>
        /// MP-client split inference (plan §5.3): register "a split of source stack S (prefab P) was
        /// just dispatched". When a HOMELESS stack of prefab P then lands in a local hand inside the
        /// window, it inherits S's home (src=split). Host/SP splits do not need this — the
        /// <c>Stackable.OnSplitStack</c> postfix copies deterministically. Expires after 3 s.
        /// <para>This 2-arg overload accepts ANY quantity on the landing stack. Callers that know
        /// the source's PRE-SPLIT quantity should use the 3-arg overload, which lets the matcher
        /// reject a same-prefab stack too big to be this split's child (a picked-up floor stack
        /// must not steal the inheritance).</para>
        /// <para>Callers the P2/P3 phase ADDS: <c>ItemActions.SplitStack</c> and the vanilla
        /// Button1/Button2 split dispatch documented in <c>ItemActions.cs</c>.</para>
        /// </summary>
        public static void NoteSplitIntent(long sourceRefId, int prefabHash)
        {
            NoteSplitIntent(sourceRefId, prefabHash, int.MaxValue);
        }

        /// <summary>As <see cref="NoteSplitIntent(long,int)"/>, with the source stack's PRE-SPLIT
        /// quantity: only a landing stack whose quantity is &lt;= this may inherit.</summary>
        public static void NoteSplitIntent(long sourceRefId, int prefabHash, int sourceQuantityBeforeSplit)
        {
            if (sourceRefId == 0) return;
            SplitIntent e;
            e.SourceId = sourceRefId;
            e.PrefabHash = prefabHash;
            e.MaxQuantity = sourceQuantityBeforeSplit > 0 ? sourceQuantityBeforeSplit : int.MaxValue;
            e.Until = UnityEngine.Time.unscaledTime + IntentSeconds;
            _splitIntents[_splitIntentNext] = e;
            _splitIntentNext = (_splitIntentNext + 1) % _splitIntents.Length;
            StatIntentsNoted++;
        }

        /// <summary>
        /// Register "the MOD's own router is about to dispatch item X" — the resolver/executor
        /// calls this immediately before EACH routed send (the home/fallback move, the merge, and
        /// every #13 continuation step). A landing whose item carries a live routed note is
        /// MECHANICAL BY DEFINITION: it is checked before intent matching in
        /// <see cref="ObserveLanding"/>, so a G fallback landing into a container that still holds
        /// a live bulk-drag CONTAINER intent can only SEED, never be rehomed — "visiting never
        /// steals a home". Notes are consumed by their landing (one dispatch = one landing) and
        /// expire after ~3 s unscaled. Pooled ring, no allocation.
        /// </summary>
        public static void NoteRoutedLanding(long itemRefId)
        {
            if (itemRefId == 0) return;
            RoutedNote e;
            e.ItemId = itemRefId;
            e.Until = UnityEngine.Time.unscaledTime + IntentSeconds;
            _routedNotes[_routedNoteNext] = e;
            _routedNoteNext = (_routedNoteNext + 1) % _routedNotes.Length;
        }

        // ---------- mechanical scope ----------

        /// <summary>
        /// Enter the MECHANICAL scope: landings (host-synchronous) and <c>Slot.Player*</c> intent
        /// prefixes fired inside it are ignored, so an internal <c>PlayerSwapToSlot</c> call (the
        /// displaced-hand swap in <c>ItemActions.EquipToActiveHand</c>) can never masquerade as an
        /// explicit player gesture. Re-entrant; ALWAYS pair with <see cref="EndMechanical"/> in a
        /// finally block. The P2/P3 phase wraps the relevant <c>ItemActions</c> internals.
        /// </summary>
        public static void BeginMechanical()
        {
            _mechanicalDepth++;
        }

        /// <summary>Leave the mechanical scope (see <see cref="BeginMechanical"/>).</summary>
        public static void EndMechanical()
        {
            if (_mechanicalDepth > 0) _mechanicalDepth--;
        }

        /// <summary>True while inside <see cref="BeginMechanical"/>/<see cref="EndMechanical"/>.</summary>
        public static bool InMechanicalScope { get { return _mechanicalDepth > 0; } }

        // ---------- per-frame pump ----------

        /// <summary>
        /// The snapshot/top-up pump (plan §4.1), called once per frame from the mod's Update path.
        /// Cheap early-outs every frame; the expensive walk runs only on the FIRST world-ready
        /// (Guards.CanDraw holding ~3 s so join fragments and deferred placements settle) and again
        /// whenever the world key or the local human changes (respawn). Also services the debounced
        /// flush, and flushes when the world stands down.
        /// </summary>
        public static void Tick()
        {
            if (!Guards.CanDraw())
            {
                if (_worldReady)
                {
                    // A mere CanDraw BLIP (F1 hide, death fade, a menu) in the SAME running world:
                    // stand the observer down and write what we have, but deliberately KEEP the
                    // snapshot latches — re-running Snapshot() on every blip would re-seed
                    // everything carried and silently undo ForgetThisWorld within seconds. The
                    // ready-path (key, human) comparison below still re-snapshots on a respawn.
                    _worldReady = false;
                    ClearIntents();            // conservative: a stale intent must never read explicit
                    if (_dirty) FlushNow();    // world stood down: write what we have
                }
                // A REAL world exit (GameState no longer Running — not a hidden UI) retires the
                // loaded table AND the snapshot latches: the next world must re-snapshot, and a
                // NEXT world that has no key yet (unsaved legacy SP) must start from an empty
                // memory-only table, never seed into — or later flush over — the previous world's
                // file. Latches reset unconditionally here: ReferenceIds restart per world, so a
                // stale (key, human) pair could otherwise suppress the next world's first snapshot.
                bool running = false;
                try { running = GameManager.GameState == GameState.Running; } catch { }
                if (!running)
                {
                    _snappedKey = null;
                    _snappedHumanId = 0;
                    _snappedThisWorld = false;
                    if (_loadedKey != null || _homes.Count > 0)
                    {
                        if (_dirty) FlushNow();
                        _homes.Clear();
                        _loadedKey = null;
                        _hadFileOnLoad = false;
                    }
                }
                _readySince = -1f;
                return;
            }

            float now = UnityEngine.Time.unscaledTime;
            if (_readySince < 0f) _readySince = now;
            if (!_worldReady)
            {
                if (now - _readySince < SettleSeconds) return;
                _worldReady = true;
            }

            EnsureLoaded();   // re-checks the world key; a legacy world gaining its first key loads here

            MaybeShowModeNotice();   // one-time 0.9.8.0 Smart Stow mode notice, first world-ready

            if (_dirty && now - _dirtyAt >= FlushDelaySeconds) FlushNow();

            Human human = Guards.LocalHuman;
            long humanId = human != null ? SafeRefId(human) : 0;
            string keyNow = _loadedKey != null ? _loadedKey : "(memory)";
            if (humanId != 0
                && (!string.Equals(keyNow, _snappedKey, StringComparison.Ordinal) || humanId != _snappedHumanId))
            {
                _snappedKey = keyNow;
                _snappedHumanId = humanId;
                try { RunGc(); } catch (Exception e) { UIALog.Warn("Stow homes GC failed: " + e.Message); }
                try { Snapshot(); } catch (Exception e) { UIALog.Warn("Stow homes snapshot failed: " + e.Message); }
            }
        }

        /// <summary>The one-time Smart Stow mode notice (plan §9.3, wording updated to the binding
        /// "Simple / Complex" rename from Build-Decisions): shown on the first world-ready after
        /// the 0.9.8.0 update — the migration step has already stamped existing installs (S-8C),
        /// so <see cref="StowModeConfig.Mode"/> is final by the time a world is running. Rides the
        /// existing center-screen <see cref="Overlay.Toast"/>; persisted via
        /// <c>SmartStowModeNoticeShown</c> so it fires once per install, ever. Fail-soft: keys not
        /// bound yet -> try again next frame (cheap); a throw latches off for this session rather
        /// than retry-spamming.</summary>
        private static void MaybeShowModeNotice()
        {
            if (_modeNoticeDone) return;
            try
            {
                if (!StowModeConfig.Available) return;   // keys not up yet: retry next ready frame
                if (StowModeConfig.ModeNoticeShown) { _modeNoticeDone = true; return; }
                string msg = StowModeConfig.Mode == StowMode.Complex
                    ? "You use Bag Profiles, so Smart Stow stays in Complex mode. Try Simple in F10 > SmartStow."
                    : "Smart Stow now returns items to where you took them from. Prefer Bag Profiles? Switch to Complex in F10 > SmartStow.";
                Overlay.Toast.Show(msg, Overlay.Theme.TextPrimary, 8f);
                StowModeConfig.ModeNoticeShown = true;
                _modeNoticeDone = true;
            }
            catch { _modeNoticeDone = true; }
        }

        // ---------- diagnostics ----------

        /// <summary>
        /// Human-readable provenance of <paramref name="item"/>'s entry for `stowhomes` / the F10
        /// Simple page ("home: Backpack ... slot 4 (you put it)"). False when the item has no entry
        /// (or a prefab-mismatched one). ASCII only; allocation is fine here (console path).
        /// </summary>
        public static bool TryDescribeEntry(DynamicThing item, out string provenance)
        {
            provenance = null;
            if (item == null) return false;
            EnsureLoaded();
            HomeEntry e;
            if (!_homes.TryGetValue(SafeRefId(item), out e) || e == null) return false;
            if (e.ItemPrefab != SafePrefabHash(item)) return false;

            string where;
            string verdict;
            if (e.ContainerId == 0)
            {
                where = "worn slots (self)";
                verdict = Guards.LocalHuman != null ? "LIVE" : "no local player";
            }
            else
            {
                DynamicThing dt = Referencable.Find<Thing>(e.ContainerId) as DynamicThing;
                if (dt == null) { where = "container ref " + e.ContainerId; verdict = "DEAD (container gone)"; }
                else if (SafePrefabHash(dt) != e.ContainerPrefab) { where = "container ref " + e.ContainerId; verdict = "DEAD (id re-issued)"; }
                else
                {
                    where = SafeName(dt) + " ref " + e.ContainerId;
                    Human human = Guards.LocalHuman;
                    if (human == null || !ReferenceEquals(dt.RootParentHuman, human)) verdict = "DORMANT (not on you)";
                    else if (BagProfileStore.IsStowExcluded(dt)) verdict = "BLOCKED (never-stow container)";
                    else if (!UI.Grid.GridModel.IsStorageContainer(dt)) verdict = "DEAD (no longer storage)";
                    else verdict = "LIVE";
                }
            }
            provenance = where + " slot " + e.SlotIndex
                + " [src " + (e.Source ?? "?") + ", homed " + (e.HomedAt ?? "?")
                + ", seen " + (e.SeenAt ?? "?") + "] " + verdict;
            return true;
        }

        /// <summary>Per-container entry counts for `stowhomes` (containerId → count; 0 = worn).</summary>
        public static void GetPerContainerCounts(Dictionary<long, int> into)
        {
            if (into == null) return;
            into.Clear();
            EnsureLoaded();
            foreach (KeyValuePair<long, HomeEntry> kv in _homes)
            {
                int n;
                into.TryGetValue(kv.Value.ContainerId, out n);
                into[kv.Value.ContainerId] = n + 1;
            }
        }

        /// <summary>Live (unexpired) intent counts for `stowhomes`.</summary>
        public static void GetIntentCounts(out int item, out int container, out int split)
        {
            float now = UnityEngine.Time.unscaledTime;
            item = 0; container = 0; split = 0;
            for (int i = 0; i < _itemIntents.Length; i++) if (_itemIntents[i].ItemId != 0 && now <= _itemIntents[i].Until) item++;
            for (int i = 0; i < _containerIntents.Length; i++) if (_containerIntents[i].Until > 0f && now <= _containerIntents[i].Until) container++;
            for (int i = 0; i < _splitIntents.Length; i++) if (_splitIntents[i].SourceId != 0 && now <= _splitIntents[i].Until) split++;
        }

        /// <summary>The loaded world key for diagnostics, or null while memory-only.</summary>
        public static string LoadedKey { get { EnsureLoaded(); return _loadedKey; } }

        // ---------- maintenance (the F10 Return Home page's two buttons) ----------

        /// <summary>
        /// "Forget homes in this world": drop EVERY remembered home for the loaded world — the
        /// in-memory table, the intent rings, and the on-disk file (re-written empty, so the wipe
        /// survives a crash). The snapshot latches are deliberately LEFT SET: the top-up pump only
        /// re-fires on a world-key or human change, so a wipe is not silently undone next frame by
        /// a re-snapshot of everything carried — homes re-learn one placement at a time, exactly
        /// like a fresh item. (<see cref="RelearnFromCarried"/> is the explicit opposite.)
        /// Returns how many entries were dropped.
        /// </summary>
        public static int ForgetThisWorld()
        {
            EnsureLoaded();
            int dropped = _homes.Count;
            _homes.Clear();
            ClearIntents();
            // The wipe is a CHANGE: mark dirty, then flush. FlushNow clears the flag only when the
            // (now empty) file actually landed on disk — a locked file leaves _dirty set, so the
            // Tick debounce retries and the wipe still survives a crash once a retry succeeds.
            MarkDirty();
            try { FlushNow(); } catch (Exception e) { UIALog.Warn("Stow homes wipe flush failed: " + e.Message); }
            UIALog.Info("Stow homes: forgot " + dropped + " home(s) for "
                + (_loadedKey != null ? "'" + _loadedKey + "'" : "(memory only)") + ".");
            return dropped;
        }

        /// <summary>
        /// "Re-learn homes from what I carry now": <see cref="ForgetThisWorld"/>, then one
        /// immediate snapshot of the current inventory — every eligible carried item's CURRENT
        /// slot becomes its home. Returns the new entry count.
        /// </summary>
        public static int RelearnFromCarried()
        {
            ForgetThisWorld();
            try { Snapshot(); } catch (Exception e) { UIALog.Warn("Stow homes re-learn failed: " + e.Message); }
            return _homes.Count;
        }

        // ---------- teardown ----------

        /// <summary>
        /// Hot-reload / shutdown: flush a dirty table, then drop EVERY static — homes, key, dirty
        /// state, ready latches, snapshot latches, mechanical depth, all three intent rings and the
        /// stats. A double-F6 must leave nothing behind (disk files survive, of course).
        /// </summary>
        public static void Reset()
        {
            try { if (_dirty) FlushNow(); } catch { }
            _homes.Clear();
            _loadedKey = null;
            _hadFileOnLoad = false;
            _dirty = false;
            _dirtyAt = 0f;
            _readySince = -1f;
            _worldReady = false;
            _snappedKey = null;
            _snappedHumanId = 0;
            _snappedThisWorld = false;
            _mechanicalDepth = 0;
            _modeNoticeDone = false;
            ClearIntents();
            StatLandingsSeen = 0; StatSeeds = 0; StatSlotRefreshes = 0; StatRehomes = 0;
            StatSplitInherits = 0; StatMergeInherits = 0; StatIntentsNoted = 0; StatIntentsMatched = 0;
            StatRoutedSuppressed = 0; StatGcPruned = 0; StatSnapshots = 0;
        }

        // =====================================================================================
        // Observer entry points (called only by Core/VanillaPlacementPatches.cs shims)
        // =====================================================================================

        /// <summary>
        /// The <c>Slot.Take</c> landing commit (plan §4.3). COLD-PATH CHEAP: `Slot.Take` fires for
        /// every chute hop and machine slot on a host, so the common case exits within a few pointer
        /// reads — world not ready, empty landing (`Slot.Empty()` calls `Take(null)`), mechanical
        /// scope, or a parent chain that never reaches the local human within 8 hops. Never mutates
        /// game state.
        /// </summary>
        internal static void ObserveLanding(Slot slot, DynamicThing child)
        {
            if (!_worldReady || _mechanicalDepth > 0) return;
            if (child == null || slot == null) return;
            Thing parent = slot.Parent;
            if (parent == null) return;
            Human human = Guards.LocalHuman;
            if (human == null) return;

            bool offLimits;
            if (!ReachesLocalHuman(parent, human, out offLimits)) return;

            StatLandingsSeen++;
            EnsureLoaded();

            // Where did it land?
            long cid; int cp;
            bool isHand = false;
            if (ReferenceEquals(parent, human))
            {
                if (slot.IsHandSlot) isHand = true;
                cid = 0; cp = 0;
            }
            else
            {
                DynamicThing dt = parent as DynamicThing;
                if (dt == null) return;
                cid = SafeRefId(dt); cp = SafePrefabHash(dt);
            }

            // A hand landing is TAKING — never a home change (S-3). The one exception is the
            // MP-client split inference: a homeless just-appeared stack inherits its source's home.
            if (isHand)
            {
                TryInheritSplitHome(child);
                return;
            }

            // The mod's OWN router dispatched this landing (a G stow / fallback / #13 continuation
            // step): MECHANICAL by definition, checked BEFORE intent matching — otherwise a
            // fallback landing into a bag whose bulk-drag CONTAINER intent is still live would be
            // rehomed src=drag, breaking "visiting never steals a home". The routed note is
            // consumed here (one dispatch = one landing); any item-scoped intent for this item is
            // deliberately NOT consumed — it belongs to the player gesture that registered it and
            // either matches its own echo or expires in 3 s.
            long childId = SafeRefId(child);
            if (ConsumeRoutedNote(childId))
            {
                StatRoutedSuppressed++;
                if (offLimits) return;
                SeedHome(child, slot, "seed");
                return;
            }

            // Explicit intents beat the mechanical rules.
            int slotIndex = slot.SlotIndex;
            if (ConsumeItemIntent(childId, cid, slotIndex) || MatchContainerIntent(cid))
            {
                StatIntentsMatched++;
                Rehome(child, slot, "drag");
                return;
            }

            // Mechanical landing: first home / same-bag slot refresh only (Rehome/Seed validate
            // eligibility themselves; the offLimits result short-circuits the body-bag case here).
            if (offLimits) return;
            SeedHome(child, slot, "seed");
        }

        /// <summary>
        /// Host/SP split lineage (plan §5.2 rule b): the child of a split inherits the source's
        /// home (src=split). Splits run only on the simulation authority, and every override of
        /// <c>OnSplitStack</c> calls base, so this postfix sees them all.
        /// </summary>
        internal static void ObserveSplit(Thing source, Thing newStack)
        {
            if (!_worldReady || source == null || newStack == null) return;
            EnsureLoaded();
            HomeEntry src;
            if (!_homes.TryGetValue(SafeRefId(source), out src) || src == null) return;
            if (src.ItemPrefab != SafePrefabHash(source)) return;   // stale entry from a re-issued id
            long childId = SafeRefId(newStack);
            if (childId == 0) return;
            HomeEntry e = CloneEntry(src);
            e.Source = "split";
            e.HomedAt = Today();
            e.SeenAt = Today();
            e.ItemPrefab = SafePrefabHash(newStack);
            InsertEntry(childId, e);   // cap-checked, marks dirty
            StatSplitInherits++;
        }

        /// <summary>
        /// Merge lineage (plan §5.2 rule e): when the SURVIVOR (<paramref name="parent"/>) has no
        /// live home and the absorbed <paramref name="child"/> has one, the survivor inherits it
        /// (src=merge). Rules a/c/d need no action (ids and entries simply persist; a fully absorbed
        /// child's entry is left to GC). Observed at the <c>Thing.Merge</c> funnel, i.e. for merges
        /// this machine dispatches.
        /// </summary>
        internal static void ObserveMerge(Thing parent, Thing child)
        {
            if (!_worldReady || parent == null || child == null) return;
            EnsureLoaded();
            HomeEntry childEntry;
            if (!_homes.TryGetValue(SafeRefId(child), out childEntry) || childEntry == null) return;
            if (childEntry.ItemPrefab != SafePrefabHash(child)) return;
            if (!IsHomeContainerAlive(childEntry)) return;          // nothing worth inheriting

            long parentId = SafeRefId(parent);
            if (parentId == 0) return;
            HomeEntry parentEntry;
            if (_homes.TryGetValue(parentId, out parentEntry) && parentEntry != null
                && parentEntry.ItemPrefab == SafePrefabHash(parent)
                && IsHomeContainerAlive(parentEntry))
                return;                                             // survivor keeps its own live home (rule c/d)

            HomeEntry e = CloneEntry(childEntry);
            e.Source = "merge";
            e.HomedAt = Today();
            e.SeenAt = Today();
            e.ItemPrefab = SafePrefabHash(parent);
            InsertEntry(parentId, e);   // cap-checked, marks dirty
            StatMergeInherits++;
        }

        /// <summary>
        /// The container id an explicit-gesture prefix should note an intent against, walking the
        /// parent chain (max 8 hops) to the LOCAL human: the human itself → sentinel 0; a
        /// DynamicThing on the local human → its ReferenceId; anything else (another player, a
        /// machine, the world) → false, note nothing.
        /// </summary>
        internal static bool TryGetLocalContainerId(Thing parentOrContainer, out long containerId)
        {
            containerId = -1;
            if (parentOrContainer == null) return false;
            Human human = Guards.LocalHuman;
            if (human == null) return false;
            bool offLimits;
            if (!ReachesLocalHuman(parentOrContainer, human, out offLimits)) return false;
            if (ReferenceEquals(parentOrContainer, human)) { containerId = 0; return true; }
            DynamicThing dt = parentOrContainer as DynamicThing;
            if (dt == null) return false;
            containerId = SafeRefId(dt);
            return containerId != 0;
        }

        // =====================================================================================
        // Internals
        // =====================================================================================

        /// <summary>Walk the parent chain (bounded, alloc-free) looking for the LOCAL human.
        /// <paramref name="offLimits"/> reports whether any hop (starting thing included) is a
        /// body bag (<see cref="InventoryScanner.ContentsOffLimits"/>).</summary>
        private static bool ReachesLocalHuman(Thing start, Human human, out bool offLimits)
        {
            offLimits = false;
            Thing t = start;
            for (int hop = 0; hop < MaxParentHops && t != null; hop++)
            {
                if (ReferenceEquals(t, human)) return true;
                DynamicThing dt = t as DynamicThing;
                if (dt == null) return false;          // a structure/machine chain never reaches a human
                if (InventoryScanner.ContentsOffLimits(dt)) offLimits = true;
                Slot ps = dt.ParentSlot;
                if (ps == null) return false;          // sitting in the world
                t = ps.Parent;
            }
            return false;
        }

        /// <summary>Does any ancestor of <paramref name="dt"/> (itself included) refuse to host
        /// homes (body bag)? Bounded walk, used on the VALIDATION path.</summary>
        private static bool HasOffLimitsAncestorOrSelf(DynamicThing dt)
        {
            Thing t = dt;
            for (int hop = 0; hop < MaxParentHops * 2 && t != null; hop++)
            {
                DynamicThing d = t as DynamicThing;
                if (d == null) return false;           // reached the human (or a structure)
                if (InventoryScanner.ContentsOffLimits(d)) return true;
                Slot ps = d.ParentSlot;
                if (ps == null) return false;
                t = ps.Parent;
            }
            return false;
        }

        /// <summary>Eligibility of a slot as a HOME (plan §3.3 / S-9): the local human's worn slots
        /// (never hands), or an unlocked slot of real storage
        /// (<see cref="UI.Grid.GridModel.IsStorageContainer"/> — which already refuses tools,
        /// single-use packaging and body bags) that is on the local player, not stow-excluded
        /// (S-10) and under no body-bag ancestor.</summary>
        private static bool IsEligibleHomeSlot(Slot slot, Thing parent, Human human)
        {
            if (slot == null || parent == null || human == null) return false;
            if (slot.IsLocked) return false;
            if (ReferenceEquals(parent, human)) return !slot.IsHandSlot;
            DynamicThing dt = parent as DynamicThing;
            if (dt == null) return false;
            if (!UI.Grid.GridModel.IsStorageContainer(dt)) return false;
            if (BagProfileStore.IsStowExcluded(dt)) return false;
            bool offLimits;
            if (!ReachesLocalHuman(dt, human, out offLimits) || offLimits) return false;
            return true;
        }

        /// <summary>(containerId, containerPrefab) for a landed slot's parent: the local human →
        /// (0, 0); a DynamicThing → (ReferenceId, PrefabHash).</summary>
        private static bool ContainerIdentity(Thing parent, Human human, out long cid, out int cp)
        {
            if (ReferenceEquals(parent, human)) { cid = 0; cp = 0; return true; }
            DynamicThing dt = parent as DynamicThing;
            if (dt == null) { cid = -1; cp = 0; return false; }
            cid = SafeRefId(dt);
            cp = SafePrefabHash(dt);
            return cid != 0;
        }

        /// <summary>"Alive" for seed-protection / inheritance (NOT the full at-use validation): the
        /// container still resolves to the same prefab. Deliberately does NOT require it to be on
        /// the player — a bag dropped on the floor is still that item's home (§4.2: pick it back up
        /// and G still returns the item there).</summary>
        private static bool IsHomeContainerAlive(HomeEntry e)
        {
            if (e == null) return false;
            if (e.ContainerId == 0) return Guards.LocalHuman != null;
            DynamicThing dt = Referencable.Find<Thing>(e.ContainerId) as DynamicThing;
            return dt != null && SafePrefabHash(dt) == e.ContainerPrefab;
        }

        private static void WriteEntry(long itemId, int itemPrefab, long cid, int cp, int slotIndex, string src)
        {
            if (itemId == 0) return;
            HomeEntry e;
            if (!_homes.TryGetValue(itemId, out e) || e == null)
            {
                if (_homes.Count >= MaxEntries) EvictOldest();
                e = new HomeEntry();
                _homes[itemId] = e;
            }
            e.ItemPrefab = itemPrefab;
            e.ContainerId = cid;
            e.ContainerPrefab = cp;
            e.SlotIndex = slotIndex;
            e.Source = src;
            e.HomedAt = Today();
            e.SeenAt = Today();
            MarkDirty();
        }

        /// <summary>Insert/replace a ready-made entry (the split/merge lineage paths) under the
        /// same <see cref="MaxEntries"/> cap <see cref="WriteEntry"/> enforces: a NEW key at the
        /// cap evicts the oldest-seen entry first.</summary>
        private static void InsertEntry(long itemId, HomeEntry e)
        {
            if (itemId == 0 || e == null) return;
            if (!_homes.ContainsKey(itemId) && _homes.Count >= MaxEntries) EvictOldest();
            _homes[itemId] = e;
            MarkDirty();
        }

        private static HomeEntry CloneEntry(HomeEntry src)
        {
            HomeEntry e = new HomeEntry();
            e.ItemPrefab = src.ItemPrefab;
            e.ContainerId = src.ContainerId;
            e.ContainerPrefab = src.ContainerPrefab;
            e.SlotIndex = src.SlotIndex;
            e.Source = src.Source;
            e.HomedAt = src.HomedAt;
            e.SeenAt = src.SeenAt;
            return e;
        }

        // ---------- intent matching ----------

        private static bool ConsumeItemIntent(long itemId, long cid, int slotIndex)
        {
            if (itemId == 0) return false;
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < _itemIntents.Length; i++)
            {
                if (_itemIntents[i].ItemId != itemId) continue;
                if (now > _itemIntents[i].Until) continue;
                if (_itemIntents[i].ContainerId != cid || _itemIntents[i].SlotIndex != slotIndex) continue;
                _itemIntents[i].ItemId = 0;
                _itemIntents[i].Until = 0f;
                return true;
            }
            return false;
        }

        private static bool MatchContainerIntent(long cid)
        {
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < _containerIntents.Length; i++)
            {
                if (_containerIntents[i].Until <= 0f || now > _containerIntents[i].Until) continue;
                if (_containerIntents[i].ContainerId == cid) return true;   // NOT consumed: bulk moves land many items
            }
            return false;
        }

        /// <summary>A live routed note for this item? Consumed on match (one dispatch = one landing).</summary>
        private static bool ConsumeRoutedNote(long itemId)
        {
            if (itemId == 0) return false;
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < _routedNotes.Length; i++)
            {
                if (_routedNotes[i].ItemId != itemId || now > _routedNotes[i].Until) continue;
                _routedNotes[i].ItemId = 0;
                _routedNotes[i].Until = 0f;
                return true;
            }
            return false;
        }

        private static void TryInheritSplitHome(DynamicThing child)
        {
            long childId = SafeRefId(child);
            if (childId == 0) return;
            HomeEntry existing;
            if (_homes.TryGetValue(childId, out existing) && existing != null
                && existing.ItemPrefab == SafePrefabHash(child) && IsHomeContainerAlive(existing))
                return;   // already has a live home — nothing to infer

            int prefab = SafePrefabHash(child);
            // The landing stack's size narrows the match: a split child can never carry MORE than
            // the source held before the split, so a bigger same-prefab stack (a picked-up floor
            // stack wandering into the hand inside the window) must not steal the inheritance.
            int childQty = 1;
            Stackable childStack = child as Stackable;
            if (childStack != null)
            {
                try { childQty = childStack.Quantity; } catch { childQty = 1; }
            }
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < _splitIntents.Length; i++)
            {
                if (_splitIntents[i].SourceId == 0 || now > _splitIntents[i].Until) continue;
                if (_splitIntents[i].PrefabHash != prefab) continue;
                if (childQty > _splitIntents[i].MaxQuantity) continue;   // too big to be this split's child
                HomeEntry src;
                if (!_homes.TryGetValue(_splitIntents[i].SourceId, out src) || src == null) continue;
                if (src.ItemPrefab != prefab) continue;
                _splitIntents[i].SourceId = 0;     // consume
                _splitIntents[i].Until = 0f;
                HomeEntry e = CloneEntry(src);
                e.Source = "split";
                e.HomedAt = Today();
                e.SeenAt = Today();
                InsertEntry(childId, e);           // cap-checked, marks dirty
                StatSplitInherits++;
                return;
            }
        }

        private static void ClearIntents()
        {
            Array.Clear(_itemIntents, 0, _itemIntents.Length);
            Array.Clear(_containerIntents, 0, _containerIntents.Length);
            Array.Clear(_splitIntents, 0, _splitIntents.Length);
            Array.Clear(_routedNotes, 0, _routedNotes.Length);
            _itemIntentNext = 0;
            _containerIntentNext = 0;
            _splitIntentNext = 0;
            _routedNoteNext = 0;
        }

        // ---------- snapshot / GC ----------

        /// <summary>First-open snapshot and every later top-up — the same idempotent code (§4.1):
        /// walk the local inventory, give every eligible HOMELESS item its current slot
        /// (src=snap when this world's file did not exist, seed otherwise). SeedHome() supplies the
        /// "never overwrite a live home" guarantee.</summary>
        private static void Snapshot()
        {
            Human human = Guards.LocalHuman;
            if (human == null) return;
            string src = (!_snappedThisWorld && !_hadFileOnLoad && _homes.Count == 0) ? "snap" : "seed";
            _snappedThisWorld = true;
            List<ScannedSlot> scanned = InventoryScanner.Scan(StowRouter.ConfiguredDepth(), false);
            for (int i = 0; i < scanned.Count; i++)
            {
                ScannedSlot s = scanned[i];
                if (s == null || s.Slot == null || s.Sealed) continue;   // never a built-in / trapped part
                DynamicThing occ = s.Slot.Get();
                if (occ == null) continue;
                SeedHome(occ, s.Slot, src);
            }
            StatSnapshots++;
            UIALog.Info("Stow homes: snapshot/top-up ran (" + _homes.Count + " entries, key "
                + (_loadedKey != null ? "'" + _loadedKey + "'" : "(memory only)") + ").");
        }

        /// <summary>GC (plan §7.5). Host/SP (whole world loaded): drop entries whose ITEM no longer
        /// resolves (or resolves to another prefab) and entries whose container is gone the same
        /// way. MP client (may not hold every Thing): drop only on a prefab MISMATCH of something
        /// that does resolve, or after 30 days unseen. Then enforce the entry cap.</summary>
        private static void RunGc()
        {
            if (_homes.Count == 0) return;
            bool isClient = false;
            try { isClient = NetworkManager.IsClient; } catch { }

            List<long> drop = null;
            DateTime today = DateTime.UtcNow.Date;
            foreach (KeyValuePair<long, HomeEntry> kv in _homes)
            {
                HomeEntry e = kv.Value;
                bool dead = false;
                Thing item = Referencable.Find<Thing>(kv.Key);
                Thing cont = e.ContainerId != 0 ? Referencable.Find<Thing>(e.ContainerId) : null;
                if (!isClient)
                {
                    if (item == null || SafePrefabHash(item) != e.ItemPrefab) dead = true;
                    else if (e.ContainerId != 0 && (cont == null || SafePrefabHash(cont) != e.ContainerPrefab)) dead = true;
                }
                else
                {
                    if (item != null && SafePrefabHash(item) != e.ItemPrefab) dead = true;
                    else if (cont != null && SafePrefabHash(cont) != e.ContainerPrefab) dead = true;
                    else if (item == null && UnseenDays(e, today) > ClientUnseenPruneDays) dead = true;
                }
                if (dead)
                {
                    if (drop == null) drop = new List<long>();
                    drop.Add(kv.Key);
                }
            }
            if (drop != null)
            {
                for (int i = 0; i < drop.Count; i++) _homes.Remove(drop[i]);
                StatGcPruned += drop.Count;
                MarkDirty();
            }
            while (_homes.Count > MaxEntries) EvictOldest();
        }

        /// <summary>Evict the single oldest-seen entry (cap enforcement). Linear scan — runs only
        /// at/over the cap, never on the landing hot path below it.</summary>
        private static void EvictOldest()
        {
            long worstId = 0;
            string worstSeen = null;
            foreach (KeyValuePair<long, HomeEntry> kv in _homes)
            {
                string seen = kv.Value.SeenAt ?? "";
                if (worstId == 0 || string.CompareOrdinal(seen, worstSeen) < 0)
                {
                    worstId = kv.Key;
                    worstSeen = seen;
                }
            }
            if (worstId != 0)
            {
                _homes.Remove(worstId);
                StatGcPruned++;
                MarkDirty();
            }
        }

        private static int UnseenDays(HomeEntry e, DateTime today)
        {
            DateTime seen;
            if (!DateTime.TryParseExact(e.SeenAt, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out seen))
                return 0;   // unparseable = treat as seen today (conservative: never prune on bad data)
            return (int)(today - seen.Date).TotalDays;
        }

        // ---------- load / save ----------

        /// <summary>(Re)load for the CURRENT world key. No key yet → hold the table in memory only
        /// (a null key is NEVER passed to <see cref="SaveScopedXmlStore"/>, whose null fallback is
        /// the broken station name). A memory-only table that GAINS a key mid-session (a legacy
        /// world's first save) keeps this session's entries — they are newer than anything on disk —
        /// and takes disk entries only for items it has not seen.</summary>
        private static void EnsureLoaded()
        {
            string key;
            if (!WorldKey.TryGet(out key)) return;   // memory-only until a key exists
            if (string.Equals(key, _loadedKey, StringComparison.Ordinal)) return;

            if (_loadedKey != null && _dirty) FlushNow();   // a previous world's data goes out first

            Dictionary<long, HomeEntry> carried = null;
            if (_loadedKey == null && _homes.Count > 0)
                carried = new Dictionary<long, HomeEntry>(_homes);   // memory-only session gaining its key

            _homes.Clear();
            _loadedKey = key;
            _hadFileOnLoad = false;
            _snappedThisWorld = false;   // a new key means a new world/file: re-decide snap-vs-seed

            StowHomeFile parsed = SaveScopedXmlStore.LoadPerSave<StowHomeFile>(StoreFolder, key, StoreLabel);
            if (parsed != null && parsed.Homes != null)
            {
                _hadFileOnLoad = true;
                for (int i = 0; i < parsed.Homes.Count; i++)
                {
                    StowHomeRecord r = parsed.Homes[i];
                    if (r == null || r.ItemId == 0) continue;
                    HomeEntry e = new HomeEntry();
                    e.ItemPrefab = r.ItemPrefab;
                    e.ContainerId = r.ContainerId;
                    e.ContainerPrefab = r.ContainerPrefab;
                    e.SlotIndex = r.SlotIndex;
                    e.Source = r.Source;
                    e.HomedAt = r.HomedAt;
                    e.SeenAt = r.SeenAt;
                    _homes[r.ItemId] = e;
                }
                UIALog.Info("Loaded " + _homes.Count + " stow home(s) for world '" + key + "'.");
            }

            if (carried != null)
            {
                foreach (KeyValuePair<long, HomeEntry> kv in carried)
                    _homes[kv.Key] = kv.Value;               // this session's memory wins over disk
                MarkDirty();
            }
        }

        private static void MarkDirty()
        {
            if (!_dirty) _dirtyAt = UnityEngine.Time.unscaledTime;
            _dirty = true;
        }

        /// <summary>Write the table now (debounce serviced by <see cref="Tick"/>). Memory-only
        /// tables (no world key) never write — see the class remarks. Only a write that actually
        /// LANDED clears the dirty flag (the <see cref="HintUsageStore"/> pattern): a failed save —
        /// a locked file, a sync client holding it — keeps the pending delta, and the debounce is
        /// RE-ARMED so the retry comes ~2 s later, not every frame (SavePerSave already warned; a
        /// per-frame retry would be a per-frame disk write + warn spam).</summary>
        private static void FlushNow()
        {
            if (_loadedKey == null) { _dirty = false; return; }   // memory-only: nothing to write or retry
            StowHomeFile file = new StowHomeFile();
            file.Schema = 1;
            file.World = _loadedKey;
            try { file.Station = BagProfileStore.CurrentSaveKey(); } catch { file.Station = null; }
            file.Saved = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            foreach (KeyValuePair<long, HomeEntry> kv in _homes)
            {
                HomeEntry e = kv.Value;
                StowHomeRecord r = new StowHomeRecord();
                r.ItemId = kv.Key;
                r.ItemPrefab = e.ItemPrefab;
                r.ContainerId = e.ContainerId;
                r.ContainerPrefab = e.ContainerPrefab;
                r.SlotIndex = e.SlotIndex;
                r.Source = e.Source;
                r.HomedAt = e.HomedAt;
                r.SeenAt = e.SeenAt;
                file.Homes.Add(r);
            }
            if (SaveScopedXmlStore.SavePerSave(StoreFolder, _loadedKey, file, StoreLabel))
                _dirty = false;
            else
                _dirtyAt = UnityEngine.Time.unscaledTime;   // still dirty; retry on the next debounce window
        }

        // ---------- small helpers ----------

        private static void TouchSeen(HomeEntry e)
        {
            string today = Today();
            if (!string.Equals(e.SeenAt, today, StringComparison.Ordinal))
            {
                e.SeenAt = today;
                MarkDirty();
            }
        }

        private static string _todayCache;
        private static int _todayCacheDay = -1;

        /// <summary>Today's "yyyy-MM-dd" (UTC), cached per day — no per-landing DateTime.ToString.</summary>
        private static string Today()
        {
            DateTime now = DateTime.UtcNow;
            int stamp = now.Year * 10000 + now.Month * 100 + now.Day;
            if (stamp != _todayCacheDay || _todayCache == null)
            {
                _todayCacheDay = stamp;
                _todayCache = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return _todayCache;
        }

        private static long SafeRefId(Thing t)
        {
            try { return t != null ? t.ReferenceId : 0; } catch { return 0; }
        }

        private static int SafePrefabHash(Thing t)
        {
            try { return t != null ? t.PrefabHash : 0; } catch { return 0; }
        }

        private static string SafeName(Thing t)
        {
            if (t == null) return "?";
            try
            {
                string n = t.DisplayName;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            try { return t.PrefabName ?? "?"; } catch { return "?"; }
        }

        /// <summary>Display name of a home container id for `stowhomes` (0 = worn slots).</summary>
        public static string DescribeContainer(long containerId)
        {
            if (containerId == 0) return "(worn slots)";
            Thing t = Referencable.Find<Thing>(containerId);
            return t != null ? SafeName(t) + " ref " + containerId : "(missing) ref " + containerId;
        }
    }
}
