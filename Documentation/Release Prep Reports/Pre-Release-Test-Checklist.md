# Pre-Release Test Checklist — Stationeers UI Ascended

A single runnable pass to go through **before every push**, top to bottom. Written for the
0.9.2.5 build (config schema v3 / HUD document schema 15) but keep using it for later releases
— bump the version numbers referenced below as they move.

Check every box. If a step's actual result does not match its **Expected**, stop and fix (or, if
it's a known/accepted gap, note it and tell FlorpyDorp before pushing) — don't just note it and
carry on.

## 0. Paths cheat-sheet (you'll need these constantly below)

- **Game install:** `C:\Program Files (x86)\Steam\steamapps\common\Stationeers`
- **Config root:** `<install>\BepInEx\config\`
  - `com.stationeersuimod.ui.cfg` — the settings file for a **plugins/SLP** install.
  - `com.stationeersuimod.ui.scriptengine.cfg` — the settings file for the **F6 dev/ScriptEngine**
    route. Different filename = a dev session and a "real" install never share settings.
  - `StationeersUIMod\` subfolder — everything else that isn't a single scalar setting:
    `HudProfiles\*.xml` (+ `.shipped-manifest`, + `<name>.png` previews), `Grid\`, `GridPins\`,
    `Assignments\`, `BeltBindings\`, `Hotkeys\`, `HintUsage\`, `Loadouts\`, `Profiles\profiles.xml`,
    `HudIcons\`, `ProfilerSnapshots\`. **A fresh-install wipe must delete both the `.cfg` AND this
    whole folder** — deleting only the `.cfg` leaves stale profiles/bindings behind, which is the
    single most common false "fresh install" a tester can accidentally run.
- **Deploy routes — never have two live at once** (see CLAUDE.md): the Debug build's
  `BepInEx\scripts\` auto-deploy (F6 hot-reload), a `BepInEx\plugins\` copy, or the
  StationeersLaunchPad/Workshop mod folder. Clear the others before testing a given route.
- **Build:** `dotnet build "Dev/StationeersUIMod.Dev.csproj" -c Debug` — must finish 0 warnings,
  0 errors before you test anything built from it.
- **Package:** `tools\package.ps1` (defaults to `-c Release`) — builds Release, stages
  `dist\StationeersUIMod\`, zips `StationeersUIMod-<version>.zip` at the repo root.
- **Publish:** `tools\publish-steam.ps1` — uploads `dist\StationeersUIMod\` (what `package.ps1`
  just staged) via steamcmd, per `tools\workshop_update.vdf`.

---

## A. Fresh install

Seeds both shipped themes + the manifest, defaults to Stationeers Blue, stamps config schema v3,
no migration log noise.

1. Quit the game. Delete `BepInEx\config\com.stationeersuimod.ui*.cfg` (both cfg names if you've
   ever run the F6 route) **and** the entire `BepInEx\config\StationeersUIMod\` folder.
2. Launch, load into a save (or the main menu is enough for `OnLoaded` to run — check the
   BepInEx log for the mod's load line either way).
3. **Expected:** `BepInEx\config\StationeersUIMod\HudProfiles\` now contains `Stationeers
   Blue.xml`, `Pure HUD.xml`, and `.shipped-manifest` with exactly those two `name|hash` lines
   (plus each theme's `<name>.png` preview, once FlorpyDorp has dropped them into the repo's
   `HudProfiles\` — see §H step 3 if that's not true yet).
4. **Expected:** the HUD renders the **Stationeers Blue** look; F9 → Profiles combo shows
   "Stationeers Blue" as the active selection.
5. **Expected:** `com.stationeersuimod.ui.cfg` → `[0. Internal]` → `ConfigVersion = 3` (or
   whatever `ConfigMigration.CurrentVersion` currently is — check the constant if this drifts).
6. **Expected:** the BepInEx log has **no** "Config migrated from vN to vM" line (a fresh install
   is stamped straight to current and runs zero migration steps).
7. **Expected:** no `ConfigMigration` warning/error lines in the log.

## B. Upgrade simulation (pre-0.9.2.5 cfg with legacy keys)

Confirms `ConfigMigration` strips dead keys, carries glitch values across the rename, and the
theme-fold top-up leaves the look unchanged.

1. Quit the game. With a fresh `.cfg` as your starting point (run §A once if you haven't), hand-edit
   `com.stationeersuimod.ui.cfg` in a text editor:
   - Change (or delete) `[0. Internal] ConfigVersion` to `0`.
   - Add back a representative sample of the keys Wave B deleted (full ledger:
     `Documentation/Release Prep Reports/wave-b-removed-keys.md`), e.g.:
     ```
     [7. HUD]
     Enabled = true
     ShowTopBar = false

     [10. Visor HUD]
     LegacyImGuiHud = true
     UseDocumentHud = false
     GlitchEnabled = true
     GlitchIntensity = 0.5
     GlitchDuration = 0.8
     TopBarHeight = 80

     [8. Radial Visuals]
     UseUnityRadial = false
     IconScale = 1.25

     [9. The Grid]
     DisplayMode = List
     ```
   - Leave `RadialPalette` → `TextAccent` **unbound/absent** (its fresh default is white — that's
     what exercises the v2→v3 `ForceIfDefault` fix).
2. Launch.
3. **Expected:** BepInEx log shows `Config migrated from v0 to v3.`
4. **Expected:** reopen the `.cfg` — every key you added above is **gone** (not blank; the key
   lines themselves absent), `ConfigVersion = 3`.
5. **Expected:** `HudConfig.FxGlitchOn` = true, `FxGlitchAmt` = 0.5, `FxGlitchDuration` = 0.8 (the
   old `Glitch*` values carried onto their tri-state-FX replacements — check via F9 → Effects →
   Alerts/Advanced, wherever the glitch sliders live, or read the values straight from the cfg's
   `11. HUD Effects (0.9.0)` section).
6. **Expected:** `RadialPalette` → `TextAccent` = `FF8C29FF` (the legacy orange), not white.
7. **Expected:** the HUD/radial look is visually **unchanged** from how it looked before this
   migration ran (aside from the accent-colour fix in step 6) — the v2→v3 step only arms
   `PendingThemeTopUp`; `HudProfileStore.LoadActive`'s first call after that consumes it and
   stamps the player's then-current globals into every profile that already carries a theme
   snapshot, so nothing visibly jumps.
8. Quit and relaunch a second time. **Expected:** no repeated "Config migrated" log line;
   `ConfigVersion` stays `3`.

## C. Per-tier repro (hand-box colours fork bare/suited and round-trip)

Confirms the opt-in `HudStyleSlot` fork (Schema 15) actually isolates a tier's look and survives
a save/reload — this is the exact class of bug ("speed shows in bare") the per-tier model exists
to prevent from recurring.

1. F9 → click the **Hand boxes** element to open its popup. At the top, under the per-tier style
   block, tick **"Separate BARE style"**.
2. With the mode tabs now showing **SUITED / BARE**, stay on **SUITED** and set the hand-box
   border or fill colour to something obvious (e.g. bright red).
3. Click the **BARE** mode tab (this sets `HudSystem.ForceTier = Bare` for live preview — no need
   to actually strip your suit). Set the hand-box colour to something else obvious (e.g. bright
   green).
4. Click back to **SUITED** — **expected:** hand boxes show red again, not green.
5. Click **BARE** again — **expected:** green, not red. (Toggling between the two tabs must never
   bleed one tier's edit into the other.)
6. Wait for the autosave (~1.5s of no edits), then either switch to another profile and back, or
   fully quit and relaunch. **Expected:** both colours are still correct per tier — the fork
   round-trips through disk, not just live memory.
7. Alternative/independent check with the editor closed: F9 → Debug → tick **"Show everything in
   power-off / bare layout"** (`HudConfig.DebugShowAllBare`). **Expected:** hand boxes render
   green (the bare fork) with the editor shut. Untick it — **expected:** back to red.
8. Reopen the Hand boxes popup and **untick** "Separate BARE style". **Expected:** bare now
   follows the suited (base) colour again — red in both tabs; the fork is cleanly disabled, not
   just hidden.

## D. Profile CRUD paths A–F

Verbatim from the Wave E change report
(`Changes Reports/2026-07-25 - Wave E profile CRUD + shipped-theme lifecycle.md` §8) — repeated
here so the whole release pass lives in one document. Do all six.

**D1. Create → edit → rename → duplicate → delete (F9)**
1. F9 → Profiles → **New**. Accept the suggested name, **Create**. Expected: a profile with
   nothing but the two hand boxes, sized to your screen, and the look you already had (no theme
   snapshot yet).
2. Move/resize something, change a colour. Confirm the autosave lands (~1.5s of quiet).
3. **Rename** it. Expected: the HUD does not flicker or reload, the combo shows the new name, and
   **Ctrl+Z still undoes** — history must survive a rename.
4. **Duplicate** it. Expected: unsaved edits travel into the copy, and you switch to the copy.
5. **Delete** the original (not active, so selectable in the victim combo). Expected: the
   **Delete** button is **dimmed** when only one profile exists.
6. Reopen F9. Expected: no stale "are you sure" strip is armed.

**D2. Edit a shipped theme → restore it**
1. Switch to **Stationeers Blue**, change something obvious (a panel colour), let it autosave.
2. F9 → **Restore shipped version** → **Restore permanently**. Expected: the HUD snaps back to
   the shipped look immediately, no restart needed.
3. Relaunch. Expected: still the shipped look — the restore recorded pristine, and sync did not
   treat the restored copy as an edit.
4. Same flow from F10 → Profiles → advanced → **Manage a profile** → **Restore shipped**.
   Expected: a toast confirms it.

**D3. Delete a shipped file on disk**
1. Quit. Delete `BepInEx\config\StationeersUIMod\HudProfiles\Pure HUD.xml`.
2. Relaunch. Expected: it comes back **and is managed** — `.shipped-manifest` has a
   `Pure HUD.xml|<hash>` line.
3. Repeat with **Stationeers Blue while it is the active profile** (the self-heal/embedded-factory
   branch, not the seed branch). Expected: it comes back looking like the real Stationeers Blue,
   not a generic starter layout, and is in the manifest.

**D4. `uiareset`**
1. Console (F3) → `uiareset`. Expected: it **prints** the three paths it would delete and deletes
   nothing — confirm the folder is still there afterward.
2. `uiareset confirm`. Expected: wipes, tells you to restart. Relaunch → clean, fully seeded
   config (this doubles as another §A pass).
3. In a **fresh session**, type `uiareset confirm` first, with no bare `uiareset` before it in
   that session. Expected: it **refuses**.

**D5. Dev-only export** *(repo dev flow only — skip on a packaged/Workshop install)*
1. F9's export-to-shipped writes into `C:\Dev\Stationeers UI Ascended\HudProfiles\`. Expected: on
   a Workshop/SLP install this resolves to nothing and does nothing.
2. After exporting a change to either curated theme, **re-embed by hand** in
   `Assets/Scripts/StationeersUIMod/UI/Hud/ShippedProfiles.cs` — there is no build step that does
   this for you, and skipping it means the self-heal path quietly restores a stale design while
   the manifest still thinks it's current.

**D6. Narrow-window regression**
1. Drag the F9 window narrow. Expected: the four CRUD buttons (New/Duplicate/Rename/Delete)
   **stack**, never run off the edge.

## E. Theme travel

A profile switch must retint radials, the Universal Inventory (Grid), and the F10 Control Center
menu together — and must never touch performance knobs.

1. Switch from **Stationeers Blue** to **Pure HUD** (F9 or F10 combo). Expected: the visor HUD,
   **radial wedges**, **Grid** window skin, and the **F10 menu** skin all retint together, in the
   same frame/transition — not just the HUD panels.
2. Before switching, note your current `FrostDownsample` / `FxBloomRes` / `FxBloomBlurSteps` /
   `FxBloomFineDetail` / `FrostUpdateEveryN` values (F9 → Effects → Glow/Bloom/Advanced, or read
   them straight from the cfg). Switch themes. Expected: those five stay exactly as they were —
   `HudTheme.Exclude` keeps every pure performance/resolution knob out of the snapshot, on
   purpose, so importing someone else's theme can never tank your frame rate.
3. Switch back to **Stationeers Blue**. Expected: full look reverts, including radial/Grid/menu.

## F. F9 restructure spot-checks

Confirms the sub-tab reorganisation didn't drop or orphan a control.

1. F9 → **Theme** tab → confirm all three sub-tabs exist and are populated: **Palette**, **All
   colours**, **Typography & boxes**.
2. F9 → **Effects** tab → confirm all seven sub-tabs exist and are populated: **Glass**, **Edges**,
   **Glow**, **Bloom**, **Alerts**, **Transitions**, **Advanced**.
3. Spot-check that every knob you remember from the pre-restructure F9 (curvature, glass tint,
   glow width, bloom threshold, alert pulse, transition fade, the four dead typography sliders
   gated behind "advanced", etc.) is findable somewhere in the above — nothing should require
   scrolling to a section that no longer exists.
4. Open an element's popup and confirm the per-tier **STYLE block** (the "Separate BARE style"
   checkbox + SUITED/BARE mode tabs, exercised fully in §C) is present at the top for a
   both-tier element (e.g. Hand boxes) and **absent** for a single-tier element (e.g. anything
   `Tiers="Bare"`-only).

## G. Double-F6 hot reload

1. With the game running and the F6 dev/ScriptEngine route active, open F9 and F10, have a radial
   open at least once, switch profiles once.
2. Press **F6** to reload. Expected: clean reload, HUD/radials/menus all functional, no console
   spam.
3. Press **F6** a **second** time immediately. Expected: still clean — no stale canvases, no
   duplicated borrowed vanilla objects (moodlet strip / portrait / damage doll), no leaked
   materials/render textures, no repeated "Config migrated" line, `ConfigMigration.PendingThemeTopUp`
   does not re-fire visibly, `HudEditorWindow`'s CRUD statics (`_profileAction`,
   `_shippedNameCache`, etc.) don't carry a stale armed flow into the reloaded window.
4. Check the BepInEx log for any NRE or "dereferencing a destroyed" error across both reloads —
   in particular around `StatusUpdates.ManagerUpdate` (the borrowed-object hand-back rule).

## H. Packaging

1. Ensure the working tree is **clean and committed** (`git status` — nothing pending). Packaging
   from a dirty tree ships whatever happens to be on disk, not what's in history.
2. Build and stage: `pwsh tools\package.ps1` (defaults to Release). Expected: it completes without
   throwing the `ScriptEngineLoader` dev-shim guard, and without the "About\Preview.png missing" /
   "About\thumb.png missing" / "uia_effects.bundle NOT found" warnings (any of those firing is
   itself a finding — chase it down before shipping).
3. Open the produced `StationeersUIMod-<version>.zip` and check its contents against the manifest
   in `tools\package.ps1`'s own header comment:
   - `StationeersUIMod\StationeersUIMod.dll`
   - `StationeersUIMod\uia_effects.bundle`
   - `StationeersUIMod\About\About.xml`, `Preview.png`, `thumb.png`
   - `StationeersUIMod\HudProfiles\Stationeers Blue.xml`, `Pure HUD.xml`, `README.md`
   - **`.png` preview files for each theme** (`Stationeers Blue.png`, `Pure HUD.png`) — **known
     gap as of this writing:** `tools\package.ps1`'s HudProfiles copy step only copies `*.xml` +
     `README.md`; it does **not** copy `*.png`. If FlorpyDorp's two screenshots are in the repo's
     `HudProfiles\` folder and this step still shows no PNGs in the zip, that's confirmation of
     the gap, not a fresh bug — flag it and get `package.ps1` fixed (add a `*.png` copy alongside
     the `*.xml` one) before relying on preview art shipping to players.
   - **Absent:** `StationeersUIMod.pdb` (the csproj uses `<DebugType>embedded</DebugType>`, so
     there should be no separate pdb to accidentally ship), `ScriptEngineLoader` anywhere, any
     `Dev\` path, any `.git` metadata.
4. Confirm version agreement across all of: `Assets\About\About.xml` `<Version>`,
   `StationeersUIMod.ModVersion` + `VersionDisplay`
   (`Assets/Scripts/StationeersUIMod/StationeersUIMod.cs`), `CHANGELOG.md`'s latest entry, and the
   zip's own filename. Also confirm `<ChangeLog>` in `About.xml` is under 8000 characters (SLP
   hard-rejects the manifest over that).

## I. Steam end-to-end

FlorpyDorp's own flow — the one test nobody else can run for him, so it's listed last but it's
not optional for a real release.

1. `tools\publish-steam.ps1` (after a fresh §H package) to push the build to the Workshop item in
   `tools\workshop_update.vdf`.
2. On a **clean machine or account** (not the dev box's existing install), subscribe to the
   Workshop item via Steam, launch through StationeersLaunchPad.
3. **Expected:** `StationeersUIMod.ModDirectory` resolves to the Workshop-subscribed folder (not
   null) and `HudProfileStore.SyncShipped` seeds `Stationeers Blue.xml` + `Pure HUD.xml` +
   `.shipped-manifest` on first load — this is functionally another §A fresh-install pass, but
   through the REAL Steam/SLP path instead of a local plugins/scripts copy, which is the one
   thing local testing can't fully substitute for.
4. Make a small, deliberate change (e.g. bump the version, tweak a shipped theme's colour) and
   push a second Workshop update.
5. On the same clean install, let Steam pull the update (or force a re-subscribe), relaunch.
   **Expected:** `SyncShipped`'s **refresh** fires for the changed theme (if this test account
   never edited it) and any theme deliberately retired between the two pushes **prunes**
   correctly, while an edit the test account made in between **survives** untouched. This is the
   one thing that proves the whole hash-manifest/canonical-hashing design (see
   `Documentation/Config-and-Theme-Migration.md` §3) actually works against a real Steam update,
   not just a hand-simulated one.
6. Confirm the About.xml description/changelog render correctly in the Workshop page and in
   StationeersLaunchPad's own mod list — this is the first time non-repo eyes see that text.
