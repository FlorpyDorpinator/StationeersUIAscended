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
    /// content area; <paramref name="advanced"/> asks for the full firehose vs the curated set.</summary>
    public interface IUiaTab
    {
        string Title { get; }
        void Build(RectTransform content, bool advanced);
    }

    /// <summary>
    /// The UGUI "UI Ascended Control Center": the player-facing front door to the mod. A single
    /// screen-space window with two always-visible master switches (Radial half / HUD half), a
    /// tab bar, and a Simple ⇄ Advanced density toggle. Built entirely in code (hot-reload safe),
    /// EventSystem-driven, and modal (cursor unlocked + game keys captured, but UGUI clicks left
    /// alive so its own controls work — unlike the radial's <see cref="ModalScope"/>).
    ///
    /// Static + pumped from StationeersUIMod.Update, mirroring HudSystem/UnityRadialView.
    /// </summary>
    public static class UiaControlCenter
    {
        private sealed class Modal : IModal { public bool UnlockCursor => true; }

        private const string InputStateKey = "UIA_ControlCenter";

        private static GameObject _root;
        private static RectTransform _window;          // the centred window panel (hit-test target)
        private static UI.Hud.PanelGraphic _windowPanel; // the window's glass surface (HUD PanelGraphic)
        private static GraphicRaycaster _raycaster;    // toggled off in edit-preview (F9 owns clicks)
        private static RectTransform _contentArea;
        private static RectTransform _popupLayer;
        private static readonly Modal _modal = new Modal();

        private static List<IUiaTab> _tabs;
        private static List<UiaControls.UiaButton> _tabButtons;
        private static int _active;
        private static bool _advanced;
        private static bool _open;
        private static bool _modalHeld;
        private static bool _editPreview;              // open behind the active F9 editor = click-to-edit
        private static int _builtThemeHash;            // UiaMenuTheme.StyleHash at last (re)build
        private static float _lastRestyle;             // unscaled time of the last live restyle
        private const float RestyleMinInterval = 0.14f; // throttle: a live palette drag can't rebuild every frame

        private static UiaControls.UiaButton _simpleBtn, _advancedBtn;

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
            EnsureBuilt();
            if (_root == null) return;
            _open = true;
            _root.SetActive(true);
            // Opened behind the active HUD editor = "edit preview": the menu becomes a live-themed
            // static surface the F9 editor can click to edit, so it must not grab input/cursor or
            // absorb clicks through its own raycaster.
            ApplyEditPreview(Windows.HudEditorMode.Active);
            BuildActiveTab();
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
            UI.Hud.HudGlobalGlass.FrostDemand =
                fxLive && UI.Hud.HudConfig.FxTierC != null && UI.Hud.HudConfig.FxTierC.Value;
            UI.Hud.HudGlobalGlass.Apply(_windowPanel, includeGlow: true, wantFrost: fxLive, wantTierB: fxLive);
        }

        /// <summary>Enter/leave edit-preview: in preview the menu yields all input to the F9
        /// editor (raycaster off, no game modal); out of preview it is the normal modal window.</summary>
        private static void ApplyEditPreview(bool on)
        {
            _editPreview = on;
            if (_raycaster != null) _raycaster.enabled = !on;
            if (on) ReleaseModal();   // F9 already owns cursor + input for editing
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
            // Clear any floating dropdown popups / catchers so none reactivate on reopen.
            if (_popupLayer != null)
                for (int i = _popupLayer.childCount - 1; i >= 0; i--)
                    Object.Destroy(_popupLayer.GetChild(i).gameObject);
            if (_root != null) _root.SetActive(false);
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

        /// <summary>Pumped every frame from StationeersUIMod.Update.</summary>
        public static void Update()
        {
            if (!_open) return;
            // The window is live HUD glass — push this frame's theme colour + F9 global effects
            // (glow, edge light, ripple, frost) onto it, exactly like a HUD box updates each frame.
            StyleWindowPanel();
            // Keep edit-preview in lock-step with the F9 editor: entering/leaving the editor while
            // the menu is open flips it between "editable surface" and normal modal window.
            if (_editPreview != Windows.HudEditorMode.Active)
                ApplyEditPreview(Windows.HudEditorMode.Active);
            // Follow the live HUD theme: rebuild when the derived (or overridden) palette changes,
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
            // In edit-preview the F9 editor owns Escape (and F10 re-press closes the menu); outside
            // it, Escape closes the window as usual.
            if (!_editPreview && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
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
            bool advanced = _advanced;
            // Capture scroll BEFORE the whole root (incl. the scroll view) is destroyed, so a
            // live theme drag while scrolled down doesn't fling the view to the top each rebuild.
            float scrollY;
            bool hadScroll = TryCaptureScroll(out scrollY);
            // Forget any frost material on the window graphic before it dies, so its dictionary
            // entry doesn't dangle across restyle churn.
            if (_windowPanel != null) UI.Hud.HudFxMaterials.Unassign(_windowPanel);
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _window = null;
            _windowPanel = null;
            _raycaster = null;
            _contentArea = null;
            _popupLayer = null;
            _tabButtons = null;
            _simpleBtn = null;
            _advancedBtn = null;
            UiaControls.PopupLayer = null;
            EnsureBuilt();
            _active = Mathf.Clamp(active, 0, _tabs != null ? _tabs.Count - 1 : 0);
            _advanced = advanced;
            RefreshDensityButtons();
            if (wasOpen && _root != null)
            {
                _open = true;
                _root.SetActive(true);
                ApplyEditPreview(wasPreview);
                BuildActiveTab();
                if (hadScroll) RestoreScroll(scrollY);
            }
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
            };

            _root = new GameObject("UIAscended_ControlCenter");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5200; // above HUD (3800) and the radial
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
            win.sizeDelta = new Vector2(980f, 660f);
            // The window is a REAL HUD glass panel (PanelGraphic), not a flat Image, so it carries
            // the full F9 effect stack — the glowing edge-lit border a HUD box has. Shape + effects
            // are pushed every frame in StyleWindowPanel(); the corners/border/glow come from the
            // panel itself (no 9-slice sprite, no Outline component).
            _windowPanel = winGo.AddComponent<UI.Hud.PanelGraphic>();
            _windowPanel.raycastTarget = true; // still blocks clicks to the world behind it
            _windowPanel.color = UiaTheme.Window;
            _windowPanel.BorderColor = UiaTheme.Border;
            StyleWindowPanel();
            UiaUi.VLayout(win, 0f, (int)UiaTheme.Pad, (int)UiaTheme.Pad, (int)UiaTheme.Pad, (int)UiaTheme.Pad);

            BuildTitleBar(win);
            BuildMasterStrip(win);
            BuildTabBar(win);

            // Content area (fills the rest).
            var contentGo = UiaUi.Go("content-area", win);
            _contentArea = (RectTransform)contentGo.transform;
            UiaUi.Size(contentGo, flexH: 1f);

            // Popup layer — above the window so dropdown lists float over everything.
            var popupGo = UiaUi.Go("popup-layer", _root.transform);
            _popupLayer = (RectTransform)popupGo.transform;
            UiaUi.Fill(_popupLayer);
            _popupLayer.SetAsLastSibling();
            UiaControls.PopupLayer = _popupLayer;

            _builtThemeHash = UiaMenuTheme.StyleHash(); // chrome now reflects this theme
            _root.SetActive(false);
        }

        private static void BuildTitleBar(RectTransform parent)
        {
            var barGo = UiaUi.Go("titlebar", parent);
            UiaUi.Size(barGo, UiaTheme.TitleH);
            var h = UiaUi.HLayout((RectTransform)barGo.transform, UiaTheme.Gap, 0, 0, 0, 0, TextAnchor.MiddleLeft);

            var titleGo = UiaUi.Go("title", barGo.transform);
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.font = UiaTheme.Font(); title.fontSize = UiaTheme.TitleSize; title.color = UiaTheme.Text;
            title.alignment = TextAlignmentOptions.Center; title.raycastTarget = false; // centred (play-test ask)
            title.text = "UI ASCENDED"; title.characterSpacing = 8f; title.fontStyle = FontStyles.Bold;
            var tle = titleGo.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f;

            // Simple / Advanced segmented toggle.
            _simpleBtn = UiaControls.Button(barGo.transform, "Simple", () => SetAdvanced(false), 96f, 30f, UiaControls.ButtonStyle.Panel);
            _advancedBtn = UiaControls.Button(barGo.transform, "Advanced", () => SetAdvanced(true), 108f, 30f, UiaControls.ButtonStyle.Panel);

            UiaControls.Button(barGo.transform, "X", Close, 34f, 30f, UiaControls.ButtonStyle.Panel);
            RefreshDensityButtons();
        }

        private static void BuildMasterStrip(RectTransform parent)
        {
            var stripGo = UiaUi.Go("master-strip", parent);
            UiaUi.Size(stripGo, 46f);
            var h = UiaUi.HLayout((RectTransform)stripGo.transform, UiaTheme.Gap, 0, 0, 4, 4, TextAnchor.MiddleLeft, true);

            MasterSwitch(stripGo.transform, "RADIAL MENUS",
                () => UIAConfig.RadialEnabled.Value, v => UIAConfig.RadialEnabled.Value = v);
            MasterSwitch(stripGo.transform, "VISOR HUD",
                () => UI.Hud.HudConfig.VisorHudEnabled.Value, v => UI.Hud.HudConfig.VisorHudEnabled.Value = v);
        }

        private static void MasterSwitch(Transform parent, string label,
            System.Func<bool> get, System.Action<bool> set)
        {
            var cell = UiaUi.Go("master", parent);
            var cimg = cell.AddComponent<Image>(); cimg.color = UiaTheme.Panel;
            Kit.UiaImages.Round(cimg);
            UiaUi.OutlineOf(cimg, UiaTheme.Divider, 1f);
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

        private static void BuildTabBar(RectTransform parent)
        {
            var barGo = UiaUi.Go("tabbar", parent);
            UiaUi.Size(barGo, UiaTheme.TabH);
            UiaUi.HLayout((RectTransform)barGo.transform, 4f, 0, 0, 6, 4, TextAnchor.LowerLeft);
            _tabButtons = new List<UiaControls.UiaButton>();
            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                var btn = UiaControls.Button(barGo.transform, _tabs[i].Title, () => SelectTab(idx), 130f, UiaTheme.TabH - 6f, UiaControls.ButtonStyle.Panel);
                _tabButtons.Add(btn);
            }
        }

        private static void SelectTab(int i)
        {
            _active = Mathf.Clamp(i, 0, _tabs.Count - 1);
            BuildActiveTab();
        }

        /// <summary>Rebuild the current tab in place (tabs call this after an action that changes
        /// what the tab should show — applying a profile, toggling a setting that reveals more, …).
        /// Preserves the scroll position: a same-tab rebuild (e.g. picking a profile in a dropdown)
        /// must not fling the view back to the top.</summary>
        public static void Refresh()
        {
            if (_open) BuildActiveTab(true);
        }

        public static bool Advanced => _advanced;

        private static void SetAdvanced(bool adv)
        {
            if (_advanced == adv) return;
            _advanced = adv;
            RefreshDensityButtons();
            BuildActiveTab(true);
        }

        private static void RefreshDensityButtons()
        {
            if (_simpleBtn != null) _simpleBtn.SetSelected(!_advanced);
            if (_advancedBtn != null) _advancedBtn.SetSelected(_advanced);
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
            for (int i = 0; i < _tabButtons.Count; i++)
                _tabButtons[i].SetSelected(i == _active);

            try { _tabs[_active].Build(_contentArea, _advanced); }
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
            _raycaster = null;
            _contentArea = null;
            _popupLayer = null;
            _tabs = null;
            _tabButtons = null;
            _simpleBtn = null;
            _advancedBtn = null;
            _active = 0;
            _advanced = false;
            Kit.UiaItemPicker.Reset();
            UiaImages.Clear();
            UiaTheme.Shutdown();
        }
    }
}
