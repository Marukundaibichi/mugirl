using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    [HarmonyPatch]
    public static class Harmony_MooGirlGrazeChain
    {
        private const float FullFoodTolerance = 0.02f;
        private const float MaxGrazeSearchRadius = 30f;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JobDriver), "Cleanup");
        }

        public static void Prefix(JobDriver __instance, JobCondition condition)
        {
            if (condition != JobCondition.Succeeded)
            {
                return;
            }

            JobDriver_Ingest ingestDriver = __instance as JobDriver_Ingest;
            if (ingestDriver == null)
            {
                return;
            }

            Pawn pawn = ingestDriver.pawn;
            Job job = ingestDriver.job;
            if (!ShouldChainGraze(pawn, job))
            {
                return;
            }

            Thing nextPlant = FindNextEdiblePlant(pawn);
            if (nextPlant == null)
            {
                return;
            }

            Job nextJob = JobMaker.MakeJob(JobDefOf.Ingest, nextPlant);
            nextJob.count = 1;
            pawn.jobs.jobQueue.EnqueueFirst(nextJob, JobTag.Misc);
        }

        private static bool ShouldChainGraze(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Dead || pawn.Downed || !pawn.Spawned || !MountedPawnUtility.IsMooGirl(pawn))
            {
                return false;
            }

            if (job.def != JobDefOf.Ingest || job.playerForced || pawn.Drafted || pawn.InMentalState)
            {
                return false;
            }

            if (pawn.jobs?.jobQueue == null || pawn.jobs.jobQueue.Count > 0)
            {
                return false;
            }

            Need_Food food = pawn.needs?.food;
            if (food == null || food.CurLevel >= food.MaxLevel - FullFoodTolerance)
            {
                return false;
            }

            return IsPlantFoodDef(job.GetTarget(TargetIndex.A).Thing?.def);
        }

        private static Thing FindNextEdiblePlant(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }

            TraverseParms traverseParms = TraverseParms.For(pawn, DangerUtility.NormalMaxDanger(pawn));
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                map,
                ThingRequest.ForGroup(ThingRequestGroup.Plant),
                PathEndMode.Touch,
                traverseParms,
                MaxGrazeSearchRadius,
                thing => IsValidPlantFood(pawn, thing));
        }

        private static bool IsValidPlantFood(Pawn pawn, Thing thing)
        {
            if (!IsMapPlantFood(thing))
            {
                return false;
            }

            if (thing.IsForbidden(pawn) || !ForbidUtility.InAllowedArea(thing.Position, pawn))
            {
                return false;
            }

            if (!pawn.CanReserve(thing, 1, 1, null, ignoreOtherReservations: false))
            {
                return false;
            }

            if (DangerUtility.GetDangerFor(thing.Position, pawn, thing.Map) > DangerUtility.NormalMaxDanger(pawn))
            {
                return false;
            }

            return FoodUtility.FoodIsSuitable(pawn, thing.def);
        }

        private static bool IsMapPlantFood(Thing thing)
        {
            Plant plant = thing as Plant;
            return plant != null
                && plant.Spawned
                && !plant.Destroyed
                && plant.IngestibleNow
                && IsPlantFoodDef(plant.def);
        }

        private static bool IsPlantFoodDef(ThingDef def)
        {
            return def != null && typeof(Plant).IsAssignableFrom(def.thingClass) && def.ingestible != null;
        }
    }
}
