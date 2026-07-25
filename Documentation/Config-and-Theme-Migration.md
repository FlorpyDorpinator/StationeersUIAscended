# Config & Shipped-Theme Migration — a maintainer's guide

**Audience:** FlorpyDorp + JacksonTheMaster (anyone shipping updates).
**TL;DR:** A player's settings and profiles live in `BepInEx/config/`, which **survives every
update**. That's good (people keep their setups) but it means *new code defaults and shipped-theme
fixes do NOT reach existing players on their own*. Two systems fix that safely:
`ConfigMigration` (for the `.cfg`) and `HudProfileStore.SyncShipped` (for shipped HUD themes).
Both obey one rule: **never destroy or overwrite anything the player made or changed.**

---

## 1. The mental model: what a Steam update actually does

When you push a Workshop update, Steam replaces the **mod folder** (`StationeersUIMod.dll`,
`About.xml`, `HudProfiles/`, `uia_effects.bundle`). It does **not** touch the player's
**`BepInEx/config/`**, which holds:

- `com.stationeersuimod.ui.cfg` — every setting (F9/F10 knobs, toggles, keybinds, the active
  profile name). *(On a dev/F6 install it's `com.stationeersuimod.ui.scriptengine.cfg`.)*
- `StationeersUIMod/HudProfiles/*.xml` — HUD themes/layouts (shipped **and** player-made).
- `StationeersUIMod/{Grid,GridPins,Assignments,BeltBindings,Hotkeys,HintUsage,Loadouts,...}` —
  per-world layouts and bindings.
- `StationeersUIMod/Profiles/profiles.xml`, `HudIcons/`, `ProfilerSnapshots/`.

Because that folder persists, **BepInEx reads the STORED value, not your new code default.** So if
you change a default because the old one was wrong, existing players never see it — only brand-new
installs do. Same story for a shipped theme they already have. That is the whole problem these two
systems exist to solve.

**Fresh installs are never a problem** — they seed everything current. All of the below is about
*updating* players. (One subtlety in detecting "fresh": StationeersLaunchPad pre-creates the new
`.cfg` empty before `OnLoaded` runs, so an existing-but-0-byte file still counts as fresh — see the
Gotchas in §2.)

---

## 2. Config migration (`Core/ConfigMigration.cs`)

### How it works
- A hidden setting `ConfigVersion` (section `0. Internal`) stamps the config's schema version.
- On load, right after `UIAConfig.Bind(config)`, `ConfigMigration.Run(config, freshInstall)`:
  - **Fresh install** → stamped straight to `CurrentVersion`, runs **no** steps (its defaults are
    already current).
  - **Existing install** → runs each `ApplyStep(from → from+1)` from the stored version up to
    `CurrentVersion`, then stamps.
- Fail-soft: a throwing step is logged and skipped; nothing blocks load.

### When to add a migration
Any time you **change a default that was wrong**, **rename a key**, or **change what a value
means**. If you're just *adding* a new setting, you don't need a migration — existing players get
its default automatically.

### How to add one (the recipe)
1. Bump `ConfigMigration.CurrentVersion` by **one**.
2. Add a `case` in `ApplyStep` for the version you're migrating **from**:

```csharp
case 1: // v1 -> v2
    // A default was wrong: push the fix only to players who never changed it.
    ForceIfDefault(HudConfig.FxGlow, oldDefault: 0.50f, corrected: 0.80f);
    break;
```

`ForceIfDefault(entry, oldDefault, corrected)` overwrites **only if** the player still holds the
exact old default (i.e. they never chose a value). Anyone who set it on purpose keeps their choice.
That's the safe way to push a corrected default.

**Renaming a key** (carry the old value across so the player doesn't silently lose it):

```csharp
case 2: // v2 -> v3  — "OldKey" became "NewKey"
    var old = cfg.Bind("10. Visor HUD", "OldKey", 0f, "(migrated)");
    if (!Equals(old.Value, 0f)) HudConfig.NewKey.Value = old.Value;
    break;
```

**Deleting a key outright** (no rename — the setting is just gone, e.g. a whole feature was
removed): unlike a default-fix or a rename, simply removing the `cfg.Bind` call does **not**
remove the key from the player's `.cfg`. BepInEx keeps any key it didn't `Bind` this run as an
**orphaned entry** and writes it straight back out on the next `Save()` — it lingers forever and
keeps reappearing in the SLP settings panel. Strip it explicitly:

```csharp
case 3: // v3 -> v4 — "DeadKey" was removed outright, no replacement
    RemoveOrphaned(cfg, "10. Visor HUD", "DeadKey");
    cfg.Save();
    break;
```

`RemoveOrphaned`/`TryReadOrphaned` (added for the 0.9.2.5 Wave B cleanup, `ApplyStep(1->2)`,
see `Documentation/Release Prep Reports/wave-b-removed-keys.md` for a worked example with 38
keys) reach BepInEx's own `ConfigFile.OrphanedEntries` — the dictionary `Save()` reads to decide
what to write back for keys nobody bound this run. **Gotcha:** on the exact `BepInEx.dll` this
project references, `OrphanedEntries` is a **private** property (verified by reflecting the
get-accessor's IL attributes — `Private`, not the public one upstream BepInEx documents), so
`cfg.OrphanedEntries` is a compile error (CS1061) here. The helpers reach it through a small
cached reflection lookup (`BindingFlags.NonPublic | BindingFlags.Instance`) instead — reflection
isn't restricted by C#'s compile-time visibility check, so a removal through it is
indistinguishable from what the public API would have done. Fail-soft: if a future BepInEx
rebuild ever renames or removes the member, the helper returns null and the step simply stops
deleting orphans instead of throwing. Always call `cfg.Save()` once after a batch of
`RemoveOrphaned` calls (not per-key) to flush the change to disk in one write.

### The disciplines that avoid most migrations entirely
- **Never repurpose a key.** New meaning ⇒ **new key name**. Reusing a key with different
  semantics silently misreads everyone's stored value — the worst kind of bug because it looks fine
  to you.
- **Enums are stored by NAME** (BepInEx does this). So you can **append** new enum values freely and
  reorder the *declaration* — just never **rename** an existing value (that orphans the stored
  choice back to default). If you must rename an enum value, do it in a migration.
- **Clamp/validate every value on read** (the codebase already does this a lot). A stale
  out-of-range stored value then can't break the UI, migration or not.

### Gotchas
- `freshInstall` is computed *before* the legacy-copy block and before `UIAConfig.Bind` runs, using
  `!cfgExists && !File.Exists(legacyCfg)`, where `cfgExists` requires the file to both exist AND be
  non-empty — SLP's pre-created stub is 0 bytes, so it reads as absent (see §1). Without that
  non-empty check, fresh-install detection never fires in production: the file is always already
  there by the time `OnLoaded` runs. Don't move `ConfigMigration.Run` before `UIAConfig.Bind` —
  steps read bound `ConfigEntry` values.
- A hand-edited `ConfigVersion` **higher** than `CurrentVersion` is left as-is — migration only ever
  runs steps for versions *below* `CurrentVersion` and never lowers the stamp, so an ahead-of-current
  value is a no-op, not a downgrade.
- Steps must be safe on any older config and must never assume a value exists.

---

## 3. Shipped-theme sync (`Features/HudProfileStore.SyncShipped`)

### How it works
Runs every launch (`OnLoaded`). It compares the themes in the **mod folder**
(`StationeersUIMod/HudProfiles/*.xml`) against the player's config folder, using a provenance file
**`.shipped-manifest`** (`<filename>|<hash>`, FNV-1a 64-bit) that records exactly what the mod last
shipped. Three moves — **none of which ever touches a profile the player made or edited**:

| Move | When | Result |
|---|---|---|
| **Seed** | a shipped theme is absent | copy it in, record its hash |
| **Refresh** | a shipped theme the player never edited, and we shipped a **new version** | overwrite with the new one, update the hash |
| **Prune** | a theme we **retired** (no longer shipped) that the player never edited | delete it; if it was active, fall back to the shipped default |

"Never edited" = the on-disk **canonical content** still matches what we recorded shipping. The
hash is taken over the *loaded → sanitized → reserialized* form (not raw bytes), so the mod's own
idempotent rewrite-on-open and any cosmetic serialization difference never read as an edit — only a
real content change does. A player edit, a rename, or a hand-made profile diverges the hash (or has
no record), so it's **left alone**. Fail-soft; inert under F6 (mod folder unknown).

### How to change the shipped theme set (the recipe)
- **Add a theme:** drop `My Theme.xml` into the repo-root `HudProfiles/` (the packaging source) and
  ship it. Existing players get it seeded on next launch; new installs get it too.
- **Fix/improve a shipped theme:** edit its `.xml` in `HudProfiles/` and ship. The sync **refreshes
  it for every player who never edited their copy.** (Before this system, your fix reached *nobody*
  who already had it.)
- **Retire a theme:** delete its `.xml` from `HudProfiles/` and ship. The sync **prunes it from
  players who never edited it**, and hands it over (keeps it) for anyone who customised it.

**Author shipped themes clean** — see the limitation below.

### Limitations & why they're safe
- **Canonical hashing handles the rewrite-on-open.** `HudProfileStore.Load` re-serializes a profile
  in canonical form the first time it's opened (the `RepairedOnLoad` fold). The sync hashes the
  *canonical* form of both sides, so that rewrite is invisible to it — refresh/prune work even for
  themes that carry legacy fx bools. (Caveat: `CanonHash` runs `Sanitize`, which must stay
  deterministic. If `Sanitize` ever folds live config/globals into a profile, the canonical hash
  could vary per player — the failure is safe either way: at worst a spurious refresh that rewrites
  an untouched theme with identical content, or a theme read as "edited" and left alone.)
- **Pre-manifest junk isn't auto-pruned.** A player who has old themes from a version *before* this
  system has no manifest record for them, so we can't prove they're pristine and we **never** delete
  them. Only themes shipped *from this system forward* are managed for pruning. (A pristine copy of a
  *current* shipped theme IS adopted into management on first sync.) The old junk is harmless; a
  player can delete it, or a future one-time known-old-names cleanup could sweep it.
- **The manifest is provenance, not a lock.** Delete `.shipped-manifest` and the next sync rebuilds
  it conservatively (adopts pristine current themes, leaves everything else).

---

## 4. Player-facing troubleshooting

If a tester's HUD/themes are "screwy" and you want a clean slate, have them **quit the game** and
delete either:

- **Everything:** `BepInEx/config/com.stationeersuimod.ui*.cfg` **and**
  `BepInEx/config/StationeersUIMod/` → relaunch reseeds fresh defaults + the two shipped themes.
- **Just themes:** `BepInEx/config/StationeersUIMod/HudProfiles/` (keeps their Grid/belt/keybind
  setups).

This is the blunt instrument. The two systems above are what make it so you *rarely* need it.

---

## 5. Release checklist (the recurring bites)

- [ ] Bumped version in **all** of: `About.xml <Version>`, `StationeersUIMod.ModVersion`,
      `VersionDisplay`, `CHANGELOG.md`, git tag — **and asked FlorpyDorp for the number**.
- [ ] `About.xml <ChangeLog>` is **under 8000 characters** (SLP hard-rejects the manifest over that;
      keep the latest ~2 versions, full history in `CHANGELOG.md`).
- [ ] Packaged from a **fresh `dist/` stage**: current Release DLL, current `About.xml`, and
      `HudProfiles/` mirrored from repo-root `HudProfiles/` (the `dist/` folder goes stale — this
      has shipped the wrong profiles/changelog before).
- [ ] Changed a shipped default, renamed a key, or **removed one outright**? → **added a
      ConfigMigration step** (§2) — removed keys need `RemoveOrphaned` + `cfg.Save()`, not just
      a deleted `Bind` call, or they linger in players' `.cfg` forever.
- [ ] Changed the shipped theme set? → source is repo-root `HudProfiles/`; themes authored clean (§3).
