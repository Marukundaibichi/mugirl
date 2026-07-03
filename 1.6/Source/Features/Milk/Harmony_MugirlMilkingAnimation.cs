using HarmonyLib;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch]
    public static class Harmony_MugirlMilkingAnimation_DisableCachedPawnRender
    {
        // StaticCacheLifecycle: 进程级 PawnRenderer.pawn 反射缓存；不持有游戏对象。
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnRenderer), "ParallelGetPreRenderResults");
        }

        private static void Prefix(PawnRenderer __instance, ref bool disableCache)
        {
            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (MugirlMilkingAnimation.HasActiveAnimation(pawn))
            {
                disableCache = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Harmony_MugirlMilkingAnimation_PawnMatrix
    {
        // StaticCacheLifecycle: 进程级 PawnRenderer.pawn 反射缓存；不持有游戏对象。
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnRenderer), "GetDrawParms");
        }

        private static void Prefix(PawnRenderer __instance, PawnRenderFlags flags, ref Rot4 bodyFacing)
        {
            MugirlMilkingAnimation.AdjustDrawFacing(PawnField?.GetValue(__instance) as Pawn, flags, ref bodyFacing);
        }

        private static void Postfix(ref PawnDrawParms __result)
        {
            if (MugirlMilkingAnimation.TryGetPawnTransform(__result.pawn, __result.flags, out Vector3 offset, out Quaternion rotation, out Vector3 scale))
            {
                __result.matrix = Matrix4x4.Translate(offset) * __result.matrix * Matrix4x4.Rotate(rotation) * Matrix4x4.Scale(scale);
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.GetTransform))]
    public static class Harmony_MugirlMilkingAnimation_NodeTransform
    {
        private static void Postfix(PawnRenderNode __instance, PawnDrawParms parms, ref Vector3 offset, ref Vector3 pivot, ref Quaternion rotation, ref Vector3 scale)
        {
            MugirlMilkingAnimation.ModifyNodeTransform(__instance, parms, ref offset, ref pivot, ref rotation, ref scale);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Harmony_MugirlMilkingAnimation_PawnDeSpawn
    {
        private static void Prefix(Pawn __instance)
        {
            MugirlMilkingAnimation.NotifyPawnLifecycleEnded(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    public static class Harmony_MugirlMilkingAnimation_PawnDestroy
    {
        private static void Prefix(Pawn __instance)
        {
            MugirlMilkingAnimation.NotifyPawnLifecycleEnded(__instance);
        }
    }
}
