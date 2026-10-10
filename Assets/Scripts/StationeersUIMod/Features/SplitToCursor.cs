using System;
using Assets.Scripts.Inventory;          // InventoryManager
using Assets.Scripts.Objects;            // Thing, DynamicThing, Slot
using Assets.Scripts.Objects.Items;      // Stackable
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Opt-in "split onto the cursor" (<see cref="UIAConfig.SplitToCursor"/>, default OFF) — the
    /// DETECTION half. It never creates, moves or splits anything: the split itself still goes
    /// through exactly today's funnel (<see cref="ItemActions.SplitStack"/> = vanilla's own
    /// Button1/Button2 interaction, or the host/SP-only <see cref="ItemActions.SplitStackCount"/>),
    /// and vanilla decides where the child lands (live V27798 <c>Stackable.SplitStack(Interaction,int)</c>
    /// + <c>SplitIntoHand</c>/<c>TrySplitIntoHand</c>, Assets.Scripts.Objects.Items/Stackable.cs
    /// ~297-377: an empty hand, else a merge into a same-prefab non-full hand stack, else the floor).
    ///
    /// <para>HOW. <see cref="Run"/> snapshots both hand occupants, dispatches the caller's split, and
    /// then WATCHES (up to <see cref="ClientWindowSec"/> on an MP client, where the child only exists
    /// after the server echo; a short window on host/SP, where <c>OnServer.InteractWith</c> /
    /// <c>OnServer.MoveToSlot</c> apply synchronously — Interactable.cs:481-492, OnServer.cs:53-70)
    /// for a hand slot whose occupant is a NEW stack: a different ReferenceId from both snapshot
    /// occupants and from the source, the source's PrefabHash, and no bigger than the source was
    /// before the split (the StowHomeStore split-inheritance filter: a picked-up floor stack must not
    /// be mistaken for the child). Found = a READY result for the context that asked; the consumer
    /// (the radial's parking layer or the Universal Inventory's click-to-place carry) polls it with
    /// <see cref="TryTake"/> and attaches the child to the cursor. Not found (the split merged into a
    /// hand stack, or landed on the floor because both hands were full, or the dispatch was refused)
    /// = nothing attaches and the vanilla outcome simply stands.</para>
    ///
    /// <para>MP-SAFE BY CONSTRUCTION: read-only over NETWORKED state (slot occupants, ReferenceId,
    /// PrefabHash, Quantity — all replicated), no message of its own. Every move the carry later
    /// performs is a normal ItemActions call re-verified at execute time against the pinned child.</para>
    ///
    /// <para>Static state is reset by <see cref="Shutdown"/> (F6 hot reload) and per context by
    /// <see cref="Cancel"/>, which every consumer calls when it goes away, so a stale result can
    /// never attach to a later, unrelated radial or window.</para>
    /// </summary>
    public static class SplitToCursor
    {
        /// <summary>Who asked for the split — only that surface may take the result.</summary>
        public enum Ctx { None = 0, Radial, Grid }

        /// <summary>How long an MP client waits for the server-echoed child (seconds).</summary>
        private const float ClientWindowSec = 3f;
        /// <summary>Host/SP: the child is already placed when the dispatch returns; this is only a
        /// safety margin for a deferred hand move.</summary>
        private const float HostWindowSec = 0.5f;
        /// <summary>A found child waits this long for its surface to be idle (no press / drag in
        /// flight) before the attach is abandoned — bounds how LATE a child can jump to the cursor.</summary>
        private const float ReadySec = 4f;

        // ---- the live watch (one at a time: one user action = one split) ----
        private static Ctx _watchCtx;
        private static long _srcId;
        private static int _prefab;
        private static int _preQty;
        private static long _leftBefore, _rightBefore;
        private static float _until;

        // ---- the ready result ----
        private static Ctx _readyCtx;
        private static Slot _readySlot;
        private static DynamicThing _readyItem;
        private static float _readyUntil;

        /// <summary>Is the opt-in on? Fail-soft to OFF (vanilla) before the config binds.</summary>
        public static bool Enabled
        {
            get
            {
                try { return UIAConfig.SplitToCursor != null && UIAConfig.SplitToCursor.Value; }
                catch { return false; }
            }
        }

        /// <summary>Dispatch a split for <paramref name="ctx"/>. With the opt-in OFF (or no context)
        /// this is exactly <c>dispatch()</c> — zero behaviour change. ON: snapshot the hands, run the
        /// SAME dispatch, and on success start watching for the child. Returns what the dispatch
        /// returned, so callers keep their existing success handling (tutorial signals etc.).</summary>
        public static bool Run(Ctx ctx, Thing source, Func<bool> dispatch)
        {
            if (dispatch == null) return false;
            if (ctx == Ctx.None || !Enabled || !(source is Stackable)) return dispatch();

            long srcId = 0; int prefab = 0, qty = 0;
            long leftBefore = 0, rightBefore = 0;
            try
            {
                srcId = source.ReferenceId;
                prefab = source.PrefabHash;
                qty = ((Stackable)source).Quantity;   // PRE-split: both funnels dispatch before any change
                var human = InventoryManager.ParentHuman;
                if (human != null)
                {
                    leftBefore = OccId(human.LeftHandSlot);
                    rightBefore = OccId(human.RightHandSlot);
                }
            }
            catch { return dispatch(); }               // unreadable: plain vanilla split, no watch

            bool ok = dispatch();
            if (!ok) return false;

            Clear();                                    // a new split supersedes any older watch/result
            _watchCtx = ctx;
            _srcId = srcId;
            _prefab = prefab;
            _preQty = qty;
            _leftBefore = leftBefore;
            _rightBefore = rightBefore;
            bool host = false;
            try { host = Assets.Scripts.GameManager.RunSimulation; } catch { }
            _until = UnityEngine.Time.unscaledTime + (host ? HostWindowSec : ClientWindowSec);
            Poll();                                     // host/SP: usually found right here
            return true;
        }

        /// <summary>Cheap idle test (no poll): is anything — a watch or a found child — held for
        /// <paramref name="ctx"/>? Lets a per-frame consumer skip all its gating when idle.</summary>
        public static bool Waiting(Ctx ctx)
        {
            return ctx != Ctx.None && (_watchCtx == ctx || _readyCtx == ctx);
        }

        /// <summary>Should the surface that dispatched for <paramref name="ctx"/> wait for a child (the
        /// radial stays open for it)? A found child: yes. Still watching: yes on an MP client (the
        /// child only shows after the server echo), NO on host/SP — there the split applied
        /// synchronously inside the dispatch (OnServer.InteractWith -> Stackable.SplitStack ->
        /// OnServer.MoveToSlot, Interactable.cs:481-492, OnServer.cs:53-70), so "not found yet" means it
        /// merged into a hand stack or fell to the floor and the surface behaves exactly as vanilla.</summary>
        public static bool Expecting(Ctx ctx)
        {
            if (ctx == Ctx.None) return false;
            Poll();
            if (_readyCtx == ctx) return true;
            if (_watchCtx != ctx) return false;
            bool host = false;
            try { host = Assets.Scripts.GameManager.RunSimulation; } catch { }
            return !host;
        }

        /// <summary>Consume the ready result for <paramref name="ctx"/>: the hand slot holding the
        /// split child and the child itself (re-verified to still be that slot's occupant). False
        /// while nothing is ready. Consumed either way once returned true.</summary>
        public static bool TryTake(Ctx ctx, out Slot slot, out DynamicThing child)
        {
            slot = null;
            child = null;
            if (ctx == Ctx.None) return false;
            Poll();
            if (_readyCtx != ctx) return false;
            float t = 0f;
            try { t = UnityEngine.Time.unscaledTime; } catch { }
            if (t > _readyUntil) { ClearReady(); return false; }   // waited too long: vanilla stands
            var s = _readySlot;
            var c = _readyItem;
            ClearReady();
            DynamicThing now = null;
            try { now = s != null ? s.Get() : null; } catch { }
            if (c == null || now == null || !ReferenceEquals(now, c)) return false;   // moved/destroyed since
            slot = s;
            child = c;
            return true;
        }

        /// <summary>The surface for <paramref name="ctx"/> went away (radial closed / reopened, the
        /// window closed or lost the mouse): forget its watch and its result. The child, if it
        /// already landed, simply stays where vanilla put it.</summary>
        public static void Cancel(Ctx ctx)
        {
            if (ctx == Ctx.None) return;
            if (_watchCtx == ctx) ClearWatch();
            if (_readyCtx == ctx) ClearReady();
        }

        /// <summary>Hot-reload teardown: drop every static (Thing/Slot refs included).</summary>
        public static void Shutdown()
        {
            Clear();
        }

        // ------------------------------------------------------------------

        private static void Poll()
        {
            if (_watchCtx == Ctx.None) return;
            float now;
            try { now = UnityEngine.Time.unscaledTime; } catch { ClearWatch(); return; }
            try
            {
                var human = InventoryManager.ParentHuman;
                if (human != null)
                {
                    if (IsChild(human.LeftHandSlot)) { Ready(human.LeftHandSlot); return; }
                    if (IsChild(human.RightHandSlot)) { Ready(human.RightHandSlot); return; }
                }
            }
            catch { ClearWatch(); return; }
            if (now > _until) ClearWatch();             // merged / floor / refused: vanilla stands
        }

        private static bool IsChild(Slot hand)
        {
            if (hand == null) return false;
            var occ = hand.Get() as Stackable;
            if (occ == null) return false;
            long id = occ.ReferenceId;
            if (id == 0 || id == _srcId || id == _leftBefore || id == _rightBefore) return false;
            if (occ.PrefabHash != _prefab) return false;
            if (_preQty > 0 && occ.Quantity > _preQty) return false;   // too big to be this split's child
            return true;
        }

        private static void Ready(Slot hand)
        {
            var ctx = _watchCtx;
            DynamicThing child = null;
            try { child = hand.Get(); } catch { }
            ClearWatch();
            if (child == null) return;
            _readyCtx = ctx;
            _readySlot = hand;
            _readyItem = child;
            try { _readyUntil = UnityEngine.Time.unscaledTime + ReadySec; } catch { _readyUntil = 0f; }
        }

        private static long OccId(Slot s)
        {
            try
            {
                var o = s != null ? s.Get() : null;
                return o != null ? o.ReferenceId : 0L;
            }
            catch { return 0L; }
        }

        private static void ClearWatch()
        {
            _watchCtx = Ctx.None;
            _srcId = 0;
            _prefab = 0;
            _preQty = 0;
            _leftBefore = 0;
            _rightBefore = 0;
            _until = 0f;
        }

        private static void ClearReady()
        {
            _readyCtx = Ctx.None;
            _readySlot = null;
            _readyItem = null;
            _readyUntil = 0f;
        }

        private static void Clear()
        {
            ClearWatch();
            ClearReady();
        }
    }
}
