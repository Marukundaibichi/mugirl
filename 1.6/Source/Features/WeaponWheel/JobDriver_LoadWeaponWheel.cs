using System.Collections.Generic;
using RimWorld;
using Mugirl.Features.WeaponWheel;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public sealed class JobDriver_LoadWeaponWheel : JobDriver
    {
        private const TargetIndex WeaponIndex = TargetIndex.A;

        private ThingWithComps TargetWeapon => job.GetTarget(WeaponIndex).Thing as ThingWithComps;
        private int SlotIndex => job.count - 1;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return TargetWeapon != null && pawn.Reserve(TargetWeapon, job, 1, 1, null, errorOnFailed);
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
                Comp_WeaponWheel comp = pawn.TryGetComp<Comp_WeaponWheel>();
                ThingWithComps weapon = TargetWeapon;
                string reason = null;
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
}
