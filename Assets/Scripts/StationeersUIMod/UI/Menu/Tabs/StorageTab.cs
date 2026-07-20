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
    /// categories / slot classes). Profiles are global; assignments are per save.</summary>
    public sealed class StorageTab : IUiaTab
    {
        public string Title => "Storage";

        private int _selected;

        // The shipped "good default" category set: profile name -> SortingClass it catches.
        private static readonly string[][] Recommended =
        {
            new[] { "Tools", "Tools" }, new[] { "Resources", "Resources" }, new[] { "Kits", "Kits" },
            new[] { "Food", "Food" }, new[] { "Clothing", "Clothing" }, new[] { "Atmospherics", "Atmospherics" },
            new[] { "Storage", "Storage" }, new[] { "Ores", "Ores" }, new[] { "Ices", "Ices" },
            new[] { "Appliances", "Appliances" },
        };

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            // ---- SmartStow+ ----
            UiaControls.Header(col, "Smart Stow (G)");
            UiaControls.ToggleRow(col, "Enable SmartStow+", UIAConfig.SmartStowPlusEnabled.Value, v => UIAConfig.SmartStowPlusEnabled.Value = v);
            UiaControls.ToggleRow(col, "1 - Prefer topping up matching stacks", UIAConfig.StowPreferStacks.Value, v => UIAConfig.StowPreferStacks.Value = v);
            UiaControls.ToggleRow(col, "2 - Use bag profiles", UIAConfig.StowUseProfiles.Value, v => UIAConfig.StowUseProfiles.Value = v);
            UiaControls.ToggleRow(col, "3 - Remember where each type went", UIAConfig.StowUseTypeMemory.Value, v => UIAConfig.StowUseTypeMemory.Value = v);
            UiaControls.SliderRow(col, "How deep to search (levels)", 1f, 5f, UIAConfig.ScanDepth.Value, v => UIAConfig.ScanDepth.Value = Mathf.RoundToInt(v), "0", 1f);
            if (advanced)
            {
                UiaControls.ToggleRow(col, "Stow tools onto the toolbelt first", UIAConfig.StowToolsToToolbeltFirst.Value, v => UIAConfig.StowToolsToToolbeltFirst.Value = v);
                UiaControls.ToggleRow(col, "Allow nested-bag stow", UIAConfig.StowIntoNestedBags.Value, v => UIAConfig.StowIntoNestedBags.Value = v);
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
                        i => { BagProfileStore.Assign(b, i <= 0 ? null : profNames[i - 1]); });
                }
            }

            // ---- profile editor ----
            UiaControls.Header(col, "Edit a profile");
            if (profNames.Count == 0)
            {
                UiaControls.Note(col, "No profiles yet - use \"Create recommended profiles\" above, or add one.");
            }
            else
            {
                _selected = Mathf.Clamp(_selected, 0, profNames.Count - 1);
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

                // Resolve by NAME, not positional index: ProfileNames() filters empty-named
                // profiles, so the dropdown index and the Profiles list index can otherwise desync.
                string selName = profNames[_selected];
                BagProfile profile = null;
                foreach (var p in BagProfileStore.Profiles)
                    if (p.Name == selName) { profile = p; break; }
                if (profile != null)
                {
                    RuleList(col, profile);
                    AddControls(col, profile);
                }
            }
        }

        // ---------- rules ----------

        private void RuleList(Transform col, BagProfile p)
        {
            bool any = p.Items.Count + p.Categories.Count + p.SlotClasses.Count > 0;
            if (!any) { UiaControls.Note(col, "This profile has no rules yet. Add an item, category or slot class below."); return; }

            for (int i = p.Items.Count - 1; i >= 0; i--) { int idx = i; RuleRow(col, "ITEM", p.Items[idx].Prefab, () => { p.Items.RemoveAt(idx); Save(); }); }
            for (int i = p.SlotClasses.Count - 1; i >= 0; i--) { int idx = i; RuleRow(col, "SLOT", p.SlotClasses[idx].Name, () => { p.SlotClasses.RemoveAt(idx); Save(); }); }
            for (int i = p.Categories.Count - 1; i >= 0; i--) { int idx = i; RuleRow(col, "CAT", p.Categories[idx].Name, () => { p.Categories.RemoveAt(idx); Save(); }); }
        }

        private static void RuleRow(Transform parent, string kind, string value, Action remove)
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
            val.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            UiaControls.Button(row.transform, "X", remove, 30f, 24f, UiaControls.ButtonStyle.Danger);
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
            string name = "Profile";
            var names = ProfileNames();
            int n = 1;
            while (names.Contains(name + " " + n)) n++;
            name = name + " " + n;
            BagProfileStore.Profiles.Add(new BagProfile { Name = name });
            _selected = BagProfileStore.Profiles.Count - 1;
            Save();
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
            Save();
        }

        // ---------- helpers ----------

        private static List<string> ProfileNames()
        {
            var list = new List<string>();
            foreach (var p in BagProfileStore.Profiles) if (!string.IsNullOrEmpty(p.Name)) list.Add(p.Name);
            return list;
        }

        private static List<DynamicThing> WornBags()
        {
            var bags = new List<DynamicThing>();
            try
            {
                if (Guards.LocalHuman == null) return bags;
                foreach (var s in InventoryScanner.Scan(2, false))
                {
                    var b = s.Occupant;
                    if (b == null || b.Slots == null || b.Slots.Count < 2) continue;
                    if (!bags.Contains(b)) bags.Add(b);
                }
            }
            catch { }
            return bags;
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
