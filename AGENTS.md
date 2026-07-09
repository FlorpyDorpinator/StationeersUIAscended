SOURCE BASEGAME CODE IS AT 

D:\ROCKETWERKZ\Stationeers\trunk

DO NOT WRITE ANYTHING THERE. NEVER.

A really complex Stationeers Mod I made can be found in 

D:\Unity\TrainMod

This also contains a bunch of reference how to handle certain things (especially pipe liquid + MP logic sync).

We are working in 

D:\Unity\StationeersUIAscended on this: 

**StationeersUIMod** — Stationeers UI overhaul (radials, SmartStow+, visor HUD) as proper Unity + SLP mod.

Rebased onto https://github.com/FlorpyDorpinator/StationeersUIAscended history.

Uses the Stationeers modding template patterns for entrypoints (OnLoaded + LaunchPadBooster.Mod).

## Notes

- Pure client-side UI mod. No prefabs required for core (ImGui).
- Use Unity for future prefab-based UI polish.
- All original BepInEx POC logic ported and cleaned of hacks.