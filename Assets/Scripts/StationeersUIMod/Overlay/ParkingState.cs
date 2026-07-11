using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.Overlay
{
    /// <summary>
    /// Option A drag-out parking: items dragged off a radial sit on the screen as chips
    /// while the player keeps navigating, then get dragged into slots or dumped on the
    /// ground with the closing right-click.
    ///
    /// PARKING IS CLIENT-SIDE VISUAL ONLY. A parked item never leaves its slot; the
    /// mutation happens when a chip is dropped on a wedge (one move message) or when the
    /// radial is exited via RMB (one drop message per chip, each individually verified
    /// against its pinned Expected occupant at execute time).
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
