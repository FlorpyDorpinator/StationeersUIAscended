# Tutorial copy: what changed from Part C (0.9.8.0)

**For:** FlorpyDorp, to review in one sitting. **Written:** 2026-09-26.

Part C of `Tutorial-Plan-and-Script.md` is the script of record. This file lists every line where
the shipped copy (`Assets/Scripts/StationeersUIMod/UI/Menu/Tutorial/TutorialCopy.g.cs`) is **not**
Part C word for word, and why. Everything not listed here ships exactly as Part C wrote it.

**How to read it**

- Text is shown the way the player sees it with default keys, like Part C does: `[G]`, `[F10]`.
  The file itself stores code tokens (`{V:SmartStow}`, `{UIA_Menu}`), so a rebind always shows
  the player's real key.
- `[Shift]` in the shipped text is the token `{V:MoveAllOfType}`: vanilla's rebindable "move all
  of this type" key (default Left Shift). The keep-open Shift and the Shift + number in-window
  Shift are wired to Shift, so they stay as the plain word "Shift".
- Reason tags:
  - **VERIFY**: a `[VERIFY]` flag in Part C, settled against the current code. The citation is given.
  - **D-016**: Simple SmartStow is the default.
  - **F10**: the rebuilt F10. Tab titles are "UI Themes", "Radial", "HUD", "SmartStow", "Controls",
    "Guide" and "Suggestions/Bugs", and there is no Simple/Advanced toggle any more.
  - **WORD**: the action word now sits outside the hovered wedge, not at the top of the wheel.
  - **EQUIP**: tools on the belt wheel say EQUIP or SWAP, never TAKE.
  - **SHIFT**: the text now uses the `{V:MoveAllOfType}` token.
  - **BUDGET**: the Part C line was over its character budget.
  - **NEW**: a variant Part C did not have.

**Totals:** 19 lessons (0-18) and 82 steps, counting the 4 tablet-track steps separately as C.0
does. There are also 7 "as I go" tips and 19 chrome strings, making 290 editable strings in all.
Of those, 43 differ from Part C. Every string fits its budget with default keys, and every
string is plain ASCII.

---

## At a glance

| Key | Tag |
|---|---|
| lesson:entry\|title | NEW |
| entry.welcome\|body@mp | spelled out from Part C's own instruction |
| entry.welcome\|body@running | NEW |
| entry.invite\|says | F10 |
| core.read\|says | WORD, EQUIP, VERIFY |
| core.stow\|title | token |
| core.finish\|body | F10 |
| tip.2\|says | WORD |
| readwheel.hints\|then | F10 |
| tools.careful\|title | BUDGET |
| tools.tablet.window\|says | VERIFY (code check) |
| split.open\|says@client | NEW (MP client) |
| split.window\|says | VERIFY |
| split.window\|says@client | NEW, VERIFY |
| dragdrop.drop\|then | VERIFY |
| gear.ingrid\|title | BUDGET |
| stow.where\|then | D-016, F10 |
| stow.where\|then@complex | NEW, F10 |
| stow.profiles\|says | D-016, F10 |
| stow.profiles\|says@complex | F10 |
| settings.keepopen\|then | F10 |
| settings.keepopen\|then@inverted | NEW |
| speed.move\|then | F10 |
| window.shiftdrag\|says | SHIFT, VERIFY |
| names.how\|says | D-016, F10, VERIFY |
| names.how\|says@complex | F10, VERIFY |
| visor.lowpower\|then | F10 |
| senses.numbers\|says | VERIFY |
| menu.* (the whole tour) | F10, see its own section |
| designer.card\|body | Rangefinder line (Q8) |
| whatsnew.card\|body | WORD, SHIFT, VERIFY |
| whatsnew.card\|says@mp | F10 |
| whatsnew.word\|body | WORD, EQUIP |
| whatsnew.belts\|body | VERIFY |
| whatsnew.window\|body | SHIFT, VERIFY |
| chrome\|waiting, chrome\|lessonsoff | F10 |
| chrome\|reopen | placeholder |

---

## Lesson 0 - Entry cards

**`lesson:entry|title`** (NEW)
- Part C: none. C.0 lists the title as "(entry cards)".
- Shipped: "WELCOME"
- Why: the GUIDE lesson list and the replay heading need a name.

**`entry.welcome|body@mp`** (spelled out)
- Part C: "second paragraph's first sentence becomes 'Multiplayer can't pause - start when you're safe.'"
- Shipped: "Your inventory now runs on wheels and one big window. Your bottom bar is just your two hands - there is no hotbar.\nMultiplayer can't pause - start when you're safe. Next you'll try each move for real, about three minutes - the game runs while you practice."
- Why: this is exactly Part C's instruction, written out in full.

**`entry.welcome|body@running`** (NEW)
- Part C: none.
- Shipped: "Your inventory now runs on wheels and one big window. Your bottom bar is just your two hands - there is no hotbar.\nThe game keeps running - start when you're safe. Next you'll try each move for real, about three minutes - the game runs while you practice."
- Why: single-player with the card pause turned off. The normal body would say "The game is paused" when it is not.

**`entry.invite|says`** (F10)
- Part C: "Lessons are ready. Multiplayer can't pause, so start one when you're safe: [F10] > GUIDE."
- Shipped: "Lessons are ready. Multiplayer can't pause, so start one when you're safe: [F10] > Guide."
- Why: the tab is titled "Guide" (`GuideTab.cs:16`). The same change is made in `core.finish|body`,
  `whatsnew.card|says@mp`, `chrome|waiting` and `chrome|lessonsoff`.

## Lesson 1 - FIRST STEPS

**`core.read|says`** (WORD, EQUIP, VERIFY 1.3)
- Part C: "Point at a few wedges. The word at the top of the wheel says what a click will do: TAKE or OPEN."
- Shipped: "Point at a few wedges. The word just outside the one you point at says what a click will do: EQUIP, SWAP or OPEN."
- Why:
  - The word now rides outside the hovered wedge (`UI/UnityRadialView.cs:314-323`, your
    2026-09-26 change).
  - On the belt wheel, THE HUB says OPEN (a branch). A tool says EQUIP with an empty hand and
    SWAP with a full one: `ToolbeltRadialFeature.cs:100` ActionText "Equip" / "Swap into hand",
    then `RadialEntry.ClickVerb` -> `TakeOrSwap("Equip")` (`Overlay/RadialMenu.cs:94-95, 159-175`).
    The belt wheel never shows TAKE.
  - The hint line really is curved under the wheel (`RadialHintContext.PublishGeometry`,
    `UnityRadialView.cs:103-105`).

**`core.stow|title`** (token)
- Part C: "Put it back with G"
- Shipped: "Put it back with [G]" (stored as `{V:SmartStow}`)
- Why: the title follows a rebind. `stow.order|title` does the same in Part C already.

**`core.finish|body`** (F10)
- Part C: "... Replay any lesson from [F10] > GUIDE."
- Shipped: "... Replay any lesson from [F10] > Guide."

**`tip.2|says`** (the "as I go" tip for 1.3) (WORD)
- Part C: "Point at a wedge: the word at the top says what a click will do."
- Shipped: "Point at a wedge: the word just outside it says what a click will do."

**D-016 mapping (the words are unchanged, only the default moved).** `core.stow|says` is Part C's
`[PENDING D-016]` line: "Press [G]: the tool goes back to the slot you took it from. Watch the belt
box flash." `core.stow|says@complex` is Part C's original Says: "...flies back to its own belt
slot...". Players in Complex SmartStow get the @complex line.

## Lesson 2 - READING A WHEEL

**`readwheel.hints|then`** (F10)
- Part C: "Want the hints back? [F10] > RADIAL > Reset hint counters."
- Shipped: "Want the hints back? [F10] > Radial > Reset hint counters."
- Why: the tab title. The button still exists, in Radial > Radial feel (`UI/Menu/Tabs/RadialTab.cs:189`).

## Lesson 3 - TOOLS AND BATTERIES / TABLETS AND CARTRIDGES

**`tools.careful|title`** (BUDGET)
- Part C: "Click takes, slide out chooses" (30 characters)
- Shipped: "Click takes, slide chooses"

**`tools.tablet.window|says`** (VERIFY, code check)
- Part C: "Rather click, like vanilla? Tap [B], free the mouse, and click the tablet: its slots open in a small window."
- Shipped: "Rather click, like vanilla? In the big window ([B]), click a tablet in one of your bags: its slots open in a small window."
- Why: the big window draws no cells for the root, so hands, helmet and the rest are not in it
  (`UI/Grid/GridRegionView.cs:459-463`). The small window opens from a click on a cell in a bag
  (`UI/Grid/BagGridCell.cs:393-401` -> `Core/DeviceWindow.Open`). A tablet held in the hand
  cannot be clicked there.

## Lesson 4 - SPLITTING STACKS

**`split.open|says@client`** (NEW)
- Part C: none.
- Shipped: "Stacks split. Slide out on a stack to get SPLIT ONE and SPLIT HALF."
- Why: an MP client never gets SPLIT COUNT (`ItemMenuBuilder.cs:726`, gate
  `ItemActions.CanSplitCount` = `GameManager.RunSimulation`). Part C's 4.2 already had a client
  line; 4.1 needed one too.

**`split.window|says`** (VERIFY 4.3)
- Part C: "In the big window, click a stack with the mouse free: split ONE, HALF, or pick a NUMBER."
- Shipped: "In the big window, free the mouse and click a stack: small buttons split off one, half, or a number you scroll to."
- Why: the controls are small square buttons "1" and "1/2", plus a count square you scroll over
  and then click (`UI/Grid/GridRegionView.cs:535-548`). The copy says what they do, not what they
  look like.

**`split.window|says@client`** (NEW, VERIFY 4.3)
- Shipped: "In the big window, free the mouse and click a stack: small buttons split off one, or half."
- Why: the count square uses the same `CanSplitCount` gate, so it is hidden on an MP client.

## Lesson 5 - DRAG AND DROP

**`dragdrop.drop|then`** (VERIFY 5.2)
- Part C: "Right-click, the wheel's key, the Close band - however you close it, dragged-out items drop."
- Shipped: "Right-click, Esc, the wheel's key, the Close band - however you close it, dragged-out items drop."
- Why: Esc drops them too (D-004, `Overlay/ParkingState.cs:11-12`). Part C's "Esc puts them back"
  fallback is not needed. Jumping to another wheel (1 - 6, Tab, Q) also drops them, because an
  open replaces the menu (`Overlay/RadialMenu.cs:551-555`). "However you close it" covers that
  without spending the 100-character budget.

## Lesson 6 - GEAR KEYS 1 - 6

**`gear.ingrid|title`** (BUDGET)
- Part C: "Open a worn bag in the window" (29 characters)
- Shipped: "A worn bag in the window"

## Lesson 9 - SMART STOW

**D-016 mapping (words unchanged).** `stow.order|says` is Part C's `[PENDING D-016]` line ("[G] puts
an item back where it came from. New items try their belt slot, a matching stack, then an empty
socket."). `stow.order|says@complex` is Part C's original ("[G] tries, in order: ...").

**`stow.where|then`** (D-016, F10)
- Part C: "[F10] > STORAGE > Settings can also show a note saying where and why (Advanced)."
- Shipped: "No home yet, or its home is full? A short note says where it went. Turn it off in [F10] > SmartStow."
- Why: in Simple mode (the default) a note ("No home yet - put in <bag>" or "Home full - put in
  <bag>") shows by default after any landing that is not the item's home
  (`Features/SmartStowPlus.cs:223-245`; `StowModeConfig.SimpleNotes`, default on). Its toggle is
  "Tell me when an item has no home yet, or its home is full" on SmartStow > Return Home
  (`UI/Menu/Tabs/Storage/SimpleStowPage.cs:61-63`).

**`stow.where|then@complex`** (NEW, F10)
- Shipped: "[F10] > SmartStow > Routing > More options can also note where each item went, and why."
- Why: in Complex mode, "Show where items were stowed (and why)" sits in the Routing page's More
  options (`UI/Menu/Tabs/Storage/RoutingPage.cs:74`).

**`stow.profiles|says`** (D-016, F10)
- Part C: "Want one bag for ores and another for cables? Give each a Bag Profile in [F10] > STORAGE > Bags."
- Shipped: "Want one bag for ores and another for cables? Set [F10] > SmartStow to Complex and give each bag a profile."
- Why: Bag Profiles exist only in Complex mode, on the Organizer page (`UI/Menu/Tabs/StorageTab.cs:18-22`).
  This is Part C's "retitle the path once the F10 layout is known".

**`stow.profiles|says@complex`** (F10)
- Shipped: "Want one bag for ores and another for cables? Give each a Bag Profile in [F10] > SmartStow > Organizer."

## Lesson 10 - SETTINGS AND HOTKEYS

**`settings.keepopen|then`** (F10)
- Part C: "Rather it always stayed open? Flip that in [F10] > RADIAL."
- Shipped: "Rather it always stayed open? Flip that in [F10] > Radial."
- Why: the tab title. The toggle is "Holding Shift keeps the wheel open after an action" in
  Radial > Behaviour (`RadialTab.cs:76`).

**`settings.keepopen|then@inverted`** (NEW)
- Shipped: "Rather it closed after each action? Flip that in [F10] > Radial."
- Why: Part C gave the inverted player a Says line but no Then line. The plain Then would have
  told them the opposite.

## Lesson 11 - FASTER WHEELS

**`speed.move|then`** (F10)
- Part C: "More speed options, like double-tap to repeat, live in [F10] > RADIAL."
- Shipped: "More speed options, like double-tap to repeat, live in [F10] > Radial."
- Why: the tab title. "Double-tap repeats last pick" is in Radial > Radial feel (`RadialTab.cs:181`).

## Lesson 12 - THE BIG WINDOW

**`window.shiftdrag|says`** (SHIFT, VERIFY 12.4)
- Part C: "Hold Shift while you drag an item: every item of that type comes along."
- Shipped: "Hold [Shift] while you drag an item into another bag: every item of that type in its bag comes along."
- Why:
  - The key is vanilla's rebindable MoveAllOfType. The token resolves live through
    `KeyManager.GetKey("MoveAllOfType")`: V27798 `KeyManager.cs:493` (AddKey), `:558` (GetKey),
    `:352` (default LeftShift). Our code reads it at `Core/ItemActions.cs:589-593`.
  - Scope: vanilla moves the same type out of the SOURCE bag into the destination
    (`ItemActions.cs:600-604`, mirroring `Slot.TryMoveAllOfType`). It is not "everything you carry".

## Lesson 13 - NAME YOUR BAGS

**`names.how|says`** (D-016, F10, VERIFY 13.2)
- Part C: "Give them names and colors: rename in [F10] > STORAGE > Bags or with a Labeller, and paint them."
- Shipped: "Give bags names and colors: a Labeller renames one, and a spray can paints one you set down."
- Why:
  - Renaming in F10 exists only in Complex mode, on SmartStow > Organizer
    (`OrganizerPage.cs:1993` -> `ItemActions.RenameThing`). Simple players get the Labeller.
  - Paint: vanilla sprays through `Thing.AttackWith` on a thing you aim at in the world (V27798
    `Thing.cs:5021-5023` -> `ISprayer.DoSpray`). A carried bag has to be set down first.

**`names.how|says@complex`** (F10, VERIFY 13.2)
- Shipped: "Give bags names and colors: rename in [F10] > SmartStow > Organizer or with a Labeller; spray paint one set down."

## Lesson 14 - YOUR VISOR

**`visor.lowpower|then`** (F10)
- Part C: "Don't want the flicker? [F10] > HUD."
- Shipped: "Don't want the flicker? [F10] > HUD > Low-power dropouts."
- Why: names the actual toggle (`UI/Menu/Tabs/HudTab.cs:111`). While Diegetic tiers is on (the
  default) it sits right under that toggle (D-020).

## Lesson 15 - YOUR SENSES

**`senses.numbers|says`** (VERIFY 15.3)
- Part C: "Power a suit to get your numbers back. Want numbers all the time? [F10] > HUD > Diegetic tiers."
- Shipped: "Power a suit to get your numbers back. Want numbers all the time? Switch off [F10] > HUD > Diegetic tiers."
- Why: with Diegetic tiers off, the tier is forced to Suited (`UI/Hud/HudSampler.cs:226`), so the
  numbers layout is always up. The line now says which way to flip the toggle.
- Not checked in game: what each suit readout shows with no suit on at all. It may read "--".

---

## Lesson 16 - THE MENU (the F10 tour), rewritten for the new F10

Part C's tour was written for the old F10. That F10 had the Simple/Advanced buttons, "HUD Themes"
and "Storage". Contract s0 asks for the same intent against the rebuilt F10. The tour keeps the
same six step ids, so saved progress and overrides still match.

Every anchor is one F10 actually reports (`UiaControlCenter.TryGetAnchorRect`,
`UiaControlCenter.cs:247-291`).

| # | Step | Anchor | Title | Callout (shipped) |
|---|---|---|---|---|
| 16.1 | `menu.density` | `search` | Find any setting | "This is the UI Ascended menu. Type in the search box to jump to any setting. Each section shows the basics; More options holds the rest." |
| 16.2 | `menu.halves` | `master` | Two halves | "Want it closer to vanilla? The two switches up top, RADIAL MENUS and VISOR HUD, turn each half off - each works without the other." |
| 16.3 | `menu.themes` | `tab:UI Themes` (demo `themeswitch`) | Pick your look | "Click a card to restyle everything at once - HUD, wheels and windows. Shipped themes are read-only: duplicate one (Author > More options) to change it." |
| 16.4 | `menu.settings` | `tab:SmartStow` | The settings tabs | "Radial, HUD and SmartStow hold the settings for your wheels, your visor, and where [G] puts things - Simple or Complex." |
| 16.5 | `menu.controls` | `tab:Controls` | Your keys | "Rebind any UI Ascended key here. Lessons always show your current keys." (unchanged) |
| 16.6 | `menu.guide` | `tab:Guide` | Your reference card | "Guide is your reference card - replay any lesson here. Found a bug? Suggestions/Bugs sends it to us. The pause button holds single-player still." |

The tour opener ("First time here? A 30-second tour." with **[Take the tour]** / **[No thanks]**)
is unchanged. So are the Back / Next / Done / Skip tour buttons.

What each callout replaced, and the code it was checked against:

- **16.1** replaced the title "Simple and Advanced" and "This is the UI Ascended menu. Simple
  shows what most players need; Advanced shows everything."
  - The density toggle is gone. The title-bar search and the per-section "More options" replaced
    it (`UiaSearch.RegisterRow`; section Extras).
  - Anchor: `search` (was the Simple / Advanced buttons).
- **16.2** replaced "Two halves. Want it closer to vanilla? Switch off RADIAL MENUS or VISOR HUD -
  each works without the other."
  - The strip sits right under the title bar. Its labels are still RADIAL MENUS and VISOR HUD
    (`UiaControlCenter.cs:791-793`).
  - The title "Two halves" already says it, so the opening "Two halves." was dropped.
- **16.3** replaced "...duplicate one (Advanced) to change it."
  - "Advanced" is gone. "Duplicate active" is now under the UI Themes tab's Author section, inside
    More options (`ProfilesTab.cs:180-212`).
  - Anchor: `tab:UI Themes` (was "HUD Themes").
- **16.4** replaced "RADIAL, HUD and STORAGE hold the settings for your wheels, your visor, and
  your bags and Smart Stow."
  - Storage is now SmartStow, with a Simple / Complex switch at the top of the tab
    (`StorageTab.cs:36, 137`).
  - Anchor changed from Radial to `tab:SmartStow`, so the player sees that switch. **Your call:**
    `tab:Radial` works too.
- **16.6** replaced "GUIDE is your reference card - replay any lesson here. The pause button up top
  holds single-player still while you browse."
  - Adds the new Suggestions/Bugs tab.
  - One callout has one anchor, so it points at the Guide tab only. Part C's "then the pause
    button" hop is gone; `pause` is available if you want a seventh callout.

---

## Lesson 17 - THE HUD DESIGNER

**`designer.card|body`** (Q8: yes, mention the Rangefinder)
- Part C: "Move, resize and restyle every piece of your HUD - even a separate look for suit and no suit. The four shipped themes are read-only, so duplicate one first. The Designer Handbook explains it all." The Rangefinder line was optional.
- Shipped: the same body, plus "\nNew: a Rangefinder element shows the distance to what you aim at."
- Checked: there are four shipped themes (`UI/Hud/ShippedProfiles.cs:29-32`). The Rangefinder is a
  real element (`HudDocument.cs:60`, `RangefinderWidget.cs`).

## Lesson 18 - WHAT'S NEW

**`whatsnew.card|body`** (WORD, SHIFT, VERIFY 18.1)
- Part C: "- Wheels now say TAKE or OPEN at the top before you click.\n- Close a wheel and anything you dragged out drops at your feet.\n- [Q] swaps belts from the [6] wheel too; RIGHT-CLICK backs out.\n- Big window: Shift + drag moves a whole type; stacks split to any number."
- Shipped: "- Wheels now say what a click does, outside the wedge you point at.\n- Close a wheel and anything you dragged out drops at your feet.\n- [Q] swaps belts from the [6] wheel too; RIGHT-CLICK backs out.\n- Big window: [Shift] + drag moves a whole type; click a stack to split it."
- Why, bullet by bullet:
  - Bullet 1: the word moved. It also isn't only TAKE/OPEN now (EQUIP, SWAP, STOW...).
  - Bullet 4: an arbitrary split count is host and single-player only (D-005), so "any number"
    was dropped.
  - Bullets 2 and 3: checked, unchanged. D-004 is at `RadialMenu.cs:551-555, 583+`. D-007 (Q on
    the [6] ring) is at `RadialController.cs:383`. D-064 (RMB back out of the picker) is at
    `RadialController.cs:455`.
  - The body is 273/280 characters with default keys.

**`whatsnew.card|says@mp`** (F10)
- Part C: "UI Ascended updated - see what's new in [F10] > GUIDE."
- Shipped: "UI Ascended updated - see what's new in [F10] > Guide."

**`whatsnew.word|body`** (WORD, EQUIP)
- Part C: "Point at a wedge and read the word at the top of the wheel: TAKE puts it in your hand, OPEN looks inside."
- Shipped: "Point at a wedge and read the word just outside it: TAKE or EQUIP puts it in your hand, SWAP trades it for what you hold, OPEN looks inside."
- Why: a take with a full hand is now worded SWAP (`RadialMenu.cs:159-175`). That was D-021's
  complaint.

**`whatsnew.belts|body`** (VERIFY 18.1)
- Part C: "[Q] opens the belt chooser from the [6] wheel as well as [MMB]. RIGHT-CLICK takes you back to the belt you came from."
- Shipped: "[Q] opens the belt chooser from the [6] wheel as well as [MMB]. RIGHT-CLICK goes back to your belt wheel."
- Why: RMB in the picker returns to the belt ring you pressed Q from, and nothing swaps
  (`RadialController.cs:455`). "The belt you came from" could be read as undoing a swap.

**`whatsnew.window|body`** (SHIFT, VERIFY 18.1 / 12.4)
- Part C: "Hold Shift while you drag to move every item of that type. Click a stack to split one, half, or a number you pick."
- Shipped: "Hold [Shift] while you drag an item into another bag to move every item of that type with it. Click a stack to split it: one, half, or (single-player or host) a number you pick."
- Why: the MoveAllOfType token. The scope is the source bag, as in 12.4. The count square is
  host and single-player only.

---

## Chrome (C.19)

- **`chrome|waiting`**: "Lesson waiting - [F10] > GUIDE" -> "Lesson waiting - [F10] > Guide" (F10).
- **`chrome|lessonsoff`**: "Lessons are off. Turn them back on in [F10] > GUIDE." -> "... > Guide." (F10).
- **`chrome|reopen`**: "The wheel closed - tap [MMB] to open it again." -> "The wheel closed - tap {OPENER} to open it again."
  - Part C says this line uses "the lesson's own opener token". `{OPENER}` is the placeholder the
    director swaps for that key, so the belt lessons show [MMB] and the tool lessons show [R].
- **Not editable, on purpose.** The GUIDE tab's Lessons-section strings from C.19 are fixed in
  `GuideTab.cs`: the header, note, Watch / Try it, the toggle, Continue / Skip / Stop / Restart.
  The same goes for the `uiatutorial` console lines. Editable keys for them would do nothing, so
  there are none. Every chrome key that is in the file is actually shown by the director.

## Also changed (not text, but it changes what shows)

- `designer.card` demo: `finish` -> `designer`. The card shows the finish scene with only the F9
  row pulsing.
- `whatsnew.card` demo: `actionword` -> `whatsnew`. It cycles the four what's-new scenes.
- `tools.tablet.replace` demo: `toolreplace` -> `toolreplace.cartridge`. The labels read
  CARTRIDGE, as Part C 3.3T asked.

## Still open

- **1.7** doubled-chevron size vs the `pushout` demo: this is demo art, not copy. The Demos agent
  owns it.
- **14.6** robot battery swap: still not taught. The robot line stays neutral ("find a charged
  battery").
- **3.2 / 3.3 canister wording:** the words switch to "canister", but the mini demo still shows
  the battery version. The stage does have a `toolreplace.canister` scene. A step has one demo
  id, so switching it per variant is a director change.

---

## Re-editing any of this

You can change any line above in game:

1. Run `uiadev`.
2. Either press F8 on a live lesson (edit while playing), or run `uiatutorial edit` for the
   editor window.
3. Edits save locally straight away and override the shipped text.
4. To fold them into the repo, press **Export** in the editor, then run
   `tools/bake-tutorial.ps1` (add `-ClearOverrides` to back up and clear your local override file
   afterwards).

The export writes this same file byte for byte, so the bake is a clean diff of just your changes.

---

## Tour mode (2026-09-26)

**Why.** Your two calls on 2026-09-26: "I need it to show all the lessons in order one after another
when a player first opens the mod", and "make sure that we explain the new universal inventory,
radials etc. Right off the bat." This replaces the plan's "8-step core, then just-in-time lessons"
pacing for the FIRST run. "Teach me as I go" and replays from Guide work as before.

**How the tour runs** (the director side is being built against this data):

1. Welcome > **Start the tour**.
2. **THE BIG PICTURE**: five paused cards, one idea each - two hands, the wheels, the Universal
   Inventory, Smart Stow, the F10 menu. It is a new lesson (`overview`), so it can be replayed from
   Guide too.
3. **LESSON 1/17 - FIRST STEPS** through **LESSON 17/17 - THE HUD DESIGNER**, back to back, in the
   C.0 order. Lesson 3 plays the tool track, then the tablet track (a new game carries both a Drill
   and a tablet, and the tablet is the one testers got stuck on).
4. **That's everything** (`entry.tourend`).

What's new (lesson 18) is not in the tour: the tour covers all of it, and it stays in Guide.

**Setup steps.** Every lesson is hands-on now. When a lesson's situation isn't there yet, its first
step is a SETUP strip that says how to get into it: take the tablet out of the jetpack, open the belt
wheel, press [F10]. If the situation is already there, the setup step is skipped without showing. If
the player never gets there, it skips itself the usual way ("skips itself in 15 s"), and that lesson
comes back by itself, just-in-time, later.

Which lessons got one:

| # | Lesson | Its C.0 trigger was... | Setup? |
|---|---|---|---|
| 1 | FIRST STEPS | Welcome > Start | No - it has its own fallbacks |
| 2 | READING A WHEEL | only a reason to fire | Yes - open a wheel to read |
| 3 | TOOLS AND BATTERIES / TABLETS AND CARTRIDGES | needed: a tool or tablet in hand | Yes - one per track |
| 4 | SPLITTING STACKS | needed: a stack in a wheel | Yes |
| 5 | DRAG AND DROP | only a reason to fire | Yes - open a wheel |
| 6 | GEAR KEYS 1 - 6 | only a reason to fire | No - 6.1 already says "TAP one" |
| 7 | BELTS | needed: a second belt | Yes |
| 8 | BAGS AND SEARCH | only a reason to fire | Yes - open the bag wheel |
| 9 | SMART STOW | only a reason to fire | No - three reads |
| 10 | SETTINGS AND HOTKEYS | needed: a wheel with settings | Yes |
| 11 | FASTER WHEELS | only a reason to fire | No - 11.1 already says "HOLD [MMB]" |
| 12 | THE BIG WINDOW | needed: window open, mouse free | Yes |
| 13 | NAME YOUR BAGS | only a reason to fire | No - two reads |
| 14 | YOUR VISOR | needed: suit power on | Yes |
| 15 | YOUR SENSES | needed: no suit power | Yes - optional, safe air only |
| 16 | THE MENU | needed: F10 open | Yes |
| 17 | THE HUD DESIGNER | needed: the F9 press | Yes |

**Safety.** No setup asks for anything dangerous.

- Lesson 14's setup only ever ADDS suit power.
- Lesson 15's is the only one that takes something away. It says "optional" in its title and its
  first word, only where the air is safe to breathe, and "let this skip" is the easy answer. On a
  fresh world that's what most players will do, and the lesson still comes the first time their suit
  loses power for real. A robot never sees it (it can't happen to them).
- Lesson 10's points at the jetpack's Thrust, not the suit: a suit's pressure and temperature are the
  one value wedge that can hurt if scrolled by accident.

Text is shown with default keys, like the rest of this file; the file stores tokens.

### THE BIG PICTURE (new lesson, the tour's opening cards)

| Key | Text (default keys) | Why |
|---|---|---|
| lesson:overview\|title | "THE BIG PICTURE" | NEW lesson title (GUIDE list row, replay heading). |
| overview.hands\|title | "Two hands, no hotbar" | NEW. Card 1 of 5: two hands, no hotbar. |
| overview.hands\|heading | "Two hands, no hotbar" | NEW. |
| overview.hands\|body | "The two boxes at the bottom of your screen are your hands - there is no hotbar. The lit box is your active hand: whatever you grab lands there. [E] switches hands.\nEverything else you carry rides on your belt and in your bags." | NEW. Same facts as 1.1 (`core.hands`), said once, up front. |
| overview.hands\|button@0 | "Next" | NEW. Each card's own button moves on, so the plain lesson path plays the cards as they are. |
| overview.wheels\|title | "Everything opens as a wheel" | NEW. Card 2: the wheels and how each one opens. |
| overview.wheels\|heading | "Everything opens as a wheel" | NEW. |
| overview.wheels\|body | "Tap a key and a wheel opens: [MMB] your toolbelt, [Tab] your bags (THE HUB), [1] - [6] each piece of gear you wear, [R] the item in your hand.\nPoint at a wedge and read the word beside it: it says what a click will do. Click to do it; RIGHT-CLICK steps back." | NEW. Keys from `UiaKeybinds` and vanilla's 1 - 6; the word beside the wedge is 1.3's (`UnityRadialView.cs:314-323`); RIGHT-CLICK back is 1.6's. |
| overview.wheels\|button@0 | "Next" | NEW. |
| overview.window\|title | "The Universal Inventory" | NEW. Card 3: the Universal Inventory (its window title, `TheGridPanel.WindowTitle`). |
| overview.window\|heading | "The Universal Inventory" | NEW. |
| overview.window\|body | "Tap [B] for the Universal Inventory: every bag you carry in one window, vanilla-style. Double-tap [Alt] to free the mouse, then click or drag items anywhere. Drag a bag's folder tab out to give it its own window.\nThe lessons call it the big window." | NEW. Tap to open, double-tap Alt and tab-out pinning, as the GUIDE card says; ties the name to "the big window" the lessons use. |
| overview.window\|button@0 | "Next" | NEW. |
| overview.stow\|title | "Smart Stow" | NEW. Card 4: Smart Stow. |
| overview.stow\|heading | "Put it back with [G]" | NEW. Same token title as 1.5. |
| overview.stow\|body | "Done with something? Press [G]: Smart Stow puts what's in your hand back where it belongs - by default, right back where you took it from. The box that flashes shows where it went.\nIt works with a wheel open, too." | NEW. "Where it belongs" holds in both modes; "by default, right back where you took it from" is Simple (D-016, the default). |
| overview.stow\|button@0 | "Next" | NEW. |
| overview.menu\|title | "Everything else: [F10]" | NEW. Card 5: F10, and skipping lessons. |
| overview.menu\|heading | "Everything else: [F10]" | NEW. |
| overview.menu\|body | "[F10] opens the UI Ascended menu: every setting, UI Themes to restyle it all, and your keys under Controls.\nIts Guide tab lists every lesson - replay any of them, or skip the one you're on." | NEW. Tabs as in the rebuilt F10; Guide's Lessons section has Watch / Try it per row and "Skip this lesson" for the running one. |
| overview.menu\|button@0 | "Got it" | NEW. Neutral on purpose: in the tour First Steps follows; in a replay the lesson just ends. |

### Setup steps (new)

| Key | Text (default keys) | Why |
|---|---|---|
| readwheel.setup\|title | "Open a wheel" | NEW setup (lesson 2). |
| readwheel.setup\|says | "Open a wheel to look at: tap [MMB] for your belt. Keep it open while you read." | Lesson 2 reads a wheel, so it needs one open. |
| tools.setup\|title | "Hold a battery tool" | NEW setup (lesson 3, tool track). |
| tools.setup\|says | "Take a tool with a battery into your hand - like the Drill on your belt: tap [MMB] and click it." | The new-game Drill has a battery (plan A.4). "Like" = an example, never assumed. |
| tools.tablet.setup\|title | "Hold the tablet" | NEW setup (lesson 3, tablet track). The tablet-track title key now sits on this step (the track's first). |
| tools.tablet.setup\|says | "Next, the tablet - a new game packs one in your jetpack. Tap [Tab], open the jetpack and click the tablet." | A new game's tablet rides in the jetpack (A.4). [Tab] root > the jetpack OPENs > the tablet is "Take to hand" (`ItemMenuBuilder.cs:358-369`). |
| split.setup\|title | "Find a stack" | NEW setup (lesson 4). |
| split.setup\|says | "Point at a stack in a wheel - like the Cable Coil on your belt: tap [MMB] and rest on it." | Done = the lesson's own trigger: resting 0.5 s on a stack wedge (more than one in it). The Cable Coil x50 is on the new-game belt. |
| dragdrop.setup\|title | "Open your belt wheel" | NEW setup (lesson 5). |
| dragdrop.setup\|says | "Open a wheel with items in it - tap [MMB] for your belt - and keep it open." | 5.1 says "drag an item off its wedge" but never says how to open one. |
| belts.setup\|title | "A second belt" | NEW setup (lesson 7). |
| belts.setup\|says | "Tap [MMB] for your belt wheel. This lesson needs a second belt in a bag - a new game packs one in your jetpack." | Done = the belt wheel open with a second belt in reach (the lesson's own trigger). The new-game mining belt is in the jetpack. |
| bags.setup\|title | "Open your bags" | NEW setup (lesson 8). |
| bags.setup\|says | "Tap [Tab]: your bags open as a wheel. Keep it open for this lesson." | 8.2 onward work inside the bag wheel. |
| settings.setup\|title | "A wheel with settings" | NEW setup (lesson 10). |
| settings.setup\|says | "Open a wheel with settings: tap [4] - a jetpack's wheel has its Thrust, a number you can scroll." | SAFETY: the jetpack, not the suit. The jetpack has two controls (Stabilizer, Thrust), so both sit on its own wheel (`Jetpack.cs` V27798 :683-735; three or more would fold into a SETTINGS wedge, `ItemMenuBuilder.UseSettingsWedge`). |
| window.setup\|title | "Open the big window" | NEW setup (lesson 12). |
| window.setup\|says | "Tap [B] to open the big window, then double-tap [Alt] to free the mouse." | Done = 1.8's own check (window open, mouse free for 1 s). |
| visor.setup\|title | "Suit power on" | NEW setup (lesson 14). |
| visor.setup\|says | "This lesson needs suit power. No power? Tap [3] and slide out on the battery slot to put a charged battery in." | Suit power = a battery in the suit that isn't empty (`HudSampler.cs:213-229`); there is no on/off switch. "Slide out on the battery slot" covers INSTALL (empty slot) and REPLACE (flat battery). |
| senses.setup\|title | "Suit power off (optional)" | NEW setup (lesson 15). "Optional" is in the title too. |
| senses.setup\|says | "Optional - only where the air is safe to breathe: tap [3] and click the battery to take it out. Not sure? Let this skip." | SAFETY: see above. Clicking the suit's battery wedge is "Take to hand" (`ItemMenuBuilder.cs:368-369`). |
| senses.setup\|then | "That's it. When this lesson ends, put it back: [3], slide out on the empty slot, INSTALL." | Shows only when they actually took the power off: how to put it back. |
| menu.setup\|title | "Open the menu" | NEW setup (lesson 16). |
| menu.setup\|says | "Press [F10] to open the UI Ascended menu - this lesson walks you through it." | The F10 tour needs F10 open; pressing it themselves teaches the key. |
| designer.setup\|title | "Open the HUD Designer" | NEW setup (lesson 17). |
| designer.setup\|says | "Press [F9], the key for the HUD Designer - where you can restyle your whole HUD." | Pressing F9 opens the Designer card first (lesson 17's own intercept); its first button opens the Designer. |

### Entry cards (changed and new)

These three bodies supersede the Lesson 0 entries at the top of this file.

| Key | Text (default keys) | Why |
|---|---|---|
| entry.welcome\|body | "Your inventory now runs on wheels and one big window. Your bottom bar is just your two hands - there is no hotbar.\nThe game is paused. The tour shows every lesson in order, hands-on: about 20-30 minutes, and the game runs while you practice. Skip any lesson from [F10] > Guide." | The tour is long now: an honest time, and where to skip. The first line is Part C's, unchanged. |
| entry.welcome\|body@mp | "Your inventory now runs on wheels and one big window. Your bottom bar is just your two hands - there is no hotbar.\nMultiplayer can't pause - start when you're safe. The tour shows every lesson in order, hands-on: about 20-30 minutes. Skip any lesson from [F10] > Guide." | Same change, multiplayer wording. |
| entry.welcome\|body@running | "Your inventory now runs on wheels and one big window. Your bottom bar is just your two hands - there is no hotbar.\nThe game keeps running - start when you're safe. The tour shows every lesson in order, hands-on: about 20-30 minutes. Skip any lesson from [F10] > Guide." | Same change, with the card pause turned off. |
| entry.welcome\|button@0 | "Start the tour" | Was "Start - 3 minutes". Buttons 2 and 3 are unchanged: "Teach me as I go", "No lessons, thanks". |
| entry.resume\|body | "You were partway through the lessons. Pick up where you left off? You can skip any lesson from [F10] > Guide." | Was "You were partway through the first lessons. Pick up where you left off? It only takes a couple of minutes." - not true of the tour. |
| entry.tourend\|title | "That's everything" | NEW card after the tour's last lesson. |
| entry.tourend\|heading | "That's everything" | NEW. |
| entry.tourend\|body | "That's all of UI Ascended: two hands, wheels, one big window, Smart Stow, your visor and the menu.\nA lesson you couldn't do yet comes back by itself the first time you need it. Replay any lesson from [F10] > Guide." | NEW. "Couldn't do yet" = a lesson whose setup skipped itself; it comes back just-in-time. |
| entry.tourend\|button@0 | "Start playing" | NEW. Same pair as First Steps' last card. |
| entry.tourend\|button@1 | "See all lessons" | NEW. Opens F10 on Guide. |

### First Steps' last card, in the tour

| Key | Text (default keys) | Why |
|---|---|---|
| core.finish\|body@tour | "That's the core: two hands, wheels and one window.\nThe other lessons follow, one after another. Skip any of them from [F10] > Guide - or stop the tour here, and each lesson shows up by itself the first time you need it." | NEW variant. In the tour, 1.9 is the hand-over to lesson 2, with the buttons "Next lesson" / "Stop the tour" (below). Replayed on its own, 1.9 keeps its old body and buttons. |

### Chrome (new)

| Key | Text | Why |
|---|---|---|
| chrome\|tourheader | "LESSON {N}/{TOTAL} - {TITLE}" | NEW. The strip header during the tour, e.g. "LESSON 3/17 - TOOLS AND BATTERIES". {N}, {TOTAL} and {TITLE} work like {OPENER}: move them around freely. Budget 48, counted with "17" and a 28-character title. |
| chrome\|tournext | "Next lesson" | NEW. First Steps' last card, in the tour. |
| chrome\|tourstop | "Stop the tour" | NEW. Same card: stop the tour; the other lessons then come just-in-time. |

### Changed for safety (not a setup)

| Key | Text (default keys) | Why |
|---|---|---|
| gear.hold\|says | "HOLD a number to take it off into your hand (never helmet or suit outside!). Holding gear that fits? Its number puts it on." | Was "HOLD a number to take that item off into your hand. Holding gear that fits? Its number puts it on." The tour runs this wherever the player happens to be, and holding 1 or 3 outside takes the helmet or suit off in vacuum. Revert it in game if you'd rather not. |

### Also worth knowing

- `core.finish` keeps its body and both buttons for replays. Only the tour uses `body@tour` and the
  two new chrome buttons.
- Lesson 3's tablet-track title (`lesson:tools|title@tablet`) moved from `tools.tablet.open` to
  `tools.tablet.setup`, the track's new first step. Same key, same words; in the editor it now shows
  under that step.
- The F10 lesson (16) has its own "Take the tour" / "Skip tour" buttons, and that is a different
  tour. The director is asked to show "Skip lesson" on those callouts during the first-run tour, so
  "Skip tour" can't be read as "skip everything".
- Setup strips only appear when their situation is missing, so to see one live, start its lesson
  without it (for example `uiatutorial play belts` with the belt wheel shut). The tour header only
  appears during the tour.

**Totals now:** 20 lessons (0-18 plus THE BIG PICTURE) and 101 steps, 13 of them setups. With the 7
tips and 22 chrome strings, that's 347 editable strings. Every one fits its budget with default keys
and is plain ASCII, and the export still writes `TutorialCopy.g.cs` byte for byte.

---

## Tour review fixes (2026-09-26)

From the review of tour mode: one new lesson string, three new lines of Guide text, and a fix to
how the lesson editor counts one chrome line.

### New lesson string

| Key | Text (default keys) | Why |
|---|---|---|
| senses.numbers\|then | "Took your suit battery out? Put it back now: [3], slide out on the empty slot, INSTALL." | NEW field on lesson 15's last step (budget 100; 87 with default keys). The setup's Then says "When this lesson ends, put it back", but when the lesson ended, nothing reminded the player. This Then plays after the last read, so it is the last thing the lesson says. It asks a question because the lesson also runs for players who never took the battery out: a flat battery (just-in-time) or a Watch replay. It doesn't say "power it on" because a suit has no power switch; suit power is a charged battery in the suit (see visor.setup). A Then line isn't cut off when the suit powers up, so a player who puts the battery back while it shows still finishes the lesson. |

### Guide text (not lesson copy, so the lesson editor doesn't show it)

| Where | Text | Why |
|---|---|---|
| F10 > Guide > Lessons: a button in its own row | "Stop the tour" | NEW. Shown only while the first-run tour is running or paused. It ends the tour and leaves the lessons switch alone. "Stop all lessons" also turns lessons off, and before this the Guide had no way to stop only the tour. The words match the tour card's button (chrome\|tourstop), but here it is a plain Guide label, like the buttons beside it. |
| The same section: a note under that button | "Stop the tour ends just the tour; Stop all lessons also turns lessons off. Lessons the tour didn't finish show up once, when you need them." | Explains how the two buttons differ. |
| The same section: the status line after pressing it | "Tour stopped. Lessons it didn't finish show up once, when you need them - or replay any from this list." | Written like the status lines already there ("Lessons stopped. Replay any of them from this list."). |

### Lesson editor: the character counter now agrees with the lint

For `chrome|tourheader`, the editor showed 28/48, but the lint measured it at 43/48. The editor
replaced only `{OPENER}`, so it counted the placeholders as the literal text [N], [TOTAL] and [TITLE].
Now one helper (`TutorialLint.ShownForBudget`) feeds the lint, the editor's counter and its "on
screen" line. All three read the header at its longest: "LESSON 17/17 - " plus a 28-character title,
which is 43/48. On the two fields with placeholders (`chrome|reopen` and `chrome|tourheader`), the
counter's tooltip explains this. If a placeholder is typed into any other field, the preview now
shows the bracketed text the player would really see; the lint already flagged it.

**Totals now:** 20 lessons and 101 steps, as before, and 348 editable strings (one new). Every one
fits its budget with default keys, all are plain ASCII, and the export still writes
`TutorialCopy.g.cs` byte for byte.
