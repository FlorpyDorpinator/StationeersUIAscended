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
    /// LEAVING an alert is a soft fade, never a snap (Mode.FadeOut): whenever the alarm stands down
    /// with the tint still lit — a warning fixed mid-flash, a critical clearing, a critical recovering
    /// to caution — the envelope and the forced-glow floor ease to nothing over <see cref="FadeOutSeconds"/>,
    /// holding the on-screen colour. A caution that runs its full flash budget ends at its own trough,
    /// so it needs no extra tail.
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
        private const float GlowOverMix = 0.5f;    // how much of a >1 brightness gain the halo floor takes
        private const float ReblinkCooldown = 10f; // re-blink when a NEW channel joins
        private const float ArmCooldown = 3f;      // minimum gap between cold-start arms
        private const float HueBlendSeconds = 0.15f;
        private const float FadeOutSeconds = 0.6f; // soft stand-down; NOT tied to breath length — a fade wants to be quick

        /// <summary>Off = nothing rendered (also a caution's resting state once it has flashed).
        /// Burst = the transient caution flash. Endless = the critical breath, which runs until the
        /// condition clears — there is deliberately no "held steady" mode: a critical either breathes
        /// or it is gone. FadeOut = the soft stand-down: a captured envelope and hue easing to nothing
        /// so LEAVING an alert is never an instant snap.</summary>
        private enum Mode { Off, Burst, Endless, FadeOut }

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

        // Soft fade-out (Mode.FadeOut): the visible tail when an alarm stands down.
        private static float _fadeFrom;            // _env snapshot at the instant the fade began
        private static float _fadeHue;             // hue held for the visible duration of the fade
        private static float _fadeT;               // 0..1 fade progress
        private static float _alertGlow = 1f;      // forced-glow-floor scale: 1 while an alarm stands, eases to 0 on fade-out

        /// <summary>True while an alarm stands and the tint filters are live.</summary>
        internal static bool Active;

        /// <summary>F9-only preview override: 0 = AUTO (follow the designer's Effects &gt; Alerts
        /// sub-tab — see <see cref="RequestAutoPreview"/>), 1 = force caution, 2 = force critical,
        /// -1 = force suppressed. Set by the designer's Alert pulse panel so the hues and brightness
        /// can be tuned while WATCHING them, instead of having to actually suffocate. Preview breathes
        /// CONTINUOUSLY (the caution flash loops instead of standing down) — the point is a steady
        /// thing to tune against. An EXPLICIT level (1/2) is also what carries the alarm to the OTHER
        /// sub-tabs, so a halo, edge-light or border-colour edit can be judged with the alarm lit.
        /// Cannot leak into normal play: <see cref="Tick"/> disarms it and resets on the very first
        /// frame the editor is no longer active, before any other state is read.</summary>
        internal static int PreviewMode;

        /// <summary>What AUTO resolves to: the persistent CRITICAL breath. It is the level that
        /// stands still long enough to judge (a caution is a three-flash annunciation that clears),
        /// and its brightness gain is the knob that was actually broken — see
        /// <see cref="LevelColor"/>.</summary>
        private const int AutoPreviewLevel = 2;

        /// <summary>Frame on which the designer's Effects &gt; Alerts sub-tab last drew itself. A
        /// frame STAMP, not a bool, so nothing has to remember to clear it when the sub-tab loses
        /// focus, the window closes, or ImGui simply skips a draw.</summary>
        private static int _autoFrame = -999;

        /// <summary>True while a preview is actually forcing state, so <see cref="Tick"/> can unwind
        /// it when the designer closes. NOT part of the alarm state machine (<see cref="Reset"/> must
        /// not touch it — Reset also runs on respawn) — it is an editor-session latch, cleared in
        /// <see cref="Shutdown"/>.</summary>
        private static bool _previewArmed;

        /// <summary>Called by HudEditorWindow every frame the Effects &gt; Alerts sub-tab is on
        /// screen. Auto-arming the preview THERE is the answer to "opening F9 makes the glow
        /// disappear" (FlorpyDorp, 2026-08-10) that does not cost the suppression its reason: an
        /// author editing border colours on any other page still never sees a hue they did not type,
        /// but the one page whose whole job is the alarm shows the alarm.</summary>
        internal static void RequestAutoPreview() { _autoFrame = Time.frameCount; }

        /// <summary>Full reset. Called from HudSystem.Shutdown, from the in-Update stand-down AND
        /// from the !snap.Valid exit — all three, or you return to a world with the previous
        /// session's alarm still latched.</summary>
        internal static void Reset()
        {
            _mode = Mode.Off; _sev = WarnSev.None; _mask = 0u; _lastSeq = 0u;
            _phase = 0f; _cycles = 0; _hue = 0f; _demoteConfirm = 0; _downConfirm = 0;
            _lastReblink = -999f; _lastArm = -999f; _lastHuman = null;
            _fadeFrom = 0f; _fadeHue = 0f; _fadeT = 0f; _alertGlow = 1f;
            _env = 0f; Active = false;
        }

        internal static void Shutdown()
        {
            Reset();
            PreviewMode = 0; _autoFrame = -999; _previewArmed = false;
            WarningSensor.Shutdown();
        }

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
            // Gated on the ARMED LATCH rather than on PreviewMode, because an AUTO preview forces
            // exactly the same state while leaving PreviewMode at 0 — testing the field alone would
            // have let every auto-armed session leak into play through the hole this block exists
            // to plug.
            if (!editorActive && _previewArmed)
            {
                PreviewMode = 0; _autoFrame = -999; _previewArmed = false;
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
            // see a hue they did not type — UNLESS a preview is asked for, which is the only way to
            // tune the alarm while watching it breathe.
            //
            // The request is either EXPLICIT (a level picked on the designer's Alert pulse panel,
            // which then follows the author to every other sub-tab) or AUTO: PreviewMode 0 arms the
            // critical breath for as long as the Effects > Alerts sub-tab is actually on screen. That
            // is what stops "F9 open = no glow at all" without weakening the suppression anywhere the
            // suppression has a reason. A negative PreviewMode is the explicit opt-out, and folds to
            // 0 here so every downstream test stays a simple "level != 0".
            int level = PreviewMode;
            if (level == 0 && editorActive && Time.frameCount - _autoFrame <= 2) level = AutoPreviewLevel;
            if (level < 0) level = 0;
            bool preview = editorActive && level != 0;

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
                // Latch that a preview really did force state this editor session, so the disarm at
                // the top of Tick knows to unwind it however it was armed.
                _previewArmed = true;
                // Force the previewed level. The sensor is not polled at all, so a real alarm cannot
                // fight the preview for control of the hue.
                bool crit = level >= 2;
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

            if (_mode == Mode.FadeOut)
            {
                // Soft stand-down: ease the captured envelope AND the forced-glow floor to nothing,
                // holding the colour that was on screen so leaving an alert is a gentle wash, not a
                // snap. Unscaled time like the rest of the pulse, so it still finishes behind a pause
                // menu. On completion the machine is finally Off, and _hue is zeroed there so a later
                // caution cannot inherit a red first frame.
                float dtf = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                _fadeT += dtf / FadeOutSeconds;
                if (_fadeT >= 1f)
                {
                    _mode = Mode.Off; _env = 0f; _hue = 0f; _alertGlow = 1f; Active = false;
                    return;
                }
                float k = 1f - _fadeT;
                float e = k * k;                    // quadratic ease-out: quick off the peak, gentle into nothing
                _hue = _fadeHue;                    // Tint reads _hue; hold the on-screen colour, do not crossfade
                _env = _fadeFrom * e;
                _alertGlow = e;                     // RAW here; Glow() quantises it per-element (see below)
                Active = true;
                return;
            }

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
            bool previewLoop = preview && level < 2;

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
                    // A caution normally ends AT its trough (the completion test fires on the phase
                    // wrap), so _env is ~0 and BeginFade goes straight to Off — effectively instant.
                    // Under a frame hitch it can end mid-wave and simply fade out instead, which is
                    // fine (a soft tail beats a snap). The fade also bites when a warning is FIXED
                    // mid-flash before the budget is spent.
                    _phase = 0f;
                    BeginFade();
                    if (_mode != Mode.FadeOut) { _env = 0f; Active = false; }
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
            _alertGlow = 1f;   // full forced-glow floor while an alarm actively stands (FadeOut eases it down)
            Active = true;
        }

        private static void Apply(WarnSev sev, uint mask)
        {
            if (sev == WarnSev.None)
            {
                if (++_demoteConfirm < 2) return;      // two agreeing POLLS (0.5 s)
                _sev = WarnSev.None; _mask = 0u; _cycles = 0; _phase = 0f;
                _demoteConfirm = 0;
                BeginFade();       // soft stand-down — snapshots _hue for the fade before we zero it
                // _hue too, or a caution arriving after a cleared critical renders RED for the
                // 0.15s the crossfade takes to walk it back down. (The fade holds its own _fadeHue.)
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
                // Soft stand-down of the red, holding its colour as it eases out (BeginFade snapshots
                // _hue first). _hue and _phase are then cleared like every other Off transition, so a
                // later caution cannot inherit a red first rise and a re-escalation resumes from the
                // trough — the fade renders from its own _fadeHue snapshot, not _hue.
                _sev = WarnSev.Caution; _cycles = BreathCount;
                BeginFade();
                _hue = 0f; _phase = 0f;
                return;
            }
            bool coldStart = _sev == WarnSev.None && now - _lastArm > ArmCooldown;
            bool reblink = newCondition && now - _lastReblink > ReblinkCooldown;
            if (coldStart || reblink)
            {
                _mode = Mode.Burst; _cycles = 0; _phase = 0f;
                // A caution is ALWAYS amber. Force the hue, or a caution arming while a cleared
                // critical is still fading (the FadeOut branch rewrites _hue to the red _fadeHue
                // every frame) would render its first ~0.15s red as MoveTowards walks it back down.
                // Escalation is a different branch, so its crossfade UP to red is unaffected.
                _hue = 0f;
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

        /// <summary>Stand the alarm DOWN with a soft fade instead of an instant snap. Snapshots the
        /// current envelope and hue and eases them to nothing over <see cref="FadeOutSeconds"/>; if the
        /// tint is already dark (a caution ending at its trough), goes straight to Off with no visible
        /// tail. The LOGICAL state (_sev/_mask/_cycles/_phase) is the caller's responsibility — this
        /// governs only the visible tail, so an alarm arriving mid-fade cleanly overrides it (its
        /// Apply branch reassigns _mode away from FadeOut). Idempotent: a repeated stand-down while
        /// already fading does NOT restart the ramp, so a persistent None never stutters the fade.</summary>
        private static void BeginFade()
        {
            if (_mode == Mode.FadeOut) return;      // already fading — never restart the ramp
            if (_env > 0.01f)
            {
                _fadeFrom = _env; _fadeHue = _hue; _fadeT = 0f;
                _mode = Mode.FadeOut;
            }
            else _mode = Mode.Off;
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

        /// <summary>ONE alert level's colour with its brightness gain applied HUE-PRESERVINGLY, plus
        /// the overdrive the display's 0..1 ceiling would otherwise have thrown away
        /// (<paramref name="over"/>, always &gt;= 1).
        ///
        /// THE WHITE-ALERT BUG (FlorpyDorp, 2026-08-10: "red alerts wash out to white"). The gain
        /// used to be applied raw and left deliberately unclamped, on the theory that "a gain above 1
        /// is allowed to blow the hue out toward white". It does not blow out toward white — it
        /// destroys the hue, and it does so WORST in the middle of the breath rather than at its
        /// peak. <see cref="Tint"/> LERPS this colour against the element's own accent and clamps
        /// PER CHANNEL, so a critical red of (1, 0.012, 0) x 3 = (3, 0.035, 0) contributes a
        /// three-times-saturated red while the element's own green and blue survive the mix intact:
        /// against Stationeers Blue's #54D5FE border at the critical breath's own floor
        /// (SettleMix, q = 0.55) the result was (1.00, 0.61, 0.69) — salmon pink — snapping back to
        /// red only at the very peak. And it was not an edge case: every shipped theme carries
        /// AlertCriticalBrightness = 3 (ShippedProfiles.cs:1588 / :3154 / :4771, and the on-disk
        /// copies), where the config DEFAULT is 1, so a profile that predates the key and falls back
        /// to the default (HudTheme.cs:215-220) was the only one seeing a red alarm.
        ///
        /// The fix is a uniform scale, which is hue-exact by construction: multiply by the gain, then
        /// divide the whole triple by its own brightest channel whenever that exceeds 1. A gain BELOW
        /// 1 is untouched (scaling down never clipped) and <paramref name="over"/> stays 1, as it
        /// also does for any picked colour dim enough that the gain never reaches the ceiling
        /// (Zirillian Red's 58007C at 1.181 does not) — those themes keep the exact colour they had.
        ///
        /// The removed factor is not discarded. It is spent on the three things that CAN carry
        /// "brighter" on an LDR canvas without lying about the hue: how far the blend travels toward
        /// the alert colour, how opaque it lands (both <see cref="Tint"/>), and how big a halo it
        /// forces (<see cref="Glow"/>).
        ///
        /// RGB only: the gain must never touch alpha, which carries the hasBorder lift in
        /// <see cref="Tint"/>.</summary>
        private static Color LevelColor(bool critical, out float over)
        {
            Color c;
            float gain;
            if (critical)
            {
                c = HudPalette.AlertCritical != null
                    ? HudPalette.AlertCritical.Value : new Color(1f, 0.290f, 0.239f, 1f);
                gain = HudConfig.FxAlertCriticalBright != null ? HudConfig.FxAlertCriticalBright.Value : 1f;
            }
            else
            {
                c = HudPalette.AlertCaution != null
                    ? HudPalette.AlertCaution.Value : new Color(1f, 0.694f, 0.239f, 1f);
                gain = HudConfig.FxAlertCautionBright != null ? HudConfig.FxAlertCautionBright.Value : 1f;
            }
            c.r *= gain; c.g *= gain; c.b *= gain;
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            over = 1f;
            if (m > 1f)
            {
                float k = 1f / m;
                c.r *= k; c.g *= k; c.b *= k;
                over = m;
            }
            return c;
        }

        /// <summary>The alert hue at this point of the caution-to-critical crossfade, with each
        /// level's own brightness gain applied BEFORE the blend — so the two levels are independently
        /// tunable and a crossfade between them stays continuous. <paramref name="over"/> crossfades
        /// with the hue for the same reason.</summary>
        private static Color AlertColor(float hueQ, out float over)
        {
            float oa, ob;
            Color a = LevelColor(false, out oa);
            Color b = LevelColor(true, out ob);
            over = Mathf.Lerp(oa, ob, hueQ);
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
            float over;
            Color a = AlertColor(Quantise(_hue, seed), out over);
            float bright = 1f + Mathf.Clamp01(
                HudConfig.FxAlertPulseStrength != null ? HudConfig.FxAlertPulseStrength.Value : 0.6f) * q;

            // WHERE THE BRIGHTNESS OVERDRIVE GOES (part 1 of 3): into how far the blend travels,
            // not into the channel values. This is the honest translation of what the old raw gain
            // did BY ACCIDENT — an alert red of (3, 0.035, 0) saturated the red channel at about a
            // third of the breath, so the alarm "dominated" earlier; but only the RED channel did,
            // and the element's own green/blue rode the mix untouched, which is precisely why the
            // result was pink rather than red. Curving the WEIGHT reproduces the intended "the
            // alarm takes over sooner" on all three channels at once, so the hue is the picked hue
            // at every point of the breath.
            //
            // A power curve, not a multiply-and-clamp: q*over would peg the weight at 1 for the
            // whole of a critical breath (its envelope floors at SettleMix = 0.55, so 0.55 * 3 is
            // already past 1) and the alarm would stop visibly breathing at exactly the settings
            // that ask it to shout. q^(1/over) leaves the peak at 1, lifts the trough, and keeps
            // the swing. over == 1 makes it the identity — a default-brightness profile takes the
            // pre-fix path bit for bit.
            float qc = over > 1.0001f ? Mathf.Pow(q, 1f / over) : q;
            Color o = Color.Lerp(c, a, qc);

            // (part 2 of 3) HUE-PRESERVING breath gain, the same rule as LevelColor and for the same
            // reason: the breath's own 1..2 brightness rides on a blend that still carries the
            // element's accent, so a per-channel Clamp01 here washed the mix toward white exactly
            // when the alarm was loudest. Scaling the whole triple by ONE factor keeps the hue and
            // parks it at the brightest value the canvas can show. Where the old expression did not
            // clip (max channel x bright <= 1) this is arithmetically identical; where it DID clip
            // it now desaturates instead of whitening — so this half of the fix bites at EVERY
            // brightness gain, including the default 1.0, and not only above the ceiling.
            float m = Mathf.Max(o.r, Mathf.Max(o.g, o.b)) * bright;
            float gain = m > 1f ? bright / m : bright;
            o.r = Mathf.Clamp01(o.r * gain);
            o.g = Mathf.Clamp01(o.g * gain);
            o.b = Mathf.Clamp01(o.b * gain);
            // Lift alpha over PanelGraphic's hasBorder gate ('bw > 0.05f && BorderColor.a > 0.004f',
            // PanelGraphic.cs:748) so a near-clear border still emits a coloured halo. A zero
            // BorderWidth still cannot — noted on the play-test list.
            // The picked colour's own alpha SCALES that lift, so the pickers' alpha bar means
            // something ("how hard may this alert force a faint border to show?") instead of being a
            // live-looking control with no effect. Both palette defaults are opaque, so this is
            // behaviour-preserving at 1.0.
            // (part 3 of 3) It rides the CURVED weight, so a high brightness gain also makes the
            // alert more solid rather than whiter — and at over == 1 it is the old expression.
            o.a = Mathf.Clamp01(Mathf.Max(c.a, qc * 0.45f * a.a));
            return o;
        }

        /// <summary>Outer-glow floor. FxGlowOn defaults FALSE (HudConfig.cs:532), so on a default
        /// install there would be no halo to tint at all. CONSTANT rather than envelope-scaled so the
        /// skirt and ramp-stop count are decided once at onset instead of on every quantisation step,
        /// and gated on Tier A by the CALLER: Tier A off is a deliberate perf/geometry choice whose
        /// documented "everything 0 = classic 0.8.0 output" invariant must hold. Within an enabled
        /// Tier A the floor DOES override a per-element customGlowOn opt-out — the alarm is a safety
        /// signal, that toggle is a styling preference, and the whole feature sits behind a checkbox.
        /// Never applied to GlowInner (that band sits under the readout text). Scaled by _alertGlow so
        /// the forced halo eases out with the colour on a fade rather than lingering at full intensity
        /// and then snapping off. _alertGlow is 1 while an alarm ACTIVELY stands — so the floor is
        /// CONSTANT then and the "changes only at onset/clear" contract holds — and only ramps during
        /// FadeOut, where it is quantised on the SAME per-element grid as <see cref="Tint"/> so the
        /// skirt re-tessellations spread across frames instead of every panel rebuilding at once.
        /// The floor is additionally scaled by <see cref="GlowOverdrive"/>, which is where a
        /// brightness gain above 1 now goes now that it no longer whitens the hue.
        /// <paramref name="seed"/> is the element's HudDocument.StableSeed(Def.Id).</summary>
        internal static float Glow(float g) { return Glow(g, 0); }

        internal static float Glow(float g, int seed)
        {
            if (!Active) return g;
            // Quantise(1, seed) clamps back to 1 for every seed, so an actively-standing alarm holds a
            // constant floor with no per-element churn; only the fade's sub-1 values vary per element.
            return Mathf.Max(g, GlowFloorAmt * GlowOverdrive() * Quantise(_alertGlow, seed));
        }

        /// <summary>The halo's share of a brightness gain above 1 — the other half of what
        /// <see cref="LevelColor"/>'s hue-preserving clamp took off the colour. "Brighter" on a
        /// display that already reads full red can only mean MORE LIGHT, i.e. a bigger halo and a
        /// more opaque line, so the gain is spent there instead of on a hue nobody asked for.
        /// Taken at <see cref="GlowOverMix"/> strength and capped by the config's own 3.0 ceiling, so
        /// the shipped themes' gain of 3 doubles the forced floor (0.35 -> 0.70) rather than tripling
        /// it into the FxGlow slider's top third.
        ///
        /// Reads the CURRENT SEVERITY's level directly rather than the crossfaded
        /// <see cref="AlertColor"/>: the perf contract on <see cref="Glow"/> is that the floor is a
        /// CONSTANT, decided at onset and clear, because _glow re-tessellates the emitted skirt. A
        /// severity is a step, so the floor stays a step; a hue crossfade would have made it a ramp
        /// and re-meshed every alerted panel for the 0.15s an escalation takes. Palette and gain are
        /// constants during play (they move only under an F9 drag, where a re-mesh is the point).</summary>
        private static float GlowOverdrive()
        {
            float over;
            LevelColor(_sev == WarnSev.Critical, out over);
            return 1f + (Mathf.Min(over, 3f) - 1f) * GlowOverMix;
        }
    }
}
