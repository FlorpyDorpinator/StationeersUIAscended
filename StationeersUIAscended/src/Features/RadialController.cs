using System.Collections.Generic;
using Assets.Scripts;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
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
                // Re-pressing the radial key closes a sticky radial.
                if (_active != null && Input.GetKeyDown(_active.Key))
                {
                    CloseAll();
                    return;
                }
                _menu.UpdateSticky();
                if (wasOpen && !_menu.IsOpen)
                {
                    CloseAll();
                    return;
                }
            }
        }

        public void Draw()
        {
            if (_menu.IsOpen) _menu.Draw();
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
