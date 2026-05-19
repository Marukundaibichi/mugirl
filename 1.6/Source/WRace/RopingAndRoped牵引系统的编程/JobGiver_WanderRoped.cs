using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobGiver_WanderRoped : JobGiver_Wander
    {
        public JobGiver_WanderRoped()
        {
            this.wanderRadius = 4f;
            this.ticksBetweenWandersRange = new IntRange(500, 800);
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            return pawn.roping.RopedTo.Cell;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!pawn.roping.IsRoped || pawn.roping.IsRopedByPawn)
            {
                return null;
            }
            return base.TryGiveJob(pawn);
        }
    }
}
