using RimWorld;
using Verse;

namespace MooGirl
{
    // 成年阶段沿用原版人类逻辑，但在基类重算后恢复雪牛娘专用背景。
    public class LifeStageWorker_MooGirlAdult : LifeStageWorker_HumanlikeAdult
    {
        public override void Notify_LifeStageStarted(Pawn pawn, LifeStageDef previousLifeStage)
        {
            BackstoryDef childhoodBefore = pawn.story?.Childhood;
            BackstoryDef adulthoodBefore = pawn.story?.Adulthood;

            base.Notify_LifeStageStarted(pawn, previousLifeStage);

            LifeStageVisualService.RestoreAdultBackstories(pawn, childhoodBefore, adulthoodBefore);
        }
    }
}
