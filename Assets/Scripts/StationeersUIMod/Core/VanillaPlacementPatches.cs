using Assets.Scripts;                 // IMergeable
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using HarmonyLib;
using StationeersUIMod.Features;

namespace StationeersUIMod.Core
{
    // =============================================================================================
    // Simple SmartStow — the OBSERVE side (0.9.8.0 P1). Eight independent fail-soft patch classes
    // (each registered by name in StationeersUIMod.OnLoaded's PatchHarness.TryPatchAll list, so a
    // game update that moves ONE target degrades one signal, never the whole capture):
    //
    //   * Slot.Take postfix              — the landing commit (StowHomeStore.ObserveLanding).
    //   * five Slot.Player* prefixes     — vanilla EXPLICIT gestures register intents.
    //   * Stackable.OnSplitStack postfix — split child inherits the source's home (host/SP).
    //   * Thing.Merge prefix             — a homeless merge survivor inherits the child's home.
    //
    // Every target verified against the LIVE build's decompile,
    // Reference/StationeersGameVersions/Stationeers 8-13-26 V27798 Orbital Update Beta/Assembly-CSharp:
    //   Slot.Take(DynamicThing)                    Assets.Scripts.Objects/Slot.cs:365
    //   Slot.PlayerMoveToSlot(DynamicThing)        Slot.cs:710  (MoveAll tail :718-726 → container intent)
    //   Slot.PlayerSwapToWorld(DynamicThing)       Slot.cs:794
    //   Slot.PlayerMergeToSlot(IMergeable)         Slot.cs:802
    //   Slot.PlayerSwapToSlot(Slot)                Slot.cs:818
    //   Slot.PlayerInsertToFreeSlot(DynamicThing)  Slot.cs:837
    //   Stackable.OnSplitStack(Stackable)          Assets.Scripts.Objects.Items/Stackable.cs:379
    //                                              (called from both SplitStack paths, :321 and :340)
    //   Thing.Merge(IMergeable, IMergeable) static Assets.Scripts.Objects/Thing.cs:3594
    //   KeyMap.MoveAll / MoveAllOfType             KeyMap.cs (global namespace) :277-279
    //
    // EVERYTHING here is observation: no patch mutates game state, skips an original, or changes a
    // return value (the Player* methods return void in 27798; our prefixes are void, so the
    // originals always run). This is a NEW Slot.Take postfix beside BeltBindingStore.SlotTakePatch —
    // Harmony runs multiple postfixes on one method; the belt patch is untouched.
    //
    // Prefixes ignore calls made inside StowHomeStore's MECHANICAL scope: ItemActions performs an
    // internal PlayerSwapToSlot for the displaced-hand swap (ItemActions.cs, the 0.9.7.3
    // two-message pattern), and that must never read as an explicit player gesture. The later
    // resolver/executor phase wraps those internals in BeginMechanical()/EndMechanical().
    // =============================================================================================

    /// <summary>Landing observer: the single point where a Thing settles into a slot (both the
    /// host's synchronous apply and an MP client's server echo flow through
    /// <c>DynamicThing.MoveToSlot</c>/<c>DragInSlot</c> → <c>Slot.Take</c>).
    /// COLD PATH: <c>Slot.Take</c> fires for every chute hop on a host, so
    /// <see cref="StowHomeStore.ObserveLanding"/> exits in a few pointer reads for anything that is
    /// not the local player's inventory. `child` is null for <c>Slot.Empty()</c>.</summary>
    [HarmonyPatch(typeof(Slot), "Take")]
    internal static class Patch_Slot_Take_StowHomes
    {
        [HarmonyPostfix]
        private static void Postfix(Slot __instance, DynamicThing child)
        {
            try { StowHomeStore.ObserveLanding(__instance, child); }
            catch { /* fail-soft: observation must never break a slot move */ }
        }
    }

    /// <summary>Vanilla drag/scroll MOVE into a slot: an explicit placement of
    /// <c>thingToMove</c> into <c>__instance</c>. With MoveAll / MoveAllOfType held the method also
    /// sweeps whole stacks into the destination CONTAINER (Slot.cs:718-726), so those get a
    /// container-scoped intent covering every landing of the sweep.</summary>
    [HarmonyPatch(typeof(Slot), "PlayerMoveToSlot")]  // [27798] Slot.cs:710
    internal static class Patch_Slot_PlayerMoveToSlot_StowIntent
    {
        [HarmonyPrefix]
        private static void Prefix(Slot __instance, DynamicThing thingToMove)
        {
            try
            {
                if (__instance == null || thingToMove == null) return;
                if (StowHomeStore.InMechanicalScope) return;
                long cid;
                if (!StowHomeStore.TryGetLocalContainerId(__instance.Parent, out cid)) return;
                StowHomeStore.NoteItemIntent(thingToMove.ReferenceId, cid, __instance.SlotIndex);
                // MoveAll / MoveAllOfType tail: explicit for the whole destination container.
                if (KeyManager.GetButton(KeyMap.MoveAll) || KeyManager.GetButton(KeyMap.MoveAllOfType))
                    StowHomeStore.NoteContainerIntent(cid);
            }
            catch { }
        }
    }

    /// <summary>Vanilla SWAP between two slots: <c>__instance</c>'s occupant goes to
    /// <paramref name="slot"/> and vice versa. A bag↔bag swap is an explicit exchange — BOTH
    /// landings rehome (the 0.9.7.3 wedge-over-wedge rule). When EITHER side is a hand, nothing is
    /// noted: the item entering the hand is being TAKEN (hand landings never rehome anyway) and the
    /// displaced held item is a MECHANICAL landing that must not steal a home (plan §4.2).</summary>
    [HarmonyPatch(typeof(Slot), "PlayerSwapToSlot")]  // [27798] Slot.cs:818
    internal static class Patch_Slot_PlayerSwapToSlot_StowIntent
    {
        [HarmonyPrefix]
        private static void Prefix(Slot __instance, Slot slot)
        {
            try
            {
                if (__instance == null || slot == null) return;
                if (StowHomeStore.InMechanicalScope) return;
                if (__instance.IsHandSlot || slot.IsHandSlot) return;   // hand-involved: mechanical side

                DynamicThing a = __instance.Get();   // lands in `slot`
                DynamicThing b = slot.Get();         // lands in `__instance`
                long cidTo, cidFrom;
                if (a != null && StowHomeStore.TryGetLocalContainerId(slot.Parent, out cidTo))
                    StowHomeStore.NoteItemIntent(a.ReferenceId, cidTo, slot.SlotIndex);
                if (b != null && StowHomeStore.TryGetLocalContainerId(__instance.Parent, out cidFrom))
                    StowHomeStore.NoteItemIntent(b.ReferenceId, cidFrom, __instance.SlotIndex);
            }
            catch { }
        }
    }

    /// <summary>Vanilla MERGE onto an occupied slot: the player chose that slot as the stack's
    /// destination. A pure merge changes quantities without a <c>Slot.Take</c> landing (the child
    /// merges away, or keeps its remainder in place), so this intent usually just expires — it
    /// exists for the paths where a landing DOES occur, and costs nothing otherwise. The
    /// survivor-inherits rule itself lives on the <c>Thing.Merge</c> funnel prefix below.</summary>
    [HarmonyPatch(typeof(Slot), "PlayerMergeToSlot")]  // [27798] Slot.cs:802
    internal static class Patch_Slot_PlayerMergeToSlot_StowIntent
    {
        [HarmonyPrefix]
        private static void Prefix(Slot __instance, IMergeable stackable)
        {
            try
            {
                if (__instance == null || __instance.IsHandSlot) return;
                if (StowHomeStore.InMechanicalScope) return;
                DynamicThing moving = stackable as DynamicThing;
                if (moving == null) return;
                long cid;
                if (!StowHomeStore.TryGetLocalContainerId(__instance.Parent, out cid)) return;
                StowHomeStore.NoteItemIntent(moving.ReferenceId, cid, __instance.SlotIndex);
            }
            catch { }
        }
    }

    /// <summary>Vanilla INSERT into the container occupying <c>__instance</c>: vanilla picks the
    /// first free slot itself (Slot.cs:842-851), so the exact index is unknowable at dispatch —
    /// a container-scoped intent covers whichever slot the item lands in. The prefix mirrors the
    /// original's own early-outs ([27798] Slot.cs:838-841: no occupant, the item matches the
    /// SLOT's own class, same prefab as the occupant, or the occupant has no slots — vanilla
    /// returns without moving anything in all of those), so a no-op gesture never leaves a live
    /// 3 s container intent behind to mis-classify an unrelated landing.</summary>
    [HarmonyPatch(typeof(Slot), "PlayerInsertToFreeSlot")]  // [27798] Slot.cs:837
    internal static class Patch_Slot_PlayerInsertToFreeSlot_StowIntent
    {
        [HarmonyPrefix]
        private static void Prefix(Slot __instance, DynamicThing slotOccupant)
        {
            try
            {
                if (__instance == null || slotOccupant == null) return;
                if (StowHomeStore.InMechanicalScope) return;
                DynamicThing container = __instance.Get();
                if (container == null) return;                              // vanilla: !Occupant
                if (slotOccupant.SlotType == __instance.Type) return;       // vanilla: belongs in this slot itself
                if (container.PrefabHash == slotOccupant.PrefabHash) return; // vanilla: same prefab
                if (!container.HasSlots) return;                            // vanilla: nothing to insert into
                long cid;
                if (!StowHomeStore.TryGetLocalContainerId(container, out cid)) return;
                StowHomeStore.NoteContainerIntent(cid);
            }
            catch { }
        }
    }

    /// <summary>Vanilla swap with a WORLD item: the world thing comes INTO <c>__instance</c> (the
    /// slot's old occupant leaves for the world — leaving never changes a home). Explicit for the
    /// incoming item unless the slot is a hand (taking).</summary>
    [HarmonyPatch(typeof(Slot), "PlayerSwapToWorld")]  // [27798] Slot.cs:794
    internal static class Patch_Slot_PlayerSwapToWorld_StowIntent
    {
        [HarmonyPrefix]
        private static void Prefix(Slot __instance, DynamicThing dynamicThing)
        {
            try
            {
                if (__instance == null || dynamicThing == null || __instance.IsHandSlot) return;
                if (StowHomeStore.InMechanicalScope) return;
                long cid;
                if (!StowHomeStore.TryGetLocalContainerId(__instance.Parent, out cid)) return;
                StowHomeStore.NoteItemIntent(dynamicThing.ReferenceId, cid, __instance.SlotIndex);
            }
            catch { }
        }
    }

    /// <summary>Split lineage, host/SP (plan §5.2 rule b): every split path creates the child and
    /// then calls <c>OnSplitStack(newStack)</c> (Stackable.cs:321, :340 — after
    /// <c>OnServer.Create</c>, so the child already has its ReferenceId), and every override calls
    /// base — one postfix sees them all. Splits run only on the simulation authority
    /// (Stackable.cs:299), so an MP client relies on the split-intent inference instead
    /// (<see cref="StowHomeStore.NoteSplitIntent"/>, wired at the ItemActions dispatch sites in the
    /// P2/P3 phase).</summary>
    [HarmonyPatch(typeof(Stackable), "OnSplitStack")]  // [27798] Stackable.cs:379 (protected virtual)
    internal static class Patch_Stackable_OnSplitStack_StowHomes
    {
        [HarmonyPostfix]
        private static void Postfix(Stackable __instance, Stackable newStack)
        {
            try { StowHomeStore.ObserveSplit(__instance, newStack); }
            catch { }
        }
    }

    /// <summary>Merge lineage (plan §5.2 rule e): observed at the one static funnel every UI merge
    /// goes through — host runs <c>OnServer.Merge</c>, a client sends <c>NetworkClient.Merge</c>
    /// (Thing.cs:3594-3604) — BEFORE quantities change, while both stacks still hold their
    /// identities. A survivor with no live home inherits the absorbed stack's.</summary>
    [HarmonyPatch(typeof(Thing), "Merge", new[] { typeof(IMergeable), typeof(IMergeable) })]  // [27798] Thing.cs:3594 (static)
    internal static class Patch_Thing_Merge_StowHomes
    {
        [HarmonyPrefix]
        private static void Prefix(IMergeable parent, IMergeable child)
        {
            try { StowHomeStore.ObserveMerge(parent as Thing, child as Thing); }
            catch { }
        }
    }
}
