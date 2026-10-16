using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Verse.AI.Group;

namespace Mugirl.Features.Corporate;

// 成交额账本与客户服务等级：每周赠礼、每周免费人员与支援小队。
public partial class CorporateNetwork
{
    private bool servicesRead;

    // StaticCacheLifecycle: 进程级不可变平衡表，不含任何每局对象引用。
    private static readonly int[] ServiceThresholds = [10000, 25000, 50000, 100000, 200000, 400000];
    // StaticCacheLifecycle: 进程级不可变平衡表，按服务等级 0–6 索引。
    private static readonly float[] OrderDiscounts = [1f, 0.95f, 0.92f, 0.90f, 0.88f, 0.85f, 0.85f];
    // StaticCacheLifecycle: 进程级不可变平衡表，按服务等级 0–6 索引。
    private static readonly int[] ExtraStock = [0, 0, 8, 12, 16, 24, 24];
    // StaticCacheLifecycle: 进程级不可变表：服务等级 1–6 的权益说明键（下标为等级 - 1），与 ServiceThresholds 等长。
    private static readonly string[] ServiceBenefitKeys =
    [
        "Mugirl.CorporateServices.Tier1", "Mugirl.CorporateServices.Tier2", "Mugirl.CorporateServices.Tier3",
        "Mugirl.CorporateServices.Tier4", "Mugirl.CorporateServices.Tier5", "Mugirl.CorporateServices.Tier6",
    ];

    public long TradeTurnover => services.turnover;
    public int ServiceLevel => CorporateMoney.ServiceLevel(services.turnover, ServiceThresholds);
    public static int MaxServiceLevel => ServiceThresholds.Length;
    public static int ServiceThreshold(int level) => ServiceThresholds[level - 1];
    public static string ServiceBenefitKey(int level) => ServiceBenefitKeys[level - 1];
    public float OrderDiscountFactor => OrderDiscounts[ServiceLevel];
    public int WeeklyStockCount => TradeSettings.weeklyStockCount + ExtraStock[ServiceLevel];
    public bool WeeklyGiftAvailable => ServiceLevel >= 3 && nextRefreshTick > Now && services.giftWeek != nextRefreshTick;
    public bool WeeklyPersonAvailable => ServiceLevel >= 4 && nextRefreshTick > Now && services.freePersonWeek != nextRefreshTick;

    public int SupportSquadSize
    {
        get
        {
            int level = ServiceLevel;
            return level >= 6 ? 8 : level >= 5 ? 4 : 0;
        }
    }

    public int WeeklySupportLimit
    {
        get
        {
            int level = ServiceLevel;
            return level >= 6 ? 2 : level >= 5 ? 1 : 0;
        }
    }

    public int WeeklySupportCallsRemaining => nextRefreshTick <= Now ? 0
        : Math.Max(0, WeeklySupportLimit - (services.supportWeek == nextRefreshTick ? services.supportCallsUsed : 0));

    public bool WeeklySupportAvailable => WeeklySupportCallsRemaining > 0;

    internal void AddTradeTurnover(long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int previous = ServiceLevel;
        services.AddTurnover(amount);
        int level = ServiceLevel;
        if (level > previous)
        {
            Record("Mugirl.CorporateServices.Unlocked", level.ToString());
            Messages.Message("Mugirl.CorporateServices.LevelUp".Translate(level), MessageTypeDefOf.PositiveEvent, false);
        }
    }

    internal void CreditPersonTurnover(Pawn? pawn, long amount)
    {
        if (pawn == null || amount <= 0 || !services.TryCreditPerson(pawn.thingIDNumber))
        {
            return;
        }

        AddTradeTurnover(amount);
    }

    private void CreditOrderTurnover(CorporateOrder order)
    {
        if (order.quantity <= 0 || order.paid <= 0)
        {
            return;
        }

        int settled = (int)((long)order.paid * Math.Min(order.quantity, order.deliveredQuantity) / order.quantity);
        AddTradeTurnover(settled - order.creditedTurnover);
        order.creditedTurnover = Math.Max(order.creditedTurnover, settled);
    }

    public int PeoplePurchasePrice(CorporatePersonOffer? offer, bool asColonist = false)
    {
        if (offer == null)
        {
            return 0;
        }

        if (offer.paid)
        {
            return offer.paidAmount;
        }

        return WeeklyPersonAvailable ? 0 : PeoplePurchaseQuote(offer, asColonist).total;
    }

    public bool TryClaimWeeklyGift(CorporateTradeContext? context, [NotNullWhen(false)] out string? reason)
    {
        EnsureWeeklyOffers();
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        if (!WeeklyGiftAvailable)
        {
            reason = "Mugirl.CorporateServices.Unavailable".Translate();
            return false;
        }

        if (!TryMakeGoods(ThingDefOf.MedicineIndustrial, null, QualityCategory.Normal, 10, out List<Thing> medicine))
        {
            reason = "Mugirl.Corporate.CannotMake".Translate();
            return false;
        }

        if (!TryMakeGoods(ThingDefOf.MealSurvivalPack, null, QualityCategory.Normal, 20, out List<Thing> meals))
        {
            DiscardUnspawnedProducts(medicine);
            reason = "Mugirl.Corporate.CannotMake".Translate();
            return false;
        }

        // 每个包裹都有持久收据，暂时无法交付时也能在订单页领取。
        int now = Now;
        CorporateOrder first = SaveOrder(ThingDefOf.MedicineIndustrial, 10, 0, now, medicine);
        CorporateOrder second = SaveOrder(ThingDefOf.MealSurvivalPack, 20, 0, now, meals);
        first.state = second.state = CorporateOrderState.Ready;
        services.giftWeek = nextRefreshTick;
        ClaimOrder(context, first, out _);
        ClaimOrder(context, second, out _);
        Record("Mugirl.CorporateServices.GiftRecord", context.Label);
        reason = null;
        return true;
    }

    public static bool ValidSupportCell([NotNullWhen(true)] Map? map, IntVec3 cell)
    {
        return map != null && GameUtility.LoadedMaps?.Contains(map) == true && cell.InBounds(map)
            && cell.Standable(map) && !cell.Fogged(map) && !cell.Roofed(map);
    }

    public bool TryCallSupport(CorporateTradeContext? context, Map? map, IntVec3 cell, [NotNullWhen(false)] out string? reason)
    {
        EnsureWeeklyOffers();
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        if (!WeeklySupportAvailable)
        {
            reason = "Mugirl.CorporateServices.Unavailable".Translate();
            return false;
        }

        if (!ValidSupportCell(map, cell))
        {
            reason = "Mugirl.CorporateServices.InvalidTarget".Translate();
            return false;
        }

        Faction? corporation = CorporateFaction;
        var pawns = new List<Pawn>();
        try
        {
            PawnKindDef kind = CorporateDefOf.Mugirl_CorporateSupport;
            int squadSize = SupportSquadSize;
            for (int i = 0; i < squadSize; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, corporation,
                    PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true,
                    allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true, allowPregnant: false, developmentalStages: DevelopmentalStage.Adult));
                pawns.Add(pawn);
                CorporateDiehardUtility.MakeDiehard(pawn);
                // GeneratePawn 后缀已补齐制服；这里再确认一次（幂等），制服补丁未施加时支援队仍穿制服。
                CorporateUniforms.EnsureRequiredApparel(pawn);
                CorporateSupportUtility.EquipFieldSupplies(pawn);
            }
        }
        catch (Exception ex)
        {
            foreach (Pawn pawn in pawns)
            {
                GeneratedPawnUtility.Discard(pawn);
            }

            MugirlLog.WarningOnce("Corporate.SupportGeneration", "Corporate support generation failed: " + ex.Message);
            reason = "Mugirl.Corporate.CannotMake".Translate();
            return false;
        }

        // 单个空投舱精确落在选定的安全露天格子。
        var info = new ActiveTransporterInfo { openDelay = 110, leaveSlag = false };
        foreach (Pawn pawn in pawns)
        {
            info.innerContainer.TryAdd(pawn);
        }

        try
        {
            DropPodUtility.MakeDropPodAt(cell, map, info, corporation);
        }
        catch (Exception ex)
        {
            // 空投舱已生成时，即使后续模组回调抛错，舱内人员也归它所有。
            if (!pawns.Any(p => p.MapHeld == map))
            {
                foreach (Pawn pawn in pawns)
                {
                    pawn.holdingOwner?.Remove(pawn);
                    GeneratedPawnUtility.Discard(pawn);
                }

                reason = "Mugirl.Corporate.DeliveryUnavailable".Translate();
                return false;
            }

            MugirlLog.WarningOnce("Corporate.SupportDrop", ex.ToString());
        }

        services.supportCallsUsed = services.supportWeek == nextRefreshTick ? services.supportCallsUsed + 1 : 1;
        services.supportWeek = nextRefreshTick;
        LordMaker.MakeNewLord(corporation, new LordJob_CorporateSupport(cell), map, pawns);
        Record("Mugirl.CorporateServices.SupportRecord", map.Parent.LabelCap + " " + cell);
        reason = null;
        return true;
    }

    private void ServicesExposeData()
    {
        CorporateStateScribe.Look(services, "services", ref servicesRead);
    }
}
