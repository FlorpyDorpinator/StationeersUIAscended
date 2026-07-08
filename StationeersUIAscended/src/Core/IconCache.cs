using System;
using System.Collections.Generic;
using Assets.Scripts.UI;
using UnityEngine;

namespace StationeersUIAscended.Core
{
    /// <summary>
    /// Sprite → ImGui texture-id resolution. The game's TextureManager handles the actual
    /// registration (ImGuiManager.ImGuiPointerFor); we just cache the sprite→(id, uv) tuple.
    /// Item thumbnails from Thing.GetThumbnail() are standalone textures in vanilla, but we
    /// still honor sprite.textureRect so atlas-packed sprites from mods draw correctly.
    /// </summary>
    public static class IconCache
    {
        public struct IconInfo
        {
            public IntPtr TextureId;
            public Vector2 Uv0;
            public Vector2 Uv1;
            public bool Valid;
        }

        private static readonly Dictionary<Sprite, IconInfo> Cache = new Dictionary<Sprite, IconInfo>();

        public static IconInfo Get(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return default;
            if (Cache.TryGetValue(sprite, out var cached)) return cached;

            var tex = sprite.texture;
            IconInfo info;
            try
            {
                Rect r;
                try { r = sprite.textureRect; }
                catch { r = new Rect(0, 0, tex.width, tex.height); } // tight-packed sprites throw
                float w = tex.width, h = tex.height;
                var uvMin = new Vector2(r.xMin / w, r.yMin / h);
                var uvMax = new Vector2(r.xMax / w, r.yMax / h);
                bool flip = UIAConfig.IconFlipV.Value;
                info = new IconInfo
                {
                    TextureId = ImGuiManager.ImGuiPointerFor(tex),
                    Uv0 = flip ? new Vector2(uvMin.x, uvMax.y) : uvMin,
                    Uv1 = flip ? new Vector2(uvMax.x, uvMin.y) : uvMax,
                    Valid = true,
                };
            }
            catch
            {
                info = default;
            }
            Cache[sprite] = info;
            return info;
        }

        public static void Clear() => Cache.Clear();
    }
}
