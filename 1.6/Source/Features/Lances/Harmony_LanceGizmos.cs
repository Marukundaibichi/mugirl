using System.Collections.Generic;
using HarmonyLib;
using Mugirl.Features.Lances;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.GetGizmos))]
    public static class Harmony_LanceGizmos
    {
        public static void Postfix(Pawn_EquipmentTracker __instance, ref IEnumerable<Gizmo> __result)
        {
            CompLanceCharge lance = __instance?.Primary?.TryGetComp<CompLanceCharge>();
            if (lance != null)
            {
                __result = AppendLanceGizmos(__result, lance);
            }
        }

        private static IEnumerable<Gizmo> AppendLanceGizmos(IEnumerable<Gizmo> original, CompLanceCharge lance)
        {
            foreach (Gizmo gizmo in original)
            {
                yield return gizmo;
            }

            foreach (Gizmo gizmo in lance.GetEquippedGizmos())
            {
                yield return gizmo;
            }
        }
    }
}
