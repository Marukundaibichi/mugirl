using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Grazing;

// 雪牛娘自发吃完一株植物后，若仍未吃饱且没有其他排队任务，就把附近下一株可食植物排到队首，连续啃食。
// 以 JobDriver.Cleanup 为挂点（原版 JobDriver_Ingest 不重写它），条件判断只是几次字段比较。
[HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup), typeof(JobCondition))]
[MugirlPatch(PatchModule.Grazing, PatchRisk.High, "Mugirl grazing jobs no longer chain to the next nearby plant.")]
public static class Harmony_JobDriver_Cleanup_GrazeChain
{
    private const float FullFoodTolerance = 0.02f;
    private const float MaxGrazeSearchRadius = 30f;

    public static void Prefix(JobDriver __instance, JobCondition condition)
    {
        if (condition != JobCondition.Succeeded || __instance is not JobDriver_Ingest ingestDriver)
        {
            return;
        }

        Pawn pawn = ingestDriver.pawn;
        if (!ShouldChainGraze(pawn, ingestDriver.job) || FindNextEdiblePlant(pawn) is not { } nextPlant)
        {
            return;
        }

        Job nextJob = JobMaker.MakeJob(JobDefOf.Ingest, nextPlant);
        nextJob.count = 1;
        pawn.jobs.jobQueue.EnqueueFirst(nextJob, JobTag.Misc);
    }

    private static bool ShouldChainGraze([NotNullWhen(true)] Pawn? pawn, [NotNullWhen(true)] Job? job)
    {
        if (pawn is null || job is null || pawn.Dead || pawn.Downed || !pawn.Spawned || !MugirlIdentity.IsMugirlPawn(pawn))
        {
            return false;
        }

        if (job.def != JobDefOf.Ingest || job.playerForced || pawn.Drafted || pawn.InMentalState)
        {
            return false;
        }

        if (pawn.jobs?.jobQueue is not { Count: 0 })
        {
            return false;
        }

        Need_Food? food = pawn.needs?.food;
        if (food is null || food.CurLevel >= food.MaxLevel - FullFoodTolerance)
        {
            return false;
        }

        return GrazeUtility.IsPlantFoodDef(job.GetTarget(TargetIndex.A).Thing?.def);
    }

    private static Thing? FindNextEdiblePlant(Pawn pawn)
    {
        Map? map = pawn.Map;
        if (map is null)
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
        if (!GrazeUtility.IsMapPlantFood(thing))
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

        return pawn.WillEat(thing, pawn);
    }
}
