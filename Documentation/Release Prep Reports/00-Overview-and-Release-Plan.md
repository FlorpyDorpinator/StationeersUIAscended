# 00 — Overview & Release Plan

> Release Prep Reports — 2026-07-25 — synthesis of seven parallel audits (reports 01–07 in this
> folder, ~1,000 lines of cited findings). Written for FlorpyDorp deciding what happens between
> now and the first public Steam/Workshop release.

---

## The one-paragraph answer

**Your fear was spaghetti; the audit found the opposite.** The 62,500-line source is unusually
clean — zero TODO/HACK debt, one descriptor model driving all three property popups, fail-soft
patching on all 22 Harmony classes, a real update-migration machine already built. What actually
stands between you and a safe release is **not a rewrite of anything** — it is roughly a dozen
small, named, S-effort fixes (two of them one-to-two-liners in the brand-new migration code that
would otherwise silently defeat it), a stale release artifact that must be rebuilt from a
committed tree, a Guide that is one version behind the mod it teaches, and a profile flow missing
New/Rename/Delete/Revert. The big restructures you're contemplating (F9 flatten, theming
simple-mode, GridTheme unification, fork-on-write theme storage) are all **post-release** work —
each is safer to do against real user feedback, and none blocks shipping.

| # | Concern | Verdict | Report |
|---|---------|---------|--------|
| 1 | Codebase / workspace mess | **ACCEPTABLE** — source is clean; the mess is git plumbing & workspace, invisible to players | [01](01-Codebase-and-Workspace-Hygiene.md) |
| 2 | Stable long-term package | **NEEDS WORK** — code is release-grade; the *artifact & process* have gaps (stale dist, unplayed migration, untested Workshop flow) | [02](02-Release-Package-Stability.md) |
| 3+7 | F9 rough / knob parity / spaghetti | **ACCEPTABLE** — parity is ~90% real *by construction*; 2 small bugs + dead-control pruning, not a rewrite | [03](03-F9-Editor-and-Knob-Parity.md) |
| 4 | Effects patchwork / profile flow | **NEEDS WORK** — ~470 knobs across 6 systems is a *presentation* problem; profile CRUD is genuinely missing | [04](04-Theming-Architecture-and-Profile-Flow.md) |
| 5 | F10 unfinished / theme mismatch | **NEEDS WORK, but small** — mismatch root-caused (data bug + one missing color source); does **not** need its own editor | [05](05-F10-Control-Center.md) |
| 6 | Theme shipping / config foundation | **NEEDS WORK** — invariant genuinely holds, but 2 tiny bugs defeat parts of it; fixes are 1–2 lines each | [06](06-Theme-Shipping-and-Config-Migration.md) |
| 8 | Tutorial / onboarding | **NEEDS WORK** — the plumbing already exists (auto-open Guide, live keybind chips); the *content* stopped at 0.9.0 | [07](07-Onboarding-and-Tutorial.md) |

---

## Direct answers to your eight questions

**1. "How messy is the codebase? Do we need to clean it up?"**
No pre-release cleanup needed. Zero TODO/FIXME debt, no orphan files, centralized logging. The
genuine findings are: ~1,900 lines of *intentional* legacy fallback renderers behind config gates
(retire one release **after** launch, via ConfigMigration), two dead config keys, one god-object
(`HudSystem.cs`, 2,422 lines — well-sectioned, one clean 380-line extraction available), and
~330 lines of duplicated store boilerplate. The real mess is the **workspace**: `.git` is 435MB
(350MB reclaimable via `git gc`), root junk files, and — important — **main history contains a
7.2MB private session log and the game's decompiled DLLs**, so the repo must stay private or be
scrubbed before ever flipping public. Also: `.gitattributes` sets `merge=union` on `*.cs` — a
silent source-corruption trap given the multi-agent workflow; remove it.

**2. "Are we shipping a stable package that works long term?"**
The *code* is. All 22 patch classes fail soft per-feature; the UGUI HUD survives even if the
ImGui hook dies; reflection sites are null-checked with stated degradation; the Update circuit
breaker and teardown are correctly engineered. The *process* is the risk: the current `dist/` and
zip are **stale** (built before the migration WIP; zip even carries the old version name), the
Workshop first-publish flow (publishedfileid 0 → paste the returned id) has never been rehearsed,
the `.vdf` changenote says 0.9.1.0, and nothing has verified the mod on the **public** game
branch (you compile against beta). All fixable in a day, plus one end-to-end install test.

**3. "Is the F9 menu too rough for users?"**
Rough, yes — spaghetti, no. Editing is centralized (click → `DescribeProps` → one generic
`HudPropDrawer`); transition effects already achieve parity *by construction* via the
`HudTransitionFx` registry (the same table drives global and per-element UI, so they cannot
drift). What makes it feel messy: ~90 controls on the Effects tab with every header expanded,
SDF esoterica at the same rank as basics, a **duplicated "Suit power & transitions" section on
two tabs**, 4 typography sliders that do nothing in document mode, and jargon labels (Tier
A/B/C). Pre-release: fix the one real bug (Reset button re-pins legacy keys), merge the duplicate
section, collapse headers, add one "Show advanced" toggle. The deep flatten is post-release.

**4. "Effects patchwork, weird profile flow, overwhelming colors?"**
The *rendering* patchwork was already fixed by the 2026-07-16 style migration — resolution is a
coherent Global/Custom two-state plus tri-state transitions. What's real: **~470 knobs across six
differently-scoped theme systems** (HudConfig ~160, HudPalette 22, RadialPalette 23, UIAConfig
~45, GridTheme 42, UiaMenuTheme 19, plus per-element keys) — an inventory/presentation problem,
~80% addressable by regrouping + pruning concrete dead knobs (named in report 04: `HudSlotNumber`,
radial `TextAccent`, the duplicate legacy Glitch family, alert-color duplicates). And yes —
**Duplicate-As is confirmed the only creation path**; New/Rename/Delete/Revert-to-shipped are all
missing (Delete even exists in the store as dead code). Those buttons are the P0; palette-first
presentation is the P1; "simple mode" is post-release.

**5. "F10 unfinished, didn't match Pure HUD?"**
Root-caused, and it's **mostly a data bug**: the F10 menu single-sources its accent from the
palette's `HudLineAccent`, and shipped **Pure HUD.xml leaves that at the default cyan** (its green
identity lives in `PanelBorder`/`Text*`, which the menu never reads). Fix the theme data + give
the menu kit a `Border` color sourced from `PanelBorder` — two S-effort changes — and F10 follows
every profile. It does **not** need its own editor: F9 click-to-edit already exposes Follow + 18
per-color overrides on the menu. Remaining polish is a short list (dropdown clamping, stale
"legacy F10 panel" label, unthemed item-picker chrome). Note: **radial colors are only editable
in the legacy ImGui window** — that surface needs a release-story decision.

**6. "Review the config thing; can my updates break player themes?"**
The foundation is **right and it holds**: a fresh adversarial pass re-verified all three earlier
fixes and found **no path that destroys player work** across all ten update scenarios. But it
found two bugs that make parts of it *silently inert* (both fail-safe, both tiny):
- **Fresh-install detection never fires in production** — SLP creates the cfg (0 bytes) *before*
  `OnLoaded`, so the `File.Exists` test is always true. Fix: treat zero-length as absent (2 lines).
- **Hash-drift freeze** — once a file is "known", pristine re-adoption is blocked; any future
  `Sanitize` change would permanently freeze theme management. Fix: one line
  (`diskHash==shipHash || (known && diskHash==prev)`).
Also P1: embed Stationeers Blue + Pure HUD as self-heal factories (today a missing shipped file
regenerates as the generic starter **persisted under the shipped name** — permanently
player-owned, unrepairable by update); sync three stale doc claims; add the Reset affordance
(F10 "Restore shipped themes" + optional `uiareset`) — which also answers the playtester incident.
Long-term: hash-manifest is correct for release; **fork-on-write** (shipped read-only in the mod
folder, user themes fork on edit) is the cleaner end-state — evaluate post-release only.

**7. "Flatten F9 so every global knob exists per-element?"**
It's closer than you think: 15 of 24 element types already carry the full knob set; the 9 that
don't are correct-by-capability (text-only/borrowed/line elements). The inverted defect: universal
knobs are *offered* on ~8 types where they do nothing (accent/font-scale) — hide them via two
virtuals. The genuine spaghetti is **one list in five places**: the ~30 steady-state effect keys
are hand-maintained in the F9 tab, the per-element mirror, two snapshot functions, and reset.
The structural fix — extend the proven `HudTransitionFx` registry pattern to steady-state
effects — makes parity true by construction, kills the five-way duplication, and is exactly one
well-scoped **post-release** project (L, with its own adversarial review).

**8. "Tutorial with gifs/videos, first-run popup, theme chooser?"**
Better news than expected: **the first-run popup already ships** (Guide auto-opens once per
install, safely in-game) and keybind chips already render **live from the bind registry**. What's
missing is content and art: the Guide never mentions the Universal Inventory, pins, belt swap,
drag flows, or the cursor latch (all 0.9.1–0.9.2 flagships), and the theme cards all show
"PREVIEW Coming soon" because no PNGs ship (the code path is done — export two screenshots).
On media: **GIF is impossible** in Unity; PNG flipbooks cost 20–60MB for 4s loops; but the game
ships `UnityEngine.VideoModule` (Mono build), so **mp4 clips from the mod folder are plausibly
feasible** (~2–4MB each) — worth a half-day spike, must fail soft to a screenshot. The Welcome
wizard (theme cards with live apply, diegetics toggle, Performance/Balanced/Fancy tier choice)
is composition of parts that already exist — M effort. And the Workshop page is the real video
channel: report 07 lists the exact ten clips to record. `About.xml`'s description is stale
("ImGui HUD", "POC") — rewrite before publish.

---

## The release-blocking set (consolidated P0 — ~2–4 days of code + a test day)

**Foundation (do first, in the WIP change set before committing it):**
1. Fresh-install detection: zero-length cfg = absent — `StationeersUIMod.cs:113` (S) *(06)*
2. Pristine re-adoption unconditional — `HudProfileStore.cs:96` (S) *(06)*
3. F9 Reset bug: delete the legacy-key re-writes — `HudEditorMode.cs:1384-1392` (S) *(03)*
4. **Commit the WIP** (ConfigMigration + SyncShipped + StateChips + these fixes) *(02)*

**Product polish:**
5. Profile flow: **New** (blank slate) + **Delete** + **Rename** + **Restore shipped** buttons (S each) *(04)*
6. Pure HUD.xml palette fix (`HudLineAccent` → theme green, `HudTextDim`) + menu-kit `Border`
   from `PanelBorder` (S+S) *(05)*
7. Merge the duplicated "Suit power & transitions" F9 sections (S) *(03)*
8. GuideTab → 0.9.2 content + live bind lookups everywhere (S) *(07)*
9. Ship preview PNGs for both themes (S) *(07)*

**Ship mechanics:**
10. Repackage from the committed tree (discard stale zip); fix `.vdf` changenote (auto-inject
    from About.xml); rehearse the publishedfileid-0 first-publish flow (S) *(02)*
11. End-to-end install test: `package.ps1 -Install` + the 6-item migration play-test list +
    double-F6; then verify once on the **public** game branch; after publish, verify a true
    subscribed install (M) *(02)*

## Strongly recommended before release (P1 — pick by taste, ~2–4 more days)

- Embed both shipped themes as self-heal factories *(06)* — closes the "broken Stationeers Blue
  forever" hole; single-source the shipped-name list (ProfilesTab.Featured ← manifest)
- Reset affordance: F10 "Restore shipped themes" + "Sweep old shipped themes" (+ `uiareset`) *(06)*
- First-run **Welcome page** in the Control Center (theme cards, diegetics, tier choice) + a
  visible "?" reopen affordance *(07)*
- F9 presentation pass: collapse Effects headers, "Show advanced" toggle, plain-language tier
  names; hide inert universal knobs; gate the 4 dead typography sliders *(03)*
- Palette-first Theme tab regroup (8 core wheels + Advanced); retire the duplicate legacy Glitch
  UI; prune dead knobs **via ConfigMigration now** (strictly harder after first-run fossilizes
  them); extend `HudTheme.Exclude` with the performance knobs *(04)*
- Workspace: remove `merge=union`, `git gc` (+350MB), untrack root junk *(01)*
- Rewrite `About.xml` Workshop description; record the trailer + ten feature clips *(07)*
- mp4 `VideoPlayer` spike (half-day, fail-soft) — decides the in-game media story *(07)*

## Post-release roadmap (P2 — sequenced, do not front-load)

1. **HudStyleFx registry** for steady-state effects (the real F9 flatten — parity by
   construction, kills the 5-way duplication) — L, own adversarial review *(03)*
2. **GridTheme + UiaMenuTheme fold into HudTheme** (`grid:`/`menu:` prefixes) so profiles carry
   the whole look; then the radial style block *(04)*
3. **Retire the three legacy render paths** (~2,200 lines: legacy ImGui HUD, fixed-panel HUD,
   ImGui radial painter) + dead keys, via ConfigMigration steps *(01/03)*
4. Simple mode (8 wheels + 5 masters); in-game media clips; first-use nudges (per-install
   HintUsage variant); patch-failure notice in F10 *(04/07/02)*
5. **Fork-on-write** theme storage — only if the manifest model bites in practice *(06)*

---

## Cross-cutting facts every future change must respect

- **SLP creates the cfg before `OnLoaded`** — never use `File.Exists` for first-run detection.
- **`HudDocument.Sanitize` is not guaranteed deterministic** (live-global bake, GUID minting) —
  anything hashing canonical profiles inherits that; the shipped-theme authoring lint (P2, 06)
  is the guard.
- **Every new `HudConfig` knob is auto-captured into profile themes** unless added to
  `HudTheme.Exclude` — performance knobs must go on the denylist.
- **Any F9 global edit stamps a full theme snapshot into the active profile** — in-game testing
  mutates the active profile's stored theme.
- Shipped-name knowledge currently lives in **three places** (HudSystem factories,
  ProfilesTab.Featured, mod folder) — the mod folder/manifest is the intended single owner.
- The shipped default is **Stationeers Blue** (not Glassy 4.0 — CLAUDE.md's architecture map is
  out of date and should be corrected).
- The repo **must stay private** (or be scrubbed) — main history carries a private session log
  and game DLLs.
- `dist/` staging goes stale — treat `tools/package.ps1` from a clean committed tree as the only
  legitimate packaging path; never hand-sync dist.
- Keys/binds shown to users must come from `UiaKeybinds`/`KeyManager` live lookups (ASCII-only
  for TMP surfaces) — never hard-coded strings.

## Suggested execution order

**Wave 1 — Foundation lockdown (1–2 days):** P0 items 1–4. The migration machinery becomes
trustworthy and committed.
**Wave 2 — Product polish (2–3 days):** P0 items 5–9, then as much P1 as appetite allows
(Welcome page and the F9/Theme presentation pass give the most first-impression value per day).
**Wave 3 — Ship (1–2 days):** P0 items 10–11 — package, test, publish, verify subscribed
install. Version number for this release: **FlorpyDorp decides** (per project rule; the P0 set
is arguably 0.9.3.0-sized, but that's your call, not this report's).
**Post-release:** the P2 sequence above, one project at a time, each with its own Changes Report
and adversarial review.

*Individual reports carry full evidence, file:line citations, severity calibration, and
"what NOT to do" guards — read them before executing any wave.*
