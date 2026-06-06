using RimWorld;
using Verse;

namespace MooGirl
{
    // 继承自 HumanlikeAdult
    public class LifeStageWorker_MooGirlAdult : LifeStageWorker_HumanlikeAdult
    {
        public override void Notify_LifeStageStarted(Pawn pawn, LifeStageDef previousLifeStage)
        {
            BackstoryDef childhoodBefore = pawn.story?.Childhood;
            BackstoryDef adulthoodBefore = pawn.story?.Adulthood;

            // 先调用基类处理儿童阶段逻辑等
            base.Notify_LifeStageStarted(pawn, previousLifeStage);

            LifeStageVisualService.RestoreAdultBackstories(pawn, childhoodBefore, adulthoodBefore);
        }
    }
}
