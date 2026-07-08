using System.Collections.Generic;
using Assets.Scripts;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
{
    /// <summary>A feature that owns one hold-key radial (toolbelt / tool / bags).</summary>
    public interface IRadialFeature
    {
        string Title { get; }
        bool Enabled { get; }
        KeyCode Key { get; }
        /// <summary>Cheap pre-check before the hold timer even starts (e.g. "holding a tool").</summary>
        bool CanOpen();
        List<RadialEntry> BuildRoot();
        /// <summary>Key released before the hold threshold: re-dispatch the vanilla tap action, if any.</summary>
        void OnTap();
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

        private IRadialFeature _pending;   // key down, waiting for hold threshold
        private float _pendingSince;
        private IRadialFeature _active;    // radial open

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

            // Session ended externally (menu closed itself) — release the modal.
            if (_modal.IsOpen) _modal.Close();

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
            if (!Input.GetKey(feature.Key))
            {
                // Tap: fall through to the vanilla action for this key.
                _pending = null;
                feature.OnTap();
                return;
            }
            float heldMs = (Time.unscaledTime - _pendingSince) * 1000f;
            if (heldMs < UIAConfig.HoldThresholdMs.Value) return;

            _pending = null;
            if (!feature.CanOpen())
            {
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            var entries = feature.BuildRoot();
            _active = feature;
            _modal.Open();
            _menu.Open(feature.Title, entries);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        private void UpdateOpen()
        {
            // Bail out of the radial whenever the world stops being interactable.
            if (GameManager.GameState != Assets.Scripts.GridSystem.GameState.Running
                || WorldManager.IsGamePaused
                || ConsoleWindow.IsOpen
                || Guards.LocalHuman == null)
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
