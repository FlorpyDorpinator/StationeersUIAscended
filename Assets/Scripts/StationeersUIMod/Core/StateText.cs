using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The live state string under a wedge icon: "87%", "5300kPa", "x25".
    ///
    /// Built on vanilla's single readout funnel — DynamicThing.GetQuantityText(), the same
    /// virtual SlotDisplay.RefreshQuantity renders into slot corners. That one call covers
    /// battery % (from the client-synced CurrentPowerPercentage byte), canister kPa (from
    /// the client-synced internal atmosphere), stack counts, filter/spray %, and dirt
    /// canister fill — and returns null for plain items, so most wedges stay clean.
    /// Client-safe by construction: everything GetQuantityText reads is networked
    /// (BatteryCell.PowerRatio, which is NOT synced to clients, is never touched).
    /// </summary>
    public static class StateText
    {
        /// <summary>TMP-friendly (keeps vanilla's &lt;size&gt; tags — TMP renders them; do NOT
        /// uppercase or feed to ImGui without stripping). Null when the item has no state.</summary>
        public static string For(DynamicThing thing)
        {
            if (thing == null) return null;
            try
            {
                string s = thing.GetQuantityText();
                if (!string.IsNullOrEmpty(s)) return s;

                // Tools show their power/fuel source's state (vanilla's FireExtinguisher
                // pattern — its GetQuantityText delegates to the child canister).
                if (thing is PowerTool pt)
                {
                    var cell = pt.Battery;
                    return cell != null ? cell.GetQuantityText() : null;
                }
                if (thing is WeldingTorch torch)
                {
                    var tank = torch.FuelTank;
                    return tank != null ? tank.GetQuantityText() : null;
                }

                // Portable atmospherics (the DynamicGasCanister tank and kin) don't override
                // GetQuantityText, but their InternalAtmosphere IS networked to clients — the
                // world tooltip reads it (PortableAtmospherics.GetPassiveTooltip), so this is
                // client-safe. Show the same kPa a small GasCanister would.
                if (thing is Assets.Scripts.Objects.PortableAtmospherics && thing.InternalAtmosphere != null)
                {
                    int kpa = UnityEngine.Mathf.RoundToInt(
                        thing.InternalAtmosphere.PressureGassesAndLiquids.ToFloat());
                    return kpa + " kPa";
                }
            }
            catch { }
            return null;
        }

        /// <summary>Plain-text variant for ImGui and the hub readout (rich-text tags stripped).</summary>
        public static string Plain(DynamicThing thing)
        {
            return Strip(For(thing));
        }

        /// <summary>GetQuantityText contains TMP rich-text tags; ImGui renders them literally.</summary>
        public static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int lt;
            while ((lt = s.IndexOf('<')) >= 0)
            {
                int gt = s.IndexOf('>', lt);
                if (gt < 0) break;
                s = s.Remove(lt, gt - lt + 1);
            }
            return s;
        }
    }
}
