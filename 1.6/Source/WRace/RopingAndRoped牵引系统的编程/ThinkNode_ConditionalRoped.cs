using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class ThinkNode_ConditionalRoped : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            Pawn_RopeTracker roping = pawn.roping;
            return roping != null && roping.IsRoped;
        }
    }
}
