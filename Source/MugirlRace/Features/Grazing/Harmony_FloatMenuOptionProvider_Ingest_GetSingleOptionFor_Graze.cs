using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Grazing;

// 雪牛娘右键地图植物时的“吃”选项。复刻 1.7 原版 FloatMenuOptionProvider_Ingest.GetSingleOptionFor（植物不是药物，相关分支省略），
// 有意的差异：寻路用 PathEndMode.Touch（与吃草准备流程一致）、适口判断用 WillEat、下令时不调用 SetForbidden(false)（植物不可禁用）。
// 原版该方法更新后需要重新对照。
[HarmonyPatch(typeof(FloatMenuOptionProvider_Ingest), "GetSingleOptionFor", typeof(Thing), typeof(FloatMenuContext))]
[MugirlPatch(PatchModule.Grazing, PatchRisk.High, "Manual Mugirl plant-ingest orders use the base menu action, which tries to unforbid non-forbiddable plants.")]
public static class Harmony_FloatMenuOptionProvider_Ingest_GetSingleOptionFor_Graze
{
    public static bool Prefix(Thing clickedThing, FloatMenuContext? context, ref FloatMenuOption? __result)
    {
        Pawn? pawn = context?.FirstSelectedPawn;
        if (!ShouldUseGrazeFloatMenu(pawn, clickedThing))
        {
            return true;
        }

        __result = BuildGrazeFloatMenuOption(pawn, clickedThing);
        return false;
    }

    private static bool ShouldUseGrazeFloatMenu([NotNullWhen(true)] Pawn? pawn, [NotNullWhen(true)] Thing? thing)
    {
        return pawn is not null
            && thing?.Map == pawn.Map
            && MugirlIdentity.IsMugirlPawn(pawn)
            && GrazeUtility.IsMapPlantFood(thing);
    }

    private static FloatMenuOption? BuildGrazeFloatMenuOption(Pawn pawn, Thing plant)
    {
        if (plant.def.ingestible is not { showIngestFloatOption: true } ingestible)
        {
            return null;
        }

        if (!plant.IngestibleNow || !pawn.RaceProps.CanEverEat(plant.def))
        {
            return null;
        }

        string label = ingestible.ingestCommandString.NullOrEmpty()
            ? "ConsumeThing".Translate(plant.LabelShort, plant).ToString()
            : ingestible.ingestCommandString.Formatted(plant.LabelShort).ToString();

        if (!plant.IsSociallyProper(pawn))
        {
            label = label + ": " + "ReservedForPrisoners".Translate().CapitalizeFirst();
        }
        else if (FoodUtility.MoodFromIngesting(pawn, plant, plant.def) < 0f)
        {
            label = $"{label} ({"WarningFoodDisliked".Translate()})";
        }

        if (!ingestible.nonDrugIngestibleWithoutFoodNeed && !pawn.WillEat(plant, pawn))
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
