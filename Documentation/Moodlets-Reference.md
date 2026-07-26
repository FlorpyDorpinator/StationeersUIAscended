# Stationeers Moodlets ("Status Updates") — Complete Reference

*Verified against the build 27701 decompile (2026-07-10). For UI Ascended design work:
everything the vanilla game can tell the player about their body/suit state, with the
exact trigger conditions and how a client-side mod reads each one.*

The game has no class named "moodlet" — the system is `Assets.Scripts.UI.StatusUpdates`
(the icon strip) and its per-icon `StatusUpdate` objects. Every trigger below runs
client-side off replicated data, so UI Ascended can re-skin all of them.

## Survival

| Moodlet | Caution when | Critical when |
|---|---|---|
| **Hungry / Starving** | `Nutrition < WarningNutrition` | `< CriticalNutrition` (starvation damage begins at 0) |
| **Thirsty / Dehydrated** | `Hydration < WarningHydration` | `< CriticalHydration` (damage at 0) |
| **Suffocating / Low O₂** | `OxygenQuality < 1.0` | `< 0.75` (brain damage once the ~7.5 s oxygen store empties) |
| **Injured** | `DamageState.TotalRatio > 0.25` | `> 0.75` |
| **Stunned / Incapacitated** | `Stun/100 > 0.05` | `> 0.75` (unconscious at 100) |
| **Needs toilet** | `SanitationRatio > 0.75` | `> 0.9` |
| **Soiled** | — | `IsSoiled` (95 % move speed) |
| **Low mood** | `Mood < 0.5` | `≤ 0` |
| **Dirty (hygiene)** | `Hygiene < 0.25` | `≤ 0` |
| **Refreshed** (buff) | `Hygiene > 1.0` — +5 % move/tool speed | — |
| **Respawn stress** (debuff) | `RespawnStressTime > 0` | — |

## Atmosphere (measured on what you BREATHE — helmet internals when closed, else world)

| Moodlet | Caution | Critical |
|---|---|---|
| **Low pressure** | < 20 kPa | < 6.3 kPa (Armstrong limit) |
| **High pressure** | > 303.97 kPa | > 607.95 kPa |
| **Freezing** | < 0 °C | < −10 °C (lung damage starts) |
| **Overheating** | > 50 °C (damage starts) | > 80 °C |
| **Toxins** (Pollutant, CH₄, Hydrazine, Silanol, HCl — CO₂ is NOT a toxin) | partial pressure > 0.5 kPa | > 1.0 kPa (damage) |

## Suit & gear

| Moodlet | Caution | Critical |
|---|---|---|
| **Suit power** | battery Mode ≤ Low | battery missing or Mode ≤ Critical |
| **Air tank** | < 30 min of breaths | missing/broken or < 5 min |
| **Filters** | `LowFilter` (sum ≤ 30) | none present or `EmptyFilter` (≤ 10) |
| **Waste tank** | ≥ 75 % of max pressure | missing/broken or ≥ 95 % |
| **Coolant** | within 10 K of the safe band edge | tank missing/broken or coolant outside 0–50 °C |
| **Suit/helmet leak** | `LeakRatio > 0` | `> 0.2` |
| **Jetpack** | ON (notice); propellant < 500 kPa over ambient (warning) | < 100 kPa over ambient |
| **Internals** | helmet closed (on) / open (off) — drawn on the vitals window, not the icon strip | — |
| **Helmet light** | on/off — vitals window | — |

## Movement / situation

| Moodlet | Trigger |
|---|---|
| **Zero-G** | `GForce < 0.01` |
| **High acceleration** | `GForce ≥ 1.5` (warning), `≥ 4` (critical) |
| **Climbing** | grab/ladder movement mode |
| **In seat/sleeper** | player is inside a slot |
| **Unconscious** | `State == Unconscious` (animated icon) |
| **Leaving mission area** | `PlayableAreaState` = Warning/Invalid |
| **Crew screen available** | seated in a CrewModuleChair wired to a rocket computer |
| **Life suspended** | inside a cryotube/sleeper that suspends life |

## Medical (timed icons with a seconds countdown)

| Moodlet | Trigger |
|---|---|
| **Healing** | cryotube regeneration active, or any medical item implementing `IHealEffectMoodle` |
| **Stimmed** | active `IStimEffectMoodle` (stim injector) |
| **Med-stunned** | active `IStunEffectMoodle` |

Plus the **7-part body damage doll** (head, upper/lower torso, arms, legs) colored by a
damage gradient — head tracks the brain organ, chest the lungs; the robot's doll tracks
its battery's damage state.

## Robot differences

Hunger, thirst, mood, hygiene, breathing and temperature moodlets never fire for the
robot (`SpeciesClass.Robot`). Its power moodlets read `Human.RobotBattery` (the uniform
slot becomes a battery slot), and an empty/missing battery stuns the brain instead of
suffocating it.

## How UI Ascended reads them (client-safe)

- Cheapest mirror of vanilla: iterate `StatusUpdates.AllStatusUpdates` and read each
  entry's `_lastStaticState` / `_lastFlashState` (public), `Icon`, `GetDisplayName()`.
- Or compute directly from `InventoryManager.ParentHuman` — every input is replicated
  (`Nutrition`, `Hydration`, `Mood`, `Hygiene`, `OxygenQuality`, `SanitationRatio`,
  `DamageState`, `MedicalEffects`, suit/helmet occupants, `BreathingAtmosphere`).
- Warning thresholds are Unity-serialized fields — read `human.WarningNutrition` etc.
  off the live instance, never hardcode (prefab values override code defaults).
- To hide the vanilla icon strip non-destructively: the `statusPanel` flag of
  `SetUIPanelVisibility`, or deactivate an ANCESTOR of `StatusUpdates.StatusTransform`
  (the manager re-activates the transform itself each frame). Never call
  `DisableStatus()` mid-game — it nulls `StatusUpdates.Parent` and nothing restores it
  until respawn.

The 0.5.0 visor HUD already covers the survival + atmosphere + suit rows in its top bar
and vitals card; this list is the menu for choosing what the HUD surfaces next
(leak, coolant, jetpack, G-force, medical timers are the obvious candidates).
