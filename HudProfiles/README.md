# HUD Profiles

The mod's **shipped** HUD layout/theme documents, kept here in the repo so they can be reviewed,
versioned, and packaged. This is the source `package.ps1` copies into the shippable mod — what's
in this folder is what every player's game seeds on first launch.

## What ships

Two curated themes, both a complete `HudDocument` (layout **and** theme — colours, glass/glow
effects, radial palette, Grid skin, F10 menu skin all travel together):

- **`Stationeers Blue.xml`** — the default active profile on a fresh install.
- **`Pure HUD.xml`** — the second curated look.

Each theme's preview screenshot lives beside its XML as `<name>.png` (e.g.
`Stationeers Blue.png`), shown wherever the mod lists profiles with art (F9/F10 profile
pickers). Add a theme's PNG here alongside its XML — same base filename, different extension.

## How the mod manages this folder (`HudProfileStore.SyncShipped`)

Every launch, the mod compares what's in **this folder** against the player's
`BepInEx/config/StationeersUIMod/HudProfiles/`, using a provenance file (`.shipped-manifest`)
that records exactly what it last shipped. Three moves, none of which ever touches a profile the
player made or edited themselves:

| Move | When | Result |
|---|---|---|
| **Seed** | a shipped theme is missing from the player's folder | copied in |
| **Refresh** | a shipped theme the player never edited, and this folder shipped a new version | overwritten with the new one |
| **Prune** | a theme retired from this folder that the player never edited | deleted (an edited copy is kept, unmanaged) |

So to change what ships:
- **Add a theme** — drop `My Theme.xml` (+ optional `.png`) in here. Every player gets it seeded
  on their next launch.
- **Fix/improve a shipped theme** — edit its `.xml` here. The sync refreshes it for every player
  who never touched their own copy of it.
- **Retire a theme** — delete its `.xml` (and `.png`) from here. The sync prunes it from players
  who never edited it; anyone who customised their copy keeps it, unmanaged.

Full detail (canonical hashing, the CRUD/manifest interaction, gotchas):
`Documentation/Config-and-Theme-Migration.md` §3.

**Hand-sync reminder:** both curated themes are also embedded verbatim as C# self-heal factories
in `Assets/Scripts/StationeersUIMod/UI/Hud/ShippedProfiles.cs`, so a missing/corrupt copy of a
shipped name can rebuild from the real design even with no mod folder available (e.g. the F6 dev
flow). **If you edit either XML in this folder, re-embed the matching constant in
`ShippedProfiles.cs` by hand** — nothing does this automatically, and letting the two drift makes
the self-healed copy hash as "player edited," silently cutting it off from future shipped
updates.

## Adding your own profile (as a player)

You don't need to touch this folder at all — use F9's **New** / **Duplicate** buttons in-game.
If you'd rather hand-author or share an XML file directly, drop it into:

```
<Stationeers>/BepInEx/config/StationeersUIMod/HudProfiles/
```

and pick it from F9 → Profiles. A profile you name yourself is never auto-managed or
auto-upgraded by the sync above — it's entirely yours, exactly as saved, forever.
