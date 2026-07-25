# 06 — Theme Shipping & Config Migration Foundation

> Release Prep Report — 2026-07-25

**Concern (FlorpyDorp):** We just built the config-versioning + shipped-theme sync so Steam updates can add/remove/fix shipped profiles and settings without breaking what players made. Is this foundation actually right — and totally safe — before the first public release? What settings are we shipping?

**Verdict:** NEEDS WORK BEFORE RELEASE — the never-destroy invariant genuinely holds on every path I could construct, but the fresh-install detection is dead code on the real SLP load path, a hash-drift edge permanently freezes theme management, and a missing self-heal factory can strand a broken "Stationeers Blue" forever; all four fixes are small.

---

## Findings

### 1. Re-verification of the three claimed fixes — all present and correct

The Changes Report ("2026-07-25 - Config migration + shipped-theme sync", §Adversarial verification) claims three defects were found and fixed. I re-verified each against the code:

| Claimed fix | Present? | Evidence |
|---|---|---|
| (1) Canonical-content hashing (deserialize → Sanitize → reserialize), not raw bytes | **Yes** | `CanonHash` at `Assets/Scripts/StationeersUIMod/Features/HudProfileStore.cs:151-168` — parses, `Sanitize()`s, normalizes `Name` to the filename, reserializes to a MemoryStream, hashes that. `Load`'s `RepairedOnLoad` write-back (`HudProfileStore.cs:293-297`) is therefore invisible to the sync. |
| (2) ConfigVersion never clamps DOWN | **Yes** | `Core/ConfigMigration.cs:49-54` — `if (from >= CurrentVersion) return;` with a correct comment explaining why a downgrade must leave the higher stamp. |
| (3) FNV-1a instead of MD5 (FIPS) | **Yes** | `HudProfileStore.cs:170-177` — hand-rolled FNV-1a 64, no `System.Security.Cryptography` anywhere in the file. |

**However, the documentation contradicts fixes (2) and (3) in three places** — a future maintainer reading the doc will "learn" the pre-fix behaviour:

- `Documentation/Config-and-Theme-Migration.md:89` — "A hand-edited `ConfigVersion` higher than `CurrentVersion` is **clamped back**" — false; the code deliberately never lowers the stamp (`ConfigMigration.cs:49-54`).
- `Documentation/Config-and-Theme-Migration.md:99` — manifest format described as `<filename>|<md5>` — it is FNV-1a.
- The Changes Report itself still says "a value above current is clamped" (line 42) and lists `System.Security.Cryptography.MD5` as a new dependency (line 67), contradicting its own fix log at lines 101-107.

### 2. The update-scenario matrix, walked branch by branch

The invariant under test: **never destroy or overwrite anything the player made or changed.** I walked `SyncShipped` (`HudProfileStore.cs:55-143`) and `ConfigMigration.Run` (`ConfigMigration.cs:36-68`) against each scenario:

| # | Scenario | Outcome | Invariant |
|---|---|---|---|
| a | Fresh install | Both themes seeded + manifest recorded (`:81-86`); active profile default "Stationeers Blue" (`UI/Hud/HudConfig.cs:317`). But see **Finding 3** — the *config* fresh-install skip misfires. | Holds |
| b | Updater, pristine shipped themes | `known && diskHash==prev` → refresh to the new version, manifest re-stamped (`:96-102`). Canonical hashing makes the Load rewrite-on-open invisible. | Holds |
| c | Updater who EDITED a shipped theme | Hash diverges → `known && edited` branch: file untouched, manifest entry kept as-is (`:107`). Never refreshed, never pruned. | Holds |
| d | Player's CUSTOM theme has the filename of a NEWLY shipped theme | `dst` exists, `!known`, `diskHash != shipHash` → left alone, never managed (`:96, :104-106`). The player's file is safe — but **the new shipped theme is silently never delivered** (no log line, no rename fallback). | Holds (silent gap) |
| e | Retired theme — pristine / edited / active | Pristine: deleted; if active, `HudActiveProfile` falls back to its DefaultValue (`:111-126`). Edited: kept, manifest entry dropped — it's theirs now (`:127-129`). Unparseable: kept (`:118`). | Holds |
| f | Mod downgrade then re-upgrade | Themes: a pristine theme tracks the installed version each way (refresh runs in both directions off `diskHash==prev`) — correct. Config: the stamp never lowers (`ConfigMigration.cs:49-54`), so re-upgrade re-runs nothing. | Holds |
| g | Player deletes `.shipped-manifest` by hand | Next sync rebuilds conservatively: pristine current themes re-adopt via `diskHash==shipHash` (`:96`), everything else left alone (`:104-106`). Pending retirements are forgotten (never pruned) — fail-safe. | Holds |
| h | Two mod versions alternating over one config dir (game beta/main branch) | Pristine themes flip-flop refresh each launch (harmless churn: one file copy + manifest write). Config stamp sticks at the higher version. Edited/custom themes untouched. | Holds |
| i | Sanitize output changes between mod versions (canonical hash drift) | **Fails safe but freezes permanently — see Finding 4.** | Holds, but management dies |
| j | F6 dev flow (`modDirectory` null) | Inert return at `:59`; `ConfigMigration` still runs (fine — dev cfg exists). `SyncShipped` holds no statics, so double-F6 is clean; `ConfigMigration.Version` is a passive ConfigEntry ref (no event hooks) — dead-assembly safe though technically never reset in a Shutdown. | Holds |

Additional mutating-path checks that came back clean: the refresh's `File.Copy(...,true)` only ever fires on `untouched==true`, and `manifest[name]` only ever stores a *shipped* hash — there is no code path that records a post-edit hash and later uses it to justify an overwrite. A player edit that canonically reverts a theme to exactly the previously-shipped content is indistinguishable from pristine and gets refreshed — semantically lossless (hand-added XML comments/formatting would be lost; canonical equality is content equality).

**Conclusion: the core invariant holds.** Nothing in the tree destroys or overwrites player-made or player-changed content. The real problems are on the *other* side — paths where the system stops working while failing safe, plus one entry-condition bug.

### 3. NEW DEFECT — fresh-install detection is dead on the real SLP path (P0)

`StationeersUIMod.cs:107-115` computes:

```csharp
freshInstall = !File.Exists(cfgPath) && !File.Exists(legacyPath);
```

But SLP **creates the ConfigFile with `saveOnInit: true` before calling OnLoaded** — `Reference/StationeersLaunchPad-master/ModLoader.cs:94`: `return Config = new ConfigFile(path, true);`. BepInEx's constructor writes the (empty) file to disk immediately when it doesn't exist. So by the time our check runs, `cfgPath` **always exists** — `freshInstall` is always `false` on every real Steam install.

The codebase already knows about this: the legacy dev-shim copy directly below (`StationeersUIMod.cs:128`) tolerates the SLP-pre-created file with `|| new FileInfo(newCfg).Length == 0`. The freshInstall check forgot the same clause.

Consequences:

- **Today (CurrentVersion=1, zero steps):** every genuinely fresh install runs the no-op step 0→1 and logs "Config migrated from v0 to v1" — which means the Changes Report's own play-test checks #1 and #5 ("no migration log noise on fresh install") will FAIL, and someone may "fix" the wrong thing.
- **Later (the real danger):** the entire design premise "fresh installs are stamped to Current and run NO steps" (`ConfigMigration.cs:24-26, 32-35`) is dead in production. A fresh install of a future v2 will run steps 0→2 on brand-new defaults. `ForceIfDefault` steps happen to be no-ops on fresh configs, and rename steps merely fossilize the old key into new installs — but the first *value-transforming* step (the exact class of step the comments warn about) would corrupt fresh installs. The safety mechanism you think you have does not exist.

Fix is two lines: treat an existing-but-empty cfg as absent, mirroring line 128's clause.

### 4. NEW DEFECT — hash drift permanently freezes management; re-adoption is blocked by `known` (P0)

`HudProfileStore.cs:96`:

```csharp
bool untouched = known ? diskHash == prev : diskHash == shipHash;
```

The pristine re-adoption test (`diskHash == shipHash`) only runs when the file has **no** manifest entry. Once a file is `known`, only `diskHash == prev` counts. Now walk scenario (i): a new mod version whose `Sanitize` canonicalizes differently (a new repair pass, a new param strip — this has happened four times already: the style migration, the transition tri-state, the Speed-tier strip, the bare-orphan strip, all in `UI/Hud/HudDocument.cs:154-255`). On first launch after that update:

- `prev` = hash computed by the OLD Sanitize.
- `diskHash` and `shipHash` = hashes computed by the NEW Sanitize — equal to each other for a pristine copy, but ≠ `prev`.
- Result: `untouched=false` → the theme reads as player-edited → **never refreshed, never pruned, and never re-adopted** (the `diskHash == shipHash` proof of pristineness is sitting right there but the `known` branch never consults it). The manifest keeps the stale hash forever.

This fails safe (nothing deleted) but silently turns the whole feature off for exactly the files it manages — the same failure class as the raw-bytes bug fix #1 addressed, one level up. The fix is one line:

```csharp
bool untouched = diskHash == shipHash || (known && diskHash == prev);
```

`diskHash == shipHash` is proof of pristineness regardless of manifest state (the disk content is canonically identical to what we currently ship), and re-stamping the manifest there self-heals the drift. When Sanitize AND the theme's content change in the same update, the pristine copy is still indistinguishable from an edit and freezes — unavoidable under any hashing scheme, and it fails safe.

### 5. Sanitize determinism — currently safe, one authoring landmine

`CanonHash`'s doc-comment claims "Sanitize is self-contained (no runtime singletons)" (`HudProfileStore.cs:149-150`). That is **not true in general**: `HudStyleMigration`'s Custom-snapshot path (`UI/Hud/HudStyleMigration.cs:126`) calls `HudElementView.SetUnifiedStyleSourceWithoutView`, which reads a dozen live `HudConfig` globals (`UI/Hud/HudElementView.cs:1567-1621` — BorderWidth, CornerRadius, EdgeFeather, GlassSheen, FxTierA, FxEdgeLight…). A profile that triggers that path canonicalizes differently depending on the player's current settings — the canonical hash becomes per-player, per-session.

**Why it doesn't bite today:** I checked both shipped XMLs — every element in `HudProfiles/Stationeers Blue.xml` (24 elements, styleSource 16×"1"/8×"2") and `HudProfiles/Pure HUD.xml` (20 elements, 13×"1"/7×"2") already carries a coherent `styleSource`, so only the deterministic residue-clean branch runs (`HudStyleMigration.cs:94-107`). The legacy transition bools they do carry (`fxCollapse`, `fxGlitch`…) migrate through `HudTransitionFx.MigrateElement` (`UI/Hud/HudTransitionFx.cs:272-312`), which reads no globals — deterministic. One more nondeterminism source exists: Sanitize **mints a random GUID** for a missing/duplicate element Id (`HudDocument.cs:168-172`) — a shipped theme with a duplicate Id would hash differently on every call and freeze its own management from day one.

So: safe now, contingent on authoring discipline forever. Every future shipped theme must (a) come out of F9 (which always writes styleSource and unique Ids) and (b) ideally be verified by a trivial self-check: `CanonHash(f) == CanonHash(f)` twice, at sync time or in a dev command. With the Finding-4 fix in place, even a drift event self-heals on the next content-matching update, which downgrades this whole class from "permanent" to "transient".

### 6. NEW DEFECT — the self-heal factory writes starter junk under the shipped default's name (P1)

`HudSystem.EnsureActiveDocument` (`UI/Hud/HudSystem.cs:245-252`) matches self-heal factories by name: "Glassy 4.0" / "Glassy 2.0" / "Glassy" get their embedded builders; **everything else — including "Stationeers Blue" and "Pure HUD" — falls to `BuildStarterDocument`**, the flat schema-6 primitive demo. `LoadActive` then *persists* that starter doc to disk under the missing profile's name (`HudProfileStore.cs:416-432`).

Failure chain: mod-folder `HudProfiles/` missing or empty on first run (a packaging slip — the stale-`dist/` problem has shipped wrong content before, per `MEMORY.md`; or a partial Workshop download; or the F6 dev flow where SyncShipped is inert) → `SyncShipped` seeds nothing → HUD builds → "Stationeers Blue" not found → **a flat starter layout is written to disk as `Stationeers Blue.xml`**. From then on SyncShipped sees an unknown file whose hash matches nothing (`!known`, `diskHash != shipHash`) and — correctly, per the invariant — **never touches it again**. The player is permanently stuck with a junk HUD wearing the shipped default's name, and the next (correct) update cannot repair it. Note `dist/` is currently in sync with repo-root `HudProfiles/` (verified byte-identical today), so this is a latent chain, not a live one.

The same architecture already solves this for Glassy 4.0: the theme is embedded verbatim in code (`UI/Hud/Glassy40Default.cs`) and used as its name-matched factory. Stationeers Blue and Pure HUD deserve the same treatment (~30 KB of embedded XML each), which also fixes the F6-clean-config dev case.

Related edge: the prune fallback writes `HudActiveProfile = DefaultValue` (`HudProfileStore.cs:123-124`). If a future update ever retires/renames "Stationeers Blue" itself without changing the config default (`HudConfig.cs:317`), the fallback points at a nonexistent theme and the same starter-junk path fires. Add to the release checklist: the shipped-default name, the `HudActiveProfile` default, and the mod-folder file must move together.

### 7. The two-system question: schema-gated auto-upgrade vs SyncShipped

The old mechanism still exists at `HudSystem.cs:255-274`: when the *active* profile is literally named "Default" with `Schema < 6`, or "Glassy 2.0" with `Schema < 12`, it is replaced in-place by the embedded factory build. Assessment:

- **No active collision.** It fires only for two legacy names that are no longer shipped (the shipped set is Stationeers Blue + Pure HUD, `HudSystem.cs:277-284`), so SyncShipped and the schema gate can never fight over the same file today.
- **But shipped-name knowledge now lives in three places:** the factory-name matcher (`HudSystem.cs:246-252`), ProfilesTab's hard-coded `Featured` array (`UI/Menu/Tabs/ProfilesTab.cs:22-23`), and the mod folder's actual contents (the real owner). And the shipped XMLs themselves are stamped `Schema="7"` (both files, line 2) while the current schema is 14 (`Glassy40Default.cs:14`) — meaningless today because no gate matches their names, but exactly the kind of dormant inconsistency that resurrects as a bug when someone later adds a schema gate for a shipped name.
- **Single-owner resolution:** the mod folder (mirrored by the manifest) should be the sole authority on "what is shipped". Concretely: freeze the schema-gate (comment it as legacy-repair-only, never extend it to new names); derive ProfilesTab's featured list from the manifest/mod-folder scan instead of a hard-coded array; stamp shipped themes with the current schema when exporting. The schema-gate itself should NOT be deleted for release — it is the only repair path for pre-manifest players still running "Default"/"Glassy 2.0".

### 8. What settings ship — the first-run surface audit

The first-run defaults read as a deliberate, coherent experience — this part is in good shape:

- Both halves on: `MasterEnable`/`RadialEnabled` (`UIAConfig.cs:311-318`), `VisorHudEnabled`+`UseDocumentHud` (`HudConfig.cs:310-316`), active profile **Stationeers Blue** (`HudConfig.cs:317-320`) — matches the curated 2026-07-24 shipped set.
- Effects: Tier A ON, Tier B ON (auto-degrades without the bundle), Tier C frost OFF-experimental (`HudConfig.cs:508-516`) — per FlorpyDorp's recorded 2026-07-13 decision. Bloom OFF (`:781`), global Glow OFF (`:586-588`), screen-glitch OFF (`:489`), TV-off and death-collapse OFF (`:713, :729`) — the flourishes are opt-in. Curvature VertexWarp at 0.25 (`:345-353`).
- Diegetics: `DiegeticTiers` ON, flicker ON, low-power dropouts ON (`:476-487`) — the bare/suited tier experience is the shipped default. `HardcoreGating` OFF (`UIAConfig.cs:623`).
- First-run guide: `GuideShown=false` auto-opens once (`UIAConfig.cs:348, StationeersUIMod.cs:362-367`). Debug toggles all off (`HudConfig.cs:329-335`, `LegacyImGuiHud` off `:336`, `HintBarPreview` off `UIAConfig.cs:740`).
- Key collisions (F9 vs creative spawn, B vs InstantStop) are disclosed in the descriptions (`HudConfig.cs:338-343`, `UIAConfig.cs:489-492`). Fine.

**Dead/misleading entries that will fossilize into every player's cfg at first run:**

1. `GridMode` — bound, self-described "DEPRECATED - no longer used" (`UIAConfig.cs:496-500`). Delete the bind; the code already refuses to read it (`StationeersUIMod.cs:810-816`, `UI/Grid/GridModel.cs:90`).
2. `RadialIconScale` — "LEGACY (unused since 0.3.1)" (`UIAConfig.cs:636-638`), no readers. Delete the bind.
3. **The whole "7. HUD" section is a trap.** Nine of its thirteen entries (`HudEnabled`, `HudHandBoxes`, `HudStatusStrip`, `HudVitals`, `HudClock`, `HudContextPanel`, `HudVisorArcs`, `Scale`, `HardcoreGating`, `UIAConfig.cs:594-624`) are read ONLY by the legacy ImGui HUD (`Features/HudOverlayFeature.cs:30-47`), which draws only when `LegacyImGuiHud=true`. In the SLP config panel a player sees "7. HUD > Enabled" and "10. Visor HUD > VisorHudEnabled" side by side; toggling the first does nothing to the HUD they're looking at. The four `HideVanilla*` entries in the same section ARE live (`HudSystem.cs:2258-2314`) — so the section can't just be deleted wholesale. Minimum fix: prefix the nine legacy descriptions with "(LEGACY ImGui HUD only — see 10. Visor HUD)".
4. `ConfigVersion` sits in section "0. Internal" (`ConfigMigration.cs:28`) — it will render at the TOP of the SLP config panel, labelled "Do NOT edit". Fine, but expect a support question or two; the never-clamp-down + never-rerun design makes hand-editing it mostly harmless (a lowered stamp re-runs idempotent steps once; `ForceIfDefault` steps are safe to re-run).
5. `GlitchShader` was already retired correctly (bound by nothing, documented at `HudConfig.cs:110-113`) — the orphaned value in old cfgs is harmless. This is the right pattern for retiring keys.

### 9. Pre-manifest junk — the documented gap is real and now has a visible face

The limitation is honestly documented (`HudProfileStore.cs:51-54`, doc §3): themes from before the manifest ("Default", "Glassy", "Glassy 2.0", "Glassy 4.0", any archived experiment) have no provenance record and are never pruned. Two things make this worse than "harmless clutter":

- **The junk gets top billing.** `ProfilesTab.PickFeatured` (`ProfilesTab.cs:89-99`) fills the front-door card grid up to SIX cards, falling through alphabetically past the two curated ones — an updating playtester's "Default", "Glassy", "Glassy 2.0", "Glassy 4.0" all become featured cards next to Stationeers Blue and Pure HUD. The curated front door is only curated on fresh installs.
- **The playtester confusion already happened:** deleted cfg + surviving `HudProfiles/` folder → stale themes kept reappearing (the recorded "screwy" incident). The doc's §4 answer is "manually delete folders" — a support-thread answer, not a product answer.

There is also a safe, code-cheap sweep available that the doc doesn't mention: the embedded factories (`BuildStarterDocument`, `BuildGlassyDocument`, `BuildGlassy2Document`, `BuildGlassy40Document`, `HudSystem.cs:527+`) can regenerate what those old shipped defaults contained. `CanonHash(on-disk "Glassy 2.0") == CanonHash(serialize(BuildGlassy2Document()))` is the same pristine-proof the manifest provides — provably-untouched legacy junk could be swept once, edited copies kept. Factory output has drifted across versions, so this catches some copies and safely misses others.

### 10. Foundation verdict — hash-manifest vs fork-on-write vs in-XML markers

Honest comparison of the three architectures:

- **In-XML origin marker** (an `Origin="shipped:0.9.2"` attribute): rejected in the Changes Report and I agree — the marker travels with the file, so any editor/save path must maintain it, hand-edits can lie about it, and it changes the schema for every consumer. Strictly worse than the manifest.
- **Fork-on-write** (shipped themes read-only from the mod folder, never copied into config; user themes in config; F9-editing a shipped theme writes a fork into config): **this is the cleaner long-term architecture.** Update semantics become trivial — replace the mod folder and refresh/retire happen by definition; no manifest, no hashes, no drift, no adoption rules; the invariant holds *by construction* because player files live in a different root. It is how most mod ecosystems handle content. Costs, honestly counted: `ListProfiles`/`Load`/`Save`/`PreviewPath`/`Duplicate` all become two-root operations with shadowing rules (`HudProfileStore.cs:246-362`); the F9 autosave path must fork-on-first-touch (`Tick`/`FlushNow`, `:437-472`); ProfilesTab and the F9 profile pickers need "shipped (read-only)" affordances; the F6 dev flow (no mod folder) needs embedded fallbacks anyway; and existing players' config copies of shipped themes must be one-shot migrated — which itself needs the canonical hash to tell pristine copies (delete, shadow the shipped one) from edited ones (keep as user fork). Call it M/L effort with real regression surface across every profile touchpoint, right before release.
- **Hash-manifest (current):** ~90 lines, zero schema change, invariant verified above, residual weaknesses all fail safe (drift freezes, collisions go undelivered — nothing is ever lost). Its two real defects (Findings 3, 4) are one- and two-line fixes.

**Recommendation: keep the hash-manifest for release, apply the fixes, and bank fork-on-write as the post-release refactor to do only if the manifest model actually bites in the wild.** The migration path from manifest to fork-on-write stays open forever (the manifest even helps it — it records which config files were ours). Do not attempt the switch this close to launch.

---

## Severity — how bad is it really

- **The invariant holds. Nothing destroys player work.** After a genuinely adversarial pass — collisions, retires, downgrades, hand-deleted manifests, corrupt files, canonical-drift — every mutating branch either proves pristineness first or leaves the file alone. This is the part that had to be right, and it is right.
- **Finding 3 (freshInstall dead) is the one that actually undermines the foundation.** Not because of today's behaviour (a spurious log line) but because the design document, the code comments, and the play-test checklist all describe a safety property that does not exist in production. Shipping it unfixed means the first real migration step is written against a false premise. Two-line fix — there is no reason to ship without it.
- **Finding 4 (drift freeze) is "the feature quietly turns itself off."** No player is harmed; FlorpyDorp is — the next Sanitize evolution silently disables refresh/prune for every existing player, and nobody will notice until a "fixed" theme doesn't reach anyone (the exact bug class this system was built to end). One-line fix.
- **Finding 6 (starter-doc under a shipped name) is low-probability, high-annoyance, permanent.** It needs a packaging or download failure to trigger — but the stale-`dist/` failure has literally happened before, and the result is unrepairable-by-update because the invariant (correctly) protects the junk.
- **The doc contradictions (Finding 1) are ugly-but-harmless today** and actively harmful the first time someone else (or future-you) maintains a migration from the doc.
- **Dead config entries and the "7. HUD" trap are cosmetic-to-confusing,** not dangerous — but first release is the only free chance to not fossilize them into thousands of cfgs.
- **Pre-manifest junk is genuinely harmless data-wise** but visibly degrades the front door for exactly the people who supported you pre-release (updating playtesters), and it already generated a confused bug report. Worth a product answer, not a README answer.

---

## Recommendations

1. **[P0, S] Fix fresh-install detection for the SLP-pre-created cfg.** In `StationeersUIMod.cs:107-115`, treat an existing zero-length cfg as absent, mirroring the clause already used at line 128: `bool cfgReal = File.Exists(cfgPath) && new FileInfo(cfgPath).Length > 0;` then `freshInstall = !cfgReal && !File.Exists(legacyPath);`. Verify with the play-test: a deleted cfg must relaunch with `ConfigVersion = 1` and **no** "Config migrated" log line.
2. **[P0, S] Make pristine re-adoption unconditional in SyncShipped.** `HudProfileStore.cs:96` → `bool untouched = diskHash == shipHash || (known && diskHash == prev);`. This self-heals canonical-hash drift (Finding 4) and re-baselines the manifest whenever disk provably equals the current shipped content. Also correct the stale comment at `HudProfileStore.cs:149-150` ("Sanitize is self-contained") to state the real contract: deterministic only for coherent-styleSource, unique-Id profiles — which shipped themes must be.
3. **[P1, S] Embed Stationeers Blue and Pure HUD as name-matched self-heal factories** (the existing `Glassy40Default.cs` pattern), wired into the factory selector at `HudSystem.cs:245-252`, so a missing shipped default regenerates as itself instead of persisting `BuildStarterDocument` junk under its name (Finding 6). Alternative if embedding feels heavy: make `LoadActive`'s self-heal NOT write to disk when the name is unrecognized — but the embed is better (it also fixes F6-clean-config and partial-download cases) and is mechanical work.
4. **[P1, S] Sync the documentation to the code.** Fix `Config-and-Theme-Migration.md:89` (no clamp-down — state the never-lower rule and why) and `:99` (FNV-1a, not md5); correct the Changes Report's §2 line 42 and dependency list. Add the release-checklist line: *shipped-default rename ⇒ change `HudConfig.HudActiveProfile` default + prune fallback + mod-folder file together* (Finding 6).
5. **[P1, M] Ship a Reset affordance instead of the README recipe.** A "Troubleshooting" row in the F10 Control Center (ASCII-labelled — remember the TMP tofu rule for any button glyphs) with two actions: **"Restore shipped themes"** (delete `.shipped-manifest` + the current shipped-named files, resync — safe because SyncShipped rebuilds; custom themes untouched) and **"Sweep old shipped themes"** (the factory-hash sweep from Finding 9: delete "Default"/"Glassy"/"Glassy 2.0"/"Glassy 4.0" only when canonically identical to their embedded factory build — provably pristine, edited copies kept). Optionally mirror as a `uiareset` console command via the existing `Patch_CommandLine_Process` pattern (`Core/FinderCommands.cs`) — it mutates only our own files, no game state, so the MP funnel rule is untouched.
6. **[P1, S] Single-source the shipped-name list.** Expose the manifest keys (or a mod-folder scan) from `HudProfileStore` and drive `ProfilesTab.Featured` (`ProfilesTab.cs:22-23`) from it, so the front-door cards always match what's actually shipped; keep the alphabetical fall-through but cap it or label non-shipped cards. Freeze the schema-gated upgrade (`HudSystem.cs:255-274`) with a comment: legacy names only, never extend — SyncShipped owns the shipped set.
7. **[P1, S] Prune the dead binds before first release fossilizes them:** delete the `GridMode` (`UIAConfig.cs:496`) and `RadialIconScale` (`:636`) binds, and prefix the nine legacy-only "7. HUD" descriptions with "(LEGACY ImGui HUD only — see 10. Visor HUD)" (`:594-624`). Do NOT move the four live `HideVanilla*` keys — changing section/key names orphans stored values (the doc's own rule).
8. **[P2, S] Robustness niceties in SyncShipped:** per-file try/catch inside the seed/refresh and prune loops so one locked/read-only file doesn't abort the entire sync (`HudProfileStore.cs:57-142` is one big try); an `Info` log when a name collision blocks delivery of a new shipped theme (scenario d); delete a retired theme's `.png` beside its `.xml` in the prune branch (`:111-130` currently leaves art behind).
9. **[P2, S] Shipped-theme authoring lint:** at sync (or as a dev console command), hash a shipped source twice and warn if unstable (catches duplicate-Id GUID minting and any future nondeterministic Sanitize immediately); stamp exported themes with the current Schema (both shipped XMLs say `Schema="7"`, current is 14) and fill `Description`/`Author` so the Control Center cards read properly.
10. **[P2, M] Add the ConfigVersion/manifest state to a `uiadiag`-style dump** (stamp value, manifest entries vs disk, per-file pristine/edited verdict) — turns every future "themes are screwy" report into a one-paste diagnosis.

*(Also: the whole changeset — `Core/ConfigMigration.cs`, `HudProfileStore.cs`, `StationeersUIMod.cs`, the doc — is currently uncommitted on `main`. Commit it as one reviewed unit with these fixes, staging only these paths per the concurrent-agent rule.)*

---

## What NOT to do

- **Do not switch to fork-on-write before release.** It is the better end-state architecture (see Finding 10) but touches every profile code path at M/L effort for zero additional player safety — the manifest already guarantees the invariant. Revisit only after real-world evidence the manifest model bites.
- **Do not build an in-XML `Origin` marker.** It duplicates what the manifest does with a worse trust model and a schema change.
- **Do not auto-prune pre-manifest junk by name alone.** "Glassy 2.0" on disk might be somebody's heavily edited daily driver. The factory-hash proof (Rec. 5) is the only safe sweep; anything less breaks the invariant you just verified.
- **Do not delete the legacy ImGui HUD + its "7. HUD" entries in a pre-release cleanup frenzy.** It's the diagnostics escape hatch and its removal touches `HudOverlayFeature`, `SettingsWindow`, and the DrawOverlay gate (`StationeersUIMod.cs:857-860`); relabel now (Rec. 7), retire calmly post-release with a proper ConfigMigration step.
- **Do not "harden" ConfigVersion against hand-editing** (hidden files, checksums). The current design already makes a fiddled stamp near-harmless; complexity here buys nothing.
- **Do not add more schema-gated name upgrades.** Every new shipped-theme fix goes through the mod folder + SyncShipped refresh — that's the single owner now.
