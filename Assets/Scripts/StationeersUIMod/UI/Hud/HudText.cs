using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// TMP label factory + font resolution for the HUD. Separate from the radial font so
    /// the two UIs can be styled independently (HudFontName config; empty = the game's
    /// default face). Labels opt into the visor warp via <see cref="TmpWarp"/>.
    /// </summary>
    internal static class HudText
    {
        private static TMP_FontAsset _font;
        private static string _fontFor;

        internal static TMP_FontAsset Font()
        {
            string want = HudConfig.FontName != null ? HudConfig.FontName.Value : "";
            if (_font != null && _fontFor == want) return _font;
            _font = Resolve(want);
            _fontFor = want;
            return _font;
        }

        private static TMP_FontAsset Resolve(string want)
        {
            TMP_FontAsset named = null, first = null;
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all != null)
            {
                foreach (var f in all)
                {
                    if (f == null) continue;
                    if (first == null) first = f;
                    if (named == null && !string.IsNullOrEmpty(want)
                        && f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                        named = f;
                }
            }
            var chosen = named;
            if (chosen == null && TMP_Settings.defaultFontAsset != null)
                chosen = TMP_Settings.defaultFontAsset;
            return chosen ?? first;
        }

        /// <summary>Cheap per-frame sync — no-op unless the config changed in the editor.</summary>
        internal static void Sync(TextMeshProUGUI t)
        {
            var f = Font();
            if (f != null && t.font != f) t.font = f;
        }

        /// <summary>Assign text only when the CONTENT changed — the HUD sets every label
        /// every frame, and an unconditional set would regenerate TMP meshes for text
        /// that didn't move.</summary>
        internal static void Set(TextMeshProUGUI t, string s)
        {
            if (t.text != s) t.text = s;
        }

        internal static void Shutdown()
        {
            _font = null;
            _fontFor = null;
        }

        internal static TextMeshProUGUI Make(Transform parent, string name, float size,
            TextAlignmentOptions align, bool warp = true, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = Font();
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.enableWordWrapping = wrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            if (warp) go.AddComponent<TmpWarp>();
            return tmp;
        }

        /// <summary>All HUD font sizes go through this: base size × the global font scale.</summary>
        internal static float Size(float baseSize)
            => baseSize * (HudConfig.FontScale != null ? HudConfig.FontScale.Value : 1f);

        internal static List<string> AllFontNames()
        {
            var names = new List<string>();
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all == null) return names;
            foreach (var f in all)
                if (f != null && !names.Contains(f.name))
                    names.Add(f.name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
