using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The 1–6 equipment keys, reimagined: TAP opens a management radial for that piece of
    /// equipment (on/off, slots, swap candidates — e.g. tap 2 to toggle your sensor lenses,
    /// tap 3 to swap your suit's battery), HOLD equips/unequips it to the active hand
    /// (vanilla's hold behavior, through the same multiplayer-safe funnel).
    /// The vanilla polled handling is suppressed by a Harmony prefix while enabled.
    /// </summary>
    public sealed class EquipmentKeyRadialFeature : IRadialFeature
    {
        /// <summary>Vanilla button names, in 1–6 order (KeyManager.SetDefaultKeys, KeyManager.cs:408-413).</summary>
        public static readonly string[] ButtonNames =
        {
            "HelmetSlot", "GlassesSlot", "SuitSlot", "BackSlot", "UniformSlot", "ToolBeltSlot",
        };

        private readonly string _buttonName;
        private readonly Func<Human, Slot> _slotGetter;

        public EquipmentKeyRadialFeature(string buttonName, Func<Human, Slot> slotGetter)
        {
            _buttonName = buttonName;
            _slotGetter = slotGetter;
        }

        public static IEnumerable<EquipmentKeyRadialFeature> CreateAll()
        {
            yield return new EquipmentKeyRadialFeature("HelmetSlot", h => h.HelmetSlot);
            yield return new EquipmentKeyRadialFeature("GlassesSlot", h => h.GlassesSlot);
            yield return new EquipmentKeyRadialFeature("SuitSlot", h => h.SuitSlot);
            yield return new EquipmentKeyRadialFeature("BackSlot", h => h.BackpackSlot);
            yield return new EquipmentKeyRadialFeature("UniformSlot", h => h.UniformSlot);
            yield return new EquipmentKeyRadialFeature("ToolBeltSlot", h => h.ToolbeltSlot);
        }

        private Slot EquipSlot
        {
            get
            {
                var human = Guards.LocalHuman;
                if (human == null) return null;
                try { return _slotGetter(human); } catch { return null; }
            }
        }

        public string Title => EquipSlot?.Get()?.DisplayName ?? _buttonName;
        /// <summary>Always on (the post-0.9.2.5 play-test round): the per-wheel enable toggles are gone — the radial half's
        /// master switch (<c>UIAConfig.RadialEnabled</c>) is the only gate.</summary>
        public bool Enabled => true;

        /// <summary>Live-resolved so user rebinds in the vanilla Controls menu are respected.</summary>
        public KeyCode Key
        {
            get
            {
                try { return KeyManager.GetKey(_buttonName); }
                catch { return KeyCode.None; }
            }
        }

        public bool OpenOnTap => true;
        public bool OpensOnBoth => false;

        public string ButtonName => _buttonName;

        public bool CanOpen() => EquipSlot?.Get() != null;

        /// <summary>True when this feature has any action for the current state — used to decide
        /// whether the vanilla polled handling should be suppressed for this button.</summary>
        public bool CanAct()
        {
            var slot = EquipSlot;
            if (slot == null) return false;
            if (slot.Get() != null) return true;
            var held = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot?.Get();
            return held != null && Slot.AllowMove(held, slot);
        }

        public List<RadialEntry> BuildRoot()
        {
            var slot = EquipSlot;
            var occ = slot?.Get();
            if (occ == null) return new List<RadialEntry>();
            return ItemMenuBuilder.BuildManageEntries(occ, slot, includeTakeEntry: true);
        }

        public void OnTap() { }

        /// <summary>Hold = vanilla-style equip semantics, both directions: occupied slot → take
        /// to hand; empty slot + fitting held item → don it (vanilla's hold-to-don, which our
        /// CheckDisplaySlot suppression would otherwise remove).</summary>
        public void OnHold()
        {
            var slot = EquipSlot;
            if (slot == null) return;
            if (slot.Get() != null)
            {
                var source = new ScannedSlot { Slot = slot, Holder = slot.Parent, Location = _buttonName }.Pin();
                ItemActions.EquipToActiveHand(source);
                return;
            }
            var held = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot?.Get();
            if (held != null && Slot.AllowMove(held, slot))
                ItemActions.StowActiveHandTo(slot);
        }
    }
}

