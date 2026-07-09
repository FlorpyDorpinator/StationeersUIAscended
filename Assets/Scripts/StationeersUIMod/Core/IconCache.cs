using System;
using System.Collections.Generic;
using Assets.Scripts.UI;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Sprite → ImGui (texture id, uv) resolution. Only UVs are cached: the game's
    /// TextureManager clears its id registry EVERY frame (ImGuiManager.PrepareImGuiFrame →
    /// TextureManager.PrepareFrame), so texture ids must be re-resolved per draw exactly
    /// like vanilla does (ImguiCreativeSpawnMenu.GetTextureId at draw time). Caching an id
    /// across frames draws the wrong texture or font-atlas garbage.
    /// </summary>
    public static class IconCache
    {
        public struct IconInfo
        {
            public IntPtr TextureId;
            public Vector2 Uv0;
            public Vector2 Uv1;
            /// <summary>Pixel size of the SAMPLED region — i.e. the same rect the UVs derive
            /// from. Aspect must come from here: using sprite.rect while the UVs come from
            /// sprite.textureRect warps every trimmed / atlas-packed sprite.</summary>
            public Vector2 PixelSize;
            public bool Valid;
        }

        private struct UvInfo
        {
            public Vector2 Uv0;
            public Vector2 Uv1;
            public Vector2 PixelSize;
        }

        private static readonly Dictionary<Sprite, UvInfo> UvCache = new Dictionary<Sprite, UvInfo>();

        public static IconInfo Get(Sprite sprite)
        {
            if (sprite == null) return default;
            Texture2D tex;
            try { tex = sprite.texture; } catch { return default; }
            if (tex == null) return default;

            if (!UvCache.TryGetValue(sprite, out var uv))
            {
                try
                {
                    Rect r;
                    try { r = sprite.textureRect; }
                    catch { r = new Rect(0, 0, tex.width, tex.height); } // tight-packed sprites throw
                    float w = tex.width, h = tex.height;
                    var uvMin = new Vector2(r.xMin / w, r.yMin / h);
                    var uvMax = new Vector2(r.xMax / w, r.yMax / h);
                    bool flip = UIAConfig.IconFlipV.Value;
                    uv = new UvInfo
                    {
                        Uv0 = flip ? new Vector2(uvMin.x, uvMax.y) : uvMin,
                        Uv1 = flip ? new Vector2(uvMax.x, uvMin.y) : uvMax,
                        PixelSize = new Vector2(Mathf.Abs(r.width), Mathf.Abs(r.height)),
                    };
                }
                catch
                {
                    uv = new UvInfo { Uv0 = Vector2.zero, Uv1 = Vector2.one, PixelSize = new Vector2(tex.width, tex.height) };
                }
                UvCache[sprite] = uv;
            }

            try
            {
                return new IconInfo
                {
                    TextureId = ImGuiManager.ImGuiPointerFor(tex), // per frame, like vanilla
                    Uv0 = uv.Uv0,
                    Uv1 = uv.Uv1,
                    PixelSize = uv.PixelSize,
                    Valid = true,
                };
            }
            catch
            {
                return default;
            }
        }

        public static void Clear() => UvCache.Clear();
    }
}

