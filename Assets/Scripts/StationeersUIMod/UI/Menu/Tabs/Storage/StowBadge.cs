using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// The Bag Profile colour badge (ANSWER #8: distinct AUTO colours, no picker, no new data).
    ///
    /// <para>Colour derivation: a stable FNV-1a hash of the profile NAME seeds a golden-ratio
    /// hue walk (successive seeds land maximally apart on the wheel), while saturation and value
    /// come from the CURRENT theme accent via <see cref="Color.RGBToHSV(Color, out float, out float, out float)"/>
    /// — so every theme tints the whole badge family its own way, nothing is hard-coded
    /// (FlorpyDorp's dynamic-theming directive), and the same profile name gets the same hue on
    /// every machine and in every session. No per-profile colour is stored and share codes are
    /// untouched.</para>
    /// </summary>
    internal static class StowBadge
    {
        /// <summary>Stable, distinct colour for a profile name. Same name = same hue, always.</summary>
        public static Color ColorFor(string profileName)
        {
            uint h = Fnv1a(profileName ?? "");
            float accentH, accentS, accentV;
            Color.RGBToHSV(UiaTheme.Accent, out accentH, out accentS, out accentV);
            // Golden-ratio conjugate walk from the accent hue, stepped by the name hash: the
            // low bits pick the step count, so related names still spread apart.
            float hue = Mathf.Repeat(accentH + (h % 4096u) * 0.61803398875f, 1f);
            // Keep S/V in a readable band whatever the theme accent is (a near-grey accent
            // would otherwise collapse every badge into mud), but let the accent steer them.
            float s = Mathf.Clamp(accentS, 0.45f, 0.85f);
            float v = Mathf.Clamp(accentV, 0.55f, 0.95f);
            return Color.HSVToRGB(hue, s, v);
        }

        /// <summary>Draw one badge chip: solid derived colour, 2-4 char tag
        /// (<see cref="BagProfileStore.ProfileTag"/>), contrast-picked text colour.</summary>
        public static GameObject Chip(Transform parent, string profileName, float w = 46f, float h = 20f)
        {
            Color c = ColorFor(profileName);
            var go = UiaUi.Go("badge", parent);
            UiaUi.Size(go, h, w, flexW: 0f);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            UiaImages.Round(img);
            string tag = "BAG";
            try { tag = BagProfileStore.ProfileTag(profileName); } catch { }
            // Perceived luminance picks dark-on-light vs light-on-dark text.
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            var t = UiaUi.Text(go.transform, tag, 11f,
                lum > 0.55f ? new Color(0.05f, 0.07f, 0.10f, 1f) : new Color(0.95f, 0.97f, 1f, 1f),
                TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            UiaUi.Fill((RectTransform)t.transform);
            return go;
        }

        /// <summary>FNV-1a over the raw chars — a STABLE hash (string.GetHashCode is
        /// runtime/session-dependent on some CLRs, which would recolour badges per launch).</summary>
        private static uint Fnv1a(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return h;
            }
        }
    }
}
