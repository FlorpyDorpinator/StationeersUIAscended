using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// The Complex-mode ORGANIZER (mockup 3, `smartstow-organizer-3.png`, with Part D's ten
    /// build-time fixes): three columns — Storage Layouts | Bags | Bag Profiles — over a
    /// full-width EDITING band and the SHARE bar.
    ///
    /// <para>Player-facing terminology: "Storage Layout" / "layout" is what code, XML and the
    /// StowProfiles/ folder call a Stow Profile; "Bag Profile" stays; nothing here is ever called
    /// "the Grid". Clicking a layout card BROWSES it — ALL layouts are editable while browsed
    /// (ANSWER #7); only its "Use" button activates. Edits to the ACTIVE layout go through the
    /// live <see cref="BagProfileStore"/> list + SaveProfiles (with its rename/delete cascades);
    /// edits to an INACTIVE layout go through <see cref="StowProfileStore"/>'s document APIs
    /// (GetProfileCopy → mutate → ReplaceProfileIn; no cascades, by design — an inactive layout's
    /// names carry no live assignments).</para>
    ///
    /// <para>The ONLY game-state mutation reachable from this page is the labeller-funnel rename
    /// (<see cref="ItemActions.RenameThing"/>/<see cref="ItemActions.LabelWith"/>) — everything
    /// else writes config XML.</para>
    /// </summary>
    internal static class OrganizerPage
    {
        private const string TabTitle = "SmartStow";

        // ---------------------------------------------------------------- build

        public static void Build(Transform host)
        {
            var st = StowShared.State;

            // BORDERLESS-PAGE CONTRACT (manila seam, polish round): the FolderTabs Card is the
            // ONE visible bordered panel the selected tab merges into — a page root must never
            // add its own Image/Outline/UiaGlassSkin, or its border runs straight through under
            // the floating tab and breaks the folder illusion. Inner structure (columns, cards,
            // the EDITING band, the share bar) keeps its own boxes; breathing room against the
            // Card's border comes from the Card's own ~10px content inset.
            var rootGo = UiaUi.Go("organizer", host);
            var root = (RectTransform)rootGo.transform;
            UiaUi.Fill(root);
            // FlorpyDorp's close-up-14 ruling: NO internal gold seams — every cell carries
            // its OWN thin teal border instead, with plain spacing between cells. Gold lives
            // only on K's outer collar frame.
            UiaUi.VLayout(root, 8f);

            var sets = StowShared.Sets();
            if (!StowProfileStore.Available || sets.Count == 0)
            {
                CyanNote(root, "Storage Layouts are not available right now - bag profiles are coming from the old Profiles folder instead. Check the log; nothing was lost.");
                return;
            }

            string browsed = StowShared.BrowsedSet(sets);
            bool browsedIsActive = StowShared.IsActiveSet(browsed);
            var browsedProfiles = StowShared.ProfilesOfBrowsed(browsed);

            // A selection that no longer exists in the browsed layout is stale.
            if (!string.IsNullOrEmpty(st.SelectedProfile)
                && FindIn(browsedProfiles, st.SelectedProfile) == null)
                st.SelectedProfile = null;

            // Drag-drop wiring: re-assigned every build, cleared by StowShared.ResetCaches.
            StowDragDrop.OnProfileDropped = DropCopy;

            var colsGo = UiaUi.Go("columns", root);
            UiaUi.Size(colsGo, flexH: 1f);
            UiaUi.HLayout((RectTransform)colsGo.transform, UiaTheme.Gap, 0, 0, 0, 0,
                TextAnchor.UpperLeft, false);

            BuildLayoutsColumn(colsGo.transform, sets, browsed);
            BuildBagsColumn(colsGo.transform, browsedIsActive);
            BuildProfilesColumn(colsGo.transform, sets, browsed, browsedIsActive, browsedProfiles);

            BuildEditingBand(root, browsed, browsedIsActive, browsedProfiles);
            BuildShareBar(root, browsed);
        }

        /// <summary>The page's Note voice (comparison round): the kit Note in the light-cyan
        /// secondary token instead of grey-slate. Call sites that override the colour after
        /// (status/win/banner lines) keep their overrides.</summary>
        private static TextMeshProUGUI CyanNote(Transform parent, string text)
        {
            var t = UiaControls.Note(parent, text);
            t.color = StowSkin.MutedCyan;
            return t;
        }

        // (The internal gold lattice seams are RETIRED - FlorpyDorp's close-up-14 ruling:
        // cells carry their own thin teal borders and gold belongs only to K's outer collar.
        // This file no longer references FolderBandGraphic/UiaFolderPalette at all.)

        // ---------------------------------------------------------------- column shells

        /// <summary>One organizer column: a glass card with its own vertical layout. Widths are
        /// weights (~420/460/450 of the mockup's 1450) so the clamped window still divides
        /// sensibly on smaller canvases.</summary>
        private static Transform Column(Transform parent, string name, float weight)
        {
            var go = UiaUi.Go(name, parent);
            var le = UiaUi.Size(go, flexH: 1f);
            le.flexibleWidth = weight;
            le.minWidth = 230f;
            // Close-up-14 ruling: every cell is a bordered box — dark rounded fill with its
            // OWN thin teal border (the CardEdge family, never gold; gold is K's outer
            // collar only).
            var img = go.AddComponent<Image>();
            img.color = StowSkin.ColumnFill;
            UiaImages.Round(img);
            UiaUi.OutlineOf(img, StowSkin.CardEdge, 1f);
            // The mockup's column breathing room: ~10px inner padding, 6px row rhythm.
            UiaUi.VLayout((RectTransform)go.transform, 6f, 10, 10, 10, 10);
            return go.transform;
        }

        /// <summary>The column's flexing scroll area; everything above/below it stays put.
        /// The MIN height is the page's degrade path (visual fix wave 1): when the window runs
        /// short, a flexH-only host has preferred 0 and the layout group crushes it FIRST —
        /// every card list vanished while fixed chrome survived. With a floor, shortfall makes
        /// the lists SCROLL instead.</summary>
        private static Transform ScrollArea(Transform col)
        {
            var hostGo = UiaUi.Go("list", col);
            var le = UiaUi.Size(hostGo, flexH: 1f);
            le.minHeight = 80f;
            ScrollRect sr;
            var content = UiaUi.ScrollView(hostGo.transform, out sr, 6f);
            // Content padding: the selection halo's ~8px feathered reach needs room inside
            // the viewport's RectMask2D, or the mask cuts it flat on the card's outboard
            // edges (close-ups 13/15). 8px sides cover the full reach; 6px top/bottom lets
            // only the near-zero tail of a first/last card's halo touch the mask.
            var v = content.GetComponent<VerticalLayoutGroup>();
            if (v != null) v.padding = new RectOffset(8, 8, 6, 6);
            return content;
        }

        /// <summary>Column header row: spaced-caps accent title + right-aligned buttons, with
        /// the mockup's breathing room (30px, floored so compression can never strike the
        /// buttons through the title).</summary>
        private static Transform HeaderRow(Transform col, string title)
        {
            var row = UiaUi.Go("colhead", col);
            // MIN-only height (no-ellipsis round): a long "BAG PROFILES IN <layout>" title
            // may wrap to a second line, and an explicit preferred height would mask the
            // row's computed (taller) preferred — the EDITING-band clip-bug class.
            UiaUi.Size(row).minHeight = 30f;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);
            // Header caps are BRIGHTER than the accent fill (colour report: HeaderText token).
            // The title has MIN-WIDTH PRIORITY (mood-round nit: "STORAGE ..." truncation) —
            // the concept always shows the full title; the buttons shrink instead. Never
            // ellipsized: it steps down to 9pt, then wraps (StowShared.Fit).
            var t = UiaUi.Text(row.transform, title.ToUpperInvariant(), UiaTheme.SmallSize,
                StowSkin.HeaderText, TextAlignmentOptions.Left);
            t.characterSpacing = 3f;
            StowShared.Fit(t, UiaTheme.SmallSize, 9f, true);
            var tle = t.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
            tle.minWidth = 132f;
            return row.transform;
        }

        // ---------------------------------------------------------------- STORAGE LAYOUTS

        private static void BuildLayoutsColumn(Transform parent,
            List<StowProfileStore.StowSetInfo> sets, string browsed)
        {
            var st = StowShared.State;
            var col = Column(parent, "layouts", 42f);

            // Density diet (comparison round): the header carries title + ONE primary; the
            // long explainer lives behind the (i); Rescan and the whole Maintenance block move
            // into the header kebab — the column ends with cards, as the concept has it.
            var head = HeaderRow(col, "Storage Layouts");
            StowSkin.BareInfo(head,
                "A layout is a set of bag profiles. One layout is active at a time. Click a "
                + "card to browse and edit it; Use makes it the one G routes by. A .xml dropped "
                + "into StowProfiles/ appears after 'Rescan the folder' (the ... menu). The four "
                + "shipped layouts are seeded once, then they are yours - 'Restore shipped "
                + "layouts' in the same menu brings back the originals; everything else is "
                + "untouched. '+ New' creates an empty layout.");
            var newBtn = UiaControls.Button(head, "+ New", () => NewStowProfile(), 62f, 26f,
                UiaControls.ButtonStyle.Primary);
            UiaSearch.RegisterRow(TabTitle, "smartstow.layouts", "New Storage Layout",
                UiaSearch.MakeJump(newBtn.gameObject));
            var colMenu = StowSkin.BareKebab(head, LayoutsColumnMenu, 26f, 26f);
            UiaSearch.RegisterRow(TabTitle, "smartstow.maint", "Restore shipped layouts",
                UiaSearch.MakeJump(colMenu.gameObject));
            UiaSearch.RegisterRow(TabTitle, "smartstow.layouts", "Rescan the layouts folder",
                UiaSearch.MakeJump(colMenu.gameObject));

            CyanNote(col, "A layout is a set of bag profiles - one is active at a time.");

            var content = ScrollArea(col);
            for (int i = 0; i < sets.Count; i++)
                LayoutCard(content, sets[i], browsed);

            // The handle is not kept: gestures write state and Refresh, and the rebuild
            // re-feeds the initial message here.
            UiaComposite.InlineStatus(col, st.LayoutsMsg, st.LayoutsKind);
        }

        /// <summary>The layouts-column utility menu: the relocated Maintenance (Restore
        /// shipped keeps its two-step via the kebab's inline confirm) + the folder rescan —
        /// recovery actions, not daily ones (comparison round findings 14/19).</summary>
        private static List<UiaComposite.MenuItem> LayoutsColumnMenu()
        {
            var items = new List<UiaComposite.MenuItem>();
            items.Add(UiaComposite.MenuItem.Do("Rescan the folder", RescanLayouts));
            items.Add(UiaComposite.MenuItem.DangerDo("Restore shipped layouts", RestoreShipped));
            return items;
        }

        private static UiaIcon IconFor(StowProfileStore.StowSetInfo info)
        {
            if (info.IsShipped)
            {
                string n = info.Name ?? "";
                // "Ascended" = the shipped layout's name since 2026-09-26 (formerly "Stationpedia
                // Ascended" — ShippedStowProfiles.LegacyAscendedName); both keep the folder glyph.
                if (n.IndexOf("Stationpedia", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Ascended", StringComparison.OrdinalIgnoreCase) >= 0) return UiaIcon.Folder;
                if (n.IndexOf("Printer", StringComparison.OrdinalIgnoreCase) >= 0) return UiaIcon.Document;
                if (n.IndexOf("Category", StringComparison.OrdinalIgnoreCase) >= 0) return UiaIcon.Briefcase;
                if (n.IndexOf("Starter", StringComparison.OrdinalIgnoreCase) >= 0) return UiaIcon.Rocket;
            }
            return UiaIcon.Document;
        }

        private static void LayoutCard(Transform parent, StowProfileStore.StowSetInfo info, string browsed)
        {
            var st = StowShared.State;
            string name = info.Name;
            bool isBrowsed = string.Equals(name, browsed, StringComparison.OrdinalIgnoreCase);

            var card = UiaUi.Go("layoutcard", parent);
            var bg = card.AddComponent<Image>();
            UiaImages.Round(bg);
            // 1:1 round (cards report): selection is CYAN — orange is reserved for the tab
            // system and the warned bag card. Unselected edge is a near-fill teal line, never
            // a light-grey frame. SELECTION REDO (close-up 15): the browsed treatment is ONE
            // PanelGraphic halo child — the mod's real continuous glow falloff — replacing
            // the stacked sprite rings that read as chunky banded steps. The opaque lifted
            // face still carries the state on its own.
            if (isBrowsed) StowSkin.SelectionHalo(card.transform);
            else UiaUi.OutlineOf(bg, StowSkin.CardEdge, 1f);
            // Cards sit ON the panel: under-shadow instead of a bottom bevel hairline.
            StowSkin.UnderShadow(bg, 2f, 0.30f);
            // Drop-hover outline (profile drag): a brighter ring, off until a drag hovers.
            var hover = UiaUi.OutlineOf(bg, UiaTheme.Good, 3f);
            hover.enabled = false;
            var drop = card.AddComponent<LayoutDropTarget>();
            drop.SetName = name;
            drop.HoverOutline = hover;
            // Browsing NEVER activates (ANSWER #7) - the click is a pure selection. Faces are
            // OPAQUE teal-ladder fills (the wash-out defect class stays dead).
            var cardBtn = card.AddComponent<UiaControls.UiaButton>()
                .Init(bg, StowSkin.CardFill, StowSkin.CardFillHover, StowSkin.CardFillSelected);
            cardBtn.SetSelected(isBrowsed);
            cardBtn.OnClick = () => BrowseSet(name);
            // Density diet: the glyph ANCHORS the card's left edge spanning both text rows
            // (comparison finding 15: 30px box, ~24px line-art glyph), and the two compact
            // rows land the card at ~50 game px.
            UiaUi.HLayout((RectTransform)card.transform, 10f, 12, 10, 5, 5,
                TextAnchor.MiddleLeft);

            var iconHost = UiaUi.Go("icon", card.transform);
            UiaUi.Size(iconHost, 30f, 30f);
            UiaIcons.Attach(iconHost.transform, IconFor(info), 24f, StowSkin.SoftText);

            // No explicit height on the body: its VLayout reports the real preferred/min
            // (two rows, or three with the rename row open) — an explicit LayoutElement
            // height would MASK it (the clip-editing live-bug class).
            var body = UiaUi.Go("body", card.transform);
            UiaUi.Size(body, flexW: 1f);
            UiaUi.VLayout((RectTransform)body.transform, 2f);

            var row = UiaUi.Go("row", body.transform);
            // MIN-only height: the name may wrap, and the card must grow with it (an explicit
            // preferred height would mask that and overlap the next card).
            UiaUi.Size(row).minHeight = 22f;
            UiaUi.HLayout((RectTransform)row.transform, 8f);

            // Row 1 = title (soft white, bold, one size DOWN from headers per the type audit).
            // NO-ELLIPSIS rule (FlorpyDorp 2026-09-26: "Stationpedia Ascended" was cut to
            // "Stationpedia A..."): the name steps 13 -> 10pt to stay on one line, and past
            // that floor it wraps onto a second line; the card grows taller. Never truncated.
            var nameT = UiaUi.Text(row.transform, name, 13f, StowSkin.SoftText,
                TextAlignmentOptions.Left);
            nameT.fontStyle = FontStyles.Bold;
            StowShared.Fit(nameT, 13f, 10f, true);
            nameT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (info.IsActive)
            {
                // The concept's solid cyan ACTIVE pill with dark ink + its own under-shadow.
                TagChip(row.transform, "ACTIVE", UiaTheme.Accent, 64f, 20f, filled: true);
            }
            else
            {
                StowSkin.TealButton(row.transform, "Use", () => SetActiveStowProfile(name), 54f, 22f);
            }
            StowSkin.BareKebab(row.transform, () => LayoutMenu(info), 24f, 22f);

            // Row 2 = the profile count alone (cards report: exactly two rows). The SHIPPED
            // chip that sat left of it is gone (FlorpyDorp 2026-09-26: "The user doesn't need
            // to know that") — the count now starts flush under the name, the same left edge
            // as row 1, so the two-row block stays aligned and balanced beside the glyph.
            var row2 = UiaUi.Go("row2", body.transform);
            UiaUi.Size(row2).minHeight = 16f;
            UiaUi.HLayout((RectTransform)row2.transform, 8f);
            var count = UiaUi.Text(row2.transform,
                info.ProfileCount + (info.ProfileCount == 1 ? " bag profile" : " bag profiles"),
                11f, StowSkin.MutedCyan, TextAlignmentOptions.Left);
            StowShared.Fit(count, 11f, 9f, true);
            count.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (string.Equals(st.RenameLayoutTarget, name, StringComparison.Ordinal))
                LayoutRenameRow(body.transform, name);
        }

        /// <summary>A small tag pill ("ACTIVE", "NO STOW") — 1:1-round recipes:
        /// <paramref name="filled"/> = the concept's SOLID pill (opaque tag colour, dark ink,
        /// its own 1px under-shadow) used by ACTIVE; unfilled = the tinted recipe NO STOW uses,
        /// first drawn for the now-retired SHIPPED chip (a hue-tinted fill LIGHTER than the
        /// card, 1px hue border, light tinted text ~2 steps brighter than the border). Contrast is forced either way so label-vs-fill can never
        /// converge under a theme pass (the round-2 blank-pill class).</summary>
        private static void TagChip(Transform parent, string text, Color color, float w,
            float h = 18f, bool filled = false)
        {
            var go = UiaUi.Go("chip", parent);
            UiaUi.Size(go, h, w, flexW: 0f);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            UiaImages.Round(img);
            Color ink;
            if (filled)
            {
                img.color = new Color(color.r, color.g, color.b, 1f);   // opaque pill
                ink = StowSkin.DarkInk;
                StowSkin.UnderShadow(img, 1f, 0.60f);                    // seats the solid pill
            }
            else
            {
                // Tinted recipe: fill = dark base LIFTED toward the hue (lighter than the
                // card), border a step brighter, text two steps brighter again.
                Color fill = Color.Lerp(StowSkin.CardFill, color, 0.20f);
                fill.a = 0.96f;
                img.color = fill;
                UiaUi.OutlineOf(img, Color.Lerp(fill, color, 0.40f), 1f);
                ink = Color.Lerp(color, Color.white, 0.45f);
            }
            if (filled) UiaUi.OutlineOf(img, new Color(color.r, color.g, color.b, 0.55f), 1f);
            var t = UiaUi.Text(go.transform, text, 10f, ink, TextAlignmentOptions.Center);
            t.characterSpacing = 1f;
            t.fontStyle = FontStyles.Bold;
            UiaUi.Fill((RectTransform)t.transform);
        }

        private static List<UiaComposite.MenuItem> LayoutMenu(StowProfileStore.StowSetInfo info)
        {
            var st = StowShared.State;
            string name = info.Name;
            var items = new List<UiaComposite.MenuItem>();
            items.Add(UiaComposite.MenuItem.Do("Rename...", () =>
            {
                st.RenameLayoutTarget = name;
                UiaControlCenter.Refresh();
            }));
            items.Add(UiaComposite.MenuItem.Do("Duplicate", () => DuplicateStowProfile(name)));
            // The last-layout safeguard, kept from the old tab: with one layout left the item
            // does not appear at all (and the store refuses anyway - two fences).
            if (StowShared.Sets().Count > 1)
                items.Add(UiaComposite.MenuItem.DangerDo("Delete layout", () => DeleteStowProfile(name)));
            return items;
        }

        private static void LayoutRenameRow(Transform parent, string oldName)
        {
            var st = StowShared.State;
            string draftId = StowShared.LayoutRenameDraftId(oldName);
            var row = UiaUi.Go("renamerow", parent);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, 4f);
            var opts = new UiaInputs.TextInputOptions();
            // No name in the placeholder (no-ellipsis round): the old head+tail-shortened
            // quote truncated long names, and the card directly above already shows it whole.
            opts.Placeholder = "New name for this layout...";
            opts.DraftId = draftId;
            opts.CharacterLimit = 40;
            // Commit ONLY on Enter/OK: focus loss keeps the draft, so clicking Cancel (or
            // anywhere else) can never APPLY the rename (fix wave finding 1).
            opts.CommitOnFocusLoss = false;
            opts.OnCommit = v => CommitRenameSet(oldName, v, draftId);
            var input = UiaInputs.TextInput(row.transform, opts);
            UiaControls.Button(row.transform, "OK",
                () => CommitRenameSet(oldName, input != null ? input.Text : null, draftId), 44f, UiaTheme.RowH);
            UiaControls.Button(row.transform, "Cancel", () =>
            {
                st.RenameLayoutTarget = null;
                UiaInputs.ClearDraft(draftId);
                UiaControlCenter.Refresh();
            }, 66f, UiaTheme.RowH);
        }

        // (The old boxed Maintenance section is retired: the concept's column ends with its
        // cards. Restore-shipped + Rescan live in the header kebab above — hide, never
        // destroy; the restore keeps its confirm step via the kebab's inline arm.)

        // ---------------------------------------------------------------- BAGS

        private static void BuildBagsColumn(Transform parent, bool browsedIsActive)
        {
            var st = StowShared.State;
            var col = Column(parent, "bags", 46f);

            StowShared.ClearThumbsIfGesture();
            var bags = StowShared.WornBags();
            var profNames = ActiveProfileNames();
            var shared = new List<string> { "(no profile)" };
            shared.AddRange(profNames);

            // Density diet (comparison round): the "Using:" line and the container count fold
            // into ONE short right-aligned header line; the rename explainer collapses behind
            // an (i); the column's prose is its single description line.
            int withProfile = 0;
            bool anyForeign = false;
            for (int i = 0; i < bags.Count; i++)
            {
                var b = bags[i];
                if (b == null) continue;
                string cur = BagProfileStore.GetAssignedProfileName(b);
                if (string.IsNullOrEmpty(cur)) continue;
                withProfile++;
                if (profNames.IndexOf(cur) < 0) anyForeign = true;
            }

            var head = HeaderRow(col, "Bags");
            // No-ellipsis round: the header keeps only the SHORT count line; the layout name
            // (which used to be head+tail-shortened into "Using: Statio...nded") moves to its
            // own wrapping line under the header while an inactive layout is browsed.
            string statusLine = bags.Count + " on you - " + withProfile + " assigned";
            var statusT = UiaUi.Text(head, statusLine, 10f, StowSkin.MutedCyan,
                TextAlignmentOptions.Right);
            StowShared.Fit(statusT, 10f, 9f, true);
            var sle = UiaUi.Size(statusT.gameObject, flexW: 1f);
            sle.minWidth = 60f;
            StowSkin.TealButton(head, "Refresh", RefreshBags, 70f, 26f);

            if (!browsedIsActive)
                CyanNote(col, "Bags use the active layout: " + (StowProfileStore.ActiveName ?? "?") + ".");

            CyanNote(col, "Pick a profile for each bag you carry. Press G and items go to the bag whose profile matches.");

            // Rename-on-assign lives HERE, next to the gesture it changes (StowRenameConfig
            // explains why the key is not bound in UIAConfig).
            if (StowRenameConfig.Available)
            {
                var ren = UiaControls.ToggleRow(col, "Rename a bag when a profile is assigned",
                    StowRenameConfig.RenameOnAssign,
                    v => { StowRenameConfig.RenameOnAssign = v; UiaControlCenter.Refresh(); });
                UiaSearch.RegisterRow(TabTitle, "smartstow.bags", "Rename a bag when a profile is assigned",
                    UiaSearch.MakeJump(ren.transform.parent.gameObject));
                StowSkin.BareInfo(ren.transform.parent,
                    "On: assigning a Bag Profile labels the container with the profile's name - "
                    + "the same multiplayer-safe rename a Labeller does. Clearing a profile never "
                    + "renames back; use the bag's Rename entry.");
            }
            else
            {
                CyanNote(col, "Rename-on-assign is unavailable right now (settings are not loaded yet).");
            }

            var content = ScrollArea(col);
            if (bags.Count == 0)
            {
                CyanNote(content, "You are not carrying a container that can take a Bag Profile. Backpacks, tool belts, jetpacks, mining belts and boxes/crates qualify - suits, tools and packaging do not. Pick one up and press Refresh.");
            }
            else
            {
                var totals = PrefabTotals(bags);
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < bags.Count; i++)
                {
                    var b = bags[i];
                    if (b == null) continue;
                    string p = StowShared.SafePrefab(b);
                    int n;
                    seen.TryGetValue(p, out n);
                    seen[p] = n + 1;
                    int total;
                    totals.TryGetValue(p, out total);
                    BagCard(content, b, total > 1 ? n + 1 : 0, profNames, shared);
                }
            }

            // Design O6: the ZERO-SETUP tier stays discoverable rather than magic.
            if (bags.Count > withProfile && UIAConfig.StowUseAffinity.Value)
                StowShared.SubNote(col, "A container with no profile is not ignored: Smart Stow still routes to it by what is already inside (step 4 on the Routing page).");
            if (anyForeign)
                StowShared.SubNote(col, "A container above is mapped to a bag profile from another layout. It is kept, not routed - press Use on that layout to route by it, or pick a new profile here to replace it.");

            // Bags track (2026-09-26): equipped tool belts and jetpacks are now listed and
            // assignable; only suits/uniforms stay out, so the note says just that.
            CyanNote(col, "Suits can't take a profile.");

            BuildTestBox(col);

            UiaComposite.InlineStatus(col, st.BagsMsg, st.BagsKind);
        }

        /// <summary>Occurrence totals per prefab, so "#2" only appears when the same kind of
        /// container really does show up more than once (LoadoutEntry.Occurrence made visible).</summary>
        private static Dictionary<string, int> PrefabTotals(List<DynamicThing> bags)
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bags.Count; i++)
            {
                var b = bags[i];
                if (b == null) continue;
                string p = StowShared.SafePrefab(b);
                int n;
                totals.TryGetValue(p, out n);
                totals[p] = n + 1;
            }
            return totals;
        }

        private static void BagCard(Transform parent, DynamicThing bag, int ordinal,
            List<string> profNames, List<string> shared)
        {
            var st = StowShared.State;
            var b = bag;
            long id = StowShared.SafeRef(b);
            bool excluded = false;
            try { excluded = BagProfileStore.IsStowExcluded(b); } catch { }

            // Comparison round: bag cards are COMPACT two-liners by default; the profile
            // dropdown + Capture/Edit open on the SELECTED card only (the concept's
            // orange-border state — orange = selection focus, shared with the excluded warn).
            bool selectedBag = st.SelectedBagId != 0 && st.SelectedBagId == id;
            var card = UiaUi.Go("bagcard", parent);
            var bg = card.AddComponent<Image>();
            Color restFill = excluded
                ? Color.Lerp(StowSkin.CardFill, UiaTheme.Warn, 0.06f)
                : StowSkin.CardFill;
            UiaImages.Round(bg);
            bool warnRing = excluded || selectedBag;
            UiaUi.OutlineOf(bg, warnRing ? UiaTheme.Warn : StowSkin.CardEdge, warnRing ? 2f : 1f);
            StowSkin.UnderShadow(bg, 2f, 0.30f);
            var cardBtn = card.AddComponent<UiaControls.UiaButton>()
                .Init(bg, restFill, StowSkin.CardFillHover, restFill);
            cardBtn.OnClick = () =>
            {
                var s = StowShared.State;
                s.SelectedBagId = s.SelectedBagId == id ? 0 : id;
                UiaControlCenter.Refresh();
            };
            UiaUi.VLayout((RectTransform)card.transform, 4f, 10, 10, 8, 8);

            // ---- identity row: thumbnail on its recessed plate + name/type + kebab ----
            var row = UiaUi.Go("row", card.transform);
            // MIN-only heights down this identity row (no-ellipsis round): a long bag name or
            // subtitle wraps and the card grows, instead of being cut to "...".
            UiaUi.Size(row).minHeight = 40f;
            UiaUi.HLayout((RectTransform)row.transform, 8f);

            // The recessed near-black icon PLATE (cards report: plates are a BAG-card thing;
            // layout cards draw bare glyphs). Art sprite centered a step smaller than it.
            var plateGo = UiaUi.Go("plate", row.transform);
            UiaUi.Size(plateGo, 40f, 40f);
            var plate = plateGo.AddComponent<Image>();
            plate.color = StowSkin.DeepPlate;
            plate.raycastTarget = false;
            UiaImages.Round(plate);
            var thumbGo = UiaUi.Go("thumb", plateGo.transform);
            var trt = (RectTransform)thumbGo.transform;
            UiaUi.Fill(trt, 3f);
            var img = thumbGo.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var sp = StowShared.Thumb(b);
            if (sp != null) { img.sprite = sp; img.color = Color.white; }
            else
            {
                img.color = StowSkin.DeepPlate;
                var ph = UiaUi.Text(thumbGo.transform, "?", 12f, StowSkin.MutedCyan, TextAlignmentOptions.Center);
                UiaUi.Fill((RectTransform)ph.transform);
            }

            var nameCol = UiaUi.Go("namecol", row.transform);
            var ncle = UiaUi.Size(nameCol, flexW: 1f);
            ncle.minHeight = 40f;
            UiaUi.VLayout((RectTransform)nameCol.transform, 1f);
            var nameRow = UiaUi.Go("namerow", nameCol.transform);
            UiaUi.Size(nameRow).minHeight = 20f;
            UiaUi.HLayout((RectTransform)nameRow.transform, 6f);
            var nameT = UiaUi.Text(nameRow.transform,
                ordinal > 0 ? StowShared.SafeName(b) + " #" + ordinal : StowShared.SafeName(b),
                UiaTheme.LabelSize, excluded ? UiaTheme.Warn : StowSkin.SoftText,
                TextAlignmentOptions.Left);
            nameT.fontStyle = FontStyles.Bold;
            StowShared.Fit(nameT, UiaTheme.LabelSize, 11f, true);
            nameT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // NO STOW uses the tinted chip recipe in the Warn hue (cards report).
            if (excluded) TagChip(nameRow.transform, "NO STOW", UiaTheme.Warn, 62f);

            // Storage slots only (bags track 2026-09-26): a jetpack's propellant canister /
            // turbine battery and a survival belt's battery + chip are DEVICE sockets, not
            // storage — counting them made a jetpack read "1 used" with nothing packed.
            int slots = 0, used = 0;
            try
            {
                if (b.Slots != null)
                {
                    for (int i = 0; i < b.Slots.Count; i++)
                    {
                        Slot s = b.Slots[i];
                        if (s == null) continue;
                        bool device = false;
                        try { device = BagProfileGate.IsDeviceSlot(b, s); } catch { }
                        if (device) continue;
                        slots++;
                        if (s.Get() != null) used++;
                    }
                }
            }
            catch { }
            var sub = UiaUi.Text(nameCol.transform,
                StowShared.TypeName(b) + " - " + slots + (slots == 1 ? " slot, " : " slots, ")
                    + used + " used",
                11f, StowSkin.MutedCyan, TextAlignmentOptions.Left);
            StowShared.Fit(sub, 11f, 9f, true);
            UiaUi.Size(sub.gameObject).minHeight = 14f;

            StowSkin.BareKebab(row.transform, () => BagMenu(b, id, excluded), 26f, 26f);

            // ---- action row: profile dropdown + Capture + Edit — SELECTED card only ----
            if (!selectedBag) return;
            var act = UiaUi.Go("actions", card.transform);
            UiaUi.Size(act, 28f);
            UiaUi.HLayout((RectTransform)act.transform, 6f);
            int idx, foreignIdx;
            var opts = BagOptions(b, profNames, shared, out idx, out foreignIdx);
            int fi = foreignIdx;
            // Part D #9: an excluded bag's dropdown is DIMMED but USABLE - an excluded
            // container may still keep a profile.
            StowShared.InlineDropdown(act.transform, opts, idx,
                i =>
                {
                    if (i == fi) return;   // re-selecting the kept cross-layout mapping changes nothing
                    AssignToBag(b, i <= 0 ? null : profNames[i - 1]);
                }, -1f, excluded);
            StowSkin.TealButton(act.transform, "Capture", () => OpenCapture(b), 74f, 26f);
            string cur = null;
            try { cur = BagProfileStore.GetAssignedProfileName(b); } catch { }
            if (!string.IsNullOrEmpty(cur) && profNames.IndexOf(cur) >= 0)
            {
                string curName = cur;
                StowSkin.TealButton(act.transform, "Edit", () =>
                {
                    // Selects that profile in the Bag Profiles column (browsing jumps back to
                    // the active layout - that is where a mapped profile lives).
                    st.BrowsedSet = StowProfileStore.ActiveName;
                    st.SelectedProfile = curName;
                    if (st.OpenRuleGroup < 0) st.OpenRuleGroup = 0;
                    UiaControlCenter.Refresh();
                }, 52f, 26f);
            }

            if (st.RenameBagId != 0 && st.RenameBagId == id)
                BagRenameRow(card.transform, b, id);
        }

        private static List<UiaComposite.MenuItem> BagMenu(DynamicThing b, long id, bool excluded)
        {
            var st = StowShared.State;
            var items = new List<UiaComposite.MenuItem>();
            items.Add(UiaComposite.MenuItem.Switch("Never stow into this",
                () => { try { return BagProfileStore.IsStowExcluded(b); } catch { return false; } },
                v => SetExcluded(b, v)));
            items.Add(UiaComposite.MenuItem.Do("Rename bag...", () =>
            {
                st.RenameBagId = id;
                st.SelectedBagId = id;   // the rename row lives on the expanded card
                UiaControlCenter.Refresh();
            }));
            string cur = null;
            try { cur = BagProfileStore.GetAssignedProfileName(b); } catch { }
            if (!string.IsNullOrEmpty(cur))
                items.Add(UiaComposite.MenuItem.Do("Clear profile", () => AssignToBag(b, null)));
            return items;
        }

        private static void BagRenameRow(Transform parent, DynamicThing b, long id)
        {
            var st = StowShared.State;
            string draftId = StowShared.BagRenameDraftId(id);
            var row = UiaUi.Go("renamerow", parent);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, 4f);
            var opts = new UiaInputs.TextInputOptions();
            opts.Placeholder = "Rename this bag...";
            opts.DraftId = draftId;
            opts.CharacterLimit = 40;
            // Commit ONLY on Enter/OK (finding 1). This row matters most: its commit SENDS the
            // multiplayer labeller rename - a focus-loss commit fired it on the Cancel click.
            opts.CommitOnFocusLoss = false;
            opts.OnCommit = v => RenameBag(b, id, v, draftId);
            var input = UiaInputs.TextInput(row.transform, opts);
            UiaControls.Button(row.transform, "OK",
                () => RenameBag(b, id, input != null ? input.Text : null, draftId), 44f, UiaTheme.RowH);
            UiaControls.Button(row.transform, "Cancel", () =>
            {
                st.RenameBagId = 0;
                UiaInputs.ClearDraft(draftId);
                UiaControlCenter.Refresh();
            }, 66f, UiaTheme.RowH);
        }

        /// <summary>The option list for one bag's profile dropdown. When the stored assignment
        /// names a Bag Profile that lives in ANOTHER layout, showing "(no profile)" would be a
        /// lie the player then acts on — so that name is appended as its own, selected, INERT
        /// row instead (ported verbatim from the pre-0.9.8.0 tab).</summary>
        private static List<string> BagOptions(DynamicThing bag, List<string> profNames,
            List<string> shared, out int index, out int foreignIndex)
        {
            index = 0;
            foreignIndex = -1;
            string cur = BagProfileStore.GetAssignedProfileName(bag);
            if (string.IsNullOrEmpty(cur)) return shared;
            int at = profNames.IndexOf(cur);
            if (at >= 0) { index = at + 1; return shared; }
            var opts = new List<string>(shared);
            opts.Add(cur + " (not in this layout)");
            foreignIndex = opts.Count - 1;
            index = foreignIndex;
            return opts;
        }

        // ---------------------------------------------------------------- test box

        private static void BuildTestBox(Transform col)
        {
            var st = StowShared.State;
            var row = UiaUi.Go("testrow", col);
            UiaUi.Size(row, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);
            // Teal secondary, not a third competing solid-cyan primary (comparison finding
            // 20 - the page's primaries are ACTIVE and + New Layout/Profile only). The
            // dry-run caption shows only while a test item is live (density diet).
            var testBtn = StowSkin.TealButton(row.transform, "Test an item...", () =>
                UiaItemPicker.Open(prefab =>
                    {
                        StowShared.State.TestPrefab = prefab;
                        StowShared.InvalidateTest();
                    },
                    () => UiaControlCenter.Refresh(), "Test an item", "Testing"),
                130f, UiaTheme.RowH);
            UiaSearch.RegisterRow(TabTitle, "smartstow.bags", "Test an item (dry-run the router)",
                UiaSearch.MakeJump(testBtn.gameObject));
            var spacerGo = UiaUi.Go("spacer", row.transform);
            UiaUi.Size(spacerGo, UiaTheme.RowH, flexW: 1f);
            if (!string.IsNullOrEmpty(st.TestPrefab))
            {
                var hint = UiaUi.Text(row.transform, "Dry-run: nothing moves.",
                    11f, StowSkin.MutedCyan, TextAlignmentOptions.Right);
                StowShared.Fit(hint, 11f, 9f, true);
                UiaUi.Size(hint.gameObject, UiaTheme.RowH, 150f, flexW: 0f);
            }
            if (!string.IsNullOrEmpty(st.TestPrefab))
                UiaControls.Button(row.transform, "Clear",
                    () =>
                    {
                        StowShared.State.TestPrefab = null;
                        StowShared.InvalidateTest();
                        UiaControlCenter.Refresh();
                    }, 60f, UiaTheme.RowH);
            if (!string.IsNullOrEmpty(st.TestPrefab))
                TestResults(col);
        }

        /// <summary>Render the dry-run for the picked prefab from the GESTURE-CACHED lines in
        /// <see cref="StowShared.TestReadout"/> (fix wave finding 4): the prefab scan and the
        /// router's inventory walk run once per gesture build, never on a ~7x/s restyle. The
        /// pooled-list discipline lives inside the readout; this only paints strings.</summary>
        private static void TestResults(Transform col)
        {
            var st = StowShared.State;
            var l = StowShared.TestReadout(st.TestPrefab);
            if (l == null) return;
            if (l.Missing != null)
            {
                CyanNote(col, l.Missing);
                return;
            }
            var head = CyanNote(col, l.Head);
            head.color = UiaTheme.Text;
            if (l.Blocker != null)
            {
                CyanNote(col, l.Blocker);
                return;
            }
            if (l.Gate != null) CyanNote(col, l.Gate);
            if (l.NoMatch)
            {
                var none = CyanNote(col, "-> vanilla Smart Stow (no rule matched)");
                none.color = UiaTheme.TextDim;
                return;
            }
            var win = CyanNote(col, l.Win);
            win.color = UiaTheme.Good;
            win.fontSize = UiaTheme.LabelSize;
            if (l.Next != null)
            {
                var next = CyanNote(col, l.Next);
                next.color = UiaTheme.TextMute;
            }
        }

        // ---------------------------------------------------------------- BAG PROFILES

        private static void BuildProfilesColumn(Transform parent,
            List<StowProfileStore.StowSetInfo> sets, string browsed, bool browsedIsActive,
            List<BagProfile> profiles)
        {
            var st = StowShared.State;
            var col = Column(parent, "profiles", 45f);

            // The browsed layout's name rides the title WHOLE (no-ellipsis round): HeaderRow's
            // title steps down to 9pt and then wraps to a second line rather than cutting it.
            var head = HeaderRow(col, browsedIsActive
                ? "Bag Profiles"
                : "Bag Profiles in " + browsed);
            var newBtn = UiaControls.Button(head, "+ New", () => NewProfile(browsed, browsedIsActive),
                62f, 26f, UiaControls.ButtonStyle.Primary);
            UiaSearch.RegisterRow(TabTitle, "smartstow.profiles", "New Bag Profile",
                UiaSearch.MakeJump(newBtn.gameObject));

            if (browsedIsActive)
            {
                CyanNote(col, "Profiles in the active layout. Click one to edit its rules below.");
            }
            else
            {
                // Informational, not a warning (FlorpyDorp 2026-09-26: "the text that is yellow
                // for some reason ... should just be white") — the theme's primary text token,
                // not UiaTheme.Warn. Genuine warnings (NO STOW, Warn status lines) keep theirs.
                var banner = CyanNote(col, "Viewing " + browsed + " - not active. Its profiles route nothing until you press Use. Edits save into its file.");
                banner.color = UiaTheme.Text;
            }

            var content = ScrollArea(col);
            if (profiles.Count == 0)
            {
                CyanNote(content, "No Bag Profiles here yet - press + New Profile, Capture a bag, or copy some in from another layout.");
            }
            else
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    BagProfile p = profiles[i];
                    if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                    ProfileRow(content, sets, browsed, browsedIsActive, p);
                }
            }

            StowShared.SubNote(col, "Drag a profile onto a layout card to COPY it there. Copy keeps the original; Move takes it. A taken name gets a \"(2)\". Container mappings follow the NAME, not the copy.");
            UiaComposite.InlineStatus(col, st.ProfsMsg, st.ProfsKind);
        }

        private static void ProfileRow(Transform parent, List<StowProfileStore.StowSetInfo> sets,
            string browsed, bool browsedIsActive, BagProfile p)
        {
            var st = StowShared.State;
            string pname = p.Name;
            bool selected = string.Equals(st.SelectedProfile, pname, StringComparison.Ordinal);

            var host = UiaUi.Go("profhost", parent);
            UiaUi.VLayout((RectTransform)host.transform, 2f);

            var row = UiaUi.Go("profrow", host.transform);
            // The mockup's row height (the badge chip needs air) as a MIN: a long profile
            // name wraps and the row grows rather than ellipsizing (no-ellipsis round).
            UiaUi.Size(row).minHeight = 32f;
            var bg = row.AddComponent<Image>();
            UiaImages.Round(bg);
            // Teal-ladder row faces + cyan selection (the layout-card recipe, row-sized).
            UiaUi.OutlineOf(bg, selected ? UiaTheme.Accent : StowSkin.CardEdge, 1f);
            var btn = row.AddComponent<UiaControls.UiaButton>()
                .Init(bg, StowSkin.CardFill, StowSkin.CardFillHover, StowSkin.CardFillSelected);
            btn.SetSelected(selected);
            btn.OnClick = () =>
            {
                StowShared.State.SelectedProfile = pname;
                if (StowShared.State.OpenRuleGroup < 0) StowShared.State.OpenRuleGroup = 0;
                UiaControlCenter.Refresh();
            };
            var drag = row.AddComponent<ProfileDragSource>();
            drag.SetName = browsed;
            drag.ProfileName = pname;
            UiaUi.HLayout((RectTransform)row.transform, 6f, 6, 4, 0, 0);

            StowBadge.Chip(row.transform, pname);
            var nameT = UiaUi.Text(row.transform, pname, UiaTheme.LabelSize, UiaTheme.Text,
                TextAlignmentOptions.Left);
            StowShared.Fit(nameT, UiaTheme.LabelSize, 11f, true);
            nameT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var count = UiaUi.Text(row.transform,
                p.RuleCount + (p.RuleCount == 1 ? " rule" : " rules"),
                11f, StowSkin.MutedCyan, TextAlignmentOptions.Right);
            UiaUi.Size(count.gameObject, 26f, 56f, flexW: 0f);
            StowSkin.BareKebab(row.transform, () => ProfileMenu(sets, browsed, browsedIsActive, pname),
                26f, 24f);

            if (string.Equals(st.RenameProfileTarget, pname, StringComparison.Ordinal))
                ProfileRenameRow(host.transform, browsed, browsedIsActive, pname);
        }

        private static List<UiaComposite.MenuItem> ProfileMenu(List<StowProfileStore.StowSetInfo> sets,
            string browsed, bool browsedIsActive, string pname)
        {
            var st = StowShared.State;
            var items = new List<UiaComposite.MenuItem>();
            items.Add(UiaComposite.MenuItem.Do("Rename...", () =>
            {
                st.RenameProfileTarget = pname;
                UiaControlCenter.Refresh();
            }));
            items.Add(UiaComposite.MenuItem.Do("Duplicate",
                () => DuplicateProfile(browsed, browsedIsActive, pname)));
            // "Copy to >" / "Move to >" flattened into one row per target layout (the kebab is
            // the accessible twin of the drag - plan §10's ruling).
            for (int i = 0; i < sets.Count; i++)
            {
                string target = sets[i].Name;
                if (string.Equals(target, browsed, StringComparison.OrdinalIgnoreCase)) continue;
                // The FULL layout name (no-ellipsis round) — never head+tail-shortened.
                items.Add(UiaComposite.MenuItem.Do("Copy to " + target,
                    () => TransferProfile(browsed, pname, target, false)));
            }
            for (int i = 0; i < sets.Count; i++)
            {
                string target = sets[i].Name;
                if (string.Equals(target, browsed, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(UiaComposite.MenuItem.Do("Move to " + target,
                    () => TransferProfile(browsed, pname, target, true)));
            }
            items.Add(UiaComposite.MenuItem.DangerDo("Delete",
                () => DeleteProfile(browsed, browsedIsActive, pname)));
            return items;
        }

        private static void ProfileRenameRow(Transform parent, string browsed, bool browsedIsActive,
            string oldName)
        {
            var st = StowShared.State;
            // The draft id carries the LAYOUT name: "Ores" exists in several layouts and a
            // name-only key leaked a typed draft across them (fix wave finding 6).
            string draftId = StowShared.ProfileRenameDraftId(browsed, oldName);
            var row = UiaUi.Go("renamerow", parent);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, 4f);
            var opts = new UiaInputs.TextInputOptions();
            opts.Placeholder = "New name for this profile...";
            opts.DraftId = draftId;
            opts.CharacterLimit = 40;
            // Commit ONLY on Enter/OK: never on the focus loss a Cancel click causes (finding 1).
            opts.CommitOnFocusLoss = false;
            opts.OnCommit = v => CommitRenameProfile(browsed, browsedIsActive, oldName, v, draftId);
            var input = UiaInputs.TextInput(row.transform, opts);
            UiaControls.Button(row.transform, "OK",
                () => CommitRenameProfile(browsed, browsedIsActive, oldName,
                    input != null ? input.Text : null, draftId), 44f, UiaTheme.RowH);
            UiaControls.Button(row.transform, "Cancel", () =>
            {
                st.RenameProfileTarget = null;
                UiaInputs.ClearDraft(draftId);
                UiaControlCenter.Refresh();
            }, 66f, UiaTheme.RowH);
        }

        // ---------------------------------------------------------------- EDITING band

        // Player-facing: "UI Ascended class" (FlorpyDorp 2026-09-26 — never the "UIA" shorthand
        // on screen). Internal identifiers (UIAClass, UIAClasses, group index 1) are unchanged.
        private static readonly string[] GroupNames = { "By item", "By UI Ascended class", "By slot class", "By category" };
        // The same kinds for sentence use ("No UI Ascended class rules yet") — lowercasing
        // GroupNames would have printed "no by ui ascended class rules".
        private static readonly string[] KindNames = { "item", "UI Ascended class", "slot class", "category" };

        private static void BuildEditingBand(Transform root, string browsed, bool browsedIsActive,
            List<BagProfile> profiles)
        {
            var st = StowShared.State;

            var band = UiaUi.Go("editing", root);
            var bg = band.AddComponent<Image>();
            // Close-up-14 ruling: EDITING is a bordered box like the columns — dark fill,
            // its own thin teal border, no gold seams.
            bg.color = StowSkin.BandFill;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, StowSkin.CardEdge, 1f);
            // NO explicit LayoutElement here (live-bug fix): a LayoutElement's minHeight
            // OVERRIDES the VerticalLayoutGroup's computed minimum on the same GameObject, so
            // the visual-fix-wave "minHeight 64" floor let the outer layout compress the band
            // BELOW its opened-group content (rule list + note + add-row), which then overflowed
            // straight through the SHARE bar. Every child row now carries its own floor, so the
            // group's computed min/preferred are honest: opening a group grows the band at the
            // Refresh rebuild, the flexible COLUMNS above give the space back (their lists
            // scroll at their 80px floors), and closing shrinks it again. The 12px bottom
            // padding keeps the last row breathing off the share bar.
            UiaUi.VLayout((RectTransform)band.transform, 4f, 10, 10, 8, 12);

            var head = UiaUi.Go("edithead", band.transform);
            // MIN-only floor (no-ellipsis round): "EDITING: <profile> (in <layout>)" may wrap,
            // and the band's honest computed height then grows with it.
            UiaUi.Size(head).minHeight = 24f;
            UiaUi.HLayout((RectTransform)head.transform, 6f);
            string title = string.IsNullOrEmpty(st.SelectedProfile)
                ? "EDITING"
                : "EDITING:  " + st.SelectedProfile + (browsedIsActive ? "" : "  (in " + browsed + ")");
            var t = UiaUi.Text(head.transform, title, UiaTheme.SmallSize, StowSkin.HeaderText,
                TextAlignmentOptions.Left);
            t.characterSpacing = 4f;
            StowShared.Fit(t, UiaTheme.SmallSize, 9f, true);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // Part D #8: the "which rule wins" footnote became this (i) popover — bare glyph,
            // no enclosing box (comparison finding 21).
            StowSkin.BareInfo(head.transform,
                "When several rules match one item: item beats UI Ascended class, UI Ascended class beats slot class, slot class beats category. Priority only breaks ties within the same kind.");

            BagProfile profile = string.IsNullOrEmpty(st.SelectedProfile)
                ? null : FindIn(profiles, st.SelectedProfile);
            if (profile == null)
            {
                CyanNote(band.transform, "Click a bag profile above to edit its rules.");
                return;
            }

            // Group header strip: four toggles, ONE open at a time (ANSWER #3's clean cap).
            int[] counts =
            {
                profile.Items.Count, profile.UIAClasses.Count,
                profile.SlotClasses.Count, profile.Categories.Count
            };
            var strip = UiaUi.Go("groups", band.transform);
            UiaUi.Size(strip, 26f).minHeight = 26f;
            UiaUi.HLayout((RectTransform)strip.transform, 4f);
            for (int g = 0; g < 4; g++)
            {
                int gi = g;
                var gb = StowSkin.TealButton(strip.transform,
                    GroupNames[g] + " - " + counts[g] + (counts[g] == 1 ? " rule" : " rules"),
                    () =>
                    {
                        var s = StowShared.State;
                        s.OpenRuleGroup = s.OpenRuleGroup == gi ? -1 : gi;
                        s.RuleSearch = null;
                        UiaControlCenter.Refresh();
                    }, -1f, 26f);
                gb.SetSelected(st.OpenRuleGroup == g);
            }

            if (st.OpenRuleGroup >= 0 && st.OpenRuleGroup < 4)
                BuildOpenGroup(band.transform, browsed, browsedIsActive, profile, st.OpenRuleGroup);
        }

        private sealed class RuleRowData
        {
            public string Label;
            public int Priority;
            public Action<int> SetPriority;
            public Action Remove;
        }

        /// <summary>A pooled rule row's widget handles + its current data index (the virtual
        /// list re-binds pooled rows as it scrolls).</summary>
        private sealed class RuleRowWidget : MonoBehaviour
        {
            public TextMeshProUGUI Label;
            public UiaControls.UiaDropdown Priority;
            public UiaControls.UiaButton Remove;
            public int DataIndex = -1;
        }

        private static void BuildOpenGroup(Transform band, string browsed, bool browsedIsActive,
            BagProfile profile, int group)
        {
            var st = StowShared.State;
            string pname = profile.Name;

            // ---- flatten the open group into row data ----
            var rows = new List<RuleRowData>();
            if (group == 0)
            {
                for (int i = 0; i < profile.Items.Count; i++)
                {
                    var r = profile.Items[i];
                    if (r == null) continue;
                    string key = r.Prefab;
                    rows.Add(MakeRow(browsed, browsedIsActive, profile, pname, 0, key, r.Prefab, r.Priority));
                }
            }
            else if (group == 1)
            {
                for (int i = 0; i < profile.UIAClasses.Count; i++)
                {
                    var r = profile.UIAClasses[i];
                    if (r == null) continue;
                    rows.Add(MakeRow(browsed, browsedIsActive, profile, pname, 1, r.Name, r.Name, r.Priority));
                }
            }
            else if (group == 2)
            {
                for (int i = 0; i < profile.SlotClasses.Count; i++)
                {
                    var r = profile.SlotClasses[i];
                    if (r == null) continue;
                    rows.Add(MakeRow(browsed, browsedIsActive, profile, pname, 2, r.Name, r.Name, r.Priority));
                }
            }
            else
            {
                for (int i = 0; i < profile.Categories.Count; i++)
                {
                    var r = profile.Categories[i];
                    if (r == null) continue;
                    rows.Add(MakeRow(browsed, browsedIsActive, profile, pname, 3, r.Name, r.Name, r.Priority));
                }
            }

            // ---- By-item search (only once the list is genuinely long - ANSWER #3) ----
            var filtered = new List<RuleRowData>(rows);
            UiaVirtualList vlist = null;
            Action refilter = () =>
            {
                filtered.Clear();
                string term = StowShared.State.RuleSearch;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.IsNullOrEmpty(term)
                        || rows[i].Label.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        filtered.Add(rows[i]);
                }
                if (vlist != null) vlist.SetCount(filtered.Count);
            };
            // The box also builds whenever a filter TERM is live, however short the list has
            // become - deleting rules below the threshold must never leave an ACTIVE filter
            // with no UI to see or clear it (fix wave finding 3).
            if (group == 0 && (rows.Count > 15 || !string.IsNullOrEmpty(st.RuleSearch)))
            {
                var srow = UiaUi.Go("rulesearch", band);
                UiaUi.Size(srow, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)srow.transform, 4f);
                var sOpts = new UiaInputs.TextInputOptions();
                sOpts.Placeholder = "Filter item rules...";
                sOpts.InitialText = st.RuleSearch;
                sOpts.OnChanged = v => { StowShared.State.RuleSearch = v; refilter(); };
                UiaInputs.TextInput(srow.transform, sOpts);
            }

            // ---- the capped, scrolling rule list (ANSWER #3: grows to a cap, then scrolls) ----
            if (rows.Count == 0)
            {
                CyanNote(band, "No " + KindNames[group] + " rules yet - add one below.");
            }
            else
            {
                const float RuleRowH = 30f;
                const float CapH = 240f;
                // Height from the FILTERED count (finding 3): a live filter must not leave a
                // 240px hole under three matching rows.
                int shown = 0;
                string term0 = st.RuleSearch;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.IsNullOrEmpty(term0)
                        || rows[i].Label.IndexOf(term0, StringComparison.OrdinalIgnoreCase) >= 0)
                        shown++;
                }
                float viewH = Mathf.Min(CapH, Mathf.Max(RuleRowH + 4f, shown * RuleRowH));
                vlist = UiaVirtualList.Create(band, RuleRowH,
                    content =>
                    {
                        var rowGo = UiaUi.Go("rulerow", content);
                        var rt = (RectTransform)rowGo.transform;
                        // The mockup boxes each rule row (the clip-editing shot showed them
                        // edge-to-edge and naked). The box is an ignoreLayout child filling
                        // the row with a 1px breathing inset, so the HLayout never counts it
                        // and adjacent pooled rows keep a visible seam.
                        var boxGo = UiaUi.Go("box", rt);
                        var ble = boxGo.AddComponent<LayoutElement>();
                        ble.ignoreLayout = true;
                        UiaUi.Fill((RectTransform)boxGo.transform, 1f);
                        var box = boxGo.AddComponent<Image>();
                        box.color = StowSkin.CardFill;
                        box.raycastTarget = false;
                        UiaImages.Round(box);
                        UiaUi.OutlineOf(box, StowSkin.CardEdge, 1f);
                        UiaUi.HLayout(rt, UiaTheme.Gap, 8, 6, 2, 2);
                        var w = rowGo.AddComponent<RuleRowWidget>();
                        w.Label = UiaUi.Text(rt, "", UiaTheme.SmallSize, StowSkin.SoftText,
                            TextAlignmentOptions.Left);
                        // Pooled rows have a FIXED height (the virtual list's RuleRowH), so a
                        // long prefab name steps down to 9pt and, only past that, wraps to two
                        // 9pt lines — which the 30px row still holds. Never ellipsized.
                        StowShared.Fit(w.Label, UiaTheme.SmallSize, 9f, true);
                        w.Label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                        var pl = UiaUi.Text(rt, "Priority", 10f, UiaTheme.TextMute,
                            TextAlignmentOptions.Right);
                        UiaUi.Size(pl.gameObject, 24f, 44f, flexW: 0f);
                        w.Priority = StowShared.InlineDropdown(rt, StowShared.TierOptions, 1, null,
                            92f, false, 24f);
                        w.Priority.OnChanged = tierIdx =>
                        {
                            if (w.DataIndex >= 0 && w.DataIndex < filtered.Count)
                                filtered[w.DataIndex].SetPriority(StowShared.TierValueAt(tierIdx));
                        };
                        w.Remove = UiaControls.Button(rt, "X", () =>
                        {
                            if (w.DataIndex >= 0 && w.DataIndex < filtered.Count)
                                filtered[w.DataIndex].Remove();
                        }, 30f, 24f, UiaControls.ButtonStyle.Danger);
                        return rt;
                    },
                    (i, rt) =>
                    {
                        var w = rt.GetComponent<RuleRowWidget>();
                        if (w == null || i < 0 || i >= filtered.Count) return;
                        w.DataIndex = i;
                        w.Label.text = filtered[i].Label;
                        // SetOptions repaints without firing OnChanged - safe for re-binds.
                        w.Priority.SetOptions(StowShared.TierOptions, StowShared.TierIndexOf(filtered[i].Priority));
                    },
                    viewH);
                // Grows to CapH, but gives space back down to ~2 rows when the page runs short:
                // Size() pins min == preferred, and an incompressible 240px list pushed the
                // add-row and the whole SHARE bar out of the window (FlorpyDorp, 21 item rules).
                // The root VLayout now shares any shortfall; the list simply scrolls sooner.
                var vle = vlist.GetComponent<LayoutElement>();
                if (vle != null) vle.minHeight = Mathf.Min(viewH, 2f * RuleRowH + 4f);
                refilter();
            }

            BuildAddControls(band, browsed, browsedIsActive, profile, group);
        }

        private static RuleRowData MakeRow(string browsed, bool browsedIsActive, BagProfile liveProfile,
            string pname, int kind, string key, string label, int priority)
        {
            var d = new RuleRowData();
            d.Label = label;
            d.Priority = priority;
            if (browsedIsActive)
            {
                // ACTIVE layout: mutate the LIVE BagProfile and SaveProfiles (today's Save()).
                d.SetPriority = v => { MutateActive(liveProfile, kind, key, v, false); };
                d.Remove = () => { MutateActive(liveProfile, kind, key, 0, true); };
            }
            else
            {
                // INACTIVE layout: GetProfileCopy -> mutate -> ReplaceProfileIn (no cascades).
                d.SetPriority = v => MutateInactive(browsed, pname, kind, key, v, false);
                d.Remove = () => MutateInactive(browsed, pname, kind, key, 0, true);
            }
            return d;
        }

        private static void MutateActive(BagProfile p, int kind, string key, int priority, bool remove)
        {
            if (p == null) return;
            ApplyRuleEdit(p, kind, key, priority, remove);
            SaveActiveProfiles();
        }

        private static void MutateInactive(string setName, string pname, int kind, string key,
            int priority, bool remove)
        {
            BagProfile copy = null;
            try { copy = StowProfileStore.GetProfileCopy(setName, pname); } catch { }
            if (copy == null)
            {
                ProfsStatus("Could not read \"" + pname + "\" from " + setName + " (see the log).",
                    UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            ApplyRuleEdit(copy, kind, key, priority, remove);
            bool ok = false;
            try { ok = StowProfileStore.ReplaceProfileIn(setName, copy); } catch { }
            if (!ok)
                ProfsStatus("Could not save the edit into " + setName + " (see the log).",
                    UiaComposite.StatusKind.Error);
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        private static void ApplyRuleEdit(BagProfile p, int kind, string key, int priority, bool remove)
        {
            if (kind == 0)
            {
                for (int i = p.Items.Count - 1; i >= 0; i--)
                {
                    var r = p.Items[i];
                    if (r == null || !string.Equals(r.Prefab, key, StringComparison.Ordinal)) continue;
                    if (remove) p.Items.RemoveAt(i); else r.Priority = priority;
                    return;
                }
            }
            else if (kind == 1)
            {
                for (int i = p.UIAClasses.Count - 1; i >= 0; i--)
                {
                    var r = p.UIAClasses[i];
                    if (r == null || !string.Equals(r.Name, key, StringComparison.Ordinal)) continue;
                    if (remove) p.UIAClasses.RemoveAt(i); else r.Priority = priority;
                    return;
                }
            }
            else if (kind == 2)
            {
                for (int i = p.SlotClasses.Count - 1; i >= 0; i--)
                {
                    var r = p.SlotClasses[i];
                    if (r == null || !string.Equals(r.Name, key, StringComparison.Ordinal)) continue;
                    if (remove) p.SlotClasses.RemoveAt(i); else r.Priority = priority;
                    return;
                }
            }
            else
            {
                for (int i = p.Categories.Count - 1; i >= 0; i--)
                {
                    var r = p.Categories[i];
                    if (r == null || !string.Equals(r.Name, key, StringComparison.Ordinal)) continue;
                    if (remove) p.Categories.RemoveAt(i); else r.Priority = priority;
                    return;
                }
            }
        }

        private static void BuildAddControls(Transform band, string browsed, bool browsedIsActive,
            BagProfile profile, int group)
        {
            string pname = profile.Name;
            if (group == 0)
            {
                var row = UiaUi.Go("addrow", band);
                UiaUi.Size(row, UiaTheme.RowH).minHeight = UiaTheme.RowH;
                UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);
                UiaControls.Button(row.transform, "+ Add rule (pick an item)...", () =>
                    UiaItemPicker.Open(prefab => AddItemRule(browsed, browsedIsActive, pname, prefab),
                        () => UiaControlCenter.Refresh(), "Add an item rule", "Add"),
                    220f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
                return;
            }

            List<string> names;
            string sentinel;
            if (group == 1)
            {
                sentinel = "+ Add rule (UI Ascended class)...";
                names = new List<string>(Enum.GetNames(typeof(UIAClass)));
                StowShared.SubNote(band, "UI Ascended classes are the mod's 22-way grouping - sharper than the game's 11 categories, and they keep catching new and modded items.");
            }
            else if (group == 2)
            {
                sentinel = "+ Add rule (slot class)...";
                names = new List<string>(Enum.GetNames(typeof(Slot.Class)));
            }
            else
            {
                sentinel = "+ Add rule (category)...";
                names = new List<string>(Enum.GetNames(typeof(SortingClass)));
            }
            var opts = new List<string> { sentinel };
            opts.AddRange(names);
            var addRow = UiaUi.Go("addrow", band);
            UiaUi.Size(addRow, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)addRow.transform, UiaTheme.Gap);
            int g = group;
            StowShared.InlineDropdown(addRow.transform, opts, 0, i =>
            {
                if (i > 0) AddEnumRule(browsed, browsedIsActive, pname, g, opts[i]);
            }, 300f);   // 300: the longer "+ Add rule (UI Ascended class)..." sentinel fits at full size
        }

        private static void AddItemRule(string browsed, bool browsedIsActive, string pname, string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return;
            if (browsedIsActive)
            {
                var p = BagProfileStore.FindProfile(pname);
                if (p == null) return;
                foreach (var r in p.Items) if (r.Prefab == prefab) return;   // dedupe, as ever
                p.Items.Add(new ItemRule { Prefab = prefab, Priority = 100 });
                SaveActiveProfiles();
                return;
            }
            BagProfile copy = null;
            try { copy = StowProfileStore.GetProfileCopy(browsed, pname); } catch { }
            if (copy == null) return;
            foreach (var r in copy.Items) if (r.Prefab == prefab) return;
            copy.Items.Add(new ItemRule { Prefab = prefab, Priority = 100 });
            bool ok = false;
            try { ok = StowProfileStore.ReplaceProfileIn(browsed, copy); } catch { }
            if (!ok) ProfsStatus("Could not save the new rule (see the log).", UiaComposite.StatusKind.Error);
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        private static void AddEnumRule(string browsed, bool browsedIsActive, string pname,
            int group, string name)
        {
            // Priorities are the shipped defaults per kind: UIA 55, slot class 60, category 50.
            if (browsedIsActive)
            {
                var p = BagProfileStore.FindProfile(pname);
                if (p == null) return;
                if (!AddEnumRuleTo(p, group, name)) { UiaControlCenter.Refresh(); return; }
                SaveActiveProfiles();
                return;
            }
            BagProfile copy = null;
            try { copy = StowProfileStore.GetProfileCopy(browsed, pname); } catch { }
            if (copy == null) return;
            if (!AddEnumRuleTo(copy, group, name)) { UiaControlCenter.Refresh(); return; }
            bool ok = false;
            try { ok = StowProfileStore.ReplaceProfileIn(browsed, copy); } catch { }
            if (!ok) ProfsStatus("Could not save the new rule (see the log).", UiaComposite.StatusKind.Error);
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        /// <summary>Add one enum-named rule, deduped. False = it already existed (no write).</summary>
        private static bool AddEnumRuleTo(BagProfile p, int group, string name)
        {
            if (group == 1)
            {
                foreach (var r in p.UIAClasses) if (r.Name == name) return false;
                p.UIAClasses.Add(new UIAClassRule { Name = name, Priority = 55 });
            }
            else if (group == 2)
            {
                foreach (var r in p.SlotClasses) if (r.Name == name) return false;
                p.SlotClasses.Add(new SlotClassRule { Name = name, Priority = 60 });
            }
            else
            {
                foreach (var r in p.Categories) if (r.Name == name) return false;
                p.Categories.Add(new CategoryRule { Name = name, Priority = 50 });
            }
            return true;
        }

        // ---------------------------------------------------------------- SHARE bar

        private static void BuildShareBar(Transform root, string browsed)
        {
            var st = StowShared.State;
            var bar = UiaUi.Go("share", root);
            var bg = bar.AddComponent<Image>();
            // Close-up-14 ruling: bordered box, like the EDITING band and the columns.
            bg.color = StowSkin.BandFill;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, StowSkin.CardEdge, 1f);
            // No explicit LayoutElement (same live-bug class as the EDITING band): an explicit
            // minHeight would MASK the VLayout's computed minimum and let the bar be squeezed
            // below its own rows. The floored children make the computed values honest.
            UiaUi.VLayout((RectTransform)bar.transform, 3f, 10, 10, 8, 8);

            var row = UiaUi.Go("sharerow", bar.transform);
            UiaUi.Size(row, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);
            var titleT = UiaUi.Text(row.transform, "SHARE LAYOUT", UiaTheme.SmallSize,
                StowSkin.HeaderText, TextAlignmentOptions.Left);
            titleT.characterSpacing = 4f;
            UiaUi.Size(titleT.gameObject, UiaTheme.RowH, 120f, flexW: 0f);
            // Part D #3: ONE Export action = clipboard + StowProfiles/Export file + fingerprint.
            // Comparison finding 17: Export/Import are TEAL secondaries — the page keeps its
            // solid-cyan primaries to ACTIVE / + New Layout / + New Profile.
            // Close-up-14: the layout name rides the label ONLY when it fits WHOLE — the
            // head+tail Shorten() turned "My Stow Profile" into "Export My Stow...file"
            // gibberish. Too long = plain "Export" (the browsed card and the caption carry
            // the context).
            bool nameFits = !string.IsNullOrEmpty(browsed) && browsed.Length <= 12;
            var exportBtn = StowSkin.TealButton(row.transform,
                nameFits ? "Export " + browsed : "Export", () => ExportStowCode(browsed),
                nameFits ? 170f : 90f, UiaTheme.RowH);
            UiaSearch.RegisterRow(TabTitle, "smartstow.share", "Export a Storage Layout",
                UiaSearch.MakeJump(exportBtn.gameObject));
            var iOpts = new UiaInputs.TextInputOptions();
            iOpts.Placeholder = "Paste a share code - or leave empty to use the clipboard";
            iOpts.DraftId = "smartstow.import";
            iOpts.InitialText = st.ImportField;
            iOpts.OnChanged = v => { StowShared.State.ImportField = v; };
            var pasteBox = UiaInputs.TextInput(row.transform, iOpts);
            // Fail-soft retint of the kit input's face toward the field ladder (the input's
            // internals are kit-owned; if its Image is not on the handle's GO this no-ops and
            // the kit token retune covers it).
            if (pasteBox != null)
            {
                var boxImg = pasteBox.GetComponent<Image>();
                if (boxImg != null) boxImg.color = StowSkin.FieldFill;
            }
            var importBtn = StowSkin.TealButton(row.transform, "Import", ImportStowCode, 84f,
                UiaTheme.RowH);
            UiaSearch.RegisterRow(TabTitle, "smartstow.share", "Import a Storage Layout",
                UiaSearch.MakeJump(importBtn.gameObject));
            if (!string.IsNullOrEmpty(st.ShareCode))
                StowSkin.TealButton(row.transform, "Copy again", CopyShareCode, 96f, UiaTheme.RowH);

            var cap = CyanNote(bar.transform, "Export copies a share code and saves a file. Import always adds a new layout - nothing is overwritten. A .xml dropped into StowProfiles/ works too (press Rescan).");
            cap.fontSize = 11f;
            UiaComposite.InlineStatus(bar.transform, st.ShareMsg, st.ShareKind);
        }

        // ---------------------------------------------------------------- gestures: layouts

        private static void BrowseSet(string name)
        {
            var st = StowShared.State;
            if (string.Equals(name, st.BrowsedSet, StringComparison.OrdinalIgnoreCase)) return;
            // Close rename rows AND drop their drafts BEFORE the browsed layout changes (the
            // profile draft id is keyed under the OLD one) - finding 6.
            StowShared.CloseRenameRows(st);
            st.BrowsedSet = name;
            st.SelectedProfile = null;
            st.OpenRuleGroup = -1;
            st.RuleSearch = null;
            StowShared.InvalidateBrowsedProfiles();
            UiaControlCenter.Refresh();
        }

        private static void RescanLayouts()
        {
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            LayoutsStatus(null, UiaComposite.StatusKind.Info);
            UiaControlCenter.Refresh();
        }

        /// <summary>Use = activate. Writes the marker, then re-resolves EVERYTHING through the
        /// one load path launch uses (<see cref="BagProfileStore.LoadProfiles"/>). Per-save
        /// ASSIGNMENTS are untouched: they name profiles, so a name the new layout also has
        /// keeps working, and one it does not have goes quiet until you switch back.</summary>
        private static void SetActiveStowProfile(string want)
        {
            var st = StowShared.State;
            if (string.IsNullOrEmpty(want)) return;
            if (string.Equals(want, StowProfileStore.ActiveName, StringComparison.Ordinal)) return;
            if (!StowProfileStore.SetActive(want))
            {
                LayoutsStatus("Could not switch to \"" + want + "\" (see the log).",
                    UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            BagProfileStore.LoadProfiles();   // one load path: re-resolve everything from disk
            st.SelectedProfile = null;
            st.OpenRuleGroup = -1;
            st.BrowsedSet = StowProfileStore.ActiveName;
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            LayoutsStatus("Now using \"" + StowProfileStore.ActiveName + "\" ("
                + BagProfileStore.Profiles.Count + " bag profile(s)).", UiaComposite.StatusKind.Good);
            StowShared.BumpGridChrome();      // chips/badges follow the new profile set
            UiaControlCenter.Refresh();
        }

        private static void NewStowProfile()
        {
            var st = StowShared.State;
            // The DEFAULT NAME is player-facing (it becomes the card's title), so it follows
            // the terminology sweep: "My Layout", as mockup 3 shows a user layout.
            string made = StowProfileStore.Create("My Layout", false);
            if (made == null)
            {
                LayoutsStatus("Could not create a new layout (see the log).", UiaComposite.StatusKind.Error);
            }
            else
            {
                LayoutsStatus("Created \"" + made + "\". Press Use on its card to switch to it.",
                    UiaComposite.StatusKind.Good);
                st.BrowsedSet = made;
            }
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            UiaControlCenter.Refresh();
        }

        /// <summary>Duplicate a layout. Create() can only copy the ACTIVE one, so an inactive
        /// source is duplicated by making an empty layout and bulk-copying its bag profiles
        /// across (one write, not fourteen — the TransferMany path).</summary>
        private static void DuplicateStowProfile(string source)
        {
            var st = StowShared.State;
            if (string.IsNullOrEmpty(source)) return;
            bool sourceIsActive = StowShared.IsActiveSet(source);
            string made = StowProfileStore.Create(source + " copy", sourceIsActive);
            if (made == null)
            {
                LayoutsStatus("Could not duplicate \"" + source + "\" (see the log).",
                    UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            int copied = sourceIsActive
                ? BagProfileStore.Profiles.Count
                : StowProfileStore.TransferMany(source, made);
            LayoutsStatus("Created \"" + made + "\" with " + copied + " bag profile(s).",
                UiaComposite.StatusKind.Good);
            st.BrowsedSet = made;
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            UiaControlCenter.Refresh();
        }

        private static void CommitRenameSet(string oldName, string typed, string draftId)
        {
            var st = StowShared.State;
            string wanted = ProfileCapture.SanitizeName(typed);
            if (string.IsNullOrEmpty(wanted))
            {
                LayoutsStatus("Type a new name first.", UiaComposite.StatusKind.Warn);
                UiaControlCenter.Refresh();
                return;
            }
            string made = StowProfileStore.RenameSet(oldName, wanted);
            if (made == null)
            {
                LayoutsStatus("Could not rename \"" + oldName + "\" to \"" + wanted
                    + "\" - that name may already be taken (see the log).", UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            LayoutsStatus("Renamed \"" + oldName + "\" to \"" + made + "\".", UiaComposite.StatusKind.Good);
            if (string.Equals(st.BrowsedSet, oldName, StringComparison.OrdinalIgnoreCase))
                st.BrowsedSet = made;
            st.RenameLayoutTarget = null;
            UiaInputs.ClearDraft(draftId);
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            UiaControlCenter.Refresh();
        }

        private static void DeleteStowProfile(string name)
        {
            var st = StowShared.State;
            bool wasActive = StowShared.IsActiveSet(name);
            if (!StowProfileStore.DeleteSet(name))
            {
                LayoutsStatus("Could not delete \"" + name
                    + "\" (the last layout cannot be deleted - see the log).", UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            st.BrowsedSet = null;
            st.SelectedProfile = null;
            st.OpenRuleGroup = -1;
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            if (wasActive)
            {
                BagProfileStore.LoadProfiles();   // the store re-pointed the marker; re-resolve once
                StowShared.BumpGridChrome();
                LayoutsStatus("Deleted \"" + name + "\". Now using \""
                    + (StowProfileStore.ActiveName ?? "?")
                    + "\". Container mappings were kept - they go quiet until their names exist again.",
                    UiaComposite.StatusKind.Good);
            }
            else
            {
                LayoutsStatus("Deleted \"" + name + "\". Container mappings were kept - they go quiet until their names exist again.",
                    UiaComposite.StatusKind.Good);
            }
            UiaControlCenter.Refresh();
        }

        private static void RestoreShipped()
        {
            var st = StowShared.State;
            int written = 0;
            // RestoreShipped re-runs LoadProfiles itself (the active document may have just
            // been overwritten on disk).
            try { written = StowProfileStore.RestoreShipped(); }
            catch (Exception e) { UIALog.Warn("Restore shipped layouts failed: " + e.Message); }
            st.SelectedProfile = null;
            st.OpenRuleGroup = -1;
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            LayoutsStatus(written > 0
                    ? "Restored " + written + " shipped layout(s)."
                    : "Nothing was restored (see the log).",
                written > 0 ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Warn);
            StowShared.BumpGridChrome();
            UiaControlCenter.Refresh();
        }

        // ---------------------------------------------------------------- gestures: profiles

        private static void NewProfile(string browsed, bool browsedIsActive)
        {
            var st = StowShared.State;
            if (browsedIsActive)
            {
                string name = BagProfileStore.UniqueProfileName("Profile");
                BagProfileStore.Profiles.Add(new BagProfile { Name = name });
                st.SelectedProfile = name;
                if (st.OpenRuleGroup < 0) st.OpenRuleGroup = 0;
                SaveActiveProfiles();
                return;
            }
            string landed = null;
            try { landed = StowProfileStore.AddProfileTo(browsed, new BagProfile { Name = "Profile" }); }
            catch { }
            if (landed == null)
            {
                ProfsStatus("Could not add a profile to " + browsed + " (see the log).",
                    UiaComposite.StatusKind.Error);
            }
            else
            {
                st.SelectedProfile = landed;
                if (st.OpenRuleGroup < 0) st.OpenRuleGroup = 0;
            }
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        private static void DuplicateProfile(string browsed, bool browsedIsActive, string pname)
        {
            var st = StowShared.State;
            if (browsedIsActive)
            {
                var src = BagProfileStore.FindProfile(pname);
                if (src == null) return;
                var copy = StowShared.CloneProfile(src);
                copy.Name = BagProfileStore.UniqueProfileName(pname);
                BagProfileStore.Profiles.Add(copy);
                st.SelectedProfile = copy.Name;
                ProfsStatus("Duplicated \"" + pname + "\" as \"" + copy.Name + "\".",
                    UiaComposite.StatusKind.Good);
                SaveActiveProfiles();
                return;
            }
            BagProfile srcCopy = null;
            try { srcCopy = StowProfileStore.GetProfileCopy(browsed, pname); } catch { }
            string landed = null;
            if (srcCopy != null)
            {
                try { landed = StowProfileStore.AddProfileTo(browsed, srcCopy); } catch { }
            }
            if (landed == null)
            {
                ProfsStatus("Could not duplicate \"" + pname + "\" (see the log).",
                    UiaComposite.StatusKind.Error);
            }
            else
            {
                st.SelectedProfile = landed;
                ProfsStatus("Duplicated \"" + pname + "\" as \"" + landed + "\".",
                    UiaComposite.StatusKind.Good);
            }
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        private static void CommitRenameProfile(string browsed, bool browsedIsActive, string oldName,
            string typed, string draftId)
        {
            var st = StowShared.State;
            string wanted = ProfileCapture.SanitizeName(typed);
            if (string.IsNullOrEmpty(wanted))
            {
                ProfsStatus("Type a new name first.", UiaComposite.StatusKind.Warn);
                UiaControlCenter.Refresh();
                return;
            }
            if (wanted == oldName)
            {
                ProfsStatus("That is already its name.", UiaComposite.StatusKind.Info);
                UiaControlCenter.Refresh();
                return;
            }
            if (browsedIsActive)
            {
                if (BagProfileStore.FindProfile(wanted) != null)
                {
                    ProfsStatus("A profile called \"" + wanted + "\" already exists.",
                        UiaComposite.StatusKind.Warn);
                    UiaControlCenter.Refresh();
                    return;
                }
                // RenameProfile cascades the name through assignments (this save AND every other
                // save's file), prefab defaults, loadouts and drop-in copies.
                bool ok = BagProfileStore.RenameProfile(oldName, wanted);
                ProfsStatus(ok
                        ? "Renamed \"" + oldName + "\" to \"" + wanted + "\" (container mappings follow)."
                        : "Could not rename \"" + oldName + "\" (see the log).",
                    ok ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
                if (ok && string.Equals(st.SelectedProfile, oldName, StringComparison.Ordinal))
                    st.SelectedProfile = wanted;
                StowShared.BumpGridChrome();   // chips/badges carry the name
            }
            else
            {
                string made = null;
                try { made = StowProfileStore.RenameProfileIn(browsed, oldName, wanted); } catch { }
                ProfsStatus(made != null
                        ? "Renamed \"" + oldName + "\" to \"" + made + "\" in " + browsed + "."
                        : "Could not rename \"" + oldName + "\" - the name may be taken (see the log).",
                    made != null ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
                if (made != null && string.Equals(st.SelectedProfile, oldName, StringComparison.Ordinal))
                    st.SelectedProfile = made;
            }
            st.RenameProfileTarget = null;
            UiaInputs.ClearDraft(draftId);
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        private static void DeleteProfile(string browsed, bool browsedIsActive, string pname)
        {
            var st = StowShared.State;
            if (browsedIsActive)
            {
                // DeleteProfile clears assignments, prefab defaults, loadout entries and shared
                // copies, so nothing is left pointing at a name that no longer exists.
                bool ok = BagProfileStore.DeleteProfile(pname);
                ProfsStatus(ok
                        ? "Deleted \"" + pname + "\". Bags that used it now have no profile."
                        : "Could not delete \"" + pname + "\" (see the log).",
                    ok ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
                StowShared.BumpGridChrome();
            }
            else
            {
                bool ok = false;
                try { ok = StowProfileStore.RemoveProfileFrom(browsed, pname); } catch { }
                ProfsStatus(ok
                        ? "Removed \"" + pname + "\" from " + browsed + "."
                        : "Could not remove \"" + pname + "\" (see the log).",
                    ok ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
            }
            if (string.Equals(st.SelectedProfile, pname, StringComparison.Ordinal))
                st.SelectedProfile = null;
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateSets();
            UiaControlCenter.Refresh();
        }

        /// <summary>Copy/Move between layouts — the kebab twin AND the drop handler both land
        /// here. TransferProfile knows the active layout's truth is the in-memory list; the
        /// sourceKept flag is the one honest-reporting case a bare name cannot carry.</summary>
        private static void TransferProfile(string source, string profileName, string target, bool move)
        {
            string landed = null;
            bool sourceKept = false;
            try { landed = StowProfileStore.TransferProfile(source, profileName, target, move, out sourceKept); }
            catch (Exception e) { UIALog.Warn("Bag profile transfer failed: " + e.Message); }
            if (landed == null)
            {
                ProfsStatus("Could not " + (move ? "move" : "copy") + " \"" + profileName + "\" to \""
                    + target + "\" (see the log).", UiaComposite.StatusKind.Error);
            }
            else if (sourceKept)
            {
                // The copy landed but the source could not be rewritten: the profile is in BOTH
                // layouts now. Say so - reporting this as a clean move creates mystery duplicates.
                string srcLabel = string.IsNullOrEmpty(source)
                    ? (StowProfileStore.ActiveName ?? "the active layout") : source;
                ProfsStatus("Copied \"" + profileName + "\" to \"" + target
                    + "\", but could not remove it from \"" + srcLabel + "\" - see the log.",
                    UiaComposite.StatusKind.Warn);
            }
            else
            {
                ProfsStatus((move ? "Moved \"" : "Copied \"") + profileName + "\" to \"" + target + "\""
                    + (string.Equals(landed, profileName, StringComparison.Ordinal)
                        ? "." : " as \"" + landed + "\" (that name was taken)."),
                    UiaComposite.StatusKind.Good);
            }
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            StowShared.BumpGridChrome();   // the active layout may have gained or lost a profile
            UiaControlCenter.Refresh();
        }

        /// <summary>The drag-drop landing: dropping a profile row on a layout card is always a
        /// COPY (the kebab keeps Move as the deliberate, spelled-out gesture).</summary>
        private static void DropCopy(string sourceSet, string profileName, string targetSet)
        {
            TransferProfile(sourceSet, profileName, targetSet, false);
        }

        // ---------------------------------------------------------------- gestures: bags

        private static void RefreshBags()
        {
            StowShared.InvalidateBags();
            BagsStatus(null, UiaComposite.StatusKind.Info);
            UiaControlCenter.Refresh();
        }

        /// <summary>Write one bag assignment. Re-gates the container at CLICK time (the list was
        /// built on an earlier frame — the bag may have been dropped), then runs the cheap
        /// typed-pack validation so assigning "Materials" to a mining backpack SAYS half its
        /// rules can never land there. Warning only, never a block.</summary>
        private static void AssignToBag(DynamicThing bag, string profileName)
        {
            if (bag == null) return;
            if (!BagProfileGate.IsAssignableContainer(bag) || !BagProfileGate.IsOnLocalPlayer(bag))
            {
                BagsStatus("That container is not on you any more - nothing was changed.",
                    UiaComposite.StatusKind.Warn);
                StowShared.InvalidateBags();
                UiaControlCenter.Refresh();
                return;
            }
            BagProfileStore.Assign(bag, profileName);
            BagsStatus(null, UiaComposite.StatusKind.Info);
            if (!string.IsNullOrEmpty(profileName))
            {
                string warn = null;
                try { warn = BagProfileGate.ValidateAssignment(bag, BagProfileStore.FindProfile(profileName)); }
                catch (Exception e) { UIALog.Warn("Profile/container validation failed: " + e.Message); }
                if (!string.IsNullOrEmpty(warn)) BagsStatus(warn, UiaComposite.StatusKind.Warn);
                if (StowRenameConfig.RenameOnAssign) RenameForProfile(bag, profileName);
            }
            StowShared.BumpGridChrome();   // an open window repaints its chip + badge
            UiaControlCenter.Refresh();
        }

        /// <summary>Label the bag with the profile's name. The decision (already labelled?
        /// send?) lives in <see cref="ItemActions.LabelWith"/>, shared with the Universal
        /// Inventory's profile popup so the two surfaces cannot drift.</summary>
        private static void RenameForProfile(DynamicThing bag, string profileName)
        {
            ItemActions.LabelResult r;
            try { r = ItemActions.LabelWith(bag, profileName); }
            catch (Exception e)
            {
                UIALog.Warn("Rename on assign failed: " + e.Message);
                r = ItemActions.LabelResult.Failed;
            }
            if (r == ItemActions.LabelResult.Unchanged) return;   // already labelled: nothing sent
            if (r == ItemActions.LabelResult.Renamed)
                BagsStatus(RenameOkNote(bag,
                        ItemActions.SanitizedRenamePreview(bag, profileName) ?? profileName),
                    UiaComposite.StatusKind.Good);
            else
                BagsStatus("The profile was assigned, but that container could not be renamed.",
                    UiaComposite.StatusKind.Warn);
        }

        /// <summary>Manual rename, same labeller funnel, same gates, same honest reporting: on
        /// an MP CLIENT the new name only exists once the server echoes it — never claim
        /// otherwise.</summary>
        private static void RenameBag(DynamicThing bag, long id, string typed, string draftId)
        {
            var st = StowShared.State;
            if (bag == null) return;
            if (string.IsNullOrEmpty(typed) || typed.Trim().Length == 0)
            {
                BagsStatus("Type a name in the Rename box first.", UiaComposite.StatusKind.Warn);
                UiaControlCenter.Refresh();
                return;
            }
            if (!BagProfileGate.IsAssignableContainer(bag) || !BagProfileGate.IsOnLocalPlayer(bag))
            {
                BagsStatus("That container is not on you any more - nothing was renamed.",
                    UiaComposite.StatusKind.Warn);
                StowShared.InvalidateBags();
                UiaControlCenter.Refresh();
                return;
            }
            bool ok = false;
            try { ok = ItemActions.RenameThing(bag, typed); }
            catch (Exception e) { UIALog.Warn("Manual bag rename failed: " + e.Message); }
            if (ok)
            {
                st.RenameBagId = 0;
                UiaInputs.ClearDraft(draftId);
                BagsStatus(RenameOkNote(bag, typed.Trim()), UiaComposite.StatusKind.Good);
            }
            else
            {
                BagsStatus("Could not rename that container (see the log).", UiaComposite.StatusKind.Error);
            }
            UiaControlCenter.Refresh();
        }

        private static string RenameOkNote(DynamicThing bag, string wanted)
        {
            bool authoritative = false;
            try { authoritative = Assets.Scripts.GameManager.RunSimulation; } catch { }
            if (authoritative) return "Renamed that container to \"" + StowShared.SafeName(bag) + "\".";
            // MP client: the rename went out as one message and lands when the server confirms.
            return "Rename sent (\"" + wanted + "\") - it appears once the server confirms it.";
        }

        private static void SetExcluded(DynamicThing bag, bool excluded)
        {
            if (bag == null) return;
            if (!BagProfileGate.IsOnLocalPlayer(bag))
            {
                BagsStatus("That container is not on you any more - nothing was changed.",
                    UiaComposite.StatusKind.Warn);
                StowShared.InvalidateBags();
                UiaControlCenter.Refresh();
                return;
            }
            BagProfileStore.SetStowExcluded(bag, excluded);
            BagsStatus(excluded
                    ? "Smart Stow will never put anything into \"" + StowShared.SafeName(bag) + "\"."
                    : null,
                UiaComposite.StatusKind.Info);
            StowShared.BumpGridChrome();
            UiaControlCenter.Refresh();
        }

        /// <summary>Open the ONE capture dialog (the same panel the Universal Inventory uses)
        /// ABOVE the F10 window (ANSWER #10). SAVE inside it auto-assigns and bumps the grid
        /// chrome version itself; the onClosed callback just re-reads our listings.</summary>
        private static void OpenCapture(DynamicThing bag)
        {
            if (bag == null) return;
            if (!BagProfileGate.IsAssignableContainer(bag) || !BagProfileGate.IsOnLocalPlayer(bag))
            {
                BagsStatus("That container is not on you any more - nothing was captured.",
                    UiaComposite.StatusKind.Warn);
                StowShared.InvalidateBags();
                UiaControlCenter.Refresh();
                return;
            }
            global::StationeersUIMod.UI.Grid.GridCapturePanel.Open(bag,
                StowShared.CaptureOverF10SortOrder, OnCaptureClosed);
        }

        private static void OnCaptureClosed()
        {
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            StowShared.InvalidateBags();
            if (UiaControlCenter.IsOpen) UiaControlCenter.Refresh();
        }

        // ---------------------------------------------------------------- gestures: share

        private static void ExportStowCode(string setName)
        {
            var st = StowShared.State;
            st.ShareCode = null;
            StowProfileDoc doc = BuildDocFor(setName);
            if (doc == null)
            {
                ShareStatus("Could not read \"" + setName + "\" to export it.", UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            string fingerprint;
            string code = StowShareCodec.Encode(doc, out fingerprint);
            if (code == null)
            {
                ShareStatus("Could not build a share code for \"" + setName + "\" (see the log).",
                    UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            st.ShareCode = code;
            bool copied = StowShared.CopyToClipboard(code);
            string file = StowShareCodec.WriteExportFile(doc.Name, code, fingerprint);
            ShareStatus("Fingerprint " + fingerprint + " - " + code.Length + " characters"
                + (copied ? ", copied to the clipboard" : ", clipboard unavailable")
                + (file != null ? ". Also saved to " + file : ". The file copy could not be written."),
                copied || file != null ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Warn);
            UiaControlCenter.Refresh();
        }

        private static void CopyShareCode()
        {
            bool ok = StowShared.CopyToClipboard(StowShared.State.ShareCode);
            ShareStatus(ok ? "Copied the code to your clipboard."
                    : "Could not reach the clipboard (see the log).",
                ok ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
            UiaControlCenter.Refresh();
        }

        /// <summary>Import: the paste box, or the CLIPBOARD when it is empty (Part D #4 — there
        /// is no in-game file browser; a .xml dropped into StowProfiles/ is the third transport).
        /// Never overwrites: the import always lands as a NEW, INACTIVE layout, and browsing
        /// jumps to it (Part D #5).</summary>
        private static void ImportStowCode()
        {
            var st = StowShared.State;
            string text = st.ImportField;
            bool fromClipboard = false;
            if (string.IsNullOrEmpty(text))
            {
                text = StowShared.ReadClipboard();
                fromClipboard = !string.IsNullOrEmpty(text);
            }
            if (string.IsNullOrEmpty(text))
            {
                ShareStatus("Paste a code into the box first (or copy one and press Import again).",
                    UiaComposite.StatusKind.Warn);
                UiaControlCenter.Refresh();
                return;
            }

            string fingerprint, error;
            int unknown;
            StowProfileDoc doc = StowShareCodec.Decode(text, out fingerprint, out error, out unknown);
            if (doc == null)
            {
                ShareStatus(error + (fromClipboard ? " (read from your clipboard)" : ""),
                    UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            string landed = StowProfileStore.ImportDoc(doc);
            if (landed == null)
            {
                ShareStatus("That code decoded, but the layout could not be written (see the log).",
                    UiaComposite.StatusKind.Error);
                UiaControlCenter.Refresh();
                return;
            }
            st.ImportField = null;
            UiaInputs.ClearDraft("smartstow.import");
            st.BrowsedSet = landed;
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            ShareStatus("Imported \"" + landed + "\" (" + doc.Profiles.Count
                + " bag profile(s), fingerprint " + fingerprint + ")"
                + (unknown > 0
                    ? ". " + unknown + " rule(s) name something this game version does not have - they were KEPT, in case they belong to a mod or a newer build."
                    : ".")
                + " It is not active yet - press Use on its card.", UiaComposite.StatusKind.Good);
            UiaControlCenter.Refresh();
        }

        /// <summary>The document to encode. The ACTIVE layout is taken from the live in-memory
        /// list so an edit made a column over is in the code; anything else reads its file.</summary>
        private static StowProfileDoc BuildDocFor(string setName)
        {
            if (string.IsNullOrEmpty(setName)) return null;
            try
            {
                bool isActive = StowShared.IsActiveSet(setName);
                return new StowProfileDoc
                {
                    Name = isActive ? StowProfileStore.ActiveName : setName,
                    Description = isActive && StowProfileStore.Active != null
                        ? StowProfileStore.Active.Description : DescriptionOf(setName),
                    Profiles = isActive
                        ? new List<BagProfile>(BagProfileStore.Profiles)
                        : StowProfileStore.ProfilesOf(setName),
                };
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not assemble '" + setName + "' for export: " + e.Message);
                return null;
            }
        }

        private static string DescriptionOf(string setName)
        {
            var sets = StowShared.Sets();
            for (int i = 0; i < sets.Count; i++)
                if (string.Equals(sets[i].Name, setName, StringComparison.OrdinalIgnoreCase))
                    return sets[i].Description;
            return null;
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>Persist an ACTIVE-layout profile/rule edit — and SAY SO when it failed
        /// (a silent failure looks exactly like a save; the edit then vanishes at relaunch).</summary>
        private static void SaveActiveProfiles()
        {
            bool ok = false;
            try { ok = BagProfileStore.SaveProfiles(); } catch { }
            if (!ok)
                ProfsStatus("Could not save - your change is only in memory. See the log.",
                    UiaComposite.StatusKind.Error);
            StowShared.InvalidateSets();
            StowShared.InvalidateBrowsedProfiles();
            UiaControlCenter.Refresh();
        }

        private static List<string> ActiveProfileNames()
        {
            var list = new List<string>();
            foreach (var p in BagProfileStore.Profiles)
                if (p != null && !string.IsNullOrEmpty(p.Name)) list.Add(p.Name);
            return list;
        }

        private static BagProfile FindIn(List<BagProfile> profiles, string name)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        private static void LayoutsStatus(string msg, UiaComposite.StatusKind kind)
        {
            var st = StowShared.State; st.LayoutsMsg = msg; st.LayoutsKind = kind;
        }

        private static void BagsStatus(string msg, UiaComposite.StatusKind kind)
        {
            var st = StowShared.State; st.BagsMsg = msg; st.BagsKind = kind;
        }

        private static void ProfsStatus(string msg, UiaComposite.StatusKind kind)
        {
            var st = StowShared.State; st.ProfsMsg = msg; st.ProfsKind = kind;
        }

        private static void ShareStatus(string msg, UiaComposite.StatusKind kind)
        {
            var st = StowShared.State; st.ShareMsg = msg; st.ShareKind = kind;
        }
    }
}
