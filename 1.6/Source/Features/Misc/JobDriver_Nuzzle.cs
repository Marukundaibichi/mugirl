using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_Nuzzle : JobDriver
    {
        private Pawn Recipient => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn recipient = Recipient;
            return CanNuzzle(pawn, recipient)
                && pawn.Reserve(recipient, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnNotCasualInterruptible(TargetIndex.A);
            this.FailOn(() => !CanNuzzle(pawn, Recipient));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch, false);
            yield return Toils_Interpersonal.WaitToBeAbleToInteract(this.pawn);
            Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch, false).socialMode = RandomSocialMode.Off;
            yield return Toils_General.Do(delegate
            {
                Pawn recipient = Recipient;
                if (CanNuzzle(pawn, recipient))
                {
                    this.pawn.interactions.TryInteractWith(recipient, MooGirl_DefOf.MooGirl_Nuzzle);
                }
            });
            yield break;
        }

        private static bool CanNuzzle(Pawn actor, Pawn recipient)
        {
            return actor != null
                && recipient != null
                && actor != recipient
                && !actor.Dead
                && !actor.Downed
                && actor.Spawned
                && recipient.Spawned
                && !recipient.Dead
                && recipient.CanCasuallyInteractNow(false, false, false, false);
        }
    }
}
