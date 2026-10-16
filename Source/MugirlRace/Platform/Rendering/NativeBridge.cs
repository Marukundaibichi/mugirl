using NativeMaterial = Waylith.Rendering.Material;
using NativeMesh = Waylith.Graphics.Meshes.Mesh;
using NativeTexture = Waylith.Rendering.Texture;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 唯一读取 RimWorld 边界类型背后引擎对象的地方。UnityStubs 公开了这些 getter（Verse 自己也读取 <c>Texture.CoreTexture</c>）；
/// 反方向的构造不可用，所以模组自建网格只能经 <see cref="NativeDraw"/> 绘制，而不能交给 Verse。
/// </summary>
internal static class NativeBridge
{
    /// <summary>Verse 材质背后的引擎材质（首次访问时解析延迟材质）。</summary>
    public static NativeMaterial Of(Material material) => material.engineMaterial;

    /// <summary>Verse 网格背后的引擎网格。</summary>
    public static NativeMesh Of(Mesh mesh) => mesh.EngineMesh;

    /// <summary>Verse 纹理背后的引擎纹理（含渲染纹理）。</summary>
    public static NativeTexture Of(Texture texture) => texture.CoreTexture;
}
