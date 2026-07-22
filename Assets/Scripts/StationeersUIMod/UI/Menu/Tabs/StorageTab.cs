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
    /// <summary>Smart storage: the SmartStow+ chain, assigning profiles to your worn bags, a
    /// one-click recommended setup, and a rule editor (add items via the searchable picker, or whole
    /// categories / slot classes). Profiles are global; assignments are per save. Per design O6 this
    /// tab is the POWER ROOM: per-rule priority tri-state (High/Normal/Low), per-bag "all of this
    /// type" prefab defaults, and a read-only TEST BOX that dry-runs the StowRouter to show where a
    /// picked item WOULD go and why (nothing ever moves).</summary>
    public sealed class StorageTab : IUiaTab
    {
        public string Title => "Storage";

        private int _selected;
        // The test box's picked item (PrefabName). Survives Refresh (tab instances persist);
        // resets on a theme Restyle, which is fine — it is a transient diagnostic, not state.
        private string _testPrefab;
        // One-line result notes shown under their sections after a gesture (apply/save/delete a
        // loadout, export/reload profiles). Same lifetime story as _testPrefab: they survive
        // Refresh, reset on a theme Restyle — transient feedback, not state.
        private string _loadoutNote;
        private string _profileNote;
        // Loadout listing + worn-bag scratch. STATIC (tab instances are recreated by a theme
        // Restyle) so a restyle-driven Build can reuse the last gesture-built data instead of
        // re-reading Loadouts/*.xml from disk and re-scanning the inventory up to ~7x/s during
        // an F9 colour-wheel drag (verified perf finding, 2026-07-20). Every NON-restyle Build
        // refills both from the source of truth, so user gestures always see fresh data.
        // ResetCaches (called from UiaControlCenter.Shutdown) drops the Thing refs on teardown.
        private static readonly List<Loadout> _loadoutScratch = new List<Loadout>();
        private static bool _loadoutScratchValid;
        private static readonly List<DynamicThing> _bagScratch = new List<DynamicThing>();
        private static bool _bagScratchValid;

        /// <summary>Hot-reload/menu teardown: drop the restyle-scoped caches (they hold
        /// DynamicThing references into the live world).</summary>
        public static void ResetCaches()
        {
            _loadoutScratch.Clear();
            _loadoutScratchValid = false;
            _bagScratch.Clear();
            _bagScratchValid = false;
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

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

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

            // ---- one-click setup ----
            UiaControls.Header(col, "Quick setup");
            UiaControls.Note(col, "Create a ready-made set of category profiles (Tools, Resources, Food...), then let Smart Stow route loot to the right bag. Assign them below, or auto-fill your worn bags in one click.");
            var qsGo = UiaUi.Go("qs", col);
            UiaUi.Size(qsGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)qsGo.transform, UiaTheme.Gap);
            UiaControls.Button(qsGo.transform, "Create recommended profiles", () => { EnsureRecommended(); Save(); }, 240f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
            UiaControls.Button(qsGo.transform, "Auto-assign to my bags", AutoAssign, 200f, UiaTheme.RowH);

            // ---- worn bags ----
            UiaControls.Header(col, "Your bags");
            var bags = WornBags();
            var profNames = ProfileNames();
            if (bags.Count == 0)
            {
                UiaControls.Note(col, "Equip some bags (backpack, belts, ore bags) to assign profiles to them.");
            }
            else
            {
                var options = new List<string> { "(no profile)" };
                options.AddRange(profNames);
                foreach (var bag in bags)
                {
                    var b = bag;
                    string cur = BagProfileStore.GetAssignedProfileName(b);
                    int idx = string.IsNullOrEmpty(cur) ? 0 : Mathf.Max(0, profNames.IndexOf(cur) + 1);
                    UiaControls.DropdownRow(col, SafeName(b), options, idx,
                        i =>
                        {
                            BagProfileStore.Assign(b, i <= 0 ? null : profNames[i - 1]);
                            BumpGridChrome();   // an open Grid / pinned window repaints its chip + badge
                            UiaControlCenter.Refresh();
                        });
                    BagSubRows(col, b, cur);
                }
            }

            // ---- loadouts (design O5a: cross-save portability; apply is ALWAYS manual, Q4) ----
            BuildLoadouts(col);

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
                UiaControls.DropdownRow(ddHost.transform, "Profile", profNames, _selected, i => { _selected = i; UiaControlCenter.Refresh(); });
                UiaControls.Button(pickGo.transform, "New", NewProfile, 70f, UiaTheme.RowH);
                if (profile != null)
                    UiaControls.Button(pickGo.transform, "Export", () => ExportProfile(selName), 80f, UiaTheme.RowH);

                if (profile != null)
                {
                    RuleList(col, profile);
                    AddControls(col, profile);
                }
            }

            // ---- sharing (design O5c) — available even with zero profiles (the import path) ----
            if (!string.IsNullOrEmpty(_profileNote)) SubNote(col, _profileNote);
            var shGo = UiaUi.Go("share", col);
            UiaUi.Size(shGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)shGo.transform, UiaTheme.Gap);
            UiaControls.Button(shGo.transform, "Reload profiles", ReloadProfiles, 150f, UiaTheme.RowH);
            UiaControls.Note(col, "Share: Export writes the selected profile to its own file in the Profiles folder. Drop a received profile file in the same folder and hit Reload profiles.");

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

        // ---------- loadouts (design O5a) ----------

        /// <summary>The Loadouts section: save the current worn arrangement as a new auto-named
        /// loadout, list every saved loadout with [Apply] [Delete], and show the last action's
        /// result note. Everything here is CONFIG state — a loadout apply writes per-save
        /// assignments through <see cref="BagProfileStore.Assign"/> and nothing else; applying is
        /// always this explicit button press (design Q4 — no auto-apply, ever).</summary>
        private void BuildLoadouts(Transform col)
        {
            UiaControls.Header(col, "Loadouts");
            UiaControls.Note(col, "A loadout remembers which profile each worn bag uses - by bag type and worn order, not by save - so one click re-applies your setup on a fresh save or a replaced bag. Applying is always manual.");

            var saveGo = UiaUi.Go("losave", col);
            UiaUi.Size(saveGo, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)saveGo.transform, UiaTheme.Gap);
            UiaControls.Button(saveGo.transform, "Save current as loadout", SaveLoadout, 220f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);

            // Disk IO only on gesture-driven builds; a theme Restyle reuses the last listing.
            if (!(UiaControlCenter.IsRestyling && _loadoutScratchValid))
            {
                LoadoutStore.LoadAll(_loadoutScratch);
                _loadoutScratchValid = true;
            }
            if (_loadoutScratch.Count == 0)
            {
                UiaControls.Note(col, "No loadouts yet. Assign profiles to your bags above, then save the arrangement here.");
            }
            else
            {
                foreach (var lo in _loadoutScratch)
                {
                    var l = lo;
                    var row = UiaUi.Go("loadout", col);
                    UiaUi.Size(row, 26f);
                    UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 6, 6, 0, 0, TextAnchor.MiddleLeft);
                    string label = l.Name + "  (" + l.Entries.Count + (l.Entries.Count == 1 ? " bag)" : " bags)");
                    var name = UiaUi.Text(row.transform, label, UiaTheme.SmallSize, UiaTheme.Text, TextAlignmentOptions.Left);
                    name.overflowMode = TextOverflowModes.Ellipsis;
                    name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                    UiaControls.Button(row.transform, "Apply", () => ApplyLoadout(l), 70f, 24f, UiaControls.ButtonStyle.Primary);
                    UiaControls.Button(row.transform, "Delete", () => DeleteLoadout(l), 70f, 24f, UiaControls.ButtonStyle.Danger);
                }
            }
            if (!string.IsNullOrEmpty(_loadoutNote)) SubNote(col, _loadoutNote);
        }

        private void SaveLoadout()
        {
            Loadout lo = LoadoutStore.SaveCurrentAsNew();
            _loadoutNote = lo == null
                ? "Nothing to save - no worn bag has a profile assigned."
                : "Saved \"" + lo.Name + "\" (" + lo.Entries.Count + (lo.Entries.Count == 1 ? " bag)." : " bags).");
            UiaControlCenter.Refresh();
        }

        private void ApplyLoadout(Loadout lo)
        {
            LoadoutApplyResult r = LoadoutStore.Apply(lo);
            string note = "Applied \"" + lo.Name + "\": " + r.Applied + " assigned";
            if (r.MissingBags > 0) note += ", " + r.MissingBags + " bag(s) not worn";
            if (r.MissingProfiles > 0) note += ", " + r.MissingProfiles + " profile(s) missing";
            _loadoutNote = note + ".";
            BumpGridChrome();   // assignments changed → chips/badges on an open Grid refresh
            UiaControlCenter.Refresh();
        }

        private void DeleteLoadout(Loadout lo)
        {
            _loadoutNote = LoadoutStore.Delete(lo)
                ? "Deleted \"" + lo.Name + "\"."
                : "Could not delete \"" + lo.Name + "\".";
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
            BagProfileStore.LoadProfiles();   // re-merges every Profiles/*.xml (drop-in imports included)
            _profileNote = "Profiles reloaded (" + BagProfileStore.Profiles.Count + " loaded).";
            BumpGridChrome();                 // names/badges may have changed on an open Grid
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

        // ---------- worn-bag sub-rows (prefab defaults + the implicit-profile hint) ----------

        /// <summary>Under each bag row: the "All &lt;bag name&gt;s" prefab-default toggle (design
        /// O5b — extend THIS bag's assigned profile to every bag of the same prefab; the instance
        /// assignment always wins over the type default in the router), or — for a profile-less
        /// bag — a hint that the zero-setup tier is doing the routing.</summary>
        private static void BagSubRows(Transform col, DynamicThing b, string assigned)
        {
            string prefabName = null;
            try { prefabName = b.PrefabName; } catch { }
            string typeDefault = BagProfileStore.GetPrefabDefaultProfileName(prefabName);

            if (!string.IsNullOrEmpty(assigned) && !string.IsNullOrEmpty(prefabName))
            {
                // Toggle is ON only when the type default IS this bag's profile; a differing
                // default shows OFF (plus the note below) and toggling ON overwrites it.
                bool on = typeDefault == assigned;
                var row = UiaUi.Go("alltype", col);
                UiaUi.Size(row, 24f);
                UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap, 18, 0, 0, 0, TextAnchor.MiddleLeft);
                var lbl = UiaUi.Text(row.transform, "All " + SafeName(b) + "s", UiaTheme.SmallSize, UiaTheme.TextDim, TextAlignmentOptions.Left);
                lbl.overflowMode = TextOverflowModes.Ellipsis;
                lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                string prefabCopy = prefabName;
                string assignedCopy = assigned;
                UiaControls.Switch(row.transform, on, v =>
                {
                    if (v) BagProfileStore.SetPrefabDefault(prefabCopy, assignedCopy);
                    else BagProfileStore.ClearPrefabDefault(prefabCopy);
                    UiaControlCenter.Refresh();
                });
                if (!string.IsNullOrEmpty(typeDefault) && typeDefault != assigned)
                    SubNote(col, "(type default is: " + typeDefault + ")");
            }
            else if (!string.IsNullOrEmpty(typeDefault))
            {
                SubNote(col, "(all " + SafeName(b) + "s default to: " + typeDefault + ")");
            }
            else if (UIAConfig.StowUseAffinity.Value)
            {
                // Design O6: make the zero-setup tier discoverable instead of magic.
                SubNote(col, "(implicit - routes by contents)");
            }
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

        // ---------- rules ----------

        private void RuleList(Transform col, BagProfile p)
        {
            bool any = p.Items.Count + p.Categories.Count + p.SlotClasses.Count > 0;
            if (!any) { UiaControls.Note(col, "This profile has no rules yet. Add an item, category or slot class below."); return; }

            for (int i = p.Items.Count - 1; i >= 0; i--)
            {
                int idx = i; var rule = p.Items[idx];
                RuleRow(col, "ITEM", rule.Prefab, rule.Priority,
                    v => { rule.Priority = v; Save(); },
                    () => { p.Items.RemoveAt(idx); Save(); });
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

            var cats = new List<string> { "+ Add category..." };
            cats.AddRange(Enum.GetNames(typeof(SortingClass)));
            UiaControls.DropdownRow(col, "By category", cats, 0, i => { if (i > 0) AddCategory(p, cats[i]); });

            var slots = new List<string> { "+ Add slot class..." };
            slots.AddRange(Enum.GetNames(typeof(Slot.Class)));
            UiaControls.DropdownRow(col, "By slot class", slots, 0, i => { if (i > 0) AddSlotClass(p, slots[i]); });
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

        private void AutoAssign()
        {
            EnsureRecommended();
            var bags = WornBags();
            var used = new HashSet<string>();
            foreach (var b in bags)
            {
                string cur = BagProfileStore.GetAssignedProfileName(b);
                if (!string.IsNullOrEmpty(cur)) { used.Add(cur); }
            }
            int pi = 0;
            foreach (var b in bags)
            {
                if (!string.IsNullOrEmpty(BagProfileStore.GetAssignedProfileName(b))) continue;
                while (pi < BagProfileStore.Profiles.Count && used.Contains(BagProfileStore.Profiles[pi].Name)) pi++;
                if (pi >= BagProfileStore.Profiles.Count) break;
                var prof = BagProfileStore.Profiles[pi++];
                BagProfileStore.Assign(b, prof.Name);
                used.Add(prof.Name);
            }
            BumpGridChrome();   // assignments changed — same refresh ApplyLoadout already does
            Save();
        }

        // ---------- helpers ----------

        private static List<string> ProfileNames()
        {
            var list = new List<string>();
            foreach (var p in BagProfileStore.Profiles) if (!string.IsNullOrEmpty(p.Name)) list.Add(p.Name);
            return list;
        }

        /// <summary>The worn-bag list — delegates to <see cref="LoadoutStore.CollectWornBags"/>,
        /// the ONE canonical enumeration, so the order shown here is exactly the order loadout
        /// occurrence indices are counted in (they can never diverge). A theme-Restyle build
        /// reuses the last gesture-built list instead of re-running the inventory scan.</summary>
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

        private void Save()
        {
            try { BagProfileStore.SaveProfiles(); } catch { }
            UiaControlCenter.Refresh();
        }
    }
}
