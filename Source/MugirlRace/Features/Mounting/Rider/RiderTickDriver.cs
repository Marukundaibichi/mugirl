using RimWorld.Planet;
using Verse.AI;

namespace Mugirl.Features.Mounting;

// 骑手放在 CompMount 的 ThingOwner 中，原版 Thing.DoTick 只遍历 Pawn.GetChildHolders（不含 Comp），所以骑手由载具代为推进。
// 不直接调用 rider.DoTick()：它会为非世界 Pawn 推进 jobs（每 tick JobTrackerTick，interval 中每 30 tick 跑常量思考树），
// 而骑手不能自行执行 Job，interval 部分也要按载具的 physiologicalTickInterval 批量推进。因此按未生成、非世界 Pawn 的分支
// 逐项对照 1.7 原版（已用反编译核对）：Tick 每 tick 调用，对应 Thing.DoTick 与 Pawn.Tick；TickInterval 对应 Pawn.TickInterval。
// 除 jobs 外，原版只在 Spawned 时推进的寻路、自身与原生 verb、牵引、飞行、姿态、社交、变异、蠕行者、血雨与环境音效同样不推进，
// 改名提示窗口也不在这里弹出。
internal static class RiderTickDriver
{
    // 原版装备 verb 每 tick 恰好推进一次：非 Normal 装备由 EquipmentTrackerTick 推进，Normal 装备随自身 DoTick
    // （CompEquippable.CompTick）推进。这两处包在 MountedCombatController.BeginVerbTick 中，骑乘远程 verb 因此在这里
    // 以载具为施放者推进，载具不再单独推进它。holders 是调用方提供的临时列表，作用同原版 Thing 的 tmpHolders。
    internal static void Tick(CompMount comp, Pawn rider, List<IThingHolder> holders)
    {
        if (rider.Destroyed)
        {
            return;
        }

        // ThingWithComps.Tick
        List<ThingComp> comps = rider.AllComps;
        for (int i = 0, count = comps.Count; i < count; i++)
        {
            comps[i].CompTick();
        }

        if (rider.IsHashIntervalTick(GenTicks.TickRareInterval))
        {
            rider.TickRare();
        }

        bool suspended = rider.Suspended;
        if (!suspended)
        {
            rider.health?.HealthTick();
            using (MountedCombatController.BeginVerbTick(comp))
            {
                rider.equipment?.EquipmentTrackerTick();
            }

            rider.abilities?.AbilitiesTick();
            rider.inventory?.InventoryTrackerTick();
            rider.genes?.GeneTrackerTick();
        }

        rider.Drawer.renderer.EffectersTick(suspended || rider.IsWorldPawn());

        // Thing.DoTick 随后推进 Pawn 子容器（背包、搬运、装备、服装及模组加入的容器）中的物品，服装和装备的组件由此推进。
        if (rider.Destroyed || ThingOwnerUtility.ContentsSuspended(rider))
        {
            return;
        }

        holders.Clear();
        holders.Add(rider);
        rider.GetChildHolders(holders);
        for (int i = 0; i < holders.Count; i++)
        {
            ThingOwner? things = holders[i].GetDirectlyHeldThings();
            if (things is null)
            {
                continue;
            }

            if (holders[i] is Pawn_EquipmentTracker)
            {
                using (MountedCombatController.BeginVerbTick(comp))
                {
                    things.DoTick();
                }
            }
            else
            {
                things.DoTick();
            }

            if (rider.Destroyed)
            {
                break;
            }
        }

        holders.Clear();
    }

    internal static void TickInterval(Pawn? rider, int delta)
    {
        if (rider is null || rider.Destroyed)
        {
            return;
        }

        // ThingWithComps.TickInterval：原版在 Suspended 判断之前推进组件。
        List<ThingComp> comps = rider.AllComps;
        for (int i = 0, count = comps.Count; i < count; i++)
        {
            comps[i].CompTickInterval(delta);
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
