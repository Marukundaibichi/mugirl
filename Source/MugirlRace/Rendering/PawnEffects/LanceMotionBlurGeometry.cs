namespace Mugirl.Rendering;

/// <summary>
/// 骑枪运动模糊每个图层的 CPU 参数；用显式 Unity 矩阵公式保持逐分量计算顺序。
/// 纯函数，不接触游戏状态，可以离线测试。
/// </summary>
internal static class LanceMotionBlurGeometry
{
    /// <summary>有效平面判定：网格边的平方长度必须大于该值。</summary>
    internal const float MinSpanSquared = 0.0001f;

    /// <summary>地面投影的 U/V 边张成的面积小于该值时不画。</summary>
    internal const float MinDeterminant = 0.0001f;

    /// <summary>
    /// 计算拖尾参数。<paramref name="travel"/> 是本帧世界位移；<paramref name="scaleOffset"/> 是源贴图的缩放（xy）与偏移（zw）。
    /// 网格太小或图层在地面上的投影退化时返回 false。
    /// </summary>
    internal static bool TryCompute(in UMatrix4x4 matrix, PawnLayerQuad quad, Vector4 scaleOffset, Vector3 travel, out BlurLayerParameters parameters)
    {
        Vector3 u = new(quad.SpanU, 0f, 0f);
        Vector3 v = new(0f, 0f, quad.SpanV);
        if (!(UnityVector.SqrMagnitude(u) > MinSpanSquared && UnityVector.SqrMagnitude(v) > MinSpanSquared))
        {
            parameters = default;
            return false;
        }

        // 拖尾只沿地面拉伸：边向量的高度分量归零。
        Vector3 worldU = UnityMatrix.MultiplyVector(matrix, u) with { Y = 0f };
        Vector3 worldV = UnityMatrix.MultiplyVector(matrix, v) with { Y = 0f };
        float determinant = worldU.X * worldV.Z - worldU.Z * worldV.X;
        if (MathF.Abs(determinant) < MinDeterminant)
        {
            parameters = default;
            return false;
        }

        // 把位移分解到图层的 U/V 边上（以边长为单位）。
        float alongU = (travel.X * worldV.Z - travel.Z * worldV.X) / determinant;
        float alongV = (worldU.X * travel.Z - worldU.Z * travel.X) / determinant;
        Vector2 tiling = new(scaleOffset.X, scaleOffset.Y);
        Vector2 offset = new(scaleOffset.Z, scaleOffset.W);
        Vector2 uvSpan = (PawnLayerQuad.UvMax - PawnLayerQuad.UvMin) * tiling;
        Vector2 a = PawnLayerQuad.UvMin * tiling + offset;
        Vector2 b = PawnLayerQuad.UvMax * tiling + offset;
        Vector2 uvMin = UnityVector.Min(a, b);
        Vector2 uvMax = UnityVector.Max(a, b);
        parameters = new BlurLayerParameters(
            scaleOffset,
            new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y),
            new Vector4(alongU * uvSpan.X, alongV * uvSpan.Y, 0f, 0f),
            new Vector4(travel, 0f),
            new Vector4(worldU * MathF.Abs(alongU) * 0.5f, 0f),
            new Vector4(worldV * MathF.Abs(alongV) * 0.5f, 0f));
        return true;
    }
}
