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
        public const float ChipRadius = 26f;

        public sealed class Chip
        {
            public ScannedSlot Source;   // pinned: Expected is verified before any mutation
            public Sprite Icon;
            public string Name;
            public Vector2 Pos;          // ImGui screen coords (y-down)
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

        /// <summary>Drop chips whose source slot no longer holds the pinned item (someone
        /// moved it — multiplayer or another of our own actions).</summary>
        public void Prune()
        {
            for (int i = Chips.Count - 1; i >= 0; i--)
            {
                var s = Chips[i].Source;
                if (s?.Slot == null || s.Occupant == null || (s.Expected != null && s.Occupant != s.Expected))
                    Chips.RemoveAt(i);
            }
            var d = Dragging?.Source;
            if (Dragging != null && (d?.Slot == null || d.Occupant == null || (d.Expected != null && d.Occupant != d.Expected)))
                Dragging = null;
        }
    }
}
