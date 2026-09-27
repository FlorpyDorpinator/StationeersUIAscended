using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>Interactive widgets for the Control Center, all EventSystem-driven (the canvas
    /// carries a GraphicRaycaster and the game keeps an EventSystem alive). Solid Image graphics,
    /// no sprites. Row-builder statics at the bottom are what the tabs actually call.</summary>
    public static class UiaControls
    {
        /// <summary>Where dropdown popups mount so they float above everything (set by the window).</summary>
        public static RectTransform PopupLayer;

        /// <summary>The dropdown whose popup is open right now (only ever one — opening a popup
        /// mounts a full-layer catcher, so a second dropdown's click lands on the catcher first).
        /// Lets the window's Esc chain close the popup instead of the window. Cleared by
        /// ClosePopup, and belt-and-braces wherever PopupLayer is dropped.</summary>
        internal static UiaDropdown OpenDropdown;

        /// <summary>Close the open dropdown popup, if any. True = there was one (Esc consumed).</summary>
        internal static bool CloseOpenDropdown()
        {
            var dd = OpenDropdown;
            OpenDropdown = null;
            if (dd == null) return false;   // none, or destroyed with its page (Unity-null)
            dd.CloseFromEsc();
            return true;
        }

        // ================= No-ellipsis text fitting =================

        /// <summary>The kit's NO-ELLIPSIS rule (FlorpyDorp, 2026-09-26: "We should never cut
        /// anything off by an ellipses"). Fit a label that lives in a FIXED-size box: TMP
        /// auto-size shrinks it from its authored size down to <paramref name="minSize"/>, and
        /// with <paramref name="wrap"/> it may break onto a second line first when the box is
        /// tall enough (TMP picks the LARGEST size that fits, so a label a hair too wide stays one
        /// line at a slightly smaller size rather than jumping to two). Overflow mode is plain
        /// Overflow — never Ellipsis/Truncate/Masking — so at the true extreme (still too long
        /// at the floor) the text spills past its box rather than hiding characters. Call AFTER
        /// the text's fontSize is set. Idempotent: a second call keeps the original max size.
        /// Where the ROW can grow instead, prefer plain word-wrap and let the layout size it.</summary>
        public static void FitText(TMP_Text t, float minSize = 9f, bool wrap = true)
        {
            if (t == null) return;
            float max = t.enableAutoSizing ? Mathf.Max(t.fontSize, t.fontSizeMax) : t.fontSize;
            t.fontSizeMax = max;
            t.fontSizeMin = Mathf.Min(minSize, max);
            t.enableAutoSizing = true;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow;
        }

        // ================= Button =================

        public sealed class UiaButton : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Action OnClick;
            private Image _bg;
            private Color _normal, _hover, _selected;
            private bool _enabled = true;
            private bool _selectedState;

            public UiaButton Init(Image bg, Color normal, Color hover, Color selected)
            {
                _bg = bg; _normal = normal; _hover = hover; _selected = selected;
                Repaint(false);
                return this;
            }

            public void SetEnabled(bool e) { _enabled = e; Repaint(false); }
            public void SetSelected(bool s) { _selectedState = s; Repaint(false); }

            private void Repaint(bool hover)
            {
                if (_bg == null) return;
                if (!_enabled) { _bg.color = _normal * 0.6f; return; }
                _bg.color = _selectedState ? _selected : (hover ? _hover : _normal);
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (_enabled && OnClick != null) OnClick();
            }
            public void OnPointerEnter(PointerEventData e) { if (_enabled) Repaint(true); }
            public void OnPointerExit(PointerEventData e) { Repaint(false); }
        }

        // ================= Toggle switch =================

        public sealed class UiaToggle : MonoBehaviour,
            IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Action<bool> OnChanged;
            private bool _value;
            private Image _track;
            private RectTransform _knob;
            private Image _knobImg;

            public UiaToggle Init(Image track, RectTransform knob, Image knobImg, bool value)
            {
                _track = track; _knob = knob; _knobImg = knobImg;
                Set(value, false);
                return this;
            }

            public bool Value => _value;

            public void Set(bool value, bool notify)
            {
                _value = value;
                if (_track != null) _track.color = value ? UiaTheme.On : UiaTheme.Off;
                if (_knob != null)
                {
                    _knob.anchorMin = _knob.anchorMax = new Vector2(value ? 1f : 0f, 0.5f);
                    _knob.pivot = new Vector2(value ? 1f : 0f, 0.5f);
                    _knob.anchoredPosition = new Vector2(value ? -2f : 2f, 0f);
                }
                if (_knobImg != null) _knobImg.color = Color.white;
                if (notify && OnChanged != null) OnChanged(value);
            }

            public void OnPointerClick(PointerEventData e) => Set(!_value, true);
            public void OnPointerEnter(PointerEventData e) { if (_knobImg != null) _knobImg.color = new Color(0.9f, 0.95f, 1f); }
            public void OnPointerExit(PointerEventData e) { if (_knobImg != null) _knobImg.color = Color.white; }
        }

        // ================= Slider (custom, layout-width agnostic) =================

        public sealed class UiaSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public Action<float> OnChanged;
            private RectTransform _track, _fill, _handle;
            private float _min, _max, _value, _step;
            private TextMeshProUGUI _valueLabel;
            private string _fmt;

            public UiaSlider Init(RectTransform track, RectTransform fill, RectTransform handle,
                float min, float max, float value, float step,
                TextMeshProUGUI valueLabel, string fmt)
            {
                _track = track; _fill = fill; _handle = handle;
                _min = min; _max = Mathf.Max(max, min + 0.0001f); _step = step;
                _valueLabel = valueLabel; _fmt = fmt;
                SetValue(value, false);
                return this;
            }

            private void SetValue(float v, bool notify)
            {
                v = Mathf.Clamp(v, _min, _max);
                if (_step > 0f) v = _min + Mathf.Round((v - _min) / _step) * _step;
                v = Mathf.Clamp(v, _min, _max);
                _value = v;
                float frac = (_value - _min) / (_max - _min);
                if (_fill != null) _fill.anchorMax = new Vector2(frac, 1f);
                if (_handle != null)
                {
                    _handle.anchorMin = _handle.anchorMax = new Vector2(frac, 0.5f);
                    _handle.anchoredPosition = Vector2.zero;
                }
                if (_valueLabel != null) _valueLabel.text = Format(_value);
                if (notify && OnChanged != null) OnChanged(_value);
            }

            private string Format(float v)
            {
                if (!string.IsNullOrEmpty(_fmt)) return v.ToString(_fmt);
                return _step >= 1f ? Mathf.RoundToInt(v).ToString() : v.ToString("0.00");
            }

            private void Apply(PointerEventData e)
            {
                if (_track == null) return;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _track, e.position, e.pressEventCamera, out local)) return;
                float w = _track.rect.width;
                if (w <= 0f) return;
                float frac = Mathf.Clamp01((local.x - _track.rect.xMin) / w);
                SetValue(_min + frac * (_max - _min), true);
            }

            public void OnPointerDown(PointerEventData e) => Apply(e);
            public void OnDrag(PointerEventData e) => Apply(e);
        }

        // ================= Dropdown =================

        /// <summary>Dropdown v2 (F10 plan pain 9): the popup clamps to the screen edges and
        /// FLIPS above the field when the space below is too short; lists longer than 10 rows
        /// scroll (as before); and past 12 options a search box appears at the top of the popup
        /// filtering the rows live (the 45-entry slot-class list's fix). Public API unchanged.</summary>
        public sealed class UiaDropdown : MonoBehaviour, IPointerClickHandler
        {
            public Action<int> OnChanged;
            private List<string> _options;
            private int _index;
            private TextMeshProUGUI _label;
            private GameObject _popup;
            private GameObject _catcher;
            private RectTransform _rowsHost;   // the scroll content the option rows rebuild into
            private string _filter = "";

            public UiaDropdown Init(TextMeshProUGUI label, List<string> options, int index)
            {
                _label = label; _options = options ?? new List<string>();
                _index = Mathf.Clamp(index, 0, Mathf.Max(0, _options.Count - 1));
                Refresh();
                return this;
            }

            public int Index => _index;

            public void SetOptions(List<string> options, int index)
            {
                _options = options ?? new List<string>();
                _index = Mathf.Clamp(index, 0, Mathf.Max(0, _options.Count - 1));
                Refresh();
            }

            private void Refresh()
            {
                if (_label != null)
                    _label.text = (_index >= 0 && _index < _options.Count) ? _options[_index] : "-";
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (_popup != null) { ClosePopup(); return; }
                OpenPopup();
            }

            private const int ScrollBeyond = 10;   // more rows than this -> the list scrolls
            private const int SearchBeyond = 12;   // more options than this -> a search box

            /// <summary>The Esc chain's close: identical to a catcher click.</summary>
            internal void CloseFromEsc() => ClosePopup();

            private void OpenPopup()
            {
                var layer = PopupLayer != null ? PopupLayer : (RectTransform)transform.root;
                _filter = "";
                OpenDropdown = this;

                // A full-layer transparent catcher below the popup: clicking anywhere off the
                // list closes it (the standard dropdown dismiss).
                _catcher = UiaUi.Go("dropdown-catcher", layer);
                var cimg = _catcher.AddComponent<Image>();
                cimg.color = new Color(0, 0, 0, 0.001f);
                UiaUi.Fill((RectTransform)_catcher.transform);
                _catcher.transform.SetAsLastSibling();
                _catcher.AddComponent<UiaButton>().Init(cimg, cimg.color, cimg.color, cimg.color).OnClick = ClosePopup;

                _popup = UiaUi.Go("dropdown-popup", layer);
                var prt = (RectTransform)_popup.transform;
                var bg = _popup.AddComponent<Image>();
                bg.color = UiaTheme.PanelRaised;
                UiaImages.Round(bg);
                UiaUi.OutlineOf(bg, UiaTheme.AccentDim, 1f);
                prt.SetAsLastSibling();

                // Field corners in layer space (for width + anchoring).
                var self = (RectTransform)transform;
                Vector3[] corners = new Vector3[4];
                self.GetWorldCorners(corners); // 0=BL,1=TL,2=TR,3=BR
                Vector2 blLocal, trLocal;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(layer,
                    RectTransformUtility.WorldToScreenPoint(null, corners[0]), null, out blLocal);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(layer,
                    RectTransformUtility.WorldToScreenPoint(null, corners[2]), null, out trLocal);
                float width = Mathf.Abs(trLocal.x - blLocal.x);

                float rowH = UiaTheme.RowH;
                bool search = _options.Count > SearchBeyond;
                float searchH = search ? rowH + 6f : 0f;
                int show = Mathf.Min(_options.Count, ScrollBeyond);
                float wantH = show * rowH + 6f + searchH;

                // Clamp / flip at the screen (= layer) edges: below by preference, above when
                // the space below is too short and above is roomier; height caps to the side
                // chosen and the ScrollView absorbs the difference.
                Rect lr = layer.rect;
                float spaceBelow = blLocal.y - lr.yMin - 6f;
                float spaceAbove = lr.yMax - trLocal.y - 6f;
                bool flipUp = wantH > spaceBelow && spaceAbove > spaceBelow;
                float height = Mathf.Min(wantH, Mathf.Max(rowH * 3f + searchH,
                    flipUp ? spaceAbove : spaceBelow));
                float x = blLocal.x;
                if (x + width > lr.xMax - 4f) x = lr.xMax - 4f - width;
                if (x < lr.xMin + 4f) x = lr.xMin + 4f;

                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = flipUp ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
                prt.anchoredPosition = flipUp
                    ? new Vector2(x, trLocal.y + 2f)
                    : new Vector2(x, blLocal.y);
                prt.sizeDelta = new Vector2(width, height);

                if (search)
                {
                    // The kit text input, riding the top of the popup. Esc in it cancels the
                    // search (UiaInputs consumes the key), a second Esc closes nothing else —
                    // the catcher owns dismissal.
                    var searchHost = UiaUi.Go("dd-search", prt);
                    var shRt = (RectTransform)searchHost.transform;
                    shRt.anchorMin = new Vector2(0f, 1f);
                    shRt.anchorMax = new Vector2(1f, 1f);
                    shRt.pivot = new Vector2(0.5f, 1f);
                    shRt.offsetMin = new Vector2(3f, -(searchH));
                    shRt.offsetMax = new Vector2(-3f, -3f);
                    UiaUi.HLayout(shRt, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);
                    var opt = new UiaInputs.TextInputOptions();
                    opt.Placeholder = "Search...";
                    opt.Height = rowH;
                    opt.OnChanged = v => { _filter = v ?? ""; RebuildRows(); };
                    var box = UiaInputs.TextInput(searchHost.transform, opt);
                    box.Focus();
                }

                // The scrolling rows area fills the rest, below the (optional) search box.
                var rowsAreaGo = UiaUi.Go("dd-rows", prt);
                var raRt = (RectTransform)rowsAreaGo.transform;
                raRt.anchorMin = Vector2.zero;
                raRt.anchorMax = Vector2.one;
                raRt.offsetMin = new Vector2(2f, 2f);
                raRt.offsetMax = new Vector2(-2f, -(searchH + 2f));

                ScrollRect scroll;
                _rowsHost = UiaUi.ScrollView(raRt, out scroll, 0f);
                UiaUi.Fill((RectTransform)scroll.gameObject.transform);
                RebuildRows();
            }

            /// <summary>(Re)build the option rows into the open popup, honouring the live
            /// filter. A user gesture path — plain rebuild, no pooling needed.</summary>
            private void RebuildRows()
            {
                if (_rowsHost == null) return;
                for (int i = _rowsHost.childCount - 1; i >= 0; i--)
                {
                    var c = _rowsHost.GetChild(i);
                    c.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                float rowH = UiaTheme.RowH;
                string f = _filter != null ? _filter.Trim() : "";
                for (int i = 0; i < _options.Count; i++)
                {
                    if (f.Length > 0 && (_options[i] == null
                        || _options[i].IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;
                    int idx = i;
                    var row = UiaUi.Go("opt", _rowsHost);
                    var rimg = row.AddComponent<Image>();
                    rimg.color = idx == _index ? UiaTheme.SelectedDim : UiaTheme.PanelRaised;
                    UiaUi.Size(row, rowH);
                    var t = UiaUi.Text(row.transform, _options[i], UiaTheme.SmallSize,
                        idx == _index ? UiaTheme.Selected : UiaTheme.Text, TextAlignmentOptions.Left);
                    // No ellipsis: shrink toward 9pt, wrapping to a 2nd line inside the 30px row.
                    FitText(t, 9f);
                    var trt = (RectTransform)t.transform; UiaUi.Fill(trt, 0f); trt.offsetMin = new Vector2(8f, 0f);
                    trt.offsetMax = new Vector2(-4f, 0f);
                    row.AddComponent<UiaButton>().Init(rimg, rimg.color, UiaTheme.PanelHover, UiaTheme.SelectedDim)
                        .OnClick = () => { _index = idx; Refresh(); ClosePopup(); if (OnChanged != null) OnChanged(idx); };
                }
            }

            private void ClosePopup()
            {
                if (_popup != null) UnityEngine.Object.Destroy(_popup);
                // The full-layer click-catcher must die WITH the popup — leaked, it sits over
                // the whole window eating every click ("after I hit a dropdown it won't let me
                // click the menu afterwards").
                if (_catcher != null) UnityEngine.Object.Destroy(_catcher);
                _popup = null;
                _catcher = null;
                _rowsHost = null;
                _filter = "";
                if (ReferenceEquals(OpenDropdown, this)) OpenDropdown = null;
            }

            private void OnDisable() => ClosePopup();
        }

        // ================= Row builders (what tabs call) =================

        public static TextMeshProUGUI Header(Transform parent, string text)
        {
            var go = UiaUi.Go("header", parent);
            UiaUi.Size(go, 32f);
            var v = (RectTransform)go.transform;
            // Headers are the near-full-saturation cyan token (UiaTheme.HeaderText, the
            // concept's #0AF4FA family), ~25% larger. No duplicate-TMP "bloom" behind it: a
            // copy at a different size drifts across the word as it goes (FlorpyDorp's ghosted
            // "POWER & GLITCH"), and a same-size copy only doubles the glyph edges.
            var t = UiaUi.Text(go.transform, text.ToUpperInvariant(), 16f, UiaTheme.HeaderText, TextAlignmentOptions.BottomLeft);
            var trt = (RectTransform)t.transform; UiaUi.Fill(trt); trt.offsetMin = new Vector2(2f, 2f);
            t.characterSpacing = 5f;
            // underline divider
            var line = UiaUi.Image(go.transform, UiaTheme.Divider, "rule");
            var lrt = (RectTransform)line.transform;
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f); lrt.sizeDelta = new Vector2(0f, 1f); lrt.anchoredPosition = Vector2.zero;
            return t;
        }

        public static TextMeshProUGUI Note(Transform parent, string text)
        {
            // TMP is an ILayoutElement, so a parent layout group reads its wrapped preferred
            // height DIRECTLY — no ContentSizeFitter (a fitter driving a layout-group child is
            // the driven-transform conflict UiaComposite's cards document; it was also
            // redundant here). The LayoutElement only pins a one-line MINIMUM so a squashed
            // page can never crush the note to a zero-height text smear (fix wave 2026-09-26).
            var go = UiaUi.Go("note", parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = UiaTheme.Font();
            t.fontSize = UiaTheme.SmallSize;
            t.color = UiaTheme.TextMute;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = new Vector4(2f, 3f, 2f, 3f);
            t.text = text ?? "";
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 18f;
            return t;
        }

        private static RectTransform RowShell(Transform parent, string label, out RectTransform right)
        {
            var row = UiaUi.Go("row", parent);
            UiaUi.Size(row, UiaTheme.RowH);
            var h = UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);
            var lblGo = UiaUi.Go("label", row.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.LabelSize; lbl.color = UiaTheme.Text;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = label;
            FitText(lbl, 9f);   // no ellipsis: shrink, then 2 lines inside the RowH row
            var le = lblGo.AddComponent<LayoutElement>(); le.flexibleWidth = 1f; le.minWidth = 60f;
            right = (RectTransform)row.transform;
            return (RectTransform)row.transform;
        }

        /// <summary>Just the switch widget (no label/row) — sized 46×22.</summary>
        public static UiaToggle Switch(Transform parent, bool value, Action<bool> onChanged)
        {
            var sw = UiaUi.Go("switch", parent);
            UiaUi.Size(sw, 22f, 46f);
            var track = sw.AddComponent<Image>(); track.color = value ? UiaTheme.On : UiaTheme.Off;
            UiaImages.Round(track);
            var knobGo = UiaUi.Go("knob", sw.transform);
            var knob = (RectTransform)knobGo.transform; knob.sizeDelta = new Vector2(18f, 18f);
            var knobImg = knobGo.AddComponent<Image>(); knobImg.color = Color.white;
            UiaImages.Round(knobImg);
            var t = sw.AddComponent<UiaToggle>().Init(track, knob, knobImg, value);
            t.OnChanged = onChanged;
            return t;
        }

        public static UiaToggle ToggleRow(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            RectTransform right;
            RowShell(parent, label, out right);
            return Switch(right, value, onChanged);
        }

        public static UiaSlider SliderRow(Transform parent, string label, float min, float max,
            float value, Action<float> onChanged, string fmt = null, float step = 0f)
        {
            RectTransform right;
            RowShell(parent, label, out right);

            var valGo = UiaUi.Go("val", right);
            UiaUi.Size(valGo, UiaTheme.RowH, 52f);
            var val = valGo.AddComponent<TextMeshProUGUI>();
            val.font = UiaTheme.Font(); val.fontSize = UiaTheme.SmallSize; val.color = UiaTheme.TextDim;
            val.alignment = TextAlignmentOptions.Right; val.raycastTarget = false;

            var trackGo = UiaUi.Go("track", right);
            UiaUi.Size(trackGo, 14f, 150f, flexW: 0f);
            var trackImg = trackGo.AddComponent<Image>(); trackImg.color = UiaTheme.Track;
            UiaImages.Round(trackImg);
            var track = (RectTransform)trackGo.transform;
            // vertically center the visible bar within the taller hit area handled by layout
            var fillGo = UiaUi.Go("fill", trackGo.transform);
            var fill = (RectTransform)fillGo.transform;
            fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f); fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>(); fillImg.color = UiaTheme.Accent; fillImg.raycastTarget = false;
            UiaImages.Round(fillImg);
            var handleGo = UiaUi.Go("handle", trackGo.transform);
            var handle = (RectTransform)handleGo.transform; handle.sizeDelta = new Vector2(10f, 20f);
            var handleImg = handleGo.AddComponent<Image>(); handleImg.color = Color.white; handleImg.raycastTarget = false;
            UiaImages.Round(handleImg);

            var s = trackGo.AddComponent<UiaSlider>().Init(track, fill, handle, min, max, value, step, val, fmt);
            s.OnChanged = onChanged;
            return s;
        }

        public static UiaDropdown DropdownRow(Transform parent, string label, List<string> options,
            int index, Action<int> onChanged)
        {
            RectTransform right;
            RowShell(parent, label, out right);
            var ddGo = UiaUi.Go("dropdown", right);
            UiaUi.Size(ddGo, UiaTheme.RowH, 170f, flexW: 0f);
            var bg = ddGo.AddComponent<Image>(); bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, UiaTheme.AccentDim, 1f);
            var lbl = UiaUi.Text(ddGo.transform, "", UiaTheme.SmallSize, UiaTheme.Text, TextAlignmentOptions.Left);
            var lrt = (RectTransform)lbl.transform; UiaUi.Fill(lrt); lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-18f, 0f);
            FitText(lbl, 9f);   // no ellipsis: the chosen option shrinks/wraps inside the field
            UiaIcons.AttachAt(ddGo.transform, UiaIcon.CaretDown, 12f, UiaTheme.TextDim, 1f, 0.5f, -6f, 0f);
            var dd = ddGo.AddComponent<UiaDropdown>().Init(lbl, options, index);
            dd.OnChanged = onChanged;
            return dd;
        }

        /// <summary>A standalone push button (not a labelled row). Style: Panel (default),
        /// Primary (accent), or Selected (orange).</summary>
        public static UiaButton Button(Transform parent, string text, Action onClick,
            float width = -1f, float height = -1f, ButtonStyle style = ButtonStyle.Panel)
        {
            var go = UiaUi.Go("button", parent);
            UiaUi.Size(go, height >= 0 ? height : UiaTheme.RowH, width, flexW: width < 0 ? 1f : 0f);
            var bg = go.AddComponent<Image>();
            UiaImages.Round(bg);
            Color normal, hover, selected, textCol;
            switch (style)
            {
                case ButtonStyle.Primary:
                    normal = UiaTheme.Accent; hover = UiaTheme.Accent * 1.1f; selected = UiaTheme.Selected;
                    textCol = new Color(0.02f, 0.06f, 0.09f); break;
                case ButtonStyle.Danger:
                    normal = new Color(0.32f, 0.10f, 0.10f, 1f); hover = new Color(0.5f, 0.16f, 0.16f, 1f);
                    selected = UiaTheme.Critical; textCol = UiaTheme.Text; break;
                default:
                    normal = UiaTheme.PanelRaised; hover = UiaTheme.PanelHover; selected = UiaTheme.SelectedDim;
                    textCol = UiaTheme.Text; break;
            }
            bg.color = normal;
            var t = UiaUi.Text(go.transform, text, UiaTheme.LabelSize, textCol, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)t.transform);
            var btn = go.AddComponent<UiaButton>().Init(bg, normal, hover, selected);
            btn.OnClick = onClick;
            // Interior surfaces follow the HUD edge light + ripple (no glow halo) — an additive
            // glass border over the button fill; the hover recolour above is untouched.
            go.AddComponent<UiaGlassSkin>();
            return btn;
        }

        /// <summary>Swap a button's text label for a sprite icon (e.g. vanilla's PauseIcon).
        /// The label is emptied, not removed, so the button's repaint path stays untouched.
        /// Null-tolerant: a missing sprite leaves the text fallback in place.</summary>
        public static void SetButtonIcon(UiaButton btn, Sprite sprite, float size)
        {
            if (btn == null || sprite == null) return;
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "";
            var go = UiaUi.Go("icon", btn.transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = UiaTheme.Text;   // themed tint, like every glyph the kit draws
        }

        public enum ButtonStyle { Panel, Primary, Danger }
    }
}
