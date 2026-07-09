# StationeersUIMod

Stationeers UI overhaul as a Unity + StationeersLaunchPad mod.

Radial menus (toolbelt / tool / bag), SmartStow+, equipment key radials, visor-style two-hand HUD. Built on the game's own ImGui. No ten-slot hotbar.

**Status:** BepInEx POC (StationeersUIAscended) ported and rebased as the Unity mod base. Namespace: StationeersUIMod. Using proper SLP entrypoints.

All aquarium/tank prototype content removed. History rebased onto https://github.com/FlorpyDorpinator/StationeersUIAscended .

## Quick links

- Original design: see the BepInEx history in git
- Use `Assets/Scenes/UI_Prefab_Editing_Environment.unity` for prefab work

## Current structure

```
Assets/Scripts/StationeersUIMod/
├── Core/
├── Features/
├── Overlay/
└── Windows/
```

Entry point: `StationeersUIMod` (MonoBehaviour + OnLoaded via LaunchPadBooster / SLP).
│   ├── PipeConnectionHelper.cs   OpenEnd → pipe network
│   ├── AtmosphereDisplaySync.cs  MP logic read mirrors
│   └── WireframeMaker.cs         Blueprint wireframes
└── patches/
    ├── ThingSaveDataPatch.cs       Save corruption guard
    └── StationpediaPatch.cs        Encyclopedia registration
```

---

## Next steps

1. You: finish Blender model + answer `docs/QUESTIONS.md`
2. You: say **GO** for POC script implementation
3. Agent: implement `StructureAquarium` per `docs/POC-SPEC.md`