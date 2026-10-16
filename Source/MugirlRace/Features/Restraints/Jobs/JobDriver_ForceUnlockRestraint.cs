using Verse.AI;

namespace Mugirl.Features.Restraints;

// 不用钥匙强行打开其他 Pawn 身上的束具（JobDef ForceUnlockSlaveApparel）；施工期间有几率割伤佩戴者。
// 目标：A = 佩戴者，B = 束具。
public class JobDriver_ForceUnlockRestraint : JobDriver
{
    private const TargetIndex TargetPawnInd = TargetIndex.A;
    private const TargetIndex ApparelInd = TargetIndex.B;

    private Pawn? TargetPawn => job.GetTarget(TargetPawnInd).Thing as Pawn;

    private Apparel? TargetApparel => job.GetTarget(ApparelInd).Thing as Apparel;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? targetPawn = TargetPawn;
        Apparel? targetApparel = TargetApparel;
        if (targetPawn == null || targetApparel == null || !CanForceUnlockNow(pawn, targetPawn, targetApparel))
        {
            return false;
        }

        bool reserveTarget = targetPawn == pawn || pawn.Reserve(targetPawn, job, 1, -1, null, errorOnFailed);
        bool reserveApparel = pawn.Reserve(targetApparel, job, 1, -1, null, errorOnFailed);
        return reserveTarget && reserveApparel;
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        Pawn? wearer = TargetPawn;
        Apparel? apparel = TargetApparel;
        bool unlockSelf = wearer == pawn;

        // JobDriver.ExposeData 会强制保存 curToilIndex，并在读档时重新执行 MakeNewToils；
        // 读档阶段 pawn 尚未生成，CanReach 等依赖生成状态的判定必然失败。若在此处按这些
        // 条件裁剪 toil，读档后 toil 数量就会小于存档里的下标，触发
        // "tried to get CurToil with curToilIndex=N but only has M toils"。
        // 因此 toil 数量只依赖存档稳定的条件（wearer 是否为施术者本人），
        // 有效性一律交给 FailOn 在运行时判定。
        this.FailOn(() => !CanForceUnlockNow(pawn, wearer, apparel));

        if (!unlockSelf)
        {
            yield return Toils_Reserve.Reserve(TargetPawnInd);
        }

        yield return Toils_Reserve.Reserve(ApparelInd);

        if (!unlockSelf)
        {
            yield return Toils_Goto.GotoThing(TargetPawnInd, PathEndMode.Touch);
        }

        Toil wait = RestraintToils.WaitOn(TargetPawnInd, ForceUnlockRestraintUtility.WorkTicks, unlockSelf);
        wait.tickAction = () =>
        {
            // 用已存档的 ticksLeftThisToil 计算已施工时长：1.6 用闭包局部计数，读档后归零。
            // 第 k 次 tickAction 时 ticksLeftThisToil = WorkTicks - k，与 1.6 计数器的触发点相同。
            int elapsed = ForceUnlockRestraintUtility.WorkTicks - ticksLeftThisToil;
            if (elapsed > 0
                && elapsed % ForceUnlockRestraintUtility.WoundCheckIntervalTicks == 0
                && Rand.Chance(ForceUnlockRestraintUtility.WoundChancePerInterval))
            {
                ApplyForceUnlockWound(pawn, wearer);
            }
        };
        yield return wait;

        Toil finish = ToilMaker.MakeToil("MugirlForceUnlockSlaveApparel");
        finish.initAction = () =>
        {
            if (wearer == null || apparel == null || !CanForceUnlockNow(pawn, wearer, apparel))
            {
                MugirlLog.WarningOnce("ForceUnlockSlaveApparel.TargetMissing", () => "Mugirl.Restraints.UnlockTargetMissingLog".Translate().ToString());
                return;
            }

            CompleteForceUnlock(wearer, apparel);
        };
        finish.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return finish;
    }

    private static bool CanForceUnlockNow(Pawn actor, Pawn? wearer, Apparel? apparel)
    {
        return wearer != null
            && ForceUnlockRestraintUtility.IsLockedForceUnlockable(wearer, apparel)
            && ForceUnlockRestraintUtility.HasRequiredStrength(actor, apparel)
            && (actor == wearer || actor.CanReach(wearer, PathEndMode.Touch, Danger.Some));
    }

    private void CompleteForceUnlock(Pawn wearer, Apparel apparel)
    {
        if (apparel is not RestraintApparel slaveApparel)
        {
            return;
        }

        if (slaveApparel.TryReleaseAndDrop(wearer, RestraintToils.DropCell(wearer, wearer, pawn), slaveApparel.LockCount))
        {
            Messages.Message("Mugirl.Restraints.ForceUnlock.Success".Translate(pawn.LabelShort, wearer.LabelShort, apparel.Label), wearer, MessageTypeDefOf.PositiveEvent);
        }
    }

    private static void ApplyForceUnlockWound(Pawn actor, Pawn? wearer)
    {
        if (wearer == null || wearer.Dead)
        {
            return;
        }

        wearer.TakeDamage(new DamageInfo(DamageDefOf.Cut, Rand.Range(2f, 5f), 0f, -1, actor));
        if (wearer.Spawned && wearer.MapHeld != null)
        {
            MoteMaker.ThrowText(wearer.DrawPos, wearer.MapHeld, "Mugirl.Restraints.ForceUnlock.WoundMote".Translate());
        }
    }
}
