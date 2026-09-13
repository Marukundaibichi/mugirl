using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public static class MugirlBabyFeedingUtility
    {
        private const float MinBreastfeedNutrition = 0.031f;

        public static bool CanMugirlBreastfeed(Pawn mom, out ChildcareUtility.BreastfeedFailReason? reason)
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

        public static bool CanMugirlBreastfeedNow(Pawn mom, out ChildcareUtility.BreastfeedFailReason? reason)
        {
            if (!CanMugirlBreastfeed(mom, out reason))
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
            return AvailableMilkNutrition(milkComp, out _);
        }

        internal static float AvailableMilkNutrition(CompMooMilkable milkComp, out float nutritionPerFullness)
        {
            nutritionPerFullness = 0f;
            if (milkComp == null)
            {
                return 0f;
            }

            nutritionPerFullness = NutritionPerFullness(milkComp);
            return Mathf.Max(0f, milkComp.Fullness) * nutritionPerFullness;
        }

        public static bool SuckleFromMugirl(Pawn baby, Pawn feeder, CompMooMilkable milkComp, int delta)
        {
            return SuckleFromMugirl(baby, feeder, milkComp, delta, null);
        }

        // 营养倍率只在本次喂食调用内传递，不跨 tick 缓存，保留其他 mod 动态修改属性的能力。
        internal static bool SuckleFromMugirl(Pawn baby, Pawn feeder, CompMooMilkable milkComp, int delta,
            float? nutritionPerFullness)
        {
            Need_Food food = baby?.needs?.food;
            if (food == null || feeder == null || milkComp == null || delta <= 0 || food.MaxLevel <= 0f)
            {
                return false;
            }

            float nutritionWanted = Mathf.Max(0f, food.NutritionWanted);
            float desiredNutrition = Mathf.Min(food.MaxLevel / 5000f * delta, nutritionWanted);
            float consumedNutrition = ConsumeMilkNutrition(milkComp, desiredNutrition, nutritionPerFullness);
            if (consumedNutrition <= 0f)
            {
                return false;
            }

            food.CurLevel = Mathf.Min(food.MaxLevel, food.CurLevel + consumedNutrition);
            MugirlNurtureUtility.AddBabyNurtureProgress(baby, feeder, delta);

            Caravan caravan = baby.GetCaravan();
            if (caravan != null && feeder.GetCaravan() == caravan && feeder.mindState != null)
            {
                feeder.mindState.BreastfeedCaravan(baby, Mathf.Clamp01(consumedNutrition / food.MaxLevel));
            }
            if (feeder.ideo != null)
            {
                baby.ideo?.IncreaseIdeoExposureIfBabyTick(feeder.Ideo);
            }

            if (Mathf.Approximately(consumedNutrition, nutritionWanted))
            {
                return false;
            }
            return consumedNutrition >= desiredNutrition;
        }

        private static float ConsumeMilkNutrition(CompMooMilkable milkComp, float desiredNutrition,
            float? knownNutritionPerFullness)
        {
            if (milkComp == null || desiredNutrition <= 0f)
            {
                return 0f;
            }

            float nutritionPerFullness = knownNutritionPerFullness ?? NutritionPerFullness(milkComp);
            if (nutritionPerFullness <= 0f)
            {
                return 0f;
            }

            float availableNutrition = Mathf.Max(0f, milkComp.Fullness) * nutritionPerFullness;
            float consumedNutrition = Mathf.Min(desiredNutrition, availableNutrition);
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
    public static class Harmony_ChildcareUtility_CanBreastfeed_Mugirl
    {
        public static void Postfix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (MugirlBabyFeedingUtility.CanMugirlBreastfeed(mom, out var _))
            {
                reason = null;
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeedNow))]
    public static class Harmony_ChildcareUtility_CanBreastfeedNow_Mugirl
    {
        public static void Postfix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
        {
            if (__result || !MugirlBabyFeedingUtility.HasMooMilkSource(mom, out var _))
            {
                return;
            }

            if (MugirlBabyFeedingUtility.CanMugirlBreastfeedNow(mom, out var mooReason))
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
    public static class Harmony_ChildcareUtility_SuckleFromLactatingPawn_Mugirl
    {
        public static bool Prefix(Pawn baby, Pawn feeder, int delta, ref bool __result)
        {
            if (!MugirlBabyFeedingUtility.HasMooMilkSource(feeder, out var milkComp))
            {
                return true;
            }

            if (MugirlBabyFeedingUtility.AvailableMilkNutrition(milkComp, out float nutritionPerFullness) <= 0f)
            {
                if (MugirlBabyFeedingUtility.HasVanillaLactation(feeder))
                {
                    return true;
                }

                __result = false;
                return false;
            }

            __result = MugirlBabyFeedingUtility.SuckleFromMugirl(baby, feeder, milkComp, delta, nutritionPerFullness);
            return false;
        }
    }
}
