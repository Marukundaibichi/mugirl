using Verse.AI;

namespace Mugirl.Features.Roping;

// 走到被牵引或被拴的雪牛娘身边解开她的绳子（JobDefs.xml JobDriver_UnropeMugirl；类名作为 curDriver 写入存档）。
public class JobDriver_UnropeMugirl : JobDriver
{
    private const int RemoveRopeTicks = 50;

    private Pawn? Target => job.GetTarget(TargetIndex.A).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? target = Target;
        return target is not null && pawn.Reserve(target, job, 1, -1, null, errorOnFailed, false);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        if (Target is null)
        {
            yield break;
        }

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A)
            .FailOn(() => !RopingService.HasAnyRope(Target) || !pawn.CanReach(Target, PathEndMode.Touch, Danger.Deadly));

        yield return Toils_General.Wait(RemoveRopeTicks)
            .WithProgressBarToilDelay(TargetIndex.A)
            .FailOn(() => !RopingService.HasAnyRope(Target));

        yield return Toils_General.Do(() =>
        {
            Pawn? target = Target;
            if (!RopingService.HasAnyRope(target))
            {
                return;
            }

            // 只解开这一只雪牛娘：BreakAllRopes 会从牵引者处放下她的绳子、解开她自己牵着的绳子与固定拴点。
            // 1.6 还对牵引者调用 BreakAllRopes，连带放开了同一牵引者牵着的其他动物和牵引者自己的绳子。
            RopingService.BreakAllRopesAndNotify(target);
            RopingService.ClearPendingSpotRope(target);

            if (target.jobs is not null)
            {
                target.jobs.ClearQueuedJobs();
                target.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        });
    }
}
