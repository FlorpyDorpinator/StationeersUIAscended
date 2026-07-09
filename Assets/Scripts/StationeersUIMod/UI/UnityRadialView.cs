using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// The primary radial renderer: a Unity/UGUI implementation built ENTIRELY IN CODE at runtime.
    ///
    /// Florpy built this on top of the earlier prefab-based radial work (from main) and the
    /// shared interaction model refined during the radial UX overhaul.
    ///
    /// Why procedural + code-built (no prefabs):
    /// - A prefab bakes the wedge count (N) into the asset → forces one prefab per N, or paging
    ///   that hides items, or dead hover sectors. A radial is inherently variable (1..14+ entries).
    /// - One RadialWedgeGraphic + runtime mesh in OnPopulateMesh serves any count.
    /// - Building the Canvas, RingViews, wedges, icons, and TMP labels entirely in C# means:
    ///   no AssetBundle, no editor round-trips for the core UI, and perfect ScriptEngine hot-reload (F6).
    ///
    /// Advantages over the original ImGui painter and over the earlier prefab approach:
    /// - Real TextMeshPro (auto-sizing + wrapping, no manual chord fitting hacks).
    /// - Icons use Unity Image.preserveAspect = true (no more warping).
    /// - Per-wedge hover animation (outward bulge + color lerp) and open scale-in reveal.
    /// - Clean polar geometry and raycast filter.
    /// - Easy future path to custom shaders or curved visor visuals.
    ///
    /// All interaction logic (hover math, satellite rings, sticky mode, level stack, actions)
    /// remains in RadialMenu / RadialController. This view is purely a consumer of that state.
    /// </summary>
    public static class UnityRadialView
    {
        private const float HoverBulge = 12f;      // px the hovered wedge grows outward
        private const float AnimSpeed = 14f;       // lerp rate (1/s)

        private static Canvas _canvas;
        private static CanvasGroup _group;
        private static TMP_FontAsset _font;
        private static RingView _main;
        private static RingView _satellite;
        private static ReadoutView _readout;
        private static float _openAnim;

        // ---------- public API ----------

        public static void Render(Vector2 center, float innerR, float outerR,
            IList<RadialEntry> entries, int hovered, string title,
            Vector2? satCenter, float satInnerR, float satOuterR,
            IList<RadialEntry> satEntries, int satHovered, string satTitle,
            RadialEntry readoutEntry, string readoutHint, bool sticky)
        {
            EnsureCanvas();
            _group.alpha = 1f;
            _canvas.gameObject.SetActive(true);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _openAnim = Mathf.Lerp(_openAnim, 1f, dt * AnimSpeed);

            _main.Render(center, innerR, outerR, entries, hovered, _openAnim, dimmed: satEntries != null);

            if (satEntries != null && satCenter.HasValue)
                _satellite.Render(satCenter.Value, satInnerR, satOuterR, satEntries, satHovered, _openAnim, dimmed: false);
            else
                _satellite.Hide();

            _readout.Render(center, innerR, title, satTitle, readoutEntry, readoutHint, sticky);
        }

        public static void Hide()
        {
            if (_canvas == null) return;
            _canvas.gameObject.SetActive(false);
            _openAnim = 0f;
        }

        /// <summary>Full teardown — required for clean ScriptEngine hot reloads.</summary>
        public static void Shutdown()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _group = null;
            _main = null;
            _satellite = null;
            _readout = null;
            _font = null;
            _openAnim = 0f;
        }

        // ---------- infrastructure ----------

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("UIAscended_RadialCanvas");
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the vanilla HUD. NOTE: the game's ImGui output canvas jumps to 32767 when
            // ImGuiManager.SetBlockUguiClicks(true) is active, so our radial would render
            // *under* the ImGui layer while a modal blocks clicks. Kept below that on purpose.
            _canvas.sortingOrder = 5000;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;   // hover is driven by RadialMenu, not the EventSystem

            _main = new RingView(go.transform, "MainRing");
            _satellite = new RingView(go.transform, "SatelliteRing");
            _readout = new ReadoutView(go.transform, Font());
        }

        internal static TMP_FontAsset Font()
        {
            if (_font != null) return _font;
            // Borrow a font the game already loaded — no bundling, no missing-glyph risk.
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all != null && all.Length > 0) _font = all[0];
            if (_font == null) _font = TMP_Settings.defaultFontAsset;
            return _font;
        }

        /// <summary>ImGui packs colors R-in-low-byte (IM_COL32). Decoding as ARGB swaps red
        /// and blue — which is exactly why the prefab wedges rendered orange accents as blue.</summary>
        internal static Color FromImGui(uint c)
        {
            byte r = (byte)(c & 0xFF);
            byte g = (byte)((c >> 8) & 0xFF);
            byte b = (byte)((c >> 16) & 0xFF);
            byte a = (byte)((c >> 24) & 0xFF);
            return new Color32(r, g, b, a);
        }

        /// <summary>ImGui screen coords (y-down) -> canvas local coords around a ring center.</summary>
        internal static Vector2 ToLocal(Vector2 imguiPoint, Vector2 imguiCenter)
            => new Vector2(imguiPoint.x - imguiCenter.x, -(imguiPoint.y - imguiCenter.y));

        internal static Vector2 CanvasAnchoredPos(Vector2 imguiCenter)
            => new Vector2(imguiCenter.x - Screen.width * 0.5f,
                           (Screen.height - imguiCenter.y) - Screen.height * 0.5f);

        // ---------- one ring ----------

        private sealed class RingView
        {
            private readonly RectTransform _root;
            private readonly List<RadialWedgeGraphic> _wedges = new List<RadialWedgeGraphic>();
            private readonly List<Image> _icons = new List<Image>();
            private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

            public RingView(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _root = (RectTransform)go.transform;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
                _root.pivot = new Vector2(0.5f, 0.5f);
                _root.sizeDelta = Vector2.zero;
            }

            public void Hide() => _root.gameObject.SetActive(false);

            public void Render(Vector2 center, float innerR, float outerR,
                IList<RadialEntry> entries, int hovered, float openAnim, bool dimmed)
            {
                _root.gameObject.SetActive(true);
                _root.anchoredPosition = CanvasAnchoredPos(center);
                _root.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, openAnim);

                int count = entries?.Count ?? 0;
                EnsureCapacity(count);

                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float sector = count > 0 ? Mathf.PI * 2f / count : 0f;
                float ringWidth = outerR - innerR;

                for (int i = 0; i < _wedges.Count; i++)
                {
                    bool used = i < count;
                    _wedges[i].gameObject.SetActive(used);
                    _icons[i].gameObject.SetActive(used);
                    _labels[i].gameObject.SetActive(used);
                    if (!used) continue;

                    var entry = entries[i];
                    var wedge = _wedges[i];

                    float a0 = -Mathf.PI * 0.5f - sector * 0.5f + sector * i;
                    wedge.SetGeometry(innerR, outerR, a0, a0 + sector, fullRing: count == 1);

                    bool isHovered = i == hovered && entry.Enabled;
                    Color target = ResolveColor(entry, isHovered, dimmed);
                    wedge.color = Color.Lerp(wedge.color, target, dt * AnimSpeed);

                    float bulgeTarget = isHovered ? HoverBulge : 0f;
                    if (!Mathf.Approximately(wedge.OuterBulge, bulgeTarget))
                    {
                        wedge.OuterBulge = Mathf.Lerp(wedge.OuterBulge, bulgeTarget, dt * AnimSpeed);
                        wedge.RefreshGeometry();
                    }

                    // Content sits at the wedge's mid-radius, upright (never rotated).
                    float aMid = a0 + sector * 0.5f;
                    float midR = (innerR + outerR) * 0.5f + (isHovered ? HoverBulge * 0.35f : 0f);
                    var slot = new Vector2(Mathf.Cos(aMid), -Mathf.Sin(aMid)) * midR;

                    float iconSize = Mathf.Clamp(ringWidth * 0.46f, 20f, 56f);
                    var icon = _icons[i];
                    icon.sprite = entry.Icon;
                    icon.enabled = entry.Icon != null;
                    icon.color = new Color(1f, 1f, 1f, (entry.Enabled ? 1f : 0.35f) * (dimmed ? 0.5f : 1f));
                    icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                    icon.rectTransform.anchoredPosition = slot + new Vector2(0f, entry.Icon != null ? ringWidth * 0.14f : 0f);

                    var label = _labels[i];
                    label.text = entry.Label ?? string.Empty;
                    label.color = FromImGui(entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled);
                    // TMP auto-sizes and wraps inside the box: no ellipsis, no manual fitting.
                    float halfAngle = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chord = 2f * midR * Mathf.Sin(halfAngle);
                    float boxW = Mathf.Clamp(Mathf.Abs(Mathf.Cos(aMid)) * ringWidth + Mathf.Abs(Mathf.Sin(aMid)) * chord - 8f,
                                             44f, ringWidth * 2.4f);
                    label.rectTransform.sizeDelta = new Vector2(boxW, ringWidth * 0.5f);
                    label.rectTransform.anchoredPosition =
                        slot - new Vector2(0f, entry.Icon != null ? iconSize * 0.5f + 2f : 0f);
                }
            }

            private static Color ResolveColor(RadialEntry entry, bool hovered, bool dimmed)
            {
                uint c = !entry.Enabled ? Theme.RingDisabled
                       : entry.FillOverride.HasValue ? (hovered ? Theme.RingStowHover : entry.FillOverride.Value)
                       : hovered ? Theme.RingHover
                       : Theme.RingBg;
                var col = FromImGui(c);
                if (dimmed) col.a *= 0.55f;
                return col;
            }

            private void EnsureCapacity(int n)
            {
                while (_wedges.Count < n)
                {
                    int idx = _wedges.Count;

                    var wgo = new GameObject("Wedge" + idx, typeof(RectTransform));
                    wgo.transform.SetParent(_root, false);
                    var wedge = wgo.AddComponent<RadialWedgeGraphic>();
                    wedge.raycastTarget = false;
                    _wedges.Add(wedge);

                    var igo = new GameObject("Icon" + idx, typeof(RectTransform));
                    igo.transform.SetParent(_root, false);
                    var img = igo.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.preserveAspect = true;   // aspect handled by UGUI: icons can't warp
                    _icons.Add(img);

                    var tgo = new GameObject("Label" + idx, typeof(RectTransform));
                    tgo.transform.SetParent(_root, false);
                    var tmp = tgo.AddComponent<TextMeshProUGUI>();
                    tmp.font = Font();
                    tmp.alignment = TextAlignmentOptions.Top;
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 8f;
                    tmp.fontSizeMax = 15f;
                    tmp.enableWordWrapping = true;
                    tmp.overflowMode = TextOverflowModes.Truncate;
                    tmp.raycastTarget = false;
                    _labels.Add(tmp);
                }
            }
        }

        // ---------- center readout ----------

        private sealed class ReadoutView
        {
            private readonly RectTransform _root;
            private readonly TextMeshProUGUI _title, _verb, _label, _sub, _warn;

            public ReadoutView(Transform parent, TMP_FontAsset font)
            {
                var go = new GameObject("Readout", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _root = (RectTransform)go.transform;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);

                _title = Make(font, 13f, out var t0); t0.SetParent(_root, false);
                _verb = Make(font, 15f, out var t1); t1.SetParent(_root, false);
                _label = Make(font, 15f, out var t2); t2.SetParent(_root, false);
                _sub = Make(font, 12f, out var t3); t3.SetParent(_root, false);
                _warn = Make(font, 12f, out var t4); t4.SetParent(_root, false);
            }

            private static TextMeshProUGUI Make(TMP_FontAsset font, float size, out RectTransform rt)
            {
                var go = new GameObject("Line", typeof(RectTransform));
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                tmp.fontSize = size;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.raycastTarget = false;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 8f;
                tmp.fontSizeMax = size;
                rt = (RectTransform)go.transform;
                return tmp;
            }

            public void Render(Vector2 center, float innerR, string title, string satTitle,
                RadialEntry hovered, string hint, bool sticky)
            {
                _root.anchoredPosition = CanvasAnchoredPos(center);
                float w = (innerR - 8f) * 1.8f;

                Place(_title, 40f, w);
                Place(_verb, 14f, w);
                Place(_label, -6f, w);
                Place(_sub, -26f, w);
                Place(_warn, -46f, w);

                _title.text = satTitle ?? title ?? string.Empty;
                _title.color = FromImGui(Theme.TextDim);

                if (hovered == null)
                {
                    _verb.text = hint ?? (sticky ? "LMB select | RMB back" : "release to cancel");
                    _verb.color = FromImGui(Theme.TextDisabled);
                    _label.text = _sub.text = _warn.text = string.Empty;
                    return;
                }

                _verb.text = hovered.ActionText ?? (hovered.IsBranch ? "Open" : "Select");
                _verb.color = FromImGui(hovered.Enabled ? Theme.Accent : Theme.TextDisabled);
                _label.text = hovered.Label ?? string.Empty;
                _label.color = FromImGui(hovered.Enabled ? Theme.TextPrimary : Theme.TextDisabled);
                _sub.text = hovered.Sublabel ?? string.Empty;
                _sub.color = FromImGui(Theme.TextDim);

                if (!hovered.Enabled && !string.IsNullOrEmpty(hovered.DisabledReason))
                {
                    _warn.text = hovered.DisabledReason;
                    _warn.color = FromImGui(Theme.Critical);
                }
                else if (!string.IsNullOrEmpty(hovered.Warning))
                {
                    _warn.text = hovered.Warning;
                    _warn.color = FromImGui(Theme.Warn);
                }
                else
                {
                    _warn.text = string.Empty;
                }
            }

            private static void Place(TextMeshProUGUI t, float y, float w)
            {
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.sizeDelta = new Vector2(w, 20f);
                t.rectTransform.anchoredPosition = new Vector2(0f, y);
            }
        }
    }
}
