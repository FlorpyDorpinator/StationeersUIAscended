using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// GitHub #12 (text too small at 4K): the ONE place the radial wheels and the Universal
    /// Inventory / pinned storage windows get their size factors. Both live on ScreenSpaceOverlay
    /// canvases with no CanvasScaler, so before this every wedge, cell and font was a fixed pixel
    /// count: half the apparent size on a 3840x2160 screen.
    ///
    /// <para><b>Resolution</b> follows the visor HUD's own per-machine toggle
    /// (<see cref="HudConfig.HudScaleWithRes"/>, "ScaleWithResolution", default on) and its
    /// width/height blend (<see cref="HudConfig.HudScaleMatch"/>) — the same log-interpolation
    /// Unity's CanvasScaler uses. Two deliberate differences from
    /// <see cref="HudConfig.EffectiveResolutionScale"/>:
    /// (1) the reference is FIXED at 1920x1080 — the resolution the wheel and the inventory windows
    /// were designed at — never the active HUD theme's RefW/RefH. A theme stamped at 4K (F9 "stamp
    /// THIS UI Theme", or any blank profile created on a 4K screen) describes the HUD layout, not
    /// the wheel, and would otherwise turn this fix off for exactly the players who need it;
    /// (2) it never goes BELOW 1: on screens smaller than 1080p the wheel and windows keep their
    /// designed pixel sizes (they were legible there before; shrinking 8 px text to 5 px is not).
    /// Off = 1 everywhere, i.e. exactly the pre-#12 pixel sizes.</para>
    ///
    /// <para><b>Text</b> is the player's "Wheel &amp; inventory text size" preference
    /// (<see cref="UIAConfig.WheelInvTextScale"/>, 0.75..2.5) — an accessibility knob that does NOT
    /// travel with a UI theme (the HelmetOpenWarning precedent).</para>
    ///
    /// <para>How each surface consumes it:
    /// the radial family (wheel, hint strip, search panel) sets its CANVAS scaleFactor to
    /// <see cref="Resolution"/> and works in 1080p reference units, so geometry, fonts and every
    /// fixed pixel constant scale together; it multiplies only its font sizes (and the text boxes
    /// that hold them) by <see cref="Text"/>. The Grid windows cannot scale their canvas (their
    /// drag / resize / clamp math is raw screen pixels), so they scale at the read points instead:
    /// <see cref="Geo"/> for pure geometry (cells, gaps, chrome buttons), <see cref="Font"/> for
    /// font sizes, and <see cref="Box"/> for the text-bearing boxes (tabs, title bars, buttons,
    /// popup rows) which grow with the text but never shrink below their resolution-scaled size.</para>
    ///
    /// <para>Values are cached per frame (several hundred reads a relayout). Hot reload: the cache
    /// is plain values, reset by <see cref="Reset"/> from <c>UnityRadialView.Shutdown</c>.</para>
    /// </summary>
    internal static class WheelGridScale
    {
        public const float TextMin = 0.75f;
        public const float TextMax = 2.5f;
        private const float RefW = 1920f, RefH = 1080f;
        private const float ResMax = 4f;   // 7680x4320 at match 0.5; a bound against a bogus Screen size

        private static int _frame = -1;
        private static float _res = 1f;
        private static float _text = 1f;

        /// <summary>The resolution factor (1 at 1920x1080 and below, 2 at 3840x2160; 1 when the
        /// player turned ScaleWithResolution off).</summary>
        public static float Resolution() { Refresh(); return _res; }

        /// <summary>The player's text multiplier (0.75..2.5, default 1).</summary>
        public static float Text() { Refresh(); return _text; }

        /// <summary>Grid: pure geometry (cells, gaps, paddings, chrome buttons).</summary>
        public static float Geo { get { Refresh(); return _res; } }

        /// <summary>Grid: font sizes (resolution x the player's text size).</summary>
        public static float Font { get { Refresh(); return _res * _text; } }

        /// <summary>Grid: boxes that HOLD text (tab, title bar, Sort/chip/CAPTURE, popup rows): they
        /// grow with a larger text size so the label keeps its designed fit, but a smaller text size
        /// never shrinks them below the resolution-scaled size.</summary>
        public static float Box { get { Refresh(); return _res * Mathf.Max(1f, _text); } }

        /// <summary>A quantised fingerprint of both factors — folded into the change polls
        /// (TheGridPanel's size hash) so a resolution change or a slider move relayouts live.</summary>
        public static int Hash()
        {
            Refresh();
            unchecked { return Mathf.RoundToInt(_res * 1000f) * 7919 + Mathf.RoundToInt(_text * 1000f); }
        }

        internal static void Reset()
        {
            _frame = -1;
            _res = 1f;
            _text = 1f;
        }

        private static void Refresh()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            _res = ComputeResolution();
            float t = 1f;
            try { if (UIAConfig.WheelInvTextScale != null) t = UIAConfig.WheelInvTextScale.Value; } catch { }
            _text = float.IsNaN(t) ? 1f : Mathf.Clamp(t, TextMin, TextMax);
        }

        private static float ComputeResolution()
        {
            try
            {
                if (HudConfig.HudScaleWithRes != null && !HudConfig.HudScaleWithRes.Value) return 1f;
                float sw = Screen.width, sh = Screen.height;
                if (sw <= 0f || sh <= 0f) return 1f;
                float m = HudConfig.HudScaleMatch != null ? Mathf.Clamp01(HudConfig.HudScaleMatch.Value) : 0.5f;
                float r = Mathf.Pow(sw / RefW, 1f - m) * Mathf.Pow(sh / RefH, m);
                if (float.IsNaN(r) || float.IsInfinity(r)) return 1f;
                return Mathf.Clamp(r, 1f, ResMax);
            }
            catch { return 1f; }
        }
    }
}
