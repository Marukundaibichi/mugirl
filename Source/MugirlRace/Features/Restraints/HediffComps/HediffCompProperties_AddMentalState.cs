using Mugirl.Features.Restraints;

namespace Mugirl.Features.Restraints;

// 佩戴未破解的洗脑头盔达到指定时长后，让玩家阵营的 Pawn 进入一次精神状态。
public class HediffCompProperties_AddMentalState : HediffCompProperties
{
    public int mentalStateTriggerTicks = 600;
    public MentalStateDef? mentalStateDef;

    public HediffCompProperties_AddMentalState()
    {
        compClass = typeof(HediffComp_AddMentalState);
    }
}
