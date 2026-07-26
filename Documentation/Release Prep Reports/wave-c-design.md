# Wave C — Design: universal per-tier style, theme folds, palette re-point

**Status:** design only (C0). No code changed by this document.
**Target:** 0.9.2.5 Experimental, Wave C.
**Audience:** the three implementation agents (C1/C2/C3) and the Integrate agent. Read this
end-to-end before touching a file; the partition in §7 is binding.

All line numbers were read on 2026-07-25 from `main` with the Wave A/B changes landed. They
drift — re-grep the quoted symbol, never trust the number alone.

---

## 1. What exists today (verified map)

### 1.1 The document model

`Assets/Scripts/StationeersUIMod/UI/Hud/HudDocument.cs`

* `HudTierMask { None, Bare=1, Suited=2, Robot=4, All=7 }` (:25) — **visibility only**.
* `HudElementDef` (:288) is flat: `Anchor/X/Y/W/H/WPct/HPct/Z/Tiers`, the style fields
  `Fill/Border/TextColor/BorderWidth/RTL/RTR/RBR/RBL/FontScale/Text/Align/Icon`, plus the
  `List<HudParam> Params` K/V bag (:334).
* **Per-tier LAYOUT fork** (:482-534): `bLayout` + `bX/bY/bW/bH/bWPct/bHPct/bAnchor`.
  `SetBareLayout(true)` seeds from the base geometry, `false` deletes the keys.
* **Per-curvature-mode LAYOUT fork** (:536-671): `mA*`/`mB*`/`mC*`/`mD*`, live tier only.
* **Per-tier VISUAL fork** (:673-807) — the thing Wave C generalises:
  * prefix `b_` (`BarePrefix`, :685); zero-alloc lookup `FindBareOverride` (:689) matches
    `k.Length == key.Length + 2 && k[0]=='b' && k[1]=='_'`;
  * `HasBareOverride` (:707), `HasAnyVisualBareOverride` (:711), `ClearVisualBareOverrides` (:725);
  * tier-aware bag accessors `GetFFor/GetBFor/GetIFor/GetSFor(bool bare, key, def)` (:739-777)
    and writers `SetFFor/SetBFor/SetIFor/SetSFor(bool bare, …)` (:782-785);
  * first-class field forks `FillFor/BorderFor/TextColorFor/BorderWidthFor/RTL..RBLFor/
    FontScaleFor(bool bare)` (:789-797) over the literal keys `b_fill`, `b_border`, `b_text`,
    `b_bw`, `b_rtl`, `b_rtr`, `b_rbr`, `b_rbl`, `b_fs`, with matching setters (:799-807).
* `Sanitize()` (:157) repairs and hosts idempotent legacy fixes; at :194-201 it strips
  orphaned bare overrides from single-mode elements (`!both`). Schema attribute at :81;
  the shipped Glassy 4.0 stamps `Schema='14'` (`Glassy40Default.cs:14`).

### 1.2 The view/render side

`UI/Hud/HudElementView.cs`

* Statics fed by `HudSystem` each frame:
  * `LayoutBare` (:24) — render tier is bare;
  * `LayoutTier` (:26) — visibility tier;
  * `EditBareTier` (:35) — editor is previewing bare **explicitly**;
  * `LayoutMode` (:41) — curvature mode.
* `EditBare(HudElementDef d) => EditBareTier && d != null && IsBoth(d.Tiers)` (:906).
  `IsBoth` (:898) = shown in Bare **and** in a live tier.
* Two-state style contract (:141-175): `styleSource` param, `StyleGlobal=1` / `StyleCustom=2`
  (`StyleLegacy=0` is regressed at load by `HudStyleMigration`). `UsesGlobalStyle`/
  `UsesCustomStyle`/`FollowGlobal`.
* **Every** effect/glass/sizing resolver short-circuits on `UsesGlobalStyle` *before* it
  consults the tier-aware getter: `Radius` (:179), `BorderWidthFor` (:187), `GlassSheenFor`
  (:391), `GlassEdgeFor` (:408), `FeatherFor` (:477), `OwnOrGlobal` (:544), `StyleFeatureOn`
  (:551), `NewSdfFeatureOn` (:560), `NewSdfOwnOrGlobal` (:566), `SdfSquircleFor` (:687),
  `SdfGaussianFor` (:695), `ShineAmountFor` (:755), `IridAmountFor` (:766), `FrostAmountFor`
  (:777), `ChromaAmountFor` (:787).
* Colours are per-element in **both** style states: `FillColor()` (:338), `BorderColor()`
  (:361), `TextColor()` (:366) all read `Def.…For(LayoutBare)`.
* `DescribeProps` (:932) → `AddUnifiedLayoutProps` (:960) / `AddUnifiedAppearanceProps` (:985) /
  `AddUnifiedEffectProps` (:1068). Appearance/Effects build the shorthands
  `gf/sf/gb/sb = d.Get*For(EditBare(d), …)` (:992-995, :1076-1079), so **every** row in those
  two blocks is already tier-aware.
* `SeedCustomStyleFromEffective(d)` (~:1450-1516) and `SetUnifiedStyleSourceWithoutView`
  (~:1540-1690) write **base** keys only (`d.SetB/SetF/SetI`, no slot).

### 1.3 The F9 editor

`Windows/HudEditorWindow.cs`

* `PreviewTierCombo` (:1442) writes `HudSystem.ForceTier` (Live / BARE / SUITED / ROBOT).
* `DrawEditTargetLine` (:1495) — the Wave A coloured edit-target line, driven by `ForceTier`.
* The element popup (:1150-1287). At :1182-1196, **only when `IsBoth(el.Def.Tiers)`**, it draws
  `Editing mode: [SUITED][BARE]` (`ModeTabButton`, :1292) which sets `HudSystem.ForceTier`.
* `DrawMenuThemePopup` (:1030) and `DrawGridStylePopup` (:1058) call
  `HudPropDrawer.DrawAll(list, null, null, null, null)` — **no `onChanged`, so no
  `MarkThemeChanged`**.
* `HudPropDrawer.DrawColorRef` (:306) writes through `p.Set` (palette NAME or `#RRGGBBAA`), so
  the colour picker itself is tier-agnostic and correct.

`UI/Hud/HudSystem.cs`

```csharp
:1011  bool explicitBare = ForceTier.HasValue && ForceTier.Value == HudTier.Bare;
:1012  HudElementView.LayoutTier   = tier;
:1013  HudElementView.EditBareTier = editorActive && explicitBare;
:1014  HudElementView.LayoutBare   = editorActive ? explicitBare : (tier == HudTier.Bare);
```
`LayoutHash` folds `LayoutBare` at :1451 (`+ (HudElementView.LayoutBare ? 4099f : 0f)`), so a
bare↔suit flip re-runs `Layout(scale)` on every panel and dirties the meshes.
`editorActive` is `Windows.HudEditorMode.Active` (`StationeersUIMod.cs:350`).

### 1.4 The 0.9.1.0 "per-mode bare-suit visual overrides"

There is **no separate BareSenses mechanism**. What 0.9.1.0 shipped *is* the `b_` family in
§1.1 plus the `EditBare(d)` write-target in the popup. `BareSensesWidget.cs:626`
(`bool bare = EditBare(d);`) is just a local alias. So Wave C **generalises this one pattern**;
it does not need to unify two competing designs.

### 1.5 Hand boxes / equipment column (the reported widgets)

`Widgets/HandBoxesWidget.cs` — render reads `Def.GetFFor(LayoutBare, …)`, `FillColor()`,
`BorderColor()`, and per-element colour refs `activeBorderColor` / `titleInactiveColor` /
`stateEmptyColor` via `Def.GetSFor(LayoutBare, …)` (:126-128). `DescribeProps` (:220) writes
through `EditBare(d)`. Icon tint at :181 is the **global** `HudConfig.TintIcon(Color.white)`.

`Widgets/EquipmentColumnWidget.cs` — same shape (`numColor`/`labelColor`/`labelEmptyColor`
at :148-150, props at :306-317). Icon tint at :203 is global. `horizontal` (:74, :142, :333),
`first`/`count` (:360-361) are **not** tier-aware.

### 1.6 Themes today

`UI/Hud/HudTheme.cs` — `Snapshot()` (:52) captures
`cfg:<HudConfig public static ConfigEntry field>` (reflection, minus `Exclude` :31-36),
`pal:<HudPalette.Name>` (22 entries), `rad:<RadialPalette.Name>` (24 entries).
`Apply()` (:73) restores; missing keys are left untouched, unknown keys ignored.
`Features/HudProfileStore.cs` — `SetActive` (:372) calls `HudTheme.Apply` (:383);
`MarkThemeChanged` (:407) arms a theme restamp; `Tick` (:451) / `FlushNow` (:468) write
`Active.Theme = HudTheme.Snapshot()`.
Verified in the player's live profiles: `Pure HUD.xml` / `Stationeers Blue.xml` each carry
**196** theme entries (150 `cfg:` + 22 `pal:` + 24 `rad:`).

**Not travelling today:** `UIAConfig` "8. Radial Visuals" (32 knobs) and "10. Hint Bar"
(15 knobs); `UI/Grid/GridTheme.cs` (31 `ConfigEntry` fields, section `13. The Grid`);
`UI/Menu/Kit/UiaMenuTheme.cs` (`Follow` + 19 colour overrides, section `12. Control Center`).

---

## 2. Root cause of "the colours don't switch back"

**Verdict: the fork is real and works, but it is a SPARSE overlay on a SINGLE SHARED BASE, and
"SUITED" is that shared base rather than a tier slot of its own.**

The load-bearing lines:

* `HudElementDef.FillFor(bool bare)` — `Assets/Scripts/StationeersUIMod/UI/Hud/HudDocument.cs:789`

  ```csharp
  public string FillFor(bool bare) { if (bare) { var p = Find("b_fill"); if (p != null && p.V != null) return p.V; } return Fill; }
  ```

  An **absent** `b_<key>` silently resolves to the base. There is no third state, so the
  document cannot distinguish "bare deliberately equals suited" from "bare has never been
  forked". Same shape for every bag key: `GetSFor` / `GetFFor` / `GetBFor` / `GetIFor`,
  `HudDocument.cs:739-777`.

* `Windows/HudEditorWindow.cs:1188`

  ```csharp
  if (ModeTabButton("SUITED", !editingBare)) HudSystem.ForceTier = HudTier.Suited;
  ```

  The SUITED tab writes **the base**, i.e. the value BARE inherits. So the sequence FlorpyDorp
  performed — pick BARE, set two colours, go back to SUITED, set the rest — writes the "rest"
  into the shared base and BARE picks them up. From his own on-disk profile,
  `<game>/BepInEx/config/StationeersUIMod/HudProfiles/Pure HUD.xml:442-513` (`Id="g2-hands"`,
  `Tiers="All"`, `styleSource=2`):

  ```
  Fill="#33CE0005"  Border="#33CE00E3"  TextColor="#23FF00F5"     <- base (both tiers)
  <P K="b_fill"   V="#0000004A" />                                <- forked, WORKS
  <P K="b_border" V="#54D5FE00" />                                <- forked, WORKS
  <P K="activeBorderColor" V="#52FF29E6" />                       <- base only, LEAKS to bare
  <P K="stateEmptyColor"   V="HudTextLabel" />                    <- base only, LEAKS to bare
  (no b_text)                                                     <- TextColor LEAKS to bare
  ```

  Exactly two of five hand-box colours switch. That is the bug, verbatim, in his data.
  Across both live profiles the entire fork amounts to
  `3 × b_border, 3 × b_fill, 2 × b_customGlowOn, 2 × b_customRippleOn` (+ four
  BareSenses word keys) — i.e. a handful of keys out of a ~60-key style surface.

Three aggravating factors, each a real defect:

1. **Whole style families are not forkable at all** (render reads the base regardless of tier):
   * drop cue — `HudElementView.cs:371` `Def.GetB("dropWholeBox", false)` and `:374`
     `Def.GetS("dropHiColor", "")`; editor at `:384-386` uses `d.SetB` / `d.Set` (no `EditBare`);
   * item-icon tint — `HandBoxesWidget.cs:181`, `EquipmentColumnWidget.cs:203` call the pure
     global `HudConfig.TintIcon` (`HudConfig.cs:284`);
   * chip icon tint — `StateChipsWidget.cs:110-111`, props at `:168-172`;
   * PngDoll region colours — `PngDollWidget.cs:133-135`, props at `:269-288`;
   * Compass FOV/tick/cardinal/degrees — `CompassWidget.cs:144-147`, props `:258-265`;
   * Readout `bar`/`barVertical`/`stack`/`wrap`/`target`/`tempIcon`/`row*Y` —
     `ReadoutWidget.cs:138-146, 194, 300, 351, 383-398`, props `:709-729`;
   * `EquipmentColumnWidget` `horizontal` (`:74`), `KeybindChipsWidget` `vertical` (`:38`),
     `SuitChipsWidget` `vertical` (`:116`), `VitalsPanelWidget` `rowPressure`/`rowTemp` (`:142-143`).

2. **On a Global-styled element the fork covers colours + font scale only.** Every sizing/glass/
   effect resolver returns the global *before* reading the slot (§1.2 list), and
   `AddUnifiedAppearanceProps` hides those rows entirely (`HudElementView.cs:1018-1021`). The
   element's `styleSource` itself is not per-tier (`:155`), so "flat in bare, glassy in suit"
   is inexpressible.

3. **Separation seeds only the base.** `SeedCustomStyleFromEffective(d)` (`:1450`) and
   `SetUnifiedStyleSourceWithoutView` (`:1540`) write `d.SetF/SetB/SetI` with no slot, so
   unchecking "Follow F9 global style" while the BARE tab is selected writes the whole snapshot
   into the shared base — a second, silent leak.

Non-causes ruled out (do not "fix" these): the colour picker (`HudPropDrawer.cs:306-365`) is
correct; `HudSystem.cs:1011-1014` correctly separates render tier from edit target;
`LayoutHash` (`:1451`) already re-lays-out on a bare↔suit flip; `Sanitize`'s orphan strip
(`HudDocument.cs:194-201`) correctly leaves `Tiers="All"` elements alone; the per-frame
`UpdatePanel` colour path re-resolves every frame, so live re-apply is not missing.

---

## 3. The universal per-tier style model

### 3.1 Principles

* **Generalise the one existing pattern.** `b_` becomes one of N slot prefixes. Nothing is
  parallel-invented.
* **Opt-in per element, default shared.** A profile that never opts in is byte-identical.
* **Seed on separation.** Turning a slot on writes a *complete* copy of the base's **stored**
  values (refs and `-1` sentinels verbatim, never resolved globals), so the fork is total from
  that instant and later base edits cannot leak. Copying stored values — not resolved ones —
  is what keeps palette-name refs and "follow the global" sentinels live, avoiding the frozen-
  snapshot drift called out in `HudDocument.cs:109-114`.
* **Look forks; content does not.** A knob that changes *how it looks* is forkable. A knob that
  changes *what it is* (readout source, PNG file names, slot slice, chip captions, point lists,
  sense order, Z, Tiers) stays shared. State this in the popup, don't leave silent gaps.

### 3.2 Data model

`UI/Hud/HudDocument.cs` — new enum next to `HudTierMask`:

```csharp
/// <summary>Which STYLE slot an element resolves against. Base is the suited/robot design every
/// slot inherits from; Bare and Robot are optional forks (see HudElementDef.TierStyleMask).
/// The prefix is exactly two chars so the zero-alloc param scan is unchanged.</summary>
public enum HudStyleSlot { Base = 0, Bare = 1, Robot = 2 }
```

`HudElementDef` additions (replacing the `b_`-hardcoded block at :673-807):

```csharp
// param key: "tierStyle" (int bitmask). bit0 = fork BARE, bit1 = fork ROBOT. 0 = shared (default).
public const int ForkBare  = 1;
public const int ForkRobot = 2;

internal static char SlotPrefixChar(HudStyleSlot s);         // Bare->'b', Robot->'r', Base->'\0'
internal static string SlotPrefix(HudStyleSlot s);           // "b_", "r_", ""

public int  TierStyleMask                    => GetI("tierStyle", 0);
public bool ForksSlot(HudStyleSlot s);                       // Base => false
public HudStyleSlot ResolveSlot(HudStyleSlot want)           // want when forked, else Base
    => ForksSlot(want) ? want : HudStyleSlot.Base;
public void SetForkSlot(HudStyleSlot s, bool on);            // sets/clears the bit; on OFF also ClearSlotOverrides(s)

public bool HasSlotOverride(HudStyleSlot s, string key);
public bool HasAnySlotOverride(HudStyleSlot s);
public void ClearSlotOverrides(HudStyleSlot s);

// tier-aware bag accessors (slot replaces the old `bool bare`)
public float  GetFFor(HudStyleSlot s, string key, float def);
public bool   GetBFor(HudStyleSlot s, string key, bool def);
public int    GetIFor(HudStyleSlot s, string key, int def);
public string GetSFor(HudStyleSlot s, string key, string def);
public void   SetFFor(HudStyleSlot s, string key, float v);
public void   SetBFor(HudStyleSlot s, string key, bool v);
public void   SetIFor(HudStyleSlot s, string key, int v);
public void   SetSFor(HudStyleSlot s, string key, string v);

// first-class field forks (keys: <p>fill, <p>border, <p>text, <p>bw, <p>rtl/rtr/rbr/rbl, <p>fs)
public string FillFor(HudStyleSlot s);      public void SetFillFor(HudStyleSlot s, string v);
public string BorderFor(HudStyleSlot s);    public void SetBorderFor(HudStyleSlot s, string v);
public string TextColorFor(HudStyleSlot s); public void SetTextColorFor(HudStyleSlot s, string v);
public float  BorderWidthFor(HudStyleSlot s); public void SetBorderWidthFor(HudStyleSlot s, float v);
public float  RTLFor/RTRFor/RBRFor/RBLFor(HudStyleSlot s);  // + setters
public float  FontScaleFor(HudStyleSlot s);                  // + setter
```

Rules:
* **Every read accessor calls `ResolveSlot` first.** An element that has not opted in reads the
  base even if stale `b_`/`r_` residue exists — dead data can never resurrect.
* Reads stay allocation-free: `FindSlotOverride(HudStyleSlot, string)` is the existing
  `FindBareOverride` scan with the prefix char passed in (`k[0] == pfx && k[1] == '_'`).
* Writers may concatenate — they only ever run at edit time.
* `SetSFor(s, key, null)` still **removes** (the shared `Set` contract) = "this slot goes back
  to inheriting the base for this key". That stays true even inside a fully seeded fork.
* The **layout** forks (`bLayout`/`b*`, `mA*`…) are untouched by Wave C. Keep them; they already
  work and have their own explicit seeded toggle. `SetForkSlot`/`ClearSlotOverrides` must not
  touch `bLayout`/`bX`/`bY`/`bW`/`bH`/`bWPct`/`bHPct`/`bAnchor` (no underscore) — reuse the
  existing "two chars then `_`" discriminator.

### 3.3 XML

Untouched profile (default — nothing new is written):

```xml
<El Id="g2-hands" Type="HandBoxes" Tiers="All" Fill="#33CE0005" Border="#33CE00E3" TextColor="#23FF00F5" …>
  <P K="gap" V="24.576" />
  <P K="activeBorderColor" V="#52FF29E6" />
</El>
```

After the author ticks **Separate BARE style** (seeded, complete — note `b_bw="-1"` and
`b_fill` keeping the *ref*, not a resolved colour):

```xml
<El Id="g2-hands" Type="HandBoxes" Tiers="All" Fill="#33CE0005" Border="#33CE00E3" TextColor="#23FF00F5" BorderWidth="0.076" …>
  <P K="gap" V="24.576" />
  <P K="activeBorderColor" V="#52FF29E6" />
  <P K="tierStyle" V="1" />
  <P K="b_fill"   V="#33CE0005" />
  <P K="b_border" V="#33CE00E3" />
  <P K="b_text"   V="#23FF00F5" />
  <P K="b_bw"     V="0.076" />
  <P K="b_rtl" V="11.771" /><P K="b_rtr" V="11.771" /><P K="b_rbr" V="11.771" /><P K="b_rbl" V="11.771" />
  <P K="b_fs"     V="0.599" />
  <P K="b_styleSource" V="2" />
  <P K="b_gap"    V="24.576" />
  <P K="b_activeBorderColor" V="#52FF29E6" />
  <P K="b_iconTintMode" V="0" />
  … (one b_<key> for every forkable row the widget declares)
</El>
```

Then the author changes the bare fill to black: only `b_fill` moves. Switching to SUITED and
changing the active-hand border moves only `activeBorderColor` — **BARE keeps its own copy.**

With ROBOT also forked, `tierStyle` is `3` and an `r_` twin exists for each key.

### 3.4 Schema + `Sanitize` upgrade

`HudDocument.Schema` goes **14 → 15**. `Glassy40Default.cs:14` stamps `Schema='15'`.
`HudSystem.cs:239/:247` gate shipped-default *replacement* on `Schema < 6` ("Default") and
`< 12` ("Glassy 2.0") only — **add no new replacement rule**; the shipped profiles now arrive
via `HudProfileStore.SyncShipped` and are repaired by `Sanitize`, not rebuilt.

New idempotent step inside `HudDocument.Sanitize()`, in the per-element loop, gated on
`Schema < 15` for the *seed* half so it runs once:

```
for each element:
  legacyFork = HasAnySlotOverride(Bare) && Params has no "tierStyle"
  if legacyFork:
      SetI("tierStyle", ForkBare)              // preserve the fork the author already built
      needsSeed.Add(element)                   // completed below, once
  if (TierStyleMask & ForkBare)  != 0 && !IsBoth(Tiers): SetForkSlot(Bare, false)   // orphan strip
  if (TierStyleMask & ForkRobot) != 0 && (Tiers & Robot) == 0: SetForkSlot(Robot, false)
```

The *seed* of a legacy fork cannot run inside `HudDocument` (it needs the widget prop list, see
§3.6), so `Sanitize` only sets the flag and raises `RepairedOnLoad`; `HudSystem.RebuildViews`
performs `SeedSlotFromBase` for any element carrying `tierStyleNeedsSeed` (a transient param
written by `Sanitize` and deleted by the seeder) on the first build after load, then
`HudProfileStore.MarkChanged()`. This is value-preserving by construction: every key it writes
holds exactly what the slot already resolved to.

Log one line per category, as `Sanitize` already does:
`"{label}: adopted N legacy bare override(s) into the per-tier fork."`

### 3.5 Render-time resolution

`HudElementView`:

```csharp
internal static HudStyleSlot LayoutSlot;      // replaces LayoutBare (render tier)
internal static HudStyleSlot EditTargetSlot;  // replaces EditBareTier (edit tier)
internal static bool LayoutBare => LayoutSlot == HudStyleSlot.Bare;   // kept: HudWarp/HudSystem read it

/// <summary>This element's resolved style slot for the CURRENT frame — the render slot when the
/// element forks it, else Base. Widgets read THIS, never LayoutSlot.</summary>
protected HudStyleSlot Slot => Def != null ? Def.ResolveSlot(LayoutSlot) : HudStyleSlot.Base;

/// <summary>The slot an F9 edit writes to: the explicitly previewed tier when this element forks
/// it, else Base. Keying off the EXPLICIT preview (not the live tier) is unchanged from
/// EditBare's contract — a 'Live' preview never silently forks.</summary>
internal static HudStyleSlot EditSlot(HudElementDef d)
    => d == null ? HudStyleSlot.Base : d.ResolveSlot(EditTargetSlot);
```

`HudSystem.cs:1011-1014` becomes:

```csharp
HudStyleSlot previewSlot =
    !ForceTier.HasValue                 ? HudStyleSlot.Base :
    ForceTier.Value == HudTier.Bare     ? HudStyleSlot.Bare :
    ForceTier.Value == HudTier.Robot    ? HudStyleSlot.Robot : HudStyleSlot.Base;
HudElementView.LayoutTier      = tier;
HudElementView.EditTargetSlot  = editorActive ? previewSlot : HudStyleSlot.Base;
HudElementView.LayoutSlot      = editorActive ? previewSlot
                               : tier == HudTier.Bare  ? HudStyleSlot.Bare
                               : tier == HudTier.Robot ? HudStyleSlot.Robot : HudStyleSlot.Base;
```

`LayoutHash` (:1451): replace `+ (HudElementView.LayoutBare ? 4099f : 0f)` with
`+ (int)HudElementView.LayoutSlot * 4099f` so a Suited↔Robot flip also re-lays-out and re-meshes.

**Live re-apply audit (constraint e):**
* Colours / bag params consumed in `UpdatePanel` → re-read every frame. Automatic.
* Values consumed in `Layout(scale)` (radii, gaps, icon scale, insets) → the `LayoutHash` change
  above forces `RelayoutAll` + `DirtyAllMeshes` on a slot flip.
* Moving-warp widgets (compass ticks, `MoodletBorrowWidget` cells) warp their *position* per
  frame from values they re-read per frame → automatic.
* Borrow widgets (`Moodlet`/`Portrait`/`DamageDoll`) style only their own holder/panel from
  `Def` per frame → automatic. The borrow hand-back contract is untouched.
* `PanelGraphic`/`PolygonPanelGraphic` setters are dirty-guarded, so a slot flip that resolves
  to the same value costs nothing.

**Style source becomes per-slot.** `StyleSourceOf(d)` → `StyleSourceOf(d, slot)` reading
`d.GetIFor(slot, "styleSource", StyleGlobal)`; `customStyleReady` likewise. Instance
`UsesGlobalStyle`/`UsesCustomStyle` resolve against `Slot` on the render path and against
`EditSlot(Def)` inside `DescribeProps`. `SeedCustomStyleFromEffective(d)` →
`SeedCustomStyleFromEffective(HudElementDef d, HudStyleSlot slot)` and every `d.SetX(...)` in it
becomes `d.SetXFor(slot, ...)`; same for `SetUnifiedStyleSourceWithoutView(el, followGlobal,
forceCustomSnapshot)` which gains a trailing `HudStyleSlot slot = HudStyleSlot.Base`
(`HudStyleMigration` keeps calling it with the default).

### 3.6 Seeding — `SeedSlotFromBase`

Do **not** hand-maintain a key manifest; the prop list already is one.

```csharp
/// <summary>Complete a slot fork: copy every FORKABLE authored value from the base into
/// <paramref name="slot"/>, so later base edits can never leak into it. Values are copied as
/// STORED (palette-name refs, -1 "follow the global" sentinels) — never as resolved colours or
/// globals — so the fork keeps tracking the palette/theme exactly as the base does.</summary>
internal static void SeedSlotFromBase(HudElementView view, HudStyleSlot slot)
```

Algorithm (runs once, on the click that enables the fork, and from the Schema-15 adoption):

1. `var prev = EditTargetSlot; EditTargetSlot = HudStyleSlot.Base;`
2. `view.DescribeProps(scratch)`; flatten `TabGroup`/`TabPage` children.
3. For every leaf, record `p.Get()` **except** when
   `p.Kind ∈ { Header, Points, TierMask, TabGroup, TabPage }` or
   `p.StableId ∈ SeedSkip` (below).
4. Set the fork bit: `d.SetForkSlot(slot, true)`.
5. `EditTargetSlot = slot;` rebuild the prop list (the list must be rebuilt — the setters closed
   over `EditSlot(d)` evaluate it at call time, but the *visible rows* depend on the slot's own
   `styleSource`), then `p.Set(recorded)` for each key matched by `StableId ?? (Group + Label)`.
6. `EditTargetSlot = prev;` `HudProfileStore.MarkChanged()`.

`SeedSkip` (setters with side effects or identity semantics — give each of these an explicit
`StableId` via the existing `WithId` helper so the match is stable):
`"styleSource"` (its own row is handled by step 4/5 writing `b_styleSource` directly),
`"zOrder"`, `"tiers"`, `"readoutSrc"`, `"pngPaths"`, `"eqFirst"`, `"eqCount"`,
`"chipCaptions"`, `"senseOrder"`, `"curveMode"`, `"curveSteps"`, `"closed"`, `"smooth"`,
`"wrapPoints"`.

Safety: for a row that is *not* actually tier-aware, step 5 writes the same value back to the
same base storage — a no-op round trip. That is why the skip list only needs to cover
side-effecting setters, not every non-forked row.

### 3.7 Making the missing surfaces forkable

Convert to the slot accessors (C1):

| File | Keys to fork |
|---|---|
| `HudElementView.cs:371-388` | `dropWholeBox`, `dropHiColor` (render **and** props) |
| `HandBoxesWidget.cs` | already forked; add `iconTintMode` / `iconTintColor` |
| `EquipmentColumnWidget.cs` | already forked; add `horizontal`, `iconTintMode`, `iconTintColor` |
| `StateChipsWidget.cs:110-111, 168-172` | `iconTintOn`, `iconTint` |
| `PngDollWidget.cs:133-135, 269-288` | `cHealthy`, `cWarn`, `cCrit`, `wholeBody` |
| `CompassWidget.cs:144-147, 258-265` | `fov`, `tickDeg`, `cardinalDeg`, `degrees` |
| `ReadoutWidget.cs` | `bar`, `barVertical`, `stack`, `wrap`, `target`, `tempIcon`, `rowTitleY`, `rowTargetY`, `rowValueY` |
| `KeybindChipsWidget.cs:38, 160` | `vertical` |
| `SuitChipsWidget.cs:104, 116` | `vertical` |
| `VitalsPanelWidget.cs:142-143, 582-583` | `rowPressure`, `rowTemp` |

**Deliberately NOT forked** (content/identity — say so in the popup with a `HudProp.Header`):
`ReadoutWidget` `src` + `label`; `PngDollWidget` `i*` PNG names + `injuredOnly`;
`EquipmentColumnWidget` `first`/`count`; `KeybindChipsWidget` `chipN`;
`SuitChipsWidget` `internals`; `BodyDollWidget` `auto`; `DynamicTextWidget` `utc`/`date`/`number`;
`BareSensesWidget` `order`; `PrimitiveView` `closed`/`smooth`/`curveMode`/`curveSteps`/`wrap`;
`Def.Z`, `Def.Tiers`, `Def.Text`, `Def.Icon`, `Def.Align`, point lists.

### 3.8 Per-element item-icon tint (directive 3)

Global `HudConfig.TintItemIcons` (`HudConfig.cs:350`, F9 row at `HudEditorWindow.cs:573`) and the
`HudItemIconTint` palette entry **stay** and remain the inherited default.

New per-element, per-slot pair — mirrors the existing transition tri-state vocabulary:

```csharp
// HudElementView
/// <summary>Item-thumbnail tint for THIS element in the current slot. Mode 0 = inherit the
/// global "Tint item icons" checkbox (+ HudItemIconTint), 1 = force on with this element's own
/// colour ref (empty = the palette entry), 2 = force off. Preserves the icon's own alpha, the
/// same contract as HudConfig.TintIcon.</summary>
protected Color TintItemIcon(Color c);   // reads GetIFor(Slot,"iconTintMode",0) / GetSFor(Slot,"iconTintColor","")

/// <summary>The two F9 rows, appended by any widget that draws item thumbnails.</summary>
protected void AddIconTintProps(List<HudProp> into);   // Group = Appearance, tier-aware
```

Call sites to convert: `HandBoxesWidget.cs:181`, `EquipmentColumnWidget.cs:203`.
`UI/Grid/BagGridCell.cs:344,806` keeps the global (a Grid cell is not an element) — the Grid's
own tier handling is §4.

---

## 4. Universal Inventory (Grid) suit/bare (directive 4)

**Chosen: option (ii), a small "Grid appearance per tier" subset.** Rejected: giving all 31
`GridTheme` entries a bare twin — it doubles a 31-row popup for a modal window the player opens
deliberately, and the real ask is "the inventory shouldn't look powered when my suit isn't".
The subset uses the *same vocabulary* as the element model (ColorRef strings, `-1` = inherit,
0/1/2 tri-state), so it reads as one system.

`UI/Grid/GridTheme.cs`, section `13. The Grid`, five new entries:

```csharp
public static ConfigEntry<bool>   PerTier;        // "GridPerTierStyle",   default false
public static ConfigEntry<string> BareFillRef;    // "GridBareFill",       default "" = inherit
public static ConfigEntry<string> BareBorderRef;  // "GridBareBorder",     default ""
public static ConfigEntry<string> BareTextRef;    // "GridBareText",       default ""
public static ConfigEntry<float>  BareOpacity;    // "GridBareOpacity",    default -1 = inherit (0..1 multiplies resolved alpha)
public static ConfigEntry<int>    BareFrostMode;  // "GridBareFrost",      default 0 = Inherit / 1 On / 2 Off
```

Resolution:

```csharp
/// <summary>The tier the Grid paints for. Fed each frame by TheGridPanel from
/// HudSystem.LastSnapshot.Tier (Bare -> Bare, else Base); Base while the HUD is unavailable.</summary>
public static HudStyleSlot Slot { get; set; }
private static bool BareActive => PerTier != null && PerTier.Value && Slot == HudStyleSlot.Bare;
```

`Fill` / `Border` / `Text` (`GridTheme.cs:294-317`) gain, before their existing body:
if `BareActive` and the matching `Bare*Ref` is non-empty, resolve **that** instead; then, if
`BareActive && BareOpacity >= 0`, multiply the resolved alpha. `FrostOn` resolution honours
`BareFrostMode` when `BareActive`.

`StyleHash()` (`GridTheme.cs:876`) must fold `(int)Slot`, `PerTier`, the three refs, `BareOpacity`
and `BareFrostMode` — otherwise the open window keeps a stale skin across a tier flip.
`TheGridPanel.Tick` already polls `StyleHash`; it additionally sets `GridTheme.Slot` from
`HudSystem.LastSnapshot` (guard `LastSnapshot == null || !LastSnapshot.Valid` → `Base`), and
`PinnedInventoryWindow.TickAll` inherits it because it polls the same hash.

`GridTheme.DescribeProps` gains a `"Per tier (suited vs bare)"` header + the five rows in **both**
cached lists (`_propsFollow`, `_propsOverride`, `GridTheme.cs:983-984`) — the rows read `PerTier`
live, so no new cache key is needed; do **not** add a third cached list.

---

## 5. Theme folds (directive 5)

### 5.1 New snapshot families in `HudTheme`

| Prefix | Source | Count |
|---|---|---|
| `cfg:` | `HudConfig` public static `ConfigEntryBase` fields minus `Exclude` | existing (150) |
| `pal:` | `HudPalette.All` | existing (22) |
| `rad:` | `Overlay.RadialPalette.All` | existing (24) |
| **`radial:`** | `UIAConfig` fields named in `RadialThemeKeys` (include-list, below) | **46** |
| **`grid:`** | `GridTheme.SnapshotInto` / `ApplyFrom` | **36** (31 + the 5 new) |
| **`menu:`** | `UiaMenuTheme.SnapshotInto` / `ApplyFrom` | **20** (`Follow` + 19) |

`Snapshot()`/`Apply()` grow four lines each; the two foreign systems expose their own pair so
`HudTheme` never reflects over another class's private layout:

```csharp
// UI/Grid/GridTheme.cs   (owner: C1)
public static void SnapshotInto(List<HudDocument.ThemeEntry> into, string prefix);
public static void ApplyFrom(Dictionary<string, string> map, string prefix);

// UI/Menu/Kit/UiaMenuTheme.cs   (owner: C2)
public static void SnapshotInto(List<HudDocument.ThemeEntry> into, string prefix);
public static void ApplyFrom(Dictionary<string, string> map, string prefix);
```

Both reflect (or iterate their own array) over their **own** `ConfigEntry` fields, key on the
field/override name, and use `GetSerializedValue()` / `SetSerializedValue()` inside `try/catch`
exactly like `HudTheme.cs:61,93`. `Apply` semantics are unchanged: a key absent from the
snapshot leaves that setting alone (this is what makes the fold non-breaking for old profiles).

### 5.2 `radial:` include-list (explicit, in `HudTheme.cs`)

Reflection over all of `UIAConfig` would drag in keybinds, control schema, hotkeys and
per-machine behaviour, so this family is an **include-list of field names**:

*Section "8. Radial Visuals" (32):* `RadialOuterRadius`, `RadialInnerRadius`, `IconFlipV`,
`RadialIconRatio`, `RadialShineIntensity`, `ParkedChipRadius`, `RadialShowWedgeLabels`,
`RadialEdgeFeather`, `RadialBorderWidth`, `RadialSideBorders`, `RadialSideWidthInner`,
`RadialSideWidthOuter`, `RadialWedgeGapDeg`, `RadialDimShading`, `RadialDimStrength`,
`RadialFontName`, `RadialUppercaseLabels`, `RadialShowStateText`, `RadialBindingCurved`,
`RadialSatelliteScale`, `RadialHubTitleSize`, `RadialTextVerb`, `RadialTextLabel`,
`RadialTextSub`, `RadialTextWarn`, `RadialRotateLongLabels`, `RadialSatelliteHubRatio`,
`RadialDynamicReadoutText`, `RadialFrost`, `RadialFrostStrength`, `RadialSheen`,
`RadialEdgeLight`.

*Section "10. Hint Bar" (14):* `HintBarCorner`, `HintBarBorderWidth`, `HintBarFeather`,
`HintBarSheen`, `HintBarSpec`, `HintBarGlow`, `HintBarGlowWidth`, `HintBarFontSize`,
`HintBarBold`, `HintBarHeight`, `HintBarPadding`, `HintBarDrop`, `HintBarFrost`,
`HintBarFrostStrength`.

**Excluded on purpose:** `HintBarPreview` (`PreviewOverWhite`, an editor-preview toggle — the
same class of thing as `HudEditorKey`); every behaviour knob in "8. Radial Visuals"' sibling
sections (`RadialMaxWedges`, flick/double-tap windows, sounds, hint-fade, `RadialHintBar`
on/off) — those are *behaviour*, not look, and must not change when a player tries a theme.

Field-name include-lists are validated at first use: log one `UIALog.Warn` naming any entry that
does not resolve to a `ConfigEntryBase` field, so a rename can never silently drop a knob.

### 5.3 `Exclude` additions (perf knobs must NOT travel)

Append to `HudTheme.Exclude` (`HudTheme.cs:31-36`) — verified `HudConfig` field names:
`"FrostDownsample"` (`:191`), `"FrostUpdateEveryN"` (`:192`), `"FxBloomRes"` (`:209`),
`"FxBloomBlurSteps"` (`:202`). Also add `"FxBloomFineDetail"` (`:204`) — it is the legacy
alias `FxBloomRes = -1` resolves through (`HudEditorWindow.cs:1521-1522`); letting one travel
without the other would make an imported theme's bloom resolution non-deterministic.

Frost/bloom **look** knobs keep travelling: `FrostStrength`, `FrostDepth`, `FrostTint`/
`FrostDarken` (whatever the exact field names are — anything that is not in the five above),
`FxBloom*` strength/threshold. Re-verify by diffing the `cfg:` key list before/after: exactly
five keys must disappear.

### 5.4 `MarkThemeChanged` wiring (the fold's other half)

Today a Grid or Menu edit changes the look but never restamps the active profile's theme, so it
would not travel even after the fold. Add `HudProfileStore.MarkThemeChanged()` to:

* `Windows/HudEditorWindow.cs:1030` `DrawMenuThemePopup` and `:1058` `DrawGridStylePopup` —
  pass a non-null `onChanged` into `HudPropDrawer.DrawAll(list, null, null, null, onChanged)`.
  *(File owned by C1 — see §7 interface note.)*
* `UI/Menu/Tabs/RadialTab.cs` — every `UiaControls.*Row` setter that writes a key in §5.2
  (wheel/hub size, caps, state text, binding-curved, icon size, border thickness, edge softness,
  shine, wedge gap, dim, child wheel size, the five text sizes…). **Not** the behaviour rows
  (flick, double-tap, sounds, max wedges, hint fade).
* `UI/Menu/Tabs/ControlsTab.cs:24` — `RadialHintBar` is behaviour: leave it alone. Only add the
  call if a travelling key is written there.
* `Windows/SettingsWindow.cs` — the ImGui radial/hint-bar sliders (it already calls
  `MarkThemeChanged` at `:266, :272, :300, :379`; extend to the newly-travelling knobs).

Contract to preserve (`HudProfileStore.cs:400-410`): `MarkChanged` = layout dirty,
`MarkThemeChanged` = theme dirty. A pure layout edit must still never restamp the theme.

### 5.5 `ConfigMigration` v2 → v3

`Core/ConfigMigration.cs`: `CurrentVersion = 3`, new `case 2:`.

The problem: when the fold lands, an updating player's profiles carry no `radial:`/`grid:`/
`menu:` keys. `Apply` leaves absent keys alone, so **their look does not change** — good — but
those knobs would also never start travelling until the player happened to edit one. The
migration's job is therefore to *capture*, not to *restore*.

Because `ConfigMigration.Run` executes at bind time (before any profile is loaded), the step
sets a one-shot flag and the work happens on first profile load:

```csharp
case 2: // v2 -> v3 — the 0.9.2.5 Wave C theme fold: radial visuals + hint bar, the Grid theme
        // and the Control Center theme now travel INSIDE a HUD profile. Stamp the player's
        // CURRENT globals into every profile that already carries a theme, so the fold is a
        // no-op for their eyes and every profile starts out holding the look they have now.
    PendingThemeTopUp = true;
    break;
```

```csharp
/// <summary>Set by the v2->v3 step; consumed exactly once by HudProfileStore after the profile
/// folder is available. Never persisted — the ConfigVersion stamp is the real record.</summary>
public static bool PendingThemeTopUp;
```

`Features/HudProfileStore.cs`, immediately after `SyncShipped` / before the first `LoadActive`:

```csharp
/// <summary>One-shot: for EVERY profile on disk that already carries a theme, append any theme
/// key missing from the snapshot using the CURRENT global value, then save. A themeless profile
/// stays themeless (it deliberately means "use whatever globals are current"). Idempotent —
/// re-running adds nothing.</summary>
internal static void TopUpAllThemes();
```

backed by

```csharp
// HudTheme.cs
/// <summary>Add every key present in a fresh Snapshot() but MISSING from <paramref name="theme"/>,
/// leaving existing values untouched. Returns how many were added.</summary>
public static int TopUp(List<HudDocument.ThemeEntry> theme);
```

Order of operations on the migrating launch:
1. all `Bind` calls → `ConfigMigration.Run` sets `PendingThemeTopUp`;
2. `HudProfileStore.SyncShipped` (unchanged);
3. `if (ConfigMigration.PendingThemeTopUp) { TopUpAllThemes(); ConfigMigration.PendingThemeTopUp = false; }`
4. `LoadActive(...)` → `SetActive` → `HudTheme.Apply` now sees a complete snapshot and re-applies
   the values it just captured — a no-op by construction.

Fail-soft: `TopUpAllThemes` wraps each file in `try/catch`, logs one summary line, and never
blocks load. A failed top-up costs nothing but a later capture.

---

## 6. `Overlay/Theme.cs` re-point (C3)

`Overlay/Theme.cs` is 23 `uint` + 7 `Color` compile-time constants, none of which follow the
palette or a profile's theme. Verified **live** consumers (everything else that greps as
`Theme.` is `UI.Menu.Kit.UiaTheme`, which is already palette-driven through `UiaMenuTheme` —
the audit's claim that `UiaControls.cs` reads `Overlay.Theme` is **stale, no work needed**):

| Consumer | Line(s) | Member |
|---|---|---|
| `Overlay/Toast.cs` | 11, 28 | `TextPrimary`, `PanelBgSolid`, `PanelBorder` |
| `Overlay/DrawUtil.cs` | 35, 87, 90 | `C(...)`, `PanelBorder` |
| `Features/ItemMenuBuilder.cs` | 40, 189, 523-524, 549, 563-564, 666, 727 | `Accent`, `Good`, `Warn`, `RingStow` |
| `Features/BagRadialFeature.cs` | 101, 367, 368 | `Accent`, `RingStow` |
| `Features/ToolbeltRadialFeature.cs` | 49, 119, 120, 169 | `Accent`, `RingStow` |
| `Features/SmartStowPlus.cs` | 168 | `TextPrimary` |
| `UI/UnityRadialView.cs` | 1102, 1107 | `Critical`, `Warn` |

Design: keep every member's **name and type**, turn the `static readonly` fields into **live
properties** that resolve from the palettes, with the current constant as the fail-soft fallback.
No call site's syntax changes (`Theme.Accent` still yields a `uint`).

```csharp
// Overlay/Theme.cs
private static uint U(HudPalette.Entry e, uint fallback);            // ImGui packed ABGR
private static uint U(RadialPalette.Entry e, uint fallback);
public static uint PanelBg      => U(HudPalette.PanelFill,   _cPanelBg);
public static uint PanelBorder  => U(HudPalette.PanelBorder, _cPanelBorder);
public static uint TextPrimary  => U(HudPalette.TextValue,   _cTextPrimary);
public static uint TextDim      => U(HudPalette.TextLabel,   _cTextDim);
public static uint TextDisabled => U(HudPalette.TextDim,     _cTextDisabled);
public static uint Accent       => U(RadialPalette.TextAccent, _cAccent);
public static uint Good         => U(HudPalette.Good,       _cGood);
public static uint Warn         => U(HudPalette.Warn,       _cWarn);
public static uint Critical     => U(HudPalette.Critical,   _cCritical);
public static uint RingBg       => U(RadialPalette.WedgeBg, _cRingBg);
public static uint RingHover    => U(RadialPalette.WedgeHover, _cRingHover);
public static uint RingHoverRim => U(RadialPalette.WedgeBorderHover, _cRingHoverRim);
public static uint RingSep      => U(RadialPalette.WedgeBorder, _cRingSep);
public static uint RingDisabled => U(RadialPalette.WedgeDisabled, _cRingDisabled);
public static uint RingStow     => U(RadialPalette.WedgeStow, _cRingStow);
public static uint RingStowHover=> U(RadialPalette.WedgeStowHover, _cRingStowHover);
public static uint HubBg        => U(RadialPalette.HubFill, _cHubBg);
public static Color UguiBg      => RadialPalette.WedgeBg?.Value ?? _cUguiBg;   // (C# 7.3: use an explicit null check)
… UguiSelectedBlue -> WedgeHover, UguiSelectedBlueBright -> WedgeBorderHover,
  UguiOrange -> WedgeBorder, UguiOrangeBright -> WedgeBorderHover,
  UguiDisabled -> WedgeDisabled, UguiShine -> RimShine
```

Constraints for C3:
* **C# 7.3** — no `?.` on a `ConfigEntry<T>` value chain that needs a default; write explicit
  null checks. No target-typed `new`.
* Palette entries may be **null before `Bind`** and after an F6 reload window — every accessor
  must fall back to the constant, never throw. Keep the private `_c*` constants as the fallback
  and as the record of the shipped look.
* `PanelBgSolid` / `AccentDim` stay derived (alpha-boosted / alpha-reduced forms of `PanelBg` /
  `Accent`) so they track automatically.
* `C(r,g,b,a)` and `StateColor(ratio)` keep their signatures; `StateColor` now returns the live
  `Critical`/`Warn`/`Good`.
* These are **ImGui** draw paths, so glyph rules do not apply, but the packed-colour order does:
  keep using `ImGui.ColorConvertFloat4ToU32` — do not hand-pack.
* Cheap by construction: each property is one config read plus a cached parse
  (`HudPalette.Entry.Value` already caches). These run inside per-frame ImGui draw lists;
  do not add allocations.

Result: Toast, the radial wedge overrides and the item menu all follow the active profile's
theme, and — because `pal:`/`rad:` already travel — they follow a profile switch for free.

---

## 7. Implementation partition (zero file overlap)

### C1 — per-tier style core + editor + tint + Grid tier (opus)

**Owns, exclusively:**

```
UI/Hud/HudDocument.cs
UI/Hud/HudElementView.cs
UI/Hud/PrimitiveView.cs
UI/Hud/HudStyleMigration.cs
UI/Hud/HudTransitionFx.cs
UI/Hud/HudSystem.cs
UI/Hud/Glassy40Default.cs            (Schema 14 -> 15 only)
UI/Hud/HudProp.cs                    (add StableId usages / no schema change)
UI/Hud/Widgets/*.cs                  (all 16)
Windows/HudEditorWindow.cs
Windows/HudEditorMode.cs
Windows/HudPropDrawer.cs
UI/Grid/GridTheme.cs
UI/Grid/TheGridPanel.cs
UI/Grid/BagGridCell.cs               (only if the Grid tier work needs it)
```

Work items: §3.2 data model; §3.4 Schema 15 + `Sanitize` adoption; §3.5 render/edit slots +
`LayoutHash`; §3.6 `SeedSlotFromBase`; §3.7 the missing surfaces; §3.8 per-element icon tint;
§4 Grid per-tier + `GridTheme.SnapshotInto`/`ApplyFrom`; editor: replace the two-button mode tab
(`HudEditorWindow.cs:1182-1196`) with

* a **"Per-tier style"** block: `[x] Separate BARE style` and (when `Tiers` includes Robot)
  `[x] Separate ROBOT style` — each seeds on enable (§3.6) and clears on disable, both bracketed
  by the existing `beginElementEdit`/`commitElementEdit` undo pair;
* a **mode tab row** `SUITED | BARE | ROBOT` (BARE/ROBOT shown only when that tier is in `Tiers`),
  driving `HudSystem.ForceTier` as today;
* the Wave A coloured edit-target line reused inside the popup (replace the `TextDisabled` at
  `:1192-1194` with `DrawEditTargetLine()`), plus one line stating whether the current tab is
  writing its own slot or the shared base;
* `HudPropDrawer`: draw a small ASCII marker (`*`) before the label of any row for which
  `Def.HasSlotOverride(EditSlot(Def), key)` — this needs the prop to carry its key, so add
  `public string SlotKey;` to `HudProp` and set it at the tier-aware call sites (or reuse
  `StableId`). Optional-but-recommended; drop it if it balloons the diff.

**Interfaces C1 must expose for the others:**

```csharp
// UI/Grid/GridTheme.cs  — called by C2 from HudTheme.Snapshot/Apply
public static void SnapshotInto(List<HudDocument.ThemeEntry> into, string prefix);
public static void ApplyFrom(Dictionary<string, string> map, string prefix);
```

and, inside `Windows/HudEditorWindow.cs` (C1's file, C2's requirement — implement it, C2 will
not touch this file):

```csharp
// DrawMenuThemePopup / DrawGridStylePopup: was DrawAll(list, null, null, null, null)
HudPropDrawer.DrawAll(_menuPropScratch, null, null, null,
    () => Features.HudProfileStore.MarkThemeChanged());
```

### C2 — theme folds + Exclude + migration v2→v3 (sonnet)

**Owns, exclusively:**

```
UI/Hud/HudTheme.cs
Core/ConfigMigration.cs
Features/HudProfileStore.cs
UI/Menu/Kit/UiaMenuTheme.cs
UI/Menu/Tabs/RadialTab.cs
UI/Menu/Tabs/ControlsTab.cs
UI/Menu/Tabs/StorageTab.cs           (only if it writes a travelling key)
Windows/SettingsWindow.cs
```

Work items: §5.1 the three new families + the two foreign `SnapshotInto`/`ApplyFrom` pairs
(writes the `UiaMenuTheme` one itself; **calls** C1's `GridTheme` one); §5.2 the
`RadialThemeKeys` include-list, living in `HudTheme.cs`, with the "unresolved name" warning;
§5.3 `Exclude` additions; §5.4 `MarkThemeChanged` in the F10 tabs and `SettingsWindow`
(**not** in `HudEditorWindow` — C1 does that one); §5.5 `CurrentVersion = 3`, the `case 2:` step,
`PendingThemeTopUp`, `HudTheme.TopUp`, `HudProfileStore.TopUpAllThemes` and its call site.

**Written interface C2 depends on (do not implement it yourself):**
`UI.Grid.GridTheme.SnapshotInto(List<HudDocument.ThemeEntry>, string)` and
`UI.Grid.GridTheme.ApplyFrom(Dictionary<string,string>, string)` come from C1. Write the calls;
if C1's branch is not merged yet the Integrate agent resolves the compile. Do **not** add a
temporary reflection shim — it will survive by accident.

`UIAConfig.cs` is **read-only** for C2 (the include-list lives in `HudTheme.cs`, so nobody edits
`UIAConfig.cs` this wave).

### C3 — `Overlay/Theme.cs` re-point to the palette (sonnet)

**Owns, exclusively:**

```
Overlay/Theme.cs
Overlay/Toast.cs
Overlay/DrawUtil.cs
Features/ItemMenuBuilder.cs
Features/BagRadialFeature.cs
Features/ToolbeltRadialFeature.cs
Features/SmartStowPlus.cs
UI/UnityRadialView.cs
```

Work items: §6. Most consumer files need **no edit at all** (the member names and types are
unchanged) — they are listed as owned only so no other agent touches them. If a consumer needs
no change, leave it untouched and say so in the report.

**Do not touch** `UI/Menu/Kit/UiaControls.cs` or any `UiaTheme` member: that path is already
palette-driven and belongs to the Control Center theme, which C2 is folding.

### Overlap ledger

| File | Wanted by | Owner | Resolution |
|---|---|---|---|
| `Windows/HudEditorWindow.cs` | C1 (popup), C2 (`MarkThemeChanged` in the Grid/Menu popups) | **C1** | C1 implements the two `onChanged` callbacks per the snippet above |
| `UI/Grid/GridTheme.cs` | C1 (per-tier), C2 (`grid:` fold) | **C1** | C1 writes `SnapshotInto`/`ApplyFrom`; C2 calls them |
| `UI/Menu/Kit/UiaMenuTheme.cs` | C2 only | **C2** | — |
| `Features/HudProfileStore.cs` | C2 (top-up) | **C2** | C1 needs `MarkChanged()`/`MarkThemeChanged()` only, both already public |
| `UI/Hud/HudConfig.cs` | nobody | — | read-only this wave (field names verified, no edits) |

---

## 8. Acceptance criteria

**A. FlorpyDorp's repro (blocking).**
1. F9 → select the hand boxes → tick **Separate BARE style**.
2. Mode tab **BARE** → set Fill, Border, Text/accent, Active-hand border, Inactive hand-name,
   Empty state-line, Icon tint to obviously different values.
3. Mode tab **SUITED** → every one of those seven reads its **original** value.
4. Change two of them in SUITED → back to **BARE** → BARE still shows the values from step 2.
5. Repeat 1-4 on the 1-6 equipment column (slot number, slot label, empty slot label, fill,
   border, icon tint) — same result.
6. Close F9, walk in-game between suited and bare (helmet off / suit power off): the whole
   hand-box + 1-6 look switches both ways, every frame, with no relayout hitch.

**B. Default-preservation.** With per-tier **off** (the default), open, edit and save an existing
profile: the XML gains no `tierStyle`, no `b_*`, no `r_*` key it did not already have, and the
rendered HUD is pixel-identical to pre-Wave-C.

**C. Legacy fork adoption.** Load `Pure HUD.xml` (Schema 7, carries `b_fill`/`b_border` on
`g2-hands`): it renders identically on first launch, is rewritten once with
`tierStyle="1"` plus a complete `b_*` set whose values equal what those keys already resolved to,
and the log shows exactly one adoption line.

**D. Profile round-trip.** Two profiles with different per-tier hand-box colours: switching
between them (Control Center → Profiles) restores each one's bare **and** suited look. Restart
the game: both survive.

**E. Grid tier.** Tick **Grid per-tier**, set a bare fill/opacity; open the Universal Inventory
while suited (normal skin) and while bare (the bare skin) — including a pinned window that was
already open when the tier flipped.

**F. Theme fold, no visual change on update.** A player upgrading from a v2 config: first launch
after the update looks **identical**. Their profile XMLs each grow by ~102 theme entries
(46 `radial:` + 36 `grid:` + 20 `menu:`); `ConfigVersion` reads `3`; `FrostDownsample`,
`FrostUpdateEveryN`, `FxBloomRes`, `FxBloomBlurSteps`, `FxBloomFineDetail` are **absent** from
every profile's theme.

**G. Theme fold, travel.** Change the radial wheel size + a Grid override + a Control Center
colour → switch profile → switch back: all three return. Change one, switch away without
saving explicitly, switch back: the debounced autosave has stamped it (the same contract the
palette already has).

**H. Overlay palette.** Recolour `HudPanelBorder` and `WedgeStowTarget` in F9: a Toast, an item
radial's stow wedge and the `UnityRadialView` warning text all follow, live, without a reload.

**I. Hygiene.** `dotnet build "Dev/StationeersUIMod.Dev.csproj" -c Debug` → 0 warnings, 0 errors.
Double-F6 hot reload leaves no stale canvas, no orphaned borrowed vanilla object, and every new
static (`HudElementView.LayoutSlot`, `EditTargetSlot`, `GridTheme.Slot`,
`ConfigMigration.PendingThemeTopUp`) is reset in the relevant `Shutdown`/teardown path.

---

## 9. Risks and watch-points

1. **`LayoutBare` → `LayoutSlot` is a wide mechanical rename** (~80 call sites across
   `PrimitiveView` and all 16 widgets). One agent, one pass, compiler-verified. Keeping
   `LayoutBare` as a computed `bool` property preserves `HudSystem`/`HudWarp` call sites.
2. **Seeding must copy STORED values, not resolved ones.** Resolving would freeze
   palette-name refs into hex and `-1` sentinels into current globals, reintroducing the
   2026-07-17 "frozen Custom snapshots drift" bug. The `DescribeProps` round-trip in §3.6 gives
   stored values because the prop getters are the raw `Get*For` accessors — verify this per
   widget before trusting it, especially `ReadoutWidget` and `BareSensesWidget` whose getters
   sometimes pass a computed default.
3. **`SeedSlotFromBase` re-entrancy.** It mutates `EditTargetSlot` while ImGui is mid-frame;
   it must restore the previous value in a `finally`, and it must not run while a drag is
   active (`HudEditorMode.CommitActiveDrag()` first).
4. **Per-slot `styleSource`** means an element can be Global in one tier and Custom in another.
   `HudStyleMigration` only ever reads/writes the base — confirm it still returns "already
   coherent" for elements whose *base* is coherent and does not try to migrate a `b_styleSource`.
5. **Robot slot scope creep.** Robot is included because the model is prefix-driven and it is
   nearly free; if it costs real time, ship `ForkBare` only and leave `ForkRobot` reserved
   (the bit and the enum member stay, the checkbox is hidden). Do not ship a half-wired Robot.
6. **Grid `StyleHash` is polled, not evented.** Forget `Slot` in the hash and a tier flip leaves
   an open Grid on a stale skin — a silent, hard-to-notice failure. Test with the window open.
7. **`radial:` include-list drift.** A future rename in `UIAConfig` silently drops a knob from
   every theme. The warn-on-unresolved-name check in §5.2 is mandatory, not optional.
8. **Two migrations in one launch.** `ConfigMigration` v2→v3 (config) and `HudDocument` Schema
   14→15 (profiles) both fire on the same first launch, and the top-up writes every profile file
   while `Sanitize`'s `RepairedOnLoad` also wants a write-back. Order matters:
   `SyncShipped` → `TopUpAllThemes` → `LoadActive`/`Sanitize`/seed → single autosave. Back up
   the HudProfiles folder before the first manual test.
9. **`FlushPendingElementEdit` and the mode tabs.** Clicking a mode tab changes
   `HudSystem.ForceTier` mid-popup; the pending-undo bracket keys off `ElementStamp`, not the
   slot, so a value gesture spanning a tab click could commit to the wrong slot. Flush the
   pending edit on any slot change (`HudEditorWindow`), same as `elMoved` already does.
10. **XML size.** A fully seeded per-tier element roughly doubles its param count; a profile
    with per-tier on for a dozen elements plus ~300 theme entries approaches 120 KB. Still
    trivial, but keep `Set(key, null)` removal semantics so turning a fork off actually shrinks
    the file.
11. **MP safety / API surface: none.** Wave C touches no game state and adds no new game API
    dependency — everything here is mod-owned config, XML and UGUI. No decompile verification
    is required for this wave; note that explicitly in the Changes Report.
12. **TMP glyph rule.** Any new *displayed* HUD string stays ASCII (`HudText.Set`). The F9 and
    F10 editors are ImGui/UGUI-kit surfaces and are exempt, but keep the `*` fork marker ASCII
    anyway so it can be reused in a HUD-side label later.
