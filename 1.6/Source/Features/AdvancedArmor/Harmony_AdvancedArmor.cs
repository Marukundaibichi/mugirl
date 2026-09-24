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
        // 预编译的 struct 字段写入委托：直接 stfld 私有字段，避免每次射击装箱 __result 再反射写入。
        private static readonly SetDarknessOffsetDelegate SetDarknessOffset = CreateDarknessOffsetSetter();
        private delegate void SetDarknessOffsetDelegate(ref ShotReport report, float value);

        private static SetDarknessOffsetDelegate CreateDarknessOffsetSetter()
        {
            try
            {
                if (DarknessOffsetField == null)
                {
                    return null;
                }

                System.Reflection.Emit.DynamicMethod method = new System.Reflection.Emit.DynamicMethod(
                    "MugirlShotReportSetOffsetFromDarkness",
                    typeof(void),
                    new[] { typeof(ShotReport).MakeByRefType(), typeof(float) },
                    typeof(Harmony_NeuralHelmetDarknessAccuracy),
                    true);
                System.Reflection.Emit.ILGenerator il = method.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, DarknessOffsetField);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (SetDarknessOffsetDelegate)method.CreateDelegate(typeof(SetDarknessOffsetDelegate));
            }
            catch
            {
                return null;
            }
        }

        private static void Postfix(Thing caster, ref ShotReport __result)
        {
            if (!(caster is Pawn pawn) || !AdvancedArmorUtility.WearsNeuralHelmet(pawn))
            {
                return;
            }

            if (SetDarknessOffset != null)
            {
                SetDarknessOffset(ref __result, 0f);
            }
            else if (DarknessOffsetField != null)
            {
                object boxed = __result;
                DarknessOffsetField.SetValue(boxed, 0f);
                __result = (ShotReport)boxed;
            }
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
