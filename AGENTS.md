# Stationeers UI Ascended — Codex Project Instructions

These are standing instructions for Codex and all delegated agents working in this repository.
They mirror and inherit the complete project rules in `CLAUDE.md`; when either document is made
stricter, follow the stricter rule. Keep both documents synchronized when standing rules change.

## Protected references and working copy

- Base-game source is at `D:\ROCKETWERKZ\Stationeers\trunk`. It is read-only reference material.
  Never write, generate, format, move, or delete anything there.
- `D:\Unity\TrainMod` is a read-only reference for advanced Stationeers mod patterns, especially
  pipe liquids and multiplayer synchronization. Never modify it unless FlorpyDorp explicitly
  changes its scope.
- The active repository is Stationeers UI Ascended. In this session it is mounted at
  `C:\Dev\Stationeers UI Ascended`; FlorpyDorp's normal working copy may be
  `D:\Unity\StationeersUIAscended`.
- The canonical mod source is `Assets/Scripts/StationeersUIMod/`. The
  `StationeersUIAscended/` project is deprecated: do not edit, build, or reference it.
- `Reference/` contains read-only decompiled/game reference material and must never be committed.

## What we are building

**Stationeers UI Ascended** is a client-side Stationeers UI replacement/overhaul by
FlorpyDorp and JacksonTheMaster, using BepInEx 5, StationeersLaunchPad, Unity UGUI, and the
game's Dear ImGui. It replaces inventory interaction and HUD presentation with consistent
radial menus, SmartStow+, and a document-driven visor-style two-hand HUD while preserving the
game's physical inventory and multiplayer authority.

Never call the mod “SSUI”; that is JacksonTheMaster's separate server-hosting project. Use
“Stationeers UI Ascended”, “UI Ascended”, or “UIA”.

Read these before large changes:

- `Documentation/stationeers_ui_redesign_proposal.pdf`
- `Documentation/UI-Ascended-Viability-Assessment-and-Plan.md`
- `Changes Reports/`
- relevant `docs/` design and architecture reports
- relevant `.specstory/history/` records when the current code does not explain intent

## Non-negotiable design rules

1. Radials are central, but only where they support Stationeers' physical two-hand logic.
2. Never add a ten-slot hotbar. The bottom UI is exactly two hand boxes.
3. Multiplayer safety is by construction. Every game-state mutation goes through the game's
   authoritative funnels (`OnServer.MoveToSlot`, `SwapSlots`, `MoveToSlotOrWorld`,
   `Slot.PlayerMoveToSlot`, `PlayerSwapToSlot`, `Thing.Interact`, or `Thing.Merge`) and is
   guarded by `Slot.AllowMove`, `AllowSwap`, or `CanMerge` at execution time. Never use
   `DynamicThing.MoveToSlot`, `Slot.Take`, or client-side `Quantity` mutation. One user action
   means one network message; no bulk loops. All mutations remain in
   `Assets/Scripts/StationeersUIMod/Core/ItemActions.cs`. Multiplayer clients read only
   networked state; server-only values degrade to `--`, never a client guess.
4. Hide vanilla UI; never destroy it. Use the game's own visibility paths, especially
   `InventoryManager.SetUIPanelVisibility`. Prefer additive overlays and opt-in hiding.
5. Fail soft. Apply Harmony patches per class through `PatchHarness.TryPatchAll`. A broken game
   update should disable one feature, not the whole mod. New shader/rendering paths retain a
   safe fallback when optional bundles are unavailable.
6. Reduce bad friction while preserving good friction: remove menu-diving, but retain two hands,
   batteries, physical storage, and logistics.
7. Prove the interaction model before art polish. Runtime-built UGUI is the established primary
   renderer for interactions that have already graduated, but prototype genuinely new interaction
   models in the game's Dear ImGui first; use prefabs only when they materially help.

## API verification and environment

- Verify every game API against the newest decompile in `Reference/StationeersGameVersions/`.
  Folder names contain build numbers; newest wins.
- The live beta-branch game install used by the development project is
  `C:\Program Files (x86)\Steam\steamapps\common\Stationeers` with BepInEx 5.4.23 + HarmonyX.
- Known traps: `OnServer`, `KeyManager`, `KeyMap`, `WorldManager`, and `UIAudioManager` are in
  the global namespace; `RG.ImGui.dll` uses `ImGuiNET`; ImGui texture IDs are per-frame and must
  not be cached; `InventoryManager.CheckDisplaySlot` is overloaded and its Harmony target needs
  explicit argument types; Mouse2/PingHighlight is a dead vanilla binding in the verified build.
- Mod source uses the `StationeersUIMod` namespace.
- Build with `dotnet build "Dev/StationeersUIMod.Dev.csproj" -c Debug` (net48, C# 7.3).
  Do not use newer language features such as target-typed `new` or default interface methods.
- Debug builds auto-deploy to `BepInEx/scripts` for F6 hot reload. Do not install the same mod
  simultaneously through scripts, plugins, and the LaunchPad/Workshop package.
- Obtain zero warnings and zero errors before committing completed work.

## Versioning, commits, and Git

- A release version changes only when FlorpyDorp explicitly names it. Day-to-day commits do not
  imply a version bump or tag.
- When cutting a named release, update together:
  `StationeersUIMod.ModVersion`, `VersionDisplay`, `Assets/About/About.xml`, `CHANGELOG.md`, and
  the matching `vX.Y.Z-alpha` tag.
- Commit identity is
  `FlorpyDorpinator <90305330+FlorpyDorpinator@users.noreply.github.com>`, never a personal Gmail.
- In PowerShell, write commit messages to a file and use `git commit -F <file>`; avoid quoted
  inline `-m` messages.
- More than one agent may work in this tree. Stage only files changed for the task; never use
  `git add -A` around concurrent work.
- Substantive change sets receive an adversarial review against the decompile and runtime
  contracts before shipping. Verify every mutation path and Harmony target signature.
- The private canonical repository is
  `https://github.com/FlorpyDorpinator/StationeersUIAscended`. A requested push uses the
  FlorpyDorpinator GitHub CLI account (`gh auth switch --user FlorpyDorpinator`); never push or
  cut a release merely because a development branch was requested.

## Current architecture

- The visor HUD is document-driven UGUI. Profiles are XML `HudDocument` files containing flat
  `HudElementDef` lists. The shipped default is “Glassy 4.0”, embedded in
  `UI/Hud/Glassy40Default.cs`. Element creation routes through `HudSystem.CreateViewFor`; widgets
  live in `UI/Hud/Widgets/`.
- `HudDocument.Sanitize()` is the fail-soft repair and migration boundary. Stale shipped defaults
  may schema-upgrade; user-named/custom profiles never auto-upgrade. Diagnose profile-specific
  bugs from the user's actual files under
  `<game>/BepInEx/config/StationeersUIMod/HudProfiles/`.
- `VisorWarp`/`HudWarp` bends mesh vertices by absolute canvas position. Apply curvature once.
  Never pre-warp element placement and then mesh-warp again. Wide graphics need subdivided edges;
  moving text may warp its position per frame.
- Borrowed vanilla objects (moodlets, portrait, damage doll) must be returned before their host is
  destroyed. `HudPanel.OnBeforeDestroy()` is the safety boundary. Detach to scene root even if the
  original parent no longer exists.
- Radial interaction is modeled in `Overlay/RadialMenu.cs`, rendered by
  `UI/UnityRadialView.cs`, and populated under `Features/`. Check `UIAConfig.IsA` when adding
  schema-dependent behavior; Option A is the default and classic Option D remains supported.
- Reuse live vanilla assets through `Core/VanillaIcons`; late-resolve assets from live singletons
  rather than hard-coding asset GUIDs. Existing sources include `PlayerStateWindow`/
  `StatusUpdates` children, moodlet ramp-bar art, and the Toilet-Update icon from
  `WastePercentageObject`.
- Console commands patch `Util.Commands.CommandLine.Process(string)` and remain read-only
  diagnostics.
- `PanelGraphic` and `PolygonPanelGraphic` are current mesh-based glass renderers.
  `HudBackdrop` owns the shared final-frame capture and dual-Kawase frost texture. Shader assets
  and the asset-bundle builder live in `Dev/UiaEffectsBundle/`.

## Workflow and safety traps

- Search ignored `Reference/` content explicitly with `rg -uu`; do not assume ordinary searches
  include it.
- Never regex-rewrite source with PowerShell content round-trips; they can corrupt UTF-8. Use
  `apply_patch` for edits.
- Every new static must reset in teardown. Unhook static events such as `Camera.onPreCull`; a
  double-F6 reload must leave no stale canvases, borrowed objects, materials, delegates, render
  hooks, or global shader bindings.
- Diagnose from real runtime state and the newest decompile. A stack containing no mod frame is
  often a vanilla race triggered by the mod, not necessarily a mod exception.
- Keep F10 radial experimental-UGUI work isolated unless a task explicitly includes it.

## Mandatory change reports

After every code, documentation, or design-decision change set, create:

`Changes Reports/YYYY-MM-DD - <short context name>.md`

Each report must explain:

- what changed and why;
- design decisions and rejected alternatives;
- new game/API dependencies with decompile citations where applicable;
- known risks and untested areas;
- exact play-tester checks.

Write reports for a collaborator who was not present during implementation.
