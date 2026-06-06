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

            Pawn pawn = __instance.pawn;
            if (!RopingService.IsMooGirlRopee(pawn)) return;
            if (!RopingService.IsFollowingRoper(pawn) && !RopingService.IsRopedToSpot(pawn)) return;

            // 转成列表后再禁用征召按钮，避免延迟枚举时修改原序列。
            List<Gizmo> gizmoList = new List<Gizmo>();
            foreach (Gizmo gizmo in __result)
            {
                if (gizmo is Command_Toggle toggle && toggle.icon == TexCommand.Draft)
                {
                    toggle.Disable("MooGirl.DraftDisabledWhileRoped".Translate());
                }
                gizmoList.Add(gizmo);
            }

            __result = gizmoList;
        }
    }
}
