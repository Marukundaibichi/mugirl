using Verse.AI;

namespace Mugirl.Features.Restraints;

// 用钥匙给自己、其他 Pawn 或尸体上的束具开锁一次（JobDef UnlockSlaveApparel）。
// 目标：A = 钥匙，B = 佩戴者（Pawn 或尸体；对自己使用时是施术者本人），C = 束具。
public class JobDriver_UnlockRestraint : JobDriver
{
    // 当前固定取物流程的结构版本；始终保存，便于后续精确辨识索引含义。
    private int itemFlowVersion = 1;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref itemFlowVersion, "itemFlowVersion", 1, forceSave: true);
    }

    private const TargetIndex KeyInd = TargetIndex.A;
    private const TargetIndex TargetInd = TargetIndex.B;
    private const TargetIndex ApparelInd = TargetIndex.C;
    private const int DefaultUnlockTicks = 100;

    private Thing? Key => job.GetTarget(KeyInd).Thing;

    private Thing? TargetThing => job.GetTarget(TargetInd).Thing;

    private Pawn? TargetPawn => RestraintToils.PawnOf(TargetThing);

    private RestraintApparel? TargetApparel => job.GetTarget(ApparelInd).Thing as RestraintApparel;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Thing? key = Key;
        Thing? targetThing = TargetThing;
        RestraintApparel? apparel = TargetApparel;
        if (key == null || targetThing == null || apparel == null)
        {
            return false;
        }

        bool reserveTarget = targetThing == pawn || pawn.Reserve(targetThing, job, 1, -1, null, errorOnFailed);
        bool reserveApparel = pawn.Reserve(apparel, job, 1, -1, null, errorOnFailed);
        bool keyAlreadyHeld = pawn.inventory != null && pawn.inventory.Contains(key);
        bool reserveKey = keyAlreadyHeld || pawn.Reserve(key, job, 1, -1, null, errorOnFailed);

        return reserveTarget && reserveApparel && reserveKey;
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        // toil 数量只取决于“是否对自己开锁”；物品位置和生成状态由 FailOn 在运行时判断。
        bool unlockSelf = job.GetTarget(TargetInd).Thing == pawn;
        int unlockTicks = TargetApparel?.Restraint?.unlockTicks ?? DefaultUnlockTicks;

        this.FailOn(() => !CanStillUnlock());

        if (!unlockSelf)
        {
            yield return Toils_Reserve.Reserve(TargetInd);
        }

        yield return Toils_Reserve.Reserve(ApparelInd);

        foreach (Toil toil in RestraintToils.AcquireItem(KeyInd))
        {
            yield return toil;
        }

        if (!unlockSelf)
        {
            yield return Toils_Goto.GotoThing(TargetInd, PathEndMode.Touch);
        }

        yield return RestraintToils.WaitOn(TargetInd, unlockTicks, unlockSelf);

        Toil finish = ToilMaker.MakeToil("MugirlUnlockSlaveApparel");
        finish.initAction = UnlockOnce;
        finish.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return finish;
    }

    // 钥匙仍有效、束具仍穿在目标身上且仍锁定。
    private bool CanStillUnlock()
    {
        Thing? key = Key;
        RestraintApparel? apparel = TargetApparel;
        return key != null
            && !key.Destroyed
            && apparel != null
            && apparel.SatisfiesKey(key)
            && TargetPawn.IsWornLockedRestraint(apparel);
    }

    private void UnlockOnce()
    {
        Pawn? wearer = TargetPawn;
        RestraintApparel? apparel = TargetApparel;
        if (wearer?.apparel == null || apparel == null || !wearer.apparel.WornApparel.Contains(apparel))
        {
            MugirlLog.WarningOnce("UnlockSlaveApparel.TargetMissing", () => "Mugirl.Restraints.UnlockTargetMissingLog".Translate().ToString());
            return;
        }

        switch (apparel.UseKey(wearer, RestraintToils.DropCell(TargetThing, wearer, pawn)))
        {
            case KeyUnlockResult.DropFailed:
                // 已回滚锁定状态，钥匙不消耗。
                return;
            case KeyUnlockResult.Released:
                Messages.Message("Mugirl.SlaveApparelFullyUnlocked".Translate(wearer.LabelShort), wearer, MessageTypeDefOf.PositiveEvent);
                break;
            default:
                Messages.Message("Mugirl.SlaveApparelUnlockPartial".Translate(pawn.LabelShort, wearer.LabelShort, apparel.LockCount), wearer, MessageTypeDefOf.PositiveEvent);
                break;
        }

        Thing? key = Key;
        if (key != null && !key.Destroyed)
        {
            key.Destroy();
        }
    }
}
