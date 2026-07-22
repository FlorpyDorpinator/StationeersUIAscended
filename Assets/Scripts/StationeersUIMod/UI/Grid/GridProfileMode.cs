namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// PROFILE MODE state for the Universal Inventory (SmartStow rework, design O4a). One static
    /// flag + a monotonic version, nothing else: entering/leaving the mode (the header tag button,
    /// Esc, closing the window) and every profile-data edit made through the Grid UI (assign via
    /// the chip popup, a capture applied, a drag-to-pin rule) bump <see cref="Version"/>, and the
    /// consumers diff it exactly like <c>GridPinStore.PinVersion</c>:
    /// <list type="bullet">
    /// <item><c>TheGridPanel.Tick</c> diffs <see cref="ChromeStamp"/> and REBUILDS the tree — a
    /// re-Bind is what creates/removes the per-region profile strip and the tab badges.</item>
    /// <item><c>PinnedInventoryWindow.TickAll</c> diffs it too and re-binds each live pin, so the
    /// strip appears/disappears in pinned windows even though they outlive the main rebuild.</item>
    /// <item><c>BagGridCell</c> reads <see cref="Active"/> directly per frame for the cell DIM (a
    /// static bool read — free), so the dim needs no rebuild at all.</item>
    /// </list>
    /// The mode NEVER mutates game state: everything reachable from it writes profile/assignment
    /// XML through <c>BagProfileStore</c>/<c>ProfileCapture</c> only. No radial is involved
    /// anywhere in this feature (FlorpyDorp Q1). Hot-reload: <see cref="Reset"/> is called from
    /// <c>TheGridPanel.Shutdown</c>, and the mode also exits on every window close
    /// (<c>TheGridPanel.Hide</c>), so a stale "editing" state can never survive.
    /// </summary>
    public static class GridProfileMode
    {
        private static bool _active;
        private static int _version;

        /// <summary>True while the Universal Inventory is in profile mode: cells dim, every bag
        /// region shows its chip + CAPTURE strip, and dropping a dragged item on a manila tab pins
        /// an item rule instead of doing nothing.</summary>
        public static bool Active { get { return _active; } }

        /// <summary>Monotonic change counter: moves on every mode flip and on every profile-data
        /// edit made through the Grid UI. Consumers diff it (never subscribe).</summary>
        public static int Version { get { return _version; } }

        /// <summary>Flip the mode (the header tag button).</summary>
        public static void Toggle()
        {
            SetActive(!_active);
        }

        /// <summary>Enter/exit profile mode. Idempotent; a real flip bumps <see cref="Version"/>
        /// so the panel and every pinned window rebuild their profile chrome.</summary>
        public static void SetActive(bool on)
        {
            if (_active == on) return;
            _active = on;
            unchecked { _version++; }
        }

        /// <summary>Signal "profile data changed" (an assignment, a capture, a pinned rule) so
        /// chips, badges and strips refresh on the next Tick without a structural game change.</summary>
        public static void BumpVersion()
        {
            unchecked { _version++; }
        }

        /// <summary>The value the chrome consumers actually diff: the version PLUS the badge
        /// config bit, so toggling <c>UIAConfig.GridProfileBadges</c> live also reads as a chrome
        /// change (badges are created/removed by the same re-Bind).</summary>
        public static int ChromeStamp()
        {
            bool badges = true;
            try
            {
                badges = UIAConfig.GridProfileBadges == null || UIAConfig.GridProfileBadges.Value;
            }
            catch { }
            unchecked { return _version * 2 + (badges ? 1 : 0); }
        }

        /// <summary>Hot-reload teardown (from <c>TheGridPanel.Shutdown</c>): drop the mode and the
        /// counter so a double-F6 starts clean.</summary>
        public static void Reset()
        {
            _active = false;
            _version = 0;
        }
    }
}
