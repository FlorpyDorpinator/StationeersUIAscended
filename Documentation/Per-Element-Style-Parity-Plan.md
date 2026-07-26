# Per-Element Style Parity — bug root causes, the `HudStyleFx` registry, and inherit-from-element

**Status:** PLAN ONLY — nothing in this document has been implemented. Written for FlorpyDorp to
review and approve before any code is written.
**Author:** investigation pass, 2026-07-26. No code, config or profile was modified.
**Companion reading:** `Documentation/Config-and-Theme-Migration.md` (the migration disciplines this
plan obeys), `Documentation/Release Prep Reports/03-F9-Editor-and-Knob-Parity.md` (the audit that
first named the five-place duplication), `Documentation/Release Prep Reports/to do/README.md`
(items 9 and 10, both absorbed here).

---

## 0. The charter — FlorpyDorp's requirements, restated

These five are the brief. Everything below serves them; where I recommend deviating I say so
explicitly and give the reason.

1. **The separation bug.** *"When I take an element off global effects and edit it individually,
   the chromatic aberration and frosted blur dissipate. The element menu SAYS 'follows global
   frosted etc.' but it doesn't actually follow once you uncheck global."* Plus: *"Backdrop
   darkening, tint, downsample, re-blur say they're shared globals, but off-global they stop
   working and there's no knob to adjust them. Broken stuff related to global on/off."*

2. **Parity.** The element popup should look **1:1 like the global menu** when it is off-global.
   F9 > Effects has sub-tabs (Glass / Edges / Glow / Bloom / Alerts / Transitions / Advanced); the
   element popup has one undifferentiated Effects tab. That parity must hold across every element.

3. **Per-category follow.** Split the single global checkbox into categories that match the global
   menu. Each element gets the tabs it actually uses, with a **follow-global checkbox per
   category** (effects, view & behavior, transitions, …). Following a category ⇒ its controls are
   **hidden**. Not following ⇒ it looks **exactly** like the global menu for that category. No more
   "Inherit / On / Off" dropdown rows for transitions: if following, hide them; if not, show the
   full 1:1 controls.

4. **Seed on unfollow.** Unchecking a follow box makes the element inherit the **current global
   values** as its starting point.

5. **Inherit from another element** (new feature). A checkbox "inherit from other element", then
   you click which element to inherit from.

---

## 1. Findings — what is actually broken, and why

### 1.1 Root cause A (the big one): separation does **not** re-seed, it reveals a stale snapshot

The popup checkbox that separates an element reads:

```csharp
// HudElementView.cs:1139-1155
into.Add(WithId(HudProp.Bool("Follow F9 global style (theme + effects)",
    () => UsesGlobalStyle,
    v => {
        var slot = EditSlot(d);
        if (v) { d.SetIFor(slot, "styleSource", StyleGlobal); return; }
        if (!d.GetBFor(slot, "customStyleReady", false))     // <-- THE GUARD
            SeedCustomStyleFromEffective(d, slot);
        d.SetIFor(slot, "styleSource", StyleCustom);
    }), "styleSource"));
```

The comment three lines above it (`HudElementView.cs:1135-1138`) promises the exact opposite of
what the code does:

> *"Unchecking seeds a complete snapshot from the CURRENT globals first (reads run under the old
> mode), so every slider below pops open at exactly the value the element is already showing — no
> jump, no stale profile values."*

**The guard makes that promise false for essentially every real element.** `customStyleReady` is
sticky: once anything has ever written a snapshot — a previous separation, F9's "Snapshot ALL as
Custom" (`HudEditorMode.cs:1228-1253`), `HudStyleMigration`'s legacy fold
(`HudStyleMigration.cs:126`), or the theme author's own session — the flag stays `true` **even
after the element is put back on Global**. Unchecking then skips the seed entirely and simply
switches the resolvers over to values frozen at some unrelated past moment.

`SetUnifiedStyleSource` has the identical guard (`HudElementView.cs:1927-1929`), so the bulk
"Snapshot ALL as Custom" button has the same defect except when `forceCustomSnapshot` is passed.

**This is not theoretical — it is reproducible from FlorpyDorp's own installed profiles.**
Read from `<game>/BepInEx/config/StationeersUIMod/HudProfiles/`:

| Profile | Element | `styleSource` | `customStyleReady` | stored `customFrost` | live global `FrostStrength` | what unchecking does |
|---|---|---|---|---|---|---|
| Stationeers Blue, Zirillian Red | `7fe6ca4964ec…` | 1 (following) | true | **0.12528** | 0.87 | frost collapses to 14% of what was on screen |
| Pure HUD, Blue, Zirillian | `g2-portrait` | 2 / 1 | true | **0.46284** | 0.87 | frost halves |
| Stationeers Blue | `g2-topbar` | 2 | true | **0.49764** | 0.87 | frost halves |
| Zirillian Red | `g2-topbar` | 1 | true | **0.54** (chroma **0.471**) | 0.87 / 0.564 | frost and chroma both drop |
| all three | `bd7856c8…` | 1 / 2 | true | 0.831 | 0.87 | small drop |
| all three | `d78f4bc7…` | 1 / 2 | true | 0.85695 | 0.87 | small drop |
| Pure HUD | `g2-hands` | 2 | true | **0** (chroma **0**) | 0.87 / 0.564 | frost and chroma gone entirely |

The same table for the edge/glow family is worse, because the stale snapshots on the *shipped*
themes were written before the edge-light work landed. `g2-clock`, `g2-day`, `g2-jetpack`,
`g2-topbar` and `g2-portrait` all carry `spec=0`, `sheen=0`, `glow=0`, `glowIn=0`, `softEdge=0`
while the live globals are `GlassEdge` + a Tier-A edge-light boost (`spec` resolves to ~0.689 in
Follow mode — the value the *other* elements in the same file snapshotted). So unchecking a
follow box on those elements does not merely dim the frost; it **removes the glass rim and the
halo**, which is most of what reads as "frosted" on screen.

`ShippedProfiles.cs` carries `customStyleReady=true` on 37 elements, with the same stale
`sheen=0 / spec=0 / glow=0` sediment (e.g. `ShippedProfiles.cs:60-82`). So a brand-new install
reproduces this too.

### 1.2 Root cause B: chroma is gated on frost, so a frost drop is always a chroma drop

```csharp
// HudElementView.cs:971-981
private float ChromaAmountFor()
{
    if (FrostAmountFor() <= 0.001f || Def == null) return 0f;   // <-- hard gate
    ...
}
```

Any mechanism that lowers frost suppresses the fringe with it, and a stale `customChroma`
(0.471 where the global is 0.564) compounds it. That is why FlorpyDorp perceives "the chromatic
aberration **and** frosted blur" dying together rather than as two independent regressions — they
are one regression with a second-order term. The gate itself is correct (the fringe is produced by
offsetting the backdrop sample, `HudPanelSdf.shader:807-816`; with no frost there is nothing to
offset) and should be kept, but it must be *labelled* in the UI, which it currently is not.

### 1.3 Root cause C: two contradictory "missing key" conventions inside the same menu

The element popup presents one uniform set of rows, but the resolvers behind them use **two
opposite rules** for what an absent per-element key means:

| Helper | Missing Custom key resolves to | Used by |
|---|---|---|
| `OwnOrGlobal` (`HudElementView.cs:719-724`) | **the global** | bfade, softEdge, glow, glowIn, glowWidth, glowDiffuse, ripple, rippleFreq, edgeFlow, frostDepth, edgeLight |
| `StyleFeatureOn` (`:726-730`) | **the global** | customBorderFadeOn, customSoftEdgeOn, customGlowOn, customRippleOn |
| `NewSdfOwnOrGlobal` (`:741-748`) | **a fixed neutral** (0 / 0.35 / 0.5 / 1 / 0.6) | glowExtraDiffuse, glowHaze, glowBreath, glowUneven, glowOrganicScale, glowFlowAura |
| `NewSdfFeatureOn` (`:735-739`) | **`false`** | customGlowBreathOn, customGlowUnevenOn, customGlowFlowOn |

The `NewSdf*` pair was a deliberate, defensible choice at the time (2026-07-16: don't let a global
added *after* a snapshot silently reactivate inside that snapshot). But it means **the SDF halo
family drops to off/neutral on separation for any element whose snapshot predates the key**, with
no UI signal. `SetUnifiedStyleSourceWithoutView` bakes the same rule in
(`HudElementView.cs:2013-2018` writes `customGlowBreathOn/UnevenOn/FlowOn = false` outright when
the source was already Custom).

Structurally this is the real bug: **the convention is per-knob and invisible, so "follows global"
means one thing for `glow` and the opposite thing for `glowHaze`.** And it is self-perpetuating —
every future global knob added to the F9 tab inherits the *neutral* rule by default and therefore
silently fails to reach existing Custom elements.

One more asymmetric knob worth naming: `rippleSmooth` is hard-zeroed while following
(`HudElementView.cs:703`, `:1873`, `PrimitiveView.cs:280`) and labelled *"energy smoothness
(per-element only)"* in the popup (`:1393`). It is the one control that genuinely has no global —
honest today, but it must be modelled explicitly rather than as a special case.

### 1.4 Root cause D: the popup's displayed defaults disagree with the renderer's defaults

The Custom mirror hard-codes literals that the resolvers do not use:

| Popup row (`HudElementView.cs`) | Shows when key absent | Renderer uses when key absent |
|---|---|---|
| `gf("customFrost", 1f)` (:1534) | 1.0 | `FrostStrength` (0.87) |
| `gb("customChromaOn", true)` (:1516) | true | `FxChromaOn` |
| `gf("customChroma", 0.3f)` (:1519) | 0.3 | `FxChroma` (0.564) |
| `gf("customShine", 0.6f)` (:1507) | 0.6 | `FxShine` |
| `gf("customIrid", 0.25f)` (:1514) | 0.25 | `FxIridescence` |
| `gf("glowWidth", 24f)` (:1459) | 24 | `FxGlowWidth` |

Consequence: the slider reads a number the HUD is not rendering, and the first drag **writes that
wrong number in**. This is a second, independent source of "it changed when I touched it".

### 1.5 What is *not* broken: the shared backdrop capture

The suspicion that Custom panels are excluded from the shared frost capture is **disproven**.
There is no `styleSource` reference anywhere in `HudSystem`, `HudBackdrop` or `HudFxMaterials`
(verified by grep across the whole source tree). Every UIA panel — Global or Custom — samples the
same global textures `_UiaBlurTex0..3` published by `HudBackdrop.Dispatch`
(`HudBackdrop.cs:236-262`), and receives the same `_FrostTint` / `_FrostDarken` uniforms pushed
once per frame onto the shared `glass` / `sdfglass` materials (`HudSystem.cs:1436-1455`).

So *"backdrop darkening / tint / downsample / re-blur stop working off-global"* is not literally
what happens. What happens is:

- the element's **own** frost strength stops tracking the global slider (correct by design), while
  darkening / tint / downsample / re-blur keep applying — so the family looks half-connected;
- because of root cause A the element's own frost has usually *also* just collapsed to a stale
  value, so moving *any* frost knob appears to do nothing useful;
- the popup names those four as "shared globals (F9 -> Effects)" in a plain text header
  (`HudElementView.cs:1539-1540`) with **no way to reach them** and no statement that editing them
  affects every panel.

That is a real usability defect and it is fixable in Phase 0 without touching the renderer.

### 1.6 The physics: what can and cannot ever be per-element

This table is the honest constraint set the category design has to respect. "Shared" means one
value physically feeds every panel (a single render texture, or a single uniform on a shared
material that exists precisely so batching survives).

| Knob family | Per-element? | Mechanism / citation |
|---|---|---|
| Frost **strength**, frost **depth** | **Yes** | packed into the panel's vertex lanes, `PanelGraphic.cs:639`, decoded `HudPanelSdf.shader:680,708-709` |
| Chromatic fringe strength | **Yes** | packed lane `PanelGraphic.cs:640`, `HudPanelSdf.shader:681,710` |
| Backdrop **darkening**, backdrop **tint** | **NO — shared** | `_FrostDarken` / `_FrostTint` uniforms on the shared material, `HudSystem.cs:1439-1440,1454-1455`; applied `HudPanelSdf.shader:817` |
| Frost **downsample**, **re-blur every N frames** | **NO — shared** | sizes/throttles the ONE dual-Kawase pyramid, `HudBackdrop.cs:265-283`, `:396` |
| `_FrostStrength` / `_ChromaStrength` uniforms | shared, but **compat-only** on SDF | `HudPanelSdf.shader:166,168` "packed amount is authoritative"; still a real multiplier on the legacy mesh path, `HudGlass.shader:187` |
| Edge-light **strength** | **Yes** (`spec` on panels, `edgeLight` on lines) | `GlassEdgeFor` `HudElementView.cs:582-596` |
| Edge-light **angle / colour / opposing rim / falloff** | **NO — shared** | `PanelGraphic.LightX/LightY/LightTint` statics (`PanelGraphic.cs:1838-1848`) + `_EdgeLightDir/Color/Rim/Sharp` uniforms (`HudSystem.cs:1466-1472`) |
| Border fade, soft edge, glow out/in, halo radius/spread/extra-diffuse, haze, breath depth, uneven amount + organic scale, flowing aura, irregular energy, energy frequency, energy smoothness, flow speed | **Yes** | all per-element params today, `ApplyMeshFx` `HudElementView.cs:672-716` + `SetSdfStyle` `:834-839` |
| Halo **breath speed** (Hz) | **NO — shared clock** | `_UiaHaloBreathWave` computed once, `HudSystem.cs:1408-1411` |
| Shine **period**; the shine sweep position | **NO — shared clock** | `_ShinePos` computed once, `HudSystem.cs:1397-1400` |
| Shine / iridescence **strength** | **Yes** | packed lanes; `ShineAmountFor` / `IridAmountFor` `HudElementView.cs:939-959` |
| Pulse **speed / depth** | **NO — shared** | `HudConfig.FxPulseSpeed/Depth` read directly in `ApplyPulse`, `HudElementView.cs:1048-1049` |
| Ripple **desync** on/amount | **NO — shared rule** | a global toggle that derives a *stable per-element* jitter from the Id hash, `RippleFreqFor` / `RippleFlowSpeedFor` `:759-799` |
| Hairlines on / min width | **NO — shared** | `HudConfig.FxHairlinesOn/FxHairlineMin`, consumed inside `PanelGraphic` |
| Box end fade **amounts** (L/R, top/bottom) | **Yes** — and already un-gated | `edgeFadeX/Y`, `HudElementView.cs:618-647`, deliberately editable in both states (`:1589-1603`) |
| Box end fade **curve / border influence** | **NO — shared** | `_EdgeFadeCurve` / `_EdgeFadeBorder` uniforms, `HudSystem.cs:1476-1479` |
| **Bloom** (both bands, every knob) | **NO — shared** | a full-screen post pass, `HudBloomFx.cs` |
| **Alerts** (breath length, flash count, strength, both brightnesses, both hues) | **NO — shared** | `HudAlertPulse` reads globals; the element contributes only a stable `AlertSeed` |
| Tier masters `FxTierA/B/C`, `SdfPanels` | **NO — shared** | capability gates |
| Corner radii ×4, corner style, border width, feather, sheen, squircle, gaussian halo, trapezoid insets, font scale | **Yes** | first-class fields / params |
| Colours (fill, border, text, widget-specific refs) | **Yes, in BOTH states** | palette-name refs live-track the theme; see `SeedCustomStyleFromEffective`'s deliberate non-snapshot, `HudElementView.cs:1835-1837` |
| Item icon tint (hands / 1-6 / grid) | **Yes** — 3-state per element already | `iconTintMode` / `iconTintColor`, `HudElementView.cs:548-562` |
| The 7 power transitions | **Yes** — Inherit / On / Off + own strength | `HudTransitionFx.All` |
| Everything else on **View & Behavior** (curvature, visor distance, vignette, scanlines, vanilla-panel hiding, diegetic tiers, dropouts) | **NO — global by nature** | whole-HUD projection and integration |

**Two honesty caveats that must survive into the UI:**

- On the **legacy mesh** path (any panel not on `sdfglass`) there is exactly ONE per-element scalar
  — `uv0.x` — shared by shine / iridescence / frost / chroma, collapsed by
  `FxStrengthFor` + `LegacyMultiplier` (`HudElementView.cs:996-1018`). Different Custom strengths
  are an unavoidable approximation there. The popup already half-admits this
  ("legacy surface: independent strengths are approximate", `:1498-1500`); the registry must make
  it a per-row flag rather than one section header.
- **Cut corners drop off `sdfglass` entirely** on an ABI-2 bundle (`ApplyFx` `:823-826`, and
  to-do item 10), taking frost depth, chroma, edge flow, the gaussian halo family and SDF
  iridescence with them. That is per-element state silently disabling per-element knobs, and the
  registry's `SdfOnly` flag is where it should be surfaced.

### 1.7 The five-place duplication (the audit's finding, now with the seed bug attached)

The same ~30 steady-state effect keys are hand-listed in five places. This is the mechanism that
lets root causes A, C and D exist at all:

| # | Site | Lines | What it hand-lists |
|---|---|---|---|
| 1 | F9 global rows | `Windows/HudEditorWindow.cs:353-739` | every global `ConfigEntry` + label + range, per sub-tab |
| 2 | Per-element Custom mirror | `UI/Hud/HudElementView.cs:1360-1542` (+ the Polyline mirror `:1552-1587`) | the same labels/ranges against param keys |
| 3 | Seed on separation | `UI/Hud/HudElementView.cs:1822-1916` | ~40 `SetFFor`/`SetBFor` calls |
| 4 | Def-only snapshot | `UI/Hud/HudElementView.cs:1942-2089` | the same ~40 again, with a *third* set of default rules |
| 5 | Reset to globals | `Windows/HudEditorMode.cs:1259-1358` | the same list a fourth time; `StripGlass` (`:1365-1400`) is arguably a fifth |

`HudTransitionFx` already proves the cure for the motion family: **one table
(`UI/Hud/HudTransitionFx.cs:119-160`) drives the F9 rows (`HudEditorWindow.cs:653-684`), the
per-element rows (`HudElementView.cs:1618-1662`), the resolver, the Sanitize migration and the
reset loop.** Those two menus *cannot* drift. The steady-state families have no such table, and
the audit already recorded that the pre-registry reset button had silently rotted once
(`03-F9-Editor-and-Knob-Parity.md` finding 5 / recommendation 7).

**This plan absorbs that to-do item** (`Documentation/Release Prep Reports/to do/README.md` →
report 03 rec 7 / `00-Overview-and-Release-Plan.md` §170 item 1). It also absorbs to-do item 9
(shipped themes still authored at Schema 7 — they get re-exported at the new schema in Phase 5)
and interlocks with item 10 (SDF chamfer), which becomes a per-row capability flag rather than a
loose caveat.

---

## 2. Phase 0 — IMMEDIATE FIXES (ship before the rework)

Goal: **make the current system stop lying.** Small, surgical, individually revertible, no schema
change, no new concepts. One Changes Report, one play-test round. Everything here is compatible
with — and strictly reduces the risk of — the rework that follows.

### 0a. Separation must preserve the on-screen look exactly *(S)*

**Change:** delete the `customStyleReady` short-circuit. Global → Custom **always** re-seeds from
the currently effective values.

- `HudElementView.cs:1152-1153` — drop the `if (!d.GetBFor(slot, "customStyleReady", false))`
  guard; call `SeedCustomStyleFromEffective(d, slot)` unconditionally.
- `HudElementView.cs:1927-1929` — same in `SetUnifiedStyleSource`; the `forceCustomSnapshot`
  parameter becomes redundant for the Global→Custom direction (keep the signature for the
  Legacy path that `HudStyleMigration` depends on).
- `SeedCustomStyleFromEffective` is already **value-preserving by construction** — every write is
  a `…For()` resolver read taken while the OLD mode is still active (`:1822-1916`), so re-seeding
  an element that is already showing the global values simply writes those values down. It is
  idempotent in effect.

**What this costs:** a dormant Custom design is no longer preserved across a Global round-trip.
Today "uncheck → recheck → uncheck" restores your old Custom values; after this it restores what
is currently on screen. That trade is correct — the current behaviour is exactly the bug — but it
is a behaviour change worth one line in the Changes Report. Phase 3 restores an explicit,
discoverable version of "remember my custom design" if FlorpyDorp wants it (open question 5).

**Also add to the seed** (currently missing, all neutral-by-default so the change is safe):
`cornerStyle`, `edgeFadeX`, `edgeFadeY`, and the portrait's `ringGlow` / `ringGlowColor`.

### 0b. Stop the resolvers lying about missing keys *(S)*

**Change:** make `NewSdfFeatureOn` / `NewSdfOwnOrGlobal` (`HudElementView.cs:735-748`) fall back
to the **global**, exactly like `StyleFeatureOn` / `OwnOrGlobal`. One convention, everywhere:
*a per-element key that is absent means "the global", never "off".*

**The safety problem this creates, and the fix:** a Custom element that deliberately lacks a key
would gain the feature on upgrade. Two mitigations, both required:

1. 0a means every *new* separation writes a complete snapshot, so the exposure is bounded to
   already-stored Custom elements.
2. A one-time, idempotent `Sanitize` back-fill: for every element with `styleSource == 2` that
   lacks any of the six SDF keys, write the **current global** into it. Gated on a stored marker
   so it runs once (same discipline as `HudTransitionFx.MigrateElement`,
   `HudTransitionFx.cs:271-310`). Bump `HudDocument.CurrentSchema` 15 → 16 here rather than in
   Phase 3 if that lands first — one bump, not two.

Also fix `SetUnifiedStyleSourceWithoutView:2013-2018`, which writes a literal `false` for the
three SDF booleans when the source was already Custom.

### 0c. Popup defaults must equal renderer defaults *(S)*

**Change:** in `AddUnifiedEffectProps` (`HudElementView.cs:1382-1541`) replace every hard-coded
display default with the same global-backed default the resolver uses. Mechanically: the getters
become `() => OwnOrGlobalRaw(key)` rather than `gf(key, <literal>)`. Small, mechanical, and it is
the last change before the registry makes the whole block disappear.

### 0d. Shared-global honesty + a way to reach them *(S)*

No renderer change — the shared knobs already apply to Custom panels (§1.5). The fix is the UI
contract:

- Every "…is a shared global" line becomes an explicit, consistently worded row:
  **`shared global — edits affect EVERY panel  [Open F9 > Effects > Glass]`**.
- Add the ones that are currently missing entirely from the popup: bloom resolution, the whole
  alert family, and — for FlorpyDorp's exact complaint — **backdrop darkening, frost tint,
  downsample and re-blur rate rendered as visible read-only rows showing their current values**,
  not as a sentence (today: a plain header at `HudElementView.cs:1539-1540`).
- Add a note under the chroma row that the fringe is gated on frost (§1.2) — today it says only
  "(uses frosted backdrop)".

**The jump-to mechanism needs one small piece of new machinery.** `ActivateEditorSubTab`
(`HudEditorWindow.cs:144-149`) only *records* which sub-tab is active; it cannot open one. The
standard ImGui way is a one-frame `ImGuiTabItemFlags.SetSelected` on `BeginTabItem`. **This must be
verified against `RG.ImGui.dll` (namespace `ImGuiNET`) before we promise it** — nothing in the
codebase uses `ImGuiTabItemFlags` today. If the binding does not expose the 3-arg overload, the
fallback is a non-clickable path string (`F9 > Effects > Glass`), which still beats the current
prose.

### Phase 0 acceptance test

For each of the four installed profiles (Pure HUD, Stationeers Blue, Zirillian Red, shipped
Glassy 4.0), on each element in turn: screenshot → uncheck follow → screenshot. **The two frames
must be pixel-identical.** Today they are demonstrably not (§1.1). This is the single test that
proves Phase 0.

---

## 3. The target model — `HudStyleFx`

### 3.1 The one table

A new `UI/Hud/HudStyleFx.cs`, modelled directly on `HudTransitionFx` including its
hot-reload discipline (**lazy `Func<>` accessors, never a cached `ConfigEntry`** — HudConfig
rebinds on every F6, `HudTransitionFx.cs:24-28`).

```csharp
internal enum HudFxKind      { Bool, Float, Color, Combo }
internal enum HudFxCategory  { Surface, Glass, Edges, Glow, Bloom, Alerts, Transitions }

internal sealed class HudStyleFxDef
{
    public readonly string Key;            // canonical id, e.g. "glow"
    public readonly string ParamKey;       // per-element param ("glow"); null => shared-only
    public readonly string OnParamKey;     // companion bool ("customGlowOn"); null if none
    public readonly string Label;          // ONE string — both menus render it
    public readonly string Tip;
    public readonly HudFxCategory Category;// decides the sub-tab on BOTH sides
    public readonly HudFxKind Kind;
    public readonly float Min, Max;

    readonly Func<ConfigEntryBase> _global;   // the global value
    readonly Func<ConfigEntry<bool>> _master; // the tier/feature gate (FxTierA/B/C, FxGlowOn…)

    public readonly bool SharedOnly;       // physically global -> read-only pointer row
    public readonly bool SdfOnly;          // needs the analytic panel; inert on mesh / Cut corners
    public readonly bool LegacyApprox;     // mesh path collapses this into the single uv0.x scalar
    public readonly bool PerTierCapable;   // false for the transitions family (shared by tiers)
    public readonly Func<HudElementView,bool> Applies;  // capability predicate
}

internal static class HudStyleFx
{
    internal static readonly HudStyleFxDef[] All = { /* ~55 rows */ };
    internal static HudStyleFxDef Find(string key);
}
```

### 3.2 The one resolution rule

```
Resolve(view, def):
    if (!def.MasterOn)                       return def.Neutral;      // tier/feature master wins
    switch (SourceOf(view.Def, def.Category, view.StyleSlot)) {
        case Global: return def.GlobalValue;
        case Donor:  return Resolve(view.Donor, def);                 // depth-1, see §5
        case Own:    return view.Def.Get(def.ParamKey, def.GlobalValue);   // <-- default IS the global
    }
```

That last default is the whole point. It kills root cause C and root cause D permanently:
a missing per-element key can never mean "off", and a knob added tomorrow reaches every existing
element for free.

### 3.3 What the one table derives

| Consumer | Today | After |
|---|---|---|
| F9 global sub-tab rows | hand-written, `HudEditorWindow.cs:353-739` | `foreach (def in All.Where(Category == tab))` |
| Element popup sub-tab rows | hand-written mirror, `HudElementView.cs:1360-1587` | **the same loop**, same labels, same ranges, same order |
| Seed on unfollow | 40 hand-written writes, `:1822-1916` | `foreach (def in category) el.Set(def.ParamKey, def.GlobalValue)` |
| Def-only snapshot | 40 more, `:1942-2089` | same loop, no live view needed |
| Reset to globals | 40 more, `HudEditorMode.cs:1259-1358` | same loop |
| `StripGlass` / "Make flat" | 30 more, `HudEditorMode.cs:1365-1400` | `foreach (def in Glass|Edges|Glow) el.Set(def.ParamKey, def.Neutral)` |
| Per-tier fork seeding | already prop-list-driven (`SeedSlotFromBase`, `:1780-1816`) | **unchanged — free** |
| Theme travel | already reflection-driven (`HudTheme.cs:66-75`) | **unchanged — free**, provided no `ConfigEntry` is renamed |
| Diagnostics | none | a `hudfx <elementId>` console command dumping every row's source + resolved value (read-only, per `Core/FinderCommands.cs` precedent) |

**Parity becomes structural**, exactly as it already is for transitions.

### 3.4 The categories

Chosen to match the F9 sub-tabs one-for-one, because requirement 2 is literally "1:1 like the
global menu".

| Element category | Follow checkbox? | Maps to F9 | Contents |
|---|---|---|---|
| **Surface** | yes | Theme > Typography & boxes (+ Appearance) | border width, 4 corner radii, corner style, feather, glass sheen, squircle, gaussian halo, trapezoid insets, font scale |
| **Glass** | yes | Effects > Glass | shine on+strength, iridescence on+strength, chroma on+strength, frost on+strength, frost depth |
| **Edges** | yes | Effects > Edges | edge-light strength (`spec` / `edgeLight`), edge energy on, irregular energy, energy frequency, energy smoothness, flow speed, flowing aura on+strength, border fade on+amount, soft edge on+width |
| **Glow** | yes | Effects > Glow (+ the Advanced halo rows) | glow on, outward, inward, haze, halo radius, spread, extra diffuse, breathing on+depth, uneven on+amount+organic scale |
| **Transitions** | yes | Effects > Transitions | the 7 `HudTransitionFx` effects |
| **Bloom** | **no** — nothing to follow | Effects > Bloom | read-only shared rows + jump |
| **Alerts** | **no** — nothing to follow | Effects > Alerts | read-only shared rows + jump |

Deliberately **not** follow-gated (they stay always-editable, in both states):

- **Colours.** Refs resolve identically in both states and a palette-name ref live-tracks the
  theme; separating must never hex-freeze a ref the author wants tracking F9. This is already the
  documented rule (`HudElementView.cs:1835-1837`, `:1205-1206`) and it is right.
- **Box end fade amounts** (`edgeFadeX/Y`). Element *geometry*, not theme — the code already says
  so (`:1589-1592`). The fade *shape* stays a shared read-only pointer.
- **Layout / tiers / Z / content.** Their own tabs, untouched.
- **Item icon tint.** Already a per-element Inherit/Custom/Off enum that works
  (`:548-562`); folding it into a category would regress a working control.

**View & Behavior gets NO per-element category.** Recommendation, with the reason: every knob on
that tab is whole-HUD projection or vanilla-panel integration (curvature mode, curve strength,
visor distance, vignette, scanlines, hide-vanilla-*), and there is no coherent meaning to "this
one element is on a different curvature". The one genuine per-element case that tab *points at* —
item icon tint — already has its per-element control. Marked as **open question 2** in case
FlorpyDorp has a case in mind I have not found.

### 3.5 Storage

One packed int per element **per slot**, so a Bare fork can follow different categories than
Suited (Wave C's per-slot contract is preserved):

```
"styleSrc"  : 2 bits per category  (0 = Global, 1 = Donor, 2 = Own)
              Surface | Glass | Edges | Glow | Transitions   -> 10 bits
              default 0 = follow everything = today's Global element
"styleDonor": string, the donor element's Id (only meaningful for categories in state Donor)
```

Written with `SetIFor(slot, "styleSrc", …)` so it goes through `HudElementDef`'s existing
copy-on-write fork protection (`HudDocument.cs:951-958`). One param instead of five booleans keeps
the XML small and keeps the fork bookkeeping single-valued.

Rationale for a packed tri-state over three separate flag sets: per category the answer is
**exactly one of** Global / Donor / Own. Modelling it as a radio makes the illegal states
unrepresentable, which is the same reasoning that produced `HudFxMode` for transitions
(`HudTransitionFx.cs:14-21`).

### 3.6 What the popup looks like

The element `Effects` tab becomes a **nested tab bar** using the machinery that already exists —
`HudPropKind.TabGroup` / `TabPage` (`UI/Hud/HudProp.cs:184-195`, drawn by
`Windows/HudPropDrawer.cs:113+`, already in production for the Grid style pages and BareSenses).
No new drawer code.

Per page, exactly two states:

**Following**
```
  [x] Follow global               (uncheck to edit)
      This element's Glow follows F9 > Effects > Glow.   [Open]
```
Nothing else. Requirement 3: *"Following a category ⇒ its controls DON'T show."*

**Not following**
```
  [ ] Follow global      ( ) Global   ( ) Inherit from element: [g2-topbar] [Pick…]   (•) Own
  ── identical rows to F9 > Effects > Glow, seeded from the current globals ──
  Glow halo                                   [x]
    outward strength                          [====|----] 0.46
    inward strength                           [==|------] 0.20
    extended atmospheric haze (SDF)           [=|-------] 0.03
  SHARED HALO / FLOWING-AURA ENVELOPE
    Halo / aura radius (px)                   [===|-----] 31.5
    spread (tight rim -> diffuse)             [======|--] 0.84
    ...
  breath speed (Hz)     0.25   shared global — affects EVERY panel   [Open F9]
```

Rules that fall out of the table and must never be broken:

- **Shared-only rows are always rendered**, greyed, showing the live global value, with the
  "affects EVERY panel" label and a jump. Never silently absent. (Requirement 1's second half.)
- **Capability-inert rows** (`SdfOnly` on a mesh panel, or on a Cut-corner panel under an ABI-2
  bundle) render with the existing "inactive because…" note rather than disappearing — the current
  behaviour at `:1352-1357` generalised to a per-row flag.
- **`LegacyApprox` rows** on a non-SDF surface carry the approximation warning per row instead of
  one section header.

### 3.7 Transitions: retiring the tri-state dropdowns

Requirement 3 says the Inherit/On/Off dropdowns go away. Under the new model:

- **Transitions following** ⇒ nothing shows but the follow line. (Today those rows are
  unconditional — deliberately so, because of the 2026-07-19 fix
  `HudElementView.cs:1605-1611`. The follow checkbox *replaces* that unconditionality: an element
  that wants to sit a transition out simply unfollows the Transitions category.)
- **Transitions not following** ⇒ 7 rows that look exactly like F9's:
  a checkbox + a strength slider each, seeded from the current global master + strength.

**Migration of existing tri-state data** (idempotent, in `Sanitize`, per slot):

| Stored state | Becomes |
|---|---|
| all 7 = Inherit (the overwhelming majority; nothing stored) | Transitions category → **Global**. Nothing written. |
| any effect On or Off | Transitions → **Own**. Then per effect: `On` → checkbox true + its stored `<key>Amt`; `Off` → checkbox false; `Inherit` → checkbox = current global master, slider = current global strength. |

**This loses per-effect granularity for mixed elements**, and I will not pretend otherwise: an
element that was "Inherit on 6, Off on 1" ends up with 6 effects frozen at today's global values
instead of tracking future global edits. It is visually identical at the moment of migration and
diverges only if the globals are later changed. See **open question 3** — my recommendation is to
keep the tri-state alive as an opt-in "Advanced" expander *inside* an unfollowed Transitions page,
because motion genuinely has the "keep the shared look, sit this one transition out" case that the
tri-state was built for, and it costs one collapsible section.

The legacy-bool mirror written by `HudTransitionFx.SetMode` (`HudTransitionFx.cs:196-215`) stays
untouched during migration — "hide, never destroy".

---

## 4. Migration

Following `Documentation/Config-and-Theme-Migration.md` §2 and §3 verbatim.

### 4.1 Schema

`HudDocument.CurrentSchema` 15 → **16** (or → 16 in Phase 0b if that ships first; one bump total).
Note the existing convention: `Sanitize` deliberately does **not** stamp `CurrentSchema` onto
loaded profiles (`HudDocument.cs:88-94`), because the shipped-default replacement gates read the
stored value. Every step below is therefore **self-gating on the absence of `styleSrc`**, exactly
like the Wave C fork adoption (`HudDocument.cs:207-220`).

### 4.2 The mapping (idempotent, fail-soft per element, per slot)

Runs **after** `HudStyleMigration.Migrate` and `HudTransitionFx.MigrateElement`, which are
unchanged.

| Stored `styleSource` (per slot) | New `styleSrc` |
|---|---|
| `1` (Global) | every category → **Global**. Dormant custom keys are **left in place**, unread. |
| `2` (Custom) | every category → **Own**. Existing values kept verbatim. **Absent keys now resolve to the global instead of to a neutral (§3.2), so incomplete old snapshots silently repair themselves.** |
| `0` (Legacy) | `HudStyleMigration` already regresses these to 1 or 2 first; then as above. |

`styleSource` is **kept and still written** for one release, so a downgrade to 0.9.2.x still
renders correctly. The write is dropped in the release after. This is the "hide, never destroy"
rule applied to a schema field.

Additional `Sanitize` sweeps introduced with this change:

- **Dangling donor**: any `styleDonor` that does not resolve to a live element in the same
  document → clear the key, drop those categories to Global, count in the repair log.
- **Self-donor / depth violation**: rejected the same way (see §5.4).
- **SDF back-fill** (from Phase 0b) if not already done.

### 4.3 Config migration

If — and only if — the registry work renames or repurposes a `HudConfig` field, add a
`ConfigMigration` step: bump `CurrentVersion` 4 → 5 and add the `case 4:` remap
(`Core/ConfigMigration.cs:28`, recipe in the migration doc §2). **The intent is to rename nothing**,
in which case no config migration is needed at all and `HudTheme` snapshot compatibility is
automatic (it reflects `HudConfig`'s public statics, `HudTheme.cs:66-75`, so an *added* entry is
captured for free and a *missing* key in an old theme is ignored, `HudTheme.cs:38-40`).

One thing to watch: `HudTheme.Exclude` (`HudTheme.cs:58-64`) deliberately keeps
`FrostDownsample`, `FrostUpdateEveryN`, `FxBloomRes`, `FxBloomBlurSteps`, `FxBloomFineDetail` out
of themes because they trade frame time on the player's own machine. The registry must carry a
`DoesNotTravel` note on those rows so the element popup's shared-row label can say
*"shared global — machine-local, does not travel with the theme"*, which is currently invisible
to authors.

### 4.4 Shipped themes

Re-export Glassy 4.0 / Pure HUD / Stationeers Blue / Zirillian Red at Schema 16 in Phase 5 — this
is exactly **to-do item 9**, which this plan absorbs. `HudProfileStore.SyncShipped` hashes the
canonical post-`Sanitize` form on both sides, so the re-export causes no spurious refresh for
players who have not edited them, and no loss for players who have (migration doc §3).

### 4.5 Round-trip safety

An old profile must load, render identically, save, and reload identically. The regression harness
for this is the Phase 0 acceptance test extended: for each shipped and each user profile, capture
the post-`Sanitize` XML before and after the change and diff it; the only permitted additions are
`styleSrc` and the SDF back-fill.

---

## 5. Inherit from another element

### 5.1 Model

A follower stores a **donor reference**, not a copy: `styleDonor` = the donor's `Id` (a GUID —
`HudDocument.Sanitize` guarantees non-empty and unique, `:189-194`, and Ids are never renamed).
Per **category**, the source is Global **XOR** Donor **XOR** Own — one radio, three states
(§3.5). A category in state Donor resolves through the donor's *own* resolution for that category.

This is deliberately **live**: the donor's values are read every frame through the same
`ApplyGlass` / `ApplyFx` path, so editing the donor ripples to every follower immediately. That is
what makes it different from a one-shot copy — and it is why we should *also* ship a plain
**"Copy <category> from…"** button, which has no lifetime semantics at all and is the right tool
for "make this one look like that one, then diverge".

### 5.2 Precedence, honestly stated

```
per category:  Own  >  Donor  >  Global
```
- Own wins because it is the element's explicit local decision.
- Donor is consulted only for categories explicitly set to Donor.
- Global is the fallback and the fail-soft landing zone: a missing / deleted / illegal donor
  degrades that category to Global. **Never to neutral, never to a blank element.**
- Shared-only knobs are unaffected — there is nothing to inherit; they stay global by physics.

UI states, so nothing is ambiguous:

| Category state | Rows shown | Header line |
|---|---|---|
| Global | none | `Following global — F9 > Effects > Glow  [Open]` |
| Donor | none (read-only preview of the resolved values, greyed) | `Inheriting Glow from "g2-topbar"  [Change] [Detach to own]` |
| Own | full 1:1 controls | `Own values (seeded from global / from g2-topbar)` |

"Detach to own" seeds from whatever the category currently resolves to — the same
seed-on-separation contract as requirement 4, applied to the donor case.

### 5.3 Pick UX

Reuse the F9 selection machinery rather than inventing a second hit-test:
`HudEditorMode.HoverElement` / `SelectedElement` / `_pendingSelectId`
(`Windows/HudEditorMode.cs:145-147`, `:199-206`, `:371-400`).

1. Set the category radio to "Inherit from element" (or press `[Pick…]`).
2. `HudEditorMode` enters a **pick mode**: the next left-click over an element **consumes the
   click** (does not change selection), writes `styleDonor`, and exits pick mode. Hovering shows
   the normal hover outline plus a "pick as donor" cue; illegal targets (self, or a donor that
   would violate the depth rule) are drawn as rejected and cannot be clicked.
3. `Esc` or right-click cancels, leaving the category at its previous state.
4. Keyboard/accessibility fallback: a dropdown listing `Type — Id` for every element in the
   document, so the feature is usable without the pick gesture. This also matters for elements
   that are currently hidden by tier.

Undo: the whole gesture is one `CommitDocumentMutation` step, bracketed like every other element
edit (`HudEditorWindow.cs:1751-1771`).

### 5.4 Cycle prevention — recommendation: **depth 1**

**Rule:** an element may inherit from a donor that is itself in state **Global or Own** for that
category. A donor that is itself in state **Donor** for that category is not a legal donor; if it
becomes one later (the donor is re-pointed after the fact), the follower's category degrades to
Global and logs one line.

Why depth 1 rather than chains-with-a-cycle-check:

1. **Cost.** Resolution runs inside the per-frame style push (`ApplyGlass` / `ApplyMeshFx` /
   `ApplyFx` are called every frame per element, `HudSystem`'s content loop). Depth 1 is a single
   dictionary lookup. A chain needs a visited-set allocation or a depth counter threaded through
   ~20 resolver call sites — allocation on a per-frame path is exactly what
   `RippleFreqFor`'s "no string alloc" comments exist to avoid (`HudElementView.cs:759-771`).
2. **No cycle detection needed at all.** "Is my donor itself a donor-follower?" is a one-hop test.
   There is no graph to walk, so there is no graph to break.
3. **It matches the described mental model.** FlorpyDorp asked for "click which element to inherit
   from" — a one-hop "make this look like that". Chains are a different, more abstract feature
   (style classes) that nobody asked for.
4. **Failure is legible.** "You cannot inherit from an element that is itself inheriting" is a
   sentence a user understands. "Cycle detected in style graph" is not.

The rejected alternative — arbitrary chains with a visited-set and a depth cap of 4 — is recorded
here so the decision is not re-litigated silently. If FlorpyDorp wants chains (**open question 1**),
the change is contained: one memoised per-frame resolve cache keyed on (element, category),
invalidated on any document mutation.

### 5.5 Lifecycle

| Event | Behaviour |
|---|---|
| Donor **deleted** | `Sanitize` sweep clears the dangling `styleDonor`; every Donor category on every follower drops to Global; one aggregated warning line. Fail-soft, never an NRE. |
| Donor **duplicated** (`Ctrl+D`, `HudEditorMode.cs:1212-1223`) | The clone gets a fresh GUID and keeps pointing at the same donor — correct, and free. |
| Follower duplicated | Same: the clone inherits from the same donor. |
| Donor **renamed** | Cannot happen — Ids are GUIDs, never user-visible names. |
| Profile **imported / theme applied** | Donor refs are internal to the document, so they travel intact. A donor whose element was pruned is caught by the `Sanitize` sweep. |
| F6 **hot-reload** | `HudStyleFx` holds no scene state and no cached `ConfigEntry` (§3.1), so it goes with the assembly. Any pick-mode state must reset in `HudEditorMode.Shutdown` (`:60-65`) — mandatory per CLAUDE.md. |

### 5.6 Per-tier interplay

A follower resolving slot `S` reads the donor's `donor.ResolveSlot(S)` — i.e. the donor's Bare
fork if it has one, else the donor's base (`HudDocument.cs:790-791`). No extra storage, and it is
the least-surprising rule: "bare follows bare". The follower's own per-slot `styleSrc` means a
bare fork can inherit from a donor while the suited base does not.

---

## 6. Phases

Each phase is independently shippable and play-testable, with its own Changes Report
(`Changes Reports/YYYY-MM-DD - <name>.md`) per CLAUDE.md. Phases 3 and 4 get an adversarial review
against the decompile and the profile-compat surfaces before merge.

| # | Phase | Effort | Ships | Review |
|---|---|---|---|---|
| **0** | Immediate fixes 0a–0d (§2) | **S** | separation preserves the look; one missing-key convention; popup defaults = renderer defaults; shared knobs labelled + reachable | normal |
| **1** | `HudStyleFx.cs` table + F9 global tabs rendered from it. **No behaviour change.** Adds the `hudfx <id>` diagnostic dump. | **M** | proves the table is complete before anything depends on it | normal |
| **2** | Element popup rendered from the table, as a `TabGroup` with Glass/Edges/Glow/Bloom/Alerts/Transitions/Advanced pages. Still the single two-state `styleSource`. Deletes the hand-written mirror (`HudElementView.cs:1360-1587`, ~230 lines). | **M** | requirement 2 (1:1 parity) | normal |
| **3** | Per-category follow: `styleSrc`, Schema 16, the `Sanitize` mapping, per-category seeding, the follow radio, the transitions migration. Deletes `SetUnifiedStyleSourceWithoutView`'s hand-list and `ResetAllElementEffects`'s hand-list in favour of registry loops. | **L** | requirements 3 + 4 | **adversarial** |
| **4** | Inherit-from-element: donor storage, pick mode, depth-1 rule, `Sanitize` sweep, "Copy from…" one-shot. | **M–L** | requirement 5 | **adversarial** |
| **5** | Cleanup: `StripGlass` on the registry; re-export the four shipped themes at Schema 16 (**absorbs to-do item 9**); drop the `styleSource` write. | **S** | tidy | normal |

Phases 0 and 1 are safe to run concurrently with other work. Phases 2–5 are strictly ordered.

### Risks

| Risk | Assessment | Mitigation |
|---|---|---|
| **Per-frame popup cost.** `_propScratch` is rebuilt **every frame** the popup is open (`HudEditorWindow.cs:1775-1777`), allocating one `HudProp` + closures per row. A registry-driven build is comparable in count but adds a table walk and per-row predicates. | Medium. Today's popup already builds ~80–150 props/frame with no complaint, and the `TabGroup` only *draws* one page — but it still *builds* all of them. | Build only the active page's rows (the drawer already knows the selected page); if that is awkward, cache the built list keyed on `(ElementStamp, activeGroup, styleSrc, tierSlot)` and rebuild on change. Measure with `Profiling.ProfilicusUniversalis` before optimising. |
| **XML size.** Unfollowing a category materialises its whole key set. | Low, and net *smaller* than today: today's separation writes ~40 keys unconditionally; per-category writes only the categories you actually unfollow. `styleSrc` is one int. | — |
| **Five-places deletion risk.** Each deleted hand-list is a chance to drop a knob silently — the exact failure the pre-registry reset button already suffered once. | High if done carelessly. | Phase 1 ships the table *before* any deletion, and the `hudfx` diagnostic dumps every row's source + resolved value. Every deletion is paired with a before/after dump diff per shipped profile, not just a screenshot. |
| **SDF ABI interplay.** Cut corners fall off `sdfglass` (to-do item 10), silently disabling the `SdfOnly` rows. | Medium — FlorpyDorp is authoring themes (Zirillian Red) that use frost/chroma/flow on panels he may want cut. | The registry's `SdfOnly` flag drives an explicit per-row "inactive: this panel's corners are Cut on an ABI-2 bundle" note. Item 10's shader fix removes the caveat entirely when the bundle is next rebuilt. |
| **Behaviour change in 0a** (dormant Custom designs no longer survive a Global round-trip). | Low but user-visible. | Called out in the Changes Report; open question 5 offers an explicit "remember my custom design" if wanted. |
| **`ImGuiTabItemFlags` availability** for the jump-to-tab buttons. | Unknown — nothing in the codebase uses it. | Verify against `RG.ImGui.dll` (namespace `ImGuiNET`) in Phase 0; fall back to a text path if absent. |
| **Legacy mesh approximation** becomes more visible once per-element strengths are easier to set. | Inherent, not new. | Per-row `LegacyApprox` warning (§3.6). |

### Test matrix

Every cell is: screenshot before → act → screenshot after → compare.

- **Profiles:** shipped Glassy 4.0, Pure HUD, Stationeers Blue, Zirillian Red, plus one profile
  hand-rolled at the *old* schema to prove migration.
- **Actions:** follow → unfollow each category in turn → refollow; unfollow all; donor-inherit one
  category; delete the donor; duplicate the follower; "Reset active per-element effects";
  "Snapshot ALL as Custom"; "ALL follow globals"; "Flatten ALL boxes".
- **Tiers:** Bare, Suited, and a Bare-forked element (`Separate BARE style` on) in both.
- **Renderers:** SDF panels on/off; `SdfCutAvailable` true/false (Cut-corner panel); TierA/B/C each
  on and off; curvature Flat / VertexWarp / CurvedWorldCanvas (mode C exercises
  `HudFxMaterials.HandleWorldSwap`).
- **Lifecycle:** F6 hot-reload twice in a row (no stale canvases, no stranded pick mode, no
  borrowed vanilla object left detached); profile switch; theme import; save → reload → diff XML.
- **The gating test, repeated at every phase:** *unchecking a follow box must not change a single
  pixel.*

---

## 7. Open questions for FlorpyDorp

Only where taste decides. Everything else is settled by the findings above.

1. **Donor chains.** I recommend **depth 1** (§5.4): you can inherit from an element, but not from
   an element that is itself inheriting. Cheap, no cycle detection, legible failure. Do you want
   real chains (A→B→C) instead? It is doable with a per-frame memo cache but adds a graph to
   maintain and invalidate.

2. **Does View & Behavior earn a per-element case?** I found none — every knob there is whole-HUD
   projection or vanilla-panel integration, and the one per-element thing that tab points at (item
   icon tint) already has its own control. If you have a case in mind (per-element scanlines? a
   per-element curvature exemption beyond the existing `fxWarp` transition?), say so now — it
   changes the category set.

3. **Keep the per-transition tri-state as an "Advanced" escape hatch?** Requirement 3 retires the
   Inherit/On/Off dropdowns. But migrating a "mostly Inherit, one Off" element into the new model
   freezes the other six at today's globals (§3.7). My recommendation: keep the tri-state as a
   collapsible "Advanced" block *inside* an unfollowed Transitions page — one extra section, and
   the "keep the look, sit this one out" case (the 2026-07-19 fix) keeps working exactly. Your call.

4. **Category granularity.** I propose five follow-able categories (Surface / Glass / Edges / Glow
   / Transitions) matching the F9 sub-tabs. Do you want the **halo envelope** (radius / spread /
   diffuse — currently shared between the glow halo and the flowing aura, which live on *different*
   F9 sub-tabs) split into its own follow-able group? It is the one place where F9's own grouping
   and the physics disagree.

5. **Should re-following DELETE the custom values, or keep them dormant?** Today they are kept, and
   that dormancy is what made root cause A possible. Options: (a) keep dormant but **always
   re-seed** on unfollow (my recommendation — smallest change, no data loss, bug fixed);
   (b) delete on re-follow (smallest files, this class of bug becomes structurally impossible, but
   "uncheck / recheck by accident" loses work); (c) keep dormant *and* offer an explicit
   "restore my previous custom values" button when you unfollow. I lean (a), with (c) as a later
   nicety if you miss it.

---

## 8. Appendix — the file map for implementers

| File | Role in this plan |
|---|---|
| `UI/Hud/HudStyleFx.cs` | **NEW** — the table (§3.1) |
| `UI/Hud/HudTransitionFx.cs` | the proven pattern being generalised; its rows fold into `HudStyleFx` as `Category = Transitions` |
| `UI/Hud/HudElementView.cs` | `:719-748` resolver conventions (0b); `:1139-1155` follow checkbox (0a, Ph3); `:1337-1622` the popup mirror (0c, Ph2 deletion); `:1822-1916` seed (0a, Ph3); `:1942-2089` def-only snapshot (Ph3 deletion); `:961-981` frost/chroma gate |
| `UI/Hud/HudDocument.cs` | `:94` schema; `:174-300` `Sanitize` (Ph3 mapping, donor sweep); `:731-970` the slot/param model `styleSrc` rides |
| `UI/Hud/HudStyleMigration.cs` | unchanged; runs *before* the new mapping |
| `UI/Hud/HudTheme.cs` | `:58-64` the machine-local exclude list → the `DoesNotTravel` note |
| `UI/Hud/HudBackdrop.cs`, `UI/Hud/HudSystem.cs:1386-1490` | the shared capture + shared uniforms — the physics behind §1.6 |
| `Dev/UiaEffectsBundle/Assets/Shaders/HudPanelSdf.shader`, `HudGlass.shader` | what is packed per-element vs uniform; ABI constraints |
| `Windows/HudEditorWindow.cs` | `:326-346` Effects sub-tab bar (Ph1); `:144-176` sub-tab activation (0d jump-to); `:1775-1793` element popup + per-frame rebuild (Ph2, risk) |
| `Windows/HudEditorMode.cs` | `:1228-1253` bulk follow; `:1259-1358` reset (Ph3 deletion); `:1365-1400` `StripGlass` (Ph5); `:145-206`, `:371-400` selection machinery (Ph4 pick mode); `:60-65` shutdown (Ph4 hot-reload) |
| `UI/Hud/HudProp.cs` `:184-195`, `Windows/HudPropDrawer.cs:113+` | `TabGroup`/`TabPage` — the sub-tab mechanism, already in production |
| `Core/ConfigMigration.cs` | `:28` `CurrentVersion` — only if a `HudConfig` key is renamed |
| `UI/Hud/ShippedProfiles.cs` | Ph5 re-export at Schema 16 (to-do item 9) |
