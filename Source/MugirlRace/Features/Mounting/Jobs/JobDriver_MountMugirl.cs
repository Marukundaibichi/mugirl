using Verse.AI;

namespace Mugirl.Features.Mounting;

// 走到雪牛娘身边，短暂停顿后上骑。类名由 JobDefs_Mounting.xml 引用，并作为 curDriver 写入存档。
public class JobDriver_MountMugirl : JobDriver
{
    private const TargetIndex MooInd = TargetIndex.A;
    private const int MountDelayTicks = 45;

    private Pawn? Moo => job.GetTarget(MooInd).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? moo = Moo;
        CompMount? comp = MountUtility.GetMountComp(moo);
        return comp is not null
            && comp.CanMount(pawn, out _)
            && pawn.Reserve(moo, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(MooInd);
        this.FailOn(() => MountUtility.GetMountComp(Moo) is not { } comp || !comp.CanMount(pawn, out _));

        yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);
        yield return Toils_General.Wait(MountDelayTicks, MooInd).WithProgressBarToilDelay(MooInd);
        yield return Toils_General.Do(() =>
        {
            CompMount? comp = MountUtility.GetMountComp(Moo);
            if (comp is null || !comp.TryMount(pawn))
            {
                EndJobWith(JobCondition.Incompletable);
            }
        });
    }
}
