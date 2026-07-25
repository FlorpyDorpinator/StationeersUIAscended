using System.Collections.Generic;
using Assets.Scripts;   // CursorManager

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The single writer of <see cref="CursorManager.BlockCursorRaycast"/>.
    ///
    /// <para>WHY THIS EXISTS. Eight independent UIA surfaces each need vanilla's world-pick raycast
    /// parked while they own the pointer — the radial (<see cref="ModalScope"/>), The Grid, every
    /// pinned window, a HUD hand-box drag, the capture panel, the profile popup, the Control Center,
    /// and the F9 HUD editor. Before this, each one wrote the SHARED boolean directly off its OWN
    /// private "did I set it" flag. So whenever two overlapped — a right-click item radial opened
    /// over The Grid, say — the FIRST to release wrote <c>false</c> and stomped the other, which still
    /// wanted it held. The Grid worked around this by RE-ASSERTING the flag every single frame it was
    /// hovered (an entire per-frame write whose only job was to undo <see cref="ModalScope"/>'s
    /// deferred close), and even that left one-frame gaps where the world raycast flickered back on.
    /// Eight uncoordinated writers on one global bool is a tug-of-war no point-fix can win.</para>
    ///
    /// <para>THE FIX. Every surface now registers a NAMED hold instead of writing the flag. The flag is
    /// <c>true</c> exactly while at least one hold is live and is written only on the true edges — the
    /// first hold (0→1) drives it on, the last release (1→0) drives it off. One owner's release can
    /// never lower the flag while another still holds it, so the stomp is impossible by construction
    /// and no defensive per-frame re-assert is needed. Holds are idempotent (a surface may call
    /// <see cref="Hold"/> every frame it is hovered — same string, no extra write).</para>
    ///
    /// <para>EDGE-DRIVEN, not asserted. We write the flag only when OUR live-hold set crosses empty,
    /// exactly as the old per-writer code wrote only on its own edges — so this never fights a
    /// non-UIA writer (CinematicCamera, RocketCanvas) that parks the raycast while we hold nothing.
    /// <see cref="Shutdown"/> force-clears to <c>false</c> so a stale hold from a hot-reloaded
    /// assembly can never leave the world raycast frozen (CLAUDE.md static-reset rule).</para>
    /// </summary>
    internal static class CursorBlockArbiter
    {
        private static readonly HashSet<string> _holders = new HashSet<string>();

        /// <summary>Register (or re-affirm) a named hold. Idempotent: calling every frame with the same
        /// id costs one HashSet probe and writes the flag only on the empty→non-empty edge.</summary>
        public static void Hold(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            bool wasEmpty = _holders.Count == 0;
            if (!_holders.Add(id)) return;      // this id already holds — no state change
            if (wasEmpty) Apply(true);          // 0 → 1: drive the flag on
            CursorDiag.NoteBlockHolders("hold", id, HoldersDebug);
        }

        /// <summary>Release a named hold. The flag falls only when the LAST holder leaves, so a release
        /// can never stomp another owner that still wants the raycast parked.</summary>
        public static void Release(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_holders.Remove(id)) return;   // this id was not holding — no state change
            if (_holders.Count == 0) Apply(false);   // 1 → 0: drive the flag off
            CursorDiag.NoteBlockHolders("release", id, HoldersDebug);
        }

        /// <summary>True while any surface holds the raycast block.</summary>
        public static bool AnyHold => _holders.Count > 0;

        /// <summary>Comma-joined live holder ids (or "-"), for the diagnostic trace.</summary>
        public static string HoldersDebug => _holders.Count == 0 ? "-" : string.Join(",", _holders);

        private static void Apply(bool block)
        {
            try
            {
                var cm = CursorManager.Instance;
                if (cm != null) cm.BlockCursorRaycast = block;
            }
            catch { /* fail soft: a missing CursorManager just means we're not in-world */ }
        }

        /// <summary>Hot-reload / teardown: drop every hold and force the flag OFF, so a stale hold from
        /// the dead assembly can never leave vanilla's world raycast frozen after an F6 reload.</summary>
        public static void Shutdown()
        {
            _holders.Clear();
            Apply(false);
        }
    }
}
