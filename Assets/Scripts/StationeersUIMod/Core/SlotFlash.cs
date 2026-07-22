using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Transient "it went in here" icon flash for the HUD's OWN equipment / hand boxes — the mod's
    /// equivalent of vanilla <c>InventoryManager.PerformHiddenSlotMoveToAnimation</c> (a sprite swap
    /// on the destination container's slot display). When an item is stowed into a worn container,
    /// the box that shows that container briefly shows the STOWED item's thumbnail instead of the
    /// container's own, as a "that's where it went" confirmation, with a small scale pop.
    ///
    /// Timing is RENDER-based, not wall-clock: the countdown starts on the first frame a widget
    /// actually draws the flash, so a flash posted while its box is momentarily not rendering (the
    /// box sliced out, its canvas toggling as a radial opens/closes) still shows for its full
    /// duration once the box is back on screen — rather than silently expiring unseen. Purely
    /// visual; never touches the occupant. Hot-reload safe: <see cref="Reset"/> clears everything.
    /// </summary>
    public static class SlotFlash
    {
        private const float DurationSec = 0.35f; // snappy, near vanilla's instant swap — FlorpyDorp:
                                                 // "no slow animation of it getting smaller… make it faster"
        private const float MaxAgeSec = 6f;      // safety: drop a flash whose box never rendered

        private struct Entry { public Sprite Icon; public float Posted; public float StartedAt; }
        private static readonly Dictionary<Slot, Entry> _flashes = new Dictionary<Slot, Entry>();
        private static readonly List<Slot> _stale = new List<Slot>();

        /// <summary>Flash a specific slot's box with <paramref name="icon"/>. The countdown begins
        /// when a widget first renders it (see class remarks), not here.</summary>
        public static void Post(Slot slot, Sprite icon)
        {
            if (slot == null || icon == null) return;
            SweepStale();
            // Don't restart an identical flash that is already running. A stow can legitimately be
            // reported TWICE — SmartStowPlus posts its chosen destination immediately, and the
            // pending resolver below confirms the same landing a frame or two later — and the second
            // report must not extend or re-trigger the first.
            Entry existing;
            if (_flashes.TryGetValue(slot, out existing) && existing.Icon == icon) return;
            _flashes[slot] = new Entry { Icon = icon, Posted = Time.unscaledTime, StartedAt = -1f };
            try { UIALog.Debug("SlotFlash: post on '" + Name(slot) + "'"); } catch { }
        }

        private static string Name(Slot s)
        {
            if (s == null) return "<null>";
            try { return string.IsNullOrEmpty(s.DisplayName) ? s.Type.ToString() : s.DisplayName; }
            catch { return "<slot>"; }
        }

        /// <summary>An item was stowed into <paramref name="dest"/>. If dest sits INSIDE a worn
        /// container (not directly in a body / hand slot), flash the outermost worn box as a proxy —
        /// exactly vanilla's rule that the confirmation appears on the visible container when the
        /// true destination is a hidden nested slot. No-op when the item went straight into a
        /// directly-visible slot (donning a helmet, etc.), which needs no proxy.</summary>
        public static void OnStow(Slot dest, Sprite icon) { OnStow(dest, icon, null); }

        public static void OnStow(Slot dest, Sprite icon, DynamicThing item)
        {
            if (dest == null || icon == null) return;
            // One stow, one flash — see AlreadyFlashed.
            if (AlreadyFlashed(item)) return;
            Human human = null;
            try { human = InventoryManager.ParentHuman; } catch { }
            if (human == null) return;
            Slot worn = ResolveWornSlot(dest, human);
            // Diagnostics, not silence: the three previous attempts at this feature all failed
            // invisibly, so each decline says WHY (visible with the mod's debug logging on).
            if (worn == null)
            {
                try { UIALog.Debug("SlotFlash: dest '" + Name(dest) + "' has no worn parent on the local human — no flash"); } catch { }
                return;
            }
            if (worn == dest)
            {
                try { UIALog.Debug("SlotFlash: dest '" + Name(dest) + "' IS a directly-worn slot — its box already shows the item, no proxy flash"); } catch { }
                return;
            }
            Post(worn, icon);
            if (item != null) _flashed.Add(new Flashed { Item = item, At = Time.unscaledTime });
        }

        // ---- pending stow resolution: the branch-agnostic path ----
        //
        // WHY THIS EXISTS (three failed attempts before it): a smart-stow's destination is knowable
        // in only two ways — either WE picked it (SmartStowPlus does, and posts directly), or VANILLA
        // picked it. For vanilla the only hook is InventoryManager.PerformHiddenSlotMoveToAnimation,
        // and vanilla calls that in exactly ONE narrow branch: the nested-container fallback at
        // InventoryManager.cs:1855-1867. It is skipped entirely whenever an earlier branch succeeds —
        // notably FindFreeSlotOpenWindowsSlotPriority (:1851). So an item that SmartStowPlus declines
        // (anything that is not a tool / stack-merge / profile / memory match — e.g. a battery going
        // into the suit) can move with that coroutine never running, and the flash is silently lost.
        //
        // Instead of predicting the destination, OBSERVE it: snapshot the item as it leaves the hand,
        // then watch where it lands. Branch-agnostic (ours or vanilla's) and latency-tolerant — on a
        // multiplayer client OnServer.MoveToSlot is only a request, so the occupant moves some frames
        // later, which a same-frame postfix would miss entirely.
        private struct Pending { public DynamicThing Item; public Sprite Icon; public Slot From; public float Posted; }
        private static readonly List<Pending> _pending = new List<Pending>();
        private const float PendingTimeoutSec = 3f; // generous enough for MP round-trip, short enough to forget

        /// <summary>Called as a smart-stow BEGINS, before anything moves: remember the item leaving
        /// <paramref name="from"/>, plus its thumbnail captured while it is certainly still alive (a
        /// stack merge can destroy the item outright). <see cref="Tick"/> flashes once it lands.</summary>
        public static void PendStow(DynamicThing item, Slot from)
        {
            if (item == null) return;
            Sprite icon = null;
            try { icon = item.GetThumbnail(); } catch { }
            PendStow(item, from, icon);
        }

        private static void PendStow(DynamicThing item, Slot from, Sprite icon)
        {
            if (item == null || icon == null) return;
            for (int i = 0; i < _pending.Count; i++)
                if (ReferenceEquals(_pending[i].Item, item)) { _pending.RemoveAt(i); break; }
            _pending.Add(new Pending { Item = item, Icon = icon, From = from, Posted = Time.unscaledTime });
        }

        // ---- hand watcher: the MOD-AGNOSTIC trigger ----
        //
        // The decisive lesson from four failed attempts: we cannot hook "the stow", because on a real
        // install we may not own it. FlorpyDorp's log shows the Workshop mod InventoryTweaks (v0.4.9)
        // patching InventoryManager.SmartStow with its own HarmonyPrefix and performing the whole
        // move itself ("Smart Stow: Processing item ... / Selected target slot ..."), so vanilla's
        // SmartStow — and therefore its PerformHiddenSlotMoveToAnimation — never runs, and every hook
        // we placed on that path was dead code.
        //
        // So don't hook the ACTION, observe the RESULT: watch both hands every frame. When an item
        // leaves a hand, follow it; if it comes to rest inside a worn container, flash that container's
        // box. This is correct no matter who moved it (us, vanilla, or any other mod) and needs no
        // patch at all. The thumbnail is cached WHILE the item is still held, so a stack-merge that
        // destroys the item on arrival still flashes the right icon.
        private static readonly DynamicThing[] _handItem = new DynamicThing[2];
        private static readonly Sprite[] _handIcon = new Sprite[2];

        private static void WatchHands()
        {
            Human human = null;
            try { human = InventoryManager.ParentHuman; } catch { }
            if (human == null)
            {
                _handItem[0] = _handItem[1] = null;
                _handIcon[0] = _handIcon[1] = null;
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                Slot hand = null;
                try { hand = i == 0 ? human.LeftHandSlot : human.RightHandSlot; } catch { }
                DynamicThing cur = null;
                try { cur = hand != null ? hand.Get() : null; } catch { }

                DynamicThing prev = _handItem[i];
                if (!ReferenceEquals(cur, prev))
                {
                    // Whatever was here just left. Follow it — PendStow ignores anything that does
                    // not come to rest in a worn container, so drops, hand-swaps and world-stows
                    // resolve to "no flash" on their own.
                    if (prev != null && _handIcon[i] != null) PendStow(prev, hand, _handIcon[i]);
                    _handItem[i] = cur;
                    _handIcon[i] = null;
                }
                // Cache the thumbnail while the item is still in hand and certainly alive.
                if (cur != null && _handIcon[i] == null)
                {
                    try { _handIcon[i] = cur.GetThumbnail(); } catch { }
                }
            }
        }

        /// <summary>Once per frame (driven by the HUD): notice anything leaving a hand, then flash any
        /// pending item that has landed somewhere other than the hand it left. Times out so an item
        /// that never came to rest in a worn container is forgotten.</summary>
        public static void Tick()
        {
            WatchHands();
            SweepStale(); // MaxAgeSec must be enforced even when no further stow ever calls Post
            if (_pending.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (now - p.Posted > PendingTimeoutSec) { _pending.RemoveAt(i); continue; }
                Slot landed = null;
                try { landed = p.Item != null ? p.Item.ParentSlot : null; } catch { }
                if (landed == null || ReferenceEquals(landed, p.From)) continue; // still in hand / still in flight
                _pending.RemoveAt(i);
                OnStow(landed, p.Icon, p.Item);
            }
        }

        // ---- one stow, one flash ----
        //
        // A single stow can be reported TWICE: optimistically by the caller that requested the move
        // (SmartStowPlus / ItemActions, at request time) and again by the hand watcher when the item
        // is observed landing. On a multiplayer client OnServer.MoveToSlot is only a request, so the
        // echo can arrive MORE than DurationSec later — by which point Post's live-entry dedupe has
        // already dropped the first flash, and the box would pop a second time out of nowhere.
        //
        // Dedupe on the ITEM INSTANCE rather than on time, so two DIFFERENT items of the same type
        // stowed back to back still flash once each (a time window would swallow the second).
        private struct Flashed { public DynamicThing Item; public float At; }
        private static readonly List<Flashed> _flashed = new List<Flashed>();

        private static bool AlreadyFlashed(DynamicThing item)
        {
            if (item == null) return false;
            float now = Time.unscaledTime;
            bool hit = false;
            for (int i = _flashed.Count - 1; i >= 0; i--)
            {
                if (now - _flashed[i].At > PendingTimeoutSec) { _flashed.RemoveAt(i); continue; }
                if (ReferenceEquals(_flashed[i].Item, item)) hit = true;
            }
            return hit;
        }

        /// <summary>Convenience: resolve the item's thumbnail now, then flash. Capture the sprite
        /// BEFORE a move that could consume the item (a stack merge) and call the Sprite overload.</summary>
        public static void OnStow(Slot dest, DynamicThing item)
        {
            if (item == null) return;
            Sprite icon = null;
            try { icon = item.GetThumbnail(); } catch { }
            OnStow(dest, icon, item);
        }

        /// <summary>The stowed item's thumbnail for <paramref name="slot"/> (or null), plus a
        /// 1→0 <paramref name="progress"/> for the scale pop. The countdown starts on the first
        /// call that returns non-null, so a not-yet-rendered flash never expires unseen.</summary>
        public static Sprite Get(Slot slot, out float progress)
        {
            progress = 0f;
            if (slot == null || _flashes.Count == 0) return null;
            Entry e;
            if (!_flashes.TryGetValue(slot, out e)) return null;
            float now = Time.unscaledTime;
            // Enforce MaxAgeSec HERE, not only in SweepStale (which runs from Post, so with no
            // further stow it never runs at all). Without this a flash posted while the HUD was
            // stood down — vanilla's hide-UI toggle flips InventoryManager.ShowUi, which
            // Guards.CanDraw gates on, and Tick deliberately runs before that gate — would sit
            // un-rendered forever and then pop with a long-stale icon the moment the box returns.
            if (e.StartedAt < 0f && now - e.Posted > MaxAgeSec) { _flashes.Remove(slot); return null; }
            if (e.StartedAt < 0f) { e.StartedAt = now; _flashes[slot] = e; } // first render — start the clock
            float elapsed = now - e.StartedAt;
            if (elapsed >= DurationSec) { _flashes.Remove(slot); return null; }
            progress = 1f - (elapsed / DurationSec);
            return e.Icon;
        }

        public static Sprite Get(Slot slot) { float p; return Get(slot, out p); }

        public static void Reset()
        {
            _flashes.Clear();
            _stale.Clear();
            _pending.Clear();
            _flashed.Clear();
            // Hot-reload: forget what the hands were holding, or the first post-reload frame reads a
            // stale occupant as a "departure" and flashes an item that never moved.
            _handItem[0] = _handItem[1] = null;
            _handIcon[0] = _handIcon[1] = null;
        }

        private static void SweepStale()
        {
            if (_flashes.Count == 0) return;
            float now = Time.unscaledTime;
            _stale.Clear();
            foreach (var kv in _flashes)
                if (now - kv.Value.Posted > MaxAgeSec) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++) _flashes.Remove(_stale[i]);
        }

        /// <summary>Walk the parent-slot chain up to the slot worn directly on the human (the one
        /// our equipment column / hand boxes actually draw). Returns null if the chain doesn't lead
        /// to the local human (e.g. a world container). Depth-guarded against a malformed cycle.
        /// Slot.Parent is the owning Thing (Thing.cs sets slot.Parent = this); DynamicThing.ParentSlot
        /// is the slot that Thing sits in (set on every MoveToSlot).</summary>
        private static Slot ResolveWornSlot(Slot slot, Human human)
        {
            Slot s = slot;
            for (int guard = 0; s != null && guard < 8; guard++)
            {
                if (s.Parent == human) return s;          // s is a body/hand slot on the human
                var dyn = s.Parent as DynamicThing;        // else climb: which slot holds this container?
                s = dyn != null ? dyn.ParentSlot : null;
            }
            return null;
        }
    }
}
