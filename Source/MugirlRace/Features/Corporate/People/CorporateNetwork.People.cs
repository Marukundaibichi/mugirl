using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mugirl.Features.Mounting;
using Mugirl.Features.Roping;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 每周展示人员的报价、买入与卖出；卖出的雪牛娘先由 Vault 托管，次日转入世界 Pawn。
public partial class CorporateNetwork
{
    private bool peopleRead;

    public IReadOnlyList<CorporatePersonOffer> PeopleOffers => people.offers;
    public int PeoplePendingSilver => people.pendingSilver;

    private void PeopleExposeData()
    {
        CorporateStateScribe.Look(people, "people", ref peopleRead);
    }

    // 读档完成后执行（LoadedGame）：生成衣物与读取市值都需要 Pawn 已完成 PostLoadInit。
    private void RefreshPeopleOffers()
    {
        foreach (CorporatePersonOffer offer in people.offers)
        {
            if (offer.pawn is not { } pawn)
            {
                continue;
            }

            // 未付款的展示人员可换成完整主题套装；已付款订单保留签约时的衣装。
            if (!offer.paid && pawn.kindDef == CorporateDefOf.Mugirl_CorporateShowcase
                && !CorporateShowcaseApparel.IsCompleteOutfit(pawn)
                && CorporateShowcaseApparel.TryEnsureOutfit(pawn))
            {
                offer.price = PeopleBasePrice(pawn);
            }

            offer.Complete(this);
        }
    }

    private void PeopleTick()
    {
        int now = Now;
        if (now < people.nextCleanupTick || people.awaitingWorld.Count == 0)
        {
            return;
        }

        people.nextCleanupTick = now + DayTicks;
        people.awaitingWorld.RemoveAll(p => p == null || p.Destroyed);
        foreach (Pawn pawn in new List<Pawn>(people.awaitingWorld))
        {
            Vault.Remove(pawn);
            if (GeneratedPawnUtility.TryPassToWorld(pawn))
            {
                people.awaitingWorld.Remove(pawn);
            }
            else if (!pawn.Spawned && pawn.holdingOwner == null)
            {
                Vault.TryAdd(pawn, false);
            }
        }
    }

    private void PeopleRefreshWeekly()
    {
        foreach (CorporatePersonOffer offer in new List<CorporatePersonOffer>(people.offers))
        {
            if (offer.paid && !offer.delivered)
            {
                continue;
            }

            if (!offer.delivered && offer.pawn != null && !offer.pawn.Destroyed)
            {
                people.awaitingWorld.Add(offer.pawn);
            }

            people.offers.Remove(offer);
        }

        Faction? corporation = CorporateFaction;
        if (corporation == null || corporation.defeated)
        {
            return;
        }

        for (int i = 0; i < CorporateDefOf.Mugirl_CorporatePeopleConfig.weeklyCount; i++)
        {
            Pawn? pawn = null;
            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(CorporateDefOf.Mugirl_CorporateShowcase,
                    faction: corporation, context: PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                    canGeneratePawnRelations: false, allowPregnant: false,
                    developmentalStages: DevelopmentalStage.Adult);
                pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null || pawn.guest == null || pawn.DevelopmentalStage != DevelopmentalStage.Adult
                    || !CorporateShowcaseApparel.TryDress(pawn))
                {
                    DiscardGeneratedPawn(pawn);
                    continue;
                }

                pawn.guest.joinStatus = ModsConfig.IdeologyActive ? JoinStatus.JoinAsSlave : JoinStatus.JoinAsColonist;
                if (pawn.IsWorldPawn())
                {
                    GameUtility.WorldPawns.RemovePawn(pawn);
                }

                if (!Vault.TryAdd(pawn, false))
                {
                    DiscardGeneratedPawn(pawn);
                    continue;
                }

                people.offers.Add(new CorporatePersonOffer
                {
                    id = NewId(),
                    pawn = pawn,
                    price = PeopleBasePrice(pawn),
                });
            }
            catch (Exception ex)
            {
                if (pawn != null && pawn.holdingOwner == null)
                {
                    DiscardGeneratedPawn(pawn);
                }

                MugirlLog.WarningOnce("Corporate.PeopleGeneration", "Mugirl.CorporatePeople.GenerationError".Translate(ex.Message));
            }
        }
    }

    private static void DiscardGeneratedPawn(Pawn? pawn)
    {
        if (pawn != null)
        {
            GeneratedPawnUtility.Discard(pawn);
        }
    }

    public IEnumerable<Pawn> PeopleSaleCandidates(CorporateTradeContext? context)
    {
        if (context == null || !context.IsValid)
        {
            return Enumerable.Empty<Pawn>();
        }

        return context.AvailablePawns.Where(IsCorporateSalePawn).ToList();
    }

    private static bool IsCorporateSalePawn(Pawn pawn)
    {
        return pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.InMentalState
            && MugirlIdentity.IsMugirlPawn(pawn) && pawn.DevelopmentalStage == DevelopmentalStage.Adult
            && !pawn.IsQuestLodger() && pawn.questTags.NullOrEmpty()
            && (pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony);
    }

    public int PeopleBasePrice(Pawn? pawn)
    {
        return pawn == null ? 0 : Math.Max(1, Price(pawn.MarketValue
            * UnityMathf.Max(0f, CorporateDefOf.Mugirl_CorporatePeopleConfig.basePriceMultiplier), 1));
    }

    private static int PeopleFee(int basePrice, float factor)
        => basePrice <= 0 ? 0 : Math.Max(0, Price(basePrice * UnityMathf.Max(0f, factor), 1));

    public CorporatePersonQuote PeoplePurchaseQuote(CorporatePersonOffer? offer, bool asColonist)
    {
        if (offer?.pawn == null)
        {
            return default;
        }

        CorporatePeopleConfigDef config = CorporateDefOf.Mugirl_CorporatePeopleConfig;
        int basePrice = offer.price;
        int premium = asColonist ? PeopleFee(basePrice, UnityMathf.Max(0f, config.colonistPriceMultiplier - 1f)) : 0;
        int handling = PeopleFee(basePrice, config.handlingFeeFactor);
        int shipping = PeopleFee(basePrice, config.shippingFeeFactor);
        return new CorporatePersonQuote
        {
            basePrice = basePrice,
            colonistPremium = premium,
            handlingFee = handling,
            shippingFee = shipping,
            total = (int)Math.Min(int.MaxValue, (long)basePrice + premium + handling + shipping),
        };
    }

    public CorporatePersonQuote PeopleSaleQuote(Pawn? pawn)
    {
        int basePrice = PeopleBasePrice(pawn);
        if (basePrice <= 0)
        {
            return default;
        }

        CorporatePeopleConfigDef config = CorporateDefOf.Mugirl_CorporatePeopleConfig;
        int handling = PeopleFee(basePrice, config.handlingFeeFactor);
        int shipping = PeopleFee(basePrice, config.shippingFeeFactor);
        return new CorporatePersonQuote
        {
            basePrice = basePrice,
            handlingFee = handling,
            shippingFee = shipping,
            total = (int)Math.Max(1L, (long)basePrice - handling - shipping),
        };
    }

    public int PeopleSalePrice(Pawn? pawn) => PeopleSaleQuote(pawn).total;

    private static Pawn? PeopleNegotiator(CorporateTradeContext context)
    {
        if (context.Negotiator is { } selected)
        {
            return !selected.Dead && !selected.Downed && !selected.InMentalState && selected.IsColonist
                && !selected.IsQuestLodger() ? selected : null;
        }

        IEnumerable<Pawn> pawns = context.Map != null ? context.Map.mapPawns.FreeColonistsSpawned
            : context.Caravan?.PawnsListForReading.Where(p => p.IsColonist) ?? Enumerable.Empty<Pawn>();
        return pawns.Where(p => !p.Dead && !p.Downed && !p.InMentalState && !p.IsQuestLodger())
            .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).FirstOrDefault();
    }

    public bool TryPurchasePerson(CorporateTradeContext? context, CorporatePersonOffer? offer, bool asColonist, [NotNullWhen(false)] out string? reason)
    {
        EnsureWeeklyOffers();
        if (offer == null || !people.offers.Contains(offer) || offer.delivered || offer.pawn is not { } pawn
            || pawn.Dead || pawn.Destroyed)
        {
            return PeopleReject("Mugirl.CorporatePeople.OfferChanged", out reason);
        }

        if (offer.paid)
        {
            asColonist = offer.buyAsColonist;
        }

        if (!asColonist && !ModsConfig.IdeologyActive)
        {
            return PeopleReject("Mugirl.CorporatePeople.RequiresIdeology", out reason);
        }

        if (!Unlocked || context == null || !context.IsValid)
        {
            return PeopleReject("Mugirl.CorporatePeople.InvalidContext", out reason);
        }

        Pawn? negotiator = PeopleNegotiator(context);
        if (negotiator == null)
        {
            return PeopleReject("Mugirl.CorporatePeople.NoNegotiator", out reason);
        }

        if (!offer.paid)
        {
            if (!CanTrade(context, out reason))
            {
                return false;
            }

            int cost = PeoplePurchasePrice(offer, asColonist);
            if (!context.TrySpendSilver(cost))
            {
                return PeopleReject("Mugirl.CorporatePeople.NoSilver", out reason);
            }

            offer.paidAmount = cost;
            offer.buyAsColonist = asColonist;
            if (cost == 0)
            {
                services.freePersonWeek = nextRefreshTick;
            }

            offer.paid = true;
            Record("Mugirl.CorporatePeople.Purchased", pawn.LabelShortCap, -cost);
        }

        if (!offer.transferApplied)
        {
            // 交付重试前确认当前身份，避免重复触发交易与招募副作用。
            if (!PlayerFactionUtility.IsPlayerFaction(pawn.Faction)
                || (asColonist ? !pawn.IsColonist : !pawn.IsSlaveOfColony))
            {
                pawn.guest.joinStatus = asColonist ? JoinStatus.JoinAsColonist : JoinStatus.JoinAsSlave;
                pawn.PreTraded(TradeAction.PlayerBuys, negotiator, new CorporatePersonnelTrader(this));
            }

            offer.transferApplied = true;
        }

        if (!context.Deliver(pawn))
        {
            return PeopleReject("Mugirl.CorporatePeople.DeliveryPending", out reason);
        }

        offer.delivered = true;
        CreditPersonTurnover(pawn, offer.paidAmount);
        reason = null;
        return true;
    }

    public bool TrySellPerson(CorporateTradeContext? context, Pawn pawn, int acceptedPrice, [NotNullWhen(false)] out string? reason)
    {
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        if (!PeopleSaleCandidates(context).Contains(pawn) || PeopleSalePrice(pawn) != acceptedPrice)
        {
            return PeopleReject("Mugirl.CorporatePeople.OfferChanged", out reason);
        }

        Pawn? negotiator = PeopleNegotiator(context);
        if (negotiator == null)
        {
            return PeopleReject("Mugirl.CorporatePeople.NoNegotiator", out reason);
        }

        HistoryEvent slavery = new HistoryEvent(HistoryEventDefOf.SoldSlave, negotiator.Named(HistoryEventArgsNames.Doer));
        if (!slavery.Notify_PawnAboutToDo())
        {
            return PeopleReject("Mugirl.CorporatePeople.IdeoRefusal", out reason);
        }

        CompMount? mount = MountUtility.GetMountComp(pawn);
        if (mount?.HasMountedPawn == true && !mount.TryDismount(sendMessage: false))
        {
            return PeopleReject("Mugirl.CorporatePeople.DismountFirst", out reason);
        }

        if (MountUtility.GetMountForRider(pawn) != null)
        {
            return PeopleReject("Mugirl.CorporatePeople.DismountFirst", out reason);
        }

        RopingService.BreakAllRopesAndNotify(pawn);
        // 地图出售沿用原版的装备/物品掉落（含武器轮盘钩子）；商队出售时携带物随人员一同转移，确认框中已说明。
        pawn.PreTraded(TradeAction.PlayerSells, negotiator, new CorporatePersonnelTrader(this));
        if (pawn.Spawned)
        {
            pawn.DeSpawn();
        }
        else
        {
            context.Caravan?.RemovePawn(pawn);
        }

        pawn.holdingOwner?.Remove(pawn);
        Vault.TryAdd(pawn, false);
        people.awaitingWorld.Add(pawn);
        pawn.SetFaction(CorporateFaction);
        GameUtility.HistoryEvents.RecordEvent(slavery);
        ThoughtDef memory = CorporateDefOf.Mugirl_CorporateSoldMugirl;
        foreach (Pawn member in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
        {
            if (member == pawn || member.IsQuestLodger() || member.needs?.mood == null)
            {
                continue;
            }

            member.needs.mood.thoughts.memories.RemoveMemoriesOfDef(memory);
            member.needs.mood.thoughts.memories.TryGainMemory(memory);
        }

        people.pendingSilver += acceptedPrice;
        CreditPersonTurnover(pawn, acceptedPrice);
        Record("Mugirl.CorporatePeople.Sold", pawn.LabelShortCap, acceptedPrice);
        TryClaimPeopleSilver(context, out _);
        reason = null;
        return true;
    }

    public bool TryClaimPeopleSilver(CorporateTradeContext? context, [NotNullWhen(false)] out string? reason)
    {
        if (!Unlocked || context == null || !context.IsValid)
        {
            return PeopleReject("Mugirl.CorporatePeople.InvalidContext", out reason);
        }

        if (people.pendingSilver > 0)
        {
            if (!context.DeliverSilver(people.pendingSilver))
            {
                return PeopleReject("Mugirl.CorporatePeople.DeliveryPending", out reason);
            }

            people.pendingSilver = 0;
        }

        reason = null;
        return true;
    }

    private static bool PeopleReject(string key, [NotNullWhen(false)] out string? reason)
    {
        reason = key.Translate();
        return false;
    }
}
