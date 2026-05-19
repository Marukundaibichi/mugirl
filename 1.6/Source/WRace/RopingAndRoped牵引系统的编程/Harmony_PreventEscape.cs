using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace MooGirl
{
    [HarmonyPatch(typeof(JobGiver_PrisonerEscape), "TryGiveJob")]
    public static class JobGiver_PrisonerEscape_RopedBlock_Patch
    {
        public static bool Prefix(Pawn pawn)
        {
            Pawn_RopeTracker roping = pawn.roping;

            bool isRoped = roping.IsRopedToSpot || RopeStateTracker.IsPendingRope(pawn);

            // 返回 false 表示阻止执行原方法
            return !isRoped;
        }
    }
}
