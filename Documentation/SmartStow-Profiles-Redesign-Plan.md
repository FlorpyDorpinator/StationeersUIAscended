# Smart Stow / Bag Profiles — Redesign Plan

*Design plan for FlorpyDorp's review. **Nothing here is built yet.** No code was changed to
write this document. Companion doc (written in parallel):
`Documentation/SmartStow-Preset-Catalog.md` — the item-to-profile mapping tables for the
shipped preset sets described in §7.*

*Status: **DRAFT — awaiting FlorpyDorp's decisions on the open questions in §14.***

---

## 0. TL;DR — the five recommendations

| # | Question | Recommendation |
|---|---|---|
| 1 | Placement | **Sub-tab strip inside the existing "Storage" tab** — `[Bags] [Profiles] [Plans] [Routing] [Grid]`. Not a 7th top-level tab (§5). |
| 2 | Bag cards | **Card grid, 3 across**, live `GetThumbnail()` sprite (paint colour included — verified), display name + occurrence ordinal, profile dropdown on the card, secondary `[Capture]` / `[Edit]` row (§6). |
| 3 | Sets naming | **"Stow Plans"** — sub-tab label `Plans`, prose "Stow Plan". Runner-up: "Manifests". Full candidate list + reasoning in §8. |
| 4 | Share code | **Two kinds.** `UIAP1-R-<setid>-<rev>` (~18 chars) for shipped plans; `UIAP1-F-<base64url>` (~700 chars for a 9-profile plan) for custom ones — clipboard-only, with an 8-char Crockford **fingerprint** for verbal confirmation. Drop-in `.xml` file sharing stays the primary path. Full spec in §9. |
| 5 | Drag-drop | ImGui drag-drop **is present** in `RG.ImGui.dll` — but **F10 is not ImGui**, it is UGUI. Use UGUI `IBeginDragHandler/IDragHandler/IEndDragHandler`, of which the mod already has three working implementations. Verification detail in §10. |

**The single biggest correction to the brief:** the F10 Control Center is **UGUI + TextMeshPro
built in code**, not ImGui. See §1.1 — it changes the thumbnail story (easier), the drag story
(different API), and makes the **TMP ASCII-only rule a hard constraint on every F10 label**, not
a nicety.

---

## 1. What exists today — the ground truth

### 1.1 Surfaces (and which UI stack each one is)

| Surface | Stack | File |
|---|---|---|
| **F10 Control Center** (Profiles / Radial / HUD / **Storage** / Controls / Guide) | **UGUI + TMP, built in code** | `UI/Menu/UiaControlCenter.cs`, `UI/Menu/Kit/Uia*.cs` |
| The Storage tab (the bag-profile power room) | UGUI | `UI/Menu/Tabs/StorageTab.cs` (689 lines) |
| Item picker overlay | UGUI | `UI/Menu/Kit/UiaItemPicker.cs` |
| Universal Inventory "profile mode" (chips, capture, badges) | UGUI | `UI/Grid/GridProfileMode.cs`, `GridRegionView.cs`, `GridProfilePopup.cs`, `GridCapturePanel.cs` |
| **F9 HUD Designer** | ImGui (`ImGuiNET`) | `Windows/HudEditorWindow.cs` |
| **Legacy** ImGui bag-profile editor | ImGui | `Windows/ProfileEditorWindow.cs` — still reachable from `Windows/SettingsWindow.cs:82` |
| Toast / profiler | ImGui | `Overlay/Toast.cs`, `Profiling/*` |

Consequences for this plan:

- **Thumbnails are trivial.** `DynamicThing.GetThumbnail()` returns a Unity `Sprite`; you assign
  it to a UGUI `Image` (`UiaItemPicker.cs:264-274, 283-289` already does exactly this, with a
  per-session `prefabHash -> Sprite` cache). The CLAUDE.md trap *"ImGui texture IDs are per-frame,
  never cache the IntPtr"* **does not apply here** — there is no `IntPtr`. Caching the `Sprite` is
  correct and already the house pattern.
- **Drag-drop is UGUI EventSystem**, not `ImGui.BeginDragDropSource`. See §10.
- **TMP ASCII-only is a hard rule for F10.** Every F10 label goes through `TextMeshProUGUI`. The
  existing code already obeys it (`GridProfilePopup` uses `"* "` as its "assigned" marker
  specifically because a dingbat tofus; `UiaItemPicker` truncates rather than ellipsises for the
  same reason). Every label in this plan is ASCII.

### 1.2 The profile data model

`Features/BagProfiles.cs` — three storage layers, three different scopes:

```
BagProfile                        (GLOBAL, Profiles/profiles.xml + any Profiles/*.xml drop-in)
  name      : string              <- THE IDENTITY. Everything points at the raw string.
  badge     : string?             <- optional 2-4 char uppercase ASCII manila-tab tag
  Items     : List<ItemRule>      { prefab : string,  priority : int = 100 }
  Categories: List<CategoryRule>  { name : SortingClass-name, priority : int = 50 }
  SlotClasses:List<SlotClassRule> { name : Slot.Class-name,   priority : int = 60 }

Assignment                        (PER SAVE, Assignments/<saveName>.xml)
  bagRef : long (Thing.ReferenceId) -> profileName

TypeMemory                        (PER SAVE, same file)
  prefabHash : int -> bagRef : long      "where this item type last went"

PrefabDefault                     (GLOBAL, prefab-defaults.xml, own root by design)
  prefab : string (bag PrefabName) -> profileName      "all bags of this type"

Loadout                           (GLOBAL, Loadouts/<name>.xml, own root)
  Entry { prefab, occurrence:int, profileName }   "the Nth mining belt I wear -> Ores"
```

**There is no concept of a set / folder / group.** `BagProfileStore.Profiles` is exactly one flat
`List<BagProfile>` (line 206), and `Assignment`, `PrefabDefault` and `LoadoutEntry` all reference a
profile **by its raw name string**. This is the single most important architectural fact for §7-§9.

### 1.3 Every matching mechanism (and where "categories" actually comes from)

`BagProfile.Match(DynamicThing)` (`BagProfiles.cs:38-52`) returns the best priority, or null:

| Rule kind | Matches on | Priority offset | Effective range |
|---|---|---|---|
| `ItemRule` | `thing.PrefabName == rule.Prefab` (exact string) | **+20000** | always beats the others |
| `SlotClassRule` | `thing.SlotType == Slot.Class` | **+10000** | beats categories |
| `CategoryRule` | `thing.SortingClass == SortingClass` | +0 | weakest |

- **"Categories" = `Assets.Scripts.Objects.SortingClass`** — verified in the 27758 decompile
  (`Assembly-CSharp/Assets/Scripts/Objects/SortingClass.cs`). It is a `ushort` enum with **11
  values only**: `Default, Kits, Tools, Resources, Food, Clothing, Appliances, Atmospherics,
  Storage, Ores, Ices`. Most items land in `Default`.
- **"Item slot classes" = `Slot.Class`** — 44 values (`Slot.cs:1176`): `None, Helmet, Suit, Back,
  GasFilter, GasCanister, Motherboard, Circuitboard, DataDisk, Organ, Ore, Plant, Uniform, Entity,
  Battery, Egg, Belt, Tool, Appliance, Ingot, Torpedo, Cartridge, AccessCard, Magazine, Circuit,
  Bottle, ProgrammableChip, Glasses, CreditCard, DirtCanister, SensorProcessingUnit,
  LiquidCanister, LiquidBottle, Wreckage, SoundCartridge, DrillHead, ScanningHead, Flare, Blocked,
  SuitMod, Crate, Portables, RocketPayload, AutoInjector`.
- **The mod already owns a much better taxonomy that profiles cannot see.** `Core/UIASort.cs`
  defines `UIAClass` — **22 classes** (`Storage, Tool, Device, PowerCell, GasCanister, Tank,
  Filter, Ore, Ingot, Material, Kit, Electronics, Food, Ingredient, Seed, Medical, Clothing,
  SuitPart, Weapon, Consumable, Decor, Misc`) backed by a generated **785-item prefab-hash table**
  (`Core/UIASortingData.g.cs`) plus type-based fallbacks. Its own header says why it exists: *"The
  game's SortingClass is too coarse for radial grouping (11 classes, most items land in Default)"*.
  `BagProfile.Match` never calls it. **This is a live architectural conflict with FlorpyDorp's
  brief — see §3.2.**

Priorities are stored as raw `int` but exposed in F10 only as a tri-state `RuleTier`
(`Low=10 / Normal=50 / High=100`, snapped by `RuleTiers.TierOf`, midpoints 30 and 75).

### 1.4 How a bag gets a profile — all five paths

The router (`Core/StowRouter.cs`) consults them in this order (stages 4-6 of the 9-stage chain):

1. **`StowStage.Profile`** — the bag's explicit per-save `Assignment` (`Assign(bag, name)`).
   Highest priority wins; shallower bag beats a nested one on a tie.
2. **`StowStage.Affinity`** — no profile at all: an *implicit* profile derived live from the bag's
   contents (same prefab +9 cap 45, same `Slot.Class` +3 cap 15, same `SortingClass` +1 cap 5,
   fires at >= 3). Bags **with** an explicit profile are excluded from this stage entirely.
3. **`StowStage.BagDefault`** — user `PrefabDefault` first, then the shipped
   `StowRouter.BagTypeDefaults` table (`ItemToolBelt -> "Tools"`, `ItemMiningBelt -> "Ores"`, 10
   entries). Only fires when the named profile actually exists.
4. **`StowStage.Memory`** — per-save "this item type went here last".
5. **Loadout apply** — writes assignments in bulk from a structural mapping (prefab + occurrence).

Assignments are written from **four** places: `StorageTab` dropdown, `GridProfilePopup` row click,
`LoadoutStore.Apply`, and `ProfileCapture.Apply`/`PinItemRule` (which always auto-assign, per
FlorpyDorp Q3).

### 1.5 The current F10 workflow, click by click

**Task A — assign a profile to a bag (F10 route):**

1. `F10`
2. Click the **Storage** tab
3. **Scroll.** Everything below is rendered above "Your bags", in one flat scroll column:
   `Universal Inventory` header + 1 slider + 3 toggles + a 3-line note; `Smart Stow (G)` header +
   8 toggles + 1 slider (+4 more in Advanced); `Quick setup` header + note + 2 buttons — roughly
   **20 rows / ~700px** before the first bag appears, in a content area of about 500px.
4. Click the bag's dropdown
5. Click the profile row
   -> writes the assignment, bumps Grid chrome, and **rebuilds the whole tab**
   (`UiaControlCenter.Refresh()`).

**= 2 clicks + 1 tab click + F10 + a scroll hunt.** And the bag is identified by **text only**
(`SafeName(b)` = `DisplayName`). Two identical backpacks are literally the same row label. Assigning
six bags means six scroll-and-hunt cycles with no way to tell them apart.

**Task A — the in-game route (already better):** `B` (Grid) -> click the profile-mode tag button ->
click the bag's chip -> click the profile row = **1 key + 3 clicks**, with the bag visually
identified by its own region. *The F10 route should aim to match this, not just beat the scroll.*

**Task B — edit which items a profile catches:**

1. From the Storage tab, **scroll further down** past "Your bags" (N rows + a sub-row per bag) and
   the whole "Loadouts" section to reach "Edit a profile"
2. Click the **Profile** dropdown
3. Click the profile -> full tab rebuild
4. Click **+ Add item...** -> the picker overlay opens
5. Click a category chip *or* type into search
6. Click the item (stays open — multi-add is good)
7. Click **Done**

**= 6 clicks + typing + two scroll journeys to add ONE item.** Each extra item is +1 click while
the picker is open (this part is genuinely good).

Adjacent friction in the same section:

- **"By category" and "By slot class" are dropdowns whose index 0 is a fake sentinel**
  (`"+ Add category..."`, `"+ Add slot class..."`). A dropdown that is really a button. The slot
  list is 45 entries deep with no search and no icons.
- **The rule list renders backwards** — `for (int i = p.Items.Count - 1; i >= 0; i--)`
  (`StorageTab.cs:376`), so a newly added rule appears at the **top** of its group. Three separate
  reversed groups, each rule row carrying a `[ITEM|SLOT|CAT]` tag chip, the value, the word
  "Priority", a dropdown, and a red `X`.
- **There is no rename, no delete, no duplicate, no reorder** for bag profiles anywhere in F10.
  `BagProfileStore.RenameProfile` exists (line 410) and **has zero callers**. The only delete path
  in the entire mod is `Windows/ProfileEditorWindow.cs:127` — the *legacy ImGui* editor, reachable
  only from the legacy ImGui `SettingsWindow`.
- Consequence: **"Create recommended profiles" is a one-way door.** One click appends 10 category
  profiles to the flat global list, and F10 offers no way to remove them.

**Task C — switch preset sets: impossible.** The concept does not exist.

### 1.6 Scorecard: what makes it clunky

| Symptom | Root cause | File |
|---|---|---|
| Scroll hunt before you reach anything | Four unrelated features stacked in one scroll column | `StorageTab.Build` |
| Bags are indistinguishable | Text-only rows, no thumbnail, no ordinal | `StorageTab.cs:140` |
| "Very busy and overwhelming" editor | 3 rule groups x reversed order + 5 controls per row + 2 sentinel dropdowns + a test box, all in one column | `StorageTab.RuleList/AddControls` |
| Cannot undo "Create recommended" | No delete UI | — |
| Two editors that disagree | UGUI Storage tab vs legacy ImGui `ProfileEditorWindow` | `Windows/ProfileEditorWindow.cs` |
| Every gesture rebuilds the whole tab | `UiaControlCenter.Refresh()` after each edit | `StorageTab.Save()` |
| Preset "sets" have no home | `Profiles` is one flat `List<BagProfile>` | `BagProfiles.cs:206` |

---

## 2. Design goals

1. **Keep 100% of today's expressive power.** Item rules, slot-class rules, category rules and
   per-rule priority must all stay reachable. Nothing gets hidden behind "advanced only" that is
   reachable today.
2. **Identify a bag by looking at it**, not by reading a name.
3. **One screen = one job.** Bags / Profiles / Plans / Routing are four jobs.
4. **Make the destructive gestures exist** (rename, delete, duplicate) — set management is
   impossible without them, and their absence is already a shipped bug.
5. **Nothing touches game state.** Every gesture in this plan writes config XML only, exactly as
   today. No new MP surface, no `OnServer.*` call, no `Slot` mutation.
6. **Migrate silently.** An existing player's `Profiles/profiles.xml`, `Assignments/*.xml`,
   `Loadouts/*.xml` and `prefab-defaults.xml` all keep working with no action from them (§12).

---

## 3. Two architectural conflicts to settle first

### 3.1 A "set" has nowhere to live, and profile names are global keys

`Assignment.ProfileName`, `PrefabDefault.ProfileName` and `LoadoutEntry.ProfileName` are all **raw
strings** resolved by `BagProfileStore.FindProfile(name)` — first match in one flat list. So if the
"Printers" plan and the "Categories" plan both contain a profile called `Tools`, they collide, and
whichever loads last silently wins for every existing assignment.

**Three ways out:**

| Option | Shape | Verdict |
|---|---|---|
| **A. One active plan at a time** | `Plans/<plan>/*.xml`; only the active plan's profiles are in `BagProfileStore.Profiles`. Names stay unqualified. | **RECOMMENDED.** Zero change to `Match`, `StowRouter`, `Assignment`, `Loadout`. Switching plans is a load, not a merge. Matches the mental model ("I use FlorpyDorp's layout"). |
| B. Qualified ids | Profile identity becomes `plan/name`; every pointer migrates. | Correct but touches every consumer and every player's on-disk assignment file. Large, risky, invisible to the player. |
| C. Flat list + a `set=` tag attribute | Profiles stay in one list, `set` is only a UI filter. | Cheapest, but the collision stays real and "copy a profile between sets" becomes a rename-or-clobber. Reject. |

**Recommend A.** Consequence to accept up front: *only one plan is live at a time.* Cross-plan
copy/paste and drag (brief §5) then means "copy from the inactive plan I'm browsing into the active
one" — which is exactly how the set manager in §7 is drawn. Switching plans **does not** wipe
assignments: assignments key on `ReferenceId`, so a bag assigned to `Tools` still says `Tools`; if
the new plan has no `Tools`, the profile stage simply declines for that bag (already fail-soft —
`StowRouter.TryProfile` skips a dangling name) and the affinity/bag-default stages take over. A
plan can also carry an optional bag mapping (§7.3) so switching *and* re-assigning is one click.

### 3.2 FlorpyDorp's own set does not map onto `SortingClass`

His bag layout: **Paints / Materials (steel+glass sheets) / Frames+Walls / Ingots+Ores /
Electronics / Liquids & Gases / Cables & Pipes / Canisters / Misc.**

Against the 11-value `SortingClass`, only `Ores`, `Atmospherics` and `Kits` are even close.
Paints, Frames+Walls, Cables & Pipes and Electronics have no category at all — they would each
have to be 8-12 hand-listed `ItemRule`s. That works today (and the preset catalog can ship exactly
that), but it is brittle: a new game version's new wall kit, or any modded item, is not caught.

Against `UIAClass` (22 classes, `Core/UIASortingData.g.cs`), the same set is nearly one-rule-each:
`Material`, `Kit`, `Ore`+`Ingot`, `Electronics`, `Tank`+`GasCanister`, `Consumable`(cables),
`Misc`. Only "Paints" has no class.

**Recommendation: add a fourth rule kind, `UIAClassRule`, at priority offset +5000** (between
category +0 and slot class +10000 — a UIA class is more specific than a game category, less
specific than a physical slot class). This is a purely additive change:

- new `[XmlElement("Class")] List<UIAClassRule>` on `BagProfile`, with the same cached-parse idiom
  as `CategoryRule`/`SlotClassRule`;
- one extra loop in `Match` calling `UIASort.Classify(thing)` — which is a dictionary hit on
  `PrefabHash` plus a type-switch fallback, i.e. the same cost class as the existing loops;
- old files round-trip unchanged (`XmlSerializer` omits an empty list; an old build re-saving drops
  the element — the same accepted risk already documented for `BagProfile.Badge`, `BagProfiles.cs:24-27`).

Without it, shipped preset set #2 ("by category") is a thin 11-profile mapping and FlorpyDorp's own
set #3 is ~45 hand-listed prefabs. With it, both are good. **This is the one genuinely new
capability the plan proposes; everything else is UI.** Flagged as open question Q1.

---

## 4. The proposed information architecture

```
F10  UI ASCENDED                                        [Simple] [Advanced] [X]
  [RADIAL MENUS  ON]                    [VISOR HUD  ON]
  ( Profiles )( Radial )( HUD )( STORAGE )( Controls )( Guide )      <- unchanged tab bar
  +---------------------------------------------------------------------------+
  |  [ Bags ] [ Profiles ] [ Plans ] [ Routing ] [ Grid ]      <- NEW sub-tabs |
  |                                                                            |
  |  ...the selected sub-tab's content...                                      |
  +---------------------------------------------------------------------------+
```

| Sub-tab | Contains | Comes from |
|---|---|---|
| **Bags** (default) | The bag-card grid. Assign / capture / edit per bag. Prefab-default toggle. | today's "Your bags" + "Quick setup" |
| **Profiles** | The profile list + the streamlined rule editor. Rename / duplicate / delete. | today's "Edit a profile" |
| **Plans** | The plan (set) manager: shipped + custom, switch, CRUD, move/copy profiles, import/export code. | **NEW** |
| **Routing** | The SmartStow+ chain toggles, scan depth, advanced toggles, the test box. Loadouts. | today's "Smart Stow (G)" + "Test box" + "Loadouts" |
| **Grid** | The Universal Inventory knobs (cell size, scroll-select, badges, ghost hints). | today's "Universal Inventory" block |

Nothing is removed. The 40-row scroll becomes five short screens.

---

## 5. Placement — sub-tab vs top-level tab

### The measurement

`UiaControlCenter` builds a fixed **980 x 660** window (`UiaControlCenter.cs:304`) with
`UiaTheme.Pad = 14` -> **952px** of usable width. `BuildTabBar` lays out **fixed 130px** buttons
with a **4px** gap (`UiaControlCenter.cs:405-412`):

| Tabs | Width used | Slack |
|---|---|---|
| 6 (today) | 6x130 + 5x4 = **800px** | 152px |
| 7 (a new top-level tab) | 7x130 + 6x4 = **934px** | **18px** |
| 8 (any future tab) | 8x130 + 7x4 = **1068px** | **overflows by 116px** |

### Option 1 — Sub-tab strip inside Storage — **RECOMMENDED**

```
  ( Profiles )( Radial )( HUD )( STORAGE )( Controls )( Guide )
  +---------------------------------------------------------------------------+
  |  [ Bags ] [ Profiles ] [ Plans ] [ Routing ] [ Grid ]                      |
  |  ------------------------------------------------------------------------ |
```

- **Pro:** tab bar untouched (152px of slack preserved for whatever comes next). Fixes the *real*
  problem — the 40-row scroll — rather than the tab-bar problem. The mod already trains the
  sub-tab idiom in F9 (`HudEditorWindow` editor sub-tabs, `ActivateEditorSubTab`), so it is a
  known pattern for both authors and players. Sub-tab buttons can be **100px** (5 x 100 + 4 x 4 =
  516px), leaving room for a right-aligned status line.
- **Con:** one extra click to reach Plans from cold (F10 -> Storage -> Plans). Mitigated by
  remembering the last sub-tab per session and by deep links (see below).
- Cost: **S**. `IUiaTab.Build` already receives the content rect; the strip is one
  `UiaUi.HLayout` of `UiaControls.Button(..., ButtonStyle.Panel)` with `SetSelected`, exactly like
  `BuildTabBar`.

### Option 2 — A 7th top-level tab ("Stow")

```
  ( Profiles )( Radial )( HUD )( Storage )( STOW )( Controls )( Guide )
```

- **Pro:** one click from cold. Signals that Smart Stow is a headline feature.
- **Con:** 18px of slack — the next tab anyone adds breaks the layout, and the two adjacent tabs
  ("Storage", "Stow") are near-synonyms in English. It also leaves "Storage" holding a random
  leftover (Grid knobs + routing toggles), which is exactly the shape we are trying to fix.
- Cost: **S** for the tab, **M** because Storage still needs splitting internally afterwards.

### Option 3 — Rename + re-partition ("Inventory" / "Stow")

Move the Grid knobs to a renamed "Inventory" tab and make "Stow" the profile/plan home.

- **Pro:** cleanest end state conceptually.
- **Con:** the Grid knobs were *just* moved into Storage in the post-0.9.2.5 play-test round
  (`StorageTab.cs:72-74` — FlorpyDorp: *"it has nothing to do with the radials"*). Moving them
  again within one or two releases is churn a tester will notice and dislike. Still 7 tabs.
- Cost: **M**.

### Recommendation

**Option 1.** Additionally, add **deep links** so the extra click never bites in practice:

- the Grid's profile-mode chip gains a small `[...]` that opens F10 straight to `Storage > Bags`
  focused on that bag (mirrors F9's `RequestEditorTab` pattern, `HudEditorWindow.cs:170`);
- `Quick setup` on the Bags sub-tab links to `Plans`.

---

## 6. Surface 1 — the Bag Cards screen

### Feasibility (verified)

- **`Thing.GetThumbnail()`** (decompile `Assembly-CSharp/Assets/Scripts/Objects/Thing.cs:1542`):

  ```csharp
  public Sprite GetThumbnail()
  {
      if (!this.HasPaintableMaskMaterial && (this.PaintableMaterial == null || this.CustomColor == null))
          return this.Thumbnail;
      int colorIndex = GameManager.GetColorIndex(this.CustomColor);
      if (this.Thumbnails == null || this.Thumbnails.Length <= colorIndex || colorIndex < 0)
          return this.Thumbnail ? this.Thumbnail : null;
      return this.Thumbnails[colorIndex];
  }
  ```

  **A painted bag returns its coloured thumbnail variant.** FlorpyDorp's "colored bags show their
  color" requirement is satisfied by calling the same method the inventory calls — no custom
  rendering, no render-texture, no new asset. `CustomColor` is networked state, so this is
  MP-client-safe.
- **Rendering it:** `Image.sprite = sprite; preserveAspect = true` — the exact three lines
  `UiaItemPicker.GridCell` already uses (`UiaItemPicker.cs:283-289`).
- **Caching:** cache `prefabHash -> Sprite` **per Build**, not per frame, and only cache non-null
  results (`UiaItemPicker.Thumb`, line 264, explains why: a thumbnail the game has not generated
  yet must be retried). *Painted* bags must key on `(prefabHash, colorIndex)` or simply not be
  cached across bags — two differently painted backpacks share a prefab hash. **Simplest correct
  rule: key the card cache on `Thing.ReferenceId`, which is per-bag.**
- **Card grid layout:** `ProfilesTab.Build` (`ProfilesTab.cs:48-58`) already ships the exact
  pattern — `GridLayoutGroup` with `cellSize`, `FixedColumnCount`, plus a `ContentSizeFitter` so
  it lives inside the scroll column. Copy it.
- **Which bags:** `LoadoutStore.CollectWornBags(list)` — *the* canonical enumeration (depth-2 scan,
  occupants with 2+ slots, first-seen order, de-duplicated). Reuse it verbatim so the card order,
  the "Your bags" order and the `LoadoutEntry.Occurrence` index can never diverge.
- **Restyle discipline:** `UiaControlCenter.IsRestyling` goes true up to ~7x/s during an F9
  colour-wheel drag. `StorageTab` already guards its inventory scan and disk IO behind it
  (`StorageTab.cs:38-40, 670-675`); the card grid **must** do the same, and must not re-fetch
  thumbnails on a restyle build.

### Option 1A — Card grid, 3 across — **RECOMMENDED**

```
+--------------------------------------------------------------------------------+
| BAGS                                        3 worn - 2 have a profile          |
| [ Apply plan to my bags ]  [ Auto-assign ]                        Plan: Starter |
+--------------------------------------------------------------------------------+
|  +----------------------+  +----------------------+  +----------------------+  |
|  |                      |  |                      |  |                      |  |
|  |      [ 96x96         |  |      [ 96x96         |  |      [ 96x96         |  |
|  |        thumbnail ]   |  |        thumbnail ]   |  |        thumbnail ]   |  |
|  |                      |  |     (red backpack)   |  |                      |  |
|  |  Mining Belt         |  |  Backpack  #2        |  |  Tool Belt           |  |
|  |  16 slots - 11 used  |  |  6 slots - 2 used    |  |  8 slots - 8 used    |  |
|  |  +----------------+  |  |  +----------------+  |  |  +----------------+  |  |
|  |  | Ores         v |  |  |  | (no profile) v |  |  |  | Tools        v |  |  |
|  |  +----------------+  |  |  +----------------+  |  |  +----------------+  |  |
|  |  [Capture] [Edit]    |  |  [Capture] [Edit]    |  |  [Capture] [Edit]    |  |
|  |  [x] All Mining Belts|  |  routes by contents  |  |  [ ] All Tool Belts  |  |
|  +----------------------+  +----------------------+  +----------------------+  |
+--------------------------------------------------------------------------------+
```

- Card `cellSize` ~ **298 x 236**, 3 columns — same width as the HUD profile cards so the two
  screens feel like one product; 12px taller to fit the dropdown + button row.
- **`#2` is the occurrence ordinal** — shown only when the same prefab appears more than once.
  This is `LoadoutEntry.Occurrence` made visible, which is the number Loadouts already key on:
  a coherence win, not a new concept.
- The `[x] All <bag>s` checkbox is today's `BagSubRows` prefab-default toggle, moved onto the card.
- A profile-less bag shows today's honest `routes by contents` hint (the affinity tier) in place of
  the checkbox — keeps the zero-setup tier discoverable, which was design O6's whole point.
- `[Capture]` opens the existing `GridCapturePanel` proposal flow — the *best* gesture in the whole
  feature and currently reachable only from inside the Grid's profile mode. Surfacing it here is a
  large UX win for a small amount of work.
- `[Edit]` deep-links to `Profiles` sub-tab with that bag's profile selected.

**Tradeoffs:** 3 cards per row at 298px means 4+ worn bags scroll. Acceptable — most players wear
3-5. A card is a lot of pixels for one dropdown; justified because the thumbnail is the whole point.

### Option 1B — Dense rows with a 40px thumbnail

```
| [icon] Mining Belt         16 slots  11 used  [ Ores        v ] [Cap][Edit] [x]All |
| [icon] Backpack #2          6 slots   2 used  [ (no profile)v ] [Cap][Edit]        |
| [icon] Tool Belt            8 slots   8 used  [ Tools       v ] [Cap][Edit] [ ]All |
```

- **Pro:** 10+ bags on screen, no scroll, closest to today's code (one extra `Image` per row).
  Cost **S**.
- **Con:** a 40px icon does not read as "the bag's colour"; it is a slightly better version of
  today rather than the thing FlorpyDorp asked for.
- **Good as a density toggle** — see the recommendation.

### Option 1C — Big cards, 2 across, with a contents strip

```
|  +-------------------------------------+  +-------------------------------------+
|  |  [128x128 thumb]  Mining Belt       |  |  [128x128 thumb]  Backpack #2       |
|  |                   16 slots, 11 used |  |                   6 slots, 2 used   |
|  |                   [ Ores          v]|  |                   [ (no profile)  v]|
|  |  [o][o][o][o][o][o][ ][ ]           |  |  [o][o][ ][ ][ ][ ]                 |
|  |  [Capture]  [Edit rules]  [x] All   |  |  [Capture]  [Edit rules]            |
|  +-------------------------------------+  +-------------------------------------+
```

- **Pro:** the contents strip (mini item thumbnails) makes "is this the bag I mean" instant and
  makes `[Capture]` self-explanatory — you can see what you are about to capture.
- **Con:** 2 across means 4 bags = scroll; N extra `Image`s and `GetThumbnail()` calls per card
  (16 for a mining belt). Cost **M**, and the restyle-rebuild cost grows with contents.

### Recommendation

**1A as the default**, with **1B available via the existing Simple/Advanced density toggle**
(`UiaControlCenter.Advanced` is already threaded into `IUiaTab.Build(content, advanced)` — the
switch costs nothing). Consider 1C's contents strip as a **hover-only** enrichment in a later
phase, so the per-frame/per-build cost is paid for one card at a time.

---

## 7. Surface 2 — the profile editor, streamlined

The brief: *"very busy and overwhelming"* — **while keeping full customization**. The busyness
comes from four separable things: (a) three rule groups rendered as three reversed lists,
(b) five controls per rule row, (c) three different *ways to add* a rule (button, sentinel dropdown,
sentinel dropdown), and (d) unrelated sections (test box, loadouts, sharing) sharing the column.

(d) is solved by the sub-tabs. Here are three options for (a)-(c).

### Option 2A — One list + one unified "Catch..." picker — **RECOMMENDED**

```
+--------------------------------------------------------------------------------+
| PROFILES                                     Plan: Stationpedia Ascended        |
|  +-------------------+  +---------------------------------------------------+  |
|  | Paints         12 |  |  Electronics                            [Rename]   |  |
|  | Materials       9 |  |  badge [ELEC]                  [Duplicate] [Delete]|  |
|  | Frames+Walls   11 |  |  ------------------------------------------------- |  |
|  | Ingots+Ores     4 |  |  [ + Catch... ]              Catches 8 things:     |  |
|  | > Electronics   8 |  |                                                    |  |
|  | Liquids+Gases   5 |  |   [i] Cable Coil ................ Item     High  X |  |
|  | Cables+Pipes    7 |  |   [i] Heavy Cable Coil .......... Item     High  X |  |
|  | Canisters       4 |  |   [s] Battery ................... Slot     Norm  X |  |
|  | Misc            1 |  |   [s] Circuitboard .............. Slot     Norm  X |  |
|  |                   |  |   [s] Motherboard ............... Slot     Norm  X |  |
|  | [ + New profile ] |  |   [s] Programmable Chip ......... Slot     Norm  X |  |
|  +-------------------+  |   [u] Electronics (UIA class) ... Class    Norm  X |  |
|                         |   [c] Kits ...................... Category Low   X |  |
|                         |                                                    |  |
|                         |  Test:  [ Pick an item... ]  -> would go here (#1) |  |
|                         +---------------------------------------------------+  |
+--------------------------------------------------------------------------------+
```

Changes from today:

- **Master-detail.** Left = profile list with a rule count; right = one profile. Replaces the
  dropdown-plus-rebuild dance, and makes rename/duplicate/delete obvious (and they finally exist).
- **ONE rule list, in a stable order** (specific -> general: Item, Slot, Class, Category), not three
  reversed lists. Newly added rules land in their group, sorted, not on top of the screen.
- **ONE add button.** `[+ Catch...]` opens the *existing* `UiaItemPicker` overlay with a source
  strip at the top:

  ```
  +---------------------------------------------------------------+
  | Catch...                        [List][Icons]          [Done]  |
  |  ( ITEMS )( CATEGORIES )( SLOT CLASSES )( UIA CLASSES )        |
  |  [ Search...                                                 ] |
  |  (All)(Kits)(Tools)(Resources)(Food)... category chips         |
  |  [ Any slot class                                          v ] |
  |  ------------------------------------------------------------- |
  |  [icon] Cable Coil ....................... Resources           |
  |  [icon] Heavy Cable Coil ................. Resources           |
  +---------------------------------------------------------------+
  ```

  The ITEMS page is `UiaItemPicker` **verbatim** (search + category chips + slot-class dropdown +
  list/icon views already exist). The other three pages are simple searchable lists of enum names
  — which finally gives the 45-entry `Slot.Class` list a search box, and kills both sentinel
  dropdowns.
- **The priority tri-state collapses to a click-to-cycle chip** (`High -> Normal -> Low -> High`) —
  `RuleTiers.Next` already exists for exactly this (`BagProfiles.cs:147`) and is currently unused.
  Saves a dropdown open per change; still 1 click to change, 0 clicks to read.
- **The test box moves into the editor** as a one-line affordance: pick an item, see whether *this*
  profile would claim it and at what rank. Same `StowRouter.ResolveAll` dry-run, read the pooled
  list immediately (`StorageTab.TestResults` already documents that discipline).
- `[u]`/`[c]`/`[s]`/`[i]` are single-letter ASCII tag chips instead of the 46px `ITEM`/`SLOT`/`CAT`
  boxes — same information, a third of the width.

**Tradeoff:** master-detail costs horizontal room (a ~180px list column out of 952px). Fine.

### Option 2B — Keep the single column, add collapsible groups

```
|  Profile: [ Electronics                v ]  [New] [Rename] [Dup] [Del] [Export] |
|  v ITEMS (2)                                                   [ + Add item ]  |
|      Cable Coil ......................................... [High v]  X          |
|      Heavy Cable Coil ................................... [High v]  X          |
|  v SLOT CLASSES (4)                                            [ + Add slot ]  |
|      ...                                                                        |
|  > CATEGORIES (1)                                              [ + Add cat  ]  |
```

- **Pro:** smallest diff from today (**S**), collapsing alone kills most of the visual noise, and
  the "which kind is this rule" question is answered by the group header rather than a chip.
- **Con:** keeps three add-paths and three lists; scanning "what does this profile catch?" still
  means reading three places. Does not solve the problem, only quiets it.

### Option 2C — Two-pane "sources -> caught" transfer list

```
|  ALL ITEMS / CATEGORIES / CLASSES        |  ELECTRONICS CATCHES                |
|  [search......................]          |                                      |
|  [ ] Cable Coil                          |  >>  [x] Cable Coil        High      |
|  [ ] Circuitboard (slot)                 |      [x] Battery (slot)    Normal    |
|  [ ] Kits (category)                <<   |      [x] Kits (category)   Low       |
```

- **Pro:** the "assemble a set" gesture is very fast — tick many, transfer once. Great for the
  initial build of a 12-rule profile.
- **Con:** transfer lists are a power-user idiom that reads as *more* complex, not less, and the
  left pane must fuse four heterogeneous sources into one checkbox list. Directly against the
  brief's stated goal. Reject.

### Recommendation

**2A.** It is the only one that reduces the *number of concepts on screen* (one list, one add
button) while gaining rename/duplicate/delete, and it reuses `UiaItemPicker` — the best-built
component in this area — instead of competing with it.

---

## 8. Surface 3 — the Plans (sets) manager

### 8.1 What a plan is

```
config/StationeersUIMod/StowPlans/<Plan Name>/
    plan.xml            <- id, display name, schema, rev, description, optional bag mapping
    <Profile>.xml       <- one BagProfile per file (SAME BagProfileFile root as today)
    ...
config/StationeersUIMod/StowPlans/.shipped-manifest      <- provenance, exactly like HudProfiles
```

- A profile file is **byte-compatible with today's `Profiles/*.xml` drop-in format**
  (`[XmlRoot("BagProfiles")]` with one `<BagProfile>`), so `ExportProfile` output and every
  profile anyone has ever shared imports into a plan unchanged.
- `plan.xml` optionally carries a **bag mapping** — the same `(prefab, occurrence, profileName)`
  triple `LoadoutEntry` already uses. This is what makes "apply the whole plan to my bags" one
  click, and it means a shipped plan can arrive pre-wired ("the Starter plan puts Tools on your
  tool belt").
- Exactly one plan is **active** at a time (config key `StowActivePlan`); its profiles are what
  `BagProfileStore.Profiles` holds (§3.1 Option A).

### 8.2 Option 3A — Plan cards + an inline profile shelf — **RECOMMENDED**

```
+--------------------------------------------------------------------------------+
| STOW PLANS                                                                      |
|  +-------------------+ +-------------------+ +-------------------+              |
|  | STATIONPEDIA      | | BY PRINTER        | | BY CATEGORY       |              |
|  | ASCENDED   ACTIVE | |                   | |                   |              |
|  | 9 profiles        | | 7 profiles        | | 11 profiles       |              |
|  | FlorpyDorp's own  | | Autolathe, Tool   | | One per game       |              |
|  | bag layout        | | Manufactory, ...  | | sorting category   |              |
|  | [Apply to bags]   | | [ Use this plan ] | | [ Use this plan ]  |              |
|  +-------------------+ +-------------------+ +-------------------+              |
|  +-------------------+ +-------------------+                                    |
|  | STARTER           | | + NEW PLAN        |                                    |
|  | 1 profile         | |                   |                                    |
|  | Early game, one   | |   (blank, or      |                                    |
|  | bag, no fuss      | |    copy active)   |                                    |
|  | [ Use this plan ] | |                   |                                    |
|  +-------------------+ +-------------------+                                    |
+--------------------------------------------------------------------------------+
| PROFILES IN: By Printer                        [Rename plan] [Delete] [Share]   |
|   Autolathe (14)   Tool Manufactory (9)   Electronics Printer (11)   ...        |
|   ^ drag one onto a plan card above to COPY it there                            |
+--------------------------------------------------------------------------------+
| [ Import a plan code... ]                                                       |
+--------------------------------------------------------------------------------+
```

- Plan cards reuse the `ProfilesTab.Card` shape (`ProfilesTab.cs:292`) — same visual family as HUD
  profile cards, so "picking a thing from cards" is one learned gesture across the mod.
- The shelf shows the profiles of the **browsed** plan (not necessarily the active one), so you can
  window-shop a plan before switching, and copy one profile out of it without switching.
- **Drag** a shelf chip onto a plan card = copy. **Right-click** a chip = a context menu with
  `Copy to > (plan list)`, `Move to > (plan list)`, `Rename`, `Delete`. The context menu is the
  accessible/keyboard-safe twin of the drag and is what ships in phase 1 (§10).

### 8.3 Option 3B — Two-column plan browser (Finder-style)

```
|  PLANS                 |  PROFILES IN "By Printer"                              |
|  > Stationpedia  (act) |   Autolathe            14 rules   [copy to v] [x]      |
|    By Printer          |   Tool Manufactory      9 rules   [copy to v] [x]      |
|    By Category         |   Electronics Printer  11 rules   [copy to v] [x]      |
|    Starter             |   ...                                                  |
|  [+ New] [Import]      |                          [ + New profile in this plan ]|
```

- **Pro:** drag between columns is unambiguous, the list scales to 20 plans, and it is the same
  master-detail shape as the profile editor (2A) — one idiom, two screens. Cheapest to build (**S**).
- **Con:** loses the "these four shipped plans are curated content, look at them" showcase that
  cards give. Given four shipped plans are a headline feature, that matters.

### 8.4 Option 3C — Modal plan switcher + a plain list

A `[Change plan...]` button opening a modal picker; everything else is a flat list.

- **Pro:** minimum surface, minimum code.
- **Con:** hides the feature; no cross-plan copy at all. Reject.

### Recommendation

**3A for the shipped/browse experience, with 3B's two-column list as the `Advanced` density
variant.** They share one data layer; the density toggle already exists.

### 8.5 The four shipped plans

| # | Plan | Shape | Notes |
|---|---|---|---|
| 1 | **By Printer** | One profile per fabricator: Autolathe, Tool Manufactory, Electronics Printer, Security Printer, Organics Printer, Hydraulic Pipe Bender, Rocket Manufactory, Recycler | See §8.6 — this one can be **generated**, and stay mod-aware. |
| 2 | **By Category** | One profile per `SortingClass` (+ per `UIAClass` if Q1 lands) | Item mapping lives in `SmartStow-Preset-Catalog.md`. |
| 3 | **Stationpedia Ascended** | Paints / Materials / Frames+Walls / Ingots+Ores / Electronics / Liquids & Gases / Cables & Pipes / Canisters / Misc | FlorpyDorp's own layout. Needs Q1 (`UIAClassRule`) to be robust; ships as item rules otherwise. |
| 4 | **Starter** | One profile ("Everything useful"), plus a bag mapping that puts it on your first backpack | Early game, one bag. The anti-configuration plan. |

**Do not reuse the four legacy starter names** (`Construction`, `Atmospherics`, `Electrical`,
`Farming` — `BagProfiles.cs:733`). Those are seeded exactly once, only when `Profiles/` is empty,
and are never updated; colliding with them would silently hand a player a stale copy. Migration
handles them explicitly in §12.

### 8.6 "By Printer" can be generated, and can stay mod-aware

Verified in the 27758 decompile: every fabricator exposes
`Dictionary<DynamicThing, Recipe> Recipes` backed by a
`DynamicThingRecipeComparable("<MachineName>")`
(`Assembly-CSharp/Assets/Scripts/Objects/Electrical/Autolathe.cs:13-17, 111`), and recipes are
registered at load from `WorldManager.RecipeData` **including from mods**
(`Util/DynamicThingRecipeComparable.cs:AddRecipe(WorldManager.RecipeData, ModAbout)`).

Two ways to use that:

- **Author-side (recommended for shipping):** a dev-only console command that walks the tables in a
  live world and writes `StowPlans/By Printer/*.xml` into the repo — the same "export to mod folder"
  idea `ProfilesTab.ExportActiveToMod` already implements for HUD themes. The shipped plan is then
  a plain, reviewable, diffable set of files.
- **Runtime-side (optional, later):** a `[Regenerate from recipes]` button on the By Printer plan
  card, which rebuilds the profiles from the *player's* installed mod set. Powerful, but it
  overwrites content and needs a two-step confirm; park it behind Advanced.

---

## 9. Share codes — concrete format

### 9.1 The honest framing

Full-fidelity plan sharing has an existing zero-risk path that already works: **drop the `.xml`
files in the folder and hit Reload.** `BagProfileStore.LoadProfiles` explicitly supports
newer-drop-in-wins imports (`BagProfiles.cs:242-253`). Codes are a *convenience for Discord*, not a
replacement. The plan therefore ships **three** transports, and is honest in the UI about which one
is which:

| Transport | Size | Use |
|---|---|---|
| **Reference code** `UIAP1-R-<id>-<rev>` | ~18 chars | "use the Printer plan" — resolves against the recipient's installed shipped plans |
| **Delta code** `UIAP1-D-<id>-<rev>-<payload>` | ~80-200 chars | "the Printer plan, but I moved 3 things" |
| **Full code** `UIAP1-F-<payload>` | ~700 chars for 9 profiles | any custom plan |
| (**File**) `StowPlans/<plan>/` folder or a single `.xml` | n/a | the primary, lossless path |

### 9.2 Wire format

```
UIAP1-<kind>-<body>

  UIAP   magic ("UI Ascended Plan")
  1      wire version (bump = a NEW prefix, never a reinterpretation of an old one)
  kind   R = reference | D = delta | F = full
  body   Base64url (RFC 4648 §5, A-Za-z0-9-_), NO padding
```

Binary payload before compression (all integers **LEB128 varint**, all strings
`varint length + ASCII bytes` — profile names are already ASCII-sanitised by
`ProfileCapture.SanitizeName`, `BagProfiles.cs:307`):

```
u8    payloadSchema        (1)
str   planId               ("" for a custom plan)
str   planDisplayName
varint rev
varint profileCount
  per profile:
    str    name
    str    badge            ("" = derive)
    varint itemCount
      per item:  str prefabName, varint priority
    varint categoryCount
      per cat:   u8 sortingClassOrdinal, varint priority
    varint slotClassCount
      per slot:  u8 slotClassOrdinal, varint priority
    varint uiaClassCount              (0 when Q1 is not adopted)
      per class: u8 uiaClassOrdinal, varint priority
varint mappingCount                   (the optional bag mapping)
  per entry: str bagPrefab, varint occurrence, varint profileIndex
u16   checksum                        (FNV-1a 32 folded to 16)
```

Then: `Deflate` (raw, `System.IO.Compression.DeflateStream` — **verified present** at
`rocketstation_Data/Managed/System.IO.Compression.dll`; needs one `<Reference>` line in
`Dev/StationeersUIMod.Dev.csproj`) -> Base64url -> prefix.

**Design decisions inside that format:**

- **Prefab NAMES, not `PrefabHash`.** A hash is 4 bytes vs ~17, but names deflate extremely well
  (every one starts `Item...`), survive a game update, are portable across mod sets, are readable
  in a bug report, and do not require a loaded world to decode. The size difference after Deflate
  is roughly 700 vs 540 characters — not worth the fragility.
- **Enums by ORDINAL, not name.** `SortingClass` and `Slot.Class` are stable, append-only game
  enums (verified: current values listed in §1.3); one byte each. Decode clamps an unknown ordinal
  to "skip this rule" and reports it, rather than throwing.
- **Raw `int` priorities, not the `RuleTier` tri-state.** A varint costs 1 byte for every value in
  use (10/50/60/100 all fit) — so the code is **lossless** even for hand-tuned XML priorities. The
  tri-state is a UI affordance and must not be baked into the wire format.
- **Checksum before encoding**, so a truncated Discord paste fails with *"this code is incomplete"*
  rather than importing half a plan.
- **A `rev` field** so "you have an older Printer plan than this code expects" is a clear message.

### 9.3 Realistic length — FlorpyDorp's 9-profile plan

Estimated rule count for *Stationpedia Ascended* (from §8.5): ~46 item rules + ~10 enum rules
across 9 profiles.

| Stage | Bytes | Note |
|---|---|---|
| Raw payload, prefab **names** | ~1040 | 46 x (1 + ~17 + 1) + 10 x 3 + 9 names/badges + framing |
| After Deflate | ~500-540 | the shared `Item`/`ItemKit` prefixes compress hard |
| Base64url (4/3) | **~700 chars** | plus the 7-char prefix |
| (alternative: prefab **hashes**) | ~400 raw -> ~390 deflated -> **~530 chars** | high-entropy, barely compresses; rejected above |

**700 characters is a copy-paste blob, not something a human types or reads aloud.** The UI must be
honest about that:

```
+--------------------------------------------------------------------------------+
| SHARE "Stationpedia Ascended"                                                   |
|                                                                                 |
|  Fingerprint:  K7QM-2ZXV        <- say this out loud to check you got the right |
|                                    code; it is not the code itself              |
|                                                                                 |
|  [ Copy plan code to clipboard ]        (about 700 characters - paste it into   |
|                                          Discord, do not try to type it)        |
|  [ Copy SHORT code ]  UIAP1-R-PRINTERS-3     (shipped plans only)               |
|  [ Open the plan folder ]               (send the folder for a perfect copy)    |
+--------------------------------------------------------------------------------+
```

- The **fingerprint** is the first 40 bits of the payload checksum in **Crockford Base32**
  (excludes `I L O U`, so no `1/l` or `0/O` confusion) — 8 characters, `XXXX-XXXX`. Purely for
  human verification; it is not decodable.
- **Import** is a `TMP_InputField` (`UiaUi.InputField`, `UiaUi.cs:129`) — Ctrl+V works because
  `TMP_InputField` uses `GUIUtility.systemCopyBuffer` internally and the Control Center holds
  `KeyInputState.Typing` while open (`UiaControlCenter.cs:248`). **Copy** needs
  `GUIUtility.systemCopyBuffer` directly -> add a `UnityEngine.IMGUIModule` reference to the csproj
  (**verified present** in the game's Managed folder).
- **Delta codes** are the real answer to "share your plan" for the common case where a player
  started from a shipped plan: encode `planId + rev` plus only the profiles that differ. A
  "Printer plan with 2 edited profiles" lands at ~150 characters. Worth designing in from v1;
  can be *implemented* in phase 4.

### 9.4 Import safety

- Import always lands as a **new plan** (`<name> (imported)`, de-collided) — it never overwrites an
  existing plan, and never touches the active one. Switching to it is a separate, explicit click.
- Unknown enum ordinals, unknown prefab names and unknown `planId`s are **skipped with a count**,
  not fatal: *"Imported 'Bob's Layout' — 9 profiles, 3 rules skipped (items this game version does
  not have)."* Same fail-soft posture as `HudDocument.Sanitize`.
- Codes carry **no file paths, no executable content, no assignments keyed to another player's
  save**. Worst case for a malicious code is a plan full of junk rules that the player deletes.

---

## 10. ImGui drag-drop verification — result

**Method:** the same reflection check used for `ImGuiTabItemFlags.SetSelected`
(`Windows/HudEditorWindow.cs:152-160`), run against the live install
`C:\Program Files (x86)\Steam\steamapps\common\Stationeers\rocketstation_Data\Managed\RG.ImGui.dll`.

**Result: PRESENT — and unusually complete.** Assembly `RG.ImGui, Version=0.0.0.0`, type
`ImGuiNET.ImGui` exposes:

```
BeginDragDropSource()                        BeginDragDropTarget()
BeginDragDropSource(ImGuiDragDropFlags)      BeginDragDropTargetCustom(Rect, UInt32)
EndDragDropSource()                          EndDragDropTarget()
SetDragDropPayload(String, IntPtr, UInt32)   AcceptDragDropPayload(String)
SetDragDropPayload(String, IntPtr, UInt32, ImGuiCond)
SetDragDropPayload(String, String,  ImGuiCond)      <- managed string overload
SetDragDropPayload(String, T,       ImGuiCond)      <- managed generic overload
AcceptDragDropPayload(String, ImGuiDragDropFlags)
AcceptDragDropPayload(String, out String, ImGuiDragDropFlags)
AcceptDragDropPayload(String, out T,      ImGuiDragDropFlags)
GetDragDropPayload()   IsDragDropActive()   IsDragDropPayloadBeingAccepted()   ClearDragDrop()
```

The `string`/`T` overloads mean no `Marshal.AllocHGlobal` dance is needed — a payload could
literally be a profile name string.

**But it is the wrong stack for this feature.** F10 is UGUI (§1.1). The relevant capability check
is the UGUI one, and it also passes: the mod already has **three working drag implementations** on
this exact stack —

- `UI/Grid/BagGridCell.cs:34` — item cell drag,
- `UI/Grid/GridTab.cs:801` — **manila-tab drag with a ghost layer at sortingOrder 5100** (the
  closest analogue to dragging a profile chip onto a plan card),
- `UI/Grid/PinnedInventoryWindow.cs:1148, 1199` — window/tab drag,

all via `IBeginDragHandler / IDragHandler / IEndDragHandler`, plus `Core/DragGhostLayer.cs` for the
carried visual. `UiaControls.UiaSlider` (`UiaControls.cs:95`) already proves `IDragHandler` works
inside the Control Center's canvas.

**One real UGUI caveat:** the Plans shelf sits inside a `ScrollRect`, which also consumes drag.
The fix is the standard one — the chip's `IBeginDragHandler` only claims the drag past a small
horizontal threshold, and forwards vertical drags to the parent scroll (`ExecuteEvents.Execute` to
`scrollRect`). `GridTab` already solves the same problem for tab drags; copy its threshold logic.

**Fallback, and what ships first:** a **right-click context menu** (`Copy to > / Move to >`) on
every profile chip, built from `GridProfilePopup`'s scrim+rows pattern (which is already a
self-contained, hot-reload-safe floating list). This ships in **phase 3**; drag ships in
**phase 4** as an accelerator on top. Reason: the context menu is discoverable, works with a
trackpad, works inside a scroll view, and is testable without a second pair of hands.

---

## 11. Click-count comparison

Counted from a cold start (`F10` counts as one input). "Hunt" = an unbounded scroll-and-read.

### Task A — assign a profile to one bag

| | Today (F10) | Today (in-game Grid) | Proposed (Bags sub-tab) |
|---|---|---|---|
| Steps | F10, Storage, **hunt/scroll ~700px**, dropdown, profile | B, profile-mode button, chip, profile | F10, Storage, (Bags is default), card dropdown, profile |
| Inputs | **1 key + 2 clicks + hunt** | 1 key + 3 clicks | **1 key + 3 clicks, no hunt** |
| Bag identified by | text only | its own grid region | **thumbnail + colour + name + #ordinal** |

*The click count barely moves. The win is that the hunt disappears and you can tell your two
backpacks apart.* The real click win is at scale:

### Task A' — set up six worn bags

| | Today | Proposed |
|---|---|---|
| | 6 x (hunt + 2 clicks) = **12 clicks + 6 hunts** | all 6 cards on screen: 12 clicks, **0 hunts** |
| With a plan | n/a (concept does not exist) | **Plans -> plan card -> [Apply to bags] = 3 clicks total** |

### Task B — add one item to a profile's catch list

| | Today | Proposed |
|---|---|---|
| Steps | hunt, dropdown, profile, `+Add item`, filter/type, item, Done | Profiles sub-tab, profile in list, `+Catch...`, item, Done |
| Inputs | **6 clicks + typing + 2 hunts** | **5 clicks + typing, 0 hunts** |
| Add a category | 2 clicks via a **sentinel dropdown** | 3 clicks via the same picker (`CATEGORIES` page) — 1 more click, but searchable and consistent |
| Add a slot class | 2 clicks in a **45-entry unsearchable dropdown** | 3 clicks, **searchable** |
| Change a priority | 2 clicks (open dropdown, pick) | **1 click** (cycle chip) |
| Rename the profile | **impossible in F10** | 2 clicks + typing |
| Delete the profile | **impossible in F10** (legacy ImGui only) | 2 clicks (with confirm) |

### Task C — switch preset sets

| | Today | Proposed |
|---|---|---|
| | **Impossible.** Nearest is "Create recommended profiles" = 1 click that appends 10 profiles you can never remove from F10. | Storage -> **Plans** -> click a plan card -> `[Use this plan]` = **3 clicks**, reversible, plus 1 more for `[Apply to bags]` |

---

## 12. Migration

Follows `Documentation/Config-and-Theme-Migration.md` disciplines throughout: never destroy
anything the player made; add a `ConfigMigration` step for every key change; ship content through a
manifest that can prove provenance.

### 12.1 Existing data — what happens to it

| Existing | Action | Rationale |
|---|---|---|
| `Profiles/profiles.xml` + `Profiles/*.xml` drop-ins | **Left in place, untouched.** On first run of the new build, its profiles are copied into a plan named **"My Profiles"**, which becomes the active plan. | The player's own work becomes a first-class plan instead of being "the old way". The originals stay as a safety net for one release. |
| `Assignments/<save>.xml` | **Untouched.** | Assignments key on `ReferenceId -> profileName`, and "My Profiles" keeps every name verbatim, so every assignment keeps resolving. This is the whole reason for the one-active-plan model (§3.1). |
| `prefab-defaults.xml` | **Untouched.** | Same: name-keyed. |
| `Loadouts/*.xml` | **Untouched**, and Loadouts stays as a section on the **Routing** sub-tab. Optionally offer *"turn this loadout into this plan's bag mapping"* (1 click). | See Q4 — whether Loadouts is eventually absorbed into plans is FlorpyDorp's call, not something to do silently. |
| The 4 legacy starters (`Construction`/`Atmospherics`/`Electrical`/`Farming`) | Migrate into "My Profiles" like anything else. Shipped plans deliberately **do not** use those four names. | `WriteStarterProfiles` only ever fires into an empty folder; those copies are frozen and un-updatable. Never collide with them. |
| `Windows/ProfileEditorWindow.cs` (legacy ImGui) | **Retire** once the new editor lands: remove the `SettingsWindow.cs:82` entry point, keep the file for one release, then delete. | Two editors over one store, with different rules (only the legacy one can delete). |

### 12.2 Shipped-plan delivery

Mirror `HudProfileStore.SyncShipped` exactly (`Documentation/Config-and-Theme-Migration.md` §3),
because that system is already proven and its failure modes are understood:

- new mod-folder source `StowPlans/` (repo root, alongside `HudProfiles/`);
- `StowPlanStore.SyncShipped(modDirectory)` at launch with a
  `StowPlans/.shipped-manifest` (`<relativePath>|<FNV-1a-64 of the canonical form>`);
- three moves only: **seed** (absent), **refresh** (pristine + we shipped a new version), **prune**
  (retired + pristine). Never overwrite an edited plan;
- `tools/package.ps1` gains a `StowPlans` copy step + a release audit ("all four shipped plans
  staged"), matching the existing `HudProfiles` block (`package.ps1:117, 136-165`);
- **the same hand-sync trap applies:** if any shipped plan is also embedded as a self-heal factory
  in code, the embed must stay canonically identical to the file, or the self-healed copy hashes as
  "player edited" and silently stops receiving updates. *Recommendation: do **not** embed shipped
  plans at all.* Unlike a HUD theme, a missing plan degrades gracefully (the router simply falls
  through to affinity/defaults), so the self-heal is not worth the maintenance hazard.

### 12.3 Config keys

| Key | Action |
|---|---|
| `StowActivePlan` (string) | **New.** Additive; no migration needed (new keys inherit their default automatically). |
| `StowLastSubTab` (string, session convenience) | **New**, additive. |
| Existing `Stow*` toggles | **Unchanged**, just re-parented to the Routing sub-tab. No key change, therefore no migration. |
| `GridProfileBadges`, `GridGhostHints`, `GridCellSize`, `GridKeyboardNav` | **Unchanged**, re-parented to the Grid sub-tab. |

If Q1 lands (`UIAClassRule`), no config migration is needed either — it is a new XML element on a
data class, not a config key. Old profiles simply have zero of them.

### 12.4 A rollback story

If a plan switch goes wrong, the player's original `Profiles/` folder is still there and "My
Profiles" is a plan they can switch back to. Add to the maintenance block:
**`[Rebuild "My Profiles" from the old Profiles folder]`** — one button, idempotent, non-destructive.

---

## 13. Implementation phases

Each phase is independently shippable, individually revertible, and leaves the mod in a coherent
state. Sizes are relative build effort: **S** ~ a day, **M** ~ 2-4 days, **L** ~ a week+.

| Phase | Contents | Size | Ships what |
|---|---|---|---|
| **P0 — Sub-tabs + Grid/Routing split** | Sub-tab strip in `StorageTab`; move the Grid knobs, the SmartStow chain, the test box and Loadouts to their sub-tabs. Zero data-model change. | **S** | The 40-row scroll dies. Instantly testable, instantly revertible. |
| **P1 — Bag cards** | The card grid (option 1A) + `ReferenceId`-keyed thumbnail cache + occurrence ordinals + `[Capture]` surfaced from `GridCapturePanel` + the prefab-default checkbox. Row density variant under Advanced. | **M** | FlorpyDorp's brief item #1. |
| **P2 — Profile editor** | Master-detail (option 2A); one rule list; `[+ Catch...]` unified picker (ITEMS page = existing `UiaItemPicker`, three new enum pages); click-to-cycle priority; **rename / duplicate / delete wired** (`RenameProfile` finally gets a caller); inline test. Retire the legacy ImGui editor entry point. | **M** | Brief item #2. Also fixes the missing-CRUD shipped bug. |
| **P3 — Plans: model + manager** | `StowPlan` model, `StowPlans/` on-disk layout, one-active-plan loading, migration of `Profiles/` into "My Profiles", plan cards + shelf (3A), plan CRUD, **right-click Copy to / Move to**, plan bag-mapping + `[Apply to bags]`. | **L** | Brief items #4 (structure) and most of #5. |
| **P4 — Shipped plans + sharing** | The four shipped plans authored against `SmartStow-Preset-Catalog.md`; `StowPlanStore.SyncShipped` + manifest; `package.ps1` step + audit; the `UIAP1` codec (R/F kinds), copy/import UI, fingerprint. | **L** | Brief items #4 (content) and #5 (share). |
| **P5 — Accelerators** | UGUI drag-and-drop for profile chips (with the scroll-threshold fix); delta codes (`UIAP1-D`); dev-side "generate By Printer from recipes" command; optional hover contents-strip on bag cards. | **M** | Polish. Nothing here blocks a release. |
| **P-opt — `UIAClassRule`** | The 4th rule kind (§3.2). **Should land before P4** if adopted, because it changes what the shipped plans are made of. | **S** | Makes plans 2 and 3 actually robust. |

**Suggested cut points:** P0+P1 is a shippable release on its own ("your bags, as cards"). P2 is a
second. P3+P4 is the headline release. P5 whenever.

---

## 14. Open questions for FlorpyDorp

**Q1 — `UIAClassRule` (the 4th rule kind).** Your own bag layout (Paints / Frames+Walls / Cables &
Pipes) does not exist in the game's 11-value `SortingClass`. The mod already has a 22-class
taxonomy with a 785-item table (`Core/UIASort.cs`). Do we let profiles match on it? **Yes** = plans
2 and 3 become a handful of clean rules that keep working for new and modded items. **No** = they
ship as ~45 hand-listed prefab rules that quietly rot. *My recommendation: yes, before P4.*

**Q2 — "Stow Plans" or another name?** Candidates, with reasoning:

| Candidate | For | Against |
|---|---|---|
| **Stow Plans** *(recommended)* | Plain English; unclaimed in mod and game; reads right for both shipped and user-made ("the Printer plan", "my plan"); short enough for a 100px sub-tab (`Plans`) | Slight overlap with "Loadout" as "a saved arrangement" — resolved by folding the bag mapping into the plan (Q4) |
| **Manifests** | Great station flavour; a manifest genuinely *is* a list of what goes where; distinctive | `.shipped-manifest` is already an internal file name (no player-facing clash, but a maintainer double-take) |
| **Collections** | Unambiguous "a group of profiles"; zero learning cost | Bland; says nothing about what it is for |
| **Schemes** | Carries "a whole coordinated set of settings" (colour scheme) | Slightly abstract; easy to confuse with HUD themes |
| **Libraries** | Literally accurate (a folder of profiles) | Technical/filing-cabinet energy; not fun |
| **Lockers** | Nice physical metaphor, on-theme | Implies storage of *items*, not of rules — actively misleading here |
| **Presets** | Perfect for the four shipped ones | Wrong for user-made ones ("my preset"?); the most overused word in mod UIs |
| **Kits** | Very Stationeers | **Collides with the game's own vocabulary** — `SortingClass.Kits`, `ItemKitWall`. Reject |

**Q3 — one active plan, or many live at once?** §3.1 recommends **one active plan** (profile names
stay unqualified, nothing else in the codebase changes). The cost: you cannot mix "the Ores profile
from plan A" with "the Tools profile from plan B" without copying one across (which is 2 clicks in
the plan manager). Acceptable?

**Q4 — does a plan absorb Loadouts?** A plan can carry a bag mapping, which is exactly what a
Loadout is. Options: (a) keep both, plan mapping is optional, Loadouts unchanged *(the safe default
this plan assumes)*; (b) plans absorb it, Loadouts becomes "save current bag mapping into this
plan" and the word retires over a release. (b) is cleaner but is a visible feature removal.

**Q5 — should the Bags sub-tab show unworn/nearby containers too?** Today everything is
worn-bags-only (`LoadoutStore.CollectWornBags`, depth 2). Crates and lockers can hold profiles in
principle. Scope creep, or the obvious next step?

**Q6 — "Profiles" is now used for three things** in F10: the HUD `Profiles` tab, the bag
`Profiles` sub-tab, and `HudProfileStore` vs `BagProfileStore` in code. Cheapest fix is to label
the new one **"Bag Profiles"** in all prose while the sub-tab button stays `Profiles`. Good enough,
or do you want the HUD one renamed (e.g. to `Themes`)?

**Q7 — share-code length.** ~700 characters for your 9-profile plan is a Discord paste, not
something anyone types. Is that acceptable with the 8-char fingerprint for verbal confirmation and
the folder/file path as the "perfect copy" alternative — or do you want plan sharing to be
file-and-short-code only, with no full code at all?

---

## 15. Risk register

| Risk | Likelihood | Mitigation |
|---|---|---|
| Plan switching orphans a player's assignments | Medium | One-active-plan + verbatim name preservation in "My Profiles" (§12.1). `StowRouter.TryProfile` already fail-softs on a dangling name. Add a one-line status on the Bags sub-tab: *"2 bags point at profiles this plan does not have."* |
| Thumbnail cache holds `Sprite`s into a dead world | Low | Key on `ReferenceId`, clear on tab teardown and on `UiaControlCenter.Shutdown` — exactly what `UiaItemPicker.Reset` and `StorageTab.ResetCaches` already do. |
| Card grid makes an F9 colour drag stutter | Medium | Honour `UiaControlCenter.IsRestyling`: reuse the last bag list and the last thumbnails on a restyle build. `StorageTab` already documents this as a *verified* perf finding (2026-07-20). |
| Shipped plan drifts from a code embed | Low | Do not embed shipped plans (§12.2). |
| Drag inside the scroll view fights the scroll | High if built naively | Threshold-and-forward, copied from `GridTab`'s tab drag. Ship the context menu first (P3), drag second (P5). |
| The `UIAP1` codec is a new attack-ish surface | Low | Data-only, length-prefixed, checksummed, fail-soft on every unknown ordinal/name; import always lands as a new inactive plan. |
| Hot-reload leaves stale statics | Medium | Every new static (active plan, shelf selection, thumbnail cache, drag state) gets a `Reset()` called from `UiaControlCenter.Shutdown` — the existing house rule. |

---

## 16. Files this plan would touch

*(For scoping only — nothing has been edited.)*

**New:** `Features/StowPlanStore.cs`, `Features/StowPlan.cs`, `Features/StowPlanCodec.cs`,
`UI/Menu/Tabs/Storage/BagsPage.cs`, `.../ProfilesPage.cs`, `.../PlansPage.cs`,
`.../RoutingPage.cs`, `.../GridPage.cs`, `UI/Menu/Kit/UiaCatchPicker.cs`,
`UI/Menu/Kit/UiaContextMenu.cs`, repo-root `StowPlans/`.

**Modified:** `UI/Menu/Tabs/StorageTab.cs` (becomes the sub-tab host),
`Features/BagProfiles.cs` (plan-aware load/save, `UIAClassRule` if Q1),
`Core/UIASort.cs` (expose `Classify` for the rule),
`UI/Menu/Kit/UiaItemPicker.cs` (source-page strip),
`Dev/StationeersUIMod.Dev.csproj` (`System.IO.Compression`, `UnityEngine.IMGUIModule`),
`tools/package.ps1` (`StowPlans` staging + audit),
`Windows/SettingsWindow.cs` (retire the legacy editor entry point),
`StationeersUIMod.cs` (plan load at init, resets at shutdown).

**Deleted (eventually):** `Windows/ProfileEditorWindow.cs`.
