using System;
using System.IO;
using Assets.Scripts;   // GameManager (IsBatchMode) lives in Assets.Scripts, not the global namespace
using UnityEngine;
using StationeersUIMod.UI.Hud;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Loads <c>uia_effects.bundle</c> (the Tier B/C shader bundle) and hands its shaders to
    /// <see cref="HudFxMaterials"/>. Fail-soft BY CONTRACT (Master-Plan 0.9.0 §4.1, §7.3, §12.9):
    /// ANY failure leaves <see cref="TierBAvailable"/> == false and every Tier B/C effect silently
    /// renders its Tier A fallback. This class NEVER throws to its caller.
    ///
    /// HOT-RELOAD SAFE (the "Beef gap", §0.6 / §4.1): a prior F6 life can leave our bundle resident,
    /// and a second <c>AssetBundle.LoadFromFile</c> on the same file throws. <see cref="EnsureLoaded"/>
    /// first scans <c>AssetBundle.GetAllLoadedAssetBundles()</c> and REUSES a resident copy;
    /// <see cref="Shutdown"/> only unloads the bundle if THIS life loaded it (<c>_ownsBundle</c>) —
    /// it never yanks shaders that a reused bundle still owns.
    ///
    /// Headless dedicated servers run OnLoaded too, so we short-circuit on
    /// <c>GameManager.IsBatchMode</c> (§12.9). Materials referencing these shaders are created and
    /// destroyed by <see cref="HudFxMaterials"/> (separate, already wired) — we only own the bundle
    /// and the raw <see cref="Shader"/> references.
    /// </summary>
    public static class HudShaderStore
    {
        // The file name we ship the bundle under (never "*.assets" — SLP auto-loads those; §0/SLP).
        private const string BundleFileName = "uia_effects.bundle";

        // The internal bundle name Unity bakes from the assetBundleName (lowercased for matching).
        private const string BundleAssetName = "uia_effects.bundle";

        // LAST-fallback dev-output path for the F6 flow (no ModData, assembly loaded from bytes).
        // Hardcode acceptable ONLY as the final dev fallback (task brief) — production uses ModDirectory.
        private const string DevBundlePath =
            @"C:\Dev\Stationeers UI Ascended\Dev\UiaEffectsBundle\Build\uia_effects.bundle";

        private static bool _attempted;    // idempotency: EnsureLoaded runs its body once per life
        private static bool _ownsBundle;   // true ONLY if THIS life called LoadFromFile (else reused)
        private static AssetBundle _bundle;

        /// <summary>True once the bundle loaded AND at least one effect shader resolved into a
        /// material family (Tier B available). Tier B/C effects check this and fall back to Tier A
        /// when false.</summary>
        public static bool TierBAvailable { get; private set; }

        /// <summary>The dual-Kawase blur shader for the Tier C backdrop chain. NOT a material family
        /// (HudBackdrop drives it directly via <c>Graphics.Blit</c>); may be null even when
        /// <see cref="TierBAvailable"/> is true, since the blit pass is optional.</summary>
        public static Shader BlurShader { get; private set; }

        /// <summary>The bright-pass + additive-composite shader for Stage 2 HUD bloom. NOT a material
        /// family (HudBloomFx drives it directly via <c>Graphics.Blit</c>); may be null even when
        /// <see cref="TierBAvailable"/> is true — bloom is an optional blit chain.</summary>
        public static Shader BloomShader { get; private set; }

        /// <summary>True only when the analytic panel SDF shader resolved and its shared
        /// <c>sdfglass</c> material family was registered. Kept separate from
        /// <see cref="TierBAvailable"/> so a panel never emits the SDF vertex contract unless
        /// the matching material can actually be assigned.</summary>
        public static bool SdfAvailable { get; private set; }

        /// <summary>The resident <c>UIA/HudPanelSdf</c> shader's declared <c>_UiaSdfAbiVersion</c>
        /// (0 when no SDF shader resolved). Minimum 2 is required for <see cref="SdfAvailable"/>;
        /// individual features that need a LATER contract gate on this value instead of forking
        /// the whole renderer, so one stale bundle costs exactly one feature.</summary>
        public static int SdfAbi { get; private set; }

        /// <summary>True when the resident panel-SDF shader understands the EXTENDED superellipse
        /// exponent lane — p in [1,8] packed as (p-1)/7 and announced per panel by flag bit 256
        /// (ABI 3). Exponent 1 is the L1 norm, whose zero contour IS the 45-degree chamfer, so
        /// this is what lets a CUT panel stay on the analytic renderer with the full frost /
        /// chroma / halo-v2 / edge-flow / dissolve / shine / iridescence set. False (an ABI-2
        /// bundle, or none) keeps the pre-0.9.2.6 behaviour exactly: cut panels render on the
        /// mesh path, and every packed exponent uses the old (p-2)/6 lane.</summary>
        public static bool SdfCutAvailable => SdfAbi >= 3;

        /// <summary>True when the resident mesh-FX shaders (HudEdgeFX/HudGlass) understand the
        /// uv1 flow payload (<c>_UiaFlowAbiVersion</c> >= 1). A pre-flow RESIDENT bundle after
        /// F6 still registers the edgefx/glass families (TierBAvailable stays true), but its
        /// shaders ignore uv1 — pushing a FlowSpeed then suppresses the static ripple bake and
        /// nothing animates it, so shapes lose their shimmer entirely. Gating FlowSpeed on this
        /// keeps the documented fail-soft: old bundle -> the static look.</summary>
        public static bool FlowAbiAvailable { get; private set; }

        /// <summary>Idempotent lazy loader. No-op after the first attempt (success OR failure) this
        /// life. Safe on a headless server (batch mode short-circuits). Never throws.</summary>
        public static void EnsureLoaded()
        {
            if (_attempted) return;
            _attempted = true;

            // Dedicated server: OnLoaded runs, but there is no rendering / no shader use.
            // GameManager is in the GLOBAL namespace (no using needed).
            if (GameManager.IsBatchMode) return;

            try
            {
                // Hot-reload: reuse a bundle a prior F6 life left resident (a 2nd LoadFromFile throws).
                _bundle = FindResidentBundle();
                if (_bundle != null)
                {
                    _ownsBundle = false; // not ours to unload
                }
                else
                {
                    string path = ResolveBundlePath();
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        UIALog.Warn("HudShaderStore: " + BundleFileName + " not found (Tier B/C disabled; " +
                                    "Tier A fallbacks active). Last path tried: " + (path ?? "<none>"));
                        return;
                    }

                    _bundle = LoadBundle(path);
                    if (_bundle == null)
                    {
                        UIALog.Warn("HudShaderStore: bundle load returned null for '" + path +
                                    "' (Tier B/C disabled; Tier A fallbacks active).");
                        return;
                    }
                    _ownsBundle = true;
                }

                // Resolve each shader by full name, then asset file name, then a scan (Beef's pattern).
                Shader edgeFx = LoadShader(_bundle, "UIA/HudEdgeFX", "HudEdgeFX");
                Shader glass = LoadShader(_bundle, "UIA/HudGlass", "HudGlass");
                Shader panelSdf = LoadShader(_bundle, "UIA/HudPanelSdf", "HudPanelSdf");
                Shader blur = LoadShader(_bundle, "UIA/HudBlur", "HudBlur");
                Shader bloom = LoadShader(_bundle, "UIA/HudBloom", "HudBloom");

                BlurShader = blur; // Tier C chain shader (kept as a raw Shader, not a material family)
                BloomShader = bloom; // Stage 2 bloom chain shader (same — HudBloomFx blits it directly)

                bool anyRegistered = false;
                if (edgeFx != null) { HudFxMaterials.Register("edgefx", edgeFx); anyRegistered = true; }
                if (glass != null) { HudFxMaterials.Register("glass", glass); anyRegistered = true; }
                // Both mesh-FX shaders ship in the same bundle, so probing edgefx speaks for
                // glass too; an older resident copy simply leaves the flow contract off.
                FlowAbiAvailable = edgeFx != null
                    && SupportsAbi(edgeFx, "_UiaFlowAbiVersion", 1f);
                if (panelSdf != null)
                {
                    // Read the ABI ONCE and keep the number: the renderer needs >= 2, while the
                    // cut-corner exponent lane needs >= 3. Anything older simply loses the newer
                    // feature (PanelGraphic then packs the legacy lane and the style pushers keep
                    // cut panels on the mesh renderer) — never the whole analytic path.
                    SdfAbi = Mathf.FloorToInt(ReadAbi(panelSdf, "_UiaSdfAbiVersion"));
                    if (SdfAbi >= 2)
                        SdfAvailable = HudFxMaterials.Register("sdfglass", panelSdf) != null;
                    else
                        UIALog.Warn("HudShaderStore: resident UIA/HudPanelSdf is ABI 1 or unknown. " +
                            "Analytic panels remain on the mesh fallback until the rebuilt bundle is " +
                            "loaded by a full game restart.");
                    if (SdfAvailable && SdfAbi < 3)
                        UIALog.Warn("HudShaderStore: resident UIA/HudPanelSdf is ABI " + SdfAbi +
                            " (cut corners need ABI 3). CUT panels keep the mesh renderer and lose " +
                            "the analytic extras until the rebuilt bundle is loaded by a full game " +
                            "restart; rounded panels are unaffected.");
                    anyRegistered |= SdfAvailable;
                }

                // Tier B is "available" once we have at least one effect-material shader. Blur is optional.
                TierBAvailable = anyRegistered;

                if (!TierBAvailable)
                    UIALog.Warn("HudShaderStore: bundle loaded but no effect shaders resolved " +
                                "(UIA/HudEdgeFX, UIA/HudGlass, UIA/HudPanelSdf). Tier B/C disabled.");
            }
            catch (Exception e)
            {
                TierBAvailable = false;
                SdfAvailable = false;
                SdfAbi = 0;
                UIALog.Warn("HudShaderStore: shader bundle load failed (" + e.Message +
                            "). Tier B/C disabled; Tier A fallbacks active.");
            }
        }

        private static bool SupportsAbi(Shader shader, string versionProperty, float minimum)
            => ReadAbi(shader, versionProperty) >= minimum;

        /// <summary>The shader's declared ABI stamp, or 0 when the shader is missing, carries no
        /// such property (a pre-stamp build) or the probe throws. Never throws.</summary>
        private static float ReadAbi(Shader shader, string versionProperty)
        {
            if (shader == null) return 0f;
            Material probe = null;
            try
            {
                probe = new Material(shader) { hideFlags = HideFlags.DontSave };
                return probe.HasProperty(versionProperty)
                    ? probe.GetFloat(versionProperty)
                    : 0f;
            }
            catch { return 0f; }
            finally
            {
                if (probe != null) UnityEngine.Object.Destroy(probe);
            }
        }

        /// <summary>Scan already-loaded bundles for a resident copy of ours (prior F6 life). Never throws.</summary>
        private static AssetBundle FindResidentBundle()
        {
            try
            {
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b == null) continue;
                    string n = b.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    n = n.ToLowerInvariant();
                    if (n == BundleAssetName || n.Contains("uia_effects"))
                        return b;
                }
            }
            catch { /* fail-soft: treat as "no resident bundle" */ }
            return null;
        }

        /// <summary>Resolve the bundle path: (1) SLP install dir (production), (2) assembly-adjacent
        /// dir (plugins-copy install), (3) hardcoded repo dev-output (F6 flow). Never throws; returns
        /// null when nothing on disk is found.</summary>
        private static string ResolveBundlePath()
        {
            // 1. SLP install (local or Workshop) — the shipping path. NULL under the F6 dev flow.
            //    (StationeersUIMod here is the CLASS in the enclosing namespace, not this namespace;
            //     resolves exactly like Core/Patches.cs's StationeersUIMod.Instance.)
            try
            {
                string dir = StationeersUIMod.ModDirectory;
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, BundleFileName);
                    if (File.Exists(p)) return p;
                }
            }
            catch { /* fall through */ }

            // 2. Assembly-adjacent (a plugins-copy install may sit the bundle next to the DLL).
            //    Assembly.Location is empty/garbage under ScriptEngine, hence the try/catch.
            try
            {
                string asmDir = Path.GetDirectoryName(typeof(HudShaderStore).Assembly.Location) ?? ".";
                string p = Path.Combine(asmDir, BundleFileName);
                if (File.Exists(p)) return p;
            }
            catch { /* fall through */ }

            // 3. LAST fallback: this repo's dev-output (F6 flow). Hardcode acceptable — dev only.
            try
            {
                if (File.Exists(DevBundlePath)) return DevBundlePath;
            }
            catch { /* fall through */ }

            return null;
        }

        /// <summary>LoadFromFile keeps the FILE HANDLE open for the bundle's whole life — which
        /// blocked the Unity bundle REBUILD while the game ran (play-test round 12: BuildPipeline
        /// "Failed to replace file", and the stale bundle then shipped silently). The dev-output
        /// path gets rebuilt constantly, so load THAT one from memory (no lock; the bundle is
        /// tiny). Shipped installs keep the cheaper memory-mapped LoadFromFile. The resident
        /// in-memory copy still needs a game RESTART to pick up new shaders — this only frees
        /// the on-disk file for rebuilds while the game runs.</summary>
        private static AssetBundle LoadBundle(string path)
        {
            if (string.Equals(path, DevBundlePath, System.StringComparison.OrdinalIgnoreCase))
                return AssetBundle.LoadFromMemory(File.ReadAllBytes(path));
            return AssetBundle.LoadFromFile(path);
        }

        /// <summary>Load a shader by full name, then asset file name, then by scanning all shaders in
        /// the bundle and matching by name (Beef's GetShader fallback; BeefsShaderChangesPlugin.cs:340).
        /// Never throws.</summary>
        private static Shader LoadShader(AssetBundle b, string fullName, string fileName)
        {
            if (b == null) return null;
            Shader s = null;
            try { s = b.LoadAsset<Shader>(fullName); } catch { }
            if (s == null) { try { s = b.LoadAsset<Shader>(fileName); } catch { } }
            if (s == null)
            {
                try
                {
                    Shader[] all = b.LoadAllAssets<Shader>();
                    if (all != null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            Shader sh = all[i];
                            if (sh == null) continue;
                            if (sh.name == fullName || sh.name.Contains(fileName) || fullName.Contains(sh.name))
                            {
                                s = sh;
                                break;
                            }
                        }
                    }
                }
                catch { /* fail-soft */ }
            }
            // A shader with no pass compiled for the running graphics API still LOADS, but Unity
            // draws it with the magenta error shader (GitHub issue #5: pink squares under
            // -force-vulkan with a DX11-only bundle). Treat it as missing so the Tier A fallback
            // runs instead. Never throws.
            try
            {
                if (s != null && !s.isSupported)
                {
                    UIALog.Warn("HudShaderStore: " + fullName + " is not supported on " +
                                SystemInfo.graphicsDeviceType + " (the bundle has no pass for it); " +
                                "that effect falls back to the plain renderer.");
                    s = null;
                }
            }
            catch { /* fail-soft */ }
            return s;
        }

        /// <summary>Hot-reload / teardown. Unloads the bundle ONLY if this life loaded it — a reused
        /// resident bundle stays owned by whoever loaded it (unloading it would strip shaders that
        /// still-live materials reference). <c>Unload(false)</c> keeps already-loaded shader objects
        /// alive for any in-flight materials; those materials are destroyed by
        /// <see cref="HudFxMaterials.Shutdown"/>. Resets the attempted flag so the next life retries.</summary>
        public static void Shutdown()
        {
            try
            {
                if (_bundle != null && _ownsBundle)
                    _bundle.Unload(false);
            }
            catch { /* fail-soft */ }

            _bundle = null;
            _ownsBundle = false;
            BlurShader = null;
            BloomShader = null;
            TierBAvailable = false;
            SdfAvailable = false;
            SdfAbi = 0;
            FlowAbiAvailable = false;
            _attempted = false; // let the next life re-attempt the load
        }
    }
}
