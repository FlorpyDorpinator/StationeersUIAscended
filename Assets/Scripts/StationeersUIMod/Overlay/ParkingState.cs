using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.Overlay
{
    /// <summary>
    /// Option A drag-out parking: items dragged off a radial sit on the screen as chips
    /// while the player keeps navigating, then get dragged into slots or dropped on the
    /// ground when the radial closes — by ANY route (D-004, 2026-09-25: RMB, Escape, MMB, the
    /// opener key, switching to another radial, guards...; <c>RadialMenu.ReleaseHeldItems</c>).
    ///
    /// PARKING IS CLIENT-SIDE VISUAL ONLY. A parked item never leaves its slot; the
    /// mutation happens when a chip is dropped on a wedge (one move message) or when the
    /// radial closes (one drop message per chip, each individually verified against its
    /// pinned Expected occupant at execute time). Only items the local player CARRIES drop on
    /// close — a chip grabbed out of a world container (charger, locker) just cancels (A1) —
    /// and a wedge action that moves a parked item un-parks it first (A6).
    /// </summary>
    public sealed class ParkingState
    {
        public const int MaxChips = 12;

        /// <summary>Bubble radius in px — live-tunable in the radial editor.</summary>
        public static float ChipRadius => UIAConfig.ParkedChipRadius != null
            ? UIAConfig.ParkedChipRadius.Value : 39f;

        public sealed class Chip
        {
            public ScannedSlot Source;      // slot-sourced: Expected verified before any mutation
            public DynamicThing WorldSource; // world-sourced (Z-grab): must still be free-lying in range
            public Sprite Icon;
            public string Name;
            public Vector2 Pos;             // ImGui screen coords (y-down)

            public DynamicThing Item => WorldSource != null ? WorldSource : Source?.Occupant;
            public bool IsWorld => WorldSource != null;
        }

        public readonly List<Chip> Chips = new List<Chip>();
        public Chip Dragging;

        public bool Active => Chips.Count > 0 || Dragging != null;

        public void Clear()
        {
            Chips.Clear();
            Dragging = null;
        }

        public Chip ChipAt(Vector2 screenPos)
        {
            for (int i = Chips.Count - 1; i >= 0; i--)
                if ((Chips[i].Pos - screenPos).magnitude <= ChipRadius + 6f)
                    return Chips[i];
            return null;
        }

        /// <summary>One slot = one chip: a slot's item can only be parked once, or a stale
        /// duplicate could emit a second mutation message for the same item.</summary>
        public void RemoveBySlot(Slot slot)
        {
            if (slot == null) return;
            for (int i = Chips.Count - 1; i >= 0; i--)
                if (Chips[i].Source?.Slot == slot)
                    Chips.RemoveAt(i);
        }

        /// <summary>A6: drop every slot-sourced chip showing this item (its pinned or its current
        /// occupant) — an action is about to move it, so it must not also be dropped on close.
        /// Only removes chips; never mutates anything.</summary>
        public void RemoveByItem(DynamicThing item)
        {
            if (item == null) return;
            for (int i = Chips.Count - 1; i >= 0; i--)
            {
                var s = Chips[i].Source;
                if (s != null && (s.Expected == item || s.Occupant == item))
                    Chips.RemoveAt(i);
            }
        }

        /// <summary>Same rule for world-grabbed chips: one world item = one chip.</summary>
        public void RemoveByWorldThing(DynamicThing thing)
        {
            if (thing == null) return;
            for (int i = Chips.Count - 1; i >= 0; i--)
                if (Chips[i].WorldSource == thing)
                    Chips.RemoveAt(i);
        }

        /// <summary>Drop chips whose source is gone stale: slot-sourced when the slot no
        /// longer holds the pinned item; world-sourced when the thing was destroyed or
        /// someone picked it up (it gained a ParentSlot).</summary>
        public void Prune()
        {
            for (int i = Chips.Count - 1; i >= 0; i--)
                if (IsStale(Chips[i])) Chips.RemoveAt(i);
            if (Dragging != null && IsStale(Dragging))
                Dragging = null;
        }

        private static bool IsStale(Chip chip)
        {
            if (chip.IsWorld)
                return chip.WorldSource == null || chip.WorldSource.ParentSlot != null;
            var s = chip.Source;
            return s?.Slot == null || s.Occupant == null || (s.Expected != null && s.Occupant != s.Expected);
        }
    }
}
