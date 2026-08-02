using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>Smart storage. As of SmartStow B3 this tab is a HOST for four sub-tabs rather than
    /// one 40-row scroll column (redesign plan §5, option 1 — a sub-tab strip inside Storage, not a
    /// 7th top-level tab, which would have left 18px of slack in the tab bar):
    ///
    /// <list type="bullet">
    /// <item><b>Bags</b> — the card grid: one card per assignable container ON YOU, with its live
    /// thumbnail (a painted bag shows its paint), name + occurrence ordinal, its Bag Profile
    /// dropdown, the "never stow into this" switch and a manual rename box.</item>
    /// <item><b>Bag Profiles</b> — the rule editor (items / UIA classes / categories / slot classes,
    /// each with a High/Normal/Low priority), profile CRUD and the share in/out gestures.</item>
    /// <item><b>Stow Profiles</b> — which named folder of Bag Profiles is active (B2's model).</item>
    /// <item><b>Settings</b> — the Universal Inventory knobs, the SmartStow+ chain and the read-only
    /// test box. The plan's separate [Routing]/[Grid] split collapsed into this one page, and
    /// nothing here is called "the Grid" — the term is "Universal Inventory" everywhere.</item>
    /// </list>
    ///
    /// <para>Bag profiles live inside the ACTIVE Stow Profile; assignments are per save. Per design
    /// O6 this tab is the POWER ROOM: per-rule priority tri-state and a TEST BOX that dry-runs the
    /// StowRouter to show where a picked item WOULD go and why (nothing ever moves).</para>
    ///
    /// <para>The ONLY game-state mutation reachable from this tab is the labeller-funnel rename
    /// (<see cref="ItemActions.RenameThing"/>) — everything else writes config XML.</para></summary>
    public sealed class StorageTab : IUiaTab
    {
        public string Title => "Storage";

        // ---- sub-tabs (redesign plan §5) ----
        private const int SubBags = 0;
        private const int SubBagProfiles = 1;
        private const int SubStowProfiles = 2;
        private const int SubSettings = 3;
        private static readonly string[] SubTabTitles = { "Bags", "Bag Profiles", "Stow Profiles", "Settings" };
        // STATIC so the chosen sub-tab survives a theme Restyle (which throws the tab INSTANCES
        // away and builds new ones); reset in ResetCaches with the rest of the statics.
        private static int _subTab = SubBags;

        private int _selected;
        // The page's scroll view, kept so a SUB-TAB switch can send it to the top first:
        // UiaControlCenter.Refresh always PRESERVES the scroll fraction (right for an in-page
        // gesture, wrong when the whole page underneath changes).
        private ScrollRect _scroll;
        // The test box's picked item (PrefabName). Survives Refresh (tab instances persist);
        // resets on a theme Restyle, which is fine — it is a transient diagnostic, not state.
        private string _testPrefab;
        // One-line result notes shown under their sections after a gesture (export/reload/import
        // profiles, switch Stow Profile). Same lifetime story as _testPrefab: they survive
        // Refresh, reset on a theme Restyle — transient feedback, not state.
        private string _profileNote;
        private string _stowNote;
        // Assignment feedback (typed-pack validation warning / "that bag went away") and rename
        // feedback. Same transient lifetime as the notes above.
        private string _assignNote;
        private string _renameNote;
        // Profile CRUD scratch: the rename field's live text and the two-click delete arm.
        // Both reset when the selected profile changes (a new target invalidates a typed name
        // and an armed confirm alike) — the ProfilesTab confirm idiom, adapted.
        private string _renameField;
        private bool _confirmDeleteProfile;
        // Stow Profile name listing + worn-bag scratch. STATIC (tab instances are recreated by a
        // theme Restyle) so a restyle-driven Build can reuse the last gesture-built data instead of
        // re-reading StowProfiles/*.xml from disk and re-scanning the inventory up to ~7x/s during
        // an F9 colour-wheel drag (verified perf finding, 2026-07-20). Every NON-restyle Build
        // refills both from the source of truth, so user gestures always see fresh data.
        // ResetCaches (called from UiaControlCenter.Shutdown) drops the Thing refs on teardown.
        private static readonly List<string> _stowScratch = new List<string>();
        private static bool _stowScratchValid;
        private static readonly List<DynamicThing> _bagScratch = new List<DynamicThing>();
        private static bool _bagScratchValid;

        // Bag-card thumbnails, keyed on ReferenceId (per bag, so two differently painted backpacks
        // never share one sprite — plan §6). Cleared at the start of every NON-restyle Bags build,
        // so repainting a bag shows up on the next gesture while an F9 colour drag still reuses the
        // sprites it already has. UGUI Sprites, not ImGui texture ids — caching is correct here.
        private static readonly Dictionary<long, Sprite> _thumbs = new Dictionary<long, Sprite>();
        // Per-card manual-rename text, keyed on ReferenceId, so a typed name survives the tab
        // rebuild that every other gesture triggers.
        private static readonly Dictionary<long, string> _renameFields = new Dictionary<long, string>();

        /// <summary>Hot-reload/menu teardown: drop the restyle-scoped caches (they hold
        /// DynamicThing references and Sprites into the live world) and the sub-tab choice.</summary>
        public static void ResetCaches()
        {
            _stowScratch.Clear();
            _stowScratchValid = false;
            _bagScratch.Clear();
            _bagScratchValid = false;
            _thumbs.Clear();
            _renameFields.Clear();
            _subTab = SubBags;
            // B3's config key is bound lazily from a static in Features; this is the only
            // mod-teardown path that reaches it (UiaControlCenter.Shutdown is called exactly once,
            // from the plugin's own teardown), so drop the entry reference here. Re-binding is
            // idempotent - ConfigFile.Bind hands back the existing entry.
            StowRenameConfig.Reset();
        }

        // The shipped "good default" category set: profile name -> SortingClass it catches.
        private static readonly string[][] Recommended =
        {
            new[] { "Tools", "Tools" }, new[] { "Resources", "Resources" }, new[] { "Kits", "Kits" },
            new[] { "Food", "Food" }, new[] { "Clothing", "Clothing" }, new[] { "Atmospherics", "Atmospherics" },
            new[] { "Storage", "Storage" }, new[] { "Ores", "Ores" }, new[] { "Ices", "Ices" },
            new[] { "Appliances", "Appliances" },
        };

        // Priority tri-state (design O6): the UI face of RuleTiers. High first because that is
        // what the eye scans for; the list is shared read-only across every rule-row dropdown.
        private static readonly List<string> TierOptions = new List<string> { "High", "Normal", "Low" };

        // ---------- host ----------

        public void Build(RectTransform content, bool advanced)
        {
            // One wrapper child carries the layout. `content` is the Control Center's REUSED content
            // area — only its CHILDREN are destroyed between builds — so a layout group must never
            // be added to it directly, or every rebuild would stack another component on it.
            var wrapGo = UiaUi.Go("storage", content);
            var wrap = (RectTransform)wrapGo.transform;
            UiaUi.Fill(wrap);
            UiaUi.VLayout(wrap, 6f);

            BuildSubTabStrip(wrap);

            // The strip stays put; only the page below it scrolls.
            var bodyGo = UiaUi.Go("subbody", wrap);
            UiaUi.Size(bodyGo, flexH: 1f);
            ScrollRect scroll;
            var col = UiaUi.ScrollView(bodyGo.transform, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);
            _scroll = scroll;

            switch (_subTab)
            {
                case SubBagProfiles: BuildBagProfilesPage(col, advanced); break;
                case SubStowProfiles: BuildStowProfilesPage(col); break;
                case SubSettings: BuildSettingsPage(col, advanced); break;
                default: BuildBagsPage(col, advanced); break;
            }
        }

        private void BuildSubTabStrip(Transform parent)
        {
            var strip = UiaUi.Go("substrip", parent);
            UiaUi.Size(strip, 28f);
            UiaUi.HLayout((RectTransform)strip.transform, 4f);
            for (int i = 0; i < SubTabTitles.Length; i++)
            {
                int idx = i;
                var btn = UiaControls.Button(strip.transform, SubTabTitles[i],
                    () =>
                    {
                        if (_subTab == idx) return;
                        _subTab = idx;
                        // A new page starts at the top; Refresh would otherwise carry the outgoing
                        // page's scroll fraction over to a completely different screen.
                        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
                        UiaControlCenter.Refresh();
                    },
                    140f, 28f);
                btn.SetSelected(i == _subTab);
            }
        }

        // ---------- page 1: BAGS ----------

        /// <summary>The card screen (redesign plan §6, option 1A): one card per assignable container
        /// the player is CARRYING (FlorpyDorp Q5 — on-player only), identified by its own live
        /// thumbnail instead of a text row. Advanced swaps the cards for the dense list (option 1B),
        /// exactly as the plan recommends — same data, ten of them on screen at once.</summary>
        private void BuildBagsPage(Transform col, bool advanced)
        {
            // A theme restyle repaints; it does not need fresh sprites. Every real gesture does.
            if (!UiaControlCenter.IsRestyling) _thumbs.Clear();

            var bags = WornBags();
            var profNames = ProfileNames();

            UiaControls.Header(col, "Bags");

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

            var topGo = UiaUi.Go("bagtop", col);
            UiaUi.Size(topGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)topGo.transform, UiaTheme.Gap);
            var status = UiaUi.Text(topGo.transform,
                bags.Count + (bags.Count == 1 ? " container on you - " : " containers on you - ")
                    + withProfile + " with a profile",
                UiaTheme.SmallSize, UiaTheme.TextMute, TextAlignmentOptions.Left);
            status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UiaControls.Button(topGo.transform, "Refresh", RefreshBags, 100f, UiaTheme.RowH);

            // The rename-on-assign switch lives HERE, next to the gesture it changes, not buried in
            // Settings. See StowRenameConfig for why the key is not bound in UIAConfig.
            if (StowRenameConfig.Available)
            {
                UiaControls.ToggleRow(col, "Rename a bag when a profile is assigned",
                    StowRenameConfig.RenameOnAssign,
                    v => { StowRenameConfig.RenameOnAssign = v; UiaControlCenter.Refresh(); });
                UiaControls.Note(col, "On: assigning a Bag Profile also LABELS that container with the profile's name - the same authoritative rename a Labeller performs, so in multiplayer everyone sees it and it is kept in the save. Only containers you are carrying are ever renamed. Clearing a profile never renames anything back: use a card's Rename box for that.");
            }
            else
            {
                UiaControls.Note(col, "Rename-on-assign is unavailable right now (settings are not loaded yet).");
            }

            if (bags.Count == 0)
            {
                UiaControls.Note(col, "Nothing here yet - you are not carrying a container that can hold a Bag Profile. Backpacks, mining belts and mining backpacks, cardboard boxes and crates can; plain tool belts, jetpacks, suits, tools that happen to have slots, and packaging (cereal boxes, supply boxes) cannot. Pick one up and press Refresh.");
            }
            else if (advanced)
            {
                UiaControls.Note(col, "Compact list (Advanced density). Switch to Simple for the cards with thumbnails.");
                BuildBagRows(col, bags, profNames);
            }
            else
            {
                BuildBagCards(col, bags, profNames);
            }

            // Design O6: keep the ZERO-SETUP tier discoverable rather than magic. This used to be a
            // per-bag "(implicit - routes by contents)" sub-row; as a card that would cost a whole
            // row on every unprofiled bag to say the same sentence, so it is stated once, here, and
            // only while it is actually true.
            if (bags.Count > withProfile && UIAConfig.StowUseAffinity.Value)
                SubNote(col, "A container with no profile is not ignored: Smart Stow still routes to it by what is already inside (step 4 on the Settings tab).");
            if (anyForeign)
                SubNote(col, "A container above is mapped to a bag profile from another Stow Profile. It is kept, not routed - switch back to that Stow Profile to use it, or pick a new profile here to replace it.");
            if (!string.IsNullOrEmpty(_assignNote)) SubNote(col, _assignNote);
            if (!string.IsNullOrEmpty(_renameNote)) SubNote(col, _renameNote);

            // Loadouts stay retired here (FlorpyDorp Q4): a Stow Profile IS "a saved group of bags",
            // so the second concept went away rather than sitting beside it. The Loadouts/*.xml files
            // stay on disk and LoadoutStore still compiles and still receives the profile-rename
            // cascade - only the UI and the apply gesture are gone (hide, never destroy). The same
            // ruling retired the per-bag "All <bag>s" prefab-default switch: every container is
            // mapped by hand, one at a time, on this screen.
        }

        /// <summary>Occurrence totals per prefab, so "#2" only appears when the same kind of
        /// container really does show up more than once. This is <c>LoadoutEntry.Occurrence</c>
        /// made visible — the same per-prefab, first-seen numbering the enumeration already uses.</summary>
        private static Dictionary<string, int> PrefabTotals(List<DynamicThing> bags)
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bags.Count; i++)
            {
                var b = bags[i];
                if (b == null) continue;
                string p = SafePrefab(b);
                int n;
                totals.TryGetValue(p, out n);
                totals[p] = n + 1;
            }
            return totals;
        }

        private void BuildBagCards(Transform col, List<DynamicThing> bags, List<string> profNames)
        {
            var shared = new List<string> { "(no profile)" };
            shared.AddRange(profNames);

            var gridGo = UiaUi.Go("bag-grid", col);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            // Same 298 width as the HUD theme cards (ProfilesTab) so the two card screens read as
            // one product; taller to carry the dropdown, the switch and the rename row.
            grid.cellSize = new Vector2(298f, 254f);
            grid.spacing = new Vector2(UiaTheme.Gap, UiaTheme.Gap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            var fit = gridGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var totals = PrefabTotals(bags);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bags.Count; i++)
            {
                var b = bags[i];
                if (b == null) continue;
                string p = SafePrefab(b);
                int n;
                seen.TryGetValue(p, out n);
                seen[p] = n + 1;
                int total;
                totals.TryGetValue(p, out total);
                BagCard(gridGo.transform, b, total > 1 ? n + 1 : 0, shared, profNames);
            }
        }

        private void BagCard(Transform parent, DynamicThing bag, int ordinal,
            List<string> shared, List<string> profNames)
        {
            var b = bag;
            long id = SafeRef(b);
            bool excluded = false;
            try { excluded = BagProfileStore.IsStowExcluded(b); } catch { }

            var card = UiaUi.Go("bagcard", parent);
            var cimg = card.AddComponent<Image>();
            cimg.color = UiaTheme.PanelRaised;
            UiaImages.Round(cimg);
            UiaUi.OutlineOf(cimg, excluded ? UiaTheme.Warn : UiaTheme.Divider, excluded ? 2f : 1f);
            UiaUi.VLayout((RectTransform)card.transform, 3f, 6, 6, 6, 6);

            // --- the LIVE thumbnail: Thing.GetThumbnail() returns the COLOURED variant for a
            // painted bag (Thing.cs:1542), which is exactly why the cards exist. flexibleHeight so
            // it absorbs whatever the fixed rows below do not use.
            var thumbGo = UiaUi.Go("thumb", card.transform);
            UiaUi.Size(thumbGo, 68f, -1f, -1f, 1f);
            var img = thumbGo.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var sp = Thumb(b);
            if (sp != null)
            {
                img.sprite = sp;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(0.10f, 0.14f, 0.18f, 1f);
                var ph = UiaUi.Text(thumbGo.transform, "no icon", 11f, UiaTheme.TextMute, TextAlignmentOptions.Center);
                UiaUi.Fill((RectTransform)ph.transform);
            }

            // --- name (+ occurrence ordinal, only when this kind of bag repeats) ---
            var nameGo = UiaUi.Go("name", card.transform);
            UiaUi.Size(nameGo, 20f);
            var nameT = nameGo.AddComponent<TextMeshProUGUI>();
            nameT.font = UiaTheme.Font();
            nameT.fontSize = 15f;
            nameT.raycastTarget = false;
            nameT.color = excluded ? UiaTheme.Warn : UiaTheme.Text;
            nameT.alignment = TextAlignmentOptions.Center;
            nameT.overflowMode = TextOverflowModes.Ellipsis;
            nameT.enableWordWrapping = false;
            nameT.text = ordinal > 0 ? SafeName(b) + "  #" + ordinal : SafeName(b);

            // --- what it IS and how full it is. The TYPE line is always the prefab's own name, so
            // a bag that has been renamed (by us or by a labeller) is still identifiable.
            int slots = 0, used = 0;
            try
            {
                if (b.Slots != null)
                {
                    slots = b.Slots.Count;
                    for (int i = 0; i < b.Slots.Count; i++)
                    {
                        Slot s = b.Slots[i];
                        if (s != null && s.Get() != null) used++;
                    }
                }
            }
            catch { }
            var subGo = UiaUi.Go("sub", card.transform);
            UiaUi.Size(subGo, 15f);
            var subT = subGo.AddComponent<TextMeshProUGUI>();
            subT.font = UiaTheme.Font();
            subT.fontSize = 11f;
            subT.raycastTarget = false;
            subT.color = UiaTheme.TextMute;
            subT.alignment = TextAlignmentOptions.Center;
            subT.overflowMode = TextOverflowModes.Ellipsis;
            subT.enableWordWrapping = false;
            subT.text = TypeName(b) + " - " + slots + " slots, " + used + " used";

            // --- the Bag Profile dropdown (carrying B2's inert "kept from another Stow Profile" row)
            int idx, foreignIdx;
            var opts = BagOptions(b, profNames, shared, out idx, out foreignIdx);
            int fi = foreignIdx;
            InlineDropdown(card.transform, opts, idx,
                i =>
                {
                    if (i == fi) return;   // re-selecting the kept mapping changes nothing
                    AssignToBag(b, i <= 0 ? null : profNames[i - 1]);
                }, 286f);

            // --- FlorpyDorp Q5: "never smart-stow into this container" ---
            UiaControls.ToggleRow(card.transform, "Never stow into this", excluded,
                v => SetExcluded(b, v));

            // --- manual rename, through the same labeller funnel as rename-on-assign ---
            var rrow = UiaUi.Go("rename", card.transform);
            UiaUi.Size(rrow, 26f);
            UiaUi.HLayout((RectTransform)rrow.transform, 4f);
            var fieldGo = UiaUi.Go("renamehost", rrow.transform);
            UiaUi.Size(fieldGo, 26f, -1f, 1f);
            UiaUi.HLayout((RectTransform)fieldGo.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);
            var input = UiaUi.InputField(fieldGo.transform, "Rename this bag...", v => SetRenameField(id, v));
            string held = GetRenameField(id);
            if (!string.IsNullOrEmpty(held)) input.text = held;
            UiaControls.Button(rrow.transform, "Rename", () => RenameBag(b), 76f, 26f);
        }

        /// <summary>The dense variant (plan §6 option 1B) behind the existing Advanced density
        /// toggle: everything a card offers, one 30px row per bag, no thumbnail.</summary>
        private void BuildBagRows(Transform col, List<DynamicThing> bags, List<string> profNames)
        {
            var shared = new List<string> { "(no profile)" };
            shared.AddRange(profNames);
            var totals = PrefabTotals(bags);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int n = 0; n < bags.Count; n++)
            {
                var b = bags[n];
                if (b == null) continue;
                long id = SafeRef(b);
                string p = SafePrefab(b);
                int c;
                seen.TryGetValue(p, out c);
                seen[p] = c + 1;
                int total;
                totals.TryGetValue(p, out total);
                bool excluded = false;
                try { excluded = BagProfileStore.IsStowExcluded(b); } catch { }

                var row = UiaUi.Go("bagrow", col);
                UiaUi.Size(row, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)row.transform, 6f);

                var label = UiaUi.Text(row.transform,
                    total > 1 ? SafeName(b) + " #" + (c + 1) : SafeName(b),
                    UiaTheme.LabelSize, excluded ? UiaTheme.Warn : UiaTheme.Text, TextAlignmentOptions.Left);
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.enableWordWrapping = false;
                var lle = label.gameObject.AddComponent<LayoutElement>();
                lle.flexibleWidth = 1f;
                lle.minWidth = 90f;

                var fieldGo = UiaUi.Go("renamehost", row.transform);
                UiaUi.Size(fieldGo, 26f, -1f, 1f, -1f, 90f);
                UiaUi.HLayout((RectTransform)fieldGo.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);
                var input = UiaUi.InputField(fieldGo.transform, "Rename...", v => SetRenameField(id, v));
                string held = GetRenameField(id);
                if (!string.IsNullOrEmpty(held)) input.text = held;
                UiaControls.Button(row.transform, "Rename", () => RenameBag(b), 76f, 26f);

                int idx, foreignIdx;
                var opts = BagOptions(b, profNames, shared, out idx, out foreignIdx);
                int fi = foreignIdx;
                InlineDropdown(row.transform, opts, idx,
                    i =>
                    {
                        if (i == fi) return;
                        AssignToBag(b, i <= 0 ? null : profNames[i - 1]);
                    }, 150f);

                var excl = UiaUi.Text(row.transform, "No stow", 11f, UiaTheme.TextMute, TextAlignmentOptions.Right);
                UiaUi.Size(excl.gameObject, UiaTheme.RowH, 54f, flexW: 0f);
                UiaControls.Switch(row.transform, excluded, v => SetExcluded(b, v));
            }
        }

        /// <summary>The option list for one bag's profile dropdown. When the stored assignment names
        /// a Bag Profile that lives in ANOTHER Stow Profile, showing "(no profile)" would be a lie
        /// the player then acts on (picking any row rewrites the assignment and destroys the
        /// cross-set mapping the model promised to keep) — so that name is appended as its own,
        /// selected, INERT row instead.</summary>
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
            opts.Add(cur + " (not in this Stow Profile)");
            foreignIndex = opts.Count - 1;
            index = foreignIndex;
            return opts;
        }

        private void RefreshBags()
        {
            _bagScratchValid = false;
            _thumbs.Clear();
            _assignNote = null;
            _renameNote = null;
            UiaControlCenter.Refresh();
        }

        /// <summary>Session thumbnail cache keyed on ReferenceId (per BAG, not per prefab — two
        /// backpacks painted differently must not share one sprite). Only successful fetches are
        /// stored, so a thumbnail the game has not generated yet retries on the next build.</summary>
        private static Sprite Thumb(DynamicThing bag)
        {
            long id = SafeRef(bag);
            Sprite sp;
            if (id != 0 && _thumbs.TryGetValue(id, out sp)) return sp;
            sp = null;
            try { sp = bag.GetThumbnail(); } catch { }
            if (sp != null && id != 0) _thumbs[id] = sp;
            return sp;
        }

        private static string GetRenameField(long id)
        {
            string v;
            return (id != 0 && _renameFields.TryGetValue(id, out v)) ? v : null;
        }

        private static void SetRenameField(long id, string v)
        {
            if (id == 0) return;
            if (string.IsNullOrEmpty(v)) _renameFields.Remove(id);
            else _renameFields[id] = v;
        }

        // ---------- page 2: BAG PROFILES ----------

        private void BuildBagProfilesPage(Transform col, bool advanced)
        {
            var profNames = ProfileNames();

            // ---- one-click setup ----
            UiaControls.Header(col, "Quick setup");
            UiaControls.Note(col, "Create a ready-made set of category profiles (Tools, Resources, Food...) inside the active Stow Profile, then map them to your containers on the Bags tab. Mapping is always a choice you make - nothing is assigned for you.");
            var qsGo = UiaUi.Go("qs", col);
            UiaUi.Size(qsGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)qsGo.transform, UiaTheme.Gap);
            UiaControls.Button(qsGo.transform, "Create recommended profiles", () => { EnsureRecommended(); Save(); }, 240f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);

            // ---- profile editor ----
            UiaControls.Header(col, "Edit a profile");
            if (profNames.Count == 0)
            {
                UiaControls.Note(col, "No profiles yet - use \"Create recommended profiles\" above, or add one.");
            }
            else
            {
                _selected = Mathf.Clamp(_selected, 0, profNames.Count - 1);
                // Resolve by NAME, not positional index: ProfileNames() filters empty-named
                // profiles, so the dropdown index and the Profiles list index can otherwise desync.
                string selName = profNames[_selected];
                BagProfile profile = BagProfileStore.FindProfile(selName);

                var pickGo = UiaUi.Go("pick", col);
                UiaUi.Size(pickGo, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)pickGo.transform, UiaTheme.Gap);
                var ddHost = UiaUi.Go("ddh", pickGo.transform);
                UiaUi.Size(ddHost, UiaTheme.RowH, flexW: 1f);
                // ddHost is a flex SLOT in pickGo's row, but it needs its own layout group to
                // stretch the DropdownRow inside it to the slot width — without this the dropdown
                // row keeps its zero default size and the field collapses to just the caret.
                UiaUi.HLayout((RectTransform)ddHost.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);
                UiaControls.DropdownRow(ddHost.transform, "Profile", profNames, _selected,
                    i =>
                    {
                        _selected = i;
                        _renameField = null;            // a new target invalidates a typed name
                        _confirmDeleteProfile = false;  // ...and any armed confirm
                        UiaControlCenter.Refresh();
                    });
                UiaControls.Button(pickGo.transform, "New", NewProfile, 70f, UiaTheme.RowH);
                if (profile != null)
                    UiaControls.Button(pickGo.transform, "Export", () => ExportProfile(selName), 80f, UiaTheme.RowH);

                if (profile != null)
                {
                    ManageProfileRow(col, selName);
                    RuleList(col, profile);
                    AddControls(col, profile);
                }
            }

            // ---- sharing (design O5c) — available even with zero profiles (the import path) ----
            if (!string.IsNullOrEmpty(_profileNote)) SubNote(col, _profileNote);
            UiaControls.Header(col, "Share");
            var shGo = UiaUi.Go("share", col);
            UiaUi.Size(shGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)shGo.transform, UiaTheme.Gap);
            UiaControls.Button(shGo.transform, "Reload profiles", ReloadProfiles, 150f, UiaTheme.RowH);
            UiaControls.Button(shGo.transform, "Import shared profiles", ImportShared, 200f, UiaTheme.RowH);
            UiaControls.Note(col, "Export writes the selected profile to its own file in the Profiles folder. Drop a received profile file in that same folder, then press Import shared profiles to add it to the active Stow Profile (a name you already have is replaced).");
        }

        // ---------- page 3: STOW PROFILES ----------

        private void BuildStowProfilesPage(Transform col)
        {
            BuildStowProfileBlock(col);
        }

        // ---------- page 4: SETTINGS ----------

        /// <summary>Everything that is a knob rather than a thing: the Universal Inventory options,
        /// the SmartStow+ chain, and the read-only router dry-run. The plan's separate
        /// [Routing]/[Grid] sub-tabs collapse here — two half-empty pages were worse than one honest
        /// one — and per FlorpyDorp's directive NOTHING on this page is called "the Grid".</summary>
        private void BuildSettingsPage(Transform col, bool advanced)
        {
            // ---- Universal Inventory (moved here from the Radial tab in the post-0.9.2.5 play-test round — FlorpyDorp:
            // "it has nothing to do with the radials". Verbatim controls, new home.) ----
            UiaControls.Header(col, "Universal Inventory");
            // Layout mode selector removed: the flat pack renderer is now the only one.
            UiaControls.SliderRow(col, "Cell size", 28f, 80f, UIAConfig.GridCellSize.Value,
                // global:: because the plugin CLASS shares the root namespace's name (same reason
                // BumpGridChrome below spells it out).
                v => { UIAConfig.GridCellSize.Value = v; global::StationeersUIMod.UI.Grid.TheGridPanel.Relayout(); }, "0", 1f);
            // Scroll-select / keyboard nav (the vanilla-style wheel-through-your-inventory feature).
            // GridSelection reads UIAConfig.GridKeyboardNav live every interactive frame, so the flip
            // takes effect without a rebuild.
            UiaControls.ToggleRow(col, "Scroll to select (mouse wheel through your inventory)",
                UIAConfig.GridKeyboardNav.Value, v => UIAConfig.GridKeyboardNav.Value = v);
            UiaControls.Note(col, "While the inventory is open during play, the mouse wheel moves a highlight " +
                "through your bags and slots. F takes the highlighted item or places your held item into an empty " +
                "highlighted cell; freeing the mouse switches the wheel to panning the window instead.");
            // Live toggles: badges refresh via GridProfileMode.ChromeStamp (folds the config
            // bit), hints are re-read by GridGhostHint.Tick — no extra plumbing needed.
            UiaControls.ToggleRow(col, "Show profile tags on bag tabs",
                UIAConfig.GridProfileBadges.Value, v => UIAConfig.GridProfileBadges.Value = v);
            UiaControls.ToggleRow(col, "Glow the bag Smart Stow would pick while dragging",
                UIAConfig.GridGhostHints.Value, v => UIAConfig.GridGhostHints.Value = v);

            // ---- SmartStow+ (numbering mirrors the router chain order after belt tools) ----
            UiaControls.Header(col, "Smart Stow (G)");
            UiaControls.ToggleRow(col, "Enable SmartStow+", UIAConfig.SmartStowPlusEnabled.Value, v => UIAConfig.SmartStowPlusEnabled.Value = v);
            UiaControls.ToggleRow(col, "1 - Prefer topping up matching stacks", UIAConfig.StowPreferStacks.Value, v => UIAConfig.StowPreferStacks.Value = v);
            UiaControls.ToggleRow(col, "2 - Route components to their sockets", UIAConfig.StowSocketPriority.Value, v => UIAConfig.StowSocketPriority.Value = v);
            UiaControls.ToggleRow(col, "3 - Use bag profiles", UIAConfig.StowUseProfiles.Value, v => UIAConfig.StowUseProfiles.Value = v);
            UiaControls.ToggleRow(col, "4 - Route to bags with similar contents", UIAConfig.StowUseAffinity.Value, v => UIAConfig.StowUseAffinity.Value = v);
            UiaControls.ToggleRow(col, "5 - Known bag types get a default", UIAConfig.StowUseBagTypeDefaults.Value, v => UIAConfig.StowUseBagTypeDefaults.Value = v);
            UiaControls.ToggleRow(col, "6 - Remember where each type went", UIAConfig.StowUseTypeMemory.Value, v => UIAConfig.StowUseTypeMemory.Value = v);
            UiaControls.ToggleRow(col, "7 - Loose build items go to one bag", UIAConfig.StowGenericFallback.Value, v => UIAConfig.StowGenericFallback.Value = v);
            UiaControls.SliderRow(col, "How deep to search (levels)", 1f, 5f, UIAConfig.ScanDepth.Value, v => UIAConfig.ScanDepth.Value = Mathf.RoundToInt(v), "0", 1f);
            if (advanced)
            {
                UiaControls.ToggleRow(col, "Stow tools onto the toolbelt first", UIAConfig.StowToolsToToolbeltFirst.Value, v => UIAConfig.StowToolsToToolbeltFirst.Value = v);
                UiaControls.ToggleRow(col, "Allow nested-bag stow", UIAConfig.StowIntoNestedBags.Value, v => UIAConfig.StowIntoNestedBags.Value = v);
                UiaControls.ToggleRow(col, "Sort lists profile items first", UIAConfig.StowProfileSort.Value, v => UIAConfig.StowProfileSort.Value = v);
                UiaControls.ToggleRow(col, "Show where items were stowed (and why)", UIAConfig.StowToastEnabled.Value, v => UIAConfig.StowToastEnabled.Value = v);
            }
            int excluded = 0;
            try { excluded = BagProfileStore.ExcludedCount; } catch { }
            if (excluded > 0)
                SubNote(col, excluded + " container(s) in this save are set to \"never stow into this\" (Bags tab).");

            // ---- test box (design O6): the router dry-run, read-only by construction ----
            UiaControls.Header(col, "Test box");
            UiaControls.Note(col, "Dry-run the Smart Stow router: pick an item and see which bag would take it, and why. This is a preview only - nothing ever moves.");
            var tbGo = UiaUi.Go("tb", col);
            UiaUi.Size(tbGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)tbGo.transform, UiaTheme.Gap);
            UiaControls.Button(tbGo.transform, "Test an item...", () =>
                UiaItemPicker.Open(prefab => { _testPrefab = prefab; }, () => UiaControlCenter.Refresh(), "Test an item", "Testing"),
                150f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
            if (!string.IsNullOrEmpty(_testPrefab))
                UiaControls.Button(tbGo.transform, "Clear", () => { _testPrefab = null; UiaControlCenter.Refresh(); }, 70f, UiaTheme.RowH);
            if (!string.IsNullOrEmpty(_testPrefab))
                TestResults(col);
        }

        // ---------- Stow Profiles (SmartStow B2) ----------

        /// <summary>The MINIMAL surface for the new model, in the existing row idiom: which Stow
        /// Profile is active, a dropdown to switch, and two ways to make another one. The card-based
        /// manager (browse an inactive set, copy/move Bag Profiles between sets, rename/delete) is
        /// B4 — the model operations it will call
        /// (<see cref="StowProfileStore.CopyProfileTo"/>/<see cref="StowProfileStore.MoveProfileTo"/>)
        /// already exist and are covered by the `stowprofiles` console dump in the meantime.
        ///
        /// <para>Switching writes the marker file and re-runs <see cref="BagProfileStore.LoadProfiles"/>
        /// — one load path for launch and for switching. Per-save ASSIGNMENTS are untouched by a
        /// switch: they name profiles, so a name the new set also has keeps working, and one it does
        /// not have reads as unassigned until you switch back (nothing is deleted).</para></summary>
        private void BuildStowProfileBlock(Transform col)
        {
            UiaControls.Header(col, "Stow Profile");
            UiaControls.Note(col, "A Stow Profile is a named folder of bag profiles. One is active at a time - only its profiles can be mapped to your containers. Switching keeps every mapping you made: a name the new Stow Profile also has keeps working, one it does not have simply goes quiet until you switch back.");

            // Disk IO only on gesture-driven builds; a theme Restyle reuses the last listing.
            if (!(UiaControlCenter.IsRestyling && _stowScratchValid))
            {
                _stowScratch.Clear();
                _stowScratch.AddRange(StowProfileStore.ListNames());
                _stowScratchValid = true;
            }

            string active = StowProfileStore.ActiveName;
            if (!StowProfileStore.Available || _stowScratch.Count == 0)
            {
                UiaControls.Note(col, "Stow Profiles are not available right now - bag profiles are coming from the old Profiles folder instead. Check the log; nothing was lost.");
                return;
            }

            int idx = Mathf.Max(0, _stowScratch.IndexOf(active));
            var row = UiaUi.Go("stowpick", col);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);
            var ddHost = UiaUi.Go("ddh", row.transform);
            UiaUi.Size(ddHost, UiaTheme.RowH, flexW: 1f);
            UiaUi.HLayout((RectTransform)ddHost.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);
            UiaControls.DropdownRow(ddHost.transform, "Active", _stowScratch, idx, i => SwitchStowProfile(i));
            UiaControls.Button(row.transform, "New", () => NewStowProfile(false), 70f, UiaTheme.RowH);
            UiaControls.Button(row.transform, "Duplicate", () => NewStowProfile(true), 100f, UiaTheme.RowH);
            if (!string.IsNullOrEmpty(_stowNote)) SubNote(col, _stowNote);
            UiaControls.Note(col, "Browsing an inactive Stow Profile, and copying or moving single bag profiles between them, is the next step (B4). Until then the console command 'stowprofiles' prints what every Stow Profile contains.");
        }

        private void SwitchStowProfile(int index)
        {
            if (index < 0 || index >= _stowScratch.Count) return;
            string want = _stowScratch[index];
            if (string.Equals(want, StowProfileStore.ActiveName, StringComparison.Ordinal)) return;
            if (!StowProfileStore.SetActive(want))
            {
                _stowNote = "Could not switch to \"" + want + "\" (see the log).";
                UiaControlCenter.Refresh();
                return;
            }
            BagProfileStore.LoadProfiles();   // one load path: re-resolve everything from disk
            _selected = 0;
            _renameField = null;
            _confirmDeleteProfile = false;
            _stowScratchValid = false;
            _stowNote = "Now using \"" + StowProfileStore.ActiveName + "\" ("
                + BagProfileStore.Profiles.Count + " bag profile(s)).";
            BumpGridChrome();                 // chips/badges follow the new profile set
            UiaControlCenter.Refresh();
        }

        private void NewStowProfile(bool duplicate)
        {
            string baseName = duplicate
                ? ((StowProfileStore.ActiveName ?? "Stow Profile") + " copy")
                : "Stow Profile";
            string made = StowProfileStore.Create(baseName, duplicate);
            _stowNote = made == null
                ? "Could not create a new Stow Profile (see the log)."
                : "Created \"" + made + "\". Pick it above to switch to it.";
            _stowScratchValid = false;
            UiaControlCenter.Refresh();
        }

        // ---------- profile sharing (design O5c) ----------

        private void ExportProfile(string profileName)
        {
            string file = BagProfileStore.ExportProfile(profileName);
            _profileNote = file != null
                ? "Exported to Profiles/" + file
                : "Export failed (see the log).";
            UiaControlCenter.Refresh();
        }

        private void ReloadProfiles()
        {
            BagProfileStore.LoadProfiles();   // re-reads the ACTIVE Stow Profile from disk
            _profileNote = "Profiles reloaded (" + BagProfileStore.Profiles.Count + " loaded).";
            _stowScratchValid = false;
            BumpGridChrome();                 // names/badges may have changed on an open Grid
            UiaControlCenter.Refresh();
        }

        /// <summary>Pull any stand-alone profile file sitting in the Profiles folder into the ACTIVE
        /// Stow Profile. This is the receiving half of the share flow: that folder is no longer
        /// loaded at launch (the Stow Profile is), so importing is now a deliberate gesture instead
        /// of a silent side effect of every start-up.</summary>
        private void ImportShared()
        {
            var added = new List<string>();
            var replaced = new List<string>();
            int stale;
            int files = BagProfileStore.ImportSharedProfiles(added, replaced, out stale);
            if (files == 0)
            {
                _profileNote = "No shared profile files found in the Profiles folder.";
            }
            else if (added.Count == 0 && replaced.Count == 0)
            {
                _profileNote = stale > 0
                    ? "Nothing imported: " + stale + " file(s) in the Profiles folder are older than what you already have."
                    : "Nothing to import (" + files + " file(s) read, no profiles in them).";
            }
            else
            {
                // NAME what changed. "2 updated" is not something a player can check; "replaced:
                // Ores, Tools" is - and an import that overwrites a profile is exactly the moment
                // they need to be able to check it.
                string note = "";
                if (added.Count > 0) note += "Added: " + string.Join(", ", added.ToArray()) + ". ";
                if (replaced.Count > 0) note += "Replaced: " + string.Join(", ", replaced.ToArray()) + ". ";
                if (stale > 0) note += stale + " older copy/copies skipped. ";
                _profileNote = note + "(into \"" + (StowProfileStore.ActiveName ?? "?") + "\")";
            }
            BumpGridChrome();
            UiaControlCenter.Refresh();
        }

        /// <summary>Nudge the Universal Inventory's profile chrome (chips, tab badges) after a
        /// profile/assignment change made from THIS tab, so an open Grid repaints without waiting
        /// for a structural change. Fail-soft — a missing Grid never breaks the tab.</summary>
        private static void BumpGridChrome()
        {
            // global:: because the plugin CLASS shares the root namespace's name.
            try { global::StationeersUIMod.UI.Grid.GridProfileMode.BumpVersion(); } catch { }
        }

        private static void SubNote(Transform col, string text)
        {
            var go = UiaUi.Go("subnote", col);
            UiaUi.Size(go, 16f);
            var t = UiaUi.Text(go.transform, text, 11f, UiaTheme.TextMute, TextAlignmentOptions.Left);
            var rt = (RectTransform)t.transform;
            UiaUi.Fill(rt);
            rt.offsetMin = new Vector2(18f, 0f);
        }

        // ---------- profile CRUD (rename / delete) ----------

        /// <summary>The destructive half of bag-profile CRUD, finally reachable from F10 (it had
        /// existed only in the legacy ImGui editor, which made "Create recommended profiles" a
        /// one-way door). Rename goes through <see cref="BagProfileStore.RenameProfile"/> — which
        /// cascades the name through assignments (this save AND every other save's file), prefab
        /// defaults, loadouts and any drop-in Profiles/*.xml copy — and delete through
        /// <see cref="BagProfileStore.DeleteProfile"/>, which clears those same references so
        /// nothing is left pointing at a name that no longer exists. Two-click confirm on delete,
        /// the same arm/confirm pattern the HUD Profiles tab uses.
        ///
        /// <para>This renames the PROFILE, never a container: the container rename is a separate,
        /// opt-in game-state mutation that lives on the Bags tab.</para></summary>
        private void ManageProfileRow(Transform col, string selName)
        {
            var row = UiaUi.Go("manage", col);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            if (_confirmDeleteProfile)
            {
                UiaControls.Button(row.transform, "Yes, delete it", () => DeleteProfile(selName), 170f,
                    UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
                UiaControls.Button(row.transform, "Cancel",
                    () => { _confirmDeleteProfile = false; UiaControlCenter.Refresh(); }, 110f, UiaTheme.RowH);
                UiaControls.Note(col, "Delete '" + selName + "'? Bags using it fall back to no profile, and any prefab default, loadout entry or shared copy of it is cleared too. This cannot be undone.");
                return;
            }

            var fieldGo = UiaUi.Go("renamehost", row.transform);
            UiaUi.Size(fieldGo, UiaTheme.RowH, flexW: 1f);
            UiaUi.HLayout((RectTransform)fieldGo.transform, 0f, 0, 0, 0, 0, TextAnchor.MiddleLeft, true);
            var input = UiaUi.InputField(fieldGo.transform, "New name for this profile...", v => _renameField = v);
            if (!string.IsNullOrEmpty(_renameField)) input.text = _renameField;

            UiaControls.Button(row.transform, "Rename", () => RenameProfile(selName), 100f, UiaTheme.RowH);
            UiaControls.Button(row.transform, "Delete",
                () => { _confirmDeleteProfile = true; UiaControlCenter.Refresh(); },
                90f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
        }

        private void RenameProfile(string oldName)
        {
            // ASCII-sanitised exactly like every other profile name (TMP renders Basic Latin only).
            string wanted = ProfileCapture.SanitizeName(_renameField);
            if (string.IsNullOrEmpty(wanted)) { _profileNote = "Type a new name first."; UiaControlCenter.Refresh(); return; }
            if (wanted == oldName) { _profileNote = "That is already its name."; UiaControlCenter.Refresh(); return; }
            if (BagProfileStore.FindProfile(wanted) != null)
            {
                _profileNote = "A profile called \"" + wanted + "\" already exists.";
                UiaControlCenter.Refresh();
                return;
            }
            bool ok = BagProfileStore.RenameProfile(oldName, wanted);
            _profileNote = ok
                ? "Renamed \"" + oldName + "\" to \"" + wanted + "\"."
                : "Could not rename \"" + oldName + "\" (see the log).";
            _renameField = null;
            BumpGridChrome();   // chips/badges carry the name
            UiaControlCenter.Refresh();
        }

        private void DeleteProfile(string name)
        {
            _confirmDeleteProfile = false;
            bool ok = BagProfileStore.DeleteProfile(name);
            _profileNote = ok
                ? "Deleted \"" + name + "\". Bags that used it now have no profile."
                : "Could not delete \"" + name + "\" (see the log).";
            _selected = 0;
            _renameField = null;
            BumpGridChrome();
            UiaControlCenter.Refresh();
        }

        // ---------- rules ----------

        private void RuleList(Transform col, BagProfile p)
        {
            if (p.RuleCount == 0) { UiaControls.Note(col, "This profile has no rules yet. Add an item, UIA class, category or slot class below."); return; }

            for (int i = p.Items.Count - 1; i >= 0; i--)
            {
                int idx = i; var rule = p.Items[idx];
                RuleRow(col, "ITEM", rule.Prefab, rule.Priority,
                    v => { rule.Priority = v; Save(); },
                    () => { p.Items.RemoveAt(idx); Save(); });
            }
            // UIA classes sit between item and slot-class rules here because that is their MATCH
            // precedence (+15000, above SlotClass's +10000): the list reads specific -> general.
            for (int i = p.UIAClasses.Count - 1; i >= 0; i--)
            {
                int idx = i; var rule = p.UIAClasses[idx];
                RuleRow(col, "UIA", rule.Name, rule.Priority,
                    v => { rule.Priority = v; Save(); },
                    () => { p.UIAClasses.RemoveAt(idx); Save(); });
            }
            for (int i = p.SlotClasses.Count - 1; i >= 0; i--)
            {
                int idx = i; var rule = p.SlotClasses[idx];
                RuleRow(col, "SLOT", rule.Name, rule.Priority,
                    v => { rule.Priority = v; Save(); },
                    () => { p.SlotClasses.RemoveAt(idx); Save(); });
            }
            for (int i = p.Categories.Count - 1; i >= 0; i--)
            {
                int idx = i; var rule = p.Categories[idx];
                RuleRow(col, "CAT", rule.Name, rule.Priority,
                    v => { rule.Priority = v; Save(); },
                    () => { p.Categories.RemoveAt(idx); Save(); });
            }
        }

        private static void RuleRow(Transform parent, string kind, string value, int priority,
            Action<int> setPriority, Action remove)
        {
            var row = UiaUi.Go("rule", parent);
            UiaUi.Size(row, 26f);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);

            var tagGo = UiaUi.Go("tag", row.transform);
            UiaUi.Size(tagGo, 20f, 46f);
            var tag = tagGo.AddComponent<Image>(); tag.color = UiaTheme.Panel;
            UiaUi.OutlineOf(tag, UiaTheme.AccentDim, 1f);
            var tt = UiaUi.Text(tagGo.transform, kind, 10f, UiaTheme.Accent, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)tt.transform);

            var val = UiaUi.Text(row.transform, value, UiaTheme.SmallSize, UiaTheme.Text, TextAlignmentOptions.Left);
            val.overflowMode = TextOverflowModes.Ellipsis;
            val.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Priority tri-state (design O6): shows the nearest bucket for any hand-tuned int
            // and only writes the canonical value back when the player actually picks one.
            var pl = UiaUi.Text(row.transform, "Priority", 10f, UiaTheme.TextMute, TextAlignmentOptions.Right);
            UiaUi.Size(pl.gameObject, 24f, 44f, flexW: 0f);
            InlineDropdown(row.transform, TierOptions, TierIndexOf(priority),
                i => setPriority(TierValueAt(i)), 92f);

            UiaControls.Button(row.transform, "X", remove, 30f, 24f, UiaControls.ButtonStyle.Danger);
        }

        /// <summary>The DropdownRow widget without its labelled row shell, for inline use inside
        /// an existing HLayout row (same look: raised panel, accent-dim outline, "v" caret).</summary>
        private static UiaControls.UiaDropdown InlineDropdown(Transform parent, List<string> options,
            int index, Action<int> onChanged, float width)
        {
            var ddGo = UiaUi.Go("dropdown", parent);
            UiaUi.Size(ddGo, 24f, width, flexW: 0f);
            var bg = ddGo.AddComponent<Image>(); bg.color = UiaTheme.PanelRaised;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, UiaTheme.AccentDim, 1f);
            var lbl = UiaUi.Text(ddGo.transform, "", UiaTheme.SmallSize, UiaTheme.Text, TextAlignmentOptions.Left);
            var lrt = (RectTransform)lbl.transform; UiaUi.Fill(lrt); lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-18f, 0f);
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            var caret = UiaUi.Text(ddGo.transform, "v", UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.Right);
            var crt = (RectTransform)caret.transform; UiaUi.Fill(crt); crt.offsetMax = new Vector2(-6f, 0f);
            var dd = ddGo.AddComponent<UiaControls.UiaDropdown>().Init(lbl, options, index);
            dd.OnChanged = onChanged;
            return dd;
        }

        private static int TierIndexOf(int priority)
        {
            RuleTier t = RuleTiers.TierOf(priority);
            if (t == RuleTier.High) return 0;
            if (t == RuleTier.Normal) return 1;
            return 2;
        }

        private static int TierValueAt(int index)
        {
            if (index == 0) return RuleTiers.HighValue;
            if (index == 1) return RuleTiers.NormalValue;
            return RuleTiers.LowValue;
        }

        private void AddControls(Transform col, BagProfile p)
        {
            var addGo = UiaUi.Go("add", col);
            UiaUi.Size(addGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)addGo.transform, UiaTheme.Gap);
            UiaControls.Button(addGo.transform, "+ Add item...", () =>
                UiaItemPicker.Open(prefab => AddItem(p, prefab), () => UiaControlCenter.Refresh()),
                150f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);

            // The 4th rule kind (FlorpyDorp Q1): the mod's own 22-class taxonomy. Listed above the
            // two game-enum dropdowns because it is the one most players want — "Electronics",
            // "Materials", "Kits" are one rule here and a dozen hand-listed prefabs otherwise.
            // Values are the enum NAMES (what the XML stores); the friendly label is in the note.
            var classes = new List<string> { "+ Add UIA class..." };
            classes.AddRange(Enum.GetNames(typeof(UIAClass)));
            UiaControls.DropdownRow(col, "By UIA class", classes, 0, i => { if (i > 0) AddUIAClass(p, classes[i]); });

            var cats = new List<string> { "+ Add category..." };
            cats.AddRange(Enum.GetNames(typeof(SortingClass)));
            UiaControls.DropdownRow(col, "By category", cats, 0, i => { if (i > 0) AddCategory(p, cats[i]); });

            var slots = new List<string> { "+ Add slot class..." };
            slots.AddRange(Enum.GetNames(typeof(Slot.Class)));
            UiaControls.DropdownRow(col, "By slot class", slots, 0, i => { if (i > 0) AddSlotClass(p, slots[i]); });
            UiaControls.Note(col, "Rule strength when several match one item: item beats UIA class, UIA class beats slot class, slot class beats category. UIA classes are the mod's own 22-way grouping (Materials, Electronics, Kits, Ores...) - broader than a single item, sharper than the game's 11 categories, and they keep catching new and modded items.");
        }

        private void AddItem(BagProfile p, string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return;
            foreach (var r in p.Items) if (r.Prefab == prefab) return;
            p.Items.Add(new ItemRule { Prefab = prefab, Priority = 100 });
            Save();
        }

        private void AddCategory(BagProfile p, string name)
        {
            foreach (var r in p.Categories) if (r.Name == name) { UiaControlCenter.Refresh(); return; }
            p.Categories.Add(new CategoryRule { Name = name, Priority = 50 });
            Save();
        }

        private void AddSlotClass(BagProfile p, string name)
        {
            foreach (var r in p.SlotClasses) if (r.Name == name) { UiaControlCenter.Refresh(); return; }
            p.SlotClasses.Add(new SlotClassRule { Name = name, Priority = 60 });
            Save();
        }

        private void AddUIAClass(BagProfile p, string name)
        {
            foreach (var r in p.UIAClasses) if (r.Name == name) { UiaControlCenter.Refresh(); return; }
            p.UIAClasses.Add(new UIAClassRule { Name = name, Priority = 55 });
            Save();
        }

        // ---------- bag gestures ----------

        /// <summary>Write one bag assignment from this tab. Re-gates the container at CLICK time
        /// (the list was built on an earlier frame — the bag may have been dropped, or the whole
        /// character swapped out), then runs the cheap typed-pack validation so assigning a
        /// "Materials" profile to a mining backpack SAYS that half its rules can never land there.
        /// Warning only, never a block: a player who means it keeps the assignment.
        ///
        /// <para>When the rename-on-assign switch is on, the container is then LABELLED with the
        /// profile's name through <see cref="ItemActions.RenameThing"/> — one user action, one
        /// assignment write, one rename message. UN-assignment deliberately does NOT rename back:
        /// the label is a physical fact the player can read in the world, and quietly removing it
        /// would be a second mutation they never asked for.</para></summary>
        private void AssignToBag(DynamicThing bag, string profileName)
        {
            if (bag == null) return;
            if (!BagProfileGate.IsAssignableContainer(bag) || !BagProfileGate.IsOnLocalPlayer(bag))
            {
                _assignNote = "That container is not on you any more - nothing was changed.";
                _bagScratchValid = false;   // force a fresh scan on the rebuild below
                UiaControlCenter.Refresh();
                return;
            }
            BagProfileStore.Assign(bag, profileName);
            _assignNote = null;
            _renameNote = null;
            if (!string.IsNullOrEmpty(profileName))
            {
                try { _assignNote = BagProfileGate.ValidateAssignment(bag, BagProfileStore.FindProfile(profileName)); }
                catch (Exception e) { UIALog.Warn("Profile/container validation failed: " + e.Message); }
                if (StowRenameConfig.RenameOnAssign) RenameForProfile(bag, profileName);
            }
            BumpGridChrome();   // an open Grid / pinned window repaints its chip + badge
            UiaControlCenter.Refresh();
        }

        private void RenameForProfile(DynamicThing bag, string profileName)
        {
            string current = null;
            try { current = bag.CustomName; } catch { }
            if (string.Equals(current, profileName, StringComparison.Ordinal)) return;  // already labelled
            bool ok = false;
            try { ok = ItemActions.RenameThing(bag, profileName); }
            catch (Exception e) { UIALog.Warn("Rename on assign failed: " + e.Message); }
            _renameNote = ok ? RenameOkNote(bag, profileName)
                             : "The profile was assigned, but that container could not be renamed.";
        }

        /// <summary>Manual rename from a bag card / dense row. Same funnel, same gates, and the same
        /// honest reporting: on a multiplayer CLIENT vanilla does no local prediction, so the new
        /// name only exists once the server echoes it back — never claim otherwise.</summary>
        private void RenameBag(DynamicThing bag)
        {
            if (bag == null) return;
            long id = SafeRef(bag);
            string wanted = GetRenameField(id);
            if (string.IsNullOrEmpty(wanted) || wanted.Trim().Length == 0)
            {
                _renameNote = "Type a name in that bag's Rename box first.";
                UiaControlCenter.Refresh();
                return;
            }
            if (!BagProfileGate.IsAssignableContainer(bag) || !BagProfileGate.IsOnLocalPlayer(bag))
            {
                _renameNote = "That container is not on you any more - nothing was renamed.";
                _bagScratchValid = false;
                UiaControlCenter.Refresh();
                return;
            }
            bool ok = false;
            try { ok = ItemActions.RenameThing(bag, wanted); }
            catch (Exception e) { UIALog.Warn("Manual bag rename failed: " + e.Message); }
            if (ok)
            {
                SetRenameField(id, null);
                _renameNote = RenameOkNote(bag, wanted.Trim());
            }
            else
            {
                _renameNote = "Could not rename that container (see the log).";
            }
            UiaControlCenter.Refresh();
        }

        private static string RenameOkNote(DynamicThing bag, string wanted)
        {
            bool authoritative = false;
            try { authoritative = Assets.Scripts.GameManager.RunSimulation; } catch { }
            if (authoritative) return "Renamed that container to \"" + SafeName(bag) + "\".";
            // MP client: the rename went out as one message and lands when the server confirms it.
            return "Rename sent (\"" + wanted + "\") - it appears once the server confirms it.";
        }

        private void SetExcluded(DynamicThing bag, bool excluded)
        {
            if (bag == null) return;
            if (!BagProfileGate.IsOnLocalPlayer(bag))
            {
                _assignNote = "That container is not on you any more - nothing was changed.";
                _bagScratchValid = false;
                UiaControlCenter.Refresh();
                return;
            }
            BagProfileStore.SetStowExcluded(bag, excluded);
            _assignNote = excluded
                ? "Smart Stow will never put anything into \"" + SafeName(bag) + "\"."
                : null;
            BumpGridChrome();
            UiaControlCenter.Refresh();
        }

        private void NewProfile()
        {
            string name = BagProfileStore.UniqueProfileName("Profile");
            BagProfileStore.Profiles.Add(new BagProfile { Name = name });
            _selected = BagProfileStore.Profiles.Count - 1;
            Save();
        }

        // ---------- test box ----------

        /// <summary>Render the dry-run for the picked prefab: the winner line plus the runner-up
        /// for context. Uses StowRouter.ResolveAll — a POOLED list, so every string is composed
        /// immediately and nothing from it is retained. The router never mutates game state.</summary>
        private void TestResults(Transform col)
        {
            DynamicThing prefab = FindPrefab(_testPrefab);
            if (prefab == null)
            {
                UiaControls.Note(col, "Item \"" + _testPrefab + "\" not available - load a world first.");
                return;
            }
            string disp;
            try { disp = prefab.DisplayName; } catch { disp = _testPrefab; }
            var head = UiaControls.Note(col, "Testing: " + disp);
            head.color = UiaTheme.Text;

            if (Guards.LocalHuman == null)
            {
                UiaControls.Note(col, "No character - load into a world to test.");
                return;
            }
            // Mirror the execution-path gates the router deliberately leaves to its caller
            // (SmartStowPlus.TryStow): report them rather than silently diverging from G.
            if (!UIAConfig.MasterEnable.Value || !UIAConfig.SmartStowPlusEnabled.Value)
                UiaControls.Note(col, "(SmartStow+ is currently disabled - this shows what it WOULD do)");

            List<StowCandidate> results = null;
            try { results = StowRouter.ResolveAll(prefab, null, StowRouter.ConfiguredDepth()); }
            catch (Exception e) { UIALog.Warn("Test box dry-run failed: " + e.Message); }
            if (results == null || results.Count == 0)
            {
                var none = UiaControls.Note(col, "-> vanilla Smart Stow (no rule matched)");
                none.color = UiaTheme.TextDim;
                return;
            }

            // Read the pooled list NOW; it is invalid after the next resolve anywhere.
            string winLine = "-> " + HolderName(results[0]) + "  (" + StageTag(results[0].Stage) + ": " + TrimmedReason(results[0]) + ")";
            string nextLine = results.Count > 1
                ? "next: " + HolderName(results[1]) + "  (" + StageTag(results[1].Stage) + ": " + TrimmedReason(results[1]) + ")"
                : null;

            var win = UiaControls.Note(col, winLine);
            win.color = UiaTheme.Good;
            win.fontSize = UiaTheme.LabelSize;
            if (nextLine != null)
            {
                var next = UiaControls.Note(col, nextLine);
                next.color = UiaTheme.TextMute;
            }
        }

        private static DynamicThing FindPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            try
            {
                var list = DynamicThing.DynamicThingPrefabs;
                if (list == null) return null;
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    if (it != null && it.PrefabName == prefabName) return it;
                }
            }
            catch { }
            return null;
        }

        private static string HolderName(StowCandidate c)
        {
            Thing t = c.Holder;
            if (t == null) return "inventory";
            try { return t.DisplayName; } catch { }
            try { return t.PrefabName ?? "container"; } catch { return "container"; }
        }

        private static string StageTag(StowStage s)
        {
            if (s == StowStage.BeltTool) return "BELT";
            if (s == StowStage.StackMerge) return "STACK";
            if (s == StowStage.FunctionalSocket) return "SOCKET";
            if (s == StowStage.Profile) return "PROFILE";
            if (s == StowStage.Affinity) return "AFFINITY";
            if (s == StowStage.BagDefault) return "DEFAULT";
            if (s == StowStage.Memory) return "MEMORY";
            if (s == StowStage.GenericFallback) return "GENERIC";
            if (s == StowStage.BeltFallback) return "BELT*";
            return "?";
        }

        /// <summary>The router's reason strings already name the profile stages ("profile: X",
        /// "bag default: X"); with the stage tag in front that prefix is redundant, so strip it.</summary>
        private static string TrimmedReason(StowCandidate c)
        {
            string reason = c.Reason ?? "";
            if (c.Stage == StowStage.Profile && reason.StartsWith("profile: ", StringComparison.Ordinal))
                return reason.Substring(9);
            if (c.Stage == StowStage.BagDefault && reason.StartsWith("bag default: ", StringComparison.Ordinal))
                return reason.Substring(13);
            return reason;
        }

        // ---------- recommended ----------

        private void EnsureRecommended()
        {
            var names = ProfileNames();
            foreach (var rec in Recommended)
            {
                if (names.Contains(rec[0])) continue;
                BagProfileStore.Profiles.Add(new BagProfile
                {
                    Name = rec[0],
                    Categories = new List<CategoryRule> { new CategoryRule { Name = rec[1], Priority = 50 } },
                });
            }
        }

        // "Auto-assign to my bags" is RETIRED (FlorpyDorp Q4: bag profiles are always mapped by
        // hand). It walked the worn-bag list and assigned profiles in list order, which is exactly
        // how tool belts and suits ended up owning profiles they should never have had (see the B1
        // report). Nothing replaces it: the Bags tab is the mapping surface.

        // ---------- helpers ----------

        private static List<string> ProfileNames()
        {
            var list = new List<string>();
            foreach (var p in BagProfileStore.Profiles) if (!string.IsNullOrEmpty(p.Name)) list.Add(p.Name);
            return list;
        }

        /// <summary>The on-player container list — still delegates to
        /// <see cref="LoadoutStore.CollectWornBags"/>, which remains the ONE canonical,
        /// gate-filtered enumeration every surface shares (the Loadouts FEATURE is retired, the scan
        /// it happens to live in is not). A theme-Restyle build reuses the last gesture-built list
        /// instead of re-running the inventory scan.</summary>
        private static List<DynamicThing> WornBags()
        {
            if (!(UiaControlCenter.IsRestyling && _bagScratchValid))
            {
                LoadoutStore.CollectWornBags(_bagScratch);
                _bagScratchValid = true;
            }
            return _bagScratch;
        }

        private static string SafeName(DynamicThing t)
        {
            try { return t.DisplayName; } catch { return t != null ? t.PrefabName : "bag"; }
        }

        /// <summary>What this container IS, independent of any custom name it has been given — so a
        /// card still tells you it is a Mining Belt after you (or a labeller) renamed it "Ores".</summary>
        private static string TypeName(DynamicThing t)
        {
            try
            {
                var prefab = t.SourcePrefab;
                if (prefab != null && !string.IsNullOrEmpty(prefab.DisplayName)) return prefab.DisplayName;
            }
            catch { }
            try { return t.PrefabName ?? "container"; } catch { return "container"; }
        }

        private static string SafePrefab(DynamicThing t)
        {
            try { return t.PrefabName ?? ""; } catch { return ""; }
        }

        private static long SafeRef(Thing t)
        {
            try { return t != null ? t.ReferenceId : 0L; } catch { return 0L; }
        }

        /// <summary>Persist the rule/profile edit that just happened — and SAY SO when it failed.
        /// A silent failure here (read-only config folder, a locked file) used to look exactly like
        /// a successful save: the UI refreshed showing the edit, which then vanished at the next
        /// launch with no explanation.</summary>
        private void Save()
        {
            bool ok = false;
            try { ok = BagProfileStore.SaveProfiles(); } catch { }
            if (!ok) _profileNote = "Could not save - your change is only in memory. See the log.";
            UiaControlCenter.Refresh();
        }
    }
}
