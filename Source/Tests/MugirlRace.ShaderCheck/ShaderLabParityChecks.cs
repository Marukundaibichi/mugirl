using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 本模组 ShaderDef 的渲染状态与 1.6 ShaderLab 原版逐项对照（docs/outline/ExtraOutlineRenderer.shader、
/// docs/lance-motion-blur/MugirlLanceMotionBlur.shader）：Queue Transparent-100、Cull Off、ZWrite Off、ZTest LEqual、
/// Blend SrcAlpha OneMinusSrcAlpha（颜色与 alpha 相同），以及 C# 依赖的贴图寻址。防止 XML 被改动后与原版效果悄悄不同。
/// </summary>
internal static class ShaderLabParityChecks
{
    private static readonly BlendState ShaderLabAlphaBlend = new(
        Enabled: true, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOp.Add, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOp.Add);

    // defName -> 必须为 Clamp 的纹理。
    private static readonly Dictionary<string, string[]> Expected = new(StringComparer.Ordinal)
    {
        ["Mugirl_ExtraOutline"] = ["_MainTex"],
        ["Mugirl_LanceMotionBlur"] = ["_MainTex", "_MaskTex"],
    };

    /// <summary>运行检查。</summary>
    public static void Run(IReadOnlyList<(ShaderDefEntry Entry, Shader Shader)> own, CheckReport report)
    {
        CheckReport.Section("ShaderLab parity of the render state");
        foreach ((string defName, string[] clampedTextures) in Expected)
        {
            (ShaderDefEntry Entry, Shader Shader) match = own.FirstOrDefault(pair => pair.Entry.DefName == defName);
            if (match.Shader is null)
            {
                report.Fail($"{defName}: not registered, so its render state cannot be checked.");
                continue;
            }

            Shader shader = match.Shader;
            PipelineState state = shader.PipelineState;
            List<string> problems = [];
            if (shader.DefaultRenderQueue != 2900)
            {
                problems.Add($"queue {shader.DefaultRenderQueue}, expected 2900 (Transparent-100)");
            }

            if (!shader.Transparent)
            {
                problems.Add("not transparent");
            }

            if (state.Blend != ShaderLabAlphaBlend)
            {
                problems.Add($"blend {state.Blend}, expected {ShaderLabAlphaBlend} (Blend SrcAlpha OneMinusSrcAlpha)");
            }

            if (!state.Depth.Test || state.Depth.Write)
            {
                problems.Add($"depth {state.Depth}, expected test on (ZTest LEqual) and write off (ZWrite Off)");
            }

            if (state.Cull != FaceCull.None)
            {
                problems.Add($"cull {state.Cull}, expected None (Cull Off)");
            }

            if (shader.FlipTextureST)
            {
                problems.Add("flipTextureST is true; the GLSL flips V itself");
            }

            Dictionary<string, TextureWrapMode> wraps = match.Entry.Program?.textureWrapOverrides ?? [];
            foreach (string texture in clampedTextures.Where(texture => !wraps.TryGetValue(texture, out TextureWrapMode mode) || mode != TextureWrapMode.Clamp))
            {
                problems.Add($"{texture} is not clamped (UV clamping in the shader relies on it at the rect border)");
            }

            if (problems.Count == 0)
            {
                CheckReport.Pass($"{defName}: render state matches the ShaderLab original.");
            }
            else
            {
                report.Fail($"{defName}: {string.Join("; ", problems)}.");
            }
        }

        foreach ((ShaderDefEntry entry, _) in own.Where(pair => !Expected.ContainsKey(pair.Entry.DefName)))
        {
            report.Fail($"{entry.DefName}: no ShaderLab parity expectation; add one to ShaderLabParityChecks.");
        }
    }
}
