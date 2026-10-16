namespace Mugirl.Features.Corporate;

// 巨企敌对派系的战斗编组按袭击点数分档（纯计算）。各档的上限与 1.7/Defs 中该派系 pawnGroupMakers 的 maxTotalPoints 一致，
// 用精确比较识别是哪一档（XML 中是整数字面量，解析为 float 后精确相等）。
internal static class CorporateRaidTierRules
{
    internal const float LowMax = 2000f;
    internal const float LowerMiddleMax = 4000f;
    internal const float HighMax = 6500f;
    internal const float TopMax = 10000f;

    /// <summary>
    /// maxTotalPoints 是某一档的上限时返回 true，并给出该编组能否用于这次袭击：点数须落在 (上一档上限, 本档上限]，最低档从 0 起含 0。
    /// 其他编组返回 false，保持原版结果。
    /// </summary>
    internal static bool TryGetTierAllowance(float maxTotalPoints, float points, out bool allowed)
    {
        if (maxTotalPoints == LowMax)
        {
            allowed = points >= 0f && points <= LowMax;
            return true;
        }

        if (maxTotalPoints == LowerMiddleMax)
        {
            allowed = points > LowMax && points <= LowerMiddleMax;
            return true;
        }

        if (maxTotalPoints == HighMax)
        {
            allowed = points > LowerMiddleMax && points <= HighMax;
            return true;
        }

        if (maxTotalPoints == TopMax)
        {
            allowed = points > HighMax && points <= TopMax;
            return true;
        }

        allowed = false;
        return false;
    }
}
