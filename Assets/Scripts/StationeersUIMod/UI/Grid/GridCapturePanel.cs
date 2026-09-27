using System;
using System.Collections.Generic;
using Assets.Scripts;              // CursorManager (world-pick block while the panel is up)
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The GENERALIZING-CAPTURE confirm panel (design O3b/O3c): pressing CAPTURE on a bag region
    /// in profile mode opens this themed dialog over the grid — a name input, one toggleable row
    /// per proposed rule (with a per-cluster "AS ITEMS" un-generalize toggle), and, when the bag
    /// already has a profile, a NEW / UPDATE / MERGE mode row. SAVE runs
    /// <see cref="ProfileCapture.Apply"/> (which writes the profile XML and AUTO-ASSIGNS it to the
    /// captured bag — FlorpyDorp Q3) and bumps <see cref="GridProfileMode.Version"/> so the grid's
    /// chips + badges refresh. CANCEL / Esc (TheGridPanel's chain) just closes. Everything here is
    /// CONFIG/PROFILE state — no game-state mutation is reachable from this panel.
    ///
    /// <para>Lifetime: TRANSIENT BY CONSTRUCTION — the whole canvas is built on <see cref="Open"/>
    /// and destroyed on <see cref="Close"/> (<see cref="Shutdown"/> IS Close), so nothing pooled
    /// can strand across a hot reload. A dimmed full-screen scrim blocks every click to the
    /// windows below (clicking it does NOT cancel — an accidental cancel would lose the row
    /// toggles); vanilla's world-pick is held off while interactive (<c>BlockCursorRaycast</c>,
    /// re-asserted per frame, released on Close). Cursor: registers NO modal — the panel opens
    /// from an interactive click and simply waits if the player re-locks the mouse.</para>
    ///
    /// <para>All displayed strings are ASCII (checkbox marks are "X" in a DRAWN box — never a
    /// dingbat; proposal display names come from ProfileCapture, ASCII-sanitised on save). Known
    /// wart, flagged in the change report: while the TMP name input is focused, vanilla still
    /// sees the keystrokes (KeyManager only gates on the game console), so typing WASD can walk
    /// the player — the field is prefilled with the bag's name precisely so typing is optional.</para>
    /// </summary>
    public static class GridCapturePanel
    {
        // --- layout constants (px, screen-space overlay) ---
        private const float PanelW = 440f;
        private const float PadX = 12f;
        private const float TitleBandH = 22f;
        private const float FieldH = 24f;
        private const float ModeBtnH = 20f;
        private const float RowH = 20f;
        private const float RowGap = 2f;
        private const float BtnH = 24f;
        private const float LinesMaxH = 300f;
        /// <summary>Top-most Grid surface: above the chip popup (5150) — the capture dialog is the
        /// most modal thing the Grid family shows.</summary>
        private const int SortOrder = 5200;

        // ---- F10 hosting (0.9.8.0 Organizer, ANSWER #10: ONE capture feature, not two) ----
        // The SAME panel can open ABOVE the F10 Control Center (its canvas is 5200, so the
        // Organizer passes 5300). Hosted mode is self-pumped by a MonoBehaviour on the root
        // (TheGridPanel only pumps Tick while the Grid window is open) and reports its Close
        // through _onClosed so the F10 tab can re-read its listings.
        private static int _sortOrder = SortOrder;
        private static bool _hosted;
        private static Action _onClosed;

        private static GameObject _root;
        private static RectTransform _panel;
        private static PanelGraphic _panelBg;
        private static TextMeshProUGUI _title;

        private static PanelGraphic _nameBg;
        private static TMP_InputField _nameInput;
        private static TextMeshProUGUI _nameLabel;
        private static TextMeshProUGUI _nameText;
        private static TextMeshProUGUI _namePlaceholder;

        private static TextMeshProUGUI _modeHint;
        private static TextMeshProUGUI _emptyLabel;

        private static CaptureProposal _proposal;
        private static string _assignedName;   // the bag's current (existing) profile, null = none
        private static CaptureApplyMode _mode;
        private static string _typedName;      // preserves the NEW-mode name across mode switches

        private static bool _open;
        private static bool _blockHeld;
        private static float _panelH;

        // Esc protection (verified UX finding, 2026-07-20): the dialog deliberately survives a
        // scrim click and a mouse re-lock to protect the row toggles, so one reflexive Esc must
        // not silently discard them either. _dirty = the player invested state since Open;
        // _escArmed = the first Esc has been seen and the title now shows the discard cue.
        private static bool _dirty;
        private static bool _escArmed;

        // Every graphic that went through GridTheme.ApplyBox may hold a shared FX material and
        // must be handed back before the destroy (the HudFxMaterials registry keys on the Graphic).
        private static readonly List<PanelGraphic> _fxBoxes = new List<PanelGraphic>(8);

        /// <summary>One themed button (bg + click + label).</summary>
        private sealed class Btn
        {
            public PanelGraphic Bg;
            public CaptureClick Click;
            public TextMeshProUGUI Label;
            public float W;
            public float H;
        }

        private static Btn _saveBtn;
        private static Btn _cancelBtn;
        private static Btn _modeNew;
        private static Btn _modeUpdate;
        private static Btn _modeMerge;

        /// <summary>One proposal row's live widgets (the CaptureLine is the model; toggles write
        /// it directly and repaint only their own marks).</summary>
        private sealed class LineRow
        {
            public CaptureLine Line;
            public PanelGraphic CheckBox;
            public TextMeshProUGUI CheckMark;
            public TextMeshProUGUI Label;
            public PanelGraphic GenBox;
            public TextMeshProUGUI GenMark;
            public TextMeshProUGUI GenLabel;
        }

        private static readonly List<LineRow> _lineRows = new List<LineRow>(16);

        /// <summary>True while the dialog is on screen (TheGridPanel's Esc chain checks it).</summary>
        public static bool IsOpen { get { return _open; } }

        /// <summary>True while the dialog's name field has keyboard focus. TheGridPanel's Esc
        /// chain skips its close then, so TMP's own built-in Esc handling just deselects the
        /// field instead of stacking a deselect AND a dialog close on one press.</summary>
        public static bool IsNameInputFocused
        {
            get { return _open && _nameInput != null && _nameInput.isFocused; }
        }

        /// <summary>Has the player invested state worth protecting — row/AS-ITEMS toggles, a
        /// mode pick, or an EDITED NAME? The ONE predicate every discard-adjacent path uses:
        /// EscClose's arm decision AND the hosted pump's F10-went-away auto-close. They
        /// diverged once (the auto-close tested only <c>_dirty</c>) and a typed-but-untoggled
        /// name was silently discarded when F10 closed under the dialog (fix wave finding 2).</summary>
        private static bool IsInvested()
        {
            if (_dirty) return true;
            if (_mode == CaptureApplyMode.NewProfile && _nameInput != null && _proposal != null)
            {
                string baseline = _proposal.SuggestedName ?? "";
                if ((_nameInput.text ?? "") != baseline) return true;
            }
            return false;
        }

        /// <summary>Esc pressed while the dialog is open (TheGridPanel's chain, or the hosted
        /// pump's own key read). An untouched dialog closes at once; once the player has
        /// invested state (<see cref="IsInvested"/>) the FIRST Esc only ARMS the discard — the
        /// title becomes the confirmation cue — and the second Esc actually closes. This is the
        /// same protect-the-toggles stance the scrim (clicking it does not cancel) and the
        /// mouse-relock survival already take; the CANCEL button stays the one-click discard.</summary>
        public static void EscClose()
        {
            if (!_open) return;
            if (!IsInvested() || _escArmed) { Close(); return; }
            _escArmed = true;
            if (_title != null) HudText.Set(_title, "Press Esc again to discard this capture");
        }

        /// <summary>The player touched capture state: remember it for the Esc guard, and if a
        /// discard was armed, disarm it (they clearly kept working) and restore the title.</summary>
        private static void MarkDirty()
        {
            _dirty = true;
            if (_escArmed)
            {
                _escArmed = false;
                if (_title != null && _proposal != null)
                    HudText.Set(_title, "Capture profile: " + (_proposal.SuggestedName ?? "Bag"));
            }
        }

        /// <summary>Build the proposal for <paramref name="bag"/> and open the dialog. A bag with
        /// no usable slots is a silent no-op; an EMPTY bag opens with a "nothing to capture" line
        /// (SAVE stays disabled). Re-opening rebuilds from scratch.</summary>
        public static void Open(DynamicThing bag)
        {
            OpenInternal(bag, SortOrder, false, null);
        }

        /// <summary>Open the SAME dialog hosted over another surface — the F10 Organizer passes
        /// its above-F10 sort order (5300) and a callback fired when the dialog closes (saved OR
        /// cancelled), so the caller can re-read its profile listings. Grid behaviour is
        /// untouched: the plain <see cref="Open(DynamicThing)"/> path never sets hosted mode.</summary>
        public static void Open(DynamicThing bag, int sortOrder, Action onClosed)
        {
            OpenInternal(bag, sortOrder, true, onClosed);
        }

        private static void OpenInternal(DynamicThing bag, int sortOrder, bool hosted, Action onClosed)
        {
            Close();
            _sortOrder = sortOrder;
            _hosted = hosted;
            _onClosed = onClosed;
            // Capture ALWAYS auto-assigns the profile it writes (design Q3), so it is an assignment
            // surface and takes the same gate as the chip popup (redesign plan Q5). The CAPTURE
            // button only exists on an assignable region now; this re-gates the public entry point.
            if (!BagProfileGate.IsAssignableContainer(bag)) { AbortOpen(); return; }
            CaptureProposal p = null;
            try { p = ProfileCapture.BuildProposal(bag); } catch { }
            if (p == null) { AbortOpen(); return; }
            _proposal = p;

            string assigned = null;
            try { assigned = BagProfileStore.GetAssignedProfileName(bag); } catch { }
            // A dangling assignment (profile deleted) offers no UPDATE/MERGE target — treat as none.
            if (!string.IsNullOrEmpty(assigned) && BagProfileStore.FindProfile(assigned) == null)
                assigned = null;
            _assignedName = string.IsNullOrEmpty(assigned) ? null : assigned;
            _mode = CaptureApplyMode.NewProfile;
            _typedName = p.SuggestedName;

            BuildUi();
            _open = _root != null;
            // Hosted mode has no TheGridPanel pump, so the panel drives itself: live theme,
            // cursor block, Esc, and the F10-went-away auto-close all run from this component.
            if (_open && _hosted) _root.AddComponent<HostedPump>();
        }

        /// <summary>A silent-no-op Open (gate failed / empty proposal): drop the hosted fields
        /// so a stale callback can never fire from a LATER close of an unrelated dialog.</summary>
        private static void AbortOpen()
        {
            _sortOrder = SortOrder;
            _hosted = false;
            _onClosed = null;
        }

        /// <summary>Pumped by TheGridPanel every open Tick: auto-close when the window/mode goes
        /// away or the F9 editor takes over, restyle to the live theme, hold the world-pick block
        /// while interactive. The panel deliberately SURVIVES a re-locked mouse (typed state must
        /// not be lost to a slipped mouse-control key) — clicks are impossible then anyway.</summary>
        public static void Tick()
        {
            if (!_open) return;
            // Hosted-over-F10: the HostedPump owns the lifetime — the Grid's pump must not
            // apply its own auto-close rules (the Grid window may well be closed).
            if (_hosted) return;
            if (_root == null) { _open = false; return; }
            if (!TheGridPanel.IsOpen || !GridProfileMode.Active || TheGridPanel.IsEditPreview)
            {
                Close();
                return;
            }

            StylePanel();

            if (TheGridPanel.IsInteractive)
            {
                _blockHeld = true;
                Core.CursorBlockArbiter.Hold("capture");
            }
            else ReleaseBlock();
        }

        /// <summary>Tear the dialog down (idempotent): unassign every ApplyBox'd graphic BEFORE the
        /// destroy (no dead HudFxMaterials keys), release the world-pick block, null every handle.</summary>
        public static void Close()
        {
            // Stash-and-null FIRST: the callback runs after teardown, and a re-entrant Open
            // from inside it must never see (or re-fire) the old callback.
            Action closed = _onClosed;
            _onClosed = null;
            _hosted = false;
            _sortOrder = SortOrder;
            _open = false;
            _proposal = null;
            _assignedName = null;
            _typedName = null;
            _dirty = false;
            _escArmed = false;
            _mode = CaptureApplyMode.NewProfile;
            _lineRows.Clear();
            for (int i = 0; i < _fxBoxes.Count; i++)
            {
                if (_fxBoxes[i] != null) HudFxMaterials.Unassign(_fxBoxes[i]);
            }
            _fxBoxes.Clear();
            ReleaseBlock();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _panel = null;
            _panelBg = null;
            _title = null;
            _nameBg = null;
            _nameInput = null;
            _nameLabel = null;
            _nameText = null;
            _namePlaceholder = null;
            _modeHint = null;
            _emptyLabel = null;
            _saveBtn = null;
            _cancelBtn = null;
            _modeNew = null;
            _modeUpdate = null;
            _modeMerge = null;
            _panelH = 0f;
            // Last: tell the hosting surface (the F10 Organizer) the dialog is gone, whether
            // saved or cancelled — it re-reads its listings either way. Fail-soft.
            if (closed != null)
            {
                try { closed(); } catch (Exception e) { UIALog.Warn("Capture onClosed failed: " + e.Message); }
            }
        }

        /// <summary>Hot-reload teardown — the dialog is transient, so this IS <see cref="Close"/>.</summary>
        public static void Shutdown()
        {
            Close();
        }

        // ---------- build ----------

        private static void BuildUi()
        {
            _root = new GameObject("UIAscended_GridCapture");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortOrder;   // 5200 over the Grid, 5300 when hosted over F10
            // The extra vertex streams the shared glass materials consume (the Window-surface
            // shell may ride the analytic SDF renderer) — the standard Grid canvas opt-in; see
            // TheGridPanel.EnsureBuilt for the full rationale.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
            _root.AddComponent<GraphicRaycaster>();

            // Dimmed scrim: blocks (and slightly darkens) everything under the dialog. Clicking it
            // does NOT cancel — the row toggles are user state worth protecting.
            var sGo = new GameObject("Scrim", typeof(RectTransform));
            sGo.transform.SetParent(_root.transform, false);
            var srt = (RectTransform)sGo.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var sImg = sGo.AddComponent<Image>();
            sImg.color = new Color(0f, 0f, 0f, 0.35f);
            sImg.raycastTarget = true;

            var pGo = new GameObject("Panel", typeof(RectTransform));
            pGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)pGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panelBg = pGo.AddComponent<PanelGraphic>();
            _panelBg.raycastTarget = true;
            _fxBoxes.Add(_panelBg);

            float innerW = PanelW - PadX * 2f;
            float y = 10f;

            _title = HudText.Make(_panel, "Title", HudText.Size(13f),
                TextAlignmentOptions.Left, warp: false);
            _title.overflowMode = TextOverflowModes.Truncate;
            SeatTopLeft(_title.rectTransform, PadX, y, innerW, TitleBandH);
            HudText.Set(_title, "Capture profile: " + (_proposal.SuggestedName ?? "Bag"));
            y += TitleBandH + 6f;

            _nameLabel = HudText.Make(_panel, "NameLabel", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            SeatTopLeft(_nameLabel.rectTransform, PadX, y, 44f, FieldH);
            HudText.Set(_nameLabel, "Name");
            BuildNameInput(PadX + 48f, y, innerW - 48f, FieldH);
            y += FieldH + 6f;

            if (_assignedName != null)
            {
                // O3c: the bag already has a profile — offer Save-as-new / Update / Merge. The
                // trio is right-aligned; the hint names the existing target.
                float trioW = 48f + 6f + 66f + 6f + 60f;
                _modeHint = HudText.Make(_panel, "ModeHint", HudText.Size(10f),
                    TextAlignmentOptions.Left, warp: false);
                _modeHint.overflowMode = TextOverflowModes.Truncate;
                SeatTopLeft(_modeHint.rectTransform, PadX, y, innerW - trioW - 8f, ModeBtnH);
                HudText.Set(_modeHint, "Existing: " + _assignedName);

                float bx = PadX + innerW - trioW;
                _modeNew = MakeBtn("NEW", 48f, ModeBtnH, SelectModeNew);
                Seat((RectTransform)_modeNew.Bg.transform, bx, y, 48f, ModeBtnH);
                _modeUpdate = MakeBtn("UPDATE", 66f, ModeBtnH, SelectModeUpdate);
                Seat((RectTransform)_modeUpdate.Bg.transform, bx + 48f + 6f, y, 66f, ModeBtnH);
                _modeMerge = MakeBtn("MERGE", 60f, ModeBtnH, SelectModeMerge);
                Seat((RectTransform)_modeMerge.Bg.transform, bx + 48f + 6f + 66f + 6f, y, 60f, ModeBtnH);
                y += ModeBtnH + 6f;
            }

            float contentH = _proposal.TotalItems == 0
                ? RowH
                : _proposal.Lines.Count * (RowH + RowGap);
            float linesH = Mathf.Min(Mathf.Max(RowH + 4f, contentH + 4f), LinesMaxH);
            BuildLines(PadX, y, innerW, linesH);
            y += linesH + 8f;

            _cancelBtn = MakeBtn("CANCEL", 76f, BtnH, DoCancel);
            _saveBtn = MakeBtn("SAVE PROFILE", 116f, BtnH, DoSave);
            float cx = PadX + innerW - 76f;
            Seat((RectTransform)_cancelBtn.Bg.transform, cx, y, 76f, BtnH);
            Seat((RectTransform)_saveBtn.Bg.transform, cx - 6f - 116f, y, 116f, BtnH);
            y += BtnH + 10f;

            _panelH = y;
            float px = (Screen.width - PanelW) * 0.5f;
            float py = (Screen.height - _panelH) * 0.5f;
            px = Mathf.Max(0f, px);
            py = Mathf.Max(0f, py);
            _panel.anchoredPosition = new Vector2(px + PanelW * 0.5f, -(py + _panelH * 0.5f));
            _panel.sizeDelta = new Vector2(PanelW, _panelH);

            ApplyModeVisuals();
            StylePanel();
        }

        private static void BuildNameInput(float x, float y, float w, float hh)
        {
            var rt = MakeRect("NameInput", _panel);
            Seat(rt, x, y, w, hh);
            _nameBg = rt.gameObject.AddComponent<PanelGraphic>();
            _nameBg.raycastTarget = true;
            _fxBoxes.Add(_nameBg);

            _nameInput = rt.gameObject.AddComponent<TMP_InputField>();
            _nameInput.targetGraphic = _nameBg;
            _nameInput.transition = Selectable.Transition.None;   // ApplyBox owns the look

            var areaGo = new GameObject("Area", typeof(RectTransform));
            areaGo.transform.SetParent(rt, false);
            var area = (RectTransform)areaGo.transform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.offsetMin = new Vector2(6f, 2f);
            area.offsetMax = new Vector2(-6f, -2f);
            areaGo.AddComponent<RectMask2D>();

            _namePlaceholder = HudText.Make(area, "Placeholder", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            FillRect(_namePlaceholder.rectTransform);
            HudText.Set(_namePlaceholder, "Profile name");

            _nameText = HudText.Make(area, "Text", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            FillRect(_nameText.rectTransform);

            _nameInput.textViewport = area;
            _nameInput.textComponent = _nameText;
            _nameInput.placeholder = _namePlaceholder;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.characterLimit = 40;   // matches ProfileCapture.SanitizeName's cap
            _nameInput.customCaretColor = true;
            _nameInput.text = _typedName ?? "";
        }

        private static void BuildLines(float x, float y, float w, float hh)
        {
            var vp = MakeRect("LinesViewport", _panel);
            Seat(vp, x, y, w, hh);
            vp.gameObject.AddComponent<RectMask2D>();

            // Wheel/drag hit for the whole list area (the ScrollRect dispatch idiom every Grid
            // scroll view uses — a RectMask2D alone receives nothing over empty area).
            var hGo = new GameObject("ScrollHit", typeof(RectTransform));
            hGo.transform.SetParent(vp, false);
            var hrt = (RectTransform)hGo.transform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;
            var hImg = hGo.AddComponent<Image>();
            hImg.color = new Color(0f, 0f, 0f, 0f);
            hImg.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vp, false);
            var content = (RectTransform)cGo.transform;
            content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;

            var scroll = vp.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.inertia = false;

            if (_proposal.TotalItems == 0)
            {
                _emptyLabel = HudText.Make(content, "Empty", HudText.Size(11f),
                    TextAlignmentOptions.Left, warp: false);
                SeatTopLeft(_emptyLabel.rectTransform, 4f, 2f, w - 8f, RowH);
                HudText.Set(_emptyLabel, "Nothing to capture - this bag is empty.");
                content.sizeDelta = new Vector2(w, RowH + 4f);
                return;
            }

            float ry = 0f;
            for (int i = 0; i < _proposal.Lines.Count; i++)
            {
                CaptureLine line = _proposal.Lines[i];
                if (line == null) continue;
                _lineRows.Add(BuildLineRow(content, line, w, ry));
                ry += RowH + RowGap;
            }
            content.sizeDelta = new Vector2(w, ry);
        }

        private static LineRow BuildLineRow(RectTransform content, CaptureLine line, float w, float ry)
        {
            var row = new LineRow();
            row.Line = line;

            var rGo = new GameObject("Line", typeof(RectTransform));
            rGo.transform.SetParent(content, false);
            var rrt = (RectTransform)rGo.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -ry);
            rrt.sizeDelta = new Vector2(w, RowH);

            bool canGen = line.CanGeneralize;
            float genZoneW = canGen ? 96f : 0f;

            // Include toggle: the whole left span of the row is the click target (a 13px box would
            // be a miserable target), with the drawn checkbox + label riding on top.
            var incGo = new GameObject("IncludeHit", typeof(RectTransform));
            incGo.transform.SetParent(rrt, false);
            var irt = (RectTransform)incGo.transform;
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(w - genZoneW, 0f);
            var iImg = incGo.AddComponent<Image>();
            iImg.color = new Color(0f, 0f, 0f, 0f);
            iImg.raycastTarget = true;
            var iClick = incGo.AddComponent<CaptureClick>();
            iClick.Clicked = delegate { ToggleInclude(row); };

            row.CheckBox = MakeCheckBox(rrt, 13f, out row.CheckMark);
            Seat((RectTransform)row.CheckBox.transform, 4f, (RowH - 13f) * 0.5f, 13f, 13f);

            row.Label = HudText.Make(rrt, "Label", HudText.Size(11f),
                TextAlignmentOptions.Left, warp: false);
            row.Label.overflowMode = TextOverflowModes.Truncate;
            SeatTopLeft(row.Label.rectTransform, 24f, 0f, w - genZoneW - 28f, RowH);
            HudText.Set(row.Label, BuildLineText(line));

            if (canGen)
            {
                // The un-generalize toggle: "AS ITEMS [ ]" — checked = save the raw member item
                // rules instead of the category/slot-class rule (Generalized == false).
                var gGo = new GameObject("GenHit", typeof(RectTransform));
                gGo.transform.SetParent(rrt, false);
                var grt = (RectTransform)gGo.transform;
                grt.anchorMin = new Vector2(1f, 0f);
                grt.anchorMax = new Vector2(1f, 1f);
                grt.pivot = new Vector2(1f, 0.5f);
                grt.anchoredPosition = Vector2.zero;
                grt.sizeDelta = new Vector2(genZoneW, 0f);
                var gImg = gGo.AddComponent<Image>();
                gImg.color = new Color(0f, 0f, 0f, 0f);
                gImg.raycastTarget = true;
                var gClick = gGo.AddComponent<CaptureClick>();
                gClick.Clicked = delegate { ToggleGeneralize(row); };

                row.GenLabel = HudText.Make(rrt, "GenLabel", HudText.Size(9f),
                    TextAlignmentOptions.Right, warp: false);
                SeatTopLeft(row.GenLabel.rectTransform, w - genZoneW, 0f, genZoneW - 20f, RowH);
                HudText.Set(row.GenLabel, "AS ITEMS");

                row.GenBox = MakeCheckBox(rrt, 11f, out row.GenMark);
                Seat((RectTransform)row.GenBox.transform, w - 16f, (RowH - 11f) * 0.5f, 11f, 11f);
            }

            UpdateRowMarks(row);
            return row;
        }

        // ---------- interactions ----------

        private static void ToggleInclude(LineRow row)
        {
            if (row == null || row.Line == null) return;
            row.Line.Included = !row.Line.Included;
            MarkDirty();
            UpdateRowMarks(row);
        }

        private static void ToggleGeneralize(LineRow row)
        {
            if (row == null || row.Line == null || !row.Line.CanGeneralize) return;
            row.Line.Generalized = !row.Line.Generalized;
            MarkDirty();
            UpdateRowMarks(row);
        }

        private static void UpdateRowMarks(LineRow row)
        {
            if (row.CheckMark != null) HudText.Set(row.CheckMark, row.Line.Included ? "X" : "");
            if (row.GenMark != null) HudText.Set(row.GenMark, row.Line.Generalized ? "" : "X");
        }

        private static void SelectModeNew() { SelectMode(CaptureApplyMode.NewProfile); }
        private static void SelectModeUpdate() { SelectMode(CaptureApplyMode.Update); }
        private static void SelectModeMerge() { SelectMode(CaptureApplyMode.Merge); }

        private static void SelectMode(CaptureApplyMode m)
        {
            if (_mode == m) return;
            if (_mode == CaptureApplyMode.NewProfile && _nameInput != null)
                _typedName = _nameInput.text;   // preserve what the user typed for a return to NEW
            _mode = m;
            MarkDirty();
            ApplyModeVisuals();
        }

        /// <summary>UPDATE/MERGE target the EXISTING profile by name, so the input shows it and
        /// locks; NEW restores the editable typed name.</summary>
        private static void ApplyModeVisuals()
        {
            if (_nameInput == null) return;
            bool isNew = _mode == CaptureApplyMode.NewProfile;
            _nameInput.interactable = isNew;
            _nameInput.text = isNew ? (_typedName ?? "") : (_assignedName ?? "");
        }

        private static void DoCancel()
        {
            Close();
        }

        private static void DoSave()
        {
            CaptureProposal p = _proposal;
            if (p == null) { Close(); return; }
            if (p.TotalItems == 0 || !p.HasIncludedLines) return;   // SAVE shows disabled; ignore

            string name = _mode == CaptureApplyMode.NewProfile
                ? (_nameInput != null ? _nameInput.text : null)
                : _assignedName;
            CaptureApplyMode mode = _mode;

            string finalName = null;
            try { finalName = ProfileCapture.Apply(p, name, mode); }
            catch (Exception ex) { UIALog.Warn("Capture apply failed: " + ex.Message); }

            Close();
            if (!string.IsNullOrEmpty(finalName)) GridProfileMode.BumpVersion();   // chips + badges refresh
        }

        // ---------- styling (per Tick; every setter downstream is equality-guarded) ----------

        private static void StylePanel()
        {
            if (_panelBg == null) return;
            GridTheme.ApplyBox(_panelBg, PanelW, _panelH, GridTheme.GridSurface.Window, false);

            Color text = GridTheme.Text;
            Color muted = HudPalette.TextLabel != null ? HudPalette.TextLabel.Value : text;
            Color accent = HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : text;
            Color border = GridTheme.Border;

            if (_title != null) _title.color = text;
            if (_nameLabel != null) _nameLabel.color = muted;
            if (_nameBg != null)
                GridTheme.ApplyBox(_nameBg, ((RectTransform)_nameBg.transform).sizeDelta.x, FieldH,
                    GridTheme.GridSurface.Button, _nameInput != null && _nameInput.isFocused);
            if (_nameInput != null)
            {
                _nameInput.caretColor = accent;
                _nameInput.selectionColor = new Color(accent.r, accent.g, accent.b, 0.3f);
            }
            if (_nameText != null)
                _nameText.color = _mode == CaptureApplyMode.NewProfile ? text : muted;
            if (_namePlaceholder != null)
                _namePlaceholder.color = new Color(muted.r, muted.g, muted.b, muted.a * 0.5f);
            if (_modeHint != null) _modeHint.color = muted;
            if (_emptyLabel != null) _emptyLabel.color = muted;

            StyleBtn(_cancelBtn, true, text, accent);
            bool saveOk = _proposal != null && _proposal.TotalItems > 0 && _proposal.HasIncludedLines;
            StyleBtn(_saveBtn, saveOk, text, accent);

            StyleModeBtn(_modeNew, CaptureApplyMode.NewProfile, text, accent);
            StyleModeBtn(_modeUpdate, CaptureApplyMode.Update, text, accent);
            StyleModeBtn(_modeMerge, CaptureApplyMode.Merge, text, accent);

            for (int i = 0; i < _lineRows.Count; i++)
            {
                var r = _lineRows[i];
                if (r == null || r.Line == null) continue;
                bool inc = r.Line.Included;
                if (r.Label != null)
                {
                    Color c = inc ? text : muted;
                    if (!inc) c.a *= 0.6f;
                    r.Label.color = c;
                }
                StyleCheck(r.CheckBox, 13f, inc, border, accent);
                if (r.CheckMark != null) r.CheckMark.color = accent;
                if (r.GenBox != null)
                {
                    StyleCheck(r.GenBox, 11f, !r.Line.Generalized, border, accent);
                    if (r.GenMark != null) r.GenMark.color = accent;
                    if (r.GenLabel != null)
                    {
                        Color gc = muted;
                        gc.a *= inc ? 1f : 0.5f;
                        r.GenLabel.color = gc;
                    }
                }
            }
        }

        private static void StyleBtn(Btn b, bool enabled, Color text, Color accent)
        {
            if (b == null || b.Bg == null) return;
            bool hover = b.Click != null && b.Click.Hover && enabled;
            GridTheme.ApplyBox(b.Bg, b.W, b.H, GridTheme.GridSurface.Button, hover);
            if (b.Label != null)
            {
                Color c = hover ? accent : text;
                if (!enabled) c.a *= 0.4f;
                b.Label.color = c;
            }
        }

        private static void StyleModeBtn(Btn b, CaptureApplyMode m, Color text, Color accent)
        {
            if (b == null || b.Bg == null) return;
            bool sel = _mode == m;
            bool hover = b.Click != null && b.Click.Hover;
            GridTheme.ApplyBox(b.Bg, b.W, b.H, GridTheme.GridSurface.Button, sel || hover);
            if (b.Label != null) b.Label.color = sel || hover ? accent : text;
        }

        /// <summary>The drawn checkbox: theme border, faint fill, accent line when checked. Styled
        /// DIRECTLY (never through ApplyBox), so it carries no shared FX material and needs no
        /// Unassign; widths/radii stay scales on the inherited theme values.</summary>
        private static void StyleCheck(PanelGraphic bg, float size, bool on, Color border, Color accent)
        {
            if (bg == null) return;
            Color fill = border;
            fill.a *= 0.10f;
            bg.color = fill;
            bg.BorderColor = on ? accent : border;
            bg.BorderWidth = Mathf.Max(0.8f, GridTheme.BorderWidth * 0.8f);
            bg.SetShape(size, size, Mathf.Min(3f, GridTheme.CornerRadius * 0.3f));
        }

        // ---------- small helpers ----------

        private static string BuildLineText(CaptureLine line)
        {
            string prefix;
            if (line.Kind == CaptureRuleKind.Category) prefix = "Category: ";
            else if (line.Kind == CaptureRuleKind.SlotClass) prefix = "Slot class: ";
            else prefix = "Item: ";
            string name = !string.IsNullOrEmpty(line.DisplayName) ? line.DisplayName
                : (!string.IsNullOrEmpty(line.RuleName) ? line.RuleName : "?");
            // Item-line display names are raw game DisplayNames (a localized or custom-named item
            // can carry non-ASCII, which the game's TMP font tofus) — fold before display.
            name = Ascii(name);
            string count = "";
            if (line.Count > 1 || line.Kind != CaptureRuleKind.Item)
                count = "  (" + line.Count + (line.Count == 1 ? " item)" : " items)");
            return prefix + name + count;
        }

        /// <summary>Strip anything outside printable Basic Latin from a DISPLAYED string (the
        /// PinnedInventoryWindow idiom): the common all-ASCII case allocates nothing.</summary>
        private static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < ' ' || s[i] > '~') { clean = false; break; }
            }
            if (clean) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c >= ' ' && c <= '~' ? c : ' ');
            }
            return sb.ToString();
        }

        private static Btn MakeBtn(string label, float w, float hh, Action onClick)
        {
            var rt = MakeRect("Btn_" + label, _panel);
            var bg = rt.gameObject.AddComponent<PanelGraphic>();
            bg.raycastTarget = true;
            _fxBoxes.Add(bg);
            var click = rt.gameObject.AddComponent<CaptureClick>();
            click.Clicked = onClick;
            var lbl = HudText.Make(rt, "Label", HudText.Size(10f),
                TextAlignmentOptions.Center, warp: false);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(w, hh);
            HudText.Set(lbl, label);
            var b = new Btn();
            b.Bg = bg;
            b.Click = click;
            b.Label = lbl;
            b.W = w;
            b.H = hh;
            return b;
        }

        private static PanelGraphic MakeCheckBox(Transform parent, float size, out TextMeshProUGUI mark)
        {
            var rt = MakeRect("Check", parent);
            rt.sizeDelta = new Vector2(size, size);
            var bg = rt.gameObject.AddComponent<PanelGraphic>();
            bg.raycastTarget = false;   // the row's wide hit zone owns the click
            mark = HudText.Make(rt, "Mark", HudText.Size(size <= 11f ? 8f : 9f),
                TextAlignmentOptions.Center, warp: false);
            var mrt = mark.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.anchoredPosition = Vector2.zero;
            mrt.sizeDelta = new Vector2(size + 4f, size + 4f);
            return bg;
        }

        /// <summary>Top-left anchored, CENTRE-pivot rect (the PanelGraphic convention).</summary>
        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        /// <summary>Place a centre-pivot rect by its top-left corner within its parent.</summary>
        private static void Seat(RectTransform rt, float x, float y, float w, float hh)
        {
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + hh * 0.5f));
            rt.sizeDelta = new Vector2(w, hh);
        }

        /// <summary>Place a TOP-LEFT-pivot rect (labels) by its top-left corner.</summary>
        private static void SeatTopLeft(RectTransform rt, float x, float y, float w, float hh)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, hh);
        }

        private static void FillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void ReleaseBlock()
        {
            if (!_blockHeld) return;
            _blockHeld = false;
            Core.CursorBlockArbiter.Release("capture");
        }

        /// <summary>Left-click surface for the dialog's buttons and row toggles. The action is a
        /// delegate set once at build (per-open, a user gesture); the component dies with the
        /// transient canvas, so nothing strands.</summary>
        private sealed class CaptureClick : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Clicked;
            public bool Hover;

            public void OnPointerClick(PointerEventData e)
            {
                if (e == null || e.button != PointerEventData.InputButton.Left) return;
                if (Clicked != null) Clicked();
            }

            public void OnPointerEnter(PointerEventData e) { Hover = true; }
            public void OnPointerExit(PointerEventData e) { Hover = false; }
        }

        /// <summary>The F10-hosted drive loop, attached to the transient root only in hosted
        /// mode (it dies with the canvas — nothing to unhook on reload). Per frame: live theme
        /// restyle, the world-pick block, Esc, and the auto-close rules — the world standing
        /// down always closes; F10 closing underneath (its own toggle key, a world change —
        /// NOT Esc: F10's Esc-close line now gates on <c>!GridCapturePanel.IsOpen</c>, so Esc
        /// never reaches F10 while this dialog is up) only closes a dialog with NOTHING
        /// invested. Invested — per the SAME <see cref="IsInvested"/> predicate EscClose uses,
        /// so a typed name counts (fix wave finding 2) — it survives floating, exactly like it
        /// survives a scrim click and a re-locked mouse.
        ///
        /// <para>The Esc chain here STANDS ALONE (it never relies on F10 or the Grid being
        /// open): first press with a focused name field just deselects it (TMP's own Esc),
        /// otherwise <see cref="EscClose"/> runs its arm-then-discard; vanilla's key-up is
        /// starved either way so one press can never also open the pause menu.</para></summary>
        private sealed class HostedPump : MonoBehaviour
        {
            private void Update()
            {
                if (!_open || !_hosted) return;
                if (!Guards.CanDraw()) { Close(); return; }

                StylePanel();
                _blockHeld = true;
                Core.CursorBlockArbiter.Hold("capture");

                bool f10Open = false;
                try { f10Open = Menu.UiaControlCenter.IsOpen; } catch { }
                if (!f10Open && !IsInvested() && !_escArmed) { Close(); return; }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Core.ModalInputChain.BeginEscSwallow();
                    if (!IsNameInputFocused) EscClose();
                }
            }
        }
    }
}
