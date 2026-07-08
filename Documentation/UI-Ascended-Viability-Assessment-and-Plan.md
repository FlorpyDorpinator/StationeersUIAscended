# UI Ascended Viability Assessment & Implementation Plan

**Assessed against:** decompiled build `Stationeers 6-30-2026 V27676 Orbit Update Beta` (cross-checked against `V24790 Toilet Update`), the existing BepInEx mods in this workspace (PasswordFix, SprayColor, TestSpawn), and the UI Ascended proposal PDF (Draft v0.1).
**Date:** 2026-07-08

---

## 1. Verdict

**Viable — and your "no prefabs, ImGui POC first, prefabs later" instinct is better-supported than the proposal assumes.** Three findings carry the assessment:

1. **Stationeers ships Dear ImGui.** The game bundles `RG.ImGui.dll` / `RG.ImGui.Unity.dll` (ImGuiNET + a custom Unity mesh renderer) and uses it for the console, loading screen, creative spawn menu, and all debug windows. `ImGuiManager` (`Assets\Scripts\UI\ImGuiManager.cs`) runs a full ImGui frame every `LateUpdate` and composites it over the game. Even better, `UI.ImGuiUi.ImGuiWindows.ImGuiWindowManager` is a **public static registry**: `ImGuiWindowManager.Open(myWindow)` renders any `ImGuiWindow` subclass you write, with mouse-control and key-capture state handled for you — **zero Harmony patches needed for basic windows**. You don't need an external IMGUI library; you piggyback on the game's own, so fonts, style, cursor handling, and click-blocking all match vanilla behavior for free.

2. **Every inventory/interaction mutation the proposal needs has a multiplayer-safe funnel.** The game routes all item movement through `OnServer.*` static methods that branch on `GameManager.RunSimulation`: apply locally on host/singleplayer, else send an authoritative message (`MoveToSlotMessage`, `SwapSlotsMessage`, `MergeStackablesMessage`, `InteractionMessage`) to the server. The mod never needs its own netcode — it computes *intent* (which item, which slot) and calls the same entry points the vanilla UI calls. Given your own desync history on high-latency servers (June 18 session), this is the single most important property: UI Ascended actions behave exactly like manual clicks on the wire.

3. **The devs are already converging on this design.** The Orbit Update beta has `KeyMap.SmartStow = KeyCode.G` — vanilla shipped basic smart-stow on **exactly the key the proposal picks** — plus `SmartTool` scaffolding (currently stubbed) and a `PingHighlight` on middle mouse. This validates the proposal's direction, shrinks the Smart Stow phase to "add stack-priority + bag profiles on top of vanilla," and provides a perfect patch point (`InventoryManager.SmartStow`, line 1809).

The genuinely hard parts are **not** the radials or smart stow — they're (a) replacing the vanilla HUD without null-reffing the god-object singletons that hard-reference it, and (b) the curved-visor aesthetic, which ImGui can only approximate. Both are cleanly deferrable to the prefab/polish phase, which is exactly where the proposal already puts them.

| Proposal feature | Viability | Effort (POC) |
|---|---|---|
| Toolbag radial (hold-MMB) | ✅ High — enumerate belt slots, `OnServer.MoveToSlot`/`SwapSlots` | S–M |
| Tool radial (R) actions | ✅ High — `Thing.Interact(InteractableType.OnOff/Mode, state)` | M |
| Slots branch (battery/cartridge discovery + swap) | ✅ High — recursive `Thing.Slots` walk + two `MoveToSlot` calls | M |
| Smart Stow priority rules | ✅ High — prefix-patch vanilla `SmartStow`, add merge/profile steps | S–M |
| Bag profiles + XML sharing | ✅ High — pure mod-side data (System.Xml and Newtonsoft.Json both ship) | M |
| Nested bag radials | ✅ Medium-high — same primitives, UX iteration is the work | M |
| Two-hand HUD + status strip (overlay) | ✅ High as *additive* overlay; ⚠️ Medium when *hiding* vanilla panels | M |
| Curved visor mode | ⚠️ Approximate in ImGui (arc-segmented), real curvature needs shader/prefab phase | L (approx) / XL (real) |
| Hardcore HUD gating | ✅ High — helmet/sensor state is trivially readable client-side | S |
| Multiplayer safety | ✅ Inherited from `OnServer.*` funnel; no server-side mod required | — |

---

## 2. What the decompile changes about the proposal

Things learned from the code that should feed back into the design doc:

- **G is taken — by the feature itself.** Vanilla `InventoryManager.SmartStow(Slot)` already does: free worn slot by type → remembered original slot → open windows (type-priority) → inside worn containers → fail sound. It does **not** do stack-merge priority, item-type destination memory across bags, or profiles. UI Ascended's Smart Stow becomes **SmartStow+**: a Harmony prefix that runs the profile/stack logic first and falls through to vanilla on no match. Small, robust, update-tolerant.
- **Input conflicts to design around** (all rebindable; defaults from `KeyManager.SetDefaultKeys()`, KeyManager.cs:397):
  - `R` = `ActiveHandSlot` (opens active-hand slot) → use **hold-R** for the tool radial, tap stays vanilla; or default to a free key.
  - `Mouse2` (MMB) = `PingHighlight` (new in these betas) → **hold-MMB** radial vs tap-ping is workable but test the feel; offer alternates.
  - `Tab` = scoreboard, `F` = InventorySelect, `E` = SwapHands, `1–6` = equipment slots. The proposal's controls table needs a pass against this map.
- **No radial menu exists anywhere in the game** (exhaustive search: colour *wheel* picker, pipe valve wheels, and blur shaders only). The radial is a from-scratch build — but Dear ImGui draw lists (`PathArcToFast`/`AddImage`/`AddText`) make a pie menu ~150–250 lines, and public Dear ImGui pie-menu implementations exist to port (e.g., the well-known `PiePopupSelectMenu` pattern from the ImGui community).
- **Item icons are free:** `thing.GetThumbnail()` returns the inventory `Sprite` (colour-variant aware); `Slot.SlotTypeIcon` gives empty-slot icons; `ImGuiManager.ImGuiPointerFor(texture)` registers any Unity `Texture` for ImGui drawing. Radial entries can show real item art with zero custom assets. (Sprites may be atlas sub-rects — pass UVs from `sprite.textureRect` when drawing.)
- **The HUD is UGUI wired by serialized fields on singletons** (`InventoryManager`, `StatusUpdates`, `PlayerStateWindow`). Dozens of `Update()` paths dereference those fields unguarded. **Hide, never destroy**: use the game's own visibility paths (`InventoryManager.SetUIPanelVisibility(...)`, `Canvas.enabled`, `UiComponentRenderer` alpha system) so vanilla logic keeps ticking under your overlay.
- **A first-person helmet layer already exists**: `FirstPersonHelmetOverlay` renders a 3D helmet mesh with frost/FOV/storm-shake. The "curved visor" fantasy has a vanilla anchor to build on in the prefab phase (and `Settings.CurrentData.HelmetOverlay` is the user toggle).
- **Helmet/sensor gating is readable client-side** for hardcore mode: `Human.HelmetSlot`/`GlassesSlot` occupants (`SensorLenses` is a real class), `Parent.HasInternals && Parent.InternalsOn`, `StatusUpdates` evaluators (`IsOxygenCritical()` etc.).
- **Vanilla mod system loads data only** (`ModData.GameDataFolder()`; no assembly loading), so BepInEx remains the code-mod vehicle — your three working mods already prove the whole toolchain against this exact game version.

---

## 3. Load-bearing APIs (verified in this decompile)

The mod's entire contact surface with the game. Everything below was read directly in the source this session.

### Rendering (prefab-less UI)
| Need | API | Where |
|---|---|---|
| Managed window (settings, profile editor) | subclass `ImGuiWindow`, `ImGuiWindowManager.Open/Close` | `UI\ImGuiUi\ImGuiWindows\ImGuiWindowManager.cs:20` |
| Full-screen HUD/radial draws inside the game's ImGui frame | Harmony postfix on `ImGuiWindowManager.Draw()` (runs every gameplay frame between `ImGui.NewFrame()` and `ImGui.Render()`), draw via `ImGui.GetForegroundDrawList()` or a borderless fullscreen window | `Assets\Scripts\UI\ImGuiManager.cs:161` (`RenderOverlay` shows frame order) |
| Textures/icons in ImGui | `ImGuiManager.ImGuiPointerFor(Texture)`; `thing.GetThumbnail()`; `Slot.SlotTypeIcon` | `ImGuiManager.cs:253`, `Thing.cs`, `Slot.cs` |
| Vanilla font | `Localization.PushFont()` (see `ImguiCreativeSpawnMenu.Draw`) | `Assets\Scripts\UI\ImGuiUi\ImguiCreativeSpawnMenu.cs` |
| Click-through control | `ImGuiManager.SetBlockUguiClicks(bool)` | `ImGuiManager.cs:82` |

### Input & cursor
| Need | API |
|---|---|
| Poll keys (console-safe) | `KeyManager.GetButton/GetButtonDown/GetButtonUp(KeyCode or KeyMap.X)` |
| Event bindings with input-state filtering | `new KeyWrap(...)` + `keyWrap.Bind(InputPhase.Down, action, KeyInputState.Game)` (`InputSystem\KeyWrapBindings.cs`) |
| "Is gameplay input allowed" guards | `KeyManager.IsMenuInputAllowed`, `ConsoleWindow.IsOpen`, `InputWindowBase.IsInputWindow`, `ImguiCreativeSpawnMenu.Show`, `GameManager.GameState == GameState.Running` |
| Unlock cursor while radial is held | implement `IModal { UnlockCursor => true }`, `MouseModeController.AddModal/RemoveModal` (`Assets\Scripts\MouseModeController.cs:30` — proven in SprayColor mod) |
| Block game keys while radial open | `KeyManager.SetInputState("UI Ascended_Radial", KeyInputState.Typing)` / `RemoveInputState` |

### Inventory read model
| Need | API |
|---|---|
| Local player | `InventoryManager.ParentHuman` |
| Hands | `InventoryManager.ActiveHandSlot`, `.LeftHandSlot/.RightHandSlot`, `Human.LeftHandSlot/RightHandSlot` |
| Named equipment | `Human.ToolbeltSlot/BackpackSlot/SuitSlot/HelmetSlot/GlassesSlot/UniformSlot` |
| Slot contents | `slot.Get()`, `slot.Get<T>()`, `slot.Contains<T>()`, `slot.IsEmpty()`; children via `thing.Slots` (recurse for "battery inside Heavy Miner") |
| Compatibility checks before offering a move | `Slot.AllowMove/AllowSwap/CanMerge/CanInsert`, `slot.IsLocked`, `thing.GetFreeSlot(Slot.Class[, exclude])` |
| Live updates for HUD | `InventoryManager.OnActiveHandChanged` (static event, InventoryManager.cs:116), `HumanHandsBehaviour.SwapHandsEvent`, `Slot.OnEnter/OnExit` |
| Tool state for radial labels | `thing.OnOff`, `Interactable.State`, `StatusUpdates` evaluators for vitals |

### Mutations (ALL multiplayer-safe — the only ways UI Ascended may change state)
| Action | API |
|---|---|
| Move item → slot | `OnServer.MoveToSlot(thing, slot)` (`OnServer.cs:60`) or `slot.PlayerMoveToSlot(thing)` (adds SFX/hand bookkeeping) |
| Swap two slots (both hands full case) | `OnServer.SwapSlots(...)` (`OnServer.cs:512`) / `slot.PlayerSwapToSlot(slot)` |
| Merge into existing stack | `Thing.Merge(parent, child)` (`Thing.cs:3864`), gate `Slot.CanMerge` / `IMergeable.CanStack`, `IsStackFull` |
| Toggle/mode a tool | `thing.Interact(InteractableType.OnOff / .Mode, state)` (`Thing.cs:3775/3846`) or `interactable.PlayerInteractWith()` |
| Battery/cartridge swap | move old out (`OnServer.MoveToSlotOrWorld(tool.Battery, handSlot)`), move new in (`OnServer.MoveToSlot(cell, tool.BatterySlot)`) — mirrors vanilla `PowerTool.AttackWith` |
| Smart stow (vanilla fallback) | `InventoryManager.SmartStow(slot)` (`InventoryManager.cs:1809`) |

**Iron rule:** never call `DynamicThing.MoveToSlot`, `Slot.Take`, or mutate `Quantity` directly on a client — that's the desync path. The server trusts move messages (no ownership check in `MoveToSlotMessage.Process`), so the funnel always works, but only the funnel is replicated.

---

## 4. Risks, ranked

1. **Beta churn.** You track beta builds (two snapshots in this workspace, ~2 weeks apart). Mitigations: the core funnel (`OnServer`, `Slot`, `Interact`) is identical across both snapshots; keep the Harmony patch surface tiny (SmartStow prefix + one ImGui draw hook + optional HUD-hide); use SprayColor's fail-soft `TryPatch` pattern so one broken patch degrades a feature instead of killing the mod; re-dump and diff on each update.
2. **Multiplayer feel on laggy links.** Actions are echo-round-trip (client sends message, server applies, state syncs back). On your ~180ms+ RocketWerkz connection, a radial-triggered equip lands visibly later than singleplayer. Mitigations: optimistic *UI-only* highlight (never optimistic state mutation); one action = one message (no bulk loops — a "stow all" must throttle); design multi-step swaps (battery A out → battery B in) to fire step 2 off the slot-change event, or accept the vanilla two-click semantics. Avoid anything that fires `MoveToSlot` per-frame.
3. **HUD replacement fragility.** `PlayerStateWindow.Update`, `StatusUpdates.HandleDamageIndicators`, etc. dereference serialized fields unguarded. Hide via `Canvas.enabled` / `SetUIPanelVisibility` / `UiComponentRenderer` alpha only; keep the objects alive. Ship the UI Ascended HUD as *overlay first, vanilla-hide second* (separate toggles) so users can bail out per-element.
4. **Input-conflict papercuts.** MMB-hold vs ping, R-hold vs active-hand-slot, radial-open key leaking into gameplay. Use `KeyManager.SetInputState` while radials are open, hold-vs-tap thresholds (~150–200 ms), and make every UI Ascended binding configurable (`BepInEx` `KeyboardShortcut` config, as in SprayColor).
5. **ImGui frame-order coupling.** Draw hooks must run between `NewFrame`/`Render` — patch `ImGuiWindowManager.Draw` (inside the gameplay branch of `RenderOverlay`), guard on `GameManager.GameState == Running`, `!ImGuiLoadingScreen.IsShowing`, `!GameManager.IsBatchMode` (dedicated servers run headless — every UI path must no-op there).
6. **Curved visor expectations.** ImGui can fake curvature (status strip as arc-positioned segments, side visor lines via draw-list arcs) but not true distortion/parallax. That's the prefab/shader phase — exactly where the proposal already schedules it (Phase 7 hardcore/diegetic polish). Set expectations in the POC: "flat-but-clean first."
7. **Distribution.** Vanilla workshop mods are data-only; BepInEx installs stay manual (your current model) or via the community StationeersLaunchPad/StationeersMods route for workshop-visible code mods. Decide at ship time; irrelevant for the POC.

---

## 5. Recommended plan

Re-sequenced from the proposal: the radial goes **first** (highest daily-play value, lowest risk, best ImGui showcase), the HUD overlay second (additive before subtractive), and all prefab/visual polish last — matching your "POC in ImGui, then make it cool" strategy.

### Phase 0 — Skeleton + ImGui hello world *(1 evening)*
New `StationeersUIAscended` project cloned from the SprayColor csproj (net48, BepInEx 5, Harmony), adding references to `RG.ImGui.dll` + `RG.ImGui.Unity.dll` from `rocketstation_Data\Managed`.
- Postfix `ImGuiWindowManager.Draw` → draw a test circle + text via `ImGui.GetForegroundDrawList()` with the game font pushed.
- Prove an `ImGuiWindow` subclass opens via `ImGuiWindowManager.Open` (this becomes the settings window).
- Guards: `GameState.Running`, `!IsBatchMode`, fail-soft TryPatch.
- **Exit test:** overlay visible in a live game; nothing drawn in menus/loading; dedicated server unaffected.

### Phase 1 — Toolbelt radial (the flagship POC) *(1–2 weekends)*
Hold-MMB (configurable; hold-threshold so tap-ping still works) → `IModal` cursor unlock + `SetInputState` → pie menu of `ToolbeltSlot.Get()?.Slots` occupants drawn with `GetThumbnail()` icons, name, charge; release → equip:
- empty active hand → `activeHand.PlayerMoveToSlot(tool)`
- occupied → `OnServer.SwapSlots(belt, human, toolSlotIdx, activeHandIdx)`
- Port a public Dear ImGui pie-menu pattern rather than inventing sector math.
- **Exit test:** equip/swap from a 6+ tool belt in singleplayer AND on a remote server; no ghost items after 30 min of abuse on a laggy link.

### Phase 2 — Tool radial (hold-R) *(1 weekend)*
Context radial for `ActiveHandSlot.Get()`: entries generated from the tool's populated `Interactable` fields (`InteractOnOff` → "Toggle", `InteractMode` → "Mode: X", …) invoking `thing.Interact(type, state)`; plus a **Slots** branch listing the tool's child slots.
- **Exit test:** welder on/off, drill mode cycle, tablet cartridge eject — all replicate on a server.

### Phase 3 — Slot swap discovery *(1–2 weekends)*
Selecting a slot in the Slots branch scans accessible inventory recursively (`Human.Slots` → occupants → `thing.Slots`, depth-capped, skipping `IsLocked`), filters by `Slot.AllowMove`, labels entries `Battery: Toolbelt` / `Battery: inside Mining Drill` with charge, warns when the source tool would lose power. Swap = move-out then move-in (second step on slot-change confirmation).
- **Exit test:** the proposal's §14.2 welder-battery flow end-to-end in multiplayer, including a battery pulled from inside another tool.

### Phase 4 — SmartStow+ *(1 weekend)*
Harmony prefix on `InventoryManager.SmartStow`: (1) merge into matching partial stack (`CanMerge`/`Thing.Merge` — vanilla lacks this), (2) bag-profile match, (3) same-item-type bag memory; no match → run vanilla. Settings in the Phase 0 window.
- **Exit test:** steel sheets stack into the construction bag from the proposal's §14.3 flow; disable mod → vanilla behavior untouched.

### Phase 5 — Bag profiles *(1–2 weekends)*
Profile model (categories + prefab hashes + priorities), XML import/export (System.Xml ships; schema per proposal §9.3), per-bag assignment keyed by the bag's `ReferenceId` per save, ImGui profile editor + per-bag assignment UI. Ship starter profiles (Construction/Atmo/Electrical/Farming).

### Phase 6 — Two-hand HUD + status strip *(2 weekends)*
Additive ImGui HUD first: two hand boxes (icon/name/charge/state via `OnActiveHandChanged` + `Slot.OnEnter/OnExit`), slim top status strip (`StatusUpdates` evaluators, `PlayerStateWindow` values), compact vitals. Then optional per-element vanilla hiding via the game's own visibility paths. Hardcore gating (helmet/sensor checks) as a toggle — it's ~a day once the strip exists.

### Phase 7 — Nested bag radials *(1–2 weekends)*
Backpack radial → bag radials → category pages; item pick uses Phase 1 equip logic. Pure UX iteration on existing primitives.

### Phase 8 — The "make it cool" phase (prefabs/UGUI)
Only after the interaction model is proven: Unity project + asset bundles (or runtime-built UGUI), curved visor via shader/mesh (building on `FirstPersonHelmetOverlay`), animated transitions, custom art. This is also the decision point for the proposal's §17.5 (mod vs. RocketWerkz pitch) — by then you'll have gameplay video of the full interaction model, which is a far stronger pitch than mockups, and the POC contains zero AI-generated art to remake.

**MVP cut (proposal §20 item 6):** Phases 0–1 + Phase 4. Radial tool selection + smarter stow is a complete, demoable, daily-driver mod on its own.

---

## 6. The proposal's open questions — answered by the code

| Question (§17) | Answer from the decompile |
|---|---|
| R: press or hold? | **Hold.** Tap-R is vanilla `ActiveHandSlot`; hold avoids stealing it. |
| MMB confirm on release or click? | **Release** — matches the modal cursor pattern and Helldivers-style muscle memory; keep a tap-passthrough for ping. |
| Left vs right hand decision? | Follow vanilla: act on `ActiveHandSlot`; `E` already swaps hands. Preferred-hand rules = later setting. |
| Both hands occupied? | `OnServer.SwapSlots` with the active hand — same as vanilla drag-swap semantics. |
| Smart stow search depth? | Vanilla: worn slots → original slot → open windows → 1 level inside worn containers. UI Ascended profiles extend depth deliberately (config: nested-bag stow on/off, matching §8.3). |
| Batteries inside other tools available? | Technically trivial (recursive slot walk). Gate behind a setting + show the consequence line ("Mining Drill will be unpowered" — readable from the source tool's `OnOff`/battery state), per §7.3. |
| Profiles per save or global? | **Definitions global** (XML files in the plugin config dir), **assignments per save** (bag `ReferenceId` → profile name), since ReferenceIds are save-scoped. |
| Curved visor default? | Off/flat default. ImGui approximates arcs; real curvature is Phase 8. |
| Hardcore mode: gameplay/server/client? | Client preference only, until/unless a server-side companion exists. It's cosmetic gating of client-drawn UI, so server enforcement is a non-goal for the mod. |
| Mod, pitch, or both? | Mod first. Vanilla adopting G-SmartStow proves RocketWerkz ships ideas from this space; a working mod with MP-safe behavior is the strongest possible pitch artifact. |

---

## 7. Why this will probably work (evidence trail)

- Your three existing mods already exercise every technique UI Ascended needs: Harmony patching UGUI singletons (PasswordFix), `MouseModeController` modal + custom GUI + `OnServer.*` server-trusted calls (SprayColor), console-command invocation (TestSpawn) — all against this exact game install.
- The game's own creative spawn menu (`ImguiCreativeSpawnMenu`) is the existence proof for "complex interactive ImGui UI with cursor unlock and key capture, in-game" — UI Ascended's radials are the same category of thing.
- Vanilla `SmartStow` + the `Slot.PlayerXxx` wrappers are the existence proof that programmatic, UI-initiated, multiplayer-replicated inventory moves are a supported first-class pattern, not a hack.
