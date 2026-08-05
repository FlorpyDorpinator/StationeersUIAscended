using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Keeps the vanilla item tooltip (<see cref="Assets.Scripts.UI.PanelToolTip"/>) sorted ABOVE the
    /// mod's own UGUI canvases (The Grid 5020, pinned windows 5030, Control Center 5200), so a tooltip
    /// raised over a mod window is never hidden behind it.
    ///
    /// <para>The trap (play-test 2026-07-24). In this game build the tooltip's text draws directly under
    /// a SHARED root canvas — <c>PopupsCanvas</c> (order 101) — NOT a private canvas of its own. That
    /// same shared canvas also hosts vanilla popups such as the leave-session <c>ConfirmationPanel</c>.
    /// The old code lifted whatever <c>Graphic.canvas</c> resolved to, so it stamped the WHOLE shared
    /// PopupsCanvas to 6000 and reordered that dialog against the pause menu (it fell behind and could
    /// not be clicked). So: when the resolved canvas is a shared ROOT we NEVER sort it — we isolate the
    /// tooltip's own subtree in a nested <c>overrideSorting</c> canvas instead, and heal any earlier
    /// raise of the shared canvas back to its vanilla order. A genuinely tooltip-private nested canvas
    /// (non-root) is still lifted directly, as before.</para>
    /// </summary>
    internal static class VanillaTooltip
    {
        // Above every mod canvas (the highest is the Control Center at 5200) with headroom.
        private const int SortOrder = 6000;

        // The vanilla default order of the shared PopupsCanvas (observed in the play-test log before the
        // old code raised it). Only ever written back onto a shared root sitting at OUR 6000 fingerprint,
        // so this never stomps a genuine vanilla value.
        private const int VanillaSharedOrder = 101;

        // One-shot diagnostic latch (resets on hot-reload): logs the tooltip canvas we found the
        // first time we lift it, so a "still behind the window" report tells us the canvas's render
        // mode + order rather than guessing.
        private static bool _logged;

        /// <summary>Show the vanilla tooltip for an item, lifted above the mod's UI. Fail-soft: the
        /// PanelToolTip singleton is world-only and may not exist yet.</summary>
        internal static void Show(Assets.Scripts.Objects.Thing thing)
        {
            var tip = Assets.Scripts.UI.PanelToolTip.Instance;
            if (tip == null || thing == null) return;
            Lift(tip);
            // Detach any screen-space receiver (e.g. the vitals card's) first. SetUpTooltip(Thing)
            // does NOT touch _tooltipToUpdate, so a stale receiver left over from a stats tooltip
            // would make vanilla's LateUpdate clear THIS item tooltip every frame (its
            // TooltipIsVisible is false while we're over a slot, not the card).
            DetachReceiver(tip);
            try { tip.SetUpTooltip(thing); } catch { }
        }

        private static System.Reflection.FieldInfo _receiverField;

        /// <summary>Null out the protected <c>_tooltipToUpdate</c> on the tooltip so an item tooltip
        /// is not governed by a leftover screen-space receiver. Cached reflection; fail-soft.</summary>
        private static void DetachReceiver(Assets.Scripts.UI.PanelToolTip tip)
        {
            try
            {
                if (_receiverField == null)
                    _receiverField = typeof(Assets.Scripts.UI.PanelToolTipScreenSpace)
                        .GetField("_tooltipToUpdate", System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic);
                if (_receiverField != null) _receiverField.SetValue(tip, null);
            }
            catch { }
        }

        /// <summary>Hide the vanilla tooltip. Null-safe.</summary>
        internal static void Clear()
        {
            var tip = Assets.Scripts.UI.PanelToolTip.Instance;
            if (tip == null) return;
            try { tip.ClearToolTip(); } catch { }
        }

        /// <summary>Hide the vanilla tooltip AND force its shared raycaster back on.
        ///
        /// WHY THIS EXISTS (Ningy's "Exit To Desktop dialog non-interactive", 2026-08-04): vanilla's
        /// SetUpTooltip DISABLES a shared GraphicRaycaster (PanelToolTipScreenSpace.GraphicRayCast —
        /// wired to the persistent alert layer, see the 2026-07-24 report) and the ONLY vanilla path
        /// that re-enables it is ClearToolTip (PanelToolTipScreenSpace.cs:25). A tooltip we raised
        /// and never cleared therefore leaves that raycaster dead for the rest of the process — at
        /// the main menu the exit-confirm dialog renders on that layer but takes no clicks, which
        /// fall through to the menu behind it. This release is called from every teardown path, and
        /// it re-enables the raycaster EXPLICITLY as well as via ClearToolTip, so it heals even when
        /// ownership tracking was lost (a pooled cell hidden mid-hover, a state we never saw).</summary>
        internal static void ForceRelease()
        {
            var tip = Assets.Scripts.UI.PanelToolTip.Instance;
            if (tip == null) return;
            try { tip.ClearToolTip(); } catch { }
            try { if (tip.GraphicRayCast != null) tip.GraphicRayCast.enabled = true; } catch { }
        }

        /// <summary>Lift the tooltip's own canvas above the mod. Idempotent — only writes when the
        /// order is wrong, so it is free on the steady path. Call it before showing a tooltip from any
        /// mod surface (the grid cells, the vitals panel, …); the raise persists for the session, so
        /// even a path that forgets to call it inherits the fix once any other path has run.</summary>
        internal static void Lift(Assets.Scripts.UI.PanelToolTip tip)
        {
            if (tip == null) return;
            try
            {
                // The canvas the tooltip's text actually draws under. On this build that resolves to the
                // SHARED root PopupsCanvas (the tooltip has no private canvas), which we must not sort.
                Canvas c = tip.ToolTipItemName != null ? tip.ToolTipItemName.canvas : null;
                if (c == null && tip.Information != null) c = tip.Information.canvas;
                if (c == null) c = tip.GetComponentInParent<Canvas>();
                if (c == null) { if (!_logged) { _logged = true; UIALog.Info("VanillaTooltip.Lift: no Canvas found on the tooltip."); } return; }

                if (!_logged)
                {
                    _logged = true;
                    var root = c.rootCanvas;
                    UIALog.Info("VanillaTooltip.Lift: graphic canvas '" + c.name + "' mode=" + c.renderMode
                        + " order=" + c.sortingOrder + " overrideSorting=" + c.overrideSorting
                        + " isRoot=" + c.isRootCanvas + " | root '" + (root != null ? root.name : "?")
                        + "' order=" + (root != null ? root.sortingOrder : -1));
                }

                if (c.isRootCanvas)
                {
                    // SHARED vanilla root (PopupsCanvas / AlertCanvas — also hosts the leave-session
                    // ConfirmationPanel and other dialogs). NEVER sort it. Undo any earlier raise this
                    // session (only when it is at OUR fingerprint 6000), then isolate the tooltip alone.
                    if (c.sortingOrder == SortOrder) c.sortingOrder = VanillaSharedOrder;
                    IsolateTooltip(tip, c);
                    return;
                }

                // A genuinely tooltip-private nested canvas: safe to lift it directly (original path).
                if (!c.overrideSorting) c.overrideSorting = true;
                if (c.sortingOrder != SortOrder) c.sortingOrder = SortOrder;
            }
            catch { }
        }

        /// <summary>Wrap the tooltip's OWN subtree (its panel + all its text) in a nested
        /// <c>overrideSorting</c> canvas at <see cref="SortOrder"/>, so only the tooltip lifts above the
        /// mod while the shared root — and every dialog on it — is left exactly where vanilla put it.
        /// Anchor = the direct child of the shared root that contains the tooltip, never the root itself.
        /// Idempotent: a re-used tooltip already carries the canvas, so this just re-affirms the order.</summary>
        private static void IsolateTooltip(Assets.Scripts.UI.PanelToolTip tip, Canvas sharedRoot)
        {
            Transform anchor = tip.transform;
            while (anchor.parent != null && anchor.parent != sharedRoot.transform)
                anchor = anchor.parent;
            if (anchor == sharedRoot.transform) return; // degenerate: the tooltip IS the root — cannot isolate

            var go = anchor.gameObject;
            var cv = go.GetComponent<Canvas>();
            if (cv == null) cv = go.AddComponent<Canvas>();
            if (!cv.overrideSorting) cv.overrideSorting = true;
            if (cv.sortingOrder != SortOrder) cv.sortingOrder = SortOrder;
        }
    }
}
