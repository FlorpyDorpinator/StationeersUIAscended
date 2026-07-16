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
        private static RectTransform _contentArea;
        private static RectTransform _popupLayer;
        private static readonly Modal _modal = new Modal();

        private static List<IUiaTab> _tabs;
        private static List<UiaControls.UiaButton> _tabButtons;
        private static int _active;
        private static bool _advanced;
        private static bool _open;
        private static bool _modalHeld;

        private static UiaControls.UiaButton _simpleBtn, _advancedBtn;

        public static bool IsOpen => _open;

        public static void Toggle() { if (_open) Close(); else Open(); }

        public static void Open()
        {
            EnsureBuilt();
            if (_root == null) return;
            _open = true;
            _root.SetActive(true);
            AcquireModal();
            BuildActiveTab();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
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
            // A rebind is capturing the next key — it owns input (Esc cancels the capture, not
            // the window).
            if (UiaRebindCapture.Active) { UiaRebindCapture.Tick(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            // If the world goes away (menu/loading), never leave the window stranded.
            if (!Guards.CanDraw()) Close();
        }

        // ---------- modal ----------

        private static void AcquireModal()
        {
            if (_modalHeld) return;
            _modalHeld = true;
            try { KeyManager.SetInputState(InputStateKey, KeyInputState.Typing); } catch { }
            try { MouseModeController.AddModal(_modal); } catch { }
            try { if (CursorManager.Instance != null) CursorManager.Instance.BlockCursorRaycast = true; } catch { }
        }

        private static void ReleaseModal()
        {
            if (!_modalHeld) return;
            _modalHeld = false;
            try { KeyManager.RemoveInputState(InputStateKey); } catch { }
            try { MouseModeController.RemoveModal(_modal); } catch { }
            try
            {
                if (CursorManager.Instance != null)
                {
                    CursorManager.Instance.BlockCursorRaycast = false;
                    CursorManager.Instance.OnApplicationFocus(true); // re-lock the cursor next frame
                }
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
            _root.AddComponent<GraphicRaycaster>();

            // Full-screen scrim (absorbs clicks behind the window; does not itself close).
            var scrim = UiaUi.Panel(_root.transform, UiaTheme.Scrim, "scrim");

            // Window panel — centered, fixed reference size.
            var winGo = UiaUi.Go("window", _root.transform);
            var win = (RectTransform)winGo.transform;
            win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
            win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = new Vector2(980f, 660f);
            var winImg = winGo.AddComponent<Image>();
            winImg.color = UiaTheme.Window;
            Kit.UiaImages.Round(winImg); // rounded window corners (the kit-wide curved look)
            UiaUi.OutlineOf(winImg, UiaTheme.AccentDim, 1.5f);
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
        /// what the tab should show — applying a profile, toggling a setting that reveals more, …).</summary>
        public static void Refresh()
        {
            if (_open) BuildActiveTab();
        }

        public static bool Advanced => _advanced;

        private static void SetAdvanced(bool adv)
        {
            if (_advanced == adv) return;
            _advanced = adv;
            RefreshDensityButtons();
            BuildActiveTab();
        }

        private static void RefreshDensityButtons()
        {
            if (_simpleBtn != null) _simpleBtn.SetSelected(!_advanced);
            if (_advancedBtn != null) _advancedBtn.SetSelected(_advanced);
        }

        private static void BuildActiveTab()
        {
            if (_contentArea == null || _tabs == null) return;
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
        }

        public static void Shutdown()
        {
            ReleaseModal();
            _open = false;
            UiaControls.PopupLayer = null;
            if (_root != null) Object.Destroy(_root);
            _root = null;
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
