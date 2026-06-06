using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_DismountMooGirl : JobDriver
    {
        private const TargetIndex MooInd = TargetIndex.A;

        private Pawn Moo => job.GetTarget(MooInd).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn == Moo || pawn.Reserve(Moo, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);
            if (pawn != Moo)
            {
                yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);
            }

            yield return Toils_General.Do(delegate
            {
                Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(Moo);
                if (comp == null || !comp.TryDismount())
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            });
        }
    }
}
