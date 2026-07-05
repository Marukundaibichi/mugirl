using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class CompRopeToBuild : CompUsable
    {
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            NotifyHitchRemoved(map);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            NotifyHitchRemoved(previousMap);
        }

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

            Pawn ropee = RopingService.FirstMugirlFollowing(pawn);
            if (ropee != null)
            {
                yield return new FloatMenuOption("Mugirl.RopeToHitch".Translate(), () => StartRoping(pawn));
            }
            else
            {
                yield return new FloatMenuOption("Mugirl.NoValidToRope".Translate(), null, MenuOptionPriority.DisabledOption);
            }
        }

        private void StartRoping(Pawn pawn)
        {
            if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
            {
                return;
            }

            Pawn ropee = RopingService.FirstMugirlFollowing(pawn);
            if (ropee == null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(Mugirl_DefOf.RopeToBuild, parent);
            job.targetB = ropee;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void NotifyHitchRemoved(Map map)
        {
            RopingService.NotifyWallRopeHitchRemoved(map, parent.PositionHeld);
        }
    }
}
