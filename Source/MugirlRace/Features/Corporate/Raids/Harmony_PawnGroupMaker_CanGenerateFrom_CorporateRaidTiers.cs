using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 巨企敌对派系的战斗编组只在对应点数档位内可用（原版只检查上限，低点数袭击也可能抽到高档编组）。
[HarmonyPatch(typeof(PawnGroupMaker), nameof(PawnGroupMaker.CanGenerateFrom))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Giant corporation raid tier filtering falls back to the base pawn-group result.")]
internal static class Harmony_PawnGroupMaker_CanGenerateFrom_CorporateRaidTiers
{
    public static void Postfix(PawnGroupMaker __instance, PawnGroupMakerParms parms, ref bool __result)
    {
        if (!__result || __instance == null || parms?.faction?.def != CorporateDefOf.Mugirl_GiantCorporation)
        {
            return;
        }

        if (__instance.kindDef != PawnGroupKindDefOf.Combat || parms.groupKind != PawnGroupKindDefOf.Combat)
        {
            return;
        }

        if (CorporateRaidTierRules.TryGetTierAllowance(__instance.maxTotalPoints, parms.points, out bool allowed))
        {
            __result = allowed;
        }
    }
}
