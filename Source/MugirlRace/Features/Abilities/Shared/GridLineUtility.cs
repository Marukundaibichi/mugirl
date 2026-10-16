namespace Mugirl.Features.Abilities;

internal static class GridLineUtility
{
    /// <summary>
    /// 与 <see cref="GenSight.PointsOnLineOfSight(IntVec3, IntVec3)"/>（迭代器版本，含起点与终点）逐格相同的走线，
    /// 写入调用方复用的 <paramref name="outCells"/>（先清空），不分配枚举器。只填 x/z，y 为 0。
    /// </summary>
    internal static void LineCellsNonAlloc(IntVec3 start, IntVec3 end, List<IntVec3> outCells)
    {
        outCells.Clear();
        bool sideOnEqual = start.x != end.x ? start.x < end.x : start.z < end.z;
        int dx = Math.Abs(end.x - start.x);
        int dz = Math.Abs(end.z - start.z);
        int x = start.x;
        int z = start.z;
        int n = 1 + dx + dz;
        int xInc = end.x > start.x ? 1 : -1;
        int zInc = end.z > start.z ? 1 : -1;
        int error = dx - dz;
        dx *= 2;
        dz *= 2;
        while (n > 0)
        {
            outCells.Add(new IntVec3(x, 0, z));
            if (error > 0 || (error == 0 && sideOnEqual))
            {
                x += xInc;
                error -= dz;
            }
            else
            {
                z += zInc;
                error += dx;
            }

            n--;
        }
    }
}
