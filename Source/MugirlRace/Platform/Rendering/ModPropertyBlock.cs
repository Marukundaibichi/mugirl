using NativeColor = Waylith.Math.Color;
using NativePropertyBlock = Waylith.Rendering.MaterialPropertyBlock;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// <see cref="NativeDraw"/> 使用的逐次绘制着色器参数，<see cref="MaterialPropertyBlock"/> 的原生对应物。
/// 提交绘制时会复制参数值，所以同一个块可以在主线程上清空后反复用于每次绘制。
/// </summary>
/// <remarks>
/// 原生块不会像 UnityStubs 那样把 <c>*_ST</c> 重排为 <c>(z, w, x, y)</c>：原生着色器按 <c>uv * _MainTex_ST.zw + _MainTex_ST.xy</c>
/// 采样，<c>_ST</c> 向量要写成引擎顺序 <c>(offset, scale)</c>。
/// </remarks>
public sealed class ModPropertyBlock
{
    internal NativePropertyBlock Native { get; } = new();

    /// <summary>清空所有值。</summary>
    public ModPropertyBlock Clear()
    {
        Native.Clear();
        return this;
    }

    /// <summary>设置 float 参数。</summary>
    public ModPropertyBlock SetFloat(int propertyId, float value)
    {
        Native.SetFloat(propertyId, value);
        return this;
    }

    /// <summary>设置颜色参数。</summary>
    public ModPropertyBlock SetColor(int propertyId, Color value)
    {
        Native.SetColor(propertyId, new NativeColor(value.r, value.g, value.b, value.a));
        return this;
    }

    /// <summary>设置向量参数。</summary>
    public ModPropertyBlock SetVector(int propertyId, Vector4 value)
    {
        Native.SetVector(propertyId, value);
        return this;
    }

    /// <summary>用 Verse 纹理设置纹理参数（包括 Verse 给出的渲染纹理）。</summary>
    public ModPropertyBlock SetTexture(int propertyId, Texture value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Native.SetTexture(propertyId, NativeBridge.Of(value));
        return this;
    }
}
