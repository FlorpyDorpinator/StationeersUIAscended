using Assets.Scripts.Objects;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// A tiny bridge that lets the visor HUD light up a hand / equipment box while the radial
    /// drag-layer is carrying an item over it (#4). The radial publishes the dragged item each
    /// frame; the HUD computes which drop-zone slot the cursor is over (via
    /// <c>HudSystem.ZoneAt</c>) before its widgets paint, so a box can show the drop cue exactly
    /// where a release would land. Purely visual — the actual drop still goes through the
    /// ItemActions funnel.
    /// </summary>
    public static class HudDropCue
    {
        /// <summary>The item the radial drag-layer is holding (null = nothing being dragged).
        /// Published by <c>RadialMenu.Draw</c>; cleared on close.</summary>
        public static DynamicThing Dragging;

        /// <summary>The HUD drop-zone slot under the cursor this frame, or null. Set by
        /// <c>HudSystem.Update</c> before the box widgets paint.</summary>
        public static Slot HoveredSlot;

        public static bool Active => Dragging != null;

        /// <summary>Would releasing the dragged item on <paramref name="slot"/> be accepted right
        /// now — a move into an empty slot, or a swap with its occupant? Same gates the drop uses,
        /// so only genuine targets light up.</summary>
        public static bool Accepts(Slot slot)
        {
            var item = Dragging;
            if (item == null || slot == null) return false;
            try
            {
                var occ = slot.Get();
                if (occ == null) return Slot.AllowMove(item, slot);
                var from = item.ParentSlot;
                return from != null && Slot.AllowSwap(from, slot);
            }
            catch { return false; }
        }

        public static void Clear()
        {
            Dragging = null;
            HoveredSlot = null;
        }
    }
}
