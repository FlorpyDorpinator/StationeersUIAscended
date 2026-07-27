# To Do — deferred until after the 0.9.2.5 build

Items FlorpyDorp explicitly parked during the release-prep review (2026-07-25). Each entry
names its source report so the full context is one click away. Nothing here blocks 0.9.2.5.

## 1. Full repo nuke + fresh private repo  *(next big pass — FlorpyDorp's call on timing)*
From 00/01 + FlorpyDorp's directive. Steps when we do it:
1. Full local backup clone (bundle + working copy) of the current repo FIRST.
2. Delete the GitHub repo `StationeersUIAscended`; recreate same name, **private**.
3. Re-commit the CURRENT state as the initial commit — history is discarded on purpose.
4. **Scrub guarantee:** no commit, blob, or metadata may carry `[redacted-email]`
   (the old remote main was already overwritten 2026-07-25, but main history still contains a
   7.2MB private session log at `.specstory/history/` and the game's decompiled DLLs in
   `Assets/Assemblies/` — the nuke removes all of it).
5. New `.gitignore` regime: track ONLY the mod source + README. Exclude game DLLs/assets,
   `Documentation/`, `Changes Reports/`, `.specstory/`, Unity template assets, `dist/`.
6. Local safety refs (`archive/*`, old branches) stay local-only, never pushed.

## 2. Onboarding & tutorial (entire report 07)
Deferred because mod functionality may still change before release. When picked up:
GuideTab → current-version content with live bind lookups, first-run Welcome page (theme
cards / diegetics / perf tier), "?" affordance, mp4 VideoPlayer spike, Workshop media set
(ten clips + trailer listed in report 07), About.xml description rewrite.
**Exception pulled forward into 0.9.2.5:** theme-card preview PNGs (FlorpyDorp supplied the
two screenshots; save as `HudProfiles/<name>.png`).

## 3. F9 "Simple mode"  *(04 rec 11)*
8 palette wheels + 5 master effect sliders + curvature + profile row; everything else behind
Advanced. Do after the Wave-D regroup has settled with real users.

## 4. Fork-on-write theme storage  *(06 P2)*
Shipped themes read-only from the mod folder; editing forks a user copy. Cleaner long-term
update semantics than the hash-manifest — revisit ONLY if the manifest model bites in practice.

## 5. Power-states rewrite  *(FlorpyDorp note on 03 rec 1)*
The suit-power / transition state machinery deserves a ground-up rethink after this big
rework; for now the Reset bug fix + section merge shipped in Wave A.

## 6. Unity template asset prune  *(01 P2, tied to the repo pass)*
Assets/TextMesh Pro (28MB) / Assets/Assets (9.2MB SLP template) / Texture2D (6.5MB) etc. ship
nothing. Handle together with item 1's ignore regime rather than churning the tree now.

## 7. Steam end-to-end update test  *(02 — FlorpyDorp runs this personally)*
Download from Steam → play → push an update → redownload → verify SyncShipped refresh/prune +
local-edit survival. The script for it is `Documentation/Release Prep Reports/
Pre-Release-Test-Checklist.md` (written in Wave F).

## 8. Re-case rename is a no-op  *(Wave E §7 — minor)*
`HudProfileStore.Rename` refuses `OrdinalIgnoreCase`-equal names, so you cannot re-case a
profile ("pure hud" → "Pure HUD") from either the F9 or F10 UI — F9 treats the refusal as a
plain cancel. Deliberate for now (the alternative is a two-step move through a temp name), but
it's a rough edge a tester will hit. Do it in the file system in the meantime.

## 9. ~~Shipped themes are still authored at Schema 7~~ — **DONE 2026-07-27** *(style-parity Phase 5)*
`Stationeers Blue.xml` and `Pure HUD.xml` are now stored in their own post-`Sanitize` canonical
form at **Schema 16**: `styleSrc` materialised on all 44 element slots, the Phase 0b SDF halo
back-fill applied to the three bare-forked elements in each, and the Wave C `tierStyle` adoption
pre-done. A new install therefore parses them and finds nothing to repair — no `RepairedOnLoad`
rewrite at boot. Produced by a throwaway console harness that loads each XML through the mod's
OWN `XmlSerializer` + `HudDocument.Sanitize` and writes the result back (no hand-transcription;
`Sanitize` reads no live global, so a headless run is valid — verified). The re-export is a
FIXED POINT: re-running it changes nothing. `ShippedProfiles.cs`'s embedded constants were
re-synced from the same output and verified line-for-line and by `CanonHash` equality.
See `Changes Reports/2026-07-27 - Style parity Phase 5 (StripGlass on the registry, shipped
themes at Schema 16).md`.

## 9b. ~~Report 03 rec 7 / item 1: the five hand-written effect lists~~ — **DONE, Phases 1–3**
The audit's "the reset button silently rotted because the knob list is hand-maintained" finding
(`03-F9-Editor-and-Knob-Parity.md` finding 5 / recommendation 7, and `00-Overview-and-Release-
Plan.md` §170 item 1) is closed by the `HudStyleFx` registry: F9's global rows (Phase 1), the
element popup (Phase 2), the seed / def-only snapshot / "reset to globals" loops (Phase 3) and
"Flatten ALL boxes" (Phase 5) are all rendered or driven from the ONE table. There is no
hand-written steady-state effect list left to drift.

## 10. SDF chamfer support — superellipse p=1  *(corner-style follow-up, 2026-07-26)*
Cut panels currently drop off the analytic `sdfglass` renderer (its silhouette is computed
in the fragment shader), losing the SDF-only glass extras (frost depth, chroma, edge-flow,
gaussian halo family, SDF iridescence). The shader's superellipse exponent at **p = 1 is
exactly a chamfer**, but the ABI quantizes it as `(p-2)/6` — lifting the range below 2 needs
a coordinated `uia_effects.bundle` rebuild (Dev/UiaEffectsBundle) + ABI bump. Do this when
the bundle is next touched; it restores full glass parity for Cut corners. (Bite risk went
up 2026-07-26: FlorpyDorp is authoring themes — Zirillian Red — that use frost/chroma/flow
on panels he may want cut.)
