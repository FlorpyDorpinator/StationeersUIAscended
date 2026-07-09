# Changelog — Stationeers UI Ascended

All notable changes to the mod. Detailed engineering write-ups live in `Changes Reports/`.

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
