# Wave B — removed config keys (AUTHORITATIVE ledger)

**This file is no longer scratch.** It is the authoritative record of every config key
0.9.2.5 Wave B removed from the shipped mod, and it is now CONSUMED by
`Core/ConfigMigration.cs`'s `ApplyStep(1 -> 2)`: the step's `RemovedKeysV1ToV2` table and
`MigrateGlitchKeys` method implement exactly the entries below, one-for-one. If you add a
future removal/rename here for a LATER version bump, add the corresponding `case` in
`ApplyStep` and bump `ConfigMigration.CurrentVersion` — don't let this file drift from the
code (see `Documentation/Config-and-Theme-Migration.md` §2 for the how-to).

Independently re-verified 2026-07-25 (task B4) against `git diff HEAD --stat` and
`git diff HEAD -- '*.cs' | grep -i bind` for the whole Wave B commit range — every removed
`Bind(` call below has a matching diff hunk; nothing was missed. Also folded in two
long-dead keys per B4's own directive (`GridMode`, self-described "DEPRECATED - no longer
used"; `RadialIconScale`/`IconScale`, already caught by B2 below) — see the final tally at
the bottom of this file.

One line per key: `section | key | migration action`.

## 0.9.2.5 Wave B / task B1 — legacy HUD render paths deleted

### "7. HUD" (UIAConfig) — the 0.1.0 ImGui HUD overlay's knobs
Fed `Features/HudOverlayFeature.cs` only; the file is gone.

7. HUD | Enabled | delete
7. HUD | HandBoxes | delete
7. HUD | StatusStrip | delete
7. HUD | Vitals | delete
7. HUD | Clock | delete
7. HUD | ContextPanel | delete
7. HUD | VisorArcs | delete
7. HUD | Scale | delete

KEPT in "7. HUD" (do NOT migrate): `HideVanillaHands`, `HideVanillaClothing`,
`HideVanillaStatus`, `HideVanillaPlayerState` (live — read by
`HudSystem.SyncVanillaVisibility` / `SyncPlayerStateCluster`), and `HardcoreGating`
(preserved by directive; see the risk note in the change report — after the overlay's
deletion it currently has no reader).

### "10. Visor HUD" (HudConfig) — legacy renderer gates
10. Visor HUD | LegacyImGuiHud | delete
10. Visor HUD | UseDocumentHud | delete

### "10. Visor HUD" — the six fixed-panel Show* toggles
Element visibility now lives per-element in the profile document.

10. Visor HUD | ShowTopBar | delete
10. Visor HUD | ShowCompass | delete
10. Visor HUD | ShowEquipment | delete
10. Visor HUD | ShowHands | delete
10. Visor HUD | ShowVitals | delete
10. Visor HUD | ShowHologram | delete

### "10. Visor HUD" — the twelve fixed-panel size sliders
(Key names as bound, which differ from the C# field names for three of them.)

10. Visor HUD | TopBarHeight | delete
10. Visor HUD | TopBarCurve | delete
10. Visor HUD | TopBarWidth | delete
10. Visor HUD | CompassWidth | delete
10. Visor HUD | CompassHeight | delete
10. Visor HUD | CompassSpanDegrees | delete
10. Visor HUD | EquipBoxSize | delete
10. Visor HUD | EquipSpacing | delete
10. Visor HUD | HandBoxWidth | delete
10. Visor HUD | HandBoxHeight | delete
10. Visor HUD | VitalsWidth | delete
10. Visor HUD | VitalsHeight | delete

### "10. Visor HUD" — the four document-mode-dead typography sliders
`LabelFontSize` SURVIVES (HandBoxesWidget reads it) — do not migrate that one.

10. Visor HUD | ValueFontSize | delete
10. Visor HUD | CompassFontSize | delete
10. Visor HUD | BareWordFontSize | delete
10. Visor HUD | VitalsRowFontSize | delete

**Total: 32 keys.**

## 0.9.2.5 Wave B / task B2 — legacy ImGui radial renderer deleted

The Unity UGUI radial (`UI/UnityRadialView.cs`) is now the ONLY radial renderer;
`Overlay/RadialMenu.cs`'s `if (UIAConfig.UseUnityRadial.Value) { ... } else { legacy
ImGui draw-list painter }` branch is gone — `Draw()` calls `UnityRadialView.Render`
unconditionally. The gate key had no other reader.

8. Radial Visuals | UseUnityRadial | delete

Also removed alongside it (pre-existing dead key, trivially in scope — Bind only,
zero readers anywhere, doc comment says "LEGACY (unused since 0.3.1)"):

8. Radial Visuals | IconScale | delete

**Total: 2 keys.**

## 0.9.2.5 Wave B / task B3 — two live glitch systems unified onto the tri-state family

Legacy `HudGlitch` had its OWN master + severity (`GlitchEnabled` default false,
`GlitchIntensity` 0-1) fully independent of the `fxGlitch` tri-state globals that already
existed for the per-element transition resolver (`FxGlitchOn` default true, `FxGlitchAmt`
0-2, default 1) — so the F9 Effects tab showed the "Glitch tear" master/strength row
(driven by `FxGlitchOn`/`FxGlitchAmt`, doing nothing to the actual tear because
`GlitchEnabled` gated whether it fired at all) AND a second, separate "Power-transition
tear / shake" block below it with its own master/duration/severity/per-event toggles
(also exposed again in F10). `HudGlitch.Trigger`/`Fire` now read `FxGlitchOn` (master) and
`FxGlitchAmt` (severity) directly; `GlitchDuration` had no tri-state analogue so it moved
to a NEW bind, `FxGlitchDuration`, in the FX section. `GlitchOnPowerDown`/`GlitchOnPowerUp`
have no tri-state analogue either (a user may want the tear on only one side of the
transition) and are KEPT as-is, unmoved, unrenamed — not a duplication, just two
orthogonal per-event gates. The F9 row and the F10 toggle now each appear ONCE.

10. Visor HUD | GlitchEnabled | rename-to 11. HUD Effects (0.9.0) / GlitchTearOn (FxGlitchOn)
10. Visor HUD | GlitchIntensity | rename-to 11. HUD Effects (0.9.0) / GlitchTearStrength (FxGlitchAmt)
10. Visor HUD | GlitchDuration | rename-to 11. HUD Effects (0.9.0) / GlitchTearDuration (FxGlitchDuration, NEW bind)

Migration notes for whoever writes this step:
- `GlitchEnabled` default was **false**; `FxGlitchOn` default is **true** and is a LIVE,
  already-in-use master (also gates the per-element `fxGlitch` tri-state resolution) — do
  NOT blindly overwrite `FxGlitchOn` with the legacy value if the user's cfg has no
  explicit `GlitchEnabled` line (i.e. they never changed it from its own default); only
  carry the value over when the OLD key is actually present in the user's file, and prefer
  leaving `FxGlitchOn` alone if it too has already been explicitly set by the user (avoid
  clobbering a deliberate choice on the surviving key with the retiring one).
- `GlitchIntensity` (0-1, default 0.85) -> `FxGlitchAmt` (0-2, default 1): copy the value
  through as-is (it is within `FxGlitchAmt`'s range) rather than rescaling; the ranges
  overlap enough that a straight copy preserves the felt severity closely.
- `GlitchDuration` (0.1-4, default 1.1) -> `FxGlitchDuration` (same range/default): direct
  copy, no rescale.
- KNOWN INTERACTION (not a bug to migrate, just worth flagging): `FxGlitchAmt` is now read
  BOTH as `HudGlitch`'s global envelope ceiling AND (unchanged, per-element tri-state
  resolver) as the fallback "how much THIS panel tears" weight for elements at Inherit —
  so for the common case (no per-element override) turning the global strength up amplifies
  the visible tear super-linearly (roughly squared) rather than linearly. Explicitly asked
  for by the merge design (single dial for the whole family); flagged here in case a
  play-tester finds "Glitch strength 2.0" excessive and asks for a rebalance.

**Total: 3 keys (2 delete-and-fold-into-existing-key, 1 delete-and-move-to-new-key).**

### Also relevant to the migration author
- Per-profile THEME snapshots (`UI/Hud/HudTheme.cs`) store these globals as
  `cfg:<FieldName>` entries inside each profile XML. `HudTheme.Apply` iterates the
  CURRENT `HudConfig` fields and looks each up in the snapshot map, so stale
  `cfg:ShowVitals` / `cfg:TopBarHeight` / ... entries in existing profiles are simply
  never read — version-tolerant by construction, no profile migration needed. New
  snapshots stop writing them.
- `FxBloomFineDetail` was NOT removed: `HudBloomFx` still reads it whenever
  `FxBloomRes == -1`, which is the shipped default.

## 0.9.2.5 Wave B / task B4 — ConfigMigration step 1 -> 2, plus two long-dead keys

Implemented `ApplyStep(1 -> 2)` in `Core/ConfigMigration.cs`: deletes every orphaned key
below, and folds the three glitch renames onto their surviving tri-state keys (only
copying a value across when the OLD key was genuinely present, and — for the master —
only when the surviving `FxGlitchOn` still holds its own default, per B3's migration
notes above). `ConfigMigration.CurrentVersion` bumped 1 -> 2.

### "9. The Grid" (UIAConfig) — one more long-dead key, in scope per B4's directive
Bind comment self-described "DEPRECATED - no longer used"; zero readers anywhere
(`GridModel.ActiveMode` is hard-wired to `Grid`, the old Nested tree renderer and its
title-bar/F10 escape hatches are gone). Not part of Wave B's own render-path deletions —
folded in here because it is the same class of dead key and B4 was asked to sweep it.

9. The Grid | DisplayMode | delete

(`RadialIconScale`/`IconScale` — the other key B4 was asked to check — was ALREADY
recorded and deleted under task B2 above; nothing further needed there.)

**Total: 1 additional key.**

### IMPLEMENTATION NOTE for whoever touches this step next: `OrphanedEntries` is PRIVATE here
BepInEx's own `ConfigFile.OrphanedEntries` — the dictionary that makes `Save()` silently
write a removed key straight back out forever unless something strips it — is a **private**
property on the exact `BepInEx.dll` this game/project ships (verified by reflecting the
get-accessor's IL attributes: `Private`, not the public one upstream BepInEx documents).
Calling `cfg.OrphanedEntries` directly is a compile error (CS1061) against this project's
reference. `ConfigMigration` reaches it via a small cached reflection helper
(`GetOrphanedEntries`, `BindingFlags.NonPublic | BindingFlags.Instance`) instead — the CLR
itself does not restrict this for a full-trust in-process mod host, so a removal through it
is indistinguishable from what the (elsewhere-public) API would have done. Fail-soft: if a
future BepInEx rebuild ever renames/removes the member, the helper returns null and the
step just stops deleting orphans rather than throwing. If you add more deletes in a later
step, reuse `RemoveOrphaned`/`TryReadOrphaned` — don't re-derive this from scratch.

## Grand total across all of Wave B + B4: 38 keys deleted, 3 of them folded into existing
## survivors instead of a bare delete (2) or a new bind (1) — see the counts above per task.
7. HUD | HardcoreGating | delete — orphaned when HudOverlayFeature died (live diegetics = HudConfig.DiegeticTiers); removed by the orchestrator post-B5
