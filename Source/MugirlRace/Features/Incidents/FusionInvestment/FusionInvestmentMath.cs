namespace Mugirl.Features.Incidents;

// 牛聚变投资的档位与奖励数量（纯计算）。
internal static class FusionInvestmentMath
{
    /// <summary>三个投资档位的白银数额，下标 + 1 即档位。</summary>
    internal static ReadOnlySpan<int> InvestmentAmounts => [1000, 3000, 5000];

    /// <summary>各档位的奶制食品总数。</summary>
    internal static ReadOnlySpan<int> MilkFoodRewardCounts => [30, 90, 180];

    /// <summary>不低于该数额的最高档位；不足最低档按第 1 档。</summary>
    internal static int TierForAmount(int amount)
    {
        ReadOnlySpan<int> amounts = InvestmentAmounts;
        for (int i = amounts.Length - 1; i >= 0; i--)
        {
            if (amount >= amounts[i])
            {
                return i + 1;
            }
        }

        return 1;
    }

    /// <summary>档位越界时取最近的档位（Unity 的 Clamp 语义，下界大于上界也不抛异常）。</summary>
    internal static int MilkFoodRewardCountForTier(int tier)
    {
        ReadOnlySpan<int> counts = MilkFoodRewardCounts;
        int index = UnityMathf.Clamp(tier, 1, counts.Length) - 1;
        return counts[index];
    }
}
