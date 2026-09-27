# 0.9.8.0 SmartStow overhaul — SIMPLE play-test (under 5 minutes)

*One single-player save, any world. Each step says what you should SEE. If a step fails, note
the step number and move on — the full list is `Play-Test-FULL.md`. Console helpers if
something looks odd: `stowhomes` (what Smart Stow remembers) and `stowprofiles` (layouts).*

**Setup (once):** F6 hot-reload (or restart). Carry a backpack with a few items in it.

## 1. The new F10 (~30s)

- [ ] Press F10. The window is **bigger**, the tabs look like **folder tabs** (selected tab
      merges into the panel), corners are sharper.
- [ ] The tab row reads: UI Themes, Radial, HUD, **SmartStow**, Controls, Guide. No
      Simple/Advanced buttons in the title bar; there's a **search box** — type "glow" and
      click a result: it jumps there and flashes the row.

## 2. Simple mode: things go back where they came from (~90s)

- [ ] SmartStow tab: the mode bar shows **Simple** selected, with an on/off switch and one-line
      explanations. (A one-time toast about the new mode may appear in-world.)
- [ ] In world: take an item OUT of your backpack into your hand, walk away, press **G**
      → it returns to the SAME bag and slot.
- [ ] Drag that item into a DIFFERENT bag by hand. Take it into your hand, press **G**
      → it goes to the NEW spot (your drag moved its home).
- [ ] Mine/craft a NEW item, press **G** → it lands somewhere sensible and (if the note toggle
      is on) a small note says where. Press G on a second one → SAME place.
- [ ] With the inventory open, drag any item around → the bag G would choose **glows**.

## 3. Complex mode: Storage Layouts (~2 min)

- [ ] Flip the mode bar to **Complex** → sub-tabs change to Organizer / Routing / Universal
      Inventory. The Organizer shows THREE columns: Storage Layouts, Bags, Bag Profiles.
- [ ] Click the **By Category** card → its Bag Profiles appear on the right with coloured
      badges, and a banner says it is **not active**. It did NOT activate (the ACTIVE chip
      stayed put). Press **Use** → it becomes active.
- [ ] Bags column: your backpack shows with its **picture**. Pick a profile in its dropdown
      → the bag gets **renamed** to match (labeller). Hold a matching item, press **G**
      → it goes into that bag.
- [ ] Click a profile row → the EDITING band below shows its four rule groups. Open one,
      change a Priority, add a rule with **+ Add rule...** — the row count updates.
- [ ] Kebab (three-dot) menu on a bag → **Never stow into this** → the card gets a warning
      outline + NO STOW tag; G now refuses that bag.
- [ ] SHARE bar: **Export Layout** → status line shows a fingerprint and "copied". Press
      **Import** (leave the box empty) → a NEW layout appears, not active, nothing overwritten.

## 4. Nothing lost (~30s)

- [ ] Flip back to **Simple**, then **Complex** again → your layouts, mappings and the NO STOW
      flag are all still there (Simple hides the system, never deletes it).
- [ ] Type into any rename box and press **Esc** → the edit cancels but **F10 stays open**.
- [ ] UI Themes tab: pick a different theme → the whole menu (folder tabs, badges, glow)
      recolours to match.

**Done.** If all boxes tick, the core overhaul works. Anything odd: screenshot + the step
number + (if stow-related) paste the `stowhomes` output.
