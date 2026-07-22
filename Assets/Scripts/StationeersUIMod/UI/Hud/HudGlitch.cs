using System;
using System.Collections.Generic;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A momentary HUD-ONLY "glitch" on the power transitions (suit death / taken off / boot):
    /// it tears and shakes the HUD panels themselves rather than running a screen shader. A
    /// camera shader can't cleanly touch the overlay HUD (and turns the see-through HUD into a
    /// solid box), so this jitters each panel's transform instead — reliable, visible in EVERY
    /// curvature mode, and genuinely confined to the HUD.
    ///
    /// Time-based envelope (no per-frame Tick dependency): fire sets a start time, and both
    /// <see cref="Active"/> and <see cref="CurrentIntensity"/> read the clock, so the effect
    /// self-expires even if the HUD update early-outs. It rides on top of the existing
    /// power-death CRT squash + flicker (which own Group.alpha and localScale); this only moves
    /// anchoredPosition, so the two never fight.
    /// </summary>
    internal static class HudGlitch
    {
        private static float _startTime = -999f;
        private static float _duration;
        private static float _maxIntensity;
        private static bool _applying;

        private static float Elapsed => Time.unscaledTime - _startTime;
        public static bool Active => _duration > 0f && Elapsed < _duration;

        /// <summary>Fire on a real power transition, honoring the per-event config gates.</summary>
        public static void Trigger(bool powerDown)
        {
            try
            {
                if (HudConfig.GlitchEnabled == null || !HudConfig.GlitchEnabled.Value) return;
                if (powerDown && !HudConfig.GlitchOnPowerDown.Value) return;
                if (!powerDown && !HudConfig.GlitchOnPowerUp.Value) return;
                Fire();
            }
            catch (Exception e) { UIALog.Warn("HUD glitch trigger failed: " + e.Message); }
        }

        /// <summary>The F10 "Test glitch" button — always fires, ignoring the gates.</summary>
        public static void TriggerTest()
        {
            try { Fire(); }
            catch (Exception e) { UIALog.Warn("HUD glitch test failed: " + e.Message); }
        }

        private static void Fire()
        {
            _maxIntensity = HudConfig.GlitchIntensity != null ? HudConfig.GlitchIntensity.Value : 0.85f;
            _duration = Mathf.Max(0.1f, HudConfig.GlitchDuration != null ? HudConfig.GlitchDuration.Value : 1.1f);
            _startTime = Time.unscaledTime;
        }

        public static float CurrentIntensity()
        {
            float e = Elapsed;
            if (_duration <= 0f || e >= _duration) return 0f;
            float p = Mathf.Clamp01(1f - e / _duration);   // 1 -> 0 across the window
            float flick = 0.5f + 0.5f * Mathf.PerlinNoise(Time.unscaledTime * 37f, 0.7f);
            return Mathf.Clamp01(p * flick) * _maxIntensity;
        }

        /// <summary>Per-frame: tear the HUD panels while active, and snap them back to their laid-out
        /// position on the frame the effect ends. Called from HudSystem after the animator.</summary>
        public static void ApplyToPanels(List<HudPanel> panels)
        {
            if (panels == null) return;
            bool active = Active;
            if (!active && !_applying) return; // idle, nothing to restore

            float i = active ? CurrentIntensity() : 0f;
            float t = Time.unscaledTime;

            // Tears SNAP between short time buckets (hold ~holdSec, then cut elsewhere) — that
            // abrupt jump is what reads as tearing; a smooth curve just reads as shake.
            const float holdSec = 0.05f;
            float bucket = Mathf.Floor(t / holdSec);

            for (int k = 0; k < panels.Count; k++)
            {
                var p = panels[k];
                var root = p != null ? p.Root : null;
                if (root == null) continue;

                // Per-element opt-in + strength (0 = this element doesn't tear).
                // Non-document panels fall back to the GLOBAL strength, not a hard 1f — the literal
                // meant the "Glitch tear" master was ignored entirely for every legacy fixed panel.
                // GlobalAmtFor already returns 0 when that master is off.
                float amt = (p is HudElementView view)
                    ? view.TransitionAmt("fxGlitch")
                    : HudTransitionFx.GlobalAmtFor("fxGlitch");
                float pi = i * amt;
                if (pi <= 0.001f) { root.anchoredPosition = Vector2.zero; continue; }

                float seed = k * 3.71f + 1.3f;
                // Fine continuous shake.
                float shake = (Mathf.PerlinNoise(t * 55f, seed) - 0.5f) * 2f;   // -1..1
                // Hard tear: this panel jumps to a big held offset on ~half its buckets, then
                // cuts — a sharp discontinuity, not a drift. Hashed per panel + per bucket.
                bool tearing = Hash(bucket * 1.7f + seed) > 0.5f;
                float tear = tearing ? (Hash(bucket + seed * 2.3f) - 0.5f) * 2f : 0f; // -1..1
                float dx = (shake * 12f + tear * 140f) * pi;
                float dy = (Mathf.PerlinNoise(t * 40f, seed + 9f) - 0.5f) * 2f * 9f * pi;
                root.anchoredPosition = new Vector2(dx, dy);
            }
            _applying = active;
        }

        /// <summary>Cheap per-value pseudo-random hash (GLSL-style frac(sin·k)) — SNAPS from one
        /// value to the next, unlike Perlin, which is what makes the tear abrupt.</summary>
        private static float Hash(float n)
        {
            float s = Mathf.Sin(n) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        public static void Shutdown()
        {
            _startTime = -999f;
            _duration = 0f;
            _applying = false;
        }
    }
}
