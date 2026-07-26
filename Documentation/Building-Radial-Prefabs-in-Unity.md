# Building Radial UI Prefabs in Unity for StationeersUIMod

> **Historical note (2026-07-09):** The procedural Unity UGUI implementation (runtime code-built, no prefabs) is now the primary and loved radial renderer on this branch. Florpy created it to solve the problems of the earlier prefab-based approach while delivering real TMP, animations, and perfect hot-reload. Prefab work for the *radial gesture system* is not the current path. This document is retained as historical reference. See `Documentation/Procedural-UGUI-Radial-System.md` for the actual system we are keeping.

**Status:** Historical planning document.  
**Target (at time of writing):** Replace (or coexist with) the ImGui `RadialMenu` + HUD overlays using real Unity prefabs, while preserving exact gameplay semantics.  
**Entry point reminder:** `public void OnLoaded(List<GameObject> prefabs, ConfigFile config)` — the `prefabs` list is for things registered via `MOD.AddPrefabs(...)` in more complex mods. For pure UI we can also Instantiate prefabs we author directly (they get included via the mod export process).

---

## 1. Strategic Context

We are moving **away** from ImGui draw-list radials toward skinned, maintainable, editor-friendly Unity UI because:
- Artist/UX iteration is vastly faster in the Scene view + Prefab editor.
- Animations, states, controller glyphs, accessibility, and theming become first-class.
- The current ImGui version was an excellent rapid prototype (and still the only shipping code).
- The `UI_Prefab_Editing_Environment.unity` scene already exists for this purpose.

**Non-goals for first prefab pass:**
- Perfect curved visor aesthetic (that can come later with a custom mask/shader or 3D helmet overlay).
- Replacing every last ImGui window (Settings + ProfileEditor can stay ImGui or move separately).

**Preserve at all costs:**
- The four radial personalities and their tap/hold distinctions.
- "Slide out for more" gesture (or an equivalent comfortable editor-time analogue).
- Sticky shopping mode.
- Center action readout + consequence warnings.
- MP safety (still route 100% through `ItemActions`).
- Rebind respect.
- All the guardrails in `Guards.cs`.

---

## 2. Recommended High-Level Prefab Architecture

### 2.1 Root Structure

Create under `Assets/Prefabs/UI/` (new):

```
StationeersUIRadial.prefab
├── Canvas (Screen Space - Overlay, high Sort Order, ignore raycasts when closed)
│   ├── EventSystem? (usually one global; be careful not to duplicate)
│   ├── RadialRoot (RectTransform, full screen, driven by RadialUIController.cs)
│   │   ├── BackgroundDim (optional, very low alpha)
│   │   ├── Hub (Image or empty Rect + child readout texts)
│   │   ├── WedgesContainer
│   │   │   └── WedgePrefab (instantiated N times, rotated)
│   │   │       ├── WedgeImage (Image or custom RadialWedgeGraphic)
│   │   │       ├── Icon (Image / RawImage)
│   │   │       ├── Label (TMP_Text, two lines)
│   │   │       ├── ActionHint (small TMP)
│   │   │       └── SlideArrow ( ">" or custom )
│   │   ├── SatelliteContainer (sibling or child, positioned in world space of canvas)
│   │   │   └── (similar wedges, smaller scale)
│   │   └── CenterReadout (TMP lines or a small panel with 4-5 TMP_Text children)
│   └── (Optional) HUD elements in same canvas or separate HUDCanvas prefab
```

Alternative (more advanced): one "Wedge" that uses a procedural mesh or a shader to draw accurate annular sectors without N rotated children. Start simple (rotated children + masked images) for speed.

### 2.2 Key Components You Will Write

- `RadialUIController : MonoBehaviour`
  - `Open(string title, Func<List<RadialEntry>> provider, bool sticky)`
  - `Close()`
  - `Update()` / late input handling that mirrors current `RadialMenu.UpdateSticky` + hold release.
  - Owns the level stack and satellite state.
  - Re-uses the **exact same `RadialEntry`** class (move it to `Core/` or keep in `Overlay/` and reference from UI layer).

- `RadialWedge : MonoBehaviour`
  - Data: `RadialEntry entry`
  - `SetData(RadialEntry e, float angle, bool dimmed)`
  - Wires PointerEnter/Exit/Click or uses manual hover (recommended: manual, because we already do angle math and want the exact same "slide past rim" feel).
  - Exposes events or delegates back to controller.

- `RadialSatellite : MonoBehaviour` (or same controller manages a second set of wedges).

- `CenterReadout : MonoBehaviour` — binds 5 TMP fields, implements the chord-fit logic or uses `TextMeshPro` auto-size + layout groups.

- `UIRadialEntryData` (light wrapper if you want editor-friendly serializable version for testing static radials).

### 2.3 Data Flow (Keep the Old Model)

The feature code (`ToolbeltRadialFeature.BuildRoot()` etc.) stays **completely unchanged**.

`RadialController` becomes (or delegates to):

```csharp
public class RadialController
{
    private RadialUIController _ui;   // new
    ...
    private void OpenRadial(...) {
        ...
        _ui.Open(feature.Title, feature.BuildRoot, sticky);
    }
}
```

Or keep the old `RadialMenu` interface and have `RadialUIController` implement the same surface the controller expects (`IsOpen`, `IsSticky`, `OnHoldReleased`, `UpdateSticky`, `Open`, `Close`).

This minimizes churn in the 4 feature classes.

---

## 3. Input & Modal Integration (The Hard Part)

Current isolation is:
- `KeyInputState.Typing`
- `MouseModeController.AddModal(...)`
- `CursorManager` focus reset
- Harmony patches that short-circuit vanilla polled handlers

For prefabs you have options:

### Option A — Keep the ModalScope (recommended for v1)
- Instantiate the radial prefab.
- When opening: still call `ModalScope.Open()` (or a UIVersion).
- While the prefab is active, **ignore Unity EventSystem clicks** for radial decisions and do the same screen-space sector math you do today (`Input.mousePosition` → angle → index). This gives pixel-perfect identical feel.
- Only use Unity buttons for "nice to have" hover sounds or eventual controller navigation.

### Option B — Embrace the EventSystem
- When radial is open, enable a CanvasGroup or separate layer that receives raycasts.
- Each wedge is a real `Button` or `IPointer*Handler`.
- Problem: the game's cursor locking + `MouseModeController` + custom input will fight the EventSystem. You will still need patches or cooperation.
- Better for future controller support.

**Recommendation:** Start with **Option A** (manual hover using the exact same `SectorFromMouse` code). The "feel" of the current MMB flick is precious — don't lose it to raycast differences on the first pass.

You will still need the deferred release logic in `ModalScope`.

---

## 4. Populating Wedges at Runtime (from RadialEntry)

```csharp
public void Rebuild(List<RadialEntry> entries, bool dimmed)
{
    ClearWedges();
    float sector = 360f / entries.Count;
    for (int i = 0; i < entries.Count; i++)
    {
        var wedge = Instantiate(_wedgePrefab, _container);
        var rt = wedge.GetComponent<RectTransform>();
        rt.localRotation = Quaternion.Euler(0,0, -90 + i * sector + sector/2);
        wedge.GetComponent<RadialWedge>().SetData(entries[i], /*angle*/ i * sector, dimmed);
    }
}
```

For icons:
- `entry.Icon` is a Unity `Sprite` from `thing.GetThumbnail()`.
- Assign directly to `Image.sprite`. Works.
- Watch for `textureRect` atlasing (same issues as before; `IconCache` logic may be reusable or simplified).

Labels: two-line fitting can be done with TMP `enableWordWrapping`, `maxVisibleLines`, or by measuring with `TMP_Text.GetPreferredValues`.

---

## 5. Reproducing "Slide Out" with Prefabs

Current UX:
- Cursor past outer radius over a wedge that has `SlideOutProvider` → after dwell, satellite appears offset radially.
- Satellite is a second smaller ring.
- Cursor back inside main → satellite closes.
- Clicking a satellite entry can promote it.

Prefab version ideas:

1. **Simple & faithful:** On dwell, `Instantiate(satellitePrefab)` at calculated world position in canvas space, feed it the sub-list. Same angle math.
2. **Nicer:** Animate the satellite in with scale + slight radial push. Use an `Animator` on the satellite root.
3. **Alternative gesture (if you hate satellites):** On "open more" click, replace the entire main ring content with the slide contents (with a breadcrumb "Back" wedge or title bar). This is simpler to implement in prefabs and may feel cleaner for deep nesting.

The original design report liked the satellite because it keeps context visible ("I'm looking at the battery slot of the drill that is still on the belt").

---

## 6. Sticky vs Hold-Release Mode in UI Terms

- Non-sticky (classic hold-MMB): the whole radial is only "armed" while the physical key is down. On key up, execute whatever is under the cursor (or cancel).
- Sticky (tap-1-6 or configured Tab): the prefab stays active after key up. LMB commits + stays open + refreshes data. RMB = back one level. Re-press key or Escape = close.

Implement two slightly different input paths in `RadialUIController` or have a `bool isSticky` that changes what `Update()` listens to.

You can drive visibility with `gameObject.SetActive` + a "just opened this frame" flag to avoid eating the first click.

---

## 7. The HUD Prefab (Easier Win)

The HUD (`HudOverlayFeature`) is even more straightforward to move:

- Create `StationeersUIHUD.prefab`
- Bottom-center two-hand box (two mirrored panels)
- Top slim status strip
- Corner vitals
- Day/time
- Contextual reticle panel

Each sub-panel can be its own prefab with a binder script (`HandBoxUI`, `StatusStripUI`, etc.) that reads from `Human`, `InventoryManager`, `Atmosphere`, `Thing.InteractOnOff` etc. every frame or on events.

Hiding vanilla:
- Continue using the game's `InventoryManager.SetUIPanelVisibility` / `Canvas.enabled` / alpha tricks (never destroy).
- The current `SyncVanillaVisibility` + per-panel tracking is good — replicate it.

---

## 8. Integration Points in StationeersUIMod.cs

```csharp
public void OnLoaded(...)
{
    ...
    _radialUI = Instantiate(radialPrefab);   // or load from bundle / Resources
    _radialUI.transform.SetParent( /* appropriate UI root or leave at scene root with its Canvas */ );
    _radialUI.SetActive(false);

    _radials = new RadialController(_radialUI.GetComponent<RadialUIController>());
    ...
}

public void DrawOverlay() { /* no longer needed for radials */ }
```

The old `Draw()` path goes away for the new system (or becomes a no-op when using prefabs).

You may still keep the ImGui patch for the Settings window and toasts for a while.

**Important:** The prefab must be included in the built mod. Use the Stationeers mod exporter tooling or put it under a folder that gets packed. Test by checking the exported mod folder.

---

## 9. Visual Theme & Assets

Current Theme (from `Overlay/Theme.cs`):
- Dark teal glass panels (`0.03, 0.11, 0.13` @ 0.62)
- Cyan primary text
- Orange accent (`1.00, 0.55, 0.16`)
- Specific ring hover / stow colors

Create matching UI atlas or 9-sliced images:
- Ring background wedge image (or full circle masked)
- Hub circle
- Accent rim
- Stow orange variant

Use Unity UI `Image` with `Filled` or custom material for partial rings if needed. Or author wedge-shaped sprite assets in 8–12 segments and rotate/scale them.

TextMeshPro is already in the project — use it.

---

## 10. Migration & Dual-Mode Strategy (Safest)

1. Introduce a config `UseImGuiRadials` (default true initially).
2. In `RadialController`:
   ```csharp
   if (UIAConfig.UseImGuiRadials) _menu = new RadialMenu(); else _ui = ...;
   ```
3. Keep both drawing paths alive during transition.
4. Once prefabs are feature-complete and feel identical (or better), flip default and delete ImGui radial code (or leave it for debugging).

This also lets you A/B test the satellite gesture vs a "replace ring" gesture.

---

## 11. Practical Steps & Checklist

- [ ] Create `Prefabs/UI/Radial/` folder + basic Canvas + hub + 8 placeholder wedges in the editing scene.
- [ ] Implement `RadialUIController` that can open/close and display a static list of entries.
- [ ] Port the hover math (`SectorFromMouse`, distance checks) into the controller.
- [ ] Wire `RadialEntry` → wedge visuals (label, icon, colors for enabled/stow/hover).
- [ ] Implement level stack + child navigation.
- [ ] Implement satellite (or decide on alternative).
- [ ] Port sticky LMB/RMB/Escape handling.
- [ ] Re-hook `RadialController` to drive the UI controller instead of (or alongside) `RadialMenu`.
- [ ] Move or duplicate center readout logic.
- [ ] Add sound hooks (reuse `UIAudioManager.Play(...)`).
- [ ] Test every feature (toolbelt MMB especially).
- [ ] Replicate guards + CanKeepRadialOpen close behavior.
- [ ] Handle icon flip + atlas cases.
- [ ] Create HUD prefab + binders in parallel (lower risk).
- [ ] Update Settings window to mention "Prefab UI (experimental)" when ready.
- [ ] Document the prefab wiring in a `PREFAB-WIRING.md` or update this doc.

---

## 12. Open Questions / Tradeoffs to Decide Early

- Do we keep the exact satellite slide-out UX, or simplify to "open more replaces the ring"?
- Manual angle hover vs full EventSystem for wedges?
- One giant Canvas for HUD + Radials, or separate?
- How do we handle controller (D-pad) navigation later? (radial is naturally good for sticks)
- World-space radials for seated vehicles or future 3D interactions? (probably out of scope)
- Performance: instantiating 20 wedges per open is fine. Pool them if you like.

---

## 13. References in the Codebase

Must-read before touching prefabs:
- `Features/RadialController.cs`
- `Features/*RadialFeature.cs` (all four)
- `Overlay/RadialMenu.cs` + `RadialEntry.cs`
- `Features/ItemMenuBuilder.cs`
- `Core/ModalScope.cs`
- `Core/Guards.cs`
- `UIAConfig.cs` (the radial section)
- `Overlay/DrawUtil.cs` (text fitting, icon drawing)
- `Changes Reports/2026-07-08 - 0.1.0 Alpha - Radial UX Overhaul.md`
- `Changes Reports/2026-07-08 - Tab Semantics Swap.md`
- `Documentation/UI-Ascended-Viability-Assessment-and-Plan.md` (historical context)
- `Assets/Scenes/UI_Prefab_Editing_Environment.unity`

The `RadialEntry` model + `ItemActions` + feature `BuildRoot` methods are the stable contract.

---

## 14. Final Notes

The current ImGui implementation is **surprisingly complete** for a prototype:
- Deep nesting
- "Grab another"
- Consequence warnings on swaps
- Stow highlighting
- Satellite "open" gesture
- Proper deferred modal release
- Fail-soft patches

When you build the prefab version, treat the ImGui code as the **executable spec**. Match the interactions first, then make it look nicer.

Once the prefab radials + HUD are solid, the rest of the mod (SmartStow+, bag profiles, settings) can stay or move gradually.

Good luck — this is the fun part.

(After the prefab work lands, consider updating `AGENTS.md` and `README.md` and removing any lingering aquarium references in documentation.)
