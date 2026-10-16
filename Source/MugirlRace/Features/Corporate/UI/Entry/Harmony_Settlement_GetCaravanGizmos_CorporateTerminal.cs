using HarmonyLib;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// PatchGovernance: 偿债入口独立于原版 Visitable，债务敌对不能封死还款；不改据点贸易。
[HarmonyPatch(typeof(Settlement), nameof(Settlement.GetCaravanGizmos))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Low, "Corporate settlement access is unavailable; console access remains available.")]
internal static class Harmony_Settlement_GetCaravanGizmos_CorporateTerminal
{
    private static void Postfix(Settlement __instance, Caravan caravan, ref IEnumerable<Gizmo> __result)
    {
        __result = Append(__result, __instance, caravan);
    }

    private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo>? original, Settlement settlement, Caravan caravan)
    {
        if (original is not null)
        {
            foreach (Gizmo gizmo in original)
            {
                yield return gizmo;
            }
        }

        if (!CorporateCommsAccess.AtSettlement(caravan, settlement))
        {
            yield break;
        }

        Command_Action command = new()
        {
            defaultLabel = "Mugirl.CorporateUI.SettlementOption".Translate(),
            defaultDesc = "Mugirl.CorporateUI.SettlementDescription".Translate(),
            icon = TexCommand.OpenLinkedQuestTex,
            action = () => CorporateCommsAccess.OpenSettlement(caravan, settlement),
        };
        if (CorporateIntroduction.Current?.Completed != true)
        {
            command.Disable("Mugirl.CorporateUI.Locked".Translate());
        }

        yield return command;
    }
}
