# Smart Stow Rework — Design Options

*2026-07-20 — brainstorm/options doc for FlorpyDorp + JacksonTheMaster, written as the
"we will brainstorm this together" pass that was deferred when the Universal Inventory
shipped.*

> **STATUS: APPROVED (2026-07-20).** FlorpyDorp approved the full scope — every option
> group **except O8 (Stow All), which is permanently rejected**. All three passes
> (2a/2b/2c) build in one go; play-testing happens after everything lands. The six open
> questions in §6 are answered inline below.

---

## 1. Where we are (and what's already good)

The current chain (`Features/SmartStowPlus.cs`, G key) runs, in order:

| # | Stage | Setup cost | Notes |
|---|-------|-----------|-------|
| 0 | Tool → directly-worn belt (home-slot aware) → worn back container | zero | uses the Belt Wheel's home-slot bindings; already great |
| 1 | Top up a matching stack anywhere | zero | vanilla lacks this; already great |
| 2 | Bag whose **assigned profile** matches (item > slot class > category; shallow beats nested on ties) | HIGH — make profiles, assign each bag | works, but this is the "forever setting up profiles one by one" problem |
| 3 | Where this item type last went (per-save memory) | zero (implicit) | quietly does a lot of the real work today |
| — | Vanilla SmartStow | — | untouched fallback |

The F10 Storage tab already has: the toggles, a 10-profile "Create recommended" +
"Auto-assign to my bags" one-click, a per-worn-bag profile dropdown, and a rule editor
(searchable item picker + category + slot-class rules). Profiles are global XML
(`Profiles/profiles.xml`), assignments + memory are per save (`Assignments/<save>.xml`).

**What's good and should survive:** the staged-resolution idea, the item > slot-class >
category precedence, shallow-beats-nested, per-save memory, profiles-as-shareable-XML, and
the F10 tab as the "power editor." **The rework is not a replacement — it's adding the
missing zero-setup tier, the capture flow, the Universal Inventory surface, and cross-save
portability.**

### The three problems, named

1. **Setup wall.** Stage 2 only pays off after the player has built and assigned profiles.
   Most players will never open F10. Today they silently live on stages 0/1/3.
2. **Identity fragility.** Assignments key on `Bag.ReferenceId` — save-scoped and
   instance-scoped. A new save, a replaced bag, or a friend's server = all assignments
   gone. This is why "profiles should swap between saves" currently can't work.
3. **No surface where the player lives.** All profile UX is in F10. The player's mental
   model of their bags is the Universal Inventory — profiles need to be visible and
   editable *there*, without cluttering it.

---

## 2. Prior art worth stealing from

- **Terraria "Quick Stack (to nearby chests)"** — the gold standard for zero-setup routing:
  items fly to containers that *already hold that item*. No configuration exists at all;
  the player's own organization IS the ruleset. Beloved, legible, self-reinforcing.
- **Terraria favorites** — lock an item so no sort/stow ever moves it. Cheap, powerful.
- **Escape from Tarkov containers** — per-container type gating (pistol case takes
  pistols). Players accept "this bag is FOR that" when the container itself says so —
  argues for **bag-type defaults** (a mining belt should route ores out of the box).
- **Diablo-style auto-sort** — sort is a *view* operation, distinct from routing. Keep our
  per-bag Sort button conceptually separate from stow rules, but let a profile inform sort
  order (profile-priority items first).
- **Factorio logistics requests** — the full rule-builder endgame. Powerful, but its lesson
  is the opposite one: almost nobody sets up requests until the game *captures them for
  you* from an existing state (the "copy from blueprint" flow). Capture-from-reality beats
  form-filling. That is exactly the "fill the bag, press the button" idea.

---

## 3. The frame: three tiers of investment

Everything below hangs off one principle: **each tier must be fully useful without the
tier above it.**

- **Tier 0 — zero setup.** The chain works well with NO profiles: stack merge, tool home
  slots, type memory, and (new) **content affinity** + **bag-type defaults**.
- **Tier 1 — one gesture.** "This bag is right → make it law": the **capture button**, the
  per-bag profile picker on the manila tab, drag-an-item-onto-a-bag to pin it there.
- **Tier 2 — full control.** The F10 rule editor (already exists), upgraded with the
  search/category/icon picker flows and priority visibility.

---

## 4. Option groups

### O1 — The router refactor (the architectural core of the "massive rework")

Extract the decision logic out of `SmartStowPlus.TryStow` into a single **`StowRouter`**:

- Ordered stages, each returning *(candidate slot, score, human-readable reason)*.
- One decision point picks the winner; the mutation stays exactly where it is
  (one `OnServer.MoveToSlot`, gated at execute time — no MP-safety change).
- **Dry-run mode**: resolve without moving. This one feature unlocks most of the UX below
  (ghost hints, "why did it go there," capture previews) for free.
- Every consumer — G-stow, a future "stow this item" wedge, profile-aware Sort — calls the
  same router, so behaviour can never drift between surfaces.

*Cost: pure refactor, no behaviour change. Recommend: do first, regardless of everything
else chosen.*

### O2 — Zero-setup routing (the missing Tier 0)

**O2a. Content affinity ("like goes with like") — RECOMMENDED.**
A bag with no profile gets an *implicit* one, derived live from its contents. When routing
an item, score each candidate bag: same prefab already inside (strong), same slot class
(medium), same sorting category (weak) — weighted by count, shallow beats nested, and
capped below any *explicit* profile match so real profiles always win. Terraria's quick
stack, generalized. The player organizes a bag once by hand and it self-maintains forever
— **which is also exactly the setup gesture the capture button needs**, so Tier 0 flows
into Tier 1: play → notice it already routes right → press capture only to make it
permanent even when the bag runs empty.

**O2b. Bag-type defaults.**
Ship a small table: mining belt → Ores, tool belt → Tools, gas canister case, etc.
(prefab → shipped profile name). Applies when a bag has no assignment. With O2a live this
matters mostly for *empty* bags fresh from the printer — worth having, tiny to build.

**O2c. Smarter type memory (keep, demote).**
Memory currently stores one bag per item type forever. Under O2a it becomes the
tiebreaker rather than a primary stage (affinity already encodes "where these live" and
survives bag replacement, which memory's ReferenceId does not).

*Chain order becomes: worn-belt tools → stack merge → explicit profile → content
affinity → bag-type default → type memory → vanilla.*

### O3 — The capture flow ("bag → profile")

**O3a. Snapshot capture.** Button captures the bag's contents as N item rules, name popup,
done. Simple, literal, but brittle: a bag of 14 ore types becomes 14 prefab rules and
still misses the 15th ore.

**O3b. Generalizing capture — RECOMMENDED.** On capture, cluster the contents: if ≥3 items
share a sorting category (or slot class), propose the *category* rule instead of item
rules; oddballs stay item rules. Show a small confirm panel:

```
Save "Mining Belt" as profile:        [name: Mining____]
  [x] Category: Ores        (12 items)
  [x] Slot class: Battery   (1 item -> as item rule instead? [ ])
  [x] Item: Flashlight
                              [Save profile]  [Cancel]
```

Each line toggleable, generalization per-line reversible (category ⇄ the raw item list).
This is "assemble then capture" that produces *robust* profiles — the bag keeps routing
new ore types it has never seen. Capture also auto-assigns the new profile to that bag.

**O3c. Re-capture / merge.** Capturing a bag that already has a profile offers
**Update** (replace rules), **Merge** (add missing), or **Save as new**. Needed so the
workflow "adjust bag by hand, re-capture" stays one gesture forever.

### O4 — Universal Inventory integration (the important one)

**O4a. Profile Mode — RECOMMENDED (your instinct, endorsed).**
One small button in the window header (next to Sort/the big X — e.g. a drawn tag/label
glyph, no new text). Clicking it puts the whole window into **profile mode**:

- Item cells dim slightly; bag chrome lights up (the mode is unmistakable, like F9's
  editor mode — the mod already trains this "mode reveals editing chrome" pattern).
- Every bag region's manila tab grows a compact strip: **profile name chip** (or
  "no profile"), **[Capture]**, and the chip itself is a dropdown (assign/clear).
- Pinned windows get the same strip in profile mode.
- Esc / the button / closing the window exits the mode. Normal mode shows **zero** new
  buttons — the only always-visible trace is O4b.

**O4b. Passive profile badge (tiny, always on, opt-out).**
A small colored pip or 2–3 letter tag on the manila tab when a bag has a profile
("MIN", "TOOL"). Glanceable "this bag is smart," zero clutter. Config off-switch.

**O4c. Drag-an-item-onto-the-tab = pin rule — RECOMMENDED.**
In profile mode, dropping an item on a bag's manila tab adds an item rule for it to that
bag's profile (creating an unnamed profile if none — named on first capture/save). Direct
manipulation instead of the F10 picker for the 90% case: "THIS goes THERE."
(Outside profile mode, drag keeps meaning *move* — no ambiguity.)

**O4d. Ghost routing hints (dry-run made visible).**
While holding an item over the window in profile mode — or optionally whenever an item is
picked up — the bag that *would* receive it on G glows faintly (router dry-run, O1). This
teaches the system silently and debugs it for free ("why is my coal going in the food
bag" becomes visible instead of a bug report).

**O4e. Profile-aware Sort.**
The existing per-bag Sort button, when the bag has a profile, orders profile-matched items
first (by rule tier), everything else after. A pure view nicety; no routing change.

### O5 — Cross-save portability ("profiles should swap between saves")

Profiles are already global. What's per-save — correctly, since `ReferenceId` is
save-scoped — is the *assignment*. The fix is a portable layer above it:

**O5a. Loadout = worn-structure mapping — RECOMMENDED.**
A **Loadout** is a small global file mapping *bag identity by structure* → profile name:
(worn slot, bag prefab, occurrence index) — "the belt on my waist: Mining", "the first
crate in my backpack: Food". One click **Apply loadout** in any save resolves the mapping
against the *current* worn bags and writes the per-save assignments. Optional auto-apply
toggle ("carry my loadout to every save") that runs on first load when a save has no
assignments file yet. Also fixes **bag replacement** inside one save: re-apply and the new
belt inherits the dead one's profile.

**O5b. Prefab-default assignments (lighter alternative).**
Skip loadouts; make an assignment optionally "all bags of this prefab" instead of "this
bag." Covers "my mining belts are always Mining" globally, but can't distinguish two
backpacks. Could ship as the simple path *inside* O5a's resolver.

**O5c. Export/share.**
"Share profile" button writing a single-profile XML (and import on drop-in). Jackson gets
your setup in one file. Trivial once O5a exists; profiles dir already supports multi-file.

### O6 — F10 tab evolution (Tier 2 stays the power room)

- Keep the tab as-is structurally; it becomes the *editor*, while the Universal Inventory
  is the *assigner/capturer*.
- **Priorities**: expose per-rule priority as a simple High/Normal/Low tri-state (maps to
  the existing ints) instead of hidden constants. Full numeric editing stays XML-only.
- Picker upgrades (from the earlier brainstorm): the item picker gains **category and
  slot-class filter chips + icon grid** view, so "add all the things I mean" is a few
  clicks. (The searchable picker already exists; this is filters + icons on top.)
- A **test box**: type/pick an item, see which profile+bag wins and why (router dry-run
  again) — the F10 twin of O4d.
- Show O2 clearly: an "(implicit — from contents)" row under bags with no profile, so the
  zero-setup tier is discoverable rather than magic.

### O7 — Feedback & explainability

- **Stow toast (debug-lite):** existing SlotFlash stays; optionally, the flashed slot's
  tooltip (or a one-line HUD whisper, config-gated, default off) names the reason:
  "→ Mining Belt (profile: Ores)". Cheap because reasons come from O1.
- **`stowtrace` console command** (read-only, FinderCommands pattern): dumps the full
  candidate table for the item currently held — the MP-safe diagnostic for bug reports.

### O8 — Stow All (flagged, needs a decision, NOT recommended for pass 2)

"Sweep everything loose through the router" is the obvious endgame button, but it
collides with a non-negotiable: **one user action = one message, no bulk loops.** A
Stow-All would either violate that or need a paced queue (one routed move per tick,
execute-time gates re-checked per item, cancel on close). Technically buildable, but it's
a new MP-safety pattern and deserves its own review. Park it; the tiers above remove most
of the need.

---

## 5. Recommended bundle & phasing

**Pass 2a — foundations (invisible):** O1 router + dry-run · O2a content affinity ·
O2b bag-type defaults · O2c memory demotion. *Playable result: stowing feels smart with
zero setup — the demo is "throw ore at G with a half-full mining belt."*

**Pass 2b — the visible feature:** O3b/O3c generalizing capture · O4a profile mode ·
O4b badges · O4c drag-to-pin · O6 priority tri-state + picker filters. *Playable result:
"set up 10 bags in a few clicks" is literally true: fill bags naturally, enter profile
mode, capture-capture-capture, done.*

**Pass 2c — portability + polish:** O5a loadouts (+O5b inside it, +O5c share) ·
O4d ghost hints · O4e profile-aware sort · O7 explainability. *Playable result: new save,
one click, whole system travels.*

Every pass is independently shippable and reversible; nothing changes a mutation path
(the single `OnServer.MoveToSlot` funnel is untouched throughout — the rework is entirely
in *deciding*, never in *moving*).

## 6. Open questions — ANSWERED by FlorpyDorp (2026-07-20)

1. **Profile mode entry point** — header button + manila-tab strips. **No radial** for
   this feature, now or later.
2. **Content affinity default** — **ON out of the box.**
3. Capture **auto-assigns** the profile to the captured bag — **yes.**
4. Loadout auto-apply on virgin saves — **NO. Apply is always a chosen, manual action**
   (a fresh save likely doesn't hold all the loadout's bags yet).
5. Badge style — implementer's choice; going with **short uppercase ASCII tags (2–4
   chars)** on the manila tab, accent-dim, config off-switch (TMP-safe by construction).
6. O8 Stow All — **never. Permanently against the philosophy.**

## 7. API notes (all already verified/shipped in current code)

`SortingClass`, `Slot.Class`, `PrefabName`/`PrefabHash`, `Slot.AllowMove`/`CanMerge` at
execute time, `OnServer.MoveToSlot`, `InventoryScanner.Scan` depth-aware walk,
`XmlSaveLoad.Instance` save keying, and the BeltBindingStore home-slot table. No new game
APIs are required by any option above except reading bag prefab names for O2b/O5a — same
call already used by profiles. Nothing reads server-only state.
