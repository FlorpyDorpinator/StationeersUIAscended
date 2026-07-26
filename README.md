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

## Current direction (as of 2026-07-09)

The primary, loved implementation for the core radials is the **procedural Unity UGUI renderer** that Florpy built on this branch.

- Fully code-built at runtime (no prefabs, no AssetBundles).
- `RadialWedgeGraphic` emits correct annular sector meshes for any entry count.
- TMP auto-sizing + preserveAspect icons + hover bulge + scale-in animations.
- Built on top of the earlier prefab experiments + the shared interaction model (`RadialMenu` / `RadialController` / `RadialEntry`).

It is enabled by default (`UseUnityRadial` in config). Toggle it off in F10 settings or the SLP panel to use the legacy ImGui painter for comparison.

See:
- `Documentation/Procedural-UGUI-Radial-System.md` — explains exactly how Florpy's procedural UGUI system works, how it was built on the earlier work, and why we love and are keeping it.
- `Documentation/How-the-Radial-System-Works.md`
- Changes Report "2026-07-09 - Procedural Unity UGUI Radial"

The older prefab planning in `Building-Radial-Prefabs-in-Unity.md` is historical.

The visor HUD is still drawn with ImGui for now.

## Other notes
- Explore middle-mouse-button unification (future).
- Keep iterating on radial personalities and SmartStow+ / bag profiles while the ImGui implementation is the reference.