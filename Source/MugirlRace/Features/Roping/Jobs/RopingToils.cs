using Verse.AI;

namespace Mugirl.Features.Roping;

// 套绳 Toil：建立原版牵引关系，让对方叫一声并短暂停顿。
public static class RopingToils
{
    private const int RopeToilTicks = 30;

    public static Toil RopePawn(TargetIndex ropeeInd)
    {
        Toil toil = ToilMaker.MakeToil("RopePawn");
        toil.initAction = () =>
        {
            Pawn actor = toil.actor;
            if (actor.jobs.curJob.GetTarget(ropeeInd).Thing is not Pawn ropee)
            {
                return;
            }

            if (!RopingService.CanStartPawnRope(actor, ropee))
            {
                actor.jobs.EndCurrentJob(JobCondition.Incompletable, true, true);
                return;
            }

            ropee.roping ??= new Pawn_RopeTracker(ropee);
            actor.roping ??= new Pawn_RopeTracker(actor);
            actor.roping.RopePawn(ropee);
            ropee.caller?.DoCall();
            PawnUtility.ForceWait(ropee, RopeToilTicks, actor, false, false);
        };

        toil.defaultCompleteMode = ToilCompleteMode.Delay;
        toil.defaultDuration = RopeToilTicks;
        toil.FailOnDespawnedOrNull(ropeeInd);
        toil.PlaySustainerOrSound(() => SoundDefOf.Roping, 1f);
        return toil;
    }
}
