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
                    other.PlayerMoveToSlot(item);
                    return true;
                }
                return Fail(); // both hands busy: taking a part out of the held tool needs a free hand
            }

            if (handOcc == null)
            {
                if (!Slot.AllowMove(item, hand)) return Fail();
                hand.PlayerMoveToSlot(item);
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

        /// <summary>Toggle a tool on/off through the same gate the vanilla hand-power key uses.</summary>
        public static bool ToggleOnOff(Thing thing)
        {
            if (thing == null || thing.InteractOnOff == null) return Fail();
            if (thing is DynamicThing dyn && (!dyn.CheckTogglePower() || !dyn.ShouldToggleOn()))
                return Fail();
            thing.Interact(InteractableType.OnOff, thing.OnOff ? 0 : 1);
            return true;
        }

        /// <summary>Cycle the tool's mode, wrapping over its own ModeStrings (each class clamps itself server-side).</summary>
        public static bool CycleMode(Thing thing)
        {
            if (thing == null || thing.InteractMode == null) return Fail();
            int count = thing.ModeStrings != null ? thing.ModeStrings.Length : 0;
            int next = count > 0 ? (thing.Mode + 1) % count : (thing.Mode == 0 ? 1 : 0);
            Thing.Interact(thing.InteractMode, next);
            return true;
        }

        /// <summary>Toggle a generic 0/1 interactable (Open, Activate, ...).</summary>
        public static bool ToggleInteractable(Interactable interactable)
        {
            if (interactable == null) return Fail();
            Thing.Interact(interactable, interactable.State == 1 ? 0 : 1);
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

