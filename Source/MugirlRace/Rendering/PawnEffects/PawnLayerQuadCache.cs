using System.Runtime.CompilerServices;

namespace Mugirl.Rendering;

/// <summary>
/// 按网格缓存 <see cref="PawnLayerQuad"/>。数据来自 Verse <c>MeshPool.GetMetaData</c>，不读取 <c>Mesh.uv/vertices/bounds/isReadable</c>
/// （兼容层逻辑，且 1.7 的池网格不一定保留 CPU 副本）。
/// </summary>
/// <remarks>
/// 只支持带 MeshPool 元数据的网格，即 <c>MeshMakerPlanes</c> 生成的全部人物网格（HAR 1.6 也使用原版网格集）。1.6 还接受其他轴对齐的
/// 四顶点网格；自建网格的第三方模组在 1.7 中不会有描边与拖尾，开发者模式下每个这样的网格记录一次。
/// </remarks>
internal static class PawnLayerQuadCache
{
    // StaticCacheLifecycle: 以网格为弱键，随原版网格释放；只保存两个浮点数，不随存档变化，不需要清理。
    private static readonly ConditionalWeakTable<Mesh, Entry> entries = new();

    private static readonly ConditionalWeakTable<Mesh, Entry>.CreateValueCallback createEntry = Create;

    /// <summary>取 <paramref name="mesh"/> 的四边形；不是 MeshPool 平面网格时返回 false。线程安全。</summary>
    internal static bool TryGet(Mesh mesh, out PawnLayerQuad quad)
    {
        Entry entry = entries.GetValue(mesh, createEntry);
        quad = entry.Quad;
        return entry.Supported;
    }

    private static Entry Create(Mesh mesh)
    {
        MeshPool.MeshMetaData metaData;
        try
        {
            metaData = MeshPool.GetMetaData(mesh);
        }
        catch (KeyNotFoundException)
        {
            // 不是 Verse 平面网格。结果被缓存，每个网格至多抛出一次。
            MugirlLog.DevMessage(() => $"Pawn effect skipped mesh '{mesh.name}': it has no MeshPool metadata (not built by MeshMakerPlanes).");
            return new Entry(default, Supported: false);
        }

        bool supported = PawnLayerQuad.TryFromMetaData(metaData.size.x, metaData.size.y, metaData.flipped, out PawnLayerQuad quad);
        return new Entry(quad, supported);
    }

    private sealed record Entry(PawnLayerQuad Quad, bool Supported);
}
