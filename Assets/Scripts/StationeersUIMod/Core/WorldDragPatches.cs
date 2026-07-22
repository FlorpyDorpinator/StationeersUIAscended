using System;
using System.Reflection;
using Assets.Scripts;              // CursorManager, WorldMouseMode
using Assets.Scripts.Objects;
using Assets.Scripts.UI;           // InputMouse
using HarmonyLib;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Lets a VANILLA mouse drag that started in the world finish on a UIA visor box — the inbound
    /// half of HUD drag-and-drop (the outbound half is <see cref="HudSlotDrag"/>).
    ///
    /// WHY A PATCH AND NOT A POLLER. Vanilla resolves a drag's destination from exactly one UI
    /// source: <c>((CurrentSlotButton != null) ? CurrentSlotButton.Slot : null) ?? InputMouse.WorldSlot</c>
    /// (InputMouse.cs:518 / :547). <c>CurrentSlotButton</c> is <c>SlotDisplayButton.CurrentSlot</c>,
    /// and the mod deactivates the hand/clothing panels that own those buttons — so over a visor box
    /// the destination is null, <c>IsValid</c> returns <c>DragResult.Drop</c>, and the switches in
    /// <c>Drag()</c>/<c>DragSlot()</c> have NO <c>case DragResult.Drop</c>. The gesture completes and
    /// nothing is mutated: a silent no-op by construction.
    ///
    /// Merely SUPPLYING a destination is not enough, and this is the load-bearing reason the prefix
    /// returns false: while a drag is live, <c>HandleSlotDisplay</c> refreshes
    /// <c>InputMouse.WorldSlot = GetHoverWorldSlot()</c> every frame (InputMouse.cs:603), and that is
    /// a PHYSICS raycast which passes straight THROUGH the visor boxes (they are raycastTarget=false)
    /// into the world behind them. A locker or crate sitting behind the hand box within reach would
    /// therefore receive a real move on the SAME release we handle — two mutations from one press.
    /// Suppressing vanilla's own resolution is what makes this exactly one message.
    ///
    /// TWO SOURCES, TWO LADDERS. <c>Drag()</c> carries a free-lying world item (ParentSlot == null) →
    /// <see cref="ItemActions.WorldDragTo"/>. <c>DragSlot()</c> carries an item taken from a world
    /// container's slot, whose <c>ParentSlot</c> is ALWAYS non-null (InputMouse.cs:492) → the existing
    /// <see cref="ItemActions.DragTo"/>, which already implements that ladder. Routing both to the
    /// world resolver would hard-fail every DragSlot release AND suppress vanilla's handling of it,
    /// which is strictly worse than doing nothing (2026-07-20 review).
    ///
    /// Registered through <see cref="PatchHarness.TryPatchAll"/>: these are PRIVATE vanilla methods,
    /// so a rename in a game update degrades only this feature instead of failing the mod.
    /// </summary>
    internal static class WorldDrag
    {
        // InputMouse.ClearTooltip is private and is called at the top of both Drag() and DragSlot()
        // (:513 / :542) before their mouse-up check. Since our prefix suppresses the original, we
        // must run it ourselves or a tooltip lingers after the drop. Resolved once, fail-soft.
        private static MethodInfo _clearTooltip;
        private static bool _clearTooltipResolved;
        private static int _lastRouteFrame = -999;   // continuity check for the stale-gesture heal

        /// <summary>Vanilla's own resolve gate is <c>KeyManager.GetMouseUp("Primary")</c> — a REBINDABLE
        /// action (KeyMap.PrimaryAction). Raw <c>Input.GetMouseButtonUp(0)</c> would disagree with it in
        /// both directions the moment a player rebinds primary action, so match vanilla and only fall
        /// back to the raw button if the lookup is unavailable.</summary>
        private static bool MouseUp()
        {
            try { return KeyManager.GetMouseUp("Primary"); }
            catch { return Input.GetMouseButtonUp(0); }
        }

        /// <summary>True when the release should be routed to a UIA box. Only ever true on the frame
        /// the button is released, and only when vanilla itself has no legitimate destination.</summary>
        private static bool WantsRelease(InputMouse mouse, out Slot dest)
        {
            dest = null;
            if (mouse == null) return false;
            if (!MouseUp()) return false;                            // Drag() runs every frame
            if (!HudSlotDrag.GateOpenForWorldDrop()) return false;   // one shared gate

            // ONE cross-surface resolver, shared with the outbound HUD release and the grid drag-out,
            // so every direction agrees about who owns a pixel (vanilla slot > grid cell > HUD box).
            // The grid-cell rung is what lets a world item land in a grid cell instead of a HUD box
            // behind it — or worse, falling through to vanilla's through-the-panel physics raycast,
            // the exact wrong-destination class this patch exists to prevent (2026-07-20 review).
            DropResolution r = DropResolver.Resolve();

            // A REAL vanilla slot button (an open bag / inventory window the mod never hides) is
            // vanilla's own to resolve — stay entirely out of the way, exactly as before.
            if (r.Surface == DropSurface.VanillaSlot) return false;

            // The 2026-07-20 false-drop guard, now applied HERE (narrowly) instead of being baked into
            // box geometry. DropResolver's HUD-box rung (HudSystem.ZoneAt) is now decoupled from the
            // alpha/dropout availability gate, so it reports a box wherever the boxes RENDER — faded or
            // not. But an inbound WORLD drag onto an OCCUPIED box faded under 0.5 (root/panel low-power
            // dropout) would swap the held tool the player forgot was there out to the floor, from a box
            // they cannot see. Re-decline the HUD-box rung when the availability gate is shut; a grid
            // cell or vanilla slot lives in a real window and is unaffected. Declining (return false)
            // lets vanilla resolve the release exactly as it did before ZoneAt was decoupled.
            if (r.Surface == DropSurface.HudZone && !HudSystem.ZonesAvailable()) return false;

            // A grid cell or a (visible) HUD box: ours to route. Carry the slot out to Route.
            if (r.HasSlot) { dest = r.Slot; return true; }

            // No slot, but another UI owns this pixel (Control Center chrome, a window's padding):
            // claim the release and mutate nothing, so vanilla cannot resolve it through the box either.
            return r.Surface == DropSurface.OtherUi;
        }

        /// <summary>Reproduce the teardown the suppressed original would have done. Drag() hides the
        /// drag display AND the world selection (InputMouse.cs:534-535); DragSlot() hides only the
        /// drag display (:570). Both clear the tooltip first.</summary>
        private static void Teardown(InputMouse mouse, bool alsoSelection)
        {
            if (!_clearTooltipResolved)
            {
                _clearTooltipResolved = true;
                try { _clearTooltip = AccessTools.Method(typeof(InputMouse), "ClearTooltip"); }
                catch { _clearTooltip = null; }
            }
            if (_clearTooltip != null)
                try { _clearTooltip.Invoke(mouse, null); } catch { }

            try { mouse.WorldMode = WorldMouseMode.Idle; } catch { }
            try { if (mouse.DragSlotDisplay != null) mouse.DragSlotDisplay.SetVisible(false); } catch { }
            if (alsoSelection)
                try { CursorManager.SetSelectionVisibility(false); } catch { }
        }

        /// <summary>Free-lying world item → a UIA box (the <c>Drag()</c> path).</summary>
        internal static bool TryLooseItem(InputMouse mouse) => Route(mouse, worldSourced: true);

        /// <summary>Item pulled from a world container's slot → a UIA box (the <c>DragSlot()</c> path).
        /// Its ParentSlot is always set, so it reuses the ALREADY-REVIEWED slot-to-slot ladder.</summary>
        internal static bool TrySlotItem(InputMouse mouse) => Route(mouse, worldSourced: false);

        /// <summary>
        /// Returns TRUE when we own this call and vanilla's original must be skipped.
        ///
        /// TWO OWNERSHIP RULES, both learned the hard way (2026-07-20 review):
        ///
        /// 1. If the OUTBOUND driver is dragging, vanilla armed a world drag on the very same press —
        ///    its <c>IsMouseOverUi</c> is false over our raycast-transparent boxes — and letting the
        ///    original run would resolve <c>InputMouse.WorldSlot</c>, a physics raycast THROUGH the
        ///    box, into whatever container sits behind it. That is two mutations from one press.
        ///    So we eject vanilla from the gesture immediately (teardown + suppress). Deferring to
        ///    vanilla here, as the first cut did, was exactly backwards.
        ///
        /// 2. Once <see cref="WantsRelease"/> claims a release, we NEVER hand it back — not on a
        ///    missing item, not on a wrong source, not on an exception after teardown. Vanilla's only
        ///    fallback for that release is the same through-the-box raycast, so returning false late
        ///    reintroduces the very double-resolution this patch exists to prevent.
        /// </summary>
        private static bool Route(InputMouse mouse, bool worldSourced)
        {
            bool owned = false;
            try
            {
                if (mouse == null) return false;

                // STALE-GESTURE HEAL. This prefix only runs while vanilla dispatches Drag()/DragSlot().
                // Other surfaces (The Grid, the Control Center, ModalScope) park
                // CursorManager.BlockCursorRaycast, which makes InputMouse.Update early-return — so a
                // drag interrupted that way is NEVER resolved, and vanilla resumes later with WorldMode
                // still Drag and CursorItem still pinned. Claiming that release would teleport an
                // abandoned item into a slot on a totally unrelated click. If we were not called on the
                // previous frame AND the button is not currently held, the gesture is over: tidy
                // vanilla's state, claim the call so it cannot resolve a stale drag, and mutate nothing.
                int frame = Time.frameCount;
                bool continuous = frame - _lastRouteFrame <= 1;
                _lastRouteFrame = frame;
                if (!continuous && !Input.GetMouseButton(0))
                {
                    Teardown(mouse, alsoSelection: worldSourced);
                    return true;
                }

                // Rule 1: our own drag owns this press.
                if (HudSlotDrag.IsDragging || Time.frameCount == HudSlotDrag.LastDragEndFrame)
                {
                    Teardown(mouse, alsoSelection: worldSourced);
                    return true;
                }

                Slot dest;
                if (!WantsRelease(mouse, out dest)) return false;   // not ours: vanilla runs untouched

                // Rule 2: from here the release is ours, whatever happens next.
                owned = true;
                Teardown(mouse, alsoSelection: worldSourced);

                DynamicThing item = mouse.CursorItem;
                if (item == null || dest == null) return true;   // claimed; nothing to move

                if (worldSourced)
                {
                    // A loose world thing has no parent slot; if it somehow does, the slot ladder
                    // below owns it and this path must not touch it.
                    if (item.ParentSlot == null) ItemActions.WorldDragTo(item, dest);
                }
                else
                {
                    Slot src = item.ParentSlot;
                    if (src != null)
                        // Expected = the item VANILLA grabbed (frozen at drag-arm time), NOT a fresh
                        // read of the slot. Calling .Pin() here would set Expected from src.Get()
                        // microseconds before DragTo compares item != Expected against that same
                        // read — a tautology that can never fail, silently voiding DragTo's
                        // documented staleness guard. With the real item pinned, a teammate swapping
                        // the slot's contents mid-drag correctly fails instead of moving whatever
                        // landed there (the server validates nothing, so this is our only check).
                        ItemActions.DragTo(
                            new ScannedSlot { Slot = src, Holder = src.Parent, Expected = item }, dest);
                }
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("World drag route failed: " + e.Message);
                return owned;   // torn down already => stay handled; never resurrect vanilla's resolve
            }
        }
    }

    // Priority.First is deliberate. Our return-false is load-bearing (it is what prevents vanilla
    // resolving the drop through the raycast-transparent HUD box into a container behind it), and
    // Harmony gates every prefix after the first bool-returning one behind runOriginal. At default
    // priority, inter-mod ordering falls to load order: another mod sorting ahead of us and returning
    // false would mean our Teardown never runs, leaving WorldMode pinned at Drag forever — vanilla's
    // Idle() is then gated off permanently and CursorItem never clears. Claim the ordering explicitly.
    [HarmonyPatch(typeof(InputMouse), "Drag")]
    internal static class Patch_InputMouse_Drag
    {
        // false = we resolved this release onto a UIA box; true = vanilla runs untouched.
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(InputMouse __instance) => !WorldDrag.TryLooseItem(__instance);
    }

    [HarmonyPatch(typeof(InputMouse), "DragSlot")]
    internal static class Patch_InputMouse_DragSlot
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(InputMouse __instance) => !WorldDrag.TrySlotItem(__instance);
    }
}
