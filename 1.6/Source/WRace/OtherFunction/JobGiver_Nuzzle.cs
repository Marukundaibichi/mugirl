using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobGiver_Nuzzle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (NuzzleUtility.GetNuzzleMTBHours(pawn) <= 0f)
            {
                return null;
            }
            Pawn t;
            if (!(from p in pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction)
                  where !p.NonHumanlikeOrWildMan() && p != pawn && p.Position.InHorDistOf(pawn.Position, 40f) && pawn.GetRoom(RegionType.Set_All) == p.GetRoom(RegionType.Set_All) && !p.Position.IsForbidden(pawn) && p.CanCasuallyInteractNow(false, false, false, false)
                  select p).TryRandomElement(out t))
            {
                return null;
            }
            Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_Nuzzle, t);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 3000;
            return job;
        }

        private const float MaxNuzzleDistance = 40f;
    }
}
