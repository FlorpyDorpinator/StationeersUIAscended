namespace StationeersUIMod.Core
{
    /// <summary>
    /// Author mode, for FlorpyDorp. ON means the shipped-theme read-only gate in
    /// <c>HudProfileStore</c> stands down for THIS session, so the UI themes the mod SHIPS can be
    /// edited in place (F9 autosave, rename, the property popups) instead of only through a
    /// duplicate. Toggled by the <c>uiadev</c> console command (see <c>FinderCommands</c>); OFF for
    /// everybody else, which is what makes a shipped theme read-only for a player.
    ///
    /// <para>SESSION-ONLY ON PURPOSE — that is the design, not an oversight, and it is why this is a
    /// plain static rather than a config entry. A static dies with the assembly on an F6 hot reload
    /// and on every restart, so the unlock can never be persisted, never lands in a .cfg a player
    /// could copy from a forum post, and can never be switched on once and forgotten. A player who
    /// finds the command in a log or on a stream therefore cannot permanently wedge themselves into
    /// editing shipped files whose edits an update then fights: their copy would hash as "player
    /// edited", freeze itself out of shipped refreshes forever (see HudProfileStore.SyncShipped) and
    /// quietly diverge from the theme everyone else is looking at. Worst case they unlock one
    /// session, and their edits stop saving again the moment they restart — recoverable by
    /// construction.</para>
    ///
    /// <para>Deliberately has NO teardown reset and is registered in no Shutdown path: the field IS
    /// the session. A hot reload swaps in a fresh assembly whose copy starts false on its own, which
    /// is exactly the wanted behaviour (re-arm it with <c>uiadev</c> after an F6).</para>
    /// </summary>
    internal static class UiaDevMode
    {
        /// <summary>True while <c>uiadev</c> has unlocked shipped-theme editing for this session.</summary>
        internal static bool Active;
    }
}
