using Verse.AI;

namespace Mugirl.Features.Mounting;

// 让雪牛娘背上的骑手下来；执行者可以是载具自己。类名由 JobDefs_Mounting.xml 引用，并作为 curDriver 写入存档。
public class JobDriver_DismountMugirl : JobDriver
{
    private const TargetIndex MooInd = TargetIndex.A;

    private Pawn? Moo => job.GetTarget(MooInd).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? moo = Moo;
        if (MountUtility.GetMountComp(moo)?.HasMountedPawn != true)
        {
            return false;
        }

        return pawn == moo || pawn.Reserve(moo, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(MooInd);
        this.FailOn(() => MountUtility.GetMountComp(Moo)?.HasMountedPawn != true);
        if (pawn != Moo)
        {
            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);
        }

        yield return Toils_General.Do(() =>
        {
            CompMount? comp = MountUtility.GetMountComp(Moo);
            if (comp is null || !comp.TryDismount())
            {
                EndJobWith(JobCondition.Incompletable);
            }
        });
    }
}
