# Stationeers UI Ascended (SSUI)

> SSUI redesigns Stationeers' inventory and HUD around modern radial interactions, smarter bag
> organization, and a cleaner visor-style two-hand interface — while preserving the game's
> physical inventory logic and survival-engineering complexity.

A BepInEx / StationeersLaunchPad mod for [Stationeers](https://store.steampowered.com/app/544550/Stationeers/)
by FlorpyDorp. This is the working repository for the full proof-of-concept implementation,
rendered with the game's own Dear ImGui (`RG.ImGui`) — no prefabs, no asset bundles (yet).

## Features

| Feature | Default binding | Status |
|---|---|---|
| Toolbelt radial — equip any tool from your belt | hold **Middle Mouse** | POC |
| Tool radial — Controls + Slots branches for the held tool | hold **R** | POC |
| Slot swap discovery — find compatible batteries/cartridges anywhere in your inventory, swap in place | via tool radial → Slots | POC |
| SmartStow+ — stack-merge priority + bag profiles on top of vanilla `G` stow | **G** (prefix on vanilla) | POC |
| Bag profiles — per-bag item/category rules, XML import/export | profile editor window | POC |
| Two-hand HUD + status strip + vitals (visor style, no ten-slot hotbar) | always on (toggleable) | POC |
| Nested bag radial — navigate backpack → bags → items | hold **Tab** | POC |
| Hardcore HUD gating (helmet/sensor-dependent visibility) | setting | POC |

All bindings and behaviors are configurable via BepInEx config
(`BepInEx/config/com.florpydorp.stationeers.uiascended.cfg`), which StationeersLaunchPad
auto-renders in its in-game mod-config panel.

## Design principles

1. **Radials, radials, radials** — but only where they support the physical two-hand logic of Stationeers.
2. **No ten-slot hotbar.** The bottom UI shows exactly two hands.
3. **Multiplayer-safe by construction.** Every mutation goes through the game's own
   authoritative funnel (`OnServer.MoveToSlot` / `SwapSlots`, `Slot.PlayerMoveToSlot` /
   `PlayerSwapToSlot`, `Thing.Interact`, `Thing.Merge`). The mod computes *intent*; the game
   moves the items. Never `DynamicThing.MoveToSlot`, never `Slot.Take`, never mutate
   `Quantity` on a client.
4. **Hide, never destroy.** Vanilla HUD objects stay alive; visibility only via the game's own
   paths (`InventoryManager.SetUIPanelVisibility`, canvas toggles).
5. **Fail soft.** Every Harmony patch group applies independently; a broken patch after a game
   update degrades one feature instead of killing the mod.

See [Documentation/SSUI-Viability-Assessment-and-Plan.md](Documentation/SSUI-Viability-Assessment-and-Plan.md)
for the full technical assessment and
[Documentation/stationeers_ui_redesign_proposal.pdf](Documentation/stationeers_ui_redesign_proposal.pdf)
for the design proposal (Draft v0.1).

## Repository layout

```
Documentation/            Design proposal (PDF) + viability assessment
StationeersUIAscended/    The mod
  src/                    C# sources
  About/About.xml         StationeersLaunchPad / Workshop metadata
Reference/                (git-ignored) decompiled game source, SLP source, example mods
```

`Reference/` is intentionally not committed — it contains decompiled game code used for API
verification only.

## Building

Requirements: .NET SDK (net48 targeting pack), Stationeers installed with BepInEx 5.4.x.

```powershell
cd StationeersUIAscended
dotnet build -c Release            # GameDir defaults to the standard Steam path
# custom install:
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Stationeers"
```

VS Code tasks (`Terminal → Run Task`):

- **SSUI: Build + Hot Reload (scripts)** — Debug build with embedded PDBs copied to
  `BepInEx\scripts` for [ScriptEngine](https://github.com/BepInEx/BepInEx.Debug) hot reload (F6 in game).
- **SSUI: Deploy (plugins)** — Release build copied to `BepInEx\plugins` (plain BepInEx install).
- **SSUI: Package (SLP mod folder)** — Release build staged as a StationeersLaunchPad folder mod
  (`About/About.xml` + DLL) into `Documents\My Games\Stationeers\mods\StationeersUIAscended`
  and `<game>\mods\StationeersUIAscended`, ready for Workshop publishing.

## Load paths

The same DLL works three ways (do **not** install more than one at a time — the plugin guards
against double-init, but keep it clean):

1. **BepInEx plugin**: `StationeersUIAscended.dll` in `BepInEx/plugins`.
2. **StationeersLaunchPad folder mod**: the packaged mod folder in
   `Documents/My Games/Stationeers/mods` (SLP finds the `BaseUnityPlugin` inside the DLL).
3. **ScriptEngine hot reload**: DLL in `BepInEx/scripts` during development.

If LaunchPadBooster is present (it ships with SLP), the mod soft-registers itself for SLP's
multiplayer mod-list via reflection — as an *optional* (non-required) client-side mod, so you
can still join unmodded servers. There is no compile-time dependency on SLP or Booster.

## Status

Full-scope POC built against Stationeers beta build **27701 (Orbital Update)**, 2026-07-08.
Expect beta churn; patches are version-tolerant and fail soft.
