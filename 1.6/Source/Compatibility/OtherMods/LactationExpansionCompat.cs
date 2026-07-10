using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class LactationExpansionCompatibility
    {
        internal const string HarmonyId = "EuterpeMilkyTitfuck.LactationExpansion";
        private const string PackageId = "Euterpe.MilkyTitfuck";

        private static bool? active;

        internal static bool Active
        {
            get
            {
                if (!active.HasValue)
                {
                    active = ModLister.GetActiveModWithIdentifier(PackageId) != null;
                }

                return active.Value;
            }
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeedNow))]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore(LactationExpansionCompatibility.HarmonyId)]
    internal static class Harmony_LactationExpansion_CanBreastfeedNow_Mugirl
    {
        public static bool Prefix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
        {
            if (!LactationExpansionCompatibility.Active || !MugirlBabyFeedingUtility.HasMooMilkSource(mom, out var _))
            {
                return true;
            }

            __result = MugirlBabyFeedingUtility.CanMugirlBreastfeedNow(mom, out reason);
            return false;
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.SuckleFromLactatingPawn))]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore(LactationExpansionCompatibility.HarmonyId)]
    internal static class Harmony_LactationExpansion_SuckleFromLactatingPawn_Mugirl
    {
        public static bool Prefix(Pawn baby, Pawn feeder, int delta, ref bool __result)
        {
            if (!LactationExpansionCompatibility.Active || !MugirlBabyFeedingUtility.HasMooMilkSource(feeder, out var milkComp))
            {
                return true;
            }

            if (MugirlBabyFeedingUtility.AvailableMilkNutrition(milkComp) <= 0f)
            {
                if (MugirlBabyFeedingUtility.HasVanillaLactation(feeder))
                {
                    return true;
                }

                __result = false;
                return false;
            }

            __result = MugirlBabyFeedingUtility.SuckleFromMugirl(baby, feeder, milkComp, delta);
            return false;
        }
    }
}
