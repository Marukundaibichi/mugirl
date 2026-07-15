using System;
using System.Reflection;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class RunAndGunCompatibility
    {
        private const string LegacyPackageId = "roolo.RunAndGun.kotobike";
        private const string ContinuedPackageId = "MemeGoddess.RunAndGun";

        internal static bool Active => ModLister.GetActiveModWithIdentifier(LegacyPackageId) != null
            || ModLister.GetActiveModWithIdentifier(ContinuedPackageId) != null;

        internal static bool IsMovingCast(Pawn pawn)
        {
            if (pawn?.CurJobDef != JobDefOf.Goto || ReflectionCache.CompType == null
                || (ReflectionCache.EnabledField == null && ReflectionCache.EnabledProperty == null))
            {
                return false;
            }

            string stanceTypeName = pawn.stances?.curStance?.GetType().Name;
            if (stanceTypeName == "Stance_RunAndGun" || stanceTypeName == "Stance_RunAndGun_Cooldown")
            {
                return true;
            }

            if (pawn.AllComps == null)
            {
                return false;
            }

            for (int i = 0; i < pawn.AllComps.Count; i++)
            {
                ThingComp comp = pawn.AllComps[i];
                if (comp == null || !ReflectionCache.CompType.IsInstanceOfType(comp))
                {
                    continue;
                }

                object enabledValue = ReflectionCache.EnabledProperty != null
                    ? ReflectionCache.EnabledProperty.GetValue(comp, null)
                    : ReflectionCache.EnabledField.GetValue(comp);
                return enabledValue is bool enabled && enabled;
            }
            return false;
        }

        private static class ReflectionCache
        {
            // StaticCacheLifecycle: 仅在任一 RunAndGun 版本已启用且资格补丁执行时初始化；不持有游戏对象。
            internal static readonly Type CompType = AccessTools.TypeByName("RunAndGun.CompRunAndGun");
            internal static readonly FieldInfo EnabledField = CompType == null
                ? null
                : AccessTools.Field(CompType, "isEnabled");
            internal static readonly PropertyInfo EnabledProperty = CompType == null
                ? null
                : AccessTools.Property(CompType, "isEnabled");
        }
    }

    [HarmonyPatch(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.IsFullFirepowerEligible))]
    internal static class Harmony_RunAndGun_WeaponWheelFullFirepower
    {
        public static bool Prepare()
        {
            return RunAndGunCompatibility.Active;
        }

        public static void Postfix(Comp_WeaponWheel __instance, ref bool __result)
        {
            if (__result && RunAndGunCompatibility.IsMovingCast(__instance?.Pawn))
            {
                __result = false;
            }
        }
    }
}
