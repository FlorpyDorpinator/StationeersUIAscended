using System.Collections.Generic;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Undo/redo for the F9 HUD editor's <see cref="HudDocument"/>, mirroring
    /// <see cref="HudPalette.History"/>'s exchange semantics: one edit gesture is one step (the
    /// editor snapshots the document on widget pickup and commits on release). The two stacks
    /// hold whole-document clones, and Undo/Redo swap the "current" document across them so a
    /// step is perfectly reversible.
    ///
    /// Static because there is exactly one live HUD document being edited at a time; a static
    /// stack lets any editor panel reach the same history without threading a reference through.
    /// </summary>
    public static class HudDocumentHistory
    {
        /// <summary>Depth cap — the same 50 as the palette history. Snapshots are cheap
        /// field-copy clones (see <see cref="HudDocument.Clone"/>), but an unbounded stack over a
        /// long editing session is still needless memory, so the oldest step is dropped past Cap.</summary>
        private const int Cap = 50;

        private static readonly List<HudDocument> _undo = new List<HudDocument>();
        private static readonly List<HudDocument> _redo = new List<HudDocument>();

        public static bool CanUndo => _undo.Count > 0;
        public static bool CanRedo => _redo.Count > 0;

        /// <summary>
        /// Record a pre-edit snapshot so the gesture that is about to mutate the document can be
        /// undone. CONTRACT: the caller passes a CLONE it took BEFORE mutating (i.e. doc.Clone());
        /// Push stores that reference AS-IS and does NOT clone again — pushing the live document
        /// would let subsequent edits corrupt the history entry. Pushing a new step clears the
        /// redo stack (the classic branch-abandon) and trims the oldest entry past Cap.
        /// </summary>
        public static void Push(HudDocument preEditSnapshot)
        {
            if (preEditSnapshot == null) return;
            _undo.Add(preEditSnapshot);
            if (_undo.Count > Cap) _undo.RemoveAt(0);
            _redo.Clear();
        }

        /// <summary>Pop the last undo step and return the document to activate, pushing a clone of
        /// <paramref name="current"/> onto the redo stack so the move is reversible. Returns null
        /// when <see cref="CanUndo"/> is false, so the caller keeps its current document.</summary>
        public static HudDocument Undo(HudDocument current)
        {
            if (_undo.Count == 0) return null;
            var restore = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            if (current != null) _redo.Add(current.Clone());
            return restore;
        }

        /// <summary>Symmetric partner to <see cref="Undo"/>: pop the last redo step, push a clone
        /// of <paramref name="current"/> back onto undo, and return the document to activate. Null
        /// when <see cref="CanRedo"/> is false.</summary>
        public static HudDocument Redo(HudDocument current)
        {
            if (_redo.Count == 0) return null;
            var restore = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            if (current != null) _undo.Add(current.Clone());
            return restore;
        }

        /// <summary>Drop all history. Called on hot-reload and profile switch, where the old
        /// document's steps are meaningless against the freshly loaded one.</summary>
        public static void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
