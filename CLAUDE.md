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
   (no bulk loops). All mutations live in `Assets/Scripts/StationeersUIMod/Core/ItemActions.cs`
   — keep it that way. On an MP client, read only NETWORKED state; server-only values (e.g.
   `SanitationRatio`) must degrade to "--", never a client-side guess.
4. **Hide, never destroy.** Vanilla HUD objects stay alive; visibility only via the game's
   own paths (`InventoryManager.SetUIPanelVisibility`). Additive overlay first, hiding opt-in.
5. **Fail soft.** Patches apply per-class via `PatchHarness.TryPatchAll`; a broken patch after
   a game update degrades one feature, never the whole mod.
6. **Reduce bad friction, preserve good friction** (proposal §18). Menu-diving is bad
   friction; two hands, batteries, and physical logistics are good friction.
7. **POC in the game's own Dear ImGui first**, prefab/UGUI "make it cool" phase later. The
   interaction model must be proven before it gets art.
8. **Every new effect, setting, dial or knob added to the HUD/UI MUST** (FlorpyDorp's standing
   directive): (1) be per-tier capable via the `HudStyleSlot` model, or be explicitly documented
   as shared/content when it deliberately is not; (2) travel with the profile theme unless it is
   a performance knob — performance knobs go in `HudTheme.Exclude` instead; (3) ship a
   `ConfigMigration` step for any config-key removal or rename (see
   `Documentation/Config-and-Theme-Migration.md` §2) — never just delete/rename the `cfg.Bind`
   call and call it done.

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
- **Repo layout**: this is a Unity project. The mod SOURCE is `Assets/Scripts/StationeersUIMod/`
  (namespace `StationeersUIMod`); the manifest/art is elsewhere in `Assets/`. `Dev/
  StationeersUIMod.Dev.csproj` is the build target. `StationeersUIAscended/` is a DEPRECATED
  older project — do not edit, build, or reference it.
- **Build**: `dotnet build "Dev/StationeersUIMod.Dev.csproj" -c Debug` (net48, C# 7.3 — no
  default-interface-members, no target-typed `new`; refs resolve from the live game install).
  The Debug build AUTO-DEPLOYS the DLL to `BepInEx/scripts` for ScriptEngine — press **F6 in
  game to hot-reload** (no restart). A `BepInEx/plugins` copy must be removed first or the
  reloaded instance stays inert. Get a clean build (0 warnings, 0 errors) before committing.
- **Deploy routes** (never install two at once): plugins copy for a stable install, the
  scripts/ hot-reload for iteration, and the StationeersLaunchPad/Workshop mod-folder package.
- **Version**: bump ALL together — `StationeersUIMod.ModVersion` + `VersionDisplay` (in
  `Assets/Scripts/StationeersUIMod/StationeersUIMod.cs`), `Assets/About/About.xml` `<Version>`
  and `<ChangeLog>`, `CHANGELOG.md` at repo root, and a git tag `vX.Y.Z-alpha` pushed to GitHub.
  **NEVER pick the version number yourself**: unless FlorpyDorp already named it, ASK him
  BEFORE committing/pushing a release — he decides minor vs patch (0.6.0 for the Hub was too
  big a jump from 0.5.0). NOT every commit is a release: day-to-day iteration commits to the
  branch with NO version bump or tag; only cut a version/tag when FlorpyDorp asks.
- **Commit identity**: `FlorpyDorpinator <90305330+FlorpyDorpinator@users.noreply.github.com>`
  — never the personal Gmail. In PowerShell, write the commit message to a file and
  `git commit -F <file>` (inline `-m` with quotes gets mis-parsed into pathspecs).
- **Git/GitHub**: repo https://github.com/FlorpyDorpinator/StationeersUIAscended (private).
  Pushes require the FlorpyDorpinator gh account (`gh auth switch --user FlorpyDorpinator`);
  the repo's credential helper is already routed through gh.
- **Review discipline**: substantive change sets get an adversarial review against the
  decompile (multi-agent workflow when available) before shipping; verify every mutation
  path and every Harmony target signature.

## Architecture map (the systems that exist now — read before touching them)

- **The document HUD IS the mod** — there is no legacy/document mode split any more. 0.9.2.5
  Wave B deleted the old ImGui HUD overlay, the legacy ImGui radial painter, and every fixed-
  panel Show*/size-slider key that predated per-element layout; the UGUI radial renderer is
  unconditional (no `UseUnityRadial` gate). The active HUD layout is an XML `HudDocument` (flat
  list of `HudElementDef`) at `config/StationeersUIMod/HudProfiles/<name>.xml`. The shipped
  default profile is **"Stationeers Blue"** (plus a second curated theme, **"Pure HUD"**) —
  both delivered onto a player's disk by `HudProfileStore.SyncShipped` reading the mod's own
  `HudProfiles/` folder at every launch, and both also embedded verbatim as self-heal factories
  in `UI/Hud/ShippedProfiles.cs` (`Glassy40Default.cs` is an older, narrower embed of the same
  pattern, still referenced by `HudSystem.cs` as a legacy fallback for a handful of pre-0.9.2.5
  profile names). **Hand-sync rule: if you edit `HudProfiles/*.xml`, re-embed the matching
  constant in `ShippedProfiles.cs` by hand** — there is no build step that does this, and a
  divergence makes the self-healed copy hash as "player edited" so it silently stops receiving
  shipped updates (see `Documentation/Config-and-Theme-Migration.md` §3). An `HudElementDef`
  (Type enum, 9-anchor + X/Y/W/H + optional WPct/HPct, Z, `Tiers` Bare/Suited/Robot mask,
  ColorRefs = palette-name-or-`#RRGGBBAA`, per-corner radii, a K/V `Params` bag, plus optional
  per-`HudStyleSlot` (Base/Bare/Robot) style forks — see the next bullet) maps to one
  `HudElementView` widget via the `HudSystem.CreateViewFor` registry; widgets live in
  `UI/Hud/Widgets/`.
- **Profiles & migration**: `HudDocument.Sanitize()` fail-soft-repairs every loaded profile and
  hosts idempotent legacy fixes. Schema-gated auto-upgrade replaces stale SHIPPED defaults
  ("Default", "Glassy N", "Stationeers Blue", "Pure HUD") when their `Schema` is below current —
  but **user-named/custom profiles NEVER auto-upgrade** (this gap caused a recurring "speed
  shows in bare" bug: the user's custom profile held a hand-made element with the wrong tier).
  Per-element per-tier styling now exists precisely to make that class of bug opt-in instead of
  silent: an element normally shares ONE look across tiers; ticking "Separate BARE/ROBOT style"
  (the `HudStyleSlot` model, F9's per-tier STYLE block) forks a tier's own copy of every look
  value, so a suited-only edit can no longer leak into bare. When a HUD/tier bug can't be
  reproduced from the shipped default, **read the user's actual on-disk profiles** at
  `<game install>/BepInEx/config/StationeersUIMod/HudProfiles/*.xml`.
- **Config-tree layout & reset**: a player's settings are NOT just the `.cfg` scalar file —
  `BepInEx/config/StationeersUIMod/` holds `HudProfiles/` (+ `.shipped-manifest`, + preview
  `.png`s), `Grid/`, `GridPins/`, `Assignments/`, `BeltBindings/`, `Hotkeys/`, `HintUsage/`,
  `Loadouts/`, `Profiles/profiles.xml`, `HudIcons/`, `ProfilerSnapshots/`. A "delete the cfg and
  you're fresh" instruction is wrong and a common tester mistake — both the `.cfg` (
  `com.stationeersuimod.ui.cfg`, or `...scriptengine.cfg` under F6) **and** this whole folder
  must go for a genuine fresh-install test. The console command `uiareset` (bare = prints what
  it would delete, `uiareset confirm` in the same session = actually deletes it, restart
  required) is the in-game affordance for this; see `Core/FinderCommands.cs` and
  `Documentation/Config-and-Theme-Migration.md` §4 for the narrower "just the shipped themes"
  alternative (F10 → HUD tab → Advanced → Maintenance → Restore shipped themes).
- **Curvature/warp** (`UI/Hud/VisorWarp.cs` + `HudWarp`): a per-graphic mesh modifier bends
  vertices by absolute canvas position — small centred elements only translate, wide panels
  bow because `PanelGraphic` SUBDIVIDES long edges. Curvature is applied ONCE by the mesh warp;
  do NOT also pre-warp element placement (double-warp `Barrel(Barrel(x))` desyncs the F9
  editor, whose handles forward-warp the logical corners a single time). Moving text (compass
  ticks, moodlet cells) can't carry a baked mesh warp, so those warp their POSITION per frame.
- **The BORROW pattern (critical).** The moodlet strip, the portrait camera, and the damage
  doll are REAL vanilla objects reparented into our HUD (`MoodletBorrowWidget`,
  `PortraitWidget`, `DamageDollBorrowWidget`) — we never re-implement them. RULE: a borrowed
  vanilla object MUST be handed back before its host is destroyed, or vanilla's per-frame
  updater NREs forever (e.g. `StatusUpdates.ManagerUpdate` dereferencing a destroyed
  `StatusTransform`). This is now self-healing via `HudPanel.OnBeforeDestroy()`; keep it that
  way, and always detach to the scene root even if the original parent is gone.
- **The radial system**: `Overlay/RadialMenu.cs` (the `RadialEntry` model + interaction) and
  `UI/UnityRadialView.cs` (UGUI wedge rendering). Menu CONTENT is built in `Features/`:
  `ItemMenuBuilder` (manage-item level), `DeviceControls` (settings enumerated exactly like
  vanilla's inventory window), plus the per-context features (Toolbelt/Bag/EquipmentKey). Live
  stat text under a wedge icon comes from `Core/StateText.For` (client-safe
  `DynamicThing.GetQuantityText`). Two schemas exist — **Option A** (`UIAConfig.IsA`, the
  default) vs classic Option D; check `IsA` when adding wedge behaviour.
- **Reuse the game's OWN assets at runtime** rather than drawing our own: `Core/VanillaIcons`
  grabs live sprites by walking `PlayerStateWindow`/`StatusUpdates` children (`SpriteByName`),
  the moodlet ramp-bar art, and the Toilet-Update icon off `WastePercentageObject`. Some
  assets are NOT in the decompile's asset rip (added in later game updates) — grab them at
  runtime from the live singleton (cached, late-resolving), never hard-code a GUID.
- **Console commands**: register via a Harmony prefix on `Util.Commands.CommandLine.Process
  (string)` (see `Core/FinderCommands.cs`: `finddead`, `findlargebox`). Read-only diagnostics
  only — no game-state mutation.

## Traps, gotchas & workflow

- **Decompile search**: the Grep tool silently skips `Reference/` (git-ignored), so it never
  finds game APIs. Search the decompile with the **Bash tool's `grep`** over
  `Reference/StationeersGameVersions/<newest build folder>/`.
- **Never regex-edit source via PowerShell** — `Get-Content`/`Set-Content` round-trips mangle
  UTF-8 (em-dashes become mojibake). Use the Edit tool.
- **TMP glyphs = ASCII only in DISPLAYED strings.** The game's TextMeshPro font reliably
  renders only Basic-Latin/ASCII. Box-drawing, arrows and dingbats — `▾ ▸ ► ● ✕ ✓ ★ °` etc.
  — render as tofu (`□`) in-game (even though the build is clean; the compiler doesn't care).
  For UI iconography (chevrons, close ✕, arrows, checkmarks) DRAW the shape with
  `TriangleGraphic`/`PanelGraphic`/`PolygonPanelGraphic` (the radial's `_triUp`/`_swipe`
  arrows already do this) or fall back to ASCII (`+ - X > v ^`). Non-ASCII in COMMENTS is
  fine — it never renders. This only bites strings that reach `HudText.Set` / `TMP.text`.
- **Hot-reload safety**: every new static MUST reset in the relevant `Shutdown`/teardown path
  (a double-F6 reload must leave no stale canvases, borrowed vanilla objects, or static
  delegates). Static event hooks — `Camera.onPreCull` and friends — MUST be unhooked on
  teardown or they call into a dead assembly after reload.
- **Diagnose from the real state**: reproduce HUD/profile bugs by reading the user's on-disk
  profiles/config, not just the shipped default; reproduce error spam by finding the throwing
  VANILLA method in the decompile (a stack with NO mod frames is usually a vanilla race we
  merely TRIGGER, e.g. `Thing.OverrideShadows`, and often benign).
- **Concurrent agents**: more than one agent may be editing the working tree at once. Stage
  and commit ONLY the files you changed (`git add <specific paths>`) — never `git add -A`
  while another agent's WIP is uncommitted.

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
