# UI Ascended — Verified Interaction Model Reference

**Date:** 2026-08-01. **Verified against:** the working tree as of this date (post-0.9.2.5 +
style-parity phases + the 2026-08-01 drag/scroll fixes), and the game decompile
`Reference/StationeersGameVersions/Stationeers 8-1-26 V27758 Orbital Update Beta/`.
**Why this exists:** the first-run tutorial, the F10 Guide tab, and the Designer Handbook all
need to describe the mod *as it actually behaves*. This file is the single code-verified
ground truth, produced by a five-agent code survey (radials, Universal Inventory, keybinds,
pause API, recent changes reports). Every behavioral claim carries a `file:line` citation into
the mod source (`Assets/Scripts/StationeersUIMod/`) or the decompile.

**Maintenance rule:** if you change an interaction, update this file in the same change set —
it is the source the player-facing teaching material is written from. Claims here were
code-verified but NOT all play-tested (see §23).

---

## 1. The control grammar in one page

- **One control scheme exists** ("The Hub" model). The old Option A/B/D chooser is deleted;
  `UIAConfig.IsA`/`IsB` are hardwired `true` (`UIAConfig.cs:63-71`). Never document scheme options.
- **TAP a wheel key** (release before 180 ms): the wheel opens **sticky** — it stays up;
  LEFT-CLICK acts, RIGHT-CLICK backs out one level (or closes at root), Esc / MMB / the same
  key again closes it.
- **HOLD a wheel key** (past 180 ms): behavior is per-key —
  - Belt wheel (MMB) and Tool wheel (R): a **transient** wheel; sweep and RELEASE the key on a
    wedge to act; release on a branch just closes.
  - Equipment keys 1-6: vanilla **equip** (occupied slot -> take to hand; empty slot + fitting
    held item -> don it).
  - Bag wheel (Tab): vanilla **scoreboard**.
- **Child wheels ("satellites")**: push the cursor THROUGH a wedge past the outer rim and dwell
  ~0.18 s -> a child wheel pops out beside it. Pull back toward the center to dismiss; RMB backs
  out. Works in both sticky and hold mode. Only one satellite level; clicking a branch inside a
  child promotes it to the main ring.
- **Swapping a component** (battery / canister / cartridge): open the item's wheel -> swipe out
  on the component's wedge -> child shows **Take / Replace / its settings** -> click **Replace**
  -> a list of every reachable compatible item (with charge/kPa and consequence warnings) ->
  click one -> one atomic swap, wheel closes (hold Shift to keep it open).
- **The mouse is captured by default.** Windows are look-only until the cursor is freed: hold
  the vanilla mouse-control key (default Alt), or **double-tap it to latch** the cursor free
  (250 ms window, on by default); press it once more to re-lock.
- **The Universal Inventory (B)**: hold B = momentary peek; tap B = stays open; tap again =
  close. Cursor captured: scroll drives a highlight cursor, F takes/places. Cursor freed:
  scroll pans, left-click takes to hand, right-click opens the item's wheel, drag moves items.
- **Smart-Stow (G)**: vanilla's key, extended by SmartStow+; stows the ACTIVE hand item through
  belt-home -> stack-merge -> socket -> profile -> affinity -> bag-default -> memory; works with
  a wheel open; the receiving HUD box flashes the stowed item.

---

## 2. Radial core constants

| Constant | Value | Cite |
|---|---|---|
| `HoldThresholdMs` (tap/hold split, all wheels) | **180 ms** (range 60-600) | `UIAConfig.cs:293-295` |
| `DragHoldSec` / `DragMovePx` (press -> chip drag) | 0.25 s / 14 px | `Overlay/RadialMenu.cs:269-270` |
| `SlideOutDwellSec` (child radial opens) | 0.18 s | `RadialMenu.cs:248` |
| `SatGraceSec` (child can't steal a flick-release) | 0.25 s | `RadialMenu.cs:249` |
| `BranchDwellSec` / `BranchGraceSec` (hold mode) | 0.25 s / 0.30 s | `RadialMenu.cs:279-280` |
| Ring outer / inner (hub) radius | 240 / 120 px default | `UIAConfig.cs:553-556` |
| Wedges per page | 14 default (6-32); overflow pages with Q | `UIAConfig.cs:367-370` (its "MORE wedge" text is stale) |
| Max parked chips | 12 | `Overlay/ParkingState.cs:20` |
| Wedge 0 position | top-center | `UI/UnityRadialView.cs:471` |
| Tap/hold discrimination | purely "was the key still down at 180 ms" — hold opens immediately at the threshold | `Features/RadialController.cs:238-277` |

Feature registration: `StationeersUIMod.cs:186-194`. Only ONE wheel can be open at a time
(single `RadialMenu` + `ModalScope`, `RadialController.cs:36-37`). Opening a wheel sets
`KeyManager.InputState = Typing`, starving every vanilla Game-state binding
(`Core/ModalScope.cs:69`) — which is why G/F/E/L are re-dispatched by the mod inside wheels.

## 3. Opening each wheel

| Wheel | Key (default) | TAP | HOLD (>=180 ms) | Cite |
|---|---|---|---|---|
| **Toolbelt ("Belt wheel")** | `ToolbeltRadialKey` = **Mouse2 (MMB)** | sticky wheel | transient wheel | `UIAConfig.cs:336-337`; `Features/ToolbeltRadialFeature.cs:25-27` (`OpensOnBoth => true`) |
| **Bag/backpack ("Inventory")** | `BagRadialKey` = **Tab** | sticky wheel (`BagRadialTapOpens` default true) | vanilla scoreboard | `UIAConfig.cs:352-356`; `Features/BagRadialFeature.cs:26-30`, `:295-312` |
| **Tool/device (manage held item)** | `ToolRadialKey` = **R** | sticky wheel | transient wheel | `UIAConfig.cs:347-348`; `Features/ToolRadialFeature.cs:38-42` (`OpensOnBoth => true`) |
| **Equipment 1-6** | live vanilla `HelmetSlot..ToolBeltSlot` (1-6) | sticky manage wheel for that worn item | vanilla equip: occupied -> take to hand; empty + fitting held item -> don | `Features/EquipmentKeyRadialFeature.cs:20-24, 60-71, 98-114` |

Details:
- Belt wheel opens **even with no belt worn** (the Hub wedge is always there)
  (`ToolbeltRadialFeature.cs:29-34`). MMB has no vanilla action in this build (vanilla
  `PingHighlight` is a dead binding) (`ToolbeltRadialFeature.cs:191-196`).
- Tool wheel needs something in the **active hand**; otherwise fail sound
  (`ToolRadialFeature.cs:44`; `RadialController.cs:281-287`). Its title carries the held item's
  live stat (`ToolRadialFeature.cs:18-30`). Vanilla R is prefix-suppressed while owned
  (`Core/Patches.cs:182-201`, gate `ToolRadialTakeOverVanillaKey` default true).
- **Tapping an equipment digit on an EMPTY slot falls through to the equip action** — e.g. tap
  3 with a suit in hand dons the suit (`RadialController.cs:281-288`).
- Vanilla's own 1-6 handling is suppressed while the mod owns them (`Core/Patches.cs:194-198`),
  and entirely swallowed while the F9 editor is open (`Core/Patches.cs:447-455`).
- **Ctrl+1..0 from normal gameplay** opens a bag previously bound to that digit (per save)
  (`RadialController.cs:595-608`; `Features/BagHotkeyStore.cs`).
- **Right-click a Universal-Inventory cell** opens that item's manage wheel (ad-hoc, always
  sticky) (`UI/Grid/BagGridCell.cs:470`; `RadialController.cs:92-131`).
- **Double-tap repeat** (`RadialDoubleTapRepeat`, default **OFF**, 250 ms window): a second tap
  of the same key re-runs the last committed action without opening the ring
  (`RadialController.cs:218-226`; `UIAConfig.cs:323-328`).
- **STALE TEXT:** the R config description ("tap keeps the vanilla open-hand-slot-window
  action, hold opens the radial", `UIAConfig.cs:348`) and the GuideTab rows ("hold" wording,
  `UI/Menu/Tabs/GuideTab.cs:31-36`) contradict the shipped dispatch. Code wins: R/MMB open on
  tap AND hold.

## 4. The hub — circle vs wedge (two different things)

**(A) The center circle of every wheel** (radius `innerR - 6`) is NOT selectable
(`RadialMenu.cs:1508-1509`). It contains:
- the 5-line **readout**: title / verb / item name / detail / stat-or-warning
  (`UI/UnityRadialView.cs:1074-1124`);
- the **CLOSE band** — the bottom 60 degrees between 0.52 and 0.94 of the hub radius, labelled
  "Close" (`RadialMenu.cs:1434-1443`; drawn `UnityRadialView.cs:259-282`).

Sticky mode only (`RadialMenu.cs:908-931`): LMB on the CLOSE band = close + **dump parked chips
to the ground**; LMB-drag anywhere else inside the hub = **move the whole wheel** on screen
(offset resets on reopen, `RadialMenu.cs:349`). In hold mode the hub does almost nothing (only
the CLOSE band is honored; hub-drag is unreachable — the comment at `RadialMenu.cs:888-891`
claiming otherwise is stale).

**(B) "The Hub" WEDGE** — toolbelt ring only, wedge 0, top-center
(`ToolbeltRadialFeature.cs:44-52`). A pure branch: clicking it (or dwelling on it 0.25 s in
hold mode) dives into the **inventory root** — the exact level Tab opens: Grab-another /
Search / all six worn slots (`BagRadialFeature.cs:39-66`). There is no "hub key".

## 5. Selecting a wedge

**Sticky:** actions run on mouse-UP; press and release must land on the SAME entry; clicks only
register within 1.2 x outer radius (`RadialMenu.cs:736-767`). Branch -> dive; action ->
execute then **close**, unless chips are parked or **Shift** is held (Shift = keep the wheel
open, `RadialMenu.cs:308-309`, consumed `:788-797`).

**Hold:** commit on key-RELEASE over a wedge (`RadialMenu.cs:426-464`); releasing over a branch
just closes (`:438-444`); releasing within 0.30 s of entering a branch cancels (momentum
guard); LMB while held dives a branch / executes; dwell 0.25 s enters a branch hands-free; RMB
backs out (`:472-568`). Hold-release has NO distance clamp — a wide flick past the rim still
selects directionally.

**Keys that work while a wheel is open** (`RadialController.cs:322-608`; `RadialMenu.cs`):

| Key | Effect | Cite |
|---|---|---|
| Esc | close (chips cancel silently — items stay put) | `RadialMenu.cs:610-614`, `:356-360` |
| E (`UIA_HandSwap`) | swap active hand + wheel rebuilds | `RadialController.cs:414-436` |
| Q (`UIA_Page`) | next page of a crowded ring; on the toolbelt ring: opens the **belt-picker** (swap worn belt) instead | `RadialController.cs:322-329`; `ToolbeltRadialFeature.cs:131-175` |
| Tab | swap the open wheel toolbelt <-> backpack | `RadialController.cs:548`, `:613-626` |
| 1-6 | jump to that equipment wheel; the open wheel's own digit closes it; **TRAP: if the hovered wedge is a bindable bag, the digit BINDS it to Ctrl+digit instead** (bind check runs first) | `RadialController.cs:550-587` |
| Ctrl+1..0 | open the bound bag's wheel | `RadialController.cs:554-559` |
| G | Smart-Stow the active hand item (re-dispatched; wheel refreshes in place) | `RadialController.cs:456-480` |
| F / helmet-light key | re-dispatched vanilla actions | `RadialController.cs:482-502` |
| any free letter A-Z | while hovering a device SETTING wedge: bind that letter as a session hotkey (same letter again = unbind) | `RadialMenu.cs:1226-1254`; `Core/WedgeHotkeys.cs` |
| C (`UIA_FineAdjust`) | hold while scrolling a value wedge: fine +-1 instead of +-10 | `Features/DeviceControls.cs:216-223` |
| Shift | keep the wheel open after the action | `RadialMenu.cs:308-309` |
| Alt (vanilla MouseControl) | world-reach: ring goes passive; LMB grabs a free item / world slot item into a drag | `RadialMenu.cs:1496-1524`, `:809-851` |
| WASD/Space | still walk/jump (`MoveWhileRadialOpen` default true) | `RadialController.cs:147-172` |
| Scroll | value wedge: adjust value; elsewhere: grow/shrink visible wedge count (session-only) | `RadialMenu.cs:1203-1221`, `:162-173` |

The in-wheel digits are hard-coded Alpha1..Alpha0 (`BagHotkeyStore.cs:49-50`) — a player who
rebound the vanilla slot keys still uses the number row inside wheels.

## 6. Child radials (satellites)

**Opens:** cursor past `outerR + 14 px` over a wedge with slide-out content, dwell 0.18 s
(`RadialMenu.cs:1565-1586`). Both modes. Never while dragging a chip or holding Alt. Only from
the main ring — no child-of-a-child; clicking a branch inside a child **promotes** it to the
main ring (`RadialMenu.cs:775-780`, `:1314-1322`). Empty providers never open; the wedge rim
chevron advertises real content only (`RadialEntry.SlideOutHasContent`, `RadialMenu.cs:60-69`).

**Closes:** pull back inside 0.8 x outer radius; circling to a different wedge retargets; RMB
drops it (source wedge suppressed so it doesn't instantly re-open); any drag start kills it
(`RadialMenu.cs:1534-1543`, `:615-636`, `:503-518`).

**Catalogue — what slides out of what** (`ItemMenuBuilder.cs`, `BagRadialFeature.cs`,
`ToolbeltRadialFeature.cs`):

| Wedge | Child contents | Cite |
|---|---|---|
| Belt tool | its manage level: settings + one wedge per its slots | `ToolbeltRadialFeature.cs:91-92` |
| Occupied component slot (battery/canister/etc.) | **Take / Replace / its settings / Open** | `ItemMenuBuilder.cs:316-317`, `:391-447` |
| Empty typed slot (STOW wedge) | **Install** — every reachable compatible item | `ItemMenuBuilder.cs:355-381` |
| Nested bag | Stow-held / Take | `ItemMenuBuilder.cs:450-479` |
| Plain stack | **Split one / Split half / Split count** (scroll to set) | `ItemMenuBuilder.cs:630-668` |
| Sealed cardboard package | Unpack | `ItemMenuBuilder.cs:714-748` |

**Replace** exists only on TYPED slots (`slot.Type != None` or prefab-restricted,
`ItemMenuBuilder.cs:417-428`) — deliberately not on generic storage. Its child list =
**Eject current** (warn accent, "to free hand / ground") + **Insert from hand** (empty slots) +
every reachable compatible item from `InventoryScanner.FindCompatible` (scan depth default 3),
each with location, live charge/kPa, and a red consequence line ("X will be unpowered") from
`InventoryScanner.ConsequenceOfRemoving` (`ItemMenuBuilder.cs:552-604`). Selecting one runs
`ItemActions.SwapIntoSlot` = **one atomic `OnServer.SwapSlots`** (`Core/ItemActions.cs:99-124`).

**Item-to-item swap by drag** also exists (sticky only): drag one wedge's item (hold 0.25 s or
move 14 px) and release it on an occupied component wedge -> swap, gated by vanilla
`AllowSwap`/`InputMouse.IsValid` (`RadialMenu.cs:714-733`, `:1020-1042`;
`ItemMenuBuilder.cs:332-348`).

## 7. Worked walkthroughs (code-verified, exact player inputs)

### 7a. Swap a power tool's battery (shortest path)
1. Tool in the **active hand**. **Tap R** -> sticky wheel titled "&lt;Tool&gt; - &lt;charge&gt;".
   The **Battery wedge is on the main ring** (name, slot, %, blue socket edge)
   (`ToolRadialFeature.cs:46-52`; `ItemMenuBuilder.cs:269-296`).
2. Move onto the Battery wedge and **keep pushing OUTWARD past the rim; hold still ~0.2 s** ->
   child pops out (a chevron on the wedge advertises it).
3. Child = **Take / Replace / battery settings / Open**.
4. **Click "Replace"** -> the list becomes the main ring: **Eject current** first, then every
   reachable battery with its location + charge + consequence warnings.
5. **Click the battery you want** -> one atomic swap, install sound, **wheel closes** (hold
   Shift to keep it open).

*Hold-mode quick grab:* hold MMB -> flick to the tool's belt wedge -> keep going past the rim,
dwell -> move onto the Battery wedge -> **release MMB** = take the battery **to hand**
(`RadialMenu.cs:426-432`). Hold mode cannot reach "Replace" (that needs a click).

### 7b. Swap a welding torch's canister
Identical shape: tap R -> canister wedge (name + kPa) -> swipe out -> child = Take / **Replace**
/ the canister's own valve settings -> click Replace -> pick the full canister (kPa shown) ->
swapped. If the canister slot is EMPTY the wedge is a STOW wedge: click = stow the held item;
swipe out = **Install** list. **There is NO "refill" verb anywhere in the mod** — never
document one. (`Core/StateText.cs:37-41`; `Features/DeviceControls.cs:32-73`;
`ItemMenuBuilder.cs:355-381`.)

### 7c. Split a stack
Any wheel showing a stack -> swipe out on it -> child = Split one / Split half / **Split count**
(hover it and **scroll** to set the number, then click). Split-count is hidden on an MP client
(`ItemMenuBuilder.cs:630-668`, `:653`).

### 7d. Device settings
Devices with more than 2 controls get a **SETTINGS** branch wedge; 2 or fewer sit inline on the
ring (`ItemMenuBuilder.cs:94-101, 123-136`). Labels are vanilla's live `ContextualName`
("Stabilizer On"). Stepped pairs collapse into **scroll wedges** with a live value between two
triangles: jetpack Thrust, portable tank Valve kPa, suit Pressure kPa / Temperature C —
one notch = +-10 for suit values, +-1 holding C; bare click nudges one step up
(`DeviceControls.cs:125-223`).

*Ammo magazines: UNVERIFIED — no magazine-specific code exists; behavior depends on the
weapon's vanilla `Slot.Class`. Play-test before documenting.*

## 8. Drag, park, placement box, greyed wedges

**Drag sources** (sticky mode only): a wedge with a DragSource (0.25 s hold or 14 px move); a
parked chip; a HUD hand/equipment box; Alt+LMB on a free world item or a world object's slot
(within interact range) (`RadialMenu.cs:807-886`, `:714-733`).

**Release ladder** (`RadialMenu.cs:984-1068`): stale check -> stack-merge onto a compatible
stack -> wedge drop (insert/swap) -> HUD hand/equipment box -> world slot (charger/locker) ->
**park** (release on open screen > 30 px outside every ring, max 12 chips) -> else silent
cancel.

**Parking ("chips")** is client-side visual only — nothing leaves its slot until you drop a
chip (`Overlay/ParkingState.cs:9-17`). Parking **locks the wheel open** (actions stop
auto-closing). Chips survive branch dives and RMB-back, but **NOT wheel switches** (Tab / 1-6 /
Ctrl+digit reopen the menu and silently clear chips — items never moved)
(`RadialMenu.cs:345`, `:788-792`, `:1305-1312`).

**Ground dump** happens on exactly three deliberate exits: LMB on the hub CLOSE band, MMB over
the CLOSE band, RMB at the first main level (`RadialMenu.cs:1136-1147`, callers `:633-635`,
`:674`, `:918`). Esc / re-press / guards / hold-release all **cancel silently — items stay in
their slots**.

**The vanilla placement box** (green place/swap, yellow merge, blue insert, red refused) is
driven by `Core/WorldSlotCue.cs:94-108` while a radial chip drag hovers a world-object slot in
range. It is wired for **radial chip drags only** — not grid-cell or hand-box drags
(`WorldSlotCue.cs:37-56`; `RadialMenu.cs:1458`).

**Greyed wedges:** still **draggable**, never clickable (fail sound); the readout shows the
DisabledReason in the critical color (`RadialMenu.cs:699-706`, `:760-764`;
`UnityRadialView.cs:1103-1107`).

**Radial wedge -> Universal-Inventory cell drop is NOT implemented** (no grid rung in the chip
release ladder), and a grid-cell drag cannot start while a wheel is open.

## 9. MMB while a wheel is open

**MMB only ever closes.** It never selects (root-caused cursor-flicker fix, FlorpyDorp
2026-07-24). Over the CLOSE band it additionally dumps parked chips; elsewhere chips stay in
their slots. Ignored while a chip is on the cursor or LMB is pressed on a wedge. In hold mode
MMB-the-button does nothing extra (its release is the exit) (`RadialMenu.cs:661-678`;
`RadialController.cs:387-394`).

## 10. Other radial facts worth teaching

- **Wedge verbs that exist:** Take to hand / Open / Stow &lt;held&gt; / Install / Replace /
  Eject (to free hand or ground) / Insert from hand / Split (one, half, count) / Sort / Unpack
  / Settings / Search / Grab another / Worn-Wear-Swap belt / Equip / The Hub -> Enter.
  Device controls are enumerated live from vanilla `Interactable`s, not a fixed list.
  (`ItemMenuBuilder.cs`, `DeviceControls.cs:32-73`, `BagRadialFeature.cs`,
  `ToolbeltRadialFeature.cs`.)
- **Live stat text** under wedges (`RadialShowStateText` default true): vanilla
  `GetQuantityText()` — battery %, canister kPa, stack xN — with PowerTool/WeldingTorch/
  PortableAtmospherics fallbacks (`Core/StateText.cs:21-55`).
- **Hub readout:** 5 lines; with nothing hovered prints "LMB select | RMB back" (sticky) or
  "hover to dive | release to cancel" (hold) (`UnityRadialView.cs:1080-1124`).
- **Hint bar** (default on) below the wheel: "LMB select - RMB back - Alt reach - E swap hand -
  Q page"; swaps to grab/place text while Alt is held; keys read live. **Progressive fade**: a
  hint kind stops drawing after 25 wheel-opens that showed it; counters reset from F10
  (`UI/RadialHintBar.cs:138-141`, `:229-277`; `UIAConfig.cs:310-312`, `:332-334`).
- **Search**: the Search wedge flips the ring into a typing panel that owns all input; Esc/RMB
  exits; scroll pages results (`BagRadialFeature.cs:48-54`; `UI/SearchPanelView.cs:83-129`).
- **Belt ghost labels** (default on): reserved wedge per belt slot with a dim "the wrench goes
  here" label; labels are curved; toggle "Show bound-tool labels" (`UIAConfig.cs:340-345`,
  `:614-617`; Wave G report).
- **Bag grouping**: a bag with more than 10 items collapses into sorting-class GROUP wedges
  (`UIAConfig.cs:357-362`; `BagRadialFeature.cs:166-194`).
- **Auto-close guards**: the wheel closes when the game pauses, console / input window /
  Stationpedia / creative menu opens, or the player goes unresponsive
  (`Core/Guards.cs:120-132`; `RadialController.cs:300-305`).
- **Cursor**: unconditionally free while any wheel is open. The double-tap cursor latch is
  suppressed while a wheel is open (Alt means "reach" there) (`Core/CursorLatch.cs:57-59`).
  The old "hold MMB to look around" feature is REMOVED (0.9.1.1) — do not document it.
- **Flick-to-commit is REMOVED** (Wave G) — you must open the ring; no flick shortcut exists.
- **Wedge sounds** (hover tick + commit click) default on. Selecting refreshes wedges twice on
  MP (+0.6 s echo catch-up) (`RadialMenu.cs:460, 797, 1037`).
- **World-item drops onto occupied targets swap like vanilla** (ground canister dragged onto an
  occupied suit canister wedge swaps it out; stacks merge; bag occupants insert) — 2026-07-26
  report.

## 11. The Universal Inventory — opening and anatomy

- **Key**: `UIA_Grid` = **B** (`Core/UiaKeybinds.cs:81-82`; `UIAConfig.cs:436-439` — its
  "rebindable from the game's Controls screen" description is STALE; B rebinds only in F10 ->
  Controls). Master gate `GridEnabled` default true. Soft key-collision with vanilla
  `InstantStop` (rover) is documented harmless (`UIAConfig.cs:135-139`).
- **Gestures** (`GridHoldPeek` default true; tap threshold 0.25 s;
  `StationeersUIMod.cs:516-584`):
  - Cursor captured: **hold B = momentary peek** (hides on release); **tap B = latched open**;
    tap again = close.
  - Cursor already free: **tap B opens latched and stays** (the 2026-08-01b same-tap-close fix);
    next tap closes.
  - `GridHoldPeek` off: plain toggle on key-down.
- **Open gate** `Guards.CanOpenUniversalInventory()` (`Core/Guards.cs:56-70`): in-game, not
  paused, no console/input-window/Stationpedia/creative, InputState Game, no vanilla menu
  front, responsive character. Deliberately no `Cursor.visible` block.
- **THE first-run trap: the window is LOOK-ONLY until the mouse is freed.** Opening it never
  frees the cursor — head-look keeps working; the raycaster only turns on while the cursor is
  free (`TheGridPanel.cs:64-70`, `:333-350`). Free the mouse by holding vanilla MouseControl
  (default Alt) or **double-tap latch** (`CursorLatchOnDoubleTap` default true, 250 ms,
  `UIAConfig.cs:315-321`; `Core/CursorLatch.cs:27-94`).
- **What closes it**: B (main window only — pins stay); the title-bar X; vanilla close-all
  (backtick) closes main + pins but pin records survive; vanilla blocking menus HIDE it (state
  intact, restored after). **Esc does NOT close it** (Esc walks the profile-mode chain then
  falls through to vanilla) (`TheGridPanel.cs:269-296`, `:503-520`, `:562-573`, `:1428-1450`).
- **Anatomy** (`TheGridPanel.cs:1198-1415`): glass window titled "Universal Inventory";
  draggable title bar; profile-mode "luggage tag" button; X; scroll viewport + auto-hiding
  scrollbar (width 4 px, opacity 0.55, follows border color by default; draggable thumb);
  bottom-right resize grip. Geometry persists on drag-END; first open centers (defaults
  560x760). **No search box exists in this window** (search lives in radials).
- **Contents**: container regions only, walk order Helmet, Glasses, Suit, Back, Uniform,
  Toolbelt, L hand, R hand (`GridModel.cs:151-158`). **The root renders NO loose cells** — a
  held wrench or worn helmet is not a cell; only containers get regions
  (`GridRegionView.cs:410-412`). Container test `GridModel.IsStorageContainer`
  (`GridModel.cs:299-338`): excludes Tool, DisposableCardboardBox, EmergencySuppliesBox,
  ItemContainer, CerealBarBox (the reusable CardboardBox stays storage).
- **Manila tabs**: trapezoid folder tab with icon + name + collapse chevron. **Click =
  collapse/expand** (per-save; **bags start COLLAPSED on a new save** — absence of a record
  means collapsed, `Features/GridCollapseStore.cs:23-44`). **Drag the tab out = pin** (S13).
- **SORT button** per expanded bag region (>= 2 sortable slots): one authoritative
  sort message (`GridRegionView.cs:263-278`, `:852-881`).
- **Profile mode** (luggage-tag button): cells dim, each bag grows a profile chip + CAPTURE
  strip; the chip opens `GridProfilePopup` (assign a SmartStow profile — config write only,
  nothing moves); dragging an item onto a manila tab writes a **pin RULE** (item does not
  move); tab flash confirms (`TheGridPanel.cs:1264-1302`; `GridProfileMode.cs:24-49`;
  `BagGridCell.cs:745-764`). Popup opacity knob default 0.95 (`GridTheme.cs:220-223`).
- **F9 co-existence**: with the F9 editor open, B opens the window as a non-interactive EDIT
  PREVIEW (`StationeersUIMod.cs:496-502`; `TheGridPanel.cs:373-381`).

## 12. Universal Inventory — navigation and selection

**Two scroll regimes** (`GridSelection.cs:134-150`, `:240-254`; gate `GridKeyboardNav` default
true):
- **Cursor CAPTURED** (normal play): the wheel drives a **highlight cursor** through every
  cell/tab (main window + pinned windows in one loop, auto-scrolling whichever window holds the
  highlight). The window's own wheel-pan is disabled (sensitivity 0). Honors vanilla's
  InvertMouseWheelInventory; suppressed in Placement mode (build-kit variant cycling); wraps at
  the ends.
- **Cursor FREED**: scroll-select stands down; the wheel **free-pans** the window
  (sensitivity 28).

**F** (vanilla InventorySelect) in the captured regime is **bidirectional**
(`GridSelection.cs:312-331`; `BagGridCell.cs:404-427`): occupied highlighted cell -> take/swap
to active hand; **empty cell + full hand -> PLACE the held item there**; highlighted TAB ->
toggle that bag open/closed. **G is deliberately NOT grid-cursor-targeted** — it always runs
global SmartStow+ (`GridSelection.cs:320-324`; the "G stows into the highlighted cell" texts at
`UIAConfig.cs:204-208`, `:527-534` and `StorageTab.cs:85-87` are STALE;
`BagGridCell.StowHereFromKeyboard` is dead code with zero call sites).

**Vanilla nav suppression**: while the window is open + cursor captured, vanilla's own
inventory wheel / Next / Prev / F are Harmony-suppressed (no double cursor); 1-6 equipment keys
are deliberately untouched (`Core/InventoryNavPatches.cs:37-62`; `GridSelection.cs:158-171`).

**Mouse (cursor freed)** (`BagGridCell.cs:349-369`): **left-click = take/equip occupant to the
ACTIVE hand** (swap if occupied; an item nested inside the held thing goes to the OTHER hand);
**right-click = the item's manage wheel**; **drag = move**; empty-cell drags forward to the
scroll view (drag-scroll). **No double-click gesture exists anywhere in the mod.**

## 13. Pinning

Three entry points, all view-only (no game-state mutation):
1. **Drag a bag's manila tab OUT of the window** -> it becomes its own pinned window (release
   inside = just the collapse toggle). Works from inside a pinned window too (nested bags can
   be torn out further) (`GridTab.cs:553-610`; `TheGridPanel.cs:957-975`).
2. **Shift + 1-6** (live vanilla slot keys): TOGGLES that worn container's pin — second press
   unpins (`StationeersUIMod.cs:651-673`).
3. **Mouse-mode plain LEFT-CLICK on a 1-6 HUD equipment box** (cursor free — same condition
   that allows drag-out; 5 px slop, resolves on mouse-up): pin-or-FOCUS (repeat click raises,
   never duplicates). Helmet/glasses (non-containers) play the vanilla fail sound
   (`StationeersUIMod.cs:707-747`, `:822-827`). The old physically-hold-Alt requirement is
   REMOVED.

**Unpin/close**: the pin's own X or shrink button unpins (bag folds back into the main tree);
Shift+# again unpins; B leaves pins alive and interactive; backtick closes everything but
records survive (pins return on next open); an unreachable container closes its window but
keeps the record (`PinnedInventoryWindow.cs:823-840`, `:647-652`; `TheGridPanel.cs:1017-1026`).

**Pinned windows support**: move (title bar), resize (grip, min 180x120), per-bag SORT, cells
identical to the main window (click/right-click/drag/tooltips), own scrollbar, nested tabs,
scroll-cursor participation, geometry persisted per save on drag-END; new pins cascade 26 px;
default 360x260.

## 14. The drag matrix

Bridged by one resolver (`Core/DropResolver.cs:65-93`); priority: vanilla slot button > grid
cell > HUD box > other UI (abort) > open space.

| From -> To | Works? | Notes / cite |
|---|---|---|
| Grid cell -> grid cell / nested bag / pinned cell | YES | insert/merge/swap/move exactly like vanilla `InputMouse.IsValid` (`BagGridCell.cs:539-682`; `ItemActions.cs:136-215`) |
| Grid cell -> hand box / 1-6 box / open vanilla slot | YES | `BagGridCell.cs:712-739` |
| Grid cell -> world (drop at feet) | YES, only when released truly OFF the panel | `BagGridCell.cs:730-734` |
| Grid cell -> radial wedge | NO (drags can't start with a wheel open) | `BagGridCell.cs:551` |
| Hand box -> grid/pinned cell, 1-6 box, other hand, ground | YES (the 2026-08-01 watchdog fix made normal-speed drags reliable; before it only fast flicks worked) | `HudSlotDrag.cs:139-166`, `:418-450` |
| 1-6 box -> all of the above | YES (same driver) | `HudSlotDrag.cs:14`, `:328-342` |
| World item (vanilla drag) -> grid/pinned cell / HUD box | YES (HUD boxes refuse when faded below the availability gate) | `Core/WorldDragPatches.cs:63-90` |
| Radial chip -> another wedge / HUD box / world slot / park | YES | `RadialMenu.cs:1011-1100`, `:1057-1066` |
| Radial chip -> grid cell | NO (not implemented) | release ladder has no grid rung |

Failed drops: invalid target = fail sound + repaint; stale source = silent restore; successful
MP moves dim "pending" until the server echo (2 s backstop) (`ItemActions.cs:667-670`;
`BagGridCell.cs:645-649`, `:781-787`). Drop cues: target HUD box lights, grid cell highlights
(`HudDropCue`; `BagGridCell.SetDropHighlight`).

## 15. Hand boxes

Two boxes only (LEFT HAND / RIGHT HAND), never a hotbar (`HandBoxesWidget.cs:10-16`). Live
thumbnail + state line (`StateText`, or "empty").

- **Plain click does NOTHING** (boxes are raycast-transparent renderers; the click-to-pin path
  explicitly excludes hands) (`HudSlotDrag.cs:24-26`; `StationeersUIMod.cs:754-765`).
- **Drag OUT** (cursor free, 5 px promote slop): to grid/pinned cells, a 1-6 box, the other
  hand (swap), an open vanilla slot, or the ground. **Drag IN** from all the same sources.
- **Active hand highlight**: accent border at 1.4x width + a lit side accent bar; inactive
  hand's title dims. Default accent orange `FF8C29E6`; all F9-editable per element
  (`HandBoxesWidget.cs:133-167`).
- **Hand swap**: normal play = pure vanilla (default **E**, vanilla SwapHands — the mod does
  not touch it). While a wheel is open = the mod's `UIA_HandSwap` (default E) drives the same
  vanilla `HumanHandsBehaviour.SwapHands()`; simple key-down, no tap/hold semantics
  (`RadialController.cs:414-431`).
- **Stow flash**: an item stowed into a container held in a hand flashes that hand box with the
  stowed item's icon (0.35 s, 1.22x pop) (`Core/SlotFlash.cs`; `HandBoxesWidget.cs:176-187`).
- Hover an occupied box (cursor free) = vanilla item tooltip (S18).

## 16. Equipment 1-6 boxes

Six boxes, vanilla order **HELMET, GLASSES, SUIT, BACK, UNIFORM, BELT** (robot uniform slot
reads BATTERY). Each: key digit, thumbnail, name, vanilla-gradient damage bar (hidden when
pristine), pulsing leak/fire glyph (`EquipmentColumnWidget.cs:32`, `:220-285`).

- **Tap = that item's manage wheel; hold = vanilla equip** (S3). Tap on empty = don from hand.
- **Click (cursor free) = pin that container's window** (S13.3).
- **Drag out / in** via the same `HudSlotDrag` driver as hands.
- **Three unrelated numeric binding systems — never conflate:**
  1. The 1-6 keys are hard-wired to the six worn slots via live vanilla buttons — rebind them
     in the GAME's Controls screen (`EquipmentKeyRadialFeature.cs:21-24`, `:61-68`).
  2. **Ctrl+1..0** = quick-open bindings for arbitrary bags (bind: hover the bag wedge in a
     wheel, press the digit; per save, `Hotkeys/<save>.xml`) (`BagHotkeyStore.cs:23-43`).
  3. **Belt home slots** = which tool TYPE lives in which belt slot; learned silently by
     observation (a `Slot.Take` postfix records real placements; per save,
     `BeltBindings/<save>.xml`) (`BeltBindingStore.cs:31-64`, `:226-242`).

## 17. Smart-Stow (SmartStow+)

- **Key**: vanilla `KeyMap.SmartStow` (default **G**) — the mod adds no binding; it prefixes
  vanilla `InventoryManager.SmartStow` (`SmartStowPlus.cs:174-203`). Master toggle
  `SmartStowPlusEnabled` default true.
- **Acts on the ACTIVE HAND only** (vanilla's empty-hand summon branch stays vanilla)
  (`SmartStowPlus.cs:21-40`).
- **Routing order — first stage that yields a candidate wins** (`Core/StowRouter.cs:14-26`,
  `:209-226`; all gates default ON): 1 BeltTool (genuine tool -> directly-worn belt,
  home-slot aware) -> 2 StackMerge (tops up partial stacks; same-press continuation within the
  bag is SP/host-only) -> 3 FunctionalSocket (component -> matching EMPTY socket; ignores gas
  contents, vanilla parity) -> 4 Profile (explicitly assigned bag rules) -> 5 Affinity
  (profile-less bag already holding similar items) -> 6 BagDefault (mining belt -> Ores etc.)
  -> 7 Memory (per-save "where this type went last"; written only by Profile/Affinity stows) ->
  8 GenericFallback (loose build items -> one deterministic general bag) -> 9 BeltFallback ->
  else vanilla SmartStow untouched. Scan depth default 3.
- **Exclusions**: the same `GridModel.IsStorageContainer` predicate the window uses — never
  stows into water-bottle bags, cereal boxes, burger/egg boxes, the starter supplies box, any
  ItemContainer (`StowRouter.cs:748-751`; `GridModel.cs:301-311`).
- **With a wheel open**: G is re-dispatched to the STATIC `InventoryManager.SmartStow(slot)`
  (bypassing the wrapper that early-returns while the cursor is free — the old "G does nothing
  in a radial" bug); the open wheel refreshes in place (`RadialController.cs:456-480`).
- **Feedback**: the receiving worn container's HUD box (or the hand box holding it) flashes the
  stowed item's icon 0.35 s with a scale pop; reliable even when vanilla did the placing
  (`Core/SlotFlash.cs`; `SmartStowPlus.cs:69, 83, 191`). Optional stow toast (destination +
  reason) default OFF (`UIAConfig.cs:426-429`).
- **Ghost hints**: while dragging in profile mode, the bag SmartStow WOULD pick glows on its
  region border (dry-run, side-effect-free, ~4 Hz); tab accent = "release here to PIN", region
  accent = "G would stow HERE" (`GridGhostHint.cs:7-35`; `GridGhostHints` default true).
- The SmartStow-profiles REDESIGN (`Documentation/SmartStow-Profiles-Redesign-Plan.md`) is
  **plan-only, no code** — never document Stow Plans / bag cards as shipped. Known gap: bag
  profile rename has no working UI.

## 18. Tooltips

- **Grid / nested / pinned cells**: hovering an occupied cell (cursor free) raises the REAL
  vanilla item tooltip (`PanelToolTip`), gated by vanilla's ShowSlotToolTips setting; cleared
  on radial-open/pool/disable (`BagGridCell.cs:491-524`; `Core/VanillaTooltip.cs`).
- **HUD hand + 1-6 boxes**: polled hover tooltip via the same inverse-warped hit-test; heavily
  gated — suppressed while dragging, any radial open, F9/F10 open, **the Universal Inventory
  open**, vanilla menus front (`Core/HudSlotTooltip.cs:54-72`).
- **Z-order**: tooltips render on a nested override canvas at order 6000 — above the grid
  (5020), pins (5030), Control Center (5200), drag ghosts (5250) (`VanillaTooltip.cs:102-136`).
- **Vitals panel**: hover opens the enriched Player Stats tooltip (exact Mood/Hygiene % +
  signed %/min trend), default ON (2026-07-26 report; `VitalsPanelWidget.cs:696`).

## 19. Keybinds — master table, glyphs, persistence

Registry: `Core/UiaKeybinds.cs` (`EnsureBuilt()` lines 69-97). Nine UIA binds; **only
`UIA_Menu` gets a native row in the game's Controls screen** (group "UI Ascended") — the other
eight rebind ONLY in F10 -> Controls tab (deliberate: native rows for R/E/Q/C/B/Tab/F9/MMB
would raise permanent CONTROLS CONFLICT banners, `UiaKeybinds.cs:34-56`).

| Binding | Default | What it does |
|---|---|---|
| `UIA_ToolbeltRadial` | **MMB** | belt wheel: tap sticky / hold transient |
| `UIA_BagRadial` | **Tab** | bag wheel: tap sticky / hold = vanilla scoreboard |
| `UIA_ToolRadial` | **R** | held-item wheel: tap sticky / hold transient (config description text stale) |
| `UIA_Grid` | **B** | Universal Inventory: hold peek / tap latch |
| `UIA_Menu` | **F10** | Control Center (also native-rebindable in game Controls) |
| `UIA_HudDesigner` | **F9** | HUD Designer |
| `UIA_HandSwap` | **E** | swap active hand while a wheel is open |
| `UIA_Page` | **Q** | page a crowded wheel; belt-picker on the belt ring |
| `UIA_FineAdjust` | **C** | fine +-1 value scroll |

Piggybacked vanilla keys (rebind in the game's own Controls screen; the mod re-reads live):
**G** SmartStow, **F** InventorySelect, **E** SwapHands, **L** helmet light, **Q** Drop/Throw
(contextual with UIA_Page — Q only pages while a wheel is open), **1-6** equipment slots,
**Alt** MouseControl (+ the mod's double-tap latch), **J** jetpack, **WASD/Space** movement,
**C** QuantityModifier (soft-collides with UIA_FineAdjust), **B** InstantStop (soft-collision,
harmless), **backtick** HideAllWindows, **MMB** PingHighlight (dead vanilla binding).

Fixed chords/gestures (not rebindable): Ctrl+1..0 open bound bag; 1..0 bind hovered bag;
Shift+1-6 pin toggle; mouse-mode click a 1-6 box = pin; Shift = keep wheel open; Esc/RMB =
close/back; scroll = value/wedge-count/page; A-Z session wedge hotkeys; F9 editor internals
(Ctrl+Z/Y undo-redo, Delete, Ctrl+D duplicate, arrows nudge, Alt bypass snap, Enter/RMB commit
drawn shape, Alt/Ctrl point-edit modifiers) (`Windows/HudEditorMode.cs:222-254`, `:642-643`,
`:687-697`, `:819-883`, `:1841-1910`).

**Glyphs**: `UiaKeybinds.Glyph(id|KeyCode)` (`UiaKeybinds.cs:235-269`) — Mouse2 -> "MMB",
LeftAlt -> "Alt", Alpha3 -> "3", etc. All consumers live-update on rebind (ControlsTab rebuilds
on assign; the hint bar re-reads every frame). Exception: `KeybindChipsWidget` duplicates its
own private glyph formatter (`KeybindChipsWidget.cs:147-157`) — DRY cleanup candidate.

**Persistence**: the nine binds live in `BepInEx/config/com.stationeersuimod.ui.cfg`; Ctrl-digit
bag hotkeys per save in `Hotkeys/<save>.xml`; wedge letter hotkeys are session-only (never
persisted). Rebind UI: F10 -> **Controls** tab (5th tab) -> click the key glyph -> press a key
(Esc cancels; LMB/RMB can never be captured) (`UI/Menu/Tabs/ControlsTab.cs:68-70`;
`UI/Menu/Kit/UiaRebindCapture.cs:41-91`).

**Config folders** (all under `BepInEx/config/StationeersUIMod/`, per-save keyed): `Grid/`
(expanded-bag records), `GridPins/` (pin records + geometry), `Assignments/` (bag->profile),
`BeltBindings/` (tool home slots, learned), `HintUsage/` (radial hint fade counters — NOT stow
memory), `Loadouts/` (named bag->profile sets), `Hotkeys/` (Ctrl-digit bags), `Profiles/`
(SmartStow profiles, global).

## 20. Pausing the game (for the tutorial + F10 pause button)

**The API** (decompile V27758; `WorldManager` is GLOBAL namespace):
- `public static void WorldManager.SetGamePause(bool)` (`WorldManager.cs:1423-1454`) — the one
  chokepoint: sets `IsGamePaused` (public getter, private setter), `Time.timeScale` 0/1, five
  manager IsPaused flags, mutes world audio, pushes `KeyInputState.Paused`, fires the public
  static event `WorldManager.OnPaused` (`WorldManager.cs:53`). It does NOT change
  `GameManager.GameState` (stays Running — our `Guards.CanDraw()` keeps passing).
- Vanilla's Esc menu pauses ONLY when `!NetworkManager.IsClient && NetworkBase.Clients.Count
  == 0` (`InventoryManager.cs:184-194`); Stationpedia adds `!InGameMenuOpen`
  (`Stationpedia.cs:93-99`); un-pause additionally gated on `!NetworkBase.IsPaused` (a
  network-held pause, e.g. a joining client).
- **Multiplayer: there is no real pause.** Esc doesn't pause MP (host with clients gets a
  LOCAL-only Pause button that desyncs prediction; clients never). The only network pause path
  (`NetworkBase.PauseEvent`) is `protected`. A mod pause is **single-player only**.
- The mod currently touches pause NOWHERE (reads `IsGamePaused` only, in Guards/HudAlertPulse).

**The recipe** (all verified):
```csharp
bool CanOwnPause() => !NetworkManager.IsClient
                   && NetworkBase.Clients.Count == 0
                   && !InventoryManager.Instance.InGameMenuOpen;
// pause:   WorldManager.SetGamePause(true);  then RE-ASSERT our Typing input state
// unpause: if (!NetworkBase.IsPaused) WorldManager.SetGamePause(false);  BEFORE releasing our modal
// observe: WorldManager.OnPaused += ... (drop our latch when someone else unpauses)
```
**Known integration traps** (all must be handled):
1. `SetGamePause(true)` clobbers our `Typing` input state with `Paused` -> Esc would open the
   vanilla menu the same frame ours closes. Re-assert
   `KeyManager.SetInputState(InputStateKey, KeyInputState.Typing)` right after pausing.
2. Unwind order: `SetGamePause(false)` FIRST, then `ReleaseModal()` (KeyManager's
   RemoveInputState pops dictionary-ordered state otherwise).
3. `Guards.VanillaMenuWantsFront()` returns true on ANY pause (`Guards.cs:90`) -> our own pause
   would demote the HUD, hide the Grid, and close radials. Add a `Guards.SelfPauseHeld` flag
   OR'd out of that check while WE hold the pause.
4. Vanilla can steal the un-pause (player opens+closes Esc while we hold) -> our latch must
   drop on `OnPaused(false)`.
5. `Time.timeScale` is force-reset on world transitions without clearing `IsGamePaused` -> clear
   our latch whenever `!Guards.CanDraw()` and on Shutdown (hot-reload safety: a double-F6 while
   paused must not leave the game frozen).
6. All mod UI clocks already use `Time.unscaledTime` — menus/HUD/animations keep running at
   timeScale 0.

**F10 header attach point**: title bar built in `UiaControlCenter.BuildTitleBar`
(`UiaControlCenter.cs:336-355`): title (flexibleWidth=1 spacer) -> Simple (:350) -> Advanced
(:351) -> **[pause button goes here]** -> X (:353). Keep the returned `UiaButton` in a static
field and mirror `RestyleCore()` (:224-231) + `Shutdown()` (:517-518) + the
`RefreshDensityButtons()` repaint idiom (:440-444). Helpers: `UiaControls.Button(parent, text,
onClick, w, h, style)` (`UiaControls.cs:396`), `SetSelected`/`SetEnabled` (:36-45). F10 today
is modality WITHOUT time-stop (unlocked cursor, Typing input state, click scrim — but the
simulation runs).

**First-run hook today** (`StationeersUIMod.cs:375-381`, plain `Update()`): fires once when
`!GuideShown && Guards.CanToggleMenus() && !IsOpen && !radialOpen`. `CanToggleMenus` blocks the
main menu and loading BUT can still fire while paused, over a vanilla menu (MP client Esc
doesn't pause), or unconscious (`Guards.cs:19-28`, `:74-81`). For the tutorial, strengthen
with: `!WorldManager.IsGamePaused` (unless ours), `!Guards.VanillaMenuWantsFront()`, and a
responsive-parent check — i.e. roughly `CanAcceptGameplayInput()` minus its `Cursor.visible`
term.

## 21. Recently CHANGED behaviors (teach the new one, never the old)

| Old | Now | Source |
|---|---|---|
| 3 control schemes (A/B/D dropdown) | ONE scheme (The Hub: tap sticky / hold transient); chooser deleted | Wave G 2026-07-25 |
| MMB tap over a wedge selected it | MMB only ever closes | 2026-07-24 |
| Flick-to-commit | removed entirely | Wave G |
| Four per-wheel enable toggles | one master "Radial enabled" | Wave G |
| Scroll-select worked while mouse FREED | inverted: highlight while CAPTURED, pan while freed; F is bidirectional | 2026-07-24 |
| B did nothing with mouse-mod on / insta-closed | opens and stays; proper toggle | 2026-07-24 + 2026-08-01b |
| Hand/1-6 drag-out only worked as a fast flick | normal-speed drags land (watchdog fix) | 2026-08-01 |
| 1-6 click-to-pin needed physical Alt held | plain click in mouse mode pins | 2026-07-26 |
| World-item drop on occupied wedge/box did nothing | swaps like vanilla (merge/insert too) | 2026-07-26 |
| No grid tooltips; tooltips hid behind windows | vanilla tooltips everywhere, always on top | 2026-07-26 |
| No scrollbar | auto-hiding themed scrollbar on grid + pins | 2026-08-01c |
| Universal Inventory settings on the Radials tab | moved to F10 -> Storage | Wave G |
| "Hold MMB to look around in a radial" | removed 0.9.1.1; cursor is simply free in wheels | About.xml 0.9.1.1 |
| Profiles only carried layout | themes carry EVERYTHING (radial + grid + menu skins); full CRUD + shipped-theme restore | 0.9.2.5 |

## 22. Stale text & dead code found (cleanup list)

1. `UIAConfig.cs:348` — R "tap keeps vanilla action, hold opens" (wrong: both open).
2. `UI/Menu/Tabs/GuideTab.cs:31-36` — "hold-a-key wheels" wording (stale); no Universal
   Inventory section at all.
3. `UIAConfig.cs:437-439` + `:135-139` — Grid key "rebindable from the game's Controls screen"
   (only F10 is).
4. `UIAConfig.cs:204-208`, `:527-534`, `StorageTab.cs:85-87` — scroll-select "while freed"
   (inverted) and "G stows into the highlighted cell" (feature does not exist).
5. `BagGridCell.StowHereFromKeyboard` (`BagGridCell.cs:428-438`) — dead, zero call sites
   (delete, or wire it and update the docs — currently it contradicts shipped behavior).
6. `UIAConfig.cs:367-370` — "overflow goes into a MORE wedge" (overflow pages with Q).
7. `RadialMenu.cs:888-891` — hub-drag "shared by sticky and hold mode" comment (hold can't).
8. `KeybindChipsWidget.cs:147-157` — private duplicate glyph formatter (route through
   `UiaKeybinds.Glyph`).
9. `RadialMenu.cs:392-401` — unreachable chip handling in `OnHoldReleased` (parking is
   sticky-only).

## 23. Uncertainties — play-test before documenting

- Ammo-magazine slot behavior in the manage wheel (no magazine-specific code; depends on
  vanilla `Slot.Class`).
- Everything dated 2026-07-24 onward is **compile-verified but NOT play-tested** (each change
  report says so explicitly): the scroll regimes, tooltips, click-to-pin, the drag watchdog
  fix, the scrollbar.
- Vanilla default keycodes (G/F/E/Alt/backtick/1-6) are read live from `KeyManager` — correct
  by construction, but glyphs shown to players must always come from the live lookup, never
  hard-coded.
- `HudSystem.ZonesAvailable()` exact fade threshold for refusing inbound world drops (comment
  says "faded under 0.5"; body not audited).
