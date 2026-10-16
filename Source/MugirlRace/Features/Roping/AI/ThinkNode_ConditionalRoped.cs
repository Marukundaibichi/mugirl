using Verse.AI;

namespace Mugirl.Features.Roping;

// 思维树条件：Pawn 被牵引或被拴（与原版 RimWorld.ThinkNode_ConditionalRoped 相同，XML 按 Mugirl 类名引用）。
public class ThinkNode_ConditionalRoped : ThinkNode_Conditional
{
    protected override bool Satisfied(Pawn pawn)
    {
        return pawn?.roping?.IsRoped == true;
    }
}
