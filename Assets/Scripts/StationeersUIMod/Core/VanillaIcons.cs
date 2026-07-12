using System;
using System.Collections.Generic;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Runtime lookup of the GAME'S own flat UI icons — the art players already know —
    /// by a small set of stable keys ("Hunger", "Temp", "Pressure"...). Sources are the
    /// typed sprite references vanilla itself displays: the StatusUpdates moodlet icons
    /// and PlayerStateWindow's ImageToggles (27701: StatusUpdates.cs:1904-2122,
    /// PlayerStateWindow.cs:560-595). Guarded and cached; misses are NOT cached because
    /// the singletons appear only in-world — callers retry until the sprite lands.
    /// </summary>
    public static class VanillaIcons
    {
        private static readonly Dictionary<string, Sprite> _cache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hunger", "food", "thirst", "water", "toilet", "power", "battery",
            "o2", "oxygen", "health", "temp", "temperature", "cold", "pressure",
            "helmet", "jetpack", "light", "toxins", "leak", "waste", "filter",
            "airtank", "air",
        };

        /// <summary>Whether this name addresses a game icon (regardless of whether it can
        /// be resolved right now) — decides icon routing at element build time.</summary>
        public static bool IsKey(string name) => !string.IsNullOrEmpty(name) && _keys.Contains(name);

        public static Sprite TryGet(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Sprite s;
            if (_cache.TryGetValue(key, out s) && s != null) return s;
            s = Resolve(key);
            if (s != null) _cache[key] = s;
            return s;
        }

        public static void Clear()
        {
            _cache.Clear();
            _rampBack = null;
            _rampFront = null;
        }

        // ---- the game's own pressure-ramp bar art (PlayerStateWindow.cs:42-52,469-481) ----

        private static Sprite _rampBack, _rampFront;

        /// <summary>The track sprite of vanilla's pressure ramp bar. Null until the
        /// PlayerStateWindow exists (world only) — callers retry.</summary>
        public static Sprite PressureRampBack()
            => _rampBack != null ? _rampBack : (_rampBack = RampSprite(false));

        /// <summary>The fill sprite of vanilla's pressure ramp bar.</summary>
        public static Sprite PressureRampFront()
            => _rampFront != null ? _rampFront : (_rampFront = RampSprite(true));

        /// <summary>Vanilla's EXACT kPa→fill mapping (PressureCurve, clamped to its last
        /// key like the external readout does). NaN when the window isn't up yet.</summary>
        public static float PressureFill(float kPa)
        {
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                var curve = psw != null ? psw.PressureCurve : null;
                if (curve == null || curve.length == 0) return float.NaN;
                float max = curve.keys[curve.length - 1].time;
                return Mathf.Clamp01(curve.Evaluate(Mathf.Clamp(kPa, 0f, max)));
            }
            catch { return float.NaN; }
        }

        private static Sprite RampSprite(bool front)
        {
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                if (psw == null) return null;
                var rt = front ? psw.InfoExternalPressureRampFront : psw.InfoExternalPressureRampBack;
                var img = rt != null ? rt.GetComponent<UnityEngine.UI.Image>() : null;
                return img != null ? img.sprite : null;
            }
            catch { return null; }
        }

        private static Sprite Resolve(string key)
        {
            try
            {
                var su = Assets.Scripts.UI.StatusUpdates.Instance;
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                switch (key.ToLowerInvariant())
                {
                    case "hunger": case "food": return Icon(su != null ? su.NutritionWarning : null);
                    case "thirst": case "water": return Icon(su != null ? su.HydrationWarning : null);
                    case "toilet": return Icon(su != null ? su.SanitationWarning : null);
                    case "power": case "battery": return Icon(su != null ? su.PowerStateWarning : null);
                    case "o2": case "oxygen": return Icon(su != null ? su.OxygenWarning : null);
                    case "health": return Icon(su != null ? su.HealthWarning : null);
                    case "toxins": return Icon(su != null ? su.ToxinsWarning : null);
                    case "leak": return Icon(su != null ? su.LeakWarning : null);
                    case "waste": return Icon(su != null ? su.WasteCritical : null);
                    case "filter": return Icon(su != null ? su.FilterWarning : null);
                    case "airtank": case "air": return Icon(su != null ? su.AirTankWarning : null);
                    case "temp": case "temperature": return Toggle(psw != null ? psw.InternalTemperatureToggle : null, 0);
                    case "cold": return Toggle(psw != null ? psw.InternalTemperatureToggle : null, 1);
                    case "pressure": return Toggle(psw != null ? psw.InternalPressureToggle : null, 0);
                    case "helmet": return Toggle(psw != null ? psw.HelmetImageToggle : null, 0);
                    case "jetpack": return Toggle(psw != null ? psw.JetPackImageToggle : null, 0);
                    case "light": return Toggle(psw != null ? psw.LightImageToggle : null, 0);
                }
            }
            catch { }
            return null;
        }

        private static Sprite Icon(Assets.Scripts.UI.StatusUpdate s)
        {
            try { return s != null ? s.Icon : null; } catch { return null; }
        }

        private static Sprite Toggle(Assets.Scripts.UI.ImageToggle t, int index)
        {
            try
            {
                return t != null && t.Sprites != null && t.Sprites.Length > index
                    ? t.Sprites[index] : null;
            }
            catch { return null; }
        }
    }
}
