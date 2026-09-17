using HarmonyLib;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class Harmony_MugirlMilkingAnimation_DrawFacing
    {
        private static void Prefix(Pawn ___pawn, PawnRenderFlags flags, ref Rot4 bodyFacing)
        {
            MugirlMilkingAnimation.AdjustDrawFacing(___pawn, flags, ref bodyFacing);
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
