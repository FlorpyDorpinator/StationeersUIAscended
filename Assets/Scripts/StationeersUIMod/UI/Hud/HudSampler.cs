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
        public string WordCognition = "";   // consciousness (O2 quality)
        public string WordToilet = "";      // bowel (server-sim only; silent on a pure client)

        // Hands + equipment come straight from slots at draw time (thumbnails), but the
        // active hand is sampled here so every panel agrees within the frame.
        public bool RightHandActive;

        // Suit setpoints (the dials the wearer set, not the felt reading). Only meaningful
        // on a powered, readable suit — a dead or absent suit reports the sentinels.
        public float SuitTargetPressureKPa = -1f;  // kPa; -1 = no powered readable suit
        public float SuitTargetTempC = float.NaN;  // NaN = absent (no readable setpoint)

        // Internal (sealed-suit) instrument readout — gated exactly like vanilla InfoInternal
        // (HasInternals && InternalsOn && a valid breathing atmosphere this frame).
        public float InternalPressureKPa;
        public float InternalTempC;
        public bool InternalValid;

        // Jetpack (BackpackSlot occupant). Gas rigs report a propellant DELTA; electric rigs
        // report a battery %. The two are mutually exclusive by JetpackIsGas.
        public bool JetpackPresent;
        public bool JetpackIsGas;
        public float JetpackThrustPct;              // OutputSetting * 100
        public float JetpackPropellantDeltaKPa = -1f; // canister interior minus world gas; -1 = no canister
        public bool JetpackLow;                     // gas delta under 500 kPa
        public bool JetpackCrit;                    // gas delta under 100 kPa
        public float JetpackBatteryPct = -1f;       // electric only; -1 = n/a

        // Needs. Sanitation (bowel) is simulated server-side only — SanitationValid says
        // whether this client's SanitationRatio can be trusted at all.
        public float Hydration01;
        public float Hygiene01;
        public float Sanitation01;
        public bool SanitationValid;

        // Suit / helmet chips (client-synced Thing bits).
        public bool HelmetPresent;
        public bool HelmetClosed;   // visor down (!GasMask.IsOpen)
        public bool HelmetLightOn;  // helmet lamp (GasMask.OnOff)
        public bool SuitAcOn;       // suit conditioner toggle (Suit.OnOff)
        public bool InternalsOn;
        public bool HasInternals;

        // Body doll: worst-of whole-body-and-organ damage per region, 0 pristine .. 1 gone.
        public float DamageHead01;
        public float DamageChest01;
        public float DamageBody01;

        // World name — constant for a whole world, cached so the HUD never churns strings.
        public string WorldName = "";

        // Player movement speed (m/s) — Human.VelocityMagnitude, vanilla's velocity readout.
        public float SpeedMs;

        // Jetpack actually flying — vanilla's IsJetpackOn(): ControlMode is Jetpack or
        // JetpackGravity (StatusUpdates.cs:296). Distinct from JetpackPresent (worn).
        public bool JetpackOn;
    }

    /// <summary>
    /// All game reads for the HUD, in one place, every one client-safe (multiplayer
    /// client = the baseline). Decompile citations in comments; thresholds are read off
    /// the live Human instance because prefab values override code defaults.
    /// </summary>
    public static class HudSampler
    {
        private static readonly HudSnapshot _snap = new HudSnapshot();

        // World name is invariant for a whole world; we read it lazily and reuse the cached
        // string to avoid a per-frame managed read/alloc. Reset on Clear() so the next world
        // re-reads immediately instead of showing the old one for up to the throttle window.
        private static string _cachedWorldName = "";
        private static float _worldNameNextCheck;

        /// <summary>Drop object references when the HUD stands down — the static
        /// snapshot must never pin a dead world's Human across unloads/hot reloads.</summary>
        public static void Clear()
        {
            _snap.Valid = false;
            _snap.Human = null;
            _cachedWorldName = "";
            _worldNameNextCheck = 0f;
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
            // Speed (m/s): the local Human's own cached rigidbody magnitude — a pure read,
            // MP-safe (it is our own controlled entity). PlayerStateWindow.cs:215-218.
            s.SpeedMs = 0f;
            try { s.SpeedMs = human.VelocityMagnitude; } catch { }

            // Jetpack flying — vanilla IsJetpackOn() (StatusUpdates.cs:296-299).
            s.JetpackOn = false;
            try
            {
                var mc = human.MovementController;
                s.JetpackOn = mc != null
                    && (mc.ControlMode == Assets.Scripts.MovementController.Mode.Jetpack
                     || mc.ControlMode == Assets.Scripts.MovementController.Mode.JetpackGravity);
            }
            catch { }

            // ---- suit / helmet chips + the suit's own setpoints. Reset first (reused
            // snapshot). PlayerStateWindow reads OutputSetting/OutputTemperature only when
            // Suit.AsThing exists; we additionally require power so a dead suit reports the
            // "no readable setpoint" sentinels instead of a frozen last-known dial. ----
            s.HelmetPresent = false;
            s.HelmetClosed = false;
            s.HelmetLightOn = false;
            s.SuitAcOn = false;
            s.InternalsOn = false;
            s.HasInternals = false;
            s.SuitTargetPressureKPa = -1f;
            s.SuitTargetTempC = float.NaN;
            try { s.HasInternals = human.HasInternals; } catch { }
            try { s.InternalsOn = human.InternalsOn; } catch { }
            try
            {
                var helmet = human.HeadAsSpaceHelmet;   // GasMask worn in the head slot
                if (helmet != null)
                {
                    s.HelmetPresent = true;
                    s.HelmetClosed = !helmet.IsOpen;     // Thing.IsOpen = visor open
                    s.HelmetLightOn = helmet.OnOff;      // Thing.OnOff = helmet lamp
                }
            }
            catch { }
            try
            {
                var suit = human.Suit;
                if (suit != null && suit.AsThing != null)
                {
                    s.SuitAcOn = suit.AsThing.OnOff;     // conditioner toggle
                    if (suit.AsThing.Powered)
                    {
                        // ISuit.OutputSetting is the pressure dial in kPa; OutputTemperature
                        // is a TemperatureKelvin (Suit.cs:330/349, SuitBase.cs:761/780).
                        s.SuitTargetPressureKPa = suit.OutputSetting;
                        s.SuitTargetTempC = suit.OutputTemperature.ToFloat() - 273.15f;
                    }
                }
            }
            catch { }

            // Reset internal readout before the shared breathing read below fills it.
            s.InternalPressureKPa = 0f;
            s.InternalTempC = 0f;
            s.InternalValid = false;

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
                    // Sealed-suit instrument readout: vanilla lights InfoInternal only when
                    // HasInternals && InternalsOn (PlayerStateWindow.Update). Reuse the
                    // breathing atmosphere already read here — never sample it twice.
                    if (s.HasInternals && s.InternalsOn)
                    {
                        s.InternalPressureKPa = breathKPa;
                        s.InternalTempC = feltC;
                        s.InternalValid = true;
                    }
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

                // Parity with the vitals panel's words-mode bands (warn ≤66%, crit ≤33%) so a
                // need that reads HUNGRY/THIRSTY on the numeric card also surfaces as a felt word
                // here. The game's raw WarningHydration (2 of 5 = 40%) fired much later, so the
                // two panels disagreed — you'd read THIRSTY yet feel nothing.
                s.WordHunger = s.FoodRatio <= 0.33f ? "STARVING" : s.FoodRatio <= 0.66f ? "HUNGRY" : "";
                s.WordThirst = s.WaterRatio <= 0.33f ? "PARCHED" : s.WaterRatio <= 0.66f ? "THIRSTY" : "";

                // Health reads as general WELLNESS (0–100), not injury — you can't "bruise" in
                // Stationeers. Only the degraded end shows (nominal = felt nothing).
                s.WordHealth = damage > 0.75f ? "DYING" : damage > 0.25f ? "AILING"
                    : damage > 0.05f ? "UNWELL" : "";

                s.WordPressure = breathKPa < 6.3f ? "VACUUM"
                    : breathKPa < 20f ? "LOW PRESSURE"
                    : breathKPa > 607.95f ? "CRUSHING"
                    : "";
            }

            // ---- jetpack (BackpackSlot occupant). Gas rigs report a propellant DELTA —
            // canister interior minus the world's GAS pressure (Jetpack.PropellantDelta uses
            // PressureGasses, not …AndLiquids); electric rigs report a battery %. A gas rig
            // with no canister is empty by definition, so it flags low AND critical. ----
            s.JetpackPresent = false;
            s.JetpackIsGas = false;
            s.JetpackThrustPct = 0f;
            s.JetpackPropellantDeltaKPa = -1f;
            s.JetpackLow = false;
            s.JetpackCrit = false;
            s.JetpackBatteryPct = -1f;
            try
            {
                var jetpack = human.BackpackSlot != null ? human.BackpackSlot.Get<Jetpack>() : null;
                if (jetpack != null)
                {
                    s.JetpackPresent = true;
                    s.JetpackIsGas = jetpack.IsGasPowered;
                    s.JetpackThrustPct = jetpack.OutputSetting * 100f;
                    if (jetpack.IsGasPowered)
                    {
                        GasCanister canister;
                        if (jetpack.PropellentSlot != null
                            && jetpack.PropellentSlot.Contains<GasCanister>(out canister)
                            && canister.InternalAtmosphere != null)
                        {
                            float world = 0f;
                            var wa = human.WorldAtmosphere;
                            if (wa != null && wa.IsValid()) world = wa.PressureGasses.ToFloat();
                            float delta = canister.InternalAtmosphere.PressureGassesAndLiquids.ToFloat() - world;
                            s.JetpackPropellantDeltaKPa = delta;
                            s.JetpackLow = delta < 500f;
                            s.JetpackCrit = delta < 100f;
                        }
                        else
                        {
                            s.JetpackLow = true;
                            s.JetpackCrit = true;
                        }
                    }
                    else
                    {
                        var electric = jetpack as JetpackElectric;
                        if (electric != null && electric.Battery != null)
                            s.JetpackBatteryPct = electric.Battery.CurrentPowerPercentage;
                    }
                }
            }
            catch { }

            // ---- needs. Hydration/Hygiene are replicated ratios. Sanitation (bowel) is only
            // simulated where the stomach tick runs (GameManager.RunSimulation) — a pure
            // client has no trustworthy value, so it ships a validity flag, not a fake 0. ----
            s.Hydration01 = 0f;
            s.Hygiene01 = 0f;
            s.Sanitation01 = 0f;
            s.SanitationValid = false;
            try { s.Hydration01 = Mathf.Clamp01(human.HydrationRatio); } catch { }
            try { s.Hygiene01 = Mathf.Clamp01(human.HygieneRatio); } catch { }
            try { s.Sanitation01 = Mathf.Clamp01(human.SanitationRatio); } catch { }
            try { s.SanitationValid = GameManager.RunSimulation; } catch { }

            // Consciousness + bowel felt words (bare senses can place these too). Empty when
            // nominal. Consciousness rides O2 quality (low breathable O2 → you black out);
            // the bowel word needs the server sim (SanitationValid) or it stays silent so a
            // pure MP client never guesses a server-only value.
            if (isRobot)
            {
                s.WordCognition = "";
                s.WordToilet = "";
            }
            else
            {
                s.WordCognition = s.O2Quality < 0.5f ? "FADING" : s.O2Quality < 0.9f ? "DAZED" : "";
                s.WordToilet = !s.SanitationValid ? ""
                    : s.Sanitation01 > 0.75f ? "DESPERATE"
                    : s.Sanitation01 > 0.45f ? "NEED TO GO"
                    : "";
            }

            // ---- body doll (vanilla StatusUpdates.HandleDamageIndicators): each region is
            // the worst of the whole-body TotalRatio and its signature organ; robot chest is
            // the power cell, not lungs. Any read failure = 0 (pristine), never a false alarm. ----
            s.DamageHead01 = 0f;
            s.DamageChest01 = 0f;
            s.DamageBody01 = 0f;
            try
            {
                float body = human.DamageState.TotalRatio;
                float head = body;
                try { if (human.OrganBrain != null) head = Mathf.Max(head, human.OrganBrain.DamageState.TotalRatio); } catch { }
                float chest = body;
                if (isRobot)
                {
                    try { if (human.RobotBattery != null) chest = Mathf.Max(chest, human.RobotBattery.DamageState.TotalRatio); } catch { }
                }
                else
                {
                    try { if (human.OrganLungs != null) chest = Mathf.Max(chest, human.OrganLungs.DamageState.TotalRatio); } catch { }
                }
                s.DamageBody01 = Mathf.Clamp01(body);
                s.DamageHead01 = Mathf.Clamp01(head);
                s.DamageChest01 = Mathf.Clamp01(chest);
            }
            catch { }

            // ---- world name. Invariant per world; refresh only when nothing is cached or on
            // a lazy ~5 s timer (also catches a world reload) to avoid per-frame string churn. ----
            try
            {
                if (string.IsNullOrEmpty(_cachedWorldName) || Time.unscaledTime >= _worldNameNextCheck)
                {
                    _cachedWorldName = WorldManager.CurrentWorldName ?? "";
                    _worldNameNextCheck = Time.unscaledTime + 5f;
                }
            }
            catch { }
            s.WorldName = _cachedWorldName;

            try { s.RightHandActive = InventoryManager.ActiveHandSlot == human.RightHandSlot; }
            catch { }

            if (DebugShowAll) ForceEverythingVisible(s);

            return s;
        }

        /// <summary>F9 debug: pretend every readable value is present and interesting so
        /// EVERY snapshot-driven widget shows its content at once — the worst-case full
        /// layout, for arranging the HUD so nothing overlaps when full. Real reads above are
        /// overwritten; this touches no game state.</summary>
        public static bool DebugShowAll;

        private static void ForceEverythingVisible(HudSnapshot s)
        {
            // Needs: all rows present (toilet needs valid + >0.25; health needs <1).
            s.FoodRatio = 0.62f; s.WaterRatio = 0.48f;
            s.Sanitation01 = 0.55f; s.SanitationValid = true;
            s.HealthRatio = 0.7f; s.O2Quality = 0.9f;
            s.DamageHead01 = 0.4f; s.DamageChest01 = 0.55f; s.DamageBody01 = 0.3f;
            s.Hydration01 = 0.48f; s.Hygiene01 = 0.6f;

            // Atmosphere + internal instruments valid with lively numbers.
            s.HasAtmosphere = true; s.PressureKPa = 101.3f; s.TempC = 21.5f; s.O2Fraction = 0.21f;
            s.FeltValid = true; s.FeltTempC = 21.5f;
            s.InternalValid = true; s.InternalPressureKPa = 99.2f; s.InternalTempC = 20.4f;
            s.SuitTargetPressureKPa = 101f; s.SuitTargetTempC = 20f;
            s.SpeedMs = 3.4f;

            // Jetpack + suit chips lit.
            s.JetpackPresent = true; s.JetpackIsGas = true; s.JetpackThrustPct = 120f;
            s.JetpackPropellantDeltaKPa = 5712f; s.JetpackLow = false; s.JetpackCrit = false;
            s.JetpackOn = true;
            s.HelmetPresent = true; s.HelmetClosed = true; s.HelmetLightOn = true;
            s.SuitAcOn = true; s.HasInternals = true; s.InternalsOn = true;

            // Bare-tier felt words, so the power-off preview has something in every row.
            s.WordTemp = "WARM"; s.WordAir = "THIN"; s.WordHunger = "PECKISH";
            s.WordThirst = "THIRSTY"; s.WordHealth = "AILING"; s.WordPressure = "LOW";
            s.WordCognition = "DAZED"; s.WordToilet = "NEED TO GO";
        }

        private static bool UsesTiers()
            => HudConfig.DiegeticTiers == null || HudConfig.DiegeticTiers.Value;

        private static void Worst(HudSnapshot s, int level)
        {
            if (level > s.SuitStatusLevel) s.SuitStatusLevel = level;
        }
    }
}
