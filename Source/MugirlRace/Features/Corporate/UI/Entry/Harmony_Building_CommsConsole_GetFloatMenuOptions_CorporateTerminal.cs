using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Corporate;

// PatchGovernance: 只追加独立通讯选项，不替换原版外交或轨道贸易；失败时保留原菜单。
[HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Low, "Corporate console access is unavailable; vanilla communications remain intact.")]
internal static class Harmony_Building_CommsConsole_GetFloatMenuOptions_CorporateTerminal
{
    private static void Postfix(Building_CommsConsole __instance, Pawn myPawn, ref IEnumerable<FloatMenuOption> __result)
    {
        __result = Append(__result, __instance, myPawn);
    }

    private static IEnumerable<FloatMenuOption> Append(IEnumerable<FloatMenuOption>? original, Building_CommsConsole console, Pawn? pawn)
    {
        if (original is not null)
        {
            foreach (FloatMenuOption option in original)
            {
                yield return option;
            }
        }

        if (pawn is null || !CorporateCommsAccess.IsPlayerFaction(console.Faction) || !CorporateCommsAccess.IsPlayerFaction(pawn.Faction))
        {
            yield break;
        }

        string? failure = CorporateCommsAccess.ConsoleFailure(console, pawn);
        string label = "Mugirl.CorporateUI.CallOption".Translate();
        if (failure is not null)
        {
            yield return new FloatMenuOption(label + ": " + failure, null);
            yield break;
        }

        yield return new FloatMenuOption(label, () =>
        {
            if (CorporateCommsAccess.CheckConsole(console, pawn) == CorporateCommsAccess.ConsoleBlock.None)
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(CorporateDefOf.Mugirl_CorporateUseComms, console), JobTag.Misc);
            }
        });
    }
}
