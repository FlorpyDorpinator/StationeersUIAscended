using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using ImGuiNET;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// The in-game LESSON EDITOR (0.9.8.0): FlorpyDorp's ImGui dev window for the tutorial copy,
    /// the tutorial's counterpart of the F9 HUD designer. Every string a lesson shows - step
    /// titles, card headings/bodies, strip Says/Then lines and their variants, oops/branch lines,
    /// F10 callouts, button labels, lesson titles, the "as I go" tips and the C.19 chrome lines -
    /// is one <see cref="TField"/> keyed by the contract s6 grammar, and is edited here through
    /// <see cref="TutorialTextStore"/>.
    ///
    /// <para><b>Dev only.</b> Refuses unless <see cref="UiaDevMode.Active"/> (the session-only
    /// <c>uiadev</c> latch) and closes itself if that latch drops. Opened by
    /// <c>uiatutorial edit</c>, by the coach card's EDIT button (<see cref="OpenAt"/>), and by
    /// <see cref="DevHotkey"/> (F8) - "edit while playing": F8 opens the editor jumped to the step
    /// currently on screen; with the editor open F8 jumps again when the lesson has moved on, and
    /// otherwise closes it.</para>
    ///
    /// <para><b>Live.</b> Every keystroke goes to the text store in memory at once; when the edited
    /// step is the one on screen (the card's <see cref="TutorialCoach.CurrentStepId"/>, the strip's
    /// <see cref="TutorialStrip.CurrentStepId"/>, <see cref="TutorialDirector.ActiveStepId"/> - which
    /// includes a preview) the showing strip/card is re-resolved through
    /// <see cref="TutorialDirector.RefreshActiveText"/>, so the lesson updates as you type. A lesson
    /// title refreshes for any step of its lesson; a chrome line whenever anything is showing.
    /// DISK: autosave when a field is committed (focus leaves it after an edit), on every revert /
    /// token insert, on page switch and on close - plus the Save button. Export writes the
    /// shipped-copy C# via <see cref="TutorialCopyExport"/>; tools/bake-tutorial.ps1 folds it into
    /// the repo.</para>
    ///
    /// <para><b>A standalone ImGui window</b> drawn from the plugin's ImGui pass
    /// (<see cref="Draw"/>), not a game <c>ImGuiWindow</c>: it has to live over the tutorial coach's
    /// modal card, so it owns the same three input holds the coach does - a KeyManager Typing state
    /// (vanilla binds starve while you type), an <c>IModal</c> cursor unlock, and a
    /// <see cref="CursorBlockArbiter"/> hold (no world picks through the window) - exactly the
    /// vanilla creative-spawn-menu pattern (<c>ImguiCreativeSpawnMenu.ShowMenu</c>, V27798).</para>
    ///
    /// <para><b>Over the UGUI modals.</b> ImGui renders into ONE full-screen RawImage
    /// (<c>ImGuiManager.outputRawImage</c>, ImGuiManager.cs V27798) whose canvas sits BELOW the
    /// mod's UGUI stack at rest - under the coach card (5600) the window would be drawn under the
    /// scrim. While open we lift that canvas to 5900 (the F9 designer's level, HudEditorMode) and,
    /// while the pointer is over THIS window, make the RawImage a raycast target so the click is not
    /// also delivered to the card underneath (ImGui input is read straight from Unity Input and is
    /// not blocked by UGUI; UGUI is not blocked by ImGui - vanilla's own
    /// <c>ImGuiManager.SetBlockUguiClicks</c> uses the same two flags). Both are edge-driven and
    /// only ever undo what this window did.</para>
    ///
    /// <para><b>Keyboard over the coach.</b> The coach reads raw Enter (= Next) and Escape (= close)
    /// in its Update. <see cref="OwnsKeyboard"/> is true while this window has keyboard focus or a
    /// text field is being edited; the coach must skip its nav keys while it is (see the build
    /// report). Escape here: inside a field it is ImGui's own "undo this field's typing"; with the
    /// window focused and no field active it closes the editor (with the shared Esc swallow, so the
    /// key-up never reaches vanilla's pause menu).</para>
    ///
    /// <para><b>Self-healing.</b> If <see cref="Draw"/> stops being called while open (the world
    /// ended, the ImGui pass stood down, the radial editor took the screen) a
    /// <c>Canvas.willRenderCanvases</c> hook - live ONLY while the window is open - closes it within
    /// <see cref="StaleFrames"/> frames and releases every hold; the cursor modal also lapses on its
    /// own. <see cref="Shutdown"/> (hot reload) unhooks it and resets every static.</para>
    ///
    /// <para>Never mutates game state: text overrides, one export file, UI holds. Nothing here
    /// touches the item-mutation funnel or the server message path.</para>
    /// </summary>
    internal static class TutorialEditorWindow
    {
        // ================================================================ constants

        /// <summary>The dev hotkey (read only while <see cref="UiaDevMode.Active"/>). F8 is free:
        /// vanilla's only F8 read is <c>InventoryManager.Update_UnityEditor</c> (Shift+F8, collider
        /// toggle, InventoryManager.cs:1263 V27798), which has no caller in the shipped build; the mod
        /// binds F9/F10 and nothing else in the F-row; F5/F6/F7/F11 are taken on the dev machine
        /// (vanilla QuickSave + Station Notepad, ScriptEngine reload, UnityExplorer, SEGI+).</summary>
        internal const KeyCode DevHotkey = KeyCode.F8;

        /// <summary>This window's KeyManager input-state owner name.</summary>
        internal const string InputStateKey = "UIA_TutorialEditor";

        private const string CursorHoldId = "tutorialeditor";
        private const string WindowId = "Lesson Editor - UI Ascended (dev)###UIATutorialEditor";
        private const int StaleFrames = 3;          // frames without a Draw before the heal hook closes us
        private const int ImGuiLiftOrder = 5900;    // the F9 designer's level: above the coach (5600) + Handbook (5700), below tooltips (6000)
        private const int VanillaBlockOrder = 32767; // ImGuiManager.SetBlockUguiClicks(true) parks the canvas here (ImGuiManager.cs:112 V27798)
        private const float PollSeconds = 0.5f;     // lesson states + override count
        private const float LabelRefreshSeconds = 1f;
        private const float ArmSeconds = 3f;        // two-click window for "Revert step"
        private const uint FieldBytes = 2048u;
        private const uint SearchBytes = 64u;
        private const string TipsGroupId = "~tips";
        private const string ChromeGroupId = "~chrome";

        private static readonly Vector4 Accent = new Vector4(0.25f, 0.85f, 0.93f, 1f);
        private static readonly Vector4 WarnCol = new Vector4(1f, 0.72f, 0.25f, 1f);
        private static readonly Vector4 GoodCol = new Vector4(0.45f, 0.92f, 0.55f, 1f);
        private static readonly Vector4 BadCol = new Vector4(1f, 0.42f, 0.36f, 1f);
        private static readonly Vector4 DimCol = new Vector4(0.60f, 0.64f, 0.68f, 1f);
        private static readonly Vector4 TextCol = new Vector4(0.90f, 0.93f, 0.95f, 1f);
        private static readonly Vector4 PreviewCol = new Vector4(0.80f, 0.90f, 0.94f, 1f);

        // ================================================================ key tokens

        /// <summary>One insert button. Mirrors the "[Key] -> token" table in
        /// Tutorial-Plan-and-Script.md ("How to read and edit Part C"): the label is the key as the
        /// script writes it, the code is what the text store keeps.</summary>
        private sealed class Tok
        {
            internal readonly string Label;        // visible text (width is measured from this)
            internal readonly string ButtonLabel;  // Label + a unique ImGui id suffix
            internal readonly string Code;
            internal readonly string Note;

            internal Tok(int index, string label, string code, string note)
            {
                Label = label;
                ButtonLabel = label + "##uiatl_tok" + index;
                Code = code;
                Note = note;
            }
        }

        private static readonly Tok[] Tokens =
        {
            new Tok(0, "[MMB]", "{UIA_ToolbeltRadial}", "Belt wheel (default middle mouse)."),
            new Tok(1, "[R]", "{UIA_ToolRadial}", "Held-item wheel; also 'open device' in the big window."),
            new Tok(2, "[Tab]", "{UIA_BagRadial}", "Bag wheel / The Hub root."),
            new Tok(3, "[B]", "{UIA_Grid}", "Universal Inventory (the big window)."),
            new Tok(4, "[F10]", "{UIA_Menu}", "Control Center."),
            new Tok(5, "[F9]", "{UIA_HudDesigner}", "HUD Designer."),
            new Tok(6, "[Q]", "{UIA_Page}", "Only while a wheel is open (page / belt chooser)."),
            new Tok(7, "[C]", "{UIA_FineAdjust}", "Fine value steps."),
            new Tok(8, "[E]", "{V:SwapHands}", "Gameplay hand swap (vanilla key)."),
            new Tok(9, "[E] in wheel", "{UIA_HandSwap}", "Hand swap INSIDE a wheel (the mod's key)."),
            new Tok(10, "[G]", "{V:SmartStow}", "Smart Stow (vanilla key, SmartStow+ behaviour)."),
            new Tok(11, "[F]", "{V:InventorySelect}", "Vanilla inventory-select key."),
            new Tok(12, "[Alt]", "{V:MouseControl}", "Free the mouse (vanilla key)."),
            new Tok(13, "[1]", "{V:HelmetSlot}", "Gear key 1 (helmet), read live."),
            new Tok(14, "[2]", "{V:GlassesSlot}", "Gear key 2 (glasses), read live."),
            new Tok(15, "[3]", "{V:SuitSlot}", "Gear key 3 (suit), read live."),
            new Tok(16, "[4]", "{V:BackSlot}", "Gear key 4 (back), read live."),
            new Tok(17, "[5]", "{V:UniformSlot}", "Gear key 5 (uniform), read live."),
            new Tok(18, "[6]", "{V:ToolBeltSlot}", "Gear key 6 (tool belt), read live."),
        };

        // ================================================================ model

        /// <summary>One editable string. The buffer is authoritative only while ImGui has the field
        /// active; otherwise it is re-read from the store every frame, so an edit made anywhere else
        /// (the coach card's in-place fields, a revert) shows up here at once.</summary>
        private sealed class Field
        {
            internal string Key;
            internal string Label;
            internal string KeyNote;         // "(core.hands|says)"
            internal int Budget;
            internal bool SingleLine;        // titles / headings / buttons (budget <= 28)
            internal float Lines;            // multi-line box height, in text lines
            internal Page Page;              // owning page (null for a lesson title)
            internal Group Group;            // owning lesson / pseudo group
            internal string Buffer = "";
            internal bool Active;
            // Caches, rebuilt only when the text changes (reference compare): no per-frame strings.
            internal string CacheFor;
            internal string PreviewText = "";
            internal string CountLabel = "";
            internal int RawLength;
            internal bool StandIns;          // placeholders counted at their longest (TutorialLint.ShownForBudget)
            internal bool Over;
            internal string Lint;            // null = OK
        }

        /// <summary>A lesson step, or the single page of the tips / chrome pseudo groups.</summary>
        private sealed class Page
        {
            internal string Id;
            internal TStep Step;             // null on a pseudo page
            internal Group Owner;
            internal Field[] Fields;
            internal string TitleKey;        // this step's "|title" key (list row text), if it has one
            internal string HeaderNote;      // "STRIP  -  demo handswap"
            internal string RowLabel = "";
            internal bool Edited;
        }

        private sealed class Group
        {
            internal string Id;
            internal TLesson Lesson;         // null for the tips / chrome groups
            internal string FixedTitle;      // pseudo groups' display title
            internal Field TitleField;       // the lesson title: edited on top of each of its step pages
            internal Page[] Pages;
            internal readonly List<Page> Visible = new List<Page>();
            internal string RowLabel = "";
            internal bool Edited;
            internal string StateLabel = "";
            internal Vector4 StateCol;
        }

        private static Group[] _groups;
        private static Dictionary<string, Page> _pageById;

        // ================================================================ state

        private sealed class EditorModal : global::Assets.Scripts.IModal
        {
            // Self-healing: if Draw() stops running while we think we are open, the unlock lapses on
            // its own - MouseModeController.Check polls this every frame (MouseModeController.cs, V27798).
            public bool UnlockCursor { get { return _open && Time.frameCount - _lastDrawFrame <= StaleFrames; } }
        }

        private static readonly EditorModal _modal = new EditorModal();

        private static bool _open;
        private static bool _holdsAcquired;
        private static int _lastDrawFrame = -1000;
        private static bool _requestFocus;
        private static bool _focused, _hovered, _anyFieldActive;   // as of the last Draw

        private static Page _selPage;
        private static Field _insertTarget;
        private static Group _openGroupOnce;
        private static bool _scrollToSel;
        private static bool _pageScrollReset;
        private static Page _armRevertPage;
        private static float _armUntil;

        private static string _search = "";
        private static bool _editedOnly;
        private static bool _filterDirty = true;
        private static int _filterVersion = -1;
        private static int _version;
        private static float _nextLabelRefresh;

        private static string _status;
        private static Vector4 _statusCol;
        private static bool _dirty;
        private static string _savedLabel = "Autosave: on field commit";
        private static int _countShown = -1;
        private static string _countLabel = "";
        private static int _polledCount = -1;
        private static float _nextPoll;

        private static string _liveId, _activeId, _liveLabel = "";
        private static bool _stripVisible, _cardOpen;
        private static string _previewStepId;
        // We started a Director preview and have not stopped it. Separate from _previewStepId, which
        // PollLive forgets once nothing shows - the Director's preview (which PAUSES the lesson flow)
        // can outlive that, so every close path keys on this.
        private static bool _previewOwned;
        private static float _previewAt;
        private static bool _refreshWarned;

        // Caret tracking for "insert at the cursor" (see TextCallback).
        private static Field _cbField;             // the field whose InputText call is in flight
        private static Field _caretField;          // last field the callback reported from
        private static int _caretPos = -1;         // its caret (byte index == char index: copy is ASCII)
        private static Field _refocusField;        // give this field the keyboard next frame
        private static Field _pendingCaretField;   // ...and put its caret here once it is active
        private static int _pendingCaret = -1;
        private static ImGuiInputTextCallback _imCb;
        private static bool _cbTried;

        // ImGui canvas lift + UGUI click shield.
        private static Canvas _imguiCanvas;
        private static UnityEngine.UI.RawImage _imguiImage;
        private static bool _imguiResolved;
        private static bool _liftMine, _liftOrigOverride;
        private static int _liftOrigOrder;
        private static bool _shieldMine, _shieldOrigEnabled;
        private static UnityEngine.UI.GraphicRaycaster _shieldRaycaster;

        private static Canvas.WillRenderCanvases _healHook;
        private static int _errors;
        private static float _errorLogAt;

        // ================================================================ public surface

        /// <summary>True while the editor window is open.</summary>
        internal static bool IsOpen { get { return _open; } }

        /// <summary>True while this window has keyboard focus or one of its text fields is being
        /// edited (as of the last ImGui frame). Surfaces that read raw Enter / Escape (the coach's
        /// Next / close, the Control Center, the Handbook) must skip those keys while it is true, or
        /// typing a line break here also pages the card.</summary>
        internal static bool OwnsKeyboard { get { return _open && (_focused || _anyFieldActive); } }

        /// <summary>Open, or close when already open. Opening jumps to the step on screen, if any.</summary>
        internal static void Toggle()
        {
            if (_open) { Close(); return; }
            OpenAt(LiveStepNow());
        }

        /// <summary>Open the editor on <paramref name="stepId"/> (null = the live step, else the last
        /// selection). Refuses with "type uiadev first" unless author mode is on.</summary>
        internal static void OpenAt(string stepId)
        {
            try
            {
                if (!UiaDevMode.Active)
                {
                    Say("Lesson editor: type uiadev first (author mode unlocks it).", true);
                    return;
                }
                if (!Guards.CanDraw())
                {
                    Say("Lesson editor: load into a world first.", true);
                    return;
                }
                EnsureModel();
                if (string.IsNullOrEmpty(stepId)) stepId = LiveStepNow();
                Page p;
                if (!string.IsNullOrEmpty(stepId) && _pageById.TryGetValue(stepId, out p)) Select(p, true);
                else if (!string.IsNullOrEmpty(stepId)) Status("No step '" + stepId + "' in the lesson script.", WarnCol);
                if (_selPage == null) SelectFirst();
                if (!_open) OpenInternal();
                _requestFocus = true;
            }
            catch (Exception e) { NoteError("open", e); }
        }

        /// <summary>Close (saves pending typing, ends our own preview, releases every hold).</summary>
        internal static void Close()
        {
            CloseInternal(false, false);
        }

        /// <summary>The ImGui pass: called every frame from the plugin's ImGui hook. Cheap when
        /// closed (one bool when author mode is off; one GetKeyDown when it is on). Never throws.</summary>
        internal static void Draw()
        {
            try
            {
                if (!UiaDevMode.Active)
                {
                    if (_open) CloseInternal(false, false);
                    return;
                }
                _lastDrawFrame = Time.frameCount;

                if (Input.GetKeyDown(DevHotkey) && Guards.CanToggleMenus()) OnHotkey();
                if (!_open) return;
                if (!Guards.CanDraw()) { CloseInternal(false, false); return; }

                // Escape (last frame's focus): with no field being edited, it closes the editor; in a
                // field it is ImGui's own "revert this field's typing" and is left alone.
                if (_focused && !_anyFieldActive && Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseInternal(true, false);
                    return;
                }

                AssertHolds();
                PollLive();
                PollSlow();
                DrawWindow();
                SetShield(_open && _hovered);
            }
            catch (Exception e) { NoteError("draw", e); }
        }

        /// <summary>Hot-reload / plugin teardown: release every hold, unhook the heal hook, hand the
        /// ImGui canvas back, and reset every static. Idempotent.</summary>
        internal static void Shutdown()
        {
            try { UnhookHeal(); } catch { }
            if (_open && _dirty)
            {
                // Best effort: unsaved typing survives an F6 if the store is still loaded.
                try { TutorialTextStore.Save(); } catch { }
            }
            _open = false;
            try { ReleaseHolds(); } catch { }
            try { SetShield(false); } catch { }
            try { RestoreImGuiCanvas(); } catch { }

            _groups = null;
            _pageById = null;
            _lastDrawFrame = -1000;
            _requestFocus = false;
            _focused = _hovered = _anyFieldActive = false;
            _selPage = null;
            _insertTarget = null;
            _openGroupOnce = null;
            _scrollToSel = false;
            _pageScrollReset = false;
            _armRevertPage = null;
            _armUntil = 0f;
            _search = "";
            _editedOnly = false;
            _filterDirty = true;
            _filterVersion = -1;
            _version = 0;
            _nextLabelRefresh = 0f;
            _status = null;
            _statusCol = TextCol;
            _dirty = false;
            _savedLabel = "Autosave: on field commit";
            _countShown = -1;
            _countLabel = "";
            _polledCount = -1;
            _nextPoll = 0f;
            _liveId = null;
            _activeId = null;
            _liveLabel = "";
            _stripVisible = false;
            _cardOpen = false;
            _previewStepId = null;
            _previewOwned = false;
            _previewAt = 0f;
            _refreshWarned = false;
            _cbField = null;
            _caretField = null;
            _caretPos = -1;
            _refocusField = null;
            _pendingCaretField = null;
            _pendingCaret = -1;
            _imCb = null;
            _cbTried = false;
            _imguiCanvas = null;
            _imguiImage = null;
            _imguiResolved = false;
            _liftMine = false;
            _shieldMine = false;
            _shieldRaycaster = null;
            _healHook = null;
            _errors = 0;
            _errorLogAt = 0f;
        }

        // ================================================================ open / close

        private static void OpenInternal()
        {
            _open = true;
            _lastDrawFrame = Time.frameCount;   // the heal hook must not fire before the first Draw
            _requestFocus = true;
            InvalidateCaches();                 // re-resolve glyphs: a rebind may have happened while closed
            _filterDirty = true;
            AcquireHolds();
            HookHeal();
        }

        /// <summary><paramref name="escSwallow"/>: closed by an Escape DOWN - starve vanilla's
        /// key-UP binding. <paramref name="fromHeal"/>: called from the willRenderCanvases hook
        /// (outside any ImGui frame, possibly with the world gone) - it draws nothing, but still
        /// releases every hold AND ends our preview like every other close.</summary>
        private static void CloseInternal(bool escSwallow, bool fromHeal)
        {
            if (!_open) return;
            _open = false;
            if (escSwallow) { try { ModalInputChain.BeginEscSwallow(); } catch { } }
            // EVERY close path ends our preview - the heal path (fromHeal) included: a preview left
            // running keeps the lesson paused behind a step whose Stop button lived in this window.
            // TutorialDirector.StopPreview is idempotent and draws no ImGui, so it is safe here.
            StopOurPreview();
            if (_dirty) SaveNow(null);
            ReleaseHolds();
            SetShield(false);
            RestoreImGuiCanvas();
            UnhookHeal();
            _focused = _hovered = _anyFieldActive = false;
            _refocusField = null;
            _pendingCaretField = null;
            _pendingCaret = -1;
            _cbField = null;
            _armRevertPage = null;
            ResetFieldActivity();
        }

        private static void OnHotkey()
        {
            string live = LiveStepNow();
            if (!_open) { OpenAt(live); return; }
            // Open already: F8 follows the lesson when it has moved on, otherwise it closes.
            Page p;
            if (live != null && (_selPage == null || !string.Equals(_selPage.Id, live, StringComparison.Ordinal))
                && _pageById != null && _pageById.TryGetValue(live, out p))
            {
                Select(p, true);
                _requestFocus = true;
                return;
            }
            CloseInternal(false, false);
        }

        // ================================================================ input holds

        private static void AcquireHolds()
        {
            if (_holdsAcquired) return;
            _holdsAcquired = true;
            try { KeyManager.SetInputState(InputStateKey, KeyInputState.Typing); } catch { }
            try { global::Assets.Scripts.MouseModeController.AddModal(_modal); } catch { }
            CursorBlockArbiter.Hold(CursorHoldId);
            // Assert the unlock THIS frame (ModalScope precedent): otherwise the cursor stays locked
            // for one frame and the mouse motion made while opening turns the camera.
            try { global::Assets.Scripts.MouseModeController.Check(); } catch { }
        }

        /// <summary>Per-frame while open. A modal closing underneath (the coach card) pops KeyManager
        /// to the map's last entry, which can be the pause's "WorldManager"/Paused; ModalInputChain
        /// does not know this window, so re-take Typing ourselves whenever it is gone.</summary>
        private static void AssertHolds()
        {
            try
            {
                if (KeyManager.InputState != KeyInputState.Typing)
                    KeyManager.SetInputState(InputStateKey, KeyInputState.Typing);
            }
            catch { }
            try { global::Assets.Scripts.MouseModeController.AddModal(_modal); } catch { }   // idempotent
            CursorBlockArbiter.Hold(CursorHoldId);                                          // idempotent
            RaiseImGuiCanvas();
        }

        /// <summary>Mirror of the coach's ReleaseModal: input state, then hand Typing back to any
        /// still-open UIA modal, then the cursor modal and the pick block; re-lock the cursor only when
        /// no other UIA surface still holds it.</summary>
        private static void ReleaseHolds()
        {
            if (!_holdsAcquired) return;
            _holdsAcquired = false;
            try { KeyManager.RemoveInputState(InputStateKey); } catch { }
            try { ModalInputChain.ReassertTop(); } catch { }
            try { global::Assets.Scripts.MouseModeController.RemoveModal(_modal); } catch { }
            CursorBlockArbiter.Release(CursorHoldId);
            if (CursorBlockArbiter.AnyHold) return;
            try
            {
                var cm = global::Assets.Scripts.CursorManager.Instance;
                if (cm != null) cm.OnApplicationFocus(true);   // re-lock the cursor next frame
            }
            catch { }
        }

        // ================================================================ ImGui canvas lift + shield

        /// <summary>ImGuiManager.current (private static) -> outputRawImage (private [SerializeField])
        /// -> its canvas. Verified against ImGuiManager.cs (V27798); the same reflection
        /// HudEditorMode.RaiseImGuiCanvas uses. Fail-soft: null means "no lift, no shield".</summary>
        private static bool ResolveImGuiCanvas()
        {
            if (_imguiResolved) return _imguiCanvas != null && _imguiImage != null;
            _imguiResolved = true;
            try
            {
                var t = typeof(global::Assets.Scripts.UI.ImGuiManager);
                var curF = t.GetField("current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object cur = curF != null ? curF.GetValue(null) : null;
                var imgF = cur != null
                    ? t.GetField("outputRawImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                _imguiImage = imgF != null ? imgF.GetValue(cur) as UnityEngine.UI.RawImage : null;
                _imguiCanvas = _imguiImage != null ? _imguiImage.canvas : null;
            }
            catch
            {
                _imguiImage = null;
                _imguiCanvas = null;
            }
            return _imguiCanvas != null && _imguiImage != null;
        }

        /// <summary>Raise-only, re-asserted every frame (a radial closing runs vanilla's
        /// SetBlockUguiClicks(false), which drops overrideSorting). Never lowers a higher vanilla lift
        /// (an open console / ImGui modal at 32767).</summary>
        private static void RaiseImGuiCanvas()
        {
            if (!ResolveImGuiCanvas()) return;
            try
            {
                if (!_imguiCanvas.overrideSorting || _imguiCanvas.sortingOrder < ImGuiLiftOrder)
                {
                    if (!_liftMine)
                    {
                        _liftOrigOverride = _imguiCanvas.overrideSorting;
                        _liftOrigOrder = _imguiCanvas.sortingOrder;
                        _liftMine = true;
                    }
                    _imguiCanvas.overrideSorting = true;
                    _imguiCanvas.sortingOrder = ImGuiLiftOrder;
                }
            }
            catch { }
        }

        /// <summary>Put back what WE changed - and only while it is still ours. If the F9 designer is
        /// up it re-raises the canvas on its next Update (HudEditorMode.RaiseImGuiCanvas), so this can
        /// never strand F9 low.</summary>
        private static void RestoreImGuiCanvas()
        {
            if (_liftMine && _imguiCanvas != null)
            {
                try
                {
                    // Only undo a value that still equals the one WE set. The console lifts this same
                    // canvas to 32767 (ConsoleWindow.Show -> ImGuiManager.SetBlockUguiClicks(true),
                    // ImGuiManager.cs:112 V27798); writing our recorded pre-lift values over that would
                    // drop the open console under the mod's UGUI. Its own unblock puts it back later.
                    // (Any other lift is left alone too: an order that is no longer ours is not ours to
                    // restore - unlike the shield, a lift left in place blocks nothing.)
                    if (_imguiCanvas.sortingOrder == ImGuiLiftOrder)
                    {
                        _imguiCanvas.sortingOrder = _liftOrigOrder;
                        if (_imguiCanvas.overrideSorting) _imguiCanvas.overrideSorting = _liftOrigOverride;
                    }
                }
                catch { }
            }
            _liftMine = false;
            _imguiResolved = false;   // re-resolve on the next open (a stale ref after a scene change)
            _imguiCanvas = null;
            _imguiImage = null;
        }

        /// <summary>While the pointer is over this window, the full-screen ImGui RawImage becomes a
        /// UGUI raycast target, so a click on an editor button is not ALSO a click on the coach card /
        /// F10 underneath. Off the moment the pointer leaves. Edge-driven; if the image is already a
        /// raycast target when we would take it, it belongs to a vanilla ImGui modal (or a radial's
        /// SetBlockUguiClicks) and is left alone.</summary>
        private static void SetShield(bool on)
        {
            if (on)
            {
                if (!ResolveImGuiCanvas()) return;
                try
                {
                    if (!_shieldMine)
                    {
                        if (_imguiImage.raycastTarget) return;   // someone else's block
                        var rc = _imguiCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                        if (rc == null) rc = _imguiCanvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                        _shieldRaycaster = rc;
                        _shieldOrigEnabled = rc.enabled;
                        _shieldMine = true;
                    }
                    if (_shieldRaycaster != null && !_shieldRaycaster.enabled) _shieldRaycaster.enabled = true;
                    if (!_imguiImage.raycastTarget) _imguiImage.raycastTarget = true;
                }
                catch { }
                return;
            }
            if (!_shieldMine) return;
            _shieldMine = false;
            try
            {
                // Only undo what is still ours. A vanilla block taken since (the console:
                // ImGuiManager.SetBlockUguiClicks(true), ImGuiManager.cs:112 V27798) sets the SAME two
                // flags we set - raycaster on, raycastTarget on - so the flags cannot tell whose they
                // are now; its canvas lift can: it parks the canvas at 32767 with overrideSorting, and
                // its unblock clears both flags itself. While that block is live the flags are its -
                // leave them. (Keyed on vanilla's exact order, not "any lift but ours": a foreign lift
                // WITHOUT a block must never leave our full-screen raycast target stuck on.)
                bool vanillaBlock = _imguiCanvas != null && _imguiCanvas.overrideSorting
                    && _imguiCanvas.sortingOrder == VanillaBlockOrder;
                if (!vanillaBlock)
                {
                    if (_imguiImage != null && _imguiImage.raycastTarget) _imguiImage.raycastTarget = false;   // we set true
                    if (_shieldRaycaster != null && _shieldRaycaster.enabled && !_shieldOrigEnabled)
                        _shieldRaycaster.enabled = false;                                                     // we set true
                }
            }
            catch { }
            _shieldRaycaster = null;
        }

        // ================================================================ self-heal hook

        private static void HookHeal()
        {
            if (_healHook != null) return;
            _healHook = OnWillRenderCanvases;
            try { Canvas.willRenderCanvases += _healHook; }
            catch { _healHook = null; }
        }

        private static void UnhookHeal()
        {
            if (_healHook == null) return;
            try { Canvas.willRenderCanvases -= _healHook; } catch { }
            _healHook = null;
        }

        /// <summary>Runs every frame (only while open), after LateUpdate - i.e. after this frame's
        /// ImGui pass would have called Draw. A few frames with no Draw = nobody is drawing us.</summary>
        private static void OnWillRenderCanvases()
        {
            try
            {
                if (!_open) { UnhookHeal(); return; }
                if (!UiaDevMode.Active || Time.frameCount - _lastDrawFrame > StaleFrames)
                    CloseInternal(false, true);
            }
            catch { }
        }

        // ================================================================ model build

        private static void EnsureModel()
        {
            if (_groups != null) return;
            var groups = new List<Group>();
            var byId = new Dictionary<string, Page>(StringComparer.Ordinal);

            TLesson[] lessons = null;
            try { lessons = TutorialChapters.Lessons; }
            catch (Exception e) { UIALog.Warn("Lesson editor: TutorialChapters unavailable - " + e.Message); }

            if (lessons != null)
            {
                for (int li = 0; li < lessons.Length; li++)
                {
                    TLesson lesson = lessons[li];
                    if (lesson == null || string.IsNullOrEmpty(lesson.Id)) continue;
                    var g = new Group { Id = lesson.Id, Lesson = lesson };
                    if (!string.IsNullOrEmpty(lesson.TitleKey))
                        g.TitleField = MakeField(lesson.TitleKey, "Lesson title", 0, null, g);

                    var pages = new List<Page>();
                    TStep[] steps = lesson.Steps;
                    if (steps != null)
                        for (int si = 0; si < steps.Length; si++)
                            AddStepPage(steps[si], g, pages, byId);
                    g.Pages = pages.ToArray();
                    groups.Add(g);
                }
            }

            string[] tips = null, chrome = null;
            try { tips = TutorialChapters.TipKeys; } catch { }
            try { chrome = TutorialChapters.ChromeKeys; } catch { }
            AddTipGroup(groups, byId, tips);
            AddKeyGroup(groups, byId, ChromeGroupId, "CHROME LINES (C.19)", chrome);

            _groups = groups.ToArray();
            _pageById = byId;
            _version++;
            _filterDirty = true;
        }

        /// <summary>One page per step, its <see cref="TStep.Fields"/> in declaration (= export) order.</summary>
        private static void AddStepPage(TStep step, Group g, List<Page> pages, Dictionary<string, Page> byId)
        {
            if (step == null || string.IsNullOrEmpty(step.Id) || byId.ContainsKey(step.Id)) return;
            var p = new Page
            {
                Id = step.Id,
                Step = step,
                Owner = g,
                HeaderNote = KindName(step.Kind)
                    + (string.IsNullOrEmpty(step.DemoId) ? "  -  no demo" : "  -  demo " + step.DemoId),
            };
            var fields = new List<Field>();
            if (step.Fields != null)
            {
                for (int fi = 0; fi < step.Fields.Length; fi++)
                {
                    TField tf = step.Fields[fi];
                    if (string.IsNullOrEmpty(tf.Key)) continue;
                    fields.Add(MakeField(tf.Key, tf.Label, tf.Budget, p, g));
                    if (p.TitleKey == null && tf.Key.EndsWith("|title", StringComparison.Ordinal))
                        p.TitleKey = tf.Key;
                }
            }
            p.Fields = fields.ToArray();
            pages.Add(p);
            byId[p.Id] = p;
        }

        /// <summary>The "as I go" tips are real steps ("tip.3", <see cref="TutorialChapters.FindStep"/>),
        /// so each gets its own page: it can be previewed, and it lights up when it is the strip on
        /// screen. A tip key whose step cannot be found still lands on a flat fallback page.</summary>
        private static void AddTipGroup(List<Group> groups, Dictionary<string, Page> byId, string[] keys)
        {
            if (keys == null || keys.Length == 0) return;
            var g = new Group { Id = TipsGroupId, FixedTitle = "TIPS (as I go)" };
            var pages = new List<Page>();
            var loose = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key)) continue;
                int bar = key.IndexOf('|');
                TStep step = null;
                try { step = bar > 0 ? TutorialChapters.FindStep(key.Substring(0, bar)) : null; } catch { }
                if (step == null || step.Fields == null) { loose.Add(key); continue; }
                AddStepPage(step, g, pages, byId);   // a step already paged (several keys) is skipped
            }
            if (loose.Count > 0) pages.Add(MakeKeyPage(TipsGroupId + ".other", g, loose, byId));
            g.Pages = pages.ToArray();
            groups.Add(g);
        }

        private static void AddKeyGroup(List<Group> groups, Dictionary<string, Page> byId, string id,
            string title, string[] keys)
        {
            if (keys == null || keys.Length == 0) return;
            var g = new Group { Id = id, FixedTitle = title };
            g.Pages = new[] { MakeKeyPage(id, g, new List<string>(keys), byId) };
            groups.Add(g);
        }

        /// <summary>A page of loose keys (the C.19 chrome lines): no step behind it, so no preview;
        /// labels and budgets come from the chapter declarations when they exist.</summary>
        private static Page MakeKeyPage(string id, Group g, List<string> keys, Dictionary<string, Page> byId)
        {
            var p = new Page
            {
                Id = id,
                Owner = g,
                HeaderNote = "shown by the lesson system itself, not by one step",
            };
            var fields = new List<Field>();
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key)) continue;
                string label = null;
                int budget = 0;
                try
                {
                    TField tf;
                    if (TutorialChapters.TryField(key, out tf)) { label = tf.Label; budget = tf.Budget; }
                }
                catch { }
                fields.Add(MakeField(key, label, budget, p, g));
            }
            p.Fields = fields.ToArray();
            byId[id] = p;
            return p;
        }

        private static Field MakeField(string key, string label, int budget, Page page, Group group)
        {
            int b = budget > 0 ? budget : BudgetForKey(key);
            return new Field
            {
                Key = key,
                Label = string.IsNullOrEmpty(label) ? LabelForKey(key) : label,
                KeyNote = "(" + key + ")",
                Budget = b,
                SingleLine = b <= 28,
                Lines = b >= 200 ? 5f : 3f,
                Page = page,
                Group = group,
            };
        }

        /// <summary>Contract s6 budgets, for keys that arrive without a TField budget (lesson titles,
        /// tips, chrome). Chrome has no contract budget; it uses the strip line's 125.</summary>
        private static int BudgetForKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return 125;
            if (key.StartsWith("lesson:", StringComparison.Ordinal)) return 28;
            if (key.StartsWith("tip.", StringComparison.Ordinal)) return 125;
            if (key.StartsWith("chrome|", StringComparison.Ordinal)) return 125;
            int bar = key.IndexOf('|');
            string field = bar >= 0 ? key.Substring(bar + 1) : key;
            int at = field.IndexOf('@');
            if (at >= 0) field = field.Substring(0, at);
            switch (field)
            {
                case "title":
                case "heading": return 28;
                case "body": return 280;
                case "says":
                case "oops":
                case "branch": return 125;
                case "then": return 100;
                case "callout": return 160;
                case "button": return 24;
                default: return 125;
            }
        }

        private static string LabelForKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            if (key.StartsWith("lesson:", StringComparison.Ordinal)) return "Lesson title";
            if (key.StartsWith("tip.", StringComparison.Ordinal))
            {
                int bar = key.IndexOf('|');
                return "Tip " + (bar > 4 ? key.Substring(4, bar - 4) : key.Substring(4));
            }
            int b = key.IndexOf('|');
            return b >= 0 && b < key.Length - 1 ? key.Substring(b + 1) : key;
        }

        private static string KindName(TPresentation k)
        {
            switch (k)
            {
                case TPresentation.Card: return "CARD";
                case TPresentation.Strip: return "STRIP";
                case TPresentation.Callout: return "CALLOUT";
                case TPresentation.Tip: return "TIP";
                case TPresentation.Chrome: return "CHROME";
                default: return "?";
            }
        }

        // ================================================================ selection

        private static void Select(Page p, bool jump)
        {
            if (p == null) return;
            if (!ReferenceEquals(_selPage, p))
            {
                if (_dirty) SaveNow(null);          // switching pages is a commit point too
                _selPage = p;
                _pageScrollReset = true;
                _armRevertPage = null;
                _insertTarget = DefaultTarget(p);
                // A token re-focus aimed at the old page must not grab the keyboard when that page
                // is shown again later.
                _refocusField = null;
                _pendingCaretField = null;
                _pendingCaret = -1;
            }
            if (jump)
            {
                _openGroupOnce = p.Owner;
                _scrollToSel = true;
            }
        }

        private static void SelectFirst()
        {
            if (_groups == null) return;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].Pages != null && _groups[i].Pages.Length > 0) { Select(_groups[i].Pages[0], true); return; }
        }

        private static Field DefaultTarget(Page p)
        {
            if (p == null) return null;
            if (p.Fields != null)
            {
                for (int i = 0; i < p.Fields.Length; i++) if (!p.Fields[i].SingleLine) return p.Fields[i];
                if (p.Fields.Length > 0) return p.Fields[0];
            }
            return p.Owner != null ? p.Owner.TitleField : null;
        }

        private static bool BelongsTo(Field f, Page p)
        {
            return f != null && p != null && (ReferenceEquals(f.Page, p) || ReferenceEquals(f, p.Owner.TitleField));
        }

        // ================================================================ live state

        /// <summary>What is on screen right now: the card's step (a card is modal and sits on top),
        /// else the strip's, else the director's active step (a lesson between steps). Safe to call
        /// any time.</summary>
        private static string LiveStepNow()
        {
            PollLive();
            return _liveId;
        }

        private static void PollLive()
        {
            bool stripVis = false, card = false;
            string strip = null, cardStep = null, active = null;
            try { stripVis = TutorialStrip.IsVisible; if (stripVis) strip = TutorialStrip.CurrentStepId; } catch { }
            try { card = TutorialCoach.CardOpen; if (card) cardStep = TutorialCoach.CurrentStepId; } catch { }
            try { active = TutorialDirector.ActiveStepId; } catch { }
            _stripVisible = stripVis;
            _cardOpen = card;
            _activeId = string.IsNullOrEmpty(active) ? null : active;
            // A preview that ended by itself (the strip faded, the card was closed) is not ours any more.
            if (_previewStepId != null && !stripVis && !card && Time.unscaledTime - _previewAt > 1f)
                _previewStepId = null;
            string live = !string.IsNullOrEmpty(cardStep) ? cardStep
                : (!string.IsNullOrEmpty(strip) ? strip
                : (card && _previewStepId != null ? _previewStepId : _activeId));
            if (!string.Equals(live, _liveId, StringComparison.Ordinal))
            {
                _liveId = live;
                _liveLabel = live == null ? "" : "On screen: " + live;
            }
        }

        private static bool IsLivePage(Page p)
        {
            if (p == null || p.Step == null) return false;
            return string.Equals(p.Id, _liveId, StringComparison.Ordinal)
                || string.Equals(p.Id, _activeId, StringComparison.Ordinal)
                || string.Equals(p.Id, _previewStepId, StringComparison.Ordinal);
        }

        /// <summary>Would an edit to <paramref name="f"/> change what is on screen right now?</summary>
        private static bool AffectsScreen(Field f)
        {
            if (f == null) return false;
            if (f.Group != null && f.Group.Lesson != null
                && f.Key.StartsWith("lesson:", StringComparison.Ordinal))
            {
                // A lesson title (or lesson 3's tablet-track title, declared on one of its steps):
                // the strip header shows it for EVERY step of that lesson.
                Page lp;
                if (_liveId != null && _pageById != null && _pageById.TryGetValue(_liveId, out lp))
                    return ReferenceEquals(lp.Owner, f.Group);
                return false;
            }
            if (f.Page != null && f.Page.Step != null) return IsLivePage(f.Page);
            return _stripVisible || _cardOpen;   // chrome lines: whatever is showing may use them
        }

        private static void RefreshScreen(Field f)
        {
            if (!AffectsScreen(f)) return;
            try { TutorialDirector.RefreshActiveText(); }
            catch (Exception e)
            {
                if (_refreshWarned) return;
                _refreshWarned = true;
                UIALog.Warn("Lesson editor: RefreshActiveText failed - " + e.Message);
            }
        }

        /// <summary>2 Hz: lesson states, and the override count (a change made outside this window -
        /// the card's in-place fields, uiatutorial reset - re-labels the list).</summary>
        private static void PollSlow()
        {
            float now = Time.unscaledTime;
            if (now >= _nextLabelRefresh) { _nextLabelRefresh = now + LabelRefreshSeconds; _filterDirty = true; }
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;

            int n = SafeOverrideCount();
            if (n != _polledCount) { _polledCount = n; _version++; }

            if (_groups == null) return;
            for (int i = 0; i < _groups.Length; i++)
            {
                Group g = _groups[i];
                if (g.Lesson == null) continue;
                try
                {
                    TLessonState s = TutorialDirector.StateOf(g.Id);
                    g.StateLabel = StateName(s);
                    g.StateCol = StateColor(s);
                }
                catch { g.StateLabel = ""; }
            }
        }

        private static string StateName(TLessonState s)
        {
            switch (s)
            {
                case TLessonState.New: return "NEW";
                case TLessonState.Offered: return "OFFERED";
                case TLessonState.Active: return "IN PROGRESS";
                case TLessonState.Done: return "DONE";
                case TLessonState.Skipped: return "SKIPPED";
                case TLessonState.Learned: return "LEARNED";
                case TLessonState.Later: return "LATER";
                default: return "";
            }
        }

        private static Vector4 StateColor(TLessonState s)
        {
            switch (s)
            {
                case TLessonState.Active: return GoodCol;
                case TLessonState.Offered: return Accent;
                case TLessonState.New: return TextCol;
                default: return DimCol;
            }
        }

        // ================================================================ window

        private static void DrawWindow()
        {
            ImGui.SetNextWindowPos(new Vector2(12f, 40f), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(DefaultSize(), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(460f, 320f),
                new Vector2(Screen.width * 0.98f, Screen.height * 0.98f));
            if (_requestFocus) { ImGui.SetNextWindowFocus(); _requestFocus = false; }

            bool open = true;
            bool began = false;
            _anyFieldActive = false;
            try
            {
                bool visible = ImGui.Begin(WindowId, ref open, ImGuiWindowFlags.NoCollapse);
                began = true;
                if (visible) DrawContents();
                _hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows
                    | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                _focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            }
            catch (Exception e) { NoteError("window", e); }
            finally { if (began) ImGui.End(); }

            if (!open) CloseInternal(false, false);
        }

        /// <summary>First-use size: the left column of the screen, leaving the centre (the coach card,
        /// 720 px at the 1080p reference, scaled like its CanvasScaler: match 0.5) and the top-centre
        /// strip visible. ImGui remembers any move/resize afterwards (imgui.ini).</summary>
        private static Vector2 DefaultSize()
        {
            float scale = Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
            float side = (Screen.width - 720f * scale) * 0.5f - 24f;
            return new Vector2(Mathf.Clamp(side, 560f, 1000f), Mathf.Max(420f, Screen.height * 0.9f));
        }

        private static void DrawContents()
        {
            global::StationeersUIMod.Windows.HudEditorWindow.ClampWindowToScreen();
            DrawHeader();

            float avail = ImGui.GetContentRegionAvail().x;
            float listW = Mathf.Clamp(avail * 0.36f, 190f, 420f);
            ImGui.BeginChild("##uiatl_list", new Vector2(listW, 0f), true);
            try { DrawList(); }
            finally { ImGui.EndChild(); }
            ImGui.SameLine();
            ImGui.BeginChild("##uiatl_page", new Vector2(0f, 0f), true);
            try { DrawPage(); }
            finally { ImGui.EndChild(); }
        }

        private static void DrawHeader()
        {
            ImGui.TextColored(Accent, "LESSON EDITOR");
            ImGui.SameLine();
            ImGui.TextDisabled("dev only (uiadev)  -  F8 jumps to the lesson on screen");

            int n = SafeOverrideCount();
            if (n != _countShown) { _countShown = n; _countLabel = "Overrides: " + n; }
            ImGui.TextUnformatted(_countLabel);
            ImGui.SameLine();
            if (ImGui.Button("Save##uiatl")) DoSave();
            ImGui.SameLine();
            if (ImGui.Button("Export##uiatl")) DoExport();
            ImGui.SameLine();
            ImGui.TextDisabled("then run tools/bake-tutorial.ps1");

            // The store refuses every save (a TutorialText.xml it could not read, or uiareset): say so
            // up front and permanently - never let typing look saved. Cached string, no per-frame alloc.
            string blocked = SafeBlockedReason();
            if (blocked != null)
            {
                ImGui.PushTextWrapPos(0f);
                Colored(BadCol, blocked);
                ImGui.PopTextWrapPos();
                if (SafeLoadFailed())
                {
                    if (ImGui.SmallButton("Reload from disk##uiatl_reload")) DoReload();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Read TutorialText.xml again (after fixing or deleting it).");
                        ImGui.TextUnformatted("Edits made since it failed are discarded - they could not be saved.");
                        ImGui.EndTooltip();
                    }
                }
            }
            else ImGui.TextDisabled(_dirty ? "Autosave: on field commit (typing not saved yet)" : _savedLabel);
            if (_liveId != null)
            {
                Colored(GoodCol, _liveLabel);
                ImGui.SameLine();
                if (ImGui.SmallButton("Go to it##uiatl_live"))
                {
                    Page p;
                    if (_pageById != null && _pageById.TryGetValue(_liveId, out p)) Select(p, true);
                }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                ImGui.PushTextWrapPos(0f);
                Colored(_statusCol, _status);
                ImGui.PopTextWrapPos();
            }
            ImGui.Separator();
        }

        // ---------------------------------------------------------------- left: lesson list

        private static void DrawList()
        {
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##uiatl_search", "search: step id or any text", ref _search, SearchBytes))
                _filterDirty = true;
            if (ImGui.IsItemActive()) _anyFieldActive = true;
            if (ImGui.Checkbox("Edited only##uiatl", ref _editedOnly)) _filterDirty = true;
            RefilterIfNeeded();
            ImGui.Separator();

            ImGui.BeginChild("##uiatl_tree", new Vector2(0f, 0f), false);
            try
            {
                if (_groups == null || _groups.Length == 0)
                {
                    ImGui.TextDisabled("No lesson data (TutorialChapters is empty).");
                    return;
                }
                bool filtering = _editedOnly || _search.Length > 0;
                int shown = 0;
                for (int gi = 0; gi < _groups.Length; gi++)
                {
                    Group g = _groups[gi];
                    if (g.Visible.Count == 0) continue;
                    shown++;
                    if (filtering || ReferenceEquals(_openGroupOnce, g)) ImGui.SetNextItemOpen(true);
                    if (ReferenceEquals(_openGroupOnce, g)) _openGroupOnce = null;

                    if (g.Edited) ImGui.PushStyleColor(ImGuiCol.Text, WarnCol);
                    bool open = ImGui.TreeNodeEx(g.RowLabel, ImGuiTreeNodeFlags.None);
                    if (g.Edited) ImGui.PopStyleColor();
                    if (g.StateLabel.Length > 0)
                    {
                        ImGui.SameLine();
                        Colored(g.StateCol, g.StateLabel);
                    }
                    if (!open) continue;
                    try
                    {
                        for (int pi = 0; pi < g.Visible.Count; pi++)
                        {
                            Page p = g.Visible[pi];
                            bool sel = ReferenceEquals(p, _selPage);
                            bool live = IsLivePage(p);
                            bool tint = live || p.Edited;
                            if (tint) ImGui.PushStyleColor(ImGuiCol.Text, live ? GoodCol : WarnCol);
                            if (ImGui.Selectable(p.RowLabel, sel)) Select(p, false);
                            if (tint) ImGui.PopStyleColor();
                            if (sel && _scrollToSel) { ImGui.SetScrollHereY(0.35f); _scrollToSel = false; }
                        }
                    }
                    finally { ImGui.TreePop(); }
                }
                if (shown == 0) ImGui.TextDisabled("Nothing matches.");
                ImGui.Spacing();
                ImGui.TextDisabled("green = on screen   amber * = edited");
            }
            finally
            {
                // A filtered-out target must not scroll / pop open the list later, out of the blue.
                _scrollToSel = false;
                _openGroupOnce = null;
                ImGui.EndChild();
            }
        }

        private static void RefilterIfNeeded()
        {
            if (_groups == null) return;
            if (!_filterDirty && _filterVersion == _version) return;
            _filterDirty = false;
            _filterVersion = _version;
            RebuildLabels();

            bool hasSearch = !string.IsNullOrEmpty(_search);
            for (int gi = 0; gi < _groups.Length; gi++)
            {
                Group g = _groups[gi];
                g.Visible.Clear();
                if (g.Pages == null) continue;
                bool groupHit = hasSearch && (Has(g.Id) || Has(GroupTitle(g)));
                for (int pi = 0; pi < g.Pages.Length; pi++)
                {
                    Page p = g.Pages[pi];
                    if (_editedOnly && !p.Edited) continue;
                    if (hasSearch && !groupHit && !PageMatches(p)) continue;
                    g.Visible.Add(p);
                }
                // Only the lesson TITLE is edited: keep one page so the title stays reachable.
                if (g.Visible.Count == 0 && _editedOnly && g.TitleField != null && g.Pages.Length > 0
                    && SafeOverridden(g.TitleField.Key) && (!hasSearch || groupHit))
                    g.Visible.Add(g.Pages[0]);
            }
        }

        private static bool PageMatches(Page p)
        {
            if (Has(p.Id)) return true;
            if (p.Fields == null) return false;
            for (int i = 0; i < p.Fields.Length; i++)
            {
                Field f = p.Fields[i];
                if (Has(f.Key) || Has(SafeGet(f.Key))) return true;
            }
            return false;
        }

        private static bool Has(string s)
        {
            return s != null && _search.Length > 0 && s.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Row labels carry the edited marker and the step title, so they are rebuilt on a
        /// version change (and once a second for edits made elsewhere) - never per frame.</summary>
        private static void RebuildLabels()
        {
            for (int gi = 0; gi < _groups.Length; gi++)
            {
                Group g = _groups[gi];
                bool gEdited = g.TitleField != null && SafeOverridden(g.TitleField.Key);
                if (g.Pages != null)
                {
                    for (int pi = 0; pi < g.Pages.Length; pi++)
                    {
                        Page p = g.Pages[pi];
                        bool e = false;
                        for (int fi = 0; fi < p.Fields.Length; fi++)
                            if (SafeOverridden(p.Fields[fi].Key)) { e = true; break; }
                        p.Edited = e;
                        if (e) gEdited = true;
                        // Row text: the step's title; a step without one (a tip) shows the start of
                        // its first line; a loose-key page counts its lines.
                        string title = p.Step == null
                            ? p.Fields.Length + " line(s)"
                            : (p.TitleKey != null ? RowText(SafeGet(p.TitleKey))
                                : (p.Fields.Length > 0 ? Snippet(RowText(SafeGet(p.Fields[0].Key)), 40) : ""));
                        p.RowLabel = (p.Step == null ? "All lines" : p.Id)
                            + (title.Length > 0 ? "   " + title : "")
                            + (e ? "  *" : "")
                            + "###uiatl_p_" + p.Id;
                    }
                }
                g.Edited = gEdited;
                g.RowLabel = RowText(GroupTitle(g))
                    + (g.Lesson != null ? "  (" + g.Id + ")" : "")
                    + (gEdited ? "  *" : "")
                    + "###uiatl_g_" + g.Id;
            }
        }

        private static string GroupTitle(Group g)
        {
            if (g.Lesson == null) return g.FixedTitle ?? g.Id;
            string t = string.IsNullOrEmpty(g.Lesson.TitleKey) ? null : SafeGet(g.Lesson.TitleKey);
            return string.IsNullOrEmpty(t) ? g.Id : t;
        }

        /// <summary>A list row: one line, and no '#' (ImGui reads "##"/"###" in a label as id syntax).</summary>
        private static string RowText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('\n') < 0 && s.IndexOf('#') < 0) return s;
            return s.Replace('\n', ' ').Replace('#', ' ');
        }

        private static string Snippet(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "...";
        }

        // ---------------------------------------------------------------- right: the step

        private static void DrawPage()
        {
            Page p = _selPage;
            if (p == null)
            {
                ImGui.TextDisabled("Pick a step on the left.");
                ImGui.TextDisabled("Or play a lesson and press F8 while its strip or card is on screen.");
                return;
            }

            if (p.Owner.TitleField != null)
            {
                DrawField(p.Owner.TitleField);
                ImGui.Separator();
            }

            Colored(Accent, p.Step != null ? p.Id : GroupTitle(p.Owner));
            ImGui.SameLine();
            Colored(DimCol, p.HeaderNote);
            if (IsLivePage(p)) ImGui.TextColored(GoodCol, "ON SCREEN - updates as you type");

            if (p.Step != null)
            {
                if (ImGui.Button("Preview##uiatl")) DoPreview(p);
                ImGui.SameLine();
                if (ImGui.Button("Stop preview##uiatl")) DoStopPreview();
                ImGui.SameLine();
            }
            bool armed = ReferenceEquals(_armRevertPage, p) && Time.unscaledTime < _armUntil;
            string revertLabel = armed ? "Click again to revert##uiatl_rp"
                : (p.Step != null ? "Revert step##uiatl_rp" : "Revert all lines##uiatl_rp");
            if (armed) ImGui.PushStyleColor(ImGuiCol.Text, WarnCol);
            bool revertClicked = ImGui.Button(revertLabel);
            if (armed) ImGui.PopStyleColor();
            if (revertClicked)
            {
                if (armed) DoRevertPage(p);
                else { _armRevertPage = p; _armUntil = Time.unscaledTime + ArmSeconds; }
            }

            DrawTokenRow(p);
            ImGui.Separator();

            ImGui.BeginChild("##uiatl_fields", new Vector2(0f, 0f), false);
            try
            {
                if (_pageScrollReset) { ImGui.SetScrollY(0f); _pageScrollReset = false; }
                if (p.Fields.Length == 0) ImGui.TextDisabled("This step has no editable text.");
                for (int i = 0; i < p.Fields.Length; i++) DrawField(p.Fields[i]);
            }
            finally { ImGui.EndChild(); }
        }

        private static void DrawTokenRow(Page p)
        {
            Field target = _insertTarget;
            if (!BelongsTo(target, p)) { target = DefaultTarget(p); _insertTarget = target; }

            ImGui.TextDisabled("Insert a key into:");
            ImGui.SameLine();
            Colored(Accent, target != null ? target.Label : "-");
            if (_cbTried && _imCb == null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(at the end: caret tracking unavailable)");
            }

            var style = ImGui.GetStyle();
            float spacing = style.ItemSpacing.x;
            float pad = style.FramePadding.x * 2f;
            float avail = ImGui.GetContentRegionAvail().x;
            float x = 0f;
            for (int i = 0; i < Tokens.Length; i++)
            {
                Tok t = Tokens[i];
                float w = ImGui.CalcTextSize(t.Label).x + pad;
                if (i > 0 && x + spacing + w <= avail) { ImGui.SameLine(); x += spacing + w; }
                else x = w;
                if (ImGui.Button(t.ButtonLabel)) InsertToken(target, t.Code);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(t.Code);
                    ImGui.TextUnformatted(t.Note);
                    ImGui.TextUnformatted("Shows now as: " + SafeResolve(t.Code));   // on hover only
                    ImGui.EndTooltip();
                }
            }
            ImGui.TextDisabled("Write literally (no token): Shift, Ctrl, Esc, LEFT-CLICK, RIGHT-CLICK,");
            ImGui.TextDisabled("SCROLL WHEEL, the range 1 - 6, and digits pressed inside a wheel.");
        }

        private static void DrawField(Field f)
        {
            ImGui.PushID(f.Key);
            try
            {
                bool overridden = SafeOverridden(f.Key);
                Colored(Accent, f.Label);
                bool labelHovered = ImGui.IsItemHovered();
                // The key rides on the label line when it fits (room kept for EDITED + Revert);
                // in a narrow window it moves to the label's tooltip instead of clipping.
                float spacing = ImGui.GetStyle().ItemSpacing.x;
                float room = ImGui.GetContentRegionAvail().x - ImGui.CalcTextSize(f.Label).x - spacing
                    - (overridden ? ImGui.CalcTextSize("EDITED Revert").x + spacing * 4f : 0f);
                if (ImGui.CalcTextSize(f.KeyNote).x <= room)
                {
                    ImGui.SameLine();
                    Colored(DimCol, f.KeyNote);
                }
                else if (labelHovered)
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(f.Key);
                    ImGui.EndTooltip();
                }
                if (overridden)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(WarnCol, "EDITED");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Shipped text:");
                        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
                        ImGui.TextUnformatted(SafeDefault(f.Key));
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Revert")) RevertField(f);
                }

                if (!f.Active) f.Buffer = SafeGet(f.Key);
                if (ReferenceEquals(_refocusField, f)) { ImGui.SetKeyboardFocusHere(); _refocusField = null; }

                ImGuiInputTextCallback cb = TextCallback();
                ImGuiInputTextFlags flags = cb != null ? ImGuiInputTextFlags.CallbackAlways : ImGuiInputTextFlags.None;
                bool changed;
                _cbField = f;
                try
                {
                    if (f.SingleLine)
                    {
                        ImGui.SetNextItemWidth(-1f);
                        changed = cb != null
                            ? ImGui.InputText("##t", ref f.Buffer, FieldBytes, flags, cb)
                            : ImGui.InputText("##t", ref f.Buffer, FieldBytes, flags);
                    }
                    else
                    {
                        var size = new Vector2(-1f,
                            f.Lines * ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().FramePadding.y * 2f);
                        changed = cb != null
                            ? ImGui.InputTextMultiline("##t", ref f.Buffer, FieldBytes, size, flags, cb)
                            : ImGui.InputTextMultiline("##t", ref f.Buffer, FieldBytes, size, flags);
                    }
                }
                finally { _cbField = null; }
                if (f.Buffer == null) f.Buffer = "";

                bool active = ImGui.IsItemActive();
                bool committed = ImGui.IsItemDeactivatedAfterEdit();
                if (active) { _anyFieldActive = true; _insertTarget = f; }
                f.Active = active;
                if (changed) OnEdited(f);
                if (committed) OnCommitted(f);

                RefreshCaches(f, f.Buffer);
                Colored(f.Over ? WarnCol : DimCol, f.CountLabel);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Characters on screen, after key tokens resolve.");
                    ImGui.TextUnformatted("Raw text: " + f.RawLength + ". Budget: " + f.Budget + ".");   // on hover only
                    if (f.StandIns)
                    {
                        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
                        ImGui.TextUnformatted(TutorialLint.StandInsNote);
                        ImGui.PopTextWrapPos();
                    }
                    ImGui.EndTooltip();
                }
                ImGui.SameLine();
                if (f.Lint == null) ImGui.TextDisabled("lint OK");
                else Colored(WarnCol, f.Lint);

                ImGui.TextDisabled("on screen:");
                ImGui.SameLine();
                ImGui.PushTextWrapPos(0f);
                Colored(PreviewCol, f.PreviewText.Length > 0 ? f.PreviewText : "(empty)");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }
            finally { ImGui.PopID(); }
        }

        // ================================================================ edits

        private static void OnEdited(Field f)
        {
            try { TutorialTextStore.Set(f.Key, f.Buffer); }
            catch (Exception e) { Status("Could not store " + f.Key + ": " + e.Message, BadCol); return; }
            _dirty = true;
            _version++;
            RefreshScreen(f);
        }

        /// <summary>Focus left the field after an edit: autosave, then show what the store actually
        /// kept (it sanitises to ASCII, and empty / equal-to-shipped means "no override"). The
        /// message names the REAL reason - "stored as ASCII" only when the typed text had non-ASCII.</summary>
        private static void OnCommitted(Field f)
        {
            string typed = f.Buffer ?? "";
            SaveNow(null);
            string kept = SafeGet(f.Key);
            if (!string.Equals(kept, typed, StringComparison.Ordinal))
            {
                string why;
                if (typed.Length == 0) why = ": empty means the shipped text - restored.";
                else if (!string.Equals(SafeSanitize(typed), typed, StringComparison.Ordinal))
                    why = ": stored as ASCII (non-ASCII characters became '?').";
                else why = ": the store holds different text (changed elsewhere) - showing what it kept.";
                Status(f.Label + why, WarnCol);
            }
            f.Buffer = kept;
        }

        private static void RevertField(Field f)
        {
            try { TutorialTextStore.Revert(f.Key); }
            catch (Exception e) { Status("Revert failed: " + e.Message, BadCol); return; }
            f.Active = false;
            f.Buffer = SafeGet(f.Key);
            _version++;
            SaveNow(f.Label + " reverted to the shipped text (saved).");
            RefreshScreen(f);
        }

        private static void DoRevertPage(Page p)
        {
            _armRevertPage = null;
            int n = 0;
            bool screen = false;
            for (int i = 0; i < p.Fields.Length; i++)
            {
                Field f = p.Fields[i];
                if (!SafeOverridden(f.Key)) continue;
                try { TutorialTextStore.Revert(f.Key); n++; } catch { }
                f.Active = false;
                f.Buffer = SafeGet(f.Key);
                if (AffectsScreen(f)) screen = true;
            }
            _version++;
            SaveNow(n == 0 ? "Nothing to revert on " + p.Id + "."
                : "Reverted " + n + " line(s) on " + (p.Step != null ? p.Id : GroupTitle(p.Owner)) + " (saved).");
            if (screen)
            {
                try { TutorialDirector.RefreshActiveText(); } catch { }
            }
        }

        /// <summary>Insert a key token at the caret of <paramref name="f"/> (its last reported
        /// position), or at the end when the caret is unknown; then give the field the keyboard back
        /// with the caret just after the token.</summary>
        private static void InsertToken(Field f, string code)
        {
            if (f == null) { Status("Click into a field first, then pick a key.", WarnCol); return; }
            string cur = SafeGet(f.Key) ?? "";
            int pos = cur.Length;
            if (_imCb != null && ReferenceEquals(_caretField, f) && _caretPos >= 0)
                pos = Mathf.Clamp(_caretPos, 0, cur.Length);
            f.Buffer = cur.Substring(0, pos) + code + cur.Substring(pos);
            f.Active = false;
            OnEdited(f);
            SaveNow(null);
            _caretField = f;
            _caretPos = pos + code.Length;          // a second click chains after the first token
            if (_imCb != null)
            {
                _refocusField = f;
                _pendingCaretField = f;
                _pendingCaret = _caretPos;
            }
        }

        private static void DoSave()
        {
            SaveNow("Saved " + SafeOverrideCount() + " override(s) to config/StationeersUIMod/Tutorial/TutorialText.xml.");
        }

        /// <summary>Write the overrides to disk. <see cref="TutorialTextStore.Save"/> catches its own IO
        /// errors and returns false (read-only folder, a second game instance holding the file), so the
        /// result - not the absence of an exception - decides whether we may say "saved". A failure
        /// stays dirty, so the next commit / close retries.</summary>
        private static void SaveNow(string okMessage)
        {
            bool ok;
            try { ok = TutorialTextStore.Save(); }
            catch (Exception e)
            {
                ok = false;
                UIALog.Error("Lesson editor save failed: " + e);
            }
            if (!ok)
            {
                // A refusal (failed load / uiareset) is not an IO failure: say which it was.
                string blocked = SafeBlockedReason();
                Status(blocked != null ? "NOT SAVED - " + blocked
                    : "SAVE FAILED - config/StationeersUIMod/Tutorial/TutorialText.xml could not be written (see the log).", BadCol);
                return;
            }
            _dirty = false;
            _savedLabel = "Autosave: on field commit (saved " + DateTime.Now.ToString("HH:mm:ss") + ")";
            if (okMessage != null) Status(okMessage, GoodCol);
        }

        /// <summary>The failed-load way out: re-read TutorialText.xml (fixed or deleted by hand). Every
        /// field re-reads the store on the next frame; the screen is re-resolved at once.</summary>
        private static void DoReload()
        {
            bool ok;
            try { ok = TutorialTextStore.Reload(); }
            catch (Exception e)
            {
                ok = false;
                UIALog.Warn("Lesson editor: reload failed - " + e.Message);
            }
            _dirty = false;            // memory now mirrors the disk (or the shipped copy): nothing unsaved
            _version++;
            _filterDirty = true;
            ResetFieldActivity();      // !Active -> each field's buffer re-reads the store next frame
            try { TutorialDirector.RefreshActiveText(); } catch { }
            string still = SafeBlockedReason();   // uiareset keeps saving off even after a good read
            if (!ok) Status("TutorialText.xml is still unreadable - saving stays off (see the log).", BadCol);
            else if (still != null) Status("Reloaded TutorialText.xml. " + still, WarnCol);
            else Status("Reloaded TutorialText.xml - saving is back on.", GoodCol);
        }

        private static void DoExport()
        {
            if (_dirty) SaveNow(null);
            string path;
            TutorialCopyExport.Summary s;
            string error;
            if (!TutorialCopyExport.Export(out path, out s, out error))
            {
                Status("EXPORT FAILED - " + error, BadCol);
                return;
            }
            string msg = "Exported " + s.Pairs + " strings (" + s.Edited + " edited) to " + path
                + "  - then run tools/bake-tutorial.ps1";
            if (s.Orphans > 0) msg += "  (" + s.Orphans + " override(s) for keys the script no longer has were left out)";
            if (s.Duplicates > 0) msg += "  WARNING: duplicate key '" + s.FirstDuplicate + "' - bake will refuse this file";
            Status(msg, s.Duplicates > 0 ? WarnCol : GoodCol);
            try
            {
                global::Assets.Scripts.ConsoleWindow.Print("Lesson editor: " + msg,
                    s.Duplicates > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);
            }
            catch { }
        }

        private static void DoPreview(Page p)
        {
            bool ok;
            try { ok = TutorialDirector.PreviewStep(p.Id); }
            catch (Exception e) { Status("Preview failed: " + e.Message, BadCol); return; }
            if (!ok)
            {
                Status("Preview refused for " + p.Id + " (not in a world, or it cannot show right now).", WarnCol);
                return;
            }
            _previewStepId = p.Id;
            _previewOwned = true;
            _previewAt = Time.unscaledTime;
            Status("Previewing " + p.Id + " - it updates as you type. Stop preview (or closing the editor) ends it.", GoodCol);
        }

        private static void DoStopPreview()
        {
            try { TutorialDirector.StopPreview(); } catch { }
            _previewStepId = null;
            _previewOwned = false;
            Status("Preview stopped.", TextCol);
        }

        /// <summary>End the preview this window started, if any (idempotent on the Director side, so
        /// a preview that already ended by itself costs nothing).</summary>
        private static void StopOurPreview()
        {
            if (!_previewOwned && _previewStepId == null) return;
            try { TutorialDirector.StopPreview(); } catch { }
            _previewStepId = null;
            _previewOwned = false;
        }

        // ================================================================ caches

        private static void RefreshCaches(Field f, string text)
        {
            if (text == null) text = "";
            if (f.CacheFor != null && ReferenceEquals(f.CacheFor, text)) return;
            f.CacheFor = text;
            string clean = SafeSanitize(text);
            // Placeholders the director swaps in before resolving (chrome|reopen's {OPENER}, the tour
            // header's {N} / {TOTAL} / {TITLE}) are stood in for at their longest by the SAME helper
            // the lint measures with, so the counter, the "on screen" line and the lint line agree.
            string shown = SafeShownForBudget(f.Key, clean);
            f.StandIns = !string.Equals(shown, clean, StringComparison.Ordinal);
            string resolved = SafeResolve(shown);
            f.PreviewText = resolved;
            f.RawLength = text.Length;
            f.Over = resolved.Length > f.Budget;
            f.CountLabel = resolved.Length + " / " + f.Budget;
            try { f.Lint = TutorialLint.CheckField(f.Key, text, f.Budget); }
            catch (Exception e) { f.Lint = "lint failed: " + e.Message; }
        }

        private static void InvalidateCaches()
        {
            if (_groups == null) return;
            for (int gi = 0; gi < _groups.Length; gi++)
            {
                Group g = _groups[gi];
                if (g.TitleField != null) g.TitleField.CacheFor = null;
                if (g.Pages == null) continue;
                for (int pi = 0; pi < g.Pages.Length; pi++)
                    for (int fi = 0; fi < g.Pages[pi].Fields.Length; fi++) g.Pages[pi].Fields[fi].CacheFor = null;
            }
        }

        private static void ResetFieldActivity()
        {
            if (_groups == null) return;
            for (int gi = 0; gi < _groups.Length; gi++)
            {
                Group g = _groups[gi];
                if (g.TitleField != null) g.TitleField.Active = false;
                if (g.Pages == null) continue;
                for (int pi = 0; pi < g.Pages.Length; pi++)
                    for (int fi = 0; fi < g.Pages[pi].Fields.Length; fi++) g.Pages[pi].Fields[fi].Active = false;
            }
        }

        // ================================================================ caret callback

        /// <summary>ImGui.NET's input-text callback takes an <c>ImGuiInputTextCallbackData*</c>, and
        /// this assembly compiles without unsafe code (asmdef allowUnsafeCode=false), so the delegate
        /// is built from a two-instruction DynamicMethod (<c>ldarg.0; call OnTextCallback(IntPtr);
        /// ret</c> - a pointer and an IntPtr are both native int on the IL stack) and wrapped back in
        /// the safe <see cref="ImGuiInputTextCallbackDataPtr"/>. Built once; the native thunk is made up
        /// front so a runtime that cannot marshal it fails HERE, not inside an InputText call - and the
        /// delegate is DRY-CALLED once from managed code (a null data pointer, which
        /// <see cref="OnTextCallback"/> ignores): a DynamicMethod is JIT-compiled on its first call, and
        /// that must not be the first native igInputText callback, where a compile failure unwinds
        /// through ImGui's C frames instead of landing in this try/catch. Any failure = null = no caret
        /// tracking: tokens append at the end (the window says so).</summary>
        private static ImGuiInputTextCallback TextCallback()
        {
            if (_cbTried) return _imCb;
            _cbTried = true;
            try
            {
                MethodInfo invoke = typeof(ImGuiInputTextCallback).GetMethod("Invoke");
                ParameterInfo[] ps = invoke != null ? invoke.GetParameters() : null;
                if (ps == null || ps.Length != 1 || invoke.ReturnType != typeof(int))
                    throw new NotSupportedException("unexpected ImGuiInputTextCallback signature");
                MethodInfo target = typeof(TutorialEditorWindow).GetMethod("OnTextCallback",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null) throw new MissingMethodException("OnTextCallback");

                var dm = new DynamicMethod("UIA_TutorialEditor_TextCallback", typeof(int),
                    new Type[] { ps[0].ParameterType }, typeof(TutorialEditorWindow).Module, true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, target);
                il.Emit(OpCodes.Ret);
                var cb = (ImGuiInputTextCallback)dm.CreateDelegate(typeof(ImGuiInputTextCallback));
                Marshal.GetFunctionPointerForDelegate(cb);   // prove it marshals before ImGui ever sees it

                // The dry call. This assembly cannot spell a pointer argument in C# (no unsafe), so a
                // second DynamicMethod makes it: ldarg.0 (the callback); ldc.i4.0; conv.u (a NULL
                // ImGuiInputTextCallbackData*); callvirt Invoke; ret. Invoking it compiles the callback
                // above under THIS try/catch; OnTextCallback returns 0 at once for a null pointer.
                var probe = new DynamicMethod("UIA_TutorialEditor_TextCallbackProbe", typeof(int),
                    new Type[] { typeof(ImGuiInputTextCallback) }, typeof(TutorialEditorWindow).Module, true);
                ILGenerator pil = probe.GetILGenerator();
                pil.Emit(OpCodes.Ldarg_0);
                pil.Emit(OpCodes.Ldc_I4_0);
                pil.Emit(OpCodes.Conv_U);
                pil.Emit(OpCodes.Callvirt, invoke);
                pil.Emit(OpCodes.Ret);
                var dry = (Func<ImGuiInputTextCallback, int>)probe.CreateDelegate(typeof(Func<ImGuiInputTextCallback, int>));
                dry(cb);
                _imCb = cb;
            }
            catch (Exception e)
            {
                _imCb = null;
                UIALog.Warn("Lesson editor: caret tracking unavailable (tokens will append at the end) - " + e.Message);
            }
            return _imCb;
        }

        /// <summary>CallbackAlways: fires every frame while a field is being edited. Records the caret,
        /// and once (right after a token insert re-focused the field) moves it past the new token.</summary>
        private static int OnTextCallback(IntPtr data)
        {
            try
            {
                Field f = _cbField;
                if (f == null || data == IntPtr.Zero) return 0;
                var d = new ImGuiInputTextCallbackDataPtr(data);
                if (ReferenceEquals(_pendingCaretField, f) && _pendingCaret >= 0)
                {
                    int len = d.BufTextLen;
                    int c = _pendingCaret > len ? len : _pendingCaret;
                    d.CursorPos = c;
                    d.SelectionStart = c;
                    d.SelectionEnd = c;
                    _pendingCaretField = null;
                    _pendingCaret = -1;
                }
                _caretField = f;
                _caretPos = d.CursorPos;
            }
            catch { }
            return 0;
        }

        // ================================================================ small helpers

        /// <summary>Coloured text that is NEVER a printf format: ImGui.Text / TextColored /
        /// TextDisabled pass their string to igText(fmt, ...), so a '%' in lesson copy ("20%
        /// battery") would read garbage off the native stack. Every dynamic string goes through here
        /// (TextUnformatted); ImGui.Text* is used only for fixed strings with no '%'.</summary>
        private static void Colored(Vector4 col, string s)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, col);
            ImGui.TextUnformatted(s ?? "");
            ImGui.PopStyleColor();
        }

        private static void Status(string msg, Vector4 col)
        {
            _status = msg;
            _statusCol = col;
        }

        /// <summary>Console + toast: the refusals happen with the console open (uiatutorial edit) or
        /// from a key/button in the world, so say it in both places.</summary>
        private static void Say(string msg, bool warn)
        {
            try
            {
                global::Assets.Scripts.ConsoleWindow.Print(msg, warn ? ConsoleColor.Yellow : ConsoleColor.Cyan);
            }
            catch { }
            try
            {
                global::StationeersUIMod.Overlay.Toast.Show(msg,
                    warn ? global::StationeersUIMod.Overlay.Theme.Warn : global::StationeersUIMod.Overlay.Theme.TextPrimary, 3f);
            }
            catch { }
        }

        private static string SafeGet(string key)
        {
            try { return TutorialTextStore.Get(key) ?? ""; }
            catch { return ""; }
        }

        private static string SafeDefault(string key)
        {
            try { return TutorialTextStore.Default(key) ?? ""; }
            catch { return ""; }
        }

        private static bool SafeOverridden(string key)
        {
            try { return TutorialTextStore.IsOverridden(key); }
            catch { return false; }
        }

        private static int SafeOverrideCount()
        {
            try { return TutorialTextStore.OverrideCount; }
            catch { return 0; }
        }

        private static string SafeBlockedReason()
        {
            try { return TutorialTextStore.SaveBlockedReason; }
            catch { return null; }
        }

        private static bool SafeLoadFailed()
        {
            try { return TutorialTextStore.LoadFailed; }
            catch { return false; }
        }

        private static string SafeSanitize(string s)
        {
            try { return TutorialTextStore.Sanitize(s) ?? ""; }
            catch { return s ?? ""; }
        }

        private static string SafeResolve(string s)
        {
            try { return TutorialTokens.Resolve(s) ?? ""; }
            catch { return s ?? ""; }
        }

        private static string SafeShownForBudget(string key, string s)
        {
            try { return TutorialLint.ShownForBudget(key, s) ?? ""; }
            catch { return s ?? ""; }
        }

        private static void NoteError(string where, Exception e)
        {
            _errors++;
            float now = Time.unscaledTime;
            if (_errors <= 3 || now - _errorLogAt >= 5f)
            {
                _errorLogAt = now;
                UIALog.Warn("Lesson editor " + where + " failed: " + e);
            }
        }
    }
}
