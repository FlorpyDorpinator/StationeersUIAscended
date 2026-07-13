using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
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
        // Border thickness comes from config (UIAConfig.RadialBorderWidth) so it is tunable live.
        private const float HoverContentPop = 6f;  // extra outward for icon+label on hover
        private const float HoverScale = 1.04f;    // subtle label/icon scale on hover

        private static Canvas _canvas;
        private static CanvasGroup _group;
        private static TMP_FontAsset _font;
        private static string _fontFor;            // config value the cache was resolved for
        private static bool _fontIsBoldAsset;
        private static RingView _main;
        private static RingView _satellite;
        private static ReadoutView _readout;      // the detail readout (follows the action)
        private static ReadoutView _readoutHome;  // keeps the MAIN hub dressed while the detail moved
        private static RectTransform _closeRoot;   // hub CLOSE button (always at the MAIN hub)
        private static RadialWedgeGraphic _closeBand;
        private static TextMeshProUGUI _closeLabel;
        private static TextMeshProUGUI _pageMain;  // "1/2  -  Q: next page" above a paged ring
        private static TextMeshProUGUI _pageSat;
        private static float _openAnim;

        // ---------- public API ----------

        public static void Render(Vector2 center, float innerR, float outerR,
            IList<RadialEntry> entries, int hovered, string title,
            Vector2? satCenter, float satInnerR, float satOuterR,
            IList<RadialEntry> satEntries, int satHovered, string satTitle,
            RadialEntry readoutEntry, string readoutHint, bool sticky,
            DynamicThing dragging = null, bool closeHovered = false,
            string pageText = null, string satPageText = null)
        {
            EnsureCanvas();
            _group.alpha = 1f;
            _canvas.gameObject.SetActive(true);
            UpdateCloseButton(center, innerR, closeHovered);
            UpdatePageLabel(_pageMain, pageText, center, outerR + 24f);
            UpdatePageLabel(_pageSat, satPageText, satCenter ?? center, satOuterR + 18f);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            // Slightly nicer open curve (ease out)
            float openT = EaseOut01(_openAnim);
            _openAnim = Mathf.Lerp(_openAnim, 1f, dt * AnimSpeed);

            bool satActive = satEntries != null && satCenter.HasValue;

            _main.Render(center, innerR, outerR, entries, hovered, openT, dimmed: satActive, dragging);

            if (satActive)
                _satellite.Render(satCenter.Value, satInnerR, satOuterR, satEntries, satHovered, openT, dimmed: false, dragging);
            else
                _satellite.Hide();

            // Option A: the DETAIL readout moves into the middle of the child radial while
            // one is open, but the main hub keeps its circle, title and colours — both
            // hubs stay dressed (visible in the editor side by side).
            if (UIAConfig.IsA && satActive)
            {
                _readout.Render(satCenter.Value, Mathf.Max(satInnerR - 2f, 46f), satTitle ?? title, null,
                    readoutEntry, readoutHint, sticky);
                _readoutHome.Render(center, innerR, title, null, null, "RMB: back", sticky);
            }
            else
            {
                _readout.Render(center, innerR, title, satTitle, readoutEntry, readoutHint, sticky);
                _readoutHome.Hide();
            }
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
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _group = null;
            _main = null;
            _satellite = null;
            _readout = null;
            _readoutHome = null;
            _closeRoot = null;
            _closeBand = null;
            _closeLabel = null;
            _pageMain = null;
            _pageSat = null;
            _font = null;
            _fontFor = null;
            _openAnim = 0f;
        }

        // ---------- infrastructure ----------

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("UIAscended_RadialCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
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
            _readoutHome = new ReadoutView(go.transform, Font());

            // Hub CLOSE button — created last so it draws over the hub backing.
            var cgo = new GameObject("CloseButton", typeof(RectTransform));
            cgo.transform.SetParent(go.transform, false);
            _closeRoot = (RectTransform)cgo.transform;
            _closeRoot.anchorMin = _closeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _closeRoot.sizeDelta = Vector2.zero;

            var bandGo = new GameObject("Band", typeof(RectTransform));
            bandGo.transform.SetParent(_closeRoot, false);
            _closeBand = bandGo.AddComponent<RadialWedgeGraphic>();
            _closeBand.raycastTarget = false;

            var clGo = new GameObject("Label", typeof(RectTransform));
            clGo.transform.SetParent(_closeRoot, false);
            _closeLabel = clGo.AddComponent<TextMeshProUGUI>();
            _closeLabel.font = Font();
            _closeLabel.alignment = TextAlignmentOptions.Center;
            _closeLabel.enableAutoSizing = true;
            _closeLabel.fontSizeMin = 8f;
            _closeLabel.fontSizeMax = 15f;
            _closeLabel.enableWordWrapping = false;
            _closeLabel.raycastTarget = false;

            _pageMain = MakePageLabel(go.transform, "PageMain");
            _pageSat = MakePageLabel(go.transform, "PageSat");
        }

        private static TextMeshProUGUI MakePageLabel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = Font();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 9f;
            tmp.fontSizeMax = 14f;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 18f);
            return tmp;
        }

        /// <summary>The "1/2 - Q: next page" counter above a paged ring.</summary>
        private static void UpdatePageLabel(TextMeshProUGUI label, string text, Vector2 imguiCenter, float yAbove)
        {
            if (label == null) return;
            bool show = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(show);
            if (!show) return;
            SyncFont(label);
            label.fontStyle = WedgeFontStyle();
            label.text = WedgeText(text);
            label.color = RadialPalette.TextDim.Value;
            label.rectTransform.anchoredPosition = CanvasAnchoredPos(imguiCenter) + new Vector2(0f, yAbove);
        }

        /// <summary>The always-available exit: a band hugging the bottom of the hub circle.
        /// Anchored to the MAIN hub even while the readout has moved into a satellite.</summary>
        private static void UpdateCloseButton(Vector2 center, float innerR, bool hovered)
        {
            if (_closeRoot == null) return;
            _closeRoot.anchoredPosition = CanvasAnchoredPos(center);
            float hubR = innerR - 6f;

            _closeBand.SetGeometry(hubR * 0.52f, hubR * 0.94f, Mathf.PI / 3f, Mathf.PI * 2f / 3f, fullRing: false);
            _closeBand.SideBorders = false;
            _closeBand.FeatherSides = true;
            _closeBand.SideFeatherLimit = float.MaxValue;
            _closeBand.BorderWidth = 1.6f;
            _closeBand.BorderColor = RadialPalette.HubBorder.Value;
            _closeBand.color = hovered ? RadialPalette.HubCloseHover.Value : RadialPalette.HubCloseFill.Value;
            _closeBand.RimHighlight = 0f;
            _closeBand.RefreshGeometry();

            SyncFont(_closeLabel);
            _closeLabel.fontStyle = WedgeFontStyle();
            _closeLabel.text = WedgeText("Close");
            _closeLabel.color = RadialPalette.HubCloseText.Value;
            _closeLabel.rectTransform.sizeDelta = new Vector2(hubR * 1.1f, 18f);
            _closeLabel.rectTransform.anchoredPosition = new Vector2(0f, -hubR * 0.73f);
        }

        /// <summary>
        /// The radial font. Resolution order: the configured name (substring match), else a
        /// font whose name says Bold (the Option A bold-caps look), else whatever the game
        /// loaded first. Re-resolves live when the config value changes (radial editor).
        /// </summary>
        internal static TMP_FontAsset Font()
        {
            string want = UIAConfig.RadialFontName != null ? UIAConfig.RadialFontName.Value : "";
            if (_font != null && _fontFor == want) return _font;
            _font = ResolveFont(want);
            _fontFor = want;
            _fontIsBoldAsset = _font != null
                && _font.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0;
            return _font;
        }

        /// <summary>Faux-bold only when the asset itself isn't a Bold face already.</summary>
        internal static FontStyles WedgeFontStyle()
            => _fontIsBoldAsset ? FontStyles.Normal : FontStyles.Bold;

        private static TMP_FontAsset ResolveFont(string want)
        {
            TMP_FontAsset first = null;
            TMP_FontAsset bold = null;
            TMP_FontAsset named = null;
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all != null)
            {
                foreach (var f in all)
                {
                    if (f == null) continue;
                    if (first == null) first = f;
                    if (bold == null && f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0)
                        bold = f;
                    if (named == null && !string.IsNullOrEmpty(want)
                        && f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                        named = f;
                }
            }
            var chosen = named ?? bold ?? first;
            if (chosen == null) chosen = TMP_Settings.defaultFontAsset;
            return chosen;
        }

        /// <summary>Every distinct TMP font the game has loaded — the radial editor's dropdown.</summary>
        internal static List<string> AllFontNames()
        {
            var names = new List<string>();
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all == null) return names;
            foreach (var f in all)
                if (f != null && !names.Contains(f.name))
                    names.Add(f.name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Cheap per-frame font sync (no-op unless the config changed) — shared by
        /// every overlay view so a font picked in the radial editor propagates everywhere.</summary>
        internal static void SyncFont(TextMeshProUGUI tmp)
        {
            var f = Font();
            if (f != null && tmp.font != f) tmp.font = f;
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

        internal static string WedgeText(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return UIAConfig.RadialUppercaseLabels != null && UIAConfig.RadialUppercaseLabels.Value
                ? s.ToUpperInvariant() : s;
        }

        // ---------- one ring ----------

        private sealed class RingView
        {
            private readonly RectTransform _root;
            private readonly List<RadialWedgeGraphic> _wedges = new List<RadialWedgeGraphic>();
            private readonly List<Image> _icons = new List<Image>();
            private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
            private readonly List<TextMeshProUGUI> _states = new List<TextMeshProUGUI>();
            private readonly List<TriangleGraphic> _triUp = new List<TriangleGraphic>();
            private readonly List<TriangleGraphic> _triDown = new List<TriangleGraphic>();

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
                IList<RadialEntry> entries, int hovered, float openAnim, bool dimmed,
                DynamicThing dragging)
            {
                _root.gameObject.SetActive(true);
                _root.anchoredPosition = CanvasAnchoredPos(center);
                _root.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, openAnim);

                int count = entries?.Count ?? 0;
                EnsureCapacity(count);

                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
                float sector = count > 0 ? Mathf.PI * 2f / count : 0f;
                float ringWidth = outerR - innerR;

                float gapRad = UIAConfig.RadialWedgeGapDeg != null
                    ? UIAConfig.RadialWedgeGapDeg.Value * Mathf.Deg2Rad : 0f;
                if (count > 1) gapRad = Mathf.Min(gapRad, sector * 0.3f);
                bool sideBorders = UIAConfig.RadialSideBorders == null || UIAConfig.RadialSideBorders.Value;
                float dimStrength = UIAConfig.RadialDimShading != null && !UIAConfig.RadialDimShading.Value ? 0f
                    : UIAConfig.RadialDimStrength != null ? UIAConfig.RadialDimStrength.Value : 1f;

                for (int i = 0; i < _wedges.Count; i++)
                {
                    bool used = i < count;
                    _wedges[i].gameObject.SetActive(used);
                    _icons[i].gameObject.SetActive(used);
                    _labels[i].gameObject.SetActive(used);
                    _states[i].gameObject.SetActive(used);
                    _triUp[i].gameObject.SetActive(used);
                    _triDown[i].gameObject.SetActive(used);
                    if (!used) continue;

                    var entry = entries[i];
                    var wedge = _wedges[i];

                    float a0 = -Mathf.PI * 0.5f - sector * 0.5f + sector * i;
                    bool lone = count == 1;
                    float g = lone ? 0f : gapRad * 0.5f;
                    wedge.SetGeometry(innerR, outerR, a0 + g, a0 + sector - g, fullRing: lone);
                    wedge.SideBorders = sideBorders && !lone;
                    wedge.FeatherSides = !lone && gapRad > 0.0005f;
                    // Outward side fringe may fill at most half the gap, or it bleeds into
                    // the neighbour (gap is angular; convert to px at the band's mid radius).
                    wedge.SideFeatherLimit = lone ? float.MaxValue
                        : gapRad * 0.5f * (innerR + outerR) * 0.5f;
                    wedge.SideWidthInner = UIAConfig.RadialSideWidthInner != null
                        ? UIAConfig.RadialSideWidthInner.Value : 3.2f;
                    wedge.SideWidthOuter = UIAConfig.RadialSideWidthOuter != null
                        ? UIAConfig.RadialSideWidthOuter.Value : 3.2f;

                    bool isHovered = i == hovered && entry.Enabled;

                    // Dynamic background shading: while one wedge is highlighted the others
                    // recede. Strength is user-tunable, 0 disables (Jackson's shading, exposed).
                    Color fillTarget = ResolveUguiFill(entry, isHovered, dimmed, dragging);
                    Color borderTarget = ResolveUguiBorder(entry, isHovered, dimmed);

                    if (hovered >= 0 && !isHovered && dimStrength > 0f)
                    {
                        float rgbMult = 1f - 0.38f * dimStrength;
                        float aMult = 1f - 0.48f * dimStrength;
                        fillTarget = new Color(fillTarget.r * rgbMult, fillTarget.g * rgbMult,
                            fillTarget.b * rgbMult, fillTarget.a * aMult);
                    }

                    // Lerp current toward target (smooth)
                    wedge.color = Color.Lerp(wedge.color, fillTarget, dt * AnimSpeed);

                    float bulgeTarget = isHovered ? HoverBulge : 0f;
                    if (!Mathf.Approximately(wedge.OuterBulge, bulgeTarget))
                    {
                        wedge.OuterBulge = Mathf.Lerp(wedge.OuterBulge, bulgeTarget, dt * AnimSpeed);
                    }

                    // Border: always on (the orange outline), hover shifts its colour.
                    float bw = UIAConfig.RadialBorderWidth.Value;
                    float borderWTarget = isHovered ? bw * 1.2f : bw;
                    wedge.BorderWidth = Mathf.Lerp(wedge.BorderWidth, borderWTarget, dt * AnimSpeed * 0.7f);
                    wedge.BorderColor = Color.Lerp(wedge.BorderColor, borderTarget, dt * AnimSpeed);

                    // Rim gloss: subtle, and only really visible on the selected wedge.
                    // Intensity is the "shader look" knob (0 = flat), colour comes from the
                    // RimShine palette entry.
                    float shineIntensity = UIAConfig.RadialShineIntensity != null
                        ? UIAConfig.RadialShineIntensity.Value : 1f;
                    float shineTarget = (isHovered ? 0.55f : 0.10f) * shineIntensity;
                    wedge.RimHighlight = Mathf.Lerp(wedge.RimHighlight, shineTarget, dt * AnimSpeed);

                    // Any geometry property (bulge/border/rim) changed -> rebuild mesh this frame
                    wedge.RefreshGeometry();

                    // === Content placement (icon + label + state) with hover pop ===
                    float aMid = a0 + sector * 0.5f;
                    float baseMidR = (innerR + outerR) * 0.5f;
                    float hoverExtra = isHovered ? (HoverBulge * 0.32f + HoverContentPop) : 0f;
                    float midR = baseMidR + hoverExtra;

                    var dir = new Vector2(Mathf.Cos(aMid), -Mathf.Sin(aMid));
                    var slot = dir * midR;

                    // Icons scale WITH the wedge: bounded by the band's thickness and by the
                    // wedge's width at mid radius, times the user's ratio.
                    float halfAngleIc = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chordIc = 2f * baseMidR * Mathf.Sin(halfAngleIc);
                    float iconRatio = UIAConfig.RadialIconRatio != null ? UIAConfig.RadialIconRatio.Value : 0.62f;
                    float iconSize = Mathf.Min(ringWidth, chordIc) * iconRatio;
                    float contentScale = isHovered ? HoverScale : 1f;
                    bool showLabels = UIAConfig.RadialShowWedgeLabels.Value;

                    if (entry.IsScrollAdjust)
                    {
                        RenderScrollWedge(i, entry, slot, ringWidth, isHovered, dimmed);
                        continue;
                    }
                    _triUp[i].gameObject.SetActive(false);
                    _triDown[i].gameObject.SetActive(false);

                    var icon = _icons[i];
                    // STOW wedges show the slot; hovering previews the item that would go in.
                    Sprite sprite = isHovered && entry.HoverIcon != null ? entry.HoverIcon : entry.Icon;
                    icon.sprite = sprite;
                    icon.enabled = sprite != null;
                    float iconAlpha = (entry.Enabled ? 1f : 0.32f) * (dimmed ? 0.45f : 1f);
                    icon.color = new Color(1f, 1f, 1f, iconAlpha);
                    icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize) * contentScale;
                    // With labels hidden the icon owns the wedge, so centre it in the band.
                    icon.rectTransform.anchoredPosition = showLabels
                        ? slot + dir * (ringWidth * 0.05f) + new Vector2(0f, ringWidth * 0.12f)
                        : slot;

                    // Live state under the icon: which battery is full, which canister is empty.
                    var state = _states[i];
                    bool showState = UIAConfig.RadialShowStateText.Value && !string.IsNullOrEmpty(entry.StateText);
                    state.gameObject.SetActive(showState);
                    if (showState)
                    {
                        SyncFont(state);
                        state.fontStyle = WedgeFontStyle();
                        state.text = entry.StateText;
                        Color sc = RadialPalette.TextPrimary.Value;
                        if (dimmed) sc.a *= 0.5f;
                        state.color = sc;
                        state.rectTransform.sizeDelta = new Vector2(Mathf.Max(64f, iconSize * 1.6f), 15f);
                        state.rectTransform.anchoredPosition =
                            icon.rectTransform.anchoredPosition
                            - new Vector2(0f, iconSize * contentScale * 0.5f + 8f);
                    }

                    var label = _labels[i];
                    // The hub readout already names whatever is selected; per-wedge names just
                    // collide with neighbouring icons on a crowded belt.
                    bool labelOn = showLabels || (sprite == null && !string.IsNullOrEmpty(entry.Label));
                    label.gameObject.SetActive(labelOn);
                    if (!labelOn) continue;

                    SyncFont(label);
                    label.fontStyle = WedgeFontStyle();
                    label.text = WedgeText(entry.Label);
                    label.color = entry.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDisabled.Value;
                    label.rectTransform.localScale = Vector3.one * contentScale;

                    float halfAngle = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chord = 2f * midR * Mathf.Sin(halfAngle);
                    float boxW = Mathf.Clamp(Mathf.Abs(Mathf.Cos(aMid)) * ringWidth + Mathf.Abs(Mathf.Sin(aMid)) * chord - 6f,
                                             42f, ringWidth * 2.5f);
                    if (sprite == null)
                    {
                        // Text-only wedge (STOW, controls): the label owns the middle of the band.
                        label.rectTransform.sizeDelta = new Vector2(boxW, ringWidth * 0.5f);
                        label.rectTransform.anchoredPosition = slot;
                        label.alignment = TextAlignmentOptions.Center;
                    }
                    else
                    {
                        label.rectTransform.sizeDelta = new Vector2(boxW, ringWidth * 0.48f);
                        label.rectTransform.anchoredPosition =
                            slot - dir * (ringWidth * 0.02f) -
                            new Vector2(0f, iconSize * contentScale * 0.48f + 1f);
                        label.alignment = TextAlignmentOptions.Top;
                    }
                }
            }

            /// <summary>A scroll-adjustable value wedge: NAME on top, then an up triangle,
            /// the live value, and a down triangle — nudged by the mouse wheel.</summary>
            private void RenderScrollWedge(int i, RadialEntry entry, Vector2 slot,
                float ringWidth, bool isHovered, bool dimmed)
            {
                _icons[i].enabled = false;
                var accent = isHovered ? RadialPalette.WedgeBorderHover.Value : RadialPalette.WedgeBorder.Value;
                if (dimmed) accent.a *= 0.5f;

                var label = _labels[i];
                label.gameObject.SetActive(true);
                SyncFont(label);
                label.fontStyle = WedgeFontStyle();
                label.text = WedgeText(entry.Label);
                label.color = entry.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDisabled.Value;
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.localScale = Vector3.one;
                label.rectTransform.sizeDelta = new Vector2(Mathf.Max(72f, ringWidth), 16f);
                label.rectTransform.anchoredPosition = slot + new Vector2(0f, 26f);

                var up = _triUp[i];
                up.gameObject.SetActive(true);
                up.Configure(pointsUp: true, size: 11f);
                up.color = accent;
                up.rectTransform.anchoredPosition = slot + new Vector2(0f, 11f);
                up.SetVerticesDirty();

                var state = _states[i];
                state.gameObject.SetActive(true);
                SyncFont(state);
                state.fontStyle = WedgeFontStyle();
                string v = null;
                try { v = entry.ValueText?.Invoke(); } catch { }
                state.text = v ?? entry.StateText ?? "";
                Color sc = RadialPalette.TextPrimary.Value;
                if (dimmed) sc.a *= 0.5f;
                state.color = sc;
                state.rectTransform.sizeDelta = new Vector2(Mathf.Max(72f, ringWidth), 16f);
                state.rectTransform.anchoredPosition = slot - new Vector2(0f, 2f);

                var down = _triDown[i];
                down.gameObject.SetActive(true);
                down.Configure(pointsUp: false, size: 11f);
                down.color = accent;
                down.rectTransform.anchoredPosition = slot - new Vector2(0f, 15f);
                down.SetVerticesDirty();
            }

            private static Color ResolveUguiFill(RadialEntry entry, bool hovered, bool dimmed,
                DynamicThing dragging)
            {
                Color col;
                if (!entry.Enabled)
                    col = RadialPalette.WedgeDisabled.Value;
                else if (dragging != null && hovered)
                    // Dragging an item: the hovered wedge turns orange when it would accept
                    // the drop, and reads disabled when it wouldn't.
                    col = entry.AcceptsDrop && entry.ResolveDrop(dragging) != null
                        ? RadialPalette.WedgeStowHover.Value
                        : RadialPalette.WedgeDisabled.Value;
                else if (entry.StowStyle)
                    // Option A STOW wedge: quiet until hovered, then the orange "goes here".
                    col = hovered ? RadialPalette.WedgeStowHover.Value : RadialPalette.WedgeBg.Value;
                else if (entry.FillOverride.HasValue)
                    col = hovered ? RadialPalette.WedgeStowHover.Value : RadialPalette.WedgeStow.Value;
                else if (hovered)
                    col = RadialPalette.WedgeHover.Value;
                else
                    col = RadialPalette.WedgeBg.Value;

                if (dimmed) col.a *= 0.48f;
                return col;
            }

            /// <summary>The always-on outline. Hover shifts it to the hover border colour.</summary>
            private static Color ResolveUguiBorder(RadialEntry entry, bool hovered, bool dimmed)
            {
                Color border = !entry.Enabled
                    ? RadialPalette.WedgeDisabled.Value
                    : hovered ? RadialPalette.WedgeBorderHover.Value
                              : RadialPalette.WedgeBorder.Value;
                if (dimmed) border.a *= 0.55f;
                return border;
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

                    var sgo = new GameObject("State" + idx, typeof(RectTransform));
                    sgo.transform.SetParent(_root, false);
                    var stmp = sgo.AddComponent<TextMeshProUGUI>();
                    stmp.font = Font();
                    stmp.alignment = TextAlignmentOptions.Center;
                    stmp.enableAutoSizing = true;
                    stmp.fontSizeMin = 8f;
                    stmp.fontSizeMax = 12f;
                    stmp.enableWordWrapping = false;
                    stmp.overflowMode = TextOverflowModes.Overflow;
                    stmp.raycastTarget = false;
                    _states.Add(stmp);

                    var ugo = new GameObject("TriUp" + idx, typeof(RectTransform));
                    ugo.transform.SetParent(_root, false);
                    var triU = ugo.AddComponent<TriangleGraphic>();
                    triU.raycastTarget = false;
                    triU.rectTransform.sizeDelta = new Vector2(14f, 12f);
                    _triUp.Add(triU);

                    var dgo = new GameObject("TriDown" + idx, typeof(RectTransform));
                    dgo.transform.SetParent(_root, false);
                    var triD = dgo.AddComponent<TriangleGraphic>();
                    triD.raycastTarget = false;
                    triD.rectTransform.sizeDelta = new Vector2(14f, 12f);
                    _triDown.Add(triD);
                }
            }
        }

        // ---------- center readout ----------

        private sealed class ReadoutView
        {
            private readonly RectTransform _root;
            private readonly CircleGraphic _hubBacking;
            private readonly TextMeshProUGUI _title, _verb, _label, _sub, _warn;

            public ReadoutView(Transform parent, TMP_FontAsset font)
            {
                var go = new GameObject("Readout", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _root = (RectTransform)go.transform;
                _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);

                // A CircleGraphic, not an Image: an Image with no sprite renders a white QUAD,
                // which is why the hub used to be a square.
                var hub = new GameObject("HubBacking", typeof(RectTransform));
                hub.transform.SetParent(_root, false);
                _hubBacking = hub.AddComponent<CircleGraphic>();
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

            public void Hide() => _root.gameObject.SetActive(false);

            public void Render(Vector2 center, float innerR, string title, string satTitle,
                RadialEntry hovered, string hint, bool sticky)
            {
                _root.gameObject.SetActive(true);
                _root.anchoredPosition = CanvasAnchoredPos(center);
                float w = (innerR - 6f) * 1.85f;
                // The readout also serves small satellite hubs (Option A): squeeze the
                // line spacing down with the radius so five lines still fit the circle.
                float s = Mathf.Clamp(innerR / 110f, 0.45f, 1f);

                SyncFont(_title); SyncFont(_verb); SyncFont(_label); SyncFont(_sub); SyncFont(_warn);

                if (_hubBacking != null)
                {
                    _hubBacking.rectTransform.anchoredPosition = Vector2.zero;
                    _hubBacking.SetRadius(innerR - 6f);
                    _hubBacking.color = RadialPalette.HubFill.Value;
                    _hubBacking.BorderColor = RadialPalette.HubBorder.Value;
                    _hubBacking.BorderWidth = UIAConfig.RadialBorderWidth.Value * 0.8f;
                    _hubBacking.Refresh();
                }

                Place(_title, 38f * s, w);
                Place(_verb, 13f * s, w);
                Place(_label, -7f * s, w);
                Place(_sub, -25f * s, w);
                Place(_warn, -43f * s, w);

                _title.text = satTitle ?? title ?? string.Empty;
                _title.color = RadialPalette.TextDim.Value;

                if (hovered == null)
                {
                    _verb.text = hint ?? (sticky
                        ? (UIAConfig.IsB ? "MMB/LMB select | RMB back" : "LMB select | RMB back")
                        : (UIAConfig.IsB ? "hover to dive | release to cancel" : "release to cancel"));
                    _verb.color = RadialPalette.TextDim.Value;
                    _label.text = _sub.text = _warn.text = string.Empty;
                    return;
                }

                _verb.text = hovered.ActionText ?? (hovered.IsBranch ? "Open" : "Select");
                _verb.color = hovered.Enabled ? RadialPalette.TextAccent.Value : RadialPalette.TextDim.Value;
                // Action wedges often ARE their verb ("Replace", "Stabilizer Off") — don't
                // print the same word twice in the readout.
                _label.text = string.Equals(hovered.Label, _verb.text, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty : hovered.Label ?? string.Empty;
                _label.color = hovered.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDim.Value;
                _sub.text = hovered.Sublabel ?? string.Empty;
                _sub.color = RadialPalette.TextDim.Value;

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
                    // Bug 6: the hovered item's live stat (canister kPa, battery %, stack xN,
                    // filter %, device value) in the center readout, so EVERY radial surfaces
                    // it the same way — not just the toolbelt radial (which baked it into its
                    // sublabel). StateText carries TMP tags; the readout is TMP, so they render.
                    string stat = null;
                    try { stat = hovered.ValueText != null ? hovered.ValueText() : hovered.StateText; }
                    catch { }
                    _warn.text = stat ?? string.Empty;
                    _warn.color = RadialPalette.TextAccent.Value;
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
