using Verse.AI;

namespace Mugirl.Features.Abilities;

// 扣篮 Job（XML driverClass）：走到受害者 → 取头生成扣篮道具 → 冲刺到助跑格 → 起跳扣篮 → 等待结算。
public class JobDriver_Dunk : JobDriver
{
    private DunkController? prop;

    private Thing? PickupTarget => job.GetTarget(TargetIndex.A).Thing;
    private IntVec3 ImpactCell => job.GetTarget(TargetIndex.B).Cell;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Thing? target = PickupTarget;
        return target is not null
            && target.Spawned
            && pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        AddFinishAction(_ =>
        {
            if (prop is not null && !prop.Destroyed && !prop.SequenceStarted)
            {
                prop.TryDropSafely();
            }
        });

        Toil goToObject = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        goToObject.FailOnDestroyedOrNull(TargetIndex.A);
        yield return goToObject;

        Toil pickUp = new()
        {
            defaultCompleteMode = ToilCompleteMode.Instant,
            initAction = () =>
            {
                Pawn? victim = PickupTarget as Pawn;
                if (!DunkUtility.CanDunk(pawn, victim, out _, out string? reason)
                    || !DunkController.TryFindLandingCell(pawn, ImpactCell, out _)
                    || !DunkController.TryCreate(pawn, victim, job.ability, out prop))
                {
                    if (!reason.NullOrEmpty())
                    {
                        Messages.Message(reason, (Thing?)victim ?? pawn, MessageTypeDefOf.RejectInput, historical: false);
                    }

                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.ability?.StartCooldown(job.ability.def.cooldownTicksRange.RandomInRange);
                IntVec3 approach = FindApproachCell(pawn, ImpactCell);
                job.SetTarget(TargetIndex.C, approach);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
            }
        };
        yield return pickUp;

        Toil runUp = Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
        runUp.tickAction = () => prop?.NotifyRunning();
        runUp.AddFailCondition(() => prop is null || prop.Destroyed || !prop.HasPayload);
        yield return runUp;

        Toil beginDunk = new()
        {
            defaultCompleteMode = ToilCompleteMode.Instant,
            initAction = () =>
            {
                if (prop is null || prop.Destroyed || !prop.BeginDunk(ImpactCell))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            }
        };
        yield return beginDunk;

        Toil waitForFinish = new()
        {
            defaultCompleteMode = ToilCompleteMode.Never,
            tickAction = () =>
            {
                if (prop is null || prop.Destroyed || prop.Completed)
                {
                    ReadyForNextToil();
                }
            }
        };
        yield return waitForFinish;
    }

    // 助跑格：篮筐朝施术者方向 3 格（含对角）；不可达时在附近 3 格内随机，再不行就原地起跳。
    private static IntVec3 FindApproachCell(Pawn pawn, IntVec3 impactCell)
    {
        Map map = pawn.Map;
        IntVec3 direction = pawn.Position - impactCell;
        IntVec3 preferred = impactCell + new IntVec3(Math.Sign(direction.x) * 3, 0, Math.Sign(direction.z) * 3);

        if (IsValidApproach(pawn, preferred, map))
        {
            return preferred;
        }

        if (CellFinder.TryFindRandomCellNear(preferred.ClampInsideMap(map), map, 3, cell => IsValidApproach(pawn, cell, map), out IntVec3 result))
        {
            return result;
        }

        return pawn.Position;
    }

    private static bool IsValidApproach(Pawn pawn, IntVec3 cell, Map map)
    {
        return cell.IsValid
            && cell.InBounds(map)
            && cell.Standable(map)
            && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref prop, "dunkProp");
    }
}
