using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompRopeToBuild : CompUsable
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            if (!pawn.CanReserve(parent))
            {
                yield return new FloatMenuOption("Reserved".Translate(), null, MenuOptionPriority.DisabledOption);
                yield break;
            }

            if (!pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
            {
                yield break;
            }

            Pawn ropee = RopingService.FirstMooGirlFollowing(pawn);
            if (ropee != null)
            {
                yield return new FloatMenuOption("MooGirl.RopeToHitch".Translate(), () => StartRoping(pawn));
            }
            else
            {
                yield return new FloatMenuOption("MooGirl.NoValidToRope".Translate(), null, MenuOptionPriority.DisabledOption);
            }
        }

        private void StartRoping(Pawn pawn)
        {
            if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
            {
                return;
            }

            Pawn ropee = RopingService.FirstMooGirlFollowing(pawn);
            if (ropee == null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(MooGirl_DefOf.RopeToBuild, parent);
            job.targetB = ropee;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
