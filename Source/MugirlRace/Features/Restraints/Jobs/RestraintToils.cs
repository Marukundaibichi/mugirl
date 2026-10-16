using Verse.AI;

namespace Mugirl.Features.Restraints;

// 束具相关 JobDriver 共用的 toil。
// JobDriver.ExposeData 在 PostLoadInit 阶段调用 SetupToils 重建 toil，并按存档里的 curToilIndex 继续；
// 那时物品与 Pawn 尚未生成，Spawned、背包、可达性等判断都不可靠。所以 toil 数量只能取决于存档稳定的数据
// （例如目标是否为施术者本人），运行时的分支一律交给跳转 toil 与 FailOn。
internal static class RestraintToils
{
    // 取得要使用的物品，固定产出 5 个 toil：物品已在施术者背包里就直接使用；
    // 否则预留、走过去并拿到手上，之后 itemInd 指向手上的那一件（从一叠中拿起时是拆分出的新物品）。
    internal static IEnumerable<Toil> AcquireItem(TargetIndex itemInd)
    {
        Toil acquired = Toils_General.Label();

        Toil skipIfInInventory = ToilMaker.MakeToil("MugirlSkipIfItemInInventory");
        skipIfInInventory.initAction = () =>
        {
            Pawn actor = skipIfInInventory.actor;
            Thing? item = actor.CurJob.GetTarget(itemInd).Thing;
            if (item != null && actor.inventory != null && actor.inventory.Contains(item))
            {
                actor.jobs.curDriver.JumpToToil(acquired);
            }
        };
        yield return skipIfInInventory;

        yield return Toils_Reserve.Reserve(itemInd);
        yield return Toils_Goto.GotoThing(itemInd, PathEndMode.ClosestTouch).FailOnForbidden(itemInd);

        Toil pickUp = ToilMaker.MakeToil("MugirlPickUpItem");
        pickUp.initAction = () =>
        {
            Pawn actor = pickUp.actor;
            Job job = actor.CurJob;
            Thing? item = job.GetTarget(itemInd).Thing;
            if (item == null || item.Destroyed || actor.carryTracker.TryStartCarry(item, 1) <= 0)
            {
                actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (actor.carryTracker.CarriedThing is { } carried)
            {
                job.SetTarget(itemInd, carried);
            }
        };
        pickUp.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return pickUp;

        yield return acquired;
    }

    // 施工等待。对其他 Pawn 用 WaitWith（对方原地等待，显示进度条）；目标就是施术者时改用普通 Wait，
    // 因为 1.7 的 Toils_General.WaitWith 在目标等于 toil.actor 时会记录警告。
    internal static Toil WaitOn(TargetIndex targetInd, int ticks, bool targetIsActor)
    {
        if (targetIsActor)
        {
            return Toils_General.Wait(ticks).WithProgressBarToilDelay(targetInd);
        }

        return Toils_General.WaitWith(targetInd, ticks, useProgressBar: true);
    }

    // 目标为 Pawn 时返回本身，为尸体时返回其中的 Pawn。
    internal static Pawn? PawnOf(Thing? target)
    {
        return target switch
        {
            Pawn pawn => pawn,
            Corpse corpse => corpse.InnerPawn,
            _ => null,
        };
    }

    // 脱下的束具优先落在目标所在格，其次佩戴者，最后施术者。
    internal static IntVec3 DropCell(Thing? target, Pawn? wearer, Pawn actor)
    {
        if (target != null && target.PositionHeld.IsValid)
        {
            return target.PositionHeld;
        }

        if (wearer != null && wearer.PositionHeld.IsValid)
        {
            return wearer.PositionHeld;
        }

        return actor.PositionHeld;
    }
}
