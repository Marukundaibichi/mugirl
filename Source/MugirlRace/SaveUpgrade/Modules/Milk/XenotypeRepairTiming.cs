namespace Mugirl.SaveUpgrade;

/// <summary>保留原先递减后返回的边界：300 次有效更新后，下一次更新执行；DLC 关闭时计时不动。</summary>
internal static class XenotypeRepairTiming
{
    internal static bool ShouldRun(ref int remainingTicks, bool pending, bool biotechActive)
    {
        if (!biotechActive || !pending)
        {
            return false;
        }

        if (remainingTicks > 0)
        {
            remainingTicks--;
            return false;
        }

        return true;
    }
}
