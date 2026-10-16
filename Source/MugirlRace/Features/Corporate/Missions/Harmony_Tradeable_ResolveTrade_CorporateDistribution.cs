using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 分销合同的销售进度：玩家向巨企以外的商人卖出货物时计入。
// Tradeable_Pawn 与 Tradeable_RoyalFavor 重写 ResolveTrade 且不调用基类，人员与王权恩惠交易不经过本补丁（分销只统计物品）。
[HarmonyPatch(typeof(Tradeable), nameof(Tradeable.ResolveTrade))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Distribution sales progress is not updated; vanilla trade remains intact.")]
public static class Harmony_Tradeable_ResolveTrade_CorporateDistribution
{
    public sealed class Sale
    {
        public ThingDef? def;
        public ThingDef? stuff;
        public int count;
    }

    public static void Prefix(Tradeable __instance, out Sale? __state)
    {
        __state = null;
        if (TradeSession.giftMode || __instance.ActionToDo != TradeAction.PlayerSells
            || !__instance.HasAnyThing || __instance.IsCurrency)
        {
            return;
        }

        CorporateNetwork? network = CorporateNetwork.Current;
        if (network == null || TradeSession.trader?.Faction == network.CorporateFaction)
        {
            return;
        }

        __state = new Sale { def = __instance.ThingDef, stuff = __instance.StuffDef, count = Math.Abs(__instance.CountToTransfer) };
    }

    public static void Postfix(Sale? __state)
    {
        if (__state != null)
        {
            CorporateNetwork.Current?.NotifyDistributionSale(__state.def, __state.stuff, __state.count);
        }
    }
}
