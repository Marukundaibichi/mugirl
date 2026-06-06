using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_Nuzzle : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnNotCasualInterruptible(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch, false);
            yield return Toils_Interpersonal.WaitToBeAbleToInteract(this.pawn);
            Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch, false).socialMode = RandomSocialMode.Off;
            yield return Toils_General.Do(delegate
            {
                Pawn recipient = (Pawn)this.pawn.CurJob.targetA.Thing;
                this.pawn.interactions.TryInteractWith(recipient, MooGirl_DefOf.MooGirl_Nuzzle);
            });
            yield break;
        }
    }
}
