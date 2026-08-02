using System;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// A themed text input for the coach's DEV EDIT mode.
    ///
    /// <para>WHY THIS EXISTS rather than <c>UiaUi.InputField</c>: the kit's factory is the right
    /// pattern (a code-built <see cref="TMP_InputField"/>, no template prefab, RectMask2D viewport,
    /// UiaTheme colours) but it is hard-wired to <c>LineType.SingleLine</c>, has no caret/selection
    /// theming and no seed text. The step BODY needs all three. This is the same construction,
    /// parameterised - deliberately a thin wrapper, not a second widget kit. If UiaUi.InputField
    /// ever grows those parameters, delete this file and call it instead.</para>
    ///
    /// <para>All colours read from <see cref="UiaTheme"/> at build time, exactly like the rest of
    /// the kit, so the field re-tints when the coach rebuilds on a theme change. No statics -
    /// nothing to reset on hot-reload; the field dies with its parent canvas.</para>
    /// </summary>
    internal static class TutorialTextField
    {
        /// <summary>Build a themed input. <paramref name="multiLine"/> switches to
        /// MultiLineNewline (Enter inserts a newline instead of submitting) and turns word wrap on.
        /// The caller owns layout: the returned field's GameObject is a normal layout child.</summary>
        internal static TMP_InputField Make(Transform parent, string seed, string placeholder,
            bool multiLine, int characterLimit, Action<string> onChanged = null)
        {
            var go = UiaUi.Go(multiLine ? "edit-body" : "edit-heading", parent);
            // Build INACTIVE, activate after wiring. The coach adds fields to a live canvas, so
            // AddComponent would run TMP_InputField's Awake/OnEnable immediately - with
            // textComponent/viewport still null, its one-time activation machinery (text-change
            // registration, caret setup) silently no-ops and the field never responds to clicks.
            // Deactivating first defers OnEnable until everything below is assigned.
            go.SetActive(false);
            var bg = go.AddComponent<Image>();
            bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, UiaTheme.AccentDim, 1f);
            // Interior surfaces follow the HUD edge light / ripple, same as UiaControls.Button.
            go.AddComponent<UiaGlassSkin>();

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.transition = Selectable.Transition.None;   // the theme owns the look

            var areaGo = UiaUi.Go("area", go.transform);
            var area = UiaUi.Fill((RectTransform)areaGo.transform, 6f);
            areaGo.AddComponent<RectMask2D>();

            var align = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            var ph = UiaUi.Text(area, placeholder ?? "", UiaTheme.SmallSize, UiaTheme.TextMute, align, multiLine);
            UiaUi.Fill((RectTransform)ph.transform);
            ph.richText = false;
            var txt = UiaUi.Text(area, "", UiaTheme.SmallSize, UiaTheme.Text, align, multiLine);
            UiaUi.Fill((RectTransform)txt.transform);
            txt.richText = false;

            input.textViewport = area;
            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline
                                       : TMP_InputField.LineType.SingleLine;
            input.richText = false;                 // a stray '<' in copy is text, not markup
            if (characterLimit > 0) input.characterLimit = characterLimit;

            // Caret + selection from the live theme (the kit's factory leaves these at TMP's
            // hardcoded defaults, which read as a black caret on a dark panel).
            input.customCaretColor = true;
            input.caretColor = UiaTheme.Accent;
            input.caretWidth = 2;
            input.selectionColor = UiaTheme.SelectedDim;

            if (onChanged != null)
                input.onValueChanged.AddListener(v => onChanged(v));

            // Seed LAST: TMP_InputField only lays text out once textComponent/viewport are wired.
            input.text = seed ?? "";
            go.SetActive(true);   // NOW OnEnable runs with a fully-wired field
            return input;
        }

        /// <summary>True while this field owns the keyboard - the coach suppresses its Enter/Esc
        /// navigation so typing cannot page the tutorial. Null-safe (a destroyed field is false).</summary>
        internal static bool Focused(TMP_InputField f)
        {
            if (f == null) return false;
            try { return f.isFocused; }
            catch { return false; }
        }
    }
}
