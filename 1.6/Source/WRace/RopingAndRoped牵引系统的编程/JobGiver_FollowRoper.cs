using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobGiver_FollowRoper : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return null;
            }

            // 防止 roping 为 null
            Pawn_RopeTracker roping = pawn.roping;
            if (roping == null || !roping.IsRoped)
            {
                return null;
            }

            Pawn roper = roping.RopedByPawn;
            if (roper == null || roper.Dead || !roper.Spawned)
            {
                return null;
            }

            if (!pawn.CanReach(roper, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn))
            {
                return null;
            }

            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = false;
            }

            return JobMaker.MakeJob(MooGirl_DefOf.Job_FollowRoper, roper);
        }
    }

}
