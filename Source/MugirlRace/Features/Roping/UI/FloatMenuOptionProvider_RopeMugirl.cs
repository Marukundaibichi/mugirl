using Verse.AI;

namespace Mugirl.Features.Roping;

// 右键雪牛娘时提供“牵引”与“解除牵引”选项（原版按反射发现 FloatMenuOptionProvider 子类）。
public class FloatMenuOptionProvider_RopeMugirl : FloatMenuOptionProvider
{
    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => true;

    public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
    {
        return RopingService.IsMugirlRopee(target) && target.Spawned && !target.Dead;
    }

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn? pawn = context.FirstSelectedPawn;
        Pawn? target = clickedPawn;
        if (target is null || pawn is null || pawn == target)
        {
            yield break;
        }

        if (!pawn.Map.reachability.CanReach(pawn.Position, target, PathEndMode.Touch, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false)))
        {
            yield break;
        }

        if (!PawnPredicates.HasCapacity(pawn, PawnCapacityDefOf.Moving) || !PawnPredicates.HasCapacity(target, PawnCapacityDefOf.Moving))
        {
            yield break;
        }

        if (RopingService.CanStartPawnRope(pawn, target))
        {
            // 牵引总会成功；标签沿用 1.6 的“成功率 100%”格式。
            string label = "Mugirl.Rope.TargetWithSuccessChance".Translate(
                "Mugirl.Rope.Target".Translate(target.LabelShortCap).ToString(),
                1f.ToStringPercent(),
                "Mugirl.Rope.SuccessChance".Translate()).ToString();

            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(label, () => TakeOrderedJob(pawn, RopingDefOf.Mugirl_Rope, target), MenuOptionPriority.High, null, target, 0f, null, null, true, 0),
                pawn,
                target,
                "ReservedBy",
                null);
        }

        bool targetRoped = RopingService.IsRopedByPawn(target) || RopingService.IsRopedToSpot(target);
        bool pawnRoped = RopingService.IsRopedByPawn(pawn) || RopingService.IsRopedToSpot(pawn);
        if (targetRoped && !pawnRoped)
        {
            yield return new FloatMenuOption(
                "Mugirl.Unrope.Target".Translate(target.LabelShortCap),
                () => TakeOrderedJob(pawn, RopingDefOf.Mugirl_Unrope, target));
        }
    }

    private static void TakeOrderedJob(Pawn pawn, JobDef jobDef, Pawn target)
    {
        pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, target), JobTag.Misc);
    }
}
