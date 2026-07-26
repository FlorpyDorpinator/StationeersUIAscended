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

## 9. Shipped themes are still authored at Schema 7  *(Wave E §7 / Config-and-Theme-Migration §3)*
`Stationeers Blue.xml` and `Pure HUD.xml` both carry `Schema="7"` while `HudDocument.CurrentSchema`
has moved to 15. Harmless today — `Sanitize` migrates + re-saves them on open every time, and
`SyncShipped` hashes the canonical (post-`Sanitize`) form on both sides, so the stale `Schema`
attribute never causes a spurious refresh — but it does mean an extra disk write at boot for
every player, and it's needless churn to keep carrying forward. Re-export both at the current
schema (F9's dev-only export-to-shipped flow, §D5 of the checklist) during the next
theme-authoring pass, not mid-release.
