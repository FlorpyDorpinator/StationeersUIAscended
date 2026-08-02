# The HUD Designer Handbook

### Stationeers UI Ascended

Your visor HUD is not a fixed picture. It is a document you own: a list of elements, each with a
place, a size, a colour and a look, saved as a plain file you can edit, break, fix and hand to a
friend. This handbook is about the F9 HUD Designer - how to move things, how to restyle them, how
to give your bare-headed self a different HUD from your suited self, and how to get back out of
trouble when a change goes somewhere you did not expect. Nothing here is destructive; everything
here is undoable.

---

## Opening things

- **F9** opens the **HUD Designer**. It is a floating window over the live HUD. Everything on the
  HUD stays running while it is open.
- **F10** opens the **Control Center** - the ordinary settings menu, with tabs for Profiles,
  Radial, HUD, Storage, Controls and Guide.
- Both keys are rebindable. All of UI Ascended's keys rebind under **F10 -> Controls**; the menu
  key additionally appears in the game's own Controls screen, under "UI Ascended".

## Where your HUD lives on disk

Every look you can save is one file:

```
<game install>/BepInEx/config/StationeersUIMod/HudProfiles/<name>.xml
```

Alongside it, optionally, `<name>.png` - the preview image the F10 Profiles cards show. That
folder also holds `.shipped-manifest`, which records what the mod last shipped so an update can
tell your edits from ours.

You do not have to go looking for it. **F9 -> Open folder** (next to the profile dropdown) opens
it for you, and **F10 -> Profiles -> Advanced -> Open profiles folder** does the same.

> Two curated themes ship in the box: **Stationeers Blue** (the fresh-install default) and
> **Pure HUD**. Both are fully editable, and both can be put back exactly as shipped in one
> click if you ever wreck them.

<!-- page -->

# 1. The mental model

Before you touch anything, three ideas. They explain almost every "why did it do that?" you will
hit later.

## A profile is a document of elements

Your HUD is not a screen with slots. It is a **flat list of elements**, in one XML file. Each
element carries:

- a **type** - Box, Label, Readout, Compass, Portrait, HandBoxes, EquipmentColumn, MoodletDashboard,
  VitalsPanel, DamageDoll, Polyline, Shape and so on;
- a **place** - one of nine anchors (TopLeft ... BottomRight) plus an X/Y offset from it, so a
  bottom-anchored element stays at the bottom on any screen;
- a **size** - Width and Height in pixels, or `Width %` / `Height %` for a proportion of the screen
  (`-1` on either means "use the fixed pixel value instead");
- a **Z order** - who draws in front of whom;
- **Show in (Bare / Suited)** - which suit states this element exists in at all;
- **colours** - each one either a palette entry NAME or a literal `#RRGGBBAA`;
- **per-corner radii**, and a bag of type-specific settings (a readout's source, a compass's field
  of view, a doll's damage colours...).

Add an element, move it, delete it, draw a new one. There is no fixed inventory of slots to fill.

## The theme travels WITH the profile

A profile does not just store where things are. It stores a **theme**: a snapshot of the complete
global look, re-applied whenever you switch to that profile. That includes:

- the whole HUD colour palette and every global effect setting;
- the **radial menus** - wheel size, colours, glass effects, and the key-hint bar's look;
- the **Universal Inventory** skin;
- the **F10 Control Center** skin.

Switch profiles and the entire mod retints as one piece. This was a real bug once: recolouring the
globals for one profile silently recoloured every other profile that referenced a palette slot.
Themes exist so that cannot happen again.

## Performance knobs deliberately stay on your machine

Five settings never travel with a theme: the frosted-backdrop **blur resolution**, its **re-blur
every N frames**, the **bloom glow resolution**, and the **bloom blur step** counts. These trade
frame time on *your* hardware. Importing a friend's beautiful theme must never quietly halve your
framerate. Wherever one of them appears, it is tagged **`[machine-local]`** so you know.

A profile with **no** saved theme is legal and useful: it means "follow whatever the globals
currently are". A theme is stamped into a profile only when you edit a **global** - nudging an
element never restamps it.

<!-- page -->

# 2. Your first edits

Press **F9**. The window that opens has a permanent toolbar at the top and five tabs: **Build**,
**Theme**, **Effects**, **View & Behavior**, **Diagnostics**.

The toolbar carries, in order: the title, a save indicator that reads `PROFILE - Saved` or
`PROFILE - Saving...`, your active profile dropdown with an **Open folder** button, the profile
management row, a **Preview** dropdown, and **Undo / Redo / Exit**.

Everything autosaves. After about a second and a half of quiet the indicator flips back to
`Saved`. There is no save button and you do not need one.

## Select, move, resize

**Click any element on the HUD.** It gets a selection outline and a popup opens titled
`Edit: <type>`. That popup is the whole context for that one element - we tour it next page.

- **Drag** the element to move it. **Drag a corner** to resize it.
- **Ctrl+drag** on empty HUD space draws a box-select marquee: everything inside is selected and
  moves together.
- **Arrow keys** nudge the selection 1 px per press. **Shift + arrow** nudges one grid cell.

## Snapping

On the **Build** tab, under `CANVAS & GRID`:

- **Snap to grid** - on by default. **Hold Alt while dragging to bypass it** for one move.
- **Show grid** - draws the grid so you can see what you are snapping to.
- **Grid size (px)** - 2 to 64.

## Undo, duplicate, delete

- **Ctrl+Z** undo, **Ctrl+Y** redo. The toolbar's Undo/Redo buttons do the same thing. Undo history
  survives a profile *rename*.
- **Ctrl+D** duplicates the selected element. So does the **Duplicate** button in its popup.
- **Delete** removes it. So does the **Delete** button in its popup.

One gesture is one undo step. Dragging a slider and then clicking a different page still undoes as
a single step, not two.

> These shortcuts deliberately do nothing while a text field in the designer has the keyboard -
> Delete there erases a character, not your compass.

## Adding something new

Build tab, `CREATE & EDIT GEOMETRY`: pick a type from the dropdown and press **Add element**. It
appears near the middle of the screen, selected, ready to drag. If you delete something and want it
back, this is how - every widget type is in that list, including the ones that came with a shipped
theme.

The number keys 1-6 are swallowed while the designer is open, so typing a size will not also swap
your helmet.

<!-- page -->

# 3. The element popup: the shape of it

Click the compass. The popup that opens is titled `Edit: Compass`, and it has the same skeleton
for every element in the mod.

## The header (only on "Both" elements)

If the element exists in both bare and suited states, the popup starts with:

- a **Separate BARE style** checkbox (the *Per-tier styles* chapter explains it in full);
- a two-button **Editing mode** switch: **SUITED** / **BARE**;
- a line telling you exactly where your next edit will land - for example
  *"Look values write the SHARED base (bare inherits it)."*

Read that line. It is the single best defence against "I changed it and the wrong mode moved".

## The tab bar

Under the header: **Content**, **Layout**, **Appearance**, **Effects**, **Interaction**. A tab only
appears if this element actually has rows in it - a drawn line has no Content tab, a plain label
has no Interaction tab.

- **Content** - what the element *is*. A readout's data source, a label's text, an icon's file
  name, a chip's caption, how many rows a stacked readout shows.
- **Layout** - `Anchor`, `X`, `Y`, `Width`, `Height`, `Width %` / `Height %` (`-1` = fixed),
  `Z order`, and `Show in (Bare / Suited)`.
- **Appearance** - the colour rows. `Text / accent`, `Fill`, `Border` (or `Ring colour` on the
  portrait), plus the item-icon tint where the widget has one. Colours are deliberately outside the
  follow system: they are always yours to set, in every state.
- **Effects** - the style pages. This is the big one; the next chapter is entirely about it.
- **Interaction** - the handful of elements that respond to the mouse.

## The footer

Under the tabs:

- **Edit points (drag / add / delete / curve)** - on drawn lines and pen shapes only.
- **Duplicate** and **Delete**.
- **Make flat (strip optical effects)** on anything with a panel: it takes ownership of the look
  you can currently see, then switches off glass, glow and every optical layer. Corners, line
  thickness, trapezoid insets and box-end fades survive - the silhouette is not what "flat" means.

## A note on tabs and hidden knobs

Controls that cannot do anything on this element are **not shown**. A compass has no `Text /
accent` row because it does not use one; a portrait has no `Font scale`. That is deliberate: an
inert knob that looks live is worse than an absent one. Controls that are *momentarily* inert are
handled differently - they stay visible with a reason underneath. See page 4.

<!-- page -->

# 4. The style pages, and who owns a value

Open an element's **Effects** tab. Inside it is a second row of pages: **Theme**, **Glass**,
**Edges**, **Glow**, **Bloom**, **Alerts**, **Transitions**.

Those pages are the same families as F9's own Effects and Theme sub-tabs, in the same order, with
the same captions and the same slider ranges. Whatever you learn in one menu transfers to the other.

## The master row

Above the page bar sits one master checkbox for the whole element.

- **Ticked** - every family follows the F9 globals.
- **Unticked** - every family is this element's own.
- **In between** it does not lie in either direction. It prints:
  `(mixed) - some families follow the globals, some do not.`

Clicking it drives all five follow-able families at once, power transitions included.

## Per-family control

Each of **Theme / Glass / Edges / Glow** is headed by a dropdown:

```
Style source: Glass   [ Follow F9 globals | Inherit from another element | Own values ]
```

**Transitions** keeps a plain checkbox - `Follow global Transitions` - because motion is shared by
every suit tier and cannot be inherited from another element.

**Bloom** and **Alerts** have no source control at all. Bloom is one full-screen pass; Alerts is one
set of shared uniforms. There is nothing per-element there to own, so those pages are read-only.

## The golden rule of unfollowing

**Switching a family from "Follow F9 globals" to "Own values" must not change a single pixel.**
Unticking copies down exactly what is on screen at that moment and then hands you the sliders.

Re-ticking sends the family back to the globals and leaves your values dormant on disk. Untick a
second time and it re-seeds from **what is on screen now**, not from your older custom numbers.
That is intentional: the old behaviour - resurrecting a snapshot frozen at some unrelated past
moment - was the bug, not the feature.

## The four kinds of row you will see

1. **Editable rows.** Yours, while the family is Own.
2. **Shared read-only rows.** Physically global - one light angle, one frost tint, one bloom pass
   for the whole HUD. They render **in every state**, showing their live value, captioned
   `shared global - edits affect EVERY panel`, with an `Open F9 > Effects > ...` button whose text
   IS the path. Machine-local ones add `[machine-local]`.
3. **Per-element-only rows.** No global exists - box-end fades, trapezoid insets, the element's own
   font scale multiplier. These stay editable in every state: they are this element's geometry, not
   the theme's.
4. **Inert rows.** Visible, with a reason: *"sharp glass panels are OFF"*, *"a freeform pen shape
   renders the mesh halo, not the analytic panel"*, *"this element's frost resolves to 0"*. They
   used to vanish silently, which taught nobody anything.

<!-- page -->

# 5. Per-tier styles: bare and suited

Your HUD looks different when your suit has no power - fewer readouts, a plainer style. That state
is called **BARE**; the powered state is **SUITED**. (A third slot, **Robot**, exists in the file
format and is reserved; nothing in the designer creates one yet.)

Two independent things can differ between the two.

## The layout fork (implicit)

On an element whose `Show in` is set to both, pick the **BARE** editing-mode button and then drag
it. The bare position forks automatically, seeded from the suited one, and from then on the two
modes remember their own place, size and anchor. Pick **SUITED** and drag: the suited position
moves and bare keeps its own.

This has always worked and needs no opt-in.

## The style fork (opt-in)

Colours, glass, glow, corners, icon tint - the *look* - are shared by default. One element, one
look, both tiers. To split them, tick:

```
[x] Separate BARE style
    Bare keeps its OWN copy of every look value.
```

The moment you tick it, bare receives a **complete** copy of everything the element currently
resolves - not a sparse patch. That completeness is the whole point. The old partial fork is why a
long-standing bug existed where changing five hand-box colours in bare mode made only two of them
switch back: the other three had no bare twin, so a later suited edit wrote into the value bare was
inheriting.

Two guarantees follow from it:

- **Nothing is left implicitly shared**, so nothing can leak later.
- **Copy-on-write**: if you later edit the shared base for a key the fork never overrode, the fork
  is first handed the value it was actually resolving. Your bare design does not drift because you
  touched suited.

Untick the box and bare goes back to inheriting suited; the forked copies are dropped and the file
shrinks again. Undo covers it.

## What forks, and what does not

**Look forks.** Colours, fills, borders, glass, glow, edge energy, corner style, icon tint, a
compass's field of view, a doll's ramp colours, row heights, horizontal-vs-vertical.

**Content does not.** The readout's data source, a label's text, icon file names, `Z order`,
`Show in`, a shape's point list, chip captions, sense order. Changing what something *is* changes
it in both tiers, which is what you want.

**Power transitions do not.** They are deliberately shared by every tier - a fork there would let
your suited and bare layouts disagree about whether an element dies at all.

The per-family follow state forks too. A bare fork can follow the global Glass while the suited base
owns it.

<!-- page -->

# 6. Inherit from another element, and one-shot copy

You have styled one box exactly right. Now you want five more like it - and you want them to keep
matching when you tune the first one.

## Inheriting (a live link)

On the follower's **Theme / Glass / Edges / Glow** page, set `Style source` to
**Inherit from another element**. The page switches to picking mode and says so:

```
PICKING: click the element to inherit Glass from. Esc / right-click cancels.
```

Now click an element on the HUD. The hover outline turns **green** on a legal donor and **red** on
one you cannot use, with a caption riding the cursor. Escape or a right-click cancels - and because
nothing was written yet, there is nothing to undo.

If the popup is covering the element you want, use the **Donor element** dropdown on the same page.
It reaches elements the click cannot, including ones hidden by the tier you are previewing, and
labels each by type and short id (`Box 4f3a1c9e`).

Once linked:

- The follower's rows go **read-only**, showing the values resolved through the donor.
- Editing the **donor** ripples to every follower on the next frame. That is the entire point.
- **Each family picks its own donor.** Glass from A and Glow from C on one element is supported.
- **Bare follows bare.** If the donor has its own bare fork, the follower's bare takes the donor's
  bare values.

## The depth rule

**One level only.** You may inherit from an element that owns its style, or from one that follows
the globals. You may **not** inherit from an element that is itself inheriting that family. The pick
gesture rejects it (red) and the dropdown omits it.

If a donor becomes illegal later - because you re-pointed *it* afterwards - the follower quietly
drops that family to the globals and says so on its page. It never lands on a blank look.

## Breaking the link

- **Detach to own (keep these values)** freezes the donor's current values as your own. Not one
  pixel changes; the rows become editable; editing the donor no longer moves you.
- **Delete the donor** and the follower falls back to the globals the same frame. On the next load
  the dangling reference is cleared with a single log line.
- Setting the source back to **Follow F9 globals** leaves the reference dormant, ready if you
  switch back.

## One-shot copy (no link)

On a page that is set to **Own values** there is a button:

```
[ Copy Glass from another element... ]
```

Same pick gesture, no depth rule, no stored link. It copies the values across once and then the two
elements are free to diverge. This is the right tool for "make this look like that, then change it";
inheriting is the right tool for "keep these six in step forever".

<!-- page -->

# 7. Colours and palettes

Colours are handled palette-first. Retune nine wheels and the whole HUD moves together; drop to a
literal hex only where you want something to stand apart.

## F9 -> Theme

Three sub-tabs:

- **Palette** - the nine core wheels: panel fill, panel border, line accent, text label, text value,
  text dim, good, warn, critical. Ninety percent of a recolour happens here.
- **All colours** - the complete list, plus **Reset all HUD colours to defaults**.
- **Typography & boxes** - font, HUD font scale, label size; overall HUD scale and
  scale-with-resolution; corner rounding and corner style; default line thickness, edge softness,
  glass sheen and glass edge light; the sharp-glass-panel master; and three
  **apply to every element** buttons.

Both colour sub-tabs share one Undo/Redo row, so a wheel dragged on **Palette** undoes from **All
colours** and vice versa.

## Per-element colour references

Every element colour row takes a **colour reference**, which is one of two things:

- a **palette entry name** - for example `HudPanelBorder` or `HudTextValue`. The element keeps
  tracking that palette slot, so retuning the palette moves it;
- a **literal** `#RRGGBBAA` - the element stands alone and ignores the palette.

Leaving a colour row **empty** means "use this element's documented default", which is usually the
palette slot that fits it. That is different from typing a hex that happens to match: an empty row
keeps following, a hex row does not.

Pick a palette name when you want the element to belong to the scheme. Pick a hex when you want a
deliberate accent - a warning box, a signature stripe - that must not move when you retune.

## Conventions worth keeping

- `Text / accent` is the element's identity colour. Several widgets derive other tints from it, so
  a doll's healthy tint or a chip's rim can follow it for free.
- `Fill` and `Border` are the plate. A sub-pixel border width is legal and looks like a hairline -
  both shipped themes use one.
- The **item icon tint** is a tri-state per element (follow the global switch / off / on with its
  own colour), and it forks per tier like any other look value.

## Colours are never follow-gated

Colour rows and the icon tint stay editable whether the element follows the globals or owns its
style. You never have to unfollow an element just to recolour it.

Any edit to a **global** - a palette wheel, a global effect slider, a radial colour - marks the
active profile's theme dirty, and the next autosave stamps it into that profile.

<!-- page -->

# 8. What each effect family actually does

Plain descriptions, in the order the pages appear.

## Three masters gate everything

- **Core effects - surfaces, edges and glow.** Needs no shader bundle at all. Off means flat plates:
  no rim, no halo, no edge energy. It also gates the whole **Edges** and **Glow** pages.
- **Animated glass - shine, iridescence, colour fringe.** The bundle-shader glass animation.
- **Frosted backdrop - blur the world behind the HUD.** The priciest effect here, and available on
  flat and vertex-warp curvature only.

## Glass

The surface of a plate. **Hairlines** keeps sub-pixel lines fading instead of dropping out.
**Border fade** lets unlit sections of the outline dissolve. **Soft edge** feathers the rim.
**Box end fade** melts the ends of a box into the visor. **Shine** sweeps a bright band across it,
**iridescence** shifts its hue with angle, and **chromatic fringe** splits colour at the edges -
that last one is the frosted backdrop sampled at an offset, so **it needs frost above zero on that
element** or it does nothing, and the row says so when it is inactive. **Frost strength** and
**blur depth** control the blur itself; backdrop darkening and tint are shared by every panel.

## Edges

Light running along the outline. A strength, the light **colour**, the **angle** it comes from, how
much the opposing rim catches, and the **falloff**. Then the animated part: **irregular energy** and
its **frequency**, **flow speed**, a **desync** so not every box pulses in lockstep, and the
**flowing edge aura**.

## Glow

The halo band outside the plate. Outward and inward strength, **radius**, **spread** and extra
diffuse, plus the atmospheric **haze**, **breathing**, and **uneven / organic reach** that stop it
looking like a uniform blur.

## Bloom

A **full-screen post pass**, not a per-element effect. Base glow with its threshold and tint, a
breathing band, a state-reactive band, and a border/highlight band. Changing anything here changes
the whole HUD by design - that is why the bloom page in an element popup is read-only.

## Alerts

The suit-warning pulse: how long a breath lasts, how many flashes a caution gets, the pulse
strength, and the caution/critical brightnesses and hues. Deliberately **not** gated by the core
effects master - a warning must still reach you on a flat HUD.

## Transitions

Seven power-state motions, per element: **Death collapse**, **TV off**, **Dissolve frontier**,
**Flicker**, **Glitch tear**, **Warp / visor curve** and **Breathing pulse**. Each is a checkbox
plus a strength slider. A row you never touch keeps tracking the global, live - the checkbox and
slider simply display the current global until the moment you touch that row.

## The machine-local four

Blur resolution, re-blur every N frames, bloom glow resolution and bloom blur steps trade frame time
for quality. They are marked `[machine-local]` and never ride a theme.

<!-- page -->

# 9. Corner styles: Rounded and Cut

Corners have two independent controls: a **style** and a **depth**.

## The style

**F9 -> Theme -> Typography & boxes**, next to the rounding slider, is a `Corner style` combo with
two options: **Rounded** and **Cut**. Cut gives you a chamfer - the corner sliced flat at 45
degrees, the angular look a lot of sci-fi HUDs use.

Geometrically the chamfer is the rounded corner's arc collapsed to its chord, which is why
everything downstream just works: the border band follows the cut with a proper mitre join and no
notch, the soft-edge skirt and the glow halo wrap the chamfer's end points, trapezoid slants
chamfer cleanly, and the visor curvature bends a cut panel like any other.

## The depth

The **corner rounding** slider now sets how deep the round *or the cut* bites. Zero is a square
corner in either style. Per-corner radii still work, so you can cut the top two corners and leave
the bottom two square - asymmetric angular panels are one slider each.

## Per element

In an element's **Theme** style page, next to the four per-corner radii, is a tri-state
`Corner style`: **Follow global**, **Rounded**, **Cut**. It is per-tier forkable like any other look
value, so a box can be rounded in your suit and cut when bare.

"Follow global" is stored as a live-tracking sentinel, not as a frozen copy of the current global -
so an element on Follow keeps tracking the F9 combo even after you unfollow its Theme family.

## The restart note

Cut corners are drawn exactly by the sharp glass panel shader, which lives in the mod's effects
bundle. **Asset bundles do not hot-reload.** After a mod update that ships a new bundle you must
**fully restart the game** - a hot reload delivers the new code but leaves the old bundle resident.

Check it: **F9 -> Effects -> Advanced**, renderer status. It should read `READY (ABI 3)`. On an
older bundle a cut panel falls back to the mesh renderer: it keeps its shape and its mesh-path
effects but loses the analytic extras, and the menus say exactly that where it matters, rather than
leaving you to wonder why the halo looks different.

Flipping back to Rounded restores the analytic path for those panels immediately, no restart needed.

<!-- page -->

# 10. Shapes, lines and custom elements

Beyond the built-in widgets you can draw your own geometry - decorative frames, bracket lines,
filled glass panes in whatever silhouette you like. They are first-class elements: they take edge
energy, halos and the whole style system.

## Draw a line

Build tab -> **Draw a line (click points on screen)**. Then:

- **Left-click** lays each point.
- **Enter** or **right-click** finishes the line.
- **Escape** cancels the whole thing.

A line needs at least two points. It becomes a `Polyline` element centred on what you drew.

## Draw a filled shape (the pen)

Build tab -> **Draw a shape (pen: click points, close for a filled glass shape)**. Then:

- **Left-click** lays each point.
- **Click the first dot again** (once you have three or more) to close the path - the same gesture
  as Illustrator's pen.
- **Enter** or **right-click** also closes it.
- **Escape** cancels.

A filled shape needs at least three points. It renders as real glass, with fill, border, halo and
energy.

## Editing points afterwards

Select the line or shape and press **Edit points (drag / add / delete / curve)** in its popup. While
that mode is on, an orange hint line in the popup reminds you of the gestures:

- **Drag** an anchor to move it. Snapping applies; **hold Alt** to bypass it.
- **Alt+click** an anchor deletes it.
- **Click anywhere else** adds an anchor, spliced into the nearest segment.

On a Bezier shape you also get:

- **Pull a segment** to bow it into a curve; pull it back onto the line to straighten it.
- **Ctrl+click a segment** makes that segment straight again.
- **Ctrl+click an anchor** toggles it between a corner and a smooth point.
- **Alt+drag a handle** breaks the mirror and gives you a cusp with independent tangents.

Press **Done editing points** when you are finished.

## Portrait frame shapes

The live portrait is not stuck as a disc. In its **Layout** group there is a `Shape` picker:
**Round** (the default), **Triangle**, **Trapezoid**, **Square**, **Rectangle**. The trapezoid adds
**Top width** and **Bottom width** sliders, each a fraction of the element's width, so any
symmetric trapezoid is reachable.

The hologram is clipped to the silhouette you pick and the ring frames it. The angular frames are
drawn as full glass, so they carry edge light, border fade and halo just like a box does - the
round ring is a simpler renderer and honours slightly less. The shape forks per tier like any other
look value.

<!-- page -->

# 11. Profiles in practice

Everything you do lives in the **active profile**. Managing them is the F9 profile row, right under
the toolbar's save indicator.

```
Active profile  [ Stationeers Blue        v ]  [ Open folder ]
[ New ] [ Duplicate ] [ Rename ] [ Delete ]  [ Restore shipped version ]
```

Pressing a button **arms one flow**: its name field or its confirm strip appears underneath, and a
failure prints an amber notice line instead of failing silently. Only one flow is armed at a time.
Drag the window narrow and the four buttons stack rather than running off the edge.

- **New** - a blank slate. Your current screen size is stamped in, you get exactly one hand-boxes
  element, and **no saved theme** - so it opens looking like whatever your globals are right now.
  Build up from there.
- **Duplicate** - copies the **live** document, unsaved edits included, and switches you to the
  copy.
- **Rename** - moves the file and its preview image, re-points the active-profile setting, and
  **keeps your undo history**. It refuses a name that already exists, and it cannot re-case a name
  (`pure hud` -> `Pure HUD`); do that in the file system if you need it.
- **Delete** - never the **active** profile; the store refuses it even if a menu somehow offered it.
  The button dims rather than disappearing, and points you at the reason. Switch away first.
- **Restore shipped version** - appears only when the active profile carries one of our names. Two
  steps, and the HUD snaps back to the shipped look immediately without a restart.

## The same jobs from F10

**F10 -> Profiles** is the friendlier front door:

- **cards** for the shipped themes, with their preview art, click to apply;
- an **Active profile** dropdown listing everything;
- **Font (this profile)** - a per-profile font override, or inherit the global;
- under **Advanced**: **New blank profile** (auto-named, because a card layout has no text field),
  **Duplicate active**, **Open HUD Designer (F9)**, **Open profiles folder**, and a
  **Manage a profile** block where you pick any profile and **Delete** or **Restore shipped** it -
  each a two-step confirm.

Naming and renaming live in F9. F10 auto-names and points you there.

## The save indicator

`PROFILE - Saving...` means an autosave is pending; `PROFILE - Saved` means it landed. Before you
alt-tab away to copy a file, let it say Saved - a debounced autosave landing a second later would
otherwise overwrite what you just did on disk.

<!-- page -->

# 12. Sharing a profile, and how updates treat yours

## Sharing

A profile is one file, and it carries **everything**: the layout, the per-element styles, the
per-tier forks, and the theme (HUD palette, effects, radial look, Universal Inventory skin, F10
skin). To give a friend your HUD:

1. Let the save indicator read `PROFILE - Saved`.
2. Copy `<name>.xml` out of `BepInEx/config/StationeersUIMod/HudProfiles/`.
3. Optionally copy `<name>.png` too - that is the preview image the F10 cards show. The shipped
   previews are 800x450.
4. They drop both into the same folder and pick it from the dropdown.

Two things worth knowing before you hand it over:

- **Stamp your resolution.** F9 -> Theme -> Typography & boxes -> **Stamp THIS profile as designed
  at my resolution**. It saves your screen size into the profile so someone on a different monitor
  gets your proportions rather than your pixel offsets.
- **The five machine-local performance knobs never travel.** Their blur and bloom resolution stay
  theirs. That is a feature, not a gap.

A theme file authored on an old build may simply not contain some of the newer families. Nothing
breaks: an absent key leaves that setting untouched, so the radial or menu look stays wherever it
was rather than snapping to a default.

## How a mod update treats your profiles

The config folder survives every update, which is exactly why the mod keeps a
**`.shipped-manifest`** beside your profiles recording what it last shipped. On each launch it
compares, and does one of three things - none of which ever touches a profile you made or edited:

- **Seed** - a shipped theme you do not have gets copied in.
- **Refresh** - a shipped theme you **never edited** gets replaced with the improved version. This
  is how our fixes actually reach you.
- **Prune** - a theme we have retired, and you never edited, is removed.

"Never edited" is measured on content, not timestamps or bytes, so the mod's own tidy-up rewrite
when it first opens a file does not read as an edit.

Consequences worth remembering:

- **Edit a shipped theme and it is yours.** You will stop receiving updates to it, which is correct -
  we are not overwriting your work.
- **Rename a shipped theme and it is unambiguously yours**, and a pristine original comes back on the
  next launch. The designer warns you about this *before* you click.
- **Deleting a shipped theme IS a restore path.** It comes back pristine next launch. That is the
  route that works even when everything else is unreachable.

<!-- page -->

# 13. Theming the inventory, the radials and the menu

The HUD is not the only surface the profile theme covers. Three more retint with it.

## The Universal Inventory

With the designer open, press **B**. The Universal Inventory appears as a **non-interactive edit
preview** - you cannot move items in it while F9 is up, which is the point. Click the window and you
get a popup titled `Edit: Universal Inventory`.

At the top:

```
[x] Follow the HUD's global box theme
    Colours + glass + effects follow F9's global tabs.
    Uncheck above for the full override block.
```

Unchecked, you get the whole thing as pages: **Colours**, **Glass**, **Glow & energy**,
**Frost & anim**, plus a **Sizes** page and a **Per tier** page. Two conventions apply throughout:
name a palette entry or type `#RRGGBBAA` in a colour row, and **`-1` on a slider means "follow the
global value"**. Whatever you set here styles the **whole family** - the main window and every
pinned window.

**The scroll bar** has its own section, shown in *both* follow and override mode so you can dial it
in without unfollowing:

- **width** - 2 to 16 px, default 4;
- **colour** - a palette name, a hex, or **empty to follow the window border**;
- **opacity** - default 0.55 (the faint track behind it renders much lower).

It only appears when the list actually overflows, its thumb length tracks how much content there is,
and you can drag it.

**Per tier** gives the window its own bare skin - separate fill, border, text, opacity and frost -
under the same **Separate BARE style** opt-in you know from elements. It repaints an already-open
window, pinned windows included, the moment your suit power flips. Deliberately narrower than an
element's fork: the real ask is "the inventory should not look powered when my suit isn't".

## The radial menus

**F10 -> Radial** carries the size and readout settings - wheel radius, hub radius, hub text sizes,
icon ratio, border width, edge feather, shine, wedge gap. At the bottom of that tab,
**Open the radial editor** gets you the wheel colours, glass effects, the full radial palette and
the key-hint bar's styling.

All of that **look** travels with your profile. The **behaviour** siblings sitting in the same
menus deliberately do not: how many wedges a wheel shows, whether the hint bar is on at all, hint
fading, and the wedge sounds. Trying somebody's theme must never change what the mod *does*.

## The F10 menu itself

Open F10 while F9 is up and click the menu: you get `Edit: Control Center menu`. It follows the
HUD's glass by default, with the same override model. It rides the theme too, so a profile switch
retints the settings menu along with everything else.

<!-- page -->

# 14. Recovery and maintenance

Nothing you can do in the designer is unrecoverable. Here is every ladder rung, cheapest first.

## Undo

**Ctrl+Z**. It covers element edits, deletes, duplicates, donor picks, detaches and colour drags,
one step per gesture. It survives a profile **rename**. Switching to a different profile clears your
selection - element identities do not carry across documents - so pick things up again with a click.

## "I wrecked a shipped theme"

Any of these, in increasing bluntness:

- **F9 -> Restore shipped version** (two steps). The HUD snaps back immediately; no restart.
- **F10 -> Profiles -> Advanced -> Manage a profile -> Restore shipped** (two steps, with a toast
  either way).
- **F10 -> HUD -> Advanced -> Maintenance -> Restore shipped themes**. Click twice. It puts
  **both** Stationeers Blue and Pure HUD back exactly as shipped and touches nothing else - your own
  profiles are never involved.
- **Delete the file and relaunch.** A pristine copy is seeded back.

If the mod's installed folder is not reachable, these buttons explain themselves rather than
offering a commit that cannot complete - and the "restore both" path refuses to delete anything it
cannot put back.

## "My profile file is corrupt"

If a profile exists on disk but will not parse, the mod **does not overwrite it**. It copies it to
`<name>.broken.xml` (then `.broken-2`, and so on) with a warning line naming the backup, and only
then regenerates. Quarantined `.broken` files never appear in the profile dropdown, so they cannot
be picked by accident, and your original text is still there to rescue by hand.

## "I want a genuinely clean slate"

Deleting the `.cfg` is **not** a fresh install. Your profiles, grid layouts, belt bindings, pins and
hotkeys live in a whole folder next to it. Use the console:

- **`uiareset`** - prints exactly what it *would* delete and touches nothing.
- **`uiareset confirm`**, typed in the **same session**, actually deletes it. Restart afterwards.

## The diagnostic: `hudfx`

Read-only, and the fastest way to answer "where is this value coming from?".

- **`hudfx`** lists every element in the active profile with its five source letters
  (`Surface / Glass / Edges / Glow / Transitions`), plus the registry row counts.
- **`hudfx <first characters of an id>`** dumps that element: which style slot is rendering, whether
  it is forked, the per-category sources, any donor links, and then **every** style row - its key,
  where the value comes from (`own`, `own(=global)`, `global`, `donor`, `shared`), the resolved
  value and the live global.

## Hot reload versus a full restart

Code reloads. **Asset bundles do not.** After a mod update, if effects look wrong or a Cut corner
falls back to the plainer renderer, restart the game fully and confirm
**F9 -> Effects -> Advanced** reports the renderer ready.

<!-- page -->

# 15. Gotchas worth knowing before they bite you

**Type ASCII only.** The game's font reliably renders plain Latin text. Arrows, box-drawing
characters, checkmarks, bullets and degree signs come out as empty tofu boxes in any label you
author. Use `+ - X > v ^` and you will never be surprised. This applies to text you type into a
label or a chip caption - not to anything in this handbook.

**Custom profiles never auto-upgrade.** Schema fixes and improved shipped art reach the *shipped*
themes automatically. A profile you made or renamed is yours, permanently, and is never rewritten
behind your back. That is the right trade - but it means an old hand-made profile can carry an old
mistake forever. The classic one: an element whose `Show in` was set wrong, so something suit-only
appears while bare. If a tier bug will not reproduce on a shipped theme, look at the element's
`Show in` first, then at whether it needs a proper **Separate BARE style** fork.

**The designer previews SUITED when you are unpowered.** Opening F9 while you have no powered suit
would otherwise render the bare HUD - minimal style, halos off - which is not what you meant to
edit. So the preview defaults to SUITED at that moment. Use the **Preview** dropdown to choose
deliberately: **BARE** to author the bare look, **Live** to follow what you are actually wearing.
Low-power dropout flicker is also suppressed while the editor is open, so your canvas does not blink
from your real battery.

**Curvature is applied once.** The visor curve is a mesh warp applied at render time, and the
editor's handles already account for it. Do not try to pre-bend an element's placement to
"compensate" - that double-warps it and desyncs the handles from what you see.

**Re-following then unfollowing re-seeds from the screen.** Untick a follow box, change your mind,
re-tick, then untick again: you land on what is currently on screen, not on the numbers you had the
first time. Your old values are not deleted, they are simply overwritten by the fresh copy-down.

**"Flatten ALL boxes" writes to the suited base.** If an element has a bare fork, flatten from the
**BARE** editing tab to flatten bare too, or untick the fork first.

**The bulk buttons leave motion alone.** `ALL follow Theme + Effects globals` and
`Snapshot ALL as Custom` rewrite the four steady-state families across every element but preserve
each element's own transitions setting - a bulk *style* operation should not silently re-enable a
death collapse you deliberately turned off across a dozen elements. The per-element master checkbox
is the opposite: one explicit, visible action on one element, so it takes motion with it.

<!-- page -->

# 16. Where to go from here

## The Guide tab

**F10 -> Guide** is the live reference for everything outside the designer: the two halves of the
mod, how the radial wheels open and behave, how Smart Stow routes an item, and a key list that reads
your **current** bindings rather than the defaults. If you rebind something, that list updates.

The Guide opens itself once, on your first session. To see the first-run walkthrough again, type
**`uiatutorial`** in the console at any time.

## Rebinding

**F10 -> Controls** rebinds every one of UI Ascended's keys: click the key glyph and press the key
you want (Escape cancels; mouse buttons cannot be captured for these). The menu key also appears in
the game's own Controls screen under "UI Ascended". Vanilla keys the mod piggybacks - Smart Stow,
inventory select, swap hands, the mouse-control modifier, the equipment digits - are rebound in the
game's own Controls screen, and the mod reads them live.

## A suggested first project

1. **Duplicate** Stationeers Blue so you are never editing the shipped copy.
2. Rename the duplicate to something of yours.
3. Retune the nine wheels on **Theme -> Palette**. Watch the radial menus and the F10 menu follow.
4. Pick one box. Untick **Follow global Glass**, push its frost up, and confirm the rest of the HUD
   did not move.
5. Point two more boxes at it with **Inherit from another element -> Glass**, then drag the first
   one's frost again and watch all three track.
6. Tick **Separate BARE style** on your hand boxes, switch the editing mode to **BARE**, and give
   your unpowered self a plainer look.
7. **Stamp THIS profile as designed at my resolution**, let it save, and copy the XML somewhere
   safe.

If step 4 moves a pixel, that is a bug worth reporting - unfollowing is supposed to be invisible.

## Reporting something

Include three things and any problem becomes reproducible in minutes:

- your **profile XML** (from the HudProfiles folder);
- the output of **`hudfx <id>`** for the element that is misbehaving;
- whether it happens on a **shipped** theme too, or only on your own.

Post it on the mod's Workshop page. Themes are welcome there as well - a profile is one small file,
and the whole point of the document HUD is that yours can be somebody else's starting point.

---

*Stationeers UI Ascended - FlorpyDorp + JXSN.*
