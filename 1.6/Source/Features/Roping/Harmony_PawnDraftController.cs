using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_PawnDraftController_GetGizmos
    {
        [HarmonyPostfix]
        public static void GetGizmosPostfix(ref IEnumerable<Gizmo> __result, Pawn_DraftController __instance)
        {
            if (__result == null) return;

            Pawn pawn = __instance?.pawn;
            if (!RopingService.IsMugirlRopee(pawn)) return;
            if (!RopingService.IsRopedByPawn(pawn) && !RopingService.IsRopedToSpot(pawn) && !RopingService.IsPendingSpotRope(pawn)) return;

            __result = DisableDraftGizmo(__result);
        }

        private static IEnumerable<Gizmo> DisableDraftGizmo(IEnumerable<Gizmo> gizmos)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                if (gizmo is Command_Toggle toggle && toggle.icon == TexCommand.Draft)
                {
                    toggle.Disable("Mugirl.DraftDisabledWhileRoped".Translate());
                }

                yield return gizmo;
            }
        }
    }
}
