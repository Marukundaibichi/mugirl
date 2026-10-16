using Verse.AI;

namespace Mugirl.Features.Roping;

// 走到雪牛娘身边给她套上牵引绳（JobDefs.xml JobDriver_RopeMugirl；类名作为 curDriver 写入存档）。
public class JobDriver_RopeMugirl : JobDriver
{
    private Pawn? Target => job.GetTarget(TargetIndex.A).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? target = Target;
        return RopingService.CanStartPawnRope(pawn, target) && pawn.Reserve(target, job, 1, -1, null, errorOnFailed, false);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        if (Target is null)
        {
            yield break;
        }

        this.FailOn(() => !RopingService.CanStartPawnRope(pawn, Target));
        yield return Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null, false);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch, false)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A)
            .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
        yield return RopingToils.RopePawn(TargetIndex.A);
    }
}
