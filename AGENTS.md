SOURCE BASEGAME CODE IS AT 

D:\ROCKETWERKZ\Stationeers\trunk

DO NOT WRITE ANYTHING THERE. NEVER.

A really complex Stationeers Mod I made can be found in 

D:\Unity\TrainMod

This also contains a bunch of reference how to handle certain things (especially pipe liquid + MP logic sync).

We are working in 

D:\Unity\Tankioneers on this: 

**Tankioneers** — Stationeers Aquarium Mod

Pipe-connected aquarium with real liquid simulation, custom volume (bypasses 2×2 m world grid liquid), IC10 logic.

## Before implementing POC

Read `docs/POC-SPEC.md`. Do not write aquarium scripts until user gives manual GO.

## Key technical facts

- Vanilla liquid **rendering** is World-mode only at 2 m grid cells.
- Aquarium uses `AtmosphereMode.Thing` + custom water plane visual.
- Port patterns from TrainMod: `InitInternalAtmosphere`, `AtmosphereDisplaySync`, `PipeConnectionHelper`.