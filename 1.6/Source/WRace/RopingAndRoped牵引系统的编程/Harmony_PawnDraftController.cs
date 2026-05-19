using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_PawnDraftController_GetGizmos
    {
        [HarmonyPostfix]
        public static void GetGizmosPostfix(ref IEnumerable<Gizmo> __result, Pawn_DraftController __instance)
        {
            if (__result == null) return;

            var pawn = __instance.pawn;
            if (pawn?.RaceProps?.body != MooGirl_DefOf.MooGirlBody) return;
            if (pawn.jobs.curJob?.def != MooGirl_DefOf.Job_FollowRoper && pawn.roping?.IsRopedToSpot != true) return;

            // 转成列表，保证可以修改并保持 Disable 功能
            var gizmoList = new List<Gizmo>();
            foreach (var g in __result)
            {
                if (g is Command_Toggle toggle && toggle.icon == TexCommand.Draft)
                {
                    toggle.Disable("MooGirl.DraftDisabledWhileRoped".Translate());
                }
                gizmoList.Add(g);
            }

            __result = gizmoList;
        }
    }
}
