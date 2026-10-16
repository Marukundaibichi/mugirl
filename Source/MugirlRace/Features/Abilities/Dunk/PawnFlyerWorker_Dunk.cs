namespace Mugirl.Features.Abilities;

// 扣篮飞行器的进度曲线（XML workerClass）：前 36% 的时间原地蓄力，之后线性飞完全程。
public sealed class PawnFlyerWorker_Dunk : PawnFlyerWorker
{
    private const float WindupFraction = 0.36f;

    public PawnFlyerWorker_Dunk(PawnFlyerProperties properties) : base(properties)
    {
    }

    public override float AdjustedProgress(float t)
    {
        return t <= WindupFraction ? 0f : UnityMathf.InverseLerp(WindupFraction, 1f, t);
    }
}
