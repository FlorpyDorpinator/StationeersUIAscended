using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Optional user PNG overrides for HUD icons. Anyone can drop <c>&lt;name&gt;.png</c> into
    /// <see cref="Dir"/> and have it returned as a Sprite by <see cref="TryGet"/> — the caller
    /// decides whether to prefer it over the built-in vector glyph.
    ///
    /// The directory is scanned lazily, exactly once. Every Texture2D/Sprite this class creates
    /// is tracked so <see cref="Shutdown"/> can destroy them: under ScriptEngine hot-reload the
    /// static state is torn down and rebuilt, and leaked Unity objects would survive the reload.
    /// Load failures are per-file and fail soft — one bad PNG never blocks the rest.
    /// </summary>
    public static class HudIconStore
    {
        /// <summary>Where users drop icon overrides. Created on first scan if absent.</summary>
        public static string Dir => Path.Combine(BepInEx.Paths.ConfigPath, "StationeersUIMod", "HudIcons");

        // Case-insensitive: "Flame.png" answers a TryGet("flame"). Misses are cached as null
        // so a name with no file does not re-probe the dictionary or the disk.
        private static readonly Dictionary<string, Sprite> _cache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Texture2D> _textures = new List<Texture2D>();
        private static bool _scanned;

        /// <summary>The override sprite for <paramref name="name"/> (file stem, no extension),
        /// or null when no such PNG exists. Triggers the one-time directory scan on first call.</summary>
        public static Sprite TryGet(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!_scanned) Scan();
            if (_cache.TryGetValue(name, out var sprite)) return sprite;
            _cache[name] = null; // remember the miss
            return null;
        }

        private static void Scan()
        {
            _scanned = true;
            try
            {
                Directory.CreateDirectory(Dir);
                var files = Directory.GetFiles(Dir, "*.png");
                int loaded = 0;
                foreach (var file in files)
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        // mipChain false: HUD icons are drawn near 1:1, mipmaps would only blur them.
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        tex.filterMode = FilterMode.Bilinear;
                        if (!tex.LoadImage(bytes, false))
                        {
                            UnityEngine.Object.Destroy(tex);
                            UIALog.Warn($"HudIconStore: '{Path.GetFileName(file)}' is not a decodable PNG; skipped.");
                            continue;
                        }
                        tex.filterMode = FilterMode.Bilinear; // LoadImage can reset sampler state
                        var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                        _textures.Add(tex);
                        _cache[Path.GetFileNameWithoutExtension(file)] = sprite;
                        loaded++;
                    }
                    catch (Exception e)
                    {
                        UIALog.Warn($"HudIconStore: failed to load '{Path.GetFileName(file)}': {e.Message}");
                    }
                }
                UIALog.Info($"HudIconStore: loaded {loaded} icon override(s) from {Dir}.");
            }
            catch (Exception e)
            {
                UIALog.Error("HudIconStore scan failed: " + e);
            }
        }

        /// <summary>Destroy every created sprite/texture and reset to unscanned, so the next
        /// <see cref="TryGet"/> re-reads the directory (picks up added/edited PNGs).</summary>
        public static void Reload()
        {
            foreach (var kv in _cache)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            foreach (var tex in _textures)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            _cache.Clear();
            _textures.Clear();
            _scanned = false;
        }

        /// <summary>Hot-reload teardown — identical to <see cref="Reload"/>; created Unity
        /// objects must not outlive the mod instance.</summary>
        public static void Shutdown() => Reload();
    }
}
