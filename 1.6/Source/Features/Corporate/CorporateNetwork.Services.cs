using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    public partial class CorporateNetwork
    {
        // Monotonic, per-save totals. Money transfers and gifts are not merchandise.
        private long tradeTurnover;
        // Pawn.thingIDNumber 在同一存档内稳定；保留曾产生非免费成交额的人员 ID。
        private List<int> creditedPersonIds = new List<int>();
        private int giftWeek = -1;
        private int freePersonWeek = -1;
        private int supportWeek = -1;
        private int supportCallsUsed;
        // StaticCacheLifecycle: immutable balance tables; no per-game object references.
        private static readonly int[] ServiceThresholds = { 10000, 25000, 50000, 100000, 200000, 400000 };
        private static readonly float[] OrderDiscounts = { 1f, 0.95f, 0.92f, 0.90f, 0.88f, 0.85f, 0.85f };
        private static readonly int[] ExtraStock = { 0, 0, 8, 12, 16, 24, 24 };

        public long TradeTurnover => tradeTurnover;
        public int ServiceLevel => ServiceThresholds.Count(t => tradeTurnover >= t);
        public static int MaxServiceLevel => ServiceThresholds.Length;
        public static int ServiceThreshold(int level) => ServiceThresholds[level - 1];
        public float OrderDiscountFactor => OrderDiscounts[ServiceLevel];
        public int WeeklyStockCount => TradeSettings.weeklyStockCount + ExtraStock[ServiceLevel];
        public bool WeeklyGiftAvailable => ServiceLevel >= 3 && nextRefreshTick > Now && giftWeek != nextRefreshTick;
        public bool WeeklyPersonAvailable => ServiceLevel >= 4
            && nextRefreshTick > Now && freePersonWeek != nextRefreshTick;
        public int SupportSquadSize => ServiceLevel >= 6 ? 8 : ServiceLevel >= 5 ? 4 : 0;
        public int WeeklySupportLimit => ServiceLevel >= 6 ? 2 : ServiceLevel >= 5 ? 1 : 0;
        public int WeeklySupportCallsRemaining => nextRefreshTick <= Now ? 0
            : Math.Max(0, WeeklySupportLimit - (supportWeek == nextRefreshTick ? supportCallsUsed : 0));
        public bool WeeklySupportAvailable => WeeklySupportCallsRemaining > 0;

        internal void AddTradeTurnover(long amount)
        {
            if (amount <= 0) return;
            int previous = ServiceLevel;
            tradeTurnover += Math.Min(amount, long.MaxValue - tradeTurnover);
            if (ServiceLevel > previous)
            {
                Record("Mugirl.CorporateServices.Unlocked", ServiceLevel.ToString());
                Messages.Message("Mugirl.CorporateServices.LevelUp".Translate(ServiceLevel), MessageTypeDefOf.PositiveEvent, false);
            }
        }

        internal void CreditPersonTurnover(Pawn pawn, long amount)
        {
            if (pawn == null || amount <= 0 || creditedPersonIds.Contains(pawn.thingIDNumber)) return;
            creditedPersonIds.Add(pawn.thingIDNumber);
            AddTradeTurnover(amount);
        }

        private void CreditOrderTurnover(CorporateOrder order)
        {
            if (order.quantity <= 0 || order.paid <= 0) return;
            int settled = (int)((long)order.paid * Math.Min(order.quantity, order.deliveredQuantity) / order.quantity);
            AddTradeTurnover(settled - order.creditedTurnover);
            order.creditedTurnover = Math.Max(order.creditedTurnover, settled);
        }

        public int PeoplePurchasePrice(CorporatePersonOffer offer, bool asColonist = false)
        {
            if (offer == null) return 0;
            if (offer.paid) return offer.paidAmount < 0 ? offer.price : offer.paidAmount;
            return WeeklyPersonAvailable ? 0 : PeoplePurchaseQuote(offer, asColonist).total;
        }

        public bool TryClaimWeeklyGift(CorporateTradeContext context, out string reason)
        {
            EnsureWeeklyOffers();
            if (!CanTrade(context, out reason)) return false;
            if (!WeeklyGiftAvailable) { reason = "Mugirl.CorporateServices.Unavailable".Translate(); return false; }
            if (!TryMakeGoods(ThingDefOf.MedicineIndustrial, null, QualityCategory.Normal, 10, out List<Thing> medicine))
            { reason = "Mugirl.Corporate.CannotMake".Translate(); return false; }
            if (!TryMakeGoods(ThingDefOf.MealSurvivalPack, null, QualityCategory.Normal, 20, out List<Thing> meals))
            {
                foreach (Thing thing in medicine) DiscardUnspawnedProduct(thing);
                reason = "Mugirl.Corporate.CannotMake".Translate(); return false;
            }
            // Each parcel has a durable receipt, including when delivery is temporarily unavailable.
            CorporateOrder first = SaveOrder(ThingDefOf.MedicineIndustrial, 10, 0, Now, medicine);
            CorporateOrder second = SaveOrder(ThingDefOf.MealSurvivalPack, 20, 0, Now, meals);
            first.state = second.state = CorporateOrderState.Ready;
            giftWeek = nextRefreshTick;
            ClaimOrder(context, first, out _);
            ClaimOrder(context, second, out _);
            Record("Mugirl.CorporateServices.GiftRecord", context.Label);
            reason = null;
            return true;
        }

        public static bool ValidSupportCell(Map map, IntVec3 cell)
        {
            return map != null && MugirlGameUtility.LoadedMaps?.Contains(map) == true && cell.InBounds(map)
                && cell.Standable(map) && !cell.Fogged(map) && !cell.Roofed(map);
        }

        public bool TryCallSupport(CorporateTradeContext context, Map map, IntVec3 cell, out string reason)
        {
            EnsureWeeklyOffers();
            if (!CanTrade(context, out reason)) return false;
            if (!WeeklySupportAvailable) { reason = "Mugirl.CorporateServices.Unavailable".Translate(); return false; }
            if (!ValidSupportCell(map, cell)) { reason = "Mugirl.CorporateServices.InvalidTarget".Translate(); return false; }
            var pawns = new List<Pawn>();
            try
            {
                PawnKindDef kind = MugirlContentDefOf.Mugirl_CorporateSupport;
                for (int i = 0; i < SupportSquadSize; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, CorporateFaction,
                        PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true,
                        allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: true, allowPregnant: false, developmentalStages: DevelopmentalStage.Adult));
                    pawns.Add(pawn);
                    CorporateDiehardUtility.MakeDiehard(pawn);
                    CorporateUniforms.EnsureRequiredApparel(pawn);
                    CorporateSupportUtility.EquipFieldSupplies(pawn);
                }
            }
            catch (Exception ex)
            {
                foreach (Pawn pawn in pawns) MugirlGeneratedPawnUtility.Discard(pawn);
                MugirlLog.WarningOnce("Corporate.SupportGeneration", "Corporate support generation failed: " + ex.Message);
                reason = "Mugirl.Corporate.CannotMake".Translate(); return false;
            }
            // A single pod lands precisely at the selected safe outdoor cell.
            var info = new ActiveTransporterInfo { openDelay = 110, leaveSlag = false };
            foreach (Pawn pawn in pawns) info.innerContainer.TryAdd(pawn);
            try { DropPodUtility.MakeDropPodAt(cell, map, info, CorporateFaction); }
            catch (Exception ex)
            {
                // A spawned pod owns its passengers even if a subsequent mod callback throws.
                if (!pawns.Any(p => p.MapHeld == map))
                {
                    foreach (Pawn pawn in pawns) { pawn.holdingOwner?.Remove(pawn); MugirlGeneratedPawnUtility.Discard(pawn); }
                    reason = "Mugirl.Corporate.DeliveryUnavailable".Translate(); return false;
                }
                MugirlLog.WarningOnce("Corporate.SupportDrop", ex.ToString());
            }
            supportCallsUsed = supportWeek == nextRefreshTick ? supportCallsUsed + 1 : 1;
            supportWeek = nextRefreshTick;
            LordMaker.MakeNewLord(CorporateFaction, new LordJob_CorporateSupport(cell), map, pawns);
            Record("Mugirl.CorporateServices.SupportRecord", map.Parent.LabelCap + " " + cell);
            reason = null;
            return true;
        }

        private void ServicesExposeData()
        {
            Scribe_Values.Look(ref tradeTurnover, "corporateTradeTurnover", 0L);
            Scribe_Collections.Look(ref creditedPersonIds, "corporateCreditedPersonIds", LookMode.Value);
            Scribe_Values.Look(ref giftWeek, "corporateGiftWeek", -1);
            Scribe_Values.Look(ref freePersonWeek, "corporateFreePersonWeek", -1);
            Scribe_Values.Look(ref supportWeek, "corporateSupportWeek", -1);
            Scribe_Values.Look(ref supportCallsUsed, "corporateSupportCallsUsed", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tradeTurnover = Math.Max(0L, tradeTurnover);
                if (creditedPersonIds == null) creditedPersonIds = new List<int>();
                creditedPersonIds = creditedPersonIds.Where(id => id > 0).Distinct().ToList();
                // Previous versions stored only the week of a single successful call.
                if (supportCallsUsed < 0) supportCallsUsed = supportWeek >= 0 ? 1 : 0;
            }
        }
    }

    public sealed class LordJob_CorporateSupport : LordJob
    {
        private IntVec3 fallback;
        private int supportGraphVersion = 1;

        // Lord.SetJob 会在 CreateGraph 之后自动追加减员逃跑分支，必须在此关闭。
        // 旧档先还原原有索引与计时数据，再于首个 tick 移除自动逃跑分支。
        public override bool AddFleeToil => supportGraphVersion == 0 && Scribe.mode == LoadSaveMode.PostLoadInit;

        public LordJob_CorporateSupport() { }
        public LordJob_CorporateSupport(IntVec3 fallback) { this.fallback = fallback; }
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            // 搜敌 toil 使用本模组 duty，空闲时队员会处理自己或附近友方小人的伤口。
            var fight = new LordToil_CorporateSupportHunt(fallback);
            var exit = new LordToil_ExitMap(LocomotionUrgency.Jog, canDig: true);
            graph.AddToil(fight);
            graph.AddToil(exit);
            var leave = new Transition(fight, exit);
            leave.AddTrigger(new Trigger_TicksPassed(CorporateNetwork.DayTicks));
            leave.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(leave);
            // No casualty, damage, or morale retreat transition.
            return graph;
        }
        public override void LordJobTick()
        {
            if (supportGraphVersion != 0) return;
            supportGraphVersion = 1;
            bool wasFleeing = lord.CurLordToil is LordToil_PanicFlee;
            if (wasFleeing)
            {
                lord.GotoToil(lord.Graph.lordToils[0]);
            }
            foreach (Pawn pawn in lord.ownedPawns)
            {
                // 旧版原生 TendPatient 没有协同等待，读档后重新分配专用医疗 Job。
                if (wasFleeing || pawn.CurJobDef == JobDefOf.TendPatient)
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
            lord.Graph.transitions.RemoveAll(t => t.target is LordToil_PanicFlee);
            lord.Graph.lordToils.RemoveAll(t => t is LordToil_PanicFlee);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref fallback, "fallback");
            // 缺失字段代表旧图；新建小队使用版本 1，保留原有 fallback 存档键。
            Scribe_Values.Look(ref supportGraphVersion, "supportGraphVersion", 0);
        }
    }

    internal struct CorporatePersonTradeAmount
    {
        internal Pawn pawn;
        internal double amount;
    }

    internal sealed class CorporateTurnoverCapture
    {
        internal double otherAmount;
        internal readonly List<CorporatePersonTradeAmount> personAmounts = new List<CorporatePersonTradeAmount>();
    }

    // 原版巨企商队、据点和轨道交易同样入账；先捕获交易中的实际 Pawn 引用。
    // TryExecute 成功后才入账，货币、赠礼与同一雪牛娘的后续买卖均不重复计入。
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    internal static class Harmony_CorporateTradeTurnover
    {
        internal static void Prefix(TradeDeal __instance, out CorporateTurnoverCapture __state)
        {
            __state = new CorporateTurnoverCapture();
            if (TradeSession.giftMode || TradeSession.trader?.Faction?.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile) return;
            foreach (Tradeable tradeable in __instance.AllTradeables)
            {
                if (tradeable.IsCurrency || tradeable.ActionToDo == TradeAction.None) continue;
                double value = Math.Abs((double)tradeable.CurTotalCurrencyCostForSource);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) continue;
                if (!(tradeable is Tradeable_Pawn))
                {
                    __state.otherAmount += value;
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
                        __state.personAmounts.Add(new CorporatePersonTradeAmount { pawn = pawn, amount = share });
                    else __state.otherAmount += share;
                }
            }
        }
        internal static void Postfix(bool __result, bool actuallyTraded, CorporateTurnoverCapture __state)
        {
            if (!__result || !actuallyTraded || __state == null) return;
            CorporateNetwork network = CorporateNetwork.Current;
            if (network == null) return;
            network.AddTradeTurnover((long)Math.Min(1000000000d, Math.Floor(__state.otherAmount)));
            foreach (CorporatePersonTradeAmount person in __state.personAmounts)
                network.CreditPersonTurnover(person.pawn, (long)Math.Min(1000000000d, Math.Floor(person.amount)));
        }
    }
}
