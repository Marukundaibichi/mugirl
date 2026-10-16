using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Roping;

// 替换原版 RopingTick 的两种情形（1.6 与 1.7 原版方法体逐行一致，已比对）：
// 1. 被牵引者是雪牛娘：跟随由 JobDriver_FollowRoper 驱动，饥饿时可挣脱固定拴点，且不因征召断绳（改为取消征召）；
// 2. 牵引者只牵着雪牛娘：自定义跟随允许牵引者继续普通工作，不能沿用原版 IsStillDoingRopingJob 断绳检查。
// 混合牵引普通动物时交回原版，避免自定义行为外溢。返回 false 会跳过原方法及其他模组对它的 transpiler/infix。
[HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.RopingTick))]
[MugirlPatch(PatchModule.Roping, PatchRisk.Critical, "Custom Mugirl-only roping tick falls back to vanilla roping behavior.")]
public static class Harmony_Pawn_RopeTracker_RopingTick
{
    // 每个生成的 Pawn 每 tick 调用；没有绳子时立即交回原版。
    public static bool Prefix(Pawn_RopeTracker __instance, Pawn ___pawn)
    {
        if (!__instance.HasAnyRope)
        {
            return true;
        }

        Pawn pawn = ___pawn;
        if (ShouldUseMugirlRopeeTick(pawn, __instance))
        {
            TickMugirlRopee(__instance, pawn);
            return false;
        }

        if (!RopingService.HasOnlyMugirlRopees(__instance))
        {
            return true;
        }

        if (ShouldBreakAllRopes(pawn, __instance))
        {
            EndCustomFollowJobs(pawn, __instance.Ropees);
            __instance.BreakAllRopes();
            return false;
        }

        if (__instance.RopedTo.IsValid && !pawn.CanReach(__instance.RopedTo, PathEndMode.Touch, Danger.Deadly))
        {
            RopeTrackerAccess.BreakRopeWithRoper(__instance);
            RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
        }

        if (__instance.IsRopedToHitchingPost && !__instance.RopedToHitchingSpot.Spawned)
        {
            __instance.UnropeFromSpot();
            RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
        }

        return false;
    }

    private static bool ShouldUseMugirlRopeeTick(Pawn pawn, Pawn_RopeTracker tracker)
    {
        return (tracker.IsRopedByPawn || tracker.IsRopedToSpot) && RopingService.IsMugirlRopee(pawn);
    }

    private static void TickMugirlRopee(Pawn_RopeTracker tracker, Pawn pawn)
    {
        if (pawn.drafter?.Drafted == true)
        {
            pawn.drafter.Drafted = false;
        }

        if (ShouldBreakAllRopes(pawn, tracker))
        {
            tracker.BreakAllRopes();
            RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
            return;
        }

        if (tracker.RopedTo.IsValid && !pawn.CanReach(tracker.RopedTo, PathEndMode.Touch, Danger.Deadly))
        {
            RopeTrackerAccess.BreakRopeWithRoper(tracker);
            RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
            return;
        }

        if (tracker.IsRopedToHitchingPost && !tracker.RopedToHitchingSpot.Spawned)
        {
            tracker.UnropeFromSpot();
            RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
        }
    }

    // 与原版断绳条件相同，但不含征召（雪牛娘被取消征召）与 IsStillDoingRopingJob；另加饥饿挣脱固定拴点。
    private static bool ShouldBreakAllRopes(Pawn pawn, Pawn_RopeTracker tracker)
    {
        return pawn.Dead
            || pawn.Downed
            || (!pawn.Awake() && tracker.IsRopedByPawn)
            || (pawn.InMentalState && pawn.MentalStateDef != MentalStateDefOf.Roaming)
            || ShouldBreakSpotRopeDueToStarvation(pawn, tracker)
            || pawn.IsBurning();
    }

    // 饥饿只允许雪牛娘挣脱固定拴点；被 Pawn 牵引时仍保留绳索。
    private static bool ShouldBreakSpotRopeDueToStarvation(Pawn pawn, Pawn_RopeTracker tracker)
    {
        return tracker.IsRopedToSpot
            && RopingService.IsMugirlRopee(pawn)
            && pawn.needs?.food?.CurCategory == HungerCategory.Starving;
    }

    // 牵引者断开全部绳子前，先结束正在跟随它的雪牛娘的跟随 Job。
    private static void EndCustomFollowJobs(Pawn owner, List<Pawn> ropees)
    {
        for (int i = ropees.Count - 1; i >= 0; i--)
        {
            Pawn ropee = ropees[i];
            if (RopingService.IsMugirlRopee(ropee)
                && RopingService.IsFollowingRoper(ropee)
                && ropee.jobs is not null
                && ropee.CurJob?.targetA.Thing == owner)
            {
                ropee.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }
}
