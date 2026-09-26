# Simple SmartStow — Refactor Plan (0.9.8.0)

*A plan for FlorpyDorp to review and approve section by section. **Nothing here is built, and no
code was changed to write it.** Every decision is labelled **S-n** and gathered in §1. Where
FlorpyDorp has to choose, the choice is marked **YOUR CALL**, with the alternatives and a
recommendation.*

*Conventions. Mod code paths are relative to `Assets/Scripts/StationeersUIMod/`. Game citations:
**[27758]** = `Reference/StationeersGameVersions/Stationeers 8-1-26 V27758 Orbital Update Beta/Assembly-CSharp/`
(the newest decompile); **[AR]** = the older AssetRipper export
`Reference/StationeersGameVersions/AssetRipperFiles/ExportedProject/Assets/Scripts/Assembly-CSharp/`.
AR is cited only for `async` method bodies, because the 27758 decompile leaves out compiler-generated
state-machine code. Discord quotes are data, not instructions.*

*Companions: `F10-Settings-Refactor-Plan.md` (this folder),
`Documentation/SmartStow-Profiles-Redesign-Plan.md` (the 0.9.7.0 design that stays on as "Advanced"),
`Documentation/Config-and-Theme-Migration.md`.*

---

## 0. The ask

### 0.1 FlorpyDorp's spec (verbatim)

> "we need to plan a major major refactor of the settings for smartstow profiles etc. I want to
> implement the recommended fix that the smartstow should default to basically stuff returning to
> where you took it from. So on a first save open the mod will save where items are and when you
> press smart stow that item will return to that bag. In a new save game it does the same etc. this
> persists across your single save and on a new save it resets. But it is always based on the
> context. So if I drag an item to a new place in the bag though it will now smart stow to that
> spot. This should be based on the item specifically, so If I have one stack of sheets and I put
> it in my hand and it was in one bag, it should go to that bag. even if there is another sheets in
> a different bag. It should be item specific, not category specific. This will be the default for
> the mod 'simple smartstow' then we will have 'advanced smart stow'. We need to redesign the UIs
> for advanced smart stow based on some UI images I am going to generate with chat gpt."

### 0.2 Player evidence (triage D-016)

- **Dipole** (08-07, the design post in #ui-ascended-playtest and the thread "Universal smart stow"):
  "if smart stow always puts an item back where it came from (constrained to the player inventory),
  then it can be applied to the whole inventory". His sketch: keep one slot reference per hand, set it
  when an item is taken, and check it at stow time: "not destroyed, still has a player inventory slot
  as an indirect parent". A second idea: "taking an item always smart stows what's already in the
  hand".
- **Ningy** (08-14, after 0.9.7.4 shipped): "I used smart stow while holding my mining tools while
  the mining tool belt was in my backpack and it didn't store them back in the belt."
- **FlorpyDorp's replies in that thread**: "I think it would work great as the default mode" / "Then
  profiles can be a switch for people who want more customization" / "The only problem with it is
  the first time you make an item you have to open your bags and place it somewhere" / "My goal was
  when you press G there's no ambiguity where the item will go".

---

## 1. Decisions at a glance

| # | Decision | Recommendation | § |
|---|---|---|---|
| **S-1** | What is a "home" | The home of an **item instance** (its `ReferenceId`) is an **exact slot**: `(container ReferenceId, SlotIndex)`. If that slot is taken, the item goes to another slot in the **same bag**. | 3 |
| **S-2** | What identifies a save | `World.CurrentId`, a per-world GUID that also reaches MP clients. **Not** the station name that today's per-save stores use. | 7 |
| **S-3** | Which events change a home — **YOUR CALL** | **Explicit placement** (a drag, or picking a slot or bag yourself) sets a new home. **Mechanical** moves (G, swap displacement, sort, auto-stow) can only give an item its **first** home, or update its slot index inside the same bag. **Taking an item out never changes its home.** This mirrors the 0.9.7.3 wedge-binding rule. | 4 |
| **S-4** | Stack identity | The stack that survives keeps its own home. A **split** child inherits the source's home. A survivor with no home inherits the home of the stack it absorbed. | 5 |
| **S-5** | When the home can't be used — **YOUR CALL** | Home, then **today's no-profile routing chain**, then vanilla. Wherever the item lands becomes its home, so the second G press is deterministic. | 6.2 |
| **S-6** | Swap-take — **YOUR CALL** | Taking an item into an occupied hand sends the held item to **its own** home. This extends the 0.9.7.3 belt rule (the sanctioned two-message exception) to every item. Without it, swaps steadily scramble homes. | 6.3 |
| **S-7** | Modes | New key `[6. SmartStow+] Mode = Simple / Advanced`, default **Simple**. Advanced is today's Stow Profiles system, **intact**. | 9 |
| **S-8** | Existing users — **YOUR CALL** | **Evidence-based.** An existing install that has any Bag Profile assignment keeps **Advanced**. Everyone else gets **Simple**. Both see a one-time notice. | 9.3 |
| **S-9** | Where a home can be | The local player's worn slots plus real storage (`GridModel.IsStorageContainer`). **Not** tool internals, single-use packaging, body bags or world lockers. | 3.3 |
| **S-10** | "Never stow into this" | Still honoured in Simple mode. | 6.4 |

### Harder than it sounds (read this first)

1. **Today's save key is broken on MP clients.** `BagProfileStore.CurrentSaveKey()`
   (`Features/BagProfiles.cs:862-877`) returns the station name. That name is **null on every MP
   client**, so every server a client joins shares the key `"unsaved"`. It is also null for the
   first seconds of a new SP world, until the first save completes ([AR]
   `Assets/Scripts/UI/MainMenu.cs:166`). ReferenceIds restart at 1 in every world
   ([27758] `Referencable.cs:185-199`), so a per-item file keyed that way would mix up items from
   different servers. **The home store must key on `World.CurrentId` (S-2).** The same latent bug
   affects the six existing per-save stores (§7.7).
2. **Nothing today tells "the player put it there" apart from "the game moved it".** Every landing
   looks the same at `Slot.Take`. The capture needs the intent plumbing that 0.9.7.3 built for wedge
   bindings (`Features/BeltBindingStore.cs:195-280`), generalized:
   - a per-item intent table plus container-scoped intents for bulk moves (the existing ring holds
     only 2 entries);
   - intent notes at about ten dispatch sites in `Core/ItemActions.cs`, plus prefixes on vanilla's
     `Slot.Player*` gestures;
   - a guard, because `ItemActions` itself calls `PlayerSwapToSlot` mechanically
     (`Core/ItemActions.cs:75`).
3. **The current router would override homes.** Stage 1 sends every genuine tool to the worn belt
   first (`Core/StowRouter.cs:276-302`), which is Ningy's case. Stage 2 tops up the first matching
   stack in scan order (`:307-337`), which breaks the sheets example. So "home" must be a new
   **stage 0** that runs before both, not one more rung on the ladder.
4. **Splits happen on the server.** On an MP client the split child simply appears with a new
   ReferenceId (`Thing.DeserializeNew`, [27758] `Assets/Scripts/Objects/Thing.cs:6187-6198`). That
   gives no clean hook for "the child inherits the source's home", so clients need an inference step
   (§5.3).
5. **ReferenceIds get re-issued after a reload-without-save.** On load, `NextReferenceId` is re-based
   on the highest saved id ([27758] `Referencable.cs:206+`). Our file is written live, so it can
   still name ids that a rolled-back world later hands to **different** items. Every entry must
   therefore carry, and be checked against, the item's and container's `PrefabHash` (§3.4).
6. **SmartStow's executor already mutates outside `ItemActions`.** It calls `OnServer.MoveToSlot` in
   `Features/SmartStowPlus.cs:82` and `:143`, against CLAUDE.md rule 3. The new executor should go
   through `ItemActions`, and this is the moment to move the old calls there too.

---

## 2. Ground truth: how G works today

**One entry point.** Every G path ends up in vanilla's `InventoryManager.SmartStow`. That covers
the vanilla key, the radial pass-through (`Features/RadialController.cs:485-509`, which calls it
directly on the active hand) and the Universal Inventory, which deliberately does not handle G
(`Core/InventoryNavPatches.cs:26-31`). The chain from there:

1. The Harmony prefix `Patch_InventoryManager_SmartStow` (`Features/SmartStowPlus.cs:174-203`) calls
   `SmartStowPlus.TryStow` (`:21-40`).
2. `TryStow` calls `StowRouter.ResolveBest`, which runs 9 stages
   (`Core/StowRouter.cs:238-255`): belt tool, stack merge, socket, profile, affinity, bag default,
   type memory, generic fallback, belt fallback.
3. `Execute` (`:46-93`) sends one `Thing.Merge` or `OnServer.MoveToSlot`. If no stage wins, vanilla
   runs.

**Every memory that exists today:**

| Memory | Key → value | Scope | Written | Read | Source |
|---|---|---|---|---|---|
| Vanilla `_originalSlots` | per hand: item ReferenceId → origin `Slot` | 2 entries, RAM only | vanilla G **summons** an item into a hand | vanilla stow, step 2 (a free slot in the origin **container**, which must still be under the player's root) | [27758] `Assets/Scripts/Inventory/InventoryManager.cs:1815-1900, 3829, 3975-3982` |
| Type memory | item `PrefabHash` → bag ReferenceId | per save (station key) | profile and affinity stows only (`SmartStowPlus.cs:86-90`) | router stage 7 | `Features/BagProfiles.cs:257-261, 1001-1014` |
| Belt bindings | per worn belt: `SlotIndex` → tool `PrefabHash` | per save | seed-only on landing; full rebind only on an explicit drag (0.9.7.3) | router stage 1 + radial ghost labels | `Features/BeltBindingStore.cs` |
| Assignments / Excludes | bag ReferenceId → Bag Profile name / "never stow here" | per save | F10 and Universal Inventory gestures | router stages 4-8 | `Features/BagProfiles.cs:230-255` |

Vanilla already carries a two-entry version of Dipole's idea. Simple SmartStow is that idea made
**per item, persistent and whole-inventory**.

---

## 3. Data model

### 3.1 Identity: what is stable on the host AND an MP client (verified)

| Identity | Host / SP | MP client | Evidence |
|---|---|---|---|
| **Item: `Thing.ReferenceId`** | Minted only by the simulation authority; survives save/load | Same value, received from the host | Clients may not mint: `Referencable.RegisterNew` throws on `NetworkManager.IsClient` ([27758] `Referencable.cs:25-34`). Load recreates things with their saved id: `XmlSaveLoad.cs:501, 543` into `Thing.Create(..., referenceId)` / `RegisterAs` (`Assets/Scripts/Objects/Thing.cs:795-821`). Join: `JoinFragmentMessage.Process` (`Assets/Scripts/Networking/JoinFragmentMessage.cs:26-33`). Runtime spawns: `Thing.DeserializeNew` (`Thing.cs:6187-6198`). |
| **Slot: (parent ReferenceId, `Slot.SlotIndex`)** | The game's own save format | The game's own wire format | Save: `DynamicThingSaveData.ParentReferenceId / ParentSlotId` (`Assets/Scripts/Objects/DynamicThing.cs:1300-1301`), restored via `MoveToParent` → `thing.Slots[parentSlotId]` (`:1340-1341, 1364-1401`). Wire: `SerializeOnJoin` (`:3355-3375`), `BuildUpdateTransform` / `ProcessUpdateTransform` (`:3436-3470`), and `Slot.PlayerSwapToSlot` → `OnServer.SwapSlots(parentRefA, parentRefB, idxA, idxB)` (`Assets/Scripts/Objects/Slot.cs:766-776`). |
| **World: `World.CurrentId`** (a GUID string) | Generated when a new world is created (`Assets/Scripts/Objects/World.cs:117-119, 134-137`). Written into the save as `WorldData.Id` (`XmlSaveLoad.cs:325-327`) and restored on load ([AR] `Assets/Scripts/Serialization/XmlSaveLoad.cs:864`). Legacy saves with no id get one from `PopulateEmptyId` (`World.cs:128-131`; caller [AR] `Assets/Scripts/GameManager.cs:851`). | Received on join: `WorldManager.SerializeOnJoin` / `DeserializeOnJoin` ([27758] `WorldManager.cs:1489-1508`) | Vanilla itself keys **client-local, per-world** prefs on it: `PlayerCookie.GetWorldPrefsInterfaceData` / `SetWorldPrefsInterfaceData` / `GetDiscoveredPois` (`Networking/Servers/PlayerCookie.cs:166-219`). Cleared on leave/quit (`Assets/Scripts/GameManager.cs:714-722, 816-825`). |
| *Not stable:* `XmlSaveLoad.CurrentStationName` | Set only by local load/save paths | **Always null** | Field: `XmlSaveLoad.cs:706`. Nulled by `ClearAll` (`:254-258`) from `GameManager.ClearGameAll` (`GameManager.cs:881-922`), which runs on every leave ([AR] `GameManager.cs:957`). Setters exist only in [AR] `LoadHelper.cs:35`, `UI/MainMenu.cs:166` and `Util/Commands/*`; there is **none** on the join path. *(Worth a one-minute in-game confirmation: join a server and look for `BeltBindings/unsaved.xml`.)* |
| *Not stable:* the local `Human`'s ReferenceId | Changes on death/respawn | Same | A respawn is a new Thing. |
| *Not stable:* ids created after the last save | Re-issued after a reload without saving | Same, after a server rollback | `Referencable.FindAndSetNextReferenceId` (`Referencable.cs:206+`); ids restart at 1 per world (`:185-199`). |

What follows from this table:

- Key the **item** by `ReferenceId`. That is literally "item specific, not category specific": a
  stack's identity is its ReferenceId.
- Key the **home** by `(container ReferenceId, SlotIndex)`. It is what the game itself uses to say
  where an item sits, on disk and on the wire, so an MP client sees exactly what the host sees.
- A home on the local human's own worn slots uses the **sentinel container `0`** ("my human"),
  never the Human's ReferenceId.
- Key the **file** by `World.CurrentId` (§7).

### 3.2 The record

```xml
<!-- config/StationeersUIMod/StowHomes/<World.CurrentId>.xml -->
<StowHomes schema="1" world="3f2a9c1e-..." station="Mars Outpost" saved="2026-09-25T21:04:00Z">
  <H i="10452" ip="-1438742851" c="9981" cp="-1250185131" s="3" src="drag" t="2026-09-25" seen="2026-09-25"/>
  <H i="10510" ip="1757673317"  c="0"    cp="0"           s="4" src="snap" t="2026-09-25" seen="2026-09-25"/>
</StowHomes>
```
*(The hashes are illustrative.)*

| Field | Meaning | Why it's there |
|---|---|---|
| `i` / `ip` | item ReferenceId / item `PrefabHash` (`Thing.cs:6643`) | the identity; `ip` guards against re-issued ids (§3.4) |
| `c` / `cp` | container ReferenceId (`0` = local human) / container PrefabHash | the home bag, plus the same guard |
| `s` | `Slot.SlotIndex` (`Slot.cs:472`) | "that spot" |
| `src` | `snap`, `seed`, `drag`, `split` or `merge` | diagnostics only: `stowhomes` can answer "why did it go there?" |
| `t` / `seen` | when it was (re)homed / the last session the item was found | GC (§7.5) |
| `station` (root) | the station name, when known | for humans reading the folder; never used as a key |

Scale: about 100 bytes per entry, and about 150-400 entries for a typical carried inventory, which
is tens of KB. It is written through `Features/SaveScopedXmlStore.cs` (same serializer, same
fail-soft rules), passing its **own** key. The helper's null-key fallback is the station name
(`:47-51`), so the store must never pass null.

### 3.3 Which slots can be a home (S-9)

**Recommended.** A home can be:
- (a) the local human's worn slots, but never the hands;
- (b) slots of containers that pass `GridModel.IsStorageContainer` (`UI/Grid/GridModel.cs:310-353`):
  bags, belts, suit and uniform storage (their typed sockets included), crates, reusable boxes.

**Excluded:**
- **Hands.** An item is never "home" in a hand.
- **Tool internals.** A `Tool` is not storage (`GridModel.cs:313`), so a battery pulled out of a
  welder is not sent back into the welder.
- **Single-use packaging and dispensers**, such as the starter box, water-bottle bag and cereal box
  (`:325-326`). Otherwise "unpack, then G" would put the item straight back in the box.
- **Body-bag contents** (`Core/InventoryScanner.cs:48-49`).
- **Anything not on the local player**, such as world lockers. That is out of scope for v1 (Q8).

*Alternative (rejected):* the literal "wherever it came from". It sends batteries back into tools and
unpacked supplies back into their packaging. An item taken from a slot that isn't eligible simply
has no home, and routes like a new item (§6.2).

### 3.4 Validation at use (every G press)

The steps run in order. Any failure means the home is not live for this press; the entry is kept,
except in case 2.

1. Is there an entry for `held.ReferenceId`, and does `ip == held.PrefabHash`?
2. The container resolves by `Referencable.Find<Thing>(c)` ([27758] `Referencable.cs:136`) and
   `cp` matches. If `c` is `0`, use the local human instead.
   - **Destroyed** (it does not resolve): the home is dead, and the next landing re-seeds it (§4.2).
3. The container is on the local player: the container is the human, or
   `DynamicThing.RootParentHuman == LocalHuman` (`DynamicThing.cs:1079`; same test as
   `Features/BagProfileGate.cs:75-85`).
4. No ancestor of the container is off limits (body bag), and the container is not stow-excluded
   (S-10).
5. `s < container.Slots.Count`, and the slot is not locked (`Slot.IsLocked`, `Slot.cs:967`).
   Execute-time gates (`Slot.AllowMove` / `Slot.CanMerge`) come afterwards, in §6.1.

No depth cap applies. The home is found by id, not by scan, which is exactly what fixes Ningy's belt
inside the backpack.

---

## 4. Capture semantics

### 4.1 First open, and the top-up

- **Trigger:** the first frame of a world where `Guards.CanDraw()` holds (Running, local human
  present; `Core/Guards.cs:25-34`) **and** about 3 s have passed. The delay lets join fragments and
  deferred `MoveToParentWhenReady` placements settle ([27758] `DynamicThing.cs:1373, 1404`). The
  trigger re-fires when the world key or the local human changes (for example after a respawn).
- **What it does:** walks the local inventory with the same walker the router uses
  (`InventoryScanner.Scan`, which skips body-bag contents). Every eligible item (§3.3) that has **no
  live home** gets its current slot recorded (`src=snap` when the file didn't exist, `seed`
  otherwise).
- **It never overwrites a home.** So the "first save open" snapshot and every later top-up are the
  same idempotent code. Installing mid-save, updating from 0.9.7.4, or a client's first join to a
  server all get "homes = what you're carrying now", which is exactly the spec.

### 4.2 Event policy (S-3)

| Event | Code path | Effect on the home | Why |
|---|---|---|---|
| First open / top-up | §4.1 | homeless items only: `snap` / `seed` | "the mod will save where items are" |
| **Drag an item into a slot** (Universal Inventory cell, HUD box, radial chip drop, pinned window) | `ItemActions.DragTo` `:222-326`, `SwapIntoSlot` `:172-210`, `WorldDragTo` `:428-506` | **rehome** to the target | "if I drag an item to a new place in the bag … it will now smart stow to that spot" |
| **Choose a destination** (radial Stow/Insert wedges, F-place in the Universal Inventory, equipment-key stow, Z-grab) | `ItemActions.StowActiveHandTo` `:154-165` (called from `Features/ItemMenuBuilder.cs:380, 470, 545, 585`, `UI/Grid/BagGridCell.cs:433`, `Features/EquipmentKeyRadialFeature.cs:119`), `MoveWorldItemToSlot` `:388-408` | **rehome** | the player picked the bag or slot |
| **Drag-swap bag slot ↔ bag slot** (neither side a hand) | `DragTo` / `SwapIntoSlot` swap branches | **both rehome** | an explicit exchange, like 0.9.7.3's wedge-over-wedge |
| **Swap that involves a hand** (take X into an occupied hand; drag X onto a hand box) | `EquipToActiveHand` `:36-79` (its `PlayerSwapToSlot` at `:75`); swap branches | X: unchanged (it is *leaving*). The displaced held item: **mechanical** (no rehome), and sent to its own home per S-6 | same split as 0.9.7.3: the drop target is explicit; the displaced side is not |
| Vanilla UI drag / scroll-select | `Slot.PlayerMoveToSlot` / `PlayerMergeToSlot` / `PlayerSwapToSlot` / `PlayerInsertToFreeSlot` / `PlayerSwapToWorld` ([27758] `Slot.cs:623, 745, 766, 785, 736`; callers `UI/InputMouse.cs:522-567`, `UI/SlotDisplayButton.cs:448-540`, `UI/InventoryWindowManager.cs:489-498`) | **rehome** (target). The MoveAll / MoveAllOfType tail (`Slot.cs:631-638`) counts as explicit for its **destination container** | the vanilla gesture funnel |
| Bulk "Shift+drag all" (D-018, in flight in `ItemActions`) | new bulk action | **rehome** for its destination container | the player chose the container |
| **Smart Stow itself** (home or fallback landing), including the #13 continuation | `SmartStowPlus.Execute` / `ContinueStackRemainder` | **seed only** (first home if the item has no *live* home) | "visiting" must not steal the home; a destroyed home re-seeds |
| Vanilla sort | `ItemActions.SortContainer` `:760-769` → `Slot.SortContents` ([27758] `Slot.cs:839`) | same-container **slot refresh** only | a sort reshuffles slots, never bags |
| Mined ore into the belt, auto-stowed pickups, outputs | server `MoveToSlot`s | **seed only** | new ore gets the mining belt as its home, a good default |
| Take into a hand (radial take, click, vanilla G summon, split into hand) | `EquipToActiveHand`, `TakeOrDrop`, `Eject` | **no change** | leaving is not a placement |
| Dropped, put in a world locker, consumed, merged away | — | **no change** (the entry goes dormant; GC in §7.5) | pick it back up and G still returns it home |
| World load / join placements | `MoveToParent`, `DeserializeOnJoin` | **ignored** (the observer is off until the world is ready) | these are not player actions |
| Another player's actions (MP) | — | **ignored** (only local intents rehome; seeding only touches the local inventory) | each player owns their own memory |

The rule in one line: **explicit placements rehome; mechanical landings only seed a first home, or
refresh the slot index inside the same bag; leaving never changes a home.**

> **S-3 — YOUR CALL.**
> - **A: "last landing wins"** (every landing rehomes). *Rejected:* G's own fallback, the swap-take
>   and sorting would silently rewrite homes. This is exactly the bug class that 0.9.7.3 fixed for
>   wedges (`Changes Reports/2026-08-06b - Wedge bindings are sticky...`).
> - **B: vanilla-style "the slot you last took it from"**. This is Dipole's literal two-slot memory,
>   i.e. `_originalSlots`. *Rejected as the whole model:* it lives in RAM only, holds 2 entries,
>   ignores drags, and forgets as soon as you take the next item.
> - **C: explicit rehomes, mechanical seeds** (the table above). **Recommended.**

### 4.3 How "explicit" is detected

- **Intents** generalize `BeltBindingStore.NoteExplicitPlacement` (`Features/BeltBindingStore.cs:195-251`):
  - *item-scoped*: `(item refId, dest container refId, slot index, expiry)`;
  - *container-scoped*: `(container refId, expiry)`, used for bulk moves.
  - Each is registered **just before** the authoritative send.
  - Expiry is 3 s unscaled, the 0.9.7.3 precedent. On an MP client, `Slot.Take` fires when the
    **server echo** arrives, not at dispatch.
- **Commit on landing.** A postfix on `Slot.Take(DynamicThing)` ([27758] `Slot.cs:119-141`) sees both
  the host's synchronous apply and the client's echo apply (`DynamicThing.MoveToSlot`, `:2829`;
  `DragInSlot`, `:1862`). If an intent matches, the item is rehomed; otherwise the mechanical rules
  apply. It can share one patch class with the existing `BeltBindingStore.SlotTakePatch` (`:320-339`).
- **Vanilla gestures.** Prefixes on the five `Slot.Player*` methods register intents. **Guard:**
  `ItemActions.EquipToActiveHand` calls `PlayerSwapToSlot` for a *mechanical* swap
  (`Core/ItemActions.cs:75`), so `ItemActions` holds a "mechanical scope" flag around that call and
  the prefix ignores calls made inside it.
- **Cost.** `Slot.Take` fires for every chute hop and machine slot on a host. The observer must
  early-out in a few pointer reads:
  - return while the world is not ready;
  - return when `child == null` (that is `Slot.Empty()`);
  - walk the parent chain up to 8 hops looking for the local human, and stop at the first miss.

---

## 5. Stack identity (S-4)

### 5.1 Facts (verified)

- **A split creates a NEW stack.** The source keeps its ReferenceId and its slot. The child is
  created by `OnServer.Create` (so it gets a new id) and moved into a hand, or merged into a hand
  stack (`Assets/Scripts/Objects/Items/Stackable.cs:389-417` via `SplitIntoHand` / `TrySplitIntoHand`
  `:439-474`; the slot variant is `:420-436`).
  - Every path calls `OnSplitStack(newStack)` (`:477-484`). Every override calls `base`: `Ore.cs:77-86`,
    `PureIce.cs:108-120`, `Slag.cs:20-24`, `Fertiliser.cs:9-18`. `Plant`'s own `SplitStack`s call
    `this.OnSplitStack` (`Plant.cs:1686-1745`).
  - Splits run **only on the simulation authority** (`Stackable.cs:391`).
- **A merge keeps the target.** In `Stackable.Merge` (`:486-515`) the target absorbs up to
  `MaxQuantity`. A child that reaches 0 is destroyed (`OnServer.Destroy`, `:512`); a child with a
  remainder keeps its id.
- The funnel is `Thing.Merge(parent, child)`: the host runs `OnServer.Merge`, a client sends one
  `NetworkClient.Merge` ([27758] `Thing.cs:3864-3872`).

### 5.2 Rules

| # | Situation | Rule |
|---|---|---|
| a | A whole stack moves (taken, stowed, dragged) | Same ReferenceId, so the home follows the §4.2 table. |
| b | Split (take one / take half / choose number) | The **child inherits the source's home** (`src=split`). The source keeps its own. |
| c | Merge, child fully absorbed | The survivor keeps its home. The child's entry is dropped (or GC'd). |
| d | Merge, remainder left | Both keep their ids and their homes. |
| e | The survivor had **no** home and the absorbed child had one | The survivor **inherits** it (`src=merge`). Example: loose floor sheets in hand absorb a bag's sheets, so they are now "from that bag". |

### 5.3 How the rules are wired

- **Host / SP:**
  - a postfix on `Stackable.OnSplitStack` has `__instance` (the source) and `newStack` (the child),
    so it copies deterministically;
  - a prefix on `Thing.Merge` applies rule (e).
- **MP client:** the split child just *appears*. The client registers a **split intent** —
  `(source refId, PrefabHash, expiry)` — where splits are dispatched:
  - `ItemActions.SplitStack` (`:629-643`);
  - vanilla's Button1 / Button2 interaction on a `Stackable`, reached through
    `Interactable.PlayerInteractWith` (the chain is documented in `ItemActions.cs:593-603`; the
    signature needs checking at implementation time).
  
  When a homeless stack with the same PrefabHash lands in a **local hand** while the intent is still
  live, it inherits the source's home.
- **If the inference misses**, the child is simply homeless. It then routes as a new item (§6.2),
  where the stack-merge stage usually tops up the source stack anyway.

### 5.4 FlorpyDorp's sheets, worked through

Setup: stack **X** (50 sheets) lives in backpack **A** slot 3. Another sheets stack **Y** sits in
bag **B**.

1. **Take X into your hand, press G.** Same ReferenceId, and the home is A:3. G sends it to A:3.
   Y and bag B are never considered. *(Today, stage 2 could merge it into Y if B scans first.)*
2. **Take half of X** (vanilla or radial split). X keeps 25 in A:3. The new stack **Z** (25) appears
   in your hand and inherits A:3 (rule b). G finds A:3 occupied by X, a matching partial stack, and
   **merges Z into X**. Result: 50 sheets back in A:3.
3. **Drag X from A:3 into B:7.** That's an explicit placement, so X's home becomes B:7. From now on,
   G on X goes to B:7, while Y keeps its own home.
4. **Merge X onto Y by dragging.** X is fully absorbed and destroyed. Y keeps its home in B. There's
   one stack now, and its home is where you put it.

---

## 6. Resolution order

### 6.1 The Simple resolver (one G press)

0. The existing gates are unchanged: `MasterEnable`, `SmartStowPlusEnabled`, not batch mode, item in
   a hand (`SmartStowPlus.cs:23-33`).
1. **Home live?** (§3.4)
   - **a.** The exact slot is empty and passes `Slot.AllowMove` → **move** (1 message).
   - **b.** The exact slot holds a matching partial stack and passes `Slot.CanMerge` → **merge**
     (1 message). The existing host-only #13 continuation stays **inside the home bag**.
   - **c.** Any partial matching stack **in the home bag** → merge.
   - **d.** The first free slot **in the home bag** that accepts the item. A type-matched slot wins
     over a generic one, mirroring `StowRouter.BestDirectSlot` (`:965-977`).
2. **No live home, or the home bag is full** → the **fallback chain** (§6.2).
3. **Nothing fits** → vanilla `SmartStow`, exactly as a declined router does today (the fail sound
   if vanilla also finds nothing).

Where the item lands afterwards follows §4.2: a fallback landing only **seeds** a home, so a full
home bag is "visiting", never a new home.

### 6.2 The fallback chain (S-5) — YOUR CALL

| Option | Behaviour when there's no usable home | For | Against |
|---|---|---|---|
| F1 **Vanilla** | Decline to vanilla | Zero new logic | Vanilla fills the first generic bag in body order. That is the "walls went to the wrong backpack" bug the router exists to fix (`Documentation/SmartStow-Default-Routing.md` §3). |
| F2 **Stay in hand** | Fail sound + "no home yet" note | Zero ambiguity | FlorpyDorp's own objection: "the first time you make an item you have to open your bags and place it somewhere". |
| **F3 Today's no-profile chain** *(recommended)* | Belt tool → stack merge → socket → affinity → generic fallback → belt last-resort → vanilla | Already built and play-tested (three rounds). This zero-setup tier is exactly the "brand-new item" case. The landing seeds a home, so the ambiguity lasts one press. | Needs a router flag that skips Profile / BagDefault / Memory and treats every bag as unprofiled (affinity and generic fallback currently skip profiled bags: `StowRouter.cs:497, 707`). |
| F4 Full Advanced chain | Profiles included | Nothing new to build | Brings profiles into "Simple" through the back door. A Simple player can't see or edit the profiles that would be routing their items. |

**Recommended: F3, and the Simple chain is not configurable.** It ignores the per-stage toggles,
which remain Advanced settings, and is gated only by the master switch. It stays predictable and
testable.

### 6.3 Swap-take sends the held item home (S-6) — YOUR CALL

- **Taking into an occupied hand is the most common radial gesture.** A plain swap drops the held
  item **A** into the taken item **B**'s slot. Now B's home is occupied by A, which isn't home
  either, and every further swap scrambles the bag a little more.
- **0.9.7.3 already solved this for belt tools.** It swaps, then relocates A to its own home
  (`ItemActions.cs:64-76`, `BeltHomeFor` `:98-119`, and the same logic in the `DragTo` /
  `SwapIntoSlot` swap branches). This is the documented **two-message exception**: each message is
  independently server-gated, and a refused relocation degrades to exactly the old swap.
- **Recommended:** generalize `BeltHomeFor` to "A's live Simple home, if it is free and accepts A"
  in Simple mode. Keep the belt-binding lookup as the fallback when A has no instance home.
- This is also the half of Dipole's proposal that makes wheels feel like other games' wheels ("they
  select things but aren't usually moving items around an inventory behind the scenes").
- *Alternatives:* ship it behind a toggle, or leave it out. Leaving it out is not recommended,
  because homes degrade from the first swap.

### 6.4 Gates (S-10)

- **Stow exclusion** (`BagProfileStore.IsStowExcluded`, `Features/BagProfiles.cs:978-984`) is
  honoured in Simple mode too. An item whose home is an excluded bag uses the fallback chain.
  - This matches the router's own rule: "an exclusion the belt stage ignored would be an exclusion
    the player cannot trust" (`StowRouter.cs:804-813`).
  - Simple mode has no UI to set exclusions. A one-line note shows when any exist (§10.1).
- **Off limits** (body bags) and **eligibility** (§3.3) are enforced both at capture and at use.

### 6.5 Where it plugs in

- `StowStage.Home` becomes the new stage 0 in `Core/StowRouter.cs:14-26`.
- `ResolveBest` gains a mode argument:
  - **Simple:** Home, then the F3 stages.
  - **Advanced:** today's chain, byte-identical.
- `ResolveAll` (the F10 test box, `stowtrace`) reports the Home stage too, so every diagnostic
  surface keeps mirroring the G key (`StowRouter.cs:39-54`).
- **Dry runs stay side-effect-free.** In particular they never seed a home, the same rule as the
  belt table (`:181-196`).

---

## 7. Persistence

### 7.1 Save identity (S-2)

| Option | Resets on a new world | Works on MP clients | Survives renaming the save | Branches ("Save As") | Verdict |
|---|---|---|---|---|---|
| Station name (today's `CurrentSaveKey`) | only if the name is new | **no**: every server is `"unsaved"` | no | separate | Reject. A reused name also inherits a stale file whose low ReferenceIds collide. |
| **`World.CurrentId` GUID** | yes (a new GUID per world, `World.cs:117-119`) | **yes** (replicated on join) | yes | **shared** | **Recommended.** It is the same key vanilla uses for per-world client prefs. |
| GUID + station name | yes | clients: GUID only | no | separate | Extra complexity for branch separation nobody asked for. |
| Sidecar file in the save folder | yes | **no save folder on clients** | yes | separate, and rolls back with the save | Reject. It writes into the game's save/cloud-sync area (`StationSaveContainer : ICloudSyncable`) and needs a save hook. |

**Fallback:** when `World.CurrentId` is empty (a legacy pure-SP save where `PopulateEmptyId` never
ran), the key is `station-<CurrentSaveKey()>`. If that is also `"unsaved"`, the store keeps its
table in memory only until a key exists.

### 7.2 Layout

- One file per world: `BepInEx/config/StationeersUIMod/StowHomes/<World.CurrentId>.xml`, written via
  `SaveScopedXmlStore.SavePerSave("StowHomes", worldKey, …)`.
- The new store is `Features/StowHomeStore.cs`. It follows the `BeltBindingStore` load/save shape:
  `EnsureLoaded()` re-checks the key on use, then loads.

### 7.3 Write policy

- **Debounced**: dirty flag, flush about 2 s after the last change, like the HUD autosave. A bulk
  move of 20 items should not write 20 files.
- **Also flushed** when the world key changes and on `Shutdown`.
- **Crash exposure:** at most the last ~2 s of re-homes. They are re-learned from the next drag, or
  seeded by the next top-up.

### 7.4 Lifetime

| Situation | Result |
|---|---|
| New world | New GUID → empty file → first-open snapshot. "on a new save it resets" ✔ |
| Same save, any number of sessions | Same GUID. "persists across your single save" ✔ |
| Dedicated server restarted from its save | Same GUID ✔ |
| Server wiped / new world | New GUID ✔ |
| "Save As" branch | Shares the GUID and the file. ReferenceIds match at the branch point, and later drift is caught by §3.4 validation. **Accepted** (Q9). |
| Reload an older save (rollback) | Entries for items made after that save may now name other items → PrefabHash validation. Items that still exist keep their most recent home, which is the player's latest intent. |

### 7.5 Size and GC

- **At world-ready (host / SP):** the whole world is loaded, so drop entries whose item id doesn't
  resolve or resolves to a different PrefabHash.
- **MP client:**
  - drop on a PrefabHash mismatch;
  - drop "not found" entries only when unseen for 30 days *(tunable)*. This is conservative in case
    a client doesn't hold every Thing.
- **Cap:** 4,096 entries per world. Oldest `seen` goes first.
- **Other worlds' files are never auto-deleted** (you may come back to a save months later).
  - Tools to clean them up: F10 "Forget homes in this world" (§10.1) and `uiareset`.

### 7.6 Reset / repair interplay

| Tool | Effect on homes |
|---|---|
| `uiareset` (`Core/FinderCommands.cs:292-361`) | Deletes the whole tree, `StowHomes/` included. After the restart, each world re-snapshots on its next open. |
| **Repair config folders** | Add `SaveScopedXmlStore.DirFor("StowHomes")` to `ConfigTreeRepair.Folders()` (`Core/ConfigTreeRepair.cs:113-137`). That list is hand-maintained, per the 2026-08-10 report. |
| Folder deleted mid-session | The in-memory table is the surviving truth, and the next debounced flush recreates it (`SaveScopedXmlStore.Save` creates the directory, `:120-137`). |
| File missing at the next load | Treated as a first open: snapshot current positions. This is harmless degradation. |

### 7.7 Flag: the same bug exists in the six existing per-save stores

`Assignments/`, `BeltBindings/`, `Hotkeys/`, `Grid/`, `GridPins/` and `HintUsage/` all key on
`CurrentSaveKey()`. So on MP clients:

- **every server shares one `unsaved.xml` per store;**
- a bag assignment or wedge binding learned on server X can re-appear on server Y, attached to
  whatever carries the same ReferenceId there.

Moving them to `World.CurrentId` needs a file-rename migration, so it is **out of scope** here. It is
listed as an optional phase (P5c) because the new store proves the key first.

---

## 8. Multiplayer safety (CLAUDE.md rule 3)

| Requirement | How the plan meets it |
|---|---|
| Memory is client-local **read** state | The store is config XML on this machine. Capture, snapshot, GC and dry runs **send nothing**. |
| On a client, read only networked state | Everything is networked: ReferenceIds, parent + SlotIndex, PrefabHash, `RootParentHuman`, `World.CurrentId`. It never reads a server-only value. |
| Every mutation goes through the game's funnel, in `ItemActions` | The home move is a new `ItemActions.StowHeldTo(hand, dest)`, a sibling of `StowActiveHandTo` (`ItemActions.cs:154-165`) that calls `OnServer.MoveToSlot`. The merge uses `ItemActions.MergeInto` (`:929-935`). **The two existing direct calls in `SmartStowPlus.cs:82, 143` move into `ItemActions` in the same phase.** |
| Gated at execute time | `Slot.AllowMove` / `Slot.CanMerge` are re-checked immediately before each send, on the same call stack as the decision (as in `SmartStowPlus.Execute`). |
| One user action = one message | A home stow is one message. Exceptions, both **already sanctioned**: the host-only #13 continuation (`SmartStowPlus.cs:95-147`, unchanged, confined to the home bag), and S-6's swap-then-relocate (the documented two-message exception). |
| A client's echo | Nothing moves locally. The intent window matches the server echo (§4.3). If the server refuses, the item stays in the hand, and no home changes because no landing happened. |

---

## 9. Mode structure and migration

### 9.1 Config

| Key | Section | Type / default | Notes |
|---|---|---|---|
| `Mode` | `6. SmartStow+` | enum `Simple` / `Advanced`, default **Simple** | New key. `Enabled` stays the master for both modes. |
| `SimpleNotes` | `6. SmartStow+` | bool, default **true** | Toasts in Simple mode, only for exceptions: "no home yet - put in X", "home full - put in X". Reuses `StowToast` (`SmartStowPlus.cs:156-171`). Q10. |
| `SmartStowModeNoticeShown` | `0. Internal` | bool, default false | The one-time update notice. |

No key is removed or renamed. The existing `Stow*` toggles stay bound and apply to Advanced.

### 9.2 What each mode does

| Surface | Simple | Advanced |
|---|---|---|
| G resolver | Home → F3 chain → vanilla (§6) | Today's 9-stage chain, unchanged (optional home stage: P5a) |
| Home capture / observer | on | **on as well** (cheap), so switching modes is seamless |
| F10 Storage | Mode block + one Simple page (§10.1) | Mode block + today's four sub-tabs |
| Universal Inventory profile mode, bag-tab badges, capture panel | hidden (the data stays on disk) | as today |
| Profile-aware sort (`Features/ProfileSort.cs:127`) | off | as today (`StowProfileSort`) |
| Assignments, Stow Profiles, exclusions, type memory | **kept on disk, not consulted** (exclusions excepted, S-10) | as today |
| Swap-take routing (S-6) | instance home, then the belt binding | belt binding (today) |

Hide, never destroy: switching Advanced → Simple → Advanced loses nothing.

### 9.3 Existing users (S-8) — YOUR CALL

| Option | What happens on update | For | Against |
|---|---|---|---|
| A. **Flip everyone** | The new key's default (Simple) applies to all | One default everywhere; this is what the Discord players asked for | Players who built Bag Profiles (FlorpyDorp and testers) suddenly see them "ignored" |
| B. **Fresh installs only** | A migration step sets `Advanced` on every existing install | Zero behaviour change on update | Most current players never set up profiles (the design was "very hard to understand", per FlorpyDorp in the D-016 thread), and they'd never get the better default |
| **C. Evidence-based** *(recommended)* | Existing install: **Advanced if any `<Assign>` exists in any `Assignments/*.xml`**, otherwise Simple. Any IO error → Advanced (that is today's behaviour). | Respects investment; everyone else gets the new default | A heuristic. Someone who assigned one bag once keeps Advanced, which is harmless. |

Mechanics:

- **The step.** One `ConfigMigration` step (`Core/ConfigMigration.cs`, currently `CurrentVersion = 4`),
  written per `Config-and-Theme-Migration.md` §2. **Coordinate the version number:** the in-flight
  D-020 fix may claim v4→v5, so SmartStow takes the next free step.
  - The step runs at bind time, before the stores load.
  - It reads `Paths.ConfigPath/StationeersUIMod/Assignments/*.xml`, fail-soft.
  - Fresh installs are stamped straight to current and run no steps, so they get the default:
    Simple.
- **The notice.** On the first world-ready after the update, a one-time ASCII toast:
  - Simple: *"Smart Stow now returns items to where you took them from. Prefer Bag Profiles? F10 >
    Storage > Advanced."*
  - Advanced: *"You use Bag Profiles, so Smart Stow stays in Advanced mode. Try Simple in F10 >
    Storage."*

### 9.4 CLAUDE.md rule 8 statement

1. `Mode` and `SimpleNotes` are **behaviour** settings with no HUD visual. They are documented here as
   **shared, not per-tier**.
2. They live in `UIAConfig`, which `HudTheme` snapshots only through an explicit include-list
   (`UI/Hud/HudTheme.cs`, `RadialThemeKeys`). So they **do not travel with a HUD theme**, which is
   correct for behaviour. The precedent is `RadialShiftKeepsOpen`: "a theme must never flip a
   player's Shift muscle memory" (`UI/Menu/Tabs/RadialTab.cs:30-33`). *FlorpyDorp: please confirm
   this reading of rule 8, that behaviour is not look.*
3. No key is removed or renamed. The existing-user step is a value step, added the §2 way.

---

## 10. UI touchpoints

### 10.1 Minimal F10 Storage changes now

Today's Storage tab hosts four sub-tabs (`UI/Menu/Tabs/StorageTab.cs:40-47, 136-163`). The minimal
change adds a mode block above the strip, and swaps the strip for one page in Simple mode:

```
STORAGE
+------------------------------------------------------------------------------+
| SMART STOW (G)                                              [ on/off switch ] |
| Mode: [ Simple - back where it came from ] [ Advanced - Bag Profiles & rules ]|
|  Simple: every item goes back to the bag and slot you last put it in. Move it |
|  by hand and its home moves. New items go where they fit best, once.          |
+------------------------------------------------------------------------------+
 SIMPLE                                     | ADVANCED
  Homes remembered in this world: 143       |  [Bags][Bag Profiles][Stow Profiles][Settings]
  In your hand: Steel Sheets x50            |  ...today's four pages, unchanged...
    home: Backpack #2, slot 4 (you put it)  |
  [x] Tell me when an item has no home yet  |
      or its home is full                   |
  [ Forget homes in this world ]   (confirm)|
  [ Re-learn homes from what I carry now ]  |
                                   (confirm)|
  (2 containers are set to "never stow"     |
   - Advanced > Bags)                       |
  UNIVERSAL INVENTORY  ...unchanged block...|
```

- **The in-hand readout** is the Simple tester's tool. The Advanced test box picks a *prefab*, so it
  cannot show an *instance* home, and it stays Advanced-only.
- **Both buttons** use the arm/confirm idiom already in `StorageTab` (`:811-822`).
- **Build-time costs** (store counts, the hand lookup) are cached across a restyle exactly like
  `_bagScratch` (`StorageTab.cs:78-88`). A theme drag re-builds F10 about 7 times a second.
- **Naming collision (Q9):** the F10 title bar already has a global **Simple / Advanced** density
  toggle (`UI/Menu/UiaControlCenter.cs:387-389`). The F10 plan proposes resolving it there.

### 10.2 Other surfaces

| Surface | Change | Phase |
|---|---|---|
| Universal Inventory profile-mode button, badges, capture panel | Hidden in Simple (`UI/Grid/TheGridPanel.cs` toggle, `GridRegionView.cs:385-391`) | P4 |
| `ProfileSort` | Gated on Advanced | P4 |
| Guide tab "Smart storage" text | Rewrite for Simple (`UI/Menu/Tabs/GuideTab.cs:74-81`) | P4 |
| Tutorial step 13 "Smart-Stow" | Rewrite (`UI/Menu/Tutorial/TutorialSteps.cs:173-180`). Coordinate with `Tutorial-Plan-and-Script.md`. | P4 |
| `stowtrace` | Prints the HOME line first (`Core/FinderCommands.cs:141-205`) | P2 |
| **New `stowhomes` console dump** (read-only) | World key, count, the held item's entry with provenance, per-bag counts, GC stats | P0 |
| Optional: a "home ghost" in the Universal Inventory (while holding an item, its home slot glows) | New, modelled on `UI/Grid/GridGhostHint.cs` | P5b |

### 10.3 PLACEHOLDER — Advanced SmartStow UI redesign (waiting on FlorpyDorp's images)

> **Deferred until FlorpyDorp supplies his ChatGPT UI concept images.** Nothing in this plan changes
> the Advanced screens beyond the mode block above. When the images arrive, fill this in:
>
> | Image | Screen it shows | What it decides | Feasibility notes (Kit v2, see F10 plan) |
> |---|---|---|---|
> | `concepts/smartstow-organizer-1.png`, `-2.png` | Earlier drafts of the Organizer | Superseded by #3 | — |
> | **`concepts/smartstow-organizer-3.png`** (**CHOSEN**, FlorpyDorp 2026-09-25) | SmartStow tab → Organizer, Complex mode | See "What #3 decides" below the table. | Every control maps to an existing store call (review doc, Part B2). The new kit pieces are listed in the F10 plan §9, row 3. |
>
> **What #3 decides:**
> - **Tab name:** the Storage tab becomes **SmartStow**.
> - **Mode names:** **Simple / Complex**. This answers Q9: the player-facing label for today's
>   system is "Complex", not "Advanced".
> - **Mode bar placement:** the mode bar sits above the sub-tabs, as §10.1 has it.
> - **Sub-tabs:** **Organizer / Routing / Universal Inventory**. Today's Settings page becomes
>   Routing.
> - **Renaming:** "Stow Profile" is shown to players as **"Storage Layout"**, while code, XML and
>   the `StowProfiles/` folder keep their names.
> - **Organizer layout:** Layouts | Bags | Bag Profiles, with a rule editor underneath.
> - **Sharing:** layout-level sharing only, which implies the single-Bag-Profile file share is
>   retired.
>
> **Still needed from FlorpyDorp:**
> - **The Simple-mode page.** No image of it yet; §10.1's sketch stands.
> - **Q9's leftover.** The mode's "Simple" repeats the F10 title bar's Simple/Advanced density
>   button on the same screen (F10 plan Q4).
>
> **§9.1's `Mode` key.** It hasn't shipped, so its values can be named `Simple / Complex` to match
> the UI instead of `Simple / Advanced`.
>
> **Build-time fixes** (listed in `Advanced-SmartStow-UI-Mockup-Review.md` Part D):
> - add the Smart Stow on/off switch to the mode bar;
> - give the rule editor full height or a virtualized list;
> - add share-code import;
> - fix the share-bar text;
> - grow the window (F10 plan Q2).
>
> Inputs already on record: the 0.9.7.0 plan's recommended shapes (bag cards 3-across; the
> master-detail Bag Profile editor, Option 2A; set cards + profile shelf, Option 3A; drag between
> sets as an accelerator), plus FlorpyDorp's 2026-08-07 note that the profile system "is very hard
> to understand right now". The redesign is built on the F10 plan's Kit v2 (Stage 5 there).

---

## 11. Phased implementation

Sizes use the 0.9.7.0 plan's scale: **S** ≈ a day, **M** ≈ 2-4 days, **L** ≈ a week or more. Each
phase is independently shippable and revertible.

| Phase | Contents | Behaviour change | Size |
|---|---|---|---|
| **P0 Identity + store** | `World.CurrentId` key helper; `Features/StowHomeStore.cs` (model, load/save via `SaveScopedXmlStore`, validation, debounce, GC, cap); `stowhomes` dump; `ConfigTreeRepair.Folders()` line; `Shutdown` reset | none | M |
| **P1 Capture (observe only)** | Snapshot/top-up pump; `Slot.Take` observer (shared with `BeltBindingStore`); intent table; intent notes at the `ItemActions` sites in §4.2 (+ the mechanical-scope guard); `Slot.Player*` prefixes; `OnSplitStack` postfix; `Thing.Merge` prefix; client split intents | none (homes recorded, not used). Verify with `stowhomes`. | M-L |
| **P2 Resolver + executor** | `StowStage.Home` + Simple resolve path + F3 flag in `StowRouter`; `SmartStowPlus.TryStow` branches on `Mode`; `ItemActions.StowHeldTo`; the existing executor's `OnServer` calls move into `ItemActions`; `Mode` / `SimpleNotes` keys; `stowtrace` HOME line | yes, behind `Mode` (dev builds default Advanced until P4) | M |
| **P3 Swap-take home (S-6)** | Generalize `BeltHomeFor` in `EquipToActiveHand` / `DragTo` / `SwapIntoSlot` | yes | S-M |
| **P4 Mode UX + migration** | Migration step + notice; F10 mode block + Simple page (§10.1); Universal Inventory / ProfileSort gating; Guide + tutorial copy | **the default flips** | M |
| P5a *(opt)* | An Advanced-chain "home first" stage toggle, default off | opt-in | S |
| P5b *(opt)* | The "home ghost" glow in the Universal Inventory | visual | S |
| P5c *(opt)* | Move the six per-save stores to `World.CurrentId`, with file-rename migration (§7.7) | fixes MP-client cross-talk | M |
| Deferred | The Advanced SmartStow UI redesign from FlorpyDorp's images (§10.3) | — | TBD |

**Sequencing constraints:**
- Land P1's `ItemActions` edits **after** this wave's grid/drag workstream (D-002 / D-005 / D-018,
  which owns `Core/ItemActions.cs`). D-018's new bulk action needs its container-scoped intent note.
- P2 needs P1's data to be meaningful, but can be developed against a hand-edited homes file.

### Per-phase play-test checklists

**P0/P1 (observe-only; check `stowhomes` after each step):**
1. New SP world, right after spawn: every carried item is listed with `src=snap`; nothing in the
   hands is listed.
2. Drag sheets from backpack slot 3 to belt slot 2 → the entry shows `belt / 2` with `src=drag`.
3. Radial-take sheets while holding a wrench: the sheets' home is unchanged; the wrench's home is
   unchanged.
4. Sort a bag → slot indexes refresh; containers don't change.
5. Take half of a stack → the child shows `src=split` with the parent's home. Test on the host
   **and** as an MP client.
6. Quit and reload → homes persist. Start a new world → a new file and a fresh snapshot.
7. MP client on a dedicated server: the file is named by the world GUID, not `unsaved.xml`. Rejoin →
   same file.
8. Delete `StowHomes/` mid-session, then make a change → the file is recreated.
9. Double-F6 → no duplicate observer, no stale intents, no stale world key.

**P2:**
1. The four sheets scenarios (§5.4).
2. **Ningy's case:** a drill whose home is the mining belt inside the backpack → G puts it in that
   belt.
3. Home bag full → the item goes elsewhere, the note says so, and `stowhomes` shows the home
   unchanged.
4. Drop the home bag on the floor → fallback. Pick the bag up again → G returns the item home.
5. Craft a new item → the fallback landing becomes its home, and a second G goes to the same place.
6. An excluded home bag is skipped. A body bag is never a home.
7. MP client: one message per G (watch the log); a refused move leaves the item in the hand.
8. The build hologram is cancelled after G (the existing postfix, `SmartStowPlus.cs:214-224`).

**P3:** Belt-tool and bag-item swap-takes put the held item back on its home. On MP, accept the
two-hop visual. Bindings and homes are both unchanged afterwards.

**P4:**
- Fresh install → Simple.
- Existing install with assignments → Advanced + the notice.
- Existing install without assignments → Simple + the notice.
- SmartStow+ disabled → it stays disabled.
- Switching modes back and forth loses nothing.
- `uiareset` → fresh state.

---

## 12. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| `Slot.Take` observer cost on large host bases (every chute hop) | Medium | Early-outs (§4.3). Profile a chute-heavy save with `uiaprof` before P1 ships. |
| An explicit move misclassified as mechanical, or the reverse → homes drift | Medium | Mechanical is the conservative default. The `src` provenance field + `stowhomes` make every case diagnosable. |
| Server echo takes more than 3 s → an explicit drag is treated as mechanical | Low-Med | Precedent window; widen to 5 s if play-tests show it. Degradation is benign: the home simply isn't updated. |
| ReferenceId reuse after a rollback → wrong home | Low | PrefabHash validation on the item and the container (§3.4). |
| A game update changes a bag prefab's slot list (SlotIndex shift) | Low | Validation + same-bag fallback + slot refresh on landing. |
| Legacy pure-SP save with an empty `World.CurrentId` | Low | The `station-<name>` fallback key. |
| Players confuse F10's Simple/Advanced density toggle with the SmartStow modes | Medium | Naming (Q9); the F10 plan proposes a fix. |
| Existing users surprised by the default flip | Medium | S-8 option C + the one-time notice. |
| Widening the two-message exception (S-6) | Low | The same degrade-to-old-behaviour ordering as 0.9.7.3. |
| Hot reload | Medium | Every new static (table, intents, world key, snapshot latch, split intents) resets in `Shutdown`. Patches go through `PatchHarness.TryPatchAll` (fail-soft per class). |
| Concurrent edits to `ItemActions` / `UI/Grid` in this wave | Medium | Sequencing (§11). Stage only this work's files. |

---

## 13. Open questions for FlorpyDorp

1. **(S-3) Capture:** approve "drags and chosen-destination placements set the home; everything
   mechanical only seeds a first home; taking never changes a home"?
2. **(S-5) Fallback:** approve "home → today's no-profile chain → vanilla, and wherever it lands
   becomes its home"? *(Alternative: stay in hand with a note.)*
3. **(S-8) Existing users:** A (flip all), B (fresh only) or **C (keep Advanced only if you've
   assigned a Bag Profile)**?
4. **(S-1) Granularity:** home = **exact slot** (falls back within the bag), or just the bag?
5. **(S-6) Swap-take:** should taking into an occupied hand send the held item home (yes /
   behind a toggle / no)?
6. **(S-9)** Homes only in real storage + worn slots (not inside tools, not in packaging): OK?
7. **(S-10)** Keep "never stow into this" active in Simple mode: OK?
8. **World lockers:** out of scope for v1, so G only ever returns items into your own inventory?
   (D-018's quick-store covers lockers.)
9. **Naming:** keep "Simple / Advanced SmartStow" (it clashes with F10's Simple/Advanced toggle), or
   player-facing labels like **"Return Home" / "Bag Profiles"**? Also: are save **branches**
   sharing homes OK?
10. **Teaching notes** ("no home yet → X", "home full → X") on by default in Simple?
11. Should Advanced get an optional **"home first"** stage (P5a)?

---

## Appendix A — Game APIs this plan depends on (all verified)

| API | Use | Citation ([27758] unless noted) |
|---|---|---|
| `World.CurrentId` | file key | `Assets/Scripts/Objects/World.cs:117-140`; `Assets/Scripts/Serialization/XmlSaveLoad.cs:325-327`; [AR] `XmlSaveLoad.cs:864`; `WorldManager.cs:1489-1508`; `Assets/Scripts/GameManager.cs:714-722, 816-825` |
| `Thing.ReferenceId` / `Referencable.Find<T>` | item and container identity; lookup | `Referencable.cs:25-34, 68-120, 136-150, 185-199, 206+`; `Assets/Scripts/Objects/Thing.cs:795-821, 6187-6198`; `Assets/Scripts/Networking/JoinFragmentMessage.cs:26-33` |
| `Thing.PrefabHash` | validation | `Thing.cs:6643` |
| `Slot.SlotIndex`, `Slot.IsLocked`, `Slot.IsHandSlot`, `Slot.Parent` | home slot; gates | `Assets/Scripts/Objects/Slot.cs:472, 967, 197, 923` |
| `Slot.AllowMove` / `AllowSwap` / `CanMerge` | execute-time gates | `Slot.cs:380, 323/343, 306` |
| `Slot.Take` | landing observer (Harmony postfix) | `Slot.cs:119-141`; callers `DynamicThing.cs:1862, 2829` |
| `Slot.PlayerMoveToSlot` / `PlayerSwapToWorld` / `PlayerMergeToSlot` / `PlayerSwapToSlot` / `PlayerInsertToFreeSlot` | vanilla explicit-intent prefixes | `Slot.cs:623-640, 736, 745-764, 766-776, 785+` |
| `DynamicThing.RootParentHuman` | "is it on me" | `Assets/Scripts/Objects/DynamicThing.cs:1079` |
| Save and wire slot identity (reference only) | why (container, SlotIndex) is safe | `DynamicThing.cs:1300-1301, 1340-1341, 1364-1401, 3355-3420, 3436-3470` |
| `Stackable.SplitStack` ×2, `OnSplitStack`, `Merge` | stack rules (postfix on `OnSplitStack`) | `Assets/Scripts/Objects/Items/Stackable.cs:389-436, 439-474, 477-484, 486-515`; overrides `Ore.cs:65-86`, `PureIce.cs:108-130`, `Slag.cs:20-24`, `Fertiliser.cs:9-18`, `Plant.cs:1686-1760` |
| `Thing.Merge` | merge funnel (prefix) | `Thing.cs:3864-3872` |
| `InventoryManager.SmartStow` | the existing G prefix target (unchanged) | `Assets/Scripts/Inventory/InventoryManager.cs:1815-1900` |
| `XmlSaveLoad.CurrentStationName` | *not* used as a key (§3.1) | `XmlSaveLoad.cs:254-258, 706`; `GameManager.cs:881-922`; [AR] `LoadHelper.cs:35`, `UI/MainMenu.cs:166`, `GameManager.cs:957` |

## Appendix B — Files this plan would touch (for scoping only)

**New:**
- `Features/StowHomeStore.cs` (the store, intents, capture rules; ~450 lines);
- a world-key helper (~60 lines);
- `Core/VanillaPlacementPatches.cs` (`Slot.Player*`, `OnSplitStack`, `Thing.Merge` patches; ~150 lines);
- the `stowhomes` command (~100 lines, in `Core/StowCommands.cs` or `FinderCommands.cs`).

**Modified:**
- `Core/StowRouter.cs` (Home stage, Simple path, F3 flag);
- `Features/SmartStowPlus.cs` (mode branch; executor via `ItemActions`);
- `Core/ItemActions.cs` (`StowHeldTo`; intent notes; mechanical scope; S-6);
- `Features/BeltBindingStore.cs` (share the `Slot.Take` observer);
- `UIAConfig.cs` (two keys);
- `Core/ConfigMigration.cs` (one step);
- `Core/ConfigTreeRepair.cs` (one line);
- `UI/Menu/Tabs/StorageTab.cs` (mode block, Simple page);
- `UI/Grid/TheGridPanel.cs`, `GridRegionView.cs`, `Features/ProfileSort.cs` (gating);
- `UI/Menu/Tabs/GuideTab.cs`, `UI/Menu/Tutorial/TutorialSteps.cs` (copy);
- `StationeersUIMod.cs` (patch list, `Shutdown` reset).

**Untouched:** the Stow Profile model, `StowShareCodec`, the shipped presets, every Advanced page.
