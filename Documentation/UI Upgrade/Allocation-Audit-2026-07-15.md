# Allocation & GC Audit — 2026-07-15

Triggered by FlorpyDorp's discussion with RocketWerkz developers: *"execution time alone is
not a useful metric — GC and managed allocations are often the larger performance issue in
Unity/Mono."* This is a critical engineering review of ALL mod code, assuming it must run in
a large Stationeers world without adding GC pressure.

**Method:** four parallel auditors (profiler / per-frame HUD / mesh+effects / Harmony+radial+
ImGui), each with the full allocation checklist, followed by **adversarial verification of
every individual finding** against the actual code (to kill the false positives that plague
LLM allocation audits — struct enumerators, compiler-cached lambdas, cold paths). Results:
**32 raw findings → 28 confirmed + 1 upgraded + 3 downgraded, 0 fabrications survived.**
Confirmed severity split: 2 high, 14 medium, 13 low.

---

## 1. Was the code intentionally written to minimize allocations?

**Yes — as an explicit architecture — and the audit confirms the architecture held.**
Techniques in force, verified by the auditors:

- **Dirty-guard discipline everywhere**: every mesh-affecting property (Sheen, Spec, Glow,
  ripple, shape — ~15 per PanelGraphic) NaN-guards, clamps, and `Mathf.Approximately`-guards
  before `SetVerticesDirty()`. Steady state = **zero mesh rebuilds** (confirmed live: a 10s
  profiler window recorded no `Hud.Mesh.*` samples at all).
- **Static scratch buffers** for mesh generation (`_stopD/_stopC/_stopM/_inW/_miter`,
  polyline scratch) — no per-rebuild arrays. uGUI's own `s_VertexHelper` grows once to the
  high-water mark and is reused, so even a 10k-vert glow rebuild is GC-free after warmup.
- **`readonly struct` profiler scopes**: `ProfilicusUniversalis.Time()` returns a struct;
  `using` over the concrete struct type compiles to a direct `Dispose()` call — **no
  IDisposable boxing**, ever. Hidden profiler = a stack struct and an early-out.
- **Change-caches on string-producing paths**: readout values, jetpack thrust/propellant,
  vitals numbers, world name, day counter all rebuild their strings only when the underlying
  value changes (`_lastX` + cached string pattern).
- **Reference-keyed parse caches**: palette colors re-convert only when the config string
  *reference* changes; bloom/frost tints likewise; bare-senses CSV re-parses on change only.
- **Cached `Shader.PropertyToID`** ints; material property setters (no alloc) for all
  per-frame uniforms; persistent RT pyramids rebuilt only on resolution/step change.
- **Local functions never converted to delegates** (verified per call site): the mesh
  builders' capture frames stay on the stack — no closure allocations per rebuild.
- **Idempotent material assignment** (`HudFxMaterials.Assign` early-outs when assigned);
  no per-frame `GetComponent` in the steady-state path (borrow widgets use the List-buffer
  `GetComponentsInChildren` overload).
- **No LINQ anywhere in a hot path** (confirmed by sweep); string-keyed dictionaries with
  explicit comparers (no enum-key comparer boxing on hot ops).

**Where intent failed** (the confirmed findings): string *formatting* edges that slipped
through the change-cache discipline, one per-frame Unity-API array copy, one per-frame
`GetComponentsInChildren`, and the profiler's own display drawing. Details below.

## 2–3. Confirmed allocation sources (why / frequency / importance / fix)

### Steady-state gameplay, per frame — the ones that matter (all FIXED as of this audit)

| # | Site | What allocates | Rate | Fix |
|---|---|---|---|---|
| HIGH | `HudSampler.cs:303` | `$"{m/60:00}:{m%60:00}"` → `string.Format(object,object)`: **2 int boxes + string, every frame**, for a value that changes once per in-game minute | 60/s | change-cache on minutes |
| HIGH | `HudSampler.cs:305` | `date.ToString("yyyy-MM-dd")` every frame; changes once per in-game day | 60/s | change-cache on day |
| MED | `CompassWidget.cs:155` | heading int + `"°"` concat per frame, unguarded | 60/s | cache on rounded int |
| MED | `ReadoutWidget.cs:250` | `Enum.TryParse` of the param-bag source string per frame per readout | 60/s × N | cache keyed on string ref |
| MED | `EquipmentColumnWidget.cs:140` | `(i+1).ToString()` ×6 per frame for constant digits | 360/s | static `{"1".."6"}` table |
| MED | `VanillaIcons.cs:92` | `curve.keys` **copies the whole keyframe array** per call per frame | 60/s | non-allocating indexer |
| MED | `VanillaIcons.cs:244` | `key.ToLowerInvariant()` per state-chip lookup per frame | 60/s × 3 | OrdinalIgnoreCase compares |
| MED | `HudBackdrop.cs:179` | `GetComponentsInChildren<Camera>` **array per frost tick** (my helmet fix from this morning — caught by the audit within hours) | 60/s | cache resolved camera on main-camera identity |

### While a radial is open (per-event context, still worth fixing — FIXED)

| # | Site | What | Fix |
|---|---|---|---|
| MED | `UnityRadialView.cs:628/647` | `ToUpperInvariant` per label wedge per frame (twice!) | cache display label per wedge slot |
| MED | `UnityRadialView.cs:554/559` | `letter.ToString()` / `"^"+digit` badge strings per frame — the existing `hk.text != badge` guard saved the *set*, not the *alloc* | change-guard + static digit table |
| MED | `BagProfiles.cs:168` | `Path.GetInvalidFileNameChars()` char[] per bag-wedge lookup per frame | static hoist |
| LOW | `RadialMenu.cs:1335`, `:120`, `UnityRadialView.cs:707` | KeyCode.ToString, GetRange sublist on paged rings, live ValueText delegates | documented, deferred |

### The profiler's own drawing — the observer effect (FIXED)

| # | Site | What | Impact |
|---|---|---|---|
| MED | `ProfilicusUniversalis.cs:398,342` | **~9 `ToString` per metric row per frame while visible** (~90–180 strings/frame at 10–20 rows) | The profiler *inflated the two numbers it exists to report*: its previous-frame draw cost lands inside `Frame.Total`, and its string garbage lands inside `GC.Alloc KB/frame`. Fixed by building row strings inside the existing 0.5s refresh throttle. |
| LOW | `:273` header concat (object[] + int box), `:231` `List.Sort(Comparison)` internal wrapper | 60/s + 2/s while visible | header cached; comparer noted |

### Editor-only (F9/F10 open — the designer's own workstation, rated honestly)

- MED `HudEditorWindow.cs:385` — `ListProfiles()` did **disk IO + string allocs every
  frame** while the Designer header was open (FIXED: cache, refresh on combo-open/save).
- MED `RadialEditorMode.cs:84` — demo radial entries rebuilt every frame (deferred).
- MED `HudPropDrawer.cs:52` — `"##hp"+i` concats + property boxing per prop per frame while
  an element popup is open (deferred).
- LOW — palette snapshot dictionaries per frame while editors are open (undo machinery),
  popup title enum concats, `string.Join` hot-signature (all deferred, documented).

### Downgraded / refuted by verification (examples of why the adversarial pass matters)

- `DynamicTextWidget:112` clock `+ " UTC"` concat — real but tiny and downstream of the
  HudSampler fix (the source string now changes once a minute).
- `HudEditorWindow:788` per-frame palette snapshot — only taken while an ImGui item is
  *actively being edited*, not every frame.
- Every claimed `foreach` allocation was refuted (all List/Dictionary struct enumerators).
- The `using (Time(...))` scopes were confirmed **zero-alloc** — auditors were explicitly
  hunting for boxing there and proved the struct never boxes.

## 4. UI rebuild discipline

**We do NOT rebuild UI per frame.** Meshes rebuild only on real value changes (verified
guard-by-guard); the breathing pulse tints via `CanvasRenderer.SetColor` without re-meshing;
compass ticks/moodlets move by transform, not mesh. Two exceptions found:
1. `CompassWidget` dirtied its caret mesh unconditionally every frame (the caret never
   moves) — the single per-frame mesh rebuild in the HUD. **Fixed.**
2. Radial wedges DO rebuild per frame while a radial is open — **by design** (hover bulge
   and rim lerps animate the geometry). The rebuild itself allocates nothing (struct verts
   into reused buffers); it's CPU-bounded, not GC-bounded, and it's an interaction context.

Text: TMP setters are change-guarded through `HudText.Set`; the gaps were the *string
construction* before the guard (the findings above), not the UI layer itself.

## 5. Harmony patches

All 12 patches inventoried with per-invocation verdicts. The per-frame ones are clean:
`ImGuiWindowManager.Draw` postfix (a null-check + delegate into our draw), `AllowMouseControl`
getter (one bool test), `CheckDisplaySlotInput` (one static read), `UpdateJetpackPanels`
(one bool). `HandleJump` boxes a KeyInputState enum via reflection — but only on an actual
jump while a radial is open (per-event). `CommandLine.Process` / `SmartStow` / spawn patches
allocate on invocation but are user-input-cold. **No patch allocates per frame.** No
patch-related delegate churn; patched once at load, unpatched on destroy.

## 6. Does the profiler distort what it measures?

**When hidden (the shipped default): no — verified zero-alloc, zero-work.** Scopes are
stack structs; `Record` early-outs before touching any collection; the GC monitor doesn't
even read the heap size when hidden.

**When visible: yes, it did — materially.** Its per-frame row rendering allocated ~90–180
strings/frame, which (a) fed back into `GC.Alloc KB/frame` (it measured its own garbage) and
(b) inflated `Frame.Total` (measured as wall-clock including the previous frame's profiler
draw). The A/B driver was doubly affected: it force-shows the window during both capture
phases, and the ON phase draws more rows than OFF, so the draw cost didn't fully cancel in
the delta. **Fixed** by moving all cell formatting into the 0.5s-throttled refresh; the
window now allocates ~2×/sec instead of per frame. Residual, acceptable: the
`ConcurrentQueue` segment allocation (~one 32-slot block per 32 samples while enabled) and
snapshot writing (one-shot bursts, deliberately excluded from windows).

## 7. Why our profiler cannot reliably report managed allocations

- **The runtime**: Stationeers ships Unity's old Mono with the **Boehm-Demers-Weiser GC** —
  conservative, non-moving, non-generational in the .NET sense, **stop-the-world**. There
  are no allocation callbacks, no ETW-style events, no per-thread allocation counters in
  the .NET 4.x profile the game exposes. Nothing tells you *who* allocated.
- **What we can read is a heap-size snapshot** (`Profiler.GetMonoUsedSizeLong`), and a
  positive frame-to-frame delta is only a **lower bound** on allocation: any frame where a
  GC ran (or the heap shrank) reports zero or garbage-minus-collection — meaning the
  technique **undercounts hardest exactly when churn is worst**. Objects born and collected
  within one frame are invisible entirely.
- **No attribution**: the heap is process-global. Our delta includes the game's own
  allocations, every other mod, and background threads. We cannot say "this KB came from
  scope X" — only controlled A/B toggling (same scene, same camera, effect on vs off)
  yields a *statistical* attribution, which is exactly why the A/B driver exists.
- **Why timing alone misleads** (the RocketWerkz point, and they're right): a scope that
  runs in 0.2 ms but allocates 50 KB/frame looks innocent in a timing table while it
  schedules multi-millisecond stop-the-world pauses that land *later*, attributed to
  nothing — the frame spike shows up in `Frame.Total` on some unrelated frame. On Boehm,
  allocation rate ≈ future pause frequency. Microbenchmarks that report only mean
  wall-clock hide this cost completely, which is precisely why experienced Unity teams are
  skeptical of them.

## 8. Could we improve the profiler?

What's actually available on this Unity/Mono (2022.3 player, .NET 4.x Mono):

| API | Status | Value / limitation |
|---|---|---|
| `Profiler.GetMonoUsedSizeLong/GetMonoHeapSizeLong` | **in use** | whole-heap snapshot; lower-bound deltas as discussed |
| `GC.CollectionCount(0)` | **in use** | exact and cheap; collection *frequency* is the best churn proxy we have |
| `GC.GetTotalMemory(false)` | fallback in use | same limits, less precise than the Unity binding |
| `ProfilerRecorder` ("GC Allocated In Frame", Unity.Profiling) | **worth probing** | would give the real per-frame allocation number; but most memory counters only emit in **development builds** / with the profiler active — must be probed at runtime and fail soft to our delta method. Worth adding: if the player is a dev build, we get truth; else we degrade. |
| `GC.GetAllocatedBytesForCurrentThread()` | probe via reflection | .NET Core-era API; some Unity Mono profiles expose it, ours may not. If present: main-thread-only attribution, a big step up. One-time reflection probe, cached delegate, fail-soft. |
| Unity Memory Profiler package | no | editor tooling, not shippable in a mod |
| Per-scope heap sampling (before/after a `Time` scope) | rejected | a GC inside the scope corrupts the sample; only meaningful for large allocations; adds two native calls per scope |

Recommended (cheap) upgrades: the `ProfilerRecorder` + `GetAllocatedBytesForCurrentThread`
runtime probes with fail-soft rows, and labeling the existing alloc row "≥ KB/frame (lower
bound)" so nobody over-trusts it. The honest architecture remains: **exact collection
counts + lower-bound bytes + controlled A/B deltas**, which is a defensible answer to the
Dean Hall critique — we measure allocation *consequences* exactly and allocation *volume*
conservatively, and we never present timing without the memory columns beside it.

## 9. Allocation health score

**Before this audit's fixes: 7/10.** After the fix batch (all HIGHs, all steady-state and
radial MEDIUMs, the profiler observer effect, the editor disk-IO): **9/10.**

Being brutally honest about both directions:
- The architecture was genuinely built for zero steady-state allocation, and the audit
  *proved* the big claims: no per-frame mesh rebuilds, no closure/boxing leaks in the mesh
  pipeline, struct profiler scopes, clean Harmony patches, a truly zero-cost hidden
  profiler. Four adversarial auditors failed to find a single LINQ call, per-frame
  collection allocation, or delegate churn in a hot path. That is *rare* for a mod of this
  size.
- But the string-formatting edges were real, shipped, and per-frame — the clock alone was
  ~5 allocations every frame for a value that changes once a minute; and the profiler
  polluted its own two headline metrics while open, which would have been embarrassing in
  the RocketWerkz conversation. The helmet-camera fix I wrote *this morning* allocated an
  array per frame — caught by this audit hours later, which is exactly why reviews like
  this need to be recurring, not one-time.

**What I'd insist on before a 1.0 release (professional review hat on):**
1. All HIGH/MED steady-state fixes in (done in this pass) — and re-verify with a 10-minute
   `GC.Alloc` soak in a busy base, radials open and closed, editors closed.
2. The profiler alloc row relabeled as a lower bound + the ProfilerRecorder probe added.
3. The deferred editor-open items (demo rebuild, prop-drawer boxing) fixed before any
   version where players live in F9 for long sessions — they're the designer's workbench.
4. A CI-style rule for future code: any string on a per-frame path must go through a
   change-cache; any `GetComponents*` on a per-frame path must use the List-buffer overload
   or an identity cache. (Both patterns already exist in-repo to copy.)
5. Exception-storm hygiene: the widget try/catch-per-slot style is free until something
   throws every frame; the 3-strike log suppressor caps the logging but not the Exception
   objects. Acceptable risk today; worth a per-widget disable latch eventually.
