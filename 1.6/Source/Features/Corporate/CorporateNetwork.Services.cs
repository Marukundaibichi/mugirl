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
        public bool WeeklyPersonAvailable => ModsConfig.IdeologyActive && ServiceLevel >= 4
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

        private void CreditOrderTurnover(CorporateOrder order)
        {
            if (order.quantity <= 0 || order.paid <= 0) return;
            int settled = (int)((long)order.paid * Math.Min(order.quantity, order.deliveredQuantity) / order.quantity);
            AddTradeTurnover(settled - order.creditedTurnover);
            order.creditedTurnover = Math.Max(order.creditedTurnover, settled);
        }

        public int PeoplePurchasePrice(CorporatePersonOffer offer)
        {
            if (offer == null) return 0;
            if (offer.paid) return offer.paidAmount < 0 ? offer.price : offer.paidAmount;
            return WeeklyPersonAvailable ? 0 : offer.price;
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
                    pawn.mindState.canFleeIndividual = false;
                    CorporateUniforms.EnsureRequiredApparel(pawn);
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
            Scribe_Values.Look(ref giftWeek, "corporateGiftWeek", -1);
            Scribe_Values.Look(ref freePersonWeek, "corporateFreePersonWeek", -1);
            Scribe_Values.Look(ref supportWeek, "corporateSupportWeek", -1);
            Scribe_Values.Look(ref supportCallsUsed, "corporateSupportCallsUsed", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tradeTurnover = Math.Max(0L, tradeTurnover);
                // Previous versions stored only the week of a single successful call.
                if (supportCallsUsed < 0) supportCallsUsed = supportWeek >= 0 ? 1 : 0;
            }
        }
    }

    public sealed class LordJob_CorporateSupport : LordJob
    {
        private IntVec3 fallback;
        public LordJob_CorporateSupport() { }
        public LordJob_CorporateSupport(IntVec3 fallback) { this.fallback = fallback; }
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var fight = new LordToil_HuntEnemies(fallback);
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
        public override void ExposeData() { Scribe_Values.Look(ref fallback, "fallback"); }
    }

    // Vanilla caravan/settlement/orbital deals also contribute both gross directions.
    // Capture before TryExecute resets its tradeables; silver itself and gifts are excluded.
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    internal static class Harmony_CorporateTradeTurnover
    {
        internal static void Prefix(TradeDeal __instance, out long __state)
        {
            __state = 0;
            if (TradeSession.giftMode || TradeSession.trader?.Faction?.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile) return;
            double value = __instance.AllTradeables.Where(t => !t.IsCurrency && t.ActionToDo != TradeAction.None)
                .Sum(t => Math.Abs((double)t.CurTotalCurrencyCostForSource));
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
                __state = (long)Math.Min(1000000000d, Math.Floor(value));
        }
        internal static void Postfix(bool __result, bool actuallyTraded, long __state)
        {
            if (__result && actuallyTraded) CorporateNetwork.Current?.AddTradeTurnover(__state);
        }
    }
}
