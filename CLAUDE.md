# Stationeers UI Ascended — Project Instructions

## What we are building and why

**Stationeers UI Ascended** is a UI replacement mod for Stationeers (BepInEx 5 +
StationeersLaunchPad), built by **FlorpyDorp** together with **JacksonTheMaster**. It
redesigns the game's inventory interaction and HUD around radial menus, smart storage, and
a visor-style two-hand interface — while preserving the game's physical inventory logic.

> NAMING RULE: the mod is never called "SSUI". SSUI is JacksonTheMaster's **separate**
> server-hosting project. Use "Stationeers UI Ascended", "UI Ascended", or "UIA".

Design sources (read these before large changes):
- `Documentation/stationeers_ui_redesign_proposal.pdf` — the design proposal (17pp).
- `Documentation/UI-Ascended-Viability-Assessment-and-Plan.md` — API-verified technical plan.
- `Changes Reports/` — dated after-reports for every change set, including design philosophy.

## Design philosophy (non-negotiable unless FlorpyDorp says otherwise)

1. **Radials, radials, radials** — but only where they support the physical two-hand logic.
2. **No ten-slot hotbar, ever.** The bottom UI is exactly two hand boxes.
3. **Multiplayer-safe by construction.** Every game-state mutation goes through the game's
   authoritative funnel — `OnServer.MoveToSlot/SwapSlots/MoveToSlotOrWorld`,
   `Slot.PlayerMoveToSlot/PlayerSwapToSlot`, `Thing.Interact`, `Thing.Merge` — and is gated
   by `Slot.AllowMove/AllowSwap/CanMerge` **at execute time**. Never `DynamicThing.MoveToSlot`,
   never `Slot.Take`, never mutate `Quantity` client-side. One user action = one message
   (no bulk loops). All mutations live in `src/Core/ItemActions.cs` — keep it that way.
4. **Hide, never destroy.** Vanilla HUD objects stay alive; visibility only via the game's
   own paths (`InventoryManager.SetUIPanelVisibility`). Additive overlay first, hiding opt-in.
5. **Fail soft.** Patches apply per-class via `PatchHarness.TryPatchAll`; a broken patch after
   a game update degrades one feature, never the whole mod.
6. **Reduce bad friction, preserve good friction** (proposal §18). Menu-diving is bad
   friction; two hands, batteries, and physical logistics are good friction.
7. **POC in the game's own Dear ImGui first**, prefab/UGUI "make it cool" phase later. The
   interaction model must be proven before it gets art.

## Methods & environment (how this codebase is worked on)

- **Game install**: `C:\Program Files (x86)\Steam\steamapps\common\Stationeers` (beta branch).
  BepInEx 5.4.23 + HarmonyX. Never commit anything from `Reference/` — it contains decompiled
  game source used for API verification only (git-ignored).
- **API verification**: every game API a change relies on must be verified against the
  decompile in `Reference/StationeersGameVersions/` (newest snapshot wins; folder names carry
  the build number). Known traps: `OnServer`/`KeyManager`/`KeyMap`/`WorldManager`/`UIAudioManager`
  are in the GLOBAL namespace; `RG.ImGui.dll` uses namespace `ImGuiNET`; ImGui texture IDs are
  per-frame (never cache the IntPtr); `InventoryManager.CheckDisplaySlot` is overloaded (Harmony
  patches must specify argument types); `Mouse2`/PingHighlight is a dead vanilla binding.
- **Build**: `dotnet build -c Release` in `StationeersUIAscended/` (net48; refs resolve from
  the live game install; override with `-p:GameDir=...`).
- **Deploy/test**: VS Code tasks — "UI Ascended: Deploy (plugins)" for stable installs,
  "UI Ascended: Build + Hot Reload (scripts)" for ScriptEngine hot reload (F6 in game; remove
  the plugins copy first or the reloaded instance stays inert), "UI Ascended: Package (SLP mod
  folder)" for the StationeersLaunchPad/Workshop route (never install two routes at once).
- **Version**: bump ALL of these together — `UIAscendedPlugin.PluginVersion` (System.Version
  format for BepInEx) + `VersionDisplay` ("x.y.z Alpha"), `About/About.xml` `<Version>` and
  `<ChangeLog>`, `CHANGELOG.md` at repo root, and a git tag `vX.Y.Z-alpha` pushed to GitHub.
  **NEVER pick the new version number yourself**: unless FlorpyDorp already named it for this
  change set, ASK him what it should be BEFORE committing/pushing a release — he decides
  minor vs patch (e.g. 0.6.0 for the Hub was too big a jump from 0.5.0).
- **Git/GitHub**: repo https://github.com/FlorpyDorpinator/StationeersUIAscended (private).
  Pushes require the FlorpyDorpinator gh account (`gh auth switch --user FlorpyDorpinator`);
  the repo's credential helper is already routed through gh.
- **Review discipline**: substantive change sets get an adversarial review against the
  decompile (multi-agent workflow when available) before shipping; verify every mutation
  path and every Harmony target signature.

## MANDATORY: Changes Reports

After ANY change set (code, docs, or design decisions), write a new file in
`Changes Reports/` named:

```
YYYY-MM-DD - <short context name>.md
```

Each report must cover: what changed and why, design decisions made (and alternatives
rejected), any new game-API dependencies (with decompile citations), known risks or
untested areas, and what a play-tester should check. These reports are the shared record
for FlorpyDorp and JacksonTheMaster — write them for a collaborator who wasn't present.
