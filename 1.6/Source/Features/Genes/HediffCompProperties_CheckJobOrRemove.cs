using Verse;
using Verse.AI;

namespace MooGirl
{
    public class HediffCompProperties_CheckJobOrRemove : HediffCompProperties_Disappears
    {
        // 要求 Pawn 当前必须执行的 Job
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
                return (HediffCompProperties_CheckJobOrRemove)this.props;
            }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = this.parent.pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            // 如果未配置 requiredJob，则不做检查
            if (Props.requiredJob == null)
            {
                return;
            }

            Job curJob = pawn.CurJob;

            // 没有 Job 或 Job 不匹配 → 立刻移除 Hediff
            if (curJob == null || curJob.def != Props.requiredJob)
            {
                pawn.health.RemoveHediff(this.parent);
            }
        }
    }
}
