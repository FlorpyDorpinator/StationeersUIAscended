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

        // #3: Ctrl still opens a bound bag (Ctrl+number). The toolbelt<->backpack SWAP moved off
        // Ctrl onto Tab (see UpdateRadialShortcuts / SwapToolbeltBackpack), so no tap-timing state
        // is needed any more — a Tab press while a wheel is open is unambiguously "swap".

        // R2 flick-commit: mouse position (ImGui space) captured at key-down, so a fast
        // directional flick can resolve a wedge sector without ever drawing the ring.
        private Vector2 _pendingMouse;

        // R3 double-tap repeat: the last feature TAPPED and when, to detect a fast second tap
        // (a per-feature tap-timing pattern).
        private IRadialFeature _lastTapFeature;
        private float _lastTapAt = -1f;

        // Ad-hoc radial (opened by a surface that is not a feature key — The Grid's right-click).
        // Held so the opener can restore its OWN modal state the moment the radial's ModalScope
        // closes (that close clears CursorManager.BlockCursorRaycast, which The Grid still wants).
        private System.Action _adHocClosed;

        public bool IsRadialOpen => _menu.IsOpen;
        public IRadialFeature ActiveFeature => _active;

        /// <summary>The live controller (there is exactly one, built by the plugin). Null before
        /// construction and after <see cref="ShutdownImmediate"/>, so a stale reference can never
        /// call into a dead assembly after an F6 reload.</summary>
        public static RadialController Active { get; private set; }

        /// <summary>True while ANY radial is on screen. Surfaces that share the cursor with the
        /// radial (The Grid) gate their own pointer handling on this: the radial polls raw Input,
        /// so a click meant for a wedge must not also land on a UGUI widget underneath.</summary>
        public static bool AnyRadialOpen
        {
            get { var c = Active; return c != null && c._menu.IsOpen; }
        }

        public RadialController()
        {
            Active = this;
        }

        public void Register(IRadialFeature feature) => _features.Add(feature);

        /// <summary>Static shorthand for <see cref="OpenAdHoc"/> against the live controller.
        /// Returns false (and does nothing) when there is no controller or it refused.
        /// <para>No close callback: the one caller (The Grid) used to restore its cursor-raycast
        /// block here, but that was a one-shot fired BEFORE the radial's deferred ModalScope close
        /// cleared the flag again. The Grid now re-asserts the block every Tick while latched, which
        /// is order-independent and idempotent, so the callback is gone.</para></summary>
        public static bool OpenAdHocRadial(string title, System.Func<List<RadialEntry>> provider)
        {
            var c = Active;
            return c != null && c.OpenAdHoc(title, provider, null);
        }

        /// <summary>
        /// Open a STICKY radial that no feature key owns — the entry point for another surface
        /// (The Grid's right-click-a-cell → that item's manage radial). Behaves exactly like a
        /// tapped feature radial from here on: the same single <see cref="RadialMenu"/> and the same
        /// single <see cref="ModalScope"/>, closed by Escape / RMB / selecting a wedge, which routes
        /// through <see cref="CloseAll"/> and fires <paramref name="onClosed"/>.
        /// <para>Refuses when the radial half is switched OFF — <c>Update()</c> is not pumped then,
        /// so an opened radial would strand its modal (and the cursor) forever — when a radial is
        /// already up, or when the provider yields nothing.</para>
        /// </summary>
        public bool OpenAdHoc(string title, System.Func<List<RadialEntry>> provider, System.Action onClosed)
        {
            if (provider == null) return false;
            if (UIAConfig.RadialEnabled == null || !UIAConfig.RadialEnabled.Value) return false;
            if (_menu.IsOpen || _modal.IsOpen) return false;
            if (!Guards.CanKeepRadialOpen()) return false;

            List<RadialEntry> probe;
            try { probe = provider(); }
            catch (System.Exception e) { UIALog.Warn("Ad-hoc radial build failed: " + e.Message); return false; }
            if (probe == null || probe.Count == 0)
            {
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return false;
            }

            _pending = null;
            _active = null;          // no owning feature: no owner key, no re-press-to-close
            _adHocClosed = onClosed;
            _modal.Open();
            _menu.Open(title, provider, sticky: true);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
            return true;
        }

        /// <summary>Drop the ad-hoc close callback, optionally running it. Invoked on every close
        /// path so the opening surface always gets its modal state back; NOT invoked on hot-reload
        /// teardown (there is nothing left to restore, and the callback may be a dead delegate).</summary>
        private void FinishAdHoc(bool invoke)
        {
            var cb = _adHocClosed;
            _adHocClosed = null;
            if (!invoke || cb == null) return;
            try { cb(); }
            catch (System.Exception e) { UIALog.Warn("Ad-hoc radial close callback failed: " + e.Message); }
        }

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
                    // R3 double-tap repeat: a fast second tap of the SAME feature re-runs the
                    // last committed action for that feature without opening the ring. A miss
                    // (toggle off, wrong feature, no cached action) falls through to the normal
                    // pending resolve, so a lone tap keeps today's open-sticky behavior.
                    if (UIAConfig.RadialDoubleTapRepeat != null && UIAConfig.RadialDoubleTapRepeat.Value
                        && _lastTapFeature == feature && _lastTapAt >= 0f
                        && UIAConfig.RadialDoubleTapMs != null
                        && (Time.unscaledTime - _lastTapAt) * 1000f < UIAConfig.RadialDoubleTapMs.Value)
                    {
                        _lastTapFeature = null;
                        _lastTapAt = -1f;
                        if (RadialMenu.RepeatLast(feature.Title)) return;
                    }

                    _pending = feature;
                    _pendingSince = Time.unscaledTime;
                    _pendingMouse = Overlay.DrawUtil.MousePos(); // R2 flick origin
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

                // R2 flick-commit: a fast release with the mouse flicked past the selection
                // radius executes the wedge in that direction WITHOUT drawing the ring.
                if (UIAConfig.RadialFlickCommit != null && UIAConfig.RadialFlickCommit.Value
                    && UIAConfig.RadialFlickMs != null && heldMs < UIAConfig.RadialFlickMs.Value
                    && TryFlickCommit(feature))
                    return;

                // R3: remember this tap so a fast second tap of the same feature can repeat the
                // last commit (see the key-down scan). Recorded for every tap gesture.
                _lastTapFeature = feature;
                _lastTapAt = Time.unscaledTime;

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

            // #4/#9: re-dispatch the vanilla game keys the modal's Typing state otherwise swallows
            // (smart stow, inventory select, helmet light) so they keep working with a wheel open.
            UpdatePassthroughKeys();

            // E swaps the active hand while any radial is open (never while the search
            // panel is typing — E is a letter there). Q flips pages on crowded rings, or on
            // the toolbelt ring opens the belt-picker (swap the worn tool-belt).
            if (!_menu.IsSearchOpen)
            {
                UpdateHandSwitch();
                var pageKey = UIAConfig.RadialPageKey.Value;
                if (pageKey != KeyCode.None && pageKey != (_active != null ? _active.Key : KeyCode.None)
                    && Input.GetKeyDown(pageKey))
                {
                    if (_active is ToolbeltRadialFeature) OpenBeltPicker();
                    else _menu.NextPage();
                }
            }

            // #3: Tab-swap (toolbelt<->backpack) / 1–6 equipment jump / Ctrl+number bag-open / number-key
            // bag bind. Returns true when it changed the radial.
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

        // ---------- R2 flick-commit ----------

        /// <summary>R2: resolve the wedge in the flick direction (mouse delta from the press
        /// origin) and run it directly — no ring is ever drawn. The travel gate mirrors the
        /// menu's own hub-claim radius so a stray micro-flick can't fire. Returns true when it
        /// committed an enabled action.</summary>
        private bool TryFlickCommit(IRadialFeature feature)
        {
            if (feature == null || !feature.CanOpen()) return false;

            Vector2 delta = Overlay.DrawUtil.MousePos() - _pendingMouse;
            float outerR = UIAConfig.RadialOuterRadius.Value;
            // Same geometry the ring uses: inner radius clamped, then the hub-claim line.
            float innerR = Mathf.Clamp(UIAConfig.RadialInnerRadius.Value, 104f, Mathf.Max(104f, outerR - 30f));
            float selectR = innerR - 6f;
            if (delta.magnitude < selectR) return false;

            List<RadialEntry> root;
            try { root = feature.BuildRoot(); }
            catch (System.Exception e) { UIALog.Warn("Flick-commit build failed: " + e.Message); return false; }
            if (root == null || root.Count == 0) return false;

            int idx = RadialMenu.SectorFromMouse(delta, root.Count);
            if (idx < 0 || idx >= root.Count) return false;

            var entry = root[idx];
            if (entry == null || entry.OnSelect == null || !entry.Enabled) return false;

            try { entry.OnSelect(); }
            catch (System.Exception e) { UIALog.Error("Flick-commit action '" + entry.Label + "' failed: " + e); return false; }

            if (UIAConfig.RadialWedgeSounds != null && UIAConfig.RadialWedgeSounds.Value)
                UIAudioManager.Play(UIAudioManager.ClickMediumHash);
            else
                UIAudioManager.Play(UIAudioManager.ClickLightHash);
            return true;
        }

        // ---------- #4 / #9: game-key pass-through while a radial is open ----------

        /// <summary>
        /// Opening a radial puts <c>KeyManager.InputState = Typing</c> (via <see cref="ModalScope"/>),
        /// which disarms EVERY Game-bound vanilla binding — so smart stow (G), inventory select and the
        /// helmet-light toggle silently die while a wheel is up. Re-dispatch them straight to the
        /// vanilla methods their bindings call, so they keep working. We poll the BOUND keycodes
        /// (<c>KeyMap.*</c>), not hard-coded letters, so a rebind is honoured, and skip any key that is
        /// the open radial's OWN key (there it means select/close) and everything while the search box
        /// is capturing text. Jetpack (J) is handled separately — it is a MovementController control-mode
        /// toggle polled in physics, gated by <c>!Cursor.visible</c>, not a callable action (see
        /// <c>Patch_MovementController_MovementHandler</c>).
        /// </summary>
        private void UpdatePassthroughKeys()
        {
            if (_menu.IsSearchOpen) return;
            KeyCode ownKey = _active != null ? _active.Key : KeyCode.None;

            if (KeyMap.SmartStow != KeyCode.None && KeyMap.SmartStow != ownKey
                && Input.GetKeyDown(KeyMap.SmartStow))
            {
                try
                {
                    var iwm = Assets.Scripts.UI.InventoryWindowManager.Instance;
                    if (iwm != null) iwm.SmartStow();   // re-enters SmartStowPlus's own prefix
                }
                catch (System.Exception e) { UIALog.Warn("Radial smart-stow pass-through failed: " + e.Message); }
            }

            if (KeyMap.InventorySelect != KeyCode.None && KeyMap.InventorySelect != ownKey
                && Input.GetKeyDown(KeyMap.InventorySelect))
            {
                try
                {
                    var iwm = Assets.Scripts.UI.InventoryWindowManager.Instance;
                    if (iwm != null) iwm.InventorySelect();
                }
                catch (System.Exception e) { UIALog.Warn("Radial inventory-select pass-through failed: " + e.Message); }
            }

            if (KeyMap.ToggleLight != KeyCode.None && KeyMap.ToggleLight != ownKey
                && Input.GetKeyDown(KeyMap.ToggleLight))
            {
                try
                {
                    var human = Guards.LocalHuman;
                    if (human != null) human.ToggleHelmetLight();
                }
                catch (System.Exception e) { UIALog.Warn("Radial helmet-light pass-through failed: " + e.Message); }
            }
        }

        // ---------- E.4 belt-swap (Q on the toolbelt ring) ----------

        /// <summary>E.4: swap the ring's root to the belt-picker (choose which tool-belt to wear).
        /// Keeps <c>_active</c> as the toolbelt feature so re-press/close still track the toolbelt
        /// key. Selecting a belt runs its own OnSelect (the swap, built in ToolbeltRadialFeature).</summary>
        private void OpenBeltPicker()
        {
            List<RadialEntry> picker;
            try { picker = ToolbeltRadialFeature.BuildBeltPicker(); }
            catch (System.Exception e)
            {
                UIALog.Warn("Belt-picker build failed: " + e.Message);
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            if (picker == null || picker.Count == 0)
            {
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            if (!_modal.IsOpen) _modal.Open();
            _menu.Open("Swap Belt", ToolbeltRadialFeature.BuildBeltPicker, sticky: true);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        // ---------- #3: radial switching + bag hotkeys ----------

        /// <summary>While a radial is open: a Tab press swaps toolbelt&lt;-&gt;backpack, plain 1–6 jumps to
        /// that equipment radial, hovering a bindable bag + a number BINDS it to Ctrl+&lt;number&gt;, and
        /// Ctrl+&lt;number&gt; opens the bound bag. Returns true when it changed the open radial or bound a
        /// bag, so the caller stops for this frame. Search mode owns its own keys.</summary>
        private bool UpdateRadialShortcuts()
        {
            if (_menu.IsSearchOpen) return false;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // #3: Tab swaps the OPEN wheel between the toolbelt and the backpack (this replaced the
            // old Ctrl tap). Tab normally OPENS the backpack wheel, but while a wheel is already up
            // the modal's Typing state suppresses vanilla Tab (scoreboard) and the bag-open handler,
            // so a Tab press here can only mean "swap". SwapToolbeltBackpack no-ops (returns false)
            // on any other wheel, so Tab falls through harmlessly there. On the backpack wheel this
            // runs BEFORE the re-press-to-close in UpdateOpen, so Tab swaps to the toolbelt instead
            // of closing it (RMB / Esc still close, per FlorpyDorp).
            if (Input.GetKeyDown(KeyCode.Tab) && SwapToolbeltBackpack()) return true;

            for (int slot = 0; slot < BagHotkeyStore.SlotCount; slot++)
            {
                if (!Input.GetKeyDown(BagHotkeyStore.KeyForSlot(slot))) continue;

                if (ctrl)
                {
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

                // Plain 1–6: switch straight to that equipment's radial — but pressing the
                // digit of the ALREADY-OPEN radial closes it (toggle). This is the number-key
                // twin of the re-press-to-close on line ~270, which only fires for non-number
                // owner keys because these digits are consumed here first.
                int digit = BagHotkeyStore.DisplayDigit(slot);
                if (digit >= 1 && digit <= 6)
                {
                    var eq = EquipFeatureForDigit(digit);
                    if (eq != null)
                    {
                        if (_active == eq) { CloseAll(); return true; }
                        SwitchToFeature(eq); return true;
                    }
                }
                return true; // a number press is always consumed while a radial is open
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
        /// switch used by the Tab swap and 1–6.</summary>
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
            // E.5: never strand the new radial-feel statics/timers past a close.
            _lastTapFeature = null;
            _lastTapAt = -1f;
            // Hand the opening surface (The Grid) its modal state back — the ModalScope close above
            // cleared BlockCursorRaycast, which that surface's own modal still wants held.
            FinishAdHoc(true);
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
            BeltBindingStore.Reset(); // same for belt tool-home bindings; the per-save file is untouched
            RadialMenu.ResetRepeatCache(); // R3: drop the cached last-commit delegate (it pins live game objects)
            _active = null;
            _pending = null;
            // E.5 hot-reload: reset the new radial-feel statics/timers (a double-F6 must strand nothing).
            _lastTapFeature = null;
            _lastTapAt = -1f;
            _pendingMouse = Vector2.zero;
            // Drop the ad-hoc callback WITHOUT running it (it points into the surface we are tearing
            // down) and release the static handle, so nothing survives into the reloaded assembly.
            FinishAdHoc(false);
            if (Active == this) Active = null;
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

