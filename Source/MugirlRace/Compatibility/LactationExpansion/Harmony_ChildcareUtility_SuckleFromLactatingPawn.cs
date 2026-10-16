using HarmonyLib;
using Mugirl.Features.Milk;

namespace Mugirl.Compatibility.LactationExpansion;

[HarmonyPriority(Priority.First)]
[HarmonyBefore(ModCompat.LactationExpansionHarmonyId)]
[HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.SuckleFromLactatingPawn))]
[MugirlPatch(PatchModule.LactationExpansion, PatchRisk.High, "Lactation Expansion may consume Mugirl milk sources through the vanilla Lactating hediff path.")]
internal static class Harmony_ChildcareUtility_SuckleFromLactatingPawn
{
    public static bool Prefix(Pawn baby, Pawn feeder, int delta, ref bool __result)
    {
        if (!ModCompat.LactationExpansion || !BabyFeedingUtility.HasMooMilkSource(feeder, out var milkComp))
        {
            return true;
        }

        if (BabyFeedingUtility.AvailableMilkNutrition(milkComp, out float nutritionPerFullness) <= 0f)
        {
            if (BabyFeedingUtility.HasVanillaLactation(feeder))
            {
                return true;
            }

            __result = false;
            return false;
        }

        __result = BabyFeedingUtility.SuckleFromMugirl(baby, feeder, milkComp, delta, nutritionPerFullness);
        return false;
    }
}
