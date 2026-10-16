using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 玩家主动伤害研究员：未表态时转入执行分支，已选择保护时撤销谢礼。战斗中每次命中都会经过，先做最便宜的判定。
[HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Attacking a research target before dialogue may not select the execution branch.")]
public static class Harmony_Thing_TakeDamage_CorporateResearchTargets
{
    public static void Prefix(Thing __instance, DamageInfo dinfo)
    {
        if (dinfo.Amount > 0 && __instance is Pawn pawn && dinfo.Instigator?.Faction is { } faction
            && PlayerFactionUtility.IsPlayerFaction(faction))
        {
            CorporateNetwork.Current?.ResearcherAttacked(pawn);
        }
    }
}
