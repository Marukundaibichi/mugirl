using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Restraints;

// TryUnequipSomething 是私有方法，只能按名字指定（已核对 1.7）。原版在穿戴 toil 的每个 tickIntervalAction 调用它。
[HarmonyPatch(typeof(JobDriver_Wear), "TryUnequipSomething")]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Wear jobs can proceed to unequip locked slave apparel or trigger slave-apparel auto-stage callbacks while making room for new apparel.")]
public static class Harmony_JobDriver_Wear_TryUnequipSomething
{
    public static bool Prefix(JobDriver_Wear __instance, out bool __state)
    {
        __state = false;
        if (RestraintWearGuard.WouldReplaceLockedSlaveApparel(__instance.GetActor(), RestraintWearGuard.JobTargetApparel(__instance.job)))
        {
            __instance.EndJobWith(JobCondition.Incompletable);
            return false;
        }

        // JobDriver_Wear 会先单独卸下冲突服装，之后才调用 ApparelTracker.Wear；
        // 因此这里也要覆盖这段替换窗口。
        RestraintAutoStageContext.BeginSuppressNextStage();
        __state = true;
        return true;
    }

    public static void Finalizer(bool __state)
    {
        if (__state)
        {
            RestraintAutoStageContext.EndSuppressNextStage();
        }
    }
}
