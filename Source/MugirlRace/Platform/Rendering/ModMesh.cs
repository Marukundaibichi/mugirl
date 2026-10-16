using System.Diagnostics.CodeAnalysis;
using Waylith.Graphics.Meshes;
using NativeColor32 = Waylith.Math.Color32;
using NativeMesh = Waylith.Graphics.Meshes.Mesh;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 模组自建的网格（丝带、碎片、自定义轮廓等）。Verse 只能绘制 <see cref="Mesh"/>，而创建它要调用 UnityStubs 的 setter
/// （每个 setter 都整份重建 CPU 数据），所以模组网格改用原生引擎网格，并通过 <see cref="NativeDraw"/> 绘制。
/// </summary>
/// <remarks>
/// 每个网格只在主线程惰性构建一次并长期持有；模组网格是静态几何体。坐标沿用 RimWorld 地图约定：X 向东、Y 向上（高度）、Z 向北；
/// 平放的四边形位于 XZ 平面。替代 <c>new Mesh()</c> + <c>vertices/triangles/uv</c> setter，以及 <c>Object.Instantiate(mesh)</c> 复制网格。
/// </remarks>
public sealed class ModMesh : IDisposable
{
    private ModMesh(NativeMesh mesh) => Native = mesh;

    internal NativeMesh Native { get; }

    /// <summary>创建只有一个子网格的网格。</summary>
    /// <param name="debugName">引擎诊断中显示的名字，以 <c>Mugirl_</c> 开头。</param>
    /// <param name="vertices">顶点位置。</param>
    /// <param name="triangles">三角形索引，俯视（从 +Y 看）为顺时针，与 <c>MeshPool</c> 的平面相同。</param>
    /// <param name="uv0">第一组纹理坐标（着色器 <c>TexCoord0</c>）。</param>
    /// <param name="uv1">第二组纹理坐标（着色器 <c>TexCoord1</c>）。</param>
    /// <param name="colors">可选的逐顶点颜色。</param>
    /// <param name="bounds">
    /// 可选的包围盒覆盖。透明排序使用包围盒中心；只有顶点着色器把几何体移到网格范围之外很远时才需要扩大。
    /// </param>
    public static ModMesh Create(
        string debugName,
        Vector3[] vertices,
        int[] triangles,
        Vector2[]? uv0 = null,
        Vector2[]? uv1 = null,
        Color32[]? colors = null,
        (Vector3 Min, Vector3 Max)? bounds = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(debugName);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Length % 3 != 0)
        {
            throw new ArgumentException("Triangle index count must be a multiple of 3.", nameof(triangles));
        }

        NativeColor32[]? nativeColors = null;
        if (colors is not null)
        {
            nativeColors = new NativeColor32[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                Color32 c = colors[i];
                nativeColors[i] = new NativeColor32(c.r, c.g, c.b, c.a);
            }
        }

        var mesh = new NativeMesh(MeshArrays.Build(vertices, triangles, normals: null, nativeColors, uv0, uv1))
        {
            DebugName = debugName,
        };

        if (bounds is { } b)
        {
            mesh.Bounds = new MeshBounds(b.Min.X, b.Min.Y, b.Min.Z, b.Max.X, b.Max.Y, b.Max.Z);
        }

        return new ModMesh(mesh);
    }

    /// <summary>释放 GPU 资源。只用于会重建的网格；静态网格在整个游戏会话中保留。替代 <c>Object.Destroy(mesh)</c>。</summary>
    [SuppressMessage("Usage", "CA1816", Justification = "没有终结器；GPU 资源的生命周期由引擎管理。")]
    public void Dispose() => Native.Dispose();
}
