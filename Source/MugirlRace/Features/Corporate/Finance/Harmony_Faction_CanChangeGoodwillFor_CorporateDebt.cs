using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 违约期间拒绝正向好感变化，避免用礼物或任务奖励绕过债务敌对；负向变化照常记录。
[HarmonyPatch(typeof(Faction), nameof(Faction.CanChangeGoodwillFor))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Positive goodwill changes may be accepted during default; debt balance is preserved.")]
internal static class Harmony_Faction_CanChangeGoodwillFor_CorporateDebt
{
    public static void Postfix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
    {
        if (goodwillChange > 0 && CorporateNetwork.DefaultedLoanFaction is { } debtor
            && CorporateNetwork.IsDebtPair(debtor, __instance, other))
        {
            __result = false;
        }
    }
}
