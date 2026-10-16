namespace Mugirl.Features.Restraints;

// 佩戴已破解的洗脑头盔达到指定时长后，给玩家的殖民者添加一次记忆。
public class HediffCompProperties_AddThought : HediffCompProperties
{
    // 佩戴后等待多少 tick 再添加记忆。
    public int triggerTicks = 600;

    // 达成条件后写入的 ThoughtDef。
    public ThoughtDef? thoughtDef;

    public HediffCompProperties_AddThought()
    {
        compClass = typeof(HediffComp_AddThought);
    }
}
