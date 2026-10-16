namespace Mugirl.Features.Lances;

/// <summary>贴地直线冲锋：进度线性、无抛物线高度。</summary>
public sealed class PawnFlyerWorker_LanceCharge : PawnFlyerWorker
{
    public PawnFlyerWorker_LanceCharge(PawnFlyerProperties properties)
        : base(properties)
    {
    }

    public override float AdjustedProgress(float t)
    {
        return UnityMathf.Clamp01(t);
    }

    public override float GetHeight(float t)
    {
        return 0f;
    }
}
