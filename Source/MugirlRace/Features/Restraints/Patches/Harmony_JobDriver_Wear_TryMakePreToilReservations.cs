using HarmonyLib;

namespace Mugirl.Features.Restraints;

// 穿戴 Job 不能预留或执行会替换掉锁定束具的服装。
[HarmonyPatch(typeof(JobDriver_Wear), nameof(JobDriver_Wear.TryMakePreToilReservations))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Wear jobs can reserve apparel that would replace locked slave apparel.")]
public static class Harmony_JobDriver_Wear_TryMakePreToilReservations
{
    public static bool Prefix(JobDriver_Wear __instance, ref bool __result)
    {
        if (RestraintWearGuard.WouldReplaceLockedSlaveApparel(__instance.GetActor(), RestraintWearGuard.JobTargetApparel(__instance.job)))
        {
            __result = false;
            return false;
        }

        return true;
    }
}
