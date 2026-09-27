using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Menu.Tutorial;
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
        /// (the toolbelt/Hub ring). Takes precedence over OpenOnTap; OnTap/OnHold never run.</summary>
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
        /// <summary>The one shared menu. <c>internal</c> (not private) only so the uiatest harness
        /// (Testing/UiaTestHarness.cs) can read the live ring and stage parked chips on it.</summary>
        internal readonly RadialMenu _menu = new RadialMenu();
        private readonly ModalScope _modal = new ModalScope("UIAscended_Radial");

        private IRadialFeature _pending;   // key down, waiting to resolve tap vs hold
        private float _pendingSince;
        private IRadialFeature _active;    // radial open
        // Tutorial hook (Build Contract s3/s4): the active radial's kind, set explicitly at each open
        // site rather than re-derived from _active's runtime type — the same BagRadialFeature instance
        // backs both the Tab root and a Ctrl+number bound-bag dive, which only the OPEN SITE can tell
        // apart. Read through the static ActiveWheelKind property below.
        private TWheelKind _activeKind = TWheelKind.Other;
        private KeyCode _releaseKey;       // key to wait out during a deferred modal release

        // #3: Ctrl still opens a bound bag (Ctrl+number). The toolbelt<->backpack SWAP moved off
        // Ctrl onto Tab (see UpdateRadialShortcuts / SwapToolbeltBackpack), so no tap-timing state
        // is needed any more — a Tab press while a wheel is open is unambiguously "swap".

        // NOTE (the post-0.9.2.5 play-test round): R2 flick-commit is GONE — config, key-down origin capture and the
        // TryFlickCommit resolver. It was a second, racier route into "commit a wedge" that
        // intermittently ate MMB, and hold-open -> point -> release-selects already IS that
        // gesture (FlorpyDorp). Nothing replaced it; the pending resolve below is the one path.

        // R3 double-tap repeat: the last feature TAPPED and when, to detect a fast second tap
        // (a per-feature tap-timing pattern).
        private IRadialFeature _lastTapFeature;
        private float _lastTapAt = -1f;

        // Ad-hoc radial (opened by a surface that is not a feature key — The Grid's right-click).
        // Held so the opener can restore its OWN modal state the moment the radial's ModalScope
        // closes (that close clears CursorManager.BlockCursorRaycast, which The Grid still wants).
        private System.Action _adHocClosed;

        // D-064: true while the Q belt-picker (OpenBeltPicker) is the displayed sticky radial.
        // OpenBeltPicker replaces the menu's root via _menu.Open(...) rather than pushing a
        // child level (RadialMenu exposes no public "push a level" API), so the menu's own
        // internal back-stack has nothing under the picker to pop to — its RMB handling would
        // just close everything. This flag lets RMB instead reopen _active's own root (the
        // belt ring Q was pressed from), which _active still names because OpenBeltPicker never
        // reassigns it. Cleared by every path that navigates away from the picker or closes the
        // radial (SwitchToFeature, OpenBagRadial, CloseAll, ShutdownImmediate).
        // B15b: its menu-side mirror, RadialMenu.RmbReturnsFromRoot (what the hint strip reads), is
        // reset by every _menu.Open/_menu.Close — i.e. by each of those same paths — and re-asserted
        // every frame while the picker is up (UpdateOpen), so neither can outlive the other.
        private bool _inBeltPicker;

        public bool IsRadialOpen => _menu.IsOpen;
        public IRadialFeature ActiveFeature => _active;

        /// <summary>Tutorial hook (Build Contract s4): the active radial's kind, or <c>Other</c> when
        /// none is open. Search mode overrides whatever feature is behind it (the Hub's SEARCH wedge
        /// flips the SAME menu into search, without changing <see cref="_active"/>).</summary>
        internal static TWheelKind ActiveWheelKind
        {
            get
            {
                var c = Active;
                if (c == null || !c._menu.IsOpen) return TWheelKind.Other;
                if (c._menu.IsSearchOpen) return TWheelKind.Search;
                return c._activeKind;
            }
        }

        /// <summary>Classify a feature instance into its tutorial wheel kind. The toolbelt ring is
        /// checked first (both the MMB feature and the 6-key equipment feature show it, D-007) so the
        /// 6 key never falls through to the generic Gear bucket.</summary>
        private static TWheelKind ClassifyFeatureKind(IRadialFeature f)
        {
            if (f == null) return TWheelKind.Other;
            if (IsToolbeltRing(f)) return TWheelKind.Belt;
            if (f is EquipmentKeyRadialFeature) return TWheelKind.Gear;
            if (f is ToolRadialFeature) return TWheelKind.HeldTool;
            if (f is BagRadialFeature) return TWheelKind.Bag;
            return TWheelKind.Other;
        }

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
            _activeKind = TWheelKind.Other;
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
                // The Universal Inventory's scroll-nav owns the device-window key (R) on the frame its
                // highlight is on a device: yield it so R opens that device's internals window instead of
                // this radial. Same-key features only; everything else opens normally.
                if (feature.Key == global::StationeersUIMod.UI.Grid.GridSelection.DeviceWindowKey
                    && global::StationeersUIMod.UI.Grid.GridSelection.OwnsDeviceWindowKey) continue;
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

                // R3: remember this tap so a fast second tap of the same feature can repeat the
                // last commit (see the key-down scan). Recorded for every tap gesture.
                _lastTapFeature = feature;
                _lastTapAt = Time.unscaledTime;

                if (feature.OpensOnBoth)
                {
                    // The Hub/toolbelt ring: tap opens the same radial LATCHED (click a wedge to select).
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
            _activeKind = ClassifyFeatureKind(feature);
            Core.CursorDiag.NoteRadial("OPEN " + feature.Title + " sticky=" + sticky);
            _modal.Open();
            _menu.Open(feature.Title, feature.BuildRoot, sticky);
            TutorialSignals.Raise(TSignal.WheelOpened, TutorialSignals.Pack((int)_activeKind, sticky ? 1 : 0));
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        private void UpdateOpen()
        {
            // Bail out whenever the world stops being interactable or vanilla UI takes over
            // (pause, console, keyboard windows, Stationpedia, creative menu, unconscious).
            var cankeepWhy = Guards.CanKeepRadialOpenWhy();
            if (cankeepWhy != null)
            {
                CloseAll("cankeep:" + cankeepWhy);
                return;
            }

            // Scroll-wheel value adjust works in both hold and sticky modes.
            _menu.UpdateScroll();

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
                    // D-007: Q must belt-swap regardless of whether this ring was opened by MMB
                    // (ToolbeltRadialFeature) or the 6 equipment key (EquipmentKeyRadialFeature
                    // now showing the identical content) — see IsToolbeltRing.
                    if (IsToolbeltRing(_active)) OpenBeltPicker();
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
                // DORMANT since the post-0.9.2.5 play-test round (self-gating no-op): the pre-Hub hold mode, which gave a
                // held-open ring the tapped ring's full drag behaviour. The Hub's hold mode is
                // strictly transient (UpdateHoldB below dives on LMB), so this returns at once.
                // Left wired up so reviving it is a one-line guard flip — see RadialMenu.
                _menu.UpdateHoldInteractiveA();
                if (!_menu.IsOpen) { CloseAll("holdA-menu-closed"); return; }

                if (_active != null && !Input.GetKey(_active.Key))
                {
                    bool stayOpen = _menu.OnHoldReleased();
                    if (!stayOpen) CloseAll("hold-released");
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseAll("escape-hold");
                    return;
                }
                // The Hub: while the key is held, LMB dives into branches, RMB backs out,
                // and an executed action closes the menu right here.
                _menu.UpdateHoldB();
                if (!_menu.IsOpen)
                {
                    CloseAll("holdB-menu-closed");
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
                    // Re-pressing the radial key closes a sticky radial — except the MMB-keyed
                    // radial, where that press is the menu's own gesture (it handles MMB in
                    // UpdateSticky: CLOSE band dumps parked chips, anywhere else dismisses).
                    bool repressSelects = _active != null && _active.Key == KeyCode.Mouse2;
                    if (_active != null && !repressSelects && Input.GetKeyDown(_active.Key))
                    {
                        CloseAll("sticky-repress");
                        return;
                    }
                    // (The classic-schema "MMB dismisses a radial it doesn't own" clause went
                    // with the schema chooser in the post-0.9.2.5 play-test round — the menu owns MMB in every radial now.)

                    // D-064: RMB out of the Q belt-picker returns to the belt ring you Q'd away
                    // from (same belt, live state) instead of falling into the menu's own RMB
                    // handling below, which would just close everything (the picker is a fresh
                    // root, not a pushed child — see _inBeltPicker). _active still names the
                    // origin ring; SwitchToFeature reopens it sticky and clears the flag.
                    // A5: only when that ring can actually reopen — otherwise RMB falls through to
                    // the menu's own handling (a close) instead of being swallowed with a fail sound —
                    // and never while a drag is in flight: RMB mid-drag cancels just the drag (the
                    // menu's handling below), exactly as the hint strip promises.
                    // B15b: the same bool tells the menu (and through it the hint strip) that RMB at
                    // this root is a "back", so the strip can never disagree with what RMB does.
                    bool pickerReturns = _inBeltPicker && CanReturnTo(_active);
                    _menu.RmbReturnsFromRoot = pickerReturns;
                    if (pickerReturns && !_menu.IsDragging && Input.GetMouseButtonDown(1))
                    {
                        SwitchToFeature(_active);
                        // Tutorial hook: the D-064 RMB-back gesture specifically (SwitchToFeature is
                        // also reached by Tab-swap and the 1-6 jump, which are not "picker back").
                        TutorialSignals.Raise(TSignal.BeltPickerBack);
                        return;
                    }
                }
                _menu.UpdateSticky();
                if (wasOpen && !_menu.IsOpen)
                {
                    CloseAll("sticky-menu-closed");
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
                    // NOT InventoryWindowManager.SmartStow(): that wrapper early-returns whenever the
                    // cursor is free (InputMouse.IsMouseControl, decompile InventoryWindowManager.cs:523)
                    // — which is ALWAYS the case with a radial open — so G silently did nothing (the very
                    // toolbelt-radial-with-a-tool-in-hand case FlorpyDorp reported, 2026-07-24). Call the
                    // real handler directly on the active hand slot: it is the exact static method vanilla's
                    // PerformSmartSwapClick reaches, and the one SmartStowPlus's prefix patches, so the belt/
                    // profile/affinity routing (and the worn-box stow flash) run identically. It no-ops on an
                    // empty hand of its own accord (selectedSlot.Occupant == null, InventoryManager.cs:1817).
                    var slot = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot;
                    if (slot != null && slot.Get() != null)
                    {
                        Assets.Scripts.Inventory.InventoryManager.SmartStow(slot);
                        // The wheel's wedges are built from a snapshot; the stow just changed the belt
                        // and the hand, so re-read them now (and again post-roundtrip on an MP client) —
                        // otherwise the wedge stays stale until the radial is closed and reopened.
                        _menu.RefreshAfterExternalMutation();
                    }
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
        /// key. Selecting a belt runs its own OnSelect (the swap, built in ToolbeltRadialFeature).
        /// <c>internal</c> (not private) only so the uiatest harness (Testing/UiaTestHarness.cs) can
        /// drive the Q branch's action without a synthesized key press.</summary>
        internal void OpenBeltPicker()
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
            _inBeltPicker = true; // D-064: RMB from here returns to _active's ring instead of closing
            _activeKind = TWheelKind.BeltPicker;   // tutorial hook: Q replaced the root with the picker
            TutorialSignals.Raise(TSignal.WheelOpened, TutorialSignals.Pack((int)TWheelKind.BeltPicker, 1));
            TutorialSignals.Raise(TSignal.BeltPickerOpened);
            // B15b: publish it at once (UpdateOpen re-asserts it every frame from here on), so the
            // strip reads "RMB back" from the picker's very first frame.
            _menu.RmbReturnsFromRoot = CanReturnTo(_active);
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        /// <summary>A5 / B15b: can RMB out of the belt picker actually reopen <paramref name="f"/> —
        /// the same gates <see cref="SwitchToFeature"/> applies before it switches. Fail-soft: a
        /// throwing CanOpen counts as "no" (RMB then closes, like at any other root).</summary>
        private static bool CanReturnTo(IRadialFeature f)
        {
            if (f == null) return false;
            try { return f.Enabled && f.CanOpen(); }
            catch { return false; }
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
                        if (_active == eq) { CloseAll("eq-key-toggle"); return true; }
                        SwitchToFeature(eq);
                        TutorialSignals.Raise(TSignal.InWheelDigitJump);
                        return true;
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
            if (IsToolbeltRing(_active))
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

        /// <summary>True when <paramref name="f"/> is "the toolbelt ring" regardless of which
        /// key opened it — the MMB Belt Wheel (<see cref="ToolbeltRadialFeature"/>) or the 6
        /// equipment key (<see cref="EquipmentKeyRadialFeature"/> for "ToolBeltSlot"), which
        /// shows identical content as of D-007. Toolbelt-only gestures (Q belt-swap, Tab
        /// swap-to-backpack) key off this instead of a bare type check, so they behave the same
        /// no matter which key opened the ring. Internal so the key-hint strip keys its "Q change
        /// belt" hint off the SAME test (A12b / B3).</summary>
        internal static bool IsToolbeltRing(IRadialFeature f)
        {
            return f is ToolbeltRadialFeature
                || (f is EquipmentKeyRadialFeature eq && eq.ButtonName == "ToolBeltSlot");
        }

        /// <summary>Reopen the (sticky) radial on another feature's root, keeping the modal — the
        /// switch used by the Tab swap and 1–6 (and the D-064 RMB return). <c>internal</c> (not private)
        /// only so the uiatest harness (Testing/UiaTestHarness.cs) can open a ring sticky and drive the
        /// RMB-return branch's action without synthesized input.</summary>
        internal void SwitchToFeature(IRadialFeature f)
        {
            if (f == null || !f.Enabled || !f.CanOpen())
            {
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            bool wasOpen = _menu.IsOpen;   // tutorial hook: a switch REPLACES an open ring (D-004)
            _active = f;
            _activeKind = ClassifyFeatureKind(f);
            _inBeltPicker = false; // D-064: any explicit switch leaves the Q belt-picker behind
            if (!_modal.IsOpen) _modal.Open();
            _menu.Open(f.Title, f.BuildRoot, sticky: true);
            // Tutorial hook: raised AFTER Open() actually replaces the ring (D-004's ReleaseHeldItems
            // runs at the top of Open — the old ring's held chips are already resolved by this point).
            if (wasOpen) TutorialSignals.Raise(TSignal.WheelClosed, (int)TCloseRoute.Switch);
            TutorialSignals.Raise(TSignal.WheelOpened, TutorialSignals.Pack((int)_activeKind, 1));
            UIAudioManager.Play(UIAudioManager.ClickLightHash);
        }

        /// <summary>Open a specific bag's radial (Ctrl+number activation), opening the modal if needed.</summary>
        private void OpenBagRadial(DynamicThing bag)
        {
            if (bag == null) return;
            var b = bag;
            bool wasOpen = _menu.IsOpen;   // tutorial hook: a switch REPLACES an open ring (D-004)
            _active = BagFeature(); // owner key = Tab, for close/re-press (null-safe)
            _activeKind = TWheelKind.BoundBag;   // a direct Ctrl+number dive, not the Tab/Hub root
            _inBeltPicker = false; // D-064: Ctrl+number (reachable while the picker is up) leaves it behind
            if (!_modal.IsOpen) _modal.Open();
            _menu.Open(b.DisplayName, () => BagRadialFeature.BuildBagLevel(b), sticky: true);
            // Tutorial hook: raised AFTER Open() actually replaces the ring (D-004's ReleaseHeldItems
            // runs at the top of Open — the old ring's held chips are already resolved by this point).
            if (wasOpen) TutorialSignals.Raise(TSignal.WheelClosed, (int)TCloseRoute.Switch);
            TutorialSignals.Raise(TSignal.WheelOpened, TutorialSignals.Pack((int)TWheelKind.BoundBag, 1));
            TutorialSignals.Raise(TSignal.BoundBagOpened);
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

        public void CloseAll(string reason = null)
        {
            Core.CursorDiag.NoteRadial("CLOSE reason=" + (reason ?? "?") + " sticky=" + _menu.IsSticky);
            TutorialSignals.Raise(TSignal.WheelClosed, (int)MapCloseReason(reason));
            // A2: whatever the menu's close does, the modal below is ALWAYS released — a throw here
            // must never strand the cursor/input lock behind a half-closed radial.
            try { _menu.Close(); }
            catch (System.Exception e) { UIALog.Warn("Radial close failed: " + e.Message); }
            _releaseKey = _active?.Key ?? KeyCode.None;
            _modal.RequestDeferredClose();
            _modal.Pump(_releaseKey);
            _active = null;
            _activeKind = TWheelKind.Other;
            _pending = null;
            _inBeltPicker = false; // D-064: never survives a close
            _menu.RmbReturnsFromRoot = false; // B15b: its menu-side mirror (Close resets it too)
            // E.5: never strand the new radial-feel statics/timers past a close.
            _lastTapFeature = null;
            _lastTapAt = -1f;
            // Hand the opening surface (The Grid) its modal state back — the ModalScope close above
            // cleared BlockCursorRaycast, which that surface's own modal still wants held.
            FinishAdHoc(true);
        }

        /// <summary>Tutorial hook (Build Contract s3): best-effort <see cref="TCloseRoute"/> for
        /// CloseAll's free-text diagnostic reason. Several sticky-mode gestures (Escape, RMB-at-root,
        /// MMB, the close band, an executed action) all collapse into the SAME "sticky-menu-closed" /
        /// "holdB-menu-closed" reason here — RadialMenu's own internal Close() call sites decide the
        /// wheel's fate without reporting back which one fired, so those fall to Other rather than
        /// guess wrong. The unambiguous reasons (a guard tripping, the opener key, hold-mode Escape)
        /// map precisely.</summary>
        private static TCloseRoute MapCloseReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return TCloseRoute.Other;
            if (reason.StartsWith("cankeep:", System.StringComparison.Ordinal)) return TCloseRoute.Guard;
            if (reason == "sticky-repress" || reason == "hold-released" || reason == "eq-key-toggle")
                return TCloseRoute.OpenerKey;
            if (reason == "escape-hold") return TCloseRoute.Esc;
            return TCloseRoute.Other;
        }

        /// <summary>Immediate teardown (plugin OnDestroy / hot reload) — no deferred release.</summary>
        public void ShutdownImmediate()
        {
            RadialMovement.Active = false; // hot-reload safety: never strand the pass-through
            // A2: one failing close must not abort the rest of this teardown (a double-F6 has to
            // leave no canvas, modal or static behind).
            try { _menu.Close(); }
            catch (System.Exception e) { UIALog.Warn("Radial shutdown close failed: " + e.Message); }
            _modal.Close();
            UI.UnityRadialView.Shutdown(); // destroy the canvases, or every hot reload stacks another
            UI.ParkedItemsView.Shutdown();
            UI.SearchPanelView.Shutdown();
            Windows.RadialEditorMode.Shutdown();
            BagHotkeyStore.Reset(); // drop in-memory binds; the per-save file is untouched
            BeltBindingStore.Reset(); // same for belt tool-home bindings; the per-save file is untouched
            RadialMenu.ResetRepeatCache(); // R3: drop the cached last-commit delegate (it pins live game objects)
            _active = null;
            _activeKind = TWheelKind.Other;
            _pending = null;
            _inBeltPicker = false; // D-064: hot-reload safety — a double-F6 must strand nothing
            _menu.RmbReturnsFromRoot = false; // B15b: its menu-side mirror
            _lastTapFeature = null;
            _lastTapAt = -1f;
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

