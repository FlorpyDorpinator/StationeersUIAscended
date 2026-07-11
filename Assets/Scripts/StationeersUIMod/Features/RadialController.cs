using System.Collections.Generic;
using Assets.Scripts;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>A feature that owns one radial key.</summary>
    public interface IRadialFeature
    {
        string Title { get; }
        bool Enabled { get; }
        KeyCode Key { get; }
        /// <summary>When true the radial opens on TAP (sticky) and OnHold() runs on a long press.
        /// When false the radial opens on HOLD and OnTap() re-dispatches the vanilla tap.</summary>
        bool OpenOnTap { get; }
        /// <summary>Cheap pre-check before anything happens (e.g. "holding a tool").</summary>
        bool CanOpen();
        List<RadialEntry> BuildRoot();
        void OnTap();
        void OnHold();
    }

    /// <summary>
    /// Drives all radial menus: tap-vs-hold gating per feature, one shared RadialMenu,
    /// one shared ModalScope. Only one radial can be open at a time.
    /// </summary>
    public sealed class RadialController
    {
        private readonly List<IRadialFeature> _features = new List<IRadialFeature>();
        private readonly RadialMenu _menu = new RadialMenu();
        private readonly ModalScope _modal = new ModalScope("UIAscended_Radial");

        private IRadialFeature _pending;   // key down, waiting to resolve tap vs hold
        private float _pendingSince;
        private IRadialFeature _active;    // radial open
        private KeyCode _releaseKey;       // key to wait out during a deferred modal release

        public bool IsRadialOpen => _menu.IsOpen;
        public IRadialFeature ActiveFeature => _active;

        public void Register(IRadialFeature feature) => _features.Add(feature);

        public void Update()
        {
            if (_menu.IsOpen)
            {
                UpdateOpen();
                return;
            }

            // Finish a deferred modal release: the Typing state is held until the closing
            // keys (Escape/RMB/hold key) are physically up, so their key-UP events can't
            // fire vanilla actions (pause menu, ToggleActiveHandTool).
            if (_modal.IsOpen)
            {
                _modal.RequestDeferredClose();
                _modal.Pump(_releaseKey);
                return;
            }

            if (!Guards.CanAcceptGameplayInput())
            {
                _pending = null;
                return;
            }

            if (_pending != null)
            {
                UpdatePending();
                return;
            }

            foreach (var feature in _features)
            {
                if (!feature.Enabled || feature.Key == KeyCode.None) continue;
                if (Input.GetKeyDown(feature.Key))
                {
                    _pending = feature;
                    _pendingSince = Time.unscaledTime;
                    return;
                }
            }
        }

        private void UpdatePending()
        {
            var feature = _pending;
            float heldMs = (Time.unscaledTime - _pendingSince) * 1000f;

            if (!Input.GetKey(feature.Key))
            {
                _pending = null;
                if (feature.OpenOnTap)
                {
                    // Tap opens the management radial (sticky — the key is already up).
                    OpenRadial(feature, sticky: true);
                }
                else
                {
                    // Tap falls through to the vanilla action for this key.
                    feature.OnTap();
                }
                return;
            }

            if (heldMs < UIAConfig.HoldThresholdMs.Value) return;
            _pending = null;

            if (feature.OpenOnTap)
            {
                // Long press = the feature's hold action (e.g. equip to hand).
                feature.OnHold();
                return;
            }
            OpenRadial(feature, sticky: false);
        }

        private void OpenRadial(IRadialFeature feature, bool sticky)
        {
            if (!feature.CanOpen())
            {
                // No radial to show — give tap-features their hold action instead
                // (e.g. tap 3 with an empty suit slot dons the suit from your hand).
                if (feature.OpenOnTap) feature.OnHold();
                else UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            _active = feature;
            _modal.Open();
            _menu.Open(feature.Title, feature.BuildRoot, sticky);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        private void UpdateOpen()
        {
            // Bail out whenever the world stops being interactable or vanilla UI takes over
            // (pause, console, keyboard windows, Stationpedia, creative menu, unconscious).
            if (!Guards.CanKeepRadialOpen())
            {
                CloseAll();
                return;
            }

            // Option A: scroll-wheel value adjust works in both hold and sticky modes.
            if (UIAConfig.IsA) _menu.UpdateScroll();

            // E swaps the active hand while any radial is open (never while the search
            // panel is typing — E is a letter there). Q flips pages on crowded rings.
            if (!_menu.IsSearchOpen)
            {
                UpdateHandSwitch();
                var pageKey = UIAConfig.RadialPageKey.Value;
                if (pageKey != KeyCode.None && pageKey != (_active != null ? _active.Key : KeyCode.None)
                    && Input.GetKeyDown(pageKey))
                    _menu.NextPage();
            }

            // Keep the vanilla active-hand ring alive. Unlocking the cursor flips
            // InputMouse.IsMouseControl, whose RefreshAnimation clause hides the ring
            // (SlotDisplayButton.cs:319). Re-asserting the public state each frame is
            // vanilla's own pattern (InventoryManager.cs:969) and no-ops when unchanged.
            try
            {
                var hand = Assets.Scripts.Inventory.InventoryManager.Instance?.ActiveHand;
                var btn = hand != null ? hand.SlotDisplayButton : null;
                if (btn != null) btn.SetDisplayAnimState(Assets.Scripts.UI.SlotDisplayState.Highlighted);
            }
            catch { }

            if (!_menu.IsSticky)
            {
                if (_active != null && !Input.GetKey(_active.Key))
                {
                    bool stayOpen = _menu.OnHoldReleased();
                    if (!stayOpen) CloseAll();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseAll();
                    return;
                }
            }
            else
            {
                bool wasOpen = _menu.IsOpen;
                // While the search panel is typing, raw key presses are TEXT — the re-press
                // and MMB dismiss gestures must not fire (the panel handles its own exits).
                if (!_menu.IsSearchOpen)
                {
                    // Re-pressing the radial key closes a sticky radial.
                    if (_active != null && Input.GetKeyDown(_active.Key))
                    {
                        CloseAll();
                        return;
                    }
                    // Option A: tapping middle mouse dismisses any sticky radial it doesn't own.
                    if (UIAConfig.IsA && Input.GetMouseButtonDown(2)
                        && _active != null && _active.Key != KeyCode.Mouse2)
                    {
                        CloseAll();
                        return;
                    }
                }
                _menu.UpdateSticky();
                if (wasOpen && !_menu.IsOpen)
                {
                    CloseAll();
                    return;
                }
            }
        }

        /// <summary>
        /// E swaps the active hand while a radial is open (configurable; Q is reserved for
        /// a future gesture). The modal holds the key in the Typing state, so vanilla can't
        /// see it (its own E-swap binding is KeyInputState.Game-gated) — we drive the same
        /// vanilla toggle underneath: HumanHandsBehaviour.SwapHands(), the exact path the
        /// vanilla E key runs, behind its own gates (smart tool, unresponsive, sleeping).
        /// After a switch the radial rebuilds: STOW previews and equip verbs follow the
        /// active hand.
        /// </summary>
        private void UpdateHandSwitch()
        {
            KeyCode key = UIAConfig.RadialHandSwapKey.Value;
            KeyCode ownKey = _active != null ? _active.Key : KeyCode.None;
            if (key == KeyCode.None || key == ownKey || !Input.GetKeyDown(key)) return;

            var human = Guards.LocalHuman;
            if (human == null) return;
            try
            {
                var im = Assets.Scripts.Inventory.InventoryManager.Instance;
                if (im == null || im.IsUsingSmartTool) return;       // vanilla's own swap gates
                if (human.IsUnresponsive || human.IsSleeping) return;

                human.HumanHandsBehaviour.SwapHands();
                UIAudioManager.Play(UIAudioManager.ClickLightHash);
                _menu.RefreshAll();
            }
            catch (System.Exception e)
            {
                UIALog.Warn("Hand switch failed: " + e.Message);
            }
        }

        public void Draw()
        {
            if (_menu.IsOpen)
            {
                _menu.Draw();
                return;
            }
            // UGUI canvases must be explicitly hidden when closed (or every reload stacks).
            UI.UnityRadialView.Hide();
            UI.ParkedItemsView.Hide();
            UI.SearchPanelView.Hide();
        }

        public void CloseAll()
        {
            _menu.Close();
            _releaseKey = _active?.Key ?? KeyCode.None;
            _modal.RequestDeferredClose();
            _modal.Pump(_releaseKey);
            _active = null;
            _pending = null;
        }

        /// <summary>Immediate teardown (plugin OnDestroy / hot reload) — no deferred release.</summary>
        public void ShutdownImmediate()
        {
            _menu.Close();
            _modal.Close();
            UI.UnityRadialView.Shutdown(); // destroy the canvases, or every hot reload stacks another
            UI.ParkedItemsView.Shutdown();
            UI.SearchPanelView.Shutdown();
            Windows.RadialEditorMode.Shutdown();
            _active = null;
            _pending = null;
        }

        /// <summary>True while this controller owns the given key (pending hold or open radial).</summary>
        public bool OwnsKey(KeyCode key)
        {
            if (_pending != null && _pending.Key == key) return true;
            if (_menu.IsOpen && _active != null && _active.Key == key) return true;
            return false;
        }
    }
}

