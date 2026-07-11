using System;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using CharacterCustomisation;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>The diegetic tier the HUD renders at (feasibility report §3).</summary>
    public enum HudTier
    {
        /// <summary>No suit, or the suit has no power: felt-sense WORDS, no numbers.</summary>
        Bare,
        /// <summary>Powered suit worn: the full instrument readout.</summary>
        Suited,
        /// <summary>Playing the robot: always the full readout — you ARE the computer.</summary>
        Robot,
    }

    /// <summary>One frame of everything the HUD draws. Filled by <see cref="HudSampler"/>;
    /// panels only ever read this, never the game directly.</summary>
    public sealed class HudSnapshot
    {
        public bool Valid;
        /// <summary>The local human — for slot/thumbnail reads only; every VALUE the HUD
        /// shows must come from the sampled fields below.</summary>
        public Human Human;
        public bool IsRobot;
        public HudTier Tier;
        public bool SuitPowered;        // the game's own Thing.Powered bit
        public bool LowPower;           // battery % under the config threshold

        // Top bar (external / suit-sensor readings)
        public bool HasAtmosphere;
        public float PressureKPa;
        public float TempC;
        public float O2Fraction;        // 0..1 of total pressure
        public int SuitBatteryPct = -1; // -1 = no battery
        public float WaterRatio;        // hydration / nominal store
        public string SuitStatus = "";  // NOMINAL / CHECK / WARNING / CRITICAL
        public int SuitStatusLevel;     // 0..3 for colouring

        // Compass
        public float HeadingDeg;        // vanilla-parity (PlayerStateWindow formula)

        // Clock
        public uint Day;
        public string TimeText = "";
        public string DateText = "";
        public string DayPartWord = ""; // BARE's vague clock: MORNING, DUSK...

        // Vitals card
        public float HealthRatio = 1f;
        public float O2Quality = 1f;
        public float FoodRatio = 1f;
        public float FeltTempC;
        /// <summary>False when no valid atmosphere reading exists this frame (network
        /// atmospheres expire after 5 s) — temperature shows "--" and no felt-temp word.</summary>
        public bool FeltValid;

        // BARE felt senses (empty string = nominal, nothing to feel)
        public string WordTemp = "";
        public string WordAir = "";
        public string WordHunger = "";
        public string WordThirst = "";
        public string WordHealth = "";
        public string WordPressure = "";

        // Hands + equipment come straight from slots at draw time (thumbnails), but the
        // active hand is sampled here so every panel agrees within the frame.
        public bool RightHandActive;
    }

    /// <summary>
    /// All game reads for the HUD, in one place, every one client-safe (multiplayer
    /// client = the baseline). Decompile citations in comments; thresholds are read off
    /// the live Human instance because prefab values override code defaults.
    /// </summary>
    public static class HudSampler
    {
        private static readonly HudSnapshot _snap = new HudSnapshot();

        /// <summary>Drop object references when the HUD stands down — the static
        /// snapshot must never pin a dead world's Human across unloads/hot reloads.</summary>
        public static void Clear()
        {
            _snap.Valid = false;
            _snap.Human = null;
        }

        public static HudSnapshot Sample()
        {
            var s = _snap;
            s.Valid = false;
            var human = Guards.LocalHuman;
            s.Human = human;
            if (human == null) return s;
            s.Valid = true;

            // ---- tier (HudState machine input) ----
            bool isRobot = false;
            try { isRobot = human.SpeciesClass == SpeciesClass.Robot; } catch { }
            s.IsRobot = isRobot;

            BatteryCell battery = null;
            bool suitPowered = false;
            try
            {
                if (isRobot)
                {
                    battery = human.RobotBattery;              // uniform slot became Battery
                    suitPowered = battery != null && !battery.IsEmpty;
                }
                else
                {
                    var suit = human.Suit;
                    if (suit != null && suit.AsThing != null)
                    {
                        battery = suit.Battery;
                        // Thing.Powered IS the game's synced suit-power definition
                        // (Suit.CheckPowerState / SuitBase.CheckPowerState drive it from
                        // "battery present && !IsEmpty" server-side).
                        suitPowered = suit.AsThing.Powered;
                    }
                }
            }
            catch { }
            s.SuitPowered = suitPowered;
            s.Tier = !UsesTiers() ? (isRobot ? HudTier.Robot : HudTier.Suited)
                : isRobot ? HudTier.Robot
                : suitPowered ? HudTier.Suited
                : HudTier.Bare;

            int pct = -1;
            try { if (battery != null) pct = battery.CurrentPowerPercentage; } catch { }
            s.SuitBatteryPct = pct;
            // A robot with NO battery is running on brain fumes (vanilla stuns it) —
            // that is maximum glitch, not a rock-steady display.
            float lowThreshold = HudConfig.LowPowerThreshold != null ? HudConfig.LowPowerThreshold.Value : 10f;
            s.LowPower = s.Tier != HudTier.Bare && (pct < 0 ? isRobot : pct <= lowThreshold);

            // ---- external atmosphere (the suit's environment sensors) ----
            // Reset FIRST: the snapshot object is reused across frames, and a stale
            // reading must never survive an invalid atmosphere (network atmospheres
            // expire — a real vacuum must read as 0 kPa, not as last week's air).
            s.HasAtmosphere = false;
            s.PressureKPa = 0f;
            s.TempC = 0f;
            s.O2Fraction = 0f;
            try
            {
                var atmos = human.WorldAtmosphere;
                if (atmos != null && atmos.IsValid())
                {
                    float kPa = atmos.PressureGassesAndLiquids.ToFloat();
                    s.PressureKPa = kPa;
                    s.TempC = atmos.Temperature.ToFloat() - 273.15f;
                    float o2 = 0f;
                    try { o2 = atmos.PartialPressureO2.ToFloat(); } catch { }
                    s.O2Fraction = kPa > 0.01f ? Mathf.Clamp01(o2 / kPa) : 0f;
                    s.HasAtmosphere = true;
                }
            }
            catch { }

            // ---- suit status word: worst-of everything the suit knows ----
            s.SuitStatusLevel = 0;
            try
            {
                var suit = human.Suit;
                if (isRobot)
                {
                    // The robot's "suit" is itself: battery + damage.
                    if (battery == null || battery.IsCritical) Worst(s, 3);
                    else if (battery.IsLow) Worst(s, 2);
                }
                else if (suit != null && suit.AsThing != null)
                {
                    // Battery bands = vanilla StatusUpdates predicates (Mode<=3 caution,
                    // null/Mode<=1 critical).
                    if (battery == null || battery.IsCritical) Worst(s, 3);
                    else if (battery.IsLow) Worst(s, 1);

                    var air = suit.AirTank;
                    if (air == null) Worst(s, 3);
                    else
                    {
                        float airKPa = 0f;
                        try { airKPa = air.Pressure.ToFloat(); } catch { }
                        if (airKPa < 500f) Worst(s, 2);
                        else if (airKPa < 2000f) Worst(s, 1);
                    }

                    if (suit.HasFilters)
                    {
                        if (suit.EmptyFilter) Worst(s, 3);
                        else if (suit.LowFilter) Worst(s, 1);
                    }

                    if (suit.HasWasteTankSlot)
                    {
                        var waste = suit.WasteTank;
                        float max = suit.WasteMaxPressure.ToFloat();
                        if (waste != null && max > 1f)
                        {
                            float ratio = waste.Pressure.ToFloat() / max;
                            if (ratio > 0.9f) Worst(s, 2);
                            else if (ratio > 0.75f) Worst(s, 1);
                        }
                    }
                }
            }
            catch { }
            s.SuitStatus = s.SuitStatusLevel >= 3 ? "CRITICAL"
                : s.SuitStatusLevel == 2 ? "WARNING"
                : s.SuitStatusLevel == 1 ? "CHECK"
                : "NOMINAL";

            // ---- compass: vanilla parity — PlayerStateWindow shows
            // (EntityRotation.yaw + 180) % 360; we use the CAMERA so seats/free-look read
            // where you actually look. ----
            try
            {
                var cam = CameraController.CurrentCamera;
                if (cam != null)
                    s.HeadingDeg = (cam.transform.eulerAngles.y + 180f) % 360f;
            }
            catch { }

            // ---- clock. TimeOfDay is [0,1) with 0 ≈ sunrise (per-world SunriseOffset),
            // so the ship clock maps sunrise to 06:00. The date is fictional flavour:
            // mission epoch 2080-01-01 + days survived. ----
            try
            {
                s.Day = WorldManager.DaysPast + 1;
                float t = (OrbitalSimulation.TimeOfDay + 0.25f) % 1f; // 0 sunrise -> 06:00
                int minutes = Mathf.FloorToInt(t * 24f * 60f) % (24 * 60);
                s.TimeText = $"{minutes / 60:00}:{minutes % 60:00}";
                var date = new DateTime(2080, 1, 1).AddDays(WorldManager.DaysPast);
                s.DateText = date.ToString("yyyy-MM-dd");
                float tod = OrbitalSimulation.TimeOfDay;
                s.DayPartWord = tod < 0.06f ? "DAWN"
                    : tod < 0.2f ? "MORNING"
                    : tod < 0.3f ? "MIDDAY"
                    : tod < 0.44f ? "AFTERNOON"
                    : tod < 0.56f ? "DUSK"
                    : tod < 0.94f ? "NIGHT"
                    : "PREDAWN";
            }
            catch { s.TimeText = ""; s.DateText = ""; }

            // ---- vitals (all replicated: OxygenQuality/Nutrition/Hydration ship in the
            // entity update; DamageState ratios are client-visible; Oxygenation is NOT
            // replicated — never read it here). ----
            float hydration = 5f, damage = 0f;
            try { damage = human.DamageState.TotalRatio; } catch { }
            try { hydration = human.Hydration; } catch { }
            try { s.FoodRatio = Mathf.Clamp01(human.NutritionRatio); } catch { }
            try { s.O2Quality = isRobot ? 1f : Mathf.Clamp01(human.OxygenQuality); } catch { }
            s.HealthRatio = Mathf.Clamp01(1f - damage);
            s.WaterRatio = Mathf.Clamp01(hydration / 5f);

            // Felt temperature: what you BREATHE (helmet internals when closed, else world).
            // No valid atmosphere anywhere = vacuum for pressure purposes (0 kPa, so the
            // VACUUM/CHOKING words fire in a true void) but UNKNOWN for temperature (no
            // felt-temp word — you can't feel the warmth of nothing).
            bool feltValid = s.HasAtmosphere;
            float feltC = s.TempC;
            float breathKPa = s.HasAtmosphere ? s.PressureKPa : 0f;
            float toxins = 0f;
            float o2Quality = s.O2Quality;
            try
            {
                var breathing = human.BreathingAtmosphere;
                if (breathing != null && breathing.IsValid())
                {
                    feltValid = true;
                    feltC = breathing.Temperature.ToFloat() - 273.15f;
                    breathKPa = breathing.PressureGassesAndLiquids.ToFloat();
                    try { toxins = breathing.PartialPressureHumanToxins.ToFloat(); } catch { }
                }
            }
            catch { }
            s.FeltTempC = feltC;
            s.FeltValid = feltValid;

            // ---- BARE felt-sense words (empty = nominal). Bands anchored to the REAL
            // damage/warning thresholds so the words agree with what hurts you:
            // lungs damage <-10°C / >+50°C, vanilla caution <0°C / >80°C critical;
            // toxins warn at 0.5 kPa; Armstrong limit 6.3 kPa; safe floor 20 kPa;
            // nutrition/hydration warn thresholds read off the live Human. ----
            if (isRobot)
            {
                s.WordTemp = s.WordAir = s.WordHunger = s.WordThirst = s.WordPressure = "";
                s.WordHealth = damage > 0.75f ? "FAILING" : damage > 0.25f ? "DAMAGED" : "";
            }
            else
            {
                s.WordTemp = !feltValid ? ""
                    : feltC <= -10f ? "FREEZING"
                    : feltC <= 0f ? "COLD"
                    : feltC <= 10f ? "CHILLY"
                    : feltC >= 80f ? "BURNING"
                    : feltC >= 50f ? "HOT"
                    : feltC >= 32f ? "WARM"
                    : "";

                s.WordAir = breathKPa < 6.3f || o2Quality < 0.5f ? "CHOKING"
                    : toxins > 0.5f ? "TOXIC AIR"
                    : o2Quality < 0.9f ? "THIN AIR"
                    : "";

                float warnFood = 15f, critFood = 5f, warnWater = 2f, critWater = 1f;
                try { warnFood = human.WarningNutrition; critFood = human.CriticalNutrition; } catch { }
                try { warnWater = human.WarningHydration; critWater = human.CriticalHydration; } catch { }
                float rawFood = 50f;
                try { rawFood = human.Nutrition; } catch { }
                s.WordHunger = rawFood <= critFood ? "STARVING" : rawFood <= warnFood ? "HUNGRY" : "";
                s.WordThirst = hydration <= critWater ? "PARCHED" : hydration <= warnWater ? "THIRSTY" : "";

                s.WordHealth = damage > 0.75f ? "DYING" : damage > 0.25f ? "HURT"
                    : damage > 0.05f ? "BRUISED" : "";

                s.WordPressure = breathKPa < 6.3f ? "VACUUM"
                    : breathKPa < 20f ? "LOW PRESSURE"
                    : breathKPa > 607.95f ? "CRUSHING"
                    : "";
            }

            try { s.RightHandActive = InventoryManager.ActiveHandSlot == human.RightHandSlot; }
            catch { }

            return s;
        }

        private static bool UsesTiers()
            => HudConfig.DiegeticTiers == null || HudConfig.DiegeticTiers.Value;

        private static void Worst(HudSnapshot s, int level)
        {
            if (level > s.SuitStatusLevel) s.SuitStatusLevel = level;
        }
    }
}
