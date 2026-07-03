using Verse;
using Verse.AI;

namespace Mugirl
{
    public class ThinkNode_ConditionalRoped : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return pawn?.roping?.IsRoped == true;
        }
    }
}
