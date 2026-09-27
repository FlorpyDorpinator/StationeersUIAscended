# UI Ascended 1.0 - launch kit

Everything needed to announce 1.0: the store text, the posts, the guide and its pictures.
Built 2026-09-26 from a verified feature inventory (every claim traced to the CHANGELOG, a
Changes Report or the source). The mod is never called "SSUI" anywhere in here; that's
JacksonTheMaster's separate project.

| File | What it is | Where it goes |
|---|---|---|
| `workshop-description.bbcode` | New Steam Workshop page text (4.4k of 8k chars, ASCII, no double quotes) | Pushed by `tools/update-workshop-page.ps1` |
| `steam-guide.md` | 12-section illustrated guide, with how-to-publish steps at the top | Pasted into Steam by hand (no API exists) |
| `guide-images/` | 18 pictures in guide order: 1080p JPGs cut from the 4K `Release Folder/` captures, 2 GIFs and a square guide icon | Uploaded in the guide editor |
| `reddit-post.md` | Title options + body for r/Stationeers, and a suggested gallery | Reddit |
| `discord-post.txt` | The short version (1.4k of Discord's 2k limit) | Discord announcement |

## Links

FlorpyDorp's call (2026-09-26): **no Discord invite anywhere**, and the guide link is added
once the guide is finished. Both placeholders have been removed. When the guide exists, add a
"Full illustrated guide:" line to the Workshop description's Getting started list and re-run
`tools/update-workshop-page.ps1`. The script still refuses a real run if a `<...LINK>`
placeholder ever reappears.

## Launch-day order

1. **Decide on the tutorial and play-test.** The tutorial is being finished in a parallel
   session. Almost everything new since 0.9.7.4 is still play-untested; the checklists are
   `Documentation/0.9.8.0/Play-Test-SIMPLE.md` and `-FULL.md`.
2. **Cut the release.** The version is already bumped to 1.0.0 in `StationeersUIMod.cs`,
   `About.xml` and `CHANGELOG.md`. Then:
   - commit;
   - run `tools/package.ps1`, then `tools/publish-steam.ps1`;
   - tag `v1.0.0` and push `main` plus the tag.
   Fill in the release date in `CHANGELOG.md` first.
3. **Make the Workshop page public, with the new description:**
   `powershell -ExecutionPolicy Bypass -File tools\update-workshop-page.ps1 -Visibility Public`
   Steam's public API currently answers "not found" for the item, which is what it returns
   for items that aren't public, so announcement links would dead-end until this runs. The
   script only touches the page (description and visibility), never the mod files.
4. **Publish the Steam guide** whenever it's finished (`steam-guide.md`, about 15 minutes).
   Then add a "Full illustrated guide:" line with its URL to the Workshop description and
   re-run step 3 without `-Visibility`.
5. **Post.**
   - **Reddit:** a gallery post; the order is in the file.
   - **Discord:** paste `discord-post.txt`, and attach `Release Folder/GIFs/UI-Themes-Showcase.mp4`
     plus `guide-images/01-hero-visor-hud.jpg`. The MP4s play inline in Discord and are much
     smaller than the GIFs.
6. **Optional:** add the four theme HUD shots and the themes GIF to the Workshop item's own
   image gallery. This is web only: Workshop page > Add/edit images & videos.

## If the tutorial ships in 1.0 - APPLIED 2026-09-26

FlorpyDorp decided the tutorial ships live in 1.0. The lines below were applied to every
file, reworded for tour mode (the Welcome card offers a guided tour or just-in-time
lessons), and the F10 Guide description in the Steam guide was updated to match. They're
kept here as a record:

- `reddit-post.md`: replace the "**Coming next:**" line with
  `**New in-game tutorial** *(new in 1.0)*: 19 short lessons that teach the mod as you play - start from the Welcome card or F10 > Guide.`
- `discord-post.txt`: delete the "**Coming next:**" line and add this bullet to the list:
  `- **In-game tutorial** - 19 short lessons that teach the mod as you play`
- `workshop-description.bbcode`: add a section before Multiplayer-friendly:
  `[h2]Learn it as you play[/h2]` followed by
  `A 19-lesson in-game tutorial: a Welcome card, hands-on First Steps, and short lessons that appear the first time you need them. Replay any of them from F10 > Guide.`
- `steam-guide.md`, section 1: replace the italic "Coming next" line with
  `[i]New to all this? The in-game tutorial teaches it in short lessons - start from the Welcome card or F10 > Guide.[/i]`
- `Assets/About/About.xml`: add a bullet to the 1.0.0 block (no double quotes), for example
  `[*] NEW IN-GAME TUTORIAL: 19 short lessons - a Welcome card, hands-on First Steps and just-in-time lessons, replayable from F10 - Guide`.
  Add the same to `CHANGELOG.md`.

## Check before announcing

Flagged by the feature audit; most are FlorpyDorp's call.

- **Workshop visibility:** the item appears not to be public (see step 3).
- **Untested bulk:** Simple SmartStow, the rebuilt F10, the Suggestions/Bugs tab and fix
  wave 1 are compile- and review-verified but mostly not play-tested.
- **Config section name:** `[Feedback]` is unnumbered, unlike the other sections. Rename it
  before 1.0 ships, or a later rename needs a ConfigMigration step.
- **Relay domain:** the Suggestions/Bugs form prints its relay address,
  `uiascended.ssui.dev`. The relay's landing page says UI Ascended and SSUI are separate
  projects; the posts never mention the domain.
- ~~**Dev tools in the player DLL**~~ **CLOSED 2026-09-26:** the release screenshot server
  (`Testing/UiaShotServer.cs`) is now left out of Release builds (`#if DEBUG` around the
  trigger-file poll), according to the Second Dev session, which got a Release build of the
  whole tree at 0 warnings / 0 errors.
- **Stale in-game strings:** a few still say "F10 > Storage" instead of "F10 > SmartStow".
  They are in the share-code export text, a routing message and two cfg descriptions.
- **Multiplayer wording:** the posts say "every inventory action goes through the game's own
  server-checked path" (true by design) and deliberately don't promise "no server install
  needed", because no formal mixed modded/unmodded server test is on record.
- **Canister gotcha:** Smart Stow fills empty canister sockets without checking the gas
  type, as vanilla does. The guide mentions it as a tip.
- **Credits:** every piece now has a thank-you listing the 11 Discord reporters. The names
  come from the reporters column of `Discord/triage/issues.md`, checked against the authors
  in the bug-report threads and the playtest channel, and are listed alphabetically. Each
  piece also has a special thank-you to JacksonTheMaster (JXSN). The maker line is now
  "Made by FlorpyDorp", with Jackson credited in that special thank-you. `About.xml`'s
  `<Author>` still reads "FlorpyDorp + JXSN". ThunderDuck posted in #general but isn't
  credited with any report, so they aren't listed. Names are plain text, not @-mentions;
  on Discord you can turn them into mentions to ping people.
