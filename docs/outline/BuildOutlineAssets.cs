using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildOutlineAssets
{
    public static void Build()
    {
        string modRoot = File.ReadAllText("mod-root.txt").Trim();
        // Build each supported desktop target with explicit shader graphics APIs.
        BuildOne(modRoot, "Windows", BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
        BuildOne(modRoot, "MacOS", BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
        BuildOne(modRoot, "Linux", BuildTarget.StandaloneLinux64,
            new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/MugirlExtraOutline.shader");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
        {
            if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                throw new Exception(message.message);
        }
        Debug.Log("MUGIRL_OUTLINE_BUNDLES_OK");
    }

    private static void BuildOne(string modRoot, string platform, BuildTarget target, GraphicsDeviceType[] apis)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            Debug.LogWarning("MUGIRL_OUTLINE_BUNDLE_SKIPPED " + platform + ": install this Unity platform module to build it.");
            return;
        }
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, apis);
        string output = Path.Combine(modRoot, "1.6/Resources/Outline", platform);
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild
        {
            assetBundleName = "mugirloutline",
            assetNames = new[] { "Assets/MugirlExtraOutline.shader" }
        };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle
                | BuildAssetBundleOptions.StrictMode, target);
        if (manifest == null || !File.Exists(Path.Combine(output, "mugirloutline")))
            throw new Exception("Failed to build Mugirl outline for " + platform);
        Debug.Log("MUGIRL_OUTLINE_BUNDLE_OK " + platform);
    }
}
