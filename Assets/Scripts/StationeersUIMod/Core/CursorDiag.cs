using System.Reflection;
using System.Text;
using Assets.Scripts;       // CursorManager, MouseModeController
using Assets.Scripts.UI;    // InputMouse
using StationeersUIMod.Features;   // RadialController
using StationeersUIMod.UI.Hud;     // HudSlotDrag
using UnityEngine;
using UnityEngine.EventSystems;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Opt-in per-frame tracer for the cursor / drag subsystem. OFF by default (one bool test per
    /// frame when off, zero allocations). Toggle in-game with the <c>uiadiag</c> console command.
    ///
    /// <para>WHY. The "cursor flickers when I open a radial and move to a wedge" bug is intermittent
    /// and order-dependent — it cannot be reproduced or root-caused from source alone, and several
    /// blind fixes have missed. When enabled, this samples every cursor- and drag-related value each
    /// frame and writes ONE BepInEx log line whenever any of them CHANGES, stamped with the frame
    /// number and the mouse position. An intermittent flicker therefore leaves an exact transition
    /// trail — e.g. <c>block T-&gt;F</c> or <c>vis T-&gt;F</c> on the very frames the pointer crosses a
    /// wedge — and the live cursor-block holder set names WHO drove the change. Read the log, fix with
    /// certainty (the discipline FlorpyDorp asked for: see the change report).</para>
    ///
    /// <para>The arbiter (<see cref="CursorBlockArbiter"/>) also calls <see cref="NoteBlockHolders"/> on
    /// every hold/release edge, so the trace pairs each <c>BlockCursorRaycast</c> flip with its cause
    /// on the exact frame it happened, not just the after-the-fact per-frame diff.</para>
    /// </summary>
    internal static class CursorDiag
    {
        /// <summary>Master switch. Flipped by the <c>uiadiag</c> console command.</summary>
        public static bool Enabled;

        /// <summary>Set by <c>uiadiag cursor</c>: suppress the noisy world-highlight axes
        /// (found/worldMode/carried/overUI) and log ONLY cursor lock/visibility/block plus every
        /// SetCursor call and CURSORFLIP correction — a clean flicker-only trace.</summary>
        public static bool CursorOnly;

        // Safety cap: a flicker toggling every frame at 60 fps is exactly what we want to capture, but
        // an unattended session should not spool the log forever. Auto-disable past this many lines.
        private const int MaxLines = 8000;
        private static int _lines;

        // ---- flicker auto-detector ----
        // A Cursor.visible toggle that REVERTS within this many frames is a blink the eye catches.
        // Tuned DOWN from 40: a legit FPS interlude between two menus (close console → ~0.6s later open a
        // radial) is ~40 frames and must NOT flag; a real churn-blink hid the cursor for only ~16–24
        // frames. 22 frames (~0.35s) catches the blink and excludes the interlude.
        private const int FlickerWindow = 22;
        private static bool _fdInit, _fdCurVis, _fdVisBefore;
        private static int _fdLastChangeFrame = -999;
        private static int _fdCount;

        // ---- last-sampled snapshot (only compared while Enabled) ----
        private static bool _haveLast;
        private static bool _block, _vis, _inGame, _inCC, _alt, _mouseCtl, _radial, _hudDrag, _overUi;
        private static CursorLockMode _lock;
        private static int _modals;
        private static string _found = "", _worldMode = "", _carried = "", _holders = "";

        private static FieldInfo _openModalsField;
        private static bool _openModalsFieldResolved;

        /// <summary>Turn the trace on/off and print the current state. Returns the new enabled state.
        /// <paramref name="cursorOnly"/> (the <c>uiadiag cursor</c> form) strips the world-highlight
        /// noise for a focused pointer-flicker trace.</summary>
        public static bool Toggle(bool cursorOnly = false)
        {
            Enabled = !Enabled;
            if (Enabled)
            {
                CursorOnly = cursorOnly;
                _haveLast = false;   // force a full baseline line next Sample
                _lines = 0;
                _fdInit = false; _fdCount = 0;   // reset the flicker auto-detector
                UIALog.Info("[UIA-diag] cursor/drag trace ON" + (cursorOnly ? " (cursor-only)" : "")
                    + " — reproduce, then run `uiadiag` again to stop. Watch for SetCursor(...), MODAL and "
                    + "FLICKER DETECTED lines.");
            }
            else
            {
                CursorOnly = false;
                UIALog.Info("[UIA-diag] cursor/drag trace OFF (" + _lines + " lines).");
            }
            return Enabled;
        }

        /// <summary>Called by <see cref="CursorBlockArbiter"/> on every hold/release edge so the exact
        /// cause of a <c>BlockCursorRaycast</c> flip is logged on the frame it happens.</summary>
        public static void NoteBlockHolders(string action, string id, string holders)
        {
            if (!Enabled) return;
            Emit("BLOCK " + action + " '" + id + "' -> holders=[" + holders + "]");
        }

        /// <summary>Called from <c>RadialController</c> on every radial OPEN / CLOSE, so a trace shows
        /// whether the wheel is actually cycling and — crucially — the Mouse2 (middle-button) state and
        /// the close REASON at that instant. If a CLOSE logs <c>mouse2=True</c> with reason
        /// <c>hold-released</c>, the button is physically down but read as up (the cursor-lock-resets-
        /// input loop). Logged even in cursor-only mode.</summary>
        public static void NoteRadial(string msg)
        {
            if (!Enabled) return;
            bool m2 = false;
            try { m2 = Input.GetKey(KeyCode.Mouse2); } catch { }
            Emit("RADIAL " + msg + " mouse2=" + B(m2));
        }

        /// <summary>Called from a patch on <c>MouseModeController.AddModal/RemoveModal</c> so a trace
        /// NAMES every cursor modal as it comes and goes. When the radial closes with a second modal
        /// appearing, this line identifies exactly what that modal is (a vanilla input window, a bag
        /// window, our own scope, …).</summary>
        public static void NoteModal(string action, string typeName)
        {
            if (!Enabled) return;
            // The game's ImGui integration calls RemoveModal(imguiModal) EVERY frame (a defensive
            // no-op) — pure noise that buries the trace. Skip it; every OTHER modal (our UiaModal, a
            // vanilla input window, the console) is rare and load-bearing for the cankeep diagnosis.
            if (typeName == "ImGuiModal") return;
            Emit("MODAL " + action + " " + typeName + " modals=" + OpenModalCount());
        }

        /// <summary>Logged from a postfix on <c>CursorManager.SetCursor</c> — the SINGLE funnel that
        /// writes <c>Cursor.visible</c>/<c>lockState</c> together. Two calls in one frame = the hardware
        /// pointer blinks; the caller string names who drove it.</summary>
        public static void NoteSetCursor(bool isLocked)
        {
            if (!Enabled) return;
            bool vis = false; string lk = "?";
            try { vis = Cursor.visible; lk = Cursor.lockState.ToString(); } catch { }
            Emit("SetCursor(locked=" + B(isLocked) + ") -> vis=" + B(vis) + " lock=" + lk + " | " + ShortCaller());
        }

        /// <summary>Best-effort compact caller chain for the SetCursor trace (only built while Enabled).</summary>
        private static string ShortCaller()
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false);
                var sb = new StringBuilder(48);
                int shown = 0;
                for (int i = 0; i < st.FrameCount && shown < 4; i++)
                {
                    var m = st.GetFrame(i) != null ? st.GetFrame(i).GetMethod() : null;
                    if (m == null) continue;
                    string n = (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "." + m.Name;
                    if (n.IndexOf("CursorDiag", System.StringComparison.Ordinal) >= 0) continue;
                    if (n.IndexOf("Patch_CursorManager", System.StringComparison.Ordinal) >= 0) continue;
                    if (sb.Length > 0) sb.Append(" <- ");
                    sb.Append(n);
                    shown++;
                }
                return sb.Length > 0 ? sb.ToString() : "?";
            }
            catch { return "?"; }
        }

        /// <summary>Per-frame sample from the plugin Update (placed at the END of the frame so it sees
        /// the resolved state). Emits a line only when a tracked value changed since last frame.</summary>
        public static void Sample()
        {
            if (!Enabled) return;

            bool block = false, vis = false, inGame = false, inCC = false, alt = false, mouseCtl = false;
            bool radial = false, hudDrag = false, overUi = false;
            CursorLockMode lockState = CursorLockMode.None;
            int modals = -1;
            string found = "<null>", worldMode = "?", carried = "<null>";
            string holders = "-";

            try { var cm = CursorManager.Instance; if (cm != null) block = cm.BlockCursorRaycast; } catch { }
            try { holders = CursorBlockArbiter.HoldersDebug; } catch { }
            try { vis = Cursor.visible; } catch { }
            try { lockState = Cursor.lockState; } catch { }
            try { inGame = MouseModeController.InGame; } catch { }
            try { inCC = MouseModeController.InCharacterCustomisation; } catch { }
            try { alt = MouseModeController.AltKeyDown; } catch { }
            try { modals = OpenModalCount(); } catch { }
            try { var cm = CursorManager.Instance; if (cm != null && cm.FoundThing != null) found = cm.FoundThing.name; } catch { }
            try { var im = InputMouse.Instance; if (im != null) { worldMode = im.WorldMode.ToString(); if (im.CursorItem != null) carried = im.CursorItem.name; } } catch { }
            try { mouseCtl = InputMouse.IsMouseControl; } catch { }
            try { radial = RadialController.AnyRadialOpen; } catch { }
            try { hudDrag = HudSlotDrag.IsDragging; } catch { }
            try { var es = EventSystem.current; overUi = es != null && es.IsPointerOverGameObject(); } catch { }

            // Auto-detector: flag the instant Cursor.visible toggles AND reverts within FlickerWindow
            // frames (hidden→shown→hidden or shown→hidden→shown) — that IS the blink. Self-reports so the
            // intermittent flicker no longer needs perfect trace timing; the SetCursor lines just above it
            // name the two culprits.
            try
            {
                int now = Time.frameCount;
                if (!_fdInit) { _fdInit = true; _fdCurVis = vis; _fdVisBefore = vis; }
                else if (vis != _fdCurVis)
                {
                    if (vis == _fdVisBefore && (now - _fdLastChangeFrame) <= FlickerWindow)
                        Emit("*** FLICKER DETECTED *** #" + (++_fdCount) + " cursor blinked "
                             + B(_fdVisBefore) + "->" + B(_fdCurVis) + "->" + B(vis)
                             + " in " + (now - _fdLastChangeFrame) + " frames (see the two SetCursor lines above for the cause)");
                    _fdVisBefore = _fdCurVis;
                    _fdCurVis = vis;
                    _fdLastChangeFrame = now;
                }
            }
            catch { }

            if (!_haveLast)
            {
                _haveLast = true;
                StoreLast(block, holders, vis, lockState, inGame, inCC, alt, modals, found, worldMode, carried, mouseCtl, radial, hudDrag, overUi);
                Emit("baseline block=" + B(block) + " holders=[" + holders + "] vis=" + B(vis) + " lock=" + lockState
                     + " mm(InGame=" + B(inGame) + ",CC=" + B(inCC) + ",Alt=" + B(alt) + ") modals=" + modals
                     + " found=" + found + " worldMode=" + worldMode + " carried=" + carried
                     + " mouseCtl=" + B(mouseCtl) + " radial=" + B(radial) + " hudDrag=" + B(hudDrag) + " overUI=" + B(overUi));
                return;
            }

            var sb = new StringBuilder(96);
            Diff(sb, "block", _block, block);
            if (_holders != holders) { sb.Append("holders[").Append(_holders).Append("->").Append(holders).Append("] "); }
            Diff(sb, "vis", _vis, vis);
            if (_lock != lockState) { sb.Append("lock[").Append(_lock).Append("->").Append(lockState).Append("] "); }
            Diff(sb, "InGame", _inGame, inGame);
            Diff(sb, "CC", _inCC, inCC);
            Diff(sb, "Alt", _alt, alt);
            if (_modals != modals) { sb.Append("modals[").Append(_modals).Append("->").Append(modals).Append("] "); }
            // The world-highlight axes churn constantly during free-look and drown the cursor signal —
            // suppressed in `uiadiag cursor` mode so the flicker trace shows only cursor lock/visibility.
            if (!CursorOnly)
            {
                if (_found != found) { sb.Append("found[").Append(_found).Append("->").Append(found).Append("] "); }
                if (_worldMode != worldMode) { sb.Append("worldMode[").Append(_worldMode).Append("->").Append(worldMode).Append("] "); }
                if (_carried != carried) { sb.Append("carried[").Append(_carried).Append("->").Append(carried).Append("] "); }
            }
            Diff(sb, "mouseCtl", _mouseCtl, mouseCtl);
            Diff(sb, "radial", _radial, radial);
            Diff(sb, "hudDrag", _hudDrag, hudDrag);
            Diff(sb, "overUI", _overUi, overUi);

            if (sb.Length > 0)
            {
                StoreLast(block, holders, vis, lockState, inGame, inCC, alt, modals, found, worldMode, carried, mouseCtl, radial, hudDrag, overUi);
                Emit(sb.ToString().TrimEnd());
            }
        }

        private static void Emit(string body)
        {
            if (_lines >= MaxLines)
            {
                if (_lines == MaxLines) { UIALog.Info("[UIA-diag] line cap reached (" + MaxLines + ") — trace auto-OFF."); Enabled = false; _lines++; }
                return;
            }
            _lines++;
            Vector2 m = Vector2.zero;
            try { m = Input.mousePosition; } catch { }
            UIALog.Info("[UIA-diag] f=" + Time.frameCount + " m=(" + (int)m.x + "," + (int)m.y + ") | " + body);
        }

        private static void Diff(StringBuilder sb, string name, bool was, bool now)
        {
            if (was != now) sb.Append(name).Append(' ').Append(B(was)).Append("->").Append(B(now)).Append(' ');
        }

        private static string B(bool b) => b ? "T" : "F";

        private static void StoreLast(bool block, string holders, bool vis, CursorLockMode lockState, bool inGame,
            bool inCC, bool alt, int modals, string found, string worldMode, string carried, bool mouseCtl,
            bool radial, bool hudDrag, bool overUi)
        {
            _block = block; _holders = holders; _vis = vis; _lock = lockState; _inGame = inGame; _inCC = inCC;
            _alt = alt; _modals = modals; _found = found; _worldMode = worldMode; _carried = carried;
            _mouseCtl = mouseCtl; _radial = radial; _hudDrag = hudDrag; _overUi = overUi;
        }

        private static int OpenModalCount()
        {
            if (!_openModalsFieldResolved)
            {
                _openModalsFieldResolved = true;
                _openModalsField = typeof(MouseModeController).GetField("_openModals",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }
            if (_openModalsField == null) return -1;
            var list = _openModalsField.GetValue(null) as System.Collections.ICollection;
            return list != null ? list.Count : -1;
        }

        /// <summary>Hot-reload / teardown: silence the trace and forget the snapshot.</summary>
        public static void Shutdown()
        {
            Enabled = false;
            _haveLast = false;
            _lines = 0;
        }
    }
}
