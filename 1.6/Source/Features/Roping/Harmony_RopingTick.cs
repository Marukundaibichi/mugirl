using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_RopeTracker), "RopingTick")]
    public static class Patch_RopingTick
    {
        // 反射字段只缓存一次，所有绳索 tracker patch 共用。
        // StaticCacheLifecycle: 进程级 Pawn_RopeTracker 内部成员反射缓存；不持有游戏对象。
        private static readonly FieldInfo pawnField = AccessTools.Field(typeof(Pawn_RopeTracker), "pawn");
        private static readonly MethodInfo breakRopeWithRoperMethod =
            AccessTools.Method(typeof(Pawn_RopeTracker), "BreakRopeWithRoper");

        internal static Pawn PawnFor(Pawn_RopeTracker tracker)
        {
            return tracker == null || pawnField == null ? null : pawnField.GetValue(tracker) as Pawn;
        }

        private static void EndCustomFollowJobs(Pawn owner, List<Pawn> ropees)
        {
            if (owner == null || ropees == null)
            {
                return;
            }

            for (int i = ropees.Count - 1; i >= 0; i--)
            {
                Pawn ropee = ropees[i];
                if (RopingService.IsMooGirlRopee(ropee) &&
                    RopingService.IsFollowingRoper(ropee) &&
                    ropee.jobs != null &&
                    ropee.CurJob?.targetA.Thing == owner)
                {
                    ropee.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        private static void ForceUnrope(Pawn_RopeTracker tracker)
        {
            Pawn owner = PawnFor(tracker);
            EndCustomFollowJobs(owner, tracker.Ropees);
            tracker.BreakAllRopes();
        }

        private static void BreakRopeWithRoper(Pawn_RopeTracker tracker)
        {
            if (breakRopeWithRoperMethod == null)
            {
                MooGirlLog.WarningOnce(
                    "RopingTick.BreakRopeWithRoperMissing",
                    "MooGirl.RopingTick.BreakRopeWithRoperMissing".Translate().ToString());
                tracker.BreakAllRopes();
                return;
            }

            breakRopeWithRoperMethod.Invoke(tracker, null);
        }

        private static bool ShouldBreakAllRopes(Pawn pawn, Pawn_RopeTracker tracker)
        {
            return pawn.Dead ||
                pawn.Downed ||
                (!pawn.Awake() && tracker.IsRopedByPawn) ||
                ShouldDropRopesDueToMentalState(pawn) ||
                pawn.IsBurning();
        }

        private static bool ShouldUseMooGirlRopeeTick(Pawn pawn, Pawn_RopeTracker tracker)
        {
            return RopingService.IsMooGirlRopee(pawn) && (tracker.IsRopedByPawn || tracker.IsRopedToSpot);
        }

        private static void ClearDraftedRopee(Pawn pawn)
        {
            if (pawn?.drafter?.Drafted == true)
            {
                pawn.drafter.Drafted = false;
            }
        }

        private static void TickMooGirlRopee(Pawn_RopeTracker tracker, Pawn pawn)
        {
            ClearDraftedRopee(pawn);

            if (ShouldBreakAllRopes(pawn, tracker))
            {
                tracker.BreakAllRopes();
                RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
                return;
            }

            if (tracker.RopedTo.IsValid &&
                !pawn.CanReach(tracker.RopedTo, PathEndMode.Touch, Danger.Deadly))
            {
                BreakRopeWithRoper(tracker);
                RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
                return;
            }

            if (tracker.IsRopedToHitchingPost && !tracker.RopedToHitchingSpot.Spawned)
            {
                tracker.UnropeFromSpot();
                RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
            }
        }

        private static bool ShouldDropRopesDueToMentalState(Pawn pawn)
        {
            return pawn.InMentalState && pawn.MentalStateDef != MentalStateDefOf.Roaming;
        }

        public static bool Prefix(Pawn_RopeTracker __instance)
        {
            Pawn pawn = PawnFor(__instance);
            if (pawn == null)
            {
                return true;
            }

            if (ShouldUseMooGirlRopeeTick(pawn, __instance))
            {
                TickMooGirlRopee(__instance, pawn);
                return false;
            }

            if (!RopingService.HasOnlyMooGirlRopees(__instance))
            {
                return true;
            }

            RopingService.RefreshRoperFromTracker(pawn);

            // 这是本 patch 唯一替代原版 RopingTick 的场景：牵引者只牵着雪牛娘时，
            // 自定义跟随允许牵引者继续普通工作，因此不能沿用原版 IsStillDoingRopingJob
            // 断绳检查。混合牵引普通 pawn 时交回原版，避免自定义行为外溢。
            if (ShouldBreakAllRopes(pawn, __instance))
            {
                ForceUnrope(__instance);
                return false;
            }

            if (__instance.RopedTo.IsValid &&
                !pawn.CanReach(__instance.RopedTo, PathEndMode.Touch, Danger.Deadly))
            {
                BreakRopeWithRoper(__instance);
                RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
            }

            if (__instance.IsRopedToHitchingPost && !__instance.RopedToHitchingSpot.Spawned)
            {
                __instance.UnropeFromSpot();
                RopingService.NotifyPawnNoLongerRopedToTarget(pawn);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.RopePawn))]
    public static class Patch_RopeTracker_RopePawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_RopeTracker __instance, Pawn ropee)
        {
            RopingService.RegisterPawnRope(Patch_RopingTick.PawnFor(__instance), ropee);
        }
    }

    [HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.RopeToSpot))]
    public static class Patch_RopeTracker_RopeToSpot
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_RopeTracker __instance)
        {
            RopingService.RegisterRopedToSpot(Patch_RopingTick.PawnFor(__instance));
        }
    }

    [HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.BreakAllRopes))]
    public static class Patch_RopeTracker_BreakAllRopes
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_RopeTracker __instance)
        {
            RopingService.NotifyBreakAllRopes(Patch_RopingTick.PawnFor(__instance));
        }
    }

    [HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.DropRope))]
    public static class Patch_RopeTracker_DropRope
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ropee)
        {
            RopingService.NotifyPawnNoLongerRopedToTarget(ropee);
        }
    }

    [HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.UnropeFromSpot))]
    public static class Patch_RopeTracker_UnropeFromSpot
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_RopeTracker __instance)
        {
            RopingService.NotifyPawnNoLongerRopedToTarget(Patch_RopingTick.PawnFor(__instance));
        }
    }
}
