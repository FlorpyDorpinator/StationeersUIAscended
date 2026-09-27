using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// The settings search (F10 plan principle 3: "discoverable by search, not by memory").
    /// Tabs register their rows DURING Build — <see cref="RegisterRow"/> — so the index is built
    /// from what actually exists, never from a hand list that drifts; the shell clears a tab's
    /// registrations (<see cref="BeginTab"/>) right before rebuilding it. Editors that live
    /// OUTSIDE F10 (the F9 Designer, the radial editor) register through
    /// <see cref="RegisterExternal"/> as "it's over there → open it" entries.
    ///
    /// <para>The title-bar box (<see cref="CreateBox"/>): typing filters a dropdown of matches;
    /// choosing one switches to the owning tab (which re-registers fresh actions), then invokes
    /// the row's jump — scroll into view + a brief accent flash (<see cref="MakeJump"/>).</para>
    ///
    /// <para>Scope note: a tab's rows enter the index the first time that tab is BUILT this
    /// session (they then persist across tab switches, refreshed on every rebuild). Until every
    /// tab registers its rows, search knows only the converted tabs — by design, the index is
    /// registrations, not guesses.</para>
    ///
    /// <para>Statics are the registry + the transient popup handles; <see cref="Reset"/> (from
    /// UiaControlCenter.Shutdown) clears everything, <see cref="CloseResults"/> rides the
    /// window's Close/Restyle.</para>
    /// </summary>
    public static class UiaSearch
    {
        private sealed class Entry
        {
            public string Tab;
            public string Section;
            public string Label;
            public Action Jump;
        }

        private sealed class ExternalEntry
        {
            public string Label;
            public string Description;
            public Action Open;
        }

        private static readonly List<Entry> _entries = new List<Entry>(64);
        private static readonly List<ExternalEntry> _external = new List<ExternalEntry>(8);

        private static UiaTextInput _box;
        private static GameObject _results;
        private static string _query = "";

        // Focus-loss backstop for the results popup (rows act on pointer DOWN, which fires the
        // same instant the field deselects — this only catches "clicked somewhere else entirely").
        private static float _unfocusedSince = -1f;

        // ---------- registry ----------

        /// <summary>Clear one tab's registrations. The shell calls this right before that tab's
        /// Build, so re-registration replaces instead of accumulating.</summary>
        public static void BeginTab(string tabTitle)
        {
            if (string.IsNullOrEmpty(tabTitle)) return;
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (string.Equals(_entries[i].Tab, tabTitle, StringComparison.Ordinal))
                    _entries.RemoveAt(i);
        }

        /// <summary>Register one settings row. Call during the tab's Build, after the row is
        /// built: <paramref name="jumpHighlight"/> should scroll it into view and flash it —
        /// <see cref="MakeJump"/> builds that action from the row's GameObject.</summary>
        public static void RegisterRow(string tabTitle, string sectionId, string label,
            Action jumpHighlight)
        {
            if (string.IsNullOrEmpty(tabTitle) || string.IsNullOrEmpty(label)) return;
            var e = new Entry();
            e.Tab = tabTitle;
            e.Section = sectionId ?? "";
            e.Label = label;
            e.Jump = jumpHighlight;
            _entries.Add(e);
        }

        /// <summary>Register a setting that lives in an external editor (F9, the radial editor):
        /// search answers "it's over there" and <paramref name="open"/> takes the player to it.
        /// Registrations persist until <see cref="Reset"/>; re-registering a label replaces it.</summary>
        public static void RegisterExternal(string label, string description, Action open)
        {
            if (string.IsNullOrEmpty(label)) return;
            for (int i = _external.Count - 1; i >= 0; i--)
                if (string.Equals(_external[i].Label, label, StringComparison.OrdinalIgnoreCase))
                    _external.RemoveAt(i);
            var e = new ExternalEntry();
            e.Label = label;
            e.Description = description ?? "";
            e.Open = open;
            _external.Add(e);
        }

        // ---------- jump + highlight ----------

        /// <summary>An Action that scrolls <paramref name="row"/> into view and flashes a brief
        /// accent overlay on it — the standard target for <see cref="RegisterRow"/>.</summary>
        public static Action MakeJump(GameObject row) => MakeJump(row, null);

        /// <summary>As above, with a <paramref name="reveal"/> step first — a row inside a
        /// collapsed "More options" disclosure passes <c>() =&gt; section.SetExpanded(true)</c>.</summary>
        public static Action MakeJump(GameObject row, Action reveal)
        {
            return () =>
            {
                if (reveal != null) { try { reveal(); } catch { } }
                JumpToRow(row);
            };
        }

        private static void JumpToRow(GameObject row)
        {
            if (row == null) return;
            try
            {
                var sr = row.GetComponentInParent<ScrollRect>();
                Canvas.ForceUpdateCanvases();
                if (sr != null && sr.content != null && sr.viewport != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(sr.content);
                    float contentH = sr.content.rect.height;
                    float viewH = sr.viewport.rect.height;
                    if (contentH > viewH + 1f)
                    {
                        var rowRt = (RectTransform)row.transform;
                        Vector3 world = rowRt.TransformPoint(rowRt.rect.center);
                        Vector3 local = sr.content.InverseTransformPoint(world);
                        // Content pivot is top (0.5, 1): local.y runs 0 at the top, negative down.
                        float fromTop = -local.y;
                        float target = 1f - (fromTop - viewH * 0.4f) / (contentH - viewH);
                        sr.verticalNormalizedPosition = Mathf.Clamp01(target);
                    }
                }
            }
            catch { }
            UiaSearchHighlight.Flash(row);
        }

        // ---------- the title-bar box ----------

        /// <summary>Build the title-bar search box. Owns its results popup; the box itself is a
        /// kit TextInput, so Esc in it cancels the search without closing F10.</summary>
        public static void CreateBox(Transform parent, float width)
        {
            var hostGo = UiaUi.Go("search-host", parent);
            // flexH pinned 0: without it the host's HLayout reports its child's flexible height
            // upward and the TITLE BAR becomes flexible (the half-empty-window breakage).
            UiaUi.Size(hostGo, 30f, width, flexW: 0f, flexH: 0f);
            UiaUi.HLayout((RectTransform)hostGo.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);

            var opt = new UiaInputs.TextInputOptions();
            opt.Placeholder = "Search settings...";
            opt.OnChanged = OnQueryChanged;
            opt.Height = 30f;
            _box = UiaInputs.TextInput(hostGo.transform, opt);

            // The magnifier rides the right end of the field (drawn, ASCII-free).
            UiaIcons.AttachAt(_box.transform, UiaIcon.Search, 14f, UiaTheme.TextMute,
                1f, 0.5f, -8f, 0f);

            hostGo.AddComponent<SearchBoxDriver>();
        }

        private static void OnQueryChanged(string q)
        {
            _query = q ?? "";
            if (_query.Trim().Length == 0) { CloseResults(); return; }
            RebuildResults();
        }

        /// <summary>Close the results popup (idempotent; safe after the layer died).</summary>
        public static void CloseResults()
        {
            if (_results != null) UnityEngine.Object.Destroy(_results);
            _results = null;
            _unfocusedSince = -1f;
        }

        /// <summary>The window Esc chain's close: True = the panel was open (Esc consumed).
        /// A panel destroyed under us (layer sweep) counts as already closed.</summary>
        internal static bool CloseResultsIfOpen()
        {
            bool open = _results != null;   // Unity-null: destroyed reads as closed
            CloseResults();
            return open;
        }

        /// <summary>Hot-reload / shutdown teardown.</summary>
        public static void Reset()
        {
            CloseResults();
            _entries.Clear();
            _external.Clear();
            _box = null;
            _query = "";
        }

        private const int MaxResults = 9;
        private const float ResultRowH = 30f;

        private static void RebuildResults()
        {
            CloseResults();
            var layer = UiaControls.PopupLayer;
            if (layer == null || _box == null) return;
            string q = _query.Trim();
            if (q.Length == 0) return;

            // Match: case-insensitive substring on the row label or its tab title; externals
            // match on label or description. Registration order is relevance enough for now.
            int shown = 0;
            _results = UiaUi.Go("search-results", layer);
            var prt = (RectTransform)_results.transform;
            var bg = _results.AddComponent<Image>();
            bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, UiaTheme.Border, 1f);
            prt.SetAsLastSibling();

            var colGo = UiaUi.Go("rows", _results.transform);
            var col = UiaUi.Fill((RectTransform)colGo.transform, 4f);
            UiaUi.VLayout(col, 0f);

            for (int i = 0; i < _entries.Count && shown < MaxResults; i++)
            {
                var e = _entries[i];
                if (!Matches(e.Label, q) && !Matches(e.Tab, q)) continue;
                var snapshot = e;
                ResultRow(colGo.transform, e.Label, e.Tab, () => JumpToEntry(snapshot));
                shown++;
            }
            for (int i = 0; i < _external.Count && shown < MaxResults; i++)
            {
                var e = _external[i];
                if (!Matches(e.Label, q) && !Matches(e.Description, q)) continue;
                var open = e.Open;
                ResultRow(colGo.transform, e.Label, "opens editor", () =>
                {
                    CloseResults();
                    ClearBox();
                    if (open != null) { try { open(); } catch { } }
                });
                shown++;
            }

            if (shown == 0)
            {
                var t = UiaUi.Text(colGo.transform, "No matching setting", UiaTheme.SmallSize,
                    UiaTheme.TextMute, TextAlignmentOptions.Left);
                UiaUi.Size(t.gameObject, ResultRowH);
                shown = 1;
            }

            float height = shown * ResultRowH + 8f;
            UiaComposite.PlacePopup(prt, (RectTransform)_box.transform, layer, 340f, height);
            _unfocusedSince = -1f;
        }

        private static bool Matches(string hay, string needle)
        {
            return !string.IsNullOrEmpty(hay)
                && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ResultRow(Transform parent, string label, string context, Action pick)
        {
            var rowGo = UiaUi.Go("result", parent);
            UiaUi.Size(rowGo, ResultRowH);
            var rimg = rowGo.AddComponent<Image>();
            rimg.color = new Color(0f, 0f, 0f, 0.001f);
            UiaImages.Round(rimg);
            UiaUi.HLayout((RectTransform)rowGo.transform, 8f, 8, 8, 0, 0);

            var t = UiaUi.Text(rowGo.transform, label, UiaTheme.SmallSize, UiaTheme.Text,
                TextAlignmentOptions.Left);
            // No ellipsis: shrink toward 9pt, wrapping to a second line inside the 30px row.
            UiaControls.FitText(t, 9f);
            var tle = t.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f;

            var ctx = UiaUi.Text(rowGo.transform, context ?? "", UiaTheme.SmallSize - 1f,
                UiaTheme.TextMute, TextAlignmentOptions.Right);
            // Same no-ellipsis fit: if a long label squeezes this column, it shrinks/wraps
            // instead of overflowing sideways into the label.
            UiaControls.FitText(ctx, 9f);

            // Pointer DOWN, not click: the press that lands here is the same press that just
            // unfocused the search field, and acting on DOWN beats any focus-loss cleanup.
            var d = rowGo.AddComponent<ResultRowDown>();
            d.Pick = pick;
            d.Bg = rimg;
        }

        private static void JumpToEntry(Entry snapshot)
        {
            CloseResults();
            ClearBox();
            // Switching tabs REBUILDS the target tab, which re-registers fresh jump actions —
            // so look the entry up again by key and run the fresh one (the snapshot's action
            // would point at destroyed objects).
            UiaControlCenter.SelectTabByTitle(snapshot.Tab);
            Entry fresh = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (string.Equals(e.Tab, snapshot.Tab, StringComparison.Ordinal)
                    && string.Equals(e.Section, snapshot.Section, StringComparison.Ordinal)
                    && string.Equals(e.Label, snapshot.Label, StringComparison.Ordinal))
                { fresh = e; break; }
            }
            var act = fresh != null ? fresh.Jump : null;
            if (act != null) { try { act(); } catch { } }
        }

        private static void ClearBox()
        {
            _query = "";
            if (_box != null)
            {
                _box.SetTextSilent("");
                try
                {
                    if (_box.Field != null) _box.Field.DeactivateInputField();
                    var es = EventSystem.current;
                    if (es != null && _box.Field != null
                        && es.currentSelectedGameObject == _box.Field.gameObject)
                        es.SetSelectedGameObject(null);
                }
                catch { }
            }
        }

        /// <summary>Called by the box driver each frame: closes an orphaned results popup a
        /// beat after the field loses focus to something that wasn't a result row.</summary>
        internal static void TickBox(bool boxFocused)
        {
            if (_results == null) return;
            if (boxFocused) { _unfocusedSince = -1f; return; }
            if (_unfocusedSince < 0f) { _unfocusedSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _unfocusedSince > 0.25f) CloseResults();
        }

        /// <summary>Lives on the search host so the backstop tick runs only while the box
        /// exists (and dies with the window — no static pump needed).</summary>
        private sealed class SearchBoxDriver : MonoBehaviour
        {
            private void Update()
            {
                bool focused = false;
                try { focused = _box != null && _box.Field != null && _box.Field.isFocused; }
                catch { }
                TickBox(focused);
            }
        }

        private sealed class ResultRowDown : MonoBehaviour,
            IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Pick;
            public Image Bg;

            public void OnPointerDown(PointerEventData e)
            {
                if (e != null && e.button != PointerEventData.InputButton.Left) return;
                if (Pick != null) Pick();
            }

            public void OnPointerEnter(PointerEventData e)
            {
                if (Bg != null) Bg.color = UiaTheme.PanelHover;
            }

            public void OnPointerExit(PointerEventData e)
            {
                if (Bg != null) Bg.color = new Color(0f, 0f, 0f, 0.001f);
            }
        }
    }

    /// <summary>The "you searched for this" flash: a rounded accent overlay on the row that
    /// fades out over ~1.2s and destroys itself. Attach-and-forget; no statics.</summary>
    internal sealed class UiaSearchHighlight : MonoBehaviour
    {
        private const float Life = 1.2f;
        private Image _img;
        private float _bornAt;

        /// <summary>Flash <paramref name="row"/>. A second flash on the same row restarts it.</summary>
        internal static void Flash(GameObject row)
        {
            if (row == null) return;
            var existing = row.GetComponent<UiaSearchHighlight>();
            if (existing != null) { existing._bornAt = Time.unscaledTime; return; }
            row.AddComponent<UiaSearchHighlight>();
        }

        private void Awake()
        {
            _bornAt = Time.unscaledTime;
            var go = UiaUi.Go("search-hilite", transform);
            var rt = UiaUi.Fill((RectTransform)go.transform, -2f);   // a whisker beyond the row
            _img = go.AddComponent<Image>();
            UiaImages.Round(_img);
            _img.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        private void Update()
        {
            float age = Time.unscaledTime - _bornAt;
            if (age >= Life || _img == null)
            {
                if (_img != null) Destroy(_img.gameObject);
                Destroy(this);
                return;
            }
            Color c = UiaTheme.Selected;
            c.a = 0.38f * (1f - age / Life);
            _img.color = c;
        }
    }
}
