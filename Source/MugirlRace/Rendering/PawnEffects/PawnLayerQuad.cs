namespace Mugirl.Rendering;

/// <summary>
/// 人物图层四边形在物体空间中的两条边：U 边沿 +X（镜像网格为 -X），V 边沿 +Z，UV 覆盖 [0, 1]。
/// 对 Verse <c>MeshMakerPlanes.NewPlaneMesh</c> 生成的网格（GraphicMeshSet、GridPlane、plane10 等），这与 1.6 从 <c>Mesh.uv</c>、
/// <c>Mesh.vertices</c> 推出的 uSpan = (SpanU, 0, 0)、vSpan = (0, 0, SpanV)、uvMin = (0, 0)、uvMax = (1, 1) 完全相同。
/// </summary>
/// <param name="SpanU">U 边沿 X 的长度，镜像网格为负。</param>
/// <param name="SpanV">V 边沿 Z 的长度。</param>
internal readonly record struct PawnLayerQuad(float SpanU, float SpanV)
{
    /// <summary>网格 UV 的最小角（1.6 的 uvMin）。</summary>
    internal static Vector2 UvMin => Vector2.Zero;

    /// <summary>网格 UV 的最大角（1.6 的 uvMax）。</summary>
    internal static Vector2 UvMax => Vector2.One;

    /// <summary>
    /// 由 <c>MeshPool.MeshMetaData</c> 的尺寸与镜像标志构造。宽或高不为正（含 NaN）时返回 false：1.6 同样拒绝没有面积的网格。
    /// </summary>
    internal static bool TryFromMetaData(float width, float height, bool flipped, out PawnLayerQuad quad)
    {
        if (!(width > 0f) || !(height > 0f))
        {
            quad = default;
            return false;
        }

        quad = new PawnLayerQuad(flipped ? -width : width, height);
        return true;
    }
}
