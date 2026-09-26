# F10 Control Center — Settings Refactor Plan (0.9.8.0)

*A plan-level document for FlorpyDorp to review section by section. **No code was changed to write
this.** Decisions for FlorpyDorp are marked **YOUR CALL** and collected in §12. Code paths are
relative to `Assets/Scripts/StationeersUIMod/`. Three files are being patched in this wave
(`UI/Menu/Kit/UiaUi.cs`, `UI/Menu/UiaControlCenter.cs`, `UI/Menu/Tabs/HudTab.cs`); their line
numbers below are from the working tree with those patches applied.*

*Companions:*
- *`SmartStow-Simple-Refactor-Plan.md` (this folder; its §10 is the first new screen this refactor
  hosts);*
- *`Documentation/Release Prep Reports/05-F10-Control-Center.md` (the 2026-07-25 audit; its open
  polish items are folded into §3);*
- *`Documentation/SmartStow-Profiles-Redesign-Plan.md` (the 0.9.7.0 wireframes for the Storage
  screens).*

---

## 0. The ask

- **FlorpyDorp, 2026-09-25, on triage D-009:** "Yeah we need to refactor these whole menus".
- **His 08-03 reply** to Ningy's D-009 report (the F10 rename box renders as a thin line and typed
  text isn't visible): "Rebuilding those menus from scratch. They suck." He showed concept art and
  estimated "days of work" (from the triage record).
- **Images are coming.** He is generating ChatGPT UI concept images for the Advanced SmartStow
  screens, and this refactor must be able to absorb them (§8, §9).
- **Scope:** a plan only. The three targeted patches landing in this wave are **Stage 0** (§7).

## 1. TL;DR

- **The shell is good; the kit underneath it is thin.**
  - The shell: a live HUD-glass window, a palette that follows the HUD theme, throttled restyle,
    hot-reload safety.
  - The kit: one factory class, four widgets and six row builders — and **no composite components**.
- **Almost every pain point traces back to four structural gaps:**
  1. Widgets have no intrinsic size, state or handles.
  2. The page model rebuilds everything, and keeps its state in tab fields and statics.
  3. One global Simple/Advanced flag, which every tab interprets differently.
  4. A fixed tab bar that cannot grow.
  
  The symptoms include the thin text box, the buried toggles, the scroll jump, a 1,936-line Storage
  tab, three confirm idioms and three text-input implementations.
- **Recommended order:**
  1. Stage 0 patches (now).
  2. Kit v2 (no visual change).
  3. Page/state model.
  4. Information architecture (navigation, search, the density rule).
  5. Visual pass **from his images**.
  6. Advanced SmartStow screens **from his images**.
  7. Polish.
- **Stages 1-2 are invisible foundations and don't need the images.** Stages 4-5 wait for them.

---

## 2. What exists today

### 2.1 The shell — `UI/Menu/UiaControlCenter.cs` (634 lines)

- **Canvas.** One ScreenSpaceOverlay canvas, sorting order 5200, `CanvasScaler` at 1920×1080,
  match 0.5 (`:322-331`).
- **Window.** A **fixed 1000×660** window (`:342`). It is a real HUD `PanelGraphic`, restyled every
  frame with the F9 glass stack (`StyleWindowPanel`, `:98-117`).
- **Title bar.** Title, the **Simple / Advanced** density buttons (`:387-389`), pause `||` and `X`
  (`:374-400`).
- **Master strip.** RADIAL MENUS / VISOR HUD (`:443-485`).
- **Tab bar.** Fixed **130 px** buttons (`:487-499`). Six tabs use 800 of the 972 px available.
  Below it sit the content area and a popup layer for dropdowns (`:358-368`).
- **Contract.** `IUiaTab.Build(RectTransform content, bool advanced)` is the entire tab contract
  (`:15-19`).
- **Rebuild model.**
  - `Refresh()` destroys and rebuilds the active tab, keeping only the scroll fraction (`:511-558`).
  - `Restyle()` destroys and rebuilds the **whole window, including new tab instances**, up to about
    7 times a second while an F9 palette drag runs (`:194-196, 226-278`).
  - Tabs check `IsRestyling` to skip disk and inventory work (`:222`).
- **Density flag.** `_advanced` is a static and is **never persisted** (`:48, 518-525, 628`). Every
  launch opens in Simple.
- **Esc.** Esc closes the window (`:204-212`), even while a text field has focus.

### 2.2 The tabs

| Tab (title) | File (lines) | Simple shows | Advanced adds | Notes |
|---|---|---|---|---|
| HUD Themes | `Tabs/ProfilesTab.cs` (438) | 4 featured cards with preview PNG; "All HUD Themes" dropdown; per-theme font | Author row (New blank, Duplicate, F9, folder, Export to mod); Manage (delete, restore shipped) | No text input by design: rename lives in F9 (`:126-130`) |
| Radial | `Tabs/RadialTab.cs` (118) | Behaviour; Size & readout; Radial feel; Toolbelt wheel | Appearance; Hub text sizes; Bag wheels; "Open the radial editor" | Wheel colours live **only** in the ImGui radial editor (`Windows/SettingsWindow.cs`) |
| HUD | `Tabs/HudTab.cs` (179, patched) | Look (→ F9); Show: Diegetic tiers, with **Power & glitch** under it while Diegetics is on (`:46-52`); Hide vanilla panels | Power & glitch always; F9 link; Maintenance (Repair config folders, Restore shipped themes) | D-020 moved Power & glitch here this wave |
| Storage | `Tabs/StorageTab.cs` (1,936) | Sub-tabs Bags / Bag Profiles / Stow Profiles / Settings (hand-built strip, `:165-195`) | Bags as dense rows; Settings adds 4 toggles | The largest file in `UI/Menu`, with about 20 statics of transient state (`:39-119`) |
| Controls | `Tabs/ControlsTab.cs` (91) | Hint bar, cursor latch, key rebinds, chord reference | — | |
| Guide | `Tabs/GuideTab.cs` (119) | Text, live key chips, tutorial and handbook buttons | — | The Smart Stow text changes with Simple SmartStow |

### 2.3 The kit — `UI/Menu/Kit/`

| File | Lines | Provides |
|---|---|---|
| `UiaUi.cs` | 212 | Game-object helpers; `Text`; `OutlineOf`; `VLayout` / `HLayout` (both **always** `childControlHeight = true`, `childForceExpandHeight = false`, `:85-113`); `Size` (a `LayoutElement`); `InputField` (`:129`); `ScrollView` (`:178`) |
| `UiaControls.cs` | 450 | `UiaButton`, `UiaToggle`, `UiaSlider`, `UiaDropdown`; row builders `Header`, `Note`, `ToggleRow`, `SliderRow`, `DropdownRow`, `Button`, `Switch`, `SetButtonIcon` (`:266-446`) |
| `UiaTheme.cs` | 116 | Live colour getters that follow the HUD palette; **const** metrics (`:87-96`) |
| `UiaMenuTheme.cs` | 206 | Follow/override state, `StyleHash`, and the `menu:` theme family |
| `UiaGlassSkin.cs` / `UiaScrollbar.cs` / `UiaImages.cs` | 57 / 182 / 96 | Glass border on buttons; auto scroll indicator; the rounded 9-slice sprite and PNG cache |
| `UiaItemPicker.cs` / `UiaRebindCapture.cs` | 387 / 104 | The item-picker overlay; key capture |

### 2.4 Settings live in five places

| Surface | Stack | What it owns |
|---|---|---|
| F10 Control Center | UGUI | This plan |
| F9 HUD Designer | ImGui (`Windows/HudEditorWindow.cs` 2,595 lines, `HudEditorMode.cs` 2,083) | Every element, palette and effect |
| Radial editor | ImGui (`Windows/SettingsWindow.cs` 417) | Wheel colours, wedge glass, radial palette |
| Legacy bag-profile editor | ImGui (`Windows/ProfileEditorWindow.cs` 208) | The 0.9.7.0 plan said to retire it; it is still in the tree |
| Universal Inventory profile mode | UGUI (`UI/Grid/GridProfilePopup.cs`, `GridCapturePanel.cs`) | Assign and capture, in-grid |

There is also StationeersLaunchPad's generic `.cfg` panel for every bound key.

**The D-020 confusion is the symptom.** FlorpyDorp told a tester the flicker toggle was under
"F9 → effects → diegetics"; it was actually F10 → HUD → Advanced.

---

## 3. Pain points

| # | Pain point | Evidence | Status |
|---|---|---|---|
| 1 | **D-009:** the rename box renders as a thin line, and typed text is invisible | `UiaUi.InputField` had no intrinsic height. A spriteless `Image` reports 0, and `TMP_InputField` is not an `ILayoutElement`, so every caller's `HLayout` collapsed it. The caret colour defaulted to near-black. | **Landing now:** `Size(go, h: RowH, flexH: 1f)` plus themed caret/selection in `UiaUi.InputField` |
| 2 | **D-020:** Power & glitch buried under Advanced | An Advanced-only header in `HudTab` | **Landing now:** moved under Diegetic tiers; visible in Simple while Diegetics is on; `LowPowerThreshold` default is 10 % (`UI/Hud/HudConfig.cs:441`) |
| 3 | Simple ⇄ Advanced scroll jump | `SetAdvanced` replays the scroll *fraction* onto a taller page (`UiaControlCenter.cs:518-525`). Unity reports 0 ("bottom") for content that doesn't scroll. | **Landing now:** `TryCaptureScroll` treats non-scrolling content as top (`:562-586`) |
| 4 | Esc while typing closes the whole window and loses the draft | `Update` closes on Esc without checking focus (`:204-212`) | Open |
| 5 | **Three text-input implementations** | `UiaUi.InputField`; `UI/Menu/Tutorial/TutorialTextField.cs` (which exists *because* the kit's field lacked multi-line, seed text and caret theming, and had to be built inactive first: `:9-18, 33-38`); `UI/Grid/GridCapturePanel.cs:373-404` (its own, HudText-based, 40-char limit) | Open |
| 6 | Text drafts live in ad-hoc statics | `StorageTab._renameFields`, `_renameField`, `_setRenameField`, `_importField` (`:66-77, 97-100`) | Open |
| 7 | Three confirm idioms | StorageTab's two-row "Yes, delete it" (`:811-822`); ProfilesTab's confirm flags (`:166-182`); HudTab's 5-second toast arm (`RestoreShippedThemes`, `:139+`) | Open |
| 8 | Dropdowns used as buttons | Sentinel index 0 such as "+ Add category..." or "Copy to..." (`StorageTab.cs:1578-1588, 861-905`) | Open |
| 9 | Dropdown popups: no screen clamp, 10-row cap, no search | `UiaDropdown.OpenPopup` (`UiaControls.cs:193-248`); e.g. the 45-entry slot-class list | Open (05 report, P2) |
| 10 | Master switches can go stale | Painted only at build and on click (`MasterSwitch`, `UiaControlCenter.cs:455-485`) | Open (05 report, P2) |
| 11 | A restyle loses tab-instance state | `StorageTab._selected` is an instance field (`:49`), and Restyle recreates the instances | Open (05 report, P2) |
| 12 | Popup chrome doesn't match the window | The picker and rebind prompt are a flat Image + Outline (`UiaItemPicker.cs:86`, `UiaRebindCapture.cs:85`); the window is PanelGraphic glass | Open (05 report, P2) |
| 13 | Help text as ever-longer Notes; SubNotes are fixed 16 px, single line, no wrap | `StorageTab.SubNote` (`:1379-1387`). The longest sentences likely clip at the right edge *(verify in game)*. | Open |
| 14 | Every gesture rebuilds the whole tab, and long lists aren't virtualized | `Refresh` (`UiaControlCenter.cs:511-514`). The shipped "By Printer" set carries 559 item rules (`Features/StowShareCodec.cs:67-68`), and the rule editor draws one row per rule. | Open |
| 15 | The density flag isn't persisted, and each tab defines "Advanced" its own way | `_advanced` is a static | Open |
| 16 | F10's Simple/Advanced collides with the new Smart Stow Simple/Advanced modes | Title bar `:387-389` vs `SmartStow-Simple-Refactor-Plan.md` §9 | **New** |
| 17 | Feedback toasts are centre-screen ImGui, not next to the control that caused them | `Overlay.Toast.Show` from `HudTab`, `ProfilesTab.cs:260` | Open |

---

## 4. What structurally blocks nicer layouts

- **B1 — The page contract is "rebuild everything".**
  - `Build(content, advanced)` is the whole interface. Every change calls `Refresh()`, which
    destroys and rebuilds the page. `Restyle()` rebuilds the window **and new tab instances**.
  - *So:* state is pushed into statics (Storage has ~20); drafts need their own caches; anything
    expensive needs its own restyle guard (`StorageTab.cs:78-100, 632-641`).
  - A master-detail editor with shelves and drag multiplies all three problems.
- **B2 — Row builders are fire-and-forget.**
  - `ToggleRow` returns the switch, `DropdownRow` the dropdown, `Note` the text
    (`UiaControls.cs:266-392`). There is **no handle to the row**, so it can't be hidden, disabled,
    given help, or updated in place.
  - "Show this only while Diegetics is on" therefore needs a full `Refresh`, which is exactly what
    the D-020 patch had to do.
- **B3 — Layout is `HLayout`/`VLayout` with `childControlHeight = true` and
  `childForceExpandHeight = false`.**
  - Widgets have **no intrinsic size** (`UiaUi.cs:85-113`). Every widget must bring its own
    `LayoutElement` or it collapses; D-009 was that failure.
  - Beyond that, the only structure is ad-hoc `GridLayoutGroup` card grids with hard-coded cells
    (`StorageTab.cs:309`, `ProfilesTab.cs:55`).
- **B4 — There are no composite components.**
  - Missing: section/card, segmented control, menu button, confirm button, list, master-detail,
    modal, popover, search box, icon.
  - Each tab hand-rolls what it needs. The Storage sub-tab strip, for example, is 140 px buttons
    (`StorageTab.cs:165-195`).
- **B5 — Colours are frozen into widgets at build time.**
  - `UiaButton` captures its normal, hover and selected colours at `Init` (`UiaControls.cs:29-34`),
    and the kit documents that a theme change needs a full rebuild (`UiaTheme.cs:16-18`).
  - That is fine today, but it makes Restyle the main way state gets lost (B1).
- **B6 — The geometry is fixed.**
  - The window is 1000×660 (`UiaControlCenter.cs:342`), tab buttons are 130 px (`:496`), metrics
    are consts (`UiaTheme.cs:87-96`), and card cells are fixed.
  - The bar fits at most 7 tabs, and there is no second navigation level except hand-built strips.
- **B7 — Iconography.**
  - TMP renders ASCII only (CLAUDE.md), so icons must be **drawn** (the HUD's
    `PanelGraphic` / `TriangleGraphic`) or taken from **vanilla sprites** (`Core/VanillaIcons.cs`).
  - The kit has only `SetButtonIcon` (`UiaControls.cs:430-446`). Any concept image full of icons
    needs this plumbing first.
- **B8 — Input ownership.**
  - The window owns Esc; text fields don't (pain 4).
  - There is no focus model and no keyboard navigation.
- **B9 — Settings are split between UGUI and ImGui.**
  - F9 and the radial editor draw their own ImGui widgets (`Windows/HudPropDrawer.cs`).
  - A unified look or a search across all settings therefore has to either port them or link to
    them (§5, principle 2).

---

## 5. Redesign principles

1. **Simple = first-hour and gameplay-relevant. Advanced = tuning, authoring, maintenance.** A
   setting that changes what the player *experiences in play* is never Advanced-only: flicker and
   dropouts, whether a wheel closes, the Smart Stow mode. D-020 is the cautionary tale.
2. **One home per setting. Link, don't duplicate.** F10 links into F9 and the radial editor for
   look authoring, as `HudTab` already does. No knob is editable in two places.
3. **Discoverable by search, not by memory.** Search should also know the F9-only and
   radial-editor-only settings, and answer "it's over there → [Open]".
4. **One of each control, in the kit.** One text input, one confirm, one segmented control, one menu
   button. Tabs never re-implement them.
5. **State survives rebuilds.** Drafts, selections, armed confirms and scroll position live in a
   page state object that neither Refresh nor Restyle destroys.
6. **Help on demand.** Short labels plus an (i) popover. Notes are kept only for warnings.
7. **Keep theme-following.** Everything reads `UiaTheme`. Any new menu **look** knob joins
   `UiaMenuTheme`'s `menu:` family, so it travels with the HUD theme (CLAUDE.md rule 8, points 1-2:
   the menu is shared across tiers and is documented as such). **Behaviour** knobs never travel.
8. **ASCII text, drawn icons.** No glyph iconography in any displayed string.
9. **Hot-reload and MP safety unchanged.**
   - Every new static resets in `UiaControlCenter.Shutdown`.
   - F10 still writes config only. The one game-state write remains the labeller rename
     (`ItemActions.RenameThing`).
10. **Performance budget.**
    - No disk IO and no inventory scans in a restyle build.
    - Virtualize any list over ~50 rows.

---

## 6. Information architecture

### 6.1 Options — YOUR CALL

| Option | Shape | For | Against |
|---|---|---|---|
| A. Keep the top tab bar, regroup | 6-7 tabs, sections inside | Smallest change (S-M) | It cannot grow past 7 tabs, and Storage keeps its nested strip. The Advanced SmartStow screens have nowhere to go. |
| **B. Left navigation rail + search** *(recommended target)* | Grouped pages, with sub-pages | Unlimited pages and sub-pages (Storage's four sub-tabs become rail children). Search results jump to the page and highlight the row. The density question becomes per-section "More options". | A new shell (M-L). The rail takes ~200 px, so the window likely grows (Q2). |
| C. Card dashboard → pages | A home screen of big cards | A great first-run moment; it can showcase themes | One extra click to reach everything, and it still needs B's page model underneath |

**Recommendation:** B as the structural target, with C's cards as its landing page if the images
point that way. The *visual* treatment comes from FlorpyDorp's images; this plan only commits to the
structure.

```
+----------------------------------------------------------------------------------+
| UI ASCENDED               [ search settings...            ]            [||] [X]  |
| [ RADIAL MENUS  on ]  [ VISOR HUD  on ]                                          |
+-------------------+--------------------------------------------------------------+
| LOOK              |  HUD > Show                                                  |
|   HUD Themes      |  +--------------------------------------------------------+  |
|   HUD             |  | Diegetic tiers                           [on]   (i)    |  |
|   Radial look     |  | POWER & GLITCH                                         |  |
| PLAY              |  |   Low-power dropouts                     [on]          |  |
|   Radial wheels   |  |   Threshold                    [-----o----]  10 %      |  |
|   Smart Stow      |  |   > More options                                       |  |
|   Univ. Inventory |  +--------------------------------------------------------+  |
|   Controls        |                                                              |
| MAKE              |                                                              |
|   HUD Designer  > |                                                              |
|   Radial editor > |                                                              |
| HELP              |                                                              |
| MAINTENANCE       |                                                              |
+-------------------+--------------------------------------------------------------+
```

A draft page map, as a sketch rather than a commitment:

- **LOOK:** HUD Themes, HUD, Radial look.
- **PLAY:** Radial wheels, Smart Stow (with the mode block), Universal Inventory, Controls.
- **MAKE:** HUD Designer ↗, Radial editor ↗, theme authoring (Advanced).
- **HELP:** Guide, Tutorial, Handbook.
- **MAINTENANCE** (Advanced): repair folders, restore shipped themes, restore shipped Stow Profiles,
  forget Smart Stow homes.

### 6.2 The density toggle — YOUR CALL

| Option | What it means |
|---|---|
| a. Keep a global Simple/Advanced toggle | **Persist it**, and enforce principle 1 as a written rule. |
| **b. Per-section "More options"** *(recommended)* | Each section discloses its extras on its own, remembered per section. A global "Show everything" switch covers power users. |
| c. Drop the distinction | Everything is always shown. |

Option b also frees the words **Simple / Advanced** for Smart Stow's modes. The alternative is to
rename the F10 toggle to something like "Essentials / Everything" (pain 16, Q4).

---

## 7. Kit v2: the components the redesign needs

| Component | Replaces | Must do |
|---|---|---|
| **TextInput / TextInputRow** | `UiaUi.InputField`, `TutorialTextField`, `GridCapturePanel`'s field | See the requirement list below the table. |
| **RowRef** (returned by every row builder) | Fire-and-forget returns | `SetVisible`, `SetEnabled(reason)`, `SetValue`, `SetHelp`. Partial updates, no `Refresh`. |
| **Section / Card** | `Header` + loose rows | A titled group with an optional "More options" disclosure (remembered) and optional (i) help. |
| **Segmented control** | The density buttons, hand-built sub-tab strips | N options, one selected. Used for the Smart Stow mode. |
| **Menu button** | Sentinel dropdowns (pain 8) | "Add rule > Item / UIA class / Category / Slot class": the 0.9.7.0 plan's "+ Catch..." entry point. |
| **ConfirmButton** | The three idioms (pain 7) | Inline "Sure? [Yes] [Cancel]"; disarms on page leave or timeout; danger styling. |
| **Dropdown v2** | `UiaDropdown` | Clamps or flips at the screen edge; scrolls beyond 10 rows; optional search; keyboard. |
| **VirtualList** | One row per item, rebuilt each time | Recycled rows for hundreds of rules or items. |
| **MasterDetail** | Ad-hoc layouts | List on the left, detail pane on the right. For the Bag Profile editor and the Stow Profile manager (0.9.7.0 plan options 2A/3A). |
| **Icon** | — | Drawn primitives or vanilla sprites, with an ASCII fallback (B7). |
| **Popover / tooltip** | Long Notes | (i) help and hover tips. |
| **Inline status line** | The centre-screen ImGui toast | Per-page feedback, next to the control. |
| **PageState** | Tab statics | Survives Refresh and Restyle; one `Reset` in `Shutdown`. |

**TextInput requirements.** It must:

- carry an intrinsic `LayoutElement` (min and preferred height = `RowH`, flexible width);
- build inactive → wire → activate (`TutorialTextField.cs:33-38`);
- use the rounded sprite plus `UiaGlassSkin`;
- take its caret and selection colours from the theme;
- set `richText = false`;
- support `characterLimit` (40 for profile names);
- filter to **ASCII on commit** (the TMP rule);
- commit on **Enter**, and on **Esc cancel the edit and consume the key**, so the window doesn't
  close (pain 4);
- support single- and multi-line;
- offer an optional inline validation line;
- keep a **kit-owned draft cache keyed by a stable id** (pain 6).

---

## 8. Staged refactor

| Stage | Contents | Files | Size | Exit criteria |
|---|---|---|---|---|
| **0: landing now** | D-009 text box, D-020 Power & glitch, scroll-to-top fix (§3, rows 1-3) | `Kit/UiaUi.cs`, `Tabs/HudTab.cs`, `UiaControlCenter.cs` (the F10-patches workstream) | — | Covered by the wave's Changes Report |
| **1: Kit v2 foundation, no visual change** | TextInput, then migrate every caller: the 6 `StorageTab` fields, the picker search, `TutorialTextField`, `GridCapturePanel`. Also ConfirmButton, MenuButton, Dropdown v2, RowRef, Esc ownership. | `UI/Menu/Kit/*` (new `UiaInputs.cs`, `UiaComposite.cs`), call sites | M | Screens look the same; pains 4-10 closed |
| **2: Page model** | An `IUiaPage` with a persistent `PageState`; Restyle re-skins without losing state; split `StorageTab` into `Tabs/Storage/*Page.cs` (the 0.9.7.0 split); persist the last page and the density setting | `UiaControlCenter.cs`, `Tabs/*` | M | Pain 11 closed; `StorageTab` under 400 lines |
| **3: Information architecture** | Navigation (Q1); a settings search index, covering F10 plus a static list of the F9 / radial-editor keys with jump links (Q5); the density rule (Q3); where the Smart Stow mode lives | Shell + pages | M-L | Every setting reachable from search; no gameplay knob is Advanced-only |
| **4: Visual pass** ← images | Section cards, icons, spacing, window size (Q2), unified popup chrome (pain 12) | Kit + shell | L | Matches the approved images |
| **5: Advanced SmartStow screens** ← images | Master-detail Bag Profile editor; Stow Profile cards plus shelf; bag cards; drag between sets | `Tabs/Storage/*`, Kit | L | Matches the approved images; every 0.9.7.0 capability still reachable |
| **6: Polish** | VirtualList everywhere, window drag/resize, keyboard navigation, retire `Windows/ProfileEditorWindow.cs`, decide the radial editor's future (Q6) | various | M | — |

- **Stage 5's drag** reuses the mod's existing UGUI drag work: `UI/Grid/BagGridCell.cs`,
  `UI/Grid/GridTab.cs`, `UI/Grid/PinnedInventoryWindow.cs`, `Core/DragGhostLayer.cs`.
- **Play-testing:**
  - Stage 1: every text field shows its text and caret while typing; Esc cancels the edit without
    closing F10; every destructive button still needs two clicks; dropdowns near the screen bottom
    open upward.
  - Stage 2: a theme drag in F9 while an F10 editor has a selection and a half-typed name → both
    survive.
  - Stage 3: search for "flicker", "home", "wedge colour" → each lands on the right control, or
    links to F9 / the radial editor.

---

## 9. PLACEHOLDER — FlorpyDorp's UI concept images

> **Waiting on FlorpyDorp.** Suggested drop folder: `Documentation/0.9.8.0/concepts/`. Add one row
> per image:
>
> | # | File | Screen | Must-keep elements | Kit v2 components it needs (§7) | Conflicts with constraints (ASCII text, window size, theme-follow, MP-safe writes) | Decision |
> |---|---|---|---|---|---|---|
> | 1 | `concepts/smartstow-organizer-1.png` | Storage → Organizer (first draft) | — | — | Profile-level priority; tool belts with profiles; no UIA class rules; no exclusion; no Simple/Complex mode | **Superseded** by #3 |
> | 2 | `concepts/smartstow-organizer-2.png` | Storage → Organizer (revision 1) | — | — | as #3 | **Superseded** by #3 (#3 changes the tab name and the mode labels) |
> | 3 | `concepts/smartstow-organizer-3.png` | **SmartStow** tab → Organizer (Complex mode) | See the list below the table. | Segmented control; Menu button / popover; Dropdown v2; ConfirmButton (deletes inside the kebab menus); TextInput (rename bag and layout, paste a code); Icon; Section/Card; MasterDetail; VirtualList (rule groups); inline status line | See the list below the table. | **CHOSEN** by FlorpyDorp, 2026-09-25, as the design of record for the SmartStow Organizer. Its conflicts are fixed at build time, not by regenerating the image. Review: `Advanced-SmartStow-UI-Mockup-Review.md` |
>
> **#3 must-keep elements:**
> - the main tab named **SmartStow**;
> - a **SMART STOW MODE** bar with a **Simple / Complex** segmented switch and both one-line
>   explanations;
> - sub-tabs **Organizer / Routing / Universal Inventory**;
> - three columns: Storage Layouts | Bags | Bag Profiles;
> - **layout cards:** icon, name, SHIPPED tag, profile count, ACTIVE or Use, kebab menu;
> - **bag cards:**
>   - thumbnail, name with its #n ordinal, slot count;
>   - profile dropdown, Capture, Edit;
>   - a kebab menu with Never stow / Rename bag / Clear profile;
>   - the NO STOW warning state;
> - **profile rows:** coloured badge, name, rule count, kebab menu;
> - the **EDITING** section: four collapsible rule groups in precedence order, each rule with its
>   own priority and X, plus "+ Add rule...";
> - the share bar.
>
> **#3 conflicts, fixed at build time:**
> - **Window size.** At 1000×660 the image reads at about 10-11 px, against the kit's 15 / 13 px.
>   At the kit's sizes it needs about 1450×950 (Q2).
> - **Editor height.** The rule-editor band is about 100 px tall, but shipped profiles carry 100+
>   item rules. It needs full height or a scrolling, virtualized list.
> - **Import.** "Import Layout from File" implies a file browser that doesn't exist, and there is no
>   way to import a pasted code.
> - **Wrong copy.** The share-bar text says a file gives "full fidelity" (it also has the typo
>   "fidielity"), but the code and the file carry the same content.
> - **Duplicate "Simple".** The mode switch's "Simple" repeats the title bar's Simple/Advanced
>   density button (Q4).
> - **Missing on/off switch.** The Smart Stow on/off switch is not in the mode bar.
> - **Icons.** The layout icons (document, briefcase, rocket) need Icon support, as drawn
>   primitives or vanilla sprites.
>
> ASCII text is fine throughout. Multiplayer safety is fine too: the only game-state write is the
> labeller rename.
>
> **What #3 implies for the questions below.** Answers marked "confirm" are implied by the image,
> not yet stated by FlorpyDorp.
> - **Navigation:** keeps the top tab bar, i.e. option A, with Storage renamed SmartStow
>   *(confirm Q1)*.
> - **Window size:** needs to grow *(confirm Q2)*.
> - **Density:** keeps the global title-bar toggle *(Q3 open)*.
> - **Icon style:** drawn line icons.
> - **How Complex is reached from Simple:** the mode switch at the top of the SmartStow tab.
> - **Text input and confirm:** not shown.
>
> **Questions every image set should settle:**
> - navigation shape (Q1);
> - window size (Q2);
> - density model (Q3);
> - icon style (drawn primitives vs vanilla sprites);
> - how the Advanced SmartStow screens are reached from Simple;
> - what a text input and a confirm look like.
>
> **Prior art already in the repo:**
> - `Documentation/UI Concept Art.png` — the HUD/radial visual language: thin cyan line work,
>   icon-over-label tiles, glass panels;
> - the 0.9.7.0 wireframes in `Documentation/SmartStow-Profiles-Redesign-Plan.md` §6-§8.

---

## 10. Absorbing Simple and Advanced SmartStow

- **Now, before the images:**
  - The Smart Stow **mode block** and the **Simple page** from `SmartStow-Simple-Refactor-Plan.md`
    §10.1 go on today's Storage tab, using today's kit.
  - Under option B they become the header of PLAY → Smart Stow.
  - Its two destructive buttons are the first users of Stage 1's ConfirmButton.
- **What the Advanced screens (Stage 5) will need from the kit:**
  - MasterDetail;
  - VirtualList (for sets with up to 559 rules);
  - a card grid with live per-bag thumbnails (already cached by ReferenceId in `StorageTab.Thumb`,
    `:530-539`);
  - shelf-to-card drag-and-drop;
  - the item picker as the "catch" source (`UiaItemPicker`);
  - ConfirmButton and TextInput.
- **The performance constraint carries over:** thumbnails and profile listings stay cached across
  restyles (`StorageTab.cs:78-100, 632-641`).

---

## 11. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Big-bang rewrite stalls, leaving menus half-migrated | Medium | Stages 1-2 change nothing visible and each is shippable; migrate page by page behind the same `IUiaTab` contract until Stage 3. |
| State loss or leaks across hot reload | Medium | PageState + a single `Shutdown` reset (house rule); test double-F6 at every stage. |
| Restyle cost grows with richer screens | Medium | Keep the ~7 Hz throttle; cache listings and thumbnails; consider in-place re-skin once RowRefs exist. |
| Concept images assume glyph icons or free-form text | Medium | Kit Icon component (B7); record any conflicts in the §9 table before building. |
| Window growth breaks 1080p / small screens | Low-Med | Keep the 1920×1080 reference scaler; lay out against a minimum; decide in Q2. |
| Concurrent edits to `UI/Menu/**` (this wave's patches) | Medium | Start Stage 1 after the wave merges; stage only your own files. |
| Search index drifts from the real settings | Low | Build the index from RowRef registrations, not a hand list. The F9 / radial-editor keys are the one hand list; flag it like `ConfigTreeRepair.Folders()`. |

---

## 12. Open questions for FlorpyDorp

1. **IA:** left rail + search (recommended), keep the top tabs, or a card dashboard?
2. **Window size:** keep 1000×660, grow (e.g. 1280×760), or make it resizable?
3. **Density:** per-section "More options" (recommended), a persisted global toggle, or none?
4. **Naming:** F10's Simple/Advanced vs Smart Stow's Simple/Advanced — which one gets renamed?
5. **Settings search**, including jump links into F9 and the radial editor: yes?
6. **Radial editor:** port the wheel colours into F10 eventually, or keep it as a linked ImGui
   editor?
7. **Help:** (i) popovers instead of long Notes, OK?
8. **Order:** may Stages 1-2 (the invisible foundation) start before your images arrive?
9. Should F10 **remember the last page and the density setting** across launches?
