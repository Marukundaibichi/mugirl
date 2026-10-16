using Verse.AI;

namespace Mugirl.Features.Milk;

// 雪牛娘给倒地者或站立殖民者喂奶（类名沿用旧 JobDef 的 driverClass）。
public sealed class JobDriver_FeedMilk : JobDriver
{
    private const TargetIndex RecipientInd = TargetIndex.A;

    // 被喂者等待 Job 的 loadID，随存档保存，读档后仍能提前解除等待。
    private int forcedWaitJobLoadId = MilkPartnerWait.NoJob;

    private Pawn? Recipient => job.GetTarget(RecipientInd).Thing as Pawn;

    private Pawn MooPawn => pawn;

    public override void ExposeData()
    {
        base.ExposeData();
        MilkPartnerWait.Expose(ref forcedWaitJobLoadId);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? recipient = Recipient;
        return MilkInteractionUtility.CanFeedPawnNow(MooPawn, recipient)
            && pawn.Reserve(recipient, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(RecipientInd);
        this.FailOn(() => !MilkInteractionUtility.CanFeedPawnNow(MooPawn, Recipient));
        AddFinishAction(_ =>
        {
            MilkingAnimation.EndFeeding(MooPawn, Recipient);
            MilkInteractionUtility.EndMilkInteractionWait(Recipient, forcedWaitJobLoadId);
            forcedWaitJobLoadId = MilkPartnerWait.NoJob;
        });

        yield return Toils_Goto.GotoThing(RecipientInd, PathEndMode.Touch);

        Toil feed = Toils_General.Wait(MilkInteractionUtility.DirectMilkInteractionTicks);
        feed.handlingFacing = true;
        feed.initAction = () =>
        {
            Pawn? recipient = Recipient;
            pawn.pather.StopDead();
            forcedWaitJobLoadId = recipient != null && !recipient.Destroyed && !recipient.Downed
                ? MilkInteractionUtility.ForceMilkInteractionWait(
                    recipient,
                    MilkInteractionUtility.DirectMilkInteractionTicks + MilkInteractionUtility.PartnerWaitExtraTicks,
                    MilkingAnimation.FeedingRecipientFacing(MooPawn, recipient))
                : MilkPartnerWait.NoJob;
            MilkingAnimation.StartFeeding(MooPawn, recipient);
        };
        feed.tickAction = () => MilkingAnimation.TickFeeding(MooPawn, Recipient);
        feed.WithProgressBarToilDelay(RecipientInd);
        feed.FailOnCannotTouch(RecipientInd, PathEndMode.Touch);
        yield return feed;

        yield return Toils_General.DoAtomic(ApplyFeedEffects);
    }

    private void ApplyFeedEffects()
    {
        if (!MilkInteractionUtility.ApplyFeed(Recipient, MooPawn))
        {
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
        }
    }
}
