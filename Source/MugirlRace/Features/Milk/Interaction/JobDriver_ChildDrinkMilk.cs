using Verse.AI;

namespace Mugirl.Features.Milk;

// 小孩从雪牛娘处找奶喝：获得哺育进度，并合并普通喝奶收益。
public sealed class JobDriver_ChildDrinkMilk : JobDriver
{
    private const TargetIndex MooInd = TargetIndex.A;

    // 雪牛娘等待 Job 的 loadID，随存档保存，读档后仍能提前解除等待。
    private int forcedWaitJobLoadId = MilkPartnerWait.NoJob;

    private Pawn? MooPawn => job.GetTarget(MooInd).Thing as Pawn;

    private Pawn Child => pawn;

    public override void ExposeData()
    {
        base.ExposeData();
        MilkPartnerWait.Expose(ref forcedWaitJobLoadId);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? mooPawn = MooPawn;
        return MilkInteractionUtility.CanChildBreastfeedNow(Child, mooPawn)
            && pawn.Reserve(mooPawn, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(MooInd);
        this.FailOn(() => !MilkInteractionUtility.CanChildBreastfeedNow(Child, MooPawn));
        AddFinishAction(_ =>
        {
            MilkingAnimation.EndFeeding(MooPawn, Child);
            MilkInteractionUtility.EndMilkInteractionWait(MooPawn, forcedWaitJobLoadId);
            forcedWaitJobLoadId = MilkPartnerWait.NoJob;
        });

        yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

        Toil feed = Toils_General.Wait(MilkInteractionUtility.ChildBreastfeedInteractionTicks);
        feed.handlingFacing = true;
        feed.initAction = () =>
        {
            Pawn? mooPawn = MooPawn;
            if (mooPawn == null || mooPawn.Destroyed)
            {
                forcedWaitJobLoadId = MilkPartnerWait.NoJob;
                return;
            }

            pawn.pather.StopDead();
            forcedWaitJobLoadId = MilkInteractionUtility.ForceMilkInteractionWait(
                mooPawn,
                MilkInteractionUtility.ChildBreastfeedInteractionTicks + MilkInteractionUtility.PartnerWaitExtraTicks,
                Rot4.South);
            MilkingAnimation.StartFeeding(mooPawn, Child);
        };
        feed.tickAction = () => MilkingAnimation.TickFeeding(MooPawn, Child);
        feed.WithProgressBarToilDelay(MooInd);
        feed.FailOnCannotTouch(MooInd, PathEndMode.Touch);
        yield return feed;

        yield return Toils_General.DoAtomic(ApplyBreastfeedEffects);
    }

    private void ApplyBreastfeedEffects()
    {
        if (!MilkInteractionUtility.ApplyChildBreastfeed(Child, MooPawn))
        {
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
        }
    }
}
