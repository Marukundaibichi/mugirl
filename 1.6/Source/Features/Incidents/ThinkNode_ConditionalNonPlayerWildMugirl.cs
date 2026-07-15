using Verse;
using Verse.AI;

namespace Mugirl
{
    public class ThinkNode_ConditionalNonPlayerWildMugirl : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return MugirlWildSlaveUtility.IsNonPlayerWildMugirl(pawn);
        }
    }
}
