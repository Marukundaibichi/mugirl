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
        public float purchaseMultiplier = 2f;
        public float saleMultiplier = 1.5f;
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
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref price, "price", 0);
            Scribe_Values.Look(ref paid, "paid", false);
            Scribe_Values.Look(ref delivered, "delivered", false);
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
                        if (offer.paid && !offer.delivered) peoplePendingSilver += offer.price;
                        peopleOffers.Remove(offer);
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
            // A disabled DLC never silently changes purchases into free colonists.
            if (!ModsConfig.IdeologyActive || CorporateFaction == null || CorporateFaction.defeated) return;
            CorporatePeopleConfigDef config = CorporatePeopleDefOf.Mugirl_CorporatePeople;
            for (int i = 0; i < config.weeklyCount; i++)
            {
                Pawn pawn = null;
                try
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(Mugirl_DefOf.Mugirl_Slave,
                        faction: null, context: PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                        canGeneratePawnRelations: false, allowPregnant: false,
                        developmentalStages: DevelopmentalStage.Adult);
                    pawn = PawnGenerator.GeneratePawn(request);
                    if (pawn == null || pawn.guest == null || pawn.DevelopmentalStage != DevelopmentalStage.Adult)
                    {
                        MugirlGeneratedPawnUtility.Discard(pawn);
                        continue;
                    }
                    MugirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);
                    pawn.guest.joinStatus = JoinStatus.JoinAsSlave;
                    if (pawn.IsWorldPawn()) MugirlGameUtility.WorldPawns.RemovePawn(pawn);
                    if (!Vault.TryAdd(pawn, false)) { MugirlGeneratedPawnUtility.Discard(pawn); continue; }
                    peopleOffers.Add(new CorporatePersonOffer
                    {
                        id = NewId(), pawn = pawn,
                        price = Mathf.Max(1, Mathf.CeilToInt(pawn.MarketValue *
                            Mathf.Max(config.purchaseMultiplier, config.saleMultiplier + 0.1f)))
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

        public int PeopleSalePrice(Pawn pawn)
        {
            return pawn == null ? 0 : Mathf.Max(1, Mathf.FloorToInt(pawn.MarketValue * CorporatePeopleDefOf.Mugirl_CorporatePeople.saleMultiplier));
        }

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
        {
            if (!ModsConfig.IdeologyActive) return PeopleReject("Mugirl.CorporatePeople.RequiresIdeology", out reason);
            if (offer == null || !peopleOffers.Contains(offer) || offer.delivered || offer.pawn == null
                || offer.pawn.Dead || offer.pawn.Destroyed)
                return PeopleReject("Mugirl.CorporatePeople.OfferChanged", out reason);
            if (!Unlocked || context == null || !context.IsValid)
                return PeopleReject("Mugirl.CorporatePeople.InvalidContext", out reason);
            Pawn negotiator = PeopleNegotiator(context);
            if (negotiator == null) return PeopleReject("Mugirl.CorporatePeople.NoNegotiator", out reason);
            if (!offer.paid)
            {
                if (!CanTrade(context, out reason)) return false;
                if (!context.TrySpendSilver(offer.price)) return PeopleReject("Mugirl.CorporatePeople.NoSilver", out reason);
                offer.paid = true;
                Record("Mugirl.CorporatePeople.Purchased", offer.pawn.LabelShortCap, -offer.price);
            }
            Pawn pawn = offer.pawn;
            if (!pawn.IsSlaveOfColony)
            {
                pawn.guest.joinStatus = JoinStatus.JoinAsSlave;
                pawn.PreTraded(TradeAction.PlayerBuys, negotiator, new CorporatePersonnelTrader(this));
            }
            if (!context.Deliver(pawn)) return PeopleReject("Mugirl.CorporatePeople.DeliveryPending", out reason);
            offer.delivered = true;
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
