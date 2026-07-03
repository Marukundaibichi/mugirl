using Verse;

namespace Mugirl
{
    public class HediffCompProperties_MugirlNurtureProgress : HediffCompProperties
    {
        public HediffCompProperties_MugirlNurtureProgress()
        {
            compClass = typeof(HediffComp_MugirlNurtureProgress);
        }
    }

    // 记录雪牛娘哺育是否已结算，避免满进度后重复发放奖励。
    public class HediffComp_MugirlNurtureProgress : HediffComp
    {
        private bool completed;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (!completed && IsComplete)
            {
                TryComplete();
                return;
            }

            if (!completed && ShouldRemoveIncompleteNurture)
            {
                Pawn?.health?.RemoveHediff(parent);
            }
        }

        public void TryComplete(Pawn feeder = null)
        {
            if (completed || !IsComplete)
            {
                return;
            }

            completed = true;
            MugirlNurtureUtility.CompleteNurture(Pawn, parent, feeder);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref completed, "completed", false);
        }

        private bool IsComplete => parent?.def != null && parent.Severity >= parent.def.maxSeverity;

        private bool ShouldRemoveIncompleteNurture => Pawn?.ageTracker?.CurLifeStage?.reproductive == true;
    }
}
