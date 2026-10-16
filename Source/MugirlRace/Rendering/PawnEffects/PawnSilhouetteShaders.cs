namespace Mugirl.Rendering;

/// <summary>
/// 构成人物轮廓的原版镂空着色器：描边与运动拖尾只处理这些图层；伤口、泡沫、纹身、阴影与叠加效果不算轮廓。
/// 按引擎着色器名（即 ShaderDef 的 defName）比较，名字取自原版
/// ShaderDatabase 着色器，不经 UnityStubs 的 Object 运算符。
/// </summary>
internal static class PawnSilhouetteShaders
{
    // StaticCacheLifecycle: 进程级常量（原版 ShaderDef 名），首次绘制时由 ShaderDatabase 构建，永不清理。只在主线程使用。
    private static HashSet<string>? names;

    /// <summary><paramref name="shaderName"/> 是轮廓着色器。</summary>
    internal static bool Contains(string shaderName)
    {
        names ??= Build();
        return names.Contains(shaderName);
    }

    // ShaderDatabase 的着色器由 Shader.CreateLateBound(defName) 创建，name 就是 defName（例如 CutoutSkinColorOverride 为 "CutoutSkinOverride"）。
    private static HashSet<string> Build() => new(StringComparer.Ordinal)
    {
        ShaderDatabase.Cutout.name,
        ShaderDatabase.CutoutComplex.name,
        ShaderDatabase.CutoutHair.name,
        ShaderDatabase.CutoutSkin.name,
        ShaderDatabase.CutoutSkinColorOverride.name,
        ShaderDatabase.CutoutWithOverlay.name,
    };
}
