using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public sealed class CorporatePeopleConfigDef : Def
    {
        public int weeklyCount = 4;
        public float basePriceMultiplier = 1.5f;
        public float colonistPriceMultiplier = 1.5f;
        public float handlingFeeFactor = 0.05f;
        public float shippingFeeFactor = 0.05f;
    }

    public struct CorporatePersonQuote
    {
        public int basePrice;
        public int colonistPremium;
        public int handlingFee;
        public int shippingFee;
        public int total;
    }

    [DefOf]
    public static class CorporatePeopleDefOf
    {
        public static CorporatePeopleConfigDef Mugirl_CorporatePeople;
        public static ThoughtDef Mugirl_CorporateSoldMugirl;
        public static TraderKindDef Mugirl_CorporatePersonnelTrader;
        static CorporatePeopleDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CorporatePeopleDefOf)); }
    }

    public sealed class CorporatePersonOffer : IExposable
    {
        public int id;
        public Pawn pawn;
        public int price;
        public bool paid;
        public bool delivered;
        public int paidAmount = -1;
        public bool buyAsColonist;
        public bool transferApplied;
        public int priceVersion = 1;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref price, "price", 0);
            Scribe_Values.Look(ref paid, "paid", false);
            Scribe_Values.Look(ref delivered, "delivered", false);
            Scribe_Values.Look(ref paidAmount, "paidAmount", -1);
            Scribe_Values.Look(ref buyAsColonist, "buyAsColonist", false);
            Scribe_Values.Look(ref transferApplied, "transferApplied", false);
            Scribe_Values.Look(ref priceVersion, "priceVersion", 0);
        }
    }

    public partial class CorporateNetwork
    {
        private List<CorporatePersonOffer> peopleOffers = new List<CorporatePersonOffer>();
        private List<Pawn> peopleAwaitingWorld = new List<Pawn>();
        private int peoplePendingSilver;
        private int peopleNextCleanupTick;
        public IReadOnlyList<CorporatePersonOffer> PeopleOffers => peopleOffers;
        public int PeoplePendingSilver => peoplePendingSilver;

        partial void PeopleExposeData()
        {
            Scribe_Collections.Look(ref peopleOffers, "corporatePeopleOffers", LookMode.Deep);
            Scribe_Collections.Look(ref peopleAwaitingWorld, "corporatePeopleAwaitingWorld", LookMode.Reference);
            Scribe_Values.Look(ref peoplePendingSilver, "corporatePeoplePendingSilver", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (peopleOffers == null) peopleOffers = new List<CorporatePersonOffer>();
                if (peopleAwaitingWorld == null) peopleAwaitingWorld = new List<Pawn>();
                foreach (CorporatePersonOffer offer in peopleOffers.ToList())
                {
                    if (offer.pawn == null || offer.pawn.Destroyed || offer.pawn.Dead)
                    {
                        if (offer.paid && !offer.delivered) peoplePendingSilver += offer.paidAmount < 0 ? offer.price : offer.paidAmount;
                        peopleOffers.Remove(offer);
                        continue;
                    }
                    // 未付款的旧混搭可换成完整主题套装；已付款订单保留签约时的衣装。
                    if (!offer.paid && offer.pawn.kindDef == MugirlContentDefOf.Mugirl_CorporateShowcase
                        && !CorporateShowcaseApparel.IsCompleteOutfit(offer.pawn)
                        && CorporateShowcaseApparel.TryEnsureOutfit(offer.pawn))
                        offer.price = PeopleBasePrice(offer.pawn);
                    // 旧订单把 ×2 买价存入 price；已付款额仍按旧收据保留。
                    if (offer.priceVersion == 0)
                    {
                        if (offer.paid && offer.paidAmount < 0) offer.paidAmount = offer.price;
                        offer.price = PeopleBasePrice(offer.pawn);
                        offer.priceVersion = 1;
                    }
                }
                peopleAwaitingWorld.RemoveAll(p => p == null || p.Destroyed);
                peopleNextCleanupTick = 0;
            }
        }

        partial void PeopleTick()
        {
            if (Now < peopleNextCleanupTick || peopleAwaitingWorld.Count == 0) return;
            peopleNextCleanupTick = Now + DayTicks;
            foreach (Pawn pawn in peopleAwaitingWorld.ToList())
            {
                if (pawn == null || pawn.Destroyed) { peopleAwaitingWorld.Remove(pawn); continue; }
                Vault.Remove(pawn);
                if (MugirlGeneratedPawnUtility.TryPassToWorld(pawn)) peopleAwaitingWorld.Remove(pawn);
                else if (!pawn.Spawned && pawn.holdingOwner == null) Vault.TryAdd(pawn, false);
            }
        }

        partial void PeopleRefreshWeekly()
        {
            foreach (CorporatePersonOffer offer in peopleOffers.ToList())
            {
                if (offer.paid && !offer.delivered) continue;
                if (!offer.delivered && offer.pawn != null && !offer.pawn.Destroyed)
                    peopleAwaitingWorld.Add(offer.pawn);
                peopleOffers.Remove(offer);
            }
            if (CorporateFaction == null || CorporateFaction.defeated) return;
            for (int i = 0; i < CorporatePeopleDefOf.Mugirl_CorporatePeople.weeklyCount; i++)
            {
                Pawn pawn = null;
                try
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(MugirlContentDefOf.Mugirl_CorporateShowcase,
                        faction: CorporateFaction, context: PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                        canGeneratePawnRelations: false, allowPregnant: false,
                        developmentalStages: DevelopmentalStage.Adult);
                    pawn = PawnGenerator.GeneratePawn(request);
                    if (pawn == null || pawn.guest == null || pawn.DevelopmentalStage != DevelopmentalStage.Adult)
                    {
                        MugirlGeneratedPawnUtility.Discard(pawn);
                        continue;
                    }
                    if (!CorporateShowcaseApparel.TryDress(pawn))
                    {
                        MugirlGeneratedPawnUtility.Discard(pawn);
                        continue;
                    }
                    pawn.guest.joinStatus = ModsConfig.IdeologyActive ? JoinStatus.JoinAsSlave : JoinStatus.JoinAsColonist;
                    if (pawn.IsWorldPawn()) MugirlGameUtility.WorldPawns.RemovePawn(pawn);
                    if (!Vault.TryAdd(pawn, false)) { MugirlGeneratedPawnUtility.Discard(pawn); continue; }
                    peopleOffers.Add(new CorporatePersonOffer
                    {
                        id = NewId(), pawn = pawn,
                        price = PeopleBasePrice(pawn)
                    });
                }
                catch (Exception ex)
                {
                    if (pawn != null && pawn.holdingOwner == null) MugirlGeneratedPawnUtility.Discard(pawn);
                    MugirlLog.WarningOnce("Corporate.PeopleGeneration", "Mugirl.CorporatePeople.GenerationError".Translate(ex.Message));
                }
            }
        }

        public IEnumerable<Pawn> PeopleSaleCandidates(CorporateTradeContext context)
        {
            if (context == null || !context.IsValid) return Enumerable.Empty<Pawn>();
            return context.AvailablePawns.Where(IsCorporateSalePawn).ToList();
        }

        private static bool IsCorporateSalePawn(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.InMentalState
                && MugirlIdentity.IsMugirlPawn(pawn) && pawn.DevelopmentalStage == DevelopmentalStage.Adult
                && !pawn.IsQuestLodger() && pawn.questTags.NullOrEmpty()
                && (pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony);
        }

        public int PeopleBasePrice(Pawn pawn)
        {
            return pawn == null ? 0 : Mathf.Max(1, Price(pawn.MarketValue *
                Mathf.Max(0f, CorporatePeopleDefOf.Mugirl_CorporatePeople.basePriceMultiplier), 1));
        }

        private static int PeopleFee(int basePrice, float factor)
            => basePrice <= 0 ? 0 : Math.Max(0, Price(basePrice * Mathf.Max(0f, factor), 1));

        public CorporatePersonQuote PeoplePurchaseQuote(CorporatePersonOffer offer, bool asColonist)
        {
            if (offer == null || offer.pawn == null) return default(CorporatePersonQuote);
            CorporatePeopleConfigDef config = CorporatePeopleDefOf.Mugirl_CorporatePeople;
            int basePrice = offer.price;
            int premium = asColonist ? PeopleFee(basePrice, Mathf.Max(0f, config.colonistPriceMultiplier - 1f)) : 0;
            int handling = PeopleFee(basePrice, config.handlingFeeFactor);
            int shipping = PeopleFee(basePrice, config.shippingFeeFactor);
            return new CorporatePersonQuote
            {
                basePrice = basePrice, colonistPremium = premium, handlingFee = handling, shippingFee = shipping,
                total = (int)Math.Min(int.MaxValue, (long)basePrice + premium + handling + shipping)
            };
        }

        public CorporatePersonQuote PeopleSaleQuote(Pawn pawn)
        {
            int basePrice = PeopleBasePrice(pawn);
            if (basePrice <= 0) return default(CorporatePersonQuote);
            CorporatePeopleConfigDef config = CorporatePeopleDefOf.Mugirl_CorporatePeople;
            int handling = PeopleFee(basePrice, config.handlingFeeFactor);
            int shipping = PeopleFee(basePrice, config.shippingFeeFactor);
            return new CorporatePersonQuote
            {
                basePrice = basePrice, handlingFee = handling, shippingFee = shipping,
                total = (int)Math.Max(1L, (long)basePrice - handling - shipping)
            };
        }

        public int PeopleSalePrice(Pawn pawn) => PeopleSaleQuote(pawn).total;

        private Pawn PeopleNegotiator(CorporateTradeContext context)
        {
            if (context.Negotiator != null)
            {
                Pawn selected = context.Negotiator;
                return !selected.Dead && !selected.Downed && !selected.InMentalState && selected.IsColonist
                    && !selected.IsQuestLodger() ? selected : null;
            }
            IEnumerable<Pawn> pawns = context.Map != null ? context.Map.mapPawns.FreeColonistsSpawned
                : context.Caravan.PawnsListForReading.Where(p => p.IsColonist);
            return pawns.Where(p => !p.Dead && !p.Downed && !p.InMentalState && !p.IsQuestLodger())
                .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).FirstOrDefault();
        }

        public bool TryPurchasePerson(CorporateTradeContext context, CorporatePersonOffer offer, out string reason)
            => TryPurchasePerson(context, offer, false, out reason);

        public bool TryPurchasePerson(CorporateTradeContext context, CorporatePersonOffer offer, bool asColonist, out string reason)
        {
            EnsureWeeklyOffers();
            if (offer == null || !peopleOffers.Contains(offer) || offer.delivered || offer.pawn == null
                || offer.pawn.Dead || offer.pawn.Destroyed)
                return PeopleReject("Mugirl.CorporatePeople.OfferChanged", out reason);
            if (offer.paid) asColonist = offer.buyAsColonist;
            if (!asColonist && !ModsConfig.IdeologyActive)
                return PeopleReject("Mugirl.CorporatePeople.RequiresIdeology", out reason);
            if (!Unlocked || context == null || !context.IsValid)
                return PeopleReject("Mugirl.CorporatePeople.InvalidContext", out reason);
            Pawn negotiator = PeopleNegotiator(context);
            if (negotiator == null) return PeopleReject("Mugirl.CorporatePeople.NoNegotiator", out reason);
            if (!offer.paid)
            {
                if (!CanTrade(context, out reason)) return false;
                int cost = PeoplePurchasePrice(offer, asColonist);
                if (!context.TrySpendSilver(cost)) return PeopleReject("Mugirl.CorporatePeople.NoSilver", out reason);
                offer.paidAmount = cost;
                offer.buyAsColonist = asColonist;
                if (cost == 0) freePersonWeek = nextRefreshTick;
                offer.paid = true;
                Record("Mugirl.CorporatePeople.Purchased", offer.pawn.LabelShortCap, -cost);
            }
            Pawn pawn = offer.pawn;
            if (!offer.transferApplied)
            {
                // 旧档可能在付款后已完成 PreTraded，却因空投失败待重新交付。
                if (!MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction)
                    || (asColonist ? !pawn.IsColonist : !pawn.IsSlaveOfColony))
                {
                    pawn.guest.joinStatus = asColonist ? JoinStatus.JoinAsColonist : JoinStatus.JoinAsSlave;
                    pawn.PreTraded(TradeAction.PlayerBuys, negotiator, new CorporatePersonnelTrader(this));
                }
                offer.transferApplied = true;
            }
            if (!context.Deliver(pawn)) return PeopleReject("Mugirl.CorporatePeople.DeliveryPending", out reason);
            offer.delivered = true;
            CreditPersonTurnover(pawn, offer.paidAmount < 0 ? offer.price : offer.paidAmount);
            reason = null;
            return true;
        }

        public bool TrySellPerson(CorporateTradeContext context, Pawn pawn, int acceptedPrice, out string reason)
        {
            if (!CanTrade(context, out reason)) return false;
            if (!PeopleSaleCandidates(context).Contains(pawn) || PeopleSalePrice(pawn) != acceptedPrice)
                return PeopleReject("Mugirl.CorporatePeople.OfferChanged", out reason);
            Pawn negotiator = PeopleNegotiator(context);
            if (negotiator == null) return PeopleReject("Mugirl.CorporatePeople.NoNegotiator", out reason);
            HistoryEvent slavery = new HistoryEvent(HistoryEventDefOf.SoldSlave, negotiator.Named(HistoryEventArgsNames.Doer));
            if (!slavery.Notify_PawnAboutToDo()) return PeopleReject("Mugirl.CorporatePeople.IdeoRefusal", out reason);
            Comp_MugirlMount mount = MountedPawnUtility.GetMountComp(pawn);
            if (mount?.HasMountedPawn == true && !mount.TryDismount(sendMessage: false))
                return PeopleReject("Mugirl.CorporatePeople.DismountFirst", out reason);
            if (MountedPawnUtility.GetMountForRider(pawn) != null)
                return PeopleReject("Mugirl.CorporatePeople.DismountFirst", out reason);
            RopingService.BreakAllRopesAndNotify(pawn);
            // Map sales retain vanilla dropped equipment/inventory, including weapon-wheel hooks.
            // Caravan sales transfer carried gear with the pawn, as disclosed in the confirmation.
            pawn.PreTraded(TradeAction.PlayerSells, negotiator, new CorporatePersonnelTrader(this));
            if (pawn.Spawned) pawn.DeSpawn();
            else if (context.Caravan != null) context.Caravan.RemovePawn(pawn);
            pawn.holdingOwner?.Remove(pawn);
            Vault.TryAdd(pawn, false);
            peopleAwaitingWorld.Add(pawn);
            pawn.SetFaction(CorporateFaction);
            MugirlGameUtility.HistoryEvents.RecordEvent(slavery);
            ThoughtDef memory = CorporatePeopleDefOf.Mugirl_CorporateSoldMugirl;
            foreach (Pawn member in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
            {
                if (member == pawn || member.IsQuestLodger() || member.needs?.mood == null) continue;
                member.needs.mood.thoughts.memories.RemoveMemoriesOfDef(memory);
                member.needs.mood.thoughts.memories.TryGainMemory(memory);
            }
            peoplePendingSilver += acceptedPrice;
            CreditPersonTurnover(pawn, acceptedPrice);
            Record("Mugirl.CorporatePeople.Sold", pawn.LabelShortCap, acceptedPrice);
            TryClaimPeopleSilver(context, out reason);
            reason = null;
            return true;
        }

        public bool TryClaimPeopleSilver(CorporateTradeContext context, out string reason)
        {
            if (!Unlocked || context == null || !context.IsValid)
                return PeopleReject("Mugirl.CorporatePeople.InvalidContext", out reason);
            if (peoplePendingSilver > 0)
            {
                if (!context.DeliverSilver(peoplePendingSilver)) return PeopleReject("Mugirl.CorporatePeople.DeliveryPending", out reason);
                peoplePendingSilver = 0;
            }
            reason = null;
            return true;
        }

        private static bool PeopleReject(string key, out string reason) { reason = key.Translate(); return false; }
    }

    // Only used by the vanilla PreTraded hooks/tales; the terminal owns the transaction itself.
    internal sealed class CorporatePersonnelTrader : ITrader
    {
        private readonly CorporateNetwork network;
        public CorporatePersonnelTrader(CorporateNetwork network) { this.network = network; }
        public TraderKindDef TraderKind => CorporatePeopleDefOf.Mugirl_CorporatePersonnelTrader;
        public IEnumerable<Thing> Goods => network.Vault;
        public int RandomPriceFactorSeed => 0;
        public string TraderName => "Mugirl.CorporatePeople.TraderName".Translate();
        public bool CanTradeNow => true;
        public float TradePriceImprovementOffsetForPlayer => 0f;
        public Faction Faction => network.CorporateFaction;
        public TradeCurrency TradeCurrency => TradeCurrency.Silver;
        public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator) => Enumerable.Empty<Thing>();
        public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            throw new InvalidOperationException("Corporate personnel transfers must use the validated terminal transaction.");
        }
        public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            throw new InvalidOperationException("Corporate personnel transfers must use the validated terminal transaction.");
        }
    }
}
