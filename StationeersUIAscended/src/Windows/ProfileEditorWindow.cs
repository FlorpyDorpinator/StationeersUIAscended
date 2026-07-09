using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using ImGuiNET;
using StationeersUIAscended.Core;
using StationeersUIAscended.Features;
using UI.ImGuiUi.ImGuiWindows;
using UnityEngine;
using GameImGuiWindow = UI.ImGuiUi.ImGuiWindows.ImGuiWindow;

namespace StationeersUIAscended.Windows
{
    /// <summary>
    /// Phase 5 UI: edit bag profiles (item / slot-class / category rules with priorities)
    /// and assign profiles to the bags you are wearing. Definitions are global XML;
    /// assignments are per save, keyed by the bag's ReferenceId.
    /// </summary>
    public sealed class ProfileEditorWindow : GameImGuiWindow
    {
        private int _selected;
        private string _newProfileName = "";
        private string _newItemPrefab = "";
        private int _newItemPriority = 100;
        // The worn-bag list is cached: DrawContent runs every ImGui frame and a full
        // recursive inventory scan per frame is wasted work.
        private List<DynamicThing> _wornBags = new List<DynamicThing>();
        private float _wornBagsScannedAt = -999f;

        public ProfileEditorWindow() : base("UI Ascended - Bag Profiles", new Vector2(560f, 520f)) { }

        public override void OnOpen()
        {
            BagProfileStore.EnsureSaveLoaded();
            _wornBagsScannedAt = -999f;
        }

        public override void OnClose() { }

        private void RefreshWornBags()
        {
            if (Time.unscaledTime - _wornBagsScannedAt < 1f) return;
            _wornBagsScannedAt = Time.unscaledTime;
            _wornBags.Clear();
            foreach (var scanned in InventoryScanner.Scan(2, false))
            {
                var bag = scanned.Occupant;
                if (bag == null || bag.Slots == null || bag.Slots.Count < 2) continue;
                if (!_wornBags.Contains(bag)) _wornBags.Add(bag);
            }
        }

        public override void DrawContent()
        {
            DrawAssignments();
            ImGui.Separator();
            DrawProfileList();
            ImGui.Separator();
            DrawRuleEditor();
            ImGui.Separator();
            if (ImGui.Button("Save all profiles")) BagProfileStore.SaveProfiles();
            ImGui.SameLine();
            if (ImGui.Button("Reload from disk")) BagProfileStore.LoadProfiles();
            ImGui.SameLine();
            ImGui.TextDisabled("XML: " + BagProfileStore.ProfilesDir);
        }

        // --- worn-bag assignments ---

        private void DrawAssignments()
        {
            ImGui.Text("Worn bags");
            var human = Guards.LocalHuman;
            if (human == null)
            {
                ImGui.TextDisabled("(no player)");
                return;
            }
            RefreshWornBags();
            foreach (var bag in _wornBags)
            {
                if (bag == null) continue;
                string current = BagProfileStore.GetAssignedProfileName(bag) ?? "(none)";
                if (ImGui.BeginCombo("##assign_" + bag.ReferenceId, bag.DisplayName + "  ->  " + current))
                {
                    if (ImGui.Selectable("(none)", current == "(none)"))
                        BagProfileStore.Assign(bag, null);
                    foreach (var profile in BagProfileStore.Profiles)
                        if (ImGui.Selectable(profile.Name, current == profile.Name))
                            BagProfileStore.Assign(bag, profile.Name);
                    ImGui.EndCombo();
                }
            }
        }

        // --- profile list ---

        private void DrawProfileList()
        {
            ImGui.Text("Profiles");
            var names = BagProfileStore.Profiles.Select(p => p.Name).ToArray();
            if (names.Length > 0)
            {
                _selected = Mathf.Clamp(_selected, 0, names.Length - 1);
                ImGui.Combo("Edit profile", ref _selected, names, names.Length);
            }
            else
            {
                ImGui.TextDisabled("(no profiles loaded)");
            }

            ImGui.InputText("New profile name", ref _newProfileName, 48);
            ImGui.SameLine();
            if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(_newProfileName)
                && BagProfileStore.Profiles.All(p => p.Name != _newProfileName))
            {
                BagProfileStore.Profiles.Add(new BagProfile { Name = _newProfileName.Trim() });
                _selected = BagProfileStore.Profiles.Count - 1;
                _newProfileName = "";
            }
            if (names.Length > 0)
            {
                ImGui.SameLine();
                if (ImGui.Button("Delete selected"))
                {
                    BagProfileStore.Profiles.RemoveAt(_selected);
                    _selected = 0;
                }
            }
        }

        // --- rule editor ---

        private void DrawRuleEditor()
        {
            if (_selected < 0 || _selected >= BagProfileStore.Profiles.Count) return;
            var profile = BagProfileStore.Profiles[_selected];
            ImGui.Text("Rules for '" + profile.Name + "'");

            // Item rules
            for (int i = profile.Items.Count - 1; i >= 0; i--)
            {
                var rule = profile.Items[i];
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "Item");
                ImGui.SameLine();
                ImGui.Text(rule.Prefab + "  (prio " + rule.Priority + ")");
                ImGui.SameLine();
                if (ImGui.SmallButton("x##item" + i)) profile.Items.RemoveAt(i);
            }
            // Slot class rules
            for (int i = profile.SlotClasses.Count - 1; i >= 0; i--)
            {
                var rule = profile.SlotClasses[i];
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.9f, 1f), "SlotClass");
                ImGui.SameLine();
                ImGui.Text(rule.Name + "  (prio " + rule.Priority + ")");
                ImGui.SameLine();
                if (ImGui.SmallButton("x##slot" + i)) profile.SlotClasses.RemoveAt(i);
            }
            // Category rules
            for (int i = profile.Categories.Count - 1; i >= 0; i--)
            {
                var rule = profile.Categories[i];
                ImGui.TextColored(new Vector4(0.5f, 0.9f, 0.5f, 1f), "Category");
                ImGui.SameLine();
                ImGui.Text(rule.Name + "  (prio " + rule.Priority + ")");
                ImGui.SameLine();
                if (ImGui.SmallButton("x##cat" + i)) profile.Categories.RemoveAt(i);
            }

            ImGui.Spacing();
            ImGui.InputText("Prefab", ref _newItemPrefab, 64);
            ImGui.SameLine();
            if (ImGui.Button("From hand"))
            {
                var held = InventoryManager.ActiveHandSlot?.Get();
                if (held != null) _newItemPrefab = held.PrefabName;
            }
            ImGui.SliderInt("Priority", ref _newItemPriority, 1, 200);
            if (ImGui.Button("+ Item rule") && !string.IsNullOrWhiteSpace(_newItemPrefab))
                profile.Items.Add(new ItemRule { Prefab = _newItemPrefab.Trim(), Priority = _newItemPriority });
            ImGui.SameLine();
            if (ImGui.BeginCombo("##addslotclass", "+ SlotClass rule"))
            {
                foreach (var name in Enum.GetNames(typeof(Slot.Class)))
                    if (ImGui.Selectable(name))
                        profile.SlotClasses.Add(new SlotClassRule { Name = name, Priority = _newItemPriority });
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.BeginCombo("##addcategory", "+ Category rule"))
            {
                foreach (var name in Enum.GetNames(typeof(SortingClass)))
                    if (ImGui.Selectable(name))
                        profile.Categories.Add(new CategoryRule { Name = name, Priority = _newItemPriority });
                ImGui.EndCombo();
            }
        }
    }
}
