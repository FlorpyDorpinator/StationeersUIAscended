# Smart Stow — Preset Catalog (review document, no code changes)

*2026-08-01. A scan-and-approve catalog for FlorpyDorp: what a Smart Stow **profile** can
actually match today, the game's own category/printer taxonomies pulled from data, and a
proposed "Stationpedia Ascended" (renamed **"Ascended"** on 2026-09-26, see Part C) 9-bag preset set expressed as the same Item/SlotClass/Category
rules the profile system evaluates at runtime. **No code was changed to produce this document.***

**Sources**
- Mod source: `Assets/Scripts/StationeersUIMod/Features/BagProfiles.cs`,
  `Assets/Scripts/StationeersUIMod/Core/StowRouter.cs`.
- Game decompile: `Reference/StationeersGameVersions/Stationeers 8-1-26 V27758 Orbital Update
  Beta/Assembly-CSharp/` (build **0.2.6413.27758**, patch-noted 29/07/2026 — read via the Bash
  tool, since Grep silently skips the git-ignored `Reference/` tree per CLAUDE.md).
- Live game data: `<Stationeers install>/rocketstation_Data/StreamingAssets/Data/*.xml` (printer
  recipes) and `.../StreamingAssets/Language/english.xml` (display names) — this is the same
  0.2.6413.27758 install, i.e. **already current** as of the 2026-07-28/29 update the task asked
  me to caveat against. Nothing here is stale, but a future patch can still add prefabs this
  catalog doesn't know about — re-run the extraction if items feel missing after a game update.
- Every prefab name below is real: pulled from `RecipeData/PrefabName` in the printer XML files,
  cross-referenced against `RecordThing/Key` in `english.xml` for the display name, and (where
  found) against the `SortingClass`/`SlotType` fields serialized into each item's exported
  prefab under `Reference/StationeersGameVersions/AssetRipperFiles/ExportedProject/Assets/
  GameObject/<Prefab>_0.prefab`. No item name is invented.

---

## Part A — What a profile can match today

A `BagProfile` (`Assets/Scripts/StationeersUIMod/Features/BagProfiles.cs:21-55`) is a bag of
three independent rule lists, evaluated every time the router asks "would this bag take this
held item?" (`BagProfile.Match`, same file, lines 38-52):

```
foreach Item rule:      rule.Prefab == thing.PrefabName        -> score = priority + 20000
foreach SlotClass rule: rule.Name (parsed) == thing.SlotType    -> score = priority + 10000
foreach Category rule:  rule.Name (parsed) == thing.SortingClass -> score = priority
return the highest score found (or null = "this bag doesn't want it")
```

The `+20000` / `+10000` offsets are a hard **tier order**, not a suggestion: any matching Item
rule always beats any matching SlotClass rule, which always beats any matching Category rule,
regardless of the priority numbers inside each tier. Priority itself is edited in the F10 UI as
a 3-step tri-state — **Low = 10, Normal = 50, High = 100** (`BagProfiles.cs:111-160`,
`RuleTiers`) — full numeric control stays available by hand-editing the profile XML.

### A1 — Mechanism 1: explicit item match (`<Item prefab="..." priority=".."/>`)

Exact `PrefabName` string equality. This is the only mechanism that can single out one item
regardless of what category or slot class it shares with siblings — e.g. the shipped starter
"Farming" profile pins `ItemFertilizer` explicitly (`BagProfiles.cs:792`) because Fertilizer's
own `SortingClass` is the `Default` junk bucket and its `Slot.Class` (`Plant`) is shared with
every crop and seed in the game. Always wins ties against SlotClass/Category rules for the same
bag, and is the only way to split the ~224-item `Kits` `SortingClass` bucket by hand (see A2).

### A2 — Mechanism 2: `Slot.Class` (`<SlotClass name="..." priority=".."/>`)

The physical socket type an item occupies. Verified enum, 44 values, `ushort`-backed
(`Reference/.../Assembly-CSharp/Assets/Scripts/Objects/Slot.cs:1176-1309`). Two values have
**zero items using them** in this build (dead/reserved — flagged so nobody chases a ghost):
`Torpedo`, `Magazine`, `Circuit`, `Blocked`, `RocketPayload`, `AutoInjector`, `Entity`,
`DirtCanister` also has zero *direct* users (the item named "Dirt Canister" actually reports
`SlotType=Ore`, not `Slot.Class.DirtCanister` — a real trap, see the ⚠ in Part C6).

| Slot.Class | Example items (real prefabs) |
|---|---|
| `None` | generic — anything with no dedicated socket: `ItemIronFrames` (Iron Frames), `ItemPlainCake` (Cake), most `ItemKit*` build kits |
| `Helmet` | `ItemHardHat` (Hard Hat), `ItemSpaceHelmet` (Space Helmet), `ItemGasMask` (Gas Mask) |
| `Suit` | `ItemEvaSuit` (Eva Suit), `ItemHardSuit` (Hardsuit), `ItemSuitSpace` (Space Suit) |
| `Back` | `ItemMiningBackPack` (Mining Backpack), `ItemJetpackBasic` (Jetpack Basic), `ItemHardJetpack` (Hardsuit Jetpack) |
| `GasFilter` | `ItemGasFilterOxygen` (Filter Oxygen), `ItemGasFilterNitrogenL` (Heavy Filter Nitrogen), `ItemGasFilterCarbonDioxideInfinite` (Catalytic Filter CO2) |
| `GasCanister` | `ItemGasCanisterEmpty` (Canister), `ItemGasCanisterSmart` (Gas Canister Smart), `ItemHighVolumeGasCanisterEmpty` (High Volume Gas Canister) |
| `Motherboard` | `MotherboardLogic` (Logic Motherboard), `MotherboardComms` (Communications Motherboard), `MotherboardMap` (Map Motherboard) |
| `Circuitboard` | `CircuitboardAirlockControl` (Airlock), `CircuitboardAirControl` (Air Control), `CircuitboardGasDisplay` (Gas Display) |
| `DataDisk` | `ItemDataDisk` (Data Disk) — only one prefab uses this class |
| `Organ` | `OrganLungs` (Human Lungs), `OrganLungsZrilian` (Lungs), `OrganLungsChicken` (Organ Lungs Chicken) |
| `Ore` | `ItemIronOre` (Ore Iron), `ItemCharcoal` (Charcoal), `ItemIce` (Ice Water) — note: **all Ices also use `Slot.Class.Ore`**, not a dedicated ice class |
| `Plant` | `Fertilizer`, `ItemAlienMushroom` (Alien Mushroom), `SeedBag_Potato` (Potato Seeds) |
| `Uniform` | `UniformMarine` (Marine Uniform), `ItemClothingBagOveralls_US` (Overalls US), `UniformCommander` (Uniform Commander) |
| `Entity` | *(0 items in this build)* |
| `Battery` | `ItemBatteryCell` (Battery Cell Small), `ItemBatteryCellLarge` (Large), `ItemBatteryCellNuclear` (Nuclear) |
| `Egg` | `ItemEgg` (Egg), `ItemFertilizedEgg` (Fertilized Egg) |
| `Belt` | `ItemToolBelt` (Tool Belt), `ItemMiningBelt` (Mining Belt), `ItemMkIIToolbelt` (Tool Belt MK II) |
| `Tool` | `ItemWrench` (Wrench), `ItemWeldingTorch` (Welding Torch), **`ItemCableCoil`** (Cable Coil) ⚠ — the game classifies cable as a *tool* |
| `Appliance` | `ApplianceMicrowave` (Microwave), `ApplianceChemistryStation` (Chemistry Station), `ApplianceBobbleHeadMarine` (Bobble Head) |
| `Ingot` | `ItemIronIngot`, `ItemSteelIngot`, `ItemAstroloyIngot` |
| `Torpedo` | *(0 items in this build)* |
| `Cartridge` | `CartridgeAtmosAnalyser` (Atmos Analyzer), `CartridgeOreScannerColor`, `CartridgeTracker` |
| `AccessCard` | `AccessCardBlue`, `AccessCardRed`, `AccessCardBlack` (12 colors total) |
| `Magazine` | *(0 items in this build)* |
| `Circuit` | *(0 items in this build — distinct dead value from `Circuitboard`/`ProgrammableChip`)* |
| `Bottle` | `ItemHandSanitizer`, `ItemSprayCanBlack` / `ItemSprayCanBlue` (spray paint) |
| `ProgrammableChip` | `ItemIntegratedCircuit10` (IC10) — only one prefab |
| `Glasses` | `ItemGlasses`, `ItemNVG` (Night Vision Goggles), `ItemSensorLenses` |
| `CreditCard` | `ItemCreditCard` — only one prefab |
| `DirtCanister` | *(0 direct items — see the ⚠ above)* |
| `SensorProcessingUnit` | `ItemSensorProcessingUnitOreScanner`, `...CelestialScanner`, `...MesonScanner` |
| `LiquidCanister` | `ItemLiquidCanisterEmpty`, `ItemLiquidCanisterSmart` |
| `LiquidBottle` | `ItemWaterBottle` — only one prefab |
| `Wreckage` | ~60 `ItemWreckage*` debris prefabs (not player-stowable content; excluded from Parts B-E) |
| `SoundCartridge` | `ItemSoundCartridgeBass`, `...Drums`, `...Synth` |
| `DrillHead` | `ItemRocketMiningDrillHead` (Basic), `...HeadIce`, `...HeadDurable` |
| `ScanningHead` | `ItemRocketScanningHead`, `ItemRocketDeepScanningHead` |
| `Flare` | `ItemRoadFlare` — only one prefab |
| `Blocked` | *(0 items in this build)* |
| `SuitMod` | `ItemSuitModCryogenicUpgrade` — only one prefab |
| `Crate` | `CrateMkII`, `DynamicCrate` |
| `Portables` | `DynamicGasCanisterEmpty` (Portable Gas Tank), `DynamicGasTankAdvanced` (Mk II), `DynamicLiquidCanisterEmpty` |
| `RocketPayload` | *(0 direct items — used for the payload-bay socket, not a held-item type)* |
| `AutoInjector` | *(0 items in this build)* |

### A3 — Mechanism 3: `SortingClass` category (`<Category name="..." priority=".."/>`)

The coarse, hard-authored, one-per-prefab category. Verified enum, **only 11 values**
(`Reference/.../Assembly-CSharp/Assets/Scripts/Objects/SortingClass.cs:6-30`):

| SortingClass | Count seen¹ | Example items |
|---|---|---|
| `Default` | 210 | the junk-drawer bucket: `AccessCardBlue`, `ItemBatteryCell`, `MotherboardLogic`, most Cartridges/Circuitboards/Motherboards, all Wreckage |
| `Kits` | 224 | almost every `ItemKit*` construction/device kit: `ItemKitWall` (Wall), `ItemKitAirlock` (Airlock), `ItemKitComputer` (Computer) — see Part D's printer tables for the full roster |
| `Tools` | 51 | `ItemWrench`, `ItemPickaxe`, `ItemWeldingTorch`, `ItemTablet` |
| `Resources` | 66 | ingots, cable coils, gas filters, `ItemMilk`, `ItemSugar`, `ItemPeaceLily` |
| `Food` | 34 | cooked dishes, canned goods, `ItemPillHeal`/`ItemPillStun` |
| `Clothing` | 57 | suits, helmets, uniforms, backpacks, belts, jetpacks |
| `Appliances` | 14 | `ApplianceMicrowave`, `ApplianceChemistryStation`, bobbleheads |
| `Atmospherics` | 12 | canisters, portable AC/scrubber/generator/hydroponics |
| `Storage` | 13 | cardboard boxes, crates, supply packages |
| `Ores` | 15 | raw ores, `ItemCharcoal`, `ItemDirtyOre`, `ItemReagentMix` |
| `Ices` | 21 | `ItemIce` (Water), `ItemNitrice`, `ItemOxite`, all `ItemPureIce*` variants |

¹ Counts are from the ~717 prefabs whose exported prefab yaml carried a `SortingClass` field in
the AssetRipper dump; see the caveat under Part B.

**Why `SortingClass` alone is a trap** (already documented and shipped-around in
`Documentation/SmartStow-Default-Routing.md` §2 — this catalog reuses that analysis): a battery
cell, an access card, and fertilizer are all `SortingClass=Default`; a cable coil is
`SortingClass=Resources` but `Slot.Class=Tool` (so a category-only Electrical bag misses it,
and an unguarded Tools bag steals it onto your toolbelt); every wall, frame, door, and kit — 224
of them — share `SortingClass=Kits` with `Slot.Class=None`, so **Category rules cannot tell a
wall kit from a rocket-engine kit**; only an explicit `Item` rule can.

---

## Part B — What a profile can match today, by category

One table per `SortingClass`, item counts from the same 717-prefab data pull. Two categories
(`Kits` at 224 and `Default` at 210) are too large to enumerate row-by-row here per the "list
the pattern + count" instruction — they're fully enumerated already, split by which fabricator
prints them, in **Part D**.

**Caveat on completeness**: `SortingClass`/`Slot.Class` are only readable from the game's
serialized prefab data (not from decompiled C# — both fields are plain `public` values on
`DynamicThing`, set per-prefab in the Unity inspector, `Reference/.../DynamicThing.cs:3588`).
54 real, verified prefabs (mostly the four raw sheet types, the plain/heavy/medium gas-filter
family for 6 of the rarer gases, and several Rocket-Manufactory crew-module kits) did **not**
carry a `SortingClass` in the AssetRipper export I could read — likely because their asset was
exported without the outline sub-object AssetRipper needs, not because the item doesn't have a
category in-game. Those are marked "inferred" below/in Part C rather than omitted.

### Atmospherics (12)
| Prefab | Display name |
|---|---|
| `ItemGasCanisterEmpty` | Canister |
| `ItemGasCanisterSmart` | Gas Canister (Smart) |
| `ItemHighVolumeGasCanisterEmpty` | High Volume Gas Canister |
| `ItemLiquidCanisterEmpty` | Liquid Canister |
| `ItemLiquidCanisterSmart` | Liquid Canister (Smart) |
| `DynamicAirConditioner` | Portable Air Conditioner |
| `DynamicScrubber` | Portable Air Scrubber |
| `DynamicGenerator` | Portable Generator |
| `DynamicHydroponics` | Portable Hydroponics |
| `DynamicLiquidCanisterEmpty` | Portable Liquid Tank |
| `DynamicMKIILiquidCanisterEmpty` | Portable Liquid Tank Mk II |
| `DynamicMKIILiquidCanisterWater` | Portable Liquid Tank Mk II (Water) |

### Ores (15)
`ItemCharcoal` (Charcoal), `ItemDirtyOre`/`ItemSpaceOre` (Dirty Ore ×2 prefabs, same name),
`ItemCoalOre`, `ItemCobaltOre`, `ItemCopperOre`, `ItemGoldOre`, `ItemIronOre`, `ItemLeadOre`,
`ItemNickelOre`, `ItemSiliconOre`, `ItemSilverOre`, `ItemUraniumOre`, `ItemReagentMix`,
`ItemSpaceIce`.

### Ices (21)
Pattern: `ItemPureIce*` (14 gas/liquid ices) + 6 standalone named ices + `ItemPureIce` (plain
water). Full list: `ItemNitrice`, `ItemOxite`, `ItemVolatiles`, `ItemIce` (Water),
`ItemPureIceCarbonDioxide`, `ItemPureIceHydrogen`, `ItemPureIceLiquidCarbonDioxide`,
`ItemPureIceLiquidHydrogen`, `ItemPureIceLiquidVolatiles`, `ItemPureIceLiquidNitrogen`,
`ItemPureIceLiquidNitrous`, `ItemPureIceLiquidOxygen`, `ItemPureIceLiquidPollutant`,
`ItemPureIceVolatiles`, `ItemPureIceNitrogen`, `ItemPureIceNitrous`, `ItemPureIceOxygen`,
`ItemPureIcePollutant`, `ItemPureIcePollutedWater`, `ItemPureIceSteam`, `ItemPureIce` (Water).

### Storage (13)
`ItemBurgerBox`, `CardboardBox`, `ItemCerealBarBag`, `ItemCerealBarBox`, `CrateMkII`,
`DynamicCrate`, `ItemEggCarton`, `CardboardBoxLarge`, `ItemMiningPackage`,
`ItemPortablesPackage`, `ItemResidentialPackage`, `ItemWaterBottleBag`,
`ItemWaterBottlePackage`.

### Appliances (14)
`ApplianceDeskLampLeft`/`Right`, `ApplianceSeedTray`, `AppliancePackagingMachine`, 3×
`ApplianceBobbleHead*`, `ApplianceChemistryStation`, `ApplianceMicrowave`,
`AppliancePlantGeneticAnalyzer`/`Splicer`/`Stabilizer`, `ApplianceReagentProcessor`,
`ApplianceTabletDock`.

### Food (34), Clothing (57), Tools (51), Resources (66), Kits (224), Default (210)
Enumerated fully across the printer tables in **Part D** (these six categories are exactly what
the fabricators mostly produce) rather than repeated here — see Part D for the complete,
citation-backed roster. Representative examples pulled forward for scanning:
- **Food**: `ItemBreadLoaf`, `ItemMuffin`, `ItemPillHeal`, `ItemCannedMushroom`.
- **Clothing**: `ItemHardSuit`, `ItemMiningBelt`, `UniformMarine`, `ItemJetpackBasic`.
- **Tools**: `ItemWrench`, `ItemPickaxe`, `ItemLaptop`, `ItemMiningDrill`.
- **Resources**: `ItemIronIngot`, `ItemCableCoil`, `ItemGasFilterOxygen`, `ItemSugar`.
- **Kits**: `ItemKitWall`, `ItemKitAirlock`, `ItemKitLogicCircuit`, `ItemKitRocketBattery`.
- **Default**: `ItemBatteryCell`, `AccessCardBlue`, `CircuitboardAirlockControl`, `ItemDataDisk`.

---

## Part C — FlorpyDorp's set ("Ascended", 9 bags)

> **Renamed 2026-09-26.** This is FlorpyDorp's "Stationpedia Ascended" layout. It shipped under
> that name from B4 (2026-08-02) until FlorpyDorp renamed it **"Ascended"**, because the long name
> was cut off by an ellipsis in the F10 layout cards. The content (C1-C9 below) is unchanged, so
> `ShippedStowProfiles.Revision` did not move. Existing installs are carried over once by
> `StowProfileStore.MigrateRenamedShipped`: the player's copy, including any edits, is renamed in
> place (both the file and its internal name), `.active` is re-pointed, and the `.shipped` marker
> entry moves to the new name. If a player already owns an "Ascended", nothing is renamed and the
> old layout stays as one of their own. The C# builder is `ShippedStowProfiles.BuildAscended`, and
> `ShippedStowProfiles.LegacyAscendedName` records the old name.

Every bag below is written as the actual `<Item>`/`<SlotClass>`/`<Category>` rule XML the
profile system evaluates (Part A's mechanism). It ships as `StowProfiles/Ascended.xml`
(originally proposed as `Profiles/Stationpedia Ascended.xml`, before the B2 Stow Profile model). Priorities use the F10 tri-state (Low/Normal/High =
10/50/100); I bumped a few above the shipped defaults where two of these 9 bags could otherwise
tie. **⚠ = judgment call** — flagged so FlorpyDorp can veto fast; top 10 are called out at the
end of this section for the report-back.

### C1 — Paints
```xml
<BagProfile name="Paints">
  <Item prefab="ItemSprayCanBlack" priority="100"/>
  <Item prefab="ItemSprayCanBlue" priority="100"/>
  <Item prefab="ItemSprayCanMetallicBronze" priority="100"/>
  <Item prefab="ItemSprayCanBrown" priority="100"/>
  <Item prefab="ItemSprayCanMetallicGold" priority="100"/>
  <Item prefab="ItemSprayCanGreen" priority="100"/>
  <Item prefab="ItemSprayCanGrey" priority="100"/>
  <Item prefab="ItemSprayCanKhaki" priority="100"/>
  <Item prefab="ItemSprayCanMetallicObsidian" priority="100"/>
  <Item prefab="ItemSprayCanOrange" priority="100"/>
  <Item prefab="ItemSprayCanPink" priority="100"/>
  <Item prefab="ItemSprayCanPurple" priority="100"/>
  <Item prefab="ItemSprayCanRed" priority="100"/>
  <Item prefab="ItemSprayCanMetallicSilver" priority="100"/>
  <Item prefab="ItemSprayCanWhite" priority="100"/>
  <Item prefab="ItemSprayCanYellow" priority="100"/>
</BagProfile>
```
16 items — every spray-paint color the Tool Manufactory prints (12 plain + 4 metallic). Item
rules only: all 16 share `Slot.Class=Bottle` with `ItemHandSanitizer`, so a SlotClass rule would
also catch sanitizer — explicit prefabs avoid that. **⚠** Paint-mixer reagent ingredients
(`SoyOil`, the `ReagentColor*` dyes) aren't real inventory items — `SoyOil` is a `Bottle`-class
food item also produced by the Reagent Grinder, and `ReagentColor*` are pure Reagents with no
`Slot.Class` at all — neither can be "stowed", so neither belongs in this bag or any other.

### C2 — Materials (sheets)
```xml
<BagProfile name="Materials">
  <Item prefab="ItemIronSheets" priority="100"/>
  <Item prefab="ItemSteelSheets" priority="100"/>
  <Item prefab="ItemPlasticSheets" priority="100"/>
  <Item prefab="ItemGlassSheets" priority="100"/>
  <Item prefab="ItemStelliteGlassSheets" priority="100"/>
  <Item prefab="ItemAstroloySheets" priority="100"/>
</BagProfile>
```
6 items — every sheet material the Autolathe prints. **⚠** All 6 are among the 54 prefabs with
no readable `SortingClass` in this data pull (see Part B's caveat) — item rules sidestep that
gap entirely, but if FlorpyDorp later wants a `Category` fallback for future sheet types, it
can't be added with confidence until someone confirms their real `SortingClass` in-game.

### C3 — Frames + Walls (incl. kits for frames, walls, windows)
```xml
<BagProfile name="Frames and Walls">
  <Item prefab="ItemIronFrames" priority="100"/>
  <Item prefab="ItemSteelFrames" priority="100"/>
  <Item prefab="ItemKitWallFlat" priority="100"/>
  <Item prefab="ItemKitWallArch" priority="100"/>
  <Item prefab="ItemKitWallGeometry" priority="100"/>
  <Item prefab="ItemKitWallIron" priority="100"/>
  <Item prefab="ItemKitWallPadded" priority="100"/>
  <Item prefab="ItemKitWall" priority="100"/>
  <Item prefab="ItemKitCompositeCladding" priority="100"/>
  <Item prefab="ItemKitReinforcedWindows" priority="100"/>
  <Item prefab="ItemKitWindowShutter" priority="100"/>
  <Item prefab="ItemKitCompositeFloorGrating" priority="100"/>
  <Item prefab="ItemKitDoor" priority="100"/>
  <Item prefab="ItemKitInteriorDoors" priority="100"/>
  <Item prefab="ItemKitBlastDoor" priority="100"/>
  <Item prefab="ItemKitAirlock" priority="100"/>
  <Item prefab="ItemKitAirlockGate" priority="100"/>
  <Item prefab="ItemKitRailing" priority="100"/>
  <Item prefab="ItemKitLadder" priority="100"/>
  <Item prefab="ItemKitStairs" priority="100"/>
  <Item prefab="ItemKitStairwell" priority="100"/>
</BagProfile>
```
21 items. **This is the mechanism's sharpest limitation for this bag**: every one of these is
`Slot.Class=None` + `SortingClass=Kits`, *identical* to a rocket-engine kit or a satellite-dish
kit — there is no rule shape other than "list every prefab by hand" that can isolate "wall-ish
kits" from the other 200+ Kits items. **⚠** Doors/airlocks/stairs/ladders/railings are included
here as "things that go in a wall opening or connect floors" — FlorpyDorp's brief said "frames,
walls, windows" specifically; if doors and airlocks should be Misc instead, that's an 8-item
trim, not a redesign.

### C4 — Ingots + Ores
```xml
<BagProfile name="Ingots and Ores">
  <SlotClass name="Ingot" priority="60"/>
  <SlotClass name="Ore" priority="60"/>
  <Category name="Ores" priority="50"/>
  <Category name="Ices" priority="50"/>
</BagProfile>
```
Rule-based, not item-listed — `Slot.Class=Ingot` (17 items: every base + superalloy ingot) and
`Slot.Class=Ore` (39 items: raw ores, charcoal, biomass, solid fuel, dirty ore, space ice, all
21 Ices — since Ices also report `Slot.Class=Ore`, see Part A2) cover the whole mined/smelted
material chain without listing a single prefab. **⚠** Two consequences worth a fast veto:
- `Slot.Class=Ore` also catches `ItemDirtCanister` (Dirt Canister) — its `Slot.Class` is
  literally `Ore`, *not* `Slot.Class.DirtCanister` (which has zero real users, Part A2). If
  Dirt Canister should live with the other canisters instead, it needs an explicit `Item`
  exception in **both** this bag (excluding it isn't expressible — profiles have no "not" rule)
  and a higher-priority `Item` rule in C8 (Canisters) to win the tie.
- Ices are grouped here (mined, solid) rather than in C6 Liquids & Gases (their eventual
  melted state) — a defensible either-or, flagged for veto.

### C5 — Electronics (circuit boards, chips, cartridges, batteries)
```xml
<BagProfile name="Electronics">
  <SlotClass name="Circuitboard" priority="60"/>
  <SlotClass name="Motherboard" priority="60"/>
  <SlotClass name="Cartridge" priority="60"/>
  <SlotClass name="ProgrammableChip" priority="60"/>
  <SlotClass name="DataDisk" priority="60"/>
  <SlotClass name="SoundCartridge" priority="60"/>
  <SlotClass name="SensorProcessingUnit" priority="60"/>
  <SlotClass name="Battery" priority="60"/>
</BagProfile>
```
Rule-based: 6+10+10+1+1+4+3+6 = 41 items covered by 8 `SlotClass` rules, zero prefabs listed.
**⚠** Battery is FlorpyDorp's own flagged question — included here because
`StowRouter.IsFunctionalSocketClass` (`Core/StowRouter.cs:346-366`) already treats `Battery` as
a functional-socket item alongside `Cartridge`/`Circuitboard`/`Motherboard`/`ProgrammableChip`,
so grouping it with electronics matches how the router already privileges it over generic bag
slots. If FlorpyDorp wants batteries in Misc instead, delete one line. **Not** included: the
~15 electronics/logic *device kits* (`ItemKitComputer`, `ItemKitSensor`, `ItemKitBattery`,
`ItemKitLogicCircuit`, `ItemCableAnalyser`, ...) — those are `SortingClass=Kits` like every
other kit and can only be added by listing each one explicitly (same limitation as C3). Left
for Misc by default; say the word and I'll add the ~15-item exception list.

### C6 — Liquids & Gases
```xml
<BagProfile name="Liquids and Gases">
  <SlotClass name="GasFilter" priority="60"/>
  <SlotClass name="LiquidBottle" priority="60"/>
</BagProfile>
```
**⚠ This is the bag the brief asked me to clarify against Canisters — here is the split I
propose:** "Liquids & Gases" = atmospheric **consumables** that are not themselves a container
— all 34 gas filters (`Slot.Class=GasFilter`: plain/heavy/medium/catalytic × 7-8 gas types) and
the one `LiquidBottle` (`ItemWaterBottle`). "Canisters" (C8, below) = the **container hardware**
itself, gas or liquid alike. Rationale: a filter goes *inside* a canister/suit socket the same
way a battery goes inside a tool — it's consumed, not carried empty — while a canister is the
vessel you're moving between bags. If FlorpyDorp instead wants this bag to mean "bottled/loose
liquids and gases" (e.g. `ItemMilk`, `ItemHandSanitizer`), that's a different and smaller set —
flag which reading is intended.

### C7 — Cables & Pipes
```xml
<BagProfile name="Cables and Pipes">
  <Item prefab="ItemCableCoil" priority="100"/>
  <Item prefab="ItemCableCoilHeavy" priority="100"/>
  <Item prefab="ItemCableCoilSuperHeavy" priority="100"/>
  <Item prefab="ItemKitPipe" priority="100"/>
  <Item prefab="ItemKitPipeLiquid" priority="100"/>
  <Item prefab="ItemKitInsulatedPipe" priority="100"/>
  <Item prefab="ItemKitInsulatedLiquidPipe" priority="100"/>
  <Item prefab="ItemKitInsulatedPipeUtility" priority="100"/>
  <Item prefab="ItemKitInsulatedPipeUtilityLiquid" priority="100"/>
  <Item prefab="ItemKitPipeUtility" priority="100"/>
  <Item prefab="ItemKitPipeUtilityLiquid" priority="100"/>
  <Item prefab="ItemKitPipeOrgan" priority="100"/>
  <Item prefab="ItemKitPipeRadiator" priority="100"/>
  <Item prefab="ItemKitPipeRadiatorLiquid" priority="100"/>
  <Item prefab="ItemPipeDigitalValve" priority="100"/>
  <Item prefab="ItemWaterPipeDigitalValve" priority="100"/>
  <Item prefab="ItemPipeGasMixer" priority="100"/>
  <Item prefab="ItemPipeValve" priority="100"/>
  <Item prefab="ItemLiquidPipeValve" priority="100"/>
  <Item prefab="ItemPipeMeter" priority="100"/>
  <Item prefab="ItemWaterPipeMeter" priority="100"/>
  <Item prefab="ItemPipeAnalyizer" priority="100"/>
  <Item prefab="ItemLiquidPipeAnalyzer" priority="100"/>
  <Item prefab="ItemPipeIgniter" priority="100"/>
  <Item prefab="ItemPipeLabel" priority="100"/>
  <Item prefab="ItemPipeCowl" priority="100"/>
  <Item prefab="ItemPipeHeater" priority="100"/>
  <Item prefab="ItemLiquidPipeHeater" priority="100"/>
  <Item prefab="ItemPipeVolumePump" priority="100"/>
  <Item prefab="ItemLiquidPipeVolumePump" priority="100"/>
  <Item prefab="ItemKitTurboVolumePump" priority="100"/>
  <Item prefab="ItemKitLiquidTurboVolumePump" priority="100"/>
</BagProfile>
```
32 items — the 3 cable coils (`Slot.Class=Tool`, so they need an explicit exception exactly like
the existing shipped "Electrical" starter profile already does for the same reason — precedent
in `BagProfiles.cs:776-777`) plus every pipe-family kit/hardware item, identified by the
`ItemPipe*` / `ItemLiquidPipe*` / `ItemWaterPipe*` / `ItemKit*Pipe*` naming pattern. **⚠**
`ItemKitTank`/`ItemKitLiquidTank`/insulated tank kits are deliberately **excluded** here — they
route to C8 Canisters instead (a tank is a container, not a pipe run); double-check that split
matches intent. Not included: `ItemHydroponicTray`/`ItemKitHydroponicStation` etc. even though
they're Hydraulic-Pipe-Bender output — those are farming devices, not pipe runs, and fall to
Misc.

### C8 — Canisters
```xml
<BagProfile name="Canisters">
  <SlotClass name="GasCanister" priority="60"/>
  <SlotClass name="LiquidCanister" priority="60"/>
  <SlotClass name="Portables" priority="60"/>
  <Item prefab="ItemKitTank" priority="100"/>
  <Item prefab="ItemKitLiquidTank" priority="100"/>
  <Item prefab="ItemKitTankInsulated" priority="100"/>
  <Item prefab="ItemKitLiquidTankInsulated" priority="100"/>
  <Item prefab="ItemKitDynamicCanister" priority="100"/>
  <Item prefab="ItemKitDynamicGasTankAdvanced" priority="100"/>
  <Item prefab="ItemKitDynamicLiquidCanister" priority="100"/>
  <Item prefab="ItemKitDynamicMKIILiquidCanister" priority="100"/>
</BagProfile>
```
13 items — 5 via `SlotClass=GasCanister`/`LiquidCanister` (empty + smart, gas + liquid, plus the
high-volume canister), 5 via `SlotClass=Portables` (the deployed dynamic tank items, distinct
prefabs from their build kits), 8 via explicit tank/canister-kit exceptions (same "kits can only
be split by listing" limitation as C3/C5). **⚠** `DynamicAirConditioner` / `DynamicScrubber` /
`DynamicGenerator` / `DynamicHydroponics` are `SortingClass=Atmospherics` like canisters but
`Slot.Class=None` — they will **not** match this bag's rules (or C6's) and fall through to
Misc. If FlorpyDorp wants portable *devices* (not just tanks) grouped here, they need their own
4 item exceptions.

### C9 — Misc (everything else)
```xml
<BagProfile name="Misc">
  <Category name="Default" priority="10"/>
  <Category name="Tools" priority="10"/>
  <Category name="Food" priority="10"/>
  <Category name="Clothing" priority="10"/>
  <Category name="Appliances" priority="10"/>
  <Category name="Storage" priority="10"/>
  <Category name="Resources" priority="10"/>
  <Category name="Kits" priority="10"/>
</BagProfile>
```
This is the **structural catch-all**, not a curated list: every `SortingClass` value except
`Atmospherics`/`Ores`/`Ices` (claimed above), all at Low priority so any of C1-C8's Item/SlotClass
rules — which carry the `+20000`/`+10000` tier bonus — always win the tie for anything they
specifically claim. What lands here in practice: every tool not caught by C7 (wrenches, drills,
welding torches, tablets — `SortingClass=Tools`), all food, all suits/helmets/uniforms/belts/
glasses (`SortingClass=Clothing`), stray appliances/bobbleheads, storage boxes/crates, the
Resources leftovers not claimed by higher-tier rules elsewhere (milk, cheese, eggs, cocoa
powder, sugar, soy oil, live plants), **and the ~200 non-wall/non-electronics/non-tank Kits** —
computer/console/sensor/satellite-dish/rocket-part/LArRE/atmospherics-device kits, all of it,
because nothing else claims them. **⚠ this makes Misc large by construction** (roughly
250-300 items land here once C1-C8's specific claims are subtracted) — that's the honest
consequence of only having one generic bag in a 9-bag set built mostly around raw materials.
Seeds specifically: seed items are `Slot.Class=Plant`, not claimed by any of C1-C8, so they land
in Misc via `Category=Food` or `Category=Resources` depending on the seed — consistent with the
brief listing "seeds" under Misc.

### Top 10 judgment calls (fast-veto list)

1. **Battery → Electronics** (C5) — could go to Misc instead; router precedent favors Electronics.
2. **Liquids & Gases vs Canisters split** (C6/C8) — filters+bottle vs container hardware; confirm this is the intended reading, not "loose liquids like Milk."
3. **Ices → Ingots+Ores, not Liquids & Gases** (C4) — mined solid state vs eventual melted product.
4. **Dirt Canister → Ingots+Ores** (C4) — its `Slot.Class` is `Ore`, not a canister class; will NOT land in C8 without a manual exception.
5. **Doors/airlocks/stairs/railings/ladders → Frames+Walls** (C3) — brief said "frames, walls, windows"; these 8 items are an inference, easy to trim to Misc.
6. **Electronics/logic device *kits* → Misc, not Electronics** (C5) — Kit Computer, Kit Sensor, Kit Battery, Kit Logic Circuit etc. are indistinguishable from any other kit by rule; excluded by default, ~15-item exception list available if wanted.
7. **Portable AC/Scrubber/Generator/Hydroponics → Misc, not Canisters** (C8) — `Slot.Class=None` despite being "Atmospherics"-category Portables-adjacent devices.
8. **Cable coil "primary" bag = Cables & Pipes, never Electronics** — the coil's own `Slot.Class=Tool` trap means it needs the explicit exception either way; confirm C7 (not C5) is where FlorpyDorp wants it.
9. **6 sheet materials (C2) have no confirmed `SortingClass`** in this data pull — item-listed so it doesn't matter for matching, but flag if a *new* sheet type ships later (it won't auto-join Materials).
10. **Misc absorbs ~200 kit items** (C9) — if that's too broad in practice, the fix is a 10th bag ("Devices"/"Machines") splitting Kits further, not a rule change to the existing 9.

---

## Part D — The by-printer preset set

One table per fabricator, prefab + display name, pulled from
`<Stationeers install>/rocketstation_Data/StreamingAssets/Data/*.xml`
(`RecipeData/PrefabName`, cross-referenced to `Data/RecipeType.cs`,
`Reference/.../Assembly-CSharp/RecipeType.cs:4-50`, for the printer roster). Items produced by
more than one printer are listed once under their **primary** (most-specific) printer, with a
"⚠ dup" marker in the other table(s) it also appears in — the resolution table below explains
each call.

### Multi-printer items (primary marked)

| Item | Primary | Also produced by | Why |
|---|---|---|---|
| `ItemCableCoil` (Cable Coil) | Autolathe | Electronics Printer | Autolathe is the earliest-available fabricator |
| `ItemEvaSuit` (Eva Suit) | Tool Manufactory | Autolathe | suits are Tool Manufactory's specialty |
| `ItemSpaceHelmet` (Space Helmet) | Tool Manufactory | Autolathe | same |
| `ItemExplosive` (Demolition Charge) | Security Printer | Tool Manufactory | explosives are Security Printer's specialty |
| `ItemMiningCharge` (Mining Charge) | Security Printer | Tool Manufactory | same |
| `ItemIronFrames` (Iron Frames) | Autolathe | Terraforming Manufactory | Autolathe is the general/base source |
| `ItemKitAccessBridge`, `ItemKitStairwell` | Autolathe | Rocket Manufactory | general construction over launch-site-only context |
| `ItemKitChute`, `ItemKitPipe`, `ItemKitStandardChute` | Hydraulic Pipe Bender | Autolathe | pipe/chute family specialty |
| `ItemLabeller` (Labeller) | Tool Manufactory | Electronics Printer | it's a handheld tool |
| `ItemWallLight` (Kit Lights) | Electronics Printer | Autolathe | lighting/logic device specialty |
| all 19 Automated-Oven dishes | Microwave | Automated Oven | Microwave is the base cooking appliance — **Automated Oven has zero exclusive outputs**, it duplicates the Microwave's entire recipe list |
| 7 Arc-Furnace base metals + Charcoal | Furnace | Arc Furnace, (Charcoal also Centrifuge) | Furnace is the base smelter — **Arc Furnace has zero exclusive outputs** either |

### Autolathe (70)
The general-purpose starter fabricator. Full roster: `ItemAstroloySheets`, 3×
`ApplianceBobbleHead*`, `ItemBurgerBox`, `ItemCableCoil`⭐, `CardboardBox`, `ItemCoffeeMug`,
`ItemEggCarton`, `ItemEmptyCan`, `ItemEvaSuit`⚠dup, `ItemGlassSheets`, `ItemIronFrames`⭐,
`ItemIronSheets`, and 45 `ItemKit*`/`KitStructure*` structural kits (Access Bridge, Arc Furnace,
Arched Wall, Autolathe, Basic Chutes, Beds, Blast Door, Centrifuge, Chairs, Cladding, Combustion
Centrifuge, Combustion Deep Miner, Composite Window Shutter, Container Mount, Crate Mk II,
Crate, Deep Miner, Door, Electronics Printer, Flat Wall, Floor Grating, Furnace, Furniture,
Geometric Wall, Hydraulic Pipe Bender, Interior Doors, Iron Wall, Ladder, Lights⚠dup, Linear
Rail Door, Locker, ODA Flag, Padded Wall, Pipe⚠dup, Powered Chutes⚠dup, Railing, Recycler,
Reinforced Walls, Rocket Manufactory, SDB Hopper, SDB Silo, Security Printer, Sign, Sorter,
Stacker, Stairs, Stairwell⚠dup, Tables, Tool Manufactory, Wall), plus `CardboardBoxLarge`,
`ItemPlasticSheets`, `ItemSpaceHelmet`⚠dup, `ItemSteelFrames`, `ItemSteelSheets`,
`ItemStelliteGlassSheets`.

### Electronics Printer (129)
Circuit boards, motherboards, cartridges, batteries, sound cartridges, tablets, and ~85
device/appliance kits (computer, console, sensors, satellite dishes, solar panels, hydroponics,
LArRE robotic-arm docks, logic I/O/memory/processor/switch/transmitter, vending machines,
weather station, wind turbines, and more). Includes `ItemWallLight`⭐ (Kit Lights) and
`DynamicLight` (Portable Light, sourced from `DynamicObjectsFabricator.xml`). Full roster is
129 rows — see `electronics.xml::ElectronicsPrinterRecipes` for the authoritative list; notable
names: `ItemBatteryCellLarge`/`Nuclear`/plain, `ItemCableCoilHeavy`/`SuperHeavy`, 7 Cartridge
types, `ItemDataDisk`, `ItemIntegratedCircuit10`, `ItemTablet`/`ItemAdvancedTablet`,
`ItemLaptop`, 4 `MotherboardX` types, `PortableSolarPanel`, `PortableComposter`.

### Hydraulic Pipe Bender (130)
The atmospherics/plumbing fabricator: all pressure/insulated pipe kits, valves, meters,
analyzers, radiators, the entire gas-filter family (plain + heavy + medium, all ~8 gases),
canisters (`ItemGasCanisterEmpty`/`Smart`, `ItemLiquidCanisterEmpty`/`Smart`), tanks (plain +
insulated + portable + portable Mk II, gas + liquid), airlocks, hangar doors, cryo tube,
showers, toilets, sleepers, drinking fountains, water bottle filler/purifier, and
`ItemKitChute`⭐/`ItemKitStandardChute`⭐/`ItemKitPipe`⭐ (primary here, dup at Autolathe). Also
sourced 4 extra kits from `DynamicObjectsFabricator.xml` (Portable Gas Tank, Portable Gas Tank
Mk II, Portable Liquid Tank, Portable Liquid Tank Mk II).

### Tool Manufactory (119)
Hand tools, mining drills, welding/cutting tools, suits/helmets/backpacks/uniforms (Hardsuit,
Icarus, HARM, Marine, EVA⚠dup, Space Helmet⚠dup), the entire 16-color spray-paint family
(sourced from `paints.xml`, same `ToolManufactoryRecipes` tag as the base tools file), all 16
national-flag Overalls uniforms, mining belts/backpacks, chem lights, flares, and
`ItemLabeller`⭐ (primary here, dup at Electronics Printer). `ItemExplosive`⚠dup/
`ItemMiningCharge`⚠dup also appear here (primary at Security Printer).

### Security Printer (15)
`CartridgeAccessController`, 12× `AccessCard<Color>`, `ItemExplosive`⭐ (Demolition Charge),
`ItemMiningCharge`⭐.

### Furnace (14)
Base-tier smelting: `ItemCharcoal`, `ItemSolidFuel`, and 11 base ingots (Silicon, Iron, Gold,
Copper, Silver, Lead, Nickel, Steel, Electrum, Invar, Constantan, Solder — one recipe each,
sourced from `furnace.xml`; the shared `ingots.xml::IngotRecipes` data backs this same list).

### Advanced Furnace (5)
Superalloy ingots only: `ItemAstroloyIngot`, `ItemHastelloyIngot`, `ItemInconelIngot`,
`ItemWaspaloyIngot`, `ItemStelliteIngot`.

### Arc Furnace (8) — no exclusive outputs
`ItemCharcoal` + 7 base metals (Iron/Gold/Copper/Silver/Lead/Nickel/Silicon) — an alternate,
industrial (non-crucible) recipe path to the same 8 items the Furnace already makes. Kept as
its own table for completeness since it's a real fabricator with a real recipe file
(`arcfurnace.xml`), but every output is a duplicate.

### Centrifuge (12)
`ItemBiomass`⭐, `ItemCharcoal`⚠dup (primary Furnace), and 10 raw ores exclusive to this
machine: Coal, Cobalt, Copper, Gold, Iron, Lead, Nickel, Silicon, Silver, Uranium.

### Chemistry Set (3)
`ItemPillHeal` (Pill, Medical), `ItemPillStun` (Pill, Stun), `ItemMilk`.

### Reagent Grinder (4)
`ItemFlour`, `ItemSugar`, `ItemCocoaPowder`, `ItemSoyOil` — input→output processing
(`reagentgrinder.xml::ReagentGrinderRecipes` uses `InputPrefab`/`OutputPrefab`, not a printed
`RecipeData`, but the mechanism is the same "you get this item out").

### Microwave (19)
Baked Potato, Bread Loaf, Burger, Cake, Cereal Bar, Chocolate Bar, Chocolate Cake, Chocolate
Cereal Bar, Condensed Milk, Cooked Corn, Cooked Mushroom, Cooked Pumpkin, Cooked Rice, Cooked
Soybean, Cooked Tomato, French Fries, Muffin, Powdered Eggs, Pumpkin Pie — the base cooking
appliance; primary for all 19 (Automated Oven duplicates the entire list).

### Automated Oven (19) — no exclusive outputs
Identical 19-item list to the Microwave (`automatedoven.xml` and `cooking.xml` define the same
`PrefabName` set under `AutomatedOvenRecipes`/`MicrowaveRecipes`). A bulk/upgraded cooking path,
not a new recipe.

### Packaging Machine (9)
`ItemCannedCondensedMilk`, `ItemCannedEdamame`, `ItemFrenchFries` (Canned French Fries — distinct
prefab from the Microwave's `ItemFries`), `ItemCannedMushroom`, `ItemCannedPowderedEggs`,
`ItemCannedRicePudding`, `ItemCornSoup`, `ItemPumpkinSoup`, `ItemTomatoSoup`.

### Rocket Manufactory (43)
Rocket/launch-site kits: fuselage, avionics, crew module parts, engines (pressure-fed gas/
liquid, pumped gas/liquid), fuel tanks/umbilicals (gas/liquid/power/chute), launch mount
(ground + orbital), payload bay/delivery container, launch tower, 7 mining-drill-head variants,
2 scanning-head variants, plus `ItemKitAccessBridge`⚠dup/`ItemKitStairwell`⚠dup (primary at
Autolathe).

### Terraforming Manufactory (2)
`ItemKitDebug`⭐ (Kit Debug — likely dev/creative-tooling content, flagged for verification, not
a normal play-test item) and `ItemIronFrames`⚠dup (primary at Autolathe).

### Paint Mixer — not a print recipe
`RecipeType.PaintMixer` exists (`Reference/.../RecipeType.cs:19`) and the machine class
`Assets/Scripts/Objects/Appliances/PaintMixer.cs` exists, but it does not *print* a new prefab —
it **fills an already-held empty spray can** (from the Tool Manufactory, Part C1) with a mixed
reagent color from `ColorDye`/`SoyOil` ingredients (`IPaintMixerIngredient`). No output table;
the 16 spray-can prefabs are already covered under Tool Manufactory.

### Organics Printer — no recipe data found
`RecipeType.OrganicsPrinter` and the class `Assets/Scripts/Objects/Electrical/
OrganicsPrinter.cs` both exist, and it loads recipes by the string key `"OrganicsPrinter"`
(`OrganicsPrinter.cs:111`) the same way every other fabricator does — but **no
`OrganicsPrinterRecipes` tag exists anywhere in `StreamingAssets/Data/`** (confirmed by
searching the entire game install, not just the `Data` folder). Either this fabricator ships
empty in the current build, or its recipes are seeded by a mod/DLC data file not present here.
No output table possible from this data.

### Recycler — dynamic, not a fixed recipe list
`RecycleRecipes` in `recycling.xml` is entirely commented out. The Recycler reclaims materials
by the item's own registered `Recipe` mixture at recycle time
(`DynamicThingRecipeComparable.AddRecipe` calls `Recycler.AddRecycleRecipe` for every OTHER
fabricator's recipe, `Assets/Scripts/Util/DynamicThingRecipeComparable.cs:72`) — i.e. every
printed item is automatically recyclable back into (a fraction of) its own ingredients, driven
by code, not a standalone data table. Not a "produces" list in the sense the other tables are.

### Not printed (looted / mined / grown / spawned)

Real, verified prefabs with **zero** matching `RecipeData` anywhere in `StreamingAssets/Data/`.
Grouped for scanning (134 total; Wreckage debris, editor templates, and NPC/vehicle prefabs
excluded as not player-stowable):

| Group | Items |
|---|---|
| **Mined — raw ore/ice variants** | `ItemDirtyOre`/`ItemSpaceOre` (Dirty Ore), `ItemReagentMix`, `ItemSpaceIce`, and all 21 Ices from Part B (`ItemNitrice`, `ItemOxite`, `ItemVolatiles`, `ItemIce`, 14× `ItemPureIce*`) |
| **Grown / foraged** | `ItemAlienMushroom`, `Fertilizer`, `ItemCropHay` (Hay), `ItemCheeseWedge`, `ItemEgg`/`ItemFertilizedEgg`, `SeedBag_Potato`, `ItemPeaceLily`, `ItemTropicalPlant`, `DecayedFood` |
| **Harvested (organs)** | `OrganBrain`, `OrganLungs`, `OrganLungsZrilian`, `OrganLungsChicken`, `HumanSkull` |
| **Locker-spawn suits/gear** (not fabricator output in this build) | `ItemSuitAdvancedAC`, `ItemCryoMask`, `ItemDebugHeadlamp`⚠, `ItemDebugJetpack`⚠, `ItemEmergencyEvaSuit`, `ItemEmergencySpaceHelmet`, `ItemSuitEmergency`, `ItemEmergencyToolBelt`, `ItemSuitHard`, `ItemSuitInsulated`, `ItemSuitNormal`, `ItemSuitSpace`, `ItemJetpackTurbine`, `ItemSuitModCryogenicUpgrade`, 7× `ItemEmergency*` tools (Angle Grinder, Arc Welder, Crowbar, Drill, Pickaxe, Screwdriver, Wire Cutters, Wrench) |
| **Deployed device forms** (their *kit* is printed — Part D above — but the assembled/uninstalled item itself has no separate recipe) | `DynamicAirConditioner`, `DynamicScrubber`, `DynamicGenerator`, `DynamicHydroponics`, `DynamicLiquidCanisterEmpty`/`DynamicMKII*`, `DynamicGasCanisterEmpty`, `DynamicGasTankAdvanced`, `DynamicAFrameStripes`/`WIP`, `DynamicBarrier`, `DynamicChannelizer`, `DynamicWorkCone` |
| **Catalytic (endgame) gas filters** | all 7 `ItemGasFilter*Infinite` — no print recipe found; likely tech-tree/research or trade-only |
| **Misc found/traded items** | `ItemBook`, `CartridgeGPS`, `CartridgeOreScanner` (their "Color"/plain successors ARE printed), `DynamicBodyBag`, `ItemDynamite`, `ItemGrenade`, `ItemHandSanitizer`, `ItemInsulatedCanisterPackage`, `ItemSecurityCamera`, `ItemWirelessBatteryCellExtraLarge`, `ItemHighVolumeGasCanisterEmpty`, `ItemHealPill` |
| **Debug/creative-only** (flag for verification, may not be reachable in normal play) | `ItemAuthoringTool`, `ItemTerrainEditor`, `DynamicGPR`, `DynamicPortal`, `ItemRTG`/`ItemRTGSurvival`, `Robot` (AIMeE Bot) |
| **Storage packaging** | `ItemCerealBarBag`/`Box`, `CrateMkII`, `DynamicCrate`, `ItemMiningPackage`, `ItemPortablesPackage`, `ItemResidentialPackage`, `ItemWaterBottleBag`/`Package` |
| **Unreleased/orphan kits** (no fabricator claims them — likely future content or removed from the active recipe set) | `ItemKitHydroponicAutomated`, `ItemGasTankStorage`, `ItemKitConveyor`, `ItemKitCube`, `ItemKitDockingPort`, `ItemKitDuct`, 4× `ItemKitFuselageType[A-D]`, `ItemGasSensor`, 2× `ItemKitPressureFed*Heavy`, `ItemLiquidTankStorage`, `ItemPipeLiquidRadiator`, `ItemKitLowVolumeLiquidPipes`/`Pipes`, `ItemPipeRadiator`, `ItemKitRespawnPointWallMounted`, `ItemKitPictureFrame` |

---

## Part E — Starter profile (single-bag early game)

One bag, active before FlorpyDorp has set up anything else — matches the early-game haul
(ores, ice, basic tools, food) with pure `Category` rules, mirroring the shape of the mod's
existing shipped starters (`BagProfiles.cs:733-818`, `WriteStarterProfiles`) but consolidated
into one bag instead of four:

```xml
<BagProfile name="Starter Haul">
  <Category name="Ores" priority="50"/>
  <Category name="Ices" priority="50"/>
  <Category name="Tools" priority="50"/>
  <Category name="Food" priority="50"/>
</BagProfile>
```

Deliberately **no** `Kits`/`Default`/`Resources` — those would swallow every wall kit, battery,
and access card the moment you pick one up, which defeats the point of a *starter* bag (get the
ore/ice/tool/food haul under control first). All four rules at the same Normal priority since
there's no other profile to tie against yet — the moment FlorpyDorp assigns a second bag
(Materials, Ingots+Ores, ...), this profile should be retired or narrowed, not left running
alongside the 9-bag set (its `Ores`/`Ices` rules would otherwise tie-compete with C4 on bag
depth/order, an avoidable ambiguity).

---

*No `Assets/Scripts/StationeersUIMod/**` files were modified to produce this document. Scratch
extraction scripts and raw JSON dumps used to build the tables above live in this session's
temp scratchpad, not in the repo.*
