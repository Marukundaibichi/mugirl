using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ShaderRegistry = Waylith.Rendering.ShaderRegistry;
using WaylithShader = Waylith.Rendering.Shader;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 开发者模式下对模组原生着色器的自检。RimWorld 1.7 中着色器缺失或损坏不会在加载时失败：首次绘制时回退到默认着色器
/// （或显示品红），未知属性名被忽略。这些检查把此类错误变成启动时的日志错误。
/// </summary>
public static class ShaderSelfTest
{
    /// <summary>本模组所有 ShaderDef、MaterialDef 与 GLSL 文件共用的前缀（GLSL 文件按文件名在所有模组间解析）。</summary>
    public const string ShaderNamePrefix = "Mugirl_";

    /// <summary>解析（从而编译）本模组定义的每个 ShaderDef，并报告失败。</summary>
    public static void Run()
    {
        int checkedCount = 0;
        foreach (ShaderDef def in DefDatabase<ShaderDef>.AllDefsListForReading.Where(d => d.defName.StartsWith(ShaderNamePrefix, StringComparison.Ordinal)))
        {
            checkedCount++;
            if (!TryGetShader(def.defName, out _, out string? problem))
            {
                MugirlLog.Error($"Shader self-test: {problem}");
            }
        }

        MugirlLog.DevMessage($"Shader self-test checked {checkedCount} ShaderDefs.");
    }

    /// <summary><paramref name="shaderDefName"/> 没有声明 <paramref name="propertyName"/> 时记录一次错误。</summary>
    internal static void VerifyProperty(string shaderDefName, string propertyName, int propertyId)
    {
        string key = shaderDefName + "/" + propertyName;
        if (!TryGetShader(shaderDefName, out WaylithShader? shader, out string? problem))
        {
            MugirlLog.ErrorOnce(key, $"Shader self-test: {problem} (needed for property {propertyName}).");
        }
        else if (!shader.HasProperty(propertyId))
        {
            MugirlLog.ErrorOnce(key, $"Shader self-test: ShaderDef {shaderDefName} does not declare property {propertyName}; values set for it are ignored.");
        }
    }

    private static bool TryGetShader(string shaderDefName, [NotNullWhen(true)] out WaylithShader? shader, [NotNullWhen(false)] out string? problem)
    {
        if (!ShaderRegistry.TryResolve(shaderDefName, out shader))
        {
            problem = $"ShaderDef {shaderDefName} is not registered with the engine.";
            return false;
        }

        if (shader.HasErrors)
        {
            problem = $"ShaderDef {shaderDefName} failed to compile; it renders with the fallback shader.";
            shader = null;
            return false;
        }

        problem = null;
        return true;
    }
}
