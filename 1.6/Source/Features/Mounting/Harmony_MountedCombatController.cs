using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    [HarmonyPatch(typeof(Verb), nameof(Verb.WarmupTime), MethodType.Getter)]
    public static class Harmony_MountedCombatController_WarmupTime
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            MountedCombatController.TryGetWarmupTimeOverride(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Stance_Warmup), nameof(Stance_Warmup.StanceDraw))]
    public static class Harmony_MountedCombatController_StanceWarmupDraw
    {
        public static bool Prefix(Stance_Warmup __instance)
        {
            return !MountedCombatController.IsMountedVerb(__instance?.verb);
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
    public static class Harmony_MountedCombatController_EquipmentRemoved
    {
        public static void Prefix(ThingWithComps eq)
        {
            MountedCombatController.NotifyEquipmentRemoved(eq);
        }
    }
}
