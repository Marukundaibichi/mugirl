using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class AdvancedArmorUtility
    {
        internal static bool Wears(Pawn pawn, ThingDef apparelDef)
        {
            if (pawn?.apparel?.WornApparel == null || apparelDef == null)
            {
                return false;
            }

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                if (pawn.apparel.WornApparel[i].def == apparelDef)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool WearsNeuralArmor(Pawn pawn)
        {
            return Wears(pawn, Mugirl_DefOf.Mugirl_NeuralCombatArmor);
        }

        internal static bool WearsNeuralHelmet(Pawn pawn)
        {
            return Wears(pawn, Mugirl_DefOf.Mugirl_NeuralAmplifierHelmet);
        }
    }

    [HarmonyPatch(typeof(StaggerHandler), nameof(StaggerHandler.Notify_BulletImpact))]
    internal static class Harmony_NeuralArmorBulletStagger
    {
        private static bool Prefix(StaggerHandler __instance)
        {
            return !AdvancedArmorUtility.WearsNeuralArmor(__instance?.parent);
        }
    }

    [HarmonyPatch(typeof(ThoughtWorker_PsychicDrone), "CurrentStateInternal")]
    internal static class Harmony_NeuralHelmetPsychicDrone
    {
        private static bool Prefix(Pawn p, ref ThoughtState __result)
        {
            if (!AdvancedArmorUtility.WearsNeuralHelmet(p))
            {
                return true;
            }

            __result = ThoughtState.Inactive;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    internal static class Harmony_NeuralHelmetDarknessAccuracy
    {
        // StaticCacheLifecycle: immutable reflection metadata for the lifetime of the process; no game objects are retained.
        private static readonly FieldInfo DarknessOffsetField = AccessTools.Field(typeof(ShotReport), "offsetFromDarkness");

        private static void Postfix(Thing caster, ref ShotReport __result)
        {
            if (!(caster is Pawn pawn) || !AdvancedArmorUtility.WearsNeuralHelmet(pawn) || DarknessOffsetField == null)
            {
                return;
            }

            object boxed = __result;
            DarknessOffsetField.SetValue(boxed, 0f);
            __result = (ShotReport)boxed;
        }
    }

    [HarmonyPatch(typeof(GameCondition_UnnaturalDarkness), nameof(GameCondition_UnnaturalDarkness.AffectedByDarkness))]
    internal static class Harmony_NeuralHelmetUnnaturalDarkness
    {
        private static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && AdvancedArmorUtility.WearsNeuralHelmet(pawn))
            {
                __result = false;
            }
        }
    }
}
