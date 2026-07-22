using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// GHOST ROUTING HINTS (design O4d — "dry-run made visible"): while an item drag is live in
    /// the Grid's PROFILE MODE, the bag that Smart Stow (G) WOULD route the dragged item into
    /// glows with the theme's own hover accent on its region border. This teaches the routing
    /// silently and debugs it for free ("why is my coal going in the food bag" becomes visible).
    ///
    /// <para><b>Plumbing.</b> <c>BagGridCell</c> reports the gesture (<see cref="BeginDrag"/> /
    /// <see cref="EndDrag"/>); <c>TheGridPanel.Tick</c> pumps <see cref="Tick"/> (in both its
    /// open and pins-only branches, since a drag can live on a pinned window with the main window
    /// closed); each <c>GridRegionView</c> asks <see cref="IsHinted"/> for its container — a
    /// static long compare per region per frame, folded into its existing style guard, so the
    /// accent rides the ONE shared <c>GridTheme.ApplyBox</c> hover mechanic (the tab keeps its
    /// separate drop-cue highlight: tab accent = "release here to PIN", region accent = "G would
    /// stow it HERE").</para>
    ///
    /// <para><b>Cost.</b> Zero steady-state work: with no drag live, Tick is one bool read and
    /// IsHinted compares against 0. During a drag the router dry-run (<see cref="StowRouter"/> —
    /// pure decision, pooled lists, never a mutation) re-resolves at most ~4x/sec, keyed
    /// implicitly by the dragged item (a new gesture resets the throttle so its first frame
    /// resolves immediately). The resolve mirrors the G key exactly: same depth, same config
    /// gates, same stage order — behaviour can never drift because it IS the same router.</para>
    ///
    /// <para><b>Hot reload.</b> The statics hold the dragged item/slot only for the duration of a
    /// gesture (every drag end/cancel path clears them); <see cref="Reset"/> is called from
    /// <c>TheGridPanel.Shutdown</c> so a double-F6 strands nothing.</para>
    /// </summary>
    public static class GridGhostHint
    {
        private const float ResolveInterval = 0.25f;   // at most ~4 dry-runs per second

        private static bool _dragLive;
        private static DynamicThing _dragItem;   // gesture-scoped only; cleared on every drag exit path
        private static Slot _dragSlot;           // the slot the drag started from (excluded by the router)
        private static long _hintRefId;          // ReferenceId of the would-receive bag (0 = no hint)
        private static float _nextResolve;

        /// <summary>An item drag started (called by the source cell AFTER it pinned its source).
        /// Recording is unconditional — the mode/config gates live in <see cref="Tick"/>, so a
        /// drag that starts outside profile mode still hints correctly if the mode is entered
        /// mid-gesture (and costs nothing otherwise).</summary>
        public static void BeginDrag(DynamicThing item, Slot sourceSlot)
        {
            _dragItem = item;
            _dragSlot = sourceSlot;
            _dragLive = item != null;
            _hintRefId = 0L;
            _nextResolve = 0f;   // first Tick after the grab resolves immediately
        }

        /// <summary>The gesture ended (drop, cancel, cell recycled, window hidden). Idempotent.</summary>
        public static void EndDrag()
        {
            _dragLive = false;
            _dragItem = null;
            _dragSlot = null;
            _hintRefId = 0L;
        }

        /// <summary>Is this container the current would-receive hint? A long compare — safe and
        /// free per region per frame.</summary>
        public static bool IsHinted(long containerRefId)
        {
            return containerRefId != 0L && containerRefId == _hintRefId;
        }

        /// <summary>Throttled dry-run pump (from <c>TheGridPanel.Tick</c>). No drag = one bool
        /// read. Mode/config off mid-drag clears the hint and resolves nothing.</summary>
        public static void Tick()
        {
            if (!_dragLive) return;
            if (!GridProfileMode.Active || !ConfigOn())
            {
                if (_hintRefId != 0L) _hintRefId = 0L;
                return;
            }
            if (Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + ResolveInterval;

            long id = 0L;
            try
            {
                DynamicThing item = _dragItem;
                StowCandidate winner;
                // dryRun: a hint resolve must be side-effect-free — in particular it must never
                // seed the belt home-slot table (a config disk write the REAL G press owns).
                if (item != null
                    && StowRouter.ResolveBest(item, _dragSlot, StowRouter.ConfiguredDepth(), true, out winner)
                    && winner.Holder != null)
                {
                    id = winner.Holder.ReferenceId;
                }
            }
            catch { id = 0L; }
            _hintRefId = id;
        }

        /// <summary>Hot-reload teardown (from <c>TheGridPanel.Shutdown</c>).</summary>
        public static void Reset()
        {
            EndDrag();
            _nextResolve = 0f;
        }

        /// <summary>O4d config gate — fail-open (default true) around an unbound entry. Also
        /// honours the SmartStow+ MASTER gates the executor checks (SmartStowPlus.TryStow):
        /// the glow means "G would stow HERE", and with SmartStow+ off a G press runs VANILLA
        /// stow — the sibling dry-run surfaces (F10 test box, stowtrace) print a disabled
        /// disclaimer, but a border glow cannot carry one, so the honest equivalent is to not
        /// show it at all.</summary>
        private static bool ConfigOn()
        {
            try
            {
                if (UIAConfig.GridGhostHints != null && !UIAConfig.GridGhostHints.Value) return false;
                if (UIAConfig.MasterEnable != null && !UIAConfig.MasterEnable.Value) return false;
                if (UIAConfig.SmartStowPlusEnabled != null && !UIAConfig.SmartStowPlusEnabled.Value) return false;
                return true;
            }
            catch { return true; }
        }
    }
}
