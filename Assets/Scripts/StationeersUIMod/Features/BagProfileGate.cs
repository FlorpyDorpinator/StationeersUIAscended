using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;          // Slot, DynamicThing, Thing, ToolBelt, MiningBelt, Container
using Assets.Scripts.Objects.Items;    // Backpack, Jetpack, CardboardBox, DisposableCardboardBox
using Objects.Items;                   // SECOND game namespace: CerealBarBox, EmergencySuppliesBox
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The ONE gate that decides which containers may carry a Bag Profile, plus the cheap
    /// typed-pack sanity check that runs when a profile is assigned to one.
    ///
    /// <para><b>Why this exists.</b> Two disagreeing container classifiers already shipped:
    /// <c>LoadoutStore.CollectWornBags</c> filtered on nothing but "2+ slots" (so the Terrain
    /// Manipulator — a <c>VoxelTool : PowerTool : Tool</c> with a Battery slot and a DirtCanister
    /// slot — showed up in F10's bag list and could be assigned a profile), while
    /// <c>GridModel.IsStorageContainer</c> did real class filtering but is deliberately BROADER
    /// than profile assignment wants (it admits suits and uniforms via its SlotType switch,
    /// because those really are storage regions in the Universal Inventory). FlorpyDorp's ruling
    /// (redesign plan §14): only backpacks and boxes are assignable — real wearable/carryable
    /// storage, NOT tools with slots and NOT food packaging.</para>
    ///
    /// <para><b>Class hierarchy this encodes</b> (every edge verified against the 27758 decompile,
    /// <c>Assembly-CSharp</c>; note CerealBarBox/EmergencySuppliesBox live in the un-prefixed
    /// <c>Objects.Items</c> tree, not <c>Assets.Scripts.Objects.Items</c>):</para>
    /// <code>
    /// WearableItem
    ///   Backpack                 -> PASS   (ItemHardBackpack, ItemMiningBackPack, ...)
    ///     Jetpack                -> FAIL   (propulsion, not storage; ItemSpacepack et al)
    ///   ToolBelt                 -> FAIL   (ItemToolBelt, ToolBeltMk2, SurvivalToolbelt)
    ///     MiningBelt             -> PASS   (ore storage; MiningBeltMk2 inherits)
    /// ItemRenamable
    ///   CardboardBox             -> PASS   (CardboardBox, CardboardBoxLarge)
    ///     DisposableCardboardBox -> FAIL   (unpack-and-self-destruct packaging)
    ///     CerealBarBox           -> FAIL
    ///     EmergencySuppliesBox   -> FAIL
    /// DraggableThing
    ///   Container                -> PASS   (DynamicCrate, CrateMkII; no subclasses exist)
    /// </code>
    ///
    /// <para>Client-safe: reads only type identity and prefab/slot data, never server-only state.
    /// Stateless — nothing to reset on hot reload.</para>
    /// </summary>
    public static class BagProfileGate
    {
        /// <summary>May this container be assigned a Bag Profile? Class-based, with the same
        /// 5-name packaging exception list <see cref="UI.Grid.GridModel.IsStorageContainer"/>
        /// independently arrived at. Fail-closed on null / non-<see cref="DynamicThing"/>.
        ///
        /// <para>Deliberately NARROWER than the router's "is real storage" gate: a tool belt is
        /// still a perfectly good stow DESTINATION (the belt stage, bag-type defaults, affinity
        /// all still route to it) — it just cannot own a profile of its own.</para></summary>
        public static bool IsAssignableContainer(Thing thing)
        {
            var t = thing as DynamicThing;
            if (t == null) return false;
            if (t.Slots == null || t.Slots.Count == 0) return false;
            if (t is Jetpack) return false;                  // Backpack subtype - exclude FIRST
            if (t is Backpack) return true;
            if (t is MiningBelt) return true;                // covers MiningBeltMk2; plain ToolBelt/Mk2/Survival fall through
            if (t is ToolBelt) return false;
            if (t is DisposableCardboardBox || t is CerealBarBox || t is EmergencySuppliesBox) return false;
            if (t is CardboardBox) return true;
            if (t is Container) return true;                 // DynamicCrate, CrateMkII
            return false;
        }

        /// <summary>Is this container currently somewhere on the LOCAL player? One property read
        /// (<c>DynamicThing.RootParentHuman</c> walks the ParentSlot chain up to a Human — networked
        /// inventory state, MP-client-safe). Used as the final gate immediately before a write, so
        /// a container reference that went stale between the build that listed it and the click
        /// that assigns it can never be written to. Not a substitute for enumeration: discovery
        /// still goes through <see cref="LoadoutStore.CollectWornBags"/>.</summary>
        public static bool IsOnLocalPlayer(Thing thing)
        {
            var t = thing as DynamicThing;
            if (t == null) return false;
            try
            {
                var human = Guards.LocalHuman;
                return human != null && ReferenceEquals(t.RootParentHuman, human);
            }
            catch { return false; }
        }

        // ------------------------------------------------------- typed-pack validation ----

        /// <summary>The one <see cref="Slot.Class"/> every TYPE-RESTRICTED slot of this container
        /// shares, or null when the container is generic (no typed slots) or mixed (two different
        /// typed classes). <c>Slot.Class.None</c> slots are ignored: a mining belt legitimately
        /// keeps one unrestricted tool slot alongside its ore slots
        /// (<c>MiningBelt.IsOreSlot</c> accepts <c>None</c> or <c>Ore</c>, decompile
        /// <c>Objects/MiningBelt.cs</c>), and that one slot must not stop the pack reading as an
        /// "Ore pack".</summary>
        public static Slot.Class? TypedClassOf(DynamicThing bag)
        {
            if (bag == null || bag.Slots == null) return null;
            Slot.Class? found = null;
            for (int i = 0; i < bag.Slots.Count; i++)
            {
                Slot s = bag.Slots[i];
                if (s == null || s.Type == Slot.Class.None) continue;
                if (!found.HasValue) found = s.Type;
                else if (found.Value != s.Type) return null;   // mixed -> not a typed pack
            }
            return found;
        }

        /// <summary>Warn text for assigning <paramref name="profile"/> to <paramref name="bag"/>,
        /// or null when there is nothing to say. NEVER blocks — an edge case the player understands
        /// better than we do must not be refused (redesign plan: "blocking is too hostile").
        ///
        /// <para>Only typed packs are checked (a mining backpack whose slots are all
        /// <c>Ore</c>): a rule whose items cannot enter ANY of those typed slots is reported. The
        /// compatibility test is the EXISTING <see cref="InventoryScanner.IsTypeCompatible"/>
        /// (class match + the slot's <c>SpecificTypePrefabHashes</c> lock) — no new rules here.</para>
        ///
        /// <para>Cost: one pass over <c>DynamicThing.DynamicThingPrefabs</c> (~800 templates), and
        /// only when the profile carries broad (category / UIA-class) rules or item rules that
        /// still need resolving. Call this on an ASSIGNMENT GESTURE only — never per frame.</para>
        ///
        /// <para>ASCII only: the string is displayed through TextMeshPro.</para></summary>
        public static string ValidateAssignment(DynamicThing bag, BagProfile profile)
        {
            if (bag == null || profile == null) return null;
            Slot.Class? typedNullable = TypedClassOf(bag);
            if (!typedNullable.HasValue) return null;          // generic pack: nothing to validate
            Slot.Class typed = typedNullable.Value;

            // The typed slots we judge against (a lone None slot is real but is not the pack's
            // purpose; a rule that can only land there is still worth flagging).
            var typedSlots = new List<Slot>(bag.Slots.Count);
            for (int i = 0; i < bag.Slots.Count; i++)
            {
                Slot s = bag.Slots[i];
                if (s != null && s.Type == typed) typedSlots.Add(s);
            }
            if (typedSlots.Count == 0) return null;

            var bad = new List<string>();

            // 1) Slot-class rules: decidable without touching the prefab table.
            for (int i = 0; i < profile.SlotClasses.Count; i++)
            {
                var r = profile.SlotClasses[i];
                Slot.Class cls;
                if (r == null || !r.TryGetSlotClass(out cls)) continue;
                if (cls != typed) AddBad(bad, "slot class " + cls);
            }

            // 2) Item / category / UIA-class rules need real prefabs. Gather what is unresolved,
            //    then settle all of them in ONE walk of the prefab table.
            var wantedItems = new Dictionary<string, bool>(StringComparer.Ordinal); // prefab -> fits
            var seenItems = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profile.Items.Count; i++)
            {
                var r = profile.Items[i];
                if (r == null || string.IsNullOrEmpty(r.Prefab)) continue;
                if (!wantedItems.ContainsKey(r.Prefab)) wantedItems[r.Prefab] = false;
            }

            var wantedCategories = new Dictionary<SortingClass, bool>();
            for (int i = 0; i < profile.Categories.Count; i++)
            {
                var r = profile.Categories[i];
                SortingClass sc;
                if (r == null || !r.TryGetSortingClass(out sc)) continue;
                if (!wantedCategories.ContainsKey(sc)) wantedCategories[sc] = false;
            }

            var wantedClasses = new Dictionary<UIAClass, bool>();
            for (int i = 0; i < profile.UIAClasses.Count; i++)
            {
                var r = profile.UIAClasses[i];
                UIAClass uc;
                if (r == null || !r.TryGetUIAClass(out uc)) continue;
                if (!wantedClasses.ContainsKey(uc)) wantedClasses[uc] = false;
            }

            if (wantedItems.Count > 0 || wantedCategories.Count > 0 || wantedClasses.Count > 0)
                ScanPrefabs(typedSlots, wantedItems, seenItems, wantedCategories, wantedClasses);

            foreach (var kv in wantedItems)
            {
                if (kv.Value) continue;
                if (!seenItems.Contains(kv.Key)) continue;   // prefab unknown to this build - can't judge
                AddBad(bad, kv.Key);
            }
            foreach (var kv in wantedCategories)
                if (!kv.Value) AddBad(bad, "category " + kv.Key);
            foreach (var kv in wantedClasses)
                if (!kv.Value) AddBad(bad, UIASort.DisplayName(kv.Key) + " (UIA class)");

            if (bad.Count == 0) return null;

            string list = bad[0];
            int shown = Math.Min(bad.Count, 3);
            for (int i = 1; i < shown; i++) list += ", " + bad[i];
            if (bad.Count > shown) list += " and " + (bad.Count - shown) + " more";
            return "Heads up: " + typedSlots.Count + " of this container's slots only take "
                + typed + ". " + bad.Count + " rule" + (bad.Count == 1 ? "" : "s")
                + " cannot fit there: " + list + ".";
        }

        /// <summary>One walk of the prefab table settling every unresolved rule at once. Bails as
        /// soon as everything is answered.</summary>
        private static void ScanPrefabs(List<Slot> typedSlots,
            Dictionary<string, bool> wantedItems, HashSet<string> seenItems,
            Dictionary<SortingClass, bool> wantedCategories, Dictionary<UIAClass, bool> wantedClasses)
        {
            List<DynamicThing> prefabs;
            try { prefabs = DynamicThing.DynamicThingPrefabs; }
            catch { return; }
            if (prefabs == null) return;

            int itemsLeft = wantedItems.Count;
            int catsLeft = wantedCategories.Count;
            int classesLeft = wantedClasses.Count;

            for (int i = 0; i < prefabs.Count; i++)
            {
                DynamicThing t = prefabs[i];
                if (t == null) continue;
                if (itemsLeft == 0 && catsLeft == 0 && classesLeft == 0) return;

                bool? fits = null;   // computed at most once per prefab

                string prefabName = null;
                try { prefabName = t.PrefabName; } catch { }
                if (prefabName != null && wantedItems.ContainsKey(prefabName))
                {
                    seenItems.Add(prefabName);
                    if (!wantedItems[prefabName])
                    {
                        fits = FitsAny(typedSlots, t);
                        if (fits.Value) { wantedItems[prefabName] = true; itemsLeft--; }
                    }
                }

                if (catsLeft > 0)
                {
                    SortingClass sc;
                    try { sc = t.SortingClass; } catch { continue; }
                    bool already;
                    if (wantedCategories.TryGetValue(sc, out already) && !already)
                    {
                        if (!fits.HasValue) fits = FitsAny(typedSlots, t);
                        if (fits.Value) { wantedCategories[sc] = true; catsLeft--; }
                    }
                }

                if (classesLeft > 0)
                {
                    UIAClass uc = UIASort.Classify(t);
                    bool already;
                    if (wantedClasses.TryGetValue(uc, out already) && !already)
                    {
                        if (!fits.HasValue) fits = FitsAny(typedSlots, t);
                        if (fits.Value) { wantedClasses[uc] = true; classesLeft--; }
                    }
                }
            }
        }

        private static bool FitsAny(List<Slot> slots, DynamicThing thing)
        {
            for (int i = 0; i < slots.Count; i++)
                if (InventoryScanner.IsTypeCompatible(slots[i], thing)) return true;
            return false;
        }

        private static void AddBad(List<string> bad, string label)
        {
            if (string.IsNullOrEmpty(label)) return;
            for (int i = 0; i < bad.Count; i++) if (bad[i] == label) return;
            bad.Add(label);
        }
    }
}
