using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Grazing;

// 雪牛娘吃地图上的植物时改用动物式准备流程（预约后走到植物旁就地啃食），不按工具使用者的方式先把植物拿起来。
// 流程复刻原版 JobDriver_Ingest.PrepareToIngestToils_NonToolUser 与 ReserveFood（1.7 结构未变），
// 有意的差异：预约失败只记一次警告而不是红字错误。
[HarmonyPatch(typeof(JobDriver_Ingest), "PrepareToIngestToils", typeof(Toil))]
[MugirlPatch(PatchModule.Grazing, PatchRisk.High, "Mugirls use the base tool-user ingest preparation for map plants, which may pick plants up before eating.")]
public static class Harmony_JobDriver_Ingest_PrepareToIngestToils_Graze
{
    public static bool Prefix(JobDriver_Ingest __instance, ref IEnumerable<Toil> __result)
    {
        if (!ShouldUseAnimalGrazeToils(__instance.pawn, __instance.job))
        {
            return true;
        }

        __result = PrepareAnimalGrazeToils();
        return false;
    }

    private static bool ShouldUseAnimalGrazeToils([NotNullWhen(true)] Pawn? pawn, [NotNullWhen(true)] Job? job)
    {
        if (pawn is null || job is null || job.def != JobDefOf.Ingest || !MugirlIdentity.IsMugirlPawn(pawn))
        {
            return false;
        }

        Thing? thing = job.GetTarget(TargetIndex.A).Thing;
        return thing?.Map == pawn.Map && GrazeUtility.IsMapPlantFood(thing);
    }

    private static IEnumerable<Toil> PrepareAnimalGrazeToils()
    {
        yield return ReserveFood();
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch).FailOnDespawnedNullOrForbidden(TargetIndex.A);
    }

    private static Toil ReserveFood()
    {
        Toil toil = ToilMaker.MakeToil("MugirlReserveGrazePlant");
        toil.initAction = delegate
        {
            Pawn? actor = toil.actor;
            Job? curJob = actor?.jobs?.curJob;
            if (actor?.Faction is null || curJob is null)
            {
                return;
            }

            Thing? thing = curJob.GetTarget(TargetIndex.A).Thing;
            if (thing is null || actor.carryTracker?.CarriedThing == thing)
            {
                return;
            }

            int maxAmountToPickup = FoodUtility.GetMaxAmountToPickup(thing, actor, curJob.count);
            if (maxAmountToPickup == 0)
            {
                return;
            }

            if (!actor.Reserve(thing, curJob, 10, maxAmountToPickup))
            {
                // 按植物 Def 去重：同一种植物每局只报一次，旧版按 Pawn 去重会随参与的 Pawn 增多而累积键。
                MugirlLog.WarningOnce(
                    "GrazeIngest.ReserveFailed." + thing.def.defName,
                    "Pawn food reservation for " + actor + " on job " + curJob + " failed, because it could not register grazing plant from " + thing + " - amount: " + maxAmountToPickup);
                actor.jobs?.EndCurrentJob(JobCondition.Errored);
                return;
            }

            curJob.count = maxAmountToPickup;
        };
        toil.defaultCompleteMode = ToilCompleteMode.Instant;
        toil.atomicWithPrevious = true;
        return toil;
    }
}
