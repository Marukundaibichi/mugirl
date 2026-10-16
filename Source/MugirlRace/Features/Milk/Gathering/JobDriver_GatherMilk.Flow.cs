using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Milk;

// 挤奶流程：预约、接近、采集与清理，当前进度和目标等待归属随存档保存。
public sealed partial class JobDriver_GatherMilk : JobDriver
{
    private const TargetIndex AnimalInd = TargetIndex.A;

    // 他人采集时目标等待 Job 的过期上限；正常情况下采集结束时由清理逻辑提前结束。
    private const int TargetWaitExpiryTicks = 15000;

    private float gatherProgress;
    private Pawn? activeGatherTarget;
    private bool gatherEffectsActive;
    private int forcedWaitJobLoadId = MilkPartnerWait.NoJob;

    private Pawn? TargetPawn => job.GetTarget(AnimalInd).Thing as Pawn;


    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref gatherProgress, "gatherProgress", 0f);
        Scribe_References.Look(ref activeGatherTarget, "activeGatherTarget");
        Scribe_Values.Look(ref gatherEffectsActive, "gatherEffectsActive", false);
        MilkPartnerWait.Expose(ref forcedWaitJobLoadId);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? targetPawn = TargetPawn;
        if (!CanDoGatherWork(pawn) || targetPawn == null || !CanGather(GetComp(targetPawn)))
        {
            return false;
        }

        return pawn.Reserve(targetPawn, job, 1, 1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(AnimalInd);
        this.FailOn(() => TargetPawn == null || !CanDoGatherWork(pawn));

        Pawn? targetPawn = TargetPawn;
        if (targetPawn == null)
        {
            yield break;
        }

        AddFinishAction(_ => CleanupGatherEffects());

        yield return Toils_Goto.GotoThing(AnimalInd, PathEndMode.Touch);
        yield return MakeGatherToil(targetPawn, controlsTarget: targetPawn != pawn);
    }

    private bool CanGather([NotNullWhen(true)] CompMilkProduction? comp)
    {
        if (comp == null || !comp.Active)
        {
            return false;
        }

        return (job.playerForced ? comp.Fullness > 0f : comp.Fullness >= comp.MilkThreshold)
            && !comp.IsManagedByMilkingDevice;
    }

    private static bool CanDoGatherWork(Pawn? gatherer)
    {
        return gatherer?.RaceProps?.Humanlike == true && !gatherer.RaceProps.IsMechanoid && gatherer.skills != null;
    }

    // 自采集只推进自己的进度和动画；他人采集（controlsTarget）还会让目标进入等待姿态，结束时只中断这里创建的等待 Job。
    private Toil MakeGatherToil(Pawn targetPawn, bool controlsTarget)
    {
        Toil wait = ToilMaker.MakeToil("MakeGatherToil");
        wait.initAction = () =>
        {
            Pawn actor = wait.actor;
            if (controlsTarget)
            {
                actor.pather.StopDead();
                Job targetWaitJob = JobMaker.MakeJob(MilkDefOf.Job_MugirlMilkingTargetWait, actor);
                targetWaitJob.expiryInterval = TargetWaitExpiryTicks;
                forcedWaitJobLoadId = MilkPartnerWait.Begin(targetPawn, targetWaitJob);
            }

            activeGatherTarget = targetPawn;
            gatherEffectsActive = true;
            StartGatherEffects(actor, targetPawn);
        };

        wait.tickAction = () =>
        {
            Pawn actor = wait.actor;
            CompMilkProduction? comp = GetComp(targetPawn);
            if (!CanDoGatherWork(actor) || !CanGather(comp))
            {
                actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                return;
            }

            TickGatherEffects(actor, targetPawn);
            actor.skills.Learn(SkillDefOf.Animals, 0.13f, false);
            gatherProgress += 1f;

            if (gatherProgress >= WorkTotal)
            {
                CompleteGather(pawn);
                actor.jobs.EndCurrentJob(JobCondition.Succeeded, true);
            }
        };

        wait.AddFinishAction(CleanupGatherEffects);
        wait.FailOnDespawnedOrNull(AnimalInd);
        wait.FailOnCannotTouch(AnimalInd, PathEndMode.Touch);
        wait.AddEndCondition(() => CanGather(GetComp(targetPawn)) ? JobCondition.Ongoing : JobCondition.Incompletable);
        wait.defaultCompleteMode = ToilCompleteMode.Never;
        wait.WithProgressBar(AnimalInd, () => gatherProgress / WorkTotal, false, -0.5f);
        wait.activeSkill = () => SkillDefOf.Animals;
        return wait;
    }

    private void CleanupGatherEffects()
    {
        if (!gatherEffectsActive)
        {
            forcedWaitJobLoadId = MilkPartnerWait.NoJob;
            return;
        }

        Pawn? cleanupTarget = activeGatherTarget;
        EndGatherEffects(pawn, cleanupTarget);
        gatherEffectsActive = false;
        activeGatherTarget = null;

        if (cleanupTarget != pawn)
        {
            MilkPartnerWait.End(cleanupTarget, MilkDefOf.Job_MugirlMilkingTargetWait, forcedWaitJobLoadId);
        }

        forcedWaitJobLoadId = MilkPartnerWait.NoJob;
    }
}
