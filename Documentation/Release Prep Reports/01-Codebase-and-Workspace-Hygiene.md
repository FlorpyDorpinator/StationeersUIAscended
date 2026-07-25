# 01 — Codebase & Workspace Hygiene

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** "How messy and busy is our code base and our workspace? Do we need to clean it up?"

**Verdict:** ACCEPTABLE FOR RELEASE, IMPROVE AFTER — the shipped source is unusually clean for the pace it was written at; the actual mess lives in the workspace and git plumbing, none of which a player ever sees.

---

## Findings

### 1. The source tree itself is in very good shape — say it plainly

The mod source (`Assets/Scripts/StationeersUIMod/`, 161 files, 62,535 lines) shows discipline that most solo projects at 80+ commits/17 days do not:

- **Zero TODO/HACK/FIXME debt.** A full grep for `TODO|HACK|FIXME|XXX|LEGACY` across all 62.5k lines finds exactly **one** hit, and it is a deliberate doc string, not a debt marker: the `"LEGACY (unused since 0.3.1 — see IconRatio)"` config description at `Assets/Scripts/StationeersUIMod/UIAConfig.cs:637`.
- **No orphan files.** A reference scan of all 161 files found only 4 candidates whose primary type is never named elsewhere — and all four are false positives: Harmony patch classes registered by `typeof(...)` in the harness call at `Assets/Scripts/StationeersUIMod/StationeersUIMod.cs:189-218` (`Core/CursorDiagPatches.cs`, `Core/WorldDragPatches.cs`), the console-command patch at `Assets/Scripts/StationeersUIMod/Core/FinderCommands.cs:249-250`, and `Features/BagProfiles.cs` (which hosts `BagProfileStore`, heavily referenced). There is no dead file in the tree.
- **Logging is funneled.** Only 2 direct `Debug.Log` calls exist outside `Core/UIALog.cs`.
- **Doc-comment density is exceptional.** `UIAConfig.cs` (807 lines) documents *why* for nearly every entry, including cross-references and play-test rationale (e.g. the socket-priority caveat at `Assets/Scripts/StationeersUIMod/UIAConfig.cs:439-445`). File headers state design intent (`Assets/Scripts/StationeersUIMod/UI/Hud/HudElementView.cs:8-14`, `Assets/Scripts/StationeersUIMod/Windows/HudEditorMode.cs:9-17`). This is the single biggest reason the codebase will survive being maintained solo.
- **No disabled-code blocks.** No `#if false`, no commented-out method graveyards; the only conditional compilation is the debug gate in `Assets/Scripts/StationeersUIMod/Core/UIALog.cs:37`.

The honest caveat: **335 empty `catch { }` blocks** across the source, concentrated where vanilla state is read defensively (`UI/Hud/HudSampler.cs` has 36, `UI/Hud/HudSystem.cs` 21, `Core/CursorDiag.cs` 18, `Overlay/RadialMenu.cs` 16). This is the deliberate fail-soft idiom and mostly correct for a mod reading game internals — but it is also a bug-masking pattern: a genuine regression in a sampler will render as a silently blank readout, not an error. Not a cleanup item; a thing to stay aware of when diagnosing "X stopped updating" reports.

### 2. Legacy fallback paths: the real dead-code story (~1,900 lines, all intentional, all off by default)

There is essentially no *accidental* dead code. There are, however, **three parallel legacy render paths** kept as escape hatches, which together are the largest cleanup opportunity:

| Legacy path | Size | Gate (default) | Where |
|---|---|---|---|
| ImGui HUD overlay (`HudOverlayFeature`) | 335 lines | `HudConfig.LegacyImGuiHud` = **false** (`UI/Hud/HudConfig.cs:336`) | Drawn only at `StationeersUIMod.cs:857-858`; the comment at `StationeersUIMod.cs:852-856` already calls it "a diagnostics-only escape hatch" |
| Pre-document hand-coded UGUI HUD (`TopStatusBar`, `CompassRibbon`, `EquipmentColumn`, `HandBoxes`, `VitalsCard`, `BareSensesPanel`) | 1,188 lines across 6 files in `UI/Hud/` | `HudConfig.UseDocumentHud` = **true** (`UI/Hud/HudConfig.cs:313`); the false-branch builds them at `UI/Hud/HudSystem.cs:197-206` | These are NOT the document widgets — `Widgets/HandBoxesWidget` etc. are separate implementations (`UI/Hud/Widgets/HandBoxesWidget.cs:17`) |
| ImGui radial painter inside `RadialMenu` | ~360 lines (`Overlay/RadialMenu.cs:1443-1801`) | `UIAConfig.UseUnityRadial` = **true** (`UIAConfig.cs:632-635`); user-flippable at `Windows/SettingsWindow.cs:262` | The UGUI renderer (`UI/UnityRadialView.cs`) is the primary |

Each is documented and gated, so this is "ugly but harmless" — **except** that two of the three gates are user-visible config keys. A player who flips `UseDocumentHud` or `UseUnityRadial` off (or inherits an old .cfg) gets a HUD that looks nothing like the screenshots, and will file a bug about it. Shipping three renderers means supporting three renderers.

**Option D is NOT dead code.** `UIAConfig.IsA` (`UIAConfig.cs:83`) is checked in 20 places across 12 files (e.g. `Features/BagRadialFeature.cs:36`, `Features/ItemMenuBuilder.cs:235`, `Features/RadialController.cs:315`), and `ControlSchema.OptionD` is a live, selectable config value (`UIAConfig.cs:324-330`). It's a maintained A/B behavior set, not a leftover. Whether you *want* to keep maintaining it post-release is a product decision, not a hygiene one.

**Dead config entries (bound, never read):**
- `RadialIconScale` — self-described LEGACY, zero readers outside its own bind (`UIAConfig.cs:636-638`).
- `GridMode` — self-described "DEPRECATED - no longer used" (`UIAConfig.cs:496-500`); referenced only in comments explaining that it's ignored (`StationeersUIMod.cs:810`, `UI/Grid/GridModel.cs:90`, `UI/Grid/TheGridPanel.cs:22`). Its supporting `BagGridView` nested-tree view (`UI/Grid/BagGridView.cs`) is likewise retained-but-unreachable per the note at `UI/Grid/TheGridPanel.cs:22`.
- The whole legacy "7. HUD" section (`HudHandBoxes`, `HudStatusStrip`, `HudVitals`, `HudClock`, `HudContextPanel`, `HudVisorArcs`, `HudScale` at `UIAConfig.cs:236-242`) exists solely to feed the ImGui escape hatch `Features/HudOverlayFeature.cs:42-47`. (The `HideVanilla*` entries in the same section are live — used by the real HUD at `UI/Hud/HudSystem.cs:2259` — so they must survive any prune.)

The uncommitted `Core/ConfigMigration.cs` (fail-soft versioned migration steps, `Core/ConfigMigration.cs:22-27`) is exactly the right vehicle to eventually delete these keys cleanly.

### 3. Size/complexity hotspots — file-by-file honest calls

| File | Lines | Members/types | Call |
|---|---|---|---|
| `UI/Hud/HudSystem.cs` | 2,422 | 92 methods, 1 static class | **The one genuine god-object.** It owns canvas lifecycle, the tier state machine, THREE curvature implementations, the per-frame drive, editor support, vanilla-panel visibility, AND four hard-coded shipped-profile builders (`BuildStarterDocument`/`BuildGlassyDocument`/`BuildGlassy40Document`/`BuildGlassy2Document`, lines 525-908, ~380 lines). It is well-sectioned (`lifecycle` 126, `document mode` 221, `main loop` 908, `curvature` 1513, `helpers` 2029, `editor` 2058, `vanilla visibility` 2236) so it navigates fine today — but it's the file every HUD change touches. The profile builders are the extraction that pays for itself: pure data, no coupling. |
| `Windows/HudEditorWindow.cs` | 1,806 | 48 | Cohesive: one ImGui window's knobs. Fine. |
| `Windows/HudEditorMode.cs` | 1,793 | 81 | Cohesive: the F9 screen-state + tools, internally sectioned (designer 157, point-edit 719, structural edits 1059, line tool 1619, pen tool 1681). Fine; the pen/line tools would be the natural split *if* it grows again. |
| `UI/Hud/HudElementView.cs` | 1,765 | 77 | Big because it IS the element contract (geometry resolve, per-curvature-mode placement, bare overrides, style). Cohesive-but-big. Leave it. |
| `Overlay/RadialMenu.cs` | 1,801 | 3 types | Model + interaction + the ~360-line legacy ImGui painter (`drawing` section, line 1443). Deleting the legacy painter (finding 2) IS the split — no other surgery needed. |
| `UI/Hud/PanelGraphic.cs` | 1,757 | 35, single sealed `MaskableGraphic` | One mesh generator. Cohesive. Leave it. |
| `UI/Grid/TheGridPanel.cs` | 1,539 | static class + 3 tiny MonoBehaviours (lines 1437/1476/1522) | Fine. |
| `UI/Grid/GridTheme.cs` | 1,169 | 32 | Mostly flat `cfg.Bind` boilerplate, same shape as `UIAConfig.Bind` (500 lines of it). Verbose, boring, harmless. Leave it. |

Bottom line: only `HudSystem.cs` is genuinely tangled, and only one extraction there (the shipped-profile builders) has a clear payoff-to-risk ratio right now.

### 4. Duplicated helper code — quantified

Six per-save XML stores repeat the identical `EnsureSaveLoaded`/`Save` + `XmlSerializer` + `Directory.CreateDirectory` + `_loadedSaveKey` idiom nearly verbatim: `Features/BagHotkeyStore.cs:67-108`, `Features/BeltBindingStore.cs:69-121`, `Features/GridCollapseStore.cs:53-90`, `Features/GridPinStore.cs:60-100`, `Features/HintUsageStore.cs:47-86`, plus the multi-file variants in `Features/LoadoutStore.cs:98-115`, `Features/BagProfiles.cs` and `Features/HudProfileStore.cs`. That is roughly **300-350 duplicated lines**. The duplication is *consistent* (same idiom every time), which is why it hasn't hurt yet — but a serialization bug fix (e.g. the sharedness gotcha documented at `Features/GridCollapseStore.cs:18`) currently has to be applied eight times.

### 5. Folder taxonomy & naming — mostly coherent, three quirks

Current shape: `Core/` 31 files / 7.2k lines (plumbing, patches, mutations), `Features/` 19 / 6.4k (radial content + persistence stores), `Overlay/` 7 / 2.6k (radial model + ImGui-era helpers + palette), `Profiling/` 2 / 666 (live, console-driven — wired at `StationeersUIMod.cs:154,339-343`), `UI/` 94 / 38.8k (all UGUI rendering: `Grid/`, `Hud/`, `Hud/Widgets/`, `Menu/`), `Windows/` 6 / 5.0k (ImGui editor windows), plus 2 root files.

Quirks (cosmetic, not confusing enough to reorganize pre-release):
- **`Overlay/` is a historical name** from the ImGui era: it holds the radial *model* (`RadialMenu.cs`) while the *renderer* lives at `UI/UnityRadialView.cs`. The pairing is documented, but a newcomer looks in the wrong folder first.
- **HUD persistence is split across boundaries**: `Features/HudProfileStore.cs` stores what `UI/Hud/HudDocument.cs` defines. Same for visual drag-cues living in `Core/` (`Core/SlotFlash.cs`, `Core/HudDropCue.cs`, `Core/DragGhostLayer.cs`, `Core/WorldSlotCue.cs`) — they're UI, filed under plumbing.
- **`UIA` vs `Uia` casing is mixed**: `Core/UIALog.cs`, `Core/UIASort.cs`, `UIAConfig.cs` vs `Core/UiaKeybinds.cs`, `Core/UiaAbDriver.cs`, `Profiling/UiaGcMonitor.cs`, and the entire `UI/Menu/Kit/Uia*.cs` family. Purely cosmetic.

### 6. Workspace clutter at the repo root — this is where it's actually messy

The repo root currently mixes project, junk, and build output:

- **Tracked junk (should not be in git):**
  - `mdpi_6037.pdf` — 413 bytes; it is not a PDF, it's a saved "Access Denied" HTML error page.
  - `t3.ps1` — a UTF-16 one-off script that greps `MatterState` out of `Assets/Assemblies/Assembly-CSharp.dll`; a scratch command, not a tool.
  - `modding tools issue.md` — a 3.5KB scratch note from 2026-07-09.
- **Untracked junk:** `debug.log` (Chromium crashpad spam, ignored via `*.log`), `.utmp/` scratch (ignored, `.gitignore:5`).
- **Build artifacts at root (correctly ignored, `.gitignore:96-100`):** six `StationeersUIMod-*.zip` (~8MB) and `dist/` (2.4MB). Fine, just visual noise.
- **`StationeersUIAscended/` (906KB, 28 tracked files)** — the DEPRECATED pre-Unity POC. Nothing live references it: the `StationeersUIAscended` hits in `Assets/About/About.xml:3` (the `<ModID>`) and `.vscode/tasks.json:8,43` (deleting its *stale DLL* from the game folder) are name coincidences/cleanup, not dependencies. It is pure history sitting in the working tree.
- **Two documentation folders**: `Documentation/` (the CLAUDE.md-canonical design sources, 3 files + PDF) and `docs/` (27 tracked working docs incl. `ARCHITECTURE.md`, plus extension-less files `Big Prompt`, `UI Upgrade`, and a 1.8MB `UI Concept Art.png`). Every new collaborator (or agent) has to learn which one is real.
- **`HudProfiles/` at root is intentional and fine** — it's the shipped extra-profiles payload, self-documented (`HudProfiles/README.md:1-17`) and packaged verbatim into the release (`dist/StationeersUIMod/HudProfiles/`). Not clutter.
- **`Old Themes/` (13 tracked XMLs incl. `Show Jackson.xml`, `Attempt 1.xml`)** — design history for the Glassy line. Harmless, but it's museum content living at top level.
- **`Changes Reports/` — 140 files / 1.3MB in 17 days (~8/day).** The mandate is working exactly as designed; at this growth rate it will pass 300 files within two months and become append-only noise without an index.

### 7. Git repository health — the one finding that surprised me

- **`.git` is 435MB, but the actual packed history is only 65MB.** `git count-objects -vH` shows **10,239 loose objects totaling 349.7MB**, and `git fsck` reports **8,735 unreachable objects** — debris from the July 9 history scrub plus binary churn. A single `git gc --prune=now` reclaims ~350MB and makes every clone/fetch/status faster. Zero risk, five minutes.
- **A 7.2MB private session log is still reachable in main's history.** `.specstory/history/2026-07-08_19-03-31Z-hello-please-read-through.md` was committed in `552db40` and only *untracked* in `dfc3b6e` — untracking does not remove history. Your own `.gitignore:84` says these logs "contain account details." Irrelevant while the repo stays private; **disqualifying if the repo ever goes public.**
- **Game binaries are tracked**: `Assets/Assemblies/` (13MB) includes the game's own `Assembly-CSharp.dll` (6.2MB) and `Assembly-CSharp-firstpass.dll`. Standard practice for a private modding repo, but this is redistribution of RocketWerkz's copyrighted code the moment the repo flips public. Same public-flip blocker class as the session log. (Note `.gitignore:78` excludes `Assets/Plugins/**` but `Assets/Assemblies/` was never covered.)
- **`.gitattributes:40-43` sets `merge=union` for `*.cs`, `*.js`, `*.json`, `*.xml`.** Union merge resolves conflicts by *keeping both sides with no conflict markers* — on C# source this silently produces duplicated/interleaved code that may even still compile. With the documented workflow of concurrent agents editing one tree, this is a latent source-corruption trap, not a convenience.
- **Stale branches**: `Imgui-Unity`, `Imgui-Unity-Sol-0.9.0.5`, `florpy/imgui-playspace` are fully merged into main; `origin/imgui` lingers remotely. `archive/prefab-radial-experiment` is correctly named as an archive.
- **Unity `.meta` coverage is inconsistent**: only 29 of 161 source `.cs` files have `.meta` siblings (e.g. `Core/Guards.cs.meta` exists, `Core/StowRouter.cs` has none). The day anyone opens the Unity editor again, it will generate ~130 metas and dirty the tree. Related: the main Unity project's asset payload (`Assets/TextMesh Pro` 28MB, `Assets/Assets` 9.2MB of SLP-template leftovers — `BootScreen/DM_logo.png`, `TestStructureBlender.fbx`, `deanamiccore_background.mp3`, `PineapplePlant` — plus `Assets/Texture2D` 6.5MB, `Assets/GameData/autolathe.xml`) ships **nothing**: the actual release package is just DLL + About + HudProfiles + `uia_effects.bundle` (see `dist/StationeersUIMod/`), and the shader bundle is built by the separate mini-project `Dev/UiaEffectsBundle/` (clean, tracked source only). The main Unity project is ~45MB of tracked template payload whose only remaining consumer appears to be history.

---

## Severity — how bad is it really

**Nothing in this area blocks the release.** A player installing from Workshop receives a 7-file package; every finding above is invisible to them.

Calibrated:

- **Will bite users:** the two user-visible legacy gates (`UseDocumentHud`, `UseUnityRadial`) — support-ticket generators, not crashes. Everything else user-facing is clean.
- **Will bite YOU (soon):** `merge=union` on `.cs` (silent code corruption on the next real merge — you run concurrent agents, this is loaded); the 435MB `.git` (slow clones, slow status, and it grows); `Changes Reports/` unindexed growth.
- **Will bite you (only on a public-repo flip):** the session-log blob and the tracked game DLLs. Both are history-rewrite jobs, and you have already paid the SHA-churn cost of one scrub — better to decide the repo's public/private future *before* accumulating more history on top.
- **Ugly but harmless:** root junk files, two doc folders, `Old Themes/` at root, the deprecated POC folder, Uia/UIA casing, `.meta` inconsistency, ~1,900 lines of gated legacy renderers, 300+ lines of store boilerplate, `HudSystem.cs` at 2,422 lines. All cosmetic or contained.

Overall: this is a **clean codebase in a messy garage**. The cleanup that matters is measured in hours, not days.

---

## Recommendations

Nothing here is P0 — no hygiene item must block the release.

1. **P1 (S)** — Fix `.gitattributes`: delete the `merge=union` lines for `*.cs`/`*.js`/`*.json`/`*.xml` (`.gitattributes:40-43`), keeping union only if you truly want it for `CHANGELOG.md`-style append files. One-line diff; prevents silent source corruption in exactly the concurrent-agent workflow CLAUDE.md warns about.
2. **P1 (S)** — `git gc --prune=now` (optionally `git remote prune origin` and delete the merged branches `Imgui-Unity`, `Imgui-Unity-Sol-0.9.0.5`, `florpy/imgui-playspace`). Reclaims ~350MB of loose objects; zero behavioral risk.
3. **P1 (S)** — Untrack the root junk: `git rm --cached mdpi_6037.pdf t3.ps1 "modding tools issue.md"` and delete them (move the note into `docs/` if it still matters). While there, add `Assets/Assemblies/**` to `.gitignore` for *future* changes (history keeps the old blobs either way — see rec 8).
4. **P1 (S)** — Decide the repo's public/private future NOW and write it down in CLAUDE.md. If it stays private forever, recs 8's history concerns evaporate. If a public flip is ever on the table, treat the `.specstory` blob (`552db40`) and `Assets/Assemblies/` game DLLs as pre-flip scrub blockers.
5. **P2 (M)** — Retire the legacy render paths, one release after 1.0 feels stable: delete `Features/HudOverlayFeature.cs` + the dead "7. HUD" toggles (`UIAConfig.cs:236-242`, keeping `HideVanilla*` + `HardcoreGating` if still wired), the pre-document panel set (`UI/Hud/TopStatusBar.cs`, `CompassRibbon.cs`, `EquipmentColumn.cs`, `HandBoxes.cs`, `VitalsCard.cs`, `BareSensesPanel.cs` and the `HudSystem.cs:197-206` branch), the ImGui radial painter (`Overlay/RadialMenu.cs:1443-1801` + `UseUnityRadial`), plus the dead keys `RadialIconScale` and `GridMode`/`BagGridView`. Use `Core/ConfigMigration.cs` steps to drop the orphaned keys from player .cfg files. Net: ~2,200 lines and two support-matrix dimensions gone. (None of this touches displayed strings, so the TMP ASCII-only rule is not implicated.)
6. **P2 (S)** — Extract the four shipped-profile builders (`UI/Hud/HudSystem.cs:525-908`, ~380 lines) into a `UI/Hud/HudShippedProfiles.cs`. Pure mechanical move, shrinks the god-object by 16%, and puts "what ships" in one findable place next to `Glassy40Default.cs`. Do NOT attempt a broader HudSystem decomposition (see What NOT to do).
7. **P2 (S)** — Consolidate documentation: fold `docs/` into `Documentation/` (or the reverse), give `Big Prompt`/`UI Upgrade` extensions, and add a 20-line index at `Changes Reports/README.md` grouping the 140 reports by feature area so the mandated record stays navigable. Move `Old Themes/` under `Documentation/` or `HudProfiles/Archive/`.
8. **P2 (S)** — Move `StationeersUIAscended/` out of the working tree: it is already fully in git history; delete the directory in a commit ("the POC lives at tag/history X"), or keep it on an `archive/` branch like the prefab experiment. Update the two doc mentions.
9. **P2 (M)** — Introduce one `SaveScopedXmlStore<T>` helper (C# 7.3-compatible: plain generic class, no default interface members) and migrate the six small stores onto it (`BagHotkeyStore`, `BeltBindingStore`, `GridCollapseStore`, `GridPinStore`, `HintUsageStore`, `LoadoutStore`). Leave `HudProfileStore`/`BagProfiles` alone — their shapes differ enough that forcing them under the helper is where this refactor would start costing more than it saves.
10. **P2 (S)** — Decide the main Unity project's fate: if nothing ships from it (the package is DLL + About + HudProfiles + `uia_effects.bundle`, and the bundle builds from `Dev/UiaEffectsBundle/`), then `Assets/Assets/` (9.2MB of SLP-template leftovers), `Assets/Texture2D/`, `Assets/GameData/` and possibly `Assets/TextMesh Pro/` can be pruned from the tree — or the whole Unity-project layer documented as "editor convenience only." If Unity IS still needed, open it once and commit the generated `.meta` files so the tree stops being one editor-launch away from 130 dirty files.

---

## What NOT to do

- **Do not decompose `HudSystem.cs` beyond the profile-builder extraction.** Its sections (lifecycle / main loop / curvature / vanilla visibility) share the same static state (`_canvas`, `_panels`, `_animator`, tier tracking). Splitting them into classes means threading that state through constructors or — worse — more static singletons, for zero behavioral gain days before a release. The section comments already do the navigational work.
- **Do not "fix" the 335 empty catches wholesale.** They are the fail-soft contract around vanilla reads. A blanket logging pass would spam `UIALog` on every vanilla race the mod merely observes (the `Thing.OverrideShadows` class of noise) and drown real errors.
- **Do not reorganize the folder taxonomy or normalize `Uia`/`UIA` casing now.** Both are cosmetic; renames churn every open branch and the uncommitted WIP (`Core/ConfigMigration.cs`, `HudProfileStore` SyncShipped, StateChips words mode) for zero player value.
- **Do not delete Option D pre-release.** It's live, referenced in 12 files, and it is your A/B control for interaction complaints during the first public wave. Decide its fate *after* you have public feedback, then remove it with a ConfigMigration step.
- **Do not rewrite the store boilerplate before release.** Serialization code is exactly where a "harmless" refactor eats a save file. It's a P2 for a quiet week.
- **Do not scrub git history again casually.** The last scrub already changed every SHA; do it at most once more, only if/when the public-flip decision (rec 4) demands it, and do the session log + game DLLs in the same pass.
