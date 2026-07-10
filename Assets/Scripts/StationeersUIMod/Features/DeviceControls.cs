using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Clothing;
using Assets.Scripts.Objects.Items;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using UnityEngine;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// Option A scroll-adjustable device controls. Verified against the 27701 decompile:
    /// stepped settings are ButtonN interactable PAIRS — SuitBase Button1/Button2 =
    /// pressure setting +/-1 kPa (SuitBase.cs:995-1030), Button3/Button4 = AC temperature
    /// +/-1 K (SuitBase.cs:1031-1066), Jetpack Button1/Button2 = thrust +/-0.1
    /// (Jetpack.cs:757-812). One scroll notch = one Interactable.PlayerInteractWith()
    /// = exactly one vanilla button click = ONE InteractionMessage; all clamping happens
    /// server-side inside the item's own InteractWith under GameManager.RunSimulation.
    /// Values shown are plain replicated fields (OutputSetting / OutputTemperature), so
    /// they read correctly on multiplayer clients.
    /// </summary>
    public static class DeviceControls
    {
        public static void AddValueControls(List<RadialEntry> entries, DynamicThing thing)
        {
            if (thing == null) return;

            var up = thing.InteractButton1;
            var down = thing.InteractButton2;
            if (ItemMenuBuilder.IsRealControl(up) && ItemMenuBuilder.IsRealControl(down))
            {
                string label = "Setting";
                Func<string> value = null;
                if (thing is SuitBase sb)
                {
                    label = "Pressure";
                    value = () => Mathf.RoundToInt(sb.OutputSetting) + " kPa";
                }
                else if (thing is Jetpack jp)
                {
                    label = "Thrust";
                    value = () => jp.OutputSetting.ToString("0.0");
                }
                entries.Add(ScrollEntry(thing, label, value, up, down));
            }

            if (thing is SuitBase suit)
            {
                var tUp = thing.InteractButton3;
                var tDown = thing.InteractButton4;
                if (ItemMenuBuilder.IsRealControl(tUp) && ItemMenuBuilder.IsRealControl(tDown))
                {
                    entries.Add(ScrollEntry(thing, "Temperature",
                        () => (suit.OutputTemperature.ToFloat() - 273.15f).ToString("0.0") + "°C",
                        tUp, tDown));
                }

                // Filtration / air release are binary toggles on the same funnel.
                // (SuitBase maps LogicType.Filtration -> Exporting, AirRelease -> Importing.)
                AddToggle(entries, thing, thing.InteractExport, "Filtration");
                AddToggle(entries, thing, thing.InteractImport, "Air Release");
            }
        }

        private static RadialEntry ScrollEntry(DynamicThing owner, string label, Func<string> value,
            Interactable up, Interactable down)
        {
            return new RadialEntry
            {
                Label = label,
                ActionText = "Scroll to adjust",
                Sublabel = "wheel up / down, click = up",
                ValueText = value,
                OnScroll = d => ItemActions.PressInteractable(owner, d > 0 ? up : down),
                // A bare click nudges up, so the wedge is never a dead end.
                OnSelect = () => ItemActions.PressInteractable(owner, up),
            };
        }

        private static void AddToggle(List<RadialEntry> entries, DynamicThing owner,
            Interactable interactable, string label)
        {
            if (!ItemMenuBuilder.IsRealControl(interactable)) return;
            bool on = false;
            try { on = interactable.State == 1; } catch { }
            entries.Add(new RadialEntry
            {
                Label = label,
                StateText = on ? "ON" : "OFF",
                ActionText = on ? "Turn off " + label : "Turn on " + label,
                OnSelect = () => ItemActions.PressInteractable(owner, interactable),
            });
        }
    }
}
