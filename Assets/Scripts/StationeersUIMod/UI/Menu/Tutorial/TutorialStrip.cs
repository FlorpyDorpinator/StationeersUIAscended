using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// The lesson STRIP (plan A.8): one slim glass line that fades in top-centre with a tiny looping
    /// demo on its left, tells the player what to do, and confirms when they have done it. The game
    /// RUNS under it - practice happens on the live world, because wheels and the Universal
    /// Inventory cannot open on a paused one (plan A.5).
    ///
    /// <para><b>It never takes input.</b> Own ScreenSpaceOverlay canvas at sort 5150 with NO
    /// GraphicRaycaster and a non-blocking CanvasGroup; every graphic is raycastTarget=false. It
    /// never frees the cursor, never listens to a key (not even Esc - its controls live in F10 &gt;
    /// Guide, the plan A.10 precedent of the Universal Inventory), and can never eat a click.</para>
    ///
    /// <para><b>Z-map</b> (verified 2026-09-26 against every sortingOrder in the mod): over the HUD
    /// (3800), hint ring (4999), wheel (5000), search (5005), parked items (5010), Universal
    /// Inventory (5020), pinned windows (5030-5090), tab drag ghost (5100) and the spotlight (5140);
    /// under F10 (5200 - the strip is hidden behind its window), drag ghosts (5250), callouts (5300),
    /// the coach (5600), the Handbook (5700), vanilla's F2 helper-hints panel lifted to 5800
    /// (<c>Core/HelperHintsLift</c>), the F9 ImGui lift (5900) and tooltips (6000). KNOWN
    /// TIE: the Grid's profile-assign popup is also 5150 - it opens inside the Universal Inventory
    /// window, which the strip dodges, so the two never share pixels in practice.</para>
    ///
    /// <para><b>Placement and dodge</b> (2026-09-26: DYNAMIC - the old fixed 170 px covered the
    /// moodlet row on a scaled/curved HUD). Top-centre, 560-760 x 64-96, parked 12 px under the
    /// lowest SHOWN top-zone HUD element over its x-span, measured live from the document views
    /// (<see cref="HudSystem.TryCollectTopZoneRects"/>: forward-warped once, exactly like the F9
    /// handles, the borrowed moodlet row at its rendered cell height), so the tier, profile, theme,
    /// HUD scale, resolution and curvature are all measured, never assumed. Nothing up there = 24 px
    /// under the top edge; never so low that its bottom passes 40% of the screen height (above the
    /// crosshair zone - it overlaps the HUD instead). (a) While a wheel is open it stays put if the
    /// ring + its reserved action-word band (+ a child wheel) clears it, else rises JUST enough - over
    /// the look-only moodlet row / top bar, never the wheel it is teaching. (b) If a window
    /// (Universal Inventory, pinned) or a child wheel that reaches the top edge still covers its
    /// band, it slides to the free run nearest the centre (narrowing to 440 if it must), else up to
    /// the top edge, else it stays (accepting the overlap). Windows are probed with their own hit
    /// tests (<c>TheGridPanel.HitTestWindow</c>, <c>PinnedInventoryWindow.HitTestAny</c>), the wheel
    /// from its published geometry (<c>Overlay.RadialHintContext</c>). Re-measured on show, on every
    /// state flip (wheel, child wheel, windows, tier, curvature mode, resolution) and at 4 Hz while
    /// visible; the strip SLIDES to each new spot (no popping). It stands down (fades out in 0.15 s,
    /// keeps its state, freezes a pending confirm) while a vanilla menu wants the front, while the
    /// F9 designer or F10 is up, while a coach CARD is up, and whenever the world cannot draw.</para>
    ///
    /// <para><b>Motion</b> (unscaled clocks - it works on a paused world too): enter = alpha 0-&gt;1 over
    /// 0.35 s (smooth step) while rising 8 px, the demo following 0.15 s later; a step change fades
    /// the old text out (0.2 s) and the new in (0.35 s); success flashes the border in the theme's
    /// Good colour (0.45 s), grows a DRAWN tick (two panels - never a glyph) over the demo slot and
    /// shows the Then line for its reading time (min 1.4 s); a waiting pulse lights every [Key] in
    /// the text for ~0.6 s.</para>
    ///
    /// <para><b>Looks.</b> The menu skin (<see cref="UiaTheme"/>) plus the F9 global effect stack on
    /// the ANALYTIC panel renderer the HUD boxes use (<see cref="ApplyGlass"/>, shared with the
    /// coach), re-polled against <see cref="UiaMenuTheme.StyleHash"/> - it follows the active UI
    /// Theme with no new knobs (CLAUDE.md rule 8: shared chrome, travels with the theme's menu
    /// family; nothing per-tier, nothing to migrate).</para>
    ///
    /// <para><b>Cost.</b> Zero allocation per frame: text strings (plain + key-lit variants) are
    /// built only when the content changes; Tick only moves, fades and swaps prebuilt references.</para>
    ///
    /// <para><b>Hot reload.</b> <see cref="Shutdown"/> destroys the canvas + demo stage and resets
    /// every static; a pending Confirm callback is dropped, never invoked. No event hooks.</para>
    /// </summary>
    internal static class TutorialStrip
    {
        // ---- layering / placement (reference px at 1080p; the CanvasScaler scales) ----
        private const int SortOrder = 5150;
        private const float TopHome = 170f;          // ONLY while the HUD was never measurable (off / mode C)
        private const float TopClear = 24f;          // nothing of the HUD above the strip's span
        private const float TopEdge = 10f;           // the ceiling, and the compact chip's home
        private const float ZoneGap = 12f;           // clearance under the HUD's top zone
        private const float ZoneFrac = 0.40f;        // "top zone" = an element whose centre is in the upper 40%
        private const float MaxBottomFrac = 0.40f;   // the strip's bottom never passes 40% of the screen height
        private const float SideSlack = 8f;          // an element this close to the strip's ends still counts
        private const float WheelGap = 12f;          // clearance above an open wheel's occupied extent
        private const float TopDeadband = 1.5f;      // re-measure noise below this never retargets
        private const float MinW = 560f, MaxW = 760f, DemoBaseW = 640f;
        private const float MinDodgeW = 440f;
        private const float MinH = 64f, MaxH = 96f, CompactH = 34f;
        private const float Pad = 10f;
        private const float DemoW = 150f, DemoH = 76f, DemoGap = 12f;
        private const float TickLane = 30f;          // right-hand room for the tick when there is no demo
        private const float HeaderH = 16f;
        private const float DrainH = 2f;
        private const float EdgeMargin = 16f;
        private const float DodgeGap = 16f;
        private const float DodgeStep = 16f;
        private const float Corner = 8f;

        // ---- timings (unscaled seconds) ----
        private const float EnterDur = 0.35f, EnterRise = 8f, DemoDelay = 0.15f;
        private const float TextOutDur = 0.2f, TextInDur = 0.35f;
        private const float HideDur = 0.25f;
        private const float FlashDur = 0.45f;
        private const float ThenMin = 1.4f;
        private const float PulseDur = 0.9f, PulseLitFrom = 0.1f, PulseLitTo = 0.7f;
        private const float StandDownRate = 7f;
        private const float DodgeInterval = 0.25f;
        private const float RestyleInterval = 0.14f;
        private const float SlideRate = 10f;
        private const float ChromeDemoAlpha = 0.35f;

        private enum Vis : byte { Hidden, Entering, Visible, Hiding }
        private enum Kind : byte { Body, Chrome, Then }

        private static readonly Func<string, string> GlyphFn = TutorialTokens.Glyph;
        private static readonly StringBuilder _sb = new StringBuilder(256);
        private static readonly bool[] _occ = new bool[512];

        // ---- built objects ----
        private static GameObject _root;
        private static Canvas _canvas;
        private static CanvasGroup _rootGroup;
        private static RectTransform _strip;
        private static PanelGraphic _panel;
        private static RectTransform _panelRt;
        private static RectTransform _flashRt;
        private static PanelGraphic _flash;
        private static CanvasGroup _flashGroup;
        private static RectTransform _demoSlot;
        private static CanvasGroup _demoGroup;
        private static RectTransform _demoHost;
        private static TutorialDemoStage _demo;
        private static RectTransform _tickRoot;
        private static CanvasGroup _tickGroup;
        private static RectTransform _tickShort, _tickLong;
        private static RectTransform _textCol;
        private static CanvasGroup _textGroup;
        private static TextMeshProUGUI _headerTmp;
        private static TextMeshProUGUI _bodyTmp;
        private static RectTransform _bodyRt;
        private static RectTransform _drainRt;
        private static Image _drainImg;
        private static int _builtThemeHash;
        private static string _litHex = "FFA040";

        // ---- logical content ----
        private static bool _shown;
        private static string _stepId, _header, _body, _demoId;
        private static bool _demoKnown;
        private static string _bodyPlain, _bodyLit;
        private static string _chrome, _chromePlain, _chromeLit;
        private static bool _chromeCompact;
        private static string _then, _thenPlain, _thenLit;
        private static int _rev, _appliedRev = -1;

        // ---- applied presentation ----
        private static Kind _appliedKind;
        private static bool _appliedCompact;
        private static bool _demoOn;
        private static string _demoShownId;
        private static bool _shownLit;
        private static float _prefW = MinW, _w = MinW, _h = MaxH, _capW = MaxW;

        // ---- animation state ----
        private static Vis _vis = Vis.Hidden;
        private static float _alphaVis;          // 0..1 from the enter / hide curve
        private static float _enterT0, _hideT0, _hideFrom;
        private static float _standMul = 1f;     // stand-down multiplier (vanilla menu / F9 / no world)
        private static int _txPhase;             // 0 steady, 1 fading out, 2 fading in
        private static float _txT0;
        private static bool _confirming;
        private static float _confirmT0 = -100f, _confirmDur;
        private static Action _onDone;
        private static bool _tickOn;
        private static float _pulseT0 = -1f;
        private static float _drainFrac = -1f;

        // ---- placement state ----
        private static float _curX, _curTop = TopHome, _tgtX, _tgtTop = TopHome;
        private static Vector2 _lastPos = new Vector2(float.NaN, float.NaN);
        private static float _nextDodge;
        private static bool _lastWheel, _lastGrid, _lastSat;
        private static int _lastPins = -1, _lastScreenW, _lastScreenH, _lastHudKey = -1;
        // The HUD's top-zone rects (screen px, y up) of the last measurement, and whether it measured.
        private static readonly List<Rect> _zone = new List<Rect>(64);
        private static bool _zoneKnown;
        private static float _heldBase = -1f;    // last MEASURED home (ref px); -1 = never measured
        // The open wheel's discs (ref px, centre origin, y up): the ring + its action-word band + WheelGap.
        private static readonly float[] _wcx = new float[2], _wcy = new float[2], _wr = new float[2];
        private static int _wheelN;
        private static float _nextRestyle;
        private static float _nextBuildTry;      // a failed build retries at most every 5 s

        // ================= API =================

        /// <summary>True between <see cref="Show"/> and <see cref="Hide"/> (even while stood down
        /// under a vanilla menu - the lesson is still "up").</summary>
        internal static bool IsVisible { get { return _shown; } }

        /// <summary>The step on the strip, or null when hidden.</summary>
        internal static string CurrentStepId { get { return _shown ? _stepId : null; } }

        /// <summary>Show (or update) the strip. Idempotent: identical arguments are a no-op, so it
        /// is safe to call every frame. A DIFFERENT step id clears the chrome line, the drain line,
        /// the waiting pulse and any pending <see cref="Confirm"/> (its callback is dropped); the
        /// same step with new text just cross-fades. From hidden it plays the enter animation.
        /// <paramref name="demoId"/> null / unknown = no demo slot (the strip narrows).</summary>
        internal static void Show(string stepId, string header, string bodyResolved, string demoId)
        {
            header = header ?? "";
            bodyResolved = bodyResolved ?? "";
            bool sameStep = _shown && string.Equals(stepId, _stepId, StringComparison.Ordinal);
            if (sameStep && string.Equals(header, _header, StringComparison.Ordinal)
                && string.Equals(bodyResolved, _body, StringComparison.Ordinal)
                && string.Equals(demoId, _demoId, StringComparison.Ordinal))
                return;
            if (!EnsureBuilt()) return;

            if (!sameStep)
            {
                ClearChromeState();
                CancelConfirm();
                _pulseT0 = -1f;
                _drainFrac = -1f;
                ApplyDrain();
            }
            _stepId = stepId;
            _header = header;
            _body = bodyResolved;
            _demoId = demoId;
            _bodyPlain = BuildVariant(_body, false);
            _bodyLit = BuildVariant(_body, true);
            _demoKnown = _demo != null && !string.IsNullOrEmpty(demoId) && KnownDemo(demoId);
            _rev++;

            if (!_shown || _vis == Vis.Hidden || _vis == Vis.Hiding)
            {
                bool fromZero = _vis == Vis.Hidden || _alphaVis <= 0.001f;
                float resume = fromZero ? 0f : _alphaVis;
                _shown = true;
                if (!_root.activeSelf) _root.SetActive(true);
                // No text cross-fade on entry: the whole strip fades in with the new content.
                _txPhase = 0;
                ApplyContentNow();
                _vis = Vis.Entering;
                _enterT0 = Time.unscaledTime - resume * EnterDur;
                if (fromZero)
                {
                    EvaluateDodge();
                    _curX = _tgtX;
                    _curTop = _tgtTop;
                    ApplyPosition(0f, true);
                }
            }
            // Visible: Tick sees _rev and cross-fades.
        }

        /// <summary>The Lesson Editor's live-typing path (<c>TutorialDirector.RefreshActiveText</c>).
        /// When <paramref name="stepId"/> is the step on the strip, the header / body text is swapped
        /// IN PLACE: no cross-fade, no demo restart, and no confirm / chrome / drain / pulse reset (the
        /// strip never owns the spotlight - the caller simply does not re-apply it) - the strip only
        /// re-fits to the new text. Any other step, or a strip that is not showing, behaves exactly
        /// like <see cref="Show"/>(stepId, header, bodyResolved, &lt;the current demo id&gt;). Allocates
        /// only the new display strings (plain + key-lit) that TMP is handed.</summary>
        internal static void UpdateText(string stepId, string header, string bodyResolved)
        {
            header = header ?? "";
            bodyResolved = bodyResolved ?? "";
            if (!_shown || _root == null || _vis == Vis.Hidden || _vis == Vis.Hiding
                || !string.Equals(stepId, _stepId, StringComparison.Ordinal))
            {
                Show(stepId, header, bodyResolved, _demoId);
                return;
            }
            bool headerChanged = !string.Equals(header, _header, StringComparison.Ordinal);
            bool bodyChanged = !string.Equals(bodyResolved, _body, StringComparison.Ordinal);
            if (!headerChanged && !bodyChanged) return;
            _header = header;
            if (bodyChanged)
            {
                _body = bodyResolved;
                _bodyPlain = BuildVariant(_body, false);
                _bodyLit = BuildVariant(_body, true);
            }
            // Mid cross-fade, OLD text still fading out: ApplyContentNow lands the new content when
            // the fade-out ends. (A pending _rev change re-applies everything anyway.)
            if (_txPhase == 1) return;
            if (headerChanged && _headerTmp != null) _headerTmp.text = _header;
            // A chrome / Then line keeps the text slot; the new body shows when it clears.
            if (!bodyChanged || _appliedKind != Kind.Body) return;
            if (_bodyTmp != null) _bodyTmp.text = CurrentVariant(_shownLit) ?? "";   // mid-pulse: the lit variant
            Measure(_bodyPlain ?? "");
            Relayout();
            _nextDodge = 0f;   // a new width / height re-probes the dodge band
        }

        /// <summary>The player did it: flash the border Good, grow the drawn tick, show
        /// <paramref name="thenResolved"/> (null/empty keeps the current text) for at least
        /// <paramref name="minSeconds"/> (never under 1.4 s; 0 = a reading time from its length),
        /// then call <paramref name="onDone"/> once. The clock freezes while the strip is stood down
        /// (vanilla menu, F9), so the player always gets to see it. A later Confirm, Hide, or a Show
        /// of a different step cancels it (the callback is dropped, not invoked).</summary>
        internal static void Confirm(string thenResolved, float minSeconds, Action onDone)
        {
            _confirming = true;
            _confirmT0 = Time.unscaledTime;
            _then = string.IsNullOrEmpty(thenResolved) ? null : thenResolved;
            _thenPlain = _then != null ? BuildVariant(_then, false) : null;
            _thenLit = _then != null ? BuildVariant(_then, true) : null;
            float read = _then != null ? Mathf.Clamp(1.2f + 0.05f * _then.Length, ThenMin, 6f) : ThenMin;
            _confirmDur = minSeconds > 0f ? Mathf.Max(ThenMin, minSeconds) : read;
            _onDone = onDone;
            _pulseT0 = -1f;
            _drainFrac = -1f;
            ApplyDrain();
            _tickOn = true;
            if (_tickShort != null) _tickShort.localScale = new Vector3(0f, 1f, 1f);
            if (_tickLong != null) _tickLong.localScale = new Vector3(0f, 1f, 1f);
            if (_then != null) _rev++;
        }

        /// <summary>Swap the text for a chrome line (C.19: on hold, "tap [MMB] to open it again",
        /// lessons off, the 1.8 overrun...). Null restores the step text. <paramref name="compact"/>
        /// (Presentation extra, for the "Lesson waiting" chip): hide header + demo and shrink to a
        /// slim chip at the top edge.</summary>
        internal static void SetChrome(string lineResolved, bool compact = false)
        {
            if (string.IsNullOrEmpty(lineResolved)) lineResolved = null;
            if (lineResolved == null) compact = false;
            if (string.Equals(lineResolved, _chrome, StringComparison.Ordinal) && compact == _chromeCompact) return;
            _chrome = lineResolved;
            _chromeCompact = compact;
            _chromePlain = _chrome != null ? BuildVariant(_chrome, false) : null;
            _chromeLit = _chrome != null ? BuildVariant(_chrome, true) : null;
            _rev++;
            _nextDodge = 0f;
        }

        /// <summary>The S2 timed-read drain line under the text: 1 = full, 0 = empty; &lt; 0 hides
        /// it. Cheap enough to call every frame (only changed values are written).</summary>
        internal static void SetDrain(float fraction01)
        {
            float f = fraction01 < 0f ? -1f : Mathf.Clamp01(fraction01);
            if (Mathf.Abs(f - _drainFrac) < 0.0005f) return;
            _drainFrac = f;
            ApplyDrain();
        }

        /// <summary>One waiting pulse: every [Key] in the text lights in the active-hand accent for
        /// ~0.6 s. The Director calls it every 4 s after 20 s without progress.</summary>
        internal static void PulseKeys()
        {
            if (!_shown) return;
            _pulseT0 = Time.unscaledTime;
        }

        /// <summary>Hide the strip (fade 0.25 s, or at once). Drops any pending Confirm callback,
        /// the chrome line and the drain line.</summary>
        internal static void Hide(bool fade = true)
        {
            _shown = false;
            CancelConfirm();
            ClearChromeState();
            _pulseT0 = -1f;
            _drainFrac = -1f;
            ApplyDrain();
            if (_root == null) { _vis = Vis.Hidden; _alphaVis = 0f; return; }
            if (!fade || _alphaVis <= 0.001f || _standMul <= 0.001f || _vis == Vis.Hidden)
            {
                HideNow();
                return;
            }
            if (_vis != Vis.Hiding)
            {
                _vis = Vis.Hiding;
                _hideT0 = Time.unscaledTime;
                _hideFrom = _alphaVis;
            }
        }

        /// <summary>Per-frame pump (StationeersUIMod.Update, after the Control Center). Zero
        /// allocation.</summary>
        internal static void Tick()
        {
            // Idle (nothing showing or fading, no confirm clock to run): out BEFORE the stand-down
            // probes below - they include a native InventoryManager.InGameMenuOpen call - so an idle
            // strip costs one branch per frame.
            if (!_confirming && (_root == null || _vis == Vis.Hidden)) return;

            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            bool worldOk = CanDrawSafe();
            // Stand down under a vanilla menu, the F9 designer, F10 (its window covers the strip's
            // spot anyway - this also freezes a pending confirm so it is seen on return) and a coach
            // CARD (its scrim would only dim us - the card has the player's attention).
            bool standDown = !worldOk || VanillaFront() || DesignerUp() || MenuUp() || CardUp();

            // 1. The confirm clock. Frozen while stood down; runs while hidden (the Director's
            //    flow must still complete). The callback may re-enter Show / Hide / Confirm.
            if (_confirming)
            {
                if (standDown && _shown) _confirmT0 += dt;
                else if (now - _confirmT0 >= _confirmDur)
                {
                    var cb = _onDone;
                    _onDone = null;
                    _confirming = false;
                    if (cb != null)
                    {
                        try { cb(); }
                        catch (Exception e) { UIALog.Warn("TutorialStrip: confirm callback failed: " + e.Message); }
                    }
                    now = Time.unscaledTime;
                }
            }

            if (_root == null || _vis == Vis.Hidden) return;

            // 2. Stand-down multiplier (0.15 s fade; instant when the world is gone).
            _standMul = !worldOk ? 0f : Mathf.MoveTowards(_standMul, standDown ? 0f : 1f, StandDownRate * dt);

            // 3. Enter / hide curve.
            float enterK = 1f;
            switch (_vis)
            {
                case Vis.Entering:
                {
                    float t = (now - _enterT0) / EnterDur;
                    enterK = Ease01(t);
                    _alphaVis = enterK;
                    if (t >= 1f) { _vis = Vis.Visible; _alphaVis = 1f; enterK = 1f; }
                    break;
                }
                case Vis.Hiding:
                {
                    float t = (now - _hideT0) / HideDur;
                    _alphaVis = _hideFrom * (1f - Ease01(t));
                    if (t >= 1f) { HideNow(); return; }
                    break;
                }
                default:
                    _alphaVis = 1f;
                    break;
            }
            float alpha = _alphaVis * _standMul;
            SetAlpha(_rootGroup, alpha);
            // The glass stops DRAWING at zero, not just fades: on the analytic path its border and
            // glow ride uv2/uv3, which a CanvasGroup alpha never reaches (Unity delivers group alpha
            // in the vertex colour only) - a stood-down strip would otherwise leave its frame over a
            // vanilla menu, the designer or a coach card. Runs BEFORE the stood-down early-out below.
            if (_panel != null && _panel.enabled != (alpha > 0.001f)) _panel.enabled = alpha > 0.001f;

            // 4. Theme follow (throttled; rebuilds the canvas, keeps every piece of state).
            if (now >= _nextRestyle)
            {
                _nextRestyle = now + RestyleInterval;
                if (UiaMenuTheme.StyleHash() != _builtThemeHash) { Rebuild(); return; }
            }

            // 5. Text transition (cross-fade on content change; instant while invisible).
            float txA = 1f;
            if (_txPhase == 0 && _rev != _appliedRev)
            {
                if (alpha <= 0.01f) ApplyContentNow();
                else { _txPhase = 1; _txT0 = now; }
            }
            if (_txPhase == 1)
            {
                float t = (now - _txT0) / TextOutDur;
                txA = 1f - Ease01(t);
                if (t >= 1f) { ApplyContentNow(); _txPhase = 2; _txT0 = now; txA = 0f; }
            }
            else if (_txPhase == 2)
            {
                float t = (now - _txT0) / TextInDur;
                txA = Ease01(t);
                if (t >= 1f) { _txPhase = 0; txA = 1f; }
            }
            SetAlpha(_textGroup, txA);

            if (alpha <= 0.001f) return;   // fully stood down: nothing else is visible

            // 6. Placement (4 Hz, or at once on a state flip) + slide toward the target.
            bool wheel = WheelOpen();
            bool grid = GridOpen();
            int pins = PinCount();
            bool sat = wheel && SatShown();
            bool screenFlip = Screen.width != _lastScreenW || Screen.height != _lastScreenH;
            bool hudFlip = HudKey() != _lastHudKey;
            if (now >= _nextDodge || wheel != _lastWheel || grid != _lastGrid || pins != _lastPins
                || sat != _lastSat || screenFlip || hudFlip)
            {
                // A resolution hop or a tier / curvature flip measures again 0.1 s later: the
                // CanvasScaler and the HUD's own relayout may land a frame after the flip.
                _nextDodge = now + (screenFlip || hudFlip ? 0.1f : DodgeInterval);
                EvaluateDodge();
            }
            float k = 1f - Mathf.Exp(-SlideRate * dt);
            _curX = Mathf.Abs(_tgtX - _curX) < 0.25f ? _tgtX : _curX + (_tgtX - _curX) * k;
            _curTop = Mathf.Abs(_tgtTop - _curTop) < 0.25f ? _tgtTop : _curTop + (_tgtTop - _curTop) * k;
            float rise = _vis == Vis.Entering ? (1f - enterK) * EnterRise : 0f;
            ApplyPosition(rise, false);

            // 7. Waiting pulse: swap to the key-lit variant for the middle of the pulse (two text
            //    swaps per pulse, both prebuilt references).
            bool lit = false;
            if (_pulseT0 >= 0f)
            {
                float pt = now - _pulseT0;
                if (pt >= PulseDur) _pulseT0 = -1f;
                else lit = pt >= PulseLitFrom && pt <= PulseLitTo;
            }
            if (_txPhase == 0 && lit != _shownLit)
            {
                _shownLit = lit;
                string s = CurrentVariant(lit);
                if (_bodyTmp != null && s != null) _bodyTmp.text = s;
            }

            // 8. Success flash + drawn tick.
            float ct = now - _confirmT0;
            SetAlpha(_flashGroup, ct >= 0f && ct <= FlashDur ? Mathf.Sin(ct / FlashDur * Mathf.PI) : 0f);
            float tickA = 0f;
            if (_tickOn)
            {
                tickA = _txPhase == 1 ? txA : 1f;
                SetScaleX(_tickShort, Ease01((ct - 0.05f) / 0.15f));
                SetScaleX(_tickLong, Ease01((ct - 0.20f) / 0.22f));
            }
            SetAlpha(_tickGroup, tickA);

            // 9. Demo: follows the enter by 0.15 s, fades with the text, gives way to the tick,
            //    dims under a chrome line.
            if (_demoOn)
            {
                float d = txA;
                if (_vis == Vis.Entering) d *= Ease01((now - _enterT0 - DemoDelay) / EnterDur);
                if (_tickOn) d *= 1f - Ease01(ct / 0.2f);
                if (_appliedKind == Kind.Chrome) d *= ChromeDemoAlpha;
                SetAlpha(_demoGroup, d);
                if (_demo != null && d > 0.001f)
                {
                    try { _demo.Tick(); }
                    catch (Exception e)
                    {
                        UIALog.Warn("TutorialStrip: demo tick failed: " + e.Message);
                        try { _demo.Destroy(); } catch { }
                        _demo = null;
                        _demoOn = false;
                        if (_demoSlot != null) _demoSlot.gameObject.SetActive(false);
                    }
                }
            }

            // 10. The glass: theme fill/border + the F9 global effect stack (dirty-guarded setters).
            StylePanel(alpha);
        }

        /// <summary>Hot-reload / plugin teardown: destroy the canvas and demo, reset every static.
        /// A pending Confirm callback is dropped, never invoked into a dying assembly.</summary>
        internal static void Shutdown()
        {
            _onDone = null;
            _confirming = false;
            DestroyRoot();
            _shown = false;
            _stepId = _header = _body = _demoId = null;
            _demoKnown = false;
            _bodyPlain = _bodyLit = null;
            _chrome = _chromePlain = _chromeLit = null;
            _chromeCompact = false;
            _then = _thenPlain = _thenLit = null;
            _rev = 0;
            _appliedRev = -1;
            _appliedKind = Kind.Body;
            _appliedCompact = false;
            _demoOn = false;
            _demoShownId = null;
            _shownLit = false;
            _prefW = MinW; _w = MinW; _h = MaxH; _capW = MaxW;
            _vis = Vis.Hidden;
            _alphaVis = 0f;
            _enterT0 = _hideT0 = _hideFrom = 0f;
            _standMul = 1f;
            _txPhase = 0;
            _txT0 = 0f;
            _confirmT0 = -100f;
            _confirmDur = 0f;
            _tickOn = false;
            _pulseT0 = -1f;
            _drainFrac = -1f;
            _curX = 0f; _curTop = TopHome; _tgtX = 0f; _tgtTop = TopHome;
            _lastPos = new Vector2(float.NaN, float.NaN);
            _nextDodge = 0f;
            _lastWheel = _lastGrid = _lastSat = false;
            _lastPins = -1;
            _lastHudKey = -1;
            _lastScreenW = _lastScreenH = 0;
            _zone.Clear();
            _zoneKnown = false;
            _heldBase = -1f;
            _wheelN = 0;
            _nextRestyle = 0f;
            _nextBuildTry = 0f;
            _builtThemeHash = 0;
            _litHex = "FFA040";
            _sb.Length = 0;
        }

        // ================= content =================

        private static void ClearChromeState()
        {
            _chrome = _chromePlain = _chromeLit = null;
            _chromeCompact = false;
        }

        private static void CancelConfirm()
        {
            _confirming = false;
            _onDone = null;
            _then = _thenPlain = _thenLit = null;
        }

        private static Kind WantKind()
        {
            if (_confirming && _then != null) return Kind.Then;
            if (_chrome != null) return Kind.Chrome;
            return Kind.Body;
        }

        private static string CurrentVariant(bool lit)
        {
            switch (_appliedKind)
            {
                case Kind.Then: return lit ? _thenLit : _thenPlain;
                case Kind.Chrome: return lit ? _chromeLit : _chromePlain;
                default: return lit ? _bodyLit : _bodyPlain;
            }
        }

        /// <summary>Push the current logical content into the widgets and re-lay the strip. Runs
        /// only on a content change (or a theme rebuild) - never per frame.</summary>
        private static void ApplyContentNow()
        {
            _appliedRev = _rev;
            if (_root == null) return;
            var kind = WantKind();
            // A Then line survives the confirm's end (the Director moves on with Show / Hide);
            // anything else retires the tick.
            if (kind != Kind.Then && !_confirming) _tickOn = false;
            _appliedKind = kind;
            _appliedCompact = kind == Kind.Chrome && _chromeCompact;
            _shownLit = false;

            _demoOn = !_appliedCompact && _demoKnown && _demo != null;
            if (_demoOn && !string.Equals(_demoShownId, _demoId, StringComparison.Ordinal))
            {
                try { _demo.Show(_demoId, GlyphFn, true); _demoShownId = _demoId; }
                catch (Exception e)
                {
                    UIALog.Warn("TutorialStrip: demo '" + _demoId + "' failed to show: " + e.Message);
                    _demoOn = false;
                }
            }

            if (_headerTmp != null)
            {
                _headerTmp.gameObject.SetActive(!_appliedCompact);
                if (!string.Equals(_headerTmp.text, _header, StringComparison.Ordinal)) _headerTmp.text = _header ?? "";
            }
            string text = CurrentVariant(false) ?? "";
            if (_bodyTmp != null) _bodyTmp.text = text;

            Measure(text);
            Relayout();
            _nextDodge = 0f;
        }

        /// <summary>Preferred width from the text length (560-760), height from the wrapped text
        /// (64-96; a demo always takes the full 96), the compact chip 34.</summary>
        private static void Measure(string text)
        {
            int len = VisibleLength(_appliedKind == Kind.Then ? _then : (_appliedKind == Kind.Chrome ? _chrome : _body));
            if (_appliedCompact)
            {
                _prefW = Mathf.Clamp(260f + len * 7.2f, 300f, MaxW);
                _h = CompactH;
                return;
            }
            float baseW = _demoOn ? DemoBaseW : MinW;
            _prefW = Mathf.Clamp(baseW + Mathf.Max(0, len - 70) * 3.2f, MinW, MaxW);
            if (_demoOn) { _h = MaxH; return; }
            float colW = Mathf.Min(_prefW, _capW) - Pad * 2f - TickLane;
            float bodyH = 20f;
            try
            {
                if (_bodyTmp != null) bodyH = _bodyTmp.GetPreferredValues(text, Mathf.Max(60f, colW), 0f).y;
            }
            catch { bodyH = 40f; }
            _h = Mathf.Clamp(Pad * 2f + HeaderH + 2f + bodyH + DrainH + 4f, MinH, MaxH);
        }

        private static int VisibleLength(string s) { return string.IsNullOrEmpty(s) ? 0 : s.Length; }

        /// <summary>Lay every child out for the applied width/height/demo/compact state. Manual
        /// anchoring (no layout groups): deterministic and rebuild-free.</summary>
        private static void Relayout()
        {
            if (_strip == null) return;
            _w = Mathf.Min(_prefW, Mathf.Max(MinDodgeW, _capW));
            if (_appliedCompact) _w = Mathf.Min(_prefW, _capW);
            _strip.sizeDelta = new Vector2(_w, _h);

            if (_panel != null) _panel.SetShape(_w, _h, Corner);
            if (_panelRt != null) _panelRt.sizeDelta = new Vector2(_w, _h);
            if (_flashRt != null) _flashRt.sizeDelta = new Vector2(_w + 2f, _h + 2f);
            if (_flash != null) _flash.SetShape(_w + 2f, _h + 2f, Corner + 1f);

            if (_demoSlot != null)
            {
                bool slot = _demoOn;
                if (_demoSlot.gameObject.activeSelf != slot) _demoSlot.gameObject.SetActive(slot);
                _demoSlot.anchoredPosition = new Vector2(Pad + DemoW * 0.5f, 0f);
            }
            // The tick: over the demo slot when there is one, else in the right-hand lane.
            if (_tickRoot != null)
            {
                if (_demoOn)
                {
                    _tickRoot.anchorMin = _tickRoot.anchorMax = new Vector2(0f, 0.5f);
                    _tickRoot.anchoredPosition = new Vector2(Pad + DemoW * 0.5f, 0f);
                    _tickRoot.localScale = Vector3.one;
                }
                else
                {
                    _tickRoot.anchorMin = _tickRoot.anchorMax = new Vector2(1f, 0.5f);
                    _tickRoot.anchoredPosition = new Vector2(-(Pad + TickLane * 0.5f), 0f);
                    _tickRoot.localScale = new Vector3(0.62f, 0.62f, 1f);
                }
            }

            if (_textCol != null)
            {
                float left = Pad + (_demoOn ? DemoW + DemoGap : 0f);
                float right = Pad + (_demoOn || _appliedCompact ? 0f : TickLane);
                float top = _appliedCompact ? 4f : Pad - 2f;
                float bottom = _appliedCompact ? 4f : Pad - 4f;
                _textCol.offsetMin = new Vector2(left, bottom);
                _textCol.offsetMax = new Vector2(-right, -top);
            }
            if (_bodyRt != null)
            {
                _bodyRt.offsetMin = new Vector2(0f, DrainH + 3f);
                _bodyRt.offsetMax = new Vector2(0f, _appliedCompact ? 0f : -(HeaderH + 2f));
            }
            if (_bodyTmp != null)
            {
                _bodyTmp.alignment = _appliedCompact ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
                _bodyTmp.enableWordWrapping = !_appliedCompact;
            }
            ApplyDrain();
        }

        /// <summary>Build a display string for TMP. Every literal run is wrapped in &lt;noparse&gt; so
        /// a '&lt;' in (dev-edited) copy stays text; with <paramref name="lit"/> every [Key] span is
        /// coloured in the active-hand accent (the waiting pulse). Runs on content change only.</summary>
        private static string BuildVariant(string s, bool lit)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // A literal closing tag in copy would break out of the noparse wrapper: neutralise it.
            if (s.IndexOf("noparse", StringComparison.OrdinalIgnoreCase) >= 0) s = NeutraliseNoparse(s);
            var sb = _sb;
            sb.Length = 0;
            int i = 0;
            while (i < s.Length)
            {
                int open = s.IndexOf('[', i);
                if (open < 0) { AppendLiteral(sb, s, i, s.Length - i); break; }
                int close = s.IndexOf(']', open + 1);
                if (close < 0) { AppendLiteral(sb, s, i, s.Length - i); break; }
                if (close - open > 24)
                {
                    // Too long for a key glyph: the '[' is just text - keep scanning after it.
                    AppendLiteral(sb, s, i, open + 1 - i);
                    i = open + 1;
                    continue;
                }
                AppendLiteral(sb, s, i, open - i);
                if (lit) sb.Append("<color=#").Append(_litHex).Append('>');
                AppendLiteral(sb, s, open, close - open + 1);
                if (lit) sb.Append("</color>");
                i = close + 1;
            }
            string result = sb.ToString();
            sb.Length = 0;
            return result;
        }

        private static void AppendLiteral(StringBuilder sb, string s, int start, int len)
        {
            if (len <= 0) return;
            sb.Append("<noparse>").Append(s, start, len).Append("</noparse>");
        }

        /// <summary>"noparse" in any case -&gt; "no parse" (copy can then never close our wrapper).
        /// Rare path - allocates its own builder.</summary>
        private static string NeutraliseNoparse(string s)
        {
            var b = new StringBuilder(s.Length + 8);
            int i = 0;
            while (i < s.Length)
            {
                int at = s.IndexOf("noparse", i, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { b.Append(s, i, s.Length - i); break; }
                b.Append(s, i, at - i).Append(s, at, 2).Append(' ').Append(s, at + 2, 5);
                i = at + 7;
            }
            return b.ToString();
        }

        private static bool KnownDemo(string id)
        {
            try { return TutorialDemoStage.IsKnownDemo(id); }
            catch { return false; }
        }

        // ================= placement =================

        /// <summary>Compute the target position (and a width cap). Runs on show, on every state flip
        /// and at 4 Hz while visible - never per frame. Allocation-free: the HUD's top-zone rects
        /// land in one static list, the wheel in two fixed discs, the occupancy scan in one static
        /// bool array. Reference px at 1080p (the strip canvas's own units); the rules, in order:
        /// <list type="number">
        /// <item>HOME: <see cref="ZoneGap"/> under the lowest SHOWN top-zone HUD element over the
        /// strip's x-span (<see cref="Baseline"/>): measured, so the bare tier's missing top bar, a
        /// minimal custom HUD, another profile / theme / HUD scale / resolution / curvature all land
        /// right. Nothing up there = <see cref="TopClear"/>. Clamped so the strip's bottom stays above
        /// <see cref="MaxBottomFrac"/> of the screen - a HUD reaching lower than that is overlapped
        /// rather than pushing the strip onto the crosshair zone. HUD not measurable right now = the
        /// last measured home (else the legacy 170).</item>
        /// <item>WHEEL: if the open ring + its reserved action-word band (+ a child wheel) reaches
        /// the strip, rise JUST enough to clear it, but never above <see cref="TopEdge"/>. Least harm
        /// by design: it overlaps the look-only moodlet row / top bar, never the wheel, and stays
        /// centred right above the wheel the lesson is teaching (a sideways slide would pull the
        /// player's eyes off it).</item>
        /// <item>STILL BLOCKED (a Universal Inventory / pinned window over the band, or a child
        /// wheel reaching the top edge): slide to the free run nearest the centre, narrowing to
        /// <see cref="MinDodgeW"/> if it must, and drop under a lower side HUD element when that
        /// band is free too; else ride the top edge if that is clear; else stay and accept the
        /// overlap.</item>
        /// </list></summary>
        private static void EvaluateDodge()
        {
            _lastWheel = WheelOpen();
            _lastGrid = GridOpen();
            _lastPins = PinCount();
            _lastSat = _lastWheel && SatShown();
            _lastHudKey = HudKey();
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            _wheelN = 0;

            float sf = ScaleFactor();
            float cw = Screen.width / sf, ch = Screen.height / sf;
            float x = 0f;
            float cap = MaxW;
            float top = TopEdge;          // the compact chip rides the top edge, over the look-only top bar
            bool estimated = false;

            if (!_appliedCompact)
            {
                float wantW = _prefW;
                float half = wantW * 0.5f;

                // 1. Home: under whatever the HUD actually draws up there.
                _zoneKnown = MeasureZone();
                top = Baseline(0f, wantW, sf, ch);
                if (_zoneKnown) _heldBase = top;

                // 2. An open wheel: stay if it clears the strip, else rise just enough. Measured over
                //    the span the band probe below actually covers (every column is widened by half
                //    a step), less half a px, so a strip that has risen clear never reads as blocked
                //    at the tangent and slides for nothing.
                if (_lastWheel && LoadWheel(sf, out estimated))
                {
                    float wTop = WheelTopOver(-half - DodgeStep * 0.5f, half + DodgeStep * 0.5f, ch) - 0.5f;
                    if (top + _h > wTop) top = Mathf.Max(TopEdge, wTop - _h);
                }

                // 3. A window, or a child wheel reaching the top edge, still in the band: slide.
                if (_wheelN > 0 || _lastGrid || _lastPins > 0)
                {
                    float yTop = ch * 0.5f - top;
                    float yBot = yTop - _h;
                    if (Blocked(-half, half, yTop, yBot, sf))
                    {
                        bool found = false;
                        int n = Mathf.Min(_occ.Length, Mathf.CeilToInt(cw / DodgeStep));
                        float x0 = -cw * 0.5f;
                        for (int c = 0; c < n; c++)
                            _occ[c] = HitBand(x0 + (c + 0.5f) * DodgeStep, yTop, yBot, sf);
                        float bestD = float.MaxValue;
                        int runStart = -1;
                        for (int c = 0; c <= n; c++)
                        {
                            bool free = c < n && !_occ[c];
                            if (free && runStart < 0) runStart = c;
                            if (free || runStart < 0) continue;
                            float l = x0 + runStart * DodgeStep + (runStart == 0 ? EdgeMargin : DodgeGap);
                            float r = x0 + c * DodgeStep - (c == n ? EdgeMargin : DodgeGap);
                            runStart = -1;
                            float avail = r - l;
                            if (avail < MinDodgeW) continue;
                            float ww = Mathf.Min(wantW, avail);
                            float cx = Mathf.Clamp(0f, l + ww * 0.5f, r - ww * 0.5f);
                            float d = Mathf.Abs(cx);
                            if (d < bestD) { bestD = d; x = cx; cap = ww; found = true; }
                        }
                        if (found)
                        {
                            // Beside the centre the HUD may hang LOWER (a custom side element):
                            // drop under it when that band is free too, else keep the scanned band.
                            float sideTop = Baseline(x, cap, sf, ch);
                            if (sideTop > top + 0.5f)
                            {
                                float y2 = ch * 0.5f - sideTop;
                                if (!Blocked(x - cap * 0.5f, x + cap * 0.5f, y2, y2 - _h, sf)) top = sideTop;
                            }
                        }
                        else if (top > TopEdge)
                        {
                            // No side has room: ride the top edge (over the look-only top bar) if
                            // that band is clear; otherwise stay and accept the overlap.
                            float yTop2 = ch * 0.5f - TopEdge;
                            if (!Blocked(-half, half, yTop2, yTop2 - _h, sf)) top = TopEdge;
                        }
                    }
                }
            }

            _tgtX = x;
            if (Mathf.Abs(top - _tgtTop) > TopDeadband) _tgtTop = top;
            if (Mathf.Abs(cap - _capW) > 1f)
            {
                _capW = cap;
                Relayout();
            }
            // A wheel opened this frame publishes its geometry from its draw, one step behind: the
            // estimate above is replaced by the real ring as soon as it lands.
            if (estimated) _nextDodge = Mathf.Min(_nextDodge, Time.unscaledTime + 0.05f);
        }

        /// <summary>Measure the HUD's top zone into <see cref="_zone"/>. False = not measurable now.</summary>
        private static bool MeasureZone()
        {
            try { return HudSystem.TryCollectTopZoneRects(_zone, ZoneFrac); }
            catch { _zone.Clear(); return false; }
        }

        /// <summary>HOME for a strip centred at <paramref name="cx"/>, <paramref name="w"/> wide (ref
        /// px, canvas centre origin): <see cref="ZoneGap"/> under the lowest measured top-zone rect
        /// overlapping that span, <see cref="TopClear"/> when none does; the last measured home (else
        /// <see cref="TopHome"/>) when the HUD could not be measured. Clamped to [TopEdge, the
        /// 40%-of-screen floor]. Returns px below the top edge.</summary>
        private static float Baseline(float cx, float w, float sf, float ch)
        {
            float top;
            if (_zoneKnown)
            {
                float hwS = Screen.width * 0.5f;
                float xl = cx - w * 0.5f - SideSlack, xr = cx + w * 0.5f + SideSlack;
                float bottom = -1f;
                for (int i = 0; i < _zone.Count; i++)
                {
                    Rect r = _zone[i];
                    if ((r.xMax - hwS) / sf <= xl || (r.xMin - hwS) / sf >= xr) continue;
                    float b = (Screen.height - r.yMin) / sf;   // its bottom edge, px below the top
                    if (b > bottom) bottom = b;
                }
                top = bottom >= 0f ? bottom + ZoneGap : TopClear;
            }
            else top = _heldBase >= 0f ? _heldBase : TopHome;
            return Mathf.Clamp(top, TopEdge, Mathf.Max(TopEdge, ch * MaxBottomFrac - _h));
        }

        /// <summary>Load the open wheel as up to two discs: the ring and a child wheel, each grown by
        /// its reserved action-word band (<see cref="WheelWordBand"/>) and <see cref="WheelGap"/>.
        /// The geometry is what the radial itself PUBLISHED this frame (ImGui px, y down); a wheel
        /// that opened before its first draw falls back to the configured ring at the screen centre
        /// (<paramref name="estimated"/>) and is re-measured 0.05 s later.</summary>
        private static bool LoadWheel(float sf, out bool estimated)
        {
            estimated = false;
            _wheelN = 0;
            try
            {
                float band = WheelWordBand();
                if (Overlay.RadialHintContext.GeometryFresh && Overlay.RadialHintContext.OuterR > 1f)
                {
                    AddDisc(Overlay.RadialHintContext.Center, Overlay.RadialHintContext.OuterR + band, sf);
                    if (Overlay.RadialHintContext.SatelliteShown && Overlay.RadialHintContext.SatOuterR > 1f)
                        AddDisc(Overlay.RadialHintContext.SatCenter, Overlay.RadialHintContext.SatOuterR + band, sf);
                }
                else
                {
                    float r = UIAConfig.RadialOuterRadius != null ? UIAConfig.RadialOuterRadius.Value : 240f;
                    AddDisc(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), r + band, sf);
                    estimated = true;
                }
            }
            catch { _wheelN = 0; }
            return _wheelN > 0;
        }

        /// <summary>One wheel disc: ImGui px (top-left origin, y down) -&gt; strip canvas ref px
        /// (centre origin, y up), radius grown by <see cref="WheelGap"/>.</summary>
        private static void AddDisc(Vector2 imgui, float radiusPx, float sf)
        {
            if (_wheelN >= _wr.Length || radiusPx <= 0f) return;
            _wcx[_wheelN] = (imgui.x - Screen.width * 0.5f) / sf;
            _wcy[_wheelN] = (Screen.height * 0.5f - imgui.y) / sf;
            _wr[_wheelN] = radiusPx / sf + WheelGap;
            _wheelN++;
        }

        /// <summary>The band an open ring reserves ABOVE its rim, in screen px (the radial canvas is
        /// unscaled): <c>UnityRadialView.UpdateActionWord</c>'s bandTop - the lift (hover bulge 13 +
        /// hover border bw x 1.2 + two AA fringes + VerbGapPx 3), the curved word's plate
        /// (fs x 1.3 + 10), its border and the 12 px counter clearance - plus ~12 px for the page
        /// counter's own glyphs on top of it. Reserved whether or not the word shows (it rides the
        /// HOVERED wedge and can pass 12 o'clock at any time), so the strip does not bob as the
        /// pointer moves.</summary>
        private static float WheelWordBand()
        {
            float bw = UIAConfig.RadialBorderWidth != null ? Mathf.Max(0f, UIAConfig.RadialBorderWidth.Value) : 3.2f;
            float fs = UIAConfig.RadialTextVerb != null ? Mathf.Max(0f, UIAConfig.RadialTextVerb.Value) : 15f;
            float fe = UIAConfig.RadialEdgeFeather != null ? Mathf.Max(0f, UIAConfig.RadialEdgeFeather.Value) : 1.25f;
            return 13f + bw * 1.2f + 2f * fe + 3f + fs * 1.3f + 10f + bw + 12f + 12f;
        }

        /// <summary>How far below the top edge (ref px) the highest wheel disc point over the span
        /// [<paramref name="xl"/>, <paramref name="xr"/>] (canvas ref px, centre origin) lies;
        /// <see cref="float.MaxValue"/> when no disc reaches the span.</summary>
        private static float WheelTopOver(float xl, float xr, float ch)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _wheelN; i++)
            {
                float r = _wr[i];
                float dx = Mathf.Clamp(_wcx[i], xl, xr) - _wcx[i];
                if (Mathf.Abs(dx) >= r) continue;
                float below = ch * 0.5f - (_wcy[i] + Mathf.Sqrt(r * r - dx * dx));
                if (below < best) best = below;
            }
            return best;
        }

        /// <summary>Does a wheel disc cross one DodgeStep-wide column of the band (canvas ref px,
        /// centre origin, y up)? Exact segment-vs-disc, widened by half a column.</summary>
        private static bool WheelHitsColumn(float cx, float yTop, float yBot)
        {
            for (int i = 0; i < _wheelN; i++)
            {
                float r = _wr[i];
                float dx = Mathf.Max(0f, Mathf.Abs(cx - _wcx[i]) - DodgeStep * 0.5f);
                if (dx >= r) continue;
                float s = Mathf.Sqrt(r * r - dx * dx);
                if (_wcy[i] - s <= yTop && _wcy[i] + s >= yBot) return true;
            }
            return false;
        }

        private static bool Blocked(float xl, float xr, float yTop, float yBot, float sf)
        {
            for (float x = xl; x <= xr + 0.01f; x += DodgeStep)
                if (HitBand(Mathf.Min(x, xr), yTop, yBot, sf)) return true;
            return false;
        }

        /// <summary>Probe one column of the strip's band against the open wheel's discs and (top,
        /// middle, bottom) against the Universal Inventory and every pinned window. Canvas coords
        /// (centre origin) -&gt; screen px for the window hit tests.</summary>
        private static bool HitBand(float cx, float yTop, float yBot, float sf)
        {
            if (_wheelN > 0 && WheelHitsColumn(cx, yTop, yBot)) return true;
            float sx = cx * sf + Screen.width * 0.5f;
            float hy = Screen.height * 0.5f;
            return HitAt(sx, (yTop - 4f) * sf + hy)
                || HitAt(sx, (yTop + yBot) * 0.5f * sf + hy)
                || HitAt(sx, (yBot + 4f) * sf + hy);
        }

        private static bool HitAt(float x, float y)
        {
            var p = new Vector2(x, y);
            try
            {
                if (global::StationeersUIMod.UI.Grid.TheGridPanel.HitTestWindow(p)) return true;
                if (global::StationeersUIMod.UI.Grid.PinnedInventoryWindow.HitTestAny(p)) return true;
            }
            catch { }
            return false;
        }

        private static void ApplyPosition(float rise, bool force)
        {
            if (_strip == null) return;
            var p = new Vector2(_curX, -(_curTop + _h * 0.5f) - rise);
            if (!force && Mathf.Abs(p.x - _lastPos.x) < 0.05f && Mathf.Abs(p.y - _lastPos.y) < 0.05f) return;
            _lastPos = p;
            _strip.anchoredPosition = p;
        }

        private static float ScaleFactor()
        {
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            return sf > 0.01f ? sf : 1f;
        }

        // ================= small helpers =================

        private static float Ease01(float t) { return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)); }

        private static void SetAlpha(CanvasGroup g, float a)
        {
            if (g == null) return;
            a = Mathf.Clamp01(a);
            if (Mathf.Abs(g.alpha - a) > 0.002f || (a == 0f && g.alpha != 0f) || (a == 1f && g.alpha != 1f)) g.alpha = a;
        }

        private static void SetScaleX(RectTransform rt, float x)
        {
            if (rt == null) return;
            x = Mathf.Clamp01(x);
            if (Mathf.Abs(rt.localScale.x - x) > 0.001f) rt.localScale = new Vector3(x, 1f, 1f);
        }

        private static void ApplyDrain()
        {
            if (_drainRt == null) return;
            bool on = _drainFrac >= 0f && !_appliedCompact;
            if (_drainRt.gameObject.activeSelf != on) _drainRt.gameObject.SetActive(on);
            if (on) SetScaleX(_drainRt, _drainFrac);
        }

        private static bool CanDrawSafe() { try { return Guards.CanDraw(); } catch { return false; } }
        private static bool VanillaFront() { try { return Guards.VanillaMenuWantsFront(); } catch { return false; } }
        private static bool DesignerUp() { try { return global::StationeersUIMod.Windows.HudEditorMode.Active; } catch { return false; } }
        private static bool CardUp() { return TutorialCoach.CardOpen && !TutorialCoach.CalloutOpen; }
        private static bool MenuUp() { try { return UiaControlCenter.IsOpen; } catch { return false; } }
        private static bool WheelOpen() { try { return global::StationeersUIMod.Features.RadialController.AnyRadialOpen; } catch { return false; } }
        private static bool GridOpen() { try { return global::StationeersUIMod.UI.Grid.TheGridPanel.IsOpen; } catch { return false; } }
        private static int PinCount() { try { return global::StationeersUIMod.UI.Grid.PinnedInventoryWindow.LiveCount; } catch { return 0; } }

        /// <summary>A child wheel (satellite) is on screen, per the radial's own published geometry.</summary>
        private static bool SatShown()
        {
            try { return Overlay.RadialHintContext.GeometryFresh && Overlay.RadialHintContext.SatelliteShown; }
            catch { return false; }
        }

        /// <summary>The HUD's live tier + curvature mode + bare layout, packed: a flip re-measures the
        /// top zone at once (the bare tier drops the top bar; each curvature mode has its own layout).</summary>
        private static int HudKey()
        {
            try
            {
                return ((int)HudElementView.LayoutTier << 8) | ((int)HudElementView.LayoutMode << 1)
                    | (HudElementView.LayoutBare ? 1 : 0);
            }
            catch { return 0; }
        }

        /// <summary>The window glass, pushed every visible frame exactly like the coach: theme fill +
        /// border + <see cref="ApplyGlass"/>. <paramref name="alpha"/> is the strip's effective opacity
        /// (enter / hide / stand-down curves), which the analytic path must apply to its border and
        /// glow layers itself.</summary>
        private static void StylePanel(float alpha)
        {
            if (_panel == null) return;
            _panel.color = UiaTheme.Window;
            _panel.BorderColor = UiaTheme.Border;
            _panel.BorderWidth = HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f;
            ApplyGlass(_panel, alpha);
        }

        // ================= the tutorial glass (strip + coach) =================

        /// <summary>
        /// THE tutorial glass - the lesson strip here and the coach's card + callout
        /// (<c>TutorialCoach.StyleGlass</c>): the F9 global effect stack on the ANALYTIC SDF panel
        /// renderer the HUD boxes use (<c>UIA/HudPanelSdf</c> through the shared "sdfglass" material)
        /// whenever the HUD itself can use it, with the mesh renderer as the fail-soft fallback. The
        /// caller sets the theme fill / border colour / width / shape first.
        ///
        /// <para><b>Why (2026-09-26, the "X" on the lesson strip).</b> On the MESH path a
        /// <see cref="PanelGraphic"/> carrying the inner glow fills its interior with rings that are
        /// UNIFORMLY SCALED copies of the contour, sized by the SHORT side
        /// (sMax = 1 - (glowInD + 2) / minHalf = 0.41 on a 96 px strip), and bridges the outermost
        /// ring to the stop-0 vertices, whose depth TAPERS from glowInD (26.4 px) down to ~rc near every
        /// corner. On a short, wide panel the bridge rulings are sheared hundreds of px sideways, so
        /// adjacent rulings ending at different stop-0 depths CROSS: the bridge quads fold and the
        /// translucent fill is drawn two and three times over long wedges converging on each corner -
        /// the hard-edged dark "X" (an off-line replica of the mesh measured 176 folded triangles and
        /// ~7,300 ref px^2 double-covered on the 760x96 strip, and 0 with the inner glow off; the
        /// classic centre fan folds worse). DenseFill cannot help - the fold IS in the dense bridge.
        /// The analytic path draws fill, border, inner glow and halo in fragment space over a plain
        /// parameter grid, so it cannot fold. The mesh fallback therefore drops the glow layers
        /// (<c>includeGlow: false</c> - inner glow AND halo: <see cref="HudGlobalGlass.Apply"/> must
        /// set every field once per frame, and zeroing only the inner glow after it would flip the
        /// dirty-guarded setter twice a frame and rebuild the mesh every frame) and keeps sheen, edge
        /// light, ripple, border fade, soft edge, frost and shine.</para>
        ///
        /// <para><b>Order</b> is <c>HudElementView.ApplyFx</c>'s fail-soft invariant: the sdfglass
        /// material is assigned FIRST and SDF mode switched on only when that succeeded (an SDF
        /// parameter grid under any other material draws as a solid grid); Apply is then told the
        /// slot is owned (externalMaterial) so the two never reassign it against each other. Frost is
        /// CONSUMED while the backdrop capture is live, never demanded (FrostDemand is single-writer).
        /// Gated on <see cref="HudSystem.FxClockLive"/> like the Grid shell: a stopped FX clock would
        /// freeze the shader-time edge flow.</para>
        ///
        /// <para><b>Fades.</b> Unity delivers a CanvasGroup's alpha through the vertex COLOUR only
        /// (TMP's SDF shader multiplies its whole result by it for that reason); the analytic panel
        /// carries its border and glow in uv2/uv3, so a group fade reaches only its fill. A caller
        /// that fades the panel through a CanvasGroup passes that opacity as
        /// <paramref name="fade"/>: the border colour, the glow strengths and the flowing aura / haze
        /// are scaled with it (the tiny parameter grid rebuilds only while a fade runs; at 1 nothing
        /// changes, so the steady state never rebuilds) - and must stop the graphic drawing at 0
        /// (<see cref="Tick"/> does). The coach's panels never fade (SetActive only): fade 1.</para>
        ///
        /// <para>Per-frame safe (dirty-guarded setters, idempotent Assign); shared chrome that follows
        /// the active theme's effect globals - nothing per-tier, no new knob (CLAUDE.md rule 8).
        /// Returns true while the analytic path is live.</para>
        /// </summary>
        internal static bool ApplyGlass(PanelGraphic panel, float fade = 1f)
        {
            if (panel == null) return false;
            bool fxLive = HudSystem.FxClockLive;
            bool sdf = fxLive
                && (!panel.CornersAreCut || HudShaderStore.SdfCutAvailable)
                && HudConfig.SdfPanels != null && HudConfig.SdfPanels.Value
                && HudShaderStore.SdfAvailable
                && HudFxMaterials.Assign(panel, "sdfglass");
            fade = Mathf.Clamp01(fade);
            if (sdf) PushSdfGlobals(panel, fade);
            else panel.SetSdfStyle(false, 2f, false, 0f, 0f, 1f, 0f, 0f, 0f,
                false, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
            HudGlobalGlass.Apply(panel, includeGlow: sdf, wantFrost: fxLive, wantTierB: fxLive,
                externalMaterial: sdf);
            if (sdf && fade < 0.999f)
            {
                Color bc = panel.BorderColor;
                bc.a *= fade;
                panel.BorderColor = bc;
                panel.Glow *= fade;
                panel.GlowInner *= fade;
            }
            return sdf;
        }

        /// <summary>The GLOBAL branch of the analytic-only extras - <c>GridTheme.ApplySdfShell</c> with
        /// every Grid override at its sentinel, i.e. what a follow-global HUD box resolves in
        /// <c>HudElementView.ApplyFx</c>: squircle, halo quality, edge flow, frost + chroma (only while
        /// the backdrop capture is live), shine / iridescence (Tier B on the running FX clock) and the
        /// organic-halo family. No boot dissolve, no per-element end fades. The two halo terms that
        /// the glow strength does not scale (flowing aura, haze) take <paramref name="fade"/>.</summary>
        private static void PushSdfGlobals(PanelGraphic panel, float fade)
        {
            bool tierA = On(HudConfig.FxTierA);
            bool tierB = HudSystem.FxClockLive && On(HudConfig.FxTierB);
            float squircle = Mathf.Clamp(Val(HudConfig.SdfSquircle, 2f), 2f, 8f);
            bool gaussian = On(HudConfig.SdfGaussianHalo);
            float flow = Mathf.Clamp(Val(HudConfig.FxEdgeFlowSpeed, 0.22f), 0f, 4f);
            float frost = HudBackdrop.Active && On(HudConfig.FxTierC)
                ? Mathf.Clamp01(Val(HudConfig.FrostStrength, 1f)) : 0f;
            float frostDepth = Mathf.Clamp01(Val(HudConfig.FrostDepth, 1f));
            float chroma = frost > 0.001f && tierB && On(HudConfig.FxChromaOn)
                ? Mathf.Clamp01(Val(HudConfig.FxChroma, 0f)) : 0f;
            float shine = tierB && On(HudConfig.FxShineOn) ? Mathf.Clamp(Val(HudConfig.FxShine, 0f), 0f, 2f) : 0f;
            float irid = tierB && On(HudConfig.FxIridOn) ? Mathf.Clamp01(Val(HudConfig.FxIridescence, 0f)) : 0f;
            bool glowOn = tierA && On(HudConfig.FxGlowOn);
            bool edgeOn = tierA && On(HudConfig.FxEdgeLightOn);
            float aura = edgeOn && On(HudConfig.FxGlowFlowAuraOn)
                ? Mathf.Clamp(Val(HudConfig.FxGlowFlowAura, 0.6f), 0f, 2f) : 0f;
            bool auraOn = aura > 0.001f;
            float haze = glowOn ? Mathf.Clamp01(Val(HudConfig.FxGlowHaze, 0f)) : 0f;
            float breath = (glowOn || auraOn) && On(HudConfig.FxGlowBreathOn)
                ? Mathf.Clamp01(Val(HudConfig.FxGlowBreath, 0.35f)) : 0f;
            float uneven = (glowOn || auraOn) && On(HudConfig.FxGlowUnevenOn)
                ? Mathf.Clamp01(Val(HudConfig.FxGlowUneven, 0.5f)) : 0f;
            float organic = 1f;
            if (uneven > 0.001f)
            {
                float v = Val(HudConfig.FxGlowOrganicScale, 1f);
                organic = v < 0.2f ? 1f : Mathf.Clamp(v, 0.25f, 4f);
            }
            panel.SetSdfStyle(true, squircle, gaussian, flow,
                frost, frostDepth, chroma, shine, irid,
                false,      // dissolve: the tutorial does not ride the HUD's boot-dissolve clock
                0f, 0f,     // edgeFadeX/Y: a HUD-element feature
                haze * fade, breath, uneven, aura * fade, organic);
            // The SDF ABI carries independent final strengths; uv0.x's combined volume is unused
            // there and must not force legacy mesh semantics (mirrors HudElementView.ApplyFx).
            panel.FxStrength = 0f;
        }

        private static bool On(ConfigEntry<bool> e) { return e != null && e.Value; }
        private static float Val(ConfigEntry<float> e, float d) { return e != null ? e.Value : d; }

        // ================= build / teardown =================

        private static bool EnsureBuilt()
        {
            if (_root != null) return true;
            // A build that threw must not retry (and log) on every Show of a Director that calls
            // Show each frame.
            if (Time.unscaledTime < _nextBuildTry) return false;
            try
            {
                Build();
                return _root != null;
            }
            catch (Exception e)
            {
                UIALog.Error("TutorialStrip build failed (lessons fall back to no strip): " + e);
                try { DestroyRoot(); } catch { }
                _nextBuildTry = Time.unscaledTime + 5f;
                return false;
            }
        }

        private static void Build()
        {
            _root = new GameObject("UIAscended_TutorialStrip");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortOrder;   // full z-map in the class remarks
            // The glass materials read the extra vertex streams (the UiaControlCenter / TheGridPanel
            // opt-in): without them a frost/edgefx material samples undefined data.
            _canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // Deliberately NO GraphicRaycaster - the strip can never take a click.
            _rootGroup = _root.AddComponent<CanvasGroup>();
            _rootGroup.interactable = false;
            _rootGroup.blocksRaycasts = false;
            _rootGroup.alpha = 0f;

            try { _litHex = ColorUtility.ToHtmlStringRGB(UiaTheme.Selected); } catch { _litHex = "FFA040"; }

            var stripGo = new GameObject("strip", typeof(RectTransform));
            stripGo.transform.SetParent(_root.transform, false);
            _strip = (RectTransform)stripGo.transform;
            _strip.anchorMin = _strip.anchorMax = new Vector2(0.5f, 1f);
            _strip.pivot = new Vector2(0.5f, 0.5f);
            _strip.sizeDelta = new Vector2(_w, _h);

            // Glass panel (a real HUD PanelGraphic: the coach / F10 effect stack). Centre pivot -
            // PanelGraphic draws its shape centred on the pivot.
            _panelRt = Node("panel", _strip, new Vector2(0.5f, 0.5f));
            _panel = _panelRt.gameObject.AddComponent<PanelGraphic>();
            _panel.raycastTarget = false;
            _panel.color = UiaTheme.Window;
            _panel.BorderColor = UiaTheme.Border;
            // Mesh-fallback guard for a sheen GRADIENT across this wide panel (the 2026-07-15 bowtie).
            // Not the glow fold - that is why the fallback carries no glow layers (see ApplyGlass);
            // without them the dense rings are star-shaped and fold-free. Inert on the analytic path.
            _panel.DenseFill = true;

            // Demo slot (left): framed well + the mini stage + the success tick.
            _demoSlot = Node("demo-slot", _strip, new Vector2(0f, 0.5f));
            _demoSlot.sizeDelta = new Vector2(DemoW, DemoH);
            var frame = UiaUi.Image(_demoSlot, UiaTheme.Panel, "demo-frame");
            frame.raycastTarget = false;
            UiaUi.Fill((RectTransform)frame.transform);
            var dgRt = Node("demo-group", _demoSlot, new Vector2(0.5f, 0.5f));
            dgRt.sizeDelta = new Vector2(DemoW, DemoH);
            _demoGroup = dgRt.gameObject.AddComponent<CanvasGroup>();
            _demoGroup.interactable = false;
            _demoGroup.blocksRaycasts = false;
            _demoHost = Node("demo-host", dgRt, new Vector2(0.5f, 0.5f));
            _demoHost.sizeDelta = new Vector2(DemoW, DemoH);
            try { _demo = TutorialDemoStage.Create(_demoHost); }
            catch (Exception e)
            {
                _demo = null;
                UIALog.Warn("TutorialStrip: demo stage unavailable (the strip still works): " + e.Message);
            }
            _demoShownId = null;

            // Text column.
            var colGo = new GameObject("text", typeof(RectTransform));
            colGo.transform.SetParent(_strip, false);
            _textCol = (RectTransform)colGo.transform;
            _textCol.anchorMin = Vector2.zero;
            _textCol.anchorMax = Vector2.one;
            _textCol.pivot = new Vector2(0.5f, 0.5f);
            _textGroup = colGo.AddComponent<CanvasGroup>();
            _textGroup.interactable = false;
            _textGroup.blocksRaycasts = false;

            _headerTmp = UiaUi.Text(_textCol, "", UiaTheme.SmallSize, UiaTheme.Accent, TextAlignmentOptions.TopLeft);
            var hRt = _headerTmp.rectTransform;
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.sizeDelta = new Vector2(0f, HeaderH);
            hRt.anchoredPosition = Vector2.zero;
            _headerTmp.richText = false;
            _headerTmp.fontStyle = FontStyles.Bold;
            _headerTmp.characterSpacing = 3f;
            UiaControls.FitText(_headerTmp, 9f, false);

            _bodyTmp = UiaUi.Text(_textCol, "", UiaTheme.LabelSize, UiaTheme.Text, TextAlignmentOptions.TopLeft, true);
            _bodyRt = _bodyTmp.rectTransform;
            _bodyRt.anchorMin = Vector2.zero;
            _bodyRt.anchorMax = Vector2.one;
            _bodyRt.pivot = new Vector2(0.5f, 0.5f);
            _bodyTmp.richText = true;   // for the key-lit pulse; every literal run is <noparse>-wrapped
            _bodyTmp.enableAutoSizing = true;
            _bodyTmp.fontSizeMin = 11f;
            _bodyTmp.fontSizeMax = UiaTheme.LabelSize;
            _bodyTmp.overflowMode = TextOverflowModes.Overflow;

            _drainImg = UiaUi.Image(_textCol, UiaTheme.AccentDim, "drain");
            _drainImg.raycastTarget = false;
            _drainImg.sprite = null;
            _drainImg.type = Image.Type.Simple;
            _drainRt = (RectTransform)_drainImg.transform;
            _drainRt.anchorMin = new Vector2(0f, 0f);
            _drainRt.anchorMax = new Vector2(1f, 0f);
            _drainRt.pivot = new Vector2(0f, 0f);
            _drainRt.sizeDelta = new Vector2(0f, DrainH);
            _drainRt.anchoredPosition = Vector2.zero;
            _drainRt.gameObject.SetActive(false);

            // Success tick: two drawn panels (never a glyph - the TMP font has no check mark).
            _tickRoot = Node("tick", _strip, new Vector2(0f, 0.5f));
            _tickRoot.sizeDelta = new Vector2(44f, 36f);
            _tickGroup = _tickRoot.gameObject.AddComponent<CanvasGroup>();
            _tickGroup.interactable = false;
            _tickGroup.blocksRaycasts = false;
            _tickGroup.alpha = 0f;
            Color good = UiaTheme.Good;
            // Pen path L -> J (short arm, down-right) then J -> R (long arm, up-right).
            var joint = new Vector2(-4f, -10f);
            const float shortLen = 15f, longLen = 30f, stroke = 5f;
            var shortStart = joint + new Vector2(-shortLen * 0.7071f, shortLen * 0.7071f);
            _tickShort = Arm(_tickRoot, "short", shortStart, -45f, shortLen, stroke, good);
            _tickLong = Arm(_tickRoot, "long", joint, 52f, longLen, stroke, good);

            // Success flash ring (Good), over everything, faded by its own group.
            _flashRt = Node("flash", _strip, new Vector2(0.5f, 0.5f));
            _flashGroup = _flashRt.gameObject.AddComponent<CanvasGroup>();
            _flashGroup.interactable = false;
            _flashGroup.blocksRaycasts = false;
            _flashGroup.alpha = 0f;
            _flash = _flashRt.gameObject.AddComponent<PanelGraphic>();
            _flash.raycastTarget = false;
            _flash.color = new Color(good.r, good.g, good.b, 0.06f);
            _flash.BorderColor = good;
            _flash.BorderWidth = 2.2f;
            _flash.Glow = 0.8f;
            _flash.GlowWidth = 12f;

            _builtThemeHash = UiaMenuTheme.StyleHash();
            _nextRestyle = Time.unscaledTime + RestyleInterval;
            _lastPos = new Vector2(float.NaN, float.NaN);
            Relayout();
            _root.SetActive(false);
        }

        /// <summary>A centred-pivot node anchored at <paramref name="anchor"/> of its parent.</summary>
        private static RectTransform Node(string name, Transform parent, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>One tick stroke: a pivot-at-start holder (rotated along the stroke, scaled on x
        /// to "draw" it) carrying a centred rounded panel.</summary>
        private static RectTransform Arm(RectTransform parent, string name, Vector2 start, float angleDeg,
            float len, float stroke, Color col)
        {
            var holderGo = new GameObject(name, typeof(RectTransform));
            holderGo.transform.SetParent(parent, false);
            var h = (RectTransform)holderGo.transform;
            h.anchorMin = h.anchorMax = new Vector2(0.5f, 0.5f);
            h.pivot = new Vector2(0f, 0.5f);
            h.sizeDelta = new Vector2(len, stroke);
            h.anchoredPosition = start;
            h.localEulerAngles = new Vector3(0f, 0f, angleDeg);
            h.localScale = new Vector3(0f, 1f, 1f);
            var barGo = new GameObject("bar", typeof(RectTransform));
            barGo.transform.SetParent(h, false);
            var b = (RectTransform)barGo.transform;
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f);
            b.pivot = new Vector2(0.5f, 0.5f);
            b.sizeDelta = new Vector2(len, stroke);
            b.anchoredPosition = Vector2.zero;
            var g = barGo.AddComponent<PanelGraphic>();
            g.raycastTarget = false;
            g.CornerCut = 0;   // round caps even under a global CUT corner style
            g.color = col;
            g.BorderColor = new Color(0f, 0f, 0f, 0f);
            g.BorderWidth = 0f;
            g.SetShape(len, stroke, stroke * 0.5f);
            return h;
        }

        /// <summary>Live theme change: rebuild the canvas (kit colours freeze at build) and put the
        /// SAME content back without replaying the enter animation.</summary>
        private static void Rebuild()
        {
            bool wasActive = _root != null && _root.activeSelf;
            DestroyRoot();
            if (!EnsureBuilt()) return;
            // The lit accent may have changed with the theme: rebuild the variants.
            _bodyPlain = BuildVariant(_body, false);
            _bodyLit = BuildVariant(_body, true);
            if (_chrome != null) { _chromePlain = BuildVariant(_chrome, false); _chromeLit = BuildVariant(_chrome, true); }
            if (_then != null) { _thenPlain = BuildVariant(_then, false); _thenLit = BuildVariant(_then, true); }
            _demoKnown = _demo != null && !string.IsNullOrEmpty(_demoId) && KnownDemo(_demoId);
            if (wasActive) _root.SetActive(true);
            _txPhase = 0;
            ApplyContentNow();
            SetAlpha(_rootGroup, _alphaVis * _standMul);
            SetAlpha(_textGroup, 1f);
            ApplyPosition(0f, true);
        }

        private static void HideNow()
        {
            _vis = Vis.Hidden;
            _alphaVis = 0f;
            _txPhase = 0;
            _tickOn = false;
            _shownLit = false;
            if (_rootGroup != null) _rootGroup.alpha = 0f;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        private static void DestroyRoot()
        {
            if (_demo != null)
            {
                try { _demo.Destroy(); } catch (Exception e) { UIALog.Warn("TutorialStrip demo teardown: " + e.Message); }
                _demo = null;
            }
            if (_panel != null) { try { HudFxMaterials.Unassign(_panel); } catch { } }
            if (_root != null) { try { UnityEngine.Object.Destroy(_root); } catch { } }
            _root = null;
            _canvas = null;
            _rootGroup = null;
            _strip = null;
            _panel = null;
            _panelRt = null;
            _flashRt = null;
            _flash = null;
            _flashGroup = null;
            _demoSlot = null;
            _demoGroup = null;
            _demoHost = null;
            _tickRoot = null;
            _tickGroup = null;
            _tickShort = _tickLong = null;
            _textCol = null;
            _textGroup = null;
            _headerTmp = null;
            _bodyTmp = null;
            _bodyRt = null;
            _drainRt = null;
            _drainImg = null;
            _demoShownId = null;
            _lastPos = new Vector2(float.NaN, float.NaN);
        }
    }
}
