# Changelog — Stationeers UI Ascended

All notable changes to the mod. Detailed engineering write-ups live in `Changes Reports/`.

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
