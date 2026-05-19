using RimWorld;
using Verse;

namespace MooGirl
{
    // 继承自 HumanlikeAdult
    public class LifeStageWorker_MooGirlAdult : LifeStageWorker_HumanlikeAdult
    {
        public override void Notify_LifeStageStarted(Pawn pawn, LifeStageDef previousLifeStage)
        {
            // 先调用基类处理儿童阶段逻辑等
            base.Notify_LifeStageStarted(pawn, previousLifeStage);

            // 只针对 MooGirl
            if (pawn.RaceProps.body == MooGirl_DefOf.MooGirlBody)
            {
                // 强制成人背景
                pawn.story.Adulthood = MooGirl_DefOf.MooGirl_Colonist;
                pawn.Notify_DisabledWorkTypesChanged();
            }
        }
    }
}
