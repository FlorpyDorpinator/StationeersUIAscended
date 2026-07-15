using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects;
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
        /// <summary>When true, BOTH gestures open the radial: tap = sticky, hold = transient
        /// (Option B's toolbelt). Takes precedence over OpenOnTap; OnTap/OnHold never run.</summary>
        bool OpensOnBoth { get; }
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

        // #3: Ctrl tap (swap toolbelt<->backpack) vs Ctrl+number (open a bound bag) disambiguation.
        private float _ctrlDownAt = -1f;
        private bool _ctrlConsumed;
        private const float CtrlTapSec = 0.35f;

        public bool IsRadialOpen => _menu.IsOpen;
        public IRadialFeature ActiveFeature => _active;

        public void Register(IRadialFeature feature) => _features.Add(feature);

        public void Update()
        {
            // 0.6.2: WASD/Space pass-through while a radial is open. Recomputed every
            // frame so it self-clears on close/search/seat; the patches read this flag.
            // Seat detection is an ALLOWLIST of on-foot control modes — review finding:
            // Human.LockedToSeat is a dead vanilla field (never assigned, always false),
            // while ControlMode is the live signal. Anything not walking/flying (Seated,
            // LyingDown, Ladder, Grab, cryo...) keeps the pass-through OFF, because
            // rover/shuttle pilot input reads the very AllowMouseControl gate we force.
            bool move = false;
            if (_menu.IsOpen && !_menu.IsSearchOpen
                && UIAConfig.RadialMovementEnabled != null && UIAConfig.RadialMovementEnabled.Value)
            {
                try
                {
                    var human = Guards.LocalHuman;
                    var mc = human != null ? human.MovementController : null;
                    if (mc != null)
                    {
                        var mode = mc.ControlMode;
                        move = mode == Assets.Scripts.MovementController.Mode.Animation
                            || mode == Assets.Scripts.MovementController.Mode.Jetpack
                            || mode == Assets.Scripts.MovementController.Mode.JetpackGravity;
                    }
                }
                catch { move = false; }
            }
            RadialMovement.Active = move;

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

            // #3: Ctrl+number opens a bound bag straight from gameplay (before the 1–6 feature keys).
            if (TryOpenBoundBagFromGameplay())
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
                if (feature.OpensOnBoth)
                {
                    // Option B toolbelt: tap opens the same radial LATCHED (tap a wedge to select).
                    OpenRadial(feature, sticky: true);
                }
                else if (feature.OpenOnTap)
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

            if (!feature.OpensOnBoth && feature.OpenOnTap)
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

            // #4: hover a setting wedge + press a letter to bind that key to the setting.
            _menu.UpdateHotkeyCapture();

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

            // #3: Ctrl-swap / 1–6 equipment jump / Ctrl+number bag-open / number-key bag bind.
            // Runs every frame (tracks the Ctrl tap); returns true when it changed the radial.
            if (UpdateRadialShortcuts()) return;

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
                // Option A hold mode: same drag behaviour as the tapped radial — move the
                // radial (hub drag), drag items off wedges, Alt-grab from the world, drop,
                // click-select (self-gates to Option A). A click-select/close tears down here.
                _menu.UpdateHoldInteractiveA();
                if (!_menu.IsOpen) { CloseAll(); return; }

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
                // Option B: while the key is held, LMB dives into branches (The Hub),
                // RMB backs out, and an executed action closes the menu right here.
                if (UIAConfig.IsB)
                {
                    _menu.UpdateHoldB();
                    if (!_menu.IsOpen)
                    {
                        CloseAll();
                        return;
                    }
                }
            }
            else
            {
                bool wasOpen = _menu.IsOpen;
                // While the search panel is typing, raw key presses are TEXT — the re-press
                // and MMB dismiss gestures must not fire (the panel handles its own exits).
                if (!_menu.IsSearchOpen)
                {
                    // Re-pressing the radial key closes a sticky radial — except Option B's
                    // MMB-keyed radial, where that press IS the select gesture (the menu
                    // handles it in UpdateSticky).
                    bool repressSelects = UIAConfig.IsB && _active != null
                        && _active.Key == KeyCode.Mouse2;
                    if (_active != null && !repressSelects && Input.GetKeyDown(_active.Key))
                    {
                        CloseAll();
                        return;
                    }
                    // Option A only: tapping middle mouse dismisses any sticky radial it
                    // doesn't own. In B the menu owns MMB (select / dismiss-on-empty).
                    if (UIAConfig.IsA && !UIAConfig.IsB && Input.GetMouseButtonDown(2)
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

        // ---------- #3: radial switching + bag hotkeys ----------

        /// <summary>While a radial is open: Ctrl-tap swaps toolbelt&lt;-&gt;backpack, plain 1–6 jumps to
        /// that equipment radial, hovering a bindable bag + a number BINDS it to Ctrl+&lt;number&gt;, and
        /// Ctrl+&lt;number&gt; opens the bound bag. Returns true when it changed the open radial or bound a
        /// bag, so the caller stops for this frame. Search mode owns its own keys.</summary>
        private bool UpdateRadialShortcuts()
        {
            if (_menu.IsSearchOpen) return false;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
            {
                _ctrlDownAt = Time.unscaledTime;
                _ctrlConsumed = false;
            }

            for (int slot = 0; slot < BagHotkeyStore.SlotCount; slot++)
            {
                if (!Input.GetKeyDown(BagHotkeyStore.KeyForSlot(slot))) continue;

                if (ctrl)
                {
                    _ctrlConsumed = true; // a combo, not a tap — so the release won't also swap
                    var bag = BagHotkeyStore.ResolveBag(slot);
                    if (bag != null) OpenBagRadial(bag);
                    else UIAudioManager.Play(UIAudioManager.ActionFailHash);
                    return true;
                }

                // Hovering a bindable bag: bind it to Ctrl+<this digit>.
                var hov = _menu.HoveredEntry();
                if (hov != null && hov.BindableBag != null)
                {
                    BagHotkeyStore.Bind(slot, hov.BindableBag);
                    UIAudioManager.Play(UIAudioManager.ClickLightHash);
                    _menu.RefreshAll(); // repaint the digit badge
                    return true;
                }

                // Plain 1–6: switch straight to that equipment's radial.
                int digit = BagHotkeyStore.DisplayDigit(slot);
                if (digit >= 1 && digit <= 6)
                {
                    var eq = EquipFeatureForDigit(digit);
                    if (eq != null) { SwitchToFeature(eq); return true; }
                }
                return true; // a number press is always consumed while a radial is open
            }

            // Ctrl released with no number pressed during the hold = a tap = swap.
            if (Input.GetKeyUp(KeyCode.LeftControl) || Input.GetKeyUp(KeyCode.RightControl))
            {
                bool tap = !_ctrlConsumed && _ctrlDownAt >= 0f && Time.unscaledTime - _ctrlDownAt < CtrlTapSec;
                _ctrlDownAt = -1f;
                if (tap) return SwapToolbeltBackpack();
            }
            return false;
        }

        /// <summary>Ctrl+&lt;number&gt; from normal gameplay (no radial open) opens the bound bag. Consumes
        /// the press so it never falls through to a vanilla/equipment 1–6 action. Returns true when the
        /// combo was handled.</summary>
        private bool TryOpenBoundBagFromGameplay()
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return false;
            for (int slot = 0; slot < BagHotkeyStore.SlotCount; slot++)
            {
                if (!Input.GetKeyDown(BagHotkeyStore.KeyForSlot(slot))) continue;
                var bag = BagHotkeyStore.ResolveBag(slot);
                if (bag != null) OpenBagRadial(bag);
                // Ctrl+digit belongs to bag hotkeys either way — consume it silently so an
                // unbound slot never falls through to the equipment 1–6 keys.
                return true;
            }
            return false;
        }

        /// <summary>Swap the OPEN radial between the toolbelt and the backpack-slot container. Only
        /// acts when the current radial is one of those two (the hub / others do nothing, per design).
        /// Returns true when it swapped.</summary>
        private bool SwapToolbeltBackpack()
        {
            if (_active is ToolbeltRadialFeature)
            {
                var back = EquipFeatureForDigit(4); // BackSlot
                if (back != null) { SwitchToFeature(back); return true; }
            }
            else if (_active is EquipmentKeyRadialFeature e && e.ButtonName == "BackSlot")
            {
                var tb = ToolbeltFeature();
                if (tb != null) { SwitchToFeature(tb); return true; }
            }
            return false;
        }

        /// <summary>Reopen the (sticky) radial on another feature's root, keeping the modal — the
        /// switch used by Ctrl-swap and 1–6.</summary>
        private void SwitchToFeature(IRadialFeature f)
        {
            if (f == null || !f.Enabled || !f.CanOpen())
            {
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            _active = f;
            if (!_modal.IsOpen) _modal.Open();
            _menu.Open(f.Title, f.BuildRoot, sticky: true);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        /// <summary>Open a specific bag's radial (Ctrl+number activation), opening the modal if needed.</summary>
        private void OpenBagRadial(DynamicThing bag)
        {
            if (bag == null) return;
            var b = bag;
            _active = BagFeature(); // owner key = Tab, for close/re-press (null-safe)
            if (!_modal.IsOpen) _modal.Open();
            _menu.Open(b.DisplayName, () => BagRadialFeature.BuildBagLevel(b), sticky: true);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        private EquipmentKeyRadialFeature EquipFeatureForDigit(int digit)
        {
            if (digit < 1 || digit > 6) return null;
            string name = EquipmentKeyRadialFeature.ButtonNames[digit - 1];
            foreach (var f in _features)
                if (f is EquipmentKeyRadialFeature e && e.ButtonName == name) return e;
            return null;
        }

        private IRadialFeature ToolbeltFeature()
        {
            foreach (var f in _features)
                if (f is ToolbeltRadialFeature) return f;
            return null;
        }

        private IRadialFeature BagFeature()
        {
            foreach (var f in _features)
                if (f is BagRadialFeature) return f;
            return null;
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
            RadialMovement.Active = false; // hot-reload safety: never strand the pass-through
            _menu.Close();
            _modal.Close();
            UI.UnityRadialView.Shutdown(); // destroy the canvases, or every hot reload stacks another
            UI.ParkedItemsView.Shutdown();
            UI.SearchPanelView.Shutdown();
            Windows.RadialEditorMode.Shutdown();
            BagHotkeyStore.Reset(); // drop in-memory binds; the per-save file is untouched
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

