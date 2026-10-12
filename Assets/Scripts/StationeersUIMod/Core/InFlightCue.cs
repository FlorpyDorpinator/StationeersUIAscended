using System.Collections.Generic;
using Assets.Scripts.Objects;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// "This item is IN FLIGHT" — a purely visual bridge so every surface that shows an item which
    /// the player is currently carrying on the cursor (a radial drag chip, a split-to-cursor carry
    /// chip, a parked chip, or the Universal Inventory's split-carry ghost) draws its source DIMMED,
    /// exactly like a Universal Inventory cell already dims its own drag source (BagGridCell
    /// <c>_dragDim</c>, alpha x0.35). FlorpyDorp 2026-10-11: a split child sitting in the hand box at
    /// full strength while it also rides the cursor read as "it went to my hand AND made an icon".
    ///
    /// <para>MP rule: nothing moves. The item physically stays in its slot until the placing click
    /// sends one ItemActions message; this only changes how its source is drawn.</para>
    ///
    /// <para>Publishers re-publish EVERY frame they are live (the radial from <c>RadialMenu.Draw</c>,
    /// the grid carry from <c>GridSplitCarry.Tick</c>); a reader treats a publication older than one
    /// frame as gone, so a surface that stops drawing (closed, stood down, threw) can never leave a
    /// box stuck dim. Holds Thing references: <see cref="Shutdown"/> drops them (F6 hot reload).</para>
    /// </summary>
    internal static class InFlightCue
    {
        private static readonly List<DynamicThing> _radial = new List<DynamicThing>(16);
        private static int _radialFrame = -999;
        private static DynamicThing _grid;
        private static int _gridFrame = -999;

        /// <summary>The radial publishes its in-flight chip and every parked chip (one call per Draw).</summary>
        public static void PublishRadial(DynamicThing dragging, IList<DynamicThing> parked)
        {
            _radial.Clear();
            if (dragging != null) _radial.Add(dragging);
            if (parked != null)
                for (int i = 0; i < parked.Count; i++)
                    if (parked[i] != null) _radial.Add(parked[i]);
            _radialFrame = Time.frameCount;
        }

        public static void ClearRadial()
        {
            _radial.Clear();
            _radialFrame = -999;
        }

        /// <summary>The Universal Inventory split carry publishes its child each Tick (null = none).</summary>
        public static void PublishGrid(DynamicThing child)
        {
            _grid = child;
            _gridFrame = child != null ? Time.frameCount : -999;
        }

        /// <summary>Should a surface showing <paramref name="item"/> draw it as an in-flight source?</summary>
        public static bool Is(DynamicThing item)
        {
            if (item == null) return false;
            int f = Time.frameCount;
            if (_grid != null && f - _gridFrame <= 1 && ReferenceEquals(_grid, item)) return true;
            if (f - _radialFrame <= 1)
                for (int i = 0; i < _radial.Count; i++)
                    if (ReferenceEquals(_radial[i], item)) return true;
            return false;
        }

        /// <summary>Hot-reload teardown: drop every Thing reference.</summary>
        public static void Shutdown()
        {
            ClearRadial();
            _grid = null;
            _gridFrame = -999;
        }
    }
}
