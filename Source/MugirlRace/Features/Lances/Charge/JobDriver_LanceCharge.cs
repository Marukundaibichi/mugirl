using Verse.AI;

namespace Mugirl.Features.Lances;

public sealed class JobDriver_LanceCharge : JobDriver
{
    private LocalTargetInfo ChargeTarget => job.GetTarget(TargetIndex.A);

    private LocalTargetInfo PointTargetCell => job.GetTarget(TargetIndex.B);

    private Pawn? TargetPawn => job.GetTarget(TargetIndex.A).Thing as Pawn;

    private bool IsPointCharge => job.def == LancesDefOf.Mugirl_LancePointCharge;

    private CompLanceCharge? LanceComp => pawn.equipment?.Primary?.TryGetComp<CompLanceCharge>();

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        if (!IsPointCharge)
        {
            return ChargeTarget.IsValid;
        }

        Pawn? target = TargetPawn;
        // 冲锋在起飞时锁定目标引用，不需要独占敌人；强行预约敌对 Pawn 会偶发失败并产生任务警告。
        return target is not null && !target.Destroyed && !target.Dead;
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        if (IsPointCharge)
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => TargetPawn is not { Dead: false } || LanceComp is null);
        }
        else
        {
            this.FailOn(() => !ChargeTarget.IsValid || pawn.Map is null
                || !ChargeTarget.Cell.InBounds(pawn.Map) || LanceComp is null);
        }

        Toil charge = new()
        {
            defaultCompleteMode = ToilCompleteMode.Instant,
            initAction = () =>
            {
                // PawnFlyer 起飞时挂起本任务，落地后用新的 JobDriver 恢复同一个 Job。
                // “已发射”标记因此必须保存在 Job.count 上（随 Job 存档、跨 JobDriver 保留），不能放在 JobDriver 字段里。
                if (job.count <= 0 && !TryLaunchDirectCharge())
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            },
        };
        yield return charge;
    }

    private bool TryLaunchDirectCharge()
    {
        Map? map = pawn.Map;
        CompLanceCharge? comp = LanceComp;
        ThingDef flyerDef = LancesDefOf.Mugirl_LanceChargeFlyer;
        IntVec3 targetCell = IsPointCharge && PointTargetCell.IsValid
            ? PointTargetCell.Cell
            : ChargeTarget.Cell;
        if (map is null || comp is null || flyerDef is null
            || !typeof(PawnFlyer_LanceCharge).IsAssignableFrom(flyerDef.thingClass)
            || !LanceChargeDestinationUtility.TryFindLandingCell(pawn, targetCell, IsPointCharge, out IntVec3 destination))
        {
            return false;
        }

        if (targetCell != pawn.Position)
        {
            pawn.Rotation = Rot4.FromAngleFlat((targetCell - pawn.Position).ToVector3().AngleFlat());
        }

        job.count = 1;
        if (PawnFlyer.MakeFlyer(flyerDef, pawn, destination, null, null) is not PawnFlyer_LanceCharge flyer)
        {
            job.count = 0;
            return false;
        }

        flyer.Configure(!IsPointCharge, comp.Props.steamTrail, IsPointCharge ? TargetPawn : null);
        GenSpawn.Spawn(flyer, destination, map, WipeMode.Vanish);
        SelectionUtility.ReselectIfSelectedInPlaying(
            pawn,
            playSound: false,
            forceDesignatorDeselect: false);
        return true;
    }
}
