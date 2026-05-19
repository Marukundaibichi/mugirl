using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_RopeTracker), "RopingTick")]
    public static class Patch_RopingTick
    {
        // 缓存反射结果，避免每 tick 反射
        private static readonly FieldInfo pawnField = AccessTools.Field(typeof(Pawn_RopeTracker), "pawn");

        // 统一结束 rope 相关工作
        private static void EndRopingJobs(Pawn_RopeTracker tracker)
        {
            var pawn = (Pawn)pawnField.GetValue(tracker);
            pawn?.jobs?.EndCurrentJob(JobCondition.InterruptForced);

            var ropees = tracker.Ropees;
            if (ropees != null)
            {
                for (int i = 0; i < ropees.Count; i++)
                {
                    ropees[i]?.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        private static void ForceUnrope(Pawn_RopeTracker tracker)
        {
            tracker.BreakAllRopes();
            EndRopingJobs(tracker);
        }

        public static bool Prefix(Pawn_RopeTracker __instance)
        {
            var pawn = (Pawn)pawnField.GetValue(__instance);
            if (pawn == null) return true;

            // 检查是否有 MooGirl 被牵引
            bool hasMooRopee = false;
            var ropees = __instance.Ropees;
            if (ropees != null)
            {
                for (int i = 0; i < ropees.Count; i++)
                {
                    var r = ropees[i];
                    if (r?.RaceProps?.body == MooGirl_DefOf.MooGirlBody)
                    {
                        hasMooRopee = true;
                        break;
                    }
                }
            }
            if (!hasMooRopee) return true; // 非 MooGirl → 走原方法

            // 牵引者异常，每 10 tick 检查一次
            if (Find.TickManager.TicksGame % 10 == 0)
            {
                if (pawn.Dead || pawn.Downed || pawn.IsBurning() ||
                    (pawn.InMentalState && pawn.MentalStateDef != MentalStateDefOf.Roaming))
                {
                    ForceUnrope(__instance);
                    return false;
                }
            }

            // follower 状态异常
            foreach (var follower in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (follower.CurJob?.def != MooGirl_DefOf.Job_FollowRoper) continue;
                if (follower.RaceProps?.body != MooGirl_DefOf.MooGirlBody) continue;

                Pawn targetPawn = follower.CurJob.targetA.Thing as Pawn;
                if (targetPawn == null || !targetPawn.Spawned) continue;

                if (targetPawn.Dead || targetPawn.Downed || !targetPawn.Awake() || targetPawn.IsBurning() ||
                    (targetPawn.InMentalState && targetPawn.MentalStateDef != MentalStateDefOf.Roaming))
                {
                    ForceUnrope(__instance);

                    follower.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                    targetPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                    return false;
                }
            }

            // 无法到达目标 → 每 30 tick 检查一次
            if (Find.TickManager.TicksGame % 30 == 0)
            {
                if (__instance.RopedTo.IsValid &&
                    !pawn.CanReach(__instance.RopedTo, PathEndMode.Touch, Danger.Deadly))
                {
                    ForceUnrope(__instance);
                    return false;
                }
            }

            // Hitching post 丢失
            if (__instance.IsRopedToHitchingPost && !__instance.RopedToHitchingSpot.Spawned)
            {
                __instance.UnropeFromSpot();
                EndRopingJobs(__instance);
                return false;
            }

            return false; // 已处理，不执行原方法
        }
    }
}
