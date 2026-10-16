using Verse.AI;

namespace Mugirl.Features.Roping;

// 思维树节点：拴在固定点的雪牛娘在拴点附近闲逛。与原版 Verse.AI.JobGiver_WanderRoped 相同，
// 但半径更小（4 格）、间隔更长（500~800 tick），且对 roping 为空的 Pawn 安全。
public class JobGiver_WanderRoped : JobGiver_Wander
{
    public JobGiver_WanderRoped()
    {
        wanderRadius = 4f;
        ticksBetweenWandersRange = new IntRange(500, 800);
    }

    protected override IntVec3 GetWanderRoot(Pawn pawn)
    {
        return pawn.roping?.RopedTo.Cell ?? IntVec3.Invalid;
    }

    protected override Job? TryGiveJob(Pawn pawn)
    {
        if (pawn?.roping?.IsRoped != true || pawn.roping.IsRopedByPawn)
        {
            return null;
        }

        return base.TryGiveJob(pawn);
    }
}
