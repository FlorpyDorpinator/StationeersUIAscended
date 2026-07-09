using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{

    public static class UnityRadialView
    {
        private const float HoverBulge = 13f;      // px the hovered wedge grows outward
        private const float AnimSpeed = 16f;       // lerp rate (1/s) - snappier modern feel
        private const float BorderWidth = 3.2f;    // slim but powerful border
        private const float HoverContentPop = 6f;  // extra outward for icon+label on hover
        private const float HoverScale = 1.04f;    // subtle label/icon scale on hover

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
            // Slightly nicer open curve (ease out)
            float openT = EaseOut01(_openAnim);
            _openAnim = Mathf.Lerp(_openAnim, 1f, dt * AnimSpeed);

            _main.Render(center, innerR, outerR, entries, hovered, openT, dimmed: satEntries != null);

            if (satEntries != null && satCenter.HasValue)
                _satellite.Render(satCenter.Value, satInnerR, satOuterR, satEntries, satHovered, openT, dimmed: false);
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

        private static float EaseOut01(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t); // quadratic ease out
        }

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
                _root.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, openAnim);

                int count = entries?.Count ?? 0;
                EnsureCapacity(count);

                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
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

                    // Dynamic background shading: non-highlighted wedges shade darker when any wedge is highlighted.
                    // This makes the highlighted wedge stand out (the background shades based on the active one).
                    float bgShade = 1f;
                    if (hovered >= 0 && !isHovered)
                    {
                        bgShade = 0.52f;
                    }

                    // Resolve modern UGUI colors (after feedback):
                    // - Blue on selected/hovered (the popping selected state)
                    // - Orange for borders + rim/outer highlight
                    Color fillTarget = ResolveUguiFill(entry, isHovered, dimmed);
                    Color borderTarget = ResolveUguiBorder(entry, isHovered, dimmed);

                    if (bgShade < 1f)
                    {
                        // Shade non-highlighted wedge backgrounds
                        float d = 0.62f;
                        fillTarget = new Color(fillTarget.r * d, fillTarget.g * d, fillTarget.b * d, fillTarget.a * bgShade);
                    }

                    // Lerp current toward target (smooth)
                    wedge.color = Color.Lerp(wedge.color, fillTarget, dt * AnimSpeed);

                    float bulgeTarget = isHovered ? HoverBulge : 0f;
                    if (!Mathf.Approximately(wedge.OuterBulge, bulgeTarget))
                    {
                        wedge.OuterBulge = Mathf.Lerp(wedge.OuterBulge, bulgeTarget, dt * AnimSpeed);
                    }

                    // Border (slim but powerful, derived from the wedge's own bg color + accent)
                    float borderWTarget = isHovered ? BorderWidth * 1.2f : BorderWidth;
                    wedge.BorderWidth = Mathf.Lerp(wedge.BorderWidth, borderWTarget, dt * AnimSpeed * 0.7f);
                    wedge.BorderColor = Color.Lerp(wedge.BorderColor, borderTarget, dt * AnimSpeed);

                    // Modern rim shine + orange highlight (stronger + more visible on hover for pop)
                    float shineTarget = isHovered ? 0.72f : 0.18f;
                    wedge.RimHighlight = Mathf.Lerp(wedge.RimHighlight, shineTarget, dt * AnimSpeed);

                    // Any geometry property (bulge/border/rim) changed -> rebuild mesh this frame
                    wedge.RefreshGeometry();

                    // === Content placement (icon + label) with hover pop ===
                    float aMid = a0 + sector * 0.5f;
                    float baseMidR = (innerR + outerR) * 0.5f;
                    float hoverExtra = isHovered ? (HoverBulge * 0.32f + HoverContentPop) : 0f;
                    float midR = baseMidR + hoverExtra;

                    var dir = new Vector2(Mathf.Cos(aMid), -Mathf.Sin(aMid));
                    var slot = dir * midR;

                    float iconSize = Mathf.Clamp(ringWidth * 0.46f, 20f, 56f);
                    float contentScale = isHovered ? HoverScale : 1f;

                    var icon = _icons[i];
                    icon.sprite = entry.Icon;
                    icon.enabled = entry.Icon != null;
                    float iconAlpha = (entry.Enabled ? 1f : 0.32f) * (dimmed ? 0.45f : 1f);
                    icon.color = new Color(1f, 1f, 1f, iconAlpha);
                    icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize) * contentScale;
                    icon.rectTransform.anchoredPosition = slot + dir * (ringWidth * 0.05f) +
                                                         new Vector2(0f, entry.Icon != null ? ringWidth * 0.12f : 0f);

                    var label = _labels[i];
                    label.text = entry.Label ?? string.Empty;
                    label.color = FromImGui(entry.Enabled ? Theme.TextPrimary : Theme.TextDisabled);
                    label.rectTransform.localScale = Vector3.one * contentScale;

                    // Modern auto box width
                    float halfAngle = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chord = 2f * midR * Mathf.Sin(halfAngle);
                    float boxW = Mathf.Clamp(Mathf.Abs(Mathf.Cos(aMid)) * ringWidth + Mathf.Abs(Mathf.Sin(aMid)) * chord - 6f,
                                             42f, ringWidth * 2.5f);
                    label.rectTransform.sizeDelta = new Vector2(boxW, ringWidth * 0.48f);
                    label.rectTransform.anchoredPosition =
                        slot - dir * (ringWidth * 0.02f) -
                        new Vector2(0f, entry.Icon != null ? iconSize * contentScale * 0.48f + 1f : 0f);
                }
            }

            private static Color ResolveUguiFill(RadialEntry entry, bool hovered, bool dimmed)
            {
                Color col;
                if (!entry.Enabled)
                {
                    col = Theme.UguiDisabled;
                }
                else if (entry.FillOverride.HasValue)
                {
                    // Stow uses orange fill (orange works really well)
                    col = hovered ? Theme.UguiOrangeBright : Theme.UguiOrange;
                }
                else if (hovered)
                {
                    // Blue on selected/hovered - darker/richer so it pops without being "too bright" as a background
                    col = Theme.UguiSelectedBlue;
                }
                else
                {
                    col = Theme.UguiBg;
                }

                if (dimmed) col.a *= 0.48f;
                return col;
            }

            private static Color ResolveUguiBorder(RadialEntry entry, bool hovered, bool dimmed)
            {
                // Orange borders + orange highlight (as requested). Blue is reserved for the selected wedge fill.
                Color baseBorder;
                if (!entry.Enabled)
                {
                    baseBorder = new Color(0.22f, 0.22f, 0.22f, 0.40f);
                }
                else if (entry.FillOverride.HasValue)
                {
                    // Stronger orange border on stow
                    baseBorder = hovered ? Theme.UguiOrangeBright : Theme.UguiOrange;
                }
                else
                {
                    // Orange border for normal wedges too (powerful accent)
                    baseBorder = hovered ? Theme.UguiOrangeBright : Theme.UguiOrange;
                }

                // Slim but powerful border - higher alpha on hover for pop
                float alphaMul = dimmed ? 0.55f : (hovered ? 0.95f : 0.68f);
                baseBorder.a *= alphaMul;

                // Make blue-selected wedges have slightly more saturated/powerful orange border for contrast
                if (hovered && !entry.FillOverride.HasValue)
                {
                    baseBorder = Color.Lerp(baseBorder, Theme.UguiOrangeBright, 0.25f);
                }

                return baseBorder;
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
                    wedge.BorderWidth = 0f;
                    wedge.RimHighlight = 0f;
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
            private readonly Image _hubBacking;
            private readonly TextMeshProUGUI _title, _verb, _label, _sub, _warn;

            public ReadoutView(Transform parent, TMP_FontAsset font)
            {
                var go = new GameObject("Readout", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _root = (RectTransform)go.transform;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);

                // Very subtle modern hub backing (slightly transparent dark glass)
                var hub = new GameObject("HubBacking", typeof(RectTransform));
                hub.transform.SetParent(_root, false);
                _hubBacking = hub.AddComponent<Image>();
                _hubBacking.color = new Color(0.06f, 0.06f, 0.06f, 0.38f); // very subtle glass
                _hubBacking.raycastTarget = false;

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
                float w = (innerR - 6f) * 1.85f;

                // Modern slim hub backing (glass over the center)
                if (_hubBacking != null)
                {
                    // Subtle dark glass center. Square for now (no sprite/mask to stay asset-free).
                    // Can be replaced with a circular mask + sprite later for perfect modern visor look.
                    float hubSize = innerR * 1.48f;
                    _hubBacking.rectTransform.sizeDelta = new Vector2(hubSize, hubSize);
                    _hubBacking.rectTransform.anchoredPosition = Vector2.zero;
                }

                Place(_title, 38f, w);
                Place(_verb, 13f, w);
                Place(_label, -7f, w);
                Place(_sub, -25f, w);
                Place(_warn, -43f, w);

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
