using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    internal static class MugirlEventUtility
    {
        private const string MigrationTag = "Mugirl_Migration";
        private const string RunawayTag = "Mugirl_RunawayFarm";
        private const string RunawayRewardTag = "Mugirl_RunawayReward";
        private const string FusionInvestorTag = "Mugirl_FusionInvestor";

        internal static bool IsMigrationPawn(Pawn pawn)
        {
            return HasTag(pawn, MigrationTag);
        }

        internal static bool IsRunawayFarmPawn(Pawn pawn)
        {
            return HasTag(pawn, RunawayTag);
        }

        internal static bool IsRunawayRewardPawn(Pawn pawn)
        {
            return HasTag(pawn, RunawayRewardTag);
        }

        internal static bool IsFusionInvestor(Pawn pawn)
        {
            return HasTag(pawn, FusionInvestorTag);
        }

        internal static bool IsPassiveEventPawn(Pawn pawn)
        {
            return IsMigrationPawn(pawn) || IsRunawayFarmPawn(pawn);
        }

        internal static bool CanUseTemporaryMilk(Pawn pawn)
        {
            return IsMigrationPawn(pawn);
        }

        internal static void WearBikiniOnly(Pawn pawn)
        {
            EnsureBikiniOnly(pawn);
        }

        internal static void EnsureBikiniOnly(Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return;
            }

            ThingDef bikiniDef = MugirlContentDefOf.Mugirl_Bikini;
            if (bikiniDef == null)
            {
                return;
            }

            Apparel wornBikini = null;
            System.Collections.Generic.List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                Apparel removedApparel = wornApparel[i];
                if (removedApparel?.def == bikiniDef && wornBikini == null)
                {
                    wornBikini = removedApparel;
                    pawn.apparel.Lock(wornBikini);
                    continue;
                }

                pawn.apparel.Unlock(removedApparel);
                pawn.apparel.Remove(removedApparel);
                removedApparel.Destroy(DestroyMode.Vanish);
            }

            if (wornBikini != null)
            {
                return;
            }

            ThingDef stuff = bikiniDef.MadeFromStuff ? GenStuff.RandomStuffFor(bikiniDef) : null;
            Apparel apparel = ThingMaker.MakeThing(bikiniDef, stuff) as Apparel;
            if (apparel != null)
            {
                pawn.apparel.Wear(apparel, dropReplacedApparel: false, locked: true);
            }
        }

        internal static void MarkMigrationPawn(Pawn pawn)
        {
            AddTag(pawn, MigrationTag);
        }

        internal static void MarkRunawayFarmPawn(Pawn pawn)
        {
            AddTag(pawn, RunawayTag);
        }

        internal static void MarkRunawayRewardPawn(Pawn pawn)
        {
            AddTag(pawn, RunawayRewardTag);
        }

        internal static void MarkFusionInvestor(Pawn pawn)
        {
            AddTag(pawn, FusionInvestorTag);
        }

        internal static void ClearFusionInvestor(Pawn pawn)
        {
            RemoveTag(pawn, FusionInvestorTag);
        }

        internal static void ClearTemporaryEventTags(Pawn pawn)
        {
            bool hadTemporaryEventTag = IsPassiveEventPawn(pawn);
            RemoveTag(pawn, MigrationTag);
            RemoveTag(pawn, RunawayTag);
            if (hadTemporaryEventTag && !IsPassiveEventPawn(pawn))
            {
                UnlockEventBikini(pawn);
            }
        }

        internal static void ClearRunawayPanic(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || MugirlContentDefOf.Mugirl_RunawayPanic == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MugirlContentDefOf.Mugirl_RunawayPanic);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        internal static void PreparePassiveWildPawn(Pawn pawn)
        {
            if (pawn?.mindState == null)
            {
                return;
            }

            pawn.mindState.canFleeIndividual = false;
            pawn.mindState.Active = true;
        }

        private static bool HasTag(Pawn pawn, string tag)
        {
            return pawn?.questTags != null && pawn.questTags.Contains(tag);
        }

        private static void AddTag(Pawn pawn, string tag)
        {
            if (pawn == null || tag.NullOrEmpty())
            {
                return;
            }

            if (pawn.questTags == null)
            {
                pawn.questTags = new System.Collections.Generic.List<string>();
            }

            if (!pawn.questTags.Contains(tag))
            {
                pawn.questTags.Add(tag);
            }
        }

        private static void RemoveTag(Pawn pawn, string tag)
        {
            pawn?.questTags?.Remove(tag);
        }

        private static void UnlockEventBikini(Pawn pawn)
        {
            if (pawn?.apparel == null || MugirlContentDefOf.Mugirl_Bikini == null)
            {
                return;
            }

            System.Collections.Generic.List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def == MugirlContentDefOf.Mugirl_Bikini)
                {
                    pawn.apparel.Unlock(apparel);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MindState), "CheckStartMentalStateBecauseRecruitAttempted")]
    internal static class PawnMindState_CheckStartMentalStateBecauseRecruitAttempted_MugirlEvents_Patch
    {
        public static bool Prefix(Pawn_MindState __instance, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            if (!MugirlEventUtility.IsPassiveEventPawn(pawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.Notify_DamageTaken))]
    internal static class PawnMindState_NotifyDamageTaken_MugirlEvents_Patch
    {
        public static bool Prefix(Pawn_MindState __instance)
        {
            Pawn pawn = __instance.pawn;
            return !MugirlEventUtility.IsPassiveEventPawn(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ShouldShowQuestionMark))]
    internal static class Pawn_ShouldShowQuestionMark_MugirlFusionInvestor_Patch
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!__result && MugirlFusionInvestmentUtility.CanTalkToInvestor(__instance))
            {
                __result = true;
            }
        }
    }
}
