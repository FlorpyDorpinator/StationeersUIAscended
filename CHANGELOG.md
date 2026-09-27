# Changelog — Stationeers UI Ascended

All notable changes to the mod. Detailed engineering write-ups live in `Changes Reports/`.

## 1.0.0 -- 2026-09-26 -- UI ASCENDED 1.0

FlorpyDorp named this release 1.0 on 2026-09-26; it replaces the planned "0.9.8.0". Engineering
detail: the 2026-09-25/26 Changes Reports - "Triage fix wave 1", "0.9.8.0 SmartStow + F10
overhaul (orchestrated wave)" and its agent reports, the "Simple SmartStow" pair, the Feedback
pipeline reports and "Suggestions-Bugs F10 tab", and "Remove VitalsTip hover diagnostic".
Everything below except fix wave 1 was uncommitted and largely play-untested when this entry
was written - see `Documentation/0.9.8.0/Play-Test-SIMPLE.md` / `-FULL.md`.

- **Simple SmartStow is the new default**: each item remembers the exact bag and slot it was
  last placed in (per world, found by identity at any nesting depth) and G returns it there.
  A manual move re-homes it; G/sort/swap never steal a home. Existing Bag Profile users stay
  in **Complex** mode (ConfigMigration v4 -> v5, evidence-based) with a one-time notice.
- **F10 Control Center rebuilt (Kit v2)**: 1450x950 window, manila folder tabs, title-bar
  settings search, remembered last tab, per-section "More options" instead of the global
  Simple/Advanced toggle. "HUD Themes" -> **UI Themes**; the Radial tab gains every wheel
  colour/glass knob; Storage -> **SmartStow** with the three-column Organizer (Storage
  Layouts / Bags / Bag Profiles), Routing, Return Home and Universal Inventory pages.
  "Stow Profiles" -> "Storage Layouts"; shipped "Stationpedia Ascended" -> "Ascended"
  (edits preserved). Equipped tool belts and jetpacks accept Bag Profiles (working slots
  protected).
- **In-game feedback**: new F10 **Suggestions/Bugs** tab and the `uiafeedback` console
  command. Reports are saved to an outbox first, then sent to the feedback relay
  (FlorpyDorp's server behind a Cloudflare Tunnel), filed as GitHub issues and planned by a
  Claude triage bot; players see status (Received / Under review / Fixed in / Closed).
  Anonymous unless a contact is given. New `[Feedback]` cfg section (Enabled, RelayUrl -
  empty = built-in relay).
- **Wheels** (fix wave 1): action word plate (TAKE/EQUIP/SWAP/OPEN/STOW) following the
  hovered wedge; unified close = drop at feet (never ejects world-container items); curved
  theme-AUTO hint ring with honest wording; bold names, 2x chevron, "Recent item" tag; the 6
  key opens the full MMB ring (The Hub + Q change belt) and RMB from the belt chooser returns
  to its ring; vanilla-hidden built-in slots stay hidden (no phantom cable-coil storage).
- **Universal Inventory** (fix wave 1): drag into world containers/device slots (also from a
  hand box) with vanilla's placement box; Shift+drag moves every stack of a type; compact
  square buttons incl. choose-a-number split (host/SP); popup focus fixed; rescue cells for
  items trapped in stacks.
- **HUD**: new Rangefinder element (m/ft; not in the shipped themes); shipped themes' glitch
  threshold 27% -> 10% (with a load-time fix for installed copies); removed the 0.9.7.x
  "VitalsTip diag" log spam.
- **Under the hood**: per-save files keyed to the world ID (fixes MP clients sharing an
  "unsaved" file across servers), atomic per-save writes; verified against game build 27798
  back to 24790.
- **Release text**: About.xml description/in-game description rewritten (four themes, no
  tofu-prone characters); About.xml ChangeLog trimmed to 1.0.0 + 0.9.7.4 + 0.9.7.3 for the
  8000-char cap (the 0.9.7.4 line now correctly says Shift+1-6); new Workshop description,
  Steam guide and Reddit/Discord announcements in `Documentation/Launch/`.
- **New 19-lesson in-game tutorial** (FlorpyDorp: ships live in 1.0): a Welcome card
  offers a guided tour (overview cards + every lesson in order) or just-in-time lessons;
  F10 > Guide lists every lesson with Watch / Try it / Skip / Restart; `uiatutorial`
  console command. Replaces the 0.9.7.0 15-step coach. Engineering detail: the 2026-09-26
  tutorial Changes Reports.

## 0.9.7.4 Experimental -- 2026-08-10 -- FOUR SHIPPED THEMES + PLAY-TEST ROUND 4

Engineering detail: the five 2026-08-10 Changes Reports.

- **Four shipped themes at their newest masters**: Stationeers Blue, Stationeers Blue
  Minimalist, Zirillian Red, Pure HUD — copied byte-exact from FlorpyDorp's live config
  (the old repo copies of Blue/Pure HUD carried 30 dead pre-Wave-B keys), `ShippedProfiles.cs`
  embeds regenerated for all four and proven by inverse-transform byte compare. Previews
  normalized to 800x450 (zip ~1.9 MB smaller). Shipped themes are READ-ONLY in F9/F10
  (duplicate to edit); the new `uiadev` console command unlocks authoring mode. Packaging now
  stages HudProfiles from an explicit four-name allowlist — a stray theme in the folder can
  never ride into the zip again.
- **1-6 opens containers in-grid by default**: the equipment keys reveal the worn container's
  region inside the Universal Inventory (repeat press toggles); a container the player pinned
  into its own window stays pinned — the key focuses that window instead.
- **Alert glow**: red criticals no longer wash out to white/pink at high brightness gain on
  older profiles (fallback resolution), and the pulse stays visible with F9 open so it can be
  tuned live.
- **F9**: toolbar Pause button (single-player, shared GamePause latch, auto-released on editor
  close) and the ImGui editor + element popup lifted above the mod's own UGUI HUD elements —
  but still below the game's own modal ImGui level.
- **Config-tree resilience**: full audit of every store against a missing/deleted config
  folder (launch AND mid-session); the broken write paths fixed; new non-destructive
  F10 → HUD → Maintenance → "Repair config folders" button.

## 0.9.7.3 Experimental -- 2026-08-06 -- PLAY-TEST ROUND 3

Engineering detail: the four 2026-08-06 b/c/d/e Changes Reports.

- **Toolbelt wedge bindings are sticky.** Swapping a tool from the MMB wheel into an occupied
  hand no longer dumps the hand tool into the taken tool's wedge and steal its binding — the
  displaced tool returns to its OWN bound wedge (click and drag paths both). Bindings move ONLY
  on explicit mouse drags (wedge-over-wedge = the two tools exchange homes; drag from a bag onto
  a wedge = that wedge rebinds); every mechanical landing (swap displacement, keyboard stow,
  SmartStow) is seed-only and can never overwrite an existing home. Implemented as a one-shot
  drag-intent handshake between the drag dispatchers and the Slot.Take observer, with a
  deliberate documented two-message swap+relocate (each half independently server-gated).
- **Smart-stow no longer strands the build hologram.** Vanilla's only build-mode exit is
  CancelPlacement, called solely from its own input paths — so ANY hand-emptying route (ours and
  vanilla's own G-branch alike) left a ghost hologram. Now every mod funnel that empties the
  active hand cancels placement, plus a postfix on InventoryManager.SmartStow covering vanilla's
  fallthrough. Host/SP keeps the mode when the stow actually failed (full inventory).
- **Invertible Shift semantics** (play-tester request): F10 → Radials → Behaviour → "Holding
  Shift keeps the wheel open after an action" (default ON = today's behaviour). OFF: wheels stay
  open after every action and Shift closes them. One decision property (`KeepOpenAfterAction`);
  the search panel follows; hold-mode wheels and parking unchanged. A personal input preference —
  deliberately NOT part of the radial theme family.
- **F2 tips panel layering**: vanilla's helper-hints panel now always draws above the mod's UI
  (HUD, wheels, Universal Inventory, pinned windows) via a tightly-scoped nested override canvas
  (+ its own raycaster so it stays clickable), and drops back to vanilla's natural layering the
  moment a vanilla full-attention menu (Esc, Stationpedia, console) is front. Type resolved by
  reflection; teardown restores vanilla exactly.

## 0.9.7.2 Experimental -- 2026-08-06 -- COMPASS HOTFIX

0.9.7.1's compass wrap-seam fix over-corrected: it derived cardinal marks as "every Nth tick"
with N walked down until it divided the ticks-per-turn — but the shipped 14.597° spacing snaps
to 25 ticks/turn (5²), so N collapsed 4→3→2→1 and EVERY tick became a nearest-45°-named letter
("NW NWN N N NE NE NE"). Letters now render on their own lattice — the eight true compass
bearings, whose 45° spacing divides 360 by construction — fully decoupled from the tick-spacing
knob (now a pure density control, still snapped to a 360-divisor so the seam stays fixed).
`cardinalDeg` selects all eight points (< 67.5) vs the four majors; minor ticks crowding a shown
cardinal are skipped. Letter misplacement/spam is now structurally impossible: no tunable value
participates in letter placement.

## 0.9.7.1 Experimental -- 2026-08-04 -- CRITICAL PUBLIC-BRANCH FIX + PLAY-TEST ROUND

Engineering detail: `Changes Reports/2026-08-03 - Public-branch crash fix...` and
`2026-08-04 - Play-test round 2...`.

- **CRITICAL — public-branch crash loop fixed.** The 2026-07-28 beta game update renamed
  `Slot.SpecificTypePrefabHash` (int) to `SpecificTypePrefabHashes` (int[]); 0.9.7.0 compiled
  against the beta shape, so DEFAULT-branch clients threw `MissingFieldException` every frame the
  Universal Inventory enumerated — the circuit breaker stood the whole per-frame body down in 5s
  cycles, killing the Grid and starving radial content (the "cardboard box won't open" and
  "radial menu missing" playtester reports). The slot prefab-lock field is now resolved by cached
  reflection, whichever shape the running game has, fail-open. One build runs on both branches.
- **Body bags are not storage** (design ruling): the bag's three slots are the corpse's organs
  (Brain/Lungs/Stomach) and are now invisible to every mod surface — manage radial, bag radial,
  Ctrl-bind, Universal Inventory (Grid/Tree/pinned), search and SmartStow routing — via one shared
  `ContentsOffLimits` predicate. Vanilla paths (cryotube revival) untouched; the after-death
  cardboard box remains full storage.
- **Compass wrap-seam fixed**: ticks were laid on an unwrapped lattice, so a tick spacing that
  does not divide 360 (the shipped themes' 14.597°) shifted the whole strip by `360 % spacing`
  (~9.7° — the reported "N is 10° off from one side"). The step now snaps to the nearest exact
  divisor of 360 at render time (fixes custom profiles too, which never auto-upgrade), the
  cardinal pattern and letters are wrap-consistent, and the readout shows 0°, never 360°.
- **"Exit To Desktop" confirm dialog unclickable after leaving a dedicated server**: vanilla's
  item tooltip disables a shared alert-layer `GraphicRaycaster` and only `ClearToolTip` re-enables
  it; a HUD-slot tooltip left up at world unload could never be cleared (its tick sits below the
  HUD stand-down return), leaving that layer's clicks dead at the main menu. The tooltip is now
  force-released (clear + explicit raycaster re-enable) on the stand-down edge and on the
  invalid-snapshot path.

## 0.9.7.0 Experimental -- 2026-08-02 -- SMARTSTOW PROFILES REBUILT + F9 STYLE PARITY

Two concurrent work lines land together: a ground-up rebuild of bag-routing profiles
(SmartStow), and a five-phase pass that makes every per-element style page in F9 render from
the exact same table as the F9 global menu. Plus a parallel session's device windows, first-run
tutorial and Universal Inventory polish. Engineering detail across `Changes Reports/` 2026-07-25
through 2026-08-02 (`SmartStow B1`-`B5`/beautification/final-review, `Style parity Phase 0`-`5`,
and the standalone reports below).

### SmartStow Profiles, rebuilt (the headline feature)
- **Stow Profiles**: a named folder of **Bag Profiles** (item-routing rules). Exactly one Stow
  Profile is active at a time; you can browse an inactive one and copy/move individual Bag
  Profiles into the active one without switching. **Loadouts are retired** from the UI (data kept
  on disk, hide-never-destroy) -- a Stow Profile's optional bag mapping replaces them.
- **F10 Storage is now four sub-tabs**: **Bags** (3-across card grid, live `GetThumbnail()`
  previews -- a painted bag shows its real colour -- occurrence ordinal, slot-fill count, a profile
  dropdown, a "never stow into this" exclude switch and an inline rename box; an Advanced list
  view is available), **Bag Profiles** (the rule editor: item / category / slot-class / new
  **UIA-class** rules, each with a click-to-cycle priority chip, plus "Create recommended
  profiles"), **Stow Profiles** (the set manager: browse/rename/duplicate/delete, copy or move a
  profile between sets, export/import, "Restore shipped Stow Profiles"), and **Settings**
  (Universal Inventory sizing/scroll options, the Smart Stow chain toggles, the dry-run test box).
- **Only real containers are assignable**: backpacks, mining belts/backpacks, boxes and crates --
  not tool belts, jetpacks, suits, tools-with-slots or packaging.
- **Assigning a profile can rename the bag to match** ("labeller-funnel" rename) -- the same
  server-authoritative rename path the in-game Labeller uses, so it is MP-safe and every client
  sees it; on by default, toggleable, and never auto-reverts on unassign (use the manual Rename
  button for that).
- **New `UIAClassRule`**: profiles can now match on the mod's own 22-class `UIAClass` taxonomy
  (`Core/UIASortingData.g.cs`), not just the game's coarse 11-value `SortingClass` or the 44-value
  `Slot.Class` -- closes the gap that made hand-curated sets brittle against new/modded items.
- **Four shipped Stow Profile sets**: **By Printer** (14 per-fabricator profiles + a catch-all,
  570 rules -- can be regenerated from a live world's recipe tables), **By Category** (12
  profiles, one per `SortingClass` + Electronics + a catch-all, 21 rules), **Stationpedia
  Ascended** (FlorpyDorp's own 9-bag layout: Paints / Materials / Frames and Walls / Ingots and
  Ores / Electronics / Liquids and Gases / Cables and Pipes / Canisters / Misc, 108 rules), and
  **Starter** (one "Starter Haul" bag, 4 rules, mapped onto your first backpack). Seeded once to
  disk (`.shipped`-marked); a Restore button brings the originals back without touching your edits.
- **Share codes**: `UIAP1-F-<base64url>` -- a self-contained, Deflate-compressed export of an
  entire Stow Profile set (Starter ~220 chars, By Category ~530, Stationpedia Ascended ~1400, By
  Printer ~6000), shown with an 8-character Crockford-Base32 fingerprint for verbal confirmation.
  Export copies to the clipboard and saves a `.txt`; Import reads a pasted code (or the clipboard)
  and always creates a **new** Stow Profile, never overwriting one you have. A plain `.xml`
  dropped into the `StowProfiles/` folder still works with no import step, same as before.
- **Profile and set CRUD finally exists in F10**: rename / duplicate / delete for both Bag
  Profiles and Stow Profiles (`BagProfileStore.RenameProfile` had zero callers before this).
- **Final-review hardening (10 fixes)**: a scroll-wheel-over-a-list infinite-recursion **crash**
  fixed before it shipped broadly; click-dragging inside a list no longer snaps/jumps instead of
  scrolling; switching sub-tabs no longer leaves an armed Delete button live; a failed cross-set
  move no longer falsely reports success or duplicates the profile; re-assigning an
  already-correctly-labelled bag no longer spams redundant rename network messages; duplicating a
  14-profile set is now one file write instead of fourteen; share-code import/export counts are
  accurate.
- **Terminology, to stop new and old features colliding**: F10's old "Profiles" tab (HUD styling)
  is renamed **"HUD Themes"**; "the Grid" is renamed **"Universal Inventory"** everywhere in F9/F10
  (the F9 design-canvas alignment grid is a separate, unrelated feature and was left alone).
- New `hudfx` / `stowprofiles` console diagnostics for troubleshooting either system.
  (`Changes Reports/2026-08-01 - SmartStow B1 ...`, `... B2 ...`, `2026-08-02 - SmartStow B3 ...`,
  `... B4 ...`, `... B5 ...`, `... beautification ...`, `... final review fixes ...`)

### Per-element style parity in F9 (Phases 0-5)
- **Root cause fixed**: unchecking "Follow global style" on an element used to unpredictably
  strip its frost, chromatic fringe, glass rim and halo. Un-following now preserves exactly what
  is on screen (seed-on-separation), and F9's "shared global" rows (backdrop darkening, tint,
  bloom, alerts) show live real values with jump-to-F9 buttons instead of being unreachable prose.
- **The element popup now renders from the same table as the F9 global menu** (`HudStyleFx`
  registry) -- captions and ranges can no longer drift into 2-3 different names for the same
  setting across menus (a few were consolidated/renamed as a result; border width's max dropped
  from 8 to 6 to match the global slider).
- **Five independently-followable categories** replace the old all-or-nothing checkbox: Theme /
  Glass / Edges / Glow / Transitions, each with its own "follow global" toggle -- unfollowing one
  changes nothing on screen until you actually edit it. Fixes: unfollowing Theme on the
  portrait/body-doll ring no longer disappears its border; unfollowing Transitions no longer
  reveals previously-hidden dormant power-transition settings; the "ALL follow global" bulk
  buttons no longer silently re-enable a death-collapse effect you had turned off per-element.
  Transitions itself is simplified from a tri-state to plain on/off.
- **Inherit style from another element**: a category can be set to live-track a donor element you
  pick by clicking it on the HUD (edits to the donor ripple through immediately), plus a one-shot
  "Copy from..." for a static copy. Illegal picks (inheriting from something that itself inherits)
  are rejected in the UI; deleting a donor falls back to global style gracefully.
- **"Flatten ALL boxes"/"Make flat" no longer permanently kills the power-on dissolve reveal** on
  every box it touches (a long-standing side effect), and flattening a drawn line now correctly
  kills its edge-light too. Shipped themes (Stationeers Blue, Pure HUD) re-exported at Schema 16
  so fresh installs load with no first-launch repair messages.
  (`Changes Reports/2026-07-26 - Style parity Phase 0 ...` through `2026-07-27 - Style parity
  Phase 5 ...`)

### Cut (chamfered) corners
- **New corner style** alongside rounded -- global or per-element, per suit tier, travels with the
  theme. Took three follow-up passes to reach full parity with rounded corners: the initial X-crease
  and broken iridescence in the mesh geometry, then full glass richness (frost/chroma/halo/shine)
  on the SDF renderer (**ABI 3** -- needs a full game restart, not just F6, to pick up the updated
  shader bundle; F9 shows "READY (ABI 3)" once active), then the halo "X" artifact root-caused to a
  true Minkowski corner offset. A theme-switch "absent key bleed" bug found along the way (a
  setting like corner style could leak from the previous profile instead of resetting) is fixed for
  every theme knob going forward.
  (`Changes Reports/2026-07-26 - Cut (chamfered) corner style ...` and the three follow-up reports)

### Tooltips, vitals & inventory QoL
- **Detailed vitals tooltips**: hovering the HUD vitals panel (or the vanilla Player Stats panel)
  shows exact mood/hygiene percentages and per-minute trend rates, matching vanilla's own
  tooltip styling. New F10 toggle, on by default. Also fixes the hidden vanilla vitals panel
  lingering as an invisible tooltip trap at its old screen location.
- **Vitals Cognition row now matches vanilla** -- it only appears when actually stunned/concussed
  (showing the real stun percentage), not merely from low oxygen/pressure.
- **Item tooltips always render on top** of the Universal Inventory and pinned/nested bag windows
  (were drawing behind them), and are shown again when hovering items inside those windows at all
  (a QoL fix restored a dead code path).
- Universal Inventory profile-dropdown popup opacity is now an F9 slider (was fixed and
  unreadably transparent); mouse-mode click on a 1-6 equipment slot reliably opens its pinned
  window again.
  (`Changes Reports/2026-07-26 - Detailed vitals tooltips ...`, `... Vitals cognition matches
  vanilla ...`, `... Tooltips always on top ...`, `... QoL - inventory tooltips ...`)

### From the parallel work line
- **Device internals open in our own themed pinned window**, not the vanilla popup -- clicking a
  device (mouse freed) or pressing R on a scroll-selected one now behaves like tearing out a bag,
  including devices nested inside bags (previously often failed to open there). The window also
  shows the device's real buttons/switches (lock/arm, valve, on/off) as themed clickable controls,
  so button-only devices with no slots (a demo charge) get a window too; plain inert items still
  go straight to hand.
- **First-run tutorial coach**: a 15-step, animated-demo walkthrough that opens automatically for
  a new player (auto-pausing single-player), replayable via `uiatutorial` or a Guide-tab button.
  New **pause button** in the F10 header and on the coach itself (single-player only; disabled
  under multiplayer or an existing vanilla pause). New in-game **Designer Handbook** -- a 17-page
  illustrated F9 guide, viewable as an in-game flip-book or opened as a real PDF.
- **A real scrollbar** for the Universal Inventory and pinned windows -- auto-hides when content
  fits, draggable, resizes with content; width/colour/opacity are F9 knobs that travel with the
  theme.
- Fixes: dragging an item **out** of a hand slot no longer silently fails on anything but an
  instant flick-drag (a watchdog was eating the release edge); the Universal Inventory key (`B`)
  no longer opens-then-instantly-closes with the mouse-modifier held; edge glow/edge energy
  turning OFF now actually clears the leftover static rim on the compass, equipment columns,
  vitals panel and bare-senses readout.
- F10 profile cards show much larger previews (~73% of the card vs ~53% before); added a preview
  image for the upcoming "Zirillian Red" theme (preview only -- the theme itself isn't shipped yet).
  (`Changes Reports/2026-08-01 - First-run tutorial coach ...`, `... Hand-box drag-out ...`,
  `2026-08-01b - Grid key opens then instantly closes ...`, `2026-08-01c - Scroll bar for the
  Universal Inventory ...`, `2026-08-02 - Device internals ...`, `... Device windows show their
  vanilla switches ...`, `2026-07-26 - F10 profile cards bigger previews ...`)

### Radial simplification (post-0.9.2.5 play-test round)
- **The Hub is now the only interaction model** -- the Option A/D control-scheme choice and the
  four per-wheel on/off toggles (toolbelt/tool/bag/equipment-key) are removed; radials are core
  functionality now, not an opt-in.
- **"Flick to commit" is removed** -- it could fire a wedge without the ring ever appearing, and
  occasionally swallowed a real MMB press.
- Bound-tool label setting simplified to a plain show/hide toggle (always curved). Universal
  Inventory settings moved from the F10 Radials tab to the Storage tab. F10's HUD tab "Look"
  section is reduced to a note + an "Open the HUD Designer (F9)" button (the sliders are still
  fully live in F9); the "Visor-edge vignette" toggle is removed from F10 (the setting itself is
  untouched).
- **Portrait ring root-caused**: it now renders a correctly lit edge-glass effect when following
  the global style (a `CircleGraphic` hairline bug, two earlier attempts superseded); the
  alarm-pulse recolour refreshes reliably. Body-doll "Outline width" slider restored.
  (`Changes Reports/2026-07-25 - Wave G F10 alterations ...`)

### Other fixes
- **World-item drops swap like vanilla**: dragging a ground item onto an occupied radial wedge,
  HUD hand/equipment box or world slot now swaps the occupant out (with merge/insert where
  applicable) instead of doing nothing -- two rounds, closing one remaining occupied-wedge case.
- **F9 authors the powered HUD**: opening F9 while not wearing a powered suit used to silently
  preview the stripped-down Bare HUD; F9 now defaults to previewing Suited, and editing is immune
  to your real battery level (test buttons still preview transitions on demand).
- **Owned edge-light now respects the edge masters when they are OFF** (previously stayed lit
  regardless).
- **Game-update compatibility**: `Slot.SpecificTypePrefabHash` became
  `Slot.SpecificTypePrefabHashes` (plural) in a 2026-07-28 game update; updated to match.
- Freeform pen-shape borders no longer spike past a sharp tip and self-cross; sharp corners bevel
  cleanly. Moodlet dashboard gained a vertical/horizontal layout toggle (F9); moodlet "words" mode
  no longer shows raw unresolved localization tags.
  (`Changes Reports/2026-07-25 - Freeform pen-shape border spike ...`, `... Moodlet dashboard
  vertical-horizontal toggle ...`, `... Moodlet words mode strips unresolved localization tags
  ...`, `2026-07-26 - World-item drops swap like vanilla ...`, `2026-07-26 - F9 authors the
  powered HUD ...`, `2026-08-01 - Game update fix ...`, `2026-08-01 - Edge glass persists after
  turning effects off ...`)

## 0.9.2.5 Experimental — 2026-07-25 — PER-SUIT STYLES, TOTAL THEMES + THE BIG CLEANUP

The release-prep build: a seven-report audit (`Documentation/Release Prep Reports/`) executed
as six waves (A–F), each with its own Changes Report. Engineering detail in the
`2026-07-25 - Wave *` reports; the runnable pre-push script is
`Documentation/Release Prep Reports/Pre-Release-Test-Checklist.md`.

### The headliner — per-tier styles that actually remember (Wave C)
- **Root cause of "bare/suited colours don't stick":** the old per-tier fork was a *sparse*
  `b_` overlay on one shared base — any un-forked key leaked between tiers, whole families
  (icon tint, drop cue, compass ticks, readout rows) were unforkable at all, and Global-styled
  elements forked colours only. Replaced by the opt-in **HudStyleSlot** model: per-element
  fork bitmask (default = shared, old profiles byte-identical), **seed-on-separation** (the
  fork gets a complete copy of the stored style — palette refs and follow-global sentinels,
  never frozen colours), copy-on-write protection, per-slot `styleSource`
  ("flat in bare, glassy in suit" is now expressible), Schema 14→15 with idempotent adoption
  of legacy profiles. Robot slot reserved in the data model.
- **Icon tint per element** (inherit global / own colour / off), per-tier capable; the
  Universal Inventory gets its own per-tier skin setting.
- Adversarially reviewed: 26 persistence-invariant checks passed (tier round-trip, profile
  round-trip, relaunch, hot reload, old-profile compatibility).

### Themes carry everything (Wave C)
- Radial visuals + hint bar (46-key include-list), the **GridTheme** (Universal Inventory) and
  the **F10 Control Center skin** now travel inside the profile theme (`radial:`/`grid:`/
  `menu:` families). Perf knobs (frost downsample/cadence, bloom res…) excluded by design.
- ConfigMigration **v3**: one-shot top-up stamps current globals into existing themed profiles
  — an updating player's look does not change. `TextAccent` gains a real identity (the radial
  accent orange, previously hardcoded) via ForceIfDefault.
- `Overlay/Theme.cs` constants became live palette-driven properties (toasts, item menus,
  radial accents retint with profiles).

### The big cleanup (Wave B)
- **Legacy render paths deleted** (~2,900 lines): the 0.1.0 ImGui HUD overlay, the pre-document
  fixed-panel HUD (document mode is now unconditional — it IS the mod), and the ImGui radial
  painter. Glitch systems unified onto the FX family (values carried over). Dead knobs pruned.
- ConfigMigration **v2** strips all 39 removed keys from player configs (BepInEx orphan
  handling via verified fail-soft reflection).

### Profiles & shipping (Wave E + A)
- **New / Duplicate / Rename / Delete / Restore-shipped** in F9 and the F10 Profiles tab, with
  guards (never delete the active profile; shipped-name warnings; two-step confirms).
- **Self-heal factories**: Stationeers Blue + Pure HUD embedded verbatim — a missing shipped
  file regenerates pristine and manifest-managed, never as player-owned starter junk.
- **`uiareset`** (print-then-confirm) + F10 "Restore shipped themes"; corrupt profiles are
  quarantined to `.broken.xml` instead of overwritten (Wave F).
- SyncShipped hardened (Wave A): zero-length SLP cfg counts as a fresh install; pristine
  re-adoption self-heals canonical-hash drift.

### The F9 designer, reorganized (Wave D + A)
- Effects: 7 sub-tabs (Glass / Edges / Glow / Bloom / Alerts / Transitions / Advanced) instead
  of a wall of open dropdowns; Theme: palette-first (9 semantic wheels, All colours,
  Typography & boxes). Plain-language labels (jargon demoted to hints); "experimental"/"legacy"
  dropped from things that aren't. Active tab + edit-tier (BARE/SUITED/ROBOT) clearly marked;
  header combos no longer truncate. Capability flags moved to widget-declared virtuals; inert
  universal knobs hidden per-widget. Zero binding changes (parity-audited).
- F10 fully matches Pure HUD (and every theme): the menu kit gained a real Border source +
  theme-derived dividers; the shipped Pure HUD palette was fixed (Wave A).

### Process & packaging (Wave F)
- `package.ps1`: version-agreement abort, staged-content audit, dirty-tree warning;
  `publish-steam.ps1` injects the changenote from About.xml at publish time.
- Workspace: `merge=union` trap removed, game DLLs + junk untracked, deprecated POC project
  deleted, `docs/` merged into `Documentation/`. (The full repo reset is deferred — see
  `Documentation/Release Prep Reports/to do/`.)
- CLAUDE.md: new standing rule — every new knob must be per-tier capable, travel with the
  theme (or be excluded as perf), and key changes ship a ConfigMigration step.

## 0.9.2.0 Experimental — 2026-07-24 — THEMES THAT TRAVEL + RADIAL/CURSOR POLISH

A play-test → fix round plus a HUD theming pass from a parallel work stream. Engineering
detail across several reports in `Changes Reports/` (2026-07-23 and 2026-07-24).

### Fixed
- **The cursor flicker in radials — real root cause.** The long-hunted arrow-pointer strobe
  was the "walk / jump / jetpack while a radial is open" feature (`RadialMovement`): its
  `HandleJump` / `MovementHandler` Harmony patches hid then re-showed `Cursor.visible` **every
  physics frame** merely to satisfy a `!Cursor.visible` gate that is only *reached* when the
  ascend/jetpack input is actually held. A direct sub-frame write, invisible to any
  end-of-frame sampler. Both patches now gate the toggle on the real input
  (`KeyManager.GetAscend() > 0.01` / `KeyManager.GetButton(KeyMap.Jetpack)`), so on the vast
  majority of frames the pointer is left completely alone. Fail-safe: if the input accessor
  throws, fall back to the old always-toggle so jump/jetpack never break.
  (`Changes Reports/2026-07-24 - Cursor flicker root cause ...`)
- **Cursor-block stomp race.** `CursorManager.BlockCursorRaycast` had eight independent UIA
  writers each on its own private flag; whichever released first stomped the others. Replaced
  by a single `CursorBlockArbiter` (named holds; flag true iff ≥1 hold; edge-driven;
  `Shutdown()` force-clears). (`Changes Reports/2026-07-23 - Cursor-block arbiter ...`)
- **Can't drag hand / 1-6 items into the Universal Inventory or pinned windows.** A fast
  flick-drop resolved its drop raycast before the receiving window's `GraphicRaycaster` was
  re-enabled that frame. `PrimeDragRaycasters()` now force-enables raycasters for the frame
  while a drag is in flight, before the release resolves.
- **Smart Stow (G) did nothing with a radial open.** The pass-through called the
  `InventoryWindowManager.SmartStow()` wrapper, which early-returns while the cursor is free.
  Now calls `InventoryManager.SmartStow(ActiveHandSlot)` directly + refreshes the wedge.
  (`Changes Reports/2026-07-24 - Smart Stow (G) works again ...`)
- **Split-to-ground spawned the items inside the player** (physics launch + self-damage) — now
  placed and launched clear. (`Changes Reports/2026-07-23 - Split-to-ground ...`)
- **A suit-power moodlet could stick lit** in the borrowed vanilla status strip (our brightness
  tint clobbered vanilla's fade). (`Changes Reports/2026-07-24 - Suit-power moodlet stuck ...`)

### Changed / added
- **Middle mouse now only closes a radial.** MMB over an item used to select it; it now closes
  the wheel (dumping any in-progress sort chips to the ground). (`RadialMenu.UpdateStickyOptionA`)
- **Vanilla-style placement box when dragging from a radial.** Dragging a chip/item from a
  radial toward a world device/locker/charger slot drives the game's own
  `CursorManager.SetSelection` cue (green place/swap, yellow merge, blue insert, red refused),
  matching vanilla. Read-only — the move still funnels through `ItemActions`. (`Core/WorldSlotCue.cs`)
- **Greyed (hands-full) radial items stay draggable.** A greyed item refuses a click (won't jump
  into a full hand) but can still be dragged out as an icon.
- **Per-profile themes.** Global colours, glass/glow effects and the radial palette now travel
  with the saved HUD profile (`UI/Hud/HudTheme.cs`, new). Shipped profiles curated to
  **Stationeers Blue** + **Pure HUD**; retired themes moved to `Old Themes/`.
  (`Changes Reports/2026-07-24 - Per-profile themes ...`, `... Curate shipped HUD profiles ...`)
- **Projected-lens HUD theming pass** — moodlet warning words, per-element colours, icon
  toggles, scan-lines, carried-item tint. (`Changes Reports/2026-07-24 - Projected-lens ...`)
- **Equipment damage bars** — a worn item taking damage shows a thin green→yellow→red health bar
  under its icon, tinted by the game's own damage gradient; the body damage-doll matches.
  (`Changes Reports/2026-07-23 - Equipment damage bar ...`)
- **`uiadiag` console diagnostic** (`uiadiag` / `uiadiag cursor`) — read-only cursor/drag trace;
  OFF by default. (`Core/CursorDiag.cs`, `Core/CursorDiagPatches.cs`)

## 0.9.1.1 Experimental — 2026-07-22 — STABILITY, DRAG POLISH + SIZE KNOBS

A play-test → fix round on top of 0.9.1.0. Engineering detail in
`Changes Reports/2026-07-22 - Bug-fix and polish round (...).md`.

### Fixed
- **Cursor freeze / OS-wide lag from hot-reload accumulation.** `OnDestroy` early-returned
  (`Instance != this`) on a reload, leaking canvases and leaving all 17 Harmony patches applied;
  each F6 stacked another set → FPS collapse. Teardown is now unconditional + idempotent; added a
  per-frame exception circuit breaker; `LoadOnStart=false` moves load off the boot frame (dev-only
  accumulation; a normal install never reloads).
- **Radial cursor flicker.** (a) one-frame open hitch → `MouseModeController.Check()` in
  `ModalScope.Open`. (b) world-hover-highlight flicker on move-to-wedge → the grid/pinned
  cursor-block updaters now yield to an open radial (single owner, no per-frame toggle).
- **Drag bridge.** Decoupled world-drop + box-grab from the HUD drop-zone availability gate
  (`ZoneAt` now uses alpha-free `CollectBoxGeometry`; `ZonesAvailable` kept only for the inbound
  false-drop guard). Fixed pinned-bag drag-out gating on the main window's interactive state.
- **Drag/menu canvas layering.** Dragged item always renders on top (new `DragGhostLayer`, order
  5250, incl. a mirror for the vanilla world ghost); pinned windows accept drops (added the
  `VanillaWorldDragLive()` cursor-block exception + raycasters forced on during any drag);
  inventory + pinned windows hide under vanilla blocking menus (ESC/pause, IC10 editor,
  Stationpedia) via `Guards.VanillaMenuWantsFront()` + an MP-client fix (`InventoryManager.InGameMenuOpen`).
- **Smart Stow into consumable containers.** Water-bottle bag / cereal box / burger-egg box /
  starter supplies package are no longer stow targets nor grid storage regions (one shared
  class-based `IsRealStorage` predicate; ore/mining bags verified safe).
- **Yellow pinned windows** — the nested canvas from the pin's raycaster now gets the SDF shader
  channels.

### Changed / added
- **Head-look-during-radial removed** (unreliable; cursor is now unconditionally free in radials).
- **F9 Sizes knobs added:** manila tab icon, sort button, close/X (chrome) button, and pinned
  window title icon + title text — all live-relayout wired.

## 0.9.1.0 Experimental — 2026-07-20 — THE UNIVERSAL INVENTORY + THE BELT WHEEL

The inventory half of the redesign lands: one window for everything you carry, bags you can pin out
onto the screen, and the tool radial grown into a real loadout system. Plus a long artifact hunt
through the HUD's glass and a large batch of designer/menu work.

### The Universal Inventory (new)
- **One window for everything you carry** (`B`): worn equipment, both hands' contents, and every bag
  nested inside them — recursively, however deep.
- **Flat grid layout**: each storage bag is a bordered block of one-item boxes with a trapezoidal
  **manila-folder tab**; a tool that merely *has* slots (drill, welder, tablet) stays a **single box**
  instead of unfolding. The storage-vs-tool test is decompile-verified (`is Tool` → box; `SlotType`
  Back/Belt/Suit/Uniform → region; any `Slot.Class.None`/`Ore` general slot → region) — vanilla makes
  no such distinction, it expands a drill exactly like a bag.
- **Pin a bag out**: drag its folder tab out of the window and it becomes its own movable/resizable
  window with a **shrink/restore** button (the game's own `ResizeShrinkIcon`, resolved live at
  runtime). `Shift`+`1`–`6` toggles that worn bag's window; mouse-mod + click a HUD equipment slot
  pins it. Pins persist per-save with their geometry.
- **Pins outlive the window**: `B` closes only the main window — pinned bags stay up and keep
  updating. The vanilla close-all (`` ` ``) is what puts everything away (keeping the pin records);
  a pin's own ✕/shrink is what truly unpins.
- **It never steals the mouse**: `B` opens with the cursor still **locked**, so head-look and movement
  keep working. The window registers no cursor-unlocking modal at all — you free the mouse with the
  vanilla mouse-modifier, and only then does it take clicks.
- **Item drag-drop** box→box (move / swap / merge), click-to-hand, right-click → that item's radial,
  and per-container **Sort** in each box's corner. Every mutation is one message through
  `ItemActions` with the occupant identity pinned at **drag start** and re-verified at execute time.
- **Resizable + movable with memory**: bottom-right grip (three diagonal lines) resizes, the title bar
  moves it, and both persist. The grid **re-wraps** as the width changes; an F10 slider sets box size
  and icons scale with it.
- **Inherits the global box theme** — colour, border width, corner radius, sheen, edge light and frost
  all resolve from your F9 globals (with per-value overrides), and the window is **click-editable in
  the F9 designer** like any box.
- A **"Nested" tree view** was built first; it is kept in the codebase but hidden and unreachable.

### The Belt Wheel (the MMB tool radial)
- Renamed from "Toolbelt". **`Q` swaps which tool belt you are wearing** — the belt *is* the loadout,
  so a second belt is a second kit, swapped in one gated `OnServer.SwapSlots`.
- **Tool home slots**: smart-stow returns each tool **type** to the slot it lives in, per belt, stored
  per save; occupied home → nearest free slot, which becomes the new home. Manual placement rebinds.
- **Stable wedge geometry** — a slot keeps its angle even when its tool is in your hand, with the
  bound tool's name ghosted in place (moved onto the hub arc with its own colour).
- **Flick-commit** (flick to a tool without the ring ever drawing) and **double-tap repeat-last**, both
  opt-in in F10; a second normal tap still closes.
- **Wedge sounds** (hover tick / commit / denied) using the game's own pooled UI sounds.
- **Hint bar**: fades per hint as you learn it, restyled to black curved glass, and author-able from
  the F10 radial editor.
- **Head-look hold** — hold MMB while a radial is open to look around without closing it.
- Radial value pills, hub text fit, value placement, RMB-back on satellites.
- **Anti-hotbar guard**: tool-into-hand wedges can never be key-bound (bags keep Ctrl+1–0, device
  settings keep letter binds).

### HUD glass — the halo/artifact hunt
- **Corner rays root-caused**: it was the *specular* light lobe, not geometry — halos now shape by a
  soft cosine lobe. Ray streaks fade with skirt depth; a frame-projected, low-passed wave replaced the
  old modulation.
- The **halo X crease** fix is ungated; **freeform pen shapes** get halos and corner fans.
- **Organic breathing + flowing SDF halo**; moving ripple on lines and shapes; **per-element ripple
  desync** (frequency + tempo); `RippleSmooth` honoured while flowing; extra-diffuse + organic scale.
- Box **end-fade** shape and border influence.

### Designer + menus
- **Pen tool reworked** to an Illustrator-style Bézier model, with point editing moved into the popup.
- **Resolution-independent HUD scaling**; the authoring resolution now travels with the profile.
- Off-screen element **self-heal**; **element inspector audit** (missing and lying controls); custom
  elements reach parity with the global Effects tab; stacked readout row heights exposed.
- **Bare senses overhaul** (layout, editable sense catalog, nested tabs); per-mode bare-suit visual
  overrides (Robot checkbox removed).
- **Status alert pulse** — suit warnings tint and breathe the HUD.
- **F10 Control Center** is real HUD glass (`PanelGraphic`), follows the F9 theme, is click-to-edit,
  matches the HUD panel fill, and keeps its scroll position; storage-tab dropdown collapse fixed.
- Style standardization — legacy paths regressed to the two-state contract.

### Inventory + interaction
- **Drag items out of the HUD hand and 1–6 boxes**, and drop a **world item into a HUD slot** (inbound
  drag) — vanilla's own drag lives on panels the mod hides, so this reimplements the gesture through
  the MP-safe funnel.
- **Cursor latch** (double-tap the mouse modifier).
- Damage alert, unpack boxes, radial toggle, stow priority.

### Fixes
- The bowtie **"X" artifact is gone from every inventory box** — root cause was `PanelGraphic`'s
  single-fan interior interpolating any interior gradient *radially* along corner→centre triangles;
  the dense interior-ring fill is now applied to every Grid surface.
- **Smart-stow flash root-caused** (an InventoryTweaks conflict) after several attempts; the leak and
  broken-X damage icons are the real vanilla art.
- The vanilla **controls-conflict banner** is gone (UIA keys un-register from the vanilla screen).
- **Dragging an item out of a HUD slot no longer opens/pins the inventory** — the pin now requires a
  genuine click (same slot, sub-threshold movement, no grab in the gesture).
- **Scrolling fixed** in the inventory window (the viewport had no raycast target, so the wheel
  produced no events over empty area); it is hover-gated and empty boxes drag-scroll.
- **TMP glyph rule**: displayed strings are ASCII-only — arrow/dingbat glyphs rendered as tofu boxes.
  Icons are drawn, not typed.
- Verified: vanilla's `InstantStop` (`B`) is a **dead binding** with no consumer, so the inventory
  hotkey does not conflict with it.

## 0.9.0.1 Experimental — 2026-07-16 — THE PEN, THE CONTROL CENTER + LIGHT YOU AIM

A design-tooling pass on top of 0.9.0: draw your own shapes, aim the light, and take every effect
per-element — plus a player-facing settings hub. Defaults are unchanged, so an untouched HUD looks
exactly like 0.9.0.

### The pen tool — draw your own glass
- **Pen tool → filled glass shapes**: click points to lay a path, close it into a `Shape` element.
  Every glass effect (sheen, edge light, glow, frost) applies to your own shapes.
- **Full pen editor**: drag anchors, **Bézier handles** for smooth curves, **corner points**, and
  insert/delete points — Adobe-style editing on a live HUD element.
- Fixed the F9 selection box so shapes/lines can actually be grabbed.

### UGUI "Control Center"
- A player-facing settings hub **alongside** (not replacing) the ImGui F9/F10 windows.
- **Master on/off** for the radial half and the HUD half, independently.
- **In-world hint bar + guide** that teaches the controls.
- **Key rebinding** — in our menu *and* the game's own Controls screen.
- HUD profiles front-and-centre; bag-profile setup GUI.

### Edge light you aim
- The border/line highlight is fully configurable: **colour**, **direction** (angle — spin it and the
  catch sweeps around the frame), **opposing-rim catch**, and **falloff** (tight catch ↔ broad wash).
  Borders, pen shapes and drawn lines all share the one key light.
- A box that zeroes its own edge light now **opts out of the global boost** (previously the global
  always relit it).
- Colour/angle/rim/falloff update **live** (folded into the layout hash — they're read inside the
  mesh, so a change now re-meshes); the tint reverts when Edge light is unchecked instead of sticking.

### Per-element everything
- **Edge fade** — a box's far ends dissolve into the visor (new `HudEdgeFade` vertex-alpha modifier),
  so a wide bar melts away instead of ending on a hard line.
- **Make flat** — one click strips a box's glass/glow/edge light to a plain bordered box; per element
  or **Flatten ALL boxes**.
- Per-element sliders (all `-1 = follow global`): glass sheen, glass edge light, **edge softness /
  AA**, border fade, soft edge, **glow out / in / width / diffuse**, **edge ripple / freq**.
- **"Follow global effects" is its own checkbox**, independent of colours — and separating anything
  from global now **freezes it at the current global value**, so nothing jumps; it stays what you were
  looking at until you change it.
- Ripple reworked: frequency floor **0.5 → 0.05** (a single slow light→dark sweep across a full-width
  bar) plus a **"Ripple gradient"** slider that smooths the layered noise into a clean sine gradient.

### Global defaults
- **Global glass sheen** + **glass edge light** defaults (per-element `-1` follows them), with
  "Make ALL elements follow global glass".
- **Global frost strength** slider — scales every element's frost at once.

### Bloom + fixes
- **Dynamic bloom**: anamorphic streak, pulse, state-reactive response, resolution control.
- Fixed the bright **"bowtie X"** artifact across glass panels (uniform concentric inset) + dense
  interior fix.
- Compass no longer breaks under curvature; F10 dropdown bug fixed and the menu restyled
  (grey/rounded); hairlines can render thinner than before.

## 0.9.0 Experimental — 2026-07-13 — EFFECTS + THE PROFILER

Three tiers of new HUD effects — every one togglable **globally** (F9 → "Effects (global)") and
**per element** (each element's popup) — plus a built-in profiler to prove none of it hurts the
game. Planned and adversarially verified against the codebase, TheRealBeef's shader mods, the
StationeersLaunchPad source, and Jackson's profiler (see `Documentation/UI Upgrade/Master-Plan-0.9.0.md`).

### Tier A — mesh effects (on by default)
- **Hairlines that fade**: lines below 1px render 1px wide and dim by coverage instead of
  vanishing (draw down to 0.05px; phone-wire AA).
- **Directional edge-light on drawn lines** — brighter where a stroke faces the key light,
  matching the panel borders (shared light direction).
- **Breathing pulse** (per-element opt-in) — a gentle glow oscillation with per-element phase
  offsets; drives the CanvasRenderer tint, never fights the animator/glitch channels.

### Tier B — shader effects (on when `uia_effects.bundle` is present; degrade to Tier A without it)
- **Shine sweep** — a light band that travels across panels on a configurable period.
- **Iridescent edges** — subtle thin-film shimmer (spectral_zucconi6, branchless).
- **Dissolve reveal** — panels "power on" behind a travelling bright frontier on boot.
- Shaders ship in an AssetBundle built from `Dev/UiaEffectsBundle` (own mini-project;
  `build-bundle.bat`); loaded fail-soft from the mod folder (`ModData.DirectoryPath`), never
  named `*.assets` (SLP auto-load safety), hot-reload-safe.

### Tier C — frosted glass (EXPERIMENTAL, off by default; Flat/Warp curvature only)
- Panels **blur, darken and tint the world behind them** — an OnRenderImage capture (pass-through
  safe) into a ¼-res dual-Kawase pyramid, re-blurred every N frames, one shared blur texture for
  every panel. Gray-leaning tint per FlorpyDorp; darkening/tint/throttle/downsample all config.
- Stands down automatically on curvature switch, world unload, disable, and hot reload.

### The profiler (Profilicus Universalis by JacksonTheMaster, vendored with permission)
- F9 → **Profiler** or console **`uiaprof [on|off|clear|save]`** — rolling 10s per-metric table
  (HUD phases, mesh rebuilds, blur dispatch, `Frame.Total`), Markdown snapshots to
  `BepInEx/config/StationeersUIMod/ProfilerSnapshots/`.
- **`uiaprof ab <effect>`** — measures an effect's real cost: toggles it ON/OFF around two
  captured windows and prints the ms/frame delta (tiera/tierb/frost/shine/edgelight/irid/
  chroma/pulse/dissolve).

### HUD BLOOM — elements light each other (added 2026-07-15)
- **True light bleed between elements**: any bright HUD pixel — borders, text, accent chips,
  glow halos — blooms onto its neighbours. The HUD renders through a RenderTexture in every
  curvature mode (flat/vertex-warp are routed onto the RT rig while bloom is on, pixel-1:1),
  a soft-knee bright-pass extracts the light, the dual-Kawase chain blurs it, and it composites
  additively back into the HUD image — so every presentation carries the glow consistently.
- Full control set in F9: **strength / threshold / soft knee / blur steps (1–5, reach doubles
  per step) / spread** (the *continuous* width fine-adjust between steps) / **fine-detail**
  checkbox (thin borders + drawn lines survive into the bloom) / **saturation** (white-hot →
  own hues → neon) / **tint** (hue-wheel picker).
- Frosted glass and bloom **coexist** on flat/vertex-warp. Default off; fail-soft without the
  shader bundle. Architecture verified against TheRealBeef's mods and the game's own bloom
  (research write-ups in `Changes Reports/`).

### The glow system, matured (2026-07-14/15 play-test rounds)
- **Inner + outer glow, separately dialable** — frames glow *into* the glass and *out* of it
  (the concept art's both-ways light), each 0–2, per-element overrides, shared width (to 160px)
  and a **diffuseness** slider (tight rim glow → wide soft haze).
- **Ripple overdrive** — the border shimmer now goes to 2.5: dark troughs clip to fully dark
  (the line visibly breaks up) and bright crests overshoot into the specular and the glow.
- A long artifact hunt, all fixed: Mach-banded halo falloff (8-stop C¹-smooth curve), radial
  spokes (ripple aliasing — mesh columns now sample the shimmer densely enough; the halo keeps
  only a fraction of it), corner rays (fan density scales with the glow skirt), inner-glow
  corner X-pattern (bands now miter on the corner bisector with zero double-draw, compressing
  their falloff instead of truncating it), and glow escaping through trapezoid slants (the
  miter now respects each corner's true interior angle).
- **Hand boxes and the 1–6 column receive every mesh effect** (they styled their glass by hand
  and silently skipped the new-effects push).

### Radial glass (F10, added 2026-07-15)
- The radial menus join the effects system: **frosted-glass wedges** (shares the HUD's blur —
  needs Tier C + flat/vertex-warp), plus **sheen** and **edge light** on wedge rims under the
  same key light as the panels. New "Effects (0.9.0)" section in the radial editor; the menu
  says why frost is inactive instead of silently doing nothing.

### Fixes & plumbing
- **Frost no longer X-rays the first-person helmet**: the backdrop capture moved from the main
  camera to the game's foreground (helmet) camera, so frosted panels blur the visor frame like
  everything else (the helmet is a separate camera's layer the main camera never draws).
- **Profiler measures memory now** (Dean Hall's critique answered): allocation KB/frame +
  gen0 collections in the live table, and `uiaprof ab` prints the allocation delta alongside
  ms/frame. Steady state: zero mesh rebuilds, ~0 added alloc.
- **Packaging**: releases no longer ship the dev hot-reload shim (it self-initialized the mod
  under SLP with an empty prefab list and the wrong cfg); settings migrate automatically to the
  new SLP config identity (`<ModID>` added). The inert `stationeersmods` marker file is gone.
- **Shipped HUD profiles now import on first run** (0.8.0 shipped them inert in the zip);
  the shipped default layout for the 0.9.0 play-test is **"Smaller Test"**.
- **Latent mode-C bug fixed**: elements added/duplicated in the F9 editor while in curvature
  mode C lost their world-space material (a guarded no-op re-apply) — now re-applied for real.
- Effect materials are exempt from the mode-C material swap (they carry their own ZTest clones);
  effect systems never touch borrowed vanilla graphics (marker-interface scoping).
- Dev-only: the shader bundle loads from memory, so it can be rebuilt while the game runs.
- Retired the dead `GlitchShader` config knob.

## 0.8.0 Alpha — 2026-07-13 — GLASSY + THE DESIGNER, DEEPER

The HUD designer from 0.7.0 grows up: a full **"Glassy"** dashboard redesign as the shipped
look, two new curvature modes, per-element effects and per-mode/per-tier layouts, a tunable
moodlet dashboard, curved line-work, and a long list of radial refinements.

### The Glassy HUD
- **New shipped default: "Glassy 4.0"** — a procedural-glass, car-dashboard redesign. Glass
  sheen/edge-light rendering on boxes; the game's own ramp-bar art on the gauges; a de-blued
  restyle. (The whole Glassy 2.0 → 4.0 line ships in this version; older shipped defaults
  auto-upgrade, your custom profiles are left untouched.)
- **Moodlet dashboard**: vanilla's real status strip relocated into the bar as centered chips
  that **bend onto the visor curve**, keep their hover tooltips, and hide the vanilla strip.
  New **transparency** and **brightness** sliders per moodlet dashboard.

### Curvature
- **Mode C walk-swim fixed** — the curved world-space canvas now head-locks by parenting to the
  camera, so it no longer drifts as you move.
- **New Mode D (Curved RT)** — Mode C's curved look *without* the swim, rendered to a fixed
  RenderTexture. Both C and D are selectable.
- **Per-curvature-mode placement** — each element remembers its own position/size per mode
  (A/B/C/D); switching curvature recalls that mode's arrangement.

### The designer (F9), deeper
- **Per-element effects** — Death collapse / Glitch tear / Warp each have a per-element on/off
  + 0–2× strength slider (turn the top bar's CRT collapse off, etc.). Glitch moved into F9.
- **Per-tier layout** — one element can sit in a different spot/size in bare vs suit mode
  (no more duplicating an element per mode).
- **Global box colours** — retint every box from one place; per-element "Follow global colours".
- **Z order re-layers live** — drag the Z slider and elements re-stack immediately (e.g. a box
  behind the moodlets).
- **Curved line-work** — the draw tool's Polyline gets a **Smooth (curved)** toggle: a spline
  through your points, with a smoothness control.
- Readout labels **wrap** to their box; **Icon-scale** slider; **"Show grid"** snap overlay;
  boxes get **trapezoid** top/bottom insets for angled ends.

### Bare senses & vitals
- **Bare senses**: rearrange / add / remove any of **8** felt-senses per row (now including
  **Consciousness** and **Toilet**); health reads as general **wellness** (UNWELL → AILING →
  DYING); hunger/thirst now trigger at the same thresholds as the vitals card.
- **Vitals panel**: "Row lines" and "Row icons" toggles.
- **Body doll**: edit the PNG doll's healthy / warning / critical damage colours from its popup.

### Radials
- One or two device settings show **inline** (no "Settings" wedge for a single toggle).
- **Split-stack** wedges (Split one / half / count); live **stats on swap/replace/eject** wedges.
- Swipe chevron only appears on wedges that actually open a child radial.
- **Sorting-class wedge colours**; hold-mode drag parity; drag a chip onto a hand/1-6 box.
- **Wedge hotkeys**: bind a device setting to a key from its wedge — now it won't shadow any
  game keybind (or Ctrl / C), and pressing a wedge's own letter again unbinds it.

### Effects & fixes
- Optional **power-transition glitch**: a static/distortion pass on suit death/boot, reusing the
  game's own CameraFilterPack shaders, tunable.
- Fixed a crash from two moodlet / damage-doll widgets fighting over one vanilla object
  (`StatusUpdates` NRE spam); borrowed vanilla UI is now always restored before destroy.

### Console
- **`finddead`** — locate dead-player body bags near you, with coordinates.
- **`findlargebox`** — locate large cardboard boxes near you, with coordinates + name.

### Bundled
- Your saved HUD profiles are versioned in the repo under `HudProfiles/` (share/restore by
  dropping them into `config/StationeersUIMod/HudProfiles/`).

## 0.7.0 Alpha — 2026-07-11 — THE HUD DESIGNER

The HUD stops being ours and becomes YOURS. Everything on screen is now an element in a
**layout document** you edit in-game with F9 — built across five reviewed phases (opus
implementation fleets + per-phase adversarial reviews; engineering details in the
Changes Report).

### The designer (F9)
- **Click any element** → selection box with 8 drag handles (they hug curved elements —
  handles are drawn through the visor warp). Drag to move, handles to resize; **grid
  snap** with a live toggle + size slider (hold Alt to bypass).
- **Create your own UI**: add any of 18 element types; boxes with per-corner rounding;
  your own text labels; icons (26 built-in thin-line glyphs, PNG overrides in
  `config/StationeersUIMod/HudIcons/`); **draw line work** by clicking points on screen.
- **Edit anything in place**: the popup shows exactly the selected element's properties —
  geometry, per-corner radii, tier visibility, fonts, and every colour as either a
  palette link (theme follows the wheels) or a custom literal. Gauges can flip
  **vertical/horizontal** and recolor their **warn/crit zones per element**.
- **Undo/redo** (Ctrl+Z/Y, 50 steps), Del removes, Ctrl+D duplicates.
- **Profiles**: layouts autosave; save-as/switch/share as plain XML in
  `config/StationeersUIMod/HudProfiles/`. The shipped Default matches the concept art
  (vertical instrument cards, dashboard moodlets, round hologram) and regenerates if
  deleted.

### New live elements
- **Moodlet dashboard**: vanilla's status icons re-rendered as centered chips under the
  top bar — 1 = dead center, N = even spread — with caution/critical coloring and our
  thin-line glyphs (vanilla sprite fallback). The vanilla strip is hidden.
- **Body damage silhouette** (7 regions, the game's own per-organ ratios), **suit status
  chips** with state dots (helmet/AC/light, open-visor-while-suited reads critical),
  **vertical instrument cards** with target markers, **readout cards** for any of 18
  data sources (incl. the new felt-temperature), world name / day counter / active-hand
  badge / keybind chips.
- New client-safe data: suit pressure/temperature setpoints, jetpack thrust + propellant
  delta, hygiene, per-region damage, world name. The toilet gauge is server-only and
  honestly reads `--` on multiplayer clients.

### Under the hood
- The legacy fixed-panel HUD remains behind `UseDocumentHud` (off = exact 0.5.0
  behavior) until the designer proves itself in play-testing.
- Vanilla's bottom-right instrument cluster hides through the game's own
  `SetVisible` path, reconciled per-frame, fully restored on exit; the hide-vanilla
  toggles now default on (the mod replaces the whole HUD surface).
- Chip drops (0.6.2) survive on the new hand/equipment widgets.

## 0.6.2 Alpha — 2026-07-11

Three radial quality-of-life additions (FlorpyDorp's spec):

- **Drop chips on your body**: a dragged item chip released over a visor-HUD **hand box**
  or **1-6 equipment box** moves into that slot — swap when occupied, straight move when
  free, all through the same execute-time-gated `ItemActions` funnel (one action = one
  message). The drop zones hit-test through the curvature inverse, so curved HUDs drop
  where they draw. A release on a box is always consumed — it never falls through to
  screen-parking.
- **No child radials mid-drag**: slide-out satellites can't open while you're holding a
  chip, and any open one closes the moment a drag starts.
- **Move while the radial is open** (option, default ON, F10 → Radials): WASD keeps
  walking and Space jumps with a radial up. Implementation note: walking was never
  blocked by key capture — it dies because the unlocked cursor flips vanilla's
  `AllowMouseControl`; we force that one getter back to its normal-gameplay value while
  the radial is open (never touching the key-capture state, so no other vanilla binding
  can fire), and bridge jump's two extra gates only for the duration of the jump check.
  Typing in the search panel never moves you; seated players are excluded (vehicles read
  the same gate); camera look stays on the cursor.

## 0.6.1 Alpha — 2026-07-11

**F9 creative-spawn crash fixed** (post-release addition, same version): vanilla binds
F9 to creative SpawnItem, and its handler NREs when no spawnable is selected — every F9
press in a Creative world threw. Two fail-soft Harmony prefixes: a null-guard on
`Human.SpawnDynamicThing` (vanilla would only ever crash in that state), and suppression
of the vanilla spawn while our HUD editor owns the same key — one press toggles the
editor OR spawns, never both. With the creative spawn menu open, F9 spawns as vanilla
intends; rebind `HudEditorKey` to get both keys at once.

**Option B hold-mode fix (play-test feedback)**: dwell-to-enter now works on **every**
branch wedge, not just The Hub. Hold MMB, rest on The Hub (¼ s) → rest on Backpack →
rest on a category → release on the item: a full no-click journey from belt to any item.
The 0.3 s release grace after each dive still guards against misfires, and entering a
level restarts the dwell timer so you can't cascade through two levels in one rest
without meaning to. Center hint now reads "hover to dive | release to cancel".

## 0.6.0 Alpha — 2026-07-11

**Radial Option B — "The Hub"** (`ControlSchema: OptionB`, pick it in F10 or the config).
Option B is Option A plus a new middle-mouse gesture language; A and D are unchanged.

- **The Hub wedge**: the toolbelt radial grows a top-center wedge, *The Hub*, that
  branches into exactly what Tab opens — Search, every worn equipment piece, grab-another
  — built by the same code, so the two can never drift apart. With no toolbelt worn the
  MMB radial still opens (just the Hub).
- **Hold MMB** (transient, as always): flick and release to equip, and now — point at
  The Hub for a quarter second (or LMB-click it) to dive in; LMB enters bags/categories,
  RMB backs out one level; **releasing MMB always closes** and runs whatever enabled
  action you were hovering. A release right after diving in is treated as gesture
  momentum and cancels (0.3 s grace), never fires the wedge that happens to be under
  the cursor. Releasing over a branch just closes (only Option A latches sticky there).
- **Tap MMB** (new): the same toolbelt radial opens **sticky** — flick around, then
  **tap MMB again on a wedge to select it** (branches navigate deeper, actions run and
  close the menu), tap the CLOSE band to close deliberately (drops parked chips), tap
  empty space to dismiss. MMB is the select button in **every** sticky radial under B
  (Tab and the 1-6 keys too); LMB keeps working exactly as in A.
- Everything Option A does — STOW wedges, device satellites, scroll-adjust values, the
  search panel, drag-out parking, Shift-keep-open, auto-close after one action — is
  inherited unchanged by B.

## 0.5.0 Alpha — 2026-07-10

**The visor HUD** — the full-UI replacement from the feasibility report, built to the
concept art. User guide: `Documentation/Visor-HUD.md`. The radials are untouched.

### The interface
- **Curved top status bar**: UTC date/clock, PRESSURE · O₂ · TEMP · POWER · WATER
  cells, SUIT STATUS word (worst-of battery/air/filters/waste). Value colors flip at
  the game's REAL damage/warning thresholds (verified in the decompile).
- **Compass ribbon** at top-center (~12 % of the screen, adjustable): sliding cardinal
  letters + ticks past a fixed caret with exact degrees — same numbers as vanilla's
  Navigation readout, but camera-true in free-look.
- **Equipment column** (left): six live-thumbnail boxes = keys 1-6; the robot's #5
  reads BATTERY like its real slot.
- **Hand tray** (bottom-center): LEFT/RIGHT hand boxes on a trapezoid shelf, active
  hand in orange. Never a hotbar.
- **Vitals card** (bottom-right): the game's live 3D player render as a cyan
  **hologram** (tinted portrait RenderTexture + scanlines) beside HEALTH / O₂ /
  POWER / TEMP and a DAY + time footer.
- **Vignette** visor-edge darkening. All procedural UGUI — no assets, no shaders shipped.

### Diegetic tiers + flicker
- The HUD is the SUIT's HUD: numbers only while a powered suit is worn (the game's own
  synced `Powered` bit). Without one: felt-sense **words** — WARM, COLD, THIN AIR,
  HUNGRY, PARCHED, HURT — that flare when a sensation changes; the clock becomes
  MORNING/DUSK/NIGHT. The robot always gets the full readout.
- **Power death collapses the HUD**: 0.8 s decaying flicker + CRT vertical squash, then
  bare senses fade in slowly. A powered suit **boots the panels back one by one**;
  turning any panel off flickers it out; under 10 % battery the HUD glitches with
  single-frame dropouts. All under a `FlickerAnimations` switch.

### Curvature — A, B and C, all implemented
- **A — Vertex warp** (default): per-element mesh bend, crisp SDF text, zero cost.
- **B — Dome projection**: the HUD renders into a RenderTexture shown on a dome grid
  (one true projection) with an optional scanline wash. Off-screen disabled camera,
  manually rendered — the game's own off-screen pattern.
- **C — Curved world canvas** (experimental): the canvas physically floats ahead of
  the camera, cylinder-bent, on ZTest-always materials (the game ships TMP's Overlay
  shader). True perspective curvature.

### The F9 HUD editor
- **Click any HUD element on screen** → an edit popup opens next to it with exactly
  that element's colors and sliders. Hovering highlights its palette entries orange in
  the F9 window (which scrolls to them).
- Everything is editable: curvature mode/strength, per-panel toggles, every size, any
  loaded TMP font + per-role font sizes, the tier/flicker behavior, and all 19 HUD
  colors (own palette — the radials keep theirs) with **Undo/Redo** and reset.
- **Preview tier** combo (BARE/SUITED/ROBOT) + **Test power-death / Test boot** buttons.

### Docs
- `Documentation/Moodlets-Reference.md`: every vanilla moodlet/status with exact trigger
  conditions and client-safe read paths — the menu for what the HUD surfaces next.
- Vanilla hands/clothing/status panels hide only via the game's own path (opt-in,
  reconciled against their ACTUAL state so vanilla's own re-shows — waking from
  unconsciousness — can't strand them; restored on exit). Legacy 0.1.0 ImGui HUD
  kept as a fallback toggle.

### Review
- 7-dimension adversarial pass (~40 raw findings; verified by hand): **28 fixed,
  5 accepted/refuted** — plus the play-test catch that the curvature bent the wrong
  way on all three modes (flipped; `CurveInvert` in F9 for either taste). Highlights:
  stale atmosphere readings could mask a vacuum (fields now reset; missing air reads
  as 0 kPa); 1.4 px hairlines were silently culled by a 1 px mesh guard; the forced
  portrait camera wasn't released when only the vitals card hid; F9's test buttons
  were cancelled one frame later; F9 = vanilla's CREATIVE spawn key (gated +
  documented — rebind in creative); editor clicks reached the world (BlockCursorRaycast
  now held); robot with no battery now glitches hardest instead of not at all.

## 0.4.0 Alpha — 2026-07-10

Iteration 2 on the Option A schema, plus the full-UI feasibility report
(`Documentation/Visor-UI-Feasibility-Report.md` — verdict: green light).

### Settings, everywhere, complete
- **Generic device settings**: radials now enumerate controls exactly like vanilla's
  inventory window (same filter, same live state-baked labels, same dry-run for
  disabled states). This surfaces everything that was missing — **jetpack stabilizer**,
  suit **A/C · Air · Filter · Lock**, helmet **visor · light · flush**, portable-tank
  valve — automatically, for every item, including modded ones.
- **One settings rule everywhere**: >2 controls (or controls alongside slots) collapse
  into a SETTINGS wedge; 1–2 controls with no slots stay inline (a canister just says
  OPEN/CLOSE). Clicking SETTINGS opens all of them as wedges.
- **Component satellites**: slide over a battery/canister/cartridge in any slot →
  TAKE / **REPLACE** (the restored full swap list with consequence warnings) / settings.
- **Vanilla units**: suit pressure in kPa, temperature in °C; jetpack thrust as the
  HUD's ×100 number (vanilla never displays thrust in kPa — verified in the decompile).

### Parity
- **Tab shows all six worn pieces** (glasses were missing) and opening any of them is
  IDENTICAL to tapping its 1–6 key. Empty worn slots are STOW wedges.
- Bag levels show empty slots the same way everywhere.

### New interactions
- **Search is a radial**: top wedge = SEARCH + text box (fixed), bottom half fills with
  result wedges as you type; results drag out like any item; wheel pages; Shift keeps
  searching after a take.
- **Z-grab (the vanilla mouse-mod key)**: with a radial open, hold it and click world
  items to tear them into the drag layer as chips — then drop them into bags. Grab AND
  drop are 3 m range-gated client-side because build 27701's server never range-checks
  item moves (verified; the mod must not out-reach vanilla).
- **The radial is draggable by its hub** (snaps back to center on reopen) and has a
  **CLOSE button** on the hub's bottom edge (colors editable in the editor).
- **Shift = keep open**: holding Shift through any action that would close the radial
  keeps it open for the next action.
- **E swaps hands** (Q freed for a future gesture) and the vanilla active-hand ring now
  **stays lit while radials are open** (the cursor-unlock path was hiding it).

### Presentation options (playtest knobs)
- Bag grouping by sorting class: on/off. Free space: empty-slot wedges / aggregate STOW
  wedge / both. Max wedges per radial (6–32); **crowded rings page with Q** — a
  "1/2 · Q: next page" counter floats above the ring (no MORE wedge; vanilla Q-throw
  never fires while a radial is open).
- **Coarse/fine value scrolling**: suit pressure & temperature step ±10 per wheel notch,
  hold **C** for ±1 (thrust and the portable-tank valve stay 1:1 — thrust only has 19
  steps and the valve steps ±10 natively). Real worn suits (`Suit`, a separate class
  from `SuitBase`) now pair into triangle scroll wedges properly.
- **Side line widths**: the wedge outline's side lines are now true pixel widths, set
  separately at the hub and at the rim (equal = straight parallel lines; the old look
  was rim-fat/hub-skinny by construction).
- Icons now scale with their wedge (ratio slider). Shine intensity slider (0 = flat)
  with the RimShine palette colour actually visible at full strength. Dragged-out item
  bubbles are 50% bigger by default and size-adjustable; the editor shows a live demo
  chip. Disabled wedge text has its own palette colour (was welded to the wedge fill).
- **Both hubs stay dressed**: the child radial takes the detail readout, the main hub
  keeps its circle, title and colours.
- **Editor: hover-to-find colours** — point at any element in the preview and the
  entries painting it light up orange in the colour list (and the list scrolls to
  them). **Undo/Redo buttons** for colour edits (one drag = one step; reset is one
  step too). The readout never repeats itself ("Replace / Replace") anymore.

### Review
- Second adversarial pass on this iteration (15 agents): 10 confirmed findings fixed.
  The big one: **Z-grab was typed to DynamicThing where vanilla's pickup is hard-typed
  to Item** — a client could have stuffed the LANDER CAPSULE into a backpack, a
  mutation no vanilla client can produce; now gated `is Item` at grab AND execute.
  Also fixed: wedge-hover vs hub CLOSE/drag zone overlap (a click on a highlighted
  wedge could dump chips), GasMask-family label cache serving one-state-behind labels,
  stale MORE-wedge snapshots (paging removed the mechanism), search-exit orphaning an
  in-flight drag, world-drag drop feedback, and multiplayer label refresh after the
  server round-trip.

## 0.3.0 Alpha — 2026-07-10

The Option A control schema — a full interaction overhaul, F10-switchable against the
classic behavior (now "Option D"). Full write-up: `Documentation/Option-A-Control-Schema.md`.

### Option A schema (new default)
- **STOW wedges**: empty slots show the blank slot + "STOW"; hovering previews the held
  item on the orange fill.
- **Device child radials**: slide out on a tool/device for its controls — on/off wedges
  name the CURRENT state; suit pressure/temperature and jetpack thrust are
  **scroll-wheel adjustable** (one notch = one vanilla button press, clamped
  server-side). Hardsuit-sized devices branch into SETTINGS / SLOTS.
- **Nested bags**: click opens (bag becomes the main radial, RMB backtracks); slide out
  for STOW / TAKE. Works in Tab and the 1–6 radials.
- **The swap-with-everything satellite is gone** (Option A) — items slide out to
  TAKE / OPEN instead.
- **Auto-close**: one successful action closes the radial. Tap MMB to dismiss any
  sticky radial.
- **Q/E hand switching while a radial is open** (both schemas): Q = left hand active,
  E = right hand active, and the radial rebuilds so stow/equip targets follow. Drives
  the vanilla swap underneath; keys configurable.
- **Search all bags**: the radial transforms into a type-to-filter panel; click a
  result → free hand, or drops at your feet when both hands are full.
- **Drag-out parking**: press-drag items out of radials and park them on screen (≤12);
  drag chips into STOW wedges/bags to transfer; RMB-out drops all parked items on the
  ground; Escape cancels without moving anything. Client-visual until the drop; every
  mutation verified against the pinned occupant at execute time.

### Visuals
- **Full wedge outlines**: borders on the side edges too, with a wedge gap and
  side-edge anti-aliasing (the sides were never AA'd before — that was the jaggedness).
- **Bold game font** (RBNoBold, the scoreboard title face) + ALL CAPS wedge labels;
  font picker with every game font.
- **Live state under icons**: battery %, canister kPa, stack counts, filter wear —
  via the game's own client-safe `GetQuantityText` funnel.
- **Child radials show the readout in their own center** while open.
- Jackson's dim shading exposed: on/off + strength slider.

### Tools & data
- **Radial editor** (F10): screen goes black, live example radials show every wedge
  state; edit all colors (hue wheels), font, sizes, thickness, gap, feather, shading.
- **UIA sorting classes**: 22-class taxonomy for 785 items generated from the game's
  Stationpedia export (classify + audit agent pipeline); bag grouping now says
  "Storage" for nested backpacks instead of the game's coarse classes.

### Removed
- **Emergency Inject (Shift+9 / Shift+8)** — the 0.2.0 testing aid is gone. It read
  raw modifier keys, which the new search panel made dangerous (typing `(` is Shift+9
  and would have consumed a real injector).

### Review
- 20 findings confirmed by a 29-agent adversarial review (6 dimensions + per-finding
  refutation against the 27701 decompile), all fixed. Highlights: search takes now
  bypass `PlayerMoveToSlot`'s hidden MoveAll/MoveAllOfType tail (typing Shift or
  holding jetpack-descend would have bulk-dumped containers on one click); device
  controls re-verify possession at execute time (no adjusting an item a teammate just
  took); a press no longer survives RMB-back to fire an off-screen wedge; releases and
  scrolls only count on the rings (no inserting into a wedge from across the screen);
  radial canvases can no longer freeze over the loading screen.

## 0.2.0 Alpha — 2026-07-09

First playtest-driven iteration, built live against beta 27701 on the official server.

### Interaction
- **Tab semantics swapped**: tap Tab opens the bag radial (sticky — click to navigate),
  hold Tab shows the vanilla scoreboard. Config `TapOpensRadial` flips it back.
- **Slide-out satellite rings**: push past the rim on a tool/item to open its
  controls/slots without committing; dwell-gated so fast flicks aren't hijacked.
- **Sticky "shopping"**: radials stay open and refresh across actions.
- **Find item + Grab another**: flattened type-grouped search across all bags; one-flick
  repeat of the last retrieval. Nested-bag digging eliminated.
- **Equipment keys 1–6**: tap = management radial (toggle, slots, swaps), hold = equip/don.
- **Emergency inject** (testing aid): Shift+9 health / Shift+8 stim auto-injector, routed
  through the game's own server-authoritative use funnel; on-screen toast feedback.

### Visuals & readability
- Real-control filtering: no more phantom "Activate" on crowbars (vanilla `CanKeyInteract`
  filter, same as the inventory window).
- Orange stow slices: wedges that mean "held item goes HERE" fill orange.
- Two-line word-wrapped wedge labels; control wedges name the action, not the item.
- Hub (center circle): dark 80% translucent backing, text geometrically fitted to the
  circle's chord per line, 33% bigger by default (in-game size slider added).
- All-cyan text palette — no grey text anywhere.
- ASCII-only glyphs (the game's ImGui font can't render fancy arrows).

### Fixes
- 25 confirmed findings from a 31-agent adversarial review against the 27701 decompile,
  including: flick-release satellite hijack; RMB/Escape key-up leaking into vanilla
  (tool toggled / pause menu opened on close); held-tool battery "take to hand" paradox
  (now routes to the other hand); SmartStow+ stowing into the other hand; locked-slot
  bypass; stale-menu re-verification; vanilla-parity input gates (unconscious, cursor
  visible, Stationpedia); hold-to-don on 1–6 restored.
- Hotfix: frozen game input after closing any radial (deferred modal release never
  completed).
- Hotfix: Tab tap froze input via the scoreboard (vanilla-key suppression is now
  live-gated: vanilla always handles keys the mod can't act on).

### Dev
- ScriptEngine hot-reload flow (DLL in `BepInEx\scripts`, F6/watcher) with F3-console
  load/unload announcements. `CLAUDE.md` + `Changes Reports/` discipline established.

## 0.1.0 Alpha — 2026-07-08

Initial full-scope proof of concept, built in one pass against beta 27701:
toolbelt radial (hold MMB), tool radial (hold R), slot-swap discovery with consequence
warnings, SmartStow+ (stack merge → bag profiles → type memory → vanilla), bag profiles
with XML import/export and editor, visor HUD overlay (two-hand boxes, status strip,
vitals, clock, context panel, hardcore gating), nested bag radial, settings window (F10).
Multiplayer-safe by construction: every mutation through the game's `OnServer`/`Slot.Player*`
funnel with legality gates at execute time.
