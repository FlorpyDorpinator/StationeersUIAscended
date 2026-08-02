using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// One pooled square slot cell in The Grid — a glass <see cref="PanelGraphic"/> carrying the
    /// occupant's thumbnail and a small corner state readout, or a dimmed <see cref="Slot.SlotTypeIcon"/>
    /// when the slot is empty. Cells are rented/returned by <c>BagGridView</c>; the view owns the pool
    /// and the layout, the cell owns its own visuals and interaction.
    ///
    /// Reads only NETWORKED state (occupant identity + <see cref="StateText"/>, which is client-safe by
    /// construction). The one Stage-1 mutation is left-click → equip the occupant to the active hand
    /// through <see cref="ItemActions.EquipToActiveHand"/> (the MP-safe funnel, occupant re-verified at
    /// execute time via <see cref="ScannedSlot.Pin"/>). EventSystem-driven, like the Control Center
    /// widgets — The Grid's canvas carries a GraphicRaycaster and the game keeps an EventSystem alive.
    ///
    /// Steady-state is allocation-free: <see cref="RefreshIfDirty"/> updates the icon/text ONLY when the
    /// (occupant reference, state string) key changes; palette colours are re-applied every call but the
    /// graphic setters are dirty-guarded, so an unchanged cell costs no mesh rebuild. The occupant
    /// REFERENCE is diffed every frame (structural changes repaint at once), but the allocating StateText
    /// STRING is only recomputed on a ~100ms poll cadence, so a static grid does no per-frame string work.
    /// </summary>
    public sealed class BagGridCell : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private PanelGraphic _bg;
        private Image _icon;
        private TextMeshProUGUI _state;
        private RectTransform _rect;

        private Slot _slot;
        private bool _hover;
        private bool _activeHand;

        // ---- drag-drop (Stage 2 core) ----
        // All three drag events land on the SOURCE cell (UGUI routes the whole gesture to the
        // object where the drag began), so the ghost + the current drop target live as instance
        // fields on the source. The one static (_activeDrag) is only a back-reference to the cell
        // currently dragging and is cleared by CancelDrag/CancelActiveDrag on every exit path.
        private bool _dragging;            // this cell is the live drag SOURCE
        private GameObject _ghostGo;       // floating thumbnail following the cursor (child of the root canvas)
        private RectTransform _ghostRt;
        private BagGridCell _dropTarget;   // the cell currently highlighted under the cursor (may be null)
        private bool _dragDim;             // source cell dimmed while its item is in-flight
        private bool _dropHighlight;       // this cell is the hovered drop target
        private bool _selected;            // the keyboard/scroll-select cursor (#4) is on this cell
        private GridTab _dropTabTarget;    // profile mode only: the manila tab hovered as a PIN-RULE drop

        // A drag begun on an EMPTY cell is not an item drag at all — it is the user trying to
        // scroll the list. The gesture is forwarded to the parent hierarchy (the ScrollRect) and
        // this cell then acts purely as a relay: OnDrag/OnEndDrag forward too and run NO item
        // logic. Instance-scoped (UGUI routes the whole gesture to the object it began on).
        private bool _forwardingDrag;

        // The source pinned AT BEGIN. Pinning at DROP made ItemActions.DragTo's identity guard a
        // tautology (Expected was read from the same slot microseconds before the check), and a
        // pooled cell re-Bound mid-drag would have moved whatever landed in the recycled slot.
        // _dragSlot records the slot the gesture started on so a re-Bind invalidates the drop.
        private ScannedSlot _dragSource;
        private Slot _dragSlot;

        // The cell currently dragging, so the panel can cancel a gesture that Unity will never
        // deliver OnEndDrag for (hiding the window deactivates the cell mid-drag). Nulled by
        // CancelDrag/CancelActiveDrag, so teardown leaves no reference to a destroyed cell.
        private static BagGridCell _activeDrag;

        /// <summary>True while a cell drag (an item torn out of a grid or pinned cell) is in flight.
        /// The grid/pinned panels OR this into their GraphicRaycaster gate so the raycaster stays ON
        /// for the whole gesture — <c>EventSystem.RaycastAll</c> must still find a cell under the
        /// cursor to resolve the drop even if the mouse-control key is released mid-drag.</summary>
        public static bool IsDragActive { get { return _activeDrag != null; } }

        // MP pending: after a send-only client dispatch there is no local prediction, so the
        // source + target dim until the panel's signature-diff rebuild reconciles (occupant
        // actually changes) or a short backstop elapses. Never optimistically mutate local state.
        private bool _pending;
        private float _pendingUntil;
        private DynamicThing _pendingBaseline;
        private const float PendingTimeout = 2.0f;
        private float _iconBaseAlpha = 1f;   // 1 for an occupant, 0.28 for the empty-slot placeholder

        // Reused across drag frames (a user gesture, not steady state) so the target raycast
        // allocates no per-frame List. Cleared before each use.
        private static readonly List<RaycastResult> RayHits = new List<RaycastResult>(16);

        // Dirty key: occupant REFERENCE identity + the last state string. Slot.OnOccupantChange
        // covers insert/remove/swap; this catches in-place stack/charge deltas that fire no event.
        private object _lastOccupant;   // boxed reference only for identity compare (ReferenceEquals)
        private string _lastState;
        private bool _bound;

        // StateText.For/Strip allocate; polling the STATE STRING every frame is wasteful for a
        // grid of stateful cells. The occupant REFERENCE is diffed every frame (structural
        // changes repaint immediately); the string is only recomputed on this cadence.
        private float _nextStatePoll;
        private const float StatePollInterval = 0.1f;

        // The cell's laid-out square side. The corner sweep rides the INHERITED theme (the theme
        // caps the radius against the box's short side), so the shape may only be issued by
        // GridTheme.ApplyBox — SetSize therefore records the size and restyles, exactly as
        // GridRegionView does with _boxW/_boxH. No radius or border literal lives on this path.
        private float _size = 46f;

        /// <summary>The cell's RectTransform (the view positions/sizes the cell through this).</summary>
        public RectTransform Rect => _rect;

        /// <summary>The glass background graphic. NOTE the cell no longer needs an outside party to
        /// wire its glass: <see cref="GridTheme.ApplyBox(PanelGraphic,float,float,GridTheme.CellState)"/>
        /// resolves the whole stack (fill, line, corner, sheen/edge-light, the shared HudFxMaterials
        /// pass) for the Cell surface. Kept as a read-only handle for diagnostics/inspection.</summary>
        public PanelGraphic Background => _bg;

        /// <summary>The slot this cell currently shows (null while pooled/idle).</summary>
        public Slot Slot => _slot;

        /// <summary>Build a cell GameObject under <paramref name="parent"/>. The PanelGraphic is the
        /// raycast target (icon + text are inert), so pointer events land on this component.</summary>
        public static BagGridCell Create(Transform parent)
        {
            var go = new GameObject("BagGridCell", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var bg = go.AddComponent<PanelGraphic>();
            bg.raycastTarget = true;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(rect, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            var state = HudText.Make(rect, "State", 10f, TextAlignmentOptions.BottomRight, warp: false);
            state.rectTransform.anchorMin = state.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            state.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var cell = go.AddComponent<BagGridCell>();
            cell._rect = rect;
            cell._bg = bg;
            cell._icon = icon;
            cell._state = state;
            cell.Idle();
            return cell;
        }

        /// <summary>The F9-editable item-icon scale — the fraction of the cell's edge the thumbnail
        /// fills (<see cref="UIAConfig.GridIconScale"/>, clamped 0.4..1.0, shipped default 0.70).
        /// Falls back to 0.70 before the config binds (hot reload before <c>UIAConfig.Init</c>).
        /// Also read by <see cref="GridRegionView"/>'s resize guard, so a live icon-scale edit
        /// re-applies even when the cell SIZE did not move.</summary>
        internal static float IconScale()
        {
            try { if (UIAConfig.GridIconScale != null) return Mathf.Clamp(UIAConfig.GridIconScale.Value, 0.4f, 1f); }
            catch { }
            return 0.70f;
        }

        /// <summary>Resize the cell to a square of <paramref name="size"/> px and re-lay the icon and
        /// corner text. Called by the view on layout; the panel shape tracks the rect exactly.</summary>
        public void SetSize(float size)
        {
            size = Mathf.Max(8f, size);
            _size = size;
            _rect.sizeDelta = new Vector2(size, size);
            ApplyBoxStyle();   // re-resolves the corner against the new size (theme-owned)

            float iconSize = size * IconScale();
            _icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            _icon.rectTransform.anchoredPosition = new Vector2(0f, size * 0.06f);

            _state.rectTransform.sizeDelta = new Vector2(size - 4f, size * 0.30f);
            _state.rectTransform.anchoredPosition = new Vector2(-2f, -size * 0.5f + size * 0.16f);
            _state.fontSize = HudText.Size(Mathf.Clamp(size * 0.16f, 8f, 13f));
        }

        /// <summary>Bind the cell to a slot and take it out of the pool. Forces an immediate content
        /// refresh (the dirty key is reset so the first RefreshIfDirty repaints unconditionally).</summary>
        public void Bind(Slot slot)
        {
            // A pooled cell may be re-bound while a gesture is still live on it; drop the drag
            // outright (its pinned source no longer describes what this cell shows).
            CancelDrag();
            _slot = slot;
            _bound = false;              // force the next refresh to repaint
            _lastOccupant = null;
            _lastState = null;
            _nextStatePoll = 0f;         // first refresh polls the state string unconditionally
            // A structural rebuild reconciles any pending move (the occupant just changed), so a
            // freshly (re)bound cell starts clean — never carry a stale pending/drop/selection cue
            // across (GridSelection re-applies the cursor after the rebuild, by Slot identity).
            _pending = false;
            _dropHighlight = false;
            _selected = false;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            HudText.Sync(_state);
            RefreshIfDirty();
        }

        /// <summary>Mark this cell as the local player's ACTIVE hand (accent border). The view sets
        /// this; it feeds the palette repaint. Cheap and dirty-guarded downstream.</summary>
        public void SetActiveHand(bool active)
        {
            _activeHand = active;
        }

        /// <summary>Update the icon + corner text only when the (occupant, state) key changed; always
        /// re-apply theme colours (guarded, so a live F9 palette edit shows without a mesh rebuild on
        /// an unchanged cell). Cheap enough to call every Grid Tick for every visible cell.</summary>
        public void RefreshIfDirty()
        {
            if (_slot == null) return;
            Repaint();

            DynamicThing occ = null;
            try { occ = _slot.Get(); } catch { }

            // Resolve a pending move: the server echo has landed (the occupant reference changed
            // from what it was at dispatch) or the backstop timer elapsed. Clearing here un-dims
            // even when no full rebuild fired (e.g. a same-container reorder).
            if (_pending && (!ReferenceEquals(occ, _pendingBaseline) || Time.unscaledTime >= _pendingUntil))
                _pending = false;

            // Cheap every frame: the occupant reference tells us about insert/remove/swap. The
            // STATE STRING is expensive (allocates), so recompute it only when the occupant
            // changed structurally OR the poll clock is due — an unchanged, un-due cell reuses
            // the cached string and hits the early-return below without allocating.
            bool structural = !_bound || !ReferenceEquals(occ, _lastOccupant);
            string state = _lastState;
            if (structural || Time.unscaledTime >= _nextStatePoll)
            {
                _nextStatePoll = Time.unscaledTime + StatePollInterval;
                state = null;
                if (occ != null)
                {
                    try { state = StateText.Strip(StateText.For(occ)); } catch { }
                }
            }

            if (_bound && ReferenceEquals(occ, _lastOccupant) && state == _lastState) return;
            _bound = true;
            _lastOccupant = occ;
            _lastState = state;

            Sprite sprite = null;
            bool empty = occ == null;
            if (!empty) { try { sprite = occ.GetThumbnail(); } catch { } }
            if (sprite == null) { try { sprite = _slot.SlotTypeIcon; } catch { } }

            _icon.sprite = sprite;
            _icon.enabled = sprite != null;
            // Empty slot: dim the type-icon so it reads as a placeholder, not an occupant. The
            // in-flight/pending dim is layered on top in Repaint (which runs every frame).
            _iconBaseAlpha = empty ? 0.28f : 1f;
            ApplyIconTint();

            HudText.Set(_state, empty ? "" : (state ?? ""));
        }

        /// <summary>Apply the cell's box style + text colour. Runs every refresh; every setter
        /// downstream is equality-guarded, so an unchanged cell costs no mesh rebuild.</summary>
        private void Repaint()
        {
            ApplyBoxStyle();

            ApplyIconTint();

            // Through the THEME, not the raw palette: the F9 Grid popup's 'Text' override must
            // recolour the per-cell state text along with the window titles (it resolves to the
            // palette entry while Following, so the follow-mode look is unchanged).
            Color stateCol = GridTheme.Text;
            if (_dragDim || _pending) stateCol.a *= 0.35f;
            else if (GridProfileMode.Active) stateCol.a *= 0.5f;   // profile mode: content recedes, bag chrome leads
            _state.color = stateCol;
        }

        /// <summary>Style the cell's box through the ONE shared styling path,
        /// <see cref="GridTheme.ApplyBox(PanelGraphic,float,float,GridTheme.CellState)"/>: fill,
        /// border colour, border WIDTH, the corner sweep, sheen/edge-light AND the shared glass
        /// stack, all inherited from the theme. Cells are the most numerous surface on screen, so
        /// the four interaction states are expressed as SCALES on the inherited line rather than as
        /// absolute widths — a thin global border now renders thin cell outlines in every state.
        ///
        /// <para>The glass/frost the cell used to request itself (a direct
        /// <c>HudGlobalGlass.Apply</c> with includeGlow/wantFrost false, wantTierB true) is supplied
        /// by ApplyBox for the Cell surface, so it is NOT applied here as well — one call, no
        /// double-apply.</para>
        ///
        /// <para>Precedence: a live drag's drop-target cue outranks everything (it is the transient
        /// "release here" signal during a mouse gesture); the keyboard/scroll-select cursor then wins
        /// over the persistent active-hand accent and hover, so the wheel cursor is always the most
        /// prominent box on screen.</para></summary>
        private void ApplyBoxStyle()
        {
            GridTheme.CellState state;
            if (_dropHighlight) state = GridTheme.CellState.Pending;  // hovered drop target
            else if (_selected) state = GridTheme.CellState.Selected; // keyboard/scroll cursor
            else if (_activeHand) state = GridTheme.CellState.ActiveHand;
            else if (_hover) state = GridTheme.CellState.Hover;
            else state = GridTheme.CellState.Idle;

            GridTheme.ApplyBox(_bg, _size, _size, state);
        }

        /// <summary>Toggle this cell's keyboard/scroll-select cursor highlight (set by
        /// <see cref="GridSelection"/>). Pure visuals — a scale on the inherited line (the heaviest of
        /// the cell states) plus the accent colour, repainted through the shared theme path.</summary>
        public void SetSelected(bool on)
        {
            if (_selected == on) return;
            _selected = on;
            if (_slot != null) Repaint();
        }

        /// <summary>Tint the icon: its base alpha (occupant vs empty placeholder) knocked down while
        /// the item is in-flight (drag source) or a send-only move is still pending its server echo.
        /// Called from RefreshIfDirty (on content change) and Repaint (every frame), so the dim tracks
        /// the drag/pending state without needing a fresh occupant diff.</summary>
        private void ApplyIconTint()
        {
            float a = _iconBaseAlpha;
            if (_dragDim || _pending) a *= 0.35f;
            // Profile mode (O4a): item cells dim slightly so the bag chrome (chips, tabs) reads as
            // the editing surface. A static bool read folded into the SAME multiply the drag dim
            // uses — no new object, no per-frame cost, clears everywhere the moment the mode flips
            // (Repaint runs each Tick and the colour setters are equality-guarded).
            else if (GridProfileMode.Active) a *= 0.5f;
            // #9: optional global item-icon tint (hands / 1-6 / inventory grids share one toggle),
            // preserving the base/drag/pending/profile alpha computed above.
            _icon.color = HudConfig.TintIcon(new Color(1f, 1f, 1f, a));
        }

        // ---- interaction (left-click → equip to active hand; right-click → the item's radial) ----

        public void OnPointerClick(PointerEventData e)
        {
            if (e == null) return;
            // A radial polls raw Input, not the EventSystem, so while one is up a click aimed at a
            // wedge must never ALSO act on the cell underneath. (Belt and braces: the radial's
            // ModalScope raises the game's own full-screen UGUI click blocker, but that call is
            // fail-soft by design, so the gate lives here too.)
            if (RadialController.AnyRadialOpen) return;
            // UGUI still dispatches OnPointerClick after a drag when press and release land on the
            // same object, and it fires BEFORE OnEndDrag — without this a left-DRAG would also run
            // click-to-hand, mutating state the user never asked to change.
            if (e.dragging) return;

            if (e.button == PointerEventData.InputButton.Right)
            {
                OpenItemRadial();
                return;
            }
            if (e.button != PointerEventData.InputButton.Left) return;
            // A device/tool with internal slots opens its VANILLA internals window on click — matching
            // vanilla, where a plain click opens that window rather than jumping the item to the hand.
            // Plain items still go to the hand; a device is taken by DRAGGING it (or F in scroll-nav).
            DynamicThing occ = null;
            try { occ = _slot != null ? _slot.Get() : null; } catch { }
            // Open the device window; only if that actually happened do we consume the click. If the
            // window can't open (no vanilla Display built yet), fall through so the click still takes the
            // item to hand rather than dying.
            if (occ != null && Core.DeviceWindow.CanOpen(occ) && Core.DeviceWindow.Open(occ)) return;
            EquipOccupantToActiveHand();
        }

        /// <summary>Equip this cell's occupant into the active hand — the shared body of the left-click
        /// and the keyboard <c>F</c> path (<see cref="ActivateFromKeyboard"/>), so both drive the ONE
        /// gated funnel (<see cref="ItemActions.EquipToActiveHand"/>, occupant re-verified at execute
        /// time via <see cref="ScannedSlot.Pin"/>). No-op on an empty cell or the active hand itself
        /// (clicking the item into the hand it already occupies). Returns whether a move was issued.</summary>
        private bool EquipOccupantToActiveHand()
        {
            if (_slot == null) return false;

            DynamicThing occ = null;
            try { occ = _slot.Get(); } catch { }
            if (occ == null) return false; // empty cell: no-op

            // The item already in the active hand: equipping it to the hand it's already in is a
            // clean no-op (no message on the wire, no redundant funnel call).
            if (_slot == Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot) return false;

            // Snapshot the occupant NOW so the funnel re-verifies identity at execute time
            // (a teammate may have taken it between build and action). One action = one message.
            var source = new ScannedSlot { Slot = _slot };
            source.Pin();
            bool ok = ItemActions.EquipToActiveHand(source);

            // Reflect the change immediately; the panel's signature diff also rebuilds shortly.
            RefreshIfDirty();
            return ok;
        }

        /// <summary>Keyboard <c>F</c> (scroll-select #4) on this cell: the SAME action the left-click
        /// runs — equip the occupant to the active hand. Guarded against firing under an open radial
        /// (which owns the keyboard). BIDIRECTIONAL: an OCCUPIED cell takes/swaps its item (same as the
        /// click); an EMPTY cell with a full hand receives the held item (vanilla hand-to-slot) — the one
        /// place F diverges from the drag-only click. Returns whether a move was issued.</summary>
        public bool ActivateFromKeyboard()
        {
            if (RadialController.AnyRadialOpen) return false;
            if (_slot == null) return false;

            DynamicThing occ = null;
            try { occ = _slot.Get(); } catch { }
            // OCCUPIED cell: take it / swap with the held item — the shared click funnel (unchanged).
            if (occ != null) return EquipOccupantToActiveHand();

            // EMPTY cell: F with a FULL hand PLACES the held item here (vanilla InventorySelect's
            // hand-to-slot). This is where F deliberately diverges from the mouse path — with the cursor
            // captured there is no drag, so F is how the keyboard both TAKES and PLACES. Only attempt it
            // when the hand actually holds something, so an empty-hand F over an empty cell is a silent
            // no-op rather than the fail sound. StowActiveHandTo re-verifies emptiness + Slot.AllowMove
            // at execute time and routes through OnServer.MoveToSlot (the MP-safe funnel).
            DynamicThing held = null;
            try { held = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot?.Get(); } catch { }
            if (held == null) return false;

            bool ok = ItemActions.StowActiveHandTo(_slot);
            RefreshIfDirty();
            return ok;
        }

        /// <summary>
        /// Right-click → this occupant's MANAGE radial, the same ring the equipment keys and the bag
        /// wheel build (<see cref="ItemMenuBuilder.BuildManageEntries"/>), opened through the one
        /// shared <see cref="RadialController"/> so there is still exactly one radial and one radial
        /// modal in flight. Every action on that ring is an ItemMenuBuilder entry, so all mutation
        /// keeps running through <see cref="ItemActions"/> (one gated message, identity re-verified).
        ///
        /// <para>Modal interplay (the trap): The Grid holds its OWN cursor-unlock modal and the radial
        /// opens a second one. MouseModeController keeps a LIST of modals and unlocks while ANY of them
        /// wants it, so neither side can strand the cursor — but the radial's ModalScope CLEARS
        /// <c>CursorManager.BlockCursorRaycast</c> when it closes, which The Grid still wants held
        /// while it is latched. A one-shot restore callback here did NOT fix that: it ran at
        /// <c>CloseAll</c>, while the ModalScope close is DEFERRED (it waits for clear frames) and
        /// cleared the flag afterwards. <see cref="TheGridPanel"/> now re-asserts the block every Tick
        /// while latched — order-independent and idempotent — so no callback is passed at all.</para>
        /// </summary>
        private void OpenItemRadial()
        {
            if (_slot == null) return;

            DynamicThing occ = null;
            try { occ = _slot.Get(); } catch { }
            if (occ == null) return;   // empty cell: no ring

            // Captured once per gesture (not per frame): the ring's provider re-reads the slot on
            // every refresh, so an item taken by a teammate collapses the ring instead of acting
            // on a stale reference.
            Slot slot = _slot;
            bool includeTake = !ReferenceEquals(slot, Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot);

            if (!RadialController.OpenAdHocRadial(occ.DisplayName, () => BuildManageFor(slot, includeTake)))
                return;

            // A drag begun on this cell can never survive the radial taking the cursor.
            CancelDrag();
            _hover = false;
            ClearOccupantTooltip();
            Repaint();
        }

        /// <summary>Ring provider: re-resolve the slot's CURRENT occupant every rebuild and hand back
        /// its manage entries (empty when the slot emptied out, which closes/blanks the ring rather
        /// than acting on a vanished item).</summary>
        private static List<RadialEntry> BuildManageFor(Slot slot, bool includeTake)
        {
            DynamicThing occ = null;
            try { occ = slot != null ? slot.Get() : null; } catch { }
            if (occ == null) return new List<RadialEntry>();
            return ItemMenuBuilder.BuildManageEntries(occ, slot, includeTake);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hover = true;
            if (_slot != null) Repaint();
            ShowOccupantTooltip();
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hover = false;
            if (_slot != null) Repaint();
            ClearOccupantTooltip();
        }

        /// <summary>Raise the vanilla item tooltip for this cell's occupant — the SAME
        /// <see cref="Assets.Scripts.UI.PanelToolTip"/> the vanilla inventory slots drive on hover
        /// (<c>SlotDisplayButton.ShowSlotTooltip</c>). Because <see cref="BagGridCell"/> is the shared
        /// cell for the main Universal Inventory grid, nested-bag regions AND pinned windows, wiring
        /// it here gives all three surfaces vanilla-faithful tooltips at once. Honors the game's own
        /// <c>ShowSlotToolTips</c> setting for parity. Fail-soft: the singleton is world-only and may
        /// not exist yet, and the occupant is re-read live (never a stale reference).</summary>
        private void ShowOccupantTooltip()
        {
            try
            {
                if (!Assets.Scripts.Serialization.Settings.CurrentData.ShowSlotToolTips) return;
                DynamicThing occ = _slot != null ? _slot.Get() : null;
                if (occ == null) return;
                // Route through VanillaTooltip so the tooltip's canvas is lifted ABOVE the mod's Grid /
                // pinned / Control Center canvases — otherwise it renders hidden behind the window.
                Core.VanillaTooltip.Show(occ);
            }
            catch { }
        }

        /// <summary>Hide the vanilla item tooltip. Null-safe and cheap; called on hover exit and on
        /// every teardown path that can bypass <see cref="OnPointerExit"/> (radial open, pool return,
        /// deactivate) so a tooltip never lingers over a cell no longer under the cursor.</summary>
        private static void ClearOccupantTooltip()
        {
            Core.VanillaTooltip.Clear();
        }

        // ---- drag-drop (Stage 2 core: cell → cell move/swap/merge via the ItemActions funnel) ----

        /// <summary>Start a drag from this cell — only when it holds an occupant and the panel is
        /// latched (interactive). Pins the source occupant so the drop re-verifies identity, spawns a
        /// floating thumbnail ghost on the top-most canvas, and dims the source (its item is in-flight).</summary>
        public void OnBeginDrag(PointerEventData e)
        {
            _forwardingDrag = false;
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            // Accept a drag out of a PINNED bag even when the MAIN window is closed. TheGridPanel
            // .IsInteractive is (_open && _interactive) — false whenever the Universal Inventory window
            // is hidden — but a pin stays live after B closes the main window and carries its OWN
            // interactive GraphicRaycaster, gated by PinnedInventoryWindow.Interactive. Reading only the
            // main window's flag rejected the pinned-bag drag-out even though its raycaster had already
            // delivered this event. Either surface being interactive is sufficient; the per-window
            // raycaster is what actually decided event delivery.
            if (_slot == null || !(TheGridPanel.IsInteractive || PinnedInventoryWindow.Interactive)) return;
            if (RadialController.AnyRadialOpen) return;   // the radial owns the cursor this gesture

            DynamicThing occ = null;
            try { occ = _slot.Get(); } catch { }
            if (occ == null)
            {
                // Empty cell: nothing to drag. A grid is mostly empty cells, so CONSUMING the
                // gesture here is what killed drag-to-scroll — hand it up the hierarchy instead
                // (the viewport's ScrollRect claims it) and relay the rest of the gesture.
                ForwardDrag(e, ExecuteEvents.beginDragHandler);
                _forwardingDrag = true;
                return;
            }

            // Pin the source HERE, at BEGIN — the whole point of the identity guard is that the
            // occupant is captured before the gesture, so DragTo's `item != source.Expected` check
            // can actually fire when a teammate takes the item mid-drag.
            _dragSlot = _slot;
            _dragSource = new ScannedSlot { Slot = _slot, Holder = _slot.Parent };
            _dragSource.Pin();

            _dragging = true;
            _dragDim = true;
            _dropTarget = null;
            _activeDrag = this;
            SpawnGhost(e);
            // Ghost routing hint (O4d): tell the hint pump what is being dragged and from where.
            // Gating (profile mode + config) lives in GridGhostHint.Tick; every drag exit path
            // below calls EndDrag, so the item reference is strictly gesture-scoped.
            GridGhostHint.BeginDrag(occ, _slot);
            Repaint();
        }

        /// <summary>Follow the cursor with the ghost and highlight whichever cell sits under the
        /// pointer (excluding this source cell). No mutation happens on drag — only visuals.</summary>
        public void OnDrag(PointerEventData e)
        {
            if (_forwardingDrag)
            {
                ForwardDrag(e, ExecuteEvents.dragHandler);
                return;                                   // relay only: no item-drag logic at all
            }
            if (!_dragging || e == null) return;
            if (_ghostRt != null) _ghostRt.position = new Vector3(e.position.x, e.position.y, 0f);

            BagGridCell target;
            GridTab tabTarget;
            FindDropUnder(e, out target, out tabTarget);
            if (target != _dropTarget)
            {
                if (_dropTarget != null) _dropTarget.SetDropHighlight(false);
                _dropTarget = target;
                if (_dropTarget != null) _dropTarget.SetDropHighlight(true);
            }
            // Profile mode only (FindDropUnder never reports a tab otherwise): the hovered manila
            // tab lights up as the drag-to-pin drop cue.
            if (tabTarget != _dropTabTarget)
            {
                if (_dropTabTarget != null) _dropTabTarget.SetDropHighlight(false);
                _dropTabTarget = tabTarget;
                if (_dropTabTarget != null) _dropTabTarget.SetDropHighlight(true);
            }
        }

        /// <summary>Resolve the drop: find the target cell under the cursor and route the move through
        /// <see cref="ItemActions.DragTo"/> (the ONE-message funnel — it re-verifies the pinned occupant
        /// and picks move/swap/merge/insert with the Slot gates AT EXECUTE TIME). On success both the
        /// source and target hold a "pending" dim until the signature-diff rebuild reconciles; an invalid
        /// or missing target snaps back (the cell never moved) and DragTo plays the fail sound itself.
        /// Always tears the ghost down, even on cancel/abort.</summary>
        public void OnEndDrag(PointerEventData e)
        {
            if (_forwardingDrag)
            {
                _forwardingDrag = false;
                ForwardDrag(e, ExecuteEvents.endDragHandler);
                return;                                   // relay only: nothing was ever picked up
            }
            if (!_dragging) { DestroyGhost(); return; }
            _dragging = false;
            _dragDim = false;
            GridGhostHint.EndDrag();   // the would-receive glow dies with the gesture
            if (ReferenceEquals(_activeDrag, this)) _activeDrag = null;

            BagGridCell target = null;
            GridTab dropTab = null;
            if (e != null) FindDropUnder(e, out target, out dropTab);
            if (_dropTarget != null) { _dropTarget.SetDropHighlight(false); _dropTarget = null; }
            if (_dropTabTarget != null) { _dropTabTarget.SetDropHighlight(false); _dropTabTarget = null; }
            DestroyGhost();

            // The source pinned at BEGIN. If the cell was recycled onto a different slot mid-gesture
            // (pooled re-Bind), the gesture no longer refers to the item the user grabbed — restore
            // the visuals and send NOTHING rather than move the wrong thing.
            var source = _dragSource;
            bool sourceValid = source != null && ReferenceEquals(_dragSlot, _slot);
            _dragSource = null;
            _dragSlot = null;

            if (sourceValid && target != null && target._slot != null && _slot != null)
            {
                bool ok = ItemActions.DragTo(source, target._slot); // one authoritative message
                if (ok)
                {
                    // Send-only on a client: keep both ends dimmed until the server echo changes the
                    // occupant (RefreshIfDirty clears it) or the backstop elapses. On host/SP the move
                    // applied locally, so the next signature diff rebuilds and Bind clears it at once.
                    MarkPending();
                    target.MarkPending();
                }
                else Repaint();   // invalid: DragTo already played ActionFailHash; just restore the source look
            }
            else if (sourceValid && target == null && dropTab != null)
            {
                // Drag-to-pin (design O4c): released on a bag's manila tab in profile mode. This
                // records an ITEM RULE in that bag's profile (auto-creating an assigned profile
                // named after the bag when it has none) — a config/profile write ONLY. The item
                // does NOT move: no ItemActions call, no message, nothing pending. FindDropUnder
                // reports a tab only while GridProfileMode.Active, so normal drags never land here.
                PinRuleToTab(source, dropTab);
                Repaint();
            }
            else if (sourceValid && target == null && dropTab == null)
            {
                // The release landed OUTSIDE this grid's own cells and tabs — over a HUD hand /
                // worn-equipment box, an open vanilla window slot, or genuinely open space. Resolve
                // that cross-surface destination and tear the item OUT of the grid; on success the
                // source cell dims until the server echo, on any abort surface it just restores.
                if (!TryDropOffGrid(source)) Repaint();
            }
            else Repaint();       // stale source or an unbound target cell: no message, restore the look
        }

        /// <summary>
        /// The release landed OUTSIDE The Grid's own cells and tabs. Resolve the cross-surface drop
        /// destination through the shared <see cref="Core.DropResolver"/> — the ONE canonical
        /// cursor→destination priority the inbound world drag (<see cref="Core.WorldDrag"/>) and the
        /// outbound HUD-box drag (<see cref="HudSlotDrag"/>) also use — and tear the item OUT of the
        /// grid accordingly:
        /// <list type="bullet">
        /// <item>a HUD hand / worn-equipment box, or an OPEN vanilla window slot → move it there via
        /// <see cref="ItemActions.DragTo"/> (the item-drag funnel: insert / merge / swap / move, each
        /// re-gated at execute time). This mirrors the outbound HUD-box release, which routes a vanilla
        /// slot, a grid cell OR a HUD box all through DragTo.</item>
        /// <item>genuinely open space BEYOND the window → drop it at the player's feet via
        /// <see cref="ItemActions.DropToWorld"/>, but ONLY when the release is truly off the panel
        /// (<see cref="TheGridPanel.HitTestWindow"/> = false). That single off-panel gate is what stops
        /// an irreversible fling to the floor when the release merely grazed the window's own padding —
        /// and it is a cursor-position test, so a transient HUD alpha flicker/dropout on the release
        /// frame can no longer swallow a deliberate ground-drop. (The old <c>HudSystem.ZonesAvailable</c>
        /// conjunct was removed here: box AVAILABILITY is now decoupled from ground-drops, matching
        /// <see cref="HudSlotDrag"/>'s own release.)</item>
        /// </list>
        /// The grid-cell rung of the resolver can only be the SOURCE cell here (a different cell would
        /// have been caught upstream by <see cref="FindDropUnder"/>), so it, other-UI chrome, and an
        /// ambiguous no-zones frame are all ABORT surfaces. Returns true only when exactly ONE
        /// authoritative message went out (the caller then leaves the source dimmed-pending); false on
        /// every abort, so the caller merely restores the source look. One user action = at most one
        /// message. The pinned <paramref name="source"/> is re-verified again inside DragTo/DropToWorld
        /// at execute time, on top of the caller's <c>_dragSlot</c>/<c>_dragSource</c> staleness gate.
        /// </summary>
        private bool TryDropOffGrid(ScannedSlot source)
        {
            Core.DropResolution r = Core.DropResolver.Resolve();

            // A HUD hand/equipment box or an OPEN vanilla window slot is a real move destination.
            if (r.Surface == Core.DropSurface.HudZone || r.Surface == Core.DropSurface.VanillaSlot)
            {
                if (r.HasSlot && ItemActions.DragTo(source, r.Slot)) { MarkPending(); return true; }
                return false;   // DragTo already played ActionFailHash on an invalid target
            }

            // Genuinely open space: drop at the player's feet — gated ONLY on the release being truly
            // OFF the window (HitTestWindow = false). That off-panel test is the real anti-false-drop
            // guard here: a HUD flicker does not move the cursor off the panel over the world, so a
            // deliberate ground-drop no longer needs the HUD to be offering zones this frame. Removing
            // the old ZonesAvailable() conjunct is the whole point of this path — box AVAILABILITY
            // (alpha/dropout) is decoupled from ground-drops. The move still funnels through
            // ItemActions.DropToWorld -> OnServer.MoveToSlotOrWorld, re-gated at execute time.
            if (r.Surface == Core.DropSurface.None
                && !TheGridPanel.HitTestWindow((UnityEngine.Vector2)Input.mousePosition))
            {
                if (ItemActions.DropToWorld(source)) { MarkPending(); return true; }
            }

            // GridCell (the source cell itself), OtherUi (window chrome / another panel), or a release
            // still over the window's own padding: abort with no message.
            return false;
        }

        /// <summary>Resolve a tab drop into a pinned item rule through
        /// <see cref="Features.ProfileCapture.PinItemRule"/>. Uses the occupant PINNED at drag
        /// begin (what the user actually grabbed); the bag comes from the tab's owning region.
        /// A bag dropped on its OWN tab is ignored (a "this bag goes inside itself" rule).</summary>
        private static void PinRuleToTab(ScannedSlot source, GridTab tab)
        {
            DynamicThing item = source != null ? source.Expected : null;
            if (item == null || tab == null) return;
            GridRegionView region = tab.OwnerRegion;
            DynamicThing bag = region != null ? region.BoundContainer : null;
            if (bag == null || ReferenceEquals(bag, item)) return;
            // Drag-to-pin CREATES and auto-assigns a profile, so it is an assignment surface and
            // takes the same gate as the chip popup (redesign plan Q5): a tool with slots, a suit
            // or a packaging box must not acquire a profile by having something dropped on its tab.
            if (!Features.BagProfileGate.IsAssignableContainer(bag)) return;
            string finalName = null;
            try { finalName = ProfileCapture.PinItemRule(bag, item.PrefabName); }
            catch (System.Exception ex) { UIALog.Warn("Drag-to-pin failed: " + ex.Message); }
            if (!string.IsNullOrEmpty(finalName))
            {
                // Success feedback: on an ALREADY-profiled bag the chrome rebuild below is
                // byte-identical (chip/badge unchanged, item correctly doesn't move), so the
                // gesture used to succeed invisibly. The tab flash is keyed by RefId and
                // therefore survives the rebuild the bump triggers.
                GridTab.FlashPin(tab.RefId);
                GridProfileMode.BumpVersion();   // chips/badges refresh
            }
        }

        /// <summary>Hand a drag event up the hierarchy from this cell's PARENT, so the first ancestor
        /// that handles it (the list's ScrollRect) takes the gesture. Starting at the parent — not at
        /// this GameObject — is what stops the event bouncing straight back into this component.
        /// Allocation-free (the ExecuteEvents handler delegates are static singletons) and null-safe.</summary>
        private void ForwardDrag<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn)
            where T : IEventSystemHandler
        {
            if (e == null) return;
            Transform parent = transform.parent;
            if (parent == null) return;
            ExecuteEvents.ExecuteHierarchy(parent.gameObject, e, fn);
        }

        /// <summary>Flag this cell dimmed-pending against its CURRENT occupant; the dim lifts when that
        /// occupant reference changes (server echo) or the backstop timer elapses (see RefreshIfDirty).</summary>
        public void MarkPending()
        {
            _pending = true;
            _pendingUntil = Time.unscaledTime + PendingTimeout;
            try { _pendingBaseline = _slot != null ? _slot.Get() : null; } catch { _pendingBaseline = null; }
            Repaint();
        }

        /// <summary>Toggle this cell's drop-target highlight (set by the active drag source).</summary>
        public void SetDropHighlight(bool on)
        {
            if (_dropHighlight == on) return;
            _dropHighlight = on;
            if (_slot != null) Repaint();
        }

        /// <summary>Ray-pick the top-most drop target under the pointer: a <see cref="BagGridCell"/>
        /// (skipping THIS source cell), or — in profile mode ONLY — a <see cref="GridTab"/> (the
        /// drag-to-pin drop). One raycast resolves both: a tab and a cell never overlap spatially,
        /// so whichever surfaces first in the (top-most-first) hit list is the target. Outside
        /// profile mode the tab probe never runs, so item-drag behaviour is byte-identical to the
        /// old cell-only pick. Reuses the shared hit list (drag frames aren't steady state, but
        /// still no per-frame allocation); the ghost is not a raycast target, so it never occludes
        /// the target.</summary>
        private void FindDropUnder(PointerEventData e, out BagGridCell cell, out GridTab tab)
        {
            cell = null;
            tab = null;
            var es = EventSystem.current;
            if (es == null) return;
            bool wantTab = GridProfileMode.Active;
            RayHits.Clear();
            es.RaycastAll(e, RayHits);
            for (int i = 0; i < RayHits.Count; i++)
            {
                var go = RayHits[i].gameObject;
                if (go == null) continue;
                var c = go.GetComponentInParent<BagGridCell>();
                if (c != null && c != this) { cell = c; break; }
                if (wantTab)
                {
                    // Only a hit on the tab polygon itself resolves (icon/name/chevron are
                    // non-raycast; no cell has a GridTab ancestor), so this cannot misfire.
                    var t = go.GetComponentInParent<GridTab>();
                    if (t != null) { tab = t; break; }
                }
            }
            // Emptied on the way OUT too: a RaycastResult holds a GameObject reference, and leaving
            // the last gesture's hits in a static stranded destroyed objects across an F6 reload.
            RayHits.Clear();
        }

        /// <summary>Create the floating drag ghost — an <see cref="Image"/> of the item thumbnail on the
        /// shared TOP-MOST drag layer (<see cref="DragGhostLayer"/>, 5250), sized to the current cell icon
        /// (F10 <see cref="UIAConfig.GridCellSize"/>), non-raycast so it never blocks target detection.
        /// Hosting it there (not on this window's own canvas) keeps a main-grid cell ghost ABOVE the
        /// pinned windows it might be dropped into. Destroyed on drag end (and on Idle).</summary>
        private void SpawnGhost(PointerEventData e)
        {
            DestroyGhost();
            Transform host = DragGhostLayer.EnsureHost();
            if (host == null) return;

            _ghostGo = new GameObject("BagGridDragGhost", typeof(RectTransform));
            _ghostGo.transform.SetParent(host, false);
            _ghostRt = (RectTransform)_ghostGo.transform;
            _ghostRt.anchorMin = _ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRt.pivot = new Vector2(0.5f, 0.5f);

            float size = 46f;
            try { size = UIAConfig.GridCellSize.Value; } catch { }
            _ghostRt.sizeDelta = new Vector2(size * 0.9f, size * 0.9f);

            var img = _ghostGo.AddComponent<Image>();
            img.sprite = _icon != null ? _icon.sprite : null;
            img.preserveAspect = true;
            img.raycastTarget = false;
            // #9: keep a dragged item's ghost tinted too, so the green wash doesn't drop mid-drag.
            img.color = HudConfig.TintIcon(new Color(1f, 1f, 1f, 0.85f));

            _ghostRt.SetAsLastSibling();   // draw above the panel within the overlay canvas
            _ghostRt.position = new Vector3(e.position.x, e.position.y, 0f);
        }

        /// <summary>Destroy the drag ghost if present (idempotent; safe from Idle / teardown).</summary>
        private void DestroyGhost()
        {
            if (_ghostGo != null) Object.Destroy(_ghostGo);
            _ghostGo = null;
            _ghostRt = null;
        }

        /// <summary>Abort any live drag on this cell WITHOUT sending a message: clear the drag flags
        /// and the pinned source, drop the target highlight, destroy the ghost and repaint. Idempotent,
        /// and safe to call from a path where Unity will never deliver OnEndDrag (a deactivated
        /// component gets no drag events at all, which is exactly how a hidden panel used to strand a
        /// ghost and leave the source cell dimmed forever).</summary>
        private void CancelDrag()
        {
            // Only the drag OWNER clears the ghost hint: CancelDrag also runs on pooled sibling
            // cells being re-Bound mid-gesture (Bind → CancelDrag), and those must not kill the
            // LIVE drag's glow. _dragging is true only on the source cell.
            if (_dragging) GridGhostHint.EndDrag();
            if (ReferenceEquals(_activeDrag, this)) _activeDrag = null;
            // A forwarded (scroll) gesture dies with the cell too — the ScrollRect gets no further
            // relay, so the flag must never survive into the next gesture on a recycled cell.
            _forwardingDrag = false;
            if (_dropTarget != null) { _dropTarget.SetDropHighlight(false); _dropTarget = null; }
            if (_dropTabTarget != null) { _dropTabTarget.SetDropHighlight(false); _dropTabTarget = null; }
            DestroyGhost();
            if (!_dragging && !_dragDim && _dragSource == null && _dragSlot == null) return;
            _dragging = false;
            _dragDim = false;
            _dragSource = null;
            _dragSlot = null;
            if (_slot != null) Repaint();
        }

        /// <summary>Cancel whatever drag is currently in flight, from anywhere (TheGridPanel calls this
        /// from Hide/Shutdown). Clears the static, so teardown holds no reference to a destroyed cell.</summary>
        public static void CancelActiveDrag()
        {
            var cell = _activeDrag;
            _activeDrag = null;
            RayHits.Clear();
            if (cell != null) cell.CancelDrag();
        }

        /// <summary>Deactivating a cell (pool return, panel hide, canvas teardown) stops Unity from
        /// ever delivering OnEndDrag, so the gesture has to die here.</summary>
        private void OnDisable()
        {
            if (_hover) ClearOccupantTooltip();   // deactivation can bypass OnPointerExit
            CancelDrag();
        }

        /// <summary>Return the cell to the pool: unbind, clear the dirty key, hide it. No event
        /// subscriptions or statics are held, so a pooled cell strands nothing across an F6 reload
        /// (its canvas is destroyed by TheGridPanel.Shutdown).</summary>
        public void Idle()
        {
            if (_hover) ClearOccupantTooltip();   // pooled while hovered → don't strand the tooltip
            _slot = null;
            _hover = false;
            _activeHand = false;
            _bound = false;
            _lastOccupant = null;
            _lastState = null;
            _nextStatePoll = 0f;
            // If a structural rebuild recycles this cell mid-drag, tear the ghost down and drop all
            // drag/pending state so nothing is stranded (a deactivated cell stops firing drag events).
            CancelDrag();
            _dropHighlight = false;
            _selected = false;
            _pending = false;
            _pendingBaseline = null;
            _iconBaseAlpha = 1f;
            if (_icon != null) { _icon.sprite = null; _icon.enabled = false; }
            if (_state != null) _state.text = "";
            // Drop any shared glass material so a pooled cell doesn't linger in HudFxMaterials'
            // assignment registry (no-op when it never carried one).
            if (_bg != null) HudFxMaterials.Unassign(_bg);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
