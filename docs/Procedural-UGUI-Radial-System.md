# Procedural Unity UGUI Radial — The System Florpy Built

**Date:** 2026-07-09 (narrative corrected)  
**Branch:** `Imgui-Unity` (name is historical; the radials are now UGUI)  
**Status:** This is the primary, loved implementation. We are keeping it.

## TL;DR

The current implementation on this branch is the **procedural Unity UGUI radial renderer**. It is perfect, it works, and we love it.

Florpy built this on top of the earlier radial work (prefab-based experiments that lived on `main`) plus the interaction model that was refined during the big UX overhaul (slide-outs, sticky mode, equipment keys, Find item, etc.).

- No prefabs. Everything (Canvas, wedges, icons, TMP labels, center readout) is constructed in C# at runtime.
- One `RadialWedgeGraphic` (a `MaskableGraphic`) generates the correct annular sector mesh for *any* number of entries via `OnPopulateMesh`.
- The complex interaction logic (hover calculation, satellite rings, dwell/grace, level stack, sticky vs hold-release) lives in the shared `RadialMenu` + `RadialController`. The UGUI view is a pure renderer that gets told exactly what to draw.
- This fixes every major problem the earlier prefab approach had while keeping the exact same gameplay feel and `RadialEntry` contract.

We are sticking with the procedural UGUI. The old ImGui draw-list path is now the fallback/legacy option (toggleable via config for comparison).

---

## Why This Approach (and How It Improves on the Earlier Work from `main`)

The earlier UGUI ideas (prefab-based) that existed on `main` had fundamental problems because radials are *inherently procedural*:

- A radial can have anywhere from 1 to 14+ wedges depending on context (toolbelt tools + empty stow slots, bag contents, swap candidates, etc.).
- Pre-authoring fixed layouts or a "10-slot" prefab meant either:
  - One prefab per possible N (maintenance hell)
  - Paging logic that hid items
  - Or dividing the circle by a baked `Capacity` instead of actual entry count → dead hover zones

Other issues discovered in review of that work:
- Color unpacking bugs (ImGui `IM_COL32` packs R in the low byte; treating it as ARGB swapped red/blue on every accent and stow slice).
- GameObject leaks (instantiate on open, only SetActive(false) on close).
- No proper modal/input isolation → hover broke when cursor was locked.
- TMP mesh rebuild thrash on every hover.
- Satellite math was mirrored relative to hover math.
- Fixed 10-wedge assumption dropped tools on a 14-slot toolbelt.

Florpy's solution (this implementation):
- **Procedural mesh, not authored geometry.** `RadialWedgeGraphic.SetGeometry(inner, outer, a0, a1, fullRing)` + `OnPopulateMesh` builds the exact annular sector needed right now. One component type for the whole lifetime of the mod.
- **Built entirely in code.** `UnityRadialView` creates its own `DontDestroyOnLoad` ScreenSpaceOverlay Canvas, pools `RingView`s (which manage wedges + icons + labels), and constructs everything from the data passed in `Render(...)`. No `.prefab` files, no AssetBundles.
- **Perfect hot reload compatibility.** `Shutdown()` destroys the canvas root so F6 ScriptEngine reloads don't accumulate multiple canvases.
- **Correct color handling.** `FromImGui(uint)` properly decodes the low-byte R packing that the old prefab wedges got wrong.
- **Preserve aspect + real typography.** `Image.preserveAspect = true` and TMP `enableAutoSizing + wordWrapping` replace the hand-rolled fitting that ImGui (and the old prefabs) needed.
- **Shared state, honest A/B.** All the hard logic (SectorFromMouse, satellite open after dwell, grace period so flicks aren't hijacked, promote satellite, sticky LMB/RMB, center readout content) stayed in `RadialMenu`. The UGUI view only paints. You can toggle live and the behavior is identical.

This is the "make it cool" phase done right: we got the interaction model solid first (in the shared engine + the ImGui prototype), then Florpy replaced just the paint with something that gives us TMP, animations, and a clean future without the authoring tax of prefabs.

---

## Architecture — How the System Actually Works

### 1. The Stable Contract (`RadialEntry` + `IRadialFeature`)
Everything the features build is still `List<RadialEntry>`:
- Label / Sublabel / Warning / ActionText
- Icon (Sprite from the game)
- Enabled + DisabledReason
- `OnSelect` action (executes via `ItemActions`)
- `ChildProvider` (deeper level on click)
- `SlideOutProvider` (satellite on rim push)

The four features (`ToolbeltRadialFeature`, `ToolRadialFeature`, `BagRadialFeature`, `EquipmentKeyRadialFeature`) and `ItemMenuBuilder` are completely unaware of ImGui vs UGUI.

### 2. The Interaction Engine (`RadialController` + `RadialMenu`)
- `RadialController` owns the tap/hold state machine and one `ModalScope`.
- `RadialMenu` owns:
  - The level `_stack`
  - Current `_satellite`
  - Per-frame hover using mouse delta + `SectorFromMouse` (Atan2 math matching ImGui convention)
  - Dwell timer for slide-outs (0.18s) + fresh-satellite grace (0.25s)
  - `OnHoldReleased`, `UpdateSticky`, `PushBranch`, `PromoteSatellite`
  - At the end of `Draw()` it either calls the UGUI `Render(...)` or falls back to the legacy ImGui `DrawRing`/`DrawCenterReadout`.

All the tricky timing and "what does releasing the key actually mean" logic is in one place.

### 3. The Procedural UGUI Renderer (`UnityRadialView` + `RadialWedgeGraphic`)

**Canvas creation (lazy, once):**
- `EnsureCanvas()` creates a `GameObject("UIAscended_RadialCanvas")` with `DontDestroyOnLoad`.
- `RenderMode.ScreenSpaceOverlay`, `sortingOrder = 5000`.
- `CanvasGroup` with `blocksRaycasts = false` (hover/click decisions are still made by the math in `RadialMenu`, not Unity EventSystem).
- Three children managed by `RingView` / `ReadoutView`.

**Per-frame `Render(...)`:**
- Positions the rings using `CanvasAnchoredPos` (converts from ImGui screen space center).
- Scales the whole thing in with `_openAnim` lerp.
- For the main and satellite rings: `RingView.Render(...)`

**RingView.Render:**
- `EnsureCapacity(n)` — creates exactly as many wedges/icons/labels as needed this frame (pools upward, never shrinks).
- For each entry:
  - Computes `a0 / a1` exactly the same way the old ImGui code did.
  - Calls `wedge.SetGeometry(innerR, outerR, a0, a1, count==1)`
  - Lerps `wedge.color` and `wedge.OuterBulge` (the hover pop).
  - Places icon + label at the mid-angle, mid-radius (offset for hover bulge).
  - Uses `preserveAspect = true` on the `Image`.
  - TMP gets `enableAutoSizing`, word wrap, and a computed `sizeDelta` based on chord + radial width (similar math to the old fitting but now native).

**The magic wedge — `RadialWedgeGraphic`:**
- Inherits `MaskableGraphic`.
- `OnPopulateMesh(VertexHelper vh)`:
  - Calculates segments based on arc length (~4px per segment).
  - For each segment emits two verts (inner + outer) with the current `color`.
  - Builds two triangles per segment.
  - Y flip: `new Vector2(Mathf.Cos(t), -Mathf.Sin(t))` because ImGui is y-down.
- `SetGeometry` only dirties when values actually change.
- Implements `ICanvasRaycastFilter.IsRaycastLocationValid` with proper polar test (back-converts to ImGui angle convention) so the graphic is "honest" even though we don't use Unity raycasts for hover today.

**ReadoutView:**
- Five TMP lines (title, verb, label, sublabel, warning).
- Positioned relative to the hub center using the same anchored conversion.
- Colors come through `FromImGui` so they match the theme the rest of the mod uses.

**Animations:**
- Open: root scale from 0.86 → 1.0
- Hover: color lerp + `OuterBulge` added to outer radius on the hovered wedge only (then `RefreshGeometry()`).

**Shutdown / Hide:**
- `Hide()` just deactivates the canvas root and resets the open anim.
- `Shutdown()` does `Object.Destroy` on the whole canvas GameObject. Critical for ScriptEngine reloads.

### 4. The Fallback
When `UseUnityRadial` is false, `RadialMenu` still runs the exact same state update, then calls the old `DrawRing` / `DrawCenterReadout` using `ImGui.GetForegroundDrawList()` + `DrawUtil`. The UGUI canvas is hidden. This path is kept so we can always A/B or fall back if something goes wrong with the new renderer.

---

## Key Advantages We Get From This System

- Any N works perfectly (single-entry rings are true closed annuli; 14-slot toolbelts are fully reachable).
- No authoring step. Change radii, colors, or add new entry kinds and it just works.
- Beautiful text and correctly proportioned icons for free.
- Hover feedback that feels alive (bulge + lerp).
- Still 100% multiplayer safe — the renderer never touches game state.
- Hot reload just works.
- Future visual upgrades (better shaders, visor integration) can be done by extending the same `RingView` / `WedgeGraphic` objects.

---

## Files That Matter for Understanding This System

**The renderer (what Florpy built):**
- `UI/UnityRadialView.cs` — the orchestrator, RingView, ReadoutView, color conversion, positioning helpers.
- `UI/RadialWedgeGraphic.cs` — the procedural mesh + polar raycast filter.

**The shared engine (refined during overhaul, now feeding the UGUI view):**
- `Overlay/RadialMenu.cs` — `RadialEntry`, level/satellite management, hover + slide-out logic, the `if (UseUnityRadial)` branch.
- `Features/RadialController.cs` — tap/hold, modal, one active radial, calls into menu.
- `Overlay/DrawUtil.cs` + `Theme.cs` — still used by the legacy path, and Theme colors are fed into UGUI via `FromImGui`.

**Where the content comes from:**
- `Features/*RadialFeature.cs`
- `Features/ItemMenuBuilder.cs`
- `Core/ItemActions.cs` (the only place that mutates inventory)

**Config & UI:**
- `UIAConfig.cs` (`UseUnityRadial` now defaults to true)
- `Windows/SettingsWindow.cs`

---

## Relationship to the Earlier Prefab Work

The conceptual model (`RadialEntry` as the pure data, "slide out for more", center verb+label+warning, sticky shopping) came from the original design + the work on `main`.

Florpy kept the good parts and the hard-won interaction details (dwell gates, grace periods, consequence warnings, re-validation on execute) and replaced the fragile authored UI layer with a clean, code-driven, procedural one.

The old prefab code is no longer in this branch. The procedural UGUI is the realization of the "Unity phase" done without the liabilities.

---

## Going Forward

- This is the one we iterate on and ship.
- The legacy ImGui painter can eventually be removed if we want, but keeping it a while is cheap insurance.
- HUD is still ImGui for now (that's fine — different problem space).
- If we ever want curved-visor 3D-projected radials or heavy art, we have a living procedural UGUI foundation to build on instead of starting from authored fixed assets again.

The system Florpy shipped is the one we wanted. This doc exists so everyone understands exactly how it works and why the procedural code-built approach was the right answer.

Read the code in `UI/UnityRadialView.cs` and `RadialWedgeGraphic.cs` together with `RadialMenu.Draw()` — that's the heart of it.