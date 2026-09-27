using System;
using Assets.Scripts;
using Assets.Scripts.UI;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// The tutorial COACH - since 0.9.8.0 a single-card presenter. <c>TutorialDirector</c> does all
    /// the sequencing (lessons, Watch mode's Back/Next, the F10 tour) and hands the coach one
    /// <see cref="TCardSpec"/> at a time: <see cref="OpenCard"/> for a modal CARD,
    /// <see cref="OpenCallout"/> for an F10-tour CALLOUT pinned beside a menu anchor. It no longer
    /// knows any step list (the old 15-step <c>TutorialSteps</c> dependency is gone).
    ///
    /// <para><b>A sibling of the Control Center, deliberately.</b> Own ScreenSpaceOverlay canvas, a
    /// scrim that absorbs clicks, a window whose surface is a real HUD <see cref="PanelGraphic"/>
    /// carrying the F9 global effect stack on the analytic panel renderer the HUD boxes use
    /// (<see cref="TutorialStrip.ApplyGlass"/>, shared with the lesson strip), and every colour
    /// read from <see cref="UiaTheme"/> so the card re-skins with the active UI Theme (a live theme
    /// change rebuilds it - <see cref="UiaMenuTheme.StyleHash"/> poll, throttled).</para>
    ///
    /// <para><b>Sort orders.</b> CARD 5600 (over F10 5200 and the drag ghosts 5250, under the
    /// Handbook 5700, vanilla's F2 helper-hints panel lifted to 5800 (<c>Core/HelperHintsLift</c>,
    /// dropped back while a vanilla menu is front), the F9 ImGui lift 5900 and tooltips 6000).
    /// CALLOUT 5300: over F10 (the tour
    /// lives inside it), under the coach's own card layer; a callout draws NO scrim (F10 has one)
    /// and outlines its anchor with a pulsing accent ring + a drawn pointer on this canvas (the
    /// spotlight's 5140 would sit under F10). KNOWN TIE: the SmartStow capture dialog hosted over
    /// F10 is also 5300 - it is opened by a player gesture on the SmartStow tab, not during the
    /// tour.</para>
    ///
    /// <para><b>Scrim with a hole.</b> A card with <see cref="TCardSpec.HasHole"/> swaps the
    /// full-screen scrim for four dark panels around the hole (expanded 14 px so a spotlight ring
    /// shows through undimmed) and slides off it if they would overlap.</para>
    ///
    /// <para><b>Pause.</b> A card with <see cref="TCardSpec.Pause"/> latches the game paused through
    /// <see cref="GamePause"/> (reason "tutorial"; single-player only, and only while
    /// <c>UIAConfig.TutorialAutoPause</c> is on). Every clock here and in the demo stage is
    /// <c>Time.unscaledTime</c> - a <c>Time.time</c> animation would freeze behind the card. The
    /// header pause button is F10-parity: lit while our latch holds, greyed in multiplayer. Card to
    /// card (OpenCard while open) the latch is KEPT - never a one-frame unpause between cards.</para>
    ///
    /// <para><b>Unwind order (load-bearing).</b> Close marks the coach closed FIRST, then releases the
    /// PAUSE, then the modal input state - so neither the pause release's
    /// <see cref="GamePause.PausedChanged"/> repaint nor <see cref="ModalInputChain.ReassertTop"/>
    /// sees the coach as still up, and Typing goes to the layer underneath instead of back to ours
    /// (reference section 20). Esc closes on key-DOWN and starts the
    /// <see cref="ModalInputChain.BeginEscSwallow"/>, so vanilla's key-UP Escape never opens the
    /// pause menu over the world this close just revealed.</para>
    ///
    /// <para><b>Dev edit (uiadev).</b> While <see cref="UiaDevMode.Active"/>, a card whose step
    /// owns a heading/body field shows them as text fields that write through
    /// <c>TutorialTextStore.Set</c> (+ Save) when a field is left - to the keys the spec names
    /// (<see cref="TCardSpec.HeadingKey"/> / <see cref="TCardSpec.BodyKey"/>, default
    /// "&lt;StepId&gt;|heading" / "|body"), so the Welcome's body@mp / body@running variant edits the
    /// line actually shown - and every card/callout with a step id shows a small EDIT button that
    /// opens <c>TutorialEditorWindow.OpenAt(StepId)</c>. Fields show the RAW stored text (tokens
    /// intact), never the resolved glyphs, so an edit can never bake "[G]" over "{V:SmartStow}".
    /// ONLY A FIELD THE PLAYER TYPED INTO IS EVER WRITTEN (a per-field dirty flag from its change
    /// callback): a card re-presented because the Lesson Editor (F8) changed the store rebuilds its
    /// fields FROM the store instead of writing their stale text back over the editor's edit.</para>
    ///
    /// <para><b>Hot reload.</b> <see cref="Shutdown"/> clears every static: the canvas is destroyed,
    /// the demo stage torn down, the <see cref="GamePause.PausedChanged"/> subscription dropped, the
    /// pause latch and modal released, and no Director callback is ever invoked from teardown.</para>
    /// </summary>
    internal static class TutorialCoach
    {
        private sealed class Modal : IModal { public bool UnlockCursor => true; }

        internal const string InputStateKey = "UIA_Tutorial";   // ModalInputChain reasserts by this name
        private const string PauseReason = "tutorial";
        private const string CursorHoldId = "tutorial";

        private const int CardSortOrder = 5600;
        private const int CalloutSortOrder = 5300;

        // ---- card metrics (reference px at 1080p; the CanvasScaler scales) ----
        private const float WinW = 720f;
        private const float DemoW = 680f;
        private const float DemoH = 300f;
        private const float HeadingH = 30f;
        private const float BodyReadH = 132f;
        private const float BodyEditH = 214f;
        private const float NavH = 36f;
        private const float EditBtnW = 60f;
        // ---- callout metrics ----
        private const float CalloutW = 380f;
        private const float CalloutH = 200f;
        private const float CalloutHeadH = 24f;
        private const float MiniW = 150f, MiniH = 76f;   // a callout's optional demo: the strip's mini layout
        private const float CalloutPad = 12f;
        private const float CalloutGap = 16f;
        private const float EdgeMargin = 12f;
        private const float AnchorPad = 6f;
        private const float PointerSize = 14f;
        // ---- hole / motion ----
        private const float HoleMargin = 14f;
        private const float RestyleMinInterval = 0.14f;   // same throttle as UiaControlCenter
        private const float ToastSeconds = 2.4f;
        private const float EnterGuardSec = 0.25f;        // a fresh card ignores Enter this long
        private const float RingPulseHz = 1.2f;

        /// <summary>The body sentences that claim a freeze. When no pause is held they are swapped
        /// for <see cref="TCardSpec.RunningLine"/> (multiplayer) or <see cref="RunningFallback"/>.</summary>
        private static readonly string[] PauseSentences =
        {
            "The game is paused while you read.",
            "The game is paused.",
        };
        private const string RunningFallback = "Your game is still running.";

        private static readonly Modal _modal = new Modal();
        private static readonly Func<string, string> GlyphFn = TutorialTokens.Glyph;

        private enum Mode : byte { None, Card, Callout }

        // ---- built objects ----
        private static GameObject _root;
        private static Canvas _canvas;
        private static RectTransform _rootRt;
        private static Image _scrimFull, _scrimTop, _scrimBottom, _scrimLeft, _scrimRight;

        private static RectTransform _card;
        private static PanelGraphic _cardPanel;
        private static RectTransform _titleBtns;
        private static GameObject _editBtnCard;
        private static UiaControls.UiaButton _pauseBtn;
        private static TextMeshProUGUI _cardHeading;
        private static GameObject _demoArea;
        private static RectTransform _demoHost;
        private static RectTransform _cardBody;
        private static RectTransform _cardNav;
        private static TutorialDemoStage _demo;

        private static RectTransform _callout;
        private static PanelGraphic _calloutPanel;
        private static RectTransform _calloutContent;
        private static GameObject _calloutDemoArea;
        private static TutorialDemoStage _calloutDemo;
        private static RectTransform _pointer;
        private static TriangleGraphic _pointerG;
        private static RectTransform _ring;
        private static PanelGraphic _ringG;
        private static CanvasGroup _ringGroup;

        // ---- per-render widgets ----
        private static TextMeshProUGUI _bodyLabel;
        private static TMP_InputField _headingField, _bodyField;
        private static string _editHeadingKey, _editBodyKey;
        // The player typed into this field since it was built / last committed. The ONLY licence to
        // write it back: a clean field's text may be stale (the F8 editor changed the store since).
        private static bool _headingDirty, _bodyDirty;
        private static TextMeshProUGUI _toast, _countLabel;

        // ---- state ----
        private static TCardSpec _spec;
        private static Mode _mode;
        private static Rect _anchorScreen;
        private static string _liveHeading, _liveBody;   // resolved display text (inline edits update it)
        private static bool _open;
        private static bool _modalHeld;
        private static bool _builtDev;
        private static bool _rendering;
        private static int _gen;
        private static int _builtThemeHash;
        private static float _lastRestyle;
        private static float _toastUntil;
        private static float _openedAt;
        private static float _placedScale = -1f;
        private static int _placedW, _placedH;
        private static int _lastFieldFocusFrame = -999;
        private static Action<bool> _pausedHandler;

        /// <summary>True while a card OR a callout is up (the historical name every modal gate in
        /// the mod reads).</summary>
        internal static bool IsOpen { get { return _open; } }

        /// <summary>True while a card or a callout is up (contract name).</summary>
        internal static bool CardOpen { get { return _open; } }

        /// <summary>True while the up card is an F10-tour callout.</summary>
        internal static bool CalloutOpen { get { return _open && _mode == Mode.Callout; } }

        /// <summary>The step id of the card on screen, or null.</summary>
        internal static string CurrentStepId { get { return _open && _spec != null ? _spec.StepId : null; } }

        // ================= open / close =================

        /// <summary>Present <paramref name="spec"/> as a modal CARD (sort 5600, scrim, optional hole).
        /// While a card is already up the content swaps in place and the pause latch is kept.</summary>
        internal static void OpenCard(TCardSpec spec)
        {
            if (spec == null) return;
            Present(spec, Mode.Card, default(Rect));
        }

        /// <summary>Present <paramref name="spec"/> as a 380x200 CALLOUT beside
        /// <paramref name="anchorScreenRect"/> (screen px, bottom-left origin - what
        /// <c>UiaControlCenter.TryGetAnchorRect</c> returns). Sort 5300, no scrim of its own, a
        /// pulsing ring on the anchor. A zero rect centres the callout with no ring. Closes itself
        /// (as "Later") if F10 closes under it.</summary>
        internal static void OpenCallout(TCardSpec spec, Rect anchorScreenRect)
        {
            if (spec == null) return;
            Present(spec, Mode.Callout, anchorScreenRect);
        }

        /// <summary>Close the card/callout and give everything back (open flag, then pause, then
        /// modal). Invokes no callback.</summary>
        internal static void CloseCard()
        {
            _gen++;
            CloseInternal();
        }

        /// <summary>Historical name of <see cref="CloseCard"/>.</summary>
        internal static void Close() { CloseCard(); }

        /// <summary>COMPAT SHIM for pre-0.9.8.0 callers (<c>uiatutorial [edit]</c>, the old Guide tab
        /// button): the coach no longer owns a step list. <paramref name="devEdit"/> opens the lesson
        /// editor; otherwise First Steps replays in Watch mode through the Director. New code calls
        /// the Director / <see cref="OpenCard"/> directly - delete this once nothing references it.</summary>
        [Obsolete("0.9.8.0: the Director sequences lessons - call TutorialDirector.PlayLesson / TutorialEditorWindow.Toggle.")]
        internal static void Open(bool devEdit = false)
        {
            try
            {
                if (devEdit) TutorialEditorWindow.Toggle();
                else TutorialDirector.PlayLesson("core", true);
            }
            catch (Exception e) { UIALog.Warn("TutorialCoach.Open (compat shim) failed: " + e.Message); }
        }

        /// <summary>RETIRED: the first-run Welcome now belongs to <c>TutorialDirector.Tick()</c>
        /// (contract section 8). Kept only as a deliberate NO-OP so a not-yet-migrated caller still
        /// compiles without double-opening anything; the Obsolete warning marks the call for
        /// deletion.</summary>
        [Obsolete("0.9.8.0: the first-run block moved into TutorialDirector.Tick() - delete this call.")]
        internal static void OpenFirstRun() { }

        private static void Present(TCardSpec spec, Mode mode, Rect anchor)
        {
            _gen++;
            // Whatever the player TYPED into an in-place dev field of the OUTGOING card survives the
            // swap (written to that card's key). An untouched field writes nothing: this is also the
            // re-present the Lesson Editor triggers on every keystroke (RefreshActiveText -> OpenCard),
            // and its fields hold the text from BEFORE that edit - the render below rebuilds them from
            // the store instead.
            if (_open) { try { CommitFields(); } catch { } }
            try { EnsureBuilt(); }
            catch (Exception e)
            {
                // A half-built root would be a screen-filling click blocker with no modal held and
                // no Escape (Update only pumps while _open). Tear down whatever got created.
                UIALog.Error("TutorialCoach build failed: " + e);
                try { DestroyRoot(); } catch { }
                if (_open) CloseInternal();
                return;
            }
            if (_root == null) return;

            bool wasOpen = _open;
            _spec = spec;
            _mode = mode;
            _anchorScreen = anchor;
            _liveHeading = Tok(spec.Heading);
            _liveBody = Tok(spec.Body);
            _open = true;
            if (!_root.activeSelf) _root.SetActive(true);
            if (!wasOpen) _openedAt = Time.unscaledTime;
            AcquireModal();
            HookPause();
            ApplyPause(spec.Pause);
            SafeRender();
        }

        /// <summary>Hold or drop OUR pause reason for the card now up. Holding is skipped in
        /// multiplayer (and when TutorialAutoPause is off); dropping it while the modal stays
        /// held hands Typing straight back to us (SetGamePause(false) pops KeyManager's map).</summary>
        private static void ApplyPause(bool want)
        {
            bool hold = want && AutoPauseOn();
            try
            {
                if (hold)
                {
                    if (GamePause.CanOwnPause()) GamePause.Hold(PauseReason, InputStateKey);
                }
                else if (GamePause.Held)
                {
                    GamePause.Release(PauseReason);
                    if (_open) ModalInputChain.ReassertTop();
                }
                else GamePause.Release(PauseReason);
            }
            catch (Exception e) { UIALog.Warn("TutorialCoach pause: " + e.Message); }
        }

        private static bool AutoPauseOn()
        {
            try { return UIAConfig.TutorialAutoPause == null || UIAConfig.TutorialAutoPause.Value; }
            catch { return true; }
        }

        /// <summary>Close and give everything back. ORDER MATTERS: the open flag first (so nothing the
        /// pause release triggers - the PausedChanged repaint, ModalInputChain.ReassertTop - treats the
        /// coach as still up), then the pause latch, then the modal input state (see the class
        /// remarks).</summary>
        private static void CloseInternal()
        {
            if (!_open) return;
            try { CommitFields(); } catch { }
            _open = false;
            try { GamePause.Release(PauseReason); } catch (Exception e) { UIALog.Warn("TutorialCoach pause release: " + e.Message); }
            UnhookPause();
            // Symmetric with UiaControlCenter.Close: drop the shared glass/edgefx material now so a
            // reopen cannot flash one stale frame. (We never write HudGlobalGlass.FrostDemand - it is
            // single-writer, owned per frame by the Control Center; GridTheme sets the precedent.)
            if (_cardPanel != null) { try { HudFxMaterials.Unassign(_cardPanel); } catch { } }
            if (_calloutPanel != null) { try { HudFxMaterials.Unassign(_calloutPanel); } catch { } }
            if (_root != null) _root.SetActive(false);
            ReleaseModal();
            _spec = null;
            _mode = Mode.None;
            _anchorScreen = default(Rect);
            _liveHeading = _liveBody = null;
        }

        /// <summary>Esc / X / F10-under-a-callout: close FIRST, then tell the Director "Later".</summary>
        private static void Later()
        {
            var spec = _spec;
            CloseCard();
            var cb = spec != null ? spec.OnLater : null;
            if (cb == null) return;
            try { cb(); }
            catch (Exception e) { UIALog.Warn("TutorialCoach: OnLater failed: " + e.Message); }
        }

        /// <summary>The world went away / F10 closed under a callout: close and report it as Later,
        /// so the Director never waits on a card that is gone.</summary>
        private static void SystemClose() { Later(); }

        /// <summary>A card button. The Director answers with OpenCard (next card) or CloseCard; if
        /// it does neither, the press still ends the card - a button must never go dead.</summary>
        private static void Press(int index)
        {
            if (!_open || _spec == null) return;
            var spec = _spec;
            if (spec.Buttons == null || spec.Buttons.Length == 0) { Later(); return; }
            try { CommitFields(); } catch { }
            int gen = _gen;
            var cb = spec.OnButton;
            if (cb != null)
            {
                try { cb(index); }
                catch (Exception e) { UIALog.Warn("TutorialCoach: OnButton(" + index + ") failed: " + e.Message); }
            }
            if (_open && _gen == gen) CloseCard();
        }

        private static void PressPrimary()
        {
            if (_spec == null) return;
            if (_spec.Buttons == null || _spec.Buttons.Length == 0) { Later(); return; }
            Press(Mathf.Clamp(_spec.PrimaryIndex, 0, _spec.Buttons.Length - 1));
        }

        // ================= per-frame pump =================

        /// <summary>Pumped every frame from StationeersUIMod.Update (like the Control Center).
        /// Drives the glass, the demo, the anchor ring, the theme poll and the keys (Esc, Enter,
        /// and the F10 key under a callout).</summary>
        internal static void Update()
        {
            if (!_open) return;

            StylePanels();
            // Keep the pause button honest with no event (a client joining flips CanOwnPause
            // silently) - two dirty-guarded setters, F10 does the same.
            PaintChip();
            TickDemo();
            ToastTick();
            TickRing();
            CheckReplace();

            // If the world goes away (menu / loading / unload), never leave the card stranded -
            // and never leave the pause latched behind it.
            if (!Guards.CanDraw()) { SystemClose(); return; }
            // A callout belongs to F10: it goes when the menu goes (its X, a tab click that closes...).
            if (_mode == Mode.Callout && !UiaControlCenter.IsOpen) { SystemClose(); return; }

            // Follow the live theme exactly as UiaControlCenter does: poll the resolved-colour hash
            // and rebuild, throttled so an F9 colour-wheel drag cannot rebuild the canvas 60x/s.
            if (UiaMenuTheme.StyleHash() != _builtThemeHash
                && Time.unscaledTime - _lastRestyle >= RestyleMinInterval)
            {
                Restyle();
                return;
            }
            // uiadev toggled while a card is up: re-render with / without the edit affordances.
            if (DevActive() != _builtDev) { SafeRender(); return; }

            // Edit mode: while a field owns the keyboard, Enter is a newline and Esc is TMP's own
            // deselect - neither may act on the card. TMP deactivates a single-line field on the
            // same frame it handles submit, so keys are also swallowed for one frame after a field
            // loses focus - otherwise the Enter that commits the heading would ALSO press a button.
            // The Lesson Editor (F8, ImGui) counts too: typing a line break or pressing Esc in one of
            // its fields must never page or close the card underneath it.
            if ((_builtDev && (TutorialTextField.Focused(_headingField) || TutorialTextField.Focused(_bodyField)))
                || TutorialEditorWindow.OwnsKeyboard)
            {
                _lastFieldFocusFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount - _lastFieldFocusFrame <= 1) return;

            // The console, vanilla input windows and the creative menu read the same raw keys -
            // while one is up, nav keys are not ours (CanDraw was already checked above).
            if (!Guards.CanToggleMenus()) return;

            // The Handbook viewer stacks ABOVE the coach (5700 vs 5600) and reads the same raw key
            // state this same frame - while it is up, Escape/Enter belong to it, not to us.
            if (HandbookViewer.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Vanilla's Escape binding fires on key-UP: starve it until the key is physically
                // released, or the pause menu opens over the world this close just revealed.
                ModalInputChain.BeginEscSwallow();
                Later();
                return;
            }
            // Under a callout the F10 key closes the tour AND the menu: this Update runs before the
            // plugin's F10 toggle check, which then sees the coach closed and toggles F10 shut.
            if (_mode == Mode.Callout && MenuKeyDown()) { Later(); return; }
            if (Time.unscaledTime - _openedAt < EnterGuardSec) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { PressPrimary(); return; }
            // Nothing else is global on purpose: Backspace must stay a text-edit key, and arrow keys
            // belong to the fields.
        }

        private static bool MenuKeyDown()
        {
            try { return UIAConfig.SettingsWindowKey != null && Input.GetKeyDown(UIAConfig.SettingsWindowKey.Value); }
            catch { return false; }
        }

        private static bool DevActive()
        {
            try { return UiaDevMode.Active; } catch { return false; }
        }

        private static void TickDemo()
        {
            if (_mode == Mode.Callout)
            {
                if (_calloutDemo == null || _calloutDemoArea == null || !_calloutDemoArea.activeSelf) return;
                try { _calloutDemo.Tick(); }
                catch (Exception e)
                {
                    UIALog.Warn("Tutorial callout demo tick failed: " + e.Message);
                    try { _calloutDemo.Destroy(); } catch { }
                    _calloutDemo = null;
                    _calloutDemoArea.SetActive(false);
                }
                return;
            }
            if (_demo == null || _mode != Mode.Card || _demoArea == null || !_demoArea.activeSelf) return;
            try { _demo.Tick(); }
            catch (Exception e)
            {
                UIALog.Warn("Tutorial demo tick failed: " + e.Message);
                try { _demo.Destroy(); } catch { }   // never leave a frozen mock on screen
                _demo = null;
            }
        }

        private static void TickRing()
        {
            if (_ringGroup == null || _ring == null || !_ring.gameObject.activeSelf) return;
            float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * RingPulseHz * 2f * Mathf.PI);
            float a = Mathf.Lerp(0.35f, 1f, s);
            if (Mathf.Abs(_ringGroup.alpha - a) > 0.002f) _ringGroup.alpha = a;
        }

        /// <summary>The CanvasScaler resolves a frame after the canvas is built, and the screen can
        /// resize under a card: re-place the scrim hole / card / callout whenever the scale moves.</summary>
        private static void CheckReplace()
        {
            if (_canvas == null) return;
            if (Mathf.Approximately(_canvas.scaleFactor, _placedScale)
                && Screen.width == _placedW && Screen.height == _placedH) return;
            Place();
        }

        // ================= build =================

        private static void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new GameObject("UIAscended_TutorialCoach");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CardSortOrder;   // 5600 card / 5300 callout, set per render
            // The glass materials read the extra vertex streams (the UiaControlCenter opt-in).
            _canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();   // the card's own controls are clickable
            _rootRt = (RectTransform)_root.transform;

            // Scrim: one full-screen panel, or four around a hole. Both absorb clicks (they do not
            // themselves close anything).
            _scrimFull = UiaUi.Panel(_root.transform, UiaTheme.Scrim, "scrim");
            _scrimTop = ScrimPart("scrim-top");
            _scrimBottom = ScrimPart("scrim-bottom");
            _scrimLeft = ScrimPart("scrim-left");
            _scrimRight = ScrimPart("scrim-right");

            BuildCardWindow();
            BuildCalloutWindow();

            _builtThemeHash = UiaMenuTheme.StyleHash();
            _placedScale = -1f;
            _root.SetActive(false);
        }

        private static Image ScrimPart(string name)
        {
            var img = UiaUi.Image(_root.transform, UiaTheme.Scrim, name);
            img.sprite = null;                        // full-bleed pieces stay sharp-cornered
            img.type = Image.Type.Simple;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            img.gameObject.SetActive(false);
            return img;
        }

        private static void BuildCardWindow()
        {
            var winGo = UiaUi.Go("card", _root.transform);
            _card = (RectTransform)winGo.transform;
            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.sizeDelta = new Vector2(WinW, 614f);
            // A REAL HUD glass panel, like the Control Center's window - it carries the F9 effect
            // stack (glow halo, edge light, ripple) so the card reads as part of the visor.
            _cardPanel = winGo.AddComponent<PanelGraphic>();
            _cardPanel.raycastTarget = true;
            _cardPanel.color = UiaTheme.Window;
            _cardPanel.BorderColor = UiaTheme.Border;
            _cardPanel.DenseFill = true;   // big panel + inner glow: the F10 "bowtie X" guard
            UiaUi.VLayout(_card, UiaTheme.Gap, (int)UiaTheme.Pad, (int)UiaTheme.Pad,
                (int)UiaTheme.Pad, (int)UiaTheme.Pad);

            BuildTitleBar(_card);

            // Rule under the title, then the card's heading - centred, above the animation
            // (FlorpyDorp's 2026-08-02 layout: TITLE / rule / HEADING / demo / centred copy).
            var ruleGo = UiaUi.Go("rule", _card);
            UiaUi.Size(ruleGo, 2f);
            var rule = ruleGo.AddComponent<Image>();
            rule.color = UiaTheme.AccentDim;
            rule.raycastTarget = false;

            var headGo = UiaUi.Go("heading", _card);
            UiaUi.Size(headGo, HeadingH);
            _cardHeading = headGo.AddComponent<TextMeshProUGUI>();
            _cardHeading.font = UiaTheme.Font();
            _cardHeading.fontSize = UiaTheme.TitleSize + 2f;
            _cardHeading.color = UiaTheme.Accent;
            _cardHeading.alignment = TextAlignmentOptions.Center;
            _cardHeading.raycastTarget = false;
            _cardHeading.richText = false;
            _cardHeading.characterSpacing = 4f;
            _cardHeading.fontStyle = FontStyles.Bold;
            UiaControls.FitText(_cardHeading, 12f, false);

            BuildDemoArea(_card);

            var bodyGo = UiaUi.Go("body", _card);
            _cardBody = (RectTransform)bodyGo.transform;
            UiaUi.Size(bodyGo, BodyReadH);
            UiaUi.VLayout(_cardBody, 6f);

            var navGo = UiaUi.Go("nav", _card);
            _cardNav = (RectTransform)navGo.transform;
            UiaUi.Size(navGo, NavH);
            UiaUi.HLayout(_cardNav, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleCenter);
        }

        private static void BuildTitleBar(RectTransform parent)
        {
            var barGo = UiaUi.Go("titlebar", parent);
            UiaUi.Size(barGo, UiaTheme.TitleH);
            var bar = (RectTransform)barGo.transform;

            // TRUE-centred title (FlorpyDorp's 2026-08-02 layout) - not an HLayout child, or the
            // right-side buttons would shove it off centre: a full-bleed centred label with the
            // button cluster anchored to the right edge on top of it.
            var title = UiaUi.Text(bar, "UI ASCENDED TUTORIAL", UiaTheme.TitleSize, UiaTheme.Text,
                TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)title.transform);
            title.characterSpacing = 8f;
            title.fontStyle = FontStyles.Bold;

            var btnsGo = UiaUi.Go("titlebtns", bar);
            _titleBtns = (RectTransform)btnsGo.transform;
            _titleBtns.anchorMin = new Vector2(1f, 0.5f);
            _titleBtns.anchorMax = new Vector2(1f, 0.5f);
            _titleBtns.pivot = new Vector2(1f, 0.5f);
            _titleBtns.anchoredPosition = Vector2.zero;
            _titleBtns.sizeDelta = new Vector2(76f, 30f);
            UiaUi.HLayout(_titleBtns, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleRight);

            // Dev-only EDIT (uiadev): jumps the lesson editor to this card's step.
            var edit = UiaControls.Button(_titleBtns, "EDIT", OpenEditor, EditBtnW, 30f, UiaControls.ButtonStyle.Panel);
            _editBtnCard = edit != null ? edit.gameObject : null;
            if (_editBtnCard != null) _editBtnCard.SetActive(false);

            // The pause control, F10-parity: lit while OUR latch holds the freeze, GREYED when a
            // pause cannot be owned (multiplayer, or vanilla already paused). The button state IS
            // the status - no "game is running" prose (FlorpyDorp 2026-08-02). Vanilla's own
            // PauseIcon sprite when the runtime grab finds it, ASCII "||" otherwise.
            _pauseBtn = UiaControls.Button(_titleBtns, "||", TogglePause, 34f, 30f,
                UiaControls.ButtonStyle.Panel);
            UiaControls.SetButtonIcon(_pauseBtn, Core.VanillaIcons.PauseIcon(), 13f);

            UiaControls.Button(_titleBtns, "X", Later, 34f, 30f, UiaControls.ButtonStyle.Panel);
            PaintChip();
        }

        private static void BuildDemoArea(RectTransform parent)
        {
            _demoArea = UiaUi.Go("demo-area", parent);
            UiaUi.Size(_demoArea, DemoH);

            // Framed backdrop (behind the stage, so the demo draws on top).
            var frameGo = UiaUi.Go("demo-frame", _demoArea.transform);
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

            var stageGo = UiaUi.Go("demo-stage", _demoArea.transform);
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
            _demoArea.SetActive(false);
        }

        private static void BuildCalloutWindow()
        {
            var go = UiaUi.Go("callout", _root.transform);
            _callout = (RectTransform)go.transform;
            _callout.anchorMin = _callout.anchorMax = new Vector2(0.5f, 0.5f);
            _callout.pivot = new Vector2(0.5f, 0.5f);
            _callout.sizeDelta = new Vector2(CalloutW, CalloutH);
            _calloutPanel = go.AddComponent<PanelGraphic>();
            _calloutPanel.raycastTarget = true;
            _calloutPanel.color = UiaTheme.Window;
            _calloutPanel.BorderColor = UiaTheme.Border;

            var contentGo = UiaUi.Go("content", _callout);
            _calloutContent = (RectTransform)contentGo.transform;
            UiaUi.Fill(_calloutContent, CalloutPad);
            UiaUi.VLayout(_calloutContent, 6f);

            // Optional mini demo (a step's DemoId, e.g. the themes callout): the strip's 150x76 mini
            // layout, OUTSIDE the per-render content (which is cleared each card) - the content
            // reserves a spacer for it. Its own stage instance (one per host, contract s5).
            _calloutDemoArea = UiaUi.Go("callout-demo", _callout);
            var cdRt = (RectTransform)_calloutDemoArea.transform;
            cdRt.anchorMin = cdRt.anchorMax = new Vector2(0.5f, 1f);
            cdRt.pivot = new Vector2(0.5f, 0.5f);
            cdRt.sizeDelta = new Vector2(MiniW, MiniH);
            cdRt.anchoredPosition = new Vector2(0f, -(CalloutPad + CalloutHeadH + 6f + MiniH * 0.5f));
            var cdFrame = cdRt.gameObject.AddComponent<Image>();
            cdFrame.color = UiaTheme.Panel;
            cdFrame.raycastTarget = false;
            UiaImages.Round(cdFrame);
            var cdHostGo = UiaUi.Go("stage", cdRt);
            var cdHost = UiaUi.Fill((RectTransform)cdHostGo.transform);
            try { _calloutDemo = TutorialDemoStage.Create(cdHost); }
            catch (Exception e)
            {
                _calloutDemo = null;
                UIALog.Warn("TutorialDemoStage.Create (callout) failed: " + e.Message);
            }
            _calloutDemoArea.SetActive(false);

            // The drawn pointer toward the anchor (never a glyph).
            var pGo = UiaUi.Go("pointer", _callout);
            _pointer = (RectTransform)pGo.transform;
            _pointer.anchorMin = _pointer.anchorMax = new Vector2(0.5f, 0.5f);
            _pointer.pivot = new Vector2(0.5f, 0.5f);
            _pointer.sizeDelta = new Vector2(PointerSize, PointerSize);
            _pointerG = pGo.AddComponent<TriangleGraphic>();
            _pointerG.raycastTarget = false;
            _pointerG.color = UiaTheme.Border;
            _pointerG.Configure(true, PointerSize);
            pGo.SetActive(false);

            // The anchor ring: an accent outline around the F10 anchor, on THIS canvas (above F10).
            var rGo = UiaUi.Go("anchor-ring", _root.transform);
            _ring = (RectTransform)rGo.transform;
            _ring.anchorMin = _ring.anchorMax = new Vector2(0.5f, 0.5f);
            _ring.pivot = new Vector2(0.5f, 0.5f);
            _ringGroup = rGo.AddComponent<CanvasGroup>();
            _ringGroup.interactable = false;
            _ringGroup.blocksRaycasts = false;
            _ringG = rGo.AddComponent<PanelGraphic>();
            _ringG.raycastTarget = false;
            _ringG.color = new Color(0f, 0f, 0f, 0f);
            _ringG.BorderColor = UiaTheme.Accent;
            _ringG.BorderWidth = 2.4f;
            _ringG.Glow = 0.6f;
            _ringG.GlowWidth = 10f;
            rGo.SetActive(false);

            go.SetActive(false);
        }

        /// <summary>Push this frame's theme colour + the F9 global effect stack onto the live window
        /// glass, exactly as a HUD box updates each frame. Every setter is dirty-guarded.</summary>
        private static void StylePanels()
        {
            if (_mode == Mode.Card) StyleGlass(_cardPanel, _card);
            else if (_mode == Mode.Callout) StyleGlass(_calloutPanel, _callout);
        }

        private static void StyleGlass(PanelGraphic panel, RectTransform rt)
        {
            if (panel == null || rt == null) return;
            var size = rt.sizeDelta;
            float corner = HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f;
            panel.color = UiaTheme.Window;
            panel.BorderColor = UiaTheme.Border;
            panel.BorderWidth = HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f;
            panel.SetShape(size.x, size.y, corner);
            // The shared tutorial glass (TutorialStrip.ApplyGlass): the analytic SDF panel the HUD
            // boxes use, the mesh without glow layers as the fallback. On the mesh path the inner
            // glow FOLDS the dense interior of a panel this shape - the 2026-09-26 "X": dark wedges
            // into every corner of the 380x200 callout, thinner slivers on the 720x614 card. Material
            // Tier B/C still ride the running FX clock; frost is CONSUMED, never demanded
            // (single-writer flag - see CloseInternal()). No fade: these panels show via SetActive.
            TutorialStrip.ApplyGlass(panel);
        }

        /// <summary>Rebuild the whole canvas after a live theme change (kit widgets freeze their
        /// colours at build time). Keeps the spec, mode, anchor and the pause; in-place dev edits
        /// are committed first. The demo stage is recreated, so a theme drag restarts its loop.</summary>
        private static void Restyle()
        {
            _lastRestyle = Time.unscaledTime;
            try { CommitFields(); } catch { }
            bool wasOpen = _open;
            DestroyRoot();
            try { EnsureBuilt(); }
            catch (Exception e)
            {
                UIALog.Error("TutorialCoach restyle rebuild failed: " + e);
                try { DestroyRoot(); } catch { }
            }
            if (_root == null)
            {
                if (wasOpen) SystemClose();   // no window: never hold the modal / pause for nothing
                return;
            }
            if (!wasOpen) return;
            _root.SetActive(true);
            SafeRender();
        }

        // ================= render =================

        private static void SafeRender()
        {
            try { Render(); }
            catch (Exception e)
            {
                _rendering = false;
                UIALog.Error("TutorialCoach render failed: " + e);
                SystemClose();
            }
        }

        private static void Render()
        {
            if (_root == null || _spec == null) return;
            try { CommitFields(); } catch { }   // typed text only (see Present)
            _rendering = true;
            try
            {
                _builtDev = DevActive();
                _bodyLabel = null;
                _headingField = null;
                _bodyField = null;
                _editHeadingKey = null;
                _editBodyKey = null;
                _headingDirty = _bodyDirty = false;   // new fields, seeded from the store
                _toast = null;
                _countLabel = null;

                bool card = _mode == Mode.Card;
                _canvas.sortingOrder = card ? CardSortOrder : CalloutSortOrder;
                if (_card != null && _card.gameObject.activeSelf != card) _card.gameObject.SetActive(card);
                if (_callout != null && _callout.gameObject.activeSelf == card) _callout.gameObject.SetActive(!card);
                if (card) { SetActive(_ring, false); SetActive(_pointer, false); }
                if (card) RenderCard();
                else RenderCallout();
                Place();
                PaintChip();
            }
            finally { _rendering = false; }
        }

        private static void RenderCard()
        {
            var spec = _spec;
            if (_cardHeading != null) _cardHeading.text = Upper(_liveHeading);

            bool demo = _demo != null && !string.IsNullOrEmpty(spec.DemoId) && KnownDemo(spec.DemoId);
            if (_demoArea != null && _demoArea.activeSelf != demo) _demoArea.SetActive(demo);
            if (demo) ShowDemo(spec.DemoId);

            bool edit = _builtDev && !string.IsNullOrEmpty(spec.StepId);
            if (_editBtnCard != null && _editBtnCard.activeSelf != edit) _editBtnCard.SetActive(edit);
            if (_titleBtns != null)
                _titleBtns.sizeDelta = new Vector2(edit ? 76f + UiaTheme.Gap + EditBtnW : 76f, 30f);

            Clear(_cardBody);
            Clear(_cardNav);
            float bodyH = _builtDev ? BodyEditH : BodyReadH;
            UiaUi.Size(_cardBody.gameObject, bodyH);
            if (_builtDev) BuildEditBody(_cardBody, spec);
            else _bodyLabel = BuildReadLabel(_cardBody, DisplayBody(), UiaTheme.LabelSize, TextAlignmentOptions.Top, 96f);
            BuildButtons(_cardNav, spec, NavH - 4f, WinW - UiaTheme.Pad * 2f, false);

            int children = demo ? 6 : 5;
            float h = UiaTheme.Pad * 2f + UiaTheme.TitleH + 2f + HeadingH + bodyH + NavH
                + (demo ? DemoH : 0f) + UiaTheme.Gap * (children - 1);
            _card.sizeDelta = new Vector2(WinW, h);
        }

        private static void RenderCallout()
        {
            var spec = _spec;
            Clear(_calloutContent);

            // Header row: heading + (dev) EDIT + a drawn X.
            var head = UiaUi.Go("head", _calloutContent);
            UiaUi.Size(head, CalloutHeadH);
            UiaUi.HLayout((RectTransform)head.transform, 6f, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            var ht = UiaUi.Text(head.transform, Upper(_liveHeading), UiaTheme.SmallSize, UiaTheme.Accent,
                TextAlignmentOptions.Left);
            ht.richText = false;
            ht.fontStyle = FontStyles.Bold;
            ht.characterSpacing = 4f;
            UiaControls.FitText(ht, 9f, false);
            var hle = ht.gameObject.AddComponent<LayoutElement>();
            hle.flexibleWidth = 1f;
            hle.minWidth = 0f;
            if (_builtDev && !string.IsNullOrEmpty(spec.StepId))
            {
                var eb = UiaControls.Button(head.transform, "EDIT", OpenEditor, 48f, 22f, UiaControls.ButtonStyle.Panel);
                ShrinkLabel(eb, UiaTheme.SmallSize);
            }
            var x = UiaControls.Button(head.transform, "", Later, 24f, 22f, UiaControls.ButtonStyle.Panel);
            if (x != null) UiaIcons.SetButtonIcon(x, UiaIcon.X, 10f);

            // Optional mini demo under the header (the callout grows 82 px to make room).
            bool demo = _calloutDemo != null && !string.IsNullOrEmpty(spec.DemoId) && KnownDemo(spec.DemoId);
            if (_calloutDemoArea != null && _calloutDemoArea.activeSelf != demo) _calloutDemoArea.SetActive(demo);
            if (demo)
            {
                UiaUi.Size(UiaUi.Go("demo-space", _calloutContent), MiniH);
                try { _calloutDemo.Show(spec.DemoId, GlyphFn, true); }
                catch (Exception e) { UIALog.Warn("Tutorial callout demo '" + spec.DemoId + "' failed to show: " + e.Message); }
            }

            // Body copy (160-char budget): auto-shrinks rather than spilling out.
            _bodyLabel = BuildReadLabel(_calloutContent, DisplayBody(), UiaTheme.SmallSize + 1f,
                TextAlignmentOptions.TopLeft, 60f);

            var nav = UiaUi.Go("nav", _calloutContent);
            UiaUi.Size(nav, 28f);
            UiaUi.HLayout((RectTransform)nav.transform, 6f, 0, 0, 0, 0, TextAnchor.MiddleCenter);
            BuildButtons(nav.transform, spec, 26f, CalloutW - CalloutPad * 2f, true);

            _callout.sizeDelta = new Vector2(CalloutW, demo ? CalloutH + MiniH + 6f : CalloutH);
        }

        private static TextMeshProUGUI BuildReadLabel(RectTransform host, string text, float size,
            TextAlignmentOptions align, float slotH)
        {
            var textGo = UiaUi.Go("copy", host);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.font = UiaTheme.Font();
            t.fontSize = size;
            t.color = UiaTheme.Text;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = false;                 // a '<' in dev-edited copy is text, not markup
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = new Vector4(2f, 2f, 2f, 2f);
            // Auto-size guards against copy that runs long: it shrinks rather than spilling out.
            t.enableAutoSizing = true;
            t.fontSizeMin = 10f;
            t.fontSizeMax = size;
            t.text = text ?? "";
            // An EXPLICIT preferred height, not the TMP's own: auto-sizing text inside a layout
            // group whose height it also feeds is the classic TMP oscillation. The label owns a
            // fixed slot (plus any slack) and shrinks its glyphs inside it.
            UiaUi.Size(textGo, slotH, flexH: 1f);
            var le = textGo.GetComponent<LayoutElement>();
            if (le != null) le.minHeight = Mathf.Min(slotH, 40f);
            return t;
        }

        /// <summary>The card's buttons, left to right, the primary one in the primary style. Widths
        /// come from each label (measured with the real font) and share the row fairly.</summary>
        private static void BuildButtons(Transform row, TCardSpec spec, float height, float rowW, bool small)
        {
            var labels = spec.Buttons;
            if (labels == null || labels.Length == 0)
            {
                MakeButton(row, "Close", -1, height, true, rowW, small);
                return;
            }
            int primary = Mathf.Clamp(spec.PrimaryIndex, 0, labels.Length - 1);
            float each = (rowW - UiaTheme.Gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
                MakeButton(row, Tok(labels[i]), i, height, i == primary, each, small);
        }

        private static void MakeButton(Transform row, string label, int index, float height, bool primary,
            float maxW, bool small)
        {
            int idx = index;
            Action click;
            if (idx < 0) click = Later;
            else click = () => Press(idx);
            var btn = UiaControls.Button(row, label ?? "", click, 120f, height,
                primary ? UiaControls.ButtonStyle.Primary : UiaControls.ButtonStyle.Panel);
            if (btn == null) return;
            var t = btn.GetComponentInChildren<TextMeshProUGUI>();
            float w = 120f;
            if (t != null)
            {
                t.richText = false;
                if (small) t.fontSize = UiaTheme.SmallSize;
                try { w = t.GetPreferredValues(label ?? "").x + (small ? 22f : 34f); } catch { }
                UiaControls.FitText(t, 9f, false);   // no ellipsis: shrink inside the button
            }
            w = Mathf.Clamp(w, small ? 56f : 96f, Mathf.Max(56f, maxW));
            var le = btn.GetComponent<LayoutElement>();
            if (le != null) { le.preferredWidth = w; le.minWidth = w; }
        }

        private static void ShrinkLabel(UiaControls.UiaButton btn, float size)
        {
            if (btn == null) return;
            var t = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.fontSize = size;
        }

        private static void ShowDemo(string demoId)
        {
            if (_demo == null || string.IsNullOrEmpty(demoId)) return;
            try { _demo.Show(demoId, GlyphFn, false); }
            catch (Exception e) { UIALog.Warn("Tutorial demo '" + demoId + "' failed to show: " + e.Message); }
        }

        private static bool KnownDemo(string id)
        {
            try { return TutorialDemoStage.IsKnownDemo(id); }
            catch { return false; }
        }

        // ================= placement =================

        /// <summary>Scrim (hole), then the card or the callout (+ ring + pointer), in coach-canvas
        /// local space (centre origin).</summary>
        private static void Place()
        {
            if (_rootRt == null) return;
            LayoutScrim();
            if (_mode == Mode.Card) PlaceCard();
            else if (_mode == Mode.Callout) PlaceCallout();
            if (_canvas != null) _placedScale = _canvas.scaleFactor;
            _placedW = Screen.width;
            _placedH = Screen.height;
        }

        private static bool HoleWanted()
        {
            return _mode == Mode.Card && _spec != null && _spec.HasHole
                && _spec.HoleScreenRect.width > 0.5f && _spec.HoleScreenRect.height > 0.5f;
        }

        private static void LayoutScrim()
        {
            bool card = _mode == Mode.Card;
            bool hole = HoleWanted();
            SetActive(_scrimFull, card && !hole);
            SetActive(_scrimTop, hole);
            SetActive(_scrimBottom, hole);
            SetActive(_scrimLeft, hole);
            SetActive(_scrimRight, hole);
            if (!hole) return;
            Rect c = _rootRt.rect;
            Rect h = Expand(ScreenToLocal(_spec.HoleScreenRect), HoleMargin);
            h = Rect.MinMaxRect(Mathf.Clamp(h.xMin, c.xMin, c.xMax), Mathf.Clamp(h.yMin, c.yMin, c.yMax),
                Mathf.Clamp(h.xMax, c.xMin, c.xMax), Mathf.Clamp(h.yMax, c.yMin, c.yMax));
            SetRect(_scrimTop, c.xMin, h.yMax, c.xMax, c.yMax);
            SetRect(_scrimBottom, c.xMin, c.yMin, c.xMax, h.yMin);
            SetRect(_scrimLeft, c.xMin, h.yMin, h.xMin, h.yMax);
            SetRect(_scrimRight, h.xMax, h.yMin, c.xMax, h.yMax);
        }

        /// <summary>Centred; with a hole the card slides vertically off it when they would overlap
        /// (clamped to the screen; stays centred if it cannot clear).</summary>
        private static void PlaceCard()
        {
            if (_card == null) return;
            Vector2 pos = Vector2.zero;
            if (HoleWanted())
            {
                Rect hole = Expand(ScreenToLocal(_spec.HoleScreenRect), HoleMargin);
                Vector2 size = _card.sizeDelta;
                Rect c = _rootRt.rect;
                var centred = new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y);
                if (centred.Overlaps(hole))
                {
                    const float gap = 16f;
                    float y = hole.center.y < 0f
                        ? hole.yMax + gap + size.y * 0.5f      // hole low: the card goes up
                        : hole.yMin - gap - size.y * 0.5f;     // hole high: the card goes down
                    float half = size.y * 0.5f;
                    y = Mathf.Clamp(y, c.yMin + EdgeMargin + half, Mathf.Max(c.yMin + EdgeMargin + half, c.yMax - EdgeMargin - half));
                    var moved = new Rect(-size.x * 0.5f, y - half, size.x, size.y);
                    if (!moved.Overlaps(hole)) pos = new Vector2(0f, y);
                }
            }
            _card.anchoredPosition = pos;
        }

        /// <summary>Beside the anchor: below, then right, left, above - the first that clears the
        /// anchor inside the screen. Adds the pulsing ring on the anchor and the drawn pointer.</summary>
        private static void PlaceCallout()
        {
            if (_callout == null) return;
            Vector2 csize = _callout.sizeDelta;
            float w = csize.x, h = csize.y;
            bool hasAnchor = _anchorScreen.width > 0.5f && _anchorScreen.height > 0.5f;
            if (!hasAnchor)
            {
                _callout.anchoredPosition = Vector2.zero;
                if (_ring != null) _ring.gameObject.SetActive(false);
                if (_pointer != null) _pointer.gameObject.SetActive(false);
                return;
            }
            Rect c = _rootRt.rect;
            Rect a = ScreenToLocal(_anchorScreen);
            Rect keepOut = Expand(a, CalloutGap * 0.5f);
            int side = -1;
            Vector2 pos = Vector2.zero;
            for (int s = 0; s < 4; s++)
            {
                Vector2 p = ClampCentre(CandidateCentre(s, a, w, h), w, h, c);
                var r = new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h);
                if (r.Overlaps(keepOut)) continue;
                side = s;
                pos = p;
                break;
            }
            if (side < 0) { side = 0; pos = ClampCentre(CandidateCentre(0, a, w, h), w, h, c); }
            _callout.anchoredPosition = pos;

            if (_ring != null && _ringG != null)
            {
                Rect ra = Expand(a, AnchorPad);
                _ring.anchoredPosition = ra.center;
                _ring.sizeDelta = ra.size;
                _ringG.SetShape(ra.width, ra.height, Mathf.Min(8f, Mathf.Min(ra.width, ra.height) * 0.5f));
                _ringG.BorderColor = UiaTheme.Accent;
                _ring.gameObject.SetActive(true);
                _ring.SetAsLastSibling();
            }
            PlacePointer(side, a, pos, w, h);
        }

        /// <summary>Side 0 below the anchor, 1 right of it, 2 left, 3 above.</summary>
        private static Vector2 CandidateCentre(int side, Rect a, float w, float h)
        {
            switch (side)
            {
                case 0: return new Vector2(a.center.x, a.yMin - CalloutGap - h * 0.5f);
                case 1: return new Vector2(a.xMax + CalloutGap + w * 0.5f, a.center.y);
                case 2: return new Vector2(a.xMin - CalloutGap - w * 0.5f, a.center.y);
                default: return new Vector2(a.center.x, a.yMax + CalloutGap + h * 0.5f);
            }
        }

        private static Vector2 ClampCentre(Vector2 p, float w, float h, Rect c)
        {
            float hw = w * 0.5f, hh = h * 0.5f;
            p.x = Mathf.Clamp(p.x, c.xMin + EdgeMargin + hw, Mathf.Max(c.xMin + EdgeMargin + hw, c.xMax - EdgeMargin - hw));
            p.y = Mathf.Clamp(p.y, c.yMin + EdgeMargin + hh, Mathf.Max(c.yMin + EdgeMargin + hh, c.yMax - EdgeMargin - hh));
            return p;
        }

        private static void PlacePointer(int side, Rect a, Vector2 calloutPos, float w, float h)
        {
            if (_pointer == null || _pointerG == null) return;
            const float inset = 18f;
            float off = PointerSize * 0.36f + 1f;   // the triangle's half-height, just outside the edge
            Vector2 local;
            float rot;
            bool up;
            switch (side)
            {
                case 0:   // callout below the anchor: pointer on its top edge, pointing up
                    local = new Vector2(Mathf.Clamp(a.center.x - calloutPos.x, -w * 0.5f + inset, w * 0.5f - inset), h * 0.5f + off);
                    rot = 0f; up = true; break;
                case 1:   // right of the anchor: left edge, pointing left
                    local = new Vector2(-w * 0.5f - off, Mathf.Clamp(a.center.y - calloutPos.y, -h * 0.5f + inset, h * 0.5f - inset));
                    rot = 90f; up = true; break;
                case 2:   // left of the anchor: right edge, pointing right
                    local = new Vector2(w * 0.5f + off, Mathf.Clamp(a.center.y - calloutPos.y, -h * 0.5f + inset, h * 0.5f - inset));
                    rot = -90f; up = true; break;
                default:  // above the anchor: bottom edge, pointing down
                    local = new Vector2(Mathf.Clamp(a.center.x - calloutPos.x, -w * 0.5f + inset, w * 0.5f - inset), -h * 0.5f - off);
                    rot = 0f; up = false; break;
            }
            _pointerG.color = UiaTheme.Border;
            _pointerG.Configure(up, PointerSize);
            _pointer.anchoredPosition = local;
            _pointer.localEulerAngles = new Vector3(0f, 0f, rot);
            _pointer.gameObject.SetActive(true);
        }

        private static Rect ScreenToLocal(Rect screen)
        {
            Vector2 a, b;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, screen.min, null, out a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRt, screen.max, null, out b);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static Rect Expand(Rect r, float by)
        {
            return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
        }

        private static void SetRect(Image img, float x0, float y0, float x1, float y1)
        {
            if (img == null) return;
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
            rt.anchoredPosition = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        }

        private static void SetActive(Component c, bool on)
        {
            if (c == null) return;
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        // ================= copy =================

        private static string Tok(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            if (s.IndexOf('{') < 0) return s;
            try { return TutorialTokens.Resolve(s); }
            catch { return s; }
        }

        private static string Upper(string s) { return (s ?? "").ToUpperInvariant(); }

        /// <summary>The body to draw: the live copy with its pause sentence swapped when the freeze
        /// is not there - for the card's RunningLine when no pause can be owned (multiplayer), for
        /// "Your game is still running." when single-player simply is not paused (auto-pause off, or
        /// the player un-paused from the header). Replacement only; a body without a pause sentence
        /// is returned unchanged.</summary>
        private static string DisplayBody()
        {
            string body = _liveBody ?? "";
            if (body.Length == 0) return body;
            string replacement = null;
            if (!CanOwnPauseSafe()) replacement = _spec != null ? Tok(_spec.RunningLine) : null;
            if (string.IsNullOrEmpty(replacement) && !PauseHeld()) replacement = RunningFallback;
            if (string.IsNullOrEmpty(replacement)) return body;
            for (int i = 0; i < PauseSentences.Length; i++)
            {
                int at = body.IndexOf(PauseSentences[i], StringComparison.Ordinal);
                if (at < 0) continue;
                return body.Substring(0, at) + replacement + body.Substring(at + PauseSentences[i].Length);
            }
            return body;
        }

        private static void RepaintBodyText()
        {
            if (_bodyLabel == null || !_open) return;
            _bodyLabel.text = DisplayBody();
        }

        // ================= dev edit (uiadev) =================

        private static void OpenEditor()
        {
            string id = _spec != null ? _spec.StepId : null;
            try { TutorialEditorWindow.OpenAt(id); }
            catch (Exception e) { UIALog.Warn("TutorialEditorWindow.OpenAt failed: " + e.Message); }
        }

        /// <summary>The key an in-place field edits for one line of the card: the spec's explicit key
        /// (<see cref="TCardSpec.HeadingKey"/> / <see cref="TCardSpec.BodyKey"/> - the Director names
        /// the variant it actually showed), else the step's default field via <see cref="EditKey"/>.
        /// "" = composed text, read-only. An explicit key neither the step declares nor the shipped
        /// copy knows is read-only too (writing it would create an orphan override).</summary>
        private static string SpecKey(string explicitKey, string stepId, string field, string fallbackField)
        {
            if (explicitKey == null) return EditKey(stepId, field, fallbackField);
            if (explicitKey.Length == 0) return null;
            return KnownKey(explicitKey) ? explicitKey : null;
        }

        private static bool KnownKey(string key)
        {
            int bar = key.IndexOf('|');
            if (bar <= 0 || bar >= key.Length - 1) return false;
            TField f;
            if (TryField(key.Substring(0, bar), key.Substring(bar + 1), out f)) return true;
            try { return TutorialTextStore.HasDefault(key); }
            catch { return false; }
        }

        /// <summary>The store key an in-place field edits, or null when this step owns no such field
        /// (writing one would create an orphan key the exporter never emits). Headings fall back to
        /// the step "title" - a Watch-mode card is headed by its step's title.</summary>
        private static string EditKey(string stepId, string field, string fallbackField)
        {
            if (string.IsNullOrEmpty(stepId)) return null;
            if (StepHasField(stepId, field)) return stepId + "|" + field;
            if (fallbackField != null && StepHasField(stepId, fallbackField)) return stepId + "|" + fallbackField;
            return null;
        }

        private static bool StepHasField(string stepId, string field)
        {
            TField f;
            return TryField(stepId, field, out f);
        }

        /// <summary>Find a step's field by name; TField.Key may be the full "stepId|field" key or the
        /// bare field name, both accepted.</summary>
        private static bool TryField(string stepId, string field, out TField found)
        {
            found = default(TField);
            try
            {
                var step = TutorialChapters.FindStep(stepId);
                if (step == null || step.Fields == null) return false;
                string full = stepId + "|" + field;
                for (int i = 0; i < step.Fields.Length; i++)
                {
                    string k = step.Fields[i].Key;
                    if (k == null) continue;
                    if (string.Equals(k, full, StringComparison.Ordinal) || string.Equals(k, field, StringComparison.Ordinal))
                    {
                        found = step.Fields[i];
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int BudgetOf(string key, int fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            int bar = key.IndexOf('|');
            if (bar <= 0) return fallback;
            TField f;
            if (TryField(key.Substring(0, bar), key.Substring(bar + 1), out f) && f.Budget > 0) return f.Budget;
            return fallback;
        }

        private static string StoreGet(string key)
        {
            try { return TutorialTextStore.Get(key) ?? ""; }
            catch { return ""; }
        }

        private static void BuildEditBody(RectTransform host, TCardSpec spec)
        {
            // The keys the Director actually read this card from (a body variant such as
            // "entry.welcome|body@mp"), else the step's default fields.
            _editHeadingKey = SpecKey(spec.HeadingKey, spec.StepId, "heading", "title");
            _editBodyKey = SpecKey(spec.BodyKey, spec.StepId, "body", null);

            // Seeded from the store while _rendering is set, so the seed's own change callback is
            // not mistaken for typing (only typing sets a field dirty).
            if (_editHeadingKey != null)
            {
                _headingField = TutorialTextField.Make(host, StoreGet(_editHeadingKey),
                    "Card heading - ASCII only", false, 80, OnHeadingChanged);
                UiaUi.Size(_headingField.gameObject, UiaTheme.RowH);
                _headingField.onEndEdit.AddListener(OnHeadingEndEdit);
            }
            if (_editBodyKey != null)
            {
                _bodyField = TutorialTextField.Make(host, StoreGet(_editBodyKey),
                    "Card body - ASCII only", true, 900, OnBodyChanged);
                UiaUi.Size(_bodyField.gameObject, 110f, flexH: 1f);
                var ble = _bodyField.gameObject.GetComponent<LayoutElement>();
                if (ble != null) ble.minHeight = 90f;
                _bodyField.onEndEdit.AddListener(OnBodyEndEdit);
            }
            else
            {
                // A composed body (Watch mode: Says + Then) has no single field to write - read it
                // here, edit it in the lesson editor (EDIT, top right).
                _bodyLabel = BuildReadLabel(host, DisplayBody(), UiaTheme.LabelSize, TextAlignmentOptions.Top, 90f);
            }

            var rowGo = UiaUi.Go("edit-status", host);
            UiaUi.Size(rowGo, 20f);
            UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            _countLabel = UiaUi.Text(rowGo.transform, "", UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.Left);
            _countLabel.richText = false;
            UiaUi.Size(_countLabel.gameObject, 20f, 200f, flexW: 1f);
            _toast = UiaUi.Text(rowGo.transform, "", UiaTheme.SmallSize, UiaTheme.Good, TextAlignmentOptions.Right);
            _toast.richText = false;
            UiaUi.Size(_toast.gameObject, 20f, 300f, flexW: 0f);
            var tc = _toast.color; tc.a = 0f; _toast.color = tc;
            UpdateCounts();

            // Literal token text - deliberately NOT resolved.
            UiaControls.Note(host, _editHeadingKey == null && _editBodyKey == null
                ? "Dev (uiadev): this card has no heading/body field of its own - EDIT opens the lesson editor at this step."
                : "Dev (uiadev): saved when you leave a field. Tokens stay as typed, e.g. {UIA_Grid} {V:SmartStow}. EDIT opens the lesson editor.");
        }

        /// <summary>A heading keystroke (TMP onValueChanged). The seed set during the build fires this
        /// too - with _rendering set, and that is not typing.</summary>
        private static void OnHeadingChanged(string _)
        {
            if (_rendering) return;
            _headingDirty = true;
            UpdateCounts();
        }

        private static void OnBodyChanged(string _)
        {
            if (_rendering) return;
            _bodyDirty = true;
            UpdateCounts();
        }

        private static void UpdateCounts()
        {
            if (_countLabel == null) return;
            string s = "";
            if (_headingField != null)
                s = "Heading " + (_headingField.text ?? "").Length + "/" + BudgetOf(_editHeadingKey, 28);
            if (_bodyField != null)
                s += (s.Length > 0 ? "    " : "") + "Body " + (_bodyField.text ?? "").Length + "/" + BudgetOf(_editBodyKey, 280);
            _countLabel.text = s;
        }

        // TMP fires onEndEdit on EVERY deselect, typed or not: only a dirty field is written.
        private static void OnHeadingEndEdit(string v)
        {
            if (_rendering || !_open || !_headingDirty) return;
            CommitOne(_editHeadingKey, v, true);
        }

        private static void OnBodyEndEdit(string v)
        {
            if (_rendering || !_open || !_bodyDirty) return;
            CommitOne(_editBodyKey, v, false);
        }

        /// <summary>Commit what the player TYPED into the in-place fields (close, card swap, button,
        /// restyle, re-render). A field nobody typed into writes nothing - its text may be stale, and
        /// comparing it with the live store would write the old text back over a Lesson Editor edit
        /// (plus Save). Unchanged typed text writes nothing either.</summary>
        private static void CommitFields()
        {
            if (_rendering) return;
            if (_headingDirty && _headingField != null && _editHeadingKey != null) CommitOne(_editHeadingKey, _headingField.text, true);
            if (_bodyDirty && _bodyField != null && _editBodyKey != null) CommitOne(_editBodyKey, _bodyField.text, false);
        }

        /// <summary>Write one TYPED field through the text store (ASCII-sanitised there; empty or equal
        /// to the shipped default reverts) and save at once, then show the store's own result on the
        /// card and the lint verdict in the toast. Clears that field's dirty flag.</summary>
        private static void CommitOne(string key, string text, bool heading)
        {
            if (heading) _headingDirty = false; else _bodyDirty = false;
            if (string.IsNullOrEmpty(key)) return;
            text = text ?? "";
            if (string.Equals(StoreGet(key), text, StringComparison.Ordinal)) return;
            bool saved;
            try
            {
                TutorialTextStore.Set(key, text);
                saved = TutorialTextStore.Save();
            }
            catch (Exception e)
            {
                UIALog.Warn("TutorialCoach: saving '" + key + "' failed: " + e.Message);
                Toast("SAVE FAILED - see the log", false);
                return;
            }
            string stored = StoreGet(key);
            if (heading)
            {
                _liveHeading = Tok(stored);
                if (_cardHeading != null && _mode == Mode.Card) _cardHeading.text = Upper(_liveHeading);
            }
            else _liveBody = Tok(stored);
            if (!saved)
            {
                // Kept in memory (the card shows it), but NOT on disk: the store refused (a
                // TutorialText.xml it could not read, or uiareset) or the write failed. The Lesson
                // Editor's status line / the log say which.
                bool blocked = false;
                try { blocked = TutorialTextStore.SaveBlockedReason != null; } catch { }
                Toast(blocked ? "NOT SAVED - saving is off (F8 editor says why)" : "SAVE FAILED - see the log", false);
                return;
            }
            string problem = null;
            try { problem = TutorialLint.CheckField(key, stored, BudgetOf(key, heading ? 28 : 280)); } catch { }
            Toast(problem == null ? "saved" : "saved - " + problem, problem == null);
        }

        private static void Toast(string msg, bool good)
        {
            if (_toast == null) return;
            _toast.text = msg ?? "";
            var c = good ? UiaTheme.Good : UiaTheme.Warn;
            c.a = 1f;
            _toast.color = c;
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        /// <summary>Fade the confirmation label out over its final second. Unscaled - the game may be
        /// paused behind us.</summary>
        private static void ToastTick()
        {
            if (_toast == null) return;
            float left = _toastUntil - Time.unscaledTime;
            float a = left <= 0f ? 0f : Mathf.Clamp01(left);
            var c = _toast.color;
            if (Mathf.Abs(c.a - a) > 0.004f) { c.a = a; _toast.color = c; }
        }

        // ================= pause chip =================

        private static bool PauseHeld()
        {
            try { return GamePause.Held; }
            catch { return false; }
        }

        private static bool CanOwnPauseSafe()
        {
            try { return GamePause.CanOwnPause(); }
            catch { return false; }
        }

        /// <summary>Repaint the header pause button from the live world state: lit while OUR latch
        /// holds the freeze, greyed when a pause cannot be owned (multiplayer, or a vanilla-owned
        /// pause) - the same truth table as the F10 header button.</summary>
        private static void PaintChip()
        {
            if (_pauseBtn == null) return;
            bool held = PauseHeld();
            _pauseBtn.SetSelected(held);
            _pauseBtn.SetEnabled(held || (CanOwnPauseSafe() && !WorldManager.IsGamePaused));
        }

        private static void TogglePause()
        {
            if (GamePause.Held) GamePause.Release(PauseReason);
            else GamePause.Hold(PauseReason, InputStateKey);
            if (_open && !GamePause.Held) ModalInputChain.ReassertTop();
            PaintChip();
            RepaintBodyText();   // a pause sentence in the copy follows the freeze
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

        /// <summary>Anything else pausing / unpausing (the player round-tripped Esc) repaints the
        /// chip and the body copy, whose pause sentence depends on it.</summary>
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
            // "WorldManager"/Paused whenever a pause is still latched (e.g. F10's own) - leaving a
            // still-open layer underneath in the wrong state. Hand Typing back explicitly. (Callers
            // clear _open FIRST, so this can never re-assert the coach's own key.)
            ModalInputChain.ReassertTop();
            try { MouseModeController.RemoveModal(_modal); } catch { }
            CursorBlockArbiter.Release(CursorHoldId);
            // Only re-lock the cursor when no other UIA surface still holds it - a callout / the
            // Guide tab's Watch opens the coach OVER F10, and closing must not yank F10's cursor.
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
            if (_calloutDemo != null)
            {
                try { _calloutDemo.Destroy(); } catch (Exception e) { UIALog.Warn("Tutorial callout demo teardown: " + e.Message); }
                _calloutDemo = null;
            }
            if (_cardPanel != null) { try { HudFxMaterials.Unassign(_cardPanel); } catch { } }
            if (_calloutPanel != null) { try { HudFxMaterials.Unassign(_calloutPanel); } catch { } }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _rootRt = null;
            _scrimFull = _scrimTop = _scrimBottom = _scrimLeft = _scrimRight = null;
            _card = null;
            _cardPanel = null;
            _titleBtns = null;
            _editBtnCard = null;
            _pauseBtn = null;
            _cardHeading = null;
            _demoArea = null;
            _demoHost = null;
            _cardBody = null;
            _cardNav = null;
            _callout = null;
            _calloutPanel = null;
            _calloutContent = null;
            _calloutDemoArea = null;
            _pointer = null;
            _pointerG = null;
            _ring = null;
            _ringG = null;
            _ringGroup = null;
            _bodyLabel = null;
            _headingField = null;
            _bodyField = null;
            _headingDirty = _bodyDirty = false;
            _toast = null;
            _countLabel = null;
            _placedScale = -1f;
        }

        /// <summary>Hot-reload / plugin teardown. Must leave NOTHING behind: no canvas, no borrowed
        /// pause, no modal input state, no live event subscription, no callback into the Director.
        /// The open flag drops BEFORE the modal release so ModalInputChain.ReassertTop cannot put
        /// the coach's own Typing state straight back (the pre-0.9.8.0 teardown order could strand
        /// it across an F6).</summary>
        internal static void Shutdown()
        {
            UnhookPause();
            try { GamePause.Release(PauseReason); } catch { }
            _open = false;
            ReleaseModal();
            DestroyRoot();
            _spec = null;
            _mode = Mode.None;
            _anchorScreen = default(Rect);
            _liveHeading = _liveBody = null;
            _editHeadingKey = _editBodyKey = null;
            _headingDirty = _bodyDirty = false;
            _builtDev = false;
            _rendering = false;
            _gen = 0;
            _builtThemeHash = 0;
            _lastRestyle = 0f;
            _toastUntil = 0f;
            _openedAt = 0f;
            _placedW = _placedH = 0;
            _lastFieldFocusFrame = -999;
        }
    }
}
