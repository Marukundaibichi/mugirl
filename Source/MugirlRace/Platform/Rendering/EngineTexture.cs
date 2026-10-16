using NativeTexture = Waylith.Rendering.Texture;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 引擎纹理的不透明句柄：由 <see cref="EngineMaterialView"/> 从 Verse 材质读出，原样交给 <see cref="NativeShaderPass"/>。
/// 业务代码因此不接触 Waylith 类型，也不必经 UnityStubs 的 <c>Material.mainTexture</c> 取回 <see cref="Texture"/>。
/// </summary>
public readonly struct EngineTexture
{
    internal EngineTexture(NativeTexture? native) => Native = native;

    /// <summary>材质上没有这张纹理。</summary>
    public bool IsNull => Native is null;

    internal NativeTexture? Native { get; }
}
