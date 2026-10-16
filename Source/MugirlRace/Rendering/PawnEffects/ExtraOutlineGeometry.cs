namespace Mugirl.Rendering;

/// <summary>
/// 额外描边每个图层的 CPU 参数；用显式 Unity 矩阵公式保持逐分量计算顺序。
/// 纯函数，不接触游戏状态，可以离线测试。
/// </summary>
internal static class ExtraOutlineGeometry
{
    /// <summary>矩阵把某条边压到短于该长度时不画。</summary>
    internal const float MinEdgeLength = 0.0001f;

    /// <summary>
    /// 计算描边参数。<paramref name="matrix"/> 是图层的绘制矩阵；<paramref name="scaleOffset"/> 是源贴图的缩放（xy）与偏移（zw）；
    /// <paramref name="width"/> 是世界单位的描边宽度。矩阵把某条边压没时返回 false。
    /// </summary>
    internal static bool TryCompute(in UMatrix4x4 matrix, PawnLayerQuad quad, Vector4 scaleOffset, float width, out OutlineLayerParameters parameters)
    {
        // uSpan/vSpan 是网格边向量（y 归零），MultiplyVector 只用矩阵的 3×3 部分。
        Vector3 worldU = UnityMatrix.MultiplyVector(matrix, new Vector3(quad.SpanU, 0f, 0f));
        Vector3 worldV = UnityMatrix.MultiplyVector(matrix, new Vector3(0f, 0f, quad.SpanV));
        float sizeU = UnityVector.Magnitude(worldU);
        float sizeV = UnityVector.Magnitude(worldV);
        if (sizeU < MinEdgeLength || sizeV < MinEdgeLength)
        {
            parameters = default;
            return false;
        }

        Vector2 tiling = new(scaleOffset.X, scaleOffset.Y);
        Vector2 offset = new(scaleOffset.Z, scaleOffset.W);
        Vector2 a = PawnLayerQuad.UvMin * tiling + offset;
        Vector2 b = PawnLayerQuad.UvMax * tiling + offset;
        Vector2 min = UnityVector.Min(a, b);
        Vector2 max = UnityVector.Max(a, b);
        parameters = new OutlineLayerParameters(
            scaleOffset,
            new Vector4(worldU * (width / sizeU), 0f),
            new Vector4(worldV * (width / sizeV), 0f),
            new Vector4(min.X, min.Y, max.X, max.Y),
            new Vector4(width / sizeU * (max.X - min.X), width / sizeV * (max.Y - min.Y), 0f, 0f));
        return true;
    }
}
