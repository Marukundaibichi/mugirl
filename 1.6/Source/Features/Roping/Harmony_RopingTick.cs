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
        private static readonly FieldInfo pawnField = AccessTools.Field(typeof(Pawn_RopeTracker), "pawn");

        internal static Pawn PawnFor(Pawn_RopeTracker tracker)
        {
            return tracker == null ? null : (Pawn)pawnField.GetValue(tracker);
        }

        private static void EndRopingJobs(Pawn owner, List<Pawn> ropees)
        {
            owner?.jobs?.EndCurrentJob(JobCondition.InterruptForced);

            if (ropees == null)
            {
                return;
            }

            for (int i = 0; i < ropees.Count; i++)
            {
                ropees[i]?.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private static void ForceUnrope(Pawn_RopeTracker tracker)
        {
            Pawn owner = PawnFor(tracker);
            List<Pawn> ropees = tracker.Ropees == null ? null : new List<Pawn>(tracker.Ropees);

            tracker.BreakAllRopes();
            RopingService.NotifyBreakAllRopes(owner);
            EndRopingJobs(owner, ropees);
        }

        public static bool Prefix(Pawn_RopeTracker __instance)
        {
            Pawn pawn = PawnFor(__instance);
            if (pawn == null)
            {
                return true;
            }

            if (!RopingService.AnyMooGirlRopee(__instance))
            {
                return true;
            }

            RopingService.RefreshRoperFromTracker(pawn);

            // 牵引者异常的检查间隔沿用旧逻辑。
            if (Find.TickManager.TicksGame % 10 == 0)
            {
                if (pawn.Dead || pawn.Downed || pawn.IsBurning() ||
                    (pawn.InMentalState && pawn.MentalStateDef != MentalStateDefOf.Roaming))
                {
                    ForceUnrope(__instance);
                    return false;
                }
            }

            List<Pawn> ropees = __instance.Ropees;
            if (ropees != null)
            {
                for (int i = 0; i < ropees.Count; i++)
                {
                    Pawn follower = ropees[i];
                    if (!RopingService.IsMooGirlRopee(follower) || !RopingService.IsFollowingRoper(follower))
                    {
                        continue;
                    }

                    if (follower.CurJob.targetA.Thing != pawn)
                    {
                        continue;
                    }

                    if (pawn.Dead || pawn.Downed || !pawn.Awake() || pawn.IsBurning() ||
                        (pawn.InMentalState && pawn.MentalStateDef != MentalStateDefOf.Roaming))
                    {
                        ForceUnrope(__instance);
                        follower.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                        pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                        return false;
                    }
                }
            }

            // 距离检查间隔沿用旧逻辑。
            if (Find.TickManager.TicksGame % 30 == 0)
            {
                if (__instance.RopedTo.IsValid &&
                    !pawn.CanReach(__instance.RopedTo, PathEndMode.Touch, Danger.Deadly))
                {
                    ForceUnrope(__instance);
                    return false;
                }
            }

            if (__instance.IsRopedToHitchingPost && !__instance.RopedToHitchingSpot.Spawned)
            {
                __instance.UnropeFromSpot();
                RopingService.NotifyBreakAllRopes(pawn);
                EndRopingJobs(pawn, ropees);
                return false;
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

    [HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.UnropeFromSpot))]
    public static class Patch_RopeTracker_UnropeFromSpot
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_RopeTracker __instance)
        {
            RopingService.NotifyBreakAllRopes(Patch_RopingTick.PawnFor(__instance));
        }
    }
}
