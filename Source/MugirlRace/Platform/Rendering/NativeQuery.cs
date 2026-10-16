using Waylith.Rendering;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 只读查询游戏发布给着色器的全局渲染状态，替代 <c>Shader.GetGlobalVector</c>/<c>GetGlobalFloat</c>。
/// 模组不写全局状态（<c>Shader.SetGlobal*</c> 已禁用）。
/// </summary>
public static class NativeQuery
{
    /// <summary>
    /// 读取全局向量（例如 <see cref="ShaderPropertyIDs.MapSunLightDirection"/>）；游戏尚未设置时返回 <see langword="false"/>，
    /// 而不是像 Unity 那样得到零向量。
    /// </summary>
    public static bool TryGetGlobalVector(int propertyId, out Vector4 value) => GlobalShaderState.TryGetVector(propertyId, out value);

    /// <summary>读取全局 float；尚未设置时返回 <see langword="false"/>。</summary>
    public static bool TryGetGlobalFloat(int propertyId, out float value) => GlobalShaderState.TryGetFloat(propertyId, out value);
}
