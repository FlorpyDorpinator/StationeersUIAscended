using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace StationeersUIMod.Features
{
    // ---------------------------------------------------------------- capture model ----

    public enum CaptureRuleKind
    {
        Item,      // one prefab -> ItemRule
        Category,  // a SortingClass cluster -> CategoryRule (or member ItemRules when un-generalized)
        SlotClass, // a Slot.Class cluster -> SlotClassRule (or member ItemRules when un-generalized)
    }

    public enum CaptureApplyMode
    {
        NewProfile, // create a fresh profile (name de-collided with " 2", " 3", ...)
        Update,     // replace the target profile's rules with the captured set
        Merge,      // add captured rules the target profile doesn't already have
    }

    /// <summary>One distinct prefab behind a proposal line — retained so the UI can
    /// un-generalize a Category/SlotClass line back into its raw item rules.</summary>
    public sealed class CaptureMember
    {
        public string Prefab;      // PrefabName
        public string DisplayName; // client-safe display name
        public int Count;          // occupant instances (a stack in one slot counts once)
    }

    /// <summary>One toggleable row of the capture confirm panel (design O3b).</summary>
    public sealed class CaptureLine
    {
        public CaptureRuleKind Kind;
        public string RuleName;    // SortingClass / Slot.Class enum name; PrefabName for Item lines
        public string DisplayName; // plain human label ("Ores", "Battery", "Flashlight") — UI adds the "Category:" prefix
        public int Count;          // total occupant instances behind this line
        public bool Included = true;
        public bool Generalized;   // Category/SlotClass only: false = save Members as item rules instead
        public List<CaptureMember> Members = new List<CaptureMember>();

        public bool CanGeneralize { get { return Kind != CaptureRuleKind.Item; } }
    }

    /// <summary>What a capture would save — pure data, no game references beyond the bag
    /// itself. Build one, let the player toggle lines in the confirm panel, then Apply.</summary>
    public sealed class CaptureProposal
    {
        public DynamicThing Bag;
        public string SuggestedName; // ASCII-sanitised bag DisplayName
        public int TotalItems;       // direct occupants seen (0 = the bag was empty)
        public List<CaptureLine> Lines = new List<CaptureLine>();

        public bool HasIncludedLines
        {
            get
            {
                for (int i = 0; i < Lines.Count; i++)
                    if (Lines[i] != null && Lines[i].Included) return true;
                return false;
            }
        }
    }

    // ------------------------------------------------------------------ the engine ----

    /// <summary>
    /// Generalizing capture (design O3b/O3c): turn a hand-organized bag into a robust
    /// profile. BuildProposal clusters the bag's DIRECT occupants (contents of nested bags
    /// belong to the nested bag's own capture; the nested bag itself counts as an occupant)
    /// into category rules (&gt;= 3 sharing a SortingClass), then slot-class rules (&gt;= 3
    /// remaining sharing a Slot.Class, None excluded), then item rules for the oddballs.
    /// Apply writes the profile through BagProfileStore and ALWAYS auto-assigns it to the
    /// captured bag (FlorpyDorp Q3). Everything here is local config — no game-state
    /// mutation, no MP concern, no statics (nothing to reset on hot reload).
    /// </summary>
    public static class ProfileCapture
    {
        /// <summary>Minimum cluster size before a general rule is proposed over item rules.</summary>
        public const int ClusterThreshold = 3;

        private const int MaxNameLength = 40;

        /// <summary>Cluster the bag's direct contents into a rule proposal. Null only when
        /// the bag is unusable (null / no slots); an EMPTY bag returns a proposal with zero
        /// lines (TotalItems == 0) so the UI can say "nothing to capture". Locked slots are
        /// skipped, matching what the player can actually see and reach.</summary>
        public static CaptureProposal BuildProposal(DynamicThing bag)
        {
            if (bag == null || bag.Slots == null) return null;

            var occupants = new List<DynamicThing>();
            foreach (Slot s in bag.Slots)
            {
                if (s == null || s.IsLocked) continue;
                DynamicThing occ = s.Get();
                if (occ != null) occupants.Add(occ);
            }

            var proposal = new CaptureProposal
            {
                Bag = bag,
                SuggestedName = SanitizeName(SafeDisplayName(bag)),
                TotalItems = occupants.Count,
            };
            if (occupants.Count == 0) return proposal;

            bool[] used = new bool[occupants.Count];

            // 1) Sorting-category clusters, in first-appearance order (stable panel layout).
            //    SortingClass.Default clusters too — a deliberately mixed "junk" bag becomes
            //    a Default-category profile; the confirm panel lets the player untick it.
            for (int i = 0; i < occupants.Count; i++)
            {
                if (used[i]) continue;
                SortingClass sc = occupants[i].SortingClass;
                int count = CountRemaining(occupants, used, sc, Slot.Class.None, true);
                if (count < ClusterThreshold) continue;
                var line = new CaptureLine
                {
                    Kind = CaptureRuleKind.Category,
                    RuleName = sc.ToString(),
                    DisplayName = sc.ToString(),
                    Count = count,
                    Generalized = true,
                };
                for (int j = i; j < occupants.Count; j++)
                    if (!used[j] && occupants[j].SortingClass == sc) { AddMember(line, occupants[j]); used[j] = true; }
                proposal.Lines.Add(line);
            }

            // 2) Slot-class clusters among what remains. Slot.Class.None never clusters:
            //    a "None" rule would match every generic item in the game (the same reason
            //    the affinity scorer skips None).
            for (int i = 0; i < occupants.Count; i++)
            {
                if (used[i]) continue;
                Slot.Class cls = occupants[i].SlotType;
                if (cls == Slot.Class.None) continue;
                int count = CountRemaining(occupants, used, SortingClass.Default, cls, false);
                if (count < ClusterThreshold) continue;
                var line = new CaptureLine
                {
                    Kind = CaptureRuleKind.SlotClass,
                    RuleName = cls.ToString(),
                    DisplayName = cls.ToString(),
                    Count = count,
                    Generalized = true,
                };
                for (int j = i; j < occupants.Count; j++)
                    if (!used[j] && occupants[j].SlotType == cls) { AddMember(line, occupants[j]); used[j] = true; }
                proposal.Lines.Add(line);
            }

            // 3) Remainder -> one Item line per distinct prefab (duplicates fold into Count).
            for (int i = 0; i < occupants.Count; i++)
            {
                if (used[i]) continue;
                string prefab = occupants[i].PrefabName;
                var line = new CaptureLine
                {
                    Kind = CaptureRuleKind.Item,
                    RuleName = prefab,
                    DisplayName = SafeDisplayName(occupants[i]),
                    Generalized = false,
                };
                for (int j = i; j < occupants.Count; j++)
                {
                    if (used[j] || occupants[j].PrefabName != prefab) continue;
                    AddMember(line, occupants[j]);
                    used[j] = true;
                    line.Count++;
                }
                proposal.Lines.Add(line);
            }

            return proposal;
        }

        /// <summary>Write the proposal as a profile and auto-assign it to the captured bag
        /// (Q3 — always). Returns the FINAL profile name (NewProfile may de-collide it with
        /// " 2"/" 3"), or null when nothing was saved (no included lines / dead proposal).
        /// Update replaces the target's rules wholesale; Merge only adds missing rules and
        /// never touches existing priorities. The Update/Merge target is resolved by the RAW
        /// name (hand-edited XML names need not survive SanitizeName); only a genuinely
        /// missing target fail-softs to creating a sanitized, de-collided profile.
        /// Local config writes only — no game state.</summary>
        public static string Apply(CaptureProposal proposal, string name, CaptureApplyMode mode)
        {
            if (proposal == null || proposal.Bag == null) return null;

            var items = new List<ItemRule>();
            var categories = new List<CategoryRule>();
            var slotClasses = new List<SlotClassRule>();
            for (int i = 0; i < proposal.Lines.Count; i++)
            {
                CaptureLine line = proposal.Lines[i];
                if (line == null || !line.Included) continue;
                if (line.Kind == CaptureRuleKind.Category && line.Generalized)
                    AddCategoryRule(categories, line.RuleName);
                else if (line.Kind == CaptureRuleKind.SlotClass && line.Generalized)
                    AddSlotClassRule(slotClasses, line.RuleName);
                else
                {
                    // Item line, or an un-generalized cluster: raw item rules for the members.
                    for (int m = 0; m < line.Members.Count; m++)
                    {
                        CaptureMember member = line.Members[m];
                        if (member != null && !string.IsNullOrEmpty(member.Prefab))
                            AddItemRule(items, member.Prefab);
                    }
                }
            }
            if (items.Count == 0 && categories.Count == 0 && slotClasses.Count == 0) return null;

            string finalName;
            if (mode == CaptureApplyMode.NewProfile)
            {
                finalName = SanitizeName(name);
                if (string.IsNullOrEmpty(finalName)) finalName = proposal.SuggestedName;
                if (string.IsNullOrEmpty(finalName)) finalName = "Captured Bag";
                finalName = BagProfileStore.UniqueProfileName(finalName);
                BagProfileStore.Profiles.Add(new BagProfile
                {
                    Name = finalName,
                    Items = items,
                    Categories = categories,
                    SlotClasses = slotClasses,
                });
            }
            else
            {
                // UPDATE/MERGE target the EXISTING profile: resolve it by the RAW name the caller
                // handed us (the bag's live assignment, verbatim — the way PinItemRule does).
                // Profile names loaded from hand-edited/shared XML need not survive SanitizeName
                // (non-ASCII, doubled spaces, >40 chars); sanitizing BEFORE the lookup used to
                // miss the target and silently fork a near-duplicate under the sanitized name
                // (verified defect, 2026-07-20). Sanitize only on the fail-soft CREATE below.
                finalName = name;
                BagProfile target = BagProfileStore.FindProfile(finalName);
                if (target == null)
                {
                    finalName = SanitizeName(name);
                    if (string.IsNullOrEmpty(finalName)) finalName = proposal.SuggestedName;
                    if (string.IsNullOrEmpty(finalName)) finalName = "Captured Bag";
                    finalName = BagProfileStore.UniqueProfileName(finalName); // never collide with an unrelated profile
                    BagProfileStore.Profiles.Add(new BagProfile
                    {
                        Name = finalName,
                        Items = items,
                        Categories = categories,
                        SlotClasses = slotClasses,
                    });
                }
                else if (mode == CaptureApplyMode.Update)
                {
                    target.Items = items;
                    target.Categories = categories;
                    target.SlotClasses = slotClasses;
                    // UIAClasses are deliberately LEFT ALONE: capture clusters by SortingClass and
                    // Slot.Class only, so it can never propose a UIA-class rule — wiping the ones
                    // the player hand-added would be silent data loss with no way to get them back.
                }
                else // Merge
                {
                    for (int i = 0; i < items.Count; i++) AddItemRule(target.Items, items[i].Prefab);
                    for (int i = 0; i < categories.Count; i++) AddCategoryRule(target.Categories, categories[i].Name);
                    for (int i = 0; i < slotClasses.Count; i++) AddSlotClassRule(target.SlotClasses, slotClasses[i].Name);
                }
            }

            BagProfileStore.SaveProfiles();
            BagProfileStore.Assign(proposal.Bag, finalName); // Q3: always bind the captured bag (saves assignments too)
            return finalName;
        }

        /// <summary>Drag-to-pin (design O4c): "THIS goes THERE" as one gesture. Adds an item rule
        /// for <paramref name="itemPrefab"/> to the profile assigned to <paramref name="bag"/>;
        /// when the bag has no (or a dangling) assignment, a profile named after the bag is
        /// created and auto-assigned first. Deduped (an existing rule is left untouched), saved
        /// through the store, config writes ONLY — no game-state mutation, the item does not move.
        /// Returns the profile name the rule landed in, or null on unusable input.</summary>
        public static string PinItemRule(DynamicThing bag, string itemPrefab)
        {
            if (bag == null || string.IsNullOrEmpty(itemPrefab)) return null;

            string name = BagProfileStore.GetAssignedProfileName(bag);
            BagProfile profile = string.IsNullOrEmpty(name) ? null : BagProfileStore.FindProfile(name);
            if (profile == null)
            {
                // No profile (or the assignment dangles at a deleted one): create one named after
                // the bag — the "unnamed profile, named on first capture" of the design, except a
                // bag display name is always available, so it is named at birth.
                string baseName = SanitizeName(SafeDisplayName(bag));
                if (string.IsNullOrEmpty(baseName)) baseName = "Captured Bag";
                name = BagProfileStore.UniqueProfileName(baseName);
                profile = new BagProfile { Name = name };
                BagProfileStore.Profiles.Add(profile);
            }

            AddItemRule(profile.Items, itemPrefab);
            BagProfileStore.SaveProfiles();
            BagProfileStore.Assign(bag, name);   // idempotent when already assigned; heals a dangle
            return name;
        }

        /// <summary>Printable-ASCII-only profile name (TMP-safe by construction): control
        /// chars and non-ASCII stripped, whitespace collapsed/trimmed, max 40 chars.</summary>
        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder(Math.Min(raw.Length, MaxNameLength));
            bool lastWasSpace = true; // swallows leading spaces
            for (int i = 0; i < raw.Length && sb.Length < MaxNameLength; i++)
            {
                char c = raw[i];
                if (c == '\t') c = ' ';
                if (c < 0x20 || c > 0x7E) continue;
                if (c == ' ')
                {
                    if (lastWasSpace) continue;
                    lastWasSpace = true;
                }
                else lastWasSpace = false;
                sb.Append(c);
            }
            while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.ToString();
        }

        // -------------------------------------------------------------------- helpers ----

        /// <summary>Count not-yet-consumed occupants matching a SortingClass (byCategory) or
        /// a Slot.Class — one loop shared by both cluster passes.</summary>
        private static int CountRemaining(List<DynamicThing> occupants, bool[] used, SortingClass sc, Slot.Class cls, bool byCategory)
        {
            int count = 0;
            for (int i = 0; i < occupants.Count; i++)
            {
                if (used[i]) continue;
                if (byCategory ? occupants[i].SortingClass == sc : occupants[i].SlotType == cls) count++;
            }
            return count;
        }

        private static void AddMember(CaptureLine line, DynamicThing occ)
        {
            string prefab = occ.PrefabName;
            for (int i = 0; i < line.Members.Count; i++)
            {
                if (line.Members[i].Prefab == prefab)
                {
                    line.Members[i].Count++;
                    return;
                }
            }
            line.Members.Add(new CaptureMember { Prefab = prefab, DisplayName = SafeDisplayName(occ), Count = 1 });
        }

        private static void AddItemRule(List<ItemRule> list, string prefab)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Prefab, prefab, StringComparison.Ordinal)) return;
            list.Add(new ItemRule { Prefab = prefab }); // model default priority (100)
        }

        private static void AddCategoryRule(List<CategoryRule> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(new CategoryRule { Name = name }); // model default priority (50)
        }

        private static void AddSlotClassRule(List<SlotClassRule> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(new SlotClassRule { Name = name }); // model default priority (60)
        }

        /// <summary>DisplayName is client-safe (CustomName is networked; the localization
        /// lookup is local — verified R3), but capture is a UI click, so belt-and-braces.</summary>
        private static string SafeDisplayName(Thing thing)
        {
            try
            {
                string n = thing.DisplayName;
                return string.IsNullOrEmpty(n) ? thing.PrefabName : n;
            }
            catch
            {
                return thing != null ? thing.PrefabName : null;
            }
        }
    }
}
