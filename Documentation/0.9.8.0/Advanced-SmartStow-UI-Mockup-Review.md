# Advanced SmartStow UI: concept review and build notes

*2026-09-25. This reviews FlorpyDorp's ChatGPT concept images for the redesigned F10 SmartStow
screen: a three-column "Organizer" (Storage Layouts | Bags | Bag Profiles) with a share bar.
The images are in `concepts/`. The review fills the placeholders in
`SmartStow-Simple-Refactor-Plan.md` §10.3 and `F10-Settings-Refactor-Plan.md` §9. Nothing is
built. Background on today's system: `Documentation/SmartStow-Brief.md`.*

> **Status: `concepts/smartstow-organizer-3.png` is CHOSEN** (FlorpyDorp, 2026-09-25). It is the
> design of record. **Part D** lists what it decides and the fixes that are made at build time,
> not by regenerating the image. Part A is kept as history: it is the prompt that turned mockup 1
> into mockups 2 and 3.

- **Part A** is the revision prompt used on mockup 1 (history).
- **Part B** covers what building it takes.
- **Part C** lists the calls only FlorpyDorp can make, updated in Part D.
- **Part D** covers the chosen design.

---

## Part A: revision prompt (history, used on mockup 1)

This asked for two images and an optional third. Mockups 2 and 3 came from it; images 2 and 3 of
the prompt were never generated.

```text
Revise the attached "UI Ascended" settings mockup (Storage tab, Organizer page). Keep the
overall look exactly: the dark teal glass panels, the orange selected-tab style, the fonts, the
title bar with Simple/Advanced, pause and close buttons, the RADIAL MENUS / VISOR HUD switches,
the main tab row with Storage selected, the three-column layout, and the share bar along the
bottom. Make only the changes below.

TEXT RULE: every label uses plain letters, digits and basic punctuation only. Draw icons as
simple line icons. No emoji, and no arrows, check marks, bullets or other special symbols inside
text.

IMAGE 1 - the Organizer, main state

1. Mode bar. Directly under the main tab row, add a full-width bar: the label
   "SMART STOW MODE" on the left, then a two-option segmented switch:
   "Return Home" (unselected) and "Storage Layouts" (selected, orange). After the switch, a
   short muted line: "Storage Layouts: you decide which bag holds what, with bag profiles and
   rules."

2. Sub-tabs. The sub-tab row under the mode bar reads "Organizer" (selected), "Routing",
   "Universal Inventory". There is no tab called "Grid".

3. Left column, STORAGE LAYOUTS. Keep the "+ New Layout" button. Subtitle: "A layout is a set
   of bag profiles. One layout is active at a time." Five layout cards, each with a kebab (three
   vertical dots) menu button on the right:
   - "Stationpedia Ascended", "9 bag profiles", a cyan ACTIVE badge, and a highlighted border
     because it is also the selected card
   - "By Printer", "14 bag profiles", a "Use" button
   - "By Category", "12 bag profiles", a "Use" button
   - "Starter", "1 bag profile", a "Use" button
   - "My Layout", "5 bag profiles", a "Use" button
   The first four cards also carry a small muted "SHIPPED" tag under the name. "My Layout" does
   not.

4. Middle column, BAGS. Subtitle: "Pick a profile for each bag you carry. Press G and items go
   to the bag whose profile matches." Each bag card now starts with a small square thumbnail of
   the bag on the left, then the name and slot count, the Profile dropdown, and the buttons. Four
   cards:
   - "Mining Belt", Profile "Ingots and Ores", buttons "Capture" and "Edit"
   - "Backpack #1" (the thumbnail is painted orange), Profile "Materials", buttons "Capture"
     and "Edit". Show its kebab menu OPEN as a small dropdown panel with three items:
     "Never stow into this" (with a small off switch), "Rename bag...", "Clear profile"
   - "Backpack #2", Profile "(no profile)", buttons "Capture" and "Create"
   - "Cardboard Box", drawn in a warning state: an orange outline, a small orange "NO STOW" tag
     next to the name, and a dimmed profile dropdown
   Remove the Tool Belt and Utility Belt cards. Under the last card add one muted line: "Tool
   belts and suits can't take a profile. Tools still go to your tool belt automatically."

5. Right column, BAG PROFILES. Subtitle: "Profiles in the active layout. Click one to edit its
   rules." Keep "+ New Profile". Each profile row is: a small coloured badge with 2-4 capital
   letters, the profile name, a muted rule count, and a kebab menu button. Remove the priority
   numbers and remove the pencil and trash buttons from the rows. Rows:
   - "ORE" Ingots and Ores, "4 rules" (selected, highlighted)
   - "MAT" Materials, "6 rules"
   - "WALL" Frames and Walls, "21 rules"
   - "ELEC" Electronics, "8 rules"
   - "CAN" Canisters, "11 rules"
   - "PIPE" Cables and Pipes, "32 rules"
   - "MISC" Misc, "8 rules"

6. Editing section. The heading reads "EDITING: Ingots and Ores". Remove the Priority dropdown
   from the heading. Below it are four collapsible groups in this order: "By item - 0 rules",
   "By UIA class - 0 rules", "By slot class - 2 rules", "By category - 2 rules". Show "By slot
   class" EXPANDED. Inside it are two rule rows, each with the rule name, a small priority
   dropdown and a small X remove button: "Ingot" [Normal] [X] and "Ore" [Normal] [X]. Below
   those, a "+ Add slot class" button. Replace the footnote with: "When several rules match one
   item: item beats UIA class, UIA class beats slot class, slot class beats category. Priority
   only breaks ties within the same kind."

7. Share bar, SHARE LAYOUT. Subtitle: "Share a layout with other players as a code or a file."
   Controls, left to right: an "Export Layout" button, a text field with the placeholder "Paste a
   share code...", and an "Import" button. Under them, one muted line: "Export copies a share
   code and saves a file. Import always adds a new layout - nothing is overwritten." At the far
   right, a small status line: "Code copied - fingerprint 7KQ29XMA".

IMAGE 2 - the same screen while browsing a layout that is NOT active

Same as Image 1, with these changes:
- In the left column, "By Printer" has the highlighted (selected) border. "Stationpedia
  Ascended" keeps its ACTIVE badge but is not highlighted.
- The right column heading reads "BAG PROFILES in By Printer". Directly under it, a thin banner:
  "Viewing By Printer - not active. Its profiles don't route anything until you use it." It has
  a "Use this layout" button on the right.
- Profile rows: "AUTO" Autolathe, "TOOL" Tool Manufactory, "ELEC" Electronics Printer, "PIPE"
  Hydraulic Pipe Bender, "FURN" Furnace, "RAW" Not Printed. Each row has a small "Copy to active"
  button instead of a kebab menu.
- The editing section shows "VIEWING: Autolathe" in read-only form: rule rows with no X buttons
  and no "+ Add" button.
- The Bags column header gains a muted line "Using: Stationpedia Ascended". The bag cards are
  unchanged.

IMAGE 3 (optional) - Return Home mode

Same window. In the mode bar, "Return Home" is selected, and the helper line reads "Return
Home: every item goes back to the bag and slot you last put it in. Move it yourself and its
home moves." The sub-tabs are "Return Home" (selected) and "Universal Inventory". The page is
one panel:
- "Homes remembered in this world: 143"
- a highlighted box: "In your hand: Steel Sheets x50" and, under it, "Home: Backpack #2, slot 4
  (you put it there)"
- a switch row: "Tell me when an item has no home yet, or its home is full"
- two buttons: "Forget homes in this world" and "Re-learn homes from what I carry now"
- a muted line: "2 containers are set to never stow. Change this under Storage Layouts > Bags."
```

---

## Part B: what building it would take

### B1. The model doesn't change

This design is a new face on the existing data. The only renaming is what the player sees:

| Player sees | Code, XML and folders keep |
|---|---|
| Storage Layout | Stow Profile (`StowProfileStore`, `StowProfiles/`, `<StowProfile>`) |
| Bag Profile | Bag Profile (unchanged) |
| Return Home / Storage Layouts | `[6. SmartStow+] Mode = Simple / Advanced` (Simple plan S-7) |

Keeping the folder name means no file-rename migration is needed. The footer text in mockup 1
("the Layouts folder") must not be taken literally.

There is one **optional** data addition: a per-profile **badge colour**. It would be an additive
XML attribute next to the existing `badge`, so no config migration is needed. It must also
travel in share codes, which means a new flag bit or schema step in `StowShareCodec`. A
zero-cost alternative is to derive the colour from the badge text.

### B2. Every control maps to an existing call

| Mockup control | Backed by (today) |
|---|---|
| Use / Use this layout | `StowProfileStore.SetActive` then `BagProfileStore.LoadProfiles` |
| + New Layout | `StowProfileStore.Create` |
| Layout ⋮: Rename / Duplicate / Delete | `RenameSet` / `Create` + `TransferMany` / `DeleteSet` (keep the arm/confirm step) |
| Browsing a non-active layout | `StowProfileStore.ProfilesOf(name)` (already served read-only) |
| Bag card Profile dropdown | `BagProfileStore.Assign` + `ItemActions.LabelWith` (rename-on-assign) |
| Bag ⋮: Never stow into this | `BagProfileStore.SetStowExcluded` |
| Bag ⋮: Rename bag | `ItemActions.RenameThing` (the multiplayer-safe labeller funnel) |
| Bag card Capture | `ProfileCapture.BuildProposal` / `Apply` |
| Bag card Create | small new helper: a profile named after the bag + `Assign` (the `PinItemRule` path without an item) |
| Bag card Edit | selects that profile in the right column (UI only) |
| Profile ⋮: Copy to / Move to / Copy to active | `StowProfileStore.TransferProfile` |
| Profile ⋮: Rename / Delete | `BagProfileStore.RenameProfile` / `DeleteProfile` (these already cascade through every save) |
| Rule groups, priority, X, + Add | the existing rule list and add controls in `StorageTab.cs` |
| Export Layout / Import | `StowShareCodec.Encode` + `WriteExportFile` / `Decode` + `StowProfileStore.ImportDoc` |
| Routing sub-tab | today's Settings page, unchanged (router switches, depth slider, test box) |
| Universal Inventory sub-tab | today's Universal Inventory block |
| Return Home page (image 3) | Simple plan §10.1 (P4) |

### B3. What is genuinely new

- **Three-column Organizer page.** It replaces today's four sub-tabs. Three ~300 px columns fill
  the same width as today's three 298 px bag cards (`StorageTab.cs:309-312`). The page needs
  independent scrolling per column.
- **Kebab popup menu.** There's no such widget in `UI/Menu/Kit/UiaControls.cs` today. This is
  one reusable control, used on layout, bag and profile rows.
- **Collapsible rule groups**, which are also new in the kit.
- **Drawn icons.** The kebab, chevron, pencil, capture bracket and share icons have to be
  `PolygonPanelGraphic` shapes or sprites taken from the game. The game font is ASCII-only.
  `UiaControls.SetButtonIcon` already accepts a sprite.
- **Capture dialog hosted over F10.** `GridCapturePanel` is a Universal Inventory surface. It
  needs a sort order above F10, or an F10 twin that reuses `ProfileCapture`.
- **Read-only state** for the rule editor while browsing a non-active layout.

### B4. What gets retired

"Hide, never destroy" applies to all of these: the functions stay, only the UI buttons go.

- **The single-Bag-Profile file share.** This is the Bag Profiles page's Export and "Import
  shared profiles" (`BagProfileStore.ExportProfile` / `ImportSharedProfiles`). Layout sharing
  plus "Copy to" covers the same need, and it removes the second sharing system.
- **"Create recommended profiles."** The shipped layouts replace it.
- **The legacy ImGui SmartStow+ section and its "Open profile editor" button** in
  `Windows/SettingsWindow.cs:74-85`, reachable from F10 → Radial → "Open the radial editor".

### B5. Sequencing and size

- **Order of work.** Build it after the Simple plan's P4 (the mode bar here *is* P4's mode block).
  Build it on the F10 plan's Kit v2, since the popup menu and collapsible groups belong in the
  kit, not in `StorageTab`.
- **Size.** Rough estimate: **L** (a week or more), almost all UI. There are no router or store
  changes, apart from the optional badge colour and the small Create helper.

---

## Part C: calls for FlorpyDorp (the prompt assumes the first option)

1. **Tool belts stay unable to own a profile?** This is the current ruling
   (`BagProfileGate.cs:62`). Mockup 1 gave the Tool Belt a profile.
2. **Mode names.** "Return Home" / "Storage Layouts", or "Simple" / "Advanced"? The latter
   clashes with F10's own Simple/Advanced button, which sits on the same screen (Simple plan Q9).
3. **Player-facing rename** "Stow Profile" → "Storage Layout", with code and folders unchanged?
4. **Badge colours.** Chosen per profile (new data), or derived from the badge text (no new
   data)?
5. **Retire the single-profile file share** in favour of layout sharing + "Copy to"?

---

## Part D: the chosen design (`concepts/smartstow-organizer-3.png`)

### D1. What it decides

- **Main tab:** Storage becomes **SmartStow**.
- **Mode bar:** "SMART STOW MODE", with a **Simple / Complex** switch and a one-line explanation of
  each. This answers C2 and the Simple plan's Q9: the player-facing name for today's system is
  **Complex**. Since the Simple plan's `Mode` key hasn't shipped, its values can match
  (`Simple / Complex`).
- **Sub-tabs:** **Organizer / Routing / Universal Inventory**. Today's Settings page becomes
  Routing.
- **C1, tool belts:** they still can't take a profile. The note is on screen.
- **C3, rename:** yes. Players see **Storage Layout**; code, XML and `StowProfiles/` keep their
  names.
- **C5, single-profile file share:** retired, by implication. The Bag Profiles page's share block
  is gone and only layout sharing remains.
- **C4, badge colours:** still open. The image colours every badge differently, but doesn't say
  whether players pick the colours or they're derived from the badge text.
- **Layout icons:** new in #3 (document, briefcase, rocket). Shipped layouts can get fixed icons,
  and player-made layouts a default document icon. Letting players pick an icon would be new
  optional data, like the badge colour.

### D2. Fixes made at build time (no new image needed)

| # | In the image | At build time |
|---|---|---|
| 1 | No Smart Stow on/off switch | Add an on/off switch at the right end of the mode bar (Simple plan §10.1; F10 plan principle 1). |
| 2 | Rule editor is a band about 100 px tall with four groups side by side | Shipped profiles carry 100+ item rules (559 in By Printer). Keep the four group headers; the expanded group opens a scrolling VirtualList, with a search box in "By item". The band grows while editing, and the columns above shrink. |
| 3 | "Export Layout to File" and "Generate Share Code" are separate buttons | Today they are one action: `StowShareCodec` copies the code to the clipboard **and** writes `StowProfiles/Export/<name>.txt`. Make them one "Export Layout" button, or keep two buttons that call the same code path. |
| 4 | "Import Layout from File" | There is no in-game file browser, and a `.xml` dropped into `StowProfiles/` already appears in the list by itself. Make it "Import" with a paste box that falls back to the clipboard, as today's Import code button does. |
| 5 | Share-bar text: "export a file for full fidielity" | The code and the file carry the same content. Suggested text: "Export copies a share code and saves a file. Import always adds a new layout." |
| 6 | Mode switch "Simple" next to the title bar's "Simple / Advanced" | Resolve with F10 plan Q3/Q4: per-section "More options", or rename the density toggle. |
| 7 | Density needs about 1450×950 at the kit's text sizes | F10 plan Q2 (window size). |
| 8 | The "which rule wins" footnote was dropped | An (i) popover next to EDITING (F10 plan principle 6). |
| 9 | The NO STOW card dims its profile dropdown | An excluded container may still keep a profile. Dimming is fine, but keep the dropdown usable. |
| 10 | Placeholder data (slot counts, "High" on the shipped Ingot/Ore rules) | The real values show. The shipped slot-class rules are 60 = Normal. |

### D3. Screens that still have no image

- **The Simple-mode page.** The Simple plan's §10.1 sketch stands in until there is one.
- **Browsing a layout that isn't active.** This needs a banner, read-only rules and "Copy to
  active".
- **The Routing and Universal Inventory sub-tabs.** They hold today's controls, restyled.
- **The capture dialog over F10**, and the look of text inputs and confirms. These are also open
  questions in the F10 plan's §9.
