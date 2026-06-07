using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobGiver_Nuzzle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Map == null || NuzzleUtility.GetNuzzleMTBHours(pawn) <= 0f)
            {
                return null;
            }

            if (!TryFindNuzzleTarget(pawn, out Pawn target))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_Nuzzle, target);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 3000;
            return job;
        }

        private static bool TryFindNuzzleTarget(Pawn pawn, out Pawn target)
        {
            target = null;
            int candidateCount = 0;
            Room pawnRoom = pawn.GetRoom(RegionType.Set_All);
            var spawnedPawns = pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction);

            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn candidate = spawnedPawns[i];
                if (!IsValidNuzzleTarget(pawn, candidate, pawnRoom))
                {
                    continue;
                }

                candidateCount++;
                if (Rand.RangeInclusive(1, candidateCount) == 1)
                {
                    target = candidate;
                }
            }

            return target != null;
        }

        private static bool IsValidNuzzleTarget(Pawn pawn, Pawn candidate, Room pawnRoom)
        {
            return candidate != pawn
                && !candidate.NonHumanlikeOrWildMan()
                && candidate.Position.InHorDistOf(pawn.Position, MaxNuzzleDistance)
                && pawnRoom == candidate.GetRoom(RegionType.Set_All)
                && !candidate.Position.IsForbidden(pawn)
                && pawn.CanReserve(candidate)
                && candidate.CanCasuallyInteractNow(false, false, false, false);
        }

        private const float MaxNuzzleDistance = 40f;
    }
}
