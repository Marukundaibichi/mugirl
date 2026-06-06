using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(GridsUtility), nameof(GridsUtility.IsPolluted))]
    public static class GridsUtility_IsPolluted_Patch
    {
        public static bool Prefix(IntVec3 c, Map map, ref bool __result)
        {
            if (map == null || map.pollutionGrid == null)
            {
                __result = false;  // 或你认为合理的默认值
                return false;      // 跳过原函数，防止崩溃
            }
            return true;  // 继续执行原函数
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.TickLong))]
    public static class Plant_TickLong_Patch
    {
        public static bool Prefix(Plant __instance)
        {
            if (__instance.Map == null)
            {
                return false; // 跳过原方法
            }
            return true; // 继续执行原方法
        }
    }


}
