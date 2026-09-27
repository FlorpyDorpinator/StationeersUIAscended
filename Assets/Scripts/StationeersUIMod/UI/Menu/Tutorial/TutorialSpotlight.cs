using System;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// The tutorial SPOT (plan A.8): a pulsing accent outline drawn 8 px OUTSIDE a real HUD element,
    /// so a lesson can point at "your hands" or "the compass" on the player's own visor.
    ///
    /// <para><b>Where the rect comes from.</b> <see cref="HudSystem.TryGetElementScreenRect"/>: the
    /// first live element of that type (+ Readout "src"), forward-warped exactly like the F9
    /// handles - never a second warp. An element that is not in the player's profile, hidden at this
    /// tier, faded out or off screen returns false and the spot is simply skipped; the copy never
    /// depends on it (copy names elements, never screen positions). Live spots are re-tracked 10x a
    /// second, so a flicker, a tier change or an F9 drag moves / hides / restores the ring.</para>
    ///
    /// <para><b>Layering.</b> Own ScreenSpaceOverlay canvas at sort 5140: over the HUD (3800), the
    /// hint ring (4999), the wheel (5000), search (5005), parked items (5010), the Universal
    /// Inventory (5020), pinned windows (5030-5090) and the tab drag ghost (5100); under the lesson
    /// strip (5150), F10 (5200), the drag ghosts (5250), the coach (5600) and vanilla's F2
    /// helper-hints panel lifted to 5800 (<c>Core/HelperHintsLift</c>). NO GraphicRaycaster
    /// and a non-blocking CanvasGroup: it can never eat a click. It stands down (alpha 0) while a
    /// vanilla menu wants the front, while the F9 designer is up, while F10 is up (unless a coach
    /// card is showing it through a scrim hole), and whenever the world cannot draw. Because it
    /// sits UNDER F10, F10 anchors are highlighted by the coach's callout ring, not here.</para>
    ///
    /// <para><b>Cost.</b> The pulse (alpha 0.35-1.0 at 1.2 Hz) rides the root CanvasGroup - no mesh
    /// rebuild. Tracking is throttled; everything is pooled (up to <see cref="MaxSpots"/> rings, a
    /// struct array of requests) - zero per-frame allocation. Looks: the menu skin's accent
    /// (<see cref="UiaTheme.Accent"/>, which follows the active UI Theme), so there is nothing new
    /// to theme, nothing per-tier and nothing to migrate (CLAUDE.md rule 8: shared chrome).</para>
    ///
    /// <para><b>Hot reload.</b> <see cref="Shutdown"/> destroys the canvas and resets every static;
    /// no event hooks exist.</para>
    /// </summary>
    internal static class TutorialSpotlight
    {
        private const int SortOrder = 5140;
        internal const int MaxSpots = 6;
        private const float Margin = 8f;          // ring sits this far OUTSIDE the element (ref px)
        private const float RingWidth = 2.4f;
        private const float RingCorner = 12f;
        private const float PulseHz = 1.2f;
        private const float MinAlpha = 0.35f, MaxAlpha = 1f;
        private const float TrackInterval = 0.1f;
        private const float FadeRate = 8f;         // stand-down fade, per second

        private struct Spot
        {
            public bool Live;
            public bool IsRect;                    // ShowRect: a fixed screen rect, never tracked
            public HudElementType Type;
            public string Src;                     // stored reference, no copy
            public Rect Screen;                    // last known screen rect (px, bottom-left origin)
            public bool OnScreen;
        }

        private static readonly Spot[] _spots = new Spot[MaxSpots];
        private static readonly PanelGraphic[] _rings = new PanelGraphic[MaxSpots];

        private static GameObject _root;
        private static Canvas _canvas;
        private static RectTransform _rootRt;
        private static CanvasGroup _group;
        private static int _count;
        private static float _nextTrack;
        private static float _t0;
        private static float _standDown = 1f;      // 1 = shown, 0 = stood down (eased)
        private static float _placedScale = -1f;
        private static int _placedW, _placedH;
        private static float _nextBuildTry;

        /// <summary>True while at least one spot is requested (shown or temporarily off screen).</summary>
        internal static bool Active { get { return _count > 0; } }

        // ================= API =================

        /// <summary>Outline the first live HUD element of <paramref name="type"/> (for a Readout,
        /// the one whose "src" is <paramref name="srcParam"/>, e.g. "ExternalPressure"). ADDITIVE:
        /// several calls outline several elements (a step may point at two readouts); the same
        /// type+src twice is one ring; <see cref="Clear"/> removes them all. Returns false - and
        /// adds nothing - when the element is not on screen right now (not in the profile, hidden
        /// at this tier, faded, off screen, the world-canvas curvature, or every ring in use).
        /// The copy still stands in that case.</summary>
        internal static bool Show(HudElementType type, string srcParam = null)
        {
            Rect r;
            bool on;
            try { on = HudSystem.TryGetElementScreenRect(type, srcParam, out r); }
            catch { on = false; r = default(Rect); }
            if (!on) return false;

            // Already shown: refresh its rect, report success.
            for (int i = 0; i < MaxSpots; i++)
            {
                if (!_spots[i].Live || _spots[i].IsRect || _spots[i].Type != type) continue;
                if (!SameSrc(_spots[i].Src, srcParam)) continue;
                _spots[i].Screen = r;
                _spots[i].OnScreen = true;
                Place(i);
                return true;
            }

            int slot = FreeSlot();
            if (slot < 0) return false;
            if (!EnsureBuilt()) return false;
            _spots[slot].Live = true;
            _spots[slot].IsRect = false;
            _spots[slot].Type = type;
            _spots[slot].Src = srcParam;
            _spots[slot].Screen = r;
            _spots[slot].OnScreen = true;
            _count++;
            Arm();
            Place(slot);
            return true;
        }

        /// <summary>Outline an arbitrary SCREEN rect (px, bottom-left origin, y up), 8 px outside it.
        /// Additive like <see cref="Show"/>; never tracked (the rect is fixed).</summary>
        internal static void ShowRect(Rect screenRect)
        {
            if (screenRect.width < 1f || screenRect.height < 1f) return;
            for (int i = 0; i < MaxSpots; i++)
            {
                if (!_spots[i].Live || !_spots[i].IsRect) continue;
                if (_spots[i].Screen == screenRect) return;   // same rect twice = one ring
            }
            int slot = FreeSlot();
            if (slot < 0) return;
            if (!EnsureBuilt()) return;
            _spots[slot].Live = true;
            _spots[slot].IsRect = true;
            _spots[slot].Type = default(HudElementType);
            _spots[slot].Src = null;
            _spots[slot].Screen = screenRect;
            _spots[slot].OnScreen = true;
            _count++;
            Arm();
            Place(slot);
        }

        /// <summary>Remove every outline. Cheap; safe to call every step.</summary>
        internal static void Clear()
        {
            for (int i = 0; i < MaxSpots; i++)
            {
                _spots[i].Live = false;
                _spots[i].Src = null;
                _spots[i].OnScreen = false;
                if (_rings[i] != null && _rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(false);
            }
            _count = 0;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        /// <summary>Per-frame pump (StationeersUIMod.Update, after the Control Center). Zero
        /// allocation: tracking at 10 Hz, the pulse on the CanvasGroup.</summary>
        internal static void Tick()
        {
            if (_count == 0 || _root == null) return;

            bool worldOk = false;
            try { worldOk = Guards.CanDraw(); } catch { worldOk = false; }
            // Behind F10 the ring would only peek out dimmed - unless a coach card is up (a Watch
            // card opened from the Guide tab shows its SPOT through the scrim hole).
            bool cardUp = TutorialCoach.CardOpen && !TutorialCoach.CalloutOpen;
            bool standDown = !worldOk || VanillaFront() || DesignerUp() || (MenuUp() && !cardUp);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!worldOk) _standDown = 0f;
            else _standDown = Mathf.MoveTowards(_standDown, standDown ? 0f : 1f, FadeRate * dt);

            if (_standDown <= 0.001f)
            {
                if (_group != null && _group.alpha != 0f) _group.alpha = 0f;
                return;
            }

            float now = Time.unscaledTime;
            // A resolution / canvas-scale change (or the scaler's first frame) re-places everything.
            bool rescale = _canvas != null
                && (!Mathf.Approximately(_canvas.scaleFactor, _placedScale)
                    || Screen.width != _placedW || Screen.height != _placedH);
            if (now >= _nextTrack || rescale)
            {
                _nextTrack = now + TrackInterval;
                Track(rescale);
            }

            float s = 0.5f + 0.5f * Mathf.Sin((now - _t0) * PulseHz * 2f * Mathf.PI);
            float a = Mathf.Lerp(MinAlpha, MaxAlpha, s) * _standDown;
            if (_group != null && Mathf.Abs(_group.alpha - a) > 0.002f) _group.alpha = a;
        }

        /// <summary>Hot-reload / plugin teardown: destroy the canvas, reset every static.</summary>
        internal static void Shutdown()
        {
            for (int i = 0; i < MaxSpots; i++)
            {
                _spots[i] = default(Spot);
                _rings[i] = null;
            }
            if (_root != null) { try { UnityEngine.Object.Destroy(_root); } catch { } }
            _root = null;
            _canvas = null;
            _rootRt = null;
            _group = null;
            _count = 0;
            _nextTrack = 0f;
            _t0 = 0f;
            _standDown = 1f;
            _placedScale = -1f;
            _placedW = _placedH = 0;
            _nextBuildTry = 0f;
        }

        // ================= internals =================

        private static bool SameSrc(string a, string b)
        {
            bool ea = string.IsNullOrEmpty(a), eb = string.IsNullOrEmpty(b);
            if (ea || eb) return ea == eb;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int FreeSlot()
        {
            for (int i = 0; i < MaxSpots; i++) if (!_spots[i].Live) return i;
            return -1;
        }

        private static bool VanillaFront()
        {
            try { return Guards.VanillaMenuWantsFront(); } catch { return false; }
        }

        private static bool DesignerUp()
        {
            try { return global::StationeersUIMod.Windows.HudEditorMode.Active; } catch { return false; }
        }

        private static bool MenuUp()
        {
            try { return UiaControlCenter.IsOpen; } catch { return false; }
        }

        /// <summary>Show the canvas and restart the pulse phase on the first spot of a batch.</summary>
        private static void Arm()
        {
            if (_root == null) return;
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                _t0 = Time.unscaledTime;
                _standDown = 1f;
                _nextTrack = Time.unscaledTime + TrackInterval;
            }
        }

        /// <summary>Re-query every tracked spot; hide rings whose element left the screen and bring
        /// them back when it returns. Colour is re-pushed too (dirty-guarded) so a UI Theme change
        /// recolours live rings without a rebuild.</summary>
        private static void Track(bool replaceAll)
        {
            Color accent = UiaTheme.Accent;
            for (int i = 0; i < MaxSpots; i++)
            {
                if (!_spots[i].Live) continue;
                if (!_spots[i].IsRect)
                {
                    Rect r;
                    bool on;
                    try { on = HudSystem.TryGetElementScreenRect(_spots[i].Type, _spots[i].Src, out r); }
                    catch { on = false; r = default(Rect); }
                    bool moved = on && r != _spots[i].Screen;
                    bool flipped = on != _spots[i].OnScreen;
                    _spots[i].OnScreen = on;
                    if (on) _spots[i].Screen = r;
                    if (moved || flipped || replaceAll) Place(i);
                }
                else if (replaceAll) Place(i);

                var g = _rings[i];
                if (g != null) g.BorderColor = accent;
            }
        }

        /// <summary>Position ring <paramref name="i"/> over its spot's screen rect (8 px outside),
        /// or hide it when the spot is off screen.</summary>
        private static void Place(int i)
        {
            if (_rootRt == null) return;
            var g = EnsureRing(i);
            if (g == null) return;
            if (!_spots[i].Live || !_spots[i].OnScreen)
            {
                if (g.gameObject.activeSelf) g.gameObject.SetActive(false);
                return;
            }
            Vector2 a, b;
            Rect s = _spots[i].Screen;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, s.min, null, out a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, s.max, null, out b);
            float xMin = Mathf.Min(a.x, b.x) - Margin, xMax = Mathf.Max(a.x, b.x) + Margin;
            float yMin = Mathf.Min(a.y, b.y) - Margin, yMax = Mathf.Max(a.y, b.y) + Margin;
            float w = Mathf.Max(4f, xMax - xMin), h = Mathf.Max(4f, yMax - yMin);
            var rt = (RectTransform)g.transform;
            rt.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            g.SetShape(w, h, Mathf.Min(RingCorner, Mathf.Min(w, h) * 0.5f));
            if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);

            if (_canvas != null) _placedScale = _canvas.scaleFactor;
            _placedW = Screen.width;
            _placedH = Screen.height;
        }

        private static bool EnsureBuilt()
        {
            if (_root != null) return true;
            if (Time.unscaledTime < _nextBuildTry) return false;   // a failed build retries every 5 s at most
            try
            {
                _root = new GameObject("UIAscended_TutorialSpotlight");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                _canvas = _root.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = SortOrder;   // see the class remarks for the full z-map
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                // Deliberately NO GraphicRaycaster: this layer can never take a click.
                _group = _root.AddComponent<CanvasGroup>();
                _group.interactable = false;
                _group.blocksRaycasts = false;
                _group.alpha = 0f;
                _rootRt = (RectTransform)_root.transform;
                _root.SetActive(false);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn("TutorialSpotlight build failed (lessons still work, minus outlines): " + e.Message);
                try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
                _root = null; _canvas = null; _rootRt = null; _group = null;
                _nextBuildTry = Time.unscaledTime + 5f;
                return false;
            }
        }

        private static PanelGraphic EnsureRing(int i)
        {
            if (_rings[i] != null) return _rings[i];
            if (_rootRt == null) return null;
            var go = new GameObject("spot-" + i, typeof(RectTransform));
            go.transform.SetParent(_rootRt, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);   // PanelGraphic draws centred on its pivot
            var g = go.AddComponent<PanelGraphic>();
            g.raycastTarget = false;
            g.color = new Color(0f, 0f, 0f, 0f);  // border-only ring
            g.BorderColor = UiaTheme.Accent;
            g.BorderWidth = RingWidth;
            g.Glow = 0.6f;                       // a soft halo so the ring reads on a busy scene
            g.GlowWidth = 10f;
            g.GlowDiffuse = 0.5f;
            go.SetActive(false);
            _rings[i] = g;
            return g;
        }
    }
}
