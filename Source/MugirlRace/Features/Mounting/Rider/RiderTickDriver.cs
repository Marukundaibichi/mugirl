using Verse.AI;

namespace Mugirl.Features.Mounting;

// 骑手放在 CompMount 的 ThingOwner 中，原版 Thing.DoTick 只遍历 Pawn.GetChildHolders（不含 Comp），
// 所以骑手的 tracker 由载具代为推进。调用清单对照 1.7 Pawn.TickInterval（已用反编译核对），刻意省略：
// jobs（骑手不能自行执行 Job）、interactions/mutant/creepjoiner（原版只在 Spawned 时推进）与改名提示窗口。
internal static class RiderTickDriver
{
    internal static void TickInterval(Pawn? rider, int delta)
    {
        if (rider is null || rider.Destroyed)
        {
            return;
        }

        if (rider.Suspended)
        {
            rider.guilt?.GuiltTrackerTickInterval(delta);
            return;
        }

        rider.health?.HealthTickInterval(delta);
        if (rider.Dead)
        {
            return;
        }

        rider.mindState?.MindStateTickInterval(delta);
        rider.carryTracker?.CarryHandsTickInterval(delta);
        bool humanlike = PawnPredicates.IsHumanlike(rider);
        if (!rider.InCryptosleep && humanlike)
        {
            rider.infectionVectors?.InfectionTickInterval(delta);
        }

        Thing? firstParentThing = ThingOwnerUtility.GetFirstParentThing(rider);
        if (!rider.Spawned && firstParentThing is not null)
        {
            PawnUtility.GainComfortFromThingIfPossible(rider, firstParentThing, delta);
        }

        rider.needs?.NeedsTrackerTickInterval(delta);
        rider.apparel?.ApparelTrackerTickInterval(delta);
        rider.caller?.CallTrackerTickInterval(delta);
        rider.skills?.SkillsTickInterval(delta);
        rider.drafter?.DraftControllerTickInterval(delta);
        rider.relations?.RelationsTrackerTickInterval(delta);

        if (ModsConfig.RoyaltyActive && rider.psychicEntropy is not null)
        {
            rider.psychicEntropy.PsychicEntropyTrackerTickInterval(delta);
        }

        if (humanlike)
        {
            rider.guest?.GuestTrackerTickInterval(delta);
        }

        rider.ideo?.IdeoTrackerTickInterval(delta);
        rider.genes?.GeneTrackerTickInterval(delta);

        if (ModsConfig.RoyaltyActive && rider.royalty is not null)
        {
            rider.royalty.RoyaltyTrackerTickInterval(delta);
        }

        if (ModsConfig.IdeologyActive)
        {
            rider.style?.StyleTrackerTickInterval(delta);
            rider.styleObserver?.StyleObserverTickInterval(delta);
            rider.surroundings?.SurroundingsTrackerTickInterval(delta);
        }

        if (ModsConfig.BiotechActive)
        {
            rider.learning?.LearningTickInterval(delta);
            PollutionUtility.PawnPollutionTickInterval(rider, delta);
        }

        GasUtility.PawnGasEffectsTickInterval(rider, delta);
        ToxicUtility.PawnToxicTickInterval(rider, delta);
        VacuumUtility.PawnVacuumTickInterval(rider, delta);

        if (!rider.IsMutant || rider.mutant?.Def?.disableAging != true)
        {
            rider.ageTracker?.AgeTickInterval(delta);
        }

        rider.records?.RecordsTickInterval(delta);
        rider.guilt?.GuiltTrackerTickInterval(delta);

        if (!rider.Spawned)
        {
            ClearHiddenJobs(rider);
        }
    }

    // 上骑前清空骑手的工作、寻路与姿态，并取消征召，避免进入容器后残留生成态逻辑。
    internal static void PrepareForContainer(Pawn pawn)
    {
        pawn.jobs?.ClearQueuedJobs();
        if (pawn.jobs?.curJob is not null)
        {
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        pawn.pather?.StopDead();
        pawn.stances?.CancelBusyStanceHard();
        if (pawn.drafter is not null)
        {
            pawn.drafter.Drafted = false;
            pawn.drafter.FireAtWill = false;
        }

        pawn.mindState?.priorityWork.ClearPrioritizedWorkAndJobQueue();
    }

    // 骑手在容器中不能持有 Job；其他系统给它排的工作在每次推进后清掉。
    internal static void ClearHiddenJobs(Pawn rider)
    {
        rider.jobs?.ClearQueuedJobs();
        if (rider.jobs?.curJob is not null)
        {
            rider.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        rider.pather?.StopDead();
        rider.stances?.CancelBusyStanceHard();
    }
}
