namespace Mugirl.Features.Abilities;

// 目标 Job 中断后立即移除所属 Hediff，避免冲锋加速在任务结束后残留。
// 在 CompPostTick 中移除自身是安全的：1.7 的 HealthTick 遍历 Hediff 列表的副本。
public class HediffComp_CheckJobOrRemove : HediffComp_Disappears
{
    public new HediffCompProperties_CheckJobOrRemove? Props => props as HediffCompProperties_CheckJobOrRemove;

    public override void CompPostTick(ref float severityAdjustment)
    {
        base.CompPostTick(ref severityAdjustment);

        Pawn? pawn = parent?.pawn;
        JobDef? requiredJob = Props?.requiredJob;
        if (pawn is null || pawn.Dead || requiredJob is null)
        {
            return;
        }

        if (pawn.CurJob?.def != requiredJob)
        {
            pawn.health?.RemoveHediff(parent);
        }
    }
}
