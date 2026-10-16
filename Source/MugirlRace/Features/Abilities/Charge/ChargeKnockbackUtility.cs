namespace Mugirl.Features.Abilities;

// 冲锋击退的方向与落点。1.6 把单位方向向量截断成整数格（IntVec3(Vector3) 是 (int) 截断），
// 斜向的方向全部变成 (0,0,0)，击退与烟迹只在正东西南北方向生效；这里改为先乘距离再四舍五入。
internal static class ChargeKnockbackUtility
{
    // StaticCacheLifecycle: 击退走线的复用缓冲；只在主线程 tick 内使用，每次调用先清空，只保存坐标。
    private static readonly List<IntVec3> tmpLineCells = [];

    /// <summary>沿水平方向 <paramref name="direction"/> 前进 <paramref name="distance"/> 格的整数偏移（逐分量四舍五入）；方向为零时为零。</summary>
    internal static IntVec3 RoundedOffset(Vector3 direction, float distance)
    {
        Vector3 unit = UnityVector.Normalized(new Vector3(direction.X, 0f, direction.Z));
        return new IntVec3(UnityMathf.RoundToInt(unit.X * distance), 0, UnityMathf.RoundToInt(unit.Z * distance));
    }

    /// <summary>从 <paramref name="from"/> 指向 <paramref name="victim"/> 的方向上，把受害者击退 <paramref name="distance"/> 格的偏移。</summary>
    internal static IntVec3 KnockbackOffset(IntVec3 from, IntVec3 victim, int distance)
    {
        return RoundedOffset(new Vector3(victim.x - from.x, 0f, victim.z - from.z), distance);
    }

    /// <summary>
    /// 沿击退方向从最远处往回找第一个在地图内且可站立的格子作为落点；击退路径上的墙会被飞越，但不会落进墙里或地图外。
    /// 除受害者当前格外没有可用格时返回 false（不击退）。
    /// </summary>
    internal static bool TryFindKnockbackCell(Map map, IntVec3 from, IntVec3 victim, int distance, out IntVec3 cell)
    {
        cell = IntVec3.Invalid;
        IntVec3 destination = victim + KnockbackOffset(from, victim, distance);
        if (destination == victim)
        {
            return false;
        }

        GridLineUtility.LineCellsNonAlloc(victim, destination, tmpLineCells);
        try
        {
            for (int i = tmpLineCells.Count - 1; i > 0; i--)
            {
                IntVec3 candidate = tmpLineCells[i];
                if (candidate.InBounds(map) && candidate.Standable(map))
                {
                    cell = candidate;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            tmpLineCells.Clear();
        }
    }
}
