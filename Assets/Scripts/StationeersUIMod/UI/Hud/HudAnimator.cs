using System.Collections.Generic;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The flicker engine. Every HUD panel gets a <see cref="Fader"/> driving its
    /// CanvasGroup: turning a panel OFF plays a short square-noise flicker-out, turning it
    /// ON plays a flicker-in (staggered across panels on a full suit boot). Suit power
    /// DEATH is the big one: 0.8s of decaying flicker plus a CRT-style vertical collapse,
    /// then the BARE tier fades in slowly (your eyes adjust). Below the low-power
    /// threshold the whole HUD suffers occasional single-frame dropouts.
    ///
    /// Everything runs on unscaled time; noise is Perlin (deterministic per seed) so two
    /// panels never blink in lockstep.
    /// </summary>
    public sealed class HudAnimator
    {
        private const float FlickerOutDur = 0.4f;
        private const float FlickerInDur = 0.5f;
        private const float PowerDeathDur = 0.8f;
        private const float BareFadeInDur = 2.2f;
        private const float BootStaggerStep = 0.09f;

        public sealed class Fader
        {
            public CanvasGroup Group;
            public RectTransform Root;
            public int Seed;
            public bool SuitTier;             // participates in power-death collapse

            internal bool Target = true;
            internal float Phase = 1f;        // 1 = settled visible, 0 = settled hidden
            internal float Timer = -1f;       // active animation clock (<0 = idle)
            internal float Delay;             // boot stagger
            internal bool DyingCollapse;      // this hide is the power-death animation
            internal bool SlowFadeIn;         // this show is the BARE fade
            internal float CapAlpha = 1f;     // a panel dying mid-boot must not FLASH first

            internal void Snap(bool visible)
            {
                Target = visible;
                Timer = -1f;
                Phase = visible ? 1f : 0f;
                if (Group != null) Group.alpha = visible ? 1f : 0f;
                if (Root != null) Root.localScale = Vector3.one;
                if (Group != null) Group.gameObject.SetActive(visible);
            }
        }

        private readonly List<Fader> _faders = new List<Fader>();
        private float _dropoutUntil = -1f;
        private float _nextDropoutRoll;

        public Fader Register(RectTransform root, CanvasGroup group, bool suitTier, int seed)
        {
            var f = new Fader { Root = root, Group = group, SuitTier = suitTier, Seed = seed };
            _faders.Add(f);
            return f;
        }

        public void Clear() => _faders.Clear();

        /// <summary>Per-panel desired visibility. Animates the change unless animations
        /// are off (or the fader is brand new).</summary>
        public void SetVisible(Fader f, bool visible, bool instant = false)
        {
            if (f == null || f.Target == visible) return;
            f.Target = visible;
            if (instant || HudConfig.FlickerAnimations == null || !HudConfig.FlickerAnimations.Value)
            {
                f.Snap(visible);
                return;
            }
            f.Timer = 0f;
            f.Delay = 0f;
            f.DyingCollapse = false;
            f.SlowFadeIn = false;
            if (visible && f.Group != null) f.Group.gameObject.SetActive(true);
        }

        /// <summary>The suit died: every suit-tier panel flickers out WITH the vertical
        /// collapse; the BARE panels come back with the slow fade.</summary>
        public void PowerDeath()
        {
            foreach (var f in _faders)
            {
                if (!f.SuitTier || !f.Target) continue;
                f.Target = false;
                f.Timer = 0f;
                f.Delay = 0f;
                f.DyingCollapse = true;
                f.SlowFadeIn = false;
                // A panel still mid-boot (alpha near 0) collapses from where it IS —
                // never a full-brightness flash on its way out.
                f.CapAlpha = f.Group != null ? Mathf.Clamp(f.Group.alpha, 0.1f, 1f) : 1f;
            }
        }

        /// <summary>Suit booted: suit-tier panels flicker in one by one. The eligibility
        /// filter keeps config-DISABLED panels dark (the animator doesn't know about
        /// toggles; the caller does).</summary>
        public void BootUp(System.Func<Fader, bool> eligible = null)
        {
            int i = 0;
            foreach (var f in _faders)
            {
                if (!f.SuitTier || f.Target) continue;
                if (eligible != null && !eligible(f)) continue;
                f.Target = true;
                f.Timer = 0f;
                f.Delay = BootStaggerStep * i++;
                f.DyingCollapse = false;
                f.SlowFadeIn = false;
                if (f.Group != null) f.Group.gameObject.SetActive(true);
            }
        }

        /// <summary>Marks the next show of this fader as the slow BARE fade-in.</summary>
        public void SlowShow(Fader f)
        {
            if (f == null || f.Target) return;
            f.Target = true;
            f.Timer = 0f;
            f.Delay = 0.6f; // the world goes dark for a beat before senses return
            f.DyingCollapse = false;
            f.SlowFadeIn = true;
            if (f.Group != null) f.Group.gameObject.SetActive(true);
        }

        /// <summary>Global multiplier from low-power dropouts (applied to the root group).</summary>
        public float DropoutMultiplier(bool lowPower, float time)
        {
            if (!lowPower || HudConfig.LowPowerDropouts == null || !HudConfig.LowPowerDropouts.Value)
                return 1f;
            if (time < _dropoutUntil) return 0.06f;
            if (time >= _nextDropoutRoll)
            {
                // Roll every ~0.3s; ~15% of rolls drop 1-3 frames.
                _nextDropoutRoll = time + 0.3f;
                if (Mathf.PerlinNoise(time * 1.7f, 88.13f) > 0.72f)
                    _dropoutUntil = time + Mathf.Lerp(0.02f, 0.07f, Mathf.PerlinNoise(time, 3.7f));
            }
            return 1f;
        }

        public void Update(float dt, float time)
        {
            foreach (var f in _faders)
            {
                if (f.Timer < 0f || f.Group == null) continue;
                f.Timer += dt;
                float t = f.Timer - f.Delay;
                if (t < 0f) { f.Group.alpha = f.Target ? 0f : f.Group.alpha; continue; }

                if (!f.Target)
                {
                    float dur = f.DyingCollapse ? PowerDeathDur : FlickerOutDur;
                    float p = Mathf.Clamp01(t / dur);
                    float decay = 1f - p;
                    bool on = Mathf.PerlinNoise(time * 24f, f.Seed * 7.31f) > 0.35f + 0.5f * p;
                    f.Group.alpha = decay * (on ? 1f : 0.12f) * (f.DyingCollapse ? f.CapAlpha : 1f);
                    if (f.DyingCollapse && f.Root != null)
                    {
                        // CRT die: stretch a touch wider while the panel squashes flat.
                        float sq = 1f - p * p;
                        f.Root.localScale = new Vector3(1f + 0.25f * p, Mathf.Max(0.02f, sq), 1f);
                    }
                    if (p >= 1f) f.Snap(false);
                }
                else if (f.SlowFadeIn)
                {
                    float p = Mathf.Clamp01(t / BareFadeInDur);
                    f.Group.alpha = p * p; // ease-in — senses come back gradually
                    if (p >= 1f) f.Snap(true);
                }
                else
                {
                    float p = Mathf.Clamp01(t / FlickerInDur);
                    bool on = Mathf.PerlinNoise(time * 26f, f.Seed * 11.77f) > 0.75f - 0.6f * p;
                    f.Group.alpha = p * (on ? 1f : 0.15f);
                    if (f.Root != null) f.Root.localScale = Vector3.one;
                    if (p >= 1f) f.Snap(true);
                }
            }
        }
    }
}
