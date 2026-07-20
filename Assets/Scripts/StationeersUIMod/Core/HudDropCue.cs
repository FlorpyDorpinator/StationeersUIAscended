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
        /// <summary>Which funnel will actually execute the drop. The cue MUST match it: each drag
        /// system resolves through a different ItemActions entry point with different capabilities,
        /// so a single source-blind rule lights boxes for drops that always fail (2026-07-20 review).
        /// Defaults to the radial chip case, so the radial needs no changes.</summary>
        public enum DropCueKind
        {
            /// <summary>Radial parking chip → ItemActions.SwapIntoSlot / MoveWorldItemToSlot.
            /// Neither has an insert rung, and the world variant refuses an occupied destination.</summary>
            RadialChip = 0,
            /// <summary>HUD box grab → ItemActions.DragTo (insert / merge / swap-from-parent).</summary>
            HudSlotDrag,
            /// <summary>Vanilla world drag → ItemActions.WorldDragTo (insert / merge / swap-to-world / move).</summary>
            WorldDrag,
        }

        /// <summary>Set by whoever publishes <see cref="Dragging"/>. Reset by <see cref="Clear"/>.</summary>
        public static DropCueKind Kind;

        public static bool Accepts(Slot slot)
        {
            var item = Dragging;
            if (item == null || slot == null) return false;
            try
            {
                if (Kind == DropCueKind.RadialChip)
                {
                    // UNCHANGED shipped behaviour, and correct for BOTH radial funnels: a world chip
                    // has no ParentSlot, so an occupied box stays dark — which is exactly what
                    // MoveWorldItemToSlot does (it hard-fails on an occupied destination).
                    var occ0 = slot.Get();
                    if (occ0 == null) return Slot.AllowMove(item, slot);
                    var from0 = item.ParentSlot;
                    return from0 != null && Slot.AllowSwap(from0, slot);
                }

                // DragTo / WorldDragTo ladders, rung for rung.
                if (Slot.CanInsert(item, slot)) return true;      // into a worn container's contents
                var occ = slot.Get();
                if (occ == null) return Slot.AllowMove(item, slot);
                if (Slot.CanMerge(item, slot)) return true;
                var from = item.ParentSlot;
                return from != null
                    ? Slot.AllowSwap(from, slot)                  // DragTo's source-slot rung
                    : Slot.AllowSwap(slot, item);                 // WorldDragTo's swap-to-world rung
            }
            catch { return false; }
        }

        public static void Clear()
        {
            Dragging = null;
            HoveredSlot = null;
            Kind = DropCueKind.RadialChip;   // back to the default publisher's semantics
        }
    }
}
