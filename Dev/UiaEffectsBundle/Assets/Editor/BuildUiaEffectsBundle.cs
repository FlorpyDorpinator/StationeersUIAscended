using System.IO;
using UnityEditor;
using UnityEngine;

// Minimal AssetBundle builder for uia_effects.bundle.
//
// This lives in a DEDICATED mini content project (Dev/UiaEffectsBundle) rather than the main
// 2022.3.7f1 game project, so the locally-installed 2022.3.62f3 editor can build the bundle
// WITHOUT ever opening the main project with a mismatched editor. Beef's shipped mods prove that
// .62-built bundles load fine in the 7f1 game (same 2022.3 LTS minor); HudShaderStore loads
// fail-soft either way.
//
// NO ShaderVariantCollection is generated and the four shaders use ZERO shader_feature keywords
// (every effect toggle is a uniform float) -> there are no variants to strip, so variant stripping
// is a non-issue. If a future shader ever adds shader_feature, add an SVC here and mark it too.
public static class UiaBundleBuilder
{
    private const string BundleName = "uia_effects.bundle";
    private const string OutputDir = "Build";

    private static readonly string[] ShaderAssets =
    {
        "Assets/Shaders/HudEdgeFX.shader",
        "Assets/Shaders/HudGlass.shader",
        "Assets/Shaders/HudBlur.shader",
        "Assets/Shaders/HudBloom.shader",
    };

    // Invoked by build-bundle.bat: Unity.exe ... -executeMethod UiaBundleBuilder.Build
    [MenuItem("UIA/Build Effects Bundle")]
    public static void Build()
    {
        // 1. Tag each shader asset with the bundle name via its importer.
        foreach (string path in ShaderAssets)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogError("UiaBundleBuilder: asset not found: " + path);
                continue;
            }
            if (importer.assetBundleName != BundleName)
            {
                importer.assetBundleName = BundleName;
                importer.SaveAndReimport();
            }
        }

        // 2. Ensure the output directory exists (BuildAssetBundles requires it).
        if (!Directory.Exists(OutputDir))
            Directory.CreateDirectory(OutputDir);

        // 3. Build the bundle. ChunkBasedCompression matches Beef's shipped bundles; ForceRebuild
        //    guarantees a clean artifact every run.
        BuildPipeline.BuildAssetBundles(
            OutputDir,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        AssetDatabase.Refresh();

        string outPath = Path.GetFullPath(Path.Combine(OutputDir, BundleName));
        if (File.Exists(outPath))
            Debug.Log("UiaBundleBuilder: built " + BundleName + " -> " + outPath);
        else
            Debug.LogError("UiaBundleBuilder: build finished but " + outPath + " is missing.");
    }
}
