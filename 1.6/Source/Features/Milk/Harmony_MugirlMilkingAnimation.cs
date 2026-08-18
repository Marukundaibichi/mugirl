using HarmonyLib;
using Verse;

namespace Mugirl
{
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
