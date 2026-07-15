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
            if (!Slot.AllowSwap(source.Slot, hand)) return Fail();
            source.Slot.PlayerSwapToSlot(hand);
            return true;
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

        /// <summary>Stow the active-hand item into a specific empty slot.</summary>
        public static bool StowActiveHandTo(Slot destination)
        {
            Slot hand = InventoryManager.ActiveHandSlot;
            DynamicThing item = hand?.Get();
            if (item == null || destination == null || destination.Get() != null) return Fail();
            if (!Slot.AllowMove(item, destination)) return Fail();
            OnServer.MoveToSlot(item, destination);
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

            if (targetSlot.Get() == null)
            {
                if (!Slot.AllowMove(item, targetSlot)) return Fail();
                OnServer.MoveToSlot(item, targetSlot);
            }
            else
            {
                if (!Slot.AllowSwap(candidate.Slot, targetSlot)) return Fail();
                OnServer.SwapSlots(candidate.Slot, targetSlot);
            }
            UIAudioManager.Play(targetSlot.Type == Slot.Class.Battery
                ? UIAudioManager.InstallBatteryHash
                : UIAudioManager.ObjectPutHash);
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
            if (!Slot.AllowMove(item, destination)) return Fail();
            float maxDist = 3f;
            try { maxDist = CursorManager.MaxInteractDistance; } catch { }
            if ((item.ThingTransformPosition - human.ThingTransformPosition).magnitude > maxDist + 0.75f)
                return Fail();                                  // out of reach = no grab
            OnServer.MoveToSlot(item, destination);
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            return true;
        }

        /// <summary>Drop a parked item at the player's feet (Option A parking dump). Verified
        /// against the pinned occupant so a stale chip can never drop someone else's item.</summary>
        public static bool DropToWorld(ScannedSlot source)
        {
            DynamicThing item = source?.Occupant;
            if (item == null) return false; // silent: dump loops report their own result
            if (source.Expected != null && item != source.Expected) return false;
            OnServer.MoveToSlotOrWorld(item, null);
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
        /// Execute-time possession gate for interact paths — the interactable counterpart of
        /// the ScannedSlot.Expected pin on move paths. Radial entries capture Thing/Interactable
        /// references at build time; by the time the user clicks or scrolls, a teammate may
        /// have taken the item (sticky radials only refresh after our OWN actions), or it may
        /// have despawned. Vanilla's server side does NOT possession-check InteractionMessages
        /// (Thing.PreventInteraction is only IsBroken/AllowInteraction/IsAuthorized), so the
        /// client must refuse to send: never adjust a device sitting in someone else's bag.
        /// </summary>
        private static bool IsCarriedByLocalPlayer(Thing thing)
        {
            var human = InventoryManager.ParentHuman;
            if (human == null || thing == null) return false;
            Thing node = thing;
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
                    // Both hands full → create the split ON THE GROUND at the player (host/SP only,
                    // gated). This mirrors vanilla's own SplitStack(Interaction): create the new
                    // stack at a WORLD position, set its quantity, then reduce the source. The old
                    // SplitStack(count, null) spawned it at the world ORIGIN, which despawns — that's
                    // why the split "vanished" instead of dropping.
                    if (human == null) return Fail();
                    var prefab = s.SourcePrefab;
                    if (prefab == null) return Fail();
                    var split = OnServer.Create<Assets.Scripts.Objects.Items.Stackable>(
                        prefab, human.ThingTransformPosition, UnityEngine.Quaternion.identity);
                    if (split == null) return Fail();
                    split.Quantity = UnityEngine.Mathf.Min(count, split.MaxQuantity);
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

