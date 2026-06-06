using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_MountMooGirl : JobDriver
    {
        private const TargetIndex MooInd = TargetIndex.A;

        private Pawn Moo => job.GetTarget(MooInd).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Moo, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);
            this.FailOn(() =>
            {
                Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(Moo);
                return comp == null || !comp.CanMount(pawn, out _);
            });

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);
            yield return Toils_General.Wait(45, MooInd).WithProgressBarToilDelay(MooInd);
            yield return Toils_General.Do(delegate
            {
                Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(Moo);
                if (comp == null || !comp.TryMount(pawn))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            });
        }
    }
}
