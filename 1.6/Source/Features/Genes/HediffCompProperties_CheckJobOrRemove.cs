using Verse;
using Verse.AI;

namespace Mugirl
{
    public class HediffCompProperties_CheckJobOrRemove : HediffCompProperties_Disappears
    {
        // 维持该 Hediff 时要求 Pawn 正在执行的 Job。
        public JobDef requiredJob;

        public HediffCompProperties_CheckJobOrRemove()
        {
            this.compClass = typeof(HediffComp_CheckJobOrRemove);
        }
    }
    public class HediffComp_CheckJobOrRemove : HediffComp_Disappears
    {
        public new HediffCompProperties_CheckJobOrRemove Props
        {
            get
            {
                return props as HediffCompProperties_CheckJobOrRemove;
            }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            HediffCompProperties_CheckJobOrRemove checkProps = Props;
            // 未配置 requiredJob 时只保留 Disappears 的原有倒计时逻辑。
            if (checkProps?.requiredJob == null)
            {
                return;
            }

            Job curJob = pawn.CurJob;

            // 目标 Job 中断后立即移除 Hediff，避免状态在任务结束后残留。
            if (curJob == null || curJob.def != checkProps.requiredJob)
            {
                pawn.health?.RemoveHediff(parent);
            }
        }
    }
}
