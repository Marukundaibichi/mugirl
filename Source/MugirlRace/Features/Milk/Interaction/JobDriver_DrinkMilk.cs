using Verse.AI;

namespace Mugirl.Features.Milk;

// 小人右键雪牛娘喝奶：补充饱食度、心情与疗愈 buff；雪牛娘用原版 Wait_MaintainPosture 原地等待。
public sealed class JobDriver_DrinkMilk : JobDriver
{
    private const TargetIndex MooInd = TargetIndex.A;

    // 雪牛娘等待 Job 的 loadID，随存档保存，读档后仍能提前解除等待。
    private int forcedWaitJobLoadId = MilkPartnerWait.NoJob;

    private Pawn? MooPawn => job.GetTarget(MooInd).Thing as Pawn;

    private Pawn Drinker => pawn;

    public override void ExposeData()
    {
        base.ExposeData();
        MilkPartnerWait.Expose(ref forcedWaitJobLoadId);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? mooPawn = MooPawn;
        return MilkInteractionUtility.CanDrinkMilkNow(Drinker, mooPawn)
            && pawn.Reserve(mooPawn, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(MooInd);
        this.FailOn(() => !MilkInteractionUtility.CanDrinkMilkNow(Drinker, MooPawn));
        AddFinishAction(_ =>
        {
            MilkPartnerWait.End(MooPawn, JobDefOf.Wait_MaintainPosture, forcedWaitJobLoadId);
            forcedWaitJobLoadId = MilkPartnerWait.NoJob;
        });

        yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

        Toil drink = Toils_General.Wait(MilkInteractionUtility.DirectMilkInteractionTicks, MooInd);
        drink.initAction = () =>
        {
            Pawn? mooPawn = MooPawn;
            if (mooPawn == null || mooPawn.Destroyed)
            {
                forcedWaitJobLoadId = MilkPartnerWait.NoJob;
                return;
            }

            pawn.pather.StopDead();
            PawnUtility.ForceWait(mooPawn, MilkInteractionUtility.DirectMilkInteractionTicks + MilkInteractionUtility.PartnerWaitExtraTicks, pawn, true);
            forcedWaitJobLoadId = MilkPartnerWait.CurrentLoadId(mooPawn);
        };
        drink.WithProgressBarToilDelay(MooInd);
        drink.FailOnCannotTouch(MooInd, PathEndMode.Touch);
        yield return drink;

        yield return Toils_General.DoAtomic(ApplyDrinkEffects);
    }

    private void ApplyDrinkEffects()
    {
        if (!MilkInteractionUtility.ApplyAdultDrink(Drinker, MooPawn))
        {
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
        }
    }
}
