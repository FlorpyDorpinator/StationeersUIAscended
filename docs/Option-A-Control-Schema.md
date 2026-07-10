# Option A Control Schema

*Stationeers UI Ascended 0.3.0 Alpha — 2026-07-09*

The F10 menu now has a **Control schema** selector at the top:

| Schema | What it is |
|---|---|
| **Option A** (default) | The new interaction model described in this document. |
| **Option D** | The classic pre-0.3.0 behavior — swap lists, click-executes-on-press, held-item stow previews. Kept for A/B comparison; switch back any time, no restart needed. |

The schema switches **behavior**. Visuals (colors, borders, font, sizes, shading) are
separate config entries with Option-A-flavored defaults, all tunable live in the
**radial editor** — so you can run Option D behavior with the new look, or vice versa.

---

## The visual pass

- **Full wedge outlines.** Borders now run along the straight *side* edges of every
  wedge, not just the arcs, with a small angular gap (default 1.2°) between wedges so
  each outline reads as its own line. Side edges are anti-aliased (they never were
  before — that was the jaggedness). Everything is mesh-baked: overlay canvases get no
  MSAA, so the ~1px color ramp *is* the anti-aliasing.
- **Borders are always on** and shift color on hover (`WedgeBorder` →
  `WedgeBorderSelected` in the palette).
- **Bold game font.** All radial text now uses **RBNoBold** — the game's only true
  bold TMP face (RBNo3.1 family; the scoreboard/leaderboard title font). Wedge labels
  render ALL CAPS by default (`UppercaseLabels`). Pick any other game font from the
  dropdown in the editor.
- **Jackson's dim shading is now tunable**: `DimShading` on/off + `DimStrength` 0–1
  (non-highlighted wedges recede while one is highlighted).
- **Live state under icons.** Anything with a state shows it permanently under its
  icon: battery **87%**, canister **5300kPa**, stacks **x25**, filter wear, dirt
  canister fill. Open a bag and you instantly see *which* battery is full. This uses
  the game's own `GetQuantityText()` readout funnel, so it is multiplayer-client-safe
  by construction (the one trap — `BatteryCell.PowerRatio` is server-only — is avoided;
  the synced percentage byte is what renders).

## STOW wedges

Empty slots no longer preview the held item. They show the **blank slot icon + "STOW"**.
Hovering the wedge swaps in the held item's icon on the orange "goes here" fill, so the
preview happens exactly when you're aiming at it.

## Child radials (satellites)

- **The readout text moves into the child radial.** While a satellite is open, the
  parent hub goes quiet and the hovered-entry readout renders in the middle of the
  child ring.
- **Devices**: slide outward over a tool/device and its controls appear as a child
  radial — the on/off wedge is labeled with the **current state** ("ON" / "OFF"),
  battery slots show charge, modes show the current mode.
- **Scroll-adjustable values**: suit pressure, suit AC temperature, jetpack thrust
  render as `NAME / ▲ / value / ▼` wedges. **Mouse wheel** nudges the value — one
  notch is exactly one vanilla button press (`Interactable.PlayerInteractWith()`, one
  network message, clamped server-side). Clicking the wedge nudges up.
- **Big devices** (hardsuit body): the child radial offers **SETTINGS** and **SLOTS**,
  and becomes whichever you pick.
- **Nested bags**: slide out for **STOW** (held item into the bag) / **TAKE** (the bag
  to your hand). **Clicking** a nested bag opens it — the bag becomes the main radial
  and right-click backtracks. This works in the Tab radial and the backpack hotkey (4)
  radial alike.
- **The swap-with-everything satellite is gone** in Option A. Slide out on an item and
  you get **TAKE / OPEN**; open makes it the main radial, right-click backs out.
  (Option D still has the old swap lists.)

## Click model, auto-close, dismissal

- Actions run on **mouse-up** (press-and-hold is how drags start — see parking).
- **One action → the radial closes.** Stow something, grab something — the radial gets
  out of your way. (Parking suspends this: see below.)
- **Middle-mouse tap dismisses** any sticky radial that isn't the toolbelt's own.
- Tab / MMB / 1–6 all share this model.

## Search all bags

The Tab radial's **SEARCH** wedge transforms the radial into a panel: search box on
top, live-filtered results below (name, icon, count, location, state). Click a result —
it lands in a free hand, or **drops at your feet** when both hands are full. Enter takes
the top result. Escape / right-click returns to the radial.

Typing is captured raw (`Input.inputString`) rather than through a focusable text field:
our modal already holds the game's keys, and raw capture can't lose focus while UGUI
clicks are blocked.

## Drag-out parking (the multi-transfer feature)

1. **Press and hold** (≥0.25s, or just drag) on any item wedge → the item tears off and
   follows your cursor. The radial **locks open**.
2. **Release over open screen** → the item parks there in a small blue-shaded circle.
   Park up to 12.
3. Keep navigating radials (open other bags, other levels) with items parked.
4. **Drag a parked chip onto any wedge that can take it** (STOW wedges, bags) → the
   item moves there. The wedge glows orange when the drop would fit.
5. **Right-click out of the radial entirely** → every parked item **drops on the
   ground** at your feet. **Escape cancels instead** — nothing moves, because parked
   items never actually left their slots.

Parking is client-side visual state. An item on your screen is still physically in its
slot; the one network mutation happens at the drop, verified against the pinned
occupant at execute time — a chip whose item someone else moved simply evaporates.

> Design note: the RMB-out dump sends one drop message per chip. This is the mod's
> single sanctioned deviation from "one user action = one message" — each chip was
> parked by a deliberate act, the dump is a deliberate exit, and it is capped at 12.

## UIA sorting classes

Crowded bags group by the mod's **own 22-class taxonomy** instead of the game's 11
coarse `SortingClass`es (where most things are "Default"): Storage, Tools, Devices,
Power Cells, Canisters, Tanks, Filters, Ores, Ingots, Materials, Kits, Electronics,
Food, Ingredients, Seeds, Medical, Clothing, Suit Parts, Weapons, Consumables, Decor,
Misc. Nested backpacks are **Storage**, not Clothing.

The table (`Core/UIASortingData.g.cs`, 785 vanilla items) was generated from the game's
own Stationpedia export by a classify-and-audit agent pipeline; unknown/modded items
fall back to class-based heuristics (`UIASort.Fallback`). The table is data — future
versions can let players ship their own.

## The radial editor

**F10 → "Open radial editor"**: the screen goes black and a live example main radial +
child radial appear, with a wedge in every state — normal, hovered (follow your mouse),
disabled, STOW, branch, and a scroll-value wedge you can actually scroll. The F10 window
becomes the control panel:

- every palette color (hue-wheel pickers with transparency),
- font dropdown (every TMP font the game has loaded),
- radial / hub / child radial sizes, icon size,
- border thickness, side borders, wedge gap, edge softness (anti-aliasing),
- dim shading + strength, ALL CAPS, state text, name labels.

Everything renders through the real renderer, so what you see is exactly what the game
shows. **Exit** button or Escape leaves the editor.

---

## For testers

- F10 → check the schema selector says **Option A**.
- Middle-mouse hold → toolbelt: empty slots should read STOW and preview your held tool
  on hover in orange.
- Tab → SEARCH → type two letters → click a result with both hands full → it should
  drop at your feet, never vanish.
- Tab → open backpack → press-hold-drag an item off the ring → park it → drag it into
  another bag's STOW wedge.
- Park two items → right-click out → both drop at your feet.
- Slide out on your suit (3) → SETTINGS → scroll on TEMPERATURE — watch the value step
  by 1K per notch (server round-trip on a client: slight delay is normal).
- Switch to Option D in F10 → verify the classic behavior returns without a reload.
