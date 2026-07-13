using System;
using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Clothing;
using Assets.Scripts.Objects.Items;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Option A device settings, enumerated EXACTLY like vanilla's inventory window
    /// (InventoryWindow.SetInteractions, 27701): iterate Thing.Interactables, filter
    /// CanKeyInteract &amp;&amp; Slot == null, label from Interactable.ContextualName (live,
    /// state-baked: "Stabilizer On", "A/C Off"), disabled state from the side-effect-free
    /// dry-run Parent.InteractWith(i, interaction, doAction:false).
    ///
    /// Stepped ButtonN PAIRS collapse into one triangle scroll wedge. TWO suit families
    /// exist with identical button semantics — SuitBase and Suit : AtmosphericItem (real
    /// worn suits!) — plus Jetpack and DynamicGasCanister; anything else with real
    /// Button1/2 or Button3/4 pairs gets a generic scroll wedge. One wheel notch sends
    /// `coarse` vanilla button presses (suit pressure/temp: 10, hold the fine-adjust key
    /// for 1; thrust/valve: 1 — thrust has 19 steps total, the valve steps ±10 natively).
    /// Values display in VANILLA units (suit pressure kPa, temp °C, thrust = the HUD's
    /// unitless ×100 number).
    /// </summary>
    public static class DeviceControls
    {
        /// <summary>Every real control on the thing as radial entries (Option A).</summary>
        public static List<RadialEntry> BuildEntries(DynamicThing thing)
        {
            var entries = new List<RadialEntry>();
            if (thing == null || thing.Interactables == null) return entries;

            var consumed = new HashSet<Interactable>();
            AddScrollPairs(entries, thing, consumed);

            Interactable lastRead = null;
            foreach (var interactable in thing.Interactables)
            {
                if (interactable == null || consumed.Contains(interactable)) continue;
                if (!ItemMenuBuilder.IsRealControl(interactable)) continue;

                // GasMask-family GetContextualName caches by the LAST-QUERIED Action
                // (GasMask.cs:114-138): re-querying the same action returns the pre-toggle
                // string. Prime with a different interactable's name so every label read
                // recomputes fresh — even across menu rebuilds on single-control items.
                PrimeLabelCache(thing, interactable, lastRead);
                lastRead = interactable;

                string label;
                bool disabled;
                string stateLine;
                ReadInteractable(thing, interactable, out label, out disabled, out stateLine);

                var i = interactable;
                var t = thing;
                entries.Add(new RadialEntry
                {
                    Label = label,
                    ActionText = label,
                    Sublabel = thing.DisplayName,
                    Enabled = !disabled,
                    DisabledReason = disabled ? (string.IsNullOrEmpty(stateLine) ? "Unavailable" : stateLine) : null,
                    OnSelect = () => ItemActions.PressInteractable(t, i),
                    HotkeyThing = t,          // #4: hover + a letter binds this setting to that key
                    HotkeyInteractable = i,
                });
            }
            return entries;
        }

        private static void PrimeLabelCache(DynamicThing thing, Interactable next, Interactable lastRead)
        {
            try
            {
                if (lastRead != null && lastRead.Action != next.Action) return; // cache already off this action
                foreach (var other in thing.Interactables)
                {
                    if (other == null || other == next || other.Action == next.Action) continue;
                    var unused = other.ContextualName; // pollute the cache with a different action
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Vanilla's own label/state read (Interactable.UpdateDisplay body, minus the UI):
        /// dry-run for IsDisabled, ContextualName for the state-aware label.
        /// </summary>
        private static void ReadInteractable(Thing thing, Interactable interactable,
            out string label, out bool disabled, out string stateLine)
        {
            label = null;
            disabled = false;
            stateLine = null;
            try
            {
                var interaction = InventoryManager.Parent != null
                    ? new Interaction(InventoryManager.Parent, InventoryManager.ActiveHandSlot, thing, false)
                    : default(Interaction);
                Thing.DelayedActionInstance dai;
                if (!thing.PreventInteraction(out dai, interactable, interaction))
                    dai = thing.InteractWith(interactable, interaction, false); // dry-run, no side effects
                if (dai != null)
                {
                    disabled = dai.IsDisabled;
                    stateLine = StateText.Strip(dai.GetStateMessage());
                }
            }
            catch { }
            try { label = StateText.Strip(interactable.ContextualName); } catch { }
            if (string.IsNullOrEmpty(label))
            {
                try { label = interactable.DisplayName; } catch { }
            }
            if (string.IsNullOrEmpty(label)) label = interactable.Action.ToString();
        }

        // ---------- stepped pairs -> scroll wedges ----------

        private static void AddScrollPairs(List<RadialEntry> entries, DynamicThing thing,
            HashSet<Interactable> consumed)
        {
            var b1 = thing.InteractButton1;
            var b2 = thing.InteractButton2;
            var b3 = thing.InteractButton3;
            var b4 = thing.InteractButton4;
            bool p12 = ItemMenuBuilder.IsRealControl(b1) && ItemMenuBuilder.IsRealControl(b2);
            bool p34 = ItemMenuBuilder.IsRealControl(b3) && ItemMenuBuilder.IsRealControl(b4);

            if (thing is Jetpack jp)
            {
                // Vanilla's HUD shows thrust as OutputSetting*100 with no unit — match it.
                // 19 steps total: coarse stepping would jump half the range, so 1 per notch.
                if (p12) Pair(entries, thing, consumed, b1, b2,
                    "Thrust", () => Mathf.RoundToInt(jp.OutputSetting * 100f).ToString(), 1);
                return;
            }
            if (thing is DynamicGasCanister dgc)
            {
                // NOTE: reversed pair on the portable tank — Button2 raises, Button1 lowers,
                // and each press already steps ±10 kPa natively.
                if (p12) Pair(entries, thing, consumed, b2, b1,
                    "Valve", () => Mathf.RoundToInt(dgc.OutputSetting) + " kPa", 1);
                return;
            }
            // TWO suit families with identical button semantics: SuitBase, and the real
            // worn suits which are Suit : AtmosphericItem (NOT a SuitBase subclass —
            // matching on SuitBase alone left pressure/temp as loose up/down wedges).
            if (thing is SuitBase sb)
            {
                if (p12) Pair(entries, thing, consumed, b1, b2,
                    "Pressure", () => Mathf.RoundToInt(sb.OutputSetting) + " kPa", 10);
                if (p34) Pair(entries, thing, consumed, b3, b4,
                    "Temperature", () => (sb.OutputTemperature.ToFloat() - 273.15f).ToString("0.0") + "°C", 10);
                return;
            }
            if (thing is Suit suit)
            {
                if (p12) Pair(entries, thing, consumed, b1, b2,
                    "Pressure", () => Mathf.RoundToInt(suit.OutputSetting) + " kPa", 10);
                if (p34) Pair(entries, thing, consumed, b3, b4,
                    "Temperature", () => (suit.OutputTemperature.ToFloat() - 273.15f).ToString("0.0") + "°C", 10);
                return;
            }

            // Anything else with real pairs: generic scroll wedges, labelled from the
            // up-button's own name, no value line (we don't know its field).
            if (p12) Pair(entries, thing, consumed, b1, b2, PairLabel(b1, "Setting"), null, 1);
            if (p34) Pair(entries, thing, consumed, b3, b4, PairLabel(b3, "Setting 2"), null, 1);
        }

        private static string PairLabel(Interactable up, string fallback)
        {
            try
            {
                var name = up.DisplayName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return fallback;
        }

        private static void Pair(List<RadialEntry> entries, DynamicThing thing,
            HashSet<Interactable> consumed, Interactable up, Interactable down,
            string label, Func<string> value, int coarseSteps)
        {
            consumed.Add(up);
            consumed.Add(down);
            var t = thing;
            entries.Add(new RadialEntry
            {
                Label = label,
                ActionText = "Scroll to adjust",
                Sublabel = coarseSteps > 1
                    ? "wheel = ±" + coarseSteps + ", hold " + FineKeyName() + " = ±1"
                    : "wheel up / down, click = up",
                ValueText = value,
                OnScroll = d => ItemActions.PressInteractable(t, d > 0 ? up : down, StepsPerNotch(coarseSteps)),
                // A bare click nudges up one step, so the wedge is never a dead end.
                OnSelect = () => ItemActions.PressInteractable(t, up),
                HotkeyThing = t,          // #4: a bound key nudges this setting up one step
                HotkeyInteractable = up,
            });
        }

        private static int StepsPerNotch(int coarse)
        {
            if (coarse <= 1) return 1;
            var fineKey = UIAConfig.RadialFineAdjustKey != null
                ? UIAConfig.RadialFineAdjustKey.Value : KeyCode.C;
            bool fine = fineKey != KeyCode.None && Input.GetKey(fineKey);
            return fine ? 1 : coarse;
        }

        private static string FineKeyName()
        {
            return UIAConfig.RadialFineAdjustKey != null
                ? UIAConfig.RadialFineAdjustKey.Value.ToString() : "C";
        }
    }
}
