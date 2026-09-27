# Steam guide - Stationeers UI Ascended 1.0

Everything below is ready to paste. Steam has no API for guides, so publishing it is a few
minutes of clicking in the browser. The images are in `Documentation/Launch/guide-images/`.

## How to publish it (about 15 minutes)

1. In Steam, open the **Stationeers** community hub > **Guides** > **Create a Guide**
   (https://steamcommunity.com/app/544550/guides - the button is on the right).
2. Fill in the **Guide settings** below: title, category, language and summary. For the
   guide icon, upload `00-guide-icon.jpg`. Tick that you created it and click
   **Save & continue**.
3. On the editor page, click **Add/Edit Images** and upload every file in `guide-images/`.
   If Steam refuses a GIF as too large, skip it; each GIF has a still that covers the section.
4. For each section below, click **Add Section**. Paste the **Section title**, then paste
   the **Body**. Wherever the body says `[IMAGE: filename]`, delete that marker, leave the
   cursor there, and click that image in the image panel on the right. Steam inserts its own
   image tag at the cursor.
5. Click **Preview** to check it, then **Publish**, and set visibility to **Public**.
6. Copy the guide's URL. Once the guide is finished, add a "Full illustrated guide:" line with
   it to the Workshop description's Getting started list (then re-run
   `tools\update-workshop-page.ps1`), and to the posts if they haven't gone out yet.

Two sections want a screenshot that doesn't exist yet: the F9 HUD Designer (section 7) and
the Suggestions/Bugs tab (section 9). They're marked `[NEW SCREENSHOT NEEDED: ...]` with
what to capture. Take them in game with F12 (Steam's screenshot key), or publish without them
and add them later. A guide can be edited after it's published.

---

## Guide settings

**Title:** Stationeers UI Ascended 1.0 - The Complete Guide

**Category:** Modding or Configuration (or the closest one Steam offers)

**Language:** English

**Summary:**
Everything in Stationeers UI Ascended 1.0: the radial wheels, the two-hand visor HUD, the Universal Inventory, a Smart Stow that remembers where things go, themes, the F9 HUD Designer, the F10 Control Center, reporting bugs from inside the game, and a full controls cheat sheet.

---

## Section 1

**Section title:** Welcome to UI Ascended

**Body:**
```
[IMAGE: 01-hero-visor-hud.jpg]

[b]Stationeers UI Ascended[/b] rebuilds Stationeers' inventory and HUD around your two hands. Radial wheels replace menu-diving, the HUD becomes a curved suit visor you can redesign live, and Smart Stow puts things back where they came from.

It keeps the game's own physical inventory rules. You still have two hands, batteries still run flat, and hauling things around is still a real job. The mod just gets the menus out of your way.

[b]There is no ten-slot hotbar, ever.[/b] The bottom of your screen is two hand boxes plus your worn gear.

This guide walks through every part of the mod in the order you'll meet it. Section 10 is a one-page controls cheat sheet if you just want the keys.

[i]New to all this? The in-game tutorial teaches it in 19 short lessons. Take the guided tour from the Welcome card, or replay any lesson from F10 > Guide.[/i]
```

## Section 2

**Section title:** Install it (2 minutes)

**Body:**
```
[b]You need:[/b]
[list]
[*][b]BepInEx 5[/b] and [b]StationeersLaunchPad[/b]. UI Ascended is a StationeersLaunchPad plugin mod, so install those first.
[*]Stationeers on the public or beta branch. One build of the mod runs on both.
[/list]

[b]Then:[/b]
[olist]
[*]Subscribe to Stationeers UI Ascended on the Workshop.
[*]Launch the game through StationeersLaunchPad.
[*]Load a world. The visor HUD replaces the vanilla panels, and [b]Stationeers Blue[/b] is the starting theme.
[/olist]

[b]Good to know[/b]
[list]
[*]Install [b]one copy only[/b]: never the Workshop version and a manual copy at the same time.
[*]After updating the mod, restart the game once so the new glass effects load.
[*]Uninstalling is just unsubscribing. The mod only hides the vanilla HUD, so it comes back as soon as the mod is gone.
[/list]
```

## Section 3

**Section title:** Your visor

**Body:**
```
[IMAGE: 02-top-bar-compass.jpg]
[b]The top bar:[/b] the time, outside pressure and temperature, a compass ribbon with your exact bearing, and the day counter.

[IMAGE: 03-two-hands-and-gear-1-6.jpg]
[b]Your two hands and your gear.[/b] LEFT HAND and RIGHT HAND sit in the middle, with the active hand highlighted. Around them are your six worn slots: helmet, glasses and suit on the left, back, uniform and belt on the right, numbered 1-6 to match the gear keys. A worn item that takes damage shows a bar under its icon. You can drag items into and out of every one of these boxes.

[IMAGE: 04-vitals-card-hologram.jpg]
[b]The vitals card:[/b] food and water, jetpack thrust and propellant, suit temperature and pressure with their targets, your speed, suit status chips, and a live hologram portrait of your character. Hover the card for exact mood and hygiene percentages and how fast they're changing.

[h3]The visor is part of your suit[/h3]
[list]
[*]Exact numbers only appear while you wear a [b]powered suit[/b]. Without one you get what your body feels: WARM, COLD, THIN AIR, HUNGRY, HURT.
[*]The visor boots up when the suit powers on, glitches as the suit battery runs low, and collapses when it dies.
[*]Robots always get the full readout.
[*]The game's real status icons (moodlets) and the body damage silhouette live on the visor too.
[/list]
```

## Section 4

**Section title:** The wheels

**Body:**
```
[IMAGE: 05-belt-wheel-closeup.jpg]
[IMAGE: 06-belt-wheel-animated.gif]

[h3]Belt Wheel (middle mouse)[/h3]
Hold middle mouse, flick toward a tool and release: it's in your hand. Tap instead and the wheel stays open so you can click; tap again to close. Every tool keeps its own wedge, and an empty slot shows a ghost of the tool that belongs there. [b]The Hub[/b] wedge opens your whole inventory. [b]Q[/b] opens the belt chooser, which swaps the belt you're wearing.

[h3]Inventory wheel (Tab)[/h3]
Tap Tab for your worn bags plus [b]Search[/b]. Click a bag to go into it and right-click to come back out. Search filters everything you carry as you type. Holding Tab still shows the vanilla scoreboard.

[h3]Device wheel (R)[/h3]
R opens the wheel for the item in your active hand: its switches, its settings and its slots. That covers everything vanilla's own window shows, like jetpack thrust and stabilizer, suit pressure, temperature, air and filters, the helmet visor and lights, and tank valves. Point at a value and scroll to change it (hold C for fine steps). Slide over a battery, canister or cartridge to TAKE or REPLACE it.

[h3]Gear keys 1-6[/h3]
Tap a number for that piece of gear's wheel, so you can swap its battery, filters or canisters without opening anything. Hold it to equip or unequip, like vanilla. The 6 key opens the full Belt Wheel.

[h3]Working the wheels[/h3]
[list]
[*]The word on the wheel tells you what a click will do: TAKE, EQUIP, SWAP, OPEN or STOW.
[*][b]Left-click[/b] acts. [b]Right-click[/b] goes back a level, or closes the wheel at the top.
[*][b]Shift[/b]-click keeps the wheel open after an action. F10 > Radial can flip this so wheels stay open by default.
[*][b]E[/b] swaps your active hand. [b]Q[/b] pages through a crowded wheel.
[*][b]Drag items out[/b] of a wheel and drop them on a charger, a locker, a bag, or a hand or gear box. Close the wheel with something still dragged out and it drops at your feet.
[*]Hold [b]Alt[/b] to free the cursor. With a wheel open, Alt-click loose items within 3 m to pull them in.
[*]You can walk and jump with a wheel open.
[*][b]Bag hotkeys:[/b] hover a bag in a wheel and press a number to bind it. Ctrl+number then opens that bag from anywhere.
[/list]
```

## Section 5

**Section title:** The Universal Inventory (B)

**Body:**
```
[IMAGE: 07-universal-inventory.jpg]

Press [b]B[/b] for one window with [b]everything you carry[/b]: every worn bag and every bag nested inside another. Tap B to toggle it, or hold B to peek and let go. Your cursor stays locked so you can keep moving; hold Alt to use the mouse.

[list]
[*][b]Drag and drop[/b] to move, swap and merge stacks. Click an item to take it to your hand, or right-click it for its wheel.
[*][b]Pin a bag[/b] into its own window by dragging its tab out. Pinned windows remember their place in each world, and the vanilla close-all key puts them away.
[*][b]Shift+drag[/b] moves every stack of that item type at once, into bags and into or out of lockers.
[*][b]Drag straight into the world:[/b] chargers, lockers and device slots, with the game's own green placement box.
[*][b]Split stacks[/b] with the small square buttons: 1, half, or a number you choose (host or single-player).
[*]Every bag has a [b]Sort[/b] button.
[*][b]Shift+1-6[/b] opens that worn bag straight in the window, or brings its pinned window forward.
[/list]
```

## Section 6

**Section title:** Smart Stow (G)

**Body:**
```
Press [b]G[/b] and the item in your hand goes where it belongs. There are two modes, set in F10 > SmartStow.

[IMAGE: 08-smartstow-return-home.jpg]
[h3]Simple mode (the default)[/h3]
[list]
[*]Every item remembers the [b]exact bag and slot[/b] you last put it in, separately in each world. G sends it back there, even into a belt inside a backpack.
[*]Move something by hand and its home moves with it. G, sorting and swapping never take an item's home.
[*]A brand-new item finds a sensible spot once, and that spot becomes its home. A short note tells you when that happens: [i]No home yet - put in X[/i] or [i]Home full - put in X[/i].
[*]While you drag an item, the bag G would pick glows.
[*]The Return Home page can forget every home in this world, or re-learn them from what you're carrying.
[/list]

[IMAGE: 09-smartstow-organizer.jpg]
[h3]Complex mode (rules)[/h3]
[list]
[*][b]Storage Layouts[/b] are sets of [b]Bag Profiles[/b]. A profile decides what a bag accepts: by item, category, slot type or the mod's own item classes, each with a priority.
[*]The Organizer shows your layouts, your bags and your profiles side by side. Drag a profile onto a layout to copy it.
[*]Four layouts ship: By Printer, By Category, Ascended and Starter.
[*]Share a layout with a code (UIAP1-F-...) or an XML file. Assigning a profile can also rename the bag to match.
[*]If you'd set up Bag Profiles before 1.0, you stay in Complex mode automatically.
[/list]

[b]In both modes[/b] Smart Stow never stows into consumables or body bags, and you can mark any bag as never stow into this.

[b]Tip:[/b] like vanilla, Smart Stow fills an empty canister socket without checking the gas type. Keep an eye on which canister you stow while your suit's air slot is empty.
```

## Section 7

**Section title:** Themes and the F9 HUD Designer

**Body:**
```
[IMAGE: 10-f10-ui-themes.jpg]
[IMAGE: 11-themes-showcase-animated.gif]

[h3]Four themes[/h3]
Open [b]F10 > UI Themes[/b] and click a card to switch. A theme restyles everything at once: the visor, the wheels, the Universal Inventory and the F10 menu itself.

[IMAGE: 12-theme-zirillian-red.jpg]
[IMAGE: 13-theme-pure-hud.jpg]
[IMAGE: 14-theme-blue-minimalist.jpg]

[h3]Make your own with F9[/h3]
[NEW SCREENSHOT NEEDED: press F9, click one HUD element so its handles show, and open its style popup on the Glass or Edges page.]

Press [b]F9[/b] to open the HUD Designer:
[list]
[*][b]Click[/b] any part of the HUD to select it, drag it to move it, and pull the handles to resize it. It snaps to a grid; hold Alt to place freely.
[*][b]Restyle it:[/b] frosted glass, glow, edge light, bloom, alert pulses and cut or rounded corners, for one element or the whole theme.
[*][b]Build:[/b] add new elements, your own text labels and icons, and even draw shapes with the pen tool.
[*][b]Suited vs bare:[/b] an element can look different, or sit somewhere else, depending on whether you're in a powered suit.
[*][b]Preview[/b] the bare, suited or robot HUD, and test the power-death and boot-up effects. In single-player, the Pause button freezes the game while you work.
[*]Ctrl+Z / Ctrl+Y undo and redo. Del deletes, and Ctrl+D duplicates.
[/list]

The shipped themes are read-only, so [b]duplicate one first[/b] and edit the copy. Themes save as XML files you can share. For the deep dive, read the illustrated [b]Designer Handbook[/b] in F10 > Guide.
```

## Section 8

**Section title:** The F10 Control Center

**Body:**
```
[IMAGE: 15-f10-controls-rebind.jpg]

Press [b]F10[/b] for every setting in one window:
[list]
[*][b]Two master switches[/b] at the top turn the radial menus or the visor HUD off on their own, if you only want one half of the mod.
[*][b]Tabs:[/b] UI Themes, Radial, HUD, SmartStow, Controls, Guide and Suggestions/Bugs.
[*][b]Search[/b] box in the title bar: type a setting's name and it jumps there and flashes it.
[*][b]Controls:[/b] rebind every UI Ascended key. F10 itself is also listed in the game's own Controls screen.
[*]Each section has [b]More options[/b] for the deeper settings.
[/list]

[IMAGE: 16-f10-radial-settings.jpg]
[b]Radial[/b] holds the wheel behaviour, colours and glass.

[IMAGE: 17-f10-guide-tab.jpg]
[b]Guide[/b] lists every tutorial lesson (watch it, try it, skip it or start over) plus the Designer Handbook.
```

## Section 9

**Section title:** Report a bug or suggest a feature

**Body:**
```
[NEW SCREENSHOT NEEDED: F10 > Suggestions/Bugs with Bug selected and the What gets sent card visible.]

You can report bugs and ideas without leaving the game:
[olist]
[*]Open [b]F10 > Suggestions/Bugs[/b].
[*]Pick [b]Bug[/b] or [b]Suggestion[/b], give it a title, and describe it. For a bug, say what you did, what you expected, and what happened instead.
[*]Optionally switch on [b]Include my HUD profile[/b] or [b]recent log lines[/b]. They're the two things we'd otherwise have to ask you for.
[*]Click [b]Send[/b]. You'll see [i]Thanks - that's now UIA-123[/i].
[/olist]

[b]Your reports[/b] shows every report you've sent and its status: Received, Under review, Fixed in a version, or Closed.

[b]What gets sent:[/b] only what you type, plus the mod version and how it's installed, the game build, your theme's name (and whether you've edited it), single-player or multiplayer and your suit tier, your screen size and your OS. The form lists it all before you send. It's [b]anonymous[/b] unless you add your Discord name. Your Steam ID and save name are never sent.

If our server can't be reached, your report is saved on your PC and sent automatically later. Nothing is lost. There's also a console version: press F3 and type [b]uiafeedback[/b] for help.
```

## Section 10

**Section title:** Controls cheat sheet

**Body:**
```
Every UI Ascended key can be rebound in F10 > Controls. These are the defaults.

[table]
[tr][th]Key[/th][th]What it does[/th][/tr]
[tr][td]Middle mouse (hold / tap)[/td][td]Belt Wheel: flick and release, or tap to keep it open[/td][/tr]
[tr][td]Tab (tap)[/td][td]Inventory wheel: your bags plus Search (hold Tab = vanilla scoreboard)[/td][/tr]
[tr][td]R[/td][td]Wheel for the item in your active hand[/td][/tr]
[tr][td]1-6 (tap / hold)[/td][td]That gear's wheel / equip or unequip[/td][/tr]
[tr][td]Shift + 1-6[/td][td]Open that bag in the Universal Inventory[/td][/tr]
[tr][td]B (tap / hold)[/td][td]Universal Inventory: toggle / peek[/td][/tr]
[tr][td]G[/td][td]Smart Stow[/td][/tr]
[tr][td]F9[/td][td]HUD Designer[/td][/tr]
[tr][td]F10[/td][td]Control Center[/td][/tr]
[tr][td]E (wheel open)[/td][td]Swap active hand[/td][/tr]
[tr][td]Q (wheel open)[/td][td]Next page, or the belt chooser on the Belt Wheel[/td][/tr]
[tr][td]Scroll (on a value)[/td][td]Adjust it; hold C for fine steps[/td][/tr]
[tr][td]Shift + click[/td][td]Keep the wheel open after an action[/td][/tr]
[tr][td]Shift + drag[/td][td]Move every stack of that item type[/td][/tr]
[tr][td]Right-click / Esc[/td][td]Back one level / close the wheel[/td][/tr]
[tr][td]Alt (hold / double-tap)[/td][td]Free the cursor / keep it free[/td][/tr]
[tr][td]Ctrl + number[/td][td]Open a bag you bound to that number[/td][/tr]
[tr][td]F3[/td][td]Game console (for the uia commands)[/td][/tr]
[/table]
```

## Section 11

**Section title:** Multiplayer, resets and troubleshooting

**Body:**
```
[h3]Multiplayer[/h3]
Every inventory action goes through the game's own server-checked path, the same way vanilla does it. A few extras are host or single-player only: choosing a number when splitting, some sort orders, and the pause buttons. Readings that only the server knows show [b]--[/b] on clients instead of a guess.

[h3]Starting fresh[/h3]
[list]
[*][b]Just the themes:[/b] F10 > HUD > Maintenance > Restore shipped themes. Your own themes aren't touched.
[*][b]Missing settings folders:[/b] F10 > HUD > Maintenance > Repair config folders. Nothing is deleted.
[*][b]Everything:[/b] press F3, type [b]uiareset[/b] to see what would be deleted, then [b]uiareset confirm[/b] and restart the game.
[/list]

[h3]If something looks wrong[/h3]
[list]
[*]Make sure only one copy of the mod is installed.
[*]After an update, restart the game once.
[*]F9 is also vanilla's creative-mode spawn key. UI Ascended handles both, and you can rebind the designer key in F10 > Controls if you prefer.
[*]Still stuck? Send a report from F10 > Suggestions/Bugs with log lines switched on.
[/list]

[h3]Handy extras[/h3]
Press F3 and type [b]finddead[/b] to locate a lost body bag, or [b]findlargebox[/b] to find large boxes near you.
```

## Section 12

**Section title:** Credits and links

**Body:**
```
Stationeers UI Ascended is made by [b]FlorpyDorp[/b].

[h3]Thank you[/h3]
To everyone who play-tested and reported bugs on our Discord: [b]aproposmath, ConductorWon, Dipole, JoeDiertay, Ningy, PeteWasEre, StormCircuit, ViroMan, Wilhelm W. Walrus, Windsinger and WreckerRecreation[/b]. This release is built on your reports.

[b]Special thanks to JacksonTheMaster (JXSN)[/b], who helped build UI Ascended and wrote Profilicus Universalis, the profiler built into the mod.

[list]
[*]Workshop page: https://steamcommunity.com/sharedfiles/filedetails/?id=3776545141
[*]Bugs and ideas: F10 > Suggestions/Bugs in game
[/list]

Thanks for playing, and tell us what you'd like next!
```
