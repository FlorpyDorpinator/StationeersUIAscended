# SmartStow: how profiles work, and the settings UI

*2026-09-25. This describes the code as it is now (0.9.7.4), not a plan. It contains two briefs:
a very simple one first, then a detailed one. Code references are collected at the end.*

---

# Part 1: The very simple brief

**SmartStow is the G key.** You hold an item, press G, and UI Ascended decides which bag it goes
into. You don't have to open your inventory.

**You teach it with profiles.** A **Bag Profile** is a short list of what one bag should hold,
for example "Ores: anything that is an ore or an ice". You give a bag a Bag Profile, and from
then on G sends matching items to that bag.

**Profiles come in sets.** A **Stow Profile** is a named set of Bag Profiles, like a
"Mining trip" set or a "Base building" set. Only one set is active at a time. Switching sets
never deletes anything. A bag whose profile isn't in the current set just stops receiving items
until you switch back.

**It works without setup too.** If a bag has no profile, G still tries sensible things: it tops
up a stack you already carry, fills an empty battery or canister socket, or sends the item to a
bag that already holds similar items. If nothing fits, the game's normal G behaviour runs.

**Where you set it up:**
- **F10 → Storage** has four pages. **Bags** lists the containers you're carrying, with a
  picture of each. There you pick a Bag Profile for each one or mark it "never stow into this".
  **Bag Profiles** is where you edit the lists. **Stow Profiles** is where you pick, copy and
  share sets. **Settings** has the on/off switches and a test box that shows where an item
  would go.
- **In the inventory window**, the luggage-tag button turns on profile mode. Each bag then shows
  a label you can click to change its profile, and a **CAPTURE** button. CAPTURE creates a
  profile from what the bag holds right now. Dragging an item onto a bag's tab adds that item
  to the bag's list.

The mod ships with four ready-made sets: **Starter**, **By Category**, **By Printer** and
**Stationpedia Ascended**.

---

# Part 2: The detailed brief

## 1. What SmartStow does

Pressing **G** (vanilla Smart Stow) with an item in hand runs UI Ascended's router first. The
router is a fixed chain of steps. The first step that finds a destination wins. The item then
moves with **one** multiplayer-safe move or merge. If every step declines, the unmodified
vanilla Smart Stow runs instead. The router only decides. It never moves anything itself, so the
test box and the drag-glow preview use the same router as a real G press and always agree with
it.

## 2. The model: three layers plus two per-save extras

```
Stow Profile "By Category"         (a named set: one file, ONE set active at a time)
 ├─ Bag Profile "Ores"             (a named list of match rules)
 │    ├─ Category = Ores
 │    └─ Category = Ices
 ├─ Bag Profile "Tools"
 └─ ...

Assignments (per SAVE):  this Mining Backpack  ->  "Ores"
                         that Crate             ->  "Tools"
Exclusions  (per SAVE):  this Cardboard Box     ->  never stow into it
Type memory (per SAVE):  Iron Ingot             ->  last went into that Crate
```

| Thing | What it is | Scope |
|---|---|---|
| **Bag Profile** | A name, an optional 2–4 letter badge, and a list of rules. | Lives inside a Stow Profile. |
| **Stow Profile** | A named, described set of Bag Profiles. Exactly one is **active**. Only the active set's Bag Profiles exist as far as routing is concerned. | Global (per machine, all saves). |
| **Assignment** | "This specific container uses Bag Profile *X*". Stored by the container's ID and the profile **name**. | Per save. |
| **Exclusion** | "Never stow anything into this container." Independent of assignment; it applies to every router step. | Per save. |
| **Type memory** | "The last item of this type went into that bag." Recorded automatically after profile and affinity stows. | Per save. |

**Assignments point at a name, not at a set.** That's why switching Stow Profiles is safe:

- If the new set has a Bag Profile with the same name, the bag keeps working.
- If it doesn't, the bag "goes quiet" and is treated as unassigned. The mapping is **kept**, and
  the UI shows it as `Ores (not in this Stow Profile)`, an inert entry you can't select by
  accident.
- Switching back brings it straight back.

## 3. How a Bag Profile matches an item

A Bag Profile has four kinds of rule:

| Rule kind (UI tag) | Matches on | Match strength |
|---|---|---|
| **ITEM** | One exact item (prefab), e.g. Steel Sheets. | Strongest |
| **UIA** | The mod's own 22-way item classes (Tool, Ore, Ingot, Material, Kit, Electronics, Food, Seed, Medical, Filter, Tank, ...). Sharper than the game's categories and they also cover modded items. | ↓ |
| **SLOT** | The game's slot class (Battery, GasCanister, Ore, Plant, Tool, ...). | ↓ |
| **CAT** | The game's 11 sorting categories (Ores, Ices, Tools, Food, Kits, Resources, ...). | Weakest |

- **The kind always beats the priority.** Any matching ITEM rule beats any UIA rule, which beats
  any SLOT rule, which beats any CAT rule, whatever their priorities.
- **Priority only breaks ties between rules of the same kind.** The UI shows priority as three
  steps: **High / Normal / Low** (100 / 50 / 10). Hand-edited XML can use any number, and the
  UI shows the nearest step.
- **When two bags both want an item,** the stronger match wins. On an exact tie the bag nested
  less deeply wins.
- **Rules can only add items.** There is no "not" rule. To keep a specific item out of a broad
  rule, give another bag a stronger rule for that item.

## 4. How G decides: the router chain

| # | Step (test-box tag) | What it does | Settings switch |
|---|---|---|---|
| 1 | **BELT** | A real tool goes to your worn toolbelt, into its remembered home slot when possible. If the belt is full it goes to your worn back container. | Advanced: "Stow tools onto the toolbelt first" |
| 2 | **STACK** | Tops up a matching partial stack anywhere you can reach. On a host or single-player, the leftover continues into the same bag. | "1 - Prefer topping up matching stacks" |
| 3 | **SOCKET** | A component (canister, battery, filter, cartridge, board, chip) goes into a matching **empty socket** (for example a suit tank or a tool's battery slot) before any bag. | "2 - Route components to their sockets" |
| 4 | **PROFILE** | Goes to the bag whose **assigned** Bag Profile matches best. | "3 - Use bag profiles" |
| 5 | **AFFINITY** | For bags **without** a profile: goes to a bag that already holds the same or similar items. It needs a minimum score to fire. | "4 - Route to bags with similar contents" |
| 6 | **DEFAULT** | Bags with no profile of known types act as if they had one: tool belts use "Tools", mining belts and mining backpacks use "Ores", the horticulture belt uses "Farming". This only happens if the active set **has** a Bag Profile with that name and it matches the item. | "5 - Known bag types get a default" |
| 7 | **MEMORY** | Goes to the bag where this item type last went in this save. | "6 - Remember where each type went" |
| 8 | **GENERIC** | Loose build items (walls, frames, kits) go to one consistent general bag instead of whichever comes first. Among bags without a profile it picks the one with the most items of that category, then the emptiest, then the shallowest. | "7 - Loose build items go to one bag" |
| 9 | **BELT\*** | Last resort for "fake tools" like cable coils: a bare toolbelt slot. | same switch as step 1 |
| — | *(vanilla)* | Nothing matched, so the game's own Smart Stow runs. | — |

Rules that apply to every step:
- Excluded containers are skipped.
- Hands are never destinations.
- Consumable packaging is never storage (water-bottle bags, cereal boxes, starter supply
  boxes).
- Bags that carry a profile are "law": affinity and generic fallback never dump off-profile
  items into them.
- "How deep to search" (1–5) controls how far into bags inside bags the router looks.

## 5. Which containers can have a Bag Profile

**Can:** backpacks (including mining backpacks), mining belts, cardboard boxes, crates.

**Can't:** jetpacks, tool belts, suits and uniforms, tools that happen to have slots (such as
the Terrain Manipulator), and disposable packaging.

Tool belts still **receive** items through the belt, default and affinity steps. They just can't
own a profile. A profile assigned to an ineligible container is kept on disk but ignored, and
the log says so once.

Assigning a profile to a **typed** pack (for example a mining backpack whose slots only take ore)
runs a check. The mod warns you if some rules can never land there, e.g. "3 rules cannot fit
there: ...". It's a warning only. It never blocks the assignment.

## 6. Stow Profile lifecycle

- **Shipped sets:** four sets are written to disk once:

  | Set | Bags |
  |---|---|
  | **Starter** | 1 bag, "Starter Haul": ores, ices, tools, food |
  | **By Category** | 12 bags: one per game category, plus Electronics and Other |
  | **By Printer** | 14 bags: one per fabricator, plus Not Printed |
  | **Stationpedia Ascended** | 9 bags, FlorpyDorp's own layout: Paints, Materials, Frames and Walls, Ingots and Ores, Electronics, Liquids and Gases, Cables and Pipes, Canisters, Misc |

  After seeding, the sets belong to the player: edits and deletions stick and are never
  refreshed automatically. **Restore shipped Stow Profiles** (armed with a confirm step) is the
  only way to get the originals back, and it overwrites those four names.
- **Your first set:** on first launch the mod creates **"My Stow Profile"**. It holds your old
  pre-set bag profiles if you had any; otherwise it holds four starters (Construction,
  Atmospherics, Electrical, Farming). This runs once, marked by a stamp file, and the old
  `Profiles/` folder is left untouched.
- **Switching** rewrites the "active" marker and reloads. It changes no assignments.
- **Deleting** a set is not allowed for the last remaining one. Deleting the active set switches
  to another set. Container mappings survive, they just go quiet.
- **Copy/Move between sets:** you can copy or move a single Bag Profile from any set to any
  other. If the name is already taken, the copy gets "(2)". Mappings follow the **name**.
- **Sharing a whole set:** Export produces a `UIAP1-F-...` code. The code goes to the clipboard
  and a copy is saved to `StowProfiles/Export/`, along with an 8-character fingerprint you can
  read out loud to confirm the code arrived intact. Import always creates a **new, inactive**
  set and never overwrites. Rules naming items this game version lacks are kept, in case they
  belong to a mod or a newer build. Dropping a `.xml` into `StowProfiles/` also works.
- **Sharing one Bag Profile** is an older, separate system. Export writes
  `Profiles/<name>.xml`. The receiver drops the file into their `Profiles/` folder and presses
  **Import shared profiles**. That merges it into the active set, replacing a same-named profile
  only if the file is newer.
- **Renaming or deleting a Bag Profile** also updates every reference to it: assignments in
  **every** save, loadout entries and prefab defaults. A delete leaves exported copies alone.

## 7. Where it's saved

All paths are under `BepInEx/config/StationeersUIMod/`.

| Path | Holds |
|---|---|
| `StowProfiles/<name>.xml` | One Stow Profile each, holding its Bag Profiles and rules. |
| `StowProfiles/.active` | The name of the active set. |
| `StowProfiles/.shipped`, `.migrated` | Seed-once and migrate-once markers. |
| `StowProfiles/Export/` | Text copies of exported share codes. |
| `Assignments/<save>.xml` | Per save: container → Bag Profile, exclusions, type memory. |
| `Profiles/` | Legacy store, no longer loaded. Now only the single-profile share drop-box. |
| `com.stationeersuimod.ui.cfg` | The on/off switches (section "6. SmartStow+"), plus `RenameBagOnProfileAssign`. |

## 8. The UI

### 8.1 F10 → Storage tab

A strip of four sub-tab buttons sits above a scrolling page. F10's global **Simple/Advanced**
density switch changes some pages.

**Bags**: the mapping screen.
- **Status line:** "3 containers on you - 2 with a profile", with a **Refresh** button.
- **Toggle:** "Rename a bag when a profile is assigned" (on by default). It labels the
  container with the profile's name through the game's own Labeller path, so it's
  multiplayer-safe. Clearing a profile never renames the bag back.
- **Card grid**, three columns, one card per assignable container you're carrying. Each card
  has:
  - a live thumbnail, which shows the bag's paint colour;
  - its name, with "#2" added when you carry two of the same kind;
  - a type line such as "Mining Backpack - 8 slots, 3 used";
  - a **Bag Profile dropdown** ("(no profile)" plus the active set's profiles, plus the inert
    "not in this Stow Profile" row when relevant);
  - a **"Never stow into this"** toggle, which gives the card a warning-coloured outline;
  - a **Rename** box with an OK button.
- **Advanced** swaps the cards for dense one-line rows with the same controls.
- Footnotes explain two things: bags without a profile still receive items by affinity, and a
  bag mapped to another set's profile is kept but not routed to.

**Bag Profiles**: the rule editor for the **active** set.
- **Quick setup:** "Create recommended profiles" adds ten one-rule category profiles (Tools,
  Resources, Kits, Food, Clothing, Atmospherics, Storage, Ores, Ices, Appliances). It skips
  names that already exist and assigns nothing.
- **Edit a profile:**
  - a **Profile** dropdown, plus **New** and **Export**;
  - a rename field with **Rename** and **Delete** (two-click confirm);
  - the **rule list**, one row per rule: a kind tag (ITEM / UIA / SLOT / CAT), its value, a
    **Priority** dropdown (High/Normal/Low) and an **X** to remove it;
  - add controls: **"+ Add item..."**, which opens a searchable item picker, and three
    dropdowns for **By UIA class**, **By category** and **By slot class**;
  - a note explaining that item beats UIA class beats slot class beats category.
- **Share:** **Reload profiles** and **Import shared profiles**.

**Stow Profiles**: the set manager.
- **Set list**, one row per set:
  - the name, with `*` marking the active set and "(shipped)" marking the four presets;
  - counts, e.g. "9 profiles - 31 rules";
  - **"in use"** on the active set, or a **Use** button on the others.

  Clicking a name only **browses** that set. **Use** is a separate click, so you can look
  inside a set, or copy one profile out of it, without switching.
- **For the browsed set:**
  - its description;
  - a rename field;
  - buttons: **Rename**, **Duplicate**, **New**, **Refresh list**, **Delete** (confirm).
- **Shelf**, "Bag Profiles in 'X'": one row per profile with its rule count and two
  target-picker dropdowns, **Copy to...** and **Move to...**.
- **Share:**
  - **Export "X" as a code**;
  - a paste box (leave it empty to read the clipboard);
  - **Import code**;
  - **Copy again**.
- **Maintenance:** **Restore shipped Stow Profiles** (confirm).
- **Status feedback:** results of each action appear as a one-line note, e.g. "Now using 'By
  Category' (12 bag profiles)".

**Settings**: switches and diagnostics.
- **Universal Inventory:**
  - cell size;
  - scroll-to-select;
  - "Show profile tags on bag tabs";
  - "Glow the bag Smart Stow would pick while dragging".
- **Smart Stow (G):**
  - Enable SmartStow+;
  - the seven numbered router switches (§4);
  - the "How deep to search" slider.

  Advanced adds four more: tools-to-toolbelt first, nested-bag stow, profile-aware sort (a
  profiled bag's matching items sort first), and "Show where items were stowed (and why)"
  (a toast).
- **Status note:** how many containers in this save are excluded.
- **Test box:** "Test an item..." opens the item picker and does a dry run. It shows the winner
  as `-> Mining Backpack (PROFILE: Ores)` and the runner-up as `next: ...`. Nothing moves.

### 8.2 Universal Inventory (the inventory window): profile mode

- **Profile button:** a **luggage-tag button** in the window header toggles profile mode. It
  stays highlighted while the mode is on. Esc or closing the window exits the mode.
- **In profile mode:**
  - Item cells dim, so the bag chrome stands out.
  - Every assignable bag region gets a strip along its top edge with two controls:
    - a **profile chip** showing the bag's current profile. Clicking it opens a small dropdown
      with "(no profile)" and every profile in the active set; `*` marks the current one.
    - a **CAPTURE** button. It opens a confirm dialog:
      - a name field;
      - one checkbox row per proposed rule. The bag's contents are grouped: three or more
        items sharing a category become a category rule, three or more of the rest sharing a
        slot class become a slot-class rule, and anything left over becomes item rules. An
        **AS ITEMS** toggle expands a group back into individual item rules.
      - if the bag already has a profile, a choice of **NEW / UPDATE / MERGE**;
      - **SAVE** and **CANCEL**.

      Saving creates or updates the profile and always assigns it to that bag.
  - **Drag an item onto a bag's tab** to add an ITEM rule for it to that bag's profile. If the
    bag has no profile, one named after the bag is created first. The tab flashes to confirm.
    The item itself doesn't move.
  - **Ghost glow:** while you drag, the bag that G *would* choose glows with the accent border.
- **Outside profile mode:** bag tabs show the profile's 2–4 letter badge (e.g. `ORE`) when that
  option is on.

### 8.3 In play

- **Stow flash:** after a routed G press, the destination slot flashes.
- **Toast:** optional (off by default). A one-line toast names where the item went and why.
- **Radial:** G still works while a radial menu is open.

### 8.4 Console (read-only diagnostics)

`stowprofiles` prints:
- the active set and the other sets;
- the active set's profiles;
- every reachable container with how the router will treat it: assigned, not assignable, not
  in this set, or excluded.

Sub-commands: `stowprofiles rules`, `stowprofiles export <set>`, `stowprofiles roundtrip`.

## 9. Rough edges worth knowing before a redesign

- **Two words that sound alike.** "Bag Profile" and "Stow Profile" are easy to confuse. So are
  the four sub-tabs "Bags", "Bag Profiles", "Stow Profiles" and "Settings".
- **Two sharing systems.** Single Bag Profiles use a file in `Profiles/` plus an **Import shared
  profiles** button. Whole sets use a `UIAP1` code plus **Import code**. They behave
  differently (merge-into-active vs. always-new-set).
- **Edit only the active set.** The rule editor only edits the active set. The Stow Profiles
  page can browse inactive sets but can't edit their rules.
- **No drag-and-drop between sets yet.** Copying and moving profiles between sets goes through
  the "Copy to... / Move to..." dropdowns.
- **An old, inconsistent second UI still exists.** F10 → Radial → "Open the radial editor" opens
  the legacy ImGui settings window. It still has a SmartStow+ section with outdated numbering
  and an "Open profile editor" button that opens the old ImGui Bag Profile editor.
- **Numbering doesn't match the router.** The Settings page numbers its switches 1–7, but the
  toolbelt step (router step 1) sits in Advanced, unnumbered.
- **ASCII-only text.** The game's font renders only plain ASCII. Displayed text can't use icons,
  arrows or check-mark glyphs; those must be drawn shapes.
- **Changing soon.** FlorpyDorp decided on 2026-09-25 (D-016) to make a new **"Simple
  SmartStow"** the default: per-save, "return each item to where it came from". Today's
  profile system becomes **"Advanced SmartStow"**, and its UI redesign waits on his concept
  images. See `Documentation/0.9.8.0/README.md`.

## 10. Source map

| Area | File |
|---|---|
| Router (the decision chain) | `Assets/Scripts/StationeersUIMod/Core/StowRouter.cs` |
| G-key executor, flash, toast, memory | `Features/SmartStowPlus.cs` |
| Bag Profile model, rules, assignments, exclusions, memory | `Features/BagProfiles.cs` |
| Stow Profile store (active set, seed, migrate, copy/move) | `Features/StowProfileStore.cs` |
| Shipped presets (hand-synced with `Documentation/SmartStow-Preset-Catalog.md`) | `Features/ShippedStowProfiles.g.cs` |
| Which containers can have a profile, typed-pack warning | `Features/BagProfileGate.cs` |
| Share codes | `Features/StowShareCodec.cs` |
| CAPTURE and drag-to-pin | `Features/ProfileCapture.cs`, `UI/Grid/GridCapturePanel.cs`, `UI/Grid/BagGridCell.cs` |
| Profile mode, chip dropdown, ghost glow | `UI/Grid/GridProfileMode.cs`, `GridProfilePopup.cs`, `GridGhostHint.cs`, `GridRegionView.cs` |
| F10 Storage tab | `UI/Menu/Tabs/StorageTab.cs` |
| Switches | `UIAConfig.cs` (section "6. SmartStow+"), `Features/StowRenameConfig.cs` |
| Console diagnostic | `Core/StowCommands.cs` |

All `Features/`, `UI/` and `Core/` paths are relative to `Assets/Scripts/StationeersUIMod/`.
Earlier design history: `Documentation/SmartStow-Profiles-Redesign-Plan.md` and the dated
"SmartStow B1–B5" Changes Reports.
