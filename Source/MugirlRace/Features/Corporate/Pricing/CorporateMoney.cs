namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企金额、期限与服务等级的纯计算：不访问 Def、游戏状态或引擎，可离线测试。
/// 金额一律用 decimal 精确计算；超过 <see cref="MaxTotal"/> 或不为正的合计按 0 处理，拒绝异常合同而不是截断成看似正常的金额。
/// </summary>
internal static class CorporateMoney
{
    /// <summary>单笔金额上限（1 亿白银），定价、加工报酬与兑现价值图共用。</summary>
    internal const decimal MaxTotal = 100000000m;

    /// <summary>参与定价的单件估值上限，与 <see cref="MaxTotal"/> 相同。</summary>
    internal const float MaxUnitValue = 100000000f;

    // 份额计算的系数饱和界：保证 int × 系数不超出 decimal 范围（(decimal)float 在 |f| > 7.9e28 时抛异常）。
    private const float MaxShareFactor = 1e15f;

    /// <summary>合计金额取整到整数白银；不为正或超过上限返回 0。</summary>
    internal static int Total(decimal total, bool roundUp = true)
    {
        if (total <= 0m || total > MaxTotal)
        {
            return 0;
        }

        return (int)(roundUp ? decimal.Ceiling(total) : decimal.Floor(total));
    }

    /// <summary>游戏的 float 估值换成 decimal：NaN、无穷与非正为 0，上限 1 亿。(decimal)float 保留 7 位有效数字。</summary>
    internal static decimal Value(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 0m : (decimal)UnityMathf.Min(value, MaxUnitValue);
    }

    /// <summary>单价 × 数量；单价无效（NaN、无穷、非正或超过上限）或数量不为正时返回 0。</summary>
    internal static int Price(float unitValue, int quantity, bool roundUp = true)
    {
        if (quantity <= 0 || float.IsNaN(unitValue) || float.IsInfinity(unitValue) || unitValue <= 0f || unitValue > MaxUnitValue)
        {
            return 0;
        }

        return Total((decimal)unitValue * quantity, roundUp);
    }

    /// <summary>
    /// <paramref name="amount"/> × <paramref name="factor"/> 向上取整。系数按 (decimal)float 的 7 位有效数字解释（0.8f 即 0.8），
    /// 避免 float 乘积落在整数边界附近时差 1，以及超过 2^24 的整数转 float 时丢失精度。
    /// 系数不是有限数时返回 0；结果超出 int 范围时饱和。
    /// </summary>
    internal static int CeilingShare(int amount, float factor) => Share(amount, factor, roundUp: true);

    /// <summary>与 <see cref="CeilingShare"/> 相同，向下取整。</summary>
    internal static int FloorShare(int amount, float factor) => Share(amount, factor, roundUp: false);

    private static int Share(int amount, float factor, bool roundUp)
    {
        if (float.IsNaN(factor) || float.IsInfinity(factor))
        {
            return 0;
        }

        decimal exactFactor = factor > MaxShareFactor ? (decimal)MaxShareFactor
            : factor < -MaxShareFactor ? -(decimal)MaxShareFactor
            : (decimal)factor;
        decimal value = amount * exactFactor;
        value = roundUp ? decimal.Ceiling(value) : decimal.Floor(value);
        return value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
    }

    /// <summary>累计成交额达到的服务等级：门槛按升序排列，返回已达到的门槛数。</summary>
    internal static int ServiceLevel(long turnover, ReadOnlySpan<int> thresholds)
    {
        int level = 0;
        foreach (int threshold in thresholds)
        {
            if (turnover >= threshold)
            {
                level++;
            }
        }

        return level;
    }

    internal static bool IsValidSpeed(CorporateOrderSpeed speed)
        => speed >= CorporateOrderSpeed.Standard && speed <= CorporateOrderSpeed.Priority;

    /// <summary>备货 tick：加急为一半、优先为四分之一，向上取整且至少 1 tick；天数不为正或速度无效时为 0。</summary>
    internal static int PreparationTicks(int days, CorporateOrderSpeed speed, int dayTicks)
    {
        if (!IsValidSpeed(speed) || days <= 0)
        {
            return 0;
        }

        long ticks = (long)days * dayTicks;
        int divisor = speed == CorporateOrderSpeed.Express ? 2 : speed == CorporateOrderSpeed.Priority ? 4 : 1;
        return (int)Math.Min(int.MaxValue, Math.Max(1L, (ticks + divisor - 1) / divisor));
    }

    /// <summary>加急总价：加急 ×1.5、优先 ×2，向上取整；普通价不为正或速度无效时为 0。</summary>
    internal static int ExpeditedTotal(int standardTotal, CorporateOrderSpeed speed)
    {
        if (standardTotal <= 0 || !IsValidSpeed(speed))
        {
            return 0;
        }

        decimal factor = speed == CorporateOrderSpeed.Express ? 1.5m : speed == CorporateOrderSpeed.Priority ? 2m : 1m;
        return Total(standardTotal * factor);
    }

    /// <summary>
    /// 固定单价时买得起的最大数量：<c>Total(unitPrice × q)</c> 大于 0 且不超过 <paramref name="silver"/>。
    /// 与逐个数量报价的二分搜索结果相同，但单价只需计算一次。
    /// </summary>
    internal static int AffordableQuantity(decimal unitPrice, int available, int silver)
    {
        int low = 0;
        int high = Math.Max(0, available);
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            int quote = Total(unitPrice * middle);
            if (quote > 0 && quote <= silver)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
