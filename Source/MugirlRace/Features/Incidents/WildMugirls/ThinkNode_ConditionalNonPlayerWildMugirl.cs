using Verse.AI;

namespace Mugirl.Features.Incidents;

// XML ThinkTree 条件（Mugirl_ThinkTreeDefs.xml）：不属于玩家的野生雪牛娘走野人行为分支。
public class ThinkNode_ConditionalNonPlayerWildMugirl : ThinkNode_Conditional
{
    protected override bool Satisfied(Pawn pawn)
    {
        return WildMugirlUtility.IsNonPlayerWildMugirl(pawn);
    }
}
