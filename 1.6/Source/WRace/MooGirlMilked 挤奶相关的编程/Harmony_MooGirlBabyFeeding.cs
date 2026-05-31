using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public static class MooGirlBabyFeedingUtility
    {
        private const float MinBreastfeedNutrition = 0.031f;

        public static bool CanMooGirlBreastfeed(Pawn mom, out ChildcareUtility.BreastfeedFailReason? reason)
        {
            reason = null;
            if (!ModsConfig.BiotechActive)
            {
                reason = ChildcareUtility.BreastfeedFailReason.NoBiotechMod;
            }
            else if (mom == null)
            {
                reason = ChildcareUtility.BreastfeedFailReason.MomNull;
            }
            else if (mom.Dead)
            {
                reason = ChildcareUtility.BreastfeedFailReason.MomDead;
            }
            else if (mom.RaceProps == null || !mom.RaceProps.Humanlike)
            {
                reason = ChildcareUtility.BreastfeedFailReason.MomNotHumanLike;
            }
            else if (!HasMooMilkSource(mom, out var _))
            {
                reason = ChildcareUtility.BreastfeedFailReason.MomNotLactating;
            }
            return !reason.HasValue;
        }

        public static bool CanMooGirlBreastfeedNow(Pawn mom, out ChildcareUtility.BreastfeedFailReason? reason)
        {
            if (!CanMooGirlBreastfeed(mom, out reason))
            {
                return false;
            }
            if (!HasMooMilkSource(mom, out var milkComp) || AvailableMilkNutrition(milkComp) < MinBreastfeedNutrition)
            {
                reason = ChildcareUtility.BreastfeedFailReason.MomNotEnoughMilk;
            }
            else if (mom.InMentalState && !mom.Downed)
            {
                reason = ChildcareUtility.BreastfeedFailReason.MomInMentalState;
            }
            return !reason.HasValue;
        }

        public static bool HasMooMilkSource(Pawn pawn, out CompMooMilkable milkComp)
        {
            milkComp = pawn?.TryGetComp<CompMooMilkable>();
            return milkComp != null && milkComp.Active;
        }

        public static bool HasVanillaLactation(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Lactating) != null;
        }

        public static float AvailableMilkNutrition(CompMooMilkable milkComp)
        {
            if (milkComp == null)
            {
                return 0f;
            }
            return milkComp.Fullness * NutritionPerFullness(milkComp);
        }

        public static bool SuckleFromMooGirl(Pawn baby, Pawn feeder, CompMooMilkable milkComp, int delta)
        {
            if (baby?.needs?.food == null || feeder == null || milkComp == null)
            {
                return false;
            }

            float nutritionWanted = baby.needs.food.NutritionWanted;
            float desiredNutrition = Mathf.Min(baby.needs.food.MaxLevel / 5000f * (float)delta, nutritionWanted);
            float consumedNutrition = ConsumeMilkNutrition(milkComp, desiredNutrition);
            if (consumedNutrition <= 0f)
            {
                return false;
            }

            baby.needs.food.CurLevel += consumedNutrition;
            MooGirlNurtureUtility.AddBabyNurtureProgress(baby, feeder, delta);

            Caravan caravan = baby.GetCaravan();
            if (caravan != null && feeder.GetCaravan() == caravan)
            {
                feeder.mindState.BreastfeedCaravan(baby, consumedNutrition / baby.needs.food.MaxLevel);
            }
            baby.ideo?.IncreaseIdeoExposureIfBabyTick(feeder.Ideo);

            if (Mathf.Approximately(consumedNutrition, nutritionWanted))
            {
                return false;
            }
            return consumedNutrition >= desiredNutrition;
        }

        private static float ConsumeMilkNutrition(CompMooMilkable milkComp, float desiredNutrition)
        {
            if (desiredNutrition <= 0f)
            {
                return 0f;
            }

            float nutritionPerFullness = NutritionPerFullness(milkComp);
            float consumedNutrition = Mathf.Min(desiredNutrition, AvailableMilkNutrition(milkComp));
            if (consumedNutrition <= 0f)
            {
                return 0f;
            }

            milkComp.ConsumePercentage(consumedNutrition / nutritionPerFullness);
            return consumedNutrition;
        }

        private static float NutritionPerFullness(CompMooMilkable milkComp)
        {
            ThingDef milkDef = milkComp?.Props?.milkDef;
            if (milkDef == null)
            {
                return 10f;
            }

            return Mathf.Max(0.001f, milkDef.GetStatValueAbstract(StatDefOf.Nutrition) * 100f);
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeed))]
    public static class Harmony_ChildcareUtility_CanBreastfeed_MooGirl
    {
        public static void Postfix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (MooGirlBabyFeedingUtility.CanMooGirlBreastfeed(mom, out var _))
            {
                reason = null;
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeedNow))]
    public static class Harmony_ChildcareUtility_CanBreastfeedNow_MooGirl
    {
        public static void Postfix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
        {
            if (__result || !MooGirlBabyFeedingUtility.HasMooMilkSource(mom, out var _))
            {
                return;
            }

            if (MooGirlBabyFeedingUtility.CanMooGirlBreastfeedNow(mom, out var mooReason))
            {
                reason = null;
                __result = true;
            }
            else
            {
                reason = mooReason;
            }
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.SuckleFromLactatingPawn))]
    public static class Harmony_ChildcareUtility_SuckleFromLactatingPawn_MooGirl
    {
        public static bool Prefix(Pawn baby, Pawn feeder, int delta, ref bool __result)
        {
            if (!MooGirlBabyFeedingUtility.HasMooMilkSource(feeder, out var milkComp))
            {
                return true;
            }

            if (MooGirlBabyFeedingUtility.AvailableMilkNutrition(milkComp) <= 0f)
            {
                if (MooGirlBabyFeedingUtility.HasVanillaLactation(feeder))
                {
                    return true;
                }

                __result = false;
                return false;
            }

            __result = MooGirlBabyFeedingUtility.SuckleFromMooGirl(baby, feeder, milkComp, delta);
            return false;
        }
    }
}
