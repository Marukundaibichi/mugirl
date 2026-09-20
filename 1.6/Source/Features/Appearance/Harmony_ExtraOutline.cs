using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.Draw))]
    public static class Harmony_ExtraOutline_Body
    {
        // Consume the final visible requests/matrices prepared by the game and HAR.
        // No extra tree traversal, no second animation evaluation, no changes to requests.
        public static void Prefix(PawnRenderTree __instance, PawnDrawParms parms,
            List<PawnGraphicDrawRequest> ___drawRequests)
        {
            MugirlExtraOutline.DrawBody(__instance.pawn, ___drawRequests, parms);
        }
    }
}
