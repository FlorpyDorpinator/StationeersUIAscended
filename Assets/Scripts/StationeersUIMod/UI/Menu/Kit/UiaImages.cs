using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>Loads and caches PNG files (profile preview art) as sprites. Textures are owned
    /// here so they can be released together on <see cref="Clear"/> (hot-reload safety).</summary>
    public static class UiaImages
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>Load a PNG as a Sprite (cached by path). Null path / missing file / bad image
        /// all return null so callers just fall back to a placeholder.</summary>
        public static Sprite Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Sprite s;
            if (_cache.TryGetValue(path, out s)) return s;
            _cache[path] = null; // negative-cache so a bad file isn't retried every rebuild
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) { Object.Destroy(tex); return null; }
                tex.wrapMode = TextureWrapMode.Clamp;
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _cache[path] = s;
                return s;
            }
            catch { return null; }
        }

        // ---- the shared rounded-corner sprite (the menu's "curved" look) ----
        // One tiny 9-sliced SDF rounded rect serves every Image in the kit: assigning it turns a
        // sharp quad into a rounded panel with baked AA, at zero per-element cost. Generated once,
        // lazily; the Unity-null check regenerates after a hot-reload destroyed the texture.
        private static Sprite _rounded;

        public static Sprite Rounded()
        {
            if (_rounded != null && _rounded) return _rounded;
            const int S = 32;      // texture size
            const float R = 10f;   // corner radius in sprite px (≈ on-screen reference px via 9-slice)
            const float F = 1.5f;  // AA feather
            var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
            { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // Signed distance to a rounded rect centred in the texture:
                    // q = |p| - half + R;  d = |max(q,0)| + min(max(qx,qy), 0) - R.
                    float qx = Mathf.Abs(x + 0.5f - S * 0.5f) - (S * 0.5f - R);
                    float qy = Mathf.Abs(y + 0.5f - S * 0.5f) - (S * 0.5f - R);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                        + Mathf.Min(Mathf.Max(qx, qy), 0f) - R;
                    float a = Mathf.Clamp01(0.5f - d / F);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _rounded = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(14f, 14f, 14f, 14f)); // 9-slice border
            _rounded.hideFlags = HideFlags.DontSave;
            return _rounded;
        }

        /// <summary>Give an Image the shared rounded-rect sprite (9-sliced): the kit-wide
        /// "curved and rounded" look with baked AA. Safe on any size; hairlines just soften.</summary>
        public static void Round(UnityEngine.UI.Image img)
        {
            if (img == null) return;
            img.sprite = Rounded();
            img.type = UnityEngine.UI.Image.Type.Sliced;
        }

        public static void Clear()
        {
            foreach (var kv in _cache)
            {
                if (kv.Value == null) continue;
                if (kv.Value.texture != null) Object.Destroy(kv.Value.texture);
                Object.Destroy(kv.Value);
            }
            _cache.Clear();
            if (_rounded != null)
            {
                if (_rounded.texture != null) Object.Destroy(_rounded.texture);
                Object.Destroy(_rounded);
                _rounded = null;
            }
        }
    }
}
