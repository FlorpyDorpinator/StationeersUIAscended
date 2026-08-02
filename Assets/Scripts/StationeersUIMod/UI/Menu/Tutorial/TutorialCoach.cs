using System;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.UI;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// The first-run "coach": a modal, click-through card that teaches the 15 steps in
    /// <see cref="TutorialSteps"/> with a looping mock demo per step
    /// (<see cref="TutorialDemoStage"/>) and LIVE key glyphs, so a rebind can never make the
    /// tutorial lie.
    ///
    /// <para><b>A sibling of the Control Center, deliberately.</b> Same construction as
    /// <see cref="UiaControlCenter"/> - own ScreenSpaceOverlay canvas, a full-screen scrim that
    /// absorbs clicks, one centred window whose surface is a real HUD <c>PanelGraphic</c>, and
    /// every colour/font read from <see cref="UiaTheme"/> so the card re-skins with the active
    /// profile. Sorting order 5600: above the Control Center (5200), below tooltips (6000).</para>
    ///
    /// <para><b>Pause.</b> The first-run open latches the game paused through
    /// <see cref="GamePause"/> (single-player only; there is no MP pause), which is why every clock
    /// in here and in the demo stage is <c>Time.unscaledTime</c> - a <c>Time.time</c> animation
    /// would freeze solid behind the card. The header chip reports the truth either way.</para>
    ///
    /// <para><b>Unwind order (load-bearing).</b> Close releases the PAUSE first and the modal input
    /// state second. <c>WorldManager.SetGamePause</c> pushes its own <c>KeyInputState.Paused</c>
    /// onto KeyManager's stack; popping our Typing state while the pause state is still stacked
    /// leaves vanilla in the wrong input mode (reference section 20 / plan section 5.1).</para>
    ///
    /// <para><b>Hot-reload.</b> Every static below is cleared in <see cref="Shutdown"/>: the canvas
    /// is destroyed, the demo stage torn down, the <see cref="GamePause.PausedChanged"/>
    /// subscription dropped (a stale delegate would call into a dead assembly), the pause latch and
    /// modal released, and <see cref="TutorialTextStore"/> flushed.</para>
    /// </summary>
    internal static class TutorialCoach
    {
        private sealed class Modal : IModal { public bool UnlockCursor => true; }

        internal const string InputStateKey = "UIA_Tutorial";   // ModalInputChain reasserts by this name
        private const string PauseReason = "tutorial";
        private const string CursorHoldId = "tutorial";

        // ---- metrics (reference px at 1080p; the CanvasScaler scales) ----
        private const float WinW = 720f;
        private const float WinHRead = 704f;   // title + rule + step heading + demo + copy + progress + nav
        private const float WinHEdit = 760f;   // + the edit row and the token hint line
        private const float DemoW = 680f;
        private const float DemoH = 300f;
        private const float NavH = 36f;
        private const float RestyleMinInterval = 0.14f;  // same throttle as UiaControlCenter
        private const float ToastSeconds = 2.4f;

        // The welcome step's third sentence has a multiplayer variant (plan section 6, step 1):
        // there is no MP pause, so the copy must not claim the game is stopped.
        private const string PausedLine = "The game is paused while you read.";
        private const string RunningLine = "Your game is still running - Skip any time.";

        private static readonly Modal _modal = new Modal();

        private static GameObject _root;
        private static RectTransform _window;
        private static UI.Hud.PanelGraphic _windowPanel;
        private static RectTransform _demoHost;   // the 680x300 stage rect handed to the demo
        private static RectTransform _bodyHost;   // cleared + rebuilt per step
        private static RectTransform _navHost;    // cleared + rebuilt per step
        private static UiaControls.UiaButton _pauseBtn;   // header pause, F10-parity (greyed in MP)
        private static TextMeshProUGUI _stepHeading;      // centered step name above the demo
        private static TextMeshProUGUI _bodyLabel;
        private static TextMeshProUGUI _toast;
        private static TMP_InputField _headingField, _bodyField;
        private static TutorialDemoStage _demo;

        private static bool _open;
        private static bool _modalHeld;
        private static bool _devEdit;        // requested mode
        private static bool _builtDevEdit;   // mode the live canvas was built for
        private static int _index;
        private static int _builtThemeHash;
        private static float _lastRestyle;
        private static float _toastUntil;
        private static Action<bool> _pausedHandler;

        internal static bool IsOpen { get { return _open; } }

        // ================= open / close =================

        /// <summary>The first-world-entry path: open at step 1 and (config permitting, and only
        /// when we can legally own the pause) latch the game paused so a fresh-save player is not
        /// burning oxygen while reading. Manual replays never auto-pause - plan section 5.3.</summary>
        internal static void OpenFirstRun()
        {
            Open(false);
            if (!_open) return;
            try
            {
                if (UIAConfig.TutorialAutoPause != null && UIAConfig.TutorialAutoPause.Value
                    && GamePause.CanOwnPause())
                    GamePause.Hold(PauseReason, InputStateKey);
            }
            catch (Exception e) { UIALog.Warn("TutorialCoach auto-pause failed: " + e.Message); }
            PaintChip();
            RepaintBodyText();   // the welcome copy differs paused vs running
        }

        /// <summary>Open at step 1. <paramref name="devEdit"/> swaps the heading/body labels for
        /// text fields (the <c>uiatutorial edit</c> path) - see <see cref="TutorialTextStore"/>.</summary>
        internal static void Open(bool devEdit = false)
        {
            TutorialTextStore.Load();
            // The two modes have different window heights and different body widgets, so a mode
            // change is a rebuild, not a re-render.
            if (_root != null && _builtDevEdit != devEdit) DestroyRoot();
            _devEdit = devEdit;
            try { EnsureBuilt(); }
            catch (Exception e)
            {
                // A half-built root would be a screen-filling click blocker with no modal held and
                // no Escape (Update only pumps while _open). Tear down whatever got created.
                UIALog.Error("TutorialCoach build failed: " + e);
                try { DestroyRoot(); } catch { }
                return;
            }
            if (_root == null) return;
            _open = true;
            _index = 0;
            _root.SetActive(true);
            AcquireModal();
            HookPause();
            RenderStep();
        }

        /// <summary>Close and give everything back. ORDER MATTERS: pause latch first, modal input
        /// state second (see the class remarks).</summary>
        internal static void Close()
        {
            if (!_open) return;
            // Edit mode: whatever sits in the fields right now must survive this close (X, Skip,
            // Esc) exactly like Back/Next preserves it - in memory; disk still needs Save.
            try { CaptureFields(); } catch { }
            _open = false;
            try { GamePause.Release(PauseReason); } catch (Exception e) { UIALog.Warn("TutorialCoach pause release: " + e.Message); }
            UnhookPause();
            // Symmetric with UiaControlCenter.Close: drop the shared glass/edgefx material now so a
            // reopen cannot flash one stale frame. (We deliberately never write
            // HudGlobalGlass.FrostDemand - it is single-writer, owned per frame by the Control
            // Center; GridTheme sets the same precedent.)
            if (_windowPanel != null) { try { UI.Hud.HudFxMaterials.Unassign(_windowPanel); } catch { } }
            if (_root != null) _root.SetActive(false);
            ReleaseModal();
        }

        // Frame stamp of the last frame a dev-edit field held keyboard focus (see Update).
        private static int _lastFieldFocusFrame = -999;

        // ================= per-frame pump =================

        /// <summary>Pumped every frame from StationeersUIMod.Update (like the Control Center).
        /// Drives the window glass, the demo stage, the theme poll, and the two nav keys.</summary>
        internal static void Update()
        {
            if (!_open) return;

            StyleWindowPanel();
            // Keep the pause button honest with no event (a client joining flips CanOwnPause
            // silently) - two dirty-guarded setters, F10 does the same.
            PaintChip();

            if (_demo != null)
            {
                try { _demo.Tick(); }
                catch (Exception e)
                {
                    UIALog.Warn("Tutorial demo tick failed: " + e.Message);
                    try { _demo.Destroy(); } catch { }   // never leave a frozen mock on screen
                    _demo = null;
                }
            }
            ToastTick();

            // If the world goes away (menu / loading / unload), never leave the card stranded -
            // and never leave the pause latched behind it.
            if (!Guards.CanDraw()) { Close(); return; }

            // Follow the live theme exactly as UiaControlCenter does: poll the resolved-colour hash
            // and rebuild, throttled so an F9 colour-wheel drag cannot rebuild the canvas 60x/s.
            if (UiaMenuTheme.StyleHash() != _builtThemeHash
                && Time.unscaledTime - _lastRestyle >= RestyleMinInterval)
            {
                Restyle();
                return;
            }

            // Edit mode: while a field owns the keyboard, Enter is a newline and Esc is TMP's own
            // deselect - neither may page the tutorial. TMP deactivates a single-line field on the
            // same frame it handles submit, so nav keys are also swallowed for one frame after a
            // field loses focus - otherwise the Enter that commits the heading would ALSO page.
            if (_devEdit && (TutorialTextField.Focused(_headingField) || TutorialTextField.Focused(_bodyField)))
            {
                _lastFieldFocusFrame = Time.frameCount;
                return;
            }
            if (_devEdit && Time.frameCount - _lastFieldFocusFrame <= 1) return;

            // The console (the very window `uiatutorial` is typed into), vanilla input windows and
            // the creative menu read the same raw Enter/Escape - while one is up, nav keys are not
            // ours (CanDraw was already checked above, so this adds exactly those three).
            if (!Guards.CanToggleMenus()) return;

            // The Handbook viewer stacks ABOVE the coach (sort 5700 vs 5600) and reads the same raw
            // key state this same frame - while it is up, Escape/Enter belong to it, not to us.
            if (HandbookViewer.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Vanilla's Escape binding fires on key-UP: starve it until the key is physically
                // released, or the pause menu opens over the world this Close just revealed.
                ModalInputChain.BeginEscSwallow();
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Next(); return; }
            // Nothing else is global on purpose: Backspace must stay a text-edit key, and arrow
            // keys belong to the fields.
        }

        // ================= navigation =================

        private static TutorialStep Current()
        {
            var all = TutorialSteps.All;
            if (all == null || all.Count == 0) return null;
            return all[Mathf.Clamp(_index, 0, all.Count - 1)];
        }

        private static void Next()
        {
            CaptureFields();
            var all = TutorialSteps.All;
            if (all == null || all.Count == 0) { Close(); return; }
            if (_index >= all.Count - 1) { Finish(); return; }
            _index++;
            RenderStep();
        }

        private static void Back()
        {
            CaptureFields();
            if (_index <= 0) return;
            _index--;
            RenderStep();
        }

        private static void Replay()
        {
            CaptureFields();
            _index = 0;
            RenderStep();
        }

        /// <summary>Next on the last step: the player has seen the whole thing.</summary>
        private static void Finish()
        {
            try { if (UIAConfig.TutorialCompleted != null) UIAConfig.TutorialCompleted.Value = true; }
            catch (Exception e) { UIALog.Warn("TutorialCompleted write failed: " + e.Message); }
            Close();
        }

        /// <summary>Skip counts as shown (confirmed policy, plan section 12.3): GuideShown was
        /// already set by the first-run trigger, so we only close. TutorialCompleted stays false -
        /// that flag means "read to the end", and the Guide tab's Replay button is the re-entry.</summary>
        private static void Skip()
        {
            Close();
        }

        // ================= build =================

        private static float WindowHeight(bool devEdit) { return devEdit ? WinHEdit : WinHRead; }

        private static void EnsureBuilt()
        {
            if (_root != null) return;

            _builtDevEdit = _devEdit;

            _root = new GameObject("UIAscended_TutorialCoach");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5600;   // above the Control Center (5200), below tooltips (6000)
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();   // the card's own controls are clickable

            // Full-screen scrim: absorbs every click behind the card (it does not itself close).
            UiaUi.Panel(_root.transform, UiaTheme.Scrim, "scrim");

            var winGo = UiaUi.Go("window", _root.transform);
            _window = (RectTransform)winGo.transform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(WinW, WindowHeight(_devEdit));
            // A REAL HUD glass panel, like the Control Center's window - it carries the F9 effect
            // stack (glow halo, edge light, ripple) so the card reads as part of the visor.
            _windowPanel = winGo.AddComponent<UI.Hud.PanelGraphic>();
            _windowPanel.raycastTarget = true;
            _windowPanel.color = UiaTheme.Window;
            _windowPanel.BorderColor = UiaTheme.Border;
            StyleWindowPanel();
            UiaUi.VLayout(_window, UiaTheme.Gap, (int)UiaTheme.Pad, (int)UiaTheme.Pad,
                (int)UiaTheme.Pad, (int)UiaTheme.Pad);

            BuildTitleBar(_window);

            // Rule under the title, then the step's own heading - centered, above the animation
            // (FlorpyDorp's 2026-08-02 layout: TITLE / rule / STEP NAME / demo / centered copy).
            var ruleGo = UiaUi.Go("rule", _window);
            UiaUi.Size(ruleGo, 2f);
            var rule = ruleGo.AddComponent<Image>();
            rule.color = UiaTheme.AccentDim;
            rule.raycastTarget = false;

            var headGo = UiaUi.Go("stepheading", _window);
            UiaUi.Size(headGo, 30f);
            _stepHeading = headGo.AddComponent<TextMeshProUGUI>();
            _stepHeading.font = UiaTheme.Font();
            _stepHeading.fontSize = UiaTheme.TitleSize + 2f;
            _stepHeading.color = UiaTheme.Accent;
            _stepHeading.alignment = TextAlignmentOptions.Center;
            _stepHeading.raycastTarget = false;
            _stepHeading.characterSpacing = 4f;
            _stepHeading.fontStyle = FontStyles.Bold;

            BuildDemoArea(_window);

            var bodyGo = UiaUi.Go("body", _window);
            _bodyHost = (RectTransform)bodyGo.transform;
            UiaUi.Size(bodyGo, flexH: 1f);
            UiaUi.VLayout(_bodyHost, 6f);

            var navGo = UiaUi.Go("nav", _window);
            _navHost = (RectTransform)navGo.transform;
            UiaUi.Size(navGo, NavH);
            UiaUi.HLayout(_navHost, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);

            _builtThemeHash = UiaMenuTheme.StyleHash();
            _root.SetActive(false);
        }

        private static void BuildTitleBar(RectTransform parent)
        {
            var barGo = UiaUi.Go("titlebar", parent);
            UiaUi.Size(barGo, UiaTheme.TitleH);
            var bar = (RectTransform)barGo.transform;

            // TRUE-centered title (FlorpyDorp's 2026-08-02 layout) - not an HLayout child, or the
            // right-side buttons would shove it off center: a full-bleed centered label with the
            // button cluster anchored to the right edge on top of it.
            var title = UiaUi.Text(bar, "UI ASCENDED TUTORIAL", UiaTheme.TitleSize, UiaTheme.Text,
                TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)title.transform);
            title.characterSpacing = 8f;
            title.fontStyle = FontStyles.Bold;

            var btnsGo = UiaUi.Go("titlebtns", bar);
            var btns = (RectTransform)btnsGo.transform;
            btns.anchorMin = new Vector2(1f, 0.5f);
            btns.anchorMax = new Vector2(1f, 0.5f);
            btns.pivot = new Vector2(1f, 0.5f);
            btns.anchoredPosition = Vector2.zero;
            btns.sizeDelta = new Vector2(76f, 30f);
            UiaUi.HLayout(btns, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleRight);

            // The pause control, F10-parity: lit while OUR latch holds the freeze, GREYED when a
            // pause cannot be owned (multiplayer, or vanilla already paused). The button state IS
            // the status - no "game is running" prose (FlorpyDorp 2026-08-02). Vanilla's own
            // PauseIcon sprite when the runtime grab finds it, ASCII "||" otherwise.
            _pauseBtn = UiaControls.Button(btns, "||", TogglePause, 34f, 30f,
                UiaControls.ButtonStyle.Panel);
            UiaControls.SetButtonIcon(_pauseBtn, Core.VanillaIcons.PauseIcon(), 13f);

            UiaControls.Button(btns, "X", Close, 34f, 30f, UiaControls.ButtonStyle.Panel);
            PaintChip();
        }

        private static void BuildDemoArea(RectTransform parent)
        {
            var hostGo = UiaUi.Go("demo-area", parent);
            UiaUi.Size(hostGo, DemoH);

            // Framed backdrop (behind the stage, so the demo draws on top).
            var frameGo = UiaUi.Go("demo-frame", hostGo.transform);
            var frame = (RectTransform)frameGo.transform;
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(DemoW, DemoH);
            frame.anchoredPosition = Vector2.zero;
            var frameImg = frameGo.AddComponent<Image>();
            frameImg.color = UiaTheme.Panel;
            frameImg.raycastTarget = false;
            UiaImages.Round(frameImg);
            frameGo.AddComponent<UiaGlassSkin>();   // themed edge light / ripple, no halo

            var stageGo = UiaUi.Go("demo-stage", hostGo.transform);
            var stage = (RectTransform)stageGo.transform;
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
            stage.pivot = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(DemoW, DemoH);
            stage.anchoredPosition = Vector2.zero;
            _demoHost = stage;

            try { _demo = TutorialDemoStage.Create(_demoHost); }
            catch (Exception e)
            {
                _demo = null;
                UIALog.Warn("TutorialDemoStage.Create failed (the coach still works, minus art): " + e.Message);
            }
        }

        /// <summary>Push this frame's theme colour + the F9 global effect stack onto the window
        /// glass, exactly as a HUD box updates each frame. Every setter is dirty-guarded.</summary>
        private static void StyleWindowPanel()
        {
            if (_windowPanel == null || _window == null) return;
            var size = _window.sizeDelta;
            float corner = UI.Hud.HudConfig.CornerRadius != null ? UI.Hud.HudConfig.CornerRadius.Value : 10f;
            _windowPanel.color = UiaTheme.Window;
            _windowPanel.BorderColor = UiaTheme.Border;
            _windowPanel.BorderWidth = UI.Hud.HudConfig.BorderWidth != null ? UI.Hud.HudConfig.BorderWidth.Value : 1.4f;
            _windowPanel.SetShape(size.x, size.y, corner);
            // Material-driven Tier B/C only look right while the HUD FX clock runs; frost is
            // CONSUMED when someone else keeps the backdrop alive, never demanded (single-writer
            // flag - see Close()).
            bool fxLive = UI.Hud.HudSystem.FxClockLive;
            UI.Hud.HudGlobalGlass.Apply(_windowPanel, includeGlow: true, wantFrost: fxLive, wantTierB: fxLive);
        }

        /// <summary>Rebuild the whole card after a live theme change (kit widgets freeze their
        /// colours at build time). Preserves step, mode and any in-progress edit text; the demo
        /// stage is recreated and re-Shown, so a theme drag mid-demo restarts that loop.</summary>
        private static void Restyle()
        {
            _lastRestyle = Time.unscaledTime;
            bool wasOpen = _open;
            CaptureFields();
            DestroyRoot();
            EnsureBuilt();
            if (_root == null) return;
            if (!wasOpen) return;
            _root.SetActive(true);
            RenderStep();
        }

        // ================= per-step render =================

        private static void RenderStep()
        {
            if (_bodyHost == null || _navHost == null) return;
            var all = TutorialSteps.All;
            if (all == null || all.Count == 0) return;
            _index = Mathf.Clamp(_index, 0, all.Count - 1);
            var step = all[_index];

            ShowDemo(step.DemoId);

            Clear(_bodyHost);
            Clear(_navHost);
            _bodyLabel = null;
            _headingField = null;
            _bodyField = null;
            _toast = null;

            if (_stepHeading != null)
                _stepHeading.text = Resolve(TutorialTextStore.Heading(step)).ToUpperInvariant();

            if (_devEdit) BuildEditBody(step, all.Count);
            else BuildReadBody(step, all.Count);
            BuildNav(all.Count);
            PaintChip();
        }

        private static void BuildReadBody(TutorialStep step, int count)
        {
            // The step heading lives ABOVE the demo now (_stepHeading, set in RenderStep) -
            // the body is just the centered copy.
            var textGo = UiaUi.Go("copy", _bodyHost);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.font = UiaTheme.Font();
            t.fontSize = UiaTheme.LabelSize;
            t.color = UiaTheme.Text;
            t.alignment = TextAlignmentOptions.Top;   // horizontally centered, top-anchored
            t.raycastTarget = false;
            t.richText = false;                 // a '<' in dev-edited copy is text, not markup
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = new Vector4(2f, 2f, 2f, 2f);
            // Auto-size guards against a dev-edited step that runs long: it shrinks rather than
            // spilling out of the card.
            t.enableAutoSizing = true;
            t.fontSizeMin = 11f;
            t.fontSizeMax = UiaTheme.LabelSize;
            t.text = DisplayBody(step);
            // An EXPLICIT preferred height, not the TMP's own: auto-sizing text inside a layout
            // group whose height it also feeds is the classic TMP oscillation. The label owns a
            // fixed slot (plus any slack) and shrinks its glyphs inside it.
            UiaUi.Size(textGo, 96f, flexH: 1f);
            var le = textGo.GetComponent<LayoutElement>();
            if (le != null) le.minHeight = 60f;
            _bodyLabel = t;

            BuildProgressRow(count);

            if (_index == count - 1) BuildFinishExtras();
        }

        private static void BuildEditBody(TutorialStep step, int count)
        {
            _headingField = TutorialTextField.Make(_bodyHost, TutorialTextStore.Heading(step),
                "Step heading", false, 80);
            UiaUi.Size(_headingField.gameObject, UiaTheme.RowH);

            _bodyField = TutorialTextField.Make(_bodyHost, TutorialTextStore.Body(step),
                "Step body copy - ASCII only", true, 900);
            UiaUi.Size(_bodyField.gameObject, 110f, flexH: 1f);
            var ble = _bodyField.gameObject.GetComponent<LayoutElement>();
            if (ble != null) ble.minHeight = 96f;

            BuildProgressRow(count);

            // Save / Revert / Reset row + the fading confirmation label.
            var rowGo = UiaUi.Go("edit-actions", _bodyHost);
            UiaUi.Size(rowGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            UiaControls.Button(rowGo.transform, "Save", DoSave, 92f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
            UiaControls.Button(rowGo.transform, "Revert step", DoRevertStep, 120f, UiaTheme.RowH);
            UiaControls.Button(rowGo.transform, "Reset all", DoResetAll, 108f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
            _toast = UiaUi.Text(rowGo.transform, "", UiaTheme.SmallSize, UiaTheme.Good, TextAlignmentOptions.Left);
            UiaUi.Size(_toast.gameObject, UiaTheme.RowH, 200f, flexW: 1f);
            var tc = _toast.color; tc.a = 0f; _toast.color = tc;

            // Literal token text - deliberately NOT run through Resolve().
            UiaControls.Note(_bodyHost,
                "Tokens: {UIA_Grid} {V:SmartStow} - ASCII only - saved to config/StationeersUIMod/Tutorial. "
                + "Back/Next keeps your edits in memory; Save writes to disk.");
        }

        private static void BuildProgressRow(int count)
        {
            var rowGo = UiaUi.Go("progress", _bodyHost);
            UiaUi.Size(rowGo, 20f);
            UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);

            var dotsGo = UiaUi.Go("dots", rowGo.transform);
            UiaUi.Size(dotsGo, 20f, 200f, flexW: 1f);
            UiaUi.HLayout((RectTransform)dotsGo.transform, 4f, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            for (int i = 0; i < count; i++)
            {
                var dotGo = UiaUi.Go("dot", dotsGo.transform);
                UiaUi.Size(dotGo, 8f, 8f, flexW: 0f);
                var img = dotGo.AddComponent<Image>();
                img.raycastTarget = false;
                UiaImages.Round(img);
                img.color = i == _index ? UiaTheme.Selected
                          : (i < _index ? UiaTheme.AccentDim : UiaTheme.Off);
            }

            var lbl = UiaUi.Text(rowGo.transform, "Step " + (_index + 1) + " of " + count,
                UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.Right);
            UiaUi.Size(lbl.gameObject, 20f, 130f, flexW: 0f);
        }

        /// <summary>Step 15's two extra affordances, above the nav row (plan section 6, step 15).</summary>
        private static void BuildFinishExtras()
        {
            var rowGo = UiaUi.Go("finish-extras", _bodyHost);
            UiaUi.Size(rowGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            UiaControls.Button(rowGo.transform, "Open the Designer Handbook", OpenHandbook, 250f, UiaTheme.RowH);
            UiaControls.Button(rowGo.transform, "Replay tutorial", Replay, 150f, UiaTheme.RowH);
            var spacer = UiaUi.Go("spacer", rowGo.transform);
            UiaUi.Size(spacer, UiaTheme.RowH, 0f, flexW: 1f);
        }

        private static void BuildNav(int count)
        {
            UiaControls.Button(_navHost, "Skip tutorial", Skip, 140f, NavH - 4f);

            var spacer = UiaUi.Go("spacer", _navHost);
            UiaUi.Size(spacer, NavH - 4f, 0f, flexW: 1f);

            var back = UiaControls.Button(_navHost, "Back", Back, 100f, NavH - 4f);
            if (back != null) back.SetEnabled(_index > 0);

            bool last = _index >= count - 1;
            UiaControls.Button(_navHost, last ? "Got it - start playing" : "Next", Next,
                last ? 220f : 110f, NavH - 4f, UiaControls.ButtonStyle.Primary);
        }

        /// <summary>Point the stage at this step's scene. Steps 4 and 5 deliberately share the
        /// belt-wheel scene (plan section 7); the stage's own Show() is a documented no-op when the
        /// id is already on screen, so the loop keeps running across that boundary and we do not
        /// need to de-dupe here.</summary>
        private static void ShowDemo(string demoId)
        {
            if (_demo == null || string.IsNullOrEmpty(demoId)) return;
            try { _demo.Show(demoId); }
            catch (Exception e) { UIALog.Warn("Tutorial demo '" + demoId + "' failed to show: " + e.Message); }
        }

        // ================= copy resolution =================

        /// <summary>The body text to draw: the store's copy (override or shipped), with the
        /// welcome step's multiplayer variant applied when we are NOT holding the pause, then all
        /// glyph tokens resolved. The MP swap is skipped in edit mode and whenever the step has a
        /// hand-written override - a dev editing that step must see exactly what they typed.</summary>
        private static string DisplayBody(TutorialStep step)
        {
            if (step == null) return "";
            string raw = TutorialTextStore.Body(step);
            if (!_devEdit && step.Id == "welcome" && !PauseHeld()
                && !TutorialTextStore.HasOverride(step.Id))
                raw = raw.Replace(PausedLine, RunningLine);
            return Resolve(raw);
        }

        private static void RepaintBodyText()
        {
            if (_bodyLabel == null) return;
            var step = Current();
            if (step == null) return;
            _bodyLabel.text = DisplayBody(step);
        }

        /// <summary>Replace every <c>{token}</c> with the player's CURRENT key glyph, in brackets.
        /// Re-run on every step render, so a rebind between steps is picked up. Unresolvable
        /// tokens degrade to their own inner name rather than throwing.</summary>
        private static string Resolve(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            if (s.IndexOf('{') < 0) return s;
            var sb = new StringBuilder(s.Length + 24);
            int pos = 0;
            while (pos < s.Length)
            {
                int open = s.IndexOf('{', pos);
                if (open < 0) { sb.Append(s, pos, s.Length - pos); break; }
                int close = s.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(s, pos, s.Length - pos); break; }
                sb.Append(s, pos, open - pos);
                sb.Append(GlyphFor(s.Substring(open + 1, close - open - 1)));
                pos = close + 1;
            }
            return sb.ToString();
        }

        private static string GlyphFor(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            string inner = token;
            string glyph = null;
            try
            {
                if (token.StartsWith("V:", StringComparison.Ordinal))
                {
                    // A VANILLA button, live from the game's own registry (GLOBAL namespace).
                    inner = token.Substring(2);
                    KeyCode k = KeyManager.GetKey(inner);
                    if (k != KeyCode.None) glyph = UiaKeybinds.Glyph(k);
                }
                else
                {
                    UiaKeybinds.EnsureBuilt();
                    if (UiaKeybinds.Find(token) != null)
                    {
                        KeyCode k = UiaKeybinds.Key(token);
                        if (k != KeyCode.None) glyph = UiaKeybinds.Glyph(k);
                    }
                }
            }
            catch { glyph = null; }
            if (string.IsNullOrEmpty(glyph) || glyph == "-") glyph = Friendly(inner);
            return "[" + glyph + "]";
        }

        /// <summary>Fallback label for an unbound/unknown token: the bare id, minus our own
        /// "UIA_" prefix so it at least reads like a control name.</summary>
        private static string Friendly(string inner)
        {
            if (string.IsNullOrEmpty(inner)) return "?";
            return inner.StartsWith("UIA_", StringComparison.Ordinal) ? inner.Substring(4) : inner;
        }

        // ================= edit mode plumbing =================

        /// <summary>Auto-apply the fields to the IN-MEMORY override before navigating (documented
        /// in the hint line: Save is what writes to disk). Text identical to the shipped copy is
        /// stored as "no override", so simply paging through edit mode never bloats the file.</summary>
        private static void CaptureFields()
        {
            if (!_devEdit) return;
            if (_headingField == null || _bodyField == null) return;
            var step = Current();
            if (step == null) return;
            string h = _headingField.text ?? "";
            string b = _bodyField.text ?? "";
            if (h == (step.DefaultHeading ?? "") && b == (step.DefaultBody ?? ""))
            {
                if (TutorialTextStore.HasOverride(step.Id)) TutorialTextStore.ClearOverride(step.Id);
                return;
            }
            TutorialTextStore.SetOverride(step.Id, h, b);
        }

        private static void DoSave()
        {
            CaptureFields();
            Toast(TutorialTextStore.Save()
                ? "saved"
                : "SAVE FAILED - see the log (file locked or folder read-only?)");
        }

        private static void DoRevertStep()
        {
            var step = Current();
            if (step == null) return;
            TutorialTextStore.ClearOverride(step.Id);
            RenderStep();          // fields reload from the shipped copy
            Toast("step reverted (not saved yet)");
        }

        private static void DoResetAll()
        {
            TutorialTextStore.ResetAll();
            RenderStep();
            Toast("all steps reset - file deleted");
        }

        private static void Toast(string msg)
        {
            if (_toast == null) return;
            _toast.text = msg ?? "";
            var c = UiaTheme.Good; c.a = 1f;
            _toast.color = c;
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        /// <summary>Fade the confirmation label out over its final second. Unscaled - the game is
        /// paused behind us.</summary>
        private static void ToastTick()
        {
            if (_toast == null) return;
            float left = _toastUntil - Time.unscaledTime;
            float a = left <= 0f ? 0f : Mathf.Clamp01(left);
            var c = _toast.color;
            if (Mathf.Abs(c.a - a) > 0.004f) { c.a = a; _toast.color = c; }
        }

        private static void OpenHandbook()
        {
            try { HandbookViewer.Open(); }
            catch (Exception e) { UIALog.Warn("HandbookViewer.Open failed: " + e.Message); }
        }

        // ================= pause chip =================

        private static bool PauseHeld()
        {
            try { return GamePause.Held; }
            catch { return false; }
        }

        /// <summary>Repaint the header pause button from the live world state: lit while OUR
        /// latch holds the freeze, greyed when a pause cannot be owned (multiplayer, or a
        /// vanilla-owned pause) - same truth-table as the F10 header button.</summary>
        private static void PaintChip()
        {
            if (_pauseBtn == null) return;
            bool held = PauseHeld();
            _pauseBtn.SetSelected(held);
            _pauseBtn.SetEnabled(held || (GamePause.CanOwnPause() && !WorldManager.IsGamePaused));
        }

        private static void TogglePause()
        {
            if (GamePause.Held) GamePause.Release(PauseReason);
            else GamePause.Hold(PauseReason, InputStateKey);
            PaintChip();
            RepaintBodyText();   // the welcome step's copy differs paused vs running
        }

        /// <summary>Stored delegate so the unsubscribe in <see cref="UnhookPause"/> matches - an
        /// anonymous method here would leak a subscription into the dead assembly on F6.</summary>
        private static void HookPause()
        {
            if (_pausedHandler != null) return;
            _pausedHandler = OnPausedChanged;
            try { GamePause.PausedChanged += _pausedHandler; }
            catch (Exception e) { _pausedHandler = null; UIALog.Warn("GamePause.PausedChanged hook failed: " + e.Message); }
        }

        private static void UnhookPause()
        {
            if (_pausedHandler == null) return;
            try { GamePause.PausedChanged -= _pausedHandler; }
            catch { }
            _pausedHandler = null;
        }

        /// <summary>Anything else unpausing (the player round-tripped Esc) repaints the chip - and
        /// the welcome step's copy, whose third sentence depends on it.</summary>
        private static void OnPausedChanged(bool paused)
        {
            PaintChip();
            RepaintBodyText();
        }

        // ================= modal =================

        private static void AcquireModal()
        {
            if (_modalHeld) return;
            _modalHeld = true;
            try { KeyManager.SetInputState(InputStateKey, KeyInputState.Typing); } catch { }
            try { MouseModeController.AddModal(_modal); } catch { }
            CursorBlockArbiter.Hold(CursorHoldId);
        }

        private static void ReleaseModal()
        {
            if (!_modalHeld) return;
            _modalHeld = false;
            try { KeyManager.RemoveInputState(InputStateKey); } catch { }
            // If our key was current, KeyManager pops to the map's LAST entry - which is
            // "WorldManager"/Paused whenever a pause is still latched (e.g. F10's own) - leaving
            // a still-open layer underneath in the wrong state. Hand Typing back explicitly.
            ModalInputChain.ReassertTop();
            try { MouseModeController.RemoveModal(_modal); } catch { }
            CursorBlockArbiter.Release(CursorHoldId);
            // Only re-lock the cursor when no other UIA surface still holds it - the Guide tab
            // opens the coach OVER the F10 window, and closing must not yank F10's free cursor.
            if (CursorBlockArbiter.AnyHold) return;
            try
            {
                if (CursorManager.Instance != null)
                    CursorManager.Instance.OnApplicationFocus(true);   // re-lock the cursor next frame
            }
            catch { }
        }

        // ================= teardown =================

        private static void Clear(RectTransform host)
        {
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var c = host.GetChild(i);
                c.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        private static void DestroyRoot()
        {
            if (_demo != null)
            {
                try { _demo.Destroy(); } catch (Exception e) { UIALog.Warn("Tutorial demo teardown: " + e.Message); }
                _demo = null;
            }
            if (_windowPanel != null) { try { UI.Hud.HudFxMaterials.Unassign(_windowPanel); } catch { } }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _window = null;
            _windowPanel = null;
            _demoHost = null;
            _bodyHost = null;
            _navHost = null;
            _pauseBtn = null;
            _stepHeading = null;
            _bodyLabel = null;
            _toast = null;
            _headingField = null;
            _bodyField = null;
        }

        /// <summary>Hot-reload / plugin teardown. Must leave NOTHING behind: no canvas, no borrowed
        /// pause, no modal input state, no live event subscription, no cached text.</summary>
        internal static void Shutdown()
        {
            UnhookPause();
            try { GamePause.Release(PauseReason); } catch { }
            ReleaseModal();
            _open = false;
            DestroyRoot();
            _index = 0;
            _devEdit = false;
            _builtDevEdit = false;
            _builtThemeHash = 0;
            _lastRestyle = 0f;
            _toastUntil = 0f;
            TutorialTextStore.Shutdown();
        }
    }
}
