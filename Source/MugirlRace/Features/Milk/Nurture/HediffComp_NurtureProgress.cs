namespace Mugirl.Features.Milk;

// 记录雪牛娘哺育是否已结算，避免满进度后重复发放奖励；成年（进入可繁殖阶段）前未完成的哺育随之消失。
public sealed class HediffComp_NurtureProgress : HediffComp
{
    private bool mugirlNurtureCompleted;

    // 未完成的哺育在成年后由原版 HealthTickInterval 按 ShouldRemove 移除，不在 tick 中直接删除自身。
    public override bool CompShouldRemove => base.CompShouldRemove || (!mugirlNurtureCompleted && !IsComplete && ShouldRemoveIncompleteNurture);

    private bool IsComplete => parent?.def != null && parent.Severity >= parent.def.maxSeverity;

    private bool ShouldRemoveIncompleteNurture => Pawn?.ageTracker?.CurLifeStage?.reproductive == true;

    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        base.CompPostTickInterval(ref severityAdjustment, delta);
        if (!mugirlNurtureCompleted && IsComplete)
        {
            TryComplete();
        }
    }

    public void TryComplete(Pawn? feeder = null)
    {
        if (mugirlNurtureCompleted || !IsComplete)
        {
            return;
        }

        mugirlNurtureCompleted = true;
        NurtureUtility.CompleteNurture(Pawn, parent, feeder);
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref mugirlNurtureCompleted, "mugirlNurtureCompleted", false);
    }
}
