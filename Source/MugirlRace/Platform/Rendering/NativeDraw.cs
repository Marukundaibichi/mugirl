using WaylithGraphics = Waylith.Rendering.Graphics;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 通过 Waylith 原生提交队列绘制模组自建网格（<see cref="ModMesh"/>）。凡是 Verse 能表达的绘制都改走
/// <c>Mugirl.Rendering.VfxDraw</c>；本类只因为 Verse 无法绘制模组自建的网格而存在。
/// </summary>
/// <remarks>
/// 绘制排入当前帧的地图通道（layer 0，经 <c>LayerPassRouter</c> 进入 <c>MapMain</c>），与 Verse 的地图绘制相同。
/// 只在主线程的绘制阶段调用（<c>DrawAt</c>、<c>DynamicDrawPhaseAt(DrawPhase.Draw, …)</c>、<c>Graphic_Fleck.DrawFleck</c>、
/// <c>MapComponentUpdate</c>）。与 UnityStubs 的 <c>Graphics.DrawMesh</c> 不同，不会额外排入资源包多 Pass 材质的附加 Pass。
/// </remarks>
public static class NativeDraw
{
    private const int MapLayer = 0;

    /// <summary>
    /// 用 <paramref name="material"/>（Verse 材质，例如 <c>MaterialDef.Material</c> 或 <c>MaterialPool</c> 给出的）排队绘制 <paramref name="mesh"/>。
    /// </summary>
    /// <param name="mesh">网格。</param>
    /// <param name="matrix">用 <see cref="UnityMatrix"/> 构造的物体到世界变换（Unity 布局）。</param>
    /// <param name="material">材质；不会被修改。</param>
    /// <param name="properties">可选的逐次绘制参数。</param>
    /// <param name="renderQueue">非零时只对这次绘制覆盖材质的渲染队列。</param>
    public static void Mesh(ModMesh mesh, in UMatrix4x4 matrix, Material material, ModPropertyBlock? properties = null, int renderQueue = 0)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        WaylithGraphics.DrawMesh(mesh.Native, UnityMatrix.ToWaylithLayout(matrix), NativeBridge.Of(material), properties?.Native, MapLayer, renderQueue);
    }
}
