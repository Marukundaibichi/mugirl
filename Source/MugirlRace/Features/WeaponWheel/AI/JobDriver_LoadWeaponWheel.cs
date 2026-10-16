using Verse.AI;

namespace Mugirl.Features.WeaponWheel;

// 装填任务的两段流程；槽位由 Job.count 编码，任务进度由原版 JobDriver 保存。
public sealed class JobDriver_LoadWeaponWheel : JobDriver
{
    private const TargetIndex WeaponIndex = TargetIndex.A;

    private ThingWithComps? TargetWeapon => job.GetTarget(WeaponIndex).Thing as ThingWithComps;

    private int SlotIndex => WeaponWheelJobs.SlotIndexOf(job);

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        ThingWithComps? weapon = TargetWeapon;
        return weapon != null && pawn.Reserve(weapon, job, 1, 1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(WeaponIndex);
        this.FailOnBurningImmobile(WeaponIndex);
        this.FailOnForbidden(WeaponIndex);

        yield return Toils_Goto.GotoThing(WeaponIndex, PathEndMode.ClosestTouch);

        Toil attach = ToilMaker.MakeToil("LoadWeaponWheel");
        attach.initAction = delegate
        {
            CompWeaponWheel? comp = pawn.TryGetComp<CompWeaponWheel>();
            ThingWithComps? weapon = TargetWeapon;
            string? reason = null;
            if (comp == null || weapon == null || !comp.TryAttachGroundWeapon(weapon, SlotIndex, out reason))
            {
                if (!reason.NullOrEmpty())
                {
                    Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                }
                EndJobWith(JobCondition.Incompletable);
            }
        };
        attach.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return attach;
    }
}
