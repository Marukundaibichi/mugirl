using Verse.AI;

namespace Mugirl.Features.Roping;

// 思维树节点（Mugirl_ThinkTreeDefs.xml）：被 Pawn 牵引的雪牛娘跟随牵引者，并取消征召。
public class JobGiver_MugirlFollowRoper : ThinkNode_JobGiver
{
    protected override Job? TryGiveJob(Pawn pawn)
    {
        if (pawn is null || pawn.Dead || pawn.Destroyed)
        {
            return null;
        }

        Pawn_RopeTracker? roping = pawn.roping;
        if (roping is null || !roping.IsRoped)
        {
            return null;
        }

        Pawn? roper = roping.RopedByPawn;
        if (roper is null || roper.Dead || !roper.Spawned)
        {
            return null;
        }

        if (!pawn.CanReach(roper, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn))
        {
            return null;
        }

        if (pawn.drafter is not null)
        {
            pawn.drafter.Drafted = false;
        }

        return JobMaker.MakeJob(RopingDefOf.Mugirl_FollowRoper, roper);
    }
}
