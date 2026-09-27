using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.UI;
using StationeersUIMod.Core;
using StationeersUIMod.UI.Menu.Kit;
using StationeersUIMod.UI.Menu.Tabs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu
{
    /// <summary>One tab of the Control Center. <see cref="Build"/> fills the (freshly cleared)
    /// content area. <paramref name="advanced"/> is a historical parameter from the retired global
    /// Simple/Advanced density toggle — the shell always passes <c>true</c> now (Kit v2, 0.9.8.0);
    /// every tab expresses its own "curated vs full" split with per-Section "More options"
    /// disclosures instead. Kept in the signature so a tab implementation never has to change
    /// shape again if a future per-tab density need ever comes back.</summary>
    public interface IUiaTab
    {
        string Title { get; }
        void Build(RectTransform content, bool advanced);
    }

    /// <summary>
    /// The UGUI "UI Ascended Control Center": the player-facing front door to the mod. A single
    /// screen-space window with two always-visible master switches (Radial half / HUD half), a
    /// manila-folder tab bar and a title-bar settings search. The old global Simple/Advanced
    /// density toggle is gone (Kit v2, 0.9.8.0): every tab is built from Sections whose own
    /// "More options" disclosures carry what used to be Advanced-only content, so every Build
    /// always runs at full content (see <c>BuildActiveTab</c>). Built entirely in code
    /// (hot-reload safe), EventSystem-driven, and modal (cursor unlocked + game keys captured,
    /// but UGUI clicks left alive so its own controls work — unlike the radial's
    /// <see cref="ModalScope"/>).
    ///
    /// Static + pumped from StationeersUIMod.Update, mirroring HudSystem/UnityRadialView.
    /// </summary>
    public static class UiaControlCenter
    {
        private sealed class Modal : IModal { public bool UnlockCursor => true; }

        internal const string InputStateKey = "UIA_ControlCenter";   // ModalInputChain reasserts by this name
        private const string PauseReason = "f10";   // our name in the shared GamePause latch

        private static GameObject _root;
        private static RectTransform _window;          // the centred window panel (hit-test target)
        private static UI.Hud.PanelGraphic _windowPanel; // the window's glass surface (HUD PanelGraphic)
        private static Kit.UiaGlassSkin _windowEdge;   // the window exterior's flowing edge line
        private static GraphicRaycaster _raycaster;    // toggled off in edit-preview (F9 owns clicks)
        private static RectTransform _contentArea;
        private static RectTransform _popupLayer;
        private static readonly Modal _modal = new Modal();

        private static List<IUiaTab> _tabs;
        private static UiaComposite.FolderTabsHandle _folderTabs;   // the manila-folder tab strip + content card
        private static int _active;
        private static bool _open;
        private static bool _modalHeld;
        /// <summary>True while this window holds the game modal (false in F9 edit-preview) -
        /// ModalInputChain uses it to decide whether to hand the Typing state back here.</summary>
        internal static bool ModalHeld => _modalHeld;
        private static bool _editPreview;              // open behind the active F9 editor = click-to-edit
        private static int _builtThemeHash;            // UiaMenuTheme.StyleHash at last (re)build
        private static float _lastRestyle;             // unscaled time of the last live restyle
        private const float RestyleMinInterval = 0.14f; // throttle: a live palette drag can't rebuild every frame

        private static UiaControls.UiaButton _pauseBtn;

        // Tutorial anchors (0.9.8.0, TryGetAnchorRect): the title-bar search host and X, and the
        // master-switch strip. Nulled with the root in RestyleCore/Shutdown like every other handle.
        private static RectTransform _searchAnchor;
        private static RectTransform _closeAnchor;
        private static RectTransform _masterStrip;
        private static readonly Vector3[] _anchorCorners = new Vector3[4];

        // The pause relay is a VANILLA-rooted static event (WorldManager.OnPaused, via GamePause), so
        // the handler lives in a field and is unsubscribed on Close/Shutdown — a lambda subscribed
        // inline would keep a hot-reloaded assembly's method alive forever.
        private static readonly System.Action<bool> _onPauseChanged = OnPauseChanged;
        private static bool _pauseHooked;

        public static bool IsOpen => _open;

        /// <summary>True screen-point-over-the-window test (menu canvas is ScreenSpaceOverlay, so
        /// the camera arg is null). Used by the F9 editor to route a click onto the menu as an
        /// editable surface. False when closed or not yet built.</summary>
        public static bool HitTestWindow(Vector2 screenPoint)
        {
            if (!_open || _window == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_window, screenPoint, null);
        }

        public static void Toggle() { if (_open) Close(); else Open(); }

        public static void Open()
        {
            bool wasOpen = _open;   // tutorial: MenuOpened fires on the closed -> open edge only
            EnsureBuilt();
            if (_root == null) return;
            _open = true;
            _root.SetActive(true);
            HookPause();       // repaint the pause button from vanilla's own OnPaused, while we're up
            RefreshPauseButton();
            // Opened behind the active HUD editor = "edit preview": the menu becomes a live-themed
            // static surface the F9 editor can click to edit, so it must not grab input/cursor or
            // absorb clicks through its own raycaster.
            ApplyEditPreview(Windows.HudEditorMode.Active);
            BuildActiveTab();
            // Lessons (0.9.8.0): a real open only - an F9 edit-preview surface is not the player
            // opening the menu. Raise never throws.
            if (!wasOpen && !_editPreview) Tutorial.TutorialSignals.Raise(Tutorial.TSignal.MenuOpened);
        }

        /// <summary>Push the window's glass: theme fill/border + the F9 global effect stack (glow
        /// halo + edge light + ripple + sheen + frost). Sized from the RectTransform, cornered to
        /// the global HUD radius so it matches a HUD box. Cheap — every setter is dirty-guarded.</summary>
        private static void StyleWindowPanel()
        {
            if (_windowPanel == null || _window == null) return;
            var size = _window.sizeDelta;
            float corner = UI.Hud.HudConfig.CornerRadius != null ? UI.Hud.HudConfig.CornerRadius.Value : 10f;
            _windowPanel.color = UiaTheme.Window;
            _windowPanel.BorderColor = UiaTheme.Border;
            float bw = UI.Hud.HudConfig.BorderWidth != null ? UI.Hud.HudConfig.BorderWidth.Value : 1.4f;
            _windowPanel.BorderWidth = bw;
            _windowPanel.SetShape(size.x, size.y, corner);
            // The outer window gets the full stack INCLUDING the glow halo. The material-driven
            // Tier B (shine) + Tier C (frost) only look right while the HUD's FX clock is running —
            // with the Visor HUD master off that clock stops, so gate them on it and fall back to
            // the static Tier-A glow/edge/ripple (adversarial review 2026-07-17). FrostDemand keeps
            // the backdrop capture alive for us while the clock is live and Tier C is on.
            bool fxLive = UI.Hud.HudSystem.FxClockLive;
            bool frostWanted = fxLive && UI.Hud.HudConfig.FxTierC != null && UI.Hud.HudConfig.FxTierC.Value;
            UI.Hud.HudGlobalGlass.FrostDemand = frostWanted;
            // The window panel stays on the mesh/glass-material path in every state; its MOVING
            // edge light is the _windowEdge overlay (one flowing line, whether or not Tier C
            // frost owns this panel's material).
            Kit.UiaGlassSkin.ApplySdfFlow(_windowPanel, false);
            UI.Hud.HudGlobalGlass.Apply(_windowPanel, includeGlow: true, wantFrost: fxLive,
                wantTierB: fxLive, externalMaterial: false);
            if (_windowEdge != null) _windowEdge.CornerOverride = corner;
            // Dense interior mesh (dirty-guarded; the GridTheme.WantsDenseFill precedent): with
            // inner glow on — every 0.9.7.4 shipped theme enables it — the sparse fan interior
            // of a panel THIS large re-triggers the 2026-07-15 "Bowtie X" fan-fold, drawn as a
            // frosted X across the whole window. A dense fill is star-shaped by construction
            // and cannot fold.
            _windowPanel.DenseFill = true;
        }

        /// <summary>Enter/leave edit-preview: in preview the menu yields all input to the F9
        /// editor (raycaster off, no game modal); out of preview it is the normal modal window.</summary>
        private static void ApplyEditPreview(bool on)
        {
            _editPreview = on;
            if (_raycaster != null) _raycaster.enabled = !on;
            if (on)
            {
                // Drop a held pause BEFORE releasing the modal: removing our Typing key while the
                // game is paused would pop KeyManager onto vanilla's "WorldManager"/Paused state
                // and strand the freeze with no button left to clear it.
                Core.GamePause.Release(PauseReason);
                ReleaseModal();   // F9 already owns cursor + input for editing
            }
            else AcquireModal();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            _editPreview = false;
            // The window is hidden — stop asking HudSystem to keep the frost backdrop alive, and
            // drop its shared glass/edgefx material NOW (symmetric with Restyle/Shutdown) so a
            // reopen after Tier C is disabled can't flash one stale-frost frame before the next
            // StyleWindowPanel re-evaluates (adversarial review 2026-07-17).
            UI.Hud.HudGlobalGlass.FrostDemand = false;
            if (_windowPanel != null) UI.Hud.HudFxMaterials.Unassign(_windowPanel);
            // If the F9 editor had the menu selected as its edit target, drop that — else its
            // theme popup keeps drawing over the now-hidden menu (adversarial review 2026-07-17).
            Windows.HudEditorMode.MenuSelected = false;
            if (_raycaster != null) _raycaster.enabled = true; // restore for a normal reopen
            Kit.UiaItemPicker.Reset();
            Kit.UiaRebindCapture.Reset();
            // Kit transients close through their owners FIRST (statics nulled), then the layer
            // sweep catches anything ownerless (dropdown popups track themselves per-widget).
            UiaComposite.CloseTransients();
            UiaSearch.CloseResults();
            // Clear any floating dropdown popups / catchers so none reactivate on reopen.
            if (_popupLayer != null)
                for (int i = _popupLayer.childCount - 1; i >= 0; i--)
                    Object.Destroy(_popupLayer.GetChild(i).gameObject);
            if (_root != null) _root.SetActive(false);
            // Un-pause BEFORE ReleaseModal: vanilla's un-pause pops KeyManager's input-state map, so
            // dropping our own Typing key first would land us in whatever state sorts last.
            GamePause.Release(PauseReason);
            UnhookPause();
            ReleaseModal();
        }

        /// <summary>Open straight to the Guide tab (first-run / "?" affordance).</summary>
        public static void OpenGuide()
        {
            EnsureBuilt();
            if (_tabs != null)
                for (int i = 0; i < _tabs.Count; i++)
                    if (_tabs[i] is GuideTab) { _active = i; break; }
            Open();
        }

        // ---------- tutorial hooks (0.9.8.0; titles, never indices - tabs get appended) ----------

        /// <summary>Open F10 on the tab titled <paramref name="title"/> (e.g. the lesson Finish card's
        /// "See all lessons" -> <c>Tutorial.TutorialAnchors.GuideTab</c>). Already open: selects it.
        /// An unknown title opens on the current tab.</summary>
        internal static void OpenOnTab(string title)
        {
            if (_open) { SelectTab(title); return; }
            EnsureBuilt();
            int i = IndexOfTab(title);
            if (i >= 0) _active = i;
            Open();
        }

        /// <summary>Select the tab titled <paramref name="title"/> (exact match first, then
        /// case-insensitive). Open: rebuilds the page and raises MenuTabSelected like a click.
        /// Closed: only remembers it for the next Open. False = no such tab.</summary>
        internal static bool SelectTab(string title)
        {
            if (_tabs == null) EnsureBuilt();
            int i = IndexOfTab(title);
            if (i < 0) return false;
            if (_open) SelectTab(i);
            else _active = i;
            return true;
        }

        private static int IndexOfTab(string title)
        {
            if (_tabs == null || string.IsNullOrEmpty(title)) return -1;
            for (int i = 0; i < _tabs.Count; i++)
                if (string.Equals(_tabs[i].Title, title, System.StringComparison.Ordinal)) return i;
            for (int i = 0; i < _tabs.Count; i++)
                if (string.Equals(_tabs[i].Title, title, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The SCREEN rect (px, bottom-left origin, y up - Input.mousePosition space) of an
        /// F10 anchor, for the tutorial's tour callouts. Grammar: "tab:&lt;exact tab title&gt;",
        /// "search" (title-bar search box), "pause" (title-bar pause button), "close" (title-bar X);
        /// extras "master" (both master switches), "master:radial", "master:hud", "window",
        /// "content" (the selected page). False when F10 is closed, the id is unknown, or the anchor
        /// is not on screen. Forces a canvas layout pass first so a window opened or rebuilt this
        /// very frame reports its real geometry.</summary>
        internal static bool TryGetAnchorRect(string anchorId, out Rect screenRect)
        {
            screenRect = default(Rect);
            if (!_open || _root == null || string.IsNullOrEmpty(anchorId)) return false;
            try
            {
                Canvas.ForceUpdateCanvases();
                string id = anchorId.Trim();
                RectTransform rt = null;
                if (id.StartsWith("tab:", System.StringComparison.OrdinalIgnoreCase))
                {
                    int i = IndexOfTab(id.Substring(4).Trim());
                    if (i < 0 || _folderTabs == null || _folderTabs.Tabs == null || i >= _folderTabs.Tabs.Count) return false;
                    var tab = _folderTabs.Tabs[i];
                    rt = tab != null ? (RectTransform)tab.transform : null;
                }
                else
                {
                    switch (id.ToLowerInvariant())
                    {
                        case "search": rt = _searchAnchor; break;
                        case "pause": rt = _pauseBtn != null ? (RectTransform)_pauseBtn.transform : null; break;
                        case "close": rt = _closeAnchor; break;
                        case "master": rt = _masterStrip; break;
                        case "master:radial": rt = MasterCell(0); break;
                        case "master:hud": rt = MasterCell(1); break;
                        case "window": rt = _window; break;
                        case "content": rt = _contentArea; break;
                    }
                }
                return ScreenRectOf(rt, out screenRect);
            }
            catch
            {
                screenRect = default(Rect);
                return false;
            }
        }

        /// <summary>Master switch cell i (0 = RADIAL MENUS, 1 = VISOR HUD - BuildMasterStrip order).</summary>
        private static RectTransform MasterCell(int i)
        {
            if (_masterStrip == null || i < 0 || i >= _masterStrip.childCount) return null;
            return _masterStrip.GetChild(i) as RectTransform;
        }

        private static bool ScreenRectOf(RectTransform rt, out Rect r)
        {
            r = default(Rect);
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            rt.GetWorldCorners(_anchorCorners);   // 0=BL, 2=TR; overlay canvas -> camera null
            Vector2 a = RectTransformUtility.WorldToScreenPoint(null, _anchorCorners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(null, _anchorCorners[2]);
            r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return r.width >= 1f && r.height >= 1f;
        }

        /// <summary>Pumped every frame from StationeersUIMod.Update.</summary>
        public static void Update()
        {
            if (!_open) return;
            // The window is live HUD glass — push this frame's theme colour + F9 global effects
            // (glow, edge light, ripple, frost) onto it, exactly like a HUD box updates each frame.
            StyleWindowPanel();
            // Keep the pause button honest even when no event fires (a client connecting while
            // F10 is open flips CanOwnPause with no PausedChanged) - two cheap setters.
            RefreshPauseButton();
            // Keep edit-preview in lock-step with the F9 editor: entering/leaving the editor while
            // the menu is open flips it between "editable surface" and normal modal window.
            if (_editPreview != Windows.HudEditorMode.Active)
                ApplyEditPreview(Windows.HudEditorMode.Active);
            // Follow the live UI theme: rebuild when the derived (or overridden) palette changes,
            // so an F9 palette drag re-skins the menu. THROTTLED — a continuous colour-wheel drag
            // changes the hash every frame, and a full canvas teardown+rebuild 60x/s is a GC
            // hitch (adversarial review 2026-07-17); ~7 rebuilds/s reads as live and settles on the
            // final value one interval after release.
            if (UiaMenuTheme.StyleHash() != _builtThemeHash
                && Time.unscaledTime - _lastRestyle >= RestyleMinInterval)
                Restyle();
            // A rebind is capturing the next key — it owns input (Esc cancels the capture, not
            // the window).
            if (UiaRebindCapture.Active) { UiaRebindCapture.Tick(); return; }
            // ESC OWNERSHIP CHAIN. In edit-preview the F9 editor owns Escape outright (and F10
            // re-press closes the menu). Otherwise, in order:
            //  1. A FOCUSED TEXT FIELD (F10 plan pain 4): the press only cancels the edit —
            //     UiaInputs.AnyFocused covers both event orderings of the same frame.
            //  2. Registered interceptors (reverse registration order): dialogs stacked over
            //     F10 — the colour popup, and whatever comes later — consume Esc themselves.
            //  3. The kit's own transients: an open dropdown popup / kebab menu / popover /
            //     search-results panel closes (topmost only), or an armed inline confirm
            //     disarms — never the whole window.
            //  4. Only then may Esc close the window — and not while the Handbook viewer, the
            //     tutorial coach or the capture dialog is stacked ABOVE it (they read the same
            //     raw key state this same frame; without the yield one press would close every
            //     layer).
            if (!_editPreview && Input.GetKeyDown(KeyCode.Escape)
                && !Kit.UiaInputs.AnyFocused
                && !Tutorial.TutorialEditorWindow.OwnsKeyboard)   // Esc in a Lesson Editor field is ImGui's
            {
                if (ConsumeEscByInterceptors()) return;
                if (TryCloseKitTransient()) return;
                if (!HandbookViewer.IsOpen && !Tutorial.TutorialCoach.IsOpen
                    && !UI.Grid.GridCapturePanel.IsOpen)
                {
                    // Vanilla's Escape binding fires on key-UP - starve it until the key is
                    // released, so closing F10 with Esc can never open the vanilla pause menu
                    // on the same press.
                    Core.ModalInputChain.BeginEscSwallow();
                    Close();
                    return;
                }
            }
            // If the world goes away (menu/loading), never leave the window stranded.
            if (!Guards.CanDraw()) Close();
        }

        /// <summary>True while a theme-driven <see cref="Restyle"/> is rebuilding the window.
        /// Tabs whose Build does heavy work that a pure re-skin does not need (disk IO, an
        /// inventory scan — see StorageTab) check this and reuse a short-lived cache instead:
        /// an F9 colour-wheel drag restyles up to ~7x/s, and none of that work changes what the
        /// restyle repaints. User gestures always build with this false, i.e. fresh data.</summary>
        public static bool IsRestyling { get; private set; }

        /// <summary>Rebuild the whole window in place to re-skin frozen-colour widgets (buttons,
        /// outlines) after a live theme change. Preserves open/active/advanced/edit-preview.</summary>
        private static void Restyle()
        {
            IsRestyling = true;
            try
            {
                RestyleCore();
            }
            finally
            {
                IsRestyling = false;
            }
        }

        private static void RestyleCore()
        {
            _lastRestyle = Time.unscaledTime;
            bool wasOpen = _open;
            bool wasPreview = _editPreview;
            int active = _active;
            // Capture scroll BEFORE the whole root (incl. the scroll view) is destroyed, so a
            // live theme drag while scrolled down doesn't fling the view to the top each rebuild.
            float scrollY;
            bool hadScroll = TryCaptureScroll(out scrollY);
            // Kit transients (kebab menu / popover / search results) live on the popup layer
            // that is about to die — close them THROUGH their owners so the static handles are
            // nulled, not dangling at destroyed objects.
            UiaComposite.CloseTransients();
            UiaSearch.CloseResults();
            // Forget any frost material on the window graphic before it dies, so its dictionary
            // entry doesn't dangle across restyle churn.
            if (_windowPanel != null) UI.Hud.HudFxMaterials.Unassign(_windowPanel);
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _window = null;
            _windowPanel = null;
            _windowEdge = null;
            _raycaster = null;
            _contentArea = null;
            _popupLayer = null;
            _folderTabs = null;
            _pauseBtn = null;
            _searchAnchor = null;
            _closeAnchor = null;
            _masterStrip = null;
            UiaControls.PopupLayer = null;
            UiaControls.OpenDropdown = null;   // its popup died with the root just now
            EnsureBuilt();
            _active = Mathf.Clamp(active, 0, _tabs != null ? _tabs.Count - 1 : 0);
            RefreshPauseButton();
            if (wasOpen && _root != null)
            {
                _open = true;
                _root.SetActive(true);
                ApplyEditPreview(wasPreview);
                BuildActiveTab();
                if (hadScroll) RestoreScroll(scrollY);
            }
        }

        // ---------- Esc interceptors ----------

        // Dialogs hosted OVER the window (the capture panel's colour popup, future stacked
        // pickers) register here so the window's own close-on-Esc yields to them. Called in
        // REVERSE registration order (most recently stacked first).
        private static readonly List<System.Func<bool>> _escInterceptors =
            new List<System.Func<bool>>();

        /// <summary>Register an Esc interceptor: called (newest first) when Esc goes down while
        /// F10 is open, BEFORE the window considers closing. Return true = "I consumed Esc this
        /// frame" (typically: my popup was open and I just closed it). Unregister by the SAME
        /// delegate reference; the list is cleared in Shutdown, so a hot reload strands nothing.</summary>
        public static void RegisterEscInterceptor(System.Func<bool> tryConsume)
        {
            if (tryConsume != null && !_escInterceptors.Contains(tryConsume))
                _escInterceptors.Add(tryConsume);
        }

        /// <summary>Remove a previously registered interceptor (by reference). Unknown = no-op.</summary>
        public static void UnregisterEscInterceptor(System.Func<bool> tryConsume)
        {
            if (tryConsume != null) _escInterceptors.Remove(tryConsume);
        }

        /// <summary>Run the interceptors, newest first. Robust against an interceptor that
        /// unregisters itself (or others) mid-call, and against one that throws.</summary>
        private static bool ConsumeEscByInterceptors()
        {
            for (int i = _escInterceptors.Count - 1; i >= 0; i--)
            {
                if (i >= _escInterceptors.Count) continue;   // list shrank under us
                bool consumed = false;
                try
                {
                    var f = _escInterceptors[i];
                    consumed = f != null && f();
                }
                catch (System.Exception e) { UIALog.Warn("Esc interceptor threw: " + e.Message); }
                if (consumed) return true;
            }
            return false;
        }

        /// <summary>Esc against the kit's own transients: close the topmost open popup, or
        /// disarm the armed inline confirm. ONE consumer per press — the next Esc moves on.</summary>
        private static bool TryCloseKitTransient()
        {
            if (UiaComposite.CloseTopTransient()) return true;    // kebab menu / popover
            if (UiaControls.CloseOpenDropdown()) return true;     // dropdown popup
            if (UiaSearch.CloseResultsIfOpen()) return true;      // search results
            if (UiaComposite.DisarmArmedConfirm()) return true;   // armed "Sure?" row
            return false;
        }

        // ---------- modal ----------

        private static void AcquireModal()
        {
            if (_modalHeld) return;
            _modalHeld = true;
            try { KeyManager.SetInputState(InputStateKey, KeyInputState.Typing); } catch { }
            try { MouseModeController.AddModal(_modal); } catch { }
            Core.CursorBlockArbiter.Hold("controlcenter");
        }

        private static void ReleaseModal()
        {
            if (!_modalHeld) return;
            _modalHeld = false;
            try { KeyManager.RemoveInputState(InputStateKey); } catch { }
            try { MouseModeController.RemoveModal(_modal); } catch { }
            Core.CursorBlockArbiter.Release("controlcenter");
            try
            {
                if (CursorManager.Instance != null)
                    CursorManager.Instance.OnApplicationFocus(true); // re-lock the cursor next frame
            }
            catch { }
        }

        // ---------- build ----------

        private static void EnsureBuilt()
        {
            if (_root != null) return;

            _tabs = new List<IUiaTab>
            {
                new ProfilesTab(),
                new RadialTab(),
                new HudTab(),
                new StorageTab(),
                new ControlsTab(),
                new GuideTab(),
                new FeedbackTab(),
            };

            // Editors that live OUTSIDE F10 (F9, the legacy radial editor) register here so the
            // title-bar search box can say "it's over there" and jump to them — RadialTab and
            // HudTab each keep their own in-tab shortcut button; this is the SAME open path,
            // just reachable from anywhere via search (F10 plan principle 3).
            RegisterExternals();

            // Restore the last-open tab (persisted per launch — Q16 "remember the last page").
            // Restyle overwrites _active right after EnsureBuilt, so this only steers a genuinely
            // fresh build; OpenGuide still overrides it after.
            string lastTab = UiaMenuPrefs.LastTab;
            if (!string.IsNullOrEmpty(lastTab))
                for (int i = 0; i < _tabs.Count; i++)
                    if (string.Equals(_tabs[i].Title, lastTab, System.StringComparison.Ordinal))
                    { _active = i; break; }

            _root = new GameObject("UIAscended_ControlCenter");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5200; // above HUD (3800) and the radial
            // The extra vertex streams the shared glass materials may consume (the same opt-in
            // every glass canvas makes — see GridProfilePopup.BuildShell / TheGridPanel for the
            // full rationale). Without them a glass-material graphic on this canvas samples
            // undefined uv1..uv3/normal/tangent data.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.TexCoord3
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _raycaster = _root.AddComponent<GraphicRaycaster>();

            // Full-screen scrim (absorbs clicks behind the window; does not itself close).
            var scrim = UiaUi.Panel(_root.transform, UiaTheme.Scrim, "scrim");

            // Window panel — centered, fixed reference size.
            var winGo = UiaUi.Go("window", _root.transform);
            var win = (RectTransform)winGo.transform;
            _window = win;
            win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
            win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = WindowSize();   // 1450x950 reference, clamped to the real canvas
            // The window is a REAL HUD glass panel (PanelGraphic), not a flat Image, so it carries
            // the full F9 effect stack — the glowing edge-lit border a HUD box has. Shape + effects
            // are pushed every frame in StyleWindowPanel(); the corners/border/glow come from the
            // panel itself (no 9-slice sprite, no Outline component).
            _windowPanel = winGo.AddComponent<UI.Hud.PanelGraphic>();
            _windowPanel.raycastTarget = true; // still blocks clicks to the world behind it
            _windowPanel.color = UiaTheme.Window;
            _windowPanel.BorderColor = UiaTheme.Border;
            // The window EXTERIOR's moving edge light: a border-only flowing line laid over the
            // window's own edge. The window panel itself stays on the frost-capable glass
            // material (the frost backdrop needs it, and its glow halo takes its tint from its
            // border), so the sweep rides this overlay instead.
            _windowEdge = Kit.UiaGlassSkin.Add(winGo, UiaTheme.Border, -1f, -1f, flow: true);
            StyleWindowPanel();
            UiaUi.VLayout(win, 0f, (int)UiaTheme.Pad, (int)UiaTheme.Pad, (int)UiaTheme.Pad, (int)UiaTheme.Pad);

            BuildTitleBar(win);
            BuildMasterStrip(win);

            // Air between the master bars and the main tab peaks (combined visual round:
            // the tabs crowded the bars at half the concept's spacing).
            UiaUi.Size(UiaUi.Go("tab-air", win), 12f);

            // The manila-folder tab strip + content card (Kit v2, mockup 3's visual language):
            // the selected tab merges into the card the tabs build their pages onto. The zone
            // flexes to fill the rest of the window; pages build into the card's content area.
            var titles = new string[_tabs.Count];
            for (int i = 0; i < _tabs.Count; i++) titles[i] = _tabs[i].Title;
            // mainLevel: the MAIN strip's measured deltas (12px olive collar band with the
            // brighter top line; main-tab teals) — the sub strips take the default.
            _folderTabs = UiaComposite.FolderTabs(win, titles, _active, SelectTab, 170f, UiaTheme.TabH, true);
            _contentArea = _folderTabs.Content;

            // Popup layer — above the window so dropdown lists float over everything. Built
            // BEFORE the title bar's search box would first need it, but after the window: the
            // search box only opens its results while the window is open, and the layer pointer
            // is what everything mounts on.
            var popupGo = UiaUi.Go("popup-layer", _root.transform);
            _popupLayer = (RectTransform)popupGo.transform;
            UiaUi.Fill(_popupLayer);
            _popupLayer.SetAsLastSibling();
            UiaControls.PopupLayer = _popupLayer;

            _builtThemeHash = UiaMenuTheme.StyleHash(); // chrome now reflects this theme
            _root.SetActive(false);
        }

        /// <summary>Register the two out-of-F10 editors with the settings search. Idempotent
        /// (RegisterExternal replaces by label), so calling this every EnsureBuilt is harmless.</summary>
        private static void RegisterExternals()
        {
            UiaSearch.RegisterExternal("HUD Designer (F9)",
                "Layout, scale, colours and curvature for the visor HUD.",
                OpenHudDesignerExternal);
            UiaSearch.RegisterExternal("Legacy radial editor",
                "Wheel colours, glass, appearance and the key-hint strip, with a live black-screen preview.",
                OpenLegacyRadialEditorExternal);
        }

        /// <summary>Same open path as HudTab's "Open the HUD Designer (F9)" button.</summary>
        private static void OpenHudDesignerExternal()
        {
            try
            {
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) { Close(); inst.ToggleHudEditor(); }
            }
            catch { }
        }

        /// <summary>Same open path as RadialTab's "Open the legacy radial editor" button.</summary>
        private static void OpenLegacyRadialEditorExternal()
        {
            try
            {
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) { Close(); inst.ToggleSettingsWindow(); }
            }
            catch { }
        }

        /// <summary>The window's reference size: 1450x950 at the 1920x1080 reference resolution
        /// (mockup 3 needs the room — at 1000x660 its text lands at ~10px), CLAMPED to the real
        /// canvas so an odd aspect (ultrawide at low height, 4:3) never overflows the screen.
        /// The clamp mirrors CanvasScaler's matchWidthOrHeight=0.5 math (a logarithmic lerp of
        /// the two axis ratios), and never shrinks below the old 1000x660.</summary>
        private static Vector2 WindowSize()
        {
            const float RefW = 1450f, RefH = 950f;
            const float Margin = 24f;
            float w = RefW, h = RefH;
            try
            {
                float sw = Screen.width, sh = Screen.height;
                if (sw > 100f && sh > 100f)
                {
                    float logW = Mathf.Log(sw / 1920f, 2f);
                    float logH = Mathf.Log(sh / 1080f, 2f);
                    float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, 0.5f));
                    if (scale > 0.0001f)
                    {
                        w = Mathf.Min(RefW, sw / scale - Margin);
                        h = Mathf.Min(RefH, sh / scale - Margin);
                    }
                }
            }
            catch { }
            // KNOWN ENVELOPE: the 660 floor can itself overflow once the canvas is shorter than
            // ~684 units (660 + margin) — that needs an aspect wider than ~3.03:1 at match 0.5,
            // e.g. triple-surround 5760x1080 (canvas height ≈ 624). Accepted: below the floor
            // the layout would be unusable anyway, and a centred window that clips top/bottom
            // beats one that collapses. Revisit only if surround players actually report it.
            return new Vector2(Mathf.Max(1000f, w), Mathf.Max(660f, h));
        }

        private static void BuildTitleBar(RectTransform parent)
        {
            var barGo = UiaUi.Go("titlebar", parent);
            // flexH pinned 0: a LayoutGroup bubbles its children's flexible heights up, so one
            // stretchy child (the old search field) turned this fixed bar into a flexible one
            // that split the window's spare ~830 units 1:1 with the tab zone (2026-09-26).
            UiaUi.Size(barGo, UiaTheme.TitleH, flexH: 0f);
            var h = UiaUi.HLayout((RectTransform)barGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);

            // Leading spacer mirroring the right-hand cluster (search 250 + pause 34 + X 34 +
            // three 8px gaps = 342), so the flexible centred title is OPTICALLY centred too —
            // without it the heavier right side pushed "UI ASCENDED" ~160 units left of true.
            UiaUi.Size(UiaUi.Go("title-balance", barGo.transform), 30f, 342f, flexW: 0f);

            var titleGo = UiaUi.Go("title", barGo.transform);
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            // 31pt (combined visual round): the concept's title cap is ~0.67x the sub-tab
            // height — the old 22pt measured ~30% small. Local, not UiaTheme.TitleSize —
            // the item picker's titles must not inflate with it.
            title.font = UiaTheme.Font(); title.fontSize = 31f; title.color = UiaTheme.Text;
            title.alignment = TextAlignmentOptions.Center; title.raycastTarget = false; // centred (play-test ask)
            title.text = "UI ASCENDED"; title.characterSpacing = 8f; title.fontStyle = FontStyles.Bold;
            var tle = titleGo.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f;

            // Settings search (F10 plan principle 3): typing filters every registered row;
            // choosing one jumps to its tab and flashes it. Registered-externals (F9, radial
            // editor) surface here too once they register.
            UiaSearch.CreateBox(barGo.transform, 250f);
            _searchAnchor = barGo.transform.Find("search-host") as RectTransform;   // tutorial anchor "search"

            // Pause. Vanilla's own PauseIcon sprite when the runtime grab finds it (ASCII "||"
            // fallback - the TMP font has no pause glyph). Lit while WE hold the freeze; greyed
            // in multiplayer, where there is no pause to take (see GamePause.CanOwnPause).
            _pauseBtn = UiaControls.Button(barGo.transform, "||", TogglePause, 34f, 30f, UiaControls.ButtonStyle.Panel);
            UiaControls.SetButtonIcon(_pauseBtn, Core.VanillaIcons.PauseIcon(), 13f);

            var closeBtn = UiaControls.Button(barGo.transform, "X", Close, 34f, 30f, UiaControls.ButtonStyle.Panel);
            _closeAnchor = closeBtn != null ? (RectTransform)closeBtn.transform : null;   // tutorial anchor "close"
            RefreshPauseButton();

            // The title-bar row is one of the few places the MOVING edge light runs (search,
            // pause, X — FlorpyDorp 2026-09-26); every interior control stays static.
            foreach (var skin in barGo.GetComponentsInChildren<Kit.UiaGlassSkin>(true))
                skin.Flow = true;
        }

        // ---------- pause ----------

        /// <summary>Header "||": take or drop the shared single-player freeze. Our modal input-state key
        /// rides along so <c>GamePause</c> can put it back on top of the <c>Paused</c> state that
        /// <c>SetGamePause(true)</c> stomps it with — otherwise Esc opens the vanilla menu over us.</summary>
        private static void TogglePause()
        {
            if (GamePause.Held) GamePause.Release(PauseReason);
            else GamePause.Hold(PauseReason, InputStateKey);
            RefreshPauseButton();
        }

        /// <summary>Paint the pause button from the live world state. Disabled (and honestly so) when a
        /// pause cannot be owned — multiplayer, or a vanilla menu already holding it — unless the
        /// freeze is already ours, which must always stay releasable.</summary>
        private static void RefreshPauseButton()
        {
            if (_pauseBtn == null) return;
            bool held = GamePause.Held;
            _pauseBtn.SetSelected(held);
            // A vanilla-owned pause (Esc menu closed via prompt, console `pause`, joining client)
            // is NOT clickable-through: Hold would decline and the click would look broken.
            _pauseBtn.SetEnabled(held || (GamePause.CanOwnPause() && !WorldManager.IsGamePaused));
        }

        private static void OnPauseChanged(bool paused) { RefreshPauseButton(); }

        private static void HookPause()
        {
            if (_pauseHooked) return;
            _pauseHooked = true;
            GamePause.PausedChanged += _onPauseChanged;
        }

        private static void UnhookPause()
        {
            if (!_pauseHooked) return;
            _pauseHooked = false;
            GamePause.PausedChanged -= _onPauseChanged;
        }

        private static void BuildMasterStrip(RectTransform parent)
        {
            var stripGo = UiaUi.Go("master-strip", parent);
            _masterStrip = (RectTransform)stripGo.transform;   // tutorial anchors "master" / "master:*"
            // 45 = the measured 37-game-px bars + padding; padL/R 7 lands the bars 21 game
            // px in from the window border (combined visual round).
            UiaUi.Size(stripGo, 45f, flexH: 0f);   // fixed bar — same bubbling hardening as the title bar
            var h = UiaUi.HLayout((RectTransform)stripGo.transform, UiaTheme.Gap, 7, 7, 4, 4, TextAnchor.MiddleLeft, true);

            MasterSwitch(stripGo.transform, "RADIAL MENUS",
                () => UIAConfig.RadialEnabled.Value, v => UIAConfig.RadialEnabled.Value = v);
            MasterSwitch(stripGo.transform, "VISOR HUD",
                () => UI.Hud.HudConfig.VisorHudEnabled.Value, v => UI.Hud.HudConfig.VisorHudEnabled.Value = v);
        }

        private static void MasterSwitch(Transform parent, string label,
            System.Func<bool> get, System.Action<bool> set)
        {
            // The measured concept bar (combined visual round): teal-dark fill (#0C1A1E on
            // the shipped accent — UiaTheme.Bar's new resolution), radius-7 corners, a dim
            // rim with a lighter TOP edge (the concept's top-lit read).
            //
            // The rim is now the kit's FLOWING glass line (FlorpyDorp: the moving edge light on
            // every line) in the same dim #212C31-family tint, cornered to the bar's radius-7
            // sprite. It REPLACES both the static Outline component and the old 1px "toprim"
            // Image: the glass line's own edge light already lifts the top edge (the key-lit
            // catch), which is exactly the top-lit read the toprim faked — and a static 1px
            // strip coincident with that edge would sit still under the travelling light,
            // reading as a second, frozen line.
            var cell = UiaUi.Go("master", parent);
            var cimg = cell.AddComponent<Image>(); cimg.color = UiaTheme.Bar;
            Kit.UiaImages.RoundLarge(cimg);
            Kit.UiaGlassSkin.Add(cell, UiaTheme.AccentHsv(0.33f, 0.19f), -1f, 7f, flow: true);
            var le = cell.AddComponent<LayoutElement>(); le.flexibleWidth = 1f;
            var h = UiaUi.HLayout((RectTransform)cell.transform, UiaTheme.Gap, 10, 10, 0, 0, TextAnchor.MiddleLeft);

            var lblGo = UiaUi.Go("label", cell.transform);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UiaTheme.Font(); lbl.fontSize = UiaTheme.LabelSize; lbl.color = UiaTheme.Text;
            lbl.alignment = TextAlignmentOptions.Left; lbl.raycastTarget = false; lbl.text = label; lbl.characterSpacing = 3f;
            var lle = lblGo.AddComponent<LayoutElement>(); lle.flexibleWidth = 1f;

            var stateGo = UiaUi.Go("state", cell.transform);
            UiaUi.Size(stateGo, UiaTheme.RowH, 40f);
            var state = stateGo.AddComponent<TextMeshProUGUI>();
            state.font = UiaTheme.Font(); state.fontSize = UiaTheme.SmallSize;
            state.alignment = TextAlignmentOptions.Right; state.raycastTarget = false;

            System.Action paint = () =>
            {
                bool on = get();
                state.text = on ? "ON" : "OFF";
                state.color = on ? UiaTheme.On : UiaTheme.TextMute;
            };
            UiaControls.Switch(cell.transform, get(), v => { set(v); paint(); });
            paint();
        }

        private static void SelectTab(int i)
        {
            _active = Mathf.Clamp(i, 0, _tabs.Count - 1);
            // Persist for the next launch (Q16). Fail-soft: with no config bound this is a no-op.
            if (_tabs != null && _active < _tabs.Count)
                UiaMenuPrefs.LastTab = _tabs[_active].Title;
            BuildActiveTab();
            // Lessons (0.9.8.0): arg = index, text = the tab's (existing) title string.
            if (_tabs != null && _active < _tabs.Count)
                Tutorial.TutorialSignals.Raise(Tutorial.TSignal.MenuTabSelected, _active, _tabs[_active].Title);
        }

        /// <summary>Switch to the tab with this title and rebuild it (the settings search's
        /// jump path). No-op on an unknown title; safe while closed (the tab builds on Open).</summary>
        internal static void SelectTabByTitle(string title)
        {
            if (_tabs == null || string.IsNullOrEmpty(title)) return;
            for (int i = 0; i < _tabs.Count; i++)
                if (string.Equals(_tabs[i].Title, title, System.StringComparison.Ordinal))
                {
                    SelectTab(i);
                    return;
                }
        }

        /// <summary>Rebuild the current tab in place (tabs call this after an action that changes
        /// what the tab should show — applying a profile, toggling a setting that reveals more, …).
        /// Preserves the scroll position: a same-tab rebuild (e.g. picking a profile in a dropdown)
        /// must not fling the view back to the top.</summary>
        public static void Refresh()
        {
            if (_open) BuildActiveTab(true);
        }

        private static void BuildActiveTab() => BuildActiveTab(false);

        private static void BuildActiveTab(bool preserveScroll)
        {
            if (_contentArea == null || _tabs == null) return;

            // A same-tab rebuild (Refresh from a dropdown/toggle) should keep the reader where they
            // were, not reset to the top. Capture the outgoing scroll fraction before we clear.
            float scrollY = 1f;
            bool hadScroll = preserveScroll && TryCaptureScroll(out scrollY);

            // Clear (hide immediately to avoid a one-frame overlap, then destroy).
            for (int i = _contentArea.childCount - 1; i >= 0; i--)
            {
                var c = _contentArea.GetChild(i);
                c.gameObject.SetActive(false);
                Object.Destroy(c.gameObject);
            }
            if (_folderTabs != null) _folderTabs.SetSelected(_active);

            // Fresh search registrations for this tab: Build registers its rows, so the index
            // always mirrors what the page really shows (F10 plan §11's anti-drift rule).
            UiaSearch.BeginTab(_tabs[_active].Title);
            // Every tab is now Sections/More-options (Kit v2, 0.9.8.0): the global Simple/Advanced
            // density split is gone (ANSWERS #15), so every Build always gets the full content —
            // a straggler branch that still checks this argument shows its content rather than
            // hiding it.
            try { _tabs[_active].Build(_contentArea, true); }
            catch (System.Exception e) { UIALog.Error("Control Center tab build failed: " + e); }

            if (hadScroll) RestoreScroll(scrollY);
        }

        /// <summary>Read the current tab's scroll fraction (1 = top). False when there is no
        /// scroll view (some tabs don't use one).</summary>
        private static bool TryCaptureScroll(out float y)
        {
            y = 1f;
            if (_contentArea == null) return false;
            var sr = _contentArea.GetComponentInChildren<ScrollRect>();
            if (sr == null) return false;
            // Unity's own verticalNormalizedPosition reads as 0 (its "bottom" value) once the
            // content is short enough that it doesn't need to scroll at all - even though the
            // player is really looking at the (only) top of the list, with this kit's top-anchored
            // ScrollView content (see UiaUi.ScrollView). Opening a "More options" disclosure (or
            // any Refresh that reshapes the tab) can go straight from a short, non-scrolling body
            // to a much taller one; capturing that raw 0 and replaying it below dropped the player
            // at the genuine bottom of the new, taller content instead of back at the top they
            // actually had (FlorpyDorp, D-020, from the era of the old global Simple/Advanced
            // switch this bug first surfaced under). Match UiaScrollbar's own "nothing to scroll"
            // test (content-viewport <= 1px) and call that top, the same way GridSelection already
            // does for the identical Grid-side case.
            if (sr.content != null && sr.viewport != null
                && sr.content.rect.height - sr.viewport.rect.height <= 1f)
            {
                y = 1f;
                return true;
            }
            y = sr.verticalNormalizedPosition;
            return true;
        }

        /// <summary>Restore a scroll fraction onto the freshly-built tab. The ContentSizeFitter
        /// needs a layout pass before verticalNormalizedPosition is meaningful, so force one.</summary>
        private static void RestoreScroll(float y)
        {
            if (_contentArea == null) return;
            var sr = _contentArea.GetComponentInChildren<ScrollRect>();
            if (sr == null) return;
            Canvas.ForceUpdateCanvases();
            if (sr.content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(sr.content);
            sr.verticalNormalizedPosition = Mathf.Clamp01(y);
        }

        public static void Shutdown()
        {
            // Pause first, modal second (KeyManager unwind order) — and never leave a hot-reloaded
            // assembly's handler on vanilla's OnPaused, nor the world frozen by a dead window.
            GamePause.Release(PauseReason);
            UnhookPause();
            ReleaseModal();
            _open = false;
            _editPreview = false;
            IsRestyling = false;
            Tabs.StorageTab.ResetCaches();   // restyle-scoped loadout/bag caches hold Thing refs
            _builtThemeHash = 0;
            UI.Hud.HudGlobalGlass.FrostDemand = false;
            if (_windowPanel != null) UI.Hud.HudFxMaterials.Unassign(_windowPanel);
            UiaControls.PopupLayer = null;
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _window = null;
            _windowPanel = null;
            _windowEdge = null;
            _raycaster = null;
            _contentArea = null;
            _popupLayer = null;
            _tabs = null;
            _folderTabs = null;
            _pauseBtn = null;
            _searchAnchor = null;
            _closeAnchor = null;
            _masterStrip = null;
            _active = 0;
            Kit.UiaItemPicker.Reset();
            Tabs.RadialTab.ResetTransients();
            Tabs.ProfilesTab.ResetCaches();   // gesture caches added by the review fix wave
            // Kit v2 statics: inputs (focus registry + drafts), composite transients, the
            // search registry, page state, and the lazily-bound prefs — one reset each, here,
            // per the hot-reload house rule. The Esc-interceptor list and the open-dropdown
            // tracker clear too, so a reloaded assembly can never call into a dead one.
            _escInterceptors.Clear();
            UiaControls.OpenDropdown = null;
            UiaInputs.Reset();
            UiaComposite.Reset();
            UiaSearch.Reset();
            UiaPageState.Reset();
            UiaMenuPrefs.Reset();
            UiaImages.Clear();
            UiaTheme.Shutdown();
        }
    }
}
