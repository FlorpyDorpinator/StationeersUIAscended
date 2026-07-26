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

        // --- 0.9.0 radial glass FX (frosted backdrop + sheen/edge-light). Resolved once per
        // Render, pushed onto every wedge and the close band. Statics so the nested RingView and
        // UpdateCloseButton read one consistent frame's state.
        private static bool _fxFrostActive;    // assign the shared glass material to wedges this frame
        private static float _fxFrostStrength; // uv0.x volume when active, else 0
        private static float _fxSheen;
        private static float _fxEdgeLight;
        private static bool _fxDemand;         // a frost-enabled radial is visible (backdrop demand)

        /// <summary>True while a frost-enabled radial is on screen and the HUD's Tier C master is on.
        /// HudSystem folds this into its backdrop gate so the blurred-screen capture keeps running for
        /// the radial even when the HUD's own elements wouldn't ask for it. Cleared on Hide/Shutdown.</summary>
        public static bool RadialFrostWanted => _fxDemand;

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
            UpdateRadialFx();
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

            // The DETAIL readout moves into the middle of the child radial while one is open,
            // but the main hub keeps its circle, title and colours — both hubs stay dressed
            // (visible in the editor side by side).
            if (satActive)
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
            _fxDemand = false; // no visible radial ⇒ drop backdrop demand (HudSystem stands it down)
            if (_canvas == null) return;
            _canvas.gameObject.SetActive(false);
            _openAnim = 0f;
            // Hand every wedge back to the stock UI material so a pooled wedge never keeps the glass
            // material while hidden (idempotent; a no-op when nothing was assigned).
            _main?.ClearFx();
            _satellite?.ClearFx();
            Hud.HudFxMaterials.Unassign(_closeBand);
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
            _fxDemand = false;
            _fxFrostActive = false;
            _fxFrostStrength = 0f;
        }

        // ---------- 0.9.0 glass FX ----------

        /// <summary>Resolve this frame's radial glass-FX state from config + Tier C availability, and
        /// declare backdrop demand. Cheap (a handful of reads); called once per Render.</summary>
        private static void UpdateRadialFx()
        {
            _fxSheen = UIAConfig.RadialSheen != null ? UIAConfig.RadialSheen.Value : 0f;
            _fxEdgeLight = UIAConfig.RadialEdgeLight != null ? UIAConfig.RadialEdgeLight.Value : 0f;

            // Demand = the user wants frost AND the HUD's Tier C master is on. Set true even before
            // the backdrop is running so HudSystem's gate spins the capture up for us; the material
            // assignment below then engages once it is Active + the bundle resolved.
            bool frostCfg = UIAConfig.RadialFrost != null && UIAConfig.RadialFrost.Value
                && Hud.HudConfig.FxTierC != null && Hud.HudConfig.FxTierC.Value;
            _fxDemand = frostCfg;

            _fxFrostActive = frostCfg && Hud.HudBackdrop.Active
                && HudShaderStore.TierBAvailable && Hud.HudFxMaterials.Available;
            _fxFrostStrength = _fxFrostActive
                ? (UIAConfig.RadialFrostStrength != null ? UIAConfig.RadialFrostStrength.Value : 0.85f)
                : 0f;
        }

        /// <summary>Push this frame's glass FX onto one wedge (or the close band): sheen/edge-light are
        /// pure vertex colour (always applied, default 0 = the flat look), the shared glass material is
        /// assigned only while frost is active. Idempotent + per-frame cheap — the setters are
        /// dirty-guarded and Assign/Unassign are dictionary lookups.</summary>
        private static void ApplyWedgeFx(RadialWedgeGraphic w)
        {
            if (w == null) return;
            w.Sheen = _fxSheen;
            w.EdgeLight = _fxEdgeLight;
            w.FxStrength = _fxFrostStrength;
            if (_fxFrostActive)
            {
                if (!Hud.HudFxMaterials.Assign(w, "glass")) Hud.HudFxMaterials.Unassign(w);
            }
            else Hud.HudFxMaterials.Unassign(w);
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
            ApplyWedgeFx(_closeBand);
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
            // A small outward chevron on any wedge that can be swiped past the rim (has a
            // slide-out satellite or is a click-in branch) — the "you can swipe this" hint.
            private readonly List<TriangleGraphic> _swipe = new List<TriangleGraphic>();
            // #4: the bound hotkey letter, badged near the HUB side of a setting wedge.
            private readonly List<TextMeshProUGUI> _hotkey = new List<TextMeshProUGUI>();
            // 1B.3: a grey tool-type binding label on a stable-geometry belt slot wedge
            // (occupied OR empty-but-bound), near the HUB side. Parallels _hotkey.
            private readonly List<TextMeshProUGUI> _binding = new List<TextMeshProUGUI>();

            // Per-wedge string caches (parallel to the pools above), so the per-frame draw
            // allocates nothing while a radial is open. WedgeText (ToUpperInvariant) is cached
            // keyed on the source-label reference; the hotkey badge letter caches its one-char
            // string keyed on the char. All self-heal on any change — no reset needed.
            private readonly List<string> _labelSrc = new List<string>();
            private readonly List<string> _labelDisplay = new List<string>();
            private readonly List<char> _lastBadgeLetter = new List<char>();
            private readonly List<string> _badgeStr = new List<string>();
            // Binding-label display cache (WedgeText'd), keyed on the source-label reference.
            private readonly List<string> _bindingSrc = new List<string>();
            private readonly List<string> _bindingDisplay = new List<string>();
            // Ctrl+digit bag badges ("^0".."^9") are constant — no per-frame concat.
            private static readonly string[] BagDigitBadges =
                { "^0", "^1", "^2", "^3", "^4", "^5", "^6", "^7", "^8", "^9" };

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

            /// <summary>Hand every pooled wedge back to the stock UI material (radial closed).</summary>
            public void ClearFx()
            {
                for (int i = 0; i < _wedges.Count; i++)
                    Hud.HudFxMaterials.Unassign(_wedges[i]);
            }

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
                    if (!used) { _swipe[i].gameObject.SetActive(false); _hotkey[i].gameObject.SetActive(false); _binding[i].gameObject.SetActive(false); Hud.HudFxMaterials.Unassign(_wedges[i]); continue; }

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

                    // 0.9.0 glass FX (frost material + sheen/edge-light) — set before the rebuild
                    // below bakes sheen/edge-light/FxStrength into the mesh.
                    ApplyWedgeFx(wedge);

                    // Any geometry property (bulge/border/rim) changed -> rebuild mesh this frame
                    wedge.RefreshGeometry();

                    // === Content placement (icon + label + state) with hover pop ===
                    float aMid = a0 + sector * 0.5f;
                    float baseMidR = (innerR + outerR) * 0.5f;
                    float hoverExtra = isHovered ? (HoverBulge * 0.32f + HoverContentPop) : 0f;
                    float midR = baseMidR + hoverExtra;

                    var dir = new Vector2(Mathf.Cos(aMid), -Mathf.Sin(aMid));
                    var slot = dir * midR;

                    // Swipe affordance: a small outward chevron at the rim of any wedge that
                    // opens a satellite (slide-out) or a click-in branch — "you can swipe this".
                    // Scroll wedges never show it (they carry their own up/down triangles).
                    // A slide-out that yields NOTHING (a tool with no settings/slots — wire
                    // cutters, a wrench) must not advertise a swipe it can't honour, so the
                    // slide-out arm is gated on real content, not just a non-null provider.
                    bool swipeable = !entry.IsScrollAdjust && entry.Enabled
                        && ((entry.HasSlideOut && entry.SlideOutHasContent()) || entry.IsBranch);
                    var sw = _swipe[i];
                    sw.gameObject.SetActive(swipeable);
                    if (swipeable)
                    {
                        sw.Configure(pointsUp: true, size: 5f);
                        float swAlpha = (isHovered ? 0.95f : 0.45f) * (dimmed ? 0.4f : 1f);
                        var swc = RadialPalette.TextPrimary.Value; swc.a *= swAlpha;
                        sw.color = swc;
                        var swRt = sw.rectTransform;
                        swRt.anchoredPosition = dir * (outerR - 9f);
                        swRt.localEulerAngles = new Vector3(0f, 0f,
                            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                        sw.SetVerticesDirty();
                    }

                    // The bound-hotkey badge near the HUB (inner) side of a wedge: a device
                    // SETTING wedge shows its bound LETTER (#4); a bindable BAG shows its
                    // Ctrl+digit as "^N" (#3). Set before the scroll-wedge early-return so scroll
                    // settings (suit pressure/temp) show their letter too.
                    var hk = _hotkey[i];
                    string badge = null;
                    if (entry.CanHotkey)
                    {
                        char letter = Core.WedgeHotkeys.LetterFor(entry.HotkeyInteractable);
                        if (letter != '\0')
                        {
                            // Rebuild the one-char badge string only when the bound letter changes.
                            if (letter != _lastBadgeLetter[i]) { _lastBadgeLetter[i] = letter; _badgeStr[i] = letter.ToString(); }
                            badge = _badgeStr[i];
                        }
                    }
                    else if (entry.BindableBag != null)
                    {
                        int digit = global::StationeersUIMod.Features.BagHotkeyStore.DigitForBag(entry.BindableBag);
                        if (digit >= 0) badge = digit < BagDigitBadges.Length ? BagDigitBadges[digit] : "^" + digit; // ^ = Ctrl
                    }
                    bool showHk = badge != null;
                    hk.gameObject.SetActive(showHk);
                    if (showHk)
                    {
                        hk.rectTransform.anchoredPosition = dir * (innerR + 11f);
                        var hkc = RadialPalette.TextPrimary.Value; hkc.a *= (dimmed ? 0.5f : 1f);
                        hk.color = hkc;
                        if (hk.text != badge) hk.text = badge;
                    }

                    // 1B.3: grey tool-type binding label on a stable-geometry belt slot wedge. Sits
                    // just outside the hub so an empty-but-bound reserved slot still reads as "the
                    // <tool> goes here". Only when the entry carries one, stable geometry is on AND
                    // the player wants the ghost labels at all (ShowBindingLabels, the post-0.9.2.5 play-test round — it
                    // replaced the old curved/straight toggle; curved is the only rendering now).
                    // Cached WedgeText per slot (self-heals on ref change) — no per-frame alloc.
                    var bind = _binding[i];
                    bool showBinding = !entry.IsScrollAdjust && entry.BindingLabel != null
                        && UIAConfig.ToolbeltStableGeometry != null && UIAConfig.ToolbeltStableGeometry.Value
                        && (UIAConfig.RadialShowBindingLabels == null || UIAConfig.RadialShowBindingLabels.Value);
                    bind.gameObject.SetActive(showBinding);
                    if (showBinding)
                    {
                        if (!ReferenceEquals(entry.BindingLabel, _bindingSrc[i]))
                        {
                            _bindingSrc[i] = entry.BindingLabel;
                            _bindingDisplay[i] = WedgeText(entry.BindingLabel);
                        }
                        SyncFont(bind);
                        bind.fontStyle = WedgeFontStyle();
                        if (bind.text != _bindingDisplay[i]) bind.text = _bindingDisplay[i];
                        Color bc = RadialPalette.TextBinding.Value;   // its own colour, default grey
                        if (dimmed) bc.a *= 0.5f;
                        bind.color = bc;
                        // Sit the bound-tool name at the BOTTOM of the wedge — its inner end — hugging
                        // the arc where the HUB begins, and run it TANGENTIALLY along that arc rather
                        // than out along the spoke (FlorpyDorp's sketch). Two consequences worth
                        // knowing: the text follows the ring instead of pointing at the centre, and the
                        // whole middle of the wedge is freed for the icon + its live value.
                        float bH = 13f;
                        float bR = innerR + 4f + bH * 0.5f;            // just clear of the hub rim
                        float bTheta = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                        // Tangent to the hub circle; flipped on the LOWER half so a bottom wedge's
                        // name is never upside-down (top half reads with "up" pointing outward,
                        // bottom half with "up" pointing inward — the usual arc-label convention).
                        float bAng = dir.y >= 0f ? bTheta - 90f : bTheta + 90f;
                        // Bound by the wedge's CHORD at this radius, so a long tool name auto-sizes
                        // down (the pool enables autosizing 7..11.5) instead of running into the
                        // neighbouring wedge's border.
                        float bHalf = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                        float bLen = Mathf.Clamp(2f * bR * Mathf.Sin(bHalf) - 8f, 28f, 220f);
                        var brt = bind.rectTransform;
                        brt.pivot = new Vector2(0.5f, 0.5f);
                        brt.localRotation = Quaternion.Euler(0f, 0f, bAng);
                        brt.anchoredPosition = dir * bR;
                        brt.sizeDelta = new Vector2(bLen, bH);
                        bind.alignment = TextAlignmentOptions.Center;
                        bind.enableWordWrapping = false;
                        bind.overflowMode = TextOverflowModes.Ellipsis;
                        // Bend it GLYPH BY GLYPH around the hub so it truly follows the arc instead
                        // of being a straight line merely rotated to the tangent. The rect placement
                        // above already put it on the ring at the right angle; this only re-lays the
                        // glyphs inside that local space, so the two compose. Must run AFTER the
                        // text/size/rotation are final — it reads the built mesh. (Unconditional
                        // since the post-0.9.2.5 play-test round: curved was always the intended look, so the straight-line
                        // fallback retired with the BindingLabelCurved toggle.)
                        RadialArcText.Curve(bind, bR, dir.y >= 0f);
                    }

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
                        RenderScrollWedge(i, entry, slot, dir, chordIc, ringWidth, isHovered, dimmed);
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

                    // Live value (kPa / % / xN): horizontal, sitting just BELOW the icon's bottom
                    // edge (never on top of it), and auto-sized to the wedge's chord width AT THE
                    // VALUE'S OWN RADIUS so it can never reach a neighbouring wedge's border. No
                    // backing — white on the dark wedge reads fine.
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
                        state.rectTransform.localEulerAngles = Vector3.zero;
                        state.enableWordWrapping = false;
                        // Clear of the icon (below its bottom edge, not overlapping it).
                        Vector2 sp = icon.rectTransform.anchoredPosition
                            - new Vector2(0f, iconSize * contentScale * 0.5f + 9f);
                        state.rectTransform.anchoredPosition = sp;
                        // Box = the wedge's chord at this value's radius, so auto-size shrinks the
                        // text to fit within the wedge (down to the pool's min font) — never wider
                        // than the wedge, never onto a neighbour.
                        float vChord = 2f * sp.magnitude * Mathf.Sin(halfAngleIc);
                        state.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(vChord - 10f, 34f, 220f), 14f);
                    }

                    var label = _labels[i];
                    // The hub readout already names whatever is selected; per-wedge names just
                    // collide with neighbouring icons on a crowded belt.
                    bool labelOn = showLabels || (sprite == null && !string.IsNullOrEmpty(entry.Label));
                    label.gameObject.SetActive(labelOn);
                    if (!labelOn) continue;

                    // Cache WedgeText (ToUpperInvariant) per wedge-slot: recompute only when the
                    // source-label reference changes, then reuse it for the assignment and the
                    // length check below.
                    if (!ReferenceEquals(entry.Label, _labelSrc[i]))
                    {
                        _labelSrc[i] = entry.Label;
                        _labelDisplay[i] = WedgeText(entry.Label);
                    }
                    string labelDisplay = _labelDisplay[i];

                    SyncFont(label);
                    label.fontStyle = WedgeFontStyle();
                    label.text = labelDisplay;
                    label.color = entry.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDisabled.Value;
                    label.rectTransform.localScale = Vector3.one * contentScale;

                    float halfAngle = Mathf.Min(sector * 0.5f, Mathf.PI * 0.5f);
                    float chord = 2f * midR * Mathf.Sin(halfAngle);
                    float boxW = Mathf.Clamp(Mathf.Abs(Mathf.Cos(aMid)) * ringWidth + Mathf.Abs(Mathf.Sin(aMid)) * chord - 6f,
                                             42f, ringWidth * 2.5f);
                    label.rectTransform.localEulerAngles = Vector3.zero; // pooled label: clear prior rotation
                    label.enableWordWrapping = true;                    // ...and prior no-wrap
                    if (sprite == null)
                    {
                        // #5: a long control label ("STABILIZER OFF") overflows a narrow wedge when
                        // drawn flat. Angle it along the wedge's radial spoke (kept upright, one line)
                        // so it runs down the band's length instead of clipping out the sides. Gated
                        // on a genuinely long label and a not-very-wide wedge (few-wedge rings, where
                        // the label already fits, stay flat).
                        float radialLen = ringWidth + (isHovered ? HoverBulge : 0f);
                        bool rotate = (UIAConfig.RadialRotateLongLabels == null || UIAConfig.RadialRotateLongLabels.Value)
                            && labelDisplay.Length >= 10
                            && boxW < ringWidth * 1.5f;
                        if (rotate)
                        {
                            float angDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                            if (angDeg > 90f) angDeg -= 180f; else if (angDeg < -90f) angDeg += 180f;
                            label.rectTransform.localEulerAngles = new Vector3(0f, 0f, angDeg);
                            label.rectTransform.sizeDelta = new Vector2(radialLen * 1.05f, ringWidth * 0.42f);
                            label.enableWordWrapping = false; // run as one line down the spoke
                        }
                        else
                        {
                            label.rectTransform.sizeDelta = new Vector2(boxW, ringWidth * 0.5f);
                        }
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
            private void RenderScrollWedge(int i, RadialEntry entry, Vector2 slot, Vector2 dir,
                float chordIc, float ringWidth, bool isHovered, bool dimmed)
            {
                _icons[i].enabled = false;
                var accent = isHovered ? RadialPalette.WedgeBorderHover.Value : RadialPalette.WedgeBorder.Value;
                if (dimmed) accent.a *= 0.5f;

                // Horizontal NAME / value / ▲▼ stack, auto-sized to the wedge width so a long name
                // ("THRUST") shrinks to fit instead of clipping (kept readable, never rotated).
                float sw = Mathf.Clamp(chordIc - 6f, 48f, 220f);

                var label = _labels[i];
                label.gameObject.SetActive(true);
                SyncFont(label);
                label.fontStyle = WedgeFontStyle();
                label.text = WedgeText(entry.Label);
                label.color = entry.Enabled ? RadialPalette.TextPrimary.Value : RadialPalette.TextDisabled.Value;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.rectTransform.localScale = Vector3.one;
                label.rectTransform.localEulerAngles = Vector3.zero;
                label.rectTransform.sizeDelta = new Vector2(sw, 16f);
                label.rectTransform.anchoredPosition = slot + new Vector2(0f, 26f);

                var up = _triUp[i];
                up.gameObject.SetActive(true);
                up.Configure(pointsUp: true, size: 11f);
                up.color = accent;
                up.rectTransform.localEulerAngles = Vector3.zero;
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
                state.rectTransform.localEulerAngles = Vector3.zero;
                state.rectTransform.sizeDelta = new Vector2(sw, 16f);
                state.rectTransform.anchoredPosition = slot - new Vector2(0f, 2f);

                var down = _triDown[i];
                down.gameObject.SetActive(true);
                down.Configure(pointsUp: false, size: 11f);
                down.color = accent;
                down.rectTransform.localEulerAngles = Vector3.zero;
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
                else if (entry.GroupStyle)
                    // Sorting-class GROUP wedge: its own fill (defaults to the normal fill),
                    // normal hover fill on hover. The blue edge (below) is what marks it.
                    col = hovered ? RadialPalette.WedgeHover.Value : RadialPalette.GroupWedgeFill.Value;
                else if (entry.FillOverride.HasValue)
                    col = hovered ? RadialPalette.WedgeStowHover.Value : RadialPalette.WedgeStow.Value;
                else if (hovered)
                    col = RadialPalette.WedgeHover.Value;
                else
                    col = RadialPalette.WedgeBg.Value;

                if (dimmed) col.a *= 0.48f;
                return col;
            }

            /// <summary>The always-on outline. Hover shifts it to the hover border colour.
            /// Sorting-class GROUP wedges keep their distinct edge colour in every state, so a
            /// category wedge reads as one at a glance (hover feedback comes from the bulge/shine).</summary>
            private static Color ResolveUguiBorder(RadialEntry entry, bool hovered, bool dimmed)
            {
                Color border = !entry.Enabled
                    ? RadialPalette.WedgeDisabled.Value
                    : entry.GroupStyle ? RadialPalette.GroupWedgeBorder.Value
                    : entry.DeviceSlotStyle && !hovered ? RadialPalette.DeviceSlotBorderColor.Value
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
                    stmp.fontSizeMin = 8.5f;
                    stmp.fontSizeMax = 13f;
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

                    var swgo = new GameObject("Swipe" + idx, typeof(RectTransform));
                    swgo.transform.SetParent(_root, false);
                    var triW = swgo.AddComponent<TriangleGraphic>();
                    triW.raycastTarget = false;
                    triW.rectTransform.sizeDelta = new Vector2(11f, 9f);
                    _swipe.Add(triW);

                    var hkgo = new GameObject("Hotkey" + idx, typeof(RectTransform));
                    hkgo.transform.SetParent(_root, false);
                    var hktmp = hkgo.AddComponent<TextMeshProUGUI>();
                    hktmp.font = Font();
                    hktmp.alignment = TextAlignmentOptions.Center;
                    hktmp.enableAutoSizing = false;
                    hktmp.fontSize = 15f;
                    hktmp.fontStyle = FontStyles.Bold;
                    hktmp.enableWordWrapping = false;
                    hktmp.overflowMode = TextOverflowModes.Overflow;
                    hktmp.raycastTarget = false;
                    hktmp.rectTransform.sizeDelta = new Vector2(22f, 22f);
                    _hotkey.Add(hktmp);

                    var bngo = new GameObject("Binding" + idx, typeof(RectTransform));
                    bngo.transform.SetParent(_root, false);
                    var bntmp = bngo.AddComponent<TextMeshProUGUI>();
                    bntmp.font = Font();
                    bntmp.alignment = TextAlignmentOptions.Center;
                    bntmp.enableAutoSizing = true;
                    bntmp.fontSizeMin = 7f;
                    bntmp.fontSizeMax = 11.5f;
                    bntmp.enableWordWrapping = false;
                    bntmp.overflowMode = TextOverflowModes.Ellipsis;
                    bntmp.raycastTarget = false;
                    _binding.Add(bntmp);

                    // Parallel per-wedge string caches (see field declarations).
                    _labelSrc.Add(null);
                    _labelDisplay.Add(null);
                    _lastBadgeLetter.Add('\0');
                    _badgeStr.Add(null);
                    _bindingSrc.Add(null);
                    _bindingDisplay.Add(null);
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

                // #5: each of the five readout lines is sized separately (line 1 bold), applied to
                // BOTH the main hub and the child (satellite) readout. When dynamic-text is on, the
                // sizes scale with the hub (s) so a small child hub never overlaps its lines while
                // the big main hub stays full-size; off = fixed sizes everywhere.
                float textScale = (UIAConfig.RadialDynamicReadoutText == null || UIAConfig.RadialDynamicReadoutText.Value) ? s : 1f;
                float tSz = (UIAConfig.RadialHubTitleSize != null ? UIAConfig.RadialHubTitleSize.Value : 18f) * textScale;
                float vSz = (UIAConfig.RadialTextVerb != null ? UIAConfig.RadialTextVerb.Value : 15f) * textScale;
                float lSz = (UIAConfig.RadialTextLabel != null ? UIAConfig.RadialTextLabel.Value : 15f) * textScale;
                float sSz = (UIAConfig.RadialTextSub != null ? UIAConfig.RadialTextSub.Value : 12f) * textScale;
                float wSz = (UIAConfig.RadialTextWarn != null ? UIAConfig.RadialTextWarn.Value : 12f) * textScale;
                _title.fontSizeMax = tSz; _title.fontSizeMin = Mathf.Min(8f, tSz); _title.fontStyle = WedgeFontStyle();
                _verb.fontSizeMax = vSz; _verb.fontSizeMin = Mathf.Min(8f, vSz);
                _label.fontSizeMax = lSz; _label.fontSizeMin = Mathf.Min(8f, lSz);
                _sub.fontSizeMax = sSz; _sub.fontSizeMin = Mathf.Min(8f, sSz);
                _warn.fontSizeMax = wSz; _warn.fontSizeMin = Mathf.Min(8f, wSz);

                if (_hubBacking != null)
                {
                    _hubBacking.rectTransform.anchoredPosition = Vector2.zero;
                    _hubBacking.SetRadius(innerR - 6f);
                    _hubBacking.color = RadialPalette.HubFill.Value;
                    _hubBacking.BorderColor = RadialPalette.HubBorder.Value;
                    _hubBacking.BorderWidth = UIAConfig.RadialBorderWidth.Value * 0.8f;
                    _hubBacking.Refresh();
                }

                // Each line's box is clamped to the hub CIRCLE's chord at its own height (minus the
                // border + a small margin), so no line — however wide — can spill onto the orange
                // ring; text stays entirely inside the blue interior and auto-sizes to fit.
                float rIn = Mathf.Max(8f, (innerR - 6f) - (UIAConfig.RadialBorderWidth.Value * 0.8f + 7f));
                Place(_title, 38f * s, Mathf.Min(w, FitWidth(38f * s, rIn)));
                Place(_verb, 13f * s, Mathf.Min(w, FitWidth(13f * s, rIn)));
                Place(_label, -7f * s, Mathf.Min(w, FitWidth(-7f * s, rIn)));
                Place(_sub, -25f * s, Mathf.Min(w, FitWidth(-25f * s, rIn)));
                Place(_warn, -43f * s, Mathf.Min(w, FitWidth(-43f * s, rIn)));

                _title.text = satTitle ?? title ?? string.Empty;
                _title.color = RadialPalette.TextDim.Value;

                if (hovered == null)
                {
                    _verb.text = hint ?? (sticky
                        ? "LMB select | RMB back"
                        : "hover to dive | release to cancel");
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

            /// <summary>The full chord width of a circle of radius <paramref name="rIn"/> at height
            /// <paramref name="y"/>, accounting for a 20px line's half-height, so a line placed there
            /// fits entirely inside the circle. Zero when the height is already past the circle.</summary>
            private static float FitWidth(float y, float rIn)
            {
                float dy = Mathf.Abs(y) + 10f; // 10 = half the 20px line box, the constraining edge
                float inside = rIn * rIn - dy * dy;
                return inside <= 0f ? 0f : 2f * Mathf.Sqrt(inside);
            }
        }
    }
}
