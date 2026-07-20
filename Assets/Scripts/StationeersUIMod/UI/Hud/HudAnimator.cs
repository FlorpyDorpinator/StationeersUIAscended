using System.Collections.Generic;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The flicker engine. Every HUD panel gets a <see cref="Fader"/> driving its
    /// CanvasGroup: turning a panel OFF plays a short square-noise flicker-out, turning it
    /// ON plays a flicker-in (staggered across panels on a full suit boot). Suit power
    /// DEATH is the big one: 0.8s of decaying flicker plus, optionally, one of two CRT
    /// death animations — the vertical collapse (fxCollapse) or the full TV-OFF squash to a
    /// line and pinch to a dot (fxTvOff) — then the BARE tier fades in slowly (your eyes
    /// adjust). Below the low-power threshold the whole HUD suffers occasional
    /// single-frame dropouts.
    ///
    /// Every one of those is PER ELEMENT. The animator itself owns no per-element policy:
    /// HudSystem pushes a resolved 0..2 strength into each Fader every frame
    /// (CollapseAmt / TvOffAmt / FlickerAmt / DissolveAmt) from the shared
    /// <see cref="HudTransitionFx"/> resolver, and 0 always means "this element sits this
    /// one out". The global masters still gate the whole effect in PowerDeath.
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

        // --- TV-OFF shape (the classic CRT power-off) ---
        /// <summary>Fraction of the death spent squashing to the horizontal line; the rest pinches
        /// that line to a dot. 0.6 reads as "collapse, hold, wink" at every strength.</summary>
        private const float TvLinePhase = 0.6f;
        /// <summary>Vertical scale the picture squashes down to — the "line". Not 0: a zero scale
        /// makes Unity drop the mesh entirely, and the whole point is that the line stays lit.</summary>
        private const float TvLineThickness = 0.02f;
        /// <summary>Horizontal scale the line pinches down to — the "dot".</summary>
        private const float TvDotSize = 0.01f;
        /// <summary>Where in phase 2 the dot stops holding and blinks out.</summary>
        private const float TvWinkStart = 0.72f;

        public sealed class Fader
        {
            public CanvasGroup Group;
            public RectTransform Root;
            public int Seed;
            public bool SuitTier;             // participates in power-death collapse
            /// <summary>Per-element CRT-collapse strength on death: 0 = no squash (just flicker
            /// out), 1 = the default squash, up to 2. Synced from the element's fxCollapse props.</summary>
            public float CollapseAmt = 1f;
            /// <summary>Per-element TV-OFF strength on death: 0 = the element does not play the
            /// horizontal collapse at all, 1 = the default shape, 2 = exaggerated (wider bloom,
            /// harder pinch). Synced from the element's fxTvOff props by HudSystem.
            ///
            /// TV-off and the vertical collapse both drive localScale, so they cannot both play on
            /// one element: TV-off WINS, because it is the complete animation and the collapse is
            /// only its first half.</summary>
            public float TvOffAmt = 1f;
            /// <summary>Per-element flicker DEPTH: 0 = a clean fade with no blinking at all,
            /// 1 = the classic square-noise flicker, 2 = heavy (blinks all the way to black and
            /// spends more frames dark). Synced from the element's fxFlicker props.</summary>
            public float FlickerAmt = 1f;
            /// <summary>Per-element dissolve PARTICIPATION (the frontier itself is one shared
            /// uniform, so this gates rather than scales — see the class remarks on HudSystem's
            /// dissolve). 0 = this element does not ride the dissolve, so it guttters out at the
            /// normal flicker speed instead of lingering for the frontier.</summary>
            public float DissolveAmt = 1f;

            internal bool Target = true;
            internal float Phase = 1f;        // 1 = settled visible, 0 = settled hidden
            internal float Timer = -1f;       // active animation clock (<0 = idle)
            internal float Delay;             // boot stagger
            internal bool DyingPower;         // this hide is a power-DEATH (not an ordinary hide)
            internal bool DyingCollapse;      // ...and it plays the vertical CRT collapse
            internal bool DyingTvOff;         // ...and it plays the TV-off horizontal collapse
            internal bool SlowFadeIn;         // this show is the BARE fade
            internal float CapAlpha = 1f;     // a panel dying mid-boot must not FLASH first

            internal void Snap(bool visible)
            {
                Target = visible;
                Timer = -1f;
                Phase = visible ? 1f : 0f;
                // The death flags describe the animation that just ENDED; leaving them set would
                // let the next ordinary hide inherit a power-death duration and a CRT squash.
                DyingPower = false;
                DyingCollapse = false;
                DyingTvOff = false;
                if (Group != null) Group.alpha = visible ? 1f : 0f;
                if (Root != null) Root.localScale = Vector3.one;
                if (Group != null) Group.gameObject.SetActive(visible);
            }
        }

        private readonly List<Fader> _faders = new List<Fader>();
        private float _dropoutUntil = -1f;
        private float _nextDropoutRoll;

        /// <summary>When &gt; 0, the fade-OUT lasts this long instead of the built-in flicker/death
        /// duration. Set by HudSystem so a power-down that is playing the 1.2 s dissolve frontier
        /// keeps its panels on screen for the whole animation instead of vanishing in 0.4 s.</summary>
        public float OutDurOverride;

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
            f.DyingPower = false;
            f.DyingCollapse = false;
            f.DyingTvOff = false;
            f.SlowFadeIn = false;
            if (visible && f.Group != null) f.Group.gameObject.SetActive(true);
        }

        /// <summary>The suit died: every suit-tier panel flickers out WITH the vertical
        /// collapse; the BARE panels come back with the slow fade.</summary>
        public void PowerDeath()
        {
            // "Turn off the same way it turns on" (FlorpyDorp): BootUp staggers each panel by
            // BootStaggerStep, so mirroring it means power-down must stagger too — the old code
            // pinned Delay to 0, which is why OFF was one abrupt flash while ON rippled.
            // DyingCollapse (the CRT squash) is now a separate, opt-in flourish rather than the
            // definition of powering down; with the mirror on, panels simply flicker out in order.
            bool mirror = HudConfig.FxPowerDownMirrorsBoot == null
                       || HudConfig.FxPowerDownMirrorsBoot.Value;
            // Two INDEPENDENT things, which is the distinction the old code never drew:
            //   mirror   -> does power-down ripple like boot, or happen all at once?
            //   collapse -> is the CRT squash played at all? (now opt-in, default OFF)
            // Per-element strength still scales the squash via Fader.CollapseAmt.
            bool collapse = HudConfig.FxCollapseOn != null && HudConfig.FxCollapseOn.Value;
            // TV-off is the newer, complete CRT power-off (squash to a line, pinch to a dot). Its
            // global master defaults OFF like the collapse, and the per-element strength in
            // Fader.TvOffAmt decides which elements actually play it.
            bool tvOff = HudConfig.FxTvOffOn != null && HudConfig.FxTvOffOn.Value;
            int i = 0;
            foreach (var f in _faders)
            {
                if (!f.SuitTier || !f.Target) continue;
                f.Target = false;
                f.Timer = 0f;
                f.Delay = mirror ? BootStaggerStep * i++ : 0f;
                f.DyingPower = true;
                f.DyingCollapse = collapse;
                f.DyingTvOff = tvOff;
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
                f.DyingPower = false;
                f.DyingCollapse = false;
                f.DyingTvOff = false;
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
            f.DyingPower = false;
            f.DyingCollapse = false;
            f.DyingTvOff = false;
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
                    float flick = Mathf.Clamp(f.FlickerAmt, 0f, 2f);
                    float tv = f.DyingTvOff ? Mathf.Clamp(f.TvOffAmt, 0f, 2f) : 0f;

                    // A dissolve-OUT runs 1.2s on the shader; the default 0.4s flicker would drop
                    // alpha to zero long before the frontier finished, so the effect would be
                    // invisible. HudSystem raises OutDurOverride to the dissolve length so the panel
                    // stays on screen exactly as long as the animation it is playing.
                    //
                    // Two gates on that stretch, both of which used to be missing:
                    //   DyingPower   — an ORDINARY hide (toggling an element off) must never inherit
                    //                  the dissolve length, which stayed set until the next boot.
                    //   DissolveAmt  — an element that opted OUT of the dissolve has no frontier to
                    //                  wait for, so it gutters out at its normal speed.
                    bool stretched = f.DyingPower && OutDurOverride > 0.01f && f.DissolveAmt > 0.001f;
                    float dur = stretched
                        ? OutDurOverride
                        : ((f.DyingCollapse || f.DyingTvOff) ? PowerDeathDur : FlickerOutDur);
                    float p = Mathf.Clamp01(t / dur);
                    float decay = 1f - p;
                    float cap = (f.DyingCollapse || f.DyingTvOff) ? f.CapAlpha : 1f;

                    // Flicker DEPTH is per-element now: 0 collapses the square-noise blink into a
                    // clean linear fade, 1 is the classic 0.12 floor, 2 blinks to full black and
                    // biases the noise threshold so more frames land dark.
                    float lo = flick <= 1f
                        ? Mathf.Lerp(1f, 0.12f, flick)
                        : Mathf.Lerp(0.12f, 0f, flick - 1f);
                    float thresh = 0.35f + 0.5f * p + 0.15f * Mathf.Max(0f, flick - 1f);
                    bool on = flick <= 0.001f
                           || Mathf.PerlinNoise(time * 24f, f.Seed * 7.31f) > thresh;
                    float a = decay * (on ? 1f : lo) * cap;

                    if (tv > 0.001f && f.Root != null)
                    {
                        // THE TV OFF. Phase 1: the picture squashes to a hot horizontal line while
                        // holding full opacity — the apparent brightness comes from the same light
                        // being crushed into a fraction of the area, which is exactly how the real
                        // thing reads (alpha cannot exceed 1, so a literal flash is not available).
                        // Phase 2: that line pinches to a dot, hangs for a beat, and blinks out.
                        float blend = Mathf.Clamp01(tv);        // 0..1 eases the whole effect in
                        float over = Mathf.Clamp01(tv - 1f);    // 1..2 exaggerates it
                        float sx, sy, aTv;
                        if (p < TvLinePhase)
                        {
                            float q = p / TvLinePhase;
                            float e = q * q * q;                // hangs, then snaps shut
                            sy = Mathf.Lerp(1f, TvLineThickness, e);
                            sx = 1f + (0.06f + 0.16f * over) * e; // the line blooms a little wider
                            aTv = 1f;
                        }
                        else
                        {
                            float q = Mathf.Clamp01((p - TvLinePhase) / (1f - TvLinePhase));
                            sy = TvLineThickness;
                            sx = Mathf.Lerp(1f + 0.06f + 0.16f * over, TvDotSize, q * q);
                            aTv = 1f - Mathf.Clamp01((q - TvWinkStart) / (1f - TvWinkStart));
                            aTv *= aTv;                         // the dot holds, then blinks
                        }
                        f.Root.localScale = new Vector3(
                            Mathf.Lerp(1f, sx, blend), Mathf.Lerp(1f, sy, blend), 1f);
                        // At partial strength the element is half TV-off, half plain flicker-out.
                        a = Mathf.Lerp(a, aTv * cap, blend);
                    }
                    else if (f.DyingCollapse && f.Root != null)
                    {
                        // CRT die: stretch a touch wider while the panel squashes flat. Per-element
                        // strength scales it — 0 leaves the panel un-squashed (flicker-out only).
                        float amt = Mathf.Clamp(f.CollapseAmt, 0f, 2f);
                        if (amt <= 0.001f) f.Root.localScale = Vector3.one;
                        else
                        {
                            float sq = 1f - p * p;
                            float sx = Mathf.Lerp(1f, 1f + 0.25f * p, amt);
                            float sy = Mathf.Max(0.02f, Mathf.Lerp(1f, Mathf.Max(0.02f, sq), amt));
                            f.Root.localScale = new Vector3(sx, sy, 1f);
                        }
                    }
                    f.Group.alpha = Mathf.Clamp01(a);
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
                    // Strike-on, same depth control as the fade-out: 0 = a clean ramp with no
                    // blinking, 1 = the classic strike, 2 = a struggling tube.
                    float flick = Mathf.Clamp(f.FlickerAmt, 0f, 2f);
                    float p = Mathf.Clamp01(t / FlickerInDur);
                    float lo = flick <= 1f
                        ? Mathf.Lerp(1f, 0.15f, flick)
                        : Mathf.Lerp(0.15f, 0f, flick - 1f);
                    float thresh = 0.75f - 0.6f * p + 0.12f * Mathf.Max(0f, flick - 1f);
                    bool on = flick <= 0.001f
                           || Mathf.PerlinNoise(time * 26f, f.Seed * 11.77f) > thresh;
                    f.Group.alpha = p * (on ? 1f : lo);
                    if (f.Root != null) f.Root.localScale = Vector3.one;
                    if (p >= 1f) f.Snap(true);
                }
            }
        }
    }
}
