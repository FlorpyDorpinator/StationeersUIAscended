using StationeersUIMod.Core;
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
    /// </summary>
    public static class RadialHintBar
    {
        private static GameObject _root;
        private static Canvas _canvas;
        private static RectTransform _panel;
        private static Image _bg;
        private static TextMeshProUGUI _text;
        private static string _last;

        /// <summary>Pumped each frame from the mod's Update with whether a radial is currently open.</summary>
        public static void Tick(bool radialOpen)
        {
            bool want = radialOpen
                && UIAConfig.RadialHintBar != null && UIAConfig.RadialHintBar.Value;
            if (!want)
            {
                if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }

            EnsureBuilt();
            if (_canvas == null) return;
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

            string s = BuildHint();
            if (s != _last)
            {
                _last = s;
                _text.text = s;
                float w = Mathf.Clamp(_text.preferredWidth + 30f, 120f, 1400f);
                _panel.sizeDelta = new Vector2(w, 30f);
            }

            float outerR = UIAConfig.RadialOuterRadius != null ? UIAConfig.RadialOuterRadius.Value : 240f;
            _panel.anchoredPosition = new Vector2(0f, -(outerR + 34f));
        }

        private static string BuildHint()
        {
            string swap = UiaKeybinds.Glyph("UIA_HandSwap");
            string page = UiaKeybinds.Glyph("UIA_Page");
            const string dot = "      -      ";
            if (AltHeld())
                return "LMB  grab from world" + dot + "RMB  back" + dot + "release over a wedge to place";
            return "LMB  select" + dot + "RMB  back" + dot + "Alt  reach" + dot + swap + "  swap hand" + dot + page + "  page";
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
            _canvas.sortingOrder = 5010; // just above the radial ring, but it sits below it spatially

            var panelGo = new GameObject("bar", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(600f, 30f);
            _bg = panelGo.AddComponent<Image>();
            _bg.color = new Color(0.03f, 0.05f, 0.07f, 0.82f);
            _bg.raycastTarget = false;
            var outline = panelGo.AddComponent<Outline>();
            outline.effectColor = UiaTheme.AccentDim;
            outline.effectDistance = new Vector2(1f, 1f);
            outline.useGraphicAlpha = false;

            var txtGo = new GameObject("text", typeof(RectTransform));
            txtGo.transform.SetParent(_panel, false);
            _text = txtGo.AddComponent<TextMeshProUGUI>();
            _text.font = UiaTheme.Font();
            _text.fontSize = 14f;
            _text.color = new Color(0.82f, 0.90f, 0.97f, 1f);
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 0f); trt.offsetMax = new Vector2(-12f, 0f);
        }

        public static void Shutdown()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _panel = null;
            _bg = null;
            _text = null;
            _last = null;
        }
    }
}
