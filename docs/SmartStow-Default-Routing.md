# Smart Stow — Default Routing (no profiles)

*2026-07-21. How Smart Stow (G) decides where a held item goes **when you have set up no
profiles at all** — the zero-setup tier. Written from the decompiled game behaviour (build
27701) + the mod's current chain, as the spec for the defaults rework. This is the
"draw up an MD of the defaults" deliverable; it also drives the stow bug fixes (#11/#13/#14).*

---

## 1. What vanilla actually does (the baseline we inherit)

Vanilla `InventoryManager.SmartStow` routes a held item by **`Slot.Class` type + emptiness +
worn-container order** — nothing else. In priority:

1. A free **direct worn slot** on your body whose type matches the item (or a `None` slot).
2. The **slot it originally came from**.
3. A free slot in an **open inventory window**.
4. The **first worn container** (in body-slot order) that has a free matching slot — this is
   the nested step that reaches suit/jetpack/backpack/belt interiors.
5. Fail sound.

A slot accepts an item iff it is **empty** and (`slot.Type == item.SlotType` **or**
`slot.Type == None`). Two facts fall out of this that explain most of your complaints:

- **Vanilla never inspects the gas inside a canister.** There is zero gas-type / atmosphere
  check anywhere. A nitrogen or volatiles canister will drop into your suit's breathing
  **air tank** just as happily as oxygen, whichever empty `GasCanister` slot is reached
  first. (Answer to your #11 question: it does **not** check — so "do what vanilla does"
  means no gas check. See §5 for the safety caveat.)
- **The generic backpack is the uncontrolled catch-all.** `ItemHardBackpack` is 20 slots of
  type `None`, which accept *literally anything*. In vanilla's step 4 it is reached by
  body-slot order, so a wall, a canister, or a battery can all be swallowed by whatever
  generic backpack comes first — regardless of what else you meant that bag for.

## 2. The taxonomy we have to work with

Item categorization in Stationeers is **very coarse**: `SortingClass` has only **11 values**
— `Default, Kits, Tools, Resources, Food, Clothing, Appliances, Atmospherics, Storage, Ores,
Ices` — hard-authored one-per-prefab. `Slot.Class` (what physical slot an item fits) has 44
values and is much finer.

The traps that make `SortingClass`-alone routing wrong:

| Item | SortingClass says | Slot.Class (truth) | Problem |
|---|---|---|---|
| Battery cell | **Default** | Battery | Category can't see it; "Default" is a junk bucket |
| Fertilizer | **Default** | Plant | same |
| Access card / empty canister | **Default** | AccessCard / GasCanister | "Default" hoovers unrelated junk |
| Gas filter | **Resources** | GasFilter | lumped with cable + ingots |
| **Cable coil** | Resources | **Tool** ⚠ | the game thinks a cable coil is a *tool* — it gets yanked onto your tool belt |
| Ingot | Resources | Ingot | mixed into the Resources bucket |
| Wall / frame / all kits | **Kits** | None | one bucket for every kit; `None` slot means any generic bag eats it |

**Rule that falls out: never route by `SortingClass` alone.** `Default` must never be a bag
category (it's the junk drawer), and several important items (batteries, fertilizer, filters,
cable) must be caught by **`Slot.Class` or explicit prefab**, not category.

## 3. Diagnosis of your two live misroutes

**Walls → "electrical" backpack.** There is no electrical-backpack prefab; it's a generic
`ItemHardBackpack` you labelled Electrical. Walls are `SortingClass=Kits, SlotType=None`. The
Electrical profile catches cable/battery/circuit — **not** walls, so it didn't attract them.
What actually happened: *every mod stage declined* (no tool match, no stack, the Electrical
bag is profile-owned so affinity skips it, and your walls bag was likely full or had no free
slot), so control fell through to **vanilla**, which dropped the `None`-typed wall into the
first generic backpack by body-slot order. **The bug is uncontrolled vanilla fall-through
into a generic bag**, not a bad category.

**Canister → backpack with the jetpack slot open.** Same mechanism: your jetpack/suit
`GasCanister` slot is one level deep, but the generic backpack's `None` slots also accept the
canister and were reached first in body-slot order. Vanilla has no notion of "a canister
belongs in a canister slot" — it just takes the first empty slot that fits, and `None` fits
everything.

## 4. The default routing we should ship (the new zero-setup chain)

Ordered stages, each gated at execute time, one message per placement. **Bold = new/changed.**

1. **Tool → its bound home slot on the worn tool belt** (unchanged; the Belt Wheel feature).
   A tool with a remembered home flies home.
2. **Stack top-up, with continuation** *(fixes #13)*. Merge into a matching partial stack;
   **if the held quantity still has a remainder after the stack fills, continue placing the
   remainder into the same bag automatically** (next partial stack of the same item, then a
   free slot in that bag) so one G press fully stows the hand.
3. **Functional-socket priority** *(fixes #11)*. If the item has a specific component slot
   type — **`GasCanister`, `Battery`, `GasFilter`, `Cartridge`, `DataDisk`, ...** — prefer an
   empty slot **of that exact type** (suit air/waste tank, jetpack propellant, suit battery,
   suit filter) over any generic `None` slot, anywhere reachable. This is the "canisters
   always go to canister slots, batteries to battery slots" rule. **No gas-type inspection**
   (matches vanilla — see §5 caveat).
4. **Explicit bag profile** (unchanged; only if you've set one up — but this doc is about the
   no-profile case, so usually skipped).
5. **Content affinity** (unchanged): route to a profile-less bag already holding similar
   items — same prefab (strong), same `Slot.Class` (medium), same `SortingClass` (weak).
6. **Bag-type default** *(expanded, see the table)*: an empty/unprofiled bag of a known type
   gets an implicit category so it works fresh from the printer.
7. **Type memory** (unchanged): where this item type last went.
8. **Deterministic generic fallback** *(new — replaces uncontrolled vanilla fall-through for
   the walls case)*: place `None`-typed items into a generic bag by a **stable, sensible
   rule** (e.g. the generic bag that already holds the most of that `SortingClass`, else the
   emptiest generic bag) rather than by raw body-slot iteration order.
9. **Tool-belt empty slots — LAST resort** *(fixes #14)*. An un-homed tool (or a false-tool
   like cable coil) only lands in a bare tool-belt slot when nothing above placed it, so the
   belt stops being the greedy first destination.
10. Vanilla SmartStow (final safety net only).

### Bag-type default table (prefab → implicit category)

Applies when a bag has no profile. Verified prefab types from the decompile:

| Bag prefab | Implicit category | Caught by |
|---|---|---|
| `ItemToolBelt` (8× Tool) | Tools | Slot.Class Tool (but see stage 9 — last resort for un-homed) |
| `ItemMiningBelt` / `MKII` (Ore + Tool) | Ores | Slot.Class Ore |
| `ItemMiningBackPack` / `ItemHardMiningBackPack` (Ore) | Ores | Slot.Class Ore |
| `ItemHardBackpack` (20× None) | *generic* — no implicit category | stage 8 deterministic fallback |
| Gas-canister / battery cases (typed slots) | by slot type | stage 3 socket priority |

### Category → destination map (for a "recommended profiles" one-click, and as affinity hints)

| Bag label | Catches (by, in priority) |
|---|---|
| **Tools** | Slot.Class `Tool` — **excluding cable coil** (route cable to Electrical) |
| **Ores** | Slot.Class `Ore`; SortingClass `Ores` |
| **Ices** | SortingClass `Ices` |
| **Construction** | SortingClass `Kits` (walls/frames/kits); SortingClass `Resources` (weak) |
| **Atmospherics** | Slot.Class `GasFilter`, `GasCanister`, `LiquidCanister`; SortingClass `Atmospherics` |
| **Electrical** | prefab `ItemCableCoil`/`Heavy`; Slot.Class `Battery`, `Circuitboard`, `Motherboard`, `ProgrammableChip`, `Circuit`, `DataDisk` |
| **Farming** | Slot.Class `Plant`, `Egg`, `Bottle`; prefab `Fertilizer`; SortingClass `Food` (weak) |
| **Food** | SortingClass `Food`; Slot.Class `Bottle` |
| **Clothing** | SortingClass `Clothing`; Slot.Class `Uniform`, `Glasses`, `Suit`, `Helmet` |
| **Ingots/Metals** (optional) | Slot.Class `Ingot` |

Deliberately **no "Default" bag** — that `SortingClass` is a junk bucket and would swallow
batteries, fertilizer, cards, empty canisters.

## 5. The one judgement call for you (#11 gas awareness)

Vanilla does **not** check what gas is inside a canister before choosing a slot, so the
"socket priority" rule (stage 3) will, like vanilla, put *any* canister into the first empty
`GasCanister` slot — which could mean a **volatiles or pollutant canister landing in your
suit's breathing air tank.** You said "do whatever vanilla does," so the plan above does no
gas check.

If you'd rather be safer than vanilla, we can make stage 3 **gas-aware** at low cost: route a
canister to the suit **air tank** only if it reads as breathable (oxygen/atmosphere), send
**anything else** (volatiles, nitrogen, pollutants, CO2) to the **jetpack propellant** slot
or the **waste tank**, and never auto-place an unknown gas into the breathing slot. This
reads only client-safe canister/atmosphere state. **Default in the plan: vanilla behaviour
(no gas check).** Say the word if you want the gas-aware version instead — I'd lean toward it
for safety, but it's your call.

## 6. What this fixes, mapped to your list

- **#11** — stage 3 makes canisters/batteries prefer their real sockets over a generic bag.
- **#13** — stage 2 continues the stack remainder into the same bag in one G press.
- **#14** — stage 9 makes bare tool-belt slots the last resort, not the first.
- **#12** — stages 6/8 + the tables give smarter, deterministic defaults and kill the
  uncontrolled vanilla fall-through that sent your walls into the wrong bag.
