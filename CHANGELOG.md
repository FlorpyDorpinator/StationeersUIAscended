# Changelog — Stationeers UI Ascended

All notable changes to the mod. Detailed engineering write-ups live in `Changes Reports/`.

## 0.7.0 Alpha — 2026-07-11 — THE HUD DESIGNER

The HUD stops being ours and becomes YOURS. Everything on screen is now an element in a
**layout document** you edit in-game with F9 — built across five reviewed phases (opus
implementation fleets + per-phase adversarial reviews; engineering details in the
Changes Report).

### The designer (F9)
- **Click any element** → selection box with 8 drag handles (they hug curved elements —
  handles are drawn through the visor warp). Drag to move, handles to resize; **grid
  snap** with a live toggle + size slider (hold Alt to bypass).
- **Create your own UI**: add any of 18 element types; boxes with per-corner rounding;
  your own text labels; icons (26 built-in thin-line glyphs, PNG overrides in
  `config/StationeersUIMod/HudIcons/`); **draw line work** by clicking points on screen.
- **Edit anything in place**: the popup shows exactly the selected element's properties —
  geometry, per-corner radii, tier visibility, fonts, and every colour as either a
  palette link (theme follows the wheels) or a custom literal. Gauges can flip
  **vertical/horizontal** and recolor their **warn/crit zones per element**.
- **Undo/redo** (Ctrl+Z/Y, 50 steps), Del removes, Ctrl+D duplicates.
- **Profiles**: layouts autosave; save-as/switch/share as plain XML in
  `config/StationeersUIMod/HudProfiles/`. The shipped Default matches the concept art
  (vertical instrument cards, dashboard moodlets, round hologram) and regenerates if
  deleted.

### New live elements
- **Moodlet dashboard**: vanilla's status icons re-rendered as centered chips under the
  top bar — 1 = dead center, N = even spread — with caution/critical coloring and our
  thin-line glyphs (vanilla sprite fallback). The vanilla strip is hidden.
- **Body damage silhouette** (7 regions, the game's own per-organ ratios), **suit status
  chips** with state dots (helmet/AC/light, open-visor-while-suited reads critical),
  **vertical instrument cards** with target markers, **readout cards** for any of 18
  data sources (incl. the new felt-temperature), world name / day counter / active-hand
  badge / keybind chips.
- New client-safe data: suit pressure/temperature setpoints, jetpack thrust + propellant
  delta, hygiene, per-region damage, world name. The toilet gauge is server-only and
  honestly reads `--` on multiplayer clients.

### Under the hood
- The legacy fixed-panel HUD remains behind `UseDocumentHud` (off = exact 0.5.0
  behavior) until the designer proves itself in play-testing.
- Vanilla's bottom-right instrument cluster hides through the game's own
  `SetVisible` path, reconciled per-frame, fully restored on exit; the hide-vanilla
  toggles now default on (the mod replaces the whole HUD surface).
- Chip drops (0.6.2) survive on the new hand/equipment widgets.

## 0.6.2 Alpha — 2026-07-11

Three radial quality-of-life additions (FlorpyDorp's spec):

- **Drop chips on your body**: a dragged item chip released over a visor-HUD **hand box**
  or **1-6 equipment box** moves into that slot — swap when occupied, straight move when
  free, all through the same execute-time-gated `ItemActions` funnel (one action = one
  message). The drop zones hit-test through the curvature inverse, so curved HUDs drop
  where they draw. A release on a box is always consumed — it never falls through to
  screen-parking.
- **No child radials mid-drag**: slide-out satellites can't open while you're holding a
  chip, and any open one closes the moment a drag starts.
- **Move while the radial is open** (option, default ON, F10 → Radials): WASD keeps
  walking and Space jumps with a radial up. Implementation note: walking was never
  blocked by key capture — it dies because the unlocked cursor flips vanilla's
  `AllowMouseControl`; we force that one getter back to its normal-gameplay value while
  the radial is open (never touching the key-capture state, so no other vanilla binding
  can fire), and bridge jump's two extra gates only for the duration of the jump check.
  Typing in the search panel never moves you; seated players are excluded (vehicles read
  the same gate); camera look stays on the cursor.

## 0.6.1 Alpha — 2026-07-11

**F9 creative-spawn crash fixed** (post-release addition, same version): vanilla binds
F9 to creative SpawnItem, and its handler NREs when no spawnable is selected — every F9
press in a Creative world threw. Two fail-soft Harmony prefixes: a null-guard on
`Human.SpawnDynamicThing` (vanilla would only ever crash in that state), and suppression
of the vanilla spawn while our HUD editor owns the same key — one press toggles the
editor OR spawns, never both. With the creative spawn menu open, F9 spawns as vanilla
intends; rebind `HudEditorKey` to get both keys at once.

**Option B hold-mode fix (play-test feedback)**: dwell-to-enter now works on **every**
branch wedge, not just The Hub. Hold MMB, rest on The Hub (¼ s) → rest on Backpack →
rest on a category → release on the item: a full no-click journey from belt to any item.
The 0.3 s release grace after each dive still guards against misfires, and entering a
level restarts the dwell timer so you can't cascade through two levels in one rest
without meaning to. Center hint now reads "hover to dive | release to cancel".

## 0.6.0 Alpha — 2026-07-11

**Radial Option B — "The Hub"** (`ControlSchema: OptionB`, pick it in F10 or the config).
Option B is Option A plus a new middle-mouse gesture language; A and D are unchanged.

- **The Hub wedge**: the toolbelt radial grows a top-center wedge, *The Hub*, that
  branches into exactly what Tab opens — Search, every worn equipment piece, grab-another
  — built by the same code, so the two can never drift apart. With no toolbelt worn the
  MMB radial still opens (just the Hub).
- **Hold MMB** (transient, as always): flick and release to equip, and now — point at
  The Hub for a quarter second (or LMB-click it) to dive in; LMB enters bags/categories,
  RMB backs out one level; **releasing MMB always closes** and runs whatever enabled
  action you were hovering. A release right after diving in is treated as gesture
  momentum and cancels (0.3 s grace), never fires the wedge that happens to be under
  the cursor. Releasing over a branch just closes (only Option A latches sticky there).
- **Tap MMB** (new): the same toolbelt radial opens **sticky** — flick around, then
  **tap MMB again on a wedge to select it** (branches navigate deeper, actions run and
  close the menu), tap the CLOSE band to close deliberately (drops parked chips), tap
  empty space to dismiss. MMB is the select button in **every** sticky radial under B
  (Tab and the 1-6 keys too); LMB keeps working exactly as in A.
- Everything Option A does — STOW wedges, device satellites, scroll-adjust values, the
  search panel, drag-out parking, Shift-keep-open, auto-close after one action — is
  inherited unchanged by B.

## 0.5.0 Alpha — 2026-07-10

**The visor HUD** — the full-UI replacement from the feasibility report, built to the
concept art. User guide: `docs/Visor-HUD.md`. The radials are untouched.

### The interface
- **Curved top status bar**: UTC date/clock, PRESSURE · O₂ · TEMP · POWER · WATER
  cells, SUIT STATUS word (worst-of battery/air/filters/waste). Value colors flip at
  the game's REAL damage/warning thresholds (verified in the decompile).
- **Compass ribbon** at top-center (~12 % of the screen, adjustable): sliding cardinal
  letters + ticks past a fixed caret with exact degrees — same numbers as vanilla's
  Navigation readout, but camera-true in free-look.
- **Equipment column** (left): six live-thumbnail boxes = keys 1-6; the robot's #5
  reads BATTERY like its real slot.
- **Hand tray** (bottom-center): LEFT/RIGHT hand boxes on a trapezoid shelf, active
  hand in orange. Never a hotbar.
- **Vitals card** (bottom-right): the game's live 3D player render as a cyan
  **hologram** (tinted portrait RenderTexture + scanlines) beside HEALTH / O₂ /
  POWER / TEMP and a DAY + time footer.
- **Vignette** visor-edge darkening. All procedural UGUI — no assets, no shaders shipped.

### Diegetic tiers + flicker
- The HUD is the SUIT's HUD: numbers only while a powered suit is worn (the game's own
  synced `Powered` bit). Without one: felt-sense **words** — WARM, COLD, THIN AIR,
  HUNGRY, PARCHED, HURT — that flare when a sensation changes; the clock becomes
  MORNING/DUSK/NIGHT. The robot always gets the full readout.
- **Power death collapses the HUD**: 0.8 s decaying flicker + CRT vertical squash, then
  bare senses fade in slowly. A powered suit **boots the panels back one by one**;
  turning any panel off flickers it out; under 10 % battery the HUD glitches with
  single-frame dropouts. All under a `FlickerAnimations` switch.

### Curvature — A, B and C, all implemented
- **A — Vertex warp** (default): per-element mesh bend, crisp SDF text, zero cost.
- **B — Dome projection**: the HUD renders into a RenderTexture shown on a dome grid
  (one true projection) with an optional scanline wash. Off-screen disabled camera,
  manually rendered — the game's own off-screen pattern.
- **C — Curved world canvas** (experimental): the canvas physically floats ahead of
  the camera, cylinder-bent, on ZTest-always materials (the game ships TMP's Overlay
  shader). True perspective curvature.

### The F9 HUD editor
- **Click any HUD element on screen** → an edit popup opens next to it with exactly
  that element's colors and sliders. Hovering highlights its palette entries orange in
  the F9 window (which scrolls to them).
- Everything is editable: curvature mode/strength, per-panel toggles, every size, any
  loaded TMP font + per-role font sizes, the tier/flicker behavior, and all 19 HUD
  colors (own palette — the radials keep theirs) with **Undo/Redo** and reset.
- **Preview tier** combo (BARE/SUITED/ROBOT) + **Test power-death / Test boot** buttons.

### Docs
- `docs/Moodlets-Reference.md`: every vanilla moodlet/status with exact trigger
  conditions and client-safe read paths — the menu for what the HUD surfaces next.
- Vanilla hands/clothing/status panels hide only via the game's own path (opt-in,
  reconciled against their ACTUAL state so vanilla's own re-shows — waking from
  unconsciousness — can't strand them; restored on exit). Legacy 0.1.0 ImGui HUD
  kept as a fallback toggle.

### Review
- 7-dimension adversarial pass (~40 raw findings; verified by hand): **28 fixed,
  5 accepted/refuted** — plus the play-test catch that the curvature bent the wrong
  way on all three modes (flipped; `CurveInvert` in F9 for either taste). Highlights:
  stale atmosphere readings could mask a vacuum (fields now reset; missing air reads
  as 0 kPa); 1.4 px hairlines were silently culled by a 1 px mesh guard; the forced
  portrait camera wasn't released when only the vitals card hid; F9's test buttons
  were cancelled one frame later; F9 = vanilla's CREATIVE spawn key (gated +
  documented — rebind in creative); editor clicks reached the world (BlockCursorRaycast
  now held); robot with no battery now glitches hardest instead of not at all.

## 0.4.0 Alpha — 2026-07-10

Iteration 2 on the Option A schema, plus the full-UI feasibility report
(`docs/Visor-UI-Feasibility-Report.md` — verdict: green light).

### Settings, everywhere, complete
- **Generic device settings**: radials now enumerate controls exactly like vanilla's
  inventory window (same filter, same live state-baked labels, same dry-run for
  disabled states). This surfaces everything that was missing — **jetpack stabilizer**,
  suit **A/C · Air · Filter · Lock**, helmet **visor · light · flush**, portable-tank
  valve — automatically, for every item, including modded ones.
- **One settings rule everywhere**: >2 controls (or controls alongside slots) collapse
  into a SETTINGS wedge; 1–2 controls with no slots stay inline (a canister just says
  OPEN/CLOSE). Clicking SETTINGS opens all of them as wedges.
- **Component satellites**: slide over a battery/canister/cartridge in any slot →
  TAKE / **REPLACE** (the restored full swap list with consequence warnings) / settings.
- **Vanilla units**: suit pressure in kPa, temperature in °C; jetpack thrust as the
  HUD's ×100 number (vanilla never displays thrust in kPa — verified in the decompile).

### Parity
- **Tab shows all six worn pieces** (glasses were missing) and opening any of them is
  IDENTICAL to tapping its 1–6 key. Empty worn slots are STOW wedges.
- Bag levels show empty slots the same way everywhere.

### New interactions
- **Search is a radial**: top wedge = SEARCH + text box (fixed), bottom half fills with
  result wedges as you type; results drag out like any item; wheel pages; Shift keeps
  searching after a take.
- **Z-grab (the vanilla mouse-mod key)**: with a radial open, hold it and click world
  items to tear them into the drag layer as chips — then drop them into bags. Grab AND
  drop are 3 m range-gated client-side because build 27701's server never range-checks
  item moves (verified; the mod must not out-reach vanilla).
- **The radial is draggable by its hub** (snaps back to center on reopen) and has a
  **CLOSE button** on the hub's bottom edge (colors editable in the editor).
- **Shift = keep open**: holding Shift through any action that would close the radial
  keeps it open for the next action.
- **E swaps hands** (Q freed for a future gesture) and the vanilla active-hand ring now
  **stays lit while radials are open** (the cursor-unlock path was hiding it).

### Presentation options (playtest knobs)
- Bag grouping by sorting class: on/off. Free space: empty-slot wedges / aggregate STOW
  wedge / both. Max wedges per radial (6–32); **crowded rings page with Q** — a
  "1/2 · Q: next page" counter floats above the ring (no MORE wedge; vanilla Q-throw
  never fires while a radial is open).
- **Coarse/fine value scrolling**: suit pressure & temperature step ±10 per wheel notch,
  hold **C** for ±1 (thrust and the portable-tank valve stay 1:1 — thrust only has 19
  steps and the valve steps ±10 natively). Real worn suits (`Suit`, a separate class
  from `SuitBase`) now pair into triangle scroll wedges properly.
- **Side line widths**: the wedge outline's side lines are now true pixel widths, set
  separately at the hub and at the rim (equal = straight parallel lines; the old look
  was rim-fat/hub-skinny by construction).
- Icons now scale with their wedge (ratio slider). Shine intensity slider (0 = flat)
  with the RimShine palette colour actually visible at full strength. Dragged-out item
  bubbles are 50% bigger by default and size-adjustable; the editor shows a live demo
  chip. Disabled wedge text has its own palette colour (was welded to the wedge fill).
- **Both hubs stay dressed**: the child radial takes the detail readout, the main hub
  keeps its circle, title and colours.
- **Editor: hover-to-find colours** — point at any element in the preview and the
  entries painting it light up orange in the colour list (and the list scrolls to
  them). **Undo/Redo buttons** for colour edits (one drag = one step; reset is one
  step too). The readout never repeats itself ("Replace / Replace") anymore.

### Review
- Second adversarial pass on this iteration (15 agents): 10 confirmed findings fixed.
  The big one: **Z-grab was typed to DynamicThing where vanilla's pickup is hard-typed
  to Item** — a client could have stuffed the LANDER CAPSULE into a backpack, a
  mutation no vanilla client can produce; now gated `is Item` at grab AND execute.
  Also fixed: wedge-hover vs hub CLOSE/drag zone overlap (a click on a highlighted
  wedge could dump chips), GasMask-family label cache serving one-state-behind labels,
  stale MORE-wedge snapshots (paging removed the mechanism), search-exit orphaning an
  in-flight drag, world-drag drop feedback, and multiplayer label refresh after the
  server round-trip.

## 0.3.0 Alpha — 2026-07-10

The Option A control schema — a full interaction overhaul, F10-switchable against the
classic behavior (now "Option D"). Full write-up: `docs/Option-A-Control-Schema.md`.

### Option A schema (new default)
- **STOW wedges**: empty slots show the blank slot + "STOW"; hovering previews the held
  item on the orange fill.
- **Device child radials**: slide out on a tool/device for its controls — on/off wedges
  name the CURRENT state; suit pressure/temperature and jetpack thrust are
  **scroll-wheel adjustable** (one notch = one vanilla button press, clamped
  server-side). Hardsuit-sized devices branch into SETTINGS / SLOTS.
- **Nested bags**: click opens (bag becomes the main radial, RMB backtracks); slide out
  for STOW / TAKE. Works in Tab and the 1–6 radials.
- **The swap-with-everything satellite is gone** (Option A) — items slide out to
  TAKE / OPEN instead.
- **Auto-close**: one successful action closes the radial. Tap MMB to dismiss any
  sticky radial.
- **Q/E hand switching while a radial is open** (both schemas): Q = left hand active,
  E = right hand active, and the radial rebuilds so stow/equip targets follow. Drives
  the vanilla swap underneath; keys configurable.
- **Search all bags**: the radial transforms into a type-to-filter panel; click a
  result → free hand, or drops at your feet when both hands are full.
- **Drag-out parking**: press-drag items out of radials and park them on screen (≤12);
  drag chips into STOW wedges/bags to transfer; RMB-out drops all parked items on the
  ground; Escape cancels without moving anything. Client-visual until the drop; every
  mutation verified against the pinned occupant at execute time.

### Visuals
- **Full wedge outlines**: borders on the side edges too, with a wedge gap and
  side-edge anti-aliasing (the sides were never AA'd before — that was the jaggedness).
- **Bold game font** (RBNoBold, the scoreboard title face) + ALL CAPS wedge labels;
  font picker with every game font.
- **Live state under icons**: battery %, canister kPa, stack counts, filter wear —
  via the game's own client-safe `GetQuantityText` funnel.
- **Child radials show the readout in their own center** while open.
- Jackson's dim shading exposed: on/off + strength slider.

### Tools & data
- **Radial editor** (F10): screen goes black, live example radials show every wedge
  state; edit all colors (hue wheels), font, sizes, thickness, gap, feather, shading.
- **UIA sorting classes**: 22-class taxonomy for 785 items generated from the game's
  Stationpedia export (classify + audit agent pipeline); bag grouping now says
  "Storage" for nested backpacks instead of the game's coarse classes.

### Removed
- **Emergency Inject (Shift+9 / Shift+8)** — the 0.2.0 testing aid is gone. It read
  raw modifier keys, which the new search panel made dangerous (typing `(` is Shift+9
  and would have consumed a real injector).

### Review
- 20 findings confirmed by a 29-agent adversarial review (6 dimensions + per-finding
  refutation against the 27701 decompile), all fixed. Highlights: search takes now
  bypass `PlayerMoveToSlot`'s hidden MoveAll/MoveAllOfType tail (typing Shift or
  holding jetpack-descend would have bulk-dumped containers on one click); device
  controls re-verify possession at execute time (no adjusting an item a teammate just
  took); a press no longer survives RMB-back to fire an off-screen wedge; releases and
  scrolls only count on the rings (no inserting into a wedge from across the screen);
  radial canvases can no longer freeze over the loading screen.

## 0.2.0 Alpha — 2026-07-09

First playtest-driven iteration, built live against beta 27701 on the official server.

### Interaction
- **Tab semantics swapped**: tap Tab opens the bag radial (sticky — click to navigate),
  hold Tab shows the vanilla scoreboard. Config `TapOpensRadial` flips it back.
- **Slide-out satellite rings**: push past the rim on a tool/item to open its
  controls/slots without committing; dwell-gated so fast flicks aren't hijacked.
- **Sticky "shopping"**: radials stay open and refresh across actions.
- **Find item + Grab another**: flattened type-grouped search across all bags; one-flick
  repeat of the last retrieval. Nested-bag digging eliminated.
- **Equipment keys 1–6**: tap = management radial (toggle, slots, swaps), hold = equip/don.
- **Emergency inject** (testing aid): Shift+9 health / Shift+8 stim auto-injector, routed
  through the game's own server-authoritative use funnel; on-screen toast feedback.

### Visuals & readability
- Real-control filtering: no more phantom "Activate" on crowbars (vanilla `CanKeyInteract`
  filter, same as the inventory window).
- Orange stow slices: wedges that mean "held item goes HERE" fill orange.
- Two-line word-wrapped wedge labels; control wedges name the action, not the item.
- Hub (center circle): dark 80% translucent backing, text geometrically fitted to the
  circle's chord per line, 33% bigger by default (in-game size slider added).
- All-cyan text palette — no grey text anywhere.
- ASCII-only glyphs (the game's ImGui font can't render fancy arrows).

### Fixes
- 25 confirmed findings from a 31-agent adversarial review against the 27701 decompile,
  including: flick-release satellite hijack; RMB/Escape key-up leaking into vanilla
  (tool toggled / pause menu opened on close); held-tool battery "take to hand" paradox
  (now routes to the other hand); SmartStow+ stowing into the other hand; locked-slot
  bypass; stale-menu re-verification; vanilla-parity input gates (unconscious, cursor
  visible, Stationpedia); hold-to-don on 1–6 restored.
- Hotfix: frozen game input after closing any radial (deferred modal release never
  completed).
- Hotfix: Tab tap froze input via the scoreboard (vanilla-key suppression is now
  live-gated: vanilla always handles keys the mod can't act on).

### Dev
- ScriptEngine hot-reload flow (DLL in `BepInEx\scripts`, F6/watcher) with F3-console
  load/unload announcements. `CLAUDE.md` + `Changes Reports/` discipline established.

## 0.1.0 Alpha — 2026-07-08

Initial full-scope proof of concept, built in one pass against beta 27701:
toolbelt radial (hold MMB), tool radial (hold R), slot-swap discovery with consequence
warnings, SmartStow+ (stack merge → bag profiles → type memory → vanilla), bag profiles
with XML import/export and editor, visor HUD overlay (two-hand boxes, status strip,
vitals, clock, context panel, hardcore gating), nested bag radial, settings window (F10).
Multiplayer-safe by construction: every mutation through the game's `OnServer`/`Slot.Player*`
funnel with legality gates at execute time.
