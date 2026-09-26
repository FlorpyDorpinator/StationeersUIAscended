using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The ONLY place this mod mutates game state. Every method routes through the game's
    /// multiplayer-safe funnel (OnServer.* / Slot.Player* / Thing.Interact / Thing.Merge),
    /// which applies locally in singleplayer/host and sends authoritative messages on
    /// clients. Nothing here touches DynamicThing.MoveToSlot, Slot.Take or Quantity.
    ///
    /// <para>SEALED SLOTS (D-005): no funnel here ever PLACES anything into a slot vanilla never
    /// exposes to a player — see <see cref="IsSealedSlot"/>. Taking something OUT of one is allowed
    /// (that is how an item trapped by the old phantom grid is rescued).</para>
    ///
    /// <para>Sanctioned multi-message actions (each explicitly designed, each message individually
    /// gated at execute time): the radial chip dump, the coarse PressInteractable step, and
    /// <see cref="DragAllOfTypeTo"/> — vanilla's own Shift-drag "move all of this type", which vanilla
    /// itself transmits as one MoveToSlot per stack (FlorpyDorp approved vanilla parity, D-018).</para>
    /// </summary>
    public static class ItemActions
    {
        /// <summary>
        /// One-item move with the vanilla niceties (multi-constructor cancel, slot sound) but
        /// WITHOUT Slot.PlayerMoveToSlot's MoveAll/MoveAllOfType tail: that tail triggers on
        /// raw LeftShift/LeftCtrl (KeyManager.GetButton is plain Input.GetKey — it ignores the
        /// Typing input state our modal sets, Slot.cs:623-640 + KeyManager.cs:913), so typing a
        /// capital letter in the search panel or holding jetpack-descend would turn one click
        /// into a bulk container dump. One user action = ONE message, always.
        /// (PlayerSwapToSlot has no such tail — Slot.cs:766-776 — and stays in use.)
        /// </summary>
        private static void MoveOneToSlot(DynamicThing item, Slot destination)
        {
            try { InventoryManager.Instance?.CheckCancelMultiConstructor(); } catch { }
            OnServer.MoveToSlot(item, destination);
            try { destination.PlaySlotEnterUiSound(); } catch { }
        }

        // ---- sealed slots (D-005) ----------------------------------------------------------------

        /// <summary>The ONE compiled read of <see cref="Slot.IsInteractable"/>, isolated in its own
        /// non-inlined method: were a future game build ever to drop the field, the
        /// MissingFieldException fires when THIS tiny method JITs and is caught by
        /// <see cref="IsVanillaVisibleSlot"/> — instead of failing the JIT of a large caller, the exact
        /// crash class 0.9.7.1 fixed for <c>SpecificTypePrefabHash(es)</c>. (The field is a plain
        /// public bool on every decompiled build back to the Multiplayer Update — 27701/27758 Slot.cs
        /// and the live 27798 — so this is insurance, not a known risk.)</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ReadSlotInteractable(Slot slot)
        {
            return slot.IsInteractable;
        }

        /// <summary>A11: the <see cref="ReadSlotInteractable"/> read has failed once this session, so
        /// every later call answers the fail-open value WITHOUT re-attempting it. Without this latch a
        /// build that dropped the field would throw-and-catch (and re-attempt the failed JIT) on EVERY
        /// call — and <c>GridModel</c> asks per slot per frame, so that is a performance collapse, not
        /// a one-off. A plain bool describing the GAME BUILD, so it is harmless stale: an F6 reload
        /// loads a fresh assembly whose static starts false, and the same build would only trip it
        /// again.</summary>
        private static bool _interactableReadUnavailable;

        /// <summary>Would VANILLA ever draw this slot in an item's inventory window? Vanilla's own test is
        /// <c>Slot.IsInteractable</c>: <c>InventoryWindow.SetSlots</c> builds a button only for an
        /// interactable slot, <c>Thing.HasSlots</c> is "any interactable slot", the world slot pick
        /// (<c>Thing._slotLookup</c>) registers only interactable slots, and <c>Thing.HandleSwitch</c>
        /// refuses a non-interactable one (live 27798 InventoryWindow.SetSlots, Thing.HasSlots,
        /// Thing.cs ~3688 / ~4244). Fail-OPEN (visible) on any read failure, i.e. the pre-D-005
        /// behaviour — and the FIRST failure latches (<see cref="_interactableReadUnavailable"/>), so a
        /// missing field costs one exception per session, never one per call.</summary>
        public static bool IsVanillaVisibleSlot(Slot slot)
        {
            if (slot == null) return false;
            if (_interactableReadUnavailable) return true;   // A11: remembered failure, fail-open
            try { return ReadSlotInteractable(slot); }
            catch (System.Exception e)
            {
                _interactableReadUnavailable = true;
                try
                {
                    UIALog.Warn("Slot.IsInteractable is unreadable on this game build (" + e.GetType().Name
                        + "): hidden-slot detection is off for this session - every slot now counts as "
                        + "vanilla-visible, and only stack slots stay sealed.");
                }
                catch { }
                return true;
            }
        }

        /// <summary>A SEALED slot: one no vanilla UI ever lets a player put anything INTO — a
        /// non-interactable slot of a carried thing (see <see cref="IsVanillaVisibleSlot"/>), or ANY slot
        /// of a stack (<see cref="Stackable"/>: FlorpyDorp, D-005 — "there shouldn't be any sort of
        /// inventory box associated with it"; a stack's merge/split/consume paths know nothing of
        /// contents, and a consumed coil DESTROYS its children — DestroyChildrenOnDead). Slots on a
        /// creature (the human's hands/worn slots) and on world structures are never sealed by this
        /// rule. Every placement funnel below refuses a sealed destination; taking OUT is allowed.</summary>
        public static bool IsSealedSlot(Slot slot)
        {
            if (slot == null) return false;
            DynamicThing owner = slot.Parent as DynamicThing;
            if (owner == null || owner is Assets.Scripts.Objects.Entity) return false;
            if (owner is Assets.Scripts.Objects.Items.Stackable) return true;
            return !IsVanillaVisibleSlot(slot);
        }

        /// <summary>The first child slot of <paramref name="dest"/>'s occupant that <paramref name="item"/>
        /// may be INSERTED into — vanilla's <c>PlayerInsertToFreeSlot</c> loop (Slot.cs, AllowMove per
        /// child) minus sealed children, so an insert can never tuck the item into a hidden slot or a
        /// stack. Null when <see cref="Slot.CanInsert"/> is false or only sealed children would take it
        /// (the caller then continues down the normal merge / swap / move ladder).</summary>
        private static Slot FindInsertSlot(DynamicThing item, Slot dest)
        {
            if (item == null || dest == null || !Slot.CanInsert(item, dest)) return null;
            DynamicThing host = dest.Get();
            if (host == null || host.Slots == null) return null;
            foreach (Slot child in host.Slots)
            {
                if (child == null || IsSealedSlot(child)) continue;
                if (Slot.AllowMove(item, child)) return child;
            }
            return null;
        }

        /// <summary>Equip a thing into the active hand: move when empty, vanilla-style swap when
        /// occupied. When the item sits INSIDE the thing the active hand is holding (welder in
        /// hand → its battery), a swap with that hand is a parent-child paradox the game rejects,
        /// so the item goes to the other hand instead.</summary>
        public static bool EquipToActiveHand(ScannedSlot source)
        {
            Slot hand = InventoryManager.ActiveHandSlot;
            DynamicThing item = source?.Occupant;
            if (hand == null || item == null) return Fail();
            if (source.Expected != null && item != source.Expected) return Fail(); // menu is stale

            DynamicThing handOcc = hand.Get();
            if (handOcc != null && IsInsideThing(source.Slot, handOcc))
            {
                var human = InventoryManager.ParentHuman;
                Slot other = human == null ? null
                    : hand == human.LeftHandSlot ? human.RightHandSlot : human.LeftHandSlot;
                if (other != null && other.Get() == null && Slot.AllowMove(item, other))
                {
                    MoveOneToSlot(item, other);
                    return true;
                }
                return Fail(); // both hands busy: taking a part out of the held tool needs a free hand
            }

            if (handOcc == null)
            {
                if (!Slot.AllowMove(item, hand)) return Fail();
                MoveOneToSlot(item, hand);
                return true;
            }
            // A swap would put the HELD item INTO the source slot. A sealed source (an item rescued
            // out of a hidden/stack slot) may only be emptied, never refilled: take it to a free hand.
            if (IsSealedSlot(source.Slot)) return TakeToFreeHand(source);
            if (!Slot.AllowSwap(source.Slot, hand)) return Fail();
            // Wedge-binding rule (FlorpyDorp, 2026-08-06): when taking a tool OUT of a worn-belt
            // wedge with another belt tool in hand, the held tool must NOT be dumped into the
            // taken tool's wedge — it returns to its OWN bound wedge, so both bindings survive
            // (a raw swap left the taken tool bound nowhere). Resolve the home BEFORE the swap
            // (the pre-checks read pre-swap state), swap, then relocate. DELIBERATE two-message
            // exception to the one-action-one-message rule: no single vanilla funnel expresses
            // "A to slot Y, B to hand", both messages are independently server-gated, and if the
            // relocation is refused (a teammate filled the home mid-flight) the end state is
            // exactly the old behaviour — held tool sits in the taken tool's wedge, unbound
            // (bindings themselves are protected by BeltBindingStore's observe-only policy).
            Slot home = BeltHomeFor(source.Slot, handOcc);
            source.Slot.PlayerSwapToSlot(hand);
            if (home != null) OnServer.MoveToSlot(handOcc, home);
            MaybeCancelPlacement(hand); // the (possibly building) hand item was displaced: no ghost hologram
            return true;
        }

        /// <summary>Is this slot on the belt the LOCAL player is currently wearing?</summary>
        private static bool IsWornBeltSlot(Slot slot)
        {
            try
            {
                var human = InventoryManager.ParentHuman;
                DynamicThing belt = human?.ToolbeltSlot?.Get();
                return belt != null && slot != null && ReferenceEquals(slot.Parent, belt);
            }
            catch { return false; }
        }

        /// <summary>The displaced/held item's OWN empty bound wedge on the local player's worn
        /// tool-belt, or null — non-null only when <paramref name="sourceSlot"/> (where the item is
        /// about to land) is a slot on that same belt, the home differs from the landing slot
        /// (same-type keeps plain semantics), and the home slot is empty, unlocked and accepts the
        /// item. Fail-soft: null on any doubt = plain swap, no relocation.</summary>
        private static Slot BeltHomeFor(Slot sourceSlot, DynamicThing handItem)
        {
            try
            {
                if (sourceSlot == null || handItem == null) return null;
                var human = InventoryManager.ParentHuman;
                DynamicThing belt = human?.ToolbeltSlot?.Get();
                if (belt == null || !ReferenceEquals(sourceSlot.Parent, belt)) return null;
                int home = Features.BeltBindingStore.HomeSlotFor(belt, handItem);
                if (home < 0 || home == sourceSlot.SlotIndex) return null;
                var slots = belt.Slots;
                if (slots == null) return null;
                for (int i = 0; i < slots.Count; i++)
                {
                    Slot s = slots[i];
                    if (s == null || s.SlotIndex != home) continue;
                    return (s.Get() == null && !s.IsLocked && Slot.AllowMove(handItem, s)) ? s : null;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Is this slot part of the given thing (directly or nested inside it)?</summary>
        private static bool IsInsideThing(Slot slot, Thing root)
        {
            Thing parent = slot?.Parent;
            int depth = 0;
            while (parent != null && depth++ < 8)
            {
                if (parent == root) return true;
                parent = (parent as DynamicThing)?.ParentSlot?.Parent;
            }
            return false;
        }

        /// <summary>Exit vanilla's build/precision-placement mode when a mod funnel just moved the
        /// ACTIVE-HAND item out of the hand. Vanilla tears the hologram down only from its own
        /// input paths (<c>InventoryManager.CancelPlacement</c> — mode → Normal, construction +
        /// precision cursors off, panel hidden; InventoryManager.cs:1735), so a mod-funnel stow
        /// left a GHOST hologram you could aim but never build with (FlorpyDorp, 2026-08-06).
        /// Purely LOCAL UI state — no network message, safe on MP clients. No-op unless the given
        /// slot is the active hand and a placement mode is live; fail-soft.</summary>
        public static void MaybeCancelPlacement(Slot handSlot)
        {
            try
            {
                if (handSlot == null || handSlot != InventoryManager.ActiveHandSlot) return;
                if (InventoryManager.CurrentMode == InventoryManager.Mode.Normal) return;
                var inv = InventoryManager.Instance;
                if (inv != null) inv.CancelPlacement();
            }
            catch { }
        }

        /// <summary>Stow the active-hand item into a specific empty slot.</summary>
        public static bool StowActiveHandTo(Slot destination)
        {
            Slot hand = InventoryManager.ActiveHandSlot;
            DynamicThing item = hand?.Get();
            if (item == null || destination == null || destination.Get() != null) return Fail();
            if (IsSealedSlot(destination)) return Fail();   // never refill a hidden / stack slot
            if (!Slot.AllowMove(item, destination)) return Fail();
            OnServer.MoveToSlot(item, destination);
            MaybeCancelPlacement(hand); // the held constructor left the hand: no ghost hologram
            SlotFlash.OnStow(destination, item); // "it went in here" flash on the worn box, if nested
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            return true;
        }

        /// <summary>
        /// Put <paramref name="candidate"/>'s occupant into <paramref name="targetSlot"/>:
        /// straight move when the target is empty, atomic vanilla swap when occupied
        /// (this is the welder-battery flow from proposal §14.2 — one action, no ghost items).
        /// </summary>
        public static bool SwapIntoSlot(ScannedSlot candidate, Slot targetSlot)
        {
            DynamicThing item = candidate?.Occupant;
            if (item == null || targetSlot == null) return Fail();
            if (candidate.Expected != null && item != candidate.Expected) return Fail(); // menu is stale

            // Dropping something back onto its own slot (chip released on the box it came
            // from = "changed my mind") is a clean no-op — never OnServer.SwapSlots(s, s).
            if (candidate.Slot == targetSlot) return true;

            // Never place INTO a sealed (hidden / stack) slot — see IsSealedSlot.
            if (IsSealedSlot(targetSlot)) return Fail();

            if (targetSlot.Get() == null)
            {
                if (!Slot.AllowMove(item, targetSlot)) return Fail();
                Features.BeltBindingStore.NoteExplicitPlacement(targetSlot, item); // drag = may rebind a wedge
                OnServer.MoveToSlot(item, targetSlot);
                SlotFlash.OnStow(targetSlot, item); // stow flash if the target is inside a worn container
            }
            else
            {
                // The swap would put the target's occupant INTO the candidate's slot: refused when
                // that slot is sealed (a rescued item's slot may be emptied, never refilled).
                if (IsSealedSlot(candidate.Slot)) return Fail();
                if (!Slot.AllowSwap(candidate.Slot, targetSlot)) return Fail();
                // The drop TARGET is explicit; the DISPLACED side only when the gesture stayed on
                // the belt (wedge-over-wedge = the sanctioned home exchange). Displaced out of the
                // belt (chip dropped on a hand box): the occupant lands in the vacated wedge
                // mechanically — no rebind, route it to its own bound wedge. See DragTo's swap
                // branch for the full rationale (the wrench/screwdriver case).
                Features.BeltBindingStore.NoteExplicitPlacement(targetSlot, item);
                DynamicThing displaced = targetSlot.Get();
                Slot displacedHome = null;
                if (IsWornBeltSlot(targetSlot)) Features.BeltBindingStore.NoteExplicitPlacement(candidate.Slot, displaced);
                else displacedHome = BeltHomeFor(candidate.Slot, displaced);
                OnServer.SwapSlots(candidate.Slot, targetSlot);
                if (displacedHome != null) OnServer.MoveToSlot(displaced, displacedHome);
            }
            MaybeCancelPlacement(candidate.Slot); // dragged out of the hand: no ghost hologram
            UIAudioManager.Play(targetSlot.Type == Slot.Class.Battery
                ? UIAudioManager.InstallBatteryHash
                : UIAudioManager.ObjectPutHash);
            return true;
        }

        /// <summary>
        /// Item drag-drop (cell -> cell) for the Universal Inventory grid. Resolves the target
        /// exactly like vanilla's <c>InputMouse.IsValid</c> (InputMouse.cs:142-192) — insert into a
        /// child slot, merge onto a matching stack, swap an occupied slot, or move into an empty one —
        /// then issues exactly ONE authoritative message through the funnel. Re-verifies the pinned
        /// occupant so a teammate's move between drag-start and drop can't act on a different item,
        /// and re-runs every Slot gate AT EXECUTE TIME. Prefers the raw <see cref="OnServer"/> paths
        /// over Slot.PlayerMoveToSlot (whose MoveAll tail can fire on a held modifier — see
        /// <see cref="MoveOneToSlot"/>). No client-side Quantity write, no bulk loop.
        /// </summary>
        public static bool DragTo(ScannedSlot source, Slot dest)
        {
            if (source == null || dest == null) return Fail();
            Slot src = source.Slot;
            DynamicThing item = source.Occupant;                 // src?.Get()
            if (src == null || item == null) return Fail();
            if (source.Expected != null && item != source.Expected) return Fail(); // grid is stale

            // Released on the slot it came from ("changed my mind") = clean no-op.
            if (src == dest) return true;

            // Never place INTO a sealed (hidden / stack) slot — see IsSealedSlot. Vanilla's UI
            // cannot even target one; this covers every surface that resolves a destination for
            // us (grid cells, HUD boxes, world slots, the inbound world drag).
            if (IsSealedSlot(dest)) return Fail();

            // Vanilla's two Plant guards (InputMouse.cs:154-164): a Plant-class slot is never a
            // drag destination (planting goes through the hydroponics interact path, not the
            // inventory funnel), and a Plant that is currently PLANTED cannot be dragged at all
            // (it must be harvested first). Slot.AllowMove/AllowSwap do NOT cover either case.
            if (dest.Type == Slot.Class.Plant) return Fail();
            Plant plant = item as Plant;
            if (plant != null && plant.IsPlanted) return Fail();

            // 1. Insert: nest the item into a free child slot of the destination's occupant
            //    (e.g. drop a battery onto a tool that holds one). Mirrors PlayerInsertToFreeSlot,
            //    minus sealed children (FindInsertSlot): a stack or a hidden slot is never an
            //    insert target, and such a drop continues down the ladder like a plain item.
            Slot insertInto = FindInsertSlot(item, dest);
            if (insertInto != null)
            {
                OnServer.MoveToSlot(item, insertInto);
                MaybeCancelPlacement(src); // dragged out of the hand: no ghost hologram
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
                return true;
            }

            // The source must be swappable out of its slot at all (vanilla gates merge AND move
            // behind this — InputMouse.cs:168-171).
            if (!Slot.AllowSwap(src, dest)) return Fail();

            if (dest.Get() != null)
            {
                // 2. Merge onto a matching partial stack. (A stack CONSTRUCTOR — iron frames —
                //    merged fully away also empties the hand: cancel the hologram then too.)
                if (Slot.CanMerge(item, dest))
                {
                    IMergeable held = item as IMergeable;
                    IMergeable targetStack;
                    if (held != null && dest.Contains<IMergeable>(out targetStack))
                    {
                        bool mergedOk = MergeInto(targetStack, held);
                        if (mergedOk && src.Get() == null) MaybeCancelPlacement(src);
                        return mergedOk;
                    }
                    // Vanilla's mining-belt fallback (Slot.PlayerMergeToSlot, Slot.cs:745-763):
                    // Slot.CanMerge also returns true when the destination holds a MiningBelt and
                    // the dragged thing is Ore it can take (Slot.cs:306-320 -> CanMergeAsOre). The
                    // belt itself is not IMergeable, so Contains<IMergeable> fails and without this
                    // branch dropping ore on a worn mining belt errors instead of merging.
                    // MergeAsOre -> TryAddOre only uses Thing.Merge / OnServer.MoveToSlot, both
                    // authoritative funnels (MiningBelt.cs:37-89).
                    MiningBelt belt = dest.Get() as MiningBelt;
                    if (held != null && belt != null && belt.MergeAsOre(held))
                    {
                        UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                        return true;
                    }
                    return Fail();
                }
                // 3. Swap with the occupied destination. The swap puts dest's occupant INTO src:
                //    refused when src is sealed (a rescued item's slot is emptied, never refilled).
                if (IsSealedSlot(src)) return Fail();
                if (!Slot.AllowSwap(dest, item)) return Fail();
                Features.BeltBindingStore.NoteExplicitPlacement(dest, item); // the drop TARGET: explicit
                // The DISPLACED occupant is an explicit rebind ONLY when the gesture stayed on the
                // belt (wedge dragged over wedge = the sanctioned home exchange). When the tool was
                // dragged OUT of the belt (wedge -> hand box / bag cell), the displaced item lands
                // in the vacated wedge MECHANICALLY — never rebind it; route it onward to its own
                // bound wedge instead (FlorpyDorp's wrench/screwdriver case, 2026-08-06: dragging
                // the screwdriver from slot 5 into the wrench-holding hand must send the wrench to
                // ITS slot 4, not leave it squatting in 5). Same two-message rationale as
                // EquipToActiveHand's home routing: swap first (old behaviour = the degraded state),
                // then one independently-gated relocation.
                DynamicThing displaced = dest.Get();
                Slot displacedHome = null;
                if (IsWornBeltSlot(dest)) Features.BeltBindingStore.NoteExplicitPlacement(src, displaced);
                else displacedHome = BeltHomeFor(src, displaced);
                OnServer.SwapSlots(src, dest);
                if (displacedHome != null) OnServer.MoveToSlot(displaced, displacedHome);
                MaybeCancelPlacement(src); // dragged out of the hand: no ghost hologram
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
                return true;
            }

            // 4. Move into the empty destination.
            if (!Slot.AllowMove(item, dest)) return Fail();
            Features.BeltBindingStore.NoteExplicitPlacement(dest, item); // drag = may rebind a wedge
            OnServer.MoveToSlot(item, dest);
            MaybeCancelPlacement(src); // dragged out of the hand: no ghost hologram
            SlotFlash.OnStow(dest, item);                        // stow flash if it lands in a worn container
            UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            return true;
        }

        // ---- Shift-drag: move ALL of this type (D-018, vanilla parity) ---------------------------

        /// <summary>Is vanilla's "move all of this type" modifier held right now? Reads the game's OWN,
        /// rebindable binding (<c>KeyMap.MoveAllOfType</c>, default LeftShift — KeyManager.cs ~418)
        /// through <c>KeyManager.GetButton</c>, exactly the read vanilla's
        /// <c>Slot.PlayerMoveToSlot</c> makes at the moment of the drop (Slot.cs ~636). Read it on the
        /// RELEASE frame of a deliberate drag gesture only — never for a plain click (see
        /// <see cref="MoveOneToSlot"/> for why a raw modifier must not turn a click into a bulk move).</summary>
        public static bool MoveAllOfTypeHeld()
        {
            try { return KeyManager.GetButton(KeyMap.MoveAllOfType); }
            catch { return false; }
        }

        // Rotated destination candidates for the sweep. Main-thread only; cleared before AND after
        // every use, so it never holds Slot references between gestures (hot-reload safe).
        private static readonly List<Slot> _sweepCandidates = new List<Slot>(32);

        /// <summary>
        /// Vanilla's Shift-drag "move all of this type" (D-018). Vanilla implements it as the tail of
        /// <c>Slot.PlayerMoveToSlot</c>: after the dragged item moves, <c>TryMoveAllOfType</c> walks the
        /// SOURCE container's slots and sends one <c>OnServer.MoveToSlot</c> per same-prefab item into
        /// the first free, compatible slot of the DESTINATION container (live 27798 Slot.cs
        /// PlayerMoveToSlot / TryMoveAllOfType; 27758 Slot.cs:623-720). There is no bulk network
        /// message — on an MP client each <c>OnServer.MoveToSlot</c> is its own
        /// <c>MoveToSlotMessage</c> (OnServer.cs:60-74) — so this mirrors that per-stack loop.
        ///
        /// <para>Semantics, mirroring vanilla: the dragged item itself resolves through
        /// <see cref="DragTo"/> (one gated message). Only when that is a PLAIN MOVE into an empty slot
        /// (vanilla's <c>DragResult.Valid</c> — the only outcome whose vanilla funnel,
        /// PlayerMoveToSlot, carries the tail) does the sweep follow; an insert / merge / swap stays a
        /// single action exactly as in vanilla. No sweep when the source is a creature's own slot
        /// (vanilla: <c>fromSlot.Parent is Entity</c>) or when the source has no parent slot (a world
        /// item). Every swept item is re-gated AT EXECUTE TIME (empty, class None-or-matching,
        /// IsSwappable, <c>Slot.AllowMove</c> on the item that actually moves, neither hidden nor
        /// sealed — <see cref="SweepMayTouch"/>) and a blocked item is SKIPPED, never failed.</para>
        ///
        /// <para>Deliberate deviations (all stricter than vanilla, none adds a mutation): the dragged
        /// item is never re-sent and its landing slot is never re-used (on an MP client vanilla's loop
        /// still sees the dragged item in its old slot — the move is send-only — and fires a SECOND
        /// move for it, leaving a hole at the drop slot); a locked source slot and a planted plant are
        /// skipped; the sweep neither reads from nor fills any slot vanilla would not DRAW, nor any
        /// sealed one (<see cref="SweepMayTouch"/> — which, unlike <see cref="IsSealedSlot"/> alone,
        /// also covers a world structure's hidden slots: a vending machine's stock); a same-container
        /// drag stays a single move (vanilla would reshuffle the whole type inside the bag). Swept
        /// items never rebind a belt wedge (only the dragged item is an explicit placement —
        /// BeltBindingStore's rule).</para>
        ///
        /// <para>Radial call site (future): a chip released on a wedge/box/world slot with the modifier
        /// held can call this in place of its single-move funnel with the chip's pinned
        /// <see cref="ScannedSlot"/>; world chips (no parent slot) never sweep, exactly like vanilla's
        /// <c>InputMouse.Drag()</c>.</para>
        /// </summary>
        /// <returns>True when the dragged item's own move went out (swept items may add more).</returns>
        public static bool DragAllOfTypeTo(ScannedSlot source, Slot dest)
        {
            if (source == null || dest == null) return Fail();
            Slot src = source.Slot;
            DynamicThing item = source.Occupant;
            if (src == null || item == null) return Fail();
            // Vanilla's tail runs only on the plain-move rung (dest EMPTY): everything else is DragTo.
            bool plainMove = src != dest && dest.Get() == null;
            if (!DragTo(source, dest)) return false;   // DragTo already played the fail cue
            if (!plainMove) return true;
            int swept = SweepAllOfType(item, src, dest);
            if (swept > 0)
                UIALog.Info("Move all of type: " + swept + " more " + SafeName(item) + " sent after the dragged one.");
            return true;
        }

        /// <summary>Vanilla's <c>TryMoveAllOfType</c> sweep (see <see cref="DragAllOfTypeTo"/>), one
        /// gated <c>OnServer.MoveToSlot</c> per same-prefab item. Returns how many moves went out.</summary>
        private static int SweepAllOfType(DynamicThing dragged, Slot fromSlot, Slot endSlot)
        {
            Item item = dragged as Item;
            if (item == null || fromSlot == null || endSlot == null) return 0;
            Thing fromOwner = fromSlot.Parent;
            Thing endOwner = endSlot.Parent;
            if (fromOwner == null || endOwner == null) return 0;
            if (fromOwner is Assets.Scripts.Objects.Entity) return 0;   // vanilla: never a creature's own slots
            if (ReferenceEquals(fromOwner, endOwner)) return 0;          // same container: plain move only
            List<Slot> fromSlots = fromOwner.Slots;
            List<Slot> endSlots = endOwner.Slots;
            if (fromSlots == null || endSlots == null || endSlots.Count == 0) return 0;

            // Vanilla's fill order: the destination container's slots rotated to start just after the
            // drop slot, wrapping to 0 when that index reaches the last slot's index (TryMoveAllOfType
            // + EnumUtil.RotateFrom). Rebuilt here without the two GetRange allocations.
            int start = endSlot.SlotIndex + 1;
            if (start < 0 || start >= endSlots[endSlots.Count - 1].SlotIndex || start >= endSlots.Count) start = 0;
            _sweepCandidates.Clear();
            for (int i = start; i < endSlots.Count; i++) _sweepCandidates.Add(endSlots[i]);
            for (int i = 0; i < start; i++) _sweepCandidates.Add(endSlots[i]);

            int prefab = item.PrefabHash;
            Slot.Class itemClass = item.SlotType;
            int moved = 0;
            try
            {
                for (int s = 0; s < fromSlots.Count; s++)
                {
                    Slot from = fromSlots[s];
                    // Never sweep out of a locked slot, nor one the sweep may not touch (a built-in
                    // part — an emergency suit's tank, a package's contents, a vending machine's
                    // hidden stock): stricter than vanilla's loop, which walks every slot; a side
                    // effect must never strip a built-in. See SweepMayTouch.
                    if (from == null || from.IsLocked || !SweepMayTouch(from)) continue;
                    Item occ = from.Get() as Item;
                    // The dragged item is already on its way to endSlot (send-only on a client).
                    if (occ == null || ReferenceEquals(occ, dragged)) continue;
                    if (occ.PrefabHash != prefab) continue;
                    Plant plant = occ as Plant;
                    if (plant != null && plant.IsPlanted) continue;

                    for (int i = 0; i < _sweepCandidates.Count; i++)
                    {
                        Slot to = _sweepCandidates[i];
                        // endSlot is the DRAGGED item's landing (still empty locally on a client).
                        if (to == null || ReferenceEquals(to, endSlot)) continue;
                        if (to.Get() != null) continue;
                        if (to.Type != Slot.Class.None && to.Type != itemClass) continue;
                        if (!to.IsSwappable) continue;
                        if (!SweepMayTouch(to)) continue;   // never a hidden or sealed slot
                        if (!Slot.AllowMove(occ, to)) continue;
                        OnServer.MoveToSlot(occ, to);   // one authoritative message per stack, like vanilla
                        _sweepCandidates.RemoveAt(i);
                        moved++;
                        break;
                    }
                }
            }
            finally
            {
                _sweepCandidates.Clear();   // never strand Slot refs between gestures
            }
            return moved;
        }

        /// <summary>A8: may the Shift-sweep read from, or fill, this slot? Only a slot vanilla itself
        /// would DRAW (<see cref="IsVanillaVisibleSlot"/>) that is not sealed. The visibility half is
        /// the load-bearing one: <see cref="IsSealedSlot"/> is false for EVERY slot of a world
        /// structure (its owner is not a DynamicThing), yet a structure can keep real contents in
        /// hidden slots — a vending machine's stock lives in its non-interactable slots (27758
        /// VendingMachine.cs:64-67, <c>IsSlotTradable</c> = <c>!slot.IsInteractable</c>), so a
        /// Shift-drag out of its export slot would otherwise sweep the hidden stock. The sealed half
        /// keeps the old rule for a stack's slot even if a build ever draws one. Genuinely stricter
        /// than vanilla's <c>TryMoveAllOfType</c>, which walks every slot.</summary>
        private static bool SweepMayTouch(Slot slot)
        {
            return slot != null && IsVanillaVisibleSlot(slot) && !IsSealedSlot(slot);
        }

        /// <summary>ASCII display name for a log line (never displayed through TMP).</summary>
        private static string SafeName(Thing thing)
        {
            try { return thing != null ? thing.DisplayName : "?"; }
            catch { return "?"; }
        }

        /// <summary>Take a slot's occupant into a FREE hand — the active hand when empty, else the other —
        /// and NEVER a swap, never a drop to the world. The take-out gesture for an item sitting in a
        /// SEALED slot (<see cref="IsSealedSlot"/>, e.g. something trapped in a cable coil by the old
        /// phantom grid): a swap would refill that slot, so with both hands busy this fails (the player
        /// frees a hand, or drags the item somewhere instead). One gated message; the pinned occupant is
        /// re-verified at execute time.</summary>
        public static bool TakeToFreeHand(ScannedSlot source)
        {
            DynamicThing item = source?.Occupant;
            var human = InventoryManager.ParentHuman;
            if (item == null || human == null) return Fail();
            if (source.Expected != null && item != source.Expected) return Fail(); // stale
            if (source.Slot != null && source.Slot.IsLocked) return Fail();

            Slot active = InventoryManager.ActiveHandSlot;
            Slot other = active == null ? null
                : active == human.LeftHandSlot ? human.RightHandSlot : human.LeftHandSlot;
            Slot hand = null;
            if (active != null && active != source.Slot && active.Get() == null && Slot.AllowMove(item, active)) hand = active;
            else if (other != null && other != source.Slot && other.Get() == null && Slot.AllowMove(item, other)) hand = other;
            if (hand == null) return Fail();
            MoveOneToSlot(item, hand);
            return true;
        }

        /// <summary>
        /// Swap the worn tool-belt for <paramref name="chosenBelt"/> (a spare belt found elsewhere
        /// in the local player's inventory) through vanilla's authoritative swap funnel. One
        /// <see cref="OnServer.SwapSlots(Slot,Slot)"/> = one message; the belts trade places
        /// (a worn->world move when the toolbelt slot happens to be empty is handled by the same
        /// AllowSwap/SwapSlots path, which treats an empty destination as a plain move — verified
        /// Slot.cs:343-377). Gated by Slot.AllowSwap at execute time and by an occupancy re-check
        /// so a stale picker wedge can never swap in a belt someone else already took.
        ///
        /// <para>D-005 (A4): the swap puts the WORN belt INTO the chosen belt's slot, so that slot must
        /// not be sealed — a belt the old phantom grid tucked into a cable coil's slot would otherwise
        /// trade places with the one you are wearing, and a consumed coil DESTROYS its children. The
        /// picker no longer lists such a belt (<see cref="InventoryScanner.FindCompatible"/> skips
        /// sealed slots); this is the execute-time half of that defence.</para>
        /// </summary>
        public static bool SwapWornToolbelt(DynamicThing chosenBelt)
        {
            var human = InventoryManager.ParentHuman;
            if (chosenBelt == null || human == null) return Fail();
            Slot toolbelt = human.ToolbeltSlot;
            Slot source = chosenBelt.ParentSlot;
            if (toolbelt == null || source == null) return Fail();
            if (source == toolbelt) return true;                 // already worn: clean no-op
            if (source.Get() != chosenBelt) return Fail();       // picker is stale
            if (!IsCarriedByLocalPlayer(chosenBelt)) return Fail(); // moved/taken since build
            if (IsSealedSlot(source)) return Fail();             // never refill a hidden / stack slot
            if (!Slot.AllowSwap(source, toolbelt)) return Fail();
            OnServer.SwapSlots(source, toolbelt);
            UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            return true;
        }

        /// <summary>Search-panel take: into whichever hand is free (active hand preferred), or
        /// drop at the player's feet when both hands are full (Option A search semantics).</summary>
        public static bool TakeOrDrop(ScannedSlot source)
        {
            DynamicThing item = source?.Occupant;
            var human = InventoryManager.ParentHuman;
            if (item == null || human == null) return Fail();
            if (source.Expected != null && item != source.Expected) return Fail(); // menu is stale

            Slot active = InventoryManager.ActiveHandSlot;
            Slot other = active == null ? null
                : active == human.LeftHandSlot ? human.RightHandSlot : human.LeftHandSlot;
            if (active != null && active.Get() == null && Slot.AllowMove(item, active))
            {
                MoveOneToSlot(item, active);
            }
            else if (other != null && other.Get() == null && Slot.AllowMove(item, other))
            {
                MoveOneToSlot(item, other);
            }
            else
            {
                OnServer.MoveToSlotOrWorld(item, null); // both hands full -> the ground
            }
            UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
            return true;
        }

        /// <summary>
        /// Move a free-lying WORLD item into a slot — vanilla's mouse-pickup funnel
        /// (InputMouse.MoveCurrentItemToHand calls OnServer.MoveToSlot with a world thing).
        /// Build 27701 has NO server-side range gate on MoveToSlotMessage (verified in the
        /// decompile), so the 3 m vanilla limit is enforced HERE at execute time — the mod
        /// must never out-reach the vanilla cursor.
        /// </summary>
        public static bool MoveWorldItemToSlot(DynamicThing item, Slot destination)
        {
            var human = InventoryManager.ParentHuman;
            if (item == null || destination == null || human == null) return Fail();
            // Vanilla's world-pickup funnel is hard-typed to Item (InputMouse.cs:384
            // `CursorItem = CursorThing as Item`) — without this gate a Z-grab could stuff
            // any CanPickup DynamicThing (the LANDER CAPSULE) into a backpack, a mutation
            // no vanilla client can produce. Slot.AllowMove alone does not check this.
            if (!(item is Item)) return Fail();
            if (item.ParentSlot != null) return Fail();        // someone took it meanwhile
            if (destination.Get() != null) return Fail();
            if (IsSealedSlot(destination)) return Fail();      // never refill a hidden / stack slot
            if (!Slot.AllowMove(item, destination)) return Fail();
            float maxDist = 3f;
            try { maxDist = CursorManager.MaxInteractDistance; } catch { }
            if ((item.ThingTransformPosition - human.ThingTransformPosition).magnitude > maxDist + 0.75f)
                return Fail();                                  // out of reach = no grab
            OnServer.MoveToSlot(item, destination);
            SlotFlash.OnStow(destination, item); // stow flash if the target is inside a worn container
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            return true;
        }

        /// <summary>
        /// A free-lying WORLD item dropped onto a slot — the INBOUND half of the HUD drag, and the
        /// counterpart of <see cref="DragTo"/> for a source that has no parent slot.
        ///
        /// Mirrors vanilla's <c>InputMouse.IsValid</c> ladder for the <c>ParentSlot == null</c> case
        /// (InputMouse.cs:142-192): INSERT into the destination's contents, else MERGE, else SWAP the
        /// occupant out to the world, else a plain MOVE. Vanilla's <c>AllowSwap(ParentSlot, dest)</c>
        /// rung is deliberately absent — it is gated behind <c>ParentSlot != null</c> upstream, so a
        /// world item never faces it.
        ///
        /// Kept SEPARATE from <see cref="MoveWorldItemToSlot"/> on purpose: that one hard-fails on an
        /// occupied destination, and the radial parking layer depends on exactly that contract.
        ///
        /// Every rung re-gates at execute time and each outcome emits exactly ONE authoritative
        /// message. Items only — vanilla's pickup funnel is hard-typed to <c>Item</c>, and the
        /// client-side reach cap is the ONLY limiter (build 27701 has no server-side range check on
        /// the move message).
        /// </summary>
        public static bool WorldDragTo(DynamicThing item, Slot dest)
        {
            var human = InventoryManager.ParentHuman;
            if (item == null || dest == null || human == null) return Fail();

            if (!(item is Item)) return Fail();                 // never stuff a lander capsule in a bag
            if (item.ParentSlot != null) return Fail();          // slot-sourced: DragTo owns that path
            float maxDist = 3f;
            try { maxDist = CursorManager.MaxInteractDistance; } catch { }
            if ((item.ThingTransformPosition - human.ThingTransformPosition).magnitude > maxDist + 0.75f)
                return Fail();                                   // out of reach = no grab

            // Never place INTO a sealed (hidden / stack) slot — see IsSealedSlot.
            if (IsSealedSlot(dest)) return Fail();

            // Vanilla's two Plant guards (InputMouse.cs:154-164); AllowMove/AllowSwap cover neither.
            if (dest.Type == Slot.Class.Plant) return Fail();
            Plant plant = item as Plant;
            if (plant != null && plant.IsPlanted) return Fail();

            // 1. INSERT into the destination's contents — THE BACKPACK CASE. Slot.CanInsert already
            //    requires the occupant to have child slots and at least one to accept the item;
            //    FindInsertSlot additionally skips sealed children (a stack / hidden slot is never an
            //    insert target — such a drop continues down the ladder like a plain item).
            Slot insertInto = FindInsertSlot(item, dest);
            if (insertInto != null)
            {
                OnServer.MoveToSlot(item, insertInto);
                SlotFlash.OnStow(insertInto, item);
                UIAudioManager.Play(UIAudioManager.ObjectPutHash);
                return true;
            }

            if (dest.Get() != null)
            {
                // 2. MERGE onto a matching partial stack. Checked BEFORE the swap so stackables
                //    combine instead of flinging the held item on the floor.
                if (Slot.CanMerge(item, dest))
                {
                    IMergeable held = item as IMergeable;
                    IMergeable targetStack;
                    if (held != null && dest.Contains<IMergeable>(out targetStack))
                        return MergeInto(targetStack, held);
                    MiningBelt belt = dest.Get() as MiningBelt;
                    if (held != null && belt != null && belt.MergeAsOre(held))
                    {
                        UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                        return true;
                    }
                    return Fail();
                }

                // 3. SWAP — vanilla throws the occupant OUT to the world and the dragged item takes
                //    its place (DragResult.Swap; FlorpyDorp chose to match vanilla here).
                //    PlayerSwapToWorld is the only clean route to the slot-to-world SwapSlots
                //    overload and, unlike PlayerMoveToSlot, carries NO MoveAll tail (Slot.cs:736).
                if (!Slot.AllowSwap(dest, item)) return Fail();
                Features.BeltBindingStore.NoteExplicitPlacement(dest, item); // drag = may rebind a wedge
                dest.PlayerSwapToWorld(item);
                return true;
            }

            // 4. Plain MOVE into an empty slot. Deliberately NOT dest.PlayerMoveToSlot: that carries
            //    the MoveAll/MoveAllOfType tail (Slot.cs:633) which fires on a raw held
            //    LeftShift/LeftCtrl and would turn one drop into a bulk dump. It also skips
            //    DoSwapActiveHand, which is the chosen UIA behaviour — the item lands where you
            //    aimed without stealing your active hand.
            if (!Slot.AllowMove(item, dest)) return Fail();
            Features.BeltBindingStore.NoteExplicitPlacement(dest, item); // drag = may rebind a wedge
            MoveOneToSlot(item, dest);
            SlotFlash.OnStow(dest, item);
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            return true;
        }

        /// <summary>Drop a parked item at the player's feet (Option A parking dump; also the grid's and
        /// the HUD drag's ground-drop). Verified against the pinned occupant so a stale chip can never
        /// drop someone else's item.
        ///
        /// <para>A1 — the funnel's own backstop, whatever the caller already checked:
        /// <list type="bullet">
        /// <item>Only an item the LOCAL player is carrying (<see cref="IsCarriedByLocalPlayer"/>). "Drop
        /// at my feet" of something still sitting in a WORLD container would eject it from that
        /// container at any range, and the server validates nothing: <c>MoveToWorldMessage.Process</c>
        /// just finds the thing and calls <c>MoveToWorld</c> (27758 MoveToWorldMessage.cs:46-55) —
        /// no reach, ownership or parent check. A world item (no parent slot) fails this too.</item>
        /// <item>Never out of a LOCKED slot — vanilla parity: <c>SlotDisplayButton.PlayerMoveToWorld</c>
        /// refuses <c>Slot.IsLocked</c> (27758 SlotDisplayButton.cs:472-479), and a lock can be set at
        /// runtime (the electric jetpack locks its battery slot, JetpackElectric.cs:174).</item>
        /// </list>
        /// Both refusals are SILENT (no fail cue, no message on the wire), the method's existing
        /// contract: the chip dump reports its own count, and the grid / HUD drag just restore.</para></summary>
        public static bool DropToWorld(ScannedSlot source)
        {
            DynamicThing item = source?.Occupant;
            if (item == null) return false; // silent: dump loops report their own result
            if (source.Expected != null && item != source.Expected) return false;
            if (!IsCarriedByLocalPlayer(item)) return false;                  // A1: never out of the world / a teammate
            if (source.Slot != null && source.Slot.IsLocked) return false;   // vanilla PlayerMoveToWorld parity
            OnServer.MoveToSlotOrWorld(item, null);
            MaybeCancelPlacement(source.Slot); // a held constructor dropped to the world: no ghost hologram
            return true;
        }

        /// <summary>Eject a slot's occupant to the free hand, or the world if both hands are full.</summary>
        public static bool Eject(Slot slot)
        {
            DynamicThing item = slot?.Get();
            var human = InventoryManager.ParentHuman;
            if (item == null || human == null) return Fail();

            Slot freeHand = null;
            if (human.LeftHandSlot != null && human.LeftHandSlot.Get() == null && Slot.AllowMove(item, human.LeftHandSlot))
                freeHand = human.LeftHandSlot;
            else if (human.RightHandSlot != null && human.RightHandSlot.Get() == null && Slot.AllowMove(item, human.RightHandSlot))
                freeHand = human.RightHandSlot;

            OnServer.MoveToSlotOrWorld(item, freeHand);
            UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
            return true;
        }

        /// <summary>
        /// Is <paramref name="item"/> carried by the LOCAL player — does its parent chain
        /// (<c>ParentSlot.Parent</c>, walked up to 10 levels) end at the local player's Human
        /// (<c>InventoryManager.ParentHuman</c>, 27758 InventoryManager.cs:89 — the <c>Parent</c>
        /// entity's Human cast, :49)? In a hand, worn, or nested in anything worn/held = true; in the
        /// world, in a world container, or on a teammate = false. Fail-CLOSED: null item, no local
        /// Human, or a chain deeper than the cap all answer false.
        ///
        /// <para>The mod's ONE possession idiom — public so the radial layer can make the same call
        /// BEFORE offering a gesture (e.g. a parked chip that is no longer ours cancels quietly)
        /// instead of growing a second copy that could drift. Every funnel here still re-checks it at
        /// execute time.</para>
        ///
        /// <para>Execute-time possession gate for interact paths — the interactable counterpart of
        /// the ScannedSlot.Expected pin on move paths. Radial entries capture Thing/Interactable
        /// references at build time; by the time the user clicks or scrolls, a teammate may
        /// have taken the item (sticky radials only refresh after our OWN actions), or it may
        /// have despawned. Vanilla's server side does NOT possession-check InteractionMessages
        /// (Thing.PreventInteraction is only IsBroken/AllowInteraction/IsAuthorized), so the
        /// client must refuse to send: never adjust a device sitting in someone else's bag. It is
        /// also <see cref="DropToWorld"/>'s backstop (A1): the move-to-world message is not
        /// server-validated either.</para>
        /// </summary>
        public static bool IsCarriedByLocalPlayer(Thing item)
        {
            var human = InventoryManager.ParentHuman;
            if (human == null || item == null) return false;
            Thing node = item;
            int depth = 0;
            while (node != null && depth++ < 10)
            {
                if (node == human) return true;
                node = (node as DynamicThing)?.ParentSlot?.Parent;
            }
            return false;
        }

        /// <summary>Toggle a tool on/off through the same gate the vanilla hand-power key uses.</summary>
        public static bool ToggleOnOff(Thing thing)
        {
            if (thing == null || thing.InteractOnOff == null) return Fail();
            if (!IsCarriedByLocalPlayer(thing)) return Fail(); // moved/taken since menu build
            if (thing is DynamicThing dyn && (!dyn.CheckTogglePower() || !dyn.ShouldToggleOn()))
                return Fail();
            thing.Interact(InteractableType.OnOff, thing.OnOff ? 0 : 1);
            return true;
        }

        /// <summary>Cycle the tool's mode, wrapping over its own ModeStrings (each class clamps itself server-side).</summary>
        public static bool CycleMode(Thing thing)
        {
            if (thing == null || thing.InteractMode == null) return Fail();
            if (!IsCarriedByLocalPlayer(thing)) return Fail();
            int count = thing.ModeStrings != null ? thing.ModeStrings.Length : 0;
            int next = count > 0 ? (thing.Mode + 1) % count : (thing.Mode == 0 ? 1 : 0);
            Thing.Interact(thing.InteractMode, next);
            return true;
        }

        /// <summary>Toggle a generic 0/1 interactable (Open, Activate, ...).</summary>
        public static bool ToggleInteractable(Interactable interactable)
        {
            if (interactable == null) return Fail();
            if (!IsCarriedByLocalPlayer(interactable.Parent)) return Fail();
            Thing.Interact(interactable, interactable.State == 1 ? 0 : 1);
            return true;
        }

        /// <summary>
        /// One vanilla "button press" on an interactable — the canonical funnel for the
        /// Option A device controls (scroll steps, filtration/air-release toggles).
        /// PlayerInteractWith() builds the Interaction and routes host -> OnServer.InteractWith
        /// / client -> ONE InteractionMessage; the item's own InteractWith override then
        /// validates, clamps and mutates SERVER-SIDE under GameManager.RunSimulation
        /// (Interactable.cs:418-429, NetworkClient.cs:482-507, InteractionMessage.cs:10-27).
        /// Never use Thing.Interact(InteractableType,...) for Button4+: the type switch
        /// doesn't map them (Thing.cs:3791) and the raw state-set path skips clamping.
        /// <paramref name="owner"/> is the Thing the wedge was built for — re-verified as
        /// still ours at execute time (a destroyed Thing also fails the Unity null check).
        /// </summary>
        public static bool PressInteractable(Thing owner, Interactable interactable)
            => PressInteractable(owner, interactable, 1);

        /// <summary>
        /// <paramref name="times"/> > 1 is the coarse scroll step (one wheel notch = ±10 on
        /// suit pressure/temperature, whose vanilla buttons are hardwired to ±1/press).
        /// This is the mod's SECOND sanctioned multi-message action (after the chip dump):
        /// explicitly user-designed, hard-capped at 10, each press individually validated
        /// and clamped server-side (spamming past a bound just returns AlreadyMax/Min).
        /// </summary>
        public static bool PressInteractable(Thing owner, Interactable interactable, int times)
        {
            if (owner == null || interactable == null) return Fail();
            if (interactable.Parent != owner) return Fail();  // wedge and interactable disagree
            if (!IsCarriedByLocalPlayer(owner)) return Fail();
            times = UnityEngine.Mathf.Clamp(times, 1, 10);
            for (int i = 0; i < times; i++)
                interactable.PlayerInteractWith();
            return true;
        }

        /// <summary>Split a stack MP-safely via vanilla's OWN split interactions — Button1
        /// takes one, Button2 takes half (27701 Stackable.cs:346-380, which routes through
        /// SplitIntoHand → OnServer.MoveToSlot). Runs through PlayerInteractWith like every
        /// other control, so it's one authoritative message; no client-side Quantity touch.</summary>
        public static bool SplitStack(Thing stackable, bool half)
        {
            if (stackable == null) return Fail();
            var want = half ? InteractableType.Button2 : InteractableType.Button1;
            Interactable target = null;
            try
            {
                if (stackable.Interactables != null)
                    foreach (var it in stackable.Interactables)
                        if (it != null && it.Action == want) { target = it; break; }
            }
            catch { }
            if (target == null) return Fail();
            return PressInteractable(stackable, target);
        }

        /// <summary>Vanilla "Unpack" on a sealed disposable box (cereal / water / kit package):
        /// fire its Button1 interaction through the authoritative funnel. The box's own
        /// DisposableCardboardBox.InteractWith pops one item out (into the free hand, else to the
        /// world) and destroys the box once empty — all server-side. PressInteractable requires the
        /// box to be carried by the local player, exactly as vanilla's own Unpack does.</summary>
        public static bool Unpack(Thing box)
        {
            if (box == null) return Fail();
            Interactable target = box.InteractButton1;
            if (target == null || target.Parent != box)
            {
                target = null;
                try
                {
                    if (box.Interactables != null)
                        foreach (var it in box.Interactables)
                            if (it != null && it.Action == InteractableType.Button1) { target = it; break; }
                }
                catch { }
            }
            if (target == null) return Fail();
            return PressInteractable(box, target);
        }

        /// <summary>Arbitrary-count split. There is NO vanilla interaction for it, so it is
        /// only performed while WE are the simulation authority (host / single-player), where
        /// SplitStack(int, Slot) runs server-side exactly as it would for any interaction.
        /// Returns false (offer hidden) on a multiplayer client — never a client Quantity
        /// mutation.</summary>
        public static bool SplitStackCount(Thing stackable, int count)
        {
            var s = stackable as Assets.Scripts.Objects.Items.Stackable;
            if (s == null) return Fail();
            if (!IsCarriedByLocalPlayer(stackable)) return Fail();
            bool authoritative = false;
            try { authoritative = Assets.Scripts.GameManager.RunSimulation; } catch { }
            if (!authoritative) return Fail();          // MP client: not ours to mutate
            count = UnityEngine.Mathf.Clamp(count, 1, s.Quantity - 1);
            if (count < 1) return Fail();
            var human = InventoryManager.ParentHuman;
            Slot free = human != null
                ? (human.LeftHandSlot?.Get() == null ? human.LeftHandSlot
                 : human.RightHandSlot?.Get() == null ? human.RightHandSlot : null)
                : null;
            try
            {
                if (free != null)
                {
                    s.SplitStack(count, free);      // peel the count off into the free hand
                }
                else
                {
                    // Both hands full → drop the split at a physics-SAFE position out in front of
                    // the player (host/SP only, gated). This mirrors vanilla's own
                    // Stackable.SplitStack(Interaction) exactly (27701 Stackable.cs:389-416):
                    // raycast forward 0.5m from the body centre (or the helmet when aiming) and
                    // spawn at the clear point, then copy colour/damage and reduce the source.
                    //   Old bug: we spawned at human.ThingTransformPosition — the player ORIGIN,
                    //   *inside* the character capsule collider — so physics violently ejected the
                    //   new stack, launching the player and damaging the suit. GetSafeDropPosition
                    //   is the vanilla helper that avoids exactly that.
                    if (human == null) return Fail();
                    var prefab = s.SourcePrefab;
                    if (prefab == null) return Fail();
                    UnityEngine.Vector3 origin = human.AimIk
                        ? human.HelmetSlot.Location.position : human.CenterPosition;
                    UnityEngine.Vector3 forward = human.AimIk
                        ? human.HelmetSlot.Location.forward : human.ThingTransform.forward;
                    UnityEngine.Vector3 dropPos =
                        s.GetSafeDropPosition(origin + forward * 0.1f, forward, 0.5f);
                    var split = OnServer.Create<Assets.Scripts.Objects.Items.Stackable>(
                        prefab, dropPos, s.ThingTransform.rotation);
                    if (split == null) return Fail();
                    if (s.CustomColor != null && s.CustomColor.IsSet)
                        OnServer.SetCustomColor(split, s.CustomColor.Index);
                    split.Quantity = UnityEngine.Mathf.Min(count, split.MaxQuantity);
                    split.DamageState.Copy(s.DamageState);   // split keeps the source's damage
                    s.Quantity -= count;            // vanilla reduces the source by the split amount
                }
                UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                return true;
            }
            catch { return Fail(); }
        }

        /// <summary>Is arbitrary-count split available right now (host/SP + a stack &gt; 1)?</summary>
        public static bool CanSplitCount(Thing stackable)
        {
            var s = stackable as Assets.Scripts.Objects.Items.Stackable;
            if (s == null || s.Quantity < 2) return false;
            try { return Assets.Scripts.GameManager.RunSimulation; } catch { return false; }
        }

        /// <summary>Does vanilla offer a Sort button for this container? True when it has at least
        /// one GENERIC storage slot (<see cref="Slot.IsSortable"/> = None/Ore type) and 2+ items to
        /// reorder — i.e. bags, boxes, crates, ore belts, and a jetpack's/backpack's storage, but
        /// not pure-device slots. The container must sit in a slot so SortContents has a handle.</summary>
        public static bool CanSortContainer(DynamicThing thing)
        {
            if (thing?.Slots == null || thing.ParentSlot == null) return false;
            bool sortable = false;
            int occupied = 0;
            foreach (var s in thing.Slots)
            {
                if (s == null) continue;
                if (s.IsSortable) sortable = true;
                if (s.Get() != null) occupied++;
            }
            return sortable && occupied >= 2;
        }

        /// <summary>Reorganise a bag's contents through vanilla's OWN sort funnel — Slot.SortContents
        /// (host: OnServer.SortContents; client: a SortContentsMessage, Slot.cs:828-845). The bag
        /// must sit in a slot within our carried inventory. One authoritative message; no client
        /// slot shuffling.</summary>
        public static bool SortContainer(DynamicThing bag)
        {
            if (bag == null) return Fail();
            Slot slot = bag.ParentSlot;
            if (slot == null || !IsCarriedByLocalPlayer(bag)) return Fail();
            try { slot.SortContents(); }
            catch { return Fail(); }
            UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            return true;
        }

        /// <summary>
        /// Rename a Thing exactly as a LABELLER does. The mod's only NAME mutation, and its only
        /// game-state write outside the slot funnel — so it lives here with the rest of them.
        ///
        /// <para>Verified chain (27758): <c>Labeller.InputRenameFinished</c> (Labeller.cs:97-128)
        /// sanitises, then forks — host/SP <c>Thing.RenameThing(id, name)</c> (identical to
        /// <c>OnServer.SetCustomName</c>, OnServer.cs:938-947 -> Thing.cs:1496-1502); MP client
        /// <c>NetworkClient.RenameThing(id, name)</c> (NetworkClient.cs:524-535) -> exactly ONE
        /// <c>ThingRenameMessage</c> -> server <c>Process</c> (ThingRenameMessage.cs:9-12) ->
        /// <c>OnServer.SetCustomName</c> -> the <c>Thing.CustomName</c> setter raises
        /// <c>NetworkUpdateFlags</c> bit 32 (Thing.cs:1295-1310), which replicates to every client
        /// (Thing.cs:6296-6301) and to late joiners (Thing.cs:6081-6086), and persists in the save.
        /// The fork is MANDATORY, not defensive: <c>OnServer.SetCustomName</c> on a client writes
        /// <c>_customName</c> locally with no flag and no message = a desync that never heals.</para>
        ///
        /// <para><b>The server validates nothing.</b> <c>ThingRenameMessage</c> has no authority,
        /// ownership, range, power, length or content check, and <c>NetworkBase</c> adds none — so
        /// every gate below IS the gate, exactly as the labeller's own (client-side) gates are.
        /// <c>Thing.RenameThing(long,string)</c> also has no null check and does not route through
        /// the deferred queue, so an unknown ReferenceId NREs INSIDE THE SERVER's message pump:
        /// never send without a live id, re-read at execute time.</para>
        ///
        /// <para>Not reproduced: the labeller's three local SFX (Labeller.cs:130-137) — we play a
        /// UIAudioManager cue instead, like every other action here.</para>
        /// </summary>
        public static bool RenameThing(Thing thing, string newName)
        {
            if (thing == null) return Fail();
            // OUR gate, and the honest one: only something the local player is actually carrying.
            // On a host RunSimulation makes HasAuthority unconditionally true, so this - not the
            // authority check below - is what stops a host renaming the world.
            if (!IsCarriedByLocalPlayer(thing)) return Fail();
            // Mirror Labeller.Rename's own gate (Labeller.cs:19-22, on the labeller's HOLDER). For a
            // container carried by the local player, DynamicThing.HasAuthority resolves to "my brain
            // owns the Human this is nested under" (DynamicThing.cs:791-796), which is TRUE on an MP
            // client - the check is meaningful there and free on a host.
            bool authority = false;
            try { authority = thing.HasAuthority; } catch { }
            if (!authority) return Fail();

            long id;
            try { id = thing.ReferenceId; } catch { return Fail(); }
            if (id == 0) return Fail();      // never hand the server an id it cannot Find()

            string name = SanitizeThingName(thing, newName);
            if (string.IsNullOrEmpty(name)) return Fail();

            try
            {
                if (Assets.Scripts.GameManager.RunSimulation)
                    OnServer.SetCustomName(thing, name);                  // host / single-player
                else
                    Assets.Scripts.NetworkClient.RenameThing(id, name);   // MP client: ONE message
            }
            catch { return Fail(); }
            UIAudioManager.Play(UIAudioManager.ObjectPutHash);
            return true;
        }

        /// <summary>What <see cref="LabelWith"/> did (or declined to do).</summary>
        public enum LabelResult
        {
            /// <summary>The container already carries that exact label — nothing was sent.</summary>
            Unchanged,
            /// <summary>A rename went out through the funnel.</summary>
            Renamed,
            /// <summary>Refused or failed — a gate said no, the name sanitised away, or the send threw.</summary>
            Failed
        }

        /// <summary>LABEL a container with a Bag Profile's name (SmartStow's rename-on-assign). The one
        /// implementation of that gesture, shared by the F10 bag cards and the Universal Inventory's
        /// profile popup — the two surfaces were carrying their own copies of "skip if it already says
        /// that, otherwise RenameThing in a try/catch", which is precisely the kind of pair that drifts
        /// apart (they already had: one compared raw, both should compare sanitised).
        ///
        /// <para>The equality check runs against <see cref="SanitizedRenamePreview"/>, not the raw
        /// profile name, because the sanitised form is what <c>CustomName</c> will hold. Comparing raw
        /// made every profile whose name carries markup or non-ASCII compare unequal forever and
        /// re-send a rename message on every assign.</para></summary>
        public static LabelResult LabelWith(Thing thing, string label)
        {
            if (thing == null || string.IsNullOrEmpty(label)) return LabelResult.Failed;
            string wanted = SanitizedRenamePreview(thing, label);
            if (string.IsNullOrEmpty(wanted)) return LabelResult.Failed;

            string current = null;
            try { current = thing.CustomName; } catch { }
            if (string.Equals(current, wanted, System.StringComparison.Ordinal)) return LabelResult.Unchanged;

            bool ok = false;
            try { ok = RenameThing(thing, wanted); }
            catch (System.Exception e) { UIALog.Warn("Rename on assign failed: " + e.Message); }
            return ok ? LabelResult.Renamed : LabelResult.Failed;
        }

        /// <summary>What <see cref="RenameThing"/> would actually WRITE for this name — the sanitiser's
        /// output, without sending anything. Null when nothing usable survives (which is exactly when
        /// <see cref="RenameThing"/> would refuse).
        ///
        /// <para>Exists so callers can answer "is it already called that?" against the string that
        /// will reach <c>Thing.CustomName</c> rather than against their raw input. Comparing the raw
        /// name instead makes any name carrying markup or non-ASCII compare unequal FOREVER — the
        /// stored name is the stripped form, the wanted name is not — so a "skip if unchanged" guard
        /// silently re-sends a rename message on every single assign.</para></summary>
        public static string SanitizedRenamePreview(Thing thing, string newName)
        {
            if (thing == null) return null;
            try { return SanitizeThingName(thing, newName); }
            catch { return null; }
        }

        /// <summary><c>Labeller.InputRenameFinished</c>'s sanitiser reproduced IN ORDER
        /// (Labeller.cs:99-118): empty -> the prefab's own display name; 200-character cap; still
        /// empty -> refuse; then the rich-text strip for anything that is not a <c>Sign</c>/
        /// <c>Label</c> (an unstripped "&lt;...&gt;" becomes live TMP markup in every client's
        /// tooltips and labels). The final ASCII pass is OURS, not vanilla's — this mod renders
        /// names through a TMP font that only carries Basic Latin, so a non-ASCII name would tofu
        /// in our own UI after we ourselves wrote it.</summary>
        private static string SanitizeThingName(Thing thing, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                try { value = thing.SourcePrefab != null ? thing.SourcePrefab.DisplayName : null; }
                catch { value = null; }
            }
            if (!string.IsNullOrEmpty(value) && value.Length > 200) value = value.Substring(0, 200);
            if (string.IsNullOrEmpty(value)) return null;
            if (!(thing is Sign) && !(thing is Label))
                value = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", "");
            return AsciiOnly(value);
        }

        /// <summary>Basic-Latin printable characters only, trimmed; null when nothing survives.</summary>
        private static string AsciiOnly(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            bool clean = true;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < ' ' || c > '~') { clean = false; break; }
            }
            if (!clean)
            {
                var sb = new System.Text.StringBuilder(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c >= ' ' && c <= '~') sb.Append(c);
                }
                value = sb.ToString();
            }
            value = value.Trim();
            return value.Length == 0 ? null : value;
        }

        /// <summary>Merge the held stackable into an existing stack (server-authoritative).</summary>
        public static bool MergeInto(IMergeable targetStack, IMergeable held)
        {
            if (targetStack == null || held == null) return Fail();
            Thing.Merge(targetStack, held);
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            return true;
        }

        private static bool Fail()
        {
            UIAudioManager.Play(UIAudioManager.ActionFailHash);
            return false;
        }
    }
}

