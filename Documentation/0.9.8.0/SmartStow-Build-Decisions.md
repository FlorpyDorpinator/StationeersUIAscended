# SmartStow redesign: decisions needed to build it

*2026-09-25. Direction: `concepts/smartstow-organizer-3.png` (chosen). This sheet turns the three
plans into short questions:*
- *`F10-Settings-Refactor-Plan.md` ("F10 plan");*
- *`SmartStow-Simple-Refactor-Plan.md` ("Simple plan");*
- *`Documentation/SmartStow-Profiles-Redesign-Plan.md` (0.9.7.0, "Aug plan"), which Complex mode
  continues.*

*Every question has a recommended answer. To answer, reply with the numbers you **disagree** with,
e.g. "all yes except 5 and 14". Each answer then gets logged in the Answer column.*

---

## ANSWERS — FlorpyDorp, 2026-09-25 (binding)

**Approved: everything as recommended, Part 2 as a block, with three modifications:**

- **#3 (rule editor size), modified:** the editor expands only up to a cap, then **scrolls inside
  itself**. It will never show everything at once; it must stay clean, not messy.
- **#7 (inactive layouts), REVERSED:** every Storage Layout is **editable even when it is not
  active**. Clicking a layout browses/edits it; activation happens ONLY through its "Use" button.
  (So no read-only state; edits to a non-active layout write to its document on disk.)
- **#8 (badge colours), modified:** exact colours don't matter — each profile just gets its own
  **distinct auto-assigned colour**. No colour picker.

**Part 3 is promoted into scope — build ALL of it:** settings search (with F9/radial jump links),
the radial editor's wheel colours ported into F10, drag-and-drop of profiles between layouts,
the Complex-mode "home first" toggle (P5a), the home ghost glow (P5b), the per-save stores moved
to the world id (P5c), and (i) popovers.

**New directives from the same review:**
- **Visual language:** he likes the manila-folder style — keep it, with **sharper corners** and a
  more futuristic look, with shading. All of it must be **dynamic with the UI themes** (colours
  always flow from the active theme, never hard-coded).
- **Rename:** player-facing **"HUD Themes" becomes "UI Themes"** (labels only; folders, XML and
  code names unchanged).
- Deliverables include a **simple play-test MD** (main features, under ~5 minutes) plus a full
  test MD.

---

## Already decided (no answer needed)

- **The look** is mockup 3.
- **Tab and mode names:** the main tab is **SmartStow**; the modes are **Simple / Complex**; the
  sub-tabs are **Organizer / Routing / Universal Inventory**.
- **Profile rules:**
  - tool belts and suits can't take a profile;
  - there are four rule kinds, and item beats UIA class, which beats slot class, which beats
    category;
  - one layout is active at a time;
  - bags are mapped to profiles by hand.
- **The Bags list** shows only containers on you. Each bag has "Never stow into this", and
  assigning a profile also labels the bag (Aug plan answers Q3-Q5).
- **Sharing** works by pasting a code **or** by dropping the file in the folder (Aug plan Q7).
- **Players never see "the Grid".** It's always "Universal Inventory".

---

## Part 1: Your calls (your preference matters)

### The Complex screen

| # | Question | Recommended | Why | Answer |
|---|---|---|---|---|
| 1 | Call the sets **"Storage Layouts"** on screen instead of "Stow Profiles"? Code and files keep the old name. | **Yes** | It's what mockup 3 says, and it ends the "Bag Profile vs Stow Profile" confusion. This reverses your 2026-08-01 naming, so it needs a yes. | |
| 2 | Rule editor: keep mockup 3's **four group headers**, but with **one "+ Add rule" button** that opens one search window with tabs for Items / UIA classes / Slot classes / Categories? | **Yes** | It keeps the look you like and adds the Aug plan's single-button idea. It removes three awkward "+ Add..." dropdowns and gives the 45-entry slot-class list a search box. | |
| 3 | Rule editor: while a group is open, the editor **grows taller** (the columns above shrink), and long lists scroll with a search box? | **Yes** | The biggest shipped By Printer bags hold over 100 rules. The drawn ~100 px band fits two. | |
| 4 | Share bar: **one "Export Layout"** button (copies the code and saves a file) and **one "Import"** (a paste box, or the clipboard if it's empty)? Dropping a file in the folder keeps working. | **Yes** | That's what the code already does. "Import from File" would need a file browser the game doesn't have. | |
| 5 | Put the **Smart Stow on/off switch** in the mode bar? | **Yes** | You feel that switch in play, so it shouldn't be buried in Routing (F10 plan principle 1). | |
| 6 | Keep **Routing** as a small Complex-only tab, **plus** a "Test an item" button in the Organizer? | **Yes** | The switches are expert tuning, but "why did it go there?" gets asked while arranging bags. *Alternative:* no Routing tab, with the switches folded into a collapsed section. | |
| 7 | Clicking a layout that **isn't active** shows its profiles **read-only**, with "Copy to active". You switch to it before you can edit it. | **Yes** | The model only edits the active layout, and this avoids editing something that isn't in use by accident. | |
| 8 | Badge colours **picked automatically** from the badge letters (no colour picker yet)? | **Yes** | You get mockup 3's colours with no new data or share-code change. A picker can come later. | |
| 9 | Layout icons: **fixed icons** for the four shipped layouts, **one default icon** for yours (no icon picker yet)? | **Yes** | This matches the image with no new data. | |
| 10 | **Capture** in the Organizer opens the **same capture window** the inventory uses, on top of F10? | **Yes** | One capture feature, not two. | |
| 11 | **Retire** the single-profile file share, "Create recommended profiles", and the old ImGui profile editor with its out-of-date SmartStow section? The buttons go; the code stays. | **Yes** | Layout sharing and the shipped layouts replace them, and the ImGui section contradicts the new screen. | |
| 12 | The in-inventory profile mode (chips, CAPTURE, drag-an-item-onto-a-tab) stays as it is in Complex, and is **hidden in Simple**? | **Yes** | That's the Simple plan §9.2. The data stays on disk either way. | |

### The F10 window

| # | Question | Recommended | Why | Answer |
|---|---|---|---|---|
| 13 | Keep the **top tab bar**, as in the mockup? | **Yes** | The mockup is built around it and six tabs fit. The F10 plan preferred a left sidebar plus search, for growth; that can come later if the tabs run out. Choosing tabs also removes most of the F10 plan's Stage 3. | |
| 14 | **Make F10 bigger**, about 1450×950 on a 1080p screen (today it's 1000×660)? | **Yes** | At today's size, mockup 3's text would be about 10 px. *Alternative:* a resizable window, later. | |
| 15 | Replace the title bar's **Simple / Advanced** button with small "More options" links inside each section? | **Yes** | It frees the word "Simple" for Smart Stow, so the screen doesn't show two "Simple" buttons (F10 plan's recommendation). | |
| 16 | Should F10 **remember** the last page you were on? | **Yes** | Small, and nobody likes re-navigating. | |

### Simple mode (the other side of the switch)

| # | Question | Recommended | Why | Answer |
|---|---|---|---|---|
| 17 | New players start in **Simple**. Existing players who have ever assigned a bag profile stay in **Complex**; everyone else moves to Simple. Both see a one-time message. | **Yes** | It protects people who built profiles and gives everyone else the better default (Simple plan S-8). | |
| 18 | An item's home is the **exact slot** it sat in. If that slot is taken, the item goes anywhere in the same bag. | **Yes** | That's your "that spot" spec. *Alternative:* just the bag (S-1). | |
| 19 | **Only you move a home.** Dragging an item, or choosing its slot, sets the home. G, sorting and swaps only give an item its *first* home. Taking an item out never changes its home. | **Yes** | Otherwise G and sorting would quietly rewrite homes (S-3). | |
| 20 | A **brand-new item** (no home yet) uses today's smart routing, without profiles. Wherever it lands becomes its home. | **Yes** | No setup needed, and the second G press is predictable. *Alternative:* it stays in your hand with a message (S-5). | |
| 21 | Taking something into a **full hand** sends what you were holding back to **its** home? | **Yes** | Without this, every swap scrambles your bags a little more (S-6). | |
| 22 | Homes only live in your **worn slots and real bags**: never inside tools, food packaging or body bags. | **Yes** | Otherwise a battery goes back into the drill (S-9). | |
| 23 | **"Never stow into this"** still works in Simple mode? | **Yes** | An exclusion you can't trust is worse than none (S-10). | |
| 24 | World lockers are **out of scope for now**: G only returns items into your own inventory. | **Yes** | It keeps version 1 simple. The quick-store feature (D-018) covers lockers. | |
| 25 | Show **short tips** in Simple ("no home yet - put in X", "home full - put in X")? On by default. | **Yes** | They teach how homes work, and they can be switched off. | |
| 26 | Build the **Simple page** from the plan's sketch, in mockup 3's style, **without waiting** for another image? | **Yes** | It's one small page: a count, what's in your hand, one switch and two buttons. | |

---

## Part 2: Technical approach (approve as a block: "Part 2 OK")

| # | Recommendation | Why | Source |
|---|---|---|---|
| 27 | **Build the reusable menu parts first:** text box, pop-up (kebab) menu, confirm button, segmented switch, better dropdown, scrolling long list, icons. No visible change at first. | Mockup 3 needs about ten new parts. Built once in the shared kit, they make every later F10 screen cheaper. This step also fixes Esc closing F10 while you type. | F10 plan §7, Stage 1 |
| 28 | **Make pages keep their state** (selection, open menus, half-typed names) across refreshes and theme changes. **Split the 1,936-line Storage tab** into one file per page. | Today every click rebuilds the page from scratch. The Organizer has too much going on to survive that. | F10 plan Stage 2 |
| 29 | **Icons are drawn line shapes** (like the mockup) plus the game's own bag pictures. No new image files to ship. | The game font can't show symbols. Drawn icons follow the theme colours and add nothing to package. | F10 plan B7 |
| 30 | A **new setting `Mode = Simple / Complex`**, default Simple, plus one config-migration step for existing players (question 17). | That's the house rule for config changes. It removes and renames no keys. | Simple plan §9 |
| 31 | Simple-mode **homes are saved per world**, using the world's hidden ID, not the save name. | The save name is blank for multiplayer clients, so every server would share one file. | Simple plan S-2 |
| 32 | **No data-model change for Complex mode.** The new screen is a new face on today's layouts, profiles and assignments. | Every control in mockup 3 maps to a call that already exists (review doc, Part B2). | Review doc |
| 33 | **Start after the current fix wave merges.** | The wave is editing the same menu and inventory files right now. | Both plans |

**Recommended build order:**
1. The menu parts (27).
2. Pages keep their state, and the Storage split (28).
3. The Simple-mode engine, underneath: saving homes, then using them on G, then the swap-take.
4. The mode bar, and the Organizer from mockup 3.
5. The Simple page, and the switch-over for existing players (17).
6. Polish.

Steps 3 and 4 can run in parallel.

---

## Part 3: Can wait (not needed to build this)

- Settings search across F10, F9 and the radial editor (F10 plan Q5).
- Moving the radial editor's wheel colours into F10 (F10 plan Q6).
- Drag-and-drop of profiles between layouts (Aug plan P5).
- An optional "home first" step for Complex mode (Simple plan P5a).
- A glowing "home slot" hint in the inventory (Simple plan P5b).
- **Soon, but separate:** fixing the six older per-save files (bag assignments, belt bindings, and
  others) that mix up multiplayer servers (Simple plan P5c).
- "(i)" help pop-ups instead of long notes (F10 plan Q7). These come for free with part 27.
