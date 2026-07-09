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

## Next steps

- Build the real Unity UI prefabs for radials and HUD (see `docs/Building-Radial-Prefabs-in-Unity.md`).
- Explore middle-mouse-button unification (future).
- Keep iterating on the radial personalities while the ImGui version is still authoritative.