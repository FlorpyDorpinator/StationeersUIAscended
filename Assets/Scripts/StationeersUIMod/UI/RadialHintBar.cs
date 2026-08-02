using System.Collections.Generic;
using System.Text;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// The compact, contextual key-hint bar that fades in just below an open radial: a single slim
    /// strip telling the player what the keys do right now (LMB select, RMB back, Alt reach, swap
    /// hand, page…). Its keys read live from <see cref="UiaKeybinds"/> so a rebind shows here too,
    /// and it swaps to grab/place hints while the Alt world-reach modifier is held. Toggle:
    /// <c>UIAConfig.RadialHintBar</c>. Own screen-space overlay canvas, hot-reload safe.
    ///
    /// Progressive fade (R10): each hint KIND carries a per-save exposure count in
    /// <see cref="HintUsageStore"/>; once a kind passes <see cref="FadeThreshold"/> uses it is
    /// dropped from the strip so the hints teach then get out of the way. Gated by
    /// <c>UIAConfig.RadialHintFade</c>; counters reset from the F10 menu
    /// (<see cref="HintUsageStore.ResetCounters"/>).
    /// </summary>
    public static class RadialHintBar
    {
        private static GameObject _root;
        private static Canvas _canvas;
        private static RectTransform _panel;
        // The mod's own rounded/glass renderer rather than a square UGUI Image, so the strip gets
        // curved corners, a proper rim and the same glass treatment as the rest of the UI.
        private static Hud.PanelGraphic _bg;
        private static TextMeshProUGUI _text;
        /// <summary>Editor aid only: a plain WHITE swatch drawn BEHIND the strip while previewing,
        /// so a translucent black panel can actually be judged (against the sky it reads as a
        /// different colour entirely).</summary>
        private static Image _preview;
        private static float _styleHash = float.NaN;

        /// <summary>Fallback height when config is unbound; the live value is <c>HintBarHeight</c>.</summary>
        private const float BarH = 30f;
        private const string PreviewText = "LMB  select   -   RMB  back   -   Alt  reach";
        private static string _last;

        // ---- progressive fade (R10) ----
        /// <summary>Exposures of a hint kind before it stops being drawn.</summary>
        private const int FadeThreshold = 25;
        private const string Dot = "      -      ";

        // hint-kind storage keys (must stay stable — they key the persisted counters)
        private const string K_Select = "select";
        private const string K_Back = "back";
        private const string K_Reach = "reach";
        private const string K_Swap = "swaphand";
        private const string K_Page = "page";
        private const string K_Grab = "worldgrab";
        private const string K_Place = "worldplace";
        private static readonly string[] AllKinds = { K_Select, K_Back, K_Reach, K_Swap, K_Page, K_Grab, K_Place };

        private static bool _sessionActive;
        private static readonly HashSet<string> _faded = new HashSet<string>();       // kinds past threshold this session
        private static readonly HashSet<string> _sessionShown = new HashSet<string>(); // kinds actually drawn this session
        private static int _fadeVer;                                                  // bumps when _faded is recomputed

        // composed-string cache (zero per-frame alloc while nothing on the strip changes)
        private static readonly StringBuilder _sb = new StringBuilder(160);
        private static string _composed;
        private static bool _cAlt;
        private static KeyCode _cSwapKey = KeyCode.None;
        private static KeyCode _cPageKey = KeyCode.None;
        private static int _cFadeVer = -1;

        /// <summary>Pumped each frame from the mod's Update with whether a radial is currently open.</summary>
        public static void Tick(bool radialOpen)
        {
            bool fadeOn = UIAConfig.RadialHintFade != null && UIAConfig.RadialHintFade.Value;
            UpdateSession(radialOpen, fadeOn);

            // Preview pins the strip on screen with sample text even with no wheel open, so its look
            // can be authored. It deliberately ignores the show/fade gates below.
            //
            // ON BY DEFAULT inside the RADIAL EDITOR: that editor blacks the screen out, so without
            // the strip pinned over its white swatch there is nothing to judge a translucent black
            // panel against. Outside the editor it is opt-in via the config toggle.
            bool preview = Windows.RadialEditorMode.Active
                || (UIAConfig.HintBarPreview != null && UIAConfig.HintBarPreview.Value);

            bool want = preview || (radialOpen
                && UIAConfig.RadialHintBar != null && UIAConfig.RadialHintBar.Value);
            if (!want)
            {
                if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }

            string s = preview ? PreviewText : ComposeHint(fadeOn);
            if (string.IsNullOrEmpty(s))
            {
                // every hint for this context has faded away — nothing left to teach.
                if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }

            EnsureBuilt();
            if (_canvas == null) return;
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

            // Push the authored look every frame. Everything here is dirty-guarded (PanelGraphic's
            // setters and SetShape all early-out on an unchanged value), so an untouched strip costs
            // a handful of float compares — and a slider dragged in the F10 editor updates live.
            ApplyStyle();

            // Re-measure on a text change OR a style change: font size and padding are what actually
            // set the strip's length, so a slider drag has to re-run the layout, not just the text.
            float hash = StyleHash();
            if (s != _last || !Mathf.Approximately(hash, _styleHash))
            {
                _last = s;
                _styleHash = hash;
                _text.text = s;
                float h = HeightPx();
                float w = Mathf.Clamp(_text.preferredWidth + PaddingPx(), 96f, 1400f);
                _panel.sizeDelta = new Vector2(w, h);
                _bg.SetShape(w, h, Mathf.Min(CornerPx(), h * 0.5f));
                if (_preview != null) _preview.rectTransform.sizeDelta = new Vector2(w + 40f, h + 24f);
            }

            // In preview the wheel is not open, so park the strip somewhere always visible and put a
            // WHITE swatch behind it — the only honest way to judge a translucent black panel.
            bool showPreviewBox = preview;
            if (_preview != null && _preview.enabled != showPreviewBox) _preview.enabled = showPreviewBox;
            if (preview)
            {
                _panel.anchoredPosition = new Vector2(0f, -140f);
            }
            else
            {
                float outerR = UIAConfig.RadialOuterRadius != null ? UIAConfig.RadialOuterRadius.Value : 240f;
                float drop = UIAConfig.HintBarDrop != null ? UIAConfig.HintBarDrop.Value : 34f;
                _panel.anchoredPosition = new Vector2(0f, -(outerR + drop));
            }
            if (_preview != null) _preview.rectTransform.anchoredPosition = _panel.anchoredPosition;
        }

        // ---- authored look (F10 → Radial → Hint bar; colours live in RadialPalette) ----

        private static float CornerPx() => UIAConfig.HintBarCorner != null ? UIAConfig.HintBarCorner.Value : 12f;
        private static float HeightPx() => UIAConfig.HintBarHeight != null ? UIAConfig.HintBarHeight.Value : BarH;
        private static float PaddingPx() => UIAConfig.HintBarPadding != null ? UIAConfig.HintBarPadding.Value : 24f;

        /// <summary>Cheap change-detector over the values that affect LAYOUT (not just paint), so the
        /// strip re-measures when one of them moves.</summary>
        private static float StyleHash()
        {
            float f = UIAConfig.HintBarFontSize != null ? UIAConfig.HintBarFontSize.Value : 11.2f;
            bool b = UIAConfig.HintBarBold == null || UIAConfig.HintBarBold.Value;
            return f * 31f + HeightPx() * 57f + PaddingPx() * 91f + CornerPx() * 7f + (b ? 3f : 0f);
        }

        private static void ApplyStyle()
        {
            if (_bg == null || _text == null) return;

            _bg.color = Overlay.RadialPalette.HintBarFill.Value;
            _bg.BorderColor = Overlay.RadialPalette.HintBarBorder.Value;
            _bg.BorderWidth = UIAConfig.HintBarBorderWidth != null ? UIAConfig.HintBarBorderWidth.Value : 0f;
            _bg.FeatherOverride = UIAConfig.HintBarFeather != null ? UIAConfig.HintBarFeather.Value : 1.25f;
            _bg.Sheen = UIAConfig.HintBarSheen != null ? UIAConfig.HintBarSheen.Value : 0.35f;
            _bg.Spec = UIAConfig.HintBarSpec != null ? UIAConfig.HintBarSpec.Value : 0f;
            _bg.Glow = UIAConfig.HintBarGlow != null ? UIAConfig.HintBarGlow.Value : 0f;
            _bg.GlowWidth = UIAConfig.HintBarGlowWidth != null ? UIAConfig.HintBarGlowWidth.Value : 24f;

            // FROST — the blurred-screen glass. Exactly the radial's own recipe (UnityRadialView
            // .UpdateRadialFx/ApplyWedgeFx): frost only engages when the user asked for it AND the
            // HUD's Tier C master is on AND the backdrop capture is running AND the shader bundle
            // resolved. Anything missing degrades to plain translucency rather than a broken panel.
            bool frostWanted = UIAConfig.HintBarFrost != null && UIAConfig.HintBarFrost.Value
                && Hud.HudConfig.FxTierC != null && Hud.HudConfig.FxTierC.Value;
            bool frostActive = frostWanted && Hud.HudBackdrop.Active
                && Core.HudShaderStore.TierBAvailable && Hud.HudFxMaterials.Available;
            _bg.FxStrength = frostActive
                ? (UIAConfig.HintBarFrostStrength != null ? UIAConfig.HintBarFrostStrength.Value : 0.85f)
                : 0f;
            if (frostActive)
            {
                if (!Hud.HudFxMaterials.Assign(_bg, "glass")) Hud.HudFxMaterials.Unassign(_bg);
            }
            else Hud.HudFxMaterials.Unassign(_bg);

            _text.color = Overlay.RadialPalette.HintBarText.Value;
            float fs = UIAConfig.HintBarFontSize != null ? UIAConfig.HintBarFontSize.Value : 11.2f;
            if (!Mathf.Approximately(_text.fontSize, fs)) _text.fontSize = fs;
            var style = (UIAConfig.HintBarBold == null || UIAConfig.HintBarBold.Value)
                ? FontStyles.Bold : FontStyles.Normal;
            if (_text.fontStyle != style) _text.fontStyle = style;

            // Keep the text inset in step with the padding so the two never fight over the ends.
            float inset = Mathf.Max(2f, PaddingPx() * 0.5f);
            var trt = _text.rectTransform;
            if (!Mathf.Approximately(trt.offsetMin.x, inset))
            {
                trt.offsetMin = new Vector2(inset, 0f);
                trt.offsetMax = new Vector2(-inset, 0f);
            }
        }

        // ---- session bookkeeping: one exposure per drawn kind per open→close of the ring ----

        private static void UpdateSession(bool radialOpen, bool fadeOn)
        {
            if (radialOpen && !_sessionActive)
            {
                _sessionActive = true;
                _sessionShown.Clear();
                RecomputeFaded(fadeOn);
            }
            else if (!radialOpen && _sessionActive)
            {
                _sessionActive = false;
                if (fadeOn && _sessionShown.Count > 0)
                {
                    foreach (var k in _sessionShown) HintUsageStore.Add(k);
                    HintUsageStore.Flush();
                }
                _sessionShown.Clear();
            }
        }

        private static void RecomputeFaded(bool fadeOn)
        {
            _faded.Clear();
            if (fadeOn)
            {
                for (int i = 0; i < AllKinds.Length; i++)
                    if (HintUsageStore.Get(AllKinds[i]) >= FadeThreshold) _faded.Add(AllKinds[i]);
            }
            _fadeVer++;        // invalidate the composed-string cache
            _composed = null;
        }

        // ---- strip composition (rebuilds only when an input changes → no steady-state alloc) ----

        private static string ComposeHint(bool fadeOn)
        {
            bool alt = AltHeld();
            // Compare the raw KeyCodes (no boxing) before deciding to rebuild; only turn them into
            // glyph strings on an actual change — Glyph(KeyCode) falls through to enum.ToString() for
            // E/Q, which would box the enum every frame otherwise (defeating this cache's alloc goal).
            KeyCode swapK = UiaKeybinds.Key("UIA_HandSwap");
            KeyCode pageK = UiaKeybinds.Key("UIA_Page");

            if (_composed != null && alt == _cAlt && _cFadeVer == _fadeVer
                && swapK == _cSwapKey && pageK == _cPageKey)
                return _composed;

            string swap = UiaKeybinds.Glyph(swapK);
            string page = UiaKeybinds.Glyph(pageK);
            _cAlt = alt; _cSwapKey = swapK; _cPageKey = pageK; _cFadeVer = _fadeVer;

            _sb.Length = 0;
            if (alt)
            {
                Append(K_Grab, "LMB  grab from world", fadeOn);
                Append(K_Back, "RMB  back", fadeOn);
                Append(K_Place, "release over a wedge to place", fadeOn);
            }
            else
            {
                Append(K_Select, "LMB  select", fadeOn);
                Append(K_Back, "RMB  back", fadeOn);
                Append(K_Reach, "Alt  reach", fadeOn);
                Append(K_Swap, swap + "  swap hand", fadeOn);
                Append(K_Page, page + "  page", fadeOn);
            }
            _composed = _sb.ToString();
            return _composed;
        }

        private static void Append(string kind, string text, bool fadeOn)
        {
            if (fadeOn && _faded.Contains(kind)) return;   // this kind has been learned — drop it
            _sessionShown.Add(kind);                        // fold into the session's exposure union
            if (_sb.Length > 0) _sb.Append(Dot);
            _sb.Append(text);
        }

        private static bool AltHeld()
        {
            try { return KeyManager.GetButton(KeyMap.MouseControl); }
            catch { return false; }
        }

        private static void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("UIAscended_RadialHintBar");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // BEHIND all radial content. The strip sits spatially BELOW the ring, but child radials,
            // satellites and other generated wedges expand into its area — at 5010 (above the radial's
            // 5000) the strip drew ON TOP of them. Park it just UNDER the radial canvas (5000) so the
            // ring and everything it spawns always render over the strip, while staying above the radial
            // editor's black backdrop (4900, so the F10 hint-bar preview still shows) and the HUD (3800,
            // so the strip stays visible where nothing overlaps it).
            _canvas.sortingOrder = 4999;

            // Created FIRST so it sits behind the strip in sibling order.
            var previewGo = new GameObject("previewSwatch", typeof(RectTransform));
            previewGo.transform.SetParent(_root.transform, false);
            var prt = (RectTransform)previewGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(640f, 54f);
            _preview = previewGo.AddComponent<Image>();
            _preview.color = Color.white;
            _preview.raycastTarget = false;
            _preview.enabled = false;

            var panelGo = new GameObject("bar", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(600f, BarH);
            // BLACK, curved, glassy (FlorpyDorp). PanelGraphic gives the rounded corners and the
            // rim/sheen for free; the old square Image + 1px Outline could do neither.
            // Look is AUTHORED, not hard-coded: ApplyStyle() pushes the F10 "Hint bar" settings and
            // the RadialPalette colours every frame. Only the raycast flag is structural.
            _bg = panelGo.AddComponent<Hud.PanelGraphic>();
            _bg.raycastTarget = false;

            var txtGo = new GameObject("text", typeof(RectTransform));
            txtGo.transform.SetParent(_panel, false);
            _text = txtGo.AddComponent<TextMeshProUGUI>();
            _text.font = UiaTheme.Font();
            // Size/colour/weight are pushed by ApplyStyle() from the F10 settings. NOTE the strip
            // auto-sizes to its text, so FONT SIZE is also the knob that makes the bar longer or
            // shorter — there is no separate "length" to set.
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);
        }

        public static void Shutdown()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _panel = null;
            _bg = null;
            _text = null;
            _preview = null;
            _last = null;
            _styleHash = float.NaN;   // force a re-measure on the next build (hot-reload safe)

            // progressive-fade statics (hot-reload safe: a double-F6 leaves nothing stranded)
            _sessionActive = false;
            _faded.Clear();
            _sessionShown.Clear();
            _fadeVer = 0;
            _composed = null;
            _cAlt = false;
            _cSwapKey = KeyCode.None;
            _cPageKey = KeyCode.None;
            _cFadeVer = -1;
            HintUsageStore.Reset();
        }
    }
}
