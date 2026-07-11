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

        internal static bool ClearMigrationPawn(Pawn pawn)
        {
            bool hadMigrationTag = IsMigrationPawn(pawn);
            RemoveTag(pawn, MigrationTag);
            return hadMigrationTag;
        }

        internal static int ClearPlayerMigrationPawns()
        {
            int cleared = 0;
            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (pawn != null && MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction) && ClearMigrationPawn(pawn))
                {
                    cleared++;
                }
            }

            return cleared;
        }

        internal static void ClearTemporaryEventTags(Pawn pawn)
        {
            RemoveTag(pawn, MigrationTag);
            RemoveTag(pawn, RunawayTag);
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
            if (MugirlEventUtility.IsMigrationPawn(pawn))
            {
                MapComponent_MugirlMigration.NotifyMigrationPawnAttacked(pawn);
            }

            return !MugirlEventUtility.IsPassiveEventPawn(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), "PreApplyDamage")]
    internal static class Pawn_PreApplyDamage_MugirlMigrationFlee_Patch
    {
        public static void Prefix(Pawn __instance)
        {
            if (MugirlEventUtility.IsMigrationPawn(__instance))
            {
                MapComponent_MugirlMigration.NotifyMigrationPawnAttacked(__instance);
            }
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
