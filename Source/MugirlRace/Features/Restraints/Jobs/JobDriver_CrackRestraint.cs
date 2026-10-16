using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Restraints;

// 用解码器破解地面上的高级束具（JobDef CrackBondageGear，由 CompTargetable_CrackableRestraint 选定目标后下达）。
// 目标：A = 解码器，B = 地面上的束具。解码器不被消耗，与 1.6 相同。
public class JobDriver_CrackRestraint : JobDriver
{
    // 当前固定取物流程的结构版本；始终保存，便于后续精确辨识索引含义。
    private int itemFlowVersion = 1;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref itemFlowVersion, "itemFlowVersion", 1, forceSave: true);
    }

    private const TargetIndex ItemInd = TargetIndex.A;
    private const TargetIndex ApparelInd = TargetIndex.B;

    // 1.6 起固定 60 tick，不读取 CompProperties_Usable.useDuration。
    private const int CrackTicks = 60;

    private Thing? Item => job.GetTarget(ItemInd).Thing;

    private Apparel? TargetApparel => job.GetTarget(ApparelInd).Thing as Apparel;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Thing? item = Item;
        Apparel? apparel = TargetApparel;
        if (item == null || apparel == null)
        {
            return false;
        }

        bool itemAlreadyHeld = pawn.inventory != null && pawn.inventory.Contains(item);
        bool reserveItem = itemAlreadyHeld || pawn.Reserve(item, job, 1, -1, null, errorOnFailed);
        bool reserveApparel = pawn.Reserve(apparel, job, 1, -1, null, errorOnFailed);

        return reserveItem && reserveApparel;
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        // toil 数量固定，束具与解码器的有效性在运行时判断，读档重建不会裁剪索引。
        this.FailOn(() => !IsCrackableTarget(TargetApparel) || Item is not { Destroyed: false });

        yield return Toils_Reserve.Reserve(ApparelInd);

        foreach (Toil toil in RestraintToils.AcquireItem(ItemInd))
        {
            yield return toil;
        }

        yield return Toils_Goto.GotoThing(ApparelInd, PathEndMode.Touch);

        yield return Toils_General.Wait(CrackTicks)
            .WithProgressBarToilDelay(ApparelInd)
            .FailOnDespawnedNullOrForbidden(ApparelInd);

        Toil finish = ToilMaker.MakeToil("MugirlCrackBondageGear");
        finish.initAction = () =>
        {
            Apparel? apparel = TargetApparel;
            if (!IsCrackableTarget(apparel))
            {
                return;
            }

            Item?.TryGetComp<CompTargetEffect_CrackRestraint>()?.DoEffectOn(pawn, apparel);
        };
        finish.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return finish;
    }

    // 地面上、未破解的高级束具。
    private static bool IsCrackableTarget([NotNullWhen(true)] Apparel? apparel)
    {
        return apparel != null
            && !apparel.Destroyed
            && apparel.Spawned
            && apparel.IsAdvancedRestraint()
            && !apparel.IsCrackedAdvancedRestraint();
    }
}
