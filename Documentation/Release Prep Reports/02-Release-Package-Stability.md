# 02 — Release Package Stability & Long-Term Survivability

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** "Are we shipping a stable mod package that will work long term?" — will the zip/Workshop package itself be correct, and will the mod survive game updates and real users' machines without falling over?

**Verdict:** NEEDS WORK BEFORE RELEASE — the *codebase* is genuinely release-grade (fail-soft patching, circuit breakers, disciplined teardown, real update-hygiene machinery), but the *release artifact sitting on disk today is stale*: the 0.9.2 zip and `dist/` predate the uncommitted config-migration/theme-sync work, that work has never been played in-game, and the Workshop first-publish path (ModData.DirectoryPath on a subscribed install) is untested. The gap is small and well-defined — a commit, a repackage, and one Workshop-flow test — not a rework.

---

## Findings

### 1. Packaging & deploy — scripted, guarded, and the old dev-shim bug is triple-locked

The release procedure is no longer manual. `tools/package.ps1` is a real, repeatable pipeline (documented in `Changes Reports/2026-07-22 - Build + release tasks (zip + Steam Workshop, mirroring Stationpedia).md` and wired into `.vscode/tasks.json` as *Package Release* / *Publish to Steam Workshop* / *Release & Publish* tasks):

- **Version single-source:** the zip name and staging read `<Version>` from `Assets/About/About.xml` (`tools/package.ps1:51-56`).
- **Release build enforced:** `dotnet build -c Release` (`tools/package.ps1:58-64`).
- **The 0.8.0 dev-shim packaging bug is fixed with three independent locks:**
  1. The shim only compiles in Debug: `<Compile Include="ScriptEngineLoader.cs" Condition="'$(Configuration)' == 'Debug'" />` (`Dev/StationeersUIMod.Dev.csproj:40`, with the 0.8.0 post-mortem in the comment at lines 36-39).
  2. `package.ps1` byte-scans the built Release DLL for the string `ScriptEngineLoader` and **aborts** if found (`tools/package.ps1:66-74`).
  3. The current `StationeersUIMod-0.9.2.zip` verified clean: 8 files, no shim, no loose `.pdb` (PDB is *embedded* in the DLL — `Dev/StationeersUIMod.Dev.csproj:22` — which is fine and actually improves user bug-report stack traces).
- **Ship layout is exactly right:** DLL + `uia_effects.bundle` + `About/{About.xml,Preview.png,thumb.png}` + `HudProfiles/{Stationeers Blue,Pure HUD}.xml + README.md` (verified in the zip listing). The old "ships dev/test profiles" gotcha (flagged in the 2026-07-22 report §Notes) was fixed by the 2026-07-24 curation — junk went to repo-root `Old Themes/`, which does **not** ship.
- **SLP fail-fatal `.assets` trap avoided by design:** the shader bundle deliberately ships as `.bundle`, never `.assets`, precisely because SLP auto-loads `.assets` and a failure there kills the whole mod — `Core/HudShaderStore.cs:28-29`. Bundle load itself is fail-soft to Tier A everywhere (`Core/HudShaderStore.cs:9-13, 94-107`) and `package.ps1` warns loudly rather than failing if the bundle is missing (`tools/package.ps1:84-91`).
- **About.xml ChangeLog cap:** now **4,289 / 8,000 chars** (was ~7,800 and near the SLP hard cap). Fixed; the cap is on the release checklist (`Documentation/Config-and-Theme-Migration.md:161-162`).
- **Steam publish is guarded:** `tools/publish-steam.ps1:47-58` refuses to run while `publishedfileid` is `0` unless `-Force` (an intentional first-time create), and verifies `dist/` exists first (`:39-45`).

**What is wrong with packaging right now — the actual pre-release work:**

1. **The staged/zipped artifact is one iteration behind the working tree.** The Release DLL in `dist/` was built 2026-07-24 23:42; the uncommitted `Core/ConfigMigration.cs`, the `HudProfileStore.SyncShipped` rewrite, and the StateChips words-mode changes are dated 2026-07-25 00:13-00:30. **The zip on disk does not contain the migration machinery it depends on for clean updates.** The user memory note "dist/ staging goes stale — sync before zipping" is a *recurring* failure mode; this is it happening again.
2. **Version-string drift is already visible:** the zip is named `StationeersUIMod-0.9.2.zip` while `About.xml` and `StationeersUIMod.ModVersion` now say `0.9.2.0` (`Assets/About/About.xml:5`, `Assets/Scripts/StationeersUIMod/StationeersUIMod.cs:21`), and the zip's inner `About.xml` (5,739 bytes) differs from the tree's (5,652 bytes) — proof that About.xml was edited *after* packaging. Five places must be bumped by hand (About.xml, ModVersion, VersionDisplay, CHANGELOG.md, git tag); the tag `v0.9.2.0-alpha` exists, so 4 of 5 agree, but nothing *checks* agreement.
3. **`tools/workshop_update.vdf` carries a stale changenote** ("0.9.1.0 Experimental — the Universal Inventory + the Belt Wheel") and `publishedfileid 0`. Publishing today would attach the wrong release note to the Workshop item.
4. **`HudProfiles/README.md` ships in the zip and is stale:** it still says the mod "regenerates the shipped **Default**" and describes the Glassy line as the shipped default (`HudProfiles/README.md:13-15`) — the shipped set is now Stationeers Blue + Pure HUD via `SyncShipped`. Also, the About.xml changelog tells players "the old themes are kept in an 'Old Themes' folder you can still load" (`Assets/About/About.xml`, 0.9.2.0 PER-PROFILE THEMES bullet) — that folder is repo-only and never ships; a fresh Workshop user cannot find it.

### 2. Game-update survivability — the patch surface is small, 100% harnessed, and degrades feature-by-feature

**Patch inventory: 22 Harmony patch classes, and all 22 go through `PatchHarness.TryPatchAll`** (`Core/Patches.cs:13-36`; call site `StationeersUIMod.cs:189-218`). I cross-checked every `[HarmonyPatch]` attribute in the source against the TryPatchAll list — there are no raw `harmony.PatchAll()` calls and no patch class outside the harness. A failed target logs `Patch X FAILED (feature degraded)` and the rest apply (`Core/Patches.cs:28-32`).

What degrades when each cluster breaks after a game update:

| Cluster | Targets | On failure |
|---|---|---|
| **ImGui frame hook** | `ImGuiWindowManager.Draw` postfix (`Core/Patches.cs:43-57`, `nameof` — compile-checked) | **The big one.** Radial rendering, toasts, F9/F10 ImGui windows, profiler all stop. The UGUI visor HUD *survives* (it runs off `Update`, `StationeersUIMod.cs:345-348`). This is the single point of failure for the interactive half. |
| Input suppression | `CheckDisplaySlot` (explicit arg types, `Core/Patches.cs:64`), `ToggleScoreboard` (:89), `CheckDisplaySlotInput` (:329) | Vanilla key handling comes back *alongside* radials — double actions, annoying but playable. |
| Radial movement pass-through | `AllowMouseControl` getter (:153), `AltKeyDown` getter (:167), `HandleJump` (:182), `MovementHandler` (:236) | Can't walk/jump/jetpack with a wheel open; cursor latch dies. Cosmetic-to-annoying. |
| Inbound world→HUD drag | `InputMouse.Drag`/`DragSlot` — **private vanilla targets, `Priority.First` bool prefixes** (`Core/WorldDragPatches.cs:220-233`) | Riskiest targets in the mod (renames likely, and the comment at `StationeersUIMod.cs:210-211` acknowledges exactly this). Failure = inbound drag feature gone, nothing else. |
| SmartStow+ | `SmartStow` (`nameof`), `PerformHiddenSlotMoveToAnimation` (`Features/SmartStowPlus.cs:174,213`) | G-key routing falls back to vanilla SmartStow. |
| Belt bindings | `Slot.Take` postfix (`Features/BeltBindingStore.cs:253`) | Ghost labels/fly-home freeze at first seed. |
| Vanilla NRE guards | `Human.SpawnDynamicThing` (:284), `ThingRenderer.OverrideShadowMode` (:349) | Vanilla's own crash/spam returns — we merely stop papering over it. |
| Keybind rows / console / diagnostics | `SetupKeyBindings` (`Core/UiaKeybindsPatch.cs:19`), `CommandLine.Process` (explicit types, `Core/FinderCommands.cs:249`), 3 cursor-diag patches (`Core/CursorDiagPatches.cs`) | Native Controls rows missing (mod-panel rebind still works); `finddead`/`uiadiag` gone. Inert loss. |

**Reflection-by-name inventory (fails at runtime, not patch time) — all 8 sites are null-checked with a defined degradation:**

- `Item.SmartSortItems` field swap — warn once, vanilla sort; teardown restores only if the field still holds *our* wrapper, and keeps `_original` alive for a stranded wrapper (`Features/ProfileSort.cs:55-107`). This is the most careful game-static handling in the mod.
- `KeyManager.InputState` private setter — warn once, "Space-jump in radials degraded (WASD unaffected)" (`Core/Patches.cs:130-147`).
- `ImGuiManager.SetBlockUguiClicks` — silent skip, modal isolation still holds (`Core/ModalScope.cs:109-127`).
- `KeyManager.AddKey` — looked up **with explicit argument types** so a future overload yields null instead of an ambiguous-match crash on the boot path (`Core/UiaKeybinds.cs:165-172`); failure = no native Controls rows.
- `StatusUpdates.Is<X>Caution/Critical` delegate builds — a channel missing after a game update is simply skipped (`Core/WarningSensor.cs:194-213`).
- `InputMouse.ClearTooltip` (`Core/WorldDragPatches.cs:107`), `MouseModeController._openModals` (diagnostics, `Core/CursorDiag.cs:281`), `ThingRenderer.UnityRenderer` (`Core/Patches.cs:363`) — all guarded.
- Runtime sprite walks (`VanillaIcons.SpriteByName`, `Core/VanillaIcons.cs:312`) return null → icon fallbacks; cached late-resolving as designed.

**Residual hard-fail class the harness cannot cover:** direct compiled references to game APIs (`GameManager.IsBatchMode`, `KeyManager.*`, `Human.HelmetSlot`, …). If the game renames one, the *calling method* fails to JIT and throws where the try/catch inside it cannot help — for `Update` itself that means per-frame Unity-side exception spam that the circuit breaker never sees (the breaker lives *inside* the body, `StationeersUIMod.cs:259-427`). This is inherent to every compiled mod and not fixable in general; it is why the "verify against the newest decompile" discipline matters. **Related, and more actionable: the mod is built against the beta-branch `Assembly-CSharp`** (`Dev/StationeersUIMod.Dev.csproj:28`), while the Workshop audience is predominantly on the public branch. Any beta-only API drift becomes a day-one report from users the dev flow never exercises.

### 3. Runtime robustness — the circuit breaker and teardown are excellent

- **Update() circuit breaker** (`StationeersUIMod.cs:43-54, 245-257, 435-467`): rate-limits logging (first 3 in full, then once/5 s), trips only on a *run* of 30 consecutive throwing frames, stands down for 5 s, retries, and one clean frame clears the streak. Per-instance state, so hot reload starts fresh. The announce-once latch (`_updateBreakerTripped`) prevents banner spam. This is a correctly engineered breaker, not a mute button.
- **Draw hook**: same rate-limited pattern via `ReportDrawException` (`StationeersUIMod.cs:871-890`), and the postfix itself try/catches (`Core/Patches.cs:46-56`).
- **Log-spam guards elsewhere:** per-file `WarnOnce` with re-arm on clean read (`Features/HudProfileStore.cs:238-240, 298`), once-per-Thing shadow-guard log (`Core/Patches.cs:353-374`), autosave back-off instead of per-frame retry (`Features/HudProfileStore.cs:437-457`). `UIALog` itself has no global limiter (`Core/UIALog.cs`) — acceptable because every observed hot path carries its own.
- **Teardown/hot-reload:** `OnDestroy` (`StationeersUIMod.cs:956-1058`) uses a per-instance `_torndown` latch, and — critically — the three *game-static* restores that must never be skipped (`ProfileSort.Reset`, `UiaKeybinds.Unhook`, `Harmony.UnpatchSelf`) live in a `finally` with individual try/catches (`StationeersUIMod.cs:1030-1057`). ~30 static systems are shut down directly, and `HudSystem.Shutdown` (`UI/Hud/HudSystem.cs:846-906`) cascades the rest: borrowed-object hand-back, the shader bundle (`Core/HudShaderStore` with resident-bundle reuse so a double-F6 never double-loads, `Core/HudShaderStore.cs:15-19, 85-90`), RTs/materials, and the `ActiveReplaced` event unhook (:898-902). `KeyManager.OnControlsChanged` is unhooked (`Core/UiaKeybinds.cs:226-231`). No live `Camera.onPreCull`-style static hooks exist (grep found only comments). The only stragglers are cosmetic: the shadow-guard `_logged` HashSet (`Core/Patches.cs:353`) is never cleared (a few longs; dies with the assembly on reload) — harmless.
- **Dedicated-server safety:** batch mode bails out of init before any UI work (`StationeersUIMod.cs:144-148`) and out of the bundle load (`Core/HudShaderStore.cs:81`).

### 4. MP-safety funnel — intact in substance, one letter-of-the-law deviation

Grep for `OnServer.`, `PlayerMoveToSlot/PlayerSwapToSlot`, `.Merge(`, `Quantity =` across the source:

- Every mutation path lands in `Core/ItemActions.cs` **except** `SmartStowPlus.Execute`/`ContinueStackRemainder`, which call `OnServer.MoveToSlot` directly (`Features/SmartStowPlus.cs:82, 143`). Both are gated at execute time (`Slot.AllowMove` :80, `Slot.CanMerge` :65/:132) and the multi-step remainder pass is explicitly host-only (`GameManager.RunSimulation` gate, :112-114). MP-safe in substance; it just violates the CLAUDE.md "all mutations live in ItemActions.cs" location rule.
- The `Quantity` writes in `ItemActions.SplitStackCount` (`Core/ItemActions.cs:607-609`) are behind a `GameManager.RunSimulation` authority gate (:566-568) and mirror vanilla's own `Stackable.SplitStack` — never a client-side guess.
- The inbound world-drag patch routes its mutation through `ItemActions.DragTo` (`Core/WorldDragPatches.cs:201-202`). No stragglers found: no `DynamicThing.MoveToSlot`, no `Slot.Take()` calls, no client `Quantity` mutation.

### 5. First-run / fresh-install path — solid, with one real edge case

`OnLoaded` ordering is correct and each stage fails soft (`StationeersUIMod.cs:81-237`): fresh-install detection *before* anything creates the cfg (:107-115) → legacy dev-shim cfg migration (:121-135) → `UIAConfig.Bind` (which binds `HudConfig` too, `UIAConfig.cs:795`) → `ConfigMigration.Run` (:141) → batch-mode bail (:144) → `BagProfileStore` → `ProfileSort.Install` → `HudProfileStore.SyncShipped` (:165) → keybind registration → features → patches. The whole body is wrapped in try/catch (:97-236).

- **Corrupt config:** BepInEx's own parser is lenient; `ConfigMigration.Run` is fully fail-soft (`Core/ConfigMigration.cs:36-68`) and never lowers a higher version stamp (downgrade-safe, :48-54).
- **Corrupt/missing profile:** `Load` warns once and returns null (`Features/HudProfileStore.cs:266-306`); `LoadActive` self-heals from the matching factory and writes it back (:416-432); `EnsureActiveDocument` picks the factory that matches the *name* so a corrupt Glassy respawns as glass, not the flat starter (`UI/Hud/HudSystem.cs:231-284`). A hand-edited profile is always `Sanitize()`d before use.
- **The new migration machinery is well designed** — `SyncShipped`'s seed/refresh/prune with canonical-hash provenance never touches player-edited files (`Features/HudProfileStore.cs:39-143`), FNV-1a instead of MD5 so a FIPS-policy Windows box can't kill the sync (:170-177), and the adversarial verification round already caught and fixed the freeze-on-repair and version-clamp defects (`Changes Reports/2026-07-25 - Config migration + shipped-theme sync (clean Steam updates).md:80-110`). But it is **uncommitted and has never been run in-game** (same report, :74).
- **The edge case:** if the active profile "Stationeers Blue" is missing *and* `SyncShipped` was inert (e.g. `ModData.DirectoryPath` null or unexpected on a Workshop-subscribed install — a path that has never been exercised), `LoadActive` writes the generic **starter** document to disk under the shipped name (`Features/HudProfileStore.cs:429`; factory selection `UI/Hud/HudSystem.cs:245-252` maps non-Glassy names to `BuildStarterDocument`). From then on `SyncShipped` sees a file whose hash matches neither manifest nor shipped and treats it as *player-owned forever* (`Features/HudProfileStore.cs:103-106`) — the curated theme becomes permanently unreachable for that user without a manual file delete. One bad first launch locks in the wrong HUD.

---

## Severity — how bad is it really

**The code is not the risk. The release process state is.** Everything structural — the harness, the breaker, the teardown, the funnel, the fail-soft file handling — is honestly better than most shipped mods, and every "known past fact" checked out as fixed: dev shim triple-locked, `.assets` trap designed around, ChangeLog at 4,289/8,000.

Calibrated:

- **Will bite users if shipped today (real, avoidable):** uploading the current stale `dist/`/zip ships 0.9.2.0 *without* the migration/sync machinery and with a mismatched About.xml — the first Steam update after that would then hit users with exactly the stale-theme mess the machinery was built to prevent. Plus the stale vdf changenote. This is 30 minutes of work to fix and would be embarrassing not to.
- **Will bite some users in month one (needs one test to retire):** the Workshop-install `ModData.DirectoryPath` path. If it behaves like the local mods folder, everything is fine; if not, first-run users get a flat starter HUD permanently squatting on the "Stationeers Blue" name (§5 edge case). Untested = unknown, and the failure is sticky.
- **Will happen eventually, handled well (accepted):** a game update breaking 1-3 patch targets. The harness converts this from "mod broken" into "feature X degraded, log says so." The one genuinely painful case is the `ImGuiWindowManager.Draw` postfix — radials and all ImGui UI die together — but the HUD survives and the failure is loud in the log, not a crash.
- **Ugly but harmless:** zip-name/version drift, shipping a stale `HudProfiles/README.md`, the changelog's phantom "Old Themes" folder, `SmartStowPlus`'s out-of-file `OnServer` calls, the never-cleared shadow-guard HashSet.

**Top 5 most likely to break for real users in the first month:**
1. Shipping the stale package (wrong DLL/About.xml) — process error, not code (P0-1).
2. First Workshop publish mishap: `publishedfileid 0` create-flow, forgetting to paste the returned id (duplicate items), stale changenote (P0-2/P0-3).
3. `SyncShipped` inert on Workshop installs → starter doc stranded under the shipped theme name (P0-4/P1-1).
4. Beta-vs-public branch API drift: mod compiled against the beta `Assembly-CSharp`, most users on public — a missing member throws where no harness helps (P1-4).
5. Pre-manifest theme junk on *existing* testers' installs — documented as never auto-pruned (`Features/HudProfileStore.cs:51-54`), so "screwy old themes" reports will continue from anyone who installed before 0.9.2.0; only fresh installs and future retirements are clean.

Is 0.9.2.0 + the uncommitted migration work a stable long-term base? **Yes.** The update-hygiene trio (ConfigMigration version stamp, SyncShipped provenance manifest, the packaging guard) is exactly the infrastructure a Workshop mod needs to survive its own updates, and the patch surface is small (22 classes) with defined degradation for every cluster. It just has to actually be committed, packaged, and exercised once before it ships.

---

## Recommendations

1. **P0 (S) — Commit the WIP, then repackage from the committed tree.** `git add` the four modified/untracked source files + the two reports + the doc, commit, run `tools/package.ps1`, and verify the new zip's inner `About.xml` and DLL timestamps postdate the commit. Delete or archive the stale `StationeersUIMod-0.9.2.zip`.
2. **P0 (S) — Fix `tools/workshop_update.vdf` before first publish:** update the changenote to the 0.9.2.0 line. Better: make `publish-steam.ps1` inject the changenote from `About.xml`/`CHANGELOG.md` at run time so it can never go stale again (10 lines of PowerShell).
3. **P0 (S) — Rehearse the first-publish id flow:** the moment `steamcmd -Force` returns the new `publishedfileid`, paste it into the vdf *before doing anything else*. Write that as step 0 in the release checklist (`Documentation/Config-and-Theme-Migration.md:157`) so a second `-Force` run can never create a duplicate item.
4. **P0 (M) — One end-to-end install test before publish:** `package.ps1 -Install` (local SLP mods folder, dev DLL cleared per the script's own warning) and run the play-test list in `Changes Reports/2026-07-25 - Config migration + shipped-theme sync...md:112-124` — fresh seed, edit-survives, refresh, prune, ConfigVersion stamp, double-F6. This simultaneously retires "migration unplayed" and (mostly) "ModDirectory untested." If possible after the Workshop item exists, subscribe from a second account/machine and confirm `ModDirectory` + seeding on a true Workshop install before announcing.
5. **P1 (S) — Close the starter-strands-shipped-name hole:** in `EnsureActiveDocument` (`UI/Hud/HudSystem.cs:245-252`) or `LoadActive` (`Features/HudProfileStore.cs:416-432`), when the missing profile's name matches a *shipped* profile, either retry seeding directly from `StationeersUIMod.ModDirectory` or skip the `Save()` write-back (keep the starter in memory only) so the next successful `SyncShipped` can still seed the real theme.
6. **P1 (S) — Add a version-agreement check to `package.ps1`:** after reading About.xml's version, scan the built DLL (it already byte-scans for the shim) or `StationeersUIMod.cs` for `ModVersion = "<same>"` and abort on mismatch. Kills the 0.9.2-vs-0.9.2.0 drift class permanently.
7. **P1 (S) — Refresh the shipped `HudProfiles/README.md`** (it ships in the zip) to describe Stationeers Blue + Pure HUD and the sync behaviour, and reword the About.xml "Old Themes folder" changelog line (Workshop users don't get that folder). Remember: this file is read outside the game, so non-ASCII is fine here — the TMP ASCII-only rule applies only to strings the mod displays in-game.
8. **P1 (M) — Verify against the public game branch** (or confirm beta == public for every touched API this cycle): run the packaged mod once on a public-branch install, watch the log for the `Harmony patches applied: N, failed: M` line (`Core/Patches.cs:34`) and for JIT-time member errors.
9. **P2 (S) — Surface degradation to the user:** `PatchHarness.Applied/Failed` already exists (`Core/Patches.cs:15-16`); after a game update, a one-line F10 Control Center notice ("A game update degraded N features — the rest of the mod is unaffected") would convert "mod is broken" bug reports into "feature X is off" reports.
10. **P2 (S) — Housekeeping:** move `SmartStowPlus`'s two `OnServer.MoveToSlot` calls behind thin `ItemActions` wrappers (or annotate CLAUDE.md's rule with the sanctioned exception); clear `Patch_ThingRenderer_OverrideShadowMode._logged` on teardown; delete `t3.ps1` and `debug.log` from the repo root (both git-ignored noise, `t3.ps1` is a UTF-16 scratch script).
11. **P2 (S) — Note the residual pre-manifest junk-theme support cost:** prepare a one-paragraph Workshop FAQ answer ("delete `BepInEx/config/StationeersUIMod/HudProfiles/<old theme>.xml`") since pre-0.9.2.0 installs will never self-clean (documented limitation, `Features/HudProfileStore.cs:51-54`).

## What NOT to do

- **Do not build an auto-updater, version-ping, or any HTTP check.** Steam Workshop is the update channel; the migration machinery already handles the on-disk consequences.
- **Do not rewrite packaging as CI/MSBuild targets.** `package.ps1` + the VS Code tasks are one person's workflow and they now carry the right guards; the remaining failure modes are checklist items, not automation gaps.
- **Do not "harden" every Harmony patch with explicit argument types preemptively.** Only overloaded targets need it (the two that do — `CheckDisplaySlot`, `CommandLine.Process` — already have it). Blanket typing just adds churn on every game update.
- **Do not build a reflection-abstraction layer or a startup API self-test suite.** All 8 reflection sites are individually guarded with defined degradation; a framework would be more code than the sites themselves.
- **Do not implement retroactive pruning of pre-manifest junk themes.** It was designed and deliberately deferred (memory: hash-manifest prune, "later" 2026-07-24) because the safety proof ("we can't prove they're pristine") is correct. The FAQ answer is cheaper than the risk of deleting a player's edited layout.
- **Do not strip the embedded PDB from the Release DLL to save size.** 1.29 MB is nothing, and line numbers in user-submitted stack traces are worth far more than the bytes.
- **Do not rename `uia_effects.bundle` to `.assets`** to "let SLP load it" — the current name is a deliberate defence against SLP's fail-fatal auto-load (`Core/HudShaderStore.cs:28-29`).
