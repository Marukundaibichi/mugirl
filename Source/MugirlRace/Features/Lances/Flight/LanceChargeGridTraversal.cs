namespace Mugirl.Features.Lances;

/// <summary>
/// 冲锋段扫过的地格（网格 DDA）。只用 x/z 分量，不读取游戏状态，供破墙与离线测试共用。
/// </summary>
internal static class LanceChargeGridTraversal
{
    /// <summary>
    /// 按实际网格边界依次收集 <paramref name="from"/> 到 <paramref name="to"/> 经过的地格（含两端），斜线擦过的窄小格段也包含在内；
    /// 恰好经过角点时两轴同时前进。坐标含 NaN 或无穷时返回 false 且不输出地格：1.6 在此处由 Math.Sign 抛出异常，
    /// 1.7 的饱和转换还会把 NaN 换算成地图角落的地格。
    /// </summary>
    internal static bool TryCollectCrossedCells(Vector3 from, Vector3 to, List<IntVec3> outCells)
    {
        outCells.Clear();
        if (!float.IsFinite(from.X) || !float.IsFinite(from.Z) || !float.IsFinite(to.X) || !float.IsFinite(to.Z))
        {
            return false;
        }

        IntVec3 cell = ToCell(from);
        IntVec3 end = ToCell(to);
        outCells.Add(cell);
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        int stepX = Math.Sign(dx);
        int stepZ = Math.Sign(dz);
        float strideX = stepX == 0 ? float.PositiveInfinity : 1f / MathF.Abs(dx);
        float strideZ = stepZ == 0 ? float.PositiveInfinity : 1f / MathF.Abs(dz);
        float nextX = stepX == 0 ? float.PositiveInfinity
            : ((stepX > 0 ? cell.x + 1 : cell.x) - from.X) / dx;
        float nextZ = stepZ == 0 ? float.PositiveInfinity
            : ((stepZ > 0 ? cell.z + 1 : cell.z) - from.Z) / dz;

        // 正常推进每步都让曼哈顿距离减一，因此步数上限就是起止格的曼哈顿距离；
        // 它只在极小位移使步长溢出为无穷、两轴同时越过终点时生效，防止死循环。
        int remainingSteps = Math.Abs(end.x - cell.x) + Math.Abs(end.z - cell.z);
        while (cell != end && remainingSteps-- > 0)
        {
            if (cell.x == end.x)
            {
                nextX = float.PositiveInfinity;
            }

            if (cell.z == end.z)
            {
                nextZ = float.PositiveInfinity;
            }

            if (nextX < nextZ)
            {
                cell.x += stepX;
                nextX += strideX;
            }
            else if (nextZ < nextX)
            {
                cell.z += stepZ;
                nextZ += strideZ;
            }
            else
            {
                cell.x += stepX;
                cell.z += stepZ;
                nextX += strideX;
                nextZ += strideZ;
            }

            outCells.Add(cell);
        }

        return true;
    }

    /// <summary>与 Verse 的 <c>IntVec3(Vector3)</c> 相同：x、z 分别截断取整（1.7 为饱和转换），y 为 0。</summary>
    internal static IntVec3 ToCell(Vector3 position) => new((int)position.X, 0, (int)position.Z);
}
