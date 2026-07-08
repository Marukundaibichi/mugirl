using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    internal static class MugirlMigrationIngestNutritionUtility
    {
        internal const float FixedIngestNutrition = 0.08f;

        internal static bool ShouldUseFixedNutrition(Pawn pawn)
        {
            if (pawn?.needs?.food == null || MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
            {
                return false;
            }

            return MugirlEventUtility.IsMigrationPawn(pawn);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Harmony_MugirlMigrationFixedIngestNutrition
    {
        public static void Postfix(Pawn ingester, ref float __result)
        {
            if (MugirlMigrationIngestNutritionUtility.ShouldUseFixedNutrition(ingester))
            {
                __result = MugirlMigrationIngestNutritionUtility.FixedIngestNutrition;
            }
        }
    }

    [HarmonyPatch]
    public static class Harmony_MugirlGrazeChain
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
            if (pawn == null || job == null || pawn.Dead || pawn.Downed || !pawn.Spawned || !MountedPawnUtility.IsMugirl(pawn))
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

            return MugirlGrazeUtility.IsPlantFoodDef(job.GetTarget(TargetIndex.A).Thing?.def);
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
            if (!MugirlGrazeUtility.IsMapPlantFood(thing))
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
    }

    [HarmonyPatch]
    public static class Harmony_MugirlGrazeIngest
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JobDriver_Ingest), "PrepareToIngestToils");
        }

        public static bool Prefix(JobDriver_Ingest __instance, ref IEnumerable<Toil> __result)
        {
            Pawn pawn = __instance?.pawn;
            Job job = __instance?.job;
            if (!ShouldUseAnimalGrazeToils(pawn, job))
            {
                return true;
            }

            __result = PrepareAnimalGrazeToils();
            return false;
        }

        private static bool ShouldUseAnimalGrazeToils(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || job.def != JobDefOf.Ingest || !MountedPawnUtility.IsMugirl(pawn))
            {
                return false;
            }

            Thing thing = job.GetTarget(TargetIndex.A).Thing;
            return thing?.Map == pawn.Map && MugirlGrazeUtility.IsMapPlantFood(thing);
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
                Pawn actor = toil.actor;
                Job curJob = actor?.jobs?.curJob;
                if (actor?.Faction == null || curJob == null)
                {
                    return;
                }

                Thing thing = curJob.GetTarget(TargetIndex.A).Thing;
                if (thing == null || actor.carryTracker?.CarriedThing == thing)
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
                    MugirlLog.WarningOnce(
                        "GrazeIngest.ReserveFailed." + actor.ThingID,
                        "Pawn food reservation for " + actor?.ToString() + " on job " + curJob?.ToString() + " failed, because it could not register grazing plant from " + thing?.ToString() + " - amount: " + maxAmountToPickup);
                    actor.jobs.EndCurrentJob(JobCondition.Errored);
                    return;
                }

                curJob.count = maxAmountToPickup;
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.atomicWithPrevious = true;
            return toil;
        }
    }

    [HarmonyPatch]
    public static class Harmony_MugirlGrazeFloatMenu
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(FloatMenuOptionProvider_Ingest),
                "GetSingleOptionFor",
                new[] { typeof(Thing), typeof(FloatMenuContext) });
        }

        public static bool Prefix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
        {
            Pawn pawn = context?.FirstSelectedPawn;
            if (!ShouldUseGrazeFloatMenu(pawn, clickedThing))
            {
                return true;
            }

            __result = BuildGrazeFloatMenuOption(pawn, clickedThing);
            return false;
        }

        private static bool ShouldUseGrazeFloatMenu(Pawn pawn, Thing thing)
        {
            return pawn != null
                && thing?.Map == pawn.Map
                && MountedPawnUtility.IsMugirl(pawn)
                && MugirlGrazeUtility.IsMapPlantFood(thing);
        }

        private static FloatMenuOption BuildGrazeFloatMenuOption(Pawn pawn, Thing plant)
        {
            if (plant.def.ingestible == null || !plant.def.ingestible.showIngestFloatOption)
            {
                return null;
            }

            if (!plant.IngestibleNow || !pawn.RaceProps.CanEverEat(plant.def))
            {
                return null;
            }

            string label = plant.def.ingestible.ingestCommandString.NullOrEmpty()
                ? "ConsumeThing".Translate(plant.LabelShort, plant).ToString()
                : plant.def.ingestible.ingestCommandString.Formatted(plant.LabelShort).ToString();

            if (!plant.IsSociallyProper(pawn))
            {
                label = label + ": " + "ReservedForPrisoners".Translate().CapitalizeFirst();
            }
            else if (FoodUtility.MoodFromIngesting(pawn, plant, plant.def) < 0f)
            {
                label = string.Format("{0} ({1})", label, "WarningFoodDisliked".Translate());
            }

            if (!plant.def.ingestible.nonDrugIngestibleWithoutFoodNeed && !pawn.FoodIsSuitable(plant.def))
            {
                return new FloatMenuOption(label + ": " + "FoodNotSuitable".Translate().CapitalizeFirst(), null);
            }

            if (FoodUtility.InappropriateForTitle(plant.def, pawn, allowIfStarving: true))
            {
                return new FloatMenuOption(label + ": " + "FoodBelowTitleRequirements".Translate(pawn.royalty.MostSeniorTitle.def.GetLabelFor(pawn).CapitalizeFirst()).CapitalizeFirst(), null);
            }

            if (!pawn.CanReach(plant, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);
            }

            int maxAmountToGraze = MaxAmountToGraze(pawn, plant);
            FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(label, delegate
                {
                    int currentMaxAmountToGraze = MaxAmountToGraze(pawn, plant);
                    if (currentMaxAmountToGraze == 0)
                    {
                        return;
                    }

                    Job job = JobMaker.MakeJob(JobDefOf.Ingest, plant);
                    job.count = currentMaxAmountToGraze;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }),
                pawn,
                plant);

            if (maxAmountToGraze == 0)
            {
                option.action = null;
            }

            return option;
        }

        private static int MaxAmountToGraze(Pawn pawn, Thing plant)
        {
            return FoodUtility.GetMaxAmountToPickup(
                plant,
                pawn,
                FoodUtility.WillIngestStackCountOf(pawn, plant.def, FoodUtility.NutritionForEater(pawn, plant)));
        }
    }

    internal static class MugirlGrazeUtility
    {
        internal static bool IsMapPlantFood(Thing thing)
        {
            Plant plant = thing as Plant;
            return plant != null
                && plant.Spawned
                && !plant.Destroyed
                && plant.IngestibleNow
                && IsPlantFoodDef(plant.def);
        }

        internal static bool IsPlantFoodDef(ThingDef def)
        {
            return def != null && typeof(Plant).IsAssignableFrom(def.thingClass) && def.ingestible != null;
        }
    }
}
