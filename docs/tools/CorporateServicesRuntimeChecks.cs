using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Mugirl
{
    internal static class CorporateServicesRuntimeChecks
    {
        private static long savedTurnover;
        private static int savedRefresh;
        private static int[] supportIds;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Run(Map map, CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
        {
            FieldInfo total = typeof(CorporateNetwork).GetField("tradeTurnover", Fields);
            total.SetValue(network, 0L);
            Thing preview = CorporateNetwork.MakeProduct(ThingDefOf.Steel, null, QualityCategory.Normal);
            int fullPrice = network.OrderQuote(preview, 100);
            check("Support is unavailable below the first support tier", network.SupportSquadSize == 0 && network.WeeklySupportLimit == 0);
            for (int level = 1; level <= CorporateNetwork.MaxServiceLevel; level++)
            {
                total.SetValue(network, (long)CorporateNetwork.ServiceThreshold(level) - 1);
                check("Client tier " + level + " is locked below threshold", network.ServiceLevel == level - 1);
                network.AddTradeTurnover(1);
                check("Client tier " + level + " unlocks at threshold", network.ServiceLevel == level);
            }
            check("Highest client tier discounts actual order quotes", network.OrderQuote(preview, 100) < fullPrice
                && network.OrderQuote(preview, 100) == CorporateNetwork.Price(preview.MarketValue * network.TradeSettings.orderPriceFactor * 0.85f, 100));
            preview.Destroy();
            long before = network.TradeTurnover;
            network.AddTradeTurnover(-50);
            check("Negative or zero turnover cannot erase earned tiers", network.TradeTurnover == before);
            total.SetValue(network, long.MaxValue - 2);
            network.AddTradeTurnover(100);
            check("Turnover saturates without integer overflow", network.TradeTurnover == long.MaxValue);
            total.SetValue(network, 200000L);
            Refresh(network);
            check("Premium refresh expands stock to 40 distinct types", network.Stock.Count == 40 && network.Stock.Select(s => s.sample.def).Distinct().Count() == 40);
            int[] ids = network.Stock.Select(s => s.id).ToArray();
            network.EnsureWeeklyOffers();
            check("Opening services does not reroll premium stock", ids.SequenceEqual(network.Stock.Select(s => s.id)));

            CorporateStock stock = network.Stock.OrderBy(s => network.StockUnitPrice(s)).First();
            before = network.TradeTurnover;
            int quote = network.StockUnitPrice(stock);
            check("Delivered stock counts actual paid amount", network.BuyStock(context, stock, 1, out _) && network.TradeTurnover == before + quote);
            CorporateOrder receipt = network.Orders.Last();
            before = network.TradeTurnover;
            check("Repeated receipt claim cannot duplicate turnover", !network.ClaimOrder(context, receipt, out _) && network.TradeTurnover == before);
            bool placed = network.PlaceOrder(context, ThingDefOf.Steel, null, QualityCategory.Normal, 5, out _);
            check("Prepaid orders do not count before delivery", placed && network.TradeTurnover == before);
            check("Cancelled orders cannot farm turnover", network.CancelOrder(context, network.Orders.Last(), out _) && network.TradeTurnover == before);
            Thing milk = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_Milk);
            milk.stackCount = 5;
            IntVec3 moneyCell = context.AvailableThings.First(t => t.def == ThingDefOf.Silver).Position;
            GenSpawn.Spawn(milk, moneyCell, map);
            context.Invalidate();
            quote = network.ProductQuote(milk, 5);
            check("Goods sold to corporation also count their gross payment", network.SellProduct(context, milk, 5, out _) && network.TradeTurnover == before + quote);

            before = network.TradeTurnover;
            int silver = context.SilverCount;
            int orderCount = network.Orders.Count;
            check("Weekly gift issues durable free parcels", network.TryClaimWeeklyGift(context, out _)
                && network.Orders.Count == orderCount + 2 && network.Orders.Skip(orderCount).All(o => o.paid == 0));
            check("Gift does not spend silver or add turnover", context.SilverCount == silver && network.TradeTurnover == before);
            check("Gift can only be claimed once per week", !network.TryClaimWeeklyGift(context, out _));
            CorporatePersonOffer person = network.PeopleOffers.First(o => !o.paid);
            bool asColonist = !ModsConfig.IdeologyActive;
            check("First weekly personnel quote is free in either purchase mode",
                network.WeeklyPersonAvailable && network.PeoplePurchasePrice(person, asColonist) == 0);
            check("Free personnel purchase delivers without silver or turnover",
                network.TryPurchasePerson(context, person, asColonist, out _)
                && person.delivered && person.paidAmount == 0 && context.SilverCount == silver && network.TradeTurnover == before
                && (asColonist ? person.pawn.IsColonist : person.pawn.IsSlaveOfColony));
            check("Second personnel purchase retains its regular quote", network.PeopleOffers.Where(o => !o.paid)
                .All(o => network.PeoplePurchasePrice(o, asColonist) == network.PeoplePurchaseQuote(o, asColonist).total));
            check("Free recipient cannot be delivered twice", !network.TryPurchasePerson(context, person, asColonist, out _));

            check("Invalid landing rejects call without consuming it", !network.TryCallSupport(context, map, IntVec3.Invalid, out _) && network.WeeklySupportAvailable);
            var denied = new CorporateTradeContext(map, () => false);
            IntVec3 landing = GenRadial.RadialCellsAround(map.mapPawns.FreeColonistsSpawned.First().Position, 70f, true)
                .First(c => CorporateNetwork.ValidSupportCell(map, c));
            check("Support rechecks terminal access", !network.TryCallSupport(denied, map, landing, out _) && network.WeeklySupportAvailable);
            check("Support creates real incoming drop pod", network.TryCallSupport(context, map, landing, out _));
            Lord lord = map.lordManager.lords.LastOrDefault(l => l.LordJob is LordJob_CorporateSupport);
            check("Strike team contains four corporate Mugirl with excellent weapons", lord != null && lord.ownedPawns.Count == 4
                && lord.ownedPawns.All(p => MugirlIdentity.IsMugirlPawn(p) && p.Faction == network.CorporateFaction
                    && p.equipment.Primary != null && p.equipment.Primary.TryGetQuality(out QualityCategory q) && q == QualityCategory.Excellent));
            check("Strike team wears all required combat apparel and will not flee", lord != null && lord.ownedPawns.All(p => !p.mindState.canFleeIndividual
                && p.kindDef.apparelRequired.All(d => p.apparel.WornApparel.Any(a => a.def == d))));
            check("Pod lands at the specified point and retains all passengers", lord != null && lord.ownedPawns.All(p => p.MapHeld == map && p.PositionHeld == landing));
            check("Second weekly support call is rejected", !network.TryCallSupport(context, map, landing, out _));
            check("First support tier has a four-person squad and one weekly call", network.SupportSquadSize == 4
                && network.WeeklySupportLimit == 1 && network.WeeklySupportCallsRemaining == 0);
            network.AddTradeTurnover(CorporateNetwork.ServiceThreshold(6) - network.TradeTurnover);
            check("Midweek promotion increases capacity without resetting used calls", network.SupportSquadSize == 8
                && network.WeeklySupportLimit == 2 && network.WeeklySupportCallsRemaining == 1);
            IntVec3 secondLanding = GenRadial.RadialCellsAround(landing, 15f, false)
                .First(c => c.DistanceTo(landing) >= 8f && CorporateNetwork.ValidSupportCell(map, c));
            check("Invalid advanced support call preserves its remaining allowance", !network.TryCallSupport(context, map, IntVec3.Invalid, out _)
                && network.WeeklySupportCallsRemaining == 1);
            check("Promoted client can use the second weekly call", network.TryCallSupport(context, map, secondLanding, out _));
            lord = map.lordManager.lords.LastOrDefault(l => l.LordJob is LordJob_CorporateSupport);
            check("Advanced support really deploys eight equipped corporate troopers", lord != null && lord.ownedPawns.Count == 8
                && lord.ownedPawns.All(p => MugirlIdentity.IsMugirlPawn(p) && p.Faction == network.CorporateFaction
                    && p.MapHeld == map && p.PositionHeld == secondLanding && p.equipment.Primary != null));
            check("Advanced tier rejects a third weekly call", network.WeeklySupportCallsRemaining == 0
                && !network.TryCallSupport(context, map, secondLanding, out _));
            supportIds = lord?.ownedPawns.Select(p => p.thingIDNumber).ToArray() ?? new int[0];
            CheckUniform("AI_GC_Courier", network, check);
            CheckUniform("Mugirl_CorporateRepresentative", network, check);

            // Match original TryExecute's pre-reset state, then verify the successful/failed boundary.
            var trader = TradeSession.trader;
            var negotiator = TradeSession.playerNegotiator;
            bool gift = TradeSession.giftMode;
            try
            {
                TradeSession.trader = new CorporatePersonnelTrader(network);
                TradeSession.playerNegotiator = context.Negotiator;
                TradeSession.giftMode = false;
                var deal = new TradeDeal();
                Thing bought = ThingMaker.MakeThing(ThingDefOf.Steel);
                Thing sold = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                var buy = new Tradeable(null, bought);
                var sell = new Tradeable(sold, null);
                buy.ForceTo(3); sell.ForceTo(-2);
                deal.AllTradeables.Add(buy); deal.AllTradeables.Add(sell);
                Harmony_CorporateTradeTurnover.Prefix(deal, out CorporateTurnoverCapture capture);
                long value = (long)Math.Floor(capture.otherAmount);
                check("Vanilla trades sum purchases and sales rather than net balance", value > 0 && value == (long)Math.Floor(
                    Math.Abs((double)buy.CurTotalCurrencyCostForSource) + Math.Abs((double)sell.CurTotalCurrencyCostForSource))
                    && capture.personAmounts.Count == 0);
                before = network.TradeTurnover;
                Harmony_CorporateTradeTurnover.Postfix(false, false, capture);
                check("Rejected vanilla trade adds no turnover", network.TradeTurnover == before);
                Harmony_CorporateTradeTurnover.Postfix(true, true, capture);
                check("Successful vanilla trade credits captured turnover", network.TradeTurnover == before + value);
                TradeSession.giftMode = true;
                Harmony_CorporateTradeTurnover.Prefix(deal, out capture);
                check("Vanilla gifts do not count as trade", capture.otherAmount == 0 && capture.personAmounts.Count == 0);
                bought.Destroy(); sold.Destroy();
            }
            finally { TradeSession.trader = trader; TradeSession.playerNegotiator = negotiator; TradeSession.giftMode = gift; }

            savedTurnover = network.TradeTurnover;
            savedRefresh = network.NextRefreshTick;
        }

        private static void CheckUniform(string kindName, CorporateNetwork network, Action<string, bool> check)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kindName),
                network.CorporateFaction, forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
            check(kindName + " generates in its complete professional uniform", pawn.kindDef.apparelRequired.All(d => pawn.apparel.WornApparel.Any(a => a.def == d)));
            MugirlGeneratedPawnUtility.Discard(pawn);
        }

        private static void Refresh(CorporateNetwork network)
        {
            typeof(CorporateNetwork).GetField("nextRefreshTick", Fields).SetValue(network, CorporateNetwork.Now);
            network.EnsureWeeklyOffers();
        }

        internal static void VerifyReload(CorporateNetwork network, Action<string, bool> check)
        {
            check("Client turnover and level survive full save/load", network.TradeTurnover == savedTurnover && network.ServiceLevel == 6);
            check("Weekly benefits stay consumed after save/load", network.NextRefreshTick == savedRefresh
                && !network.WeeklyGiftAvailable && !network.WeeklyPersonAvailable && !network.WeeklySupportAvailable);
            Lord lord = Find.Maps.SelectMany(m => m.lordManager.lords).FirstOrDefault(l => l.LordJob is LordJob_CorporateSupport
                && l.ownedPawns.Any(p => supportIds.Contains(p.thingIDNumber)));
            check("Incoming strike team and combat controller survive save/load", lord != null && supportIds.Length == 8
                && supportIds.OrderBy(i => i).SequenceEqual(lord.ownedPawns.Select(p => p.thingIDNumber).OrderBy(i => i)));
            Refresh(network);
            check("Next weekly refresh restores service allowances", network.WeeklyGiftAvailable && network.WeeklySupportAvailable
                && network.WeeklySupportCallsRemaining == 2
                && network.WeeklyPersonAvailable);
            if (lord != null)
            {
                for (int tick = 0; tick < 500; tick++) Find.TickManager.DoSingleTick();
                check("Saved incoming pod really opens and deploys all eight troopers", lord.ownedPawns.Count == 8 && lord.ownedPawns.All(p => p.Spawned && p.Map == lord.Map));
                check("Landed squad retains hunt-enemies duties and fearless state", lord.ownedPawns.All(p => !p.mindState.canFleeIndividual
                    && p.mindState.duty?.def == MugirlContentDefOf.Mugirl_CorporateSupportHunt));
                Pawn casualty = lord.ownedPawns.First();
                casualty.Kill(null);
                lord.LordTick();
                check("Strike team does not retreat after a casualty", lord.CurLordToil is LordToil_HuntEnemies && lord.ownedPawns.Count == 7);
                for (int tick = 0; tick <= CorporateNetwork.DayTicks; tick++) lord.LordTick();
                check("Strike team switches to map exit when its support day expires", lord.CurLordToil is LordToil_ExitMap);
            }
        }
    }
}
