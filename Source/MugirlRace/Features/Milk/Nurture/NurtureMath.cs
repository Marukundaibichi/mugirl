namespace Mugirl.Features.Milk;

// 哺育加成的纯计算，不访问游戏状态。
internal static class NurtureMath
{
    // 哺育余韵（完成哺育后）的成长点倍率。
    internal const float AfterglowGrowthFactor = 2.5f;

    // 受雪牛娘哺育的 Pawn 每日满速经验额度额外增加原版额度的 1.5 倍（原版 4000，合计 10000）。
    internal const float NurturedExtraFullRateXp = SkillRecord.MaxFullRateXpPerDay * 1.5f;

    // 哺育进度（Severity）对应的成长点倍率，与哺育 Hediff 三个阶段的学习倍率一致。
    internal static float GrowthFactorForSeverity(float severity)
    {
        if (severity >= 0.70f)
        {
            return 2.5f;
        }

        if (severity >= 0.40f)
        {
            return 2f;
        }

        return 1.5f;
    }

    // 原版已判为经验饱和（xpSinceMidnight > 4000）、但仍在受哺育者放宽后的额度内时为 true。
    internal static bool ShouldBypassSaturation(float xpSinceMidnight)
    {
        return xpSinceMidnight > SkillRecord.MaxFullRateXpPerDay
            && xpSinceMidnight <= SkillRecord.MaxFullRateXpPerDay + NurturedExtraFullRateXp;
    }
}
