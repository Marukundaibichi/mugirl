using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompRopeToBuild : CompUsable
    {

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {

            if (!pawn.CanReserve(this.parent))
            {
                yield return new FloatMenuOption("Reserved", null, MenuOptionPriority.DisabledOption);
            }
            else if (pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
            {

                var ropee = GetRopeePawn(pawn);
                if (ropee != null && ropee.RaceProps.body == MooGirl_DefOf.MooGirlBody && ropee.CurJob?.def == MooGirl_DefOf.Job_FollowRoper && ropee.CurJob.targetA.Thing == pawn)
                {
                    yield return new FloatMenuOption("MooGirl.RopeToHitch".Translate(), () => StartRoping(pawn));
                }
                else
                {
                    yield return new FloatMenuOption("MooGirl.NoValidToRope".Translate(), null, MenuOptionPriority.DisabledOption);
                }
            }
        }


        private void StartRoping(Pawn pawn)
        {
            var ropee = GetRopeePawn(pawn); 
            if (ropee != null && ropee.RaceProps.body == MooGirl_DefOf.MooGirlBody && ropee.CurJob?.def == MooGirl_DefOf.Job_FollowRoper && ropee.CurJob.targetA.Thing == pawn)
            {

                Job job = JobMaker.MakeJob(MooGirl_DefOf.RopeToBuild, this.parent);
                job.targetB = ropee;  
                pawn.jobs.StartJob(job); 
            }
        }

        private Pawn GetRopeePawn(Pawn pawn)
        {
            return pawn.Map.mapPawns.AllPawnsSpawned
                .FirstOrDefault(p => p.RaceProps.body == MooGirl_DefOf.MooGirlBody && p.CurJob?.def == MooGirl_DefOf.Job_FollowRoper && p.CurJob.targetA.Thing == pawn);
        }
    }
}
