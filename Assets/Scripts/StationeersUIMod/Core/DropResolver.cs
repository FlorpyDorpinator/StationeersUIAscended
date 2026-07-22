using Assets.Scripts.Objects;     // Slot
using Assets.Scripts.UI;          // InputMouse, WorldMouseMode
using StationeersUIMod.UI.Hud;    // HudSlotDrag, HudSystem, HudDropZone

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Which INPUT SURFACE owns the pixel under the cursor at drop time. The mod's drag targets live
    /// in two disjoint input universes — the UGUI EventSystem (open vanilla slot buttons, the bag
    /// grid) and the HUD's own raw inverse-warped hit test (the hand / worn-equipment boxes, which
    /// are <c>raycastTarget = false</c> pure renderers the EventSystem cannot see) — so "what is under
    /// the cursor" has no single built-in answer. <see cref="DropResolver"/> gives one.
    /// </summary>
    internal enum DropSurface
    {
        /// <summary>Open space: nothing the mod recognises is under the cursor.</summary>
        None,
        /// <summary>A REAL vanilla slot button — an open bag / inventory window the mod never hides.
        /// The inbound world-drag patch deliberately CEDES these to vanilla (it resolves its own
        /// windows correctly); the outbound HUD release treats one as a normal move destination.</summary>
        VanillaSlot,
        /// <summary>A cell of the mod's Universal Inventory grid (<see cref="Grid.BagGridCell"/>).</summary>
        GridCell,
        /// <summary>A HUD hand or worn-equipment box (<see cref="HudSystem.ZoneAt"/>).</summary>
        HudZone,
        /// <summary>No slot resolved, but some OTHER UGUI element owns the pixel — a window's chrome,
        /// title bar or padding, the Control Center. An ABORT surface: callers must never read it as
        /// "aimed at open space" and drop to the world (the grab gate refuses those pixels too).</summary>
        OtherUi
    }

    /// <summary>The resolved drop destination: the target <see cref="Slot"/> (or null) plus which
    /// <see cref="DropSurface"/> owns the pixel, so a caller can distinguish "landed on a slot",
    /// "landed on other UI = abort" and "landed on open space = maybe drop-to-world".</summary>
    internal struct DropResolution
    {
        public Slot Slot;
        public DropSurface Surface;
        public bool HasSlot { get { return Slot != null; } }
    }

    /// <summary>
    /// The ONE cross-surface drop resolver. Given the cursor's current screen position it answers, in
    /// a single canonical priority order, which drop target the pixel belongs to — so every drag
    /// direction (the outbound HUD-box release, the inbound world/container drag routed by
    /// <see cref="WorldDrag"/>, and the grid cell drag-OUT) agrees about who owns a pixel and no two
    /// surfaces both act on one release.
    ///
    /// <para>PRIORITY (highest first): a real vanilla slot button (an open window drawn above
    /// everything should take the drop) &gt; a bag-grid cell &gt; a HUD hand/equipment box. This is
    /// exactly the order the outbound release used inline before it was centralised here, so behaviour
    /// is unchanged; the grid-cell rung is what lets a drop aimed at a cell land there instead of a HUD
    /// box behind it, or — worse — falling through to vanilla's through-the-panel physics raycast.</para>
    ///
    /// <para>The resolver only READS: it never mutates game state and never touches the drag machinery.
    /// It composes the surface-specific hit tests that already exist (<see cref="HudSlotDrag"/>'s
    /// EventSystem lookups and occlusion test, <see cref="HudSystem.ZoneAt"/>'s inverse-warped box
    /// test). It allocates one <c>PointerEventData</c> inside the grid raycast, so call it on the
    /// RELEASE frame only (both callers gate it behind a mouse-up check) — never per frame.</para>
    /// </summary>
    internal static class DropResolver
    {
        /// <summary>Resolve the drop destination + owning surface under the cursor right now.
        /// Read-only and allocation-light (one grid raycast); intended for the release frame.</summary>
        internal static DropResolution Resolve()
        {
            DropResolution r = new DropResolution();

            // 1. A REAL vanilla slot button (open bag / inventory window the mod never hides). Highest
            //    priority: a window drawn above the HUD should receive the drop, and the inbound patch
            //    hands these back to vanilla to resolve natively.
            Slot vanilla = HudSlotDrag.VanillaSlotUnderCursor();
            if (vanilla != null) { r.Slot = vanilla; r.Surface = DropSurface.VanillaSlot; return r; }

            // 2. A cell of the mod's Universal Inventory grid (a UGUI RaycastAll pick).
            Slot cell = HudSlotDrag.GridCellSlotUnderCursor();
            if (cell != null) { r.Slot = cell; r.Surface = DropSurface.GridCell; return r; }

            // 3. A HUD hand / worn-equipment box (raw inverse-warped hit test; the EventSystem can't see
            //    these — they are raycastTarget = false). An empty box still resolves; ItemActions gates
            //    validity at execute time.
            HudDropZone zone = HudSystem.ZoneAt();
            Slot hud = zone != null ? zone.Slot : null;
            if (hud != null) { r.Slot = hud; r.Surface = DropSurface.HudZone; return r; }

            // 4. No slot, but another UGUI element owns the pixel (window chrome / padding / the Control
            //    Center). An abort surface — never a drop-to-world.
            if (HudSlotDrag.PointerOverOtherUiPublic()) { r.Surface = DropSurface.OtherUi; return r; }

            // 5. Genuinely open space.
            r.Surface = DropSurface.None;
            return r;
        }

        /// <summary>True while a VANILLA world-sourced drag is in flight — an item picked up off the
        /// ground (<see cref="WorldMouseMode.Drag"/>) or pulled from a world container's slot
        /// (<see cref="WorldMouseMode.DragSlot"/>). This is cross-surface drag STATE read straight off
        /// <see cref="InputMouse"/>: a surface that parks vanilla's cursor raycast (The Grid) must
        /// consult it and hand the raycast BACK while such a drag is live, or vanilla's
        /// <c>InputMouse.Update</c> early-returns, never dispatches <c>Drag()/DragSlot()</c>, the
        /// <see cref="WorldDrag"/> prefix never fires, and the drag strands mid-air over the panel
        /// (InputMouse.cs:342 — see <c>TheGridPanel.UpdateCursorBlock</c>).</summary>
        internal static bool VanillaWorldDragLive()
        {
            try
            {
                InputMouse m = InputMouse.Instance;
                return m != null &&
                    (m.WorldMode == WorldMouseMode.Drag || m.WorldMode == WorldMouseMode.DragSlot);
            }
            catch { return false; }
        }
    }
}
