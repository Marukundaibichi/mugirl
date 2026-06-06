using Verse;

namespace MooGirl
{
    public class HediffCompProperties_MooGirlNurtureProgress : HediffCompProperties
    {
        public HediffCompProperties_MooGirlNurtureProgress()
        {
            compClass = typeof(HediffComp_MooGirlNurtureProgress);
        }
    }

    // 记录雪牛娘哺育是否已结算，避免满进度后重复发放奖励。
    public class HediffComp_MooGirlNurtureProgress : HediffComp
    {
        private bool completed;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (!completed && parent.Severity >= parent.def.maxSeverity)
            {
                TryComplete();
            }
        }

        public void TryComplete(Pawn feeder = null)
        {
            if (completed || parent.Severity < parent.def.maxSeverity)
            {
                return;
            }

            completed = true;
            MooGirlNurtureUtility.CompleteNurture(Pawn, parent, feeder);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref completed, "completed", false);
        }
    }
}
