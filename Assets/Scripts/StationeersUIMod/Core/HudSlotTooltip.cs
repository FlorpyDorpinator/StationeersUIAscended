namespace StationeersUIMod.Core
{
    /// <summary>
    /// Shows the vanilla ITEM tooltip when the cursor hovers a HUD slot box that holds an item — a
    /// hand box or a worn 1-6 equipment box. Same <see cref="Assets.Scripts.UI.PanelToolTip"/> the
    /// Grid cells and the vanilla inventory use.
    ///
    /// HUD widgets are raycast-transparent renderers (no EventSystem hover), so this POLLS
    /// <see cref="UI.Hud.HudSystem.ZoneAt"/> — the same inverse-warped hit-test the drag layer and the
    /// equipment click-to-pin already trust — rather than an IPointer handler. Ticked once per frame
    /// from <c>HudSystem.Update</c>.
    ///
    /// Ownership is tracked (<see cref="_showing"/>): this NEVER clears a tooltip it did not itself
    /// raise, so it can coexist with the vitals card's stats tooltip (a different owner of the same
    /// singleton) without the two stomping each other.
    /// </summary>
    internal static class HudSlotTooltip
    {
        private static bool _showing;     // WE are currently showing a slot-item tooltip
        private static object _lastOcc;   // occupant we last showed (identity), to avoid per-frame re-set

        /// <summary>Per-frame driver. Cheap on the steady path: the gate short-circuits before the
        /// (allocating) ZoneAt hit-test unless the cursor is actually free over the live HUD.</summary>
        internal static void Tick()
        {
            try
            {
                if (!Allowed()) { ClearIfOurs(); return; }

                var zone = UI.Hud.HudSystem.ZoneAt();
                var slot = zone != null ? zone.Slot : null;
                Assets.Scripts.Objects.DynamicThing occ = null;
                try { occ = slot != null ? slot.Get() : null; } catch { occ = null; }
                if (occ == null) { ClearIfOurs(); return; }

                if (_showing && ReferenceEquals(occ, _lastOcc)) return; // steady state, nothing to do
                _showing = true;
                _lastOcc = occ;
                VanillaTooltip.Show(occ);
            }
            catch { }
        }

        private static void ClearIfOurs()
        {
            if (!_showing) return;   // never clear a tooltip we did not raise (e.g. the vitals stats one)
            _showing = false;
            _lastOcc = null;
            VanillaTooltip.Clear();
        }

        /// <summary>Gate: a free cursor over the live HUD with nothing else owning input. Mirrors the
        /// vitals card's own show-gate so the two tooltips never fire at cross purposes.</summary>
        private static bool Allowed()
        {
            try
            {
                if (!Assets.Scripts.Serialization.Settings.CurrentData.ShowSlotToolTips) return false;
                if (!UI.Hud.HudSlotDrag.IsCursorFree) return false;      // also covers "cursor visible + in game"
                if (UI.Hud.HudSlotDrag.IsDragging) return false;
                if (Features.RadialController.AnyRadialOpen) return false;
                if (Windows.HudEditorMode.Active || Windows.RadialEditorMode.Active) return false;
                if (UI.Menu.UiaControlCenter.IsOpen || UI.Grid.TheGridPanel.IsOpen) return false;
                if (Guards.VanillaMenuWantsFront()) return false;
                if (ImGuiNET.ImGui.GetIO().WantCaptureMouse) return false;
                var cursor = Assets.Scripts.CursorManager.Instance;
                if (cursor != null && cursor.BlockCursorRaycast) return false;
                if (UI.Hud.HudSlotDrag.PointerOverOtherUiPublic()) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Hot-reload/teardown/world-unload: drop our tooltip and forget state (statics must
        /// never survive an F6 pointing at a dead occupant). UNCONDITIONAL — not gated on _showing:
        /// the whole point of the release is to heal the shared-raycaster leak even when ownership
        /// tracking was lost (see VanillaTooltip.ForceRelease). Idempotent and cheap, so calling it
        /// on paths where no tooltip was up costs one null check.</summary>
        internal static void Reset()
        {
            try { VanillaTooltip.ForceRelease(); } catch { }
            _showing = false;
            _lastOcc = null;
        }

        /// <summary>Release ONLY if we are the one showing. For transient per-frame paths (an
        /// invalid sampler frame during a disconnect) where an unconditional ClearToolTip would
        /// stomp a legitimate vanilla tooltip that happens to be up.</summary>
        internal static void ReleaseIfOurs()
        {
            if (_showing) Reset();
        }
    }
}
