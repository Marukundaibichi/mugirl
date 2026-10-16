using System.Linq;
using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 原版巨企商队、据点和轨道交易同样入账；先捕获交易中的实际 Pawn 引用。
// TryExecute 成功后才入账，货币、赠礼与同一雪牛娘的后续买卖均不重复计入。
// 人员的取用顺序与原版 Tradeable_Pawn.ResolveTrade 相同（thingsColony/thingsTrader 的前 count 个）。
[HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Low, "Vanilla corporate trades do not contribute to client service progress.")]
internal static class Harmony_TradeDeal_TryExecute_CorporateTurnover
{
    internal static void Prefix(TradeDeal __instance, out CorporateTurnoverCapture? __state)
    {
        __state = null;
        if (TradeSession.giftMode || TradeSession.trader?.Faction?.def != CorporateDefOf.Mugirl_GiantCorporation)
        {
            return;
        }

        var capture = new CorporateTurnoverCapture();
        foreach (Tradeable tradeable in __instance.AllTradeables)
        {
            if (tradeable.IsCurrency || tradeable.ActionToDo == TradeAction.None)
            {
                continue;
            }

            double value = Math.Abs((double)tradeable.CurTotalCurrencyCostForSource);
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                continue;
            }

            if (tradeable is not Tradeable_Pawn)
            {
                capture.otherAmount += value;
                continue;
            }

            int count = tradeable.ActionToDo == TradeAction.PlayerSells
                ? tradeable.CountToTransferToDestination : tradeable.CountToTransferToSource;
            IEnumerable<Thing> source = tradeable.ActionToDo == TradeAction.PlayerSells
                ? tradeable.thingsColony : tradeable.thingsTrader;
            List<Pawn> pawns = source.Take(Math.Max(0, count)).OfType<Pawn>().ToList();
            if (pawns.Count != count || count <= 0)
            {
                // 人员清单异常时跳过这笔人员额，不能混入货物额绕过去重。
                continue;
            }

            double share = value / count;
            foreach (Pawn pawn in pawns)
            {
                if (MugirlIdentity.IsMugirlPawn(pawn))
                {
                    capture.personAmounts.Add(new CorporatePersonTradeAmount { pawn = pawn, amount = share });
                }
                else
                {
                    capture.otherAmount += share;
                }
            }
        }

        __state = capture;
    }

    internal static void Postfix(bool __result, bool actuallyTraded, CorporateTurnoverCapture? __state)
    {
        if (!__result || !actuallyTraded || __state == null || CorporateNetwork.Current is not { } network)
        {
            return;
        }

        network.AddTradeTurnover((long)Math.Min(1000000000d, Math.Floor(__state.otherAmount)));
        foreach (CorporatePersonTradeAmount person in __state.personAmounts)
        {
            network.CreditPersonTurnover(person.pawn, (long)Math.Min(1000000000d, Math.Floor(person.amount)));
        }
    }
}
