using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// THE text input for the Control Center (F10 plan §7): one implementation behind every
    /// field, replacing the three parallel ones (the old bare <c>UiaUi.InputField</c>,
    /// <c>TutorialTextField</c>, GridCapturePanel's hand-rolled box). What it guarantees:
    ///
    /// <list type="bullet">
    /// <item>An intrinsic <see cref="LayoutElement"/> (min + preferred height = the row height,
    /// flexible width), so no host layout can ever collapse it to a hairline again (D-009).</item>
    /// <item>Built INACTIVE → wired → activated (the TutorialTextField lesson: TMP_InputField's
    /// OnEnable machinery no-ops forever if it runs before textComponent/viewport exist).</item>
    /// <item>Rounded sprite + the kit glass border; caret/selection colours from the theme.</item>
    /// <item><c>richText = false</c>; optional <c>characterLimit</c>; non-ASCII rejected as typed
    /// AND stripped again on commit (the TMP tofu rule — belt and braces, since paste paths differ
    /// across TMP versions).</item>
    /// <item>Enter commits; <b>Esc cancels the edit and CONSUMES the key</b> so the window's own
    /// Esc handling doesn't also close F10 (pain 4) — see <see cref="AnyFocused"/>.</item>
    /// <item>Single- or multi-line; an optional inline validation line under the field.</item>
    /// <item>A kit-owned DRAFT CACHE keyed by a caller-supplied stable id, so a half-typed name
    /// survives the page rebuilds that every gesture triggers (pain 6) — no more per-tab
    /// dictionaries of field text.</item>
    /// </list>
    /// </summary>
    public static class UiaInputs
    {
        // ---- Esc ownership + focus registry ----

        // Live fields register on enable so the window can ask "does a text box own the keyboard
        // right now?" without walking the hierarchy. Entries die with their GameObjects; the list
        // is pruned lazily and cleared by Reset().
        private static readonly List<TMP_InputField> _live = new List<TMP_InputField>(8);

        // The frame an Esc CANCELLED an edit. TMP deactivates the field during the EventSystem's
        // own update, which may run before or after UiaControlCenter.Update in the same frame —
        // this marker covers the "TMP already dropped focus" ordering so the window still sees
        // the key as consumed and stays open.
        private static int _escConsumedFrame = -1;

        /// <summary>True while any kit text field has keyboard focus, or an Esc cancelled an edit
        /// THIS frame. The window's Update checks this before treating Esc as "close F10".</summary>
        public static bool AnyFocused
        {
            get
            {
                if (Time.frameCount == _escConsumedFrame) return true;
                for (int i = _live.Count - 1; i >= 0; i--)
                {
                    var f = _live[i];
                    if (f == null) { _live.RemoveAt(i); continue; }
                    if (f.isFocused) return true;
                }
                return false;
            }
        }

        internal static void Register(TMP_InputField f)
        {
            if (f != null && !_live.Contains(f)) _live.Add(f);
        }

        internal static void Unregister(TMP_InputField f)
        {
            if (f != null) _live.Remove(f);
        }

        internal static void NoteEscConsumed() { _escConsumedFrame = Time.frameCount; }

        // ---- draft cache ----

        private static readonly Dictionary<string, string> _drafts =
            new Dictionary<string, string>(8);

        /// <summary>The uncommitted draft for a stable field id, or null. A committed or
        /// cancelled edit clears its draft; a page rebuild mid-edit leaves it for the rebuilt
        /// field to pick back up.</summary>
        public static string PeekDraft(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string v;
            return _drafts.TryGetValue(id, out v) ? v : null;
        }

        public static void ClearDraft(string id)
        {
            if (!string.IsNullOrEmpty(id)) _drafts.Remove(id);
        }

        internal static void PutDraft(string id, string text)
        {
            if (!string.IsNullOrEmpty(id)) _drafts[id] = text ?? "";
        }

        /// <summary>Hot-reload / shutdown teardown (called from UiaControlCenter.Shutdown).</summary>
        public static void Reset()
        {
            _live.Clear();
            _drafts.Clear();
            _escConsumedFrame = -1;
        }

        // ---- ASCII discipline ----

        /// <summary>Strip anything the game's TMP font cannot render (non-ASCII → tofu). Newlines
        /// survive only in multi-line fields. Returns the input unchanged when already clean.</summary>
        public static string AsciiFilter(string s, bool multiLine)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\n' || ch == '\r') { if (!multiLine) { clean = false; break; } }
                else if (ch < 32 || ch > 126) { clean = false; break; }
            }
            if (clean) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\n' || ch == '\r') { if (multiLine) sb.Append('\n'); }
                else if (ch >= 32 && ch <= 126) sb.Append(ch);
            }
            return sb.ToString();
        }

        // ---- construction ----

        /// <summary>Everything a field can be. Plain fields (C# 7.3 — no init-only sugar);
        /// unset members mean "the default".</summary>
        public sealed class TextInputOptions
        {
            public string Placeholder;
            /// <summary>Seed text. A live draft under <see cref="DraftId"/> wins over this.</summary>
            public string InitialText;
            /// <summary>Stable id for the kit draft cache (null = caller manages its own text).</summary>
            public string DraftId;
            /// <summary>0 = unlimited. Profile names use 40.</summary>
            public int CharacterLimit;
            public bool MultiLine;
            /// <summary>Preferred height; &lt;= 0 = RowH (single line) / 3×RowH (multi-line).</summary>
            public float Height;
            /// <summary>Default FALSE: the field keeps its fixed intrinsic height. True lets it
            /// STRETCH to fill a taller host. Opt-in only — flexibleHeight is not a private
            /// preference, it BUBBLES: a LayoutGroup host without its own explicit flexible
            /// height reports its children's max upward, so a defaulted-flexible field inside
            /// the title bar made the whole bar flexible and split the window's spare height
            /// with the tab zone (the 2026-09-26 half-empty-window breakage).</summary>
            public bool FlexibleHeight;
            /// <summary>Fired on Enter (single-line) and — while <see cref="CommitOnFocusLoss"/>
            /// is true — on focus loss, with the ASCII-filtered text. NOT fired when Esc
            /// cancelled the edit.</summary>
            public Action<string> OnCommit;
            /// <summary>Default TRUE: losing focus commits (the browser-form convention, and the
            /// pre-existing wrapper contract). Set FALSE for a field whose commit sits next to a
            /// Cancel button: Unity defocuses on pointer-DOWN — before Cancel's click ever runs —
            /// so a focus-loss commit would fire on the very press that meant "don't". When
            /// false, commit fires ONLY on Enter (TMP's onSubmit; single-line — a multi-line
            /// field's Enter inserts a newline, so such a field commits through its own OK
            /// button reading <see cref="UiaTextInput.Text"/>); focus loss just keeps the
            /// draft, and Esc still cancels.</summary>
            public bool CommitOnFocusLoss = true;
            /// <summary>Fired live on every keystroke (the legacy InputField contract).</summary>
            public Action<string> OnChanged;
            /// <summary>Optional validator: return an error string to show under the field, null
            /// for OK. Display-only — commit still fires; the caller re-validates on commit.</summary>
            public Func<string, string> Validate;
        }

        /// <summary>Build a text input under <paramref name="parent"/>. Returns the live handle;
        /// its <see cref="UiaTextInput.Field"/> is the raw TMP_InputField for callers that need
        /// one (the legacy <c>UiaUi.InputField</c> wrapper returns exactly that).</summary>
        public static UiaTextInput TextInput(Transform parent, TextInputOptions opt)
        {
            if (opt == null) opt = new TextInputOptions();

            var go = UiaUi.Go("input", parent);
            // Build INACTIVE, activate after wiring (see the class doc).
            go.SetActive(false);

            float h = opt.Height > 0f ? opt.Height : (opt.MultiLine ? UiaTheme.RowH * 3f : UiaTheme.RowH);
            // The intrinsic size that D-009 was missing: min + preferred height (UiaUi.Size
            // derives the min), flexible width. Height flex is OPT-IN — see FlexibleHeight.
            UiaUi.Size(go, h: h, flexW: 1f, flexH: opt.FlexibleHeight ? 1f : 0f);

            var bg = go.AddComponent<Image>();
            bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            var focusRim = UiaUi.OutlineOf(bg, UiaTheme.AccentDim, 1f);
            go.AddComponent<UiaGlassSkin>();   // the same glass border every interior control has

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.transition = Selectable.Transition.None;   // the theme owns the look

            var areaGo = UiaUi.Go("area", go.transform);
            var area = UiaUi.Fill((RectTransform)areaGo.transform, 6f);
            areaGo.AddComponent<RectMask2D>();

            var align = opt.MultiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            var ph = UiaUi.Text(area, opt.Placeholder ?? "", UiaTheme.SmallSize, UiaTheme.TextMute, align, opt.MultiLine);
            UiaUi.Fill((RectTransform)ph.transform);
            ph.richText = false;
            var txt = UiaUi.Text(area, "", UiaTheme.SmallSize, UiaTheme.Text, align, opt.MultiLine);
            UiaUi.Fill((RectTransform)txt.transform);
            txt.richText = false;

            input.textViewport = area;
            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = opt.MultiLine ? TMP_InputField.LineType.MultiLineNewline
                                           : TMP_InputField.LineType.SingleLine;
            input.richText = false;
            if (opt.CharacterLimit > 0) input.characterLimit = opt.CharacterLimit;

            // Caret + selection from the live theme — TMP's defaults are near-black-on-dark.
            input.customCaretColor = true;
            input.caretColor = UiaTheme.Accent;
            input.caretWidth = 2;
            input.selectionColor = UiaTheme.SelectedDim;

            // Optional inline validation line, as a SIBLING under the field so it never fights
            // the field's own height. Only exists when a validator was supplied.
            TextMeshProUGUI validationLine = null;
            if (opt.Validate != null)
            {
                validationLine = UiaUi.Text(parent, "", UiaTheme.SmallSize - 1f, UiaTheme.Critical,
                    TextAlignmentOptions.Left, true);
                UiaUi.Size(validationLine.gameObject, 0f, flexW: 1f);
                validationLine.gameObject.SetActive(false);
            }

            var handle = go.AddComponent<UiaTextInput>();
            handle.Init(input, focusRim, validationLine, opt);

            // Seed LAST (TMP lays text out only once wired): a live draft wins over InitialText.
            string seed = opt.DraftId != null ? PeekDraft(opt.DraftId) : null;
            if (seed == null) seed = opt.InitialText ?? "";
            input.SetTextWithoutNotify(seed);

            go.SetActive(true);   // NOW OnEnable runs with a fully-wired field
            return handle;
        }
    }

    /// <summary>The live handle a built <see cref="UiaInputs.TextInput"/> returns: commit/cancel
    /// wiring, draft upkeep, focus registration and the focus rim. Lives on the field's own
    /// GameObject, so it dies (and unregisters) with the page like every kit widget.</summary>
    public sealed class UiaTextInput : MonoBehaviour
    {
        private TMP_InputField _field;
        private Outline _focusRim;
        private TextMeshProUGUI _validationLine;
        private UiaInputs.TextInputOptions _opt;
        private string _preEditText = "";
        private bool _focusPainted;

        /// <summary>The raw TMP field, for legacy callers (set .text, read .text, focus it).</summary>
        public TMP_InputField Field => _field;

        public string Text => _field != null ? _field.text : "";

        /// <summary>Replace the text without firing OnChanged (a page painting external state).</summary>
        public void SetTextSilent(string text)
        {
            if (_field != null) _field.SetTextWithoutNotify(text ?? "");
        }

        /// <summary>Give the field keyboard focus (a popup's search box on open).</summary>
        public void Focus()
        {
            if (_field == null) return;
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null) es.SetSelectedGameObject(_field.gameObject);
                _field.ActivateInputField();
            }
            catch { }
        }

        internal void Init(TMP_InputField field, Outline focusRim,
            TextMeshProUGUI validationLine, UiaInputs.TextInputOptions opt)
        {
            _field = field;
            _focusRim = focusRim;
            _validationLine = validationLine;
            _opt = opt;

            // Reject non-ASCII as it is TYPED, so tofu never even flashes in the box.
            _field.onValidateInput = ValidateChar;
            _field.onSelect.AddListener(OnFieldSelected);
            _field.onValueChanged.AddListener(OnFieldChanged);
            _field.onEndEdit.AddListener(OnFieldEndEdit);
            // Enter-only commit mode listens on TMP's onSubmit (fired for a single-line Enter
            // BEFORE the deactivate), because onEndEdit alone cannot tell Enter from a stray
            // defocus — and the stray defocus is exactly what this mode exists to ignore.
            if (opt != null && !opt.CommitOnFocusLoss)
                _field.onSubmit.AddListener(OnFieldSubmit);
        }

        private char ValidateChar(string text, int charIndex, char addedChar)
        {
            if (addedChar == '\n' || addedChar == '\r')
                return (_opt != null && _opt.MultiLine) ? '\n' : '\0';
            if (addedChar < 32 || addedChar > 126) return '\0';
            return addedChar;
        }

        private void OnFieldSelected(string current)
        {
            _preEditText = current ?? "";
        }

        private void OnFieldChanged(string v)
        {
            if (_opt == null) return;
            if (_opt.DraftId != null) UiaInputs.PutDraft(_opt.DraftId, v);
            RunValidation(v);
            if (_opt.OnChanged != null) _opt.OnChanged(v);
        }

        private void OnFieldEndEdit(string v)
        {
            if (_field != null && _field.wasCanceled)
            {
                // Esc: consume the key (the window must NOT also close on it), put the pre-edit
                // text back, and throw the draft away — a cancel means "forget what I typed".
                UiaInputs.NoteEscConsumed();
                if (_opt != null && _opt.DraftId != null) UiaInputs.ClearDraft(_opt.DraftId);
                if (_field != null) _field.SetTextWithoutNotify(_preEditText);
                RunValidation(_preEditText);
                return;
            }
            if (_opt == null) return;
            // Enter (single-line) or focus loss: commit, ASCII-filtered — UNLESS this field is
            // Enter-only (CommitOnFocusLoss false): then a plain defocus just keeps the draft
            // (the Enter path already committed through onSubmit before this fired).
            if (!_opt.CommitOnFocusLoss) return;
            CommitNow(v);
        }

        /// <summary>TMP onSubmit (single-line Enter), wired only in Enter-only commit mode.
        /// Guarded on wasCanceled so an Esc can never masquerade as a commit.</summary>
        private void OnFieldSubmit(string v)
        {
            if (_field != null && _field.wasCanceled) return;
            if (_opt == null) return;
            CommitNow(v);
        }

        private void CommitNow(string v)
        {
            string committed = UiaInputs.AsciiFilter(v, _opt.MultiLine);
            if (!string.Equals(committed, v, StringComparison.Ordinal) && _field != null)
                _field.SetTextWithoutNotify(committed);
            if (_opt.DraftId != null) UiaInputs.ClearDraft(_opt.DraftId);
            if (_opt.OnCommit != null) _opt.OnCommit(committed);
        }

        private void RunValidation(string v)
        {
            if (_opt == null || _opt.Validate == null || _validationLine == null) return;
            string err = null;
            try { err = _opt.Validate(v); } catch { }
            bool show = !string.IsNullOrEmpty(err);
            if (_validationLine.gameObject.activeSelf != show)
                _validationLine.gameObject.SetActive(show);
            if (show)
            {
                _validationLine.text = err;
                var le = _validationLine.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = _validationLine.preferredHeight;
            }
        }

        private void OnEnable() { UiaInputs.Register(_field); }
        private void OnDisable() { UiaInputs.Unregister(_field); }

        // The focus rim brightens while the field owns the keyboard — painted only on the
        // transition, so the per-frame cost is one bool compare.
        private void Update()
        {
            if (_field == null || _focusRim == null) return;
            bool focused = _field.isFocused;
            if (focused == _focusPainted) return;
            _focusPainted = focused;
            _focusRim.effectColor = focused ? UiaTheme.Accent : UiaTheme.AccentDim;
        }
    }
}
