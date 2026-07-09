# ScriptEngine Hot Reload for this SLP Mod

**Audience:** JacksonTheMaster (and his Claude). **Goal:** edit C#, press F6, see it in game in
~3 seconds — no Unity Editor, no game restart, no AssetBundle.

This is how the ImGui radial and the new procedural UGUI radial were both iterated on. It is
worth 20 minutes of setup; it turns a 3-minute round-trip into a 3-second one.

> **TL;DR:** ScriptEngine only instantiates `BaseUnityPlugin` classes. Our SLP entrypoint is a
> plain `MonoBehaviour` with `OnLoaded(...)`, so ScriptEngine loads the DLL and does *nothing*.
> `Dev/ScriptEngineLoader.cs` is a dev-only shim that impersonates SLP. It must also prune
> LaunchPadBooster's `Mod` registry, or the **second** F6 crashes.

---

## 1. Install ScriptEngine (once)

ScriptEngine ships with [BepInEx.Debug](https://github.com/BepInEx/BepInEx.Debug) (r11).

1. Drop `ScriptEngine.dll` into `<Game>\BepInEx\plugins\`.
2. Create `<Game>\BepInEx\scripts\` — this is the watch folder.
3. Launch once, then set `<Game>\BepInEx\config\com.bepis.bepinex.scriptengine.cfg`:

```ini
[AutoReload]
EnableFileSystemWatcher = true   # reload automatically when the DLL changes
AutoReloadDelay = 3              # seconds
DumpAssemblies = true            # optional: helps debugging

[General]
LoadOnStart = true               # load scripts/ DLLs at launch
ReloadKey = F6                   # manual reload
```

**Do not use BepInEx.ScriptLoader** (the loose-`.cs` one). It throws `Assembly.LoadFile` errors
every frame in this install (conflicts with BepInEx's `HarmonyInteropFix`). ScriptEngine loads
*compiled DLLs*; that is what we want.

---

## 2. Why a plain SLP mod does not hot reload

ScriptEngine scans a reloaded assembly for **`BaseUnityPlugin` subclasses** and `AddComponent`s
them onto a GameObject. Our shipped entrypoint is:

```csharp
public sealed class StationeersUIMod : MonoBehaviour
{
    public void OnLoaded(List<GameObject> prefabs, ConfigFile config) { ... }  // SLP DefaultEntrypoint
}
```

That is StationeersLaunchPad's "DefaultEntrypoint" shape. ScriptEngine has never heard of SLP, so
it instantiates nothing and the mod silently does not run.

**Fix:** `Dev/ScriptEngineLoader.cs` — a `[BepInPlugin]` `BaseUnityPlugin` that stands in for SLP:
it attaches the real entrypoint to its own GameObject and calls `OnLoaded(prefabs, Config)` with a
BepInEx `ConfigFile` (the same type SLP passes).

It lives **outside `Assets/`**, so Unity never compiles it and the shipped mod never contains it.

### The landmine: LaunchPadBooster's Mod registry

```csharp
// LaunchPadBooster/Mod.cs
public Mod(string name, string version) {
    ModsByHash.Add(Hash, this);   // Dictionary.Add -> THROWS on duplicate key
    AllMods.Add(this);
}
```

`Hash` is derived from the mod **name**. `LaunchPadBooster.dll` is *never* reloaded, but our
assembly is recreated on every F6 — so the static `readonly Mod MOD = new Mod("StationeersUIMod", ...)`
re-registers and the **second reload dies** with a `TypeInitializationException`.

The shim prunes `Mod.AllMods` (public) and `Mod.ModsByHash` (internal, via reflection) **before**
anything touches the `StationeersUIMod` type. Order matters: reading a `const` is compile-time
inlined and safe; touching a static property triggers the initializer.

---

## 3. Building without Unity

Unity compiles `Assets/Scripts/StationeersUIMod` via `Assets/StationeersUIMod.asmdef`. For code-only
changes that is far too slow. `Dev/StationeersUIMod.Dev.csproj` compiles the **same sources** plus
the shim with `dotnet`, and copies the DLL straight into `BepInEx\scripts`:

```powershell
cd Dev
dotnet build -c Debug     # AfterTargets=Build copies to BepInEx\scripts
```

Or in VS Code: **`Ctrl+Shift+B`** → *"UI Mod: Build + Hot Reload (ScriptEngine)"*.

Requirements baked into that csproj:

- `<TargetFramework>net48</TargetFramework>`
- **`<DebugType>embedded</DebugType>`** — ScriptEngine needs embedded PDBs
- References mirror the asmdef's `precompiledReferences`, all `Private="false"`, resolved from the
  live game install. **If you add a reference in the asmdef, add it to the csproj too** — they are
  two lists of the same thing and will drift.
- `UnityEngine.UI.dll` + `Unity.TextMeshPro.dll` are needed for the procedural UGUI radial.
- `UnityEngine.AnimationModule.dll` is needed because Booster hashes mod names with
  `Animator.StringToHash`.

Unity is still required for prefabs, scenes and asset bundles — just not for C#.

---

## 4. Deployment rules (violate these and you will lose an hour)

| Location | Should contain |
|---|---|
| `BepInEx\scripts\` | `StationeersUIMod.dll` ← the hot-reloadable copy |
| `BepInEx\plugins\` | **no** copy of our mod |
| `<Game>\mods\`, `Documents\My Games\Stationeers\mods\` | **no** copy of our mod |

If SLP also loads the mod from a `mods` folder, the shim detects it and stays inert (it logs a
warning) — otherwise you would get two instances patching the same methods. Exactly one copy, in
`scripts`, while iterating.

---

## 5. Writing hot-reload-safe code

ScriptEngine destroys the plugin GameObject and loads a **new assembly**. It does **not** undo your
side effects. Every reload must clean up after itself, or you double-patch and leak.

`StationeersUIMod.OnDestroy()` must:

```csharp
_harmony?.UnpatchSelf();          // or the next load patches the same method twice
UnityRadialView.Shutdown();       // destroy runtime-created Canvases / GameObjects
_radials?.ShutdownImmediate();    // release cursor modal + KeyManager input state
_hud?.RestoreVanillaIfNeeded();   // un-hide vanilla panels
Instance = null; _loaded = false; // reset statics
```

Rules of thumb:

- **Anything `static` survives conceptually but is re-created** in the new assembly. Statics in
  *other* assemblies (Booster, the game) persist — that is the `ModsByHash` bug.
- **Every `new GameObject(...)` you create must be destroyed** in `OnDestroy`, or reloads stack
  canvases/objects forever.
- **Every Harmony patch must be un-patched.** `UnpatchSelf()` on your own instance.
- **Any global state you flip must be restored**: `KeyManager.SetInputState`,
  `MouseModeController.AddModal`, `ImGuiManager.SetBlockUguiClicks(true)`. Leaving one of these set
  freezes the game's input (we hit this twice).

---

## 6. Confirming the reload actually happened

Print to the **F3 console** on load and unload — otherwise you cannot tell whether F6 did anything:

```csharp
ConsoleWindow.Print($"[StationeersUIMod] v{VersionDisplay} loaded ({DateTime.Now:HH:mm:ss}).",
                    ConsoleColor.Cyan);
```

Cross-check `BepInEx\LogOutput.log` for:

```
[Info :Script Engine] Loading plugins from ...\BepInEx\scripts\StationeersUIMod.dll
[Info :Script Engine] Loading com.stationeersuimod.ui.scriptengine
[Message:Script Engine] Reloaded all plugins!
```

---

## 7. Troubleshooting

| Symptom | Cause |
|---|---|
| F6 does nothing, no console lines | DLL is in `plugins\`, not `scripts\`; or no `BaseUnityPlugin` in the assembly |
| First F6 works, second throws `TypeInitializationException` | Booster `ModsByHash` duplicate — the shim's prune did not run before the mod type was touched |
| Mod loads twice / double patches | A copy also exists in `plugins\` or an SLP `mods\` folder |
| Game input frozen after a reload | A modal / input-state / `SetBlockUguiClicks` was not released in `OnDestroy` |
| Reload leaves ghost UI on screen | A runtime-created `GameObject`/`Canvas` was not destroyed |
| `Assembly.LoadFile` exceptions every frame | You installed **ScriptLoader**, not ScriptEngine — remove the loose `.cs` files |
| Changes to prefabs/sprites do nothing | Expected: assets need Unity + an AssetBundle. Only C# hot reloads. |

---

## 8. Files to look at

- `Dev/ScriptEngineLoader.cs` — the shim, with the Booster prune and full commentary
- `Dev/StationeersUIMod.Dev.csproj` — the no-Unity build
- `.vscode/tasks.json` — `Ctrl+Shift+B` runs the hot-reload build
- `Assets/Scripts/StationeersUIMod/StationeersUIMod.cs` — `OnLoaded` / `OnDestroy` lifecycle
