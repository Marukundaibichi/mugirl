using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Harmony_MountedPawnGizmos
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (MountedPawnUtility.IsMounted(__instance, out Comp_MooGirlMount comp))
            {
                __result = MountedPawnUtility.GetMountedPawnGizmos(__instance, comp);
            }
        }
    }
}
