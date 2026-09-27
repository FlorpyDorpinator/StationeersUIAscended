using Assets.Scripts.Objects;
using StationeersUIMod.UI.Menu.Tutorial;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Opens a device/tool's internal slots (a drill's battery, a tablet's cartridge, a canister's
    /// filter) in the MOD's OWN themed, draggable, resizable window — a <c>PinnedInventoryWindow</c>,
    /// the same window a torn-out bag uses — rather than vanilla's InventoryWindow. Same functionality
    /// (the item's sub-slots, fully interactive), our UI.
    ///
    /// <para>It simply pins the item by its <c>ReferenceId</c> (<c>TheGridPanel.PinContainer</c>). The
    /// grid model now builds a container node for a PINNED slot-bearing item even when Grid mode would
    /// otherwise keep it a leaf cell (see <c>GridModel.ShouldRecurse</c>), and the panel tears that node
    /// out into a pinned window on the next rebuild. Closing the window (its X) unpins it.</para>
    ///
    /// <para>VIEW-ONLY / MP-safe: pinning records a per-save VIEW preference and mutates NO game state.
    /// The pinned window's cells reuse the exact same gated <c>ItemActions</c> funnel every grid cell
    /// does (occupant re-verified at execute time), so no new mutation path is introduced.</para>
    /// </summary>
    internal static class DeviceWindow
    {
        /// <summary>Worth a window — has something the player could change: at least one INTERACTABLE
        /// sub-slot (<c>Thing.HasSlots</c> — a drill's battery, a tablet's cartridge) OR a key
        /// interaction/button (<c>Thing.HasKeyInteractions</c> — a demo charge's lock/arm, a canister's
        /// valve, an on/off switch). A thing with NEITHER (duct tape, a plain ingot) returns false and is
        /// taken to the hand instead. Storage bags go through the grid's own tabs/pins, not this path.</summary>
        public static bool CanOpen(DynamicThing item)
        {
            if (item == null) return false;
            try { return item.HasSlots || item.HasKeyInteractions; } catch { return false; }
        }

        /// <summary>TOGGLE the item's internals as a themed pinned window — open it if closed, close it
        /// if already open — matching vanilla's click-to-toggle. Returns true when the gesture was
        /// consumed (opened or closed); false only for something with no internals or on failure (the
        /// caller then falls back to taking the item to hand).</summary>
        public static bool Open(DynamicThing item)
        {
            if (!CanOpen(item)) return false;
            try
            {
                long id = item.ReferenceId;
                if (id == 0L) return false;
                if (global::StationeersUIMod.UI.Grid.TheGridPanel.IsPinned(id))
                {
                    global::StationeersUIMod.UI.Grid.TheGridPanel.UnpinContainer(id);   // already open → close
                    return true;
                }
                bool opened = global::StationeersUIMod.UI.Grid.TheGridPanel.PinContainer(id);   // closed → open
                if (opened) TutorialSignals.Raise(TSignal.DeviceWindowOpened);
                return opened;
            }
            catch { return false; }
        }
    }
}
