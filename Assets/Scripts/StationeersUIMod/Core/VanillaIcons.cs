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
            "airtank", "air", "speed", "velocity", "canister", "propellant",
            "cognition", "consciousness", "unconscious",
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
            _rampBackColor = new Color(1f, 1f, 1f, 0.251f);
            _rampFrontColor = Color.white;
            _canister = null;
            _toilet = null;
        }

        // ---- the game's own pressure-ramp bar art (PlayerStateWindow.cs:42-52,469-481) ----
        //
        // Scene truth (Base.unity): the BACK is "PressureGaugeBG" — sprite
        // "pressure-gaugebg" drawn by vanilla at WHITE ALPHA 0.251. The FRONT
        // RectTransform ("PressureGaugeLitMask") is a SPRITE-LESS Image + Mask
        // (ShowMaskGraphic off) that stretches with pressure and reveals its CHILD
        // "PressureGaugeLitImage" (sprite "pressure-gauge", white, full-size). So the
        // fill ART must be harvested from the mask's child, never the mask itself.

        private static Sprite _rampBack, _rampFront;
        private static Color _rampBackColor = new Color(1f, 1f, 1f, 0.251f);
        private static Color _rampFrontColor = Color.white;

        /// <summary>The track sprite of vanilla's pressure ramp bar. Null until the
        /// PlayerStateWindow exists (world only) — callers retry.</summary>
        public static Sprite PressureRampBack()
            => _rampBack != null ? _rampBack : (_rampBack = RampSprite(false));

        /// <summary>The lit fill art of vanilla's pressure ramp bar (the mask's child).</summary>
        public static Sprite PressureRampFront()
            => _rampFront != null ? _rampFront : (_rampFront = RampSprite(true));

        /// <summary>Vanilla's own tint for the track (white @ ~25% in the shipped scene).
        /// Valid once PressureRampBack() has resolved; the default matches the scene.</summary>
        public static Color PressureRampBackColor() => _rampBackColor;

        /// <summary>Vanilla's own tint for the lit fill art.</summary>
        public static Color PressureRampFrontColor() => _rampFrontColor;

        /// <summary>Vanilla's EXACT kPa→fill mapping (PressureCurve, clamped to its last
        /// key like the external readout does). NaN when the window isn't up yet.</summary>
        public static float PressureFill(float kPa)
        {
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                var curve = psw != null ? psw.PressureCurve : null;
                if (curve == null || curve.length == 0) return float.NaN;
                // curve[i] reads one Keyframe; curve.keys COPIES the whole array every call.
                float max = curve[curve.length - 1].time;
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
                if (rt == null) return null;

                if (!front)
                {
                    var img = rt.GetComponent<UnityEngine.UI.Image>();
                    if (img == null || img.sprite == null) return null;
                    _rampBackColor = img.color;
                    return img.sprite;
                }

                // The front object itself is the sprite-less mask — the art is on
                // whichever child actually carries a sprite (PressureGaugeLitImage).
                for (int i = 0; i < rt.childCount; i++)
                {
                    var img = rt.GetChild(i).GetComponent<UnityEngine.UI.Image>();
                    if (img != null && img.sprite != null)
                    {
                        _rampFrontColor = img.color;
                        return img.sprite;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        // ---- state-aware vanilla icon helpers (Glassy 2.0) ----

        /// <summary>The conditional temperature icon vanilla shows next to a temp readout:
        /// the HOT sprite above 50°C, the COLD sprite below 0°C, null in between — the exact
        /// ShowTooHotLimit(323.15K)/ShowTooColdLimit(273.15K) bands from PlayerStateWindow
        /// (icon-temphigh = Sprites[1], icon-templow = Sprites[0]).</summary>
        public static Sprite TempStateIcon(float celsius)
        {
            if (celsius > 50f) return TempToggle(1); // hot
            if (celsius < 0f) return TempToggle(0);  // cold
            return null;
        }

        private static Sprite TempToggle(int index)
        {
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                var t = psw != null ? psw.ExternalTemperatureToggle : null;
                return t != null && t.Sprites != null && t.Sprites.Length > index ? t.Sprites[index] : null;
            }
            catch { return null; }
        }

        /// <summary>The live sprite on a NAMED child GameObject anywhere under the
        /// PlayerStateWindow subtree — the reliable way to borrow vanilla's static HUD art
        /// (the vitals row icons, the velocity running-man, the jetpack canister) which are
        /// prefab Images, not code fields. Includes inactive children (rows toggle off). The
        /// live Image.sprite is returned, so any ColorBlindImage swap is already reflected.</summary>
        public static Sprite SpriteByName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return null;
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                if (psw == null) return null;
                var imgs = psw.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                for (int i = 0; i < imgs.Length; i++)
                {
                    var img = imgs[i];
                    if (img != null && img.sprite != null
                        && string.Equals(img.gameObject.name, goName, StringComparison.Ordinal))
                        return img.sprite;
                }
            }
            catch { }
            return null;
        }

        /// <summary>The green propellant canister icon from vanilla's jetpack box — the
        /// Image on the child named "SymbolPressureDelta" (icon-jetpackpressure). Grabbing
        /// the FIRST sprite child of the panel instead was wrong: it hit a divider line
        /// (the "glitch line" in the play-test). Cached once resolved.</summary>
        private static Sprite _canister;
        public static Sprite JetpackCanister()
            => _canister != null ? _canister : (_canister = SpriteByName("SymbolPressureDelta"));

        /// <summary>The Toilet Update's bowel-need icon: the sprite on the icon child of
        /// PlayerStateWindow.WastePercentageObject. Prefers a "Symbol"/"Icon"-named child;
        /// falls back to the first sprite-bearing child. Cached once resolved (the panel is
        /// inactive until the need crosses 25%, but its sprite refs exist regardless).</summary>
        private static Sprite _toilet;
        public static Sprite ToiletIcon()
        {
            if (_toilet != null) return _toilet;
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                var obj = psw != null ? psw.WastePercentageObject : null;
                if (obj == null) return null;
                var imgs = obj.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                UnityEngine.UI.Image fallback = null;
                for (int i = 0; i < imgs.Length; i++)
                {
                    var img = imgs[i];
                    if (img == null || img.sprite == null) continue;
                    string n = img.gameObject.name;
                    if (n.IndexOf("Symbol", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                        return _toilet = img.sprite;
                    if (fallback == null) fallback = img;
                }
                if (fallback != null) _toilet = fallback.sprite;
            }
            catch { }
            return _toilet;
        }

        /// <summary>The food-quality star sprite for a 0..1 quality, matching vanilla's
        /// GetFoodQualityIndex bands (&lt;0.45→1★, &lt;0.7→2★, &lt;0.9→3★, else 4★) read off
        /// FoodQualityToggle.Sprites[0..3].</summary>
        public static Sprite FoodQualityStar(float quality01)
        {
            int idx = quality01 < 0.45f ? 0 : quality01 < 0.7f ? 1 : quality01 < 0.9f ? 2 : 3;
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                var t = psw != null ? psw.FoodQualityToggle : null;
                return t != null && t.Sprites != null && t.Sprites.Length > idx ? t.Sprites[idx] : null;
            }
            catch { return null; }
        }

        /// <summary>A suit on/off chip sprite by state — the vanilla ImageToggle sprite for
        /// the given key ("helmet"/"jetpack"/"light") at index 1 when ON, index 0 when OFF
        /// (matching vanilla's SetImage convention). null until the window exists.</summary>
        public static Sprite SuitStateIcon(string key, bool on)
        {
            try
            {
                var psw = Assets.Scripts.UI.PlayerStateWindow.Instance;
                if (psw == null || string.IsNullOrEmpty(key)) return null;
                // Ordinal-ignore-case compares, not key.ToLowerInvariant() which allocates a
                // new lowercased string every call.
                Assets.Scripts.UI.ImageToggle t = null;
                if (string.Equals(key, "helmet", StringComparison.OrdinalIgnoreCase)) t = psw.HelmetImageToggle;
                else if (string.Equals(key, "jetpack", StringComparison.OrdinalIgnoreCase)) t = psw.JetPackImageToggle;
                else if (string.Equals(key, "light", StringComparison.OrdinalIgnoreCase)) t = psw.LightImageToggle;
                int index = on ? 1 : 0;
                return t != null && t.Sprites != null && t.Sprites.Length > index ? t.Sprites[index] : null;
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
                    // Vitals ROW icons — the game's own static symbols (heart / burger /
                    // droplet / running-man), NOT the moodlet WARNING icons. Grabbed by the
                    // named child GameObject so the vitals list matches vanilla exactly.
                    case "hunger": case "food": return SpriteByName("SymbolHunger");
                    case "thirst": case "water": return SpriteByName("SymbolHydration");
                    case "health": return SpriteByName("SymbolHealth");
                    case "toxins": return SpriteByName("SymbolToxins");
                    case "speed": case "velocity": return SpriteByName("SymbolVelocity");
                    case "canister": case "propellant": return SpriteByName("SymbolPressureDelta");
                    case "cognition": case "consciousness": case "unconscious":
                        return SpriteByName("SymbolCognition"); // icon-cognition
                    // The toilet/bowel need icon was added in the game's Toilet Update — it
                    // lives on PlayerStateWindow.WastePercentageObject (the vitals waste panel,
                    // shown when SanitationRatio > 0.25). Grabbed at runtime; this scene rip
                    // predates it so there's no asset to reference, only the live object.
                    case "toilet": return ToiletIcon();
                    case "waste": return Icon(su != null ? su.WasteCritical : null);
                    case "power": case "battery": return Icon(su != null ? su.PowerStateWarning : null);
                    case "o2": case "oxygen": return Icon(su != null ? su.OxygenWarning : null);
                    case "leak": return Icon(su != null ? su.LeakWarning : null);
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
