# How the Radial System Works (and How to Use It)

**Project:** StationeersUIMod (Unity + LaunchPadBooster)  
**Current implementation:** Procedural Unity UGUI renderer (code-built canvas + runtime annular wedges via MaskableGraphic) — the primary and loved implementation. A legacy ImGui draw-list painter is available as a toggleable fallback.  
**Date of analysis:** 2026-07-09 (updated)  
**Status:** Florpy built the procedural UGUI system on top of earlier prefab work and the shared interaction model. This is what we are keeping. See `docs/Procedural-UGUI-Radial-System.md` for the deep dive into the system Florpy built.

**Goal of this doc:** Explain the complete architecture, UX model, and extension points so developers understand exactly how the system behaves today. The ImGui code is the executable spec.

---

## 1. High-Level Architecture

The radial system is **not** a vanilla feature. Stationeers has no built-in pie/radial menus (only construction wheels, valve handles, and color pickers). All radials here are a from-scratch replacement/overlay built on the game's bundled ImGui (RG.ImGui + RG.ImGui.Unity).

### Entry & Drawing
- `StationeersUIMod` (MonoBehaviour) is the root.
- `OnLoaded(List<GameObject> prefabs, ConfigFile config)` — proper SLP entry. Currently ignores prefabs (pure overlay mod).
- `Update()` drives `RadialController.Update()`.
- Drawing happens via a **single Harmony postfix**:
  ```csharp
  [HarmonyPatch(typeof(ImGuiWindowManager), nameof(ImGuiWindowManager.Draw))]
  internal static class Patch_ImGuiWindowManager_Draw { ... StationeersUIMod.Instance?.DrawOverlay(); }
  ```
- `DrawOverlay()` calls `_radials.Draw()` (and HUD + toasts) **inside the game's ImGui frame** (after normal windows, before submit). This is why no `ImGui.Begin/EndFrame` is needed and why it respects loading screens etc.
- Guardrails live in `Core/Guards.cs` (`CanDraw`, `CanAcceptGameplayInput`, `CanKeepRadialOpen`).

### Central Driver
`Features/RadialController.cs` owns:
- List of `IRadialFeature`.
- One shared `RadialMenu _menu`.
- One shared `ModalScope _modal`.
- Tap-vs-hold state machine (`_pending`, `_pendingSince`, `_active`).

Only **one radial** can be open at a time.

---

## 2. The IRadialFeature Contract (The "Multiple Systems")

All radials implement this interface. The differences in "feel" come almost entirely from two properties + how they implement `OnTap`/`OnHold`:

```csharp
public interface IRadialFeature
{
    string Title { get; }
    bool Enabled { get; }
    KeyCode Key { get; }
    bool OpenOnTap { get; }   // THE KEY DIFFERENTIATOR

    bool CanOpen();
    List<RadialEntry> BuildRoot();
    void OnTap();
    void OnHold();
}
```

### OpenOnTap Semantics (in `RadialController.UpdatePending`)

**If `OpenOnTap == false` (hold-to-open style):**
- Short tap → `feature.OnTap()` (usually re-dispatch vanilla action).
- Hold past threshold → open radial (non-sticky, release to act).

**If `OpenOnTap == true` (tap-to-open style):**
- Short tap (key up before threshold) → open radial in **sticky** mode immediately.
- Long press → `feature.OnHold()` (usually the equip action).

This single flag creates the "multiple close/similar systems with different behaviour" the user observed.

### The Four Features Today

| Feature                  | Key (default)     | OpenOnTap | Behavior on Tap                  | Behavior on Hold                  | Sticky?      | Vanilla Suppression?          | Notes |
|--------------------------|-------------------|-----------|----------------------------------|-----------------------------------|--------------|-------------------------------|-------|
| `ToolbeltRadialFeature`  | Mouse2 (MMB)     | false    | (nothing — PingHighlight is unbound) | (nothing)                        | No (release) | No (MMB not polled the same) | "Feels best" per user. Pure flick equip. Empty slots shown for stow. |
| `ToolRadialFeature`      | R                | false    | Re-dispatch `button.PrimaryAction` (open item window) | (none)                       | No           | Yes (when key==ActiveHandSlot + config) | Hold tool → its controls/slots. |
| `BagRadialFeature`       | Tab              | **config** (`BagRadialTapOpens`) | If true: open sticky radial<br>If false: scoreboard | If true: scoreboard<br>If false: open radial | Yes when tap | Yes (ToggleScoreboard prefix) | Root = worn containers + "Find item" + "Grab another". Deeply nested. |
| `EquipmentKeyRadialFeature` (x6) | 1-6 (live from KeyManager) | **true** | Open sticky management radial   | Equip/unequip to active hand     | Yes (tap)    | Yes (CheckDisplaySlot prefix for buttonName) | Replaces both tap window + hold-equip for helmet/glasses/suit/back/uniform/toolbelt. |

**Why MMB toolbelt "works better":**
- No conflict with a useful vanilla action on release (unlike Tab/scoreboard or R/window).
- Pure hold-release flick gesture — muscle memory matches "select weapon on belt".
- No sticky mode overhead.
- Tab radials are "weird" because:
  - Configurable semantics (easy to get the wrong one in muscle memory).
  - Heavy nesting + "Find item" level + satellite slide-outs.
  - Sticky mode requires explicit close or RMB/Tab.
  - Scoreboard still needs to be reachable.

---

## 3. RadialController State Machine (Detailed)

```text
Idle
  ├─ KeyDown on a feature.Key → _pending = feature, record time
  │
Pending
  ├─ Key released before threshold
  │   ├─ OpenOnTap → OpenRadial(sticky:true)
  │   └─ !OpenOnTap → feature.OnTap()   // pass-through vanilla
  └─ Held past HoldThresholdMs → 
        ├─ OpenOnTap → feature.OnHold()
        └─ !OpenOnTap → OpenRadial(sticky:false)
```

**OpenRadial:**
- `if (!feature.CanOpen())` → fallback to OnHold (for tap features) or fail sound.
- `_modal.Open()` (Typing state + MouseModeController modal + cursor unlock).
- `_menu.Open(title, BuildRoot, sticky)`.
- `_active = feature`.

**While open (`UpdateOpen`):**
- `!CanKeepRadialOpen()` → hard close.
- Non-sticky: if key released → `menu.OnHoldReleased()` (may go sticky on branch).
- Sticky: re-press key / Escape / RMB / LMB handling in `menu.UpdateSticky()`.
- Escape or key-down on active key in sticky → CloseAll.

**CloseAll:**
- Menu close.
- Deferred modal release (see below).
- Clear state.

---

## 4. ModalScope — Why Input Feels "Right"

Copied from the game's own `ImguiCreativeSpawnMenu` pattern.

```csharp
KeyManager.SetInputState("UIAscended_Radial", KeyInputState.Typing);
MouseModeController.AddModal(modalThatUnlocksCursor);
```

**The deferred close trick (the source of many early bugs):**
Vanilla RMB (ToggleActiveHandTool) and Escape fire on **key-up**.
If you restore `Game` input state on the same frame you saw the down, the up event leaks to vanilla.

Solution:
- `RequestDeferredClose()` on close gesture.
- `Pump(holdKey)` every frame: wait until Escape + LMB + RMB + holdKey are all physically up.
- Only then `RemoveInputState` + `RemoveModal` + `CursorManager.OnApplicationFocus(true)`.

This is why radials "don't toggle your welder when you close them."

---

## 5. RadialMenu Internals (The Visual + Interaction Engine)

`Overlay/RadialMenu.cs` + `RadialEntry.cs` + `DrawUtil.cs` + `Theme.cs`.

### Data Model (`RadialEntry`)
```csharp
public class RadialEntry
{
    public string Label, Sublabel, Warning, ActionText;
    public Sprite Icon;
    public bool Enabled;
    public Action OnSelect;                       // leaf action
    public Func<List<RadialEntry>> ChildProvider; // branch (click enters level)
    public Func<List<RadialEntry>> SlideOutProvider; // satellite (slide past rim)
    ...
    public bool IsBranch => ChildProvider != null && OnSelect == null;
    public bool HasSlideOut => SlideOutProvider != null;
}
```

### Levels & Navigation
- `_stack` of `Level` (title + provider + cached entries).
- `PushBranch` adds a child level.
- `PromoteSatellite` turns a satellite into a main level (deep navigation).
- Right-click (sticky) or branch selection navigates.

### Hover & Selection (screen-space, no raycasts)
```csharp
_mainDist = (mouse - center).magnitude;
_hovered = _mainDist >= inner*0.9f ? SectorFromMouse(delta, count) : -1;
```
`SectorFromMouse` uses `Atan2` + normalize + floor.

### Satellite Rings (the "slide out > more")
- Triggered only after `SlideOutDwellSec` (0.18s) while past outer rim + has provider.
- Positioned radially outward from the source wedge.
- Size scales slightly with entry count.
- While satellite visible:
  - Main ring is dimmed and hover is forced off (`_hovered = -1`).
  - Pulling cursor inward or circling to different wedge closes it.
- Fresh satellites have a grace period so fast flick-releases don't get hijacked.

### Hold Release vs Sticky Clicks
- `OnHoldReleased()`: decides source vs satellite based on grace, executes or makes sticky on branch.
- `UpdateSticky()`: LMB acts + refreshes (shopping), RMB backs up one level, Escape closes.

### Drawing
Pure `ImGui.GetForegroundDrawList()`:
- Concentric circles for hub + outer.
- `RingSector` (thick stroked arc) for each wedge (avoids concave fill issues).
- Hover rim accent.
- `FitText` + two-line truncation per wedge width.
- Icons via `IconCache` (aspect-correct, UV from `sprite.textureRect`).
- Center readout: 5-6 lines of fitted text (breadcrumb, verb, label, sublabel, warning/slide hint). Uses chord-width calculation so text never escapes the hub circle.
- Colors from `Theme` (teal glass, cyan text, orange accent/stow).

### Why ImGui Draw Lists Were Chosen Initially
Fast iteration, no assets, pixel-perfect control, re-uses game's font atlas + texture registration, works before any Canvas exists.

---

## 6. Supporting Systems

### InventoryScanner + ScannedSlot
Recursive walk of worn containers + nested slots (depth-capped). Pins expected occupant for staleness checks. Used by Find, Bag levels, swap candidates, SmartStow+.

### ItemMenuBuilder
Single source of truth for "manage this thing":
- Take entry (optional)
- Real controls only (`IsRealControl` filters serialized nulls)
- One entry per slot → click = take/insert, slide = full swap list with consequence warnings.

### ItemActions (THE ONLY STATE MUTATOR)
Everything goes through:
- `hand.PlayerMoveToSlot`, `source.Slot.PlayerSwapToSlot`, `OnServer.MoveToSlot`, `OnServer.SwapSlots`, `Thing.Interact`, `Thing.Merge`.
- Re-validates `Expected` and `AllowMove/AllowSwap`.
- Special case for "take child while holding parent" (routes to other hand).

### Patches (Minimal & Fail-Soft)
- `Patch_InventoryManager_CheckDisplaySlot`: returns false + sets result for owned equipment keys or R.
- `Patch_KeyManager_ToggleScoreboard`: suppresses Tab scoreboard when bag radial owns it.
- `PatchHarness`: each patch independent; one broken game update only degrades its feature.

### Config (UIAConfig)
All keys rebindable (live KeyManager.GetKey for equipment). Hold threshold, radii, grouping, etc. Exposed via StationeersLaunchPad panel.

---

## Decision Note (2026-07-09)

The procedural Unity UGUI renderer (code-built at runtime, `RadialWedgeGraphic` for any-N annular sectors, TMP, hover animations) is the primary and loved implementation that Florpy built on this branch. It is based on the earlier prefab work plus the shared `RadialMenu` interaction engine.

We are keeping it. The legacy ImGui painter remains as a toggleable fallback. The interaction model is renderer-agnostic by design.

The older `Building-Radial-Prefabs-in-Unity.md` is historical. The actual Unity realization is the procedural one.

---

## 7. Current UX Observations & Pain Points

1. **MMB toolbelt is the sweet spot.** Instant, low-conflict, flick-to-equip or stow.
2. **Tab radials have higher cognitive load.** "Find item" is powerful but adds a top-level choice. Nested bags + slide-outs + sticky vs scoreboard means more modes.
3. **Equipment keys 1-6 feel like a win** for power users (tap = quick toggle/swap without opening full window) but will break muscle memory for vanilla users.
4. **Satellite dwell + grace** was the result of adversarial playtesting after flick-releases were stolen.
5. **No world-item radial yet** (see old design reports).
6. **ImGui limitations for future:**
   - Hard to do nice animations, controller support, proper focus visuals.
   - Theming is code, not skinned.
   - Text fitting and icon handling have game-specific hacks.
   - Input isolation is fragile (ModalScope + patches).

---

## 8. How to Use / Extend Today (While Still ImGui)

- Register new feature: `_radials.Register(myFeature);` in `OnLoaded`.
- Implement `IRadialFeature` + supply `BuildRoot()` returning `RadialEntry`s.
- For actions: always go through `ItemActions.*`.
- For deep search: `InventoryScanner.Scan(...)`.
- For manage UI: `ItemMenuBuilder.BuildManageEntries(...)`.
- Test with `EmergencyInjectFeature` or the in-game F10 settings.
- Config lives in `%APPDATA%\Stationeers\BepInEx\config\...` or SLP panel.

---

## 9. Relationship to Vanilla

- Vanilla "radials" are really just timed hold-to-move + key-up-to-open-window on equipment keys + Tab scoreboard.
- The mod **intercepts at the poll points** (`CheckDisplaySlot`, `ToggleScoreboard`) rather than rebinding keys.
- This preserves rebinds and lets us re-dispatch the original action on the "other" gesture.
- All multiplayer safety is inherited.

---

## 10. What Must Be Preserved (Renderer-Agnostic Core)

- The exact `IRadialFeature` + `RadialEntry` data model so features don't change.
- Tap/hold + sticky semantics and the four feature personalities.
- `ModalScope` input isolation (and the deferred release trick).
- `ItemActions` as the single mutation path.
- Consequence warnings, "Grab another", "Find item", slide-outs, center readout.
- Rebind respect + fallbacks when `!CanOpen()`.
- Guards against pause/console/creative menu/unconscious.

This document + the procedural UGUI implementation docs + the code in `Features/`, `Overlay/RadialMenu.cs`, `RadialController.cs`, `UI/UnityRadialView.cs`, `UI/RadialWedgeGraphic.cs`, `Core/ItemActions.cs`, and `ItemMenuBuilder.cs` give the full picture.

The procedural UGUI view (`UI/UnityRadialView.cs` + `RadialWedgeGraphic.cs`) is the current primary renderer. The model in `RadialMenu` is deliberately shared so any future visual work (or the legacy ImGui path) consumes the same data.

Next step: design the Unity prefab equivalent that can host the same `List<RadialEntry>` and drive the same actions with proper UGUI interactions.
