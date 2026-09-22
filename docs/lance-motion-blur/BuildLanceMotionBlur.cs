using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildLanceMotionBlur
{
    public static void Validate()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/MugirlLanceMotionBlur.shader");
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                texture.SetPixel(x, y, x >= 26 && x < 42 && y >= 4 && y < 60
                    ? (y > 44 ? Color.cyan : (y > 32 ? Color.white : Color.red)) : Color.clear);
        texture.Apply();
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        var material = new Material(shader);
        material.SetTexture("_MainTex", texture);
        material.SetTexture("_MaskTex", Texture2D.blackTexture);
        material.SetColor("_Color", Color.white);
        material.SetColor("_ColorTwo", Color.white);
        material.SetVector("_MainTex_ST", new Vector4(1,1,0,0));
        material.SetVector("_MeshUvCenter", new Vector4(.5f,.5f,0,0));
        material.SetVector("_UvRect", new Vector4(0,0,1,1));
        material.SetVector("_BlurUV", new Vector4(2,0,0,0));
        material.SetVector("_Travel", new Vector4(2,0,0,0));
        material.SetVector("_PaddingU", new Vector4(1,0,0,0));
        material.SetVector("_PaddingV", Vector4.zero);
        material.SetFloat("_Opacity", 1f);
        var obj = new GameObject("Blur validation", typeof(MeshFilter), typeof(MeshRenderer));
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        obj.GetComponent<MeshRenderer>().sharedMaterial = material;
        var camera = new GameObject("Validation camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(-.6f, 6f, 0);
        camera.transform.rotation = Quaternion.Euler(90f,0,0);
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var target = new RenderTexture(1024,512,24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var output = new Texture2D(1024,512,TextureFormat.RGBA32,false);
        output.ReadPixels(new Rect(0,0,1024,512),0,0);
        output.Apply();
        File.WriteAllBytes("blur-gpu-check.png", output.EncodeToPNG());
        int colored = 0;
        foreach (Color pixel in output.GetPixels()) if (pixel.r > .02f || pixel.g > .02f || pixel.b > .02f) colored++;
        if (colored < 500) throw new Exception("Blur shader did not render: " + colored);
        Debug.Log("MUGIRL_LANCE_BLUR_GPU_OK pixels=" + colored);
    }

    public static void Build()
    {
        string root = File.ReadAllText("mod-root.txt").Trim();
        BuildOne(root, "Windows", BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
        BuildOne(root, "MacOS", BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
        BuildOne(root, "Linux", BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/MugirlLanceMotionBlur.shader");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                throw new Exception(message.message);
        Debug.Log("MUGIRL_LANCE_BLUR_BUILD_OK");
    }

    private static void BuildOne(string root, string platform, BuildTarget target, GraphicsDeviceType[] apis)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            Debug.LogWarning("MUGIRL_LANCE_BLUR_SKIPPED " + platform);
            return;
        }
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, apis);
        string output = Path.Combine(root, "1.6/Resources/LanceMotionBlur", platform);
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild
        {
            assetBundleName = "mugirllanceblur",
            assetNames = new[] { "Assets/MugirlLanceMotionBlur.shader" }
        };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle
                | BuildAssetBundleOptions.StrictMode, target);
        if (manifest == null) throw new Exception("Lance blur build failed: " + platform);
        Debug.Log("MUGIRL_LANCE_BLUR_PLATFORM_OK " + platform);
    }
}
