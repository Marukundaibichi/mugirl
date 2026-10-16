using Mugirl.Features.Milk;

namespace Mugirl.SaveUpgrade;

/// <summary>把旧绝对结算时刻留到原本首次有效更新的时点；暂停、停用和再次存读档都不提前消费它。</summary>
internal sealed class LegacyMilkAccrualContinuation : IResourceAccrualContinuation
{
    internal const int NoTick = -99999;
    private int anchorTick = NoTick;

    public LegacyMilkAccrualContinuation()
    {
    }

    internal LegacyMilkAccrualContinuation(int anchorTick) => this.anchorTick = anchorTick;

    public void Apply(CompMilkProduction source, int currentTick, int updateInterval)
    {
        if (anchorTick != NoTick)
        {
            source.RestoreAccrualWindow(PendingTicks(currentTick, anchorTick, updateInterval));
        }
    }

    public void ExposeData() => Scribe_Values.Look(ref anchorTick, "anchorTick", NoTick, forceSave: true);

    internal static int PendingTicks(int currentTick, int anchorTick, int updateInterval)
        => anchorTick < 0 ? 0 : UnityMathf.Clamp(currentTick - anchorTick, 0, Math.Max(1, updateInterval));
}
