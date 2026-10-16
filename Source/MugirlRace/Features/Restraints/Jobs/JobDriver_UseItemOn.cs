using Verse.AI;

namespace Mugirl.Features.Restraints;

// 把物品用在其他 Pawn 或尸体身上（JobDef GiveSlaveApparelGear），包括原版对自己使用的分支。
// 目标：A = 物品，B = 目标 Pawn 或尸体；B 为空时按原版 JobDriver_UseItem 对自己使用。
public class JobDriver_UseItemOn : JobDriver_UseItem
{
    // 当前固定取物流程的结构版本；始终保存，便于后续精确辨识索引含义。
    private int itemFlowVersion = 1;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref itemFlowVersion, "itemFlowVersion", 1, forceSave: true);
    }

    private const TargetIndex ItemInd = TargetIndex.A;
    private const TargetIndex TargetInd = TargetIndex.B;
    private const int UseTicks = 60;

    private Thing? Item => job.GetTarget(ItemInd).Thing;

    protected override IEnumerable<Toil> MakeNewToils()
    {
        Thing? targetThing = job.GetTarget(TargetInd).Thing;
        if (targetThing == null)
        {
            if (Item == null)
            {
                yield break;
            }

            foreach (Toil toil in base.MakeNewToils())
            {
                yield return toil;
            }

            yield break;
        }

        // toil 数量只取决于目标是否存在（存档稳定）；物品位置与目标状态交给 FailOn。
        this.FailOn(() => Item == null || RestraintToils.PawnOf(job.GetTarget(TargetInd).Thing)?.apparel == null);
        this.FailOnDespawnedNullOrForbidden(TargetInd);
        if (RestraintToils.PawnOf(targetThing) is { Dead: false })
        {
            this.FailOnAggroMentalState(TargetInd);
        }

        yield return Toils_Reserve.Reserve(TargetInd);

        foreach (Toil toil in RestraintToils.AcquireItem(ItemInd))
        {
            yield return toil;
        }

        yield return Toils_Goto.GotoThing(TargetInd, PathEndMode.Touch);

        Toil wait = ToilMaker.MakeToil("MugirlUseItemOnWait");
        wait.initAction = () =>
        {
            if (RestraintToils.PawnOf(job.GetTarget(TargetInd).Thing) is { Dead: false } other)
            {
                PawnUtility.ForceWait(other, UseTicks);
            }
        };
        wait.defaultCompleteMode = ToilCompleteMode.Delay;
        wait.defaultDuration = UseTicks;
        yield return wait;

        Toil use = ToilMaker.MakeToil("MugirlUseItemOn");
        use.initAction = UseItemOnTarget;
        use.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return use;
    }

    // 服装类物品先放到地上再触发使用效果（CompGetSlaveApparelGear 让目标穿上它）。
    private void UseItemOnTarget()
    {
        Pawn? other = RestraintToils.PawnOf(job.GetTarget(TargetInd).Thing);
        Thing? effectiveItem = Item;
        if (other == null || effectiveItem == null)
        {
            return;
        }

        if (effectiveItem is Apparel)
        {
            effectiveItem = pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing dropped)
                ? dropped as Apparel
                : null;
        }

        effectiveItem?.TryGetComp<CompUseEffect>()?.DoEffect(other);
    }
}
