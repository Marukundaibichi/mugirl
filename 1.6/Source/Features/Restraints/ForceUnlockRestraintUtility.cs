using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    internal static class ForceUnlockRestraintUtility
    {
        internal const int WorkTicks = 3600;
        internal const int WoundCheckIntervalTicks = 600;
        internal const float WoundChancePerInterval = 0.2f;

        private const float RoughRequirementBodySize = 1f;
        private const float FineRequirementBodySize = 1f;
        private const float RoughRequirementManipulation = 1f;
        private const float FineRequirementManipulation = 1f;
        private const int RoughRequirementMelee = 10;
        private const int FineRequirementMelee = 14;
        private const float BodySizeWeight = 1f;
        private const float MeleeSkillWeight = 0.3f;
        private const float ManipulationWeight = 2f;
        private const string RoughKeyDefName = "Mugirl_SlaveApparelKey_Medieval";
        private const string FineKeyDefName = "Mugirl_SlaveApparelKey_Industrial";

        internal static ForceUnlockRestraintTier GetTier(Apparel apparel)
        {
            SlaveApparel slaveApparel = apparel as SlaveApparel;
            if (slaveApparel?.SlaveDef?.keytype == null)
            {
                return ForceUnlockRestraintTier.None;
            }

            string keyDefName = slaveApparel.SlaveDef.keytype.defName;
            if (keyDefName == RoughKeyDefName)
            {
                return ForceUnlockRestraintTier.Rough;
            }

            if (keyDefName == FineKeyDefName)
            {
                return ForceUnlockRestraintTier.Fine;
            }

            return ForceUnlockRestraintTier.None;
        }

        internal static bool IsForceUnlockable(Apparel apparel)
        {
            return GetTier(apparel) != ForceUnlockRestraintTier.None;
        }

        internal static bool IsLockedForceUnlockable(Pawn wearer, Apparel apparel)
        {
            SlaveApparel slaveApparel = apparel as SlaveApparel;
            return wearer?.apparel != null
                && apparel != null
                && slaveApparel != null
                && slaveApparel.isLocked
                && wearer.apparel.WornApparel.Contains(apparel)
                && IsForceUnlockable(apparel);
        }

        internal static bool HasAnyLockedForceUnlockable(Pawn wearer)
        {
            if (wearer?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = wearer.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (IsLockedForceUnlockable(wearer, wornApparel[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal static float GetStrengthLevel(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }

            return CalculateStrength(GetBodySize(pawn), GetManipulation(pawn), GetMeleeLevel(pawn));
        }

        internal static float GetRequiredStrength(Apparel apparel)
        {
            ForceUnlockRestraintTier tier = GetTier(apparel);
            if (tier == ForceUnlockRestraintTier.Rough)
            {
                return CalculateStrength(RoughRequirementBodySize, RoughRequirementManipulation, RoughRequirementMelee);
            }

            if (tier == ForceUnlockRestraintTier.Fine)
            {
                return CalculateStrength(FineRequirementBodySize, FineRequirementManipulation, FineRequirementMelee);
            }

            return float.MaxValue;
        }

        internal static bool HasRequiredStrength(Pawn pawn, Apparel apparel)
        {
            return GetStrengthLevel(pawn) >= GetRequiredStrength(apparel);
        }

        internal static string RequirementSummary(Apparel apparel)
        {
            ForceUnlockRestraintTier tier = GetTier(apparel);
            if (tier == ForceUnlockRestraintTier.Rough)
            {
                return "Mugirl.Restraints.ForceUnlock.RequirementRough".Translate();
            }

            if (tier == ForceUnlockRestraintTier.Fine)
            {
                return "Mugirl.Restraints.ForceUnlock.RequirementFine".Translate();
            }

            return "Mugirl.Restraints.ForceUnlock.RequirementUnsupported".Translate();
        }

        internal static string StrengthText(float strength)
        {
            return strength.ToString("0.0");
        }

        internal static bool CanReserveAndReachTarget(Pawn actor, Pawn target)
        {
            if (actor == null || target == null)
            {
                return false;
            }

            return actor == target || actor.CanReserveAndReach(target, PathEndMode.Touch, Danger.Some);
        }

        private static float CalculateStrength(float bodySize, float manipulation, int meleeLevel)
        {
            return (bodySize * BodySizeWeight)
                + (meleeLevel * MeleeSkillWeight)
                + (manipulation * ManipulationWeight);
        }

        private static float GetBodySize(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }

            float bodySize = pawn.BodySize;
            return bodySize < 0f ? 0f : bodySize;
        }

        private static float GetManipulation(Pawn pawn)
        {
            float manipulation = pawn?.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 0f;
            return manipulation < 0f ? 0f : manipulation;
        }

        private static int GetMeleeLevel(Pawn pawn)
        {
            return pawn?.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
        }
    }

    internal enum ForceUnlockRestraintTier
    {
        None,
        Rough,
        Fine
    }
}
