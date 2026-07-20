using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Status alert pulse. The game's own caution/critical alarms tint the HUD's halo and
    /// edge-ripple contour amber or red and breathe them.
    ///
    /// CAUTION is a TRANSIENT ANNUNCIATION: it breathes amber a few times (HudConfig
    /// AlertCautionBreaths) and then clears completely, back to the authored colours, and does not
    /// return for that same condition. It tells you something crossed a line and then gets out of
    /// your way. (Its wave therefore bottoms out at zero, unlike the persistent alarm's — otherwise
    /// the last breath would end at the <see cref="SettleMix"/> floor and snap off.) A genuinely NEW
    /// caution channel joining later re-blinks, subject to <see cref="ReblinkCooldown"/>.
    ///
    /// CRITICAL is a STATE: it breathes red for as long as the condition stands and never falls to
    /// nothing. There is deliberately NO hold-down timer — a suit worn with no battery / air tank /
    /// filters therefore pulses indefinitely, matching vanilla, which keeps its warning icon lit for
    /// exactly as long. Only the condition clearing stops it.
    ///
    /// Both the halo hue and the ripple contour derive from BorderColor inside OnPopulateMesh
    /// (PanelGraphic.cs:755 'Color halo = hasBorder ? Color.Lerp(color, BorderColor, 0.75f) : color'
    /// and BorderAt at :1284 'Color c = BorderColor;'), so ONE colour filter drives both.
    ///
    /// PERF CONTRACT. PanelGraphic.BorderColor's guard is 'if (_borderColor != value)'
    /// (PanelGraphic.cs:117-121) — Unity's APPROXIMATE Color/Vector4 compare, whose epsilon is far
    /// below our 1/24 step. So the quantisation step count IS the mesh rebuild rate: roughly
    /// speed * Steps rebuilds/sec per alerted panel. The grid is additionally OFFSET per element
    /// (seeded by the element id) so panels cross steps on different frames — the wave itself stays
    /// in lockstep (an alarm must read as ONE signal, unlike ApplyPulse's deliberately desynchronised
    /// decorative breathing) while the rebuild cost spreads instead of spiking. Glow is a CONSTANT
    /// floor, never an animated value, because _glow participates in the emitted skirt and ramp-stop
    /// geometry (PanelGraphic.cs:460, :711): animating it would re-tessellate every step, so it
    /// changes only at alarm onset and clear.
    ///
    /// Deliberately does NOT touch the SDF halo-breath uniform: that is the user's own global
    /// halo-breath setting (HudConfig GlowBreathingOn) and is SDF-only, so hijacking it would both
    /// disturb an existing setting and miss mesh panels, pen shapes and lines.
    /// </summary>
    internal static class HudAlertPulse
    {
        private const int Steps = 24;              // envelope quantisation steps per cycle
        private const int BreathsDefault = 3;      // caution burst length when config is unbound
        private const float SettleMix = 0.55f;     // the critical breath's floor, as a fraction of peak
        private const float GlowFloorAmt = 0.35f;
        private const float ReblinkCooldown = 10f; // re-blink when a NEW channel joins
        private const float ArmCooldown = 3f;      // minimum gap between cold-start arms
        private const float HueBlendSeconds = 0.15f;

        /// <summary>Off = nothing rendered (also a caution's resting state once it has flashed).
        /// Burst = the transient caution flash. Endless = the critical breath, which runs until the
        /// condition clears — there is deliberately no "held steady" mode: a critical either breathes
        /// or it is gone.</summary>
        private enum Mode { Off, Burst, Endless }

        private static Mode _mode;
        private static WarnSev _sev;
        private static uint _mask;
        private static uint _lastSeq;
        private static float _phase;               // 0..1, wraps; OWNED, not read off Time
        private static int _cycles;
        private static float _hue;                 // 0 = caution colour, 1 = critical colour
        private static float _lastReblink = -999f;
        private static float _lastArm = -999f;
        private static int _demoteConfirm;
        private static int _downConfirm;           // critical -> caution downgrade debounce
        private static UnityEngine.Object _lastHuman;

        private static float _env;                 // continuous, strength-folded; quantised per call

        /// <summary>True while an alarm stands and the tint filters are live.</summary>
        internal static bool Active;

        /// <summary>F9-only preview override: 0 = off, 1 = force caution, 2 = force critical. Set by
        /// the designer's Alert pulse panel so the hues and brightness can be tuned while WATCHING
        /// them, instead of having to actually suffocate. Preview breathes CONTINUOUSLY (the caution
        /// flash loops instead of standing down) — the point is a steady thing to tune against.
        /// Cannot leak into normal play: <see cref="Tick"/> disarms it and resets on the very first
        /// frame the editor is no longer active, before any other state is read.</summary>
        internal static int PreviewMode;

        /// <summary>Full reset. Called from HudSystem.Shutdown, from the in-Update stand-down AND
        /// from the !snap.Valid exit — all three, or you return to a world with the previous
        /// session's alarm still latched.</summary>
        internal static void Reset()
        {
            _mode = Mode.Off; _sev = WarnSev.None; _mask = 0u; _lastSeq = 0u;
            _phase = 0f; _cycles = 0; _hue = 0f; _demoteConfirm = 0; _downConfirm = 0;
            _lastReblink = -999f; _lastArm = -999f; _lastHuman = null;
            _env = 0f; Active = false;
        }

        internal static void Shutdown() { Reset(); PreviewMode = 0; WarningSensor.Shutdown(); }

        /// <summary>Once per frame from HudSystem.Update, after the snapshot validity gate and BEFORE
        /// the content loop, so a severity change tints on the SAME frame rather than one frame
        /// late.</summary>
        internal static void Tick(HudSnapshot snap, bool editorActive, HudTier tier)
        {
            // Disarm the preview the instant the designer closes, BEFORE anything else reads state.
            // Without this the forced level simply kept running: with editorActive false neither
            // `preview` nor the suppression branch below is true, so nothing unwound _sev/_mode and a
            // fake alarm breathed over live play until two real samples (~0.25s) demoted it — every
            // time F9 closed, since PreviewMode also survived the editor session. Clearing it HERE
            // rather than inside Reset() is deliberate: Reset() also runs on respawn/stand-down, and
            // folding it in there would kill an in-progress preview every time the body changed.
            if (!editorActive && PreviewMode != 0)
            {
                PreviewMode = 0;
                Reset();
                // Reset() clears _sev, so the first real sample after the designer closes would look
                // like a COLD START and re-flash a caution the player already watched — breaking the
                // "does not come back for that same warning" rule. Stamping the arm cooldown blocks
                // exactly that one burst: Apply then records _sev = Caution (no longer None), so the
                // cold-start path can never fire for it again. A real CRITICAL is unaffected — its
                // promotion branch is gated on severity, not on this cooldown.
                _lastArm = Time.unscaledTime;
            }

            // Suppressed while the F9 designer is open — an author editing border colours must never
            // see a hue they did not type — UNLESS they explicitly asked to preview the alert from
            // the designer's own Alert pulse panel, which is the only way to tune the hues while
            // watching them breathe.
            bool preview = editorActive && PreviewMode != 0;

            // SUITED ONLY. HudTier.Bare is "no suit, or the suit has no power" (HudSampler.cs:16-17),
            // and the alert is a visor effect — with no powered visor there is no instrument surface
            // for it to live on, and bare mode is deliberately a felt-sense readout with no numbers.
            // Robot is allowed: that tier is documented as "always the full readout - you ARE the
            // computer" (:20-21), so it has the instrumentation an alarm belongs to.
            // The F9 PREVIEW deliberately bypasses this, since it is an explicit "show me this now"
            // override — otherwise previewing while the designer forces the bare tier shows nothing.
            if (!preview && tier == HudTier.Bare)
            { if (Active || _mode != Mode.Off) Reset(); return; }

            if ((editorActive && !preview)
                || HudConfig.FxAlertPulseOn == null || !HudConfig.FxAlertPulseOn.Value
                || snap == null || !snap.Valid || snap.Human == null)
            { if (Active || _mode != Mode.Off) Reset(); return; }

            // Respawn / body swap: hard reset so the old body's state cannot bleed through.
            if (!ReferenceEquals(_lastHuman, snap.Human)) { Reset(); _lastHuman = snap.Human; }

            // Pause / full-attention menu. WorldManager.IsGamePaused alone is NOT enough: on a
            // multiplayer client it never becomes true. Guards.VanillaMenuWantsFront() is this
            // codebase's SP/MP-agnostic answer to the same question (HudSystem.cs:943).
            // The envelope KEEPS RUNNING (the HUD stays visible behind the menu, so a frozen
            // half-brightness would sit on screen for the whole pause); only the poll and the cycle
            // counter freeze, so a long pause cannot burn the caution's breath budget.
            bool frozen = false;
            try { frozen = WorldManager.IsGamePaused || Guards.VanillaMenuWantsFront(); } catch { }

            if (preview)
            {
                // Force the previewed level. The sensor is not polled at all, so a real alarm cannot
                // fight the preview for control of the hue.
                bool crit = PreviewMode >= 2;
                _sev = crit ? WarnSev.Critical : WarnSev.Caution;
                // Show each level's ACTUAL waveform — critical's persistent breath (floored at
                // SettleMix) or caution's transient flash (falling to nothing) — rather than one
                // generic pulse, since the whole point is judging how they will really look. The
                // caution flash is LOOPED by pinning the cycle count, so it never completes and
                // stands down, which would leave nothing to tune against.
                _mode = crit ? Mode.Endless : Mode.Burst;
            }
            else if (!frozen)
            {
                WarningSensor.Tick(snap.Human);
                uint seq = WarningSensor.Seq;
                if (seq != _lastSeq)          // act on SAMPLES, never on frames
                {
                    _lastSeq = seq;
                    Apply(WarningSensor.Severity, WarningSensor.Mask);
                }
            }

            if (_mode == Mode.Off) { _env = 0f; Active = false; return; }

            // The user picks a BREATH DURATION (seconds per full fade in-and-out) because that is what
            // you actually judge by eye; the envelope wants a rate, so invert it. The 0.35s floor
            // keeps the maximum below ~3 flashes/sec, out of the photosensitivity band.
            float secs = Mathf.Clamp(
                HudConfig.FxAlertBreathSeconds != null ? HudConfig.FxAlertBreathSeconds.Value : 1.4f,
                0.35f, 5f);
            float hz = 1f / secs;
            float strength = Mathf.Clamp01(
                HudConfig.FxAlertPulseStrength != null ? HudConfig.FxAlertPulseStrength.Value : 0.6f);

            // Clamp dt so an alt-tab hitch cannot skip a whole breath.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _hue = Mathf.MoveTowards(_hue, _sev == WarnSev.Critical ? 1f : 0f, dt / HueBlendSeconds);

            // A caution BURST is transient: it flashes and then clears completely, so its wave has to
            // bottom out at ZERO. (Sharing the persistent alarm's SettleMix floor would leave the
            // tint at 55% at the end of the last breath and then snap to nothing — a visible pop.)
            // The persistent critical breath keeps the SettleMix floor: it must never fully vanish,
            // because the emergency is ongoing.
            bool burst = _mode == Mode.Burst;
            float floor = burst ? 0f : SettleMix;
            // A previewed caution loops forever so there is something to tune against. Gating on this
            // flag rather than pinning _cycles is what makes that true at EVERY flash count: pinning
            // happens once per frame, but the increment and the completion test both live inside the
            // phase-wrap loop below, so at a count of 1 a single wrap would still stand it down.
            bool previewLoop = preview && PreviewMode < 2;

            _phase += dt * hz;
            while (_phase >= 1f)
            {
                _phase -= 1f;
                if (!frozen) _cycles++;            // budget frozen behind a menu
                // Burst complete: stand fully down. A caution annunciates and then gets out of the
                // way — it does NOT leave a standing tint, and it does not come back for the same
                // condition (_sev stays Caution, so no re-arm path fires). A critical never reaches
                // here: it breathes for as long as the condition stands.
                if (burst && !previewLoop && _cycles >= BreathCount)
                {
                    _mode = Mode.Off; _phase = 0f; _env = 0f; Active = false;
                    return;
                }
            }
            // Smooth sine in/out, never a hard blink — same wave form as ApplyPulse
            // (HudElementView.cs:831).
            float s = 0.5f + 0.5f * Mathf.Sin((_phase - 0.25f) * 2f * Mathf.PI);
            float env = Mathf.Lerp(floor, 1f, s);

            // Strength means different things for the two shapes, because their end states differ:
            //  - BURST: it scales the PEAK (0.5..1). It must never scale the floor, or strength 0
            //    would make the only caution cue invisible.
            //  - ENDLESS: it scales the SWING about the floor, so strength 0 is a steady tint
            //    with no motion at all — the photosensitivity accommodation the config text promises.
            _env = burst
                ? env * Mathf.Lerp(0.5f, 1f, strength)
                : SettleMix + (env - SettleMix) * strength;
            Active = true;
        }

        private static void Apply(WarnSev sev, uint mask)
        {
            if (sev == WarnSev.None)
            {
                if (++_demoteConfirm < 2) return;      // two agreeing POLLS (0.5 s)
                _mode = Mode.Off; _sev = WarnSev.None; _mask = 0u; _cycles = 0; _phase = 0f;
                _demoteConfirm = 0;
                // _hue too, or a caution arriving after a cleared critical renders RED for the
                // 0.15s the crossfade takes to walk it back down.
                _hue = 0f;
                return;
            }
            _demoteConfirm = 0;

            bool newCondition = (mask & ~_mask) != 0u;
            _mask = mask;
            float now = Time.unscaledTime;

            if (sev == WarnSev.Critical)
            {
                if (_sev != WarnSev.Critical)
                {
                    // Promotion is IMMEDIATE — do not finish the amber burst. Coming from a MOVING
                    // burst the phase is preserved so the rhythm does not stutter mid-inhale (the hue
                    // cross-fades instead, and the escalation reads as one alarm getting worse rather
                    // than two separate events). From the still Off state there is no rhythm to keep,
                    // so start at the trough or the red breath snaps on at an arbitrary brightness.
                    if (_mode != Mode.Burst) _phase = 0f;
                    _mode = Mode.Endless; _cycles = 0;
                }
                _downConfirm = 0;   // a confirming critical cancels any pending downgrade
                // No hold-down timer: a critical breathes for as long as the condition stands, which
                // means a suit worn with no battery / air tank / filters pulses indefinitely
                // (IsPowerCritical StatusUpdates.cs:435, IsAirTankCritical :555, IsFilterCritical
                // :757 all return true for an ABSENT component). That is intended — it matches
                // vanilla, which keeps the warning icon lit for exactly as long.
                _sev = WarnSev.Critical;
                return;
            }

            // Caution.
            if (_sev == WarnSev.Critical)
            {
                // Debounce the downgrade on TWO agreeing polls, mirroring the None demote above.
                // A continuous channel (oxygen, pressure, temperature) sitting exactly on its
                // critical threshold flips critical/caution sample to sample, and since this branch
                // now stands the alarm fully DOWN rather than dropping to a steady amber, an
                // undebounced flap would blink the red hard on and off at ~2 Hz — squarely in the
                // band the breath-duration floor exists to stay out of. Costs at most one extra
                // 0.25s poll of red on a genuine recovery.
                // NOTE: a separate counter from _demoteConfirm on purpose — that one is zeroed for
                // every non-None sample, so a caution would clear it before a second could land.
                if (++_downConfirm < 2) return;
                _downConfirm = 0;

                // Recovery from critical: stand the alarm fully down rather than dropping to a
                // standing amber. Caution is a transient annunciation, not a state colour — and
                // recovery must not be punished with a fresh burst either.
                // _hue and _phase MUST be cleared here like every other Off transition: Tick
                // early-returns on Mode.Off before it advances either, so whatever the red breath
                // left would stay frozen — giving a later caution a red first rise, and a later
                // re-escalation a breath that resumes mid-wave instead of from the trough.
                _sev = WarnSev.Caution; _mode = Mode.Off; _cycles = BreathCount;
                _hue = 0f; _phase = 0f;
                return;
            }
            bool coldStart = _sev == WarnSev.None && now - _lastArm > ArmCooldown;
            bool reblink = newCondition && now - _lastReblink > ReblinkCooldown;
            if (coldStart || reblink)
            {
                _mode = Mode.Burst; _cycles = 0; _phase = 0f;
                _lastArm = now; _lastReblink = now;
            }
            // NOTE: no fallback branch here. Leaving _mode at Off while _sev is Caution is now the
            // CORRECT resting state — a caution that has spent its breath budget, or one suppressed
            // by the arm cooldown for flapping across its threshold, shows nothing. (An earlier
            // revision forced a steady held tint here, back when a caution left one.)
            _sev = WarnSev.Caution;
        }

        /// <summary>How many breaths a caution runs before standing down. Config-driven so the flash
        /// can be tuned by eye; clamped because a zero or negative count would complete the burst
        /// instantly and a caution would never be seen at all.</summary>
        private static int BreathCount
        {
            get
            {
                return HudConfig.FxAlertCautionBreaths != null
                    ? Mathf.Clamp(HudConfig.FxAlertCautionBreaths.Value, 1, 10) : BreathsDefault;
            }
        }

        private static float Quantise(float v, int seed)
        {
            // Per-element grid OFFSET (not a phase offset): the wave stays in lockstep so the alarm
            // reads as one signal, but elements cross steps on different frames so the mesh rebuild
            // cost spreads instead of spiking across the whole document on one frame.
            float off = 0f;
            if (seed != 0) { unchecked { off = ((uint)seed * 2654435761u % 1000u) / 1000f; } }
            return Mathf.Clamp01((Mathf.Round(v * Steps - off) + off) / Steps);
        }

        /// <summary>The alert hue at this point of the caution-to-critical crossfade, with each
        /// level's own brightness gain applied BEFORE the blend — so the two levels are independently
        /// tunable and a crossfade between them stays continuous. RGB only: the gain must never touch
        /// alpha, which carries the hasBorder lift in <see cref="Tint"/>. Deliberately unclamped here
        /// (a gain above 1 is allowed to blow the hue out toward white); Tint does the final clamp.</summary>
        private static Color AlertColor(float hueQ)
        {
            Color a = HudPalette.AlertCaution != null
                ? HudPalette.AlertCaution.Value : new Color(1f, 0.694f, 0.239f, 1f);
            Color b = HudPalette.AlertCritical != null
                ? HudPalette.AlertCritical.Value : new Color(1f, 0.290f, 0.239f, 1f);
            float ga = HudConfig.FxAlertCautionBright != null ? HudConfig.FxAlertCautionBright.Value : 1f;
            float gb = HudConfig.FxAlertCriticalBright != null ? HudConfig.FxAlertCriticalBright.Value : 1f;
            a.r *= ga; a.g *= ga; a.b *= ga;
            b.r *= gb; b.g *= gb; b.b *= gb;
            return Color.Lerp(a, b, hueQ);
        }

        internal static Color Tint(Color c) { return Tint(c, 0); }

        /// <summary>Blend a border/stroke colour toward the alert colour and apply the breath's
        /// brightness. Identity when the feature is off or no alarm stands, so every call site stays
        /// a pure function of current state: a faded-out element self-heals on the frame it returns,
        /// and nothing has to be unwound when the alarm ends. <paramref name="seed"/> is
        /// HudDocument.StableSeed(Def.Id); 0 is legal.</summary>
        internal static Color Tint(Color c, int seed)
        {
            if (!Active || _env <= 0.0001f) return c;
            float q = Quantise(_env, seed);
            if (q <= 0.0001f) return c;
            // _hue is quantised on the SAME grid: an unquantised 0.15s crossfade would otherwise
            // re-mesh every alerted panel every frame during an escalation.
            Color a = AlertColor(Quantise(_hue, seed));
            float bright = 1f + Mathf.Clamp01(
                HudConfig.FxAlertPulseStrength != null ? HudConfig.FxAlertPulseStrength.Value : 0.6f) * q;
            Color o = Color.Lerp(c, a, q);
            o.r = Mathf.Clamp01(o.r * bright);
            o.g = Mathf.Clamp01(o.g * bright);
            o.b = Mathf.Clamp01(o.b * bright);
            // Lift alpha over PanelGraphic's hasBorder gate ('bw > 0.05f && BorderColor.a > 0.004f',
            // PanelGraphic.cs:635) so a near-clear border still emits a coloured halo. A zero
            // BorderWidth still cannot — noted on the play-test list.
            // The picked colour's own alpha SCALES that lift, so the pickers' alpha bar means
            // something ("how hard may this alert force a faint border to show?") instead of being a
            // live-looking control with no effect. Both palette defaults are opaque, so this is
            // behaviour-preserving at 1.0.
            o.a = Mathf.Clamp01(Mathf.Max(c.a, q * 0.45f * a.a));
            return o;
        }

        /// <summary>Outer-glow floor. FxGlowOn defaults FALSE (HudConfig.cs:532), so on a default
        /// install there would be no halo to tint at all. CONSTANT rather than envelope-scaled so the
        /// skirt and ramp-stop count are decided once at onset instead of on every quantisation step,
        /// and gated on Tier A by the CALLER: Tier A off is a deliberate perf/geometry choice whose
        /// documented "everything 0 = classic 0.8.0 output" invariant must hold. Within an enabled
        /// Tier A the floor DOES override a per-element customGlowOn opt-out — the alarm is a safety
        /// signal, that toggle is a styling preference, and the whole feature sits behind a checkbox.
        /// Never applied to GlowInner (that band sits under the readout text).</summary>
        internal static float Glow(float g)
        {
            return Active ? Mathf.Max(g, GlowFloorAmt) : g;
        }
    }
}
