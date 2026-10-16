using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 外交界面与 PlayerRelationKind 经由 RelationKindWith；FactionUtility.HostileTo 不调用它，所以两处都要补丁。
[HarmonyPatch(typeof(Faction), nameof(Faction.RelationKindWith))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Debt hostility may not appear in diplomacy UI.")]
internal static class Harmony_Faction_RelationKindWith_CorporateDebt
{
    public static void Postfix(Faction __instance, Faction other, ref FactionRelationKind __result)
    {
        if (__result == FactionRelationKind.Hostile || CorporateNetwork.DefaultedLoanFaction is not { } debtor)
        {
            return;
        }

        if (CorporateNetwork.IsDebtPair(debtor, __instance, other))
        {
            __result = FactionRelationKind.Hostile;
        }
    }
}
