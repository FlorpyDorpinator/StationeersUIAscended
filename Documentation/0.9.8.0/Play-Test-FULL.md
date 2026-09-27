# 0.9.8.0 SmartStow overhaul — FULL play-test

*The thorough list. Do `Play-Test-SIMPLE.md` first — this one assumes it passed and digs into
edges, multiplayer and hot-reload. Sections are independent; do them across several sessions.
Deeper per-area checklists live in each build report (all dated 2026-09-25 in
`Changes Reports/`): Kit v2 + shell (agent K), Simple SmartStow data + capture (P0-P1),
Simple SmartStow resolver + executor (P2-P3), F10 SmartStow tab (agent O), UI Themes rename +
radial port (agent R).*

**Console diagnostics:** `stowhomes` (homes: world key, counts, your hands' entries with
provenance), `stowprofiles` (layouts + per-container routing verdicts), `uiareset` (full
config wipe, confirm + restart).

## A. Simple mode — homes (single-player)

- [ ] Fresh world: ~3s after spawn, `stowhomes` lists every carried item (src=snap), nothing
      for the hands.
- [ ] The sheets test: stack of sheets in backpack slot N. Take it, G → back to slot N even
      when ANOTHER sheets stack sits in a different bag. Take half (radial split), G → the
      half MERGES back onto the source stack. Drag the stack to a new bag → G now targets the
      new spot; the other stack's home is untouched.
- [ ] Ningy's case: mining belt INSIDE the backpack, drill's home = that belt. G with the
      drill in hand → into the nested belt (any search-depth setting).
- [ ] Occupied home slot: put something else into the home slot → G still lands the item in
      the same BAG (another slot).
- [ ] Home bag full → item goes elsewhere + "Home full - put in X" note; `stowhomes` shows the
      home UNCHANGED; empty the bag, G again → home.
- [ ] Drop the home bag on the ground → G falls back (note shows). Pick the bag up → G goes
      home again.
- [ ] Swap-take: hold item A (with a home), radial-take item B → A lands back in ITS home,
      not in B's old slot.
- [ ] Sorting a bag does NOT change which BAG anything calls home (slots may renumber).
- [ ] Vanilla-only landings never steal a home: G an item into a fallback bag twice, then
      drag it home by hand — from then on G uses the hand-placed spot.
- [ ] "Never stow into this" (set in Complex) is honoured in Simple: the excluded bag is
      never a G target even as a home (fallback + note instead).
- [ ] Return Home page: home count matches `stowhomes`; the in-hand readout names the bag,
      slot and "you put it" provenance; **Forget homes in this world** zeroes the count and G
      routes like new items (no instant re-snapshot); **Re-learn from what I carry now**
      rebuilds the count from your current arrangement.
- [ ] Tools: batteries pulled from a welder do NOT go "home" into the welder; unpacked
      supplies do NOT return into their packaging; body-bag contents never involved.

## B. Simple mode — multiplayer (client on a host/dedicated server)

- [ ] Join a server: `stowhomes` shows a world-GUID key (NOT "unsaved"); the file
      `BepInEx/config/StationeersUIMod/StowHomes/<guid>.xml` appears.
- [ ] One G press = one action on the client; a server-refused move leaves the item in hand.
- [ ] Split a stack as a CLIENT (radial split) → the child inherits the source's home
      (`stowhomes` src=split) and G returns it there.
- [ ] Leave, join a DIFFERENT server: homes do not leak across (fresh/other file).
- [ ] Rejoin the first server: homes persist.
- [ ] Old per-save data: your existing SP save's bag assignments, belt bindings, hotkeys,
      pins and collapse states all still present after the update (files were adopted to the
      new world-key names; the old files remain on disk untouched).

## C. Mode switching + migration

- [ ] Fresh install (or after `uiareset`): Mode = Simple; one-time notice on first world.
- [ ] Updated install that HAD bag-profile assignments: Mode = Complex + its own notice.
- [ ] Flip Simple↔Complex live in F10 with the inventory OPEN: profile chips/badges/capture
      strip appear only in Complex; the mode button vanishes from the grid header in Simple;
      a PINNED inventory window updates too.
- [ ] Complex is untouched by default: with Simple never enabled, G routes exactly as 0.9.7.4
      (profiles → affinity → defaults → memory...). Optional: Routing's "home first" toggle
      (More options) makes Complex try the home before profiles.
- [ ] ProfileSort: in Complex a profiled bag sorts profile-items-first; in Simple sorting is
      pure vanilla.

## D. The Organizer (Complex)

- [ ] Browse vs Use: clicking cards only browses (right column follows, banner on non-active);
      Use switches; mappings survive switching both ways (a bag mapped to a profile the new
      layout lacks shows the inert "(not in this layout)" row, nothing deleted).
- [ ] Edit an INACTIVE layout: add/rename/delete a profile + change rules → switch to it →
      the edits are there. Edit the ACTIVE one → G honours the change immediately.
- [ ] Rule editor at scale: browse By Printer → open a fabricator's "By item" group (100+
      rules) → the list scrolls INSIDE the capped band, stays smooth, the filter box narrows
      it; priorities/removes hit the RIGHT rule after scrolling.
- [ ] Copy to / Move to between layouts (kebab) AND drag a profile row onto a layout card
      (copy): name collisions get "(2)"; a failed source-removal on Move reports "exists in
      both".
- [ ] Layout CRUD: New (becomes "My Layout", de-collided), Rename, Duplicate (counts match),
      Delete (confirm; last layout refuses; deleting the active one switches).
- [ ] Bags: Capture opens the capture dialog OVER F10; toggling rows + AS ITEMS + SAVE
      creates/updates the profile and auto-assigns; card thumbnails show paint colours; #2
      ordinals on duplicate bags; manual rename via kebab (MP client: "appears once the
      server confirms it" wording).
- [ ] Typed-pack warning: assign a Materials-style profile to a mining backpack → a heads-up
      names the rules that can't fit; assignment still allowed.
- [ ] Test an item: the dry-run names the destination + stage tag (HOME in Simple, PROFILE
      etc. in Complex); nothing moves.
- [ ] Share: Export → clipboard + `StowProfiles/Export/` file + fingerprint; Import from
      clipboard AND from a pasted code; a hand-dropped `.xml` in `StowProfiles/` appears
      after Rescan; imported layouts always land INACTIVE under a fresh name.
- [ ] Maintenance: Restore shipped layouts (confirm) rebuilds the four shipped sets;
      player-made layouts untouched.
- [ ] Badges: distinct colours per profile, readable text, recolour when the UI theme changes.

## E. The new F10 shell + kit

- [ ] Window ~1450x950 at 1080p; still sane at 1440p/ultrawide/4:3 (clamps, no clipped
      columns); folder-tab seam looks clean on all four shipped themes (watch for a faint
      line under the selected tab on very transparent themes).
- [ ] Search: finds rows on every tab (each tab after first visit), including inside "More
      options" disclosures (it expands them), plus "HUD Designer (F9)" as an external.
- [ ] Text inputs everywhere: visible text + caret, Enter commits, Esc cancels WITHOUT
      closing F10; a half-typed name survives an F9 theme drag (restyle).
- [ ] Kebab menus + confirm buttons: outside click closes; destructive entries need the
      inline "Sure?"; armed confirms disarm when you leave the page; nothing survives
      closing F10 or a double-F6.
- [ ] Esc layering: with a kebab/dropdown/search popup open, Esc closes JUST the popup
      (window stays); the next Esc closes F10. With the capture dialog open, Esc works the
      dialog, never F10 underneath.
- [ ] Rename-Cancel is inert: open any Rename box (bag, layout, profile), type, click
      Cancel → NOTHING is applied and no bag rename is sent (watch the bag's name);
      Enter or OK are the only ways a rename lands.
- [ ] Dropdowns near the screen bottom flip upward; long enum lists (45 slot classes) scroll
      and search.
- [ ] Per-section "More options" remembers open/closed across sessions; last-open tab is
      remembered.
- [ ] F9 colour-wheel drag with F10 open on the Organizer: smooth (no disk-IO hitching),
      selections/drafts survive.

## F. Renames + ports

- [ ] "UI Themes" everywhere (tab, notes, F9 copy, console lines) — the word combination
      "HUD theme" appears nowhere player-visible.
- [ ] RadialTab: the ported wheel-colour swatches edit live (open a radial to watch), Undo/
      Redo/Reset work, one popup session = one undo step; the legacy editor button (More
      options) still opens the old window; its SmartStow+ section and "Open profile editor"
      are GONE.
- [ ] Guide reads correctly for both modes and says "SmartStow", not "Storage".

## G. Stability

- [ ] Double-F6 mid-everything: with F10 open on the Organizer, a drag in progress, a kebab
      open, capture dialog up → reload → no stale canvases, no duplicate observers
      (`stowhomes` counters don't double-count), everything rebuilds.
- [ ] Chute-heavy base idle: no new frame cost with the inventory closed (the Slot.Take
      observer's early-outs) — spot-check with `uiaprof` if available.
- [ ] Log hygiene: a normal session adds no warning spam; `stowhomes`/`stowprofiles` never
      throw at the main menu.
