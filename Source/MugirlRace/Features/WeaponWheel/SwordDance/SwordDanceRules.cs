namespace Mugirl.Features.WeaponWheel;

// 连携剑舞的纯规则：触发保底与“绕到目标身后”的落点评分。不访问地图，由组件提供格子可用性。
internal static class SwordDanceRules
{
    // 连续失败该次数后下一次必定触发（失败计数存档，读档时钳制到 [0, 次数 - 1]）。
    internal const int GuaranteeFailures = 5;

    // 掷骰结果与失败计数的推进：成功或保底触发时计数清零。
    internal static bool ResolveChance(bool rolledSuccess, ref int failureStreak)
    {
        if (rolledSuccess)
        {
            failureStreak = 0;
            return true;
        }

        failureStreak++;
        if (failureStreak >= GuaranteeFailures)
        {
            failureStreak = 0;
            return true;
        }
        return false;
    }

    // 从攻击者指向目标的方向（各轴取符号），即“穿过目标后继续前进”的一格偏移；重合时为零。
    internal static IntVec3 BehindDirection(IntVec3 attacker, IntVec3 target)
    {
        IntVec3 relative = target - attacker;
        return new IntVec3(Math.Sign(relative.x), 0, Math.Sign(relative.z));
    }

    // 目标邻格的评分：越靠“身后”越好，同等时离正后方越近越好；不在身后半平面（含侧面）时返回 null。
    internal static int? ScoreBehindCell(IntVec3 target, IntVec3 behindDirection, IntVec3 cell)
    {
        IntVec3 offset = cell - target;
        int behindScore = offset.x * behindDirection.x + offset.z * behindDirection.z;
        if (behindScore <= 0)
        {
            return null;
        }

        IntVec3 exactBehind = target + behindDirection;
        return behindScore * 10 - cell.DistanceToSquared(exactBehind);
    }
}
