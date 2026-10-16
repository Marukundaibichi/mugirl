using HarmonyLib;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 原版在 Faction.Notify_MemberTookDamage 中以 AttackedMember 事件结算玩家友伤好感惩罚；
// 在统一出口 TryAffectGoodwillWith 处把死士成员的惩罚缩至 5%，消息数值同步缩减。
[HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Friendly-fire goodwill penalties from hitting diehard support troopers are not discounted.")]
internal static class Harmony_Faction_TryAffectGoodwillWith_CorporateDiehard
{
    internal static void Prefix(Faction __instance, Faction other, ref int goodwillChange,
        HistoryEventDef reason, GlobalTargetInfo? lookTarget)
    {
        Faction? playerFaction = Faction.OfPlayerSilentFail;
        if (goodwillChange >= 0 || reason != HistoryEventDefOf.AttackedMember || other == null
            || playerFaction == null || __instance != playerFaction || !lookTarget.HasValue)
        {
            return;
        }

        if (lookTarget.Value.Thing is Pawn pawn && pawn.Faction == other && CorporateDiehardUtility.IsDiehard(pawn))
        {
            goodwillChange = CorporateDiehardUtility.ScaleFriendlyFirePenalty(goodwillChange);
        }
    }
}
