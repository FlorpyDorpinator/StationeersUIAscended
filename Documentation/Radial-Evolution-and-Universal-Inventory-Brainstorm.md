# Radial Evolution + The Universal Inventory — Design Brainstorm

*2026-07-17 — a designer's brainstorm for FlorpyDorp & JacksonTheMaster. Nothing here is
implemented; this is a menu of options with recommendations. Decisions marked ⚑ are
FlorpyDorp's call.*

---

## Locked build decisions (2026-07-19) — FlorpyDorp greenlit the full A–Z build

Names locked (FlorpyDorp): the universal inventory system is **"The Grid"**; the MMB tool
radial (belt tools + Hub gateway) is the **"Belt Wheel"**. **No version bump/tag** —
day-to-day branch iteration until FlorpyDorp calls a release. Changes Report per phase.
Tool home slots **bind by tool TYPE**.

**Radial-list dispositions:**
- **R1 → build as Part 1B** (belt-swap picker + per-belt type-bound home slots + grey
  binding labels + anti-hotbar guard). Full implementation.
- **R2 (flick-commit)** — build, behind an **F10 on/off checkbox**.
- **R3 (double-tap repeat-last)** — build, **F10 on/off**, gated behind a *very fast*
  double-tap; a normal second tap of the radial key must STILL close the radial (keep
  that). Configurable threshold (~250 ms).
- **R4 (digit-pick)** — **REJECTED, do not implement.** Digit keys stay vanilla (slots only).
- **R5 (MP pending/drop affordance)** — deferred to an **end-of-dev bug-test pass** before
  release (parked in Documentation/MP-Swap-Latency-Investigation.md). 1B.1's belt-swap leans on it.
- **R6 (aim-context device radial)** — REJECTED (world devices use physical buttons).
- **R7 (favorites/loadout ring)** — **ON ICE** (philosophy fit unclear; hotkey budget tight).
- **R8 (sound)** — build, using existing in-game sounds (UIAudioManager hashes): hover
  tick / commit / open / close / denied.
- **R9 (wedge-count cap)** — no action; wedge count is already a player choice via the F10
  slot-count setting. Not locking it.
- **R10 (progressive hint fade)** — build; hints fade with use but stay until the player
  disables via **F10**.
- **R11 (controller)** — not now.

**Part 2 (the Inventory system) — confirmed scope & additions:**
- Shows **all equip slots 1–6 + both hands + active hand + every nested container,
  recursively/infinitely** (bag-in-bag-in-bag…).
- Hotkey **B** (tap = toggle, hold = peek). **Shift+1–6** opens that container as a pinned
  window. **Mouse-modifier + click a 1–6 HUD slot** opens just that item's box to pin
  (re-implement).
- Inventory panel and radial **allowed open together**.
- **Backtick (`)** closes all open boxes (retain vanilla behavior) — mirror for our windows.
- **Sort** button on boxes (vanilla parity — reuse vanilla's server-authoritative sort if
  it's a single call, never an N-move client loop).
- Each **pinned box has its own ✕**; the big universal window has its own **✕**.
- Follows the global HUD theme (HudPalette).
- **Bag-profile save button (name-on-save)** — **DEFERRED to pass 2**; build the grid so it
  can host a per-bag "save as profile" button/dropdown later. Ties into a planned **major
  SmartStow refactor** (brainstorm together after this lands).

**New feature — Head-look hold:** while a radial is open, hold a key to hand the mouse back
to first-person head-look (turn/move without closing the radial); release restores
cursor-to-radial. Trigger key pending (MMB conflicts with the toolbelt key).

---

## Part 1 — The radial system: where it is, and where it can go

### What we replaced, honestly scored

Vanilla's model: a 10-slot hotbar, digit keys popping per-container windows, and
mouse-driven window management. Its *real* strengths — the ones playtesters miss — are:

1. **Persistent visibility** — a window stays open; you can *glance* at your backpack.
2. **Expert speed** — digit keys are instant; no menu traversal at all for slot access.
3. **Zero learning curve** — every PC gamer already knows hotbars and windows.

Our model (two hand boxes + contextual radials + smart stow) wins on: screen space,
head-up play, physicality (actions are about *things in places*, not abstract slots),
controller-readiness, and sheer coherence. But the three vanilla strengths above are
exactly our three open weaknesses. Part 2 addresses #1. The ideas below address #2 and #3.

### The big idea: marking-menu expert acceleration (R1–R2)

The foundational HCI research on radial menus (Kurtenbach & Buxton's *marking menus*,
1991–94) found the thing that makes pie menus beat linear menus long-term: **the novice
path and the expert path are the same motor action.** A novice presses, waits, looks,
picks. An expert presses and flicks *in the remembered direction* without waiting for the
menu to draw — same muscle movement, ~2–3× faster, and the menu never even appears.
Experts "mark," novices "browse," and every novice becomes an expert for their five most
common picks without practicing anything.

We are *almost* there — hold-and-release-fire already exists. Two additions complete it:

- **R1 — Stable wedge geometry (the prerequisite).** Muscle memory dies if an item's
  angle changes between openings. Rule: **wedge angle = physical slot index**, always.
  Empty slots render as dim/empty wedges rather than re-flowing the ring. This also
  *reinforces* the physicality pillar: the radial becomes a literal view of the
  container's slots, not a sorted list. (Verify current behavior; make it a hard
  invariant. Applies to toolbelt, bag, equipment radials.)
- **R2 — Flick-commit with delayed draw.** If the user commits (moves past the selection
  radius and releases) within ~150–200 ms of the press, execute *without* fading the
  menu in — or with only a ghost flash of the chosen wedge as confirmation. The radial
  visually appears only when the user hesitates. Zero new bindings, zero new UI; the
  radial just *gets out of the way* of people who already know it. This single change is
  my highest-value recommendation in Part 1.

### The rest of the menu, ranked

- **R3 — Repeat-last (double-tap).** Double-tapping a radial key re-executes that menu's
  last selection (e.g. double-tap toolbelt = re-equip previous tool → instant
  wrench↔crowbar cycling, the single most common loop in the game). Cheap, huge.
- **R4 — Digit-pick everywhere.** Bag radials already show digit badges; make digits
  *select* in every radial while it's open (wedge 1–8 = keys 1–8). Keyboard-fast without
  aiming, and it bridges vanilla players' digit muscle memory into the new world.
- **R5 — MP honesty.** Already scoped in `Documentation/MP-Swap-Latency-Investigation.md`: pending
  ghost state on the hand box, drop toast when a move is rejected server-side, a
  `Swap.RoundTrip` metric. A radial that *feels* laggy on MP erodes trust in the whole
  system; the fix is affordance, not speed.
- ~~**R6 — Aim-context priority.** Crosshair on a device → its controls as the radial
  root.~~ **REJECTED (FlorpyDorp, 2026-07-19).** In-world devices are operated by their
  physical buttons/screens, and that's *good* friction (physical logistics), not the
  menu-diving kind the mod removes. The radial stays the verb layer for your body and
  inventory; the world keeps its native physical interaction. (Also: correcting the note —
  no device-under-crosshair radial exists today; only a read-only reticle status panel.)
- **R7 — ⚑ The favorites ring.** A user-curated 8-wedge ring on its own key: "my
  welder, my cable coils, my duct tape." Each entry is a *reference to a physical item
  in a container* — if the item isn't physically present, the wedge is dark. This is
  the accommodation feature for hotbar refugees, and it's also a slippery slope: it is
  one step from becoming a ten-slot hotbar with extra steps, which we swore never to
  build. My honest read: the physical-reference rule (dark when absent, no reserved
  slots, no auto-restock) keeps it on the right side of the line — it's *bookmarks into
  reality*, not magic slots. But this is a philosophy call, not a design call.
- **R8 — Feel.** Per-wedge-boundary audio tick (soft, pitched up slightly toward the
  top of the ring), a distinct commit sound, 40–60 ms scale-pop on the chosen wedge
  before close. Radials live and die on crispness; we have the glass looks, we should
  have the glass *sound*.
- **R9 — Wedge-count discipline.** Hick's law + motor precision: past ~8–10 wedges,
  selection time and error rate climb fast. We already page (Q); add a soft rule that
  content builders (ItemMenuBuilder, DeviceControls) prefer nesting/paging past 8 wedges
  rather than shrinking wedges.
- **R10 — Progressive hint fade.** The RadialHintBar is great for week one and noise for
  week five. Track successful uses per hint (config-persisted counter); after ~25
  successful selections of a given kind, fade that hint permanently (reset button in the
  Control Center). Onboarding that retires itself.
- **R11 — Controller note (future).** Stick-flick radial selection falls out of R1+R2
  nearly free. Not now, but never build anything that *precludes* it.

---

## Part 1B — Toolbelt & tool access: RESOLVED design (2026-07-19, with FlorpyDorp)

This supersedes the R1 / home-slot / loadout-ring sketches above *for the toolbelt
specifically*. The values ruling that settled it: **the belt is a commitment device** —
one belt worn, six fast slots, you can't be miner and electrician at once. We remove the
*execution* tedium of changing loadout; we never remove the *decision*. The earlier
"reach every tool no matter where it is" options were rejected because they dissolved the
decision (that's why they "felt like cheating"). What ships instead:

### 1B.1 — Whole-belt swap ("Q swaps the belt, not the view")

- MMB opens the fast toolbelt radial (Hub wedge + the worn belt's tools), as today.
- Press **Q** while it's open → the ring becomes a **belt picker**: one wedge per
  waist-belt-compatible container carried *anywhere* in your inventory (matched by
  `AllowSwap` into `ToolbeltSlot`, **not** by name; the worn belt shown as "current").
- Select a belt → **swap the entire worn belt for it** in one `OnServer.SwapSlots`. The
  belt and all its contents move as a unit (FlorpyDorp confirms occupied-belt swap works).
  One message, funnel-safe, gated at execute time.
- **The belt *is* the loadout preset** — a physical, in-world one, so zero philosophy cost:
  building and carrying each belt is the good friction; Q only removes the un-equip /
  re-equip fumbling. You still can't use two belts' tools at once.
- **MP:** this is a real mutation, send-only, no client prediction → **not instant** on a
  client. Needs the pending-swap affordance from `Documentation/MP-Swap-Latency-Investigation.md`
  (ghost / loading state on the belt box) or it feels laggy. Single-player is instant.
- Today the radial reads only the equipped belt ([ToolbeltRadialFeature.cs:55]) — the
  picker is a genuine new capability, not a reshuffle.

### 1B.2 — Per-belt tool home slots (smart-stow memory)

- Each belt remembers a **slot-index → tool-type** map. **G** (smart-stow) returns a tool
  to its bound slot; if that slot is occupied, the tool goes to the nearest open slot and
  **rebinds there** (that becomes its new home until moved again).
- **Any placement updates the binding:** a G-fallback rebinds; a manual drag/swap rebinds
  to the dropped slot. So dragging the wrench and cutters back to their original slots
  restores *and* remembers them; G always returns a tool to wherever it currently belongs.
- **Bind by tool TYPE, not the specific item** (recommended, ⚑ to confirm) — a depleted
  welder swapped for a fresh one still homes to the same slot; matches "my wrench lives at
  slot 6." Edge: two of the same type on one belt — the second takes nearest-free (per-type
  can't distinguish them; acceptable, rare).
- **Seeding:** the first time a belt (by `ReferenceId`) has no binding table, seed it from
  the belt's current contents on open — so a fresh-game belt immediately binds its default
  tools to their starting slots. Needs a per-belt "seeded" marker so it happens once.
- **Storage:** per-belt table keyed by belt `ReferenceId`, per-save (mirror
  `BagHotkeyStore` / `BagProfileStore`). Plugs into `SmartStowPlus` step 0 — replace
  `BestDirectSlot`'s first-free pick with home-slot-then-nearest-free for tools bound for a
  belt ([SmartStowPlus.cs:47-60], [:157]).

### 1B.3 — Binding labels on wedges (R1 stable geometry, made legible)

- Each slot wedge shows, in small grey text (reuse the `_hotkey` / `WedgeHotkeys` badge
  rendering in `UnityRadialView`'s `RingView`), the **name of the tool bound to that slot**
  — *even when the slot is empty because its tool is in your hand.* The wedge **persists**
  for a bound-but-absent tool (stable geometry: slot index → fixed angle, dim ghost when
  absent), so "flick up-right = wrench" holds whether the wrench is belted or in hand. This
  is R1 finally made visible.
- On the belt-picker ring (1B.1), a belt's wedge previews its bound tools (compact — a name
  list or a slide-out) so you can read each belt's loadout before putting it on.

### 1B.4 — No hotkey binding for tool-equip (the anti-hotbar rule)

- Tool / equip wedges in the toolbelt radials are **not** Ctrl+# assignable and **not**
  key-bindable — tools are chosen by *position* through this radial, never by a persistent
  number key. This is the line that stops the belt from becoming a hotbar.
- **Unchanged and fine:** Ctrl+1..0 still opens **bags** (`BagHotkeyStore`, `BindableBag`
  wedges), and letter-hotkeys still bind **device settings** (`WedgeHotkeys`,
  `HotkeyInteractable` wedges). Neither swaps an item into your hand.
- **Revises Part 1 R4** (digit-pick everywhere): does not apply to tool-equip wedges.

### Build order & pre-build decompile checks

- Order: **1B.2 + 1B.3 first** (memory + labels — self-contained, single-player testable),
  then **1B.1** (belt swap — pair with the MP pending-swap work), then **1B.4** (the guard).
- Confirmed: occupied-belt swap-with-contents works. Still verify before build — the
  waist-belt-compatibility test for the picker scan (`AllowSwap`/`ToolbeltSlot` typing),
  and that `ReferenceId` is stable across save/load for the binding key (the bag-hotkey
  system already relies on this — spot-confirm).

---

## Part 2 — The universal inventory ("the Manifest")

### Framing: this is not a betrayal of the design

The design philosophy bans the *hotbar* (abstract activation slots) — it does not ban
*seeing your stuff*. Visibility of system state is Nielsen's first usability heuristic,
and vanilla's persistent bag windows deliver it while our radials — transient by nature —
do not. Playtesters saying "I miss seeing inside my bag" are reporting **bad friction**
(information hiding), not asking for their hotbar back. A read-mostly, physically-honest
inventory overview is philosophically sound *provided*:

1. It never gains activation keybinds (slots are places, not buttons).
2. Every mutation routes through the existing `ItemActions` funnel (one action = one
   message, execute-time gates).
3. The radial remains the **verb** layer; the grid is the **noun** layer (right-click a
   cell → that item's radial).

Working name: **the Manifest** (a ship's manifest — what's aboard and where). Alternates:
Loadout, Kit Overview. ⚑

### The design (as requested, sharpened)

One themed glass panel, default anchored right-of-center, listing every container the
player wears in worn order:

```
┌─ MANIFEST ──────────────────────────────┐
│ ▾ Helmet                    [pin] 1/1   │
│   [🔦]                                  │
│ ▾ Suit                      [pin] 4/6   │
│   [O2][N2][filt][batt][ ][ ]            │
│   ▾ Backpack                [pin] 6/8   │
│     [ore][ore][kit][frame][cable][tool][ ][ ] │
│     ▸ Duffel bag (nested)   [pin] 3/10  │
│ ▾ Uniform                   [pin] 2/4   │
│   [tablet][crowbar][ ][ ]               │
│ ▾ Belt                      [pin] 5/8   │
└─────────────────────────────────────────┘
```

- **Sections** per container: header row (container icon, name, used/total count,
  collapse chevron, pin button) + a grid of square slot cells (~48 px, wraps).
- **Nesting**: a bag inside a bag renders as an indented sub-section *inside* the
  parent's outline — thinner border, palette accent tint, independently collapsible.
  Collapse state persists per slot-path.
- **Collapsed** sections still show the header with used/total — so a fully collapsed
  Manifest is a compact "6 containers, 21/37 slots" spine. That's the glanceable state.
- **Cells** show the item icon + the same live stat text the radials use
  (`StateText.For` — quantity, charge, pressure), and the same tier logic (a bare
  character shows uniform only — free if we host it right, see architecture).

**Interactions** (strictly funnel-safe):

- Hover → tooltip (name + stats); optionally flash the matching on-HUD equipment box so
  the grid teaches the HUD's spatial language.
- Left-click cell → take to active hand (`PlayerMoveToSlot`); click an occupied cell
  with a full hand → swap (`PlayerSwapToSlot`). Exactly vanilla's physical semantics.
- Drag cell→cell → one move/swap message on drop, with the pending-ghost affordance from
  the MP latency plan. No bulk operations, no sort button in v1 (a sort is N messages —
  it violates the one-action-one-message rule; revisit only as an explicit ⚑).
- Right-click cell → **the item's radial** (ItemMenuBuilder), centered on the cursor.
  This is the bridge that makes the two systems one system.
- Drag a *section header* out of the panel → that bag becomes a **pinned mini-window**
  you place anywhere; drag it back in (or ✕) to return it. Pin positions persist.

**Hotkey**: recommend **B** ("bags"), tap = toggle, hold = momentary peek (release
closes — the glance gesture). Tab is already the bag radial, so it's out. B is not among
vanilla's default bindings as far as recon has shown, but the recon phase must confirm
against `KeyManager` defaults in the decompile. Registered via `UiaKeybinds` (id
`UIA_Manifest`), so it lands in the game's own Controls screen for free. Secondary
bridge: **Shift+1–6** opens that equipment container directly as a pinned window —
vanilla's "press 4 for backpack" reflex keeps working, it just opens the prettier thing. ⚑

### Architecture: one component, three hosts (the important decision)

Build a single **`BagGridView`** component (a PanelGraphic-based section: header +
pooled slot cells + nested children), then host it three ways:

1. **The Manifest panel** — a modal-ish overlay composing one BagGridView per worn
   container (hotkey B).
2. **Pinned mini-windows** — a lone BagGridView with a drag-title bar.
3. **A HUD document widget** — `HudElementDef` Type `BagGrid` with a `slotPath` param.

Host 3 is the payoff: a pinned bag *is* a HUD element, which means the F9 designer moves
it, profiles persist it, tier masks gate it, the palette/frost/bloom/warp pipeline styles
it, and it ships in shareable profiles — all for zero new code. "Follows the global
theme" stops being a feature and becomes a structural consequence. It also means a
creator could bake a small always-on backpack strip into their shipped profile, which is
the *fourth* accommodation mode falling out for free.

The Manifest modal itself should be built from the same PanelGraphic/HudPalette stack
(and can borrow UiaTheme tokens from the Control Center kit) so theme-following holds
there too.

### Alternatives considered (the "is there something better?" answer)

| Option | Verdict |
|---|---|
| **A. The Manifest as specced** | The right destination. Slightly heavy as a *first* step. |
| **B. Pin-from-radial spills** — a pin button on any bag radial spills that bag into a pinned grid window; no mega-panel | The right *first* step. Directly answers the playtester quote ("keep my backpack visible") with a fraction of the surface area, and it's Manifest phase-1 work anyway (it's just BagGridView host #2). |
| **C. Legacy mode** — stop suppressing vanilla's slot windows, optionally reskin them | Cheapest accommodation, and worth keeping as an emergency escape-hatch toggle, but two visual languages on screen, fragile across game updates, and it teaches players *away* from our system. Not the plan. |
| **D. HUD bag strip** — compact always-on icon row widget | Good, and free once host #3 exists. Not a substitute for the full grid (no nesting, no management). |
| **E. Hold-to-peek only** (no pinning, no persistence) | Elegant minimalism but fails the actual request — players want *retained* visibility. Ship it as the hold-B behavior of A, not instead of A. |

**Recommendation:** build the Manifest, but componentized and staged so option B ships
first. Playtesters get their fix in the first playable build; the Manifest arrives as
"all your pins at once, organized," not as a big-bang panel. And per the standing
research directive, run a short research pass (grid-inventory conventions: Diablo/Tarkov/
RE4 cell affordances, drag ergonomics, colorblind-safe occupancy cues) before the visual
design phase.

### MP-safety rules (non-negotiable, restated for this feature)

Reads: networked occupant/quantity state only (`GetQuantityText`); server-only values
show "--". Writes: `ItemActions` funnel exclusively; `AllowMove/AllowSwap/CanMerge`
checked at execute time; one user action = one message; rejected moves surface the
drop toast (never silently snap back). No client-side quantity math, ever.

### Perf & allocation rules

Pooled cells; rebuild a section only when its slot-signature changes (event if the
decompile offers one, else a cheap per-half-second signature poll); cached stat strings
(the HudSampler/StateText discipline); no per-frame LINQ/closures; icons via the
VanillaIcons late-resolving pattern. Target: an open Manifest costs ~0 alloc/frame at
steady state, same bar as the HUD.

### Recon phase (against the decompile, before any code)

1. Slot-tree enumeration for the local Human (worn containers, nested container
   detection, stable ordering) — how vanilla's own window builder walks it.
2. Slot-change notification: does vanilla expose an occupant-changed event, or do its
   windows poll? Mirror whichever is authoritative.
3. Item icon acquisition: how vanilla slot windows render item images (sprite? RT
   thumbnail?) — reuse, never re-render our own if avoidable.
4. Vanilla drag plumbing (`InventoryManager` drag state) — decide reuse vs. own UGUI
   drag that terminates in a funnel call. (Bias: own drag, funnel commit — less
   coupling to vanilla UI internals.)
5. Stack splitting semantics — what vanilla exposes; if it isn't a clean single-message
   funnel op, splitting is out of v1.
6. `KeyCode.B` collision check in KeyManager defaults.
7. Coexistence: when our Manifest is open and the vanilla window for the same slot is
   somehow opened, who yields (extend the existing visibility reconciliation).

### Phases

- **Phase 0 — Recon** (above), findings with decompile citations. No code.
- **Phase 1 — BagGridView + pinned windows.** Read-only grid + click-to-hand +
  pin-from-bag-radial + Shift+digit direct-open. *Playtester payoff lands here.*
- **Phase 2 — The Manifest.** Hotkey B (toggle/peek), worn-order composition, nesting +
  collapse, persistence of collapse/pin state.
- **Phase 3 — Drag-drop + radial bridge.** Cell drags via funnel, right-click item
  radial, pending-ghost/drop-toast (shared work with the MP latency plan — do them
  together).
- **Phase 4 — HUD widget host + polish.** `BagGrid` HudElementDef type, F9 integration,
  theming pass, tier/robot/corpse edge cases, colorblind occupancy cues.

Each phase: clean build, Changes Report, adversarial decompile review before shipping
(house rules). Version numbers: FlorpyDorp names them.

### Risks

- **Scope gravity toward vanilla parity** (splitting, sorting, filtering, search). Hold
  the line at v1 = see/take/move/pin; everything else is a ⚑.
- **The hotbar slippery slope** — the grid must never gain activation binds; Shift+digit
  opens *windows*, never uses items.
- **MP ghost states** during in-flight moves (mitigated by pending affordance; test on a
  real client, not just host).
- **Icon perf** if vanilla uses per-item RT thumbnails (recon #3 decides the strategy).
- **Two theme systems drifting** (HudPalette vs UiaTheme) — pick HudPalette as the source
  of truth for anything that can be pinned to the HUD.

### Open questions for FlorpyDorp ⚑

1. Name: Manifest / Loadout / Kit Overview?
2. Hotkey B + Shift+1–6 bridge — approve defaults?
3. Favorites ring (R7): in, out, or prototype-behind-a-flag?
4. Manifest while radial is open: mutually exclusive, or allowed together?
5. Does v1 include hands/active-hand row in the Manifest, or worn containers only?
